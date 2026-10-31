// ModShaderTest (Phase 3.7; written by package F3, entries of F1 / F2 / L from their notes and
// fit-case files; the substitute-shader tail of Phase 5.2, package S, checks ST3): the mod
// shaders mapped in the "3.7" and "5.2" blocks of ShaderMap.Fitted.
// Run (edit mode; the graphics part runs when a device exists):
//   & Tools\unity_batch.ps1 -Method ModShaderTest.Run [-NoGraphics]        -> Spike/out/modshaders/mod_shader_report.txt
//   -mstOnly "M1,M5" runs only those checks; -mstFit "<substring>" limits M5 to the fit files whose name contains it.
// Headless part:
//   M1 every target of the "// ---- 3.7 <ID>" and "// ---- 5.2 <ID>" blocks of ShaderMap.Fitted, every ShaderMap.CopyRoutes
//      target and the Fitted target of every ShaderMap.CopyAccepts name resolves (Shader.Find) and compiles without errors;
//   M2 property names / types / defaults / [HDR] / [Gamma] of each replacement = the original's main copy in
//      Tools/survey/mod_shader_props.json (Range and Float are the same uniform: the range is an inspector hint); a shader
//      introduced by 3.7 or 5.2 may declare nothing else (README: extra properties come through as garbage; hidden placeholders
//      of a family's shared constant buffer, described "Unused (shared buffer)", are not inputs); a replacement that
//      already existed (an alias target such as HS2Next/Standard) is checked for the original's properties only; a 5.2 router
//      entry (blocks Sb, Sc) whose target already existed reads only the inputs both declare: those must agree in type and
//      flags ([HDR], [Gamma]), the original's other properties are listed as not drawn;
//      A CopyRoutes target is compared with the bundle copies it serves, not with the main copy: the json's `copies`
//      signatures that TryResolve's order gives to it (each copy's property names evaluated on an in-memory stand-in shader)
//      and the real copies of the fit manifests that name it (loaded from their bundles, full type / default / flag check);
//      it may declare only properties of the copies it serves (a target that serves several copies declares their union);
//   M3 the 3.7 and 5.2 replacements (with the CopyRoutes targets) declare in their lit forward passes the light-loop keywords (_CLUSTER_LIGHT_LOOP, _LIGHT_LAYERS,
//      _ADDITIONAL_LIGHT_SHADOWS) and the probe keywords as multi_compile_fragment (not dynamic branches: stripping),
//      opaque / cutout replacements have MotionVectors and DepthNormals passes, replacements that can sit above queue 2500
//      declare _RECEIVE_SHADOWS_OFF (or are always transparent and receive no shadows by construction);
//   M4 coverage from the committed census (Tools/survey/mod_shader_census.tsv, re-read, not re-scanned) with the current
//      ShaderMap and the Unreal heuristic thresholds; the names with CopyRoutes / CopyAccepts rules count by
//      bundle copy (Tools/survey/mod_shader_census_copies.tsv: the replacement each slot's own copy resolved to when the census
//      ran; the by-name number is reported beside it); lightmapped slots count on a lightmap path or an unlit replacement;
//   M6 the Unreal heuristic (ShaderMap.UnrealInputs) on the junk-yard pack: after ApplyUrp every Unreal material that has a
//      colour texture (a *_D / *_BaseColor / ... texture) draws it as _BaseMap; the normal map as _BumpMap;
//   M7 the lightmap routing of package L (LightmapPaths.M7, called by reflection; reported as pending until it exists);
//   M8 the 5.2 routes per bundle copy: every copy of a 5.2 entry's original in mod_shader_props.json (its property names on an
//      in-memory stand-in shader) resolves through ShaderMap.TryResolve; the main copy must take the entry's target, the
//      route of every other copy is reported (a copy without the router's inputs keeps the fallback);
//   M9 MaterialEditor parity: a shader name ShaderMap replaces that neither the plugin nor a loaded manifest declares is
//      skipped (ResolveShader null; no card of the install uses one); a name the plugin declares itself resolves; a swap to
//      the plugin's own copy keeps only the keywords that copy declares (a material with keywords outside them, swapped as
//      the applier does); the plugin's copies table names only shaders it carries.
//   M10 the compatible names: every shader under Assets/HS2Next/Shaders named HS2Next/Compatible/… is in exactly one family of
//      ShaderMap.CompatibleFamilies and a replacement target; every family has at least one; no plain HS2Next/ name
//      belongs to a family.
// Graphics part:
//   M5 probe regressions: every Tests/FitCases/*_fit.txt with a manifest line
//        # fit: shader="<original>" repl="<replacement>" item="<bundle:prefab or id:N>" [material="<name>"] rms=<max> [origFind]
//      is run through SkinFit.Sweep (-fitPipe both, Built-in vs the measurement URP asset); total RMS <= the manifest's rms.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HS2Next;
using HS2Next.Content;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class ModShaderTest
{
    static string Root => ContentSettings.ProjectRoot;
    static string OutDir => Path.Combine(Root, "Spike", "out", "modshaders");
    static StringBuilder rep;
    static int pass, fail;
    static HashSet<string> only;

    static void Begin() { rep = new StringBuilder(); pass = fail = 0; }
    static void Section(string s) => rep.AppendLine("\n== " + s);
    static void Info(string s) => rep.AppendLine("   " + s);
    static void Check(string what, bool ok, string detail = "")
    {
        if (ok) pass++; else fail++;
        rep.AppendLine($"   {(ok ? "PASS" : "FAIL")} {what}{(string.IsNullOrEmpty(detail) ? "" : " - " + detail)}");
    }
    static string Arg(string name, string def)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }
    static bool Want(string id) => only == null || only.Contains(id);

    public static void Run()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        only = Arg("-mstOnly", null)?.Split(',').Select(x => x.Trim()).ToHashSet();
        rep.AppendLine($"ModShaderTest (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
        void Step(string id, Action a)
        {
            if (!Want(id)) return;
            try { a(); }
            catch (Exception e) { Check($"{id} ran without an exception", false, e.ToString()); }
        }
        // M7's probes and M5 switch the render pipeline; the project's own comes back in every case (a run without a
        // graphics device skips M5, whose finally resets it), so the Editor never saves the measurement asset into
        // GraphicsSettings / QualitySettings at exit
        var prevDefault = GraphicsSettings.defaultRenderPipeline;
        var prevQuality = QualitySettings.renderPipeline;
        try
        {
            var entries = Entries();
            Step("M1", () => M1(entries));
            Step("M2", () => M2(entries));
            Step("M3", () => M3(entries));
            Step("M4", M4);
            Step("M6", M6);
            Step("M7", M7);
            Step("M8", () => M8(entries));
            Step("M9", M9);
            Step("M10", M10);
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) Step("M5", M5);
            else if (Want("M5")) Info("no graphics device: M5 skipped");
        }
        finally
        {
            GraphicsSettings.defaultRenderPipeline = prevDefault;
            QualitySettings.renderPipeline = prevQuality;
        }
        Directory.CreateDirectory(OutDir);
        rep.Insert(0, $"SUMMARY: {pass} PASS, {fail} FAIL\nMOD SHADER TEST ({sw.Elapsed.TotalSeconds:F0} s)\n");
        string file = Path.Combine(OutDir, "mod_shader_report.txt");
        File.WriteAllText(file, rep.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        Debug.Log($"MOD SHADER TEST: {pass} PASS, {fail} FAIL -> {file}");
        EditorApplication.Exit(fail > 0 ? 1 : 0);
    }

    // ------------------------------------------------------------------ the 3.7 and 5.2 blocks of ShaderMap.Fitted

    public sealed class Entry
    {
        public string Phase, Block, Orig, Target;
        /// <summary>A 5.2 router entry (Standard-like or particle router): an approximation that reads only the inputs it shares
        /// with the original.</summary>
        public bool Router => Phase == "5.2" && (Block == "Sb" || Block == "Sc");
    }

    /// <summary>Every replacement the 3.7 and 5.2 mappings can give: their Fitted blocks, the CopyRoutes targets and the Fitted
    /// targets of the CopyAccepts names (the latter normally already in a block).</summary>
    static List<string> AllTargets(List<Entry> entries) => entries.Select(e => e.Target)
        .Concat(ShaderMap.CopyRoutes.Values.SelectMany(l => l.Select(x => x.replacement)))
        .Concat(ShaderMap.CopyAccepts.Keys.Where(ShaderMap.Fitted.ContainsKey).Select(k => ShaderMap.Fitted[k]))
        .Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();

    /// <summary>The (phase, block, original, target) entries of every "// ---- 3.7 &lt;ID&gt;" and "// ---- 5.2 &lt;ID&gt;" block of
    /// ShaderMap.Fitted, in file order.</summary>
    public static List<Entry> Entries()
    {
        var text = File.ReadAllText(Path.Combine(Root, "Assets", "HS2Next", "Runtime", "Rendering", "ShaderMap.cs"));
        int start = text.IndexOf("Fitted =", StringComparison.Ordinal), end = start < 0 ? -1 : text.IndexOf("\n        };", start, StringComparison.Ordinal);
        if (start >= 0 && end > start) text = text.Substring(start, end - start);
        var res = new List<Entry>();
        foreach (Match b in Regex.Matches(text, @"// ---- (3\.7|5\.2) (\w+)[^\n]*\n(.*?)// ---- end of \1 \2", RegexOptions.Singleline))
            foreach (Match m in Regex.Matches(b.Groups[3].Value, "\\{\\s*\"([^\"]+)\"\\s*,\\s*\"([^\"]+)\"\\s*\\}"))
                res.Add(new Entry { Phase = b.Groups[1].Value, Block = b.Groups[2].Value, Orig = m.Groups[1].Value, Target = m.Groups[2].Value });
        return res;
    }

    /// <summary>Targets that also serve originals mapped outside the blocks (alias targets: their own lists are checked elsewhere).</summary>
    static HashSet<string> PreexistingTargets(List<Entry> entries)
    {
        var mine = new HashSet<string>(entries.Select(e => e.Orig), StringComparer.Ordinal);
        return new HashSet<string>(ShaderMap.Fitted.Where(kv => !mine.Contains(kv.Key)).Select(kv => kv.Value), StringComparer.Ordinal);
    }

    static void M1(List<Entry> entries)
    {
        int routes = ShaderMap.CopyRoutes.Values.Sum(l => l.Count);
        Section($"M1 targets of the 3.7 and 5.2 blocks ({entries.Count} entries in blocks {string.Join(", ", entries.Select(e => e.Phase + " " + e.Block).Distinct())}), "
            + $"{routes} CopyRoutes, {ShaderMap.CopyAccepts.Count} CopyAccepts names");
        Check("the 3.7 and 5.2 blocks exist", entries.Count > 0 && entries.Any(e => e.Phase == "5.2"));
        foreach (var target in AllTargets(entries))
        {
            var s = Shader.Find(target);
            int errors = s ? ShaderUtil.GetShaderMessages(s).Count(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) : -1;
            Check($"{target} resolves and compiles", s && errors == 0 && !ShaderUtil.ShaderHasError(s), s ? $"{errors} errors" : "Shader.Find failed");
        }
    }

    // ------------------------------------------------------------------ M2 properties

    sealed class Prop { public string Name, Type, Tex; public float[] Def; public bool Hdr, Gamma; }

    static Dictionary<string, object> propsJson;
    static Dictionary<string, object> PropsJson()
    {
        if (propsJson != null) return propsJson;
        string path = Path.Combine(Root, "Tools", "survey", "mod_shader_props.json");
        propsJson = File.Exists(path) ? MiniJson.Parse(File.ReadAllText(path)) as Dictionary<string, object> : null;
        return propsJson;
    }

    static List<Prop> OriginalProps(string shader)
    {
        if (!(PropsJson()?["shaders"] is Dictionary<string, object> shaders) || !shaders.TryGetValue(shader, out var so)) return null;
        var list = new List<Prop>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var po in (List<object>)((Dictionary<string, object>)so)["properties"])
        {
            var p = (Dictionary<string, object>)po;
            var pr = new Prop { Name = (string)p["name"], Type = (string)p["type"] };
            if (!seen.Add(pr.Name)) continue;   // the parsed lists of some originals repeat a name (UBER)
            var flags = ((List<object>)p["flags"]).Cast<string>().ToList();
            pr.Hdr = flags.Contains("HDR"); pr.Gamma = flags.Contains("Gamma");
            var d = p["default"];
            if (d is List<object> l) pr.Def = l.Select(x => Convert.ToSingle(x, CultureInfo.InvariantCulture)).ToArray();
            else if (d is string s) pr.Tex = s;
            else if (d != null) pr.Def = new[] { Convert.ToSingle(d, CultureInfo.InvariantCulture) };
            list.Add(pr);
        }
        return list;
    }

    static string TypeName(ShaderPropertyType t) => t == ShaderPropertyType.Texture ? "Texture" : t == ShaderPropertyType.Color ? "Color"
        : t == ShaderPropertyType.Vector ? "Vector" : t == ShaderPropertyType.Int ? "Int" : "Float";   // Range = Float (same uniform)

    // tol: the default-value tolerance (an alias onto an existing replacement compares with 1e-3: the defaults of hand-written
    // replacements are rounded to three decimals, and a material carries its own saved value of every property anyway)
    static List<string> CompareProps(List<Prop> orig, Shader s, bool exact, float tol = 1e-4f)
    {
        var diffs = new List<string>();
        foreach (var p in orig)
        {
            int i = s.FindPropertyIndex(p.Name);
            if (i < 0) { diffs.Add(p.Name + " missing"); continue; }
            var t = s.GetPropertyType(i);
            string tn = TypeName(t), on = p.Type == "Range" ? "Float" : p.Type;
            if (tn != on) { diffs.Add($"{p.Name} type {t} (original {p.Type})"); continue; }
            var fl = s.GetPropertyFlags(i);
            if (((fl & ShaderPropertyFlags.HDR) != 0) != p.Hdr) diffs.Add($"{p.Name} [HDR] {(p.Hdr ? "missing" : "extra")}");
            if (((fl & ShaderPropertyFlags.Gamma) != 0) != p.Gamma) diffs.Add($"{p.Name} [Gamma] {(p.Gamma ? "missing" : "extra")}");
            if (t == ShaderPropertyType.Texture)
            {
                string def = s.GetPropertyTextureDefaultName(i) ?? "", od = p.Tex ?? "";
                if (od == "gray") od = "grey";
                if (def == "gray") def = "grey";
                if (!string.Equals(def, od, StringComparison.Ordinal)) diffs.Add($"{p.Name} default texture \"{def}\" (original \"{p.Tex}\")");
            }
            else if (t == ShaderPropertyType.Color || t == ShaderPropertyType.Vector)
            {
                var v = s.GetPropertyDefaultVectorValue(i);
                if (p.Def != null && p.Def.Length == 4 && (Mathf.Abs(v.x - p.Def[0]) > tol || Mathf.Abs(v.y - p.Def[1]) > tol || Mathf.Abs(v.z - p.Def[2]) > tol || Mathf.Abs(v.w - p.Def[3]) > tol))
                    diffs.Add($"{p.Name} default {v} (original ({string.Join(", ", p.Def.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture)))}))");
            }
            else
            {
                float v = t == ShaderPropertyType.Int ? s.GetPropertyDefaultIntValue(i) : s.GetPropertyDefaultFloatValue(i);
                if (p.Def != null && Mathf.Abs(v - p.Def[0]) > tol) diffs.Add($"{p.Name} default {v} (original {p.Def[0]})");
            }
        }
        if (exact)
        {
            var names = new HashSet<string>(orig.Select(p => p.Name), StringComparer.Ordinal);
            for (int i = 0; i < s.GetPropertyCount(); i++)
                // a hidden placeholder of the family's shared constant buffer (described "Unused (shared buffer)") is no input, nor is
                // the per-material fog-mode stamp of the transparent shaders (set from the original's fog variants when a material is
                // switched, never read from a card or bundle)
                if (!names.Contains(s.GetPropertyName(i)) && s.GetPropertyDescription(i) != "Unused (shared buffer)" && s.GetPropertyName(i) != "_HS2FogModes")
                    diffs.Add(s.GetPropertyName(i) + " extra");
        }
        return diffs;
    }

    static void M2(List<Entry> entries)
    {
        Section("M2 property lists (originals: the main copies in Tools/survey/mod_shader_props.json)");
        Check("Tools/survey/mod_shader_props.json read", PropsJson() != null);
        var pre = PreexistingTargets(entries);
        // replacements whose property list is not the original's by design (generic-input approximations)
        var byDesign = new HashSet<string>(StringComparer.Ordinal) { "HS2Next/Compatible/Water" };
        foreach (var e in entries)
        {
            var orig = OriginalProps(e.Orig);
            if (orig == null) { Info($"{e.Orig}: no parsed object in mod_shader_props.json (not checked)"); continue; }
            var s = Shader.Find(e.Target);
            if (!s) { Check($"{e.Orig} -> {e.Target}: replacement found", false); continue; }
            if (byDesign.Contains(e.Target)) { Info($"{e.Orig} -> {e.Target}: generic inputs filled at load (by design not the original's list)"); continue; }
            if (e.Router && pre.Contains(e.Target)) { RouterParity(e, orig, s); continue; }
            bool exact = !pre.Contains(e.Target);
            var diffs = CompareProps(orig, s, exact, exact ? 1e-4f : 1e-3f);
            Check($"{e.Orig} -> {e.Target}: {(exact ? "the original's property list" : "every original property (alias of an existing replacement)")}",
                diffs.Count == 0, $"{diffs.Count} differences: " + string.Join("; ", diffs.Take(10)));
        }
        M2Routes();
    }

    /// <summary>A 5.2 router entry onto an existing replacement: the properties both declare agree in type and flags (the
    /// replacement reads them as the original wrote them); the original's other properties are not drawn (listed); the
    /// replacement's properties the original lacks keep the replacement's defaults (listed).</summary>
    static void RouterParity(Entry e, List<Prop> orig, Shader s)
    {
        var shared = orig.Where(p => s.FindPropertyIndex(p.Name) >= 0).ToList();
        var diffs = CompareProps(shared, s, false).Where(d => !d.Contains(" default ")).ToList();
        var defaults = CompareProps(shared, s, false).Where(d => d.Contains(" default ")).ToList();
        var names = new HashSet<string>(orig.Select(p => p.Name), StringComparer.Ordinal);
        var own = Enumerable.Range(0, s.GetPropertyCount()).Select(s.GetPropertyName).Where(n => !names.Contains(n)).ToList();
        Check($"{e.Orig} -> {e.Target} (5.2 {e.Block} router): the {shared.Count} inputs both declare agree in type and flags",
            shared.Count > 0 && diffs.Count == 0, $"{diffs.Count} differences: " + string.Join("; ", diffs.Take(10)));
        Info($"{e.Orig}: not drawn by the router ({orig.Count - shared.Count}): {string.Join(", ", orig.Where(p => s.FindPropertyIndex(p.Name) < 0).Select(p => p.Name).Take(40))}");
        if (own.Count > 0) Info($"{e.Orig}: the replacement's own inputs at their defaults ({own.Count}): {string.Join(", ", own.Take(30))}");
        if (defaults.Count > 0) Info($"{e.Orig}: shared inputs with another default (the material's own value is used): {string.Join("; ", defaults.Take(10))}");
    }

    // ------------------------------------------------------------------ M2 for the CopyRoutes targets (the copies they serve)

    sealed class Served { public string Label; public HashSet<string> Names; public List<Prop> Props; }

    // more real copies a CopyRoutes target serves, beside the items of the fit manifests that name it (bundle:prefab specs):
    static readonly (string target, string shader, string item)[] RouteRefs =
    {
    };

    // Cases that name a mod not distributed with these tests (the RouteRefs entry above and three '# fit:' lines of the fit
    // cases) sit in blocks the exported tree leaves out; this array has one element where those cases are present.
    static readonly bool[] LocalModCases =
    {
    };

    // Route targets one of whose served copies only those cases name. Without them a target serves no copy, or its union of
    // served copies lacks that copy's properties: M2 reports this as INFO instead of a failure; every other difference counts.
    static readonly HashSet<string> LocalModTargets = new HashSet<string>(StringComparer.Ordinal)
    {
        "HS2Next/Compatible/Hanmen/Clothes True Alpha (v2 copy)", "HS2Next/Compatible/Hanmen/Item Cutoff (v0.9 copies)",
    };

    static readonly Regex Identifier = new Regex("^[A-Za-z_][A-Za-z0-9_]*$");

    /// <summary>An in-memory shader that declares exactly these property names (as floats): ShaderMap's copy tests only ask
    /// whether a property is declared, so TryResolve on it gives the replacement of the bundle copy with that property list.</summary>
    static Shader StandIn(IEnumerable<string> names)
    {
        var sb = new StringBuilder("Shader \"Hidden/HS2NextModShaderTestStandIn\" {\nProperties {\n");
        foreach (var n in names) if (Identifier.IsMatch(n)) sb.Append(n).Append(" (\"").Append(n).Append("\", Float) = 0\n");
        sb.Append("}\nSubShader { Pass {\nHLSLPROGRAM\n#pragma vertex v\n#pragma fragment f\n")
          .Append("float4 v(float4 p : POSITION) : SV_POSITION { return p; }\nfloat4 f() : SV_Target { return 0; }\nENDHLSL\n} }\n}\n");
        return ShaderUtil.CreateShaderAsset(sb.ToString(), false);
    }

    /// <summary>The json's bundle copies of a name: (signature, property names, full parsed list for the main copy only).</summary>
    static List<(string sig, int materials, HashSet<string> names, List<Prop> props)> JsonCopies(string name)
    {
        var res = new List<(string, int, HashSet<string>, List<Prop>)>();
        var main = OriginalProps(name);
        if (main == null) return res;
        var mainNames = new HashSet<string>(main.Select(p => p.Name), StringComparer.Ordinal);
        var shaders = (Dictionary<string, object>)PropsJson()["shaders"];
        string mainSig = ((Dictionary<string, object>)shaders[name]).TryGetValue("signature", out var ms) ? (string)ms : "main";
        if (PropsJson()["copies"] is Dictionary<string, object> copies && copies.TryGetValue(name, out var co) && co is List<object> list && list.Count > 0)
            foreach (Dictionary<string, object> c in list)
            {
                var vs = (Dictionary<string, object>)c["vsMain"];
                var names = new HashSet<string>(mainNames, StringComparer.Ordinal);
                foreach (string x in (List<object>)vs["onlyThere"]) names.Remove(x);
                foreach (string x in (List<object>)vs["onlyHere"]) names.Add(x);
                string sig = (string)c["signature"];
                res.Add((sig, Convert.ToInt32(c["materials"], CultureInfo.InvariantCulture), names, sig == mainSig ? main : null));
            }
        else res.Add((mainSig, 0, mainNames, main));
        return res;
    }

    /// <summary>The property list of a loaded original (a bundle copy), in the json's form.</summary>
    static List<Prop> PropsOf(Shader s)
    {
        var list = new List<Prop>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < s.GetPropertyCount(); i++)
        {
            var t = s.GetPropertyType(i);
            var p = new Prop { Name = s.GetPropertyName(i), Type = t == ShaderPropertyType.Range ? "Range" : TypeName(t) };
            if (!seen.Add(p.Name)) continue;
            var fl = s.GetPropertyFlags(i);
            p.Hdr = (fl & ShaderPropertyFlags.HDR) != 0; p.Gamma = (fl & ShaderPropertyFlags.Gamma) != 0;
            if (t == ShaderPropertyType.Texture) p.Tex = s.GetPropertyTextureDefaultName(i) ?? "";
            else if (t == ShaderPropertyType.Color || t == ShaderPropertyType.Vector) { var v = s.GetPropertyDefaultVectorValue(i); p.Def = new[] { v.x, v.y, v.z, v.w }; }
            else if (t == ShaderPropertyType.Int) p.Def = new float[] { s.GetPropertyDefaultIntValue(i) };
            else p.Def = new[] { s.GetPropertyDefaultFloatValue(i) };
            list.Add(p);
        }
        return list;
    }

    static GameObject LoadItem(ContentCatalog gd, string spec)
    {
        int k = spec.LastIndexOf(':');
        if (k <= 0) return null;
        string bundle = spec.Substring(0, k), name = spec.Substring(k + 1);
        var pf = gd.LoadAsset<GameObject>(bundle, name);
        if (!pf) pf = gd.LoadAssetAt<GameObject>(null, bundle, null, name);   // a bundle only a zipmod ships (as SkinFit.LoadItem)
        return pf ? Object.Instantiate(pf) : null;
    }

    static void M2Routes()
    {
        Section("M2 CopyRoutes targets against the bundle copies they serve (json copies by TryResolve's order; real copies of the fit manifests)");
        var routeTargets = new HashSet<string>(ShaderMap.CopyRoutes.Values.SelectMany(l => l.Select(x => x.replacement)), StringComparer.Ordinal);
        var served = routeTargets.ToDictionary(t => t, t => new List<Served>(), StringComparer.Ordinal);
        // 1. the json copies of every name that can reach a route target (route names, names whose Fitted target is one)
        var names = new HashSet<string>(ShaderMap.CopyRoutes.Keys, StringComparer.Ordinal);
        foreach (var kv in ShaderMap.Fitted) if (routeTargets.Contains(kv.Value)) names.Add(kv.Key);
        foreach (var name in names.OrderBy(x => x, StringComparer.Ordinal))
        {
            var copies = JsonCopies(name);
            if (copies.Count == 0) { Info($"{name}: no parsed object in mod_shader_props.json"); continue; }
            var assign = new List<string>();
            foreach (var (sig, mats, cn, props) in copies)
            {
                var si = StandIn(cn);
                string target = null;
                try { if (!ShaderMap.TryResolve(name, si, out target)) target = null; }
                finally { Object.DestroyImmediate(si); }
                assign.Add($"{sig} ({cn.Count} props, {mats} mats) -> {target ?? "fallback"}");
                if (target != null && served.TryGetValue(target, out var sl))
                    sl.Add(new Served { Label = $"{name} json {sig}", Names = cn, Props = props });
            }
            Info($"{name}: " + string.Join("; ", assign));
        }
        // 2. real copies: the items of the fit manifests that name a route target (and RouteRefs), loaded from their bundles
        var refs = new List<(string target, string shader, string item)>(RouteRefs);
        foreach (var f in Directory.GetFiles(Path.Combine(Root, "Tests", "FitCases"), "*_fit.txt").OrderBy(x => x, StringComparer.Ordinal))
            foreach (var line in File.ReadLines(f).TakeWhile(l => l.StartsWith("#")))
            {
                var mm = Manifest.Match(line);
                if (!mm.Success) continue;
                var kv = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (Match k in KeyValue.Matches(mm.Groups[1].Value))
                    if (k.Groups[1].Success) kv[k.Groups[1].Value] = k.Groups[2].Value.Trim('"');
                if (kv.TryGetValue("repl", out var repl) && routeTargets.Contains(repl) && kv.TryGetValue("shader", out var sh) && kv.TryGetValue("item", out var item))
                    refs.Add((repl, sh, item));
            }
        var gd = CardResolveTest.BuildDb(null);
        try
        {
            foreach (var g in refs.Distinct().GroupBy(r => r.item))
            {
                var go = LoadItem(gd, g.Key);
                if (!go) { Info("not found: " + g.Key); continue; }
                try
                {
                    var shaders = go.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m && m.shader).Select(m => m.shader).Distinct().ToList();
                    foreach (var (target, shader, item) in g)
                    {
                        int found = 0;
                        foreach (var s in shaders.Where(s => s.name == shader))
                        {
                            if (!ShaderMap.TryResolve(s, out var r) || r != target) continue;
                            var props = PropsOf(s);
                            var cn = new HashSet<string>(props.Select(p => p.Name), StringComparer.Ordinal);
                            if (served[target].Any(x => x.Props != null && x.Names.SetEquals(cn) && x.Label.Contains(" real "))) { found++; continue; }
                            served[target].Add(new Served { Label = $"{shader} real {cn.Count}-property copy ({item.Substring(item.LastIndexOf(':') + 1)})", Names = cn, Props = props });
                            found++;
                        }
                        if (found == 0) Info($"{item}: no {shader} material that resolves to {target}");
                    }
                }
                finally { Object.DestroyImmediate(go); }
            }
        }
        finally { gd.UnloadAll(); }
        // 3. per target: every served copy's properties declared alike, nothing outside the union of the served copies
        foreach (var target in routeTargets.OrderBy(x => x, StringComparer.Ordinal))
        {
            var s = Shader.Find(target);
            if (!s) { Check($"{target}: replacement found", false); continue; }
            var sl = served[target];
            bool partial = LocalModCases.Length == 0 && LocalModTargets.Contains(target);   // a served copy is named only by a case left out
            if (sl.Count == 0)
            {
                if (partial) Info($"{target}: no served copy in this tree (the case that names one is not distributed)");
                else Check($"{target}: serves at least one known bundle copy", false, "no json copy and no real copy resolves to it");
                continue;
            }
            var diffs = new List<string>();
            // a property whose type / default / flags the served copies themselves disagree on cannot match every copy: it
            // passes when the replacement equals one of them (reported); a material keeps its own saved value of such a property
            var full = sl.Where(c => c.Props != null).Select(c => (c, d: CompareProps(c.Props, s, false))).ToList();
            var matches = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (c, d) in full)
            {
                var bad = new HashSet<string>(d.Select(x => x.Split(' ')[0]), StringComparer.Ordinal);
                foreach (var p in c.Props) if (!bad.Contains(p.Name)) matches.Add(p.Name);
            }
            foreach (var c in sl)
            {
                if (c.Props == null) { diffs.AddRange(c.Names.Where(n => s.FindPropertyIndex(n) < 0).Select(n => $"[{c.Label}] {n} missing")); continue; }
                foreach (var d in full.First(x => x.c == c).d)
                {
                    string p = d.Split(' ')[0];
                    if (!d.EndsWith(" missing", StringComparison.Ordinal) && matches.Contains(p)) Info($"{target}: the served copies disagree on {p}; the replacement follows another copy ([{c.Label}] {d})");
                    else diffs.Add($"[{c.Label}] {d}");
                }
            }
            var union = new HashSet<string>(sl.SelectMany(c => c.Names), StringComparer.Ordinal);
            var extra = new List<string>();
            for (int i = 0; i < s.GetPropertyCount(); i++)
                if (!union.Contains(s.GetPropertyName(i)) && s.GetPropertyName(i) != "_HS2FogModes") extra.Add(s.GetPropertyName(i) + " extra (in no served copy)");   // the fog-mode stamp: no input
            if (partial && extra.Count > 0)
                Info($"{target}: {extra.Count} properties in no served copy of this tree (the copy that declares them is named by a case that is not distributed): " +
                    string.Join(", ", extra.Select(x => x.Split(' ')[0])));
            else diffs.AddRange(extra);
            Check($"{target}: the property lists of the copies it serves ({string.Join(", ", sl.Select(c => c.Label))})",
                diffs.Count == 0, $"{diffs.Count} differences: " + string.Join("; ", diffs.Take(40)));
        }
    }

    // ------------------------------------------------------------------ M3 keywords and passes

    static readonly string[] LoopKeywords = { "_CLUSTER_LIGHT_LOOP", "_LIGHT_LAYERS", "_ADDITIONAL_LIGHT_SHADOWS" };
    static readonly string[] ProbeKeywords = { "_REFLECTION_PROBE_BLENDING", "_REFLECTION_PROBE_BOX_PROJECTION" };

    static List<string> LightModes(Shader s)
    {
        var res = new List<string>();
        for (int p = 0; p < s.GetPassCountInSubshader(0); p++) res.Add(s.FindPassTagValue(0, p, new ShaderTagId("LightMode")).name);
        return res;
    }
    // tag values come back interned (e.g. "MOTIONVECTORS" when another shader declared that spelling first): compare without case
    static bool HasPass(Shader s, string lightMode) => LightModes(s).Any(m => string.Equals(m, lightMode, StringComparison.OrdinalIgnoreCase));

    static void M3(List<Entry> entries)
    {
        Section("M3 keywords and passes of the replacements introduced by 3.7 and 5.2 (Fitted blocks and CopyRoutes targets)");
        var pre = PreexistingTargets(entries);
        foreach (var target in AllTargets(entries).Where(t => !pre.Contains(t) && t.StartsWith("HS2Next/", StringComparison.Ordinal)))
        {
            var s = Shader.Find(target);
            if (!s) { Check(target + " found", false); continue; }
            // lit: a UniversalForward pass with the additional-light keyword (the unlit particle stand-ins declare none)
            bool lit = HasPass(s, "UniversalForward") && !ShaderMap.NeedsNoLightmap(s);
            if (lit)
            {
                var missing = LoopKeywords.Where(k => !s.keywordSpace.FindKeyword(k).isValid).ToList();
                Check($"{target}: light-loop keywords declared", missing.Count == 0, missing.Count > 0 ? "missing " + string.Join(", ", missing) : "");
                // the probe keywords matter where the shader samples reflection probes (not the legacy family): multi_compile_fragment
                var probe = ProbeKeywords.Where(k => s.keywordSpace.FindKeyword(k).isValid).ToList();
                var dynamicProbe = ProbeKeywords.Where(k => s.keywordSpace.FindKeyword(k) is LocalKeyword lk && lk.isValid && lk.isDynamic).ToList();
                if (probe.Count > 0) Check($"{target}: reflection-probe keywords as variants (not dynamic branches)", dynamicProbe.Count == 0 && probe.Count == ProbeKeywords.Length,
                    $"declared [{string.Join(", ", probe)}], dynamic [{string.Join(", ", dynamicProbe)}]");
                else Info($"{target}: no reflection-probe keywords (samples no reflection probe)");
            }
            else Info($"{target}: unlit (no lit UniversalForward pass)");
            bool transparent = s.renderQueue > 2500;
            if (!transparent)
                Check($"{target}: MotionVectors and DepthNormals passes", HasPass(s, "MotionVectors") && HasPass(s, "DepthNormals"), "passes " + string.Join(", ", LightModes(s)));
            // above 2500: always (a transparent default queue) or by the material (blend from properties, queue kept by Swap)
            bool receiveKw = s.keywordSpace.FindKeyword(ShaderMap.ReceiveShadowsOff).isValid;
            if (transparent) Info($"{target}: transparent queue {s.renderQueue}: {(receiveKw ? "declares " + ShaderMap.ReceiveShadowsOff : "receives no shadows by construction (code)")}");
            else if (AboveTransparentByMaterial.Contains(target)) Check($"{target}: declares {ShaderMap.ReceiveShadowsOff} (materials can sit above 2500)", receiveKw);
        }
    }

    // replacements whose materials can sit above queue 2500 (blend modes from material properties)
    static readonly HashSet<string> AboveTransparentByMaterial = new HashSet<string>(StringComparer.Ordinal)
    {
        "HS2Next/Compatible/Mochie/Standard", "HS2Next/Compatible/UBER - Metallic Setup/ Core", "HS2Next/Compatible/UBER - Metallic Setup/2 Sided/ Core",
    };

    // ------------------------------------------------------------------ M4 coverage from the census

    static void M4()
    {
        Section("M4 coverage (Tools/survey/mod_shader_census.tsv with the current ShaderMap; non-null slots; the names with per-copy rules by bundle copy from mod_shader_census_copies.tsv)");
        string path = Path.Combine(Root, "Tools", "survey", "mod_shader_census.tsv");
        if (!File.Exists(path)) { Check("census read", false, path); return; }
        var lines = File.ReadAllLines(path).Where(l => !l.StartsWith("#")).ToArray();
        var head = lines[0].Split('\t');
        int Col(string n) => Array.IndexOf(head, n);
        var rows = lines.Skip(1).Select(l => l.Split('\t')).Where(f => f.Length == head.Length && !f[0].StartsWith("(")).ToList();
        // per bundle copy (census time): shader -> [(target, column -> slots)]; target "-" = fallback, "?" = copy not read (by name)
        var copies = new Dictionary<string, List<(string target, Dictionary<string, double> v)>>(StringComparer.Ordinal);
        string cpath = Path.Combine(Root, "Tools", "survey", "mod_shader_census_copies.tsv");
        if (File.Exists(cpath))
        {
            var cl = File.ReadAllLines(cpath).Where(l => !l.StartsWith("#")).ToArray();
            var ch = cl[0].Split('\t');
            foreach (var f in cl.Skip(1).Select(l => l.Split('\t')).Where(f => f.Length == ch.Length))
            {
                var v = new Dictionary<string, double>(StringComparer.Ordinal);
                for (int i = 2; i < ch.Length; i++) v[ch[i]] = double.Parse(f[i], CultureInfo.InvariantCulture);
                if (!copies.TryGetValue(f[0], out var l)) copies[f[0]] = l = new List<(string, Dictionary<string, double>)>();
                l.Add((f[1], v));
            }
        }
        else Info("Tools/survey/mod_shader_census_copies.tsv not found: every name counted by name");
        bool Mapped(string sh) => ShaderMap.Fitted.ContainsKey(sh) || sh.StartsWith("HS2Next/", StringComparison.Ordinal) || sh.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal);
        bool Heuristic(string sh) => sh.StartsWith("Unreal/", StringComparison.Ordinal);
        // byName: the shader name's rule; byTarget: the rule on a replacement a bundle copy resolved to
        double Pct(string col, Func<string, bool> byName, Func<string, bool> byTarget, bool copyAware)
        {
            int c = Col(col); double t = 0, m = 0;
            foreach (var f in rows)
            {
                double v = double.Parse(f[c], CultureInfo.InvariantCulture);
                t += v;
                if (copyAware && copies.TryGetValue(f[0], out var cl) && cl.All(x => x.v.ContainsKey(col)))
                {
                    double seen = 0;
                    foreach (var (target, cv) in cl)
                    {
                        double x = cv[col]; seen += x;
                        if (target == "?" ? byName(f[0]) : target != "-" && byTarget(target)) m += x;
                    }
                    if (v - seen > 0 && byName(f[0])) m += v - seen;   // slots without a bundle source in the copy pass: by name
                }
                else if (byName(f[0])) m += v;
            }
            return 100 * m / Math.Max(1, t);
        }
        var lmCache = new Dictionary<string, bool>(StringComparer.Ordinal);
        // a lightmap path, or no lighting at all (unlit replacements keep lightmapped slots: ShaderMap.NeedsNoLightmap, package L)
        bool TargetOnLightmapPath(string target)
        {
            if (!lmCache.TryGetValue(target, out var ok)) { var s = Shader.Find(target); lmCache[target] = ok = ShaderMap.SupportsLightmaps(s) || ShaderMap.NeedsNoLightmap(s); }
            return ok;
        }
        bool OnLightmapPath(string sh)
        {
            if (Heuristic(sh)) return true;   // URP/Lit with the heuristic's inputs
            if (!ShaderMap.Fitted.TryGetValue(sh, out var target)) return sh.StartsWith("HS2Next/", StringComparison.Ordinal) && TargetOnLightmapPath(sh);
            return TargetOnLightmapPath(target);
        }
        bool AnyTarget(string t) => true;
        double Val(Dictionary<string, double> d, string k) => d.TryGetValue(k, out var x) ? x : 0;
        var rowsSpec = new (string label, string col, Func<string, bool> byName, Func<string, bool> byTarget, double min)[]
        {
            ("mod item materials", "item_mod_mats", Mapped, AnyTarget, 80),
            ("Studio-flagged mod slots", "item_mod_flagged", Mapped, AnyTarget, 98),
            ("mod map active slots (with the Unreal heuristic)", "map_mod_active", s => Mapped(s) || Heuristic(s), AnyTarget, 94),
            ("lightmapped mod-map slots on a lightmap path (or unlit)", "map_mod_lm", OnLightmapPath, TargetOnLightmapPath, 96),
            ("installed cards' mod-part slots", "card_slots", Mapped, AnyTarget, 97),
            ("user's scenes: item slots (with the heuristic)", "scene_item_slots", s => Mapped(s) || Heuristic(s), AnyTarget, 99),
            ("user's scenes: flagged item slots", "scene_item_flagged", Mapped, AnyTarget, 100),
            ("user's scenes: map active slots (with the heuristic)", "scene_map_active", s => Mapped(s) || Heuristic(s), AnyTarget, 99),
            ("user's scenes: character-part slots", "scene_chara_slots", Mapped, AnyTarget, 95),
        };
        foreach (var (label, col, byName, byTarget, min) in rowsSpec)
        {
            if (Col(col) < 0) { Check(label, false, "column " + col + " missing"); continue; }
            double named = Pct(col, byName, byTarget, false), p = Pct(col, byName, byTarget, true);
            Check($"{label}: {p:F1} % by bundle copy (target >= {min} %; by name {named:F1} %)", p >= min - 1e-9);
        }
        Info($"without the heuristic (by copy): mod map active {Pct("map_mod_active", Mapped, AnyTarget, true):F1} %, scenes' item slots {Pct("scene_item_slots", Mapped, AnyTarget, true):F1} %, scenes' map slots {Pct("scene_map_active", Mapped, AnyTarget, true):F1} %");
        foreach (var kv in copies.OrderBy(k => k.Key, StringComparer.Ordinal))
            Info($"{kv.Key}: " + string.Join("; ", kv.Value.Select(x => $"{x.target} item mats {Val(x.v, "item_mod_mats")}, flagged {Val(x.v, "item_mod_flagged")}, "
                + $"map active {Val(x.v, "map_mod_active")}, card slots {Val(x.v, "card_slots")}, scene chara slots {Val(x.v, "scene_chara_slots")}")));
        var top = rows.Where(f => !Mapped(f[0]) && !Heuristic(f[0])).OrderByDescending(f => double.Parse(f[Col("item_mod_flagged")], CultureInfo.InvariantCulture)).Take(6)
            .Select(f => $"{f[0]} {f[Col("item_mod_flagged")]}");
        Info("largest unmapped flagged (by name): " + string.Join(", ", top));
    }

    // ------------------------------------------------------------------ M6 the Unreal heuristic on the junk-yard pack

    const string JunkYardBundle = "[xoa_designs]junk_yard_props_bundles/[xoa_designs]junk_yard_props/data_prefab_000.unity3d";
    // five items holding all 22 Unreal materials of the pack (census: Cache/modshaders/where.json)
    static readonly string[] JunkYardItems = { "Junkyard 1", "Junkyard 2", "Metal Barrel Drum Red", "Plastic Tray", "Rusted Wheel Cover" };
    static readonly Regex ColourName = new Regex(@"_(D|BaseColor|BC|ALB|Albedo|Diffuse)$", RegexOptions.IgnoreCase);

    static void M6()
    {
        Section("M6 the Unreal heuristic (ShaderMap.UnrealInputs) on the junk-yard pack");
        var gd = CardResolveTest.BuildDb(null);
        var gos = new List<GameObject>();
        try
        {
            foreach (var n in JunkYardItems)
            {
                var pf = gd.LoadAssetAt<GameObject>(null, JunkYardBundle, null, n);
                if (pf) gos.Add(Object.Instantiate(pf)); else Info("not found: " + n);
            }
            Check("junk-yard items loaded", gos.Count == JunkYardItems.Length, $"{gos.Count} of {JunkYardItems.Length}");
            // the originals: Unreal materials and their colour / normal textures, before the swap
            var before = new Dictionary<(Renderer, int), (string mat, Texture colour, Texture normal)>();
            foreach (var g in gos)
                foreach (var r in g.GetComponentsInChildren<Renderer>(true))
                {
                    var ms = r.sharedMaterials;
                    for (int i = 0; i < ms.Length; i++)
                    {
                        var m = ms[i];
                        if (!m || !m.shader || !m.shader.name.StartsWith("Unreal/", StringComparison.Ordinal)) continue;
                        Texture colour = null, normal = null;
                        foreach (var p in m.GetTexturePropertyNames().Where(p => p.StartsWith("Material_Texture2D_", StringComparison.Ordinal)))
                        {
                            var t = m.GetTexture(p);
                            if (!t) continue;
                            if (!colour && ColourName.IsMatch(t.name)) colour = t;
                            if (!normal && Regex.IsMatch(t.name, @"_(N|Normal|NRM)$", RegexOptions.IgnoreCase)) normal = t;
                        }
                        before[(r, i)] = (m.name, colour, normal);
                    }
                }
            int mats = before.Values.Select(v => v.mat).Distinct().Count();
            foreach (var g in gos) ShaderMap.ApplyUrp(g);
            int withColour = 0, colourOk = 0, withNormal = 0, normalOk = 0;
            var bad = new List<string>();
            var seen = new HashSet<string>();
            foreach (var kv in before)
            {
                var (r, i) = kv.Key; var (name, colour, normal) = kv.Value;
                if (!seen.Add(name)) continue;
                var m = r.sharedMaterials[i];
                if (colour) { withColour++; if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") == colour) colourOk++; else bad.Add($"{name}: _BaseMap {(m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") ? m.GetTexture("_BaseMap").name : "none")} (expected {colour.name})"); }
                if (normal) { withNormal++; if (m.HasProperty("_BumpMap") && m.GetTexture("_BumpMap") == normal && m.IsKeywordEnabled("_NORMALMAP")) normalOk++; }
            }
            Info($"{mats} Unreal materials in {gos.Count} items: {withColour} with a colour texture, {withNormal} with a normal map");
            Check($"every Unreal material with a colour texture draws it ({colourOk} of {withColour})", withColour > 0 && colourOk == withColour, string.Join("; ", bad.Take(6)));
            Check($"every Unreal material with a normal map gets it as _BumpMap ({normalOk} of {withNormal})", normalOk == withNormal);
        }
        finally
        {
            foreach (var g in gos) if (g) Object.DestroyImmediate(g);
            gd.UnloadAll();
        }
    }

    // ------------------------------------------------------------------ M7 lightmap routing (package L)

    static void M7()
    {
        Section("M7 lightmap routing (package L: LightmapPaths.M7)");
        var t = Type.GetType("LightmapPaths");
        var mi = t?.GetMethod("M7", BindingFlags.Public | BindingFlags.Static);
        if (mi == null) { Info("pending: LightmapPaths.M7(Action<string, bool, string> check, Action<string> info) not in the project yet"); return; }
        mi.Invoke(null, new object[] { (Action<string, bool, string>)Check, (Action<string>)Info });
    }

    // ------------------------------------------------------------------ M8 the 5.2 routes per bundle copy (ST3)

    static void M8(List<Entry> entries)
    {
        Section("M8 the 5.2 routes per bundle copy (mod_shader_props.json copies on in-memory stand-ins; ShaderMap.TryResolve)");
        Check("5.2 entries exist", entries.Any(e => e.Phase == "5.2"));
        foreach (var e in entries.Where(x => x.Phase == "5.2"))
        {
            var copies = JsonCopies(e.Orig);
            if (copies.Count == 0) { Check($"{e.Orig}: parsed in mod_shader_props.json", false); continue; }
            var assign = new List<string>();
            bool mainOk = false;
            foreach (var (sig, mats, cn, props) in copies)
            {
                var si = StandIn(cn);
                string target = null;
                try { if (!ShaderMap.TryResolve(e.Orig, si, out target)) target = null; }
                finally { Object.DestroyImmediate(si); }
                assign.Add($"{sig} ({cn.Count} props, {mats} mats) -> {target ?? "fallback"}");
                if (props != null && target == e.Target) mainOk = true;
            }
            Check($"{e.Orig} (5.2 {e.Block}): the main copy takes {e.Target}", mainOk, string.Join("; ", assign));
            if (copies.Count > 1) Info($"{e.Orig}: " + string.Join("; ", assign));
        }
    }

    // ------------------------------------------------------------------ M9 MaterialEditor parity (5.2)

    static void M9()
    {
        Section("M9 MaterialEditor parity: undeclared names skipped; the plugin's own copies keep only their keywords");
        var gd = CardResolveTest.BuildDb(null);
        var made = new List<Material>();
        try
        {
            var reg = HS2Next.Chara.Ext.MaterialEditorShaders.Of(gd);
            var plugin = HS2Next.Chara.Ext.MaterialEditorApplier.PluginCopies;
            // a name ShaderMap replaces that nobody declares (no card of the install uses one): skipped
            var undeclared = ShaderMap.Fitted.Keys.Where(n => !plugin.ContainsKey(n) && reg.Find(n) == null).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Check("some names ShaderMap replaces are declared nowhere", undeclared.Count > 0, $"{undeclared.Count}");
            int skipped = undeclared.Count(n => !HS2Next.Chara.Ext.MaterialEditorApplier.ResolveShader(n, gd, out _));
            Check($"every name declared nowhere is skipped ({skipped} of {undeclared.Count})", skipped == undeclared.Count,
                string.Join(", ", undeclared.Where(n => HS2Next.Chara.Ext.MaterialEditorApplier.ResolveShader(n, gd, out _)).Take(8)));
            // the plugin's own names that ShaderMap replaces resolve (directly: no manifest copy)
            var own = plugin.Keys.Where(n => ShaderMap.Fitted.ContainsKey(n) && reg.Find(n) == null).OrderBy(n => n, StringComparer.Ordinal).ToList();
            int resolved = own.Count(n => HS2Next.Chara.Ext.MaterialEditorApplier.ResolveShader(n, gd, out _));
            Check($"the plugin's own names resolve ({resolved} of {own.Count})", own.Count > 0 && resolved == own.Count);
            // the plugin's copies table: only names the plugin carries, each with its copy's keyword list
            Info($"plugin copies: {plugin.Count} names, {plugin.Count(kv => kv.Value.Length > 0)} with material keywords");
            // a swap to the plugin's own copy keeps only that copy's keywords: AIT/Clothes Alpha True declares none, AIT/Clothes
            // True keeps _DITHERSWITCH_ON
            foreach (var (name, kws, kept) in new[]
            {
                ("AIT/Clothes Alpha True", new[] { "_DITHERSWITCH_ON", "_NORMALMAP" }, new string[0]),
                ("AIT/Clothes True", new[] { "_DITHERSWITCH_ON", "_EMISSIONCOLORTEXBASE_ON" }, new[] { "_DITHERSWITCH_ON" }),
                ("AIT/main standard2 studio alpha", new[] { "_METALLICGLOSSMAP", "_NORMALMAP" }, new string[0]),
            })
            {
                var keep = HS2Next.Chara.Ext.MaterialEditorApplier.PluginCopyKeywords(name, gd);
                var sh = HS2Next.Chara.Ext.MaterialEditorApplier.ResolveShader(name, gd, out _);
                if (keep == null || !sh) { Check($"{name}: the plugin's copy is in effect and resolves", false); continue; }
                var m = new Material(sh); made.Add(m);
                foreach (var k in kws) m.EnableKeyword(k);
                HS2Next.Chara.Ext.MaterialEditorApplier.KeepKeywords(m, keep);
                var after = m.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal).ToArray();
                Check($"{name}: keywords [{string.Join(",", kws)}] -> [{string.Join(",", after)}] (the copy declares [{string.Join(",", keep)}])",
                    after.SequenceEqual(kept.OrderBy(k => k, StringComparer.Ordinal)));
            }
        }
        finally
        {
            foreach (var m in made) if (m) Object.DestroyImmediate(m);
            gd.UnloadAll();
        }
    }

    // ------------------------------------------------------------------ M10 compatible shader names and their families

    /// <summary>
    /// M10: the shaders under Assets/HS2Next/Shaders named HS2Next/Compatible/… (the replacements for shaders of mods and
    /// third-party asset packs) each belong to exactly one family of ShaderMap.CompatibleFamilies and are replacement targets of
    /// ShaderMap's tables; every family has at least one, and its count equals the shaders found; no other shader there has a name
    /// a family would claim with the prefix (a replacement left with its plain HS2Next/ name); the list is ordinal by label.
    /// </summary>
    static void M10()
    {
        Section("M10 compatible shader names and their families (ShaderMap.CompatibleFamilies)");
        var names = AssetDatabase.FindAssets("t:Shader", new[] { "Assets/HS2Next/Shaders" })
            .Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".shader", StringComparison.Ordinal))
            .Select(p => AssetDatabase.LoadAssetAtPath<Shader>(p)).Where(s => s).Select(s => s.name)
            .Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        var compat = names.Where(n => n.StartsWith(ShaderMap.CompatiblePrefix, StringComparison.Ordinal)).ToList();
        var targets = new HashSet<string>(ShaderMap.Fitted.Values.Concat(ShaderMap.CopyRoutes.Values.SelectMany(l => l.Select(x => x.replacement))),
            StringComparer.Ordinal);
        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        var bad = new List<string>();
        foreach (var n in compat)
        {
            var fam = ShaderMap.CompatibleFamiliesOf(n);
            if (fam.Count != 1) bad.Add($"{n} in {fam.Count} families ({string.Join(", ", fam)})");
            else found[fam[0]] = found.TryGetValue(fam[0], out var c) ? c + 1 : 1;
            if (!targets.Contains(n)) bad.Add($"{n} is no replacement target");
        }
        Check($"every compatible shader ({compat.Count} of {names.Count} shaders) is in exactly one family and a replacement target",
            compat.Count > 0 && bad.Count == 0, string.Join("; ", bad));
        var families = ShaderMap.CompatibleFamilies;
        foreach (var kv in families)
        {
            found.TryGetValue(kv.Key, out var n);
            Check($"family {kv.Key}: {kv.Value} compatible shaders", kv.Value >= 1 && kv.Value == n, $"{n} shader assets");
        }
        var labels = families.Select(kv => kv.Key).ToList();
        Check("the families are ordinal by label, each listed once", labels.SequenceEqual(labels.Distinct().OrderBy(x => x, StringComparer.Ordinal)),
            string.Join(", ", labels));
        Check("the family counts add up to the compatible shaders", families.Sum(kv => kv.Value) == compat.Count,
            $"{families.Sum(kv => kv.Value)} / {compat.Count}");
        var plain = names.Where(n => !n.StartsWith(ShaderMap.CompatiblePrefix, StringComparison.Ordinal) && n.StartsWith("HS2Next/", StringComparison.Ordinal)
            && ShaderMap.CompatibleFamiliesOf(ShaderMap.CompatiblePrefix + n.Substring("HS2Next/".Length)).Count > 0).ToList();
        Check("no shader with a plain HS2Next/ name belongs to a family", plain.Count == 0, string.Join(", ", plain));
        Info("families: " + string.Join(", ", families.Select(kv => $"{kv.Key} {kv.Value}")));
    }

    // ------------------------------------------------------------------ M5 probe regressions (graphics)

    static readonly Regex Manifest = new Regex("^#\\s*fit:\\s*(.*)$");
    static readonly Regex KeyValue = new Regex("(\\w+)=(\"[^\"]*\"|\\S+)|(\\w+)");

    // file name prefixes of the 3.7 packages' and the 5.2 tail's case files (a *_fit.txt with one of them and no manifest is reported)
    static readonly string[] Prefixes37 = { "f3_", "hmi_", "hcr_", "hct_", "hcv9_", "hooh_", "hph_", "hv2_", "hv3_", "gdc_", "l_", "s52_" };

    static void M5()
    {
        Section("M5 probe regressions (Tests/FitCases/*_fit.txt with a '# fit:' manifest; SkinFit.Sweep -fitPipe both)");
        string filter = Arg("-mstFit", null);
        var files = Directory.GetFiles(Path.Combine(Root, "Tests", "FitCases"), "*_fit.txt").OrderBy(x => x, StringComparer.Ordinal).ToList();
        var skinFit = typeof(SkinFit);
        var jobArgs = skinFit.GetField("jobArgs", BindingFlags.NonPublic | BindingFlags.Static);
        var reset = skinFit.GetMethod("ResetStatics", BindingFlags.NonPublic | BindingFlags.Static);
        int runs = 0;
        foreach (var f in files)
        {
            if (filter != null && Path.GetFileName(f).IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var manifests = File.ReadLines(f).TakeWhile(l => l.StartsWith("#")).Where(l => Manifest.IsMatch(l)).ToList();
            if (manifests.Count == 0)
            {
                if (Prefixes37.Any(p => Path.GetFileName(f).StartsWith(p, StringComparison.Ordinal))) Info($"{Path.GetFileName(f)}: no '# fit:' manifest line (not run)");
                continue;
            }
            foreach (var line in manifests)
            {
                var mm = Manifest.Match(line);
                if (!mm.Success) continue;
                var kv = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (Match k in KeyValue.Matches(mm.Groups[1].Value))
                    if (k.Groups[1].Success) kv[k.Groups[1].Value] = k.Groups[2].Value.Trim('"'); else kv[k.Groups[3].Value] = "";
                if (!kv.TryGetValue("shader", out var shader) || !kv.TryGetValue("repl", out var repl)) { Check(Path.GetFileName(f) + ": manifest has shader and repl", false, line); continue; }
                double max = kv.TryGetValue("rms", out var r) ? double.Parse(r, CultureInfo.InvariantCulture) : 0.002;
                string outName = "mst_" + Path.GetFileNameWithoutExtension(f) + "_" + runs;
                var args = new List<string> { "Unity", "-fitShader", shader, "-repl", repl, "-fitPipe", "both", "-sweepFile", f, "-sweepOut", outName };
                if (kv.TryGetValue("item", out var item)) { args.Add("-fitItem"); args.Add(item); }
                if (kv.TryGetValue("material", out var mat)) { args.Add("-fitMaterial"); args.Add(mat); }
                if (kv.ContainsKey("origFind")) args.Add("-origFind");
                string outFile = Path.Combine(Root, "Spike", "out", "fit", string.Concat(shader.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')) + "_" + outName + ".txt");
                if (File.Exists(outFile)) File.Delete(outFile);
                var sw = Stopwatch.StartNew();
                try
                {
                    jobArgs.SetValue(null, args.ToArray());
                    reset.Invoke(null, null);
                    SkinFit.Sweep();
                }
                catch (Exception e) { Check($"{Path.GetFileName(f)} ({shader})", false, (e.InnerException ?? e).Message); continue; }
                finally { jobArgs.SetValue(null, null); ShaderFit.SetPipeline(null); }
                runs++;
                var total = File.Exists(outFile) ? File.ReadLines(outFile).FirstOrDefault(l => l.StartsWith("# TOTAL RMS")) : null;
                var tm = total != null ? Regex.Match(total, @"TOTAL RMS ([0-9.]+) over (\d+) values") : Match.Empty;
                if (!tm.Success) { Check($"{Path.GetFileName(f)} ({shader})", false, "no TOTAL RMS in " + outFile); continue; }
                double rms = double.Parse(tm.Groups[1].Value, CultureInfo.InvariantCulture);
                Check($"{Path.GetFileName(f)}: {shader} vs {repl}{(item != null ? " on " + item : "")}: RMS {rms:F4} over {tm.Groups[2].Value} values (max {max})",
                    rms <= max + 1e-9, $"{sw.Elapsed.TotalSeconds:F0} s");
            }
        }
        Check("at least one fit file with a manifest ran", runs > 0, filter != null ? "filter " + filter : "");
    }

    // ------------------------------------------------------------------ JSON

    /// <summary>Minimal JSON reader: objects -> Dictionary, arrays -> List, numbers -> double (as MapShaderTest's).</summary>
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
}
