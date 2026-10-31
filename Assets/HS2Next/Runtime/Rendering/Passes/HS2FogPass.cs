// The render-graph passes of HS2FogFeature: HS2FogPass draws one full-screen triangle of HS2Next/Hidden/HS2Fog over the
// camera colour (the fog colour blended with one minus the opaque factor, the depth texture as input); HS2FogGlobalsPass
// sets or clears the fog globals the fitted transparent shaders read. Every draw carries its own MaterialPropertyBlock
// (the parameters are recorded with the draw, not read from the shared material at execution time).
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace HS2Next
{
    public sealed class HS2FogPass : ScriptableRenderPass
    {
        static readonly int ColorId = Shader.PropertyToID("_HS2FogColor");
        static readonly int ModeId = Shader.PropertyToID("_HS2FogMode");
        static readonly int ParamsId = Shader.PropertyToID("_HS2FogParams");
        static readonly int HeightId = Shader.PropertyToID("_HS2FogHeight");
        static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
        static readonly int DepthId = Shader.PropertyToID("_CameraDepthTexture");

        readonly Material material;
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        Vector4 color, mode, parameters, height;

        /// <summary>The parameters of the last recorded frame: mode (mode, excludeFar, near, far), (density, start, end, 0), (height, height density, 0, 0).</summary>
        public Vector4 LastMode => mode;
        public Vector4 LastParams => parameters;
        public Vector4 LastHeight => height;

        public HS2FogPass(Material material)
        {
            this.material = material;
            profilingSampler = new ProfilingSampler("HS2 Fog");
            // after the skybox and the background image (AfterRenderingSkybox + 0), before the copy of the opaque image:
            // shaders that sample the scene behind them (water and glass refraction, distortion) see the fogged image, as
            // HS2's grab passes run after its opaque image effects
            renderPassEvent = RenderPassEvent.AfterRenderingSkybox + 1;
        }

        public void Setup(HS2FogFeature.Input i, float near, float far)
        {
            color = new Vector4(i.ColorLinear.r, i.ColorLinear.g, i.ColorLinear.b, 1f);
            mode = new Vector4(i.Mode, i.ExcludeFarPixels ? 1f : 0f, near, far);
            parameters = new Vector4(i.Density, i.Start, i.End, 0f);
            height = new Vector4(i.Height, i.HeightDensity, 0f, 0f);
        }

        sealed class DrawData
        {
            internal Material material;
            internal MaterialPropertyBlock block;
            internal TextureHandle depth;
            internal Vector4 color, mode, parameters, height;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!material) return;
            var resources = frameData.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;
            TextureHandle target = resources.activeColorTexture, depth = resources.cameraDepthTexture;
            if (!target.IsValid() || !depth.IsValid()) return;
            using (var builder = renderGraph.AddRasterRenderPass<DrawData>("HS2 Fog", out var data, profilingSampler))
            {
                data.material = material;
                data.block = block;
                data.color = color; data.mode = mode; data.parameters = parameters; data.height = height;
                data.depth = depth; builder.UseTexture(depth, AccessFlags.Read);
                builder.SetRenderAttachment(target, 0, AccessFlags.ReadWrite);
                builder.SetRenderFunc(static (DrawData d, RasterGraphContext ctx) =>
                {
                    var b = d.block;
                    b.Clear();
                    b.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                    b.SetVector(ColorId, d.color);
                    b.SetVector(ModeId, d.mode);
                    b.SetVector(ParamsId, d.parameters);
                    b.SetVector(HeightId, d.height);
                    b.SetTexture(DepthId, (Texture)d.depth);
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, 0, MeshTopology.Triangles, 3, 1, b);
                });
            }
        }
    }

    /// <summary>Sets (or clears) the fog globals of the fitted transparent shaders: _HS2NextFogOn (on, mode bit, near, far),
    /// _HS2NextFogParams (the engine's fog parameters), _HS2NextFogColor (linear).</summary>
    public sealed class HS2FogGlobalsPass : ScriptableRenderPass
    {
        public static readonly int OnId = Shader.PropertyToID("_HS2NextFogOn");
        public static readonly int ParamsId = Shader.PropertyToID("_HS2NextFogParams");
        public static readonly int ColorId = Shader.PropertyToID("_HS2NextFogColor");

        /// <summary>The last executed globals pass switched the fog on.</summary>
        public static bool LastOn { get; private set; }

        (Vector4 on, Vector4 parameters, Vector4 color) values;
        readonly string passName;

        public HS2FogGlobalsPass(RenderPassEvent evt, string name)
        {
            renderPassEvent = evt;
            passName = name;   // kept here: a release player's sampler carries no name
            profilingSampler = new ProfilingSampler(name);
        }

        public void Setup((Vector4 on, Vector4 parameters, Vector4 color) v) => values = v;

        sealed class GlobalsData
        {
            internal Vector4 on, parameters, color;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddRasterRenderPass<GlobalsData>(passName, out var data, profilingSampler))
            {
                data.on = values.on; data.parameters = values.parameters; data.color = values.color;
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (GlobalsData d, RasterGraphContext ctx) =>
                {
                    ctx.cmd.SetGlobalVector(OnId, d.on);
                    ctx.cmd.SetGlobalVector(ParamsId, d.parameters);
                    ctx.cmd.SetGlobalVector(ColorId, d.color);
                    LastOn = d.on.x > 0.5f;
                });
            }
        }
    }
}
