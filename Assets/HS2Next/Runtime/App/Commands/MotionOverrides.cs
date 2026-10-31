// Runtime-only motion switches the bridge set on characters (`chara.motion`): per
// node a record of what `chara.motion` changed (pause, time scale, auto step, HS2's physics / look switches, stage
// switches, the joint-correction stage switch). The record is applied to the character's CharaMotion at once and again
// whenever the node is built anew (SceneRuntime.CharaBuilt: a rebuild after an appearance change, a revive by
// rebuild, `app.options {rebuild}`), so a switch survives rebuilds of the same node id. It is never saved and is
// dropped by `scene.new` / `scene.load` (the session's Cleared event), when the node is forgotten (deleted and no
// longer held by the history; a delete undone from the graveyard keeps the same CharaMotion and its switches) and by
// `chara.motion {reset}`.
//   physics false  stages dynbone and bust disabled (their chains return to the start / reference pose) and a
//                  BustKinematicStage after BustStage; true: both enabled, the kinematic stage removed
//   look false     stages eyes and neck disabled (HS2's bridge disabled the eye and neck look controllers)
//   stages         {name: enabled} for any stage except the FK writer's (fk, fkneck, fkhips) and the bridge's own
//                  bustKinematic (validated by the command); a later physics / look switch replaces the entries of
//                  its stages, so the newest request wins after a rebuild too
//   master         the joint-correction stage (`correct`) switch
// One instance per SceneRuntime (Attach is idempotent; For finds or attaches). Main thread only.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HS2Next.Motion;
using HS2Next.Motion.DynamicBones;
using HS2Next.Motion.Look;
using HS2Next.Scenes.Runtime;

namespace HS2Next.App.Commands
{
    public sealed class MotionOverrides
    {
        /// <summary>Stages the FK writer owns (chara.motion answers bad_args for them: use fk.enable).</summary>
        public static readonly string[] FkStages = { "fk", "fkneck", "fkhips" };
        public const string PhysicsStageDynBone = "dynbone", PhysicsStageBust = "bust", LookStageEyes = "eyes", LookStageNeck = "neck", CorrectStage = "correct";

        static readonly ConditionalWeakTable<SceneRuntime, MotionOverrides> perRuntime = new ConditionalWeakTable<SceneRuntime, MotionOverrides>();

        /// <summary>The switches of one node; null members were not set.</summary>
        public sealed class Record
        {
            public bool? Paused, AutoStep, Physics, Look, Master;
            public float? TimeScale;
            /// <summary>Stage name → enabled, in the order they were first set.</summary>
            public readonly Dictionary<string, bool> Stages = new Dictionary<string, bool>(StringComparer.Ordinal);

            public bool IsEmpty => Paused == null && AutoStep == null && Physics == null && Look == null && Master == null && TimeScale == null && Stages.Count == 0;

            public Record Clone()
            {
                var c = new Record { Paused = Paused, AutoStep = AutoStep, Physics = Physics, Look = Look, Master = Master, TimeScale = TimeScale };
                foreach (var kv in Stages) c.Stages[kv.Key] = kv.Value;
                return c;
            }

            public override string ToString()
            {
                var l = new List<string>();
                if (Paused != null) l.Add("paused " + Paused);
                if (TimeScale != null) l.Add("timeScale " + TimeScale);
                if (AutoStep != null) l.Add("autoStep " + AutoStep);
                if (Physics != null) l.Add("physics " + Physics);
                if (Look != null) l.Add("look " + Look);
                if (Master != null) l.Add("correct " + Master);
                foreach (var kv in Stages) l.Add(kv.Key + " " + kv.Value);
                return l.Count == 0 ? "(none)" : string.Join(", ", l);
            }
        }

        readonly SceneRuntime rt;
        readonly Dictionary<string, Record> records = new Dictionary<string, Record>(StringComparer.Ordinal);
        bool attached;

        MotionOverrides(SceneRuntime runtime) { rt = runtime; }

        /// <summary>The overrides of <paramref name="runtime"/>, subscribing to its runtime and session events once.</summary>
        public static MotionOverrides Attach(SceneRuntime runtime)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            lock (perRuntime)
            {
                if (!perRuntime.TryGetValue(runtime, out var o))
                {
                    o = new MotionOverrides(runtime);
                    perRuntime.Add(runtime, o);
                }
                o.Subscribe();
                return o;
            }
        }

        /// <summary>Same as <see cref="Attach"/> (handlers call it; the bootstrap attaches at startup).</summary>
        public static MotionOverrides For(SceneRuntime runtime) => Attach(runtime);

        void Subscribe()
        {
            if (attached) return;
            attached = true;
            rt.CharaBuilt += OnCharaBuilt;
            rt.Session.Events.Cleared += OnCleared;
            rt.Session.Events.NodeForgotten += OnForgotten;
        }

        /// <summary>Unsubscribes and drops every record (the runtime is going away).</summary>
        public void Detach()
        {
            if (!attached) return;
            attached = false;
            rt.CharaBuilt -= OnCharaBuilt;
            rt.Session.Events.Cleared -= OnCleared;
            rt.Session.Events.NodeForgotten -= OnForgotten;
            records.Clear();
        }

        public int Count => records.Count;
        public IEnumerable<string> Nodes => records.Keys;

        /// <summary>The record of a node, or null.</summary>
        public Record Get(string nodeId) => nodeId != null && records.TryGetValue(nodeId, out var r) ? r : null;

        public Record GetOrCreate(string nodeId)
        {
            if (!records.TryGetValue(nodeId, out var r)) records[nodeId] = r = new Record();
            return r;
        }

        /// <summary>Drops a node's record (the live switches stay until the node is built again).</summary>
        public bool Forget(string nodeId) => nodeId != null && records.Remove(nodeId);

        /// <summary>Drops every record.</summary>
        public void Clear() => records.Clear();

        void OnCharaBuilt(CharaRealization r)
        {
            if (r == null || !r.Motion) return;
            var rec = Get(r.NodeId);
            if (rec != null) Apply(r.Motion, rec);
        }

        void OnCleared() => records.Clear();

        void OnForgotten(string id) => records.Remove(id);

        // ------------------------------------------------------------ applying switches

        /// <summary>Applies every member of <paramref name="rec"/> to <paramref name="m"/> (physics / look first, then the stage switches).</summary>
        public static void Apply(CharaMotion m, Record rec)
        {
            if (!m || rec == null) return;
            if (rec.Paused != null) m.Paused = rec.Paused.Value;
            if (rec.TimeScale != null) m.TimeScale = rec.TimeScale.Value;
            if (rec.AutoStep != null) m.AutoStep = rec.AutoStep.Value;
            if (rec.Physics != null) SetPhysics(m, rec.Physics.Value);
            if (rec.Look != null) SetLook(m, rec.Look.Value);
            foreach (var kv in rec.Stages) SetStage(m, kv.Key, kv.Value);
            if (rec.Master != null) SetStage(m, CorrectStage, rec.Master.Value);
        }

        /// <summary>HS2's `shape.physics`: false disables the dynamic-bone and bust stages and adds the kinematic bust stage.</summary>
        public static void SetPhysics(CharaMotion m, bool on)
        {
            if (!m) return;
            var dyn = m.Get<DynamicBoneStage>();
            var bust = m.Get<BustStage>();
            if (dyn != null) dyn.Enabled = on;
            if (bust != null) bust.Enabled = on;
            var kin = m.Get<BustKinematicStage>();
            if (!on)
            {
                if (kin == null && bust != null) { kin = new BustKinematicStage(bust); m.Add(kin); }
                if (kin != null) { kin.Enabled = true; kin.Apply(); }
            }
            else if (kin != null) m.Remove(kin);
        }

        /// <summary>HS2's eye / neck look controllers: false disables the eyes and neck stages.</summary>
        public static void SetLook(CharaMotion m, bool on)
        {
            if (!m) return;
            var eyes = m.Get<EyeLookStage>();
            var neck = m.Get<NeckLookStage>();
            if (eyes != null) eyes.Enabled = on;
            if (neck != null) neck.Enabled = on;
        }

        /// <summary>Sets every stage of that name; false when the motion has none.</summary>
        public static bool SetStage(CharaMotion m, string name, bool on)
        {
            if (!m) return false;
            bool any = false;
            foreach (var s in m.Stages)
                if (s != null && s.Name == name) { s.Enabled = on; any = true; }
            return any;
        }

        /// <summary>
        /// Effective physics of a motion: the dynamic-bone and bust stages enabled (a missing stage counts as on) and no
        /// kinematic stage.
        /// </summary>
        public static bool PhysicsOn(CharaMotion m)
        {
            if (!m) return false;
            var dyn = m.Get<DynamicBoneStage>();
            var bust = m.Get<BustStage>();
            return (dyn == null || dyn.Enabled) && (bust == null || bust.Enabled) && m.Get<BustKinematicStage>() == null;
        }

        /// <summary>Effective look: the eyes and neck stages enabled (a missing stage counts as on).</summary>
        public static bool LookOn(CharaMotion m)
        {
            if (!m) return false;
            var eyes = m.Get<EyeLookStage>();
            var neck = m.Get<NeckLookStage>();
            return (eyes == null || eyes.Enabled) && (neck == null || neck.Enabled);
        }

        /// <summary>
        /// Puts the members of <paramref name="old"/> that <paramref name="keep"/> does not set back to their defaults on
        /// <paramref name="m"/> (not paused, time scale 1, auto step unless the runtime steps by hand, physics and look on,
        /// stages on); used by `chara.motion {reset}` so the live state matches the record that remains.
        /// </summary>
        public static void RestoreDefaults(CharaMotion m, Record old, Record keep, bool manualStepping)
        {
            if (!m || old == null) return;
            keep = keep ?? new Record();
            if (old.Paused != null && keep.Paused == null) m.Paused = false;
            if (old.TimeScale != null && keep.TimeScale == null) m.TimeScale = 1f;
            if (old.AutoStep != null && keep.AutoStep == null) m.AutoStep = !manualStepping;
            if (old.Physics != null && keep.Physics == null) SetPhysics(m, true);
            if (old.Look != null && keep.Look == null) SetLook(m, true);
            foreach (var kv in old.Stages)
                if (!keep.Stages.ContainsKey(kv.Key)) SetStage(m, kv.Key, true);
            if (old.Master != null && keep.Master == null) SetStage(m, CorrectStage, true);
        }

        /// <summary>Replaces a node's record (null or empty: removes it).</summary>
        public void Set(string nodeId, Record rec)
        {
            if (rec == null || rec.IsEmpty) records.Remove(nodeId);
            else records[nodeId] = rec;
        }

        public override string ToString() => $"motion overrides: {records.Count} nodes";
    }
}
