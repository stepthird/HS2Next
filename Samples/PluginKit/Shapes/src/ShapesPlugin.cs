// Sample "shapes" of the HS2Next plugin kit (CC0-1.0: dedicated to the public domain).
// An engine-side plugin that adds rows to the Studio item list: Load adds an item group with one category and five rows
// (Cube, Sphere, Cylinder, Torus, Arrow). The app calls Make whenever an item of one of these rows is built (added, loaded,
// duplicated or built again); Make builds the mesh in code and returns a new object, which the app then owns. The torus is a
// skinned mesh with eight bones along its ring; its row names them as FK bones, so item FK bends it.
// An item of these rows saves as "stp3.sample.shapes#<id>" with the group and category: without the plugin it loads as the
// fallback item and keeps that data; with the plugin it is the shape again.
using System;
using System.Collections.Generic;
using HS2Next.Api.Plugins;
using HS2Next.Api.Unity;
using UnityEngine;

namespace Stp3.Samples.Shapes
{
    public sealed class ShapesPlugin : IPlugin
    {
        /// <summary>The item group of the rows. Group numbers are one space with the item lists: take a number of your own that no
        /// list uses (AddGroup throws ArgumentException when it is taken).</summary>
        public const int Group = 820001;
        public const int Category = 0;
        public const int Cube = 1, Sphere = 2, Cylinder = 3, Torus = 4, Arrow = 5;
        public const int RingBones = 8;

        IPluginContext ctx;
        IItemRowGroup group;

        public void Load(IPluginContext context)
        {
            ctx = context;
            var rows = ctx.Unity()?.ItemRows;
            if (rows == null)
            {
                ctx.Log.Warning("shapes: no engine side in this host");
                return;
            }
            try { group = rows.AddGroup(Group, "Shapes", Make); }
            catch (ArgumentException e)
            {
                ctx.Log.Warning("shapes: " + e.Message);
                return;
            }
            group.SetCategory(Category, "Shapes");
            var ring = new string[RingBones];
            for (int i = 0; i < RingBones; i++) ring[i] = RingBone(i);
            group.SetRows(new[]
            {
                new ItemRow { Category = Category, Id = Cube, Name = "Cube" },
                new ItemRow { Category = Category, Id = Sphere, Name = "Sphere" },
                new ItemRow { Category = Category, Id = Cylinder, Name = "Cylinder" },
                new ItemRow { Category = Category, Id = Torus, Name = "Torus", FkBones = ring },
                new ItemRow { Category = Category, Id = Arrow, Name = "Arrow" },
            });
        }

        // The app removes the group when the plugin unloads; disposing it here does the same and shows the way.
        public void Unload()
        {
            group?.Dispose();
            group = null;
        }

        public static string RingBone(int i) => "ring" + i;

        /// <summary>Builds the object of one row: a new object for each call (the app owns it from then on).</summary>
        static GameObject Make(ItemRowRequest request)
        {
            switch (request.Id)
            {
                case Cube: return Static("Cube", BoxMesh(2f), new Color(0.85f, 0.35f, 0.25f));
                case Sphere: return Static("Sphere", SphereMesh(1.2f, 24, 16), new Color(0.3f, 0.6f, 0.9f));
                case Cylinder: return Static("Cylinder", CylinderMesh(0.8f, 2.4f, 24), new Color(0.4f, 0.8f, 0.4f));
                case Torus: return TorusObject(3f, 0.7f, 64, 16);
                case Arrow: return Static("Arrow", ArrowMesh(), new Color(0.95f, 0.8f, 0.2f));
                default: return null;
            }
        }

        static Material NewMaterial(Color c)
        {
            var m = new Material(Shader.Find("HS2Next/Standard")) { name = "shapes" };
            m.SetColor("_Color", c);
            return m;
        }

        static GameObject Static(string name, Mesh mesh, Color c)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = NewMaterial(c);
            return go;
        }

        // ---------------------------------------------------------------- meshes (y up, the base on y = 0)

        static Mesh Finish(string name, List<Vector3> v, List<Vector3> n, List<int> t)
        {
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh BoxMesh(float size)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            float h = size / 2f;
            Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var nn in normals)
            {
                var u = Vector3.Cross(nn, Mathf.Abs(nn.y) > 0.5f ? Vector3.forward : Vector3.up);
                var w = Vector3.Cross(nn, u);
                int b = v.Count;
                foreach (var (a, c) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                {
                    v.Add((nn + a * u + c * w) * h + new Vector3(0, h, 0));
                    n.Add(nn);
                }
                t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            }
            return Finish("shapes_box", v, n, t);
        }

        static Mesh SphereMesh(float r, int seg, int rings)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i <= rings; i++)
            {
                float th = Mathf.PI * i / rings;
                for (int j = 0; j <= seg; j++)
                {
                    float ph = 2f * Mathf.PI * j / seg;
                    var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    v.Add(d * r + new Vector3(0, r, 0));
                    n.Add(d);
                }
            }
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < seg; j++)
                {
                    int a = i * (seg + 1) + j, b = a + seg + 1;
                    t.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                }
            return Finish("shapes_sphere", v, n, t);
        }

        static void Tube(List<Vector3> v, List<Vector3> n, List<int> t, float r0, float r1, float y0, float y1, int seg)
        {
            float slope = (r0 - r1) / Mathf.Max(1e-4f, y1 - y0);
            int b = v.Count;
            for (int j = 0; j <= seg; j++)
            {
                float ph = 2f * Mathf.PI * j / seg;
                var d = new Vector3(Mathf.Cos(ph), 0, Mathf.Sin(ph));
                var nn = (d + new Vector3(0, slope, 0)).normalized;
                v.Add(d * r0 + new Vector3(0, y0, 0)); n.Add(nn);
                v.Add(d * r1 + new Vector3(0, y1, 0)); n.Add(nn);
            }
            for (int j = 0; j < seg; j++)
            {
                int a = b + 2 * j;
                t.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 });
            }
        }

        static void Disc(List<Vector3> v, List<Vector3> n, List<int> t, float r, float y, bool up, int seg)
        {
            int c = v.Count;
            var nn = up ? Vector3.up : Vector3.down;
            v.Add(new Vector3(0, y, 0)); n.Add(nn);
            for (int j = 0; j <= seg; j++)
            {
                float ph = 2f * Mathf.PI * j / seg;
                v.Add(new Vector3(Mathf.Cos(ph) * r, y, Mathf.Sin(ph) * r)); n.Add(nn);
            }
            for (int j = 0; j < seg; j++)
                t.AddRange(up ? new[] { c, c + j + 2, c + j + 1 } : new[] { c, c + j + 1, c + j + 2 });
        }

        static Mesh CylinderMesh(float r, float h, int seg)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            Tube(v, n, t, r, r, 0, h, seg);
            Disc(v, n, t, r, h, true, seg);
            Disc(v, n, t, r, 0, false, seg);
            return Finish("shapes_cylinder", v, n, t);
        }

        static Mesh ArrowMesh()
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            Tube(v, n, t, 0.25f, 0.25f, 0, 2.2f, 16);
            Disc(v, n, t, 0.25f, 0, false, 16);
            Disc(v, n, t, 0.6f, 2.2f, false, 16);
            Tube(v, n, t, 0.6f, 0.001f, 2.2f, 3.2f, 16);
            return Finish("shapes_arrow", v, n, t);
        }

        // The torus lies flat (its ring in the xz plane, centre at y = tube). Bone i sits on the ring at the angle i x 45 degrees and
        // is the child of bone i - 1, so turning a bone turns the rest of the ring after it. A vertex follows the two bones around
        // its angle, by how close it is to each.
        static GameObject TorusObject(float ring, float tube, int seg, int sides)
        {
            var go = new GameObject("Torus");
            var bones = new Transform[RingBones];
            var parent = go.transform;
            for (int i = 0; i < RingBones; i++)
            {
                float a = 2f * Mathf.PI * i / RingBones;
                var b = new GameObject(RingBone(i)).transform;
                b.SetParent(parent, false);
                b.position = new Vector3(Mathf.Cos(a) * ring, tube, Mathf.Sin(a) * ring);
                b.rotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0);
                bones[i] = b;
                parent = b;
            }
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            var w = new List<BoneWeight>();
            for (int i = 0; i <= seg; i++)
            {
                float u = 2f * Mathf.PI * i / seg;
                var c = new Vector3(Mathf.Cos(u), 0, Mathf.Sin(u));
                float k = (float)i / seg * RingBones;
                int b0 = Mathf.FloorToInt(k) % RingBones, b1 = (b0 + 1) % RingBones;
                float f = k - Mathf.Floor(k);
                for (int j = 0; j <= sides; j++)
                {
                    float s = 2f * Mathf.PI * j / sides;
                    var d = c * Mathf.Cos(s) + Vector3.up * Mathf.Sin(s);
                    v.Add(c * ring + d * tube + new Vector3(0, tube, 0));
                    n.Add(d);
                    w.Add(i == seg ? new BoneWeight { boneIndex0 = 0, weight0 = 1f } : new BoneWeight { boneIndex0 = b0, weight0 = 1f - f, boneIndex1 = b1, weight1 = f });
                }
            }
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a = i * (sides + 1) + j, b = a + sides + 1;
                    t.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                }
            var mesh = Finish("shapes_torus", v, n, t);
            mesh.boneWeights = w.ToArray();
            var bind = new Matrix4x4[RingBones];
            for (int i = 0; i < RingBones; i++) bind[i] = bones[i].worldToLocalMatrix * go.transform.localToWorldMatrix;
            mesh.bindposes = bind;
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = bones[0];
            smr.sharedMaterial = NewMaterial(new Color(0.75f, 0.45f, 0.85f));
            // bounds in the root bone's space, wide enough for a bent ring
            float e = 4f * (ring + tube);
            smr.localBounds = new Bounds(new Vector3(-ring, 0, 0), new Vector3(e, e, e));
            return go;
        }
    }
}
