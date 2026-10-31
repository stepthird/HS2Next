// Neck look stage (neck "kubifuri"): NeckLookControllerVer2 + NeckLookCalcVer2 behaviour:
// the controller, UpdateCall, prologue and type blend, ANIMATION/FORWARD/FIX/CONTROL, TARGET/AWAY with
// limit-break hysteresis, aim fixed-point iteration and side switching, per-bone rotation, first
// frames. It follows the pattern semantics, reads the data and reports the warnings.
//
// The data (bones, reference objects, type states, curve) is read once from the deserialised compat
// holders by LookSetup; the compat components themselves never run.
//
// HS2Next additions (README "Deviations"):
// - Input-pose latch: HS2 has an Animator that rewrites the neck/head pose before every LateUpdate, so the
//   look always starts from "the animated pose of this frame". HS2Next may have no pose writer at all
//   (Animator disabled, rest pose). Before each step the stage therefore checks whether the bones still hold
//   exactly what it wrote last step; if so nobody wrote them and the previously seen input pose is put back,
//   otherwise the new values are the input (animation, FK, pose tools). LatchInputPose = false disables it.
// - dt == 0 (pause) holds the last output for every type (CharaMotion contract); HS2 shows the animated pose
//   there for the non-TARGET types.
// - Pattern numbers are clamped to the valid range; changeTypeLeapTime <= 0 gives an immediate blend.
using UnityEngine;

namespace HS2Next.Motion.Look
{
    public sealed class NeckLookStage : IMotionStage
    {
        /// <summary>NECK_LOOK_TYPE_VER2.</summary>
        public const int Animation = 0, TargetType = 1, Away = 2, Forward = 3, Fix = 4, Control = 5;

        /// <summary>NeckLimitParameter.</summary>
        public sealed class LimitParam
        {
            public string Name;
            public float Up, Down, Min, Max;
        }

        /// <summary>NeckTypeStateVer2. IsLimitBreak is the runtime isLimitBreakBackup.</summary>
        public sealed class TypeState
        {
            public string Name;
            public LimitParam[] Params;
            public float LeapSpeed, HAngleLimit, VAngleLimit, LimitBreakCorrection, LimitAway;
            public bool IsLimitBreak, SerializedLimitBreak;
            public int LookType;
        }

        /// <summary>NeckObjectVer2 plus the input-pose latch.</summary>
        public sealed class Bone
        {
            public string Name;
            public Transform Reference, NeckBone, ControlBone;
            public Quaternion FixAngle, FixAngleBackup, BackupByTarget;
            /// <summary>Local rotation when the stage was set up (the rest pose for a fresh character).</summary>
            public Quaternion Rest;
            public float AngleH, AngleV;
            internal Quaternion lastWritten, lastInput;
            internal bool written;
        }

        public MotionPhase Phase => MotionPhase.NeckLook;
        public string Name => "neck";
        public bool Enabled { get; set; } = true;

        // ---- controller (NeckLookControllerVer2)
        /// <summary>Pattern number (index into States; clamped).</summary>
        public int PtnNo;
        /// <summary>Explicit target (used when FollowCamera is false).</summary>
        public Transform Target;
        /// <summary>Target type 0: follow LookTargets.CameraTransform (DefaultCamera, else Camera.main), resolved every step.</summary>
        public bool FollowCamera;
        /// <summary>Controller rate: blends the target's X and Y (not Z) towards the controller position.</summary>
        public float Rate = 1f;
        /// <summary>The controller's host (cf_J_Neck).</summary>
        public Transform Controller;

        // ---- calc (NeckLookCalcVer2)
        /// <summary>Per-instance switch (SetEnable).</summary>
        public bool CalcEnabled = true;
        public Transform Aim, CalcAngle;
        public Bone[] Bones;
        public TypeState[] States;
        public float ChangeTypeLeapTime = 1f;
        public AnimationCurve Curve;
        public float CalcLerp = 1f;
        /// <summary>No smoothing, no type blend, runs even when dt = 0 (mannequin switch).</summary>
        public bool SkipCalc;
        /// <summary>See the file header.</summary>
        public bool LatchInputPose = true;
        /// <summary>Which compat assembly the data came from (diagnostics).</summary>
        public string Source;

        /// <summary>The total (x = V, y = H) the chain was asked to turn in the last computed frame.</summary>
        public Vector2 NowAngle { get; private set; }
        /// <summary>Last real target position (reused when there is no target).</summary>
        public Vector3 BackupPos { get; private set; }
        /// <summary>Internal current look type (ANIMATION after Init).</summary>
        public int CurrentType { get; private set; }
        public float ChangeTypeTimer { get; private set; }
        public int NowPtnNo { get; private set; }
        public bool Initialised => initialised;
        bool initialised;

        public int PatternCount => States?.Length ?? 0;

        /// <summary>Init, with the backups at the current local rotations.</summary>
        public void Init()
        {
            initialised = false;
            if (Bones == null || Bones.Length == 0 || States == null || States.Length == 0 || Aim == null || CalcAngle == null) return;
            foreach (var b in Bones)
            {
                if (b.NeckBone == null) return;
                if (b.Reference == null) b.Reference = Controller != null ? Controller : b.NeckBone;
                b.AngleH = b.AngleV = 0f;
                var q = b.NeckBone.localRotation;
                b.FixAngle = b.FixAngleBackup = b.BackupByTarget = q;
                b.written = false;
            }
            foreach (var s in States) s.IsLimitBreak = s.SerializedLimitBreak;
            CurrentType = Animation;
            ChangeTypeTimer = 0f;
            NowAngle = Vector2.zero;
            BackupPos = Vector3.zero;
            initialised = true;
        }

        /// <summary>Remember the current local rotations as the rest pose (LookSetup, once).</summary>
        public void CaptureRest()
        {
            if (Bones == null) return;
            foreach (var b in Bones) if (b.NeckBone != null) b.Rest = b.NeckBone.localRotation;
        }

        public void Reset()
        {
            if (Bones != null)
                foreach (var b in Bones)
                    if (b.written && b.NeckBone != null) b.NeckBone.localRotation = b.lastInput;   // undo the look's output
            Init();
        }

        public int ClampPtn(int p) => States == null || States.Length == 0 ? 0 : Mathf.Clamp(p, 0, States.Length - 1);

        public Transform ResolveTarget() => FollowCamera ? LookTargets.CameraTransform : Target;

        public void Step(in MotionFrame frame)
        {
            if (!initialised) return;
            float dt = frame.Dt;
            int ptn = ClampPtn(PtnNo);
            if (dt <= 0f && !SkipCalc && CalcEnabled) { Pause(ptn); return; }
            Latch();
            UpdateCall(ptn, true);
            var t = ResolveTarget();
            if (t != null)
            {
                Vector3 tp = t.position;
                Vector3 cp = Controller != null ? Controller.position : Bones[0].NeckBone.position;
                tp.x = Mathf.Lerp(cp.x, tp.x, Rate);
                tp.y = Mathf.Lerp(cp.y, tp.y, Rate);   // Z is not blended
                Calc(tp, ptn, false, dt);
            }
            else Calc(Vector3.zero, ptn, true, dt);
            Record();
        }

        /// <summary>One step with skipCalc (blend and smoothing complete at once, runs at dt = 0): ChaControl's mannequin switch.</summary>
        public void ForceStep(float dt)
        {
            bool old = SkipCalc;
            SkipCalc = true;
            try { Step(new MotionFrame(dt, dt, 0f, 0)); }
            finally { SkipCalc = old; }
        }

        void Latch()
        {
            foreach (var b in Bones)
            {
                var cur = b.NeckBone.localRotation;
                if (LatchInputPose && b.written && LookMath.Same(cur, b.lastWritten)) b.NeckBone.localRotation = b.lastInput;
                else b.lastInput = cur;
            }
        }

        // dt == 0: keep the last output without rewriting it (a rewrite would re-normalise the quaternion and drift
        // by an ulp); type-change bookkeeping still happens. An external write during the pause becomes the
        // input and our output is put back.
        void Pause(int ptn)
        {
            NowPtnNo = ptn;
            UpdateCall(ptn, false);
            foreach (var b in Bones)
            {
                if (!b.written) continue;
                var cur = b.NeckBone.localRotation;
                if (LookMath.Same(cur, b.lastWritten)) continue;
                b.lastInput = cur;
                b.NeckBone.localRotation = b.lastWritten;
                b.lastWritten = b.NeckBone.localRotation;
            }
        }

        void Record()
        {
            foreach (var b in Bones) { b.lastWritten = b.NeckBone.localRotation; b.written = true; }
        }

        //
        void UpdateCall(int ptn, bool touchBones)
        {
            int type = States[ptn].LookType;
            if (CurrentType != type)
            {
                CurrentType = type;
                ChangeTypeTimer = 0f;
                foreach (var b in Bones) b.FixAngleBackup = b.FixAngle;
                if (type == Forward) foreach (var b in Bones) b.AngleH = b.AngleV = 0f;
            }
            if (touchBones && CurrentType == TargetType)
                foreach (var b in Bones)
                {
                    b.BackupByTarget = b.NeckBone.localRotation;
                    b.NeckBone.localRotation = b.FixAngle;
                }
        }

        //
        void Calc(Vector3 target, int ptn, bool useBackupPos, float dt)
        {
            NowPtnNo = ptn;
            if (!CalcEnabled) return;
            var st = States[ptn];
            if (!useBackupPos) BackupPos = target;
            if (st.Params == null || st.Params.Length != Bones.Length) return;
            if (SkipCalc) ChangeTypeTimer = ChangeTypeLeapTime;
            ChangeTypeTimer = Mathf.Clamp(ChangeTypeTimer + dt, 0f, Mathf.Max(ChangeTypeLeapTime, 0f));
            float wLin = ChangeTypeLeapTime > 0f ? Mathf.InverseLerp(0f, ChangeTypeLeapTime, ChangeTypeTimer) : 1f;
            float w = Curve != null && Curve.length > 0 ? Curve.Evaluate(wLin) : wLin;

            switch (st.LookType)
            {
                case Animation:
                    foreach (var b in Bones)
                    {
                        b.FixAngle = b.NeckBone.localRotation;
                        b.NeckBone.localRotation = Quaternion.Slerp(b.FixAngleBackup, b.FixAngle, w);
                        if (b.ControlBone != null) { b.ControlBone.localRotation = b.FixAngle; b.ControlBone.gameObject.SetActive(false); }
                    }
                    return;
                case Control:
                    if (AllControlBones())
                    {
                        var first = Bones[0].ControlBone.gameObject;
                        for (int k = 0; k < Bones.Length; k++)
                        {
                            var b = Bones[k];
                            if (k > 0) b.ControlBone.gameObject.SetActive(!first.activeSelf);   // source quirk
                            b.FixAngle = b.ControlBone.localRotation;
                            b.NeckBone.localRotation = Quaternion.Lerp(b.FixAngleBackup, b.FixAngle, wLin);
                        }
                        return;
                    }
                    DeactivateControls();
                    DoForward(w);
                    return;
                case Forward:
                    DeactivateControls();
                    DoForward(w);
                    return;
                case Fix:
                    DeactivateControls();
                    foreach (var b in Bones)
                    {
                        b.NeckBone.localRotation = Quaternion.Slerp(b.FixAngleBackup, Quaternion.Slerp(b.NeckBone.localRotation, b.FixAngle, CalcLerp), w);
                        if (b.ControlBone != null) b.ControlBone.localRotation = b.FixAngle;
                    }
                    return;
                case TargetType:
                case Away:
                    DeactivateControls();
                    TargetAway(st, target, useBackupPos, dt, w);
                    return;
                default:
                    DeactivateControls();
                    return;
            }
        }

        void DoForward(float w)
        {
            foreach (var b in Bones)
            {
                b.FixAngle = Quaternion.identity;
                b.NeckBone.localRotation = Quaternion.Slerp(b.FixAngleBackup, Quaternion.Slerp(b.NeckBone.localRotation, Quaternion.identity, CalcLerp), w);
                if (b.ControlBone != null) b.ControlBone.localRotation = b.FixAngle;
            }
        }

        bool AllControlBones()
        {
            foreach (var b in Bones) if (b.ControlBone == null) return false;
            return true;
        }

        void DeactivateControls()
        {
            foreach (var b in Bones)
                if (b.ControlBone != null && b.ControlBone.gameObject.activeSelf) b.ControlBone.gameObject.SetActive(false);
        }

        //
        void TargetAway(TypeState st, Vector3 target, bool useBackupPos, float dt, float w)
        {
            var nr = CalcAngle;
            Vector3 dirB = target - nr.position;
            float h = LookMath.SignedAround(nr.forward, dirB, nr.up);
            float v = LookMath.SignedAround(nr.forward, dirB, nr.right);
            float margin = st.IsLimitBreak ? 0f : st.LimitBreakCorrection;
            bool broken = Mathf.Abs(h) > st.HAngleLimit + margin || Mathf.Abs(v) > st.VAngleLimit + margin;
            st.IsLimitBreak = broken;

            Vector2 now;
            if (broken) now = Vector2.zero;
            else
            {
                if (useBackupPos) target = BackupPos;
                now = AngleToTarget(target);
                if (st.LookType == TargetType)
                {
                    // a. behind-and-below guard (never fires on the vanilla skeleton)
                    var frame = Matrix4x4.TRS(Aim.position, nr.rotation, Aim.lossyScale).inverse;
                    Vector3 tl = frame.MultiplyPoint3x4(target), nl = frame.MultiplyPoint3x4(nr.position);
                    if (tl.z < 0f && tl.y < 0f && ((nl.x < 0f && tl.x > nl.x && tl.x < 0f) || (nl.x > 0f && tl.x < nl.x && tl.x > 0f)))
                        target = Aim.position;
                    // b. target at the aim point: re-apply the current angles without a smoothing step
                    if (target == Aim.position)
                    {
                        NowAngle = now;
                        foreach (var b in Bones)
                        {
                            CalcBone(b);
                            b.NeckBone.localRotation = Quaternion.Slerp(b.FixAngleBackup, b.FixAngle, w);
                        }
                        return;
                    }
                }
                else
                {
                    float cur = 0f, maxSum = 0f, minSum = 0f;
                    for (int i = 0; i < Bones.Length; i++) { cur += Bones[i].AngleH; maxSum += st.Params[i].Max; minSum += st.Params[i].Min; }
                    float H = now.y;
                    if (H <= cur) H = H <= maxSum - st.LimitAway || H < 0f ? maxSum : minSum;
                    else H = H >= minSum + st.LimitAway || H > 0f ? minSum : maxSum;
                    now = new Vector2(-now.x, H);
                }
            }
            NowAngle = now;

            // 5. distribute head first
            float rx = now.x, ry = now.y;
            float t = SkipCalc ? 1f : Mathf.Clamp01(dt * st.LeapSpeed);
            for (int i = Bones.Length - 1; i >= 0; i--)
            {
                var p = st.Params[i];
                var b = Bones[i];
                float hi = Mathf.Clamp(ry, p.Min, p.Max), vi = Mathf.Clamp(rx, p.Up, p.Down);
                rx -= vi; ry -= hi;
                b.AngleH = Mathf.Lerp(b.AngleH, hi, t);
                b.AngleV = Mathf.Lerp(b.AngleV, vi, t);
                CalcBone(b);
            }
            // 6. output
            foreach (var b in Bones)
            {
                b.NeckBone.localRotation = Quaternion.Slerp(b.FixAngleBackup, Quaternion.Slerp(b.BackupByTarget, b.FixAngle, CalcLerp), w);
                if (b.ControlBone != null) b.ControlBone.localRotation = b.FixAngle;
            }
        }

        Vector2 AngleToTarget(Vector3 target)
        {
            var nr = CalcAngle;
            var last = Bones[Bones.Length - 1].NeckBone;
            Quaternion q = Quaternion.FromToRotation(Aim.forward, target - Aim.position);
            Vector3 gz = q * last.rotation * Vector3.forward;
            Vector3 up = nr.up;
            float H = LookMath.SignedAround(nr.forward, gz, up);
            Vector3 yz = Quaternion.AngleAxis(H, up) * nr.rotation * Vector3.forward;
            float V = LookMath.SignedAround(yz, gz, Vector3.Cross(up, yz));
            return new Vector2(V, H);
        }

        // world = rotAxis(H, ref.up) * rotAxis(V, ref.right) * ref.rotation; fixAngle = the local result.
        void CalcBone(Bone b)
        {
            var r = b.Reference;
            Quaternion world = Quaternion.AngleAxis(b.AngleH, r.up) * Quaternion.AngleAxis(b.AngleV, r.right) * r.rotation;
            var parent = b.NeckBone.parent;
            Quaternion local = parent != null ? Quaternion.Inverse(parent.rotation) * world : world;
            b.NeckBone.localRotation = local;
            b.FixAngle = local;
        }

        // ---- scene persistence

        /// <summary>Copies each bone's fixAngle (the neckByteData payload) into dst (length >= bone count).</summary>
        public void SaveFixAngles(Quaternion[] dst)
        {
            for (int i = 0; i < Bones.Length && i < dst.Length; i++) dst[i] = Bones[i].FixAngle;
        }

        /// <summary>LoadNeckLookCtrl: pattern, and per bone fixAngle = fixAngleBackup = q, bone local = q.</summary>
        public void LoadFixAngles(int ptn, Quaternion[] src)
        {
            PtnNo = ptn;
            for (int i = 0; i < Bones.Length && i < src.Length; i++)
            {
                var b = Bones[i];
                if (!b.written) b.lastInput = b.NeckBone.localRotation;
                b.FixAngle = b.FixAngleBackup = src[i];
                b.NeckBone.localRotation = src[i];
                if (b.ControlBone != null) b.ControlBone.localRotation = src[i];
                b.lastWritten = b.NeckBone.localRotation;
                b.written = true;
            }
        }
    }
}
