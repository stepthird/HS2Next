// UiPanelsTest.play group MB (Phase 4.2 package W): the Body pages of the
// Edit tab, in the app with the UI in Editor play mode (same harness and Ctx as UiPanelsTest, helpers of the MF group).
//   sub-pages: the strip in InspectorSchemas.BodyPages order, Shape shown first, the six Shape sections with 36 rows for a
//     female; the male (HERO107) Shape page without the female-only rows and with the height row while
//     CharaAssembler.MaleHeightPin075 is off (29 rows), without it when on (28), the Special page hidden for males; first
//     show of every page (UB5, ≤ 50 ms; WARN above, FAIL above 2×);
//   swatch grid: 7 colour chips (vanilla ids 0..6 of 336, the swatch colours, no thumbnails, no mark), a push = one entry
//     "Edit body" with HSVToRGB(Base) and alpha 1, the colour row follows, a push of the current value adds none;
//   region PartList: rows = MakerLists.Rows(8), the current mark, a pick keeps layout / rotation (one entry "Change item"),
//     the pinned not-listed cell for a missing region; paint {k} sections (paint 1 / 2 expanded on [0] / [1]);
//   ↺ = the reference value on a body row; component sliders on two characters; gated rows write only the selected
//     characters that pass the gate (female primary + male: the † rows and the futanari toggle skip the male);
//   the futanari toggle (Special): one entry "Edit body", its tooltip; the option maker.bodyThrottle → CharaEditFlush;
//   ThumbGrid cells that show no entry are named `cell-hidden`; ui.input Delete / Backspace / Ctrl+A edit the paint list's
//     search (UiCommands.Send, the bridge's path); the paint list (≈1,400 rows) scrolled with thumbnails loading (UB5).
// Characters: the SFW targets of Tools/targets.json; body thumbnails are drawn by the grid and counted, never viewed; body
// values are checked as numbers only.
// Run alone: & Tools\unity_batch.ps1 -Method UiPanelsTest.Run -NoQuit -MethodArgs @('-only','MB')
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Chara;
using HS2Next.Content;
using HS2Next.Content.Maker;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Binding;
using HS2Next.UI.Bridge;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class UiPanelsTest
{
    // The group joins UiPanelsTest.Groups after every domain reload (UiPanelsTest's static constructor runs first).
    [UnityEditor.InitializeOnLoadMethod]
    static void RegisterMakerBodyGroup()
    {
        if (!Groups.Any(g => g.id == "MB")) Groups.Add(("MB", MakerBodyGroup));
        if (!Groups.Any(g => g.id == "MBW1")) Groups.Add(("MBW1", MakerBodyW1Group));
        if (!Groups.Any(g => g.id == "MBW2")) Groups.Add(("MBW2", MakerBodyW2Group));
    }

    const string BodyRoot = "appearance.chara.body.";
    const double PageTargetMs = 50;

    /// <summary>The Maker rows of a built page (sliders, colours, lists, push grids, toggles), in order.</summary>
    static List<VisualElement> MbRows(VisualElement page) =>
        page == null ? new List<VisualElement>() : page.Query<VisualElement>().Where(e => e is MakerSliderRow || e is MakerColorRow || e is PartList || e is PushGrid || e is MakerToggleRow).ToList();

    static string MbRowPath(VisualElement e) => e.name.StartsWith("field-", StringComparison.Ordinal) ? e.name.Substring(6) : e.name;

    static IEnumerator MbSend(Ctx c, string json, int frames = 1)
    {
        UiCommands.Send(c.Host, JObject.Parse(json), out _);
        yield return PlayModeHarness.WaitFrames(frames);
    }

    static IEnumerator MakerBodyGroup(Ctx c)
    {
        var ui = c.Ui;
        bool pin0 = CharaAssembler.MaleHeightPin075;
        var throttle0 = CharaEditFlush.BodyThrottle;
        CharaAssembler.MaleHeightPin075 = false;
        yield return NewScene(c);
        var ids = new List<string>();
        yield return AddBuiltChara(c, c.FemaleCard, ids);
        yield return AddBuiltChara(c, c.MaleCard, ids);
        if (ids.Count < 2) yield break;
        string f = ids[0], m = ids[1];
        var lists = MakerCatalog.For(c.App.Catalog);
        c.Check(lists != null && MakerServices.Rows != null, "Maker lists and rows of the catalog");
        if (lists == null || MakerServices.Rows == null) yield break;

        // ---------------------------------------------------------------- sub-pages (female)
        bool lastBodyShape = !CharaEditTab.LastPage.TryGetValue("body", out var lp0) || lp0 == "shape";
        var tab = MfOpenEdit(c, f);
        yield return PlayModeHarness.WaitFrames(4);
        c.Check(tab != null && tab.panel != null, "the female's Edit tab is shown");
        if (tab == null) yield break;
        tab.ShowNode("body");
        yield return PlayModeHarness.WaitFrames(3);
        c.Check(lastBodyShape && tab.CurrentNode == "body" && tab.CurrentPage == "shape", "the Body node opens on Shape (" + tab.CurrentPage + ")");
        var strip = MfQ<VisualElement>(tab, "edit-pages-body");
        var buttons = strip?.Query<Button>().ToList() ?? new List<Button>();
        var want = InspectorSchemas.BodyPages.Select(p => "edit-page-body-" + p).ToList();
        c.Check(buttons.Select(b => b.name).SequenceEqual(want), "the Body page strip in BodyPages order (" + string.Join(", ", buttons.Select(b => b.name.Substring("edit-page-body-".Length))) + ")");
        c.Check(buttons.Count == 8 && buttons.All(b => b.style.display != DisplayStyle.None), "female: the eight Body page buttons are shown (Special included)");
        var shape = tab.SubPages.TryGetValue("body.shape", out var sp) ? sp : null;
        var sections = shape?.Query<Foldout>().ToList().Select(x => x.name).Where(n => n.StartsWith("edit-section-body.shape.", StringComparison.Ordinal)).ToList() ?? new List<string>();
        c.Check(sections.SequenceEqual(InspectorSchemas.BodyShapeSections.Select(s => "edit-section-body.shape." + s.section)), "Shape: the six sections in HS2's order (" + string.Join(", ", sections.Select(s => s.Substring("edit-section-body.shape.".Length))) + ")");
        var shapeRows = MbRows(shape).OfType<MakerSliderRow>().ToList();
        var wantRows = new List<string>();
        foreach (var (_, rows) in InspectorSchemas.BodyShapeSections)
            foreach (int i in rows)
                wantRows.Add(i == InspectorSchemas.BodyBustSoftnessRow ? BodyRoot + "bust.softness" : i == InspectorSchemas.BodyBustWeightRow ? BodyRoot + "bust.weight"
                    : i == InspectorSchemas.BodyAreolaSizeRow ? BodyRoot + "nipple.areola" : BodyRoot + "shape[" + i + "]");
        c.Check(shapeRows.Count == 36 && shapeRows.Select(r => r.Path).SequenceEqual(wantRows), "female Shape page: 36 rows in HS2's UI order (" + shapeRows.Count + ")");

        // UB5: first show of every Body page
        var ms = new List<string>();
        foreach (var p in InspectorSchemas.BodyPages)
        {
            tab.Reveal("body", p);
            yield return PlayModeHarness.WaitFrames(2);
            double t = tab.PageBuildMs.TryGetValue("body." + p, out var v) ? v : -1;
            ms.Add(p + " " + t.ToString("0.0"));
            c.Check(tab.CurrentPage == p && t >= 0 && t <= 2 * PageTargetMs, "page " + p + " shown, first show " + t.ToString("0.0") + " ms (≤ " + (2 * PageTargetMs) + ")");
            if (t > PageTargetMs) c.Warning($"UB5: Body page {p} first show {t:0.0} ms above the {PageTargetMs} ms target");
        }
        c.Info("UB5 first show of the Body pages (ms, female): " + string.Join(", ", ms) + "; Shape rows " + shapeRows.Count);
        var counts = new Dictionary<string, int>();
        foreach (var p in InspectorSchemas.BodyPages)
        {
            var rows = MbRows(tab.SubPages.TryGetValue("body." + p, out var pg) ? pg : null);
            counts["sliders"] = counts.TryGetValue("sliders", out var a0) ? a0 + rows.OfType<MakerSliderRow>().Count() : rows.OfType<MakerSliderRow>().Count();
            counts["colours"] = counts.TryGetValue("colours", out var a1) ? a1 + rows.OfType<MakerColorRow>().Count() : rows.OfType<MakerColorRow>().Count();
            counts["lists"] = counts.TryGetValue("lists", out var a2) ? a2 + rows.OfType<PartList>().Count() : rows.OfType<PartList>().Count();
            counts["push"] = counts.TryGetValue("push", out var a3) ? a3 + rows.OfType<PushGrid>().Count() : rows.OfType<PushGrid>().Count();
            counts["toggles"] = counts.TryGetValue("toggles", out var a4) ? a4 + rows.OfType<MakerToggleRow>().Count() : rows.OfType<MakerToggleRow>().Count();
        }
        c.Check(counts["sliders"] == 55 && counts["colours"] == 7 && counts["lists"] == 9 && counts["push"] == 1 && counts["toggles"] == 1,
            "female Body pages: 55 sliders / 7 colours / 9 lists / 1 swatch grid / 1 toggle (" + string.Join(", ", counts.Select(kv => kv.Key + " " + kv.Value)) + ")");

        // ---------------------------------------------------------------- swatch grid
        tab.Reveal("body", "skin");
        yield return PlayModeHarness.WaitFrames(3);
        var grid = MfQ<PushGrid>(tab, "push-colour:336");
        c.Check(grid != null && grid.IsColour && grid.ColourCategory == 336, "skin swatch grid built (push-colour:336)");
        if (grid != null)
        {
            var refs = grid.Items.Select(i => i.Ref).ToList();
            var want7 = Enumerable.Range(0, 7).Select(i => "illusion.hs2#" + i).ToList();
            c.Check(refs.SequenceEqual(want7), "7 chips = vanilla ids 0..6 of 336 (" + string.Join(", ", refs) + ")");
            bool chipsOk = true;
            foreach (var r in refs)
            {
                if (!MakerServices.Rows.ColourPreset(336, r, out _, out var sw) || !(grid.ChipColour(r) is Color cc)) { chipsOk = false; continue; }
                if (Mathf.Abs(cc.r - sw.R) > 1e-6f || Mathf.Abs(cc.g - sw.G) > 1e-6f || Mathf.Abs(cc.b - sw.B) > 1e-6f) chipsOk = false;
            }
            c.Check(chipsOk, "each chip shows its row's swatch colour (Sample H/S/V)");
            var cell3 = grid.Grid.Q("push-colour:336-illusion.hs2#3");
            var img3 = cell3?.Q(className: "hs-card__image");
            // v0.3 trial 2: the tooltip is the row name, then the grid's hint (what a click does)
            c.Check(cell3 != null && img3 != null && grid.ChipColour("illusion.hs2#3") is Color c3 && img3.style.backgroundColor.value == c3 && cell3.tooltip != null && cell3.tooltip.Split('\n')[0] == grid.Items[3].Name,
                "the «タン» chip cell is drawn with its colour, the row name first in the tooltip (" + cell3?.tooltip + ")");
            c.Check(grid.Grid.IsCurrent == null && grid.Thumbnails == null && grid.Grid.Source == null, "no current mark, no thumbnails");
            float cw = cell3?.resolvedStyle.width ?? 0;
            c.Check(Mathf.Abs(cw - PushGrid.ChipSize) < 0.6f, "chip width " + cw.ToString("0.0") + " px (28)");
            int n = UndoCount(c);
            string before = MfText(c, f, BodyRoot + "skin.color");
            ContentId.TryParse("illusion.hs2#3", out var k3);
            lists.ColourPreset(336, k3, out var hsv, out _);
            var exp = Color.HSVToRGB(hsv.H, hsv.S, hsv.V);
            c.Check(grid.Push("illusion.hs2#3"), "push «タン» (illusion.hs2#3)");
            yield return PlayModeHarness.WaitFrames(3);
            var got = InspectorValues.C4(MfGet(c, f, BodyRoot + "skin.color"));
            c.Check(grid.LastResult != null && grid.LastResult.Ok && got.R == exp.r && got.G == exp.g && got.B == exp.b && got.A == 1f && UndoCount(c) == n + 1 && c.UndoLabel == OpLabels.EditBody,
                "push → body.skin.color = HSVToRGB(" + hsv + ") alpha 1 bit for bit, one entry \"" + c.UndoLabel + "\" (" + MfText(c, f, BodyRoot + "skin.color") + ")");
            var colourRow = MfQ<MakerColorRow>(tab, "field-" + BodyRoot + "skin.color");
            c.Check(colourRow != null && !colourRow.Swatch.Alpha && Mathf.Abs(colourRow.Swatch.Value.r - exp.r) < 1e-6f && Mathf.Abs(colourRow.Swatch.Value.b - exp.b) < 1e-6f,
                "the skin colour row (no alpha) follows the push");
            int n1 = UndoCount(c);
            grid.Push("illusion.hs2#3");
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(UndoCount(c) == n1, "a push of the current value adds no entry (" + grid.LastResult?.Code + ")");
            yield return MfUndoTo(c, n);
            c.Check(MfText(c, f, BodyRoot + "skin.color") == before, "undo restores the skin colour");
        }

        // ---------------------------------------------------------------- paint sections, region list
        tab.Reveal("body", "paint");
        yield return PlayModeHarness.WaitFrames(3);
        var p1 = MfQ<VisualElement>(tab, "edit-section-body.paint.p1");
        var p2 = MfQ<VisualElement>(tab, "edit-section-body.paint.p2");
        c.Check(p1 != null && p2 != null && MfQ<PartList>(p1, "field-" + BodyRoot + "paint[0].item") != null && MfQ<PartList>(p2, "field-" + BodyRoot + "paint[1].item") != null
            && MbRows(p1).OfType<MakerSliderRow>().Count() == 7 && MbRows(p2).OfType<MakerSliderRow>().Count() == 7,
            "Paint 1 / 2 sections with the rows of paint [0] / [1] (7 sliders each)");
        var region = MfQ<PartList>(p1, "field-" + BodyRoot + "paint[0].layoutItem");
        c.Check(region != null, "paint 1 region list built");
        if (region != null)
        {
            var rr = lists.Rows(8, 1).Select(r => r.Key.ToString()).ToList();
            c.Check(region.Items.Where(i => !i.NotListed).Select(i => i.Ref).SequenceEqual(rr) && rr.Count >= 20, "region rows = MakerLists.Rows(8) (" + rr.Count + ")");
            string curRegion = (string)MfGet(c, f, BodyRoot + "paint[0].layoutItem");
            c.Check(region.CurrentRef == curRegion && region.Items.Any(i => region.Grid.IsCurrent(i)), "the stored region is marked (" + curRegion + ")");
            string pick = curRegion == "illusion.hs2#2" ? "illusion.hs2#3" : "illusion.hs2#2";
            string layout0 = MfText(c, f, BodyRoot + "paint[0].layout"), rot0 = MfText(c, f, BodyRoot + "paint[0].rotation");
            int n = UndoCount(c);
            c.Check(region.Pick(pick), "region row " + pick + " is a cell");
            yield return WaitFor(() => region.PendingRef == null, 30, new bool[1]);
            yield return PlayModeHarness.WaitFrames(3);
            c.Check(MfText(c, f, BodyRoot + "paint[0].layoutItem") == InspectorValues.Text(pick) && MfText(c, f, BodyRoot + "paint[0].layout") == layout0 && MfText(c, f, BodyRoot + "paint[0].rotation") == rot0
                && UndoCount(c) == n + 1 && c.UndoLabel == OpLabels.ChangeItem, "region pick → layoutItem " + pick + ", layout and rotation unchanged, one entry \"" + c.UndoLabel + "\"");
            yield return MfUndoTo(c, n);
            // a region that does not resolve: the pinned "not listed" cell (paint 2)
            var region2 = MfQ<PartList>(p2, "field-" + BodyRoot + "paint[1].layoutItem");
            // (a card whose mod region is not installed: the whole paint set as one object, so the planner's row check of a
            // `layoutItem` pick does not apply — as a card import or a whole-object bridge set)
            const string missing = "hs2next.test.missing#77";
            var cur = MfGet(c, f, BodyRoot + "paint[1]") as Paint;
            var mp = cur == null ? null : new Paint { Item = cur.Item, Color = cur.Color, Gloss = cur.Gloss, Metallic = cur.Metallic, LayoutItem = missing, Layout = cur.Layout, Rotation = cur.Rotation };
            var mr = mp == null ? null : ui.ShowResult(ui.Ops.EditAppearance(new[] { f }, new List<(string, object)> { (BodyRoot + "paint[1]", mp) }, UiContext.User()));
            yield return PlayModeHarness.WaitFrames(4);
            var pin = region2?.Items.FirstOrDefault();
            c.Check(pin != null && pin.NotListed && pin.Ref == missing && region2.Grid.Q("part-notlisted") != null, "a missing region shows the pinned \"not listed\" cell (" + pin?.Ref + "; " + mr?.Code + " " + mr?.Message + ")");
            yield return MfUndoTo(c, n);
        }

        // ---------------------------------------------------------------- ↺ = reference value
        tab.Reveal("body", "skin");
        yield return PlayModeHarness.WaitFrames(2);
        var gloss = MfQ<MakerSliderRow>(tab, "field-" + BodyRoot + "skin.gloss");
        if (gloss != null && MakerServices.References.TryGet(f, BodyRoot + "skin.gloss", out var refGloss))
        {
            int n = UndoCount(c);
            gloss.Write(new[] { f }, 0.93, null);
            int nr = UndoCount(c);
            gloss.Reset();
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(MfText(c, f, BodyRoot + "skin.gloss") == InspectorValues.Text(refGloss) && UndoCount(c) == nr + 1 && c.UndoLabel == OpLabels.ResetValue,
                "↺ on «ツヤ» → the reference value " + InspectorValues.Text(refGloss) + ", one entry \"" + c.UndoLabel + "\"");
            c.Check(gloss.Control.LabelElement.tooltip == L10n.Get("maker.slider.lerpTooltip"), "«ツヤ» (lerp-mapped) has the 0–100 tooltip");
            yield return MfUndoTo(c, n);
        }
        else c.Check(false, "skin gloss row and its reference value");

        // ---------------------------------------------------------------- futanari toggle (Special)
        tab.Reveal("body", "special");
        yield return PlayModeHarness.WaitFrames(2);
        var fut = MfQ<MakerToggleRow>(tab, "field-appearance.chara.profile.futanari");
        c.Check(fut != null && fut.Toggle.tooltip == L10n.Get("inspector.charaEdit.body.futanariTip") && fut.Toggle.tooltip.Length > 0, "the futanari toggle with its tooltip (data only in Studio)");
        if (fut != null)
        {
            int n = UndoCount(c);
            bool cur = MfGet(c, f, "appearance.chara.profile.futanari") is bool b0 && b0;
            fut.Toggle.value = !cur;
            yield return PlayModeHarness.WaitFrames(2);
            c.Check((MfGet(c, f, "appearance.chara.profile.futanari") is bool b1 && b1) == !cur && UndoCount(c) == n + 1 && c.UndoLabel == OpLabels.EditBody,
                "toggle → profile.futanari " + !cur + ", one entry \"" + c.UndoLabel + "\"");
            yield return MfUndoTo(c, n);
            c.Check((MfGet(c, f, "appearance.chara.profile.futanari") is bool b2 && b2) == cur && fut.Toggle.value == cur, "undo restores the toggle and its row");
        }

        // ---------------------------------------------------------------- option maker.bodyThrottle → CharaEditFlush.BodyThrottle
        ui.Options.Set(CharaEditTab.BodyThrottleOption, "off");
        bool off = CharaEditFlush.BodyThrottle == BodyThrottleMode.Off;
        ui.Options.Set(CharaEditTab.BodyThrottleOption, "always");
        bool always = CharaEditFlush.BodyThrottle == BodyThrottleMode.Always;
        ui.Options.Set(CharaEditTab.BodyThrottleOption, "auto");
        c.Check(off && always && CharaEditFlush.BodyThrottle == BodyThrottleMode.Auto, "option maker.bodyThrottle off / always / auto → CharaEditFlush.BodyThrottle");

        // ---------------------------------------------------------------- ThumbGrid hidden cells, ui.input text keys (paint list search)
        tab.Reveal("body", "paint");
        yield return PlayModeHarness.WaitFrames(3);
        var paint = MfQ<PartList>(tab, "field-" + BodyRoot + "paint[0].item");
        c.Check(paint != null && paint.Items.Count > 100, "paint 1 list built (" + paint?.Items.Count + " cells)");
        if (paint != null)
        {
            // a search for one row: the unused cells (columns beyond the width, cells past the last entry) are named cell-hidden
            var sr = paint.Items.Where(i => !i.NotListed && i.Row != null && !i.Row.IsVanilla).Skip(3).FirstOrDefault() ?? paint.Items.FirstOrDefault(i => !i.NotListed);
            string word = (sr?.Name ?? "").Split(new[] { ' ', '　', '_', '(' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            paint.Search(word);
            yield return PlayModeHarness.WaitFrames(3);
            var cells = paint.Grid.Query(className: "hs-card").ToList();
            var hidden = cells.Where(e => e.style.visibility.value == Visibility.Hidden || e.style.display.value == DisplayStyle.None).ToList();
            var shownCells = cells.Except(hidden).ToList();
            var shownRefs = new HashSet<string>(paint.Items.Select(i => i.NotListed ? "part-notlisted" : "part-" + i.Ref));
            c.Check(hidden.Count > 0 && hidden.All(e => e.name == ThumbGrid<PartItem>.HiddenCellName), "cells that show no entry are named cell-hidden (" + hidden.Count + " of " + cells.Count + ")");
            var named = cells.Where(e => e.name != null && e.name.StartsWith("part-", StringComparison.Ordinal)).ToList();
            c.Check(named.Count > 0 && named.All(e => shownRefs.Contains(e.name) && shownCells.Contains(e)), "only shown cells of searched rows carry part-<ref> names (" + named.Count + ": " + string.Join(", ", named.Select(e => e.name).Take(3)) + ")");
            var stale = c.Host.Root.Query<VisualElement>().Where(e => e.name != null && e.name.StartsWith("part-", StringComparison.Ordinal) && e.GetFirstAncestorOfType<PartList>() == paint && !shownRefs.Contains(e.name)).ToList();
            c.Check(stale.Count == 0, "no stale part-<ref> name in the searched list (" + stale.Count + ")");
            paint.Search("");
            yield return PlayModeHarness.WaitFrames(2);

            // ui.input on the focused search field: text, Backspace, Ctrl+A + Delete, Delete at the end, Backspace ×n
            var search = paint.Q<TextField>("partlist-search");
            search.Focus();
            yield return PlayModeHarness.WaitFrames(2);
            var focused = c.Host.Router.Focused;
            c.Check(focused != null && (focused == search || focused.GetFirstAncestorOfType<TextField>() == search), "the paint list search has the focus (" + focused?.name + ")");
            string typed = (sr?.Name ?? "").Split(new[] { ' ', '　', '_', '(' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (typed.Length < 3) typed = "abc";
            var esc = new JValue(typed).ToString(Newtonsoft.Json.Formatting.None);
            yield return MbSend(c, "{\"type\":\"text\",\"text\":" + esc + "}", 2);
            c.Check(search.value == typed, "typed text reaches the search (\"" + search.value + "\")");
            yield return MbSend(c, "{\"type\":\"key\",\"key\":\"Backspace\"}", 2);
            c.Check(search.value == typed.Substring(0, typed.Length - 1), "Backspace deletes one character (\"" + search.value + "\")");
            yield return MbSend(c, "{\"type\":\"key\",\"key\":\"Delete\"}", 2);
            c.Check(search.value == typed.Substring(0, typed.Length - 1), "Delete at the end deletes nothing (\"" + search.value + "\")");
            string partial = search.value;
            yield return WaitFor(() => paint.Items.Count < paint.AllItems.Count, 3, new bool[1]);   // the search's debounce (120 ms)
            int narrowed = paint.Items.Count;
            c.Check(narrowed < paint.AllItems.Count, "the typed search narrows the list (\"" + partial + "\": " + narrowed + " of " + paint.AllItems.Count + ")");
            yield return MbSend(c, "{\"type\":\"key\",\"key\":\"A\",\"mods\":[\"ctrl\"]}", 2);
            c.Check(search.cursorIndex != search.selectIndex && Math.Abs(search.cursorIndex - search.selectIndex) == search.value.Length, "Ctrl+A selects the whole text");
            yield return MbSend(c, "{\"type\":\"key\",\"key\":\"Delete\"}", 2);
            c.Check(search.value == "", "Delete removes the selection (\"" + search.value + "\")");
            var ok = new bool[1];
            yield return WaitFor(() => paint.SearchText == "" && paint.Items.Count == paint.AllItems.Count, 3, ok);
            bool marked = paint.Items.Any(i => paint.Grid.IsCurrent(i)) || paint.Items.Any(i => i.NotListed && i.Ref == paint.CurrentRef);
            c.Check(ok[0] && narrowed <= paint.AllItems.Count && marked,
                "after clearing every row is back (" + paint.Items.Count + " of " + paint.AllItems.Count + ", narrowed " + narrowed + ") with the current mark (" + paint.CurrentRef + ")");
            yield return MbSend(c, "{\"type\":\"text\",\"text\":\"xy\"}", 2);
            yield return MbSend(c, "{\"type\":\"key\",\"key\":\"Backspace\"}", 1);
            yield return MbSend(c, "{\"type\":\"key\",\"key\":\"Backspace\"}", 2);
            c.Check(search.value == "", "Backspace ×2 clears \"xy\" (\"" + search.value + "\")");
            yield return PlayModeHarness.WaitFrames(12);
            search.Blur();

            // UB5: the paint list scrolled with thumbnails loading
            HS2Next.UI.Panels.Maker.PartThumbnails.LogLoads = true;   // names each asynchronous thumbnail load in the log (a load that removes the GPU device)
            paint.SetExpanded(true);
            yield return PlayModeHarness.WaitFrames(3);
            yield return MfScroll(c, paint, "paint list (" + paint.Items.Count + " rows), pass 1");
            yield return MfScroll(c, paint, "paint list, pass 2 (warm)");
            var pt = paint.Thumbnails;
            var done = new bool[1];
            yield return WaitFor(() => pt == null || pt.Pending == 0, 60, done);   // the queued thumbnails finish while the list is shown
            c.Check(done[0], "the paint list's queued thumbnails finished (" + pt?.Pending + " pending)");
            paint.SetExpanded(false);
            HS2Next.UI.Panels.Maker.PartThumbnails.LogLoads = false;
            if (pt != null)
                c.Info($"UB5 thumbnails: from bundles {pt.FromBundles}, from the disk cache {pt.FromDiskCount}, failed {pt.FailedCount}, guard-skipped {pt.GuardSkipped}, mip<1 rejected {pt.BadMipRejected}, pending {pt.Pending}");
        }

        // ---------------------------------------------------------------- component sliders on two characters (the female and
        // the male: the paint rows are not gated; no third 4096² body is built — see the notes on the D3D12 device hang)
        {
            string f2 = m;
            int n = UndoCount(c);
            ui.ShowResult(ui.Ops.EditAppearance(new[] { f }, new List<(string, object)> { (BodyRoot + "paint[0].layout", new Vec4(0.1f, 0.2f, 0.3f, 0.4f)) }, UiContext.User()));
            ui.ShowResult(ui.Ops.EditAppearance(new[] { f2 }, new List<(string, object)> { (BodyRoot + "paint[0].layout", new Vec4(0.5f, 0.6f, 0.7f, 0.8f)) }, UiContext.User()));
            ui.Ops.Select(new[] { f, f2 });
            var insp = Inspector(c);
            insp.Refresh(true);
            insp.Chara?.ShowTab(CharaEditTab.TabId);
            var tab2 = insp.Chara?.TabBodies[CharaEditTab.TabId] as CharaEditTab;
            tab2?.Reveal("body", "paint");
            yield return PlayModeHarness.WaitFrames(4);
            var wrow = MfQ<MakerSliderRow>(tab2, "field-" + BodyRoot + "paint[0].layout#0");
            c.Check(wrow != null && wrow.Control.Mixed, "two characters (female, male) with different paint widths: the component row is mixed");
            if (wrow != null)
            {
                int nw = UndoCount(c);
                wrow.Write(wrow.Binding.Read(ui).Targets, 0.25, null);
                yield return PlayModeHarness.WaitFrames(2);
                var a1 = InspectorValues.V4(MfGet(c, f, BodyRoot + "paint[0].layout"));
                var a2 = InspectorValues.V4(MfGet(c, f2, BodyRoot + "paint[0].layout"));
                c.Check(Math.Abs(a1.X - 0.25f) < 1e-6 && Math.Abs(a2.X - 0.25f) < 1e-6 && Math.Abs(a1.Y - 0.2f) < 1e-6 && Math.Abs(a1.W - 0.4f) < 1e-6 && Math.Abs(a2.Y - 0.6f) < 1e-6 && Math.Abs(a2.W - 0.8f) < 1e-6 && UndoCount(c) == nw + 1,
                    "a paint width write keeps each character's other components (" + InspectorValues.Text(MfGet(c, f, BodyRoot + "paint[0].layout")) + " / " + InspectorValues.Text(MfGet(c, f2, BodyRoot + "paint[0].layout")) + "), one entry");
            }
            yield return MfUndoTo(c, n);
        }

        // ---------------------------------------------------------------- gated rows with a female primary and a male selected
        {
            int n = UndoCount(c);
            ui.Ops.Select(new[] { f, m });
            var insp = Inspector(c);
            insp.Refresh(true);
            insp.Chara?.ShowTab(CharaEditTab.TabId);
            var tab3 = insp.Chara?.TabBodies[CharaEditTab.TabId] as CharaEditTab;
            tab3?.Reveal("body", "shape");
            yield return PlayModeHarness.WaitFrames(4);
            c.Check(ui.Selection.Primary == f && tab3 != null, "female primary with the male selected");
            var soft = MfQ<MakerSliderRow>(tab3, "field-" + BodyRoot + "bust.softness");
            string mSoft = MfText(c, m, BodyRoot + "bust.softness"), mFut = MfText(c, m, "appearance.chara.profile.futanari");
            soft?.Write(soft.Binding.Read(ui).Targets, 0.31, null);
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(soft != null && Math.Abs(FieldValues.ToDouble(MfGet(c, f, BodyRoot + "bust.softness")) - 0.31) < 1e-6 && MfText(c, m, BodyRoot + "bust.softness") == mSoft,
                "«胸の柔らかさ» (female-only) writes the female, not the selected male");
            var head = MfQ<MakerSliderRow>(tab3, "field-" + BodyRoot + "shape[9]");
            head?.Write(head.Binding.Read(ui).Targets, 0.61, null);
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(head != null && Math.Abs(FieldValues.ToDouble(MfGet(c, m, BodyRoot + "shape[9]")) - 0.61) < 1e-6 && Math.Abs(FieldValues.ToDouble(MfGet(c, f, BodyRoot + "shape[9]")) - 0.61) < 1e-6,
                "«頭サイズ» (both sexes) writes both");
            tab3?.Reveal("body", "special");
            yield return PlayModeHarness.WaitFrames(2);
            var fut3 = MfQ<MakerToggleRow>(tab3, "field-appearance.chara.profile.futanari");
            fut3?.Write(true);
            yield return PlayModeHarness.WaitFrames(2);
            c.Check(fut3 != null && MfGet(c, f, "appearance.chara.profile.futanari") is bool fb && fb && MfText(c, m, "appearance.chara.profile.futanari") == mFut,
                "the futanari toggle writes the female only (male " + MfText(c, m, "appearance.chara.profile.futanari") + ")");
            yield return MfUndoTo(c, n);
        }

        // ---------------------------------------------------------------- male: gates, the height row with the pin off / on
        var tabM = MfOpenEdit(c, m);
        yield return PlayModeHarness.WaitFrames(4);
        if (tabM != null)
        {
            tabM.ShowNode("body");
            yield return PlayModeHarness.WaitFrames(3);
            c.Check(tabM.CurrentPage == "shape", "male: the Body node shows Shape (the last page, Special, is hidden for males)");
            var mb = MfQ<VisualElement>(tabM, "edit-pages-body")?.Query<Button>().ToList() ?? new List<Button>();
            var shownPages = mb.Where(b => b.style.display != DisplayStyle.None).Select(b => b.name.Substring("edit-page-body-".Length)).ToList();
            c.Check(shownPages.SequenceEqual(InspectorSchemas.BodyPages.Where(p => p != "special")), "male: seven Body page buttons, Special hidden (" + string.Join(", ", shownPages) + ")");
            var mRows = MbRows(tabM.SubPages.TryGetValue("body.shape", out var ms0) ? ms0 : null).OfType<MakerSliderRow>().Select(r => r.Path).ToList();
            var female = new[] { 5, 6, 7, 8, 32 }.Select(i => BodyRoot + "shape[" + i + "]").Concat(new[] { BodyRoot + "bust.softness", BodyRoot + "bust.weight" }).ToList();
            c.Check(mRows.Count == 29 && mRows.Contains(BodyRoot + "shape[0]") && !mRows.Any(female.Contains), "male Shape page (pin off): 29 rows with the height row, no female-only rows (" + mRows.Count + ")");
            tabM.Reveal("body", "skin");
            yield return PlayModeHarness.WaitFrames(3);
            var mskin = MfQ<PartList>(tabM, "field-" + BodyRoot + "skin.item");
            c.Check(mskin != null && mskin.Category == 131 && mskin.Items.Where(i => !i.NotListed).Select(i => i.Ref).SequenceEqual(lists.Rows(131, 0).Select(r => r.Key.ToString())), "male skin list = MakerLists.Rows(131) (" + mskin?.Items.Count + ")");
            foreach (var p in InspectorSchemas.BodyPages.Where(p => p != "special")) { tabM.Reveal("body", p); yield return PlayModeHarness.WaitFrames(1); }
            int mSliders = InspectorSchemas.BodyPages.Sum(p => MbRows(tabM.SubPages.TryGetValue("body." + p, out var pg) ? pg : null).OfType<MakerSliderRow>().Count());
            c.Check(mSliders == 48, "male Body pages: 48 sliders with the height row (" + mSliders + ")");
            // the pin on: the next Edit tab has no height row
            CharaAssembler.MaleHeightPin075 = true;
            var tabP = MfOpenEdit(c, m);
            yield return PlayModeHarness.WaitFrames(3);
            tabP?.Reveal("body", "shape");
            yield return PlayModeHarness.WaitFrames(3);
            var pRows = MbRows(tabP?.SubPages.TryGetValue("body.shape", out var ps) == true ? ps : null).OfType<MakerSliderRow>().Select(r => r.Path).ToList();
            c.Check(pRows.Count == 28 && !pRows.Contains(BodyRoot + "shape[0]"), "male Shape page with MaleHeightPin075 on: 28 rows, no height row (" + pRows.Count + ")");
            CharaAssembler.MaleHeightPin075 = false;
            var tabF = MfOpenEdit(c, f);
            yield return PlayModeHarness.WaitFrames(3);
            tabF?.Reveal("body", "shape");
            yield return PlayModeHarness.WaitFrames(2);
            var fRows = MbRows(tabF?.SubPages.TryGetValue("body.shape", out var fs) == true ? fs : null).OfType<MakerSliderRow>().ToList();
            c.Check(fRows.Count == 36, "the female keeps the height row whatever the pin (36 rows)");
        }
        else c.Check(false, "the male's Edit tab is shown");

        CharaAssembler.MaleHeightPin075 = pin0;
        CharaEditFlush.BodyThrottle = throttle0;
    }

    // ---------------------------------------------------------------- W-1 diagnostics (opt-in: -only MBW1 -w1diag)
    // The D3D12 hang of W-1: with the Edit tab on Body › Paint showing many cells whose thumbnail failed, a character is built
    // (cycles as MakerBodyTest.RunMultiApp's: a further BR-Chan added, the selection kept on the two females, a paint-layout
    // write on the new body and the first female, undo ×2). -w1paint <ref> sets body.paint[0].item of both females first (a
    // row without a thumbnail, so the paint list scrolls to failed cells), -w1tiles shows the 3.5 placeholder tiles again
    // (ThumbGridOptions.PlaceholderTextures, the state before the W-1 fix), -w1cycles N (25). Lines "W1DIAG ..." in the log
    // keep the numbers before a device loss. Numbers only; nothing is captured.

    static void W1Line(Ctx c, string s) { Debug.Log("W1DIAG " + s); c.Info("W1 " + s); }

    static string W1Arg(string[] a, string name, string def) { int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : def; }

    /// <summary>Cells of the page's part lists that show a row whose thumbnail failed, and the cells drawn with a texture.</summary>
    static (int failed, int textured, int cells) W1Cells(VisualElement page)
    {
        int failed = 0, textured = 0, cells = 0;
        foreach (var pl in page?.Query<PartList>().ToList() ?? new List<PartList>())
            foreach (var cell in pl.Grid.Query(className: "hs-card").ToList())
            {
                if (cell.name == null || !cell.name.StartsWith("part-", StringComparison.Ordinal)) continue;
                cells++;
                var it = pl.Items.FirstOrDefault(i => !i.NotListed && "part-" + i.Ref == cell.name);
                if (it?.Row != null && pl.Thumbnails != null && pl.Thumbnails.IsFailed(it.Row.Category, it.Row.Key, it.Column)) failed++;
                var img = cell.Q(className: "hs-card__image");
                if (img != null && img.style.backgroundImage.keyword != StyleKeyword.None && img.style.backgroundImage.value.texture != null) textured++;
            }
        return (failed, textured, cells);
    }

    static IEnumerator MakerBodyW1Group(Ctx c)
    {
        var args = Environment.GetCommandLineArgs();
        if (!args.Contains("-w1diag")) yield break;
        var ui = c.Ui;
        bool tiles = args.Contains("-w1tiles");
        string paintRef = W1Arg(args, "-w1paint", "Megadroide_bodypaint01#995");
        int cycles = int.Parse(W1Arg(args, "-w1cycles", "25"), System.Globalization.CultureInfo.InvariantCulture);
        bool keepTiles = ThumbGridOptions.PlaceholderTextures;
        ThumbGridOptions.PlaceholderTextures = tiles;
        W1Line(c, $"start: placeholder textures {tiles}, paint {paintRef}, cycles {cycles}, graphics {SystemInfo.graphicsDeviceType}");
        yield return NewScene(c);
        var ids = new List<string>();
        yield return AddBuiltChara(c, c.FemaleCard, ids);
        yield return AddBuiltChara(c, c.MaleCard, ids);
        yield return AddBuiltChara(c, c.FemaleCard, ids);
        if (ids.Count < 3) { ThumbGridOptions.PlaceholderTextures = keepTiles; yield break; }
        string f = ids[0], f2 = ids[2];
        var females = new List<string> { f, f2 };
        if (paintRef != "-")
        {
            var r = ui.ShowResult(ui.Ops.ChangeItem(females, BodyRoot + "paint[0].item", paintRef, UiContext.User()));
            yield return PlayModeHarness.WaitFrames(5);
            c.Check(r.Ok && MfText(c, f, BodyRoot + "paint[0].item") == InspectorValues.Text(paintRef), "both females' body paint 1 = " + paintRef + " (" + r.Code + ")");
        }
        ui.Ops.Select(females);
        var insp = Inspector(c);
        insp?.Refresh(true);
        insp?.Chara?.ShowTab(CharaEditTab.TabId);
        var tab = insp?.Chara?.TabBodies[CharaEditTab.TabId] as CharaEditTab;
        tab?.Reveal("body", "paint");
        yield return PlayModeHarness.WaitFrames(30);
        var page = tab?.SubPages.TryGetValue("body.paint", out var pp) == true ? pp : null;
        var cs = W1Cells(page);
        W1Line(c, $"Body › Paint shown: {cs.cells} part cells, {cs.failed} of them failed thumbnails, {cs.textured} drawn with a texture");
        var bus = c.App.Session.Commands;
        var rnd = new System.Random(42);
        for (int it = 0; it < 20; it++)
        {
            ui.Ops.EditAppearance(females, new List<(string, object)> { (BodyRoot + "paint[0].layout", new Vec4((float)rnd.NextDouble(), (float)rnd.NextDouble(), (float)rnd.NextDouble(), (float)rnd.NextDouble())) }, UiContext.User());
            yield return PlayModeHarness.WaitFrames(2);
            if (it % 5 == 4) for (int u = 0; u < 3 && bus.CanUndo; u++) { bus.Undo(); yield return PlayModeHarness.WaitFrames(1); }
        }
        W1Line(c, "20 writes done");
        var ok = new bool[1];
        int done = 0;
        for (int cy = 0; cy < cycles; cy++)
        {
            var r = ui.Ops.AddChara(c.FemaleCard, null, null, -1, null, UiContext.User());
            string id = r.NewIds.FirstOrDefault();
            ui.Ops.Select(females);
            yield return WaitFor(() => id != null && c.App.Runtime.FindChara(id)?.State == RealizationState.Built, 120, ok);
            if (!ok[0]) { c.Check(false, $"cycle {cy}: built"); break; }
            ui.Ops.EditAppearance(new[] { f, id }, new List<(string, object)> { (BodyRoot + "paint[0].layout", new Vec4(0.25f, (float)rnd.NextDouble(), 0.5f, 0.5f)) }, UiContext.User());
            yield return PlayModeHarness.WaitFrames(2);
            bus.Undo(); yield return PlayModeHarness.WaitFrames(1);
            bus.Undo(); yield return PlayModeHarness.WaitFrames(3);
            var tabNow = CharaEditTab.Current;
            var pageNow = tabNow?.SubPages.TryGetValue("body.paint", out var pn) == true ? pn : null;
            var cn = W1Cells(pageNow);
            W1Line(c, $"cycle {cy}: page {(pageNow != null ? "shown" : "none")}, {cn.cells} part cells, {cn.failed} failed, {cn.textured} textured");
            done++;
        }
        c.Check(done == cycles, $"W-1 diag: {done} of {cycles} build cycles on Body › Paint (placeholder textures {tiles}) without a device loss");
        ThumbGridOptions.PlaceholderTextures = keepTiles;
    }

    // ---------------------------------------------------------------- W-1 second hang diagnostics (opt-in: -only MBW2 -w2diag)
    // The thumbnail service under churn: a small LRU (-w2cap MB, default 2) evicts constantly, so thumbnail-only bundles are
    // unloaded while further loads from them are in flight; -w2nodiskread forces bundle loads (no PNG cache), -w2nodiskwrite
    // turns the disk-cache read-backs off. A face-skin list without the head filter (3,052 rows from a few vanilla bundles) and
    // the paint list are scrolled -w2passes times (default 6), 8 grid rows per frame. Lines "W2DIAG ..." in the log keep the
    // counters before a device loss. Numbers only.
    static IEnumerator MakerBodyW2Group(Ctx c)
    {
        var args = Environment.GetCommandLineArgs();
        if (!args.Contains("-w2diag")) yield break;
        var ui = c.Ui;
        long cap = (long)(double.Parse(W1Arg(args, "-w2cap", "2"), System.Globalization.CultureInfo.InvariantCulture) * 1024 * 1024);
        int passes = int.Parse(W1Arg(args, "-w2passes", "6"), System.Globalization.CultureInfo.InvariantCulture);
        var pt = HS2Next.UI.Panels.Maker.PartThumbnails.Shared(c.App.Catalog);
        long cap0 = pt.CapBytes;
        bool rd0 = pt.ReadDisk, wd0 = pt.WriteDisk;
        pt.CapBytes = cap;
        if (args.Contains("-w2nodiskread")) pt.ReadDisk = false;
        if (args.Contains("-w2nodiskwrite")) pt.WriteDisk = false;
        void Line(string s) { Debug.Log("W2DIAG " + s); c.Info("W2 " + s); }
        string Counters() => $"from bundles {pt.FromBundles}, disk {pt.FromDiskCount}, loose {pt.FromLoose}, failed {pt.FailedCount}, evicted {pt.Evicted}, bundles unloaded {pt.BundlesUnloaded}, disk written {pt.DiskWritten}, pending {pt.Pending}, bytes {pt.CurrentBytes / 1048576.0:0.0} MB";
        Line($"start: cap {cap / 1048576.0:0.0} MB, read disk {pt.ReadDisk}, write disk {pt.WriteDisk}, passes {passes}; {Counters()}");
        yield return NewScene(c);
        var ids = new List<string>();
        yield return AddBuiltChara(c, c.FemaleCard, ids);
        if (ids.Count == 1)
        {
            var tab = MfOpenEdit(c, ids[0]);
            tab?.Reveal("body", "paint");
            yield return PlayModeHarness.WaitFrames(5);
            var host = tab?.SubPages.TryGetValue("body.paint", out var hp) == true ? hp : null;
            var skinSpec = InspectorSchemas.CharaEdit.SelectMany(s => s.Fields).FirstOrDefault(x => x.Kind == FieldKind.PartList && x.ListContext == FieldSpec.ListContextHead);
            var lists = new List<PartList>();
            if (host != null && skinSpec != null)
            {
                var spec = skinSpec.Clone();
                spec.ListContext = null;
                var big = new PartList(ui, spec, TargetSelector.Primary(NodeKind.Chara), FaceRoot + "skin") { name = "w2-skin-all" };
                big.SetExpanded(true);
                host.Insert(0, big);
                lists.Add(big);
            }
            var paint = MfQ<PartList>(tab, "field-" + BodyRoot + "paint[0].item");
            if (paint != null) { paint.SetExpanded(true); lists.Add(paint); }
            yield return PlayModeHarness.WaitFrames(5);
            for (int pass = 0; pass < passes; pass++)
            {
                foreach (var pl in lists)
                {
                    var grid = pl.Grid;
                    int rows = Math.Max(1, (pl.Items.Count + Math.Max(1, grid.Columns) - 1) / Math.Max(1, grid.Columns));
                    for (int r = 0; r < rows; r += MakerThumbList.ExpandedRows)
                    {
                        grid.List.ScrollToItem(Math.Min(r, rows - 1));
                        yield return null;
                    }
                    Line($"pass {pass} {pl.name} ({pl.Items.Count} rows): {Counters()}");
                }
            }
            var ok = new bool[1];
            yield return WaitFor(() => pt.Pending == 0, 30, ok);
            Line($"end: {Counters()}");
            c.Check(true, $"W-1 second-hang diag: {passes} passes of {lists.Count} lists with a {cap / 1048576.0:0.0} MB thumbnail LRU without a device loss");
            foreach (var pl in lists) if (pl.name == "w2-skin-all") pl.RemoveFromHierarchy();
        }
        pt.CapBytes = cap0;
        pt.ReadDisk = rd0;
        pt.WriteDisk = wd0;
    }
}
