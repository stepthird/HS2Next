// Minimal MessagePack reader producing plain objects:
// null, bool, long, ulong, float, double, string, byte[], List<object>, Dictionary<object, object>,
// and MsgPackExt for ext values (type code + payload; e.g. LZ4 type 99, see HS2Next.Data.Ext.NestedMsgPack).
using System;
using System.Collections.Generic;
using System.Text;

namespace HS2Next.IO
{
    public static class MsgPack
    {
        public static object Read(byte[] data) { int p = 0; return Read(data, ref p); }

        public static object Read(byte[] d, ref int p)
        {
            byte b = d[p++];
            if (b <= 0x7f) return (long)b;
            if (b >= 0xe0) return (long)(sbyte)b;
            if ((b & 0xf0) == 0x80) return Map(d, ref p, b & 0x0f);
            if ((b & 0xf0) == 0x90) return Arr(d, ref p, b & 0x0f);
            if ((b & 0xe0) == 0xa0) return Str(d, ref p, b & 0x1f);
            switch (b)
            {
                case 0xc0: return null;
                case 0xc2: return false;
                case 0xc3: return true;
                case 0xc4: return Bin(d, ref p, d[p++]);
                case 0xc5: return Bin(d, ref p, U16(d, ref p));
                case 0xc6: return Bin(d, ref p, (int)U32(d, ref p));
                case 0xc7: { int n = d[p++]; return Ext(d, ref p, n); }
                case 0xc8: { int n = U16(d, ref p); return Ext(d, ref p, n); }
                case 0xc9: { int n = (int)U32(d, ref p); return Ext(d, ref p, n); }
                case 0xca: { var v = BitConverter.ToSingle(Rev(d, p, 4), 0); p += 4; return v; }
                case 0xcb: { var v = BitConverter.ToDouble(Rev(d, p, 8), 0); p += 8; return v; }
                case 0xcc: return (long)d[p++];
                case 0xcd: return (long)U16(d, ref p);
                case 0xce: return (long)U32(d, ref p);
                case 0xcf: { ulong v = BitConverter.ToUInt64(Rev(d, p, 8), 0); p += 8; return v <= long.MaxValue ? (object)(long)v : v; }
                case 0xd0: return (long)(sbyte)d[p++];
                case 0xd1: { var v = BitConverter.ToInt16(Rev(d, p, 2), 0); p += 2; return (long)v; }
                case 0xd2: { var v = BitConverter.ToInt32(Rev(d, p, 4), 0); p += 4; return (long)v; }
                case 0xd3: { var v = BitConverter.ToInt64(Rev(d, p, 8), 0); p += 8; return v; }
                case 0xd4: return Ext(d, ref p, 1);
                case 0xd5: return Ext(d, ref p, 2);
                case 0xd6: return Ext(d, ref p, 4);
                case 0xd7: return Ext(d, ref p, 8);
                case 0xd8: return Ext(d, ref p, 16);
                case 0xd9: return Str(d, ref p, d[p++]);
                case 0xda: return Str(d, ref p, U16(d, ref p));
                case 0xdb: return Str(d, ref p, (int)U32(d, ref p));
                case 0xdc: return Arr(d, ref p, U16(d, ref p));
                case 0xdd: return Arr(d, ref p, (int)U32(d, ref p));
                case 0xde: return Map(d, ref p, U16(d, ref p));
                case 0xdf: return Map(d, ref p, (int)U32(d, ref p));
            }
            throw new FormatException($"msgpack: unknown byte 0x{b:x2} at {p - 1}");
        }

        /// <summary>Advances <paramref name="p"/> past one value without building objects.</summary>
        public static void Skip(byte[] d, ref int p)
        {
            byte b = d[p++];
            if (b <= 0x7f || b >= 0xe0) return;
            if ((b & 0xf0) == 0x80) { SkipN(d, ref p, 2 * (b & 0x0f)); return; }
            if ((b & 0xf0) == 0x90) { SkipN(d, ref p, b & 0x0f); return; }
            if ((b & 0xe0) == 0xa0) { p += b & 0x1f; return; }
            switch (b)
            {
                case 0xc0: case 0xc2: case 0xc3: return;
                case 0xc4: case 0xd9: p += 1 + d[p]; return;
                case 0xc5: case 0xda: p += 2 + U16(d, p); return;
                case 0xc6: case 0xdb: p += 4 + (int)U32(d, p); return;
                case 0xc7: p += 2 + d[p]; return;
                case 0xc8: p += 3 + U16(d, p); return;
                case 0xc9: p += 5 + (int)U32(d, p); return;
                case 0xca: p += 4; return;
                case 0xcb: p += 8; return;
                case 0xcc: case 0xd0: p += 1; return;
                case 0xcd: case 0xd1: p += 2; return;
                case 0xce: case 0xd2: p += 4; return;
                case 0xcf: case 0xd3: p += 8; return;
                case 0xd4: p += 2; return;
                case 0xd5: p += 3; return;
                case 0xd6: p += 5; return;
                case 0xd7: p += 9; return;
                case 0xd8: p += 17; return;
                case 0xdc: { int n = U16(d, p); p += 2; SkipN(d, ref p, n); return; }
                case 0xdd: { int n = (int)U32(d, p); p += 4; SkipN(d, ref p, n); return; }
                case 0xde: { int n = U16(d, p); p += 2; SkipN(d, ref p, 2 * n); return; }
                case 0xdf: { int n = (int)U32(d, p); p += 4; SkipN(d, ref p, 2 * n); return; }
            }
            throw new FormatException($"msgpack: unknown byte 0x{b:x2} at {p - 1}");
        }

        static void SkipN(byte[] d, ref int p, int n) { for (int i = 0; i < n; i++) Skip(d, ref p); }
        static int U16(byte[] d, int p) => (d[p] << 8) | d[p + 1];
        static uint U32(byte[] d, int p) => ((uint)d[p] << 24) | ((uint)d[p + 1] << 16) | ((uint)d[p + 2] << 8) | d[p + 3];
        static MsgPackExt Ext(byte[] d, ref int p, int n) { sbyte t = (sbyte)d[p++]; return new MsgPackExt(t, Bin(d, ref p, n)); }

        static byte[] Rev(byte[] d, int p, int n)
        {
            var r = new byte[n];
            for (int i = 0; i < n; i++) r[i] = d[p + n - 1 - i];
            if (!BitConverter.IsLittleEndian) Array.Reverse(r);
            return r;
        }
        static int U16(byte[] d, ref int p) { int v = (d[p] << 8) | d[p + 1]; p += 2; return v; }
        static uint U32(byte[] d, ref int p) { uint v = ((uint)d[p] << 24) | ((uint)d[p + 1] << 16) | ((uint)d[p + 2] << 8) | d[p + 3]; p += 4; return v; }
        static string Str(byte[] d, ref int p, int n) { var s = Encoding.UTF8.GetString(d, p, n); p += n; return s; }
        static byte[] Bin(byte[] d, ref int p, int n) { var r = new byte[n]; Buffer.BlockCopy(d, p, r, 0, n); p += n; return r; }
        static List<object> Arr(byte[] d, ref int p, int n) { var l = new List<object>(n); for (int i = 0; i < n; i++) l.Add(Read(d, ref p)); return l; }
        static Dictionary<object, object> Map(byte[] d, ref int p, int n)
        {
            var m = new Dictionary<object, object>(n);
            for (int i = 0; i < n; i++) { var k = Read(d, ref p); m[k ?? "<nil>"] = Read(d, ref p); }
            return m;
        }
    }

    /// <summary>A MessagePack ext value: type code and raw payload.</summary>
    public sealed class MsgPackExt
    {
        public readonly sbyte Type;
        public readonly byte[] Data;
        public MsgPackExt(sbyte type, byte[] data) { Type = type; Data = data; }
        public override string ToString() => $"ext({Type}, {Data.Length} bytes)";
    }

    /// <summary>Convenience accessors over MsgPack object trees.</summary>
    public static class Node
    {
        public static object Get(this object o, string key)
        {
            if (o is Dictionary<object, object> m && m.TryGetValue(key, out var v)) return v;
            return null;
        }
        /// <summary>Map lookup by integer key (MessagePack ints are read as long).</summary>
        public static object Get(this object o, long key)
        {
            if (o is Dictionary<object, object> m && m.TryGetValue(key, out var v)) return v;
            return null;
        }
        public static bool Has(this object o, string key) => o is Dictionary<object, object> m && m.ContainsKey(key);
        public static object At(this object o, int i) => o is List<object> l && i < l.Count ? l[i] : null;
        public static int Count(this object o) => o is List<object> l ? l.Count : o is Dictionary<object, object> m ? m.Count : 0;
        public static int Int(this object o, int def = 0) => o is long l ? (int)l : o is ulong u ? (int)u : o is float f ? (int)f : o is double d ? (int)d : def;
        public static float Float(this object o, float def = 0) => o is float f ? f : o is double d ? (float)d : o is long l ? l : def;
        public static bool Bool(this object o, bool def = false) => o is bool b ? b : def;
        public static string Str(this object o, string def = null) => o as string ?? def;
        public static IEnumerable<object> Items(this object o) => o as List<object> ?? (IEnumerable<object>)Array.Empty<object>();
        public static UnityEngine.Color Color(this object o)
        {
            var l = o as List<object>;
            if (l == null || l.Count < 3) return UnityEngine.Color.white;
            return new UnityEngine.Color(l[0].Float(), l[1].Float(), l[2].Float(), l.Count > 3 ? l[3].Float() : 1);
        }
        public static UnityEngine.Vector4 Vec4(this object o)
        {
            var l = o as List<object>;
            if (l == null) return UnityEngine.Vector4.zero;
            return new UnityEngine.Vector4(l.Count > 0 ? l[0].Float() : 0, l.Count > 1 ? l[1].Float() : 0, l.Count > 2 ? l[2].Float() : 0, l.Count > 3 ? l[3].Float() : 0);
        }
        public static float[] Floats(this object o)
        {
            var l = o as List<object>;
            if (l == null) return Array.Empty<float>();
            var r = new float[l.Count];
            for (int i = 0; i < r.Length; i++) r[i] = l[i].Float();
            return r;
        }
    }
}
