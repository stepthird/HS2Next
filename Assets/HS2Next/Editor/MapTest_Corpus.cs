// MapTest.corpus (package A): every Studio map row (vanilla and mods, the AI
// gate off) loaded and unloaded in play mode through a standalone MapRealization, in chunks (-from / -limit, at most 100
// maps per Editor process; run_tests.ps1 MapTest.corpus.0-6). Per map: state, load and step milliseconds, findings,
// MapCheck (lights folded with rendering layers, renderer layers, no Built-in shader, lightmap / probe counts against
// Tools/survey/map_census.tsv, audio / cameras off, main light, blocks), the shader coverage (active non-null slots on a
// mapped shader, i.e. not the URP/Lit fallback, / all active non-null slots), the peak graphics-driver memory, and the leak
// check (material and texture counts after the unload equal those before the load).
// Native crashes (a corrupted bundle): the map being loaded is written to map_corpus_current.txt first; a run that finds
// it moves it to Cache/maps/corpus_crashes.txt and skips the maps listed there (run_tests retries the chunk).
// Rows that fail as in HS2 (the empty-bundle rows) report map.loadFailed and count as expected failures.
// Run: & Tools\unity_batch.ps1 -Method MapTest.RunCorpus -NoQuit -NoGraphics -MethodArgs '-from','0','-limit','100','-tag','0'
// Report: Spike/out/maps/map_corpus_report[_<tag>].txt ("SUMMARY: N PASS, M FAIL"); rows Cache/maps/corpus_<date>[_<tag>].tsv.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

public static partial class MapTest
{
    const string CorpusTagKey = "HS2Next.MapTest.CorpusArgs";

    static string CorpusReport(string tag) => Path.Combine(OutDir, tag == null ? "map_corpus_report.txt" : "map_corpus_report_" + tag + ".txt");

    public static void RunCorpus()
    {
        Directory.CreateDirectory(OutDir);
        string tag = Arg("-tag");
        // the arguments survive the domain reload of entering play mode through SessionState
        SessionState.SetString(CorpusTagKey, string.Join("\u001f", Environment.GetCommandLineArgs()));
        File.WriteAllText(CorpusReport(tag), "SUMMARY: 0 PASS, 1 FAIL\nMapTest.corpus: started, no result yet\n", new UTF8Encoding(false));
        PlayModeHarness.Start(nameof(MapTest) + "." + nameof(CorpusMain), CorpusReport(tag), 180);
    }

    static string CArg(string name)
    {
        var a = SessionState.GetString(CorpusTagKey, "").Split('\u001f');
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    static IEnumerator CorpusMain()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        string tag = CArg("-tag");
        rep.AppendLine($"MapTest.corpus (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}); lists with the AI gate off");
        RuntimeServices sv = null;
        try { sv = PlayServices(); }
        catch (Exception e) { Section("setup", "services"); Check("catalog and Studio lists", false, e.ToString()); }
        if (sv != null)
        {
            MapAudio.EnsureLink(sv.Hs2Root);
            yield return GuardCo("corpus", Corpus(sv, tag));
            try { sv.Catalog.UnloadAll(); } catch (Exception e) { Info("catalog unload threw: " + e.Message); }
        }
        End(Path.GetFileName(CorpusReport(tag)), "MAP CORPUS TEST", sw);
        PlayModeHarness.ExitCode = fails > 0 ? 1 : 0;
    }

    static IEnumerator Corpus(RuntimeServices sv, string tag)
    {
        Section("corpus", "every map row");
        string src = CArg("-src");
        int limit = int.TryParse(CArg("-limit"), out var l) ? l : int.MaxValue;
        int from = int.TryParse(CArg("-from"), out var fr) && fr > 0 ? fr : 0;
        var rows = sv.Db.Maps.All.Where(r => src == null || (src == "vanilla" ? r.IsVanilla : !r.IsVanilla)).Skip(from).Take(limit).ToList();
        var census = Census();
        Info($"{rows.Count} rows (src {src ?? "all"}, from {from}, of {sv.Db.Maps.All.Count}); link {MapAudio.LinkState}");

        string currentFile = Path.Combine(OutDir, "map_corpus_current" + (tag != null ? "_" + tag : "") + ".txt");
        string crashFile = Path.Combine(ContentSettings.ProjectRoot, "Cache", "maps", "corpus_crashes.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(crashFile));
        if (File.Exists(currentFile))
        {
            var last = File.ReadAllText(currentFile).Trim();
            if (last.Length > 0) File.AppendAllText(crashFile, last + "\n", new UTF8Encoding(false));
            File.Delete(currentFile);
        }
        var crashed = new HashSet<string>(File.Exists(crashFile) ? File.ReadAllLines(crashFile).Select(x => x.Trim()).Where(x => x.Length > 0) : Enumerable.Empty<string>(), StringComparer.Ordinal);

        var expect = LoadMapExpect(sv);
        Info(expect != null ? $"expectation file: {expect.Count} maps" : "expectation file absent: the per-object comparison is skipped (census counts only)");
        int expectMaps = 0, expectBad = 0;
        var expectKinds = new Dictionary<string, int>(StringComparer.Ordinal);
        var tsv = new StringBuilder("ref\tname\tstate\tloadMs\tsteps\tlights\tfolded\trenderers\tslots\tcoverage\tlightmaps\tcensusLightmaps\tlightmapped\tcensusLightmapped\tprobes\tgfxPeakMB\tleakMaterials\tleakTextures\tproblems\texpectation\tfindings\n");
        var findings = new SceneFindings();
        var map = new MapRealization(sv.Catalog, sv.Db, findings, sv.Hs2Root);
        bool[] ok = { false };
        int refused = 0;
        var refusedRefs = new List<string>();
        int loaded = 0, expectedFail = 0, unexpectedFail = 0, problemMaps = 0, leaks = 0, shadowed = 0, crashRows = 0, exceptionMaps = 0, stepFailed = 0;
        int vanSlots = 0, vanMapped = 0, maps95 = 0, mapsFull = 0, modMaps = 0;
        long gfxPeak = 0;
        var findingCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var loadMs = new List<long>();
        yield return Resources.UnloadUnusedAssets();
        int mats0 = CountOf<Material>(), tex0 = TextureCount();
        // Leak check (carry-over aware): the materials and textures that appeared during map k and are still alive after map
        // k+1 has been unloaded too are leaks of map k; Unity keeps a few objects of the active scene until the next one
        // replaces them (counts alternate +n / -n between maps), which is no leak.
        var alive0 = AliveObjects();
        (string mref, Dictionary<EntityId, string> added)? pending = null;
        var leakLines = new List<string>();
        int giKept = 0, giMaps = 0;
        int exAll = exceptions.Count;
        foreach (var row in rows)
        {
            string mref = row.Key.ToString();
            if (sv.Db.TryGetMap(row.Key, out var resolved) && resolved != row) { shadowed++; continue; }
            if (crashed.Contains(mref)) { crashRows++; tsv.Append(mref).Append('\t').Append(row.Name).Append("\tskipped (native crash in an earlier run)\n"); continue; }
            File.WriteAllText(currentFile, mref);
            census.TryGetValue(mref, out var crow);
            int f0 = findings.Count, ex0 = exceptions.Count;
            var sw = Stopwatch.StartNew();
            map.SetFlags(true, true);
            map.SetTarget(mref, Vec3.Zero, Vec3.Zero);
            yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return !map.IsBusy; }, 900, ok);
            yield return PlayModeHarness.WaitFrames(3);   // the sea step (1 frame) and the audio check (2 frames)
            map.Apply();
            long gfx = Profiler.GetAllocatedMemoryForGraphicsDriver();
            if (gfx > gfxPeak) gfxPeak = gfx;
            var probs = new List<string>();
            int lights = 0, folded = 0, slots = 0, mapped = 0, lmCount = 0, lmd = 0, probes = 0;
            string steps = "", expectNote = "";
            if (map.State == MapState.Loaded)
            {
                loaded++;
                loadMs.Add(map.LoadMs);
                try
                {
                    probs = MapCheck(map, crow, null);
                    var s = map.Structure;
                    foreach (var lt in s.Lights) if (lt) { lights++; if (BundleLights.OriginalOf(lt)) folded++; }
                    foreach (var r in s.Renderers)
                    {
                        if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                        foreach (var m in r.sharedMaterials)
                        {
                            if (!m) continue;
                            slots++;
                            var o = map.Materials.OriginOf(m);
                            bool fallback = m.shader && m.shader.name == "Universal Render Pipeline/Lit" && o != null && o.ShaderOriginal != "Universal Render Pipeline/Lit";
                            if (!fallback && !map.Materials.IsBuiltIn(m)) mapped++;
                        }
                        if (ShaderMap.IsLightmapped(r)) lmd++;
                    }
                    if (expect != null && expect.TryGetValue(mref, out var ex))
                    {
                        expectMaps++;
                        var diffs = ExpectDiff(map, ex);
                        if (diffs.Count > 0)
                        {
                            expectBad++;
                            foreach (var d in diffs) { string k = d.Split(':')[0]; expectKinds.TryGetValue(k, out int ck); expectKinds[k] = ck + 1; }
                            expectNote = string.Join("; ", diffs.Take(4));
                        }
                    }
                    lmCount = map.Lightmaps.SceneLightmaps;
                    probes = LightmapSettings.lightProbes != null ? LightmapSettings.lightProbes.count : 0;
                    steps = string.Join(" ", map.StepMs.Select(kv => kv.Key + ":" + kv.Value));
                    // flags and pose once, as the play test
                    map.SetFlags(false, false); map.Apply();
                    map.SetFlags(true, true); map.Apply();
                    map.SetTarget(mref, new Vec3(1, 2, 3), new Vec3(0, 90, 0)); map.Apply();
                    map.SetTarget(mref, Vec3.Zero, Vec3.Zero); map.Apply();
                    map.StepScripts(0.1f);
                }
                catch (Exception e) { exceptions.Add(e.GetType().Name + ": " + e.Message); }
                if (row.IsVanilla) { vanSlots += slots; vanMapped += mapped; }
                else
                {
                    modMaps++;
                    if (slots == 0 || mapped == slots) mapsFull++;
                    if (slots == 0 || mapped >= 0.95 * slots) maps95++;
                }
            }
            else
            {
                bool expected = string.IsNullOrEmpty(row.BundlePath) || crow != null && crow.Status.Contains("bundle error");
                // the bundle opened and lists the scene, but Unity 6 does not find it (by path or name): repacked bundles whose
                // archive is named by its CAB hash instead of BuildPlayer-<scene> (HS2's Unity 2018 loads them)
                bool refusedByUnity = !expected && map.MapFindingsOfLoad.Any(f => f.Code == "map.loadFailed" && f.Message.Contains("could not be loaded"));
                if (expected) expectedFail++;
                else if (refusedByUnity) { refused++; refusedRefs.Add(mref); }
                else { unexpectedFail++; probs.Add("state " + map.State); }
            }
            var fs = map.MapFindingsOfLoad.ToList();   // this load's map findings (the session merges repeated codes)
            // unload and the leak check
            map.SetTarget(null, Vec3.Zero, Vec3.Zero);
            yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return !map.IsBusy && map.PendingUnloads == 0; }, 300, ok);
            yield return Resources.UnloadUnusedAssets();
            int mats1 = CountOf<Material>(), tex1 = TextureCount();
            int dm = mats1 - mats0, dt = tex1 - tex0;
            mats0 = mats1; tex0 = tex1;   // the next map's baseline
            var alive1 = AliveObjects();
            if (pending.HasValue)
            {
                var (pref, padded) = pending.Value;
                var still = padded.Where(kv => alive1.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList();
                // Unity's realtime-GI system textures ("Unity GI (WxH) Irradiance / Realtime Directionality") of scenes with
                // realtime GI settings stay with the GI manager after the scene unload: Unity-owned, reported, not a map leak
                int gi = still.RemoveAll(UnityOwned);
                if (gi > 0) { giKept += gi; giMaps++; }
                if (still.Count > 0) { leaks++; leakLines.Add($"{pref}: {still.Count} objects still alive after the next map: {string.Join(", ", still.Take(6))}"); }
            }
            pending = (mref, alive1.Where(kv => !alive0.ContainsKey(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));
            alive0 = alive1;
            foreach (var f in fs) { findingCounts.TryGetValue(f.Code, out int c); findingCounts[f.Code] = c + 1; }
            if (fs.Any(f => f.Code == "map.stepFailed")) { stepFailed++; probs.Add("stepFailed: " + string.Join(" | ", fs.Where(f => f.Code == "map.stepFailed").Select(f => f.Message).Take(2))); }
            if (exceptions.Count > ex0) { exceptionMaps++; probs.Add("exception: " + string.Join(" | ", exceptions.Skip(ex0).Take(2))); }
            if (probs.Count > 0) problemMaps++;
            tsv.Append(mref).Append('\t').Append(row.Name).Append('\t').Append(map.State == MapState.None ? "unloaded" : map.State.ToString()).Append('\t')
               .Append(loadMs.Count > 0 && map.State != MapState.Failed ? loadMs[loadMs.Count - 1] : 0).Append('\t').Append(steps).Append('\t')
               .Append(lights).Append('\t').Append(folded).Append('\t').Append(crow?.Renderers ?? 0).Append('\t').Append(slots).Append('\t')
               .Append(slots > 0 ? (mapped / (double)slots).ToString("F4") : "1").Append('\t').Append(lmCount).Append('\t').Append(crow?.Lightmaps ?? -1).Append('\t')
               .Append(lmd).Append('\t').Append(crow?.Lightmapped ?? -1).Append('\t').Append(probes).Append('\t').Append(gfx / (1024 * 1024)).Append('\t')
               .Append(dm).Append('\t').Append(dt).Append('\t').Append(string.Join("; ", probs).Replace('\t', ' ').Replace('\n', ' ')).Append('\t').Append(expectNote.Replace('\t', ' ').Replace('\n', ' ')).Append('\t')
               .Append(string.Join(",", fs.Select(f => f.Code).Distinct())).Append('\n');
            File.Delete(currentFile);
            int n = loaded + expectedFail + unexpectedFail + refused;
            if (n % 10 == 0) Debug.Log($"[MapTest corpus] {n} / {rows.Count} maps, {sw.Elapsed.TotalSeconds:F0} s for the last; problems {problemMaps}");
        }
        map.Clear();
        yield return PlayModeHarness.WaitUntil(() => map.PendingUnloads == 0, 120, ok);
        yield return Resources.UnloadUnusedAssets();
        yield return PlayModeHarness.WaitFrames(3);
        yield return Resources.UnloadUnusedAssets();
        if (pending.HasValue)
        {
            var (lref, ladded) = pending.Value;
            var aliveEnd = AliveObjects();
            var still = ladded.Where(kv => aliveEnd.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList();
            int gi = still.RemoveAll(UnityOwned);
            if (gi > 0) { giKept += gi; giMaps++; }
            if (still.Count > 0) { leaks++; leakLines.Add($"{lref} (the last map): {still.Count} objects still alive after the clear: {string.Join(", ", still.Take(6))}"); }
        }
        foreach (var ll in leakLines) Info("leak: " + ll);
        if (giKept > 0) Info($"Unity-owned objects kept after the unload (realtime-GI textures, the built-in font material; not counted as leaks): {giKept} in {giMaps} maps");

        string date = DateTime.Now.ToString("yyyyMMdd");
        string tsvPath = Path.Combine(ContentSettings.ProjectRoot, "Cache", "maps", "corpus_" + date + (tag != null ? "_" + tag : "") + ".tsv");
        File.WriteAllText(tsvPath, tsv.ToString(), new UTF8Encoding(false));
        loadMs.Sort();
        Info($"{rows.Count} rows: loaded {loaded}, expected failures {expectedFail}, unexpected failures {unexpectedFail}, shadowed by a duplicate key {shadowed}, skipped (crash list) {crashRows}");
        Info($"load ms median {(loadMs.Count > 0 ? loadMs[loadMs.Count / 2] : 0)}, p95 {(loadMs.Count > 0 ? loadMs[(int)(loadMs.Count * 0.95)] : 0)}, max {(loadMs.Count > 0 ? loadMs[loadMs.Count - 1] : 0)}; graphics driver peak {gfxPeak / (1024 * 1024)} MB (0 without a graphics device)");
        Info($"coverage: vanilla {(vanSlots > 0 ? 100.0 * vanMapped / vanSlots : 100):F1} % of active slots; mods {mapsFull} of {modMaps} maps fully, {maps95} >= 95 %");
        Info("findings: " + string.Join(", ", findingCounts.OrderByDescending(k => k.Value).Select(k => k.Key + " x" + k.Value)));
        Info("rows: " + tsvPath);
        if (refused > 0) Warn($"{refused} rows whose scene Unity 6 does not load from its bundle (repacked bundles; HS2 does not load them either, package C): {string.Join(", ", refusedRefs)}");
        if (crashRows > 0) Warn(crashRows + " maps skipped: they crashed Unity natively (Cache/maps/corpus_crashes.txt)");
        Check("no uncaught exception and no map.stepFailed", exceptionMaps == 0 && stepFailed == 0 && exceptions.Count == exAll, $"{exceptionMaps} maps with exceptions, {stepFailed} with a failed step");
        Check("every row loads, or fails as in HS2 (empty bundle / bundle error rows)", unexpectedFail == 0, unexpectedFail + " unexpected failures");
        Check("every loaded map passes MapCheck", problemMaps == 0, $"{problemMaps} maps with problems; see the rows file");
        Check("no map leaks materials or textures (objects of a map alive after the next map's unload)", leaks == 0, leaks + " maps");
        if (expect != null)
        {
            Info("expectation differences by kind: " + string.Join(", ", expectKinds.OrderByDescending(k => k.Value).Select(k => k.Key + " x" + k.Value)));
            Check("roots, lights (original values), renderers (lightmap data, original shaders, keywords) and block results equal the expectation file",
                  expectBad == 0, $"{expectBad} of {expectMaps} maps differ");
        }
    }

    // ================================================================ expectation file (package C, Tools/survey/map_expect.py)

    static Dictionary<string, JObject> LoadMapExpect(RuntimeServices sv)
    {
        string dir = Path.Combine(ContentSettings.ProjectRoot, "Cache", "maps");
        string file = Directory.Exists(dir) ? Directory.GetFiles(dir, "expect_*.json.gz").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
        if (file == null) return null;
        using (var fs = File.OpenRead(file))
        using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress))
        using (var sr = new StreamReader(gz, Encoding.UTF8))
        using (var jr = new Newtonsoft.Json.JsonTextReader(sr))
        {
            var root = JObject.Load(jr);
            var d = new Dictionary<string, JObject>(StringComparer.Ordinal);
            foreach (JObject m in (JArray)root["maps"])
            {
                string r = (string)m["ref"];
                if (r == null || (string)m["status"] != "ok") continue;
                // a GUID#id defined twice: HS2 (and the lists) resolve the first row; the expectation marks the others key.dup = true
                var dup = m["key"]?["dup"];
                if (dup != null && dup.Type == JTokenType.Boolean && (bool)dup) continue;
                if (dup != null && dup.Type == JTokenType.String && d.ContainsKey(r)) continue;   // older files: a reason string
                d[r] = m;
            }
            return d;
        }
    }

    /// <summary>Objects Unity itself keeps after a map unload: the realtime-GI textures of scenes with realtime GI settings
    /// ("Unity GI (WxH) …", kept by the GI manager) and the built-in font's "Font Material" (made on the first TextMesh with
    /// the built-in font, kept for the process).</summary>
    static bool UnityOwned(string x) => x.StartsWith("Texture2D:Unity GI", StringComparison.Ordinal) || x == "Material:Font Material" || x == "Material:Hidden/BlitCopy";

    /// <summary>Every material and texture (Unity's temporary render-texture pool left out) by id, with "Type:name".</summary>
    static Dictionary<EntityId, string> AliveObjects()
    {
        var d = new Dictionary<EntityId, string>();
        foreach (var m in Resources.FindObjectsOfTypeAll<Material>()) d[m.GetEntityId()] = "Material:" + m.name;
        foreach (var t in Resources.FindObjectsOfTypeAll<Texture>())
            if (!(t is RenderTexture && t.name.StartsWith("TempBuffer", StringComparison.Ordinal))) d[t.GetEntityId()] = t.GetType().Name + ":" + t.name;
        return d;
    }

    static bool Near(double a, double b, double tol) =>
        double.IsNaN(a) && double.IsNaN(b) || a == b || Math.Abs(a - b) <= tol * Math.Max(1.0, Math.Abs(b));

    /// <summary>Differences between the loaded map and its expectation row ("kind: detail").</summary>
    public static List<string> ExpectDiff(MapRealization map, JObject ex)
    {
        var diffs = new List<string>();
        var s = map.Structure;
        // roots (names in GetRootGameObjects order)
        var er = (JArray)ex["roots"];
        var names = s.Roots.Select(g => g ? g.name : null).ToList();
        var enames = er.Select(x => (string)x["name"]).ToList();
        if (!names.SequenceEqual(enames)) diffs.Add($"roots: {string.Join("|", names.Take(5))} vs {string.Join("|", enames.Take(5))}");
        var lights = new Dictionary<string, Light>(StringComparer.Ordinal);
        var renderers = new Dictionary<string, Renderer>(StringComparer.Ordinal);
        s.Walk((t, p) =>
        {
            var l = t.GetComponent<Light>(); if (l) lights[p] = l;
            var r = t.GetComponent<Renderer>(); if (r) renderers[p] = r;
        });
        // lights: originals = the serialized values (a curve light's intensity follows its curve)
        var el = (JArray)ex["lights"];
        int lMissing = 0, lBad = 0; string lFirst = null;
        foreach (JArray row in el)
        {
            string path = (string)row[0];
            if (!lights.TryGetValue(path, out var l)) { lMissing++; lFirst ??= "missing " + path; continue; }
            var o = BundleLights.OriginalOf(l);
            var c = (JArray)row[4];
            Color oc = o ? o.Color : l.color;
            float oi = o ? o.Intensity : l.intensity;
            bool curve = l.GetComponent<ME_LightCurves>() != null;
            string type = (string)row[1] == "Area" ? "Rectangle" : (string)row[1];
            bool ok = l.type.ToString() == type && l.cullingMask == (int)row[10] && l.shadows.ToString() == (string)row[8] &&
                      Near(oc.r, (double)c[0], 1e-6) && Near(oc.g, (double)c[1], 1e-6) && Near(oc.b, (double)c[2], 1e-6) &&
                      (curve || Near(oi, (double)row[5], 1e-6)) &&
                      // area lights: Unity 6 derives range / spot angle from the area size (the serialized values are unused)
                      (type == "Rectangle" || type == "Disc" || Near(l.range, (double)row[6], 1e-5) && Near(l.spotAngle, (double)row[7], 1e-5));
            if (!ok) { lBad++; lFirst ??= $"{path}: {l.type} {oc} x{oi} vs {type} {c} x{row[5]}"; }
        }
        if (el.Count != lights.Count || lMissing > 0 || lBad > 0) diffs.Add($"lights: {lights.Count} vs {el.Count}, {lMissing} missing, {lBad} differ ({lFirst})");
        // renderers: lightmap data, original shader names and keywords
        var mats = (JArray)ex["materials"];
        var erd = (JArray)ex["renderers"];
        int rMissing = 0, lmBad = 0, shBad = 0, kwBad = 0; string rFirst = null;
        int first = map.Lightmaps.FirstIndex;
        foreach (JArray row in erd)
        {
            string path = (string)row[0];
            if (!renderers.TryGetValue(path, out var r)) { rMissing++; rFirst ??= "missing " + path; continue; }
            int eIdx = (int)row[5];
            // an index without a lightmap of the scene (the expectation lists serialized indices) is no lightmap at run time
            bool eLm = eIdx >= 0 && eIdx < 65534 && eIdx < map.Lightmaps.SceneLightmaps;
            int idx = ShaderMap.IsLightmapped(r) ? r.lightmapIndex - first : -1;
            var so = (JArray)row[6];
            var aso = r.lightmapScaleOffset;
            if (idx != (eLm ? eIdx : -1) || eLm && !(Near(aso.x, (double)so[0], 1e-6) && Near(aso.y, (double)so[1], 1e-6) && Near(aso.z, (double)so[2], 1e-6) && Near(aso.w, (double)so[3], 1e-6)))
            { lmBad++; rFirst ??= $"{path}: lightmap {idx} {aso} vs {eIdx} {so}"; }
            var em = (JArray)row[8];
            var sm = r.sharedMaterials;
            for (int i = 0; i < em.Count && i < sm.Length; i++)
            {
                if (em[i].Type == JTokenType.Null || !sm[i]) continue;
                var e = mats[(int)em[i]];
                var o = map.Materials.OriginOf(sm[i]);
                if (o == null) continue;
                string es = (string)e["shader"];
                // the expectation names a shader of Unity's built-in resources it could not resolve by its path id
                // ("builtin_extra#10770" = UI/Default, "builtin#10101" = GUI/Text Shader): not comparable
                bool unresolved = es != null && es.StartsWith("builtin", StringComparison.Ordinal) && es.Contains("#");
                if (!unresolved && o.ShaderOriginal != es) { shBad++; rFirst ??= $"{path}[{i}]: shader {o.ShaderOriginal} vs {e["shader"]}"; }
                var ek = ((JArray)e["keywords"]).Select(x => (string)x).ToArray();
                if (!o.Keywords.SequenceEqual(ek)) { kwBad++; rFirst ??= $"{path}[{i}]: keywords {string.Join(" ", o.Keywords)} vs {string.Join(" ", ek)}"; }
            }
        }
        if (erd.Count != renderers.Count || rMissing > 0) diffs.Add($"renderers: {renderers.Count} vs {erd.Count}, {rMissing} missing ({rFirst})");
        if (lmBad > 0) diffs.Add($"lightmapData: {lmBad} renderers ({rFirst})");
        if (shBad > 0) diffs.Add($"shaderOriginal: {shBad} slots ({rFirst})");
        if (kwBad > 0) diffs.Add($"keywords: {kwBad} slots ({rFirst})");
        // blocks: activeInHierarchy after the default flags
        var aab = ex["activeAfterBlocks"];
        if (aab != null && map.OptionFlag && map.LightFlag)
        {
            var activeL = new HashSet<int>(((JArray)aab["lights"]).Select(x => (int)x));
            int bad = 0;
            for (int i = 0; i < el.Count; i++)
                if (lights.TryGetValue((string)el[i][0], out var l) && l.gameObject.activeInHierarchy != activeL.Contains(i)) bad++;
            var activeR = new HashSet<int>(((JArray)aab["renderers"]).Select(x => (int)x));
            for (int i = 0; i < erd.Count; i++)
                if (renderers.TryGetValue((string)erd[i][0], out var r) && r.gameObject.activeInHierarchy != activeR.Contains(i)) bad++;
            if (bad > 0) diffs.Add($"activeAfterBlocks: {bad} objects");
        }
        return diffs;
    }
}
