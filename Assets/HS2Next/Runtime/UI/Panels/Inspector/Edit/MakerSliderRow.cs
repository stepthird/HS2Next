// A Maker slider row of the Edit tab: a NumberSlider
// in display units (stored × FieldSpec.Scale, i.e. 0–100 for the 0..1 Maker sliders) bound to an `appearance.` path of every
// selected character. A slider or label drag moves 0..1 (shown 0–100) and is one transaction — one history entry, Esc
// cancels; the number field takes TypedMin..TypedMax (−100..200 → −1..2) and merges consecutive typed edits of the field
// ("ui.typed/<ids>/<path>"). A stored value outside the slider range shows its number as it is and puts the knob at the
// end; nothing clamps the stored value. Writes go through StudioOps.EditAppearance.
// 4.1: component rows (FieldSpec.Component ≥ 0, e.g. «眉の幅» = face.eyebrow.layout z) read that component of each
// character's vector and write, per character, the character's own vector with only that component replaced (one
// transaction for all; typed edits merge per component); ↺ and a double click on the slider set the reference value
// (the value when the character's Edit tab first opened in this session, else the row default, F1) through
// StudioOps.ResetAppearance, one entry "Reset value"; the label of a lerp-mapped member (FieldSpec.LerpClamped) has the
// tooltip "values outside 0–100 have the effect of 0 or 100" (F12: inform, do not clamp). Template rows ({side}, {k}) are
// built with their expanded path. MakerRowKit holds the reset shared by the Maker rows.
// 4.2: a gated row writes only the selected characters that pass its gate
// (MakerRowKit.Writable: the female-only body rows skip a selected male, as the inspector's rows do).
// 4.3 (J8): a row with FieldSpec.ResetKind Constant (the hair mesh layout) resets to its
// row default itself, HS2's constants (MakerRowKit.ResetToConstant), not to the reference value.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class MakerSliderRow : VisualElement
    {
        public const string UssClass = "hs-maker-slider";
        readonly UiContext ctx;
        readonly TargetSelector sel;
        UiEdit edit;

        public FieldSpec Spec { get; }
        /// <summary>The (expanded) member path; component rows edit one component of it.</summary>
        public string Path { get; }
        public NumberSlider Control { get; }
        public FieldBinding Binding { get; }
        /// <summary>The last write's result (tests).</summary>
        public OpResult LastResult { get; private set; }
        /// <summary>True while a drag transaction is open.</summary>
        public bool Dragging => edit != null && edit.IsOpen;

        public MakerSliderRow(UiContext ctx, FieldSpec spec, TargetSelector selector, string expandedPath = null)
        {
            this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            sel = selector;
            Path = expandedPath ?? spec.Path;
            name = "field-" + Path + (spec.Component >= 0 ? "#" + spec.Component : "");
            AddToClassList(FieldRow.UssClass);
            AddToClassList(UssClass);
            double k = spec.Scale;
            Control = new NumberSlider(L10n.Get(spec.LabelKey), spec.Min * k, spec.Max * k)
            {
                TypedMin = spec.TypedMin * k, TypedMax = spec.TypedMax * k, Whole = false, Format = spec.Format ?? "0",
            };
            // ↺ is always offered: it sets the reference value (Commit is raised only by ↺ and the slider's double click)
            Control.DefaultValue = () => spec.ToDisplay(MakerRowKit.DefaultOf(spec) is double d ? d : 0);
            Control.Edited += OnEdited;
            if (spec.LabelKey != null) Loc.Bind(Control.LabelElement, spec.LabelKey);
            if (spec.LerpClamped) Loc.Tooltip(Control.LabelElement, "maker.slider.lerpTooltip");
            Loc.Tooltip(Control.ResetButton, "maker.reset.tooltip");
            Add(Control);
            Binding = ctx.Bindings.Bind(this, new FieldBinding { Selector = sel, Path = Path, Spec = spec, Apply = (el, v) => Show(v) });
        }

        // the selected characters that pass the row's gate (4.2: the female-only body rows write females only)
        IReadOnlyList<string> Targets => MakerRowKit.Writable(ctx, Spec, sel);

        void Show(BoundValues v)
        {
            style.display = v.Empty && v.Targets.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            Control.SetDisplay(Spec.ToDisplay(FieldValues.ToDouble(v.Value)), v.Mixed);
        }

        string MergeKey(IReadOnlyList<string> targets) =>
            "ui.typed/" + string.Join(",", targets.Where(t => t != null).OrderBy(t => t, StringComparer.Ordinal)) + "/" + Path + (Spec.Component >= 0 ? "#" + Spec.Component : "");

        void OnEdited(EditPhase phase, double shown)
        {
            var targets = Targets;
            if (targets.Count == 0) return;
            switch (phase)
            {
                case EditPhase.Begin:
                    edit?.Cancel();
                    edit = ctx.BeginEdit(Spec.OpLabel ?? OpLabels.EditFace);
                    var mine = edit;
                    mine.Ended += committed => { if (edit == mine) edit = null; if (!committed) Binding.MarkDirty(); };
                    return;
                case EditPhase.End:
                {
                    var e = edit;
                    edit = null;
                    LastResult = ctx.ShowResult(e != null ? e.Commit() : OpResult.NothingToDo());
                    Binding.MarkDirty();
                    return;
                }
                case EditPhase.Preview:
                    Write(targets, Spec.ClampSlider(Spec.FromDisplay(shown)), null);
                    return;
                case EditPhase.Typed:
                    Write(targets, Spec.ParseTyped(shown), MergeKey(targets));
                    return;
                case EditPhase.Commit:
                    // ↺ / double click: the reference value of each character
                    Reset();
                    return;
            }
        }

        /// <summary>↺: the reference value of each selected character (one entry "Reset value").</summary>
        public OpResult Reset()
        {
            if (edit != null && edit.IsOpen) { edit.Cancel(); edit = null; }
            LastResult = MakerRowKit.Reset(ctx, Spec, Path, Targets);
            Binding.MarkDirty();
            return LastResult;
        }

        /// <summary>Writes a stored value to every target that passes the row's gate, as the widget would (typed:
        /// <paramref name="mergeKey"/>; tests).</summary>
        public OpResult Write(IReadOnlyList<string> targets, double stored, string mergeKey)
        {
            targets = MakerRowKit.Writable(ctx, Spec, targets);
            if (targets.Count == 0) return LastResult = OpResult.NothingToDo();
            var o = UiContext.User(Spec.OpLabel, mergeKey);
            OpResult r;
            if (Spec.Component < 0)
                r = ctx.Ops.EditAppearance(targets, new List<(string, object)> { (Path, (float)stored) }, o);
            else
            {
                // per character: its own vector with this component replaced, all in one transaction
                r = OpResult.NothingToDo();
                using (var own = edit == null ? ctx.BeginEdit(Spec.OpLabel ?? OpLabels.EditFace, mergeKey) : null)
                {
                    foreach (var id in targets)
                    {
                        if (!FieldAccessor.TryGet(ctx.Model, id, Path, out var cur, out _) || cur == null) continue;
                        var nv = FieldValues.WithComponent(cur, Spec.Component, stored);
                        r = ctx.Ops.EditAppearance(new[] { id }, new List<(string, object)> { (Path, nv) }, o);
                        if (!r.Ok) break;
                    }
                    if (own != null) { if (r.Ok) r = own.Commit(); else own.Cancel(); }
                }
            }
            LastResult = r;
            if (edit == null)
            {
                ctx.ShowResult(r);
                Binding.MarkDirty();
            }
            else if (!r.Ok) ctx.ShowResult(r);
            return r;
        }
    }

    /// <summary>The per-row reset of the Maker rows (F1).</summary>
    public static class MakerRowKit
    {
        public const string ResetLabel = OpLabels.ResetValue;

        /// <summary>
        /// The selected characters a Maker row writes: every one of <paramref name="sel"/> that passes the row's gate (the
        /// inspector's rule, InspectorWrites.TargetsOf; a gated row is shown when the primary
        /// passes and writes the characters that pass it — the female-only body rows skip a selected male).
        /// </summary>
        public static IReadOnlyList<string> Writable(UiContext ctx, FieldSpec spec, TargetSelector sel) =>
            Writable(ctx, spec, sel?.Resolve(ctx) ?? (IReadOnlyList<string>)new List<string>());

        /// <summary>The characters of <paramref name="ids"/> that pass the row's gate (all of them for an ungated row).</summary>
        public static IReadOnlyList<string> Writable(UiContext ctx, FieldSpec spec, IReadOnlyList<string> ids)
        {
            ids ??= new List<string>();
            if (string.IsNullOrEmpty(spec?.Gate) || ids.Count == 0 || ctx?.Ops == null) return ids;
            return InspectorWrites.TargetsOf(ctx.Ops, spec, ids);
        }

        /// <summary>The row default of a Maker row (FieldSpec.Default; a component row takes its component of a vector default).</summary>
        public static object DefaultOf(FieldSpec spec)
        {
            var d = spec?.Default;
            if (d is double[] arr) return spec.Component >= 0 && spec.Component < arr.Length ? (object)arr[spec.Component] : arr;
            if (d is float f) return (double)f;
            if (d is int i) return (double)i;
            return d;
        }

        /// <summary>
        /// Sets <paramref name="path"/> (one component for component rows: "path#c") of every target to its reference value —
        /// the value when the character's Edit tab first opened in this session — or the row default when there is none;
        /// one entry "Reset value" (StudioOps.ResetAppearance).
        /// </summary>
        public static OpResult Reset(UiContext ctx, FieldSpec spec, string path, IReadOnlyList<string> targets)
        {
            if (targets == null || targets.Count == 0) return OpResult.NothingToDo();
            if (spec.ResetKind == ResetKind.Constant) return ResetToConstant(ctx, spec, path, targets);
            string p = spec.Component >= 0 ? path + "#" + spec.Component.ToString(System.Globalization.CultureInfo.InvariantCulture) : path;
            return ctx.ShowResult(ctx.Ops.ResetAppearance(targets, new[] { p }, new[] { spec.Component >= 0 ? DefaultOf(spec) : spec.Default }, UiContext.User(OpLabels.ResetValue)));
        }

        /// <summary>
        /// ↺ of a <see cref="ResetKind.Constant"/> row (J8: the hair mesh layout returns to
        /// HS2's (1, 1, 0, 0)): the row default itself, not the reference value — per target its own vector with the component
        /// replaced (StudioOps.EditAppearance), one entry "Reset value".
        /// </summary>
        public static OpResult ResetToConstant(UiContext ctx, FieldSpec spec, string path, IReadOnlyList<string> targets)
        {
            var d = DefaultOf(spec);
            if (targets == null || targets.Count == 0 || d == null) return OpResult.NothingToDo();
            var o = UiContext.User(OpLabels.ResetValue);
            OpResult r = OpResult.NothingToDo();
            using (var own = ctx.BeginEdit(OpLabels.ResetValue))
            {
                foreach (var id in targets)
                {
                    if (!FieldAccessor.TryGet(ctx.Model, id, path, out var cur, out _) || cur == null) continue;
                    object nv = spec.Component >= 0 ? FieldValues.WithComponent(cur, spec.Component, FieldValues.ToDouble(d)) : FieldValues.ToModel(d, cur.GetType());
                    r = ctx.Ops.EditAppearance(new[] { id }, new List<(string, object)> { (path, nv) }, o);
                    if (!r.Ok) break;
                }
                if (r.Ok) r = own.Commit(); else own.Cancel();
            }
            return ctx.ShowResult(r);
        }
    }
}
