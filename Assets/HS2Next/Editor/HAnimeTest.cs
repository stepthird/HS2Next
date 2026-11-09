// H animation rows listed and played (Phase 5.M package N; HA1-HA6, the corpus row, ZU5).
// Headless, edit mode, an AppCore test host (no HTTP).
// Content rule: H rows are played only on the clothed SFW targets of Tools/targets.json (the female and the male visual
// target); every check is a number or a name of data (row counts, controller and state names, layer weights, parameters,
// flags, bone values). Nothing is rendered or captured; only the text report is written.
// Run: -executeMethod HAnimeTest.Run [-only HA1,HA3] [-seed N]
//   HA1 the anime browser's rows = the listed keys (H included), the H groups, the facet order (groups / categories as HS2
//       orders them), every category's rows in its own order (plain categories too), the group column, search over H rows
//   HA2 24 H rows (a fixed seed over the H groups) on both targets: base controller, override bound, breast layer weight,
//       `motion`, `speed`, height / breast parameters, PV flags, layer-0 state = the row's clip at the time given, no failed
//       load; pose numbers recorded (info)
//   HA3 sway tables: per state line of rows with a table, the four enables, the per-side shape masks, the chains, the bust
//       bones (masked side moved, open side bit-exact); a plain row keeps the table (as HS2) but matches no line: everything
//       enabled and the bones back bit-exactly; an H row without a table and Stop clear it;
//       ZU5: the table step's cost (constant state, state change), a mask change's cost, no allocation per motion step
//   HA4 scene save / load with an H row: the document's anime ref, the row playing again at the same time, the same pose
//   HA5 an HS2 pose file whose anime record is an H row: loading it plays the row at the file's time
//   HA6 option items of H rows: loaded, parented under character bones, synced to the body's state and time, visibility
//   HA7 root bones: a clip's root-node orientation offset stays on cf_J_Hips (cf_J_Root keeps its rest, p_cf_anim does not
//       move), on the male rows that carry one and on plain rows / one H row per group (both targets)
// Run: -executeMethod HAnimeTest.RunCorpus [-limit N] [-release 200]
//   every listed H row without a character: the base and the override controller load, the override's clip names are
//   within the base's, the clip's state exists on layer 0 or on the breast layer, rows with a sway table find lines, option
//   items (and their controllers) load; bundles released every 200 rows (OpenBundleCount logged); time and memory (ZU5).
//   Failures of the installed list data are known classes with a maximum count (KnownCorpus); any other failure FAILs.
// Reports: Spike/out/motion/hanime_report.txt, hanime_corpus_report.txt (first line SUMMARY); EditorApplication.Exit(1) on
// a FAIL.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HS2Next.App;
using HS2Next.Chara;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Import.Pose;
using HS2Next.Motion;
using HS2Next.Motion.Animation;
using HS2Next.Motion.DynamicBones;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Panels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class HAnimeTest
{
    const float Dt = 1f / 60f;
    const int DefaultSeed = 5150;
    static StringBuilder rep;
    static int passes, fails, warns;
    static RuntimeServices services;
    static AppCore app;
    static StudioDb db;
    static string female, male;

    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "motion");
    static string WorkDir => Path.Combine(OutDir, "hanime");

    // ---------------------------------------------------------------- report

    static string Arg(string name, string def)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static int IntArg(string name, int def) => int.TryParse(Arg(name, null), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : def;

    static bool Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
        return ok;
    }

    static void Warn(string s) { rep.AppendLine("WARN  " + s); warns++; }
    static void Info(string s) => rep.AppendLine("   " + s);
    static void Section(string s) { rep.AppendLine(); rep.AppendLine("== " + s); }
    static string Row(StudioAnime r) => r == null ? "-" : $"{r.Group}/{r.Category}/{r.No}";
    static string F(float f) => f.ToString("0.######", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- host

    static JObject Call(object request)
    {
        var text = app.Call(request is string s ? s : JsonConvert.SerializeObject(request));
        return JObject.Parse(text);
    }

    static JObject Ok(object request, string what)
    {
        var r = Call(request);
        bool ok = r.Value<bool?>("ok") == true;
        if (!ok) Check(what, false, Short(r.ToString(Formatting.None)));
        return ok ? r : null;
    }

    static string Short(string s) => s == null ? "null" : s.Length > 400 ? s.Substring(0, 400) + "…" : s;

    static void Frames(int n) { for (int i = 0; i < n; i++) app.Frame(Dt); }

    static CharaRealization Real(string id) => app.Runtime.FindChara(id);
    static CharaAnimation Anim(string id) => Real(id)?.Animation;

    static AppCore NewHost(ulong seed)
    {
        var s = AppSettings.Defaults(Array.Empty<string>());
        s.Content = CardResolveTest.Settings;
        s.UserData = Path.Combine(WorkDir, "userdata");
        s.Bridge.OutputDir = Path.Combine(WorkDir, "out");
        var o = services.NewSessionOptions();
        o.Ids = new CountingIdSource(seed);
        return AppCore.ForTests(services, s, null, o);
    }

    static string LoadChara(string path, string sex)
    {
        var r = Ok(new { cmd = "chara.load", path, sex }, "chara.load " + Path.GetFileName(path));
        var a = r?["added"]?[0] as JObject;
        if (a == null || (string)a["state"] != "Built") { Check("chara.load " + Path.GetFileName(path) + ": built", false, Short(r?.ToString(Formatting.None))); return null; }
        return (string)a["key"];
    }

    static bool PlayRow(string id, StudioAnime row, float time, float? speed = null)
    {
        object req = speed != null
            ? (object)new { cmd = "chara.anime", key = id, guid = row.Guid, group = row.Group, category = row.Category, no = row.No, time, speed = speed.Value }
            : new { cmd = "chara.anime", key = id, guid = row.Guid, group = row.Group, category = row.Category, no = row.No, time };
        return Ok(req, "chara.anime " + Row(row)) != null;
    }

    static List<StudioAnime> ListedH() => db.Anime.Enumerate().Where(r => r.IsH).ToList();

    // ---------------------------------------------------------------- run

    public static void Run()
    {
        rep = new StringBuilder(); passes = fails = warns = 0;
        Directory.CreateDirectory(WorkDir);
        var sw = Stopwatch.StartNew();
        var head = new StringBuilder();
        var onlyArg = Arg("-only", null);
        var only = onlyArg != null ? new HashSet<string>(onlyArg.Split(','), StringComparer.OrdinalIgnoreCase) : null;
        int seed = IntArg("-seed", DefaultSeed);
        try
        {
            services = SceneRuntimeTest.NewServices(head);
            db = services.Db;
            rep.AppendLine($"H animation test (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}); seed {seed}");
            rep.Append(head);
            var t = TestTargets.Load();
            app = NewHost(0x48a71e5eedUL);
            female = LoadChara(TestTargets.Abs(t.visual.female), "female");
            male = LoadChara(TestTargets.Abs(t.visual.male), "male");
            Info($"targets: female {Path.GetFileName(t.visual.female)} = {female}, male {Path.GetFileName(t.visual.male)} = {male}");
            var checks = new (string id, Action run)[]
            {
                ("HA1", HA1), ("HA2", () => HA2(seed)), ("HA3", HA3), ("HA4", HA4), ("HA5", HA5), ("HA6", HA6), ("HA7", HA7),
            };
            foreach (var (id, run) in checks)
            {
                if (only != null && !only.Contains(id)) continue;
                if (female == null || male == null) { Check(id + ": targets built", false); continue; }
                var t0 = sw.Elapsed.TotalSeconds;
                try { run(); }
                catch (Exception e) { Check(id + " (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
                Info($"{id}: {sw.Elapsed.TotalSeconds - t0:F1} s");
            }
        }
        catch (Exception e) { Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        finally
        {
            try { app?.Dispose(); } catch (Exception e) { Info("host dispose threw: " + e.Message); }
            app = null;
        }
        rep.Insert(0, $"SUMMARY: {passes} PASS, {fails} FAIL ({warns} WARN, {sw.Elapsed.TotalSeconds:F0} s)\n");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "hanime_report.txt"), rep.ToString(), new UTF8Encoding(false));
        services?.Catalog?.UnloadAll();
        Debug.Log($"HANIME TEST DONE: {passes} PASS, {fails} FAIL, {warns} WARN");
        if (fails > 0) TestExit.Exit(1);
    }

    // ---------------------------------------------------------------- HA1 browser

    static void HA1()
    {
        Section("HA1 the anime browser lists every listed row, H included");
        var all = db.Anime.All;
        int vanillaKeys = all.Count(r => r.IsVanilla), vanillaH = all.Count(r => r.IsVanilla && r.IsH);
        var listed = db.Anime.Enumerate().ToList();
        int listedH = listed.Count(r => r.IsH), listedVanillaH = listed.Count(r => r.IsH && r.IsVanilla);
        var hGroups = listed.Where(r => r.IsH && r.IsVanilla).Select(r => r.Group).Distinct().ToList();
        var hCats = listed.Where(r => r.IsH && r.IsVanilla).Select(r => (r.Group, r.Category)).Distinct().Count();
        int mixed = listed.GroupBy(r => r.Group).Count(g => g.Any(r => r.IsH) && g.Any(r => !r.IsH));
        Info($"vanilla keys {vanillaKeys} ({vanillaH} H, {vanillaKeys - vanillaH} plain); listed rows {listed.Count} (H {listedH}, vanilla H {listedVanillaH}, mod {listed.Count(r => !r.IsVanilla)}); vanilla H groups {hGroups.Count}, categories {hCats}");
        Check("vanilla keys 7,120 = 6,145 H + 975 plain", vanillaKeys == 7120 && vanillaH == 6145, $"{vanillaKeys} = {vanillaH} + {vanillaKeys - vanillaH}");
        Check("every vanilla H key is listed (6,145)", listedVanillaH == 6145, listedVanillaH.ToString());
        Check("26 vanilla H groups, 359 categories", hGroups.Count == 26 && hCats == 359, $"{hGroups.Count} / {hCats}");
        Check("no group holds both H and plain rows", mixed == 0, mixed.ToString());

        var ctx = (UiContext)Activator.CreateInstance(typeof(UiContext), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { app }, null);
        var spec = ListBrowsers.Anime(ctx);
        var rows = spec.Rows();
        Check("browser rows = the listed rows (same rows, same order; no sex or H filter)", rows.Count == listed.Count && rows.SequenceEqual(listed), $"{rows.Count} / {listed.Count}");
        Check("browser rows hold every listed H row", rows.Count(r => r.IsH) == listedH, rows.Count(r => r.IsH).ToString());
        var gcol = spec.Columns.FirstOrDefault(c => c.id == "group");
        var sampleH = rows.FirstOrDefault(r => r.IsH);
        string groupName = sampleH != null && db.TryGetAnimeGroup(sampleH.Group, out var sg) ? sg.Name : null;
        Check("a group column showing the row's group name", gcol.text != null && sampleH != null && gcol.text(sampleH) == groupName,
            gcol.text != null ? $"columns {string.Join(", ", spec.Columns.Select(c => c.id))}" : "no group column");

        // facets: the browser's rule (GroupOrder then key; CategoryOrder then key) against HS2's (sort, then position; non-empty)
        var bGroups = rows.Select(spec.GroupKey).Distinct().OrderBy(k => spec.GroupOrder(k)).ThenBy(k => k, StringComparer.Ordinal).ToList();
        var hs2Groups = db.AnimeGroups().Where(g => db.CategoriesOf(g).Any(c => db.AnimeIn(c).Any()))
            .Select(g => g.Number.ToString(CultureInfo.InvariantCulture)).ToList();
        Check("group facets in HS2's order (sort, then position; non-empty groups only)", bGroups.SequenceEqual(hs2Groups), $"{bGroups.Count} groups / HS2 {hs2Groups.Count}");
        // categories per group in HS2's order; per category the browser's rows = the category's rows (as a set; in order for H
        // categories: the browser keeps the list's display order, which is the category's own order there)
        int badCats = 0, badSets = 0, badHOrder = 0, plainOrder = 0, hCatsSeen = 0;
        var notes = new List<string>();
        foreach (var g in db.AnimeGroups())
        {
            string gk = g.Number.ToString(CultureInfo.InvariantCulture);
            var inG = rows.Where(r => spec.GroupKey(r) == gk).ToList();
            var bCats = inG.Select(spec.CategoryKey).Distinct().OrderBy(k => spec.CategoryOrder(gk, k)).ThenBy(k => k, StringComparer.Ordinal).ToList();
            var cats = db.CategoriesOf(g).Where(c => db.AnimeIn(c).Any()).ToList();
            if (!bCats.SequenceEqual(cats.Select(c => c.Number.ToString(CultureInfo.InvariantCulture)))) { badCats++; if (notes.Count < 6) notes.Add("group " + gk + " categories"); }
            foreach (var c in cats)
            {
                string ck = c.Number.ToString(CultureInfo.InvariantCulture);
                var bRows = inG.Where(r => spec.CategoryKey(r) == ck).ToList();
                var cRows = db.AnimeIn(c).ToList();
                bool isH = cRows.Any(r => r.IsH);
                if (isH) hCatsSeen++;
                if (bRows.Count != cRows.Count || !new HashSet<StudioAnime>(bRows).SetEquals(cRows))
                { badSets++; if (notes.Count < 6) notes.Add($"category {gk}/{ck}: browser {bRows.Count} rows, category {cRows.Count}"); }
                else if (!bRows.SequenceEqual(cRows))
                {
                    int at = 0;
                    while (at < bRows.Count && bRows[at] == cRows[at]) at++;
                    string where = $"category {gk}/{ck} ({bRows.Count} rows): first difference at {at}: browser {Row(bRows[at])} {(bRows[at].IsVanilla ? "vanilla" : "mod")}, category {Row(cRows[at])} {(cRows[at].IsVanilla ? "vanilla" : "mod")}";
                    if (isH) { badHOrder++; if (notes.Count < 6) notes.Add(where); }
                    else { plainOrder++; if (notes.Count < 6) notes.Add("plain " + where); }
                }
            }
        }
        // 5.2: plain categories too (a mod row that repeats a base-game key keeps the key's first place, as HS2 lists 19/0 and 19/5)
        Check($"category facets in HS2's order per group; every category's rows in the browser in the category's own order (H {hCatsSeen}, plain too)",
            badCats == 0 && badSets == 0 && badHOrder == 0 && plainOrder == 0, $"groups off {badCats}, row sets off {badSets}, H orders off {badHOrder}, plain orders off {plainOrder}{(notes.Count > 0 ? ": " + string.Join(" | ", notes) : "")}");
        int hFacetGroups = bGroups.Count(k => rows.Any(r => r.IsH && spec.GroupKey(r) == k));
        int hListedGroups = rows.Where(r => r.IsH).Select(r => r.Group).Distinct().Count();
        Check("the group facet lists every H group (26 vanilla)", hFacetGroups == hListedGroups && hFacetGroups >= 26, hFacetGroups.ToString());
        if (sampleH != null)
        {
            var hits = rows.Where(r => spec.SearchText(r).IndexOf(sampleH.Name ?? "", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            Check("the search text of H rows holds their name, category and group (search covers H rows)", hits.Contains(sampleH) && spec.SearchText(sampleH).Contains(groupName ?? "\u0001"),
                $"{hits.Count} rows match the sample row's name");
        }
        // the real browser element over the spec (index rows and the group facet's rows)
        try
        {
            var b = new ListBrowser<StudioAnime>(ctx, spec);
            // v1.2: the views «★» and «最近追加» stand above «All»
            int facetRows = b.Groups?.itemsSource != null ? ((System.Collections.Generic.IList<string>)b.Groups.itemsSource).Count(k => !BrowserViews.Is(k)) : -1;
            Check("the browser element indexes every row; its group facet = \"All\" + the groups", b.Index.Rows.Count == rows.Count && facetRows == bGroups.Count + 1,
                $"index {b.Index.Rows.Count}, facet rows {facetRows}");
        }
        catch (Exception e) { Warn("the browser element could not be built headless (" + e.GetType().Name + ": " + e.Message + "); UiPanelsTest.play covers it"); }
    }

    // ---------------------------------------------------------------- HA2 24 H rows on both targets

    static void HA2(int seed)
    {
        Section("HA2 24 H rows on both targets (numbers only)");
        var hrows = ListedH();
        var groups = hrows.Select(r => r.Group).Distinct().OrderBy(g => g).ToList();
        var rng = new System.Random(seed);
        var order = groups.Select(g => (g, k: rng.Next())).OrderBy(x => x.k).Select(x => x.g).ToList();
        var pick = new List<StudioAnime>();
        for (int i = 0; pick.Count < 24 && order.Count > 0; i++)
        {
            var inG = hrows.Where(r => r.Group == order[i % order.Count]).ToList();
            pick.Add(inG[rng.Next(inG.Count)]);
        }
        Info("rows: " + string.Join(", ", pick.Select(Row)));
        foreach (var id in new[] { female, male }) app.Ops.SetAnimePattern(new[] { id }, 0.25f);
        Frames(1);
        int loads = 0, ok = 0;
        var bad = new List<string>();
        foreach (var row in pick)
            foreach (var (sex, id) in new[] { ("f", female), ("m", male) })
            {
                loads++;
                var a = Anim(id);
                int logBefore = a ? a.Log.Count : 0;
                if (!PlayRow(id, row, 0.3f, 0f)) { bad.Add($"{Row(row)} {sex}: chara.anime failed"); continue; }
                Frames(2);
                a = Anim(id);
                var why = RowState(a, row, 0.3f, 0.25f, logBefore);
                if (why.Count == 0) ok++;
                else bad.Add($"{Row(row)} {sex}: {string.Join("; ", why)}");
                Info($"{Row(row)} {sex}: layers {a.Driver?.LayerCount}, extra layer {a.ExtraLayer}, override {(a.Driver?.Controller is AnimatorOverrideController ? a.Driver.Controller.name : "-")}, " +
                     $"motion {(a.IsAnimeMotion ? "yes" : "no")}, sway {(a.Sway.Loaded ? a.Sway.Lines.Count + " lines" : "-")}, items {a.Items.Count}; pose {PoseNumbers(a)}");
            }
        Check($"every H row plays as HS2 plays it on both targets ({ok} / {loads}): base controller, override, breast layer, motion, speed, height / breast, PV flags, layer-0 state and time, no failed load",
            ok == loads && loads == 48, Brief(bad));
        foreach (var id in new[] { female, male }) app.Ops.SetAnimePattern(new[] { id }, 0f);
    }

    static string Brief(List<string> l) => l.Count == 0 ? "" : string.Join(" | ", l.Take(8)) + (l.Count > 8 ? $" (+{l.Count - 8})" : "");

    // The state an H row must leave on a character (empty = as expected).
    static List<string> RowState(CharaAnimation a, StudioAnime row, float time, float pattern, int logBefore)
    {
        var why = new List<string>();
        if (!a) { why.Add("not built"); return why; }
        var d = a.Driver;
        if (!a.IsLoaded || d == null) { why.Add("no motion loaded"); return why; }
        if (!a.IsHPlaying) why.Add("not H");
        if (a.Playing != row) why.Add("playing " + Row(a.Playing));
        if (!a.BaseController || !string.Equals(a.BaseController.name, row.FileName, StringComparison.OrdinalIgnoreCase))
            why.Add($"base {(a.BaseController ? a.BaseController.name : "-")} != {row.FileName}");
        bool hasOverride = !string.IsNullOrEmpty(row.OverrideBundle) && !string.IsNullOrEmpty(row.OverrideFile);
        if (hasOverride && !(d.Controller is AnimatorOverrideController aoc && string.Equals(aoc.name, row.OverrideFile, StringComparison.OrdinalIgnoreCase)))
            why.Add($"override {d.Controller.name} (expected {row.OverrideFile})");
        if (!hasOverride && d.Controller is AnimatorOverrideController) why.Add("a stale override stays bound");
        int clip = Animator.StringToHash(row.Clip ?? "");
        var st = d.State(0);
        if (st.shortNameHash != clip) why.Add("layer 0 is not the row's clip");
        if (Mathf.Abs(st.normalizedTime - time) > 1e-3f) why.Add($"time {F(st.normalizedTime)}");
        int bl = row.BreastLayer;
        for (int i = 1; i < d.LayerCount; i++)
        {
            float w = d.GetLayerWeight(i);
            if (i == bl) { if (Mathf.Abs(w - 1f) > 1e-6f) why.Add($"breast layer {i} weight {F(w)}"); if (d.State(i).shortNameHash != clip) why.Add($"layer {i} is not the row's clip"); }
            else if (w != 0f) why.Add($"layer {i} weight {F(w)}");
        }
        if (row.IsMotion && d.HasParameter("motion") && Mathf.Abs(d.GetFloat(Animator.StringToHash("motion")) - pattern) > 1e-6f) why.Add("motion " + F(d.GetFloat(Animator.StringToHash("motion"))));
        if (d.HasParameter("speed") && d.GetFloat(Animator.StringToHash("speed")) != 1f) why.Add("speed parameter " + F(d.GetFloat(Animator.StringToHash("speed"))));
        if (a.HeightParameter.Length > 0 && d.GetFloat(Animator.StringToHash(a.HeightParameter)) != a.OptionParam1) why.Add("height parameter is not option parameter 1");
        if (a.BreastParameter.Length > 0 && d.GetFloat(Animator.StringToHash(a.BreastParameter)) != a.OptionParam2) why.Add("breast parameter is not option parameter 2");
        for (int i = 0; i < 8; i++) if (a.PvFlags[i] != row.PvFlags[i]) { why.Add("PV flags"); break; }
        for (int i = logBefore; i < a.Log.Count; i++)
            if (a.Log[i].IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 || a.Log[i].IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0
                || a.Log[i].IndexOf("not on layer 0", StringComparison.OrdinalIgnoreCase) >= 0) { why.Add("log: " + a.Log[i]); break; }
        return why;
    }

    // Hips position and a checksum of the body bones' local rotations (numbers only).
    static string PoseNumbers(CharaAnimation a)
    {
        var bones = a.Context?.BodyBones;
        if (bones == null) return "-";
        double sum = 0;
        foreach (var kv in bones.OrderBy(k => k.Key, StringComparer.Ordinal))
            if (kv.Value) { var q = kv.Value.localRotation; sum += Math.Abs(q.x) + Math.Abs(q.y) * 2 + Math.Abs(q.z) * 3 + Math.Abs(q.w) * 4; }
        var hips = bones.TryGetValue("cf_J_Hips", out var h) && h ? h.localPosition : Vector3.zero;
        return $"hips ({F(hips.x)}, {F(hips.y)}, {F(hips.z)}), rotation sum {sum:F4}";
    }

    // ---------------------------------------------------------------- HA3 sway tables

    struct Trs { public Vector3 p, s; public Quaternion r; }

    static Trs[] Snap(IReadOnlyList<Transform> ts)
    {
        var a = new Trs[ts.Count];
        for (int i = 0; i < ts.Count; i++) if (ts[i]) a[i] = new Trs { p = ts[i].localPosition, r = ts[i].localRotation, s = ts[i].localScale };
        return a;
    }

    // Bit-exact comparison (Unity's operators use a tolerance).
    static int DifferBits(Trs[] a, Trs[] b)
    {
        int n = 0;
        for (int i = 0; i < a.Length; i++)
        {
            bool same = Bits(a[i].p, b[i].p) && Bits(a[i].s, b[i].s) && a[i].r.x.Equals(b[i].r.x) && a[i].r.y.Equals(b[i].r.y) && a[i].r.z.Equals(b[i].r.z) && a[i].r.w.Equals(b[i].r.w);
            if (!same) n++;
        }
        return n;
    }

    static bool Bits(Vector3 a, Vector3 b) => a.x.Equals(b.x) && a.y.Equals(b.y) && a.z.Equals(b.z);

    // Rows with a sway table whose lines cover the cases: a breast sway flag off, a hip flag off, masks on, masks differing
    // per side.
    static List<StudioAnime> SwayRows(out Dictionary<StudioAnime, HSwayTable> tables)
    {
        tables = new Dictionary<StudioAnime, HSwayTable>();
        var seen = new HashSet<string>();
        bool bust = false, hip = false, mask = false, sides = false;
        var pick = new List<StudioAnime>();
        foreach (var r in ListedH().Where(r => r.IsVanilla && !string.IsNullOrEmpty(r.YureFile) && r.Options.Count == 0))
        {
            if (!seen.Add(r.YureBundle + "|" + r.YureFile + "|" + r.MotionId + "|" + r.YureWho)) continue;
            var t = new HSwayTable();
            if (!t.Load(services.Catalog, r, out _) || t.Lines.Count == 0) continue;
            bool b = t.Lines.Any(l => !l.Left[0] || !l.Right[0]), h = t.Lines.Any(l => !l.HipL || !l.HipR);
            bool m = t.Lines.Any(l => l.MaskL != 0 || l.MaskR != 0), s = t.Lines.Any(l => l.MaskL != l.MaskR);
            if ((b && !bust) || (h && !hip) || (m && !mask) || (s && !sides))
            {
                pick.Add(r); tables[r] = t;
                bust |= b; hip |= h; mask |= m; sides |= s;
            }
            if (bust && hip && mask && sides && pick.Count >= 2) break;
        }
        return pick;
    }

    static void HA3()
    {
        Section("HA3 sway tables (numbers only)");
        var rows = SwayRows(out var tables);
        Info("rows: " + string.Join(", ", rows.Select(r => $"{Row(r)} ({r.YureFile}, motion {r.MotionId}, {tables[r].Lines.Count} lines)")));
        bool cBust = rows.Any(r => tables[r].Lines.Any(l => !l.Left[0] || !l.Right[0])), cHip = rows.Any(r => tables[r].Lines.Any(l => !l.HipL || !l.HipR));
        bool cMask = rows.Any(r => tables[r].Lines.Any(l => l.MaskL != 0 || l.MaskR != 0)), cSides = rows.Any(r => tables[r].Lines.Any(l => l.MaskL != l.MaskR));
        Check("rows with a sway table cover a breast flag off, a hip flag off, masks on and masks differing per side", cBust && cHip && cMask && cSides,
            $"{rows.Count} rows; breast {cBust}, hip {cHip}, masks {cMask}, per side {cSides}");
        var plain = CharaAnimation.Resolve(db, ContentIds.Vanilla, 19, 0, 0);
        var real = Real(female);
        var shape = real?.Animation?.Context?.BodyShape;
        if (shape == null || !shape.HasBustMasks) { Check("the female target has a body shape engine with bust masks", false); return; }
        var bonesL = shape.BustMaskBones(0); var bonesR = shape.BustMaskBones(1);
        Info($"bust bones per side: left {bonesL.Count} ({string.Join(", ", bonesL.Select(b => b.name))}), right {bonesR.Count}");
        Check("each side has its bust bones (the rows of the ids 2-8, 32 on that side)", bonesL.Count > 0 && bonesL.Count == bonesR.Count && bonesL.All(b => b.name.EndsWith("_L")) && bonesR.All(b => b.name.EndsWith("_R")),
            $"{bonesL.Count} / {bonesR.Count}");
        // the card's values of the masked ids (an id at 0.5 does not move the bones when masked)
        var card = real.Animation.Context.Chara?.Body?.Shape;
        PlayRow(female, plain, 0f, 0f);
        Frames(1);
        var a = Anim(female);
        var buildL = Snap(bonesL); var buildR = Snap(bonesR);
        var nipples = new List<Transform>();
        foreach (var pre in ShapeEngine.NippleRowBones)
            foreach (var sfx in new[] { "L", "R" })
                if (real.Animation.Context.BodyBones.TryGetValue(pre + sfx, out var nb) && nb) nipples.Add(nb);
        var nipBuild = Snap(nipples);
        int nipChecks = 0, okNip = 0;
        int states = 0, okStates = 0, boneChecks = 0, okBones = 0, chains = 0, okChains = 0;
        var bad = new List<string>();
        foreach (var row in rows)
        {
            if (!PlayRow(female, row, 0f, 0f)) continue;
            Frames(1);
            a = Anim(female);
            var y = a.Sway;
            Check($"{Row(row)}: the table loaded with the row ({y.Lines.Count} lines, file {y.File}, motion {y.MotionId})", y.Loaded && y.Lines.Count == tables[row].Lines.Count && y.File == row.YureFile);
            var bust = a.BustStage;
            var seenStates = new HashSet<int>();
            for (int li = 0; li < y.Lines.Count; li++)
            {
                var l = y.Lines[li];
                if (!seenStates.Add(l.StateHash) || !a.Driver.HasState(0, l.StateHash)) continue;
                states++;
                a.Play(l.State, 0f);   // re-samples: the sway step applies the line
                var why = new List<string>();
                if (y.Matched != l || y.MatchedIndex != li) why.Add("matched " + y.MatchedIndex);
                if (bust == null || bust.AnimeBustL != l.Left[0] || bust.AnimeBustR != l.Right[0] || bust.HipL != l.HipL || bust.HipR != l.HipR) why.Add("enables");
                if (shape.BustMask(0) != l.MaskL || shape.BustMask(1) != l.MaskR) why.Add($"masks {shape.BustMask(0):X2}/{shape.BustMask(1):X2} (line {l.MaskL:X2}/{l.MaskR:X2})");
                if (why.Count == 0) okStates++; else bad.Add($"{Row(row)} {l.State}: {string.Join(", ", why)}");
                // the bust bones: a side without a mask keeps the build values bit-exactly; a masked side moves when a masked id's
                // card value is not 0.5
                foreach (var (side, bones, build, m) in new[] { (0, bonesL, buildL, l.MaskL), (1, bonesR, buildR, l.MaskR) })
                {
                    boneChecks++;
                    int diff = DifferBits(Snap(bones), build);
                    bool moves = false;   // a bust id (2-8) masked whose card value is not 0.5; the nipple flag reaches no bust row
                    for (int k = 0; k < ShapeEngine.BustMaskIds.Length - 1; k++)
                        if ((m & (1 << k)) != 0 && card != null && ShapeEngine.BustMaskIds[k] < card.Length && card[ShapeEngine.BustMaskIds[k]] != 0.5f) moves = true;
                    bool good = m == 0 ? diff == 0 : (moves ? diff > 0 : diff == 0);
                    if (good) okBones++; else bad.Add($"{Row(row)} {l.State} side {side}: mask {m:X2}, {diff} bones differ from the build (moves expected {moves})");
                }
                nipChecks++;
                int nd = DifferBits(Snap(nipples), nipBuild);
                if (nd == 0) okNip++; else bad.Add($"{Row(row)} {l.State}: {nd} nipple bones differ from the build");
                Frames(1);   // the bust stage sets the chains
                if (bust != null)
                    for (int k = 0; k < 4; k++)
                    {
                        var ch = bust.Chains[k];
                        if (ch == null) continue;
                        chains++;
                        if (ch.Active == bust.WantActive(ch)) okChains++; else bad.Add($"{Row(row)} {l.State}: chain {k} active {ch.Active}");
                    }
            }
        }
        Check($"per state line: the matched line, the four enables and the per-side masks ({okStates} / {states} states)", states > 0 && okStates == states, Brief(bad));
        Check($"bust bones: an open side bit-exact to the build, a masked side moved when a masked id's card value is not 0.5 ({okBones} / {boneChecks})", boneChecks > 0 && okBones == boneChecks);
        Check($"the chains follow the enables ({okChains} / {chains})", chains > 0 && okChains == chains);
        Check($"the nipple's own bones ({nipples.Count}) keep their build values under every mask, as in HS2 ({okNip} / {nipChecks})", nipples.Count == 4 && nipChecks > 0 && okNip == nipChecks);
        // a full shape update while masked (an edit or the nipple state): the nipple rows keep the slider values, the masked
        // side's bust rows come back masked
        var mline = rows.SelectMany(r => tables[r].Lines.Select(l => (r, l))).FirstOrDefault(x => x.l.MaskL != 0 && x.l.MaskR != 0);
        if (mline.l != null)
        {
            PlayRow(female, mline.r, 0f, 0f);
            a = Anim(female);
            if (a.Driver.HasState(0, mline.l.StateHash)) a.Play(mline.l.State, 0f);
            var maskedL = Snap(bonesL);
            shape.Apply();
            var mods = a.Context.Bones;
            if (mods) mods.ApplyNow();   // the bone modifiers on top again, as an edit does
            Check("a full shape update while masked: the nipple bones keep the build values, the masked bust bones stay masked",
                  shape.BustMask(0) == mline.l.MaskL && DifferBits(Snap(nipples), nipBuild) == 0 && DifferBits(Snap(bonesL), maskedL) == 0,
                  $"nipple bones differing {DifferBits(Snap(nipples), nipBuild)}, bust bones differing {DifferBits(Snap(bonesL), maskedL)}");
        }

        // ZU5: cost of the sway step and of a mask change; no allocation per motion step with a table loaded
        var row0 = rows.FirstOrDefault(r => tables[r].Lines.Any(l => l.MaskL != 0 || l.MaskR != 0)) ?? rows.FirstOrDefault();
        if (row0 != null)
        {
            PlayRow(female, row0, 0f, 1f);
            Frames(2);
            a = Anim(female);
            var frame = new MotionFrame(Dt, Dt, 0, 0);
            for (int i = 0; i < 30; i++) a.Stage.Step(frame);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 300; i++) a.Stage.Step(frame);
            long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
            Check("no allocation per motion step with a sway table loaded (300 steps of the animation stage)", alloc == 0, alloc + " bytes");
            var t = tables[row0];
            int h0 = t.Lines[0].StateHash, h1 = t.Lines.FirstOrDefault(l => l.StateHash != h0)?.StateHash ?? 12345;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 100000; i++) t.Step(h0);
            double constNs = sw.Elapsed.TotalMilliseconds * 1e6 / 100000;
            sw.Restart();
            for (int i = 0; i < 100000; i++) t.Step((i & 1) == 0 ? h0 : h1);
            double changeNs = sw.Elapsed.TotalMilliseconds * 1e6 / 100000;
            var ml = t.Lines.FirstOrDefault(l => l.MaskL != 0 || l.MaskR != 0);
            double maskUs = double.NaN;
            if (ml != null)
            {
                sw.Restart();
                for (int i = 0; i < 100; i++) { a.ApplyShapeMasks(ml.MaskL, ml.MaskR); a.ApplyShapeMasks(0, 0); }
                maskUs = sw.Elapsed.TotalMilliseconds * 1000 / 200;
            }
            Info($"ZU5 sway table ({t.Lines.Count} lines): step at a constant state {constNs:F1} ns, step with a state change {changeNs:F1} ns, a mask change applied (side rows, bone modifiers, guard) {maskUs:F1} µs");
            Check("ZU5: the table step costs under 1 µs at a constant state and under 20 µs with a state change; a mask change under 1 ms", constNs < 1000 && changeNs < 20000 && !(maskUs >= 1000),
                $"{constNs:F1} / {changeNs:F1} ns, {maskUs:F1} µs");
        }

        // a plain row keeps the table (as HS2) but matches no line: everything enabled, the bust bones back bit-exactly
        int linesBefore = Anim(female).Sway.Lines.Count;
        PlayRow(female, plain, 0f, 0f);
        Frames(1);
        a = Anim(female);
        var bs = a.BustStage;
        Check("a plain row keeps the table loaded (as HS2) and matches no line: everything enabled, no mask",
              a.Sway.Loaded && a.Sway.Lines.Count == linesBefore && a.Sway.MatchedIndex == -1 && bs != null && bs.AnimeBustL && bs.AnimeBustR && bs.HipL && bs.HipR &&
              shape.BustMask(0) == 0 && shape.BustMask(1) == 0, a.Sway.ToString());
        int dl = DifferBits(Snap(bonesL), buildL), dr = DifferBits(Snap(bonesR), buildR);
        Check("after the plain row the bust bones equal the build values bit-exactly", dl == 0 && dr == 0, $"{dl} / {dr} bones differ");
        // an H row without a table clears it
        var noTable = ListedH().FirstOrDefault(r => r.IsVanilla && string.IsNullOrEmpty(r.YureFile) && r.Options.Count == 0);
        if (noTable != null)
        {
            PlayRow(female, noTable, 0f, 0f);
            Frames(1);
            a = Anim(female);
            Check($"an H row without a table ({Row(noTable)}) clears it: everything enabled", !a.Sway.Loaded && a.Sway.MatchedIndex == -1 && bs.AnimeBustL && bs.AnimeBustR && bs.HipL && bs.HipR &&
                  shape.BustMask(0) == 0 && shape.BustMask(1) == 0, a.Sway.ToString());
            PlayRow(female, plain, 0f, 0f);
        }
        // Stop clears it too
        var mrow = rows.FirstOrDefault(r => tables[r].Lines.Any(l => l.MaskL != 0));
        if (mrow != null)
        {
            PlayRow(female, mrow, 0f, 0f);
            var ln = tables[mrow].Lines.First(l => l.MaskL != 0);
            a = Anim(female);
            if (a.Driver.HasState(0, ln.StateHash)) a.Play(ln.State, 0f);
            bool masked = shape.BustMask(0) != 0;
            a.Stop();
            Check("Stop clears the table and opens the masks (bones back to the build values)", masked && !a.Sway.Loaded && shape.BustMask(0) == 0 && shape.BustMask(1) == 0 && DifferBits(Snap(bonesL), buildL) == 0,
                $"masked before {masked}");
            PlayRow(female, plain, 0f, 0f);
        }
    }

    // ---------------------------------------------------------------- HA4 scene save / load

    static readonly string[] MainBones =
    {
        "cf_J_Hips", "cf_J_Spine01", "cf_J_Spine02", "cf_J_Spine03", "cf_J_Neck", "cf_J_Head", "cf_J_Shoulder_L", "cf_J_Shoulder_R",
        "cf_J_ArmUp00_L", "cf_J_ArmUp00_R", "cf_J_ArmLow01_L", "cf_J_ArmLow01_R", "cf_J_Hand_L", "cf_J_Hand_R", "cf_J_Kosi01",
        "cf_J_LegUp00_L", "cf_J_LegUp00_R", "cf_J_LegLow01_L", "cf_J_LegLow01_R", "cf_J_Foot01_L", "cf_J_Foot01_R",
    };

    static Dictionary<string, (Vector3 p, Quaternion r)> Pose(string id)
    {
        var d = new Dictionary<string, (Vector3, Quaternion)>();
        var bones = Real(id)?.Animation?.Context?.BodyBones;
        if (bones == null) return d;
        foreach (var n in MainBones) if (bones.TryGetValue(n, out var t) && t) d[n] = (t.localPosition, t.localRotation);
        return d;
    }

    static (float pos, double deg) PoseDiff(Dictionary<string, (Vector3 p, Quaternion r)> a, Dictionary<string, (Vector3 p, Quaternion r)> b)
    {
        float dp = a.Count == b.Count && a.Count > 0 ? 0f : float.PositiveInfinity; double dr = dp;
        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out var o)) return (float.PositiveInfinity, float.PositiveInfinity);
            dp = Mathf.Max(dp, (kv.Value.p - o.p).magnitude);
            dr = Math.Max(dr, RotationCompare.Deg(kv.Value.r, o.r));
        }
        return (dp, dr);
    }

    static StudioAnime SwayRow() => SwayRows(out _).FirstOrDefault() ?? ListedH().First(r => r.IsVanilla);

    static void HA4()
    {
        Section("HA4 scene save / load with an H row");
        var row = SwayRow();
        PlayRow(female, row, 0.4f, 0f);
        Frames(3);
        var before = Pose(female);
        var a0 = Anim(female);
        int lines0 = a0.Sway.Lines.Count, mask0 = a0.Context.BodyShape.BustMask(0), mask1 = a0.Context.BodyShape.BustMask(1);
        Ok(new { cmd = "scene.save", name = "hanime_ha4", overwrite = true, thumb = "placeholder", clock = "2026-10-28T00:00:00Z" }, "scene.save hanime_ha4");
        Ok(new { cmd = "scene.load", name = "hanime_ha4" }, "scene.load hanime_ha4");
        Frames(3);
        var sc = Ok(new { cmd = "scene" }, "scene");
        var objs = (sc?["objects"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        var fo = objs.FirstOrDefault(o => (string)o["key"] == female);
        if (fo == null) { Check("the female node is back with its id", false, objs.Count + " objects"); return; }
        var an = fo["anime"];
        Check("the document's anime ref is the H row", (int?)an?["group"] == row.Group && (int?)an?["category"] == row.Category && (int?)an?["no"] == row.No && (string)an?["guid"] == row.Guid,
            Short(an?.ToString(Formatting.None)));
        var a = Anim(female);
        Check("the H row plays again at the saved time (speed 0), the sway table and masks as before",
            a && a.IsHPlaying && a.Playing == row && Mathf.Abs(a.NormalizedTime - 0.4f) < 1e-3f && a.Speed == 0f && a.Sway.Lines.Count == lines0 &&
            a.Context.BodyShape.BustMask(0) == mask0 && a.Context.BodyShape.BustMask(1) == mask1,
            a ? $"time {F(a.NormalizedTime)}, speed {F(a.Speed)}, lines {a.Sway.Lines.Count} / {lines0}" : "not built");
        var (dp, dr) = PoseDiff(before, Pose(female));
        Check($"the same pose ({MainBones.Length} main bones: position <= 1e-5, angle <= 0.01°)", dp <= 1e-5f && dr <= 0.01f, $"{dp:E2} / {dr:F5}°");
        // the male node is back too (used by the next checks)
        if (!objs.Any(o => (string)o["key"] == male)) Check("the male node is back with its id", false);
    }

    // ---------------------------------------------------------------- HA5 HS2 pose file with an H row

    static void HA5()
    {
        Section("HA5 an HS2 pose file with an H row");
        var row = SwayRow();
        var res = PoseImport.ResolveAnime(db, row.Group, row.Category, row.No);
        Check("the pose import resolves the H row like a plain one (no code)", res.Row == row && res.Code == null, res.Code ?? "");
        var plain = CharaAnimation.Resolve(db, ContentIds.Vanilla, 19, 0, 0);
        PlayRow(female, plain, 0f, 0f);
        Frames(1);
        var pf = new PoseFile { Name = "hanime_ha5", Sex = 1, AnimeGroup = row.Group, AnimeCategory = row.Category, AnimeNo = row.No, NormalizedTime = 0.6f, EnableIK = false, EnableFK = false };
        string path = Path.Combine(WorkDir, "hanime_ha5.dat");
        File.WriteAllBytes(path, pf.Write());
        var r = Ok(new { cmd = "pose.load", key = female, path }, "pose.load hanime_ha5.dat");
        Frames(2);
        var a = Anim(female);
        var codes = (r?["findings"] as JArray)?.OfType<JObject>().Select(f => (string)f["code"]).ToList() ?? new List<string>();
        Check("loading it plays the H row at the file's time", a && a.IsHPlaying && a.Playing == row && Mathf.Abs(a.NormalizedTime - 0.6f) < 1e-3f,
            a ? $"playing {Row(a.Playing)}, time {F(a.NormalizedTime)}; findings {string.Join(", ", codes)}" : "not built");
        Check("no anime finding (missing / H)", !codes.Any(c => c.StartsWith("pose.anime", StringComparison.Ordinal) && c != "pose.animeHidden"), string.Join(", ", codes));
        PlayRow(female, plain, 0f, 0f);
    }

    // ---------------------------------------------------------------- HA7 root bones

    static string Tr(Transform t) => t ? $"lp ({F(t.localPosition.x)}, {F(t.localPosition.y)}, {F(t.localPosition.z)}) le ({F(t.localEulerAngles.x)}, {F(t.localEulerAngles.y)}, {F(t.localEulerAngles.z)})" : "-";

    static void HA7()
    {
        Section("HA7 a clip's root-node orientation offset stays on cf_J_Hips (as HS2)");
        // the male rows of the HS2 comparison carry a 180 degree offset; plain rows and one H row per group as well
        var offsetRows = new[] { (2, 100, 1), (6, 158, 2), (8, 204, 1), (37, 301, 1) }.Select(k => CharaAnimation.Resolve(db, ContentIds.Vanilla, k.Item1, k.Item2, k.Item3)).Where(r => r != null).ToList();
        var hrows = ListedH();
        var rng = new System.Random(5150);
        var more = hrows.Select(r => r.Group).Distinct().OrderBy(g => g).Select(g => { var l = hrows.Where(r => r.Group == g).ToList(); return l[rng.Next(l.Count)]; }).ToList();
        foreach (var (g, c, n) in new[] { (19, 0, 0), (11, 5, 0), (19, 1, 11), (15, 31, 1), (0, 0, 0) }) { var r = CharaAnimation.Resolve(db, ContentIds.Vanilla, g, c, n); if (r != null) more.Add(r); }
        var frame = new MotionFrame(Dt, Dt, 0, 0);
        int loads = 0, ok = 0, offsetOk = 0;
        var bad = new List<string>();
        foreach (var id in new[] { female, male })
        {
            var a = Anim(id);
            var bones = a.Context.BodyBones;
            bones.TryGetValue("cf_J_Root", out var root);
            bones.TryGetValue("cf_J_Hips", out var hips);
            var animT = a.Animator.transform;
            Vector3 rp = root.localPosition, ap = animT.localPosition; Quaternion rq = root.localRotation, aq = animT.localRotation;
            foreach (var row in offsetRows.Concat(more))
            {
                loads++;
                a.Speed = 1f;
                a.Load(row, 0.5f);
                for (int i = 0; i < 20; i++) a.Stage.Step(frame);
                bool rootKept = root.localPosition == rp && RotationCompare.Deg(root.localRotation, rq) == 0 && animT.localPosition == ap && RotationCompare.Deg(animT.localRotation, aq) == 0;
                if (rootKept) ok++; else bad.Add($"{(id == female ? "f" : "m")} {Row(row)}: root {Tr(root)}, p_cf_anim {Tr(animT)}");
                if (offsetRows.Contains(row))
                {
                    if (Mathf.Abs(Mathf.DeltaAngle(hips.localEulerAngles.y, 180f)) < 1e-3f) offsetOk++;
                    else bad.Add($"{(id == female ? "f" : "m")} {Row(row)}: hips {Tr(hips)}");
                    if (id == male) Info($"{Row(row)} male: root {Tr(root)} | hips {Tr(hips)}");
                }
            }
            a.Speed = 1f;
            PlayRow(id, CharaAnimation.Resolve(db, ContentIds.Vanilla, 19, 0, 0), 0f, 0f);
        }
        Check($"cf_J_Root and p_cf_anim keep their rest on every row ({ok} / {loads} loads, 20 steps each)", ok == loads, Brief(bad));
        Check($"the 180 degree offset of the compared male rows sits on cf_J_Hips (local y 180) ({offsetOk} / {offsetRows.Count * 2})", offsetRows.Count == 4 && offsetOk == offsetRows.Count * 2);
    }

    // ---------------------------------------------------------------- HA6 option items of H rows

    static void HA6()
    {
        Section("HA6 option items of H rows");
        var seen = new HashSet<string>();
        var rows = new List<StudioAnime>();
        foreach (var r in ListedH().Where(r => r.IsVanilla && r.Options.Count > 0))
            if (seen.Add(r.Options[0].Bundle + "|" + r.Options[0].Asset) && rows.Count < 3) rows.Add(r);
        Info("rows: " + string.Join(", ", rows.Select(r => $"{Row(r)} ({r.Options.Count} items)")));
        Check("rows with option items exist", rows.Count > 0, rows.Count.ToString());
        foreach (var row in rows)
        {
            var a = Anim(female);
            int logBefore = a.Log.Count;
            PlayRow(female, row, 0.2f, 1f);
            Frames(12);
            a = Anim(female);
            var items = a.Items.Items;
            var failedLoads = new List<string>();
            for (int i = logBefore; i < a.Log.Count; i++)
                if (a.Log[i].IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 || a.Log[i].IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0) failedLoads.Add(a.Log[i]);
            bool parented = items.Count > 0 && items.All(it => it.Root && it.Moved.Count > 0 && it.Moved.All(m => m.moved && m.parent && m.moved.parent == m.parent));
            Check($"{Row(row)}: {items.Count} of {row.Options.Count} items loaded and parented under character bones, no failed load", items.Count == row.Options.Count && parented && failedLoads.Count == 0,
                string.Join(" | ", failedLoads.Take(3)));
            var st = a.Driver.State(0);
            int synced = 0, syncOk = 0;
            foreach (var it in items)
            {
                if (!it.Synced || it.Driver == null) continue;
                synced++;
                var s = it.Driver.State(0);
                if (s.shortNameHash == st.shortNameHash && Mathf.Abs(s.normalizedTime - st.normalizedTime) <= 1e-3f) syncOk++;
            }
            Check($"{Row(row)}: synced items on the body's state and time ({syncOk} / {synced} animated items)", syncOk == synced, $"body time {F(st.normalizedTime)}");
            Ok(new { cmd = "chara.animeOption", key = female, visible = false }, "chara.animeOption visible false");
            bool hidden = items.All(it => it.Renderers.All(rn => !rn || !rn.enabled));
            Ok(new { cmd = "chara.animeOption", key = female, visible = true }, "chara.animeOption visible true");
            bool shown = items.All(it => it.Renderers.All(rn => !rn || rn.enabled));
            Check($"{Row(row)}: the items' visibility follows the option", hidden && shown);
        }
        PlayRow(female, CharaAnimation.Resolve(db, ContentIds.Vanilla, 19, 0, 0), 0f, 1f);
    }

    // ---------------------------------------------------------------- corpus

    sealed class Fail { public string Cls, Detail; }

    public static void RunCorpus()
    {
        rep = new StringBuilder(); passes = fails = warns = 0;
        Directory.CreateDirectory(OutDir);
        int limit = IntArg("-limit", 0), release = Math.Max(1, IntArg("-release", 200));
        var sw = Stopwatch.StartNew();
        var head = new StringBuilder();
        ContentCatalog gd = null;
        var host = new GameObject("hanime_corpus");
        var fl = new List<(StudioAnime row, Fail f)>();
        try
        {
            gd = CardResolveTest.BuildDb(head);
            db = gd.LoadStudioLists();
            rep.AppendLine($"H animation corpus (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}); limit {(limit > 0 ? limit.ToString() : "none")}, release every {release} rows");
            rep.Append(head);
            var rows = ListedH();
            if (limit > 0 && rows.Count > limit) rows = rows.Take(limit).ToList();
            var animator = host.AddComponent<Animator>();
            var drivers = new Dictionary<RuntimeAnimatorController, AnimationDriver>();
            var clipNames = new Dictionary<RuntimeAnimatorController, HashSet<string>>();
            long peakWs = 0, peakUnity = 0;
            int releases = 0;
            int baseOk = 0, ovRows = 0, ovOk = 0, subsetOk = 0, stateOk = 0, swayRows = 0, swayOk = 0, swayLines = 0, optRows = 0, optOk = 0, optItems = 0;
            double loadMs = 0;
            void Release()
            {
                foreach (var d in drivers.Values) d.Dispose();
                drivers.Clear(); clipNames.Clear();
                Info($"release {++releases}: {gd.OpenBundleCount} bundles open, unity allocated {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576.0:F0} MB, reserved {UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576.0:F0} MB, managed {GC.GetTotalMemory(false) / 1048576.0:F0} MB");
                peakUnity = Math.Max(peakUnity, UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong());
                peakWs = Math.Max(peakWs, UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong());
                gd.UnloadAll();
                GC.Collect();
            }
            HashSet<string> Names(RuntimeAnimatorController c)
            {
                if (!clipNames.TryGetValue(c, out var s)) clipNames[c] = s = new HashSet<string>(c.animationClips.Where(x => x).Select(x => x.name), StringComparer.Ordinal);
                return s;
            }
            for (int i = 0; i < rows.Count; i++)
            {
                if (i > 0 && i % release == 0) Release();
                var row = rows[i];
                try
                {
                    var t0 = sw.Elapsed.TotalMilliseconds;
                    var ctrl = gd.LoadAsset<RuntimeAnimatorController>(row);
                    if (!ctrl) { fl.Add((row, new Fail { Cls = "base.missing", Detail = row.BundlePath + "/" + row.FileName })); continue; }
                    baseOk++;
                    if (!drivers.TryGetValue(ctrl, out var drv)) drivers[ctrl] = drv = AnimationDriver.Create(AnimationEngine.Playable, animator, ctrl, "hanime_corpus");
                    if (!string.IsNullOrEmpty(row.OverrideBundle) && !string.IsNullOrEmpty(row.OverrideFile))
                    {
                        ovRows++;
                        var ov = gd.LoadAssetAt<RuntimeAnimatorController>(row.Zipmod, row.OverrideBundle, "", row.OverrideFile);
                        if (!ov) fl.Add((row, new Fail { Cls = "override.missing", Detail = row.OverrideBundle + "/" + row.OverrideFile }));
                        else
                        {
                            ovOk++;
                            var extra = Names(ov).Where(n => !Names(ctrl).Contains(n)).ToList();
                            if (extra.Count == 0) subsetOk++;
                            else fl.Add((row, new Fail { Cls = "override.clipsOutsideBase", Detail = $"{row.OverrideFile}: {extra.Count} clips not in {ctrl.name}" }));
                        }
                    }
                    int hash = Animator.StringToHash(row.Clip ?? "");
                    int bl = row.BreastLayer;
                    if (drv.HasState(0, hash) || (bl > 0 && bl < drv.LayerCount && drv.HasState(bl, hash))) stateOk++;
                    else fl.Add((row, new Fail { Cls = "state.missing", Detail = $"{ctrl.name}: clip state not on layer 0 or {bl} ({drv.LayerCount} layers)" }));
                    if (!string.IsNullOrEmpty(row.YureBundle) && !string.IsNullOrEmpty(row.YureFile))
                    {
                        swayRows++;
                        var tab = new HSwayTable();
                        if (!tab.Load(gd, row, out var note)) fl.Add((row, new Fail { Cls = "sway.missing", Detail = note }));
                        else if (tab.Lines.Count == 0) fl.Add((row, new Fail { Cls = "sway.noLines", Detail = $"{row.YureFile} motion {row.MotionId}{(tab.OwnerColumn ? " owner " + row.YureWho : "")}: 0 of {tab.TableRows} rows" }));
                        else { swayOk++; swayLines += tab.Lines.Count; }
                    }
                    if (row.Options.Count > 0)
                    {
                        optRows++;
                        bool all = true;
                        foreach (var o in row.Options)
                        {
                            optItems++;
                            var prefab = gd.LoadAssetAt<GameObject>(row.Zipmod, o.Bundle, o.Manifest, o.Asset);
                            if (!prefab) { all = false; fl.Add((row, new Fail { Cls = "option.missing", Detail = o.Bundle + "/" + o.Asset })); continue; }
                            if (!string.IsNullOrEmpty(o.ControllerBundle) && !string.IsNullOrEmpty(o.ControllerAsset) && !gd.LoadAssetAt<RuntimeAnimatorController>(row.Zipmod, o.ControllerBundle, "", o.ControllerAsset))
                            { all = false; fl.Add((row, new Fail { Cls = "option.controllerMissing", Detail = o.ControllerBundle + "/" + o.ControllerAsset })); }
                            if (!string.IsNullOrEmpty(o.OverrideBundle) && !string.IsNullOrEmpty(o.OverrideAsset) && !gd.LoadAssetAt<RuntimeAnimatorController>(row.Zipmod, o.OverrideBundle, "", o.OverrideAsset))
                            { all = false; fl.Add((row, new Fail { Cls = "option.overrideMissing", Detail = o.OverrideBundle + "/" + o.OverrideAsset })); }
                        }
                        if (all) optOk++;
                    }
                    loadMs += sw.Elapsed.TotalMilliseconds - t0;
                }
                catch (Exception e) { fl.Add((row, new Fail { Cls = "exception", Detail = e.GetType().Name + ": " + e.Message })); }
            }
            Release();
            double secs = sw.Elapsed.TotalSeconds;
            Section("result");
            Info($"rows {rows.Count} (vanilla {rows.Count(r => r.IsVanilla)}, mod {rows.Count(r => !r.IsVanilla)}): base controllers {baseOk}; override rows {ovRows}, loaded {ovOk}, clips within the base {subsetOk}; " +
                 $"state found {stateOk}; sway rows {swayRows}, with lines {swayOk} ({swayLines} lines); option rows {optRows} ({optItems} items), all loaded {optOk}");
            Info($"ZU5 corpus: {secs:F1} s ({rows.Count / Math.Max(1e-3, secs):F0} rows/s, loads {loadMs / 1000:F1} s), {releases} releases; peak unity allocated {peakUnity / 1048576.0:F0} MB, peak reserved {peakWs / 1048576.0:F0} MB");
            foreach (var g in fl.GroupBy(x => x.f.Cls).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                Info($"class {g.Key}: {g.Count()} rows");
                foreach (var x in g.Take(6)) Info($"   {Row(x.row)} {x.row.Guid}: {x.f.Detail}");
            }
            bool full = limit <= 0;
            var byClass = fl.GroupBy(x => x.f.Cls).ToDictionary(g => g.Key, g => g.Count());
            int Of(string c) => byClass.TryGetValue(c, out int v) ? v : 0;
            var over = byClass.Where(kv => !KnownCorpus.ContainsKey(kv.Key) || kv.Value > KnownCorpus[kv.Key].max).Select(kv => kv.Key + " " + kv.Value).ToList();
            foreach (var kv in KnownCorpus) Info($"known class {kv.Key}: {Of(kv.Key)} rows (max {kv.Value.max}): {kv.Value.why}");
            Check("rows read: every listed H row (6,145 vanilla)", !full || rows.Count(r => r.IsVanilla) == 6145, rows.Count.ToString());
            Check("no exception", Of("exception") == 0, Of("exception").ToString());
            Check("every failure is a known data class within its maximum (0 unclassified)", over.Count == 0, over.Count == 0 ? $"{fl.Count} classified rows" : string.Join(", ", over));
            Check("every base controller loads", baseOk == rows.Count, $"{baseOk} / {rows.Count}");
            Check("every override controller loads; its clip names are within the base's except the known rows", ovOk == ovRows && subsetOk + Of("override.clipsOutsideBase") == ovOk,
                $"{ovOk} / {ovRows}, within {subsetOk}");
            Check("every row's clip state exists on layer 0 or its breast layer except the known rows", stateOk + Of("state.missing") == baseOk, $"{stateOk} / {baseOk}");
            Check($"sway tables: {swayOk} of {swayRows} rows find lines, the others known", swayOk + Of("sway.noLines") + Of("sway.missing") == swayRows && (!full || swayRows == 1136),
                $"rows {swayRows} (1,136 expected), with lines {swayOk}");
            Check("option items of every row with a block load except the known rows (494 rows expected)", optOk + RowsOf(fl, "option.missing") == optRows && (!full || optRows == 494),
                $"{optOk} / {optRows}");
        }
        catch (Exception e) { Check("corpus (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        finally { Object.DestroyImmediate(host); }
        rep.Insert(0, $"SUMMARY: {passes} PASS, {fails} FAIL ({warns} WARN, {sw.Elapsed.TotalSeconds:F0} s)\n");
        File.WriteAllText(Path.Combine(OutDir, "hanime_corpus_report.txt"), rep.ToString(), new UTF8Encoding(false));
        gd?.UnloadAll();
        Debug.Log($"HANIME CORPUS DONE: {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }

    static int RowsOf(List<(StudioAnime row, Fail f)> fl, string cls) => fl.Where(x => x.f.Cls == cls).Select(x => x.row).Distinct().Count();

    // Failures of the installed list data (measured over the 6,145 vanilla H rows; the game cannot play them either):
    // class -> (maximum rows, why).
    static readonly Dictionary<string, (int max, string why)> KnownCorpus = new Dictionary<string, (int, string)>(StringComparer.Ordinal)
    {
        ["override.clipsOutsideBase"] = (25, "one override controller carries a clip its base does not have (that clip is never played)"),
        ["state.missing"] = (6, "the row's clip is no state of its base controller (nothing plays; the current motion's pose stays)"),
        ["option.missing"] = (3, "an option item whose asset cell names a bundle path, or an asset its bundle does not hold"),
        ["sway.noLines"] = (1, "a table named with the owner-column prefix that has no owner column: its lines never match (everything enabled)"),
    };
}
