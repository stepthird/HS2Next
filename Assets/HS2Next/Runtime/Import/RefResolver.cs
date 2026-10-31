// Reference resolution R(v, P, c): a raw HS2 id, the exact Sideloader property of the
// field and the list category become one `guid#id` reference. Sideloader's <migrationInfo> renames are applied as
// Sideloader applies them (open point 15.3): GUID and id migrations of a resolve entry before its lookup, id migrations
// after a failed compatibility lookup. Also the property table.
using System;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Content;
using HS2Next.Data;

namespace HS2Next.Import
{
    /// <summary>Sideloader `Property` names and categories of every reference (`StructReference`).</summary>
    public static class RefProperties
    {
        public const string OutfitPrefix = "outfit.";

        /// <summary>Clothes item property per kind 0..7 (female table).</summary>
        public static readonly string[] ClothesFemale = { "ClothesTop", "ClothesBot", "ClothesBra", "ClothesShorts", "ClothesGloves", "ClothesPantyhose", "ClothesSocks", "ClothesShoes" };
        /// <summary>Clothes item property per kind 0..7 (male table; inner, pantyhose and socks are shared).</summary>
        public static readonly string[] ClothesMale = { "ClothesTopM", "ClothesBotM", "ClothesBra", "ClothesShorts", "ClothesGlovesM", "ClothesPantyhose", "ClothesSocks", "ClothesShoesM" };
        /// <summary>Pattern property stem per kind (both sexes): `Clothes&lt;stem&gt;Pattern&lt;j&gt;`.</summary>
        public static readonly string[] PatternStem = { "Top", "Bot", "Bra", "Shorts", "Gloves", "Pantyhose", "Socks", "Shoes" };
        public static readonly string[] HairProperty = { "HairBack", "HairFront", "HairSide", "HairOption" };
        public static readonly int[] HairCategory = { 300, 301, 302, 303 };
        public const int HairMeshCategory = 306, PatternCategory = 348;
        public const int KcoxPatternId = 58947543;
        public const string KcoxNamespace = "hs2.kcox", Unresolved = "hs2.unresolved";
        public const int MissingMarker = 99999999, LocalIdBase = 100000000;

        public static string ClothesItem(int kind, int sex, bool outfit) =>
            (outfit ? OutfitPrefix : "") + "ChaFileClothes." + (sex == 0 ? ClothesMale : ClothesFemale)[kind];

        public static string ClothesPattern(int kind, int j, bool outfit) =>
            (outfit ? OutfitPrefix : "") + "ChaFileClothes.Clothes" + PatternStem[kind] + "Pattern" + j.ToString(CultureInfo.InvariantCulture);

        public static string Accessory(int slot, bool outfit) => Card.ResolveInfo.AccessoryProperty(slot, outfit);

        /// <summary>Clothes item category per kind and sex (`coord.sex`).</summary>
        public static int ClothesCategory(int kind, int sex) =>
            (sex == 0 ? HS2Next.Save.ClothesSet.MaleCategories : HS2Next.Save.ClothesSet.FemaleCategories)[kind];
    }

    /// <summary>One reference field waiting for resolution: raw id, property, category, where the result goes.</summary>
    sealed class RefField
    {
        public int Raw;
        public string Property;
        public int Category;
        public string Path;
        public Action<string> Set;
        /// <summary>The resolved text (after <see cref="RefResolver.Resolve"/>).</summary>
        public string Result;
    }

    /// <summary>R(v, P, c) over one source's resolve entries; findings go to the report.</summary>
    sealed class RefResolver
    {
        readonly ResolveCatalog cat;
        readonly List<Card.ResolveInfo> entries;
        readonly bool[] used;
        readonly ImportReport report;

        public RefResolver(ResolveCatalog cat, List<Card.ResolveInfo> entries, ImportReport report)
        {
            this.cat = cat;
            this.entries = entries ?? new List<Card.ResolveInfo>();
            used = new bool[this.entries.Count];
            this.report = report;
        }

        static string Ref(string guid, int id) => guid + "#" + id.ToString(CultureInfo.InvariantCulture);

        public string Resolve(RefField f)
        {
            f.Result = Resolve(f.Raw, f.Property, f.Category, f.Path);
            f.Set?.Invoke(f.Result);
            return f.Result;
        }

        /// <summary><paramref name="property"/> null = the field has no Sideloader property (step 2 only).</summary>
        public string Resolve(int v, string property, int c, string path)
        {
            report.Count("ref.total");
            // 0. The KCOX "Custom Overlay Pattern" entry the plugin injects into the pattern list.
            if (c == RefProperties.PatternCategory && v == RefProperties.KcoxPatternId)
            {
                report.Count("ref.kcox");
                return Ref(RefProperties.KcoxNamespace, v);
            }
            // 1. The first resolve entry with exactly this property.
            int ei = -1;
            if (property != null)
                for (int i = 0; i < entries.Count; i++)
                    if (string.Equals(entries[i].Property, property, StringComparison.Ordinal)) { ei = i; break; }
            if (ei >= 0)
            {
                used[ei] = true;
                var e = entries[ei];
                string guid = e.GUID?.Trim();
                int slot = e.Slot;
                if (!string.IsNullOrEmpty(guid)) Migrate(ref guid, ref slot, e.Category, path);
                if (!string.IsNullOrEmpty(guid))
                {
                    if (e.Category != c) report.Warning("resolve.categoryMismatch", path, $"resolve entry category {e.Category} differs from the field's {c}; entry used");
                    // one finding per file (its count = the items): a card saved on another machine holds that machine's
                    // runtime ids (≥ 100000000) in every mod field, which says nothing about one item
                    if (v != e.Slot) report.Info("resolve.slotMismatch", "", $"card values differ from their resolve entries' slots (first: {path}, card value {v}, entry slot {e.Slot}); count = items");
                    if (!cat.IsInstalled(guid))
                    {
                        report.Warning("resolve.missingMod", path, $"zipmod {guid} is not installed; reference kept");
                        report.Count("ref.missingMod");
                    }
                    else if (!cat.HasRow(c, guid, slot))
                    {
                        report.Warning("resolve.missingItem", path, $"zipmod {guid} has no row {slot} in category {c}; reference kept");
                        report.Count("ref.missingItem");
                    }
                    else report.Count("ref.mod");
                    return Ref(guid, slot);
                }
            }
            // 2. Compatibility path, in Sideloader's order.
            if (cat.IsVanillaRow(c, v))
            {
                report.Count("ref.vanilla");
                return Ref(ContentIds.Vanilla, v);
            }
            var cand = cat.CompatCandidates(c, v);
            if (cand.Count > 0)
            {
                var g = cand[0].Key.SourceGuid;
                report.Info("resolve.compat", path, $"id {v} of category {c} has no resolve entry; taken from zipmod {g}");
                if (cand.Count > 1) report.Warning("resolve.compatAmbiguous", path, $"{cand.Count} zipmods have id {v} in category {c}; the first in load order ({g}) taken");
                report.Count("ref.compat");
                return Ref(g, v);
            }
            // Sideloader then tries an id migration whose old id is v (the first entry of this category only).
            foreach (var m in cat.ByIdOld(v))
            {
                if (m.Category != c) continue;
                if (cat.HasRow(c, m.GuidNew, m.IdNew))
                {
                    report.Info("resolve.migrated", path, $"id {v} migrated to {m.GuidNew}#{m.IdNew} (declared by {m.Zipmod?.Guid})");
                    report.Count("ref.migrated");
                    return Ref(m.GuidNew, m.IdNew);
                }
                break;
            }
            if (v == RefProperties.MissingMarker || v >= RefProperties.LocalIdBase || v < 0)
            {
                report.Warning("resolve.unresolved", path, $"id {v} of category {c} resolves to nothing");
                report.Count("ref.unresolved");
                return Ref(RefProperties.Unresolved, v);
            }
            report.Warning("resolve.missingVanilla", path, $"id {v} is not a row of category {c} on this install; kept as a base-game reference");
            report.Count("ref.missingVanilla");
            return Ref(ContentIds.Vanilla, v);
        }

        // Sideloader's migration of a resolve entry: a StripAll entry for the GUID blanks it (compatibility path); else the
        // first id migration (old id = the entry's slot, category = the entry's category) whose new GUID is installed; else
        // the first MigrateAll whose new GUID is installed (slot kept).
        void Migrate(ref string guid, ref int slot, int category, string path)
        {
            var list = cat.ByGuidOld(guid);
            if (list.Count == 0) return;
            foreach (var m in list)
                if (m.Kind == MigrationKind.StripAll)
                {
                    report.Info("resolve.migrated", path, $"zipmod {guid} is stripped by a migration entry (declared by {m.Zipmod?.Guid}); compatibility path");
                    report.Count("ref.migrationStrip");
                    guid = "";
                    return;
                }
            foreach (var m in list)
                if (m.Kind == MigrationKind.Migrate && m.IdOld == slot && m.Category == category && cat.IsInstalled(m.GuidNew))
                {
                    report.Info("resolve.migrated", path, $"{guid}#{slot} migrated to {m.GuidNew}#{m.IdNew} (declared by {m.Zipmod?.Guid})");
                    report.Count("ref.migrationId");
                    guid = m.GuidNew;
                    slot = m.IdNew;
                    return;
                }
            foreach (var m in list)
                if (m.Kind == MigrationKind.MigrateAll && cat.IsInstalled(m.GuidNew))
                {
                    report.Info("resolve.migrated", path, $"zipmod {guid} migrated to {m.GuidNew} (declared by {m.Zipmod?.Guid})");
                    report.Count("ref.migrationGuid");
                    guid = m.GuidNew;
                    return;
                }
        }

        /// <summary>resolve entries no field used.</summary>
        public void ReportUnused(string pathPrefix)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (used[i]) continue;
                report.Info("resolve.unusedEntry", pathPrefix + entries[i].Property, $"resolve entry {entries[i]} matched no field");
                report.Count("ref.unusedEntry");
            }
        }
    }
}
