// The part selector of the Maker pages: segmented buttons choosing which part's sections a page shows — the Hair page's
// «後ろ髪 · 前髪 · 横髪 · エクステ» (`hair-part-back` … `-option`), the Clothes page's kinds «トップス … 靴»
// (`clothes-kind-top` …). The shown part is view state held by a PartSelectorState object per use (PartSelectorState.Hair,
// PartSelectorState.Clothes; kept while the app runs, not stored, no history). A change raises PartChanged (the page
// detaches the shown part group and attaches the selected one). A part whose realization has no prefab (a none row, a
// failed load) shows a dimmed button (`hs-part-selector__button--none`, tooltip) that still opens. A button can be
// disabled with a tooltip (the bottom kinds under a one-piece top) and hidden (the kinds a sex has no list for). With
// wrapping on, the buttons keep their labels' width and wrap to further lines when the inspector is narrower than they need
// (the eight clothes kinds); otherwise they share one line and shorten their labels with an ellipsis.
using System;
using System.Collections.Generic;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    /// <summary>The view state of one part selector use: the part shown (kept while the app runs; not stored, no history).</summary>
    public sealed class PartSelectorState
    {
        /// <summary>The Hair page's part (0 back, 1 front, 2 side, 3 extension).</summary>
        public static readonly PartSelectorState Hair = new PartSelectorState("hair");
        /// <summary>The Clothes page's kind (0 top … 7 shoes).</summary>
        public static readonly PartSelectorState Clothes = new PartSelectorState("clothes");

        public string Id { get; }
        /// <summary>The part shown (index of the selector's buttons).</summary>
        public int Part;

        public PartSelectorState(string id, int part = 0) { Id = id; Part = part; }
        public override string ToString() => Id + " " + Part;
    }

    public sealed class PartSelector : VisualElement
    {
        public const string UssClass = "hs-part-selector", NoneClass = "hs-part-selector__button--none", WrapClass = "hs-segmented--wrap",
            DisabledClass = "hs-part-selector__button--disabled";

        /// <summary>The hair part the Hair page shows; use <see cref="PartSelectorState.Hair"/>.</summary>
        [Obsolete("PartSelectorState.Hair.Part")]
        public static int Part { get => PartSelectorState.Hair.Part; set => PartSelectorState.Hair.Part = value; }

        readonly string[] labelKeys;
        readonly string noneTooltipKey;
        readonly bool[] none, enabled, shown;
        readonly string[] disabledTips;

        public SegmentedButtons Buttons { get; }
        /// <summary>The view state this selector shows and writes.</summary>
        public PartSelectorState State { get; }
        /// <summary>The part shown.</summary>
        public int Selected => State.Part;
        /// <summary>Raised after the part changed (the page swaps its part group).</summary>
        public event Action<int> PartChanged;

        /// <summary>The Hair page's selector (its state is <see cref="PartSelectorState.Hair"/>).</summary>
        public PartSelector(IReadOnlyList<string> labelKeys, IReadOnlyList<string> names, string noneTooltipKey = null, string elementName = "hair-parts")
            : this(PartSelectorState.Hair, labelKeys, names, noneTooltipKey, elementName, false) { }

        /// <param name="state">The view state (one object per use).</param>
        /// <param name="labelKeys">One label key per part.</param>
        /// <param name="names">Element names of the buttons (`hair-part-back`, `clothes-kind-top` …).</param>
        /// <param name="wrap">Buttons keep their labels' width and wrap to further lines.</param>
        public PartSelector(PartSelectorState state, IReadOnlyList<string> labelKeys, IReadOnlyList<string> names, string noneTooltipKey, string elementName, bool wrap)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            this.labelKeys = new string[labelKeys.Count];
            for (int i = 0; i < labelKeys.Count; i++) this.labelKeys[i] = labelKeys[i];
            this.noneTooltipKey = noneTooltipKey;
            none = new bool[labelKeys.Count];
            enabled = new bool[labelKeys.Count];
            shown = new bool[labelKeys.Count];
            disabledTips = new string[labelKeys.Count];
            for (int i = 0; i < enabled.Length; i++) enabled[i] = shown[i] = true;
            name = elementName;
            AddToClassList(UssClass);
            var labels = new string[labelKeys.Count];
            for (int i = 0; i < labels.Length; i++) labels[i] = L10n.Get(labelKeys[i]);
            Buttons = new SegmentedButtons(labels);
            if (wrap) Buttons.AddToClassList(WrapClass);
            for (int i = 0; i < Buttons.Buttons.Count; i++)
            {
                if (names != null && i < names.Count) Buttons.Buttons[i].name = names[i];
                Loc.Bind(Buttons.Buttons[i], labelKeys[i]);
            }
            Buttons.Selected += i => Select(i);
            Add(Buttons);
            if (State.Part < 0 || State.Part >= labels.Length) State.Part = 0;
            Show();
        }

        /// <summary>True when wrapping is on.</summary>
        public bool Wraps => Buttons.ClassListContains(WrapClass);

        void Show() => Buttons.SetDisplay(State.Part, false, enabled);

        /// <summary>Shows part <paramref name="part"/> (no history); raises PartChanged when it changed. A hidden or disabled part
        /// is not selected.</summary>
        public void Select(int part)
        {
            if (part < 0 || part >= labelKeys.Length || !shown[part] || !enabled[part]) return;
            bool changed = part != State.Part;
            State.Part = part;
            Show();
            if (changed) PartChanged?.Invoke(part);
        }

        /// <summary>Dims the button of a part without a prefab (it still opens).</summary>
        public void SetNone(int part, bool isNone)
        {
            if (part < 0 || part >= none.Length) return;
            none[part] = isNone;
            var b = Buttons.Buttons[part];
            b.EnableInClassList(NoneClass, isNone);
            UpdateTooltip(part);
        }

        /// <summary>Whether part <paramref name="part"/>'s button is dimmed (tests).</summary>
        public bool IsNone(int part) => part >= 0 && part < none.Length && none[part];

        /// <summary>Enables or disables a part's button; a disabled button carries <paramref name="tooltipKey"/>'s text after its
        /// label. The page decides what happens to a disabled part that is shown.</summary>
        public void SetEnabled(int part, bool on, string tooltipKey = null)
        {
            if (part < 0 || part >= enabled.Length) return;
            enabled[part] = on;
            disabledTips[part] = on ? null : tooltipKey;
            Buttons.Buttons[part].EnableInClassList(DisabledClass, !on);
            Show();
            UpdateTooltip(part);
        }

        /// <summary>Whether part <paramref name="part"/>'s button is enabled.</summary>
        public bool IsEnabled(int part) => part >= 0 && part < enabled.Length && enabled[part];

        /// <summary>Shows or hides a part's button (a hidden part cannot be selected).</summary>
        public void SetShown(int part, bool on)
        {
            if (part < 0 || part >= shown.Length) return;
            shown[part] = on;
            Buttons.Buttons[part].style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Whether part <paramref name="part"/>'s button is shown.</summary>
        public bool IsShown(int part) => part >= 0 && part < shown.Length && shown[part];

        void UpdateTooltip(int part)
        {
            string t = L10n.Get(labelKeys[part]);
            if (none[part] && noneTooltipKey != null) t += " — " + L10n.Get(noneTooltipKey);
            if (!enabled[part] && disabledTips[part] != null) t += " — " + L10n.Get(disabledTips[part]);
            Buttons.Buttons[part].tooltip = t;
        }
    }
}
