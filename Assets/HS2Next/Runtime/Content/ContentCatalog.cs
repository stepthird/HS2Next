// Composes content sources in priority order (first added = highest priority) and owns the
// list DB and every loaded AssetBundle. The list DB is one table per category keyed by
// ContentId: vanilla rows under (illusion.hs2, original ID), zipmod and pack
// rows under (their GUID, their own ID). Nothing is renumbered. Cards are mapped onto it through
// their Sideloader resolve info (Resolve / KeyFor). The Studio lists (items, anime, maps, ...)
// live in a separate StudioDb built by LoadStudioLists(). Main thread only.
// Phase 4.4: the zipmods are the zipmod loader's set (the <game> filter, one file per GUID by the manifest version, GUID load
// order) and every named asset load follows its per-asset redirect (AssetRedirect.cs): a load from a bundle path that
// several sources ship is served by the first copy in load order that holds the asset's name, else by the abdata file. Only
// the serving copy is opened; before a zipmod copy of a path that also has an abdata file opens, that path's abdata
// dependencies are loaded. Two bundles whose files share a name cannot be open at once: Unity's refusal is reported as the
// finding content.cabClash (CabClashes) and the other open copy of the same path is unloaded when nothing loaded from it was
// handed out, else the asset is served from that open copy. The switches SideloaderZipmodSet / SideloaderAssetRedirect
// (default on) give the earlier sources back.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using HS2Next.Content.Studio;
using HS2Next.Data;
using UnityEngine;

namespace HS2Next.Content
{
    // Split over several files (partial): this one holds the list DB, the bundle cache and the open points; other parts
    // implement the open-point hooks (OnBundleOpening / OnBundleBytes).
    public partial class ContentCatalog : IBundleCache
    {
        readonly List<IContentSource> sources = new List<IContentSource>();
        readonly Dictionary<string, AssetBundle> bundles = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);
        // abdata-relative path -> bundle from the first source that has it (null = nobody has it).
        readonly Dictionary<string, AssetBundle> byPath = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ZipmodInfo> shippedBy;
        // Studio asset resolution: bundle existence memo and dependency manifests.
        readonly Dictionary<string, bool> inSources = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, List<ZipmodInfo>> declaredManifests;
        readonly Dictionary<string, DependencyManifest> depManifests = new Dictionary<string, DependencyManifest>(StringComparer.OrdinalIgnoreCase);

        sealed class DependencyManifest
        {
            public AssetBundleManifest Manifest;
            public ZipmodInfo Zipmod;
            public Dictionary<string, string> Names;    // any case -> name as stored in the manifest
        }

        // ---------- list DB state ----------

        // Where a row came from; decides which row wins for a key and the order of EntriesOf.
        // Source = index in `sources` (lower wins). Zip = index of the zipmod in its source's
        // scan, -1 for the source's own lists (within a source a higher Zip wins; equal Zip: the
        // earlier row, as the base game's list loader and Sideloader's resolution keep the first
        // definition of an id). Seq = global insertion counter (file order).
        struct Rank { public int Source, Zip; public long Seq; }

        sealed class Slot { public ListEntry Entry; public Rank Rank; }

        sealed class Table
        {
            public readonly Dictionary<ContentId, Slot> ByKey = new Dictionary<ContentId, Slot>();
            public List<ListEntry> Ordered;                              // built on demand
            public Dictionary<string, List<ListEntry>> ByGuid;           // built with Ordered
        }

        readonly SortedDictionary<int, Table> tables = new SortedDictionary<int, Table>();
        long seq;

        /// <summary>Sources in priority order (index 0 wins).</summary>
        public IReadOnlyList<IContentSource> Sources => sources;

        /// <summary>Directory for per-source caches (zipmod list scan), or null for no caching.</summary>
        public string CacheDir { get; }

        /// <summary>The zipmod used for each GUID (the higher-priority source wins; within a source the last file).</summary>
        public readonly Dictionary<string, ZipmodInfo> ZipmodsByGuid = new Dictionary<string, ZipmodInfo>(StringComparer.OrdinalIgnoreCase);

        public readonly List<string> Log = new List<string>();

        /// <param name="cacheDir">Cache directory (see <see cref="CacheFileFor"/>); null disables the zipmod scan cache.</param>
        public ContentCatalog(string cacheDir)
        {
            CacheDir = cacheDir;
            Ledger.Measure = MeasureBundle;
        }

        /// <summary>Appends a source with lower priority than those already added.</summary>
        public ContentCatalog AddSource(IContentSource source) { sources.Add(source); return this; }

        /// <summary>
        /// C9 (measurement H1: HS2 on the reference install lists the 15 distributionNo-gated
        /// character-list rows): when true, <see cref="ContentSettings.aiInstall"/> also opens the character-list gate of the HS2
        /// install (its AI-Shoujo flags), as it opens the Studio lists' gate (T23). True since the 4.0 integration (C9 applied:
        /// HS2 on the reference install lists the 15 rows, H1); false restores the 2.3 gate (I15).
        /// </summary>
        public static bool AiInstallOpensCharacterLists = true;

        /// <summary>
        /// The zipmod loader's set of zipmods (default true, settings key sideloaderZipmodSet): zipmods whose &lt;game&gt; values
        /// name no HS2 game are not loaded, one file per GUID is kept by the manifest version, and "the first zipmod that ships
        /// a path" means the first in GUID load order. False = the earlier set: every file, the last file of the scan per GUID,
        /// scan order. Read by the next zipmod scan.
        /// </summary>
        public static bool SideloaderZipmodSet = true;

        /// <summary>
        /// The per-asset redirect (default true, settings key sideloaderAssetRedirect): every named load from a bundle path
        /// that several sources ship is served by the first copy in load order holding the name (vanilla rows, mod rows, fixed
        /// abdata paths, Studio rows, thumbnails). False = each load reads its row's own source as before. Read at each load;
        /// switching it inside a session with bundles open can make two copies of a path meet (call UnloadAll in between).
        /// </summary>
        public static bool SideloaderAssetRedirect = true;

        /// <summary>
        /// The CAB rewrite (default true): a bundle Unity refuses because an open bundle holds a file of the same name is
        /// loaded from memory with its own file names renamed (same length) and both stay usable; false = the open copy of
        /// the same path serves the request (the finding content.cabClash). Tests switch it off to reach that rule.
        /// </summary>
        public static bool CabRewrite = true;

        /// <summary>
        /// Catalog for the given settings: the HS2 install first, then the extra mods folders. The texture guard's folder
        /// follows the settings' cache directory (<see cref="HS2Next.Chara.TextureGuard.CacheRoot"/>).
        /// </summary>
        public static ContentCatalog FromSettings(ContentSettings s)
        {
            var c = new ContentCatalog(s.ResolvedCacheDir);
            HS2Next.Chara.TextureGuard.CacheRoot = s.ResolvedCacheDir;
            SideloaderZipmodSet = s.sideloaderZipmodSet;
            SideloaderAssetRedirect = s.sideloaderAssetRedirect;
            if (s.content != null)
            {
                c.BundleCacheMB = Math.Max(0, s.content.bundleCacheMB);
                c.UnloadUnusedMinSec = Math.Max(0f, s.content.unloadUnusedMinSec);
            }
            if (!string.IsNullOrEmpty(s.hs2Root))
            {
                bool ai = AiInstallOpensCharacterLists && s.aiInstall;
                c.AddSource(new Hs2InstallSource(ContentSettings.Resolve(s.hs2Root)) { AiSpecial02 = ai, AiSpecial03 = ai, AiSpecial04 = ai });
            }
            foreach (var d in s.modsFolders)
                if (!string.IsNullOrEmpty(d)) c.AddSource(new ModsFolderSource(ContentSettings.Resolve(d)));
            return c;
        }

        /// <summary>Zipmod scan cache file of a source: &lt;CacheDir&gt;/modlists-&lt;id&gt;-&lt;hash of its folder&gt;.tsv.</summary>
        public virtual string CacheFileFor(IContentSource source)
        {
            if (CacheDir == null) return null;
            var bad = Path.GetInvalidFileNameChars();
            var id = new string(source.Id.Select(ch => Array.IndexOf(bad, ch) >= 0 ? '_' : ch).ToArray());
            var folder = (source as ZipmodSourceBase)?.ModsDir;
            return Path.Combine(CacheDir, folder != null ? $"modlists-{id}-{ZipmodSourceBase.PathHash(folder)}.tsv" : $"modlists-{id}.tsv");
        }

        // ---------- lists: loading ----------

        /// <summary>Reads the sources' own lists (vanilla characustom data; later also pack rows).</summary>
        public void LoadVanillaLists()
        {
            int dup0 = DuplicateRows;
            for (int i = 0; i < sources.Count; i++)
                foreach (var e in sources[i].ReadLists(this)) Add(e, i, -1);
            Log.Add($"vanilla list entries: {VanillaCount}");
            foreach (var s in sources.OfType<Hs2InstallSource>())
                Log.Add($"vanilla lists of {s.Id}: {s.GatedRows} rows gated by distributionNo (add50 {s.IsAdd50}), {DuplicateRows - dup0} later duplicates ignored, " +
                        $"{s.IdColumnMismatches} keyed by an ID column that differs from the row key, {s.UnfiledAssets} assets naming no category, {s.CategoryNameMismatches} filed by name under another category");
        }

        /// <summary>Scans the zipmods of every source (cached per source) and adds their list rows.</summary>
        public void LoadModLists() => LoadModLists(ScanModSources());

        /// <summary>
        /// The zipmod scans of every source (cached per source), in source order: what sources[i].ScanZipmods(CacheFileFor(..))
        /// returns. Pure file I/O, no Unity API: may run on a worker thread while nothing else uses
        /// the catalog. <paramref name="progress"/> gets the number of zipmods visited so far (any thread).
        /// </summary>
        public ZipmodScanResult[] ScanModSources(Action<int> progress = null)
        {
            var results = new ZipmodScanResult[sources.Count];
            int before = 0;
            for (int i = 0; i < sources.Count; i++)
            {
                int offset = before;
                string cache = CacheFileFor(sources[i]);
                if (sources[i] is ZipmodSourceBase zb) zb.GameFilter = SideloaderZipmodSet;
                results[i] = sources[i] is ZipmodSourceBase z && progress != null
                    ? z.ScanZipmods(cache, n => progress(offset + n))
                    : sources[i].ScanZipmods(cache);
                before += results[i].Zipmods.Count;
            }
            // the second pass: with every source enumerated, the redirectable paths and the asset names of their copies
            ApplyZipmodSet(results);
            NamesPass = ReadAssetNames(results);
            return results;
        }

        /// <summary>Marks the files of a GUID the zipmod loader does not keep (SkippedBy "version"; with
        /// <see cref="SideloaderZipmodSet"/>; within each source, in scan order). Unity-free; repeatable.</summary>
        static void ApplyZipmodSet(ZipmodScanResult[] results) =>
            SideloaderZipmods.MarkOlderFiles(results.Select(r => (IReadOnlyList<ZipmodInfo>)r.Zipmods), SideloaderZipmodSet);

        // The loaded zipmods per GUID (the higher-priority source wins; within a source the last unskipped file) and their load order.
        static Dictionary<string, ZipmodInfo> LoadedByGuid(ZipmodScanResult[] results)
        {
            var d = new Dictionary<string, ZipmodInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in results)
            {
                var local = new Dictionary<string, ZipmodInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (var z in r.Zipmods) if (z.Guid != null && z.SkippedBy == null) local[z.Guid] = z;
                foreach (var kv in local) if (!d.ContainsKey(kv.Key)) d[kv.Key] = kv.Value;
            }
            return d;
        }

        /// <summary>Merges the results of <see cref="ScanModSources"/> (main thread): adds the zipmods' list rows.</summary>
        public void LoadModLists(ZipmodScanResult[] results)
        {
            if (results == null || results.Length != sources.Count) throw new ArgumentException("one scan result per source expected", nameof(results));
            int scanned = 0, bad = 0;
            for (int i = 0; i < sources.Count; i++)
            {
                scanned += results[i].Scanned; bad += results[i].Unreadable;
                Log.AddRange(results[i].Errors);
            }
            ApplyZipmodSet(results);
            ZipmodsByGuid.Clear();
            migrations = null;
            shippedBy = null;
            inSources.Clear();
            declaredManifests = null;
            depManifests.Clear();
            loose?.Reindex();
            foreach (var kv in LoadedByGuid(results)) ZipmodsByGuid[kv.Key] = kv.Value;
            loadedInOrder = ZipmodsByGuid.Values.OrderBy(z => z.Guid, StudioCsv.LoadOrder).ToList();
            scanned_ = results.SelectMany(r => r.Zipmods).ToList();
            int n = 0, same0 = SameListDuplicates, diff0 = SameListDuplicatesDiffering;
            for (int i = 0; i < sources.Count; i++)
                for (int zi = 0; zi < results[i].Zipmods.Count; zi++)
                {
                    var z = results[i].Zipmods[zi];
                    if (z.SkippedBy != null) continue;
                    if (z.Guid != null && new ContentId(z.Guid, 0).IsVanilla)
                    {
                        Log.Add($"zipmod uses the reserved namespace {ContentIds.Vanilla}, rows ignored: {z.Path}");
                        continue;
                    }
                    foreach (var e in z.Entries) { Add(e, i, zi); n++; }
                }
            ModRowsLoaded = n;
            Log.Add($"mod list entries: {n} from {ZipmodsByGuid.Count} zipmods ({scanned} scanned, {bad} unreadable)");
            Log.Add($"mod list duplicates: {SameListDuplicates - same0} rows repeat an earlier (category, id) of their zipmod (first kept; {SameListDuplicatesDiffering - diff0} of them differ); " +
                    $"{Migrations.Count} <migrationInfo> entries");
            LogZipmodSet();
            BuildRedirect();
        }

        // ---------- the zipmod set and the per-asset redirect (AssetRedirect.cs) ----------

        List<ZipmodInfo> loadedInOrder = new List<ZipmodInfo>();
        List<ZipmodInfo> scanned_ = new List<ZipmodInfo>();
        AssetRedirect redirect;
        HashSet<string> abdataBundles;
        static bool zipmodSetLogged;

        /// <summary>The loaded zipmods (<see cref="ZipmodsByGuid"/>) in GUID load order (lower-case ordinal).</summary>
        public IReadOnlyList<ZipmodInfo> ZipmodsInLoadOrder => loadedInOrder;

        /// <summary>Every zipmod file of the last scan, also those the zipmod loader skips (<see cref="ZipmodInfo.SkippedBy"/>).</summary>
        public IReadOnlyList<ZipmodInfo> ScannedZipmods => scanned_;

        /// <summary>The redirect index of the loaded zipmods (built by <see cref="LoadModLists(ZipmodScanResult[])"/>; null before).</summary>
        public AssetRedirect Redirect => redirect;

        /// <summary>Numbers of the asset-name pass of the last scan (RU3), or null.</summary>
        public NamesPassReport NamesPass { get; private set; }

        public sealed class NamesPassReport
        {
            public double Ms, AbdataMs, IndexMs, CacheReadMs, ReadMs, CacheWriteMs;
            public int Paths, Copies, FromCache, Read, Lzma, Unreadable;
            public string CacheFile, CacheError, WriteError;
            public override string ToString() =>
                $"names pass {Ms:F1} ms ({Paths} redirectable paths, {Copies} copies: {FromCache} cached, {Read} read, {Lzma} LZMA, {Unreadable} unreadable; " +
                $"abdata list {AbdataMs:F1} ms, index {IndexMs:F1} ms, cache read {CacheReadMs:F1} ms, reads {ReadMs:F1} ms, cache write {CacheWriteMs:F1} ms)" +
                (CacheError != null ? "; " + CacheError : "") + (WriteError != null ? "; " + WriteError : "");
        }

        /// <summary>The names cache of this catalog's sources: &lt;CacheDir&gt;/bundlenames-&lt;hash of the sources' folders&gt;.tsv, or null.</summary>
        public string NamesCacheFile()
        {
            if (CacheDir == null) return null;
            uint h = 2166136261;
            foreach (var s in sources)
            {
                string f = (s as ZipmodSourceBase)?.ModsDir ?? s.Id;
                try { f = Path.GetFullPath(f); } catch (Exception) { }
                foreach (char ch in f.TrimEnd('\\', '/').ToLowerInvariant() + "|") { h ^= ch; h *= 16777619; }
            }
            return Path.Combine(CacheDir, "bundlenames-" + h.ToString("x8") + ".tsv");
        }

        // abdata-relative paths (with '/') of the *.unity3d files of every source's own store.
        HashSet<string> ListAbdataBundles()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in sources.OfType<Hs2InstallSource>())
            {
                if (!Directory.Exists(h.Abdata)) continue;
                int cut = h.Abdata.TrimEnd('\\', '/').Length + 1;
                foreach (var f in Directory.EnumerateFiles(h.Abdata, "*.unity3d", SearchOption.AllDirectories))
                    set.Add(f.Substring(cut).Replace('\\', '/'));
            }
            return set;
        }

        // Second pass of the scan (Unity-free; the worker of ScanModSources): the redirectable paths of the loaded zipmods and
        // the asset names of every copy of them, from the names cache when the zip's stamp matches, else read from the zip.
        NamesPassReport ReadAssetNames(ZipmodScanResult[] results)
        {
            var rep = new NamesPassReport();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var t = System.Diagnostics.Stopwatch.StartNew();
            var ab = ListAbdataBundles();
            abdataBundles = ab;
            rep.AbdataMs = t.Elapsed.TotalMilliseconds; t.Restart();
            var order = LoadedByGuid(results).Values.OrderBy(z => z.Guid, StudioCsv.LoadOrder).ToList();
            var idx = AssetRedirect.Build(order, ab.Contains);
            rep.IndexMs = t.Elapsed.TotalMilliseconds; t.Restart();
            rep.CacheFile = NamesCacheFile();
            var cached = BundleNamesCache.Read(rep.CacheFile, out var cerr);
            rep.CacheError = cerr;
            rep.CacheReadMs = t.Elapsed.TotalMilliseconds; t.Restart();
            var entries = new List<(string zip, string stamp, string path, BundleNames names)>();
            foreach (var p in idx.RedirectablePaths)
                foreach (var c in idx.CopiesOf(p))
                {
                    var z = (ZipmodInfo)c;
                    BundleNames names;
                    if (z.Stamp != null && cached.TryGetValue((z.Path, p), out var hit) && hit.stamp == z.Stamp) { names = hit.names; rep.FromCache++; }
                    else
                    {
                        try { names = BundleNames.ReadZipEntry(z.Index, "abdata/" + p); }
                        catch (Exception e) { names = new BundleNames { Status = BundleNames.Kind.Unreadable, Error = e.Message }; }
                        rep.Read++;
                    }
                    if (names.Status == BundleNames.Kind.Lzma) rep.Lzma++;
                    else if (names.Status == BundleNames.Kind.Unreadable) rep.Unreadable++;
                    z.SetNames(p, names);
                    entries.Add((z.Path, z.Stamp, p, names));
                    rep.Copies++;
                }
            rep.Paths = idx.RedirectablePaths.Count;
            rep.ReadMs = t.Elapsed.TotalMilliseconds; t.Restart();
            if (rep.CacheFile != null && rep.Read > 0) rep.WriteError = WriteNames(rep.CacheFile, entries, cached);
            rep.CacheWriteMs = t.Elapsed.TotalMilliseconds;
            rep.Ms = sw.Elapsed.TotalMilliseconds;
            return rep;
        }

        // Writes the names cache: the given copies plus the cached copies of other zipmods that still exist (the copies of
        // the other switch state stay cached, so switching back reads nothing).
        static string WriteNames(string file, List<(string zip, string stamp, string path, BundleNames names)> entries,
                                 Dictionary<(string zip, string path), (string stamp, BundleNames names)> cached)
        {
            var have = new HashSet<(string, string)>(entries.Select(e => (e.zip, e.path)));
            var all = new List<(string zip, string stamp, string path, BundleNames names)>(entries);
            foreach (var kv in cached)
                if (!have.Contains((kv.Key.zip, kv.Key.path)) && File.Exists(kv.Key.zip)) all.Add((kv.Key.zip, kv.Value.stamp, kv.Key.path, kv.Value.names));
            return BundleNamesCache.Write(file, all);
        }

        void BuildRedirect()
        {
            var ab = abdataBundles ?? (abdataBundles = ListAbdataBundles());
            redirect = AssetRedirect.Build(loadedInOrder, ab.Contains);
            redirect.ReadMissingNames = UnityNames;
            if (NamesPass != null) Log.Add(NamesPass.ToString());
            Log.Add($"asset redirect {(SideloaderAssetRedirect ? "on" : "off")}: {redirect.RedirectablePaths.Count} redirectable paths " +
                    $"({redirect.RedirectablePaths.Count(redirect.HasAbdata)} with an abdata file)");
        }

        void LogZipmodSet()
        {
            var game = scanned_.Where(z => z.SkippedBy == "game").Select(z => Path.GetFileName(z.Path)).ToList();
            var older = scanned_.Where(z => z.SkippedBy == "version").Select(z => Path.GetFileName(z.Path) + " (" + z.Guid + ")").ToList();
            string line = SideloaderZipmodSet
                ? $"zipmod set (the zipmod loader's): {loadedInOrder.Count} loaded; skipped by the <game> filter {game.Count}" +
                  (game.Count > 0 ? ": " + string.Join(", ", game) : "") + $"; older files of a GUID {older.Count}" + (older.Count > 0 ? ": " + string.Join(", ", older) : "")
                : $"zipmod set (SideloaderZipmodSet off: every file, the last file per GUID): {loadedInOrder.Count} GUIDs";
            Log.Add(line);
            if (!zipmodSetLogged) { zipmodSetLogged = true; Debug.Log("[content] " + line); }
        }

        // Names of a copy the scan could not read (an LZMA copy; names missing): through Unity, the bundle unloaded again when
        // this read opened it; the names cache is rewritten with them.
        BundleNames UnityNames(IRedirectZip c, string path)
        {
            if (!(c is ZipmodInfo z)) return null;
            string key = z.Path + "!" + path;
            bool open = bundles.TryGetValue(key, out var b) && b;
            if (!open) b = z.OpenBundle(path, this);
            if (!b) return new BundleNames { Status = BundleNames.Kind.Unreadable, Error = "the bundle did not open" };
            var r = new BundleNames { Status = BundleNames.Kind.Ok, Names = new HashSet<string>(b.GetAllAssetNames().Select(BundleNames.ShortName), StringComparer.Ordinal) };
            var prior = z.NamesOf(path);
            r.Compression = prior?.Compression; r.SerializedFiles = prior?.SerializedFiles; r.ResourceFiles = prior?.ResourceFiles; r.Externals = prior?.Externals;
            if (!open) UnloadBundle(b);
            z.SetNames(path, r);
            Log.Add($"asset names of {key} read through Unity ({r.Count})");
            var file = NamesCacheFile();
            if (file != null)
            {
                var entries = new List<(string zip, string stamp, string path, BundleNames names)>();
                foreach (var p in redirect.RedirectablePaths)
                    foreach (var cc in redirect.CopiesOf(p))
                        if (cc is ZipmodInfo zz && zz.NamesOf(p) is BundleNames nn) entries.Add((zz.Path, zz.Stamp, p, nn));
                var err = WriteNames(file, entries, BundleNamesCache.Read(file, out _));
                if (err != null) Log.Add(err);
            }
            return r;
        }

        /// <summary>Both of the above.</summary>
        public void LoadLists() { LoadVanillaLists(); LoadModLists(); }

        // ---------- Studio lists ----------

        /// <summary>The Studio list DB of the last <see cref="LoadStudioLists"/>, or null.</summary>
        public StudioDb StudioLists { get; private set; }

        /// <summary>
        /// Builds the Studio list DB from the vanilla
        /// abdata/studio/info bundles of the first source that has them and from the Studio CSVs of
        /// the zipmods in <see cref="ZipmodsByGuid"/> (call <see cref="LoadLists"/> or
        /// <see cref="LoadModLists"/> first; the CSV rows come from the zipmod scan cache). The info
        /// bundles are unloaded again. Also kept in <see cref="StudioLists"/>.
        /// </summary>
        public StudioDb LoadStudioLists(StudioLoadOptions options = null)
        {
            var owner = NewOwner("studio:lists");
            try { using (Own(owner)) StudioLists = StudioLoader.Build(this, options); }
            finally { Release(owner); }
            Log.Add($"studio lists: {StudioLists.Items.Count} items, {StudioLists.Anime.Count} anime, {StudioLists.Maps.Count} maps in {StudioLists.TimingsMs["total"]} ms; {StudioLists.Diagnostics.Count} diagnostics");
            return StudioLists;
        }

        void Add(ListEntry e, int source, int zip)
        {
            if (e.Key.IsEmpty) return;
            if (!tables.TryGetValue(e.Category, out var t)) tables[e.Category] = t = new Table();
            var rank = new Rank { Source = source, Zip = zip, Seq = seq++ };
            if (t.ByKey.TryGetValue(e.Key, out var old))
            {
                // Keep the old row if it comes from a higher-priority source, from a later zipmod of the
                // same source, or from the same zipmod / the same lists (the first definition of an id wins:
                // the base game skips ids it already has, Sideloader resolves to the first row).
                if (old.Rank.Source < source || (old.Rank.Source == source && old.Rank.Zip >= zip))
                {
                    DuplicateRows++;
                    if (old.Rank.Source == source && old.Rank.Zip == zip)
                    {
                        SameListDuplicates++;
                        if (!SameValues(old.Entry, e)) SameListDuplicatesDiffering++;
                    }
                    return;
                }
                old.Entry = e; old.Rank = rank;
            }
            else t.ByKey[e.Key] = new Slot { Entry = e, Rank = rank };
            t.Ordered = null; t.ByGuid = null;
        }

        // ---------- lists: queries ----------

        /// <summary>Number of zipmod rows read by the last <see cref="LoadModLists"/> (before de-duplication by key).</summary>
        public int ModRowsLoaded { get; private set; }

        /// <summary>Rows not taken because a row of the same (category, key) came first from the same zipmod or lists, or from a higher-priority source.</summary>
        public int DuplicateRows { get; private set; }

        /// <summary>Of <see cref="DuplicateRows"/>: the ones from the same zipmod or the same lists as the kept row (the
        /// first-wins rule decides them), and how many of those differ from the kept row in some column.</summary>
        public int SameListDuplicates { get; private set; }
        public int SameListDuplicatesDiffering { get; private set; }

        static bool SameValues(ListEntry a, ListEntry b)
        {
            if (a.Values.Count != b.Values.Count) return false;
            foreach (var kv in a.Values)
                if (!b.Values.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
            return true;
        }

        /// <summary>
        /// The `&lt;migrationInfo&gt;` entries of the zipmods in <see cref="ZipmodsByGuid"/>, in Sideloader's load order (zipmods by
        /// GUID, then manifest order): what Sideloader collects and applies when it resolves card references.
        /// </summary>
        public IReadOnlyList<ZipmodMigration> Migrations
        {
            get
            {
                if (migrations != null) return migrations;
                migrations = new List<ZipmodMigration>();
                foreach (var z in ZipmodsByGuid.Values.OrderBy(z => z.Guid, StudioCsv.LoadOrder)) migrations.AddRange(z.Migrations);
                return migrations;
            }
        }
        List<ZipmodMigration> migrations;

        /// <summary>Categories that have at least one row, ascending.</summary>
        public IEnumerable<int> Categories => tables.Keys;

        /// <summary>Number of distinct (category, key) rows in the DB.</summary>
        public int Count => tables.Values.Sum(t => t.ByKey.Count);

        /// <summary>Number of distinct base-game rows.</summary>
        public int VanillaCount => tables.Values.Sum(t => t.ByKey.Keys.Count(k => k.IsVanilla));

        /// <summary>Number of distinct mod and pack rows.</summary>
        public int ModCount => Count - VanillaCount;

        /// <summary>Number of rows in one category.</summary>
        public int CountOf(int category) => tables.TryGetValue(category, out var t) ? t.ByKey.Count : 0;

        /// <summary>The row for (category, key), after the priority rules.</summary>
        public bool TryGetEntry(int category, ContentId key, out ListEntry entry)
        {
            entry = null;
            if (key.IsEmpty || !tables.TryGetValue(category, out var t) || !t.ByKey.TryGetValue(key, out var s)) return false;
            entry = s.Entry;
            return true;
        }

        /// <summary>The row for (category, key), or null.</summary>
        public ListEntry GetEntry(int category, ContentId key) => TryGetEntry(category, key, out var e) ? e : null;

        /// <summary>
        /// All rows of a category: vanilla rows first, then the others by source priority, then by
        /// the source's own lists before its zipmods (in scan order), then file order.
        /// </summary>
        public IReadOnlyList<ListEntry> EntriesOf(int category) =>
            tables.TryGetValue(category, out var t) ? Build(t).Ordered : (IReadOnlyList<ListEntry>)Array.Empty<ListEntry>();

        /// <summary>Rows of one category in one namespace (case-insensitive GUID; <see cref="ContentIds.Vanilla"/> for base-game rows), in <see cref="EntriesOf"/> order.</summary>
        public IReadOnlyList<ListEntry> EntriesOf(int category, string sourceGuid) =>
            sourceGuid != null && tables.TryGetValue(category, out var t) && Build(t).ByGuid.TryGetValue(sourceGuid, out var l)
                ? l : (IReadOnlyList<ListEntry>)Array.Empty<ListEntry>();

        static Table Build(Table t)
        {
            if (t.Ordered != null) return t;
            t.Ordered = t.ByKey.Values
                .OrderBy(s => s.Entry.Key.IsVanilla ? 0 : 1)
                .ThenBy(s => s.Rank.Source).ThenBy(s => s.Rank.Zip).ThenBy(s => s.Rank.Seq)
                .Select(s => s.Entry).ToList();
            t.ByGuid = new Dictionary<string, List<ListEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in t.Ordered)
            {
                if (!t.ByGuid.TryGetValue(e.Key.SourceGuid, out var l)) t.ByGuid[e.Key.SourceGuid] = l = new List<ListEntry>();
                l.Add(e);
            }
            return t;
        }

        // ---------- cards ----------

        /// <summary>
        /// The reference a card means for a part: (resolve GUID, resolve Slot) if the card has
        /// Sideloader resolve info for <paramref name="property"/> in <paramref name="category"/>,
        /// else the vanilla ID. Returned whether or not the DB has the row, so importers can keep
        /// references to missing mods.
        /// </summary>
        public ContentId KeyFor(Card card, int category, int id, string property)
        {
            var r = property != null ? card.FindResolve(property, category) : null;
            return r != null && !string.IsNullOrEmpty(r.GUID) ? new ContentId(r.GUID, r.Slot) : ContentId.Vanilla(id);
        }

        /// <summary>
        /// Resolve a card ID: the mod row if the card has resolve info for it, else (also when that
        /// mod row is missing, which is logged) the vanilla row; null if neither exists.
        /// </summary>
        public ListEntry Resolve(Card card, int category, int id, string property)
        {
            var r = property != null ? card.FindResolve(property, category) : null;
            if (r != null)
            {
                if (!string.IsNullOrEmpty(r.GUID) && TryGetEntry(category, new ContentId(r.GUID, r.Slot), out var me)) return me;
                Log.Add($"missing mod {r}");
            }
            return GetEntry(category, ContentId.Vanilla(id));
        }

        // ---------- bundles ----------

        /// <summary>Opens a bundle by abdata-relative path from the first source that has it. A path no source has is
        /// remembered as missing (logged once); a file that exists but did not open is retried at the next request.</summary>
        public AssetBundle LoadAbdataBundle(string rel)
        {
            rel = rel.Replace('\\', '/');
            if (byPath.TryGetValue(rel, out var b) && (b || !HasRealBundle(rel))) { NoteBundleUse(null, b, false); return b; }
            b = null;
            bool found = false;
            foreach (var s in sources)
                if (s.HasBundle(rel)) { found = true; b = s.OpenBundle(rel, this); break; }
            if (b || !found) byPath[rel] = b;
            if (!b) LogOnce(found ? $"bundle did not open: {rel}" : $"bundle not found: {rel}");
            return b;
        }

        /// <summary>
        /// Bundle for a list entry: the entry's zipmod if it ships the file, else (for mod
        /// entries) the first zipmod that does, else the abdata bundle. A zipmod bundle that does not open is logged.
        /// </summary>
        public AssetBundle LoadBundle(ListEntry e, string bundlePath)
        {
            if (string.IsNullOrEmpty(bundlePath) || bundlePath == "0") return null;
            bundlePath = bundlePath.Replace('\\', '/');
            var z = e?.Zipmod;
            if (z != null && z.HasBundle(bundlePath)) return OpenZipCopy(z, bundlePath);
            // Mods may also reference bundles shipped by other mods.
            if (z != null && FindZipmodShipping(bundlePath) is ZipmodInfo other) return OpenZipCopy(other, bundlePath);
            return LoadAbdataBundle(bundlePath);
        }

        // A zipmod's copy of a path through the bundle cache; a copy that does not open is logged once.
        AssetBundle OpenZipCopy(ZipmodInfo z, string bundlePath)
        {
            var b = z.OpenBundle(bundlePath, this);
            if (!b) LogOnce($"zipmod bundle did not open: {z.Path}!{bundlePath}");
            return b;
        }

        readonly HashSet<string> logged = new HashSet<string>(StringComparer.Ordinal);
        void LogOnce(string line) { if (logged.Add(line)) Log.Add(line); }

        /// <summary>True if some source's own store (abdata) has the abdata-relative path (memoized).</summary>
        public bool HasRealBundle(string bundlePath)
        {
            if (string.IsNullOrEmpty(bundlePath)) return false;
            bundlePath = bundlePath.Replace('\\', '/');
            if (!inSources.TryGetValue(bundlePath, out var e)) inSources[bundlePath] = e = sources.Any(s => s.HasBundle(bundlePath));
            return e;
        }

        /// <summary>True if the bundle path exists as a real file of a source or in any loaded zipmod (one namespace).</summary>
        public bool BundleExists(string bundlePath) =>
            !string.IsNullOrEmpty(bundlePath) && (HasRealBundle(bundlePath) || FindZipmodShipping(bundlePath) != null);

        /// <summary>
        /// Bundle for a Studio record: the real file if a source has it, else the
        /// record's own zipmod, else the first zipmod that ships the path. If <paramref name="manifest"/>
        /// names a dependency manifest declared by a zipmod (`&lt;hs2-scene-dependency manifest="X"&gt;`),
        /// abdata/X.unity3d is read as an AssetBundleManifest and the bundle's dependencies are loaded
        /// first. Other manifest values (empty, `abdata`, vanilla `studioNN`/`addNN`) need nothing.
        /// </summary>
        public AssetBundle LoadBundle(ZipmodInfo owner, string bundlePath, string manifest)
        {
            if (string.IsNullOrEmpty(bundlePath) || bundlePath == "0") return null;
            bundlePath = bundlePath.Replace('\\', '/');
            PreloadDependencies(owner, bundlePath, manifest);
            return OpenAnywhere(owner, bundlePath);
        }

        AssetBundle OpenAnywhere(ZipmodInfo prefer, string bundlePath)
        {
            if (HasRealBundle(bundlePath)) return LoadAbdataBundle(bundlePath);
            if (prefer != null && prefer.HasBundle(bundlePath)) return OpenZipCopy(prefer, bundlePath);
            var z = FindZipmodShipping(bundlePath);
            if (z != null) return OpenZipCopy(z, bundlePath);
            LogOnce($"bundle not found: {bundlePath}");
            return null;
        }

        /// <summary>Loads the record's asset (prefab, controller, texture, ...) from its bundle (see <see cref="LoadBundle(ZipmodInfo, string, string)"/>).</summary>
        public T LoadAsset<T>(IStudioAsset record) where T : UnityEngine.Object =>
            record == null ? null : LoadAssetAt<T>(record.Zipmod, record.BundlePath, record.Manifest, record.AssetName);

        /// <summary>Loads <paramref name="asset"/> from a bundle resolved like a Studio record's (see
        /// <see cref="LoadBundle(ZipmodInfo, string, string)"/>), through the texture guard; for Studio-side references that
        /// are not an <see cref="IStudioAsset"/> (anime option items and their controllers, H override controllers).
        /// A path several sources ship follows the per-asset redirect.</summary>
        public T LoadAssetAt<T>(ZipmodInfo owner, string bundlePath, string manifest, string asset) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(asset)) return null;
            if (LooseLoad(bundlePath, asset, out T lt)) return lt;   // Sideloader's loose PNG wins (GB10)
            if (Redirects(bundlePath, asset, out var served))
            {
                PreloadDependencies(owner, bundlePath.Replace('\\', '/'), manifest);
                return LoadServed<T>(served, bundlePath, asset, StudioToday(owner, bundlePath));
            }
            var b = LoadBundle(owner, bundlePath, manifest);
            return b ? GuardLoad<T>(b, asset, Label(owner, bundlePath)) : null;
        }

        /// <summary>Loads <paramref name="asset"/> from a bundle the caller opened through this catalog with its own lookup
        /// order (e.g. a declaring zipmod first), through the texture guard; <paramref name="owner"/> and
        /// <paramref name="bundlePath"/> name the source in the guard's records. On a path several sources ship the redirect
        /// decides: the caller's bundle serves only when the rule picks its copy.</summary>
        public T LoadAssetFrom<T>(AssetBundle bundle, string asset, ZipmodInfo owner, string bundlePath) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(asset)) return null;
            if (LooseLoad(bundlePath, asset, out T lt)) return lt;   // Sideloader's loose PNG wins (GB10)
            if (Redirects(bundlePath, asset, out var served))
            {
                var mine = CopyOf(bundle, owner, bundlePath);
                if (!served.Same(mine)) return LoadServed<T>(served, bundlePath, asset, mine);
            }
            return bundle ? GuardLoad<T>(bundle, asset, Label(owner, bundlePath)) : null;
        }

        // ---------- the per-asset redirect: the rule at the catalog's entry points ----------

        /// <summary>True when the redirect decides this named load (the switch on, the path shipped by several sources);
        /// <paramref name="served"/> is then the serving source.</summary>
        bool Redirects(string bundlePath, string asset, out AssetRedirect.Served served)
        {
            served = default;
            if (!SideloaderAssetRedirect || redirect == null || string.IsNullOrEmpty(bundlePath) || bundlePath == "0" || string.IsNullOrEmpty(asset) || asset == "0") return false;
            var p = bundlePath.Replace('\\', '/');
            if (!redirect.IsRedirectable(p)) return false;
            served = redirect.Serve(p, asset);
            return true;
        }

        /// <summary>Opens the serving source of a named load: the abdata file, or a zipmod's copy (the path's abdata dependencies
        /// loaded first when the path also has an abdata file). Null when it does not open.</summary>
        AssetBundle OpenServed(AssetRedirect.Served s, string bundlePath)
        {
            bundlePath = bundlePath.Replace('\\', '/');
            if (s.Abdata) return LoadAbdataBundle(bundlePath);
            if (!(s.Zip is ZipmodInfo z)) return null;
            if (HasRealBundle(bundlePath)) PreloadAbdataDependencies(bundlePath);
            return OpenZipCopy(z, bundlePath);
        }

        // A load the rule serves: opened, counted when it differs from today's source, loaded through the texture guard.
        T LoadServed<T>(AssetRedirect.Served s, string bundlePath, string asset, AssetRedirect.Served today) where T : UnityEngine.Object
        {
            NoteServed(s, bundlePath, asset, today);
            var b = OpenServed(s, bundlePath);
            return b ? GuardLoad<T>(b, asset, Label(s.Zip as ZipmodInfo, bundlePath)) : null;
        }

        void NoteServed(AssetRedirect.Served s, string bundlePath, string asset, AssetRedirect.Served today)
        {
            if (s.Same(today)) return;
            if (redirect.NoteRedirect(bundlePath, asset, s, today))
                Log.Add($"redirect: {bundlePath.Replace('\\', '/')} {asset} <- {s.Describe()} (was {today.Describe()})");
        }

        static AssetRedirect.Served Zip(ZipmodInfo z) => new AssetRedirect.Served { Zip = z };
        static readonly AssetRedirect.Served AbdataServed = new AssetRedirect.Served { Abdata = true };

        /// <summary>The source a list row's load of <paramref name="bundlePath"/> read before the redirect: the row's zipmod when
        /// it ships the path, else (mod rows) the first zipmod that does, else the abdata file.</summary>
        public AssetRedirect.Served TodayOf(ListEntry e, string bundlePath)
        {
            if (string.IsNullOrEmpty(bundlePath)) return default;
            bundlePath = bundlePath.Replace('\\', '/');
            var z = e?.Zipmod;
            if (z != null && z.HasBundle(bundlePath)) return Zip(z);
            if (z != null && FindZipmodShipping(bundlePath) is ZipmodInfo other) return Zip(other);
            return HasRealBundle(bundlePath) ? AbdataServed : default;
        }

        // A Studio record's source before the redirect: the abdata file, else the owner's copy, else the first zipmod shipping it.
        AssetRedirect.Served StudioToday(ZipmodInfo owner, string bundlePath)
        {
            bundlePath = bundlePath.Replace('\\', '/');
            if (HasRealBundle(bundlePath)) return AbdataServed;
            if (owner != null && owner.HasBundle(bundlePath)) return Zip(owner);
            return FindZipmodShipping(bundlePath) is ZipmodInfo z ? Zip(z) : default;
        }

        // The copy a bundle the caller opened is (by its cache key), else the owner's when it ships the path, else the abdata file.
        AssetRedirect.Served CopyOf(AssetBundle bundle, ZipmodInfo owner, string bundlePath)
        {
            string key = bundle ? KeyOf(bundle) : null;
            if (key != null)
            {
                int bang = key.IndexOf('!');
                if (bang < 0) return AbdataServed;
                string zip = key.Substring(0, bang);
                if (owner != null && string.Equals(owner.Path, zip, StringComparison.OrdinalIgnoreCase)) return Zip(owner);
                var z = loadedInOrder.FirstOrDefault(x => string.Equals(x.Path, zip, StringComparison.OrdinalIgnoreCase));
                if (z != null) return Zip(z);
            }
            if (owner != null && owner.HasBundle(bundlePath)) return Zip(owner);
            return HasRealBundle(bundlePath) ? AbdataServed : default;
        }

        // The abdata manifest's dependencies of a path, loaded as the install source loads them (same cache keys), without
        // opening the path's own abdata file: a zipmod copy of an abdata path may reference them.
        void PreloadAbdataDependencies(string rel)
        {
            foreach (var h in sources.OfType<Hs2InstallSource>())
            {
                if (!h.HasBundle(rel)) continue;
                if (!abdataManifests.TryGetValue(h, out var m))
                {
                    var mb = ((IBundleCache)this).GetOrLoad(h.Id + ":abdata", () => AssetBundle.LoadFromFile(Path.Combine(h.Abdata, "abdata")));
                    m = mb ? mb.LoadAsset<AssetBundleManifest>("AssetBundleManifest") : null;
                    abdataManifests[h] = m;
                }
                if (m)
                    foreach (var d in m.GetAllDependencies(rel))
                    {
                        string dep = d;
                        ((IBundleCache)this).GetOrLoad(h.Id + ":" + dep, () => AssetBundle.LoadFromFile(Path.Combine(h.Abdata, dep)));
                    }
                return;
            }
        }
        readonly Dictionary<Hs2InstallSource, AssetBundleManifest> abdataManifests = new Dictionary<Hs2InstallSource, AssetBundleManifest>();

        /// <summary>
        /// The read model of a named load (the bridge's content.redirect, tests, comparisons): the path's copies and whether each
        /// holds <paramref name="asset"/>, the source the redirect serves, and the source <paramref name="row"/> (null: a fixed
        /// abdata path) read before it. Main thread.
        /// </summary>
        public AssetRedirect.Explanation ExplainLoad(ListEntry row, string bundlePath, string asset)
        {
            if (string.IsNullOrEmpty(bundlePath)) return null;
            bundlePath = bundlePath.Replace('\\', '/');
            var today = row != null ? TodayOf(row, bundlePath) : HasRealBundle(bundlePath) ? AbdataServed : default;
            string abFile = AbdataFileOf(bundlePath);
            var x = (redirect ?? AssetRedirect.Build(Array.Empty<IRedirectZip>(), null)).Explain(bundlePath, asset, abFile, today, AbdataNames);
            if (!SideloaderAssetRedirect || !x.Redirectable)
            {
                x.ServedKind = x.TodayKind; x.ServedGuid = x.TodayGuid; x.ServedFile = x.TodayFile;
            }
            x.RedirectOn = SideloaderAssetRedirect;
            x.ZipmodSetOn = SideloaderZipmodSet;
            return x;
        }

        /// <summary>The abdata file of a path (the first source that has it), or null.</summary>
        public string AbdataFileOf(string bundlePath)
        {
            if (string.IsNullOrEmpty(bundlePath)) return null;
            bundlePath = bundlePath.Replace('\\', '/');
            foreach (var h in sources.OfType<Hs2InstallSource>())
                if (h.HasBundle(bundlePath)) return Path.Combine(h.Abdata, bundlePath);
            return null;
        }

        readonly Dictionary<string, BundleNames> abdataNames = new Dictionary<string, BundleNames>(StringComparer.OrdinalIgnoreCase);
        BundleNames AbdataNames(string file)
        {
            if (file == null) return null;
            if (!abdataNames.TryGetValue(file, out var n))
            {
                long len = 0;
                try { len = new FileInfo(file).Length; } catch (Exception) { }
                abdataNames[file] = n = BundleNames.ReadFile(file, 0, len);
            }
            return n;
        }

        /// <summary>The zipmods declaring `&lt;hs2-scene-dependency manifest="X"&gt;`, in GUID order (empty if none).</summary>
        public IReadOnlyList<ZipmodInfo> ZipmodsDeclaringManifest(string manifest)
        {
            if (declaredManifests == null)
            {
                declaredManifests = new Dictionary<string, List<ZipmodInfo>>(StringComparer.OrdinalIgnoreCase);
                foreach (var z in ZipmodsByGuid.Values.OrderBy(z => z.Guid, StudioCsv.LoadOrder))
                    foreach (var d in z.SceneDependencies)
                    {
                        if (!declaredManifests.TryGetValue(d.Manifest, out var l)) declaredManifests[d.Manifest] = l = new List<ZipmodInfo>();
                        if (!l.Contains(z)) l.Add(z);
                    }
            }
            return manifest != null && declaredManifests.TryGetValue(manifest, out var r) ? r : (IReadOnlyList<ZipmodInfo>)Array.Empty<ZipmodInfo>();
        }

        DependencyManifest GetDependencyManifest(string manifest, ZipmodInfo owner)
        {
            if (depManifests.TryGetValue(manifest, out var dm)) return dm;
            var decl = ZipmodsDeclaringManifest(manifest);
            if (decl.Count > 0)
            {
                var z = owner != null && decl.Contains(owner) ? owner : decl[0];
                var path = manifest + ".unity3d";
                var mb = z.HasBundle(path) ? z.OpenBundle(path, this) : FindZipmodShipping(path)?.OpenBundle(path, this);
                var m = mb ? mb.LoadAsset<AssetBundleManifest>("AssetBundleManifest") : null;
                if (m)
                {
                    dm = new DependencyManifest { Manifest = m, Zipmod = z, Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) };
                    foreach (var n in m.GetAllAssetBundles()) dm.Names[n] = n;
                }
                else Log.Add($"dependency manifest {manifest} declared by {z.Guid} could not be read");
            }
            depManifests[manifest] = dm;
            return dm;
        }

        void PreloadDependencies(ZipmodInfo owner, string bundlePath, string manifest)
        {
            if (string.IsNullOrEmpty(manifest) || manifest.Equals("abdata", StringComparison.OrdinalIgnoreCase)) return;
            var dm = GetDependencyManifest(manifest, owner);
            if (dm == null || !dm.Manifest || !dm.Names.TryGetValue(bundlePath, out var name)) return;
            foreach (var d in dm.Manifest.GetAllDependencies(name)) OpenAnywhere(dm.Zipmod, d);
        }

        // ---------- asynchronous asset loads, thumbnail-only bundles (Maker lists, package L) ----------
        // A bundle first opened by a thumbnail request is "thumbnail-only" until a synchronous load (a build, a list read) asks
        // for it; PartThumbnails unloads such bundles (Unload(true) is safe: nothing but its own thumbnails came from them) when
        // their last thumbnail leaves its LRU. Bundles a build uses belong to its owner; released, they wait in the LRU of
        // unowned bundles (ContentCatalog.Ownership.cs).

        readonly HashSet<AssetBundle> thumbnailOnly = new HashSet<AssetBundle>();
        int thumbnailOpens;

        /// <summary>True when <paramref name="bundle"/> was opened by a thumbnail request and no synchronous load used it since.</summary>
        public bool IsThumbnailOnly(AssetBundle bundle) => bundle && thumbnailOnly.Contains(bundle);

        /// <summary>Number of open thumbnail-only bundles.</summary>
        public int ThumbnailOnlyCount => thumbnailOnly.Count;

        /// <summary>Number of open bundles (all sources; diagnostics).</summary>
        public int OpenBundleCount => bundles.Count;

        // Bundles a synchronous load has used (their objects may be in use: a clashing copy is unloaded only when it is not here).
        readonly HashSet<AssetBundle> handedOut = new HashSet<AssetBundle>();

        // Called with every bundle a load hands out (key null: looked up): a synchronous use clears the thumbnail-only mark, marks
        // it handed out and records it under the current owner (a pin for the pinned paths); a thumbnail open records the
        // thumbnails' owner.
        void NoteBundleUse(string key, AssetBundle b, bool opened)
        {
            if (!b) return;
            key ??= KeyOf(b);
            if (thumbnailOpens > 0)
            {
                if (opened)
                {
                    thumbnailOnly.Add(b);
                    if (key != null) { Ledger.Opened(key); Ledger.Use(key, Ledger.Thumbs); }
                }
            }
            else
            {
                if (thumbnailOnly.Count > 0 && thumbnailOnly.Remove(b) && LogBundles) BundleLog($"thumbnail-only cleared (synchronous use) {key ?? KeyOf(b)}");
                handedOut.Add(b);
                if (key != null)
                {
                    if (opened) Ledger.Opened(key);
                    Ledger.Use(key, Ledger.OwnerForUse(PinOf(key)));
                    if (Ledger.IsOwnedBy(key, Ledger.Thumbs)) Ledger.Disown(key, Ledger.Thumbs);
                }
            }
        }

        /// <summary>
        /// Starts an asynchronous load of entry[<paramref name="assetKey"/>] from the bundle entry[<paramref name="bundleKey"/>] (the
        /// bundle is opened synchronously as <see cref="LoadBundle(ListEntry, string)"/> does; only the asset load is asynchronous).
        /// <paramref name="thumbnail"/>: bundles this call opens are marked thumbnail-only. Returns null when the entry names no
        /// asset or the bundle is missing; <paramref name="bundle"/> is the bundle the request reads.
        /// </summary>
        public AssetBundleRequest LoadAssetAsync<T>(ListEntry e, string bundleKey, string assetKey, bool thumbnail, out AssetBundle bundle) where T : UnityEngine.Object
        {
            bundle = OpenForAsset(e, bundleKey, assetKey, thumbnail);
            if (!bundle) return null;
            if (IsAssetRefused(bundle, e[assetKey])) return null;   // the pre-load check refuses it (no asynchronous load either)
            var r = bundle.LoadAssetAsync<T>(e[assetKey]);
            TrackRequest(bundle, e[assetKey], r);
            return r;
        }

        /// <summary>
        /// The bundle a load of entry[<paramref name="assetKey"/>] from entry[<paramref name="bundleKey"/>] reads, opened as
        /// <see cref="LoadAssetAsync{T}"/> opens it (the copy the redirect serves the asset from, else the entry's; bundles this
        /// call opens are thumbnail-only when <paramref name="thumbnail"/>); null when the entry names no asset or the bundle
        /// is missing. The thumbnail loader checks the texture's header (<see cref="BundleFileOf"/>) before it starts the load.
        /// </summary>
        public AssetBundle OpenForAsset(ListEntry e, string bundleKey, string assetKey, bool thumbnail)
        {
            string path = e?[bundleKey], asset = e?[assetKey];
            if (string.IsNullOrEmpty(asset) || asset == "0" || string.IsNullOrEmpty(path) || path == "0") return null;
            if (thumbnail) thumbnailOpens++;
            try
            {
                if (Redirects(path, asset, out var served)) { NoteServed(served, path, asset, TodayOf(e, path)); return OpenServed(served, path); }
                return LoadBundle(e, path);
            }
            finally { if (thumbnail) thumbnailOpens--; }
        }

        /// <summary>
        /// Where the bytes of a bundle this catalog opened are, for reading its headers without Unity (on a worker): the
        /// repaired copy of the texture guard's index, a zipmod's entry (stored: the zip file at the entry's offset; deflated:
        /// read whole) or the abdata file. Null for a bundle the catalog did not open (or opened from memory only).
        /// Main thread (the zip's index is read here).
        /// </summary>
        public BundleFileRef BundleFileOf(AssetBundle bundle) => BundleFileOfKey(bundle ? KeyOf(bundle) : null);

        /// <summary>
        /// As <see cref="BundleFileOf"/> for a cache key ("&lt;zipmod file&gt;!&lt;path&gt;" or "&lt;source id&gt;:&lt;path&gt;"),
        /// whether or not the bundle is open: the bytes an open of that key reads. Null when the key names no file.
        /// </summary>
        public BundleFileRef BundleFileOfKey(string key)
        {
            if (key == null) return null;
            string repairedCopy = HS2Next.Chara.TextureGuard.RepairedPath(key);
            if (repairedCopy != null) return FileRef(key, repairedCopy, 0, -1);
            int bang = key.IndexOf('!');
            if (bang >= 0)
            {
                string zip = key.Substring(0, bang), inner = key.Substring(bang + 1);
                var z = loadedInOrder.FirstOrDefault(x => string.Equals(x.Path, zip, StringComparison.OrdinalIgnoreCase))
                        ?? ZipmodsByGuid.Values.FirstOrDefault(x => string.Equals(x.Path, zip, StringComparison.OrdinalIgnoreCase));
                if (z == null || !z.Index.Entries.TryGetValue("abdata/" + inner, out var ze)) return null;
                long offset = ze.DataOffset;   // read here: the entry computes it on first use
                var r = FileRef(key, z.Path, offset, ze.Size);
                if (ze.Method != 0) { var index = z.Index; r.ReadAll = () => index.ReadAll(ze); }
                return r;
            }
            int colon = key.IndexOf(':');
            if (colon <= 0) return null;
            string id = key.Substring(0, colon), rel = key.Substring(colon + 1);
            foreach (var h in sources.OfType<Hs2InstallSource>())
                if (string.Equals(h.Id, id, StringComparison.OrdinalIgnoreCase)) return FileRef(key, Path.Combine(h.Abdata, rel), 0, -1);
            return null;
        }

        static BundleFileRef FileRef(string key, string file, long offset, long length)
        {
            var fi = new FileInfo(file);
            if (!fi.Exists) return null;
            return new BundleFileRef
            {
                Key = key, File = file, Offset = offset, Length = length >= 0 ? length : fi.Length,
                Stamp = fi.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + fi.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
        }

        /// <summary>An asynchronous bundle open (<see cref="OpenBundleAsync"/>): done when <see cref="IsDone"/>; <see cref="Bundle"/> null = not found.</summary>
        public sealed class BundleOpen
        {
            public bool IsDone { get; internal set; }
            public AssetBundle Bundle { get; internal set; }
            /// <summary>No source ships the bundle, but its folder holds Sideloader loose PNGs (GB10): Sideloader's empty bundle —
            /// the row's textures load (as loose PNGs, <see cref="LooseTextures"/>), <see cref="Bundle"/> stays null.</summary>
            public bool Loose { get; internal set; }
            internal string Key;
            internal System.Threading.Tasks.Task<byte[]> Read;
            internal AssetBundleCreateRequest Create;
            internal int ClashesAtStart;
            internal long MemoryBytes;   // > 0: created from a byte array of this length
            internal bool Refused;       // the open point refused the bytes
            internal BundleOwner Owner;  // the owner when the open started (the thumbnails' for thumbnail opens)
        }

        readonly Dictionary<string, BundleOpen> pendingOpens = new Dictionary<string, BundleOpen>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Opens the bundle of a list entry for a thumbnail without blocking where it matters: a zipmod's bundle opens with
        /// LoadFromFileAsync at its offset (stored entries; LZMA bundles otherwise decompress synchronously: 3 s measured, U7) or
        /// is read on a worker and created with LoadFromMemoryAsync (deflated entries); abdata files and open bundles open at
        /// once as <see cref="LoadBundle(ListEntry, string)"/> does (vanilla bundles are LZ4: lazy, fast). Bundles opened here are thumbnail-only.
        /// Call <see cref="PumpOpens"/> every frame while opens are pending; a synchronous load of a pending bundle completes it.
        /// </summary>
        public BundleOpen OpenBundleAsync(ListEntry e, string bundlePath)
        {
            if (string.IsNullOrEmpty(bundlePath) || bundlePath == "0") return new BundleOpen { IsDone = true };
            bundlePath = bundlePath.Replace('\\', '/');
            // Phase 4.2 package L (GB10): a bundle nobody ships whose folder holds Sideloader loose PNGs "exists" empty
            if (!BundleExists(bundlePath) && Loose.HasFolder(bundlePath)) return new BundleOpen { IsDone = true, Loose = true };
            var z = e?.Zipmod;
            if (z != null && !z.HasBundle(bundlePath)) z = FindZipmodShipping(bundlePath);
            return OpenAsync(z, bundlePath, () => LoadBundle(e, bundlePath));
        }

        /// <summary>
        /// As <see cref="OpenBundleAsync(ListEntry, string)"/> for a load of <paramref name="asset"/>: on a path several sources
        /// ship, the copy the redirect serves the asset from (a thumbnail's ThumbTex, a part's MainData), else the row's own source.
        /// </summary>
        public BundleOpen OpenBundleAsync(ListEntry e, string bundlePath, string asset)
        {
            if (!Redirects(bundlePath, asset, out var served)) return OpenBundleAsync(e, bundlePath);
            bundlePath = bundlePath.Replace('\\', '/');
            NoteServed(served, bundlePath, asset, TodayOf(e, bundlePath));
            var z = served.Zip as ZipmodInfo;
            // another copy of the path already open: open synchronously, so a clash of the two takes the catalog's rule
            if (z != null && !OtherCopyOpen(z.Path + "!" + bundlePath, bundlePath) && !HasRealBundle(bundlePath))
                return OpenAsync(z, bundlePath, () => OpenServed(served, bundlePath));
            return SyncOpen(() => OpenServed(served, bundlePath));
        }

        // A zipmod's copy opened asynchronously (stored entries at their offset, deflated ones read on a worker), repaired copies
        // from their file; other cases (no zipmod, an open bundle) through <paramref name="sync"/> at once (SyncOpen: a
        // thumbnail-only open, or the prefetch's).
        BundleOpen OpenAsync(ZipmodInfo z, string bundlePath, Func<AssetBundle> sync)
        {
            if (z != null)
            {
                string key = z.Path + "!" + bundlePath;
                if (pendingOpens.TryGetValue(key, out var p)) return p;
                if (!bundles.ContainsKey(key))
                {
                    // Phase 4.1 (F14): a bundle of the texture guard's repair index opens from
                    // its repaired copy, asynchronously like the others (IBundleCache.GetOrLoad takes the same copy for
                    // synchronous loads)
                    var repairedCopy = HS2Next.Chara.TextureGuard.RepairedPath(key);
                    Func<AssetBundle> asIs = null;
                    Func<BundleOpen> start = null;
                    if (repairedCopy != null)
                    {
                        asIs = () => AssetBundle.LoadFromFile(repairedCopy);
                        start = () => new BundleOpen { Key = key, Create = AssetBundle.LoadFromFileAsync(repairedCopy) };
                    }
                    else if (z.Index.Entries.TryGetValue("abdata/" + bundlePath, out var ze))
                    {
                        var index = z.Index;
                        string file = z.Path;
                        if (ze.Method != 0)
                        {
                            asIs = () => AssetBundle.LoadFromMemory(index.ReadAll(ze));
                            start = () => new BundleOpen { Key = key, Read = System.Threading.Tasks.Task.Run(() => index.ReadAll(ze)) };
                        }
                        else
                        {
                            asIs = () => AssetBundle.LoadFromFile(file, 0, (ulong)ze.DataOffset);
                            start = () => new BundleOpen { Key = key, Create = AssetBundle.LoadFromFileAsync(file, 0, (ulong)ze.DataOffset) };
                        }
                    }
                    if (start != null)
                    {
                        var open = asIs;
                        OnBundleOpening(key, ref open);
                        if (ReferenceEquals(open, asIs))
                        {
                            p = start();
                            p.Owner = asyncOwner ?? Ledger.Thumbs;
                            p.ClashesAtStart = Volatile.Read(ref cabClashMessages);
                            pendingOpens[key] = p;
                            return p;
                        }
                        // the open point replaced or refused this open: the synchronous path below takes it (and asks again)
                    }
                }
            }
            return SyncOpen(sync);
        }

        // True when a copy of the same bundle path other than <paramref name="key"/> is open.
        bool OtherCopyOpen(string key, string bundlePath) => OpenCopiesOf(bundlePath).Any(kv => !string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase));

        // The open bundles (cache key, bundle) whose key names the bundle path: "<zip>!<path>" or "<source id>:<path>".
        IEnumerable<KeyValuePair<string, AssetBundle>> OpenCopiesOf(string bundlePath)
        {
            foreach (var kv in bundles)
                if (kv.Value && string.Equals(PathOfKey(kv.Key), bundlePath, StringComparison.OrdinalIgnoreCase)) yield return kv;
        }

        static string PathOfKey(string key)
        {
            int bang = key.IndexOf('!');
            if (bang >= 0) return key.Substring(bang + 1);
            int colon = key.IndexOf(':');
            return colon >= 0 ? key.Substring(colon + 1) : key;
        }

        /// <summary>Asynchronous opens in flight (<see cref="OpenBundleAsync"/>).</summary>
        public int PendingOpenCount => pendingOpens.Count;

        /// <summary>
        /// Completes every asynchronous open now (blocking; main thread) — before a shutdown, so no AssetBundleCreateRequest is
        /// still in flight when Unity tears down (Phase 4.2 package L: play-mode exit / app quit). Returns the number completed.
        /// </summary>
        public int CompletePendingOpens()
        {
            int n = pendingOpens.Count;
            foreach (var k in pendingOpens.Keys.ToList()) FinishPending(k);
            return n;
        }

        /// <summary>Advances the pending asynchronous opens (main thread, once per frame).</summary>
        public void PumpOpens()
        {
            if (pendingOpens.Count == 0) return;
            List<string> done = null;
            foreach (var p in pendingOpens.Values)
            {
                if (p.Create == null)
                {
                    if (!p.Read.IsCompleted) continue;
                    var bytes = p.Read.IsFaulted || p.Read.IsCanceled ? null : p.Read.Result;
                    if (bytes != null) { OnBundleBytes(p.Key, ref bytes); p.Refused = bytes == null; }
                    if (bytes == null) { Complete(p, null); (done ??= new List<string>()).Add(p.Key); continue; }
                    p.MemoryBytes = bytes.LongLength;
                    p.Create = AssetBundle.LoadFromMemoryAsync(bytes);
                }
                if (!p.Create.isDone) continue;
                Complete(p, p.Create.assetBundle);
                (done ??= new List<string>()).Add(p.Key);
            }
            if (done != null) foreach (var k in done) pendingOpens.Remove(k);
        }

        void Complete(BundleOpen p, AssetBundle b)
        {
            p.Bundle = b;
            p.IsDone = true;
            p.Read = null;
            if (b && !bundles.ContainsKey(p.Key)) { RegisterAsync(p, b); if (p.MemoryBytes > 0) NoteMemoryBacked(p.Key, p.MemoryBytes); }
            if (!b && !p.Refused && Volatile.Read(ref cabClashMessages) != p.ClashesAtStart)
            {
                b = RewriteOpen(p.Key);
                if (b) { p.Bundle = b; if (!bundles.ContainsKey(p.Key)) RegisterAsync(p, b); }
                else NoteClash(p.Key, null, "asynchronous open refused");
            }
            if (!b) Log.Add($"bundle not readable: {p.Key}");
        }

        // A completed asynchronous open: cached and recorded under the owner captured at its start (thumbnail-only when that
        // is the thumbnails' owner; a pinned path takes its pin; a released owner's open goes to "unscoped").
        void RegisterAsync(BundleOpen p, AssetBundle b)
        {
            Register(p.Key, b);
            Ledger.Opened(p.Key);
            var owner = p.Owner ?? Ledger.Thumbs;
            if (LogBundles) BundleLog($"open done (asynchronous) {p.Key} owner {owner.Label}");
            if (owner.Kind == BundleOwnerKind.Thumbs) { thumbnailOnly.Add(b); Ledger.Use(p.Key, owner); return; }
            string pin = PinOf(p.Key);
            Ledger.Use(p.Key, pin != null ? Ledger.Pin(pin) : owner);
        }

        // A synchronous load of a bundle that an asynchronous open has in flight: finish that open now (blocking).
        AssetBundle FinishPending(string key)
        {
            if (!pendingOpens.TryGetValue(key, out var p)) return null;
            pendingOpens.Remove(key);
            AssetBundle b;
            if (p.Create != null) b = p.Create.assetBundle;
            else
            {
                byte[] bytes = null;
                try { bytes = p.Read.Result; } catch (Exception) { }
                if (bytes != null) { OnBundleBytes(p.Key, ref bytes); p.Refused = bytes == null; }
                if (bytes != null) p.MemoryBytes = bytes.LongLength;
                b = bytes != null ? AssetBundle.LoadFromMemory(bytes) : null;
            }
            Complete(p, b);
            return p.Bundle;   // a refused open may have been rewritten
        }

        /// <summary>
        /// Unloads <paramref name="bundle"/> when it is still thumbnail-only (see <see cref="IsThumbnailOnly"/>); returns true when it
        /// did. The caller guarantees that none of its assets is in use.
        /// </summary>
        public bool UnloadThumbnailBundle(AssetBundle bundle)
        {
            if (!IsThumbnailOnly(bundle)) return false;
            thumbnailOnly.Remove(bundle);
            UnloadBundle(bundle);
            return true;
        }

        /// <summary>
        /// The file a list entry's bundle is read from and its stamp, "path|size|mtime ticks" (the zipmod that ships it, or the
        /// abdata file of the first source that has it), for caches keyed by source content; null when nobody ships it.
        /// </summary>
        public string SourceStampOf(ListEntry e, string bundlePath)
        {
            if (string.IsNullOrEmpty(bundlePath) || bundlePath == "0") return null;
            bundlePath = bundlePath.Replace('\\', '/');
            string file = null;
            var z = e?.Zipmod;
            if (z != null && z.HasBundle(bundlePath)) file = z.Path;
            else if (z != null && FindZipmodShipping(bundlePath) is ZipmodInfo other) file = other.Path;
            else file = AbdataFileOf(bundlePath);
            return StampOfFile(file);
        }

        /// <summary>
        /// As <see cref="SourceStampOf(ListEntry, string)"/> for a load of <paramref name="asset"/>: on a path several sources
        /// ship, the file of the copy the redirect serves the asset from (a thumbnail's disk cache key follows the serving copy).
        /// </summary>
        public string SourceStampOf(ListEntry e, string bundlePath, string asset)
        {
            if (!Redirects(bundlePath, asset, out var served)) return SourceStampOf(e, bundlePath);
            return StampOfFile(served.Zip != null ? served.Zip.Path : served.Abdata ? AbdataFileOf(bundlePath) : null);
        }

        string StampOfFile(string file)
        {
            if (file == null) return null;
            if (!stamps.TryGetValue(file, out var st))
            {
                var fi = new FileInfo(file);
                stamps[file] = st = fi.Exists ? file + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks : null;
            }
            return st;
        }
        readonly Dictionary<string, string> stamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Unloads one bundle (and everything loaded from it) and forgets it in the caches.</summary>
        public void UnloadBundle(AssetBundle bundle, [System.Runtime.CompilerServices.CallerMemberName] string caller = null)
        {
            if (!bundle) return;
            if (LogBundles)
            {
                var ks = bundles.Where(kv => kv.Value == bundle).Select(kv => kv.Key).ToList();
                BundleLog($"unload(true) {string.Join(" | ", ks)} '{bundle.name}' by {caller}; thumbnail-only {thumbnailOnly.Contains(bundle)}, handed out {handedOut.Contains(bundle)}, requests in flight {ks.Sum(RequestsInFlight)}, prefetch jobs {prefetches.Count(j => j.Open?.Bundle == bundle)}");
            }
            foreach (var k in bundles.Where(kv => kv.Value == bundle).Select(kv => kv.Key).ToList()) { bundles.Remove(k); memoryBacked.Remove(k); Ledger.Forget(k); }
            keyOfBundle.Remove(bundle);
            foreach (var k in byPath.Where(kv => kv.Value == bundle).Select(kv => kv.Key).ToList()) byPath.Remove(k);
            thumbnailOnly.Remove(bundle);
            handedOut.Remove(bundle);
            bundle.Unload(true);
        }

        /// <summary>The first zipmod that ships abdata/&lt;path&gt; (any case), or null: in GUID load order with
        /// <see cref="SideloaderZipmodSet"/>, else in <see cref="ZipmodsByGuid"/> order.</summary>
        public ZipmodInfo FindZipmodShipping(string bundlePath)
        {
            if (shippedBy == null)
            {
                shippedBy = new Dictionary<string, ZipmodInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (var z in SideloaderZipmodSet ? (IEnumerable<ZipmodInfo>)loadedInOrder : ZipmodsByGuid.Values)
                    foreach (var p in z.BundlePaths)
                        if (!shippedBy.ContainsKey(p)) shippedBy[p] = z;
            }
            return shippedBy.TryGetValue(bundlePath.Replace('\\', '/'), out var hit) ? hit : null;
        }

        /// <summary>Loads entry[assetKey] from the bundle entry[bundleKey] (see <see cref="LoadBundle"/>).</summary>
        public T LoadAsset<T>(ListEntry e, string bundleKey, string assetKey) where T : UnityEngine.Object =>
            LoadAssetAt<T>(e, e?[bundleKey], e?[assetKey]);

        /// <summary>Loads <paramref name="asset"/> from <paramref name="bundlePath"/> as a bundle of the entry (see
        /// <see cref="LoadBundle"/>), through the texture guard; for paths that are not an entry column (head override).
        /// A path several sources ship follows the per-asset redirect.</summary>
        public T LoadAssetAt<T>(ListEntry e, string bundlePath, string asset) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(asset) || asset == "0") return null;
            if (LooseLoad(bundlePath, asset, out T lt)) return lt;   // Sideloader's loose PNG wins (GB10)
            if (Redirects(bundlePath, asset, out var served)) return LoadServed<T>(served, bundlePath, asset, TodayOf(e, bundlePath));
            var b = LoadBundle(e, bundlePath);
            return b ? GuardLoad<T>(b, asset, Label(e?.Zipmod, bundlePath)) : null;
        }

        /// <summary>Loads an asset from an abdata bundle (a fixed path: chara/oo_base, chara/mm_base, list/customshape, ...).
        /// A path a zipmod also ships follows the per-asset redirect.</summary>
        public T LoadAsset<T>(string abdataBundle, string asset) where T : UnityEngine.Object
        {
            if (LooseLoad(abdataBundle, asset, out T lt)) return lt;   // Sideloader's loose PNG wins (GB10)
            if (Redirects(abdataBundle, asset, out var served))
                return LoadServed<T>(served, abdataBundle, asset, HasRealBundle(abdataBundle) ? AbdataServed : default);
            var b = LoadAbdataBundle(abdataBundle);
            return b ? GuardLoad<T>(b, asset, Label(null, abdataBundle)) : null;
        }

        /// <summary>
        /// Starts an asynchronous load of <paramref name="asset"/> from the bundle <see cref="LoadAsset{T}(string, string)"/> would
        /// read for the abdata path <paramref name="abdataBundle"/> (the copy the per-asset redirect serves, else the abdata file;
        /// the bundle is opened synchronously, only the asset load is asynchronous). Not for textures (a loose PNG would win in the
        /// synchronous path). Returns null when the bundle is missing or the pre-load check refuses the asset.
        /// </summary>
        public AssetBundleRequest LoadAssetAsync<T>(string abdataBundle, string asset) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(abdataBundle) || string.IsNullOrEmpty(asset)) return null;
            AssetBundle b;
            if (Redirects(abdataBundle, asset, out var served))
            {
                NoteServed(served, abdataBundle, asset, HasRealBundle(abdataBundle) ? AbdataServed : default);
                b = OpenServed(served, abdataBundle);
            }
            else b = LoadAbdataBundle(abdataBundle);
            if (!b || IsAssetRefused(b, asset)) return null;
            var r = b.LoadAssetAsync<T>(asset);
            TrackRequest(b, asset, r);
            return r;
        }

        // ---------- Sideloader's loose PNG textures (Phase 4.2 package L, gap GB10; Runtime/Content/LooseTextures.cs) ----------

        LooseTextures loose;

        /// <summary>The loose PNG textures of the zipmods in <see cref="ZipmodsByGuid"/> (index built on first use).</summary>
        public LooseTextures Loose => loose ??= new LooseTextures(this);

        // A request answered by a loose PNG (every texture load above consults it first, as Sideloader's asset hook does): true
        // when a loose PNG matches; the texture (pinned, shared) for Texture / Texture2D requests, null for another Texture
        // subclass (HS2's cast of Sideloader's Texture2D fails). Other types never match.
        bool LooseLoad<T>(string bundlePath, string asset, out T result) where T : UnityEngine.Object
        {
            result = null;
            if (!LooseTextures.Consults<T>() || ZipmodsByGuid.Count == 0 || !Loose.TryFind(bundlePath, asset, out var hit)) return false;
            if (typeof(T).IsAssignableFrom(typeof(Texture2D))) result = Loose.Load(hit) as T;
            return true;
        }

        // Texture guard (Runtime/Chara/TextureGuard.cs): a load's source label and a loaded bundle's cache key.
        static string Label(ZipmodInfo z, string bundlePath) => (z?.Guid ?? "abdata") + ":" + bundlePath?.Replace('\\', '/');

        // ---------- open points ----------
        // Every bundle the catalog opens passes one of the two hooks below before Unity reads it. They are implemented in
        // another part of this class; without an implementation the calls compile away.

        /// <summary>
        /// Before the catalog opens the bundle it caches under <paramref name="key"/> (a synchronous open; an asynchronous open
        /// before its request starts): <paramref name="open"/> is how it would be opened (the repaired copy's file, the abdata
        /// file, the zip entry in place or inflated). The hook may replace it (an open of other bytes; call
        /// <see cref="NoteMemoryBacked"/> when that open reads a byte array) or set it to null to refuse the bundle: the request
        /// then gets null and nothing is cached (the hook records its own finding). Replacing the open of an asynchronous open
        /// makes that open synchronous, and its synchronous path asks the hook again, so one open may pass here twice.
        /// </summary>
        partial void OnBundleOpening(string key, ref Func<AssetBundle> open);

        /// <summary>
        /// With the bytes of a bundle the catalog is about to open from memory (a deflated zip entry read on a worker, a
        /// renamed copy after a refused open): the hook may replace them (other bytes of the same bundle) or set them to null to
        /// refuse the bundle (the open fails, nothing is cached).
        /// </summary>
        partial void OnBundleBytes(string key, ref byte[] bytes);

        // Bundles opened from a byte array (a deflated zip entry, a renamed or patched copy): cache key -> the array's length,
        // the memory the open bundle itself holds. Forgotten when the bundle unloads.
        readonly Dictionary<string, long> memoryBacked = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Records that the bundle cached under <paramref name="key"/> was opened from a byte array of
        /// <paramref name="length"/> bytes.</summary>
        void NoteMemoryBacked(string key, long length) { if (key != null && length > 0) memoryBacked[key] = length; }

        // Never caches a null bundle (a later request tries again). A bundle Unity refuses because an open bundle holds a file of
        // the same name: when the other open copy of the same path is thumbnail-only or nothing was handed out from it
        // synchronously, it is unloaded and the open retried; else the bundle is loaded from memory with its own file names
        // renamed (RewriteOpen); else the request gets that open copy (the finding content.cabClash).
        AssetBundle IBundleCache.GetOrLoad(string key, Func<AssetBundle> load)
        {
            if (pendingOpens.Count > 0 && pendingOpens.ContainsKey(key)) FinishPending(key);
            if (bundles.TryGetValue(key, out var b) && b) { NoteBundleUse(key, b, false); return b; }
            HookCabClash();
            // A bundle with textures Unity 6 cannot upload opens from its repaired copy (Runtime/Chara/TextureGuard.cs).
            var repaired = HS2Next.Chara.TextureGuard.RepairedPath(key);
            Func<AssetBundle> open = repaired != null ? () => AssetBundle.LoadFromFile(repaired) : load;
            OnBundleOpening(key, ref open);
            if (open == null) return null;
            int before = Volatile.Read(ref cabClashMessages);
            b = open();
            bool own = true;
            if (!b && Volatile.Read(ref cabClashMessages) != before) b = OnCabClash(key, open, out own);
            if (!b) return null;
            if (own) { Register(key, b); NoteBundleUse(key, b, true); }
            else NoteBundleUse(null, b, false);   // the other copy serves; this key stays unopened
            return b;
        }

        /// <summary>One refused open (finding content.cabClash): the key asked for, the open bundle's key it met (null: not a copy
        /// of the same path, e.g. two different paths whose files share a name), and what the catalog did.</summary>
        public sealed class CabClash
        {
            public string Key, OtherKey, Resolution, Message;
            public override string ToString() => $"content.cabClash: {Key} vs {OtherKey ?? "(another path)"}: {Resolution}";
        }

        /// <summary>The content.cabClash findings of this catalog, oldest first.</summary>
        public readonly List<CabClash> CabClashes = new List<CabClash>();

        static int cabClashMessages;
        static string lastCabClashMessage;
        static bool cabHooked;
        const string CabClashText = "another AssetBundle with the same files is already loaded";

        // Unity reports a refused open only in its log: count those lines (any thread).
        static void HookCabClash()
        {
            if (cabHooked) return;
            cabHooked = true;
            Application.logMessageReceivedThreaded += (msg, stack, type) =>
            {
                if (msg == null || msg.IndexOf(CabClashText, StringComparison.Ordinal) < 0) return;
                lastCabClashMessage = msg;
                Interlocked.Increment(ref cabClashMessages);
            };
        }

        /// <summary>Unity's refusals of a bundle open seen in this process (all catalogs).</summary>
        public static int CabClashMessages => Volatile.Read(ref cabClashMessages);

        void NoteClash(string key, string other, string resolution)
        {
            var c = new CabClash { Key = key, OtherKey = other, Resolution = resolution, Message = lastCabClashMessage };
            CabClashes.Add(c);
            Log.Add(c.ToString());
            Debug.LogWarning("[content] " + c);
        }

        // Rule (a): another open copy of the same path that is thumbnail-only or was never handed out synchronously is unloaded
        // and the open retried; rule (b): otherwise (also when the open bundle is another path's) the bundle is loaded from
        // memory with its file names renamed; rule (c): when that fails, the request is served from the open copy of the same
        // path (not cached under this key).
        AssetBundle OnCabClash(string key, Func<AssetBundle> open, out bool cache)
        {
            cache = false;
            string path = PathOfKey(key);
            var others = OpenCopiesOf(path).Where(kv => !string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)).ToList();
            var unused = others.FirstOrDefault(kv => IsThumbnailOnly(kv.Value) || !handedOut.Contains(kv.Value));
            if (unused.Value)
            {
                UnloadBundle(unused.Value);
                int before = Volatile.Read(ref cabClashMessages);
                var b = open();
                if (b) { NoteClash(key, unused.Key, "the unused open copy unloaded, then opened"); cache = true; return b; }
                if (Volatile.Read(ref cabClashMessages) == before) { NoteClash(key, unused.Key, "did not open after unloading the unused copy"); return null; }
                others.RemoveAll(kv => kv.Key == unused.Key);
            }
            var rewritten = RewriteOpen(key);
            if (rewritten) { cache = true; return rewritten; }
            if (others.Count == 0) { NoteClash(key, null, "not opened (the open bundle is not a copy of this path; the rewrite failed)"); return null; }
            var used = others[0];
            NoteClash(key, used.Key, "served from the open copy");
            return used.Value;
        }

        /// <summary>One bundle loaded from memory with its own file names renamed after Unity refused it (rule (b)).</summary>
        public sealed class CabRewriteNote
        {
            public string Key, Error;
            /// <summary>The renamed files ("CAB-old -> CAB-new"), the occurrences replaced in the data, the bytes loaded.</summary>
            public List<string> Names = new List<string>();
            public int Replaced;
            public long Bytes;
            public double Ms;
            public bool Ok => Error == null;
            public override string ToString() => Ok
                ? $"content.cabRewrite: {Key}: {Names.Count} file names renamed ({Replaced} references in the data), {Bytes / 1048576.0:F1} MiB from memory, {Ms:F1} ms"
                : $"content.cabRewrite failed: {Key}: {Error} ({Ms:F1} ms)";
        }

        /// <summary>The rewrites of this catalog (rule (b)), oldest first.</summary>
        public readonly List<CabRewriteNote> CabRewrites = new List<CabRewriteNote>();
        static int cabRewriteCount;

        // Rule (b): the source bytes of the refused bundle, uncompressed with every "CAB-..." file name renamed to a name of the
        // same length that no other bundle uses (a process-wide counter hashed), loaded from memory. Null (and a failed note)
        // when the source cannot be read or rewritten (LZMA blocks) or Unity refuses the result too.
        AssetBundle RewriteOpen(string key)
        {
            if (!CabRewrite) return null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var note = new CabRewriteNote { Key = key };
            AssetBundle b = null;
            try
            {
                var raw = RawBytesOf(key);
                if (raw == null) note.Error = "the source bytes were not found";
                else
                {
                    var bytes = Hs2BundleFile.FromBytes(raw, key).Renamed(NewCabStem, out var renamed, out note.Replaced);
                    foreach (var kv in renamed) note.Names.Add(kv.Key + " -> " + kv.Value);
                    note.Bytes = bytes.LongLength;
                    if (renamed.Count == 0) note.Error = "no CAB- file name to rename";
                    else
                    {
                        OnBundleBytes(key, ref bytes);
                        if (bytes == null) note.Error = "the rewritten bundle was refused at its open point";
                        else
                        {
                            note.Bytes = bytes.LongLength;
                            int before = Volatile.Read(ref cabClashMessages);
                            b = AssetBundle.LoadFromMemory(bytes);
                            if (!b) note.Error = Volatile.Read(ref cabClashMessages) != before ? "the rewritten bundle was refused too" : "the rewritten bundle did not open";
                            else NoteMemoryBacked(key, bytes.LongLength);
                        }
                    }
                }
            }
            catch (Exception e) { note.Error = e.GetType().Name + ": " + e.Message; }
            note.Ms = sw.Elapsed.TotalMilliseconds;
            CabRewrites.Add(note);
            Log.Add(note.ToString());
            Debug.Log("[content] " + note);
            return b;
        }

        // a stem of the same length ("CAB-" + hex digits) from a hash of the old stem and a process-wide counter
        static string NewCabStem(string stem)
        {
            int n = Interlocked.Increment(ref cabRewriteCount);
            var sb = new System.Text.StringBuilder("CAB-");
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                int round = 0;
                while (sb.Length < stem.Length)
                {
                    foreach (var x in md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(stem + "#" + n + "#" + round++))) sb.Append(x.ToString("x2"));
                }
            }
            sb.Length = stem.Length;
            return sb.ToString();
        }

        // The bytes a cache key opens: the texture guard's repaired copy, an abdata file ("<source id>:<path>") or a zipmod's
        // entry ("<zipmod file>!<path>", deflated entries inflated). Null when none.
        byte[] RawBytesOf(string key)
        {
            var repaired = HS2Next.Chara.TextureGuard.RepairedPath(key);
            if (repaired != null) return File.ReadAllBytes(repaired);
            foreach (var s in sources)
            {
                if (s is Hs2InstallSource h && key.StartsWith(h.Id + ":", StringComparison.Ordinal))
                {
                    var f = Path.Combine(h.Abdata, key.Substring(h.Id.Length + 1));
                    return File.Exists(f) ? File.ReadAllBytes(f) : null;
                }
                if (s is ZipmodSourceBase zs)
                    foreach (var z in zs.Zipmods)
                        if (z.Path != null && key.Length > z.Path.Length + 1 && key[z.Path.Length] == '!' && key.StartsWith(z.Path, StringComparison.OrdinalIgnoreCase))
                            return z.Index.Entries.TryGetValue("abdata/" + key.Substring(z.Path.Length + 1), out var ze) ? z.Index.ReadAll(ze) : null;
            }
            return null;
        }

        /// <summary>
        /// The quit: asks the background texture check to stop and waits for its thread at most <paramref name="boundMs"/> ms
        /// (it ends after the bundle it reads). Returns true when no check thread runs any more. A check thread the runtime
        /// aborts during its shutdown is a needless risk; the next start resumes the check from its cache.
        /// </summary>
        public bool StopTexturePrescanForQuit(int boundMs)
        {
            var t = prescan;
            if (t == null || !t.IsAlive) return true;
            prescanStop = true;
            return t.Join(Math.Max(0, boundMs));
        }

        /// <summary>Unloads every bundle (and the objects loaded from them). The list DB stays.</summary>
        public void UnloadAll() => UnloadAll(true);

        /// <summary>
        /// Closes every bundle and clears the bundle tables (the list DB stays); returns the number of bundles closed. With
        /// <paramref name="destroyLoadedObjects"/> true the objects loaded from the bundles are destroyed with them (the tests'
        /// release between cases); with false they stay until the process ends or the scene teardown and the next
        /// UnloadUnusedAssets free them. The app's quit and the end of an Editor play session close the bundles with false:
        /// destroying a video clip whose data lives in a resource file outside its bundle corrupted the Editor's native heap.
        /// </summary>
        public int UnloadAll(bool destroyLoadedObjects)
        {
            foreach (var k in pendingOpens.Keys.ToList()) FinishPending(k);
            int closed = 0;
            foreach (var b in bundles.Values) if (b) { b.Unload(destroyLoadedObjects); closed++; }
            if (LogBundles) BundleLog($"unloadAll({(destroyLoadedObjects ? "true" : "false")}) {closed} bundles");
            bundles.Clear();
            keyOfBundle.Clear();
            memoryBacked.Clear();
            requests.Clear();
            prefetches.Clear();
            Ledger.ForgetAll();
            thumbnailOnly.Clear();
            handedOut.Clear();
            byPath.Clear();
            depManifests.Clear();
            abdataManifests.Clear();
            logged.Clear();
            loose?.ReleaseAll();   // the loose PNG textures go with the bundles (GB10)
            HS2Next.Chara.TextureGuard.ForgetLoaded();
            MaterialCopies.Forget();
            HS2Next.Chara.Mosaic.MosaicUvTransfer.ReleaseAll();   // the mosaic's body-UV mesh copies go with the bundles
            return closed;
        }
    }
}
