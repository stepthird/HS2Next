// Double-precision values of the Timeline evaluator: a vector, a rotation quaternion and a colour. Euler angles follow
// the engine's convention: degrees, applied as z about the forward axis, then x about the right axis, then y about the up
// axis (q = qy * qx * qz). Rotations are blended on the shorter arc. No engine types: the evaluator runs without the engine.
using System;

namespace HS2Next.Scenes.Timeline
{
    /// <summary>A vector in double precision.</summary>
    public readonly struct TimelineVec3 : IEquatable<TimelineVec3>
    {
        public readonly double X, Y, Z;

        public TimelineVec3(double x, double y, double z) { X = x; Y = y; Z = z; }

        public static readonly TimelineVec3 Zero = new TimelineVec3(0, 0, 0);

        /// <summary>a + (b − a) · w per component, not clamped.</summary>
        public static TimelineVec3 Lerp(in TimelineVec3 a, in TimelineVec3 b, double w) =>
            new TimelineVec3(a.X + (b.X - a.X) * w, a.Y + (b.Y - a.Y) * w, a.Z + (b.Z - a.Z) * w);

        public bool Equals(TimelineVec3 o) => X.Equals(o.X) && Y.Equals(o.Y) && Z.Equals(o.Z);
        public override bool Equals(object obj) => obj is TimelineVec3 o && Equals(o);
        public override int GetHashCode() => (X.GetHashCode() * 31 + Y.GetHashCode()) * 31 + Z.GetHashCode();
        public override string ToString() => "[" + TimelineNumbers.Text(X) + ", " + TimelineNumbers.Text(Y) + ", " + TimelineNumbers.Text(Z) + "]";
    }

    /// <summary>A rotation quaternion in double precision (x, y, z vector part, w scalar part).</summary>
    public readonly struct TimelineQuat : IEquatable<TimelineQuat>
    {
        public readonly double X, Y, Z, W;

        public TimelineQuat(double x, double y, double z, double w) { X = x; Y = y; Z = z; W = w; }

        public static readonly TimelineQuat Identity = new TimelineQuat(0, 0, 0, 1);

        const double Deg = Math.PI / 180.0;

        /// <summary>
        /// The dot product (after the shorter-arc flip) from which <see cref="Interpolate"/> blends linearly and normalises
        /// instead of slerping: arcs below about 36.4°, as HS2 interpolates its rotation keys.
        /// </summary>
        public const double LerpFromDot = 0.95;

        public static TimelineQuat operator *(in TimelineQuat a, in TimelineQuat b) => new TimelineQuat(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        public static double Dot(in TimelineQuat a, in TimelineQuat b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

        public TimelineQuat Negated() => new TimelineQuat(-X, -Y, -Z, -W);

        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z + W * W);

        /// <summary>This rotation scaled to unit length (the identity for a zero quaternion).</summary>
        public TimelineQuat Normalized()
        {
            double n = Length;
            return n > 0 ? new TimelineQuat(X / n, Y / n, Z / n, W / n) : Identity;
        }

        /// <summary>The rotation of Euler angles in degrees (the engine's order: z, then x, then y).</summary>
        public static TimelineQuat FromEuler(double x, double y, double z)
        {
            double hx = x * Deg * 0.5, hy = y * Deg * 0.5, hz = z * Deg * 0.5;
            var qx = new TimelineQuat(Math.Sin(hx), 0, 0, Math.Cos(hx));
            var qy = new TimelineQuat(0, Math.Sin(hy), 0, Math.Cos(hy));
            var qz = new TimelineQuat(0, 0, Math.Sin(hz), Math.Cos(hz));
            return qy * qx * qz;
        }

        public static TimelineQuat FromEuler(in TimelineVec3 e) => FromEuler(e.X, e.Y, e.Z);

        /// <summary>
        /// The Timeline's rotation blend from <paramref name="a"/> toward <paramref name="b0"/> on the shorter arc: when the dot
        /// product is negative, −b (the same rotation) is used; then with d = a · b ≥ <see cref="LerpFromDot"/> the blend is
        /// normalise(a + (b − a) · w), otherwise the spherical interpolation. <paramref name="w"/> is not clamped.
        /// </summary>
        public static TimelineQuat Interpolate(in TimelineQuat a, in TimelineQuat b0, double w)
        {
            double d = Dot(a, b0);
            var b = d < 0 ? b0.Negated() : b0;
            if (d < 0) d = -d;
            if (d >= LerpFromDot)
                return new TimelineQuat(a.X + (b.X - a.X) * w, a.Y + (b.Y - a.Y) * w, a.Z + (b.Z - a.Z) * w, a.W + (b.W - a.W) * w).Normalized();
            return SlerpFlipped(a, b, w);
        }

        /// <summary>
        /// Spherical interpolation from <paramref name="a"/> toward <paramref name="b0"/> on the shorter arc (−b when the dot
        /// product is negative), at any arc (a normalised lerp only where the arc vanishes). <paramref name="w"/> is not clamped.
        /// </summary>
        public static TimelineQuat Slerp(in TimelineQuat a, in TimelineQuat b0, double w) => SlerpFlipped(a, Dot(a, b0) < 0 ? b0.Negated() : b0, w);

        static TimelineQuat SlerpFlipped(in TimelineQuat a, in TimelineQuat b, double w)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z, dw = a.W - b.W;
            double sx = a.X + b.X, sy = a.Y + b.Y, sz = a.Z + b.Z, sw = a.W + b.W;
            // The angle between the two 4-vectors from the lengths of their difference and sum (precise near 0).
            double theta = 2 * Math.Atan2(Math.Sqrt(dx * dx + dy * dy + dz * dz + dw * dw), Math.Sqrt(sx * sx + sy * sy + sz * sz + sw * sw));
            double s = Math.Sin(theta);
            if (s < 1e-12)
                return new TimelineQuat(a.X + (b.X - a.X) * w, a.Y + (b.Y - a.Y) * w, a.Z + (b.Z - a.Z) * w, a.W + (b.W - a.W) * w).Normalized();
            double wa = Math.Sin((1 - w) * theta) / s, wb = Math.Sin(w * theta) / s;
            return new TimelineQuat(a.X * wa + b.X * wb, a.Y * wa + b.Y * wb, a.Z * wa + b.Z * wb, a.W * wa + b.W * wb);
        }

        /// <summary>
        /// Euler angles in degrees, each in [0, 360), in the engine's order. Pitch (x) comes from the rotation matrix; at a
        /// pitch of ±90° the roll (z) is 0 and the yaw carries the whole turn about the vertical.
        /// </summary>
        public TimelineVec3 ToEuler()
        {
            var q = Normalized();
            double x = q.X, y = q.Y, z = q.Z, w = q.W;
            double m00 = 1 - 2 * (y * y + z * z), m01 = 2 * (x * y - z * w), m02 = 2 * (x * z + y * w);
            double m10 = 2 * (x * y + z * w), m11 = 1 - 2 * (x * x + z * z), m12 = 2 * (y * z - x * w);
            double m22 = 1 - 2 * (x * x + y * y);
            double sp = -m12, cp = Math.Sqrt(m10 * m10 + m11 * m11);
            double ex = Math.Atan2(sp, cp), ey, ez;
            if (cp > 1e-9)
            {
                ey = Math.Atan2(m02, m22);
                ez = Math.Atan2(m10, m11);
            }
            else
            {
                ey = sp > 0 ? Math.Atan2(m01, m00) : Math.Atan2(-m01, m00);
                ez = 0;
            }
            return new TimelineVec3(Wrap360(ex / Deg), Wrap360(ey / Deg), Wrap360(ez / Deg));
        }

        /// <summary>An angle in degrees mapped into [0, 360).</summary>
        public static double Wrap360(double deg)
        {
            double r = deg % 360.0;
            if (r < 0) r += 360.0;
            if (r >= 360.0) r -= 360.0;
            return r == 0 ? 0.0 : r;
        }

        /// <summary>The angle between two rotations in degrees (0 … 180).</summary>
        public static double AngleBetween(in TimelineQuat a, in TimelineQuat b)
        {
            var an = a.Normalized();
            var r = new TimelineQuat(-an.X, -an.Y, -an.Z, an.W) * b.Normalized();
            double v = Math.Sqrt(r.X * r.X + r.Y * r.Y + r.Z * r.Z);
            return 2 * Math.Atan2(v, Math.Abs(r.W)) / Deg;
        }

        public bool Equals(TimelineQuat o) => X.Equals(o.X) && Y.Equals(o.Y) && Z.Equals(o.Z) && W.Equals(o.W);
        public override bool Equals(object obj) => obj is TimelineQuat o && Equals(o);
        public override int GetHashCode() => ((X.GetHashCode() * 31 + Y.GetHashCode()) * 31 + Z.GetHashCode()) * 31 + W.GetHashCode();
        public override string ToString() => "Q(" + TimelineNumbers.Text(X) + ", " + TimelineNumbers.Text(Y) + ", " + TimelineNumbers.Text(Z) + ", " + TimelineNumbers.Text(W) + ")";
    }

    /// <summary>A colour as stored (sRGB-encoded r, g, b, straight alpha, not clamped) in double precision.</summary>
    public readonly struct TimelineColor : IEquatable<TimelineColor>
    {
        public readonly double R, G, B, A;

        public TimelineColor(double r, double g, double b, double a) { R = r; G = g; B = b; A = a; }

        /// <summary>a + (b − a) · w per component (alpha included), not clamped.</summary>
        public static TimelineColor Lerp(in TimelineColor a, in TimelineColor b, double w) =>
            new TimelineColor(a.R + (b.R - a.R) * w, a.G + (b.G - a.G) * w, a.B + (b.B - a.B) * w, a.A + (b.A - a.A) * w);

        public bool Equals(TimelineColor o) => R.Equals(o.R) && G.Equals(o.G) && B.Equals(o.B) && A.Equals(o.A);
        public override bool Equals(object obj) => obj is TimelineColor o && Equals(o);
        public override int GetHashCode() => ((R.GetHashCode() * 31 + G.GetHashCode()) * 31 + B.GetHashCode()) * 31 + A.GetHashCode();
        public override string ToString() => "RGBA(" + TimelineNumbers.Text(R) + ", " + TimelineNumbers.Text(G) + ", " + TimelineNumbers.Text(B) + ", " + TimelineNumbers.Text(A) + ")";
    }

    /// <summary>Number helpers of the Timeline.</summary>
    public static class TimelineNumbers
    {
        /// <summary>The shortest round-trip text of a double (invariant culture).</summary>
        public static string Text(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The time a stored key or event time stands for: the float widened to double.</summary>
        public static double Time(float t) => t;

        /// <summary>Two times are one key time when they are equal as floats (the stored precision).</summary>
        public static bool SameTime(double a, double b) => (float)a == (float)b;
    }
}
