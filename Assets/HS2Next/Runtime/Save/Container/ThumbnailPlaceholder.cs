// The placeholder thumbnail: a solid sRGB (128, 128, 128)
// 504 × 704 8-bit RGB PNG made without rendering, for headless saves. Encoded here with a minimal
// PNG writer (IHDR, one IDAT with filter 0 rows, IEND); the bytes are deterministic.
using System;
using System.IO;
using System.IO.Compression;

namespace HS2Next.Save
{
    public static class ThumbnailPlaceholder
    {
        public const int Width = 504;
        public const int Height = 704;
        public const byte Grey = 128;

        static byte[] cached;

        /// <summary>The placeholder PNG (a copy, so callers may keep it).</summary>
        public static byte[] Png()
        {
            if (cached == null) cached = SolidRgb(Width, Height, Grey, Grey, Grey);
            return (byte[])cached.Clone();
        }

        /// <summary>A solid-colour 8-bit RGB PNG.</summary>
        public static byte[] SolidRgb(int width, int height, byte r, byte g, byte b)
        {
            var row = new byte[1 + width * 3];
            for (int x = 0; x < width; x++) { row[1 + 3 * x] = r; row[2 + 3 * x] = g; row[3 + 3 * x] = b; }
            var raw = new byte[row.Length * height];
            for (int y = 0; y < height; y++) Buffer.BlockCopy(row, 0, raw, y * row.Length, row.Length);
            return EncodeRaw(width, height, 2, raw);
        }

        /// <summary>
        /// Encodes already filtered scanlines (<paramref name="raw"/> = per row a filter byte + pixel bytes) as a PNG
        /// with bit depth 8 and the given colour type (2 = RGB, 6 = RGBA), no interlace.
        /// </summary>
        public static byte[] EncodeRaw(int width, int height, int colorType, byte[] raw)
        {
            var ms = new MemoryStream();
            ms.Write(PngInfo.Signature, 0, 8);
            var ihdr = new byte[13];
            PutBE(ihdr, 0, (uint)width);
            PutBE(ihdr, 4, (uint)height);
            ihdr[8] = 8;
            ihdr[9] = (byte)colorType;
            Chunk(ms, "IHDR", ihdr);
            Chunk(ms, "IDAT", Zlib(raw));
            Chunk(ms, "IEND", new byte[0]);
            return ms.ToArray();
        }

        static byte[] Zlib(byte[] data)
        {
            var ms = new MemoryStream();
            ms.WriteByte(0x78);
            ms.WriteByte(0x9C);
            using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, true)) ds.Write(data, 0, data.Length);
            uint a = 1, b = 0;
            foreach (byte x in data)
            {
                a = (a + x) % 65521;
                b = (b + a) % 65521;
            }
            var adler = new byte[4];
            PutBE(adler, 0, (b << 16) | a);
            ms.Write(adler, 0, 4);
            return ms.ToArray();
        }

        static void Chunk(Stream s, string type, byte[] data)
        {
            var head = new byte[8];
            PutBE(head, 0, (uint)data.Length);
            for (int i = 0; i < 4; i++) head[4 + i] = (byte)type[i];
            s.Write(head, 0, 8);
            s.Write(data, 0, data.Length);
            uint crc = Crc32.Append(0, head, 4, 4);
            crc = Crc32.Append(crc, data, 0, data.Length);
            var c = new byte[4];
            PutBE(c, 0, crc);
            s.Write(c, 0, 4);
        }

        static void PutBE(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v >> 24);
            b[o + 1] = (byte)(v >> 16);
            b[o + 2] = (byte)(v >> 8);
            b[o + 3] = (byte)v;
        }
    }
}
