// A scanned zipmod: its manifest GUID and details, list rows, Studio list CSVs, scene-dependency
// declarations, migration entries and the abdata paths it ships; the names of its manifest's top-level elements (any
// element's XML is read from the zip on demand); its <game> values, whether the zipmod loader skips it, and the asset
// names of its copies of redirectable bundle paths (AssetRedirect.cs).
using System;
using System.Collections.Generic;
using HS2Next.Content.Studio;
using HS2Next.IO;
using UnityEngine;

namespace HS2Next.Content
{
    public sealed class ZipmodInfo : IRedirectZip
    {
        public IContentSource Source { get; }

        /// <summary>The file's size and last-write time as the scan cache keys it ("size:ticks"); null when unknown.</summary>
        public string Stamp { get; internal set; }

        /// <summary>The manifest's non-blank &lt;game&gt; values (trimmed), empty when it names none; null when not read.</summary>
        public IReadOnlyList<string> Games { get; internal set; }

        /// <summary>
        /// Why the zipmod loader does not load this file (with ContentCatalog.SideloaderZipmodSet): "game" (its &lt;game&gt;
        /// values name no HS2 game) or "version" (another file of the GUID is kept); null = loaded. A skipped zipmod adds no
        /// rows, bundle paths, Studio lists, migrations or declarations.
        /// </summary>
        public string SkippedBy { get; internal set; }

        /// <summary>The last-write time of the file (from <see cref="Stamp"/>), 0 when unknown.</summary>
        public long LastWriteTicks
        {
            get
            {
                int c = Stamp?.IndexOf(':') ?? -1;
                return c > 0 && long.TryParse(Stamp.Substring(c + 1), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : 0;
            }
        }

        // The asset names of the copies of redirectable paths (exact path), set by the catalog's names pass.
        Dictionary<string, BundleNames> assetNames;

        internal void SetNames(string bundlePath, BundleNames names)
        {
            if (assetNames == null) assetNames = new Dictionary<string, BundleNames>(StringComparer.Ordinal);
            assetNames[bundlePath] = names;
        }

        /// <summary>The asset names of this zipmod's copy of <paramref name="bundlePath"/> (exact path), or null when not read.</summary>
        public BundleNames NamesOf(string bundlePath) => assetNames != null && bundlePath != null && assetNames.TryGetValue(bundlePath, out var n) ? n : null;

        /// <summary>Path of the zipmod file.</summary>
        public string Path { get; }

        /// <summary>The manifest GUID, or null if the zipmod has none (such zipmods are ignored).</summary>
        public string Guid { get; }

        /// <summary>Manifest `name`, `author`, `website`, `version` (root elements, trimmed; null when absent). For `requires`
        /// details of saved files and messages.</summary>
        public string Name { get; internal set; }
        public string Author { get; internal set; }
        public string Website { get; internal set; }
        public string Version { get; internal set; }

        /// <summary>The manifest's `&lt;migrationInfo&gt;` entries in manifest order, read as Sideloader reads them (lines it
        /// rejects are left out). Applied by the importer's reference resolution.</summary>
        public IReadOnlyList<ZipmodMigration> Migrations => migrations;

        /// <summary>List rows from abdata/list/characustom/*.csv (keys in the namespace <see cref="Guid"/>).</summary>
        public IReadOnlyList<ListEntry> Entries => entries;

        /// <summary>Abdata-relative paths of all files the zipmod ships under abdata/ (bundles and lists), in their own case
        /// (looked up case-insensitively by <see cref="HasBundle"/>).</summary>
        public IReadOnlyCollection<string> BundlePaths => bundlePaths;

        /// <summary>Studio list CSVs (abdata/studio/info*/**.csv) in zip-entry order.</summary>
        public IReadOnlyList<StudioCsvFile> StudioCsvs => studioCsvs;

        /// <summary>`&lt;hs2-scene-dependency manifest="X"&gt;` declarations of manifest.xml.</summary>
        public IReadOnlyList<SceneDependencyDecl> SceneDependencies => sceneDependencies;

        /// <summary>The names of the manifest root's child elements, distinct, in manifest order (scan cache row T); empty
        /// for a zipmod the loader skips or whose manifest does not parse as XML.</summary>
        public IReadOnlyList<string> ManifestElements => manifestElements;

        readonly List<ListEntry> entries = new List<ListEntry>();
        readonly List<StudioCsvFile> studioCsvs = new List<StudioCsvFile>();
        readonly List<SceneDependencyDecl> sceneDependencies = new List<SceneDependencyDecl>();
        readonly List<ZipmodMigration> migrations = new List<ZipmodMigration>();
        readonly List<string> manifestElements = new List<string>();
        readonly HashSet<string> bundlePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ZipIndex index;

        internal ZipmodInfo(IContentSource source, string path, string guid)
        {
            Source = source; Path = path; Guid = string.IsNullOrEmpty(guid) ? null : guid;
        }

        internal void AddEntry(ListEntry e) { e.Source = Source; e.Zipmod = this; entries.Add(e); }
        internal void AddBundlePath(string rel) => bundlePaths.Add(rel);
        internal void AddStudioCsv(StudioCsvFile f) => studioCsvs.Add(f);
        internal void AddSceneDependency(SceneDependencyDecl d) => sceneDependencies.Add(d);
        internal void SetManifestElements(IEnumerable<string> names) { manifestElements.Clear(); manifestElements.AddRange(names); }
        internal void AddMigration(ZipmodMigration m) { m.Zipmod = this; migrations.Add(m); }

        /// <summary>The zip's central directory, read on first use.</summary>
        public ZipIndex Index => index ??= new ZipIndex(Path);

        /// <summary>True when the manifest root has a child element of this name (from the scan; no file read).</summary>
        public bool HasManifestElement(string name) => name != null && manifestElements.Contains(name);

        /// <summary>The outer XML of the first child element of the manifest root named <paramref name="name"/>, read from the
        /// zip now; null when there is none or the manifest does not parse.</summary>
        public string ReadManifestElement(string name)
        {
            var all = ReadManifestElements(name);
            return all.Count > 0 ? all[0] : null;
        }

        /// <summary>The outer XML of every child element of the manifest root named <paramref name="name"/>, in manifest order,
        /// read from the zip now (empty when there is none, the manifest is missing or it does not parse).</summary>
        public IReadOnlyList<string> ReadManifestElements(string name)
        {
            var l = new List<string>();
            if (string.IsNullOrEmpty(name)) return l;
            string mx;
            try { mx = Index.ReadText("manifest.xml"); }
            catch (Exception) { return l; }
            if (string.IsNullOrEmpty(mx) || mx.IndexOf(name, StringComparison.Ordinal) < 0) return l;
            var doc = new System.Xml.XmlDocument();
            try { doc.LoadXml(mx); }
            catch (System.Xml.XmlException) { return l; }
            if (doc.DocumentElement == null) return l;
            foreach (System.Xml.XmlNode n in doc.DocumentElement.ChildNodes)
                if (n is System.Xml.XmlElement e && e.Name == name) l.Add(e.OuterXml);
            return l;
        }

        /// <summary>True if the zipmod ships abdata/&lt;path&gt;.</summary>
        public bool HasBundle(string path) => bundlePaths.Contains(path.Replace('\\', '/'));

        /// <summary>
        /// Opens abdata/&lt;path&gt; from the zip: in place (file + offset) when stored
        /// uncompressed, else from memory. Returns null if the zip does not ship it.
        /// </summary>
        public AssetBundle OpenBundle(string path, IBundleCache cache)
        {
            path = path.Replace('\\', '/');
            return cache.GetOrLoad(Path + "!" + path, () =>
            {
                if (!Index.Entries.TryGetValue("abdata/" + path, out var ze)) return null;
                return ze.Method == 0
                    ? AssetBundle.LoadFromFile(Path, 0, (ulong)ze.DataOffset)
                    : AssetBundle.LoadFromMemory(Index.ReadAll(ze));
            });
        }

        public override string ToString() => $"{Guid ?? "(no guid)"} {Path}";
    }

    /// <summary>Sideloader `MigrationType`.</summary>
    public enum MigrationKind
    {
        /// <summary>(GUIDOld, IDOld) of one category → (GUIDNew, IDNew).</summary>
        Migrate,
        /// <summary>GUIDOld → GUIDNew for every id.</summary>
        MigrateAll,
        /// <summary>Drop the GUID: the reference takes the compatibility path.</summary>
        StripAll,
    }

    /// <summary>
    /// One `&lt;migrationInfo&gt;&lt;info .../&gt;` line of a zipmod manifest, as Sideloader reads it: `migrationType` (default
    /// Migrate), `guidOld` (required), `guidNew` (default: the manifest's own GUID), and for Migrate `category` (member name
    /// or number), `idOld`, `idNew`. GUIDs trimmed. MigrateAll / StripAll carry category 0 and ids 0.
    /// </summary>
    public sealed class ZipmodMigration
    {
        public MigrationKind Kind;
        public int Category;
        public string GuidOld, GuidNew;
        public int IdOld, IdNew;
        /// <summary>The zipmod whose manifest declares it.</summary>
        public ZipmodInfo Zipmod;

        public override string ToString() => Kind == MigrationKind.Migrate ? $"{Kind} {Category} {GuidOld}#{IdOld} -> {GuidNew}#{IdNew}" : $"{Kind} {GuidOld} -> {GuidNew}";
    }

    /// <summary>Result of <see cref="IContentSource.ScanZipmods"/>.</summary>
    public sealed class ZipmodScanResult
    {
        /// <summary>All zipmod files in enumeration order (including those without a GUID).</summary>
        public readonly List<ZipmodInfo> Zipmods = new List<ZipmodInfo>();
        /// <summary>Number of zipmods read from disk (not taken from the cache) and of unreadable ones.</summary>
        public int Scanned, Unreadable;
        /// <summary>Zipmods of an older cache whose &lt;game&gt; values (version 6) or manifest element names (version 6 and 7)
        /// were read from their manifest alone (the cache upgrade reads nothing else), and zipmods skipped by the game filter.</summary>
        public int GamesRead, ElementsRead, SkippedByGame;
        /// <summary>The cache file was unreadable or incomplete and was ignored (a cold scan).</summary>
        public bool CacheDiscarded;
        public readonly List<string> Errors = new List<string>();
    }
}
