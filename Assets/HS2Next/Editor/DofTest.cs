// Render test of the item-focus DOF (U5 / U12 / U13 for DOF;
// Phase 3.3 package C). Edit mode, graphics device, the app's production URP asset (HS2NextURP + its renderer with
// HS2DofFeature); synthetic stages of URP/Unlit quads in front of a camera at the origin looking along +z (no character).
// Run: Tools/unity_batch.ps1 -Method DofTest.RunRender [-MethodArgs '-only','D1,D3']. Report: Spike/out/effects/dof_report.txt
// (first and last line "SUMMARY: N PASS, M FAIL"), images Spike/out/effects/dof/*.png. Exit 1 on a FAIL.
//   D0 setup: the production renderer has HS2DofFeature (active, its shader resolved) after URP's SSAO feature; the pass is
//      enqueued only while the inputs are active (enabled, aperture > 0) on a game camera with post-processing.
//   D1 CoC (exact): a depth staircase (quads at 2, 3, 5, 8, 13, 21 m and the far plane) with the debug view: the CoC the pass
//      computes equals HS2's legacy formula saturate(A |d - f| / d - focalSize), A = 1 / (1 - aperture) - 1, within 1 / 255
//      for 4 focal size / aperture pairs and 2 focus distances.
//   D2 focus plane sharp: a checker quad at the focus distance renders identically with the DOF on and off (max 1 / 255
//      inside the quad); the DOF off (enabled false, aperture 0) changes no pixel of the frame.
//   D3 blur and reference height: a black / white edge on a far quad (coc 1) is blurred (10-90 % width > 1.5 px at 360 px);
//      a 2x capture (720 px, reference height 360) blurs the same fraction of the frame (width ratio 2 within 25 %); a
//      nearly-in-focus edge (coc 0.25) stays within 2 px of sharp (the legacy two-scale merge; fix round of package H's
//      dof480 comparison).
//   D4 near blur: a bright near quad (out of focus in front of a sharp far plane) spreads over the sharp background next to
//      its silhouette (foreground overlap), a far quad's blur does not spread over a sharp near quad.
//   D5 cost (U13, info): frame time at 2560 x 1440 with the DOF on vs off and with the depth / opaque copies on vs off
//      (camera overrides), wall clock over 30 synchronized renders.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

public static class DofTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "effects");
    static readonly StringBuilder sb = new StringBuilder();
    static int pass, fail;
    static readonly List<Object> trash = new List<Object>();
    const float Far = 100f, Near = 0.1f, Fov = 40f;

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
        Debug.Log("DOF TEST " + line);
    }

    static void Info(string line) { sb.AppendLine("     " + line); Debug.Log("DOF TEST " + line); }

    static T Keep<T>(T o) where T : Object { trash.Add(o); return o; }

    public static void RunRender()
    {
        sb.Clear(); pass = fail = 0;
        Directory.CreateDirectory(Path.Combine(OutDir, "dof"));
        sb.AppendLine($"DOF render test {DateTime.Now:yyyy-MM-dd HH:mm:ss}, Unity {Application.unityVersion}, device {SystemInfo.graphicsDeviceType}");
        var prevDefault = GraphicsSettings.defaultRenderPipeline;
        var prevQuality = QualitySettings.renderPipeline;
        bool prevAsync = ShaderUtil.allowAsyncCompilation;
        var prevInputs = HS2DofFeature.Inputs;
        var only = Arg("-only", null)?.Split(',', ';', '+');
        bool Want(string id) => only == null || only.Contains(id);
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            var prod = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AppPipeline.EditorAssetPath);
            if (!prod || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Check("D0", false, "no production asset or no graphics device");
            else
            {
                GraphicsSettings.defaultRenderPipeline = prod; QualitySettings.renderPipeline = prod;
                Run("D0", Setup);
                if (Want("D1")) Run("D1", Coc);
                if (Want("D2")) Run("D2", FocusSharp);
                if (Want("D3")) Run("D3", BlurScale);
                if (Want("D4")) Run("D4", NearBlur);
                if (Want("D5")) Run("D5", Cost);
            }
        }
        catch (Exception e) { Check("run", false, "exception " + e); }
        finally
        {
            HS2DofFeature.Inputs = prevInputs;
            HS2DofFeature.DebugView = false;
            Cleanup();
            GraphicsSettings.defaultRenderPipeline = prevDefault; QualitySettings.renderPipeline = prevQuality;
            ShaderUtil.allowAsyncCompilation = prevAsync;
        }
        string summary = $"SUMMARY: {pass} PASS, {fail} FAIL";
        File.WriteAllText(Path.Combine(OutDir, "dof_report.txt"), summary + "\n" + sb + summary + "\n");
        Debug.Log("DOF TEST DONE " + summary);
        if (Application.isBatchMode) EditorApplication.Exit(fail == 0 && pass > 0 ? 0 : 1);
    }

    static void Run(string id, Action a)
    {
        try { a(); }
        catch (Exception e) { Check(id, false, "exception " + e); }
        finally { Cleanup(); HS2DofFeature.DebugView = false; HS2DofFeature.Inputs = default; }
    }

    static void Cleanup()
    {
        foreach (var o in trash) if (o) Object.DestroyImmediate(o);
        trash.Clear();
    }

    // ------------------------------------------------------------ stage helpers

    static Camera NewCamera()
    {
        var go = Keep(new GameObject("dof_cam"));
        var cam = go.AddComponent<Camera>();
        cam.transform.position = Vector3.zero; cam.transform.rotation = Quaternion.identity;
        cam.fieldOfView = Fov; cam.nearClipPlane = Near; cam.farClipPlane = Far;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        cam.allowHDR = true; cam.allowMSAA = false;
        var urp = go.AddComponent<UniversalAdditionalCameraData>();
        urp.renderPostProcessing = true;
        urp.antialiasing = AntialiasingMode.None;
        urp.renderShadows = false;
        return cam;
    }

    static Material Unlit(Color c, Texture tex = null)
    {
        var m = Keep(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
        m.SetColor("_BaseColor", c);
        if (tex) m.SetTexture("_BaseMap", tex);
        return m;
    }

    /// <summary>A quad facing the camera at distance z covering screen x in [x0, x1] (fractions of the width at that depth).</summary>
    static GameObject Quad(float z, float x0, float x1, float y0, float y1, Material m)
    {
        var q = Keep(GameObject.CreatePrimitive(PrimitiveType.Quad));
        Object.DestroyImmediate(q.GetComponent<Collider>());
        float halfH = z * Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
        float halfW = halfH * 16f / 9f;
        float cx = Mathf.Lerp(-halfW, halfW, (x0 + x1) * 0.5f), cy = Mathf.Lerp(-halfH, halfH, (y0 + y1) * 0.5f);
        q.transform.position = new Vector3(cx, cy, z);
        q.transform.rotation = Quaternion.identity;   // the quad's front faces -z (towards the camera)
        q.transform.localScale = new Vector3((x1 - x0) * 2 * halfW, (y1 - y0) * 2 * halfH, 1);
        q.GetComponent<MeshRenderer>().sharedMaterial = m;
        q.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        return q;
    }

    static Texture2D Checker(int n)
    {
        var t = Keep(new Texture2D(n * 8, n * 8, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
        var px = new Color32[t.width * t.height];
        for (int y = 0; y < t.height; y++) for (int x = 0; x < t.width; x++)
            px[y * t.width + x] = ((x / n + y / n) & 1) == 0 ? new Color32(230, 230, 230, 255) : new Color32(25, 25, 25, 255);
        t.SetPixels32(px); t.Apply();
        return t;
    }

    static Texture2D Edge()
    {
        var t = Keep(new Texture2D(64, 4, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
        var px = new Color32[64 * 4];
        for (int y = 0; y < 4; y++) for (int x = 0; x < 64; x++) px[y * 64 + x] = x < 32 ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255);
        t.SetPixels32(px); t.Apply();
        return t;
    }

    static Color[] Capture(Camera cam, int w, int h, string png = null)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var t = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
        t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply();
        RenderTexture.active = prev; cam.targetTexture = null;
        var px = t.GetPixels();
        if (png != null)
        {
            var o = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            o.SetPixels(px.Select(c => new Color(Mathf.LinearToGammaSpace(Mathf.Clamp01(c.r)), Mathf.LinearToGammaSpace(Mathf.Clamp01(c.g)), Mathf.LinearToGammaSpace(Mathf.Clamp01(c.b)), 1)).ToArray());
            o.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, "dof", png + ".png"), o.EncodeToPNG());
            Object.DestroyImmediate(o);
        }
        Object.DestroyImmediate(t); Object.DestroyImmediate(rt);
        return px;
    }

    static void Inputs(float focusZ, float focalSize, float aperture, int referenceHeight, bool enabled = true) =>
        HS2DofFeature.Inputs = new HS2DofFeature.Input
        {
            Enabled = enabled, FocusPoint = new Vector3(0, 0, focusZ), FocalSize = focalSize, Aperture = aperture, ReferenceHeight = referenceHeight,
        };

    static float A(float aperture) => aperture >= 0.9999f ? 9999f : 1f / (1f - aperture) - 1f;

    // ------------------------------------------------------------ D0

    static void Setup()
    {
        var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(SceneTexturesAssets.RendererPath);
        var feats = rd ? rd.rendererFeatures : new List<ScriptableRendererFeature>();
        int iSsao = feats.FindIndex(f => f is ScreenSpaceAmbientOcclusion), iDof = feats.FindIndex(f => f is HS2DofFeature);
        var dof = iDof >= 0 ? (HS2DofFeature)feats[iDof] : null;
        Check("D0.feature", dof && dof.isActive && dof.Shader && dof.Shader.name == HS2DofFeature.ShaderName && iSsao >= 0 && iSsao < iDof && !ShaderUtil.ShaderHasError(dof.Shader),
            $"features {string.Join(", ", feats.Select(f => f ? f.GetType().Name + (f.isActive ? "" : " (inactive)") : "null"))}; DOF shader {(dof && dof.Shader ? dof.Shader.name : "none")}" +
            $"{(dof && dof.Shader && ShaderUtil.ShaderHasError(dof.Shader) ? " HAS ERRORS" : "")}, max blur {(dof ? dof.MaxBlurSize : 0)}");
        var cam = NewCamera();
        Quad(5, 0, 1, 0, 1, Unlit(Color.gray));
        int before = HS2DofFeature.EnqueuedCount;
        Inputs(5, 0.1f, 0.5f, 270, enabled: false);
        Capture(cam, 480, 270);
        int offCount = HS2DofFeature.EnqueuedCount - before;
        Inputs(5, 0.1f, 0f, 270);
        Capture(cam, 480, 270);
        int ap0 = HS2DofFeature.EnqueuedCount - before - offCount;
        Inputs(5, 0.1f, 0.5f, 270);
        Capture(cam, 480, 270);
        int on = HS2DofFeature.EnqueuedCount - before - offCount - ap0;
        cam.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
        Capture(cam, 480, 270);
        int postOff = HS2DofFeature.EnqueuedCount - before - offCount - ap0 - on;
        Check("D0.enqueue", offCount == 0 && ap0 == 0 && on >= 1 && postOff == 0,
            $"enqueued: disabled {offCount}, aperture 0 {ap0}, active {on}, active without post-processing {postOff}");
    }

    // ------------------------------------------------------------ D1

    static readonly float[] Steps = { 2, 3, 5, 8, 13, 21 };

    static void Coc()
    {
        var cam = NewCamera();
        // six vertical strips, each at its own depth; the top quarter stays clear (far plane)
        for (int i = 0; i < Steps.Length; i++) Quad(Steps[i], i / 6f, (i + 1) / 6f, 0f, 0.75f, Unlit(Color.white));
        const int W = 960, H = 540;
        HS2DofFeature.DebugView = true;
        float worst = 0; int cases = 0;
        foreach (var (fs, ap) in new[] { (0f, 0.3f), (0.1f, 0.6f), (0.4f, 0.6f), (0.1f, 0.9f) })
            foreach (float focus in new[] { 5f, 13f })
            {
                Inputs(focus, fs, ap, H);
                var px = Capture(cam, W, H, cases == 0 ? "d1_coc" : null);
                var parts = new List<string>();
                for (int i = 0; i <= Steps.Length; i++)
                {
                    float d = i < Steps.Length ? Steps[i] : Far;
                    int x = i < Steps.Length ? (int)((i + 0.5f) / 6f * W) : W / 2;
                    int y = i < Steps.Length ? (int)(0.35f * H) : (int)(0.9f * H);
                    float got = px[y * W + x].r;
                    float want = HS2DofFeature.Coc(A(ap), fs, d, focus);
                    worst = Mathf.Max(worst, Mathf.Abs(got - want));
                    parts.Add($"{d:0}:{got:F3}/{want:F3}");
                }
                cases++;
                Info($"fs {fs} ap {ap} focus {focus}: d:got/want " + string.Join(" ", parts));
            }
        Check("D1.coc", worst <= 1f / 255f, $"{cases} cases x 7 depths, largest |coc - formula| {worst * 255f:F3} / 255 (limit 1 / 255)");
    }

    // ------------------------------------------------------------ D2

    static void FocusSharp()
    {
        var cam = NewCamera();
        Quad(5, 0.25f, 0.75f, 0.2f, 0.8f, Unlit(Color.white, Checker(4)));
        Quad(20, 0, 1, 0, 1, Unlit(new Color(0.3f, 0.5f, 0.7f)));
        const int W = 960, H = 540;
        Inputs(5, 0.1f, 0.6f, 0, enabled: false);
        var off = Capture(cam, W, H, "d2_off");
        Inputs(5, 0.1f, 0f, H);
        var ap0 = Capture(cam, W, H);
        Inputs(5, 0.1f, 0.6f, H);
        var on = Capture(cam, W, H, "d2_on");
        float inQuad = 0, all0 = 0, bgChange = 0;
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++)
        {
            int i = y * W + x;
            all0 = Mathf.Max(all0, Max3(off[i], ap0[i]));
            float d = Max3(off[i], on[i]);
            bool inside = x > 0.27f * W && x < 0.73f * W && y > 0.23f * H && y < 0.77f * H;
            if (inside) inQuad = Mathf.Max(inQuad, d); else bgChange = Mathf.Max(bgChange, d);
        }
        Check("D2.focusSharp", inQuad <= 1f / 255f, $"checker at the focus distance: max |on - off| inside {inQuad * 255f:F3} / 255 (limit 1 / 255)");
        Check("D2.offIdentity", all0 == 0f, $"aperture 0 vs DOF off: max |d| {all0 * 255f:F4} / 255 over the frame (must be 0)");
        Info($"the background (20 m, coc {HS2DofFeature.Coc(A(0.6f), 0.1f, 20, 5):F3}) and the checker's edges change by up to {bgChange * 255f:F1} / 255");
    }

    static float Max3(Color a, Color b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));

    // ------------------------------------------------------------ D3

    /// <summary>10-90 % width in pixels of the dark-to-bright transition along the middle row.</summary>
    static float EdgeWidth(Color[] px, int w, int h)
    {
        int y = h / 2;
        float lo = float.MaxValue, hi = float.MinValue;
        for (int x = 0; x < w; x++) { float v = px[y * w + x].g; lo = Mathf.Min(lo, v); hi = Mathf.Max(hi, v); }
        float Cross(float t)
        {
            float lv = lo + (hi - lo) * t;
            for (int x = 1; x < w; x++)
            {
                float a = px[y * w + x - 1].g, b = px[y * w + x].g;
                if (a < lv && b >= lv) return x - 1 + (lv - a) / Mathf.Max(b - a, 1e-6f);
            }
            return float.NaN;
        }
        return Cross(0.9f) - Cross(0.1f);
    }

    static void BlurScale()
    {
        var cam = NewCamera();
        Quad(21, 0, 1, 0, 1, Unlit(Color.white, Edge()));
        float coc = HS2DofFeature.Coc(A(0.9f), 0.1f, 21, 2);
        Inputs(2, 0.1f, 0.9f, 360, enabled: false);
        float sharp = EdgeWidth(Capture(cam, 640, 360), 640, 360);
        Inputs(2, 0.1f, 0.9f, 360);
        float w1 = EdgeWidth(Capture(cam, 640, 360, "d3_360"), 640, 360);
        float w2 = EdgeWidth(Capture(cam, 1280, 720, "d3_720"), 1280, 720);
        Inputs(2, 0.1f, 0.9f, 720);
        float w2own = EdgeWidth(Capture(cam, 1280, 720), 1280, 720);
        Check("D3.blur", w1 > 1.5f && w1 > sharp + 1f, $"coc {coc:F3}: edge 10-90 % width {w1:F2} px at 360 px (sharp {sharp:F2} px)");
        float ratio = w2 / w1;
        Check("D3.referenceHeight", Mathf.Abs(ratio - 2f) <= 0.5f, $"2x capture with reference height 360: {w2:F2} px = {ratio:F2} x the 360-px width (expect 2 within 0.5); with its own reference height 720: {w2own:F2} px");
        // nearly in focus (coc 0.25 at 12 m, focus 5 m, aperture 0.3): the legacy two-scale merge keeps a small full-resolution
        // blur of radius coc x 2R px (1 px here) and reaches the half-resolution blur only from coc 0.65 (fix round: the first
        // merge blended towards the half-resolution image from coc 0, ~1.8x HS2's width near the focus plane)
        Cleanup();
        cam = NewCamera();
        Quad(12, 0, 1, 0, 1, Unlit(Color.white, Edge()));
        float coc25 = HS2DofFeature.Coc(A(0.3f), 0f, 12, 5);
        Inputs(5, 0f, 0.3f, 360, enabled: false);
        float sharp25 = EdgeWidth(Capture(cam, 640, 360), 640, 360);
        Inputs(5, 0f, 0.3f, 360);
        float w25 = EdgeWidth(Capture(cam, 640, 360, "d3_coc025"), 640, 360);
        Check("D3.smallCoc", w25 > sharp25 && w25 <= sharp25 + 2f, $"coc {coc25:F3}: edge width {w25:F2} px (sharp {sharp25:F2} px; limit sharp + 2 px; coc 1 gives {w1:F2} px)");
    }

    // ------------------------------------------------------------ D4

    static void NearBlur()
    {
        const int W = 960, H = 540;
        // a: a bright near quad (2 m, out of focus) over a dark in-focus far plane (13 m, focus)
        var cam = NewCamera();
        Quad(13, 0, 1, 0, 1, Unlit(new Color(0.05f, 0.05f, 0.05f)));
        Quad(2, 0.3f, 0.5f, 0.3f, 0.7f, Unlit(Color.white));
        Inputs(13, 0f, 0.9f, H, enabled: false);
        var off = Capture(cam, W, H);
        Inputs(13, 0f, 0.9f, H);
        var on = Capture(cam, W, H, "d4_near");
        int edgeX = (int)(0.5f * W), y = H / 2;
        float outside = 0;
        for (int dx = 1; dx <= 3; dx++) outside = Mathf.Max(outside, on[y * W + edgeX + dx].g - off[y * W + edgeX + dx].g);
        Check("D4.nearOverlap", outside > 0.02f, $"the near quad's blur over the sharp background next to its edge: +{outside * 255f:F1} / 255 within 3 px (foreground overlap)");
        Cleanup();
        // b: a sharp near quad (2 m, focus) over a bright out-of-focus far plane: the far blur stays behind the near edge
        cam = NewCamera();
        Quad(21, 0, 1, 0, 1, Unlit(Color.white));
        Quad(2, 0.3f, 0.5f, 0.3f, 0.7f, Unlit(new Color(0.05f, 0.05f, 0.05f)));
        Inputs(2, 0f, 0.9f, H, enabled: false);
        off = Capture(cam, W, H);
        Inputs(2, 0f, 0.9f, H);
        on = Capture(cam, W, H, "d4_far");
        float inside = 0;
        for (int dx = 2; dx <= 4; dx++) inside = Mathf.Max(inside, Mathf.Abs(on[y * W + edgeX - dx].g - off[y * W + edgeX - dx].g));
        Check("D4.farBehind", inside <= 2f / 255f, $"a sharp near quad keeps its edge over a blurred bright background: max |d| {inside * 255f:F2} / 255 at 2-4 px inside (limit 2 / 255)");
    }

    // ------------------------------------------------------------ D5

    static void Cost()
    {
        const int W = 2560, H = 1440, N = 30;
        var cam = NewCamera();
        for (int i = 0; i < Steps.Length; i++) Quad(Steps[i], i / 6f, (i + 1) / 6f, 0f, 1f, Unlit(Color.HSVToRGB(i / 6f, 0.6f, 0.8f), Checker(8)));
        var rt = Keep(new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 1 });
        var one = Keep(new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true));
        double Measure(int n)
        {
            cam.targetTexture = rt;
            for (int i = 0; i < 3; i++) cam.Render();
            Sync();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; i++) { cam.Render(); Sync(); }
            cam.targetTexture = null;
            return sw.Elapsed.TotalMilliseconds / n;
        }
        void Sync()
        {
            var prev = RenderTexture.active; RenderTexture.active = rt;
            one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); one.Apply(); RenderTexture.active = prev;
        }
        var urp = cam.GetComponent<UniversalAdditionalCameraData>();
        Inputs(5, 0.1f, 0.6f, H, enabled: false);
        double baseMs = Measure(N);
        Inputs(5, 0.1f, 0.6f, H);
        double dofMs = Measure(N);
        Inputs(5, 0.1f, 0.6f, H, enabled: false);
        urp.requiresDepthOption = CameraOverrideOption.Off; urp.requiresColorOption = CameraOverrideOption.Off;
        double noCopies = Measure(N);
        urp.requiresDepthOption = CameraOverrideOption.UsePipelineSettings; urp.requiresColorOption = CameraOverrideOption.Off;
        double depthOnly = Measure(N);
        urp.requiresColorOption = CameraOverrideOption.UsePipelineSettings;
        double both = Measure(N);
        Info($"U13 cost at {W} x {H} (wall clock per synchronized render, {N} renders, Editor batch; includes CPU): " +
             $"base (depth + opaque copies, post-processing, no DOF) {baseMs:F2} ms; DOF on {dofMs:F2} ms (+{dofMs - baseMs:F2}); " +
             $"no copies {noCopies:F2} ms; depth copy only {depthOnly:F2} ms (+{depthOnly - noCopies:F2}); depth + opaque {both:F2} ms (opaque +{both - depthOnly:F2})");
        Check("D5.cost", dofMs > 0 && !double.IsNaN(dofMs), "measured (information; the target is checked in the app)");
    }
}
