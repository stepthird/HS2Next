// The render-graph passes of MosaicWideFeature (method (b); package R of 5.M), drawn with
// HS2Next/Hidden/HS2MosaicWide (see the shader's header for what each pass computes):
//   mask       the groups' drawn members (DrawRenderer, every submesh) into an RGBA8 mask, group g in channel g, with the
//              camera's own view-projection (jitter included) and the member's depth placement, depth-tested LEqual against
//              the camera's depth buffer: the mask holds exactly the member pixels that won the depth test in the frame
//              (none under clothes); a multisampled camera compares with the depth texture instead (tight tolerance);
//   quarter    the mask reduced 4 x 4 by max;
//   per group  occupancy, dilation (two blocks) and the block means at block resolution;
//   composite  the camera colour with every covered block replaced by its mean (largest block first); the result becomes the
//              camera colour.
// Every full-screen draw carries its own MaterialPropertyBlock (recorded with the draw).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace HS2Next
{
    public sealed class MosaicWidePass : ScriptableRenderPass
    {
        public const int PassMask = 0, PassQuarter = 4, PassOccupancy = 5, PassDilate = 6, PassMean = 7, PassComposite = 8, PassMaskDepth = 9;

        /// <summary>The last recorded frame tested the mask against the camera's depth buffer (false: the depth texture).</summary>
        public bool LastMaskByDepthBuffer { get; private set; }

        static readonly int ViewProjId = Shader.PropertyToID("_HS2MWViewProj");
        static readonly int ViewId = Shader.PropertyToID("_HS2MWView");
        static readonly int DepthId = Shader.PropertyToID("_HS2MWDepth");
        static readonly int SizeId = Shader.PropertyToID("_HS2MWSize");
        static readonly int DepthParamsId = Shader.PropertyToID("_HS2MWDepthParams");
        static readonly int MaskId = Shader.PropertyToID("_HS2MWMask");
        static readonly int QuarterId = Shader.PropertyToID("_HS2MWQuarter");
        static readonly int OccId = Shader.PropertyToID("_HS2MWOcc");
        static readonly int GroupId = Shader.PropertyToID("_HS2MWGroup");
        static readonly int GridId = Shader.PropertyToID("_HS2MWGrid");
        static readonly int CountId = Shader.PropertyToID("_HS2MWCount");
        static readonly int DebugId = Shader.PropertyToID("_HS2MWDebug");
        static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
        static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
        static readonly int[] DilIds = { Shader.PropertyToID("_HS2MWDil0"), Shader.PropertyToID("_HS2MWDil1"), Shader.PropertyToID("_HS2MWDil2"), Shader.PropertyToID("_HS2MWDil3") };
        static readonly int[] MeanIds = { Shader.PropertyToID("_HS2MWMean0"), Shader.PropertyToID("_HS2MWMean1"), Shader.PropertyToID("_HS2MWMean2"), Shader.PropertyToID("_HS2MWMean3") };
        static readonly int[] GroupIds = { Shader.PropertyToID("_HS2MWGroup0"), Shader.PropertyToID("_HS2MWGroup1"), Shader.PropertyToID("_HS2MWGroup2"), Shader.PropertyToID("_HS2MWGroup3") };
        static readonly int[] GridIds = { Shader.PropertyToID("_HS2MWGrid0"), Shader.PropertyToID("_HS2MWGrid1"), Shader.PropertyToID("_HS2MWGrid2"), Shader.PropertyToID("_HS2MWGrid3") };

        readonly Material material;
        readonly List<MosaicWideGroup> groups = new List<MosaicWideGroup>();
        Matrix4x4 viewProj, view;
        Vector4 depthParams;
        bool debug;

        /// <summary>The groups of the last recorded frame.</summary>
        public IReadOnlyList<MosaicWideGroup> Groups => groups;

        public MosaicWidePass(Material material)
        {
            this.material = material;
            profilingSampler = new ProfilingSampler("HS2 Mosaic Wide");
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        /// <summary>The frame's groups (the matrices are the camera data's, taken when the pass records).</summary>
        public void Setup(List<MosaicWideGroup> g, Camera cam, bool debugView)
        {
            groups.Clear();
            groups.AddRange(g);
            depthParams = new Vector4(cam.orthographic ? 1f : 0f, cam.nearClipPlane, cam.farClipPlane, 0f);
            debug = debugView;
        }

        /// <summary>The block grid of a group: first block index (x, y) and the number of blocks (x, y) over the frame.</summary>
        public static Vector4 Grid(MosaicWideGroup g, int w, int h)
        {
            float b = Mathf.Max(1f, g.Block);
            float k0x = Mathf.Floor((0f - g.Anchor.x) / b), k0y = Mathf.Floor((0f - g.Anchor.y) / b);
            float k1x = Mathf.Floor((w - 1f - g.Anchor.x) / b), k1y = Mathf.Floor((h - 1f - g.Anchor.y) / b);
            return new Vector4(k0x, k0y, Mathf.Max(1f, k1x - k0x + 1f), Mathf.Max(1f, k1y - k0y + 1f));
        }

        sealed class MaskData
        {
            internal Material material;
            internal TextureHandle depth;
            internal Matrix4x4 viewProj, view;
            internal Vector4 size, depthParams;
            internal bool byBuffer;
            internal readonly List<(Renderer r, int sub, int pass)> draws = new List<(Renderer, int, int)>();
        }

        sealed class DrawData
        {
            internal Material material;
            internal int pass;
            internal MaterialPropertyBlock block;
            internal TextureHandle source, mask, quarter, occ;
            internal TextureHandle[] dil = new TextureHandle[4], mean = new TextureHandle[4];
            internal Vector4 size, group, grid, count;
            internal Vector4[] groups = new Vector4[4], grids = new Vector4[4];
        }

        readonly List<MaterialPropertyBlock> blocks = new List<MaterialPropertyBlock>();
        int blockNext;
        MaterialPropertyBlock NextBlock()
        {
            if (blockNext >= blocks.Count) blocks.Add(new MaterialPropertyBlock());
            return blocks[blockNext++];
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (!material || groups.Count == 0) return;
            var resources = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            if (resources.isActiveTargetBackBuffer) return;
            TextureHandle source = resources.cameraColor, depth = resources.cameraDepthTexture;
            if (!source.IsValid() || !depth.IsValid()) return;
            blockNext = 0;
            int w = Mathf.Max(1, cameraData.cameraTargetDescriptor.width), h = Mathf.Max(1, cameraData.cameraTargetDescriptor.height);
            int qw = (w + 3) / 4, qh = (h + 3) / 4;
            var size = new Vector4(w, h, 1f / w, 1f / h);
            // the camera's own matrices, as its opaque pass drew the members (TAA jitter included, the render-target flip)
            view = cameraData.GetViewMatrix();
            viewProj = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), true) * view;
            var depthBuffer = resources.cameraDepth;
            bool byBuffer = false;
            if (depthBuffer.IsValid())
            {
                var dd = renderGraph.GetTextureDesc(depthBuffer);
                byBuffer = dd.width == w && dd.height == h && (int)dd.msaaSamples <= 1;
            }
            LastMaskByDepthBuffer = byBuffer;

            // mask
            var mask = renderGraph.CreateTexture(Desc(w, h, GraphicsFormat.R8G8B8A8_UNorm, "_HS2MosaicWideMask", true));
            using (var builder = renderGraph.AddRasterRenderPass<MaskData>("HS2 Mosaic Wide Mask", out var data, profilingSampler))
            {
                data.material = material;
                data.depth = depth;
                if (byBuffer) builder.SetRenderAttachmentDepth(depthBuffer, AccessFlags.Read);
                else builder.UseTexture(depth, AccessFlags.Read);
                data.byBuffer = byBuffer;
                data.viewProj = viewProj; data.view = view; data.size = size; data.depthParams = depthParams;
                data.draws.Clear();
                foreach (var g in groups)
                    foreach (var r in g.Renderers)
                    {
                        if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                        int subs = SubMeshes(r);
                        for (int s = 0; s < subs; s++) data.draws.Add((r, s, (byBuffer ? PassMaskDepth : PassMask) + g.Channel));
                    }
                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc(static (MaskData d, RasterGraphContext ctx) =>
                {
                    var m = d.material;
                    m.SetMatrix(ViewProjId, d.viewProj);
                    m.SetMatrix(ViewId, d.view);
                    if (!d.byBuffer) m.SetTexture(DepthId, (Texture)d.depth);
                    m.SetVector(SizeId, d.size);
                    m.SetVector(DepthParamsId, d.depthParams);
                    foreach (var (r, sub, pass) in d.draws)
                        if (r) ctx.cmd.DrawRenderer(r, m, sub, pass);
                });
            }

            // quarter
            var quarter = renderGraph.CreateTexture(Desc(qw, qh, GraphicsFormat.R8G8B8A8_UNorm, "_HS2MosaicWideQuarter", false));
            Draw(renderGraph, "HS2 Mosaic Wide Quarter", PassQuarter, quarter, d => { d.mask = mask; d.size = size; });

            // per group
            var dil = new TextureHandle[4];
            var mean = new TextureHandle[4];
            var grids = new Vector4[4];
            var gvec = new Vector4[4];
            for (int i = 0; i < groups.Count && i < 4; i++)
            {
                var g = groups[i];
                var grid = Grid(g, w, h);
                grids[i] = grid;
                gvec[i] = new Vector4(g.Block, g.Anchor.x, g.Anchor.y, g.Channel);
                int nx = (int)grid.z, ny = (int)grid.w;
                var occ = renderGraph.CreateTexture(Desc(nx, ny, GraphicsFormat.R8_UNorm, "_HS2MosaicWideOcc" + i, false));
                var gv = gvec[i];
                Draw(renderGraph, "HS2 Mosaic Wide Occupancy", PassOccupancy, occ, d => { d.quarter = quarter; d.size = size; d.group = gv; d.grid = grid; });
                dil[i] = renderGraph.CreateTexture(Desc(nx, ny, GraphicsFormat.R8_UNorm, "_HS2MosaicWideDil" + i, false));
                Draw(renderGraph, "HS2 Mosaic Wide Dilate", PassDilate, dil[i], d => { d.occ = occ; d.size = size; d.group = gv; d.grid = grid; });
                mean[i] = renderGraph.CreateTexture(Desc(nx, ny, GraphicsFormat.R16G16B16A16_SFloat, "_HS2MosaicWideMean" + i, false));
                Draw(renderGraph, "HS2 Mosaic Wide Mean", PassMean, mean[i], d => { d.source = source; d.size = size; d.group = gv; d.grid = grid; });
            }

            // composite
            var colorDesc = renderGraph.GetTextureDesc(source);
            colorDesc.name = "_HS2MosaicWideResult";
            colorDesc.clearBuffer = false;
            var destination = renderGraph.CreateTexture(colorDesc);
            int count = Mathf.Min(groups.Count, 4);
            Draw(renderGraph, debug ? "HS2 Mosaic Wide Debug" : "HS2 Mosaic Wide Composite", PassComposite, destination, d =>
            {
                d.source = source; d.size = size;
                d.count = new Vector4(count, debug ? 1f : 0f, 0f, 0f);
                for (int i = 0; i < 4; i++)
                {
                    d.dil[i] = i < count ? dil[i] : dil[0];
                    d.mean[i] = i < count ? mean[i] : mean[0];
                    d.groups[i] = gvec[i]; d.grids[i] = grids[i];
                }
            });
            resources.cameraColor = destination;
        }

        static int SubMeshes(Renderer r)
        {
            Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : r.TryGetComponent<MeshFilter>(out var f) ? f.sharedMesh : null;
            return m ? Mathf.Max(1, m.subMeshCount) : 1;
        }

        static TextureDesc Desc(int w, int h, GraphicsFormat format, string name, bool clear) => new TextureDesc(Mathf.Max(1, w), Mathf.Max(1, h))
        {
            format = format,
            name = name,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            clearBuffer = clear,
            clearColor = Color.clear,
        };

        void Draw(RenderGraph rg, string name, int pass, TextureHandle target, System.Action<DrawData> fill)
        {
            using (var builder = rg.AddRasterRenderPass<DrawData>(name, out var data, profilingSampler))
            {
                data.material = material;
                data.pass = pass;
                data.block = NextBlock();
                data.source = TextureHandle.nullHandle; data.mask = TextureHandle.nullHandle; data.quarter = TextureHandle.nullHandle; data.occ = TextureHandle.nullHandle;
                for (int i = 0; i < 4; i++) { data.dil[i] = TextureHandle.nullHandle; data.mean[i] = TextureHandle.nullHandle; }
                data.count = Vector4.zero;
                fill(data);
                void Use(TextureHandle t) { if (t.IsValid()) builder.UseTexture(t, AccessFlags.Read); }
                Use(data.source); Use(data.mask); Use(data.quarter); Use(data.occ);
                var used = new HashSet<TextureHandle>();
                for (int i = 0; i < 4; i++)
                {
                    if (data.dil[i].IsValid() && used.Add(data.dil[i])) Use(data.dil[i]);
                    if (data.mean[i].IsValid() && used.Add(data.mean[i])) Use(data.mean[i]);
                }
                builder.SetRenderAttachment(target, 0, AccessFlags.Write);
                builder.SetRenderFunc(static (DrawData d, RasterGraphContext ctx) =>
                {
                    var b = d.block;
                    b.Clear();
                    b.SetVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                    b.SetVector(SizeId, d.size);
                    b.SetVector(GroupId, d.group);
                    b.SetVector(GridId, d.grid);
                    b.SetFloat(CountId, d.count.x);
                    b.SetFloat(DebugId, d.count.y);
                    if (d.source.IsValid()) b.SetTexture(BlitTextureId, (Texture)d.source);
                    if (d.mask.IsValid()) b.SetTexture(MaskId, (Texture)d.mask);
                    if (d.quarter.IsValid()) b.SetTexture(QuarterId, (Texture)d.quarter);
                    if (d.occ.IsValid()) b.SetTexture(OccId, (Texture)d.occ);
                    for (int i = 0; i < 4; i++)
                    {
                        if (d.dil[i].IsValid()) b.SetTexture(DilIds[i], (Texture)d.dil[i]);
                        if (d.mean[i].IsValid()) b.SetTexture(MeanIds[i], (Texture)d.mean[i]);
                        b.SetVector(GroupIds[i], d.groups[i]);
                        b.SetVector(GridIds[i], d.grids[i]);
                    }
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, d.material, d.pass, MeshTopology.Triangles, 3, 1, b);
                });
            }
        }
    }
}
