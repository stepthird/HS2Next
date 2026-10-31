// The pre-upload texture check in Unity (Runtime/Content/TextureCheck.cs, ContentCatalog.TextureCheck.cs; Phase 5.2 TC2-TC6),
// headless (-nographics). Reports Spike/out/texcheck/texcheck_<part>_report.txt, first line "SUMMARY: N PASS, M FAIL";
// EditorApplication.Exit(1) on a FAIL. Numbers and names only: no image is decoded, shown or written.
//   RunParity (TC2): every bundle of the repair index (Cache/texguard/repaired.tsv) whose copy Tools/texguard_repair.py wrote
//       (LZ4; the check's own copies are uncompressed and are counted apart): the check's patch of the original copy against
//       the repaired copy texture by texture - the same actions, or one of the explained differences (a block-size texture
//       the earlier list did not name is padded; the copy's mip rule raised a healthy texture); with -cross <dir>
//       (Tools/texguard_repair.py --cross-check output) every object of every bundle that run wrote byte-equal.
//   RunLoad (TC3): every bundle whose verdict is patch / patch-grey (-keys <file>: one cache key per line; default the
//       verdict cache's and the repair index's) opened through the catalog (the check patches it, or the repaired copy
//       opens) and every Texture2D of it loaded: 0 mip assertions, 0 "unexpected size" errors, the loaded sizes and mip
//       counts equal the patched headers; scene bundles are opened and closed only.
//   RunCards (TC5): the cards whose S1 baseline reports a texture.badmip (the guard engaged: 28 + 1 at 429c2b6) built with
//       the repaired copies switched off, so the check patches the originals in memory: snapshot = baseline.
//   RunCache (TC6): the verdict cache - a second look is a cache hit, a changed stamp checks again, a corrupted line is
//       ignored and the file rewritten, a new cache reads the file back; a malformed bundle's verdict refuses it.
//   RunQuarantine: the crash quarantine (QC1 marking and its cost, QC2 a marker left behind -> refused, QC3 a stamp change
//       and a clear lift it); -phase crash / after with -userDataDir and -testCrashOnLoad: a real crash and the next start.
// Run: -executeMethod TextureCheckTest.RunParity [-cross <dir>] | RunLoad [-keys <file>] [-limit N] | RunCards [-limit N] | RunCache
//      | RunQuarantine [-phase unit|crash|crashuse|after] [-userDataDir d] [-key k] [-testCrashOnLoad k]
//      | RunMesh [-key k]: the meshes whose last field is missing are completed and every prefab loads (L0e's effects pack rows)
//      | RunCost [-limit N] [-expect cached]: TC7 - first checks and the verdict cache's share while the S1 sample builds
//      | RunPrescanMemory -cacheDir <empty folder> [-limitSec N]: the background check of every bundle into an empty verdict
//        cache, sampling the managed heap (after a full collection every 10 s) and the process's private bytes / working set
//      | RunLoadRaw -key k | -keys <file> | -file f [-asset a | -all] [-patch | -verbatim [-renameStreams]]: bundles loaded
//        straight from memory in this process (run it alone: a bundle the check refuses may end the process)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using HS2Next.Chara;
using HS2Next.Content;
using HS2Next.IO;
using HS2Next.Tests.MakerCore;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class TextureCheckTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "texcheck");
    static StringBuilder rep;
    static int passes, fails;

    static void Reset() { rep = new StringBuilder(); passes = fails = 0; }
    static void Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
    }
    static void Info(string s) => rep.AppendLine("   " + s);
    static void Finish(string part, string title, Stopwatch sw)
    {
        rep.Insert(0, $"SUMMARY: {passes} PASS, {fails} FAIL ({sw.Elapsed.TotalSeconds:F0} s)\n{title}\n");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "texcheck_" + part + "_report.txt"), rep.ToString());
        Debug.Log($"TEXTURE CHECK TEST DONE ({part}): {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }
    static string Arg(string name, string def = null)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }
    static int Limit => int.TryParse(Arg("-limit"), out int n) ? n : int.MaxValue;

    // the repair index: key -> (copy, label, name -> action)
    sealed class IndexRow { public string Key, Copy, Label; public Dictionary<string, string> Actions = new Dictionary<string, string>(StringComparer.Ordinal); }

    static List<IndexRow> ReadIndex(string file)
    {
        var l = new List<IndexRow>();
        if (!File.Exists(file)) return l;
        foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
        {
            if (line.StartsWith("#")) continue;
            var p = line.TrimStart('﻿').Split('\t');
            if (p.Length < 4) continue;
            var r = new IndexRow { Key = p[0], Copy = p[1], Label = p[2] };
            foreach (var t in p[3].Split('|'))
            {
                int eq = t.LastIndexOf('=');
                if (eq > 0) r.Actions[t.Substring(0, eq)] = t.Substring(eq + 1);
            }
            l.Add(r);
        }
        return l;
    }

    static byte[] OriginalBytes(string key, string hs2Root)
    {
        int bang = key.IndexOf('!');
        if (bang >= 0)
        {
            var z = new ZipIndex(key.Substring(0, bang));
            return z.Entries.TryGetValue("abdata/" + key.Substring(bang + 1), out var e) ? z.ReadAll(e) : null;
        }
        int colon = key.IndexOf(':');
        string f = Path.Combine(hs2Root, "abdata", key.Substring(colon + 1));
        return File.Exists(f) ? File.ReadAllBytes(f) : null;
    }

    // every Texture2D of a bundle's serialized files: path id -> (name, facts) and the raw object bytes
    static Dictionary<long, (TextureCheck.Facts facts, byte[] raw)> Textures(byte[] bundle, out Dictionary<long, byte[]> all)
    {
        var b = Hs2BundleFile.FromBytes(bundle, "x", lzma: true);
        var data = b.AllData();
        var r = new Dictionary<long, (TextureCheck.Facts, byte[])>();
        all = new Dictionary<long, byte[]>();
        foreach (var n in b.Nodes)
        {
            if ((n.Flags & 4) == 0 && n.Path.IndexOf('.') >= 0) continue;
            var sf = Hs2SerializedFile.FromBytes(data, (int)n.Offset, (int)n.Size, n.Path);
            foreach (var o in sf.Objects)
            {
                var raw = new byte[o.Size];
                Buffer.BlockCopy(data, (int)(n.Offset + o.Start), raw, 0, raw.Length);
                all[o.PathId] = raw;
                if (o.ClassId != TextureCheck.ClassTexture2D) continue;
                var t = TextureCheck.ReadFacts(sf.TypeTreeOf(o), new Hs2ObjectBytes(data, n.Offset + o.Start, o.Size), true);
                r[o.PathId] = (t, raw);
            }
        }
        return r;
    }

    // ================================================================ TC2

    public static void RunParity()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        string cacheDir = TextureGuard.CacheDir, cross = Arg("-cross");
        var all = ReadIndex(Path.Combine(cacheDir, "repaired.tsv")).Take(Limit).ToList();
        // the copies the Python tool wrote are LZ4; the check writes its own uncompressed
        var index = all.Where(r => { try { return Hs2BundleFile.OpenFile(Path.Combine(cacheDir, r.Copy), 0, new FileInfo(Path.Combine(cacheDir, r.Copy)).Length, r.Key).DataCompression != 0; } catch (Exception) { return true; } }).ToList();
        var crossKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (cross != null && File.Exists(Path.Combine(cross, "crosscheck.tsv")))
            foreach (var l in File.ReadAllLines(Path.Combine(cross, "crosscheck.tsv"))) { var p = l.Split('\t'); if (p.Length > 1 && p[1].Length > 0 && !l.StartsWith("#")) crossKeys.Add(p[0]); }
        string hs2 = CardResolveTest.GameRoot;
        rep.AppendLine($"TC2 parity: {index.Count} bundles of the repair index with the Python tool's copies ({all.Count - index.Count} copies written by the check itself, not compared; {cacheDir}); cross-check copies: {cross ?? "(none)"} ({crossKeys.Count} bundles)");
        int same = 0, explained = 0, differ = 0, failed = 0, texEqual = 0, texExplained = 0, texDiffer = 0, crossEqual = 0, crossDiffer = 0, crossCompared = 0;
        var explainedKinds = new Dictionary<string, int>();
        var pythonRows = new HashSet<IndexRow>(index);
        foreach (var row in all)
        {
            bool python = pythonRows.Contains(row);
            if (!python && !crossKeys.Contains(row.Key)) continue;
            try
            {
                var orig = OriginalBytes(row.Key, hs2);
                if (orig == null) { failed++; Check(row.Label + ": original found", false); continue; }
                var patched = TextureCheck.Patch(Hs2BundleFile.FromBytes(orig, row.Key, lzma: true), out var acts);
                if (patched == null) { differ++; Check(row.Label + ": the check patches it", false, "nothing to patch; the index says " + string.Join("|", row.Actions.Select(kv => kv.Key + "=" + kv.Value))); continue; }
                var mine = Textures(patched, out var mineAll);
                var theirs = python ? Textures(File.ReadAllBytes(Path.Combine(cacheDir, row.Copy)), out _) : mine;
                var csAct = acts.ToDictionary(a => a.PathId, a => a);
                bool bundleExplained = false, bundleDiffer = false;
                foreach (var kv in mine)
                {
                    if (!theirs.TryGetValue(kv.Key, out var py)) { bundleDiffer = true; continue; }
                    if (kv.Value.raw.SequenceEqual(py.raw)) { texEqual++; continue; }
                    var t = kv.Value.facts;
                    csAct.TryGetValue(kv.Key, out var a);
                    string kind = null;
                    // a readable texture whose data are streamed: made not readable here (rules 4), the copy kept the flag
                    if (a != null && a.Action.EndsWith("readable off", StringComparison.Ordinal) && !t.Readable)
                    {
                        string rest = a.Action.Substring(0, a.Action.Length - "readable off".Length).TrimEnd();
                        row.Actions.TryGetValue(t.Name ?? "", out var pr);
                        if (rest.Length == 0 ? (pr == null || pr.StartsWith("mips ", StringComparison.Ordinal)) : rest == pr) kind = "readable off (a readable texture with streamed data)";
                    }
                    // a block-size texture the list did not name: padded here, the copy kept it (or only raised its mip count)
                    if (kind != null) { }
                    else if (a != null && a.Action.StartsWith("pad ", StringComparison.Ordinal) && !row.Actions.TryGetValue(t.Name ?? "", out var pa1)) kind = "pad (not listed)";
                    else if (a != null && a.Action.StartsWith("pad ", StringComparison.Ordinal) && row.Actions.TryGetValue(t.Name ?? "", out var pa2) && pa2.StartsWith("mips ", StringComparison.Ordinal)) kind = "pad (listed for its mip count only)";
                    // the copy's mip rule raised the mip count of a texture without a defect class (the list mode ran it on
                    // every texture of a listed bundle; the check leaves those)
                    else if (a == null && row.Actions.TryGetValue(t.Name ?? "", out var pa3) && pa3.StartsWith("mips ", StringComparison.Ordinal)) kind = "mip count of a healthy texture raised by the copy";
                    if (kind != null) { texExplained++; bundleExplained = true; explainedKinds[kind] = explainedKinds.TryGetValue(kind, out var c) ? c + 1 : 1; Info($"{row.Label}: {t.Name}: {kind} ({a?.Action ?? "no action"}; copy: {(row.Actions.TryGetValue(t.Name ?? "", out var pa) ? pa : "none")})"); }
                    else { texDiffer++; bundleDiffer = true; Check($"{row.Label}: {t.Name} equal to the repaired copy", false, $"check {a?.Action ?? "none"}, copy {(row.Actions.TryGetValue(t.Name ?? "", out var pb) ? pb : "none")}"); }
                }
                if (!python) { }
                else if (bundleDiffer) differ++; else if (bundleExplained) explained++; else same++;
                if (cross != null && crossKeys.Contains(row.Key))
                {
                    crossCompared++;
                    string cp = Path.Combine(cross, TextureRepairCache.Stem(row.Key) + ".unity3d");
                    if (!File.Exists(cp)) { crossDiffer++; Check(row.Label + ": cross-check copy exists", false, cp); }
                    else
                    {
                        Textures(File.ReadAllBytes(cp), out var crossAll);
                        bool eq = crossAll.Count == mineAll.Count && mineAll.All(kv => crossAll.TryGetValue(kv.Key, out var x) && x.SequenceEqual(kv.Value));
                        if (eq) crossEqual++; else { crossDiffer++; Check(row.Label + ": every object equal to the cross-check copy", false); }
                    }
                }
            }
            catch (Exception e) { failed++; Check(row.Label + ": no exception", false, e.GetType().Name + ": " + e.Message); }
            GC.Collect();
        }
        Check("TC2: the check's patch of every indexed bundle equals its repaired copy texture by texture (or an explained difference)",
              differ == 0 && failed == 0 && index.Count > 0,
              $"{same} bundles equal, {explained} with explained differences, {differ} differ, {failed} failed; textures equal {texEqual}, explained {texExplained}, differ {texDiffer}; explained: " +
              string.Join(", ", explainedKinds.Select(kv => kv.Key + " " + kv.Value)));
        if (cross != null) Check("TC2: every object equal to the texguard_repair --cross-check copy", crossDiffer == 0 && crossEqual == crossCompared && crossCompared > 0, $"{crossEqual} equal, {crossDiffer} differ of {crossCompared} bundles in both");
        Finish("parity", "TC2 parity with the Python repair", sw);
    }

    // ================================================================ TC3

    public static void RunLoad()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        var keys = new List<string>();
        string keyFile = Arg("-keys");
        if (keyFile != null) keys.AddRange(File.ReadAllLines(keyFile).Select(l => l.Split('\t')[0]).Where(l => l.Length > 0 && !l.StartsWith("#")));
        else
        {
            string vf = ContentCatalog.TextureVerdicts.FilePath;
            if (vf != null && File.Exists(vf))
                foreach (var l in File.ReadAllLines(vf)) { var v = TextureVerdictCache.Parse(l); if (v != null && v.NeedsPatch) keys.Add(v.Key); }
            keys.AddRange(ReadIndex(Path.Combine(TextureGuard.CacheDir, "repaired.tsv")).Select(r => r.Key));
        }
        keys = keys.Distinct(StringComparer.OrdinalIgnoreCase).Take(Limit).ToList();
        rep.AppendLine($"TC3 headless loads: {keys.Count} bundles ({keyFile ?? "the verdict cache's patch verdicts + the repair index"}); graphics {SystemInfo.graphicsDeviceType}");
        int asserts = 0, sizeErrors = 0;
        var sizeNames = new List<string>();
        var sizeRx = new System.Text.RegularExpressions.Regex(@"Texture '(.+?)' has an unexpected size \(expected \d+, got (\d+)\)");
        Application.LogCallback hook = (msg, st, type) =>
        {
            if (msg == null) return;
            if (type == LogType.Assert && msg.IndexOf("mipLevel", StringComparison.Ordinal) >= 0) Interlocked.Increment(ref asserts);
            else if (type == LogType.Error && msg.IndexOf("has an unexpected size", StringComparison.Ordinal) >= 0)
            {
                Interlocked.Increment(ref sizeErrors);
                var m = sizeRx.Match(msg);
                lock (sizeNames) sizeNames.Add(m.Success ? m.Groups[1].Value + "|" + m.Groups[2].Value : "?");
            }
        };
        Application.logMessageReceivedThreaded += hook;
        bool guardWas = TextureGuard.Enabled;
        int opened = 0, notOpened = 0, scenes = 0, textures = 0, mismatched = 0, n = 0, readableStreamed = 0, otherSize = 0;
        var head = new StringBuilder();
        var catalog = CardResolveTest.BuildDb(head);
        var zips = catalog.ZipmodsInLoadOrder.Concat(catalog.ZipmodsByGuid.Values).GroupBy(z => z.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var key in keys)
            {
                n++;
                int bang = key.IndexOf('!');
                AssetBundle b = null;
                int a0 = Volatile.Read(ref asserts), s0 = Volatile.Read(ref sizeErrors);
                try
                {
                    if (bang < 0) b = catalog.LoadAbdataBundle(key.Substring(key.IndexOf(':') + 1));
                    else if (zips.TryGetValue(key.Substring(0, bang), out var z)) b = catalog.LoadBundle(z, key.Substring(bang + 1), null);
                    if (!b) { notOpened++; Info($"{key}: not opened (outside the loaded zipmod set, or refused)"); continue; }
                    opened++;
                    if (b.isStreamedSceneAssetBundle) { scenes++; continue; }
                    // the headers the open served: the repaired copy's (the check stored it) or the original's
                    var f = catalog.BundleFileOf(b);
                    // the headers of the served bytes by name (a name may name several textures)
                    var heads = new Dictionary<string, List<TextureCheck.Facts>>(StringComparer.Ordinal);
                    if (f != null)
                    {
                        var bytes = f.ReadAll != null ? f.ReadAll() : ReadRegion(f.File, f.Offset, f.Length);
                        foreach (var kv in Textures(bytes, out _))
                        {
                            string nm = kv.Value.facts.Name ?? "";
                            if (!heads.TryGetValue(nm, out var l)) heads[nm] = l = new List<TextureCheck.Facts>();
                            l.Add(kv.Value.facts);
                        }
                    }
                    lock (sizeNames) sizeNames.Clear();
                    foreach (var t in b.LoadAllAssets<Texture2D>())
                    {
                        textures++;
                        if (heads.TryGetValue(t.name, out var hs) && !hs.Any(h => h.W == t.width && h.H == t.height && Math.Max(1, h.Mips) == t.mipmapCount))
                        {
                            mismatched++;
                            Info($"{key}: {t.name}: loaded {t.width}x{t.height} mips {t.mipmapCount}, headers {string.Join(" / ", hs.Select(h => h.W + "x" + h.H + " mips " + h.Mips))}");
                        }
                    }
                    // "unexpected size (expected N, got 0)" of a readable texture with streamed data: the known case the
                    // guard's first layer handles after the load (not a header defect; the check leaves it)
                    lock (sizeNames)
                        foreach (var e in sizeNames)
                        {
                            var p = e.Split('|');
                            bool known = p.Length == 2 && p[1] == "0" && heads.TryGetValue(p[0], out var hs2) && hs2.Any(h => h.Readable && h.Inline == 0 && h.StreamSize > 0);
                            if (known) readableStreamed++;
                            else { otherSize++; Info($"{key}: size error of {e}"); }
                        }
                }
                catch (Exception e) { Check(key + ": no exception", false, e.GetType().Name + ": " + e.Message); }
                finally
                {
                    if (b) catalog.UnloadBundle(b);
                    int da = Volatile.Read(ref asserts) - a0, ds = Volatile.Read(ref sizeErrors) - s0;
                    if (da > 0 || ds > 0) Info($"{key}: {da} mip assertions, {ds} size errors");
                }
                if (n % 20 == 0) { EditorUtility.UnloadUnusedAssetsImmediate(true); GC.Collect(); Debug.Log($"TextureCheckTest TC3: {n}/{keys.Count}, {sw.Elapsed.TotalSeconds:F0} s"); }
            }
        }
        finally
        {
            Application.logMessageReceivedThreaded -= hook;
            TextureGuard.Enabled = guardWas;
            catalog.UnloadAll();
        }
        var st = catalog.TextureCheckStats;
        Info($"check: verified {st.Verified}, cache hits {st.CacheHits}, patched {st.Patched}, refused {st.Refused}, unreadable {st.Unreadable}; findings {catalog.TextureFindings.Count}");
        foreach (var t in catalog.TextureFindings.Where(x => x.Kind != "texture.patched")) Info(t.ToString());
        Check("TC3: every patched bundle loads its textures headless with 0 mip assertions and 0 \"unexpected size\" errors (besides the readable streamed textures' known \"got 0\")",
              asserts == 0 && otherSize == 0 && opened > 0, $"{opened} opened ({scenes} scene bundles opened only), {notOpened} not opened, {textures} textures loaded; mip assertions {asserts}, size errors {sizeErrors} (readable streamed \"got 0\" {readableStreamed}, other {otherSize})");
        Check("TC3: the loaded sizes and mip counts equal the headers of the served bytes", mismatched == 0, $"{mismatched} of {textures} differ");
        Finish("load", "TC3 headless loads of patched bundles", sw);
    }

    static byte[] ReadRegion(string file, long offset, long length)
    {
        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            fs.Position = offset;
            var b = new byte[length];
            int got = 0;
            while (got < b.Length) { int k = fs.Read(b, got, b.Length - got); if (k <= 0) break; got += k; }
            return b;
        }
    }

    // ================================================================ TC5

    public static void RunCards()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        string dir = MakerCoreTest.BaselineDir;
        // the cards whose baseline holds a texture.badmip finding
        var shas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.GetFiles(dir, "*.snap"))
        {
            using (var gz = new System.IO.Compression.GZipStream(File.OpenRead(f), System.IO.Compression.CompressionMode.Decompress))
            using (var r = new StreamReader(gz, Encoding.UTF8))
                if (r.ReadToEnd().IndexOf("texture.badmip", StringComparison.Ordinal) >= 0) shas.Add(Path.GetFileNameWithoutExtension(f));
        }
        var cards = new List<(string file, string sha)>();
        foreach (var l in File.ReadAllLines(Path.Combine(dir, "index.tsv")))
        {
            if (l.StartsWith("#")) continue;
            var p = l.Split('\t');
            if (p.Length > 1 && shas.Contains(p[1])) cards.Add((Path.Combine(TestTargets.UserData, p[0]), p[1]));
        }
        cards = cards.Take(Limit).ToList();
        rep.AppendLine($"TC5: {cards.Count} cards whose baseline reports texture.badmip ({shas.Count} baselines), built with the repaired copies off (the check patches the originals in memory)");
        bool was = TextureGuard.UseRepairedBundles;
        int equal = 0, differ = 0, failed = 0, n = 0;
        TextureGuard.UseRepairedBundles = false;
        try
        {
            using (var h = MakerCoreTest.Harness.Open())
            {
                foreach (var (file, sha) in cards)
                {
                    n++;
                    MakerCoreTest.Built b = null;
                    try
                    {
                        b = h.Build(file);
                        if (!b.Ok) { failed++; Check(Path.GetFileName(file) + ": built", false, b.Error); continue; }
                        var s = b.Snapshot();
                        var basis = MakerCoreTest.LoadBaseline(b.CardSha);
                        if (basis == null) { failed++; Check(sha.Substring(0, 12) + ": baseline", false); continue; }
                        var d = CharaSnapshot.Compare(basis, s, 20);
                        if (d.Equal) { equal++; Info($"{sha.Substring(0, 12)}: equal"); }
                        else { differ++; Check($"{sha.Substring(0, 12)}: snapshot = baseline with the check's in-memory patches", false, d.ToString()); }
                    }
                    catch (Exception e) { failed++; Check(sha.Substring(0, 12) + ": no exception", false, e.GetType().Name + ": " + e.Message); }
                    finally { b?.Dispose(); }
                    if (n % 10 == 0) h.ReleaseBundles();
                }
                var st = h.Catalog.TextureCheckStats;
                Info($"check: verified {st.Verified}, cache hits {st.CacheHits}, patched {st.Patched} (in memory), refused {st.Refused}, unreadable {st.Unreadable}");
            }
        }
        finally { TextureGuard.UseRepairedBundles = was; }
        Check("TC5: the guard-engaged cards build the same S1 snapshot with the in-process check as with the repaired copies",
              differ == 0 && failed == 0 && equal > 0, $"{equal} equal, {differ} differ, {failed} failed of {cards.Count}");
        Finish("cards", "TC5 guard-engaged cards: in-process check vs repaired copies", sw);
    }

    // ================================================================ TC6

    public static void RunCache()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        string keepRoot = TextureGuard.CacheRoot;
        string tmp = Path.Combine(Path.GetTempPath(), "hs2next_tc6_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(tmp);
        rep.AppendLine("TC6 verdict cache (a temporary cache folder; the fixtures of Editor/Fixtures/TextureCheck)");
        try
        {
            TextureGuard.CacheRoot = tmp;
            string fx = Path.Combine(ContentSettings.ProjectRoot, "Assets", "HS2Next", "Editor", "Fixtures", "TextureCheck");
            string file = Path.Combine(tmp, "mip0.unity3d");
            File.Copy(Path.Combine(fx, "tc_mip0.unity3d"), file);
            BundleFileRef Ref()
            {
                var fi = new FileInfo(file);
                return new BundleFileRef { Key = "tc6:mip0", File = file, Offset = 0, Length = fi.Length, Stamp = fi.Length + ":" + fi.LastWriteTimeUtc.Ticks };
            }
            var v1 = ContentCatalog.TextureVerdictOf(Ref());
            var v2 = ContentCatalog.TextureVerdictOf(Ref());
            Check("a first look checks the bundle (patch), a second is the cached verdict", v1 != null && v1.Kind == TextureVerdictKind.Patch && !v1.Cached && ReferenceEquals(v1, v2), v1?.ToString());
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));
            var v3 = ContentCatalog.TextureVerdictOf(Ref());
            Check("a changed stamp checks the bundle again", v3 != null && !ReferenceEquals(v1, v3) && v3.Stamp != v1.Stamp && v3.Kind == TextureVerdictKind.Patch);
            string vf = ContentCatalog.TextureVerdicts.FilePath;
            int lines = File.ReadAllLines(vf).Count(l => !l.StartsWith("#"));
            Check("each check appends one line to the verdict file", lines == 2, $"{lines} lines in {vf}");
            File.AppendAllText(vf, "not a verdict line\n");
            TextureGuard.CacheRoot = keepRoot;   // another folder, then back: the file is read again
            var unused = ContentCatalog.TextureVerdicts;
            TextureGuard.CacheRoot = tmp;
            var c = ContentCatalog.TextureVerdicts;
            Check("a new cache reads the file: the corrupted line ignored, the file rewritten without it",
                  c.Ignored == 1 && c.Loaded == 2 && !File.ReadAllLines(vf).Any(l => l.StartsWith("not a verdict")) && c.TryGet("tc6:mip0", Ref().Stamp, out var back) && back.Cached,
                  $"loaded {c.Loaded}, ignored {c.Ignored}");
            // the malformed fixture: refused
            string mal = Path.Combine(tmp, "malformed.unity3d");
            File.Copy(Path.Combine(fx, "tc_malformed.unity3d"), mal);
            var fi2 = new FileInfo(mal);
            var vm = ContentCatalog.TextureVerdictOf(new BundleFileRef { Key = "tc6:malformed", File = mal, Offset = 0, Length = fi2.Length, Stamp = "m" });
            Check("a malformed bundle's verdict is malformed (the catalog refuses it)", vm.Kind == TextureVerdictKind.Malformed, vm.ToString());
            // the repair cache layout the guard reads back
            var patched = TextureCheck.Patch(Hs2BundleFile.OpenFile(file, 0, new FileInfo(file).Length, "tc6"), out var acts);
            string copy = TextureRepairCache.Store(tmp, "tc6:mip0", "tc6:mip0.unity3d", patched, string.Join("|", acts.Select(a => a.Texture + "=" + a.Action)));
            TextureGuard.ReloadIndex();
            // the index keeps the copy's path relative with '/' (as the repair tool writes it): compared as full paths
            string served = TextureGuard.RepairedPath("tc6:mip0");
            Check("a stored patch is the guard's repaired copy of its key",
                  copy != null && served != null && string.Equals(Path.GetFullPath(served), Path.GetFullPath(copy), StringComparison.OrdinalIgnoreCase),
                  $"stored {copy}; the guard serves {served ?? "(none)"}");
        }
        catch (Exception e) { Check("no exception", false, e.GetType().Name + ": " + e.Message + " " + e.StackTrace); }
        finally
        {
            TextureGuard.CacheRoot = keepRoot;
            try { Directory.Delete(tmp, true); } catch (IOException) { }
        }
        Finish("cache", "TC6 verdict cache", sw);
    }

    // ================================================================ the crash quarantine (QC1-QC3; SW9's Editor part)

    // a zipmod bundle of the loaded set holding a texture: (zipmod, bundle path, texture name)
    static (ZipmodInfo z, string path, string tex) PickBundle(ContentCatalog cat, string want)
    {
        foreach (var z in cat.ZipmodsInLoadOrder)
            foreach (var bp in z.BundlePaths)
            {
                string key = z.Path + "!" + bp;
                if (want != null && !key.EndsWith(want, StringComparison.OrdinalIgnoreCase) && !string.Equals(key, want, StringComparison.OrdinalIgnoreCase)) continue;
                var f = cat.BundleFileOfKey(key);
                if (f == null || f.ReadAll != null || f.Length > 8 << 20) continue;
                try
                {
                    var b = Hs2BundleFile.OpenFile(f.File, f.Offset, f.Length, key);
                    // not LZMA, not a scene bundle (its serialized files are named BuildPlayer-<scene>)
                    if (b.DataCompression == 1 || b.Nodes.Any(nd => nd.Path.StartsWith("BuildPlayer-", StringComparison.Ordinal))) continue;
                    var tex = Textures(ReadRegion(f.File, f.Offset, f.Length), out _).Values.Select(v => v.facts.Name).FirstOrDefault(n => !string.IsNullOrEmpty(n));
                    if (tex != null) return (z, bp, tex);
                }
                catch (Exception) { }
            }
        return (null, null, null);
    }

    /// <summary>
    /// -phase unit (default): the quarantine in one process - the first asset load of a zipmod bundle is marked and the mark
    /// stays for the first use of what it returned until the frame ends (QC1, with the cost of a marker write), a marker left
    /// behind becomes an entry at the next start and the bundle is refused with content.quarantined (QC2), a changed stamp
    /// lifts it, a clear lifts it (QC3).
    /// -phase crash -userDataDir d [-key k]: loads with -testCrashOnLoad k (the process crashes at the load); -phase crashuse
    /// -userDataDir d [-key k]: the load returns, then the process crashes while it uses the asset (as an item's realization
    /// from a freshly loaded prefab can); -phase after -userDataDir d -key k: the next start quarantines k and refuses it (SW9,
    /// Editor).
    /// </summary>
    public static void RunQuarantine()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        string phase = Arg("-phase", "unit");
        string ud = Arg("-userDataDir") ?? Path.Combine(Path.GetTempPath(), "hs2next_qc_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        rep.AppendLine($"crash quarantine, phase {phase}, user folder {ud}");
        var cat = CardResolveTest.BuildDb(new StringBuilder());
        try
        {
            var (z, path, tex) = PickBundle(cat, Arg("-key"));
            Check("a zipmod bundle with a texture to load", z != null, z != null ? z.Path + "!" + path + " / " + tex : "none");
            if (z == null) { Finish("quarantine_" + phase, "crash quarantine", sw); return; }
            string key = z.Path + "!" + path;
            if (phase == "crash")
            {
                cat.EnableQuarantine(ud);
                var b = cat.LoadBundle(z, path, null);
                Info("loading " + tex + " with -testCrashOnLoad (the process should end here)");
                File.WriteAllText(Path.Combine(OutDirEnsured(), "texcheck_quarantine_crash_key.txt"), key);
                cat.LoadAssetFrom<Texture2D>(b, tex, z, path);
                Check("the process crashed at the load", false, "it did not (is -testCrashOnLoad given and matching?)");
                Finish("quarantine_crash", "crash quarantine: the crash run", sw);
                return;
            }
            if (phase == "crashuse")
            {
                // the first use of what the load returned, in the same frame: a native crash there must name the bundle too
                cat.EnableQuarantine(ud);
                var b = cat.LoadBundle(z, path, null);
                File.WriteAllText(Path.Combine(OutDirEnsured(), "texcheck_quarantine_crash_key.txt"), key);
                var t0 = cat.LoadAssetFrom<Texture2D>(b, tex, z, path);
                Info($"loaded {tex} ({(t0 ? t0.width + "x" + t0.height : "null")}); the mark is {(cat.Quarantine.Armed ? "armed" : "clear")}; crashing while it is used");
                UnityEngine.Diagnostics.Utils.ForceCrash(UnityEngine.Diagnostics.ForcedCrashCategory.AccessViolation);
                Check("the process crashed while it used the asset", false, "it did not");
                Finish("quarantine_crashuse", "crash quarantine: a crash in the first use after the load", sw);
                return;
            }
            if (phase == "after")
            {
                var prev = cat.EnableQuarantine(ud);
                Check("the marker of the crashed run became an entry at this start", prev != null && string.Equals(prev.Key, key, StringComparison.OrdinalIgnoreCase), prev?.ToString() ?? "none");
                var b = cat.LoadBundle(z, path, null);
                Check("a load of the quarantined bundle is refused (null) with content.quarantined", !b && cat.TextureFindings.Any(f => f.Kind == "content.quarantined"), string.Join("; ", cat.TextureFindings.Select(f => f.ToString())));
                Check("content.quarantine clears it", cat.ClearQuarantine(CrashQuarantine.ShortKey(key)) && cat.Quarantine.Entries.Count == 0);
                var b2 = cat.LoadBundle(z, path, null);
                Check("after the clear the bundle opens again", b2);
                if (b2) cat.UnloadBundle(b2);
                Finish("quarantine_after", "crash quarantine: the start after the crash (SW9, Editor)", sw);
                return;
            }
            // QC1: marking
            cat.EnableQuarantine(ud);
            var q = cat.Quarantine;
            Check("the quarantine opens its folder", q != null && File.Exists(q.MarkerFile), ud);
            var bundle = cat.LoadBundle(z, path, null);
            var t = cat.LoadAssetFrom<Texture2D>(bundle, tex, z, path);
            bool armedAfterLoad = q.Armed && !MarkerClear(q.MarkerFile);
            cat.EndFirstUseMark();   // what the end of the frame does
            Check("QC1: the first asset load of a zipmod bundle writes the marker; the mark stays for its first use and the end of the frame clears it",
                  q.Writes == 1 && armedAfterLoad && !q.Armed && MarkerClear(q.MarkerFile), $"writes {q.Writes}, armed after the load {armedAfterLoad}");
            cat.LoadAssetFrom<Texture2D>(bundle, tex, z, path);
            Check("QC1: a second load from the same bundle writes nothing", q.Writes == 1);
            // the cost of a marker write: the first loads of more bundles
            var keys = cat.ZipmodsInLoadOrder.Where(x => x.BundlePaths.Count > 0 && x.BundlePaths.First().IndexOf("map", StringComparison.OrdinalIgnoreCase) < 0)
                .Select(x => (x, x.BundlePaths.First())).Where(t => (cat.BundleFileOfKey(t.x.Path + "!" + t.Item2)?.Length ?? long.MaxValue) < (4 << 20)).Take(60).ToList();
            int before = q.Writes;
            double ms0 = q.WriteMs;
            foreach (var (zz, pp) in keys)
            {
                var bb = cat.LoadBundle(zz, pp, null);
                if (!bb) continue;
                if (bb.isStreamedSceneAssetBundle) { cat.UnloadBundle(bb); continue; }
                var names = bb.GetAllAssetNames();
                if (names.Length > 0) cat.LoadAssetFrom<Object>(bb, Path.GetFileNameWithoutExtension(names[0]), zz, pp);
                cat.UnloadBundle(bb);
            }
            int n = q.Writes - before;
            Info($"marker writes {n}, mean {(n > 0 ? (q.WriteMs - ms0) / n : 0):F3} ms, median {q.MedianWriteMs:F3} ms");
            Check("TC7 (quarantine): a marker write costs <= 0.2 ms (median)", n > 0 && q.MedianWriteMs <= 0.2, $"{n} writes, median {q.MedianWriteMs:F3} ms, mean {(n > 0 ? (q.WriteMs - ms0) / n : 0):F3} ms");
            cat.UnloadBundle(bundle);
            // QC2: a marker left behind (a process that died while loading) -> an entry at the next start, refused
            cat.DisableQuarantine();
            File.WriteAllText(Path.Combine(ud, "content", CrashQuarantine.MarkerName), key + "\t" + new FileInfo(z.Path).Length + ":" + new FileInfo(z.Path).LastWriteTimeUtc.Ticks + "\n");
            var last = cat.EnableQuarantine(ud);
            Check("QC2: a marker left behind becomes an entry at the next start", last != null && last.Key == key, last?.ToString());
            Check("QC2: the bundle is refused with content.quarantined", !cat.LoadBundle(z, path, null) && cat.TextureFindings.Any(f => f.Kind == "content.quarantined"));
            var x2 = cat.StatsExtra();
            Check("content.stats lists the entry", x2.Quarantined.Count == 1 && x2.Quarantined[0].Key == key);
            // QC3: a changed stamp lifts the entry; a clear lifts it
            cat.DisableQuarantine();
            string list = Path.Combine(ud, "content", CrashQuarantine.ListName);
            File.WriteAllText(list, File.ReadAllText(list).Replace(new FileInfo(z.Path).Length + ":", "1:"));
            cat.EnableQuarantine(ud);
            var b3 = cat.LoadBundle(z, path, null);
            Check("QC3: a changed stamp (the mod was updated) lifts the entry", b3 && cat.Quarantine.Entries.Count == 0);
            if (b3) cat.UnloadBundle(b3);
            Check("QC3: a base-game bundle is never quarantined", !CrashQuarantine.IsZipmodKey("hs2-install:chara/oo_base.unity3d"));
            cat.DisableQuarantine();
            Check("the marker is clear after a normal end", MarkerClear(Path.Combine(ud, "content", CrashQuarantine.MarkerName)));
        }
        catch (Exception e) { Check("no exception", false, e.GetType().Name + ": " + e.Message + " " + e.StackTrace); }
        finally
        {
            cat.DisableQuarantine();
            cat.UnloadAll();
            if (phase == "unit" && Arg("-userDataDir") == null) try { Directory.Delete(ud, true); } catch (IOException) { }
        }
        Finish("quarantine_" + phase, "crash quarantine", sw);
    }

    static string OutDirEnsured() { Directory.CreateDirectory(OutDir); return OutDir; }

    // a marker names no load in flight: empty, or its first byte zero
    static bool MarkerClear(string f)
    {
        using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            return fs.Length == 0 || fs.ReadByte() == 0;
    }

    // ================================================================ assets that need a truncated mesh (the fix of L0e)

    /// <summary>
    /// RunMesh [-key &lt;bundle path suffix&gt;]: a bundle whose meshes end where their last field (the streamed-data reference)
    /// begins: the verdict completes them (patch "mesh tail +N"), refuses no asset, and every prefab loads through the
    /// catalog's patched copy without a crash or an error, its meshes with vertices, indices inside the vertex count and the
    /// counts the original holds. Runs on an isolated cache root (no repaired copy of the shared cache, an empty verdict
    /// cache), so the verdict is the original's. Run it alone (the original's meshes end the process).
    /// </summary>
    public static void RunMesh()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        string want = Arg("-key", "studio/Aizen53/effects_pack1_1.unity3d");
        var cat = CardResolveTest.BuildDb(new StringBuilder());
        // an isolated cache root: the shared cache may hold a repaired copy of the bundle (an earlier open stored it), whose
        // verdict is ok; the original's verdict is the one this row checks
        string keepRoot = TextureGuard.CacheRoot;
        string isolated = Path.Combine(Path.GetTempPath(), "hs2next_mesh_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(isolated);
        TextureGuard.CacheRoot = isolated;
        Info("isolated cache root (no repaired copies, an empty verdict cache): " + isolated);
        int errors = 0;
        var firstErrors = new List<string>();
        Application.LogCallback hook = (msg, st, type) =>
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            Interlocked.Increment(ref errors);
            lock (firstErrors) if (firstErrors.Count < 8) firstErrors.Add(msg?.Length > 200 ? msg.Substring(0, 200) : msg);
        };
        Application.logMessageReceivedThreaded += hook;
        try
        {
            ZipmodInfo z = null;
            string path = null;
            foreach (var x in cat.ZipmodsInLoadOrder)
                foreach (var bp in x.BundlePaths)
                    if ((x.Path + "!" + bp).EndsWith(want, StringComparison.OrdinalIgnoreCase)) { z = x; path = bp; }
            Check("the bundle is in the loaded set", z != null, want);
            if (z == null) { Finish("mesh", "meshes completed by the patch", sw); return; }
            string key = z.Path + "!" + path;
            var f = cat.BundleFileOfKey(key);
            var vsw = Stopwatch.StartNew();
            var v = TextureCheck.Analyze(f);
            Info($"verdict {v} in {vsw.Elapsed.TotalMilliseconds:F1} ms");
            int tails = v.Defects.Count(d => d.Action.StartsWith("mesh tail +", StringComparison.Ordinal));
            Check("the verdict completes the meshes whose last fields are missing and refuses no asset", tails > 0 && v.TruncatedMeshes == 0 && v.RefusedAssets.Count == 0,
                  $"{tails} meshes completed, {v.TruncatedMeshes} cut inside a field, {v.RefusedAssets.Count} assets refused");
            var b = cat.LoadBundle(z, path, null);
            Check("the bundle opens (the patched copy)", b);
            if (!b) { Finish("mesh", "meshes completed by the patch", sw); return; }
            var prefabs = b.GetAllAssetNames().Where(n => n.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)).ToList();
            int loaded = 0, wrong = 0, meshes = 0, insane = 0;
            foreach (var n in prefabs)
            {
                Debug.Log("[texcheck] loading " + n + " from the patched copy");
                var go = cat.LoadAssetFrom<GameObject>(b, Path.GetFileNameWithoutExtension(n), z, path);
                if (!go) { wrong++; Info(n + ": null"); continue; }
                loaded++;
                Info(Describe(n, go));
                foreach (var m in MeshesOf(go)) { meshes++; if (!SaneMesh(m)) insane++; }
            }
            Check("every prefab loads from the patched copy (the process survived)", wrong == 0 && loaded == prefabs.Count, $"{prefabs.Count} prefabs: {loaded} loaded, {wrong} null");
            Check("their meshes have vertices, and indices inside the vertex count", meshes > 0 && insane == 0, $"{meshes} meshes, {insane} not sane");
            int findings = cat.TextureFindings.Count(t => t.Kind == "content.meshTruncated");
            Check("no content.meshTruncated finding", findings == 0, findings.ToString());
            cat.UnloadBundle(b);
        }
        catch (Exception e) { Check("no exception", false, e.GetType().Name + ": " + e.Message + " " + e.StackTrace); }
        finally
        {
            Application.logMessageReceivedThreaded -= hook;
            cat.UnloadAll();
            TextureGuard.CacheRoot = keepRoot;
            try
            {
                if (Directory.Exists(isolated) && new DirectoryInfo(isolated).GetFileSystemInfos("*", SearchOption.AllDirectories).All(f => (f.Attributes & FileAttributes.ReparsePoint) == 0))
                    Directory.Delete(isolated, true);
            }
            catch (Exception) { }
        }
        Info($"Unity logged {errors} errors / assertions" + (firstErrors.Count > 0 ? ": " + string.Join(" | ", firstErrors) : ""));
        Check("no error or assertion during the loads", errors == 0, errors.ToString());
        Finish("mesh", "meshes completed by the patch (L0e)", sw);
    }

    static IEnumerable<Mesh> MeshesOf(GameObject go)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            Mesh m = null;
            if (r is ParticleSystemRenderer pr) m = pr.mesh;
            else if (r is SkinnedMeshRenderer smr) m = smr.sharedMesh;
            else { var mf = r.GetComponent<MeshFilter>(); if (mf) m = mf.sharedMesh; }
            if (m) yield return m;
        }
    }

    // vertices, an index count per sub-mesh, and (readable meshes) every index inside the vertex count
    static bool SaneMesh(Mesh m)
    {
        if (m.vertexCount <= 0 || m.subMeshCount <= 0) return false;
        for (int i = 0; i < m.subMeshCount; i++)
        {
            if (m.GetIndexCount(i) == 0) return false;
            if (m.isReadable && m.GetIndices(i).Any(x => x < 0 || x >= m.vertexCount)) return false;
        }
        return true;
    }

    // ================================================================ the background check's memory

    /// <summary>
    /// RunPrescanMemory -cacheDir &lt;empty folder&gt; [-limitSec N]: builds the catalog, points the content cache at the given
    /// folder (an empty verdict cache: every bundle is checked) and runs the background check alone. It samples, every
    /// second, the process's private bytes and working set and the managed heap; every 10 s it also measures the managed
    /// heap after a full collection. Reports the peaks above the start and the state after the scan. Checks the bounds of
    /// the background check: at most 200 MB of managed heap (after a full collection) and 500 MB of private bytes.
    /// </summary>
    public static void RunPrescanMemory()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        string dir = Arg("-cacheDir");
        int limitSec = int.Parse(Arg("-limitSec", "1500"));
        Check("an empty cache folder is given", dir != null && (!Directory.Exists(dir) || !Directory.EnumerateFileSystemEntries(dir).Any()), dir ?? "(none)");
        if (dir == null) { Finish("prescanmem", "the background check's memory", sw); return; }
        var cat = CardResolveTest.BuildDb(new StringBuilder());
        string before = TextureGuard.CacheRoot;
        try
        {
            TextureGuard.CacheRoot = dir;
            long heap0 = GC.GetTotalMemory(true);
            var (priv0, ws0) = ProcessMemory();
            long heapPeak = heap0, heapFullPeak = heap0, privPeak = priv0, wsPeak = ws0;
            var t = Stopwatch.StartNew();
            cat.StartTexturePrescan();
            int n = 0;
            while (cat.PrescanRunning && t.Elapsed.TotalSeconds < limitSec)
            {
                Thread.Sleep(1000);
                n++;
                var (privNow, wsNow) = ProcessMemory();
                privPeak = Math.Max(privPeak, privNow);
                wsPeak = Math.Max(wsPeak, wsNow);
                heapPeak = Math.Max(heapPeak, GC.GetTotalMemory(false));
                if (n % 10 == 0) heapFullPeak = Math.Max(heapFullPeak, GC.GetTotalMemory(true));
                if (n % 60 == 0) Info($"   {t.Elapsed.TotalSeconds:F0} s: {cat.TextureCheckStats.PrescanDone} of {cat.TextureCheckStats.PrescanTotal}; private +{(privNow - priv0) >> 20} MB, heap {GC.GetTotalMemory(false) >> 20} MB");
            }
            bool finished = !cat.PrescanRunning;
            if (!finished)
            {
                cat.StopTexturePrescan();
                for (int i = 0; i < 60 && cat.PrescanRunning; i++) Thread.Sleep(500);
            }
            double secs = t.Elapsed.TotalSeconds;
            long heapAfter = GC.GetTotalMemory(true);
            long privAfter = ProcessMemory().priv;
            string vf = Path.Combine(dir, "texcheck", "verdicts-v1.tsv");
            int verdicts = File.Exists(vf) ? File.ReadLines(vf).Count(l => !l.StartsWith("#")) : 0;
            var cs = cat.TextureCheckStats;
            Info($"background check: {(finished ? "done" : "stopped at the bound")} in {secs:F0} s; {cs.PrescanDone} of {cs.PrescanTotal} entries; {verdicts} verdicts written; " +
                 $"{cs.PrescanChecked} bundles checked, {cs.PrescanNotBundles} entries not bundles, {cs.PrescanLeft} left to the check at their open");
            Info($"managed heap: start {heap0 >> 20} MB, peak sampled +{(heapPeak - heap0) >> 20} MB, peak after a full collection +{(heapFullPeak - heap0) >> 20} MB, after the scan +{(heapAfter - heap0) >> 20} MB");
            Info($"private bytes: start {priv0 >> 20} MB, peak +{(privPeak - priv0) >> 20} MB, after +{(privAfter - priv0) >> 20} MB; working set: start {ws0 >> 20} MB, peak +{(wsPeak - ws0) >> 20} MB");
            Check("the background check ended within the bound", finished, $"{secs:F0} s of {limitSec}");
            Check("it adds at most 200 MB to the managed heap (after a full collection)", heapFullPeak - heap0 <= 200L << 20, $"+{(heapFullPeak - heap0) >> 20} MB");
            Check("it adds at most 500 MB of private bytes at its peak", privPeak - priv0 <= 500L << 20, $"+{(privPeak - priv0) >> 20} MB");
        }
        catch (Exception e) { Check("no exception", false, e.GetType().Name + ": " + e.Message); }
        finally
        {
            cat.StopTexturePrescan();
            TextureGuard.CacheRoot = before;
            cat.UnloadAll();
        }
        Finish("prescanmem", "the background check's memory", sw);
    }

    // the process's private bytes and working set (Process.PrivateMemorySize64 reads 0 under the Editor's Mono)
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct ProcessMemoryCounters
    {
        public uint cb, PageFaultCount;
        public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage, QuotaPeakNonPagedPoolUsage,
            QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage;
    }

    [System.Runtime.InteropServices.DllImport("psapi.dll")]
    static extern bool GetProcessMemoryInfo(IntPtr process, out ProcessMemoryCounters counters, uint size);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    static (long priv, long ws) ProcessMemory()
    {
        try
        {
            if (GetProcessMemoryInfo(GetCurrentProcess(), out var c, (uint)System.Runtime.InteropServices.Marshal.SizeOf<ProcessMemoryCounters>()))
                return ((long)c.PrivateUsage.ToUInt64(), (long)c.WorkingSetSize.ToUInt64());
        }
        catch (Exception) { }
        return (0, 0);
    }

    // ================================================================ TC7 (the check's cost in Unity)

    /// <summary>
    /// RunCost [-limit N]: builds the S1 sample (the visual targets + 20 cards) through one catalog and reports the texture
    /// check's numbers: first checks (computed here) with their median / p95 ms, cache hits, and the share of opens the
    /// verdict cache served. Run it twice: the second run's share is TC7's "&gt;= 99 % from the cache in a second session".
    /// </summary>
    public static void RunCost()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        var cards = MakerCoreTest.SampleCards().Take(Limit).ToList();
        var cache = ContentCatalog.TextureVerdicts;
        int before = File.Exists(cache.FilePath) ? File.ReadAllLines(cache.FilePath).Length : 0;
        rep.AppendLine($"TC7: {cards.Count} cards of the S1 sample built through one catalog; verdict cache {cache.FilePath} ({before} lines at the start)");
        int built = 0;
        TextureCheckCounts st;
        using (var h = MakerCoreTest.Harness.Open())
        {
            foreach (var file in cards)
            {
                MakerCoreTest.Built b = null;
                try { b = h.Build(file); if (b.Ok) built++; }
                catch (Exception e) { Info(Path.GetFileName(file) + ": " + e.GetType().Name); }
                finally { b?.Dispose(); }
            }
            st = h.Catalog.TextureCheckStats;
        }
        var lines = File.Exists(cache.FilePath) ? File.ReadAllLines(cache.FilePath).Skip(before).Select(TextureVerdictCache.Parse).Where(v => v != null).ToList() : new List<TextureVerdict>();
        var ms = lines.Select(v => v.Ms).OrderBy(x => x).ToList();
        double Pct(double p) => ms.Count == 0 ? 0 : ms[Math.Min(ms.Count - 1, (int)(p * ms.Count))];
        int opens = st.Verified + st.CacheHits;
        double share = opens == 0 ? 0 : 100.0 * st.CacheHits / opens;
        Info($"built {built} of {cards.Count}; opens checked {opens}: computed {st.Verified}, from the cache {st.CacheHits} ({share:F1} %); patched {st.Patched}, refused {st.Refused}, unreadable {st.Unreadable}");
        Info($"first checks this run: {ms.Count} verdicts, median {Pct(0.5):F2} ms, p95 {Pct(0.95):F2} ms, max {(ms.Count > 0 ? ms[ms.Count - 1] : 0):F1} ms");
        Check("TC7: the cards were built", built > 0, $"{built} of {cards.Count}");
        string which = Arg("-expect", "any");
        if (which == "cached") Check("TC7: >= 99 % of the opens served from the verdict cache (a second session)", share >= 99.0, $"{share:F1} %");
        Finish("cost", "TC7 the check's cost", sw);
    }

    // ================================================================ readable textures with streamed data (W's sweep, round 4)

    // the bundle with every readable streamed texture rewritten: mode "readable" clears the readable flag, "inline" moves the
    // streamed bytes into the object (stream info cleared); null when the bundle has none
    static byte[] RewriteReadableStreamed(byte[] bundle, string mode, List<string> names)
    {
        var b = Hs2BundleFile.FromBytes(bundle, "x", lzma: true);
        var data = b.AllData();
        var replace = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var n in b.Nodes)
        {
            if ((n.Flags & 4) == 0 && n.Path.IndexOf('.') >= 0) continue;
            int at = (int)n.Offset;
            var sf = Hs2SerializedFile.FromBytes(data, at, (int)n.Size, n.Path);
            var objs = new Dictionary<long, byte[]>();
            foreach (var o in sf.Objects)
            {
                if (o.ClassId != TextureCheck.ClassTexture2D) continue;
                var tree = sf.TypeTreeOf(o);
                var r = new Hs2ObjectBytes(data, at + o.Start, o.Size);
                var t = TextureCheck.ReadFacts(tree, r, true);
                if (!t.Readable || t.Inline > 0 || t.StreamSize <= 0) continue;
                names.Add(t.Name);
                var set = new Dictionary<string, object>(StringComparer.Ordinal);
                if (mode == "readable") set["m_IsReadable"] = 0L;
                else
                {
                    string file = (t.StreamPath ?? "").Substring((t.StreamPath ?? "").LastIndexOf('/') + 1);
                    var res = b.Find(file).Value;
                    var bytes = new byte[t.StreamSize];
                    Buffer.BlockCopy(data, (int)(res.Offset + t.StreamOffset), bytes, 0, bytes.Length);
                    set["image data"] = bytes;
                    set["m_StreamData/offset"] = 0L;
                    set["m_StreamData/size"] = 0L;
                    set["m_StreamData/path"] = "";
                }
                objs[o.PathId] = Hs2TypeTree.Rewrite(tree, r, set);
            }
            if (objs.Count > 0 && sf.WithObjects(data, at, objs) is byte[] grown) replace[n.Path] = grown;
        }
        return names.Count == 0 ? null : b.WithNodes(data, replace);
    }

    /// <summary>
    /// RunReadable -keys &lt;file&gt;: every readable texture whose data are streamed, loaded headless from the original bundle and
    /// from two rewrites (readable flag cleared; data moved inline): Unity's "unexpected size (expected N, got 0)" errors and the
    /// loaded size / mip count / readability per variant (the rule for the check comes from this).
    /// </summary>
    public static void RunReadable()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        var keys = File.ReadAllLines(Arg("-keys")).Select(l => l.Split('\t')[0]).Where(l => l.Length > 0 && !l.StartsWith("#")).Distinct().ToList();
        rep.AppendLine($"readable streamed textures: {keys.Count} bundles, variants original / readable off / inline");
        int got0 = 0;
        Application.LogCallback hook = (msg, st, type) => { if (type == LogType.Error && msg != null && msg.IndexOf("has an unexpected size", StringComparison.Ordinal) >= 0) Interlocked.Increment(ref got0); };
        Application.logMessageReceivedThreaded += hook;
        var totals = new Dictionary<string, int>();
        try
        {
            foreach (var key in keys)
            {
                var orig = OriginalBytes(key, CardResolveTest.GameRoot);
                foreach (var mode in new[] { "original", "readable", "inline" })
                {
                    var names = new List<string>();
                    byte[] bytes = mode == "original" ? orig : RewriteReadableStreamed(orig, mode, names);
                    if (mode == "original") RewriteReadableStreamed(orig, "readable", names);
                    if (bytes == null || names.Count == 0) continue;
                    var ab = AssetBundle.LoadFromMemory(bytes);
                    if (!ab) { Check($"{key} [{mode}] opens", false); continue; }
                    int e0 = Volatile.Read(ref got0);
                    var info = new List<string>();
                    foreach (var name in names.Distinct())
                    {
                        var tex = ab.LoadAsset<Texture2D>(name);
                        info.Add(tex ? $"{name} {tex.width}x{tex.height} mips {tex.mipmapCount} readable {tex.isReadable}" : name + " null");
                    }
                    int e = Volatile.Read(ref got0) - e0;
                    totals[mode] = (totals.TryGetValue(mode, out var x) ? x : 0) + e;
                    Info($"{CrashQuarantine.ShortKey(key)} [{mode}]: {names.Count} textures, {e} size errors; {string.Join("; ", info)}");
                    ab.Unload(true);
                }
                EditorUtility.UnloadUnusedAssetsImmediate(true);
            }
        }
        finally { Application.logMessageReceivedThreaded -= hook; }
        Info("size errors per variant: " + string.Join(", ", totals.Select(kv => kv.Key + " " + kv.Value)));
        Check("the original bundles reproduce the \"got 0\" errors", totals.TryGetValue("original", out var a0) && a0 > 0, a0.ToString());
        Check("a rewrite loads every texture without a size error", (totals.TryGetValue("readable", out var a1) && a1 == 0) || (totals.TryGetValue("inline", out var a2) && a2 == 0),
              "readable off " + (totals.TryGetValue("readable", out var b1) ? b1 : -1) + ", inline " + (totals.TryGetValue("inline", out var b2) ? b2 : -1));
        Finish("readable", "readable textures with streamed data: which rewrite loads them", sw);
    }

    /// <summary>
    /// RunLoadRaw -key &lt;zip!path&gt; [-asset &lt;name&gt;] [-file &lt;bundle file&gt;] [-all] [-patch | -verbatim [-renameStreams]]:
    /// opens the original bundle (or a file) straight from memory (no catalog, no check) in this process and loads one asset
    /// (or with -all every asset): what loads, its sizes / formats / mip counts, renderer and mesh vertex / index counts, and
    /// the errors and assertions Unity logs - to tell whether a refusal of the check is needed (the process may end natively:
    /// run it alone). -patch loads the check's patched copy instead (made in memory). -verbatim loads the bundle rewritten
    /// uncompressed with its node table bytes as the file holds them; -renameStreams changes each resource node's name by one
    /// character there (a control: streamed data that cannot be found). Numbers only.
    /// </summary>
    public static void RunLoadRaw()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        string key = Arg("-key"), asset = Arg("-asset"), file = Arg("-file"), keyFile = Arg("-keys");
        int errors = 0, asserts = 0;
        var firstErrors = new List<string>();
        Application.LogCallback hook = (msg, st, type) =>
        {
            if (type == LogType.Assert) Interlocked.Increment(ref asserts);
            else if (type == LogType.Error || type == LogType.Exception) { Interlocked.Increment(ref errors); lock (firstErrors) if (firstErrors.Count < 8) firstErrors.Add(msg?.Length > 200 ? msg.Substring(0, 200) : msg); }
        };
        Application.logMessageReceivedThreaded += hook;
        var keys = keyFile != null ? File.ReadAllLines(keyFile).Select(l => l.Split('\t')[0].Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList() : new List<string> { key };
        int failedReads = 0, withData = 0, textures = 0;
        try
        {
            foreach (var k in keys)
            {
                int open0 = CountInLog("Could not open file"), fail0 = CountInLog("async texture load: failed");
                var t = LoadRawOne(k, file, asset);
                textures += t.textures;
                withData += t.withData;
                int open1 = CountInLog("Could not open file") - open0, fail1 = CountInLog("async texture load: failed") - fail0;
                failedReads += fail1;
                Info($"   -> {open1} 'could not open file' lines, {fail1} failed texture data reads; {t.withData} of {t.textures} textures hold more than 864 B");
            }
        }
        finally { Application.logMessageReceivedThreaded -= hook; }
        Info($"Unity logged {errors} errors, {asserts} assertions" + (firstErrors.Count > 0 ? ": " + string.Join(" | ", firstErrors) : ""));
        // a streamed texture whose data cannot be opened is reported by the engine's own log only (not to the log callback)
        Info($"the process log: {CountInLog("Could not open file")} 'could not open file' lines, {CountInLog("async texture load: failed")} failed texture data reads; {keys.Count} bundles, {textures} textures");
        Check("no error or assertion during the load", errors == 0 && asserts == 0, $"{errors} errors, {asserts} assertions");
        Finish("raw", "assets loaded straight from the bundle (original, patched or rewritten copy)", sw);
    }

    static (int textures, int withData) LoadRawOne(string key, string file, string asset)
    {
        int textures = 0, withData = 0;
        Info((file ?? key));
        var bytes = file != null ? File.ReadAllBytes(file) : OriginalBytes(key, CardResolveTest.GameRoot);
        var args = TestArgs.Get();
        if (bytes != null && (args.Contains("-patch") || args.Contains("-verbatim")))
        {
            var bf = Hs2BundleFile.FromBytes(bytes, file ?? key, lzma: true);
            Info($"nodes ({bf.NodesRecovered} recovered): " + string.Join(", ", bf.Nodes.Select(x => $"'{Printable(x.Path)}' {x.Offset}+{x.Size} flags {x.Flags}")));
            if (args.Contains("-patch"))
            {
                var v = TextureCheck.Analyze(bf);
                Info("verdict: " + v);
                var patched = TextureCheck.Patch(bf, out var actions);
                Info($"patched copy: {(patched != null ? patched.Length + " bytes" : "none (nothing to repair)")}; {actions.Count} actions: {string.Join(", ", actions)}");
                if (patched != null) bytes = patched;
            }
            else bytes = Verbatim(bf, args.Contains("-renameStreams"));
        }
        var ab = bytes != null ? AssetBundle.LoadFromMemory(bytes) : null;
        Check("the bundle opens", ab, file ?? key);
        if (!ab) return (0, 0);
        var names = ab.GetAllAssetNames();
        Info($"{names.Length} asset names; scene bundle {ab.isStreamedSceneAssetBundle}");
        var load = args.Contains("-all") ? names.ToList() : new List<string> { asset };
        int loaded = 0;
        foreach (var n in load)
        {
            Debug.Log("[texcheck] loading " + n + " raw");
            var o = ab.LoadAsset<Object>(n);
            if (o) loaded++;
            Info(Describe(n, o));
            if (o is Texture2D tx) { textures++; if (UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tx) > 864) withData++; }
        }
        Check("the assets load raw (the process survived)", loaded == load.Count, $"{loaded} of {load.Count}");
        ab.Unload(true);
        return (textures, withData);
    }

    static int CountInLog(string text)
    {
        try
        {
            using (var fs = new FileStream(Application.consoleLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var r = new StreamReader(fs))
            {
                int n = 0;
                string line;
                while ((line = r.ReadLine()) != null) if (line.IndexOf(text, StringComparison.Ordinal) >= 0) n++;
                return n;
            }
        }
        catch (Exception) { return -1; }
    }

    static string Describe(string name, Object o)
    {
        switch (o)
        {
            case null: return name + ": null";
            case Texture2D t: return $"{name}: Texture2D {t.width}x{t.height} {t.format} mips {t.mipmapCount} readable {t.isReadable} memory {UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t)}";
            case GameObject go:
                var rs = go.GetComponentsInChildren<Renderer>(true);
                var meshes = new List<string>();
                foreach (var m in MeshesOf(go))
                    meshes.Add($"{m.name} v{m.vertexCount} sub{m.subMeshCount} i{(m.subMeshCount > 0 ? (long)m.GetIndexCount(0) : 0)}" +
                               (m.isReadable && m.subMeshCount > 0 ? $" max index {m.GetIndices(0).DefaultIfEmpty(-1).Max()}" : "") + (SaneMesh(m) ? "" : " NOT SANE"));
                return $"{name}: GameObject, {rs.Length} renderers, {go.GetComponentsInChildren<ParticleSystem>(true).Length} particle systems; meshes {string.Join(", ", meshes)}";
            case Mesh mesh: return $"{name}: Mesh v{mesh.vertexCount} sub{mesh.subMeshCount}";
            default: return name + ": " + o.GetType().Name;
        }
    }

    // The bundle rewritten uncompressed: stored blocks of 128 KiB, then the node table bytes as the file holds them.
    static byte[] Verbatim(Hs2BundleFile bf, bool rename)
    {
        var data = bf.AllData();
        var resources = bf.Nodes.Where(n => (n.Flags & 4) == 0).ToList();
        var table = bf.BlocksInfo.Skip(bf.NodeTableAt).ToArray();
        if (rename)
            foreach (var n in resources)
            {
                int at = IndexOf(table, Encoding.UTF8.GetBytes(n.Path));
                if (at < 0 || n.Path.Length < 6) continue;
                table[at + 5] ^= 1;
                Info($"renamed the node '{Printable(n.Path)}' in the copy (character 6)");
            }
        var info = new MemoryStream();
        void U32(Stream st, long v) { st.WriteByte((byte)(v >> 24)); st.WriteByte((byte)(v >> 16)); st.WriteByte((byte)(v >> 8)); st.WriteByte((byte)v); }
        info.Write(new byte[16], 0, 16);
        const int chunk = 1 << 17;
        int nb = (data.Length + chunk - 1) / chunk;
        U32(info, nb);
        for (int i = 0; i < nb; i++) { int k = Math.Min(chunk, data.Length - i * chunk); U32(info, k); U32(info, k); info.WriteByte(0); info.WriteByte(0); }
        info.Write(table, 0, table.Length);
        var head = new MemoryStream();
        var sig = Encoding.ASCII.GetBytes("UnityFS\0");
        head.Write(sig, 0, sig.Length);
        U32(head, bf.FormatVersion);
        var v1 = Encoding.ASCII.GetBytes(bf.UnityVersion + "\0" + bf.UnityRevision + "\0");
        head.Write(v1, 0, v1.Length);
        long total = head.Length + 8 + 12 + info.Length + data.Length;
        if (bf.FormatVersion >= 7) total += (16 - (head.Length + 20) % 16) % 16;
        U32(head, total >> 32); U32(head, total & 0xFFFFFFFF);
        U32(head, info.Length); U32(head, info.Length); U32(head, 0x40);
        while (bf.FormatVersion >= 7 && head.Length % 16 != 0) head.WriteByte(0);
        head.Write(info.ToArray(), 0, (int)info.Length);
        head.Write(data, 0, data.Length);
        Info($"verbatim copy: {head.Length} bytes, {nb} stored blocks, node table {table.Length} bytes as the file holds it");
        return head.ToArray();
    }

    static string Printable(string s) => s == null ? "" : new string(s.Select(c => c >= ' ' && c < 127 ? c : '?').ToArray());

    static int IndexOf(byte[] hay, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= hay.Length; i++)
        {
            int k = 0;
            while (k < needle.Length && hay[i + k] == needle[k]) k++;
            if (k == needle.Length) return i;
        }
        return -1;
    }
}
