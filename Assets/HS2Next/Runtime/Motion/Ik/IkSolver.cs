// HS2Next's analytic IK solver. Deterministic, one pass, single precision Unity
// math in a fixed operation order, no frame history (the latch lives in IkStage). Steps:
//   1. body    (Body) cf_J_Hips onto the Body target; with both legs applied first turned by Δ(F_a → F_t), the frame of the
//              thigh line and the hips-above-thighs direction (bones → targets)
//   2. torso   (Body + both arms) three fixed passes (spec: two; see TorsoPasses): Spine01 turned by half of Δ(chest frame → shoulder-target frame), then
//              Spine02 by the rest; Spine03 untouched (K5). Clavicles (each applied arm): swing aimed at the shoulder target
//   3. limbs   (left leg, right leg, left arm, right arm) two-bone solve: reach clamped to [d_min, 0.99999 (L1 + L2)]
//              (narrowed by the rotation-limit option), the bend goal as the pole (fallbacks: the clip's bend plane, the
//              rest bend direction), swing-only rotations (the clip's roll stays)
//   4. ends    hands / feet take the target's world rotation (root.rotation · Euler(rot)); fingers and toes keep theirs
// Every rotation is written as the world rotation of the named transform; positions only for the pelvis.
using UnityEngine;

namespace HS2Next.Motion.Ik
{
    public static class IkSolver
    {
        /// <summary>Degenerate-vector threshold of the frame helper.</summary>
        public const float FrameEps = 1e-6f;
        /// <summary>Reach clamp factor of d_max.</summary>
        public const float ReachClamp = 0.99999f;
        /// <summary>Bend-plane twist fade by the pole's strength = its distance from the root–goal line / (L1 + L2): no twist
        /// below PoleFadeMin (a bend goal on the line, e.g. copied from a straight limb), full from PoleFadeMax
        /// (IkLimbOrientation.BendPlane).</summary>
        public static float PoleFadeMin = 0.005f, PoleFadeMax = 0.02f;
        /// <summary>Clavicle aim guard (fix round 2): the aim fades in between ClavicleFadeMin and
        /// ClavicleFadeMax x the current clavicle length (distance clavicle joint → shoulder target / clavicle → upper-arm
        /// root) and can be capped at ClavicleMaxAngle degrees (off: a 45° cap worsened IK8). Chosen against HS2's recordings
        /// (IkSolverTest RC). Settable for measurements.</summary>
        public static float ClavicleFadeMin = 0.5f, ClavicleFadeMax = 0.7f, ClavicleMaxAngle = 180f;
        /// <summary>Torso passes (fixed). Two were specified; three are used because two leave up to 1.1° on BR-Chan for
        /// target frames 45° from the clip and 2.4° on the library's IK poses (U6), three leave &lt; 0.2° (3.4 impl note).
        /// Settable for measurements only.</summary>
        public static int TorsoPasses = 3;

        static readonly Vector3[] world = new Vector3[IkGroupInfo.TargetCount];   // main thread only
        static readonly IkGroup[] LimbOrder = { IkGroup.LeftLeg, IkGroup.RightLeg, IkGroup.LeftArm, IkGroup.RightArm };
        static readonly IkTargetId[] RootTarget = { IkTargetId.Body, IkTargetId.RightThigh, IkTargetId.LeftThigh, IkTargetId.RightShoulder, IkTargetId.LeftShoulder };
        static readonly IkTargetId[] BendTarget = { IkTargetId.Body, IkTargetId.RightLegChain, IkTargetId.LeftLegChain, IkTargetId.RightArmChain, IkTargetId.LeftArmChain };
        static readonly IkTargetId[] EndTarget = { IkTargetId.Body, IkTargetId.RightFoot, IkTargetId.LeftFoot, IkTargetId.RightHand, IkTargetId.LeftHand };

        /// <summary>The root / bend-goal / end target of a limb group.</summary>
        public static IkTargetId RootTargetOf(IkGroup g) => RootTarget[(int)g];
        public static IkTargetId BendTargetOf(IkGroup g) => BendTarget[(int)g];
        public static IkTargetId EndTargetOf(IkGroup g) => EndTarget[(int)g];

        /// <summary>
        /// Solves one step. <paramref name="applied"/>[g]: the group runs (applied, bones present, targets present).
        /// <paramref name="targets"/>: root-space targets indexed by IkTargetId. <paramref name="owned"/> (IkBones.SlotCount
        /// entries) receives true for every slot written. <paramref name="info"/> is cleared and filled (Valid, residuals,
        /// clamps, poles, torso).
        /// </summary>
        public static void Solve(IkBones b, Transform root, IkTargetValue[] targets, bool[] applied, IkSolveInfo info, bool[] owned)
        {
            info.Clear();
            var w = world;
            for (int i = 0; i < w.Length; i++) w[i] = root ? root.TransformPoint(targets[i].Pos) : targets[i].Pos;
            bool body = applied[(int)IkGroup.Body];
            bool ll = applied[(int)IkGroup.LeftLeg], rl = applied[(int)IkGroup.RightLeg];
            bool la = applied[(int)IkGroup.LeftArm], ra = applied[(int)IkGroup.RightArm];
            info.Valid = body || ll || rl || la || ra;
            if (!info.Valid) return;

            // 1. body
            if (body)
            {
                var hips = b.Hips;
                Vector3 pBody = w[(int)IkTargetId.Body];
                if (ll && rl)
                {
                    var d = BodyDelta(b.Limbs[(int)IkGroup.LeftLeg].Root.position, b.Limbs[(int)IkGroup.RightLeg].Root.position, hips.position,
                        w[(int)IkTargetId.LeftThigh], w[(int)IkTargetId.RightThigh], pBody, out bool degenerate);
                    if (!degenerate) hips.rotation = d * hips.rotation;
                    else info.BodyDegenerate = true;
                }
                hips.position = pBody;
                owned[IkBones.SlotHips] = true;
            }

            // 2. torso, clavicles
            if (body && la && ra)
            {
                info.TorsoRan = true;
                Transform upL = b.Limbs[(int)IkGroup.LeftArm].Root, upR = b.Limbs[(int)IkGroup.RightArm].Root;
                Vector3 s0 = b.Spine01.position;
                Vector3 pl = w[(int)IkTargetId.LeftShoulder], pr = w[(int)IkTargetId.RightShoulder];
                bool tOk = Frame(pr - pl, (pl + pr) * 0.5f - s0, out var ft);
                for (int k = 0; k < TorsoPasses && tOk; k++)
                {
                    if (!ChestFrame(upL, upR, s0, out var fa)) { info.TorsoDegenerate = true; break; }
                    var r = ft * Quaternion.Inverse(fa);
                    b.Spine01.rotation = Quaternion.Slerp(Quaternion.identity, r, 0.5f) * b.Spine01.rotation;
                    if (!ChestFrame(upL, upR, s0, out fa)) { info.TorsoDegenerate = true; break; }
                    b.Spine02.rotation = (ft * Quaternion.Inverse(fa)) * b.Spine02.rotation;
                }
                if (!tOk) info.TorsoDegenerate = true;
                if (tOk && ChestFrame(upL, upR, s0, out var fEnd)) info.TorsoResidual = AngleBetween(ft, fEnd);
                owned[IkBones.SlotSpine01] = true;
                owned[IkBones.SlotSpine02] = true;
            }
            for (int a = (int)IkGroup.RightArm; a <= (int)IkGroup.LeftArm; a++)
            {
                if (!applied[a]) continue;
                var l = b.Limbs[a];
                Vector3 c = l.Clavicle.position;
                Vector3 va = l.Root.position - c, vt = w[(int)RootTarget[a]] - c;
                float lenA = va.magnitude, lenT = vt.magnitude;
                if (lenT >= FrameEps && lenA >= FrameEps)
                {
                    var aim = Quaternion.FromToRotation(va, vt);
                    // guard: a shoulder target close to the clavicle joint gives no usable direction; fade the aim out
                    // below ClavicleFadeMax x the clavicle length and cap the turn at ClavicleMaxAngle
                    float wgt = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ClavicleFadeMin, ClavicleFadeMax, lenT / lenA));
                    float ang = AngleBetween(Quaternion.identity, aim);
                    if (ang * wgt > ClavicleMaxAngle) wgt = ClavicleMaxAngle / ang;
                    if (wgt < 1f) aim = Quaternion.Slerp(Quaternion.identity, aim, wgt);
                    if (wgt > 0f) l.Clavicle.rotation = aim * l.Clavicle.rotation;
                }
                owned[IkBones.SlotsOf((IkGroup)a)[0]] = true;
            }

            // 3. limbs, 4. end rotations
            Quaternion rootRot = root ? root.rotation : Quaternion.identity;
            foreach (var g in LimbOrder)
            {
                int gi = (int)g;
                if (!applied[gi]) continue;
                var l = b.Limbs[gi];
                int e = (int)EndTarget[gi];
                info.Pole[gi] = SolveLimb(l, w[e], w[(int)BendTarget[gi]], out info.Clamped[e]);
                l.End.rotation = rootRot * Quaternion.Euler(targets[e].Rot);
                var slots = IkBones.SlotsOf(g);
                for (int k = 0; k < slots.Length; k++) owned[slots[k]] = true;
            }

            // residuals
            if (body) info.Residual[(int)IkTargetId.Body] = (b.Hips.position - w[(int)IkTargetId.Body]).magnitude;
            for (int gi = 1; gi < IkGroupInfo.Count; gi++)
            {
                if (!applied[gi]) continue;
                var l = b.Limbs[gi];
                info.Residual[(int)RootTarget[gi]] = (l.Root.position - w[(int)RootTarget[gi]]).magnitude;
                info.Residual[(int)EndTarget[gi]] = (l.End.position - w[(int)EndTarget[gi]]).magnitude;
            }
        }

        /// <summary>The two-bone solve of one limb toward world goal <paramref name="g"/> with pole point
        /// <paramref name="p"/>. Returns the pole source used.</summary>
        public static IkPoleSource SolveLimb(IkLimb l, Vector3 g, Vector3 p, out bool clamped)
        {
            Vector3 a = l.Root.position, m0 = l.Mid.position, e0 = l.End.position;
            float l1 = (m0 - a).magnitude, l2 = (e0 - m0).magnitude, sum = l1 + l2;
            Vector3 ga = g - a;
            float dist = ga.magnitude;
            Vector3 u = dist >= FrameEps ? ga / dist : Normalised(e0 - a);
            float dMax = ReachClamp * sum;
            float dMin = Mathf.Abs(l1 - l2) + 1e-4f * sum;
            if (IkOptions.RotationLimits)
            {
                float th = Mathf.Clamp(IkOptions.LimitMinInterior, 0f, 179f) * Mathf.Deg2Rad;
                float dl = Mathf.Sqrt(Mathf.Max(l1 * l1 + l2 * l2 - 2f * l1 * l2 * Mathf.Cos(th), 0f));
                if (dl > dMin) dMin = Mathf.Min(dl, dMax);
            }
            float d = dist;
            clamped = false;
            if (d > dMax) { d = dMax; clamped = true; }
            if (d < dMin) { d = dMin; clamped = true; }
            Vector3 g2 = a + d * u;

            // pole
            var src = IkPoleSource.Target;
            Vector3 v = Reject(p - a, u);
            float eps = 1e-5f * sum;
            if (v.magnitude < eps)
            {
                src = IkPoleSource.Clip;
                v = Reject(m0 - a, u);
                if (v.magnitude < eps)
                {
                    src = IkPoleSource.Rest;
                    var parent = l.Root.parent;
                    v = Reject((parent ? parent.rotation : Quaternion.identity) * l.RestBend, u);
                }
            }
            Vector3 vh = Normalised(v);

            // mid joint by the law of cosines on the pole's side
            float ca = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(l1 * l1 - ca * ca, 0f));
            Vector3 m = a + ca * u + h * vh;

            // swing (the input's roll stays) ...
            if ((m0 - a).sqrMagnitude > 0f && (m - a).sqrMagnitude > 0f)
                l.Root.rotation = Quaternion.FromToRotation(m0 - a, m - a) * l.Root.rotation;
            // ... then, bend-plane: twist the upper bone about its axis so its hinge side points to the pole side
            if (l.HasBendFrame && IkOptions.LimbOrientation == IkLimbOrientation.BendPlane && vh.sqrMagnitude > 0f)
            {
                float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PoleFadeMin, PoleFadeMax, v.magnitude / sum));
                Vector3 ax = (m - a).normalized;
                Vector3 cur = l.Root.rotation * l.UpBend;
                cur -= Vector3.Dot(cur, ax) * ax;
                Vector3 want = vh - Vector3.Dot(vh, ax) * ax;
                if (w > 0f && cur.sqrMagnitude > 1e-12f && want.sqrMagnitude > 1e-12f)
                    l.Root.rotation = Quaternion.AngleAxis(w * Vector3.SignedAngle(cur, want, ax), ax) * l.Root.rotation;
            }
            // the lower bone swings onto the goal; a second FromTo removes the float error of a large (near-antiparallel)
            // first swing, which the bend-plane twist can produce
            Vector3 mid = l.Mid.position;
            float tol = 1e-6f * sum;
            for (int k = 0; k < 2; k++)
            {
                Vector3 e1 = l.End.position;
                if (k == 1 && (e1 - g2).sqrMagnitude <= tol * tol) break;
                if ((e1 - mid).sqrMagnitude > 0f && (g2 - mid).sqrMagnitude > 0f)
                    l.Mid.rotation = Quaternion.FromToRotation(e1 - mid, g2 - mid) * l.Mid.rotation;
            }
            return src;
        }

        /// <summary>
        /// The body step's hips turn Δ(F_a → F_t): F_a = F(thighR − thighL, hips − mid(thighs)) from the bones,
        /// F_t = F(pThighR − pThighL, pBody − mid(pThighs)) from the targets; all inputs in one space (the result is a
        /// rotation in that space). Identity with <paramref name="degenerate"/> when either frame is degenerate. The solver
        /// applies it as hips.rotation := Δ · hips.rotation (world), then hips.position := pBody.
        /// </summary>
        public static Quaternion BodyDelta(Vector3 thighL, Vector3 thighR, Vector3 hips, Vector3 pThighL, Vector3 pThighR, Vector3 pBody, out bool degenerate)
        {
            if (Frame(thighR - thighL, hips - (thighL + thighR) * 0.5f, out var fa) && Frame(pThighR - pThighL, pBody - (pThighL + pThighR) * 0.5f, out var ft))
            {
                degenerate = false;
                return ft * Quaternion.Inverse(fa);
            }
            degenerate = true;
            return Quaternion.identity;
        }

        /// <summary>Frame helper F(x, y) as the rotation taking the unit axes (x, y, z) onto (e1, e2, e3).
        /// False when degenerate (|x| or the part of y orthogonal to x below 1e-6).</summary>
        public static bool Frame(Vector3 x, Vector3 y, out Quaternion q)
        {
            q = Quaternion.identity;
            float lx = x.magnitude;
            if (lx < FrameEps) return false;
            Vector3 e1 = x / lx;
            Vector3 y2 = y - Vector3.Dot(y, e1) * e1;
            float ly = y2.magnitude;
            if (ly < FrameEps) return false;
            Vector3 e2 = y2 / ly;
            Vector3 e3 = Vector3.Cross(e1, e2);
            q = Quaternion.LookRotation(e3, e2);
            return true;
        }

        static bool ChestFrame(Transform upL, Transform upR, Vector3 s0, out Quaternion q)
        {
            Vector3 l = upL.position, r = upR.position;
            return Frame(r - l, (l + r) * 0.5f - s0, out q);
        }

        /// <summary>Angle between two rotations in degrees, accurate near 0 (Quaternion.Angle returns 0 below about 0.08°).</summary>
        public static float AngleBetween(Quaternion a, Quaternion b)
        {
            var d = Quaternion.Inverse(a) * b;
            double v = System.Math.Sqrt((double)d.x * d.x + (double)d.y * d.y + (double)d.z * d.z);
            return (float)(2 * System.Math.Atan2(v, System.Math.Abs((double)d.w)) * 180 / System.Math.PI);
        }

        static Vector3 Reject(Vector3 v, Vector3 u) => v - Vector3.Dot(v, u) * u;

        static Vector3 Normalised(Vector3 v)
        {
            float m = v.magnitude;
            return m > 0f ? v / m : Vector3.zero;
        }
    }
}
