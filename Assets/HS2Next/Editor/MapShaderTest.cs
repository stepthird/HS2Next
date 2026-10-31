// MapShaderTest (package B of Phase 3.2): the map shader families.
// Run (edit mode; the graphics part runs when a device exists):
//   & Tools\unity_batch.ps1 -Method MapShaderTest.Run [-NoGraphics]          -> Spike/out/maps/map_shader_report.txt
//   & Tools\unity_batch.ps1 -Method MapShaderTest.RunProbe -NoQuit           -> Spike/out/maps/map_shader_probe.txt
// Run (headless part):
//   S1 every target of ShaderMap's "maps (3.2 package B)" block resolves in the Editor and compiles without errors;
//   S2 property names / types / defaults of the fitted / approximate replacements equal the originals' (Tools/survey/
//      map_shader_props.json, read from the bundles by Tools/survey/map_shader_props.py); URP's SpeedTree7 declares every
//      property of Nature/SpeedTree (M7: the material's values carry over);
//   S3 lit forward passes of the new shaders declare the light-loop keywords (_CLUSTER_LIGHT_LOOP, _LIGHT_LAYERS,
//      _ADDITIONAL_LIGHT_SHADOWS; Shaders/README.md "URP pipeline features"); SpeedTree7 declares the census keyword sets;
//   S4 the census coverage (Tools/survey/map_shader_census.tsv): every active non-null vanilla slot on a mapped shader;
//      mod maps (Phase 3.7): >= 94 % of the active slots on a mapped shader or the Unreal/* input
//      heuristic, >= 96 % of the lightmapped slots on a replacement with a lightmap path or an unlit one;
//   S5 MapShaderSet lists at least one variant of every package / built-in target (the player's shader set);
//   graphics part: S6 a synthetic mirror (MapMirrors on a horizontal quad, a red quad above it): the reflection appears where
//      the mirrored point projects and the mirror region is not constant; S7 HS2Next/Hidden/NoDraw draws nothing;
//   S8 black-box probes against the Editor's Built-in shaders of the same name (Self-Illumin/Bumped Diffuse, Soft Edge
//      Unlit, Particles/Standard Unlit; Tests/FitCases/maps_*.txt; SkinFit's probe rig, Built-in vs the measurement URP asset):
//      RMS of the linear centre means, target <= 0.002 (Shaders/README.md).
// RunProbe (play mode, graphics): the bundle-only originals (Custom/Silhouette of map 35, FX/MirrorReflection_Transparent
// of map 29, KriptoFX/ME/Particle of map 0) taken from maps loaded through MapRealization (MapMaterials keeps each copy's
// bundle source), probed the same way (the pipeline switched to Built-in for the reference and back afterwards).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HS2Next;
using HS2Next.Content;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class MapShaderTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "maps");
    static string Root => ContentSettings.ProjectRoot;
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

    // ------------------------------------------------------------------ the ShaderMap block of package B

    /// <summary>The (original, target) pairs of ShaderMap's "maps (3.2 package B)" block, in file order.</summary>
    public static List<(string orig, string target)> MapEntries()
    {
        var text = File.ReadAllText(Path.Combine(Root, "Assets", "HS2Next", "Runtime", "Rendering", "ShaderMap.cs"));
        int a = text.IndexOf("---- maps (3.2 package B)", StringComparison.Ordinal), b = text.IndexOf("---- end of maps (3.2 package B)", StringComparison.Ordinal);
        var res = new List<(string, string)>();
        if (a < 0 || b < a) return res;
        foreach (Match m in Regex.Matches(text.Substring(a, b - a), "\\{\\s*\"([^\"]+)\"\\s*,\\s*\"([^\"]+)\"\\s*\\}"))
            res.Add((m.Groups[1].Value, m.Groups[2].Value));
        return res;
    }

    static readonly string[] LoopKeywords = { "_CLUSTER_LIGHT_LOOP", "_LIGHT_LAYERS", "_ADDITIONAL_LIGHT_SHADOWS" };
    // the new lit HS2Next shaders (unlit ones: Silhouette, mirrors, particles, NoDraw, Soft Edge, Slime)
    static readonly string[] LitTargets =
    {
        "HS2Next/Compatible/Water", "HS2Next/Compatible/Water Particles Foam", "HS2Next/Nature/Tree Creator Leaves", "HS2Next/Nature/Tree Creator Bark",
        "HS2Next/Effects/Ice/IceFront", "HS2Next/Standard (Specular setup)(Two Sided)", "HS2Next/Legacy Shaders/Self-Illumin/Bumped Diffuse",
    };
    // replacements whose property list is not the original's by design (approximations with generic inputs, no-draw)
    static readonly HashSet<string> PropsByDesign = new HashSet<string> { "HS2Next/Compatible/Water", "HS2Next/Hidden/NoDraw" };

    public static void Run()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"MapShaderTest (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
        try { Headless(); }
        catch (Exception e) { Check("headless part ran without an exception", false, e.ToString()); }
        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
        {
            try { Graphics(); }
            catch (Exception e) { Check("graphics part ran without an exception", false, e.ToString()); }
            finally { ShaderFit.SetPipeline(null); }
        }
        else Info("no graphics device: S6-S8 skipped");
        End("map_shader_report.txt", "MAP SHADER TEST", sw);
        EditorApplication.Exit(fail > 0 ? 1 : 0);
    }

    static void Headless()
    {
        var entries = MapEntries();
        Section($"S1 targets of ShaderMap's maps block ({entries.Count} entries)");
        Check("the block exists", entries.Count > 0);
        foreach (var target in entries.Select(e => e.target).Distinct().OrderBy(x => x, StringComparer.Ordinal))
        {
            var s = Shader.Find(target);
            int errors = s ? ShaderUtil.GetShaderMessages(s).Count(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) : -1;
            Check($"{target} resolves and compiles", s && errors == 0 && !ShaderUtil.ShaderHasError(s), s ? $"{errors} errors" : "Shader.Find failed");
        }

        Section("S2 property lists (originals from Tools/survey/map_shader_props.json)");
        var originals = ReadProps();
        Check("original property lists read", originals.Count > 0, $"{originals.Count} shaders");
        var map = entries.GroupBy(e => e.orig).ToDictionary(g => g.Key, g => g.First().target);
        foreach (var kv in originals.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (!map.TryGetValue(kv.Key, out var target)) { Info($"{kv.Key}: no entry in the maps block (mapped elsewhere)"); continue; }
            var s = Shader.Find(target);
            if (!s) { Check($"{kv.Key} -> {target}: replacement found", false); continue; }
            var diffs = ComparePropsOf(kv.Value, s);
            if (PropsByDesign.Contains(target)) Info($"{kv.Key} -> {target}: by design not the original's list ({diffs.Count} differences)");
            else Check($"{kv.Key} -> {target}: every original property with its type and default", diffs.Count == 0, string.Join("; ", diffs.Take(8)));
        }

        Section("S3 keywords");
        foreach (var t in LitTargets)
        {
            var s = Shader.Find(t);
            if (!s) { Check(t + " found", false); continue; }
            var missing = LoopKeywords.Where(k => !s.keywordSpace.FindKeyword(k).isValid).ToList();
            Check($"{t} declares {string.Join(", ", LoopKeywords)}", missing.Count == 0, "missing " + string.Join(", ", missing));
        }
        var st7 = Shader.Find("Universal Render Pipeline/Nature/SpeedTree7");
        var census = new[] { "GEOM_TYPE_BRANCH", "GEOM_TYPE_BRANCH_DETAIL", "GEOM_TYPE_FROND", "GEOM_TYPE_LEAF", "EFFECT_BUMP", "EFFECT_HUE_VARIATION" };
        Check("SpeedTree7 declares the census keywords (" + string.Join(" ", census) + ")", st7 && census.All(k => st7.keywordSpace.FindKeyword(k).isValid));
        if (st7) foreach (var k in LoopKeywords) Info($"SpeedTree7 {k}: {(st7.keywordSpace.FindKeyword(k).isValid ? "declared" : "not declared")}");

        Section("S4 census coverage (active non-null slots, Tools/survey/map_shader_census.tsv)");
        var cov = Coverage();
        foreach (var c in cov) Info(c.Value.line);
        Check("vanilla: every active non-null slot on a mapped shader", cov.TryGetValue("vanilla", out var v) && v.mapped == v.total, v.line);
        // Phase 3.7: the mod maps by shader name, the Unreal/* input heuristic counted
        var mod = ModCoverage();
        Check($"mods: active non-null slots on a mapped shader or the Unreal/* input heuristic {mod.activePct:F1} % (>= 94 %)", mod.activePct >= 94, mod.line);
        Check($"mods: lightmapped slots on a replacement with a lightmap path or an unlit one {mod.lmPct:F1} % (>= 96 %)", mod.lmPct >= 96);

        Section("S5 shader set entries (MapShaderSet)");
        var svc = new ShaderVariantCollection();
        var errs = new List<string>();
        foreach (var l in MapShaderSet.Add(svc, errs)) Info(l);
        Check("every package / built-in target listed", errs.Count == 0, string.Join("; ", errs));
        Object.DestroyImmediate(svc);
    }

    // ------------------------------------------------------------------ properties

    sealed class Prop { public string Name, Type, Tex; public float[] Def; }

    static Dictionary<string, List<Prop>> ReadProps()
    {
        var res = new Dictionary<string, List<Prop>>(StringComparer.Ordinal);
        string path = Path.Combine(Root, "Tools", "survey", "map_shader_props.json");
        if (!File.Exists(path)) return res;
        var root = MiniJson.Parse(File.ReadAllText(path)) as Dictionary<string, object>;
        if (!(root?["shaders"] is Dictionary<string, object> shaders)) return res;
        foreach (var kv in shaders)
        {
            var list = new List<Prop>();
            foreach (var po in (List<object>)((Dictionary<string, object>)kv.Value)["properties"])
            {
                var p = (Dictionary<string, object>)po;
                var pr = new Prop { Name = (string)p["name"], Type = (string)p["type"] };
                var d = p["default"];
                if (d is List<object> l) pr.Def = l.Select(x => Convert.ToSingle(x, CultureInfo.InvariantCulture)).ToArray();
                else if (d is string s) pr.Tex = s;
                else pr.Def = new[] { Convert.ToSingle(d, CultureInfo.InvariantCulture) };
                list.Add(pr);
            }
            res[kv.Key] = list;
        }
        return res;
    }

    static List<string> ComparePropsOf(List<Prop> orig, Shader s)
    {
        var diffs = new List<string>();
        foreach (var p in orig)
        {
            int i = s.FindPropertyIndex(p.Name);
            if (i < 0) { diffs.Add(p.Name + " missing"); continue; }
            var t = s.GetPropertyType(i);
            string tn = t == ShaderPropertyType.Texture ? "Texture" : t == ShaderPropertyType.Color ? "Color" : t == ShaderPropertyType.Vector ? "Vector"
                : t == ShaderPropertyType.Range ? "Range" : t == ShaderPropertyType.Int ? "Int" : "Float";
            if (tn != p.Type) { diffs.Add($"{p.Name} type {tn} (original {p.Type})"); continue; }
            if (t == ShaderPropertyType.Texture)
            {
                string def = s.GetPropertyTextureDefaultName(i) ?? "";
                if (!string.Equals(def, p.Tex ?? "", StringComparison.Ordinal)) diffs.Add($"{p.Name} default texture \"{def}\" (original \"{p.Tex}\")");
            }
            else if (t == ShaderPropertyType.Color || t == ShaderPropertyType.Vector)
            {
                var v = s.GetPropertyDefaultVectorValue(i);
                if (p.Def != null && p.Def.Length == 4 && (Mathf.Abs(v.x - p.Def[0]) > 1e-4f || Mathf.Abs(v.y - p.Def[1]) > 1e-4f || Mathf.Abs(v.z - p.Def[2]) > 1e-4f || Mathf.Abs(v.w - p.Def[3]) > 1e-4f))
                    diffs.Add($"{p.Name} default {v} (original ({string.Join(", ", p.Def.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture)))}))");
            }
            else
            {
                float v = s.GetPropertyDefaultFloatValue(i);
                if (p.Def != null && Mathf.Abs(v - p.Def[0]) > 1e-4f) diffs.Add($"{p.Name} default {v} (original {p.Def[0]})");
            }
        }
        return diffs;
    }

    // ------------------------------------------------------------------ coverage from the census TSV

    /// <summary>Mod maps (Phase 3.7 thresholds): active non-null slots on a mapped shader or an Unreal/* material (ShaderMap's
    /// input heuristic), and lightmapped slots whose replacement has a lightmap path or draws unlit (RouteLightmapped keeps
    /// both). By shader name: the bundle copies of a shared name are counted in ModShaderTest M4.</summary>
    static (double activePct, double lmPct, string line) ModCoverage()
    {
        string path = Path.Combine(Root, "Tools", "survey", "map_shader_census.tsv");
        if (!File.Exists(path)) return (0, 0, "map_shader_census.tsv missing");
        var lines = File.ReadAllLines(path);
        var head = lines[0].Split('\t');
        int cs = Array.IndexOf(head, "source"), csh = Array.IndexOf(head, "shader"), ca = Array.IndexOf(head, "active_slots"), cl = Array.IndexOf(head, "lightmapped_slots");
        var lm = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool OnPath(string sh)
        {
            string t = ShaderMap.Fitted.TryGetValue(sh, out var r) ? r : sh.StartsWith("HS2Next/", StringComparison.Ordinal) ? sh : null;
            if (t == null) return false;
            if (!lm.TryGetValue(t, out var ok)) { var s = Shader.Find(t); lm[t] = ok = ShaderMap.SupportsLightmaps(s) || ShaderMap.NeedsNoLightmap(s); }
            return ok;
        }
        double at = 0, am = 0, lt = 0, lo = 0, unreal = 0;
        for (int k = 1; k < lines.Length; k++)
        {
            var f = lines[k].Split('\t');
            if (f.Length <= Math.Max(ca, cl) || f[cs] != "mod" || f[csh].StartsWith("(")) continue;
            string sh = f[csh];
            double a = double.Parse(f[ca], CultureInfo.InvariantCulture), l = double.Parse(f[cl], CultureInfo.InvariantCulture);
            bool heur = sh.StartsWith("Unreal/", StringComparison.Ordinal);
            bool mapped = ShaderMap.Fitted.ContainsKey(sh) || sh.StartsWith("HS2Next/", StringComparison.Ordinal) || sh.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal);
            at += a; lt += l;
            if (mapped || heur) am += a;
            if (heur) unreal += a;
            if (heur || OnPath(sh)) lo += l;
        }
        double ap = 100 * am / Math.Max(1, at), lp = 100 * lo / Math.Max(1, lt);
        return (ap, lp, $"mods: {am:F0} of {at:F0} active slots ({unreal:F0} Unreal/*), {lo:F0} of {lt:F0} lightmapped slots on a lightmap path or unlit ({lp:F1} %)");
    }

    static Dictionary<string, (long total, long mapped, string line)> Coverage()
    {
        var res = new Dictionary<string, (long, long, string)>();
        string path = Path.Combine(Root, "Tools", "survey", "map_shader_census.tsv");
        if (!File.Exists(path)) return res;
        var fitted = ShaderMap.Fitted;
        var lines = File.ReadAllLines(path);
        var head = lines[0].Split('\t');
        int cs = Array.IndexOf(head, "source"), csh = Array.IndexOf(head, "shader"), ca = Array.IndexOf(head, "active_slots");
        var tot = new Dictionary<string, long>(); var map = new Dictionary<string, long>(); var top = new Dictionary<string, List<(string, long)>>();
        for (int k = 1; k < lines.Length; k++)
        {
            var f = lines[k].Split('\t');
            if (f.Length <= ca) continue;
            string src = f[cs], sh = f[csh];
            if (sh.StartsWith("(")) continue;   // null material
            long a = long.Parse(f[ca], CultureInfo.InvariantCulture);
            tot[src] = (tot.TryGetValue(src, out var t0) ? t0 : 0) + a;
            bool ok = fitted.ContainsKey(sh) || sh.StartsWith("HS2Next/", StringComparison.Ordinal) || sh.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal);
            if (ok) map[src] = (map.TryGetValue(src, out var m0) ? m0 : 0) + a;
            else
            {
                if (!top.TryGetValue(src, out var l)) top[src] = l = new List<(string, long)>();
                l.Add((sh, a));
            }
        }
        foreach (var src in tot.Keys)
        {
            long t = tot[src], m = map.TryGetValue(src, out var mm) ? mm : 0;
            var rest = top.TryGetValue(src, out var l) ? l.OrderByDescending(x => x.Item2).Take(5).Select(x => $"{x.Item1} {x.Item2}") : Enumerable.Empty<string>();
            res[src] = (t, m, $"{src}: {m} of {t} active non-null slots mapped ({100.0 * m / Math.Max(1, t):F1} %); largest unmapped: {string.Join(", ", rest)}");
        }
        return res;
    }

    // ------------------------------------------------------------------ graphics part

    static UniversalRenderPipelineAsset SpikeAsset => AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HS2Next.App.AppPipeline.MeasureAssetPath);

    static void Graphics()
    {
        ShaderUtil.allowAsyncCompilation = false;
        Section("S6 synthetic mirror (MapMirrors, measurement URP asset)");
        MirrorCase();
        Section("S7 NoDraw");
        NoDrawCase();
        Section("S8 probes against the Editor's Built-in shaders");
        Probe("Legacy Shaders/Self-Illumin/Bumped Diffuse", null, "HS2Next/Legacy Shaders/Self-Illumin/Bumped Diffuse", "maps_selfillum.txt", 0.002);
        Probe("Legacy Shaders/Transparent/Cutout/Soft Edge Unlit", null, "HS2Next/Legacy Shaders/Transparent/Cutout/Soft Edge Unlit", "maps_softedge.txt", 0.002);
        Probe("Particles/Standard Unlit", null, "HS2Next/Particles/Standard Unlit", "maps_particles_unlit.txt", 0.002);
    }

    /// <summary>SkinFit's probe rig: Built-in with <paramref name="orig"/> vs the measurement URP asset with <paramref name="repl"/>;
    /// returns the RMS of the linear centre means (or -1 when a shader is missing).</summary>
    static double Probe(string origName, Shader orig, string replName, string cases, double target, Material src = null)
    {
        orig = orig ? orig : Shader.Find(origName);
        var repl = Shader.Find(replName);
        if (!orig || !repl) { Check($"{origName}: original and replacement found", false, $"{(orig ? "" : "original ")}{(repl ? "" : "replacement")} missing"); return -1; }
        var jobs = SkinFit.ReadJobs(Path.Combine(Root, "Tests", "FitCases", cases));
        var bi = SkinFit.Eval(jobs, orig, src, null);
        var ur = SkinFit.Eval(jobs, repl, src, SpikeAsset);
        double e = 0, worst = 0; int n = 0; string worstAt = "";
        var sb = new StringBuilder();
        foreach (var k in bi.Keys)
        {
            Color a = bi[k], b = ur[k];
            double d = Math.Max(Math.Abs(a.r - b.r), Math.Max(Math.Abs(a.g - b.g), Math.Abs(a.b - b.b)));
            e += Sq(a.r - b.r) + Sq(a.g - b.g) + Sq(a.b - b.b); n += 3;
            if (d > worst) { worst = d; worstAt = $"{jobs[k.Item1].label} {jobs[k.Item1].v1[k.Item2]} {jobs[k.Item1].v2[k.Item3]} {jobs[k.Item1].lights[k.Item4].name}: {F(a)} / {F(b)}"; }
            sb.AppendLine($"      {jobs[k.Item1].label} {jobs[k.Item1].v1[k.Item2]} {jobs[k.Item1].v2[k.Item3]} {jobs[k.Item1].lights[k.Item4].name}: builtin {F(a)} urp {F(b)}");
        }
        double rms = Math.Sqrt(e / Math.Max(1, n));
        Check($"{origName} vs {replName}: RMS {rms:F4} over {n} values (target <= {target})", rms <= target, $"worst {worst:F4} at {worstAt}");
        File.AppendAllText(Path.Combine(OutDir, "map_shader_probe_cells.txt"), $"# {origName} vs {replName} ({cases})\n{sb}");
        return rms;
    }

    static double Sq(double v) => v * v;
    static string F(Color c) => $"{c.r:F3} {c.g:F3} {c.b:F3}";

    static Texture2D Read(Camera cam)
    {
        var rt = cam.targetTexture;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var t = new Texture2D(rt.width, rt.height, TextureFormat.RGBAFloat, false, true);
        t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        t.Apply();
        RenderTexture.active = prev;
        return t;
    }

    static void MirrorCase()
    {
        ShaderFit.SetPipeline(SpikeAsset);
        var objs = new List<Object>();
        MapMirrors.Handle h = null;
        try
        {
            var root = new GameObject("mirror_case");
            objs.Add(root);
            // mirror: a 4 x 4 plane at y = 0 (Unity's plane faces +y = transform.up, HS2's mirror normal), MirrorReflection on it
            var mq = GameObject.CreatePrimitive(PrimitiveType.Plane);
            Object.DestroyImmediate(mq.GetComponent<Collider>());
            mq.transform.SetParent(root.transform, false);
            mq.transform.localScale = new Vector3(0.4f, 1, 0.4f);
            var mmat = new Material(Shader.Find("HS2Next/Compatible/FX/MirrorReflection"));
            objs.Add(mmat);
            mq.GetComponent<MeshRenderer>().sharedMaterial = mmat;
            var stub = mq.AddComponent<MirrorReflection>();
            stub.m_TextureSize = 256; stub.m_ClipPlaneOffset = 0.01f; stub.m_ReflectLayers = -1;
            // a red quad above the mirror, facing down (only visible through the reflection), at x = -1
            var red = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(red.GetComponent<Collider>());
            red.transform.SetParent(root.transform, false);
            red.transform.SetPositionAndRotation(new Vector3(-1, 1.5f, 0), Quaternion.Euler(-90, 0, 0));
            var rmat = new Material(Shader.Find("HS2Next/Unlit/Color")); rmat.SetColor("_Color", Color.red); objs.Add(rmat);
            red.GetComponent<MeshRenderer>().sharedMaterial = rmat;
            // camera above and in front, looking down at the mirror
            var cam = new GameObject("mirror_cam").AddComponent<Camera>();
            objs.Add(cam.gameObject);
            cam.transform.SetPositionAndRotation(new Vector3(0, 3, -4), Quaternion.LookRotation(new Vector3(0, -3, 4)));
            cam.fieldOfView = 50; cam.nearClipPlane = 0.1f; cam.farClipPlane = 50;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1);
            cam.targetTexture = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            objs.Add(cam.targetTexture);
            cam.enabled = false;
            var s = MapStructure.Build(new[] { root });
            var mats = new MapMaterials();
            h = MapMirrors.Attach(s, mats);
            Check("one mirror attached", h != null && h.Count == 1, $"{h?.Count ?? 0}");
            int before = MapMirrors.RendersTotal;
            cam.Render(); cam.Render();
            Check("the mirror rendered its reflection from the camera's render", MapMirrors.RendersTotal - before >= 1, $"{MapMirrors.RendersTotal - before} renders");
            var img = Read(cam); objs.Add(img);
            // expected: the red quad's centre mirrored in y = 0.01 projects into the image
            var mirrored = new Vector3(-1, -1.5f + 0.02f, 0);
            var vp = cam.WorldToViewportPoint(mirrored);
            var px = img.GetPixelBilinear(vp.x, vp.y);
            var bg = img.GetPixelBilinear(0.5f, 0.98f);
            var other = img.GetPixelBilinear(cam.WorldToViewportPoint(new Vector3(1, -1.5f, 0)).x, cam.WorldToViewportPoint(new Vector3(1, -1.5f, 0)).y);
            Info($"pixel at the mirrored red centre (viewport {vp.x:F2}, {vp.y:F2}) {px}; mirrored x = +1 {other}; background {bg}");
            Check("reflection: red where the mirrored point projects, not at the opposite side", px.r > 0.5f && px.g < 0.3f && other.r < 0.5f, $"{px} / {other}");
            // not constant over the mirror's region
            float mn = 9, mx = -9;
            for (float u = -1.5f; u <= 1.5f; u += 0.25f)
                for (float w = -1.5f; w <= 1.5f; w += 0.25f)
                {
                    var p = cam.WorldToViewportPoint(new Vector3(u, 0, w));
                    var c = img.GetPixelBilinear(p.x, p.y);
                    mn = Mathf.Min(mn, c.r); mx = Mathf.Max(mx, c.r);
                }
            Check("mirror region not constant", mx - mn > 0.1f, $"red channel {mn:F3}..{mx:F3}");
            File.WriteAllBytes(Path.Combine(OutDir, "map_shader_mirror.png"), ToPng(img));
        }
        finally
        {
            h?.Dispose();
            foreach (var o in objs) if (o) Object.DestroyImmediate(o);
            ShaderFit.SetPipeline(null);
        }
    }

    static byte[] ToPng(Texture2D linear)
    {
        var t = new Texture2D(linear.width, linear.height, TextureFormat.RGB24, false);
        t.SetPixels(linear.GetPixels());
        t.Apply();
        var b = t.EncodeToPNG();
        Object.DestroyImmediate(t);
        return b;
    }

    static void NoDrawCase()
    {
        ShaderFit.SetPipeline(SpikeAsset);
        var objs = new List<Object>();
        try
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            objs.Add(q);
            q.transform.position = Vector3.zero;
            var m = new Material(Shader.Find("HS2Next/Hidden/NoDraw")); objs.Add(m);
            q.GetComponent<MeshRenderer>().sharedMaterial = m;
            var cam = new GameObject("nodraw_cam").AddComponent<Camera>(); objs.Add(cam.gameObject);
            cam.transform.SetPositionAndRotation(new Vector3(0, 0, -2), Quaternion.identity);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.25f, 0.5f, 0.75f, 1);
            cam.targetTexture = new RenderTexture(32, 32, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); objs.Add(cam.targetTexture);
            cam.enabled = false;
            cam.Render(); cam.Render();
            var img = Read(cam); objs.Add(img);
            var bg = img.GetPixel(0, 0);   // a corner: the background as read back (the target is sRGB)
            float d = img.GetPixels().Max(c => Mathf.Abs(c.r - bg.r) + Mathf.Abs(c.g - bg.g) + Mathf.Abs(c.b - bg.b));
            Check("a quad with HS2Next/Hidden/NoDraw leaves the background unchanged", d < 0.02f, $"max |d| {d:F4}");
        }
        finally
        {
            foreach (var o in objs) if (o) Object.DestroyImmediate(o);
            ShaderFit.SetPipeline(null);
        }
    }

    /// <summary>Minimal JSON reader: objects -> Dictionary, arrays -> List, numbers -> double.</summary>
    static class MiniJson
    {
        public static object Parse(string s) { int i = 0; return Value(s, ref i); }
        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(StringComparer.Ordinal); i++; Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i); string k = Str(s, ref i); Ws(s, ref i); i++;
                    d[k] = Value(s, ref i); Ws(s, ref i);
                    if (s[i++] == '}') return d;
                }
            }
            if (c == '[')
            {
                var l = new List<object>(); i++; Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true) { l.Add(Value(s, ref i)); Ws(s, ref i); if (s[i++] == ']') return l; }
            }
            if (c == '"') return Str(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder(); i++;
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            i++;
            return sb.ToString();
        }
    }

    // ------------------------------------------------------------------ play-mode probe of bundle-only originals

    static string ProbeReport => Path.Combine(OutDir, "map_shader_probe.txt");

    public static void RunProbe()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(ProbeReport, "SUMMARY: 0 PASS, 1 FAIL\nMapShaderTest.probe: started, no result yet\n", new UTF8Encoding(false));
        PlayModeHarness.Start(nameof(MapShaderTest) + "." + nameof(ProbeMain), ProbeReport, 30);
    }

    static IEnumerator ProbeMain()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"MapShaderTest.probe (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
        File.WriteAllText(Path.Combine(OutDir, "map_shader_probe_cells.txt"), "");
        var gd = CardResolveTest.BuildDb(null);
        var db = gd.LoadStudioLists(new HS2Next.Content.Studio.StudioLoadOptions { EmulateAIInstallGate = false });
        var findings = new SceneFindings();
        var map = new MapRealization(gd, db, findings, null);
        var prevPipe = GraphicsSettings.defaultRenderPipeline;
        bool[] ok = { false };
        var cases = new[]
        {
            ("illusion.hs2#35", "Custom/Silhouette", "HS2Next/Custom/Silhouette", "maps_silhouette.txt"),
            ("illusion.hs2#29", "FX/MirrorReflection_Transparent", "HS2Next/Compatible/FX/MirrorReflection_Transparent", "maps_mirror.txt"),
            ("illusion.hs2#0", "KriptoFX/ME/Particle", "HS2Next/Compatible/KriptoFX/ME/Particle", "maps_kriptofx.txt"),
        };
        foreach (var (mref, origName, replName, file) in cases)
        {
            Section($"{origName} ({mref})");
            map.SetFlags(true, true);
            map.SetTarget(mref, HS2Next.Save.Vec3.Zero, HS2Next.Save.Vec3.Zero);
            yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return !map.IsBusy; }, 300, ok);
            if (map.State != MapState.Loaded) { Check(mref + " loaded", false, map.State.ToString()); continue; }
            Material src = null;
            foreach (var r in map.Structure.Renderers)
            {
                if (!r) continue;
                foreach (var m in r.sharedMaterials)
                {
                    var o = map.Materials.OriginOf(m);
                    if (o != null && o.ShaderOriginal == origName && o.Source)
                    {
                        // KriptoFX: prefer a source without Clip_ON (the clip threshold makes the probe a step function)
                        if (src == null || origName.StartsWith("KriptoFX") && !o.Keywords.Contains("Clip_ON")) src = o.Source;
                    }
                }
            }
            Check($"a {origName} source material in the map", src != null);
            if (src == null) continue;
            Info($"source {src.name}: keywords [{string.Join(" ", MaterialCopies.KeywordsOf(src))}], queue {src.renderQueue}");
            double rms = -1;
            try { rms = Probe(origName, src.shader, replName, file, 0.002, src); }
            catch (Exception e) { Check(origName + " probe ran", false, e.ToString()); }
            finally { ShaderFit.SetPipeline(prevPipe); }
            yield return null;
        }
        map.SetTarget(null, HS2Next.Save.Vec3.Zero, HS2Next.Save.Vec3.Zero);
        yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return !map.IsBusy && map.PendingUnloads == 0; }, 60, ok);
        try { gd.UnloadAll(); } catch (Exception) { }
        ShaderFit.SetPipeline(prevPipe);
        End("map_shader_probe.txt", "MAP SHADER PROBE", sw);
        PlayModeHarness.ExitCode = fail > 0 ? 1 : 0;
    }
}
