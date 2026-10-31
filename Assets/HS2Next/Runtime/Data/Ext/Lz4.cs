// LZ4 block decompressor (the public LZ4 block format: sequences of a token byte, literals,
// a 2-byte little-endian match offset and a match length; no frame header). Written from the
// format description, used for MessagePack-CSharp LZ4 documents (NestedMsgPack).
using System;
using System.IO;

namespace HS2Next.Data.Ext
{
    public static class Lz4
    {
        /// <summary>Decompresses one LZ4 block of <paramref name="srcLen"/> bytes into exactly <paramref name="dstLen"/> bytes.</summary>
        /// <exception cref="InvalidDataException">Malformed block or wrong output length.</exception>
        public static byte[] DecodeBlock(byte[] src, int srcOff, int srcLen, int dstLen)
        {
            var dst = new byte[dstLen];
            int n = DecodeBlock(src, srcOff, srcLen, dst, 0, dstLen);
            if (n != dstLen) throw new InvalidDataException($"lz4: decoded {n} bytes, expected {dstLen}");
            return dst;
        }

        /// <summary>Decompresses into <paramref name="dst"/> at <paramref name="dstOff"/>; returns the number of bytes written.</summary>
        public static int DecodeBlock(byte[] src, int srcOff, int srcLen, byte[] dst, int dstOff, int dstCap)
        {
            int s = srcOff, sEnd = srcOff + srcLen;
            int o = dstOff, oEnd = dstOff + dstCap;
            if (srcLen < 0 || sEnd > src.Length || oEnd > dst.Length) throw new InvalidDataException("lz4: bad buffer range");
            while (s < sEnd)
            {
                int token = src[s++];
                // literals
                int lit = token >> 4;
                if (lit == 15)
                {
                    int b;
                    do
                    {
                        if (s >= sEnd) throw new InvalidDataException("lz4: truncated literal length");
                        b = src[s++]; lit += b;
                    } while (b == 255);
                }
                if (lit > 0)
                {
                    if (s + lit > sEnd || o + lit > oEnd) throw new InvalidDataException("lz4: literal run out of range");
                    Buffer.BlockCopy(src, s, dst, o, lit);
                    s += lit; o += lit;
                }
                if (s >= sEnd) break; // last sequence has literals only
                // match
                if (s + 2 > sEnd) throw new InvalidDataException("lz4: truncated offset");
                int offset = src[s] | (src[s + 1] << 8);
                s += 2;
                if (offset == 0 || o - offset < dstOff) throw new InvalidDataException("lz4: bad match offset");
                int len = token & 15;
                if (len == 15)
                {
                    int b;
                    do
                    {
                        if (s >= sEnd) throw new InvalidDataException("lz4: truncated match length");
                        b = src[s++]; len += b;
                    } while (b == 255);
                }
                len += 4;
                if (o + len > oEnd) throw new InvalidDataException("lz4: match out of range");
                int m = o - offset;
                if (offset >= len) { Buffer.BlockCopy(dst, m, dst, o, len); o += len; }
                else for (int i = 0; i < len; i++) dst[o++] = dst[m++]; // overlapping copy
            }
            return o - dstOff;
        }
    }
}
