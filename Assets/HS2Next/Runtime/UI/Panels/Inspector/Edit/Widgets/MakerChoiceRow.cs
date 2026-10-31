// A Maker choice row (HX4): a bound appearance int (or string enum) of the character
// documents shown as segmented buttons, one per FieldSpec.Choices entry (4.3: «描画設定» «タイプ01» / «タイプ02» on
// `hair.shaderType`). A click is one StudioOps.EditAppearance set of the choice's value (FieldSpec.ChoiceValue) on every
// selected character that passes the row's gate, one entry (the row's label, "Edit hair"); a click on the current choice
// adds none (the op's no-op); several characters with different values show no highlighted button (mixed). Element names:
// the row `field-<path>`, the buttons `field-<path>-<i>` (i = choice position). 4.5 reuses it for the accessory type radio.
using System;
using System.Collections.Generic;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class MakerChoiceRow : VisualElement
    {
        public const string UssClass = "hs-maker-choice";
        readonly UiContext ctx;
        readonly TargetSelector sel;
        readonly bool stringEnum;

        public FieldSpec Spec { get; }
        public string Path { get; }
        public Label LabelElement { get; }
        public SegmentedButtons Buttons { get; }
        public FieldBinding Binding { get; }
        /// <summary>The choice shown (-1 = none / mixed).</summary>
        public int Index => Buttons.Index;
        /// <summary>The last write's result (tests).</summary>
        public OpResult LastResult { get; private set; }

        public MakerChoiceRow(UiContext ctx, FieldSpec spec, TargetSelector selector, string expandedPath = null)
        {
            this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            sel = selector;
            Path = expandedPath ?? spec.Path;
            name = "field-" + Path;
            AddToClassList(FieldRow.UssClass);
            AddToClassList(UssClass);
            // a string enum of the model stores the choice itself; an int member (hair.shaderType) its position (or ChoiceValues)
            stringEnum = spec.Default is string;
            LabelElement = new Label(spec.LabelKey != null ? L10n.Get(spec.LabelKey) : Path);
            LabelElement.AddToClassList("hs-number__label");
            if (spec.LabelKey != null) Loc.Bind(LabelElement, spec.LabelKey);
            if (spec.TooltipKey != null) Loc.Tooltip(LabelElement, spec.TooltipKey);
            Add(LabelElement);
            int n = spec.Choices?.Count ?? 0;
            var labels = new string[n];
            for (int i = 0; i < n; i++) labels[i] = L10n.Get(spec.ChoiceLabelKey(i));
            Buttons = new SegmentedButtons(labels);
            for (int i = 0; i < n; i++)
            {
                Buttons.Buttons[i].name = name + "-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Loc.Bind(Buttons.Buttons[i], spec.ChoiceLabelKey(i));
            }
            Buttons.Selected += i => Write(i);
            Add(Buttons);
            Binding = ctx.Bindings.Bind(this, new FieldBinding
            {
                Selector = sel, Path = Path, Spec = spec,
                Apply = (el, v) =>
                {
                    style.display = v.Targets == null || v.Targets.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
                    Buttons.SetDisplay(v.Mixed ? -1 : IndexOf(v.Value), v.Mixed);
                },
            });
        }

        /// <summary>The position of a stored value among the choices (-1 when none matches).</summary>
        public int IndexOf(object value)
        {
            if (value == null || Spec.Choices == null) return -1;
            for (int i = 0; i < Spec.Choices.Count; i++)
            {
                if (value is string s ? string.Equals(Spec.Choices[i], s, StringComparison.Ordinal) : FieldValues.ToDouble(value) == FieldValues.ToDouble(Spec.ChoiceValue(i, false))) return i;
            }
            return -1;
        }

        /// <summary>The selected characters this row writes (those that pass its gate).</summary>
        IReadOnlyList<string> Targets => MakerRowKit.Writable(ctx, Spec, sel);

        /// <summary>Sets choice <paramref name="i"/> on every writable selected character (one entry; tests call it).</summary>
        public OpResult Write(int i)
        {
            var targets = Targets;
            if (Spec.Choices == null || i < 0 || i >= Spec.Choices.Count || targets.Count == 0) { Binding.MarkDirty(); return LastResult = OpResult.NothingToDo(); }
            LastResult = ctx.ShowResult(ctx.Ops.EditAppearance(targets, new List<(string, object)> { (Path, Spec.ChoiceValue(i, stringEnum)) }, UiContext.User(Spec.OpLabel)));
            Binding.MarkDirty();
            return LastResult;
        }
    }
}
