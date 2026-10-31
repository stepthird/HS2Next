// UiPanelsTest.play (packages C and I): the app with the UI in Editor play mode (PlayModeHarness;
// launch override app on + ui.enabled true; -nographics possible per spike S7). Package C's groups (this file):
//   T tree: add / select (U11, Ctrl, Shift) / rename / drag-reparent / reorder / attach point rows / visibility / context
//     menu / isolate / delete / search filter / hs2studio O+click → model and history labels;
//   R tree rebuild at 2,000 nodes;
//   B list browsers: item search over the full list (U-M2 timing), add by activation; animations (H rows included), maps,
//     lights;
//   G card grid: scan, lazily indexed names, thumbnails bound and decoded (count only, U-M1 timings and LRU bytes), add,
//     change character (any sex, U27), list mode;
//   D drags: browser row → viewport (surface hit / pivot, one entry), → tree row (parent), the drag controller's drop;
//   F files: save as / dirty prompt / new / open / import / scene browser / recent;
//   O options panel and key-binding overlay (record, reset);
//   P pose browser on an HS2Next pose saved from the SFW target (no HS2 pose corpus scan).
// Package I adds its groups in Editor/UiPanelsTest_Inspector.cs (static partial void AddMoreGroups(), same Ctx).
// Characters: the SFW targets of Tools/targets.json; card images are decoded and counted, never viewed.
// Run: & Tools\unity_batch.ps1 -Method UiPanelsTest.Run -NoQuit [-NoGraphics] [-MethodArgs @('-only','T+F')] (groups in that order)
// Report: Spike/out/ui/ui_panels_report.txt ("SUMMARY: N PASS, M FAIL").
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

public static partial class UiPanelsTest
{
    public const int Port = 17353;
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "ui");
    /// <summary>ui_panels_report.txt for a full run (run_tests); with -only G1+G2 ui_panels_report_G1+G2.txt, so partial runs
    /// of packages C and I do not overwrite each other's (or the full run's) report.</summary>
    static string ReportPath
    {
        get
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, "-only");
            string only = i >= 0 && i + 1 < a.Length ? "_" + a[i + 1] : "";
            return Path.Combine(OutDir, "ui_panels_report" + only + ".txt");
        }
    }
    static string UserDataDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "ui_panels", "userdata");
    public static string Launch =>
        "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},\"userData\":\"Spike/out/ui_panels/userdata\",\"bridge\":{\"port\":" + Port + ",\"outputDir\":\"Spike/out/ui_panels/out\",\"allowQuit\":false}}";

    public static void Run()
    {
        Directory.CreateDirectory(OutDir);
        // fresh config / scene / pose folders each run (layout, options, keymap, recent files of earlier runs)
        foreach (var sub in new[] { "config", "scene", "pose" })
        {
            string d = Path.Combine(UserDataDir, sub);
            try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch (Exception) { }
        }
        File.WriteAllText(ReportPath, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("UiPanelsTest.Play", ReportPath, 30, true, Launch);
    }

    sealed class Ctx
    {
        public readonly StringBuilder Rep = new StringBuilder();
        public int Pass, Fail, Warn;
        public string Group = "";
        public HashSet<string> Only;
        public UiHost Host;
        public UiContext Ui => Host.Context;
        public AppCore App;
        public TestTargets Targets;
        public string FemaleCard, MaleCard;
        public bool Want(string g) => Only == null || Only.Contains(g);
        public void Check(bool ok, string what)
        {
            if (ok) Pass++; else Fail++;
            string l = (ok ? "PASS " : "FAIL ") + Group + " " + what;
            Rep.AppendLine(l);
            Debug.Log("UI PANELS " + l);
        }
        public void Warning(string what) { Warn++; Rep.AppendLine("WARN " + Group + " " + what); Debug.Log("UI PANELS WARN " + Group + " " + what); }
        public void Info(string what) { Rep.AppendLine("INFO " + Group + " " + what); Debug.Log("UI PANELS INFO " + Group + " " + what); }
        public string UndoLabel => App.Session.Commands.UndoList.FirstOrDefault()?.Label;
    }

    static readonly List<(string id, Func<Ctx, IEnumerator> run)> Groups = new List<(string, Func<Ctx, IEnumerator>)>();

    static UiPanelsTest()
    {
        Groups.Add(("T", TreeGroup));
        Groups.Add(("R", RebuildGroup));
        Groups.Add(("B", BrowserGroup));
        Groups.Add(("G", GridGroup));
        Groups.Add(("D", DragGroup));
        Groups.Add(("F", FilesGroup));
        Groups.Add(("O", OptionsGroup));
        Groups.Add(("P", PoseGroup));
        AddMoreGroups();
    }

    /// <summary>Package I's groups (UiPanelsTest_Inspector.cs).</summary>
    static partial void AddMoreGroups();

    static IEnumerator Play()
    {
        var c = new Ctx();
        var args = Environment.GetCommandLineArgs();
        int oi = Array.IndexOf(args, "-only");
        string[] onlyOrder = null;
        if (oi >= 0 && oi + 1 < args.Length) { onlyOrder = args[oi + 1].Split('+'); c.Only = new HashSet<string>(onlyOrder); }
        c.Rep.AppendLine($"UiPanelsTest.play (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, screen {Screen.width}x{Screen.height})");
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ok);
        c.App = AppCore.Current;
        c.Group = "H";
        c.Check(ok[0] && c.App.Stage == AppStage.Ready, "app ready (" + (c.App?.Stage.ToString() ?? "none") + (c.App?.Error != null ? ": " + c.App.Error : "") + ")");
        yield return PlayModeHarness.WaitUntil(() => UiHost.Current != null && UiHost.Current.IsReady, 30, ok);
        c.Host = UiHost.Current;
        c.Check(ok[0], "UiHost ready");
        if (!ok[0]) { Finish(c); yield break; }
        c.Targets = TestTargets.Load();
        c.FemaleCard = TestTargets.Abs(c.Targets.visual.female);
        c.MaleCard = TestTargets.Abs(c.Targets.visual.male);
        c.Check(File.Exists(c.FemaleCard) && File.Exists(c.MaleCard), "SFW target cards exist");
        foreach (var id in new[] { WorkspacePanel.PanelId, CharaBrowser.PanelId, "add.item", "add.anime", "add.map", "add.light", PoseBrowser.PanelId })
            c.Check(c.Ui.Registry.Panels.TryGet(id, out _), "panel registered: " + id);
        yield return PlayModeHarness.WaitFrames(5);
        // With -only the groups run in the order written there: the Maker groups join the list from load-time hooks
        // whose order is not fixed, and a reproduction may need one group to run right after another.
        var order = onlyOrder == null ? Groups : Groups.OrderBy(x => Array.IndexOf(onlyOrder, x.id)).ToList();
        foreach (var g in order)
        {
            if (!c.Want(g.id)) continue;
            c.Group = g.id;
            CInput.TestHeldKeys = null;
            IEnumerator e = null;
            try { e = g.run(c); }
            catch (Exception ex) { c.Check(false, "threw " + ex); continue; }
            while (true)
            {
                bool more;
                try { more = e.MoveNext(); }
                catch (Exception ex) { c.Check(false, "threw " + ex); break; }
                if (!more) break;
                yield return e.Current;
            }
            while (c.Ui.Dialogs != null && c.Ui.Dialogs.IsModalOpen) c.Ui.Dialogs.CloseCurrent();
        }
        Finish(c);
    }

    static void Finish(Ctx c)
    {
        c.Rep.Insert(0, $"SUMMARY: {c.Pass} PASS, {c.Fail} FAIL, {c.Warn} WARN\n");
        File.WriteAllText(ReportPath, c.Rep.ToString(), new UTF8Encoding(false));
        Debug.Log($"UI PANELS TEST DONE: {c.Pass} PASS, {c.Fail} FAIL");
        PlayModeHarness.ExitCode = c.Fail > 0 ? 1 : 0;
    }

    // ---------------------------------------------------------------- helpers

    static ExecuteOptions User => UiContext.User();

    /// <summary>A panel's content element (built through the shell, so its tab exists).</summary>
    static T Panel<T>(Ctx c, string id) where T : VisualElement
    {
        c.Ui.Shell?.FocusPanel(id);
        var root = c.Ui.Shell?.PanelRoot(id);
        if (root is T t) return t;
        return root?.Q<T>();
    }

    static IEnumerator NewScene(Ctx c)
    {
        while (c.Ui.Dialogs.IsModalOpen) c.Ui.Dialogs.CloseCurrent();
        SceneFiles.Current.NewNow();
        yield return PlayModeHarness.WaitFrames(2);
    }

    static string FirstItemRef(Ctx c) => StudioDb.FallbackItem.ToString();

    static IEnumerator WaitFor(Func<bool> done, double sec, bool[] ok) => PlayModeHarness.WaitUntil(done, sec, ok);

    static double Median(List<double> l) { if (l.Count == 0) return double.NaN; var s = l.OrderBy(x => x).ToList(); return s[s.Count / 2]; }
    static double Pct(List<double> l, double p) { if (l.Count == 0) return double.NaN; var s = l.OrderBy(x => x).ToList(); return s[Math.Min(s.Count - 1, (int)(p * s.Count))]; }

    // ---------------------------------------------------------------- T: tree

    static IEnumerator TreeGroup(Ctx c)
    {
        var ui = c.Ui;
        yield return NewScene(c);
        var ws = Panel<WorkspacePanel>(c, WorkspacePanel.PanelId) ?? WorkspacePanel.Current;
        c.Check(ws != null, "workspace panel built");
        if (ws == null) yield break;
        ws.RebuildNow();
        c.Check(ws.Rows.RowCount == 0, "empty scene: no rows");

        ui.Registry.Run(ActionIds.AddFolder, "test");
        string folder = ui.Selection.Primary;
        c.Check(folder != null && ui.Model.Find(folder)?.Kind == NodeKind.Folder, "add.folder: a folder, selected (U14)");
        c.Check(c.UndoLabel == OpLabels.AddFolder, "history label " + c.UndoLabel);
        var item = ui.Ops.AddItem(StudioDb.FallbackItem, null, null, -1, User);
        string itemId = item.NewIds.FirstOrDefault();
        c.Check(item.Ok && itemId != null && ui.Selection.Primary == itemId, "item added and selected");
        ui.Registry.Run(ActionIds.AddCamera, "test");
        string cam = ui.Selection.Primary;
        var lightRow = c.App.Db.Lights.Enumerate().FirstOrDefault();
        var lr = lightRow != null ? ui.Ops.AddLight(lightRow.Key, null, null, -1, User) : null;
        string light = lr?.NewIds.FirstOrDefault();
        yield return PlayModeHarness.WaitFrames(3);
        c.Check(ws.Rows.RowCount == ui.Model.Count, "the tree follows adds by itself (" + ws.Rows.RowCount + " / " + ui.Model.Count + ")");
        ws.RebuildNow();
        c.Check(ws.Rows.RowCount == ui.Model.Count, "rows = nodes (" + ws.Rows.RowCount + " / " + ui.Model.Count + ")");

        // clicks (U11)
        ws.Click(folder, false, false);
        c.Check(ui.Selection.Ids.SequenceEqual(new[] { folder }), "click selects only that node");
        ws.Click(folder, false, false);
        c.Check(ui.Selection.Ids.SequenceEqual(new[] { folder }), "click on the sole selected node keeps it (U11)");
        ws.Click(itemId, true, false);
        c.Check(ui.Selection.Count == 2 && ui.Selection.Primary == folder, "Ctrl+click adds, primary stays");
        ws.Click(itemId, true, false);
        c.Check(ui.Selection.Ids.SequenceEqual(new[] { folder }), "Ctrl+click toggles off");
        ws.Click(cam, false, true);
        var vis = ws.VisibleNodeIds();
        var expect = vis.GetRange(vis.IndexOf(folder), vis.IndexOf(cam) - vis.IndexOf(folder) + 1);
        c.Check(ui.Selection.Ids.SequenceEqual(expect) && ui.Selection.Primary == folder, "Shift+click = visible range, primary first (" + ui.Selection.Count + ")");

        // rename
        string oldName = ui.Model.Find(folder).Doc.Name;
        ws.BeginRename(folder);
        yield return PlayModeHarness.WaitFrames(2);
        c.Check(ws.RenameField != null, "rename field open");
        if (ws.RenameField != null) { ws.RenameField.value = "Props"; ws.CommitRename(true); }
        c.Check(ui.Model.Find(folder).Doc.Name == "Props" && c.UndoLabel == OpLabels.Rename, "rename → name 'Props', entry " + c.UndoLabel);
        ui.Session.Commands.Undo();
        c.Check(ui.Model.Find(folder).Doc.Name == oldName, "undo rename → '" + oldName + "'");
        ui.Session.Commands.Redo();
        ws.BeginRename(folder);
        yield return PlayModeHarness.WaitFrames(2);
        if (ws.RenameField != null) { ws.RenameField.value = "  "; ws.CommitRename(true); }
        c.Check(ui.Model.Find(folder).Doc.Name == null, "empty rename → default label (name null: " + ui.Model.Find(folder).Label + ")");
        ws.BeginRename(folder);
        yield return PlayModeHarness.WaitFrames(2);
        if (ws.RenameField != null) { ws.RenameField.value = "Ignored"; ws.CommitRename(false); }
        c.Check(ui.Model.Find(folder).Doc.Name == null, "Esc cancels the rename");

        // drag-reparent / reorder / refused
        ws.DropAt(new NodesPayload(new List<string> { itemId }, "item"), folder, null, -1);
        c.Check(ui.Model.Find(itemId).Parent?.Id == folder && c.UndoLabel == OpLabels.Parent, "drop on a folder row → parent, entry " + c.UndoLabel);
        ui.Session.Commands.Undo();
        c.Check(ui.Model.Find(itemId).Parent == null, "undo → back at the root");
        ws.DropAt(new NodesPayload(new List<string> { cam }, "cam"), null, null, 0);
        c.Check(ui.Model.Find(cam).Index == 0 && c.UndoLabel == OpLabels.Reorder, "drop between rows → reorder to 0, entry " + c.UndoLabel);
        c.Check(light == null || WorkspaceRows.MoveProblem(ui.Model, itemId, light, null) != null, "a light takes no children (drop refused: " + (light == null ? "-" : WorkspaceRows.MoveProblem(ui.Model, itemId, light, null)) + ")");
        c.Check(WorkspaceRows.MoveProblem(ui.Model, folder, folder, null) != null, "no move under itself");

        // a character with an attached item: group / point rows
        var ch = ui.Ops.AddChara(c.FemaleCard, null, null, -1, null, User);
        string chara = ch.NewIds.FirstOrDefault();
        c.Check(ch.Ok && chara != null, "SFW female card added (" + ch.Message + ")");
        yield return PlayModeHarness.WaitFrames(10);
        if (chara != null)
        {
            ws.DropAt(new NodesPayload(new List<string> { itemId }, "item"), chara, "N_Head_top", -1);
            var n = ui.Model.Find(itemId);
            c.Check(n.Parent?.Id == chara && n.Point == "N_Head_top", "drop on a point → attached to N_Head_top");
            ws.RebuildNow();
            var row = ws.Rows.RowOfNode(itemId);
            c.Check(row?.Parent?.Kind == TreeRowKind.Point && row.Parent.Parent?.Kind == TreeRowKind.Group, "point and group rows appear only with children (U12)");
            c.Check(WorkspaceRows.MoveProblem(ui.Model, folder, chara, null) != null, "a character child needs an attach point");
            c.Check(L10n.Get("attach.point.N_Head_top") != "attach.point.N_Head_top" && L10n.Get("attach.group.1") != "attach.group.1", "attach point / group names localised");
        }

        // visibility
        ui.Selection.Set(new[] { folder });
        ui.Registry.Run(ActionIds.ViewHideSelected, "test");
        c.Check(!ui.Model.Find(folder).Doc.Visible && c.UndoLabel == OpLabels.Visibility, "hide selected → visible false, entry " + c.UndoLabel);
        ui.Registry.Run(ActionIds.ViewShowAll, "test");
        c.Check(ui.Model.Find(folder).Doc.Visible, "show all");

        // context menu
        ws.ContextMenu(folder, new Vector2(100, 100));
        yield return PlayModeHarness.WaitFrames(1);
        c.Check(ui.Shell.Menus.IsOpen, "context menu opens");
        ui.Shell.Menus.CloseAll();

        // isolate (view only)
        if (chara != null)
        {
            var other = ui.Ops.AddItem(StudioDb.FallbackItem, null, null, -1, User);
            yield return PlayModeHarness.WaitFrames(5);
            ui.Selection.Set(new[] { chara });
            byte[] before = ui.Model.CanonicalBytes();
            ui.Registry.Run(ActionIds.ViewIsolate, "test");
            int hidden = Isolate.HiddenCount;
            c.Check(ws.IsIsolated && hidden > 0, "isolate hides other renderers (" + hidden + ")");
            c.Check(before.SequenceEqual(ui.Model.CanonicalBytes()), "isolate does not change the document");
            ui.Registry.Run(ActionIds.ViewIsolate, "test");
            c.Check(!ws.IsIsolated && Isolate.HiddenCount == 0, "isolate off restores");
            ui.Ops.Delete(other.NewIds, User);
        }

        // delete (no confirmation by default, U17), undo
        ui.Selection.Set(new[] { folder });
        ui.Registry.Run(ActionIds.Delete, "test");
        c.Check(ui.Model.Find(folder) == null && ui.Selection.Count == 0 && c.UndoLabel == OpLabels.Delete, "edit.delete → gone, nothing selected, entry " + c.UndoLabel);
        ui.Session.Commands.Undo();
        c.Check(ui.Model.Find(folder) != null, "undo delete");

        // search filter keeps ancestors
        if (chara != null)
        {
            ws.Rows.Build(ui.Model, ui.Model.Find(itemId).Label, n => n.Label);
            ws.Rebuild();
            c.Check(ws.Rows.RowOfNode(itemId) != null && ws.Rows.RowOfNode(chara) != null && ws.Rows.RowOfNode(cam) == null, "search keeps matches and ancestors");
            ws.Rows.Build(ui.Model, "", n => n.Label);
            ws.Rebuild();
        }

        // keyboard: tree.next / tree.prev, select all (tree focus)
        ui.Selection.Set(new[] { ws.VisibleNodeIds()[0] });
        ui.Registry.Run(ActionIds.TreeNext, "test");
        c.Check(ui.Selection.Primary == ws.VisibleNodeIds()[1], "tree.next moves the primary");
        ui.Registry.Run(ActionIds.TreePrev, "test");
        c.Check(ui.Selection.Primary == ws.VisibleNodeIds()[0], "tree.prev");
        ui.Registry.RunWith(ActionIds.SelectAll, new ActionInvocation { Source = "test", Args = new Dictionary<string, object> { ["tree"] = true } }, out _);
        c.Check(ui.Selection.Count == ws.VisibleNodeIds().Count, "select all (tree) = visible rows");

        // hs2studio O+click (K7): detach the clicked node to the root
        if (chara != null)
        {
            ui.Options.Set(OptionDefs.KeyPreset, Keymap.Hs2StudioPreset);
            yield return PlayModeHarness.WaitFrames(2);
            CInput.TestHeldKeys = new HashSet<string> { "O" };
            ws.Click(itemId, false, false);
            CInput.TestHeldKeys = null;
            c.Check(ui.Model.Find(itemId).Parent == null && ui.Selection.Primary == itemId, "hs2studio O+click detaches to the root (" + ui.Keys.PresetName + ")");
            ui.Options.Set(OptionDefs.KeyPreset, Keymap.DefaultPreset);
            yield return PlayModeHarness.WaitFrames(2);
        }
        c.Info("tree rebuilds " + ws.RebuildCount + ", last " + ws.LastRebuildMs.ToString("0.0") + " ms");
    }

    // ---------------------------------------------------------------- R: rebuild at 2,000 nodes

    static IEnumerator RebuildGroup(Ctx c)
    {
        var ui = c.Ui;
        yield return NewScene(c);
        var ws = Panel<WorkspacePanel>(c, WorkspacePanel.PanelId) ?? WorkspacePanel.Current;
        var sw = Stopwatch.StartNew();
        using (var edit = ui.BeginEdit(OpLabels.AddFolder))
        {
            string parent = null;
            for (int i = 0; i < 2000; i++)
            {
                var r = ui.Ops.AddFolder(i % 20 == 0 ? null : parent, null, -1, User);
                if (i % 20 == 0) parent = r.NewIds.FirstOrDefault();
            }
            edit.Commit();
        }
        c.Info("2,000 folders added in " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s");
        yield return PlayModeHarness.WaitFrames(2);
        var times = new List<double>();
        for (int i = 0; i < 5; i++) { ws.RebuildNow(); times.Add(ws.LastRebuildMs); yield return null; }
        double med = Median(times);
        c.Check(ui.Model.Count == 2000 && ws.Rows.RowCount == 2000, "2,000 rows (" + ws.Rows.RowCount + ")");
        if (med <= 50) c.Check(true, "rebuild at 2,000 nodes median " + med.ToString("0.0") + " ms (≤ 50)");
        else if (med <= 100) c.Warning("rebuild at 2,000 nodes median " + med.ToString("0.0") + " ms (target 50)");
        else c.Check(false, "rebuild at 2,000 nodes median " + med.ToString("0.0") + " ms (target 50)");
        yield return NewScene(c);
    }

    // ---------------------------------------------------------------- B: list browsers

    static IEnumerator BrowserGroup(Ctx c)
    {
        var ui = c.Ui;
        yield return NewScene(c);
        Panel<VisualElement>(c, "add.item");
        yield return PlayModeHarness.WaitFrames(2);
        var items = AddModule.ItemBrowser;
        c.Check(items != null, "item browser built");
        if (items != null)
        {
            // the item rows, then the accessories offered as items (group 647158; 5.2)
            int all = c.App.Db.Items.VisibleCount + c.App.Db.AccessoryItems.Count;
            c.Check(items.Index.Rows.Count == all, "item rows = visible items + the accessories offered as items (" + items.Index.Rows.Count + ")");
            var qs = new[] { "a", "椅子", "box", "テーブル", "light", "illusion", "ka", "chair table", "#399", "zzzz-none" };
            var t = new List<double>();
            for (int rep = 0; rep < 3; rep++)
                foreach (var q in qs) { t.Add(items.Search(q)); yield return null; }
            items.Search("");
            c.Info($"U-M2 (Unity): item filter + refresh over {all} rows: median {Median(t):0.00} ms, p95 {Pct(t, 0.95):0.00}, max {t.Max():0.00} ms");
            // ms; the Editor runs debug-codegen Mono (P's plain-Mono figure is 2.1 ms): warn up to 10 ms
            if (Median(t) <= 5) c.Check(true, "item filter median ≤ 5 ms (" + Median(t).ToString("0.00") + ")");
            else if (Median(t) <= 10) c.Warning("item filter median " + Median(t).ToString("0.00") + " ms (target 5; Editor debug codegen)");
            else c.Check(false, "item filter median " + Median(t).ToString("0.00") + " ms (target 5)");
            items.Search("#399");
            c.Check(items.Index.Indices.Count >= 1, "guid#id search finds item 399 (" + items.Index.Indices.Count + ")");
            items.List.SetSelection(0);
            var r = items.ActivateSelected();
            c.Check(r != null && r.Ok && ui.Model.Find(ui.Selection.Primary)?.Kind == NodeKind.Item && c.UndoLabel == OpLabels.AddItem, "activate adds the item, selected, entry " + c.UndoLabel);
            items.SelectGroup(items.Index.Rows.Count > 0 ? items.Index.Rows[0].Group.ToString() : null);
            c.Check(items.Index.Indices.All(i => items.Index.Rows[i].Group == items.Index.Rows[items.Index.Indices[0]].Group), "group facet filters");
            items.SelectGroup(null);
        }
        Panel<VisualElement>(c, "add.anime");
        Panel<VisualElement>(c, "add.map");
        Panel<VisualElement>(c, "add.light");
        yield return PlayModeHarness.WaitFrames(2);
        var anime = AddModule.AnimeBrowser;
        int animeH = anime != null ? anime.Index.Rows.Count(a => a.IsH) : 0;
        c.Check(anime != null && anime.Index.Rows.Count == c.App.Db.Anime.VisibleCount && animeH == c.App.Db.Anime.Enumerate().Count(a => a.IsH) && animeH > 0,
            "animation browser lists every listed row, H rows included (" + anime?.Index.Rows.Count + " rows, " + animeH + " H)");
        var maps = AddModule.MapBrowser;
        c.Check(maps != null && maps.Index.Rows.Count == c.App.Db.Maps.VisibleCount + 1 && maps.Index.Rows[0].Map == null, "map rows = maps + None first");
        var lights = AddModule.LightBrowser;
        c.Check(lights != null && lights.Index.Rows.Count == c.App.Db.Lights.VisibleCount, "light rows (" + lights?.Index.Rows.Count + ")");
        if (lights != null && lights.Index.Rows.Count > 0)
        {
            lights.List.SetSelection(0);
            var r = lights.ActivateSelected();
            c.Check(r != null && r.Ok && c.UndoLabel == OpLabels.AddLight, "add light, entry " + c.UndoLabel);
        }
        if (anime != null)
        {
            var row = anime.Index.Rows.FirstOrDefault(a => a.Group == 19 && a.Category == 0 && a.No == 0) ?? anime.Index.Rows.FirstOrDefault();
            var ch = ui.Ops.AddChara(c.FemaleCard, null, null, -1, null, User);
            yield return PlayModeHarness.WaitFrames(10);
            anime.SelectRow(row);
            var r = anime.ActivateSelected();
            var st = ui.Model.Find(ch.NewIds.FirstOrDefault())?.State as CharaNodeState;
            c.Check(r != null && r.Ok && c.UndoLabel == OpLabels.Animation, "animation applied to the selected character (" + row?.Name + "), entry " + c.UndoLabel);
        }
    }

    // ---------------------------------------------------------------- G: card grid

    static IEnumerator GridGroup(Ctx c)
    {
        var ui = c.Ui;
        yield return NewScene(c);
        Panel<VisualElement>(c, CharaBrowser.PanelId);
        var cb = CharaBrowser.Current;
        c.Check(cb != null, "character browser built");
        if (cb == null) yield break;
        cb.ShowSex(1);
        var ok = new bool[1];
        yield return WaitFor(() => cb.Ready, 60, ok);
        c.Check(ok[0] && cb.Library.Entries.Count > 0, "female cards scanned: " + cb.Library?.Entries.Count + " in " + cb.Library?.ScanMs.ToString("0") + " ms, folders " + cb.Library?.Folders.Count);
        if (!ok[0]) yield break;
        // names indexed lazily (header + Parameter only)
        yield return WaitFor(() => cb.Names.Pending == 0, 60, ok);
        c.Check(ok[0], $"names indexed: read {cb.Names.Read}, cached {cb.Names.Cached}, unreadable {cb.Names.Failed}, worker {cb.Names.WorkerMs:0} ms");
        var target = cb.Library.Entries.FirstOrDefault(e => string.Equals(e.FullPath, Path.GetFullPath(c.FemaleCard), StringComparison.OrdinalIgnoreCase));
        c.Check(target != null && !string.IsNullOrEmpty(target.CharaName), "the SFW target card has its character name indexed");
        // the batch screen (640×480) is below the shell's minimum layout: after the item browser's tab the bottom region can
        // end up below the screen with no height; a layout reset brings it back for the thumbnail sample
        ui.Registry.Run(ActionIds.UiResetLayout, "test");
        yield return PlayModeHarness.WaitFrames(3);
        Panel<VisualElement>(c, CharaBrowser.PanelId);
        for (int i = 0; i < 20; i++) { cb.Pump(); yield return null; }
        {
            string reg = ui.Shell.RegionOf(CharaBrowser.PanelId);
            var r = reg != null ? ui.Shell.Regions[reg] : null;
            c.Info($"chara browser: attached {cb.panel != null}, region {reg} (collapsed {r?.Collapsed}, active {r?.Active}), bound {cb.worldBound}, root display {ui.Shell.PanelRoot(CharaBrowser.PanelId)?.resolvedStyle.display}");
        }
        // thumbnails: scroll a sample of the grid (about 150 cards) and let the loader finish (count only)
        var grid = cb.Grid;
        c.Check(grid.BindCount > 0 && grid.Columns >= 1, "grid binds cells (" + grid.BindCount + ", " + grid.Columns + " columns, list " + grid.List.resolvedStyle.width.ToString("0") + "×" + grid.List.resolvedStyle.height.ToString("0") + " px)");
        var frames = new List<double>();
        var sv = grid.List.Q<ScrollView>();
        int rowsWanted = Math.Max(1, 150 / Math.Max(1, grid.Columns));
        float rowH = grid.List.fixedItemHeight;
        // one row per step; each step waits (≤ 3 s) until the visible cells are decoded, so ~150 cards are measured. The harness
        // ticks on editor updates, which outnumber player frames in batch play: frames are counted by Time.frameCount.
        int stuck = 0, lastFrame = Time.frameCount;
        for (int r = 0; r <= rowsWanted; r++)
        {
            grid.List.ScrollToItem(Math.Min(r, Math.Max(0, grid.List.itemsSource.Count - 1)));
            var sw = Stopwatch.StartNew();
            int f0 = Time.frameCount;
            while (true)
            {
                yield return null;
                if (Time.frameCount != lastFrame) { lastFrame = Time.frameCount; frames.Add(Time.unscaledDeltaTime * 1000.0); }
                if (Time.frameCount - f0 > 2 && cb.Loader.Pending == 0) break;
                if (sw.Elapsed.TotalSeconds > 3) { stuck++; break; }
            }
        }
        c.Info($"grid sample: {rowsWanted} rows scrolled, steps that did not settle in 3 s {stuck}, loader pending {cb.Loader.Pending} (in flight {cb.Loader.InFlight}, oldest {cb.Loader.OldestInFlightMs:0} ms, waiting {cb.Loader.Waiting}), updates {cb.Loader.Updates}, bind count {grid.BindCount}");
        yield return WaitFor(() => cb.Loader.Pending == 0, 30, ok);
        var L = cb.Loader;
        c.Check(L.Decoded > 0, $"thumbnails decoded {L.Decoded}, unreadable {L.FailedCount}, shrunk {L.Shrunk}, evicted {L.Evicted}");
        c.Info($"U-M1: main-thread accept per card median {Median(L.MainThreadMs):0.000} ms, p95 {Pct(L.MainThreadMs, 0.95):0.000}, max {(L.MainThreadMs.Count > 0 ? L.MainThreadMs.Max() : 0):0.00} ms; worker decode latency median {Median(L.LatencyMs):0} ms; GPU shrink max {(L.ShrinkMs.Count > 0 ? L.ShrinkMs.Max() : 0):0.00} ms; LRU peak {L.PeakBytes / 1048576.0:0.0} MB, now {L.CurrentBytes / 1048576.0:0.0} MB ({L.Count} textures)");
        c.Info($"grid scroll frames: {frames.Count}, median {Median(frames):0.0} ms, p95 {Pct(frames, 0.95):0.0}, max {(frames.Count > 0 ? frames.Max() : 0):0.0}, > 20 ms: {frames.Count(x => x > 20)}");
        c.Check(L.PeakBytes <= ThumbnailLoader.DefaultCapBytes, "LRU ≤ 110 MB (" + (L.PeakBytes / 1048576.0).ToString("0.0") + ")");
        // search by character name, add, change character
        cb.Index.SetQuery(target?.CharaName ?? "");
        cb.Refresh();
        c.Check(target != null && cb.Index.Indices.Any(i => cb.Index.Rows[i] == target), "search by character name finds the card");
        var add = cb.AddCard(target, null, null);
        yield return PlayModeHarness.WaitFrames(10);
        string chara = add?.NewIds.FirstOrDefault();
        c.Check(add != null && add.Ok && ui.Selection.Primary == chara && c.UndoLabel == OpLabels.AddCharacter, "add card → character selected, entry " + c.UndoLabel);
        if (chara != null)
        {
            string before = ui.Model.Find(chara).Label;
            var male = new CardEntry { FullPath = Path.GetFullPath(c.MaleCard), FileName = Path.GetFileNameWithoutExtension(c.MaleCard) };
            var r = cb.ChangeTo(male);
            yield return PlayModeHarness.WaitFrames(10);
            c.Check(r != null && r.Ok && ui.Model.Find(chara) != null && c.UndoLabel == OpLabels.ChangeCharacter, "change character to the male card (any sex, U27), entry " + c.UndoLabel + "; label " + before + " → " + ui.Model.Find(chara)?.Label);
            ui.Session.Commands.Undo();
            yield return PlayModeHarness.WaitFrames(5);
            c.Check(ui.Model.Find(chara)?.Label == before, "undo change character");
        }
        cb.UseListMode(true);
        yield return null;
        c.Check(cb.Q<MultiColumnListView>("chara-list")?.resolvedStyle.display == DisplayStyle.Flex, "list mode");
        cb.UseListMode(false);
        cb.Index.SetQuery("");
        cb.Refresh();
    }

    // ---------------------------------------------------------------- D: drags

    static IEnumerator DragGroup(Ctx c)
    {
        var ui = c.Ui;
        yield return NewScene(c);
        var ws = Panel<WorkspacePanel>(c, WorkspacePanel.PanelId) ?? WorkspacePanel.Current;
        var vp = ui.Viewport?.Element;
        c.Check(vp != null && AddModule.Drop != null, "viewport drop target");
        var row = c.App.Db.Items.Get(StudioDb.FallbackItem);
        var payload = new AddPayload(row?.Name ?? "item", OpLabels.AddItem, (p, pt) => ListBrowsers.AddItem(ui, row, p, pt));
        int entries = ui.Session.Commands.UndoList.Count;
        var r = AddModule.Drop.AddAt(payload, vp.worldBound.center);
        string id = r?.NewIds.FirstOrDefault();
        c.Check(r != null && r.Ok && id != null && ui.Session.Commands.UndoList.Count == entries + 1, "drop on the viewport adds in one entry (" + c.UndoLabel + ")");
        c.Info("drop point: " + ui.Model.Find(id)?.Doc.Transform.Pos);
        string outcome = DragController.SimulateDrop(payload, vp.worldBound.center);
        c.Check(outcome == "dropped" && ui.Model.Count == 2, "drag controller drop on the viewport (" + outcome + ")");
        ui.Registry.Run(ActionIds.AddFolder, "test");
        string folder = ui.Selection.Primary;
        ws.RebuildNow();
        ws.DropAt(payload, folder, null, -1);
        c.Check(ui.Model.Find(folder).Children.Count == 1, "browser row dropped on a folder row → added as its child");
        c.Check(DragController.SimulateDrop(new NodesPayload(new List<string> { folder }, "f"), new Vector2(-50, -50)) == "no target", "a drop outside every target does nothing");
    }

    // ---------------------------------------------------------------- F: files

    static IEnumerator FilesGroup(Ctx c)
    {
        var ui = c.Ui;
        var files = SceneFiles.Current;
        c.Check(files != null, "scene files controller");
        if (files == null) yield break;
        yield return NewScene(c);
        ui.Registry.Run(ActionIds.AddFolder, "test");
        ui.Ops.AddItem(StudioDb.FallbackItem, null, null, -1, User);
        var ids = ui.Model.PreOrder().Select(n => n.Id).ToList();
        object res;
        ui.Registry.RunWith(ActionIds.FileSaveAs, new ActionInvocation { Source = "test", Args = new Dictionary<string, object> { ["name"] = "c_test_scene", ["title"] = "C test" } }, out res);
        string path = res as string;
        c.Check(path != null && File.Exists(path) && files.CurrentPath == path && !files.IsDirty, "file.saveAs {name} writes the file (" + res + ")");
        c.Check(ui.Model.Title == "C test", "title stored");
        ui.Registry.RunWith(ActionIds.FileSaveAs, new ActionInvocation { Source = "test", Args = new Dictionary<string, object> { ["name"] = "c_test_scene" } }, out res);
        c.Check(path != null && (res as string) == path, "saving to the current file again is allowed");
        c.Check(files.Recent.Files.Count > 0 && string.Equals(files.Recent.Files[0], path, StringComparison.OrdinalIgnoreCase), "recent files updated");
        ui.Registry.Run(ActionIds.AddFolder, "test");
        c.Check(files.IsDirty, "dirty after a change");
        ui.Registry.Run(ActionIds.FileNew, "test");
        c.Check(ui.Dialogs.IsModalOpen && ui.Dialogs.Current.Spec.Id == "unsaved", "new on a dirty scene asks (unsaved)");
        ui.Dialogs.Press("discard");
        yield return null;
        c.Check(ui.Model.Count == 0 && files.LastOutcome == "new" && files.CurrentPath == null, "Don't save → new scene");
        files.Load(path);
        yield return PlayModeHarness.WaitFrames(5);
        c.Check(ui.Model.Count == ids.Count && ids.All(x => ui.Model.Find(x) != null) && files.CurrentPath == path, "open → the saved nodes (" + ui.Model.Count + ")");
        var imp = files.Import(path);
        c.Check(imp != null && imp.Ok && ui.Model.Count == 2 * ids.Count && c.UndoLabel == OpLabels.ImportScene && ui.Selection.Count == imp.NewIds.Count, "import merges with new ids, one entry " + c.UndoLabel);
        ui.Session.Commands.Undo();
        c.Check(ui.Model.Count == ids.Count, "undo import");
        // Ctrl+S on a titled scene overwrites (both presets, U17)
        long t0 = File.GetLastWriteTimeUtc(path).Ticks;
        ui.Registry.Run(ActionIds.AddFolder, "test");
        ui.Registry.Run(ActionIds.FileSave, "test");
        c.Check(File.GetLastWriteTimeUtc(path).Ticks >= t0 && !files.IsDirty, "file.save overwrites the current file");
        // scene browser
        ui.Registry.Run(ActionIds.FileOpen, "test");
        var sb = SceneBrowser.Open;
        c.Check(sb != null && ui.Dialogs.Current?.Spec.Id == SceneBrowser.DialogId, "file.open shows the scene browser");
        if (sb != null)
        {
            var ok = new bool[1];
            yield return WaitFor(() => { sb.Pump(); return sb.Ready; }, 20, ok);
            c.Check(ok[0] && sb.Index.Rows.Any(e => string.Equals(e.FullPath, path, StringComparison.OrdinalIgnoreCase) && e.Title == "C test"), "scene browser lists the file with its title (" + sb.Index?.Rows.Count + ")");
            sb.Press("close");
        }
        c.Check(files.RecentMenu().Count >= 1, "File ▸ Recent items");
        // untitled + hs2studio: Ctrl+S saves straight to the timestamped name (U4)
        yield return NewScene(c);
        ui.Registry.Run(ActionIds.AddFolder, "test");
        ui.Options.Set(OptionDefs.KeyPreset, Keymap.Hs2StudioPreset);
        yield return PlayModeHarness.WaitFrames(2);
        ui.Registry.Run(ActionIds.FileSave, "test");
        c.Check(files.CurrentPath != null && File.Exists(files.CurrentPath) && !ui.Dialogs.IsModalOpen, "hs2studio: untitled Ctrl+S → " + Path.GetFileName(files.CurrentPath ?? ""));
        ui.Options.Set(OptionDefs.KeyPreset, Keymap.DefaultPreset);
        yield return NewScene(c);
        ui.Registry.Run(ActionIds.AddFolder, "test");
        ui.Registry.Run(ActionIds.FileSave, "test");
        c.Check(ui.Dialogs.IsModalOpen && ui.Dialogs.Current.Spec.Id == "saveAs", "default: untitled Ctrl+S opens Save As");
        ui.Dialogs.Press("cancel");
    }

    // ---------------------------------------------------------------- O: options, key overlay

    static IEnumerator OptionsGroup(Ctx c)
    {
        var ui = c.Ui;
        ui.Registry.Run(ActionIds.FileOptions, "test");
        var op = OptionsPanel.Open;
        c.Check(op != null && ui.Dialogs.Current?.Spec.Id == OptionsPanel.DialogId, "file.options shows the options");
        if (op != null)
        {
            c.Check(op.Controls.Count == OptionDefs.All.Count, "one control per option (" + op.Controls.Count + " / " + OptionDefs.All.Count + ")");
            var t = op.Controls[OptionDefs.ConfirmDelete] as Toggle;
            t.value = true;
            c.Check(ui.Options.Bool(OptionDefs.ConfirmDelete), "toggle writes the option");
            ui.Options.Set(OptionDefs.ConfirmDelete, false);
            yield return null;
            c.Check(t.value == false, "option change refreshes the control");
            ui.Dialogs.Press("close");
        }
        yield return null;
        ui.Registry.Run(ActionIds.HelpKeys, "test");
        var ko = KeyOverlay.Open;
        c.Check(ko != null && ko.RowCount == ActionManifest.All.Count, "help.keys lists every action (" + ko?.RowCount + ")");
        if (ko != null)
        {
            string id = ActionIds.ViewFps;
            string before = ui.Keys.ChordText(id);
            ko.StartRecording(id);
            using (var e = KeyDownEvent.GetPooled('\0', KeyCode.F9, EventModifiers.None)) { e.target = ko; ko.SendEvent(e); }
            yield return null;
            c.Check(ui.Keys.Map.ChordsOf(id).Any(ch => ch.ToString() == "F9") && ui.Keys.Overrides.Bindings.ContainsKey(id), "record F9 for view.fps → keymap.json override");
            c.Check(File.Exists(ui.Keys.FilePath), "keymap.json written");
            ko.ResetToPreset(id);
            c.Check(ui.Keys.ChordText(id) == before && !ui.Keys.Overrides.Bindings.ContainsKey(id), "reset to preset");
            ui.Dialogs.Press("close");
        }
    }

    // ---------------------------------------------------------------- P: poses (HS2Next file only)

    static IEnumerator PoseGroup(Ctx c)
    {
        var ui = c.Ui;
        yield return NewScene(c);
        var ch = ui.Ops.AddChara(c.FemaleCard, null, null, -1, null, User);
        string chara = ch.NewIds.FirstOrDefault();
        yield return PlayModeHarness.WaitFrames(10);
        var pb = new PoseBrowser(ui) { UseAppFolders = false, OwnDir = c.App.Paths.PoseDir, Hs2Dir = null };
        string err = pb.Save(chara, "c_test_pose", true);
        string file = Path.Combine(c.App.Paths.PoseDir, "c_test_pose.hs2npose");
        c.Check(err == null && File.Exists(file), "save pose (.hs2npose) " + (err ?? ""));
        c.Check(pb.Save(chara, "c_test_pose", false) == "exists", "existing file needs overwrite");
        pb.StartScan();
        var ok = new bool[1];
        yield return WaitFor(() => { pb.Pump(); return pb.Ready; }, 20, ok);
        var entry = pb.Files?.Entries.FirstOrDefault(e => e.IsHs2n);
        c.Check(ok[0] && entry != null && entry.CanDelete, "pose browser lists the HS2Next pose (deletable)");
        if (entry != null)
        {
            var r = pb.Load(entry, chara);
            c.Check(r != null && r.Ok && c.UndoLabel == OpLabels.LoadPose, "load pose on the character, entry " + c.UndoLabel);
        }
        try { File.Delete(file); } catch (Exception) { }
    }
}
