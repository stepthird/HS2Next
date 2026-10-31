// The content catalog's zipmod set and per-asset redirect as the bridge sees them (group "content", read-only; owner core,
// Since 0.2.0; CoreCommands.Phase44Names):
//   content.redirect  which source serves a named asset load of a bundle path: the path's copies (the abdata file first, then
//                     the zipmods shipping it in load order) and whether each holds the asset, the copy the redirect serves,
//                     the source the catalog read before the redirect, the switch states; without an asset every name the
//                     path's zipmod copies hold with its serving copy; a list row's asset column instead of a path; without
//                     either the redirectable paths
//   content.zipmods   the zipmod set: per GUID the file the loader keeps, its version and <game> values, whether it loads (and
//                     why not: the game filter or an older version of the GUID), the other files of the GUID and the file the
//                     earlier set (last file of the scan per GUID) took
//   content.stats     (Phase 5.2, Since 1.0.0, experimental; Phase52Names) the open bundles by state (owned, in the LRU of unowned bundles,
//                     pinned, thumbnail-only, unscoped), their bytes against the budget, evictions, reloads, UnloadUnusedAssets,
//                     owners, loads outside a scope, the texture check's numbers and the quarantine; on request the largest
//                     bundles with their owners
// Nothing here opens a bundle; names come from the scan's names pass (an LZMA copy may be read once through Unity).
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Api.Bridge;
using HS2Next.Content;
using HS2Next.Content.Studio;

namespace HS2Next.App.Commands
{
    public static class ContentCommands
    {
        /// <summary>The commands of this file (in CoreCommands.Phase44Names after CharaEditCommands.Phase44Names).</summary>
        public static readonly IReadOnlyList<string> Names = new[] { "content.redirect", "content.zipmods" };

        /// <summary>The commands of this file added in Phase 5.2 (in CoreCommands.Phase52Names).</summary>
        public static readonly IReadOnlyList<string> Phase52Names = new[] { "content.stats" };

        /// <summary>content.stats: bundles listed by default and at most.</summary>
        public const int DefaultStatsTop = 20, MaxStatsTop = 500;

        /// <summary>The help group of these commands.</summary>
        public const string Group = "content";

        /// <summary>content.zipmods: rows per reply by default and at most.</summary>
        public const int DefaultZipmodLimit = 10000, MaxZipmodLimit = 10000;

        static readonly string[] ZipmodFilters = { "all", "loaded", "skipped", "several" };

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("content.redirect", "Which source serves a named asset load of a bundle path (read-only): the path's copies (the abdata file first, then the zipmods shipping the path in load order) with whether each holds the asset, the copy the per-asset redirect serves it from, the source the catalog read before the redirect, and the switch states. Without asset: every name the zipmod copies hold with its serving copy; with row: the bundle path and asset of a list row's column; with neither: the redirectable paths.", Group)
                .Arg("path", ArgTypes.String, "an abdata-relative bundle path (\"chara/30/fo_top_30.unity3d\"; the exact case is asked first, then any case)")
                .Arg("asset", ArgTypes.String, "an asset name (case ignored, as a load by name); default every name of the path's zipmod copies")
                .Arg("row", ArgTypes.Object, "instead of path / asset: {category, ref \"<guid>#<id>\", column (the asset column, e.g. MainData, ThumbTex, OverBodyMask), ab (its bundle column; default <column>AB when the row has it, ThumbAB for ThumbTex, TexAB for Tex* columns, else MainAB)}")
                .Return("path", ArgTypes.String, "the path (the indexed case)", false)
                .Return("redirectable", ArgTypes.Any, "the path has two or more sources (with neither path nor row: the number of such paths)")
                .Return("copies", ArgTypes.ArrayOf(ArgTypes.Object), "{guid (null = the abdata file), file, has (holds the asset; null: names unknown or no asset), names (how many), compression, cab (the serialized files' CAB names), resourceFiles, externals}", false)
                .Return("served", ArgTypes.Object, "{kind (zipmod / abdata / none), guid, file}: the copy a load is served from now (the redirect's when it is on)", false)
                .Return("today", ArgTypes.Object, "{kind, guid, file}: the source without the redirect (the row's own zipmod, else the first shipping one, else abdata)", false)
                .Return("switch", ArgTypes.Object, "{redirect (ContentCatalog.SideloaderAssetRedirect), zipmodSet (ContentCatalog.SideloaderZipmodSet)}")
                .Return("assets", ArgTypes.ArrayOf(ArgTypes.Object), "without asset: {name, served {kind, guid}} per name of the zipmod copies (abdata-only names are served by the abdata file)", false)
                .Return("loads", ArgTypes.Int, "loads of the path this session that the redirect served from another source than before", false)
                .Return("cabClashes", ArgTypes.ArrayOf(ArgTypes.Object), "the catalog's content.cabClash findings naming the path: {key, other, resolution}", false)
                .Return("paths", ArgTypes.ArrayOf(ArgTypes.String), "with neither path nor row: the redirectable paths (ordinal order)", false)
                .Example("{\"cmd\":\"content.redirect\",\"path\":\"chara/30/fo_top_30.unity3d\",\"asset\":\"p_cf_top_dougi\"}")
                .WithFlags(CommandFlags.ReadOnly), Redirect);
            CommandKit.Register(r, app, CommandKit.Spec("content.zipmods", "The zipmod set (read-only): per GUID in load order the file the loader keeps, its version and <game> values, whether it loads and why not (the game filter: game; another file of the GUID kept: version), the files of the GUID and the file the earlier set (the last file of the scan per GUID, no game filter) takes.", Group)
                .Arg("guid", ArgTypes.String, "one GUID (case ignored; not_found when no scanned zipmod has it)")
                .Arg("filter", ArgTypes.Enum(ZipmodFilters), "all GUIDs, the loaded ones, those with a skipped file, those with several files the game filter keeps (a version choice)", false, "all")
                .Arg("offset", ArgTypes.Int, "first entry", false, 0)
                .Arg("limit", ArgTypes.Int, "entries at most (1.." + MaxZipmodLimit + ")", false, DefaultZipmodLimit)
                .Return("switch", ArgTypes.Object, "{zipmodSet, redirect}")
                .Return("scanned", ArgTypes.Int, "zipmod files of the scan (with a GUID)").Return("guids", ArgTypes.Int, "")
                .Return("loaded", ArgTypes.Int, "GUIDs that load").Return("skipped", ArgTypes.Object, "{game, version}: files the loader skips")
                .Return("notForHs2", ArgTypes.Int, "files whose <game> values name no HS2 game (skipped by the game filter when the zipmod set is on)")
                .Return("several", ArgTypes.Int, "GUIDs with several files the game filter keeps (the version choice decides)").Return("differs", ArgTypes.Int, "GUIDs whose kept file is not the earlier set's")
                .Return("noGuid", ArgTypes.Int, "zipmod files without a GUID (ignored)")
                .Return("total", ArgTypes.Int, "entries after guid / filter")
                .Return("zipmods", ArgTypes.ArrayOf(ArgTypes.Object), "{guid, file (the kept file, else the first), version, games (null = not read), forHs2, loaded, skipped (null, game, version), today (the earlier set's file), files [{file, version, skipped}] (every file of the GUID, scan order)}")
                .Example("{\"cmd\":\"content.zipmods\",\"filter\":\"skipped\"}")
                .WithFlags(CommandFlags.ReadOnly), Zipmods);
            CommandKit.Register(r, app, CommandKit.Spec("content.stats", "The catalog's open bundles and their owners (read-only; answers during boot): counts by state (owned, waiting in the LRU of unowned bundles, pinned, thumbnail-only, unscoped), bytes against the budget, evictions, assets loaded again after an eviction, the UnloadUnusedAssets calls, the live owners, loads outside every owner's scope, the pre-upload texture check's numbers and the quarantined bundles; with bundles the largest bundles with their owners.", Group)
                .Arg("bundles", ArgTypes.Bool, "add the largest open bundles with their owners, bytes and state", false, false)
                .Arg("top", ArgTypes.Int, "with bundles: how many (1.." + MaxStatsTop + ")", false, DefaultStatsTop)
                .Return("open", ArgTypes.Int, "open bundles").Return("owned", ArgTypes.Int, "open bundles with a live owner (not pins)")
                .Return("lru", ArgTypes.Int, "unowned bundles waiting in the LRU").Return("pinned", ArgTypes.Int, "bundles every character or list needs (never evicted)")
                .Return("thumbnailOnly", ArgTypes.Int, "bundles only thumbnails used").Return("unscoped", ArgTypes.Int, "bundles loaded outside every owner's scope only (never evicted)")
                .Return("pendingOpens", ArgTypes.Int, "asynchronous opens in flight")
                .Return("ownedBytes", ArgTypes.Int, "resident + object bytes of the owned, pinned and unscoped bundles")
                .Return("lruBytes", ArgTypes.Int, "resident bytes of the LRU (what the budget limits; object bytes are freed by UnloadUnusedAssets, not by an eviction)").Return("budgetBytes", ArgTypes.Int, "the LRU's byte budget (content.bundleCacheMB)")
                .Return("evictions", ArgTypes.Int, "bundles evicted (unloaded without destroying their loaded objects)")
                .Return("reloadedAssets", ArgTypes.Int, "assets loaded again from a bundle reopened after its eviction (a second object until the first is unused)")
                .Return("unloadUnused", ArgTypes.Object, "{calls, lastMs, medianMs, maxMs}")
                .Return("owners", ArgTypes.Int, "live owners (realizations, edit-unit generations, motions)")
                .Return("unscopedLoads", ArgTypes.Int, "loads outside every owner's scope (pins not counted)")
                .Return("textureCheck", ArgTypes.Object, "{verified, patched, refused, repairedOpens (opens that read a stored repaired copy: repaired in an earlier session or by the repair tool), cacheHits, pending, prescan {done, total}}")
                .Return("quarantined", ArgTypes.ArrayOf(ArgTypes.Object), "{key, stamp}: bundles refused because a process died loading them")
                .Return("prefetch", ArgTypes.Object, "the Maker's hover / focus prefetch: {started, assetRequests (asynchronous asset loads started), used (the edit's flush then took the bundle), expired (ended unused), refused (the asset was refused before a request), limited (not started: 4 in flight), inFlight}")
                .Return("bundles", ArgTypes.ArrayOf(ArgTypes.Object), "with bundles: {key (zipmod file name!path or source:path), owners [labels], residentBytes, objectBytes, state} largest first", false)
                .Example("{\"cmd\":\"content.stats\",\"bundles\":true,\"top\":10}")
                .WithGate(ReadyGate.None).WithFlags(CommandFlags.ReadOnly | CommandFlags.Experimental), Stats);
        }

        // ------------------------------------------------------------ content.stats

        static object Stats(CoreContext c)
        {
            bool list = c.Args.Get("bundles", false);
            int top = c.Args.Get("top", DefaultStatsTop);
            if (top < 1 || top > MaxStatsTop) throw CommandKit.BadArgs("top", "1.." + MaxStatsTop);
            var cat = c.Services?.Catalog;
            var s = cat?.Ledger.Stats() ?? new BundleLedgerStats();
            var x = cat?.StatsExtra() ?? new ContentCatalog.ContentStatsExtra();
            var q = new List<object>();
            foreach (var (key, stamp) in x.Quarantined) q.Add(new BridgeReply().With("key", ShortKey(key)).With("stamp", stamp));
            var r = new BridgeReply().With("open", s.Open).With("owned", s.Owned).With("lru", s.Lru).With("pinned", s.Pinned)
                .With("thumbnailOnly", s.ThumbnailOnly).With("unscoped", s.Unscoped).With("pendingOpens", cat?.PendingOpenCount ?? 0)
                .With("ownedBytes", s.OwnedBytes).With("lruBytes", s.LruBytes).With("budgetBytes", cat != null ? s.BudgetBytes : 0L)
                .With("evictions", s.Evictions).With("reloadedAssets", s.ReloadedAssets)
                .With("unloadUnused", new BridgeReply().With("calls", cat?.UnloadUnusedCalls ?? 0).With("lastMs", Math.Round(cat?.UnloadUnusedLastMs ?? 0, 3))
                    .With("medianMs", Math.Round(cat?.UnloadUnusedMedianMs ?? 0, 3)).With("maxMs", Math.Round(cat?.UnloadUnusedMaxMs ?? 0, 3)))
                .With("owners", s.Owners).With("unscopedLoads", s.UnscopedLoads)
                .With("textureCheck", new BridgeReply().With("verified", x.Verified).With("patched", x.Patched).With("refused", x.Refused)
                    .With("repairedOpens", x.RepairedOpens).With("cacheHits", x.CacheHits).With("pending", x.Pending)
                    .With("prescan", new BridgeReply().With("done", x.PrescanDone).With("total", x.PrescanTotal)))
                .With("quarantined", q)
                .With("prefetch", new BridgeReply().With("started", cat?.PrefetchStarted ?? 0).With("assetRequests", cat?.PrefetchAssetRequests ?? 0)
                    .With("used", cat?.PrefetchUsed ?? 0).With("expired", cat?.PrefetchExpired ?? 0).With("refused", cat?.PrefetchRefused ?? 0)
                    .With("limited", cat?.PrefetchLimited ?? 0).With("inFlight", cat?.PrefetchInFlight ?? 0));
            if (list)
            {
                var l = new List<object>();
                if (cat != null)
                    foreach (var b in cat.Ledger.Largest(top))
                        l.Add(new BridgeReply().With("key", ShortKey(b.Key)).With("owners", b.Owners).With("residentBytes", b.Resident)
                            .With("objectBytes", b.Objects).With("state", b.State));
                r["bundles"] = l;
            }
            return r;
        }

        // A cache key without the folders of its zipmod file ("<file name>!<path>"); abdata keys ("<source>:<path>") as they are.
        static string ShortKey(string key)
        {
            int bang = key?.IndexOf('!') ?? -1;
            if (bang < 0) return key;
            string zip = key.Substring(0, bang);
            return System.IO.Path.GetFileName(zip) + key.Substring(bang);
        }

        static ContentCatalog Catalog(CoreContext c) =>
            c.Services?.Catalog ?? throw new BridgeException(BridgeCodes.Unavailable, "no content catalog (the game data is not loaded)");

        static BridgeReply Switches() =>
            new BridgeReply().With("redirect", ContentCatalog.SideloaderAssetRedirect).With("zipmodSet", ContentCatalog.SideloaderZipmodSet);

        // ------------------------------------------------------------ content.redirect

        static object Redirect(CoreContext c)
        {
            var catalog = Catalog(c);
            var idx = catalog.Redirect;
            bool hasRow = c.Args.Has("row");
            string path = CommandKit.OptString(c, "path"), asset = CommandKit.OptString(c, "asset");
            if (hasRow && (path != null || asset != null))
                throw CommandKit.BadArgs("row", "row or path / asset", "give row or path (with asset), not both");
            if (!hasRow && path == null)
            {
                if (asset != null) throw new BridgeException(BridgeError.Missing("path", ArgTypes.String));
                // the redirectable paths
                var paths = idx != null ? new List<string>(idx.RedirectablePaths) : new List<string>();
                var stats = new BridgeReply();
                if (idx != null) foreach (var kv in idx.Stats.OrderBy(k => k.Key, StringComparer.Ordinal)) stats[kv.Key] = kv.Value;
                return new BridgeReply().With("redirectable", paths.Count).With("withAbdata", idx != null ? paths.Count(idx.HasAbdata) : 0)
                    .With("paths", paths).With("loadsByPath", stats).With("switch", Switches())
                    .With("namesPass", catalog.NamesPass?.ToString()).With("cabClashes", catalog.CabClashes.Count);
            }
            ListEntry row = null;
            BridgeReply rowReply = null;
            if (hasRow)
            {
                if (c.Args.Kind("row") != ArgKind.Object) throw CommandKit.BadArgs("row", ArgTypes.Object);
                var ra = c.Args.Nested("row");
                if (ra.Kind("category") != ArgKind.Integer) throw CommandKit.BadArgs("row.category", ArgTypes.Int);
                int category = ra.Get<int>("category");
                if (ra.Kind("ref") != ArgKind.String) throw CommandKit.BadArgs("row.ref", ArgTypes.ContentId);
                string refText = ra.Get<string>("ref");
                if (!ContentId.TryParse(refText, out var id) || id.IsEmpty) throw CommandKit.BadArgs("row.ref", ArgTypes.ContentId, "bad content id \"" + refText + "\": expected \"guid#no\"");
                if (ra.Kind("column") != ArgKind.String) throw CommandKit.BadArgs("row.column", ArgTypes.String);
                string column = ra.Get<string>("column");
                row = catalog.GetEntry(category, id);
                if (row == null) throw CommandKit.NotFound("no row " + id + " in list " + category, "row");
                string ab = ra.Kind("ab") == ArgKind.String ? ra.Get<string>("ab") : BundleColumnOf(row, column);
                path = row[ab];
                asset = row[column];
                if (string.IsNullOrEmpty(path) || path == "0") throw CommandKit.BadArgs("row.ab", "a bundle column the row fills", "row " + id + " has no bundle in " + ab);
                if (string.IsNullOrEmpty(asset) || asset == "0") throw CommandKit.BadArgs("row.column", "an asset column the row fills", "row " + id + " has no asset in " + column);
                rowReply = new BridgeReply().With("category", category).With("ref", id.ToString()).With("column", column).With("ab", ab);
            }
            var x = catalog.ExplainLoad(row, path, asset);
            if (x == null) throw CommandKit.BadArgs("path", ArgTypes.String, "empty path");
            var copies = new List<object>();
            foreach (var cp in x.Copies)
                copies.Add(new BridgeReply().With("guid", cp.Guid).With("file", cp.File).With("has", cp.Has).With("names", cp.Names)
                    .With("compression", cp.Compression).With("cab", cp.SerializedFiles).With("resourceFiles", cp.ResourceFiles).With("externals", cp.Externals));
            var reply = new BridgeReply().With("path", x.Path).With("asset", x.Asset).With("indexed", x.Indexed).With("redirectable", x.Redirectable)
                .With("hasAbdata", x.HasAbdata).With("copies", copies)
                .With("served", new BridgeReply().With("kind", x.ServedKind).With("guid", x.ServedGuid).With("file", x.ServedFile))
                .With("today", new BridgeReply().With("kind", x.TodayKind).With("guid", x.TodayGuid).With("file", x.TodayFile))
                .With("switch", Switches());
            if (rowReply != null) reply["row"] = rowReply;
            if (asset == null)
            {
                var assets = new List<object>();
                if (idx != null)
                    foreach (var name in idx.AllNames(x.Path))
                    {
                        var e = catalog.ExplainLoad(row, x.Path, name);
                        assets.Add(new BridgeReply().With("name", name).With("served", new BridgeReply().With("kind", e?.ServedKind).With("guid", e?.ServedGuid)));
                    }
                reply["assets"] = assets;
            }
            reply["loads"] = idx != null && idx.Stats.TryGetValue(x.Path, out int loads) ? loads : 0;
            var clashes = new List<object>();
            string bare = StripExt(x.Path);
            foreach (var cc in catalog.CabClashes)
                if (KeyNames(cc.Key, bare) || KeyNames(cc.OtherKey, bare))
                    clashes.Add(new BridgeReply().With("key", cc.Key).With("other", cc.OtherKey).With("resolution", cc.Resolution));
            reply["cabClashes"] = clashes;
            return reply;
        }

        /// <summary>The bundle column of an asset column of a list row: "&lt;column&gt;AB" when the row has it (the mask
        /// columns), ThumbAB for ThumbTex, TexAB for the Tex* columns when present, else MainAB.</summary>
        public static string BundleColumnOf(ListEntry row, string column)
        {
            if (!string.IsNullOrEmpty(column) && !string.IsNullOrEmpty(row?[column + "AB"])) return column + "AB";
            if (column == "ThumbTex") return "ThumbAB";
            if (column != null && column.StartsWith("Tex", StringComparison.Ordinal) && !string.IsNullOrEmpty(row?["TexAB"])) return "TexAB";
            return "MainAB";
        }

        static string StripExt(string path) =>
            path != null && path.EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase) ? path.Substring(0, path.Length - ".unity3d".Length) : path;

        /// <summary>A catalog bundle key ("&lt;zip&gt;!&lt;path&gt;" or the abdata path) names the path (any case, '/' or '\').</summary>
        static bool KeyNames(string key, string barePath)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(barePath)) return false;
            string k = StripExt(key.Replace('\\', '/'));
            string p = barePath.Replace('\\', '/');
            return k.EndsWith(p, StringComparison.OrdinalIgnoreCase) && (k.Length == p.Length || k[k.Length - p.Length - 1] == '/' || k[k.Length - p.Length - 1] == '!' || k[k.Length - p.Length - 1] == ':');
        }

        // ------------------------------------------------------------ content.zipmods

        /// <summary>Two or more files of the GUID load on HS2 (the files the game filter skips take no part in the version
        /// choice).</summary>
        static bool Several(List<ZipmodInfo> files) => files.Count(z => SideloaderZipmods.LoadsOnHs2(z.Games)) >= 2;

        static object Zipmods(CoreContext c)
        {
            var catalog = Catalog(c);
            string guid = CommandKit.OptString(c, "guid");
            string filter = c.Args.Get("filter", "all");
            if (Array.IndexOf(ZipmodFilters, filter) < 0) throw CommandKit.BadArgs("filter", ArgTypes.Enum(ZipmodFilters));
            int offset = c.Args.Get("offset", 0), limit = c.Args.Get("limit", DefaultZipmodLimit);
            if (offset < 0) throw CommandKit.BadArgs("offset", "an index ≥ 0");
            if (limit < 1 || limit > MaxZipmodLimit) throw CommandKit.BadArgs("limit", "1.." + MaxZipmodLimit);
            var scanned = catalog.ScannedZipmods;
            // the GUIDs (case ignored, as the catalog keys them) with their files in scan order
            var groups = new Dictionary<string, List<ZipmodInfo>>(StringComparer.OrdinalIgnoreCase);
            int noGuid = 0, game = 0, version = 0, notForHs2 = 0;
            foreach (var z in scanned)
            {
                if (z.Guid == null) { noGuid++; continue; }
                if (!groups.TryGetValue(z.Guid, out var l)) groups[z.Guid] = l = new List<ZipmodInfo>(1);
                l.Add(z);
                if (z.SkippedBy == "game") game++;
                else if (z.SkippedBy == "version") version++;
                if (!SideloaderZipmods.LoadsOnHs2(z.Games)) notForHs2++;
            }
            if (guid != null && !groups.ContainsKey(guid))
                throw CommandKit.NotFound("no scanned zipmod has the GUID \"" + guid + "\"", "guid",
                    groups.Keys.Where(k => k.IndexOf(guid, StringComparison.OrdinalIgnoreCase) >= 0).Take(5).ToList());
            var loaded = catalog.ZipmodsByGuid;
            int several = 0, differs = 0;
            foreach (var kv in groups)
            {
                if (Several(kv.Value)) several++;
                if (loaded.TryGetValue(kv.Key, out var kept) && !ReferenceEquals(kept, kv.Value[kv.Value.Count - 1])) differs++;
            }
            var keys = groups.Keys.ToList();
            keys.Sort(StudioCsv.LoadOrder);
            var rows = new List<object>();
            int total = 0;
            foreach (var g in keys)
            {
                if (guid != null && !string.Equals(g, guid, StringComparison.OrdinalIgnoreCase)) continue;
                var files = groups[g];
                bool isLoaded = loaded.TryGetValue(g, out var kept);
                bool skippedAny = files.Any(z => z.SkippedBy != null);
                if (filter == "loaded" && !isLoaded || filter == "skipped" && !skippedAny || filter == "several" && !Several(files)) continue;
                if (total++ < offset || rows.Count >= limit) continue;
                var main = kept ?? files[0];
                var fl = new List<object>(files.Count);
                foreach (var z in files) fl.Add(new BridgeReply().With("file", z.Path).With("version", z.Version).With("skipped", z.SkippedBy));
                rows.Add(new BridgeReply().With("guid", main.Guid).With("file", main.Path).With("version", main.Version)
                    .With("games", main.Games != null ? new List<string>(main.Games) : null).With("forHs2", SideloaderZipmods.LoadsOnHs2(main.Games))
                    .With("loaded", isLoaded).With("skipped", isLoaded ? null : main.SkippedBy ?? "notLoaded")
                    .With("today", files[files.Count - 1].Path).With("files", fl));
            }
            return new BridgeReply().With("switch", Switches()).With("scanned", scanned.Count - noGuid).With("guids", groups.Count)
                .With("loaded", loaded.Count).With("skipped", new BridgeReply().With("game", game).With("version", version))
                .With("notForHs2", notForHs2).With("several", several).With("differs", differs).With("noGuid", noGuid)
                .With("total", total).With("zipmods", rows);
        }
    }
}
