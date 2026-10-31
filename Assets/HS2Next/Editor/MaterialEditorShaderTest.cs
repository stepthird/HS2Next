// MaterialEditor's shader registry (Runtime/Chara/Ext/MaterialEditorShaders.cs) and the independence of a character's
// MaterialEditor shader swaps from what was built before it (Phase 4.4 package M). Headless.
//   MS1 registry: the declarations of the loaded zipmods (names, declaring zipmods, the later declaration of a name wins);
//       a second build of the registry from its cache file gives the same declarations without reading a manifest; the
//       manifest parser on fixed texts (the MaterialEditor element wins over HS2_MaterialEditor, entries in manifest order,
//       entries without a name left out, no element: none).
//   MS2 copies: every declared name ShaderMap has a replacement for loads its declared copy, once (the same object when asked
//       again), and the replacement of that copy is reported; a name declared nowhere has no copy.
//   MS3 order: the cards whose MaterialEditor shader edits name a shader ShaderMap replaces (found by decoding every card; or
//       -cards a.png,b.png) are built first in a fresh session, then other copies of their shader names are loaded into the
//       process (those cards built again in reverse order, the -poison cards, the -poisonAsset prefabs
//       "guid|bundle|asset;..."), then the cards are built again in reverse order: every snapshot is identical to the first
//       build; ResolveShader of every name gives the same shader, replacement and path before and after; the process copies
//       of the names before and after the poisoning are reported (the poisoning must have added copies).
// Run: -executeMethod MaterialEditorShaderTest.Run [-only MS1,MS3] [-cards a.png,...] [-poison a.png,...]
//      [-poisonAsset "guid|bundle|asset;..."]. Report: Spike/out/maker/material_editor_shader_report.txt. Numbers only.
// RunLines -cards a.png,...: the S1 lines of those cards that differ from their baselines (redirect off), each with its
// class (review aid; report material_editor_shader_lines.txt).
// RegistryClass: the S1 explain class "me.shaderRegistry" of a differing snapshot line (MakerClothesTest.RunS1Explain): the
// lines a MaterialEditor shader edit through ShaderMap writes (the swapped materials, the extension summary's MaterialEditor
// part, the "me.shader" missing-content records).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HS2Next;
using HS2Next.Chara;
using HS2Next.Chara.Ext;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.Data.Ext;
using HS2Next.Tests.MakerCore;
using UnityEngine;
using Debug = UnityEngine.Debug;
using OT = HS2Next.Data.Ext.MaterialEditorData.ObjectType;
using static MakerTestKit;

public static class MaterialEditorShaderTest
{
    public const string ClassRegistry = "me.shaderRegistry";

    public static void Run()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        Rep.AppendLine($"MaterialEditorShaderTest (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, pipeline {(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline ? "URP" : "none")})");
        try
        {
            using (var h = MakerCoreTest.Harness.Open())
            {
                Rep.AppendLine("catalog: " + string.Join("; ", h.Catalog.Log.Where(l => l.StartsWith("zipmod set", StringComparison.Ordinal))));
                if (Want("MS1")) Guard("MS1 registry", () => MS1(h.Catalog));
                if (Want("MS2")) Guard("MS2 copies", () => MS2(h.Catalog));
                if (Want("MS3")) Guard("MS3 order", () => MS3(h));
            }
        }
        catch (Exception e) { Check("no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace)); }
        Finish("material_editor_shader_report.txt", "MaterialEditorShaderTest (MaterialEditor's shader registry; swaps independent of the build order)", sw, "ME SHADER TEST");
    }

    // ================================================================ MS1

    static void MS1(ContentCatalog cat)
    {
        var reg = MaterialEditorShaders.Of(cat);
        var decl = reg.Declarations;
        Info($"registry: {decl.Count} names declared by {reg.DeclaringZipmods} zipmods ({reg.DeclarationLines} declarations) over {reg.Zipmods} zipmods; " +
             $"{reg.FromCache} from the cache, {reg.ManifestsRead} manifests read, {reg.BuildMs:F0} ms; cache {Rel(reg.CacheFile ?? "-")}{(reg.CacheError != null ? " (" + reg.CacheError + ")" : "")}");
        Check("MS1 the loaded zipmods declare shaders", decl.Count > 0 && reg.DeclaringZipmods > 0, $"{decl.Count} names");
        Check("MS1 every declaration names its zipmod, element and bundle", decl.Values.All(d => d.Zipmod != null && (d.Element == "MaterialEditor" || d.Element == "HS2_MaterialEditor") && !string.IsNullOrEmpty(d.Bundle)),
              string.Join(", ", decl.Values.Where(d => d.Zipmod == null || string.IsNullOrEmpty(d.Bundle)).Select(d => d.Name).Take(5)));
        foreach (var d in decl.Values.OrderBy(x => x.Name, StringComparer.Ordinal))
            Info($"declared {d.Name}: {d.Zipmod.Guid} ({Path.GetFileName(d.Zipmod.Path)}) {d.Element} {d.Bundle} | {d.Asset ?? "-"}" +
                 $"{(d.RenderQueue != null ? " queue " + d.RenderQueue : "")}{(d.ShaderOptimization != null ? " opt " + d.ShaderOptimization : "")}" +
                 $"{(ShaderMap.Fitted.ContainsKey(d.Name) || ShaderMap.CopyRoutes.ContainsKey(d.Name) ? " [ShaderMap]" : "")}");
        // the later declaration wins: the last declaring zipmod in load order holds each name
        var order = cat.ZipmodsInLoadOrder.Select((z, i) => (z, i)).ToDictionary(x => x.z, x => x.i);
        int laterWins = 0, checkedNames = 0;
        var byName = new Dictionary<string, List<ZipmodInfo>>(StringComparer.Ordinal);
        foreach (var z in cat.ZipmodsInLoadOrder)
        {
            if (z.Guid == null) continue;
            foreach (var r in MaterialEditorShaders.ReadManifest(z.Path))
            {
                if (!byName.TryGetValue(r[2], out var l)) byName[r[2]] = l = new List<ZipmodInfo>();
                l.Add(z);
            }
        }
        foreach (var kv in byName)
        {
            checkedNames++;
            var last = kv.Value.OrderBy(z => order[z]).Last();
            if (decl.TryGetValue(kv.Key, out var d) && d.Zipmod == last) laterWins++;
            if (kv.Value.Select(z => z.Guid).Distinct().Count() > 1)
                Info($"declared by several zipmods: {kv.Key}: {string.Join(" < ", kv.Value.OrderBy(z => order[z]).Select(z => z.Guid))}");
        }
        Check("MS1 the later declaration of a name wins (load order), read again from every declaring manifest", laterWins == checkedNames && checkedNames == decl.Count, $"{laterWins}/{checkedNames} names, registry {decl.Count}");

        // the same declarations again from the cache file (a fresh registry object over the same catalog state)
        var again = BuildFresh(cat);
        bool same = again != null && again.Declarations.Count == decl.Count && decl.All(kv => again.Declarations.TryGetValue(kv.Key, out var o) &&
                    o.Zipmod == kv.Value.Zipmod && o.Bundle == kv.Value.Bundle && o.Asset == kv.Value.Asset && o.Element == kv.Value.Element);
        Check("MS1 a second build from the cache file gives the same declarations without reading a manifest", same && again.ManifestsRead == 0 && again.FromCache == again.Zipmods,
              again == null ? "no second registry" : $"{again.Declarations.Count} names, {again.FromCache}/{again.Zipmods} from the cache, {again.ManifestsRead} read, {again.BuildMs:F0} ms");

        // the parser
        string both = "<manifest><guid>x</guid><HS2_MaterialEditor><Shader Name=\"B\" AssetBundle=\"b.unity3d\" Asset=\"b\"/></HS2_MaterialEditor>" +
                      "<MaterialEditor><Shader Name=\"A1\" AssetBundle=\"a.unity3d\" Asset=\"a1\" RenderQueue=\"3000\"/><Shader AssetBundle=\"a.unity3d\"/>" +
                      "<Shader Name=\"A2\" AssetBundle=\"a.unity3d\" ShaderOptimization=\"False\"/></MaterialEditor></manifest>";
        var p = MaterialEditorShaders.Parse(both);
        Check("MS1 parser: <MaterialEditor> wins over <HS2_MaterialEditor>; entries in order; a nameless entry left out",
              p.Count == 2 && p[0][1] == "MaterialEditor" && p[0][2] == "A1" && p[0][5] == "3000" && p[1][2] == "A2" && p[1][4] == "" && p[1][6] == "False",
              string.Join(" ; ", p.Select(r => string.Join(",", r))));
        var p2 = MaterialEditorShaders.Parse("<manifest><HS2_MaterialEditor><Shader Name=\"B\" AssetBundle=\"b.unity3d\" Asset=\"b\"/></HS2_MaterialEditor></manifest>");
        var p3 = MaterialEditorShaders.Parse("<manifest><guid>x</guid></manifest>");
        Check("MS1 parser: <HS2_MaterialEditor> alone is read; no element: none", p2.Count == 1 && p2[0][1] == "HS2_MaterialEditor" && p2[0][2] == "B" && p3.Count == 0,
              $"{p2.Count} / {p3.Count}");
    }

    // A second registry over the same catalog state (not the catalog's own instance), built now.
    static MaterialEditorShaders BuildFresh(ContentCatalog cat)
    {
        var r = MaterialEditorShaders.Unshared(cat);
        _ = r.Declarations;
        return r;
    }

    // ================================================================ MS2

    static void MS2(ContentCatalog cat)
    {
        var reg = MaterialEditorShaders.Of(cat);
        int names = 0, loaded = 0, sameAgain = 0, byPrefab = 0, byPrefabLoaded = 0;
        foreach (var d in reg.Declarations.Values.OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            if (!ShaderMap.Fitted.ContainsKey(d.Name) && !ShaderMap.CopyRoutes.ContainsKey(d.Name)) continue;
            names++;
            var sh = reg.Original(d.Name);
            if (sh) loaded++;
            if (d.Asset != null) { byPrefab++; if (sh) byPrefabLoaded++; }
            if (sh && reg.Original(d.Name) == sh) sameAgain++;
            string repl = ShaderMap.TryResolve(d.Name, sh, out var r) ? r : "(no replacement: the swap is skipped)";
            Info($"copy {d.Name}: {(sh ? $"{sh.GetPropertyCount()} properties, {sh.keywordSpace.keywordCount} keywords" : d.Asset == null ? "none (the bundle holds no Shader asset of that name: its shaders are dependencies, which a named load does not find)" : "NOT LOADED")}" +
                 $" from {d.Zipmod.Guid} {d.Bundle} | {d.Asset ?? "-"} -> {repl}");
        }
        // A declaration without Asset asks the bundle for a Shader asset of that name; a bundle whose shaders are only
        // dependencies of its prefabs has none, so such a declaration gives no copy (reported above, not required).
        Check("MS2 every declared name with a ShaderMap replacement and a prefab loads its declared copy", byPrefab > 0 && byPrefabLoaded == byPrefab,
              $"{byPrefabLoaded}/{byPrefab} by prefab; {loaded}/{names} in all");
        Check("MS2 a copy is loaded once (the same object when asked again)", sameAgain == loaded, $"{sameAgain}/{loaded}");
        Check("MS2 a name declared nowhere has no copy (the plugin's own game shaders, unknown names)",
              reg.Original("AIT/Clothes True") == null && reg.Original("Standard") == null && reg.Original("HS2Next/no such shader") == null);
    }

    // ================================================================ MS3

    static IEnumerable<string> Names(MaterialEditorData me) =>
        me?.Shaders.Where(s => s.ShaderName != null && (ShaderMap.Fitted.ContainsKey(s.ShaderName) || ShaderMap.CopyRoutes.ContainsKey(s.ShaderName))).Select(s => s.ShaderName)
        ?? Enumerable.Empty<string>();

    static void MS3(MakerCoreTest.Harness h)
    {
        var cat = h.Catalog;
        var all = MakerCoreTest.AllCards();
        List<string> targets;
        var names = new SortedSet<string>(StringComparer.Ordinal);
        string only = Arg("-cards");
        if (!string.IsNullOrEmpty(only))
        {
            var want = new HashSet<string>(only.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
            targets = all.Where(f => want.Contains(Path.GetFileName(f))).ToList();
        }
        else targets = new List<string>();
        var dsw = Stopwatch.StartNew();
        int decoded = 0;
        foreach (var f in all)
        {
            MaterialEditorData me = null;
            try { me = CardExtensions.Decode(Card.Load(f)).MaterialEditor; decoded++; }
            catch (Exception) { }
            var n = Names(me).ToList();
            if (n.Count == 0) continue;
            if (string.IsNullOrEmpty(only)) targets.Add(f);
            if (targets.Contains(f)) names.UnionWith(n);
        }
        Info($"cards decoded: {decoded} of {all.Count} in {dsw.Elapsed.TotalSeconds:F1} s; targets {targets.Count}; their names through ShaderMap: {string.Join(", ", names)}");
        Check("MS3 there are cards to build", targets.Count > 0);
        if (targets.Count == 0) return;

        var resolve0 = names.ToDictionary(n => n, n => Resolve(cat, n), StringComparer.Ordinal);
        var copies0 = ProcessCopies(names);
        var first = BuildAll(h, targets, "first");

        // other copies of the names into the process: the targets again (reverse), the -poison cards, the -poisonAsset prefabs
        int poisonBuilt = 0;
        var poison = targets.AsEnumerable().Reverse().ToList();
        string pc = Arg("-poison");
        if (!string.IsNullOrEmpty(pc))
        {
            var want = new HashSet<string>(pc.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
            poison.AddRange(all.Where(f => want.Contains(Path.GetFileName(f))));
        }
        var keep = new List<UnityEngine.Object>();
        foreach (var f in poison)
        {
            var b = h.Build(f);
            if (b.Ok) poisonBuilt++;
            b.Dispose();
        }
        foreach (var spec in (Arg("-poisonAsset") ?? "").Split(';').Select(x => x.Trim()).Where(x => x.Length > 0))
        {
            var t = spec.Split('|');
            if (t.Length != 3) { Check("MS3 -poisonAsset entry guid|bundle|asset", false, spec); continue; }
            cat.ZipmodsByGuid.TryGetValue(t[0], out var z);
            var go = cat.LoadAssetAt<GameObject>(z, t[1], null, t[2]);
            Info($"poison asset {spec}: {(go ? "loaded" : "NOT LOADED")}");
            if (go) keep.Add(go);
        }
        var copies1 = ProcessCopies(names);
        int gained = 0;
        foreach (var n in names)
        {
            copies0.TryGetValue(n, out var a); copies1.TryGetValue(n, out var c);
            if (c.Count > a.Count) gained++;
            Info($"process copies of {n} before the first builds -> before the second: {a.Count} -> {c.Count} [{string.Join(", ", c.Props ?? new List<int>())}] (property counts)");
        }
        Info($"poison: {poisonBuilt}/{poison.Count} cards built");
        Check("MS3 the second builds meet more copies of the names in the process than the first", gained > 0, $"{gained} of {names.Count} names gained copies");

        var second = BuildAll(h, targets.AsEnumerable().Reverse().ToList(), "after the poisoning");
        int equal = 0;
        foreach (var f in targets)
        {
            first.TryGetValue(f, out var a); second.TryGetValue(f, out var c);
            bool eq = a.Sha != null && a.Sha == c.Sha;
            if (eq) equal++;
            Info($"{Path.GetFileName(f)}: {(eq ? "equal" : "DIFFERS")}; me {a.Me}{(eq ? "" : " -> " + c.Me)}");
            if (!eq && a.Lines != null && c.Lines != null)
                foreach (var l in a.Lines.Except(c.Lines).Take(4)) Info("   first only: " + Short(l, 300));
        }
        Check("MS3 every card's snapshot after the poisoning (reverse order) = its first build", equal == targets.Count, $"{equal}/{targets.Count}");
        int sameResolve = 0;
        foreach (var n in names)
        {
            var r1 = Resolve(cat, n);
            if (r1 == resolve0[n]) sameResolve++;
            else Info($"ResolveShader {n}: {resolve0[n]} -> {r1}");
            Info($"resolve {n}: {r1}");
        }
        Check("MS3 ResolveShader of every name gives the same shader, replacement and path before and after", sameResolve == names.Count, $"{sameResolve}/{names.Count}");
        GC.KeepAlive(keep);
    }

    static string Resolve(ContentCatalog cat, string name)
    {
        var sh = MaterialEditorApplier.ResolveShader(name, cat, out bool direct);
        var orig = MaterialEditorShaders.Of(cat).Original(name);
        string repl = ShaderMap.TryResolve(name, orig, out var r) ? r : "-";
        return $"{(sh ? sh.name : "null")} ({(sh ? sh.GetPropertyCount() : 0)} properties){(direct ? " direct" : "")} replacement {repl}";
    }

    struct Copies { public int Count; public List<int> Props; }

    // distinct shader objects of each name loaded in the process, with their property counts
    static Dictionary<string, Copies> ProcessCopies(IEnumerable<string> names)
    {
        var set = new HashSet<string>(names, StringComparer.Ordinal);
        var d = new Dictionary<string, Copies>(StringComparer.Ordinal);
        foreach (var s in Resources.FindObjectsOfTypeAll<Shader>())
        {
            if (!s || !set.Contains(s.name)) continue;
            if (!d.TryGetValue(s.name, out var c)) c = new Copies { Props = new List<int>() };
            c.Count++;
            c.Props.Add(s.GetPropertyCount());
            d[s.name] = c;
        }
        return d;
    }

    struct Result { public string Sha, Me; public List<string> Lines; }

    static Dictionary<string, Result> BuildAll(MakerCoreTest.Harness h, List<string> cards, string what)
    {
        var d = new Dictionary<string, Result>(StringComparer.OrdinalIgnoreCase);
        int ok = 0;
        foreach (var f in cards)
        {
            MakerCoreTest.Built b = null;
            try
            {
                b = h.Build(f);
                if (!b.Ok) { Check($"MS3 {Path.GetFileName(f)} built ({what})", false, b.Error); continue; }
                var s = b.Snapshot();
                d[f] = new Result { Sha = s.Sha256, Me = MePart(b.Context.Stats.Summary()), Lines = s.Lines };
                ok++;
            }
            finally { b?.Dispose(); }
        }
        Info($"built {ok}/{cards.Count} ({what})");
        return d;
    }

    static readonly Regex MeSummarySnap = new Regex(@";_me_[^;]*", RegexOptions.CultureInvariant);

    /// <summary>The MaterialEditor part of an extension summary ("me a/b (shader swaps ...) ..."), or "-".</summary>
    static string MePart(string summary)
    {
        if (summary == null) return "-";
        int i = summary.IndexOf("; me ", StringComparison.Ordinal);
        return i < 0 ? "-" : summary.Substring(i + 2);
    }

    // ================================================================ RunLines (review aid)

    /// <summary>
    /// -cards a.png,b.png: each card built with the per-asset redirect off (as MakerClothesTest.RunS1Explain builds, against
    /// the S1 baselines) and every snapshot line that differs from its baseline printed with its class (clothes classes,
    /// me.shaderRegistry, other). Report: Spike/out/maker/material_editor_shader_lines.txt.
    /// </summary>
    public static void RunLines()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        var want = new HashSet<string>((Arg("-cards") ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
        var cards = MakerCoreTest.AllCards().Where(f => want.Contains(Path.GetFileName(f))).ToList();
        bool r0 = ContentCatalog.SideloaderAssetRedirect;
        int differ = 0, other = 0;
        try
        {
            using (var h = MakerCoreTest.Harness.Open())
            {
                ContentCatalog.SideloaderAssetRedirect = false;
                h.Catalog.UnloadAll();
                Rep.AppendLine($"{cards.Count} of {want.Count} named cards; redirect off; baselines {MakerCoreTest.BaselineDir}");
                foreach (var f in cards)
                {
                    MakerCoreTest.Built b = null;
                    try
                    {
                        b = h.Build(f);
                        if (!b.Ok) { Check(Path.GetFileName(f) + " built", false, b.Error); continue; }
                        var basis = MakerCoreTest.LoadBaseline(b.CardSha);
                        var now = b.Snapshot();
                        if (basis == null) { Info(Path.GetFileName(f) + ": no baseline"); continue; }
                        Info($"{Path.GetFileName(f)}: {(basis.Sha256 == now.Sha256 ? "equal" : "DIFFERS")}; me {MePart(b.Context.Stats.Summary())}");
                        if (basis.Sha256 == now.Sha256) continue;
                        differ++;
                        var paths = MakerClothesTest.SnapPaths(b.Root.transform);
                        var a = Keyed(basis.Lines); var c = Keyed(now.Lines);
                        foreach (var kv in a.Concat(c.Where(x => !a.ContainsKey(x.Key))))
                        {
                            a.TryGetValue(kv.Key, out var la); c.TryGetValue(kv.Key, out var lc);
                            if (la == lc) continue;
                            string k = MakerClothesTest.ClothesClass(b.Context, paths, kv.Key, la, lc) ?? RegistryClass(b.Context, paths, a, c, kv.Key, la, lc) ?? "other";
                            if (k == "other") other++;
                            Info($"   [{k}] {Short(kv.Key, 200)}{(la != null && lc != null ? "  differing: " + Short(string.Join(" ", Tokens(la).Except(Tokens(lc))) + " -> " + string.Join(" ", Tokens(lc).Except(Tokens(la))), 600) : "")}");
                            Info($"      4.3: {Short(la, 700)}");
                            Info($"      now: {Short(lc, 700)}");
                        }
                    }
                    finally { b?.Dispose(); }
                }
            }
        }
        finally { ContentCatalog.SideloaderAssetRedirect = r0; }
        Check("lines: every differing line classified", other == 0, $"{differ} cards differ, other {other} lines");
        Finish("material_editor_shader_lines.txt", "MaterialEditorShaderTest.RunLines (the differing S1 lines of named cards)", sw, "ME SHADER LINES");
    }

    // the space-separated tokens of a snapshot line outside parentheses
    static List<string> Tokens(string s)
    {
        var l = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i <= s.Length; i++)
        {
            char ch = i < s.Length ? s[i] : ' ';
            if (ch == '(') depth++;
            else if (ch == ')') depth = Math.Max(0, depth - 1);
            else if (ch == ' ' && depth == 0) { if (i > start) l.Add(s.Substring(start, i - start)); start = i + 1; }
        }
        return l;
    }

    static Dictionary<string, string> Keyed(List<string> lines)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        var n = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var x in lines)
        {
            int p1 = x.IndexOf('|'); int p2 = p1 < 0 ? -1 : x.IndexOf('|', p1 + 1);
            string k = p2 < 0 ? x : x.Substring(0, p2);
            int i = n.TryGetValue(k, out var v) ? v + 1 : 0;
            n[k] = i;
            d[i == 0 ? k : k + "#" + i] = x;
        }
        return d;
    }

    // ================================================================ S1 explain class

    /// <summary>
    /// "me.shaderRegistry" when a differing S1 line is one a MaterialEditor shader edit through ShaderMap writes, else null:
    /// the card has such an edit (a shader name ShaderMap replaces) and the line is (1) the extension summary, equal once
    /// its MaterialEditor part is removed; (2) a missing-content line, when the records other than the "me.shader" records
    /// of those names are the same in both snapshots; (3) a material line of a renderer of an object such an edit targets,
    /// whose material carries the edit's material name. <paramref name="basis"/> / <paramref name="now"/>: both snapshots'
    /// lines keyed as the explain keys them.
    /// </summary>
    public static string RegistryClass(CharaContext ctx, Dictionary<Transform, string> paths, IDictionary<string, string> basis, IDictionary<string, string> now,
                                       string key, string before, string after)
    {
        var edits = ctx?.Ext?.MaterialEditor?.Shaders?.Where(s => s.ShaderName != null && (ShaderMap.Fitted.ContainsKey(s.ShaderName) || ShaderMap.CopyRoutes.ContainsKey(s.ShaderName))).ToList();
        if (edits == null || edits.Count == 0) return null;
        if (key == "ctx|ext")
            return before != null && after != null && MeSummarySnap.Replace(before, "") == MeSummarySnap.Replace(after, "") ? ClassRegistry : null;
        if (key.StartsWith("ctx|missing[", StringComparison.Ordinal))
        {
            var mine = new HashSet<string>(edits.Select(e => "me.shader_" + e.ShaderName.Replace(' ', '_')), StringComparer.Ordinal);
            bool Mine(string line) => line != null && mine.Any(m => line.IndexOf("|" + m + "_(", StringComparison.Ordinal) >= 0);
            if (!Mine(before) && !Mine(after)) return null;
            List<string> Rest(IDictionary<string, string> d) =>
                d.Where(kv => kv.Key.StartsWith("ctx|missing[", StringComparison.Ordinal)).OrderBy(kv => kv.Key, StringComparer.Ordinal)
                 .Select(kv => kv.Value).Where(v => !Mine(v)).Select(v => v.Substring(v.IndexOf('|', v.IndexOf('|') + 1) + 1)).ToList();
            return Rest(basis).SequenceEqual(Rest(now)) ? ClassRegistry : null;
        }
        if (!key.StartsWith("mat|", StringComparison.Ordinal)) return null;
        string rend = key.Substring(4);
        int br = rend.LastIndexOf('[');
        if (br <= 0 || !int.TryParse(rend.Substring(br + 1).TrimEnd(']'), NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot)) return null;
        rend = rend.Substring(0, br);
        foreach (var e in edits)
        {
            if ((e.ObjectType == OT.Clothing || e.ObjectType == OT.Accessory) && e.CoordinateIndex != 0) continue;
            foreach (var r in Targets(ctx, e.ObjectType, e.Slot))
            {
                if (!paths.TryGetValue(r.transform, out var p) || p != rend) continue;
                var mats = r.sharedMaterials;
                if (slot < mats.Length && mats[slot] && MaterialEditorApplier.Fmt(mats[slot].name) == e.MaterialName) return ClassRegistry;
            }
        }
        return null;
    }

    // the renderers of an ME object (as the applier finds them)
    static IEnumerable<Renderer> Targets(CharaContext ctx, OT type, int slot)
    {
        GameObject go = null;
        switch (type)
        {
            case OT.Clothing: go = slot >= 0 && slot < ctx.Clothes.Length ? ctx.Clothes[slot] : null; break;
            case OT.Accessory: go = ctx.AccessoryObject(slot); break;
            case OT.Hair: go = slot >= 0 && slot < ctx.Hair.Length ? ctx.Hair[slot] : null; break;
            case OT.Character:
                if (!ctx.Root) return Enumerable.Empty<Renderer>();
                var clothes = new HashSet<Transform>(ctx.Clothes.Where(c => c).Select(c => c.transform));
                return ctx.Root.GetComponentsInChildren<Renderer>(true)
                    .Where(r => MaterialEditorApplier.BodyParts.Contains(r.name) && !Under(r.transform, clothes));
        }
        return go ? go.GetComponentsInChildren<Renderer>(true) : Enumerable.Empty<Renderer>();
    }

    static bool Under(Transform t, HashSet<Transform> roots)
    {
        for (var p = t; p; p = p.parent) if (roots.Contains(p)) return true;
        return false;
    }
}
