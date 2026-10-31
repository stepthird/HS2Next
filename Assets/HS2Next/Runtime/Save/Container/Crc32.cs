// CRC-32 (IEEE 802.3, the ZIP checksum), slicing-by-8. The container reader checks every inflated
// entry against the central directory itself instead of relying on the platform ZipArchive
// (open point 15.16); PNG chunk CRCs use the same polynomial.
namespace HS2Next.Save
{
    public static class Crc32
    {
        static readonly uint[] Table = Build();

        static uint[] Build()
        {
            var t = new uint[8 * 256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[i] = c;
            }
            for (int i = 0; i < 256; i++)
                for (int s = 1; s < 8; s++)
                    t[s * 256 + i] = (t[(s - 1) * 256 + i] >> 8) ^ t[t[(s - 1) * 256 + i] & 0xFF];
            return t;
        }

        /// <summary>CRC of a whole array.</summary>
        public static uint Compute(byte[] data) => Append(0, data, 0, data.Length);

        /// <summary>Continues <paramref name="crc"/> (0 to start) over data[offset .. offset+count).</summary>
        public static uint Append(uint crc, byte[] data, int offset, int count)
        {
            var t = Table;
            uint c = ~crc;
            int i = offset, end = offset + count;
            while (end - i >= 8)
            {
                uint a = c ^ (uint)(data[i] | data[i + 1] << 8 | data[i + 2] << 16 | data[i + 3] << 24);
                uint b = (uint)(data[i + 4] | data[i + 5] << 8 | data[i + 6] << 16 | data[i + 7] << 24);
                c = t[7 * 256 + (a & 0xFF)] ^ t[6 * 256 + ((a >> 8) & 0xFF)] ^ t[5 * 256 + ((a >> 16) & 0xFF)] ^ t[4 * 256 + (a >> 24)] ^
                    t[3 * 256 + (b & 0xFF)] ^ t[2 * 256 + ((b >> 8) & 0xFF)] ^ t[256 + ((b >> 16) & 0xFF)] ^ t[b >> 24];
                i += 8;
            }
            while (i < end) c = t[(c ^ data[i++]) & 0xFF] ^ (c >> 8);
            return ~c;
        }
    }
}
