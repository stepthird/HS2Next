// UiShellTest.play group T (v0.3 trial fixes; helpers in UiShellTest_V03.cs): T1 the dialog layer covers the
// whole root and no visible part of the options, key-list, scene-browser and message dialogs lies outside its box; T2 the
// default sidebar layout (left workspace | inspector, right the eight browsers / scene / history, bottom empty and
// collapsed, sizes 360 / 380) and every tab inside its wrapped strip (en and ja); T3 a version 1 layout.json replaced
// once (hidden panels kept, one toast, version 2 written); T4 the inspector tab switching: a viewport click pick shows it,
// a tree click does not, a tree double click and Enter do, edit.show and ui.panel focus do, and with the inspector in
// another region a viewport pick switches nothing; T5 the facet lists of the item and animation browsers at least four
// rows tall and stacked in the sidebar, side by side in the bottom region; the character and pose browsers stacked with a
// grid of at least two columns in the sidebar, nothing outside the panel; T6 the language labels read "Language" in en,
// ja and zh-Hans; T7 the window flag (resizable, windowed, 1600 x 900) and a panel scale change relaying the regions out;
// T8 60 idle frames without a geometry change of the regions, the viewport, the toasts, the browser bodies or the splits.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using HS2Next.UI.Viewport;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class UiShellTest
{
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return PlayModeHarness.WaitFrames(1); }

    static IEnumerator DialogContained(Ctx c, string what, bool sized = false)
    {
        var ui = c.Ui;
        var root = c.Host.Root;
        yield return Frames(4);
        var layer = ui.Shell.DialogLayer;
        var box = layer.Q(className: Dialogs.DialogClass);
        c.Check(box != null && ui.Dialogs.IsModalOpen, what + ": open");
        if (box == null) yield break;
        var lb = layer.worldBound; var rb = root.worldBound;
        bool covers = Mathf.Abs(lb.x - rb.x) < 0.5f && Mathf.Abs(lb.y - rb.y) < 0.5f && Mathf.Abs(lb.width - rb.width) < 0.5f && Mathf.Abs(lb.height - rb.height) < 0.5f;
        bool last = root.hierarchy[root.hierarchy.childCount - 1] == layer;
        c.Check(covers && last && Inside(root.Q("toolbar").worldBound, lb) && Inside(root.Q("statusbar").worldBound, lb),
            $"{what}: the dimmed layer {R(lb)} covers the whole root {R(rb)} with the toolbar and the status bar, on top ({last})");
        var o = Outside(box);
        c.Check(Inside(box.worldBound, rb) && o.count == 0,
            $"{what}: box {R(box.worldBound)} inside the window; visible descendants outside it: {o.count} (left {o.left:0.#}, top {o.top:0.#}, right {o.right:0.#}, bottom {o.bottom:0.#}){(o.examples.Count > 0 ? ": " + string.Join("; ", o.examples) : "")}");
        if (sized)
        {
            float wMax = Mathf.Min(760, 0.92f * rb.width) + 0.5f, hMax = 0.85f * rb.height + 0.5f;
            var scroll = box.Q<ScrollView>("options-scroll");
            var panel = box.Q("options-panel");
            c.Check(box.worldBound.width <= wMax && box.worldBound.height <= hMax && box.worldBound.width >= Mathf.Min(760, 0.92f * rb.width) - 1,
                $"{what}: box {box.worldBound.width:0.#} x {box.worldBound.height:0.#} (width min(760, 92 %) = {wMax - 0.5f:0.#}, height ≤ 85 % = {hMax - 0.5f:0.#})");
            c.Check(scroll != null && panel != null && Mathf.Abs(scroll.worldBound.yMax - panel.worldBound.yMax) < 1 && Mathf.Abs(scroll.worldBound.xMax - panel.worldBound.xMax) < 1
                    && scroll.worldBound.height > 200 && Inside(panel.worldBound, box.worldBound),
                $"{what}: the ScrollView {R(scroll?.worldBound ?? Rect.zero)} fills the options {R(panel?.worldBound ?? Rect.zero)} inside the box");
        }
    }

    static VisualElement TreeRowOf(WorkspacePanel ws, string label) =>
        ws.Tree.Query(className: "hs-tree-row").ToList().FirstOrDefault(r => r.worldBound.height > 0 && r.Q<Label>(className: "hs-tree-row__label")?.text == label);

    static IEnumerator ClickAt(Ctx c, Vector2 p, int clicks = 1)
    {
        Ev(c, new { type = "pointer", phase = "move", x = p.x, y = p.y, button = 0 });
        Ev(c, new { type = "pointer", phase = "down", x = p.x, y = p.y, button = 0, clicks });
        Ev(c, new { type = "pointer", phase = "up", x = p.x, y = p.y, button = 0, clicks });
        yield return Frames(2);
    }

    static string Active(Ctx c, string region) => c.Ui.Shell.Regions[region].Active;

    /// <summary>A region's panels without the H group's run-time test panel.</summary>
    static List<string> Own(UiRegion r) => r.Panels.Where(p => p != "test.panel").ToList();

    static (int count, float left, float top, float right, float bottom, List<string> examples) OutsideOf(VisualElement e) =>
        e != null ? Outside(e) : (0, 0f, 0f, 0f, 0f, new List<string>());

    static IEnumerator V03Checks(Ctx c)
    {
        var ui = c.Ui;
        var sh = ui.Shell;
        var root = c.Host.Root;
        sh.ResetLayout();
        yield return Frames(4);

        // ---- T1: dialogs inside their boxes
        OptionsPanel.Show(ui);
        yield return DialogContained(c, "T1 options", sized: true);
        var focused = c.Host.Router.Focused;
        Ev(c, new { type = "key", key = "Escape" });
        yield return Frames(2);
        c.Check(!ui.Dialogs.IsModalOpen, "T1 Esc closes the options (focus was " + (focused == null ? "none" : (focused.name ?? "") + " " + focused.GetType().Name) + "; keys " + string.Join(" | ", c.Host.Router.Log.Skip(Math.Max(0, c.Host.Router.Log.Count - 3))) + ")");
        while (ui.Dialogs.IsModalOpen) ui.Dialogs.CloseCurrent();
        yield return Frames(2);
        KeyOverlay.Show(ui);
        yield return DialogContained(c, "T1 key list");
        ui.Dialogs.CloseCurrent();
        yield return Frames(2);
        ui.Dialogs.Confirm("dialog.delete.title", "msg.layoutUpdated", _ => { }, "x");
        yield return DialogContained(c, "T1 a long message");
        var mb = ui.Shell.DialogLayer.Q(className: Dialogs.DialogClass);
        c.Check(mb != null && mb.worldBound.width <= 640.5f, "T1 a message dialog keeps the 640 px width (" + mb?.worldBound.width.ToString("0.#") + ")");
        ui.Dialogs.CloseCurrent();
        yield return Frames(2);
        ui.Registry.Run(ActionIds.FileOpen, "test");
        yield return DialogContained(c, "T1 scene browser");
        while (ui.Dialogs.IsModalOpen) ui.Dialogs.CloseCurrent();
        yield return Frames(2);

        // ---- T2: the default layout and the wrapped tab strips
        var left = sh.Regions[LayoutRegions.Left]; var right = sh.Regions[LayoutRegions.Right]; var bottom = sh.Regions[LayoutRegions.Bottom];
        c.Check(left.Panels.SequenceEqual(new[] { "workspace", "inspector" }) && left.Active == "workspace" && !left.Collapsed,
            "T2 left = workspace | inspector, workspace shown (" + string.Join(", ", left.Panels) + "; " + left.Active + ")");
        c.Check(Own(right).SequenceEqual(new[] { "add.chara", "add.item", "add.anime", "add.map", "add.light", "add.pose", "scene", "history", "cameras" }) && right.Active == "add.chara" && !right.Collapsed,
            "T2 right = the browsers, scene, history and (v0.3 trial 2) the cameras, add.chara shown (" + string.Join(", ", right.Panels) + "; " + right.Active + ")");
        c.Check(bottom.Panels.Count == 0 && bottom.Collapsed, "T2 bottom empty and collapsed");
        float lw = sh.SizeOf(left), rw = sh.SizeOf(right);
        var vb = ui.Viewport.Element.worldBound;
        c.Check(Mathf.Abs(lw - 360) < 1.5f && Mathf.Abs(rw - 380) < 1.5f && vb.width >= 320 && vb.height >= 240,
            $"T2 sizes: left {lw:0.#} (360), right {rw:0.#} (380), viewport {vb.width:0.#} x {vb.height:0.#} (≥ 320 x 240)");
        c.Check(vb.yMax > root.Q("statusbar").worldBound.yMin - 3, "T2 the viewport reaches down to the status bar (bottom collapsed): " + R(vb));
        var moveMenu = new List<string>();
        foreach (var lang in new[] { "en", "ja" })
        {
            c.Host.SetLanguage(lang, save: false);
            yield return Frames(3);
            foreach (var reg in new[] { left, right })
            {
                var tabs = reg.Strip.Children().ToList();
                var sb = reg.Strip.worldBound;
                var outside = tabs.Where(t => t.worldBound.width < 1 || !Inside(t.worldBound, sb)).Select(t => t.name).ToList();
                float sum = tabs.Sum(t => t.worldBound.width + t.resolvedStyle.marginLeft + t.resolvedStyle.marginRight);
                int lines = tabs.Select(t => Mathf.Round(t.worldBound.y)).Distinct().Count();
                bool wraps = sum <= sb.width + 0.5f || lines > 1;
                c.Check(outside.Count == 0 && wraps && tabs.All(t => t.resolvedStyle.whiteSpace == WhiteSpace.NoWrap),
                    $"T2 {lang} {reg.Name}: {tabs.Count} tabs on {lines} line(s) of a {sb.width:0} x {sb.height:0} strip (tabs {sum:0} px), every tab inside{(outside.Count > 0 ? " - outside: " + string.Join(", ", outside) : "")}");
            }
        }
        c.Host.SetLanguage("en", save: false);
        yield return Frames(2);
        sh.ShowPanelMenu("add.item", new Vector2(600, 300));
        yield return Frames(1);
        c.Check(sh.Menus.IsOpen && root.panel.visualTree.Q("menu-item-move-bottom") != null, "T2 the tab menu offers «Move to bottom»");
        sh.Menus.CloseAll();

        // ---- T3: a version 1 layout.json is replaced once
        string v1 = "{\"version\":1,\"regions\":{\"left\":{\"size\":280,\"collapsed\":false,\"panels\":[\"workspace\"],\"active\":\"workspace\"},"
                    + "\"right\":{\"size\":360,\"collapsed\":false,\"panels\":[\"inspector\"],\"active\":\"inspector\"},"
                    + "\"bottom\":{\"size\":300,\"collapsed\":false,\"panels\":[\"add.chara\",\"add.item\",\"add.anime\",\"add.map\",\"add.light\",\"add.pose\",\"scene\",\"history\"],\"active\":\"add.item\"}},"
                    + "\"hidden\":[\"history\"]}";
        Directory.CreateDirectory(Path.GetDirectoryName(sh.LayoutFile));
        File.WriteAllText(sh.LayoutFile, v1, new UTF8Encoding(false));
        int toasts0 = ui.Toasts.History.Count;
        sh.ReloadLayout();
        yield return Frames(3);
        string expectToast = "Info: " + ui.L10n.Format("msg.layoutUpdated", ui.L10n.Get("action.ui.resetLayout"));
        c.Check(sh.MigratedFromVersion == 1 && left.Panels.SequenceEqual(new[] { "workspace", "inspector" }) && Own(right).SequenceEqual(new[] { "add.chara", "add.item", "add.anime", "add.map", "add.light", "add.pose", "scene", "cameras" })
                && bottom.Panels.Count == 0 && sh.RegionOf("history") == null && sh.Layout.Hidden.Contains("history"),
            "T3 version 1 file → the version 2 default, history still hidden (" + string.Join(", ", right.Panels) + ")");
        c.Check(ui.Toasts.History.Count == toasts0 + 1 && ui.Toasts.History.Last() == expectToast, "T3 one toast: " + ui.Toasts.History.LastOrDefault());
        sh.SaveLayout();
        string written = File.ReadAllText(sh.LayoutFile);
        var back = LayoutDocument.Parse(written);
        c.Check(back != null && back.Version == 2 && written.Contains("\"version\": 2") && back.Hidden.Contains("history"), "T3 layout.json written as version 2 with history hidden");
        sh.ReloadLayout();
        yield return Frames(2);
        c.Check(sh.MigratedFromVersion == -1 && ui.Toasts.History.Count == toasts0 + 1, "T3 the next load migrates nothing and shows no toast (once)");
        sh.SetPanelVisible("history", true);
        sh.ResetLayout();
        yield return Frames(3);

        // ---- T4: inspector tab switching
        ui.Ops.NewScene();
        yield return Frames(2);
        var ir = ui.Ops.AddItem(ContentId.Vanilla(821), null, null, -1, UiContext.User());
        string item = ir.NewIds.FirstOrDefault();
        c.Check(ir.Ok && item != null, "T4 an item added (" + ir + ")");
        var ws = sh.PanelRoot("workspace")?.Q<WorkspacePanel>() ?? WorkspacePanel.Current;
        if (item != null && ws != null)
        {
            for (int i = 0; i < 60 && ui.Runtime.FindItem(item)?.State != RealizationState.Built; i++) yield return PlayModeHarness.WaitFrames(1);
            ui.Viewport.Camera.Frame(new[] { item });
            ui.Viewport.Camera.Flush();
            ui.Ops.Select(Array.Empty<string>());
            sh.ShowPanel("workspace");
            yield return Frames(4);
            var g = ui.Viewport.Gizmo;
            Vector2 ip = g.ToPanel(NodeBounds.Of(ui, new[] { item }, out bool any).center);
            int auto0 = sh.AutoShows;
            yield return ClickAt(c, ip);
            c.Check(ui.Selection.Primary == item && Active(c, LayoutRegions.Left) == "inspector" && sh.AutoShows == auto0 + 1,
                $"T4 a viewport click on the item selects it and shows the inspector tab (selection {ui.Selection.Primary}, left {Active(c, LayoutRegions.Left)}, pick {ui.Viewport.Picker.Last.NodeId}, bounds {any})");
            // a click on empty space clears the selection and switches nothing
            sh.ShowPanel("workspace");
            yield return Frames(2);
            var vbb = ui.Viewport.Element.worldBound;
            yield return ClickAt(c, new Vector2(vbb.xMin + 4, vbb.yMin + 30));
            c.Check(ui.Selection.Count == 0 && Active(c, LayoutRegions.Left) == "workspace", "T4 a viewport click on empty space: selection cleared, the workspace stays (" + Active(c, LayoutRegions.Left) + ")");
            // the tree: a click only selects; a double click and Enter show the inspector
            ws.RebuildNow();
            yield return Frames(3);
            var row = TreeRowOf(ws, ui.Model.Find(item).Label);
            c.Check(row != null, "T4 the item's tree row");
            if (row != null)
            {
                yield return ClickAt(c, row.worldBound.center);
                c.Check(ui.Selection.Primary == item && Active(c, LayoutRegions.Left) == "workspace", "T4 a tree click selects and stays on the workspace (" + Active(c, LayoutRegions.Left) + ")");
                ui.Ops.Select(Array.Empty<string>());
                yield return Frames(2);
                row = TreeRowOf(ws, ui.Model.Find(item).Label);
                int shows0 = ws.InspectorShows;
                yield return ClickAt(c, row.worldBound.center, 1);
                Ev(c, new { type = "pointer", phase = "down", x = row.worldBound.center.x, y = row.worldBound.center.y, button = 0, clicks = 2 });
                Ev(c, new { type = "pointer", phase = "up", x = row.worldBound.center.x, y = row.worldBound.center.y, button = 0, clicks = 2 });
                yield return Frames(3);
                c.Check(ui.Selection.Primary == item && Active(c, LayoutRegions.Left) == "inspector" && ws.InspectorShows == shows0 + 1 && ws.RenameField == null,
                    $"T4 a tree double click selects and shows the inspector, no rename (left {Active(c, LayoutRegions.Left)}, rename {(ws.RenameField != null ? "open" : "none")})");
                sh.ShowPanel("workspace");
                ws.Focus();
                yield return Frames(2);
                Ev(c, new { type = "key", key = "Return" });
                yield return Frames(2);
                c.Check(Active(c, LayoutRegions.Left) == "inspector" && ws.InspectorShows == shows0 + 2, "T4 Enter on the focused tree shows the inspector (left " + Active(c, LayoutRegions.Left) + ", focus " + c.Host.Router.Focused?.name + ")");
                // F2 still renames
                sh.ShowPanel("workspace");
                yield return Frames(2);
                ui.Registry.Run(ActionIds.Rename, "test");
                yield return Frames(2);
                c.Check(ws.RenameField != null, "T4 F2 (edit.rename) still opens the rename");
                ws.CommitRename(false);
            }
            sh.ShowPanel("workspace");
            yield return Frames(1);
            var t = c.App.Bridge.Execute("{\"cmd\":\"ui.panel\",\"panel\":\"inspector\",\"op\":\"focus\"}");
            for (int i = 0; i < 200 && !t.IsCompleted; i++) yield return PlayModeHarness.WaitFrames(1);
            c.Check(t.IsCompleted && Active(c, LayoutRegions.Left) == "inspector", "T4 ui.panel focus inspector shows it");
            // the inspector in another region: a viewport pick switches nothing
            sh.MovePanel("inspector", LayoutRegions.Right);
            sh.ShowPanel("add.item");
            sh.ShowPanel("workspace");
            ui.Ops.Select(Array.Empty<string>());
            yield return Frames(4);
            ip = g.ToPanel(NodeBounds.Of(ui, new[] { item }, out _).center);
            int auto1 = sh.AutoShows;
            yield return ClickAt(c, ip);
            c.Check(ui.Selection.Primary == item && Active(c, LayoutRegions.Left) == "workspace" && Active(c, LayoutRegions.Right) == "add.item" && sh.AutoShows == auto1,
                $"T4 apart: a viewport pick switches nothing (left {Active(c, LayoutRegions.Left)}, right {Active(c, LayoutRegions.Right)})");
            sh.ResetLayout();
            yield return Frames(3);
            // actions that show inspector content: edit.show on a character (the Edit tab's Face node)
            var targets = TestTargets.Load();
            var ar = ui.Ops.AddChara(TestTargets.Abs(targets.visual.female), null, null, -1, null, UiContext.User());
            string chara = ar.NewIds.FirstOrDefault();
            var okc = new bool[1];
            yield return PlayModeHarness.WaitUntil(() => chara != null && ui.Runtime.FindChara(chara)?.State == RealizationState.Built, 120, okc);
            ui.Ops.Select(chara != null ? new[] { chara } : Array.Empty<string>());
            sh.ShowPanel("workspace");
            yield return Frames(2);
            var st = ui.Registry.Run(ActionIds.EditShow, "test", new Dictionary<string, object> { ["node"] = "face" });
            yield return Frames(2);
            c.Check(okc[0] && Active(c, LayoutRegions.Left) == "inspector", "T4 edit.show (BR-Chan, Face) shows the inspector (" + Active(c, LayoutRegions.Left) + ", " + st + ")");
        }
        ui.Ops.NewScene();
        yield return Frames(2);

        // ---- T5: the browsers in the sidebar and in the bottom region (beside open sidebars: the facet column; with the
        // sidebars collapsed: three columns)
        foreach (var where in new[] { "sidebar", "bottom", "bottom-wide" })
        {
            foreach (var pid in new[] { "add.item", "add.anime" })
            {
                if (where != "sidebar") sh.MovePanel(pid, LayoutRegions.Bottom);
                if (where == "bottom-wide") { sh.SetCollapsed(left, true, false); sh.SetCollapsed(right, true, false); }
                sh.FocusPanel(pid);
                yield return Frames(4);
                var ib = pid == "add.item" ? (object)root.Q<ListBrowser<StudioItem>>("items-browser") : root.Q<ListBrowser<StudioAnime>>("anime-browser");
                ListView groups = (ib as ListBrowser<StudioItem>)?.Groups ?? (ib as ListBrowser<StudioAnime>)?.Groups;
                ListView cats = (ib as ListBrowser<StudioItem>)?.Categories ?? (ib as ListBrowser<StudioAnime>)?.Categories;
                var list = (VisualElement)(ib as ListBrowser<StudioItem>)?.List ?? (ib as ListBrowser<StudioAnime>)?.List;
                if (groups == null) { c.Check(false, "T5 " + pid + " browser"); continue; }
                string g0 = ((IList<string>)groups.itemsSource).FirstOrDefault(k => k != null);
                (ib as ListBrowser<StudioItem>)?.SelectGroup(g0); (ib as ListBrowser<StudioAnime>)?.SelectGroup(g0);
                yield return Frames(4);
                var shape = (ib as ListBrowser<StudioItem>)?.BodyShape ?? (ib as ListBrowser<StudioAnime>)?.BodyShape ?? BrowserShape.Stacked;
                var body = (ib as ListBrowser<StudioItem>)?.Body ?? (ib as ListBrowser<StudioAnime>)?.Body;
                var gb = groups.worldBound; var cb = cats.worldBound; var lb = list.worldBound;
                float rowsG = gb.height / ListBrowser<StudioItem>.FacetRowHeight, rowsC = cb.height / ListBrowser<StudioItem>.FacetRowHeight;
                var pr = sh.PanelRoot(pid);
                var o = OutsideOf(pr);
                string tail = $"body {R(body?.worldBound ?? Rect.zero)}: groups {R(gb)} = {rowsG:0.#} rows, categories {R(cb)} = {rowsC:0.#} rows, list {R(lb)}; {o.count} parts outside the panel{(o.examples.Count > 0 ? ": " + string.Join("; ", o.examples) : "")}";
                bool rows = rowsG >= 4 && rowsC >= 4 && lb.height >= 80 && lb.width >= 100 && o.count == 0;
                if (where == "sidebar")
                    c.Check(shape == BrowserShape.Stacked && rows && gb.yMax <= cb.yMin + 1 && cb.yMax <= lb.yMin + 1 && gb.width > 250 && lb.height >= 200,
                        $"T5 {pid} in the sidebar: stacked; {tail}");
                else if (where == "bottom")
                    // the bottom region between the two sidebars (about 520 x 160 px of body) is too narrow for three columns:
                    // the facet lists share a column left of the list, each at least 3.5 rows
                    c.Check(shape == BrowserShape.Column && rowsG >= 3.5f && rowsC >= 3.5f && lb.width >= 250 && o.count == 0 && gb.xMax <= lb.xMin + 1 && cb.xMax <= lb.xMin + 1 && gb.yMax <= cb.yMin + 1,
                        $"T5 {pid} in the bottom region beside the sidebars: the facet lists in a column left of the list ({shape}); {tail}");
                else
                    c.Check(shape == BrowserShape.Wide && rows && gb.width >= 179.5f && cb.width >= 199.5f && gb.xMax <= cb.xMin + 1 && cb.xMax <= lb.xMin + 1,
                        $"T5 {pid} in a wide bottom region: groups | categories | list; {tail}");
                // a facet row: the name with an ellipsis, the count readable, the full text in the tooltip
                var frow = groups.Query(className: "hs-facet__row").ToList().FirstOrDefault(r => r.worldBound.height > 0 && r.Q<Label>("facet-count")?.text.Length > 0);
                var cnt = frow?.Q<Label>("facet-count");
                var nm = frow?.Q<Label>("facet-name");
                c.Check(frow != null && cnt != null && nm != null && Inside(cnt.worldBound, frow.worldBound) && cnt.worldBound.width > 4 && !string.IsNullOrEmpty(frow.tooltip) && frow.tooltip.Contains(nm.text) && frow.tooltip.Contains(cnt.text)
                        && nm.resolvedStyle.textOverflow == TextOverflow.Ellipsis,
                    $"T5 {where} {pid} facet row: name '{nm?.text}' (ellipsis), count '{cnt?.text}' inside the row, tooltip '{frow?.tooltip}'");
                (ib as ListBrowser<StudioItem>)?.SelectGroup(null); (ib as ListBrowser<StudioAnime>)?.SelectGroup(null);
            }
            if (where != "sidebar") { sh.ResetLayout(); yield return Frames(3); }
        }
        // the character and pose browsers in the 380 px sidebar
        foreach (var (pid, gridName) in new[] { ("add.chara", "chara-grid"), ("add.pose", "pose-grid") })
        {
            sh.FocusPanel(pid);
            yield return Frames(6);
            var pr = sh.PanelRoot(pid);
            var split = pr?.Q<AdaptiveSplit>();
            var grid = pr?.Q(gridName);
            int cols = pid == "add.chara" ? (CharaBrowser.Current?.Grid.Columns ?? 0) : (AddModule.Poses?.Grid.Columns ?? 0);
            var o = OutsideOf(pr);
            c.Check(split != null && split.Stacked && cols >= 2 && o.count == 0,
                $"T5 {pid} in the sidebar: folders above the grid ({(split?.Stacked == true ? "stacked" : "side by side")}), grid {R(grid?.worldBound ?? Rect.zero)} with {cols} columns; {o.count} parts outside the panel{(o.examples.Count > 0 ? ": " + string.Join("; ", o.examples) : "")}");
        }
        foreach (var pid in new[] { "add.map", "add.light", "scene", "history", "cameras" })
        {
            sh.FocusPanel(pid);
            yield return Frames(4);
            var pr = sh.PanelRoot(pid);
            var o = OutsideOf(pr);
            c.Check(pr != null && pr.worldBound.width > 300 && o.count == 0, $"T5 {pid} in the sidebar: {R(pr?.worldBound ?? Rect.zero)}, {o.count} parts outside the panel{(o.examples.Count > 0 ? ": " + string.Join("; ", o.examples) : "")}");
        }
        sh.ResetLayout();
        yield return Frames(2);

        // ---- T6: the language labels
        foreach (var lang in new[] { "en", "ja", "zh-Hans" })
        {
            c.Host.SetLanguage(lang, save: false);
            yield return Frames(1);
            c.Check(root.Q<Button>("tb-language")?.text == "Language" && L10n.Get("option.general.language") == "Language", $"T6 {lang}: the toolbar and the option read \"Language\"");
        }
        c.Host.SetLanguage("en", save: false);
        yield return Frames(1);

        // ---- T7: the window flag and a relayout after a panel scale change
#if UNITY_EDITOR
        c.Check(UnityEditor.PlayerSettings.resizableWindow && UnityEditor.PlayerSettings.fullScreenMode == FullScreenMode.Windowed
                && UnityEditor.PlayerSettings.defaultScreenWidth == 1600 && UnityEditor.PlayerSettings.defaultScreenHeight == 900 && UnityEditor.PlayerSettings.allowFullscreenSwitch,
            "T7 ProjectSettings: resizable window, windowed 1600 x 900, full-screen switch allowed");
#endif
        float scale0 = c.Host.Settings.scale;
        int rebuilds0 = sh.ResizeRebuilds;
        foreach (float k in new[] { 0.8f, 1.25f })
        {
            c.Host.Settings.scale = scale0 * k;
            yield return Frames(6);
            var rb = root.worldBound;
            var lb2 = left.Pane.worldBound; var rpb = right.Pane.worldBound; var vpb = ui.Viewport.Element.worldBound;
            var tb = root.Q("toolbar").worldBound; var stb = root.Q("statusbar").worldBound;
            bool fills = Mathf.Abs(lb2.xMin - rb.xMin) < 1 && Mathf.Abs(rpb.xMax - rb.xMax) < 1 && vpb.width >= 320 && vpb.height >= 240 && Mathf.Abs(stb.yMax - rb.yMax) < 1 && Inside(tb, rb) && Inside(stb, rb)
                         && lb2.xMax <= vpb.xMin + 0.5f && vpb.xMax <= rpb.xMin + 0.5f;
            var tabsOut = new[] { left, right }.SelectMany(r => r.Strip.Children().Where(x => !Inside(x.worldBound, r.Strip.worldBound))).Count();
            c.Check(fills && tabsOut == 0 && sh.ResizeRebuilds > rebuilds0,
                $"T7 panel scale ×{k}: root {R(rb)}, left {R(lb2)}, viewport {R(vpb)}, right {R(rpb)}, status bar {R(stb)}; tabs outside {tabsOut}; rebuilds {sh.ResizeRebuilds - rebuilds0}");
        }
        c.Host.Settings.scale = scale0;
        yield return Frames(4);
        sh.ResetLayout();
        yield return Frames(2);

        // ---- T8: an idle UI does not lay itself out again and again (the toast placement, the browser shapes and the
        // adaptive splits react to size changes only)
        sh.FocusPanel("add.item");
        yield return Frames(4);
        var watched = new List<VisualElement> { left.Pane, right.Pane, ui.Viewport.Element, root.Q("toasts") };
        watched.AddRange(root.Query(className: "hs-browser__body").ToList());
        watched.AddRange(root.Query<AdaptiveSplit>().ToList());
        int geo = 0;
        EventCallback<GeometryChangedEvent> count = _ => geo++;
        foreach (var w in watched) w?.RegisterCallback(count);
        int rb0 = sh.ResizeRebuilds;
        yield return Frames(60);
        foreach (var w in watched) w?.UnregisterCallback(count);
        c.Check(geo == 0 && sh.ResizeRebuilds == rb0, $"T8 60 idle frames: {geo} geometry changes on {watched.Count} watched elements (regions, viewport, toasts, browser bodies, adaptive splits), {sh.ResizeRebuilds - rb0} rebuilds");
        sh.ResetLayout();
        yield return Frames(2);
    }
}
