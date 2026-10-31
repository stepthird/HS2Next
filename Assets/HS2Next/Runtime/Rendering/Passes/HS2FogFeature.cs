// HS2 Studio's height fog as a URP renderer feature. HS2 fogs opaque pixels twice toward the same colour (the post-processing
// stack's deferred distance fog and an opaque-only image effect with distance and height fog) and transparent forward passes
// once in their shaders; this feature reproduces both:
//   - HS2FogPass after the opaques, the skybox and the background image, before the copy of the opaque image (so the
//     shaders that sample the scene behind them see it fogged, as in HS2) and the transparents, blends the fog colour
//     over the camera colour with the product of the two opaque factors (HS2Next/Hidden/HS2Fog, see its header and FogMath);
//   - a global-state pass at BeforeRenderingOpaques sets HS2Next's own fog globals that the fitted transparent shaders read
//     through a uniform branch (no shader keyword), and one after the transparents switches them off again, so cameras that
//     do not draw the fog (item and character thumbnails without post-processing, reflection and preview cameras, cameras of
//     other renderers) never see them on.
// RenderSettings.fog stays off (the scene's fog block writes colour and density only), so URP's own keyword fog never runs and
// URP-native opaque surfaces are fogged once, by the pass.
// The inputs are static (written by the scene's fog block through StudioFog; tests write them directly). The fog draws only
// while Inputs.Enabled, on game cameras with post-processing (the view and its captures); the command-line switch -noFogPass
// and the static Enabled turn it off for diagnostics.
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HS2Next
{
    public sealed class HS2FogFeature : ScriptableRendererFeature
    {
        /// <summary>The fog block and the scene's fog settings as the passes need them.</summary>
        public struct Input
        {
            /// <summary>effects.fog.enabled.</summary>
            public bool Enabled;
            /// <summary>The scene's fog mode as the engine numbers it (1 linear, 2 exponential, 3 exponential squared).</summary>
            public int Mode;
            public float Density, Start, End;
            /// <summary>The fog top (world y) and the height density of the half-space term.</summary>
            public float Height, HeightDensity;
            /// <summary>Keep far pixels (sky, background) unfogged by the image-effect term.</summary>
            public bool ExcludeFarPixels;
            /// <summary>The fog colour in linear space.</summary>
            public Color ColorLinear;
        }

        /// <summary>The inputs of the current frame. Default: off.</summary>
        public static Input Inputs;

        /// <summary>Tests: when set, used instead of <see cref="Inputs"/> (the scene keeps writing Inputs).</summary>
        public static Input? Override;

        /// <summary>The inputs the passes use this frame (<see cref="Override"/> when set, else <see cref="Inputs"/>).</summary>
        public static Input Current => Override ?? Inputs;

        /// <summary>Tests and diagnostics: false draws no fog (as the block off).</summary>
        public static bool Enabled = true;

        /// <summary>Frames in which the opaque fog pass was enqueued (tests).</summary>
        public static int EnqueuedCount { get; private set; }

        /// <summary>The fog globals of the last camera that drew the fog (tests): on, mode bit, near, far.</summary>
        public static Vector4 LastGlobals { get; private set; }

        public const string ShaderName = "HS2Next/Hidden/HS2Fog";

        [SerializeField] Shader shader;
        Material material;
        HS2FogPass pass;
        HS2FogGlobalsPass globalsOn, globalsOff;

        static int noFogArg = -1;

        public Shader Shader { get => shader; set => shader = value; }
        public HS2FogPass Pass => pass;

        /// <summary>The command line asks for no fog pass (-noFogPass).</summary>
        public static bool NoFogArgument
        {
            get
            {
                if (noFogArg < 0)
                {
                    noFogArg = 0;
                    try { foreach (var a in Environment.GetCommandLineArgs()) if (string.Equals(a, "-noFogPass", StringComparison.OrdinalIgnoreCase)) noFogArg = 1; }
                    catch (Exception) { }
                }
                return noFogArg == 1;
            }
        }

        public override void Create()
        {
            if (!shader) shader = Shader.Find(ShaderName);
            if (material) CoreUtils.Destroy(material);
            material = shader ? CoreUtils.CreateEngineMaterial(shader) : null;
            pass = new HS2FogPass(material);
            globalsOn = new HS2FogGlobalsPass(RenderPassEvent.BeforeRenderingOpaques, "HS2 Fog Globals");
            globalsOff = new HS2FogGlobalsPass(RenderPassEvent.AfterRenderingTransparents, "HS2 Fog Globals Off");
        }

        /// <summary>The active pipeline's first renderer has an active HS2FogFeature.</summary>
        public static bool InRenderer()
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!asset || asset.rendererDataList.Length == 0 || !(asset.rendererDataList[0] is ScriptableRendererData rd)) return false;
            foreach (var f in rd.rendererFeatures) if (f is HS2FogFeature && f.isActive) return true;
            return false;
        }

        /// <summary>The fog is drawn for the view (block on, feature enabled and in the renderer, no -noFogPass).</summary>
        public static bool Draws => Enabled && Current.Enabled && !NoFogArgument && InRenderer();

        /// <summary>True when the fog draws for a camera of this kind (the block on, the feature enabled, a game camera with post).</summary>
        public static bool DrawsFor(CameraType type, bool postProcessing) =>
            Enabled && Current.Enabled && !NoFogArgument && type == CameraType.Game && postProcessing;

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (pass == null) return;
            ref var cd = ref renderingData.cameraData;
            var cam = cd.camera;
            bool draw = material && cam && DrawsFor(cd.cameraType, cd.postProcessEnabled)
                        && !UniversalRenderer.IsOffscreenDepthTexture(ref renderingData.cameraData);
            var input = Current;
            if (draw)
            {
                float near = cam.nearClipPlane, far = cam.farClipPlane;
                globalsOn.Setup(TransparentGlobals(input, near, far));
                renderer.EnqueuePass(globalsOn);
                pass.Setup(input, near, far);
                pass.ConfigureInput(ScriptableRenderPassInput.Depth);
                pass.requiresIntermediateTexture = true;
                renderer.EnqueuePass(pass);
                globalsOff.Setup(default);
                renderer.EnqueuePass(globalsOff);
                EnqueuedCount++;
            }
            else if (HS2FogGlobalsPass.LastOn)
            {
                // a camera without fog after one with fog: make sure the globals are off for its transparents
                globalsOff.Setup(default);
                renderer.EnqueuePass(globalsOff);
            }
        }

        /// <summary>The transparent fog globals: (on, mode bit, near, far), the engine's fog parameters, the colour.</summary>
        public static (Vector4 on, Vector4 parameters, Vector4 color) TransparentGlobals(Input i, float near, float far)
        {
            float diff = i.End - i.Start;
            float inv = Mathf.Abs(diff) > 0.0001f ? 1f / diff : 0f;
            int bit = i.Mode >= 1 && i.Mode <= 3 ? 1 << (i.Mode - 1) : 0;
            var on = new Vector4(bit != 0 ? 1f : 0f, bit, near, far);
            var p = new Vector4(i.Density * 1.2011224087f, i.Density * 1.4426950408f, i.Mode == 1 ? -inv : 0f, i.Mode == 1 ? i.End * inv : 0f);
            var c = new Vector4(i.ColorLinear.r, i.ColorLinear.g, i.ColorLinear.b, 1f);
            LastGlobals = on;
            return (on, p, c);
        }

        /// <summary>The per-material fog modes of the fitted transparent shaders (bit 1 linear, 2 exponential, 4 exponential squared).</summary>
        public const string ModesProperty = "_HS2FogModes";
        static readonly int ModesId = Shader.PropertyToID(ModesProperty);
        static readonly string[] FogKeywords = { "FOG_LINEAR", "FOG_EXP", "FOG_EXP2" };

        /// <summary>The fog modes an original shader was compiled for: the fog keywords its keyword space holds (a bundle copy
        /// keeps only the fog variants of the scenes it was built with, often none).</summary>
        public static int ModesOf(Shader original)
        {
            if (!original) return 0;
            int bits = 0;
            var ks = original.keywordSpace;
            for (int i = 0; i < FogKeywords.Length; i++)
                if (ks.FindKeyword(FogKeywords[i]).isValid) bits |= 1 << i;
            return bits;
        }

        /// <summary>
        /// Sets the material's fog modes after a switch from <paramref name="original"/>: the original's modes for a material in
        /// the transparent queues (drawn forward with its own fog in HS2), 0 otherwise (opaque and cutout surfaces are drawn
        /// deferred in HS2 and fogged by the pass only). No effect on shaders without the property.
        /// </summary>
        public static void StampModes(Material m, Shader original, int renderQueue)
        {
            if (!m || !m.HasProperty(ModesId)) return;
            m.SetFloat(ModesId, renderQueue > 2500 ? ModesOf(original) : 0);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
            material = null;
        }
    }
}
