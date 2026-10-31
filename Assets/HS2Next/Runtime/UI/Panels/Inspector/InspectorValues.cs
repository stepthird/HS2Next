// Value conversions of the inspectors: widget values (double, bool, string, double[],
// Unity vectors / colours) to the model's value types (float, int, Vec3, Vec4, Color4, FileRef) and back, reads of a row's
// value on one target through FieldAccessor (component rows, face list positions), and mixed-value detection over several
// targets. Unity types appear only as accepted inputs.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using UnityEngine;

namespace HS2Next.UI.Panels
{
    public static class InspectorValues
    {
        // ------------------------------------------------------------ conversions

        public static double D(object v)
        {
            switch (v)
            {
                case null: return 0;
                case double d: return d;
                case float f: return f;
                case int i: return i;
                case long l: return l;
                case bool b: return b ? 1 : 0;
                case string s: return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : 0;
                default: return Convert.ToDouble(v, CultureInfo.InvariantCulture);
            }
        }

        public static float F(object v) => (float)D(v);
        public static int I(object v) => (int)Math.Round(D(v), MidpointRounding.AwayFromZero);
        public static bool B(object v) => v is bool b ? b : D(v) != 0;

        static double[] Arr(object v)
        {
            switch (v)
            {
                case double[] d: return d;
                case float[] f: { var a = new double[f.Length]; for (int i = 0; i < f.Length; i++) a[i] = f[i]; return a; }
                case int[] n: { var a = new double[n.Length]; for (int i = 0; i < n.Length; i++) a[i] = n[i]; return a; }
                case IList l when !(v is string):
                {
                    var a = new double[l.Count];
                    for (int i = 0; i < l.Count; i++) a[i] = D(l[i]);
                    return a;
                }
                default: return null;
            }
        }

        public static Vec3 V3(object v)
        {
            switch (v)
            {
                case Vec3 x: return x;
                case Vector3 u: return new Vec3(u.x, u.y, u.z);
                case Vector4 u4: return new Vec3(u4.x, u4.y, u4.z);
            }
            var a = Arr(v);
            return a != null && a.Length >= 3 ? new Vec3((float)a[0], (float)a[1], (float)a[2]) : Vec3.Zero;
        }

        public static Vec4 V4(object v)
        {
            switch (v)
            {
                case Vec4 x: return x;
                case Vector4 u: return new Vec4(u.x, u.y, u.z, u.w);
            }
            var a = Arr(v);
            return a != null && a.Length >= 4 ? new Vec4((float)a[0], (float)a[1], (float)a[2], (float)a[3]) : new Vec4(0f, 0f, 0f, 0f);
        }

        public static Color4 C4(object v)
        {
            switch (v)
            {
                case Color4 c: return c;
                case Color u: return new Color4(u.r, u.g, u.b, u.a);
            }
            var a = Arr(v);
            if (a == null || a.Length < 3) return Color4.White;
            return new Color4((float)a[0], (float)a[1], (float)a[2], a.Length > 3 ? (float)a[3] : 1f);
        }

        public static Vector3 U3(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        /// <summary>Component <paramref name="k"/> of a vector / colour value (Vec3, Vec4, Color4, float[]).</summary>
        public static double Component(object v, int k)
        {
            switch (v)
            {
                case Vec3 x: return k == 0 ? x.X : k == 1 ? x.Y : x.Z;
                case Vec4 x: return k == 0 ? x.X : k == 1 ? x.Y : k == 2 ? x.Z : x.W;
                case Color4 c: return k == 0 ? c.R : k == 1 ? c.G : k == 2 ? c.B : c.A;
            }
            var a = Arr(v);
            return a != null && k >= 0 && k < a.Length ? a[k] : 0;
        }

        /// <summary><paramref name="v"/> with component <paramref name="k"/> replaced (the value type kept).</summary>
        public static object WithComponent(object v, int k, double x)
        {
            float f = (float)x;
            switch (v)
            {
                case Vec3 a: return new Vec3(k == 0 ? f : a.X, k == 1 ? f : a.Y, k == 2 ? f : a.Z);
                case Vec4 a: return new Vec4(k == 0 ? f : a.X, k == 1 ? f : a.Y, k == 2 ? f : a.Z, k == 3 ? f : a.W);
                case Color4 a: return new Color4(k == 0 ? f : a.R, k == 1 ? f : a.G, k == 2 ? f : a.B, k == 3 ? f : a.A);
                case float[] a: { var c = (float[])a.Clone(); if (k >= 0 && k < c.Length) c[k] = f; return c; }
                case int[] a: { var c = (int[])a.Clone(); if (k >= 0 && k < c.Length) c[k] = (int)Math.Round(x); return c; }
            }
            return v;
        }

        /// <summary>A widget value converted to the model type <paramref name="t"/> (null stays null).</summary>
        public static object To(Type t, object v)
        {
            if (v == null) return null;
            var u = Nullable.GetUnderlyingType(t) ?? t;
            if (u == typeof(float)) return F(v);
            if (u == typeof(double)) return D(v);
            if (u == typeof(int)) return I(v);
            if (u == typeof(bool)) return B(v);
            if (u == typeof(string)) return v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);
            if (u == typeof(Vec3)) return V3(v);
            if (u == typeof(Vec4)) return V4(v);
            if (u == typeof(Color4)) return C4(v);
            // a picked path → a file reference by the one converter of every image picker (Phase 3.6 package C,
            // DefaultData referenced, others embedded); a path it cannot convert stays a string
            // (the op then refuses the type with its message)
            if (u == typeof(FileRef)) return v as FileRef ?? (v is string s && s.Length > 0 ? (FileRefs.TryFromPicked(s, null, null, out var fr, out _) ? fr : (object)s) : null);
            return v;
        }

        // ------------------------------------------------------------ reads

        /// <summary>The stored value of a row (expanded path) on one target (null id = the scene); null when it does not resolve.</summary>
        public static object Raw(SceneModel m, string id, string path)
        {
            if (m == null || path == null) return null;
            return FieldAccessor.TryGet(m, id, path, out var v, out _) && !FieldValue.IsAbsent(v) ? v : null;
        }

        /// <summary>
        /// The value a widget shows for <paramref name="f"/> (path expanded by the caller) on one target: the stored value, the
        /// component of a component row, the list position of the face eyebrow / eyes ids.
        /// </summary>
        public static object Shown(StudioOps ops, FieldSpec f, string path, string id)
        {
            var v = Raw(ops?.Model, f.Target == FieldTarget.Scene ? null : id, path);
            if (v == null) return null;
            if (f.Component >= 0) return Component(v, f.Component);
            if (path == "chara.face.eyebrow" || path == "chara.face.eyes")
            {
                var n = ops.Model.Find(id);
                var lists = n != null ? ops.ListsOf(n) : null;
                var keys = path.EndsWith("eyebrow", StringComparison.Ordinal) ? lists?.Eyebrow : lists?.Eyes;
                int pos = keys != null ? Array.IndexOf(keys, I(v)) : -1;
                return pos >= 0 ? pos : I(v);
            }
            return v;
        }

        /// <summary>The shown values on every target; <paramref name="mixed"/> when they differ (canonical text compare).</summary>
        public static object ShownMany(StudioOps ops, FieldSpec f, string path, IReadOnlyList<string> ids, out bool mixed)
        {
            mixed = false;
            object first = null;
            string firstText = null;
            bool any = false;
            foreach (var id in ids)
            {
                var v = Shown(ops, f, path, id);
                string t = Text(v);
                if (!any) { first = v; firstText = t; any = true; continue; }
                if (!string.Equals(t, firstText, StringComparison.Ordinal)) { mixed = true; break; }
            }
            return first;
        }

        /// <summary>A canonical text of a value for comparisons.</summary>
        public static string Text(object v)
        {
            if (v == null) return "null";
            if (v is double || v is float || v is int || v is bool || v is string) return Convert.ToString(v, CultureInfo.InvariantCulture);
            try { return FieldJson.ToText(v); } catch (Exception) { return v.ToString(); }
        }
    }
}
