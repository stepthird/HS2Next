// What a shot leaves in the air and on the ground: blood droplets, the splats they make, dust at a wall, a tracer and a flash. World-space lists stepped by the scene clock, drawn by
// one dynamic mesh on a hidden renderer under the runtime's root, not in the physics world, so captures, recordings and mirrors show them. Nothing is saved.
//   step      a droplet falls with drag and makes a splat where it meets the ground it is given; everything fades and ends by its own life; dt 0 moves nothing, so the pause stops them
//   caps      3,000 droplets: an emission over the cap is lost; 400 splats: the oldest goes
//   blood     with ShowBlood off no droplet and no splat starts; dust, tracer and flash stay; what lies on the ground stays
//   drawing   the hidden drops shader: a droplet's head turns to the camera, its tail and a tracer hang along their axis, a splat lies flat, its vertices at their world positions
//   numbers   every number comes from WetMath.Rng on a stream named per shot, so the effects of a shot are a function of the seed and the shot, whatever came before
using System;
using System.Runtime.InteropServices;
using HS2Next.Scenes.Wet;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    public sealed class RagdollFx : IDisposable
    {
        /// <summary>The option that shows blood; the app's option row writes <see cref="ShowBlood"/>.</summary>
        public const string OptionKey = "studio.blood";
        public const int MaxDroplets = 3000, MaxSplats = 400, MaxDust = 480, MaxTracers = 64, MaxFlashes = 64;
        public const int DustPerWall = 12, HotDust = 4, SplatRim = 14, MaxSatellites = 5;
        public const float SplatLift = .02f, SplatLife = 25f, SplatFade = 3f, TracerLife = .09f, FlashLife = .05f;
        const float Drag = 1.5f, TailPerSpeed = .012f, TracerStart = .06f, TracerEnd = .018f, TracerCore = .4f;

        /// <summary>Droplets and splats start only with this on.</summary>
        public static bool ShowBlood { get; set; } = true;

        // a droplet or a grain of dust
        struct Bit
        {
            public Vector3 P, V;
            public float Size, Age, Life;
            public Color32 Color;
            public int Seed;
        }

        struct Splat
        {
            public Vector3 P;
            public float Born;
            public Color32 Color;
        }

        struct Tracer
        {
            public Vector3 A, B;
            public float Age;
        }

        struct Flash
        {
            public Vector3 P, X, Y, Z;
            public float Size, Age;
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

        const int HeadRim = 6, BitVertices = HeadRim + 1 + 3, BitIndices = (HeadRim + 1) * 3;
        const int SatelliteVertices = HeadRim + 1, SplatVertices = SplatRim + 1 + MaxSatellites * SatelliteVertices, SplatIndices = (SplatRim + MaxSatellites * HeadRim) * 3;
        const int TracerVertices = 8, TracerIndices = 12, FlashVertices = 12, FlashIndices = 18;
        const int SplatRegion = MaxSplats * SplatVertices, SplatIndexRegion = MaxSplats * SplatIndices;
        const int VertexCount = SplatRegion + (MaxDroplets + MaxDust) * BitVertices + MaxTracers * TracerVertices + MaxFlashes * FlashVertices;
        const int IndexCount = SplatIndexRegion + (MaxDroplets + MaxDust) * BitIndices + MaxTracers * TracerIndices + MaxFlashes * FlashIndices;

        static readonly Vector2[] Unit6 = Ring(HeadRim);

        static Vector2[] Ring(int n)
        {
            var r = new Vector2[n];
            for (int i = 0; i < n; i++) r[i] = new Vector2((float)Math.Cos(i * 2 * Math.PI / n), (float)Math.Sin(i * 2 * Math.PI / n));
            return r;
        }

        readonly Transform root;
        readonly Bit[] droplets = new Bit[MaxDroplets], dust = new Bit[MaxDust];
        readonly Splat[] splats = new Splat[MaxSplats];
        readonly Tracer[] tracers = new Tracer[MaxTracers];
        readonly Flash[] flashes = new Flash[MaxFlashes];
        readonly Vector2[] rimScratch = new Vector2[SplatRim];
        readonly Vector3[] satScratch = new Vector3[MaxSatellites];
        int dropletCount, dustCount, tracerCount, flashCount, tracerNext, flashNext;
        int splatCount, splatNext, splatHigh, splatDirtyFrom = int.MaxValue, splatDirtyTo = -1;
        float clock;
        int stream;
        long drawn;
        bool changed;
        Packed[] vertices;
        int[] indices;
        int indexedBits;
        Vector3 splatMin, splatMax;
        bool splatBoundsStale;
        Mesh mesh;
        MeshRenderer renderer;
        Material material;

        /// <summary><paramref name="root"/> carries the hidden renderer; it stays at the world's origin.</summary>
        public RagdollFx(Transform root) { this.root = root; }

        public int Droplets => dropletCount;
        public int Splats => splatCount;
        public int Dust => dustCount;
        public int Tracers => tracerCount;
        public int Flashes => flashCount;
        /// <summary>Anything to step or to draw.</summary>
        public bool Any => dropletCount > 0 || splatCount > 0 || dustCount > 0 || tracerCount > 0 || flashCount > 0;
        /// <summary>The hidden renderer, null until something was drawn (tests).</summary>
        public MeshRenderer Renderer => renderer;
        /// <summary>Droplets lost at the cap, splats pushed out by it.</summary>
        public int DropletsLost { get; private set; }
        public int SplatsPushed { get; private set; }

        // ------------------------------------------------------------ numbers

        /// <summary>The numbers that follow are those of this shot of this seed.</summary>
        public void BeginShot(int seed, long shot)
        {
            stream = unchecked(seed ^ 0x51ab1e);
            drawn = shot << 24;
        }

        float R() => (float)WetMath.Rng(stream, drawn++);
        float R(float a, float b) => a + (b - a) * R();

        // a unit vector, uniform on the sphere
        Vector3 RandomUnit()
        {
            float y = R(-1f, 1f), a = R(0f, 2f * Mathf.PI), s = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            return new Vector3(s * Mathf.Cos(a), y, s * Mathf.Sin(a));
        }

        /// <summary>How many droplets a part's share <paramref name="j"/> of the bullet throws.</summary>
        public static int DropletsOf(float j, bool head) => Mathf.Clamp((int)(j / 6f), 6, head ? 60 : 35);

        // ------------------------------------------------------------ what starts

        /// <summary>Blood at a part: two droplets in three along the bullet, one back. Nothing with <see cref="ShowBlood"/> off.</summary>
        public void Blood(Vector3 point, Vector3 dir, float j, bool head)
        {
            if (!ShowBlood) return;
            int n = DropletsOf(j, head);
            for (int i = 0; i < n; i++)
            {
                if (dropletCount >= MaxDroplets) { DropletsLost += n - i; break; }
                bool back = i % 3 == 2;
                var d = ((back ? -dir : dir) + .6f * RandomUnit()).normalized;
                float v = R(.7f, 1.1f);
                droplets[dropletCount++] = new Bit
                {
                    P = point,
                    V = d * (back ? R(8f, 20f) : R(8f, 45f)),
                    Size = R(.05f, .18f),
                    Life = R(.6f, 1.6f),
                    Color = new Color(.32f * v, .02f, .03f, .95f),
                    Seed = (int)(R() * int.MaxValue),
                };
            }
            changed = true;
        }

        /// <summary>Dust where a bullet ends on a surface of <paramref name="normal"/>.</summary>
        public void DustAt(Vector3 point, Vector3 normal, Vector3 dir)
        {
            var reflected = Vector3.Reflect(dir, normal);
            for (int i = 0; i < DustPerWall; i++)
            {
                if (dustCount >= MaxDust) break;
                float grey = R(.55f, .8f);
                dust[dustCount++] = new Bit
                {
                    P = point,
                    V = (.5f * reflected + normal + .7f * RandomUnit()).normalized * R(5f, 25f),
                    Size = R(.06f, .2f),
                    Life = R(.3f, .7f),
                    Color = i < HotDust ? new Color(1f, .8f, .4f, 1f) : new Color(grey, grey, grey, 1f),
                };
            }
            changed = true;
        }

        /// <summary>A tracer from the muzzle to where the bullet ended; the oldest goes at the cap.</summary>
        public void TracerTo(Vector3 muzzle, Vector3 end)
        {
            int i = tracerCount < MaxTracers ? tracerCount++ : tracerNext++ % MaxTracers;
            tracers[i] = new Tracer { A = muzzle, B = end };
            changed = true;
        }

        /// <summary>A flash at <paramref name="point"/>, its quads on three axes at right angles, one along <paramref name="dir"/>.</summary>
        public void FlashAt(Vector3 point, Vector3 dir)
        {
            var z = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.forward;
            var x = Vector3.Cross(z, Mathf.Abs(z.y) < .9f ? Vector3.up : Vector3.right).normalized;
            float turn = R(0f, 2f * Mathf.PI);
            x = Mathf.Cos(turn) * x + Mathf.Sin(turn) * Vector3.Cross(z, x);
            int i = flashCount < MaxFlashes ? flashCount++ : flashNext++ % MaxFlashes;
            flashes[i] = new Flash { P = point, X = x, Y = Vector3.Cross(z, x), Z = z, Size = R(.5f, .8f) };
            changed = true;
        }

        // ------------------------------------------------------------ the step

        /// <summary>
        /// One step of <paramref name="dt"/> scene seconds. <paramref name="ground"/> gives the height of a point's ground, or null with no ground: a droplet then only fades.
        /// </summary>
        public void Step(float dt, float gravity, Func<float, float, float> ground)
        {
            if (dt <= 0f || !Any) return;
            clock += dt;
            float keep = Mathf.Max(0f, 1f - Drag * dt);
            int n = 0;
            for (int i = 0; i < dropletCount; i++)
            {
                var d = droplets[i];
                d.Age += dt;
                if (d.Age >= d.Life) continue;
                d.V.y -= gravity * dt;
                d.V *= keep;
                d.P += d.V * dt;
                if (ground != null)
                {
                    float y = ground(d.P.x, d.P.z);
                    if (d.P.y <= y)
                    {
                        d.P.y = y;
                        Land(d);
                        continue;
                    }
                }
                droplets[n++] = d;
            }
            dropletCount = n;
            n = 0;
            for (int i = 0; i < dustCount; i++)
            {
                var d = dust[i];
                d.Age += dt;
                if (d.Age >= d.Life) continue;
                d.V.y -= gravity * dt;
                d.V *= keep;
                d.P += d.V * dt;
                dust[n++] = d;
            }
            dustCount = n;
            n = 0;
            for (int i = 0; i < tracerCount; i++)
            {
                var t = tracers[i];
                t.Age += dt;
                if (t.Age < TracerLife) tracers[n++] = t;
            }
            tracerCount = n;
            n = 0;
            for (int i = 0; i < flashCount; i++)
            {
                var f = flashes[i];
                f.Age += dt;
                if (f.Age < FlashLife) flashes[n++] = f;
            }
            flashCount = n;
            StepSplats();
            changed = true;
        }

        // the oldest first: they fade in their last seconds and end
        void StepSplats()
        {
            int oldest = (splatNext - splatCount + MaxSplats) % MaxSplats;
            for (int i = 0, n = splatCount; i < n; i++)
            {
                int slot = (oldest + i) % MaxSplats;
                float left = splats[slot].Born + SplatLife - clock;
                if (left >= SplatFade) break;
                if (left <= 0f)
                {
                    Fade(slot, 0f);
                    splatCount--;
                    splatBoundsStale = true;
                }
                else Fade(slot, left / SplatFade);
            }
        }

        void Fade(int slot, float share)
        {
            if (vertices == null) return;
            byte a = (byte)(splats[slot].Color.a * share);
            int v = slot * SplatVertices;
            for (int i = 0; i < SplatVertices; i++) vertices[v + i].Color.a = a;
            Dirty(slot);
        }

        void Dirty(int slot)
        {
            splatDirtyFrom = Math.Min(splatDirtyFrom, slot);
            splatDirtyTo = Math.Max(splatDirtyTo, slot);
        }

        void Land(in Bit d)
        {
            int slot = splatNext;
            splatNext = (splatNext + 1) % MaxSplats;
            if (splatCount < MaxSplats) splatCount++;
            else SplatsPushed++;
            splatHigh = Math.Max(splatHigh, slot + 1);
            float size = d.Size * (3f + 4f * (float)WetMath.Rng(d.Seed, 0));
            var flat = new Vector2(d.V.x, d.V.z);
            float speed = flat.magnitude;
            var along = speed > 1e-4f ? flat / speed : new Vector2(1f, 0f);
            var p = d.P;
            p.y += SplatLift;
            splats[slot] = new Splat { P = p, Born = clock, Color = d.Color };
            Arrays();
            WriteSplat(slot, d.Seed, size, speed, along);
            splatBoundsStale = true;
        }

        /// <summary>
        /// The shape of a splat in its plane, x along the travel, y across, about its centre: an irregular outline of <see cref="SplatRim"/> points in <paramref name="rim"/>, and satellite
        /// drops in <paramref name="satellites"/> as (x, y, radius). Returns how many satellites. A function of its arguments.
        /// </summary>
        public static int SplatShape(int seed, float size, float speed, Vector2[] rim, Vector3[] satellites)
        {
            float stretch = 1f + Mathf.Clamp(speed / 25f, 0f, 1.2f);
            for (int k = 0; k < SplatRim; k++)
            {
                double a = k * 2 * Math.PI / SplatRim;
                float h = (float)WetMath.Rng(seed, 1 + k), r = size * (.7f + .45f * h);
                float x = r * (float)Math.Cos(a), y = r * (float)Math.Sin(a);
                rim[k] = new Vector2(x > 0f ? x * stretch : x, y * .85f);
            }
            int n = 2 + Math.Min(3, (int)(WetMath.Rng(seed, 20) * 4));
            for (int i = 0; i < n; i++)
            {
                float along = size * (1.3f + 1.8f * (float)WetMath.Rng(seed, 21 + 3 * i));
                float off = size * (-.7f + 1.4f * (float)WetMath.Rng(seed, 22 + 3 * i));
                float radius = size * (.08f + .17f * (float)WetMath.Rng(seed, 23 + 3 * i));
                satellites[i] = new Vector3(along, off, radius);
            }
            return n;
        }

        // ------------------------------------------------------------ reads for tests

        /// <summary>The world points of the outline of the <paramref name="i"/>-th splat from the oldest, with its centre first; false with no such splat or no mesh.</summary>
        public bool SplatOutline(int i, Vector3[] outline)
        {
            if (i < 0 || i >= splatCount || vertices == null) return false;
            int v = ((splatNext - splatCount + i + 2 * MaxSplats) % MaxSplats) * SplatVertices;
            for (int k = 0; k <= SplatRim; k++) outline[k] = vertices[v + k].Position;
            return true;
        }

        /// <summary>The satellites drawn on the <paramref name="i"/>-th splat from the oldest.</summary>
        public int SatellitesOf(int i)
        {
            if (i < 0 || i >= splatCount || vertices == null) return 0;
            int v = ((splatNext - splatCount + i + 2 * MaxSplats) % MaxSplats) * SplatVertices + SplatRim + 1, n = 0;
            for (int s = 0; s < MaxSatellites; s++, v += SatelliteVertices)
                if (vertices[v].Position != vertices[v + 1].Position) n++;
            return n;
        }

        /// <summary>The ground point of the <paramref name="i"/>-th splat from the oldest.</summary>
        public Vector3 SplatAt(int i) => splats[(splatNext - splatCount + i + 2 * MaxSplats) % MaxSplats].P;

        /// <summary>The place of the <paramref name="i"/>-th droplet.</summary>
        public Vector3 DropletAt(int i) => droplets[i].P;

        // ------------------------------------------------------------ drawing

        void WriteSplat(int slot, int seed, float size, float speed, Vector2 along)
        {
            ref var s = ref splats[slot];
            int v = slot * SplatVertices, sats = SplatShape(seed, size, speed, rimScratch, satScratch);
            var c = s.Color;
            var p = s.P;
            Vector3 World(float x, float y) => new Vector3(p.x + x * along.x - y * along.y, p.y, p.z + x * along.y + y * along.x);
            vertices[v] = new Packed { Position = p, Color = c };
            for (int k = 0; k < SplatRim; k++) vertices[v + 1 + k] = new Packed { Position = World(rimScratch[k].x, rimScratch[k].y), Color = c };
            v += SplatRim + 1;
            for (int i = 0; i < MaxSatellites; i++, v += SatelliteVertices)
            {
                if (i >= sats)
                {
                    for (int k = 0; k < SatelliteVertices; k++) vertices[v + k] = new Packed { Position = p };
                    continue;
                }
                var q = satScratch[i];
                vertices[v] = new Packed { Position = World(q.x, q.y), Color = c };
                for (int k = 0; k < HeadRim; k++) vertices[v + 1 + k] = new Packed { Position = World(q.x + q.z * Unit6[k].x, q.y + q.z * Unit6[k].y), Color = c };
            }
            Dirty(slot);
        }

        void SplatBounds()
        {
            splatBoundsStale = false;
            splatMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            splatMax = -splatMin;
            for (int i = 0; i < splatCount; i++)
            {
                var p = SplatAt(i);
                splatMin = Vector3.Min(splatMin, p);
                splatMax = Vector3.Max(splatMax, p);
            }
        }

        void WriteBit(ref Bit b, ref int v, float fadeFrom, bool tailed, ref Vector3 min, ref Vector3 max)
        {
            float left = (b.Life - b.Age) / (b.Life * fadeFrom);
            var c = b.Color;
            if (left < 1f) c.a = (byte)(c.a * left);
            float r = b.Size * .5f;
            ref var head = ref vertices[v];
            head.Position = b.P; head.Axis = default; head.Color = c; head.Offset = default;
            for (int k = 0; k < HeadRim; k++)
            {
                ref var q = ref vertices[v + 1 + k];
                q.Position = b.P; q.Axis = default; q.Color = c; q.Offset = Unit6[k] * r;
            }
            v += HeadRim + 1;
            ref var a0 = ref vertices[v];
            ref var a1 = ref vertices[v + 1];
            ref var a2 = ref vertices[v + 2];
            float speed = tailed ? b.V.magnitude : 0f, tail = TailPerSpeed * speed;
            if (tail > r)
            {
                var axis = b.V * (-1f / speed);
                var fade = c;
                fade.a = 0;
                a0.Position = b.P; a0.Axis = axis; a0.Color = c; a0.Offset = new Vector2(-r, 0f);
                a1.Position = b.P; a1.Axis = axis; a1.Color = c; a1.Offset = new Vector2(r, 0f);
                a2.Position = b.P; a2.Axis = axis; a2.Color = fade; a2.Offset = new Vector2(0f, tail);
            }
            else
            {
                a0.Position = a1.Position = a2.Position = b.P;
                a0.Axis = a1.Axis = a2.Axis = default;
                a0.Offset = a1.Offset = a2.Offset = default;
                a0.Color = a1.Color = a2.Color = default;
            }
            v += 3;
            min = Vector3.Min(min, b.P);
            max = Vector3.Max(max, b.P);
        }

        void WriteStrip(int v, Vector3 a, Vector3 b, Vector3 axis, float wa, float wb, Color32 c)
        {
            vertices[v] = new Packed { Position = a, Axis = axis, Color = c, Offset = new Vector2(-wa * .5f, 0f) };
            vertices[v + 1] = new Packed { Position = a, Axis = axis, Color = c, Offset = new Vector2(wa * .5f, 0f) };
            vertices[v + 2] = new Packed { Position = b, Axis = axis, Color = c, Offset = new Vector2(wb * .5f, 0f) };
            vertices[v + 3] = new Packed { Position = b, Axis = axis, Color = c, Offset = new Vector2(-wb * .5f, 0f) };
        }

        static readonly Color32 TracerColor = new Color(1f, .85f, .45f, .9f), CoreColor = new Color(1f, 1f, .9f, 1f), FlashColor = new Color(1f, .75f, .3f, 1f);

        void WriteFlashQuad(int v, Vector3 p, Vector3 axis, float s, Color32 c)
        {
            float h = s * .5f;
            vertices[v] = new Packed { Position = p, Axis = axis, Color = c, Offset = new Vector2(-h, -h) };
            vertices[v + 1] = new Packed { Position = p, Axis = axis, Color = c, Offset = new Vector2(h, -h) };
            vertices[v + 2] = new Packed { Position = p, Axis = axis, Color = c, Offset = new Vector2(h, h) };
            vertices[v + 3] = new Packed { Position = p, Axis = axis, Color = c, Offset = new Vector2(-h, h) };
        }

        /// <summary>The mesh of the moment; nothing when nothing changed. Not on a machine with no graphics device.</summary>
        public void Draw()
        {
            bool show = Any;
            if (renderer && renderer.enabled != show) renderer.enabled = show;
            if (!show) { changed = false; return; }
            if (!changed && splatDirtyTo < 0) return;
            changed = false;
            if (!Ensure()) return;
            const MeshUpdateFlags quiet = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds;
            if (splatDirtyTo >= 0)
            {
                int first = splatDirtyFrom * SplatVertices, count = (splatDirtyTo - splatDirtyFrom + 1) * SplatVertices;
                mesh.SetVertexBufferData(vertices, first, first, count, 0, quiet);
                splatDirtyFrom = int.MaxValue;
                splatDirtyTo = -1;
            }
            int v = SplatRegion;
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = -min;
            for (int i = 0; i < dropletCount; i++) WriteBit(ref droplets[i], ref v, .5f, true, ref min, ref max);
            for (int i = 0; i < dustCount; i++) WriteBit(ref dust[i], ref v, .5f, false, ref min, ref max);
            int bits = dropletCount + dustCount;
            for (int i = 0; i < tracerCount; i++, v += TracerVertices)
            {
                ref var t = ref tracers[i];
                var axis = t.B - t.A;
                WriteStrip(v, t.A, t.B, axis, TracerStart, TracerEnd, TracerColor);
                WriteStrip(v + 4, t.A, t.B, axis, TracerStart * TracerCore, TracerEnd * TracerCore, CoreColor);
                min = Vector3.Min(min, Vector3.Min(t.A, t.B));
                max = Vector3.Max(max, Vector3.Max(t.A, t.B));
            }
            for (int i = 0; i < flashCount; i++, v += FlashVertices)
            {
                ref var f = ref flashes[i];
                WriteFlashQuad(v, f.P, f.X, f.Size, FlashColor);
                WriteFlashQuad(v + 4, f.P, f.Y, f.Size, FlashColor);
                WriteFlashQuad(v + 8, f.P, f.Z, f.Size, FlashColor);
                min = Vector3.Min(min, f.P);
                max = Vector3.Max(max, f.P);
            }
            int idx = WriteIndices(bits);
            if (v > SplatRegion) mesh.SetVertexBufferData(vertices, SplatRegion, SplatRegion, v - SplatRegion, 0, quiet);
            mesh.SetIndexBufferData(indices, SplatIndexRegion, SplatIndexRegion, idx, quiet);
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, splatHigh * SplatIndices), quiet);
            mesh.SetSubMesh(1, new SubMeshDescriptor(SplatIndexRegion, idx), quiet);
            if (splatBoundsStale) SplatBounds();
            if (splatCount > 0)
            {
                min = Vector3.Min(min, splatMin);
                max = Vector3.Max(max, splatMax);
            }
            var b = new Bounds();
            b.SetMinMax(min, max);
            b.Expand(16f);
            mesh.bounds = b;
            DrawnIndices = splatHigh * SplatIndices + idx;
        }

        /// <summary>Indices drawn by the last <see cref="Draw"/> (tests).</summary>
        public int DrawnIndices { get; private set; }

        // bits first: their pattern never changes, so only what follows them does
        int WriteIndices(int bits)
        {
            if (indexedBits != bits)
            {
                for (int i = Math.Min(indexedBits, bits); i < bits; i++)
                {
                    int b = SplatRegion + i * BitVertices, o = SplatIndexRegion + i * BitIndices;
                    for (int k = 0; k < HeadRim; k++, o += 3)
                    {
                        indices[o] = b;
                        indices[o + 1] = b + 1 + k;
                        indices[o + 2] = b + 1 + (k + 1) % HeadRim;
                    }
                    indices[o] = b + HeadRim + 1; indices[o + 1] = b + HeadRim + 2; indices[o + 2] = b + HeadRim + 3;
                }
                indexedBits = bits;
            }
            int at = SplatIndexRegion + bits * BitIndices, vertex = SplatRegion + bits * BitVertices;
            for (int i = 0; i < tracerCount * 2; i++, vertex += 4) Quad(ref at, vertex);
            for (int i = 0; i < flashCount * 3; i++, vertex += 4) Quad(ref at, vertex);
            return at - SplatIndexRegion;
        }

        void Quad(ref int at, int v)
        {
            indices[at] = v; indices[at + 1] = v + 1; indices[at + 2] = v + 2;
            indices[at + 3] = v; indices[at + 4] = v + 2; indices[at + 5] = v + 3;
            at += 6;
        }

        // the splats' triangles stand for good: a slot is a fan of its outline and fans of its satellites
        void SplatPattern()
        {
            int o = 0;
            for (int s = 0; s < MaxSplats; s++)
            {
                int v = s * SplatVertices;
                for (int k = 0; k < SplatRim; k++, o += 3)
                {
                    indices[o] = v;
                    indices[o + 1] = v + 1 + k;
                    indices[o + 2] = v + 1 + (k + 1) % SplatRim;
                }
                v += SplatRim + 1;
                for (int i = 0; i < MaxSatellites; i++, v += SatelliteVertices)
                    for (int k = 0; k < HeadRim; k++, o += 3)
                    {
                        indices[o] = v;
                        indices[o + 1] = v + 1 + k;
                        indices[o + 2] = v + 1 + (k + 1) % HeadRim;
                    }
            }
        }

        void Arrays()
        {
            if (vertices != null) return;
            vertices = new Packed[VertexCount];
            indices = new int[IndexCount];
            SplatPattern();
            indexedBits = 0;
        }

        // the mesh and its renderer need a device; the lists do not
        bool Ensure()
        {
            Arrays();
            if (mesh) return true;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !root) return false;
            var s = Shader.Find(WetDrops.ShaderName);
            if (!s) return false;
            const MeshUpdateFlags quiet = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds;
            mesh = new Mesh { name = "HS2Next ragdoll effects", hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            mesh.SetVertexBufferParams(VertexCount, Layout);
            mesh.SetIndexBufferParams(IndexCount, IndexFormat.UInt32);
            mesh.subMeshCount = 2;
            mesh.SetIndexBufferData(indices, 0, 0, SplatIndexRegion, quiet);
            material = new Material(s) { name = "HS2Next ragdoll effects", hideFlags = HideFlags.HideAndDontSave };
            var go = new GameObject("ragdoll effects") { hideFlags = HideFlags.HideAndDontSave, layer = RenderingLayers.Chara };
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { material, material };
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.renderingLayerMask = RenderingLayers.ForLayer(RenderingLayers.Chara);
            if (splatHigh > 0) Dirty(0);
            if (splatHigh > 0) Dirty(splatHigh - 1);
            return true;
        }

        /// <summary>Everything goes, also the mesh: what a world's end and a new scene ask.</summary>
        public void Clear()
        {
            dropletCount = dustCount = tracerCount = flashCount = splatCount = 0;
            splatNext = splatHigh = tracerNext = flashNext = 0;
            splatDirtyFrom = int.MaxValue;
            splatDirtyTo = -1;
            DropletsLost = SplatsPushed = 0;
            changed = false;
            if (renderer) Destroy(renderer.gameObject);
            Destroy(mesh);
            Destroy(material);
            renderer = null;
            mesh = null;
            material = null;
            vertices = null;
            indices = null;
        }

        public void Dispose() => Clear();

        static void Destroy(Object o)
        {
            if (!o) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }
}
