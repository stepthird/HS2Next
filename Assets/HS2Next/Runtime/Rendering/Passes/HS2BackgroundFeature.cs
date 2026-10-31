// The scene's background image as a URP renderer feature (R17; Phase 3.6 package C).
// HS2 draws its background as a quad under the main camera that fills the frustum at z = far × 0.98 (BackgroundCtrl's
// farRate [V]) with the image stretched to the view; it is ordinary geometry: the map in front hides it, it hides the
// clear colour and the sky, and it goes through the post effects (DOF treats it as far). This feature draws the same
// thing as one full-screen triangle (shader HS2Next/Hidden/HS2SceneImage pass 0) after the skybox and before the
// transparents, at the NDC depth of view z = 0.98 × far of each camera's own projection (refitted for every camera, FOV
// and aspect: HS2's missing refit on aspect changes, Q10, is not kept). The depth test is against the OPAQUES only (the
// camera depth texture, copied after the opaques; a pixel whose opaque depth is nearer than the plane keeps the scene)
// and the plane's depth is written: under URP the bundle's AIOsky/V2 sky writes depth at its own dome distance, nearer
// than 0.98 × far, so a hardware depth test either lost to the sky (drawn before it) or was hidden by it (drawn after;
// BackgroundTest B3 measured both); HS2's Built-in sky sits at the far plane, so its quad covers the sky, as this does.
// Cameras: the view camera (ViewSetup.Of) and every game camera that renders inside CaptureScope() (screenshots, the
// bridge capture, scene thumbnails); never character thumbnails, previews or the Editor's scene view. The texture is
// the static Texture (written by the scene part SceneImages; null = nothing is drawn and nothing is enqueued, so scenes
// without a background render exactly as before). Registration: AddTo / RemoveFrom on the active renderer at run time
// (AppPipeline), as AmbientOcclusionControl does for HS2AoFeature. The shader is listed in the players' shader set
// (it lives under Assets/HS2Next/Shaders).
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace HS2Next
{
    public sealed class HS2BackgroundFeature : ScriptableRendererFeature
    {
        public const string ShaderName = "HS2Next/Hidden/HS2SceneImage";
        /// <summary>HS2's BackgroundCtrl.farRate [V]: the image plane at view z = far × FarRate.</summary>
        public const float FarRate = 0.98f;

        /// <summary>The background texture of the current scene (null = none).</summary>
        public static Texture Texture;

        static int captureDepth;
        /// <summary>True inside <see cref="CaptureScope"/>.</summary>
        public static bool InCapture => captureDepth > 0;

        /// <summary>Every game camera rendered until the scope is disposed draws the background (captures, thumbnails).</summary>
        public static IDisposable CaptureScope() => new Scope();

        sealed class Scope : IDisposable
        {
            bool done;
            public Scope() { captureDepth++; }
            public void Dispose() { if (done) return; done = true; captureDepth = Math.Max(0, captureDepth - 1); }
        }

        /// <summary>Frames in which the pass was enqueued (tests).</summary>
        public static int EnqueuedCount { get; private set; }

        /// <summary>Passes recorded, and the recordings skipped because the active target was the back buffer (tests).</summary>
        public static int RecordedCount, SkippedBackBuffer;



        [SerializeField] Shader shader;
        Material material;
        BackgroundPass pass;

        public Shader Shader => shader;

        public override void Create()
        {
            if (!shader) shader = Shader.Find(ShaderName);
            if (material) CoreUtils.Destroy(material);
            material = shader ? CoreUtils.CreateEngineMaterial(shader) : null;
            pass = new BackgroundPass(material);
        }

        /// <summary>The NDC depth of view z = −<paramref name="viewDepth"/> under <paramref name="gpuProjection"/> (reversed-Z aware).</summary>
        public static float NdcDepth(Matrix4x4 gpuProjection, float viewDepth)
        {
            var c = gpuProjection * new Vector4(0f, 0f, -viewDepth, 1f);
            return Mathf.Abs(c.w) > 1e-12f ? c.z / c.w : 0f;
        }

        /// <summary>The cameras that draw scene images: the view, or any game camera inside a capture scope.</summary>
        public static bool IsSceneCamera(Camera cam, bool inCapture) => cam && (inCapture || ViewSetup.Of(cam) != null);

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var tex = Texture;
            if (!tex || pass == null || !material) return;
            ref var cd = ref renderingData.cameraData;
            if (cd.cameraType != CameraType.Game) return;
            if (UniversalRenderer.IsOffscreenDepthTexture(ref renderingData.cameraData)) return;
            var cam = cd.camera;
            if (!IsSceneCamera(cam, InCapture)) return;
            var p = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true);
            pass.Setup(tex, NdcDepth(p, cam.farClipPlane * FarRate));
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

        // ------------------------------------------------------------ registration (AppPipeline)

        /// <summary>
        /// Adds the background and frame features to the active pipeline's first Universal renderer when it has none
        /// (run-time instances, never saved); returns what was added for <see cref="RemoveFrom"/>. Safe without URP.
        /// </summary>
        public static ScriptableRendererFeature[] AddSceneImageFeatures()
        {
            var rd = ActiveRendererData();
            if (!rd) return Array.Empty<ScriptableRendererFeature>();
            var added = new System.Collections.Generic.List<ScriptableRendererFeature>();
            if (!rd.TryGetRendererFeature<HS2BackgroundFeature>(out _)) added.Add(Add<HS2BackgroundFeature>(rd, "HS2BackgroundFeature (runtime)"));
            if (!rd.TryGetRendererFeature<HS2FrameFeature>(out _)) added.Add(Add<HS2FrameFeature>(rd, "HS2FrameFeature (runtime)"));
            if (added.Count > 0) rd.SetDirty();
            return added.ToArray();
        }

        /// <summary>Removes features added by <see cref="AddSceneImageFeatures"/> (idempotent).</summary>
        public static void RemoveSceneImageFeatures(ScriptableRendererFeature[] added)
        {
            if (added == null || added.Length == 0) return;
            var rd = ActiveRendererData();
            foreach (var f in added)
            {
                if (!f) continue;
                if (rd) rd.rendererFeatures.Remove(f);
                CoreUtils.Destroy(f);
            }
            if (rd) rd.SetDirty();
        }

        static T Add<T>(ScriptableRendererData rd, string name) where T : ScriptableRendererFeature
        {
            var f = ScriptableObject.CreateInstance<T>();
            f.name = name;
            f.hideFlags = HideFlags.DontSave;
            f.SetActive(true);
            rd.rendererFeatures.Add(f);
            return f;
        }

        static UniversalRendererData ActiveRendererData()
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset asset) || asset.rendererDataList.Length == 0) return null;
            return asset.rendererDataList[0] as UniversalRendererData;
        }

        // ------------------------------------------------------------ pass

        sealed class BackgroundPass : ScriptableRenderPass
        {
            static readonly int TexId = Shader.PropertyToID("_HS2ImageTex");
            static readonly int DepthId = Shader.PropertyToID("_HS2ImageDepth");
            static readonly int ScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
            static readonly int DepthTexId = Shader.PropertyToID("_CameraDepthTexture");

            readonly Material material;
            readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
            Texture texture;
            float depth;

            public BackgroundPass(Material m)
            {
                material = m;
                profilingSampler = new ProfilingSampler("HS2 Background");
                renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
            }

            public void Setup(Texture t, float ndcDepth) { texture = t; depth = ndcDepth; }

            sealed class Data
            {
                internal Material material;
                internal MaterialPropertyBlock block;
                internal Texture texture;
                internal float depth;
                internal TextureHandle sceneDepth;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (!material || !texture) return;
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) { SkippedBackBuffer++; return; }
                var color = resources.activeColorTexture;
                var depthTex = resources.activeDepthTexture;
                var sceneDepth = resources.cameraDepthTexture;
                if (!color.IsValid() || !depthTex.IsValid() || !sceneDepth.IsValid()) return;
                RecordedCount++;
                using (var builder = renderGraph.AddRasterRenderPass<Data>("HS2 Background", out var data, profilingSampler))
                {
                    data.material = material;
                    data.block = block;
                    data.texture = texture;
                    data.depth = depth;
                    data.sceneDepth = sceneDepth;
                    builder.UseTexture(sceneDepth, AccessFlags.Read);
                    builder.SetRenderAttachment(color, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(depthTex, AccessFlags.ReadWrite);
                    builder.AllowGlobalStateModification(false);
                    builder.SetRenderFunc(static (Data d, RasterGraphContext ctx) =>
                    {
                        var b = d.block;
                        b.Clear();
                        b.SetTexture(TexId, d.texture);
                        b.SetFloat(DepthId, d.depth);
                        b.SetVector(ScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                        b.SetTexture(DepthTexId, (Texture)d.sceneDepth);
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0, MeshTopology.Triangles, 3, 1, b);
                    });
                }
            }
        }
    }
}
