// UiPanelsTest.play groups FL and FU (same harness and Ctx as UiPanelsTest; the Maker groups' helpers).
//   FL: a part list, a push grid, the accessory slot list and the costume browser leave nothing behind on the objects that
//     outlive them. With those shown, 20 inspector rebuilds leave the handler counts of the language, of the shared thumbnails
//     and of the browser's shared loader as they were, and the lists of the first build are collected; with nothing selected the
//     counts are back to those before any list was shown; the browser's source listens to its loader only while its tab is
//     shown. An Edit tab built while another tab is shown holds lists that never reach a panel: they leave nothing either. A list still works: a list shown again finishes the thumbnails it asked for before it was hidden, and a shown list
//     follows the language.
//   FU: every shown `partlist-expand` of every page lies inside the inspector pane at the sidebar's default width and at its
//     minimum. The first button's parents go to the report with their bounds, for a reader who wants to see what is too wide.
// Characters: the SFW targets of Tools/targets.json; thumbnails are counted, never viewed.
// Run alone: & Tools\unity_batch.ps1 -Method UiPanelsTest.Run -NoQuit -MethodArgs @('-only','FL+FU')
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HS2Next.Content;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using HS2Next.UI.Panels.Maker;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class UiPanelsTest
{
    // The groups join UiPanelsTest.Groups after every domain reload (UiPanelsTest's static constructor runs first).
    [UnityEditor.InitializeOnLoadMethod]
    static void RegisterReleaseGroups()
    {
        if (!Groups.Any(g => g.id == "FL")) Groups.Add(("FL", ReleaseGroup));
        if (!Groups.Any(g => g.id == "FU")) Groups.Add(("FU", ExpandButtonGroup));
    }

    sealed class ListenerCounts
    {
        public int Language, Column, Files, FilesHidden;
        public override string ToString() => $"language {Language}, thumbnails {Column}, browser's loader shown {Files}, tab hidden {FilesHidden}";
    }

    // One inspector rebuild with every kind of list shown once, the counts read with the lists on the panel.
    static IEnumerator FlShowAll(Ctx c, string id, ListenerCounts n, List<WeakReference> dropped)
    {
        var tab = MfOpenEdit(c, id);
        yield return PlayModeHarness.WaitFrames(2);
        var insp = Inspector(c);
        if (tab == null || insp?.Chara == null) { c.Check(false, "the Edit tab of the character is there"); yield break; }
        insp.Chara.ShowTab(CharaCostumeTab.TabId);
        yield return PlayModeHarness.WaitFrames(3);
        var costume = insp.Chara.TabBodies.TryGetValue(CharaCostumeTab.TabId, out var body) ? body as CharaCostumeTab : null;
        var files = (costume?.Browser?.Grid.Source as FileThumbSource<CardEntry>)?.Loader;
        n.Files = files != null ? files.LoadedListeners : -1;
        if (costume?.Browser != null) dropped?.Add(new WeakReference(costume.Browser));
        insp.Chara.ShowTab(CharaEditTab.TabId);
        yield return PlayModeHarness.WaitFrames(2);
        n.FilesHidden = files != null ? files.LoadedListeners : -1;
        tab.Reveal("hair", InspectorSchemas.CharaEditPagesOf("hair")[0]);
        yield return PlayModeHarness.WaitFrames(3);
        var grid = tab.Query<PushGrid>().ToList().FirstOrDefault(g => g.panel != null);
        if (grid != null) dropped?.Add(new WeakReference(grid));
        tab.Reveal("accessories", InspectorSchemas.CharaEditPagesOf("accessories")[0]);
        yield return PlayModeHarness.WaitFrames(3);
        var slots = tab.Q<AccessorySlotList>();
        if (slots != null) dropped?.Add(new WeakReference(slots));
        tab.Reveal("face", "head");
        yield return PlayModeHarness.WaitFrames(3);
        var head = MfQ<PartList>(tab, "field-" + FaceRoot + "head");
        if (head != null) dropped?.Add(new WeakReference(head));
        n.Language = L10n.Default.LanguageListeners;
        n.Column = PartThumbnails.Shared(c.App.Catalog).LoadedColumnListeners;
        if (dropped != null && (grid == null || slots == null || head == null || files == null))
            c.Check(false, $"every kind of list is shown (push grid {grid != null}, slot list {slots != null}, part list {head != null}, browser {files != null})");
    }

    static IEnumerator ReleaseGroup(Ctx c)
    {
        var ui = c.Ui;
        string keepTab = CharaInspector.LastTab;
        yield return NewScene(c);
        var insp0 = Inspector(c);
        insp0?.Refresh(true);
        yield return PlayModeHarness.WaitFrames(3);
        int language0 = L10n.Default.LanguageListeners;
        var thumbs = PartThumbnails.Shared(c.App.Catalog);
        int column0 = thumbs.LoadedColumnListeners;
        var ids = new List<string>();
        yield return AddBuiltChara(c, c.FemaleCard, ids);
        if (ids.Count == 0) yield break;
        string id = ids[0];
        var dropped = new List<WeakReference>();
        var first = new ListenerCounts();
        yield return FlShowAll(c, id, first, dropped);
        c.Info($"FL1 before anything is shown: language {language0}, thumbnails {column0}; the first build: {first}");
        var last = new ListenerCounts();
        for (int i = 0; i < 20; i++) yield return FlShowAll(c, id, last, null);
        c.Info("FL1 after 20 rebuilds: " + last);
        c.Check(last.Language == first.Language, $"FL1 20 rebuilds with a part list, a push grid and the slot list shown leave the language's handlers ({first.Language} -> {last.Language})");
        c.Check(last.Column == first.Column, $"FL1 and the shared thumbnails' handlers ({first.Column} -> {last.Column})");
        c.Check(last.Files == first.Files && first.Files > 0, $"FL1 and, with the costume browser shown, its shared loader's handlers ({first.Files} -> {last.Files})");
        c.Check(last.FilesHidden == first.FilesHidden && last.FilesHidden < last.Files, $"FL1 the browser listens to its loader only while its tab is shown ({last.FilesHidden} hidden, {last.Files} shown)");
        // the counts say what the lists left on three events; this says whether they are gone: the lists of the first build, 20 rebuilds later
        yield return PlayModeHarness.WaitFrames(5);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        int alive = dropped.Count(w => w.IsAlive);
        string kinds = string.Join(", ", dropped.Where(w => w.IsAlive).Select(w => w.Target?.GetType().Name));
        c.Check(dropped.Count == 4 && alive == 0, $"FL1 the lists of the first build are collected: {alive} of {dropped.Count} still alive after a full collection" + (alive > 0 ? " (" + kinds + ")" : ""));

        // a list that went away and came back finishes what it asked for
        var tab = MfOpenEdit(c, id);
        yield return PlayModeHarness.WaitFrames(2);
        tab.Reveal("face", "head");
        yield return PlayModeHarness.WaitFrames(3);
        var head = MfQ<PartList>(tab, "field-" + FaceRoot + "head");
        c.Check(head != null, "FL1 the head list is shown");
        if (head == null) yield break;
        head.Grid.ScrollToPos(Math.Max(0, head.Items.Count - 1));
        yield return PlayModeHarness.WaitFrames(1);
        tab.Reveal("face", "shape");
        yield return PlayModeHarness.WaitFrames(2);
        tab.Reveal("face", "head");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        yield return PlayModeHarness.WaitFrames(3);
        while (thumbs.Pending > 0 && sw.Elapsed.TotalSeconds < 60) yield return PlayModeHarness.WaitFrames(1);
        yield return PlayModeHarness.WaitFrames(3);
        int loading = head.Query(className: "hs-card__image--loading").ToList().Count(MhShown);
        c.Check(thumbs.Pending == 0 && loading == 0, $"FL1 a list hidden while it loads and shown again finishes: {loading} cells still loading, {thumbs.Pending} pending");

        // a shown list follows the language; the listener is the named one on the panel
        string lang0 = L10n.Language;
        int builds = head.Builds;
        L10n.SetLanguage(lang0 == "ja" ? "en" : "ja");
        yield return PlayModeHarness.WaitFrames(2);
        c.Check(head.Builds > builds, $"FL1 a shown list follows the language ({builds} -> {head.Builds} row builds)");
        L10n.SetLanguage(lang0);
        yield return PlayModeHarness.WaitFrames(2);

        // an Edit tab built while another tab is shown holds lists that never reach a panel: nothing may keep them
        var hiddenLists = new List<WeakReference>();
        var inspector = Inspector(c);
        inspector.Chara.ShowTab("anime");
        yield return PlayModeHarness.WaitFrames(2);
        int ticks0 = -1;
        for (int i = 0; i < 20; i++)
        {
            inspector.Refresh(true);
            yield return PlayModeHarness.WaitFrames(2);
            if (i > 1) continue;
            var hiddenTab = inspector.Chara != null && inspector.Chara.TabBodies.TryGetValue(CharaEditTab.TabId, out var hb) ? hb : null;
            var hiddenList = hiddenTab?.Query<MakerThumbList>().ToList().FirstOrDefault();
            if (i == 1) { ticks0 = HS2Next.UI.Panels.PanelTicker.Count; continue; }
            c.Check(hiddenList != null && hiddenList.panel == null, "FL1 an Edit tab that is not shown holds a list that is not on a panel");
            if (hiddenList != null) hiddenLists.Add(new WeakReference(hiddenList));
        }
        c.Check(HS2Next.UI.Panels.PanelTicker.Count == ticks0, $"FL1 20 rebuilds with the Edit tab not shown leave the per-frame actions ({ticks0} -> {HS2Next.UI.Panels.PanelTicker.Count})");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        c.Check(hiddenLists.Count == 1 && !hiddenLists[0].IsAlive, $"FL1 a list that never reached a panel is collected ({hiddenLists.Count(w => w.IsAlive)} of {hiddenLists.Count} still alive)");

        // nothing selected: nothing of the lists is left
        ui.Ops.Select(new string[0]);
        Inspector(c)?.Refresh(true);
        yield return PlayModeHarness.WaitFrames(3);
        c.Check(L10n.Default.LanguageListeners == language0 && thumbs.LoadedColumnListeners == column0,
            $"FL1 with nothing selected the counts are those before any list was shown (language {L10n.Default.LanguageListeners} against {language0}, thumbnails {thumbs.LoadedColumnListeners} against {column0})");
        CharaInspector.LastTab = keepTab;
    }

    static string FuBound(Rect r) => string.Format(CultureInfo.InvariantCulture, "[{0:0.#}, {1:0.#}, {2:0.#}, {3:0.#}]", r.x, r.y, r.width, r.height);

    static string FuName(VisualElement e) => (string.IsNullOrEmpty(e.name) ? e.GetType().Name : e.name) + " ." + string.Join(".", e.GetClasses().Where(x => x.StartsWith("hs-", StringComparison.Ordinal)).Take(3));

    static IEnumerator ExpandButtonGroup(Ctx c)
    {
        var ui = c.Ui;
        string keepTab = CharaInspector.LastTab;
        yield return NewScene(c);
        var ids = new List<string>();
        yield return AddBuiltChara(c, c.FemaleCard, ids);
        if (ids.Count == 0) yield break;
        var tab = MfOpenEdit(c, ids[0]);
        yield return PlayModeHarness.WaitFrames(4);
        c.Check(tab != null && tab.panel != null, "FU1 the Edit tab is shown");
        if (tab == null) yield break;
        string region = ui.Shell.RegionOf("inspector") ?? LayoutRegions.Left;
        var reg = ui.Shell.Regions[region];
        float size0 = (float)ui.Shell.SizeOf(reg);
        c.Info($"FU1 the inspector's region {region}, width {size0:0.#} (default {LayoutRegions.DefaultSize(region):0}, minimum {LayoutRegions.MinSize(region):0})");
        float searchHeight = -1;
        foreach (float width in new[] { 360f, 300f })
        {
            ui.Shell.SetSize(reg, width);
            yield return PlayModeHarness.WaitFrames(4);
            int checkedButtons = 0, outside = 0, views = 0, taller = 0;
            var bad = new List<string>();
            var seen = new HashSet<VisualElement>();
            foreach (var node in new[] { "face", "body", "hair", "clothes", "accessories" })
            {
                var pages = InspectorSchemas.CharaEditPagesOf(node);
                IEnumerable<(string page, string part, string kind)> shows =
                    node == "hair" ? InspectorSchemas.HairParts.Select(p => (pages[0], p, (string)null)) :
                    node == "clothes" ? InspectorSchemas.ClothesEditKinds.Select(k => (pages[0], (string)null, k)) :
                    pages.Select(p => (p, (string)null, (string)null));
                foreach (var (page, part, kind) in shows.ToList())
                {
                    var sections = InspectorSchemas.CharaEditSections(node, page).Select(x => x.Id).ToList();
                    if (sections.Count == 0) sections.Add(null);
                    foreach (var section in sections)
                    {
                        var args = new Dictionary<string, object> { ["node"] = node, ["page"] = page, ["section"] = section, ["part"] = part, ["kind"] = kind };
                        if (ui.Registry.Actions.Run(ActionIds.EditShow, new ActionInvocation { Args = args }, out _) != ActionRunStatus.Ran) continue;
                        yield return PlayModeHarness.WaitFrames(3);
                        views++;
                        var now = CharaEditTab.Current ?? tab;
                        foreach (var b in now.Query<Button>("partlist-expand").ToList())
                        {
                            if (!MhShown(b) || b.resolvedStyle.display == DisplayStyle.None || b.worldBound.width <= 0 || float.IsNaN(b.worldBound.width) || !seen.Add(b)) continue;
                            checkedButtons++;
                            var wb = b.worldBound;
                            var pane = reg.Pane.worldBound;
                            var scroll = b.GetFirstAncestorOfType<ScrollView>();
                            var view = scroll != null ? scroll.contentViewport.worldBound : pane;
                            if (checkedButtons == 1)
                            {
                                c.Info($"FU1 at {width:0}: pane {FuBound(pane)}, the scroll's viewport {FuBound(view)}, the button {FuBound(wb)}");
                                var row = b.parent;
                                if (row != null) foreach (var child in row.Children()) c.Info($"FU1 at {width:0}:   in the tool row: {FuName(child)} {FuBound(child.worldBound)} (min {child.resolvedStyle.minWidth}, shrink {child.resolvedStyle.flexShrink}, grow {child.resolvedStyle.flexGrow}, basis {child.resolvedStyle.flexBasis})");
                                for (var p = b.parent; p != null && p != reg.Pane; p = p.parent) c.Info($"FU1 at {width:0}:   parent {FuName(p)} {FuBound(p.worldBound)}");
                            }
                            // the field that gives way keeps its one line, in a narrow pane too
                            float h = b.parent?.Q<TextField>("partlist-search")?.worldBound.height ?? 0;
                            if (searchHeight < 0) { searchHeight = h; c.Info($"FU1 the search field's height at {width:0}: {h:0.#}"); }
                            else if (Math.Abs(h - searchHeight) > 0.5f) taller++;
                            bool inside = wb.xMin >= view.xMin - 0.5f && wb.xMax <= view.xMax + 0.5f && wb.xMax <= pane.xMax + 0.5f;
                            if (!inside)
                            {
                                outside++;
                                if (bad.Count < 6) bad.Add($"{node}/{page}/{part ?? kind ?? section} {FuBound(wb)} against the viewport {FuBound(view)}");
                            }
                        }
                    }
                }
            }
            c.Check(views > 0 && checkedButtons > 0 && outside == 0, $"FU1 at width {width:0}: every shown expand button lies inside the pane ({checkedButtons} buttons on {views} views, {outside} outside)" + (bad.Count > 0 ? ": " + string.Join("; ", bad) : ""));
            c.Check(taller == 0, $"FU1 at width {width:0}: the search field keeps its height ({searchHeight:0.#}; {taller} differ)");
        }
        ui.Shell.SetSize(reg, size0);
        yield return PlayModeHarness.WaitFrames(3);
        CharaInspector.LastTab = keepTab;
    }
}
