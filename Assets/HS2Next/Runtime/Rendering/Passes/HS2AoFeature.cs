// HS2 Studio's ambient obscurance as a URP renderer feature (E8's
// alternative; Phase 3.3 package B, fix round): URP's SSAO (Alchemy, normal-based, local) could match HS2's amount but not
// its pattern (package H's ao_* captures: HS2's MSVO darkens around objects on the floor and leaves convex shapes alone;
// SSAO darkened the spheres and the character). The pass draws HS2Next/Hidden/HS2Ao (see the shader's header) after the
// opaques: obscurance at half resolution from the depth texture, a depth-aware blur, then the camera colour multiplied.
// Inputs are static (written by AmbientOcclusionControl from scene.effects.ambientOcclusion); the feature draws only while
// Inputs.Active, on game cameras with post-processing. AmbientOcclusionControl adds a run-time instance to the active
// renderer when the renderer asset has none (never saved; removed on release), so no asset entry is required.
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace HS2Next
{
    public sealed class HS2AoFeature : ScriptableRendererFeature
    {
        public struct Input
        {
            public bool Active;
            /// <summary>Base radius as a fraction of the frame height (the scales are 1, 2, 4, 8 × it).</summary>
            public float Radius;
            /// <summary>Exponent of the visibility (1 − obscurance)^Intensity.</summary>
            public float Intensity;
            /// <summary>Occluder thickness in units of the scale's world size.</summary>
            public float Thickness;
            /// <summary>Weights of the four scales.</summary>
            public Vector4 ScaleWeights;
        }

        /// <summary>The inputs of the current frame (written by AmbientOcclusionControl). Default: off.</summary>
        public static Input Inputs;

        /// <summary>Frames in which the pass was enqueued (tests).</summary>
        public static int EnqueuedCount { get; private set; }

        public const string ShaderName = "HS2Next/Hidden/HS2Ao";

        [SerializeField] Shader shader;
        Material material;
        HS2AoPass pass;

        public Shader Shader { get => shader; set => shader = value; }

        public override void Create()
        {
            if (!shader) shader = Shader.Find(ShaderName);
            if (material) CoreUtils.Destroy(material);
            material = shader ? CoreUtils.CreateEngineMaterial(shader) : null;
            pass = new HS2AoPass(material);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var input = Inputs;
            if (!input.Active || pass == null || !material) return;
            ref var cd = ref renderingData.cameraData;
            if (cd.cameraType != CameraType.Game || !cd.postProcessEnabled) return;
            if (UniversalRenderer.IsOffscreenDepthTexture(ref renderingData.cameraData)) return;
            var cam = cd.camera;
            if (!cam) return;
            int h = Mathf.Max(2, cd.cameraTargetDescriptor.height / 2);
            float px = 2f * Mathf.Tan(0.5f * cam.fieldOfView * Mathf.Deg2Rad) / h;   // world size of a half-res texel at eye depth 1
            pass.Setup(new Vector4(Mathf.Max(0.5f, input.Radius * h), Mathf.Max(0f, input.Intensity), Mathf.Max(0.01f, input.Thickness), px), input.ScaleWeights);
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            renderer.EnqueuePass(pass);
            EnqueuedCount++;
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
            material = null;
        }
    }

    public sealed class HS2AoPass : ScriptableRenderPass
    {
        public const int PassAo = 0, PassBlur = 1, PassComposite = 2;
        static readonly int ParamsId = Shader.PropertyToID("_HS2AoParams");
        static readonly int TexelId = Shader.PropertyToID("_HS2AoTexel");
        static readonly int ScalesId = Shader.PropertyToID("_HS2AoScales");
        static readonly int AoTexId = Shader.PropertyToID("_HS2AoTex");
        static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
        static readonly int DepthId = Shader.PropertyToID("_CameraDepthTexture");

        readonly Material material;
        readonly MaterialPropertyBlock[] blocks = { new MaterialPropertyBlock(), new MaterialPropertyBlock(), new MaterialPropertyBlock() };
        Vector4 parameters, scales;

        public HS2AoPass(Material material)
        {
            this.material = material;
            profilingSampler = new ProfilingSampler("HS2 AO");
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        }

        public void Setup(Vector4 p, Vector4 scaleWeights)
        {
            parameters = p;
            scales = scaleWeights;
        }

        sealed class DrawData
        {
            internal Material material;
            internal int pass;
            internal MaterialPropertyBlock block;
            internal TextureHandle depth, ao;
            internal Vector4 parameters, texel, scales;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!material) return;
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            if (resources.isActiveTargetBackBuffer) return;
            TextureHandle color = resources.cameraColor, depth = resources.cameraDepthTexture;
            if (!color.IsValid() || !depth.IsValid()) return;
            int w = Mathf.Max(2, cameraData.cameraTargetDescriptor.width / 2), h = Mathf.Max(2, cameraData.cameraTargetDescriptor.height / 2);
            var texel = new Vector4(1f / w, 1f / h, w, h);
            var ao = renderGraph.CreateTexture(Desc(w, h, "_HS2AoRaw"));
            var blurred = renderGraph.CreateTexture(Desc(w, h, "_HS2AoBlur"));
            Draw(renderGraph, "HS2 AO Obscurance", PassAo, ao, depth, default, texel, false);
            Draw(renderGraph, "HS2 AO Blur", PassBlur, blurred, depth, ao, texel, false);
            Draw(renderGraph, "HS2 AO Composite", PassComposite, color, depth, blurred, texel, true);
        }

        static TextureDesc Desc(int w, int h, string name) => new TextureDesc(w, h)
        {
            format = GraphicsFormat.R16_SFloat,
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            clearBuffer = false,
        };

        void Draw(RenderGraph rg, string name, int pass, TextureHandle target, TextureHandle depth, TextureHandle ao, Vector4 texel, bool blendOnto)
        {
            using (var builder = rg.AddRasterRenderPass<DrawData>(name, out var data, profilingSampler))
            {
                data.material = material;
                data.pass = pass;
                data.block = blocks[pass];
                data.parameters = parameters;
                data.scales = scales;
                data.texel = texel;
                data.depth = depth; builder.UseTexture(depth, AccessFlags.Read);
                data.ao = ao; if (ao.IsValid()) builder.UseTexture(ao, AccessFlags.Read);
                builder.SetRenderAttachment(target, 0, blendOnto ? AccessFlags.ReadWrite : AccessFlags.Write);
                builder.SetRenderFunc(static (DrawData d, RasterGraphContext ctx) =>
                {
                    var b = d.block;
                    b.Clear();
                    b.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                    b.SetVector(ParamsId, d.parameters);
                    b.SetVector(TexelId, d.texel);
                    b.SetVector(ScalesId, d.scales);
                    b.SetTexture(DepthId, (Texture)d.depth);
                    if (d.ao.IsValid()) b.SetTexture(AoTexId, (Texture)d.ao);
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.pass, MeshTopology.Triangles, 3, 1, b);
                });
            }
        }
    }
}
