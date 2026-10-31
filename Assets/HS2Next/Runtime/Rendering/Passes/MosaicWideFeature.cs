// The wide screen-space mosaic, method (b) (package R of 5.M): a URP renderer feature in
// the production renderer (HS2NextURP_Renderer, after HS2DofFeature) that pixelates the screen inside a dilated mask of the
// visible mosaic-set members, so the outline around the parts is covered too. It only adds coverage (method (a) is always
// drawn); the option render.mosaicWide (default on) and app.options {render: {mosaicWide}} switch it; off = the pass is not
// enqueued and the render is byte-identical to one without the feature.
// Cameras: Game cameras (the view, captures, thumbnails) that render at the top level; not preview or reflection-probe
// cameras, not cameras rendered inside another camera's render (map mirrors: MosaicCamera's frame is nested), not depth-only
// renders. Order: AfterRenderingPostProcessing, enqueued after HS2DofFeature (its feature comes first in the renderer's list),
// so DOF, bloom, grading and SMAA / TAA are in the image; the UI draws outside the camera.
// Groups: one per character (and per standalone member) with drawn members; per group
//   B = max(4 px, ceil(L / 100), round(D / 12)) output pixels (x the capture's downscale k in render pixels), L the long side
//   of the output, D the projected diameter of a sphere around the group's members (closer = larger blocks);
//   the grid anchored at the projected centre of that sphere (no crawl when the character or the camera moves).
// At most four groups get their own channel; further groups (crowded views) join the group with the largest block (their
// coverage is kept, with blocks at least theirs). Overlaps: the larger block wins. MosaicWidePass draws it.
using System.Collections.Generic;
using HS2Next.Chara.Mosaic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HS2Next
{
    /// <summary>The wide mosaic's state (the option render.mosaicWide applies here).</summary>
    public static class MosaicWide
    {
        /// <summary>The option render.mosaicWide (default true).</summary>
        public const string OptionKey = "render.mosaicWide";
        /// <summary>On by default (a decision).</summary>
        public static bool Enabled = true;
#if UNITY_EDITOR
        /// <summary>Editor tests only (not in a build): the composite writes the coverage instead of the mosaic; covered
        /// pixels stay covered (a flat colour), so no coverage is lost.</summary>
        public static bool DebugView;
#endif
    }

    /// <summary>One group as the pass draws it.</summary>
    public struct MosaicWideGroup
    {
        /// <summary>Block in render pixels and the grid anchor (render-target pixels, x right, y from the bottom).</summary>
        public float Block;
        public Vector2 Anchor;
        /// <summary>The projected diameter (render pixels) the block came from.</summary>
        public float Diameter;
        public int Channel;
        public List<Renderer> Renderers;
    }

    public sealed class MosaicWideFeature : ScriptableRendererFeature
    {
        public const string ShaderName = "HS2Next/Hidden/HS2MosaicWide";
        public const int MaxGroups = 4;

        [SerializeField] Shader shader;

        Material material;
        MosaicWidePass pass;

        public Shader Shader { get => shader; set => shader = value; }
        /// <summary>The pass (tests read its last groups).</summary>
        public MosaicWidePass Pass => pass;
        /// <summary>Frames in which the pass was enqueued (tests).</summary>
        public static int EnqueuedCount { get; private set; }
        /// <summary>The groups of the last enqueued frame (tests, chara.mosaic).</summary>
        public static readonly List<MosaicWideGroup> LastGroups = new List<MosaicWideGroup>();

        public override void Create()
        {
            if (!shader) shader = Shader.Find(ShaderName);
            if (material) CoreUtils.Destroy(material);
            material = shader ? CoreUtils.CreateEngineMaterial(shader) : null;
            pass = new MosaicWidePass(material);
        }

        /// <summary>B in output pixels: max(4, ceil(L / 100), round(D / 12)).</summary>
        public static float BlockPx(int longSideOutput, float diameterOutput) =>
            Mathf.Max(4f, Mathf.Max(Mathf.Ceil(longSideOutput / 100f), Mathf.Round(diameterOutput / 12f)));

        /// <summary>The groups of a camera frame for a render target of <paramref name="width"/> x <paramref name="height"/>
        /// pixels (the camera's pixel size scaled by the render scale), sorted by block (largest first), at most MaxGroups.</summary>
        public static List<MosaicWideGroup> Groups(MosaicCameraFrame f, Camera cam, int width, int height)
        {
            var l = new List<MosaicWideGroup>();
            if (f == null || !cam || f.Groups.Count == 0) return l;
            int k = Mathf.Max(1, f.OutputScale);
            // output pixels -> render-target pixels: the capture's downscale k and the render scale
            float toRender = k * (width / (float)Mathf.Max(1, f.Width));
            int longOut = Mathf.CeilToInt(Mathf.Max(f.Width, f.Height) / (float)k);
            var view = cam.worldToCameraMatrix;
            var proj = cam.projectionMatrix;
            bool ortho = cam.orthographic;
            foreach (var g in f.Groups)
            {
                var vp = view.MultiplyPoint(g.Centre);
                float z = Mathf.Max(-vp.z, cam.nearClipPlane);
                var clip = proj * new Vector4(vp.x, vp.y, vp.z, 1f);
                if (!ortho && clip.w <= 1e-6f) continue;
                float w = ortho ? 1f : clip.w;
                var anchor = new Vector2((clip.x / w * 0.5f + 0.5f) * width, (clip.y / w * 0.5f + 0.5f) * height);
                float diameter = 2f * g.Radius * MosaicCamera.PixelsPerUnit(proj, ortho, height, z);
                float block = toRender * BlockPx(longOut, diameter / toRender);
                var rs = new List<Renderer>();
                foreach (var m in g.Members) if (m.Renderer) rs.Add(m.Renderer);
                l.Add(new MosaicWideGroup { Block = block, Anchor = anchor, Diameter = diameter, Renderers = rs });
            }
            l.Sort((a, b) => b.Block.CompareTo(a.Block));
            while (l.Count > MaxGroups)
            {
                var extra = l[l.Count - 1];
                l.RemoveAt(l.Count - 1);
                l[0].Renderers.AddRange(extra.Renderers);
            }
            for (int i = 0; i < l.Count; i++) { var g = l[i]; g.Channel = i; l[i] = g; }
            return l;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!MosaicWide.Enabled || pass == null || !material) return;
            ref var cd = ref renderingData.cameraData;
            if (cd.cameraType != CameraType.Game) return;
            if (UniversalRenderer.IsOffscreenDepthTexture(ref renderingData.cameraData)) return;
            var cam = cd.camera;
            if (!cam) return;
            var frame = MosaicCamera.FrameOf(cam);
            if (frame == null || frame.Nested || frame.Groups.Count == 0) return;
            var groups = Groups(frame, cam, cd.cameraTargetDescriptor.width, cd.cameraTargetDescriptor.height);
            if (groups.Count == 0) return;
#if UNITY_EDITOR
            pass.Setup(groups, cam, MosaicWide.DebugView);
#else
            pass.Setup(groups, cam, false);
#endif
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            pass.requiresIntermediateTexture = true;
            renderer.EnqueuePass(pass);
            LastGroups.Clear();
            LastGroups.AddRange(groups);
            EnqueuedCount++;
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
            material = null;
        }
    }
}
