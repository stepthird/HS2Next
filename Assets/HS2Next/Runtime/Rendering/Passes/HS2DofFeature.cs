// HS2 Studio's item-focus depth of field as a URP renderer feature (
// Phase 3.3 package C). It lives in the production renderer (HS2NextURP_Renderer, after URP's SSAO feature) and draws
// HS2DofPass when the effect is active: the static Inputs (written by the scene's effect part, SceneEffects, every frame:
// enabled, the focus point in world space, focal size, aperture, the view's reference height) have enabled set and an
// aperture > 0, and the camera is a game camera with post-processing on (the view and its captures / thumbnails; not
// item or character test renders without post-processing, not reflection or preview cameras).
// Order: after URP's post-processing (AfterRenderingPostProcessing). HS2's legacy DepthOfField is an OnRenderImage effect
// that runs after PPv2's stack (PostProcessLayer draws at BeforeImageEffects; the recorded camera lists the layer before
// DepthOfField), so it blurs the graded, bloomed, vignetted and SMAA-filtered image; this pass does the same.
// CoC (exact): coc = saturate(A |d - f| / d - focalSize), A = 1 / (1 - aperture) - 1, d and f linear eye depths of the pixel
// and of the focus point (f = the focus point's view-space depth, as the legacy viewport z). Blur (approximate): HS2's
// maxBlurSize 2 half-resolution texels at coc 1, scaled by frame height / reference height for captures.
// Input.MaxBlur1080 (Phase 6.2, a runtime override's blur): when above 0 the radius at coc 1 is MaxBlur1080 x frame height
// / 1080 half-resolution texels instead; 0 (every official render) keeps the path above unchanged.
// The shader is referenced by the feature asset (kept in the player, U5).
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HS2Next
{
    public sealed class HS2DofFeature : ScriptableRendererFeature
    {
        /// <summary>The DOF block as the pass needs it.</summary>
        public struct Input
        {
            /// <summary>effects.depthOfField.enabled.</summary>
            public bool Enabled;
            /// <summary>World position of the focus (the focus node's point, else the camera pivot).</summary>
            public Vector3 FocusPoint;
            public float FocalSize, Aperture;
            /// <summary>The view's pixel height (0 = the frame's own height).</summary>
            public int ReferenceHeight;
            /// <summary>
            /// The largest blur radius on a 1080-line frame (a runtime override's blur): above 0 the radius at coc 1 is this x frame
            /// height / 1080 half-resolution texels; 0 = the feature's maxBlurSize scaled by frame height / reference height.
            /// </summary>
            public float MaxBlur1080;

            /// <summary>enabled and aperture > 0 (HS2: aperture 0 gives A = 0, no blur).</summary>
            public bool Active => Enabled && Aperture > 0f;
            /// <summary>CoC slope A = 1 / (1 − aperture) − 1 (aperture ≥ 1: a large finite slope).</summary>
            public float A => Aperture >= 0.9999f ? 9999f : 1f / (1f - Aperture) - 1f;
        }

        /// <summary>The inputs of the current frame (written by SceneEffects; tests write it directly). Default: off.</summary>
        public static Input Inputs;

        /// <summary>Tests: the pass writes (coc, dilated near coc, blend factor) instead of the blurred image.</summary>
        public static bool DebugView;

        /// <summary>HS2's legacy maxBlurSize: the blur radius at coc 1 in half-resolution texels.</summary>
        public const float HS2MaxBlurSize = 2f;

        [SerializeField] Shader shader;
        [Tooltip("Blur radius at coc 1 in half-resolution texels (HS2's maxBlurSize 2).")]
        [SerializeField] float maxBlurSize = HS2MaxBlurSize;

        Material material;
        HS2DofPass pass;

        public Shader Shader { get => shader; set => shader = value; }
        public float MaxBlurSize => maxBlurSize;
        /// <summary>The pass (tests read its last parameters).</summary>
        public HS2DofPass Pass => pass;
        /// <summary>Frames in which the pass was enqueued (tests).</summary>
        public static int EnqueuedCount { get; private set; }

        public const string ShaderName = "HS2Next/Hidden/HS2Dof";

        public override void Create()
        {
            if (!shader) shader = Shader.Find(ShaderName);
            if (material) CoreUtils.Destroy(material);
            material = shader ? CoreUtils.CreateEngineMaterial(shader) : null;
            pass = new HS2DofPass(material);
        }

        /// <summary>The radius R in half-resolution texels for a frame of <paramref name="frameHeight"/> pixels.</summary>
        public static float Radius(float maxBlur, int frameHeight, int referenceHeight) =>
            maxBlur * (referenceHeight > 0 && frameHeight > 0 ? (float)frameHeight / referenceHeight : 1f);

        /// <summary>The radius R in half-resolution texels of a blur given on a 1080-line frame, for a frame of <paramref name="frameHeight"/> pixels.</summary>
        public static float Radius1080(float maxBlur1080, int frameHeight) => maxBlur1080 * (frameHeight > 0 ? frameHeight / 1080f : 1f);

        /// <summary>The legacy CoC formula (DofTest's reference).</summary>
        public static float Coc(float a, float focalSize, float d, float f) =>
            Mathf.Clamp01(a * Mathf.Abs(d - f) / Mathf.Max(d, 1e-5f) - focalSize);

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var input = Inputs;
            if (!input.Active || pass == null || !material) return;
            ref var cd = ref renderingData.cameraData;
            if (cd.cameraType != CameraType.Game || !cd.postProcessEnabled) return;
            if (UniversalRenderer.IsOffscreenDepthTexture(ref renderingData.cameraData)) return;
            var cam = cd.camera;
            if (!cam) return;
            // the focus point's view-space depth (the legacy viewport z)
            float f = Mathf.Max(cam.worldToCameraMatrix.MultiplyPoint(input.FocusPoint).z * -1f, 1e-4f);
            int frameH = cd.cameraTargetDescriptor.height;
            float radius = input.MaxBlur1080 > 0f ? Radius1080(input.MaxBlur1080, frameH) : Radius(maxBlurSize, frameH, input.ReferenceHeight);
            pass.Setup(input.A, input.FocalSize, f, radius, DebugView);
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
