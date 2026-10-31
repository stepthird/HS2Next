// The gizmo pass (spike S5): a URP RenderGraph raster pass at AfterRenderingPostProcessing
// (after DOF, bloom, tonemapping) on the camera's active colour target, enqueued from beginCameraRendering for the view
// camera only (camera reference check), so captures never contain gizmos. Handles draw with ZTest Always, helper lines
// (FK bone lines, light helpers, camera icons) depth-tested against the scene. SetViewProjectionMatrices takes the
// non-GPU, non-jittered Camera.projectionMatrix (it flips for the target itself); colours are passed linear (the tokens
// are sRGB). Meshes are generated in code (GizmoMeshes); one unlit shader HS2Next/UI/Gizmo (_Color, _ZTest) through the
// material Resources/UI/HS2NextGizmo (Editor/UiFontAssets.Build makes it, so the player ships the shader).
// Installed by UiHost (the spec's "GizmoFeature line in AppPipeline" is this subscription instead of a renderer-asset
// feature; same pass, same event, view camera only).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace HS2Next.UI.Gizmos
{
    public struct GizmoDraw
    {
        public Mesh Mesh;
        public Matrix4x4 Matrix;
        /// <summary>sRGB colour (converted to linear when recorded).</summary>
        public Color Color;
        /// <summary>Helper (lines, icons) rather than a handle; both draw on top in 3.5 (the pass has no depth attachment
        /// after post-processing, whose colour target may differ in size from the camera depth).</summary>
        public bool DepthTest;
    }

    public sealed class GizmoPass : ScriptableRenderPass
    {
        sealed class PassData
        {
            public GizmoDraw[] draws;
            public int count;
            public Material always, depth;
            public MaterialPropertyBlock props;
            public Matrix4x4 view, proj;
            public bool setViewport;
            public Rect viewport;
        }

        static readonly int ColorId = Shader.PropertyToID("_Color");
        public Material Always, Depth;
        public GizmoDraw[] Draws = new GizmoDraw[256];
        public int Count;
        public int Recorded { get; private set; }
        readonly MaterialPropertyBlock props = new MaterialPropertyBlock();

        public GizmoPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            profilingSampler = new ProfilingSampler("HS2Next Gizmos");
        }

        public void Clear() => Count = 0;

        public void Add(in GizmoDraw d)
        {
            if (d.Mesh == null) return;
            if (Count == Draws.Length) Array.Resize(ref Draws, Draws.Length * 2);
            Draws[Count++] = d;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (Count == 0 || !Always) return;
            var res = frameData.Get<UniversalResourceData>();
            var cam = frameData.Get<UniversalCameraData>().camera;
            Recorded++;
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("HS2Next Gizmos", out var d, profilingSampler))
            {
                builder.SetRenderAttachment(res.activeColorTexture, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);
                d.draws = Draws;
                d.count = Count;
                d.always = Always;
                d.depth = Depth ? Depth : Always;
                d.props = props;
                d.view = cam.worldToCameraMatrix;
                d.proj = cam.projectionMatrix;   // non-GPU, non-jittered (spike S5)
                d.setViewport = res.isActiveTargetBackBuffer;
                d.viewport = cam.pixelRect;
                builder.SetRenderFunc((PassData p, RasterGraphContext ctx) =>
                {
                    if (p.setViewport) ctx.cmd.SetViewport(p.viewport);
                    ctx.cmd.SetViewProjectionMatrices(p.view, p.proj);
                    for (int i = 0; i < p.count; i++)
                    {
                        ref var g = ref p.draws[i];
                        p.props.SetColor(ColorId, g.Color.linear);
                        ctx.cmd.DrawMesh(g.Mesh, g.Matrix, g.DepthTest ? p.depth : p.always, 0, 0, p.props);
                    }
                });
            }
        }
    }

    /// <summary>Unit meshes of the handles (built once).</summary>
    public static class GizmoMeshes
    {
        static Mesh shaft, cone, cube, quad, ring, disc, octa, sphere, frustum;

        /// <summary>A thin cylinder along +Y from 0 to 1, radius 1 (scale x / z for the thickness).</summary>
        public static Mesh Shaft => shaft ??= Cylinder(12);
        /// <summary>A cone along +Y, base radius 1 at 0, tip at 1.</summary>
        public static Mesh Cone => cone ??= MakeCone(16);
        public static Mesh Cube => cube ??= MakeCube();
        /// <summary>A unit quad in the XY plane centred on 0 (both faces).</summary>
        public static Mesh Quad => quad ??= MakeQuad();
        /// <summary>A torus in the XZ plane: radius 1, tube radius 0.015.</summary>
        public static Mesh Ring => ring ??= Torus(96, 8, 0.015f);
        /// <summary>A disc in the XY plane, radius 1.</summary>
        public static Mesh Disc => disc ??= MakeDisc(32);
        public static Mesh Octahedron => octa ??= MakeOcta();
        public static Mesh Sphere => sphere ??= MakeSphere(12, 8);
        /// <summary>A wire camera glyph (lines): frustum pointing +Z.</summary>
        public static Mesh Frustum => frustum ??= MakeFrustum();

        static Mesh Named(string n, Mesh m) { m.name = "gizmo " + n; m.hideFlags = HideFlags.DontSave; return m; }

        static Mesh Cylinder(int n)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= n; i++)
            {
                float a = i * Mathf.PI * 2 / n;
                var c = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(c); v.Add(c + Vector3.up);
            }
            for (int i = 0; i < n; i++) { int a = i * 2; t.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 }); }
            var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateBounds();
            return Named("shaft", m);
        }

        static Mesh MakeCone(int n)
        {
            var v = new List<Vector3> { Vector3.up, Vector3.zero }; var t = new List<int>();
            for (int i = 0; i <= n; i++) { float a = i * Mathf.PI * 2 / n; v.Add(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a))); }
            for (int i = 0; i < n; i++) { t.AddRange(new[] { 0, i + 2, i + 3 }); t.AddRange(new[] { 1, i + 3, i + 2 }); }
            var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateBounds();
            return Named("cone", m);
        }

        static Mesh MakeCube()
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var m = UnityEngine.Object.Instantiate(g.GetComponent<MeshFilter>().sharedMesh);
            UnityEngine.Object.Destroy(g);
            return Named("cube", m);
        }

        static Mesh MakeSphere(int lon, int lat)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int y = 0; y <= lat; y++)
                for (int x = 0; x <= lon; x++)
                {
                    float th = y * Mathf.PI / lat, ph = x * Mathf.PI * 2 / lon;
                    v.Add(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)));
                }
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int a = y * (lon + 1) + x, b = a + lon + 1;
                    t.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateBounds();
            return Named("sphere", m);
        }

        static Mesh MakeQuad()
        {
            var m = new Mesh();
            m.SetVertices(new List<Vector3> { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(.5f, .5f), new Vector3(-.5f, .5f) });
            m.SetTriangles(new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 }, 0);
            m.RecalculateBounds();
            return Named("quad", m);
        }

        static Mesh MakeDisc(int n)
        {
            var v = new List<Vector3> { Vector3.zero }; var t = new List<int>();
            for (int i = 0; i <= n; i++) { float a = i * Mathf.PI * 2 / n; v.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0)); }
            for (int i = 0; i < n; i++) { t.AddRange(new[] { 0, i + 1, i + 2, 0, i + 2, i + 1 }); }
            var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateBounds();
            return Named("disc", m);
        }

        static Mesh MakeOcta()
        {
            var v = new List<Vector3> { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            var t = new List<int> { 0, 4, 3, 0, 3, 5, 0, 5, 2, 0, 2, 4, 1, 3, 4, 1, 5, 3, 1, 2, 5, 1, 4, 2 };
            var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateBounds();
            return Named("octa", m);
        }

        static Mesh Torus(int n, int k, float r)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= n; i++)
            {
                float a = i * Mathf.PI * 2 / n;
                var c = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                for (int j = 0; j <= k; j++)
                {
                    float b = j * Mathf.PI * 2 / k;
                    v.Add(c * (1 + r * Mathf.Cos(b)) + Vector3.up * (r * Mathf.Sin(b)));
                }
            }
            for (int i = 0; i < n; i++)
                for (int j = 0; j < k; j++)
                {
                    int a = i * (k + 1) + j, b = a + k + 1;
                    t.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateBounds();
            return Named("ring", m);
        }

        static Mesh MakeFrustum()
        {
            var v = new List<Vector3> { Vector3.zero, new Vector3(-.6f, -.4f, 1), new Vector3(.6f, -.4f, 1), new Vector3(.6f, .4f, 1), new Vector3(-.6f, .4f, 1), new Vector3(0, .6f, 1) };
            var idx = new[] { 0, 1, 0, 2, 0, 3, 0, 4, 1, 2, 2, 3, 3, 4, 4, 1, 4, 5, 5, 3 };
            var m = new Mesh(); m.SetVertices(v); m.SetIndices(idx, MeshTopology.Lines, 0); m.RecalculateBounds();
            return Named("frustum", m);
        }

        /// <summary>A line mesh from point pairs (reused: pass the previous one to refill it).</summary>
        public static Mesh Lines(Mesh reuse, IReadOnlyList<Vector3> pairs)
        {
            var m = reuse ? reuse : Named("lines", new Mesh());
            m.Clear();
            if (pairs.Count < 2) return m;
            var idx = new int[pairs.Count - pairs.Count % 2];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            m.SetVertices(new List<Vector3>(pairs));
            m.SetIndices(idx, MeshTopology.Lines, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
