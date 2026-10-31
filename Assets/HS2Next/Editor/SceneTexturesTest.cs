// Render test of the scene depth / opaque textures and the shaders that waited for them (
// row SceneTexturesTest.render, U7 / U8; Phase 3.3 package C). Edit mode, graphics device.
// Run: Tools/unity_batch.ps1 -Method SceneTexturesTest.RunRender [-MethodArgs '-only','T1,T6']. Report:
// Spike/out/effects/scene_textures_report.txt ("SUMMARY: N PASS, M FAIL" first and last), images Spike/out/effects/textures/.
//   T1 settings: the production asset has the depth texture and the full-resolution opaque texture, its renderer copies depth
//      after the opaques, runs post-processing and carries [SSAO (active, stored intensity 0: no pass without the effect
//      part), HS2DofFeature]; the measurement asset has neither
//      texture (the fit baseline stays as measured)
//   T2 SceneTextures: _HS2NextSceneDepthValid / _HS2NextSceneOpaqueValid are 1 / 1 for a camera under the production asset,
//      0 / 0 under the measurement asset and 0 for a camera that turns the texture off
//   T3 shaders: the five shaders of the package compile without errors; ShaderMap maps KriptoFX/ME/Distortion to the new
//      HS2Next/Compatible/KriptoFX/ME/Distortion (no longer to HS2Next/Hidden/NoDraw)
//   T4 soft particles (U7): HS2Next/Legacy Shaders/Particles/Additive (Soft) in front of an opaque wall fades with HS2's
//      saturate(_InvFade (scene depth - particle depth)) under the production asset (within 0.02), not under the measurement asset
//   T5 AIT/main Light depth fade (U7): the SkinFit regression cases mainlight_depth.txt (Built-in's depth fade) with the
//      production asset: URP vs Built-in (SkinFit's TOTAL RMS)
//   T6 tears refraction (U8): the SkinFit cases namida_refraction.txt with the production asset (target RMS <= 0.001;
//      0.0029 without refraction)
//   T7 KriptoFX/ME/Distortion: a distortion quad over a checker draws the shifted checker under the production asset (not
//      black, not the background), nothing under the measurement asset
//   T8 water and slime: HS2Next/Compatible/Water fades out at a shoreline (full surface further out); the slime distorts the background;
//      no NaN; both draw as before under the measurement asset
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HS2Next;
using HS2Next.App;
using HS2Next.Content;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static partial class SceneTexturesTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "effects");
    static string FitDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "fit");
    static string FitCases => Path.Combine(ContentSettings.ProjectRoot, "Tests", "FitCases");
    static readonly StringBuilder sb = new StringBuilder();
    static int pass, fail;
    static readonly List<Object> trash = new List<Object>();

    public static readonly string[] OwnShaders =
    {
        "HS2Next/Hidden/HS2Dof", "HS2Next/AIT main namida", "HS2Next/Compatible/KriptoFX/ME/Distortion", "HS2Next/Compatible/Water", "HS2Next/Effects/SlimeCutOut",
    };

    static string Arg(string name, string def)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static void Check(string id, bool ok, string detail)
    {
        if (ok) pass++; else fail++;
        string line = $"{(ok ? "PASS" : "FAIL")} {id}: {detail}";
        sb.AppendLine(line);
        Debug.Log("SCENE TEXTURES " + line);
    }

    static void Info(string line) { sb.AppendLine("     " + line); Debug.Log("SCENE TEXTURES " + line); }

    static T Keep<T>(T o) where T : Object { trash.Add(o); return o; }

    static void Cleanup()
    {
        foreach (var o in trash) if (o) Object.DestroyImmediate(o);
        trash.Clear();
    }

    static void SetPipeline(RenderPipelineAsset a) { GraphicsSettings.defaultRenderPipeline = a; QualitySettings.renderPipeline = a; }

    public static void RunRender()
    {
        sb.Clear(); pass = fail = 0;
        Directory.CreateDirectory(Path.Combine(OutDir, "textures"));
        sb.AppendLine($"scene textures render test {DateTime.Now:yyyy-MM-dd HH:mm:ss}, Unity {Application.unityVersion}, device {SystemInfo.graphicsDeviceType}");
        var prevDefault = GraphicsSettings.defaultRenderPipeline;
        var prevQuality = QualitySettings.renderPipeline;
        bool prevAsync = ShaderUtil.allowAsyncCompilation;
        var only = Arg("-only", null)?.Split(',', ';', '+');
        bool Want(string id) => only == null || only.Contains(id);
        var prod = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AppPipeline.EditorAssetPath);
        var spike = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AppPipeline.MeasureAssetPath);
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            Run("T1", () => Settings(prod, spike));
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !prod || !spike) Check("T2-T8", false, "no graphics device or asset");
            else
            {
                if (Want("T2")) Run("T2", () => Globals(prod, spike));
                if (Want("T3")) Run("T3", Shaders);
                if (Want("T4")) Run("T4", () => SoftParticles(prod, spike));
                if (Want("T7")) Run("T7", () => Distortion(prod, spike));
                if (Want("T8")) Run("T8", () => WaterSlime(prod, spike));
                if (Want("T5") || Want("T6")) Run("T5/T6", () => Fits(Want("T5"), Want("T6")));
            }
        }
        catch (Exception e) { Check("run", false, "exception " + e); }
        finally
        {
            Cleanup();
            SetPipeline(prevDefault); GraphicsSettings.defaultRenderPipeline = prevDefault; QualitySettings.renderPipeline = prevQuality;
            ShaderUtil.allowAsyncCompilation = prevAsync;
        }
        string summary = $"SUMMARY: {pass} PASS, {fail} FAIL";
        File.WriteAllText(Path.Combine(OutDir, "scene_textures_report.txt"), summary + "\n" + sb + summary + "\n");
        Debug.Log("SCENE TEXTURES TEST DONE " + summary);
        if (Application.isBatchMode) EditorApplication.Exit(fail == 0 && pass > 0 ? 0 : 1);
    }

    static void Run(string id, Action a)
    {
        try { a(); }
        catch (Exception e) { Check(id, false, "exception " + e); }
        finally { Cleanup(); }
    }

    // ------------------------------------------------------------ helpers

    static Camera NewCamera(bool post = false)
    {
        var go = Keep(new GameObject("st_cam"));
        var cam = go.AddComponent<Camera>();
        cam.transform.position = Vector3.zero; cam.transform.rotation = Quaternion.identity;
        cam.fieldOfView = 40; cam.nearClipPlane = 0.1f; cam.farClipPlane = 100;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        cam.allowHDR = true; cam.allowMSAA = false;
        var urp = go.AddComponent<UniversalAdditionalCameraData>();
        urp.renderPostProcessing = post; urp.antialiasing = AntialiasingMode.None; urp.renderShadows = false;
        return cam;
    }

    /// <summary>A camera-facing quad of size s at (x, y, z) with white vertex colours (particle shaders read them).</summary>
    static GameObject Quad(Vector3 c, float s, Material m)
    {
        var go = Keep(new GameObject("st_quad"));
        var mesh = Keep(new Mesh());
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        mesh.normals = Enumerable.Repeat(new Vector3(0, 0, -1), 4).ToArray();
        mesh.tangents = Enumerable.Repeat(new Vector4(1, 0, 0, 1), 4).ToArray();
        mesh.colors = Enumerable.Repeat(Color.white, 4).ToArray();
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off;
        go.transform.position = c; go.transform.localScale = new Vector3(s, s, 1);
        return go;
    }

    static Material Mat(string shader)
    {
        var sh = Shader.Find(shader);
        if (!sh) throw new Exception("shader not found: " + shader);
        return Keep(new Material(sh));
    }

    static Material Unlit(Color c, Texture t = null)
    {
        var m = Mat("Universal Render Pipeline/Unlit");
        m.SetColor("_BaseColor", c);
        if (t) m.SetTexture("_BaseMap", t);
        return m;
    }

    static Texture2D Checker(int n, int cells = 8)
    {
        var t = Keep(new Texture2D(n * cells, n * cells, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
        var px = new Color32[t.width * t.height];
        for (int y = 0; y < t.height; y++) for (int x = 0; x < t.width; x++)
            px[y * t.width + x] = ((x / n + y / n) & 1) == 0 ? new Color32(220, 120, 40, 255) : new Color32(30, 60, 200, 255);
        t.SetPixels32(px); t.Apply();
        return t;
    }

    static Texture2D Flat(Color c)
    {
        var t = Keep(new Texture2D(4, 4, TextureFormat.RGBA32, false, true));
        t.SetPixels(Enumerable.Repeat(c, 16).ToArray()); t.Apply();
        return t;
    }

    static Color[] Capture(Camera cam, int w, int h, string png = null)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
        cam.targetTexture = rt;
        cam.Render(); cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var t = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
        t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply();
        RenderTexture.active = prev; cam.targetTexture = null;
        var px = t.GetPixels();
        if (png != null) SavePng(px, w, h, png);
        Object.DestroyImmediate(t); Object.DestroyImmediate(rt);
        return px;
    }

    internal static void SavePng(Color[] px, int w, int h, string name)
    {
        Directory.CreateDirectory(Path.Combine(OutDir, "textures"));
        var o = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
        o.SetPixels(px.Select(c => new Color(Mathf.LinearToGammaSpace(Mathf.Clamp01(c.r)), Mathf.LinearToGammaSpace(Mathf.Clamp01(c.g)), Mathf.LinearToGammaSpace(Mathf.Clamp01(c.b)), 1)).ToArray());
        o.Apply();
        File.WriteAllBytes(Path.Combine(OutDir, "textures", name + ".png"), o.EncodeToPNG());
        Object.DestroyImmediate(o);
    }

    static bool AnyNaN(Color[] px) => px.Any(c => float.IsNaN(c.r) || float.IsNaN(c.g) || float.IsNaN(c.b) || float.IsInfinity(c.r) || float.IsInfinity(c.g) || float.IsInfinity(c.b));

    /// <summary>Mean of |a - b| over the rectangle (fractions of the frame) and the mean luminance of a there.</summary>
    static (float diff, float lum) Region(Color[] a, Color[] b, int w, int h, float x0, float x1, float y0, float y1)
    {
        double d = 0, l = 0; int n = 0;
        for (int y = (int)(y0 * h); y < (int)(y1 * h); y++) for (int x = (int)(x0 * w); x < (int)(x1 * w); x++)
        {
            int i = y * w + x;
            d += (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b)) / 3;
            l += (a[i].r + a[i].g + a[i].b) / 3;
            n++;
        }
        return ((float)(d / Math.Max(n, 1)), (float)(l / Math.Max(n, 1)));
    }

    // ------------------------------------------------------------ T1

    static void Settings(UniversalRenderPipelineAsset prod, UniversalRenderPipelineAsset spike)
    {
        var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(SceneTexturesAssets.RendererPath);
        bool ok = prod && rd && prod.supportsCameraDepthTexture && prod.supportsCameraOpaqueTexture && prod.opaqueDownsampling == Downsampling.None &&
                  rd.copyDepthMode == CopyDepthMode.AfterOpaques && rd.postProcessData &&
                  rd.rendererFeatures.Count >= 2 && rd.rendererFeatures[0] is ScreenSpaceAmbientOcclusion && rd.rendererFeatures[0].isActive &&
                  // the fog pass may follow the SSAO (it draws before the transparents); the DOF comes next and is active
                  rd.rendererFeatures.Skip(1).SkipWhile(f => f is HS2FogFeature).FirstOrDefault() is HS2DofFeature dofF && dofF.isActive &&
                  new SerializedObject(rd.rendererFeatures[0]).FindProperty("m_Settings.Intensity").floatValue == 0f;
        Check("T1.production", ok, SceneTexturesAssets.Describe());
        Check("T1.spike", spike && !spike.supportsCameraDepthTexture && !spike.supportsCameraOpaqueTexture && !SceneTextures.CopiesDepthBeforeTransparents(spike),
            spike ? $"measurement asset: depth texture {spike.supportsCameraDepthTexture}, opaque texture {spike.supportsCameraOpaqueTexture}, copies depth before transparents {SceneTextures.CopiesDepthBeforeTransparents(spike)}" : "missing");
    }

    // ------------------------------------------------------------ T2

    static void Globals(UniversalRenderPipelineAsset prod, UniversalRenderPipelineAsset spike)
    {
        var cam = NewCamera();
        Quad(new Vector3(0, 0, 5), 4, Unlit(Color.gray));
        SetPipeline(prod);
        Capture(cam, 64, 36);
        bool pd = SceneTextures.LastDepthValid, po = SceneTextures.LastOpaqueValid;
        float gd = Shader.GetGlobalFloat(SceneTextures.DepthValidId), go = Shader.GetGlobalFloat(SceneTextures.OpaqueValidId);
        var urp = cam.GetComponent<UniversalAdditionalCameraData>();
        urp.requiresDepthOption = CameraOverrideOption.Off; urp.requiresColorOption = CameraOverrideOption.Off;
        Capture(cam, 64, 36);
        bool od = SceneTextures.LastDepthValid, oo = SceneTextures.LastOpaqueValid;
        urp.requiresDepthOption = CameraOverrideOption.UsePipelineSettings; urp.requiresColorOption = CameraOverrideOption.UsePipelineSettings;
        SetPipeline(spike);
        Capture(cam, 64, 36);
        bool sd = SceneTextures.LastDepthValid, so = SceneTextures.LastOpaqueValid;
        float sgd = Shader.GetGlobalFloat(SceneTextures.DepthValidId);
        SetPipeline(prod);
        Check("T2.production", pd && po && gd == 1f && go == 1f, $"production: depth {pd} opaque {po} (globals {gd} / {go})");
        Check("T2.cameraOff", !od && !oo, $"camera overrides off: depth {od} opaque {oo}");
        Check("T2.spike", !sd && !so && sgd == 0f, $"measurement asset: depth {sd} opaque {so} (global {sgd})");
    }

    // ------------------------------------------------------------ T3

    static void Shaders()
    {
        var bad = new List<string>();
        foreach (var n in OwnShaders)
        {
            var s = Shader.Find(n);
            if (!s) bad.Add(n + " not found");
            else if (ShaderUtil.ShaderHasError(s)) bad.Add(n + " has errors: " + string.Join("; ", ShaderUtil.GetShaderMessages(s).Select(m => m.message).Take(3)));
        }
        Check("T3.compile", bad.Count == 0, bad.Count == 0 ? OwnShaders.Length + " shaders without errors" : string.Join(" | ", bad));
        ShaderMap.Fitted.TryGetValue("KriptoFX/ME/Distortion", out var to);
        Check("T3.shaderMap", to == "HS2Next/Compatible/KriptoFX/ME/Distortion", "KriptoFX/ME/Distortion -> " + (to ?? "none"));
    }

    // ------------------------------------------------------------ T4

    static void SoftParticles(UniversalRenderPipelineAsset prod, UniversalRenderPipelineAsset spike)
    {
        var cam = NewCamera();
        Quad(new Vector3(0, 0, 5), 10, Unlit(Color.black));
        var m = Mat("HS2Next/Legacy Shaders/Particles/Additive (Soft)");
        m.SetTexture("_MainTex", Flat(Color.white));
        m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        m.SetFloat("_InvFade", 1f);
        var q = Quad(new Vector3(0, 0, 4.9f), 1, m);
        const int W = 160, H = 90;
        var rows = new List<string>();
        float worst = 0, spikeMin = 1;
        float full = 0;
        foreach (float gap in new[] { 3f, 0.1f, 0.25f, 0.5f, 0.75f })
        {
            q.transform.position = new Vector3(0, 0, 5 - gap);
            SetPipeline(prod);
            float vp = Capture(cam, W, H)[H / 2 * W + W / 2].g;
            SetPipeline(spike);
            float vs = Capture(cam, W, H)[H / 2 * W + W / 2].g;
            if (gap == 3f) { full = vp; continue; }
            float want = Mathf.Clamp01(1f * gap);
            float got = full > 0 ? vp / full : 0;
            worst = Mathf.Max(worst, Mathf.Abs(got - want));
            spikeMin = Mathf.Min(spikeMin, full > 0 ? vs / full : 0);
            rows.Add($"gap {gap}: production {got:F3} (want {want:F3}), Spike {vs / Mathf.Max(full, 1e-6f):F3}");
        }
        SetPipeline(prod);
        Info("soft particle fade vs the unfaded value " + full.ToString("F3") + ": " + string.Join("; ", rows));
        Check("T4.softParticles", full > 0.1f && worst <= 0.02f, $"production fade = saturate(_InvFade dz) within {worst:F4} (limit 0.02)");
        Check("T4.spikeUnfaded", spikeMin >= 0.98f, $"measurement asset: no fade (smallest ratio {spikeMin:F3})");
    }

    // ------------------------------------------------------------ T7

    static void Distortion(UniversalRenderPipelineAsset prod, UniversalRenderPipelineAsset spike)
    {
        var cam = NewCamera();
        Quad(new Vector3(0, 0, 6), 6, Unlit(Color.white, Checker(8)));
        var m = Mat("HS2Next/Compatible/KriptoFX/ME/Distortion");
        m.SetTexture("_NormalTex", Flat(new Color(0.75f, 0.75f, 1f, 1f)));   // tangent normal (0.5, 0.5, 0.71)
        m.SetFloat("_Distortion", 40f);
        var q = Quad(new Vector3(0, 0, 4), 1.2f, m);
        const int W = 320, H = 180;
        SetPipeline(prod);
        var with = Capture(cam, W, H, "t7_distortion");
        q.SetActive(false);
        var without = Capture(cam, W, H, "t7_background");
        q.SetActive(true);
        SetPipeline(spike);
        var sWith = Capture(cam, W, H);
        q.SetActive(false);
        var sWithout = Capture(cam, W, H);
        SetPipeline(prod);
        var (d, lum) = Region(with, without, W, H, 0.45f, 0.55f, 0.45f, 0.55f);
        var (_, lumBg) = Region(without, without, W, H, 0.45f, 0.55f, 0.45f, 0.55f);
        var (ds, _) = Region(sWith, sWithout, W, H, 0f, 1f, 0f, 1f);
        Check("T7.draws", d > 0.02f && lum > 0.5f * lumBg && !AnyNaN(with), $"production: mean |with - without| {d * 255:F1} / 255 inside the quad, luminance {lum:F3} vs background {lumBg:F3}");
        Check("T7.spikeNoDraw", ds == 0f, $"measurement asset (no opaque texture): mean |with - without| {ds * 255:F3} / 255 over the frame (must be 0)");
    }

    // ------------------------------------------------------------ T8

    static void WaterSlime(UniversalRenderPipelineAsset prod, UniversalRenderPipelineAsset spike)
    {
        var cam = NewCamera();
        Quad(new Vector3(0, 0, 6), 6, Unlit(Color.white, Checker(8)));
        var water = Mat("HS2Next/Compatible/Water");
        water.renderQueue = 2999;
        var wq = Quad(new Vector3(0, 0, 6 - 0.03f), 1.5f, water);
        const int W = 320, H = 180;
        SetPipeline(prod);
        var bg = Capture(cam, W, H);
        var shallow = Capture(cam, W, H, "t8_water_shallow");
        wq.transform.position = new Vector3(0, 0, 6 - 2.5f);
        wq.transform.localScale = new Vector3(0.9f, 0.9f, 1);
        var deep = Capture(cam, W, H, "t8_water_deep");
        wq.SetActive(false);
        var bg2 = Capture(cam, W, H);
        wq.SetActive(true);
        var (dShallow, _) = Region(shallow, bg, W, H, 0.45f, 0.55f, 0.45f, 0.55f);
        var (dDeep, _) = Region(deep, bg2, W, H, 0.45f, 0.55f, 0.45f, 0.55f);
        Check("T8.waterDepth", dShallow < dDeep && !AnyNaN(shallow) && !AnyNaN(deep),
            $"water 3 cm in front of the wall changes it by {dShallow * 255:F1} / 255 (edge fade), 2.5 m in front by {dDeep * 255:F1} / 255 (full surface)");
        SetPipeline(spike);
        var sDeep = Capture(cam, W, H);
        SetPipeline(prod);
        Info($"measurement asset (old path): the deep water changes the wall by {Region(sDeep, bg2, W, H, 0.45f, 0.55f, 0.45f, 0.55f).diff * 255:F1} / 255");
        wq.SetActive(false);

        var slime = Mat("HS2Next/Effects/SlimeCutOut");
        slime.SetTexture("_BumpMap", Flat(new Color(0.75f, 0.75f, 1f, 1f)));
        slime.SetFloat("_BumpAmt", 30f);
        slime.SetColor("_Color", new Color(0.4f, 0.8f, 0.4f, 0.5f));
        Quad(new Vector3(0, 0, 4), 1.2f, slime);
        var sl = Capture(cam, W, H, "t8_slime");
        SetPipeline(spike);
        var slS = Capture(cam, W, H);
        SetPipeline(prod);
        var (dSl, _) = Region(sl, slS, W, H, 0.45f, 0.55f, 0.45f, 0.55f);
        Check("T8.slime", dSl > 0.01f && !AnyNaN(sl), $"slime: production (distorted background) vs Spike (no opaque texture) differ by {dSl * 255:F1} / 255 inside");
    }

    // ------------------------------------------------------------ T5 / T6 (SkinFit sweeps with the production asset)

    static void Fits(bool t5, bool t6)
    {
        var jobs = new List<string>();
        if (t6) jobs.Add($"Sweep -fitShader \"AIT/main namida\" -sweepFile \"{FitCases}/namida_refraction.txt\" -fitPipe both -sweepOut c33st -urpAsset production");
        if (t5) jobs.Add($"Sweep -fitItem id:821 -fitShader \"AIT/main Light\" -sweepFile \"{FitCases}/mainlight_depth.txt\" -fitPipe both -fitPix -sweepOut c33st -urpAsset production");
        string file = Path.Combine(OutDir, "textures", "scene_textures_jobs.txt");
        File.WriteAllLines(file, jobs);
        foreach (var n in new[] { "AIT_main_namida_c33st.txt", "AIT_main_Light_c33st.txt" })
        {
            var p = Path.Combine(FitDir, n);
            if (File.Exists(p)) File.Delete(p);
        }
        var skin = typeof(RenderPipelineTest).Assembly.GetType("SkinFit");
        var argsField = skin?.GetField("jobArgs", BindingFlags.NonPublic | BindingFlags.Static);
        var jobsMethod = skin?.GetMethod("Jobs", BindingFlags.Public | BindingFlags.Static);
        if (argsField == null || jobsMethod == null) { Check("T5/T6", false, "SkinFit.Jobs not found"); return; }
        var keepDefault = GraphicsSettings.defaultRenderPipeline;
        argsField.SetValue(null, new[] { "Unity", "-jobFile", file });
        try { jobsMethod.Invoke(null, null); }
        finally { argsField.SetValue(null, null); SetPipeline(keepDefault); }
        float Rms(string name)
        {
            var p = Path.Combine(FitDir, name);
            if (!File.Exists(p)) return float.NaN;
            var line = File.ReadLines(p).FirstOrDefault(l => l.StartsWith("# TOTAL RMS"));
            var parts = line?.Split(' ');
            return parts != null && parts.Length > 3 && float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : float.NaN;
        }
        if (t6)
        {
            float r = Rms("AIT_main_namida_c33st.txt");
            Check("T6.namidaRefraction", r <= 0.001f, $"namida_refraction with the production asset: TOTAL RMS {r:F4} (target <= 0.001; 0.0029 without refraction)");
        }
        if (t5)
        {
            float r = Rms("AIT_main_Light_c33st.txt");
            Check("T5.mainLightDepth", r <= 0.005f, $"mainlight_depth (Built-in's depth fade) with the production asset: TOTAL RMS {r:F4} (limit 0.005)");
        }
    }
}
