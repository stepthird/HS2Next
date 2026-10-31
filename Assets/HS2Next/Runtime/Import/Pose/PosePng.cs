// PNG container of HS2 pose files: a small image whose IEND chunk (and its CRC) is
// followed by the full pose payload. The chunk list is walked from the signature to IEND (lengths only; CRCs are
// not checked, the image bytes are kept as they are). Unity-free (System only).
namespace HS2Next.Import.Pose
{
    public static class PosePng
    {
        static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>True when <paramref name="d"/> starts with the 8-byte PNG signature.</summary>
        public static bool HasSignature(byte[] d)
        {
            if (d == null || d.Length < Signature.Length) return false;
            for (int i = 0; i < Signature.Length; i++) if (d[i] != Signature[i]) return false;
            return true;
        }

        /// <summary>
        /// Offset of the first byte after the IEND chunk's CRC (= the length of the image part). Throws
        /// PoseFormatException (Truncated / BadPng) with the offset of the chunk that breaks the walk.
        /// </summary>
        public static int ImageLength(byte[] d)
        {
            if (!HasSignature(d)) throw new PoseFormatException(PoseErrorCode.BadPng, 0, "no PNG signature");
            int p = Signature.Length;
            while (true)
            {
                if (d.Length - p < 8) throw new PoseFormatException(PoseErrorCode.Truncated, p, "PNG chunk header runs past the end of the file (no IEND)");
                uint len = ((uint)d[p] << 24) | ((uint)d[p + 1] << 16) | ((uint)d[p + 2] << 8) | d[p + 3];
                if (len > 0x7FFFFFFFu) throw new PoseFormatException(PoseErrorCode.BadPng, p, "PNG chunk length " + len + " exceeds 2^31-1");
                if ((long)d.Length - p < 12L + len) throw new PoseFormatException(PoseErrorCode.Truncated, p, "PNG chunk of " + len + " bytes runs past the end of the file");
                bool iend = d[p + 4] == (byte)'I' && d[p + 5] == (byte)'E' && d[p + 6] == (byte)'N' && d[p + 7] == (byte)'D';
                p += 12 + (int)len;
                if (iend) return p;
            }
        }
    }
}
