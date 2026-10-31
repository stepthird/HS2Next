// JSON forms of UnityEngine value types for bridge arguments and replies:
// vectors [x, y(, z(, w))], quaternions [x, y, z, w], colours [r, g, b, a] in 0–1 (three numbers mean a = 1),
// Color32 as four integers 0–255, Vector2Int / Vector3Int as integers. Floats are written in round-trip form.
// Installed into BridgeJson by BridgeRuntime (and by the Editor tests).
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HS2Next.Bridge
{
    public static class UnityJsonConverters
    {
        static bool installed;

        /// <summary>Adds the converters to BridgeJson (idempotent).</summary>
        public static void Install()
        {
            if (installed) return;
            installed = true;
            BridgeJson.AddConverter(new Vector2Converter());
            BridgeJson.AddConverter(new Vector3Converter());
            BridgeJson.AddConverter(new Vector4Converter());
            BridgeJson.AddConverter(new QuaternionConverter());
            BridgeJson.AddConverter(new ColorConverter());
            BridgeJson.AddConverter(new Color32Converter());
            BridgeJson.AddConverter(new Vector2IntConverter());
            BridgeJson.AddConverter(new Vector3IntConverter());
        }

        internal static float[] ReadFloats(JsonReader reader, int min, int max, string expected)
        {
            var tok = JToken.ReadFrom(reader);
            if (!(tok is JArray a) || a.Count < min || a.Count > max) throw new JsonSerializationException("expected " + expected);
            var r = new float[a.Count];
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Type != JTokenType.Integer && a[i].Type != JTokenType.Float) throw new JsonSerializationException("expected " + expected);
                double d = (double)a[i];
                r[i] = (float)d;
                if (float.IsInfinity(r[i]) && !double.IsInfinity(d)) throw new JsonSerializationException("expected " + expected + " (float32 range)");
            }
            return r;
        }

        internal static int[] ReadInts(JsonReader reader, int n, string expected)
        {
            var tok = JToken.ReadFrom(reader);
            if (!(tok is JArray a) || a.Count != n) throw new JsonSerializationException("expected " + expected);
            var r = new int[n];
            for (int i = 0; i < n; i++)
            {
                if (a[i].Type != JTokenType.Integer) throw new JsonSerializationException("expected " + expected);
                long v = (long)a[i];
                if (v < int.MinValue || v > int.MaxValue) throw new JsonSerializationException("expected " + expected);
                r[i] = (int)v;
            }
            return r;
        }

        internal static void Write(JsonWriter w, params float[] v)
        {
            w.WriteStartArray();
            foreach (var x in v) w.WriteValue(x);
            w.WriteEndArray();
        }

        internal static void Write(JsonWriter w, params int[] v)
        {
            w.WriteStartArray();
            foreach (var x in v) w.WriteValue(x);
            w.WriteEndArray();
        }
    }

    sealed class Vector2Converter : JsonConverter<Vector2>
    {
        public override void WriteJson(JsonWriter w, Vector2 v, JsonSerializer s) => UnityJsonConverters.Write(w, v.x, v.y);
        public override Vector2 ReadJson(JsonReader r, Type t, Vector2 e, bool has, JsonSerializer s)
        {
            var f = UnityJsonConverters.ReadFloats(r, 2, 2, "[x, y]");
            return new Vector2(f[0], f[1]);
        }
    }

    sealed class Vector3Converter : JsonConverter<Vector3>
    {
        public override void WriteJson(JsonWriter w, Vector3 v, JsonSerializer s) => UnityJsonConverters.Write(w, v.x, v.y, v.z);
        public override Vector3 ReadJson(JsonReader r, Type t, Vector3 e, bool has, JsonSerializer s)
        {
            var f = UnityJsonConverters.ReadFloats(r, 3, 3, "[x, y, z]");
            return new Vector3(f[0], f[1], f[2]);
        }
    }

    sealed class Vector4Converter : JsonConverter<Vector4>
    {
        public override void WriteJson(JsonWriter w, Vector4 v, JsonSerializer s) => UnityJsonConverters.Write(w, v.x, v.y, v.z, v.w);
        public override Vector4 ReadJson(JsonReader r, Type t, Vector4 e, bool has, JsonSerializer s)
        {
            var f = UnityJsonConverters.ReadFloats(r, 4, 4, "[x, y, z, w]");
            return new Vector4(f[0], f[1], f[2], f[3]);
        }
    }

    sealed class QuaternionConverter : JsonConverter<Quaternion>
    {
        public override void WriteJson(JsonWriter w, Quaternion q, JsonSerializer s) => UnityJsonConverters.Write(w, q.x, q.y, q.z, q.w);
        public override Quaternion ReadJson(JsonReader r, Type t, Quaternion e, bool has, JsonSerializer s)
        {
            var f = UnityJsonConverters.ReadFloats(r, 4, 4, "[x, y, z, w]");
            return new Quaternion(f[0], f[1], f[2], f[3]);
        }
    }

    sealed class ColorConverter : JsonConverter<Color>
    {
        public override void WriteJson(JsonWriter w, Color c, JsonSerializer s) => UnityJsonConverters.Write(w, c.r, c.g, c.b, c.a);
        public override Color ReadJson(JsonReader r, Type t, Color e, bool has, JsonSerializer s)
        {
            var f = UnityJsonConverters.ReadFloats(r, 3, 4, "[r, g, b(, a)]");
            return new Color(f[0], f[1], f[2], f.Length > 3 ? f[3] : 1f);
        }
    }

    sealed class Color32Converter : JsonConverter<Color32>
    {
        public override void WriteJson(JsonWriter w, Color32 c, JsonSerializer s) => UnityJsonConverters.Write(w, c.r, c.g, c.b, c.a);
        public override Color32 ReadJson(JsonReader r, Type t, Color32 e, bool has, JsonSerializer s)
        {
            var v = UnityJsonConverters.ReadInts(r, 4, "[r, g, b, a] (0-255)");
            foreach (var x in v) if (x < 0 || x > 255) throw new JsonSerializationException("expected [r, g, b, a] (0-255)");
            return new Color32((byte)v[0], (byte)v[1], (byte)v[2], (byte)v[3]);
        }
    }

    sealed class Vector2IntConverter : JsonConverter<Vector2Int>
    {
        public override void WriteJson(JsonWriter w, Vector2Int v, JsonSerializer s) => UnityJsonConverters.Write(w, v.x, v.y);
        public override Vector2Int ReadJson(JsonReader r, Type t, Vector2Int e, bool has, JsonSerializer s)
        {
            var v = UnityJsonConverters.ReadInts(r, 2, "[x, y] (integers)");
            return new Vector2Int(v[0], v[1]);
        }
    }

    sealed class Vector3IntConverter : JsonConverter<Vector3Int>
    {
        public override void WriteJson(JsonWriter w, Vector3Int v, JsonSerializer s) => UnityJsonConverters.Write(w, v.x, v.y, v.z);
        public override Vector3Int ReadJson(JsonReader r, Type t, Vector3Int e, bool has, JsonSerializer s)
        {
            var v = UnityJsonConverters.ReadInts(r, 3, "[x, y, z] (integers)");
            return new Vector3Int(v[0], v[1], v[2]);
        }
    }
}
