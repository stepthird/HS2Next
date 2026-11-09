// The Cutter's markers: the app's generated rows hs2next.cut#1 plane, #2 box, #3 tear,
// #4 hole of group 731406, category 1. A marker is an item placed, turned and scaled with the gizmo like any item; its object is
// made here: a translucent face and its outline in the vertex-coloured particle shader HS2Next/Legacy Shaders/Particles/Alpha
// Blended (plane: a 10 × 10 sheet with its edges and an arrow up; box: the faces and edges of a 4-unit cube; tear and hole: the
// outline, the face and the depth bar), on the helper layer (the view draws it, captures, mirrors and thumbnails leave it out),
// casting and taking no shadow. Captures that do not use the view's setup run inside HideForCapture(), which switches every
// marker renderer off and puts it back (nests). Markers stay in the scene after a cut and are saved as items.
// A marker's shape in a source's root space: the marker's world matrix brought into that space (position, rotation, and its
// scale times the base size as the shape's size), stored as the floats the file holds. A piece is placed at the source's root
// times the piece's root offset.
using System;
using System.Collections.Generic;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes.Bake;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS2Next.Scenes.Cut
{
    public static class CutMarkers
    {
        public const string ShaderName = "HS2Next/Legacy Shaders/Particles/Alpha Blended";
        static readonly HashSet<Renderer> alive = new HashSet<Renderer>();
        static readonly Dictionary<CutKind, Mesh[]> meshes = new Dictionary<CutKind, Mesh[]>();
        static Material material;
        static int hiddenDepth;
        static readonly List<(Renderer r, bool was)> saved = new List<(Renderer, bool)>();

        /// <summary>Makes the app's marker rows build their objects here, and a piece whose stored result is missing run its procedure
        /// again: at the app's start, and with the bake source's registration; idempotent (test hosts call it themselves).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Install()
        {
            AppItemRows.MarkerObject = MakeFor;
            BakeProvider.Recompute = CutPieces.Recompute;
        }

        /// <summary>The object of a marker row (null for an id that is no marker: the item falls back).</summary>
        public static GameObject MakeFor(GeneratedRowRequest req)
        {
            var kind = req != null ? (CutKind)req.Id : CutKind.None;
            return kind >= CutKind.Plane && kind <= CutKind.Hole ? Make(kind) : null;
        }

        /// <summary>The marker kind of an item node (CutKind.None when the item is no marker).</summary>
        public static CutKind KindOf(ItemNodeState s)
        {
            if (s == null || s.Group != AppItemRows.Group || s.Category != AppItemRows.MarkersCategory || s.Ref == null) return CutKind.None;
            string want = AppItemRows.CutGuid + "#";
            if (!s.Ref.StartsWith(want, StringComparison.OrdinalIgnoreCase)) return CutKind.None;
            return int.TryParse(s.Ref.Substring(want.Length), out int id) && id >= 1 && id <= 4 ? (CutKind)id : CutKind.None;
        }

        public static bool IsMarker(ItemNodeState s) => KindOf(s) != CutKind.None;

        /// <summary>Marker renderers alive (all runtimes).</summary>
        public static IReadOnlyCollection<Renderer> Renderers
        {
            get { Prune(); return new List<Renderer>(alive); }
        }

        /// <summary>A new marker object of a kind: the face (triangles) on the object, the outline (lines) on its child "outline".</summary>
        public static GameObject Make(CutKind kind)
        {
            var go = new GameObject("cut marker " + CutShapeInfo.Name(kind)) { layer = ViewSetup.HelperLayer };
            var mm = MeshesOf(kind);
            var mat = Material();
            var face = Attach(go, mm[0], mat);
            var outline = new GameObject("outline") { layer = ViewSetup.HelperLayer };
            outline.transform.SetParent(go.transform, false);
            var line = Attach(outline, mm[1], mat);
            Prune();
            alive.Add(face);
            alive.Add(line);
            if (hiddenDepth > 0) { saved.Add((face, face.forceRenderingOff)); saved.Add((line, line.forceRenderingOff)); face.forceRenderingOff = line.forceRenderingOff = true; }
            return go;
        }

        static Renderer Attach(GameObject go, Mesh mesh, Material mat)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }

        static Material Material()
        {
            if (material) return material;
            var sh = Shader.Find(ShaderName) ?? Shader.Find("HS2Next/Unlit/Color");
            material = new Material(sh) { name = "cut marker", hideFlags = HideFlags.HideAndDontSave };
            if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            return material;
        }

        static void Prune() => alive.RemoveWhere(r => !r);

        /// <summary>Switches every marker renderer off until disposed (captures that do not use the view's setup); nests.</summary>
        public static IDisposable HideForCapture() => new Hidden();

        sealed class Hidden : IDisposable
        {
            bool done;

            public Hidden()
            {
                if (hiddenDepth++ > 0) return;
                Prune();
                saved.Clear();
                foreach (var r in alive)
                {
                    saved.Add((r, r.forceRenderingOff));
                    r.forceRenderingOff = true;
                }
            }

            public void Dispose()
            {
                if (done) return;
                done = true;
                if (--hiddenDepth > 0) return;
                foreach (var (r, was) in saved) if (r) r.forceRenderingOff = was;
                saved.Clear();
            }
        }

        // ------------------------------------------------------------------ the shapes' meshes

        static readonly Color32 PlaneColor = new Color32(80, 210, 255, 255), BoxColor = new Color32(255, 150, 40, 255),
            TearColor = new Color32(255, 70, 70, 255), HoleColor = new Color32(255, 225, 60, 255);
        const byte FaceAlpha = 40, LineAlpha = 230;

        static Mesh[] MeshesOf(CutKind kind)
        {
            if (meshes.TryGetValue(kind, out var m) && m[0] && m[1]) return m;
            var face = new MeshBits(); var line = new MeshBits();
            var size = CutShapeInfo.BaseSize(kind);
            float hx = (float)size.X / 2, hy = (float)size.Y / 2, hz = (float)size.Z / 2;
            switch (kind)
            {
                case CutKind.Plane:
                    {
                        var c = PlaneColor;
                        face.Quad(new Vector3(-hx, 0, -hz), new Vector3(-hx, 0, hz), new Vector3(hx, 0, hz), new Vector3(hx, 0, -hz), Alpha(c, FaceAlpha));
                        var lc = Alpha(c, LineAlpha);
                        line.Loop(lc, new Vector3(-hx, 0, -hz), new Vector3(-hx, 0, hz), new Vector3(hx, 0, hz), new Vector3(hx, 0, -hz));
                        for (int i = -4; i <= 4; i += 2) { line.Line(new Vector3(i, 0, -hz), new Vector3(i, 0, hz), Alpha(c, 90)); line.Line(new Vector3(-hx, 0, i), new Vector3(hx, 0, i), Alpha(c, 90)); }
                        line.Line(Vector3.zero, new Vector3(0, 2, 0), lc);
                        foreach (var d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back }) line.Line(new Vector3(0, 2, 0), new Vector3(0, 1.6f, 0) + d * 0.3f, lc);
                        break;
                    }
                case CutKind.Box:
                    {
                        var c = BoxColor; var fc = Alpha(c, FaceAlpha); var lc = Alpha(c, LineAlpha);
                        Vector3 P(int x, int y, int z) => new Vector3(x * hx, y * hy, z * hz);
                        face.Quad(P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), fc);
                        face.Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), fc);
                        face.Quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), fc);
                        face.Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), fc);
                        face.Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), fc);
                        face.Quad(P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), fc);
                        for (int a = -1; a <= 1; a += 2)
                            for (int b = -1; b <= 1; b += 2)
                            {
                                line.Line(P(-1, a, b), P(1, a, b), lc);
                                line.Line(P(a, -1, b), P(a, 1, b), lc);
                                line.Line(P(a, b, -1), P(a, b, 1), lc);
                            }
                        break;
                    }
                case CutKind.Tear:
                case CutKind.Hole:
                    {
                        var c = kind == CutKind.Tear ? TearColor : HoleColor; var fc = Alpha(c, FaceAlpha); var lc = Alpha(c, LineAlpha);
                        const int n = 48;
                        var ring = new Vector3[n];
                        for (int i = 0; i < n; i++)
                        {
                            if (kind == CutKind.Hole)
                            {
                                double a = 2 * Math.PI * i / n;
                                ring[i] = new Vector3((float)(hx * Math.Cos(a)), 0, (float)(hz * Math.Sin(a)));
                            }
                            else
                            {
                                // the lens: half width hz·(1 − (u / hx)²), from one tip to the other along each side
                                int half = n / 2;
                                double t = i < half ? -1 + 2.0 * i / half : 1 - 2.0 * (i - half) / half;
                                double side = i < half ? 1 : -1;
                                ring[i] = new Vector3((float)(hx * t), 0, (float)(side * hz * (1 - t * t)));
                            }
                        }
                        for (int i = 0; i < n; i++)
                        {
                            face.Tri(Vector3.zero, ring[i], ring[(i + 1) % n], fc);
                            line.Line(ring[i], ring[(i + 1) % n], lc);
                            line.Line(ring[i] + new Vector3(0, hy, 0), ring[(i + 1) % n] + new Vector3(0, hy, 0), Alpha(c, 90));
                            line.Line(ring[i] - new Vector3(0, hy, 0), ring[(i + 1) % n] - new Vector3(0, hy, 0), Alpha(c, 90));
                        }
                        line.Line(new Vector3(0, -hy, 0), new Vector3(0, hy, 0), lc);
                        float tick = 0.05f;
                        line.Line(new Vector3(-tick, hy, 0), new Vector3(tick, hy, 0), lc);
                        line.Line(new Vector3(-tick, -hy, 0), new Vector3(tick, -hy, 0), lc);
                        break;
                    }
            }
            m = new[] { face.Build("cut marker face", MeshTopology.Triangles), line.Build("cut marker outline", MeshTopology.Lines) };
            meshes[kind] = m;
            return m;
        }

        static Color32 Alpha(Color32 c, byte a) => new Color32(c.r, c.g, c.b, a);

        sealed class MeshBits
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<Color32> c = new List<Color32>();
            readonly List<int> ix = new List<int>();

            public void Tri(Vector3 a, Vector3 b, Vector3 d, Color32 col)
            {
                int o = v.Count;
                v.Add(a); v.Add(b); v.Add(d); c.Add(col); c.Add(col); c.Add(col);
                ix.Add(o); ix.Add(o + 1); ix.Add(o + 2);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 d, Vector3 e, Color32 col) { Tri(a, b, d, col); Tri(a, d, e, col); }

            public void Line(Vector3 a, Vector3 b, Color32 col)
            {
                int o = v.Count;
                v.Add(a); v.Add(b); c.Add(col); c.Add(col);
                ix.Add(o); ix.Add(o + 1);
            }

            public void Loop(Color32 col, params Vector3[] p)
            {
                for (int i = 0; i < p.Length; i++) Line(p[i], p[(i + 1) % p.Length], col);
            }

            public Mesh Build(string name, MeshTopology topology)
            {
                var m = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
                m.SetVertices(v);
                m.SetColors(c);
                m.SetIndices(ix, topology, 0, true);
                m.UploadMeshData(false);
                return m;
            }
        }

        // ------------------------------------------------------------------ spaces

        public static CutXf Xf(Matrix4x4 m) => new CutXf(m.m00, m.m01, m.m02, m.m03, m.m10, m.m11, m.m12, m.m13, m.m20, m.m21, m.m22, m.m23);

        public static Matrix4x4 Matrix(CutXf x)
        {
            var m = Matrix4x4.identity;
            m.m00 = (float)x.M00; m.m01 = (float)x.M01; m.m02 = (float)x.M02; m.m03 = (float)x.M03;
            m.m10 = (float)x.M10; m.m11 = (float)x.M11; m.m12 = (float)x.M12; m.m13 = (float)x.M13;
            m.m20 = (float)x.M20; m.m21 = (float)x.M21; m.m22 = (float)x.M22; m.m23 = (float)x.M23;
            return m;
        }

        /// <summary>The step of a marker (its world matrix) in a source's root space (the source root's world matrix), as the
        /// file stores it: position, rotation, the marker's scale in that space times the base size.</summary>
        public static CutStep StepOf(Matrix4x4 markerWorld, Matrix4x4 sourceRootWorld, CutKind kind, string keep, CutRagParams ragged, bool clothesOnly)
        {
            var m = Xf(sourceRootWorld).Inverse() * Xf(markerWorld);
            var scale = m.ColumnScale;
            var b = CutShapeInfo.BaseSize(kind);
            var d = new CutStepDef
            {
                Shape = CutShapeInfo.Name(kind),
                Pos = m.Translation,
                Rot = CutQuat.FromMatrix(m),
                Size = new CutVec(b.X * scale.X, b.Y * scale.Y, b.Z * scale.Z),
                Keep = keep,
                Ragged = ragged != null && ragged.Amp > 0 ? ragged.Clone() : null,
                ClothesOnly = clothesOnly,
            };
            return CutPieces.BlockOf(d);
        }

        /// <summary>The step of a shape given in world values (cut.make's `shape`: kind, pos, rot, size).</summary>
        public static CutStep StepOf(Vector3 pos, Quaternion rot, Vector3 size, Matrix4x4 sourceRootWorld, CutKind kind, string keep, CutRagParams ragged, bool clothesOnly)
        {
            var b = CutShapeInfo.BaseSize(kind);
            var scale = new Vector3(b.X != 0 ? (float)(size.x / b.X) : 1, b.Y != 0 ? (float)(size.y / b.Y) : 1, b.Z != 0 ? (float)(size.z / b.Z) : 1);
            return StepOf(Matrix4x4.TRS(pos, rot, scale), sourceRootWorld, kind, keep, ragged, clothesOnly);
        }

        /// <summary>A piece's world matrix: the source's root times the piece's root offset.</summary>
        public static Matrix4x4 PieceWorld(Matrix4x4 sourceRootWorld, CutPieceResult piece) => Matrix(Xf(sourceRootWorld) * piece.RootOffset);

        /// <summary>A piece's world pose for its node: position and rotation of PieceWorld, the source root's scale.</summary>
        public static void PiecePose(Matrix4x4 sourceRootWorld, CutPieceResult piece, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            var w = Xf(sourceRootWorld) * piece.RootOffset;
            var t = w.Translation; var q = CutQuat.FromMatrix(w); var s = w.ColumnScale;
            position = new Vector3((float)t.X, (float)t.Y, (float)t.Z);
            rotation = new Quaternion((float)q.X, (float)q.Y, (float)q.Z, (float)q.W);
            scale = new Vector3((float)s.X, (float)s.Y, (float)s.Z);
        }
    }
}
