// The IK part of the HS2 pose import (Phase 3.4 package B): the file's 13 targets (only
// files with enableIK) are converted to HS2Next's semantics on the character they are applied to.
//   1 scale   r = median over the four limbs of |bend goal − root target| / L1 (L1 of this character in root space):
//             r ∈ [0.03, 0.3] → ×10 about the root origin (pose.ikScaleTenth), r ∈ [0.5, 1.6] → ×1, else rejected
//             (pose.ikScaleUnknown: no targets are written; flags are still imported; the raw block stays)
//   2 Body    the file's Body target T_s is where FBBIK puts cf_J_Spine01; HS2Next stores the hips target
//             T_h = T_s − Δ·v (v = hips → Spine01, Δ = the solver's step-1 hips turn), two fixed passes (a: p_Body = T_s,
//             b: p_Body = T_h1); with Body or a leg off Δ = identity (the current hips rotation; pose.ikBodyApprox)
//   3 others  pos × factor; rot as stored for hands / feet, 0 for the others (non-zero values dropped and counted)
//   4 report  scale, body shift, unreachable end targets (predicted reach > 0.99999 on this character), dropped rotations
// PlanIk is pure (no Unity object; Vector3 / Quaternion math only), so the corpus plan (PoseImportTest I6) runs without
// building a character per file. PoseIkBody.Of reads the geometry from a built character.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>The character geometry the IK import needs, in the character root's space (scale included).</summary>
    public sealed class PoseIkBody
    {
        /// <summary>Upper (L1) and lower (L2) segment lengths per limb: 0 left arm, 1 right arm, 2 left leg, 3 right leg.</summary>
        public readonly float[] L1 = new float[4], L2 = new float[4];
        /// <summary>Current root-space positions of cf_J_Hips, cf_J_Spine01, cf_J_LegUp00_L / _R.</summary>
        public Vector3 Hips, Spine01, ThighL, ThighR;
        /// <summary>False when a bone was missing (the import then rejects the scale: no lengths).</summary>
        public bool Complete;

        static readonly string[,] LimbBones =
        {
            { "cf_J_ArmUp00_L", "cf_J_ArmLow01_L", "cf_J_Hand_L" },
            { "cf_J_ArmUp00_R", "cf_J_ArmLow01_R", "cf_J_Hand_R" },
            { "cf_J_LegUp00_L", "cf_J_LegLow01_L", "cf_J_Foot01_L" },
            { "cf_J_LegUp00_R", "cf_J_LegLow01_R", "cf_J_Foot01_R" },
        };

        /// <summary>The geometry of a built character (current pose); null when not built.</summary>
        public static PoseIkBody Of(CharaRealization r)
        {
            if (r == null || r.State != RealizationState.Built || r.Context?.BodyBones == null || !r.Root) return null;
            var bones = r.Context.BodyBones;
            var root = r.Root.transform;
            var b = new PoseIkBody { Complete = true };
            Vector3 P(string name)
            {
                if (bones.TryGetValue(name, out var t) && t) return root.InverseTransformPoint(t.position);
                b.Complete = false;
                return Vector3.zero;
            }
            for (int k = 0; k < 4; k++)
            {
                var a = P(LimbBones[k, 0]); var m = P(LimbBones[k, 1]); var e = P(LimbBones[k, 2]);
                b.L1[k] = (m - a).magnitude;
                b.L2[k] = (e - m).magnitude;
            }
            b.Hips = P("cf_J_Hips");
            b.Spine01 = P("cf_J_Spine01");
            b.ThighL = P("cf_J_LegUp00_L");
            b.ThighR = P("cf_J_LegUp00_R");
            return b;
        }
    }

    /// <summary>The result of the IK conversion of one file.</summary>
    public sealed class PoseIkResult
    {
        /// <summary>1, 10, or 0 = rejected (no targets).</summary>
        public int Scale;
        /// <summary>The median bend-goal ratio r.</summary>
        public float Ratio;
        /// <summary>The 13 model targets (IkTargets.Names order; a target the file lacks is null); null when rejected.</summary>
        public IkTarget[] Targets;
        /// <summary>T_s − T_h (root space); zero when rejected.</summary>
        public Vector3 BodyShift;
        /// <summary>Body conversion with the current hips rotation (Body or a leg off).</summary>
        public bool BodyApprox;
        /// <summary>|Spine01 predicted − T_s| after the two passes (what a third pass would still move).</summary>
        public float BodyPassResidual;
        public readonly List<string> Unreachable = new List<string>();
        public int RotDropped;
    }

    public static partial class PoseImport
    {
        // limbs in PoseIkBody order (left arm, right arm, left leg, right leg): root, bend goal, end target indices
        static readonly int[,] IkLimbs = { { 1, 2, 3 }, { 4, 5, 6 }, { 7, 8, 9 }, { 10, 11, 12 } };
        // activeIK index of each limb's group (Body 0, RightLeg 1, LeftLeg 2, RightArm 3, LeftArm 4)
        static readonly int[] IkLimbGroup = { 4, 3, 2, 1 };
        /// <summary>Targets whose stored rotation is used (hands and feet).</summary>
        public static bool IkUsesRotation(int i) => i == 3 || i == 6 || i == 9 || i == 12;

        /// <summary>Converts the raw targets (index → 9 floats: pos, rot, scale) for <paramref name="body"/>.</summary>
        public static PoseIkResult PlanIk(IReadOnlyDictionary<int, float[]> raw, IReadOnlyList<bool> groups, PoseIkBody body)
        {
            var res = new PoseIkResult();
            if (raw == null || body == null || !body.Complete) return res;
            Vector3 Pos(int i) => raw.TryGetValue(i, out var f) && f != null && f.Length >= 3 ? new Vector3(f[0], f[1], f[2]) : new Vector3(float.NaN, 0, 0);
            bool Has(int i) => raw.TryGetValue(i, out var f) && f != null && f.Length >= 3;

            // 1 scale
            var ratios = new List<float>();
            for (int k = 0; k < 4; k++)
            {
                int ri = IkLimbs[k, 0], bi = IkLimbs[k, 1];
                if (!Has(ri) || !Has(bi) || body.L1[k] <= 0f) continue;
                ratios.Add((Pos(bi) - Pos(ri)).magnitude / body.L1[k]);
            }
            if (ratios.Count == 0) return res;
            ratios.Sort();
            int n = ratios.Count;
            float r = n % 2 == 1 ? ratios[n / 2] : (ratios[n / 2 - 1] + ratios[n / 2]) * 0.5f;
            res.Ratio = r;
            float factor;
            if (r >= 0.03f && r <= 0.3f) factor = 10f;
            else if (r >= 0.5f && r <= 1.6f) factor = 1f;
            else return res;
            res.Scale = (int)factor;

            // 3 targets (pos × factor about the root origin; rot for hands / feet)
            var t = new IkTarget[IkTargets.Count];
            for (int i = 0; i < IkTargets.Count; i++)
            {
                if (!raw.TryGetValue(i, out var f) || f == null || f.Length < 3) continue;
                var p = factor == 1f ? new Vec3(f[0], f[1], f[2]) : new Vec3(f[0] * factor, f[1] * factor, f[2] * factor);
                var rot = f.Length >= 6 ? new Vec3(f[3], f[4], f[5]) : Vec3.Zero;
                if (!IkUsesRotation(i))
                {
                    if (rot.X != 0f || rot.Y != 0f || rot.Z != 0f) res.RotDropped++;
                    rot = Vec3.Zero;
                }
                t[i] = new IkTarget { Pos = p, Rot = rot };
            }

            // 2 Body (Spine01 effector → hips)
            if (t[0] != null)
            {
                var ts = new Vector3(t[0].Pos.X, t[0].Pos.Y, t[0].Pos.Z);
                var v = body.Spine01 - body.Hips;
                bool G(int g) => groups == null || (g < groups.Count && groups[g]);
                bool legs = G(0) && G(1) && G(2) && t[7] != null && t[10] != null;
                Vector3 th;
                if (legs)
                {
                    var tl = new Vector3(t[7].Pos.X, t[7].Pos.Y, t[7].Pos.Z);
                    var tr = new Vector3(t[10].Pos.X, t[10].Pos.Y, t[10].Pos.Z);
                    var d1 = BodyDelta(body.ThighL, body.ThighR, body.Hips, tl, tr, ts);
                    var th1 = ts - d1 * v;
                    var d2 = BodyDelta(body.ThighL, body.ThighR, body.Hips, tl, tr, th1);
                    th = ts - d2 * v;
                    var d3 = BodyDelta(body.ThighL, body.ThighR, body.Hips, tl, tr, th);
                    res.BodyPassResidual = (th + d3 * v - ts).magnitude;
                }
                else
                {
                    th = ts - v;
                    res.BodyApprox = true;
                }
                res.BodyShift = ts - th;
                t[0] = new IkTarget { Pos = new Vec3(th.x, th.y, th.z), Rot = Vec3.Zero };
            }

            // 4 predicted reach on this character
            for (int k = 0; k < 4; k++)
            {
                int ri = IkLimbs[k, 0], ei = IkLimbs[k, 2];
                if (t[ri] == null || t[ei] == null) continue;
                float reach = (V(t[ei].Pos) - V(t[ri].Pos)).magnitude;
                if (reach > 0.99999f * (body.L1[k] + body.L2[k])) res.Unreachable.Add(IkTargets.Names[ei]);
            }
            res.Targets = t;
            return res;
        }

        static Vector3 V(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        /// <summary>
        /// The solver's step-1 hips turn: Δ(F_a → F_t) with F_a = F(thigh_R − thigh_L, hips − mid(thighs))
        /// from the bones and F_t = F(p_RightThigh − p_LeftThigh, p_Body − mid(p_thighs)); identity when a frame is degenerate.
        /// All inputs in one space; the result is a rotation of that space.
        /// </summary>
        public static Quaternion BodyDelta(Vector3 thighL, Vector3 thighR, Vector3 hips, Vector3 pThighL, Vector3 pThighR, Vector3 pBody) =>
            IkSolver.BodyDelta(thighL, thighR, hips, pThighL, pThighR, pBody, out _);   // the solver's own step-1 code

        /// <summary>The report fields and findings of an IK conversion.</summary>
        internal static void ReportIk(PoseImportReport rep, PoseIkResult ik)
        {
            var f = rep.Findings;
            rep.IkPending = false;
            rep.IkScale = ik.Scale;
            rep.IkRatio = ik.Ratio;
            if (ik.Targets == null)
            {
                f.Warning("pose.ikScaleUnknown", "", $"IK targets at an unknown scale (bend-goal ratio {ik.Ratio:G4}): not written; the effective targets show the pose you see; the raw targets stay in hs2.pose");
                return;
            }
            rep.IkApproximated = true;
            rep.IkBodyShift = new Vec3(ik.BodyShift.x, ik.BodyShift.y, ik.BodyShift.z);
            rep.IkUnreachable = ik.Unreachable.ToList();
            rep.IkRotDropped = ik.RotDropped;
            rep.IkBodyPassResidual = ik.BodyPassResidual;
            if (ik.Scale == 10) f.Info("pose.ikScaleTenth", "", $"IK targets stored at 1/10 scale (bend-goal ratio {ik.Ratio:G4}): ×10 about the root origin");
            if (ik.BodyApprox) f.Info("pose.ikBodyApprox", "", "Body target converted with the current hips rotation (Body or a leg group off)");
            f.Info("pose.ikConverted", "", $"IK targets converted: ×{ik.Scale}, body shift {ik.BodyShift.magnitude:G4} ({ik.BodyShift.x:G4}, {ik.BodyShift.y:G4}, {ik.BodyShift.z:G4})" +
                   (ik.Unreachable.Count > 0 ? ", unreachable " + string.Join(", ", ik.Unreachable) : "") +
                   (ik.RotDropped > 0 ? $", {ik.RotDropped} unused rotations dropped" : ""));
        }

        /// <summary>The conversion's members of the hs2.pose IK block (scale, bodyShift; null when not converted).</summary>
        internal static void SetIkConversion(PoseProvenance.PoseIkBlock block, PoseIkResult ik)
        {
            if (block == null) return;
            block.Scale = ik != null && ik.Targets != null ? ik.Scale : (int?)null;
            block.BodyShift = ik != null && ik.Targets != null ? new[] { ik.BodyShift.x, ik.BodyShift.y, ik.BodyShift.z } : null;
        }

        /// <summary>The raw targets of a pose file (index → 9 floats; duplicates: the first, as Studio).</summary>
        public static SortedDictionary<int, float[]> RawIk(HS2Next.Import.Pose.PoseFile pose)
        {
            var m = new SortedDictionary<int, float[]>();
            if (pose == null) return m;
            foreach (var e in pose.IK) if (!m.ContainsKey(e.Key)) m[e.Key] = Nine(e);
            return m;
        }
    }
}
