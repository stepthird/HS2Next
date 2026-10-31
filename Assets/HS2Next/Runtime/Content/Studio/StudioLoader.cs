// Builds a StudioDb:
//   phase 1: read the vanilla info bundles (ExcelData / ItemColorData) and the zipmod CSVs (merged
//     into Sideloader virtual bundles), sort all bundles, and parse every list in the game's order
//     into raw definitions with a global load-order number;
//   phase 2: apply last-wins per key, build the global anime/voice indexes, attach categories to
//     groups, bone lists and colour flags to items, and compute visibility.
// Vanilla bundles are opened through the catalog and unloaded right after reading. Main thread only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace HS2Next.Content.Studio
{
    internal sealed class StudioLoader
    {
        // ---------------------------------------------------------------- raw data

        sealed class RawRow
        {
            public string[] Cells;
            public ZipmodInfo Zip;                  // null = vanilla
            public string File;                     // CSV entry path (mods) for diagnostics
        }

        sealed class RawList
        {
            public string Name, Lower, Prefix;
            public bool Csv;                        // created from a CSV (else a vanilla ExcelData)
            public int HeaderCount;                 // header rows of the first CSV
            public readonly List<RawRow> Rows = new List<RawRow>();
            public int Files;                       // CSV files merged into this list
        }

        sealed class RawBundle
        {
            public string Path, FileName;
            public bool Virtual;
            public readonly Dictionary<string, RawList> Lists = new Dictionary<string, RawList>(StringComparer.Ordinal);
            public readonly List<RawList> Ordered = new List<RawList>();
            public readonly List<(string name, List<StudioItemColor> records)> Colors = new List<(string, List<StudioItemColor>)>();
        }

        sealed class GroupDef { public StudioListKind Kind; public int Number, Sort; public string Name, Guid; public long Order; public bool Vanilla; }
        sealed class CatDef { public StudioListKind Kind; public int Group, Number, Sort; public string Name, Guid; public long Order; public bool Vanilla, Ai, Dropped; }

        // ---------------------------------------------------------------- state

        readonly ContentCatalog catalog;
        readonly StudioLoadOptions opt;
        readonly StudioDb db;
        bool gate => opt.EmulateAIInstallGate;
        long seq;

        readonly List<GroupDef> groupDefs = new List<GroupDef>();
        readonly List<CatDef> catDefs = new List<CatDef>();
        readonly List<StudioItem> items = new List<StudioItem>();
        readonly List<StudioItemBoneList> boneListDefs = new List<StudioItemBoneList>();
        readonly List<StudioItemColor> colorDefs = new List<StudioItemColor>();
        readonly List<StudioAnime> anime = new List<StudioAnime>();
        readonly List<StudioVoice> voices = new List<StudioVoice>();
        readonly List<StudioMap> maps = new List<StudioMap>();
        readonly List<StudioLight> lights = new List<StudioLight>();
        readonly List<StudioBgm> bgms = new List<StudioBgm>();
        readonly List<StudioFilter> filters = new List<StudioFilter>();
        readonly List<StudioProbe> probes = new List<StudioProbe>();
        readonly List<StudioSky> skies = new List<StudioSky>();
        readonly List<StudioBone> bones = new List<StudioBone>();
        readonly List<StudioAccessoryPointGroup> apgs = new List<StudioAccessoryPointGroup>();
        readonly Dictionary<StudioListKind, HashSet<int>> definedGroups = new Dictionary<StudioListKind, HashSet<int>>
        {
            [StudioListKind.ItemGroup] = new HashSet<int>(), [StudioListKind.AnimeGroup] = new HashSet<int>(), [StudioListKind.VoiceGroup] = new HashSet<int>(),
        };
        readonly HashSet<int> unreadModItemGroups = new HashSet<int>();
        readonly Dictionary<string, bool> bundleExists = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        static readonly string[] SinglePrefixes = { "accessorypointgroup", "bone", "itemgroup", "light", "animegroup", "voicegroup", "bgm", "map", "filter", "probe", "studiosky" };
        static readonly string[] MultiPrefixes = { "itemcategory", "itemlist", "itembonelist", "itemdata", "animecategory", "anime", "hanime", "voicecategory", "voice" };
        // Sideloader turns two-field rows [id, name] of these kinds into [id, id, name].
        static readonly HashSet<string> NormalizedPrefixes = new HashSet<string>(StringComparer.Ordinal)
            { "itemcategory", "animecategory", "voicecategory", "itemgroup", "animegroup", "voicegroup", "bone", "mapcategory" };

        static readonly Regex ReItemCategory = Re("itemcategory_(\\d*)_(\\d*)"), ReItemList = Re("itemlist_(\\d*)_(\\d*)_(\\d*)"),
            ReItemBoneList = Re("itembonelist_(\\d*)_(\\d*)"), ReAnimeCategory = Re("animecategory_(\\d*)_(\\d*)"),
            ReAnime = Re("anime_(\\d*)_(\\d*)_(\\d*)"), ReHAnime = Re("hanime_(\\d*)_(\\d*)_(\\d*)"),
            ReVoiceCategory = Re("voicecategory_(\\d*)_(\\d*)"), ReVoice = Re("voice_(\\d*)_(\\d*)_(\\d*)");

        static Regex Re(string p) => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        StudioLoader(ContentCatalog catalog, StudioLoadOptions options)
        {
            this.catalog = catalog; opt = options; db = new StudioDb(options);
        }

        internal static StudioDb Build(ContentCatalog catalog, StudioLoadOptions options)
        {
            var l = new StudioLoader(catalog, options ?? new StudioLoadOptions());
            l.Run();
            return l.db;
        }

        void Count(string key, int n = 1) => db.Count(key, n);
        void Diag(string s) => db.Diagnostics.Add(s);
        static string Guid(RawRow r) => r.Zip?.Guid ?? ContentIds.Vanilla;
        static string Cell(string[] c, int i) => i < c.Length ? c[i] : null;
        static string Str(string[] c, int i) => i < c.Length ? c[i] : "";
        static bool TryInt(string s, out int v) => StudioCsv.TryInt(s, out v);
        static bool Blank(string[] c) { foreach (var s in c) if (!string.IsNullOrWhiteSpace(s)) return false; return true; }

        static string Loc(RawBundle b, RawList l, int row, RawRow r) =>
            $"{b.Path}:{l.Name} row {row}" + (r?.Zip != null ? $" [{r.Zip.Guid} | {System.IO.Path.GetFileName(r.Zip.Path)}]" : "");

        void Bad(string kind, string why, RawBundle b, RawList l, int row, RawRow r)
        {
            Count($"skip.{kind}.{why}");
            Diag($"skip {kind} row ({why}): {Loc(b, l, row, r)}: {string.Join(",", r.Cells.Take(8))}");
        }

        /// <summary>Anime/HAnime bundle check: the bundle exists in some source or zipmod (Sideloader-extended).</summary>
        bool BundleExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (!bundleExists.TryGetValue(path, out var e)) bundleExists[path] = e = catalog.BundleExists(path);
            return e;
        }

        // ================================================================ phase 1

        void Run()
        {
            var total = Stopwatch.StartNew();
            var bundles = new Dictionary<string, RawBundle>(StringComparer.OrdinalIgnoreCase);

            // IsInstallAdd and the vanilla source: the first source with Studio info bundles.
            IContentSource vanillaSource = null;
            List<string> vanillaPaths = null;
            foreach (var s in catalog.Sources)
            {
                var p = s.EnumerateStudioInfoBundles().ToList();
                if (p.Count == 0) continue;
                if (vanillaSource == null) { vanillaSource = s; vanillaPaths = p; }
                else Diag($"second source with Studio info bundles ignored: {s.Id}");
            }
            db.IsInstallAdd = opt.IsInstallAdd ?? (vanillaSource != null && vanillaSource.HasBundle("add50"));

            var sw = Stopwatch.StartNew();
            if (vanillaSource != null) ReadVanilla(vanillaSource, vanillaPaths, bundles);
            db.TimingsMs["vanilla read"] = sw.ElapsedMilliseconds;

            sw.Restart();
            ReadMods(bundles);
            db.TimingsMs["mod merge"] = sw.ElapsedMilliseconds;

            sw.Restart();
            foreach (var b in bundles.Values.OrderBy(b => b.Path, StudioCsv.LoadOrder)) ProcessBundle(b);
            db.TimingsMs["parse"] = sw.ElapsedMilliseconds;

            sw.Restart();
            Resolve();
            db.TimingsMs["resolve"] = sw.ElapsedMilliseconds;

            sw.Restart();
            AppendAccessoryItems();
            db.TimingsMs["accessory items"] = sw.ElapsedMilliseconds;
            db.TimingsMs["total"] = total.ElapsedMilliseconds;
        }

        RawBundle GetBundle(Dictionary<string, RawBundle> bundles, string path, bool isVirtual)
        {
            if (!bundles.TryGetValue(path, out var b))
            {
                bundles[path] = b = new RawBundle { Path = path, FileName = System.IO.Path.GetFileNameWithoutExtension(path), Virtual = isVirtual };
                if (isVirtual) Count("mod.virtualBundles");
            }
            return b;
        }

        static RawList NewList(string name, bool csv)
        {
            var lower = name.ToLowerInvariant();
            int u = lower.IndexOf('_');
            return new RawList { Name = name, Lower = lower, Prefix = u < 0 ? lower : lower.Substring(0, u), Csv = csv };
        }

        // Vanilla list cache: <cacheDir>/studiolists-<source id>-<hash of abdata>.tsv, valid while every
        // info bundle keeps its path, size and mtime. Lines (tab separated, fields escaped):
        //   F path size:ticks (one per bundle, the validity stamp) | B path | L file name | R cell ...
        //   I asset-name count (-1 = no colorDatas member) | c group category id flags(4 x 0/1)
        const string VanillaCacheHeader = "#hs2next-studiolists\t1";

        string VanillaCacheFile(IContentSource src, out string abdata)
        {
            abdata = (src as Hs2InstallSource)?.Abdata;
            if (abdata == null || catalog.CacheDir == null) return null;
            var bad = System.IO.Path.GetInvalidFileNameChars();
            var id = new string(src.Id.Select(ch => Array.IndexOf(bad, ch) >= 0 ? '_' : ch).ToArray());
            return System.IO.Path.Combine(catalog.CacheDir, $"studiolists-{id}-{ZipmodSourceBase.PathHash(abdata)}.tsv");
        }

        static string Esc(string s) => s == null ? "\\0" :
            s.IndexOfAny(EscChars) < 0 ? s : s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");
        static readonly char[] EscChars = { '\\', '\t', '\n', '\r' };

        static string Unesc(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            if (s == "\\0") return null;
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char n = s[++i];
                sb.Append(n == 't' ? '\t' : n == 'n' ? '\n' : n == 'r' ? '\r' : n);
            }
            return sb.ToString();
        }

        void ReadVanilla(IContentSource src, List<string> paths, Dictionary<string, RawBundle> bundles)
        {
            var cacheFile = VanillaCacheFile(src, out var abdata);
            List<string> stamps = null;
            if (cacheFile != null)
            {
                stamps = paths.Select(p =>
                {
                    var fi = new FileInfo(System.IO.Path.Combine(abdata, p));
                    return $"F\t{Esc(p)}\t{fi.Length}:{fi.LastWriteTimeUtc.Ticks}";
                }).ToList();
                if (TryReadVanillaCache(cacheFile, stamps, bundles)) { Count("vanilla.fromCache"); return; }
            }
            var outLines = cacheFile != null ? new List<string> { VanillaCacheHeader } : null;
            outLines?.AddRange(stamps);
            foreach (var rel in paths)
            {
                var ab = src.OpenBundle(rel, catalog);
                if (!ab) { Diag($"vanilla info bundle unreadable: {rel}"); outLines = null; continue; }
                Count("vanilla.bundles");
                outLines?.Add("B\t" + Esc(rel));
                var rb = GetBundle(bundles, rel, false);
                foreach (var asset in ab.GetAllAssetNames())
                {
                    var file = System.IO.Path.GetFileNameWithoutExtension(asset);
                    if (file.StartsWith("itemdata_", StringComparison.OrdinalIgnoreCase))
                    {
                        Count("vanilla.listAssets.itemdata");
                        var cd = ab.LoadAsset<global::Studio.ItemColorData>(asset);
                        List<StudioItemColor> recs = null;
                        var name = cd != null ? cd.name : file;
                        if (cd != null && cd.colorDatas != null)
                        {
                            recs = new List<StudioItemColor>(cd.colorDatas.Count);
                            foreach (var c in cd.colorDatas)
                            {
                                if (c == null) continue;
                                var r = new StudioItemColor { Group = c.group, Category = c.category, Id = c.id, ListName = name };
                                for (int k = 0; k < 4 && c.colors != null && k < c.colors.Count; k++) r.Colors[k] = c.colors[k] != 0;
                                recs.Add(r);
                            }
                        }
                        rb.Colors.Add((name, recs));
                        if (outLines != null)
                        {
                            outLines.Add($"I\t{Esc(name)}\t{(recs == null ? -1 : recs.Count)}");
                            if (recs != null)
                                foreach (var r in recs)
                                    outLines.Add($"c\t{r.Group}\t{r.Category}\t{r.Id}\t{(r.Colors[0] ? 1 : 0)}{(r.Colors[1] ? 1 : 0)}{(r.Colors[2] ? 1 : 0)}{(r.Colors[3] ? 1 : 0)}");
                        }
                        continue;
                    }
                    var ex = ab.LoadAsset<ExcelData>(asset);
                    if (ex == null) { Count("vanilla.nonListAssets"); Diag($"vanilla asset is not a list: {rel}:{asset}"); continue; }
                    var rl = NewList(file, false);
                    rl.Name = string.IsNullOrEmpty(ex.name) ? file : ex.name;
                    if (ex.list != null)
                        foreach (var p in ex.list)
                            rl.Rows.Add(new RawRow { Cells = p?.list != null ? p.list.ToArray() : Array.Empty<string>() });
                    Count("vanilla.listAssets.exceldata");
                    if (AddVanillaList(rb, rl) && outLines != null)
                    {
                        outLines.Add($"L\t{Esc(file)}\t{Esc(rl.Name)}");
                        foreach (var r in rl.Rows) outLines.Add("R" + string.Concat(r.Cells.Select(x => "\t" + Esc(x))));
                    }
                }
                catalog.UnloadBundle(ab);
            }
            if (outLines != null)
            {
                try
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cacheFile));
                    File.WriteAllLines(cacheFile, outLines);
                }
                catch (Exception e) { Diag($"vanilla list cache not written: {e.Message}"); }
            }
        }

        bool AddVanillaList(RawBundle rb, RawList rl)
        {
            if (rb.Lists.ContainsKey(rl.Lower)) { Diag($"duplicate list name in vanilla bundle ignored: {rb.Path}:{rl.Name}"); return false; }
            rb.Lists[rl.Lower] = rl; rb.Ordered.Add(rl);
            return true;
        }

        bool TryReadVanillaCache(string file, List<string> stamps, Dictionary<string, RawBundle> bundles)
        {
            if (!File.Exists(file)) return false;
            var lines = File.ReadAllLines(file);
            if (lines.Length < 1 + stamps.Count || lines[0] != VanillaCacheHeader) return false;
            for (int i = 0; i < stamps.Count; i++) if (lines[1 + i] != stamps[i]) return false;
            if (lines.Length > 1 + stamps.Count && lines[1 + stamps.Count].StartsWith("F\t", StringComparison.Ordinal)) return false;
            var local = new Dictionary<string, RawBundle>(StringComparer.OrdinalIgnoreCase);
            RawBundle rb = null; RawList rl = null; List<StudioItemColor> recs = null; string colorName = null;
            for (int i = 1 + stamps.Count; i < lines.Length; i++)
            {
                var t = lines[i].Split('\t');
                switch (t[0])
                {
                    case "B": rb = GetBundle(local, Unesc(t[1]), false); rl = null; recs = null; break;
                    case "L":
                        rl = NewList(Unesc(t[1]), false); rl.Name = Unesc(t[2]); recs = null;
                        if (!AddVanillaList(rb, rl)) rl = null;
                        break;
                    case "R":
                        if (rl == null) break;
                        var cells = new string[t.Length - 1];
                        for (int k = 1; k < t.Length; k++) cells[k - 1] = Unesc(t[k]);
                        rl.Rows.Add(new RawRow { Cells = cells });
                        break;
                    case "I":
                        colorName = Unesc(t[1]);
                        recs = int.Parse(t[2]) < 0 ? null : new List<StudioItemColor>();
                        rb.Colors.Add((colorName, recs));
                        rl = null;
                        break;
                    case "c":
                        if (recs == null) break;
                        var r = new StudioItemColor { Group = int.Parse(t[1]), Category = int.Parse(t[2]), Id = int.Parse(t[3]), ListName = colorName };
                        for (int k = 0; k < 4; k++) r.Colors[k] = t[4][k] == '1';
                        recs.Add(r);
                        break;
                    default: return false;
                }
            }
            foreach (var kv in local) bundles[kv.Key] = kv.Value;
            Count("vanilla.bundles", local.Count);
            Count("vanilla.listAssets.exceldata", local.Values.Sum(b => b.Ordered.Count));
            Count("vanilla.listAssets.itemdata", local.Values.Sum(b => b.Colors.Count));
            return true;
        }

        void ReadMods(Dictionary<string, RawBundle> bundles)
        {
            var zips = catalog.ZipmodsByGuid.Values
                .Where(z => z.Guid != null && !new ContentId(z.Guid, 0).IsVanilla && z.StudioCsvs.Count > 0)
                .OrderBy(z => z.Guid, StudioCsv.LoadOrder).ToList();
            Count("mod.zipmodsWithStudioCsvs", zips.Count);
            var guidsPerBundle = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var z in zips)
                foreach (var csv in z.StudioCsvs)
                {
                    Count("mod.csvFiles");
                    var rb = GetBundle(bundles, csv.VirtualBundle, true);
                    if (!guidsPerBundle.TryGetValue(rb.Path, out var gs)) guidsPerBundle[rb.Path] = gs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    gs.Add(z.Guid);
                    var name = csv.ListName;
                    var fresh = NewList(name, true);
                    bool normalize = fresh.Lower.IndexOf('_') >= 0 && NormalizedPrefixes.Contains(fresh.Prefix);
                    int first = 0;
                    if (!rb.Lists.TryGetValue(fresh.Lower, out var rl))
                    {
                        rl = fresh;
                        rl.HeaderCount = csv.HeaderCount;
                        rl.Files = 1;
                        rb.Lists[rl.Lower] = rl; rb.Ordered.Add(rl);
                        for (; first < csv.HeaderCount && first < csv.Rows.Count; first++)
                            rl.Rows.Add(new RawRow { Cells = csv.Rows[first], Zip = z, File = csv.EntryPath });
                    }
                    else
                    {
                        // Same (virtual bundle, list name): merged, entries only.
                        Count(rl.Csv ? "mod.csvMergedIntoSharedList" : "mod.csvMergedIntoVanillaList");
                        rl.Files++;
                        if (MultiPrefixes.Contains(rl.Prefix)) Diag($"multi-list CSV name shared by several files (merged): {rb.Path}:{name} [{z.Guid}]");
                        first = Math.Min(csv.HeaderCount, csv.Rows.Count);
                    }
                    for (int k = first; k < csv.Rows.Count; k++)
                    {
                        var cells = csv.Rows[k];
                        if (normalize && cells.Length == 2) { cells = new[] { cells[0], cells[0], cells[1] }; Count("mod.twoFieldRowsNormalized"); }
                        rl.Rows.Add(new RawRow { Cells = cells, Zip = z, File = csv.EntryPath });
                    }
                }
            Count("mod.virtualBundlesSharedByGuids", guidsPerBundle.Values.Count(s => s.Count > 1));
            // Rows beyond the first CSV's header rows whose first cell is empty or not an integer are removed.
            foreach (var rb in bundles.Values)
                foreach (var rl in rb.Ordered)
                {
                    int from = rl.Csv ? Math.Min(rl.HeaderCount, rl.Rows.Count) : 0;
                    int removed = 0;
                    for (int k = rl.Rows.Count - 1; k >= from; k--)
                        if (rl.Rows[k].Zip != null && !TryInt(Cell(rl.Rows[k].Cells, 0), out _)) { rl.Rows.RemoveAt(k); removed++; }
                    if (removed > 0) Count("mod.csvRowsRemovedNonInt", removed);
                }
        }

        // ---------------------------------------------------------------- per bundle

        void ProcessBundle(RawBundle b)
        {
            if (TryInt(b.FileName, out int bn) && bn >= 50 && !db.IsInstallAdd)
            {
                Count("skip.bundle.add50"); Diag($"bundle skipped (number >= 50 without add50): {b.Path}");
                return;
            }
            CheckNames(b);
            foreach (var l in Single(b, "accessorypointgroup")) ParseAccessoryPointGroups(b, l);
            foreach (var l in Single(b, "bone")) ParseBones(b, l);
            foreach (var l in Single(b, "itemgroup")) ParseGroups(b, l, StudioListKind.ItemGroup);
            foreach (var (l, n) in Multi(b, ReItemCategory, "itemcategory", 2, true, x => (x[1], x[0], 0))) ParseCategories(b, l, StudioListKind.ItemCategory, n[1]);
            foreach (var (l, n) in Multi(b, ReItemList, "itemlist", 3, true, x => (x[1], x[2], x[0]))) ParseItems(b, l, n[0], n[1], n[2]);
            foreach (var (l, n) in Multi(b, ReItemBoneList, "itembonelist", 2, false, x => (x[0], x[1], 0))) ParseItemBoneList(b, l, n[0], n[1]);
            ParseItemData(b);
            foreach (var l in Single(b, "light")) ParseLights(b, l);
            foreach (var l in Single(b, "animegroup")) ParseGroups(b, l, StudioListKind.AnimeGroup);
            foreach (var (l, n) in Multi(b, ReAnimeCategory, "animecategory", 2, true, x => (x[1], x[0], 0))) ParseCategories(b, l, StudioListKind.AnimeCategory, n[1]);
            foreach (var (l, n) in Multi(b, ReAnime, "anime", 3, true, x => (x[1], x[2], x[0]))) ParseAnime(b, l, n[0], false);
            foreach (var (l, n) in Multi(b, ReHAnime, "hanime", 3, true, x => (x[1], x[2], x[0]))) ParseAnime(b, l, n[0], true);
            foreach (var l in Single(b, "voicegroup")) ParseGroups(b, l, StudioListKind.VoiceGroup);
            foreach (var (l, n) in Multi(b, ReVoiceCategory, "voicecategory", 2, true, x => (x[1], x[0], 0))) ParseCategories(b, l, StudioListKind.VoiceCategory, n[1]);
            foreach (var (l, _) in Multi(b, ReVoice, "voice", 3, true, x => (x[0], x[1], x[2]))) ParseVoices(b, l);
            foreach (var l in Single(b, "bgm")) ParseSimple(b, l, bgms, "Bgm");
            foreach (var l in Single(b, "map")) ParseMaps(b, l);
            foreach (var l in Single(b, "filter")) ParseSimple(b, l, filters, "Filter");
            foreach (var l in Single(b, "probe")) ParseSimple(b, l, probes, "Probe");
            foreach (var l in Single(b, "studiosky")) ParseSkies(b, l);
        }

        // Single-list kinds: exactly "<kind>_<bundle file name>"; lenient mode also
        // takes the other lists of that kind in a virtual bundle, in name order.
        IEnumerable<RawList> Single(RawBundle b, string kind)
        {
            var exact = kind + "_" + b.FileName.ToLowerInvariant();
            if (b.Lists.TryGetValue(exact, out var l)) yield return l;
            if (opt.Lenient && b.Virtual)
                foreach (var x in b.Ordered.Where(x => x.Prefix == kind && x.Lower != exact).OrderBy(x => x.Lower, StringComparer.Ordinal).ToList())
                {
                    Count($"lenient.singleListAccepted.{kind}", Math.Max(1, x.Files));
                    yield return x;
                }
        }

        // Multi-list kinds: unanchored regex on the lower-case name (+ the kind-prefix test), numbers
        // from the groups, ordered by the kind's sort key, ties by name.
        List<(RawList, int[])> Multi(RawBundle b, Regex re, string kind, int groups, bool prefixTest, Func<int[], (int, int, int)> sortKey)
        {
            var res = new List<(RawList, int[], (int, int, int))>();
            foreach (var l in b.Ordered)
            {
                if (prefixTest && l.Prefix != kind) continue;
                var m = re.Match(l.Lower);
                if (!m.Success) { if (l.Prefix == kind) { Count($"skip.listName.{kind}"); Diag($"list name does not match {re}: {b.Path}:{l.Name}"); } continue; }
                var n = new int[groups];
                bool ok = true;
                for (int k = 0; k < groups; k++)
                    if (!int.TryParse(m.Groups[k + 1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out n[k])) ok = false;
                if (!ok) { Count($"skip.listName.{kind}"); Diag($"list name numbers unparsable (file skipped): {b.Path}:{l.Name}"); continue; }
                res.Add((l, n, sortKey(n)));
            }
            return res.OrderBy(x => x.Item3).ThenBy(x => x.Item1.Lower, StringComparer.Ordinal).Select(x => (x.Item1, x.Item2)).ToList();
        }

        // Single-list suffix rule and unknown list names (diagnostics).
        void CheckNames(RawBundle b)
        {
            var fn = b.FileName.ToLowerInvariant();
            foreach (var l in b.Ordered)
            {
                if (Array.IndexOf(SinglePrefixes, l.Prefix) >= 0)
                {
                    if (l.Lower == l.Prefix + "_" + fn) continue;
                    Count("parity.singleListSuffixMismatch", Math.Max(1, l.Files));
                    Count($"parity.singleListSuffixMismatch.{l.Prefix}", Math.Max(1, l.Files));
                    var z = l.Rows.FirstOrDefault(r => r.Zip != null);
                    Diag($"single list not named after its folder ({(opt.Lenient ? "read: lenient" : "not read")}, {l.Files} file(s)): {b.Path}:{l.Name}" +
                         (z != null ? $" [{z.Zip.Guid} | {System.IO.Path.GetFileName(z.Zip.Path)}]" : ""));
                    if (l.Prefix == "itemgroup" && !opt.Lenient)
                        for (int k = Math.Min(1, l.Rows.Count); k < l.Rows.Count; k++)
                            if (TryInt(Cell(l.Rows[k].Cells, 1), out int g)) unreadModItemGroups.Add(g);
                }
                else if (Array.IndexOf(MultiPrefixes, l.Prefix) < 0)
                {
                    Count("skip.unknownListKind");
                    Diag($"unknown list kind ignored: {b.Path}:{l.Name}");
                }
            }
        }

        // Header rows to skip: a fixed count per kind (parity); a CSV row with an integer first cell
        // inside that range is data the game swallows (logged). Lenient: skip only non-integer rows.
        int HeaderSkip(RawBundle b, RawList l, int count, string kind)
        {
            if (!l.Csv) return Math.Min(count, l.Rows.Count);
            if (opt.Lenient)
            {
                int k = 0;
                while (k < l.Rows.Count && !TryInt(Cell(l.Rows[k].Cells, 0), out _)) k++;
                return k;
            }
            int n = Math.Min(count, l.Rows.Count);
            for (int k = 0; k < n; k++)
                if (TryInt(Cell(l.Rows[k].Cells, 0), out _) && l.Rows[k].Cells.Count(x => !string.IsNullOrWhiteSpace(x)) >= 2)
                {
                    Count("parity.csvDataRowReadAsHeader");
                    Count($"parity.csvDataRowReadAsHeader.{kind}");
                    Diag($"CSV data row swallowed as header ({kind} expects {count} header rows): {Loc(b, l, k, l.Rows[k])}: {string.Join(",", l.Rows[k].Cells.Take(6))}");
                }
            return n;
        }

        T Fill<T>(T r, RawBundle b, RawList l, int row, RawRow raw, int id) where T : StudioRecord
        {
            r.Key = new ContentId(Guid(raw), id);
            r.Zipmod = raw.Zip;
            r.ListName = l.Name;
            r.ListBundle = b.Path;
            r.Row = row;
            r.Order = seq++;
            return r;
        }

        // ---------------------------------------------------------------- kinds

        void ParseGroups(RawBundle b, RawList l, StudioListKind kind)
        {
            for (int i = HeaderSkip(b, l, 1, kind.ToString()); i < l.Rows.Count; i++)
            {
                var r = l.Rows[i]; var c = r.Cells;
                if (!TryInt(Cell(c, 0), out int sort) || !TryInt(Cell(c, 1), out int num))
                {
                    if (!Blank(c)) Bad(kind.ToString(), "nonInt", b, l, i, r);
                    continue;
                }
                var g = Guid(r);
                groupDefs.Add(new GroupDef { Kind = kind, Number = num, Sort = sort, Name = Str(c, 2), Guid = g, Order = seq++, Vanilla = r.Zip == null });
                definedGroups[kind].Add(num);
                if (r.Zip != null) Count($"mod.rowsRead.{kind}");
            }
        }

        void ParseCategories(RawBundle b, RawList l, StudioListKind kind, int group)
        {
            var groupKind = kind == StudioListKind.ItemCategory ? StudioListKind.ItemGroup : kind == StudioListKind.AnimeCategory ? StudioListKind.AnimeGroup : StudioListKind.VoiceGroup;
            bool dropped = !definedGroups[groupKind].Contains(group);
            if (dropped)
            {
                Count($"order.categoryFileBeforeGroup.{kind}");
                var z = l.Rows.FirstOrDefault(r => r.Zip != null);
                Diag($"{kind} file read before its group {group} exists (dropped by the game{(opt.EmulateLoadOrderDrops ? ", dropped" : ", kept")}): {b.Path}:{l.Name}" +
                     (z != null ? $" [{z.Zip.Guid}]" : ""));
            }
            for (int i = HeaderSkip(b, l, 1, kind.ToString()); i < l.Rows.Count; i++)
            {
                var r = l.Rows[i]; var c = r.Cells;
                if (!TryInt(Cell(c, 0), out int sort) || !TryInt(Cell(c, 1), out int num))
                {
                    if (!Blank(c)) Bad(kind.ToString(), "nonInt", b, l, i, r);
                    continue;
                }
                bool ai = kind == StudioListKind.AnimeCategory && group == 0 && num >= 1 && num <= 12;
                catDefs.Add(new CatDef { Kind = kind, Group = group, Number = num, Sort = sort, Name = Str(c, 2), Guid = Guid(r), Order = seq++, Vanilla = r.Zip == null, Ai = ai, Dropped = dropped });
                if (ai) Count($"gate.ai.{kind}{(r.Zip == null ? "" : ".mod")}");
                if (dropped) Count($"order.categoryRowsBeforeGroup.{kind}");
                if (r.Zip != null) Count($"mod.rowsRead.{kind}");
            }
        }

        // AI-Shoujo file-name gate for ItemList files.
        static bool ItemFileGated(int xx, int g, int c) =>
            (g == 1 && (c == 1 || c == 2 || c == 3 || c == 4 || c == 7)) || (g == 1 && (c == 5 || c == 6) && xx != 10) ||
            (g == 2 && xx != 13) || (g == 3 && c >= 15 && c <= 17) || (g == 9 && c == 38) || g == 4 || g == 12;

        void ParseItems(RawBundle b, RawList l, int xx, int fg, int fc)
        {
            if (xx == 50 && !db.IsInstallAdd) { Count("skip.ItemList.add50File"); Diag($"ItemList file needs add50: {b.Path}:{l.Name}"); return; }
            bool ai = ItemFileGated(xx, fg, fc);
            bool mod = l.Rows.Any(r => r.Zip != null);
            if (ai) Count(mod ? "gate.ai.ItemListFiles.mod" : "gate.ai.ItemListFiles");
            for (int i = HeaderSkip(b, l, 1, "ItemList"); i < l.Rows.Count; i++)
            {
                var r = l.Rows[i]; var c = r.Cells;
                if (!TryInt(Cell(c, 0), out int no))
                {
                    // The game stops reading the list at the first non-integer row.
                    int lost = 0;
                    for (int k = i + 1; k < l.Rows.Count; k++) if (TryInt(Cell(l.Rows[k].Cells, 0), out _)) lost++;
                    if (lost > 0)
                    {
                        Count("parity.itemListStoppedEarly"); Count("parity.itemListRowsLost", lost);
                        Diag($"ItemList stops at a non-integer row, {lost} later rows unread: {Loc(b, l, i, r)}: {string.Join(",", c.Take(4))}");
                    }
                    break;
                }
                Count(r.Zip != null ? "mod.itemRowsRead" : "vanilla.itemRowsRead");
                if (c.Length < 7) { Bad("ItemList", "short", b, l, i, r); continue; }
                if (!TryInt(c[1], out int g) || !TryInt(c[2], out int cat)) { Bad("ItemList", "nonIntGroupCategory", b, l, i, r); continue; }
                var it = Fill(new StudioItem
                {
                    Group = g, Category = cat, Name = c[3], Manifest = c[4], BundlePath = c[5], FileName = c[6],
                    ChildAttach = Str(c, 7), FileBundleNo = xx, FileGroup = fg, FileCategory = fc, AiContent = ai,
                }, b, l, i, r, no);
                if (ai) Count(r.Zip != null ? "gate.ai.ItemListRows.mod" : "gate.ai.ItemListRows");
                items.Add(it);
            }
        }

        void ParseItemBoneList(RawBundle b, RawList l, int g, int c)
        {
            for (int i = HeaderSkip(b, l, 1, "ItemBoneList"); i < l.Rows.Count; i++)
            {
                var r = l.Rows[i]; var cells = r.Cells;
                if (!TryInt(Cell(cells, 0), out int no)) { if (!Blank(cells)) Bad("ItemBoneList", "nonInt", b, l, i, r); continue; }
                var bl = Fill(new StudioItemBoneList { FileGroup = g, FileCategory = c, Bones = cells.Skip(1).Where(s => s.Length > 0).ToArray() }, b, l, i, r, no);
                boneListDefs.Add(bl);
                if (r.Zip != null) Count("mod.rowsRead.ItemBoneList");
            }
        }

        void ParseItemData(RawBundle b)
        {
            foreach (var (name, recs) in b.Colors)
            {
                // The game stops at the first ItemData asset without readable data (Odin payloads).
                if (recs == null) { Count("itemdata.assetsWithoutData"); Diag($"ItemData asset without readable colorDatas, rest of the bundle skipped: {b.Path}:{name}"); break; }
                if (recs.Count == 0) { Count("itemdata.assetsEmpty"); Diag($"ItemData asset with an empty colorDatas list: {b.Path}:{name}"); continue; }
                Count("itemdata.assetsRead");
                foreach (var r in recs) { colorDefs.Add(r); Count("itemdata.rows"); }
            }
        }

        void ParseLights(RawBundle b, RawList l)
        {
            for (int i = HeaderSkip(b, l, 1, "Light"); i < l.Rows.Count; i++)
            {
                var r = l.Rows[i]; var c = r.Cells;
                if (!TryInt(Cell(c, 0), out int no)) { if (!Blank(c)) Bad("Light", "nonInt", b, l, i, r); continue; }
                if (c.Length < 6) { Bad("Light", "short", b, l, i, r); continue; }
                if (!TryInt(c[5], out int target)) { Bad("Light", "nonIntTarget", b, l, i, r); continue; }
                lights.Add(Fill(new StudioLight { Name = c[1], Manifest = c[2], BundlePath = c[3], FileName = c[4], Target = target }, b, l, i, r, no));
                if (r.Zip != null) Count("mod.rowsRead.Light");
            }
        }

        void ParseAnime(RawBundle b, RawList l, int xx, bool h)
        {
            string kind = h ? "HAnime" : "Anime";
            if (xx == 50 && !db.IsInstallAdd) { Count($"skip.{kind}.add50File"); return; }
            for (int i = HeaderSkip(b, l, 2, kind); i < l.Rows.Count; i++)
            {
                var r = l.Rows[i]; var c = r.Cells;
                if (Blank(c)) continue;
                var bundle = Str(c, 5);
                if (!BundleExists(bundle)) { Bad(kind, "missingBundle", b, l, i, r); continue; }
                if (!TryInt(Cell(c, 0), out int sort) || !TryInt(Cell(c, 1), out int no)) { Bad(kind, "nonInt", b, l, i, r); continue; }
                if (!TryInt(Cell(c, 2), out int g) || !TryInt(Cell(c, 3), out int cat)) { Bad(kind, "nonIntGroupCategory", b, l, i, r); continue; }
                var a = new StudioAnime { IsH = h, Sort = sort, Group = g, Category = cat, Name = Str(c, 4), BundlePath = bundle, FileName = Str(c, 6), FileBundleNo = xx };
                int optStart, block;
                if (!h)
                {
                    a.Clip = Str(c, 7);
                    // AI gate on row columns: group 0 keeps only 0/0/0.
                    a.AiContent = g == 0 && ((cat == 0 && no != 0) || (cat >= 1 && cat <= 12));
                    optStart = 8; block = 10;
                }
                else
                {
                    a.OverrideBundle = Str(c, 7); a.OverrideFile = Str(c, 8); a.Clip = Str(c, 9);
                    TryInt(Cell(c, 10), out a.BreastLayer);
                    a.YureBundle = Str(c, 11); a.YureFile = Str(c, 12);
                    TryInt(Cell(c, 13), out a.MotionId);
                    TryInt(Cell(c, 14), out a.YureWho);
                    if (!bool.TryParse(Cell(c, 15), out a.IsMotion)) { Bad(kind, "isMotionNotBool", b, l, i, r); continue; }
                    for (int k = 0; k < 8; k++) a.PvFlags[k] = !bool.TryParse(Cell(c, 16 + k), out bool pv) || pv;
                    optStart = 24; block = 9;
                }
                for (int s = optStart; ; s += block)
                {
                    string ob = Cell(c, s), oa = Cell(c, s + 1), om = Cell(c, s + 2);
                    if (string.IsNullOrEmpty(ob) || string.IsNullOrEmpty(oa) || string.IsNullOrEmpty(om)) break;
                    var o = new StudioAnimeOption
                    {
                        Bundle = ob, Asset = oa, Manifest = om, ControllerBundle = Str(c, s + 3), ControllerAsset = Str(c, s + 4),
                        OverrideBundle = Str(c, s + 5), OverrideAsset = Str(c, s + 6), Parentage = Str(c, s + 7),
                    };
                    bool.TryParse(Cell(c, s + 8), out o.CounterScale);
                    if (!h) o.IsAnimeSync = !bool.TryParse(Cell(c, s + 9), out bool sync) || sync;
                    a.Options.Add(o);
                }
                anime.Add(Fill(a, b, l, i, r, no));
                if (a.AiContent) Count(r.Zip != null ? "gate.ai.AnimeRows.mod" : "gate.ai.AnimeRows");
                if (r.Zip != null) Count($"mod.rowsRead.{kind}");
            }
        }

        void ParseVoices(RawBundle b, RawList l)
        {
            for (int i = HeaderSkip(b, l, 2, "Voice"); i < l.Rows.Count; i++)
            {
                var r = l.Rows[i]; var c = r.Cells;
                if (!TryInt(Cell(c, 0), out int no) || !TryInt(Cell(c, 1), out int g) || !TryInt(Cell(c, 2), out int cat))
                {
                    if (!Blank(c)) Bad("Voice", "nonInt", b, l, i, r);
                    continue;
                }
                voices.Add(Fill(new StudioVoice { Group = g, Category = cat, Name = Str(c, 3), BundlePath = Str(c, 4), FileName = Str(c, 5) }, b, l, i, r, no));
                if (r.Zip != null) Count("mod.rowsRead.Voice");
            }
        }

        void ParseSimple<T>(RawBundle b, RawList l, List<T> into, string kind) where T : StudioSimpleAsset, new()
        {
            for (int i = HeaderSkip(b, l, 1, kind); i < l.Rows.Count; i++)
            {
                var r = l.Rows[i]; var c = r.Cells;
                if (!TryInt(Cell(c, 0), out int no)) { if (!Blank(c)) Bad(kind, "nonInt", b, l, i, r); continue; }
                var rec = Fill(new T { Name = Str(c, 1), BundlePath = Str(c, 2), FileName = Str(c, 3) }, b, l, i, r, no);
                // BGM gate: vanilla ids < 100 need AI (mod ids are renumbered by Sideloader, never gated).
                if (kind == "Bgm" && r.Zip == null && no < 100) { rec.AiContent = true; Count("gate.ai.Bgm"); }
                into.Add(rec);
                if (r.Zip != null) Count($"mod.rowsRead.{kind}");
            }
        }

        void ParseMaps(RawBundle b, RawList l)
        {
            for (int i = HeaderSkip(b, l, 2, "Map"); i < l.Rows.Count; i++)
            {
                var r = l.Rows[i]; var c = r.Cells;
                if (!TryInt(Cell(c, 0), out int no)) { if (!Blank(c)) Bad("Map", "nonInt", b, l, i, r); continue; }
                if (c.Length < 4) { Bad("Map", "short", b, l, i, r); continue; }
                var m = Fill(new StudioMap { Name = c[1], BundlePath = c[2], FileName = c[3], Manifest = Str(c, 4), VanishBundle = Str(c, 5), VanishFile = Str(c, 6) }, b, l, i, r, no);
                // Map gate: vanilla ids 0 and 1 (unsigned comparison: negative ids pass).
                if (r.Zip == null && (uint)no < 2) { m.AiContent = true; Count("gate.ai.Map"); }
                maps.Add(m);
                if (r.Zip != null) Count("mod.rowsRead.Map");
            }
        }

        void ParseSkies(RawBundle b, RawList l)
        {
            for (int i = HeaderSkip(b, l, 2, "StudioSky"); i < l.Rows.Count; i++)
            {
                var r = l.Rows[i]; var c = r.Cells;
                if (!TryInt(Cell(c, 0), out int no)) { if (!Blank(c)) Bad("StudioSky", "nonInt", b, l, i, r); continue; }
                if (c.Length < 2) { Bad("StudioSky", "short", b, l, i, r); continue; }
                float F(int k) => float.TryParse(Cell(c, k), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
                skies.Add(Fill(new StudioSky { Name = c[1], SunRotation = new Vector3(F(2), F(3), F(4)), BundlePath = Str(c, 5), FileName = Str(c, 6), Manifest = Str(c, 7) }, b, l, i, r, no));
            }
        }

        void ParseBones(RawBundle b, RawList l)
        {
            for (int i = HeaderSkip(b, l, 1, "Bone"); i < l.Rows.Count; i++)
            {
                var r = l.Rows[i]; var c = r.Cells;
                if (!TryInt(Cell(c, 0), out int no)) { if (!Blank(c)) Bad("Bone", "nonInt", b, l, i, r); continue; }
                var t = Str(c, 1);
                if (t.Length == 0) { Bad("Bone", "noTransform", b, l, i, r); continue; }
                var bone = new StudioBone { TransformName = t, Name = Str(c, 2) };
                TryInt(Cell(c, 3), out bone.Group);
                TryInt(Cell(c, 4), out bone.Level);
                if (!TryInt(Cell(c, 5), out bone.SyncNo)) bone.SyncNo = -1;
                bones.Add(Fill(bone, b, l, i, r, no));
                if (r.Zip != null) Count("mod.rowsRead.Bone");
            }
        }

        void ParseAccessoryPointGroups(RawBundle b, RawList l)
        {
            for (int i = HeaderSkip(b, l, 1, "AccessoryPointGroup"); i < l.Rows.Count; i++)
            {
                var r = l.Rows[i]; var c = r.Cells;
                if (!TryInt(Cell(c, 0), out int no)) { if (!Blank(c)) Bad("AccessoryPointGroup", "nonInt", b, l, i, r); continue; }
                var name = Str(c, 1);
                if (name.Length == 0) { Bad("AccessoryPointGroup", "noName", b, l, i, r); continue; }
                var g = new StudioAccessoryPointGroup { Name = name, Range = Str(c, 2) };
                var parts = g.Range.Split('-');
                if (parts.Length == 2 && TryInt(parts[0], out int a) && TryInt(parts[1], out int z)) { g.First = a; g.Last = z; }
                apgs.Add(Fill(g, b, l, i, r, no));
            }
        }

        // ================================================================ phase 2

        // Groups definitions by key. The "preferred" definitions of a key are the non-gated ones when
        // the AI gate is on and there are any, else all of them. Winner = the last preferred
        // definition, display position = the first preferred one. Result ordered by that position.
        List<(TKey key, List<T> pref, List<T> other)> ByKey<TKey, T>(IEnumerable<T> defs, Func<T, TKey> key, Func<T, bool> isAi, Func<T, long> order)
        {
            var map = new Dictionary<TKey, (List<T> n, List<T> a)>();
            var keys = new List<TKey>();
            foreach (var d in defs)
            {
                var k = key(d);
                if (!map.TryGetValue(k, out var e)) { map[k] = e = (new List<T>(), new List<T>()); keys.Add(k); }
                (gate && isAi(d) ? e.a : e.n).Add(d);
            }
            var res = new List<(TKey, List<T>, List<T>)>(keys.Count);
            foreach (var k in keys)
            {
                var (n, a) = map[k];
                res.Add(n.Count > 0 ? (k, n, a) : (k, a, n));
            }
            return res.OrderBy(x => order(x.Item2[0])).ToList();
        }

        void Shadow(StudioRecord r) { r.Shadowed = true; db.shadowed.Add(r); }

        void Resolve()
        {
            var sw = Stopwatch.StartNew();
            void Lap(string name) { db.TimingsMs["resolve." + name] = sw.ElapsedMilliseconds; sw.Restart(); }
            ResolveGroups(db.itemSpace, StudioListKind.ItemGroup, StudioListKind.ItemCategory);
            ResolveGroups(db.animeSpace, StudioListKind.AnimeGroup, StudioListKind.AnimeCategory);
            ResolveGroups(db.voiceSpace, StudioListKind.VoiceGroup, StudioListKind.VoiceCategory);
            Lap("groups");

            ResolveRenumbered(items, db.Items, "ItemList");
            foreach (var it in db.Items.All)
            {
                var k = (it.Key, it.Group, it.Category);
                if (!db.itemsByFullKey.TryGetValue(k, out var ex)) db.itemsByFullKey[k] = it;
                else if (gate && ex.AiContent && !it.AiContent) db.itemsByFullKey[k] = it;
            }
            ItemStats();
            AttachBoneLists();
            AttachColors();
            Lap("items");

            ResolveGlobalKeyed(anime, db.Anime, db.animeByFullKey, db.animeGlobal, a => a.Group, a => a.Category, "Anime",
                (a, w, p) => { a.GlobalWinner = w; a.GlobalPosition = p; });
            ResolveGlobalKeyed(voices, db.Voices, db.voiceByFullKey, db.voiceGlobal, v => v.Group, v => v.Category, "Voice",
                (v, w, p) => { v.GlobalWinner = w; v.GlobalPosition = p; });
            AnimeStats();
            Lap("anime");

            ResolveRenumbered(maps, db.Maps, "Map");
            ResolveRenumbered(lights, db.Lights, "Light");
            ResolveRenumbered(bgms, db.Bgm, "Bgm");
            ResolveRenumbered(filters, db.Filters, "Filter");
            ResolveRenumbered(probes, db.Probes, "Probe");
            ResolveRenumbered(skies, db.Skies, "StudioSky");
            ResolveRenumbered(apgs, db.AccessoryPointGroups, "AccessoryPointGroup");
            // Bone tables are not renumbered by Sideloader: last wins per (GUID, no) and globally per no.
            foreach (var (key, pref, other) in ByKey(bones, x => x.Key, x => x.AiContent, x => x.Order))
            {
                var w = pref[pref.Count - 1];
                w.Position = pref[0].Order;
                foreach (var x in pref.Concat(other)) if (x != w) Shadow(x);
                if (pref.Count > 1 && w.IsVanilla) db.Count("redef.Bone.vanilla", pref.Count - 1);
                db.Bones.all.Add(w); db.Bones.byId[key] = w;
            }
            foreach (var bn in db.Bones.all.OrderBy(x => x.Order)) db.boneGlobal[bn.No] = bn;

            Lap("other");
            ComputeVisibility();
            Lap("visibility");
        }

        void ResolveGroups(GroupSpace space, StudioListKind groupKind, StudioListKind catKind)
        {
            var modNames = new Dictionary<int, HashSet<string>>();
            var declared = new HashSet<(int, string)>();
            foreach (var d in groupDefs.Where(x => x.Kind == groupKind))
            {
                if (!space.Groups.TryGetValue(d.Number, out var g))
                    space.Groups[d.Number] = g = new StudioGroup { Kind = groupKind, Number = d.Number, Position = d.Order };
                else if (d.Vanilla && declared.Contains((d.Number, ContentIds.Vanilla))) Count($"redef.{groupKind}.vanilla");
                g.Sort = d.Sort; g.Name = d.Name;
                if (declared.Add((d.Number, d.Guid.ToLowerInvariant()))) g.DeclaredBy.Add(d.Guid);
                if (!g.Names.Contains(d.Name)) g.Names.Add(d.Name);
                if (!d.Vanilla) { if (!modNames.TryGetValue(d.Number, out var s)) modNames[d.Number] = s = new HashSet<string>(StringComparer.Ordinal); s.Add(d.Name); }
            }
            space.Ordered.AddRange(space.Groups.Values.OrderBy(g => g.Sort).ThenBy(g => g.Position));
            Count($"mod.distinct.{groupKind}", modNames.Count);
            Count($"mod.sharedByGuids.{groupKind}", space.Groups.Values.Count(g => modNames.ContainsKey(g.Number) && g.DeclaredBy.Count(x => !x.Equals(ContentIds.Vanilla, StringComparison.OrdinalIgnoreCase)) > 1));
            foreach (var g in space.Groups.Values.Where(g => g.Names.Count > 1).OrderBy(g => g.Number))
            {
                Count($"conflict.names.{groupKind}");
                if (modNames.TryGetValue(g.Number, out var s) && s.Count > 1) Count($"conflict.names.{groupKind}.mod");
                Diag($"{groupKind} {g.Number} has differing names: {string.Join(" | ", g.Names)} (declared by {string.Join(", ", g.DeclaredBy.Take(6))}{(g.DeclaredBy.Count > 6 ? ", ..." : "")}); \"{g.Name}\" wins");
            }
            if (groupKind == StudioListKind.ItemGroup)
            {
                Count("mod.distinct.ItemGroup.inclUnreadCsvs", modNames.Keys.Concat(unreadModItemGroups).Distinct().Count());
                Count("mod.ItemGroupOnlyInUnreadCsvs", unreadModItemGroups.Count(n => !space.Groups.ContainsKey(n)));
            }

            // Categories: global (group, number), last wins; attached after everything is read.
            var later = catDefs.Where(x => x.Kind == catKind && x.Dropped && space.Groups.ContainsKey(x.Group)).ToList();
            Count($"order.categoryRowsGroupDefinedLater.{catKind}", later.Count);
            Count($"order.categoryGroupsDefinedLater.{catKind}", later.Select(x => x.Group).Distinct().Count());
            var defs = catDefs.Where(x => x.Kind == catKind);
            if (opt.EmulateLoadOrderDrops) defs = defs.Where(x => { if (x.Dropped) Count($"order.categoryDropped.{catKind}"); return !x.Dropped; });
            var modCatNames = new Dictionary<(int, int), HashSet<string>>();
            foreach (var (key, pref, other) in ByKey(defs.ToList(), x => (x.Group, x.Number), x => x.Ai, x => x.Order))
            {
                var w = pref[pref.Count - 1];
                var c = new StudioCategory
                {
                    Kind = catKind, Group = key.Item1, Number = key.Item2, Sort = w.Sort, Name = w.Name, Position = pref[0].Order,
                    AiContent = gate && w.Ai, DroppedInGame = pref.Concat(other).All(x => x.Dropped),
                };
                var catDeclared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var d in pref.Concat(other).OrderBy(x => x.Order))
                {
                    if (catDeclared.Add(d.Guid)) c.DeclaredBy.Add(d.Guid);
                    if (!c.Names.Contains(d.Name)) c.Names.Add(d.Name);
                    if (!d.Vanilla) { if (!modCatNames.TryGetValue(key, out var s)) modCatNames[key] = s = new HashSet<string>(StringComparer.Ordinal); s.Add(d.Name); }
                }
                int vp = pref.Count(x => x.Vanilla);
                if (vp > 1) Count($"redef.{catKind}.vanilla", vp - 1);
                space.Categories[key] = c;
                c.HasGroup = space.Groups.TryGetValue(c.Group, out var grp);
                if (c.DroppedInGame && c.HasGroup)
                {
                    Count($"order.categoryInvisibleInGame.{catKind}");
                    Diag($"{catKind} {c.Group}/{c.Number} \"{c.Name}\" is defined only before its group: invisible in the game, attached here");
                }
                if (c.HasGroup) grp.Categories.Add(c);
                else { Count($"orphan.{catKind}"); Diag($"{catKind} {c.Group}/{c.Number} \"{c.Name}\": group {c.Group} is defined nowhere"); }
            }
            foreach (var g in space.Groups.Values)
            {
                var sorted = g.Categories.OrderBy(c => c.Sort).ThenBy(c => c.Position).ToList();
                g.Categories.Clear(); g.Categories.AddRange(sorted);
            }
            Count($"mod.distinct.{catKind}", modCatNames.Count);
            Count($"mod.sharedByGuids.{catKind}", space.Categories.Values.Count(c => modCatNames.ContainsKey((c.Group, c.Number)) && c.DeclaredBy.Count(x => !x.Equals(ContentIds.Vanilla, StringComparison.OrdinalIgnoreCase)) > 1));
            foreach (var c in space.Categories.Values.Where(c => c.Names.Count > 1).OrderBy(c => c.Group).ThenBy(c => c.Number))
            {
                Count($"conflict.names.{catKind}");
                if (modCatNames.TryGetValue((c.Group, c.Number), out var s) && s.Count > 1) Count($"conflict.names.{catKind}.mod");
                Diag($"{catKind} {c.Group}/{c.Number} has differing names: {string.Join(" | ", c.Names)} (declared by {string.Join(", ", c.DeclaredBy.Take(6))}{(c.DeclaredBy.Count > 6 ? ", ..." : "")}); \"{c.Name}\" wins");
            }
        }

        // Kinds whose mod ids Sideloader renumbers: vanilla rows are last-wins per id;
        // every mod row stays its own record (as in the game), the (GUID, id) index takes the first.
        void ResolveRenumbered<T>(List<T> defs, StudioTable<T> table, string kind) where T : StudioRecord
        {
            var winners = new List<T>();
            foreach (var (key, pref, other) in ByKey(defs.Where(d => d.IsVanilla), d => d.Key, d => d.AiContent, d => d.Order))
            {
                var w = pref[pref.Count - 1];
                w.Position = pref[0].Order;
                foreach (var x in pref.Concat(other)) if (x != w) Shadow(x);
                if (pref.Count > 1) Count($"redef.{kind}.vanilla", pref.Count - 1);
                if (w is StudioItem wi)
                    foreach (var x in pref)
                        if (x is StudioItem xi && (xi.Group != wi.Group || xi.Category != wi.Category))
                            Diag($"vanilla item {w.Key} redefined with another group/category: {xi.Group}/{xi.Category} -> {wi.Group}/{wi.Category}");
                winners.Add(w);
                table.byId[key] = w;
            }
            var dups = new Dictionary<ContentId, List<T>>();
            foreach (var d in defs.Where(d => !d.IsVanilla))
            {
                d.Position = d.Order;
                winners.Add(d);
                if (!table.byId.TryGetValue(d.Key, out var ex)) { table.byId[d.Key] = d; continue; }
                if (!dups.TryGetValue(d.Key, out var l)) dups[d.Key] = l = new List<T> { ex };
                l.Add(d);
                if (gate && ex.AiContent && !d.AiContent) table.byId[d.Key] = d;
            }
            if (dups.Count > 0)
            {
                Count($"dup.{kind}.modIds", dups.Count);
                Count($"dup.{kind}.modRows", dups.Values.Sum(l => l.Count));
                foreach (var kv in dups)
                    Diag($"duplicate {kind} id inside one mod: {kv.Key} x{kv.Value.Count}; the first row is the (GUID, id) target: " +
                         string.Join("; ", kv.Value.Select(x => $"{x.ListBundle}:{x.ListName}#{x.Row}")));
            }
            table.all.AddRange(winners.OrderBy(w => w.Position));
        }

        // Anime/HAnime and voices: per-GUID last wins on (GUID, group, category, no); then a global
        // index on the raw (group, category, no) whose winner is the last definition overall. The table
        // ends in display order: by the first insertion of the raw key over all GUIDs (a mod row that
        // repeats a base-game key keeps the place of the key's first definition, as the lists show it).
        void ResolveGlobalKeyed<T>(List<T> defs, StudioTable<T> table, Dictionary<(ContentId, int, int), T> full,
            Dictionary<(int, int, int), List<T>> global, Func<T, int> grp, Func<T, int> cat, string kind, Action<T, bool, long> setGlobal) where T : StudioRecord
        {
            foreach (var (key, pref, other) in ByKey(defs, d => (d.Key, grp(d), cat(d)), d => d.AiContent, d => d.Order))
            {
                var w = pref[pref.Count - 1];
                w.Position = pref[0].Order;
                foreach (var x in pref.Concat(other)) if (x != w) Shadow(x);
                if (pref.Count > 1)
                {
                    string k = w is StudioAnime wa ? (wa.IsH ? "HAnime" : "Anime") : kind;
                    Count(w.IsVanilla ? $"redef.{k}.vanilla" : $"redef.{kind}.mod", pref.Count - 1);
                    if (!w.IsVanilla) Diag($"{kind} {w.Guid} {key.Item2}/{key.Item3}/{w.Key.Id} defined {pref.Count} times in one mod; the last wins: {w.ListBundle}:{w.ListName}#{w.Row}");
                }
                table.all.Add(w);
                full[key] = w;
            }
            var displayPos = new Dictionary<T, long>();
            foreach (var (key, pref, other) in ByKey(table.all, d => (grp(d), cat(d), d.Key.Id), d => d.AiContent, d => d.Position))
            {
                if (pref.Count == 1 && other.Count == 0)
                {
                    setGlobal(pref[0], true, pref[0].Position);
                    displayPos[pref[0]] = pref[0].Position;
                    global[key] = pref;
                    continue;
                }
                var w = pref.OrderBy(x => x.Order).Last();
                long pos = pref.Min(x => x.Position);
                var list = new List<T> { w };
                list.AddRange(pref.Concat(other).Where(x => x != w).OrderBy(x => x.Order));
                foreach (var x in list) { setGlobal(x, x == w, pos); displayPos[x] = pos; }
                global[key] = list;
                if (list.Count > 1)
                {
                    Count($"global.{kind}.contestedKeys");
                    Diag($"{kind} key {key.Item1}/{key.Item2}/{key.Item3} defined by {list.Count} GUIDs; global winner {w.Guid} ({w.ListBundle}:{w.ListName}#{w.Row}); others: " +
                         string.Join(", ", list.Skip(1).Select(x => x.Guid)));
                }
            }
            var display = table.all.OrderBy(x => displayPos.TryGetValue(x, out var p) ? p : x.Position).ThenBy(x => x.Position).ToList();
            table.all.Clear();
            table.all.AddRange(display);
        }

        void ItemStats()
        {
            // Duplicates inside one mod on (GUID, group, category, id).
            var full = items.Where(i => !i.IsVanilla).GroupBy(i => (i.Key, i.Group, i.Category)).Where(g => g.Count() > 1).ToList();
            Count("dup.ItemList.modFullKeys", full.Count);
            Count("dup.ItemList.modFullKeyRows", full.Sum(g => g.Count()));
            var vanillaGroups = new HashSet<int>(groupDefs.Where(d => d.Vanilla && d.Kind == StudioListKind.ItemGroup).Select(d => d.Number));
            Count("mod.items.intoVanillaGroups", items.Count(i => !i.IsVanilla && vanillaGroups.Contains(i.Group)));
        }

        void AnimeStats()
        {
            int modKeys = 0, withVanilla = 0, replacing = 0, contestedMods = 0;
            foreach (var kv in db.animeGlobal)
            {
                var l = kv.Value;
                var modGuids = l.Where(x => !x.IsVanilla).Select(x => x.Guid).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                if (modGuids == 0) continue;
                modKeys++;
                bool v = l.Any(x => x.IsVanilla);
                if (v) withVanilla++;
                if (v && !l[0].IsVanilla) replacing++;
                if (modGuids > 1) contestedMods++;
            }
            Count("global.Anime.modKeys", modKeys);
            Count("global.Anime.modKeysCollidingWithVanilla", withVanilla);
            Count("global.Anime.modKeysReplacingVanilla", replacing);
            Count("global.Anime.keysContestedByMods", contestedMods);
            Count("anime.withOptions.vanilla", db.Anime.All.Count(a => a.IsVanilla && !a.IsH && a.GlobalWinner && !(gate && a.AiContent) && a.Options.Count > 0));
            Count("hanime.withOptions.vanilla", db.Anime.All.Count(a => a.IsVanilla && a.IsH && a.GlobalWinner && a.Options.Count > 0));
        }

        // Bone lists attach to the item (GUID, id) of the same GUID — the first row in load order, as Sideloader's id
        // resolution — and only when that item lies in the (group, category) named by the list's FILE NAME (HS2's
        // Info.LoadItemBoneInfo looks the row up in that group / category only; verified in HS2 Studio for the 30 mod
        // rows whose file name names another group / category: no FK). Later rows replace
        // earlier lists.
        void AttachBoneLists()
        {
            var attached = new Dictionary<StudioItem, StudioItemBoneList>();
            foreach (var bl in boneListDefs)
            {
                if (!db.Items.TryGet(bl.Key, out var it))
                {
                    Count(bl.IsVanilla ? "itembonelist.noItem.vanilla" : "itembonelist.noItem.mod");
                    Diag($"ItemBoneList row without an item of its own GUID (dropped): {bl.Key} [{bl.ListBundle}:{bl.ListName}#{bl.Row}]");
                    continue;
                }
                if (it.Group != bl.FileGroup || it.Category != bl.FileCategory)
                {
                    Count(bl.IsVanilla ? "itembonelist.fileGroupCategoryDiffers.vanilla" : "itembonelist.fileGroupCategoryDiffers.mod");
                    Diag($"ItemBoneList {bl.ListBundle}:{bl.ListName}#{bl.Row} names {bl.FileGroup}/{bl.FileCategory} but item {it.Key} is {it.Group}/{it.Category} (dropped, as HS2)");
                    continue;
                }
                if (attached.TryGetValue(it, out var prev)) { Count(bl.IsVanilla ? "redef.ItemBoneList.vanilla" : "redef.ItemBoneList.mod"); Shadow(prev); db.boneLists.Remove(prev); }
                attached[it] = bl;
                it.Bones = bl.Bones;
                db.boneLists.Add(bl);
            }
        }

        void AttachColors()
        {
            foreach (var c in colorDefs)
            {
                var k = (c.Group, c.Category, c.Id);
                if (db.itemColors.ContainsKey(k)) Count("redef.ItemData.vanilla");
                db.itemColors[k] = c;
            }
            foreach (var it in db.Items.All)
                if (it.IsVanilla && db.itemColors.TryGetValue((it.Group, it.Category, it.No), out var c)) it.Color = c;
        }

        void ComputeVisibility()
        {
            // Items: not gated, group and category known.
            foreach (var it in db.Items.All)
            {
                var k = (it.Group, it.Category);
                db.itemSpace.Categories.TryGetValue(k, out var cat);
                string why = null;
                if (gate && it.AiContent) why = "ai";
                else if (!db.itemSpace.Groups.ContainsKey(it.Group)) why = "noGroup";
                else if (cat == null || !cat.HasGroup) why = "noCategory";
                else if (gate && cat.AiContent) why = "aiCategory";
                it.Visible = why == null;
                if (why != null)
                {
                    Count($"hidden.Item.{why}{(it.IsVanilla ? ".vanilla" : ".mod")}");
                    if (why == "noGroup" || why == "noCategory") Diag($"item invisible ({why} {it.Group}/{it.Category}): {it.Key} \"{it.Name}\" [{it.ListBundle}:{it.ListName}#{it.Row}]");
                }
                if (cat != null)
                {
                    if (!db.itemsByCategory.TryGetValue(k, out var l)) db.itemsByCategory[k] = l = new List<StudioItem>();
                    l.Add(it);
                }
            }
            MarkSpace(db.itemSpace, k => db.itemsByCategory.TryGetValue(k, out var l) && l.Any(x => x.Visible));

            foreach (var a in db.Anime.All.Where(x => x.GlobalWinner).OrderBy(x => x.GlobalPosition))
            {
                var k = (a.Group, a.Category);
                db.animeSpace.Categories.TryGetValue(k, out var cat);
                a.Visible = !(gate && a.AiContent) && cat != null && cat.HasGroup && !(gate && cat.AiContent);
                if (cat != null)
                {
                    if (!db.animeByCategory.TryGetValue(k, out var l)) db.animeByCategory[k] = l = new List<StudioAnime>();
                    l.Add(a);
                }
            }
            MarkSpace(db.animeSpace, k => db.animeByCategory.TryGetValue(k, out var l) && l.Any(x => x.Visible));

            foreach (var v in db.Voices.All.Where(x => x.GlobalWinner).OrderBy(x => x.GlobalPosition))
            {
                var k = (v.Group, v.Category);
                db.voiceSpace.Categories.TryGetValue(k, out var cat);
                v.Visible = !(gate && v.AiContent) && cat != null && cat.HasGroup;
                if (cat != null)
                {
                    if (!db.voiceByCategory.TryGetValue(k, out var l)) db.voiceByCategory[k] = l = new List<StudioVoice>();
                    l.Add(v);
                }
            }
            MarkSpace(db.voiceSpace, k => db.voiceByCategory.TryGetValue(k, out var l) && l.Any(x => x.Visible));

            void Simple<T>(StudioTable<T> t) where T : StudioRecord { foreach (var r in t.all) r.Visible = !(gate && r.AiContent); }
            Simple(db.Maps); Simple(db.Lights); Simple(db.Bgm); Simple(db.Filters); Simple(db.Probes); Simple(db.Skies); Simple(db.Bones); Simple(db.AccessoryPointGroups);
        }

        // ================================================================ accessories as items

        /// <summary>The group's name as the loader HS2Next imitates lists it (data; the item browser shows a localized label).</summary>
        internal const string AccessoryGroupName = "アクセサリー";

        // After the lists are resolved: item group 647158 (sort 0: second in the group list, after the vanilla group 0 that
        // also sorts 0) with one category per accessory kind 351..363 (sort = the category number, named as the character
        // list categories), rows from the character accessory lists (the catalog's rows: the base game's rows as the list holds
        // them, the Possess column ignored, then the mods' rows by GUID in the loader's order, each mod's rows in file order).
        void AppendAccessoryItems()
        {
            if (!opt.AccessoryItems) { Count("accessoryItems.off"); return; }
            var space = db.itemSpace;
            const int g = StudioDb.AccessoryGroup;
            if (space.Groups.ContainsKey(g))
            {
                Count("accessoryItems.groupNumberTaken");
                Diag($"item group {g} is defined by the lists; the accessory group is not added");
                return;
            }
            var group = new StudioGroup { Kind = StudioListKind.ItemGroup, Number = g, Sort = 0, Name = AccessoryGroupName, Position = seq++ };
            group.Names.Add(group.Name);
            for (int cat = StudioDb.AccessoryFirstCategory; cat <= StudioDb.AccessoryLastCategory; cat++)
            {
                var c = new StudioCategory
                {
                    Kind = StudioListKind.ItemCategory, Group = g, Number = cat, Sort = cat, Name = ListCategories.NameOf(cat) ?? cat.ToString(CultureInfo.InvariantCulture),
                    Position = seq++, HasGroup = true,
                };
                c.Names.Add(c.Name);
                var rows = new List<StudioItem>();
                var mods = new List<(ListEntry e, int index)>();
                int index = 0;
                foreach (var e in catalog.EntriesOf(cat))
                {
                    if (e.IsVanilla) AddAccessoryRow(e, cat, index++, rows);
                    else mods.Add((e, index++));
                }
                mods.Sort((a, b) => { int k = HS2Next.Content.Maker.MakerLists.CompareGuids(a.e.Key.SourceGuid, b.e.Key.SourceGuid); return k != 0 ? k : a.index.CompareTo(b.index); });
                foreach (var (e, i) in mods) AddAccessoryRow(e, cat, i, rows);
                db.itemsByCategory[(g, cat)] = rows;
                c.Visible = rows.Count > 0;
                space.Categories[(g, cat)] = c;
                group.Categories.Add(c);
                Count("accessoryItems.rows", rows.Count);
            }
            group.Visible = group.Categories.Exists(x => x.Visible);
            space.Groups[g] = group;
            var ordered = space.Ordered.Concat(new[] { group }).OrderBy(x => x.Sort).ThenBy(x => x.Position).ToList();
            space.Ordered.Clear();
            space.Ordered.AddRange(ordered);
        }

        void AddAccessoryRow(ListEntry e, int cat, int index, List<StudioItem> rows)
        {
            string bundle = e["MainAB"] ?? "", asset = e["MainData"] ?? "";
            // listed like every other row (as HS2 lists it); such an item builds the fallback item
            if (bundle.Length == 0 || bundle == "0" || asset.Length == 0 || asset == "0")
            {
                Count("accessoryItems.noPrefab");
                Diag($"accessory {cat} {e.Key} \"{e["Name"]}\" names no prefab: listed, it builds the fallback item");
            }
            var key = (cat, e.Key);
            if (db.accessoryByKey.ContainsKey(key)) { Count("accessoryItems.duplicateKey"); return; }
            var it = new StudioItem
            {
                Key = e.Key, Group = StudioDb.AccessoryGroup, Category = cat, Name = e["Name"] ?? "", BundlePath = bundle, FileName = asset,
                Manifest = e["MainManifest"] ?? "", ListName = ListCategories.NameOf(cat), ListBundle = "", Row = index,
                Order = seq, Position = seq, Zipmod = e.Zipmod,
            };
            seq++;
            it.Visible = true;
            db.accessoryByKey[key] = it;
            db.accessoryAll.Add(it);
            rows.Add(it);
        }

        static void MarkSpace(GroupSpace s, Func<(int, int), bool> hasVisible)
        {
            foreach (var c in s.Categories.Values) c.Visible = c.HasGroup && hasVisible((c.Group, c.Number));
            foreach (var g in s.Groups.Values) g.Visible = g.Categories.Any(c => c.Visible);
        }
    }
}
