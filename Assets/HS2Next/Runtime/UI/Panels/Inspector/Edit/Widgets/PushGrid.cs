// The Maker's push grid: a MakerThumbList without a
// current mark whose cells come from a push source — "facePreset" (the head rows of the primary's sex whose `Preset` column
// is set, MakerLists.PresetHeads, F4; the cell image is the texture named by the row's `Preset` in its ThumbAB),
// "layout:<category>" (the rows of 6 mole / 7 face paint layouts; they carry thumbnails — ThumbTex mole_layout_0n /
// facepaint_layout_0n — so their cells are image cells too, not text cells as first assumed) or "colour:<category>" (4.2:
// the rows of a colour preset list, for 336 the vanilla ids 0..6 only — HS2's seven skin swatch buttons; each cell is a
// 28 × 28 px chip filled with the row's swatch colour, IAppearanceRows.ColourPreset, drawn as ColorSwatch draws a colour;
// the row name is the tooltip; no thumbnail is loaded; 4.3 reuses it for the hair colour presets 305). A click (or Enter on
// the focus cell) = one push op on every selected character, one entry each: StudioOps.ApplyFacePreset(ids, headRef)
// ("Face preset"; a head without its preset asset changes the head, F5), StudioOps.ApplyLayoutPreset(ids, layoutPath,
// rowRef) ("Edit face") or StudioOps.ApplyColourPreset(ids, colourPath, rowRef) ("Edit body"; the colour row next to the
// grid follows through its own binding). No selection mark, no reset; the search field only when there are more than 24
// cells. Element names: the root `push-<source>`, the cells `push-<source>-<ref>` (`push-colour:336-illusion.hs2#3`).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HS2Next.Content;
using HS2Next.Content.Maker;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Panels.Maker;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class PushGrid : MakerThumbList
    {
        public const int SearchAbove = 24;
        /// <summary>The side of a colour chip (px).</summary>
        public const float ChipSize = 28f;

        readonly TargetSelector sel;
        readonly VisualElement holder;
        readonly Dictionary<string, Color> chips = new Dictionary<string, Color>(StringComparer.Ordinal);
        string signature;

        public FieldSpec Spec { get; }
        /// <summary>The push source ("facePreset", "layout:6", "layout:7", "colour:336").</summary>
        public string Source { get; }
        /// <summary>The target path (the layout vector of a layout push; the colour member of a colour push; the face block for
        /// presets).</summary>
        public string Path { get; }
        public int Sex { get; private set; } = 1;
        public OpResult LastResult { get; private set; }
        /// <summary>The colour preset category of a "colour:&lt;c&gt;" grid, else -1.</summary>
        public int ColourCategory { get; }
        public bool IsColour => ColourCategory >= 0;

        public PushGrid(UiContext ctx, FieldSpec spec, TargetSelector selector, string expandedPath = null)
            : base(ctx, spec?.LabelKey, IsColourSpec(spec) ? ChipSize : CellWidth, 1f, IsColourSpec(spec) ? 0f : CellText, !IsColourSpec(spec))
        {
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            sel = selector;
            Source = spec.PushSource ?? PushSources.FacePreset;
            ColourCategory = PushSources.ColourCategory(Source);
            Path = expandedPath ?? spec.Path;
            name = "push-" + Source;
            AddToClassList("hs-partlist--push");
            tools.Add(expandButton);
            Grid.Clicked += Push;
            Grid.Activated += Push;
            currentLabel.style.display = DisplayStyle.None;
            if (IsColour)
            {
                AddToClassList("hs-partlist--colour");
                Grid.Gap = 4f;
                Grid.MinZoom = Grid.MaxZoom = 1f;
                Grid.Zoom = 1f;   // re-applies the row height with the chip gap
                Grid.Fill = i => i?.Ref != null && chips.TryGetValue(i.Ref, out var col) ? col : (Color?)null;
            }
            // v0.3 trial 2: image cells follow the thumbnail size of the page kind (no slider of their own: the search line with
            // the slider shows only above 24 cells); every cell's tooltip ends with what a click does
            UseGridKind(Path);
            Grid.HintKey = "maker.list.hintPush";
            // rows follow the primary's sex (selection changes)
            holder = new VisualElement { name = "push-holder" };
            holder.style.display = DisplayStyle.None;
            Add(holder);
            ctx.Bindings.Bind(holder, new FieldBinding { Selector = sel, Path = InspectorSchemas.CharaEditPath("face.head"), Apply = (el, v) => Refresh(v) });
        }

        static bool IsColourSpec(FieldSpec s) => PushSources.ColourCategory(s?.PushSource) >= 0;

        protected override string CellNameOf(PartItem i) => "push-" + Source + "-" + i.Ref;

        // a colour chip's tooltip is the row name
        protected override string TooltipOf(PartItem i) => IsColour ? i?.Name : base.TooltipOf(i);

        IReadOnlyList<string> Targets => sel?.Resolve(ctx) ?? new List<string>();

        protected override void LanguageChanged() { signature = null; Refresh(default); }

        /// <summary>The swatch colour a chip shows (as stored), or null when the cell has none (tests).</summary>
        public Color? ChipColour(string reference) => reference != null && chips.TryGetValue(reference, out var c) ? c : (Color?)null;

        void Refresh(BoundValues v)
        {
            var targets = Targets;
            var n = targets.Count > 0 ? ctx.Model.Find(targets[0]) : null;
            int sex = StudioOps.SexOf(n);
            Sex = sex < 0 ? 1 : sex;
            style.display = targets.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            var lists = MakerCatalog.For(ctx.App?.Catalog);
            string sig = Source + "|" + Sex + "|" + L10n.Language + "|" + (lists == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(lists));
            if (sig == signature) return;
            signature = sig;
            var sw = Stopwatch.StartNew();
            var items = new List<PartItem>();
            chips.Clear();
            if (lists != null)
            {
                string lang = L10n.Language;
                int layout = PushSources.LayoutCategory(Source);
                if (IsColour)
                {
                    // the swatch rows (336: the vanilla ids 0..6 by id, in list order); the chip shows the row's swatch colour
                    var rows = MakerServices.Rows;
                    foreach (var r in lists.Rows(ColourCategory, Sex))
                    {
                        string rf = r.Key.ToString();
                        if (!MakerEdits.ColourPresetRowAllowed(ColourCategory, rf)) continue;
                        items.Add(new PartItem { Row = r, Ref = rf, Name = MakerLists.NameOf(r, lang), Column = null });
                        if (rows != null && rows.ColourPreset(ColourCategory, rf, out _, out var swatch)) chips[rf] = new Color(swatch.R, swatch.G, swatch.B, 1f);
                    }
                }
                else
                {
                    IEnumerable<ListRow> rows = layout >= 0 ? lists.Rows(layout, Sex) : lists.PresetHeads(Sex);
                    foreach (var r in rows)
                        items.Add(new PartItem { Row = r, Ref = r.Key.ToString(), Name = MakerLists.NameOf(r, lang), Column = Spec.ThumbColumn ?? (layout >= 0 ? PartThumbnails.ThumbColumn : PartThumbnails.PresetColumn) });
                }
            }
            tools.style.display = items.Count > SearchAbove ? DisplayStyle.Flex : DisplayStyle.None;
            SetAll(items);
            Builds++;
            LastBuildMs = sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>Pushes the cell of <paramref name="reference"/> as a click would (tests).</summary>
        public bool Push(string reference)
        {
            var it = all.FirstOrDefault(i => i.Ref == reference);
            if (it == null) return false;
            Push(it);
            return true;
        }

        void Push(PartItem it)
        {
            var targets = Targets;
            if (it?.Row == null || targets.Count == 0) return;
            var o = UiContext.User(string.IsNullOrEmpty(Spec.OpLabel) ? null : Spec.OpLabel);
            OpResult r;
            if (IsColour) r = ctx.Ops.ApplyColourPreset(targets, Path, it.Ref, o);
            else if (PushSources.LayoutCategory(Source) >= 0) r = ctx.Ops.ApplyLayoutPreset(targets, Path, it.Ref, o);
            else r = ctx.Ops.ApplyFacePreset(targets, it.Ref, UiContext.User());
            LastResult = ctx.ShowResult(r);
        }
    }
}
