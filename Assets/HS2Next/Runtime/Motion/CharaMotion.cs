// Per-character motion orchestrator (Phase 1.5).
//
// Everything that moves bones or blend shapes after the Animator (own FK/IK later, twist bones,
// neck/eye look, expression, dynamic bones) is a stage registered on the character's CharaMotion.
// One LateUpdate steps all stages in a fixed, documented order, so the result does not depend on
// component order or on Unity's undefined script order, and tests can step a character by hand
// (Step(dt)) without a play loop. A card's bone modifiers are stages here as well (phase 120, Modifiers/BoneModifierStages.cs).
// The phase order follows the authoritative per-frame table;
// MotionPhase values leave room for insertions. MotionPhase, MotionFrame and
// IMotionStage live in the HS2Next.Api assembly (Assets/HS2Next/Api).
// Probe (Phase 3.4): a side-effect-free run of the stages before a phase (the pose
// "clip + hand patterns + the card's bone modifiers" for the animation copies), every transform under the root restored afterwards.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Motion
{
    /// <summary>Receives the duration of every stage step (Stopwatch ticks) while it is set as <see cref="CharaMotion.TimingSink"/>.</summary>
    public interface IMotionTimingSink
    {
        void StageStepped(CharaMotion motion, IMotionStage stage, long ticks);
    }

    /// <summary>
    /// Component on the character root. Steps the registered stages in phase order from LateUpdate
    /// (AutoStep) or by hand (Step). Exceptions in a stage are caught, logged once per stage and counted,
    /// so one broken stage does not stop the others.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]   // after any ordinary LateUpdate writer (Animator IK pass, legacy components)
    public sealed class CharaMotion : MonoBehaviour
    {
        readonly List<IMotionStage> stages = new List<IMotionStage>();
        readonly HashSet<string> reported = new HashSet<string>();
        bool dirty;

        /// <summary>Step from LateUpdate with <see cref="DeltaSource"/> (false: only manual Step calls move the character).</summary>
        public bool AutoStep = true;
        /// <summary>The scene's motion pass steps this character in its place among the others, so LateUpdate leaves it alone. Step works as ever.</summary>
        public bool Scheduled;
        /// <summary>The delta of the LateUpdate step (null = Time.deltaTime). Scene characters get the scene clock's FrameDt
        /// (0 while the scene is paused).</summary>
        public Func<float> DeltaSource;
        /// <summary>Per-character speed (Studio "speed" for physics / expression; 0 = pause).</summary>
        public float TimeScale = 1f;
        /// <summary>Pause switch independent of TimeScale.</summary>
        public bool Paused;
        /// <summary>Clamp for one step after a hitch (seconds, scaled). Stages see at most this much time per step.</summary>
        public float MaxStep = 1f / 20f;
        /// <summary>Optional log sink (the assembler's Log while building; null = Debug.Log).</summary>
        public List<string> Log;

        public float Time { get; private set; }
        public int StepCount { get; private set; }
        public int Errors { get; private set; }
        public IReadOnlyList<IMotionStage> Stages { get { Sort(); return stages; } }

        /// <summary>Per-stage timing for measurements (app.perf); null (the default) = no timing.</summary>
        public static IMotionTimingSink TimingSink;

        public void Add(IMotionStage stage)
        {
            if (stage == null) throw new ArgumentNullException(nameof(stage));
            if (stages.Contains(stage)) return;
            stages.Add(stage);
            dirty = true;
        }

        public bool Remove(IMotionStage stage) => stages.Remove(stage);

        public T Get<T>() where T : class, IMotionStage
        {
            foreach (var s in stages) if (s is T t) return t;
            return null;
        }

        public IEnumerable<T> GetAll<T>() where T : class, IMotionStage
        {
            foreach (var s in stages) if (s is T t) yield return t;
        }

        /// <summary>Drop every stage's history (after a teleport or a pose jump).</summary>
        public void ResetAll()
        {
            foreach (var s in stages) Safe(s, () => s.Reset(), "reset");
            Time = 0f; StepCount = 0;
        }

        // ------------------------------------------------------------ placement (after 1.1.0)

        Transform placementRoot;
        Matrix4x4 placementFrom;
        bool placementPending;

        /// <summary>
        /// The physics chains were set up while <paramref name="root"/> had the world matrix <paramref name="from"/> (the assembler
        /// builds at the origin): the next step first carries every bust / hip chain's and dynamic bone's particles rigidly to where
        /// the root stands then, so the physics start from the placed pose. Moves of the root after that step stay physical. A root
        /// whose matrix is bit-equal to <paramref name="from"/> at that step touches nothing.
        /// </summary>
        public void CarryChainsAtFirstStep(Transform root, Matrix4x4 from)
        {
            placementRoot = root;
            placementFrom = from;
            placementPending = root;
        }

        void CarryToPlacement()
        {
            placementPending = false;
            if (!placementRoot) return;
            var to = placementRoot.localToWorldMatrix;
            bool same = true;
            for (int k = 0; k < 16 && same; k++) same = placementFrom[k] == to[k];
            placementRoot = null;
            if (same) return;
            var d = to * placementFrom.inverse;
            foreach (var s in stages)
            {
                if (s is DynamicBones.BustStage b) foreach (var c in b.All) c.CarryRigid(d);
                else if (s is DynamicBones.DynamicBoneStage db) foreach (var c in db.Chains) c.CarryRigid(d);
            }
        }

        // ------------------------------------------------------------ settle (after 1.1.0, the paused scene)

        /// <summary>Frames of physics time a settle runs (each of <see cref="SettleDt"/>; the other stages get no time).</summary>
        public static int SettleSteps = 120;
        /// <summary>The physics time of one settle frame (seconds).</summary>
        public static float SettleDt = 1f / 60f;
        /// <summary>True until the next step runs the requested settle.</summary>
        public bool SettlePending { get; private set; }
        /// <summary>Settles run on this character (diagnostics, tests).</summary>
        public int Settles { get; private set; }
        /// <summary>Milliseconds of the last settle (diagnostics).</summary>
        public double LastSettleMs { get; private set; }

        /// <summary>
        /// Asks the next <see cref="Step"/> to settle the physics first: the scene calls it when a change builds or reshapes the
        /// character while no time passes (the global pause, or its own pause or time scale 0), so the breasts, hips, hair and clothes chains show at once what
        /// they come to rest at, while the animation, the look, the expression and the motion's time stay where they are.
        /// </summary>
        public void RequestSettle() => SettlePending = true;

        static bool IsPhysics(IMotionStage s) => s is DynamicBones.BustStage || s is DynamicBones.DynamicBoneStage;

        /// <summary>
        /// The settle: one frame of no time poses the character as it stands (animation frame, shapes, FK / IK, look), the
        /// physics stages drop their history (particles at the posed bones), then <see cref="SettleSteps"/> frames run every
        /// stage again, the physics stages with <see cref="SettleDt"/> of time and the others with none. Time and StepCount do not
        /// change. Nothing happens when no physics stage is enabled.
        /// </summary>
        void Settle()
        {
            SettlePending = false;
            bool any = false;
            for (int i = 0; i < stages.Count && !any; i++) any = stages[i].Enabled && IsPhysics(stages[i]);
            if (!any) return;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var still = new MotionFrame(0f, 0f, Time, StepCount);
            var moving = new MotionFrame(SettleDt, SettleDt, Time, StepCount);
            SettleFrame(still, still);
            for (int i = 0; i < stages.Count; i++)
            {
                var s = stages[i];
                if (s.Enabled && IsPhysics(s)) Safe(s, () => s.Reset(), "settle");
            }
            for (int k = 0; k < SettleSteps; k++) SettleFrame(still, moving);
            Settles++;
            LastSettleMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }

        void SettleFrame(MotionFrame still, MotionFrame moving)
        {
            for (int i = 0; i < stages.Count; i++)
            {
                var s = stages[i];
                if (!s.Enabled) continue;
                var f = IsPhysics(s) ? moving : still;
                Safe(s, () => s.Step(f), "settle");
            }
        }

        /// <summary>Advance all enabled stages by one frame. dt is unscaled; TimeScale / Paused / MaxStep are applied here.</summary>
        public void Step(float dt)
        {
            Sort();
            if (placementPending) CarryToPlacement();
            if (SettlePending) Settle();
            float scaled = Paused ? 0f : Mathf.Min(Mathf.Max(dt, 0f) * Mathf.Max(TimeScale, 0f), MaxStep);
            var frame = new MotionFrame(scaled, Mathf.Max(dt, 0f), Time, StepCount);
            var sink = TimingSink;
            for (int i = 0; i < stages.Count; i++)
            {
                var s = stages[i];
                if (!s.Enabled) continue;
                if (sink == null) { Safe(s, () => s.Step(frame), "step"); continue; }
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                Safe(s, () => s.Step(frame), "step");
                sink.StageStepped(this, s, System.Diagnostics.Stopwatch.GetTimestamp() - t0);
            }
            Time += scaled;
            StepCount++;
        }

        readonly List<Transform> probeList = new List<Transform>(1024);
        Vector3[] probePos = new Vector3[0], probeScale = new Vector3[0];
        Quaternion[] probeRot = new Quaternion[0];
        bool probing;
        Transform probeRoot;
        Func<Transform, bool> probeStop;
        int probeCount;
        static readonly Func<Transform, bool> DefaultStop = Scenes.Runtime.SceneNodeHolder.IsHolder;

        /// <summary>True while <see cref="Probe"/> runs (stages may skip work that must not be repeated).</summary>
        public bool Probing => probing;

        /// <summary>
        /// Side-effect-free probe: records the local position, rotation and scale of every transform under
        /// <paramref name="root"/> (not entering transforms for which <paramref name="stop"/> returns true; default: scene
        /// node holders of attached children), steps every enabled stage with Phase &lt; <paramref name="before"/> with a
        /// frame of dt 0 (Time and StepCount unchanged), calls <paramref name="read"/>, then writes back every recorded
        /// transform that changed (also when read throws). Positions and scales come back bit for bit; a rotation the probe's
        /// stages rewrote comes back through Unity's setter, which normalises and can change the last bits (a value read from
        /// a transform and set again is not always stored identically) — those transforms are rewritten by the same stages at
        /// the next step. Untouched transforms are never written. Stage exceptions are handled as in Step. Not reentrant.
        /// The transform list is cached while root, stop rule and root.hierarchyCount stay the same.
        /// </summary>
        public void Probe(MotionPhase before, Transform root, Action read, Func<Transform, bool> stop = null)
        {
            if (read == null) throw new ArgumentNullException(nameof(read));
            if (probing) throw new InvalidOperationException("CharaMotion.Probe is not reentrant");
            if (!root) root = transform;
            stop = stop ?? DefaultStop;
            probing = true;
            // the transform list is cached per root and stop rule while the hierarchy's transform count is unchanged
            if (root != probeRoot || stop != probeStop || root.hierarchyCount != probeCount || probeList.Count == 0)
            {
                probeList.Clear();
                Collect(root, stop, true);
                probeRoot = root; probeStop = stop; probeCount = root.hierarchyCount;
            }
            int n = probeList.Count;
            if (probePos.Length < n) { probePos = new Vector3[n]; probeScale = new Vector3[n]; probeRot = new Quaternion[n]; }
            for (int i = 0; i < n; i++)
            {
                var t = probeList[i];
                if (!t) continue;
                t.GetLocalPositionAndRotation(out probePos[i], out probeRot[i]);
                probeScale[i] = t.localScale;
            }
            try
            {
                Sort();
                var frame = new MotionFrame(0f, 0f, Time, StepCount);
                for (int i = 0; i < stages.Count; i++)
                {
                    var s = stages[i];
                    if (s.Phase >= before) break;
                    if (!s.Enabled) continue;
                    Safe(s, () => s.Step(frame), "probe");
                }
                read();
            }
            finally
            {
                bool stale = false;
                for (int i = 0; i < n; i++)
                {
                    var t = probeList[i];
                    if (!t) { stale = true; continue; }
                    t.GetLocalPositionAndRotation(out var lp, out var lr);
                    if (!Same(lp, probePos[i]) || !Same(lr, probeRot[i])) t.SetLocalPositionAndRotation(probePos[i], probeRot[i]);
                    if (!Same(t.localScale, probeScale[i])) t.localScale = probeScale[i];
                }
                if (stale) probeList.Clear();
                probing = false;
            }
        }

        static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        static bool Same(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;

        void Collect(Transform t, Func<Transform, bool> stop, bool isRoot)
        {
            if (!isRoot && stop(t)) return;
            probeList.Add(t);
            int c = t.childCount;
            for (int i = 0; i < c; i++) Collect(t.GetChild(i), stop, false);
        }

        /// <summary>One step with <see cref="DeltaSource"/>: what LateUpdate does for a character that steps itself.</summary>
        public void StepAuto() => Step(DeltaSource != null ? DeltaSource() : UnityEngine.Time.deltaTime);

        void LateUpdate()
        {
            if (AutoStep && !Scheduled) StepAuto();
        }

        void OnEnable() { if (StepCount > 0) ResetAll(); }

        void Sort()
        {
            if (!dirty) return;
            // Stable by phase, then registration order.
            var indexed = new List<(IMotionStage s, int i)>(stages.Count);
            for (int i = 0; i < stages.Count; i++) indexed.Add((stages[i], i));
            indexed.Sort((a, b) => a.s.Phase != b.s.Phase ? a.s.Phase.CompareTo(b.s.Phase) : a.i.CompareTo(b.i));
            stages.Clear();
            foreach (var (s, _) in indexed) stages.Add(s);
            dirty = false;
        }

        void Safe(IMotionStage s, Action a, string what)
        {
            try { a(); }
            catch (Exception e)
            {
                Errors++;
                string key = s.Name + ":" + what + ":" + e.GetType().Name;
                if (!reported.Add(key)) return;
                string msg = $"motion: {s.Name} {what} failed ({e.GetType().Name}: {e.Message})";
                if (Log != null) Log.Add(msg); else Debug.LogWarning(msg + "\n" + e.StackTrace, this);
            }
        }
    }
}
