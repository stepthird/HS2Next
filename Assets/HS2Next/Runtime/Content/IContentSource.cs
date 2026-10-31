// The ContentSource abstraction: every place assets can come from — an HS2
// install, a stand-alone mods folder, later an HS2Next pack — implements IContentSource.
// ContentCatalog composes several of them in priority order and owns the list DB and the
// loaded AssetBundles. All members that touch Unity objects must be called on the main thread.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Content
{
    /// <summary>
    /// Shared bundle cache handed to sources by the catalog. A source must open every bundle
    /// (including dependencies) through it, so that a bundle is never loaded twice and the
    /// catalog can unload everything at once.
    /// </summary>
    public interface IBundleCache
    {
        /// <summary>
        /// Returns the bundle cached under <paramref name="key"/>, or calls <paramref name="load"/>
        /// and caches its result. A null result is not cached: the next request calls its load again.
        /// Keys must be unique across sources: prefix them with the source Id, or use the zipmod path
        /// for zipmod bundles.
        /// </summary>
        AssetBundle GetOrLoad(string key, Func<AssetBundle> load);
    }

    /// <summary>One provider of list data and AssetBundles.</summary>
    public interface IContentSource
    {
        /// <summary>Stable identifier, e.g. "hs2-install" or "mods:&lt;folder&gt;". Used in cache keys and file names.</summary>
        string Id { get; }

        /// <summary>Human-readable name for UI and logs.</summary>
        string DisplayName { get; }

        /// <summary>
        /// The source's own list rows (for an HS2 install: the vanilla abdata/list/characustom
        /// TextAssets). Each row's <see cref="ListEntry.Key"/> is in the source's namespace:
        /// <see cref="ContentIds.Vanilla"/> for base-game rows, the pack GUID for pack rows. Within one
        /// source, later rows override earlier ones with the same (category, key).
        /// Bundles needed to read the lists are opened through <paramref name="cache"/>.
        /// </summary>
        IEnumerable<ListEntry> ReadLists(IBundleCache cache);

        /// <summary>Zipmod files (Sideloader format) this source provides, in a stable order.</summary>
        IEnumerable<string> EnumerateZipmodFiles();

        /// <summary>
        /// Reads manifest GUID, list CSV rows and shipped bundle paths of every zipmod, using
        /// (and rewriting) <paramref name="cacheFile"/> keyed by path + size + mtime when it is
        /// not null. The result is also kept in <see cref="Zipmods"/>.
        /// </summary>
        ZipmodScanResult ScanZipmods(string cacheFile);

        /// <summary>Zipmods found by the last <see cref="ScanZipmods"/> call (empty before).</summary>
        IReadOnlyList<ZipmodInfo> Zipmods { get; }

        /// <summary>
        /// abdata-relative paths of the source's own Studio list bundles (every *.unity3d under
        /// abdata/studio/info, recursively), in any order. Empty for
        /// sources without an abdata store. Opened with <see cref="OpenBundle"/>.
        /// </summary>
        IEnumerable<string> EnumerateStudioInfoBundles();

        /// <summary>True if the source's own bundle store (abdata for an HS2 install) has this abdata-relative path.</summary>
        bool HasBundle(string path);

        /// <summary>
        /// Opens a bundle of the source's own store by abdata-relative path, loading its
        /// dependencies first (abdata manifest). Returns null if the source does not have it.
        /// Zipmod bundles are opened with <see cref="ZipmodInfo.OpenBundle"/>.
        /// </summary>
        AssetBundle OpenBundle(string path, IBundleCache cache);
    }
}
