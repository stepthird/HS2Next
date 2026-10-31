// Size limits of the container. Writers refuse to produce a file
// that breaks one (TooLarge); readers refuse entries whose declared size breaks one and never
// inflate past the declared size. Tests use lowered copies.
namespace HS2Next.Save
{
    public sealed class ContainerLimits
    {
        public static readonly ContainerLimits Default = new ContainerLimits();

        /// <summary>Entries per file. 0xFFFF itself is the Zip64 marker of the end record, so the last usable count is 65,534.</summary>
        public int MaxEntries = 65534;
        /// <summary>One entry, uncompressed: 1 GiB.</summary>
        public long MaxEntrySize = 1L << 30;
        /// <summary>One JSON document (manifest, chara, coord, scene): 256 MiB.</summary>
        public long MaxDocumentSize = 256L << 20;
        /// <summary>Sum of the uncompressed sizes: 4 GiB (decompression-bomb guard).</summary>
        public long MaxTotalSize = 4L << 30;
        /// <summary>Whole file: below 4 GiB (no Zip64).</summary>
        public long MaxFileSize = 0xFFFFFFFFL;
    }
}
