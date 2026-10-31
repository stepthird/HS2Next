// The view's anti-aliasing option and the TAA support of the fitted shaders (E13;
// Phase 3.3 package S). render.aa → ViewSetup.Antialiasing (smaa = SMAA High, the default and vanilla HS2; taa = URP's
// TAA, experimental (app.options reports taaExperimental); fxaa; none), render.taaQuality → the view camera's
// UniversalAdditionalCameraData.taaSettings.quality. Captures always render SMAA High (ViewSetup.ApplyTo, E12).
// TAA needs motion vectors: the fitted opaque / cutout shaders have a MotionVectors pass (and a DepthNormals pass) through
// Shaders/HS2NextPasses.hlsl. The hair's 2 × 2 screen door would otherwise stand still under the jitter: the global
// _HS2NextDitherFrame (HS2NextHair.hlsl) is Time.frameCount & 3 for a camera that renders with TAA and 0 for every other
// camera (the pattern of the fits), set before each camera renders.
// The class is not called TemporalAA so that URP's UnityEngine.Rendering.Universal.TemporalAA stays visible in HS2Next code.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HS2Next
{
    public static class TemporalAAControl
    {
        public const string DitherFrameName = "_HS2NextDitherFrame";
        static readonly int DitherFrameId = Shader.PropertyToID(DitherFrameName);
        static bool hooked;

        /// <summary>The URP mode of a render.aa value (unknown values: SMAA).</summary>
        public static AntialiasingMode ModeOf(string aa)
        {
            switch (aa)
            {
                case "taa": return AntialiasingMode.TemporalAntiAliasing;
                case "fxaa": return AntialiasingMode.FastApproximateAntialiasing;
                case "none": return AntialiasingMode.None;
                default: return AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            }
        }

        /// <summary>The URP TAA quality of a render.taaQuality value (unknown: High).</summary>
        public static TemporalAAQuality QualityOf(string q)
        {
            switch (q)
            {
                case "veryLow": return TemporalAAQuality.VeryLow;
                case "low": return TemporalAAQuality.Low;
                case "medium": return TemporalAAQuality.Medium;
                case "veryHigh": return TemporalAAQuality.VeryHigh;
                default: return TemporalAAQuality.High;
            }
        }

        /// <summary>Writes render.aa / taaQuality into the view's setup and (TAA) its camera; called every view LateApply.</summary>
        public static void Configure(ViewSetup setup, Camera cam)
        {
            Hook();
            if (setup == null) return;
            setup.Antialiasing = ModeOf(RenderOptions.Aa);
            setup.AntialiasingQuality = AntialiasingQuality.High;
            if (setup.Antialiasing == AntialiasingMode.TemporalAntiAliasing && cam && cam.TryGetComponent<UniversalAdditionalCameraData>(out var d))
            {
                var want = QualityOf(RenderOptions.TaaQuality);
                if (d.taaSettings.quality != want) d.taaSettings.quality = want;
            }
        }

        /// <summary>The dither frame a camera renders with (0 unless it renders with TAA).</summary>
        public static int DitherFrameFor(Camera cam) =>
            cam && cam.TryGetComponent<UniversalAdditionalCameraData>(out var d) && d.antialiasing == AntialiasingMode.TemporalAntiAliasing ? Time.frameCount & 3 : 0;

        public static void Hook()
        {
            if (hooked) return;
            hooked = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            Shader.SetGlobalFloat(DitherFrameId, 0f);
        }

        static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam) => Shader.SetGlobalFloat(DitherFrameId, DitherFrameFor(cam));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InitPlayer() => Hook();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void InitEditor() => Hook();
#endif
    }
}
