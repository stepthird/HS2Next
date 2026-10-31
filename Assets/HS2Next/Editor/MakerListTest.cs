// Maker list tests in Unity (Unity part; package L).
//   Run    (headless, -nographics): the app's catalog (settings) → MakerCatalog.For. Writes the vanilla list fixture of the
//          plain-Mono suite, Cache/maker/vanilla_lists.tsv (every vanilla row in catalog order, the distributionNo-gated
//          ones marked: read with the AI-Shoujo flags on; P lines with the preset ids of every vanilla head and of the
//          sampled mod heads, decoded by FacePresetLoader), then runs the plain-Mono LS groups (MakerRulesTests,
//          MakerCensusTests, MakerPresetTests, MakerHs2Tests) over it, and checks that the lists over the catalog equal the lists over the
//          fixture (rows per category, the head of every skin row, the skin lists of every head) and FacePresetLoader's face
//          blocks (refs = HeadPresets' resolution). Report Spike/out/maker/maker_lists_report.txt.
//   Thumbs (graphics, play mode; -NoQuit): U7, PartThumbnails on the ft_skin_f lists of head 3 and of the mod head with the
//          most skins: first-view time per row and bundles opened, without the disk cache (cold bundles; PNGs written),
//          then with it (a fresh loader, the thumbnail-only bundles unloaded). The PNGs go to a scratch folder, not
//          Cache/thumbs. Numbers only (no image is looked at). Report Spike/out/maker/maker_thumbs_report.txt.
// Run: & Tools\unity_batch.ps1 -Method MakerListTest.Run -NoGraphics
//      & Tools\unity_batch.ps1 -Method MakerListTest.Thumbs -NoQuit
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Content;
using HS2Next.Content.Maker;
using HS2Next.Content.Maker.Tests;
using HS2Next.UI.Panels.Maker;
using UnityEditor;
using UnityEngine;

public static class MakerListTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "maker");
    static string ReportPath => Path.Combine(OutDir, "maker_lists_report.txt");
    static string ThumbsReportPath => Path.Combine(OutDir, "maker_thumbs_report.txt");

    /// <summary>The mod heads whose presets LS samples (and H4 measures), with their head category.</summary>
    public static readonly (int category, ContentId head)[] SampleHeads =
    {
        (210, new ContentId("cunihinx.ada4", 424)), (210, new ContentId("Darkruler.ElenaFisher", 166)), (210, new ContentId("mcm82.captainmmfr", 315)),
        (210, new ContentId("SelfishCrow.raven", 278)), (110, new ContentId("Supreme.Maleface", 38)),
    };

    sealed class Rep
    {
        public readonly StringBuilder Sb = new StringBuilder();
        public int Pass, Fail;
        public void Check(bool ok, string what) { if (ok) Pass++; else Fail++; Sb.AppendLine((ok ? "ok   " : "FAIL ") + what); }
        public void Info(string s) => Sb.AppendLine("     " + s);
        public void Write(string path, string title)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, $"SUMMARY: {Pass} PASS, {Fail} FAIL\n{title} (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})\n" + Sb, new UTF8Encoding(false));
        }
    }

    // ================================================================ Run (headless)

    public static void Run()
    {
        var rep = new Rep();
        int code = 1;
        try { code = RunCore(rep); }
        catch (Exception e) { rep.Check(false, "threw: " + e); }
        rep.Write(ReportPath, "MakerListTest");
        UnityEngine.Debug.Log($"MakerListTest: {rep.Pass} PASS, {rep.Fail} FAIL -> {ReportPath}");
        TestExit.Exit(rep.Fail == 0 && code == 0 ? 0 : 1);
    }

    static int RunCore(Rep rep)
    {
        var settings = ContentSettings.LoadDefault();
        string root = ContentSettings.Resolve(settings.hs2Root);
        var sw = Stopwatch.StartNew();

        // 1. every vanilla row with the gate open (the AI-Shoujo flags on), then the app's catalog (gate as built). The two
        //    catalogs read the same list bundles, which Unity cannot hold open twice: the first is unloaded before the second.
        var open = new ContentCatalog(null).AddSource(new Hs2InstallSource(root) { AiSpecial02 = true, AiSpecial03 = true, AiSpecial04 = true });
        open.LoadVanillaLists();
        var all = new List<ListEntry>();
        foreach (int c in open.Categories) all.AddRange(open.EntriesOf(c).Where(e => e.IsVanilla));
        open.UnloadAll();
        // the rows the game's gate drops without the AI-Shoujo flags (the fixture's "gated" column; independent of C9, which
        // opens the app catalog's gate since 4.0)
        var closedKeys = new HashSet<(int, ContentId)>();
        var closed = new ContentCatalog(null).AddSource(new Hs2InstallSource(root));
        closed.LoadVanillaLists();
        foreach (int c in closed.Categories) foreach (var e in closed.EntriesOf(c).Where(e => e.IsVanilla)) closedKeys.Add((c, e.Key));
        closed.UnloadAll();
        var cat = ContentCatalog.FromSettings(settings);
        cat.LoadLists();
        rep.Info($"catalog: {cat.Count} rows ({cat.VanillaCount} vanilla, {cat.ModCount} mod) in {sw.ElapsedMilliseconds} ms; gate-open vanilla rows {all.Count}");
        sw.Restart();
        var lists = MakerCatalog.For(cat);
        var rows = (CatalogListRows)lists.Source;
        rep.Info($"maker infos: {rows.InfosInLoadOrder.Count} zipmods in load order, {rows.InfosScanned} manifests read, {rows.InfoErrors.Count} notes, {rows.InfoLoadMs:0} ms");

        // 2. the fixture
        var fixture = Path.Combine(cat.CacheDir, "maker", "vanilla_lists.tsv");
        var lines = new List<string> { FixtureListRows.VanillaHeader, "# dumped from the catalog by MakerListTest.Run (Unity) " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) };
        int gated = 0;
        foreach (var e in all)
        {
            bool g = !closedKeys.Contains((e.Category, e.Key));
            if (g) gated++;
            var cells = new List<string> { "R", N(e.Category), N(e.Key.Id), g ? "1" : "0" };
            foreach (var kv in e.Values) { if (kv.Key.Length == 0) continue; cells.Add(Clean(kv.Key)); cells.Add(Clean(kv.Value)); }
            lines.Add(string.Join("\t", cells));
        }
        var presetHeads = new List<(int, ContentId)>();
        foreach (int hc in new[] { 210, 110 }) foreach (var e in cat.EntriesOf(hc).Where(e => e.IsVanilla)) presetHeads.Add((hc, e.Key));
        presetHeads.AddRange(SampleHeads);
        var loaded = new Dictionary<(int, ContentId), FacePresetResult>();
        foreach (var (hc, key) in presetHeads)
        {
            var r = FacePresetLoader.Load(cat, lists, key, hc == 110 ? 0 : 1);
            loaded[(hc, key)] = r;
            var cells = new List<string> { "P", N(hc), key.ToString() };
            if (r.Missing) cells.Add("missing");
            else foreach (var kv in r.LocalIds.OrderBy(k => k.Key, StringComparer.Ordinal)) cells.Add(kv.Key + "=" + N(kv.Value));
            lines.Add(string.Join("\t", cells));
        }
        Directory.CreateDirectory(Path.GetDirectoryName(fixture));
        File.WriteAllLines(fixture, lines, new UTF8Encoding(false));
        rep.Check(gated == 15 && all.Count == 1676, $"fixture written: {all.Count} vanilla rows ({gated} gated), {presetHeads.Count} preset heads -> {fixture}");
        int inApp = all.Count(e => cat.TryGetEntry(e.Category, e.Key, out _));
        bool c9 = ContentCatalog.AiInstallOpensCharacterLists && settings.aiInstall;
        rep.Check(inApp == (c9 ? 1676 : 1661), $"the app catalog lists {inApp} of the 1,676 vanilla rows (C9 {(c9 ? "applied: the gated rows listed, as HS2 here (H1)" : "off: the game's gate")})");

        // 3. the plain-Mono LS groups over the fixture
        var t = new HS2Next.Save.Tests.TestRunner { TempDir = Path.Combine(Path.GetTempPath(), "hs2n_maker_ls_" + Process.GetCurrentProcess().Id), FixturesDir = Path.GetDirectoryName(cat.CacheDir) };   // the folder holding Cache/ (a worktree may share the main cache)
        Directory.CreateDirectory(t.TempDir);
        foreach (var (name, run) in new (string, Action<HS2Next.Save.Tests.TestRunner>)[] { ("MakerRulesTests", MakerRulesTests.Run), ("MakerCensusTests", MakerCensusTests.Run), ("MakerPresetTests", MakerPresetTests.Run), ("MakerHs2Tests", MakerHs2Tests.Run) })
        {
            t.Group = name;
            int f0 = t.Failed, p0 = t.Passed, l0 = t.Lines.Count;
            try { run(t); } catch (Exception e) { t.Check(false, "group threw " + e); }
            rep.Check(t.Failed == f0, $"{name}: {t.Passed - p0} passed, {t.Failed - f0} failed");
            foreach (var l in t.Lines.Skip(l0)) rep.Info(l);
        }
        try { Directory.Delete(t.TempDir, true); } catch (Exception) { }

        // 4. catalog lists = fixture lists
        var fx = FixtureListRows.Load(FixtureListRows.FindModCache(cat.CacheDir), fixture);
        var fl = new MakerLists(fx) { ShowGated = c9 };
        int catsBad = 0, catsChecked = 0, skippedRows = 0;
        foreach (int c in fx.Categories.Union(cat.Categories).OrderBy(c => c))
        {
            catsChecked++;
            var a = rows.RowsOf(c).Select(r => r.Key).ToList();
            // the fixture reads the scan cache without the zipmod loader's set: rows of zipmods the catalog skips are counted apart
            var b = fx.RowsOf(c).Where(r => (c9 || !r.Gated) && (r.Key.IsVanilla || cat.ZipmodsByGuid.ContainsKey(r.Key.SourceGuid))).Select(r => r.Key).ToList();
            skippedRows += fx.RowsOf(c).Count(r => !r.Key.IsVanilla && !cat.ZipmodsByGuid.ContainsKey(r.Key.SourceGuid));
            if (!a.SequenceEqual(b)) { catsBad++; if (catsBad <= 5) rep.Info($"category {c}: catalog {a.Count} rows, fixture {b.Count}; first difference at {Enumerable.Range(0, Math.Min(a.Count, b.Count)).FirstOrDefault(i => a[i] != b[i])}"); }
        }
        rep.Info($"fixture rows of zipmods the catalog does not load (the zipmod set {(ContentCatalog.SideloaderZipmodSet ? "on" : "off")}), left out: {skippedRows}");
        rep.Check(catsBad == 0, $"catalog rows = fixture rows ({(c9 ? "gated rows included: C9" : "non-gated")}), every category in order ({catsChecked} categories)");
        int headBad = 0, skins = 0;
        foreach (int sc in new[] { 211, 111 })
            foreach (var r in rows.RowsOf(sc))
            {
                skins++;
                var fr = fx.Find(sc, r.Key);
                if (fr == null || lists.HeadOf(r) != fl.HeadOf(fr) || lists.HeadSourceOf(r) != fl.HeadSourceOf(fr)) headBad++;
            }
        rep.Check(headBad == 0, $"head of every skin row: catalog = fixture ({skins} rows, {headBad} differ)");
        int listBad = 0, listsChecked = 0;
        foreach (var (hc, sc, sex) in new[] { (210, 211, 1), (110, 111, 0) })
            foreach (var h in lists.Rows(hc, sex))
            {
                listsChecked++;
                var ctx = ListContext.ForHead(h.Key);
                if (!lists.Rows(sc, sex, ctx).Select(r => r.Key).SequenceEqual(fl.Rows(sc, sex, ctx).Select(r => r.Key))) listBad++;
            }
        rep.Check(listBad == 0, $"skin lists per head: catalog = fixture ({listsChecked} heads)");

        // 5. face presets through the importer's face mapping
        foreach (var kv in loaded.Where(k => !k.Key.Item2.IsVanilla || k.Key.Item1 == 210 && k.Key.Item2.Id == 0))
        {
            var r = kv.Value;
            if (r.Missing) { rep.Info($"preset {kv.Key.Item2}: missing ({string.Join("; ", r.Findings)})"); continue; }
            var face = r.Face;
            bool ok = face != null && face.Head == r.Refs["face.head"].ToString() && face.Skin == r.Refs["face.skin"].ToString()
                      && face.Eyes.Left.Iris.Item == r.Refs["face.eyes.left.iris.item"].ToString() && face.Makeup.Lip.Item == r.Refs["face.makeup.lip.item"].ToString()
                      && face.Makeup.Paint[1].LayoutItem == r.Refs["face.makeup.paint[1].layoutItem"].ToString();
            var want = HeadPresets.ResolveAll(rows, r.Head, kv.Key.Item1 == 110 ? 0 : 1, r.LocalIds);
            ok &= want.All(w => r.Refs.TryGetValue(w.Key, out var got) && got == w.Value);
            rep.Check(ok, $"preset {kv.Key.Item2} ({r.Entry?.Preset ?? "no entry"}): face block refs = HeadPresets' resolution; head {face?.Head}, skin {face?.Skin}, {r.Findings.Count} findings");
        }
        rep.Info($"total {sw.ElapsedMilliseconds} ms");
        return 0;
    }

    static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
    static string Clean(string s) => s == null ? "" : s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    // ================================================================ Thumbs (U7, graphics, play mode)

    public static void Thumbs()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(ThumbsReportPath, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("MakerListTest.ThumbsPlay", ThumbsReportPath, 30, true);
    }

    public static IEnumerator ThumbsPlay()
    {
        var rep = new Rep();
        var settings = ContentSettings.LoadDefault();
        var cat = ContentCatalog.FromSettings(settings);
        cat.LoadLists();
        var lists = MakerCatalog.For(cat);
        string tmp = Path.Combine(Path.GetTempPath(), "hs2n_u7_thumbs_" + Process.GetCurrentProcess().Id);
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        var head3 = ContentId.Vanilla(3);
        var modHead = lists.Rows(210, 1).Where(h => !h.IsVanilla).OrderByDescending(h => lists.Rows(211, 1, ListContext.ForHead(h.Key)).Count).ThenBy(h => h.Key).First().Key;
        foreach (var head in new[] { head3, modHead })
        {
            var list = lists.Rows(211, 1, ListContext.ForHead(head)).ToList();
            rep.Info($"ft_skin_f list of {head}: {list.Count} rows ({list.Count(r => r.IsVanilla)} vanilla)");
            foreach (var (label, readDisk) in new[] { ("without disk cache (cold bundles)", false), ("with disk cache", true) })
            {
                var pt = new PartThumbnails(cat, tmp) { ReadDisk = readDisk, WriteDisk = !readDisk };
                int open0 = cat.OpenBundleCount, thumbOnly0 = cat.ThumbnailOnlyCount;
                var owner = new object();
                var sw = Stopwatch.StartNew();
                double screen = -1;
                const int ScreenCells = 24;
                var done = new HashSet<ContentId>();
                pt.Loaded += (c, k, tex) => done.Add(k);
                foreach (var r in list) pt.Request(211, r.Key, owner);
                int frames = 0;
                var frameMs = new List<double>();
                while (pt.Pending > 0 || (!readDisk && pt.DiskWritesPending > 0))
                {
                    var f0 = Stopwatch.StartNew();
                    pt.Update();
                    frameMs.Add(f0.Elapsed.TotalMilliseconds);
                    frames++;
                    if (screen < 0 && list.Take(ScreenCells).All(r => done.Contains(r.Key))) screen = sw.Elapsed.TotalMilliseconds;
                    if (sw.Elapsed.TotalSeconds > 120) { rep.Check(false, "loader did not settle in 120 s"); break; }
                    yield return null;
                }
                if (screen < 0) screen = sw.Elapsed.TotalMilliseconds;
                double total = sw.Elapsed.TotalMilliseconds;
                int opened = cat.OpenBundleCount - open0;
                var fv = pt.FirstViewMs;
                // 4.2 GB10: Sideloader loose-PNG thumbnails (FromLoose) are rows 4.1 counted as "no thumbnail"
                rep.Check(pt.FromBundles + pt.FromDiskCount + pt.FromLoose + pt.FailedCount == list.Count,
                    $"U7 {head} {label}: {list.Count} rows: from bundles {pt.FromBundles}, from disk {pt.FromDiskCount}, loose PNGs {pt.FromLoose}, no thumbnail {pt.FailedCount}; " +
                    $"first view per row median {Pct(fv, 0.5):0.0} ms, p95 {Pct(fv, 0.95):0.0}, max {(fv.Count > 0 ? fv.Max() : 0):0.0}; first {Math.Min(ScreenCells, list.Count)} cells {screen:0} ms, all {total:0} ms over {frames} frames " +
                    $"(Update median {Pct(frameMs, 0.5):0.00} ms, max {(frameMs.Count > 0 ? frameMs.Max() : 0):0.00}); bundles opened {opened} (thumbnail-only {cat.ThumbnailOnlyCount - thumbOnly0}); " +
                    $"accept median {Pct(pt.AcceptMs, 0.5):0.000} ms; LRU peak {pt.PeakBytes / 1048576.0:0.0} MB; PNGs written {pt.DiskWritten}");
                rep.Info($"  starts (synchronous bundle opens): median {Pct(pt.StartMs, 0.5):0.00} ms, p95 {Pct(pt.StartMs, 0.95):0.0}, slowest {pt.SlowestStart ?? "-"}; error tiles: " +
                    (pt.FailReasons.Count == 0 ? "none" : string.Join(", ", pt.FailReasons.Select(kv => kv.Key + " " + kv.Value))));
                if (readDisk) rep.Check(opened == 0 && pt.FromBundles == 0, $"U7 {head}: with the disk cache no bundle is opened ({opened})");
                int before = cat.OpenBundleCount;
                pt.Clear();
                rep.Info($"after Clear: bundles unloaded {pt.BundlesUnloaded} (open {before} -> {cat.OpenBundleCount}, thumbnail-only now {cat.ThumbnailOnlyCount})");
            }
        }
        // a synchronous load clears the thumbnail-only mark (the bundle a build uses stays open)
        var e0 = cat.EntriesOf(211).First(e => e.IsVanilla);
        var pt2 = new PartThumbnails(cat, null) { ReadDisk = false, WriteDisk = false };
        pt2.Request(211, e0.Key, "x");
        while (pt2.Pending > 0) { pt2.Update(); yield return null; }
        var b = cat.LoadBundle(e0, e0["ThumbAB"]);
        rep.Check(b && !cat.IsThumbnailOnly(b), "a synchronous load clears the thumbnail-only mark");
        pt2.Clear();
        rep.Check(b, "the bundle stays open after the thumbnails are cleared");
        try { Directory.Delete(tmp, true); } catch (Exception) { }
        cat.UnloadAll();
        rep.Write(ThumbsReportPath, "MakerListTest.Thumbs (U7)");
        PlayModeHarness.ExitCode = rep.Fail == 0 ? 0 : 1;
    }

    static double Pct(List<double> v, double p)
    {
        if (v == null || v.Count == 0) return 0;
        var s = v.OrderBy(x => x).ToList();
        return s[Math.Min(s.Count - 1, (int)Math.Floor(p * (s.Count - 1) + 0.5))];
    }
}
