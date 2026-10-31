// Value conversions between the model's field types (bool, int, float, string, Vec2 / Vec3 / Vec4, Color4, float[]) and
// the widgets' types (double, bool, string, Vector2 / Vector3 / Vector4, Color): FieldSchema.Coerce accepts only the
// exact model types (a double is not a float), so every UI write passes through ToModel with the slot's leaf type.
using System;
using System.Globalization;
using HS2Next.Save;
using HS2Next.Scenes;
using UnityEngine;

namespace HS2Next.UI.Binding
{
    public static class FieldValues
    {
        /// <summary>Equality used for mixed values (floats within 1e-6, vectors / colours per component).</summary>
        public static bool Same(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (IsNumber(a) && IsNumber(b)) return Math.Abs(ToDouble(a) - ToDouble(b)) <= 1e-6;
            if (a is float[] fa && b is float[] fb)
            {
                if (fa.Length != fb.Length) return false;
                for (int i = 0; i < fa.Length; i++) if (Math.Abs(fa[i] - fb[i]) > 1e-6f) return false;
                return true;
            }
            if (TryVector(a, out var va, out int na) && TryVector(b, out var vb, out int nb))
                return na == nb && (va - vb).sqrMagnitude <= 1e-12f;
            return a.Equals(b);
        }

        public static bool IsNumber(object v) => v is float || v is double || v is int || v is long || v is short || v is byte;

        public static double ToDouble(object v)
        {
            switch (v)
            {
                case null: return 0;
                case float f: return f;
                case double d: return d;
                case int i: return i;
                case long l: return l;
                case bool b: return b ? 1 : 0;
                case string s: return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : 0;
                default: return Convert.ToDouble(v, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Vec2 / Vec3 / Vec4 / Color4 / Quat4 / float[] / Unity vectors / Color → Vector4 + component count.</summary>
        public static bool TryVector(object v, out Vector4 r, out int n)
        {
            switch (v)
            {
                case Vec2 a: r = new Vector4(a.X, a.Y, 0, 0); n = 2; return true;
                case Vec3 a: r = new Vector4(a.X, a.Y, a.Z, 0); n = 3; return true;
                case Vec4 a: r = new Vector4(a.X, a.Y, a.Z, a.W); n = 4; return true;
                case Color4 c: r = new Vector4(c.R, c.G, c.B, c.A); n = 4; return true;
                case Quat4 q: r = new Vector4(q.X, q.Y, q.Z, q.W); n = 4; return true;
                case Vector2 a: r = a; n = 2; return true;
                case Vector3 a: r = a; n = 3; return true;
                case Vector4 a: r = a; n = 4; return true;
                case Color c: r = new Vector4(c.r, c.g, c.b, c.a); n = 4; return true;
                case float[] f when f.Length >= 1 && f.Length <= 4:
                    r = new Vector4(f[0], f.Length > 1 ? f[1] : 0, f.Length > 2 ? f[2] : 0, f.Length > 3 ? f[3] : 0); n = f.Length; return true;
            }
            r = default; n = 0;
            return false;
        }

        /// <summary>One component of a vector / colour value (null when the value is not one).</summary>
        public static object Component(object v, int i) => TryVector(v, out var r, out int n) && i >= 0 && i < n ? (object)r[i] : null;

        /// <summary>The value with component <paramref name="i"/> replaced (same type as <paramref name="whole"/>).</summary>
        public static object WithComponent(object whole, int i, double value)
        {
            if (!TryVector(whole, out var r, out int n) || i < 0 || i >= n) return whole;
            r[i] = (float)value;
            return FromVector(r, whole.GetType(), n);
        }

        static object FromVector(Vector4 r, Type t, int n)
        {
            if (t == typeof(Vec2)) return new Vec2(r.x, r.y);
            if (t == typeof(Vec3) || t == typeof(Vec3?)) return new Vec3(r.x, r.y, r.z);
            if (t == typeof(Vec4)) return new Vec4(r.x, r.y, r.z, r.w);
            if (t == typeof(Color4)) return new Color4(r.x, r.y, r.z, r.w);
            if (t == typeof(Quat4)) return new Quat4(r.x, r.y, r.z, r.w);
            if (t == typeof(Vector2)) return (Vector2)r;
            if (t == typeof(Vector3)) return (Vector3)r;
            if (t == typeof(Vector4)) return r;
            if (t == typeof(Color)) return new Color(r.x, r.y, r.z, r.w);
            if (t == typeof(float[])) { var f = new float[n]; for (int k = 0; k < n; k++) f[k] = r[k]; return f; }
            return null;
        }

        /// <summary>A widget value converted to the model's slot type (FieldAccessor.LeafType). Null when it cannot be.</summary>
        public static object ToModel(object v, Type leaf)
        {
            if (leaf == null) return v;
            var t = Nullable.GetUnderlyingType(leaf) ?? leaf;
            if (v == null) return null;
            if (t == typeof(float)) return (float)ToDouble(v);
            if (t == typeof(int)) return (int)Math.Round(ToDouble(v), MidpointRounding.AwayFromZero);
            if (t == typeof(bool)) return v is bool b ? b : ToDouble(v) != 0;
            if (t == typeof(string)) return v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);
            if (TryVector(v, out var r, out int n)) return FromVector(r, t, n) ?? v;
            return v;
        }

        /// <summary>The model value as a Unity colour (Color4 → Color; null otherwise).</summary>
        public static Color? ToColor(object v) => TryVector(v, out var r, out int n) && n == 4 ? new Color(r.x, r.y, r.z, r.w) : (Color?)null;

        public static Vector3? ToVector3(object v) => TryVector(v, out var r, out int n) && n >= 3 ? (Vector3)r : (Vector3?)null;

        /// <summary>Invariant short display of a number ("0.#####" by default).</summary>
        public static string Format(double d, string format = null) => d.ToString(format ?? "0.#####", CultureInfo.InvariantCulture);
    }
}
