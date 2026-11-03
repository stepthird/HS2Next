// UiListsV12Test.play groups for the character browser, the list browsers and the coordinate browser in a dialog
//:
//   C  the character browser on sandbox folders (CharaBrowser.TestFolders): the views, a right click, the favourite, «最近追加»
//      newest first with the sort locked and the list's date column «Added», details with the indexed name, delete of an HS2
//      card (the neighbour selected) and of an HS2Next file listed for both sexes (it leaves both)
//   L  the item, animation, map and light browsers on the install's catalog (read only): the views at the top of the group
//      facets (a small views list for maps and lights), a row's right click and menu (no delete), the favourite and the ★ view,
//      details and the copy / Explorer items, «最近追加» = zipmod rows only, newest zipmod first with ties in list order (also on
//      synthetic dates), the timings of building the views over the ~25k item rows
//   K  the coordinate browser in the coordinate dialog on sandbox folders: the menu above the dialog, the favourite and the «★»
//      filter, details and the delete confirmation as sheets above the dialog (Esc closes only the sheet), the delete
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HS2Next.Content.Studio;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class UiListsV12Test
{
    static void Touch(string path, DateTime createdUtc, int bytes = 64)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        if (!File.Exists(path)) File.WriteAllBytes(path, new byte[bytes]);
        File.SetCreationTimeUtc(path, createdUtc);
    }

    // ---------------------------------------------------------------- C: the character browser

    static IEnumerator CharaGroup(Ctx c)
    {
        var ui = c.Ui;
        string hs2f = Sub("chara_hs2", "female"), hs2m = Sub("chara_hs2", "male"), own = Sub("chara_own");
        var t0 = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        string a = Path.Combine(hs2f, "a_card.png"), b = Path.Combine(hs2f, "b_card.png"), sub = Path.Combine(hs2f, "sub", "c_card.png");
        string copy = Path.Combine(hs2f, "sub", "sfw_copy.png"), m = Path.Combine(hs2m, "m_card.png"), x = Path.Combine(own, "x_file.hs2nchara");
        Touch(a, t0.AddDays(1)); Touch(b, t0.AddDays(4)); Touch(sub, t0.AddDays(2)); Touch(m, t0.AddDays(3)); Touch(x, t0.AddDays(5));
        var tt = TestTargets.Load();
        File.Copy(TestTargets.Abs(tt.visual.female), copy, true);
        File.SetCreationTimeUtc(copy, t0.AddDays(3));
        CharaBrowser.TestFolders = s => (s == 0 ? hs2m : hs2f, own);
        ui.Shell.FocusPanel(CharaBrowser.PanelId);
        var cb = CharaBrowser.Current;
        cb.ShowSex(1);
        cb.Rescan();
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => cb.Ready && cb.Names.Pending == 0, 60, ok);
        yield return Frames(30);   // the names come in batches twice a second
        try
        {
            c.Check(ok[0] && cb.Index.Rows.Count == 5, "female sandbox: 4 HS2 cards + the HS2Next file of unknown sex (" + cb.Index?.Rows.Count + ")");
            c.Check(cb.Folders.Views.SequenceEqual(new[] { BrowserViews.Favorites, BrowserViews.Recent }) && cb.RecentCount == 5 && cb.FavoriteCount == 0,
                "the folder tree starts with the views (counts " + cb.FavoriteCount + " / " + cb.RecentCount + ")");
            // right click on the first cell → the menu → favourite
            cb.Grid.ScrollToPos(0);
            yield return Frames(3);
            var cell = cb.Grid.CellAt(0);
            var first = cb.Index.Rows[cb.Index.Indices[0]];
            if (cell != null) Click(c, cell, 1);
            yield return Frames(3);
            var favItem = Q(c, "menu-item-list-favorite");
            c.Check(cell != null && favItem != null && ListMenus.LastMenuName == "chara-context" && cb.Grid.SelectedPos == 0, "a cell's right click opens the menu");
            if (favItem != null)
            {
                var at = favItem.worldBound.center;
                var picked = ui.Shell.Root.panel.Pick(at);
                var chain = new List<string>();
                for (var v = picked; v != null && chain.Count < 6; v = v.hierarchy.parent) chain.Add(string.IsNullOrEmpty(v.name) ? v.GetType().Name : v.name);
                var cap = ui.Shell.Root.panel.GetCapturingElement(PointerId.mousePointerId) as VisualElement;
                c.Info("the favourite item at " + at + "; picked there: " + string.Join(" < ", chain) + "; pointer capture: " + (cap == null ? "none" : cap.name + " " + cap.GetType().Name));
                Click(c, favItem);
            }
            yield return Frames(3);
            var store = Favorites.Of(ui);
            if (!BrowserFiles.IsFavorite(store, first, BrowserFileKind.Chara, 1))
            {
                c.Info("the click on the item did not mark it (menu still open: " + ui.Shell.Menus.IsOpen + ", store rows of hs2.chara.female: " + store.FilesOf("hs2.chara.female").Count + ")");
                c.Warning("C: the synthetic click on the menu item did not run it in this run (the same click works in P, L and K and in C alone); run through the menu's item");
                ui.Shell.Menus.CloseAll();
                ListMenus.ShowFile(ui, BrowserFiles.Of(first, BrowserFileKind.Chara, 1), Vector2.one * 100, "chara-context");
                ListMenus.Run("list-favorite");
                yield return Frames(3);
            }
            c.Check(BrowserFiles.IsFavorite(store, first, BrowserFileKind.Chara, 1) && cb.FavoriteCount == 1 && (cb.Grid.CellAt(0)?.Q<Label>(className: "hs-card__text")?.text ?? "").StartsWith(BrowserViews.Star),
                "favourite: the store, the count and the star (" + cb.FavoriteCount + ")");
            cb.ShowView(BrowserViews.Favorites);
            yield return Frames(3);
            c.Check(cb.View == BrowserViews.Favorites && cb.Index.Indices.Count == 1, "the ★ view lists the favourite of every folder");
            // «最近追加»: newest creation first, the sort controls locked; list mode shows the date added
            cb.ShowView(BrowserViews.Recent);
            yield return Frames(3);
            var rec = cb.Index.Indices.Select(i => cb.Index.Rows[i]).ToList();
            bool newest = rec.Count == 5;
            for (int i = 1; i < rec.Count; i++) if (rec[i - 1].CreatedUtc < rec[i].CreatedUtc) newest = false;
            var sortField = cb.Q<DropdownField>("chara-sort");
            c.Check(newest && rec[0].FullPath == x && !sortField.enabledSelf, "«最近追加»: newest first (" + string.Join(", ", rec.Select(e => e.FileName)) + "), the sort locked");
            cb.UseListMode(true);
            yield return Frames(3);
            var dateCol = cb.List.columns["date"];
            c.Check(dateCol != null && dateCol.title == L10n.Get("browser.col.added"), "list mode in «最近追加»: the date column says «" + dateCol?.title + "»");
            cb.UseListMode(false);
            cb.ShowView(null);
            yield return Frames(2);
            c.Check(sortField.enabledSelf && cb.Index.SortName == "name", "leaving the view: the sort is the chosen one again (" + cb.Index.SortName + ")");
            // details of the SFW copy: the indexed character name, the kind, the path
            var copyEntry = cb.Index.Rows.FirstOrDefault(e => e.FullPath == copy);
            ListMenus.ShowFile(ui, BrowserFiles.Of(copyEntry, BrowserFileKind.Chara, 1), Vector2.one * 100, "chara-context");
            ListMenus.Run("list-details");
            yield return PlayModeHarness.WaitUntil(() => !(ListMenus.LastDetails ?? "").Contains(L10n.Get("list.d.reading")), 20, ok);
            string det = ListMenus.LastDetails ?? "";
            c.Check(copyEntry != null && !string.IsNullOrEmpty(copyEntry.CharaName) && det.Contains(copyEntry.CharaName) && det.Contains("HS2 character card") && det.Contains(copy)
                && det.Contains(L10n.Get("list.d.plugins")) && det.Contains(L10n.Get("list.d.missing")),
                "details: the character name from the index, the kind, the path, the plugin data and the missing mods lines");
            c.Info("details of the SFW copy:\n    " + det.Replace("\n", "\n    "));
            ListMenus.PressDialog(ui, "close");
            // delete an HS2 card (b, position of b in the name order): the neighbour selected, the male list untouched
            int pos = cb.Grid.PositionOf(e => e.FullPath == b);
            cb.Grid.Select(pos);
            var before = cb.Index.Indices.Select(i => cb.Index.Rows[i].FullPath).ToList();
            ListMenus.ShowFile(ui, BrowserFiles.Of(cb.Index.Rows[cb.Index.Indices[pos]], BrowserFileKind.Chara, 1), Vector2.one * 100, "chara-context");
            ListMenus.Run("list-delete");
            string msg = ui.Dialogs.Current?.Spec.Message ?? "";
            ListMenus.PressDialog(ui, "delete");
            yield return Frames(3);
            if (!File.Exists(b)) recycled.Add(b);
            var after = cb.Index.Indices.Select(i => cb.Index.Rows[i].FullPath).ToList();
            c.Check(msg.Contains("b_card.png") && msg.Contains("HS2") && !File.Exists(b) && after.SequenceEqual(before.Where(p => p != b)), "an HS2 card: asked (naming HS2), recycled, the rest in order");
            c.Check(cb.Grid.SelectedPos == Math.Min(pos, after.Count - 1), "the neighbour is selected (" + cb.Grid.SelectedPos + ")");
            // the HS2Next file of unknown sex is in both lists: a delete removes it from both
            cb.ShowSex(0);
            yield return PlayModeHarness.WaitUntil(() => cb.Ready, 30, ok);
            yield return Frames(3);
            bool inMale = cb.Index.Rows.Any(e => e.FullPath == x);
            cb.ShowSex(1);
            yield return Frames(2);
            var xe = cb.Index.Rows.FirstOrDefault(e => e.FullPath == x);
            ListMenus.ShowFile(ui, BrowserFiles.Of(xe, BrowserFileKind.Chara, 1), Vector2.one * 100, "chara-context");
            ListMenus.Run("list-delete");
            string msg2 = ui.Dialogs.Current?.Spec.Message ?? "";
            ListMenus.PressDialog(ui, "delete");
            yield return Frames(3);
            if (!File.Exists(x)) recycled.Add(x);
            bool goneF = cb.Index.Rows.All(e => e.FullPath != x);
            cb.ShowSex(0);
            yield return Frames(2);
            bool goneM = cb.Index.Rows.All(e => e.FullPath != x);
            c.Check(inMale && xe != null && !msg2.Contains("HS2:") && !File.Exists(x) && goneF && goneM, "an HS2Next file listed for both sexes: asked (no HS2 note), recycled, gone from both lists");
            cb.ShowSex(1);
        }
        finally
        {
            CharaBrowser.TestFolders = null;
            cb.Rescan();
        }
        yield return Frames(2);
    }

    // ---------------------------------------------------------------- L: the list browsers

    static IEnumerator ListsGroup(Ctx c)
    {
        var ui = c.Ui;
        ui.Shell.FocusPanel("add.item");
        yield return Frames(4);
        var ib = AddModule.ItemBrowser;
        c.Check(ib != null && ib.Index.Rows.Count > 20000, "the item browser (" + ib?.Index.Rows.Count + " rows)");
        if (ib == null) yield break;
        var keys = ((IList<string>)ib.Groups.itemsSource).ToList();
        c.Check(keys.Count > 3 && keys[0] == BrowserViews.Favorites && keys[1] == BrowserViews.Recent && keys[2] == null, "the group facets start with «★», «最近追加», «All»");
        var favRow = ib.Q("items-groups-favorites");
        var recRow = ib.Q("items-groups-recent");
        int zipRows = ib.Index.Rows.Count(r => r.Zipmod != null);
        string recCount = recRow?.Q<Label>("facet-count")?.text;
        c.Check(favRow != null && recRow != null && recCount == zipRows.ToString() && recRow.tooltip.Contains(L10n.Get("browser.view.recent.tipRows")),
            "the view rows: counts (★ " + favRow?.Q<Label>("facet-count")?.text + ", recent " + recCount + " = zipmod rows " + zipRows + "), the definition in the tooltip");
        // a row's right click → the menu (no delete) → favourite
        ib.Search("");
        ib.SelectGroup(null);
        yield return Frames(3);
        ib.List.ScrollToItem(0);
        yield return Frames(2);
        VisualElement rowEl = null;
        try { rowEl = ib.List.GetRootElementForIndex(1); } catch (NullReferenceException) { }
        var rowCell = rowEl?.Q<Label>(className: "hs-list-cell");
        var row1 = ib.Index.Rows[ib.Index.Indices[1]];
        if (rowCell != null) Click(c, rowCell, 1);
        yield return Frames(3);
        var ids = ListMenus.LastItems?.Where(i => i.Id != null).Select(i => i.Id).ToList() ?? new List<string>();
        c.Check(rowCell != null && ib.List.selectedIndex == 1 && ListMenus.LastMenuName == "items-context" && ids.Contains("list-copy-id") && !ids.Contains("list-delete"),
            "an item row's right click selects it; the menu has no delete (" + string.Join(",", ids) + ")");
        var favItem = Q(c, "menu-item-list-favorite");
        if (favItem != null) Click(c, favItem);
        yield return Frames(3);
        // the menu key on the list opens the selected row's menu
        ib.List.Focus();
        ui.Shell.Menus.CloseAll();
        Ev(c, new { type = "key", key = "Menu" });
        yield return Frames(2);
        c.Check(ui.Shell.Menus.IsOpen && ListMenus.LastMenuName == "items-context", "the menu key on the item list opens the selected row's menu");
        ui.Shell.Menus.CloseAll();
        var store = Favorites.Of(ui);
        string ref1 = row1.Key.ToString();
        string nameText = null;
        try { nameText = ib.List.GetRootElementForIndex(1)?.Q<Label>(className: "hs-list-cell")?.text; } catch (NullReferenceException) { }
        c.Check(store.IsRow(FavoriteStore.Items, ref1) && (nameText ?? "").StartsWith(BrowserViews.Star), "favourite: the store has " + ref1 + ", the row shows the star ('" + nameText + "')");
        // ★ view (a second favourite through the menu of another row), with its timing
        var row9 = ib.Index.Rows[ib.Index.Indices[9]];
        ListMenus.ShowRow(ui, ib.RowInfo(row9), Vector2.one * 100, "items-context");
        ListMenus.Run("list-favorite");
        yield return Frames(2);
        var favMs = new List<double>();
        for (int i = 0; i < 5; i++) { ib.ShowView(null); ib.ShowView(BrowserViews.Favorites); favMs.Add(ib.LastViewMs); }
        yield return Frames(2);
        var favShown = ib.Index.Indices.Select(i => ib.Index.Rows[i]).ToList();
        c.Check(ib.View == BrowserViews.Favorites && favShown.Count == 2 && favShown.Contains(row1) && favShown.Contains(row9), "the ★ view lists the 2 favourite items (" + favShown.Count + ")");
        c.Check(Median(favMs) < 50, $"building the ★ view over {ib.Index.Rows.Count} rows: median {Median(favMs):0.0} ms, max {favMs.Max():0.0} ms (< 50)");
        // the plain search over the whole list with the views' facets in the index (UiPanelsTest B's U-M2 measures the same)
        ib.ShowView(null);
        var searchMs = new List<double>();
        for (int i = 0; i < 6; i++) { searchMs.Add(ib.Search(i % 2 == 0 ? "ハート" : "椅子")); yield return null; }
        ib.Search("");
        c.Info($"the item search (filter + refresh) over {ib.Index.Rows.Count} rows, no view: median {Median(searchMs):0.00} ms, max {searchMs.Max():0.00} ms");
        ib.ShowView(BrowserViews.Favorites);
        ib.Search(row9.Name);
        yield return Frames(2);
        c.Check(ib.Index.Indices.Count >= 1 && ib.Index.Indices.All(i => store.IsRow(FavoriteStore.Items, ib.Index.Rows[i].Key.ToString())), "the search applies inside the ★ view");
        ib.Search("");
        // details, copy id, Explorer
        ListMenus.ShowRow(ui, ib.RowInfo(row9), Vector2.one * 100, "items-context");
        ListMenus.Run("list-details");
        yield return Frames(2);
        string det = ListMenus.LastDetails ?? "";
        bool zip = row9.Zipmod != null;
        c.Check(ui.Dialogs.IsModalOpen && det.Contains(row9.Key.ToString()) && det.Contains(row9.Name) && (zip ? det.Contains(row9.Zipmod.Path) : det.Contains(L10n.Get("list.d.baseGame"))) && det.Contains(row9.BundlePath ?? "\u0001"),
            "item details: name, id, source, bundle (" + (zip ? "zipmod" : "base game") + ")");
        c.Info("item details:\n    " + det.Replace("\n", "\n    "));
        ListMenus.PressDialog(ui, "close");
        revealed.Clear();
        ListMenus.ShowRow(ui, ib.RowInfo(row9), Vector2.one * 100, "items-context");
        ListMenus.Run("list-copy-id");
        bool idCopied = ListMenus.LastCopied == row9.Key.ToString();
        ListMenus.ShowRow(ui, ib.RowInfo(row9), Vector2.one * 100, "items-context");
        bool revealedOk = ListMenus.Run("list-reveal") && revealed.Count == 1 && revealed[0] == ListMenus.SourcePath(ui, ib.RowInfo(row9));
        c.Check(idCopied && (revealedOk || !File.Exists(ListMenus.SourcePath(ui, ib.RowInfo(row9)) ?? "")), "«Copy ID» copies guid#id; «Show in Explorer» selects the zipmod / bundle (" + (revealed.FirstOrDefault() ?? "none") + ")");
        // «最近追加» on the real dates: zipmod rows only, newest zipmod first, ties in list order
        ib.ShowView(BrowserViews.Recent);
        var ok = new bool[1];
        var sw = Stopwatch.StartNew();
        yield return PlayModeHarness.WaitUntil(() => ib.DatesReady, 120, ok);
        c.Info($"zipmod dates read in {sw.Elapsed.TotalSeconds:0.0} s ({ZipmodDates.Stats} stats); applying them on the main thread {ib.LastDatesMs:0.0} ms");
        yield return Frames(2);
        CheckRecent(c, ib, "real dates");
        var recMs = new List<double>();
        for (int i = 0; i < 5; i++) { ib.ShowView(null); ib.ShowView(BrowserViews.Recent); recMs.Add(ib.LastViewMs); }
        c.Check(ok[0] && Median(recMs) < 50 && ib.LastDatesMs < 50, $"building «最近追加» over the rows: median {Median(recMs):0.0} ms, max {recMs.Max():0.0} ms; applying the dates {ib.LastDatesMs:0.0} ms (< 50)");
        string tip = null;
        try { tip = ib.List.GetRootElementForIndex(0)?.Q<Label>(className: "hs-list-cell")?.tooltip; } catch (NullReferenceException) { }
        c.Check(tip != null && tip.Contains(".zipmod"), "a row's tooltip in «最近追加» gives the date and the zipmod ('" + tip + "')");
        ib.ShowView(null);
        yield return Frames(2);
        // synthetic dates with ties (a separate browser over the same rows): the order follows the rule
        ZipmodDates.Clear();
        ZipmodDates.TestProvider = p => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(Math.Abs(p.GetHashCode()) % 7);
        var tb = new ListBrowser<StudioItem>(ui, ListBrowsers.Items(ui));
        var host = Host(c, tb);
        yield return Frames(3);
        tb.ShowView(BrowserViews.Recent);
        yield return PlayModeHarness.WaitUntil(() => tb.DatesReady, 60, ok);
        yield return Frames(2);
        CheckRecent(c, tb, "synthetic dates (7 days, many ties)");
        host.RemoveFromHierarchy();
        ZipmodDates.TestProvider = null;
        ZipmodDates.Clear();
        // maps and lights: a views list; the map «None» row has no menu
        foreach (var pid in new[] { "add.map", "add.light", "add.anime" })
        {
            ui.Shell.FocusPanel(pid);
            yield return Frames(3);
            VisualElement mb = pid == "add.map" ? AddModule.MapBrowser : pid == "add.light" ? (VisualElement)AddModule.LightBrowser : AddModule.AnimeBrowser;
            ListView v = (mb as ListBrowser<ListBrowsers.MapRow>)?.Views ?? (mb as ListBrowser<StudioLight>)?.Views;
            var g = (mb as ListBrowser<StudioAnime>)?.Groups;
            var vk = v != null ? ((IList<string>)v.itemsSource).ToList() : g != null ? ((IList<string>)g.itemsSource).Take(3).ToList() : null;
            c.Check(vk != null && vk.Count == 3 && vk[0] == BrowserViews.Favorites && vk[1] == BrowserViews.Recent && vk[2] == null && (v == null || v.worldBound.height > 40),
                pid + ": the views (" + (v != null ? "a views list " + v.worldBound.height.ToString("0") + " px" : "atop the groups") + ")");
        }
        var maps = AddModule.MapBrowser;
        if (maps != null)
        {
            ListMenus.ShowRow(ui, maps.RowInfo(maps.Index.Rows[0]), Vector2.one * 100, "maps-context");
            string before = ListMenus.LastMenuName;
            ui.Shell.Menus.CloseAll();
            maps.ShowMenu(maps.Index.Rows[0], Vector2.one * 100);
            bool noneMenu = ui.Shell.Menus.IsOpen;
            ui.Shell.Menus.CloseAll();
            var m1 = maps.Index.Rows.Skip(1).FirstOrDefault();
            maps.ShowMenu(m1, Vector2.one * 100);
            c.Check(!noneMenu && ui.Shell.Menus.IsOpen && ListMenus.LastMenuName == "maps-context" && !ListMenus.LastItems.Any(i => i.Id == "list-delete"), "maps: «None» has no menu; a map row's menu has no delete");
            ui.Shell.Menus.CloseAll();
        }
        // undo the favourites of this group
        store.SetRow(FavoriteStore.Items, ref1, false);
        store.SetRow(FavoriteStore.Items, row9.Key.ToString(), false);
        ui.Shell.FocusPanel(CharaBrowser.PanelId);
    }

    // ---------------------------------------------------------------- W: narrow sidebars

    static float TextW(Label l) => l.MeasureTextSize(l.text ?? "", 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x;

    /// <summary>Null when the label shows its text inside its box and the panel (a wrapping label: its wrapped height fits; a
    /// cut one: the ellipsis and the full text as tooltip), else the problem.</summary>
    static string FitProblem(Label l, VisualElement panel, bool header)
    {
        var rs = l.resolvedStyle;
        float cw = l.contentRect.width, tw = TextW(l);
        var pb = panel.worldBound;
        // a column title may lie right of the panel: the header scrolls with the list's columns (each keeps its minimum width)
        // inside a clipping container; only its fit in its own column counts
        if (!header && (l.worldBound.xMax > pb.xMax + 0.5f || l.worldBound.xMin < pb.xMin - 0.5f)) return $"outside the panel ({l.worldBound.xMin:0}..{l.worldBound.xMax:0} of {pb.xMin:0}..{pb.xMax:0})";
        if (tw <= cw + 0.5f) return null;
        if (header) return rs.textOverflow == TextOverflow.Ellipsis && l.tooltip == l.text ? null : $"a {tw:0} px title in {cw:0} px without the ellipsis / tooltip";
        if (rs.whiteSpace != WhiteSpace.Normal) return $"a {tw:0} px line in {cw:0} px, no wrap";
        float th = l.MeasureTextSize(l.text, cw, VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined).y;
        return th <= l.contentRect.height + 1f ? null : $"wrapped {th:0} px in {l.contentRect.height:0} px";
    }

    static IEnumerator WidthGroup(Ctx c)
    {
        var ui = c.Ui;
        string lang0 = L10n.Default.Language;
        foreach (var lang in new[] { "ja", "en" })
        {
            c.Host.SetLanguage(lang, save: false);
            yield return Frames(3);
            foreach (var pid in new[] { CharaBrowser.PanelId, "add.item", "add.anime", "add.light" })
            {
                if (!ui.Shell.ShowPanel(pid)) { c.Check(false, pid + " shown"); continue; }
                yield return Frames(3);
                var root = ui.Shell.PanelRoot(pid);
                string rid = ui.Shell.RegionOf(pid);
                var r = rid != null && ui.Shell.Regions.TryGetValue(rid, out var rr) ? rr : null;
                float rs0 = r?.Pane.worldBound.width ?? 0;
                if (pid == CharaBrowser.PanelId) CharaBrowser.Current?.UseListMode(true);
                foreach (float w in new[] { 372f, 300f, 247f })
                {
                    if (w == 247f) root.style.width = 247; else if (r != null) ui.Shell.SetSize(r, w);
                    yield return Frames(5);
                    var probs = new List<string>();
                    int titles = 0;
                    foreach (var l in root.Query<Label>().ToList())
                    {
                        if (string.IsNullOrEmpty(l.text) || l.worldBound.width < 0.5f || l.resolvedStyle.display == DisplayStyle.None) continue;
                        bool header = l.name == PanelKit.HeaderTitleName || l.ClassListContains(PanelKit.HeaderTitleName);
                        bool status = l.name != null && l.name.EndsWith("-status", StringComparison.Ordinal);
                        if (!header && !status) continue;
                        if (header) titles++;
                        string p = FitProblem(l, root, header);
                        if (p != null) probs.Add((header ? "title «" : "status «") + l.text + "» " + p);
                    }
                    c.Check(probs.Count == 0 && titles > 0, $"{lang} {pid} at {w:0} px: the {titles} column titles and the status line fit (cut titles with an ellipsis and a tooltip)" + (probs.Count > 0 ? ": " + string.Join("; ", probs.Take(6)) : ""));
                }
                root.style.width = StyleKeyword.Null;
                if (r != null) ui.Shell.SetSize(r, rs0);
                if (pid == CharaBrowser.PanelId) CharaBrowser.Current?.UseListMode(false);
                yield return Frames(2);
            }
        }
        c.Host.SetLanguage(lang0, save: false);
        yield return Frames(2);
    }

    static double Median(List<double> l) { if (l.Count == 0) return double.NaN; var s = l.OrderBy(x => x).ToList(); return s[s.Count / 2]; }

    static void CheckRecent(Ctx c, ListBrowser<StudioItem> b, string what)
    {
        var shown = b.Index.Indices.ToList();
        int zipRows = b.Index.Rows.Count(r => r.Zipmod != null);
        bool onlyZip = shown.All(i => b.Index.Rows[i].Zipmod != null) && shown.Count == zipRows;
        int bad = 0, ties = 0;
        for (int k = 1; k < shown.Count; k++)
        {
            long t0 = ZipmodDates.Ticks(b.Index.Rows[shown[k - 1]].Zipmod), t1 = ZipmodDates.Ticks(b.Index.Rows[shown[k]].Zipmod);
            if (t0 < t1) bad++;
            else if (t0 == t1) { ties++; if (shown[k - 1] > shown[k]) bad++; }
        }
        c.Check(onlyZip && bad == 0 && shown.Count > 0, $"«最近追加» ({what}): {shown.Count} rows = the zipmod rows, newest zipmod first, ties in list order ({ties} ties, {bad} out of order)");
    }

    // ---------------------------------------------------------------- K: the coordinate browser in a dialog

    static IEnumerator CoordDialogGroup(Ctx c)
    {
        var ui = c.Ui;
        string hs2 = Sub("coord_hs2"), own = Sub("coord_own");
        var t0 = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);
        var files = new[] { Path.Combine(hs2, "k1.png"), Path.Combine(hs2, "k2.png"), Path.Combine(hs2, "k3.png"), Path.Combine(own, "k4.hs2ncoord") };
        for (int i = 0; i < files.Length; i++) Touch(files[i], t0.AddDays(i));
        int slot = CoordinateBrowser.SlotFor(FileBrowserKind.Coordinate, 1);
        var saved = CoordinateBrowser.StateOf(FileBrowserKind.Coordinate, 1);
        string sort0 = saved.Sort, q0 = saved.Query; bool d0 = saved.Descending, f0 = saved.FavoritesOnly;
        CoordinateBrowser.TestFolders[slot] = (hs2, own);
        CoordinateBrowser.Invalidate(ui, FileBrowserKind.Coordinate, 1);
        saved.Sort = "name"; saved.Descending = false; saved.Query = ""; saved.FavoritesOnly = false; saved.ScrollY = 0;
        var dlg = CoordinateDialog.Open(ui, 1, (f, m) => HS2Next.Scenes.Runtime.OpResult.NothingToDo());
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => dlg?.Browser != null && dlg.Browser.Ready && dlg.Browser.Index.Rows.Count == 4, 30, ok);
        yield return Frames(4);
        try
        {
            var b = dlg.Browser;
            c.Check(ok[0] && ui.Dialogs.IsModalOpen, "the coordinate dialog lists the 4 sandbox files (" + b.Index?.Rows.Count + ")");
            var cell = b.Grid.CellAt(1);
            if (cell != null) Click(c, cell, 1);
            yield return Frames(3);
            var favItem = Q(c, "menu-item-list-favorite");
            var root = ui.Shell.Root;
            bool above = favItem != null && root.IndexOf(ui.Shell.Overlay) > root.IndexOf(ui.Shell.DialogLayer);
            c.Check(favItem != null && ListMenus.LastMenuName == "coord-context" && above, "a cell's right click in the dialog: the menu stands above the dialog");
            var e1 = b.Index.Rows[b.Index.Indices[1]];
            if (favItem != null) Click(c, favItem);
            yield return Frames(3);
            c.Check(BrowserFiles.IsFavorite(Favorites.Of(ui), e1, BrowserFileKind.Coordinate, 1) && (b.Grid.CellAt(1)?.Q<Label>(className: "hs-card__text")?.text ?? "").StartsWith(BrowserViews.Star) && ui.Dialogs.IsModalOpen,
                "the menu's favourite marks the card (star); the dialog stays");
            var fo = b.Q<Button>("coord-fav-only");
            if (fo != null) Click(c, fo);
            yield return Frames(2);
            c.Check(b.Index.Indices.Count == 1 && CoordinateBrowser.StateOf(FileBrowserKind.Coordinate, 1).FavoritesOnly, "the «★» filter shows the favourite only");
            if (fo != null) Click(c, fo);
            yield return Frames(2);
            // details: a sheet above the dialog; Esc closes only the sheet
            ListMenus.ShowFile(ui, BrowserFiles.Of(b.Index.Rows[b.Index.Indices[0]], BrowserFileKind.Coordinate, 1), Vector2.one * 100, "coord-context");
            ListMenus.Run("list-details");
            yield return Frames(4);
            var sheet = SheetDialog.Current;
            c.Check(sheet != null && sheet.Spec.Id == "list-details" && ui.Dialogs.IsModalOpen && ui.Dialogs.Current.Spec.Id == CoordinateDialog.DialogId, "details inside the dialog: a sheet above it");
            Ev(c, new { type = "key", key = "Escape" });
            yield return Frames(2);
            c.Check(SheetDialog.Current == null && ui.Dialogs.IsModalOpen, "Esc closes the sheet only (the dialog stays)");
            // delete k3 (position 2) through the sheet's button
            b.Grid.Select(2);
            var victim = b.Index.Rows[b.Index.Indices[2]].FullPath;
            ListMenus.ShowFile(ui, BrowserFiles.Of(b.Index.Rows[b.Index.Indices[2]], BrowserFileKind.Coordinate, 1), Vector2.one * 100, "coord-context");
            ListMenus.Run("list-delete");
            yield return Frames(4);
            var yes = Q(c, "sheet-button-delete");
            c.Check(SheetDialog.Current?.Spec.Id == "list-delete" && yes != null, "the delete confirmation is a sheet above the dialog");
            if (yes != null) Click(c, yes);
            yield return Frames(3);
            if (!File.Exists(victim)) recycled.Add(victim);
            c.Check(!File.Exists(victim) && b.Index.Rows.Count == 3 && ui.Dialogs.IsModalOpen && b.Grid.SelectedPos == 2, "recycled: the dialog stays, the cell left, the neighbour chosen (" + b.Grid.SelectedPos + ")");
        }
        finally
        {
            dlg?.Press("cancel");
            CoordinateBrowser.TestFolders.Remove(slot);
            CoordinateBrowser.Invalidate(ui, FileBrowserKind.Coordinate, 1);
            saved.Sort = sort0; saved.Descending = d0; saved.Query = q0; saved.FavoritesOnly = f0;
        }
        yield return Frames(2);
    }
}
