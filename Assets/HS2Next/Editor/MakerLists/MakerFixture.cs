// Row source of the plain-Mono maker suite: the zipmod rows of the zipmod scan
// cache (`Cache/modlists-<source>.tsv`, read with the catalog's rules: first definition of a (category, id) wins inside a
// zipmod, a later zipmod's row replaces an earlier one's, rows of the reserved vanilla namespace ignored) and the vanilla
// rows of the list fixture `Cache/maker/vanilla_lists.tsv` (written from the catalog by MakerListsDump in Unity; gated rows
// included and marked). The manifest infos come through MakerInfoCache (`Cache/makerinfo-<source>.tsv`). Unity-free.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace HS2Next.Content.Maker.Tests
{
    public sealed class FixtureListRows : IListRows
    {
        public const string VanillaHeader = "#hs2next-maker-vanilla\t1";

        sealed class Cat
        {
            public readonly Dictionary<ContentId, (ListRow row, int zip, long seq)> ByKey = new Dictionary<ContentId, (ListRow, int, long)>();
            public readonly Dictionary<ContentId, List<ListRow>> Defs = new Dictionary<ContentId, List<ListRow>>();
            public List<ListRow> Ordered;
        }

        readonly Dictionary<int, Cat> cats = new Dictionary<int, Cat>();
        long seq;

        /// <summary>Raw zipmod rows of the cache (before de-duplication), as the census counts them: (zip path, guid, category, id, columns).</summary>
        public readonly List<(string zip, string guid, int category, int id, Dictionary<string, string> values)> RawModRows = new List<(string, string, int, int, Dictionary<string, string>)>();
        /// <summary>Zipmods in scan order with their GUID (null: no GUID).</summary>
        public readonly List<(string path, string guid)> Zipmods = new List<(string, string)>();
        /// <summary>Preset ids of sampled heads from the fixture's P lines: (head category, head key) → member → local id.</summary>
        public readonly Dictionary<(int, ContentId), Dictionary<string, int>> PresetIds = new Dictionary<(int, ContentId), Dictionary<string, int>>();
        /// <summary>Sampled heads whose preset TextAsset is not in their MainAB (P line "missing": HS2 loads no preset).</summary>
        public readonly HashSet<(int, ContentId)> PresetMissing = new HashSet<(int, ContentId)>();
        /// <summary>True when the vanilla fixture was read; its comment lines (provenance).</summary>
        public bool HasVanilla { get; private set; }
        public readonly List<string> VanillaComments = new List<string>();
        public MakerInfoCache Infos { get; private set; }
        public string ModCacheFile { get; private set; }
        List<ZipMakerInfo> loadOrder;

        /// <summary>The zipmod scan cache of the HS2 install source under <paramref name="cacheDir"/> (the HS2N_MAKER_MODCACHE variable wins), or null.</summary>
        public static string FindModCache(string cacheDir)
        {
            var env = Environment.GetEnvironmentVariable("HS2N_MAKER_MODCACHE");
            if (!string.IsNullOrEmpty(env)) return File.Exists(env) ? env : null;
            if (cacheDir == null || !Directory.Exists(cacheDir)) return null;
            return Directory.GetFiles(cacheDir, "modlists-hs2-install-*.tsv").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
        }

        /// <summary>Reads the caches. <paramref name="vanillaFixture"/> may be missing (then only mod rows exist).</summary>
        public static FixtureListRows Load(string modCache, string vanillaFixture, bool writeInfoCache = true)
        {
            var f = new FixtureListRows { ModCacheFile = modCache };
            if (vanillaFixture != null && File.Exists(vanillaFixture)) f.ReadVanilla(vanillaFixture);
            f.ReadModCache(modCache);
            var withRows = new HashSet<string>(f.RawModRows.Where(r => r.category == 210 || r.category == 211 || r.category == 110 || r.category == 111).Select(r => r.zip), StringComparer.OrdinalIgnoreCase);
            var infoFile = writeInfoCache ? Path.Combine(Path.GetDirectoryName(modCache), Path.GetFileName(modCache).Replace("modlists-", "makerinfo-")) : null;
            f.Infos = MakerInfoCache.Load(infoFile, f.Zipmods.Where(z => z.guid != null && withRows.Contains(z.path)));
            // Sideloader loads one zipmod per GUID (the catalog: the last one of the source); load order = GUID order.
            var chosen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var z in f.Zipmods) if (z.guid != null) chosen[z.guid] = z.path;
            f.loadOrder = chosen.OrderBy(kv => kv.Key, GuidLoadOrder.Instance).Select(kv => f.Infos.Get(kv.Value)).Where(z => z != null).ToList();
            return f;
        }

        void ReadVanilla(string file)
        {
            bool first = true;
            foreach (var line in File.ReadLines(file))
            {
                if (first) { first = false; if (line != VanillaHeader) return; HasVanilla = true; continue; }
                if (line.StartsWith("#", StringComparison.Ordinal)) { VanillaComments.Add(line); continue; }
                var t = line.Split('\t');
                if (t[0] == "R" && t.Length >= 4)
                {
                    var v = new Dictionary<string, string>();
                    for (int k = 4; k + 1 < t.Length; k += 2) v[t[k]] = t[k + 1];
                    int cat = Int(t[1]);
                    Add(new ListRow(cat, ContentId.Vanilla(Int(t[2])), v, null, t[3] == "1"), -1);
                }
                else if (t[0] == "P" && t.Length >= 3 && ContentId.TryParse(t[2], out var head))
                {
                    var ids = new Dictionary<string, int>(StringComparer.Ordinal);
                    if (t.Length > 3 && t[3] == "missing") PresetMissing.Add((Int(t[1]), head));
                    for (int k = 3; k < t.Length; k++)
                    {
                        int eq = t[k].LastIndexOf('=');
                        if (eq > 0) ids[t[k].Substring(0, eq)] = Int(t[k].Substring(eq + 1));
                    }
                    PresetIds[(Int(t[1]), head)] = ids;
                }
            }
        }

        void ReadModCache(string file)
        {
            string zip = null, guid = null;
            int zi = -1;
            bool first = true;
            foreach (var line in File.ReadLines(file))
            {
                if (first) { first = false; continue; }
                if (line.Length < 2 || line[1] != '\t') continue;
                char k = line[0];
                if (k == 'Z')
                {
                    var t = line.Split('\t');
                    zip = t[1];
                    guid = t.Length > 3 && t[3].Length > 0 ? t[3] : null;
                    Zipmods.Add((zip, guid));
                    zi++;
                    if (guid != null && new ContentId(guid, 0).IsVanilla) guid = null;
                }
                else if (k == 'E' && guid != null)
                {
                    var t = line.Split('\t');
                    var v = new Dictionary<string, string>();
                    for (int i = 3; i + 1 < t.Length; i += 2) v[t[i]] = t[i + 1];
                    int cat = Int(t[1]), id = Int(t[2]);
                    RawModRows.Add((zip, guid, cat, id, v));
                    Add(new ListRow(cat, new ContentId(guid, id), v, zip), zi);
                }
            }
        }

        void Add(ListRow r, int zip)
        {
            if (!cats.TryGetValue(r.Category, out var c)) cats[r.Category] = c = new Cat();
            if (c.ByKey.TryGetValue(r.Key, out var old) && old.zip >= zip)
            {
                // first definition wins inside one zipmod / the lists; Sideloader keeps the later ones as rows of their own
                if (old.zip == zip && zip >= 0)
                {
                    if (!c.Defs.TryGetValue(r.Key, out var d)) c.Defs[r.Key] = d = new List<ListRow> { old.row };
                    d.Add(r);
                }
                return;
            }
            c.Defs.Remove(r.Key);
            c.ByKey[r.Key] = (r, zip, seq++);
            c.Ordered = null;
        }

        public IReadOnlyList<ListRow> RowsOf(int category)
        {
            if (!cats.TryGetValue(category, out var c)) return Array.Empty<ListRow>();
            return c.Ordered ??= c.ByKey.Values.OrderBy(x => x.row.IsVanilla ? 0 : 1).ThenBy(x => x.zip).ThenBy(x => x.seq).Select(x => x.row).ToList();
        }

        public ListRow Find(int category, ContentId key) =>
            !key.IsEmpty && cats.TryGetValue(category, out var c) && c.ByKey.TryGetValue(key, out var x) ? x.row : null;

        /// <summary>Every definition of (category, key) when it is repeated inside its zipmod, else null.</summary>
        public IReadOnlyList<ListRow> DefinitionsOf(int category, ContentId key) =>
            cats.TryGetValue(category, out var c) && c.Defs.TryGetValue(key, out var d) ? d : null;

        public IReadOnlyDictionary<ContentId, IReadOnlyList<ListRow>> RepeatedDefinitions(int category)
        {
            if (!cats.TryGetValue(category, out var c) || c.Defs.Count == 0) return null;
            var r = new Dictionary<ContentId, IReadOnlyList<ListRow>>();
            foreach (var kv in c.Defs) r[kv.Key] = kv.Value;
            return r;
        }

        public ZipMakerInfo InfoOf(string zipPath) => Infos?.Get(zipPath);

        public IReadOnlyList<ZipMakerInfo> InfosInLoadOrder => loadOrder;

        public IEnumerable<int> Categories => cats.Keys.OrderBy(k => k);

        static int Int(string s) => int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }
}
