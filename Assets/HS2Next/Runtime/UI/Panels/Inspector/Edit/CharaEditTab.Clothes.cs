// The Clothes page of the Edit tab: one page — the kind selector «トップス · ボトムス · インナー上 · インナー下 · 手袋 · パンスト ·
// 靴下 · 靴» (section `clothes.clothes.kind`, PartSelector on PartSelectorState.Clothes, buttons `clothes-kind-<kind>`,
// wrapping to further lines when the inspector is narrower than the labels need; a male primary has no buttons for the
// inner top, inner bottom, pantyhose and socks), the selected kind's group `clothes-kind-group-<kind>` (Type · Colour 1 ·
// Colour 2 · Colour 3 · Other, HS2's tabs), built on first selection and kept, only the selected one attached; then the
// outfit-card section with the coordinate load. The kind sections are live (LiveSections): an item change shows or hides
// the colour sets, the main colour's alpha, the break and the option parts after the flush that swaps the part, a pattern
// set to none hides the pattern colour and layout rows; their rows are kept per path and re-attached, so an open pattern
// list survives the re-evaluation. The kind buttons follow every kind's item: a kind whose realization has no clothes
// prefab is dimmed, the bottom (inner bottom) is disabled with a tooltip while the top (inner top) is a one-piece — a
// disabled kind that is shown falls back to the top; the data stay as stored. Rows drawn here: the pattern of a colour set
// (a button with the current pattern's thumbnail and name opening the 348 list, which is built on first open),
// «初期カラー» (`clothes-default-colours-<kind>`, StudioOps.ResetClothesColours), the outfit-card button
// (`clothes-coordinate-load`) opening the coordinate dialog with HS2's three load modes and (4.6) «保存…»
// (`clothes-coordinate-save`) saving the worn coordinate as a .hs2ncoord file. edit.show takes `kind` (name or 0..7).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed partial class CharaEditTab
    {
        public const string ClothesNode = "clothes";
        const string KindsKey = "<kinds>";
        const string ClothesPathHead = "appearance.coord.clothes.";

        PartSelector clothesKinds;
        VisualElement clothesHost;
        readonly VisualElement[] clothesGroups = new VisualElement[8];
        LiveSections clothesLive;
        // rows of the clothes live sections by row key: re-attached when a section is refilled
        readonly Dictionary<string, VisualElement> clothesRows = new Dictionary<string, VisualElement>(StringComparer.Ordinal);
        int clothesSex = 1;

        /// <summary>The Clothes page's kind selector (null until the Clothes node is built).</summary>
        public PartSelector ClothesKinds => clothesKinds;
        /// <summary>The built kind groups of the Clothes page (null entries: not built yet).</summary>
        public IReadOnlyList<VisualElement> ClothesGroups => clothesGroups;
        /// <summary>Time of the last kind switch of the Clothes page (ms; a first selection includes the group build).</summary>
        public double ClothesKindSwitchMs { get; private set; }
        /// <summary>The Clothes page's live sections (null until the Clothes node is built).</summary>
        public LiveSections ClothesLive => clothesLive;
        /// <summary>Clothes sections still waiting for the flush that realizes a change (tests).</summary>
        public int ClothesPending => clothesLive?.PendingCount ?? 0;
        /// <summary>Re-evaluates the Clothes page's marked live sections now (tests).</summary>
        public void EvaluateClothesNow() => clothesLive?.EvaluateNow();
        /// <summary>The last result of a clothes page button («初期カラー», a coordinate load; tests).</summary>
        public OpResult LastClothesResult { get; private set; }

        int LivePageRebuilds() => (clothesLive?.Rebuilds ?? 0) + (accLive?.Rebuilds ?? 0);
        int LivePageEvaluations() => (clothesLive?.Evaluations ?? 0) + (accLive?.Evaluations ?? 0);

        /// <summary>The kind selector, the host of the selected kind's group (built on first selection), the outfit-card section.</summary>
        VisualElement BuildClothesPage(VisualElement c, List<SchemaSection> sections)
        {
            var sw = Stopwatch.StartNew();
            int sex = StudioOps.SexOf(ctx.Model.Find(primary));
            clothesSex = sex < 0 ? 1 : sex;
            clothesLive = NewLiveSections();
            clothesLive.Fill = FillClothesSection;
            clothesLive.Evaluated = e => RefreshAlphas(e.Fold);
            var itemPaths = new List<string>();
            for (int k = 0; k < InspectorSchemas.ClothesEditKinds.Count; k++) itemPaths.Add(InspectorSchemas.CoordEditPath("clothes." + InspectorSchemas.ClothesEditKinds[k] + ".item"));
            clothesLive.AddSignal(KindsKey, itemPaths, RefreshKindMarks);
            WithGateMemo(() =>
            {
                foreach (var s in sections)
                {
                    if (s.Id == InspectorSchemas.ClothesKindSection)
                    {
                        var fo = InspectorUi.Section(s.LabelKey, true, "edit-section-" + s.Id);
                        sectionFoldouts[s.Id] = fo;
                        var keys = new List<string>();
                        var names = new List<string>();
                        for (int k = 0; k < InspectorSchemas.ClothesEditKinds.Count; k++)
                        {
                            keys.Add(InspectorSchemas.ClothesKindLabelKey(k));
                            names.Add("clothes-kind-" + InspectorSchemas.ClothesEditKinds[k]);
                        }
                        clothesKinds = new PartSelector(PartSelectorState.Clothes, keys, names, "maker.clothes.kindNone", "clothes-kinds", true);
                        for (int k = 0; k < keys.Count; k++) clothesKinds.SetShown(k, InspectorSchemas.ClothesKindShown(k, clothesSex));
                        clothesKinds.PartChanged += ShowClothesKind;
                        fo.Add(clothesKinds);
                        c.Add(fo);
                        clothesHost = new VisualElement { name = "clothes-kind-host" };
                        c.Add(clothesHost);
                    }
                    else if (InspectorSchemas.ClothesKindOf(s.Id) < 0) AddSection(c, s);
                }
                RefreshKindMarks();
            });
            int k0 = PartSelectorState.Clothes.Part;
            if (!clothesKinds.IsShown(k0) || !clothesKinds.IsEnabled(k0)) PartSelectorState.Clothes.Part = k0 = 0;
            ShowClothesKind(k0);
            clothesLive.Attach(c);
            PageBuildMs[ClothesNode + ".clothes"] = sw.Elapsed.TotalMilliseconds;
            return c;
        }

        /// <summary>Shows kind <paramref name="k"/>'s group (built on first use; the other groups are detached, not destroyed).</summary>
        void ShowClothesKind(int k)
        {
            if (clothesHost == null || k < 0 || k >= clothesGroups.Length) return;
            var sw = Stopwatch.StartNew();
            if (clothesGroups[k] == null)
            {
                var g = new VisualElement { name = "clothes-kind-group-" + InspectorSchemas.ClothesEditKinds[k] };
                WithGateMemo(() => { foreach (var s in SectionsOf(ClothesNode)) if (InspectorSchemas.ClothesKindOf(s.Id) == k) AddLiveSection(clothesLive, g, s, k); });
                clothesGroups[k] = g;
                PageBuildMs[ClothesNode + ".group." + InspectorSchemas.ClothesEditKinds[k]] = sw.Elapsed.TotalMilliseconds;
            }
            clothesHost.Clear();
            clothesHost.Add(clothesGroups[k]);
            ClothesKindSwitchMs = sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>The kind buttons after an item change: dimmed without a clothes prefab, the bottom / inner bottom disabled under
        /// a one-piece top / inner top (a disabled kind that is shown falls back to the top).</summary>
        void RefreshKindMarks()
        {
            if (clothesKinds == null) return;
            for (int k = 0; k < InspectorSchemas.ClothesEditKinds.Count; k++)
            {
                if (!clothesKinds.IsShown(k)) continue;
                clothesKinds.SetEnabled(k, GateHolds(SchemaGates.MakerClothes(k, SchemaGates.ClothesKind)), InspectorSchemas.ClothesOnePieceTipKey);
                clothesKinds.SetNone(k, !GateHolds(SchemaGates.MakerClothes(k, SchemaGates.ClothesPrefab)));
            }
            if (!clothesKinds.IsEnabled(clothesKinds.Selected)) clothesKinds.Select(0);
        }

        /// <summary>The rows of a clothes live section: built once per row key, re-attached on every refill.</summary>
        void FillClothesSection(LiveSections.Entry e)
        {
            foreach (var f in e.Section.Fields)
            {
                if (!GateHolds(f.Gate)) continue;
                string key = f.Path + "#" + f.Component + "|" + f.Action;
                if (!clothesRows.TryGetValue(key, out var row))
                {
                    row = BuildRow(f, null) ?? NoRow;
                    clothesRows[key] = row;
                }
                if (row != NoRow) e.Fold.Add(row);
            }
        }

        /// <summary>The main colours' alpha follows its gate without a row rebuild (re-evaluated with the section).</summary>
        static void RefreshAlphas(VisualElement fold)
        {
            fold?.Query<MakerColorRow>().ForEach(r => r.RefreshAlpha());
        }

        /// <summary>The clothes rows this page draws itself (null = the generic widget, NoRow = nothing).</summary>
        VisualElement BuildClothesRow(FieldSpec f, string path)
        {
            if (f.Kind == FieldKind.PartList && path != null && path.StartsWith(ClothesPathHead, StringComparison.Ordinal) && path.EndsWith(".pattern", StringComparison.Ordinal))
            {
                int k = InspectorSchemas.ClothesKindOf("clothes.clothes." + path.Substring(ClothesPathHead.Length));
                return new ClothesPatternRow(ctx, f, sel, path, k < 0 ? "?" : InspectorSchemas.ClothesEditKinds[k], ClothesSetOfPath(path));
            }
            if (f.Kind != FieldKind.Button) return null;
            if (IsActionOf(f, InspectorSchemas.ActionResetClothesColoursPrefix)) return ClothesDefaultsButton(f) ?? NoRow;
            if (f.Action == InspectorSchemas.ActionApplyCoordinate) return CoordinateLoadButton(f);
            if (f.Action == InspectorSchemas.ActionSaveCoordinate) return CoordinateSaveButton(f);
            return null;
        }

        /// <summary>The colour set (1..3) of a clothes member path ("…colors[1].pattern" → 2), or 0.</summary>
        static int ClothesSetOfPath(string path)
        {
            int i = path.IndexOf(".colors[", StringComparison.Ordinal);
            if (i < 0 || i + 9 >= path.Length) return 0;
            char d = path[i + 8];
            return d >= '0' && d <= '2' ? d - '0' + 1 : 0;
        }

        /// <summary>«初期カラー» (`clothes-default-colours-&lt;kind&gt;`): the kind's three colour sets take the worn prefab's defaults on
        /// the selected characters that pass the row's gate (one entry "Edit clothes").</summary>
        VisualElement ClothesDefaultsButton(FieldSpec f)
        {
            int k = InspectorSchemas.ClothesKindOfAction(f.Action);
            if (k < 0) return null;
            var b = InspectorUi.Button(f.LabelKey, () => ResetClothesColours(k, f), "clothes-default-colours-" + InspectorSchemas.ClothesEditKinds[k]);
            if (f.TooltipKey != null) Loc.Tooltip(b, f.TooltipKey);
            return b;
        }

        /// <summary>«初期カラー» of kind <paramref name="k"/> as its button does (tests).</summary>
        public OpResult ResetClothesColours(int k, FieldSpec spec = null)
        {
            spec ??= SectionsOf(ClothesNode).SelectMany(s => s.Fields).FirstOrDefault(x => x.Action == InspectorSchemas.ActionResetClothesColours(k));
            var targets = spec != null ? MakerRowKit.Writable(ctx, spec, sel) : sel.Resolve(ctx);
            if (targets.Count == 0) return LastClothesResult = OpResult.NothingToDo();
            return LastClothesResult = ctx.ShowResult(ctx.Ops.ResetClothesColours(targets, k, UiContext.User(OpLabels.EditClothes)));
        }

        /// <summary>The outfit-card button (`clothes-coordinate-load`): the coordinate dialog of the primary's sex.</summary>
        VisualElement CoordinateLoadButton(FieldSpec f)
        {
            var b = InspectorUi.Button(f.LabelKey, () => OpenCoordinateDialog(), "clothes-coordinate-load");
            if (f.TooltipKey != null) Loc.Tooltip(b, f.TooltipKey);
            return b;
        }

        /// <summary>«保存…» (`clothes-coordinate-save`, 4.6): the coordinate save flow of the primary — the prompt «コーデ名», the
        /// overwrite question, a .hs2ncoord in the primary's sex folder with the typed name as its coord.name (not recorded).</summary>
        VisualElement CoordinateSaveButton(FieldSpec f)
        {
            var b = InspectorUi.Button(f.LabelKey, () => LastClothesResult = InspectorFlows.SaveCoordinate(ctx, new[] { primary }), "clothes-coordinate-save");
            if (f.TooltipKey != null) Loc.Tooltip(b, f.TooltipKey);
            return b;
        }

        /// <summary>The coordinate dialog (the outfit cards of the primary's sex, HS2's three load modes); null without a dialog
        /// layer. The chosen mode loads on every selected character (StudioOps.ApplyCoordinateFile, one entry "Load coordinate").</summary>
        public CoordinateDialog OpenCoordinateDialog()
        {
            if (ctx.Dialogs == null) return null;
            return CoordinateDialog.Open(ctx, clothesSex, (path, mode) =>
            {
                var targets = sel.Resolve(ctx);
                if (targets.Count == 0) return LastClothesResult = OpResult.NothingToDo();
                return LastClothesResult = ctx.ShowResult(ctx.Ops.ApplyCoordinateFile(targets, path, mode, UiContext.User(OpLabels.LoadCoordinate)));
            });
        }

        /// <summary>edit.show on the Clothes node: the kind to show (the argument, else the kind a section id names; -1 = none).
        /// Before the page is built the view state is set so that only that kind's group is built.</summary>
        int PrepareClothesReveal(string node, string section, int kind)
        {
            int ck = node == ClothesNode ? (kind >= 0 && kind < 8 ? kind : InspectorSchemas.ClothesKindOf(section)) : -1;
            if (ck >= 0 && clothesKinds == null) PartSelectorState.Clothes.Part = ck;
            return ck;
        }
    }
}
