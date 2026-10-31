// The mod-wide sweep (Phase 5.2): every row of the work list that Tools/mod_sweep_report.py worklist wrote
// from the zipmod scan cache is loaded through the catalog's entry points (so the per-asset redirect, the loose PNGs, the
// texture check and the in-process texture guard all apply) and gets one result line with its class.
//   part   a character-list entry: every asset column (prefabs, textures, the face's material and shape data, the pose
//          controller and IK data) loaded; a prefab is instantiated under an inactive root, its renderers' shaders mapped,
//          checked for the component its kind needs and destroyed; accessory prefabs whose move transforms are also chain
//          bones are counted
//   anime  a Studio anime row: its controller loaded and its clips counted (no playback)
//   loose  a loose PNG entry: decoded and freed (16-bit files counted); an entry that answers a Studio record's bundle path
//          is also loaded through the Studio entry point and must come back as the loose texture
//   bundle a bundle no list row names: opened only (the texture check reads it at the open)
//   item   a Studio item row the item corpus skipped (its bundle is in the corpus crash list): the prefab loaded, then built,
//          stepped and deleted as the corpus does; other item rows and the map rows come from the corpora's rows files
//   fixture  test rows of the work list written with `worklist --sw3`: key "crash" ends the process natively (the check of
//          the crash isolation), key "ok" loads nothing
// One process loads at most -maxLoads bundles (default 4,000; the Editor dies natively after about 10,000 opens in one
// process) and is resumable: the result lines are appended to <dir>/results.tsv as rows finish, and the row in flight is
// named in <dir>/current.txt. A process that finds current.txt left by a dead chunk process runs that row alone and stops
// (the runner bounds it at a few minutes): a row that loads alone is a load-count crash (flag lcc), a row that also kills
// the lone process (or one started with -oneRow) is class crash.
// Run (headless; one Unity at a time through Tools/unity_batch.ps1, repeated until `mod_sweep_report.py status` says done):
//   -executeMethod ModSweepTest.Run -dir <sweep dir> [-maxLoads 4000] [-maxMin 45] [-oneRow] [-limit N] [-kinds part,loose]
// Outputs in <dir>: results.tsv (idx kind class ms loads flags keys detail), shaders.tsv (idx, original shader, fallback
// slots), progress.tsv (one line per process), zipset.tsv (every scanned zipmod file and why the zipmod loader skips it).
// Numbers and ids only; no image is written or viewed.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.App.Commands;
using HS2Next.Chara;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class ModSweepTest
{
    // the classes of Tools/mod_sweep_report.py in the order "the first that applies"
    // ("refused": the pre-load check stopped the load with a finding - an asset that needs a truncated mesh, a quarantined bundle)
    static readonly string[] Classes = { "crash", "deviceLoss", "exception", "textureRefused", "refused", "nullLoad", "badPrefab", "missingAsset", "missingBundle", "asHS2" };

    // character-list asset columns HS2 reads (other columns of mod lists are ignored by the game)
    static readonly string[] TextureColumns =
    {
        "MainTex", "MainTex02", "MainTex03", "ColorMaskTex", "ColorMask02Tex", "ColorMask03Tex", "AddTex", "GlossTex", "NormalMapTex",
        "OcclusionMapTex", "TexD", "TexC", "ThumbTex", "OverBodyMask", "OverBodyBMask", "OverBraMask", "OverInnerTBMask",
        "OverInnerBMask", "OverPanstMask",
    };
    static readonly string[] OtherColumns = { "MainData", "MainData02", "MatData", "ShapeAnime", "IKData" };

    const double SlowMs = 2000;
    static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

    sealed class Row
    {
        public int Idx, Zi;
        public string Kind, Key, Bundle, Asset, Extra;
        public bool Run;
    }

    sealed class Result
    {
        public string Class, Detail = "";
        public readonly List<string> Flags = new List<string>();
        public readonly List<string> Keys = new List<string>();
        public double Ms;
        public int Loads;

        public void Add(string cls, string detail)
        {
            if (cls == null) return;
            if (Class == null || Array.IndexOf(Classes, cls) < Array.IndexOf(Classes, Class)) { Class = cls; Detail = detail ?? ""; }
        }
    }

    static string dir;
    static ContentCatalog gd;
    static StudioDb db;
    static RuntimeServices sv;
    static ItemTest.Scene scene;
    static GameObject holder;
    static readonly List<string> logExceptions = new List<string>();
    static readonly List<string> logErrors = new List<string>();
    static Dictionary<string, ZipmodInfo> zipByPath;
    static List<string> zipPaths;
    static HashSet<string> studioBundles;
    static Dictionary<string, List<StudioAnime>> animeByZip;
    static Dictionary<string, List<StudioItem>> itemsByZip;

    static string Arg(string name, string def = null)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static bool Flag(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

    static string F(double x) => x.ToString("0.###", CultureInfo.InvariantCulture);

    static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    public static void Run()
    {
        var swAll = Stopwatch.StartNew();
        dir = Arg("-dir");
        if (string.IsNullOrEmpty(dir) || !File.Exists(Path.Combine(dir, "worklist.tsv")))
        {
            Debug.LogError("[ModSweep] -dir <sweep dir> with a worklist.tsv (Tools/mod_sweep_report.py worklist) expected");
            EditorApplication.Exit(2);
            return;
        }
        int maxLoads = int.TryParse(Arg("-maxLoads"), out var ml) ? ml : 4000;
        double maxMin = double.TryParse(Arg("-maxMin"), NumberStyles.Float, CultureInfo.InvariantCulture, out var mm) ? mm : 45;
        int limit = int.TryParse(Arg("-limit"), out var lim) ? lim : int.MaxValue;
        bool oneRow = Flag("-oneRow");
        var kinds = Arg("-kinds")?.Split(',', '+').Select(x => x.Trim()).Where(x => x.Length > 0).ToHashSet();
        string start = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        string mode = "chunk", reason = "";
        int first = -1, last = -1, rowsDone = 0, loads = 0;
        Application.logMessageReceived += OnLog;
        try
        {
            var rows = ReadWorklist();
            var done = DoneRows();
            var byIdx = rows.ToDictionary(x => x.Idx);
            string current = Path.Combine(dir, "current.txt");

            // a row left in flight by a dead process
            Row isolate = null;
            if (File.Exists(current))
            {
                var t = File.ReadAllText(current).Split('\t');
                File.Delete(current);
                if (t.Length >= 2 && int.TryParse(t[0], out int ci) && byIdx.TryGetValue(ci, out var crow) && !done.Contains(ci))
                {
                    string step = t.Length > 3 ? t[3] : "";
                    if (t[1] == "alone" || t[1] == "isolate")
                    {
                        var r = new Result();
                        r.Add("crash", "the process died on this row alone" + (step.Length > 0 ? " (" + step + ")" : ""));
                        Append(crow, r);
                        done.Add(ci);
                        File.AppendAllText(Path.Combine(dir, "crashes.tsv"), ci + "\t" + crow.Kind + "\t" + Clean(crow.Key) + "\t" + Clean(step) + "\n", Utf8);
                    }
                    else
                    {
                        isolate = crow;
                        mode = "isolate";
                        isolateLoads = t.Length > 2 && int.TryParse(t[2], out int il) ? il : -1;
                    }
                }
            }
            if (isolate == null && oneRow) mode = "alone";

            var todo = isolate != null ? new List<Row> { isolate }
                : rows.Where(r => r.Run && !done.Contains(r.Idx) && (kinds == null || kinds.Contains(r.Kind))).Take(oneRow ? 1 : limit).ToList();
            if (todo.Count == 0) { reason = "nothing left"; return; }

            Setup();
            WriteZipset();
            int sinceUnload = 0, lastZi = -1;
            foreach (var row in todo)
            {
                if (mode == "chunk" && (loads >= maxLoads - 50 || swAll.Elapsed.TotalMinutes >= maxMin)) { reason = loads >= maxLoads - 50 ? "load bound" : "time bound"; break; }
                if (row.Zi != lastZi && lastZi >= 0 && (gd.OpenBundleCount >= 48 || sinceUnload >= 300)) { UnloadAll(); sinceUnload = 0; }
                else if (sinceUnload >= 300) { UnloadAll(); sinceUnload = 0; }
                lastZi = row.Zi;
                File.WriteAllText(current, row.Idx + "\t" + mode + "\t" + loads + "\t" + row.Kind, Utf8);
                var r = Process(row, mode != "chunk");
                if (mode == "isolate")
                {
                    r.Flags.Add("lcc=" + isolateLoads);
                    if (r.Detail.Length == 0) r.Detail = "crashed a chunk process after " + isolateLoads + " bundle loads; alone it loads";
                }
                loads += r.Loads;
                Append(row, r);
                File.Delete(current);
                if (first < 0) first = row.Idx;
                last = row.Idx;
                rowsDone++;
                sinceUnload++;
                if (rowsDone % 500 == 0) Debug.Log($"[ModSweep] {rowsDone} rows, {loads} bundle loads, {swAll.Elapsed.TotalMinutes:F1} min");
            }
            if (reason.Length == 0) reason = mode == "chunk" && rowsDone == todo.Count ? "list end" : mode;
        }
        catch (Exception e)
        {
            reason = "exception " + Clean(e.GetType().Name + ": " + e.Message);
            Debug.LogError("[ModSweep] " + e);
        }
        finally
        {
            Application.logMessageReceived -= OnLog;
            try { UnloadAll(); } catch (Exception) { }
            if (holder) Object.DestroyImmediate(holder);
            File.AppendAllText(Path.Combine(dir, "progress.tsv"),
                $"{Process_Id()}\t{start}\t{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}\t{mode}\t{first}\t{last}\t{rowsDone}\t{loads}\t{F(swAll.Elapsed.TotalMinutes)}\t{reason}\n", Utf8);
            Debug.Log($"[ModSweep] DONE: {mode}, {rowsDone} rows, {loads} bundle loads, {swAll.Elapsed.TotalMinutes:F1} min ({reason})");
        }
    }

    static int isolateLoads = -1;
    static int Process_Id() => System.Diagnostics.Process.GetCurrentProcess().Id;

    static void OnLog(string condition, string stack, LogType type)
    {
        if (type == LogType.Exception) logExceptions.Add(condition.Length > 300 ? condition.Substring(0, 300) : condition);
        else if (type == LogType.Error) logErrors.Add(condition.Length > 200 ? condition.Substring(0, 200) : condition);
    }

    // ------------------------------------------------------------------ inputs and outputs

    static List<Row> ReadWorklist()
    {
        var rows = new List<Row>();
        var crashBundles = CorpusCrashBundles();
        foreach (var line in File.ReadLines(Path.Combine(dir, "worklist.tsv")))
        {
            if (line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("idx\t", StringComparison.Ordinal)) continue;
            var t = line.Split('\t');
            if (t.Length < 7) continue;
            var r = new Row { Idx = int.Parse(t[0], CultureInfo.InvariantCulture), Kind = t[1], Zi = int.Parse(t[2], CultureInfo.InvariantCulture), Key = t[3], Bundle = t[4], Asset = t[5], Extra = t[6] };
            r.Run = r.Kind == "part" || r.Kind == "anime" || r.Kind == "loose" || r.Kind == "bundle" || r.Kind == "fixture" || (r.Kind == "item" && crashBundles.Contains(r.Bundle));
            rows.Add(r);
        }
        zipPaths = new List<string>();
        foreach (var line in File.ReadLines(Path.Combine(dir, "zipmods.tsv")).Skip(1))
        {
            var t = line.Split('\t');
            if (t.Length >= 2) zipPaths.Add(t[1]);
        }
        return rows;
    }

    // the bundles of Cache/items/corpus_crashes.txt (the item corpus skips every row of them)
    static HashSet<string> CorpusCrashBundles()
    {
        var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string f = Path.Combine(ContentSettings.ProjectRoot, "Cache", "items", "corpus_crashes.txt");
        if (File.Exists(f))
            foreach (var line in File.ReadAllLines(f))
            {
                var t = line.Trim().Split('|');
                if (t.Length >= 4) s.Add(t[3].Replace('\\', '/'));
            }
        return s;
    }

    static HashSet<int> DoneRows()
    {
        var s = new HashSet<int>();
        string f = Path.Combine(dir, "results.tsv");
        if (!File.Exists(f)) return s;
        foreach (var line in File.ReadLines(f))
        {
            int tab = line.IndexOf('\t');
            if (tab > 0 && int.TryParse(line.Substring(0, tab), out int i)) s.Add(i);
        }
        return s;
    }

    static void Append(Row row, Result r)
    {
        string f = Path.Combine(dir, "results.tsv");
        if (!File.Exists(f)) File.WriteAllText(f, "idx\tkind\tclass\tms\tloads\tflags\tkeys\tdetail\n", Utf8);
        File.AppendAllText(f, $"{row.Idx}\t{row.Kind}\t{r.Class ?? "ok"}\t{F(r.Ms)}\t{r.Loads}\t{string.Join(",", r.Flags)}\t{Clean(string.Join("|", r.Keys.Distinct()))}\t{Clean(r.Detail)}\n", Utf8);
    }

    static void WriteZipset()
    {
        string f = Path.Combine(dir, "zipset.tsv");
        if (File.Exists(f)) return;
        var sb = new StringBuilder("path\tguid\tskippedBy\tloadOrder\n");
        var order = new Dictionary<ZipmodInfo, int>();
        for (int i = 0; i < gd.ZipmodsInLoadOrder.Count; i++) order[gd.ZipmodsInLoadOrder[i]] = i;
        foreach (var z in gd.ScannedZipmods)
        {
            string skipped = z.SkippedBy ?? (order.ContainsKey(z) ? "" : "notLoaded");
            sb.Append(Clean(z.Path)).Append('\t').Append(Clean(z.Guid)).Append('\t').Append(skipped).Append('\t').Append(order.TryGetValue(z, out int o) ? o : -1).Append('\n');
        }
        File.WriteAllText(f, sb.ToString(), Utf8);
    }

    // ------------------------------------------------------------------ setup

    static void Setup()
    {
        var head = new StringBuilder();
        gd = CardResolveTest.BuildDb(head);
        db = gd.LoadStudioLists(new StudioLoadOptions { EmulateAIInstallGate = false });
        sv = new RuntimeServices { Catalog = gd, Db = db, Hs2Root = CardResolveTest.GameRoot, Settings = new SceneRuntimeSettings { ManualStepping = true, GraveyardMaxNodes = 0 } };
        sv.UserData = Path.Combine(dir, "userdata");
        zipByPath = new Dictionary<string, ZipmodInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var z in gd.ScannedZipmods) if (z.Path != null) zipByPath[z.Path] = z;
        studioBundles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in db.Items.All) if (!string.IsNullOrEmpty(it.BundlePath)) studioBundles.Add(it.BundlePath.Replace('\\', '/'));
        foreach (var m in db.Maps.All) if (!string.IsNullOrEmpty(m.BundlePath)) studioBundles.Add(m.BundlePath.Replace('\\', '/'));
        animeByZip = db.Anime.All.Where(a => a.Zipmod != null).GroupBy(a => a.Zipmod.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        itemsByZip = db.Items.All.Where(a => a.Zipmod != null).GroupBy(a => a.Zipmod.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        holder = new GameObject("ModSweep");
        holder.SetActive(false);
        Debug.Log("[ModSweep] " + head.ToString().Trim());
    }

    // the release between chunks of rows, as the app evicts: Unload(false) for every open bundle (destroying the objects of some
    // mod bundles with Unload(true) corrupts the native heap: clips whose data live in an external resource file the bundle
    // cannot reach), the catalog's tables cleared, then the unreferenced objects freed
    static void UnloadAll()
    {
        scene?.Dispose();
        scene = null;
        if (gd == null) return;
        foreach (var b in AssetBundle.GetAllLoadedAssetBundles().ToList()) if (b) b.Unload(false);
        gd.UnloadAll();
        Resources.UnloadUnusedAssets();
        GC.Collect();
    }

    // ------------------------------------------------------------------ one row

    static Result Process(Row row, bool verbose)
    {
        var r = new Result();
        var sw = Stopwatch.StartNew();
        int open0 = gd.OpenBundleCount, clash0 = gd.CabClashes.Count, ex0 = logExceptions.Count, err0 = logErrors.Count, tf0 = gd.TextureFindings.Count;
        TextureGuard.BeginBuild();
        ZipmodInfo z = row.Zi >= 0 && row.Zi < zipPaths.Count && zipByPath.TryGetValue(zipPaths[row.Zi], out var zz) ? zz : null;
        try
        {
            if (row.Kind == "fixture") Fixture(row, r);
            else if (z == null) r.Add("missingBundle", "zipmod file not in the scan");
            else if (z.SkippedBy != null || !gd.ZipmodsByGuid.TryGetValue(z.Guid ?? "", out var loaded) || loaded != z)
                r.Class = "outside";
            else
                switch (row.Kind)
                {
                    case "part": Part(row, z, r, verbose); break;
                    case "anime": Anime(row, z, r); break;
                    case "loose": Loose(row, z, r); break;
                    case "bundle": Bundle(row, z, r); break;
                    case "item": Item(row, z, r, verbose); break;
                    default: r.Add("exception", "unknown kind " + row.Kind); break;
                }
        }
        catch (Exception e)
        {
            var st = (e.StackTrace ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(4);
            r.Add("exception", e.GetType().Name + ": " + e.Message + " @ " + string.Join(" < ", st));
        }
        if (r.Class != "outside")
        {
            foreach (var b in TextureGuard.TakePending())
                if (b.Repaired) r.Flags.Add("repaired");
                else r.Add("textureRefused", "texture " + b.Texture + " " + b.Width + "x" + b.Height + " " + b.Format + " mips " + b.Mips + ": " + b.Reason);
            // the pre-load check's refusals of this row's loads (the catalog's findings)
            for (int i = tf0; i < gd.TextureFindings.Count; i++)
            {
                var f = gd.TextureFindings[i];
                if (f == null) continue;
                if (f.Kind == "texture.malformedBundle" || f.Kind == "texture.patchFailed") r.Add("textureRefused", f.Kind + ": " + f.Detail);
                else if (f.Kind == "content.meshTruncated" || f.Kind == "content.quarantined") r.Add("refused", f.Kind + ": " + f.Detail);
                else if (f.Kind == "texture.patched") r.Flags.Add("patched");
            }
            if (logExceptions.Count > ex0) r.Add("exception", logExceptions[ex0]);
            if (gd.CabClashes.Count > clash0) r.Flags.Add("cabClash=" + (gd.CabClashes.Count - clash0));
            if (logErrors.Count > err0)
            {
                r.Flags.Add("err=" + (logErrors.Count - err0));
                if (r.Class == null && r.Detail.Length == 0) r.Detail = "error: " + logErrors[err0];
            }
        }
        r.Ms = sw.Elapsed.TotalMilliseconds;
        r.Loads = Math.Max(0, gd.OpenBundleCount - open0);
        return r;
    }

    static void Step(Row row, string step)
    {
        File.WriteAllText(Path.Combine(dir, "current.txt"), row.Idx + "\t" + (Flag("-oneRow") ? "alone" : "isolate") + "\t0\t" + step, Utf8);
    }

    static double Timed(Result r, Func<Object> load, out Object o)
    {
        var sw = Stopwatch.StartNew();
        o = load();
        double ms = sw.Elapsed.TotalMilliseconds;
        if (ms > SlowMs) r.Flags.Add("slow=" + F(ms));
        return ms;
    }

    // ---- part: a character-list entry

    static Type ColumnType(int category, string column)
    {
        switch (column)
        {
            case "MainData": case "MainData02": return category == 500 || category == 501 ? typeof(RuntimeAnimatorController) : typeof(GameObject);
            case "MatData": return typeof(Material);
            case "ShapeAnime": case "IKData": return typeof(TextAsset);
            default: return typeof(Texture2D);
        }
    }

    static Object LoadTyped(ListEntry e, string bundleCol, string col, Type t)
    {
        if (t == typeof(GameObject)) return gd.LoadAsset<GameObject>(e, bundleCol, col);
        if (t == typeof(RuntimeAnimatorController)) return gd.LoadAsset<RuntimeAnimatorController>(e, bundleCol, col);
        if (t == typeof(Material)) return gd.LoadAsset<Material>(e, bundleCol, col);
        if (t == typeof(TextAsset)) return gd.LoadAsset<TextAsset>(e, bundleCol, col);
        return gd.LoadAsset<Texture2D>(e, bundleCol, col);
    }

    static void Part(Row row, ZipmodInfo z, Result r, bool verbose)
    {
        int ei = int.Parse(row.Extra, CultureInfo.InvariantCulture);
        var parts = row.Key.Split('/');
        int cat = int.Parse(parts[0], CultureInfo.InvariantCulture);
        ListEntry e = ei < z.Entries.Count && z.Entries[ei].Category == cat && z.Entries[ei].Key.Id.ToString(CultureInfo.InvariantCulture) == parts[1] ? z.Entries[ei]
            : z.Entries.FirstOrDefault(x => x.Category == cat && x.Key.Id.ToString(CultureInfo.InvariantCulture) == parts[1]);
        if (e == null) { r.Add("missingAsset", "entry not in the zipmod's scan"); return; }
        if (gd.TryGetEntry(cat, e.Key, out var winner) && winner != e) { r.Flags.Add("shadowed"); return; }
        int loadsCols = 0;
        foreach (var col in OtherColumns.Concat(TextureColumns))
        {
            string asset = e[col];
            if (string.IsNullOrEmpty(asset) || asset == "0") continue;
            string bundleCol = col == "IKData" ? "IKAB" : ContentCommands.BundleColumnOf(e, col);
            string bundle = e[bundleCol];
            var type = ColumnType(cat, col);
            if (verbose) Step(row, "load " + col);
            loadsCols++;
            Timed(r, () => LoadTyped(e, bundleCol, col, type), out var o);
            r.Keys.Add(ServedKey(e, bundle, asset));
            if (!o) { Diagnose(r, e, z, bundle, asset, type, col); continue; }
            if (o is GameObject prefab) Prefab(row, r, prefab, cat, col, verbose);
        }
        r.Flags.Add("cols=" + loadsCols);
    }

    // the catalog key of the copy a list row's load is served from (zip file + "!" + path, or "abdata:" + path)
    static string ServedKey(ListEntry e, string bundle, string asset)
    {
        if (string.IsNullOrEmpty(bundle) || bundle == "0") return "";
        string p = bundle.Replace('\\', '/');
        var s = ContentCatalog.SideloaderAssetRedirect && gd.Redirect != null && gd.Redirect.IsRedirectable(p) ? gd.Redirect.Serve(p, asset) : gd.TodayOf(e, p);
        if (s.Zip is ZipmodInfo z) return z.Path + "!" + p;
        return s.Abdata ? "abdata:" + p : "";
    }

    static string StudioKey(ZipmodInfo owner, string bundle)
    {
        if (string.IsNullOrEmpty(bundle) || bundle == "0") return "";
        bundle = bundle.Replace('\\', '/');
        if (gd.HasRealBundle(bundle)) return "abdata:" + bundle;
        if (owner != null && owner.HasBundle(bundle)) return owner.Path + "!" + bundle;
        var z = gd.FindZipmodShipping(bundle);
        return z != null ? z.Path + "!" + bundle : "";
    }

    static string RequiredComponent(int cat)
    {
        if (cat >= 240 && cat <= 247) return "CmpClothes";
        if (cat >= 300 && cat <= 303) return "CmpHair";
        if (cat >= 350 && cat <= 363) return "CmpAccessory";
        return null;
    }

    static bool HasComponent(GameObject go, string name)
    {
        switch (name)
        {
            case "CmpClothes": return go.GetComponent<AIChara.CmpClothes>();
            case "CmpHair": return go.GetComponent<AIChara.CmpHair>();
            case "CmpAccessory": return go.GetComponent<AIChara.CmpAccessory>();
            default: return true;
        }
    }

    static void Prefab(Row row, Result r, GameObject prefab, int cat, string col, bool verbose)
    {
        // the game draws a part without its kind's component (a hair without CmpHair, a garment without CmpClothes): only the
        // part's colour / option controls need it, so its absence is a flag; a part is "bad" when it has nothing to draw: the
        // part's build places the whole prefab, so an empty prefab (no renderer and no light: the lists' dummies) is the only case
        string need = RequiredComponent(cat);
        if (need != null && !HasComponent(prefab, need)) r.Flags.Add("no" + need);
        if (verbose) Step(row, "instantiate " + col);
        var inst = Object.Instantiate(prefab, holder.transform);
        try
        {
            int all = inst.GetComponentsInChildren<Renderer>(true).Length, lights = inst.GetComponentsInChildren<Light>(true).Length;
            if (lights > 0) r.Flags.Add("lights=" + lights);
            if (need != null && all == 0 && lights == 0) r.Add("badPrefab", col + ": an empty prefab (no renderer, no light)");
            if (cat >= 350 && cat <= 363 && inst.GetComponent<AIChara.CmpAccessory>() is AIChara.CmpAccessory acc && acc && ChainBoneMove(inst, acc)) r.Flags.Add("sw8");
            if (verbose) Step(row, "shaders " + col);
            var before = new Dictionary<Renderer, string[]>();
            foreach (var rd in inst.GetComponentsInChildren<Renderer>(true))
                before[rd] = rd.sharedMaterials.Select(m => m && m.shader ? m.shader.name : null).ToArray();
            ShaderMap.ApplyUrp(inst);
            int fb = 0;
            var copies = new HashSet<Material>();
            var byShader = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var rd in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mats = rd.sharedMaterials;
                before.TryGetValue(rd, out var names);
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!mats[i]) continue;
                    copies.Add(mats[i]);
                    if (!ShaderMap.IsFallback(mats[i])) continue;
                    fb++;
                    string n = names != null && i < names.Length && names[i] != null ? names[i] : "(unknown)";
                    byShader.TryGetValue(n, out int c);
                    byShader[n] = c + 1;
                }
            }
            if (fb > 0)
            {
                r.Flags.Add("fb=" + fb);
                var sb = new StringBuilder();
                foreach (var kv in byShader) sb.Append(row.Idx).Append('\t').Append(Clean(kv.Key)).Append('\t').Append(kv.Value).Append('\n');
                File.AppendAllText(Path.Combine(dir, "shaders.tsv"), sb.ToString(), Utf8);
            }
            foreach (var m in copies) Object.DestroyImmediate(m);
        }
        finally { Object.DestroyImmediate(inst); }
    }

    // an accessory whose move transform (trfMove01 / trfMove02) is also a bone of a DynamicBone chain (5.7 row l.640)
    static bool ChainBoneMove(GameObject inst, AIChara.CmpAccessory acc)
    {
        var moves = new[] { acc.trfMove01, acc.trfMove02 }.Where(t => t).ToList();
        if (moves.Count == 0) return false;
        foreach (var b in inst.GetComponentsInChildren<DynamicBone>(true))
        {
            var roots = new List<Transform>();
            if (b.m_Root) roots.Add(b.m_Root);
            if (b.m_Roots != null) roots.AddRange(b.m_Roots.Where(t => t));
            var excl = new HashSet<Transform>((b.m_Exclusions ?? new List<Transform>()).Where(t => t));
            foreach (var root in roots)
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (Excluded(t, root, excl)) continue;
                    if (moves.Contains(t)) return true;
                }
        }
        return false;
    }

    static bool Excluded(Transform t, Transform root, HashSet<Transform> excl)
    {
        for (var p = t; p; p = p.parent)
        {
            if (excl.Contains(p)) return true;
            if (p == root) break;
        }
        return false;
    }

    // why a load returned null: no bundle, no such asset (or another type), or Unity returns null for an asset the bundle has
    static void Diagnose(Result r, ListEntry e, ZipmodInfo owner, string bundle, string asset, Type type, string what)
    {
        if (string.IsNullOrEmpty(bundle) || bundle == "0") { r.Add("missingBundle", what + ": no bundle column"); return; }
        bundle = bundle.Replace('\\', '/');
        bool loose = gd.Loose.HasFolder(bundle);
        bool exists = gd.BundleExists(bundle) || (owner != null && owner.HasBundle(bundle));
        if (!exists && !loose) { r.Add("missingBundle", what + ": bundle in no source"); return; }
        if (!exists) { r.Add("missingAsset", what + ": loose PNG folder without this file"); return; }
        AssetBundle b = e != null ? gd.LoadBundle(e, bundle) : gd.LoadBundle(owner, bundle, "");
        if (!b) { NotOpened(r, what, bundle); return; }
        // never load past the pre-load check here: a refused asset would end the process
        if (gd.IsAssetRefused(b, asset)) { r.Add("refused", what + ": refused by the pre-load check"); return; }
        if (!b.Contains(asset))
        {
            var x = e != null ? gd.ExplainLoad(e, bundle, asset) : null;
            bool other = x != null && x.Copies.Any(c => c.Has == true);
            r.Add(other ? "nullLoad" : "missingAsset", what + (other ? ": the names of the bundle list the asset, Unity does not find it under this name" : ": asset not in the bundle"));
            return;
        }
        var any = b.LoadAsset(asset);
        if (any) r.Add("missingAsset", what + ": " + type.Name + " requested, the bundle holds a " + any.GetType().Name);
        else r.Add("nullLoad", what + ": the bundle lists the asset, the load returns null");
    }

    // the row of a key a list file repeats: the work list's occurrence number (extra) picks the same definition in list order
    static T Nth<T>(IEnumerable<T> same, Row row) where T : class
    {
        var list = same.ToList();
        int n = int.TryParse(row.Extra, NumberStyles.Integer, CultureInfo.InvariantCulture, out var o) && o > 0 ? o : 1;
        return list.Count >= n ? list[n - 1] : list.FirstOrDefault();
    }

    // a bundle that did not open: refused by the pre-load check (a finding of the catalog names its path - the check reports
    // a refused bundle once, the first time) or not opened by Unity
    static void NotOpened(Result r, string what, string bundle)
    {
        string p = (bundle ?? "").Replace('\\', '/');
        var f = gd.TextureFindings.LastOrDefault(x => x != null && x.Key != null && x.Key.Replace('\\', '/').EndsWith(p, StringComparison.OrdinalIgnoreCase)
                                                      && (x.Kind == "texture.malformedBundle" || x.Kind == "texture.patchFailed" || x.Kind == "content.quarantined"));
        if (f == null) r.Add("nullLoad", what + ": bundle did not open");
        else r.Add(f.Kind == "content.quarantined" ? "refused" : "textureRefused", what + ": " + f.Kind);
    }

    // a Studio list row the list loader did not keep: a row whose bundle no source ships is dropped (as the game's loader does)
    static void NotInDb(Result r, ZipmodInfo z, string bundle)
    {
        string p = (bundle ?? "").Replace('\\', '/');
        bool exists = p.Length > 0 && p != "0" && (gd.BundleExists(p) || z.HasBundle(p) || gd.Loose.HasFolder(p));
        if (!exists) r.Add("missingBundle", "row dropped by the list loader: its bundle is in no source");
        else r.Add("nullLoad", "row not in the list DB");
    }

    // ---- anime: a Studio anime row's controller

    static void Anime(Row row, ZipmodInfo z, Result r)
    {
        var k = row.Key.Split('/');
        int g = int.Parse(k[0], CultureInfo.InvariantCulture), c = int.Parse(k[1], CultureInfo.InvariantCulture), no = int.Parse(k[2], CultureInfo.InvariantCulture);
        var a = animeByZip.TryGetValue(z.Path, out var l) ? Nth(l.Where(x => x.Group == g && x.Category == c && x.No == no), row) : null;
        if (a == null) { NotInDb(r, z, row.Bundle); return; }
        r.Keys.Add(StudioKey(z, a.BundlePath));
        Timed(r, () => gd.LoadAsset<RuntimeAnimatorController>(a), out var o);
        if (!o) { Diagnose(r, null, z, a.BundlePath, a.FileName, typeof(RuntimeAnimatorController), "controller"); return; }
        r.Flags.Add("clips=" + ((RuntimeAnimatorController)o).animationClips.Length);
    }

    // ---- loose: a loose PNG entry

    static void Loose(Row row, ZipmodInfo z, Result r)
    {
        string path = row.Key;
        if (z.Index.Entries.TryGetValue("abdata/" + path, out var ze))
        {
            var bytes = z.Index.ReadAll(ze);
            // PNG signature (8) + IHDR length (4) + "IHDR" (4) + width (4) + height (4) + bit depth (1)
            if (bytes != null && bytes.Length > 24 && bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[12] == (byte)'I' && bytes[13] == (byte)'H' && bytes[24] == 16)
                r.Flags.Add("png16");
        }
        else { r.Add("missingAsset", "zip entry not found"); return; }
        var hit = new LooseTextures.Hit(z, path);
        Timed(r, () => gd.Loose.Acquire(hit), out var o);
        var tex = o as Texture2D;
        if (!tex) { r.Add("nullLoad", "the PNG did not decode"); return; }
        gd.Loose.Release(tex);
        // a Studio record's bundle path: the Studio entry point must answer with the loose texture
        if (!string.IsNullOrEmpty(row.Bundle) && studioBundles.Contains(row.Bundle))
        {
            var t = gd.LoadAssetAt<Texture2D>(z, row.Bundle, "", row.Asset);
            r.Flags.Add("sw6=" + (t && gd.Loose.Owns(t) ? "ok" : "fail"));
        }
    }

    // ---- bundle: a bundle no list row names

    static void Bundle(Row row, ZipmodInfo z, Result r)
    {
        string path = row.Key.Replace('\\', '/');
        r.Keys.Add(z.Path + "!" + path);
        int clash0 = gd.CabClashes.Count;
        Timed(r, () => z.OpenBundle(path, gd), out var o);
        var b = o as AssetBundle;
        if (!b)
        {
            if (gd.CabClashes.Count > clash0) return;   // another copy of the same content is open (the CAB rule decides)
            NotOpened(r, "bundle", path);
            return;
        }
        r.Flags.Add(b.isStreamedSceneAssetBundle ? "scenes=" + b.GetAllScenePaths().Length : "assets=" + b.GetAllAssetNames().Length);
    }

    // ---- fixture: the crash isolation's own check (test rows only)

    static void Fixture(Row row, Result r)
    {
        if (row.Key != "crash") { r.Flags.Add("fixture"); return; }
        Debug.Log("[ModSweep] fixture row " + row.Idx + ": a native abort on purpose");
        UnityEngine.Diagnostics.Utils.ForceCrash(UnityEngine.Diagnostics.ForcedCrashCategory.Abort);
    }

    // ---- a range of Studio item rows built as the item corpus builds them, released every -every rows (diagnosis of a
    // release-time native crash): -executeMethod ModSweepTest.RunItemRange -from N -limit M [-every K] [-oneByOne]
    // [-guid G] [-skipGuid G,..] [-onlyGuid G,..] [-noTextureCheck] [-noVideo] [-unloadFalse] [-softBundle part]. Each row and
    // each release step is logged before it runs ("[WRange] ..."), so a native crash names the last row or the bundle being
    // unloaded; -oneByOne unloads the loaded bundles one at a time. Unlike the sweep, the release here is the catalog's
    // UnloadAll (Unload(true)) unless -unloadFalse: the reproducer of the heap corruption that such a release causes
    // (-from 18300 -limit 514 -every 50 dies at row 18813 in under a minute; -unloadFalse does not).
    public static void RunItemRange()
    {
        int from = int.TryParse(Arg("-from"), out var f0) ? f0 : 0, limit = int.TryParse(Arg("-limit"), out var l0) ? l0 : 250;
        int every = int.TryParse(Arg("-every"), out var e0) && e0 > 0 ? e0 : 250;
        bool oneByOne = Flag("-oneByOne"), unloadFalse = Flag("-unloadFalse");
        string guid = Arg("-guid");
        if (Flag("-noTextureCheck")) TextureGuard.Enabled = false;
        dir = Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "modsweep", "itemrange");
        Directory.CreateDirectory(dir);
        Application.logMessageReceived += OnLog;
        try
        {
            Setup();
            var all = db.Items.All.Where(r => guid == null || string.Equals(r.Key.SourceGuid, guid, StringComparison.OrdinalIgnoreCase)).ToList();
            // -skipGuid / -onlyGuid (comma lists, case ignored) filter the window after it is cut, so row numbers stay the corpus's
            var skip = new HashSet<string>((Arg("-skipGuid") ?? "").Split(",;".ToCharArray()).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
            var keep = new HashSet<string>((Arg("-onlyGuid") ?? "").Split(",;".ToCharArray()).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
            var rows = all.Skip(from).Take(limit).ToList();
            int cut = rows.Count;
            rows = rows.Where(r => !skip.Contains(r.Key.SourceGuid) && (keep.Count == 0 || keep.Contains(r.Key.SourceGuid))).ToList();
            Debug.Log($"[WRange] {cut - rows.Count} rows of the window left out by -skipGuid / -onlyGuid");
            Debug.Log($"[WRange] {rows.Count} rows from {from} of {all.Count} (every {every}, oneByOne {oneByOne}, unloadFalse {unloadFalse}, texture check {TextureGuard.Enabled})");
            var index = new Dictionary<StudioItem, int>();
            for (int i = 0; i < all.Count; i++) index[all[i]] = i;
            int n = 0, at = from;
            foreach (var row in rows)
            {
                at = index[row];
                Debug.Log($"[WRange] row {at} {row.Key} {row.Group}/{row.Category} {row.BundlePath} {row.FileName}");
                BuildLikeCorpus(row);
                if (++n % every == 0) Release(oneByOne, unloadFalse, at);
            }
            Release(oneByOne, unloadFalse, at);
            Debug.Log("[WRange] DONE without a crash");
        }
        catch (Exception e) { Debug.LogError("[WRange] " + e); }
        finally { Application.logMessageReceived -= OnLog; }
    }

    static void Release(bool oneByOne, bool unloadFalse, int lastRow)
    {
        Debug.Log($"[WRange] release after row {lastRow}: scene dispose");
        scene?.Dispose();
        scene = null;
        if (oneByOne || unloadFalse)
            foreach (var b in AssetBundle.GetAllLoadedAssetBundles().ToList())
            {
                if (!b) continue;
                // -softBundle <part of a name>: that bundle alone is released with Unload(false) (its objects stay alive)
                string soft = Arg("-softBundle");
                bool destroy = !unloadFalse && !(soft != null && b.name.IndexOf(soft, StringComparison.OrdinalIgnoreCase) >= 0);
                Debug.Log($"[WRange] unload({destroy}) {b.name}");
                b.Unload(destroy);
            }
        Debug.Log("[WRange] catalog UnloadAll");
        gd.UnloadAll();
        Debug.Log("[WRange] UnloadUnusedAssets");
        Resources.UnloadUnusedAssets();
        GC.Collect();
        Debug.Log($"[WRange] released after row {lastRow}");
    }

    static void BuildLikeCorpus(StudioItem row)
    {
        try
        {
            if (db.TryGetItem(row.Key.SourceGuid, row.Group, row.Category, row.No, out var resolved) && resolved != null && resolved != row) return;
            var prefab = gd.LoadAsset<GameObject>(row);
            if (!prefab) return;
            if (Flag("-noVideo"))   // diagnosis switch: the prefab's video players neither play on awake nor run
                foreach (var v in prefab.GetComponentsInChildren<UnityEngine.Video.VideoPlayer>(true)) { v.playOnAwake = false; v.enabled = false; }
            if (scene == null) scene = new ItemTest.Scene(sv);
            var add = scene.Session.AddItem(ItemDefaults.FromPrefab(prefab, row));
            scene.Runtime.Apply();
            var id = add.Status == CommandStatus.Error ? null : add.NewId;
            if (id == null) return;
            var stt = scene.State(id);
            if (stt != null && stt.Options.Count > 0)
            {
                for (int i = 0; i < stt.Options.Count; i++) scene.Session.Set(id, "item.options[" + i + "]", false);
                scene.Runtime.Apply();
                for (int i = 0; i < stt.Options.Count; i++) scene.Session.Set(id, "item.options[" + i + "]", true);
            }
            scene.Session.Set(id, "item.fk.enabled", true);
            scene.Session.Set(id, "item.dynamicBones", false);
            scene.Runtime.Tick(ItemTest.Dt);
            scene.Session.Set(id, "item.fk.enabled", false);
            scene.Session.Set(id, "item.dynamicBones", true);
            scene.Runtime.Tick(ItemTest.Dt);
            scene.Runtime.Tick(ItemTest.Dt);
            scene.Session.Set(id, "visible", false);
            scene.Runtime.Apply();
            scene.Runtime.Ops.Delete(new[] { id });
            scene.Runtime.Apply();
        }
        catch (Exception e) { Debug.LogWarning("[WRange] row exception " + e.GetType().Name + ": " + e.Message); }
    }

    // ---- item: a Studio item row the corpus skipped

    static void Item(Row row, ZipmodInfo z, Result r, bool verbose)
    {
        var k = row.Key.Split('/');
        int g = int.Parse(k[0], CultureInfo.InvariantCulture), c = int.Parse(k[1], CultureInfo.InvariantCulture), no = int.Parse(k[2], CultureInfo.InvariantCulture);
        var it = itemsByZip.TryGetValue(z.Path, out var l) ? Nth(l.Where(x => x.Group == g && x.Category == c && x.No == no), row) ?? l.FirstOrDefault(x => x.No == no) : null;
        if (it == null) { NotInDb(r, z, row.Bundle); return; }
        if (db.TryGetItem(it.Key.SourceGuid, it.Group, it.Category, it.No, out var resolved) && resolved != null && resolved != it) { r.Flags.Add("shadowed"); return; }
        r.Keys.Add(StudioKey(z, it.BundlePath));
        if (verbose) Step(row, "load prefab");
        Timed(r, () => gd.LoadAsset<GameObject>(it), out var o);
        var prefab = o as GameObject;
        if (!prefab) { Diagnose(r, null, z, it.BundlePath, it.FileName, typeof(GameObject), "prefab"); return; }
        if (verbose) Step(row, "build");
        if (scene == null) scene = new ItemTest.Scene(sv);
        var add = scene.Session.AddItem(ItemDefaults.FromPrefab(prefab, it));
        scene.Runtime.Apply();
        var id = add.Status == CommandStatus.Error ? null : add.NewId;
        var real = scene.Item(id);
        if (real == null || real.State != RealizationState.Built) { r.Add("exception", "build: " + real?.State + " " + real?.Detail); return; }
        r.Flags.Add("buildMs=" + F(real.BuildMilliseconds));
        if (verbose) Step(row, "step");
        scene.Runtime.Tick(ItemTest.Dt);
        scene.Runtime.Tick(ItemTest.Dt);
        scene.Runtime.Ops.Delete(new[] { id });
        scene.Runtime.Apply();
    }
}
