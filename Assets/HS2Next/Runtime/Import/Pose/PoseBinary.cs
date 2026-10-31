// Binary helpers of the HS2 pose-file reader/writer: a bounds-checked
// little-endian cursor that raises PoseFormatException with the absolute byte offset, a growable writer,
// .NET BinaryWriter strings (7-bit encoded byte length + UTF-8) kept as raw bytes, and float <-> bits
// conversions that never pass the bits through float arithmetic. Unity-free (System only).
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace HS2Next.Import.Pose
{
    /// <summary>Reinterprets IEEE-754 single bits as a float and back (no conversion, no allocation).</summary>
    public static class FloatBits
    {
        [StructLayout(LayoutKind.Explicit)]
        struct Union
        {
            [FieldOffset(0)] public uint U;
            [FieldOffset(0)] public float F;
        }

        public static float ToFloat(uint bits) { var u = new Union { U = bits }; return u.F; }
        public static uint ToBits(float value) { var u = new Union { F = value }; return u.U; }
        /// <summary>True for NaN bit patterns (exponent all ones, mantissa non-zero), whatever the payload.</summary>
        public static bool IsNaN(uint bits) => (bits & 0x7F800000u) == 0x7F800000u && (bits & 0x007FFFFFu) != 0;
    }

    /// <summary>
    /// A .NET BinaryWriter string as it was in the file: the raw bytes (7-bit length prefix + UTF-8 bytes) and the
    /// value .NET's BinaryReader would decode (invalid UTF-8 → U+FFFD). Writing an unchanged value reproduces the raw
    /// bytes; a changed value is written canonically.
    /// </summary>
    internal struct RawString
    {
        public string Value;
        public byte[] Raw;
        /// <summary>False when the raw bytes are not what a BinaryWriter would write for <see cref="Value"/>
        /// (non-minimal length prefix or invalid UTF-8).</summary>
        public bool Canonical;

        public static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, false);

        public static byte[] Encode(string s)
        {
            var body = Utf8.GetBytes(s ?? "");
            var w = new PoseWriter(body.Length + 5);
            w.String7(body);
            return w.ToArray();
        }
    }

    /// <summary>Bounds-checked little-endian reader over a byte array; positions are absolute offsets.</summary>
    internal sealed class PoseReader
    {
        readonly byte[] d;
        readonly int end;
        public int Pos;

        public PoseReader(byte[] data, int start, int end)
        {
            d = data;
            Pos = start;
            this.end = end;
        }

        public int Remaining => end - Pos;

        void Need(int n, string what)
        {
            if (end - Pos < n)
                throw new PoseFormatException(PoseErrorCode.Truncated, Pos, $"{what}: needs {n} bytes, {Math.Max(0, end - Pos)} left");
        }

        public byte Byte(string what) { Need(1, what); return d[Pos++]; }

        public int Int32(string what)
        {
            Need(4, what);
            int v = d[Pos] | (d[Pos + 1] << 8) | (d[Pos + 2] << 16) | (d[Pos + 3] << 24);
            Pos += 4;
            return v;
        }

        public uint UInt32(string what) => unchecked((uint)Int32(what));

        /// <summary>A .NET BinaryReader string (Read7BitEncodedInt + UTF-8) with its raw bytes.</summary>
        public RawString String(string what)
        {
            int start = Pos;
            int len = 0, shift = 0;
            while (true)
            {
                if (shift == 35) throw new PoseFormatException(PoseErrorCode.BadString, start, what + ": 7-bit length longer than 5 bytes");
                if (Pos >= end) throw new PoseFormatException(PoseErrorCode.Truncated, start, what + ": the length prefix runs past the end of the file");
                byte b = d[Pos++];
                len |= (b & 0x7F) << shift;
                shift += 7;
                if ((b & 0x80) == 0) break;
            }
            int prefix = Pos - start;
            if (len < 0) throw new PoseFormatException(PoseErrorCode.BadString, start, what + ": negative length " + len);
            if (end - Pos < len) throw new PoseFormatException(PoseErrorCode.Truncated, start, $"{what}: {len} bytes declared, {end - Pos} left");
            var raw = new byte[prefix + len];
            Buffer.BlockCopy(d, start, raw, 0, raw.Length);
            string value = RawString.Utf8.GetString(d, Pos, len);
            Pos += len;
            bool canonical = prefix == Length7(len) && RawString.Utf8.GetByteCount(value) == len && SameUtf8(value, d, start + prefix, len);
            return new RawString { Value = value, Raw = raw, Canonical = canonical };
        }

        static bool SameUtf8(string value, byte[] d, int off, int len)
        {
            var b = RawString.Utf8.GetBytes(value);
            if (b.Length != len) return false;
            for (int i = 0; i < len; i++) if (b[i] != d[off + i]) return false;
            return true;
        }

        internal static int Length7(int v)
        {
            uint u = (uint)v;
            int n = 1;
            while (u >= 0x80) { u >>= 7; n++; }
            return n;
        }

        public byte[] Slice(int start, int length)
        {
            var r = new byte[length];
            Buffer.BlockCopy(d, start, r, 0, length);
            return r;
        }
    }

    /// <summary>Growable little-endian writer (BinaryWriter layout).</summary>
    internal sealed class PoseWriter
    {
        byte[] buf;
        int n;

        public PoseWriter(int capacity = 4096) { buf = new byte[Math.Max(16, capacity)]; }

        public int Length => n;

        void Ensure(int more)
        {
            if (n + more <= buf.Length) return;
            int cap = buf.Length;
            while (cap < n + more) cap *= 2;
            Array.Resize(ref buf, cap);
        }

        public void Byte(byte b) { Ensure(1); buf[n++] = b; }

        public void Bytes(byte[] b)
        {
            if (b == null || b.Length == 0) return;
            Ensure(b.Length);
            Buffer.BlockCopy(b, 0, buf, n, b.Length);
            n += b.Length;
        }

        public void Bytes(byte[] b, int off, int len)
        {
            if (len <= 0) return;
            Ensure(len);
            Buffer.BlockCopy(b, off, buf, n, len);
            n += len;
        }

        public void Int32(int v)
        {
            Ensure(4);
            buf[n++] = (byte)v; buf[n++] = (byte)(v >> 8); buf[n++] = (byte)(v >> 16); buf[n++] = (byte)(v >> 24);
        }

        public void UInt32(uint v) => Int32(unchecked((int)v));

        /// <summary>BinaryWriter.Write(string) for already encoded UTF-8 bytes.</summary>
        public void String7(byte[] utf8)
        {
            uint v = (uint)utf8.Length;
            while (v >= 0x80) { Byte((byte)(v | 0x80)); v >>= 7; }
            Byte((byte)v);
            Bytes(utf8);
        }

        public byte[] ToArray()
        {
            var r = new byte[n];
            Buffer.BlockCopy(buf, 0, r, 0, n);
            return r;
        }
    }
}
