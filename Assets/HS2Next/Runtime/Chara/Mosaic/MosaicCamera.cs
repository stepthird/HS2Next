// The per-camera block of the mosaic (method (a); package R of
// 5.M). Before every camera that raises RenderPipelineManager.beginCameraRendering renders (the view, captures, thumbnails,
// reflection probes, the Editor's views) every drawn member of the mosaic set gets a MaterialPropertyBlock with this camera's
// block; the materials keep fixed defaults, so a character's material text (S1) does not depend on what was rendered.
//   HS2's cell (behaviour of AIT/mnpb): screen-aligned cells of 1 / d in aspect-corrected NDC, d the distance from the
//     camera to the patch renderer's origin, i.e. H / (2 d) pixels for a view H pixels high, in perspective and orthographic
//     projections alike; on the body that is z tan(fov / 2) / d units (tan(fov / 2) when the origin is at the patch),
//     orthographic orthoSize / d. Which origin HS2 draws a skinned patch with (the root bone or the renderer's transform)
//     is not taken for granted: d is the smallest of the distances to the root bone, to the renderer's transform and to the
//     member's centre, so the cell used here is never smaller than HS2's.
//   Floor: F = k max(4, ceil(L / 100)) render pixels, L the long side of the final output, k its downscale (a capture's
//     supersampling registers k through OutputScope; 1 otherwise).
//   Procedural members: cell = max(F, H / (2 d)) pixels (_MosaicCellPx).
//   One grid per character: the members that draw one source texture share the largest of their blocks, the procedural
//     members the largest of their cells (no seam between overlapping members such as o_mnpa over o_mnpb).
//   Texture members: the block must cover at least that many pixels on the body. Pixels per unit at the member's centre
//     P = H m11 / (2 z) (perspective, m11 = 1 / tan(fov / 2) of the projection, z the view depth) or H m11 / 2
//     (orthographic, m11 = 1 / orthoSize); UV per unit along the member's densest UV direction s = (the mesh's UV per mesh
//     unit) / (the smallest scale of the skinning matrices at the region); a view across the surface needs more:
//     1 / max(|n . v|, 0.5) (n the region's mean normal, v the direction to the camera). The block is the smallest power of
//     two UV b (2^-12 ... 1, a UV-aligned nested grid) with b >= need / P * s * slant (_MosaicBlockUv).
// Nested renders: a camera that raises its events inside another camera's render pushes its frame at begin and pops it at end,
// and the outer camera's blocks are written again. Map mirrors render through RenderSingleCamera, which raises no camera
// event: they draw with the outer camera's blocks and have no frame of their own. Puddle mirrors push and pop their frame
// with Enter and Leave (not nested). Every member also gets its world centre and radius for the wide mosaic
// (MosaicCamera.FrameOf). The global _HS2MosaicFloorPx (the floor in render pixels) serves mosaic-shader renderers no
// record knows. Cost: a few matrix products per drawn member (ZU4, MosaicTest.timing MR6: LastMilliseconds).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS2Next.Chara.Mosaic
{
    /// <summary>One group of drawn members (a character, or one standalone renderer) as a camera sees them.</summary>
    public sealed class MosaicGroup
    {
        public MosaicState State;
        public readonly List<MosaicMember> Members = new List<MosaicMember>();
        /// <summary>World centre and radius of a sphere around the group's members.</summary>
        public Vector3 Centre;
        public float Radius;
    }

    /// <summary>What the camera step computed for one camera render.</summary>
    public sealed class MosaicCameraFrame
    {
        public Camera Camera;
        public int Width, Height, OutputScale = 1;
        /// <summary>F in render pixels.</summary>
        public float FloorPx;
        /// <summary>The camera renders inside another camera's render (a mirror).</summary>
        public bool Nested;
        public readonly List<MosaicGroup> Groups = new List<MosaicGroup>();
        public int DrawnMembers;
    }

    public static class MosaicCamera
    {
        public static readonly int FloorId = Shader.PropertyToID("_HS2MosaicFloorPx");
        static readonly int BlockId = Shader.PropertyToID("_MosaicBlockUv");
        static readonly int CellId = Shader.PropertyToID("_MosaicCellPx");
        public const float MinBlockUv = 1f / 4096f;

        static bool hooked;
        static readonly List<MosaicCameraFrame> stack = new List<MosaicCameraFrame>();
        static readonly Dictionary<Camera, int> outputScale = new Dictionary<Camera, int>();

        /// <summary>Milliseconds of the last camera step, their total and the number of steps since start (ZU4).</summary>
        public static double LastMilliseconds { get; private set; }
        public static double TotalMilliseconds { get; private set; }
        public static int Calls { get; private set; }
        /// <summary>The last camera step's frame (tests).</summary>
        public static MosaicCameraFrame Last { get; private set; }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void EditorHook() => Hook();
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RuntimeHook() => Hook();

        /// <summary>Subscribes to the pipeline's camera callbacks (idempotent).</summary>
        public static void Hook()
        {
            if (hooked) return;
            hooked = true;
            RenderPipelineManager.beginCameraRendering += OnBegin;
            RenderPipelineManager.endCameraRendering += OnEnd;
        }

        /// <summary>
        /// While the scope lives, <paramref name="cam"/> renders a final output <paramref name="k"/> times smaller than its
        /// target (a capture's supersampling): the floor is taken from the output size and scaled by k.
        /// </summary>
        public static IDisposable OutputScope(Camera cam, int k) => new Scope(cam, k);

        sealed class Scope : IDisposable
        {
            readonly Camera cam; readonly bool had; readonly int before;
            public Scope(Camera c, int k)
            {
                cam = c;
                had = c && outputScale.TryGetValue(c, out before);
                if (c) outputScale[c] = Math.Max(1, k);
            }
            public void Dispose()
            {
                if (!cam) return;
                if (had) outputScale[cam] = before; else outputScale.Remove(cam);
            }
        }

        /// <summary>The frame of a camera rendering now (between its begin and end), or null.</summary>
        public static MosaicCameraFrame FrameOf(Camera cam)
        {
            for (int i = stack.Count - 1; i >= 0; i--) if (stack[i].Camera == cam) return stack[i];
            return null;
        }

        static void OnBegin(ScriptableRenderContext ctx, Camera cam)
        {
            var f = Apply(cam, stack.Count > 0);
            stack.Add(f);
        }

        static void OnEnd(ScriptableRenderContext ctx, Camera cam)
        {
            if (stack.Count == 0) return;
            int i = stack.Count - 1;
            while (i > 0 && stack[i].Camera != cam) i--;
            stack.RemoveAt(i);
            // the outer camera's blocks again (a nested render wrote its own)
            if (stack.Count > 0) Apply(stack[stack.Count - 1].Camera, stack.Count > 1, stack[stack.Count - 1]);
        }

        /// <summary>For a camera rendered with no camera events: what the begin event does (its frame pushed, the blocks applied), the frame not
        /// nested, its final output <paramref name="outputScale"/> times smaller than its target. Every Enter has its <see cref="Leave"/>.</summary>
        public static void Enter(Camera cam, int outputScale)
        {
            using (OutputScope(cam, outputScale)) stack.Add(Apply(cam, false));
        }

        /// <summary>What the end event does for <paramref name="cam"/>: its frame popped, the outer camera's blocks written again.</summary>
        public static void Leave(Camera cam) => OnEnd(default, cam);

        // ---------------------------------------------------------------- the rule

        /// <summary>F = k max(4, ceil(L / 100)) render pixels for a render of <paramref name="width"/> x
        /// <paramref name="height"/> pixels whose output is k times smaller.</summary>
        public static float FloorPx(int width, int height, int k = 1)
        {
            k = Math.Max(1, k);
            int l = Mathf.CeilToInt(Math.Max(width, height) / (float)k);
            return k * Mathf.Max(4f, Mathf.Ceil(l / 100f));
        }

        /// <summary>HS2's cell in pixels for a view <paramref name="heightPx"/> high and the distance d to the origin.</summary>
        public static float Hs2CellPx(float heightPx, float d) => heightPx / (2f * Mathf.Max(d, 1e-3f));

        /// <summary>Pixels per world unit at view depth <paramref name="z"/> (perspective) or anywhere (orthographic).</summary>
        public static float PixelsPerUnit(Matrix4x4 projection, bool orthographic, float heightPx, float z) =>
            heightPx * Mathf.Abs(projection.m11) / (orthographic ? 2f : 2f * Mathf.Max(z, 1e-4f));

        /// <summary>The smallest power-of-two UV block (MinBlockUv ... 1) covering <paramref name="needPx"/> pixels.</summary>
        public static float BlockUv(float needPx, float pxPerUnit, float uvPerUnit, float slant)
        {
            if (!(pxPerUnit > 0f) || !(uvPerUnit > 0f)) return 1f;
            float need = needPx / pxPerUnit * uvPerUnit * Mathf.Max(slant, 1f);
            if (!(need > MinBlockUv)) return MinBlockUv;
            if (need >= 1f) return 1f;
            return Mathf.Pow(2f, Mathf.Ceil(Mathf.Log(need, 2f) - 1e-6f));
        }

        // ---------------------------------------------------------------- per camera

        /// <summary>Writes this camera's blocks / cells into the drawn members' property blocks and returns the frame.</summary>
        public static MosaicCameraFrame Apply(Camera cam, bool nested, MosaicCameraFrame reuse = null)
        {
            var sw = Stopwatch.StartNew();
            var f = reuse ?? new MosaicCameraFrame();
            f.Camera = cam;
            f.Nested = nested;
            f.Groups.Clear();
            f.DrawnMembers = 0;
            if (!cam) return f;
            f.Width = Math.Max(1, cam.pixelWidth);
            f.Height = Math.Max(1, cam.pixelHeight);
            f.OutputScale = outputScale.TryGetValue(cam, out int k) ? k : 1;
            f.FloorPx = FloorPx(f.Width, f.Height, f.OutputScale);
            Shader.SetGlobalFloat(FloorId, f.FloorPx);
            var camPos = cam.transform.position;
            var fwd = cam.transform.forward;
            var proj = cam.projectionMatrix;
            bool ortho = cam.orthographic;
            int frame = Time.frameCount;
            foreach (var s in MosaicSet.LiveStates())
            {
                MosaicGroup g = null;
                foreach (var m in s.Members)
                    if (Update(m, cam, camPos, fwd, proj, ortho, f, frame))
                    {
                        if (g == null) f.Groups.Add(g = new MosaicGroup { State = s });
                        g.Members.Add(m);
                    }
                if (g != null) { Unify(g.Members); Bound(g); }
            }
            foreach (var m in MosaicSet.Standalone())
                if (Update(m, cam, camPos, fwd, proj, ortho, f, frame))
                {
                    var g = new MosaicGroup();
                    g.Members.Add(m);
                    Unify(g.Members);
                    Bound(g);
                    f.Groups.Add(g);
                }
            LastMilliseconds = sw.Elapsed.TotalMilliseconds;
            TotalMilliseconds += LastMilliseconds;
            Calls++;
            Last = f;
            return f;
        }

        static void Bound(MosaicGroup g)
        {
            var b = new Bounds(g.Members[0].WorldCentre, Vector3.zero);
            foreach (var m in g.Members) b.Encapsulate(new Bounds(m.WorldCentre, Vector3.one * (2f * m.WorldRadius)));
            g.Centre = b.center;
            g.Radius = b.extents.magnitude;
        }

        /// <summary>The member's skinning matrix at its region (the weighted blend of its region bones' matrices), else the
        /// renderer's own matrix.</summary>
        public static Matrix4x4 RegionMatrix(MosaicMember m)
        {
            var r = m.Renderer;
            if (m.RegionBones.Length == 0) return r.localToWorldMatrix;
            var acc = new Matrix4x4();
            bool any = false;
            for (int i = 0; i < m.RegionBones.Length; i++)
            {
                var b = m.RegionBones[i];
                if (!b) continue;
                var mm = b.localToWorldMatrix * m.RegionBind[i];
                float w = m.RegionWeights[i];
                for (int e = 0; e < 16; e++) acc[e] += mm[e] * w;
                any = true;
            }
            return any ? acc : r.localToWorldMatrix;
        }

        /// <summary>
        /// One grid per character: members that draw the same source texture take the largest of their blocks (they lie on
        /// one UV layout, so their blocks then coincide where they overlap) and every procedural member the largest cell
        /// (one screen grid); then the property blocks are written.
        /// </summary>
        static void Unify(List<MosaicMember> members)
        {
            float cell = 0f;
            foreach (var m in members) if (m.Mode == MosaicMode.Procedural) cell = Mathf.Max(cell, m.CellPx);
            foreach (var m in members)
            {
                if (m.Mode == MosaicMode.Procedural) { m.CellPx = cell; m.BlockPx = cell; }
                else
                {
                    float b = m.BlockUv;
                    foreach (var o in members) if (o.Mode == MosaicMode.Texture && o.Source == m.Source) b = Mathf.Max(b, o.BlockUv);
                    m.SharedBlockUv = b;
                }
            }
            foreach (var m in members)
            {
                if (m.Mode == MosaicMode.Texture) { m.BlockUv = m.SharedBlockUv; m.BlockPx = m.BlockUv * m.PxPerUv; }
                m.Block.SetFloat(BlockId, m.BlockUv > 0f ? m.BlockUv : MosaicUnit.DefaultBlockUv);
                m.Block.SetFloat(CellId, m.CellPx);
                m.Renderer.SetPropertyBlock(m.Block);
            }
        }

        static bool Update(MosaicMember m, Camera cam, Vector3 camPos, Vector3 fwd, Matrix4x4 proj, bool ortho, MosaicCameraFrame f, int frame)
        {
            var r = m.Renderer;
            m.Drawn = false;
            if (!r || !r.enabled || !r.gameObject.activeInHierarchy || (cam.cullingMask & (1 << r.gameObject.layer)) == 0) return false;
            var reg = m.Region ?? new MosaicRegion();
            var M = RegionMatrix(m);
            var centre = M.MultiplyPoint3x4(reg.Centroid);
            float sx = ((Vector3)M.GetColumn(0)).magnitude, sy = ((Vector3)M.GetColumn(1)).magnitude, sz = ((Vector3)M.GetColumn(2)).magnitude;
            float sMax = Mathf.Max(sx, Mathf.Max(sy, sz));
            // the smallest stretch along the surface (two directions across the region's mean normal; all three axes
            // without a normal): world UV density = mesh UV density / this
            float sMin;
            if (reg.Normal != Vector3.zero)
            {
                var t1 = Vector3.Cross(reg.Normal, Mathf.Abs(reg.Normal.x) < 0.9f ? Vector3.right : Vector3.up).normalized;
                var t2 = Vector3.Cross(reg.Normal, t1);
                sMin = Mathf.Min(M.MultiplyVector(t1).magnitude, M.MultiplyVector(t2).magnitude);
            }
            else sMin = Mathf.Min(sx, Mathf.Min(sy, sz));
            sMin = Mathf.Max(sMin, 1e-6f);
            m.WorldCentre = centre;
            m.WorldRadius = reg.Radius * sMax;
            // HS2's distance: the smallest of the candidate origins (never a smaller cell than HS2's)
            float d = (camPos - centre).magnitude;
            d = Mathf.Min(d, (camPos - r.transform.position).magnitude);
            if (r is SkinnedMeshRenderer smr && smr.rootBone) d = Mathf.Min(d, (camPos - smr.rootBone.position).magnitude);
            float cellPx = Mathf.Max(f.FloorPx, Hs2CellPx(f.Height, d));
            float z = Mathf.Max(Vector3.Dot(centre - camPos, fwd), cam.nearClipPlane);
            float ppu = PixelsPerUnit(proj, ortho, f.Height, z);
            m.Block ??= new MaterialPropertyBlock();
            if (m.Mode == MosaicMode.Texture)
            {
                float uvPerUnit = reg.UvPerUnit / sMin;
                float slant = 1f;
                if (reg.Normal != Vector3.zero)
                {
                    var n = M.MultiplyVector(reg.Normal).normalized;
                    var v = ortho ? -fwd : (camPos - centre).normalized;
                    slant = 1f / Mathf.Max(Mathf.Abs(Vector3.Dot(n, v)), 0.5f);
                }
                m.BlockUv = BlockUv(cellPx, ppu, uvPerUnit, slant);
                m.BlockPx = uvPerUnit > 0 ? m.BlockUv / uvPerUnit * ppu : 0f;
                m.PxPerUv = uvPerUnit > 0 ? ppu / uvPerUnit : 0f;
            }
            else
            {
                m.BlockUv = 0f;
                m.BlockPx = cellPx;
            }
            m.CellPx = cellPx;
            m.LastCamera = cam.name;
            m.Drawn = true;
            m.Frame = frame;
            f.DrawnMembers++;
            return true;
        }
    }
}
