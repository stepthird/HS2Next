// Joint correction ("CharaUtils.Expression" equivalent, Studio 関節補正) as a motion stage.
//
// binding, categories and Studio toggles (all OFF by
// default), the per-frame algorithm (rows strictly in table order, LookAt part then Correct
// part; Euler via JointCorrectionMath, not Unity's eulerAngles), interactions and quirks
// (reproduced by default; deliberate fixes behind flags). Runs at MotionPhase.Correct (L8 of
// 4), after FK / IK / look, before the bust / hip chains and dynamic bones.
//
// The correction is pose-driven: Step ignores the frame's dt and evaluates every step, also while
// paused (Step(0)), exactly like Expression.LateUpdate runs every frame regardless of time.
// No allocation in Step / Refresh.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Motion.Corrective
{
    public sealed class JointCorrectionStage : IMotionStage
    {
        // ---------------------------------------------------------------- flags

        /// <summary>
        /// Deliberate fix (upper-arm Euler wrap): the Correct part decomposes with the Euler
        /// solution of smaller outer angles (JointCorrectionMath.Decompose(q, o, true)), so lowering the arm
        /// past 90° about its Z no longer flips the twist correction (E7: -19.8° at z = 85 and at z = 95
        /// instead of -19.8° / -99.0°). Identical to the game whenever the outer angles are within ±90°.
        /// Default false (faithful).
        /// </summary>
        public bool FixEulerWrap;

        /// <summary>
        /// Deliberate fix (trace-only LookAt quaternion): convert the aim frame with the
        /// largest-diagonal method instead of the trace formula, and leave the bone untouched when the aim
        /// direction is degenerate (target on the look bone, or aim parallel to the up vector) instead of
        /// writing the formula's arbitrary result. Default false (faithful).
        /// </summary>
        public bool RobustLookAt;

        /// <summary>
        /// HS2Next addition (not a game behaviour by itself): when a row is switched off, put its driven bone
        /// back to the rotation it had before the correction, unless something rewrote the bone since the
        /// last correction. In the game the Animator rewrites every corrected bone each frame, so switching a
        /// category off shows the clip value at once; HS2Next has no per-frame clip writer yet (the Animator is
        /// off, rest pose), and without this the bones would keep the last corrected value. When a writer
        /// rewrites the bones every frame this flag changes nothing. Default true.
        /// </summary>
        public bool RestoreOnDisable = true;

        // ---------------------------------------------------------------- IMotionStage

        public MotionPhase Phase => MotionPhase.Correct;
        public string Name => "correct";
        /// <summary>Master switch (Expression.enable / ChaInfo.enableExpression; Studio never sets it false).</summary>
        public bool Enabled { get; set; } = true;

        // ---------------------------------------------------------------- data

        sealed class BoundRow
        {
            public JointCorrectionRow Row;
            public Transform Look, Target, Up, Correct, Reference;
            public bool LookActive, CorrectActive;
            public int LookSlot = -1, CorrectSlot = -1;
            public bool Enabled;
        }

        public JointCorrectionTable Table { get; }
        readonly BoundRow[] rows;
        readonly bool[] categories = new bool[JointCorrectionTable.CategoryCount];

        // Driven bones (unique) and the restore bookkeeping.
        readonly Transform[] driven;
        readonly Quaternion[] input, written;
        readonly bool[] hasWritten, active, wasActive;
        int enabledRows;

        /// <summary>Names of the table that did not resolve (empty for the vanilla skeleton).</summary>
        public readonly List<string> Unbound = new List<string>();
        /// <summary>Bound driven bones (look bones of LookAt parts with a target, correct bones of Correct parts with a reference).</summary>
        public int DrivenCount => driven.Length;
        /// <summary>Distinct bound source bones (targets, up bones, references).</summary>
        public int SourceCount { get; }
        /// <summary>Rows whose every part is bound.</summary>
        public int BoundRows { get; }
        /// <summary>Evaluations done (Step + Refresh), for tests.</summary>
        public int Evaluations { get; private set; }

        /// <summary>Binds the table's bones through resolve (name -> Transform or null; resolve inside p_cf_anim, first name wins).</summary>
        public JointCorrectionStage(JointCorrectionTable table, Func<string, Transform> resolve)
        {
            Table = table ?? throw new ArgumentNullException(nameof(table));
            if (resolve == null) throw new ArgumentNullException(nameof(resolve));
            var slots = new List<Transform>();
            var sources = new HashSet<Transform>();
            int Slot(Transform t)
            {
                int i = slots.IndexOf(t);
                if (i >= 0) return i;
                slots.Add(t);
                return slots.Count - 1;
            }
            Transform R(string name)
            {
                if (name == null) return null;
                var t = resolve(name);
                if (t == null && !Unbound.Contains(name)) Unbound.Add(name);
                return t;
            }

            rows = new BoundRow[table.Rows.Count];
            int bound = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                var r = table.Rows[i];
                var b = new BoundRow { Row = r };
                bool ok = true;
                if (r.HasLookAt)
                {
                    b.Look = R(r.LookBone);
                    b.Target = R(r.TargetBone);
                    b.Up = R(r.UpBone);
                    b.LookActive = b.Look != null && b.Target != null;
                    if (b.LookActive)
                    {
                        b.LookSlot = Slot(b.Look);
                        sources.Add(b.Target);
                        if (b.Up != null) sources.Add(b.Up);
                    }
                    ok &= b.LookActive && (r.UpBone == null || b.Up != null);
                }
                if (r.HasCorrect)
                {
                    b.Correct = R(r.CorrectBone);
                    b.Reference = R(r.ReferenceBone);
                    b.CorrectActive = b.Correct != null && b.Reference != null;
                    if (b.CorrectActive) { b.CorrectSlot = Slot(b.Correct); sources.Add(b.Reference); }
                    ok &= b.CorrectActive;
                }
                if (ok) bound++;
                rows[i] = b;
            }
            BoundRows = bound;
            SourceCount = sources.Count;
            driven = slots.ToArray();
            input = new Quaternion[driven.Length];
            written = new Quaternion[driven.Length];
            hasWritten = new bool[driven.Length];
            active = new bool[driven.Length];
            wasActive = new bool[driven.Length];
        }

        // ---------------------------------------------------------------- switches

        /// <summary>Studio EnableExpressionCategory(c, on): sets every row of category c.</summary>
        public void EnableCategory(int category, bool on)
        {
            if (category < 0 || category >= categories.Length) return;
            categories[category] = on;
            for (int i = 0; i < rows.Length; i++) if (rows[i].Row.Category == category) rows[i].Enabled = on;
            UpdateActive();
        }

        /// <summary>The last state set by EnableCategory (Studio OICharInfo.expression[c]).</summary>
        public bool IsCategoryEnabled(int category) => category >= 0 && category < categories.Length && categories[category];

        /// <summary>EnableIndex(i, on): one row (does not change the category state).</summary>
        public void EnableIndex(int row, bool on)
        {
            if (row < 0 || row >= rows.Length) return;
            rows[row].Enabled = on;
            UpdateActive();
        }

        public bool IsRowEnabled(int row) => row >= 0 && row < rows.Length && rows[row].Enabled;

        /// <summary>Every category on / off.</summary>
        public void EnableAll(bool on) { for (int c = 0; c < categories.Length; c++) EnableCategory(c, on); }

        /// <summary>The driven bone of a row (look bone of a LookAt row, else the correct bone), null when unbound.</summary>
        public Transform DrivenOf(int row) => row < 0 || row >= rows.Length ? null : rows[row].LookActive ? rows[row].Look : rows[row].CorrectActive ? rows[row].Correct : null;

        // Recomputes which driven bones some enabled row writes; bones that just lost every row are restored.
        void UpdateActive()
        {
            enabledRows = 0;
            for (int j = 0; j < active.Length; j++) { wasActive[j] = active[j]; active[j] = false; }
            for (int i = 0; i < rows.Length; i++)
            {
                var b = rows[i];
                if (!b.Enabled) continue;
                if (b.LookActive || b.CorrectActive) enabledRows++;
                if (b.LookSlot >= 0) active[b.LookSlot] = true;
                if (b.CorrectSlot >= 0) active[b.CorrectSlot] = true;
            }
            for (int j = 0; j < active.Length; j++)
            {
                if (active[j] || !wasActive[j] || !hasWritten[j]) continue;
                var t = driven[j];
                if (RestoreOnDisable && t && JointCorrectionMath.Same(t.localRotation, written[j])) t.localRotation = input[j];
                hasWritten[j] = false;
            }
        }

        // ---------------------------------------------------------------- stage

        public void Step(in MotionFrame frame) => Evaluate();

        /// <summary>Nothing to drop: the correction keeps no history (each evaluation reads the current pose).</summary>
        public void Reset() { }

        /// <summary>One evaluation now (after a pose edit outside the step loop). No-op while the master switch is off.</summary>
        public void Refresh() { if (Enabled) Evaluate(); }

        void Evaluate()
        {
            if (enabledRows == 0) return;
            // Remember the uncorrected value of every driven bone that something else wrote since our last write.
            for (int j = 0; j < driven.Length; j++)
            {
                if (!active[j]) continue;
                var q = driven[j].localRotation;
                if (!hasWritten[j] || !JointCorrectionMath.Same(q, written[j])) input[j] = q;
            }
            for (int i = 0; i < rows.Length; i++)
            {
                var b = rows[i];
                if (!b.Enabled) continue;
                if (b.LookActive) ApplyLookAt(b);
                if (b.CorrectActive) ApplyCorrect(b);
            }
            for (int j = 0; j < driven.Length; j++)
            {
                if (!active[j]) continue;
                written[j] = driven[j].localRotation;
                hasWritten[j] = true;
            }
            Evaluations++;
        }

        //
        void ApplyCorrect(BoundRow b)
        {
            var r = b.Row;
            var C = b.Correct;
            var refRot = b.Reference.localRotation;
            if (r.Calc == JcCalc.Quaternion)
            {
                var q = C.localRotation;
                if (r.Use[0]) q.x = refRot.x * (r.Min[0] + r.Max[0]) * 0.5f;
                if (r.Use[1]) q.y = refRot.y * (r.Min[1] + r.Max[1]) * 0.5f;
                if (r.Use[2]) q.z = refRot.z * (r.Min[2] + r.Max[2]) * 0.5f;
                C.localRotation = q;   // not renormalised (as the game)
                return;
            }
            var o = r.CorrectOrder;
            bool fix = FixEulerWrap;
            var c = JointCorrectionMath.Decompose(C.localRotation, o, fix);
            var rr = JointCorrectionMath.Decompose(refRot, o, fix);
            float k = r.CharmRate;
            bool charm = k != 0f;
            var h = charm ? JointCorrectionMath.Decompose(Quaternion.Slerp(refRot, Quaternion.identity, k), o, fix) : default;
            for (int a = 0; a < 3; a++)
            {
                if (!r.Use[a]) continue;
                float ra = rr[a];
                float t = Mathf.Min(Mathf.Abs(ra), 90f) / 90f;
                float f = r.Min[a] + (r.Max[a] - r.Min[a]) * t;
                float v = ra * f;
                if (charm && JointCorrectionMath.Opposite(ra, h[a])) v = -v;
                c[a] = v;
            }
            C.localRotation = JointCorrectionMath.Compose(c, o);
        }

        //
        void ApplyLookAt(BoundRow b)
        {
            var r = b.Row;
            var L = b.Look;
            Vector3 u0 = Vector3.up;
            if (b.Up != null)
            {
                if (r.UpAxis == JcAxis.X) u0 = b.Up.right;
                else if (r.UpAxis == JcAxis.Y) u0 = b.Up.up;
                else if (r.UpAxis == JcAxis.Z) u0 = b.Up.forward;
            }
            Vector3 f = Vector3.Normalize(b.Target.position - L.position);
            Vector3 s = Vector3.Normalize(Vector3.Cross(u0, f));
            Vector3 v = Vector3.Cross(f, s);
            if (RobustLookAt && (f.sqrMagnitude < 0.5f || s.sqrMagnitude < 0.5f)) return;   // degenerate aim: keep the bone
            bool rev = r.TargetAxis == JcAxis.RevX || r.TargetAxis == JcAxis.RevY || r.TargetAxis == JcAxis.RevZ;
            if (rev) { f = -f; s = -s; }

            Vector3 ax = Vector3.zero, ay = Vector3.zero, az = Vector3.zero;
            var src = r.SourceAxis;
            switch (r.TargetAxis)
            {
                case JcAxis.X:
                case JcAxis.RevX:
                    ax = f;
                    if (src == JcAxis.Y) { ay = v; az = -s; }
                    else if (src == JcAxis.RevY) { ay = -v; az = s; }
                    else if (src == JcAxis.Z) { ay = s; az = v; }
                    else if (src == JcAxis.RevZ) { ay = -s; az = -v; }
                    break;
                case JcAxis.Y:
                case JcAxis.RevY:
                    ay = f;
                    if (src == JcAxis.X) { ax = v; az = s; }
                    else if (src == JcAxis.RevX) { ax = -v; az = -s; }
                    else if (src == JcAxis.Z) { ax = -s; az = v; }
                    else if (src == JcAxis.RevZ) { ax = s; az = -v; }
                    break;
                case JcAxis.Z:
                case JcAxis.RevZ:
                    az = f;
                    if (src == JcAxis.X) { ax = v; ay = -s; }
                    else if (src == JcAxis.RevX) { ax = -v; ay = s; }
                    else if (src == JcAxis.Y) { ax = s; ay = v; }
                    else if (src == JcAxis.RevY) { ax = -s; ay = -v; }
                    break;
            }
            var q = RobustLookAt ? JointCorrectionMath.FromAxesRobust(ax, ay, az) : JointCorrectionMath.FromAxesTrace(ax, ay, az);
            L.rotation = JointCorrectionMath.Normalized(q);

            int la = r.LimitAxis == JcAxis.X ? 0 : r.LimitAxis == JcAxis.Y ? 1 : r.LimitAxis == JcAxis.Z ? 2 : -1;
            if (r.LimitAxis == JcAxis.None) return;
            var ql = L.localRotation;
            var o = r.LookOrder;
            var e = JointCorrectionMath.Decompose(ql, o);
            if (la >= 0)
            {
                var h = JointCorrectionMath.Decompose(Quaternion.Slerp(ql, Quaternion.identity, 0.5f), o);
                float ea = e[la];
                if (JointCorrectionMath.Opposite(ea, h[la])) ea = -ea;
                e[la] = Mathf.Clamp(ea, r.LimitMin, r.LimitMax);
            }
            L.localRotation = JointCorrectionMath.Compose(e, o);
        }
    }
}
