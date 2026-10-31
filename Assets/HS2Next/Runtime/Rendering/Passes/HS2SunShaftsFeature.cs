// HS2 Studio's sun shafts as a URP renderer feature. It lives in the production renderer (HS2NextURP_Renderer, after the
// item-focus DOF) and draws HS2SunShaftsPass when the scene's sun-shaft block is on: the static Inputs (written by the
// scene's effect part every frame: enabled, the caster's world point or none, threshold colour, colour, distance falloff,
// blur size, intensity) have Enabled set and the camera is a game camera with post-processing on (the view, its captures
// and thumbnails; not item or character renders without post-processing, not reflection or preview cameras).
// Order: after URP's post-processing (AfterRenderingPostProcessing), after the DOF: HS2's effect runs as the last image
// effect of the camera, on the graded, bloomed, vignetted, anti-aliased and depth-of-field-blurred image.
// Sun position: the caster's viewport point in the rendering camera (the view or a capture camera); no caster = the
// screen centre; a caster behind the camera adds nothing (the pass is not enqueued). Arithmetic: SunShaftMath. The
// command-line switch -noSunShafts (tests, diagnostics) turns the pass off. The shader is referenced by the feature asset
// (kept in the player).
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using HS2Next.Scenes.Runtime;

namespace HS2Next
{
    public sealed class HS2SunShaftsFeature : ScriptableRendererFeature
    {
        /// <summary>The sun-shaft block as the pass needs it.</summary>
        public struct Input
        {
            /// <summary>effects.sunShafts.enabled.</summary>
            public bool Enabled;
            /// <summary>A caster node resolved to a point (else the screen centre).</summary>
            public bool HasCaster;
            /// <summary>The caster's world point (a character: its eye midpoint).</summary>
            public Vector3 CasterPoint;
            /// <summary>Threshold and colour as stored (0..1 values used as they are).</summary>
            public Color Threshold, Colour;
            /// <summary>Distance falloff (the prefilter's radius in UV units), blur size, intensity.</summary>
            public float MaxRadius, BlurSize, Intensity;
        }

        /// <summary>What the pass did for one camera (effects.state reads the view's).</summary>
        public struct Frame
        {
            public bool Drawn, Behind;
            /// <summary>The sun's viewport point (x, y).</summary>
            public Vector2 Sun;
            public int Width, Height;
        }

        /// <summary>The inputs of the current frame (written by SceneEffects; tests write it directly). Default: off.</summary>
        public static Input Inputs;

        /// <summary>Tests: inputs used instead of <see cref="Inputs"/> while set (the scene keeps writing Inputs).</summary>
        public static Input? Override;

        /// <summary>Tests: false keeps the pass out whatever the inputs (the feature's own switch).</summary>
        public static bool Enabled = true;

        /// <summary>Tests: the pass writes the shaft buffer as grey instead of the composite.</summary>
        public static bool DebugView;

        /// <summary>The last camera the feature saw with the block on (any camera kind that passed the filters).</summary>
        public static Frame Last { get; private set; }

        /// <summary>Frames in which the pass was enqueued (tests).</summary>
        public static int EnqueuedCount { get; private set; }

        public const string ShaderName = "HS2Next/Hidden/HS2SunShafts";

        static readonly bool CommandLineOff = Array.IndexOf(Environment.GetCommandLineArgs(), "-noSunShafts") >= 0;

        [SerializeField] Shader shader;

        Material material;
        HS2SunShaftsPass pass;

        public Shader Shader { get => shader; set => shader = value; }
        /// <summary>The pass (tests read its last parameters).</summary>
        public HS2SunShaftsPass Pass => pass;
        /// <summary>The -noSunShafts switch.</summary>
        public static bool SwitchedOff => CommandLineOff;

        public override void Create()
        {
            if (!shader) shader = Shader.Find(ShaderName);
            if (material) CoreUtils.Destroy(material);
            material = shader ? CoreUtils.CreateEngineMaterial(shader) : null;
            pass = new HS2SunShaftsPass(material);
        }

        /// <summary>The frame the pass would draw for <paramref name="cam"/> with <paramref name="input"/> (no rendering).</summary>
        public static Frame Evaluate(Camera cam, Input input, int width, int height)
        {
            var f = new Frame { Width = width, Height = height };
            Vector3 vp = input.HasCaster && cam ? cam.WorldToViewportPoint(input.CasterPoint) : new Vector3(0.5f, 0.5f, 1f);
            SunShaftMath.SunPosition(input.HasCaster, vp.x, vp.y, vp.z, out float sx, out float sy, out bool behind);
            f.Sun = new Vector2(sx, sy);
            f.Behind = behind;
            f.Drawn = input.Enabled && Enabled && !CommandLineOff && !behind;
            return f;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var input = Override ?? Inputs;
            if (!input.Enabled || !Enabled || CommandLineOff || pass == null || !material) return;
            ref var cd = ref renderingData.cameraData;
            if (cd.cameraType != CameraType.Game || !cd.postProcessEnabled) return;
            if (UniversalRenderer.IsOffscreenDepthTexture(ref renderingData.cameraData)) return;
            var cam = cd.camera;
            if (!cam) return;
            var desc = cd.cameraTargetDescriptor;
            var f = Evaluate(cam, input, desc.width, desc.height);
            Last = f;
            if (!f.Drawn) return;
            var colour = new Vector4(input.Colour.r, input.Colour.g, input.Colour.b, input.Colour.a) * input.Intensity;
            pass.Setup(f.Sun, input.MaxRadius, input.BlurSize, new Vector4(input.Threshold.r, input.Threshold.g, input.Threshold.b, 0f),
                colour, DebugView);
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            pass.requiresIntermediateTexture = true;
            renderer.EnqueuePass(pass);
            EnqueuedCount++;
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
            material = null;
        }
    }
}
