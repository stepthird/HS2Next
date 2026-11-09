// Stains at run time: a scene part with one rig per character whose block is not empty. A scene without wet has no rig: the frame's calls return at once and nothing is
// drawn, created or subscribed.
//   who tells it     a change of `chara.wet` (the rig takes the document in the next Apply), a change of `chara.state` (the film copies the garment again), a built
//                    character (CharaBuilt: its maps resolve on the new body) and a flush of the character (AfterFlush)
//   the clock        Tick takes the scene clock's delta: the pause stops a stain, a step and a recording step it, a capture only reads it. Timeline seeks move nothing
//   what ops call    Bake, Preview, CaptureNow, FloorEdit, AttachRay and AddRay; every change of a block is a WetEditCommand
// The clock and the puddles live here while a scene runs; SaveCapture writes them into the document before a save, a duplicate and a delete.
using System;
using System.Collections.Generic;
using HS2Next.Chara.Edit;
using HS2Next.Save;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>How a character's stains stand, for <c>wet.status</c> and the panel.</summary>
    public sealed class WetCharaStatus
    {
        /// <summary>The name of the current stain; null when none.</summary>
        public string Current;
        public float T;
        public bool Running;
        /// <summary>The current stain's radius now.</summary>
        public double Radius;
        /// <summary>Slots that show a stain, slots left out for their state, puddles.</summary>
        public int Targets, Unreadable, Tiled, NoFilm, Puddles;
        /// <summary>Maps that act, and maps kept for a part that is not there, over all stains.</summary>
        public int Live, Dormant, Protected;
        public float Floor;
        /// <summary>One of <see cref="WetFloorSources"/>.</summary>
        public string FloorSource;
    }

    public sealed class WetPart : ISceneRuntimePart, ISceneTickPart, IDisposable
    {
        /// <summary>A drag bakes at most this often, in seconds of real time; the last value bakes at the commit.</summary>
        public const float PreviewEvery = 0.05f;
        const string WetPath = "chara.wet", StatePath = "chara.state";

        SceneRuntime rt;
        readonly Dictionary<string, WetRig> rigs = new Dictionary<string, WetRig>(StringComparer.Ordinal);
        readonly HashSet<string> wetDirty = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> stateDirty = new HashSet<string>(StringComparer.Ordinal);
        readonly List<string> scratch = new List<string>();
        Transform root;
        string mapRef;
        float lastPreview = float.NegativeInfinity;

        /// <summary>Characters with a rig (tests).</summary>
        public int Rigs => rigs.Count;
        /// <summary>The runtime's root of what is drawn beside the characters; null until the first stain.</summary>
        public Transform Root => root;

        // ------------------------------------------------------------ ISceneRuntimePart

        public void Attach(SceneRuntime r)
        {
            rt = r;
            r.CharaBuilt += OnCharaBuilt;
            r.Session.Events.NodeForgotten += OnForgotten;
        }

        public void OnChanged(SceneChangeBatch b)
        {
            if (b.Origin == ChangeOrigin.Capture) return;
            var l = b.Changes;
            for (int i = 0; i < l.Count; i++)
            {
                var c = l[i];
                if (c.Kind != SceneChangeKind.NodeChanged || c.Path == null) continue;
                if (FieldPath.Overlaps(c.Path, WetPath)) wetDirty.Add(c.NodeId);
                else if (rigs.Count > 0 && FieldPath.Overlaps(c.Path, StatePath)) stateDirty.Add(c.NodeId);
            }
        }

        public void Apply()
        {
            if (wetDirty.Count == 0 && stateDirty.Count == 0) return;
            if (wetDirty.Count > 0)
            {
                scratch.Clear();
                scratch.AddRange(wetDirty);
                wetDirty.Clear();
                foreach (var id in scratch) Sync(id);
            }
            if (stateDirty.Count > 0)
            {
                foreach (var id in stateDirty)
                    if (rigs.TryGetValue(id, out var rig)) rig.StateChanged();
                stateDirty.Clear();
            }
        }

        public void LateApply()
        {
            if (rigs.Count == 0) return;
            var map = rt.GetPart<MapRealization>();
            if (map == null || map.State != MapState.Loading)   // a map on its way says nothing yet: the old one stays the one to compare with
            {
                string now = map != null && map.State == MapState.Loaded ? map.Ref : null;
                if (now != mapRef)
                {
                    bool moved = mapRef != null && now != null;
                    mapRef = now;
                    foreach (var rig in rigs.Values)
                    {
                        if (moved) rig.MapChanged();
                        else rig.MeasureSoon();
                    }
                }
            }
            foreach (var rig in rigs.Values) rig.LateApply();
        }

        public void Tick(float dt)
        {
            if (rigs.Count == 0) return;
            foreach (var rig in rigs.Values) rig.Tick(dt);
        }

        public void Clear()
        {
            foreach (var rig in rigs.Values) rig.Dispose();
            rigs.Clear();
            wetDirty.Clear();
            stateDirty.Clear();
            mapRef = null;
        }

        public void OnLoadedRealized() { }

        public void Dispose()
        {
            Clear();
            if (rt != null)
            {
                rt.CharaBuilt -= OnCharaBuilt;
                rt.Session.Events.NodeForgotten -= OnForgotten;
            }
            if (root) Destroy(root.gameObject);
            root = null;
        }

        // ------------------------------------------------------------ rigs

        Transform WetRoot()
        {
            if (root) return root;
            var go = new GameObject("Wet") { hideFlags = HideFlags.HideAndDontSave };
            root = go.transform;
            root.SetParent(rt.Root.transform, false);
            return root;
        }

        static CharaNodeState StateOf(TreeNode n) => n?.State as CharaNodeState;

        WetRig Rig(string id, CharaRealization r)
        {
            if (rigs.TryGetValue(id, out var rig)) return rig;
            rig = new WetRig(rt, this, id, WetRoot());
            rig.Bind(r);
            rigs[id] = rig;
            return rig;
        }

        void Drop(string id)
        {
            if (!rigs.TryGetValue(id, out var rig)) return;
            rig.Dispose();
            rigs.Remove(id);
        }

        void Sync(string id)
        {
            var r = rt.FindChara(id);
            var st = StateOf(rt.Model.Find(id));
            if (st == null || st.Wet.IsEmpty) { Drop(id); return; }
            if (r == null || r.State != RealizationState.Built) return;   // the build's event brings it
            Rig(id, r).Sync(st.Wet);
        }

        void OnCharaBuilt(CharaRealization c)
        {
            var st = StateOf(c.Node);
            if (st == null) return;
            if (st.Wet.IsEmpty) { Drop(c.NodeId); return; }
            wetDirty.Remove(c.NodeId);
            if (rigs.TryGetValue(c.NodeId, out var rig)) rig.Bind(c);
            else Rig(c.NodeId, c).Sync(st.Wet);
        }

        void OnForgotten(string id)
        {
            if (rt.Model.Find(id) == null) Drop(id);
        }

        /// <summary>A flush of the character ran: one line in its realization.</summary>
        internal void AfterFlush(CharaRealization c, FlushReport r)
        {
            if (rigs.TryGetValue(c.NodeId, out var rig)) rig.AfterFlush(r);
        }

        /// <summary>The character shows dry until the handle is disposed: a thumbnail of the character or an outfit. Null when it has no stain.</summary>
        public IDisposable Suspend(CharaRealization c)
        {
            if (c == null || !rigs.TryGetValue(c.NodeId, out var rig)) return null;
            rig.Suspend(true);
            return new Handle(rig);
        }

        sealed class Handle : IDisposable
        {
            WetRig rig;
            public Handle(WetRig rig) { this.rig = rig; }
            public void Dispose()
            {
                rig?.Suspend(false);
                rig = null;
            }
        }

        internal void Collect(CharaRealization c, List<(string id, string path, object value, Action after)> writes)
        {
            if (rigs.TryGetValue(c.NodeId, out var rig)) rig.Collect(writes);
        }

        // ------------------------------------------------------------ what ops call

        /// <summary>True while a bake can run: a graphics device and the shader.</summary>
        public bool CanBake => WetBake.Available;

        /// <summary>
        /// The maps of <paramref name="stain"/> of <paramref name="block"/> (a copy the caller edits, not yet in the model), on what the character shows now, with the current pose:
        /// every picture goes into the file's store and the stain's Maps are set. With <paramref name="missing"/> only the targets without a live map get one (Apply again).
        /// Null on success; else `unsupported`, wet.noFrame or not_found.
        /// </summary>
        public string Bake(string nodeId, CharaWet block, WetStain stain, bool missing = false)
        {
            var r = rt.FindChara(nodeId);
            if (r == null || r.State != RealizationState.Built) return CommandCodes.NotFound;
            var rig = Ensure(nodeId, r);
            return rig.Bake(block, stain, missing);
        }

        /// <summary>
        /// A shape dragged: the character shows <paramref name="stain"/>'s new shape at once, with no picture, at most every <see cref="PreviewEvery"/> of real time. The commit is a
        /// <see cref="Bake"/> with the last value. Null on success or when the time has not come.
        /// </summary>
        public string Preview(string nodeId, CharaWet block, WetStain stain)
        {
            float now = Time.realtimeSinceStartup;
            if (now - lastPreview < PreviewEvery) return null;
            lastPreview = now;
            var r = rt.FindChara(nodeId);
            if (r == null || r.State != RealizationState.Built) return CommandCodes.NotFound;
            return Ensure(nodeId, r).Preview(block, stain);
        }

        // a bake of a character's first stain comes before its block is in the model; the next Apply drops the rig when the edit did not come
        WetRig Ensure(string nodeId, CharaRealization r)
        {
            wetDirty.Add(nodeId);
            return Rig(nodeId, r);
        }

        /// <summary>True when going from <paramref name="before"/> to <paramref name="after"/> needs a bake: the maps hold the shape's axes, the noise, the origin and a radius up to the baked reach.</summary>
        public static bool NeedsBake(WetStain before, WetStain after)
        {
            var a = before.Shape;
            var b = after.Shape;
            if (a.Up != b.Up || a.Down != b.Down || a.Front != b.Front || a.Back != b.Back || a.Side != b.Side || a.Outer != b.Outer || a.Noise != b.Noise) return true;
            var o = before.Origin;
            var p = after.Origin;
            if (o.Mode != p.Mode || o.Bone != p.Bone || !o.Local.Equals(p.Local) || o.Down != p.Down || o.Out != p.Out) return true;
            float reach = 0f;
            foreach (var m in before.Maps) reach = Mathf.Max(reach, m.Reach);
            return after.Maps.Count == 0 || b.Radius + b.Feather > reach;
        }

        /// <summary>The runtime's clock and puddles of a node into the document, silently: what an edit that replaces the block starts from.</summary>
        public void CaptureNow(string nodeId) => rt.Capture.CaptureSubtrees(new[] { nodeId });

        /// <summary>
        /// The floor a block would have: its height and source, and <paramref name="block"/>'s puddles moved by the difference to the floor of now. An edit of the floor
        /// passes through here, so undo brings the puddles back with the floor. A fixed floor without a height given takes the present one.
        /// </summary>
        public float FloorEdit(string nodeId, CharaWet block, out string source)
        {
            source = WetFloorSources.None;
            var r = rt.FindChara(nodeId);
            if (r == null || r.State != RealizationState.Built) return 0f;
            var rig = Ensure(nodeId, r);
            if (rig.Doc == null) rig.Sync(block);
            rig.MeasureNow();
            float was = rig.Floor.Y;
            float y = rig.FloorFor(block.Floor, out source);
            float d = y - was;
            if (d != 0f)
                foreach (var p in block.Puddles) p.Center = new Vec3(p.Center.X, p.Center.Y + d, p.Center.Z);
            return y;
        }

        /// <summary>
        /// The surface a ray meets on the character's body and clothes, as a stain's origin: a bone's address and the point in its space. False when it meets nothing.
        /// </summary>
        public bool OriginOf(string nodeId, Ray ray, WetOriginValues origin, float maxDistance = 1000f)
        {
            var r = rt.FindChara(nodeId);
            if (r == null || r.State != RealizationState.Built) return false;
            var surface = WetTargets.Collect(r, true, true);
            if (!WetTargets.Raycast(surface, ray, maxDistance, out var hit)) return false;
            string address = BoneAddress(r, hit.Bone, out var bone);
            if (address == null) return false;
            var local = bone.InverseTransformPoint(hit.Point);
            origin.Mode = WetOriginValues.ModeBone;
            origin.Bone = address;
            origin.Local = new Vec3(local.x, local.y, local.z);
            return true;
        }

        // the address of a bone, or of the first ancestor that has one
        static string BoneAddress(CharaRealization r, Transform t, out Transform bone)
        {
            var cc = r.Context;
            for (var x = t; x; x = x.parent)
            {
                if ((cc.BodyBones != null && cc.BodyBones.TryGetValue(x.name, out var a) && a == x) || (cc.HeadBones != null && cc.HeadBones.TryGetValue(x.name, out var b) && b == x))
                {
                    bone = x;
                    return x.name;
                }
                if (x == r.Root.transform) break;
            }
            foreach (var row in AdvancedPoseResolver.Bones(r))
                if (row.Bone == t) { bone = t; return row.Address; }
            bone = null;
            return null;
        }

        /// <summary>
        /// The tool's attach: clears the character's stains and puddles, adds one stain, dry, from where a ray meets the character, or from the default origin with
        /// <paramref name="ray"/> null; one history entry. The block's switches stay. A ray that meets nothing changes nothing (not_found). <paramref name="prepare"/> gets the new
        /// block and stain before the bake; the ray runs <paramref name="maxDistance"/>.
        /// </summary>
        public CommandResult AttachRay(string nodeId, Ray? ray, string preset, string name, ExecuteOptions o = default, Action<CharaWet, WetStain> prepare = null, float maxDistance = 1000f) =>
            NewStain(nodeId, ray, preset, name, true, o, prepare, maxDistance);

        /// <summary>
        /// As <see cref="AttachRay"/>, but the character keeps the stains and puddles it has: one more stain, dry, as the current one. <paramref name="prepare"/> may start it.
        /// </summary>
        public CommandResult AddRay(string nodeId, Ray? ray, string preset, string name, ExecuteOptions o = default, Action<CharaWet, WetStain> prepare = null, float maxDistance = 1000f) =>
            NewStain(nodeId, ray, preset, name, false, o, prepare, maxDistance);

        CommandResult NewStain(string nodeId, Ray? ray, string preset, string name, bool clear, ExecuteOptions o, Action<CharaWet, WetStain> prepare, float maxDistance)
        {
            var st = StateOf(rt.Model.Find(nodeId));
            if (st == null) return CommandResult.Error(CommandCodes.NotFound, "no character " + nodeId);
            CaptureNow(nodeId);
            var block = st.Wet.Clone();
            if (clear)
            {
                block.Stains.Clear();
                block.Puddles.Clear();
            }
            var stain = new WetStain { Id = block.NextId(), Name = name ?? "stain", Seed = 1 };
            if (!WetPreset.ApplyTo(stain, string.IsNullOrEmpty(preset) ? WetPreset.Water : preset))
                return CommandResult.Error(CommandCodes.BadArgs, "no preset " + preset);
            if (ray.HasValue && !OriginOf(nodeId, ray.Value, stain.Origin, maxDistance))
                return CommandResult.Error(CommandCodes.NotFound, "the ray meets nothing on the character");
            block.Stains.Add(stain);
            block.Current = stain.Id;
            prepare?.Invoke(block, stain);
            string why = Bake(nodeId, block, stain);
            if (why != null) return CommandResult.Error(why, "the stain cannot be baked");
            return rt.Session.Commands.Execute(new WetEditCommand(nodeId, block, WetEditCommand.LabelAdd), o);
        }

        // ------------------------------------------------------------ reads

        /// <summary>The status of a character with stains, or null.</summary>
        public WetCharaStatus StatusOf(string nodeId)
        {
            if (!rigs.TryGetValue(nodeId, out var rig) || rig.Doc == null) return null;
            var s = new WetCharaStatus
            {
                Targets = rig.Slots.Count,
                Unreadable = rig.Unreadable,
                Tiled = rig.Tiled,
                NoFilm = rig.NoFilm,
                Puddles = rig.Puddles.Count,
                Floor = rig.Floor.Y,
                FloorSource = rig.Floor.Source,
            };
            foreach (var run in rig.Runs)
            {
                s.Live += run.Live.Count;
                s.Dormant += run.Dormant;
                s.Protected += run.Protected;
                if (run.Doc.Id != rig.Doc.Current) continue;
                s.Current = run.Doc.Name;
                s.T = run.T;
                s.Running = run.Running;
                s.Radius = run.Radius;
            }
            return s;
        }

        /// <summary>The clock of a stain now, or NaN.</summary>
        public float TimeOf(string nodeId, string stainId)
        {
            if (rigs.TryGetValue(nodeId, out var rig))
                foreach (var run in rig.Runs) if (run.Doc.Id == stainId) return run.T;
            return float.NaN;
        }

        /// <summary>The rig of a character, or null: what tests and the panel read.</summary>
        public WetRig RigOf(string nodeId) => rigs.TryGetValue(nodeId, out var rig) ? rig : null;

        static void Destroy(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }
}
