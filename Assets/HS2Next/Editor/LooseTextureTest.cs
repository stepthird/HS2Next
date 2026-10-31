// Loose-PNG texture tests (Phase 4.2 package L, gap GB10;
// Runtime/Content/LooseTextures.cs). Headless; numbers only (sizes, formats, mip counts, wrap modes, hashes of the decoded
// data) — no texture is shown or written.
//   Run      : the census (Tools/survey/loose_png_census.py refs.tsv) against the catalog:
//              C1 the catalog finds exactly the census's loose texture references (every row x HS2's texture pairs);
//              C2 every reference loads a texture through ContentCatalog.LoadAsset (the decoded PNG of the census's zipmod and
//                 entry, its size, a full mip chain, 8-bit RGBA, sRGB, readable, Clamp exactly when the path contains
//                 "clamp"); references naming the same entry share one texture;
//              C3 request types (Texture = Texture2D, Cubemap null, Sprite / Object not answered);
//              C4 sync = async (LooseTextures.LoadAsync decoded separately: equal data hash, format, mips, wrap);
//              C5 the cache's takes and release (pins, takes, ReleaseAll: 0 alive, Texture2D objects back to the count before);
//              C6 rows whose bundle holds the asset are unchanged (the same object as the bundle's own load), and no vanilla
//                 row has a loose texture;
//              C7 OpenBundleAsync marks the loose-only bundle paths (Loose, no bundle) and opens existing ones as before;
//              C8 the Maker thumbnails (PartThumbnails) load loose thumbnails asynchronously and release them on Clear.
//   RunCards : every character card (Tools/targets.json card folders) → importer → documents; the slots whose row has a
//              loose main texture (thumbnails excluded) → Spike/out/maker/loose_cards.txt (card, sex, in the S1 sample,
//              slot path, reference, category, texture pairs); the input of T's S1 re-take.
// Args: -census <refs.tsv> (default <project>/Cache/survey/loose_png/refs.tsv), -limit N (C2 references), -c2Stride k (C2 loads
// every k-th reference, spread over the census; default 1 = all), -sample N (C4 / C6 /
// C8 sizes, default 200 / 200 / 60). Run: -executeMethod LooseTextureTest.Run | LooseTextureTest.RunCards (-nographics is fine).
// Reports: Spike/out/maker/loose_texture_report.txt, loose_cards_report.txt; first line "SUMMARY: N PASS, M FAIL".
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HS2Next.Content;
using HS2Next.Import;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.UI.Panels.Maker;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class LooseTextureTest
{
    // The texture requests HS2 makes from list rows (as the census's TEXTURE_PAIRS).
    static readonly (string ab, string[] cols)[] Pairs =
    {
        ("ThumbAB", new[] { "ThumbTex", "Preset" }),
        ("MainAB", new[] { "MainTex", "MainTex02", "MainTex03", "ColorMaskTex", "ColorMask02Tex", "ColorMask03Tex", "AddTex", "GlossTex", "OcclusionMapTex", "NormalMapTex" }),
        ("MainTexAB", new[] { "MainTex" }),
        ("TexAB", new[] { "TexC", "TexD" }),
        ("OverBodyMaskAB", new[] { "OverBodyMask" }), ("OverBraMaskAB", new[] { "OverBraMask" }), ("OverBodyBMaskAB", new[] { "OverBodyBMask" }),
        ("OverInnerTBMaskAB", new[] { "OverInnerTBMask" }), ("OverInnerBMaskAB", new[] { "OverInnerBMask" }), ("OverPanstMaskAB", new[] { "OverPanstMask" }),
    };

    sealed class Ref
    {
        public int Category;
        public string Key, Ab, Col, Bundle, Asset, Png, PngGuid;
        public int Width, Height, BitDepth, ColourType;
        public bool Clamp, BundleExists, Thumb;
        public string Id => Category.ToString(CultureInfo.InvariantCulture) + "|" + Key.ToLowerInvariant() + "|" + Ab + "|" + Col;
    }

    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "maker");
    static string DefaultCensus => Path.Combine(ContentSettings.ProjectRoot, "Cache", "survey", "loose_png", "refs.tsv");

    static StringBuilder rep;
    static int passes, fails;

    static void Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
    }

    static void Info(string s) => rep.AppendLine("   " + s);

    static void Finish(string file, string title, Stopwatch sw)
    {
        rep.Insert(0, $"SUMMARY: {passes} PASS, {fails} FAIL ({sw.Elapsed.TotalSeconds:F0} s)\n{title}\n");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, file), rep.ToString());
        Debug.Log($"LOOSE TEXTURE TEST DONE ({file}): {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }

    static string Arg(string name, string def = null)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static int IntArg(string name, int def) => int.Parse(Arg(name, def.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);

    static List<Ref> ReadCensus(string file)
    {
        var l = new List<Ref>();
        foreach (var line in File.ReadLines(file).Skip(1))
        {
            var c = line.Split('\t');
            if (c.Length < 18) continue;
            l.Add(new Ref
            {
                Category = int.Parse(c[0], CultureInfo.InvariantCulture), Key = c[1], Ab = c[2], Col = c[3], Bundle = c[4], Asset = c[5],
                Png = c[6], PngGuid = c[7], Width = int.Parse(c[11], CultureInfo.InvariantCulture), Height = int.Parse(c[12], CultureInfo.InvariantCulture),
                BitDepth = int.Parse(c[13], CultureInfo.InvariantCulture), ColourType = int.Parse(c[14], CultureInfo.InvariantCulture),
                Clamp = c[15] == "1", BundleExists = c[16] == "1", Thumb = c[17] == "1",
            });
        }
        return l;
    }

    static int FullChain(int w, int h) { int n = 1, m = Math.Max(w, h); while (m > 1) { m >>= 1; n++; } return n; }

    static string Hash(Texture2D t)
    {
        var raw = t.GetRawTextureData<byte>().ToArray();
        using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(raw)).Replace("-", "").ToLowerInvariant();
    }

    static bool EightBitRgba(Texture2D t) => t.format == TextureFormat.RGBA32 || t.format == TextureFormat.ARGB32;

    static string Short(string s, int n = 300) => s == null ? "" : s.Length > n ? s.Substring(0, n) + "…" : s;

    // ================================================================ Run

    public static void Run()
    {
        HS2Next.Content.LooseTextures.KeepReadable = true;   // the checks compare decoded data (the product drops the CPU copy)
        rep = new StringBuilder(); passes = fails = 0;
        var sw = Stopwatch.StartNew();
        string census = Arg("-census", DefaultCensus);
        int limit = IntArg("-limit", int.MaxValue), sample = IntArg("-sample", 200), thumbs = IntArg("-thumbs", 60);
        int stride = Math.Max(1, IntArg("-c2Stride", 1));
        rep.AppendLine($"loose PNG textures (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, colour space {QualitySettings.activeColorSpace}); census {Path.GetFileName(Path.GetDirectoryName(census))}/{Path.GetFileName(census)}");
        if (!File.Exists(census)) { Check("census file present (run Tools/survey/loose_png_census.py --out Cache/survey/loose_png)", false, census); Finish("loose_texture_report.txt", "GB10 loose PNG textures", sw); return; }
        var head = new StringBuilder();
        ContentCatalog cat = null;
        try
        {
            cat = CardResolveTest.BuildDb(head);
            Info(head.ToString().Trim());
            var refs = ReadCensus(census);
            // the census reads the scan cache without the zipmod loader's set (Phase 4.4): references of rows whose zipmod the
            // catalog does not load are counted apart
            var notLoaded = refs.Where(r => { var k = ContentId.Parse(r.Key); return !k.IsVanilla && !cat.ZipmodsByGuid.ContainsKey(k.SourceGuid); }).ToList();
            refs = refs.Except(notLoaded).ToList();
            Info($"census references of zipmods the catalog does not load (the zipmod set {(ContentCatalog.SideloaderZipmodSet ? "on" : "off")}), left out: {notLoaded.Count}{(notLoaded.Count > 0 ? " (" + string.Join(", ", notLoaded.Take(6).Select(r => r.Id)) + ")" : "")}");
            var L = cat.Loose;
            Info($"census: {refs.Count} references of {refs.Select(r => r.Category + "|" + r.Key.ToLowerInvariant()).Distinct().Count()} rows ({refs.Count(r => r.Thumb)} thumbnails, {refs.Count(r => r.BundleExists)} with an existing bundle, {refs.Count(r => r.Clamp)} clamp); catalog index: {L.Files} loose PNG files in {L.FolderCount} folders");
            C1(cat, refs);
            C2(cat, stride == 1 ? refs : refs.Where((r, i) => i % stride == 0).ToList(), limit);
            if (stride > 1) Info($"C2 above: every {stride}th of the {refs.Count} references (-c2Stride)");
            C3(cat, refs);
            C4(cat, refs, sample);
            C5(cat, refs);
            C6(cat, refs, sample);
            C7(cat, refs);
            C8(cat, refs, thumbs);
        }
        catch (Exception e) { Check("no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace)); }
        finally { cat?.UnloadAll(); }
        Finish("loose_texture_report.txt", "GB10 loose PNG textures", sw);
    }

    // C1: the catalog's loose references = the census's
    static void C1(ContentCatalog cat, List<Ref> refs)
    {
        var mine = new Dictionary<string, LooseTextures.Hit>(StringComparer.Ordinal);
        int rows = 0, vanillaHits = 0;
        foreach (int c in cat.Categories)
            foreach (var e in cat.EntriesOf(c))
            {
                rows++;
                foreach (var (ab, cols) in Pairs)
                    foreach (var col in cols)
                        if (cat.Loose.TryFind(e, ab, col, out var hit))
                        {
                            mine[c.ToString(CultureInfo.InvariantCulture) + "|" + e.Key.ToString().ToLowerInvariant() + "|" + ab + "|" + col] = hit;
                            if (e.IsVanilla) vanillaHits++;
                        }
            }
        var theirs = refs.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var onlyMine = mine.Keys.Where(k => !theirs.ContainsKey(k)).ToList();
        var onlyTheirs = theirs.Keys.Where(k => !mine.ContainsKey(k)).ToList();
        int sameFile = refs.Count(r => mine.TryGetValue(r.Id, out var h) && "abdata/" + h.Path == r.Png && string.Equals(h.Zipmod.Guid, r.PngGuid, StringComparison.OrdinalIgnoreCase));
        Info($"C1: {rows} catalog rows; loose references {mine.Count} (vanilla rows {vanillaHits})");
        Check("C1: the catalog's loose texture references = the census's (count and set)", onlyMine.Count == 0 && onlyTheirs.Count == 0 && mine.Count == refs.Count,
              $"catalog {mine.Count}, census {refs.Count}; only catalog {onlyMine.Count} {string.Join(", ", onlyMine.Take(4))}; only census {onlyTheirs.Count} {string.Join(", ", onlyTheirs.Take(4))}");
        Check("C1: the same PNG entry and zipmod as the census for every reference", sameFile == refs.Count, $"{sameFile} of {refs.Count}");
        var byCat = refs.GroupBy(r => r.Category).OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Select(r => r.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count()} rows / {g.Count()} refs");
        Info("C1 per category: " + string.Join("; ", byCat));
    }

    // C2: every census reference loads its PNG texture through the catalog
    static void C2(ContentCatalog cat, List<Ref> refs, int limit)
    {
        var L = cat.Loose;
        int n = 0, loaded = 0, source = 0, sized = 0, headerless = 0, mips = 0, rgba = 0, srgb = 0, readable = 0, wrapOk = 0, clamp = 0, shared = 0, sharedOk = 0;
        var formats = new Dictionary<string, int>(StringComparer.Ordinal);
        var bad = new List<string>();
        var window = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        int created0 = L.Created, destroyed0 = L.Destroyed;
        var sw = Stopwatch.StartNew();
        foreach (var r in refs.Take(limit))
        {
            n++;
            var e = cat.GetEntry(r.Category, ContentId.Parse(r.Key));
            var t = e != null ? cat.LoadAsset<Texture2D>(e, r.Ab, r.Col) : null;
            if (t == null) { if (bad.Count < 6) bad.Add("no texture " + r.Id); continue; }
            loaded++;
            var src = L.SourceOf(t);
            if (L.Owns(t) && "abdata/" + src.Path == r.Png && string.Equals(src.Zipmod?.Guid, r.PngGuid, StringComparison.OrdinalIgnoreCase)) source++;
            else if (bad.Count < 6) bad.Add($"source {r.Id}: {src}");
            if (r.Width == 0) headerless++;
            else if (t.width == r.Width && t.height == r.Height) sized++;
            else if (bad.Count < 6) bad.Add($"size {r.Id}: {t.width}x{t.height} vs {r.Width}x{r.Height}");
            if (t.mipmapCount == FullChain(t.width, t.height)) mips++;
            else if (bad.Count < 6) bad.Add($"mips {r.Id}: {t.mipmapCount} of {FullChain(t.width, t.height)}");
            if (r.ColourType == -1 ? t.format == TextureFormat.RGB24 : EightBitRgba(t)) rgba++;
            else if (bad.Count < 6) bad.Add($"format {r.Id}: {t.format} (PNG depth {r.BitDepth} type {r.ColourType})");
            string kind = $"PNG depth {r.BitDepth} type {r.ColourType} -> {t.format}";
            formats.TryGetValue(kind, out int fc); formats[kind] = fc + 1;
            if (GraphicsFormatUtility.IsSRGBFormat(t.graphicsFormat)) srgb++;
            if (t.isReadable) readable++;
            var want = r.Clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            if (t.wrapModeU == want && t.wrapModeV == want) wrapOk++;
            else if (bad.Count < 6) bad.Add($"wrap {r.Id}: {t.wrapModeU}/{t.wrapModeV}, want {want}");
            if (r.Clamp) clamp++;
            if (window.TryGetValue(r.Png, out var prev)) { shared++; if (ReferenceEquals(prev, t)) sharedOk++; }
            else window[r.Png] = t;
            if (window.Count >= 200)
            {
                L.ReleaseAll();
                window.Clear();
            }
        }
        double ms = sw.Elapsed.TotalMilliseconds;
        L.ReleaseAll();
        Info($"C2: {n} references in {ms / 1000:F1} s; decoded {L.Created - created0} PNGs ({L.DecodeMs / 1000:F1} s in LoadImage), destroyed {L.Destroyed - destroyed0}; formats: {string.Join(", ", formats.OrderBy(k => k.Key).Select(k => k.Key + " " + k.Value))}");
        Check("C2: every census reference loads a texture through ContentCatalog.LoadAsset<Texture2D>", loaded == n && n > 0, $"{loaded} of {n} {string.Join("; ", bad)}");
        Check("C2: the texture is the census's PNG entry of the census's zipmod (LooseTextures.SourceOf)", source == loaded, $"{source} of {loaded}");
        Check("C2: size = the PNG header", sized + headerless == loaded, $"{sized} sized, {headerless} without a PNG header (not a PNG signature; LoadImage's result kept as Sideloader keeps it)");
        Check("C2: full mip chain (LoadImage regenerates the mips; Texture2D(2, 2, RGBA32, true))", mips == loaded, $"{mips} of {loaded}");
        Check("C2: 8-bit RGBA texture for PNG data (HS2: ARGB32; 16-bit and grey PNGs too), RGB24 for JPEG data named .png (LoadImage's JPEG format)",
              rgba == loaded, $"{rgba} of {loaded}");
        Check("C2: sRGB (linear false, as Sideloader's constructor)", srgb == loaded, $"{srgb} of {loaded}");
        Check("C2: readable (LoadImage with markNonReadable false)", readable == loaded, $"{readable} of {loaded}");
        Check("C2: wrap Clamp exactly when the path contains \"clamp\", else Repeat", wrapOk == loaded, $"{wrapOk} of {loaded} ({clamp} clamp)");
        Check("C2: references naming the same PNG entry share one texture (AddTex / GlossTex of a paint, ...)", sharedOk == shared, $"{sharedOk} of {shared}");
        Check("C2: the cache is empty after ReleaseAll", L.Live == 0, $"live {L.Live}");
    }

    static Ref FirstMain(List<Ref> refs, int category) => refs.FirstOrDefault(r => r.Category == category && !r.Thumb && !r.BundleExists && r.Width > 0);

    // C3: request types
    static void C3(ContentCatalog cat, List<Ref> refs)
    {
        var r = refs.FirstOrDefault(x => x.Key.Equals("STN.QRCode#1", StringComparison.OrdinalIgnoreCase) && x.Col == "AddTex") ?? FirstMain(refs, 313);
        if (r == null) { Check("C3: a loose paint row", false); return; }
        var e = cat.GetEntry(r.Category, ContentId.Parse(r.Key));
        var t2 = cat.LoadAsset<Texture2D>(e, r.Ab, r.Col);
        var t = cat.LoadAssetAt<Texture>(e, e[r.Ab], e[r.Col]);
        var cube = cat.LoadAssetAt<Cubemap>(e, e[r.Ab], e[r.Col]);
        var sprite = cat.LoadAssetAt<Sprite>(e, e[r.Ab], e[r.Col]);
        var obj = cat.LoadAssetAt<Object>(e, e[r.Ab], e[r.Col]);
        Info($"C3 on {r.Key} {r.Col} ({r.Png}): Texture2D {(t2 ? t2.width + "x" + t2.height + " " + t2.format + " mips " + t2.mipmapCount + " " + t2.wrapMode + " name \"" + t2.name + "\"" : "null")}");
        Check("C3: a Texture request gets the same texture as a Texture2D request", t2 != null && ReferenceEquals(t, t2));
        Check("C3: a Cubemap request gets null (HS2: the cast of Sideloader's Texture2D fails); Sprite / Object requests are not answered (no bundle: null)",
              cube == null && sprite == null && (r.BundleExists || obj == null), $"cube {(cube ? "set" : "null")}, sprite {(sprite ? "set" : "null")}, object {(obj ? obj.GetType().Name : "null")}");
        Check("C3: the texture has no name (Sideloader leaves it empty; HS2's material records show \"\")", t2 != null && t2.name == "");
        cat.Loose.ReleaseAll();
    }

    // C4: sync = async (decoded separately)
    static void C4(ContentCatalog cat, List<Ref> refs, int sample)
    {
        var L = cat.Loose;
        var files = refs.Where(r => r.Width > 0).GroupBy(r => r.Png).Select(g => g.First()).ToList();
        int step = Math.Max(1, files.Count / Math.Max(1, sample));
        int n = 0, equal = 0, released = 0;
        var bad = new List<string>();
        for (int i = 0; i < files.Count && n < sample; i += step)
        {
            var r = files[i];
            n++;
            var e = cat.GetEntry(r.Category, ContentId.Parse(r.Key));
            var a = cat.LoadAsset<Texture2D>(e, r.Ab, r.Col);
            string ha = a ? Hash(a) : null, fa = a ? $"{a.width}x{a.height} {a.format} {a.mipmapCount} {a.wrapModeU}" : null;
            L.ReleaseAll();
            if (!L.TryFind(e, r.Ab, r.Col, out var hit)) { if (bad.Count < 4) bad.Add("no hit " + r.Id); continue; }
            var req = L.LoadAsync(hit);
            var w = Stopwatch.StartNew();
            while (!req.Ready && w.Elapsed.TotalSeconds < 30) System.Threading.Thread.Sleep(1);
            var b = req.Finish();
            string hb = b ? Hash(b) : null, fb = b ? $"{b.width}x{b.height} {b.format} {b.mipmapCount} {b.wrapModeU}" : null;
            if (ha != null && ha == hb && fa == fb && !ReferenceEquals(a, b)) equal++;
            else if (bad.Count < 4) bad.Add($"{r.Id}: sync {fa} {ha?.Substring(0, 12)} async {fb} {hb?.Substring(0, 12)}");
            L.Release(b);
            if (!b && L.Live == 0) released++;
        }
        Check("C4: sync = async (LooseTextures.LoadAsync, decoded separately): equal data SHA-256, size, format, mips, wrap", equal == n && n > 0, $"{equal} of {n} files {string.Join("; ", bad)}");
        Check("C4: an async take released is destroyed (nothing pinned it)", released == n, $"{released} of {n}");
    }

    // C5: takes, pins, release
    static void C5(ContentCatalog cat, List<Ref> refs)
    {
        var L = cat.Loose;
        L.ReleaseAll();
        Resources.UnloadUnusedAssets();
        int tex0 = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
        int c0 = L.Created, d0 = L.Destroyed;
        var picks = refs.Where(r => r.Width > 0 && !r.Thumb).GroupBy(r => r.Png).Select(g => g.First()).Take(12).ToList();
        var hits = new List<LooseTextures.Hit>();
        foreach (var r in picks) if (L.TryFind(cat.GetEntry(r.Category, ContentId.Parse(r.Key)), r.Ab, r.Col, out var h)) hits.Add(h);
        if (hits.Count < 4) { Check("C5: loose main textures to take", false, hits.Count.ToString()); return; }
        var a1 = L.Acquire(hits[0]); var a2 = L.Acquire(hits[0]);
        bool same = a1 && ReferenceEquals(a1, a2) && L.Taken == 2 && L.Live == 1;
        L.Release(a1);
        bool alive1 = (bool)a1;
        L.Release(a2);
        bool gone = !a1;
        var p = L.Load(hits[1]); var p2 = L.Acquire(hits[1]);
        L.Release(p2);
        bool pinned = p && ReferenceEquals(p, p2) && L.Live == 1;
        var many = hits.Skip(2).Select(h => L.Acquire(h)).ToList();
        int live = L.Live;
        L.ReleaseAll();
        bool allGone = !p && many.All(t => !t) && L.Live == 0;
        int tex1 = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
        Info($"C5: created {L.Created - c0}, destroyed {L.Destroyed - d0}; live after the takes {live}; Texture2D objects {tex0} -> {tex1}");
        Check("C5: two takes of one file share one texture (Taken 2, Live 1); the first release keeps it, the second destroys it", same && alive1 && gone);
        Check("C5: a pinned texture (synchronous load) survives the release of a take", pinned);
        Check("C5: ReleaseAll destroys every cached texture; created = destroyed; Texture2D objects back to the count before (0 leaked)",
              allGone && L.Created - c0 == L.Destroyed - d0 && tex1 == tex0, $"created {L.Created - c0}, destroyed {L.Destroyed - d0}, objects {tex1 - tex0:+0;-0;0}");
    }

    // C6: rows whose bundle holds the asset are unchanged; vanilla rows have no loose texture
    static void C6(ContentCatalog cat, List<Ref> refs, int sample)
    {
        var L = cat.Loose;
        var looseIds = new HashSet<string>(refs.Select(r => r.Id), StringComparer.Ordinal);
        var candidates = new List<(ListEntry e, string ab, string col)>();
        int vanillaPairs = 0, vanillaLoose = 0;
        foreach (int c in cat.Categories)
            foreach (var e in cat.EntriesOf(c))
                foreach (var (ab, cols) in Pairs)
                {
                    if (ab == "ThumbAB" || string.IsNullOrEmpty(e[ab]) || e[ab] == "0") continue;
                    foreach (var col in cols)
                    {
                        if (string.IsNullOrEmpty(e[col]) || e[col] == "0") continue;
                        if (e.IsVanilla) { vanillaPairs++; if (L.TryFind(e, ab, col, out _)) vanillaLoose++; }
                        // candidates: vanilla rows, and mod rows of the paint / underhair lists (bundles the body tests open)
                        if ((e.IsVanilla || c == 313 || c == 335) &&
                            !looseIds.Contains(c.ToString(CultureInfo.InvariantCulture) + "|" + e.Key.ToString().ToLowerInvariant() + "|" + ab + "|" + col))
                            candidates.Add((e, ab, col));
                    }
                }
        Check("C6: no vanilla row has a loose texture", vanillaLoose == 0, $"{vanillaLoose} of {vanillaPairs} vanilla texture pairs");
        // the vanilla rows of the bundles the loose rows share (chara/00/st_paint_00 ...) first, then a spread over the rest
        var shared = new HashSet<string>(refs.Where(r => r.BundleExists).Select(r => r.Bundle), StringComparer.OrdinalIgnoreCase);
        var picks = candidates.Where(x => x.e.IsVanilla && shared.Contains(x.e[x.ab])).Take(sample / 4).ToList();
        int step = Math.Max(1, candidates.Count / Math.Max(1, sample));
        for (int i = 0; i < candidates.Count && picks.Count < sample; i += step) picks.Add(candidates[i]);
        int n = 0, same = 0, missing = 0, mods = 0, guarded = 0, redirected = 0;
        var bad = new List<string>();
        foreach (var (e, ab, col) in picks)
        {
            // a texture the per-asset redirect (Phase 4.4) serves from another file is not this bundle's object: counted apart
            if (ContentCatalog.SideloaderAssetRedirect && cat.Redirect != null && cat.Redirect.IsRedirectable(e[ab]))
            {
                var x = cat.ExplainLoad(e, e[ab], e[col]);
                if (x != null && (x.ServedKind != x.TodayKind || !string.Equals(x.ServedFile, x.TodayFile, StringComparison.OrdinalIgnoreCase))) { redirected++; continue; }
            }
            int inc = HS2Next.Chara.TextureGuard.Incidents;
            var t = cat.LoadAsset<Texture2D>(e, ab, col);
            if (HS2Next.Chara.TextureGuard.Incidents != inc) { guarded++; continue; }   // the texture guard replaced it: not comparable
            var b = cat.LoadBundle(e, e[ab]);
            var direct = b ? b.LoadAsset<Texture2D>(e[col]) : null;
            if (direct == null) { missing++; continue; }   // the asset is not in the bundle (or not a texture): nothing to compare
            n++;
            if (!e.IsVanilla) mods++;
            if (ReferenceEquals(t, direct) && !L.Owns(t)) same++;
            else if (bad.Count < 4) bad.Add($"{e.Category} {e.Key} {col}: catalog {(t ? (L.Owns(t) ? "a loose texture" : "another object") : "null")} vs the bundle's object");
        }
        Info($"C6: {candidates.Count} non-loose texture pairs with a bundle column; {picks.Count} picked ({picks.Count(x => x.e.IsVanilla && shared.Contains(x.e[x.ab]))} vanilla rows of the bundles loose rows also name), {missing} without the asset in the bundle, {guarded} replaced by the texture guard, {redirected} served from another file by the redirect");
        Check("C6: rows whose bundle holds the asset load the bundle's own texture object (unchanged)", same == n && n > 0, $"{same} of {n} ({mods} mod rows) {string.Join("; ", bad)}");
        cat.UnloadAll();
    }

    // C7: OpenBundleAsync
    static void C7(ContentCatalog cat, List<Ref> refs)
    {
        var mains = refs.Where(r => !r.Thumb).GroupBy(r => r.Category + "|" + r.Key.ToLowerInvariant()).Select(g => g.First()).ToList();
        int loose = 0, looseOk = 0, exist = 0, existOk = 0;
        foreach (var r in mains)
        {
            var e = cat.GetEntry(r.Category, ContentId.Parse(r.Key));
            var o = cat.OpenBundleAsync(e, e[r.Ab]);
            int guard = 0;
            while (o != null && !o.IsDone && guard++ < 20000) { cat.PumpOpens(); System.Threading.Thread.Sleep(1); }
            if (r.BundleExists) { exist++; if (o.IsDone && o.Bundle && !o.Loose) existOk++; }
            else { loose++; if (o.IsDone && !o.Bundle && o.Loose) looseOk++; }
        }
        // a bundle path nobody ships whose folder holds no loose PNG: not Loose
        ListEntry miss = null;
        foreach (int c in cat.Categories)
        {
            miss = cat.EntriesOf(c).FirstOrDefault(e => !string.IsNullOrEmpty(e["MainAB"]) && e["MainAB"] != "0" && !cat.BundleExists(e["MainAB"].Replace('\\', '/')) && !cat.Loose.HasFolder(e["MainAB"]));
            if (miss != null) break;
        }
        var mo = miss != null ? cat.OpenBundleAsync(miss, miss["MainAB"]) : null;
        Check("C7: OpenBundleAsync of a loose-only bundle path: done, no bundle, Loose", looseOk == loose && loose > 0, $"{looseOk} of {loose} rows");
        Check("C7: OpenBundleAsync of a loose row whose bundle exists: the bundle, not Loose", existOk == exist, $"{existOk} of {exist} rows");
        Check("C7: a missing bundle without loose PNGs: no bundle, not Loose", miss == null || (mo.IsDone && !mo.Bundle && !mo.Loose), miss == null ? "no such row" : $"{miss.Category} {miss.Key}");
        cat.UnloadAll();
    }

    // C8: the Maker thumbnails
    static void C8(ContentCatalog cat, List<Ref> refs, int count)
    {
        var L = cat.Loose;
        L.ReleaseAll();
        var rows = refs.Where(r => r.Thumb && r.Col == PartThumbnails.ThumbColumn && r.Width > 0).GroupBy(r => r.Category + "|" + r.Key.ToLowerInvariant()).Select(g => g.First()).ToList();
        int step = Math.Max(1, rows.Count / Math.Max(1, count));
        var pick = new List<Ref>();
        for (int i = 0; i < rows.Count && pick.Count < count; i += step) pick.Add(rows[i]);
        var owner = new object();
        var th = new PartThumbnails(cat, null) { ReadDisk = false, WriteDisk = false };
        try
        {
            foreach (var r in pick) th.Request(r.Category, ContentId.Parse(r.Key), owner);
            var w = Stopwatch.StartNew();
            while (th.Pending > 0 && w.Elapsed.TotalSeconds < 120) { th.Update(50); System.Threading.Thread.Sleep(1); }
            int ok = 0, sized = 0;
            foreach (var r in pick)
            {
                var t = th.TryGet(r.Category, ContentId.Parse(r.Key));
                if (t != null && L.Owns(t)) ok++;
                if (t != null && t.width == r.Width && t.height == r.Height) sized++;
            }
            Info($"C8: {pick.Count} loose thumbnails of {pick.Select(r => r.Category).Distinct().Count()} categories in {w.Elapsed.TotalMilliseconds:F0} ms; from loose {th.FromLoose}, bundles {th.FromBundles}, failed {th.FailedCount} ({string.Join(", ", th.FailReasons.Select(k => k.Key + " " + k.Value))}); accept median {(th.AcceptMs.Count > 0 ? th.AcceptMs.OrderBy(x => x).ElementAt(th.AcceptMs.Count / 2) : 0):F2} ms");
            Check("C8: PartThumbnails loads every loose thumbnail asynchronously (loose texture, the PNG's size)", ok == pick.Count && sized == pick.Count && th.FromLoose == pick.Count && th.FailedCount == 0 && pick.Count > 0,
                  $"{ok} loose, {sized} sized of {pick.Count}; failed {th.FailedCount}");
            int live = L.Live;
            foreach (var r in pick) th.Release(r.Category, ContentId.Parse(r.Key), owner);
            th.Clear();
            Check("C8: Clear releases them (the loose cache empty again)", live == pick.Count && L.Live == 0, $"live {live} -> {L.Live}");
        }
        finally { th.Dispose(); }
    }

    // ================================================================ RunCards

    public static void RunCards()
    {
        HS2Next.Content.LooseTextures.KeepReadable = true;   // the checks compare decoded data (the product drops the CPU copy)
        rep = new StringBuilder(); passes = fails = 0;
        var sw = Stopwatch.StartNew();
        string outFile = Arg("-out", Path.Combine(OutDir, "loose_cards.txt"));
        using (var h = MakerCoreTest.Harness.Open())
        {
            var cat = h.Catalog;
            var cards = MakerCoreTest.AllCards();
            var s1 = new HashSet<string>(MakerCoreTest.SampleCards(), StringComparer.OrdinalIgnoreCase);
            rep.AppendLine($"cards referencing loose-PNG rows (Unity {Application.unityVersion}): {cards.Count} card files; S1 sample {s1.Count}");
            var lines = new List<string>();
            int chars = 0, failed = 0, withLoose = 0, s1Loose = 0, slots = 0;
            var bySlot = new Dictionary<string, int>(StringComparer.Ordinal);
            string ud = Path.GetFullPath(TestTargets.UserData).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            foreach (var file in cards)
            {
                byte[] bytes;
                try { bytes = File.ReadAllBytes(file); } catch (Exception) { failed++; continue; }
                if (Hs2Importer.Detect(bytes) != Hs2SourceKind.Chara) continue;
                chars++;
                CharaContainer c;
                try { c = h.Importer.Import(new CardAppearanceSource { FullPath = file, Sha256 = "", ReadBytes = () => bytes, Name = Path.GetFileNameWithoutExtension(file) }, new FindingList()); }
                catch (Exception) { failed++; continue; }
                if (c == null) { failed++; continue; }
                string rel = (Path.GetFullPath(file).StartsWith(ud, StringComparison.OrdinalIgnoreCase) ? Path.GetFullPath(file).Substring(ud.Length) : Path.GetFileName(file)).Replace('\\', '/');
                int sex = c.Chara.Profile.Sex;
                var hits = LooseHits(cat, c, sex, bySlot);
                if (hits.Count == 0) continue;
                withLoose++;
                bool inS1 = s1.Contains(file);
                if (inS1) s1Loose++;
                slots += hits.Count;
                foreach (var x in hits) lines.Add($"{rel}\t{(sex == 0 ? "male" : "female")}\t{(inS1 ? "S1" : "-")}\t{x}");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(outFile));
            var head = $"# cards whose documents reference rows with a Sideloader loose-PNG main texture (Phase 4.2 package L, GB10)\n" +
                       $"# {withLoose} of {chars} character cards ({s1Loose} in the S1 sample targets + 20), {slots} slots; by slot: " +
                       string.Join(", ", bySlot.OrderBy(k => k.Key).Select(k => k.Key + " " + k.Value)) + "\n" +
                       "# card (relative to UserData)\tsex\tS1 sample\tslot (document.member)\treference\tcategory\tloose texture pairs\n";
            File.WriteAllText(outFile, head + string.Join("\n", lines) + "\n", new UTF8Encoding(false));
            Info($"{chars} character cards imported ({failed} failed); {withLoose} reference loose-PNG rows ({s1Loose} in the S1 sample), {slots} slots; written {Path.GetFileName(outFile)}");
            Info("by slot: " + string.Join(", ", bySlot.OrderBy(k => k.Key).Select(k => k.Key + " " + k.Value)));
            Check("cards: every character card imported", failed == 0, failed + " failed");
        }
        Finish("loose_cards_report.txt", "GB10 cards referencing loose-PNG rows (input of the S1 re-take)", sw);
    }

    /// <summary>The slots of a character's documents that reference rows with a Sideloader loose-PNG main texture, one
    /// "document.member, reference, category, loose texture pairs" line each (tab-separated), counted by slot into
    /// <paramref name="bySlot"/> (RunCards; AllCardsTest per card).</summary>
    internal static List<string> LooseHits(ContentCatalog cat, CharaContainer c, int sex, Dictionary<string, int> bySlot)
    {
        var hits = new List<string>();
        foreach (var (doc, member, value, category) in References(c, sex))
        {
            if (!ContentId.TryParse(value, out var id) || id.IsEmpty) continue;
            var e = cat.GetEntry(category, id);
            if (e == null) continue;
            var pairs = new List<string>();
            foreach (var (ab, cols) in Pairs)
            {
                if (ab == "ThumbAB") continue;
                foreach (var col in cols) if (cat.Loose.TryFind(e, ab, col, out _)) pairs.Add(ab + "/" + col);
            }
            if (pairs.Count == 0) continue;
            hits.Add($"{doc}.{member}\t{value}\t{category}\t{string.Join(",", pairs)}");
            string slot = SlotName(doc + "." + member);
            bySlot.TryGetValue(slot, out int k); bySlot[slot] = k + 1;
        }
        return hits;
    }

    // ================================================================ RunQuit (play mode, graphics; runner -NoQuit)

    static string QuitReport => Path.Combine(OutDir, "loose_quit_report.txt");

    /// <summary>
    /// Quit with thumbnail loads in flight (the 4.2 shutdown hang): play mode; A — the shared Maker thumbnails (disk cache on,
    /// so the disk loader has UnityWebRequests in flight), a second instance (loose reads) and a third (bundle thumbnails:
    /// AssetBundleRequests and bundle opens) are asked for the face skin and paint lists, pumped 3 frames, then
    /// PartThumbnails.ReleaseAllForQuit must complete the asset loads / opens and leave no instance, no web request and no loose
    /// take; B — the same with new instances, then the coroutine ends with the loads in flight and the harness exits
    /// (EditorApplication.Exit). The exit itself is judged outside: the runner must return 0 well inside its timeout, and the
    /// log must hold the quit hook's line "[thumbs] quit (" and no "Leftover web requests after shut down".
    /// Run: unity_batch.ps1 -Method LooseTextureTest.RunQuit -NoQuit -TimeoutMin 5. Report Spike/out/maker/loose_quit_report.txt.
    /// </summary>
    public static void RunQuit()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(QuitReport, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("LooseTextureTest.QuitPlay", QuitReport, 10, true);
    }

    public static IEnumerator QuitPlay()
    {
        HS2Next.Content.LooseTextures.KeepReadable = true;   // the checks compare decoded data (the product drops the CPU copy)
        rep = new StringBuilder(); passes = fails = 0;
        var sw = Stopwatch.StartNew();
        // -nohook (diagnostics): the instances are left to Unity's shutdown, as before 4.2 (expect "Leftover web requests")
        bool noHook = TestArgs.Get().Contains("-nohook");
        PartThumbnails.ReleaseOnQuit = !noHook;
        rep.AppendLine($"quit with thumbnail loads in flight (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, play mode {Application.isPlaying}){(noHook ? "; -nohook: no release at quit" : "")}");
        var cat = ContentCatalog.FromSettings(ContentSettings.LoadDefault());
        cat.LoadLists();
        var keys = new List<(int, ContentId)>();
        foreach (int c in new[] { 211, 313 }) foreach (var e in cat.EntriesOf(c)) keys.Add((c, e.Key));
        int looseRows = keys.Count(k => cat.Loose.TryFind(cat.GetEntry(k.Item1, k.Item2), "ThumbAB", PartThumbnails.ThumbColumn, out _));
        Info($"{keys.Count} rows of the face skin (211) and paint (313) lists, {looseRows} with a loose thumbnail; disk cache {PartThumbnails.CacheDirFor(cat) != null}");
        for (int pass = 0; pass < 2; pass++)
        {
            string tag = pass == 0 ? "A" : "B";
            var shared = PartThumbnails.Shared(cat);
            var other = new PartThumbnails(cat, null) { ReadDisk = false, WriteDisk = false };
            var bundles = new PartThumbnails(cat, null) { ReadDisk = false, WriteDisk = false };
            var owner = new object();
            // the shared instance asks every row (disk-cached ones become web requests), the other only the loose rows (worker
            // reads), the third only rows with a bundle thumbnail (asynchronous asset loads / bundle opens); A in list order, B
            // from the end
            var order = pass == 0 ? keys : Enumerable.Reverse(keys).ToList();
            foreach (var (c, k) in order)
            {
                shared.Request(c, k, owner);
                if (cat.Loose.TryFind(cat.GetEntry(c, k), "ThumbAB", PartThumbnails.ThumbColumn, out _)) other.Request(c, k, owner);
                else bundles.Request(c, k, owner);
            }
            int asyncLoads = 0;
            for (int f = 0; f < 3; f++)
            {
                shared.Update(); other.Update(); bundles.Update();
                asyncLoads = bundles.AssetLoadsInFlight + bundles.OpensInFlight + shared.AssetLoadsInFlight + shared.OpensInFlight;
                yield return null;
            }
            int pending = shared.Pending + other.Pending + bundles.Pending, web = shared.DiskInFlight + other.DiskInFlight;
            int loose = shared.LooseInFlight + other.LooseInFlight;
            asyncLoads = bundles.AssetLoadsInFlight + bundles.OpensInFlight + shared.AssetLoadsInFlight + shared.OpensInFlight;
            Info($"{tag}: after 3 frames: pending {pending}, disk web requests in flight {web}, loose reads in flight {loose}, asset loads / bundle opens in flight {asyncLoads} (catalog opens {cat.PendingOpenCount}); live instances {PartThumbnails.LiveInstances}");
            Check($"{tag}: thumbnail loads are in flight (web requests, loose reads, bundle asset loads)", pending > 0 && web > 0 && loose > 0 && asyncLoads > 0,
                  $"pending {pending}, web {web}, loose {loose}, asset loads / opens {asyncLoads}");
            if (pass == 0)
            {
                var r = PartThumbnails.ReleaseAllForQuit("LooseTextureTest A");
                Check("A: ReleaseAllForQuit clears every instance (asset loads and bundle opens completed, web requests disposed, loose reads cancelled, no loose take left)",
                      r.instances >= 3 && r.webRequests == web && r.looseReads == loose && r.assetLoads + r.bundleOpens > 0 && cat.PendingOpenCount == 0
                      && PartThumbnails.LiveInstances == 0 && shared.Pending + other.Pending + bundles.Pending == 0 && cat.Loose.Taken == 0,
                      $"instances {r.instances}, web {r.webRequests}, loose {r.looseReads}, asset loads {r.assetLoads}, bundle opens {r.bundleOpens}; catalog opens left {cat.PendingOpenCount}, live {PartThumbnails.LiveInstances}, pending {shared.Pending + other.Pending + bundles.Pending}, loose takes {cat.Loose.Taken}, loose live {cat.Loose.Live}");
                yield return null;
            }
            else
            {
                Info("B: ending the session now with these loads in flight (EditorApplication.Exit through the harness); the runner judges the exit");
            }
        }
        rep.Insert(0, $"SUMMARY: {passes} PASS, {fails} FAIL ({sw.Elapsed.TotalSeconds:F0} s)\nGB10 quit with thumbnail loads in flight\n");
        File.WriteAllText(QuitReport, rep.ToString(), new UTF8Encoding(false));
        Debug.Log($"LOOSE QUIT TEST: {passes} PASS, {fails} FAIL; exiting with thumbnail loads in flight");
        PlayModeHarness.ExitCode = fails == 0 ? 0 : 1;
    }

    // ================================================================ RunS1Explain

    /// <summary>
    /// MakerCoreTest's S1 build path vs the stored baselines, explained: a differing snapshot line is "loose" when it equals
    /// the baseline line after every loose texture of the build (its snapshot text "(WxH format mips=N)", no name) is
    /// written back as "null" (the slot was empty before GB10), "Y10" for the bust gravity of motion|bust lines, else
    /// "other" (printed). Cards: the S1 sample (default), -targets, -all; -limit N. The S1 re-take of T (Y10 + GB10) is judged
    /// with it: every difference must be loose or Y10. Report Spike/out/maker/loose_s1_explain_report.txt.
    /// </summary>
    public static void RunS1Explain()
    {
        HS2Next.Content.LooseTextures.KeepReadable = true;   // the checks compare decoded data (the product drops the CPU copy)
        rep = new StringBuilder(); passes = fails = 0;
        var sw = Stopwatch.StartNew();
        var a = TestArgs.Get();
        List<string> cards = a.Contains("-all") ? MakerCoreTest.AllCards()
            : a.Contains("-targets") ? TestTargets.Load().VisualCards.Where(File.Exists).ToList() : MakerCoreTest.SampleCards();
        int limit = IntArg("-limit", int.MaxValue);
        if (cards.Count > limit) cards = cards.Take(limit).ToList();
        rep.AppendLine($"S1 differences explained (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}): {cards.Count} files ({(a.Contains("-all") ? "every card" : a.Contains("-targets") ? "the visual targets" : "the visual targets + 20")})");
        int built = 0, equal = 0, differ = 0, noBase = 0, failed = 0, looseCards = 0, y10Cards = 0, otherCards = 0, otherLines = 0;
        var gravityRe = new System.Text.RegularExpressions.Regex(@"^motion\|bust\[\d+\]");
        using (var h = MakerCoreTest.Harness.Open())
        {
            int n = 0;
            foreach (var file in cards)
            {
                if (++n % 8 == 0) h.ReleaseBundles();
                var b = h.Build(file);
                try
                {
                    if (b.NotCharacter) continue;
                    if (!b.Ok) { failed++; Info($"{b.Rel}: build failed {b.Error}"); continue; }
                    built++;
                    var baseSnap = MakerCoreTest.LoadBaseline(b.CardSha);
                    if (baseSnap == null) { noBase++; continue; }
                    var now = b.Snapshot();
                    if (now.Sha256 == baseSnap.Sha256) { equal++; continue; }
                    differ++;
                    var descs = h.Catalog.Loose.AliveTextures.Where(t => t).Select(t => "=(" + t.width.ToString(CultureInfo.InvariantCulture) + "x" + t.height.ToString(CultureInfo.InvariantCulture) + " " + t.graphicsFormat + " mips=" + t.mipmapCount.ToString(CultureInfo.InvariantCulture) + ")").Distinct().ToList();
                    var bl = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var (k, l) in KeyedLines(baseSnap.Lines)) bl[k] = l;
                    int lo = 0, gy = 0, ot = 0;
                    var others = new List<string>();
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var (k, l) in KeyedLines(now.Lines))
                    {
                        seen.Add(k);
                        if (bl.TryGetValue(k, out var old) && old == l) continue;
                        if (old != null && descs.Aggregate(l, (s, d) => s.Replace(d, "=null")) == old) { lo++; continue; }
                        if (old != null && gravityRe.IsMatch(k)) { gy++; continue; }
                        ot++;
                        if (others.Count < 3) others.Add((old == null ? "+ " : "~ ") + Short(k + " " + (old == null ? l : l.Substring(Math.Min(l.Length, k.Length))), 200));
                    }
                    foreach (var k in bl.Keys) if (!seen.Contains(k)) { ot++; if (others.Count < 3) others.Add("- " + Short(k, 200)); }
                    if (lo > 0) looseCards++;
                    if (gy > 0) y10Cards++;
                    if (ot > 0) { otherCards++; otherLines += ot; }
                    Info($"{b.Rel}: differs: loose texture lines {lo}, Y10 gravity lines {gy}, other {ot}{(others.Count > 0 ? " — " + string.Join(" | ", others) : "")}");
                }
                finally { b.Dispose(); }
            }
        }
        Info($"{built} built, {equal} equal, {differ} differ ({looseCards} with loose texture lines, {y10Cards} with Y10 gravity lines, {otherCards} with other lines), {noBase} without a baseline, {failed} failed");
        Check("S1 explained: every difference is a loose texture slot (GB10) or the Y10 bust gravity", otherCards == 0 && failed == 0 && built > 0, $"{otherLines} other lines on {otherCards} cards");
        Finish("loose_s1_explain_report.txt", "GB10 + Y10: the S1 differences explained", sw);
    }

    // CharaSnapshot's keys: the line up to its second '|' (numbered when repeated).
    static List<(string key, string line)> KeyedLines(List<string> lines)
    {
        var l = new List<(string, string)>(lines.Count);
        var n = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var x in lines)
        {
            int p1 = x.IndexOf('|'); int p2 = p1 < 0 ? -1 : x.IndexOf('|', p1 + 1);
            string k = p2 < 0 ? x : x.Substring(0, p2);
            int c = n.TryGetValue(k, out var v) ? v + 1 : 0;
            n[k] = c;
            l.Add((c == 0 ? k : k + "#" + c, x));
        }
        return l;
    }

    // "chara.body.paint[1].item" -> "chara.body.paint[k].item"; "coord.clothes.top.colors[2].pattern" -> "coord.clothes.top.colors[k].pattern"
    static string SlotName(string p) => System.Text.RegularExpressions.Regex.Replace(p, @"\[\d+\]", "[k]");

    // Every string member of the two documents that parses as a reference, with its category (MakerEdits.CategoryOf).
    static IEnumerable<(string doc, string member, string value, int category)> References(CharaContainer c, int sex)
    {
        var chara = JObject.Parse(Encoding.UTF8.GetString(CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, c.Chara.Write)));
        foreach (var v in chara.Descendants().OfType<JValue>())
        {
            if (v.Type != JTokenType.String) continue;
            int category = HS2Next.Scenes.MakerEdits.CategoryOf(AppearancePaths.Chara(v.Path), sex);
            if (category >= 0) yield return ("chara", v.Path, (string)v.Value, category);
        }
        var coord = JObject.Parse(Encoding.UTF8.GetString(CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, c.Coord.Write)));
        foreach (var v in coord.Descendants().OfType<JValue>())
        {
            if (v.Type != JTokenType.String) continue;
            int category = HS2Next.Scenes.MakerEdits.CategoryOf(AppearancePaths.Coord(v.Path), sex, c.Coord);
            if (category < 0 && v.Parent is JProperty jp && jp.Name == "item" && jp.Parent is JObject o && o["category"] != null && o["category"].Type == JTokenType.Integer)
                category = (int)o["category"];   // an accessory entry (its own category member)
            if (category >= 0) yield return ("coord", v.Path, (string)v.Value, category);
        }
    }
}
