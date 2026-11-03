// UiShellTest.play group U (Phase 4.0 package U): the UI defects found while recording the v0.2 demo. Q app.quit with a
// changed scene (no unsaved-changes prompt; the window close still asks), D the dropdown menu in the dark theme, K the key
// list labels fill their rows (descenders were clipped), F list-browser facets (only groups with matches during a search;
// counts add up), M the item list's name column keeps its minimum, I a narrow inspector (no horizontal overflow, one-line
// segmented buttons, tabs inside the strip), S BR-Chan has no warning for parts without a mask renderer, R a language switch
// keeps the selected node's name, P the colour picker: hex + Enter applies and commits, Esc reverts and closes. Events are
// synthesised through the panel (UiCommands.Send, as ui.input does).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2Next.App.Commands;
using HS2Next.Content.Studio;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class UiShellTest
{
    static bool Near(Color a, Color b, float tol = 0.02f) => Mathf.Abs(a.r - b.r) <= tol && Mathf.Abs(a.g - b.g) <= tol && Mathf.Abs(a.b - b.b) <= tol;
    static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1);

    static IEnumerator Click(Ctx c, VisualElement el)
    {
        var p = el.worldBound.center;
        Pointer(c, "move", p);
        yield return PlayModeHarness.WaitFrames(1);
        Pointer(c, "down", p);
        yield return PlayModeHarness.WaitFrames(1);
        Pointer(c, "up", p);
        yield return PlayModeHarness.WaitFrames(1);
    }

    static IEnumerator V02Defects(Ctx c)
    {
        var ui = c.Ui;
        var root = c.Host.Root;
        ui.Ops.NewScene();
        yield return PlayModeHarness.WaitFrames(1);

        // ---- Q: app.quit with a changed scene
        var files = SceneFiles.Current;
        var lightKey = ui.App.Db.Lights.Enumerate().First().Key;
        var lr = ui.Ops.AddLight(lightKey, null, null, -1, UiContext.User());
        string light = lr.NewIds.FirstOrDefault();
        c.Check(files != null && light != null && files.IsDirty, "Q a changed scene (light added)");
        if (files != null)
        {
            AppCommands.QuitRequested = true;
            bool allowed;
            try { allowed = files.WantsToQuit(); }
            finally { AppCommands.QuitRequested = false; }
            c.Check(allowed && !ui.Dialogs.IsModalOpen, "Q app.quit: wantsToQuit lets the quit through without the unsaved-changes dialog");
            bool held = !files.WantsToQuit();
            yield return PlayModeHarness.WaitFrames(1);
            c.Check(held && ui.Dialogs.IsModalOpen, "Q window close (no app.quit): a changed scene still asks first");
            ui.Dialogs.CloseCurrent();
            yield return PlayModeHarness.WaitFrames(1);
            c.Check(!ui.Dialogs.IsModalOpen && files.LastOutcome == "cancelled", "Q the prompt cancelled (" + files.LastOutcome + ")");
        }

        // ---- D: the dropdown menu (GenericDropdownMenu lives outside .hs-root)
        var dd = new DropdownField(new List<string> { "one", "two", "three" }, 0) { name = "u-dropdown" };
        dd.style.position = Position.Absolute; dd.style.left = 400; dd.style.top = 200; dd.style.width = 160;
        root.Add(dd);
        yield return PlayModeHarness.WaitFrames(2);
        var input = dd.Q(className: "unity-base-popup-field__input") ?? dd;
        yield return Click(c, input);
        yield return PlayModeHarness.WaitFrames(2);
        var outer = root.panel.visualTree.Q(className: "unity-base-dropdown__container-outer");
        var itemLabel = root.panel.visualTree.Q(className: "unity-base-dropdown__label");
        c.Check(outer != null, "D the dropdown menu opened");
        if (outer != null)
        {
            var bg = outer.resolvedStyle.backgroundColor;
            c.Check(Near(bg, Rgb(41, 43, 50)), "D menu background = --hs-bg-2 (" + bg + ")");
            var tc = itemLabel?.resolvedStyle.color ?? Color.clear;
            c.Check(itemLabel != null && Near(tc, Rgb(222, 224, 229)), "D item text = --hs-text (" + tc + ")");
            root.panel.visualTree.Q(className: "unity-base-dropdown")?.RemoveFromHierarchy();
        }
        dd.RemoveFromHierarchy();

        // ---- K: the key list labels fill their rows
        var ko = KeyOverlay.Show(ui);
        yield return PlayModeHarness.WaitFrames(3);
        var rows = root.Query(className: "hs-keys__row").ToList().Where(r => r.worldBound.height > 0).Take(8).ToList();
        bool fill = rows.Count > 0;
        foreach (var r in rows)
        {
            var lab = r.Q(className: "hs-keys__label");
            if (lab == null || lab.layout.height < r.layout.height - 0.5f) { fill = false; c.Info("K row " + r.name + ": label " + lab?.layout.height + " of " + r.layout.height); }
        }
        c.Check(ko != null && fill, "K key list: the labels are as tall as their rows (" + rows.Count + " rows)");
        ui.Dialogs.CloseCurrent();
        yield return PlayModeHarness.WaitFrames(1);

        // ---- F / M: the item browser
        ui.Shell.FocusPanel("add.item");
        yield return PlayModeHarness.WaitFrames(3);
        var br = root.Q<ListBrowser<StudioItem>>("items-browser");
        c.Check(br != null, "F item browser built");
        if (br != null)
        {
            int allGroups = br.Index.Rows.Select(r => r.Group).Distinct().Count();
            br.Search("ハート");
            yield return PlayModeHarness.WaitFrames(2);
            // v1.2: the views «★» and «最近追加» stand above «All»
            var shown = ((IList<string>)br.Groups.itemsSource).Where(k => !BrowserViews.Is(k)).ToList();
            var counts = br.Index.FacetCounts("group", "category");
            var shownKeys = shown.Where(k => k != null).ToList();
            c.Check(br.Index.Indices.Count > 0 && shown.Count > 1 && shown[0] == null, "F search: matches " + br.Index.Indices.Count + ", group rows " + shown.Count + " (All first)");
            c.Check(shownKeys.All(k => counts.TryGetValue(k, out int n) && n > 0), "F search: every shown group has matches (no \"(0)\" rows)");
            c.Check(shownKeys.Sum(k => counts[k]) == br.Index.Indices.Count, "F search: the shown group counts add up to the matches (" + shownKeys.Sum(k => counts.TryGetValue(k, out int n) ? n : 0) + ")");
            c.Check(shownKeys.Count < allGroups, "F search: groups without matches hidden (" + shownKeys.Count + " of " + allGroups + ")");
            string g0 = shownKeys.FirstOrDefault();
            if (g0 != null)
            {
                br.SelectGroup(g0);
                yield return PlayModeHarness.WaitFrames(1);
                c.Check(br.Groups.selectedIndex == ((IList<string>)br.Groups.itemsSource).IndexOf(g0), "F the selected group row follows the facet (" + br.Groups.selectedIndex + ")");
                var cats = ((IList<string>)br.Categories.itemsSource).Where(k => k != null).ToList();
                var cc = br.Index.FacetCounts("category");
                c.Check(cats.Count > 0 && cats.All(k => cc.TryGetValue(g0 + "\u0001" + k, out int n) && n > 0), "F categories of the group: only those with matches (" + cats.Count + ")");
                br.SelectGroup(null);
            }
            br.Search("");
            yield return PlayModeHarness.WaitFrames(2);
            int facetRows = ((IList<string>)br.Groups.itemsSource).Count(k => !BrowserViews.Is(k));
            c.Check(facetRows == allGroups + 1, "F empty search: every group again (" + facetRows + ")");
            // M: the name column keeps its minimum (the right sidebar since the v0.3 trial)
            var headers = br.List.Query(className: "unity-multi-column-header__column").ToList().Where(h => h.worldBound.width > 0).ToList();
            float nameW = headers.Count > 0 ? headers[0].worldBound.width : 0;
            c.Check(nameW >= 139, "M name column " + nameW.ToString("0") + " px wide (min 140; list " + br.List.worldBound.width.ToString("0") + " px)");
        }

        // ---- S / I / R: BR-Chan in a narrow inspector
        var targets = TestTargets.Load();
        var ar = ui.Ops.AddChara(TestTargets.Abs(targets.visual.female), null, null, -1, null, UiContext.User());
        string chara = ar.NewIds.FirstOrDefault();
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => chara != null && ui.Runtime.FindChara(chara)?.State == RealizationState.Built, 120, ok);
        c.Check(ok[0], "S BR-Chan built");
        if (ok[0])
        {
            yield return PlayModeHarness.WaitFrames(3);
            var masks = ui.Findings.Where(f => f.Code == "chara.maskRendererMissing" && f.NodeId == chara).ToList();
            c.Check(masks.All(f => f.Severity < HS2Next.Save.FindingSeverity.Warning), "S no warning for parts without a mask renderer (" + masks.Count + " info: " + string.Join("; ", masks.Select(f => f.Message)) + ")");
            // the inspector's region (left since the v0.3 trial) at its minimum width, the inspector tab shown
            var right = ui.Shell.Regions[ui.Shell.RegionOf("inspector")];
            float before = (float)ui.Shell.Layout.Region(right.Name).Size;
            ui.Shell.SetSize(right, 300);
            ui.Ops.Select(new[] { chara });
            ui.Shell.FocusPanel("inspector");
            yield return PlayModeHarness.WaitFrames(3);
            var insp = root.Q<CharaInspector>();
            insp?.ShowTab("look");
            yield return PlayModeHarness.WaitFrames(3);
            var scroll = root.Q<ScrollView>("inspector-scroll");
            c.Check(insp != null && scroll != null, "I character inspector shown (inspector " + scroll?.worldBound.width.ToString("0") + " px)");
            if (insp != null && scroll != null)
            {
                float xMax = scroll.contentViewport.worldBound.xMax + 0.5f;
                c.Check(scroll.horizontalScroller.resolvedStyle.display == DisplayStyle.None || !scroll.horizontalScroller.visible || scroll.horizontalScrollerVisibility == ScrollerVisibility.Hidden, "I no horizontal scrollbar");
                var over = scroll.contentContainer.Query<VisualElement>().ToList().Where(e => e.resolvedStyle.display != DisplayStyle.None && e.worldBound.width > 0 && e.worldBound.xMax > xMax && !(e is ScrollView) && e.GetFirstAncestorOfType<ScrollView>() == scroll).Select(e => (e.name ?? e.GetType().Name) + " " + e.worldBound.xMax.ToString("0")).Take(5).ToList();
                c.Check(over.Count == 0, "I nothing overflows the inspector's right edge " + xMax.ToString("0") + (over.Count > 0 ? ": " + string.Join(", ", over) : ""));
                var segs = insp.Query(className: "hs-segmented__button").ToList().Where(b => b.worldBound.width > 0).ToList();
                // one line each (no wrap), one height, inside their row (v0.2: the text wrapped and the buttons stuck out of the row)
                c.Check(segs.Count > 0 && segs.All(b => b.resolvedStyle.whiteSpace == WhiteSpace.NoWrap && b.resolvedStyle.textOverflow == TextOverflow.Ellipsis)
                        && segs.Select(b => Mathf.Round(b.worldBound.height)).Distinct().Count() == 1
                        && segs.All(b => { var fr = b.GetFirstAncestorOfType<HS2Next.UI.Binding.FieldRow>(); return fr != null && b.worldBound.yMin >= fr.worldBound.yMin - 0.5f && b.worldBound.yMax <= fr.worldBound.yMax + 0.5f && b.worldBound.xMax <= fr.worldBound.xMax + 0.5f; }),
                    "I segmented buttons: one line, one height, inside their row (" + segs.Count + "; heights " + string.Join(",", segs.Select(b => b.worldBound.height.ToString("0.#")).Distinct()) + ")");
                var strip = insp.Q("tabs");
                var tabs = strip?.Query(className: "hs-inspector__tab").ToList() ?? new List<VisualElement>();
                c.Check(tabs.Count > 0 && tabs.All(t => t.worldBound.xMax <= strip.worldBound.xMax + 0.5f), "I tabs inside the strip (" + tabs.Count + " tabs, strip " + strip?.worldBound.height.ToString("0") + " px tall)");
            }
            // R: a language switch keeps the node's name
            ui.Ops.Rename(chara, "U-Test", UiContext.User());
            yield return PlayModeHarness.WaitFrames(2);
            int entries = ui.Session.Commands.UndoList.Count;
            c.Host.SetLanguage("ja", false);
            yield return PlayModeHarness.WaitFrames(3);
            c.Host.SetLanguage("en", false);
            yield return PlayModeHarness.WaitFrames(3);
            string nm = ui.Model.Find(chara)?.Doc.Name;
            c.Check(nm == "U-Test" && ui.Session.Commands.UndoList.Count == entries, "R language switches keep the selected node's name (\"" + nm + "\", history " + entries + " → " + ui.Session.Commands.UndoList.Count + ")");
            ui.Shell.SetSize(right, before);
        }

        // ---- P: the colour picker's hex field
        if (light != null)
        {
            ui.Ops.Select(new[] { light });
            ui.Shell.FocusPanel("inspector");
            yield return PlayModeHarness.WaitFrames(3);
            var row = root.Q("field-light.color");
            var swatch = row?.Q<Button>("swatch");
            c.Check(swatch != null, "P light colour row");
            if (swatch != null)
            {
                int n0 = ui.Session.Commands.UndoList.Count;
                yield return Click(c, swatch);
                yield return PlayModeHarness.WaitFrames(3);
                var picker = root.Q<ColorPicker>();
                var hex = picker?.Q<TextField>("hex");
                c.Check(hex != null, "P picker open");
                if (hex != null)
                {
                    hex.Focus();
                    yield return PlayModeHarness.WaitFrames(1);
                    Ev(c, new { type = "key", key = "A", mods = new[] { "ctrl" } });
                    Ev(c, new { type = "text", text = "FF0000" });
                    yield return PlayModeHarness.WaitFrames(1);
                    Ev(c, new { type = "key", key = "Return" });
                    yield return PlayModeHarness.WaitFrames(2);
                    FieldAccessor.TryGet(ui.Model, light, "light.color", out var v, out _);
                    var col = v is HS2Next.Save.Color4 c4 ? new Color(c4.R, c4.G, c4.B) : Color.clear;
                    c.Check(root.Q<ColorPicker>() == null, "P hex + Enter closes the picker");
                    c.Check(Near(col, Color.red, 0.005f), "P hex + Enter applied FF0000 (" + col + ")");
                    c.Check(ui.Session.Commands.UndoList.Count == n0 + 1, "P one history entry (" + n0 + " → " + ui.Session.Commands.UndoList.Count + ")");
                    // Esc in the hex field: revert and close
                    yield return Click(c, swatch);
                    yield return PlayModeHarness.WaitFrames(3);
                    hex = root.Q<ColorPicker>()?.Q<TextField>("hex");
                    if (hex != null)
                    {
                        hex.Focus();
                        yield return PlayModeHarness.WaitFrames(1);
                        Ev(c, new { type = "key", key = "A", mods = new[] { "ctrl" } });
                        Ev(c, new { type = "text", text = "00FF00" });
                        Ev(c, new { type = "key", key = "Escape" });
                        yield return PlayModeHarness.WaitFrames(2);
                        FieldAccessor.TryGet(ui.Model, light, "light.color", out v, out _);
                        col = v is HS2Next.Save.Color4 c5 ? new Color(c5.R, c5.G, c5.B) : Color.clear;
                        c.Check(root.Q<ColorPicker>() == null && Near(col, Color.red, 0.005f) && ui.Session.Commands.UndoList.Count == n0 + 1,
                            "P Esc in the hex field reverts and closes (" + col + ", picker " + (root.Q<ColorPicker>() == null ? "closed" : "open") + ")");
                    }
                    else c.Check(false, "P picker reopened");
                }
            }
        }
        ui.Ops.NewScene();
        yield return PlayModeHarness.WaitFrames(1);
    }
}
