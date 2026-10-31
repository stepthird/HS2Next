// Fit a URP replacement shader to an original HS2 shader: render the same probe cases under
// Built-in (original) and URP (replacement), grid-search the replacement's _HS2Fit params,
// and report per-case differences. Then compare a real asset rendered both ways.
// Run: -executeMethod ShaderFit.Run -fitShader "AIT/Clothes True" -fitBundle chara/00/fo_top_00.unity3d
//      -fitReplacement "HS2Next/AIT Clothes True"
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class ShaderFit
{
    static string GameRoot => CardResolveTest.GameRoot;
    // The output folder, created when first used (a fresh checkout has no Spike/out).
    static string FitOut { get { var d = Path.Combine(HS2Next.Content.ContentSettings.ProjectRoot, "Spike", "out", "fit"); Directory.CreateDirectory(d); return d; } }

    static string Arg(string name, string def)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    struct Case { public string name; public Action<Material> set; }
    struct Lit { public string name; public string mode; public Vector3 euler; }

    static readonly Lit[] Lights =
    {
        new Lit { name = "amb", mode = "ambient" },
        new Lit { name = "L0", mode = "light", euler = new Vector3(0, 180, 0) },
        new Lit { name = "L30", mode = "light", euler = new Vector3(0, 210, 0) },
        new Lit { name = "L60", mode = "light", euler = new Vector3(0, 240, 0) },
        new Lit { name = "L80", mode = "light", euler = new Vector3(0, 260, 0) },
        new Lit { name = "L120", mode = "light", euler = new Vector3(0, 300, 0) },
        new Lit { name = "L150", mode = "light", euler = new Vector3(0, 330, 0) },
        new Lit { name = "M30", mode = "mixed", euler = new Vector3(0, 210, 0) },
        new Lit { name = "M150", mode = "mixed", euler = new Vector3(0, 330, 0) },
    };

    static Case C(string n, Action<Material> s) => new Case { name = n, set = s };
    static Texture2D K(float r, float g, float b, float a) => ShaderProbe.Const(new Color(r, g, b, a));

    static readonly Case[] Cases =
    {
        C("base", m => { }),
        C("gloss0", m => m.SetTexture("_MetallicGlossMap", K(0, 1, 1, 0))),
        C("gloss.5", m => m.SetTexture("_MetallicGlossMap", K(0, 1, 1, 0.5f))),
        C("gloss1", m => m.SetTexture("_MetallicGlossMap", K(0, 1, 1, 1))),
        C("metal1gloss.5", m => m.SetTexture("_MetallicGlossMap", K(1, 1, 1, 0.5f))),
        C("exgloss1", m => m.SetFloat("_ExGloss", 1)),
        C("red", m => m.SetColor("_BaseColor", new Color(1, 0, 0, 1))),
        C("gray.25", m => m.SetTexture("_MainTex", ShaderProbe.Const(new Color(0.25f, 0.25f, 0.25f, 1), false))),
        C("detailK", m => m.SetTexture("_DetailGlossMap", K(0, 0, 0, 1))),
        C("carv1", m => m.SetFloat("_CarvatureStrength", 1)),
    };

    public static void Run()
    {
        string shaderName = Arg("-fitShader", "AIT/Clothes True");
        string bundleRel = Arg("-fitBundle", "chara/00/fo_top_00.unity3d");
        string replName = Arg("-fitReplacement", "HS2Next/AIT Clothes True");
        string outDir = Arg("-fitOut", FitOut);
        Directory.CreateDirectory(outDir);
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HS2Next.App.AppPipeline.MeasureAssetPath);
        var b = AssetBundle.LoadFromFile(Path.Combine(GameRoot, "abdata", bundleRel));
        var prefabs = b.LoadAllAssets<GameObject>();
        var src = prefabs.SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials)
            .FirstOrDefault(m => m && m.shader && m.shader.name == shaderName);
        var orig = src.shader;
        var repl = Shader.Find(replName);
        var sb = new StringBuilder();
        sb.AppendLine($"# fit {shaderName} -> {replName} (found {repl != null})");

        // Reference: original under Built-in.
        SetPipeline(null);
        var refv = new Dictionary<(string, string), Color>();
        using (var rig = new ShaderProbe.Rig())
            foreach (var l in Lights)
            {
                rig.Lighting(l.mode, l.euler);
                foreach (var c in Cases) { var m = ShaderProbe.Baseline(orig, null); c.set(m); refv[(c.name, l.name)] = rig.Render(m); Object.DestroyImmediate(m); }
            }

        // Grid search over _HS2Fit under URP.
        SetPipeline(urp);
        var grid = new List<Vector4>();
        grid.Add(Vector4.one);  // replacement is analytic now; keep the hook for future params
        Vector4 best = Vector4.one; double bestErr = double.MaxValue;
        Dictionary<(string, string), Color> bestV = null;
        using (var rig = new ShaderProbe.Rig())
        {
            rig.Lighting("ambient"); var warm = ShaderProbe.Baseline(repl, null); rig.Render(warm); rig.Render(warm); Object.DestroyImmediate(warm);
            foreach (var g in grid)
            {
                var v = new Dictionary<(string, string), Color>();
                double err = 0;
                foreach (var l in Lights)
                {
                    rig.Lighting(l.mode, l.euler);
                    foreach (var c in Cases)
                    {
                        var m = ShaderProbe.Baseline(repl, null); c.set(m); m.SetVector("_HS2Fit", g);
                        var o = rig.Render(m); Object.DestroyImmediate(m);
                        v[(c.name, l.name)] = o;
                        var r = refv[(c.name, l.name)];
                        err += Sq(o.r - r.r) + Sq(o.g - r.g) + Sq(o.b - r.b);
                    }
                }
                if (err < bestErr) { bestErr = err; best = g; bestV = v; }
            }
        }
        int nv = Cases.Length * Lights.Length * 3;
        sb.AppendLine($"best _HS2Fit = {best}  RMS = {Math.Sqrt(bestErr / nv):F4} (linear, over {nv} values)");
        sb.AppendLine("case/light: builtin -> urp");
        foreach (var c in Cases)
            sb.AppendLine($"  {c.name,-14} " + string.Join("  ", Lights.Select(l =>
                $"{l.name}:{refv[(c.name, l.name)].r:F3}/{bestV[(c.name, l.name)].r:F3}")));

        // Real asset: render the first prefabs with original (Built-in) vs replacement (URP).
        RealCompare(prefabs, shaderName, repl, best, urp, outDir, sb);
        SetPipeline(null);
        File.WriteAllText(Path.Combine(outDir, Safe(shaderName) + "_fit.txt"), sb.ToString());
        b.Unload(true);
        Debug.Log("FIT DONE");
    }


    // Built-in only: sweep ExGloss x MetallicGlossMap to see how they drive metallic/smoothness.
    public static void ProbeGloss()
    {
        string shaderName = Arg("-fitShader", "AIT/Clothes True");
        string bundleRel = Arg("-fitBundle", "chara/00/fo_top_00.unity3d");
        var b = AssetBundle.LoadFromFile(Path.Combine(GameRoot, "abdata", bundleRel));
        var orig = b.LoadAllAssets<GameObject>().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials)
            .First(m => m && m.shader && m.shader.name == shaderName).shader;
        SetPipeline(null);
        var sb = new StringBuilder();
        sb.AppendLine("ex mgR mgA | amb L0 L30 L60");
        using (var rig = new ShaderProbe.Rig())
            foreach (var ex in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                foreach (var r in new[] { 0f, 1f })
                    foreach (var a in new[] { 0f, 0.5f, 1f })
                    {
                        var vals = new List<string>();
                        foreach (var l in Lights.Take(4))
                        {
                            rig.Lighting(l.mode, l.euler);
                            var m = ShaderProbe.Baseline(orig, null);
                            m.SetFloat("_ExGloss", ex); m.SetTexture("_MetallicGlossMap", K(r, 1, 1, a));
                            vals.Add(rig.Render(m).r.ToString("F4")); Object.DestroyImmediate(m);
                        }
                        sb.AppendLine($"{ex} {r} {a} | {string.Join(" ", vals)}");
                    }
        File.WriteAllText(Path.Combine(FitOut, Safe(shaderName) + "_gloss.txt"), sb.ToString());
        b.Unload(true);
        Debug.Log("FIT DONE");
    }


    // Ablation on a real material: neutralize one input at a time and compare mean brightness
    // of the original (Built-in) and replacement (URP) renders of the same prefab.
    public static void Ablate()
    {
        string shaderName = Arg("-fitShader", "AIT/Clothes True");
        string bundleRel = Arg("-fitBundle", "chara/00/fo_top_00.unity3d");
        string replName = Arg("-fitReplacement", "HS2Next/AIT Clothes True");
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HS2Next.App.AppPipeline.MeasureAssetPath);
        var b = AssetBundle.LoadFromFile(Path.Combine(GameRoot, "abdata", bundleRel));
        var prefab = b.LoadAllAssets<GameObject>().First(g => g.transform.parent == null &&
            g.GetComponentsInChildren<Renderer>(true).Any(r => r.sharedMaterials.Any(m => m && m.shader.name == shaderName)));
        var repl = Shader.Find(replName);
        var go = (GameObject)Object.Instantiate(prefab);
        var rends = go.GetComponentsInChildren<Renderer>(true);
        foreach (var r in rends) r.sharedMaterials = r.sharedMaterials.Select(m => m ? new Material(m) : null).ToArray();
        var mats = rends.SelectMany(r => r.sharedMaterials).Where(m => m && m.shader.name == shaderName).ToList();
        var bb = Bounds(go);
        var cam = new GameObject("cam").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.fieldOfView = 20;
        float d = Mathf.Max(bb.size.x, bb.size.y) / (2 * Mathf.Tan(10 * Mathf.Deg2Rad)) * 1.1f;
        cam.transform.position = bb.center + new Vector3(0, 0, d + bb.extents.z); cam.transform.LookAt(bb.center);
        cam.nearClipPlane = 0.1f; cam.farClipPlane = d * 4;
        var sb = new StringBuilder($"# ablation {shaderName} on {prefab.name} ({mats.Count} mats)\n");
        var m0 = mats[0];
        sb.AppendLine("material floats: " + string.Join(" ", Enumerable.Range(0, m0.shader.GetPropertyCount())
            .Where(i => m0.shader.GetPropertyType(i) == ShaderPropertyType.Float || m0.shader.GetPropertyType(i) == ShaderPropertyType.Range)
            .Select(i => $"{m0.shader.GetPropertyName(i)}={m0.GetFloat(m0.shader.GetPropertyName(i)):0.###}")));
        sb.AppendLine("material textures: " + string.Join(" ", m0.GetTexturePropertyNames().Where(n => m0.GetTexture(n)).Select(n => $"{n}={m0.GetTexture(n).name}")));
        var variants = new List<(string, Action<Material>)>
        {
            ("as-is", m => { }),
            ("occ=white", m => m.SetTexture("_OcclusionMap", ShaderProbe.Const(Color.white))),
            ("detail1=white", m => m.SetTexture("_DetailGlossMap", ShaderProbe.Const(Color.white))),
            ("detail2=white", m => m.SetTexture("_DetailGlossMap2", ShaderProbe.Const(Color.white))),
            ("dmask=black", m => m.SetTexture("_DetailMask", ShaderProbe.Const(Color.black))),
            ("carv=0", m => m.SetFloat("_CarvatureStrength", 0)),
            ("bump=flat", m => m.SetTexture("_BumpMap", ShaderProbe.Const(new Color(0.5f, 0.5f, 1, 1)))),
            ("detailmain=black", m => m.SetTexture("_DetailMainTex", ShaderProbe.Const(new Color(0, 0, 0, 0)))),
            ("all-neutral", m => { m.SetTexture("_OcclusionMap", ShaderProbe.Const(Color.white)); m.SetTexture("_DetailMask", ShaderProbe.Const(Color.black));
                                   m.SetFloat("_CarvatureStrength", 0); m.SetTexture("_BumpMap", ShaderProbe.Const(new Color(0.5f, 0.5f, 1, 1))); }),
        };
        foreach (var (lname, mode, euler) in new[] { ("ambient", "ambient", Vector3.zero), ("light", "light", new Vector3(25, 200, 0)), ("mixed", "mixed", new Vector3(25, 200, 0)) })
        {
            sb.AppendLine($"## {lname}: variant  builtin  urp  ratio");
            using (var rig = new ShaderProbe.Rig())
            {
                rig.quad.gameObject.SetActive(false); rig.cam.enabled = false;
                rig.Lighting(mode, euler);
                foreach (var (vn, set) in variants)
                {
                    var saved = mats.Select(m => new Material(m)).ToList();
                    foreach (var m in mats) set(m);
                    SetPipeline(null);
                    foreach (var m in mats) m.shader = Shader.Find(shaderName) ?? saved[0].shader;
                    for (int k = 0; k < mats.Count; k++) mats[k].shader = saved[k].shader;
                    float bi = MeanLum(Shot(cam, 400, 400));
                    SetPipeline(urp);
                    foreach (var m in mats) m.shader = repl;
                    Shot(cam, 64, 64); // warm-up after pipeline switch
                    float ur = MeanLum(Shot(cam, 400, 400));
                    for (int k = 0; k < mats.Count; k++) mats[k].CopyPropertiesFromMaterial(saved[k]);
                    for (int k = 0; k < mats.Count; k++) mats[k].shader = saved[k].shader;
                    sb.AppendLine($"  {vn,-18} {bi:F4}  {ur:F4}  {ur / Math.Max(1e-6f, bi):F3}");
                }
            }
        }
        SetPipeline(null);
        File.WriteAllText(Path.Combine(FitOut, Safe(shaderName) + "_ablate.txt"), sb.ToString());
        Object.DestroyImmediate(go); Object.DestroyImmediate(cam.gameObject);
        b.Unload(true);
        Debug.Log("FIT DONE");
    }

    static float MeanLum(Texture2D t)
    {
        var px = t.GetPixels(); double s = 0; int n = 0;
        foreach (var p in px) if (p.maxColorComponent > 1e-4f) { s += (p.r + p.g + p.b) / 3; n++; }
        return n == 0 ? 0 : (float)(s / n);
    }


    public static void ProbeDetailMask()
    {
        string shaderName = Arg("-fitShader", "AIT/Clothes True");
        string bundleRel = Arg("-fitBundle", "chara/00/fo_top_00.unity3d");
        var b = AssetBundle.LoadFromFile(Path.Combine(GameRoot, "abdata", bundleRel));
        var orig = b.LoadAllAssets<GameObject>().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials)
            .First(m => m && m.shader && m.shader.name == shaderName).shader;
        SetPipeline(null);
        var sb = new StringBuilder();
        sb.AppendLine("detailmap mask(rgba) occScale | ambient");
        using (var rig = new ShaderProbe.Rig())
        {
            rig.Lighting("ambient");
            foreach (var which in new[] { "_DetailGlossMap" })
                foreach (var mask in new[] { new Color(1, 0, 0, 0) })
                    foreach (var sc in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                        foreach (var g in new[] { 0f, 0.125f, 0.25f, 0.375f, 0.5f, 0.625f, 0.75f, 0.875f, 1f })
                        {
                            var m = ShaderProbe.Baseline(orig, null);
                            m.SetTexture(which, K(1, g, 1, 1));
                            m.SetTexture("_DetailMask", ShaderProbe.Const(mask));
                            m.SetFloat("_DetailOcculusionScale", sc); m.SetFloat("_DetailOcculusionScale2", sc);
                            sb.AppendLine($"{which} G={g} mask={mask} scale={sc} | {rig.Render(m).r:F4}");
                            Object.DestroyImmediate(m);
                        }
        }
        File.WriteAllText(Path.Combine(FitOut, Safe(shaderName) + "_dmask.txt"), sb.ToString());
        b.Unload(true);
        Debug.Log("FIT DONE");
    }


    public static void ProbeMix()
    {
        string shaderName = Arg("-fitShader", "AIT/Clothes True");
        string bundleRel = Arg("-fitBundle", "chara/00/fo_top_00.unity3d");
        var b = AssetBundle.LoadFromFile(Path.Combine(GameRoot, "abdata", bundleRel));
        var orig = b.LoadAllAssets<GameObject>().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials)
            .First(m => m && m.shader && m.shader.name == shaderName).shader;
        SetPipeline(null);
        var sb = new StringBuilder();
        sb.AppendLine("ambient lightInt yaw | out   (base material, quad facing camera)");
        using (var rig = new ShaderProbe.Rig())
            foreach (var amb in new[] { 0f, 0.25f, 0.5f, 1f })
                foreach (var li in new[] { 0f, 0.5f, 1f, 2f })
                    foreach (var yaw in new[] { 0f, 60f, 120f })
                    {
                        rig.Lighting("light", new Vector3(0, 180 + yaw, 0), li);
                        rig.light.enabled = li > 0;
                        var sh = new SphericalHarmonicsL2(); sh.AddAmbientLight(new Color(amb, amb, amb).linear);
                        RenderSettings.ambientLight = new Color(amb, amb, amb); RenderSettings.ambientProbe = sh;
                        var m = ShaderProbe.Baseline(orig, null);
                        sb.AppendLine($"{amb} {li} {yaw} | {rig.Render(m).r:F4}");
                        Object.DestroyImmediate(m);
                    }
        File.WriteAllText(Path.Combine(FitOut, Safe(shaderName) + "_mix.txt"), sb.ToString());
        b.Unload(true);
        Debug.Log("FIT DONE");
    }

    static void RealCompare(GameObject[] prefabs, string shaderName, Shader repl, Vector4 fit, RenderPipelineAsset urp, string outDir, StringBuilder sb)
    {
        var root = new GameObject("fit_root");
        float x = 0;
        foreach (var p in prefabs.Where(g => g.transform.parent == null).Take(3))
        {
            var go = (GameObject)Object.Instantiate(p, root.transform);
            var bb = Bounds(go);
            go.transform.position += new Vector3(x - bb.min.x, -bb.min.y, -bb.center.z);
            x += bb.size.x * 1.05f;
        }
        var rends = root.GetComponentsInChildren<Renderer>(true);
        foreach (var r in rends) r.sharedMaterials = r.sharedMaterials.Select(m => m ? new Material(m) : null).ToArray();
        var all = Bounds(root);
        var cam = new GameObject("cam").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        cam.fieldOfView = 20;
        float d = Mathf.Max(all.size.x * 0.6f, all.size.y) / (2 * Mathf.Tan(10 * Mathf.Deg2Rad)) * 1.05f;
        cam.transform.position = all.center + new Vector3(0, 0, d + all.extents.z);
        cam.transform.LookAt(all.center);
        cam.nearClipPlane = 0.1f; cam.farClipPlane = d * 4;
        var light = new GameObject("sun").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1f; light.shadows = LightShadows.None;
        light.transform.rotation = Quaternion.Euler(25, 200, 0);
        Action env = () => ApplyEnv(new Color(0.35f, 0.35f, 0.38f));

        SetPipeline(null); env();
        Shot(cam, 64, 64); // warm-up after the pipeline switch
        var a = Shot(cam, 1200, 800);
        SetPipeline(urp); env();
        int swapped = 0;
        foreach (var r in rends) foreach (var m in r.sharedMaterials.Where(m => m && m.shader.name == shaderName))
            { m.shader = repl; m.SetVector("_HS2Fit", fit); swapped++; }
        Shot(cam, 64, 64); // warm-up after the pipeline switch
        var bimg = Shot(cam, 1200, 800);
        SetPipeline(null);
        double sum = 0; int n = 0; double sumAbs = 0;
        var diff = new Texture2D(a.width, a.height, TextureFormat.RGB24, false);
        var pa = a.GetPixels(); var pb = bimg.GetPixels(); var pd = new Color[pa.Length];
        for (int i = 0; i < pa.Length; i++)
        {
            bool fg = pa[i].maxColorComponent > 0.001f || pb[i].maxColorComponent > 0.001f;
            var dc = new Color(Mathf.Abs(pa[i].r - pb[i].r), Mathf.Abs(pa[i].g - pb[i].g), Mathf.Abs(pa[i].b - pb[i].b));
            pd[i] = dc * 4;
            if (!fg) continue;
            n++; sum += (dc.r * dc.r + dc.g * dc.g + dc.b * dc.b) / 3; sumAbs += (dc.r + dc.g + dc.b) / 3;
        }
        diff.SetPixels(pd); diff.Apply();
        string s = Safe(shaderName);
        File.WriteAllBytes(Path.Combine(outDir, s + "_real_builtin.png"), Encode(a));
        File.WriteAllBytes(Path.Combine(outDir, s + "_real_urp.png"), Encode(bimg));
        File.WriteAllBytes(Path.Combine(outDir, s + "_real_diffx4.png"), diff.EncodeToPNG());
        sb.AppendLine($"real asset mean linear: builtin {MeanLum(a):F4} urp {MeanLum(bimg):F4}");
        sb.AppendLine($"real asset: swapped {swapped} materials, foreground px {n}, RMS {Math.Sqrt(sum / Math.Max(1, n)):F4}, mean |d| {sumAbs / Math.Max(1, n):F4} (linear 0..1)");
        Object.DestroyImmediate(root); Object.DestroyImmediate(cam.gameObject); Object.DestroyImmediate(light.gameObject);
    }

    static byte[] Encode(Texture2D linearTex)
    {
        // Convert linear float pixels to sRGB 8-bit for viewing.
        var t = new Texture2D(linearTex.width, linearTex.height, TextureFormat.RGB24, false);
        t.SetPixels(linearTex.GetPixels().Select(c => c.gamma).ToArray()); t.Apply();
        return t.EncodeToPNG();
    }

    static Texture2D Shot(Camera cam, int w, int h)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
        cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt;
        var t = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
        t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply();
        RenderTexture.active = null; cam.targetTexture = null; Object.DestroyImmediate(rt);
        return t;
    }

    static void ApplyEnv(Color ambient)
    {
        var sh = new SphericalHarmonicsL2(); sh.AddAmbientLight(ambient.linear);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = ambient;
        RenderSettings.ambientProbe = sh;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = ShaderProbe.BlackCube; RenderSettings.reflectionIntensity = 0;
    }

    public static void SetPipeline(RenderPipelineAsset rp)
    {
        GraphicsSettings.defaultRenderPipeline = rp;
        QualitySettings.renderPipeline = rp;
    }

    static double Sq(float v) => v * v;
    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
    }
    static string Safe(string s) => string.Concat(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
}
