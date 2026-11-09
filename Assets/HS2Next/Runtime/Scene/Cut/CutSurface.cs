// The surfaces of a cut, Unity-free, in double. A shape placed in the source's root space
// gives one signed value per face at each point p; with u, w, v the coordinates of p − c along the shape's X, Y, Z:
//   Plane  1 face   f = w                                            front keeps f > 0, back f ≤ 0
//   Box    6 faces  f_k = ±axis · (p − c) − half_k, k = +X, −X, +Y, −Y, +Z, −Z;   inside: every f_k ≤ 0
//   Tear   4 faces  f0 = v − hz·prof(u), f1 = −v − hz·prof(u), f2 = w − hy, f3 = −w − hy, prof = 1 − (u / hx)²
//   Hole   3 faces  f0 = (√((u / hx)² + (v / hz)²) − 1)·min(hx, hz), f1 = w − hy, f2 = −w − hy
// The outside of a box or an opening is the union over k of (f_k > 0 and f_j ≤ 0 for every j < k): pieces that do not overlap.
// Ragged: f_k += Rag(a, b, k) with (a, b) the face's in-plane coordinates (plane (u, v); box ±X (w, v), ±Y (u, v), ±Z (u, w);
// tear (u, w) times clamp01(3·prof(u)) so the tips stay pointed; hole (angle about Y × the mean half size, w)) and
//   s = max(0.001, tooth), x = a / s, y = b / s, r = randomness, A = amplitude,
//   wob = (N(0.31x, 0.31y) − 0.5)·1.4r, tri(t) = 1 − 4|frac(t) − 0.5|, zig = ½(tri(x + wob) + tri(y − 0.7wob)),
//   h = lerp(1, 0.3 + 1.4·H(⌊x + wob⌋, ⌊y − wob⌋), r), fine = 2·N(2.3x, 2.3y) − 1,
//   Rag = A·(zig·h·(1 − 0.4r) + 0.4r·fine);
// H(i, j, seed) is this file's integer hash to [0, 1] and N the value noise on its lattice with smoothstep interpolation; the
// seed of face k is seed·131 + k·7919, so both sides of one face use one pattern and the pieces mesh with each other.
using System;

namespace HS2Next.Scenes.Cut
{
    /// <summary>The ragged function and its hash.</summary>
    public static class CutRag
    {
        /// <summary>The seed of face k.</summary>
        public static int FaceSeed(int seed, int k) => unchecked(seed * 131 + k * 7919);

        static uint Mix(uint h)
        {
            unchecked
            {
                h ^= h >> 16; h *= 0x7feb352dU;
                h ^= h >> 15; h *= 0x846ca68bU;
                h ^= h >> 16;
                return h;
            }
        }

        /// <summary>The lattice hash H(i, j, seed) in [0, 1].</summary>
        public static double Hash(int i, int j, int seed)
        {
            unchecked
            {
                uint h = Mix((uint)seed * 0x9E3779B1U + 0x632BE5ABU);
                h = Mix(h ^ ((uint)i * 0x85EBCA77U));
                h = Mix(h ^ ((uint)j * 0xC2B2AE3DU));
                return (h >> 8) / 16777215.0;
            }
        }

        static double Smooth(double t) => t * t * (3 - 2 * t);

        /// <summary>Value noise N(x, y) on the hash's lattice, smoothstep interpolation, in [0, 1].</summary>
        public static double Noise(double x, double y, int seed)
        {
            double fx = Math.Floor(x), fy = Math.Floor(y);
            int i = (int)fx, j = (int)fy;
            double sx = Smooth(x - fx), sy = Smooth(y - fy);
            double a = Hash(i, j, seed), b = Hash(i + 1, j, seed), c = Hash(i, j + 1, seed), d = Hash(i + 1, j + 1, seed);
            double ab = a + (b - a) * sx, cd = c + (d - c) * sx;
            return ab + (cd - ab) * sy;
        }

        static double Tri(double t) => 1 - 4 * Math.Abs(t - Math.Floor(t) - 0.5);

        /// <summary>Rag(a, b) of one face: amp, tooth and randomness already in range; faceSeed from FaceSeed.</summary>
        public static double Rag(double a, double b, int faceSeed, double amp, double tooth, double random)
        {
            if (amp == 0) return 0;
            double s = Math.Max(0.001, tooth), x = a / s, y = b / s, r = random;
            double wob = (Noise(0.31 * x, 0.31 * y, faceSeed) - 0.5) * 1.4 * r;
            double zig = 0.5 * (Tri(x + wob) + Tri(y - 0.7 * wob));
            double h = 1 + (0.3 + 1.4 * Hash((int)Math.Floor(x + wob), (int)Math.Floor(y - wob), faceSeed) - 1) * r;
            double fine = 2 * Noise(2.3 * x, 2.3 * y, faceSeed) - 1;
            return amp * (zig * h * (1 - 0.4 * r) + 0.4 * r * fine);
        }
    }

    /// <summary>A shape placed in the source's root space with its ragged values applied.</summary>
    public sealed class CutSurface
    {
        public readonly CutKind Kind;
        public readonly CutVec C, Ex, Ey, Ez;
        /// <summary>Half sizes along X, Y, Z.</summary>
        public readonly double Hx, Hy, Hz;
        /// <summary>The ragged values in range (amp 0 for a clean cut).</summary>
        public readonly double Amp, Tooth, Random;
        public readonly int Seed;
        public readonly int FaceCount;
        readonly int[] faceSeeds;

        public CutSurface(CutKind kind, CutVec pos, CutQuat rot, CutVec size, CutRagParams ragged)
        {
            Kind = kind;
            C = pos;
            var q = rot.Normalized;
            Ex = q.Rotate(new CutVec(1, 0, 0));
            Ey = q.Rotate(new CutVec(0, 1, 0));
            Ez = q.Rotate(new CutVec(0, 0, 1));
            Hx = Math.Abs(size.X) * 0.5; Hy = Math.Abs(size.Y) * 0.5; Hz = Math.Abs(size.Z) * 0.5;
            if (ragged != null)
            {
                var a = ragged.Applied(out _);
                Amp = a.Amp; Tooth = a.Tooth; Random = a.Random; Seed = a.Seed;
            }
            FaceCount = kind switch { CutKind.Plane => 1, CutKind.Box => 6, CutKind.Tear => 4, CutKind.Hole => 3, _ => 0 };
            faceSeeds = new int[FaceCount];
            for (int k = 0; k < FaceCount; k++) faceSeeds[k] = CutRag.FaceSeed(Seed, k);
        }

        public static CutSurface Of(CutStepDef s) => new CutSurface(s.Kind, s.Pos, s.Rot, s.Size, s.Ragged);

        public bool Ragged => Amp > 0;
        /// <summary>The widening of the whole-mesh tests: 2 × amplitude + 0.02.</summary>
        public double Margin => 2 * Amp + 0.02;
        /// <summary>A plane splits by one face; a box and the openings by the union.</summary>
        public bool IsPlane => Kind == CutKind.Plane;
        public bool IsOpening => Kind == CutKind.Tear || Kind == CutKind.Hole;

        /// <summary>The shape's coordinates of p: u along X, w along Y, v along Z.</summary>
        public void Local(CutVec p, out double u, out double w, out double v)
        {
            var d = p - C;
            u = CutVec.Dot(d, Ex); w = CutVec.Dot(d, Ey); v = CutVec.Dot(d, Ez);
        }

        double Prof(double u) => Hx > 0 ? 1 - (u / Hx) * (u / Hx) : -1;

        /// <summary>Face k without the ragged term.</summary>
        public double Clean(int k, CutVec p)
        {
            Local(p, out double u, out double w, out double v);
            return Clean(k, u, w, v);
        }

        double Clean(int k, double u, double w, double v)
        {
            switch (Kind)
            {
                case CutKind.Plane: return w;
                case CutKind.Box:
                    switch (k)
                    {
                        case 0: return u - Hx;
                        case 1: return -u - Hx;
                        case 2: return w - Hy;
                        case 3: return -w - Hy;
                        case 4: return v - Hz;
                        default: return -v - Hz;
                    }
                case CutKind.Tear:
                    switch (k)
                    {
                        case 0: return v - Hz * Prof(u);
                        case 1: return -v - Hz * Prof(u);
                        case 2: return w - Hy;
                        default: return -w - Hy;
                    }
                case CutKind.Hole:
                    switch (k)
                    {
                        case 0:
                            {
                                double nx = Hx > 0 ? u / Hx : 1e9, nz = Hz > 0 ? v / Hz : 1e9;
                                return (Math.Sqrt(nx * nx + nz * nz) - 1) * Math.Min(Hx, Hz);
                            }
                        case 1: return w - Hy;
                        default: return -w - Hy;
                    }
                default: return 0;
            }
        }

        /// <summary>The ragged term of face k at p (0 for a clean cut).</summary>
        public double RagAt(int k, double u, double w, double v)
        {
            if (Amp <= 0) return 0;
            double a, b, taper = 1;
            switch (Kind)
            {
                case CutKind.Plane: a = u; b = v; break;
                case CutKind.Box:
                    if (k <= 1) { a = w; b = v; }
                    else if (k <= 3) { a = u; b = v; }
                    else { a = u; b = w; }
                    break;
                case CutKind.Tear:
                    a = u; b = w;
                    taper = Math.Min(1, Math.Max(0, 3 * Prof(u)));
                    break;
                case CutKind.Hole:
                    a = Math.Atan2(v, u) * 0.5 * (Hx + Hz); b = w;
                    break;
                default: return 0;
            }
            if (taper == 0) return 0;
            return taper * CutRag.Rag(a, b, faceSeeds[k], Amp, Tooth, Random);
        }

        /// <summary>Face k with the ragged term: the value the clip uses.</summary>
        public double Face(int k, CutVec p)
        {
            Local(p, out double u, out double w, out double v);
            return Clean(k, u, w, v) + RagAt(k, u, w, v);
        }

        /// <summary>Every face's value at p into f[o..o + FaceCount).</summary>
        public void Faces(CutVec p, double[] f, int o)
        {
            Local(p, out double u, out double w, out double v);
            for (int k = 0; k < FaceCount; k++) f[o + k] = Clean(k, u, w, v) + RagAt(k, u, w, v);
        }

        /// <summary>The unragged region value: the plane's f; for a box or an opening the largest face (≤ 0 inside).</summary>
        public double Region(CutVec p)
        {
            Local(p, out double u, out double w, out double v);
            if (Kind == CutKind.Plane) return w;
            double m = double.NegativeInfinity;
            for (int k = 0; k < FaceCount; k++) m = Math.Max(m, Clean(k, u, w, v));
            return m;
        }

        /// <summary>The region of a point with the ragged faces: for a plane 0 front (f > 0) or 1 back; for a box or an opening
        /// −1 inside (every f_k ≤ 0) or the outside piece k (f_k > 0 and f_j ≤ 0 for every j &lt; k).</summary>
        public int Classify(CutVec p)
        {
            Local(p, out double u, out double w, out double v);
            if (Kind == CutKind.Plane) return Clean(0, u, w, v) + RagAt(0, u, w, v) > 0 ? 0 : 1;
            for (int k = 0; k < FaceCount; k++)
                if (Clean(k, u, w, v) + RagAt(k, u, w, v) > 0) return k;
            return -1;
        }
    }
}
