// The Maker colour row: label, the 3.5 ColorSwatch and ↺ (`color-reset`). The swatch opens
// the 3.5 ColorPicker unchanged, with the alpha strip and field only where the row allows alpha (FieldSpec.ColorAlpha;
// on the face pages every colour but the sclera); without alpha each character's stored alpha is written back unchanged.
// The picker edits the stored sRGB values directly (HSV of the stored values, no linear conversion);
// values outside 0..1 show clamped in the picker and stay stored until a control moves. Opening the picker opens one
// transaction, every change is a preview set (the units run in the next flush), OK / Enter / a click outside commits one
// entry (the row's label, "Edit face" on the face pages), Esc / Cancel reverts. Several characters: the swatch shows the
// primary's colour with the mixed mark; a change writes the colour to every selected character. ↺ = the reference value
// (StudioOps.ResetAppearance). Writes go through StudioOps.EditAppearance.
// 4.4: a row whose alpha depends on the worn prefab (FieldSpec.ColorAlphaGate, the clothes main colours: the prefab's
// useColorA0n) has the alpha strip only while that gate holds on the primary — evaluated when the row is built, when the
// picker opens and when the page re-evaluates the row's section after an item change (RefreshAlpha; no row rebuild). A
// write keeps each character's stored alpha unless the character passes the alpha gate itself.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class MakerColorRow : VisualElement
    {
        public const string UssClass = "hs-maker-color";
        readonly UiContext ctx;
        readonly TargetSelector sel;
        UiEdit edit;

        public FieldSpec Spec { get; }
        public string Path { get; }
        public ColorSwatch Swatch { get; }
        public Button ResetButton { get; }
        public FieldBinding Binding { get; }
        /// <summary>The open picker (null when closed).</summary>
        public ColorPicker Picker { get; private set; }
        /// <summary>The last write's result (tests).</summary>
        public OpResult LastResult { get; private set; }

        public MakerColorRow(UiContext ctx, FieldSpec spec, TargetSelector selector, string expandedPath = null)
        {
            this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            sel = selector;
            Path = expandedPath ?? spec.Path;
            name = "field-" + Path;
            AddToClassList(FieldRow.UssClass);
            AddToClassList(UssClass);
            style.flexDirection = FlexDirection.Row;
            AlphaShown = AlphaHolds(PrimaryOf());
            Swatch = new ColorSwatch(spec.LabelKey != null ? L10n.Get(spec.LabelKey) : Path) { Alpha = AlphaShown };
            Swatch.style.flexGrow = 1;
            if (spec.LabelKey != null) Loc.Bind(Swatch.LabelElement, spec.LabelKey);
            Swatch.Clicked += () => OpenPicker();
            Add(Swatch);
            ResetButton = new Button(() => Reset()) { name = "color-reset", text = "↺" };
            ResetButton.AddToClassList("hs-field__default");
            Loc.Tooltip(ResetButton, "maker.reset.tooltip");
            Add(ResetButton);
            Binding = ctx.Bindings.Bind(this, new FieldBinding { Selector = sel, Path = Path, Spec = spec, Apply = (el, v) => Show(v) });
        }

        // the selected characters that pass the row's gate (the male-only beard colour writes males only)
        IReadOnlyList<string> Targets => MakerRowKit.Writable(ctx, Spec, sel);

        /// <summary>True while the row offers alpha: FieldSpec.ColorAlpha and, with a FieldSpec.ColorAlphaGate, that gate on the
        /// primary (re-evaluated by <see cref="RefreshAlpha"/> and when the picker opens).</summary>
        public bool AlphaShown { get; private set; }

        string PrimaryOf()
        {
            var ids = sel?.Resolve(ctx);
            return ids != null && ids.Count > 0 ? ids[0] : null;
        }

        bool AlphaHolds(string id) =>
            Spec.ColorAlpha && (string.IsNullOrEmpty(Spec.ColorAlphaGate) || (id != null && InspectorGates.Holds(ctx.Ops, Spec.ColorAlphaGate, id)));

        /// <summary>Re-evaluates the alpha gate on the primary and refreshes the swatch (the page calls it when it re-evaluates the
        /// row's section; the picker calls it when it opens).</summary>
        public void RefreshAlpha()
        {
            bool a = AlphaHolds(PrimaryOf());
            if (a == AlphaShown) return;
            AlphaShown = a;
            Swatch.Alpha = a;
            Binding?.MarkDirty();
        }

        void Show(BoundValues v)
        {
            style.display = v.Targets == null || v.Targets.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            Swatch.SetDisplay(FieldValues.ToColor(v.Value) ?? Color.white, v.Mixed);
        }

        /// <summary>Opens the picker (tests call it directly). Returns false when no layer or character is there.</summary>
        public bool OpenPicker()
        {
            var targets = Targets;
            var layer = ctx.Shell?.Overlay ?? panel?.visualTree;
            if (layer == null || targets.Count == 0) return false;
            Picker?.Close(true);
            RefreshAlpha();
            var picker = new ColorPicker(Swatch.Value, AlphaShown);
            Picker = picker;
            edit?.Cancel();
            edit = ctx.BeginEdit(Spec.OpLabel ?? OpLabels.EditFace);
            var mine = edit;
            picker.Changed += (c, _) => Write(c);
            picker.Closed += commit =>
            {
                Picker = null;
                if (edit == mine) edit = null;
                if (commit) LastResult = ctx.ShowResult(mine.Commit());
                else mine.Cancel();
                Binding.MarkDirty();
            };
            var blocker = new VisualElement { name = "color-blocker" };
            blocker.style.position = Position.Absolute;
            blocker.style.left = 0; blocker.style.top = 0; blocker.style.right = 0; blocker.style.bottom = 0;
            blocker.RegisterCallback<PointerDownEvent>(e => { picker.Close(true); e.StopPropagation(); });
            picker.Closed += _ => blocker.RemoveFromHierarchy();
            picker.style.position = Position.Absolute;
            var wb = Swatch.Button.worldBound;
            picker.style.left = Mathf.Max(0, wb.xMin - 260);
            picker.style.top = wb.yMax;
            picker.RegisterCallback<GeometryChangedEvent>(_ => { float maxY = layer.layout.height - picker.layout.height - 2; if (maxY > 0 && picker.layout.y > maxY + 0.5f) picker.style.top = maxY; });
            layer.Add(blocker);
            layer.Add(picker);
            UiDefer.Frames(2, () => { if (picker.panel != null) picker.Focus(); });
            return true;
        }

        /// <summary>A preview set of <paramref name="c"/> on every selected character (inside the picker's transaction).</summary>
        public OpResult Write(Color c)
        {
            var targets = Targets;
            if (targets.Count == 0) return OpResult.NothingToDo();
            OpResult r;
            var o = UiContext.User(Spec.OpLabel);
            bool alphaAll = AlphaShown && (string.IsNullOrEmpty(Spec.ColorAlphaGate) || targets.All(AlphaHolds));
            if (alphaAll)
                r = ctx.Ops.EditAppearance(targets, new List<(string, object)> { (Path, new Color4(c.r, c.g, c.b, c.a)) }, o);
            else
            {
                // no alpha on this row (or not for every character): a character without it keeps its own stored alpha
                r = OpResult.NothingToDo();
                using (var own = edit == null ? ctx.BeginEdit(Spec.OpLabel ?? OpLabels.EditFace) : null)
                {
                    foreach (var id in targets)
                    {
                        float a = AlphaShown && AlphaHolds(id) ? c.a : FieldAccessor.TryGet(ctx.Model, id, Path, out var cur, out _) && cur is Color4 c4 ? c4.A : 1f;
                        r = ctx.Ops.EditAppearance(new[] { id }, new List<(string, object)> { (Path, new Color4(c.r, c.g, c.b, a)) }, o);
                        if (!r.Ok) break;
                    }
                    if (own != null) { if (r.Ok) r = own.Commit(); else own.Cancel(); }
                }
            }
            LastResult = r;
            if (!r.Ok) ctx.ShowResult(r);
            if (edit == null) Binding.MarkDirty();
            return r;
        }

        /// <summary>↺: the reference value of each selected character (one entry "Reset value").</summary>
        public OpResult Reset()
        {
            LastResult = MakerRowKit.Reset(ctx, Spec, Path, Targets);
            Binding.MarkDirty();
            return LastResult;
        }
    }
}
