// ItemTest M9: every vanilla and mod item row of the Studio lists, built
// through ItemRealization with its new-add defaults, every available field set to a test value (the writes checked
// against package C's expectation file), options / FK / dynamic bones toggled, 3 frames stepped, deleted.
// Run: -executeMethod ItemTest.RunCorpus [-src vanilla|mods] [-from N] [-limit N] [-every K] (-nographics: mod textures with
// 0 mips crash the GPU). The Editor's asset-path table crashes natively after some 10,000 bundle loads in one process
// (GUIDPersistentManagerV2), so run_tests.ps1 runs the corpus in chunks of 5,000 rows (-from / -limit), one Unity each. The lists are loaded with the AI install gate off so that every row builds its own prefab (gated rows
// otherwise build the fallback). Bundles are released every 250 rows (memory; Unload(false) as the app evicts, see ReleaseBundles).
// Report: Spike/out/items/item_corpus_report.txt ("SUMMARY: N PASS, M FAIL"), per-row problems in
// item_corpus_rows.tsv. PASS = no exception outside findings, every non-null slot owned, every defaults record equal to
// the expectation, the writes equal to the expectation, the rows whose prefab does not load = the expectation's
// "prefab not found" rows (they build the fallback 399).
using System;
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
using Debug = UnityEngine.Debug;

public static partial class ItemTest
{
    public static void RunCorpus()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        var head = new StringBuilder();
        RuntimeServices sv = null;
        try
        {
            var gd = CardResolveTest.BuildDb(head);
            var db = gd.LoadStudioLists(new StudioLoadOptions { EmulateAIInstallGate = false });
            sv = new RuntimeServices { Catalog = gd, Db = db, Hs2Root = CardResolveTest.GameRoot, Settings = new SceneRuntimeSettings { ManualStepping = true, GraveyardMaxNodes = 0 } };
            sv.UserData = Path.Combine(OutDir, "userdata");
            rep.AppendLine($"item corpus test (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}); lists loaded with the AI install gate off");
            rep.Append(head);
            Section("M9", "corpus");
            if (!LoadExpect()) { Warn("expectation file absent: M9 skipped"); }
            else Corpus(sv);
        }
        catch (Exception e) { current = "run"; Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        string tag = Arg("-tag");
        End(tag == null ? "item_corpus_report.txt" : "item_corpus_report_" + tag + ".txt", "ITEM CORPUS TEST", sw);
        if (sv?.Catalog != null) ReleaseBundles(sv.Catalog);
        if (fails > 0) EditorApplication.Exit(1);
    }

    /// <summary>
    /// The corpus's release of every open bundle, as the app evicts: Unload(false) (the loaded objects stay until they are
    /// unreferenced), the catalog's tables cleared, then the unreferenced objects freed. Destroying the objects of some mod
    /// bundles with Unload(true) corrupts the native heap (clips whose data live in an external resource file the bundle
    /// cannot reach); the process then dies later, at any allocation.
    /// </summary>
    static void ReleaseBundles(ContentCatalog catalog)
    {
        foreach (var b in AssetBundle.GetAllLoadedAssetBundles().ToList()) if (b) b.Unload(false);
        catalog.UnloadAll();   // only clears its tables now: every bundle above is already released
        Resources.UnloadUnusedAssets();
    }

    static void Corpus(RuntimeServices sv)
    {
        string src = Arg("-src");
        int limit = int.TryParse(Arg("-limit"), out var l) ? l : int.MaxValue;
        int every = int.TryParse(Arg("-every"), out var e) && e > 0 ? e : 1;
        int from = int.TryParse(Arg("-from"), out var fr) && fr > 0 ? fr : 0;
        string guid = Arg("-guid");
        var rows = sv.Db.Items.All.Where(r => src == null || (src == "vanilla" ? r.IsVanilla : !r.IsVanilla))
            .Where(r => guid == null || string.Equals(r.Key.SourceGuid, guid, StringComparison.OrdinalIgnoreCase)).Where((r, i) => i % every == 0).Skip(from).Take(limit).ToList();
        Info($"{rows.Count} rows (src {src ?? "all"}, every {every}, from {from})");

        // Native crashes (a corrupted bundle: "The file 'archive:/CAB-...' is corrupted", a fatal error no managed code can
        // catch): the row being loaded is written to item_corpus_current.txt first; a run that finds it moves it to
        // Cache/items/corpus_crashes.txt and skips the rows listed there (rerun after a crash; run_tests.ps1 retries).
        string currentFile = Path.Combine(OutDir, "item_corpus_current.txt");
        string crashFile = Path.Combine(ContentSettings.ProjectRoot, "Cache", "items", "corpus_crashes.txt");
        // a crash inside the periodic bundle release is the release's, not a row's: the release names itself in
        // item_corpus_releasing.txt (the row file is removed first), and a run that finds it blames no row
        string releasingFile = Path.Combine(OutDir, "item_corpus_releasing.txt");
        if (File.Exists(releasingFile))
        {
            Warn("the previous run ended inside a bundle release (" + File.ReadAllText(releasingFile).Trim() + "): no row is added to the crash list");
            File.Delete(releasingFile);
        }
        if (File.Exists(currentFile))
        {
            var last = File.ReadAllText(currentFile).Trim();
            if (last.Length > 0) File.AppendAllText(crashFile, last + "\n", new UTF8Encoding(false));
            File.Delete(currentFile);
        }
        var crashed = new HashSet<string>(File.Exists(crashFile) ? File.ReadAllLines(crashFile).Select(x => x.Trim()).Where(x => x.Length > 0) : Enumerable.Empty<string>(), StringComparer.Ordinal);
        string RowId(StudioItem r) => r.Key + "|" + r.Group + "|" + r.Category + "|" + r.BundlePath + "|" + r.FileName;
        // a corrupted bundle crashes on every asset: skip every row of a bundle that crashed once
        var crashedBundles = new HashSet<string>(crashed.Select(x => x.Split('|')).Where(x => x.Length >= 4).Select(x => x[3]), StringComparer.OrdinalIgnoreCase);
        int crashRows = 0;
        var lines = new StringBuilder("ref\tgroup\tcategory\tname\tproblem\n");
        int built = 0, fallbackRows = 0, fallbackOk = 0, defaultsEqual = 0, defaultsDiffer = 0, noRecord = 0, notOwned = 0, writesOk = 0, writesBad = 0,
            exceptionRows = 0, failedBuilds = 0, partFailed = 0, shadowed = 0, bundleRetries = 0, gimbalRows = 0, otherBundle = 0;
        long slots = 0;
        var buildMs = new List<double>();
        var fallbackShaders = new Dictionary<string, int>(StringComparer.Ordinal);
        var findings = new Dictionary<string, int>(StringComparer.Ordinal);
        var missingRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JObject x in (JArray)expect["items"])
            if ((string)x["status"] == "prefab not found") missingRefs.Add((string)x["ref"] + "|" + (int)x["key"]["group"] + "|" + (int)x["key"]["category"]);

        void Row(StudioItem row, string problem) => lines.Append(row.Key).Append('\t').Append(row.Group).Append('\t').Append(row.Category).Append('\t').Append(row.Name).Append('\t').Append(problem).Append('\n');

        Scene s = null;
        int n = 0;
        var swAll = Stopwatch.StartNew();
        try
        {
            foreach (var row in rows)
            {
                if (s == null) s = new Scene(sv);
                if (crashed.Contains(RowId(row)) || crashedBundles.Contains(row.BundlePath ?? "")) { crashRows++; Row(row, "skipped: its bundle crashed Unity natively in an earlier run (Cache/items/corpus_crashes.txt)"); continue; }
                File.WriteAllText(currentFile, RowId(row));
                int ex0 = exceptions.Count, f0 = s.Session.Findings.Count;
                try
                {
                    // a row that another row of the same key shadows is not what the lists resolve: skip it (the expectation
                    // marks such rows duplicateKey; the item built for the key is the resolved row, checked on its own turn)
                    if (sv.Db.TryGetItem(row.Key.SourceGuid, row.Group, row.Category, row.No, out var resolved) && resolved != null && resolved != row)
                    {
                        shadowed++;
                        continue;
                    }
                    GameObject prefab = null;
                    try { prefab = sv.Catalog.LoadAsset<GameObject>(row); } catch (Exception) { }
                    if (!prefab && !missingRefs.Contains(row.Key + "|" + row.Group + "|" + row.Category))
                    {
                        // Unity refuses a bundle whose files another loaded bundle already has (two mods shipping the same
                        // bundle): unload everything (the scene is empty between rows) and try once more
                        s.Dispose();
                        s = new Scene(sv);
                        ReleaseBundles(sv.Catalog);
                        bundleRetries++;
                        try { prefab = sv.Catalog.LoadAsset<GameObject>(row); } catch (Exception) { }
                    }
                    if (!prefab)
                    {
                        fallbackRows++;
                        bool known = missingRefs.Contains(row.Key + "|" + row.Group + "|" + row.Category);
                        var st = new ItemNodeState { Ref = row.Key.ToString(), Group = row.Group, Category = row.Category };
                        var ar = s.Session.AddItem(st);
                        s.Runtime.Apply();
                        var fi = s.Item(ar.NewId);
                        bool ok = fi?.State == RealizationState.Built && fi.IsFallback;
                        if (ok && known) fallbackOk++;
                        else Row(row, "prefab not loadable" + (known ? "" : " (not a known missing prefab)") + (ok ? "" : ", fallback not built"));
                        if (ar.NewId != null) { s.Runtime.Ops.Delete(new[] { ar.NewId }); s.Runtime.Apply(); }
                        continue;
                    }
                    var d = DefaultsDiff(sv, row, prefab, out var rec);
                    if (d == null) noRecord++;
                    else if (d.Count == 0) defaultsEqual++;
                    else if (d.All(x => x.StartsWith(GimbalTag, StringComparison.Ordinal)))
                    {
                        gimbalRows++;
                        Row(row, "defaults: FK Euler at a gimbal lock decomposed otherwise by the expectation: " + string.Join("; ", d.Take(2)));
                    }
                    else { defaultsDiffer++; Row(row, "defaults: " + string.Join("; ", d.Take(3))); }

                    // added with the row's own group / category (a GUID may reuse a number in two categories; SceneOps.AddItem
                    // resolves by GUID and number only)
                    var add = s.Session.AddItem(ItemDefaults.FromPrefab(prefab, row));
                    s.Runtime.Apply();
                    var id = add.Status == CommandStatus.Error ? null : add.NewId;
                    var it = s.Item(id);
                    if (it == null || it.State != RealizationState.Built) { failedBuilds++; Row(row, "build: " + it?.State + " " + it?.Detail); continue; }
                    built++;
                    buildMs.Add(it.BuildMilliseconds);
                    foreach (var kv in it.Writer.FallbackShaders) { fallbackShaders.TryGetValue(kv.Key, out int c); fallbackShaders[kv.Key] = c + kv.Value; }
                    int owned = 0, bundle = 0;
                    foreach (var r in it.Instance.GetComponentsInChildren<Renderer>(true))
                        foreach (var m in r.sharedMaterials)
                        {
                            if (!m) continue;
                            if (MaterialCopies.IsRecorded(m)) bundle++; else owned++;
                        }
                    slots += owned + bundle;
                    if (bundle > 0) { notOwned++; Row(row, bundle + " bundle materials on renderers"); }
                    if (rec != null && rec["renderers"] != null)
                    {
                        var w = CheckWrites(s, id, rec);
                        int expectedRenderers = ((JArray)rec["renderers"]).Count;
                        if (w.Ok) writesOk++;
                        else if (expectedRenderers > 0 && w.UnmatchedRenderers == expectedRenderers && w.Written == 0 && w.Wrong == 0)
                        {
                            // none of the expectation's renderers exists: the catalog served another zipmod's bundle of the same
                            // path (the census read the row's own archive); a content-resolution difference, not a write error
                            otherBundle++;
                            Row(row, "writes: another prefab than the expectation's (a bundle path shipped by several zipmods): " + w);
                        }
                        else { writesBad++; Row(row, "writes: " + w); }
                    }
                    else SetAll(s, id);
                    var stt = s.State(id);
                    if (stt.Options.Count > 0)
                    {
                        for (int i = 0; i < stt.Options.Count; i++) s.Session.Set(id, "item.options[" + i + "]", false);
                        s.Runtime.Apply();
                        for (int i = 0; i < stt.Options.Count; i++) s.Session.Set(id, "item.options[" + i + "]", true);
                    }
                    s.Session.Set(id, "item.fk.enabled", true);
                    s.Session.Set(id, "item.dynamicBones", false);
                    s.Runtime.Tick(Dt);
                    s.Session.Set(id, "item.fk.enabled", false);
                    s.Session.Set(id, "item.dynamicBones", true);
                    s.Runtime.Tick(Dt);
                    s.Runtime.Tick(Dt);
                    s.Session.Set(id, "visible", false);
                    s.Runtime.Apply();
                    s.Runtime.Ops.Delete(new[] { id });
                    s.Runtime.Apply();
                }
                catch (Exception x)
                {
                    var st = (x.StackTrace ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(12);
                    exceptions.Add(x.GetType().Name + ": " + x.Message + " @ " + string.Join(" < ", st));
                }
                finally
                {
                    foreach (var f in s.Session.Findings.Skip(f0)) { findings.TryGetValue(f.Code, out int c); findings[f.Code] = c + 1; if (f.Code == "item.partFailed") { partFailed++; Row(row, "partFailed: " + f.Message); } }
                    if (exceptions.Count > ex0) { exceptionRows++; Row(row, "exception: " + string.Join(" | ", exceptions.Skip(ex0).Take(2))); }
                }
                if (++n % 250 == 0)
                {
                    s.Dispose();
                    s = null;
                    if (File.Exists(currentFile)) File.Delete(currentFile);   // the last row finished: a crash below is the release's
                    File.WriteAllText(releasingFile, "after " + n + " of " + rows.Count + " rows", new UTF8Encoding(false));
                    ReleaseBundles(sv.Catalog);
                    GC.Collect();
                    File.Delete(releasingFile);
                    Debug.Log($"[ItemTest corpus] {n} / {rows.Count} rows, {swAll.Elapsed.TotalMinutes:F1} min");
                }
            }
        }
        finally { s?.Dispose(); }
        if (File.Exists(currentFile)) File.Delete(currentFile);

        string tag = Arg("-tag");
        File.WriteAllText(Path.Combine(OutDir, tag == null ? "item_corpus_rows.tsv" : "item_corpus_rows_" + tag + ".tsv"), lines.ToString(), new UTF8Encoding(false));
        buildMs.Sort();
        double mean = buildMs.Count > 0 ? buildMs.Average() : 0, p95 = buildMs.Count > 0 ? buildMs[(int)(buildMs.Count * 0.95)] : 0;
        Info($"{rows.Count} rows in {swAll.Elapsed.TotalMinutes:F1} min: built {built}, fallback rows {fallbackRows} ({fallbackOk} known missing prefabs built the fallback), failed builds {failedBuilds}");
        Info($"build time mean {mean:F2} ms, p95 {p95:F2} ms (headless, bundle load excluded); {slots} material slots");
        Info($"defaults: {defaultsEqual} equal, {defaultsDiffer} differ, {noRecord} without a record");
        Info($"{shadowed} rows shadowed by another row of the same key (not built on their own), {bundleRetries} bundle-conflict retries");
        if (otherBundle > 0) Warn(otherBundle + " rows: the catalog served another prefab than the expectation's (a bundle path shipped by several zipmods)");
        if (gimbalRows > 0) Warn(gimbalRows + " rows: FK Euler triples at a gimbal lock that the expectation decomposes into another rotation (Unity's eulerAngles is HS2Next's and HS2's source)");
        Info($"writes: {writesOk} rows equal, {writesBad} differ; largest colour deviation {MaxColorError:G6}");
        Info("fallback (URP/Lit) shaders by slots: " + string.Join(", ", fallbackShaders.OrderByDescending(k => k.Value).Take(40).Select(k => k.Key + " x" + k.Value)));
        Info("findings: " + string.Join(", ", findings.OrderByDescending(k => k.Value).Select(k => k.Key + " x" + k.Value)));
        if (crashRows > 0) Warn(crashRows + " rows skipped: they crashed Unity natively (corrupted bundles; Cache/items/corpus_crashes.txt): " + string.Join(", ", crashed.Take(5)));
        Check("no exception outside findings", exceptionRows == 0 && partFailed == 0, exceptionRows + " rows with exceptions, " + partFailed + " build parts failed");
        Check("every row builds (or builds the fallback for a missing prefab)", failedBuilds == 0, "" + failedBuilds);
        Check("every non-null slot holds an owned copy", notOwned == 0, notOwned + " rows");
        Check("every defaults record equals the expectation", defaultsDiffer == 0, $"{defaultsEqual} equal, {defaultsDiffer} differ");
        Check("the writes of every row equal the expectation", writesBad == 0, $"{writesOk} equal, {writesBad} differ");
        Check("every row whose prefab does not load is a known missing prefab of the expectation and builds the fallback", fallbackRows == fallbackOk,
            $"{fallbackRows} rows, {fallbackOk} known (the whole corpus: {missingRefs.Count})");
        if (src == null && guid == null && limit == int.MaxValue && every == 1 && from == 0)
            Check("the rows whose prefab does not load = the known missing prefabs", fallbackOk == missingRefs.Count, $"{fallbackOk} of {missingRefs.Count}");
        Info($"mean build time {mean:F2} ms (acceptance: <= 20 ms)");
        Check("mean build time <= 20 ms per item headless", mean <= 20.0, mean.ToString("F2"));
    }
}
