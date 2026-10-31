// The thumbnail guard (v0.3 trial 2, package G; the head-type list crash of the v0.3 trial).
//   Headers (headless, -nographics): the app's catalog; every row of every list category with ThumbAB / ThumbTex (the heads
//     210 / 110 also with Preset): the bundle the thumbnail load opens (ContentCatalog.OpenForAsset, thumbnail-only, unloaded
//     after its rows), its bytes (BundleFileOf) and the texture's header (TextureHeaders.Get), per defect class, in two passes:
//     the original copies (TextureGuard.UseRepairedBundles off) and the repaired copies (on). Checks: the classes of
//     TextureHeaders.Defect on synthetic headers; the LZMA decoder's nodes equal Python's lzma (-lzmaRef <file> of the
//     census' --lzma-ref); malformed bundles (a node outside the data) are defects like the others; the bundles the reader
//     cannot parse otherwise hold no defect of the census; with the original copies the
//     defects equal the census' for the same copies (Tools/survey/thumb_guard_census.py defects.tsv, -census <file>; skipped
//     without); with the repaired copies no thumbnail has a defect that is not in the texture guard's list (those get the
//     error tile).
//   Play (graphics, -NoQuit): Darkruler.2B's head thumbnail (p_cf_head_02, 512 x 511 DXT5; ThumbTex and Preset of 210 #101)
//     through PartThumbnails: (a) the guard's records and repaired copies out of the way (an empty cache root): the catalog's
//     texture check pads both in memory at the open (512 x 512 DXT5, the verdict "pad 512x511->512x512" in the isolated
//     verdict cache and the patch in its repair cache), both load, no RGBA32 fallback and no failed read logged; (b) the
//     project's cache: loaded from the repaired copy, 512 x 512, no fallback logged; (c) every block-size texture of the
//     census rows the same way as (b).
// Run: & Tools\unity_batch.ps1 -Method ThumbGuardTest.RunHeaders -NoGraphics [-MethodArgs @('-census','<defects.tsv>','-lzmaRef','<lzma_ref.tsv>')]
//      & Tools\unity_batch.ps1 -Method ThumbGuardTest.RunPlay -NoQuit [-MethodArgs @('-census','<defects.tsv>')]
// Reports: Spike/out/maker/thumb_guard_headers_report.txt, thumb_guard_play_report.txt ("SUMMARY: N PASS, M FAIL").
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Chara;
using HS2Next.Content;
using HS2Next.UI.Panels.Maker;
using UnityEditor;
using UnityEngine;

public static class ThumbGuardTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "maker");
    static string HeadersReport => Path.Combine(OutDir, "thumb_guard_headers_report.txt");
    static string PlayReport => Path.Combine(OutDir, "thumb_guard_play_report.txt");

    sealed class Rep
    {
        public readonly StringBuilder Sb = new StringBuilder();
        public int Pass, Fail;
        public bool Check(bool ok, string what, string detail = "")
        {
            if (ok) Pass++; else Fail++;
            Sb.AppendLine((ok ? "PASS " : "FAIL ") + what + (ok || string.IsNullOrEmpty(detail) ? "" : " — " + detail));
            return ok;
        }
        public void Info(string s) => Sb.AppendLine("   " + s);
        public void Write(string path, string title, double seconds)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string sum = $"SUMMARY: {Pass} PASS, {Fail} FAIL ({seconds:0.0} s)";
            File.WriteAllText(path, sum + "\n" + title + "\n" + Sb + sum + "\n", new UTF8Encoding(false));
        }
    }

    static string Arg(string name)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    // ------------------------------------------------------------ headers (headless)

    public static void RunHeaders()
    {
        var rep = new Rep();
        var sw = Stopwatch.StartNew();
        try { Headers(rep); }
        catch (Exception e) { rep.Check(false, "threw: " + e); }
        rep.Write(HeadersReport, "ThumbGuardTest.RunHeaders (the thumbnail texture headers of every Maker list)", sw.Elapsed.TotalSeconds);
        UnityEngine.Debug.Log($"ThumbGuardTest.RunHeaders: {rep.Pass} PASS, {rep.Fail} FAIL -> {HeadersReport}");
        EditorApplication.Exit(rep.Fail == 0 ? 0 : 1);
    }

    static string ClassOf(string defect) => defect == null ? null : defect.StartsWith("image size", StringComparison.Ordinal) ? "image size" : defect;

    static void Units(Rep rep)
    {
        TextureHeader H(int w, int h, int f, int m, int size) => new TextureHeader { Name = "t", Width = w, Height = h, Format = f, Mips = m, CompleteSize = size };
        rep.Check(TextureHeaders.Defect(H(512, 512, 12, 10, 349552)) == null, "Defect: 512 x 512 DXT5, 10 mips, the full chain -> none");
        rep.Check(TextureHeaders.Defect(H(512, 511, 12, 10, 349552)) == "block size", "Defect: 512 x 511 DXT5 (the trial's thumbnail) -> block size");
        rep.Check(TextureHeaders.Defect(H(493, 388, 10, 9, 0)) == "block size", "Defect: 493 x 388 DXT1 -> block size");
        rep.Check(TextureHeaders.Defect(H(256, 256, 12, 0, 65536)) == "mip < 1", "Defect: mips 0 -> mip < 1");
        rep.Check(TextureHeaders.Defect(H(0, 256, 4, 1, 0)) == "empty size", "Defect: width 0 -> empty size");
        rep.Check(TextureHeaders.Defect(H(64, 64, 4, 9, 21848)) == "mips > chain", "Defect: 64 x 64 with 9 mips -> mips > chain");
        rep.Check(TextureHeaders.Defect(H(256, 256, 34, 1, 32768)) == "cpu format", "Defect: ETC_RGB4 -> cpu format");
        rep.Check(ClassOf(TextureHeaders.Defect(H(256, 256, 4, 1, 1000))) == "image size", "Defect: RGBA32 256 x 256 with 1,000 bytes -> image size");
        rep.Check(TextureHeaders.Defect(H(256, 256, 3, 1, 196634)) == null, "Defect: RGB24 256 x 256 with 26 bytes more than the chain -> none (common, harmless)");
        rep.Check(TextureHeaders.Defect(H(128, 128, 29, 1, 5000)) == null, "Defect: a crunched DXT5 (its stream is shorter by design) -> none");
        rep.Check(TextureHeaders.Defect(H(4, 4, 4, 1, 64)) == null, "Defect: the guard's 4 x 4 RGBA32 fallback -> none");
    }

    static Dictionary<string, (string cls, string texture)> ReadCensus(Rep rep, string file)
    {
        var d = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        if (file == null || !File.Exists(file)) { rep.Info("no census file (-census <defects.tsv> of Tools/survey/thumb_guard_census.py): the comparison is skipped"); return null; }
        foreach (var l in File.ReadLines(file))
        {
            if (l.StartsWith("#", StringComparison.Ordinal)) continue;
            var p = l.Split('\t');
            if (p.Length < 9) continue;
            d[p[8] + "|" + p[2]] = (p[0].Split(',')[0], p[2]);
        }
        rep.Info($"census {file}: {d.Count} defective textures");
        return d;
    }

    // The runtime's LZMA decoder (Hs2Lzma through Hs2BundleFile) against Python's lzma module: the SHA-256 of every node of
    // the LZMA thumbnail copies (Tools/survey/thumb_guard_census.py --lzma-ref <file>; -lzmaRef <file> here).
    static void LzmaCheck(Rep rep, string file)
    {
        if (file == null || !File.Exists(file)) { rep.Info("no LZMA reference (-lzmaRef <file> of thumb_guard_census.py --lzma-ref): the decoder comparison is skipped"); return; }
        int nodes = 0, equal = 0, bundles = 0;
        long bytes = 0;
        var bad = new List<string>();
        var sw = Stopwatch.StartNew();
        foreach (var g in File.ReadLines(file).Where(l => !l.StartsWith("#", StringComparison.Ordinal)).Select(l => l.Split('\t')).Where(p => p.Length >= 4).GroupBy(p => p[0]))
        {
            bundles++;
            try
            {
                int bang = g.Key.IndexOf('!');
                var zip = new HS2Next.IO.ZipIndex(g.Key.Substring(0, bang));
                var ze = zip.Entries["abdata/" + g.Key.Substring(bang + 1)];
                var b = ze.Method == 0 ? Hs2BundleFile.OpenFile(zip.Path, ze.DataOffset, ze.Size, g.Key, lzma: true) : Hs2BundleFile.FromBytes(zip.ReadAll(ze), g.Key, lzma: true);
                foreach (var p in g)
                {
                    nodes++;
                    var n = b.Find(p[1]);
                    if (n == null) { bad.Add(g.Key + " " + p[1] + ": no node"); continue; }
                    var data = b.ReadRange(n.Value, 0, (int)n.Value.Size);
                    bytes += data.Length;
                    string sha;
                    using (var h = System.Security.Cryptography.SHA256.Create()) sha = string.Concat(h.ComputeHash(data).Select(x => x.ToString("x2")));
                    if (data.Length.ToString(CultureInfo.InvariantCulture) == p[2] && sha == p[3]) equal++;
                    else bad.Add($"{g.Key} {p[1]}: {data.Length} bytes {sha.Substring(0, 12)} vs {p[2]} {p[3].Substring(0, Math.Min(12, p[3].Length))}");
                }
            }
            catch (Exception e) { bad.Add(g.Key + ": " + e.GetType().Name + ": " + e.Message); }
        }
        rep.Check(nodes > 0 && equal == nodes, $"the LZMA decoder: {equal} of {nodes} nodes of {bundles} LZMA thumbnail bundles equal Python's lzma ({bytes / 1048576.0:0.0} MB in {sw.Elapsed.TotalSeconds:0.0} s)",
            string.Join(" | ", bad.Take(5)));
    }

    static void Headers(Rep rep)
    {
        Units(rep);
        LzmaCheck(rep, Arg("-lzmaRef"));
        var settings = ContentSettings.LoadDefault();
        var cat = ContentCatalog.FromSettings(settings);
        cat.LoadLists();
        var census = ReadCensus(rep, Arg("-census"));
        // the thumbnail references, grouped by the row's source and bundle path (the rows of one bundle in one go)
        var refs = new List<(ListEntry e, string col)>();
        foreach (int c in cat.Categories)
            foreach (var e in cat.EntriesOf(c))
                foreach (var col in c == 210 || c == 110 ? new[] { "ThumbTex", "Preset" } : new[] { "ThumbTex" })
                {
                    string ab = e["ThumbAB"], tex = e[col];
                    if (string.IsNullOrEmpty(ab) || ab == "0" || string.IsNullOrEmpty(tex) || tex == "0") continue;
                    refs.Add((e, col));
                }
        var groups = refs.GroupBy(r => (r.e.Zipmod?.Path ?? "") + "|" + r.e["ThumbAB"].Replace('\\', '/').ToLowerInvariant()).ToList();
        rep.Info($"catalog: {cat.Count} rows; thumbnail references {refs.Count} in {groups.Count} (source, bundle path) groups");
        bool keepRepaired = TextureGuard.UseRepairedBundles;
        try
        {
            foreach (bool repaired in new[] { false, true })
            {
                TextureGuard.UseRepairedBundles = repaired;
                TextureHeaders.ClearCache();
                cat.UnloadAll();
                cat.LoadLists();
                var sw = Stopwatch.StartNew();
                var classes = new Dictionary<string, int>(StringComparer.Ordinal);
                var defects = new Dictionary<string, (string cls, string texture, string label)>(StringComparer.OrdinalIgnoreCase);
                int noBundle = 0, noFile = 0, lzma = 0, lzmaRead = 0, malformed = 0, unreadable = 0, missing = 0, ok = 0, listedDefects = 0;
                var unreadableSamples = new List<string>();
                var unreadableKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var noFileSamples = new List<string>();
                var unlisted = new List<string>();
                var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var g in groups)
                {
                    var opened = new HashSet<AssetBundle>();
                    foreach (var (e, col) in g)
                    {
                        AssetBundle b = null;
                        try { b = cat.OpenForAsset(e, "ThumbAB", col, true); } catch (Exception) { }
                        if (!b) { noBundle++; continue; }
                        opened.Add(b);
                        var file = cat.BundleFileOf(b);
                        if (file == null)
                        {
                            noFile++;
                            if (noFileSamples.Count < 6) noFileSamples.Add($"{e.Category}/{e.Key} {e["ThumbAB"]} (source {e.Zipmod?.Path ?? "abdata"}, bundle {b.name})");
                            continue;
                        }
                        keys.Add(file.Key);
                        var h = TextureHeaders.Get(file);
                        if (h.Lzma && h.Error == null) lzmaRead++;
                        string cls;
                        TextureHeader th = default;
                        if (h.Malformed) { cls = "malformed bundle"; th.Name = e[col]; malformed++; }
                        else
                        {
                            if (h.Error != null)
                            {
                                if (h.Lzma) lzma++;
                                else { unreadable++; if (unreadableKeys.Add(file.Key)) unreadableSamples.Add(file.Key + ": " + h.Error); }
                                continue;
                            }
                            if (!h.TryGet(e[col], out th)) { missing++; continue; }
                            cls = ClassOf(TextureHeaders.Defect(th));
                            if (cls == null) { ok++; continue; }
                        }
                        classes[cls] = (classes.TryGetValue(cls, out int n) ? n : 0) + 1;
                        string label = (e.Zipmod?.Guid ?? "abdata") + ":" + e["ThumbAB"];
                        defects[file.Key + "|" + th.Name] = (cls, th.Name, label);
                        bool listed = TextureGuard.IsListed(label, e[col]);
                        if (listed) listedDefects++;
                        else if (unlisted.Count < 12) unlisted.Add($"{cls} {label} / {e[col]} {th.Width}x{th.Height} {th.FormatName} mips {th.Mips}");
                    }
                    foreach (var b in opened) if (b && cat.IsThumbnailOnly(b)) cat.UnloadThumbnailBundle(b);
                }
                string pass = repaired ? "repaired copies" : "original copies";
                rep.Info($"{pass}: {refs.Count} references in {sw.Elapsed.TotalSeconds:0.0} s; bundles {keys.Count} (read {TextureHeaders.BundlesRead} so far); headers ok {ok}, " +
                         $"defective {classes.Values.Sum()} ({string.Join(", ", classes.Select(kv => kv.Key + " " + kv.Value))}; unique textures {defects.Count}), texture not in the bundle {missing}, " +
                         $"no bundle {noBundle}, bundle file unknown {noFile}, LZMA read {lzmaRead}, LZMA not decoded {lzma}, malformed bundles' references {malformed}, unreadable otherwise {unreadable}");
                // a bundle the reader cannot parse loads as before (fail open, logged once): none may hold a defect the census found
                foreach (var u in unreadableSamples) rep.Info($"{pass}: unreadable (loads unchecked): {u}");
                foreach (var u in noFileSamples) rep.Info($"{pass}: bundle file unknown (loads unchecked): {u}");
                if (census != null)
                {
                    var hit = census.Keys.Where(k => unreadableKeys.Contains(k.Substring(0, k.LastIndexOf('|')))).ToList();
                    rep.Check(hit.Count == 0, $"{pass}: the {unreadableKeys.Count} bundles the reader cannot parse (not malformed) hold no defect of the census", string.Join("; ", hit.Take(6)));
                }
                rep.Info($"{pass}: references whose opened bundle has no file the catalog knows (another copy serves it; they load unchecked): {noFile}");
                if (!repaired && census != null)
                {
                    // the defects found here (the copies the rows are served from) against the census' for the same copies
                    var mine = defects.ToDictionary(kv => kv.Key, kv => kv.Value.cls, StringComparer.OrdinalIgnoreCase);
                    var theirs = census.Where(kv => keys.Contains(kv.Key.Substring(0, kv.Key.LastIndexOf('|')))).ToDictionary(kv => kv.Key, kv => kv.Value.cls, StringComparer.OrdinalIgnoreCase);
                    var onlyMine = mine.Keys.Except(theirs.Keys, StringComparer.OrdinalIgnoreCase).ToList();
                    var onlyTheirs = theirs.Keys.Except(mine.Keys, StringComparer.OrdinalIgnoreCase).ToList();
                    var differ = mine.Keys.Intersect(theirs.Keys, StringComparer.OrdinalIgnoreCase).Where(k => mine[k] != theirs[k]).ToList();
                    rep.Check(onlyMine.Count == 0 && onlyTheirs.Count == 0 && differ.Count == 0,
                        $"original copies: the header reader finds the census' defects of the served copies ({mine.Count} here, {theirs.Count} in the census for these copies)",
                        $"only here {onlyMine.Count} ({string.Join("; ", onlyMine.Take(4))}), only in the census {onlyTheirs.Count} ({string.Join("; ", onlyTheirs.Take(4))}), class differs {differ.Count} ({string.Join("; ", differ.Take(4))})");
                    rep.Check(classes.TryGetValue("block size", out int bs) && bs > 0 && defects.Keys.Any(k => k.EndsWith("|p_cf_head_02", StringComparison.OrdinalIgnoreCase) && defects[k].label.StartsWith("Darkruler.2B:", StringComparison.Ordinal)),
                        "original copies: Darkruler.2B's p_cf_head_02 (512 x 511 DXT5) is a block-size defect");
                }
                if (repaired)
                    rep.Check(unlisted.Count == 0, $"repaired copies: no thumbnail has a defect the guard's list does not name ({defects.Count} defective, {listedDefects} references listed: error tile without a load)",
                        string.Join(" | ", unlisted));
                cat.UnloadAll();
            }
        }
        finally { TextureGuard.UseRepairedBundles = keepRepaired; }
    }

    // ------------------------------------------------------------ play (graphics)

    public static void RunPlay()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(PlayReport, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("ThumbGuardTest.Play", PlayReport, 20, true);
    }

    public static IEnumerator Play()
    {
        var rep = new Rep();
        var total = Stopwatch.StartNew();
        int fallbacks = 0, readFailed = 0;
        var bad = new List<string>();
        Application.LogCallback onLog = (m, st, t) =>
        {
            if (m == null) return;
            if (m.Contains("requires multiple-of-four dimensions")) { fallbacks++; lock (bad) if (bad.Count < 6) bad.Add(m); }
            else if (m.Contains("Streaming Size differs") || m.Contains("async texture load: failed")) { readFailed++; lock (bad) if (bad.Count < 6) bad.Add(m); }
        };
        Application.logMessageReceivedThreaded += onLog;
        var settings = ContentSettings.LoadDefault();
        var cat = ContentCatalog.FromSettings(settings);
        cat.LoadLists();
        string projectCache = TextureGuard.CacheRoot;
        string empty = Path.Combine(Path.GetTempPath(), "hs2n_thumbguard_" + Process.GetCurrentProcess().Id);
        rep.Info($"graphics {SystemInfo.graphicsDeviceType}; cache {projectCache}; isolated cache root {empty}; header check {PartThumbnails.HeaderCheck}");
        rep.Check(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null, "a graphics device (the upload path is the one that crashed)");
        var head = new ContentId("Darkruler.2B", 101);
        try
        {
            // (a) no records, no repaired copies: the catalog's texture check pads both textures in memory at the open
            Directory.CreateDirectory(Path.Combine(empty, "texguard"));
            TextureGuard.CacheRoot = empty;
            TextureHeaders.ClearCache();
            int patched0 = cat.TextureCheckStats.Patched;
            var pa = new PartThumbnails(cat, null) { ReadDisk = false, WriteDisk = false };
            var owner = new object();
            pa.Request(210, head, owner);
            pa.Request(210, head, owner, PartThumbnails.PresetColumn);
            yield return Settle(pa, rep, "(a)");
            var a1 = pa.TryGet(210, head);
            var a2 = pa.TryGet(210, head, PartThumbnails.PresetColumn);
            bool padded(Texture2D t) => t != null && t.width == 512 && t.height == 512 && t.format == TextureFormat.DXT5;
            rep.Check(padded(a1) && padded(a2) && pa.HeaderRejected == 0 && pa.FromBundles == 2,
                $"(a) without records or repaired copies: Darkruler.2B #101 ThumbTex and Preset load padded by the texture check, {(a1 ? a1.width + "x" + a1.height + " " + a1.format : "null")} / {(a2 ? a2.width + "x" + a2.height + " " + a2.format : "null")} (refused {pa.HeaderRejected}, loaded {pa.FromBundles}, reasons {Reasons(pa)})");
            rep.Check(fallbacks == 0 && readFailed == 0, $"(a) no RGBA32 fallback and no failed texture read logged ({fallbacks}, {readFailed})", string.Join(" | ", bad));
            // the pad is recorded where the texture check records it: the (isolated) verdict cache and repair cache, and the
            // catalog's count of this session's patches
            string vf = Path.Combine(empty, "texcheck", "verdicts-v1.tsv"), ri = Path.Combine(empty, "texguard", "repaired.tsv");
            string vline = File.Exists(vf) ? File.ReadLines(vf).FirstOrDefault(l => l.IndexOf("Darkruler_2B/thumbs.unity3d", StringComparison.OrdinalIgnoreCase) >= 0) : null;
            string rline = File.Exists(ri) ? File.ReadLines(ri).FirstOrDefault(l => l.IndexOf("Darkruler_2B/thumbs.unity3d", StringComparison.OrdinalIgnoreCase) >= 0) : null;
            rep.Check(vline != null && vline.Split('\t').Length > 2 && vline.Split('\t')[2] == "patch" && vline.Contains("p_cf_head_02=pad 512x511->512x512")
                      && rline != null && rline.Contains("p_cf_head_02=pad 512x511->512x512") && cat.TextureCheckStats.Patched > patched0,
                "(a) the pad is recorded by the texture check: verdict \"patch\" with p_cf_head_02=pad 512x511->512x512 in the (isolated) verdict cache, the patched copy in its repair cache, and the catalog's patch count",
                $"verdict line {(vline != null ? "found" : "missing")}, repair index line {(rline != null ? "found" : "missing")}, patched +{cat.TextureCheckStats.Patched - patched0}");
            pa.Dispose();
            yield return null;

            // (b) the project's cache: the repaired copy loads, 512 x 512
            TextureGuard.CacheRoot = projectCache;
            TextureHeaders.ClearCache();
            cat.UnloadAll();
            cat.LoadLists();
            var pb = new PartThumbnails(cat, null) { ReadDisk = false, WriteDisk = false };
            pb.Request(210, head, owner);
            pb.Request(210, head, owner, PartThumbnails.PresetColumn);
            yield return Settle(pb, rep, "(b)");
            var t1 = pb.TryGet(210, head);
            var t2 = pb.TryGet(210, head, PartThumbnails.PresetColumn);
            rep.Check(t1 != null && t2 != null && t1.width == 512 && t1.height == 512 && pb.HeaderRejected == 0,
                $"(b) with the repaired copy: both load, {(t1 ? t1.width + "x" + t1.height + " " + t1.format : "null")} / {(t2 ? t2.width + "x" + t2.height : "null")} (refused {pb.HeaderRejected}, reasons {Reasons(pb)})");
            rep.Check(fallbacks == 0 && readFailed == 0, $"(b) no RGBA32 fallback and no failed texture read logged ({fallbacks}, {readFailed})", string.Join(" | ", bad));
            pb.Dispose();
            yield return null;

            // (c) every block-size thumbnail of the census, project cache
            var census = Arg("-census");
            if (census != null && File.Exists(census))
            {
                var pc = new PartThumbnails(cat, null) { ReadDisk = false, WriteDisk = false };
                int n = 0;
                foreach (var l in File.ReadLines(census))
                {
                    if (l.StartsWith("#", StringComparison.Ordinal)) continue;
                    var p = l.Split('\t');
                    if (p.Length < 10 || !p[0].Contains("block size")) continue;
                    foreach (var r in p[9].Split(' '))
                    {
                        var q = r.Split('/');
                        if (q.Length < 3 || !int.TryParse(q[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int category) || !ContentId.TryParse(q[1], out var key)) continue;
                        pc.Request(category, key, owner, q[2]);
                        n++;
                    }
                }
                yield return Settle(pc, rep, "(c)");
                rep.Check(n > 0 && pc.Pending == 0 && fallbacks == 0 && readFailed == 0,
                    $"(c) the {n} census rows with a block-size thumbnail: loaded {pc.FromBundles}, refused by the header {pc.HeaderRejected}, guard-skipped {pc.GuardSkipped}, failed {pc.FailedCount} ({Reasons(pc)}); fallbacks {fallbacks}, failed reads {readFailed}",
                    string.Join(" | ", bad));
                pc.Dispose();
            }
            else rep.Info("(c) skipped: no -census file");
        }
        finally
        {
            Application.logMessageReceivedThreaded -= onLog;
            TextureGuard.CacheRoot = projectCache;
            try { if (Directory.Exists(empty) && new DirectoryInfo(empty).GetFileSystemInfos("*", SearchOption.AllDirectories).All(f => (f.Attributes & FileAttributes.ReparsePoint) == 0)) Directory.Delete(empty, true); } catch (Exception) { }
        }
        rep.Write(PlayReport, "ThumbGuardTest.RunPlay (the header check and the repaired copies through PartThumbnails)", total.Elapsed.TotalSeconds);
        UnityEngine.Debug.Log($"ThumbGuardTest.RunPlay: {rep.Pass} PASS, {rep.Fail} FAIL -> {PlayReport}");
        PlayModeHarness.ExitCode = rep.Fail == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------ experiment: two bundles with the same internal name

    /// <summary>
    /// Clash (graphics, -NoQuit): Darkruler.2B's thumbnail bundle carries the vanilla head thumbnail bundle's internal name
    /// (chara/thumb/00/fo_head_00.unity3d) and its .resS file name (355 / 367 thumbnail copies of this install share those of
    /// that one bundle). Both are loaded through PartThumbnails (vanilla head 0 and 2B #101), then the vanilla bundle alone is
    /// unloaded (after the uploads; and, in a fresh instance, right after 2B's request): 2B's bundle stays open, its texture
    /// alive, a new load from it reads its data, no read fails (first runs: 6 PASS; Unity logs "AssetBundle '…fo_head_00…' was
    /// already unloaded" for the shared name, harmless). Report thumb_guard_clash_report.txt.
    /// </summary>
    public static void RunClash()
    {
        Directory.CreateDirectory(OutDir);
        string rp = Path.Combine(OutDir, "thumb_guard_clash_report.txt");
        File.WriteAllText(rp, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("ThumbGuardTest.Clash", rp, 10, true);
    }

    public static IEnumerator Clash()
    {
        var rep = new Rep();
        var total = Stopwatch.StartNew();
        var lines = new List<string>();
        Application.LogCallback onLog = (m, st, t) =>
        {
            if (m == null) return;
            if (m.Contains("Closing file") || m.Contains("already unloaded") || m.Contains("Could not open file archive") || m.Contains("async texture load: failed"))
                lock (lines) lines.Add(m.Length > 220 ? m.Substring(0, 220) : m);
        };
        Application.logMessageReceivedThreaded += onLog;
        var cat = ContentCatalog.FromSettings(ContentSettings.LoadDefault());
        cat.LoadLists();
        var p = new PartThumbnails(cat, null) { ReadDisk = false, WriteDisk = false };
        var owner = new object();
        var van = ContentId.Vanilla(0);
        var mod = new ContentId("Darkruler.2B", 101);
        p.Request(210, van, owner);
        yield return Settle(p, rep, "vanilla head 0");
        p.Request(210, mod, owner);
        yield return Settle(p, rep, "2B #101");
        var ev = cat.GetEntry(210, van);
        var em = cat.GetEntry(210, mod);
        var bv = cat.OpenForAsset(ev, "ThumbAB", "ThumbTex", true);
        var bm = cat.OpenForAsset(em, "ThumbAB", "ThumbTex", true);
        var tv = p.TryGet(210, van);
        var tm = p.TryGet(210, mod);
        rep.Info($"vanilla bundle '{(bv ? bv.name : "null")}' ({cat.BundleFileOf(bv)?.Key}), texture {(tv ? tv.width + "x" + tv.height : "null")}; 2B bundle '{(bm ? bm.name : "null")}' ({cat.BundleFileOf(bm)?.Key}), texture {(tm ? tm.width + "x" + tm.height : "null")}; same object {ReferenceEquals(bv, bm)}");
        for (int i = 0; i < 120; i++) yield return null;   // the uploads finish
        lock (lines) { rep.Info("log before the unload: " + string.Join(" | ", lines)); lines.Clear(); }
        // the vanilla bundle alone (its texture dropped first)
        p.Release(210, van, owner);
        p.Trim();
        for (int i = 0; i < 30; i++) { p.Update(); yield return null; }
        rep.Info($"after unloading the vanilla bundle: vanilla bundle alive {(bool)bv}, 2B bundle alive {(bool)bm}, 2B texture alive {(bool)tm}; bundles unloaded {p.BundlesUnloaded}");
        lock (lines) { rep.Info("log at the unload: " + string.Join(" | ", lines)); lines.Clear(); }
        // a new load from the 2B bundle: its Preset texture
        p.Request(210, mod, owner, PartThumbnails.PresetColumn);
        yield return Settle(p, rep, "2B #101 Preset after the vanilla unload");
        for (int i = 0; i < 120; i++) yield return null;
        var tp = p.TryGet(210, mod, PartThumbnails.PresetColumn);
        string after;
        lock (lines) { after = string.Join(" | ", lines); rep.Info("log after the new load: " + after); }
        rep.Check(tp != null && (bool)bm, $"2B's Preset loads after the vanilla bundle's unload ({(tp ? tp.width + "x" + tp.height : "null")}, 2B bundle alive {(bool)bm})");
        rep.Check(after.IndexOf("Could not open file archive", StringComparison.Ordinal) < 0 && after.IndexOf("async texture load: failed", StringComparison.Ordinal) < 0,
            "no failed read after the vanilla unload (Unity's \"already unloaded\" message for the shared name is harmless)", after);
        p.Dispose();
        for (int i = 0; i < 30; i++) yield return null;
        lock (lines) lines.Clear();
        // C: the vanilla bundle unloaded right after the 2B texture's request is done (its upload may still be pending)
        cat.UnloadAll();
        cat.LoadLists();
        var q = new PartThumbnails(cat, null) { ReadDisk = false, WriteDisk = false };
        q.Request(210, van, owner);
        yield return Settle(q, rep, "C: vanilla head 0");
        for (int i = 0; i < 60; i++) yield return null;
        q.Request(210, mod, owner);
        while (q.Pending > 0) { q.Update(8); yield return null; }
        q.Release(210, van, owner);
        q.Trim();
        q.Update();
        var tc = q.TryGet(210, mod);
        for (int i = 0; i < 120; i++) { q.Update(); yield return null; }
        string afterC;
        lock (lines) afterC = string.Join(" | ", lines);
        rep.Info($"C: 2B texture {(tc ? tc.width + "x" + tc.height : "null")} alive {(bool)tc}; log: {afterC}");
        rep.Check(afterC.IndexOf("Could not open file archive", StringComparison.Ordinal) < 0 && afterC.IndexOf("async texture load: failed", StringComparison.Ordinal) < 0,
            "C: no failed read of 2B's texture when the vanilla bundle is unloaded right after 2B's request", afterC);
        q.Dispose();
        Application.logMessageReceivedThreaded -= onLog;
        rep.Write(Path.Combine(OutDir, "thumb_guard_clash_report.txt"), "ThumbGuardTest.RunClash (two bundles with one internal name)", total.Elapsed.TotalSeconds);
        PlayModeHarness.ExitCode = rep.Fail == 0 ? 0 : 1;
    }

    static string Reasons(PartThumbnails p) => p.FailReasons.Count == 0 ? "none" : string.Join(", ", p.FailReasons.Select(kv => kv.Key + " " + kv.Value));

    static IEnumerator Settle(PartThumbnails p, Rep rep, string what)
    {
        var sw = Stopwatch.StartNew();
        while (p.Pending > 0 && sw.Elapsed.TotalSeconds < 120)
        {
            p.Update(8);
            yield return null;
        }
        for (int i = 0; i < 10; i++) yield return null;   // uploads finishing
        rep.Check(p.Pending == 0, $"{what} nothing pending after {sw.Elapsed.TotalSeconds:0.0} s ({p.Pending})");
    }
}
