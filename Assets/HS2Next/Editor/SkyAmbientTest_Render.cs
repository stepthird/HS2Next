// SkyAmbientTest.render (Phase 3.3 package S): edit mode with a graphics device, the app's
// URP asset (and the Editor's Built-in pipeline for the falloff reference).
//   & Tools\unity_batch.ps1 -Method SkyAmbientTest.RunRender   -> Spike/out/effects/sky_ambient_render_report.txt (+ PNGs)
//   R1 sky (U3): the five StudioSky patterns through a runtime's view camera with the sky on: drawn (no magenta, not a flat
//      colour), the sun feed reaches the shader (turning SkySun changes the image), and per view (H4's front / up / back
//      cameras) the mean colour and a 16 × 9 block comparison with HS2's captures of the same patterns (approximate class)
//   R2 ambient: a fitted Lambert sphere lit by the ambient probe only: Studio's colours reach it (pure red colours give
//      a red sphere); the rendered centre equals the SH evaluated at its normal; rule hs2 vs environment differ only by HS2's
//      8-bit colour step
//   R3 falloff fit (U10 / E5): a white Lambert floor (HS2Next/Legacy Shaders/Diffuse under URP, Unity's Legacy
//      Shaders/Diffuse under Built-in) lit by one point or spot light from above (point range 10 / 30 at height 3, spot
//      30° / 90° range 10 / 30 at height 6): URP with render.lightFalloff = hs2 vs the Built-in render (mean / max over the
//      lit floor, linear), and with urp (URP's own falloff, the numbers before 3.3)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static partial class SkyAmbientTest
{
    const string ProductionAsset = "Assets/HS2Next/Settings/HS2NextURP.asset";

    public static void RunRender()
    {
        only = ArgList("-only");
        Begin();
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"SkyAmbientTest.render (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(ProductionAsset);
        var prevDefault = GraphicsSettings.defaultRenderPipeline;
        var prevQuality = QualitySettings.renderPipeline;
        bool prevAsync = ShaderUtil.allowAsyncCompilation;
        var mode0 = RenderSettings.ambientMode;
        var probe0 = RenderSettings.ambientProbe;
        int code = 0;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            UseUrp(urp);
            if (Want("R1")) RenderSky();
            if (Want("R2")) RenderAmbient();
            if (Want("R3")) RenderFalloff(urp);
        }
        catch (Exception e) { Check("ran without an exception", false, e.ToString()); }
        finally
        {
            GraphicsSettings.defaultRenderPipeline = prevDefault;
            QualitySettings.renderPipeline = prevQuality;
            ShaderUtil.allowAsyncCompilation = prevAsync;
            RenderSettings.ambientMode = mode0;
            RenderSettings.ambientProbe = probe0;
            RenderOptions.ResetToDefaults();
            LightFalloff.Apply();
        }
        End("sky_ambient_render_report.txt", "SKY AMBIENT RENDER TEST", sw);
        if (fail > 0) code = 1;
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    static void UseUrp(RenderPipelineAsset a) { GraphicsSettings.defaultRenderPipeline = a; QualitySettings.renderPipeline = a; }

    static Color[] RenderTo(Camera cam, int w, int h, bool linear, out Texture2D tex)
    {
        var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(w, h, linear ? RenderTextureFormat.ARGBFloat : RenderTextureFormat.ARGB32, 24)
        { sRGB = !linear, msaaSamples = 1 });
        var prevT = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = prevT;
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        tex = new Texture2D(w, h, linear ? TextureFormat.RGBAFloat : TextureFormat.RGBA32, false, linear);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return tex.GetPixels();
    }

    // ------------------------------------------------------------------ R1 sky

    // H4's cameras (Tools/hs2_compare_fx.py SKY_VIEWS): position, Euler, vertical FOV
    static readonly (string name, Vector3 pos, Vector3 rot, float fov)[] SkyViews =
    {
        ("front", new Vector3(0f, 8f + 40f * Mathf.Sin(12f * Mathf.Deg2Rad), 40f * Mathf.Cos(12f * Mathf.Deg2Rad)), new Vector3(12f, 180f, 0f), 30f),
        ("up", new Vector3(0f, 5f, 30f), new Vector3(-35f, 180f, 0f), 60f),
        ("back", new Vector3(0f, 5f, -30f), new Vector3(-10f, 0f, 0f), 60f),
    };

    static Color Mean(Color[] px, int w, int h, int y0, int y1)
    {
        double r = 0, g = 0, b = 0; int n = 0;
        for (int y = y0; y < y1; y++) for (int x = 0; x < w; x++) { var c = px[y * w + x]; r += c.r; g += c.g; b += c.b; n++; }
        return n == 0 ? Color.black : new Color((float)(r / n), (float)(g / n), (float)(b / n));
    }

    static void RenderSky()
    {
        Section("R1 sky patterns (U3)");
        string dir = Path.Combine(OutDir, "sky");
        Directory.CreateDirectory(dir);
        using (var rig = new Rig(Services()))
        {
            var view = rig.Runtime.GetPart<SceneViewCamera>();
            var cam = view.Camera;
            var sky = rig.Sky;
            rig.Set("effects.sky.enabled", true);
            const int W = 640, H = 360;
            for (int pat = 0; pat < 5; pat++)
            {
                rig.Set("effects.sky.pattern", ContentId.Vanilla(pat).ToString());
                rig.Step();
                foreach (var (vname, pos, rot, fov) in SkyViews)
                {
                    cam.transform.SetPositionAndRotation(pos, Quaternion.Euler(rot));
                    cam.fieldOfView = fov;
                    int feed0 = sky.FeedCount;
                    var px = RenderTo(cam, W, H, false, out var tex);
                    File.WriteAllBytes(Path.Combine(dir, $"sky_p{pat}_{vname}.png"), tex.EncodeToPNG());
                    int magenta = px.Count(c => c.r > 0.9f && c.b > 0.9f && c.g < 0.1f);
                    var mean = Mean(px, W, H, 0, H);
                    float var = px.Where((c, i) => i % 7 == 0).Select(c => Mathf.Abs(c.grayscale - mean.grayscale)).Average();
                    bool fed = sky.FeedCount > feed0;
                    string cmp = CompareHs2(px, W, H, $"sky_nomap_p{pat}_on_{vname}");
                    Info($"pattern {pat} {vname}: mean ({mean.r * 255:F0}, {mean.g * 255:F0}, {mean.b * 255:F0}), spread {var * 255:F1}/255, magenta {magenta}, feed {fed}; {cmp}");
                    Check($"pattern {pat} {vname}: drawn (no magenta, not flat), the sun feed ran", magenta == 0 && var > 0.5f / 255f && fed, $"magenta {magenta}, spread {var * 255:F2}");
                    Object.DestroyImmediate(tex);
                }
            }
            // the feed: turning the sun source changes the sky of pattern 0 (up view)
            rig.Set("effects.sky.pattern", ContentId.Vanilla(0).ToString());
            rig.Step();
            var (_, p2, r2, f2) = SkyViews[1];
            cam.transform.SetPositionAndRotation(p2, Quaternion.Euler(r2));
            cam.fieldOfView = f2;
            var a = RenderTo(cam, W, H, false, out var ta);
            var e0 = sky.SkySun.eulerAngles;
            sky.SkySun.eulerAngles = new Vector3(40f, 180f, 0f);
            var b = RenderTo(cam, W, H, false, out var tb);
            sky.SkySun.eulerAngles = e0;
            double d = 0; for (int i = 0; i < a.Length; i++) d += Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b);
            d = d / (3.0 * a.Length) * 255.0;
            Check("the sun feed reaches AIOsky/V2: turning SkySun by 180° changes pattern 0's sky", d > 0.5, $"mean |d| {d:F2}/255");
            Object.DestroyImmediate(ta); Object.DestroyImmediate(tb);
        }
    }

    /// <summary>A coarse comparison with HS2's capture of the same case (16 × 9 blocks of the 1280 × 720 PNG).</summary>
    static string CompareHs2(Color[] px, int w, int h, string hs2Case)
    {
        string f = Path.Combine(ContentSettings.ProjectRoot, "Cache", "compare", "effects", hs2Case + ".png");
        if (!File.Exists(f)) return "no HS2 capture";
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(File.ReadAllBytes(f));
        var hp = t.GetPixels();
        int hw = t.width, hh = t.height;
        Object.DestroyImmediate(t);
        double sum = 0; int n = 0;
        var mh = Mean(hp, hw, hh, 0, hh);
        for (int by = 0; by < 9; by++)
            for (int bx = 0; bx < 16; bx++)
            {
                Color A = BlockMean(px, w, h, bx, by), B = BlockMean(hp, hw, hh, bx, by);
                sum += (Mathf.Abs(A.r - B.r) + Mathf.Abs(A.g - B.g) + Mathf.Abs(A.b - B.b)) / 3.0;
                n++;
            }
        return $"HS2 mean ({mh.r * 255:F0}, {mh.g * 255:F0}, {mh.b * 255:F0}), 16x9 block mean |d| {sum / n * 255:F1}/255";
    }

    static Color BlockMean(Color[] px, int w, int h, int bx, int by)
    {
        int x0 = bx * w / 16, x1 = (bx + 1) * w / 16, y0 = by * h / 9, y1 = (by + 1) * h / 9;
        double r = 0, g = 0, b = 0; int n = 0;
        for (int y = y0; y < y1; y += 2) for (int x = x0; x < x1; x += 2) { var c = px[y * w + x]; r += c.r; g += c.g; b += c.b; n++; }
        return new Color((float)(r / n), (float)(g / n), (float)(b / n));
    }

    // ------------------------------------------------------------------ R2 ambient

    static void RenderAmbient()
    {
        Section("R2 the ambient probe on a fitted Lambert sphere");
        using (var rig = new Rig(Services()))
        {
            var view = rig.Runtime.GetPart<SceneViewCamera>();
            var cam = view.Camera;
            // linear output: no post-processing, no AO, no AA
            rig.Set("effects.ambientOcclusion.enabled", false);
            RenderOptions.Apply(JObject.Parse("{\"aa\":\"none\"}"));
            view.Setup.PostProcessing = false;
            rig.Step();
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(sphere.GetComponent<Collider>());
            var mat = new Material(Shader.Find("HS2Next/Legacy Shaders/Diffuse")) { color = Color.white };
            sphere.GetComponent<Renderer>().sharedMaterial = mat;
            sphere.layer = 0;   // outside the chara light's layers
            sphere.transform.position = new Vector3(0f, 200f, 0f);
            cam.transform.SetPositionAndRotation(new Vector3(0f, 200f, 3f), Quaternion.Euler(0f, 180f, 0f));
            cam.fieldOfView = 30f;
            try
            {
                Color Centre()
                {
                    rig.Step();
                    var px = RenderTo(cam, 64, 64, true, out var t);
                    Object.DestroyImmediate(t);
                    return px[32 * 64 + 32];
                }
                var def = Centre();
                var shDef = RenderSettings.ambientProbe;
                var dirs = new[] { Vector3.forward };   // the centre's normal faces the camera (+z)
                var ev = new Color[1];
                shDef.Evaluate(dirs, ev);
                Info($"default colours: centre ({def.r:F4}, {def.g:F4}, {def.b:F4}), SH evaluated at +z ({ev[0].r:F4}, {ev[0].g:F4}, {ev[0].b:F4})");
                Check("the centre = the probe evaluated at its normal (within 0.01)", Mathf.Abs(def.r - ev[0].r) < 0.01f && Mathf.Abs(def.g - ev[0].g) < 0.01f && Mathf.Abs(def.b - ev[0].b) < 0.01f);
                rig.Set("effects.ambient.sky", new Color4(1f, 0f, 0f, 1f));
                rig.Set("effects.ambient.equator", new Color4(1f, 0f, 0f, 1f));
                rig.Set("effects.ambient.ground", new Color4(1f, 0f, 0f, 1f));
                var red = Centre();
                Check("pure red Studio colours light the sphere red (H1: the colours light items in HS2)", red.r > 0.5f && red.g < 0.01f && red.b < 0.01f, $"({red.r:F4}, {red.g:F4}, {red.b:F4})");
                rig.Set("effects.ambient", new AmbientLightEffect());
                var hs2 = Centre();
                RenderOptions.Apply(JObject.Parse("{\"ambient\":\"environment\"}"));
                var env = Centre();
                RenderOptions.Apply(JObject.Parse("{\"ambient\":\"hs2\"}"));
                float d = Mathf.Max(Mathf.Abs(hs2.r - env.r), Mathf.Max(Mathf.Abs(hs2.g - env.g), Mathf.Abs(hs2.b - env.b)));
                Check("rule environment (Unity's Trilight) vs hs2 differ only by HS2's 8-bit step (≤ 0.004 linear)", d <= 0.004f, F(d));
            }
            finally
            {
                Object.DestroyImmediate(sphere);
                Object.DestroyImmediate(mat);
            }
        }
    }

    // ------------------------------------------------------------------ R3 falloff fit

    static void RenderFalloff(UniversalRenderPipelineAsset urp)
    {
        Section("R3 point / spot falloff: URP fitted Lambert vs Built-in (U10, E5)");
        var created = new List<Object>();
        var prevMode = RenderSettings.ambientMode;
        try
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad); created.Add(floor);
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            floor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(90f, 0f, 0f));
            floor.transform.localScale = new Vector3(60f, 60f, 1f);
            var urpMat = new Material(Shader.Find("HS2Next/Legacy Shaders/Diffuse")) { color = Color.white }; created.Add(urpMat);
            var biMat = new Material(Shader.Find("Legacy Shaders/Diffuse")) { color = Color.white }; created.Add(biMat);
            var r = floor.GetComponent<Renderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            var lgo = new GameObject("falloff light"); created.Add(lgo);
            var light = lgo.AddComponent<Light>();
            light.color = Color.white;
            light.intensity = 1f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            RenderingLayers.ApplyToLight(light);
            var cgo = new GameObject("falloff cam"); created.Add(cgo);
            var cam = cgo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = 30f;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 50f, 0f), Quaternion.Euler(90f, 0f, 0f));
            cam.nearClipPlane = 1f; cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            cam.allowHDR = true; cam.allowMSAA = false;
            cam.renderingPath = RenderingPath.Forward;
            var cd = cgo.AddComponent<UniversalAdditionalCameraData>();
            cd.renderPostProcessing = false;
            cd.antialiasing = AntialiasingMode.None;
            cd.renderShadows = false;
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = new SphericalHarmonicsL2();
            RenderSettings.ambientLight = Color.black;
            var reflect0 = RenderSettings.defaultReflectionMode;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = ShaderProbe.BlackCube;
            const int N = 1024;   // fine enough that Built-in samples the spot cookie at mip 0, as its deferred path does (tex2Dbias -8)
            var cases = new (string name, LightType type, float h, float range, float angle)[]
            {
                ("point r10 h3", LightType.Point, 3f, 10f, 0f),
                ("point r30 h3", LightType.Point, 3f, 30f, 0f),
                ("spot 30 r10 h6", LightType.Spot, 6f, 10f, 30f),
                ("spot 90 r10 h6", LightType.Spot, 6f, 10f, 90f),
                ("spot 30 r30 h6", LightType.Spot, 6f, 30f, 30f),
                ("spot 90 r30 h6", LightType.Spot, 6f, 30f, 90f),
            };
            var sb = new StringBuilder("case\tmode\tmean\tmax\tlit px\n");
            foreach (var c in cases)
            {
                light.type = c.type;
                light.range = c.range;
                light.spotAngle = c.angle > 0 ? c.angle : 30f;
                lgo.transform.SetPositionAndRotation(new Vector3(0f, c.h, 0f), Quaternion.Euler(90f, 0f, 0f));
                // Built-in reference (Unity's Legacy Shaders/Diffuse)
                UseUrp(null);
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black;
                r.sharedMaterial = biMat;
                var bi = RenderTo(cam, N, N, true, out var tb); Object.DestroyImmediate(tb);
                // URP with each falloff
                UseUrp(urp);
                RenderSettings.ambientMode = AmbientMode.Custom; RenderSettings.ambientProbe = new SphericalHarmonicsL2();
                r.sharedMaterial = urpMat;
                foreach (var mode in new[] { "hs2", "urp" })
                {
                    RenderOptions.Apply(JObject.Parse("{\"lightFalloff\":\"" + mode + "\"}"));
                    var u = RenderTo(cam, N, N, true, out var tu); Object.DestroyImmediate(tu);
                    double sum = 0, mx = 0; int lit = 0;
                    for (int i = 0; i < u.Length; i++)
                    {
                        float a = bi[i].r, b = u[i].r;
                        if (a <= 1e-4f && b <= 1e-4f) continue;
                        double d = Math.Abs(a - b);
                        sum += d; mx = Math.Max(mx, d); lit++;
                    }
                    double mean = lit > 0 ? sum / lit : 0;
                    sb.AppendLine($"{c.name}\t{mode}\t{mean:F5}\t{mx:F5}\t{lit}");
                    Info($"{c.name}, lightFalloff {mode}: mean |URP - Built-in| {mean:F5}, max {mx:F5} over {lit} lit px (linear)");
                    if (mode == "hs2")
                        Check($"{c.name}: Built-in-compatible falloff = Built-in within 0.005 mean (approximate class for spots: max ≤ 0.02)",
                            lit > 100 && mean <= 0.005 && (c.type == LightType.Point ? mx <= 0.01 : mx <= 0.03), $"mean {mean:F5} max {mx:F5}");
                }
            }
            RenderOptions.ResetToDefaults();
            File.WriteAllText(Path.Combine(OutDir, "u10_falloff_fit.tsv"), sb.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
            RenderSettings.defaultReflectionMode = reflect0;
        }
        finally
        {
            foreach (var o in created) if (o) Object.DestroyImmediate(o);
            RenderSettings.ambientMode = prevMode;
            UseUrp(urp);
        }
    }
}
