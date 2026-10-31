// Skin-family shader work (Phase 1.3): inventory of the skin materials on assembled characters,
// black-box probes of the original shaders, grid fit of the URP replacements, and a real-asset
// comparison on an assembled character (only the target shader's renderers drawn).
// Run:
//   -executeMethod SkinFit.Inventory                       -> Spike/out/fit/skin_inventory.txt
//   -executeMethod SkinFit.Inventory -invShaders "A;B" -invOut face_parts_inventory.txt  (other families)
//   -executeMethod SkinFit.Probe -fitShader "AIT/Skin True Face"
//   -executeMethod SkinFit.Sweep -fitShader "AIT/Skin True Face" -sweepFile <cases.txt> [-fitPipe builtin|urp|both]
//   -executeMethod SkinFit.Real  [-fitShader "A;B"] [-card <png>] [-limit N] [-shadows] [-realSet "a=v;b=v"] [-realSetFor "A;B"]
//                                [-realOut <name>] [-bakeStatic] [-realFallback]
//                                                          -> Spike/out/fit/skin_real*.txt (or <name>.txt)
//   -executeMethod SkinFit.MatProps -fitShader <name>      -> <shader>_matprops.txt (saved properties, also undeclared ones)
//   -executeMethod SkinFit.ScanStudioItems                 -> scan_studio_items.txt (hair-like studio items and their shaders)
// Common: -hairType 0|1 overrides the cards' hair shader type; -fitItem "bundle:prefab" uses a studio item instead of
// the cards; Sweep extras -fitPattern / -fitStats / -fitDump (per-cell coverage, statistics, full images).
// The original shader is taken from an assembled character (first card unless -card).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Chara;
using HS2Next.Content;
using HS2Next.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class SkinFit
{
    public static readonly string[] Family = { "AIT/Skin True", "AIT/Skin True Face", "AIT/Skin Translucency", "AIT/Skin Translucency simple" };
    // The output folder, created when first used (a fresh checkout has no Spike/out).
    static string FitDir { get { var d = Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "fit"); Directory.CreateDirectory(d); return d; } }
    // Arguments: the command line, or one line of a Jobs file while that job runs.
    static string[] jobArgs;
    static string[] Args => jobArgs ?? Environment.GetCommandLineArgs();
    static bool Flag(string n) => Args.Contains(n);
    static string Arg(string name, string def)
    {
        var a = Args;
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    // Jobs -jobFile <txt>: several SkinFit methods in one Unity session. Each non-comment line is
    // "<Method> <args...>" with the arguments as on the command line ("..." quotes a value with spaces); a line's
    // arguments replace the command line's for that job (-drawerKw too). A failing job is logged and the next one runs.
    // Progress in Spike/out/fit/jobs_<file name>.log.
    public static void Jobs()
    {
        string file = Arg("-jobFile", null);
        Directory.CreateDirectory(FitDir);
        var log = new StringBuilder();
        string logPath = Path.Combine(FitDir, "jobs_" + Path.GetFileNameWithoutExtension(file) + ".log");
        foreach (var raw in File.ReadAllLines(file))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            var toks = new List<string>(); var cur = new StringBuilder(); bool q = false, any = false;
            foreach (char ch in line)
            {
                if (ch == '"') { q = !q; any = true; continue; }
                if (ch == ' ' && !q) { if (any || cur.Length > 0) toks.Add(cur.ToString()); cur.Clear(); any = false; continue; }
                cur.Append(ch);
            }
            if (any || cur.Length > 0) toks.Add(cur.ToString());
            var mi = typeof(SkinFit).GetMethod(toks[0], System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            jobArgs = new[] { "Unity" }.Concat(toks.Skip(1)).ToArray();
            try
            {
                if (mi == null) throw new Exception("no method " + toks[0]);
                ResetStatics();
                mi.Invoke(null, null);
                log.AppendLine($"OK   {sw.Elapsed.TotalSeconds,6:F1} s  {line}");
            }
            catch (Exception e)
            {
                log.AppendLine($"FAIL {sw.Elapsed.TotalSeconds,6:F1} s  {line}\n     {(e.InnerException ?? e).GetType().Name}: {(e.InnerException ?? e).Message}");
                Debug.LogException(e.InnerException ?? e);
                try { gd?.UnloadAll(); } catch { }
                gd = null; studioDb = null; ShaderFit.SetPipeline(null);
            }
            finally { jobArgs = null; HS2Next.MaterialCopies.KeepSerializedKeywords = true; }
            File.WriteAllText(logPath, log.ToString());
        }
        Debug.Log("SKINFIT JOBS DONE");
    }

    static void ResetStatics()
    {
        patterns.Clear(); pixels.Clear(); dumps.Clear(); drawerKwCache.Clear(); curSrc = null;
    }
    static string Safe(string s) => string.Concat(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
    static string Fl(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ characters
    static ContentCatalog gd;
    static List<(string name, GameObject go)> BuildCharas(int limit)
    {
        ShaderUtil.allowAsyncCompilation = false;   // otherwise first renders use the cyan placeholder
        HS2Next.MaterialCopies.KeepSerializedKeywords = !Flag("-drawerKw");   // -drawerKw: Phase 1 copies (drawer keywords)
        gd = CardResolveTest.BuildDb(null);
        var cards = Arg("-card", null) is string one ? new[] { one }
            : Directory.GetFiles(Path.Combine(CardResolveTest.GameRoot, @"UserData\chara\female"), "*.png").Take(limit).ToArray();
        var res = new List<(string, GameObject)>();
        foreach (var p in cards)
        {
            var card = Card.Load(p);
            // -hairType 0|1: override the hair shader type in the imported documents (0 dithering hair_07, 1 cutout hair_08).
            string ht = Arg("-hairType", null);
            var asm = new CharaAssembler(gd);
            res.Add((card.Name, asm.BuildFromDocuments(p, (chara, coord) => { if (ht != null && chara?.Hair != null) chara.Hair.ShaderType = int.Parse(ht); })));
        }
        return res;
    }

    // -fitItem "studio/xx.unity3d:prefab": an abdata prefab instantiated as an extra object (studio items);
    // "id:<no>": a vanilla Studio item by its list number, loaded through the catalog as ItemRealization does.
    static HS2Next.Content.Studio.StudioDb studioDb;
    static HS2Next.Content.Studio.StudioDb StudioLists => studioDb = studioDb ?? (gd = gd ?? CardResolveTest.BuildDb(null)).LoadStudioLists();
    static GameObject LoadItem(string spec)
    {
        if (string.IsNullOrEmpty(spec)) return null;
        gd = gd ?? CardResolveTest.BuildDb(null);
        if (spec.StartsWith("id:"))
        {
            var row = StudioLists.Items.Get(ContentId.Vanilla(int.Parse(spec.Substring(3))));
            var pf = row != null ? gd.LoadAsset<GameObject>(row) : null;
            if (!pf) { Debug.LogWarning("item not found " + spec); return null; }
            var ig = Object.Instantiate(pf);
            ig.name = $"{row.No}_{pf.name}";
            return ig;
        }
        int k = spec.LastIndexOf(':');
        var prefab = gd.LoadAsset<GameObject>(spec.Substring(0, k), spec.Substring(k + 1));
        // a bundle that only a zipmod ships: resolved like a Studio record's (the shipping zipmod)
        if (!prefab) prefab = gd.LoadAssetAt<GameObject>(null, spec.Substring(0, k), null, spec.Substring(k + 1));
        if (!prefab) { Debug.LogWarning("item not found " + spec); return null; }
        var go = Object.Instantiate(prefab);
        go.name = spec.Substring(k + 1);
        return go;
    }

    // Several items: "a;b;c" (each "bundle:prefab" or "id:<no>"); the ones that load, in order.
    static List<GameObject> LoadItems(string specs) => string.IsNullOrEmpty(specs) ? new List<GameObject>()
        : specs.Split(';').Where(x => x.Trim().Length > 0).Select(x => LoadItem(x.Trim())).Where(g => g).ToList();

    static void Cleanup(IEnumerable<GameObject> gos)
    {
        foreach (var g in gos) if (g) Object.DestroyImmediate(g);
        gd?.UnloadAll(); gd = null; studioDb = null;
        ShaderFit.SetPipeline(null);
    }

    static string PathOf(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (; t && t != root; t = t.parent) parts.Add(t.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    static string InactiveAncestor(Transform t, Transform root)
    {
        for (; t && t != root; t = t.parent) if (!t.gameObject.activeSelf) return t.name + " inactive";
        return "root inactive";
    }

    static string Val(Material m, Shader sh, int i)
    {
        string n = sh.GetPropertyName(i);
        switch (sh.GetPropertyType(i))
        {
            case ShaderPropertyType.Texture:
                var t = m.GetTexture(n);
                var st = m.GetTextureScale(n); var of = m.GetTextureOffset(n);
                string tiling = st != Vector2.one || of != Vector2.zero ? $" st({Fl(st.x)},{Fl(st.y)},{Fl(of.x)},{Fl(of.y)})" : "";
                return t ? $"{t.name} [{t.width}x{t.height} {(t is RenderTexture ? "RT" : t.GetType().Name)}]{tiling}" : "null" + tiling;
            case ShaderPropertyType.Color:
                var c = m.GetColor(n); return $"({Fl(c.r)},{Fl(c.g)},{Fl(c.b)},{Fl(c.a)})";
            case ShaderPropertyType.Vector:
                var v = m.GetVector(n); return $"({Fl(v.x)},{Fl(v.y)},{Fl(v.z)},{Fl(v.w)})";
            case ShaderPropertyType.Int: return m.GetInteger(n).ToString();
            default: return Fl(m.GetFloat(n));
        }
    }

    public static string DescribeShader(Shader sh)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### shader {sh.name}: renderQueue {sh.renderQueue}, subshaders {sh.subshaderCount}");
        ShaderData sd = null;
        try { sd = ShaderUtil.GetShaderData(sh); } catch { }
        for (int s = 0; s < sh.subshaderCount; s++)
        {
            sb.AppendLine($"  subshader {s}: RenderType={sh.FindSubshaderTagValue(s, new ShaderTagId("RenderType")).name} Queue={sh.FindSubshaderTagValue(s, new ShaderTagId("Queue")).name}");
            for (int p = 0; p < sh.GetPassCountInSubshader(s); p++)
            {
                string pn = "";
                try { var pd = sd?.GetSubshader(s)?.GetPass(p); if (pd != null) pn = $" Name=\"{pd.Name}\"{(pd.IsGrabPass ? " GRAB" : "")}"; } catch { }
                sb.AppendLine($"    pass {p}: LightMode={sh.FindPassTagValue(s, p, new ShaderTagId("LightMode")).name}{pn}");
            }
        }
        sb.AppendLine("  keywords: " + string.Join(" ", sh.keywordSpace.keywords.Select(k => $"{k.name}({k.type})")));
        sb.AppendLine("  properties (name type default [range] \"description\" attributes):");
        for (int i = 0; i < sh.GetPropertyCount(); i++)
        {
            var t = sh.GetPropertyType(i);
            string def = t == ShaderPropertyType.Texture ? sh.GetPropertyTextureDefaultName(i)
                : t == ShaderPropertyType.Color || t == ShaderPropertyType.Vector ? sh.GetPropertyDefaultVectorValue(i).ToString("F3")
                : sh.GetPropertyDefaultFloatValue(i).ToString("F3");
            string range = t == ShaderPropertyType.Range ? $" [{sh.GetPropertyRangeLimits(i).x}..{sh.GetPropertyRangeLimits(i).y}]" : "";
            var attrs = sh.GetPropertyAttributes(i);
            var flags = sh.GetPropertyFlags(i);
            sb.AppendLine($"    {sh.GetPropertyName(i)} {t} {def}{range} \"{sh.GetPropertyDescription(i)}\"{(attrs.Length > 0 ? " [" + string.Join(",", attrs) + "]" : "")}{(flags != ShaderPropertyFlags.None ? " flags " + flags : "")}");
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ inventory
    public static void Inventory()
    {
        Directory.CreateDirectory(FitDir);
        var charas = BuildCharas(int.Parse(Arg("-limit", "3")));
        // -fitItem "bundle:prefab": also inventory a studio item prefab (e.g. a hair-type studio item).
        foreach (var itemGo in LoadItems(Arg("-fitItem", null))) charas.Add(("item " + itemGo.name, itemGo));
        // -invShaders "A;B": exact shader names to describe (default: the AIT/Skin* family).
        var only = Arg("-invShaders", null)?.Split(';');
        bool Want(string shn) => only != null ? only.Contains(shn) : shn.StartsWith("AIT/Skin");
        string outName = Arg("-invOut", "skin_inventory.txt");
        var sb = new StringBuilder($"# Inventory of [{(only != null ? string.Join(", ", only) : "AIT/Skin*")}] (assembled characters, after CharaAssembler.Build)\n");
        var shaders = new Dictionary<string, Shader>();
        var allShaders = new SortedDictionary<string, int>();
        foreach (var (name, go) in charas)
        {
            sb.AppendLine($"\n== card {name}");
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int s = 0; s < mats.Length; s++)
                {
                    var m = mats[s];
                    string shn = m && m.shader ? m.shader.name : "(null)";
                    allShaders[shn] = allShaders.TryGetValue(shn, out var k) ? k + 1 : 1;
                    if (!m || !m.shader || !Want(shn)) continue;
                    shaders[shn] = m.shader;
                    bool vis = r.enabled && r.gameObject.activeInHierarchy;
                    string inactive = vis ? "" : !r.enabled ? ", renderer disabled" : $", inactive ({InactiveAncestor(r.transform, go.transform)})";
                    sb.AppendLine($"-- {PathOf(r.transform, go.transform)} slot {s}/{mats.Length} [{r.GetType().Name}{inactive}]");
                    sb.AppendLine($"   material {m.name}  shader {shn}  queue {m.renderQueue}  RenderType {m.GetTag("RenderType", true)}  keywords [{string.Join(" ", m.shaderKeywords)}]");
                    var bb = r.bounds; sb.AppendLine($"   bounds centre {bb.center.ToString("F3")} size {bb.size.ToString("F3")}, layer {r.gameObject.layer}, shadows {r.shadowCastingMode}/{r.receiveShadows}");
                    var mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh)
                        sb.AppendLine($"   mesh {mesh.name}: {mesh.vertexCount} verts, submeshes {mesh.subMeshCount}, channels " +
                            string.Join(" ", Enumerable.Range(0, 8).Where(u => mesh.HasVertexAttribute(VertexAttribute.TexCoord0 + u)).Select(u => "uv" + u)) +
                            (mesh.HasVertexAttribute(VertexAttribute.Color) ? " color" : "") + (mesh.HasVertexAttribute(VertexAttribute.Tangent) ? " tangent" : ""));
                    if (mesh && mesh.isReadable)
                    {
                        var uv0 = new List<Vector2>(); mesh.GetUVs(0, uv0);
                        if (uv0.Count > 0) sb.AppendLine($"   uv0 u {uv0.Min(v => v.x):F3}..{uv0.Max(v => v.x):F3} v {uv0.Min(v => v.y):F3}..{uv0.Max(v => v.y):F3}");
                        var uv1 = new List<Vector2>(); mesh.GetUVs(1, uv1);
                        if (uv1.Count > 0) sb.AppendLine($"   uv1 u {uv1.Min(v => v.x):F3}..{uv1.Max(v => v.x):F3} v {uv1.Min(v => v.y):F3}..{uv1.Max(v => v.y):F3}");
                        var cols = mesh.colors;
                        if (cols.Length > 0) sb.AppendLine($"   vertex colour min ({cols.Min(c => c.r):F3},{cols.Min(c => c.g):F3},{cols.Min(c => c.b):F3},{cols.Min(c => c.a):F3}) mean ({cols.Average(c => c.r):F3},{cols.Average(c => c.g):F3},{cols.Average(c => c.b):F3},{cols.Average(c => c.a):F3}) max ({cols.Max(c => c.r):F3},{cols.Max(c => c.g):F3},{cols.Max(c => c.b):F3},{cols.Max(c => c.a):F3})");
                        var tans = mesh.tangents; var nrm = mesh.normals;
                        if (tans.Length > 0) sb.AppendLine($"   tangent.w values {string.Join(",", tans.Select(t => t.w).Distinct().Take(4))}; mean |dot(n,t)| {Enumerable.Range(0, tans.Length).Average(i => Mathf.Abs(Vector3.Dot(nrm[i].normalized, ((Vector3)tans[i]).normalized))):F3}");
                    }
                    else if (mesh) sb.AppendLine("   (mesh not readable)");
                    sb.AppendLine("   other slots: " + string.Join(", ", mats.Select((x, j) => j == s ? null : $"{j}:{(x ? x.name + " (" + x.shader.name + ")" : "null")}").Where(x => x != null)));
                    for (int i = 0; i < m.shader.GetPropertyCount(); i++)
                        sb.AppendLine($"     {m.shader.GetPropertyName(i)} = {Val(m, m.shader, i)}");
                }
            }
        }
        sb.AppendLine("\n== all shaders on the 3 characters (material slots)");
        foreach (var kv in allShaders) sb.AppendLine($"  {kv.Value,4}  {kv.Key}{(HS2Next.ShaderMap.Fitted.ContainsKey(kv.Key) ? "  [fitted]" : "")}");
        sb.AppendLine("\n== shader descriptions");
        foreach (var sh in shaders.Values) sb.AppendLine(DescribeShader(sh));
        // Prefab-side skin materials replaced at load (body slot 0 `..._create`).
        var bodyPrefab = gd.LoadAsset<GameObject>("chara/oo_base.unity3d", "p_cf_body_00");
        if (bodyPrefab)
        {
            sb.AppendLine("== prefab p_cf_body_00 renderers (before assembly)");
            foreach (var r in bodyPrefab.GetComponentsInChildren<Renderer>(true))
                sb.AppendLine($"  {PathOf(r.transform, bodyPrefab.transform)}: " + string.Join(", ", r.sharedMaterials.Select(x => x ? $"{x.name} ({x.shader.name})" : "null")));
        }
        File.WriteAllText(Path.Combine(FitDir, outName), sb.ToString().Replace("\r\n", "\n"));
        Cleanup(charas.Select(c => c.go));
        Debug.Log("SKINFIT DONE");
    }

    // ScanBundles -scanDir studio [-scanShader "Shader Forge/studio_hair_07"] [-scanMax N]: shader names of the
    // materials in abdata/<dir>/*.unity3d (one bundle at a time) and the prefabs whose renderers use -scanShader
    // -> Spike/out/fit/scan_<dir>.txt
    public static void ScanBundles()
    {
        ShaderUtil.allowAsyncCompilation = false;
        gd = CardResolveTest.BuildDb(null);
        string dir = Arg("-scanDir", "studio"), want = Arg("-scanShader", "Shader Forge/studio_hair_07");
        var wants = want.Split(';');   // several shaders: "A;B"
        int max = int.Parse(Arg("-scanMax", "8"));
        var counts = new SortedDictionary<string, int>();
        var sb = new StringBuilder($"# materials in abdata/{dir}/*.unity3d; prefabs using {want}\n");
        var files = Directory.GetFiles(Path.Combine(CardResolveTest.GameRoot, "abdata", dir), "*.unity3d");
        sb.AppendLine($"# {files.Length} bundles in {Path.Combine(CardResolveTest.GameRoot, "abdata", dir)}");
        foreach (var f in files)
        {
            string rel = dir + "/" + Path.GetFileName(f);
            var b = gd.LoadAbdataBundle(rel);
            if (!b) { sb.AppendLine($"{rel}: not loaded"); continue; }
            var mats = b.LoadAllAssets<Material>();
            int hits = 0;
            foreach (var m in mats)
            {
                string shn = m.shader ? m.shader.name : "(null)";
                counts[shn] = counts.TryGetValue(shn, out var k) ? k + 1 : 1;
                if (wants.Contains(shn)) hits++;
            }
            sb.AppendLine($"{rel}: {mats.Length} materials, {hits} with {want}");
            if (hits > 0)
            {
                int shown = 0;
                foreach (var go in b.LoadAllAssets<GameObject>())
                {
                    if (go.transform.parent) continue;
                    var rs = go.GetComponentsInChildren<Renderer>(true).Where(r => r.sharedMaterials.Any(m => m && m.shader && wants.Contains(m.shader.name))).ToArray();
                    if (rs.Length == 0) continue;
                    sb.AppendLine($"   prefab {go.name}: " + string.Join(", ", rs.Select(r => $"{r.name}[{string.Join("/", r.sharedMaterials.Select(m => m ? m.name + "(" + m.shader.name + ")" : "null"))}]")));
                    if (++shown >= max) break;
                }
            }
            gd.UnloadBundle(b);
        }
        sb.AppendLine("\n== shader counts");
        foreach (var kv in counts) sb.AppendLine($"  {kv.Value,5}  {kv.Key}");
        File.WriteAllText(Path.Combine(FitDir, "scan_" + Safe(dir) + ".txt"), sb.ToString().Replace("\r\n", "\n"));
        gd.UnloadAll(); gd = null;
        Debug.Log("SKINFIT DONE");
    }

    // ScanStudioItems [-scanShader "Shader Forge/studio_hair_07"] [-scanMax N]: vanilla studio items whose name mentions
    // hair (髪 / ヘア / hair), their prefab renderers' shaders -> Spike/out/fit/scan_studio_items.txt
    public static void ScanStudioItems()
    {
        ShaderUtil.allowAsyncCompilation = false;
        gd = CardResolveTest.BuildDb(null);
        string want = Arg("-scanShader", "Shader Forge/studio_hair_07");
        int max = int.Parse(Arg("-scanMax", "40"));
        var db = gd.LoadStudioLists();
        var sb = new StringBuilder($"# vanilla studio items named like hair; shaders of their renderers (want {want})\n");
        int n = 0;
        foreach (var it in db.Items.All.Where(i => i.IsVanilla && i.Name != null && (i.Name.Contains("髪") || i.Name.Contains("ヘア") || i.Name.ToLowerInvariant().Contains("hair"))))
        {
            if (n++ >= max) break;
            var go = gd.LoadAsset<GameObject>(it);
            if (!go) { sb.AppendLine($"{it}: prefab not loaded"); continue; }
            var rs = go.GetComponentsInChildren<Renderer>(true);
            sb.AppendLine($"{it}: " + string.Join(", ", rs.Select(r => $"{r.name}[{string.Join("/", r.sharedMaterials.Select(m => m ? m.name + "(" + (m.shader ? m.shader.name : "null") + ", q" + m.renderQueue + ")" : "null"))}]")));
        }
        File.WriteAllText(Path.Combine(FitDir, "scan_studio_items.txt"), sb.ToString().Replace("\r\n", "\n"));
        gd.UnloadAll(); gd = null;
        Debug.Log("SKINFIT DONE");
    }

    // ScanItems [-scanShader "A;B"] [-scanOut name]: every vanilla Studio item (catalog rows, loaded as ItemRealization does):
    // number, group / category names, item name, bundle:prefab, ItemComponent flags, and per renderer slot the material,
    // shader, queue and loaded keywords; then per shader the items using it (all shaders, or -scanShader)
    // -> Spike/out/fit/<scanOut, default scan_items>.txt. Headless is fine (-nographics).
    public static void ScanItems()
    {
        ShaderUtil.allowAsyncCompilation = false;
        gd = CardResolveTest.BuildDb(null);
        var db = StudioLists;
        var wants = Arg("-scanShader", null)?.Split(';');
        var sb = new StringBuilder("# vanilla Studio items: no | group / category | name | bundle:prefab | ItemComponent | renderer[slot] material (shader, queue, keywords)\n");
        var perShader = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        var matCount = new Dictionary<string, int>();
        string lastBundle = null; int n = 0;
        foreach (var it in db.Items.All.Where(i => i.IsVanilla).OrderBy(i => i.BundlePath, StringComparer.Ordinal).ThenBy(i => i.No))
        {
            if (it.BundlePath != lastBundle) { if (++n % 20 == 0) { gd.UnloadAll(); EditorUtility.UnloadUnusedAssetsImmediate(); } lastBundle = it.BundlePath; }
            db.TryGetItemGroup(it.Group, out var grp); db.TryGetItemCategory(it.Group, it.Category, out var cat);
            string head = $"{it.No} | {it.Group} {grp?.Name} / {it.Category} {cat?.Name} | {it.Name} | {it.BundlePath}:{it.FileName}";
            var go = gd.LoadAsset<GameObject>(it);
            if (!go) { sb.AppendLine(head + " | prefab not loaded"); continue; }
            var ic = go.GetComponent<Studio.ItemComponent>();
            string icS = ic == null ? "no ItemComponent" : $"IC info {ic.info?.Count ?? 0} rend {ic.rendererInfos?.Count ?? 0} alpha {Fl(ic.alpha)} glass ({Fl(ic.defGlass.r)},{Fl(ic.defGlass.g)},{Fl(ic.defGlass.b)},{Fl(ic.defGlass.a)}) em ({Fl(ic.defEmissionColor.r)},{Fl(ic.defEmissionColor.g)},{Fl(ic.defEmissionColor.b)}) x{Fl(ic.defEmissionStrength)} lc {Fl(ic.defLightCancel)} anime {ic.isAnime} opt {ic.optionInfos?.Count ?? 0}";
            var parts = new List<string>(); var shadersHere = new HashSet<string>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials;
                for (int s = 0; s < ms.Length; s++)
                {
                    var m = ms[s]; string shn = m && m.shader ? m.shader.name : "(null)";
                    shadersHere.Add(shn); matCount[shn] = matCount.TryGetValue(shn, out var c) ? c + 1 : 1;
                    if (wants != null && !wants.Contains(shn)) continue;
                    string kw = m ? string.Join(" ", HS2Next.MaterialCopies.KeywordsOf(m)) : "";
                    parts.Add($"{r.name}[{s}] {(m ? m.name : "null")} ({shn}, q{(m ? m.renderQueue : 0)}, [{kw}])");
                }
            }
            foreach (var shn in shadersHere)
            {
                if (!perShader.TryGetValue(shn, out var l)) perShader[shn] = l = new List<string>();
                l.Add($"{it.No} {it.Name}");
            }
            if (wants != null && parts.Count == 0) continue;
            sb.AppendLine(head + " | " + icS);
            foreach (var p in parts) sb.AppendLine("    " + p);
        }
        sb.AppendLine("\n== per shader: material slots, items");
        foreach (var kv in perShader.OrderByDescending(kv => kv.Value.Count))
            sb.AppendLine($"  {matCount[kv.Key],5} slots {kv.Value.Count,5} items  {kv.Key}{(HS2Next.ShaderMap.Fitted.ContainsKey(kv.Key) ? "  [fitted]" : "")}");
        if (wants != null)
            foreach (var w in wants)
                if (perShader.TryGetValue(w, out var l)) sb.AppendLine($"\n== items with {w}\n  " + string.Join("\n  ", l));
        File.WriteAllText(Path.Combine(FitDir, Arg("-scanOut", "scan_items") + ".txt"), sb.ToString().Replace("\r\n", "\n"));
        gd.UnloadAll(); gd = null; studioDb = null;
        Debug.Log("SKINFIT DONE");
    }

    // KwCheck [-fitItem specs | -card png] [-fitShader "A;B"]: for every material of the given shaders (default all) whose
    // loaded keywords differ from the drawers', what the probe tools see: a probe baseline (Base) right after creation,
    // after setting every toggle-drawer float to its other value, after HasProperty / GetFloat, after a render; and a
    // MaterialCopies copy after property reads, a render and a plain shader re-assignment -> Spike/out/fit/kwcheck.txt.
    // Needs graphics for the render step.
    public static void KwCheck()
    {
        Directory.CreateDirectory(FitDir);
        var only = Arg("-fitShader", null)?.Split(';');
        var built = new List<GameObject>();
        if (Arg("-fitItem", null) is string items) built.AddRange(LoadItems(items)); else built.AddRange(BuildCharas(1).Select(c => c.go));
        foreach (var g in built) g.SetActive(false);
        var sb = new StringBuilder($"# KwCheck: keywords seen by the probe tools (MaterialCopies.KeepSerializedKeywords {HS2Next.MaterialCopies.KeepSerializedKeywords})\n");
        string K(Material m) => "[" + string.Join(" ", m.shaderKeywords.OrderBy(x => x, StringComparer.Ordinal)) + "]";
        var seen = new HashSet<Material>();
        using (var rig = NewRig())
        {
            rig.Lighting("ambient");
            foreach (var m in built.SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials))
            {
                if (!m || !m.shader || !seen.Add(m) || (only != null && !only.Contains(m.shader.name))) continue;
                var loaded = HS2Next.MaterialCopies.KeywordsOf(m); var drawer = DrawerKeywordsOf(m);
                bool differs = !loaded.OrderBy(x => x).SequenceEqual(drawer.OrderBy(x => x));
                var declared = loaded.Union(drawer).Except(loaded.Intersect(drawer)).Where(k => m.shader.keywordSpace.FindKeyword(k).isValid).ToArray();
                sb.AppendLine($"{m.name} ({m.shader.name}) recorded {HS2Next.MaterialCopies.IsRecorded(m)}: loaded [{string.Join(" ", loaded)}] drawers [{string.Join(" ", drawer)}]{(differs ? "  DIFFERS, declared by the shader: [" + string.Join(" ", declared) + "]" : "")}");
                if (!differs) continue;
                var sh = m.shader;
                var toggles = Enumerable.Range(0, sh.GetPropertyCount()).Where(i => sh.GetPropertyAttributes(i).Any(a => a.StartsWith("Toggle") || a.StartsWith("MaterialToggle") || a.StartsWith("KeywordEnum")))
                    .Select(i => sh.GetPropertyName(i)).ToArray();
                var b = Base(sh, m);
                sb.AppendLine($"   Base: created {K(b)}");
                foreach (var t in toggles) b.SetFloat(t, b.GetFloat(t) > 0.5f ? 0 : 1);
                sb.AppendLine($"   Base: toggles {string.Join(",", toggles)} flipped {K(b)}");
                b.HasProperty("_MainTex"); b.GetFloat(toggles.FirstOrDefault() ?? "_Cutoff");
                sb.AppendLine($"   Base: after HasProperty / GetFloat {K(b)}");
                rig.Render(b);
                sb.AppendLine($"   Base: after a render {K(b)}");
                Object.DestroyImmediate(b);
                var c = HS2Next.MaterialCopies.Copy(m);
                sb.AppendLine($"   Copy: created {K(c)}");
                c.HasProperty("_MainTex"); foreach (var t in toggles) c.GetFloat(t);
                sb.AppendLine($"   Copy: after property reads {K(c)}");
                rig.Render(c);
                sb.AppendLine($"   Copy: after a render {K(c)}");
                c.shader = c.shader;
                sb.AppendLine($"   Copy: after a plain shader re-assignment {K(c)}");
                Object.DestroyImmediate(c);
            }
        }
        File.WriteAllText(Path.Combine(FitDir, Arg("-kwOut", "kwcheck") + ".txt"), sb.ToString().Replace("\r\n", "\n"));
        Cleanup(built);
        Debug.Log("SKINFIT DONE");
    }

    // MatProps -fitShader <name>: every saved property of the first material using the shader (m_SavedProperties,
    // including entries the shader does not declare) -> Spike/out/fit/<shader>_matprops.txt
    public static void MatProps()
    {
        string shaderName = Arg("-fitShader", "Shader Forge/hair_07");
        FindOriginal(shaderName, out var src, out var built);
        var sb = new StringBuilder($"# saved properties of {(src ? src.name : "none")} ({shaderName})\n");
        if (src)
        {
            var so = new SerializedObject(src);
            sb.AppendLine("keywords: " + string.Join(" ", src.shaderKeywords));
            foreach (var group in new[] { "m_TexEnvs", "m_Floats", "m_Ints", "m_Colors" })
            {
                var arr = so.FindProperty("m_SavedProperties." + group);
                if (arr == null) continue;
                sb.AppendLine("## " + group);
                for (int i = 0; i < arr.arraySize; i++)
                {
                    var e = arr.GetArrayElementAtIndex(i);
                    string n = e.FindPropertyRelative("first").stringValue;
                    var v = e.FindPropertyRelative("second");
                    var tex = group == "m_TexEnvs" ? v.FindPropertyRelative("m_Texture").objectReferenceValue : null;
                    string val = group == "m_TexEnvs" ? (tex is Texture tx ? $"{tx.name} [{tx.width}x{tx.height} {tx.graphicsFormat} filter {tx.filterMode} aniso {tx.anisoLevel} mipBias {tx.mipMapBias} wrap {tx.wrapMode} mips {tx.mipmapCount}]" : "null")
                        : group == "m_Colors" ? v.colorValue.ToString("F3") : group == "m_Ints" ? v.intValue.ToString() : v.floatValue.ToString("0.###", CultureInfo.InvariantCulture);
                    bool decl = src.shader.FindPropertyIndex(n) >= 0;
                    sb.AppendLine($"  {n} = {val}{(decl ? "" : "   (not declared by the shader)")}");
                }
            }
        }
        File.WriteAllText(Path.Combine(FitDir, Safe(shaderName) + "_matprops" + Arg("-mpOut", "") + ".txt"), sb.ToString().Replace("\r\n", "\n"));
        Cleanup(built);
        Debug.Log("SKINFIT DONE");
    }

    // ------------------------------------------------------------------ probing
    public struct Lit { public string name; public string mode; public Vector3 euler; }
    static Lit L(string n, string m, float yaw, float pitch = 0) => new Lit { name = n, mode = m, euler = new Vector3(pitch, 180 + yaw, 0) };
    public static readonly Lit[] Lights =
    {
        L("amb", "ambient", 0), L("L0", "light", 0), L("L30", "light", 30), L("L60", "light", 60), L("L80", "light", 80),
        L("L120", "light", 120), L("L150", "light", 150), L("L180", "light", 180), L("M30", "mixed", 30), L("M150", "mixed", 150),
    };

    static Lit LightByName(string n)
    {
        var known = Lights.FirstOrDefault(l => l.name == n);
        if (known.name != null) return known;
        var parts = n.Substring(1).Split('p');
        float yaw = float.Parse(parts[0], CultureInfo.InvariantCulture), pitch = parts.Length > 1 ? float.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        return L(n, n[0] == 'M' ? "mixed" : "light", yaw, pitch);
    }

    // Quad with UV0..UV3 = 0..1, white vertex colour, tangents. Same facing as Unity's quad primitive.
    static Mesh probeMesh;
    static Mesh ProbeMesh(Mesh like)
    {
        if (probeMesh) return probeMesh;
        var m = Object.Instantiate(like);
        m.name = "probe_quad";
        var uv = new List<Vector2>(); like.GetUVs(0, uv);
        m.SetUVs(1, uv); m.SetUVs(2, uv); m.SetUVs(3, uv);
        m.colors = Enumerable.Repeat(Color.white, m.vertexCount).ToArray();
        if (m.tangents.Length == 0) m.RecalculateTangents();
        return probeMesh = m;
    }

    static List<Vector2> probeUv;
    static Vector4[] probeTan;
    static ShaderProbe.Rig curRig;
    static Material curSrc;   // real material of the shader being probed (texture value "src")
    static MeshRenderer backdrop;
    static bool bgDepth;
    static void ResetMesh()
    {
        if (lightmapped && curRig != null) ClearLightmap();
        if (curRig != null && curRig.cam)
        {
            curRig.cam.backgroundColor = Color.black; if (backdrop) backdrop.enabled = false;
            curRig.cam.transform.position = new Vector3(0, 0, 2); curRig.cam.nearClipPlane = 0.1f; curRig.cam.farClipPlane = 10;
            curRig.cam.fieldOfView = 2 * Mathf.Atan(0.25f / 2) * Mathf.Rad2Deg;
            curRig.quad.transform.position = Vector3.zero;
            curRig.cam.depthTextureMode = DepthTextureMode.None; bgDepth = false;
            var acd0 = curRig.cam.GetComponent<UniversalAdditionalCameraData>(); if (acd0) acd0.requiresDepthOption = CameraOverrideOption.Off;
        }
        if (!probeMesh) return;
        if (probeUv == null) { probeUv = new List<Vector2>(); probeMesh.GetUVs(0, probeUv); }
        probeMesh.colors = Enumerable.Repeat(Color.white, probeMesh.vertexCount).ToArray();
        for (int c = 0; c < 4; c++) probeMesh.SetUVs(c, probeUv);
        if (probeTan == null) probeTan = probeMesh.tangents;
        probeMesh.tangents = probeTan;
    }

    public static ShaderProbe.Rig NewRig()
    {
        var rig = new ShaderProbe.Rig();
        curRig = rig; if (backdrop) Object.DestroyImmediate(backdrop.gameObject);
        var mf = rig.quad.GetComponent<MeshFilter>();
        mf.sharedMesh = ProbeMesh(mf.sharedMesh);
        // Perspective, same framing as the ortho rig: Built-in computes view vectors from the camera
        // position even for ortho cameras while URP uses the camera forward, so ortho would differ.
        rig.cam.orthographic = false;
        rig.cam.fieldOfView = 2 * Mathf.Atan(0.25f / 2) * Mathf.Rad2Deg;
        return rig;
    }

    // Probe baseline: the real material's values and keywords, every 2D texture at its neutral default.
    // Keywords (Phase 3.1 re-verification): the ones the material was loaded with (MaterialCopies.KeywordsOf), as HS2 and
    // a player draw it. Before 2.3 the source's keywords had been switched by the Editor's [Toggle] / [MaterialToggle]
    // drawers when its properties were first read (CopyPropertiesFromMaterial on a bundle material, new Material on a
    // character's copy); -drawerKw reproduces that (the drawers' keywords for the source's values, and the Phase 1 copies
    // through MaterialCopies.KeepSerializedKeywords = false) for before / after comparisons.
    public static bool DrawerKw => Flag("-drawerKw");
    public static Material Base(Shader sh, Material src)
    {
        if (src) curSrc = src;
        var m = ShaderProbe.Baseline(sh, src);
        if (src) { m.renderQueue = src.renderQueue; m.shaderKeywords = SourceKeywords(src); }
        return m;
    }

    static readonly Dictionary<Material, string[]> drawerKwCache = new Dictionary<Material, string[]>();
    public static string[] SourceKeywords(Material src)
    {
        if (!DrawerKw) return HS2Next.MaterialCopies.KeywordsOf(src);
        if (!drawerKwCache.TryGetValue(src, out var k))
        {
            var t = new Material(src);   // the Editor applies the drawers to a new copy
            drawerKwCache[src] = k = t.shaderKeywords; Object.DestroyImmediate(t);
        }
        return (string[])k.Clone();
    }

    // The drawers' keywords for a material's current values, without touching the material (a throwaway copy).
    public static string[] DrawerKeywordsOf(Material src)
    {
        var t = new Material(src); var k = t.shaderKeywords; Object.DestroyImmediate(t); return k;
    }

    // "r:g:b:a" constant, or a single number for all channels.
    static Color ParseColor(string v)
    {
        var p = v.Split(':').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
        return p.Length == 1 ? new Color(p[0], p[0], p[0], p[0]) : new Color(p[0], p[1], p.Length > 2 ? p[2] : 0, p.Length > 3 ? p[3] : 1);
    }

    // Linear ramp (clamped): R = G = B = u (or v), A = 1. Used with constant UVs to decode UV transforms.
    static readonly Dictionary<bool, Texture2D> ramps = new Dictionary<bool, Texture2D>();
    static Texture2D Ramp(bool u)
    {
        if (ramps.TryGetValue(u, out var t) && t) return t;
        const int n = 256;
        t = new Texture2D(n, n, TextureFormat.RGBAFloat, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) { float v = ((u ? x : y) + 0.5f) / n; px[y * n + x] = new Color(v, v, v, 1); }
        t.SetPixels(px); t.Apply();
        return ramps[u] = t;
    }

    // "ramp:u:x,0,1,1" (or x/0/1/1 inside sweep lists) -> channels: 'x' = ramp along u (or v), number = constant.
    static Texture2D Ramp(string spec)
    {
        if (rampsByName.TryGetValue(spec, out var t) && t) return t;
        var p = spec.Split(':'); bool u = p[1] == "u"; var ch = p[2].Split(p[2].Contains('/') ? '/' : ',');
        // "ramp@64:" / "rampr@64:" = ramp texture of 64x64 texels instead of 256x256 (texel-size dependent terms).
        int at = p[0].IndexOf('@');
        int n = at > 0 ? int.Parse(p[0].Substring(at + 1)) : 256;
        string kind = at > 0 ? p[0].Substring(0, at) : p[0];
        // "rampr:" = same ramp with Repeat wrap (for textures sampled with offsets or large tiling).
        t = new Texture2D(n, n, TextureFormat.RGBAFloat, false, true) { wrapMode = kind == "rampr" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
        {
            float v = ((u ? x : y) + 0.5f) / n;
            // "x" = the ramp value, "xN" = sawtooth frac(v * N) (slope N), a number = constant.
            float C(int i) => ch[i] == "x" ? v : ch[i].StartsWith("x") ? Mathf.Repeat(v * float.Parse(ch[i].Substring(1), CultureInfo.InvariantCulture), 1f)
                : float.Parse(ch[i], CultureInfo.InvariantCulture);
            px[y * n + x] = new Color(C(0), C(1), C(2), C(3));
        }
        t.SetPixels(px); t.Apply();
        return rampsByName[spec] = t;
    }
    static readonly Dictionary<string, Texture2D> rampsByName = new Dictionary<string, Texture2D>();

    // "mips" / "mipsN": 256x256 trilinear texture whose mip level k is the constant (1, k / N, 1, 1) (N default 16), to read
    // which level of detail a sample used (G channel, fractional between levels).
    static Texture2D MipLevels(string spec)
    {
        if (rampsByName.TryGetValue(spec, out var t) && t) return t;
        // "mipsb" = bilinear filtering, "mipsa" = trilinear with anisoLevel 8, "mipsc" = trilinear clamp; default trilinear repeat.
        char kind = spec.Length > 4 && char.IsLetter(spec[4]) ? spec[4] : 't';
        string num = spec.Substring(kind == 't' ? 4 : 5);
        float div = num.Length > 0 ? float.Parse(num, CultureInfo.InvariantCulture) : 16;
        t = new Texture2D(256, 256, TextureFormat.RGBAFloat, true, true)
        {
            wrapMode = kind == 'c' ? TextureWrapMode.Clamp : TextureWrapMode.Repeat,
            filterMode = kind == 'b' ? FilterMode.Bilinear : FilterMode.Trilinear, anisoLevel = kind == 'a' ? 8 : 0
        };
        for (int k = 0; k < t.mipmapCount; k++)
        {
            int n = Mathf.Max(1, 256 >> k);
            t.SetPixels(Enumerable.Repeat(new Color(1, k / div, 1, 1), n * n).ToArray(), k);
        }
        t.Apply(false);
        return rampsByName[spec] = t;
    }

    public static void SetProp(Material m, string name, string value)
    {
        // Background behind the probe quad (for blended shaders): _BG=r:g:b (linear clear colour),
        // _BGTEX=<texture value> (unlit backdrop quad behind the probe, e.g. a ramp, for refraction).
        if (name == "_BG" && curRig != null) { curRig.cam.backgroundColor = ParseColor(value).gamma; return; }
        if (name == "_BGTEX" && curRig != null) { Backdrop(value); return; }
        // _BGZ=z: backdrop position along z (default -0.5; set after _BGTEX). _BGDEPTH=1 (before _BGTEX): Built-in backdrop
        // with a shadow-caster pass (Legacy Shaders/Diffuse), so that it is in Built-in's _CameraDepthTexture (with _DEPTHTEX=1).
        if (name == "_BGZ" && backdrop) { backdrop.transform.position = new Vector3(0, 0, float.Parse(value, CultureInfo.InvariantCulture)); return; }
        if (name == "_BGDEPTH") { bgDepth = value != "0"; return; }
        // Lightmaps on the probe quad (uv1 = uv0, lightmapScaleOffset (1, 1, 0, 0); Phase 3.7 package L): _LM=<texture value>
        // (lightmap colour; non-directional), _LMDIR=<texture value> (directional lightmap: Combined Directional mode),
        // _LMMASK=<texture value> (shadow mask; the probe light becomes a mixed Shadowmask light on channel 0, quality
        // shadowmask mode Shadowmask), _LMSUB=1 (the probe light becomes a mixed Subtractive light), _LMSUBCOL=r:g:b
        // (RenderSettings.subtractiveShadowColor). Cleared at the next cell.
        if ((name == "_LM" || name == "_LMDIR" || name == "_LMMASK" || name == "_LMSUB" || name == "_LMSUBCOL") && curRig != null) { Lightmap(name, value); return; }
        // "_QUEUE=n": the probe material's render queue (transparent queues: Built-in forward applies no realtime shadows there).
        if (name == "_QUEUE" && m) { m.renderQueue = int.Parse(value, CultureInfo.InvariantCulture); return; }
        // Mesh overrides on the probe quad: _VCOL=r:g:b:a (vertex colour), _UV1/_UV2/_UV3=u:v (constant UV),
        // _TANGENT=x:y:z:w (all vertex tangents, object space).
        if (name == "_VCOL" && probeMesh) { probeMesh.colors = Enumerable.Repeat(ParseColor(value), probeMesh.vertexCount).ToArray(); return; }
        if (name == "_CAMDIST" && curRig != null)   // probe camera distance, same framing
        {
            float d = float.Parse(value, CultureInfo.InvariantCulture);
            curRig.cam.transform.position = new Vector3(0, 0, d); curRig.cam.nearClipPlane = d * 0.05f; curRig.cam.farClipPlane = d * 5;
            curRig.cam.fieldOfView = 2 * Mathf.Atan(0.25f / d) * Mathf.Rad2Deg;
            return;
        }
        if (name == "_QUADOFF" && curRig != null)   // move the probe quad (camera unchanged), x:y:z
        {
            var c = ParseColor(value);
            curRig.quad.transform.position = new Vector3(c.r, c.g, c.b);
            return;
        }
        // _NEARFAR=n:f: probe camera clip planes (depth-dependent terms); _DEPTHTEX=1: the camera renders
        // _CameraDepthTexture (Built-in depthTextureMode Depth; URP: the asset's depth texture is requested per camera).
        // _FOVT=t: probe camera field of view 2 atan(t) (default t = 0.25 / distance); _CAMPOS=x:y:z: camera position,
        // orientation unchanged (looking along -z).
        if (name == "_FOVT" && curRig != null) { curRig.cam.fieldOfView = 2 * Mathf.Atan(float.Parse(value, CultureInfo.InvariantCulture)) * Mathf.Rad2Deg; return; }
        if (name == "_CAMPOS" && curRig != null) { var c = ParseColor(value); curRig.cam.transform.position = new Vector3(c.r, c.g, c.b); return; }
        if (name == "_NEARFAR" && curRig != null) { var c = ParseColor(value); curRig.cam.nearClipPlane = c.r; curRig.cam.farClipPlane = c.g; return; }
        if (name == "_DEPTHTEX" && curRig != null)
        {
            bool on = value != "0";
            curRig.cam.depthTextureMode = on ? DepthTextureMode.Depth : DepthTextureMode.None;
            var acd = curRig.cam.GetComponent<UniversalAdditionalCameraData>(); if (!acd) acd = curRig.cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            acd.requiresDepthOption = on ? CameraOverrideOption.On : CameraOverrideOption.Off;
            return;
        }
        if (name == "_TANGENT" && probeMesh) { probeMesh.tangents = Enumerable.Repeat((Vector4)ParseColor(value), probeMesh.vertexCount).ToArray(); return; }
        if (name.StartsWith("_UV") && name.Length == 4 && probeMesh)
        {
            // "_UVn=none": the probe quad without that UV channel (what a shader reading it gets from a mesh without it;
            // Phase 4.3 UH1: most hair meshes have no uv1). Restored by ResetMesh at the next cell.
            if (value == "none")
            {
                probeMesh.SetUVs(name[3] - '0', new List<Vector2>());
                if (probeMesh.HasVertexAttribute((VertexAttribute)((int)VertexAttribute.TexCoord0 + name[3] - '0'))) Debug.LogWarning("UV channel not removed: " + name);
                return;
            }
            var c = ParseColor(value);
            probeMesh.SetUVs(name[3] - '0', Enumerable.Repeat(new Vector2(c.r, c.g), probeMesh.vertexCount).ToList());
            return;
        }
        // "_G_Name=v": global float (debug switches in replacement shaders).
        if (name.StartsWith("_G_")) { Shader.SetGlobalFloat(name.Substring(2), float.Parse(value, CultureInfo.InvariantCulture)); return; }
        // "_SETCOLOR_Name=r:g:b:a": Material.SetColor on any property name, whatever its declared type (HS2's Studio
        // writes the glass colour into the float properties _Metallic4 / _Glossiness4 this way).
        if (name.StartsWith("_SETCOLOR_") && m) { m.SetColor(name.Substring(9), ParseColor(value)); return; }
        // "_KW=+NAME" / "_KW=-NAME": enable / disable a material keyword.
        if (name == "_KW" && m) { if (value.StartsWith("-")) m.DisableKeyword(value.Substring(1)); else m.EnableKeyword(value.TrimStart('+')); return; }
        // "_Tex_ST=sx:sy:ox:oy": texture tiling / offset of a texture property.
        if (name.EndsWith("_ST") && m.shader.FindPropertyIndex(name.Substring(0, name.Length - 3)) >= 0)
        {
            var st = ParseColor(value); string tn = name.Substring(0, name.Length - 3);
            m.SetTextureScale(tn, new Vector2(st.r, st.g)); m.SetTextureOffset(tn, new Vector2(st.b, st.a));
            return;
        }
        var sh = m.shader; int i = sh.FindPropertyIndex(name);
        if (i < 0) { if (name == "_HS2Fit" || name.StartsWith("_HS2")) m.SetVector(name, ParseColor(value)); else Debug.LogWarning("no property " + name); return; }
        switch (sh.GetPropertyType(i))
        {
            case ShaderPropertyType.Texture:
                if (value == "src") m.SetTexture(name, curSrc ? curSrc.GetTexture(name) : null);   // the real material's texture
                else if (value.StartsWith("mips")) m.SetTexture(name, MipLevels(value));
                else if (value == "rampu" || value == "rampv") m.SetTexture(name, Ramp(value == "rampu"));
                else if (value.StartsWith("ramp:") || value.StartsWith("rampr:") || value.StartsWith("ramp@") || value.StartsWith("rampr@")) m.SetTexture(name, Ramp(value));
                else if (value.StartsWith("srgb")) m.SetTexture(name, ShaderProbe.Const(ParseColor(value.Substring(4)), false));
                else m.SetTexture(name, ShaderProbe.Const(ParseColor(value)));
                break;
            case ShaderPropertyType.Color: case ShaderPropertyType.Vector: m.SetVector(name, ParseColor(value)); break;
            default: m.SetFloat(name, float.Parse(value, CultureInfo.InvariantCulture)); break;
        }
    }

    // Unlit textured quad 0.5 behind the probe quad, 3x its size (pipeline-appropriate unlit shader).
    static void Backdrop(string value)
    {
        bool urp = GraphicsSettings.currentRenderPipeline != null;
        if (!backdrop)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.name = "probe_backdrop";
            q.transform.position = new Vector3(0, 0, -0.5f);
            q.transform.rotation = curRig.quad.transform.rotation;
            q.transform.localScale = Vector3.one * 3;
            backdrop = q.GetComponent<MeshRenderer>();
        }
        backdrop.transform.position = new Vector3(0, 0, -0.5f);
        var sh = Shader.Find(urp ? "Universal Render Pipeline/Unlit" : bgDepth ? "Legacy Shaders/Diffuse" : "Unlit/Texture");
        if (!backdrop.sharedMaterial || backdrop.sharedMaterial.shader != sh) backdrop.sharedMaterial = new Material(sh);
        var tex = value.StartsWith("ramp:") ? Ramp(value) : value == "rampu" || value == "rampv" ? Ramp(value == "rampu") : ShaderProbe.Const(ParseColor(value));
        backdrop.sharedMaterial.SetTexture(urp ? "_BaseMap" : "_MainTex", tex);
        if (urp) backdrop.sharedMaterial.SetColor("_BaseColor", Color.white);
        backdrop.enabled = true;
    }

    // ---- probe lightmaps (Phase 3.7 package L)
    static bool lightmapped;
    static ShadowmaskMode? smMode0;
    static Color subColor0 = new Color(0.42f, 0.478f, 0.627f, 1);
    static Texture2D TexOf(string value) =>
        value.StartsWith("ramp:") || value.StartsWith("rampr:") || value.StartsWith("ramp@") || value.StartsWith("rampr@") ? Ramp(value)
        : value == "rampu" || value == "rampv" ? Ramp(value == "rampu")
        : value.StartsWith("srgb") ? ShaderProbe.Const(ParseColor(value.Substring(4)), false) : ShaderProbe.Const(ParseColor(value));

    static void Lightmap(string name, string value)
    {
        if (!lightmapped) subColor0 = RenderSettings.subtractiveShadowColor;
        lightmapped = true;
        if (name == "_LMSUBCOL") { RenderSettings.subtractiveShadowColor = ParseColor(value); return; }
        if (name == "_LMSUB")
        {
            curRig.light.bakingOutput = new LightBakingOutput { isBaked = true, lightmapBakeType = LightmapBakeType.Mixed, mixedLightingMode = MixedLightingMode.Subtractive, occlusionMaskChannel = -1, probeOcclusionLightIndex = -1 };
            curRig.light.shadows = LightShadows.Hard;
            return;
        }
        var lms = LightmapSettings.lightmaps;
        var d = lms != null && lms.Length > 0 ? lms[0] : new LightmapData();
        var tex = TexOf(value);
        if (name == "_LM") d.lightmapColor = tex;
        else if (name == "_LMDIR") d.lightmapDir = tex;
        else
        {
            d.shadowMask = tex;
            curRig.light.bakingOutput = new LightBakingOutput { isBaked = true, lightmapBakeType = LightmapBakeType.Mixed, mixedLightingMode = MixedLightingMode.Shadowmask, occlusionMaskChannel = 0, probeOcclusionLightIndex = 0 };
            if (smMode0 == null) smMode0 = QualitySettings.shadowmaskMode;   // restored by ClearLightmap (a project setting)
            QualitySettings.shadowmaskMode = ShadowmaskMode.Shadowmask;
            curRig.light.shadows = LightShadows.Hard;   // a mixed light baked with shadows (the rig resets it per cell)
        }
        LightmapSettings.lightmapsMode = d.lightmapDir ? LightmapsMode.CombinedDirectional : LightmapsMode.NonDirectional;
        LightmapSettings.lightmaps = new[] { d };
        curRig.quad.lightmapIndex = 0;
        curRig.quad.lightmapScaleOffset = new Vector4(1, 1, 0, 0);
    }

    static void ClearLightmap()
    {
        lightmapped = false;
        LightmapSettings.lightmaps = new LightmapData[0];
        LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional;
        if (curRig.quad) curRig.quad.lightmapIndex = -1;
        if (curRig.light) curRig.light.bakingOutput = new LightBakingOutput { isBaked = false, lightmapBakeType = LightmapBakeType.Realtime, mixedLightingMode = MixedLightingMode.IndirectOnly, occlusionMaskChannel = -1, probeOcclusionLightIndex = -1 };
        RenderSettings.subtractiveShadowColor = subColor0;
        if (smMode0 != null) { QualitySettings.shadowmaskMode = smMode0.Value; smMode0 = null; }
    }

    // "a=1;b=0:0:0:1" -> list of (prop, value)
    public static List<(string, string)> ParseSets(string s) => string.IsNullOrEmpty(s) ? new List<(string, string)>()
        : s.Split(';').Where(x => x.Contains('=')).Select(x => (x.Substring(0, x.IndexOf('=')).Trim(), x.Substring(x.IndexOf('=') + 1).Trim())).ToList();

    static Shader FindOriginal(string shaderName, out Material src, out List<GameObject> built)
    {
        built = new List<GameObject>();
        src = null;
        if (shaderName == "AIT/Skin Translucency")
        {
            gd = CardResolveTest.BuildDb(null);
            var body = gd.LoadAsset<GameObject>("chara/oo_base.unity3d", "p_cf_body_00");
            src = body.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).First(m => m && m.shader.name == shaderName);
            return src.shader;
        }
        if (Arg("-fitItem", null) is string item)
        {
            ShaderUtil.allowAsyncCompilation = false;
            built.AddRange(LoadItems(item));
        }
        else
        {
            var ch = BuildCharas(1);
            built.AddRange(ch.Select(c => c.go));
        }
        string want = Arg("-fitMaterial", null);
        src = built.SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials)
            .FirstOrDefault(m => m && m.shader && m.shader.name == shaderName && (want == null || m.name == want));
        // Studio items sit at the origin, inside the probe view: hide them (their materials stay valid).
        if (Arg("-fitItem", null) != null) foreach (var g in built) g.SetActive(false);
        return src ? src.shader : null;
    }

    static string F3(Color c) => $"{c.r:F4},{c.g:F4},{c.b:F4}";

    // Generic probe: each input varied alone against the real-material baseline, 4 lights.
    // -fitShader may list several shaders ("A;B"); -probeBgs "0:0:0;1:1:1" repeats every line over these
    // clear colours (blended shaders: out = src * a + bg * (1 - a) separates colour and coverage).
    public static void Probe()
    {
        var names = Arg("-fitShader", "AIT/Skin True Face").Split(';');
        var bgs = Arg("-probeBgs", "0:0:0").Split(';');
        Directory.CreateDirectory(FitDir);
        FindOriginal(names[0], out _, out var built);
        ShaderFit.SetPipeline(null);
        foreach (var shaderName in names)
        {
            var src = built.SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials)
                .FirstOrDefault(m => m && m.shader && m.shader.name == shaderName);
            if (!src) { Debug.LogWarning("no material for " + shaderName); continue; }
            var sh = src.shader;
            var sb = new StringBuilder($"# probe {shaderName} (material {src.name}, keywords [{string.Join(" ", src.shaderKeywords)}])\n");
            sb.AppendLine("# baseline = real material values, 2D textures neutral; each line: variant then per light R,G,B (linear)");
            var lights = Lights.Where(l => new[] { "amb", "L0", "L60", "L150" }.Contains(l.name)).ToArray();
            var sets = ParseSets(Arg("-set", ""));
            using (var rig = NewRig())
                foreach (var bg in bgs)
                {
                    sb.AppendLine($"\n## background {bg}");
                    Color Render(Lit l, Action<Material> f)
                    {
                        rig.Lighting(l.mode, l.euler);
                        ResetMesh(); SetProp(null, "_BG", bg);
                        var m = Base(sh, src); foreach (var (k, v) in sets) SetProp(m, k, v); f(m);
                        var c = rig.Render(m); Object.DestroyImmediate(m); return c;
                    }
                    void Line(string label, Action<Material> f) => sb.AppendLine($"  {label,-34} " + string.Join("  ", lights.Select(l => $"{l.name}:{F3(Render(l, f))}")));
                    Line("baseline", m => { });
                    for (int i = 0; i < sh.GetPropertyCount(); i++)
                    {
                        string n = sh.GetPropertyName(i);
                        var t = sh.GetPropertyType(i);
                        if (t == ShaderPropertyType.Texture && sh.GetPropertyTextureDimension(i) == TextureDimension.Tex2D)
                            foreach (var (lab, c) in new[] { ("W", new Color(1, 1, 1, 1)), ("R", new Color(1, 0, 0, 1)), ("G", new Color(0, 1, 0, 1)), ("B", new Color(0, 0, 1, 1)), ("K", new Color(0, 0, 0, 1)), ("A0", new Color(1, 1, 1, 0)), ("A.5", new Color(1, 1, 1, 0.5f)) })
                                Line($"tex {n} {lab}", m => m.SetTexture(n, ShaderProbe.Const(c)));
                        else if (t == ShaderPropertyType.Color || t == ShaderPropertyType.Vector)
                            foreach (var (lab, c) in new[] { ("W", new Vector4(1, 1, 1, 1)), ("R", new Vector4(1, 0, 0, 1)), ("K", new Vector4(0, 0, 0, 1)), ("A0", new Vector4(1, 1, 1, 0)), ("A.5", new Vector4(1, 1, 1, 0.5f)), ("0", Vector4.zero) })
                                Line($"vec {n} {lab}", m => m.SetVector(n, c));
                        else if (t == ShaderPropertyType.Float || t == ShaderPropertyType.Range)
                        {
                            var r = t == ShaderPropertyType.Range ? sh.GetPropertyRangeLimits(i) : new Vector2(0, 1);
                            foreach (var v in new[] { r.x, (r.x + r.y) / 2, r.y }.Distinct())
                                Line($"flt {n} {Fl(v)}", m => m.SetFloat(n, v));
                        }
                    }
                }
            File.WriteAllText(Path.Combine(FitDir, Safe(shaderName) + "_probe" + Arg("-probeOut", "") + ".txt"), sb.ToString().Replace("\r\n", "\n"));
        }
        Cleanup(built);
        Debug.Log("SKINFIT DONE");
    }

    // Sweep: -sweepFile <txt> with lines "label | set | sweep | sweep2 | lights | tilt" (set "a=..;b=..",
    // sweep "prop=v1,v2,...", lights "amb,L0" or empty = all, tilt = quad yaw in degrees).
    // Texture values are "r:g:b:a" (linear constant) or "srgbr:g:b:a".
    // -fitPipe builtin (default) | urp | both. With urp/both the replacement (-repl, default
    // "HS2Next/AIT <name>") is rendered under URP; both prints "builtin/urp" and the RMS per block.
    // -sweepOut <name> overrides the output file suffix.
    public class Job { public string label, setStr; public List<(string, string)> sets; public string p1, p2; public string[] v1, v2; public Lit[] lights; public float tilt; }

    public static List<Job> ReadJobs(string file)
    {
        var jobs = new List<Job>();
        foreach (var line in File.ReadAllLines(file))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
            var f = line.Split('|').Select(x => x.Trim()).ToArray();
            string Fx(int i) => i < f.Length ? f[i] : "";
            (string, string[]) Sw(string a) { if (a == "") return (null, new string[] { null }); int k = a.IndexOf('='); return (a.Substring(0, k), a.Substring(k + 1).Split(',')); }
            var (p1, v1) = Sw(Fx(2)); var (p2, v2) = Sw(Fx(3));
            var ln = Fx(4) == "" ? null : Fx(4).Split(',');
            jobs.Add(new Job
            {
                label = Fx(0), setStr = Fx(1), sets = ParseSets(Fx(1)), p1 = p1, v1 = v1, p2 = p2, v2 = v2,
                // Named lights from the table, or any "L<yaw>" / "M<yaw>" / "L<yaw>p<pitch>" built on the fly.
                lights = ln == null ? Lights : ln.Select(LightByName).ToArray(),
                tilt = Fx(5) == "" ? 0 : float.Parse(Fx(5), CultureInfo.InvariantCulture),
            });
        }
        return jobs;
    }

    // Renders every job cell; key = (job index, a index, b index, light index).
    public static Dictionary<(int, int, int, int), Color> Eval(List<Job> jobs, Shader shader, Material src, RenderPipelineAsset rp, List<(string, string)> extra = null)
    {
        ShaderFit.SetPipeline(rp);
        var res = new Dictionary<(int, int, int, int), Color>();
        using (var rig = NewRig())
        {
            var q = rig.quad.transform; var rot0 = q.rotation; var sc0 = q.localScale;
            rig.Lighting("ambient"); var w = Base(shader, src); rig.Render(w); rig.Render(w); Object.DestroyImmediate(w);
            for (int j = 0; j < jobs.Count; j++)
            {
                var jb = jobs[j];
                q.rotation = Quaternion.Euler(0, jb.tilt, 0) * rot0;
                q.localScale = new Vector3(sc0.x / Mathf.Max(0.2f, Mathf.Cos(jb.tilt * Mathf.Deg2Rad)), sc0.y, sc0.z);
                for (int ia = 0; ia < jb.v1.Length; ia++)
                    for (int ib = 0; ib < jb.v2.Length; ib++)
                        for (int il = 0; il < jb.lights.Length; il++)
                        {
                            var l = jb.lights[il];
                            rig.Lighting(l.mode, l.euler);
                            ResetMesh();
                            var m = Base(shader, src);
                            if (m.HasProperty("_HS2Fit")) m.SetVector("_HS2Fit", Vector4.one);
                            foreach (var (k, v) in jb.sets) SetProp(m, k, v);
                            if (jb.p1 != null) SetProp(m, jb.p1, jb.v1[ia]);
                            if (jb.p2 != null) SetProp(m, jb.p2, jb.v2[ib]);
                            if (extra != null) foreach (var (k, v) in extra) SetProp(m, k, v);
                            res[(j, ia, ib, il)] = rig.Render(m); Object.DestroyImmediate(m);
                            if (Flag("-fitPix")) pixels[(rp != null, j, ia, ib, il)] = (Color[])rig.Last.Clone();
                            if (Flag("-fitPattern")) patterns[(rp != null, j, ia, ib, il)] = Pattern(rig.Last, rig.cam.backgroundColor.linear);
                            if (Flag("-fitStats")) patterns[(rp != null, j, ia, ib, il)] = Stats(rig.Last);
                            if (Flag("-fitDump")) dumps.AppendLine($"cell {(rp != null ? "urp" : "builtin")} {jb.label} {jb.v1[ia]} {jb.v2[ib]} {jb.lights[il].name}\n" + Dump(rig.Last));
                        }
            }
            q.rotation = rot0; q.localScale = sc0;
            if (lightmapped) ClearLightmap();   // the probe lightmaps and the quality shadowmask mode back as they were
        }
        return res;
    }

    // -fitPattern: coverage of the 8x8 pixels at the image centre (row by row from the top, '#' = differs from
    // the background, '.' = background), to read screen-door patterns.
    static readonly Dictionary<(bool, int, int, int, int), string> patterns = new Dictionary<(bool, int, int, int, int), string>();
    // -fitPix (with -fitPipe both): per-pixel mean |d| between the pipelines over the centre half of each cell.
    static readonly Dictionary<(bool, int, int, int, int), Color[]> pixels = new Dictionary<(bool, int, int, int, int), Color[]>();
    static string PixDiff(Color[] a, Color[] b)
    {
        int s = (int)Math.Round(Math.Sqrt(a.Length)); double d = 0; int n = 0;
        for (int y = s / 4; y < 3 * s / 4; y++) for (int x = s / 4; x < 3 * s / 4; x++)
        { var p = a[y * s + x]; var q = b[y * s + x]; d += (Mathf.Abs(p.r - q.r) + Mathf.Abs(p.g - q.g) + Mathf.Abs(p.b - q.b)) / 3; n++; }
        return $"px|d| {d / n:F4}";
    }
    static string Pattern(Color[] px, Color bg)
    {
        int s = (int)Math.Round(Math.Sqrt(px.Length));
        var sb = new StringBuilder();
        for (int y = s / 2 + 3; y >= s / 2 - 4; y--)
        {
            for (int x = s / 2 - 4; x < s / 2 + 4; x++)
            {
                var c = px[y * s + x];
                sb.Append(Mathf.Abs(c.r - bg.r) + Mathf.Abs(c.g - bg.g) + Mathf.Abs(c.b - bg.b) > 1e-3f ? '#' : '.');
            }
            if (y > s / 2 - 4) sb.Append('/');
        }
        return sb.ToString();
    }

    // -fitStats: red-channel min / max over the centre half and the 4 corner values of that region (TL TR BL BR).
    static string Stats(Color[] px)
    {
        int s = (int)Math.Round(Math.Sqrt(px.Length));
        float mn = 9, mx = -9;
        for (int y = s / 4; y < 3 * s / 4; y++) for (int x = s / 4; x < 3 * s / 4; x++) { mn = Mathf.Min(mn, px[y * s + x].r); mx = Mathf.Max(mx, px[y * s + x].r); }
        float P(int x, int y) => px[y * s + x].r;
        int a = s / 4, b = 3 * s / 4 - 1;
        return $"{mn:F4}..{mx:F4} TL {P(a, b):F4} TR {P(b, b):F4} BL {P(a, a):F4} BR {P(b, a):F4}";
    }

    // -fitDump: the full probe image (R channel, rows from the top) of every cell -> <shader>_<suffix>_dump.txt
    static readonly StringBuilder dumps = new StringBuilder();
    static string Dump(Color[] px)
    {
        int s = (int)Math.Round(Math.Sqrt(px.Length));
        var sb = new StringBuilder();
        for (int y = s - 1; y >= 0; y--)
        {
            for (int x = 0; x < s; x++) sb.Append(px[y * s + x].r.ToString("F4", CultureInfo.InvariantCulture)).Append(x < s - 1 ? " " : "");
            sb.Append('\n');
        }
        return sb.ToString();
    }

    // -urpAsset spike (default) | production | <asset path>: the URP asset of the URP side (HS2Next.App.AppPipeline.ResolveAssetPath).
    static UniversalRenderPipelineAsset UrpAsset => AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HS2Next.App.AppPipeline.ResolveAssetPath(Arg("-urpAsset", null)));
    static string ReplName(string orig) => Arg("-repl", "HS2Next/AIT " + orig.Substring(4));

    public static void Sweep()
    {
        string shaderName = Arg("-fitShader", "AIT/Skin True Face");
        var sh = FindOriginal(shaderName, out var src, out var built);
        // -origFind: use the editor's shader of that name (e.g. Unity's full Built-in Standard) as the reference
        // instead of the bundle copy (bundle copies of Standard are stripped to the variants their materials used).
        if (Flag("-origFind") && Shader.Find(shaderName) is Shader full) sh = full;
        ShaderUtil.allowAsyncCompilation = false;
        string pipe = Arg("-fitPipe", "builtin");
        var repl = pipe != "builtin" ? Shader.Find(ReplName(shaderName)) : null;
        var jobs = ReadJobs(Arg("-sweepFile", "sweep.txt"));
        var extra = ParseSets(Arg("-extra", ""));
        var bi = pipe != "urp" ? Eval(jobs, sh, src, null) : null;
        var ur = pipe != "builtin" ? Eval(jobs, repl, src, UrpAsset, extra) : null;
        var sb = new StringBuilder($"# sweep {shaderName}  pipe {pipe}{(repl ? "  repl " + repl.name : "")}  extra [{Arg("-extra", "")}]\n");
        if (src) sb.AppendLine($"# source {src.name}: keywords used [{string.Join(" ", SourceKeywords(src))}] (loaded [{string.Join(" ", HS2Next.MaterialCopies.KeywordsOf(src))}], drawers [{string.Join(" ", DrawerKeywordsOf(src))}]){(DrawerKw ? " -drawerKw" : "")}");
        double tot = 0; int totN = 0;
        for (int j = 0; j < jobs.Count; j++)
        {
            var jb = jobs[j];
            sb.AppendLine($"\n## {jb.label}   set [{jb.setStr}]{(jb.tilt != 0 ? $"  tilt {jb.tilt}" : "")}");
            sb.AppendLine($"{jb.p1,-12} {jb.p2,-12} | " + string.Join("  ", jb.lights.Select(l => $"{l.name,-20}")));
            double e = 0; int n = 0;
            for (int ia = 0; ia < jb.v1.Length; ia++)
                for (int ib = 0; ib < jb.v2.Length; ib++)
                {
                    var cells = new List<string>();
                    for (int il = 0; il < jb.lights.Length; il++)
                    {
                        var k = (j, ia, ib, il);
                        if (pipe == "both")
                        {
                            Color a = bi[k], b = ur[k];
                            cells.Add($"{a.r:F3}/{b.r:F3} {a.g:F3}/{b.g:F3} {a.b:F3}/{b.b:F3}");
                            e += Sq(a.r - b.r) + Sq(a.g - b.g) + Sq(a.b - b.b); n += 3;
                        }
                        else cells.Add(F3((bi ?? ur)[k]));
                        if (pipe == "both" && pixels.TryGetValue((false, j, ia, ib, il), out var pxa) && pixels.TryGetValue((true, j, ia, ib, il), out var pxb)) cells.Add("[" + PixDiff(pxa, pxb) + "]");
                        if (patterns.TryGetValue((pipe == "urp", j, ia, ib, il), out var pat)) cells.Add("[" + pat + (pipe == "both" && patterns.TryGetValue((true, j, ia, ib, il), out var pu) && pu != pat ? " urp " + pu : "") + "]");
                    }
                    sb.AppendLine($"{jb.v1[ia],-12} {jb.v2[ib],-12} | " + string.Join("  ", cells));
                }
            if (pipe == "both") { sb.AppendLine($"   block RMS {Math.Sqrt(e / Math.Max(1, n)):F4}"); tot += e; totN += n; }
        }
        if (pipe == "both") sb.Insert(0, $"# TOTAL RMS {Math.Sqrt(tot / Math.Max(1, totN)):F4} over {totN} values\n");
        string suffix = Arg("-sweepOut", "sweep" + (pipe == "builtin" ? "" : "_" + pipe));
        if (dumps.Length > 0) File.WriteAllText(Path.Combine(FitDir, Safe(shaderName) + "_" + suffix + "_dump.txt"), dumps.ToString());
        File.WriteAllText(Path.Combine(FitDir, Safe(shaderName) + "_" + suffix + ".txt"), sb.ToString().Replace("\r\n", "\n"));
        Cleanup(built);
        Debug.Log("SKINFIT DONE");
    }

    static double Sq(float v) => v * v;

    // SkinInfo -renderer <name>: bone scales relative to the renderer transform and the lengths of the skinned
    // normals / tangents in renderer space (Built-in builds tangent-space rotations from these unnormalized vectors).
    public static void SkinInfo()
    {
        string want = Arg("-renderer", "o_eyebase_L");
        var charas = BuildCharas(1);
        var sb = new StringBuilder();
        foreach (var smr in charas[0].go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.name == want))
        {
            sb.AppendLine($"{smr.name}: transform lossyScale {smr.transform.lossyScale.ToString("F4")}, rootBone {(smr.rootBone ? smr.rootBone.name : "-")}");
            var m = smr.sharedMesh; var bw = m.GetAllBoneWeights(); var bpv = m.GetBonesPerVertex();
            var used = new HashSet<int>(); foreach (var w in bw) if (w.weight > 0) used.Add(w.boneIndex);
            foreach (var bi in used)
            {
                var bone = smr.bones[bi];
                var rel = smr.transform.worldToLocalMatrix * bone.localToWorldMatrix * m.bindposes[bi];
                var c0 = new Vector3(rel.m00, rel.m10, rel.m20).magnitude; var c1 = new Vector3(rel.m01, rel.m11, rel.m21).magnitude; var c2 = new Vector3(rel.m02, rel.m12, rel.m22).magnitude;
                sb.AppendLine($"  bone {bone.name}: skin matrix column lengths {c0:F4} {c1:F4} {c2:F4}");
            }
            var baked = new Mesh(); smr.BakeMesh(baked, true);
            var nl = baked.normals.Select(v => v.magnitude).ToArray(); var tl = baked.tangents.Select(v => ((Vector3)v).magnitude).ToArray();
            sb.AppendLine($"  baked normals |n| {nl.Min():F4}..{nl.Max():F4}, tangents |t| {(tl.Length > 0 ? tl.Min().ToString("F4") + ".." + tl.Max().ToString("F4") : "none")}, tangent.w {string.Join(",", baked.tangents.Select(v => v.w).Distinct())}");
            var mn = m.normals; var mt = m.tangents; var muv = m.uv;
            if (mt.Length == mn.Length)
            {
                var dots = Enumerable.Range(0, mn.Length).Select(k => Mathf.Abs(Vector3.Dot(mn[k].normalized, ((Vector3)mt[k]).normalized))).ToArray();
                sb.AppendLine($"  mesh |dot(n, t)| mean {dots.Average():F3} max {dots.Max():F3}; normal z mean {mn.Average(v => v.normalized.z):F3} min {mn.Min(v => v.normalized.z):F3}; tangent mean {new Vector3(mt.Average(v => v.x), mt.Average(v => v.y), mt.Average(v => v.z)).ToString("F3")}");
                sb.AppendLine($"  uv0 u {muv.Min(v => v.x):F3}..{muv.Max(v => v.x):F3} v {muv.Min(v => v.y):F3}..{muv.Max(v => v.y):F3}; position z {m.vertices.Min(v => v.z):F3}..{m.vertices.Max(v => v.z):F3}, x {m.vertices.Min(v => v.x):F3}..{m.vertices.Max(v => v.x):F3}");
                for (int k = 0; k < mn.Length; k += mn.Length / 8) sb.AppendLine($"   v{k}: pos {m.vertices[k].ToString("F3")} n {mn[k].ToString("F3")} t {mt[k].ToString("F3")} uv {muv[k].ToString("F3")}");
            }
            var on = m.normals.Select(v => v.magnitude).ToArray();
            sb.AppendLine($"  mesh normals |n| {on.Min():F4}..{on.Max():F4}");
        }
        File.WriteAllText(Path.Combine(FitDir, "skininfo.txt"), sb.ToString().Replace("\r\n", "\n"));
        Cleanup(charas.Select(c => c.go));
        Debug.Log("SKINFIT DONE");
    }

    // DumpTex: every 2D texture of the first material of each -fitShader ("A;B") as <mat>_<prop>_rgb.png and
    // _a.png (read back through a linear blit, so sRGB textures come out as stored) plus per-channel statistics.
    public static void DumpTex()
    {
        var names = Arg("-fitShader", "AIT/Liquid").Split(';');
        string dir = Path.Combine(FitDir, "tex"); Directory.CreateDirectory(dir);
        FindOriginal(names[0], out _, out var built);
        var sb = new StringBuilder("# texture statistics (stored values, 0..1): min / mean / max per channel\n");
        foreach (var shaderName in names)
        {
            var src = built.SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials)
                .FirstOrDefault(m => m && m.shader && m.shader.name == shaderName);
            if (!src) continue;
            for (int i = 0; i < src.shader.GetPropertyCount(); i++)
            {
                if (src.shader.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
                string n = src.shader.GetPropertyName(i);
                var t = src.GetTexture(n) as Texture2D;
                if (!t) continue;
                var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                bool srgb = GraphicsFormatUtility.IsSRGBFormat(t.graphicsFormat);
                Graphics.Blit(t, rt);
                RenderTexture.active = rt;
                var rd = new Texture2D(t.width, t.height, TextureFormat.RGBAFloat, false, true);
                rd.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0); rd.Apply(); RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                var px = rd.GetPixels();
                if (srgb) for (int k = 0; k < px.Length; k++) { var a = px[k].a; px[k] = px[k].gamma; px[k].a = a; }
                var mn = new Vector4(9, 9, 9, 9); var mx = new Vector4(-9, -9, -9, -9); var sum = Vector4.zero;
                foreach (var c in px) { Vector4 v = c; mn = Vector4.Min(mn, v); mx = Vector4.Max(mx, v); sum += v; }
                sum /= px.Length;
                sb.AppendLine($"{shaderName} {src.name} {n} = {t.name} {t.width}x{t.height} {t.graphicsFormat} srgb={srgb} wrap={t.wrapMode} filter={t.filterMode}");
                sb.AppendLine($"   R {mn.x:F3}/{sum.x:F3}/{mx.x:F3}  G {mn.y:F3}/{sum.y:F3}/{mx.y:F3}  B {mn.z:F3}/{sum.z:F3}/{mx.z:F3}  A {mn.w:F3}/{sum.w:F3}/{mx.w:F3}");
                var o = new Texture2D(t.width, t.height, TextureFormat.RGB24, false);
                o.SetPixels(px.Select(c => new Color(c.r, c.g, c.b, 1)).ToArray()); o.Apply();
                File.WriteAllBytes(Path.Combine(dir, $"{Safe(src.name)}_{n}_rgb.png"), o.EncodeToPNG());
                o.SetPixels(px.Select(c => new Color(c.a, c.a, c.a, 1)).ToArray()); o.Apply();
                File.WriteAllBytes(Path.Combine(dir, $"{Safe(src.name)}_{n}_a.png"), o.EncodeToPNG());
                Object.DestroyImmediate(o); Object.DestroyImmediate(rd);
            }
        }
        File.WriteAllText(Path.Combine(dir, "stats.txt"), sb.ToString().Replace("\r\n", "\n"));
        Cleanup(built);
        Debug.Log("SKINFIT DONE");
    }

    // ------------------------------------------------------------------ real asset
    // Real: assemble a card, draw only the renderers whose material uses one of -fitShader (";"-separated,
    // default the whole family that has a replacement), other slots of those renderers hidden. Render
    // views framed on each target renderer under Built-in (original) and URP (replacement) into linear
    // float targets, no MSAA, and report RMS / mean |d| over foreground pixels, for a front key light
    // and a back light (translucency), shadows on with -shadows. Writes <out>/real_<card>_<view>_<light>_{builtin,urp,diffx4}.png.
    // Views: face (o_head + face parts), eyes (same renderers framed on the eyeballs), body, mouth, mnpb
    // (metrics only). -realSet "a=v;b=v" sets properties on every target material that has them
    // (e.g. "_WeatheringAll=1" for the liquid overlay, "_NamidaScale=1" for tears).
    public static void Real()
    {
        var targets = Arg("-fitShader", "AIT/Skin True;AIT/Skin True Face;AIT/Skin Translucency simple").Split(';');
        bool shadows = Flag("-shadows");
        string outDir = Path.Combine(FitDir, "real"); Directory.CreateDirectory(outDir);
        // -fitItem "bundle:prefab": compare a studio item instead of characters (views: hair).
        var charas = Arg("-fitItem", null) is string itemSpec ? LoadItems(itemSpec).Select(g => (name: g.name, go: g)).ToList()
            : BuildCharas(int.Parse(Arg("-limit", "1")));
        ShaderUtil.allowAsyncCompilation = false;
        var invisible = new Material(Shader.Find("Hidden/HS2Next/Invisible"));
        var sb = new StringBuilder($"# real-asset comparison, shaders [{string.Join(", ", targets)}], shadows {shadows}\n");
        double totAbs = 0; long totN = 0;
        var kwNotes = new List<string>();
        // Studio items can carry their own lights (lamps); they would light the two pipelines differently (light intensity
        // conventions, Phase 3.2), so the shader comparison switches them off (-keepLights keeps them).
        int itemLights = 0;
        if (!Flag("-keepLights"))
            foreach (var (_, g) in charas)
                foreach (var lt in g.GetComponentsInChildren<Light>(true)) { lt.enabled = false; itemLights++; }
        if (itemLights > 0) sb.AppendLine($"# {itemLights} lights inside the compared objects switched off (-keepLights keeps them)");
        foreach (var (name, go) in charas)
        {
            foreach (var c in charas) c.go.SetActive(c.go == go);   // assembled at the same spot
            var rends = go.GetComponentsInChildren<Renderer>(true);
            var targetRends = new List<Renderer>();
            var mats = new List<Material>();
            foreach (var r in rends)
            {
                var ms = r.sharedMaterials;
                bool any = ms.Any(m => m && targets.Contains(m.shader.name));
                if (!any || !r.gameObject.activeInHierarchy || !r.enabled) { r.enabled = false; continue; }
                for (int i = 0; i < ms.Length; i++)
                    if (!ms[i] || !targets.Contains(ms[i].shader.name)) ms[i] = invisible;
                    else
                    {
                        // A copy with the loaded keywords (-drawerKw: a plain copy, i.e. the drawers' keywords, as before 3.1).
                        var kwLoaded = HS2Next.MaterialCopies.KeywordsOf(ms[i]); var kwDrawer = DrawerKeywordsOf(ms[i]);
                        if (!kwLoaded.OrderBy(x => x).SequenceEqual(kwDrawer.OrderBy(x => x)))
                            kwNotes.Add($"{r.name}/{ms[i].name} ({ms[i].shader.name}): loaded [{string.Join(" ", kwLoaded)}] drawers [{string.Join(" ", kwDrawer)}]; differing keywords the shader declares: [{string.Join(" ", kwLoaded.Union(kwDrawer).Except(kwLoaded.Intersect(kwDrawer)).Where(k => ms[i].shader.keywordSpace.FindKeyword(k).isValid))}]");
                        ms[i] = DrawerKw ? new Material(ms[i]) : HS2Next.MaterialCopies.Copy(ms[i]); mats.Add(ms[i]);
                        // -realSetFor "A;B": only materials of these shaders get -realSet (default: all targets).
                        var setFor = Arg("-realSetFor", null)?.Split(';');
                        if (setFor == null || setFor.Contains(ms[i].shader.name))
                            foreach (var (k, v) in ParseSets(Arg("-realSet", ""))) if (ms[i].HasProperty(k)) SetProp(ms[i], k, v);
                    }
                r.sharedMaterials = ms;
                if (r is SkinnedMeshRenderer smr)
                {
                    smr.updateWhenOffscreen = true;
                    if (Flag("-bakeStatic"))   // replace by a static copy of the posed mesh (skinning-dependent behaviour)
                    {
                        var baked = new Mesh(); smr.BakeMesh(baked, true);
                        var st = new GameObject(r.name);
                        st.transform.SetParent(r.transform.parent, false);
                        st.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                        st.transform.localScale = Vector3.one;
                        st.transform.localScale = new Vector3(1 / st.transform.lossyScale.x, 1 / st.transform.lossyScale.y, 1 / st.transform.lossyScale.z);
                        st.AddComponent<MeshFilter>().sharedMesh = baked;
                        var mr = st.AddComponent<MeshRenderer>(); mr.sharedMaterials = ms;
                        r.enabled = false;
                        targetRends.Add(mr);
                        continue;
                    }
                }
                targetRends.Add(r);
            }
            var origShader = mats.ToDictionary(m => m, m => m.shader);
            var origQueue = mats.ToDictionary(m => m, m => m.renderQueue);
            var origKw = mats.ToDictionary(m => m, m => m.shaderKeywords);
            sb.AppendLine($"\n== {name}: target renderers " + string.Join(", ", targetRends.Select(r => $"{r.name}({string.Join("/", r.sharedMaterials.Where(m => m != invisible).Select(m => m.shader.name))})")));
            // One view per distinct target mesh group: head, body, mouth (tongue + teeth).
            var faceParts = new[] { "o_head", "o_eyebase_L", "o_eyebase_R", "o_eyelashes", "o_eyeshadow", "o_namida" };
            var faceGrp = targetRends.Where(r => faceParts.Contains(r.name)).ToArray();
            var eyeFrame = faceGrp.Where(r => r.name.StartsWith("o_eyebase")).ToArray();
            var groups = new List<(string, Renderer[], Renderer[])>
            {
                ("face", faceGrp, faceGrp.Where(r => r.name == "o_head").DefaultIfEmpty(faceGrp.FirstOrDefault()).ToArray()),
                ("eyes", eyeFrame.Length > 0 ? faceGrp : new Renderer[0], eyeFrame),
                ("body", targetRends.Where(r => r.name == "o_body_cf").ToArray(), null),
                ("mouth", targetRends.Where(r => r.name == "o_tang" || r.name == "o_tooth").Where(r => r.transform.IsChildOf(CharaAssembler.Find(go.transform, "ct_head") ?? go.transform)).ToArray(), null),
                ("mnpb", targetRends.Where(r => r.name == "o_mnpa" || r.name == "o_mnpb").ToArray(), null),
            };
            // Hair (Shader Forge hair shaders): the hair alone, and hair + face parts framed on the head (hair shadows on the face).
            var hairGrp = targetRends.Where(r => r.sharedMaterials.Any(m => m != invisible && m.shader.name.StartsWith("Shader Forge/"))).ToArray();
            if (hairGrp.Length > 0)
            {
                groups.Add(("hair", hairGrp, null));
                if (faceGrp.Length > 0) groups.Add(("head", hairGrp.Concat(faceGrp).ToArray(), faceGrp.Where(r => r.name == "o_head").ToArray()));
            }
            // Clothes and accessories: one view per clothes kind (ct_*) or accessory slot (ca_slot*) holding target
            // renderers that no view above covers, framed on that group.
            var covered = new HashSet<Renderer>(groups.SelectMany(g => g.Item2));
            foreach (var g in targetRends.Where(r => !covered.Contains(r)).GroupBy(r => PartRoot(r.transform, go.transform)))
                groups.Add((g.Key, g.ToArray(), null));
            foreach (var (view, grp, frameOn) in groups)
            {
                if (grp.Length == 0 || grp[0] == null) continue;
                foreach (var r in targetRends) r.enabled = grp.Contains(r);
                var fr = frameOn != null && frameOn.Length > 0 && frameOn[0] != null ? frameOn : grp;
                var b = BakedBounds(fr[0]); foreach (var r in fr) b.Encapsulate(BakedBounds(r));
                if (view == "eyes")   // eye region: centred on the eyeballs, 0.5 x the head bounds
                {
                    var hb = grp.FirstOrDefault(r => r.name == "o_head") is Renderer hr ? BakedBounds(hr) : b;
                    b = new Bounds(b.center, hb.size * 0.5f);
                }
                foreach (var (lname, euler) in new[] { ("front", new Vector3(30, 200, 0)), ("back", new Vector3(10, 20, 0)) })
                {
                    var imgs = new Texture2D[2];
                    for (int pass = 0; pass < 2; pass++)
                    {
                        bool urp = pass == 1;
                        foreach (var m in mats)
                        {
                            // URP: the replacement as ShaderMap.ApplyUrp would set it (queue kept, undeclared keywords dropped).
                            if (urp && HS2Next.ShaderMap.ResolveFor(m) is string rn && Shader.Find(rn) is Shader rs) HS2Next.ShaderMap.Swap(m, rs);
                            // -realFallback: unmapped originals take ShaderMap's URP/Lit fallback (e.g. the Unreal/* input heuristic, 3.7 F3)
                            else if (urp && Flag("-realFallback")) HS2Next.ShaderMap.ApplyFallback(m);
                            // Back to the original without the Editor's drawers (a plain shader assignment would switch keywords;
                            // -drawerKw keeps that pre-3.1 behaviour).
                            else if (DrawerKw) { m.shader = origShader[m]; m.renderQueue = origQueue[m]; }
                            else { HS2Next.MaterialCopies.SetShader(m, origShader[m]); m.shaderKeywords = origKw[m]; m.renderQueue = origQueue[m]; }
                        }
                        imgs[pass] = RenderBounds(b, view == "body" ? 0.55f : view == "mnpb" ? 0.5f : 1.0f, euler, shadows, urp ? UrpAsset : null);
                    }
                    var (rms, mad, n, diff) = Compare(imgs[0], imgs[1]);
                    var (madE, nE) = CompareEroded(imgs[0], imgs[1]);
                    totAbs += mad * n; totN += n;
                    string tag = $"real_{Safe(name)}_{view}_{lname}";
                    // Body and mnpb views are written for metrics only (not for viewing).
                    if (view != "body" && view != "mnpb")
                    {
                        File.WriteAllBytes(Path.Combine(outDir, tag + "_builtin.png"), EncodeSrgb(imgs[0]));
                        File.WriteAllBytes(Path.Combine(outDir, tag + "_urp.png"), EncodeSrgb(imgs[1]));
                        File.WriteAllBytes(Path.Combine(outDir, tag + "_diffx4.png"), diff.EncodeToPNG());
                    }
                    sb.AppendLine($"   {view,-6} {lname,-6}: foreground px {n,7}, RMS {rms:F4}, mean |d| {mad:F4}, mean builtin {MeanOf(imgs[0]):F4} urp {MeanOf(imgs[1]):F4}; eroded px {nE,7} mean |d| {madE:F4}");
                    if (view == "mnpb")   // flat-colour patches: most common colours instead of images
                        for (int k = 0; k < 2; k++)
                            sb.AppendLine($"          {(k == 0 ? "builtin" : "urp    ")} top colours: " + string.Join("  ", imgs[k].GetPixels().Where(c => c.maxColorComponent > 1e-4f)
                                .GroupBy(c => $"{c.r:F3},{c.g:F3},{c.b:F3}").OrderByDescending(g => g.Count()).Take(5).Select(g => $"{g.Key} x{g.Count()}")));
                    Object.DestroyImmediate(imgs[0]); Object.DestroyImmediate(imgs[1]); Object.DestroyImmediate(diff);
                }
            }
        }
        sb.AppendLine($"\nALL views: mean |d| {totAbs / Math.Max(1, totN):F4} (linear, foreground-weighted)");
        sb.AppendLine($"keywords: {(DrawerKw ? "the drawers' (-drawerKw, as before 3.1)" : "as loaded")}; target materials whose loaded keywords differ from the drawers': {kwNotes.Distinct().Count()}");
        foreach (var k in kwNotes.Distinct()) sb.AppendLine("   " + k);
        File.WriteAllText(Path.Combine(FitDir, Arg("-realOut", "skin_real") + (shadows ? "_shadows" : "") + ".txt"), sb.ToString().Replace("\r\n", "\n"));
        Cleanup(charas.Select(c => c.go));
        Debug.Log("SKINFIT DONE");
    }

    // Nearest ancestor that is a clothes kind (ct_*) or an accessory slot (ca_slot*); "other" when none.
    static string PartRoot(Transform t, Transform root)
    {
        for (var p = t; p && p != root; p = p.parent)
            if (p.name.StartsWith("ct_") || p.name.StartsWith("ca_slot")) return p.name;
        return "other";
    }

    // World bounds of the skinned mesh as posed (renderer bounds are deliberately loose after assembly).
    static Bounds BakedBounds(Renderer r)
    {
        if (!(r is SkinnedMeshRenderer smr)) return r.bounds;
        var m = new Mesh(); smr.BakeMesh(m, true);
        var v = m.vertices; Object.DestroyImmediate(m);
        var b = new Bounds(smr.transform.TransformPoint(v[0]), Vector3.zero);
        foreach (var p in v) b.Encapsulate(smr.transform.TransformPoint(p));
        return b;
    }

    static Texture2D RenderBounds(Bounds b, float distScale, Vector3 lightEuler, bool shadows, RenderPipelineAsset rp)
    {
        ShaderFit.SetPipeline(rp);
        var amb = new Color(0.42f, 0.42f, 0.45f);
        var sh = new SphericalHarmonicsL2(); sh.AddAmbientLight(amb.linear);
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = amb; RenderSettings.ambientProbe = sh;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = ShaderProbe.BlackCube; RenderSettings.reflectionIntensity = 0;
        var key = new GameObject("key").AddComponent<Light>();
        key.type = LightType.Directional; key.intensity = 1; key.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        HS2Next.RenderingLayers.ApplyToLight(key);   // all layers, also under an asset with rendering layers
        key.transform.rotation = Quaternion.Euler(lightEuler);
        var cam = new GameObject("cam").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.fieldOfView = 24;
        float h = Mathf.Max(b.size.y, b.size.x) * 1.1f;
        float d = h / (2 * Mathf.Tan(12 * Mathf.Deg2Rad)) / distScale * 0.55f;
        cam.transform.position = b.center + Vector3.forward * (d + b.extents.z);   // HS2 faces +Z
        cam.transform.LookAt(b.center);
        cam.nearClipPlane = 0.05f; cam.farClipPlane = d * 4 + 10;
        const int W = 600, H = 800;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
        cam.targetTexture = rt; cam.Render(); cam.Render();
        RenderTexture.active = rt;
        var t = new Texture2D(W, H, TextureFormat.RGBAFloat, false, true);
        t.ReadPixels(new Rect(0, 0, W, H), 0, 0); t.Apply();
        RenderTexture.active = null; cam.targetTexture = null;
        Object.DestroyImmediate(rt); Object.DestroyImmediate(cam.gameObject); Object.DestroyImmediate(key.gameObject);
        return t;
    }

    static (double rms, double mad, int n, Texture2D diff) Compare(Texture2D a, Texture2D b)
    {
        var pa = a.GetPixels(); var pb = b.GetPixels(); var pd = new Color[pa.Length];
        double sq = 0, ab = 0; int n = 0;
        for (int i = 0; i < pa.Length; i++)
        {
            var d = new Color(Mathf.Abs(pa[i].r - pb[i].r), Mathf.Abs(pa[i].g - pb[i].g), Mathf.Abs(pa[i].b - pb[i].b), 1);
            pd[i] = new Color(Mathf.Min(1, d.r * 4), Mathf.Min(1, d.g * 4), Mathf.Min(1, d.b * 4), 1);
            if (pa[i].maxColorComponent <= 1e-4f && pb[i].maxColorComponent <= 1e-4f) continue;
            n++; sq += (d.r * d.r + d.g * d.g + d.b * d.b) / 3; ab += (d.r + d.g + d.b) / 3;
        }
        var t = new Texture2D(a.width, a.height, TextureFormat.RGB24, false);
        t.SetPixels(pd); t.Apply();
        return (Math.Sqrt(sq / Math.Max(1, n)), ab / Math.Max(1, n), n, t);
    }

    // Mean |d| over pixels that are foreground in both images together with their 4 neighbours (1-pixel erosion
    // of the common coverage), so alpha-edge filtering and screen-door edges do not dominate.
    static (double mad, int n) CompareEroded(Texture2D a, Texture2D b)
    {
        var pa = a.GetPixels(); var pb = b.GetPixels(); int w = a.width, h = a.height;
        var fg = new bool[pa.Length];
        for (int i = 0; i < pa.Length; i++) fg[i] = pa[i].maxColorComponent > 1e-4f && pb[i].maxColorComponent > 1e-4f;
        double ab = 0; int n = 0;
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                int i = y * w + x;
                if (!fg[i] || !fg[i - 1] || !fg[i + 1] || !fg[i - w] || !fg[i + w]) continue;
                n++; ab += (Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b)) / 3;
            }
        return (ab / Math.Max(1, n), n);
    }

    static double MeanOf(Texture2D t)
    {
        double s = 0; int n = 0;
        foreach (var p in t.GetPixels()) if (p.maxColorComponent > 1e-4f) { s += (p.r + p.g + p.b) / 3; n++; }
        return n == 0 ? 0 : s / n;
    }

    static byte[] EncodeSrgb(Texture2D lin)
    {
        var t = new Texture2D(lin.width, lin.height, TextureFormat.RGB24, false);
        t.SetPixels(lin.GetPixels().Select(c => c.gamma).ToArray()); t.Apply();
        var bytes = t.EncodeToPNG(); Object.DestroyImmediate(t); return bytes;
    }
}
