// Eye look stage (eye "mesen"): EyeLookController + EyeLookCalc behaviour:
// Init, entry and early exits, target classification, per-eye angles with the bending rule, clamps, AWAY "sorasi",
// smoothing and write-back, end of frame, operations and patterns, and the outputs for the face (IEyeLookOutputs).
// It reads the data and reports the warnings.
//
// Differences from HS2 (README "Deviations"):
// - The global switch (EyeLookCalc.isEnabled, static in HS2) is GlobalEnabled AND a per-character
//   LookEnabled (initialised from DefaultEnabled).
// - Init references come from the prefab rest pose (LookSetup passes them); fixAngle starts at the rest
//   local rotations instead of zero quaternions.
// - Pattern numbers are clamped to the valid range.
using UnityEngine;

namespace HS2Next.Motion.Look
{
    public sealed class EyeLookStage : IMotionStage, IEyeLookOutputs
    {
        /// <summary>EYE_LOOK_TYPE.</summary>
        public const int NoLook = 0, TargetType = 1, Away = 2, Forward = 3, Control = 4;

        /// <summary>EyeTypeState.</summary>
        public sealed class TypeState
        {
            public float Threshold, BendingMultiplier, MaxAngleDifference;
            public float Up, Down, Min, Max;
            public float LeapSpeed, ForntTagDis, NearDis, HAngleLimit, VAngleLimit;
            public int LookType;
        }

        /// <summary>EyeObject with its runtime state.</summary>
        public sealed class Eye
        {
            public Transform Transform;
            /// <summary>EYE_LR: 0 left, 1 right.</summary>
            public int LR;
            public Vector3 RefLook, RefUp, DirUp;
            public Quaternion Orig, FixAngle;
            public float AngleH, AngleV;
        }

        /// <summary>The game's global switch (EyeLookCalc.setEnable affects all characters).</summary>
        public static bool GlobalEnabled = true;
        /// <summary>Initial value of LookEnabled for new characters.</summary>
        public static bool DefaultEnabled = true;

        public MotionPhase Phase => MotionPhase.EyeLook;
        public string Name => "eyes";
        public bool Enabled { get; set; } = true;
        /// <summary>Per-character switch (effective: GlobalEnabled &amp;&amp; LookEnabled).</summary>
        public bool LookEnabled = DefaultEnabled;

        // ---- controller (EyeLookController)
        public int PtnNo;
        /// <summary>Explicit target (used when FollowCamera is false). No target: the eyes do nothing.</summary>
        public Transform Target;
        /// <summary>Target type 0: follow LookTargets.CameraTransform, resolved every step.</summary>
        public bool FollowCamera;

        // ---- calc (EyeLookCalc)
        public Transform RootNode;
        public Eye[] Eyes;
        public Vector3 HeadLookVector = Vector3.forward, HeadUpVector = Vector3.up;
        public TypeState[] States;
        public float SorasiRate = 1.5f;
        public GameObject TargetObj;
        public string Source;
        /// <summary>Number of eyes whose Init references came from the prefab rest pose (diagnostics; LookSetup).</summary>
        public int ReferencesFromPrefab;

        /// <summary>Final target used in the last computed frame (after near clamp / forward / control).</summary>
        public Vector3 TargetPos { get; private set; }
        public int NowPtnNo { get; private set; }
        public bool Initialised => initialised;
        bool initialised, hasOutput;
        readonly float[] rateH = new float[2];
        float rateV;

        public int PatternCount => States?.Length ?? 0;
        public int ClampPtn(int p) => States == null || States.Length == 0 ? 0 : Mathf.Clamp(p, 0, States.Length - 1);
        public Transform ResolveTarget() => FollowCamera ? LookTargets.CameraTransform : Target;

        // ---- IEyeLookOutputs
        public bool HasOutput => hasOutput;
        public float AngleH(int eye) => Eyes != null && eye >= 0 && eye < Eyes.Length ? Eyes[eye].AngleH : 0f;
        public float AngleV(int eye) => Eyes != null && eye >= 0 && eye < Eyes.Length ? Eyes[eye].AngleV : 0f;
        public float AngleHRate(int eye) => eye >= 0 && eye < 2 ? rateH[eye] : 0f;
        public float AngleVRate => rateV;

        /// <summary>
        /// Init. restRelative[i] = inverse(eye parent world rotation) * rootNode world rotation in the prefab
        /// rest pose; restLocal[i] = the eye's rest local rotation (origRotation).
        /// </summary>
        public void Init(Quaternion[] restRelative, Quaternion[] restLocal)
        {
            initialised = false;
            if (Eyes == null || Eyes.Length == 0 || States == null || States.Length == 0) return;
            if (RootNode == null) return;
            var look = HeadLookVector.normalized; var up = HeadUpVector.normalized;
            for (int i = 0; i < Eyes.Length; i++)
            {
                var e = Eyes[i];
                if (e.Transform == null) continue;
                var rel = restRelative != null && i < restRelative.Length ? restRelative[i] : Quaternion.identity;
                e.RefLook = rel * look;
                e.RefUp = rel * up;
                e.Orig = restLocal != null && i < restLocal.Length ? restLocal[i] : e.Transform.localRotation;
            }
            initialised = true;
            ResetState();
        }

        void ResetState()
        {
            foreach (var e in Eyes)
            {
                e.AngleH = e.AngleV = 0f;
                e.DirUp = e.RefUp;
                e.FixAngle = e.Orig;
            }
            rateH[0] = rateH[1] = 0f; rateV = 0f;
            hasOutput = false;
        }

        /// <summary>Back to the rest pose without inertia (eye bones at their rest local rotations).</summary>
        public void Reset()
        {
            if (!initialised) return;
            ResetState();
            foreach (var e in Eyes) if (e.Transform != null) e.Transform.localRotation = e.Orig;
        }

        public void Step(in MotionFrame frame)
        {
            var t = ResolveTarget();
            if (t == null) return;   // no target: nothing at all
            UpdateCalc(t.position, ClampPtn(PtnNo), frame.Dt);
        }

        void DeactivateTargetObj()
        {
            if (TargetObj != null && TargetObj.activeSelf) TargetObj.SetActive(false);
        }

        //
        void UpdateCalc(Vector3 target, int ptn, float dt)
        {
            if (!initialised) { DeactivateTargetObj(); return; }
            NowPtnNo = ptn;
            if (!(GlobalEnabled && LookEnabled) || dt <= 0f) { DeactivateTargetObj(); return; }
            var st = States[ptn];
            int type = st.LookType;
            if (type == NoLook)
            {
                DeactivateTargetObj();
                foreach (var e in Eyes) if (e.Transform != null) e.Transform.localRotation = e.FixAngle;
                return;
            }

            // classification in the head frame
            var root = RootNode;
            Vector3 p = root.InverseTransformPoint(target);
            if (p.magnitude < st.NearDis)
            {
                p = p.normalized * st.NearDis;
                target = root.TransformPoint(p);
            }
            if (p.z < 0f
                || LookMath.Angle(new Vector3(p.x, 0f, p.z), Vector3.forward) > st.HAngleLimit
                || LookMath.Angle(new Vector3(0f, p.y, p.z), Vector3.forward) > st.VAngleLimit)
                type = Forward;
            if (type == Forward) target = root.position + root.forward * st.ForntTagDis;
            if ((type == Control || st.LookType == Control) && TargetObj != null)
            {
                if (!TargetObj.activeSelf) TargetObj.SetActive(true);
                target = Vector3.MoveTowards(root.position, TargetObj.transform.position, st.ForntTagDis);
                TargetObj.transform.position = Vector3.MoveTowards(root.position, target, 0.5f);
            }
            else if (TargetObj != null)
            {
                TargetObj.transform.position = Vector3.MoveTowards(root.position, target, 0.5f);
                DeactivateTargetObj();
            }

            // per eye
            float sep = -1f;
            float k = Mathf.Clamp01(dt * st.LeapSpeed);
            for (int i = 0; i < Eyes.Length; i++)
            {
                var e = Eyes[i];
                var tr = e.Transform;
                if (tr == null) continue;
                tr.localRotation = e.Orig;
                Quaternion P = tr.parent != null ? tr.parent.rotation : Quaternion.identity;
                Vector3 d = Quaternion.Inverse(P) * (target - tr.position).normalized;

                float H = LookMath.SignedAround(e.RefLook, d, e.RefUp);
                float V = LookMath.SignedAround(d - LookMath.Project(d, e.RefUp), d, Vector3.Cross(e.RefUp, d));

                float H3 = Mathf.Max(0f, Mathf.Abs(H) - st.Threshold) * LookMath.Sgn(H);
                float V4 = Mathf.Max(0f, Mathf.Abs(V) - st.Threshold) * LookMath.Sgn(V);
                float bm = Mathf.Abs(st.BendingMultiplier), bs = LookMath.Sgn(st.BendingMultiplier);
                H = Mathf.Max(Mathf.Abs(H3) * bm, Mathf.Abs(H) - st.MaxAngleDifference) * LookMath.Sgn(H) * bs;
                V = Mathf.Max(Mathf.Abs(V4) * bm, Mathf.Abs(V) - st.MaxAngleDifference) * LookMath.Sgn(V) * bs;

                H = e.LR == 1 ? Mathf.Clamp(H, -st.Max, -st.Min) : Mathf.Clamp(H, st.Min, st.Max);
                V = Mathf.Clamp(V, st.Up, st.Down);

                if (type == Away)
                {
                    if (i == 0)
                    {
                        float s = SorasiRate;
                        float cur = U(e.AngleH, st), tgt = U(H, st), gap = cur - tgt;
                        if (Mathf.Abs(gap) < s)
                        {
                            float goal;
                            if (gap < 0f) goal = tgt >= -s ? tgt - s : tgt + s;
                            else if (gap > 0f) goal = tgt <= s ? tgt + s : tgt - s;
                            else goal = tgt + s;
                            sep = Mathf.InverseLerp(-1f, 1f, goal);
                            H = Mathf.Lerp(-st.Max, -st.Min, sep);
                        }
                        else
                        {
                            sep = Mathf.InverseLerp(-1f, 1f, cur);
                            H = e.AngleH;
                        }
                    }
                    else H = Mathf.Lerp(-st.Max, -st.Min, sep);
                    V = -V;
                }

                e.AngleH = Mathf.Lerp(e.AngleH, H, k);
                e.AngleV = Mathf.Lerp(e.AngleV, V, k);

                Vector3 n = Quaternion.AngleAxis(e.AngleH, e.RefUp) * (Quaternion.AngleAxis(e.AngleV, Vector3.Cross(e.RefUp, e.RefLook)) * e.RefLook);
                Vector3 upT = e.RefUp;
                Vector3.OrthoNormalize(ref n, ref upT);
                e.DirUp = Vector3.Slerp(e.DirUp, upT, dt * 5f);
                Vector3.OrthoNormalize(ref n, ref e.DirUp);
                Quaternion D = Quaternion.LookRotation(n, e.DirUp) * Quaternion.Inverse(Quaternion.LookRotation(e.RefLook, e.RefUp));
                tr.localRotation = D * e.Orig;
            }

            //
            TargetPos = target;
            foreach (var e in Eyes) if (e.Transform != null) e.FixAngle = e.Transform.localRotation;
            ComputeRates(st);
            hasOutput = true;
        }

        // Normalised horizontal units of the AWAY rule (right-eye range for both eyes).
        static float U(float a, TypeState st) => Mathf.Lerp(-1f, 1f, Mathf.InverseLerp(-st.Max, -st.Min, a));

        //
        void ComputeRates(TypeState st)
        {
            for (int i = 0; i < 2; i++)
            {
                if (i >= Eyes.Length) { rateH[i] = 0f; continue; }
                float a = Eyes[i].AngleH;
                rateH[i] = i == 0 ? Mathf.Lerp(-1f, 1f, Mathf.InverseLerp(st.Min, st.Max, a))
                                  : Mathf.Lerp(-1f, 1f, Mathf.InverseLerp(-st.Max, -st.Min, a));
            }
            float v = Eyes[0].AngleV;
            if (st.Down > st.Up) rateV = v >= 0f ? -Mathf.InverseLerp(0f, st.Down, v) : Mathf.InverseLerp(0f, st.Up, v);
            else rateV = v >= 0f ? -Mathf.InverseLerp(0f, st.Up, v) : Mathf.InverseLerp(0f, st.Down, v);
        }

        // ---- other operations

        /// <summary>GetAngleHRate(EYE_LR): indexed by array slot (slot 0 = left).</summary>
        public float GetAngleHRate(int eyeLR) => AngleHRate(eyeLR);

        /// <summary>SaveAngle: copy the current eye local rotations into fixAngle and into dst (the eyesByteData payload, 2 quaternions).</summary>
        public void SaveAngles(Quaternion[] dst)
        {
            for (int i = 0; i < Eyes.Length; i++)
            {
                if (Eyes[i].Transform != null) Eyes[i].FixAngle = Eyes[i].Transform.localRotation;
                if (i < dst.Length) dst[i] = Eyes[i].FixAngle;
            }
        }

        /// <summary>LoadAngle: fixAngle := src and the eye local rotations are set at once.</summary>
        public void LoadAngles(Quaternion[] src)
        {
            for (int i = 0; i < Eyes.Length && i < src.Length; i++)
            {
                Eyes[i].FixAngle = src[i];
                if (Eyes[i].Transform != null) Eyes[i].Transform.localRotation = src[i];
            }
        }

        /// <summary>SetAngleHV: overwrite an eye's smoothed angles.</summary>
        public void SetAngleHV(int index, float h, float v)
        {
            if (Eyes == null || index < 0 || index >= Eyes.Length) return;
            Eyes[index].AngleH = h; Eyes[index].AngleV = v;
        }
    }
}
