// A Maker toggle row (the user's decision Y9): a bound Bool member of the character documents
// (`appearance.chara.…`; 4.2: the female-only «ふたなり» `profile.futanari` on the Body › Special page). A change is one
// StudioOps.EditAppearance set on every selected character that passes the row's gate (FieldSpec.Gate; the futanari toggle
// writes females only), one entry (the row's label, "Edit body"), undoable; several characters with different values show
// the mixed mark. FieldSpec.TooltipKey is the tooltip of the label and the toggle (the futanari toggle: data only in Studio,
// no visible effect). Element names: the row `field-<path>`, the toggle `toggle`.
using System;
using System.Collections.Generic;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class MakerToggleRow : VisualElement
    {
        public const string UssClass = "hs-maker-toggle";
        readonly UiContext ctx;
        readonly TargetSelector sel;

        public FieldSpec Spec { get; }
        public string Path { get; }
        public Toggle Toggle { get; }
        public FieldBinding Binding { get; }
        /// <summary>The last write's result (tests).</summary>
        public OpResult LastResult { get; private set; }

        public MakerToggleRow(UiContext ctx, FieldSpec spec, TargetSelector selector, string expandedPath = null)
        {
            this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            sel = selector;
            Path = expandedPath ?? spec.Path;
            name = "field-" + Path;
            AddToClassList(FieldRow.UssClass);
            AddToClassList(UssClass);
            Toggle = new Toggle(spec.LabelKey != null ? L10n.Get(spec.LabelKey) : Path) { name = "toggle" };
            Toggle.AddToClassList("hs-field__toggle");
            if (spec.LabelKey != null) Loc.Bind(Toggle, spec.LabelKey);
            if (spec.TooltipKey != null)
            {
                Loc.Tooltip(Toggle, spec.TooltipKey);
                if (Toggle.labelElement != null) Loc.Tooltip(Toggle.labelElement, spec.TooltipKey);
            }
            Toggle.RegisterValueChangedCallback(e => Write(Spec.Inverted ? !e.newValue : e.newValue));
            Add(Toggle);
            Binding = ctx.Bindings.Bind(this, new FieldBinding
            {
                Selector = sel, Path = Path, Spec = spec,
                Apply = (el, v) =>
                {
                    style.display = v.Targets == null || v.Targets.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
                    bool on = v.Value is bool b && b;
                    Toggle.SetValueWithoutNotify(Spec.Inverted ? !on : on);
                    Toggle.showMixedValue = v.Mixed;
                },
            });
        }

        /// <summary>The selected characters this row writes (those that pass its gate).</summary>
        IReadOnlyList<string> Targets => MakerRowKit.Writable(ctx, Spec, sel);

        /// <summary>Sets the member to <paramref name="value"/> on every writable selected character (one entry; tests call it).</summary>
        public OpResult Write(bool value)
        {
            var targets = Targets;
            if (targets.Count == 0) { Binding.MarkDirty(); return LastResult = OpResult.NothingToDo(); }
            LastResult = ctx.ShowResult(ctx.Ops.EditAppearance(targets, new List<(string, object)> { (Path, value) }, UiContext.User(Spec.OpLabel)));
            Binding.MarkDirty();
            return LastResult;
        }
    }
}
