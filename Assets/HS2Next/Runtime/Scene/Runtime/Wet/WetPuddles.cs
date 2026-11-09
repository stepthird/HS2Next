// The puddles of a character: the runtime's list, a mesh per puddle and the claims on the mirrors of their heights.
//   list      a droplet joins the nearest puddle of its stain within the puddle's radius + 0.6, else it starts one, at most 30 per character; at the limit it is
//             lost. A puddle grows by its share, its centre drifts towards where the droplet fell
//   mesh      a fan of 40 segments and 4 rings with the outline's noise, rebuilt when the area moved by 2 %, on a hidden renderer 0.015 above the floor
//   drawing   an unlit disc with the mirror image over it: colour, rim, shine, mirror strength and plane height through the renderer's block
//   mirrors   one claim per height, held while a puddle lies there; a character with no puddle holds none, so a scene without puddles renders no mirror
// The list is the runtime's: the capture writes it into the document. A kept stain's puddle has its colour in the list.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes.Wet;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    public sealed class WetPuddles : IDisposable
    {
        public const string ShaderName = "HS2Next/Hidden/WetPuddle";
        /// <summary>A puddle lies this far above the floor.</summary>
        public const float Lift = 0.015f;
        const int Segments = 40, Rings = 4;
        const float Rebuild = 0.02f;

        static readonly int ColourId = Shader.PropertyToID("_WetPuddleColor");
        static readonly int EdgeId = Shader.PropertyToID("_WetPuddleEdge");
        static readonly int ShineId = Shader.PropertyToID("_WetPuddleShine");
        static readonly int StrengthId = Shader.PropertyToID("_WetMirrorStrength");

        public sealed class Puddle
        {
            public string Stain;
            public Vector3 Center;
            public float Area, Seed;
            public Color? Colour;
            public GameObject Object;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public float DrawnArea = -1f, DrawnScale = -1f;
            public int Height;
        }

        readonly object owner;
        readonly Transform root;
        readonly List<Puddle> list = new List<Puddle>();
        readonly Dictionary<int, PlanarMirrors.Handle> handles = new Dictionary<int, PlanarMirrors.Handle>();
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        readonly Vector3[] vertices = new Vector3[1 + Segments * Rings];
        readonly Vector2[] uv = new Vector2[1 + Segments * Rings];
        static int[] triangles;
        Material material;
        bool hidden;

        public WetPuddles(object owner, Transform root) { this.owner = owner; this.root = root; }

        public IReadOnlyList<Puddle> List => list;
        public int Count => list.Count;
        /// <summary>Claims held on mirror heights (tests).</summary>
        public int Mirrors => handles.Count;
        /// <summary>Changes with every change of the list (the capture's mark).</summary>
        public int Version { get; private set; }

        /// <summary>The list becomes the document's.</summary>
        public void Sync(CharaWet doc)
        {
            Clear();
            foreach (var p in doc.AppliedPuddles())
                list.Add(new Puddle
                {
                    Stain = p.Stain,
                    Center = new Vector3(p.Center.X, p.Center.Y, p.Center.Z),
                    Area = p.AppliedArea,
                    Seed = p.Seed,
                    Colour = p.Color.HasValue ? new Color(p.Color.Value.R, p.Color.Value.G, p.Color.Value.B, p.Color.Value.A) : (Color?)null,
                });
            Version++;
        }

        /// <summary>True when <paramref name="doc"/>'s list says what this list says.</summary>
        public bool Same(IReadOnlyList<WetPuddleEntry> doc)
        {
            if (doc.Count != list.Count) return false;
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                var b = doc[i];
                if (a.Stain != b.Stain || a.Center.x != b.Center.X || a.Center.y != b.Center.Y || a.Center.z != b.Center.Z || a.Area != b.AppliedArea || a.Seed != b.Seed) return false;
                if (a.Colour.HasValue != b.Color.HasValue) return false;
            }
            return true;
        }

        /// <summary>A puddle was moved from outside.</summary>
        public void Touch() => Version++;

        /// <summary>The list as the document holds it.</summary>
        public List<WetPuddleEntry> ToEntries()
        {
            var l = new List<WetPuddleEntry>(list.Count);
            foreach (var p in list)
                l.Add(new WetPuddleEntry
                {
                    Stain = p.Stain,
                    Center = new Vec3(p.Center.x, p.Center.y, p.Center.z),
                    Area = p.Area,
                    Seed = p.Seed,
                    Color = p.Colour.HasValue ? new Color4(p.Colour.Value.r, p.Colour.Value.g, p.Colour.Value.b, p.Colour.Value.a) : (Color4?)null,
                });
            return l;
        }

        /// <summary>A droplet of a stain landed at <paramref name="p"/>.</summary>
        public void Land(string stain, Vector3 p, float size, float growth, float scale, int seed)
        {
            double add = WetMath.AreaOf(growth, size);
            var own = OwnOf(stain);
            var step = WetMath.Join(p.x, p.z, add, scale, own.Count, i => own[i].Area, i => own[i].Center.x, i => own[i].Center.z);
            if (step.Index < 0)
            {
                if (list.Count >= WetLimits.MaxPuddles) return;
                list.Add(new Puddle { Stain = stain, Center = p, Area = (float)step.Area, Seed = (float)(WetMath.Rng(seed ^ 0x77, list.Count) * 100.0) });
            }
            else
            {
                var q = own[step.Index];
                q.Area = (float)step.Area;
                q.Center.x = (float)step.X;
                q.Center.z = (float)step.Z;
            }
            Version++;
        }

        List<Puddle> OwnOf(string stain)
        {
            var l = new List<Puddle>();
            foreach (var p in list) if (p.Stain == stain && !p.Colour.HasValue) l.Add(p);
            return l;
        }

        /// <summary>Hidden with the node.</summary>
        public bool Hidden
        {
            get => hidden;
            set
            {
                if (hidden == value) return;
                hidden = value;
                foreach (var p in list) if (p.Renderer) p.Renderer.enabled = !value;
            }
        }

        /// <summary>
        /// Brings the meshes, the blocks and the mirror claims up to the list. <paramref name="lookOf"/> gives a puddle's stain look; a stain that is gone draws the
        /// default look. Nothing happens without a graphics device.
        /// </summary>
        public void Draw(Func<string, WetPuddleLook> lookOf, WetMirrorValues mirror)
        {
            if (list.Count == 0) { ReleaseMirrors(null); return; }
            if (!Ensure()) return;
            HashSet<int> used = null;
            foreach (var p in list)
            {
                var look = lookOf(p.Stain) ?? new WetPuddleLook();
                if (!p.Object) Make(p);
                float scale = look.Scale;
                if (p.DrawnArea < 0f || p.DrawnScale != scale || Mathf.Abs(p.Area - p.DrawnArea) > Rebuild * Mathf.Max(p.DrawnArea, 1e-5f))
                    Shape(p, scale);
                p.Object.transform.position = p.Center + new Vector3(0f, Lift, 0f);
                int key = Mathf.RoundToInt(p.Center.y * 1000f);
                p.Height = key;
                float planeY = float.NaN;
                if (mirror.On)
                {
                    if (!handles.TryGetValue(key, out var h)) handles[key] = h = PlanarMirrors.Request(owner, p.Center.y, mirror.Scale);
                    h.Scale = mirror.Scale;
                    if (!h.Puddles.Contains(p.Renderer)) h.Puddles.Add(p.Renderer);
                    planeY = h.PlaneY;
                    (used ??= new HashSet<int>()).Add(key);
                }
                var c = p.Colour ?? new Color(look.Color.R, look.Color.G, look.Color.B, look.Color.A);
                p.Renderer.GetPropertyBlock(block);
                block.SetColor(ColourId, c);
                block.SetFloat(EdgeId, look.Edge);
                block.SetFloat(ShineId, look.Shine);
                block.SetFloat(StrengthId, mirror.On ? mirror.Strength : 0f);
                block.SetFloat(PlanarMirrors.PlaneYId, float.IsNaN(planeY) ? 0f : planeY);
                p.Renderer.SetPropertyBlock(block);
            }
            ReleaseMirrors(used);
        }

        void ReleaseMirrors(HashSet<int> used)
        {
            if (handles.Count == 0) return;
            List<int> gone = null;
            foreach (var kv in handles)
            {
                kv.Value.Puddles.RemoveAll(r => !r);
                if (used != null && used.Contains(kv.Key)) { Prune(kv); continue; }
                (gone ??= new List<int>()).Add(kv.Key);
            }
            if (gone == null) return;
            foreach (int k in gone) { handles[k].Dispose(); handles.Remove(k); }
        }

        void Prune(KeyValuePair<int, PlanarMirrors.Handle> kv)
        {
            var l = kv.Value.Puddles;
            for (int i = l.Count - 1; i >= 0; i--)
            {
                bool here = false;
                foreach (var p in list) if (p.Renderer == l[i] && p.Height == kv.Key) { here = true; break; }
                if (!here) l.RemoveAt(i);
            }
        }

        bool Ensure()
        {
            if (material) return true;
            if (!root || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return false;
            var s = Shader.Find(ShaderName);
            if (!s) return false;
            material = new Material(s) { name = "HS2Next wet puddle", hideFlags = HideFlags.HideAndDontSave };
            return true;
        }

        void Make(Puddle p)
        {
            p.Object = new GameObject("wet puddle") { hideFlags = HideFlags.HideAndDontSave, layer = RenderingLayers.Chara };
            p.Object.transform.SetParent(root, false);
            p.Mesh = new Mesh { name = "HS2Next wet puddle", hideFlags = HideFlags.HideAndDontSave };
            p.Mesh.MarkDynamic();
            p.Object.AddComponent<MeshFilter>().sharedMesh = p.Mesh;
            p.Renderer = p.Object.AddComponent<MeshRenderer>();
            p.Renderer.sharedMaterial = material;
            p.Renderer.shadowCastingMode = ShadowCastingMode.Off;
            p.Renderer.receiveShadows = false;
            p.Renderer.lightProbeUsage = LightProbeUsage.Off;
            p.Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            p.Renderer.renderingLayerMask = RenderingLayers.ForLayer(RenderingLayers.Chara);
            p.Renderer.enabled = !hidden;
        }

        void Shape(Puddle p, float scale)
        {
            float radius = (float)WetMath.PuddleRadius(p.Area, scale);
            vertices[0] = Vector3.zero;
            uv[0] = Vector2.zero;
            for (int s = 0; s < Segments; s++)
            {
                double a = s * (Math.PI * 2 / Segments);
                float o = (float)WetMath.Outline(a, p.Seed) * radius;
                float x = (float)Math.Cos(a) * o, z = (float)Math.Sin(a) * o;
                for (int r = 1; r <= Rings; r++)
                {
                    float t = r / (float)Rings;
                    int i = 1 + s * Rings + r - 1;
                    vertices[i] = new Vector3(x * t, 0f, z * t);
                    uv[i] = new Vector2(t, 0f);
                }
            }
            p.Mesh.SetVertices(vertices);
            p.Mesh.SetUVs(0, uv);
            p.Mesh.SetTriangles(Triangles(), 0, false);
            var extent = radius * 1.3f + .01f;
            p.Mesh.bounds = new Bounds(Vector3.zero, new Vector3(extent * 2f, .02f, extent * 2f));
            p.DrawnArea = p.Area;
            p.DrawnScale = scale;
        }

        static int[] Triangles()
        {
            if (triangles != null) return triangles;
            var t = new List<int>(Segments * (3 + 6 * (Rings - 1)));
            for (int s = 0; s < Segments; s++)
            {
                int n = (s + 1) % Segments, a = 1 + s * Rings, b = 1 + n * Rings;
                t.Add(0); t.Add(b); t.Add(a);
                for (int r = 0; r < Rings - 1; r++)
                {
                    t.Add(a + r); t.Add(b + r); t.Add(a + r + 1);
                    t.Add(a + r + 1); t.Add(b + r); t.Add(b + r + 1);
                }
            }
            return triangles = t.ToArray();
        }

        /// <summary>Removes the puddles of a stain, or with null every puddle.</summary>
        public void Remove(string stain)
        {
            for (int i = list.Count - 1; i >= 0; i--)
                if (stain == null || list[i].Stain == stain)
                {
                    Free(list[i]);
                    list.RemoveAt(i);
                    Version++;
                }
        }

        void Clear()
        {
            foreach (var p in list) Free(p);
            list.Clear();
        }

        void Free(Puddle p)
        {
            if (p.Object) Destroy(p.Object);
            if (p.Mesh) Destroy(p.Mesh);
            p.Object = null; p.Mesh = null; p.Renderer = null;
        }

        public void Dispose()
        {
            Clear();
            foreach (var h in handles.Values) h.Dispose();
            handles.Clear();
            if (material) Destroy(material);
            material = null;
        }

        static void Destroy(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }
}
