// Minimal bounds-checked MessagePack reader for the ExtendedSave trailer of pose files (Unity-free; the shared
// Runtime/IO/MsgPack.cs has UnityEngine helpers and no bounds checks). Values decode to plain objects: null, bool,
// long, ulong, float (float32), double (float64), string, byte[] (bin), List<object>, Dictionary<object, object>
// and PoseMsgPackExt. Every value also has an exact byte span, so callers keep raw slices and re-emit them unchanged.
// Malformed input raises PoseMsgPackException (an internal type that the trailer decoder catches).
using System;
using System.Collections.Generic;
using System.Text;

namespace HS2Next.Import.Pose
{
    /// <summary>A MessagePack ext value: type code and payload.</summary>
    public sealed class PoseMsgPackExt
    {
        public readonly sbyte Type;
        public readonly byte[] Data;
        public PoseMsgPackExt(sbyte type, byte[] data) { Type = type; Data = data; }
        public override string ToString() => "ext(" + Type + ", " + Data.Length + " bytes)";
    }

    /// <summary>Malformed MessagePack (offset relative to the decoded buffer).</summary>
    public sealed class PoseMsgPackException : Exception
    {
        public int Offset { get; }
        public PoseMsgPackException(int offset, string message) : base("msgpack at " + offset + ": " + message) { Offset = offset; }
    }

    /// <summary>One map entry as byte spans [start, end) of the source buffer.</summary>
    public struct PoseMsgPackSpan
    {
        public int KeyStart, KeyEnd, ValueStart, ValueEnd;
    }

    public static class PoseMsgPack
    {
        const int MaxDepth = 64;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, false);

        /// <summary>Position after the value at <paramref name="p"/>.</summary>
        public static int Skip(byte[] d, int p, int end) => Skip(d, p, end, 0);

        static int Skip(byte[] d, int p, int end, int depth)
        {
            if (depth > MaxDepth) throw new PoseMsgPackException(p, "nesting deeper than " + MaxDepth);
            Need(p, 1, end);
            byte b = d[p++];
            if (b <= 0x7f || b >= 0xe0) return p;
            if ((b & 0xf0) == 0x80) return SkipN(d, p, end, 2 * (b & 0x0f), depth);
            if ((b & 0xf0) == 0x90) return SkipN(d, p, end, b & 0x0f, depth);
            if ((b & 0xe0) == 0xa0) return Over(p, b & 0x1f, end);
            switch (b)
            {
                case 0xc0: case 0xc2: case 0xc3: return p;
                case 0xc4: case 0xd9: Need(p, 1, end); return Over(p + 1, d[p], end);
                case 0xc5: case 0xda: Need(p, 2, end); return Over(p + 2, U16(d, p), end);
                case 0xc6: case 0xdb: Need(p, 4, end); return Over(p + 4, Len32(d, p), end);
                case 0xc7: Need(p, 1, end); return Over(p + 2, d[p], end);
                case 0xc8: Need(p, 2, end); return Over(p + 3, U16(d, p), end);
                case 0xc9: Need(p, 4, end); return Over(p + 5, Len32(d, p), end);
                case 0xca: return Over(p, 4, end);
                case 0xcb: return Over(p, 8, end);
                case 0xcc: case 0xd0: return Over(p, 1, end);
                case 0xcd: case 0xd1: return Over(p, 2, end);
                case 0xce: case 0xd2: return Over(p, 4, end);
                case 0xcf: case 0xd3: return Over(p, 8, end);
                case 0xd4: return Over(p, 2, end);
                case 0xd5: return Over(p, 3, end);
                case 0xd6: return Over(p, 5, end);
                case 0xd7: return Over(p, 9, end);
                case 0xd8: return Over(p, 17, end);
                case 0xdc: { Need(p, 2, end); return SkipN(d, p + 2, end, U16(d, p), depth); }
                case 0xdd: { Need(p, 4, end); return SkipN(d, p + 4, end, Len32(d, p), depth); }
                case 0xde: { Need(p, 2, end); return SkipN(d, p + 2, end, 2L * U16(d, p), depth); }
                case 0xdf: { Need(p, 4, end); return SkipN(d, p + 4, end, 2L * Len32(d, p), depth); }
            }
            throw new PoseMsgPackException(p - 1, "unknown type byte 0x" + b.ToString("x2"));
        }

        static int SkipN(byte[] d, int p, int end, long n, int depth)
        {
            if (n > end - p) throw new PoseMsgPackException(p, n + " elements cannot fit in " + (end - p) + " bytes");
            for (long i = 0; i < n; i++) p = Skip(d, p, end, depth + 1);
            return p;
        }

        /// <summary>Decodes the value at <paramref name="p"/>; <paramref name="next"/> = position after it.</summary>
        public static object Decode(byte[] d, int p, int end, out int next) => Decode(d, p, end, out next, 0);

        static object Decode(byte[] d, int p, int end, out int next, int depth)
        {
            if (depth > MaxDepth) throw new PoseMsgPackException(p, "nesting deeper than " + MaxDepth);
            Need(p, 1, end);
            int at = p;
            byte b = d[p++];
            if (b <= 0x7f) { next = p; return (long)b; }
            if (b >= 0xe0) { next = p; return (long)(sbyte)b; }
            if ((b & 0xf0) == 0x80) return Map(d, p, end, b & 0x0f, out next, depth);
            if ((b & 0xf0) == 0x90) return Arr(d, p, end, b & 0x0f, out next, depth);
            if ((b & 0xe0) == 0xa0) return Str(d, p, end, b & 0x1f, out next);
            switch (b)
            {
                case 0xc0: next = p; return null;
                case 0xc2: next = p; return false;
                case 0xc3: next = p; return true;
                case 0xc4: Need(p, 1, end); return Bin(d, p + 1, end, d[p], out next);
                case 0xc5: Need(p, 2, end); return Bin(d, p + 2, end, U16(d, p), out next);
                case 0xc6: Need(p, 4, end); return Bin(d, p + 4, end, Len32(d, p), out next);
                case 0xc7: Need(p, 2, end); return Ext(d, p + 1, end, d[p], out next);
                case 0xc8: Need(p, 3, end); return Ext(d, p + 2, end, U16(d, p), out next);
                case 0xc9: Need(p, 5, end); return Ext(d, p + 4, end, Len32(d, p), out next);
                case 0xca: Need(p, 4, end); next = p + 4; return FloatBits.ToFloat(U32(d, p));
                case 0xcb: Need(p, 8, end); next = p + 8; return BitConverter.Int64BitsToDouble((long)U64(d, p));
                case 0xcc: Need(p, 1, end); next = p + 1; return (long)d[p];
                case 0xcd: Need(p, 2, end); next = p + 2; return (long)U16(d, p);
                case 0xce: Need(p, 4, end); next = p + 4; return (long)U32(d, p);
                case 0xcf: { Need(p, 8, end); next = p + 8; ulong v = U64(d, p); return v <= long.MaxValue ? (object)(long)v : v; }
                case 0xd0: Need(p, 1, end); next = p + 1; return (long)(sbyte)d[p];
                case 0xd1: Need(p, 2, end); next = p + 2; return (long)(short)U16(d, p);
                case 0xd2: Need(p, 4, end); next = p + 4; return (long)(int)U32(d, p);
                case 0xd3: Need(p, 8, end); next = p + 8; return (long)U64(d, p);
                case 0xd4: return Ext(d, p, end, 1, out next);
                case 0xd5: return Ext(d, p, end, 2, out next);
                case 0xd6: return Ext(d, p, end, 4, out next);
                case 0xd7: return Ext(d, p, end, 8, out next);
                case 0xd8: return Ext(d, p, end, 16, out next);
                case 0xd9: Need(p, 1, end); return Str(d, p + 1, end, d[p], out next);
                case 0xda: Need(p, 2, end); return Str(d, p + 2, end, U16(d, p), out next);
                case 0xdb: Need(p, 4, end); return Str(d, p + 4, end, Len32(d, p), out next);
                case 0xdc: Need(p, 2, end); return Arr(d, p + 2, end, U16(d, p), out next, depth);
                case 0xdd: Need(p, 4, end); return Arr(d, p + 4, end, Len32(d, p), out next, depth);
                case 0xde: Need(p, 2, end); return Map(d, p + 2, end, U16(d, p), out next, depth);
                case 0xdf: Need(p, 4, end); return Map(d, p + 4, end, Len32(d, p), out next, depth);
            }
            throw new PoseMsgPackException(at, "unknown type byte 0x" + b.ToString("x2"));
        }

        /// <summary>Reads a map header at <paramref name="p"/>: element count and the position of the first key.</summary>
        public static bool TryMapHeader(byte[] d, int p, int end, out int count, out int first)
        {
            count = 0; first = p;
            if (p >= end) return false;
            byte b = d[p];
            if ((b & 0xf0) == 0x80) { count = b & 0x0f; first = p + 1; return true; }
            if (b == 0xde && end - p >= 3) { count = U16(d, p + 1); first = p + 3; return true; }
            if (b == 0xdf && end - p >= 5) { long n = U32(d, p + 1); if (n > int.MaxValue) return false; count = (int)n; first = p + 5; return true; }
            return false;
        }

        /// <summary>Reads an array header at <paramref name="p"/>.</summary>
        public static bool TryArrayHeader(byte[] d, int p, int end, out int count, out int first)
        {
            count = 0; first = p;
            if (p >= end) return false;
            byte b = d[p];
            if ((b & 0xf0) == 0x90) { count = b & 0x0f; first = p + 1; return true; }
            if (b == 0xdc && end - p >= 3) { count = U16(d, p + 1); first = p + 3; return true; }
            if (b == 0xdd && end - p >= 5) { long n = U32(d, p + 1); if (n > int.MaxValue) return false; count = (int)n; first = p + 5; return true; }
            return false;
        }

        /// <summary>The entries of the map at <paramref name="p"/> as byte spans, in order; <paramref name="next"/> = after the map.</summary>
        public static List<PoseMsgPackSpan> MapSpans(byte[] d, int p, int end, out int next)
        {
            if (!TryMapHeader(d, p, end, out int n, out int q)) throw new PoseMsgPackException(p, "not a map");
            if (n > end - q) throw new PoseMsgPackException(p, n + " map entries cannot fit in " + (end - q) + " bytes");
            var list = new List<PoseMsgPackSpan>(n);
            for (int i = 0; i < n; i++)
            {
                var s = new PoseMsgPackSpan { KeyStart = q };
                q = Skip(d, q, end);
                s.KeyEnd = s.ValueStart = q;
                q = Skip(d, q, end);
                s.ValueEnd = q;
                list.Add(s);
            }
            next = q;
            return list;
        }

        /// <summary>Integer value of a decoded long / ulong.</summary>
        public static bool TryInt(object v, out long x)
        {
            switch (v)
            {
                case long l: x = l; return true;
                case ulong u when u <= long.MaxValue: x = (long)u; return true;
                default: x = 0; return false;
            }
        }

        /// <summary>Numeric value of a decoded float / double / integer.</summary>
        public static bool TryNumber(object v, out double x)
        {
            switch (v)
            {
                case float f: x = f; return true;
                case double g: x = g; return true;
                case long l: x = l; return true;
                case ulong u: x = u; return true;
                default: x = 0; return false;
            }
        }

        /// <summary>Raw IEEE bits when the value at <paramref name="p"/> is a float32 (0xca).</summary>
        public static bool TryFloat32Bits(byte[] d, int p, int end, out uint bits)
        {
            bits = 0;
            if (end - p < 5 || d[p] != 0xca) return false;
            bits = U32(d, p + 1);
            return true;
        }

        // ---------------------------------------------------------------- writing (only what the trailer encoder needs)

        internal static void WriteMapHeader(PoseWriter w, int n) => Header(w, n, 0x80, 0xde, 0xdf);
        internal static void WriteArrayHeader(PoseWriter w, int n) => Header(w, n, 0x90, 0xdc, 0xdd);

        static void Header(PoseWriter w, int n, byte fix, byte b16, byte b32)
        {
            if (n < 16) w.Byte((byte)(fix | n));
            else if (n <= 0xffff) { w.Byte(b16); w.Byte((byte)(n >> 8)); w.Byte((byte)n); }
            else { w.Byte(b32); w.Byte((byte)(n >> 24)); w.Byte((byte)(n >> 16)); w.Byte((byte)(n >> 8)); w.Byte((byte)n); }
        }

        /// <summary>Compact signed integer (MessagePack's smallest form).</summary>
        internal static void WriteInt(PoseWriter w, long v)
        {
            if (v >= 0 && v <= 0x7f) { w.Byte((byte)v); return; }
            if (v < 0 && v >= -32) { w.Byte((byte)(sbyte)v); return; }
            if (v >= sbyte.MinValue && v <= sbyte.MaxValue) { w.Byte(0xd0); w.Byte((byte)(sbyte)v); return; }
            if (v >= 0 && v <= 0xff) { w.Byte(0xcc); w.Byte((byte)v); return; }
            if (v >= short.MinValue && v <= short.MaxValue) { w.Byte(0xd1); BE(w, (ulong)v, 2); return; }
            if (v >= 0 && v <= 0xffff) { w.Byte(0xcd); BE(w, (ulong)v, 2); return; }
            if (v >= int.MinValue && v <= int.MaxValue) { w.Byte(0xd2); BE(w, (ulong)v, 4); return; }
            if (v >= 0 && v <= 0xffffffffL) { w.Byte(0xce); BE(w, (ulong)v, 4); return; }
            w.Byte(0xd3); BE(w, (ulong)v, 8);
        }

        /// <summary>UTF-8 string with the smallest header.</summary>
        internal static void WriteString(PoseWriter w, string s)
        {
            var b = Utf8.GetBytes(s ?? "");
            int n = b.Length;
            if (n < 32) w.Byte((byte)(0xa0 | n));
            else if (n <= 0xff) { w.Byte(0xd9); w.Byte((byte)n); }
            else if (n <= 0xffff) { w.Byte(0xda); BE(w, (ulong)n, 2); }
            else { w.Byte(0xdb); BE(w, (ulong)n, 4); }
            w.Bytes(b);
        }

        static void BE(PoseWriter w, ulong v, int n) { for (int i = n - 1; i >= 0; i--) w.Byte((byte)(v >> (8 * i))); }

        // ---------------------------------------------------------------- primitives

        static void Need(int p, int n, int end) { if (end - p < n) throw new PoseMsgPackException(p, "needs " + n + " bytes, " + Math.Max(0, end - p) + " left"); }
        static int Over(int p, long n, int end) { if (n > end - p) throw new PoseMsgPackException(p, "needs " + n + " bytes, " + Math.Max(0, end - p) + " left"); return p + (int)n; }
        static int U16(byte[] d, int p) => (d[p] << 8) | d[p + 1];
        static uint U32(byte[] d, int p) => ((uint)d[p] << 24) | ((uint)d[p + 1] << 16) | ((uint)d[p + 2] << 8) | d[p + 3];
        static long Len32(byte[] d, int p) => U32(d, p);
        static ulong U64(byte[] d, int p) => ((ulong)U32(d, p) << 32) | U32(d, p + 4);

        static string Str(byte[] d, int p, int end, long n, out int next) { next = Over(p, n, end); return Utf8.GetString(d, p, (int)n); }
        static byte[] Bin(byte[] d, int p, int end, long n, out int next)
        {
            next = Over(p, n, end);
            var r = new byte[n];
            Buffer.BlockCopy(d, p, r, 0, (int)n);
            return r;
        }
        static PoseMsgPackExt Ext(byte[] d, int p, int end, long n, out int next)
        {
            Need(p, 1, end);
            sbyte t = (sbyte)d[p];
            var data = Bin(d, p + 1, end, n, out next);
            return new PoseMsgPackExt(t, data);
        }
        static List<object> Arr(byte[] d, int p, int end, long n, out int next, int depth)
        {
            if (n > end - p) throw new PoseMsgPackException(p, n + " elements cannot fit in " + (end - p) + " bytes");
            var l = new List<object>((int)n);
            for (long i = 0; i < n; i++) { l.Add(Decode(d, p, end, out p, depth + 1)); }
            next = p;
            return l;
        }
        static Dictionary<object, object> Map(byte[] d, int p, int end, long n, out int next, int depth)
        {
            if (2 * n > end - p) throw new PoseMsgPackException(p, n + " map entries cannot fit in " + (end - p) + " bytes");
            var m = new Dictionary<object, object>((int)n);
            for (long i = 0; i < n; i++)
            {
                var k = Decode(d, p, end, out p, depth + 1);
                var v = Decode(d, p, end, out p, depth + 1);
                m[k ?? "<nil>"] = v;
            }
            next = p;
            return m;
        }
    }
}
