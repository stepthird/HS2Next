// The padded uniform Catmull-Rom of HS2's route tween and its two length rules (
// 4), as mathematics: pad a path with one ghost point at each end (reflect the end spans, or wrap
// around when the first and last path points coincide), evaluate span k = min(floor(s·m), m − 1) at u = s·m − k with
// m = padded length − 3. HS2's segment length is measured on the padded array padded a second time, with
// N = 20 × (once-padded length) chords: a straight segment of length d measures 3d.
using System;

namespace HS2Next.Scenes.Routes
{
    public static class RouteSpline
    {
        /// <summary>
        /// One padding: <c>[g0, p0 … pn-1, gn]</c> with g0 = p0 + (p0 − p1), gn = pn-1 + (pn-1 − pn-2); when p0 and pn-1
        /// coincide (Unity's vector equality, <see cref="Vec3d.ApproxEqual"/>) the ghosts wrap around: g0 = pn-2, gn = p1.
        /// Needs at least two points.
        /// </summary>
        public static Vec3d[] Pad(Vec3d[] path)
        {
            if (path == null || path.Length < 2) throw new ArgumentException("a path needs at least two points");
            int n = path.Length;
            var a = new Vec3d[n + 2];
            Array.Copy(path, 0, a, 1, n);
            if (Vec3d.ApproxEqual(a[1], a[n]))
            {
                a[0] = a[n - 1];
                a[n + 1] = a[2];
            }
            else
            {
                a[0] = a[1] + (a[1] - a[2]);
                a[n + 1] = a[n] + (a[n] - a[n - 1]);
            }
            return a;
        }

        /// <summary>Uniform Catmull-Rom over a padded array at s ∈ [0, 1] (s is not clamped; s outside extrapolates the end spans).</summary>
        public static Vec3d Interp(Vec3d[] pts, double s)
        {
            int m = pts.Length - 3;
            int k = (int)Math.Floor(s * m);
            if (k > m - 1) k = m - 1;
            if (k < 0) k = 0;
            double u = s * m - k;
            return Span(pts[k], pts[k + 1], pts[k + 2], pts[k + 3], u);
        }

        /// <summary>The standard uniform Catmull-Rom segment between b and c at u ∈ [0, 1].</summary>
        public static Vec3d Span(Vec3d a, Vec3d b, Vec3d c, Vec3d d, double u)
        {
            double u2 = u * u, u3 = u2 * u;
            return 0.5 * ((-1 * a + 3 * b - 3 * c + d) * u3 + (2 * a - 5 * b + 4 * c - d) * u2 + (c - a) * u + 2 * b);
        }

        /// <summary>
        /// HS2's segment length (the `Hs2` timing rule, R3): the once-padded array <paramref name="padded"/> is padded again
        /// and sampled at i / N, N = 20 × padded.Length; the sum of the chords.
        /// </summary>
        public static double Hs2Length(Vec3d[] padded)
        {
            var w = Pad(padded);
            int m = w.Length - 3;
            // Cubic coefficients per span of the twice-padded array: C(u) = ((a·u + b)·u + c)·u + d.
            var co = new double[m * 12];
            for (int k = 0; k < m; k++)
            {
                Vec3d p0 = w[k], p1 = w[k + 1], p2 = w[k + 2], p3 = w[k + 3];
                int o = k * 12;
                co[o + 0] = 0.5 * (-p0.X + 3 * p1.X - 3 * p2.X + p3.X); co[o + 1] = 0.5 * (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X); co[o + 2] = 0.5 * (p2.X - p0.X); co[o + 3] = p1.X;
                co[o + 4] = 0.5 * (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y); co[o + 5] = 0.5 * (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y); co[o + 6] = 0.5 * (p2.Y - p0.Y); co[o + 7] = p1.Y;
                co[o + 8] = 0.5 * (-p0.Z + 3 * p1.Z - 3 * p2.Z + p3.Z); co[o + 9] = 0.5 * (2 * p0.Z - 5 * p1.Z + 4 * p2.Z - p3.Z); co[o + 10] = 0.5 * (p2.Z - p0.Z); co[o + 11] = p1.Z;
            }
            int n = padded.Length * 20;
            double len = 0, px = w[1].X, py = w[1].Y, pz = w[1].Z;
            for (int i = 1; i <= n; i++)
            {
                double s = (double)i / n;
                int k = (int)Math.Floor(s * m);
                if (k > m - 1) k = m - 1;
                double u = s * m - k;
                int o = k * 12;
                double x = ((co[o + 0] * u + co[o + 1]) * u + co[o + 2]) * u + co[o + 3];
                double y = ((co[o + 4] * u + co[o + 5]) * u + co[o + 6]) * u + co[o + 7];
                double z = ((co[o + 8] * u + co[o + 9]) * u + co[o + 10]) * u + co[o + 11];
                double dx = x - px, dy = y - py, dz = z - pz;
                len += Math.Sqrt(dx * dx + dy * dy + dz * dz);
                px = x; py = y; pz = z;
            }
            return len;
        }

        /// <summary>
        /// The arc length of the curve itself (the `Length` timing rule): Gauss-Legendre quadrature of |C'(u)| on every span
        /// (exact for straight equally spaced spans; well below 1e-9 relative for the curves of a route).
        /// </summary>
        public static double ArcLength(Vec3d[] padded)
        {
            int m = padded.Length - 3;
            double len = 0;
            for (int k = 0; k < m; k++)
            {
                Vec3d a = padded[k], b = padded[k + 1], c = padded[k + 2], d = padded[k + 3];
                // C'(u) = 0.5 · (3 A u² + 2 B u + Cc) with the cubic's coefficients.
                var A = -1 * a + 3 * b - 3 * c + d;
                var B = 2 * a - 5 * b + 4 * c - d;
                var Cc = c - a;
                const int pieces = 8;
                for (int j = 0; j < pieces; j++)
                {
                    double u0 = (double)j / pieces, h = 1.0 / pieces;
                    for (int g = 0; g < Gauss.Length; g += 2)
                    {
                        double u = u0 + h * (Gauss[g] + 1) / 2;
                        var dv = 0.5 * (3 * u * u * A + 2 * u * B + Cc);
                        len += Gauss[g + 1] * h / 2 * dv.Magnitude;
                    }
                }
            }
            return len;
        }

        // 5-point Gauss-Legendre: (node, weight) pairs on [-1, 1].
        static readonly double[] Gauss =
        {
            0.0, 0.5688888888888889,
            -0.5384693101056831, 0.4786286704993665, 0.5384693101056831, 0.4786286704993665,
            -0.9061798459386640, 0.2369268850561891, 0.9061798459386640, 0.2369268850561891,
        };
    }
}
