// Placeholder for the HS2Next pack format: content
// baked with Unity 6 into a new format, independent of the legacy bundle format, also the way
// to ship community parts with HS2Next. v1 only reserves the entry point; there is no
// implementation yet.
//
// How it is meant to plug in: a pack has its own GUID; ReadLists returns its rows with
// Key = (PackGuid, id) (they land in the ContentCatalog list DB under that ContentId),
// HasBundle/OpenBundle serve its bundles, and EnumerateZipmodFiles/ScanZipmods return nothing.
namespace HS2Next.Content
{
    /// <summary>An HS2Next-format content pack (not implemented yet,).</summary>
    public interface IPackSource : IContentSource
    {
        /// <summary>The pack's GUID; the namespace of all its list rows.</summary>
        string PackGuid { get; }

        /// <summary>Version of the pack format the pack was built with.</summary>
        int FormatVersion { get; }
    }
}
