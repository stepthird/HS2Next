// Diagnostic (Editor only, not part of the suite): finds writes to materials that are shared between characters (asset
// bundle materials and anything else that outlives one build). Run:
//   -executeMethod MaterialLeakProbe.Run -list <text file: card paths, one per line> [-seq cdsc] [-noext] [-nomotion]
//   [-unload] [-verbose]
// For every card of the list (in order) the builds of -seq run (d = documents, s = documents without the hs2.kkex stores;
// default "dsd"; c, the retired Phase 1 card path, is read as d). Around each build every loaded material
// (Resources.FindObjectsOfTypeAll) is snapshotted (shader, render queue, keywords incl. invalid ones, every property):
// before the build, after CharaAssembler.Build, after the P2 snapshot reads, after Motion.Step(0) and after the cleanup.
// A material that existed at the start of a phase and changed during it is reported with the differing values (-verbose:
// also the materials first seen in a phase). -unload: release the bundles (ContentCatalog.UnloadAll +
// UnloadUnusedAssetsImmediate) after every card. Report: Spike/out/import/material_leak_probe.txt.
// This found the Phase 1 leak: the shared materials changed in the "reads" phase, i.e. the Unity Editor itself switches a
// loaded material's toggle keywords the first time its properties are read (Runtime/Rendering/MaterialCopies.cs).
//   -executeMethod MaterialLeakProbe.Trigger [-bundle <abdata bundle>] [-material <name>]: which call makes the Editor
//   change a freshly loaded material (one call per fresh load); material_trigger_probe.txt.
//   -executeMethod MaterialLeakProbe.Keywords -card <png> [-props]: every renderer slot's shader, keywords (and with -props
//   every property) with the Phase 1 material handling and the current one, Built-in and after ShaderMap.ApplyUrp;
//   material_keywords_probe.txt.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Chara;
using HS2Next.Chara.Documents;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.Import;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class MaterialLeakProbe
{
    static StringBuilder rep;
    static bool verbose;

    static string Arg(string name, string def = null)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static bool Flag(string name) => Environment.GetCommandLineArgs().Contains(name);

    static string T(float f) => f.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>The comparable state of a material: key → value.</summary>
    public static SortedDictionary<string, string> State(Material m)
    {
        var d = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var sh = m.shader;
        d["~name"] = m.name;
        d["~shader"] = sh ? sh.name + "#" + sh.GetEntityId() : "none";
        d["~queue"] = m.renderQueue + "/" + m.rawRenderQueue;
        d["~keywords"] = string.Join(",", m.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal));
        d["~valid"] = string.Join(",", m.enabledKeywords.Select(k => k.name).OrderBy(k => k, StringComparer.Ordinal));
        d["~flags"] = m.globalIlluminationFlags + " " + m.doubleSidedGI + " " + m.enableInstancing;
        if (sh)
            for (int p = 0; p < sh.GetPropertyCount(); p++)
            {
                string n = sh.GetPropertyName(p);
                switch (sh.GetPropertyType(p))
                {
                    case ShaderPropertyType.Float: case ShaderPropertyType.Range: d[n] = T(m.GetFloat(n)); break;
                    case ShaderPropertyType.Int: d[n] = m.GetInteger(n).ToString(CultureInfo.InvariantCulture); break;
                    case ShaderPropertyType.Color: { var c = m.GetColor(n); d[n] = T(c.r) + "," + T(c.g) + "," + T(c.b) + "," + T(c.a); break; }
                    case ShaderPropertyType.Vector: { var v = m.GetVector(n); d[n] = T(v.x) + "," + T(v.y) + "," + T(v.z) + "," + T(v.w); break; }
                    case ShaderPropertyType.Texture:
                        var t = m.GetTexture(n);
                        var o = m.GetTextureOffset(n); var s = m.GetTextureScale(n);
                        d[n] = (t ? t.name + "#" + t.GetEntityId() : "null") + " st " + T(s.x) + "," + T(s.y) + "," + T(o.x) + "," + T(o.y);
                        break;
                }
            }
        return d;
    }

    sealed class Entry { public Material M; public SortedDictionary<string, string> S; }

    static Dictionary<Material, Entry> Snap()
    {
        var r = new Dictionary<Material, Entry>();
        foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            if (m) r[m] = new Entry { M = m, S = State(m) };
        return r;
    }

    static string Kind(Material m) =>
        $"id {m.GetEntityId()}{(EditorUtility.IsPersistent(m) ? " persistent" : "")}{(AssetDatabase.Contains(m) ? " asset" : "")}{(m.hideFlags != HideFlags.None ? " " + m.hideFlags : "")}";

    static string Short(string s) => s == null ? "(absent)" : s.Length > 200 ? s.Substring(0, 200) + "…" : s;

    // Reports materials of `a` that changed in `b` (and, with -verbose, materials new in `b`).
    static int Diff(string phase, Dictionary<Material, Entry> a, Dictionary<Material, Entry> b, HashSet<Material> inBuild)
    {
        int n = 0;
        foreach (var kv in b)
        {
            if (!a.TryGetValue(kv.Key, out var old))
            {
                if (verbose) rep.AppendLine($"      new in {phase}: {kv.Value.M.name} ({Kind(kv.Value.M)}) shader {kv.Value.S["~shader"]} keywords [{kv.Value.S["~keywords"]}]");
                continue;
            }
            var keys = old.S.Keys.Union(kv.Value.S.Keys).Where(k => !(old.S.TryGetValue(k, out var x) && kv.Value.S.TryGetValue(k, out var y) && x == y)).ToList();
            if (keys.Count == 0) continue;
            n++;
            rep.AppendLine($"   CHANGED in {phase}: '{kv.Value.M.name}' ({Kind(kv.Value.M)}{(inBuild != null && inBuild.Contains(kv.Key) ? ", used by this build" : "")}) shader {old.S["~shader"]}:");
            foreach (var k in keys.Take(12))
                rep.AppendLine($"      {k}: {Short(old.S.TryGetValue(k, out var x) ? x : null)}  ->  {Short(kv.Value.S.TryGetValue(k, out var y) ? y : null)}");
            if (keys.Count > 12) rep.AppendLine($"      ... {keys.Count - 12} more");
        }
        return n;
    }

    static void Cleanup(HashSet<GameObject> before)
    {
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (before.Contains(go)) continue;
            var rts = new HashSet<RenderTexture>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (!m) continue;
                    foreach (var id in m.GetTexturePropertyNameIDs())
                        if (m.GetTexture(id) is RenderTexture rt) rts.Add(rt);
                }
            Object.DestroyImmediate(go);
            foreach (var rt in rts) { rt.Release(); Object.DestroyImmediate(rt); }
        }
    }

    // The reads of CharaDocBuildTest.MaterialsOf (keywords and every property of every renderer's materials).
    static int Reads(GameObject root)
    {
        int n = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
            {
                if (!m) continue;
                n += m.shaderKeywords.Length;
                var sh = m.shader;
                if (!sh) continue;
                for (int p = 0; p < sh.GetPropertyCount(); p++)
                {
                    string pn = sh.GetPropertyName(p);
                    switch (sh.GetPropertyType(p))
                    {
                        case ShaderPropertyType.Float: case ShaderPropertyType.Range: m.GetFloat(pn); break;
                        case ShaderPropertyType.Int: m.GetInteger(pn); break;
                        case ShaderPropertyType.Color: m.GetColor(pn); break;
                        case ShaderPropertyType.Vector: m.GetVector(pn); break;
                        case ShaderPropertyType.Texture: m.GetTexture(pn); break;
                    }
                }
            }
        return n;
    }

    public static void Run()
    {
        rep = new StringBuilder();
        verbose = Flag("-verbose");
        string seq = Arg("-seq", "dsd");
        var list = Arg("-list");
        var cards = list != null && File.Exists(list) ? File.ReadAllLines(list, Encoding.UTF8).Select(l => l.Trim().TrimStart('﻿')).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList() : new List<string>();
        rep.AppendLine($"material leak probe (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}); sequence {seq}; cards {cards.Count}" +
                       $"{(Flag("-noext") ? "; ext off" : "")}{(Flag("-nomotion") ? "; motion off" : "")}{(Flag("-unload") ? "; unload after every card" : "")}");
        var gd = CardResolveTest.BuildDb(null);
        var importer = CharaDocBuildTest.NewImporter(gd);
        bool motion0 = HS2Next.Motion.MotionSetup.Enabled;
        if (Flag("-nomotion")) HS2Next.Motion.MotionSetup.Enabled = false;
        int total = 0, ci = 0;
        try
        {
            foreach (var file in cards)
            {
                ci++;
                if (!File.Exists(file)) { rep.AppendLine($"card {ci}: not found"); continue; }
                var bytes = File.ReadAllBytes(file);
                rep.AppendLine();
                rep.AppendLine($"== card {ci} ({Path.GetFileName(file).Length} chars)");
                int bi = 0;
                foreach (char k in seq)
                {
                    bi++;
                    CharaBuildInput input;
                    {
                        var rt = k == 's' ? DocumentRoundTrip.Run(importer, bytes, file, gd, DocumentRoundTrip.StripKkex) : DocumentRoundTrip.Run(importer, bytes, file, gd);
                        if (!rt.Ok) { rep.AppendLine($"  build {bi} ({k}): round trip failed: {rt.Error}"); continue; }
                        input = rt.Input;
                    }
                    rep.AppendLine($"  build {bi} ({k})");
                    var before = new HashSet<GameObject>(SceneManager.GetActiveScene().GetRootGameObjects());
                    var s0 = Snap();
                    var asm = new CharaAssembler(gd);
                    if (Flag("-noext")) asm.ExtOptions = null;
                    var root = asm.Build(input);
                    var s1 = Snap();
                    var used = new HashSet<Material>(root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m));
                    total += Diff("Build", s0, s1, used);
                    rep.AppendLine($"    renderer materials {used.Count}, of them bundle/shared (existing before the build or persistent): " +
                                   used.Count(id => s0.ContainsKey(id) || (s1.TryGetValue(id, out var e) && EditorUtility.IsPersistent(e.M))));
                    if (verbose)
                        foreach (var id in used)
                            if (s1.TryGetValue(id, out var e) && (s0.ContainsKey(id) || EditorUtility.IsPersistent(e.M)))
                                rep.AppendLine($"      shared on a renderer: '{e.M.name}' ({Kind(e.M)}) shader {e.S["~shader"]} keywords [{e.S["~keywords"]}]");
                    Reads(root);
                    var s2 = Snap();
                    total += Diff("reads", s1, s2, used);
                    asm.Context.Motion?.Step(0f);
                    var s3 = Snap();
                    total += Diff("Step(0)", s2, s3, used);
                    Cleanup(before);
                    var s4 = Snap();
                    total += Diff("cleanup", s3, s4, used);
                }
                if (Flag("-unload")) { gd.UnloadAll(); EditorUtility.UnloadUnusedAssetsImmediate(true); GC.Collect(); }
            }
        }
        catch (Exception e) { rep.AppendLine("EXCEPTION " + e); }
        finally { HS2Next.Motion.MotionSetup.Enabled = motion0; }
        rep.Insert(0, $"SUMMARY: {total} material changes outside the build's own copies\n");
        gd.UnloadAll();
        string outFile = Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "import", "material_leak_probe.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outFile));
        File.WriteAllText(outFile, rep.ToString());
        Debug.Log("MATERIAL LEAK PROBE DONE: " + total);
    }

    static string Kw(Material m) => m ? "[" + string.Join(",", m.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal)) + "]" : "(null)";

    // Keywords per renderer slot of one card built with the Phase 1 material handling and with the current one, Built-in
    // and after ShaderMap.ApplyUrp: -card <png>. Report: Spike/out/import/material_keywords_probe.txt.
    public static void Keywords()
    {
        rep = new StringBuilder();
        string file = Arg("-card");
        var gd = CardResolveTest.BuildDb(null);
        Dictionary<string, string> One(bool phase1, bool urp)
        {
            HS2Next.Chara.CharaAssembler.OwnAllMaterials = !phase1;
            HS2Next.MaterialCopies.KeepSerializedKeywords = !phase1;
            gd.UnloadAll(); EditorUtility.UnloadUnusedAssetsImmediate(true);
            var before = new HashSet<GameObject>(SceneManager.GetActiveScene().GetRootGameObjects());
            var root = new CharaAssembler(gd).BuildFromDocuments(file);
            if (urp) HS2Next.ShaderMap.ApplyUrp(root);
            var d = new Dictionary<string, string>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (!m) continue;
                    string valid = string.Join(",", m.enabledKeywords.Select(k => k.name).OrderBy(k => k, StringComparer.Ordinal));
                    var st = State(m);
                    string props = string.Join(" ", st.Where(kv => kv.Key != "~name" && kv.Key != "~shader" && kv.Key != "~keywords" && kv.Key != "~valid")
                        .Select(kv => kv.Key + "=" + System.Text.RegularExpressions.Regex.Replace(kv.Value, "#-?[0-9]+(:[0-9]+)?", "")));
                    d[GetPath(r.transform) + "[" + i + "]"] = (m.shader ? m.shader.name : "-") + " | " + Kw(m) + " | valid [" + valid + "]" + (Flag("-props") ? " | " + props : "");
                }
            }
            Cleanup(before);
            return d;
        }
        try
        {
            foreach (bool urp in new[] { false, true })
            {
                var a = One(true, urp); var b = One(false, urp);
                rep.AppendLine($"== {(urp ? "URP" : "Built-in")}: {a.Count} / {b.Count} slots");
                foreach (var k in a.Keys.Union(b.Keys).OrderBy(x => x, StringComparer.Ordinal))
                {
                    a.TryGetValue(k, out var x); b.TryGetValue(k, out var y);
                    if (x != y) rep.AppendLine($"  {k}\n    phase1 {x}\n    now    {y}");
                }
            }
        }
        finally { HS2Next.Chara.CharaAssembler.OwnAllMaterials = true; HS2Next.MaterialCopies.KeepSerializedKeywords = true; }
        gd.UnloadAll();
        File.WriteAllText(Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "import", "material_keywords_probe.txt"), rep.ToString());
        Debug.Log("MATERIAL KEYWORDS PROBE DONE");
    }

    static string GetPath(Transform t)
    {
        var l = new List<string>();
        for (var x = t; x; x = x.parent) l.Add(x.name);
        l.Reverse();
        return string.Join("/", l.Skip(1));
    }

    // Which call makes the Editor change a freshly loaded bundle material: every trial loads -material from -bundle fresh
    // (bundles released before), makes one call and reads the keywords. Report: Spike/out/import/material_trigger_probe.txt.
    public static void Trigger()
    {
        rep = new StringBuilder();
        string bundle = Arg("-bundle", "chara/hair_shader_mat.unity3d"), asset = Arg("-material", "hair_dithering");
        var gd = CardResolveTest.BuildDb(null);
        var trials = new List<(string name, Func<Material, string> act)>
        {
            ("keywords read twice", m => Kw(m)),
            ("shader getter", m => m.shader ? m.shader.name : "-"),
            ("renderQueue", m => m.renderQueue.ToString()),
            ("enabledKeywords", m => string.Join(",", m.enabledKeywords.Select(k => k.name))),
            ("HasProperty", m => m.HasProperty("_Color").ToString()),
            ("GetFloat of every float property", m => { var sh = m.shader; int n = 0; for (int p = 0; p < sh.GetPropertyCount(); p++) if (sh.GetPropertyType(p) == ShaderPropertyType.Float || sh.GetPropertyType(p) == ShaderPropertyType.Range) { m.GetFloat(sh.GetPropertyName(p)); n++; } return n + " floats"; }),
            ("shader.GetPropertyCount", m => m.shader.GetPropertyCount().ToString()),
            ("shader.keywordSpace", m => m.shader.keywordSpace.keywordCount.ToString()),
            ("shader.GetPropertyAttributes", m => { var sh = m.shader; var l = new List<string>(); for (int p = 0; p < sh.GetPropertyCount(); p++) { var a = sh.GetPropertyAttributes(p); if (a.Length > 0) l.Add(sh.GetPropertyName(p) + "=" + m.GetFloat(sh.GetPropertyName(p)).ToString(CultureInfo.InvariantCulture) + "[" + string.Join("|", a) + "]"); } return string.Join(" ", l); }),
            ("new Material(m)", m => { var c = new Material(m); string r = "copy " + Kw(c); Object.DestroyImmediate(c); return r; }),
            ("new Material(m) + shader reassigned", m => { var c = new Material(m); c.shader = c.shader; string r = "copy " + Kw(c); Object.DestroyImmediate(c); return r; }),
            ("copy, MaterialEditor.ApplyMaterialPropertyDrawers(copy)", m => { var c = new Material(m); MaterialEditor.ApplyMaterialPropertyDrawers(c); string r = "copy " + Kw(c); Object.DestroyImmediate(c); return r; }),
            ("m.shader = m.shader", m => { m.shader = m.shader; return ""; }),
            ("GetTexturePropertyNameIDs", m => m.GetTexturePropertyNameIDs().Length + " ids"),
            ("GetTexture(_MainTex)", m => { var t = m.GetTexture("_MainTex"); return t ? t.name : "null"; }),
            ("MaterialCopies.Copy", m => { var c = HS2Next.MaterialCopies.Copy(m); string r = "copy " + Kw(c); Object.DestroyImmediate(c); return r; }),
            ("MaterialCopies.Copy + SetShader(copy, its shader)", m => { var c = HS2Next.MaterialCopies.Copy(m); HS2Next.MaterialCopies.SetShader(c, c.shader); string r = "copy " + Kw(c) + " valid " + string.Join(",", c.enabledKeywords.Select(k => k.name)); Object.DestroyImmediate(c); return r; }),
            ("rendered once (graphics runs only)", m =>
            {
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return "no graphics device";
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.GetComponent<Renderer>().sharedMaterial = m;
                var camGo = new GameObject("probe cam"); var cam = camGo.AddComponent<Camera>();
                cam.transform.position = new Vector3(0, 0, -2); cam.enabled = false;
                var rt = new RenderTexture(64, 64, 24); cam.targetTexture = rt; cam.Render();
                cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(camGo); Object.DestroyImmediate(q);
                return "rendered";
            }),
            ("Resources.FindObjectsOfTypeAll<Material>", m => Resources.FindObjectsOfTypeAll<Material>().Length + " materials"),
        };
        foreach (var (name, act) in trials)
        {
            gd.UnloadAll(); EditorUtility.UnloadUnusedAssetsImmediate(true); GC.Collect();
            var m = gd.LoadAsset<Material>(bundle, asset);
            if (!m) { rep.AppendLine($"{name}: {asset} not loaded from {bundle}"); continue; }
            string k0 = Kw(m);
            string r;
            try { r = act(m); } catch (Exception e) { r = "threw " + e.GetType().Name + ": " + e.Message; }
            string k1 = Kw(m);
            string k2 = Kw(m);
            rep.AppendLine($"{name}: before {k0}; result {r}; after {k1}; again {k2}");
        }
        gd.UnloadAll();
        string outFile = Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "import", "material_trigger_probe.txt");
        File.WriteAllText(outFile, rep.ToString());
        Debug.Log("MATERIAL TRIGGER PROBE DONE");
    }
}
