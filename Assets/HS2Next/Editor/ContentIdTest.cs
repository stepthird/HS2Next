// Batch test: ContentId equality / parse / format, and a few facts of the list DB built from the
// configured sources (vanilla + all zipmods): counts, the base game's list rules (distributionNo gate,
// first definition wins), list category names, zipmod <migrationInfo> entries. Writes
// Spike/out/content/contentid.txt.
// Run: -executeMethod ContentIdTest.Run [-expectVanilla N] [-expectModRows N] [-expectGated N]
// Paths come from Config/settings.local.json (see CardResolveTest.Settings).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Content;
using HS2Next.Data;
using UnityEditor;
using UnityEngine;

public static class ContentIdTest
{
    static string Arg(string name, string def)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    public static void Run()
    {
        var sb = new StringBuilder();
        int pass = 0, fail = 0;
        void Check(string what, bool ok)
        {
            if (ok) pass++; else fail++;
            sb.AppendLine($"{(ok ? "ok  " : "FAIL")} {what}");
        }

        // ---------- ContentId ----------
        sb.AppendLine("# ContentId");
        var v5 = ContentId.Vanilla(5);
        Check("Vanilla(5).ToString() == \"illusion.hs2#5\"", v5.ToString() == "illusion.hs2#5");
        Check("Vanilla(5).IsVanilla, ModGuid null", v5.IsVanilla && v5.ModGuid == null && !v5.IsEmpty);
        Check("GUID compares case-insensitively", new ContentId("ILLUSION.HS2", 5) == v5 && new ContentId("ILLUSION.HS2", 5).IsVanilla);
        Check("equal ids have equal hashes", new ContentId("Some.Mod", 7).GetHashCode() == new ContentId("some.mod", 7).GetHashCode());
        Check("different id or guid are not equal", new ContentId("a", 1) != new ContentId("a", 2) && new ContentId("a", 1) != new ContentId("b", 1));
        Check("default is empty, ToString \"\", not vanilla", default(ContentId).IsEmpty && default(ContentId).ToString() == "" && !default(ContentId).IsVanilla && default(ContentId) != ContentId.Vanilla(0));
        var samples = new[] { ContentId.Vanilla(0), ContentId.Vanilla(-1), new ContentId("com.example.mod", 123456), new ContentId("odd#guid", 3), new ContentId("x", int.MinValue) };
        foreach (var s in samples)
            Check($"round-trip \"{s}\"", ContentId.TryParse(s.ToString(), out var p) && p == s && p.SourceGuid == s.SourceGuid && ContentId.Parse(s.ToString()) == s);
        Check("TryParse(\"\") -> empty", ContentId.TryParse("", out var e0) && e0.IsEmpty);
        foreach (var bad in new[] { null, "abc", "#5", "x#", "x#1.5", "x# 5", "x#5a", "x#99999999999" })
            Check($"TryParse rejects {(bad == null ? "null" : "\"" + bad + "\"")}", !ContentId.TryParse(bad, out _));
        bool threw = false;
        try { ContentId.Parse("nope"); } catch (FormatException) { threw = true; }
        Check("Parse throws FormatException", threw);
        threw = false;
        try { new ContentId("", 1); } catch (ArgumentException) { threw = true; }
        Check("empty GUID is rejected", threw);
        var dict = new Dictionary<ContentId, int> { [new ContentId("Mod.A", 1)] = 1 };
        Check("dictionary lookup ignores GUID case", dict.ContainsKey(new ContentId("mod.a", 1)));
        Check("CompareTo orders by GUID then id", new ContentId("a", 2).CompareTo(new ContentId("A", 10)) < 0 && new ContentId("b", 0).CompareTo(new ContentId("a", 99)) > 0);

        // ---------- list DB ----------
        sb.AppendLine();
        sb.AppendLine("# list DB");
        var gd = CardResolveTest.BuildDb(sb);
        // 1,661 = the base game's own count after its distributionNo gate; 15 rows gated.
        // With the AI-Shoujo flags opened by the settings (C9 since 4.0: ContentCatalog.AiInstallOpensCharacterLists and
        // aiInstall, as HS2 on the reference install, H1) the 15 rows are listed: 1,676, 0 gated.
        var hs2 = gd.Sources.OfType<Hs2InstallSource>().FirstOrDefault();
        bool aiOpen = hs2 != null && hs2.AiSpecial02 && hs2.AiSpecial03 && hs2.AiSpecial04;
        // with the zipmod loader's set on (Phase 4.4) the <game> filter drops the 2 rows of ddaa33_saya: 16,324
        sb.AppendLine($"zipmod set {(ContentCatalog.SideloaderZipmodSet ? "on" : "off")}: {(gd.ScannedZipmods ?? new List<ZipmodInfo>()).Count(z => z.SkippedBy == "game")} files skipped by the <game> filter, {(gd.ScannedZipmods ?? new List<ZipmodInfo>()).Count(z => z.SkippedBy == "version")} older files of a GUID");
        int expV = int.Parse(Arg("-expectVanilla", aiOpen ? "1676" : "1661")), expM = int.Parse(Arg("-expectModRows", ContentCatalog.SideloaderZipmodSet ? "16324" : "16326")), expG = int.Parse(Arg("-expectGated", aiOpen ? "0" : "15"));
        sb.AppendLine($"distinct rows {gd.Count}: vanilla {gd.VanillaCount}, mod {gd.ModCount}; mod rows read {gd.ModRowsLoaded}; zipmod GUIDs {gd.ZipmodsByGuid.Count}; categories {gd.Categories.Count()}");
        Check($"vanilla entry count {expV}", gd.VanillaCount == expV);
        Check($"mod rows read {expM}", gd.ModRowsLoaded == expM);
        Check("sum of CountOf == Count", gd.Categories.Sum(gd.CountOf) == gd.Count);
        if (hs2 != null)
        {
            sb.AppendLine($"vanilla gate: {hs2.GatedRows} rows gated (add50 {hs2.IsAdd50}); ID-column keys {hs2.IdColumnMismatches}; unfiled assets {hs2.UnfiledAssets}; name/categoryNo mismatches {hs2.CategoryNameMismatches}");
            Check($"vanilla rows gated by distributionNo {expG}", hs2.GatedRows == expG);
            if (aiOpen) Check("gate: 2/3/4 kept with the AI-Syoujyo flags opened by the settings (C9), 0/1/30 kept", hs2.PassesGate(2) && hs2.PassesGate(3) && hs2.PassesGate(4) && hs2.PassesGate(0) && hs2.PassesGate(1) && hs2.PassesGate(30));
            else Check("gate: 2/3/4 dropped without the AI-Syoujyo flags, 0/1/30 kept", !hs2.PassesGate(2) && !hs2.PassesGate(3) && !hs2.PassesGate(4) && hs2.PassesGate(0) && hs2.PassesGate(1) && hs2.PassesGate(30));
            var closed = new Hs2InstallSource(hs2.Root);
            Check("gate of a source without the flags: 2/3/4 dropped", !closed.PassesGate(2) && !closed.PassesGate(3) && !closed.PassesGate(4) && closed.PassesGate(1));
            Check("gate: >= 50 follows add50", hs2.PassesGate(50) == hs2.IsAdd50 && hs2.PassesGate(60) == hs2.IsAdd50);
        }
        sb.AppendLine($"duplicates: {gd.DuplicateRows} rows not taken; {gd.SameListDuplicates} repeat an id of their own zipmod or lists (first kept), {gd.SameListDuplicatesDiffering} of them with other columns");
        Check("list category names", ListCategories.NameOf(240) == "fo_top" && ListCategories.TryParse("ft_skin_b", out int cn) && cn == 231
            && ListCategories.TryParse(" 348 ", out int cn2) && cn2 == 348 && !ListCategories.TryParse("FO_TOP", out _)
            && ListCategories.OfListAsset("fo_top_00") == 240 && ListCategories.OfListAsset("ao_none_00") == 350 && ListCategories.OfListAsset("namelist") == null);
        var migr = gd.Migrations;
        sb.AppendLine($"migrationInfo entries: {migr.Count}");
        foreach (var m in migr) sb.AppendLine($"     {m} (declared by {m.Zipmod?.Guid})");
        Check("migration entries carry their zipmod and a trimmed old GUID", migr.All(m => m.Zipmod != null && !string.IsNullOrEmpty(m.GuidOld) && m.GuidOld == m.GuidOld.Trim() && !string.IsNullOrEmpty(m.GuidNew)));
        Check("migration entries in Sideloader load order (GUID)", migr.Select(m => m.Zipmod.Guid).SequenceEqual(migr.Select(m => m.Zipmod.Guid).OrderBy(g => g, HS2Next.Content.Studio.StudioCsv.LoadOrder)));
        Check("Migrate entries name a list category", migr.Where(m => m.Kind == MigrationKind.Migrate).All(m => ListCategories.NameOf(m.Category) != null));
        int withName = gd.ZipmodsByGuid.Values.Count(z => z.Name != null);
        sb.AppendLine($"zipmods with a manifest name: {withName} of {gd.ZipmodsByGuid.Count}");
        Check("manifest names read", withName > gd.ZipmodsByGuid.Count / 2);

        // GUIDs that differ only by case (they now share one namespace).
        var allGuids = gd.Sources.SelectMany(s => s.Zipmods).Where(z => z.Guid != null).Select(z => z.Guid).Distinct(StringComparer.Ordinal).ToList();
        var caseGroups = allGuids.GroupBy(g => g, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).ToList();
        sb.AppendLine($"zipmod GUIDs differing only by case: {caseGroups.Count}{(caseGroups.Count > 0 ? " (" + string.Join("; ", caseGroups.Take(5).Select(g => string.Join(" / ", g))) + ")" : "")}");

        Check("vanilla head 210#0 exists", gd.TryGetEntry(210, ContentId.Vanilla(0), out var head) && head.IsVanilla && head.Category == 210 && head.Key.Id == 0);
        if (head != null) sb.AppendLine($"     {head} : {head["MainAB"]} / {head["MainData"]}");
        Check("210#0 has MainAB and MainData", !string.IsNullOrEmpty(head?["MainAB"]) && !string.IsNullOrEmpty(head?["MainData"]));
        Check("lookup by parsed string \"illusion.hs2#0\"", gd.GetEntry(210, ContentId.Parse("illusion.hs2#0")) == head);
        var heads = gd.EntriesOf(210);
        Check("EntriesOf(210): vanilla rows first", heads.Count == gd.CountOf(210) && heads.Skip(heads.Count(x => x.IsVanilla)).All(x => !x.IsVanilla));
        Check("EntriesOf(210, vanilla) are all vanilla", gd.EntriesOf(210, ContentIds.Vanilla).All(x => x.IsVanilla) && gd.EntriesOf(210, ContentIds.Vanilla).Count == heads.Count(x => x.IsVanilla));
        Check("unknown category is empty", gd.EntriesOf(-12345).Count == 0 && gd.CountOf(-12345) == 0);

        // One zipmod entry, picked deterministically (first GUID in ordinal order that has rows).
        var zm = gd.ZipmodsByGuid.Values.Where(z => z.Entries.Count > 0).OrderBy(z => z.Guid, StringComparer.Ordinal).FirstOrDefault();
        Check("a zipmod with list rows exists", zm != null);
        if (zm != null)
        {
            var row = zm.Entries[0];
            sb.AppendLine($"zipmod {zm.Guid}: {zm.Entries.Count} rows; first {row} : {row["MainAB"]} / {row["MainData"] ?? row["MainTex"]}");
            Check("row key is (zipmod GUID, own id)", row.Key == new ContentId(zm.Guid, row.Key.Id) && !row.IsVanilla && row.Zipmod == zm);
            Check("TryGetEntry by (GUID, id)", gd.TryGetEntry(row.Category, new ContentId(zm.Guid, row.Key.Id), out var hit) && hit.Key == row.Key);
            Check("TryGetEntry with upper-case GUID", gd.GetEntry(row.Category, new ContentId(zm.Guid.ToUpperInvariant(), row.Key.Id)) == hit);
            Check("EntriesOf(category, GUID) contains it", gd.EntriesOf(row.Category, zm.Guid).Contains(hit));
            Check("lookup by string form", gd.GetEntry(row.Category, ContentId.Parse(row.Key.ToString())) == hit);

            // Card resolve path with a synthetic card.
            const string prop = "test.property";
            var card = new Card();
            card.Resolve.Add(new Card.ResolveInfo { GUID = zm.Guid, Property = prop, Slot = row.Key.Id, Category = row.Category });
            Check("Resolve(card) -> mod row", gd.Resolve(card, row.Category, 0, prop) == hit);
            Check("KeyFor(card) -> (GUID, slot)", gd.KeyFor(card, row.Category, 0, prop) == row.Key);
            Check("KeyFor without resolve info -> vanilla id", gd.KeyFor(card, row.Category, 42, "other.property") == ContentId.Vanilla(42));
            var missing = new Card();
            missing.Resolve.Add(new Card.ResolveInfo { GUID = "hs2next.test.missing", Property = prop, Slot = 1, Category = 210 });
            int logBefore = gd.Log.Count;
            Check("Resolve(missing mod) falls back to vanilla and logs", gd.Resolve(missing, 210, 0, prop) == head && gd.Log.Count == logBefore + 1);
            Check("KeyFor(missing mod) keeps the mod key", gd.KeyFor(missing, 210, 0, prop) == new ContentId("hs2next.test.missing", 1));
        }

        sb.Insert(0, $"contentid test: {pass} passed, {fail} failed\n\n");
        var outDir = Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "content");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "contentid.txt"), sb.ToString().Replace("\r\n", "\n"));
        gd.UnloadAll();
        Debug.Log($"CONTENTID TEST DONE: {pass} passed, {fail} failed");
        if (Application.isBatchMode) TestExit.Exit(fail == 0 ? 0 : 1);
    }
}
