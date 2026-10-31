// The catalog queries reference resolution needs, indexed once per catalog: vanilla
// rows, mod rows by (category, zipmod-local id) in Sideloader's load order, installed GUIDs, and the zipmod manifests'
// <migrationInfo> entries by old GUID and by old id. Build it after ContentCatalog.LoadLists().
using System;
using System.Collections.Generic;
using HS2Next.Content;
using HS2Next.Content.Studio;

namespace HS2Next.Import
{
    public sealed class ResolveCatalog
    {
        public ContentCatalog Catalog { get; }

        readonly Dictionary<int, Dictionary<int, List<ListEntry>>> compat = new Dictionary<int, Dictionary<int, List<ListEntry>>>();
        readonly Dictionary<string, List<ZipmodMigration>> byGuidOld = new Dictionary<string, List<ZipmodMigration>>(StringComparer.Ordinal);
        readonly Dictionary<int, List<ZipmodMigration>> byIdOld = new Dictionary<int, List<ZipmodMigration>>();
        static readonly List<ListEntry> NoRows = new List<ListEntry>();
        static readonly List<ZipmodMigration> NoMigrations = new List<ZipmodMigration>();

        /// <summary>Number of migration entries of the installed zipmods (one zipmod per GUID).</summary>
        public int MigrationCount { get; }

        public ResolveCatalog(ContentCatalog catalog)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            int n = 0;
            // Sideloader collects every loaded manifest's entries in load order (GUID order) and looks them up by the
            // old GUID (ordinal) and by the old id.
            foreach (var m in catalog.Migrations)
            {
                n++;
                if (!byGuidOld.TryGetValue(m.GuidOld, out var g)) byGuidOld[m.GuidOld] = g = new List<ZipmodMigration>();
                g.Add(m);
                if (!byIdOld.TryGetValue(m.IdOld, out var i)) byIdOld[m.IdOld] = i = new List<ZipmodMigration>();
                i.Add(m);
            }
            MigrationCount = n;
        }

        /// <summary>A base-game row of <paramref name="category"/> with id <paramref name="id"/> exists (after the list gate).</summary>
        public bool IsVanillaRow(int category, int id) => Catalog.TryGetEntry(category, ContentId.Vanilla(id), out _);

        /// <summary>The row (<paramref name="guid"/>, <paramref name="id"/>) exists in <paramref name="category"/> (GUID ignoring case).</summary>
        public bool HasRow(int category, string guid, int id) =>
            !string.IsNullOrEmpty(guid) && Catalog.TryGetEntry(category, new ContentId(guid, id), out _);

        /// <summary>A zipmod with this GUID is installed (ignoring case, as the catalog compares GUIDs).</summary>
        public bool IsInstalled(string guid) => !string.IsNullOrEmpty(guid) && Catalog.ZipmodsByGuid.ContainsKey(guid);

        /// <summary>The installed zipmod of a GUID, or null.</summary>
        public ZipmodInfo Zipmod(string guid) =>
            !string.IsNullOrEmpty(guid) && Catalog.ZipmodsByGuid.TryGetValue(guid, out var z) ? z : null;

        /// <summary>
        /// Mod rows of <paramref name="category"/> whose zipmod-local id is <paramref name="id"/>, in Sideloader's load order
        /// (zipmods by GUID, then list order): the compatibility path's candidates.
        /// </summary>
        public IReadOnlyList<ListEntry> CompatCandidates(int category, int id)
        {
            if (!compat.TryGetValue(category, out var byId))
            {
                byId = new Dictionary<int, List<ListEntry>>();
                var rows = new List<(ListEntry e, int seq)>();
                int seq = 0;
                foreach (var e in Catalog.EntriesOf(category)) if (!e.IsVanilla) rows.Add((e, seq++));
                rows.Sort((a, b) =>
                {
                    int c = StudioCsv.LoadOrder.Compare(a.e.Key.SourceGuid, b.e.Key.SourceGuid);
                    return c != 0 ? c : a.seq.CompareTo(b.seq);
                });
                foreach (var (e, _) in rows)
                {
                    if (!byId.TryGetValue(e.Key.Id, out var l)) byId[e.Key.Id] = l = new List<ListEntry>();
                    l.Add(e);
                }
                compat[category] = byId;
            }
            return byId.TryGetValue(id, out var r) ? r : NoRows;
        }

        /// <summary>Migration entries whose old GUID is exactly <paramref name="guid"/> (Sideloader compares ordinally), in load order.</summary>
        public IReadOnlyList<ZipmodMigration> ByGuidOld(string guid) =>
            guid != null && byGuidOld.TryGetValue(guid, out var l) ? l : NoMigrations;

        /// <summary>Migration entries whose old id is <paramref name="id"/>, in load order.</summary>
        public IReadOnlyList<ZipmodMigration> ByIdOld(int id) => byIdOld.TryGetValue(id, out var l) ? l : NoMigrations;
    }
}
