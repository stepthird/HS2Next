// Lightmap paths of the fitted shaders (Phase 3.7 package L; Shaders/README.md
// "Lightmap paths (Phase 3.7, package L)").
//
//   M7 (edit mode, graphics): ShaderMap.RouteLightmapped keeps legacy and unlit replacements on a lightmapped renderer (only a
//       lit replacement without a lightmap path gets a URP/Lit copy), and lightmapped legacy probes (Tests/FitCases/
//       l_legacy_lm_fit.txt on the Editor's Built-in Legacy Shaders/Diffuse and Bumped Specular) match Built-in.
//       Run: & Tools\unity_batch.ps1 -Method LightmapPaths.Run   -> Spike/out/maps/lightmap_paths_report.txt
//       (ModShaderTest may call LightmapPaths.M7(check) for its M7 rows.)
//   RunProbe (play mode, graphics): originals that exist only in map bundles (ASESampleShaders/Translucency, Bakery/Standard,
//       Standard glass of bushitsu): the map is loaded through MapRealization, the bundle material taken from MapMaterials'
//       origins, the map hidden, and SkinFit sweep files run on it (Built-in reference, and the replacement under the Spike
//       URP asset when one is named). Jobs from -lmJobs <file>, one per line:
//         map ref | original shader | material name or * | replacement or - | case file (Tests/FitCases/...) | output suffix
//       Output: Spike/out/fit/<shader>_<suffix>.txt in SkinFit.Sweep's format; report Spike/out/maps/lightmap_probe.txt.
//       Run: & Tools\unity_batch.ps1 -Method LightmapPaths.RunProbe -NoQuit -MethodArgs @('-lmJobs','<file>')
//   Serve (play mode, the app): BridgeServe plus one Editor-only command, dev.mapRenderers (enable / disable renderers of the
//       loaded map by original shader, material or path substring; the bushitsu attribution).
//       Run: & Tools\unity_batch.ps1 -Method LightmapPaths.Serve -NoQuit -MethodArgs @('-bridgePort','17374','-serveMinutes','60')
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.Api.Bridge;
using HS2Next.App;
using HS2Next.App.Commands;
using HS2Next.Content;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class LightmapPaths
{
    static string Root => ContentSettings.ProjectRoot;
    static string OutDir => Path.Combine(Root, "Spike", "out", "maps");
    static string FitDir => Path.Combine(Root, "Spike", "out", "fit");
    static UniversalRenderPipelineAsset SpikeAsset => AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AppPipeline.MeasureAssetPath);

    static string Arg(string name, string def)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static StringBuilder rep;
    static int pass, fail;
    static void Begin() { rep = new StringBuilder(); pass = fail = 0; }
    static void Section(string s) => rep.AppendLine("\n== " + s);
    static void Info(string s) => rep.AppendLine("   " + s);
    static void Check(string what, bool ok, string detail = "")
    {
        if (ok) pass++; else fail++;
        rep.AppendLine($"   {(ok ? "PASS" : "FAIL")} {what}{(string.IsNullOrEmpty(detail) ? "" : " - " + detail)}");
    }
    static void End(string file, string title, Stopwatch sw)
    {
        Directory.CreateDirectory(OutDir);
        rep.Insert(0, $"SUMMARY: {pass} PASS, {fail} FAIL\n{title} ({sw.Elapsed.TotalSeconds:F0} s)\n");
        File.WriteAllText(Path.Combine(OutDir, file), rep.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        Debug.Log($"{title}: {pass} PASS, {fail} FAIL -> {Path.Combine(OutDir, file)}");
    }

    // ------------------------------------------------------------------ M7

    /// <summary>Edit-mode entry: the M7 checks into Spike/out/maps/lightmap_paths_report.txt (exit code 1 on a FAIL).</summary>
    public static void Run()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        var prev = GraphicsSettings.defaultRenderPipeline;
        try { M7(Check, Info); }
        catch (Exception e) { Check("M7 ran", false, e.ToString()); }
        finally { ShaderFit.SetPipeline(prev); }
        End("lightmap_paths_report.txt", "LIGHTMAP PATHS (M7)", sw);
        EditorApplication.Exit(fail > 0 ? 1 : 0);
    }

    /// <summary>
    /// The M7 checks: routing of lightmapped renderers (legacy and unlit replacements kept,
    /// a lit replacement without a lightmap path moved to URP/Lit) and lightmapped legacy probes against Built-in
    /// (RMS ≤ 0.002 over Tests/FitCases/l_legacy_lm_fit.txt). Needs a graphics device for the probes.
    /// </summary>
    public static void M7(Action<string, bool, string> check, Action<string> info = null)
    {
        // routing
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        var lmTex = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, true);
        lmTex.SetPixels(Enumerable.Repeat(new Color(0.5f, 0.4f, 0.3f, 1), 16).ToArray()); lmTex.Apply();
        var lmPrev = LightmapSettings.lightmaps;
        try
        {
            var names = new[] { "HS2Next/Legacy Shaders/Diffuse", "HS2Next/Legacy Shaders/Transparent/Bumped Specular", "HS2Next/Unlit/Transparent",
                                "HS2Next/Mobile/Particles/Additive", "HS2Next/AIT main standard2 studio", "HS2Next/Standard" };
            var mats = names.Select(n => new Material(Shader.Find(n))).ToArray();
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            LightmapSettings.lightmaps = new[] { new LightmapData { lightmapColor = lmTex } };
            r.lightmapIndex = 0; r.lightmapScaleOffset = new Vector4(1, 1, 0, 0);
            int moved = ShaderMap.RouteLightmapped(r);
            var after = r.sharedMaterials;
            var kept = Enumerable.Range(0, names.Length).Where(i => after[i] == mats[i]).Select(i => names[i]).ToList();
            bool ok = moved == 1 && after[4] != mats[4] && after[4].shader.name == "Universal Render Pipeline/Lit"
                      && Enumerable.Range(0, names.Length).All(i => i == 4 || after[i] == mats[i]);
            check("M7.route", ok, $"{moved} slot moved (expected the lit AIT main standard2 studio only); kept [{string.Join(", ", kept)}]");
            foreach (var n in names.Take(4))
                check($"M7.lightmapPath {n}", ShaderMap.SupportsLightmaps(Shader.Find(n)) || Unlit(Shader.Find(n)),
                    $"SupportsLightmaps {ShaderMap.SupportsLightmaps(Shader.Find(n))}, NeedsNoLightmap {Unlit(Shader.Find(n))}");
            check("M7.litWithoutPath", !ShaderMap.SupportsLightmaps(Shader.Find("HS2Next/AIT main standard2 studio")) && !Unlit(Shader.Find("HS2Next/AIT main standard2 studio")),
                "AIT main standard2 studio: no lightmap path and lit (routed)");
            // originals whose bundle copy has no LIGHTMAP_ON variant: the replacement stays (HS2 draws them without the lightmap)
            var ms2 = Shader.Find("HS2Next/AIT main standard2 studio");
            var m1 = new Material(ms2); var m2 = new Material(ms2);
            r.sharedMaterials = new[] { m1, m2 };
            var noLm = ms2; var withLm = Shader.Find("Universal Render Pipeline/Lit");
            int moved2 = ShaderMap.RouteLightmapped(r, m => m == m1 ? noLm : withLm);
            var after2 = r.sharedMaterials;
            check("M7.routeOriginal", moved2 == 1 && after2[0] == m1 && after2[1] != m2,
                $"{moved2} moved; slot whose original compiles no LIGHTMAP_ON kept {after2[0] == m1}, slot whose original has it routed {after2[1] != m2}");
            Object.DestroyImmediate(m1); Object.DestroyImmediate(m2);
            foreach (var m in mats) Object.DestroyImmediate(m);
        }
        finally
        {
            LightmapSettings.lightmaps = lmPrev ?? new LightmapData[0];
            Object.DestroyImmediate(go); Object.DestroyImmediate(lmTex);
        }

        // probes: the Editor's Built-in legacy shaders on a lightmapped quad vs the generated replacements (measurement URP asset)
        var cases = Path.Combine(Root, "Tests", "FitCases", "l_legacy_lm_fit.txt");
        if (!File.Exists(cases)) { check("M7.probe cases", false, cases + " missing"); return; }
        var jobs = SkinFit.ReadJobs(cases);
        foreach (var s in new[] { "Legacy Shaders/Diffuse", "Legacy Shaders/Bumped Specular", "Legacy Shaders/Transparent/Bumped Specular" })
        {
            var orig = Shader.Find(s); var repl = Shader.Find("HS2Next/" + s);
            if (!orig || !repl) { check($"M7.probe {s}", false, "shader missing"); continue; }
            var bi = SkinFit.Eval(jobs, orig, null, null);
            var ur = SkinFit.Eval(jobs, repl, null, SpikeAsset);
            double e = 0, worst = 0; int n = 0;
            foreach (var k in bi.Keys)
            {
                Color a = bi[k], b = ur[k];
                e += Sq(a.r - b.r) + Sq(a.g - b.g) + Sq(a.b - b.b); n += 3;
                worst = Math.Max(worst, Math.Max(Math.Abs(a.r - b.r), Math.Max(Math.Abs(a.g - b.g), Math.Abs(a.b - b.b))));
            }
            double rms = Math.Sqrt(e / Math.Max(1, n));
            check($"M7.probe {s}", rms <= 0.002, $"RMS {rms:F4} over {n} values (max {worst:F4}), l_legacy_lm_fit.txt");
        }
    }

    static double Sq(double v) => v * v;
    // the rule of ShaderMap.NeedsNoLightmap: no additional-light keyword = no lit forward pass
    static bool Unlit(Shader s) => s && !s.keywordSpace.FindKeyword("_ADDITIONAL_LIGHTS").isValid;
    static string F(Color c) => $"{c.r:F4},{c.g:F4},{c.b:F4}";

    // ------------------------------------------------------------------ play-mode probes of map-bundle originals

    static string ProbeReport => Path.Combine(OutDir, "lightmap_probe.txt");

    public static void RunProbe()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(ProbeReport, "SUMMARY: 0 PASS, 1 FAIL\nLightmapPaths.probe: started, no result yet\n", new UTF8Encoding(false));
        PlayModeHarness.Start(nameof(LightmapPaths) + "." + nameof(ProbeMain), ProbeReport, 40);
    }

    struct ProbeJob { public string map, shader, material, repl, cases, suffix; }

    static IEnumerator ProbeMain()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"LightmapPaths.probe (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
        var jobs = new List<ProbeJob>();
        string jf = Arg("-lmJobs", null);
        if (jf != null && File.Exists(jf))
            foreach (var line in File.ReadAllLines(jf))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                var f = line.Split('|').Select(x => x.Trim()).ToArray();
                if (f.Length < 6) continue;
                jobs.Add(new ProbeJob { map = f[0], shader = f[1], material = f[2], repl = f[3], cases = f[4], suffix = f[5] });
            }
        Check("probe jobs read", jobs.Count > 0, jf ?? "(no -lmJobs)");
        var gd = CardResolveTest.BuildDb(null);
        var db = gd.LoadStudioLists(new HS2Next.Content.Studio.StudioLoadOptions { EmulateAIInstallGate = false });
        var findings = new SceneFindings();
        var map = new MapRealization(gd, db, findings, null);
        var prevPipe = GraphicsSettings.defaultRenderPipeline;
        bool[] ok = { false };
        string cur = null;
        foreach (var jb in jobs)
        {
            Section($"{jb.shader} ({jb.map}, material {jb.material}) -> {jb.repl}: {jb.cases}");
            if (cur != jb.map)
            {
                map.SetFlags(true, true);
                map.SetTarget(jb.map, HS2Next.Save.Vec3.Zero, HS2Next.Save.Vec3.Zero);
                yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return !map.IsBusy; }, 300, ok);
                cur = jb.map;
                if (map.State == MapState.Loaded) MapInfo(map);
            }
            if (map.State != MapState.Loaded) { Check(jb.map + " loaded", false, map.State.ToString()); continue; }
            Material src = null;
            foreach (var r in map.Structure.Renderers)
            {
                if (!r) continue;
                foreach (var m in r.sharedMaterials)
                {
                    var o = map.Materials.OriginOf(m);
                    if (o != null && o.ShaderOriginal == jb.shader && o.Source && (jb.material == "*" || o.Source.name == jb.material)) { src = o.Source; break; }
                }
                if (src) break;
            }
            Check($"a {jb.shader} source material", src != null);
            if (src == null) continue;
            Info($"source {src.name}: keywords [{string.Join(" ", MaterialCopies.KeywordsOf(src))}], queue {src.renderQueue}");
            // the map's own geometry and lightmaps stay out of the probe view
            var active = map.Structure.Roots.Where(g => g).ToDictionary(g => g, g => g.activeSelf);
            foreach (var g in active.Keys) g.SetActive(false);
            var lmSaved = LightmapSettings.lightmaps; var modeSaved = LightmapSettings.lightmapsMode;
            try { Sweep(jb, src); }
            catch (Exception e) { Check(jb.shader + " probe ran", false, e.ToString()); }
            finally
            {
                ShaderFit.SetPipeline(prevPipe);
                LightmapSettings.lightmapsMode = modeSaved; LightmapSettings.lightmaps = lmSaved;
                foreach (var kv in active) if (kv.Key) kv.Key.SetActive(kv.Value);
            }
            yield return null;
        }
        map.SetTarget(null, HS2Next.Save.Vec3.Zero, HS2Next.Save.Vec3.Zero);
        yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return !map.IsBusy && map.PendingUnloads == 0; }, 60, ok);
        try { gd.UnloadAll(); } catch (Exception) { }
        ShaderFit.SetPipeline(prevPipe);
        End("lightmap_probe.txt", "LIGHTMAP PROBE", sw);
        PlayModeHarness.ExitCode = fail > 0 ? 1 : 0;
    }

    // Realtime GI (DYNAMICLIGHTMAP_ON record): renderers with a realtime lightmap index after the load; lightmapped slots by the
    // shader they are drawn with and how many keep a replacement because their original compiles no LIGHTMAP_ON.
    static void MapInfo(MapRealization map)
    {
        int lm = 0, rt = 0, n = 0, slots = 0, lit = 0, noLmOrig = 0;
        var litBy = new Dictionary<string, int>();
        foreach (var r in map.Structure.Renderers)
        {
            if (!r) continue;
            n++;
            if (r.realtimeLightmapIndex >= 0 && r.realtimeLightmapIndex < 0xFFFE) rt++;
            if (!ShaderMap.IsLightmapped(r)) continue;
            lm++;
            foreach (var m in r.sharedMaterials)
            {
                if (!m || !m.shader) continue;
                slots++;
                var o = map.Materials.OriginOf(m);
                if (m.shader.name == "Universal Render Pipeline/Lit")
                {
                    lit++;
                    string k = o == null ? "(no origin)" : $"{o.ShaderOriginal} (keyword space {(o.SourceShader ? o.SourceShader.keywordSpace.keywordCount : -1)}, LIGHTMAP_ON {(o.SourceShader && o.SourceShader.keywordSpace.FindKeyword("LIGHTMAP_ON").isValid)})";
                    litBy[k] = litBy.TryGetValue(k, out var c) ? c + 1 : 1;
                }
                if (o != null && o.SourceShader && !o.SourceShader.keywordSpace.FindKeyword("LIGHTMAP_ON").isValid && !ShaderMap.SupportsLightmaps(m.shader)) noLmOrig++;
            }
        }
        Info($"map: {n} renderers, {lm} lightmapped ({slots} slots: {lit} on URP/Lit, {noLmOrig} kept because the original has no LIGHTMAP_ON), {rt} with a realtime (Enlighten) lightmap index; LightmapSettings {LightmapSettings.lightmaps.Length} lightmaps, mode {LightmapSettings.lightmapsMode}");
        foreach (var kv in litBy.OrderByDescending(x => x.Value).Take(12)) Info($"   on URP/Lit: {kv.Value} x {kv.Key}");
    }

    static void Sweep(ProbeJob jb, Material src)
    {
        var jobs = SkinFit.ReadJobs(Path.Combine(Root, "Tests", "FitCases", jb.cases));
        var bi = SkinFit.Eval(jobs, src.shader, src, null);
        var repl = jb.repl != "-" ? Shader.Find(jb.repl) : null;
        if (jb.repl != "-" && !repl) Check(jb.repl + " found", false);
        var ur = repl ? SkinFit.Eval(jobs, repl, src, SpikeAsset) : null;
        var sb = new StringBuilder($"# sweep {jb.shader}  pipe {(ur != null ? "both" : "builtin")}{(repl ? "  repl " + repl.name : "")}  map {jb.map} material {src.name}\n");
        sb.AppendLine($"# source {src.name}: keywords used [{string.Join(" ", MaterialCopies.KeywordsOf(src))}]");
        double tot = 0; int totN = 0;
        for (int j = 0; j < jobs.Count; j++)
        {
            var job = jobs[j];
            sb.AppendLine($"\n## {job.label}   set [{job.setStr}]{(job.tilt != 0 ? $"  tilt {job.tilt}" : "")}");
            sb.AppendLine($"{job.p1,-12} {job.p2,-12} | " + string.Join("  ", job.lights.Select(l => $"{l.name,-20}")));
            double e = 0; int n = 0;
            for (int ia = 0; ia < job.v1.Length; ia++)
                for (int ib = 0; ib < job.v2.Length; ib++)
                {
                    var cells = new List<string>();
                    for (int il = 0; il < job.lights.Length; il++)
                    {
                        var k = (j, ia, ib, il);
                        if (ur != null)
                        {
                            Color a = bi[k], b = ur[k];
                            cells.Add($"{a.r:F3}/{b.r:F3} {a.g:F3}/{b.g:F3} {a.b:F3}/{b.b:F3}");
                            e += Sq(a.r - b.r) + Sq(a.g - b.g) + Sq(a.b - b.b); n += 3;
                        }
                        else cells.Add($"{bi[k].r:F4},{bi[k].g:F4},{bi[k].b:F4}");
                    }
                    sb.AppendLine($"{job.v1[ia],-12} {job.v2[ib],-12} | " + string.Join("  ", cells));
                }
            if (ur != null) { sb.AppendLine($"   block RMS {Math.Sqrt(e / Math.Max(1, n)):F4}"); tot += e; totN += n; }
        }
        double rms = Math.Sqrt(tot / Math.Max(1, totN));
        if (ur != null) sb.Insert(0, $"# TOTAL RMS {rms:F4} over {totN} values\n");
        Directory.CreateDirectory(FitDir);
        string file = Path.Combine(FitDir, Safe(jb.shader) + "_" + jb.suffix + ".txt");
        File.WriteAllText(file, sb.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        if (ur != null) Check($"{jb.shader} vs {jb.repl}: RMS {rms:F4} over {totN} values", true, Path.GetFileName(file));
        else Info($"Built-in only: {Path.GetFileName(file)}");
    }

    static string Safe(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) ? c : '_'));

    // ------------------------------------------------------------------ BridgeServe with dev.mapRenderers

    const string Launch = "{\"app\":{\"autoStart\":true},\"bridge\":{\"allowQuit\":true}}";
    static string ServeReport => Path.Combine(Root, "Spike", "out", "bridge", "lightmap_serve_report.txt");

    static double ServeMinutes => double.TryParse(Arg("-serveMinutes", "60"), NumberStyles.Float, CultureInfo.InvariantCulture, out var m) && m > 0 ? m : 60;

    public static void Serve()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ServeReport));
        File.WriteAllText(ServeReport, $"LightmapPaths.Serve: requested {DateTime.Now:yyyy-MM-dd HH:mm:ss}, {ServeMinutes:F0} min\n", new UTF8Encoding(false));
        PlayModeHarness.Start(nameof(LightmapPaths) + "." + nameof(ServeMain), ServeReport, ServeMinutes + 5, true, Launch);
    }

    static void Line(string s)
    {
        Debug.Log("LightmapPaths.Serve: " + s);
        try { File.AppendAllText(ServeReport, s + "\n", new UTF8Encoding(false)); } catch (Exception) { }
    }

    static IEnumerator ServeMain()
    {
        double minutes = ServeMinutes;
        var sw = Stopwatch.StartNew();
        AppCore app = null;
        while (sw.Elapsed.TotalSeconds < 60 && (app = AppCore.Current) == null) yield return null;
        if (app == null) { Line("RESULT: FAIL (no app started)"); PlayModeHarness.Finish(1); yield break; }
        while (app.Stage == AppStage.Starting && sw.Elapsed.TotalMinutes < minutes) yield return null;
        if (app.Stage == AppStage.Ready && app.BridgeServices != null)
        {
            try
            {
                CommandKit.Register(app.BridgeServices.Commands, app, CommandKit.Spec("dev.mapRenderers",
                        "Editor-only (LightmapPaths.Serve): enables or disables the loaded map's renderers whose original shader (exact), material name (substring) or path (substring) matches; every given filter must match.")
                    .Arg("shader", ArgTypes.String, "original shader name (exact) of any slot", defaultValue: "")
                    .Arg("material", ArgTypes.String, "material name substring of any slot", defaultValue: "")
                    .Arg("path", ArgTypes.String, "renderer path substring", defaultValue: "")
                    .Arg("enabled", ArgTypes.Bool, "the value to set", defaultValue: true)
                    .Arg("all", ArgTypes.Bool, "apply to every renderer of the map (filters ignored)", defaultValue: false)
                    .Return("matched", ArgTypes.Int, "renderers set").Return("paths", ArgTypes.ArrayOf(ArgTypes.String), "the first 200 paths")
                    .WithFlags(CommandFlags.ReadOnly).WithTimeout(60), DevMapRenderers);
                Line("dev.mapRenderers registered");
            }
            catch (Exception e) { Line("dev.mapRenderers not registered: " + e.Message); }
            Line($"ready after {sw.Elapsed.TotalSeconds:F1} s; listening on {app.Bridge?.Url ?? "(not listening: " + app.Bridge?.BindError + ")"}; serving up to {minutes:F0} min (stop with app.quit)");
        }
        else Line($"startup failed after {sw.Elapsed.TotalSeconds:F1} s: {app.Error}");
        while (sw.Elapsed.TotalMinutes < minutes && !app.IsDisposed) yield return null;
        Line(app.IsDisposed ? "the app was disposed; ending" : $"time limit ({minutes:F0} min) reached; ending");
        PlayModeHarness.Finish(0);
    }

    static object DevMapRenderers(CoreContext c)
    {
        var part = c.Runtime.GetPart<MapRealization>();
        if (part == null || part.State != MapState.Loaded) throw new InvalidOperationException("no map loaded");
        string shader = c.Args.Get("shader", ""), material = c.Args.Get("material", ""), path = c.Args.Get("path", "");
        bool enabled = c.Args.Get("enabled", true), all = c.Args.Get("all", false);
        var paths = new List<string>();
        int n = 0;
        foreach (var r in part.Structure.Renderers)
        {
            if (!r) continue;
            string p = part.Structure.PathOf(r.transform);
            bool hit = all;
            if (!all)
            {
                bool sOk = shader.Length == 0, mOk = material.Length == 0;
                foreach (var m in r.sharedMaterials)
                {
                    if (!m) continue;
                    var o = part.Materials.OriginOf(m);
                    string orig = o != null ? o.ShaderOriginal : m.shader ? m.shader.name : "";
                    string mn = o != null && o.Source ? o.Source.name : m.name;
                    if (!sOk && orig == shader) sOk = true;
                    if (!mOk && mn.IndexOf(material, StringComparison.Ordinal) >= 0) mOk = true;
                }
                hit = sOk && mOk && (path.Length == 0 || p.IndexOf(path, StringComparison.Ordinal) >= 0);
            }
            if (!hit) continue;
            r.enabled = enabled;
            n++;
            if (paths.Count < 200) paths.Add(p);
        }
        return new BridgeReply().With("matched", n).With("paths", paths);
    }
}

/// <summary>ModShaderTest's M7 hook (found by reflection, Editor, global namespace): the checks of <see cref="LightmapPaths.M7"/>.</summary>
public static class ModShaderLightmapChecks
{
    public static void Run(Action<string, bool, string> check, Action<string> info) => LightmapPaths.M7(check, info);
}
