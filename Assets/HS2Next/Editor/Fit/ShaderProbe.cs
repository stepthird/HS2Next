// Black-box probing of compiled HS2 shaders: render a flat quad with controlled inputs under
// the Built-in pipeline and record the output color, to infer how each input is used.
// Run: -executeMethod ShaderProbe.Run -probeShader "AIT/Clothes True" -probeBundle chara/00/fo_top_00.unity3d
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class ShaderProbe
{
    static string GameRoot => CardResolveTest.GameRoot;

    static string Arg(string name, string def)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    // Shared scene for probes: ortho camera looking at a quad that fills the view.
    public class Rig : IDisposable
    {
        public Camera cam; public MeshRenderer quad; public Light light; RenderTexture rt; Texture2D read;
        public Color[] Last;   // pixels of the last Render (row-major from the bottom, size x size)
        public Rig(int size = 32)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.position = Vector3.zero;
            q.transform.rotation = Quaternion.Euler(0, 180, 0); // face the camera at +Z
            quad = q.GetComponent<MeshRenderer>();
            cam = new GameObject("probe_cam").AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = 0.25f;
            cam.transform.position = new Vector3(0, 0, 2); cam.transform.LookAt(Vector3.zero);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 10;
            light = new GameObject("probe_light").AddComponent<Light>();
            light.type = LightType.Directional;
            HS2Next.RenderingLayers.ApplyToLight(light);   // all layers, also under an asset with rendering layers
            rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            read = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
            cam.targetTexture = rt;
        }

        // mode: "ambient" = flat white ambient only; "light" = one directional light only.
        public void Lighting(string mode, Vector3 lightEuler = default, float intensity = 1)
        {
            var sh = new SphericalHarmonicsL2();
            var amb = mode == "ambient" ? Color.white : mode == "mixed" ? new Color(0.35f, 0.35f, 0.38f) : Color.black;
            sh.AddAmbientLight(amb.linear);
            RenderSettings.ambientMode = AmbientMode.Flat;
            // Built-in converts a Flat ambient color from sRGB; URP uses it as-is. Feed URP the
            // linear value so both pipelines see the same ambient.
            RenderSettings.ambientLight = amb;
            RenderSettings.ambientProbe = sh;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = BlackCube;
            RenderSettings.reflectionIntensity = 0;
            light.enabled = mode == "light" || mode == "mixed";
            light.intensity = intensity;
            light.color = Color.white;
            light.shadows = LightShadows.None;
            light.transform.rotation = Quaternion.Euler(lightEuler);
        }

        public Color Render(Material m)
        {
            quad.sharedMaterial = m;
            cam.Render();
            RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); read.Apply();
            RenderTexture.active = null;
            var px = read.GetPixels();
            Last = px;
            int n = px.Length;
            // centre half of the image only
            var c = new Color(0, 0, 0, 0); int k = 0;
            int s = rt.width;
            for (int y = s / 4; y < 3 * s / 4; y++) for (int x = s / 4; x < 3 * s / 4; x++) { c += px[y * s + x]; k++; }
            return c / k;
        }

        public void Dispose()
        {
            Object.DestroyImmediate(quad.gameObject); Object.DestroyImmediate(cam.gameObject); Object.DestroyImmediate(light.gameObject);
            Object.DestroyImmediate(rt); Object.DestroyImmediate(read);
        }
    }

    static Cubemap blackCube;
    public static Cubemap BlackCube
    {
        get
        {
            if (blackCube) return blackCube;
            blackCube = new Cubemap(4, TextureFormat.RGBA32, false);
            foreach (CubemapFace f in Enum.GetValues(typeof(CubemapFace)))
                if (f != CubemapFace.Unknown) blackCube.SetPixels(Enumerable.Repeat(Color.black, 16).ToArray(), f);
            blackCube.Apply();
            return blackCube;
        }
    }

    static readonly Dictionary<string, Texture2D> consts = new Dictionary<string, Texture2D>();
    public static Texture2D Const(Color c, bool linear = true)
    {
        string key = $"{c}{linear}";
        if (consts.TryGetValue(key, out var t) && t) return t;
        t = new Texture2D(4, 4, TextureFormat.RGBAFloat, false, linear);
        t.SetPixels(Enumerable.Repeat(c, 16).ToArray()); t.Apply();
        return consts[key] = t;
    }

    public static Texture2D Neutral(string defName)
    {
        switch (defName)
        {
            case "black": return Const(new Color(0, 0, 0, 0));
            case "gray": case "grey": return Const(new Color(0.5f, 0.5f, 0.5f, 0.5f));
            case "bump": case "normal": return Const(new Color(0.5f, 0.5f, 1, 1));
            case "red": return Const(new Color(1, 0, 0, 1));
            default: return Const(Color.white);
        }
    }

    // Material with every texture at its declared default, every color/float at default.
    public static Material Baseline(Shader sh, Material source)
    {
        var m = new Material(sh);
        if (source) { m.CopyPropertiesFromMaterial(source); m.shaderKeywords = source.shaderKeywords; }
        for (int i = 0; i < sh.GetPropertyCount(); i++)
        {
            string n = sh.GetPropertyName(i);
            switch (sh.GetPropertyType(i))
            {
                case ShaderPropertyType.Texture:
                    if (sh.GetPropertyTextureDimension(i) == TextureDimension.Tex2D)
                        m.SetTexture(n, Neutral(sh.GetPropertyTextureDefaultName(i)));
                    break;
            }
        }
        return m;
    }

    public static void Run()
    {
        string shaderName = Arg("-probeShader", "AIT/Clothes True");
        string bundleRel = Arg("-probeBundle", "chara/00/fo_top_00.unity3d");
        string outDir = Arg("-probeOut", Path.Combine(HS2Next.Content.ContentSettings.ProjectRoot, "Spike", "out", "probe"));
        Directory.CreateDirectory(outDir);
        GraphicsSettings.defaultRenderPipeline = null;  // probe under Built-in
        QualitySettings.renderPipeline = null;
        var b = AssetBundle.LoadFromFile(Path.Combine(GameRoot, "abdata", bundleRel));
        var src = b.LoadAllAssets<Material>().FirstOrDefault(m => m.shader && m.shader.name == shaderName);
        if (!src)
            src = b.LoadAllAssets<GameObject>().SelectMany(g => g.GetComponentsInChildren<Renderer>(true))
                .SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m && m.shader && m.shader.name == shaderName);
        var sb = new StringBuilder();
        if (!src) { File.WriteAllText(Path.Combine(outDir, Safe(shaderName) + ".txt"), "material not found"); b.Unload(true); return; }
        var sh = src.shader;
        sb.AppendLine($"# {shaderName}  from {bundleRel} material {src.name}  keywords [{string.Join(" ", src.shaderKeywords)}]");
        sb.AppendLine("## properties (name type default)");
        for (int i = 0; i < sh.GetPropertyCount(); i++)
        {
            var t = sh.GetPropertyType(i);
            string def = t == ShaderPropertyType.Texture ? sh.GetPropertyTextureDefaultName(i)
                : t == ShaderPropertyType.Color || t == ShaderPropertyType.Vector ? sh.GetPropertyDefaultVectorValue(i).ToString("F3")
                : sh.GetPropertyDefaultFloatValue(i).ToString("F3");
            sb.AppendLine($"  {sh.GetPropertyName(i)} {t} {def} \"{sh.GetPropertyDescription(i)}\"");
        }

        using (var rig = new Rig())
        {
            foreach (var mode in new[] { "ambient", "light" })
            {
                rig.Lighting(mode, new Vector3(20, 200, 0));
                var bm = Baseline(sh, null);
                var baseC = rig.Render(bm);
                sb.AppendLine($"\n## mode={mode}  baseline(all defaults) = {F(baseC)}");
                var srcC = rig.Render(src);
                sb.AppendLine($"  source material as-is = {F(srcC)}");
                // Each texture slot: red / green / blue / black constant.
                for (int i = 0; i < sh.GetPropertyCount(); i++)
                {
                    string n = sh.GetPropertyName(i);
                    var t = sh.GetPropertyType(i);
                    if (t == ShaderPropertyType.Texture && sh.GetPropertyTextureDimension(i) == TextureDimension.Tex2D)
                    {
                        var res = new List<string>();
                        foreach (var (lab, c) in new[] { ("R", new Color(1, 0, 0, 1)), ("G", new Color(0, 1, 0, 1)), ("B", new Color(0, 0, 1, 1)), ("K", new Color(0, 0, 0, 1)), ("A0", new Color(1, 1, 1, 0)) })
                        {
                            var m = Baseline(sh, null); m.SetTexture(n, Const(c));
                            var o = rig.Render(m); Object.DestroyImmediate(m);
                            res.Add($"{lab}={F(o)}");
                        }
                        sb.AppendLine($"  tex {n}: {string.Join("  ", res)}");
                    }
                    else if (t == ShaderPropertyType.Color)
                    {
                        var res = new List<string>();
                        foreach (var (lab, c) in new[] { ("R", new Color(1, 0, 0, 1)), ("K", new Color(0, 0, 0, 1)), ("A0", new Color(1, 1, 1, 0)) })
                        {
                            var m = Baseline(sh, null); m.SetColor(n, c);
                            var o = rig.Render(m); Object.DestroyImmediate(m);
                            res.Add($"{lab}={F(o)}");
                        }
                        sb.AppendLine($"  col {n}: {string.Join("  ", res)}");
                    }
                    else if (t == ShaderPropertyType.Float || t == ShaderPropertyType.Range)
                    {
                        var res = new List<string>();
                        foreach (var v in new[] { 0f, 1f })
                        {
                            var m = Baseline(sh, null); m.SetFloat(n, v);
                            var o = rig.Render(m); Object.DestroyImmediate(m);
                            res.Add($"{v}={F(o)}");
                        }
                        sb.AppendLine($"  flt {n}: {string.Join("  ", res)}");
                    }
                }
                Object.DestroyImmediate(bm);
            }
        }
        File.WriteAllText(Path.Combine(outDir, Safe(shaderName) + ".txt"), sb.ToString());
        b.Unload(true);
        Debug.Log("PROBE DONE " + shaderName);
    }

    public static string F(Color c) => $"({c.r:F3},{c.g:F3},{c.b:F3},{c.a:F2})";
    static string Safe(string s) => string.Concat(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
}
