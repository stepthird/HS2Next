// Heels (after 1.0.0; the Heelz plugin's behaviour in the Studio): while the worn female
// shoes have heel data (Content/HeelsData) and the shoes are on, the body is lifted and the foot joints are posed.
//   HeelsLift  phase 110 (after the shape stage, before the IK preparation, so the pose probes see it): cf_N_height
//              localPosition = (0, root.y, 0); when the heels end, the value before the lift once (the plugin writes
//              (0, 0, 0); the build's value is that, up to the sign of a zero). Applied once when attached (before the
//              dynamic bones build their chains). Unlift() takes the lift off until the next step (before the shape bones
//              are captured again, so the capture holds the body's own value).
//   HeelsFoot  phase 210 (after IK, before the neck look and FK: the game poses the feet after its IK solve or, without IK,
//              before its FK): per joint in the game's order (ankle and foot left, ankle and foot right, toes left, toes
//              right): "fixed" = the parent's world rotation; position += move; scale = the heel's scale; then rolls about
//              the joint's own right, up and forward axes (each angle clamped to [min, max] when min or max is not zero).
//              The rolls are relative to this step's pose: a joint that still holds this stage's last write (or what the
//              card's bone modifiers make of it) goes back to its previous base first, so a still pose does not accumulate.
//              When the heels end, the joints that still hold that write go back to their base once.
using System.Collections.Generic;
using HS2Next.Chara;
using HS2Next.Content;
using UnityEngine;

namespace HS2Next.Motion.Heels
{
    /// <summary>The heel state of one character: the worn shoes' heel data and whether the shoes are on.</summary>
    public sealed class HeelsState
    {
        readonly CharaContext ctx;
        ListEntry row;
        HeelsData.Heel heel;
        bool resolved;

        /// <summary>The clothes state of the shoes: on (set by the scene's clothes state; true without a scene).</summary>
        public bool ShoesOn = true;

        public HeelsState(CharaContext ctx) { this.ctx = ctx; }

        /// <summary>The heel data of the worn shoes (looked up again when the shoe row changes), or null.</summary>
        public HeelsData.Heel Heel
        {
            get
            {
                var r = ctx?.ClothesEntries != null && ctx.ClothesEntries.Length > 7 ? ctx.ClothesEntries[7] : null;
                if (!resolved || r != row) { row = r; heel = HeelsData.For(r); resolved = true; }
                return heel;
            }
        }

        /// <summary>The heel data in effect: the worn shoes' while they are on, else null.</summary>
        public HeelsData.Heel Active => ShoesOn ? Heel : null;
    }

    public static class HeelsSetup
    {
        public static void Attach(CharaContext ctx)
        {
            if (ctx?.Motion == null || ctx.BodyBones == null) return;
            Transform B(string n) => ctx.BodyBones.TryGetValue(n, out var t) ? t : null;
            var state = new HeelsState(ctx);
            var lift = new HeelsLift(state, B("cf_N_height"));
            ctx.Motion.Add(lift);
            lift.Step(default);   // the body stands lifted before the stages that capture the pose are attached
            ctx.Motion.Add(new HeelsFoot(state, ctx, new[] { B("cf_J_Foot01_L"), B("cf_J_Foot02_L"), B("cf_J_Foot01_R"), B("cf_J_Foot02_R"), B("cf_J_Toes01_L"), B("cf_J_Toes01_R") }));
        }
    }

    public sealed class HeelsLift : IMotionStage
    {
        public MotionPhase Phase => (MotionPhase)110;
        public string Name => "heels.lift";
        public bool Enabled { get; set; } = true;
        public readonly HeelsState State;
        /// <summary>cf_N_height.</summary>
        public readonly Transform Height;
        bool lifted;
        Vector3 basePos;

        public HeelsLift(HeelsState state, Transform height) { State = state; Height = height; }

        /// <summary>True while the stage keeps the body lifted.</summary>
        public bool Lifted => lifted;

        public void Step(in MotionFrame frame) => Apply();

        /// <summary>The lift of the current state now; true when it changed cf_N_height (lifted, lowered or another height).</summary>
        public bool Apply()
        {
            if (!Height) return false;
            var before = Height.localPosition;
            var h = State.Active;
            if (h != null)
            {
                if (!lifted) basePos = Height.localPosition;
                Height.localPosition = new Vector3(0f, h.Root.y, 0f);
                lifted = true;
            }
            else if (lifted) { Height.localPosition = basePos; lifted = false; }
            return Height.localPosition != before;
        }

        /// <summary>The lift off until the next step or <see cref="Apply"/> (around a capture of the shape bones).</summary>
        public void Unlift() { if (lifted && Height) Height.localPosition = basePos; }

        public void Reset() { }
    }

    public sealed class HeelsFoot : IMotionStage
    {
        public MotionPhase Phase => (MotionPhase)210;
        public string Name => "heels.foot";
        public bool Enabled { get; set; } = true;
        public readonly HeelsState State;
        /// <summary>Ankle left, foot left, ankle right, foot right, toes left, toes right.</summary>
        public readonly Transform[] Joints;
        readonly CharaContext ctx;
        readonly Vector3[] bp = new Vector3[6], bs = new Vector3[6], wp = new Vector3[6], ws = new Vector3[6];
        readonly Quaternion[] br = new Quaternion[6], wr = new Quaternion[6];
        bool written;

        public HeelsFoot(HeelsState state, CharaContext ctx, Transform[] joints) { State = state; this.ctx = ctx; Joints = joints; }

        /// <summary>True while the joints hold this stage's write.</summary>
        public bool Written => written;

        public void Step(in MotionFrame frame)
        {
            var h = State.Active;
            if (h == null) { Release(); return; }
            for (int i = 0; i < 6; i++)
            {
                var t = Joints[i];
                if (!t) continue;
                if (written && Unchanged(i, t)) { t.localPosition = bp[i]; t.localRotation = br[i]; t.localScale = bs[i]; }
                else { bp[i] = t.localPosition; br[i] = t.localRotation; bs[i] = t.localScale; }
            }
            Pose(Joints[0], h.Ankle); Pose(Joints[1], h.Foot);
            Pose(Joints[2], h.Ankle); Pose(Joints[3], h.Foot);
            Pose(Joints[4], h.Toes); Pose(Joints[5], h.Toes);
            for (int i = 0; i < 6; i++)
            {
                var t = Joints[i];
                if (!t) continue;
                wp[i] = t.localPosition; wr[i] = t.localRotation; ws[i] = t.localScale;
            }
            written = true;
        }

        /// <summary>One joint posed by its heel values (the game's order of operations).</summary>
        public static void Pose(Transform t, HeelsData.Joint j)
        {
            if (!t) return;
            if (j.Fixed && t.parent) t.eulerAngles = t.parent.eulerAngles;
            t.localPosition += j.Move;
            t.localScale = j.Scale;
            var at = t.position;
            bool clamp = j.RollMin != Vector3.zero || j.RollMax != Vector3.zero;
            t.RotateAround(at, t.right, clamp ? Mathf.Clamp(j.Roll.x, j.RollMin.x, j.RollMax.x) : j.Roll.x);
            t.RotateAround(at, t.up, clamp ? Mathf.Clamp(j.Roll.y, j.RollMin.y, j.RollMax.y) : j.Roll.y);
            t.RotateAround(at, t.forward, clamp ? Mathf.Clamp(j.Roll.z, j.RollMin.z, j.RollMax.z) : j.Roll.z);
        }

        // nothing rewrote the joint since the last step: it holds that write, or what the bone modifiers made of it
        bool Unchanged(int i, Transform t)
        {
            if (Near(t, wp[i], wr[i], ws[i])) return true;
            var m = ctx?.Bones;
            return m && m.Predict(t, wp[i], wr[i], ws[i], out var p, out var r, out var s) && Near(t, p, r, s);
        }

        static bool Near(Transform t, Vector3 p, Quaternion r, Vector3 s)
        {
            const float E = 1e-6f;
            var lp = t.localPosition; var ls = t.localScale; var lr = t.localRotation;
            bool same = Mathf.Abs(lp.x - p.x) <= E && Mathf.Abs(lp.y - p.y) <= E && Mathf.Abs(lp.z - p.z) <= E
                        && Mathf.Abs(ls.x - s.x) <= E && Mathf.Abs(ls.y - s.y) <= E && Mathf.Abs(ls.z - s.z) <= E;
            if (!same) return false;
            bool q = Mathf.Abs(lr.x - r.x) <= E && Mathf.Abs(lr.y - r.y) <= E && Mathf.Abs(lr.z - r.z) <= E && Mathf.Abs(lr.w - r.w) <= E;
            bool nq = Mathf.Abs(lr.x + r.x) <= E && Mathf.Abs(lr.y + r.y) <= E && Mathf.Abs(lr.z + r.z) <= E && Mathf.Abs(lr.w + r.w) <= E;
            return q || nq;
        }

        /// <summary>The joints that still hold this stage's write back to their base, once.</summary>
        public void Release()
        {
            if (!written) return;
            for (int i = 0; i < 6; i++)
            {
                var t = Joints[i];
                if (t && Unchanged(i, t)) { t.localPosition = bp[i]; t.localRotation = br[i]; t.localScale = bs[i]; }
            }
            written = false;
        }

        public void Reset() => written = false;
    }
}
