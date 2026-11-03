// UiListsV12Test.play (v1.2 package L): the browsers' favourites, «最近追加» (recently added),
// right-click menu and delete, in Editor play with the UI (PlayModeHarness; -nographics works: no pixel is read). Every file the
// test lists, marks or deletes lies in a sandbox folder (-sandbox <dir>, default Spike/out/ui_lists_v12/sandbox): copies of a few
// pose files of the HS2 install and of the repository's pose fixture, synthetic card files; the app's UserData (favorites.json)
// is the sandbox's userdata folder; BrowserFiles.TestSandbox refuses any delete outside the sandbox, so a real file is never
// touched. A file deleted by the test goes to the Windows recycle bin; at the start and at the end of the run (in a finally, so
// also after a failed group) the test takes out of the recycle bin exactly the entries deleted from under its sandbox root
// (UiListsV12Test_Recycle.cs, through the shell's recycle-bin folder) and checks by count that none is left and that every
// other entry is still there; group Z reports the numbers.
// Groups (in this order; -only A+B picks some):
//   FS the favourites store alone: toggle, the file after a reload, unknown members kept, the unreadable file kept as .bak (once)
//   RB delete refusals: the recycle bin unavailable (injected) and a path outside the sandbox leave the file
//   P  the pose browser: the views above the folders and their counts, a right click / the menu key / a list row's right click
//      open the menu, the favourite toggle (star, the ★ view, the file), «最近追加» newest first by creation time whatever the
//      sort, details, delete to the recycle bin (the entry leaves in place, the neighbour selected, scroll kept), a refused delete
//   M6, CB (UiListsV12Test_Keep.cs) owner item 6: the Costume tab's «読込» keeps the list; the character browser keeps its own
//   C, L, K (UiListsV12Test_Lists.cs) the character browser, the list browsers (with the view timings), the coordinate dialog
//   W  (UiListsV12Test_Lists.cs) the character browser's status line and the list headers at 372 / 300 / 247 px in ja and en
// Cards and pose images are never looked at; the checks read element state and numbers.
// Run: & Tools\unity_batch.ps1 -Method UiListsV12Test.Run -NoQuit -NoGraphics [-MethodArgs @('-only','P','-sandbox','<dir>')]
// Report: Spike/out/ui/ui_lists_v12_report.txt ("SUMMARY: N PASS, M FAIL").
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.UI;
using HS2Next.UI.Bridge;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using HS2Next.UI.Widgets;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

public static partial class UiListsV12Test
{
    public const int Port = 17651;
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "ui");
    static string ReportPath
    {
        get
        {
            string only = Arg("-only");
            return Path.Combine(OutDir, "ui_lists_v12_report" + (only != null ? "_" + only : "") + ".txt");
        }
    }

    static string Arg(string name)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    /// <summary>The sandbox folder (the -sandbox argument, else Spike/out/ui_lists_v12/sandbox in the project).</summary>
    static string Sandbox => Path.GetFullPath(Arg("-sandbox") ?? Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "ui_lists_v12", "sandbox"));
    static string UserDataDir => Path.Combine(Sandbox, "userdata");

    static string Launch =>
        "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},\"userData\":\"" + UserDataDir.Replace('\\', '/') +
        "\",\"bridge\":{\"port\":" + Port + ",\"outputDir\":\"Spike/out/ui_lists_v12/out\",\"allowQuit\":false}}";

    public static void Run()
    {
        Directory.CreateDirectory(OutDir);
        // a fresh sandbox each run (only the test's own folder; it holds no link: the test makes plain folders and copies)
        try { if (Directory.Exists(Sandbox)) Directory.Delete(Sandbox, true); } catch (Exception e) { Debug.LogWarning("sandbox not cleared: " + e.Message); }
        Directory.CreateDirectory(Path.Combine(UserDataDir, "config"));
        File.WriteAllText(ReportPath, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("UiListsV12Test.Play", ReportPath, 25, true, Launch);
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
        public bool Want(string g) => Only == null || Only.Contains(g);
        public void Check(bool ok, string what)
        {
            if (ok) Pass++; else Fail++;
            string l = (ok ? "PASS " : "FAIL ") + Group + " " + what;
            Rep.AppendLine(l);
            Debug.Log("UI LISTS " + l);
        }
        public void Warning(string what) { Warn++; Rep.AppendLine("WARN " + Group + " " + what); Debug.Log("UI LISTS WARN " + Group + " " + what); }
        public void Info(string what) { Rep.AppendLine("INFO " + Group + " " + what); Debug.Log("UI LISTS INFO " + Group + " " + what); }
    }

    static readonly List<(string id, Func<Ctx, IEnumerator> run)> Groups = new List<(string, Func<Ctx, IEnumerator>)>();

    static UiListsV12Test()
    {
        Groups.Add(("FS", StoreGroup));
        Groups.Add(("RB", RefuseGroup));
        Groups.Add(("P", PoseGroup));
        Groups.Add(("M6", CostumeGroup));     // UiListsV12Test_Keep.cs
        Groups.Add(("CB", CharaKeepGroup));
        Groups.Add(("C", CharaGroup));        // UiListsV12Test_Lists.cs
        Groups.Add(("L", ListsGroup));
        Groups.Add(("K", CoordDialogGroup));
        Groups.Add(("W", WidthGroup));
    }

    static IEnumerator Play()
    {
        var c = new Ctx();
        string only = Arg("-only");
        if (only != null) c.Only = new HashSet<string>(only.Split('+'));
        c.Rep.AppendLine($"UiListsV12Test.play (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, screen {Screen.width}x{Screen.height}, sandbox {Sandbox})");
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ok);
        c.App = AppCore.Current;
        c.Group = "H";
        c.Check(ok[0] && c.App.Stage == AppStage.Ready, "app ready (" + (c.App?.Stage.ToString() ?? "none") + (c.App?.Error != null ? ": " + c.App.Error : "") + ")");
        yield return PlayModeHarness.WaitUntil(() => UiHost.Current != null && UiHost.Current.IsReady, 30, ok);
        c.Host = UiHost.Current;
        c.Check(ok[0], "UiHost ready");
        if (!ok[0]) { Finish(c); yield break; }
        c.Check(string.Equals(Path.GetFullPath(c.App.Paths.UserData), Path.GetFullPath(UserDataDir), StringComparison.OrdinalIgnoreCase), "UserData is the sandbox's (" + c.App.Paths.UserData + ")");
        // every delete of this run is refused outside the sandbox
        BrowserFiles.TestSandbox = Sandbox;
        ListMenus.TestReveal = p => revealed.Add(p);
        // the recycle bin: entries left by an earlier run of this sandbox go first; the others are remembered to check them at the end
        c.Group = "Z";
        var others = BinStart(c);
        yield return PlayModeHarness.WaitFrames(5);
        try
        {
        foreach (var g in Groups)
        {
            if (!c.Want(g.id)) continue;
            c.Group = g.id;
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
            CloseAll(c);
            RecycleBin.TestUnavailable = null;
            ZipmodDates.TestProvider = null;
        }
        }
        finally
        {
            BrowserFiles.TestSandbox = null;
            ListMenus.TestReveal = null;
            RecycleBin.TestUnavailable = null;
            ZipmodDates.TestProvider = null;
            c.Group = "Z";
            c.Info("deleted to the recycle bin by this run: " + recycled.Count + (recycled.Count > 0 ? " (" + string.Join(" | ", recycled) + ")" : ""));
            BinEnd(c, others);
        }
        Finish(c);
    }

    /// <summary>The run's start: takes out the recycle-bin entries an earlier run left from this sandbox; returns the other entries.</summary>
    static HashSet<string> BinStart(Ctx c)
    {
        try
        {
            var probs = new List<string>();
            var r = RecycleBinSandboxPurge.Purge(Sandbox, probs);
            if (r.before > 0) c.Info($"recycle bin at the start: {r.before} entries of an earlier run from the sandbox, {r.removed} removed, {r.left} left");
            var others = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in RecycleBinSandboxPurge.List()) if (e.BinFile != null) others.Add(e.BinFile);
            c.Info($"recycle bin at the start: {others.Count} other entries (left alone)");
            return others;
        }
        catch (Exception e) { c.Check(false, "the recycle bin could not be read at the start: " + e.Message); return null; }
    }

    /// <summary>The run's end: takes out exactly the entries this run put in the recycle bin from the sandbox, checks by count that
    /// none is left and that every other entry of the start is still there.</summary>
    static void BinEnd(Ctx c, HashSet<string> others)
    {
        try
        {
            var probs = new List<string>();
            var r = RecycleBinSandboxPurge.Purge(Sandbox, probs);
            foreach (var p in probs) c.Info("recycle bin: " + p);
            var now = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in RecycleBinSandboxPurge.List()) if (e.BinFile != null) now.Add(e.BinFile);
            int kept = others == null ? -1 : others.Count(o => now.Contains(o));
            c.Check(r.before == recycled.Count && r.removed == r.before && r.left == 0,
                $"recycle bin at the end: {r.before} entries from the sandbox (this run deleted {recycled.Count}), {r.removed} removed, {r.left} left");
            c.Check(others != null && kept == others.Count, $"recycle bin at the end: every other entry is still there ({kept} of {others?.Count})");
        }
        catch (Exception e) { c.Check(false, "the recycle bin could not be cleaned: " + e.Message); }
    }

    static readonly List<string> revealed = new List<string>();
    static readonly List<string> recycled = new List<string>();

    static void CloseAll(Ctx c)
    {
        c.Ui.Shell?.Menus?.CloseAll();
        while (SheetDialog.Current != null) SheetDialog.Current.Press("cancel");
        while (c.Ui.Dialogs != null && c.Ui.Dialogs.IsModalOpen) c.Ui.Dialogs.CloseCurrent();
    }

    static void Finish(Ctx c)
    {
        c.Rep.Insert(0, $"SUMMARY: {c.Pass} PASS, {c.Fail} FAIL, {c.Warn} WARN\n");
        File.WriteAllText(ReportPath, c.Rep.ToString(), new UTF8Encoding(false));
        Debug.Log($"UI LISTS TEST DONE: {c.Pass} PASS, {c.Fail} FAIL");
        PlayModeHarness.ExitCode = c.Fail > 0 ? 1 : 0;
    }

    // ---------------------------------------------------------------- helpers

    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return PlayModeHarness.WaitFrames(1); }

    static void Ev(Ctx c, object o) => UiCommands.Send(c.Host, JObject.FromObject(o), out _);

    /// <summary>A synthetic click (button 0 left, 1 right) at an element's centre (scroll it into view first).</summary>
    static void Click(Ctx c, VisualElement el, int button = 0, int clicks = 1)
    {
        var p = el.worldBound.center;
        Ev(c, new { type = "pointer", x = p.x, y = p.y, button, clicks });
    }

    static VisualElement Q(Ctx c, string name) => c.Ui.Shell.Root.Q(name);

    /// <summary>Clicks a button through the panel (synthetic pointer events at its centre; it is scrolled into view first when
    /// it lies in a scroll view).</summary>
    static void Press(Ctx c, Button b)
    {
        if (b == null) return;
        b.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(b);
        Click(c, b);
    }

    /// <summary>A test host for a browser: an absolute box in the UI root (the browser lays out and gets synthetic events).</summary>
    static VisualElement Host(Ctx c, VisualElement content, float w = 700, float h = 560)
    {
        var root = c.Ui.Shell.Root;
        float rw = root.layout.width, rh = root.layout.height;
        if (!float.IsNaN(rw) && rw > 100) w = Mathf.Min(w, rw - 40);
        if (!float.IsNaN(rh) && rh > 100) h = Mathf.Min(h, rh - 40);
        var box = new VisualElement { name = "v12-test-host" };
        box.style.position = Position.Absolute;
        box.style.left = 20; box.style.top = 20; box.style.width = w; box.style.height = h;
        box.style.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 1f);
        content.style.flexGrow = 1;
        box.Add(content);
        root.Add(box);
        // the menus' overlay and the dialogs stay above the test's box, as above the regions
        c.Ui.Shell.Overlay.BringToFront();
        c.Ui.Shell.DialogLayer.BringToFront();
        c.Info("test host " + w + " x " + h + " in a root of " + rw + " x " + rh);
        return box;
    }

    static string Sub(params string[] parts) => Path.Combine(new[] { Sandbox }.Concat(parts).ToArray());

    /// <summary>Copies <paramref name="n"/> HS2 pose files (.dat, the first folder of the install holding that many, sorted) into
    /// <paramref name="dir"/> (read only at the install); returns the copies.</summary>
    static List<string> CopyPoses(Ctx c, string dir, int n)
    {
        var res = new List<string>();
        string src = c.App.Paths.Hs2PoseDir;
        if (src == null || !Directory.Exists(src)) return res;
        foreach (var d in new[] { src }.Concat(Directory.GetDirectories(src, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal)))
        {
            var files = Directory.GetFiles(d, "*.dat").OrderBy(x => x, StringComparer.Ordinal).Take(n).ToList();
            if (files.Count < n) continue;
            Directory.CreateDirectory(dir);
            foreach (var f in files) { string t = Path.Combine(dir, Path.GetFileName(f)); File.Copy(f, t, true); res.Add(t); }
            break;
        }
        return res;
    }

    // ---------------------------------------------------------------- FS: the store alone

    static IEnumerator StoreGroup(Ctx c)
    {
        string dir = Sub("store");
        Directory.CreateDirectory(dir);
        string f = Path.Combine(dir, FavoriteStore.FileName);
        var s = FavoriteStore.Load(f);
        c.Check(s.Problem == null && s.RowsOf(FavoriteStore.Items).Count == 0, "a missing file: an empty store, no problem");
        bool a = s.SetRow(FavoriteStore.Items, "illusion.hs2#12", true), b = s.SetRow(FavoriteStore.Items, "illusion.hs2#12", true);
        s.SetFile("hs2.chara.female", "sub\\A.png", true);
        c.Check(a && !b && s.Writes == 2 && File.Exists(f), "toggle on: changed once, written at once (" + s.Writes + " writes)");
        c.Check(s.IsFile("hs2.chara.female", "SUB/a.PNG") && !s.IsFile("hs2.chara.male", "sub/A.png"), "files: path without case, '/' or '\\\\', per root");
        var o = JObject.Parse(File.ReadAllText(f));
        o["future"] = new JObject { ["x"] = 1 };
        File.WriteAllText(f, o.ToString());
        var s2 = FavoriteStore.Load(f);
        c.Check(s2.IsRow(FavoriteStore.Items, "illusion.hs2#12") && s2.IsFile("hs2.chara.female", "sub/A.png") && s2.Problem == null, "reloaded: the favourites are back");
        s2.SetRow(FavoriteStore.Items, "illusion.hs2#12", false);
        var s3 = FavoriteStore.Load(f);
        c.Check(!s3.IsRow(FavoriteStore.Items, "illusion.hs2#12") && JObject.Parse(File.ReadAllText(f))["future"] != null, "toggle off persists; an unknown member is kept");
        c.Check(Directory.GetFiles(dir, "*.tmp").Length == 0, "no temporary file left");
        File.WriteAllText(f, "{ not json");
        var s4 = FavoriteStore.Load(f);
        c.Check(s4.Problem != null && s4.BackupPath != null && File.Exists(f + ".bak") && !File.Exists(f) && s4.RowsOf(FavoriteStore.Items).Count == 0,
            "an unreadable file: kept as .bak, the store starts empty, the problem said (" + s4.Problem + ")");
        s4.SetRow(FavoriteStore.Maps, "illusion.hs2#1", true);
        c.Check(File.Exists(f) && FavoriteStore.Load(f).IsRow(FavoriteStore.Maps, "illusion.hs2#1") && File.ReadAllText(f + ".bak") == "{ not json", "after it: a new file; the .bak keeps the old text");
        // the app's store: an unreadable favorites.json in the UserData config folder is reported once as a toast
        string appFile = Path.Combine(c.Ui.Options.ConfigDir, FavoriteStore.FileName);
        File.WriteAllText(appFile, "[1,2");
        int before = c.Ui.Toasts.History.Count;
        Favorites.Reload();
        Favorites.Of(c.Ui);
        Favorites.Of(c.Ui);
        int toasts = c.Ui.Toasts.History.Skip(before).Count(t => t.Contains(FavoriteStore.FileName + ".bak"));
        c.Check(toasts == 1 && File.Exists(appFile + ".bak"), "the app's unreadable favourites: one toast naming the .bak (" + toasts + ")");
        try { File.Delete(appFile + ".bak"); } catch (Exception) { }
        Favorites.Reload();
        yield break;
    }

    // ---------------------------------------------------------------- RB: refusals

    static IEnumerator RefuseGroup(Ctx c)
    {
        string dir = Sub("refuse");
        Directory.CreateDirectory(dir);
        string f = Path.Combine(dir, "keep.png");
        File.WriteAllBytes(f, new byte[] { 1, 2, 3 });
        var bf = new BrowserFile { FullPath = f, Kind = BrowserFileKind.Chara, RootId = "hs2next.chara", RelPath = "keep.png" };
        RecycleBin.TestUnavailable = "test: no recycle bin";
        int before = c.Ui.Toasts.History.Count;
        string err = BrowserFiles.Delete(c.Ui, bf);
        RecycleBin.TestUnavailable = null;
        c.Check(err != null && File.Exists(f) && c.Ui.Toasts.History.Skip(before).Any(t => t.Contains("keep.png")), "recycle bin unavailable: refused, the file stays, a toast (" + err + ")");
        string outside = Path.Combine(Path.GetDirectoryName(Sandbox), "v12_not_in_sandbox.png");
        err = BrowserFiles.Delete(c.Ui, new BrowserFile { FullPath = outside, Kind = BrowserFileKind.Chara, RootId = "x", RelPath = "x" });
        c.Check(err == "outside the test sandbox", "a path outside the sandbox: refused before anything (" + err + ")");
        c.Check(RecycleBin.Unavailable(@"\\server\share\x.png") != null, "a network path has no recycle bin");
        yield break;
    }

    // ---------------------------------------------------------------- P: the pose browser

    static IEnumerator PoseGroup(Ctx c)
    {
        var ui = c.Ui;
        string hs2 = Sub("pose_hs2"), own = Sub("pose_own");
        var copies = CopyPoses(c, Path.Combine(hs2, "a"), 4).Concat(CopyPoses(c, Path.Combine(hs2, "b"), 2)).ToList();
        Directory.CreateDirectory(own);
        string fixture = Path.Combine(ContentSettings.ProjectRoot, "Tests", "SaveFormat", "fixtures", "v02_dev1", "pose.hs2npose");
        string ownFile = Path.Combine(own, "fixture_pose.hs2npose");
        if (File.Exists(fixture)) File.Copy(fixture, ownFile, true);
        c.Check(copies.Count == 6 && File.Exists(ownFile), "sandbox poses: " + copies.Count + " HS2 copies + the fixture");
        if (copies.Count < 6) yield break;
        // creation times: newest = b's second file, then the own file, … (set on the sandbox copies only)
        var all = copies.Concat(new[] { ownFile }).ToList();
        var t0 = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        int[] order = { 3, 5, 0, 6, 2, 1, 4 };   // rank of each file: 0 = oldest
        for (int i = 0; i < all.Count; i++) File.SetCreationTimeUtc(all[i], t0.AddHours(order[i]));
        var pb = new PoseBrowser(ui) { UseAppFolders = false, Hs2Dir = hs2, OwnDir = own, IndexFile = "" };
        var host = Host(c, pb);
        pb.StartScan();
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => { pb.Pump(); return pb.Complete; }, 30, ok);
        yield return Frames(4);
        c.Check(ok[0] && pb.Index.Indices.Count == 7, "listed 7 poses (" + pb.Index?.Indices.Count + ")");
        c.Check(pb.Folders.Views.SequenceEqual(new[] { BrowserViews.Favorites, BrowserViews.Recent }), "the folder tree starts with «★» and «最近追加»");
        var favRow = pb.Q("folder-view-favorites") as Label;
        var recRow = pb.Q("folder-view-recent") as Label;
        c.Check(favRow != null && recRow != null && favRow.text.Contains("(0)") && recRow.text.Contains("(7)") && !string.IsNullOrEmpty(recRow.tooltip),
            "view rows with counts: '" + favRow?.text + "', '" + recRow?.text + "', tooltip '" + recRow?.tooltip + "'");

        // right click on a grid cell → the menu; its «Favourite» item → the store, the star, the ★ view
        pb.Grid.ScrollToPos(2);
        yield return Frames(3);
        var cell = pb.Grid.CellAt(2);
        c.Check(cell != null, "grid cell 2 bound");
        if (cell == null) { host.RemoveFromHierarchy(); yield break; }
        var entry2 = pb.Index.Rows[pb.Index.Indices[2]];
        Click(c, cell, 1);
        yield return Frames(3);
        var favItem = Q(c, "menu-item-list-favorite");
        c.Check(ListMenus.LastMenuName == "pose-context" && favItem != null && pb.Grid.SelectedPos == 2 && Q(c, "menu-item-list-delete") != null,
            "right click: the cell selected, the menu with favourite / details / reveal / copy / delete (" + string.Join(",", ListMenus.LastItems?.Where(i => i.Id != null).Select(i => i.Id) ?? new string[0]) + ")");
        if (favItem != null) Click(c, favItem);
        yield return Frames(3);
        var store = Favorites.Of(ui);
        c.Check(BrowserFiles.IsFavorite(store, entry2) && File.ReadAllText(Path.Combine(ui.Options.ConfigDir, FavoriteStore.FileName)).Contains(Path.GetFileName(entry2.FullPath)),
            "«Favourite» marks the file (favorites.json names it)");
        var cellText = pb.Grid.CellAt(2)?.Q<Label>(className: "hs-card__text")?.text;
        c.Check(cellText != null && cellText.StartsWith(BrowserViews.Star), "the cell shows the star ('" + cellText + "')");
        // the menu key on the focused grid opens the menu of the focus cell
        pb.Grid.Focus();
        pb.Grid.Select(4);
        yield return Frames(2);
        ui.Shell.Menus.CloseAll();
        Ev(c, new { type = "key", key = "Menu" });
        yield return Frames(2);
        bool menuKey = ui.Shell.Menus.IsOpen && ListMenus.LastMenuName == "pose-context";
        ui.Shell.Menus.CloseAll();
        Ev(c, new { type = "key", key = "F10", mods = new[] { "shift" } });
        yield return Frames(2);
        c.Check(menuKey && ui.Shell.Menus.IsOpen, "the menu key and Shift+F10 open the focus cell's menu");
        ui.Shell.Menus.CloseAll();
        // a second favourite through the menu, then the ★ view
        ListMenus.ShowFile(ui, BrowserFiles.Of(pb.Index.Rows[pb.Index.Indices[5]]), Vector2.one * 100, "pose-context");
        ListMenus.Run("list-favorite");
        yield return Frames(2);
        pb.ShowView(BrowserViews.Favorites);
        yield return Frames(3);
        var favShown = pb.Index.Indices.Select(i => pb.Index.Rows[i]).ToList();
        c.Check(pb.View == BrowserViews.Favorites && favShown.Count == 2 && favShown.All(e => BrowserFiles.IsFavorite(store, e)) && favRow.text.Contains("(2)"),
            "the ★ view lists the 2 favourites of every folder (" + favShown.Count + ", row '" + favRow.text + "')");
        // a reload of the store keeps them
        Favorites.Reload();
        store = Favorites.Of(ui);
        yield return Frames(2);
        c.Check(pb.Index.Indices.Count == 2 && BrowserFiles.IsFavorite(store, entry2), "after reloading the store from the file: the same 2");
        // «最近追加»: every file, newest creation time first, whatever the sort
        pb.ShowView(BrowserViews.Recent);
        yield return Frames(3);
        var rec = pb.Index.Indices.Select(i => pb.Index.Rows[i]).ToList();
        bool newest = rec.Count == 7;
        for (int i = 1; i < rec.Count; i++) if (rec[i - 1].CreatedUtc < rec[i].CreatedUtc) newest = false;
        string expectFirst = all[Array.IndexOf(order, 6)];
        c.Check(newest && string.Equals(rec[0].FullPath, expectFirst, StringComparison.OrdinalIgnoreCase), "«最近追加»: 7 files newest first (" + string.Join(", ", rec.Select(e => order[all.FindIndex(p => string.Equals(p, e.FullPath, StringComparison.OrdinalIgnoreCase))])) + ")");
        pb.ShowView(null);
        yield return Frames(2);
        c.Check(pb.View == null && pb.Index.Indices.Count == 7 && pb.Index.SortName == null, "back to all: folder order again");
        // the list mode: a right click on a row selects it and opens the menu
        Press(c, pb.Q<Button>("pose-list-mode"));
        yield return Frames(4);
        VisualElement rowEl = null;
        try { rowEl = pb.List.style.display == DisplayStyle.Flex ? pb.List.GetRootElementForIndex(1) : null; } catch (NullReferenceException) { }
        var rowCell = rowEl?.Q<Label>(className: "hs-list-cell");
        if (rowCell != null) Click(c, rowCell, 1);
        yield return Frames(3);
        c.Check(rowCell != null && pb.List.selectedIndex == 1 && ui.Shell.Menus.IsOpen && ListMenus.LastMenuName == "pose-context", "list mode: a row's right click selects it and opens the menu");
        ui.Shell.Menus.CloseAll();
        Label nameCell = null;
        try { nameCell = pb.List.GetRootElementForIndex(2)?.Q<Label>(className: "hs-list-cell"); } catch (NullReferenceException) { }
        c.Check(nameCell != null && nameCell.text.StartsWith(BrowserViews.Star) == BrowserFiles.IsFavorite(store, pb.Index.Rows[pb.Index.Indices[2]]), "list mode: a favourite's row shows the star ('" + nameCell?.text + "')");
        Press(c, pb.Q<Button>("pose-grid-mode"));
        yield return Frames(3);
        // details
        var e3 = pb.Index.Rows[pb.Index.Indices[3]];
        ListMenus.ShowFile(ui, BrowserFiles.Of(e3), Vector2.one * 100, "pose-context");
        ListMenus.Run("list-details");
        yield return Frames(3);
        string det = ListMenus.LastDetails ?? "";
        c.Check(ui.Dialogs.IsModalOpen && ui.Dialogs.Current.Spec.Id == "list-details" && det.Contains(Path.GetFileName(e3.FullPath)) && det.Contains(e3.FullPath) && det.Contains("HS2 pose file"),
            "details: a dialog with the file name, the path and the kind");
        var lbl = ui.Dialogs.Current?.Element?.Q<Label>("list-details-text");
        c.Check(lbl != null && lbl.selection.isSelectable, "details: the text is selectable");
        ListMenus.PressDialog(ui, "copy");
        c.Check(ListMenus.LastCopied == lbl?.text && ui.Dialogs.IsModalOpen, "«Copy all» copies the text and keeps the dialog");
        ListMenus.PressDialog(ui, "close");
        // the explorer and the copy items
        revealed.Clear();
        ListMenus.ShowFile(ui, BrowserFiles.Of(e3), Vector2.one * 100, "pose-context");
        ListMenus.Run("list-reveal");
        ListMenus.ShowFile(ui, BrowserFiles.Of(e3), Vector2.one * 100, "pose-context");
        ListMenus.Run("list-copy-path");
        c.Check(revealed.Count == 1 && revealed[0] == e3.FullPath && ListMenus.LastCopied == e3.FullPath, "«Show in Explorer» selects the file; «Copy path» copies it");
        // delete: entry 3 of 7 in grid mode, selected, scrolled
        pb.Grid.Select(3);
        yield return Frames(2);
        var before = pb.Index.Indices.Select(i => pb.Index.Rows[i].FullPath).ToList();
        string victim = before[3];
        var sv = pb.Grid.List.Q<ScrollView>();
        float offBefore = sv?.scrollOffset.y ?? -1;
        int deletes = BrowserFiles.DeletesDone, undoBefore = ui.Session.Commands.UndoList.Count;
        ListMenus.ShowFile(ui, BrowserFiles.Of(pb.Index.Rows[pb.Index.Indices[3]]), Vector2.one * 100, "pose-context");
        ListMenus.Run("list-delete");
        yield return Frames(2);
        string msg = ui.Dialogs.Current?.Spec.Message ?? "";
        c.Check(ui.Dialogs.IsModalOpen && ui.Dialogs.Current.Spec.Id == "list-delete" && msg.Contains(Path.GetFileName(victim)) && msg.Contains("HS2"),
            "delete asks first, naming the file and that it leaves HS2 ('" + msg.Replace("\n", " ") + "')");
        ListMenus.PressDialog(ui, "cancel");
        yield return Frames(2);
        c.Check(File.Exists(victim) && pb.Index.Indices.Count == 7, "cancel: nothing deleted");
        ListMenus.Run("list-delete");
        yield return Frames(2);
        int toastsBefore = ui.Toasts.History.Count;
        ListMenus.PressDialog(ui, "delete");
        yield return Frames(3);
        if (!File.Exists(victim)) recycled.Add(victim);
        var after = pb.Index.Indices.Select(i => pb.Index.Rows[i].FullPath).ToList();
        c.Check(!File.Exists(victim) && RecycleBin.LastRecycled && BrowserFiles.DeletesDone == deletes + 1, "the file went to the recycle bin (" + BrowserFiles.LastProblem + ")");
        c.Check(after.SequenceEqual(before.Where(p => p != victim)), "the entry left the list; the order of the rest is kept");
        c.Check(pb.Grid.SelectedPos == 3 && pb.Grid.Selected?.FullPath == before[4], "the neighbour is selected (" + pb.Grid.SelectedPos + ")");
        c.Check(sv == null || Math.Abs(sv.scrollOffset.y - offBefore) < 0.5f, "the scroll stays (" + offBefore + " → " + sv?.scrollOffset.y + ")");
        c.Check(ui.Toasts.History.Skip(toastsBefore).Any(t => t.Contains(Path.GetFileName(victim)) && t.Contains("recycle bin")), "the toast says it went to the recycle bin");
        c.Check(ui.Session.Commands.UndoList.Count == undoBefore, "no history entry (" + undoBefore + " → " + ui.Session.Commands.UndoList.Count + ")");
        // refused: the recycle bin unavailable → the file and the list stay
        RecycleBin.TestUnavailable = "test: no recycle bin";
        string keep = after[0];
        ListMenus.ShowFile(ui, BrowserFiles.Of(pb.Index.Rows[pb.Index.Indices[0]]), Vector2.one * 100, "pose-context");
        ListMenus.Run("list-delete");
        ListMenus.PressDialog(ui, "delete");
        yield return Frames(2);
        RecycleBin.TestUnavailable = null;
        c.Check(File.Exists(keep) && pb.Index.Indices.Count == 6 && BrowserFiles.LastProblem != null, "a refused delete leaves the file and the list (" + BrowserFiles.LastProblem + ")");
        // the footer's Delete goes through the same confirmation
        pb.Grid.Select(0);
        var del = pb.Q("pose-delete") as Button;
        c.Check(del != null && del.enabledSelf, "the footer Delete is enabled for an HS2 file");
        if (del != null) Click(c, del);
        yield return Frames(2);
        c.Check(ui.Dialogs.IsModalOpen && ui.Dialogs.Current.Spec.Id == "list-delete", "the footer Delete asks with the same dialog");
        ListMenus.PressDialog(ui, "cancel");
        c.Info("ui.state poses: " + (PoseBrowser.UiState() == null ? "n/a (the app's browser not shown)" : "ok"));
        host.RemoveFromHierarchy();
        yield return Frames(2);
    }
}
