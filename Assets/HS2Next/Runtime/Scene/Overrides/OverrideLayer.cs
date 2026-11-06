// The runtime override layer of one scene runtime (Unity-free core): slots that lay values over the scene model for
// display. A slot = (owner, node id or none, canonical path, priority, acquisition number) with at most one value. A
// channel = (node id or none, path); its winner is the slot that has a value with the highest priority, among equal
// priorities the one acquired last. An owner holds at most one slot per channel (Acquire of a channel it holds returns that
// slot with the new priority). The layer never writes the model: no command, change batch, history entry, version or dirty
// mark. Slots live until their owner releases them; the host releases an owner's slots (the Timeline at stop, a plugin when
// it leaves loaded), every slot when the scene is cleared (new, load: SceneEvents.Cleared) and a node's slots when the node
// is forgotten (NodeForgotten). A deleted node keeps its slots (TargetExists false) and shows them again after an undo.
// The host (the Unity application, IOverrideHost) is told when a channel's winner or winning value changed and applies it.
//   Version       changes when a slot is acquired or released, gains or loses its value, changes priority, or a winner
//                 changes: the inspector's mark refreshes on it (values that only change do not count)
//   ValueVersion  changes on every change, values included
//   ModelChanged  raised for every slot of a channel that a model change (not a save capture) touched: the core Timeline
//                 suspends its channel while it is paused
//   IsDriving     the owners' rule for the camera controller and gizmos (the core Timeline: only while it plays); DrivenBy
//   ValueBelow    what a channel would show without one slot: the value a weight blends with
using System;
using System.Collections.Generic;
using System.Threading;
using HS2Next.Api.Plugins;

namespace HS2Next.Scenes.Overrides
{
    /// <summary>What the layer needs from the scene runtime.</summary>
    public interface IOverrideHost
    {
        /// <summary>The kind of a node in the model or among the deleted nodes an undo can bring back; false when neither.</summary>
        bool TryKindOf(string nodeId, out NodeKind kind);
        /// <summary>True when the node is in the model (not deleted) and built.</summary>
        bool TargetExists(string nodeId);
        /// <summary>The channel's winner or its winning value changed (null winner: the model value shows again). Apply now.</summary>
        void Apply(OverrideChannelState channel);
        /// <summary>True when the winning slot takes effect now (an FK bone that the FK pose writes, a focus that resolves).</summary>
        bool IsEffective(OverrideSlot winner);
        /// <summary>The node ids of the model in pre-order (the order of listings).</summary>
        IEnumerable<string> NodeOrder();
    }

    /// <summary>One channel: its slots and its winner.</summary>
    public sealed class OverrideChannelState
    {
        internal readonly List<OverrideSlot> Slots = new List<OverrideSlot>(2);

        internal OverrideChannelState(string nodeId, OverridePath path)
        {
            NodeId = nodeId;
            Path = path;
        }

        /// <summary>The node id; null for the scene channels.</summary>
        public string NodeId { get; }
        public OverridePath Path { get; }
        /// <summary>The winning slot (a value, the highest priority, then the latest acquisition), or null.</summary>
        public OverrideSlot Winner { get; private set; }
        /// <summary>Host data (the application's binding, e.g. an FK bone), kept with the channel.</summary>
        public object Tag;
        public int Count => Slots.Count;
        public OverrideSlot this[int i] => Slots[i];

        /// <summary>Recomputes the winner; true when it changed.</summary>
        internal bool Elect()
        {
            OverrideSlot w = null;
            for (int i = 0; i < Slots.Count; i++)
            {
                var s = Slots[i];
                if (!s.HasValue) continue;
                if (w == null || s.Priority > w.Priority || (s.Priority == w.Priority && s.Acquisition > w.Acquisition)) w = s;
            }
            if (w == Winner) return false;
            Winner = w;
            return true;
        }

        public override string ToString() => (NodeId ?? "scene") + " " + Path.Text + " (" + Slots.Count + " slots" + (Winner != null ? ", " + Winner.Owner + " wins" : "") + ")";
    }

    public sealed class OverrideLayer
    {
        /// <summary>The owner of the core Timeline's slots.</summary>
        public const string TimelineOwner = "timeline";
        /// <summary>The owner of the scene's constraints.</summary>
        public const string ConstraintsOwner = "constraints";
        /// <summary>The priority of the constraints' slots: above the Timeline's and a plugin's default.</summary>
        public const int ConstraintsPriority = 10;

        // the channels of one node: the simple channels by index, FK bones by name
        sealed class NodeChannels
        {
            public readonly OverrideChannelState[] Fixed = new OverrideChannelState[18];
            // FK bones by name; a bone's and an IK target's channels by their canonical text, which never equals a name
            public Dictionary<string, OverrideChannelState> Bones;
            public int Slots;
        }

        readonly int thread = Thread.CurrentThread.ManagedThreadId;
        readonly Dictionary<string, NodeChannels> nodes = new Dictionary<string, NodeChannels>(StringComparer.Ordinal);
        readonly Dictionary<string, string> ownerNames = new Dictionary<string, string>(StringComparer.Ordinal) { [TimelineOwner] = "Timeline", [ConstraintsOwner] = "Constraints" };
        OverrideChannelState camera, dof;
        long acquisitions;
        SceneEvents events;

        public OverrideLayer(IOverrideHost host = null) { Host = host; }

        public IOverrideHost Host { get; set; }
        /// <summary>Slots acquired (every owner).</summary>
        public int Count { get; private set; }
        public int Version { get; private set; }
        public int ValueVersion { get; private set; }
        /// <summary>True when any slot is acquired (the application's fast path).</summary>
        public bool Any => Count > 0;

        /// <summary>
        /// Raised for each slot of a channel that a model change touched (any origin but a save capture; the scene's camera and
        /// depth of field included), after the change was committed. Main thread.
        /// </summary>
        public event Action<OverrideSlot> ModelChanged;

        /// <summary>
        /// Whether an owner drives its channels now (the camera controller is disabled and gizmo drags are refused only while
        /// it does); null or no answer = every owner drives. The core Timeline answers true only while it plays.
        /// </summary>
        public Func<string, bool> IsDriving;

        // ------------------------------------------------------------ owners

        /// <summary>The display name of an owner ("Timeline", a plugin's name); the owner id when none was given.</summary>
        public string OwnerName(string owner) => owner != null && ownerNames.TryGetValue(owner, out var n) ? n : owner;
        public void SetOwnerName(string owner, string name) { if (owner != null) ownerNames[owner] = name ?? owner; }

        // ------------------------------------------------------------ thread

        /// <summary>InvalidOperationException off the layer's thread.</summary>
        public void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != thread)
                throw new InvalidOperationException("runtime overrides are used on the main thread only");
        }

        public bool OnLayerThread => Thread.CurrentThread.ManagedThreadId == thread;

        // ------------------------------------------------------------ acquire

        /// <summary>
        /// The owner's slot of the channel (<paramref name="nodeId"/>, <paramref name="path"/>), created without a value or the
        /// one it holds (its priority set to <paramref name="priority"/>). Null when <paramref name="nodeId"/> names no node of
        /// the model and no deleted node an undo could bring back. ArgumentException (naming the path and the expected form)
        /// for an unknown path, a scene path with a node id, a node path without one and a path the node's kind does not have.
        /// </summary>
        public OverrideSlot Acquire(string owner, string nodeId, string path, int priority = 0)
        {
            CheckThread();
            if (string.IsNullOrEmpty(owner)) throw new ArgumentException("an owner is required", nameof(owner));
            var p = OverridePath.Parse(path);
            if (p.SceneWide)
            {
                if (nodeId != null) throw new ArgumentException("path " + p.Text + " is scene-wide: the node id must be null", nameof(nodeId));
            }
            else
            {
                if (nodeId == null) throw new ArgumentException("path " + p.Text + " needs a node id (" + p.KindsText + ")", nameof(nodeId));
                var h = Host;
                if (h == null || !h.TryKindOf(nodeId, out var kind)) return null;
                if (!p.Accepts(kind)) throw new ArgumentException("path " + p.Text + " is not a path of a " + kind.ToString().ToLowerInvariant() + " node (it belongs to " + p.KindsText + ")", nameof(path));
            }
            var ch = Channel(nodeId, p, create: true);
            for (int i = 0; i < ch.Slots.Count; i++)
            {
                var s = ch.Slots[i];
                if (s.Owner != owner) continue;
                if (s.Priority != priority)
                {
                    s.SetPriority(priority);
                    Version++;
                    ValueVersion++;
                    if (ch.Elect() || s.HasValue) Changed(ch);
                }
                return s;
            }
            var slot = new OverrideSlot(this, ch, owner, priority, ++acquisitions);
            ch.Slots.Add(slot);
            Count++;
            if (nodeId != null) nodes[nodeId].Slots++;
            Version++;
            ValueVersion++;
            return slot;
        }

        // ------------------------------------------------------------ lookups (allocation-free)

        /// <summary>The channel of (node, path), or null when no slot was ever acquired there (or all were released).</summary>
        public OverrideChannelState Find(string nodeId, OverridePath p) => p == null ? null : Channel(nodeId, p, create: false);

        /// <summary>The winner of a simple node channel (not FkBone) or a scene channel; null when none.</summary>
        public OverrideSlot WinnerOf(string nodeId, OverrideChannel ch, int index = 0)
        {
            if (Count == 0) return null;
            if (ch == OverrideChannel.Camera) return camera?.Winner;
            if (ch == OverrideChannel.DepthOfField) return dof?.Winner;
            if (nodeId == null || !nodes.TryGetValue(nodeId, out var n)) return null;
            return n.Fixed[FixedIndex(ch, index)]?.Winner;
        }

        /// <summary>The winner of an FK bone channel of a node; null when none.</summary>
        public OverrideSlot BoneWinner(string nodeId, string bone)
        {
            if (Count == 0 || nodeId == null || bone == null || !nodes.TryGetValue(nodeId, out var n) || n.Bones == null) return null;
            return n.Bones.TryGetValue(bone, out var c) ? c.Winner : null;
        }

        /// <summary>
        /// What the channel of <paramref name="s"/> shows without <paramref name="s"/>: the value of its best other slot in
        /// <paramref name="into"/> (at least the kind's count of doubles). False when there is none: the model shows. Allocates nothing.
        /// </summary>
        public bool ValueBelow(OverrideSlot s, double[] into)
        {
            var ch = s.Channel;
            OverrideSlot w = null;
            for (int i = 0; i < ch.Slots.Count; i++)
            {
                var x = ch.Slots[i];
                if (x == s || !x.HasValue) continue;
                if (w == null || x.Priority > w.Priority || (x.Priority == w.Priority && x.Acquisition > w.Acquisition)) w = x;
            }
            if (w == null) return false;
            int n = w.ValueCount;
            for (int i = 0; i < n; i++) into[i] = w[i];
            return true;
        }

        /// <summary>The winner of (node, path text), or null.</summary>
        public OverrideSlot Winner(string nodeId, string path) =>
            OverridePath.TryParse(path, out var p, out _) ? Channel(nodeId, p, create: false)?.Winner : null;

        /// <summary>True when the node has any acquired slot.</summary>
        public bool HasNode(string nodeId) => Count > 0 && nodeId != null && nodes.TryGetValue(nodeId, out var n) && n.Slots > 0;

        /// <summary>Every channel of a node (its simple channels, then its FK bones by name).</summary>
        public IEnumerable<OverrideChannelState> ChannelsOf(string nodeId)
        {
            if (nodeId == null)
            {
                if (camera != null) yield return camera;
                if (dof != null) yield return dof;
                yield break;
            }
            if (!nodes.TryGetValue(nodeId, out var n)) yield break;
            foreach (var c in n.Fixed) if (c != null) yield return c;
            if (n.Bones != null)
            {
                var keys = new List<string>(n.Bones.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (var k in keys) yield return n.Bones[k];
            }
        }

        /// <summary>The owner whose slot drives the channel now (it wins and its owner drives: the Timeline only while it plays), or null.</summary>
        public string DrivenBy(string nodeId, string path)
        {
            var w = Winner(nodeId, path);
            if (w == null) return null;
            var f = IsDriving;
            return f == null || f(w.Owner) ? w.Owner : null;
        }

        /// <summary>The owner of the winning camera slot when it drives the view now (the camera controller is disabled), or null.</summary>
        public string CameraDrivenBy()
        {
            var w = camera?.Winner;
            if (w == null) return null;
            var f = IsDriving;
            return f == null || f(w.Owner) ? w.Owner : null;
        }

        internal bool TargetExists(string nodeId) => nodeId == null ? events != null || Host != null : Host != null && Host.TargetExists(nodeId);
        internal bool IsEffective(OverrideSlot s)
        {
            if (s.NodeId != null && !TargetExists(s.NodeId)) return false;
            var h = Host;
            return h == null || h.IsEffective(s);
        }

        // ------------------------------------------------------------ listings

        /// <summary>
        /// The slots, in (scene channels, then nodes in the model's pre-order, then deleted nodes by id; path; priority;
        /// acquisition) order: of one node (null: all), of one owner (null: all), with or without those without a value.
        /// </summary>
        public List<OverrideSlot> Slots(string nodeId = null, string owner = null, bool withValueOnly = false)
        {
            var r = new List<OverrideSlot>();
            if (Count == 0) return r;
            var order = new List<string>();
            if (nodeId != null) order.Add(nodeId);
            else
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var h = Host;
                if (h != null)
                    foreach (var id in h.NodeOrder())
                        if (nodes.ContainsKey(id) && seen.Add(id)) order.Add(id);
                var rest = new List<string>();
                foreach (var id in nodes.Keys) if (!seen.Contains(id)) rest.Add(id);
                rest.Sort(StringComparer.Ordinal);
                order.AddRange(rest);
                Add(r, camera, owner, withValueOnly);
                Add(r, dof, owner, withValueOnly);
            }
            foreach (var id in order)
            {
                if (!nodes.TryGetValue(id, out var n)) continue;
                var chans = new List<OverrideChannelState>();
                foreach (var c in n.Fixed) if (c != null) chans.Add(c);
                if (n.Bones != null) chans.AddRange(n.Bones.Values);
                chans.Sort((a, b) => string.CompareOrdinal(a.Path.Text, b.Path.Text));
                foreach (var c in chans) Add(r, c, owner, withValueOnly);
            }
            return r;
        }

        static void Add(List<OverrideSlot> r, OverrideChannelState c, string owner, bool withValueOnly)
        {
            if (c == null || c.Slots.Count == 0) return;
            int start = r.Count;
            foreach (var s in c.Slots)
                if ((owner == null || s.Owner == owner) && (!withValueOnly || s.HasValue)) r.Add(s);
            if (r.Count - start > 1)
                r.Sort(start, r.Count - start, Comparer<OverrideSlot>.Create((a, b) => a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : a.Acquisition.CompareTo(b.Acquisition)));
        }

        /// <summary>The Api's description of a slot.</summary>
        public OverrideInfo Info(OverrideSlot s) => new OverrideInfo(s.NodeId, s.Path, s.Owner, s.Priority, s.IsWinning, s.IsEffective, s.TargetExists);

        /// <summary>The Api listing: every owner's slots that hold a value (of one node, or all).</summary>
        public IReadOnlyList<OverrideInfo> List(string nodeId = null)
        {
            var l = Slots(nodeId, null, withValueOnly: true);
            var r = new OverrideInfo[l.Count];
            for (int i = 0; i < r.Length; i++) r[i] = Info(l[i]);
            return r;
        }

        // ------------------------------------------------------------ release

        /// <summary>Releases one slot (its owner or the host).</summary>
        internal void Release(OverrideSlot s, bool apply = true)
        {
            if (s.IsReleased) return;
            var ch = s.Channel;
            bool won = ch.Winner == s;
            s.MarkReleased();
            ch.Slots.Remove(s);
            Count--;
            Version++;
            ValueVersion++;
            if (ch.NodeId != null && nodes.TryGetValue(ch.NodeId, out var n)) n.Slots--;
            bool changed = ch.Elect() || won;
            if (ch.Slots.Count == 0) Drop(ch);
            if (changed && apply) Changed(ch);
        }

        /// <summary>Releases every slot of <paramref name="owner"/>; the count released. The model values show at once.</summary>
        public int ReleaseOwner(string owner)
        {
            CheckThread();
            int k = 0;
            foreach (var s in Slots(null, owner)) { Release(s); k++; }
            return k;
        }

        /// <summary>Releases the slots of a node whose id can never return (NodeForgotten); nothing is applied.</summary>
        public int ReleaseNode(string nodeId)
        {
            if (nodeId == null || !nodes.ContainsKey(nodeId)) return 0;
            int k = 0;
            foreach (var s in Slots(nodeId)) { Release(s, apply: false); k++; }
            nodes.Remove(nodeId);
            return k;
        }

        /// <summary>Releases every slot of every owner (the scene is cleared: new, load); nothing is applied.</summary>
        public int ReleaseEverything()
        {
            int k = 0;
            foreach (var s in Slots()) { Release(s, apply: false); k++; }
            nodes.Clear();
            camera = dof = null;
            return k;
        }

        // ------------------------------------------------------------ session events

        /// <summary>Follows a session: Cleared releases every slot, NodeForgotten the node's slots (subscribe before the runtime).</summary>
        public void Attach(SceneEvents ev)
        {
            Detach();
            events = ev;
            if (ev == null) return;
            ev.Cleared += OnCleared;
            ev.NodeForgotten += OnForgotten;
        }

        public void Detach()
        {
            if (events == null) return;
            events.Cleared -= OnCleared;
            events.NodeForgotten -= OnForgotten;
            events = null;
        }

        void OnCleared() => ReleaseEverything();
        void OnForgotten(string id) => ReleaseNode(id);

        /// <summary>
        /// A committed change batch (the runtime's Changed handler): raises <see cref="ModelChanged"/> for each slot of a channel a
        /// change touched (not for save captures). Cheap when no slot exists.
        /// </summary>
        public void NotifyModelChanged(SceneChangeBatch b)
        {
            if (Count == 0 || b == null || b.Origin == ChangeOrigin.Capture || ModelChanged == null) return;
            List<OverrideSlot> hit = null;
            foreach (var c in b.Changes)
            {
                switch (c.Kind)
                {
                    case SceneChangeKind.SceneChanged:
                        Touch(camera, c.Path, ref hit);
                        Touch(dof, c.Path, ref hit);
                        break;
                    case SceneChangeKind.NodeChanged:
                        if (c.NodeId == null || !nodes.TryGetValue(c.NodeId, out var n)) break;
                        foreach (var ch in n.Fixed) Touch(ch, c.Path, ref hit);
                        if (n.Bones != null) foreach (var ch in n.Bones.Values) Touch(ch, c.Path, ref hit);
                        break;
                }
            }
            if (hit == null) return;
            foreach (var s in hit)
            {
                if (s.IsReleased) continue;
                try { ModelChanged(s); }
                catch (Exception e) { SceneEvents.Log?.Invoke("override layer: a ModelChanged handler threw: " + e); }
            }
        }

        static void Touch(OverrideChannelState ch, string path, ref List<OverrideSlot> hit)
        {
            if (ch == null || ch.Slots.Count == 0 || !ch.Path.TouchedBy(path)) return;
            foreach (var s in ch.Slots)
            {
                hit ??= new List<OverrideSlot>();
                if (!hit.Contains(s)) hit.Add(s);
            }
        }

        // ------------------------------------------------------------ internals

        internal void ValueChanged(OverrideSlot s, bool hadValue)
        {
            var ch = s.Channel;
            ValueVersion++;
            bool elected = ch.Elect();
            if (elected || hadValue != s.HasValue) Version++;
            if (elected || ch.Winner == s) Changed(ch);
        }

        void Changed(OverrideChannelState ch)
        {
            var h = Host;
            if (h == null) return;
            try { h.Apply(ch); }
            catch (Exception e) { SceneEvents.Log?.Invoke("override layer: applying " + ch + " threw: " + e); }
        }

        // the key of a channel in a node's dictionary; null for the fixed channels
        static string KeyOf(OverridePath p) => p.Channel == OverrideChannel.FkBone ? p.Bone : p.IsBone || p.IsIkTarget ? p.Text : null;

        static int FixedIndex(OverrideChannel ch, int index) => ch == OverrideChannel.ItemColor && index > 0 ? 14 + index : (int)ch;

        OverrideChannelState Channel(string nodeId, OverridePath p, bool create)
        {
            if (p.Channel == OverrideChannel.Camera)
                return camera ??= create ? new OverrideChannelState(null, p) : null;
            if (p.Channel == OverrideChannel.DepthOfField)
                return dof ??= create ? new OverrideChannelState(null, p) : null;
            if (nodeId == null) return null;
            if (!nodes.TryGetValue(nodeId, out var n))
            {
                if (!create) return null;
                nodes[nodeId] = n = new NodeChannels();
            }
            string key = KeyOf(p);
            if (key != null)
            {
                if (n.Bones == null) { if (!create) return null; n.Bones = new Dictionary<string, OverrideChannelState>(StringComparer.Ordinal); }
                if (n.Bones.TryGetValue(key, out var b)) return b;
                if (!create) return null;
                return n.Bones[key] = new OverrideChannelState(nodeId, p);
            }
            int i = FixedIndex(p.Channel, p.Index);
            var c = n.Fixed[i];
            if (c == null && create) n.Fixed[i] = c = new OverrideChannelState(nodeId, p);
            return c;
        }

        void Drop(OverrideChannelState ch)
        {
            var p = ch.Path;
            if (p.Channel == OverrideChannel.Camera) { if (camera == ch) camera = null; return; }
            if (p.Channel == OverrideChannel.DepthOfField) { if (dof == ch) dof = null; return; }
            if (!nodes.TryGetValue(ch.NodeId, out var n)) return;
            string key = KeyOf(p);
            if (key != null) { if (n.Bones != null && n.Bones.TryGetValue(key, out var b) && b == ch) n.Bones.Remove(key); }
            else
            {
                int i = FixedIndex(p.Channel, p.Index);
                if (n.Fixed[i] == ch) n.Fixed[i] = null;
            }
            if (n.Slots <= 0) nodes.Remove(ch.NodeId);
        }
    }
}
