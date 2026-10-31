// Batch test: zipmod character list CSVs (abdata/list/characustom).
// Part 1 checks the Sideloader reading rules on synthetic CSV text (header with trailing commas, stop
// rule, line ends, byte-order marks, no trimming of fields, no distributionNo gate, Possess forced to 1).
// Part 2 counts the zipmod rows of the list DB built from the configured sources against the census of
// this install (unique (GUID, category, id), rows read, spot categories; expected values are arguments).
// Writes Spike/out/content/zipmod_lists.txt.
// Run: -executeMethod ZipmodListTest.Run [-expectUnique N] [-expectRows N] [-expectCat cat=N,cat=N,...]
// Paths come from Config/settings.local.json (see CardResolveTest.Settings).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Content;
using HS2Next.IO;
using UnityEditor;
using UnityEngine;

public static class ZipmodListTest
{
    // Census of this install (every zipmod CSV read like Sideloader's list loader; a scratch script). With the zipmod
    // loader's set on (ContentCatalog.SideloaderZipmodSet, Phase 4.4) the <game> filter drops the 2 rows of ddaa33_saya (240, 243).
    static string DefaultUnique => ContentCatalog.SideloaderZipmodSet ? "15572" : "15574";
    static string DefaultRows => ContentCatalog.SideloaderZipmodSet ? "16324" : "16326";
    static string DefaultCats => "210=364,211=3003,314=491,110=9,111=142,212=124,240=" + (ContentCatalog.SideloaderZipmodSet ? "807" : "808");

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
        void Eq(string what, long expected, long measured) => Check($"{what}: expected {expected}, measured {measured}", expected == measured);

        // ---------- reading rules ----------
        sb.AppendLine("# reading rules (synthetic CSV text)");
        CharaListCsvFile P(string text) => CharaListCsv.Parse(text, out _);
        string Ids(CharaListCsvFile f) => f == null ? "null" : string.Join(",", f.Rows.Select(r => r.id));

        var bare = P("240\n0\nabdata/list/x\nID,Kind,Name,MainAB\n7,0,a,b.unity3d\n8,0,c,d.unity3d\n");
        Check("bare header: category 240, rows 7,8", bare != null && bare.Category == 240 && Ids(bare) == "7,8");
        var excel = P("240,,,,\r\n0,,,,\r\nabdata/list/x,,,,\r\nID,Kind,Name,MainAB,\r\n7,0,a,b.unity3d,\r\n");
        Check("Excel trailing commas: category 240, row 7, trailing key empty",
            excel != null && excel.Category == 240 && Ids(excel) == "7" && excel.Keys.Length == 5 && excel.Keys[4] == "");
        Check("first-field rule tolerates white space and text after the comma (\" 211 ,x\")", P(" 211 ,x\n0\np\nID,Name\n1,a\n")?.Category == 211);
        var stop = P("240\n0\np\nID,Name\n1,a\n\n2,b\n");
        Check("reading stops at the first line without ',' (blank line)", Ids(stop) == "1");
        Check("reading stops at a comma-less text line", Ids(P("240\n0\np\nID,Name\n1,a\nend\n2,b\n")) == "1");
        Check("CR-only line ends", Ids(P("240\r0\rp\rID,Name\r5,a\r6,b\r")) == "5,6");
        Check("mixed CR / CRLF / LF line ends", Ids(P("240\r\n0\rp\nID,Name\r5,a\n6,b\r\n")) == "5,6");
        Check("fewer than 4 lines -> rejected", P("240\n0\np\n") == null);
        Check("non-integer category -> rejected", P("fo_top\n0\np\nID,Name\n1,a\n") == null);
        Check("non-integer distributionNo -> rejected", P("240\nx\np\nID,Name\n1,a\n") == null);
        var gate = P("240\n2\np\nID,Name\n1,a\n");
        Check("distributionNo 2 is read and not gated", gate != null && gate.DistributionNo == 2 && Ids(gate) == "1");
        Check("distributionNo 51 is not gated", Ids(P("240\n51\np\nID,Name\n1,a\n")) == "1");
        var ws = P("240\n0\np\n ID, Name \n 12 , padded ,x\n");
        Check("key line trimmed, keys not trimmed one by one", ws != null && ws.Keys.SequenceEqual(new[] { "ID", " Name" }));
        Check("id from column 0 with white space; fields kept verbatim", ws != null && Ids(ws) == "12" && ws.Rows[0].fields[1] == " padded ");
        Check("no quoting: a quoted comma splits", P("240\n0\np\nID,Name\n3,\"a,b\"\n")?.Rows[0].fields.Length == 3);
        var bad = P("240\n0\np\nID,Name\nx,a\n4,b\n");
        Check("row with a non-integer column 0 is dropped and counted", bad != null && Ids(bad) == "4" && bad.BadIdRows == 1);
        var pos = P("240\n0\np\nID,Kind,Possess,Name\n1,0,0,a\n2,0,99,b\n");
        Check("Possess forced to 1 on every row", pos != null && pos.Rows.All(r => r.fields[2] == "1"));
        Check("row too short for Possess -> file rejected", P("240\n0\np\nID,Kind,Possess,Name\n1,0,0,a\n2,0\n") == null);
        Check("entry rule: abdata/list/characustom*/**.csv, any case",
            CharaListCsv.IsCharaListEntry("abdata/list/characustom/00/fo_top_00.csv") && CharaListCsv.IsCharaListEntry("ABData/List/CharaCustom/x.CSV")
            && CharaListCsv.IsCharaListEntry("abdata/list/characustom_extra/x.csv") && !CharaListCsv.IsCharaListEntry("abdata/list/map/x.csv")
            && !CharaListCsv.IsCharaListEntry("abdata/list/characustom/x.txt"));
        var text = "240\n0\np\nID,Name\n1,é\n";
        Check("decode: UTF-8 with and without BOM", ZipIndex.DecodeText(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray()) == text
            && ZipIndex.DecodeText(Encoding.UTF8.GetBytes(text)) == text);
        Check("decode: UTF-16 LE / BE with BOM", ZipIndex.DecodeText(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray()) == text
            && ZipIndex.DecodeText(Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes(text)).ToArray()) == text);
        Check("decode: UTF-32 LE with BOM", ZipIndex.DecodeText(Encoding.UTF32.GetPreamble().Concat(Encoding.UTF32.GetBytes(text)).ToArray()) == text);
        Check("decode: invalid UTF-8 becomes U+FFFD", ZipIndex.DecodeText(new byte[] { 0x31, 0xFF, 0x32 }) == "1�2");

        // ---------- this install ----------
        sb.AppendLine();
        sb.AppendLine("# zipmod character lists of the configured sources");
        var gd = CardResolveTest.BuildDb(sb);
        var zips = gd.Sources.SelectMany(s => s.Zipmods).Where(z => z.Guid != null && !new ContentId(z.Guid, 0).IsVanilla).ToList();
        var keys = new HashSet<(int cat, ContentId key)>();
        long rows = 0;
        foreach (var z in zips)
            foreach (var e in z.Entries) { rows++; keys.Add((e.Category, e.Key)); }
        var byCat = keys.GroupBy(k => k.cat).ToDictionary(g => g.Key, g => g.Count());
        sb.AppendLine($"zipmods with a GUID {zips.Count}, with list rows {zips.Count(z => z.Entries.Count > 0)}; rows read {rows}; unique (GUID, category, id) {keys.Count}");
        sb.AppendLine("  " + $"zipmod set {(ContentCatalog.SideloaderZipmodSet ? "on" : "off")}: {(gd.ScannedZipmods ?? new List<ZipmodInfo>()).Count(z => z.SkippedBy == "game")} files skipped by the <game> filter, {(gd.ScannedZipmods ?? new List<ZipmodInfo>()).Count(z => z.SkippedBy == "version")} older files of a GUID");
        Eq("rows read (all zipmods, before de-duplication)", long.Parse(Arg("-expectRows", DefaultRows)), rows);
        Eq("unique (GUID, category, id)", long.Parse(Arg("-expectUnique", DefaultUnique)), keys.Count);
        Eq("catalog ModRowsLoaded = rows read", rows, gd.ModRowsLoaded);
        Eq("catalog mod rows = unique keys", keys.Count, gd.ModCount);
        foreach (var kv in Arg("-expectCat", DefaultCats).Split(',').Select(s => s.Split('=')))
        {
            int cat = int.Parse(kv[0]);
            Eq($"category {cat}: unique mod rows", long.Parse(kv[1]), byCat.TryGetValue(cat, out var n) ? n : 0);
            Eq($"category {cat}: catalog rows minus vanilla rows", byCat.TryGetValue(cat, out n) ? n : 0, gd.CountOf(cat) - gd.EntriesOf(cat, ContentIds.Vanilla).Count);
        }
        var errors = gd.Log.Where(l => l.Contains("characustom")).ToList();
        sb.AppendLine($"list files rejected during this scan (cold scans only): {errors.Count}");
        foreach (var l in errors.Take(20)) sb.AppendLine("     " + l);
        sb.AppendLine();
        sb.AppendLine("unique mod rows per category:");
        foreach (var kv in byCat.OrderBy(kv => kv.Key)) sb.AppendLine($"  {kv.Key,4} {kv.Value,6}");

        sb.Insert(0, $"zipmod list test: {pass} passed, {fail} failed\n\n");
        var outDir = Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "content");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "zipmod_lists.txt"), sb.ToString().Replace("\r\n", "\n"));
        gd.UnloadAll();
        Debug.Log($"ZIPMOD LIST TEST DONE: {pass} passed, {fail} failed");
        if (Application.isBatchMode) TestExit.Exit(fail == 0 ? 0 : 1);
    }
}
