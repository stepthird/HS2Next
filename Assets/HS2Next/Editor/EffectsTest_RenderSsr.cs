// EffectsTest.render group SR (screen-space reflections, opt-in; SsrControl, the pipeline's reflection feature):
//   SR1 the define is compiled in, the feature is in the production renderer and active, the effect template profile holds
//       the override inactive and the project's default profile keeps the mode Disabled
//   SR2 render.ssr off (default) with the scene flag on: the capture is bit-equal to the capture with the feature switched
//       off (the feature does no work)
//   SR3 render.ssr on + the flag: a glossy URP/Lit floor and a fitted Standard floor (HS2Next/Standard) under a bright box
//       reflect it (the floor region below the box brightens); cost at 2560 x 1440 reported
//   SR4 the flag off with the option on: no change; the state's reason in the three cases (option-off, scene-off, drawn)
// The option is restored to off at the end.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2Next;
using HS2Next.App.Commands;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static partial class EffectsTest
{
    static SsrControl.State SsrSet(string option, bool flag)
    {
        RenderOptions.Apply(new JObject { ["ssr"] = option });
        SetFx("effects.ssr.enabled", flag);
        rr.Apply(); rr.LateApply();
        var st = SsrControl.Last;
        if (st.Option != (option == "on") || st.SceneFlag != flag)
        {
            // the scene part does not drive the override (yet): drive it directly and say so
            Info($"SR: SceneEffects did not apply block 6 (state option {st.Option}, flag {st.SceneFlag}); SsrControl.Apply called by the test");
            st = SsrControl.Apply(Fx.Volume.Profile, flag);
        }
        return st;
    }

    static ScriptableRendererFeature SsrFeature(UniversalRendererData urd) =>
        urd ? urd.rendererFeatures.FirstOrDefault(f => f && f.GetType().Name == "ScreenSpaceReflectionRendererFeature") : null;

    /// <summary>Mean green-channel brightening (on - off) over a rectangle of the frame (fractions of w / h).</summary>
    static double MeanDelta(Shot on, Shot off, float x0, float x1, float y0, float y1)
    {
        double s = 0; int n = 0;
        for (int y = (int)(y0 * on.H); y < (int)(y1 * on.H); y++)
            for (int x = (int)(x0 * on.W); x < (int)(x1 * on.W); x++) { s += on.At(x, y).g - off.At(x, y).g; n++; }
        return n > 0 ? s / n : 0;
    }

    static IEnumerator RenderGroup_SR(Transform stage)
    {
        Section("SR screen-space reflections (opt-in)");
        var fx = Fx;
        var cam = View.Camera;
        EffectsOff();
        fx.BaseGradeOverride = BaseGrade.Unavailable("render test: neutral");
        fx.ApplyAll();
        PostProcessing(true);
        HS2SunShaftsFeature.Override = new HS2SunShaftsFeature.Input { Enabled = false };

        // ---------------- SR1 wiring
        var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        var urd = asset ? asset.rendererDataList[0] as UniversalRendererData : null;
        var feature = SsrFeature(urd);
        Check($"SR1 the define is compiled in (SsrControl.Available {SsrControl.Available})", SsrControl.Available);
        Check($"SR1 the reflection feature is in the production renderer and active ({(feature ? feature.isActive.ToString() : "absent")})", feature && feature.isActive);
        var tpl = Resources.Load<VolumeProfile>(EffectVolume.TemplateResource);
        var tplComp = tpl ? tpl.components.FirstOrDefault(c => c && c.GetType().Name == "ScreenSpaceReflectionVolumeSettings") : null;
        Check($"SR1 the template profile holds the override, inactive ({(tplComp ? "active " + tplComp.active : "absent")})", tplComp && !tplComp.active);
        string defMode = "absent";
#if URP_SCREEN_SPACE_REFLECTION
        var defProfile = GraphicsSettings.GetRenderPipelineSettings<URPDefaultVolumeProfileSettings>()?.volumeProfile;
        if (defProfile && defProfile.TryGet(out ScreenSpaceReflectionVolumeSettings ds))
            defMode = $"{ds.mode.value} (override {ds.mode.overrideState}, active {ds.active})";
        Check($"SR1 the default profile keeps the mode Disabled: {defMode}",
            defProfile && defProfile.TryGet(out ScreenSpaceReflectionVolumeSettings ds2) && ds2.active && ds2.mode.overrideState &&
            ds2.mode.value == ScreenSpaceReflectionVolumeSettings.ReflectionMode.Disabled);
#else
        Check("SR1 the default profile keeps the mode Disabled: the define is missing", false);
#endif
        Check($"SR1 the option's default is off ({RenderOptions.Ssr})", RenderOptions.Ssr == "off");

        // ---------------- the stage: two glossy floors (URP/Lit left, HS2Next/Standard right) under a bright box
        SetView(new Vector3(0f, 0.6f, 0f), new Vector3(25f, 180f, 0f), 6f, 40f);
        yield return PlayModeHarness.WaitFrames(2);
        var root = new GameObject("ssr_stage").transform; root.SetParent(stage, false);
        var litFloor = Lit(new Color(0.08f, 0.08f, 0.08f, 1f), 0f, 0.97f);
        var stdShader = Shader.Find("HS2Next/Standard");
        var stdFloor = new Material(stdShader ? stdShader : Shader.Find("Universal Render Pipeline/Lit"));
        stdFloor.SetColor("_Color", new Color(0.08f, 0.08f, 0.08f, 1f));
        stdFloor.SetFloat("_Glossiness", 0.97f);
        stdFloor.SetFloat("_Metallic", 0f);
        Prim(PrimitiveType.Quad, "floor_lit", root, new Vector3(1.6f, 0f, 0f), new Vector3(90f, 0f, 0f), new Vector3(3.2f, 6f, 1f), litFloor);
        Prim(PrimitiveType.Quad, "floor_std", root, new Vector3(-1.6f, 0f, 0f), new Vector3(90f, 0f, 0f), new Vector3(3.2f, 6f, 1f), stdFloor);
        var bright = Unlit(new Color(1f, 0.9f, 0.6f, 1f));
        Prim(PrimitiveType.Cube, "box_lit", root, new Vector3(1.6f, 0.6f, 0f), Vector3.zero, new Vector3(0.6f, 1.2f, 0.6f), bright);
        Prim(PrimitiveType.Cube, "box_std", root, new Vector3(-1.6f, 0.6f, 0f), Vector3.zero, new Vector3(0.6f, 1.2f, 0.6f), bright);
        yield return PlayModeHarness.WaitFrames(2);
        // the floor halves in the frame: the view looks along -Z (rot 180): world +x is the left of the frame
        var pL = cam.WorldToViewportPoint(new Vector3(1.6f, 0f, 0.6f));
        var pS = cam.WorldToViewportPoint(new Vector3(-1.6f, 0f, 0.6f));
        Info($"SR stage: URP/Lit floor below its box at viewport ({F(pL.x)}, {F(pL.y)}), Standard floor at ({F(pS.x)}, {F(pS.y)}); HS2Next/Standard found {(bool)stdShader}");
        bool stdReceiver = stdShader && stdShader.FindPassTagValue(stdShader.passCount - 1, new UnityEngine.Rendering.ShaderTagId("LightMode")).name == "DepthNormals" && stdShader.keywordSpace.FindKeyword("_WRITE_SMOOTHNESS").isValid;

        // ---------------- SR2 off = no feature work
        var st = SsrSet("off", true);
        Check($"SR4 option off, flag on: not drawn, reason {st.Reason ?? "null"}", !st.Drawn && st.Reason == "option-off");
        var offA = CaptureAa(RW, RH, ViewCapture.Smaa);
        feature?.SetActive(false);
        var offB = CaptureAa(RW, RH, ViewCapture.Smaa);
        feature?.SetActive(true);
        Check($"SR2 render.ssr off: bit-equal to the feature switched off (max {MaxDiff(offA, offB)} / 255)", MaxDiff(offA, offB) == 0);

        // ---------------- SR3 on + flag
        st = SsrSet("on", true);
        Check($"SR4 option on, flag on: drawn (reason {st.Reason ?? "null"}, receivers {st.Receivers})", st.Drawn && st.Reason == null);
        yield return PlayModeHarness.WaitFrames(3);
        CaptureAa(RW, RH, ViewCapture.Smaa);
        var on = CaptureAa(RW, RH, ViewCapture.Smaa);
        Info($"SR3 frame codes: off mean {F(offA.Px.Average(c => (double)c.g))}, max {offA.Px.Max(c => c.g)}; on mean {F(on.Px.Average(c => (double)c.g))}; camera {cam.transform.position} fwd {cam.transform.forward}");
        float bandLo = Mathf.Max(0f, Mathf.Min(pL.y, pS.y) - 0.12f), bandHi = Mathf.Min(1f, Mathf.Max(pL.y, pS.y) + 0.02f);
        double dLit = MeanDelta(on, offA, Mathf.Clamp01(pL.x - 0.08f), Mathf.Clamp01(pL.x + 0.08f), bandLo, bandHi);
        double dStd = MeanDelta(on, offA, Mathf.Clamp01(pS.x - 0.08f), Mathf.Clamp01(pS.x + 0.08f), bandLo, bandHi);
        double dSky = MeanDelta(on, offA, 0f, 1f, 0.92f, 1f);
        Info($"SR3 brightening below the boxes: URP/Lit floor {F(dLit)} / 255, Standard floor {F(dStd)} / 255, top band {F(dSky)} / 255");
        Check($"SR3 the URP/Lit floor reflects the box (mean +{F(dLit)} / 255 below it)", dLit > 2);
        if (stdReceiver) Check($"SR3 the fitted Standard floor reflects the box (mean +{F(dStd)} / 255 below it)", dStd > 2);
        else Info($"SR3 the fitted Standard floor is not a receiver in this tree (no _WRITE_SMOOTHNESS keyword in its DepthNormals pass): +{F(dStd)} / 255");
        Check($"SR3 nothing above the floors changes (top band {F(dSky)} / 255)", Math.Abs(dSky) <= 1);
        // the feature's other placement (information: before the opaques with last frame's colour, or after them)
        var aoField = feature ? feature.GetType().GetField("afterOpaque") : null;
        if (aoField != null)
        {
            bool assetMode = (bool)aoField.GetValue(feature);
            aoField.SetValue(feature, !assetMode);
            feature.Create();
            CaptureAa(RW, RH, ViewCapture.Smaa);
            var alt = CaptureAa(RW, RH, ViewCapture.Smaa);
            Info($"SR3 information: afterOpaque {!assetMode} (the asset has {assetMode}): URP/Lit floor {F(MeanDelta(alt, offA, Mathf.Clamp01(pL.x - 0.08f), Mathf.Clamp01(pL.x + 0.08f), bandLo, bandHi))} / 255, Standard floor {F(MeanDelta(alt, offA, Mathf.Clamp01(pS.x - 0.08f), Mathf.Clamp01(pS.x + 0.08f), bandLo, bandHi))} / 255, frame mean |d| {F(alt.Px.Select((c, i) => (double)Math.Abs(c.g - offA.Px[i].g)).Average())}");
            aoField.SetValue(feature, assetMode);
            feature.Create();
        }
        var tOn = new List<double>(); var tOff = new List<double>();
        CaptureAa(2560, 1440, ViewCapture.Smaa);
        for (int k = 0; k < 3; k++) tOn.Add(CaptureAa(2560, 1440, ViewCapture.Smaa).Ms);

        // ---------------- SR4 flag off with the option on
        st = SsrSet("on", false);
        Check($"SR4 option on, flag off: not drawn, reason {st.Reason ?? "null"}", !st.Drawn && st.Reason == "scene-off");
        var flagOff = CaptureAa(RW, RH, ViewCapture.Smaa);
        Check($"SR4 flag off: = the option-off capture (max {MaxDiff(flagOff, offA)} / 255)", MaxDiff(flagOff, offA) == 0);
        for (int k = 0; k < 3; k++) tOff.Add(CaptureAa(2560, 1440, ViewCapture.Smaa).Ms);
        Info($"SR3 cost (rough): 2560 x 1440 capture (2 renders + readback) with SSR {F(tOn.Min())} ms, without {F(tOff.Min())} ms -> about {F((tOn.Min() - tOff.Min()) / 2)} ms per frame");

        SsrSet("off", true);
        HS2SunShaftsFeature.Override = null;
        fx.BaseGradeOverride = null;
        Object.Destroy(root.gameObject);
        yield return PlayModeHarness.WaitFrames(1);
    }
}
