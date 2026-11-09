// The drops of a character: droplets falling from the wet parts and the splashes they leave at the floor, in fixed steps of a stain's clock, drawn by one
// dynamic mesh on a hidden renderer under the runtime's wet root. Nothing is saved: a loaded running stain emits again from its clock.
//   step      a droplet falls under gravity and lands on the floor as it is at that moment, where it gives its puddle its share and leaves 3 splashes
//   caps      1,500 droplets and 600 splashes; an emission over a cap is lost
//   drawing   per droplet the head, the tail and the highlight, per splash a small triangle, in world space; the shader turns them to the camera in use, so a
//             mirror and every capture draw them right
// Everything is a function of the stain's seed, its clock and the bones at the step: no System.Random, no real time.
using System;
using System.Runtime.InteropServices;
using HS2Next.Scenes.Wet;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    public sealed class WetDrops : IDisposable
    {
        public const string ShaderName = "HS2Next/Hidden/WetDrops";
        public const int MaxDrops = 1500, MaxSplashes = 600, SplashesPerDrop = 3;
        /// <summary>How long a splash lives, in seconds.</summary>
        public const float SplashLife = 0.22f;
        const int DropVertices = 8, DropTriangles = 4, SplashVertices = 3;

        struct Drop
        {
            public Vector3 P, V;
            public float Size, Shine;
            public Color32 Color;
            public int Stain, Seed;
        }

        struct Splash
        {
            public Vector3 P, V;
            public float Size, Age;
            public Color32 Color;
        }

        static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
        };

        // the vertex struct follows Unity's fixed stream order: position, normal, colour, then the texture coordinate
        [StructLayout(LayoutKind.Sequential)]
        struct Packed
        {
            public Vector3 Position, Axis;
            public Color32 Color;
            public Vector2 Offset;
        }

        static readonly int VertexCount = MaxDrops * DropVertices + MaxSplashes * SplashVertices;
        static readonly int IndexCount = MaxDrops * DropTriangles * 3 + MaxSplashes * 3;

        readonly Drop[] drops = new Drop[MaxDrops];
        readonly Splash[] splashes = new Splash[MaxSplashes];
        readonly Transform root;
        Packed[] vertices;
        int[] indices;
        int dropCount, splashCount, splashIndex, indexed, drawnIndices;
        bool changed;
        Mesh mesh;
        MeshRenderer renderer;
        Material material;

        public WetDrops(Transform root) { this.root = root; }

        /// <summary>Droplets and splashes in flight.</summary>
        public int Drops => dropCount;
        public int Splashes => splashCount;
        /// <summary>Droplets emitted, and a hash of where and when (WD8, WD8a).</summary>
        public int Emitted { get; private set; }
        public ulong EmitHash { get; private set; } = 14695981039346656037UL;
        /// <summary>The hidden renderer, null until something flew (tests).</summary>
        public MeshRenderer Renderer => renderer;
        /// <summary>Hidden with the node.</summary>
        public bool Hidden { get; set; }

        /// <summary>Sends a droplet from a point; false over the cap. <paramref name="step"/> and <paramref name="index"/> name it in the hash.</summary>
        public bool Emit(Vector3 p, float size, Color c, float shine, int stain, int seed, long step, int index)
        {
            if (dropCount >= MaxDrops) return false;
            drops[dropCount++] = new Drop { P = p, Size = size, Shine = shine, Color = c, Stain = stain, Seed = seed };
            Emitted++;
            unchecked
            {
                ulong h = EmitHash;
                h = (h ^ (ulong)step) * 1099511628211UL;
                h = (h ^ (ulong)index) * 1099511628211UL;
                h = (h ^ (uint)seed) * 1099511628211UL;
                h = (h ^ (uint)BitConverter.SingleToInt32Bits(p.x)) * 1099511628211UL;
                h = (h ^ (uint)BitConverter.SingleToInt32Bits(p.y)) * 1099511628211UL;
                h = (h ^ (uint)BitConverter.SingleToInt32Bits(p.z)) * 1099511628211UL;
                EmitHash = h;
            }
            changed = true;
            return true;
        }

        /// <summary>Starts the hash over (tests).</summary>
        public void ResetHash() { EmitHash = 14695981039346656037UL; Emitted = 0; }

        /// <summary>One fixed step: everything falls; <paramref name="land"/> gets each droplet that reached <paramref name="floor"/>.</summary>
        public void Step(float floor, Action<int, Vector3, float> land)
        {
            float dt = (float)WetMath.DropStep, g = (float)WetMath.Gravity;
            int n = 0;
            for (int i = 0; i < dropCount; i++)
            {
                var d = drops[i];
                d.V.y -= g * dt;
                d.P += d.V * dt;
                if (d.P.y <= floor)
                {
                    d.P.y = floor;
                    land(d.Stain, d.P, d.Size);
                    Burst(d);
                    continue;
                }
                drops[n++] = d;
            }
            dropCount = n;
            n = 0;
            for (int i = 0; i < splashCount; i++)
            {
                var s = splashes[i];
                s.Age += dt;
                s.V.y -= g * dt;
                s.P += s.V * dt;
                if (s.Age >= SplashLife || s.P.y < floor) continue;
                splashes[n++] = s;
            }
            splashCount = n;
            changed = true;
        }

        void Burst(in Drop d)
        {
            for (int k = 0; k < SplashesPerDrop && splashCount < MaxSplashes; k++)
            {
                long at = splashIndex++;
                double a = WetMath.Rng(d.Seed ^ 0x5a5a5a, at * 2) * Math.PI * 2, up = 2 + 3 * WetMath.Rng(d.Seed ^ 0x5a5a5a, at * 2 + 1);
                splashes[splashCount++] = new Splash
                {
                    P = d.P,
                    V = new Vector3((float)Math.Cos(a) * 3f, (float)up, (float)Math.Sin(a) * 3f),
                    Size = d.Size * .6f,
                    Color = d.Color,
                };
            }
        }

        /// <summary>Takes away what flies: of one stain, or with -1 all of it.</summary>
        public void Clear(int stain = -1)
        {
            if (stain < 0) { dropCount = splashCount = 0; changed = true; return; }
            int n = 0;
            for (int i = 0; i < dropCount; i++)
                if (drops[i].Stain != stain) drops[n++] = drops[i];
            if (n != dropCount) changed = true;
            dropCount = n;
        }

        /// <summary>The mesh of the moment; nothing when nothing changed.</summary>
        public void Draw()
        {
            bool show = !Hidden && (dropCount > 0 || splashCount > 0);
            if (renderer && renderer.enabled != show) renderer.enabled = show;
            if (!changed || !show) { if (!show) changed = false; return; }
            changed = false;
            if (!Ensure()) return;
            int v = 0, idx = 0;
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = -min;
            for (int i = 0; i < dropCount; i++)
            {
                ref var d = ref drops[i];
                float speed = Mathf.Abs(d.V.y), head = d.Size, tail = head + Mathf.Min(speed * .018f, 12f * head);
                var axis = Vector3.up;
                var hi = new Color32(255, 255, 255, (byte)Mathf.Clamp(d.Shine * d.Color.a * 1.6f + d.Shine * 90f, 0f, 255f));
                var fade = d.Color;
                fade.a = 0;
                // the head
                vertices[v] = new Packed { Position = d.P, Axis = axis, Offset = new Vector2(-head, -head), Color = d.Color };
                vertices[v + 1] = new Packed { Position = d.P, Axis = axis, Offset = new Vector2(head, -head), Color = d.Color };
                vertices[v + 2] = new Packed { Position = d.P, Axis = axis, Offset = new Vector2(head, head), Color = d.Color };
                vertices[v + 3] = new Packed { Position = d.P, Axis = axis, Offset = new Vector2(-head, head), Color = d.Color };
                // the tail, narrowing upwards
                vertices[v + 4] = new Packed { Position = d.P, Axis = axis, Offset = new Vector2(0f, tail), Color = fade };
                // the highlight
                float h = head * .4f;
                vertices[v + 5] = new Packed { Position = d.P, Axis = axis, Offset = new Vector2(-h * 1.3f, h * .2f), Color = hi };
                vertices[v + 6] = new Packed { Position = d.P, Axis = axis, Offset = new Vector2(-h * .1f, h * .2f), Color = hi };
                vertices[v + 7] = new Packed { Position = d.P, Axis = axis, Offset = new Vector2(-h * .7f, h * 1.2f), Color = hi };
                v += DropVertices;
                idx += DropTriangles * 3;
                min = Vector3.Min(min, d.P);
                max = Vector3.Max(max, d.P);
            }
            for (int i = 0; i < splashCount; i++)
            {
                ref var s = ref splashes[i];
                float k = s.Size * (1f - s.Age / SplashLife);
                vertices[v] = new Packed { Position = s.P, Offset = new Vector2(-k, -k), Color = s.Color };
                vertices[v + 1] = new Packed { Position = s.P, Offset = new Vector2(k, -k), Color = s.Color };
                vertices[v + 2] = new Packed { Position = s.P, Offset = new Vector2(0f, k), Color = s.Color };
                v += SplashVertices;
                idx += 3;
                min = Vector3.Min(min, s.P);
                max = Vector3.Max(max, s.P);
            }
            WriteIndices(dropCount, splashCount);
            const MeshUpdateFlags quiet = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds;
            mesh.SetVertexBufferData(vertices, 0, 0, v, 0, quiet);
            mesh.SetIndexBufferData(indices, 0, 0, idx, quiet);
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, idx), quiet);
            var b = new Bounds();
            b.SetMinMax(min, max);
            b.Expand(2f);
            mesh.bounds = b;
            drawnIndices = idx;
        }

        /// <summary>Indices drawn by the last <see cref="Draw"/> (tests).</summary>
        public int DrawnIndices => drawnIndices;

        // drops first, then splashes: the pattern of a drop never changes, so only what follows it does
        void WriteIndices(int nd, int ns)
        {
            if (indexed != nd)
            {
                for (int i = Math.Min(indexed, nd); i < nd; i++)
                {
                    int b = i * DropVertices, o = i * DropTriangles * 3;
                    indices[o] = b; indices[o + 1] = b + 1; indices[o + 2] = b + 2;
                    indices[o + 3] = b; indices[o + 4] = b + 2; indices[o + 5] = b + 3;
                    indices[o + 6] = b + 3; indices[o + 7] = b + 2; indices[o + 8] = b + 4;
                    indices[o + 9] = b + 5; indices[o + 10] = b + 6; indices[o + 11] = b + 7;
                }
                indexed = nd;
            }
            int first = nd * DropVertices, at = nd * DropTriangles * 3;
            for (int i = 0; i < ns; i++, at += 3)
            {
                indices[at] = first + i * SplashVertices;
                indices[at + 1] = first + i * SplashVertices + 1;
                indices[at + 2] = first + i * SplashVertices + 2;
            }
        }

        bool Ensure()
        {
            if (mesh) return true;
            var s = Shader.Find(ShaderName);
            if (!s || !root) return false;
            vertices = new Packed[VertexCount];
            indices = new int[IndexCount];
            mesh = new Mesh { name = "HS2Next wet drops", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            mesh.SetVertexBufferParams(VertexCount, Layout);
            mesh.SetIndexBufferParams(IndexCount, IndexFormat.UInt32);
            mesh.subMeshCount = 1;
            material = new Material(s) { name = "HS2Next wet drops", hideFlags = HideFlags.HideAndDontSave };
            var go = new GameObject("wet drops") { hideFlags = HideFlags.HideAndDontSave, layer = RenderingLayers.Chara };
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.renderingLayerMask = RenderingLayers.ForLayer(RenderingLayers.Chara);
            return true;
        }

        public void Dispose()
        {
            if (renderer) Destroy(renderer.gameObject);
            Destroy(mesh);
            Destroy(material);
            renderer = null;
            mesh = null;
            material = null;
            dropCount = splashCount = 0;
        }

        static void Destroy(Object o)
        {
            if (!o) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }
}
