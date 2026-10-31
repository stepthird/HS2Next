// The eye side selector and its link (F10): segmented buttons «左目» / «右目»
// (`side-left`, `side-right`; view state, not stored, default left, kept while the app runs) choose which eye the Eyes
// page's `{side}` rows edit — changing it re-expands those rows (bindings re-targeted, no history); the link toggle
// (`field-appearance.chara.face.eyes.linked`, a bound Bool row) writes `face.eyes.linked` (one entry "Edit face") and copies
// nothing (HS2); two buttons copy one eye to the other (`eyes-copy-lr`, `eyes-copy-rl`): per character one set of the
// whole eye object (`face.eyes.right` ← a clone of `face.eyes.left`, or the reverse), one entry "Edit face" for all
// selected characters; with the link on the consequence re-sets the source to its own value (a no-op). Highlight, offset
// and shadow are shared members and are not copied. 4.2–4.5 reuse the selector with other choices (paint 1 / 2, …).
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
    public sealed class SideSelector : VisualElement
    {
        public const string Left = "left", Right = "right";
        public const string LinkedPath = "appearance.chara.face.eyes.linked";
        /// <summary>The side the Eyes page shows (view state, kept while the app runs).</summary>
        public static string EyeSide = Left;

        readonly UiContext ctx;
        readonly TargetSelector sel;

        public SegmentedButtons Buttons { get; }
        public Toggle LinkToggle { get; }
        public VisualElement LinkRow { get; }
        public Button CopyLeftToRight { get; }
        public Button CopyRightToLeft { get; }
        public FieldBinding LinkBinding { get; }
        public string Side => EyeSide;
        /// <summary>Raised after the side changed (the page re-expands its {side} rows).</summary>
        public event Action<string> SideChanged;
        public OpResult LastResult { get; private set; }

        public SideSelector(UiContext ctx, TargetSelector selector, FieldSpec linkSpec = null, string copyLeftToRightKey = null, string copyRightToLeftKey = null)
        {
            this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            sel = selector;
            name = "eyes-side";
            AddToClassList("hs-side-selector");
            Buttons = new SegmentedButtons(new[] { L10n.Get("maker.side.left"), L10n.Get("maker.side.right") });
            Buttons.Buttons[0].name = "side-left";
            Buttons.Buttons[1].name = "side-right";
            Loc.Bind(Buttons.Buttons[0], "maker.side.left");
            Loc.Bind(Buttons.Buttons[1], "maker.side.right");
            Buttons.Selected += i => SetSide(i == 1 ? Right : Left);
            Add(Buttons);

            string linkPath = linkSpec?.Path ?? LinkedPath;
            LinkRow = new VisualElement { name = "field-" + linkPath };
            LinkRow.AddToClassList(FieldRow.UssClass);
            LinkToggle = new Toggle(L10n.Get(linkSpec?.LabelKey ?? "maker.eyes.linked")) { name = "toggle" };
            LinkToggle.AddToClassList("hs-field__toggle");
            Loc.Bind(LinkToggle, linkSpec?.LabelKey ?? "maker.eyes.linked");
            LinkToggle.RegisterValueChangedCallback(e => WriteLinked(e.newValue));
            LinkRow.Add(LinkToggle);
            Add(LinkRow);
            LinkBinding = ctx.Bindings.Bind(LinkRow, new FieldBinding
            {
                Selector = sel, Path = linkPath,
                Apply = (el, v) => { LinkToggle.SetValueWithoutNotify(v.Value is bool b && b); LinkToggle.showMixedValue = v.Mixed; },
            });

            var copies = new VisualElement();
            copies.AddToClassList("hs-row");
            copies.style.flexDirection = FlexDirection.Row;
            CopyLeftToRight = InspectorUi.Button(copyLeftToRightKey ?? "maker.eyes.copyLeftToRight", () => Copy(Left, Right), "eyes-copy-lr");
            CopyRightToLeft = InspectorUi.Button(copyRightToLeftKey ?? "maker.eyes.copyRightToLeft", () => Copy(Right, Left), "eyes-copy-rl");
            CopyLeftToRight.style.flexGrow = 1;
            CopyRightToLeft.style.flexGrow = 1;
            copies.Add(CopyLeftToRight);
            copies.Add(CopyRightToLeft);
            Add(copies);
            ShowSide();
        }

        IReadOnlyList<string> Targets => sel?.Resolve(ctx) ?? new List<string>();

        void ShowSide() => Buttons.SetDisplay(EyeSide == Right ? 1 : 0, false);

        /// <summary>Shows <paramref name="side"/> ("left" / "right"); the page re-expands its rows (no history).</summary>
        public void SetSide(string side)
        {
            side = side == Right ? Right : Left;
            bool changed = side != EyeSide;
            EyeSide = side;
            ShowSide();
            if (changed) SideChanged?.Invoke(side);
        }

        void WriteLinked(bool on)
        {
            var targets = Targets;
            if (targets.Count == 0) return;
            LastResult = ctx.ShowResult(ctx.Ops.EditAppearance(targets, new List<(string, object)> { (LinkRow.name.Substring("field-".Length), on) }, UiContext.User(OpLabels.EditFace)));
            LinkBinding.MarkDirty();
        }

        /// <summary>Copies eye <paramref name="from"/> to <paramref name="to"/> on every selected character (one entry "Edit face";
        /// StudioOps.CopyEye: per character one set of the whole eye object).</summary>
        public OpResult Copy(string from, string to)
        {
            var targets = Targets;
            if (targets.Count == 0 || from == to) return LastResult = OpResult.NothingToDo();
            return LastResult = ctx.ShowResult(ctx.Ops.CopyEye(targets, from == Left, UiContext.User(OpLabels.EditFace)));
        }
    }
}
