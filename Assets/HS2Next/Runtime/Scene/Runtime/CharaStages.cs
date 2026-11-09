// Motion stages that stay on a scene character across its rebuilds. A rebuild destroys the bones, the CharaMotion and every stage,
// so an entry here holds a factory, not a stage: the factory runs when the entry is added to a built character and again at every
// CharaBuilt, with the new realization. A flush keeps the motion and with it the stages, so it runs nothing. Whoever needs a stage on
// a character's whole life uses this: the plugin view (PluginCharaStages, which holds the plugin's phase range) and the host's own
// stages, at any phase. An entry is keyed by node and owner.
//
// Life of an entry: it survives a rebuild; a deleted node keeps its stages with the character in the graveyard, since an undo brings
// the same motion back (a graveyard entry that was evicted comes back through a rebuild, so through CharaBuilt); NodeForgotten ends
// it; so does the end of the realization (new scene, load, dispose): nothing here is saved.
//
// This is a runtime part made at the first For, so a scene that never adds a stage has none of it; events are subscribed only while
// an entry exists. Removal never happens under a walk of CharaMotion's stage list: while a guarded stage call is on the stack
// (BeginCall) a take-off waits for the next Apply or LateApply, with the stage switched off at once, because a list walk that loses
// an element skips the stage after it for that frame. Main thread only.
using System;
using System.Collections.Generic;
using HS2Next.Motion;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>The stages kept on scene characters across rebuilds; one per runtime, made by <see cref="For"/>.</summary>
    public sealed class CharaStageHost : ISceneRuntimePart
    {
        static readonly IMotionStage[] None = new IMotionStage[0];

        sealed class Entry : IDisposable
        {
            internal CharaStageHost Host;
            internal string NodeId, Owner;
            internal Func<CharaRealization, IReadOnlyList<IMotionStage>> Make;
            internal IMotionStage[] Stages = None;
            internal CharaMotion Motion;
            internal bool Ended;

            public void Dispose() => Host.End(this);
        }

        readonly Dictionary<string, List<Entry>> byNode = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
        readonly List<(CharaMotion motion, IMotionStage stage)> lateRemoval = new List<(CharaMotion, IMotionStage)>();
        readonly List<Entry> lateAttach = new List<Entry>();
        SceneRuntime runtime;
        int calls, count;
        bool subscribed;

        CharaStageHost() { }

        /// <summary>The host of <paramref name="runtime"/>, made at the first call.</summary>
        public static CharaStageHost For(SceneRuntime runtime)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            var h = runtime.GetPart<CharaStageHost>();
            if (h == null) runtime.AddPart(h = new CharaStageHost());
            return h;
        }

        /// <summary>The host of <paramref name="runtime"/>, or null when nothing asked for one.</summary>
        public static CharaStageHost Of(SceneRuntime runtime) => runtime?.GetPart<CharaStageHost>();

        /// <summary>Entries now.</summary>
        public int Count => count;

        /// <summary>True while the host follows the runtime's events: while an entry exists.</summary>
        public bool Listening => subscribed;

        /// <summary>
        /// Keeps the stages <paramref name="make"/> gives on the character <paramref name="nodeId"/>. <paramref name="make"/> runs at once
        /// when the character is built, and at every later build with the new realization (r.Motion, r.Context and r.Root are the new
        /// ones); it may do what the new body needs, and may return null or an empty list. The stages go on the motion in list order, at
        /// the phases they name: no phase is refused here. A stage that was off stays off, by its index in the list. Dispose the result to
        /// take them off. ArgumentException for a node that is no character.
        /// </summary>
        public IDisposable Add(string nodeId, string owner, Func<CharaRealization, IReadOnlyList<IMotionStage>> make)
        {
            if (make == null) throw new ArgumentNullException(nameof(make));
            if (string.IsNullOrEmpty(owner)) throw new ArgumentException("a stage needs an owner", nameof(owner));
            var r = runtime.FindChara(nodeId);
            if (r == null && (nodeId == null || runtime.Model.Find(nodeId)?.Kind != NodeKind.Chara))
                throw new ArgumentException("not a character: " + (nodeId ?? "(null)"), nameof(nodeId));
            var e = new Entry { Host = this, NodeId = nodeId, Owner = owner, Make = make };
            if (!byNode.TryGetValue(nodeId, out var list)) byNode[nodeId] = list = new List<Entry>();
            list.Add(e);
            count++;
            Subscribe();
            if (r != null && r.State == RealizationState.Built) Attach(e, r);
            return e;
        }

        /// <summary><see cref="Add(string, string, Func{CharaRealization, IReadOnlyList{IMotionStage}})"/> for one stage.</summary>
        public IDisposable Add(string nodeId, string owner, Func<CharaRealization, IMotionStage> make)
        {
            if (make == null) throw new ArgumentNullException(nameof(make));
            return Add(nodeId, owner, r =>
            {
                var s = make(r);
                return s != null ? new[] { s } : None;
            });
        }

        /// <summary>Ends every entry of <paramref name="owner"/>; returns how many.</summary>
        public int RemoveOwner(string owner)
        {
            int n = 0;
            foreach (var e in Snapshot())
                if (e.Owner == owner) { End(e); n++; }
            return n;
        }

        /// <summary>The stages <paramref name="owner"/> has on the current body of <paramref name="nodeId"/>, in step order of its entries.</summary>
        public IReadOnlyList<IMotionStage> StagesOf(string nodeId, string owner)
        {
            var l = new List<IMotionStage>();
            if (nodeId != null && byNode.TryGetValue(nodeId, out var list))
                foreach (var e in list)
                    if (e.Owner == owner) l.AddRange(e.Stages);
            return l;
        }

        /// <summary>A call into a stage begins: CharaMotion may be walking its list. Pair with <see cref="EndCall"/>.</summary>
        public void BeginCall() => calls++;

        /// <summary>The call begun with <see cref="BeginCall"/> ended.</summary>
        public void EndCall() => calls--;

        List<Entry> Snapshot()
        {
            var l = new List<Entry>(count);
            foreach (var kv in byNode) l.AddRange(kv.Value);
            return l;
        }

        void Attach(Entry e, CharaRealization r)
        {
            var m = r.Motion;
            if (!m || e.Ended || e.Motion == m) return;
            if (calls > 0)
            {
                if (!lateAttach.Contains(e)) lateAttach.Add(e);
                return;
            }
            // the switches first: a stage is asked before it leaves
            var old = e.Stages;
            var off = new bool[old.Length];
            for (int i = 0; i < old.Length; i++) off[i] = !old[i].Enabled;
            TakeOff(e);
            IReadOnlyList<IMotionStage> made = null;
            try { made = e.Make(r); }
            catch (Exception x) { Debug.LogWarning("stages of " + e.Owner + " on " + e.NodeId + ": the factory threw " + x); }
            if (e.Ended || !m) return;
            e.Motion = m;
            if (made == null || made.Count == 0) return;
            var stages = new List<IMotionStage>(made.Count);
            for (int i = 0; i < made.Count; i++)
            {
                var s = made[i];
                if (s == null) continue;
                if (i < off.Length && off[i]) s.Enabled = false;
                m.Add(s);
                stages.Add(s);
            }
            e.Stages = stages.ToArray();
        }

        void TakeOff(Entry e)
        {
            var m = e.Motion;
            var stages = e.Stages;
            e.Motion = null;
            e.Stages = None;
            foreach (var s in stages)
            {
                (s as IDisposable)?.Dispose();
                if (!m) continue;
                if (calls > 0)
                {
                    s.Enabled = false;
                    lateRemoval.Add((m, s));
                }
                else m.Remove(s);
            }
        }

        void End(Entry e)
        {
            if (e.Ended) return;
            e.Ended = true;
            TakeOff(e);
            if (byNode.TryGetValue(e.NodeId, out var list) && list.Remove(e))
            {
                count--;
                if (list.Count == 0) byNode.Remove(e.NodeId);
            }
            if (count == 0) Unsubscribe();
        }

        void Subscribe()
        {
            if (subscribed) return;
            subscribed = true;
            runtime.CharaBuilt += OnBuilt;
            runtime.Session.Events.NodeForgotten += OnForgotten;
        }

        void Unsubscribe()
        {
            if (!subscribed) return;
            subscribed = false;
            runtime.CharaBuilt -= OnBuilt;
            runtime.Session.Events.NodeForgotten -= OnForgotten;
        }

        void OnBuilt(CharaRealization r)
        {
            if (r == null || !byNode.TryGetValue(r.NodeId, out var list)) return;
            foreach (var e in list.ToArray()) Attach(e, r);
        }

        void OnForgotten(string id)
        {
            if (!byNode.TryGetValue(id, out var list)) return;
            foreach (var e in list.ToArray()) End(e);
        }

        void Flush()
        {
            if (calls > 0) return;
            if (lateRemoval.Count > 0)
            {
                foreach (var (m, s) in lateRemoval)
                    if (m) m.Remove(s);
                lateRemoval.Clear();
            }
            if (lateAttach.Count > 0)
            {
                var l = lateAttach.ToArray();
                lateAttach.Clear();
                foreach (var e in l)
                {
                    var r = runtime.FindChara(e.NodeId);
                    if (r != null && r.State == RealizationState.Built) Attach(e, r);
                }
            }
        }

        void ISceneRuntimePart.Attach(SceneRuntime r) => runtime = r;
        void ISceneRuntimePart.OnChanged(SceneChangeBatch b) { }
        void ISceneRuntimePart.OnLoadedRealized() { }

        void ISceneRuntimePart.Apply()
        {
            if (lateRemoval.Count > 0 || lateAttach.Count > 0) Flush();
        }

        void ISceneRuntimePart.LateApply()
        {
            if (lateRemoval.Count > 0 || lateAttach.Count > 0) Flush();
        }

        void ISceneRuntimePart.Clear()
        {
            foreach (var e in Snapshot()) End(e);
            lateRemoval.Clear();
            lateAttach.Clear();
        }
    }
}
