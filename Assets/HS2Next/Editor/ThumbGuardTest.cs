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
    /// already unloaded" for the shared name, harmless). Then the namesake check, by content (see Namesakes). Report
    /// thumb_guard_clash_report.txt.
    /// </summary>
    public static void RunClash()
    {
        Directory.CreateDirectory(OutDir);
        string rp = Path.Combine(OutDir, "thumb_guard_clash_report.txt");
        File.WriteAllText(rp, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("ThumbGuardTest.Clash", rp, Arg("-fb1Pairs") == "0" ? 28 : 10, true);
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
        yield return Namesakes(rep, cat);
        rep.Write(Path.Combine(OutDir, "thumb_guard_clash_report.txt"), "ThumbGuardTest.RunClash (two bundles with one internal name)", total.Elapsed.TotalSeconds);
        PlayModeHarness.ExitCode = rep.Fail == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------ namesakes: both open, one asset name, different content

    sealed class NsCopy
    {
        public string Build, Key, Asset, Column, Hash;
        public ListEntry Entry;
        public override string ToString() => Key + " / " + Asset;
    }

    sealed class NsTally
    {
        public int Pairs, Requests, Equal, Different, ToNamesake, Empty, OtherContent, NotBothOpen, NoRequest, TimedOut, RawHashes, ReadBacks;
        public readonly List<string> Samples = new List<string>();
        public void Judge(string state, string got, NsCopy from, NsCopy other)
        {
            Requests++;
            if (got != null && got == from.Hash) { Equal++; return; }
            Different++;
            if (got == null) Empty++;
            else if (other != null && got == other.Hash) ToNamesake++;
            else OtherContent++;
            if (Samples.Count < 24) Samples.Add($"{state}: {from} (own {from.Hash}) read {got ?? "nothing"} with {other?.ToString() ?? "no namesake"} ({other?.Hash ?? "-"})");
        }
        public string Line => $"{Pairs} pairs, {Requests} requests: equal {Equal}, different {Different} (the namesake's content {ToNamesake}, empty {Empty}, other {OtherContent}); not both open {NotBothOpen}, no request {NoRequest}, timed out {TimedOut}";
    }

    // A hash of the texture's bytes, in memory: the raw data of a readable texture, else a read-back through a render target.
    // Nothing is written or shown.
    static string ContentHash(Texture2D t, NsTally y)
    {
        if (!t) return null;
        byte[] data;
        if (t.isReadable) { data = t.GetRawTextureData(); y.RawHashes++; }
        else
        {
            var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var active = RenderTexture.active;
            var copy = new Texture2D(t.width, t.height, TextureFormat.RGBA32, false, true);
            try
            {
                Graphics.Blit(t, rt);
                RenderTexture.active = rt;
                copy.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0, false);
                data = copy.GetRawTextureData();
            }
            finally
            {
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(copy);
            }
            y.ReadBacks++;
        }
        using (var h = System.Security.Cryptography.SHA256.Create())
            return t.width.ToString(CultureInfo.InvariantCulture) + "x" + t.height.ToString(CultureInfo.InvariantCulture) + " " + t.format + " " + string.Concat(h.ComputeHash(data).Take(8).Select(x => x.ToString("x2")));
    }

    // The load path's own refusals: never load what the header check or the pre-load check would keep from an upload.
    static bool SafeToLoad(ContentCatalog cat, AssetBundle b, string asset)
    {
        if (cat.IsAssetRefused(b, asset)) return false;
        var file = cat.BundleFileOf(b);
        if (file == null) return false;
        var h = TextureHeaders.Get(file);
        if (h == null || h.Malformed || h.Error != null || !h.TryGet(asset, out var th)) return false;
        return TextureHeaders.Defect(th) == null;
    }

    static AssetBundle OpenCopy(ContentCatalog cat, NsCopy c)
    {
        try { return cat.OpenForAsset(c.Entry, "ThumbAB", c.Column, true); }
        catch (Exception) { return null; }
    }

    static string SyncHash(AssetBundle b, NsCopy c, NsTally y) => b ? ContentHash(b.LoadAsset<Texture2D>(c.Entry[c.Column]), y) : null;

    // An asynchronous load through the path the thumbnails use. box[0] = the hash of what it read (null: nothing).
    static IEnumerator AsyncHash(ContentCatalog cat, NsCopy c, NsTally y, string[] box, Action started = null)
    {
        box[0] = null;
        var r = cat.LoadAssetAsync<Texture2D>(c.Entry, "ThumbAB", c.Column, true, out AssetBundle _);
        if (r == null) { y.NoRequest++; yield break; }
        started?.Invoke();
        var sw = Stopwatch.StartNew();
        while (!r.isDone && sw.Elapsed.TotalSeconds < 30) yield return null;
        if (!r.isDone) { y.TimedOut++; yield break; }
        box[0] = ContentHash(r.asset as Texture2D, y);
    }

    /// <summary>
    /// Namesakes (Unity 6's asynchronous asset load is said to find its bundle by the build name, which mods share): every
    /// thumbnail reference is opened as the loader opens it, for its build name and the copy that serves it. Copies of one build
    /// name holding one asset name are hashed one at a time (alone: a synchronous load, the hash of the bytes in memory), which
    /// finds the pairs with different content. For each such pair both are opened and the asset requested asynchronously, from
    /// the second and from the first; the same synchronously; the same with a namesake that lacks the asset; and with the
    /// namesake closed before the request or just after it. A read is judged against the copy's own hash. No image is opened or
    /// written. -fb1Pairs N bounds the pairs and the search (default below; 0 = every reference, every copy).
    /// </summary>
    static IEnumerator Namesakes(Rep rep, ContentCatalog cat)
    {
        int limit = int.TryParse(Arg("-fb1Pairs"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int lim) ? lim : DefaultNamesakePairs;
        var total = Stopwatch.StartNew();
        var y = new NsTally();
        bool Over(double minutes) => limit == 0 && total.Elapsed.TotalMinutes > minutes;   // a hand run stops looking at its budget, and says so
        cat.UnloadAll();
        cat.LoadLists();
        // every reference once: the build name and the copy that serves it
        var refs = new List<(ListEntry e, string col)>();
        foreach (int c in cat.Categories)
            foreach (var e in cat.EntriesOf(c))
                foreach (var col in c == 210 || c == 110 ? new[] { "ThumbTex", "Preset" } : new[] { "ThumbTex" })
                {
                    string ab = e["ThumbAB"], tex = e[col];
                    if (string.IsNullOrEmpty(ab) || ab == "0" || string.IsNullOrEmpty(tex) || tex == "0") continue;
                    if (cat.Loose.TryFind(e, "ThumbAB", col, out _) || TextureGuard.IsListed((e.Zipmod?.Guid ?? "abdata") + ":" + ab.Replace('\\', '/'), tex)) continue;
                    refs.Add((e, col));
                }
        var groups = refs.GroupBy(r => (r.e.Zipmod?.Path ?? "") + "|" + r.e["ThumbAB"].Replace('\\', '/').ToLowerInvariant()).ToList();
        var sameName = new Dictionary<string, Dictionary<string, NsCopy>>(StringComparer.Ordinal);   // build name | asset -> key -> copy
        var keysOfBuild = new Dictionary<string, Dictionary<string, NsCopy>>(StringComparer.Ordinal);   // build name -> key -> one copy
        int opened = 0, unopened = 0, groupsRead = 0;
        foreach (var g in groups)
        {
            if (limit > 0 && groupsRead >= NamesakeSearchGroups) break;
            groupsRead++;
            var open = new HashSet<AssetBundle>();
            foreach (var (e, col) in g)
            {
                AssetBundle b = null;
                try { b = cat.OpenForAsset(e, "ThumbAB", col, true); } catch (Exception) { }
                if (!b) { unopened++; continue; }
                if (open.Add(b)) opened++;
                string key = cat.BundleFileOf(b)?.Key;
                if (key == null) continue;
                var copy = new NsCopy { Build = b.name, Key = key, Asset = e[col].ToLowerInvariant(), Column = col, Entry = e };
                if (string.IsNullOrEmpty(copy.Build)) continue;
                string k = copy.Build + "|" + copy.Asset;
                if (!sameName.TryGetValue(k, out var m)) sameName[k] = m = new Dictionary<string, NsCopy>(StringComparer.OrdinalIgnoreCase);
                if (!m.ContainsKey(key)) m[key] = copy;
                if (!keysOfBuild.TryGetValue(copy.Build, out var ks)) keysOfBuild[copy.Build] = ks = new Dictionary<string, NsCopy>(StringComparer.OrdinalIgnoreCase);
                if (!ks.ContainsKey(key)) ks[key] = copy;
            }
            foreach (var b in open) if (b) cat.UnloadBundle(b);
            if (groupsRead % 32 == 0) yield return null;
        }
        var shared = keysOfBuild.Where(kv => kv.Value.Count > 1).OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        var candidates = sameName.Where(kv => kv.Value.Count > 1).OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        rep.Info($"namesakes: {refs.Count} references in {groups.Count} groups, {groupsRead} read ({opened} bundles opened, {unopened} references without a bundle) in {total.Elapsed.TotalSeconds:0.0} s; {keysOfBuild.Count} build names, {shared.Count} shared by several copies (" +
                 string.Join(", ", shared.Take(4).Select(kv => kv.Key + " " + kv.Value.Count)) + $"), {shared.Sum(kv => kv.Value.Count)} copies of those; {candidates.Count} (build name, asset) names in two or more copies");
        // the pairs with different content, by hash, each copy alone
        var pairs = new List<(NsCopy a, NsCopy b)>();
        int hashed = 0, unsafeCopies = 0, sameContent = 0, namesTried = 0;
        foreach (var kv in candidates)
        {
            if ((limit > 0 && pairs.Count >= limit) || Over(10)) break;
            namesTried++;
            var seen = new List<NsCopy>();
            int tried = 0;
            foreach (var c in kv.Value.Values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (limit > 0 && tried >= 4) break;
                tried++;
                var b = OpenCopy(cat, c);
                if (!b) { unsafeCopies++; continue; }
                if (!SafeToLoad(cat, b, c.Entry[c.Column])) { unsafeCopies++; cat.UnloadBundle(b); continue; }
                c.Hash = SyncHash(b, c, y);
                cat.UnloadBundle(b);
                hashed++;
                if (c.Hash == null) { unsafeCopies++; continue; }
                seen.Add(c);
                yield return null;
            }
            for (int i = 1; i < seen.Count; i++)
            {
                // every copy against the first that differs from it: a group of n with m contents gives n - 1 or fewer pairs
                var other = seen.Take(i).FirstOrDefault(x => x.Hash != seen[i].Hash);
                if (other != null) pairs.Add((other, seen[i]));
                else sameContent++;
            }
        }
        UnityEngine.Debug.Log($"[namesakes] {pairs.Count} pairs with different content ({total.Elapsed.TotalMinutes:0.0} min)");
        rep.Info($"namesakes: {namesTried} of {candidates.Count} names tried, {hashed} copies hashed alone, {unsafeCopies} left out (no load, refused or defective), {pairs.Count} pairs with different content, {sameContent} copies with their name's content ({total.Elapsed.TotalSeconds:0.0} s)");
        string Shape(string hash) => hash.Substring(0, hash.LastIndexOf(' '));
        rep.Info($"namesakes: of those pairs {pairs.Count(p => Shape(p.a.Hash) == Shape(p.b.Hash))} have one size and format, so only the pixels tell them apart");
        rep.Check(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null, "namesakes: a graphics device (the read-back of a texture that is not readable needs one)");
        // a pair found: an asynchronous load from the second and from the first, both open, then the same synchronously
        var box = new string[1];
        var baseline = new NsTally();
        int i0 = 0;
        foreach (var (a, b) in pairs)
        {
            i0++;
            if ((limit > 0 && y.Pairs >= limit) || Over(18)) break;
            y.Pairs++;
            if (y.Pairs % 100 == 0) UnityEngine.Debug.Log($"[namesakes] {y.Line} ({total.Elapsed.TotalMinutes:0.0} min)");
            // alone, asynchronously: what the same request reads with no namesake open (a difference here is not the name's)
            var bo = OpenCopy(cat, b);
            if (bo) { yield return AsyncHash(cat, b, baseline, box); baseline.Judge("alone", box[0], b, null); cat.UnloadBundle(bo); }
            foreach (var (first, second, from, other, state) in new[] { (a, b, b, a, "both open, from the second"), (a, b, a, b, "both open, from the first") })
            {
                var o1 = OpenCopy(cat, first);
                var o2 = OpenCopy(cat, second);
                if (!o1 || !o2 || ReferenceEquals(o1, o2) || o1.name != o2.name)
                {
                    y.NotBothOpen++;
                    if (y.Samples.Count < 24) y.Samples.Add($"not both open: {first} ({(bool)o1}) and {second} ({(bool)o2})");
                }
                else
                {
                    yield return AsyncHash(cat, from, y, box);
                    if (!o1 || !o2) y.NotBothOpen++;
                    y.Judge(state + ", asynchronous", box[0], from, other);
                    y.Judge(state + ", synchronous", SyncHash(from == first ? o1 : o2, from, y), from, other);
                }
                if (o1) cat.UnloadBundle(o1);
                if (o2) cat.UnloadBundle(o2);
                yield return null;
            }
            if (i0 > 12) continue;
            // the namesake closed before the request, and just after it
            foreach (bool inFlight in new[] { false, true })
            {
                var o1 = OpenCopy(cat, a);
                var o2 = OpenCopy(cat, b);
                if (!o1 || !o2 || ReferenceEquals(o1, o2)) y.NotBothOpen++;
                else
                {
                    if (!inFlight) cat.UnloadBundle(o1);
                    yield return AsyncHash(cat, b, y, box, inFlight ? () => cat.UnloadBundle(o1) : (Action)null);
                    y.Judge(inFlight ? "the namesake closed just after the request" : "the namesake closed before the request", box[0], b, a);
                }
                if (o1) cat.UnloadBundle(o1);
                if (o2) cat.UnloadBundle(o2);
                yield return null;
            }
        }
        // several namesakes in flight at once, as a list's scroll has them (the loader keeps at most 8 in flight)
        var together = new NsTally();
        foreach (var kv in candidates)
        {
            if ((limit > 0 && together.Pairs >= limit / 4) || Over(20)) break;
            var copies = kv.Value.Values.Where(x => x.Hash != null).Take(8).ToList();
            if (copies.Select(x => x.Hash).Distinct().Count() < 2) continue;
            var open = copies.Select(x => OpenCopy(cat, x)).ToList();
            if (open.Any(x => !x) || open.Distinct().Count() != open.Count) together.NotBothOpen++;
            else
            {
                together.Pairs++;
                var requests = copies.Select(x => cat.LoadAssetAsync<Texture2D>(x.Entry, "ThumbAB", x.Column, true, out AssetBundle _)).ToList();
                var sw = Stopwatch.StartNew();
                while (requests.Any(r => r != null && !r.isDone) && sw.Elapsed.TotalSeconds < 30) yield return null;
                for (int i = 0; i < copies.Count; i++)
                {
                    var r = requests[i];
                    if (r == null) { together.NoRequest++; continue; }
                    if (!r.isDone) { together.TimedOut++; continue; }
                    together.Judge("several in flight", ContentHash(r.asset as Texture2D, together), copies[i], copies.FirstOrDefault(x => x.Hash != copies[i].Hash));
                }
            }
            foreach (var b in open) if (b) cat.UnloadBundle(b);
            yield return null;
        }
        // a namesake open that lacks the asset (a read through the name would find nothing)
        var lacking = new NsTally();
        foreach (var kv in shared)
        {
            if ((limit > 0 && lacking.Pairs >= limit) || Over(22)) break;
            var copies = kv.Value.Values.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();
            for (int i = 1; i < copies.Count && !Over(22); i += limit > 0 ? 64 : 8)
            {
                var a = copies[0];
                var c = copies[i];
                var lone = OpenCopy(cat, c);
                if (!lone) continue;
                if (!SafeToLoad(cat, lone, c.Entry[c.Column])) { cat.UnloadBundle(lone); continue; }
                c.Hash = SyncHash(lone, c, lacking);
                cat.UnloadBundle(lone);
                if (c.Hash == null) continue;
                var o1 = OpenCopy(cat, a);
                var o2 = OpenCopy(cat, c);
                if (o1 && o2 && !ReferenceEquals(o1, o2) && o1.name == o2.name && !o1.Contains(c.Entry[c.Column]))
                {
                    lacking.Pairs++;
                    yield return AsyncHash(cat, c, lacking, box);
                    lacking.Judge("a namesake open that lacks the asset", box[0], c, null);
                }
                if (o1) cat.UnloadBundle(o1);
                if (o2) cat.UnloadBundle(o2);
                yield return null;
            }
        }
        cat.UnloadAll();
        cat.LoadLists();
        rep.Info($"namesakes, baseline (alone, asynchronous against synchronous): {baseline.Line}");
        rep.Info($"namesakes: {y.Pairs} of {pairs.Count} pairs measured, {total.Elapsed.TotalMinutes:0.0} min");
        rep.Info($"namesakes, both open: {y.Line}; hashes by raw bytes {y.RawHashes}, by read-back {y.ReadBacks}");
        rep.Info($"namesakes, several in flight at once (the groups count as pairs): {together.Line}");
        rep.Info($"namesakes, a namesake open that lacks the asset: {lacking.Line}");
        foreach (var s in baseline.Samples.Concat(y.Samples).Concat(together.Samples).Concat(lacking.Samples)) rep.Info("namesakes: " + s);
        rep.Check(y.Pairs > 0, $"namesakes: pairs of one build name and one asset name with different content were found by hash ({y.Pairs}); a zero here means the check below proves nothing");
        rep.Check(baseline.Different == 0, $"namesakes: alone, an asynchronous load reads what a synchronous one reads ({baseline.Equal} of {baseline.Requests})");
        rep.Check(y.Different == 0 && y.Requests > 0, $"namesakes: both open or one just closed, a load reads the bundle it was called on ({y.Equal} of {y.Requests} equal, {y.Different} different)");
        rep.Check(together.Different == 0 && together.Requests > 0, $"namesakes: with several in flight at once, each load reads the bundle it was called on ({together.Equal} of {together.Requests} equal, {together.Different} different, {together.Pairs} groups)");
        rep.Check(lacking.Different == 0, $"namesakes: with a namesake open that lacks the asset, a load reads the bundle it was called on ({lacking.Equal} of {lacking.Requests} equal, {lacking.Different} different, {lacking.Pairs} pairs)");
    }

    /// <summary>Pairs a row run measures (a hand run takes -fb1Pairs 0: every copy of every name).</summary>
    const int DefaultNamesakePairs = 40;
    /// <summary>The (source, bundle path) groups a bounded run opens to find them.</summary>
    const int NamesakeSearchGroups = 400;

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
