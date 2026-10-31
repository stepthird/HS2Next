// PNG structure checks without decoding: the
// chunk walk that finds where the thumbnail prefix ends, and the IHDR fields. Pixel data is never
// decoded; the reader hands the thumbnail out as bytes.
using System;

namespace HS2Next.Save
{
    public struct PngHeader
    {
        public int Width, Height, BitDepth, ColorType, Interlace;
    }

    public static class PngInfo
    {
        public static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>True if <paramref name="b"/> starts with the PNG signature at <paramref name="offset"/>.</summary>
        public static bool HasSignature(byte[] b, int offset = 0)
        {
            if (b == null || b.Length - offset < 8) return false;
            for (int i = 0; i < 8; i++) if (b[offset + i] != Signature[i]) return false;
            return true;
        }

        static uint BE(byte[] b, long o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);

        /// <summary>
        /// Walks the chunks of a PNG starting at offset 0 of <paramref name="b"/> (at most <paramref name="limit"/>
        /// bytes): IHDR first, then any chunks, IEND last. Returns the offset just after the IEND chunk's CRC, or
        /// -1 when the structure is broken or IEND is missing. Chunk CRCs are not checked.
        /// </summary>
        public static long FindEnd(byte[] b, long limit, out PngHeader header)
        {
            header = default;
            if (limit > b.Length) limit = b.Length;
            if (!HasSignature(b) || limit < 8 + 25) return -1;
            long p = 8;
            bool first = true;
            while (p + 12 <= limit)
            {
                uint len = BE(b, p);
                if (len > int.MaxValue) return -1;
                long type = p + 4;
                long end = p + 12 + len;
                if (end > limit) return -1;
                bool isIhdr = b[type] == 'I' && b[type + 1] == 'H' && b[type + 2] == 'D' && b[type + 3] == 'R';
                if (first)
                {
                    if (!isIhdr || len != 13) return -1;
                    header.Width = (int)BE(b, p + 8);
                    header.Height = (int)BE(b, p + 12);
                    header.BitDepth = b[p + 16];
                    header.ColorType = b[p + 17];
                    header.Interlace = b[p + 20];
                    first = false;
                }
                if (b[type] == 'I' && b[type + 1] == 'E' && b[type + 2] == 'N' && b[type + 3] == 'D') return end;
                p = end;
            }
            return -1;
        }

        /// <summary>The IHDR of a complete PNG, or null when it is not a structurally valid PNG.</summary>
        public static PngHeader? ReadHeader(byte[] png)
        {
            if (png == null) return null;
            long end = FindEnd(png, png.Length, out var h);
            return end == png.Length ? h : (PngHeader?)null;
        }
    }
}
