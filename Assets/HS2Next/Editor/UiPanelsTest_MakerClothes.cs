// UiPanelsTest.play group MC (Phase 4.4 package W): the Clothes page of the Edit tab and the Costume tab, in the app with the
// UI in Editor play mode (same harness and Ctx as UiPanelsTest, helpers of the MF / MB / MH groups).
//   page: one page — the kind selector «トップス … 靴» (`clothes-kind-*`, PartSelectorState.Clothes, wrapping), the selected
//     kind's group (Type, Colour 1–3, Other in HS2's order; groups built on first selection and kept, only the selected one
//     attached), the outfit-card section; first show, first kind group and cached kind switch (WU5: ≤ 50 / 50 / 5 ms; WARN
//     above, FAIL above 2×); WU4: every kind button at least as wide as its label in every UI language, at the inspector's
//     width here and at the player's inspector widths (360 / 247 logical px);
//   rows against the clothes facts (MakerServices.Clothes) and the document: colour sets per useColorN0n / useColorA0n, the
//     main colour's alpha per useColorA0n (live, no rebuild), the pattern rows while a pattern is set, «破れ具合» per useBreak,
//     the option toggles per option part, «初期カラー» per prefab;
//   live gates: an item change shows / hides the sets, the alpha, the break and the options after the realizing flush (undo
//     restores them); pattern «なし» hides the pattern colour and layout rows; the one-piece top disables the bottom button
//     (tooltip), undo enables it;
//   writes: «ツヤ» typed (one entry "Edit clothes"), ↺ = the prefab default ("Reset value"), «初期カラー» (one entry, the three
//     sets = the prefab defaults), the pattern list built on the button's first open, a pick = one entry "Change item", the
//     pattern row kept across the section's re-evaluation, «幅» typed 150 → stored 1.5, the alpha 0.5 of a useColorA set;
//   the coordinate dialog (HS2's three modes; a mode without a card keeps the dialog open; «服読込み» = one entry "Load
//     coordinate", the accessories unchanged), the Costume tab (the female coordinates, «読込» = one entry, the clothes state
//     kept), edit.show {kind};
//   male (HERO107): four kind buttons, the rows follow its facts;
//   WU5: the top list (≈ 910 rows) scrolled with thumbnails loading (after the census guard records of WU6), the pending
//     thumbnail loads and the catalog's open bundles before and after.
// Characters: the SFW targets of Tools/targets.json; coordinates: the SFW vanilla coordinate cards «ガーリーブラウス» (female)
// and «作業着» (male), names only; thumbnails are drawn by the grids and counted, never viewed; values are numbers only.
// Run alone: & Tools\unity_batch.ps1 -Method UiPanelsTest.Run -NoQuit -MethodArgs @('-only','MC')
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HS2Next.Content.Maker;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class UiPanelsTest
{
    // The group joins UiPanelsTest.Groups after every domain reload (UiPanelsTest's static constructor runs first).
    [UnityEditor.InitializeOnLoadMethod]
    static void RegisterMakerClothesGroup()
    {
        if (!Groups.Any(g => g.id == "MC")) Groups.Add(("MC", MakerClothesGroup));
    }

    const string ClothesRoot = "appearance.coord.clothes.";
    const double KindSwitchTargetMs = 5;
    const float PlayerInspectorWidth = 360f, PlayerNarrowWidth = 247f;   // logical px (WU4)

    static string McK(int k) => InspectorSchemas.ClothesEditKinds[k];
    static ClothesPart McPart(Ctx c, string id, int k) => c.App.Session.Model.Find(id)?.Appearance?.Stored?.Coord?.Clothes?.Parts[k];
    static ClothesKindFacts McFacts(string id, int k) => MakerServices.Clothes != null && MakerServices.Clothes.TryKind(id, k, out var f) ? f : null;
    static VisualElement McRow(CharaEditTab tab, string member) => MfQ<VisualElement>(tab, "field-" + ClothesRoot + member);
    static bool McRowShown(CharaEditTab tab, string member) => MhShown(McRow(tab, member));
    static ClothesPatternRow McPattern(CharaEditTab tab, int k, int n) => MfQ<ClothesPatternRow>(tab, "clothes-pattern-row-" + McK(k) + "-" + n);
    static MakerColorRow McColour(CharaEditTab tab, int k, int n) => MfQ<MakerColorRow>(tab, "field-" + ClothesRoot + McK(k) + ".colors[" + (n - 1) + "].base");

    /// <summary>The rows of kind k's group checked against the facts and the document: (what, expected, shown).</summary>
    static List<(string what, bool want, bool got)> McExpect(Ctx c, CharaEditTab tab, string id, int k)
    {
        var l = new List<(string, bool, bool)>();
        var f = McFacts(id, k);
        bool svc = MakerServices.Clothes != null;
        bool prefab = !svc || (f != null && f.HasPrefab);
        var part = McPart(c, id, k);
        string p = McK(k) + ".";
        l.Add(("item list", true, McRowShown(tab, p + "item")));
        l.Add(("«初期カラー»", prefab, MhShown(MfQ<VisualElement>(tab, "clothes-default-colours-" + McK(k)))));
        for (int n = 1; n <= 3; n++)
        {
            string s = p + "colors[" + (n - 1) + "].";
            bool set = !svc || (f != null && f.SetShown(n));
            l.Add(("colour " + n, set, McRowShown(tab, s + "base")));
            l.Add(("gloss " + n, set, McRowShown(tab, s + "gloss")));
            l.Add(("metallic " + n, set, McRowShown(tab, s + "metallic")));
            l.Add(("pattern " + n, set, MhShown(McPattern(tab, k, n))));
            bool pat = set && part != null && part.Colors[n - 1].Pattern != DocDefaults.NoneRef;
            l.Add(("pattern colour " + n, pat, McRowShown(tab, s + "patternColor")));
            for (int comp = 0; comp < 4; comp++) l.Add(("layout " + n + "." + comp, pat, MhShown(MfQ<VisualElement>(tab, "field-" + ClothesRoot + s + "layout#" + comp))));
            l.Add(("rotation " + n, pat, McRowShown(tab, s + "rotation")));
            var cr = McColour(tab, k, n);
            if (set && cr != null) l.Add(("alpha " + n, !svc || f.AlphaShown(n), cr.AlphaShown));
        }
        l.Add(("«破れ具合»", !svc || (f != null && f.HasPrefab && f.UseBreak), McRowShown(tab, p + "breakRate")));
        l.Add(("option 1", !svc || (f != null && f.OptionShown(1)), McRowShown(tab, p + "hideOptions[0]")));
        l.Add(("option 2", !svc || (f != null && f.OptionShown(2)), McRowShown(tab, p + "hideOptions[1]")));
        return l;
    }

    static void McCheckRows(Ctx c, CharaEditTab tab, string id, int k, string when)
    {
        var l = McExpect(c, tab, id, k);
        var bad = l.Where(x => x.want != x.got).Select(x => x.what + " " + (x.want ? "missing" : "shown")).ToList();
        c.Check(bad.Count == 0, when + ": " + McK(k) + " rows follow the clothes facts and the document (" + l.Count(x => x.got) + " shown" + (bad.Count > 0 ? "; " + string.Join(", ", bad) : "") + ")");
    }

    static IEnumerator McWaitRows(Ctx c, CharaEditTab tab, string id, int k, Func<bool> extra = null, double sec = 20)
    {
        var ok = new bool[1];
        yield return WaitFor(() => McExpect(c, tab, id, k).All(x => x.want == x.got) && (extra == null || extra()), sec, ok);
        yield return PlayModeHarness.WaitFrames(2);
    }

    /// <summary>Picks <paramref name="reference"/> in kind k's item list and waits until the document and the realization have it.</summary>
    static IEnumerator McPick(Ctx c, CharaEditTab tab, string id, int k, string reference, bool[] done)
    {
        done[0] = false;
        yield return MhSettle(c, tab);
        var list = MfQ<PartList>(tab, "field-" + ClothesRoot + McK(k) + ".item");
        if (list == null || !list.Pick(reference)) { c.Check(false, "kind " + McK(k) + ": " + reference + " is listed and picked"); yield break; }
        var ok = new bool[1];
        yield return WaitFor(() => McPart(c, id, k)?.Item == reference, 60, ok);
        int fl0 = c.App.Runtime.FindChara(id)?.EditFlushes ?? 0;
        yield return WaitFor(() => (c.App.Runtime.FindChara(id)?.EditFlushes ?? 0) > fl0 || McFacts(id, k)?.Item == reference, 10, ok);
        yield return McWaitRows(c, tab, id, k);
        done[0] = McPart(c, id, k)?.Item == reference;
    }

    /// <summary>WU4: every shown kind button against its label's measured width (logical px).</summary>
    static List<string> McNarrowButtons(PartSelector sel, out int lines, out string widths)
    {
        var bad = new List<string>();
        var ys = new HashSet<int>();
        var ws = new List<string>();
        for (int i = 0; i < sel.Buttons.Buttons.Count; i++)
        {
            if (!sel.IsShown(i)) continue;
            var b = sel.Buttons.Buttons[i];
            var rs = b.resolvedStyle;
            var ts = b.MeasureTextSize(b.text ?? "", 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
            float need = ts.x + rs.paddingLeft + rs.paddingRight + rs.borderLeftWidth + rs.borderRightWidth;
            ws.Add(b.name.Substring("clothes-kind-".Length) + " " + rs.width.ToString("0.0") + "/" + need.ToString("0.0"));
            if (rs.width + 0.5f < need) bad.Add(b.name + " " + rs.width.ToString("0.0") + " < " + need.ToString("0.0"));
            ys.Add(Mathf.RoundToInt(b.layout.y));
        }
        lines = ys.Count;
        widths = string.Join(", ", ws);
        return bad;
    }

    static string McCoordFile(Ctx c, int sex, string name)
    {
        string d = CoordinateBrowser.Hs2Dir(c.Ui, sex);
        string p = d == null ? null : Path.Combine(d, name + ".png");
        return p != null && File.Exists(p) ? p : null;
    }

    static IEnumerator MakerClothesGroup(Ctx c)
    {
        var ui = c.Ui;
        PartSelectorState.Clothes.Part = 0;
        yield return NewScene(c);
        var ids = new List<string>();
        yield return AddBuiltChara(c, c.FemaleCard, ids);
        yield return AddBuiltChara(c, c.MaleCard, ids);
        if (ids.Count < 2) yield break;
        string f = ids[0], m = ids[1];
        var lists = MakerCatalog.For(c.App.Catalog);
        c.Check(lists != null && MakerServices.Rows != null, "Maker lists and rows");
        if (lists == null) yield break;
        if (MakerServices.Clothes == null) c.Warning("MakerServices.Clothes is not registered: every prefab feature holds (rows checked against that rule)");
        int u00 = UndoCount(c);
        var cat = c.App.Catalog;
        var pt = HS2Next.UI.Panels.Maker.PartThumbnails.Shared(cat);
        c.Info($"start: open bundles {cat.OpenBundleCount}, thumbnail-only {cat.ThumbnailOnlyCount}, pending thumbnails {pt?.Pending}");

        // ---------------------------------------------------------------- the page (female)
        var tab = MfOpenEdit(c, f);
        yield return PlayModeHarness.WaitFrames(4);
        c.Check(tab != null && tab.panel != null, "the female's Edit tab is shown");
        if (tab == null) yield break;
        int gc0 = GC.CollectionCount(0), ge0 = tab.GateEvaluations;
        tab.ShowNode("clothes");
        int gcs = GC.CollectionCount(0) - gc0, gates = tab.GateEvaluations - ge0;
        yield return PlayModeHarness.WaitFrames(3);
        var page = tab.Pages.TryGetValue("clothes", out var pg0) ? pg0 : null;
        c.Check(page != null && tab.CurrentNode == "clothes" && tab.CurrentPage == null && MfQ<VisualElement>(tab, "edit-pages-clothes") == null,
            "the Clothes node is one page (no page strip)");
        double firstShow = tab.PageBuildMs.TryGetValue("clothes.clothes", out var fs) ? fs : -1;
        double firstGroup = tab.PageBuildMs.TryGetValue("clothes.group.top", out var fg) ? fg : -1;
        var parts0 = tab.PageBuildMs.Where(kv => kv.Key.StartsWith("section.clothes.clothes.", StringComparison.Ordinal)).Select(kv => kv.Key.Substring("section.clothes.clothes.".Length) + " " + kv.Value.ToString("0.0")).ToList();
        c.Info($"WU5 first show breakdown (ms): {string.Join(", ", parts0)}; GC collections (gen 0) during it {gcs}; gate evaluations {gates}");
        TimedShow(c, "WU5: Clothes page first show", firstShow, gcs, PageTargetMs, $"top group {firstGroup:0.0} ms");
        var secs = page?.Query<Foldout>().ToList().Select(x => x.name).Where(n => n.StartsWith("edit-section-clothes.clothes.", StringComparison.Ordinal)).Select(n => n.Substring("edit-section-clothes.clothes.".Length)).ToList() ?? new List<string>();
        var wantSecs = new List<string> { "kind" }.Concat(InspectorSchemas.ClothesKindSections.Select(s => "top." + s)).Concat(new[] { "coordinate" }).ToList();
        c.Check(secs.SequenceEqual(wantSecs), "sections in order: kind, top type / colour1-3 / other, coordinate (" + string.Join(", ", secs) + ")");
        var sel = tab.ClothesKinds;
        var kindBtns = Enumerable.Range(0, 8).Select(k => MfQ<Button>(tab, "clothes-kind-" + McK(k))).ToList();
        c.Check(sel != null && kindBtns.All(b => b != null) && Enumerable.Range(0, 8).All(k => sel.IsShown(k)) && sel.Selected == 0 && sel.Buttons.Index == 0 && sel.Wraps,
            "the kind selector: eight buttons «トップス … 靴» (clothes-kind-top … shoes), Top selected, wrapping on");
        if (sel == null) yield break;

        // WU4: every kind button against its label in every UI language (the CJK labels are the widest), at the inspector's
        // width here and at the widths the player shows: the layout and the text are measured in logical px, which do not
        // depend on the UI scale (a round trip of PanelSettings.scale to 2.19 in this shared session left the dock narrower
        // for the later groups: MB's colour chips measured 26 px instead of 28), so the player's 2.19 is taken as its
        // inspector widths — 360 px (the default) and 247 px (the narrowest seen at 2.19 on a small window)
        {
            string lang0 = L10n.Language;
            foreach (var lang in new[] { "en", "ja", "zh-Hans" })
            {
                c.Host.SetLanguage(lang, save: false);
                yield return PlayModeHarness.WaitFrames(4);
                // a language switch may rebuild the inspector: the measured selector is the one shown now
                bool rebuilt = sel.panel == null;
                tab = MfOpenEdit(c, f) ?? tab;
                tab.ShowNode("clothes");
                yield return PlayModeHarness.WaitFrames(4);
                sel = tab.ClothesKinds ?? sel;
                if (rebuilt) c.Info("WU4 (" + lang + "): the language switch rebuilt the inspector (the selector is measured on the new page)");
                var bad = McNarrowButtons(sel, out int lines, out string widths);
                float w0 = sel.resolvedStyle.width;
                c.Check(bad.Count == 0, $"WU4 ({lang}): every kind button holds its label at the inspector's width {w0:0} px ({lines} line(s); {widths})" + (bad.Count > 0 ? "; narrow: " + string.Join(", ", bad) : ""));
                foreach (float w in new[] { PlayerInspectorWidth, PlayerNarrowWidth })
                {
                    sel.style.width = w;
                    yield return PlayModeHarness.WaitFrames(3);
                    var bad2 = McNarrowButtons(sel, out int lines2, out string widths2);
                    c.Check(bad2.Count == 0, $"WU4 ({lang}): in {w:0} px every kind button holds its label ({lines2} line(s); {widths2})" + (bad2.Count > 0 ? "; narrow: " + string.Join(", ", bad2) : ""));
                }
                sel.style.width = StyleKeyword.Null;
                yield return PlayModeHarness.WaitFrames(2);
            }
            c.Host.SetLanguage(lang0, save: false);
            yield return PlayModeHarness.WaitFrames(4);
            tab = MfOpenEdit(c, f) ?? tab;
            tab.ShowNode("clothes");
            yield return PlayModeHarness.WaitFrames(4);
            sel = tab.ClothesKinds ?? sel;
            kindBtns = Enumerable.Range(0, 8).Select(k => MfQ<Button>(tab, "clothes-kind-" + McK(k))).ToList();
            page = tab.Pages.TryGetValue("clothes", out var pg1) ? pg1 : page;
            c.Check(tab.panel != null && sel.panel != null && sel.Selected == 0, "after the language switches the Clothes page is shown again (Top)");
        }
        McCheckRows(c, tab, f, 0, "first show");
        var topFacts = McFacts(f, 0);
        c.Info("BR-Chan's top: item " + McPart(c, f, 0)?.Item + ", prefab " + topFacts?.PrefabName + ", sets " + string.Join("", Enumerable.Range(1, 3).Select(n => topFacts != null && topFacts.SetShown(n) ? "1" : "0"))
            + ", alpha " + string.Join("", Enumerable.Range(1, 3).Select(n => topFacts != null && topFacts.AlphaShown(n) ? "1" : "0")) + ", break " + topFacts?.UseBreak + ", options " + topFacts?.Option01Count + " / " + topFacts?.Option02Count);
        c.Check(McPattern(tab, 0, 1) == null || McPattern(tab, 0, 1).List == null, "the pattern list of a set is not built with the section (built on the button's first open)");

        // ---------------------------------------------------------------- kind switch, detached groups
        int gcGroup0 = GC.CollectionCount(0);
        sel.Select(1);
        int gcsGroup = GC.CollectionCount(0) - gcGroup0;
        yield return PlayModeHarness.WaitFrames(2);
        double bottomGroup = tab.PageBuildMs.TryGetValue("clothes.group.bottom", out var fg1) ? fg1 : -1;
        TimedShow(c, "WU5: first Bottom group", bottomGroup, gcsGroup, PageTargetMs);
        c.Check(tab.ClothesGroups[0] != null && tab.ClothesGroups[0].panel == null && tab.ClothesGroups[1]?.panel != null && McRow(tab, "top.item") == null,
            "the Top group is detached (kept), the Bottom group attached, no Top row in the tab");
        McCheckRows(c, tab, f, 1, "bottom");
        var topList0 = MfQ<PartList>(tab.ClothesGroups[0], "field-" + ClothesRoot + "top.item");
        yield return PfCheck(c, MfQ<PartList>(tab, "field-" + ClothesRoot + "bottom.item"), "clothes", topList0);
        c.Info("the top list's current cell " + topList0?.CurrentRef + " at position " + topList0?.PositionOf(topList0?.CurrentRef ?? "") + " of " + topList0?.Items.Count);
        var topList = MfQ<PartList>(tab.ClothesGroups[0], "field-" + ClothesRoot + "top.item");
        int binds0 = topList?.Grid.BindCount ?? 0, scrolls0 = topList?.AttachScrolls ?? 0;
        var switches = new List<(double ms, int gcs)>();
        for (int n = 0; n < 6; n++)
        {
            int gcSwitch0 = GC.CollectionCount(0);
            sel.Select(n % 2 == 0 ? 0 : 1);
            int gcsSwitch = GC.CollectionCount(0) - gcSwitch0;
            yield return PlayModeHarness.WaitFrames(2);
            switches.Add((tab.ClothesKindSwitchMs, gcsSwitch));
        }
        double cached = TimedMedian(c, "WU5: cached kind switch", switches, KindSwitchTargetMs);
        c.Check(sel.Selected == 1 && tab.ClothesGroups[1].panel != null, "after the cached switches the Bottom group is shown");
        c.Info($"WU5 (Editor play): Clothes page first show {firstShow:0.0} ms (incl. the Top group {firstGroup:0.0}), first Bottom group {bottomGroup:0.0} ms, cached switch {cached:0.000} ms; top list row binds during the three re-attaches {(topList?.Grid.BindCount ?? 0) - binds0}, scrolls after a re-attach {(topList?.AttachScrolls ?? 0) - scrolls0}");
        sel.Select(0);
        yield return PlayModeHarness.WaitFrames(4);
        c.Check(topList != null && topList.CurrentRef != null && topList.CellOf(topList.CurrentRef) != null, "after the cached switches the top list still shows its current cell (" + topList?.CurrentRef + ")");

        // ---------------------------------------------------------------- «ツヤ», ↺, «初期カラー» (Top, set 1)
        bool set1 = MakerServices.Clothes == null || (topFacts != null && topFacts.SetShown(1));
        if (set1)
        {
            int u1 = UndoCount(c);
            var gloss = MfQ<MakerSliderRow>(tab, "field-" + ClothesRoot + "top.colors[0].gloss");
            c.Check(gloss != null && gloss.Spec.ResetKind == ResetKind.PrefabDefault, "«ツヤ» of set 1 (a prefab-default ↺)");
            if (gloss != null)
            {
                gloss.Write(new[] { f }, 0.3, "mc.typed");
                yield return PlayModeHarness.WaitFrames(2);
                c.Check(Mathf.Abs(McPart(c, f, 0).Colors[0].Gloss - 0.3f) < 1e-6f && UndoCount(c) == u1 + 1 && c.UndoLabel == "Edit clothes", "«ツヤ» typed 30 → stored 0.3, one entry \"Edit clothes\" (" + c.UndoLabel + ")");
                gloss.Reset();
                yield return PlayModeHarness.WaitFrames(2);
                float want = topFacts?.Defaults != null ? topFacts.Defaults[0].Gloss : float.NaN;
                float got = McPart(c, f, 0).Colors[0].Gloss;
                c.Check(c.UndoLabel == "Reset value" && (float.IsNaN(want) || Mathf.Abs(got - want) < 1e-6f), $"↺ «ツヤ» = the prefab default {want:0.####} (stored {got:0.####}, \"{c.UndoLabel}\")");
            }
            int u2 = UndoCount(c);
            var defBtn = MfQ<Button>(tab, "clothes-default-colours-top");
            c.Check(defBtn != null, "«初期カラー» (clothes-default-colours-top)");
            var r = tab.ResetClothesColours(0);
            yield return PlayModeHarness.WaitFrames(2);
            var cols = McPart(c, f, 0).Colors;
            bool eq = topFacts?.Defaults != null && Enumerable.Range(0, 3).All(i => Mathf.Abs(cols[i].Gloss - topFacts.Defaults[i].Gloss) < 1e-6f && Mathf.Abs(cols[i].Metallic - topFacts.Defaults[i].Metallic) < 1e-6f
                && cols[i].Pattern == topFacts.Defaults[i].Pattern && Mathf.Abs(cols[i].Base.R - topFacts.Defaults[i].Base.R) < 1e-6f && Mathf.Abs(cols[i].Rotation - topFacts.Defaults[i].Rotation) < 1e-6f);
            c.Check(r != null && r.Ok && UndoCount(c) == u2 + 1 && c.UndoLabel == "Edit clothes" && (eq || topFacts?.Defaults == null),
                "«初期カラー» → the three sets = the prefab defaults, one entry \"Edit clothes\" (" + r?.Message + ")");
            yield return MfUndoTo(c, u1);
            yield return McWaitRows(c, tab, f, 0);
        }
        else c.Warning("BR-Chan's top shows no colour set 1 (the gloss / defaults steps were not taken)");

        // ---------------------------------------------------------------- the pattern window (Top, set 1)
        if (set1)
        {
            var prow = McPattern(tab, 0, 1);
            c.Check(prow != null && prow.Button.name == "clothes-pattern-top-1" && prow.List == null && !prow.Expanded, "the pattern button of set 1 (clothes-pattern-top-1), list not built");
            if (prow != null)
            {
                string pat0 = McPart(c, f, 0).Colors[0].Pattern;
                prow.SetExpanded(true);
                yield return PlayModeHarness.WaitFrames(3);
                var pl = prow.List;
                int rows348 = lists.Rows(348, 1).Count;
                c.Check(pl != null && pl.Category == 348 && pl.AllItems.Count(i => !i.NotListed) == rows348 && pl.name == "field-" + ClothesRoot + "top.colors[0].pattern",
                    $"the first open builds the 348 list ({pl?.AllItems.Count} cells, {rows348} rows; built in {prow.ListBuildMs:0.0} ms)");
                int u3 = UndoCount(c);
                string pick = pat0 == "illusion.hs2#1" ? "illusion.hs2#2" : "illusion.hs2#1";
                bool picked = pl != null && pl.Pick(pick);
                var ok = new bool[1];
                yield return WaitFor(() => McPart(c, f, 0).Colors[0].Pattern == pick, 10, ok);
                yield return McWaitRows(c, tab, f, 0, () => McRowShown(tab, "top.colors[0].patternColor"));
                c.Check(picked && UndoCount(c) == u3 + 1 && c.UndoLabel == "Change item" && McRowShown(tab, "top.colors[0].patternColor") && MhShown(MfQ<VisualElement>(tab, "field-" + ClothesRoot + "top.colors[0].layout#0")),
                    "a pattern pick = one entry \"Change item\"; the pattern colour and the five layout rows appear");
                c.Check(McPattern(tab, 0, 1) == prow && prow.Expanded && prow.List == pl && prow.CurrentRef == pick && !string.IsNullOrEmpty(prow.CurrentName),
                    "the pattern row is the same element after the section's re-evaluation, still open; the button shows «" + prow.CurrentName + "»");
                var width = MfQ<MakerSliderRow>(tab, "field-" + ClothesRoot + "top.colors[0].layout#0");
                if (width != null)
                {
                    width.Write(new[] { f }, 1.5, "mc.typed.w");
                    yield return PlayModeHarness.WaitFrames(2);
                    c.Check(Mathf.Abs(McPart(c, f, 0).Colors[0].Layout.X - 1.5f) < 1e-6f && width.Spec.LerpClamped, "«幅» typed 150 → stored 1.5 (lerp-clamped row, nothing clamps the stored value)");
                }
                pl?.Pick(DocDefaults.NoneRef);
                yield return WaitFor(() => McPart(c, f, 0).Colors[0].Pattern == DocDefaults.NoneRef, 10, ok);
                yield return McWaitRows(c, tab, f, 0, () => !McRowShown(tab, "top.colors[0].patternColor"), 5);
                c.Check(!McRowShown(tab, "top.colors[0].patternColor") && !MhShown(MfQ<VisualElement>(tab, "field-" + ClothesRoot + "top.colors[0].rotation")) && MhShown(prow),
                    "«なし» hides the pattern colour and the layout rows (the pattern button stays)");
                prow.SetExpanded(false);
                yield return MfUndoTo(c, u3);
                yield return McWaitRows(c, tab, f, 0);
                McCheckRows(c, tab, f, 0, "after the undo of the pattern edits");
            }
        }

        // ---------------------------------------------------------------- live gates: item changes
        int u4 = UndoCount(c);
        var done = new bool[1];
        // one-piece top (240#1 «ワンピース»): the bottom button disabled with the tooltip
        yield return McPick(c, tab, f, 0, "illusion.hs2#1", done);
        {
            var ok = new bool[1];
            yield return WaitFor(() => !sel.IsEnabled(1), 10, ok);
            string tip = kindBtns[1]?.tooltip ?? "";
            c.Check(done[0] && !sel.IsEnabled(1) && !kindBtns[1].enabledSelf && tip.Contains(L10n.Get(InspectorSchemas.ClothesOnePieceTipKey)) && c.UndoLabel == "Change item",
                "«ワンピース» (240#1): the Bottom button is disabled with «" + L10n.Get(InspectorSchemas.ClothesOnePieceTipKey) + "» (tooltip «" + tip + "»)");
            sel.Select(1);
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(sel.Selected == 0, "a disabled kind cannot be selected (Top stays)");
            McCheckRows(c, tab, f, 0, "one-piece top");
        }
        // a useBreak top (240#2 «Tシャツ»)
        yield return McPick(c, tab, f, 0, "illusion.hs2#2", done);
        {
            var ok = new bool[1];
            yield return WaitFor(() => sel.IsEnabled(1), 10, ok);
            var tf = McFacts(f, 0);
            c.Check(done[0] && sel.IsEnabled(1) && (MakerServices.Clothes == null || (tf != null && tf.UseBreak)) && McRowShown(tab, "top.breakRate"),
                "«Tシャツ» (240#2): the Bottom button enabled again, «破れ具合» shown (useBreak " + tf?.UseBreak + ")");
            var br = MfQ<MakerSliderRow>(tab, "field-" + ClothesRoot + "top.breakRate");
            if (br != null)
            {
                int ub = UndoCount(c);
                br.Write(new[] { f }, 0.4, null);
                yield return PlayModeHarness.WaitFrames(2);
                c.Check(Mathf.Abs(McPart(c, f, 0).BreakRate - 0.4f) < 1e-6f && UndoCount(c) == ub + 1 && c.UndoLabel == "Edit clothes", "«破れ具合» 40 → stored 0.4, one entry");
                br.Reset();
                yield return PlayModeHarness.WaitFrames(2);
                c.Check(McPart(c, f, 0).BreakRate == 0f && c.UndoLabel == "Reset value", "↺ «破れ具合» = 0 (\"" + c.UndoLabel + "\")");
            }
            McCheckRows(c, tab, f, 0, "useBreak top");
        }
        // option parts (240#51 «クリスマスポンチョ»)
        yield return McPick(c, tab, f, 0, "illusion.hs2#51", done);
        {
            var tf = McFacts(f, 0);
            var opt = MfQ<MakerToggleRow>(tab, "field-" + ClothesRoot + "top.hideOptions[0]");
            c.Check(done[0] && opt != null && opt.Spec.Inverted && opt.Toggle.value == !McPart(c, f, 0).HideOptions[0],
                "«クリスマスポンチョ» (240#51): «オプションパーツ01を表示» shown (option parts " + tf?.Option01Count + " / " + tf?.Option02Count + "), on = shown");
            if (opt != null)
            {
                int uo = UndoCount(c);
                opt.Write(true);   // the stored value: hideOptions[0] = true
                yield return PlayModeHarness.WaitFrames(2);
                c.Check(McPart(c, f, 0).HideOptions[0] && UndoCount(c) == uo + 1 && c.UndoLabel == "Edit clothes" && !opt.Toggle.value, "toggle off → hideOptions[0] true, one entry \"Edit clothes\"");
            }
            McCheckRows(c, tab, f, 0, "option-part top");
        }
        yield return MfUndoTo(c, u4);
        yield return McWaitRows(c, tab, f, 0);
        McCheckRows(c, tab, f, 0, "after the undo of the item changes");

        // ---------------------------------------------------------------- the live alpha (Bottom 241#9 «ショートレギンス»)
        sel.Select(1);
        yield return PlayModeHarness.WaitFrames(3);
        int u5 = UndoCount(c);
        var colour1 = McColour(tab, 1, 1);
        bool alphaBefore = colour1?.AlphaShown ?? false;
        yield return McPick(c, tab, f, 1, "illusion.hs2#9", done);
        {
            colour1 = McColour(tab, 1, 1);
            var bf = McFacts(f, 1);
            c.Check(done[0] && colour1 != null && colour1.AlphaShown && (MakerServices.Clothes == null || (bf != null && bf.AlphaShown(1))),
                "«ショートレギンス» (241#9, useColorA01): the main colour of set 1 offers alpha (live; before: " + alphaBefore + ")");
            if (colour1 != null && colour1.OpenPicker())
            {
                yield return PlayModeHarness.WaitFrames(2);
                c.Check(colour1.Picker != null && colour1.Picker.Q("alpha") != null, "the picker has the alpha strip");
                colour1.Write(new Color(0.8f, 0.2f, 0.3f, 0.5f));
                colour1.Picker?.Close(true);
                yield return PlayModeHarness.WaitFrames(2);
                c.Check(Mathf.Abs(McPart(c, f, 1).Colors[0].Base.A - 0.5f) < 1e-6f && UndoCount(c) == u5 + 2, "alpha 0.5 stored, one entry for the picker session");
            }
        }
        yield return MfUndoTo(c, u5);
        yield return McWaitRows(c, tab, f, 1);
        c.Check(McColour(tab, 1, 1) == null || McColour(tab, 1, 1).AlphaShown == alphaBefore, "undo: the alpha follows the earlier bottom again (" + McColour(tab, 1, 1)?.AlphaShown + ")");
        sel.Select(0);
        yield return PlayModeHarness.WaitFrames(3);

        // ---------------------------------------------------------------- edit.show {kind}
        {
            ui.Registry.Actions.Run(ActionIds.EditShow, new ActionInvocation { Args = new Dictionary<string, object> { ["node"] = "clothes", ["kind"] = "shoes" } }, out var res);
            yield return PlayModeHarness.WaitFrames(3);
            var et = CharaEditTab.Current ?? tab;
            var d = res as Dictionary<string, object>;
            c.Check(et.ClothesKinds?.Selected == 7 && d != null && (d.TryGetValue("kind", out var kv) ? kv as string : null) == "shoes" && et.ClothesGroups[7]?.panel != null,
                "edit.show {node: clothes, kind: shoes} shows the Shoes group (reply kind " + (d != null && d.TryGetValue("kind", out var kk) ? kk : "-") + ")");
            ui.Registry.Actions.Run(ActionIds.EditShow, new ActionInvocation { Args = new Dictionary<string, object> { ["node"] = "clothes", ["section"] = "clothes.clothes.bottom.colour1" } }, out _);
            yield return PlayModeHarness.WaitFrames(3);
            c.Check(et.ClothesKinds?.Selected == 1, "edit.show {section: clothes.clothes.bottom.colour1} selects the Bottom kind");
            et.ClothesKinds?.Select(0);
            yield return PlayModeHarness.WaitFrames(2);
        }

        // ---------------------------------------------------------------- the coordinate dialog (female, «服読込み»)
        string girly = McCoordFile(c, 1, "ガーリーブラウス");
        if (girly != null)
        {
            int u6 = UndoCount(c);
            var accBefore = string.Join(",", StudioOps.AccessoryIdsOf(c.App.Session.Model.Find(f)) ?? (IReadOnlyList<string>)new List<string>());
            var dlg = tab.OpenCoordinateDialog();
            var ok = new bool[1];
            yield return WaitFor(() => dlg?.Browser != null && dlg.Browser.Ready, 30, ok);
            c.Check(dlg != null && ui.Dialogs.IsModalOpen && ui.Dialogs.Current?.Spec.Id == CoordinateDialog.DialogId && dlg.Browser.Sex == 1 && dlg.Browser.Index.Rows.Count > 0,
                $"the coordinate dialog lists the female outfit cards ({dlg?.Browser?.Index?.Rows.Count} files) with «服読込み» / «装飾読込み» / «全て読込み»");
            if (dlg != null)
            {
                dlg.Press("clothes");
                yield return PlayModeHarness.WaitFrames(2);
                c.Check(ui.Dialogs.IsModalOpen && UndoCount(c) == u6, "a mode without a card keeps the dialog open (no edit)");
                bool chosen = dlg.Browser.Choose(girly);
                dlg.Press("clothes");
                yield return PlayModeHarness.WaitFrames(4);
                var accAfter = string.Join(",", StudioOps.AccessoryIdsOf(c.App.Session.Model.Find(f)) ?? (IReadOnlyList<string>)new List<string>());
                c.Check(chosen && !ui.Dialogs.IsModalOpen && dlg.LastResult != null && dlg.LastResult.Ok && UndoCount(c) == u6 + 1 && c.UndoLabel == "Load coordinate" && accAfter == accBefore,
                    "«ガーリーブラウス» with «服読込み»: one entry \"" + c.UndoLabel + "\", the accessories unchanged (" + dlg.LastResult?.Message + ")");
                var okb = new bool[1];
                yield return WaitFor(() => c.App.Runtime.FindChara(f)?.State == RealizationState.Built, 30, okb);
                tab = CharaEditTab.Current ?? tab;
                yield return McWaitRows(c, tab, f, 0);
                McCheckRows(c, tab, f, 0, "after the coordinate load");
                yield return MfUndoTo(c, u6);
            }
        }
        else c.Warning("the female coordinate «ガーリーブラウス» is not in the install (the dialog load was not taken)");

        // ---------------------------------------------------------------- the Costume tab (female, all)
        {
            var insp = Inspector(c);
            insp?.Chara?.ShowTab(CharaCostumeTab.TabId);
            yield return PlayModeHarness.WaitFrames(3);
            var ct = insp?.Chara?.TabBodies.TryGetValue(CharaCostumeTab.TabId, out var cb) == true ? cb as CharaCostumeTab : null;
            var ok = new bool[1];
            yield return WaitFor(() => ct?.Browser != null && ct.Browser.Ready, 30, ok);
            c.Check(ct != null && ct.Browser != null && ct.Browser.Ready && ct.Browser.Sex == 1 && MfQ<Button>(ct, "costume-load") != null && MfQ<Button>(insp.Chara, "tab-costume") != null,
                "the Costume tab (tab-costume) lists the female outfit cards (" + ct?.Browser?.Index?.Rows.Count + ") with «読込» (costume-load)");
            string jersey = McCoordFile(c, 1, "ジャージ");
            if (ct != null && jersey != null)
            {
                int u7 = UndoCount(c);
                var st0 = (c.App.Session.Model.Find(f)?.State as CharaNodeState)?.State?.Clothes?.ToArray();
                bool chosen = ct.Browser.Choose(jersey);
                var r = ct.Load(ct.Browser.Selected?.FullPath);
                yield return PlayModeHarness.WaitFrames(4);
                var st1 = (c.App.Session.Model.Find(f)?.State as CharaNodeState)?.State?.Clothes?.ToArray();
                c.Check(chosen && r != null && r.Ok && UndoCount(c) == u7 + 1 && c.UndoLabel == "Load coordinate" && st0 != null && st1 != null && st0.SequenceEqual(st1),
                    "«ジャージ» loaded whole: one entry \"" + c.UndoLabel + "\", the clothes state kept (" + r?.Message + ")");
                var okb = new bool[1];
                yield return WaitFor(() => c.App.Runtime.FindChara(f)?.State == RealizationState.Built, 30, okb);
                yield return MfUndoTo(c, u7);
            }
            else if (jersey == null) c.Warning("the female coordinate «ジャージ» is not in the install (the Costume load was not taken)");
        }

        // ---------------------------------------------------------------- male (HERO107): four kinds
        {
            var tm = MfOpenEdit(c, m);
            yield return PlayModeHarness.WaitFrames(4);
            if (tm != null)
            {
                tm.Reveal("clothes", null, null, -1, 0);
                yield return PlayModeHarness.WaitFrames(3);
                var ms = tm.ClothesKinds;
                var shownM = Enumerable.Range(0, 8).Where(k => ms != null && ms.IsShown(k)).Select(McK).ToList();
                c.Check(ms != null && shownM.SequenceEqual(new[] { "top", "bottom", "gloves", "shoes" }), "HERO107: four kind buttons (" + string.Join(", ", shownM) + ")");
                McCheckRows(c, tm, m, 0, "male top");
                ms?.Select(4);
                yield return PlayModeHarness.WaitFrames(3);
                McCheckRows(c, tm, m, 4, "male gloves");
                var mf = Enumerable.Range(0, 8).Select(k => McFacts(m, k)).ToList();
                c.Info("HERO107 facts: " + string.Join("; ", Enumerable.Range(0, 8).Where(k => mf[k] != null).Select(k => McK(k) + " " + mf[k].PrefabName + " break " + mf[k].UseBreak)));
                var mInsp = Inspector(c);
                mInsp?.Chara?.ShowTab(CharaCostumeTab.TabId);
                yield return PlayModeHarness.WaitFrames(3);
                var mct = mInsp?.Chara?.TabBodies.TryGetValue(CharaCostumeTab.TabId, out var mcb) == true ? mcb as CharaCostumeTab : null;
                var ok = new bool[1];
                yield return WaitFor(() => mct?.Browser != null && mct.Browser.Ready, 30, ok);
                string work = McCoordFile(c, 0, "作業着");
                c.Check(mct?.Browser != null && mct.Browser.Sex == 0 && (work == null || mct.Browser.Index.Rows.Any(e => string.Equals(e.FullPath, work, StringComparison.OrdinalIgnoreCase))),
                    "the male's Costume tab lists the male outfit cards (" + mct?.Browser?.Index?.Rows.Count + ", «作業着» among them)");
                mInsp?.Chara?.ShowTab(CharaEditTab.TabId);
                yield return PlayModeHarness.WaitFrames(2);
            }
            else c.Check(false, "the male's Edit tab");
        }

        // ---------------------------------------------------------------- WU5: the top list scrolled with thumbnails (after WU6's records)
        tab = MfOpenEdit(c, f);
        yield return PlayModeHarness.WaitFrames(4);
        if (tab != null)
        {
            tab.Reveal("clothes", null, null, -1, 0);
            yield return PlayModeHarness.WaitFrames(3);
            yield return MhSettle(c, tab);
            var list = MfQ<PartList>(tab, "field-" + ClothesRoot + "top.item");
            if (list != null)
            {
                int open0 = cat.OpenBundleCount, thumbOnly0 = cat.ThumbnailOnlyCount;
                HS2Next.UI.Panels.Maker.PartThumbnails.LogLoads = true;   // names each asynchronous thumbnail load in the log (a load that removes the GPU device)
                list.SetExpanded(true);
                yield return PlayModeHarness.WaitFrames(3);
                yield return McScroll(c, list, "Top list (" + list.Items.Count + " rows), pass 1");
                yield return McScroll(c, list, "Top list, pass 2 (warm)");
                var tp = list.Thumbnails;
                int pendingPeak = tp?.Pending ?? 0;
                var okp = new bool[1];
                yield return WaitFor(() => tp == null || tp.Pending == 0, 90, okp);
                c.Check(okp[0], "the top list's queued thumbnails finished (" + tp?.Pending + " pending; " + pendingPeak + " after the scroll)");
                list.SetExpanded(false);
                list.ScrollToCurrent();
                HS2Next.UI.Panels.Maker.PartThumbnails.LogLoads = false;
                if (tp != null) c.Info($"WU5 thumbnails: from bundles {tp.FromBundles}, from the disk cache {tp.FromDiskCount}, loose {tp.FromLoose}, failed {tp.FailedCount}, guard-skipped {tp.GuardSkipped}, mip<1 rejected {tp.BadMipRejected}, pending {tp.Pending} (after the scroll {pendingPeak}); open bundles {open0} → {cat.OpenBundleCount}, thumbnail-only {thumbOnly0} → {cat.ThumbnailOnlyCount}");
            }
            else c.Check(false, "the top list (field-…top.item)");
        }

        yield return MfUndoTo(c, u00);
        PartSelectorState.Clothes.Part = 0;
        c.Info($"end: open bundles {cat.OpenBundleCount}, thumbnail-only {cat.ThumbnailOnlyCount}, pending thumbnails {pt?.Pending}");
    }
    /// <summary>WU5: scrolls a list page by page (one page per frame, thumbnails loading) and reports the frame times and row binds.</summary>
    static IEnumerator McScroll(Ctx c, PartList pl, string what)
    {
        var grid = pl.Grid;
        int rows = Math.Max(1, (pl.Items.Count + Math.Max(1, grid.Columns) - 1) / Math.Max(1, grid.Columns));
        int binds0 = grid.BindMs.Count;
        var frames = new List<double>();
        int step = Math.Max(1, MakerThumbList.ExpandedRows);
        for (int r = 0; r < rows; r += step)
        {
            grid.List.ScrollToItem(Math.Min(r, rows - 1));
            yield return null;
            frames.Add(Time.unscaledDeltaTime * 1000.0);
            if (frames.Count > 600) break;
        }
        for (int i = 0; i < 30; i++) { yield return null; frames.Add(Time.unscaledDeltaTime * 1000.0); }
        var binds = grid.BindMs.Skip(binds0).ToList();
        double perCell = binds.Count > 0 ? binds.Average() / Math.Max(1, grid.Columns) : double.NaN;
        var tp = pl.Thumbnails;
        c.Info($"WU5 {what}: {frames.Count} frames, frame median {Median(frames):0.0} ms, p95 {Pct(frames, 0.95):0.0}, max {frames.DefaultIfEmpty(0).Max():0.0}; row binds {binds.Count}, median {Median(binds):0.000} ms per grid row ({grid.Columns} cells, {perCell:0.000} ms per cell); pending thumbnails {tp?.Pending}, open bundles {c.App.Catalog.OpenBundleCount}");
        if (Pct(frames, 0.95) > 20) c.Warning($"WU5 {what}: p95 frame {Pct(frames, 0.95):0.0} ms above 20 ms (Editor batch play frame times include the harness)");
        c.Check(binds.Count > 0, "WU5 " + what + ": the grid bound rows while scrolling");
    }
}
