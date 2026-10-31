// Typed reads from the MessagePack object trees of HS2's vanilla objects: a missing key
// keeps the MemberInit value the caller passes (the documents' absent values are those values), a value of the wrong type
// is reported and also keeps it, unknown keys are counted, fixed-length arrays are padded or cut, −0 and non-finite floats
// are counted (−0 is normalised to +0 here; the writer would do it anyway).
using System;
using System.Collections.Generic;
using HS2Next.IO;
using HS2Next.Save;

namespace HS2Next.Import
{
    sealed class RawReader
    {
        readonly ImportReport report;
        /// <summary>−0 values met in plugin data (vanilla objects are counted separately by <see cref="CountNegativeZeros"/>).</summary>
        public int PluginNegativeZeros;

        public RawReader(ImportReport report) { this.report = report; }

        static bool IsNumber(object o) => o is float || o is double || o is long || o is ulong;

        static float ToFloat(object o) => o is float f ? f : o is double d ? (float)d : o is long l ? l : o is ulong u ? u : 0f;

        public static Dictionary<object, object> Map(object o) => o as Dictionary<object, object>;

        public bool Has(object map, string key) => map is Dictionary<object, object> m && m.ContainsKey(key);

        bool TryGet(object map, string key, out object v)
        {
            v = null;
            return map is Dictionary<object, object> m && m.TryGetValue(key, out v);
        }

        void Bad(string path, string what)
        {
            report.Warning("card.badValue", path, what + "; MemberInit value used");
            report.Count("card.badValue");
        }

        /// <summary>A float32 as HS2 holds it: −0 → +0 (counted by the caller's census), NaN/±Infinity → default (reported).</summary>
        public float Finite(float f, float def, string path)
        {
            if (float.IsNaN(f) || float.IsInfinity(f))
            {
                report.Warning("float.nonFinite", path, "non-finite value replaced by the MemberInit value");
                report.Count("float.nonFinite");
                return def;
            }
            return f == 0f ? 0f : f;
        }

        /// <summary>A plugin float: −0 counted and normalised, non-finite reported.</summary>
        public float Plugin(float f, string path)
        {
            if (f == 0f)
            {
                if (BitConverter.ToInt32(BitConverter.GetBytes(f), 0) != 0) PluginNegativeZeros++;
                return 0f;
            }
            return Finite(f, 0f, path);
        }

        public float? PluginN(float? f, string path) => f.HasValue ? Plugin(f.Value, path) : (float?)null;

        public float F(object map, string key, float def, string path)
        {
            if (!TryGet(map, key, out var v)) return def;
            if (!IsNumber(v)) { Bad(path, "not a number"); return def; }
            return Finite(ToFloat(v), def, path);
        }

        public int I(object map, string key, int def, string path)
        {
            if (!TryGet(map, key, out var v)) return def;
            if (v is long l) return unchecked((int)l);
            if (v is ulong u) return unchecked((int)u);
            Bad(path, "not an integer");
            return def;
        }

        public bool B(object map, string key, bool def, string path)
        {
            if (!TryGet(map, key, out var v)) return def;
            if (v is bool b) return b;
            Bad(path, "not a bool");
            return def;
        }

        /// <summary>A string; nil and a missing key give <paramref name="def"/>.</summary>
        public string S(object map, string key, string def, string path)
        {
            if (!TryGet(map, key, out var v) || v == null) return def;
            if (v is string s) return s;
            Bad(path, "not a string");
            return def;
        }

        float[] Floats(object v, int n, string path, out bool ok)
        {
            ok = false;
            if (!(v is List<object> l) || l.Count < n) return null;
            var r = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (!IsNumber(l[i])) return null;
                r[i] = ToFloat(l[i]);
            }
            ok = true;
            return r;
        }

        public Color4 Col(object map, string key, Color4 def, string path)
        {
            if (!TryGet(map, key, out var v)) return def;
            var f = Floats(v, 4, path, out bool ok);
            if (!ok) { Bad(path, "not a colour (4 numbers)"); return def; }
            return new Color4(Finite(f[0], def.R, path), Finite(f[1], def.G, path), Finite(f[2], def.B, path), Finite(f[3], def.A, path));
        }

        public Vec4 V4(object map, string key, Vec4 def, string path)
        {
            if (!TryGet(map, key, out var v)) return def;
            var f = Floats(v, 4, path, out bool ok);
            if (!ok) { Bad(path, "not a Vector4"); return def; }
            return new Vec4(Finite(f[0], def.X, path), Finite(f[1], def.Y, path), Finite(f[2], def.Z, path), Finite(f[3], def.W, path));
        }

        public Vec3 V3(object map, string key, Vec3 def, string path)
        {
            if (!TryGet(map, key, out var v)) return def;
            return V3Value(v, def, path);
        }

        public Vec3 V3Value(object v, Vec3 def, string path)
        {
            var f = Floats(v, 3, path, out bool ok);
            if (!ok) { Bad(path, "not a Vector3"); return def; }
            return new Vec3(Finite(f[0], def.X, path), Finite(f[1], def.Y, path), Finite(f[2], def.Z, path));
        }

        /// <summary>A float array copied with its read length (shape sliders).</summary>
        public float[] FloatArray(object map, string key, float[] def, string path)
        {
            if (!TryGet(map, key, out var v)) return def;
            if (!(v is List<object> l)) { Bad(path, "not an array"); return def; }
            var r = new float[l.Count];
            for (int i = 0; i < r.Length; i++)
            {
                if (!IsNumber(l[i])) { Bad(path + "[" + i + "]", "not a number"); r[i] = i < def.Length ? def[i] : 0f; continue; }
                r[i] = Finite(ToFloat(l[i]), i < def.Length ? def[i] : 0f, path + "[" + i + "]");
            }
            return r;
        }

        /// <summary>
        /// The elements of a fixed-length array of objects: exactly <paramref name="n"/> slots; a missing key gives n
        /// nulls (MemberInit elements); a short array is padded with nulls (`card.arrayPadded`), a long one cut (`card.arrayCut`).
        /// </summary>
        public object[] Fixed(object map, string key, int n, string path)
        {
            var r = new object[n];
            if (!TryGet(map, key, out var v)) return r;
            if (!(v is List<object> l)) { Bad(path, "not an array"); return r; }
            if (l.Count < n) { report.Warning("card.arrayPadded", path, $"{l.Count} elements, {n} expected; padded with MemberInit elements"); report.Count("card.arrayPadded"); }
            else if (l.Count > n) { report.Warning("card.arrayCut", path, $"{l.Count} elements, {n} expected; cut"); report.Count("card.arrayCut"); }
            for (int i = 0; i < n && i < l.Count; i++) r[i] = l[i];
            return r;
        }

        /// <summary>Counts keys of <paramref name="map"/> that HS2's class does not have (`card.unknownKey`; ignored as HS2 does).</summary>
        public void CheckKeys(object map, HashSet<string> known, string path)
        {
            if (!(map is Dictionary<object, object> m)) return;
            foreach (var k in m.Keys)
            {
                if (k is string s && known.Contains(s)) continue;
                report.Info("card.unknownKey", path + "." + k, "key not in HS2's class; ignored");
                report.Count("card.unknownKey");
            }
        }

        /// <summary>Census of −0 floats in a vanilla object tree (every float, imported or not, as the spec's census counts).</summary>
        public static int CountNegativeZeros(object o)
        {
            switch (o)
            {
                case float f: return f == 0f && BitConverter.ToInt32(BitConverter.GetBytes(f), 0) != 0 ? 1 : 0;
                case double d: return d == 0d && BitConverter.DoubleToInt64Bits(d) != 0 ? 1 : 0;
                case List<object> l: { int n = 0; foreach (var x in l) n += CountNegativeZeros(x); return n; }
                case Dictionary<object, object> m: { int n = 0; foreach (var x in m.Values) n += CountNegativeZeros(x); return n; }
                default: return 0;
            }
        }

        public static HashSet<string> Keys(params string[] k) => new HashSet<string>(k, StringComparer.Ordinal);
    }
}
