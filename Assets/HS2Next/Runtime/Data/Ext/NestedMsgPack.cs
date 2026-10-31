// Nested MessagePack documents stored inside `bin` values of plugin data.
// MessagePack-CSharp writes them either plain or LZ4-compressed:
//   Lz4Block (ext type 99):       ext payload = MessagePack int (uncompressed length) + one raw LZ4 block.
//   Lz4BlockArray (ext type 98):  array [ext 98 {int length per block}, bin block1, bin block2, ...]
//                                 (MessagePack-CSharp v2 form; accepted for completeness, not seen on cards).
// Small documents (< 64 bytes) are always plain, so every reader must accept both forms for the same key.
using System;
using System.IO;
using HS2Next.IO;

namespace HS2Next.Data.Ext
{
    public enum NestedEncoding { Plain, Lz4Block, Lz4BlockArray }

    public static class NestedMsgPack
    {
        public const sbyte Lz4BlockType = 99, Lz4BlockArrayType = 98;

        /// <summary>Decodes a nested document (plain or LZ4). Null input gives null.</summary>
        public static object Read(byte[] bin) => Read(bin, out _);

        public static object Read(byte[] bin, out NestedEncoding enc)
        {
            var doc = Unwrap(bin, out enc);
            if (doc == null || doc.Length == 0) return null;
            return MsgPack.Read(doc);
        }

        /// <summary>Returns the plain MessagePack bytes of a nested document (decompressing LZ4 forms; plain input is returned as is).</summary>
        public static byte[] Unwrap(byte[] bin, out NestedEncoding enc)
        {
            enc = NestedEncoding.Plain;
            if (bin == null || bin.Length == 0) return bin;
            int p = 0;
            if (TryExtHeader(bin, ref p, out sbyte type, out int len) && type == Lz4BlockType)
            {
                enc = NestedEncoding.Lz4Block;
                int q = p;
                int raw = MsgPack.Read(bin, ref q).Int(-1);
                if (raw < 0) throw new InvalidDataException("lz4 ext 99: bad length");
                return Lz4.DecodeBlock(bin, q, p + len - q, raw);
            }
            p = 0;
            int count = ArrayHeader(bin, ref p);
            if (count >= 2 && TryExtHeader(bin, ref p, out type, out len) && type == Lz4BlockArrayType)
            {
                enc = NestedEncoding.Lz4BlockArray;
                int q = p, end = p + len;
                var sizes = new int[count - 1];
                long total = 0;
                for (int i = 0; i < sizes.Length; i++)
                {
                    if (q >= end) throw new InvalidDataException("lz4 ext 98: missing block length");
                    sizes[i] = MsgPack.Read(bin, ref q).Int(-1);
                    if (sizes[i] < 0) throw new InvalidDataException("lz4 ext 98: bad block length");
                    total += sizes[i];
                }
                p = end;
                var dst = new byte[total];
                int o = 0;
                for (int i = 0; i < sizes.Length; i++)
                {
                    if (!(MsgPack.Read(bin, ref p) is byte[] block)) throw new InvalidDataException("lz4 ext 98: block is not bin");
                    int n = Lz4.DecodeBlock(block, 0, block.Length, dst, o, sizes[i]);
                    if (n != sizes[i]) throw new InvalidDataException("lz4 ext 98: block length mismatch");
                    o += n;
                }
                return dst;
            }
            return bin;
        }

        static bool TryExtHeader(byte[] d, ref int p, out sbyte type, out int len)
        {
            type = 0; len = 0;
            if (p >= d.Length) return false;
            byte b = d[p];
            int hdr;
            switch (b)
            {
                case 0xd4: len = 1; hdr = 1; break;
                case 0xd5: len = 2; hdr = 1; break;
                case 0xd6: len = 4; hdr = 1; break;
                case 0xd7: len = 8; hdr = 1; break;
                case 0xd8: len = 16; hdr = 1; break;
                case 0xc7: if (p + 2 > d.Length) return false; len = d[p + 1]; hdr = 2; break;
                case 0xc8: if (p + 3 > d.Length) return false; len = (d[p + 1] << 8) | d[p + 2]; hdr = 3; break;
                case 0xc9: if (p + 5 > d.Length) return false; len = (int)(((uint)d[p + 1] << 24) | ((uint)d[p + 2] << 16) | ((uint)d[p + 3] << 8) | d[p + 4]); hdr = 5; break;
                default: return false;
            }
            if (p + hdr + 1 + len > d.Length || len < 0) return false;
            type = (sbyte)d[p + hdr];
            p += hdr + 1;
            return true;
        }

        static int ArrayHeader(byte[] d, ref int p)
        {
            byte b = d[p];
            if ((b & 0xf0) == 0x90) { p++; return b & 0x0f; }
            if (b == 0xdc && p + 3 <= d.Length) { int n = (d[p + 1] << 8) | d[p + 2]; p += 3; return n; }
            if (b == 0xdd && p + 5 <= d.Length) { int n = (int)(((uint)d[p + 1] << 24) | ((uint)d[p + 2] << 16) | ((uint)d[p + 3] << 8) | d[p + 4]); p += 5; return n; }
            return -1;
        }
    }
}
