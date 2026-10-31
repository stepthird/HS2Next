// The Maker lists over the app's ContentCatalog: IListRows on the catalog's list DB (rows in
// EntriesOf order, the catalog's first-definition-wins rule already applied) and the manifest infos of the zipmods with
// head / face-skin rows (MakerInfoCache, one cache file per source next to its zipmod list cache:
// `makerinfo-<source id>-<hash>.tsv`). MakerCatalog.For(catalog) keeps one MakerLists per catalog and rebuilds it when the
// catalog's lists were reloaded. The catalog's distributionNo gate drops gated vanilla rows at load
// (`Hs2InstallSource.AiSpecial02/03/04`), so they never appear here (unchanged until H1). Main thread only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace HS2Next.Content.Maker
{
    public sealed class CatalogListRows : IListRows
    {
        readonly ContentCatalog catalog;
        readonly Dictionary<int, List<ListRow>> rows = new Dictionary<int, List<ListRow>>();
        readonly Dictionary<ListEntry, ListRow> wrapped = new Dictionary<ListEntry, ListRow>();
        readonly Dictionary<string, ZipMakerInfo> infos = new Dictionary<string, ZipMakerInfo>(StringComparer.OrdinalIgnoreCase);
        List<ZipMakerInfo> loadOrder;

        /// <summary>Manifests read from disk (not from the caches), skipped lines, load time.</summary>
        public int InfosScanned { get; private set; }
        public readonly List<string> InfoErrors = new List<string>();
        public double InfoLoadMs { get; private set; }

        public ContentCatalog Catalog => catalog;

        public CatalogListRows(ContentCatalog catalog)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            LoadInfos();
        }

        /// <summary>The Maker info cache file of a source: its zipmod list cache name with `makerinfo-` for `modlists-` (null: no cache).</summary>
        public static string InfoCacheFileFor(ContentCatalog catalog, IContentSource source)
        {
            var f = catalog.CacheFileFor(source);
            if (f == null) return null;
            var name = Path.GetFileName(f);
            return Path.Combine(Path.GetDirectoryName(f), name.StartsWith("modlists-", StringComparison.Ordinal) ? "makerinfo-" + name.Substring("modlists-".Length) : "makerinfo-" + name);
        }

        void LoadInfos()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // the zipmods that define head or face-skin rows, per source
            var bySource = new Dictionary<IContentSource, List<(string, string)>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (int cat in new[] { 210, 211, 110, 111 })
                foreach (var e in catalog.EntriesOf(cat))
                {
                    var z = e.Zipmod;
                    if (z == null || z.Guid == null || !seen.Add(z.Path)) continue;
                    if (!bySource.TryGetValue(z.Source, out var l)) bySource[z.Source] = l = new List<(string, string)>();
                    l.Add((z.Path, z.Guid));
                }
            foreach (var kv in bySource)
            {
                var c = MakerInfoCache.Load(InfoCacheFileFor(catalog, kv.Key), kv.Value);
                InfosScanned += c.Scanned;
                InfoErrors.AddRange(c.Errors);
                foreach (var z in c.ByPath) infos[z.Key] = z.Value;
            }
            // Sideloader's load order: one zipmod per GUID (the catalog's choice), GUIDs in load order
            loadOrder = catalog.ZipmodsByGuid.OrderBy(kv => kv.Key, GuidLoadOrder.Instance)
                .Select(kv => infos.TryGetValue(kv.Value.Path, out var z) ? z : null).Where(z => z != null).ToList();
            InfoLoadMs = sw.Elapsed.TotalMilliseconds;
        }

        ListRow Wrap(ListEntry e)
        {
            if (e == null) return null;
            if (!wrapped.TryGetValue(e, out var r)) wrapped[e] = r = new ListRow(e.Category, e.Key, e.Values, e.ZipPath);
            return r;
        }

        public IReadOnlyList<ListRow> RowsOf(int category)
        {
            if (rows.TryGetValue(category, out var l)) return l;
            var src = catalog.EntriesOf(category);
            l = new List<ListRow>(src.Count);
            foreach (var e in src) l.Add(Wrap(e));
            rows[category] = l;
            return l;
        }

        public ListRow Find(int category, ContentId key) => catalog.TryGetEntry(category, key, out var e) ? Wrap(e) : null;

        readonly Dictionary<int, Dictionary<ContentId, IReadOnlyList<ListRow>>> repeated = new Dictionary<int, Dictionary<ContentId, IReadOnlyList<ListRow>>>();

        /// <summary>
        /// References of <paramref name="category"/> defined several times inside the zipmod the catalog took them from, every
        /// definition in file order (ZipmodInfo.Entries keeps them; the catalog's row is the first).
        /// </summary>
        public IReadOnlyDictionary<ContentId, IReadOnlyList<ListRow>> RepeatedDefinitions(int category)
        {
            if (repeated.TryGetValue(category, out var d)) return d;
            d = new Dictionary<ContentId, IReadOnlyList<ListRow>>();
            var zips = new HashSet<ZipmodInfo>();
            foreach (var e in catalog.EntriesOf(category)) if (e.Zipmod != null) zips.Add(e.Zipmod);
            foreach (var z in zips)
            {
                Dictionary<ContentId, List<ListEntry>> byKey = null;
                foreach (var e in z.Entries)
                {
                    if (e.Category != category) continue;
                    byKey ??= new Dictionary<ContentId, List<ListEntry>>();
                    if (!byKey.TryGetValue(e.Key, out var l)) byKey[e.Key] = l = new List<ListEntry>();
                    l.Add(e);
                }
                if (byKey == null) continue;
                foreach (var kv in byKey)
                {
                    if (kv.Value.Count < 2 || !ReferenceEquals(catalog.GetEntry(category, kv.Key), kv.Value[0])) continue;
                    var defs = new List<ListRow>(kv.Value.Count) { Wrap(kv.Value[0]) };
                    for (int i = 1; i < kv.Value.Count; i++) defs.Add(new ListRow(category, kv.Key, kv.Value[i].Values, z.Path));
                    d[kv.Key] = defs;
                }
            }
            repeated[category] = d;
            return d;
        }

        public ZipMakerInfo InfoOf(string zipPath) => zipPath != null && infos.TryGetValue(zipPath, out var z) ? z : null;

        public IReadOnlyList<ZipMakerInfo> InfosInLoadOrder => loadOrder;

        /// <summary>The catalog row behind a list row (for bundle and asset loads), or null.</summary>
        public ListEntry EntryOf(ListRow r) => r == null ? null : catalog.GetEntry(r.Category, r.Key);
    }

    /// <summary>One <see cref="MakerLists"/> per catalog, rebuilt after the catalog's lists were reloaded.</summary>
    public static class MakerCatalog
    {
        sealed class Holder { public MakerLists Lists; public long Stamp; }
        static readonly ConditionalWeakTable<ContentCatalog, Holder> lists = new ConditionalWeakTable<ContentCatalog, Holder>();

        static long StampOf(ContentCatalog c) => ((long)c.Count << 32) ^ c.ModRowsLoaded ^ ((long)c.ZipmodsByGuid.Count << 16);

        /// <summary>The Maker lists of <paramref name="catalog"/> (built on first use: reads the Maker info caches).</summary>
        public static MakerLists For(ContentCatalog catalog)
        {
            if (catalog == null) return null;
            var h = lists.GetOrCreateValue(catalog);
            long stamp = StampOf(catalog);
            if (h.Lists == null || h.Stamp != stamp)
            {
                h.Lists = new MakerLists(new CatalogListRows(catalog));
                h.Stamp = stamp;
                var rows = (CatalogListRows)h.Lists.Source;
                catalog.Log.Add($"maker lists: manifest infos of {rows.InfosInLoadOrder.Count} zipmods ({rows.InfosScanned} read, {rows.InfoErrors.Count} notes) in {rows.InfoLoadMs:0} ms");
            }
            return h.Lists;
        }

        /// <summary>Forgets the lists of a catalog (the next <see cref="For"/> rebuilds them).</summary>
        public static void Forget(ContentCatalog catalog) { if (catalog != null) lists.Remove(catalog); }
    }
}
