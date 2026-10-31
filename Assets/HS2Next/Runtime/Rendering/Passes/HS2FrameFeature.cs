// The scene's frame image as a URP renderer feature (R16, R28; Phase 3.6 package C).
// HS2 shows the frame as a full-canvas RawImage drawn by its UI camera over the 3D view (PNG alpha shows the scene
// through, stretched to the canvas) and renders that camera into its screenshots. This feature draws the frame texture
// alpha-blended over the camera's whole target after post-processing (and after the item-focus DOF: event + 1), so it
// is not graded, bloomed or blurred; on the view camera the target is the viewport (the camera renders into its rect).
// Gizmos, route lines and markers draw after it (on top, R28). Cameras: the view camera, and capture cameras only inside
// CaptureScope(true) (screenshots by default; the bridge `capture` passes false, so its images and baselines do not
// change); never scene thumbnails (their crop would cut the frame) or other cameras. Texture: the static Texture
// (SceneImages; null = nothing enqueued). Registered at run time with HS2BackgroundFeature.AddSceneImageFeatures.
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace HS2Next
{
    public sealed class HS2FrameFeature : ScriptableRendererFeature
    {
        /// <summary>The frame texture of the current scene (null = none).</summary>
        public static Texture Texture;

        static int scopeDepth, includeDepth;
        /// <summary>True inside a capture scope (the view camera does not render then).</summary>
        public static bool InCapture => scopeDepth > 0;
        /// <summary>True inside a capture scope that includes the frame.</summary>
        public static bool CaptureIncludesFrame => includeDepth > 0;

        /// <summary>A capture scope: capture cameras draw the frame iff <paramref name="includeFrame"/>.</summary>
        public static IDisposable CaptureScope(bool includeFrame) => new Scope(includeFrame);

        sealed class Scope : IDisposable
        {
            readonly bool include;
            bool done;
            public Scope(bool include)
            {
                this.include = include;
                scopeDepth++;
                if (include) includeDepth++;
            }
            public void Dispose()
            {
                if (done) return;
                done = true;
                scopeDepth = Math.Max(0, scopeDepth - 1);
                if (include) includeDepth = Math.Max(0, includeDepth - 1);
            }
        }

        /// <summary>Frames in which the pass was enqueued (tests).</summary>
        public static int EnqueuedCount { get; private set; }

        [SerializeField] Shader shader;
        Material material;
        FramePass pass;

        public Shader Shader => shader;

        public override void Create()
        {
            if (!shader) shader = Shader.Find(HS2BackgroundFeature.ShaderName);
            if (material) CoreUtils.Destroy(material);
            material = shader ? CoreUtils.CreateEngineMaterial(shader) : null;
            pass = new FramePass(material);
        }

        /// <summary>Whether <paramref name="cam"/> draws the frame now.</summary>
        public static bool Draws(Camera cam)
        {
            if (!cam) return false;
            if (InCapture) return CaptureIncludesFrame;
            return ViewSetup.Of(cam) != null;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var tex = Texture;
            if (!tex || pass == null || !material) return;
            ref var cd = ref renderingData.cameraData;
            if (cd.cameraType != CameraType.Game) return;
            if (UniversalRenderer.IsOffscreenDepthTexture(ref renderingData.cameraData)) return;
            if (!Draws(cd.camera)) return;
            pass.Setup(tex);
            pass.requiresIntermediateTexture = true;
            renderer.EnqueuePass(pass);
            EnqueuedCount++;
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
            material = null;
        }

        sealed class FramePass : ScriptableRenderPass
        {
            static readonly int TexId = Shader.PropertyToID("_HS2ImageTex");
            static readonly int ScaleBiasId = Shader.PropertyToID("_BlitScaleBias");

            readonly Material material;
            readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
            Texture texture;

            public FramePass(Material m)
            {
                material = m;
                profilingSampler = new ProfilingSampler("HS2 Frame");
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing + 1;
            }

            public void Setup(Texture t) => texture = t;

            sealed class Data
            {
                internal Material material;
                internal MaterialPropertyBlock block;
                internal Texture texture;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (!material || !texture) return;
                var resources = frameData.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;
                var color = resources.activeColorTexture;
                if (!color.IsValid()) return;
                using (var builder = renderGraph.AddRasterRenderPass<Data>("HS2 Frame", out var data, profilingSampler))
                {
                    data.material = material;
                    data.block = block;
                    data.texture = texture;
                    builder.SetRenderAttachment(color, 0, AccessFlags.ReadWrite);
                    builder.AllowGlobalStateModification(false);
                    builder.SetRenderFunc(static (Data d, RasterGraphContext ctx) =>
                    {
                        var b = d.block;
                        b.Clear();
                        b.SetTexture(TexId, d.texture);
                        b.SetVector(ScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                        ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, 1, MeshTopology.Triangles, 3, 1, b);
                    });
                }
            }
        }
    }
}
