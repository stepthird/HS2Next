// The render-graph pass of HS2SunShaftsFeature: six full-screen draws of HS2Next/Hidden/HS2SunShafts (prefilter, four
// radial blurs ping-ponging two 8-bit sRGB textures of the frame's size, composite; see the shader's header) from the
// camera colour and depth into a new colour texture that becomes the camera colour. Each draw carries its own
// MaterialPropertyBlock (textures and parameters are recorded with the draw, not read from the shared material at
// execution time).
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using HS2Next.Scenes.Runtime;

namespace HS2Next
{
    public sealed class HS2SunShaftsPass : ScriptableRenderPass
    {
        public const int PassPrefilter = 0, PassRadial = 1, PassComposite = 2, PassDebug = 3;
        /// <summary>The intermediate format: 8-bit sRGB, as the original effect's temporaries in a linear-space project.</summary>
        public const GraphicsFormat BufferFormat = GraphicsFormat.R8G8B8A8_SRGB;

        static readonly int SunId = Shader.PropertyToID("_HS2ShaftSun");
        static readonly int ThresholdId = Shader.PropertyToID("_HS2ShaftThreshold");
        static readonly int ColorId = Shader.PropertyToID("_HS2ShaftColor");
        static readonly int SizeId = Shader.PropertyToID("_HS2ShaftSize");
        static readonly int ShaftTexId = Shader.PropertyToID("_HS2ShaftTex");
        static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
        static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
        static readonly int DepthId = Shader.PropertyToID("_CameraDepthTexture");

        readonly Material material;
        readonly MaterialPropertyBlock[] blocks = new MaterialPropertyBlock[6];
        Vector2 sun;
        float maxRadius, blurSize;
        Vector4 threshold, colour;
        bool debug;

        /// <summary>The parameters of the last recorded frame: sun (x, y), maxRadius, blur size.</summary>
        public Vector4 LastParams => new Vector4(sun.x, sun.y, maxRadius, blurSize);

        public HS2SunShaftsPass(Material material)
        {
            this.material = material;
            for (int i = 0; i < blocks.Length; i++) blocks[i] = new MaterialPropertyBlock();
            profilingSampler = new ProfilingSampler("HS2 Sun Shafts");
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        /// <summary>The frame's parameters: the sun's viewport point, the falloff radius, the blur size, the threshold and colour × intensity.</summary>
        public void Setup(Vector2 sunViewport, float maxRadius, float blurSize, Vector4 threshold, Vector4 colourTimesIntensity, bool debugView = false)
        {
            sun = sunViewport;
            this.maxRadius = maxRadius;
            this.blurSize = blurSize;
            this.threshold = threshold;
            colour = colourTimesIntensity;
            debug = debugView;
        }

        sealed class DrawData
        {
            internal Material material;
            internal int pass;
            internal MaterialPropertyBlock block;
            internal TextureHandle source, depth, shafts;
            internal Vector4 sun, threshold, colour, size;
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
            var size = new Vector4(w, h, 1f / w, 1f / h);

            var colorDesc = renderGraph.GetTextureDesc(source);
            colorDesc.name = "_HS2SunShaftsResult";
            colorDesc.clearBuffer = false;
            var destination = renderGraph.CreateTexture(colorDesc);
            var a = renderGraph.CreateTexture(Desc(w, h, "_HS2ShaftA"));
            var b = renderGraph.CreateTexture(Desc(w, h, "_HS2ShaftB"));

            var sunP = new Vector4(sun.x, sun.y, maxRadius, 0f);
            Draw(renderGraph, "HS2 Shafts Prefilter", PassPrefilter, 0, a, source, depth, default, sunP, size);
            for (int i = 0; i < SunShaftMath.StepFactors.Length; i++)
            {
                var s = new Vector4(sun.x, sun.y, maxRadius, SunShaftMath.Step(i, blurSize));
                bool even = (i & 1) == 0;
                Draw(renderGraph, "HS2 Shafts Radial " + i, PassRadial, 1 + i, even ? b : a, default, default, even ? a : b, s, size);
            }
            // four blurs: a -> b -> a -> b -> a
            Draw(renderGraph, debug ? "HS2 Shafts Debug" : "HS2 Shafts Composite", debug ? PassDebug : PassComposite, 5, destination, source, default, a, sunP, size);
            resources.cameraColor = destination;
        }

        static TextureDesc Desc(int w, int h, string name) => new TextureDesc(w, h)
        {
            format = BufferFormat,
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            clearBuffer = false,
        };

        void Draw(RenderGraph rg, string name, int pass, int blockIndex, TextureHandle target, TextureHandle source, TextureHandle depth,
            TextureHandle shafts, Vector4 sunParams, Vector4 size)
        {
            using (var builder = rg.AddRasterRenderPass<DrawData>(name, out var data, profilingSampler))
            {
                data.material = material;
                data.pass = pass;
                data.block = blocks[blockIndex];
                data.sun = sunParams;
                data.threshold = threshold;
                data.colour = colour;
                data.size = size;
                data.source = source; if (source.IsValid()) builder.UseTexture(source, AccessFlags.Read);
                data.depth = depth; if (depth.IsValid()) builder.UseTexture(depth, AccessFlags.Read);
                data.shafts = shafts; if (shafts.IsValid()) builder.UseTexture(shafts, AccessFlags.Read);
                builder.SetRenderAttachment(target, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (DrawData d, RasterGraphContext ctx) =>
                {
                    var b = d.block;
                    b.Clear();
                    b.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                    b.SetVector(SunId, d.sun);
                    b.SetVector(ThresholdId, d.threshold);
                    b.SetVector(ColorId, d.colour);
                    b.SetVector(SizeId, d.size);
                    if (d.source.IsValid()) b.SetTexture(BlitTextureId, (Texture)d.source);
                    if (d.depth.IsValid()) b.SetTexture(DepthId, (Texture)d.depth);
                    if (d.shafts.IsValid()) b.SetTexture(ShaftTexId, (Texture)d.shafts);
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.pass, MeshTopology.Triangles, 3, 1, b);
                });
            }
        }
    }
}
