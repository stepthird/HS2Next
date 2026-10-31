// Small double-precision vector / quaternion math of the route evaluator:
// Unity-free, so the evaluator runs under plain Mono. Conventions follow Unity's (left-handed, Euler angles in
// degrees applied Z, then X, then Y; Quaternion.Euler(x, y, z) = Y · X · Z).
using System;
using HS2Next.Save;

namespace HS2Next.Scenes.Routes
{
    /// <summary>A 3-vector of doubles (route space: units of 10 cm).</summary>
    public struct Vec3d : IEquatable<Vec3d>
    {
        public double X, Y, Z;

        public Vec3d(double x, double y, double z) { X = x; Y = y; Z = z; }

        public static readonly Vec3d Zero = new Vec3d(0, 0, 0);
        public static readonly Vec3d Up = new Vec3d(0, 1, 0);
        public static readonly Vec3d Forward = new Vec3d(0, 0, 1);

        public static Vec3d From(Vec3 v) => new Vec3d(v.X, v.Y, v.Z);
        public Vec3 ToVec3() => new Vec3((float)X, (float)Y, (float)Z);

        public static Vec3d operator +(Vec3d a, Vec3d b) => new Vec3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3d operator -(Vec3d a, Vec3d b) => new Vec3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3d operator -(Vec3d a) => new Vec3d(-a.X, -a.Y, -a.Z);
        public static Vec3d operator *(Vec3d a, double s) => new Vec3d(a.X * s, a.Y * s, a.Z * s);
        public static Vec3d operator *(double s, Vec3d a) => new Vec3d(a.X * s, a.Y * s, a.Z * s);

        public double SqrMagnitude => X * X + Y * Y + Z * Z;
        public double Magnitude => Math.Sqrt(X * X + Y * Y + Z * Z);

        public static double Dot(Vec3d a, Vec3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static Vec3d Cross(Vec3d a, Vec3d b) => new Vec3d(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public static double Distance(Vec3d a, Vec3d b) => (a - b).Magnitude;
        public static Vec3d Lerp(Vec3d a, Vec3d b, double t) => a + (b - a) * t;

        /// <summary>Unity's <c>Vector3 ==</c>: squared distance below 9.99999944e-11 (used where HS2 compares path points).</summary>
        public static bool ApproxEqual(Vec3d a, Vec3d b) => (a - b).SqrMagnitude < 9.99999944e-11;

        public bool IsFinite => !(double.IsNaN(X) || double.IsInfinity(X) || double.IsNaN(Y) || double.IsInfinity(Y) || double.IsNaN(Z) || double.IsInfinity(Z));

        public bool Equals(Vec3d o) => X.Equals(o.X) && Y.Equals(o.Y) && Z.Equals(o.Z);
        public override bool Equals(object obj) => obj is Vec3d o && Equals(o);
        public override int GetHashCode() => (X.GetHashCode() * 31 + Y.GetHashCode()) * 31 + Z.GetHashCode();
        public override string ToString() =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:R}, {1:R}, {2:R})", X, Y, Z);
    }

    /// <summary>A unit quaternion of doubles (x, y, z, w), Unity's conventions.</summary>
    public struct Quatd
    {
        public double X, Y, Z, W;

        public Quatd(double x, double y, double z, double w) { X = x; Y = y; Z = z; W = w; }

        public static readonly Quatd Identity = new Quatd(0, 0, 0, 1);

        public static Quatd operator *(Quatd a, Quatd b) => new Quatd(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        /// <summary>Rotation of <paramref name="deg"/> degrees about a unit axis.</summary>
        public static Quatd AngleAxis(double deg, Vec3d axis)
        {
            double h = deg * Math.PI / 360.0, s = Math.Sin(h);
            return new Quatd(axis.X * s, axis.Y * s, axis.Z * s, Math.Cos(h));
        }

        /// <summary>Unity's <c>Quaternion.Euler</c>: Z first, then X, then Y (q = Y · X · Z).</summary>
        public static Quatd Euler(double x, double y, double z) =>
            AngleAxis(y, new Vec3d(0, 1, 0)) * AngleAxis(x, new Vec3d(1, 0, 0)) * AngleAxis(z, new Vec3d(0, 0, 1));

        public static Quatd Euler(Vec3 e) => Euler(e.X, e.Y, e.Z);

        public Quatd Conjugate => new Quatd(-X, -Y, -Z, W);

        /// <summary>The inverse of a unit quaternion.</summary>
        public Quatd Inverse => Conjugate;

        /// <summary>Rotates a vector (q · v · q⁻¹).</summary>
        public Vec3d Rotate(Vec3d v)
        {
            var u = new Vec3d(X, Y, Z);
            var t = 2.0 * Vec3d.Cross(u, v);
            return v + W * t + Vec3d.Cross(u, t);
        }

        public override string ToString() =>
            string.Format(System.Globalization.CultureInfo.InvariantCulture, "Q({0:R}, {1:R}, {2:R}, {3:R})", X, Y, Z, W);
    }
}
