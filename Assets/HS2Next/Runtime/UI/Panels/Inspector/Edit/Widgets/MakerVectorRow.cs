// A Maker transform row of the Accessories page (XQ14): one `Vec3` member of an
// accessory's transform set (`moves[c].position` in tenths, `.rotation` in degrees, `.scale`) shown in stored units as three
// number fields x, y, z (position with one decimal, rotation with up to one, scale with two), each with − / + step buttons, a
// drag on its axis label and a per-axis ↺. Every UI write follows HS2's input rules on the row's grid and limits
// (FieldSpec.Grid, TypedMin / TypedMax, Wrap360): a typed number is first rounded to the grid's decimals as HS2's text field
// does (position 12.35 → 12.4, rotation 359.6 → 360), then clamped to the typed range and put on the grid (rotation
// truncated and wrapped into [0, 360): 359.6 → 0, 400 → 0, −30 → 0; position −100..100; scale 0.01..100); a step or a
// drag adds the step size of the set's rate (FieldSpec.Step: rotation wraps, 359 + 5 → 4; the others clamp). The x axis's
// buttons and label carry the same signs as y and z (XQ14; HS2 reverses x). A held step button repeats every 0.05 s after
// 0.3 s and is one history entry per press; a press-and-drag on an axis label adds the drag in screen pixels (logical px × the
// UI scale) × the step, as HS2's label drag, and is one entry; each typed value is one entry. A stored value outside the
// limits or off the grid is shown as it is and only rewritten by an edit.
// ↺ sets the component to the row default (position 0, rotation 0, scale 1; "Reset value"). Writes go through
// StudioOps.EditAppearance on the row's targets (the Accessories page: the primary). Layout: the label, then the three axes
// on a line that wraps when the inspector is narrower than three axes need. Element names: the row `field-<path>`, the axes
// line `axes`, per axis `field-<path>#<i>` holding `axis`, `minus`, `value`, `plus`, `reset`.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class MakerVectorRow : VisualElement
    {
        public const string UssClass = "hs-maker-vector";
        /// <summary>Delay before a held step button repeats, and its repeat interval (s).</summary>
        public const float RepeatDelay = 0.3f, RepeatInterval = 0.05f;

        readonly UiContext ctx;
        readonly TargetSelector sel;
        readonly Func<double> stepOf;
        readonly TextField[] fields = new TextField[3];
        readonly VisualElement[] axes = new VisualElement[3];
        readonly double[] shown = new double[3];
        bool mixed;
        UiEdit edit;
        // the open press-and-hold or drag: axis, sign (steps), start value, start x, steps applied
        int holdAxis = -1, holdSign;
        float holdNext;
        IVisualElementScheduledItem holdTick;
        int dragAxis = -1;
        double dragStart;
        float dragX;
        int dragPx;

        public FieldSpec Spec { get; }
        public string Path { get; }
        public FieldBinding Binding { get; }
        /// <summary>The last write's result (tests).</summary>
        public OpResult LastResult { get; private set; }
        /// <summary>Steps applied by the open (or the last) press-and-hold (tests).</summary>
        public int HoldSteps { get; private set; }
        /// <summary>True while a held step or an axis drag is open (one history entry).</summary>
        public bool Holding => edit != null && edit.IsOpen;
        /// <summary>The number field of axis <paramref name="i"/> (0..2).</summary>
        public TextField Field(int i) => fields[i];

        /// <param name="stepOf">The step size of the set's rate for this row (position 0.1 / 1 / 10, rotation 1 / 5 / 10, scale
        /// 0.01 / 0.1 / 1).</param>
        public MakerVectorRow(UiContext ctx, FieldSpec spec, TargetSelector selector, string expandedPath, Func<double> stepOf)
        {
            this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            sel = selector;
            this.stepOf = stepOf ?? (() => spec.Grid ?? 1);
            Path = expandedPath ?? spec.Path;
            name = "field-" + Path;
            AddToClassList(FieldRow.UssClass);
            AddToClassList(UssClass);
            var label = new Label(spec.LabelKey != null ? L10n.Get(spec.LabelKey) : Path);
            label.AddToClassList("hs-maker-vector__label");
            if (spec.LabelKey != null) Loc.Bind(label, spec.LabelKey);
            Add(label);
            // the three axes on the line below the label; an axis keeps the width of its fixed parts and the line wraps when the
            // inspector is narrower than three of them (a part never lies under the next axis)
            var line = new VisualElement { name = "axes" };
            line.AddToClassList("hs-maker-vector__axes");
            Add(line);
            for (int i = 0; i < 3; i++) line.Add(axes[i] = BuildAxis(i));
            Binding = ctx.Bindings.Bind(this, new FieldBinding { Selector = sel, Path = Path, Spec = spec, Apply = (el, v) => Show(v) });
            RegisterCallback<DetachFromPanelEvent>(_ => EndHold(true));
        }

        VisualElement BuildAxis(int i)
        {
            var a = new VisualElement { name = "field-" + Path + "#" + i.ToString(CultureInfo.InvariantCulture) };
            a.AddToClassList("hs-maker-vector__axis");
            var axis = new Label("XYZ"[i].ToString()) { name = "axis" };
            axis.AddToClassList("hs-maker-vector__axislabel");
            Loc.Tooltip(axis, "maker.acc.axisTip");
            axis.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) { axis.CapturePointer(e.pointerId); BeginDrag(i, e.position.x); e.StopPropagation(); } });
            axis.RegisterCallback<PointerMoveEvent>(e => { if (dragAxis == i && axis.HasPointerCapture(e.pointerId)) DragTo(e.position.x); });
            axis.RegisterCallback<PointerUpEvent>(e => { if (axis.HasPointerCapture(e.pointerId)) axis.ReleasePointer(e.pointerId); EndHold(false); });
            axis.RegisterCallback<PointerCaptureOutEvent>(_ => EndHold(false));
            a.Add(axis);
            a.Add(StepButton(i, -1, "minus", "−"));
            var f = new TextField { name = "value", isDelayed = true };
            f.AddToClassList("hs-maker-vector__field");
            f.RegisterValueChangedCallback(e => Typed(i, e.newValue));
            a.Add(f);
            fields[i] = f;
            a.Add(StepButton(i, +1, "plus", "+"));
            var r = new Button(() => Reset(i)) { name = "reset", text = "↺" };
            r.AddToClassList("hs-maker-vector__reset");
            Loc.Tooltip(r, "maker.reset.tooltip");
            a.Add(r);
            return a;
        }

        // a step button: press = one step and the start of one entry, held = repeats, release = commit
        VisualElement StepButton(int i, int sign, string elementName, string text)
        {
            var b = new Label(text) { name = elementName };
            b.AddToClassList("unity-button");
            b.AddToClassList("hs-maker-vector__step");
            Loc.Tooltip(b, "maker.acc.stepTip");
            b.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) { b.CapturePointer(e.pointerId); BeginHold(i, sign); e.StopPropagation(); } });
            b.RegisterCallback<PointerUpEvent>(e => { if (b.HasPointerCapture(e.pointerId)) b.ReleasePointer(e.pointerId); EndHold(false); });
            b.RegisterCallback<PointerCaptureOutEvent>(_ => EndHold(false));
            return b;
        }

        IReadOnlyList<string> Targets => MakerRowKit.Writable(ctx, Spec, sel);

        void Show(BoundValues v)
        {
            style.display = v.Targets == null || v.Targets.Count == 0 || v.Empty ? DisplayStyle.None : DisplayStyle.Flex;
            mixed = v.Mixed;
            for (int i = 0; i < 3; i++)
            {
                var c = FieldValues.Component(v.Value, i);
                shown[i] = c != null ? FieldValues.ToDouble(c) : 0;
                if (fields[i].focusController?.focusedElement == fields[i]) continue;   // keep a value being typed
                fields[i].SetValueWithoutNotify(mixed ? "—" : c == null ? "" : Text(shown[i]));
            }
        }

        string Text(double v) => v.ToString(Spec.Format ?? "0.###", CultureInfo.InvariantCulture);

        /// <summary>The component <paramref name="i"/> of the primary's stored value (as shown).</summary>
        public double Value(int i) => shown[i];

        /// <summary>
        /// A typed number → the stored value: rounded to the grid's decimals first (HS2's field text), then clamped to the typed
        /// range, put on the grid and wrapped (FieldSpec.ClampTyped).
        /// </summary>
        public static double TypedValue(FieldSpec spec, double typed)
        {
            double v = typed;
            if (spec.Grid.HasValue && spec.Grid.Value > 0)
            {
                int d = (int)Math.Round(-Math.Log10(spec.Grid.Value));
                if (d >= 0 && d <= 15) v = Math.Round(v, d, MidpointRounding.AwayFromZero);
            }
            return spec.ClampTyped(v);
        }

        void Typed(int i, string text)
        {
            if (!double.TryParse((text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            {
                Binding.MarkDirty();
                return;
            }
            var targets = Targets;
            if (targets.Count == 0) return;
            Write(targets, i, TypedValue(Spec, d), MergeKey(targets, i));
        }

        string MergeKey(IReadOnlyList<string> targets, int i) =>
            "ui.typed/" + string.Join(",", targets.Where(t => t != null).OrderBy(t => t, StringComparer.Ordinal)) + "/" + Path + "#" + i.ToString(CultureInfo.InvariantCulture);

        /// <summary>Types <paramref name="typed"/> into axis <paramref name="i"/> as the field would (tests).</summary>
        public OpResult Type(int i, double typed)
        {
            var targets = Targets;
            if (targets.Count == 0) return LastResult = OpResult.NothingToDo();
            return Write(targets, i, TypedValue(Spec, typed), MergeKey(targets, i));
        }

        /// <summary>Writes component <paramref name="i"/> = <paramref name="stored"/> on every target (its own vector with the
        /// component replaced; inside an open hold / drag the edit collects the writes).</summary>
        public OpResult Write(IReadOnlyList<string> targets, int i, double stored, string mergeKey)
        {
            targets = MakerRowKit.Writable(ctx, Spec, targets);
            if (targets.Count == 0) return LastResult = OpResult.NothingToDo();
            var o = UiContext.User(Spec.OpLabel, mergeKey);
            OpResult r = OpResult.NothingToDo();
            using (var own = edit == null && targets.Count > 1 ? ctx.BeginEdit(Spec.OpLabel ?? OpLabels.EditAccessory, mergeKey) : null)
            {
                foreach (var id in targets)
                {
                    if (!FieldAccessor.TryGet(ctx.Model, id, Path, out var cur, out _) || cur == null) continue;
                    r = ctx.Ops.EditAppearance(new[] { id }, new List<(string, object)> { (Path, FieldValues.WithComponent(cur, i, stored)) }, o);
                    if (!r.Ok) break;
                }
                if (own != null) { if (r.Ok) r = own.Commit(); else own.Cancel(); }
            }
            LastResult = r;
            if (edit == null || !r.Ok) ctx.ShowResult(r);
            Binding.MarkDirty();
            return r;
        }

        /// <summary>↺ of axis <paramref name="i"/>: the row default's component (position 0, rotation 0, scale 1), one entry.</summary>
        public OpResult Reset(int i)
        {
            EndHold(true);
            var c = Spec.Clone();
            c.Component = i;
            LastResult = MakerRowKit.ResetToConstant(ctx, c, Path, Targets);
            Binding.MarkDirty();
            return LastResult;
        }

        // ---------------------------------------------------------------- press-and-hold steps and label drags

        bool OpenEdit()
        {
            EndHold(false);
            if (Targets.Count == 0) return false;
            edit = ctx.BeginEdit(Spec.OpLabel ?? OpLabels.EditAccessory);
            if (!edit.IsOpen) { ctx.ShowResult(OpResult.From(edit.Failure)); edit = null; return false; }
            var mine = edit;
            mine.Ended += committed => { if (edit == mine) edit = null; if (!committed) Binding.MarkDirty(); };
            return true;
        }

        /// <summary>Presses the − (<paramref name="sign"/> −1) or + (+1) button of axis <paramref name="i"/>: one step now, then a
        /// step every <see cref="RepeatInterval"/> s after <see cref="RepeatDelay"/> s until <see cref="EndHold"/> (tests call it).</summary>
        public void BeginHold(int i, int sign)
        {
            if (!OpenEdit()) return;
            holdAxis = i;
            holdSign = sign < 0 ? -1 : 1;
            HoldSteps = 0;
            StepOnce();
            holdNext = Time.realtimeSinceStartup + RepeatDelay;
            holdTick = schedule.Execute(() =>
            {
                if (holdAxis < 0) return;
                float now = Time.realtimeSinceStartup;
                while (holdAxis >= 0 && now >= holdNext)
                {
                    StepOnce();
                    holdNext += RepeatInterval;
                }
            }).Every(10);
        }

        void StepOnce()
        {
            var targets = Targets;
            if (targets.Count == 0 || holdAxis < 0) return;
            int i = holdAxis;
            // each target steps from its own current component
            var o = UiContext.User(Spec.OpLabel);
            foreach (var id in targets)
            {
                if (!FieldAccessor.TryGet(ctx.Model, id, Path, out var cur, out _) || cur == null) continue;
                var cc = FieldValues.Component(cur, i);
                double v = Spec.Step(cc != null ? FieldValues.ToDouble(cc) : 0, holdSign * stepOf());
                var r = ctx.Ops.EditAppearance(new[] { id }, new List<(string, object)> { (Path, FieldValues.WithComponent(cur, i, v)) }, o);
                LastResult = r;
                if (!r.Ok) { ctx.ShowResult(r); break; }
            }
            HoldSteps++;
            Binding.MarkDirty();
        }

        /// <summary>Starts an axis-label drag of axis <paramref name="i"/> at panel x <paramref name="x"/> (tests call it).</summary>
        public void BeginDrag(int i, float x)
        {
            if (!OpenEdit()) return;
            dragAxis = i;
            dragX = x;
            dragPx = 0;
            dragStart = shown[i];
            if (FieldAccessor.TryGet(ctx.Model, Targets[0], Path, out var cur, out _) && FieldValues.Component(cur, i) is object c) dragStart = FieldValues.ToDouble(c);
        }

        /// <summary>The drag at panel x <paramref name="x"/>: the start value + the drag's screen pixels (logical px × the UI scale)
        /// × the step, the same direction on every axis (XQ14).</summary>
        public void DragTo(float x)
        {
            if (dragAxis < 0 || edit == null) return;
            int px = ScreenPixels(x - dragX);
            if (px == dragPx) return;
            dragPx = px;
            int steps = px;
            var targets = Targets;
            if (targets.Count == 0) return;
            double v = Spec.Step(dragStart, steps * stepOf());
            var o = UiContext.User(Spec.OpLabel);
            foreach (var id in targets)
            {
                if (!FieldAccessor.TryGet(ctx.Model, id, Path, out var cur, out _) || cur == null) continue;
                var r = ctx.Ops.EditAppearance(new[] { id }, new List<(string, object)> { (Path, FieldValues.WithComponent(cur, dragAxis, v)) }, o);
                LastResult = r;
                if (!r.Ok) { ctx.ShowResult(r); break; }
            }
            Binding.MarkDirty();
        }

        /// <summary>Screen pixels of a drag of <paramref name="logical"/> panel px (× the panel's pixels per point), toward zero.</summary>
        public int ScreenPixels(float logical) => (int)Math.Truncate(logical * (panel != null ? panel.scaledPixelsPerPoint : 1f));

        /// <summary>Ends the open hold or drag: commits its one entry (<paramref name="cancel"/>: discards it).</summary>
        public void EndHold(bool cancel)
        {
            holdTick?.Pause();
            holdTick = null;
            holdAxis = -1;
            dragAxis = -1;
            var e = edit;
            edit = null;
            if (e == null || !e.IsOpen) return;
            if (cancel) e.Cancel();
            else LastResult = ctx.ShowResult(e.Commit());
            Binding.MarkDirty();
        }
    }
}
