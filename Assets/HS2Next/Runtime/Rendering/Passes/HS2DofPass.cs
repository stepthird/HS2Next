// The render-graph pass of HS2DofFeature (Phase 3.3 package C): five full-screen
// draws of HS2Next/Hidden/HS2Dof (CoC, downsample, near dilation, half-resolution disc blur, composite; see the shader's
// header) from the camera colour and depth into a new colour texture that becomes the camera colour. Each draw carries
// its own MaterialPropertyBlock (textures and parameters are recorded with the draw, not read from the shared material
// at execution time).
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace HS2Next
{
    public sealed class HS2DofPass : ScriptableRenderPass
    {
        public const int PassCoc = 0, PassDownsample = 1, PassNearDilate = 2, PassBlur = 3, PassComposite = 4, PassDebug = 5;

        static readonly int ParamsId = Shader.PropertyToID("_HS2DofParams");
        static readonly int HalfTexelId = Shader.PropertyToID("_HS2DofHalfTexel");
        static readonly int CocId = Shader.PropertyToID("_HS2DofCoc");
        static readonly int HalfId = Shader.PropertyToID("_HS2DofHalf");
        static readonly int NearId = Shader.PropertyToID("_HS2DofNear");
        static readonly int BlurId = Shader.PropertyToID("_HS2DofBlur");
        static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
        static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
        static readonly int DepthId = Shader.PropertyToID("_CameraDepthTexture");

        readonly Material material;
        readonly MaterialPropertyBlock[] blocks = new MaterialPropertyBlock[6];
        Vector4 parameters;
        bool debug;

        /// <summary>The parameters of the last recorded frame (A, focalSize, focus eye depth, R in half-res texels).</summary>
        public Vector4 LastParams => parameters;

        public HS2DofPass(Material material)
        {
            this.material = material;
            for (int i = 0; i < blocks.Length; i++) blocks[i] = new MaterialPropertyBlock();
            profilingSampler = new ProfilingSampler("HS2 DOF");
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        /// <summary>Sets the frame's parameters: A, focal size, the focus point's eye depth and the radius R (half-res texels).</summary>
        public void Setup(float a, float focalSize, float focusEyeDepth, float radius, bool debugView = false)
        {
            parameters = new Vector4(a, focalSize, focusEyeDepth, radius);
            debug = debugView;
        }

        sealed class DrawData
        {
            internal Material material;
            internal int pass;
            internal MaterialPropertyBlock block;
            internal TextureHandle source, depth, coc, half, near, blur;
            internal Vector4 parameters, halfTexel;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!material) return;
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            if (resources.isActiveTargetBackBuffer) return;
            TextureHandle source = resources.cameraColor, depth = resources.cameraDepthTexture;
            if (!source.IsValid() || !depth.IsValid()) return;

            int w = Mathf.Max(1, cameraData.cameraTargetDescriptor.width), h = Mathf.Max(1, cameraData.cameraTargetDescriptor.height);
            int hw = Mathf.Max(1, w / 2), hh = Mathf.Max(1, h / 2);
            var halfTexel = new Vector4(1f / hw, 1f / hh, hw, hh);

            var colorDesc = renderGraph.GetTextureDesc(source);
            colorDesc.name = "_HS2DofResult";
            colorDesc.clearBuffer = false;
            var destination = renderGraph.CreateTexture(colorDesc);
            var coc = renderGraph.CreateTexture(Desc(w, h, GraphicsFormat.R16G16_SFloat, "_HS2DofCoc"));
            var half = renderGraph.CreateTexture(Desc(hw, hh, GraphicsFormat.R16G16B16A16_SFloat, "_HS2DofHalf"));
            var near = renderGraph.CreateTexture(Desc(hw, hh, GraphicsFormat.R16_SFloat, "_HS2DofNear"));
            var blur = renderGraph.CreateTexture(Desc(hw, hh, GraphicsFormat.R16G16B16A16_SFloat, "_HS2DofBlur"));

            Draw(renderGraph, "HS2 DOF CoC", PassCoc, coc, source, depth, default, default, default, default, halfTexel);
            Draw(renderGraph, "HS2 DOF Downsample", PassDownsample, half, source, depth, coc, default, default, default, halfTexel);
            Draw(renderGraph, "HS2 DOF Near", PassNearDilate, near, source, depth, coc, default, default, default, halfTexel);
            Draw(renderGraph, "HS2 DOF Blur", PassBlur, blur, source, depth, coc, half, near, default, halfTexel);
            Draw(renderGraph, debug ? "HS2 DOF Debug" : "HS2 DOF Composite", debug ? PassDebug : PassComposite, destination, source, depth, coc, default, near, blur, halfTexel);
            resources.cameraColor = destination;
        }

        static TextureDesc Desc(int w, int h, GraphicsFormat format, string name) => new TextureDesc(w, h)
        {
            format = format,
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            clearBuffer = false,
        };

        void Draw(RenderGraph rg, string name, int pass, TextureHandle target, TextureHandle source, TextureHandle depth,
            TextureHandle coc, TextureHandle half, TextureHandle near, TextureHandle blur, Vector4 halfTexel)
        {
            using (var builder = rg.AddRasterRenderPass<DrawData>(name, out var data, profilingSampler))
            {
                data.material = material;
                data.pass = pass;
                data.block = blocks[pass];
                data.parameters = parameters;
                data.halfTexel = halfTexel;
                data.source = source; builder.UseTexture(source, AccessFlags.Read);
                data.depth = depth; builder.UseTexture(depth, AccessFlags.Read);
                data.coc = coc; if (coc.IsValid()) builder.UseTexture(coc, AccessFlags.Read);
                data.half = half; if (half.IsValid()) builder.UseTexture(half, AccessFlags.Read);
                data.near = near; if (near.IsValid()) builder.UseTexture(near, AccessFlags.Read);
                data.blur = blur; if (blur.IsValid()) builder.UseTexture(blur, AccessFlags.Read);
                builder.SetRenderAttachment(target, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (DrawData d, RasterGraphContext ctx) =>
                {
                    var b = d.block;
                    b.Clear();
                    b.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                    b.SetVector(ParamsId, d.parameters);
                    b.SetVector(HalfTexelId, d.halfTexel);
                    b.SetTexture(BlitTextureId, (Texture)d.source);
                    b.SetTexture(DepthId, (Texture)d.depth);
                    if (d.coc.IsValid()) b.SetTexture(CocId, (Texture)d.coc);
                    if (d.half.IsValid()) b.SetTexture(HalfId, (Texture)d.half);
                    if (d.near.IsValid()) b.SetTexture(NearId, (Texture)d.near);
                    if (d.blur.IsValid()) b.SetTexture(BlurId, (Texture)d.blur);
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.pass, MeshTopology.Triangles, 3, 1, b);
                });
            }
        }
    }
}
