// Format-owned value types of the save documents: plain float
// structs without UnityEngine, so the format layer runs under plain Mono; the Phase 2.2 runtime
// model converts them to Vector2/3/4, Color and Quaternion. Equality is bitwise (float bits), which
// is what the round-trip tests compare; −0 and +0 therefore differ here although JSON maps −0 to +0.
using System;

namespace HS2Next.Save
{
    static class FloatBits
    {
        public static int Of(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0);
        public static bool Same(float a, float b) => Of(a) == Of(b);
    }

    /// <summary>`v2`: (x, y).</summary>
    public struct Vec2 : IEquatable<Vec2>
    {
        public float X, Y;
        public Vec2(float x, float y) { X = x; Y = y; }
        public bool Equals(Vec2 o) => FloatBits.Same(X, o.X) && FloatBits.Same(Y, o.Y);
        public override bool Equals(object obj) => obj is Vec2 o && Equals(o);
        public override int GetHashCode() => FloatBits.Of(X) * 31 + FloatBits.Of(Y);
        public override string ToString() => $"({X}, {Y})";
    }

    /// <summary>`v3` and `euler` (Euler degrees, Unity Z-X-Y order, never normalised).</summary>
    public struct Vec3 : IEquatable<Vec3>
    {
        public float X, Y, Z;
        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static readonly Vec3 Zero = new Vec3(0, 0, 0);
        public static readonly Vec3 One = new Vec3(1, 1, 1);
        public bool Equals(Vec3 o) => FloatBits.Same(X, o.X) && FloatBits.Same(Y, o.Y) && FloatBits.Same(Z, o.Z);
        public override bool Equals(object obj) => obj is Vec3 o && Equals(o);
        public override int GetHashCode() => (FloatBits.Of(X) * 31 + FloatBits.Of(Y)) * 31 + FloatBits.Of(Z);
        public override string ToString() => $"({X}, {Y}, {Z})";
    }

    /// <summary>`v4`: (x, y, z, w).</summary>
    public struct Vec4 : IEquatable<Vec4>
    {
        public float X, Y, Z, W;
        public Vec4(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
        public bool Equals(Vec4 o) => FloatBits.Same(X, o.X) && FloatBits.Same(Y, o.Y) && FloatBits.Same(Z, o.Z) && FloatBits.Same(W, o.W);
        public override bool Equals(object obj) => obj is Vec4 o && Equals(o);
        public override int GetHashCode() => ((FloatBits.Of(X) * 31 + FloatBits.Of(Y)) * 31 + FloatBits.Of(Z)) * 31 + FloatBits.Of(W);
        public override string ToString() => $"({X}, {Y}, {Z}, {W})";
    }

    /// <summary>`col`: [r, g, b, a] as entered (sRGB-encoded rgb, straight alpha, no clamping).</summary>
    public struct Color4 : IEquatable<Color4>
    {
        public float R, G, B, A;
        public Color4(float r, float g, float b, float a) { R = r; G = g; B = b; A = a; }
        public static readonly Color4 White = new Color4(1, 1, 1, 1);
        public static readonly Color4 Black = new Color4(0, 0, 0, 1);
        public bool Equals(Color4 o) => FloatBits.Same(R, o.R) && FloatBits.Same(G, o.G) && FloatBits.Same(B, o.B) && FloatBits.Same(A, o.A);
        public override bool Equals(object obj) => obj is Color4 o && Equals(o);
        public override int GetHashCode() => ((FloatBits.Of(R) * 31 + FloatBits.Of(G)) * 31 + FloatBits.Of(B)) * 31 + FloatBits.Of(A);
        public override string ToString() => $"RGBA({R}, {G}, {B}, {A})";
    }

    /// <summary>`quat`: [x, y, z, w], captured poses only; not required to be unit length.</summary>
    public struct Quat4 : IEquatable<Quat4>
    {
        public float X, Y, Z, W;
        public Quat4(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
        public static readonly Quat4 Identity = new Quat4(0, 0, 0, 1);
        public bool Equals(Quat4 o) => FloatBits.Same(X, o.X) && FloatBits.Same(Y, o.Y) && FloatBits.Same(Z, o.Z) && FloatBits.Same(W, o.W);
        public override bool Equals(object obj) => obj is Quat4 o && Equals(o);
        public override int GetHashCode() => ((FloatBits.Of(X) * 31 + FloatBits.Of(Y)) * 31 + FloatBits.Of(Z)) * 31 + FloatBits.Of(W);
        public override string ToString() => $"Q({X}, {Y}, {Z}, {W})";
    }
}
