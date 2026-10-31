// EffectsTest.render (Phase 3.3 package B): play mode,
// the app's URP asset (AppPipeline), no map, a scene runtime with the standard parts; synthetic stages on layer 10 (lit by
// the chara light) in front of the view camera; captures through ViewCapture (the view's ViewSetup, HDR, SMAA).
//   R1 neutral grade (base grade off), sliders 0: post-processing on = off within 1 / 255 (U6: URP's LDR path with the
//      strip is the identity at 8 bits) on a 16 × 4 chart (grey / red / green / blue ramps)
//   R2 the base grade with HS2's defaults, and filter 10 at blend 1: the capture = the CPU strip applied to the
//      post-processing-off capture (ApplyLut2D in sRGB) within mean 1 / 255
//   R3 vignette 0.35 on flat grey = the analytic formula (PPv2 classic = URP's) within 1 / 255
//   R4 bloom: an HDR patch on black brightens its surroundings (ring) and leaves the corners; HDR captures keep values
//      above 1 (E12); a 2× capture has the same ring brightness within 2 / 255 (U12)
//   R5 AO: HS2AoFeature (fix round; added to the renderer at run time by AmbientOcclusionControl): AO on
//      darkens the crease of a box on a floor, intensity 0.2 vs 1 differ (run-time control), off restores the image
//   R6 self shadow: a box's shadow on the floor with shadows on; off: the floor equals the floor without the box
//   R7 reflection probe (U11): probe row 5 at intensity 2 changes a metallic sphere; off restores it; importance 10
//   R8 no NaN / Inf in an HDR render with every effect on
//   R9 capture = view: the view camera rendered into a target of the capture's size = ViewCapture (≤ 8 / 255, mean)
//   R10 cost (U13, rough): capture time at 2560 × 1440 with every effect vs none
// Extra groups (Phase 5.R): every static IEnumerator RenderGroup_<ID>(Transform stage) of the partial files runs after R10
// in ordinal name order (an exception fails that group only); -renderGroups <ID,...> runs only those groups (R1-R10 skipped).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HS2Next;
using HS2Next.App.Commands;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static partial class EffectsTest
{
    static string RenderReport => Path.Combine(OutDir, "effects_render_report.txt");
    static string RenderDir => Path.Combine(OutDir, "render");
    const int RW = 640, RH = 360;

    public static void RunRender()
    {
        Directory.CreateDirectory(RenderDir);
        File.WriteAllText(RenderReport, "SUMMARY: 0 PASS, 1 FAIL\nEffectsTest.render: started, no result yet\n", new UTF8Encoding(false));
        PlayModeHarness.Start(nameof(EffectsTest) + "." + nameof(RenderMain), RenderReport, 30);
    }

    sealed class Shot
    {
        public int W, H;
        public Color32[] Px;
        public double Ms;
        public Color32 At(int x, int y) => Px[y * W + x];
    }

    static SceneRuntime rr;
    static SceneSession rs;
    static SceneEffects Fx => rr.GetPart<SceneEffects>();
    static SceneViewCamera View => rr.GetPart<SceneViewCamera>();

    static Shot Capture(string name, int w = RW, int h = RH)
    {
        rr.Apply();
        rr.LateApply();
        var sw = Stopwatch.StartNew();
        var png = ViewCapture.Render(View.Camera, w, h, ViewCapture.Smaa);
        double ms = sw.Elapsed.TotalMilliseconds;
        if (name != null) File.WriteAllBytes(Path.Combine(RenderDir, name + ".png"), png);
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(png);
        var s = new Shot { W = t.width, H = t.height, Px = t.GetPixels32(), Ms = ms };
        Object.Destroy(t);
        return s;
    }

    static void SetFx(string path, object v)
    {
        var r = rs.Set(null, path, v);
        if (r == null || r.Status == CommandStatus.Error) Check("set " + path, false, r?.ToString());
    }

    static void PostProcessing(bool on) => View.Setup.PostProcessing = on;

    static void SetView(Vector3 pivot, Vector3 rot, float dist, float fov = 23f)
    {
        rs.Set(null, "camera.current", new CameraView { Pivot = new Vec3(pivot.x, pivot.y, pivot.z), Rot = new Vec3(rot.x, rot.y, rot.z), Distance = dist, Fov = fov });
        rr.Apply();
        rr.LateApply();
    }

    /// <summary>Every effect off except grading (neutral or base) — the base state of each stage.</summary>
    static void EffectsOff()
    {
        using (var tx = rs.Commands.BeginOrJoin("test", default))
        {
            tx.Set(null, "effects.bloom.enabled", false);
            tx.Set(null, "effects.vignette.enabled", false);
            tx.Set(null, "effects.ambientOcclusion.enabled", false);
            tx.Set(null, "effects.reflectionProbe.enabled", false);
            tx.Set(null, "effects.shadows.enabled", true);
            tx.Commit();
        }
        rr.Apply();
    }

    static Material Unlit(Color c, Texture tex = null)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetColor("_BaseColor", c);
        if (tex) m.SetTexture("_BaseMap", tex);
        return m;
    }

    static Material Lit(Color c, float metallic, float smoothness)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smoothness);
        return m;
    }

    static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 pos, Vector3 euler, Vector3 scale, Material m)
    {
        var g = GameObject.CreatePrimitive(t);
        g.name = name;
        var col = g.GetComponent<Collider>();
        if (col) Object.Destroy(col);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localEulerAngles = euler;
        g.transform.localScale = scale;
        g.layer = 10;
        g.GetComponent<Renderer>().sharedMaterial = m;
        RenderingLayers.ApplyToRenderers(g);
        return g;
    }

    static double D(Color32 a, Color32 b) => Math.Max(Math.Abs(a.r - b.r), Math.Max(Math.Abs(a.g - b.g), Math.Abs(a.b - b.b)));
    static float S2L(float s) => s <= 0.04045f ? s / 12.92f : Mathf.Pow((s + 0.055f) / 1.055f, 2.4f);
    static float L2S(float l) => l <= 0.0031308f ? l * 12.92f : 1.055f * Mathf.Pow(l, 1f / 2.4f) - 0.055f;

    /// <summary>ApplyLut2D of a 0..1 sRGB colour in a 32³ strip (bilinear in r / g, lerp between the two blue slices).</summary>
    static float[] ApplyStrip(float[] lut, float r, float g, float b)
    {
        float cr = Mathf.Clamp01(r) * 31f, cg = Mathf.Clamp01(g) * 31f, cb = Mathf.Clamp01(b) * 31f;
        int b0 = (int)Math.Floor(cb), r0 = (int)Math.Floor(cr), g0 = (int)Math.Floor(cg);
        float fb = cb - b0, fr = cr - r0, fg = cg - g0;
        int b1 = Math.Min(b0 + 1, 31), r1 = Math.Min(r0 + 1, 31), g1 = Math.Min(g0 + 1, 31);
        var o = new float[3];
        for (int ch = 0; ch < 3; ch++)
        {
            float T(int rr2, int gg, int bb) => lut[(gg * 1024 + rr2 + 32 * bb) * 3 + ch];
            float Slice(int bb) => (T(r0, g0, bb) * (1 - fr) + T(r1, g0, bb) * fr) * (1 - fg) + (T(r0, g1, bb) * (1 - fr) + T(r1, g1, bb) * fr) * fg;
            o[ch] = Slice(b0) * (1 - fb) + Slice(b1) * fb;
        }
        return o;
    }

    static IEnumerator RenderMain()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"EffectsTest.render (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
        var pipe = new HS2Next.App.AppPipeline();
        pipe.Apply();
        Info("pipeline: " + pipe.Summary);
        bool ppAssigned = EnsurePostProcessData();
        var s = Services();
        rs = SceneRuntimeTest.NewSession(s);
        rr = new SceneRuntime(rs, s, null, true).AddStandardParts();
        var stage = new GameObject("effects_render_stage").transform;
        IEnumerator body = null;
        try { body = RenderBody(stage); }
        catch (Exception e) { Check("render body started", false, e.ToString()); }
        if (body != null)
        {
            while (true)
            {
                object cur;
                try
                {
                    if (!body.MoveNext()) break;
                    cur = body.Current;
                }
                catch (Exception e) { Check("render body ran without an exception", false, e.ToString()); break; }
                yield return cur;
            }
        }
        try { RemoveTestSsao(); } catch (Exception e) { Info("SSAO cleanup: " + e.Message); }
        Object.Destroy(stage.gameObject);
        rr.Dispose();
        if (ppAssigned) RestorePostProcessData();
        pipe.Restore();
        rep.Insert(0, $"SUMMARY: {pass} PASS, {fail} FAIL\nEFFECTS RENDER TEST ({sw.Elapsed.TotalSeconds:F0} s)\n");
        File.WriteAllText(RenderReport, rep.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        Debug.Log($"EFFECTS RENDER TEST: {pass} PASS, {fail} FAIL");
        PlayModeHarness.ExitCode = fail > 0 ? 1 : 0;
    }

    const string UrpPostProcessData = "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";
    static UniversalRendererData ppData;

    /// <summary>
    /// The production renderer needs URP's PostProcessData for any Volume post-processing; when the asset has none (package C
    /// sets it), it is assigned in memory for the test (never saved) and reported. True when assigned here.
    /// </summary>
    public static bool EnsurePostProcessData()
    {
        var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        var urd = asset ? asset.rendererDataList[0] as UniversalRendererData : null;
        if (!urd) { Info("no Universal renderer data"); return false; }
        if (urd.postProcessData) { Info("renderer post-processing data: " + urd.postProcessData.name + " (asset)"); return false; }
        urd.postProcessData = UnityEditor.AssetDatabase.LoadAssetAtPath<PostProcessData>(UrpPostProcessData);
        urd.SetDirty();
        ppData = urd;
        Info("the production renderer has no PostProcessData (post-processing off): URP's default assigned in memory for this test (package C sets it in the asset)");
        return urd.postProcessData;
    }

    public static void RestorePostProcessData()
    {
        if (ppData) { ppData.postProcessData = null; ppData.SetDirty(); }
        ppData = null;
    }

    static ScreenSpaceAmbientOcclusion testSsao;
    static ScriptableRendererData testSsaoData;

    static void RemoveTestSsao()
    {
        if (testSsao && testSsaoData)
        {
            Fx?.AmbientOcclusion.Release();
            testSsaoData.rendererFeatures.Remove(testSsao);
            testSsaoData.SetDirty();
            Object.Destroy(testSsao);
        }
        testSsao = null;
        testSsaoData = null;
    }

    static IEnumerator RenderBody(Transform stage)
    {
        yield return PlayModeHarness.WaitFrames(3);
        var fx = Fx;
        Check("runtime with SceneEffects and a view camera", fx != null && View?.Camera);
        Info($"view: HDR {View.Camera.allowHDR}, post {View.Setup.PostProcessing}, AA {View.Setup.Antialiasing}, volume mask {View.Setup.VolumeMask}, profile {fx.Volume.ProfileSource}");
        Info($"U15 (no map): RenderSettings.defaultReflectionMode {RenderSettings.defaultReflectionMode}, custom {(RenderSettings.customReflectionTexture ? RenderSettings.customReflectionTexture.name : "none")}, intensity {RenderSettings.reflectionIntensity}, ambient mode {RenderSettings.ambientMode}");
        var onlyGroups = ArgList("-renderGroups");
        if (onlyGroups != null) { yield return RunExtraGroups(stage, onlyGroups); yield break; }

        // ---------------- stage A: a 16 × 4 chart filling the view
        var chartTex = new Texture2D(16, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var cpx = new Color32[64];
        for (int i = 0; i < 16; i++)
        {
            byte v = (byte)Math.Min(255, i * 17);
            cpx[0 * 16 + i] = new Color32(v, v, v, 255);
            cpx[1 * 16 + i] = new Color32(v, (byte)(v / 3), (byte)(v / 5), 255);
            cpx[2 * 16 + i] = new Color32((byte)(v / 4), v, (byte)(v / 2), 255);
            cpx[3 * 16 + i] = new Color32((byte)(v / 2), (byte)(v / 4), v, 255);
        }
        chartTex.SetPixels32(cpx);
        chartTex.Apply(false);
        var chartRoot = new GameObject("chart").transform; chartRoot.SetParent(stage, false);
        Prim(PrimitiveType.Quad, "chart", chartRoot, Vector3.zero, new Vector3(0, 180, 0), new Vector3(9f, 4.5f, 1f), Unlit(Color.white, chartTex));
        SetView(Vector3.zero, new Vector3(0, 180, 0), 10f);
        // block centres in the capture (the quad over-fills the view: map from the visible part)
        var cam = View.Camera;
        var blocks = new List<(int x, int y)>();
        for (int row = 0; row < 4; row++)
            for (int col = 0; col < 16; col++)
            {
                // quad local (−0.5..0.5) → world; facing +Z (rot 180): local x → world −x
                float lx = (col + 0.5f) / 16f - 0.5f, ly = (row + 0.5f) / 4f - 0.5f;
                var wp = chartRoot.TransformPoint(new Vector3(-lx * 9f, ly * 4.5f, 0f));
                var vp = cam.WorldToViewportPoint(wp);
                if (vp.x < 0.05f || vp.x > 0.95f || vp.y < 0.05f || vp.y > 0.95f) continue;
                blocks.Add(((int)(vp.x * RW), (int)(vp.y * RH)));
            }
        Info($"chart: {blocks.Count} block centres inside the view");

        stage.gameObject.SetActive(true);
        EffectsOff();

        // R1 neutral identity
        fx.BaseGradeOverride = BaseGrade.Unavailable("render test: neutral");
        SetFx("effects.colorGrading.saturation", 0); SetFx("effects.colorGrading.brightness", 0); SetFx("effects.colorGrading.contrast", 0);
        fx.ApplyAll();
        yield return PlayModeHarness.WaitFrames(2);
        PostProcessing(false);
        var off = Capture("r1_post_off");
        PostProcessing(true);
        var on = Capture("r1_neutral");
        double r1max = 0, r1sum = 0;
        foreach (var (x, y) in blocks) { double d = D(off.At(x, y), on.At(x, y)); r1max = Math.Max(r1max, d); r1sum += d; }
        Check($"R1 neutral grade through URP = identity: max {r1max} / 255, mean {F(r1sum / Math.Max(1, blocks.Count))} over {blocks.Count} blocks (U6)", blocks.Count > 30 && r1max <= 1);

        // R2 base grade, defaults; filter 10 at blend 1
        fx.BaseGradeOverride = null;
        SetFx("effects.colorGrading", new ColorGradingEffect());
        rr.Apply();
        bool bgOk = SceneEffects.BaseGradeOf(s0().Hs2Root).Ok;
        Check("R2 base grade read", bgOk);
        foreach (var (label, lut, blend) in new[] { ("defaults", DocDefaults.NoneRef, 0f), ("filter10_b1", ContentId.Vanilla(10).ToString(), 1f) })
        {
            SetFx("effects.colorGrading.lut", lut);
            SetFx("effects.colorGrading.blend", blend);
            rr.Apply();
            yield return PlayModeHarness.WaitFrames(2);
            var g = Capture("r2_" + label);
            var strip = fx.StripData;
            double sum = 0, mx = 0;
            foreach (var (x, y) in blocks)
            {
                var o = off.At(x, y);
                var e = ApplyStrip(strip, o.r / 255f, o.g / 255f, o.b / 255f);
                var c = g.At(x, y);
                double d = Math.Max(Math.Abs(c.r - e[0] * 255), Math.Max(Math.Abs(c.g - e[1] * 255), Math.Abs(c.b - e[2] * 255)));
                sum += d; mx = Math.Max(mx, d);
            }
            double mean = sum / Math.Max(1, blocks.Count);
            Check($"R2 {label}: capture = the strip applied to the post-off capture: mean {F(mean)} / 255, max {F(mx)} / 255", mean <= 1.0, "lut found " + fx.LutFound);
        }
        SetFx("effects.colorGrading", new ColorGradingEffect());
        Object.Destroy(chartRoot.gameObject);

        // ---------------- stage B: flat grey for the vignette
        var greyRoot = new GameObject("grey").transform; greyRoot.SetParent(stage, false);
        Prim(PrimitiveType.Quad, "grey", greyRoot, Vector3.zero, new Vector3(0, 180, 0), new Vector3(20f, 12f, 1f), Unlit(new Color(180 / 255f, 180 / 255f, 180 / 255f)));
        fx.BaseGradeOverride = BaseGrade.Unavailable("render test: neutral");
        SetFx("effects.colorGrading.saturation", 0); SetFx("effects.colorGrading.brightness", 0); SetFx("effects.colorGrading.contrast", 0);
        fx.ApplyAll();
        yield return PlayModeHarness.WaitFrames(2);
        var gOff = Capture("r3_vignette_off");
        SetFx("effects.vignette.enabled", true);
        SetFx("effects.vignette.intensity", 0.35f);
        rr.Apply();
        yield return PlayModeHarness.WaitFrames(2);
        var gV = Capture("r3_vignette_035");
        double vmax = 0, vsum = 0; int vn = 0;
        for (int y = 2; y < RH - 2; y += 3)
            for (int x = 2; x < RW - 2; x += 3)
            {
                float u = (x + 0.5f) / RW, v = (y + 0.5f) / RH;
                float dx = Math.Abs(u - 0.5f) * 0.35f * 3f, dy = Math.Abs(v - 0.5f) * 0.35f * 3f;
                float vf = Mathf.Pow(Mathf.Clamp01(1f - (dx * dx + dy * dy)), 0.15f * 5f);
                var o = gOff.At(x, y);
                float e = L2S(S2L(o.g / 255f) * vf) * 255f;
                double d = Math.Abs(gV.At(x, y).g - e);
                vmax = Math.Max(vmax, d); vsum += d; vn++;
            }
        Check($"R3 vignette 0.35 = the analytic formula: max {F(vmax)} / 255, mean {F(vsum / vn)} / 255", vmax <= 1.5 && vsum / vn <= 0.6);
        SetFx("effects.vignette.enabled", false);
        Object.Destroy(greyRoot.gameObject);

        // ---------------- stage C: bloom
        var bloomRoot = new GameObject("bloom").transform; bloomRoot.SetParent(stage, false);
        Prim(PrimitiveType.Quad, "black", bloomRoot, new Vector3(0, 0, -0.05f), new Vector3(0, 180, 0), new Vector3(20f, 12f, 1f), Unlit(Color.black));
        Prim(PrimitiveType.Quad, "hot", bloomRoot, Vector3.zero, new Vector3(0, 180, 0), new Vector3(0.4f, 0.4f, 1f), Unlit(new Color(6f, 6f, 6f, 1f)));
        yield return PlayModeHarness.WaitFrames(2);
        var bOff = Capture("r4_bloom_off");
        SetFx("effects.bloom.enabled", true);
        SetFx("effects.bloom.intensity", 10f);
        rr.Apply();
        yield return PlayModeHarness.WaitFrames(2);
        var bOn = Capture("r4_bloom_on");
        var bOn2 = Capture("r4_bloom_on_2x", RW * 2, RH * 2);
        int cx = RW / 2, cy = RH / 2;
        double ringOff = 0, ringOn = 0, ring2 = 0, corner = 0; int rn = 0;
        for (int a = 0; a < 32; a++)
        {
            double ang = a * Math.PI / 16;
            int x = cx + (int)(Math.Cos(ang) * 40), y = cy + (int)(Math.Sin(ang) * 40);
            ringOff += bOff.At(x, y).g; ringOn += bOn.At(x, y).g; ring2 += bOn2.At(x * 2, y * 2).g; rn++;
        }
        ringOff /= rn; ringOn /= rn; ring2 /= rn;
        corner = Math.Max(D(bOff.At(3, 3), bOn.At(3, 3)), D(bOff.At(RW - 4, RH - 4), bOn.At(RW - 4, RH - 4)));
        Check($"R4 bloom brightens the ring around an HDR patch: {F(ringOff)} → {F(ringOn)} / 255", ringOn - ringOff >= 5);
        Check($"R4 … the corners stay (max |d| {corner} / 255)", corner <= 2);
        Check($"R4 HDR capture: the patch centre saturates ({bOff.At(cx, cy).g} / 255)", bOff.At(cx, cy).g >= 250);
        Info($"U12 bloom at 2×: ring {F(ring2)} / 255 vs {F(ringOn)} at 1× (URP's mip count follows the resolution)");
        Check($"R4 2× capture ring within 4 / 255 of 1× (U12)", Math.Abs(ring2 - ringOn) <= 4, F(ring2 - ringOn));
        SetFx("effects.bloom.enabled", false);
        Object.Destroy(bloomRoot.gameObject);

        // ---------------- stage D: floor and box (shadows, AO), sphere (probe)
        var litRoot = new GameObject("lit").transform; litRoot.SetParent(stage, false);
        Prim(PrimitiveType.Quad, "floor", litRoot, Vector3.zero, new Vector3(90, 0, 0), new Vector3(30f, 30f, 1f), Lit(new Color(0.7f, 0.7f, 0.7f), 0f, 0.2f));
        var box = Prim(PrimitiveType.Cube, "box", litRoot, new Vector3(0f, 1.5f, 0f), new Vector3(0, 25, 0), new Vector3(3f, 3f, 3f), Lit(new Color(0.8f, 0.75f, 0.7f), 0f, 0.2f));
        SetView(new Vector3(0, 1, 0), new Vector3(35, 200, 0), 16f, 40f);
        SetFx("charaLight.rot", new[] { 50f, 35f });
        rr.Apply(); rr.LateApply();
        yield return PlayModeHarness.WaitFrames(3);
        var shA = Capture("r6_shadow_on");
        box.SetActive(false);
        yield return PlayModeHarness.WaitFrames(2);
        var shE = Capture("r6_shadow_on_nobox");
        box.SetActive(true);
        SetFx("effects.shadows.enabled", false);
        rr.Apply(); rr.LateApply();
        yield return PlayModeHarness.WaitFrames(2);
        var shB = Capture("r6_shadow_off");
        box.SetActive(false);
        yield return PlayModeHarness.WaitFrames(2);
        var shC = Capture("r6_shadow_off_nobox");
        box.SetActive(true);
        int sil = 0, shadowPx = 0, leak = 0; double leakMax = 0;
        for (int i = 0; i < shA.Px.Length; i++)
        {
            bool inSil = D(shB.Px[i], shC.Px[i]) > 2;
            if (inSil) { sil++; continue; }
            if (shE.Px[i].g - shA.Px[i].g > 8) shadowPx++;
            double d = D(shB.Px[i], shE.Px[i]);
            if (d > 2) { leak++; leakMax = Math.Max(leakMax, d); }
        }
        Check($"R6 shadows on: the box casts a shadow on the floor ({shadowPx} px darker by > 8 / 255; silhouette {sil} px)", shadowPx > shA.Px.Length / 200);
        Check($"R6 self shadow off: outside the box the floor equals the floor without the box ({leak} px differ, max {leakMax} / 255)", leak <= shA.Px.Length / 2000);
        SetFx("effects.shadows.enabled", true);

        // AO (fix round): HS2AoFeature (the control adds it to the renderer at run time when the asset has none)
        rr.Apply(); rr.LateApply();
        yield return PlayModeHarness.WaitFrames(3);
        var aoOff = Capture("r5_ao_off");
        SetFx("effects.ambientOcclusion.enabled", true);
        SetFx("effects.ambientOcclusion.intensity", 1f);
        rr.Apply();
        yield return PlayModeHarness.WaitFrames(3);
        var aoOn = Capture("r5_ao_1");
        SetFx("effects.ambientOcclusion.intensity", 0.2f);
        rr.Apply();
        yield return PlayModeHarness.WaitFrames(3);
        var aoLow = Capture("r5_ao_02");
        int darker = 0; double sumHi = 0, sumLo = 0;
        for (int i = 0; i < aoOff.Px.Length; i++)
        {
            int dh = aoOff.Px[i].g - aoOn.Px[i].g, dl = aoOff.Px[i].g - aoLow.Px[i].g;
            if (dh > 4) darker++;
            sumHi += Math.Max(0, dh); sumLo += Math.Max(0, dl);
        }
        bool aoUsed = Fx.AmbientOcclusion.Feature;
        Info($"AO pass {Fx.AmbientOcclusion.Source}, inputs exponent {F(HS2Next.HS2AoFeature.Inputs.Intensity)}, radius {F(HS2Next.HS2AoFeature.Inputs.Radius)}; darkening sum intensity 1: {F(sumHi)}, 0.2: {F(sumLo)}");
        Check($"R5 AO on darkens the image ({darker} px by > 4 / 255)", darker > aoOff.Px.Length / 500);
        Check("R5 run-time control: intensity 1 darkens more than 0.2 (HS2AoFeature inputs)", aoUsed && sumHi > sumLo * 1.5);
        SetFx("effects.ambientOcclusion.enabled", false);
        rr.Apply();
        yield return PlayModeHarness.WaitFrames(3);
        var aoBack = Capture("r5_ao_back_off");
        double aoRest = 0; for (int i = 0; i < aoOff.Px.Length; i++) aoRest = Math.Max(aoRest, D(aoOff.Px[i], aoBack.Px[i]));
        Check($"R5 AO off restores the image (max {aoRest} / 255)", aoRest <= 2);
        box.SetActive(false);

        // probe (U11)
        var sphere = Prim(PrimitiveType.Sphere, "sphere", litRoot, new Vector3(0, 2.5f, 0), Vector3.zero, new Vector3(4f, 4f, 4f), Lit(new Color(0.9f, 0.9f, 0.9f), 1f, 0.92f));
        yield return PlayModeHarness.WaitFrames(2);
        var pOff = Capture("r7_probe_off");
        SetFx("effects.reflectionProbe.enabled", true);
        SetFx("effects.reflectionProbe.cubemap", ContentId.Vanilla(5).ToString());
        SetFx("effects.reflectionProbe.intensity", 2f);
        rr.Apply(); rr.LateApply();
        yield return PlayModeHarness.WaitFrames(4);
        var pOn = Capture("r7_probe_on");
        var sp = cam.WorldToViewportPoint(sphere.transform.position);
        int scx = (int)(sp.x * RW), scy = (int)(sp.y * RH);
        double pd = 0; int pn = 0;
        for (int y = scy - 25; y <= scy + 25; y += 2)
            for (int x = scx - 25; x <= scx + 25; x += 2) { pd += D(pOff.At(x, y), pOn.At(x, y)); pn++; }
        pd /= pn;
        var probe = Fx.Probe;
        Check($"R7 probe row 5 × 2 changes the metallic sphere (mean |d| {F(pd)} / 255), importance {probe.Probe.importance}, cubemap {probe.CubemapName}", pd >= 5 && probe.Probe.importance == 10 && probe.Active);
        SetFx("effects.reflectionProbe.enabled", false);
        rr.Apply(); rr.LateApply();
        yield return PlayModeHarness.WaitFrames(4);
        var pBack = Capture("r7_probe_back_off");
        double pb = 0; for (int i = 0; i < pOff.Px.Length; i++) pb = Math.Max(pb, D(pOff.Px[i], pBack.Px[i]));
        Check($"R7 probe off restores the sphere (max {pb} / 255)", pb <= 2);

        // R8 no NaN with every effect on
        fx.BaseGradeOverride = null;
        using (var tx = rs.Commands.BeginOrJoin("all on", default))
        {
            tx.Set(null, "effects.colorGrading", new ColorGradingEffect { Lut = ContentId.Vanilla(3).ToString(), Blend = 0.5f });
            tx.Set(null, "effects.bloom", new BloomEffect());
            tx.Set(null, "effects.vignette", new VignetteEffect());
            tx.Set(null, "effects.reflectionProbe.enabled", true);
            tx.Commit();
        }
        box.SetActive(true);
        rr.Apply(); rr.LateApply();
        yield return PlayModeHarness.WaitFrames(3);
        var rt = new RenderTexture(RW, RH, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        var prevT = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render(); cam.Render();
        cam.targetTexture = prevT;
        var prevA = RenderTexture.active;
        RenderTexture.active = rt;
        var ft = new Texture2D(RW, RH, TextureFormat.RGBAHalf, false, true);
        ft.ReadPixels(new Rect(0, 0, RW, RH), 0, 0);
        ft.Apply();
        RenderTexture.active = prevA;
        int bad = 0;
        foreach (var c in ft.GetPixels()) if (float.IsNaN(c.r) || float.IsNaN(c.g) || float.IsNaN(c.b) || float.IsInfinity(c.r) || float.IsInfinity(c.g) || float.IsInfinity(c.b)) bad++;
        Check($"R8 no NaN / Inf with grading, bloom, vignette, probe on ({bad} bad texels)", bad == 0);
        Object.Destroy(ft);
        rt.Release(); Object.Destroy(rt);

        // R9 capture = view (same frame)
        var vrt = new RenderTexture(RW, RH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        View.ApplySetup();
        float aspect = cam.aspect;
        cam.targetTexture = vrt;
        cam.ResetProjectionMatrix();
        cam.aspect = RW / (float)RH;
        cam.Render(); cam.Render();
        RenderTexture.active = vrt;
        var vt = new Texture2D(RW, RH, TextureFormat.RGBA32, false);
        vt.ReadPixels(new Rect(0, 0, RW, RH), 0, 0);
        vt.Apply();
        RenderTexture.active = prevA;
        cam.targetTexture = prevT;
        cam.ResetAspect();
        var vpx = vt.GetPixels32();
        var cap = Capture("r9_capture");
        File.WriteAllBytes(Path.Combine(RenderDir, "r9_view.png"), vt.EncodeToPNG());
        double cmax = 0, csum = 0;
        for (int i = 0; i < vpx.Length; i++) { double d = D(vpx[i], cap.Px[i]); cmax = Math.Max(cmax, d); csum += d; }
        Check($"R9 capture = view: max {cmax} / 255, mean {F(csum / vpx.Length)} / 255 (U12)", csum / vpx.Length <= 1 && cmax <= 8 + 4);
        Object.Destroy(vt); vrt.Release(); Object.Destroy(vrt);

        // R10 rough cost (U13): capture time at 2560 × 1440, every effect on vs off
        var tOn = new List<double>(); var tOff = new List<double>();
        for (int k = 0; k < 3; k++) tOn.Add(Capture(null, 2560, 1440).Ms);
        PostProcessing(false);
        for (int k = 0; k < 3; k++) tOff.Add(Capture(null, 2560, 1440).Ms);
        PostProcessing(true);
        Info($"U13 (rough): 2560 × 1440 capture (2 renders + readback) with grading / bloom / vignette / probe {F(tOn.Min())} ms, post-processing off {F(tOff.Min())} ms → ≈ {F((tOn.Min() - tOff.Min()) / 2)} ms per frame for the post stack");
        Check("R10 timing measured", tOn.Count == 3);
        Object.Destroy(litRoot.gameObject);

        yield return RunExtraGroups(stage, null);
    }

    /// <summary>
    /// Runs the render groups of the partial files: every static <c>IEnumerator RenderGroup_&lt;ID&gt;(Transform stage)</c> of this
    /// class in ordinal name order, or only the IDs in <paramref name="ids"/>; an exception fails that group and the next runs.
    /// </summary>
    static IEnumerator RunExtraGroups(Transform stage, string[] ids)
    {
        const string prefix = "RenderGroup_";
        var groups = typeof(EffectsTest).GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name.StartsWith(prefix, StringComparison.Ordinal) && m.ReturnType == typeof(IEnumerator) &&
                        m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Transform))
            .Where(m => ids == null || ids.Contains(m.Name.Substring(prefix.Length)))
            .OrderBy(m => m.Name, StringComparer.Ordinal).ToList();
        Info("render groups: " + (groups.Count == 0 ? "none" : string.Join(", ", groups.Select(m => m.Name.Substring(prefix.Length)))));
        if (ids != null) Check($"render groups asked ({string.Join(",", ids)}) all exist", groups.Count == ids.Length);
        foreach (var m in groups)
        {
            IEnumerator g = null;
            try { g = (IEnumerator)m.Invoke(null, new object[] { stage }); }
            catch (Exception e) { Check(m.Name + " started", false, (e.InnerException ?? e).ToString()); continue; }
            while (true)
            {
                object cur;
                try
                {
                    if (!g.MoveNext()) break;
                    cur = g.Current;
                }
                catch (Exception e) { Check(m.Name + " ran without an exception", false, e.ToString()); break; }
                yield return cur;
            }
        }
    }

    static RuntimeServices s0() => Services();
}
