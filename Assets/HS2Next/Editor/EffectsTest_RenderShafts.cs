// EffectsTest.render group SS (sun shafts; HS2SunShaftsFeature, HS2SunShaftsPass, Hidden/HS2SunShafts, SunShaftMath):
//   SS1 the feature is in the production renderer after the DOF and active, its shader resolves; the pass is enqueued only
//       while the block is on, only for game cameras with post-processing (a post-off capture and a preview camera get
//       none), and the scene's block reaches the feature's inputs
//   SS2 synthetic stage (a bright clear colour at the far plane, a black occluder and a near white quad, no AA): the shaft buffer (debug view)
//       and the composite equal SunShaftMath's reference computed from the shafts-off capture, at caster none and at a
//       caster point; a caster behind the camera changes nothing (0 / 255) and enqueues nothing
//   SS3 cost: 2560 x 1440 capture time with and without (reported)
//   SS4 capture parity: the shaft delta of the view camera's own render = the capture's at 1x; the 2x capture's delta
//       reduced 2 x 2 = the 1x delta within 1.5 / 255 mean (UV-unit steps)
// The group drives the pass through HS2SunShaftsFeature.Override (exact parameters; the scene keeps writing Inputs) and
// restores everything it creates.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using HS2Next;
using HS2Next.App.Commands;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static partial class EffectsTest
{
    static Shot CaptureAa(int w, int h, string aa)
    {
        rr.Apply();
        rr.LateApply();
        var sw = Stopwatch.StartNew();
        var png = ViewCapture.Render(View.Camera, w, h, aa);
        double ms = sw.Elapsed.TotalMilliseconds;
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(png);
        var s = new Shot { W = t.width, H = t.height, Px = t.GetPixels32(), Ms = ms };
        Object.Destroy(t);
        return s;
    }

    static HS2SunShaftsFeature.Input ShaftInput(float intensity = 5f) => new HS2SunShaftsFeature.Input
    {
        Enabled = true,
        Threshold = new Color(128 / 255f, 128 / 255f, 128 / 255f, 1f),
        Colour = Color.white,
        MaxRadius = 0.75f, BlurSize = 5f, Intensity = intensity,
    };

    static SunShaftMath.Params MathParams(HS2SunShaftsFeature.Input i, Vector2 sun) => new SunShaftMath.Params
    {
        HasCaster = true, CasterX = sun.x, CasterY = sun.y, CasterZ = 1f,
        ThresholdR = i.Threshold.r, ThresholdG = i.Threshold.g, ThresholdB = i.Threshold.b,
        ColourR = i.Colour.r, ColourG = i.Colour.g, ColourB = i.Colour.b,
        MaxRadius = i.MaxRadius, BlurSize = i.BlurSize, Intensity = i.Intensity,
    };

    static byte Code(float linear) => (byte)Math.Round(Mathf.Clamp01(L2S(Mathf.Clamp01(linear))) * 255f);

    /// <summary>Per-pixel code differences of a channel-max over the interior (2 px margin): mean, p99, max.</summary>
    static (double mean, double p99, double max) CodeStats(Shot a, Func<int, int, double> expected)
    {
        var d = new List<double>();
        for (int y = 2; y < a.H - 2; y++)
            for (int x = 2; x < a.W - 2; x++)
            {
                var c = a.At(x, y);
                d.Add(Math.Abs(Math.Max(c.r, Math.Max(c.g, c.b)) - expected(x, y)));
            }
        d.Sort();
        return (d.Average(), d[(int)(d.Count * 0.99)], d[d.Count - 1]);
    }

    static IEnumerator RenderGroup_SS(Transform stage)
    {
        Section("SS sun shafts");
        var fx = Fx;
        var cam = View.Camera;
        EffectsOff();
        fx.BaseGradeOverride = BaseGrade.Unavailable("render test: neutral");
        SetFx("effects.colorGrading.saturation", 0); SetFx("effects.colorGrading.brightness", 0); SetFx("effects.colorGrading.contrast", 0);
        fx.ApplyAll();
        PostProcessing(true);
        HS2SunShaftsFeature.Override = null;
        HS2SunShaftsFeature.DebugView = false;

        // ---------------- SS1 wiring
        var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        var urd = asset ? asset.rendererDataList[0] as UniversalRendererData : null;
        var feats = urd ? urd.rendererFeatures : new List<ScriptableRendererFeature>();
        int iDof = feats.FindIndex(f => f is HS2DofFeature), iSh = feats.FindIndex(f => f is HS2SunShaftsFeature);
        var feature = iSh >= 0 ? (HS2SunShaftsFeature)feats[iSh] : null;
        Check($"SS1 HS2SunShaftsFeature in the production renderer after HS2DofFeature (indices {iDof}, {iSh} of {feats.Count}) and active",
            feature && iDof >= 0 && iSh > iDof && feature.isActive);
        Check("SS1 the shader resolves (" + HS2SunShaftsFeature.ShaderName + ")", Shader.Find(HS2SunShaftsFeature.ShaderName) && feature && feature.Shader);

        // the stage: the clear colour (render.backgroundColor, a far pixel at the far plane) bright, a black occluder lower
        // left and a white quad upper right, both near (the white one must not feed the shafts: it is not far)
        SetView(Vector3.zero, new Vector3(0, 180, 0), 10f);
        yield return PlayModeHarness.WaitFrames(2);
        var bgBefore = RenderOptions.BackgroundColor;
        void Background(Color c) { RenderOptions.Apply(new Newtonsoft.Json.Linq.JObject { ["backgroundColor"] = new Newtonsoft.Json.Linq.JArray(c.r, c.g, c.b, 1f) }); }
        Background(new Color(0.95f, 0.85f, 0.7f, 1f));
        var root = new GameObject("shafts_stage").transform; root.SetParent(stage, false);
        float oh = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 5f;
        var occMat = Unlit(Color.black); occMat.SetFloat("_Cull", 0f);
        var occ = Prim(PrimitiveType.Quad, "occluder", root, Vector3.zero, Vector3.zero, Vector3.one, occMat);
        occ.transform.position = cam.ViewportToWorldPoint(new Vector3(0.3f, 0.32f, 5f));
        occ.transform.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
        occ.transform.localScale = new Vector3(oh * 0.5f, oh * 0.45f, 1f);
        var nearMat = Unlit(Color.white); nearMat.SetFloat("_Cull", 0f);
        var nearQ = Prim(PrimitiveType.Quad, "near_white", root, Vector3.zero, Vector3.zero, Vector3.one, nearMat);
        nearQ.transform.position = cam.ViewportToWorldPoint(new Vector3(0.75f, 0.7f, 6f));
        nearQ.transform.rotation = Quaternion.LookRotation(cam.transform.forward, cam.transform.up);
        nearQ.transform.localScale = new Vector3(oh * 0.35f, oh * 0.3f, 1f);
        yield return PlayModeHarness.WaitFrames(2);

        // SS1 enqueue rules
        var input = ShaftInput();
        HS2SunShaftsFeature.Override = new HS2SunShaftsFeature.Input { Enabled = false };
        int n0 = HS2SunShaftsFeature.EnqueuedCount;
        var off = CaptureAa(RW, RH, ViewCapture.None);
        Check($"SS1 block off: no pass ({HS2SunShaftsFeature.EnqueuedCount - n0} enqueued)", HS2SunShaftsFeature.EnqueuedCount == n0);
        HS2SunShaftsFeature.Override = null;
        var offNoOverride = CaptureAa(RW, RH, ViewCapture.None);
        Check($"SS1 off = no inputs: max {MaxDiff(off, offNoOverride)} / 255", MaxDiff(off, offNoOverride) == 0);
        HS2SunShaftsFeature.Override = input;
        PostProcessing(false);
        n0 = HS2SunShaftsFeature.EnqueuedCount;
        CaptureAa(RW, RH, ViewCapture.None);
        Check($"SS1 post-processing off: no pass ({HS2SunShaftsFeature.EnqueuedCount - n0} enqueued)", HS2SunShaftsFeature.EnqueuedCount == n0);
        PostProcessing(true);
        var prevGo = new GameObject("shafts_preview_cam");
        var prev = prevGo.AddComponent<Camera>();
        prev.CopyFrom(cam);
        prev.cameraType = CameraType.Preview;
        prev.enabled = false;
        var prt = new RenderTexture(160, 90, 24);
        prev.targetTexture = prt;
        n0 = HS2SunShaftsFeature.EnqueuedCount;
        prev.Render();
        Check($"SS1 a preview camera: no pass ({HS2SunShaftsFeature.EnqueuedCount - n0} enqueued)", HS2SunShaftsFeature.EnqueuedCount == n0);
        prev.targetTexture = null; prt.Release(); Object.Destroy(prt); Object.Destroy(prevGo);
        n0 = HS2SunShaftsFeature.EnqueuedCount;
        CaptureAa(RW, RH, ViewCapture.None);
        Check($"SS1 block on, capture with post-processing: the pass runs ({HS2SunShaftsFeature.EnqueuedCount - n0} enqueued)", HS2SunShaftsFeature.EnqueuedCount > n0);
        HS2SunShaftsFeature.Override = null;
        // the scene's block reaches the inputs (SceneEffects writes them in Apply / LateApply)
        SetFx("effects.sunShafts.enabled", true);
        SetFx("effects.sunShafts.intensity", 3.5f);
        rr.Apply(); rr.LateApply();
        var sin = HS2SunShaftsFeature.Inputs;
        Check($"SS1 the scene's block reaches the inputs (enabled {sin.Enabled}, intensity {F(sin.Intensity)}, caster {(sin.HasCaster ? "point" : "none")})",
            sin.Enabled && Math.Abs(sin.Intensity - 3.5f) < 1e-6f && !sin.HasCaster);
        SetFx("effects.sunShafts.enabled", false);
        rr.Apply(); rr.LateApply();
        Check("SS1 the block off clears the inputs' enabled", !HS2SunShaftsFeature.Inputs.Enabled);

        // ---------------- SS2 GPU = CPU reference
        HS2SunShaftsFeature.Override = new HS2SunShaftsFeature.Input { Enabled = false };
        Background(Color.black);
        var mask = CaptureAa(RW, RH, ViewCapture.None);
        Background(new Color(0.95f, 0.85f, 0.7f, 1f));
        var rgb = new float[off.W * off.H * 3];
        var depth = new float[off.W * off.H];
        for (int i = 0; i < off.Px.Length; i++)
        {
            rgb[i * 3] = S2L(off.Px[i].r / 255f); rgb[i * 3 + 1] = S2L(off.Px[i].g / 255f); rgb[i * 3 + 2] = S2L(off.Px[i].b / 255f);
            // near = visible against a black background (the white quad); the black occluder gives 0 whatever its depth
            depth[i] = mask.Px[i].r + mask.Px[i].g + mask.Px[i].b > 0 ? 0.5f : 1f;
        }
        int occPx = off.Px.Count(c => c.r + c.g + c.b == 0);
        int nearPx = depth.Count(d => d < 1f);
        Info($"SS2 stage: black occluder {occPx} px, near white quad {nearPx} px of {off.Px.Length}; background codes {off.Px.Max(c => c.r)} / {off.Px.Max(c => c.g)} / {off.Px.Max(c => c.b)}");
        foreach (var (label, point) in new (string, Vector3?)[] { ("none", null), ("caster", cam.ViewportToWorldPoint(new Vector3(0.62f, 0.66f, 50f))) })
        {
            var inp = input;
            inp.HasCaster = point.HasValue;
            if (point.HasValue) inp.CasterPoint = point.Value;
            HS2SunShaftsFeature.Override = inp;
            HS2SunShaftsFeature.DebugView = true;
            var dbg = CaptureAa(RW, RH, ViewCapture.None);
            var sun = HS2SunShaftsFeature.Last.Sun;
            HS2SunShaftsFeature.DebugView = false;
            var comp = CaptureAa(RW, RH, ViewCapture.None);
            var p = MathParams(inp, sun);
            var cpu = SunShaftMath.Shafts(rgb, depth, off.W, off.H, p);
            var cpuOut = SunShaftMath.Apply(rgb, cpu, p);
            var sb = CodeStats(dbg, (x, y) => Code(cpu[y * off.W + x]));
            var sc = CodeStats(comp, (x, y) => { int i = y * off.W + x; return Math.Max(Code(cpuOut[i * 3]), Math.Max(Code(cpuOut[i * 3 + 1]), Code(cpuOut[i * 3 + 2]))); });
            double lit = cpu.Count(v => v > 2f / 255f) / (double)cpu.Length;
            Info($"SS2 {label}: sun ({F(sun.x)}, {F(sun.y)}), CPU buffer > 2/255 on {F(lit * 100)} % of the frame");
            Check($"SS2 {label}: shaft buffer GPU = CPU: mean {F(sb.mean)}, p99 {sb.p99}, max {sb.max} codes", lit > 0.05 && sb.mean <= 0.5 && sb.p99 <= 1);
            Check($"SS2 {label}: composite GPU = CPU: mean {F(sc.mean)}, p99 {sc.p99}, max {sc.max} codes (intensity 5 amplifies one buffer code)", sc.mean <= 1 && sc.p99 <= 6);
        }
        // behind the camera
        var behind = input;
        behind.HasCaster = true;
        behind.CasterPoint = cam.transform.position - cam.transform.forward * 20f;
        HS2SunShaftsFeature.Override = behind;
        n0 = HS2SunShaftsFeature.EnqueuedCount;
        var bh = CaptureAa(RW, RH, ViewCapture.None);
        Check($"SS2 caster behind the camera: no change (max {MaxDiff(bh, off)} / 255), no pass, reported behind ({HS2SunShaftsFeature.Last.Behind})",
            MaxDiff(bh, off) == 0 && HS2SunShaftsFeature.EnqueuedCount == n0 && HS2SunShaftsFeature.Last.Behind);

        // ---------------- SS3 cost
        var cost = ShaftInput();
        var tOn = new List<double>(); var tOff = new List<double>();
        HS2SunShaftsFeature.Override = cost;
        CaptureAa(2560, 1440, ViewCapture.Smaa);
        for (int k = 0; k < 3; k++) tOn.Add(CaptureAa(2560, 1440, ViewCapture.Smaa).Ms);
        HS2SunShaftsFeature.Override = new HS2SunShaftsFeature.Input { Enabled = false };
        for (int k = 0; k < 3; k++) tOff.Add(CaptureAa(2560, 1440, ViewCapture.Smaa).Ms);
        Info($"SS3 cost (rough): 2560 x 1440 capture (2 renders + readback) with shafts {F(tOn.Min())} ms, without {F(tOff.Min())} ms -> about {F((tOn.Min() - tOff.Min()) / 2)} ms per frame");
        Check("SS3 cost measured", tOn.Count == 3 && tOff.Count == 3);

        // ---------------- SS4 capture parity (moderate light so nothing clips)
        var par = ShaftInput(1f);
        par.Colour = new Color(0.5f, 0.5f, 0.5f, 1f);
        var vrt = new RenderTexture(RW, RH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Shot ViewShot()
        {
            View.ApplySetup();
            var prevT = cam.targetTexture;
            cam.targetTexture = vrt;
            cam.ResetProjectionMatrix();
            cam.aspect = RW / (float)RH;
            cam.Render(); cam.Render();
            var prevA = RenderTexture.active;
            RenderTexture.active = vrt;
            var vt = new Texture2D(RW, RH, TextureFormat.RGBA32, false);
            vt.ReadPixels(new Rect(0, 0, RW, RH), 0, 0);
            vt.Apply();
            RenderTexture.active = prevA;
            cam.targetTexture = prevT;
            cam.ResetAspect();
            var sh = new Shot { W = RW, H = RH, Px = vt.GetPixels32() };
            Object.Destroy(vt);
            return sh;
        }
        HS2SunShaftsFeature.Override = new HS2SunShaftsFeature.Input { Enabled = false };
        var vOff = ViewShot(); var cOff = CaptureAa(RW, RH, ViewCapture.Smaa); var c2Off = CaptureAa(RW * 2, RH * 2, ViewCapture.Smaa);
        HS2SunShaftsFeature.Override = par;
        var vOn = ViewShot(); var cOn = CaptureAa(RW, RH, ViewCapture.Smaa); var c2On = CaptureAa(RW * 2, RH * 2, ViewCapture.Smaa);
        double vmax = 0, vsum = 0, dsum = 0, dsum2 = 0, d2max = 0;
        for (int y = 0; y < RH; y++)
            for (int x = 0; x < RW; x++)
            {
                int i = y * RW + x;
                double dv = vOn.Px[i].g - vOff.Px[i].g, dc = cOn.Px[i].g - cOff.Px[i].g;
                vmax = Math.Max(vmax, Math.Abs(dv - dc)); vsum += Math.Abs(dv - dc); dsum += Math.Abs(dc);
                double d2 = 0;
                for (int k = 0; k < 4; k++)
                {
                    int j = (2 * y + (k >> 1)) * RW * 2 + 2 * x + (k & 1);
                    d2 += c2On.Px[j].g - c2Off.Px[j].g;
                }
                d2 /= 4;
                dsum2 += Math.Abs(d2 - dc); d2max = Math.Max(d2max, Math.Abs(d2 - dc));
            }
        double n = RW * RH;
        Info($"SS4 the shaft delta at 1x: mean {F(dsum / n)} / 255 (green)");
        Check($"SS4 view = capture at 1x: the shaft delta differs by mean {F(vsum / n)}, max {vmax} / 255", dsum / n > 2 && vsum / n <= 0.5 && vmax <= 2);
        Check($"SS4 2x capture reduced 2 x 2 = 1x: the shaft delta differs by mean {F(dsum2 / n)}, max {F(d2max)} / 255", dsum2 / n <= 1.5);
        vrt.Release(); Object.Destroy(vrt);

        HS2SunShaftsFeature.Override = null;
        HS2SunShaftsFeature.DebugView = false;
        fx.BaseGradeOverride = null;
        Background(bgBefore);
        Object.Destroy(root.gameObject);
        yield return PlayModeHarness.WaitFrames(1);
    }

    static double MaxDiff(Shot a, Shot b)
    {
        if (a.Px.Length != b.Px.Length) return 999;
        double m = 0;
        for (int i = 0; i < a.Px.Length; i++) m = Math.Max(m, D(a.Px[i], b.Px[i]));
        return m;
    }
}
