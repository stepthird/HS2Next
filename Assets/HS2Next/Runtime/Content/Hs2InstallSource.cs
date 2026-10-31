// An installed HS2: vanilla abdata bundles (dependencies from the abdata AssetBundleManifest),
// the vanilla character lists in abdata/list/characustom, and the zipmods under <root>/mods.
// The lists are read like the base game's list loader: bundles in
// name order, each TextAsset filed under the category its name names (others ignored), rows gated by
// distributionNo, a row keyed by its ID column, the first definition of an id kept (ContentCatalog).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HS2Next.IO;
using UnityEngine;

namespace HS2Next.Content
{
    public sealed class Hs2InstallSource : ZipmodSourceBase
    {
        public const string DefaultId = "hs2-install";

        /// <summary>Install root (the folder that contains abdata and mods).</summary>
        public string Root { get; }
        public string Abdata => Path.Combine(Root, "abdata");

        AssetBundleManifest manifest;

        /// <summary>
        /// AI-Syoujyo DLC flags of the base game's list gate: rows with distributionNo 2 / 3 / 4 are kept only with the
        /// flag (the game sets them from an AI-Syoujyo install it finds in the registry; HS2Next does not look). Default false.
        /// </summary>
        public bool AiSpecial02, AiSpecial03, AiSpecial04;

        /// <summary>Rows dropped by the distributionNo gate in the last <see cref="ReadLists"/>.</summary>
        public int GatedRows { get; private set; }
        /// <summary>Rows whose ID column differs from their dictList key (keyed by the ID column, as the game does).</summary>
        public int IdColumnMismatches { get; private set; }
        /// <summary>List TextAssets whose name names no category (the game ignores them) / filed under another category than their categoryNo.</summary>
        public int UnfiledAssets { get; private set; }
        public int CategoryNameMismatches { get; private set; }

        bool? add50;

        /// <summary>The base game's "append disc" check: abdata/add50 exists and lists adv/scenario/c10/50/08.unity3d. Rows with
        /// distributionNo ≥ 50 are kept only when it holds.</summary>
        public bool IsAdd50
        {
            get
            {
                if (add50 == null)
                {
                    var f = Path.Combine(Abdata, "add50");
                    add50 = File.Exists(f) && File.ReadAllText(f).Contains("adv/scenario/c10/50/08.unity3d");
                }
                return add50.Value;
            }
        }

        /// <summary>The base game's distributionNo gate for its own list rows (mod rows are not gated).</summary>
        public bool PassesGate(int distributionNo) =>
            (AiSpecial02 || distributionNo != 2) && (AiSpecial03 || distributionNo != 3) && (AiSpecial04 || distributionNo != 4) && (IsAdd50 || distributionNo < 50);

        public Hs2InstallSource(string root, string id = DefaultId)
            : base(id, "HS2 install", Path.Combine(root, "mods"))
        {
            Root = root;
        }

        string Key(string rel) => Id + ":" + rel;

        public override IEnumerable<ListEntry> ReadLists(IBundleCache cache)
        {
            GatedRows = IdColumnMismatches = UnfiledAssets = CategoryNameMismatches = 0;
            var dir = Path.Combine(Abdata, "list", "characustom");
            if (!Directory.Exists(dir)) yield break;
            foreach (var f in Directory.GetFiles(dir, "*.unity3d").OrderBy(x => x, StringComparer.Ordinal))
            {
                var b = OpenBundle("list/characustom/" + Path.GetFileName(f), cache);
                if (!b) continue;
                foreach (var ta in b.LoadAllAssets<TextAsset>())
                {
                    object o;
                    try { o = MsgPack.Read(ta.bytes); } catch { continue; }
                    int dataCat = o.Get("categoryNo").Int(-1);
                    if (dataCat < 0) continue;
                    // The game files the asset under the category its name names and ignores assets that name none.
                    int? named = ListCategories.OfListAsset(ta.name);
                    if (named == null) { UnfiledAssets++; continue; }
                    int cat = named.Value;
                    if (cat != dataCat) CategoryNameMismatches++;
                    var keys = o.Get("lstKey").Items().Select(k => k.Str()).ToList();
                    if (!(o.Get("dictList") is Dictionary<object, object> dl)) continue;
                    bool pass = PassesGate(o.Get("distributionNo").Int(0));
                    int idCol = keys.IndexOf("ID");
                    foreach (var kv in dl)
                    {
                        if (!pass) { GatedRows++; continue; }
                        var vals = kv.Value.Items().ToList();
                        int id = kv.Key.Int();
                        if (idCol >= 0 && idCol < vals.Count && int.TryParse(vals[idCol].Str(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int idv) && idv != id)
                        {
                            IdColumnMismatches++;
                            id = idv;
                        }
                        var e = new ListEntry { Category = cat, Key = ContentId.Vanilla(id), Source = this };
                        for (int i = 0; i < keys.Count && i < vals.Count; i++) e.Values[keys[i]] = vals[i].Str();
                        yield return e;
                    }
                }
            }
        }

        public override bool HasBundle(string path) => File.Exists(Path.Combine(Abdata, path.Replace('\\', '/')));

        public override IEnumerable<string> EnumerateStudioInfoBundles()
        {
            var dir = Path.Combine(Abdata, "studio", "info");
            if (!Directory.Exists(dir)) return Enumerable.Empty<string>();
            return Directory.EnumerateFiles(dir, "*.unity3d", SearchOption.AllDirectories)
                .Select(f => "studio/info/" + f.Substring(dir.Length + 1).Replace('\\', '/'));
        }

        public override AssetBundle OpenBundle(string path, IBundleCache cache)
        {
            var rel = path.Replace('\\', '/');
            if (!manifest)
            {
                var mb = cache.GetOrLoad(Key("abdata"), () => AssetBundle.LoadFromFile(Path.Combine(Abdata, "abdata")));
                if (mb) manifest = mb.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
            }
            if (manifest)
                foreach (var d in manifest.GetAllDependencies(rel))
                    cache.GetOrLoad(Key(d), () => AssetBundle.LoadFromFile(Path.Combine(Abdata, d)));
            var full = Path.Combine(Abdata, rel);
            return cache.GetOrLoad(Key(rel), () => File.Exists(full) ? AssetBundle.LoadFromFile(full) : null);
        }
    }
}
