// Batch test of the HS2 card / coordinate importer (Runtime/Import):
//   A1 import → save → reload of every file: no exception, no load failure, only the expected unreadable files rejected,
//      and only with `card.*` codes; save → load → save is byte-identical;
//   A2 field-by-field equality of the reloaded documents with an independent evaluation of the HS2 card trees (MemberInit
//      values for missing keys; every `ref` against R(v, P, c) computed here from the catalog, not by the importer);
//      plugin sections against the decoder outputs; counts against decoder counts minus reported drops;
//   A3 raw plugin copies byte-identical to RawPluginData(id), index fields;
//   A5 report census (`accessory.type120`, `coord.sex`, `hair.bundlesDropped`, `float.negativeZero`);
//   A6 re-import with a fixed clock gives identical bytes;
//   timing: import ms per file (mean, p95, max), save and load ms.
// A4 (visual parity from saved files) belongs to the document-based character build (wave 2).
// Files: the card folders of Tools/targets.json (`cards`) and UserData/coordinate; a file is imported by its marker, so
// coordinate cards in the card folders and character cards in the coordinate folder are imported by the other importer
// and counted apart (the census numbers are over the 957 character cards of the card folders and the 1,123 coordinates
// of the coordinate folder). Census numbers apply to the full run only.
// Run: -executeMethod ImportTest.Run     headless sample (-limit N files per folder, default 25)
//      -executeMethod ImportTest.RunFull every file (needs -nographics; about 5 min)
//      options: -expectRejectedChara N (default 0), -expectRejectedCoord N (default 0)
// Report: Spike/out/import/import_report.txt, first line "N passed, M failed"; EditorApplication.Exit(1) on a failure.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Data;
using HS2Next.Data.Ext;
using HS2Next.Import;
using HS2Next.IO;
using HS2Next.Save;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using SaveUncensor = HS2Next.Save.UncensorData;

public static class ImportTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "import");
    static readonly DateTime Fixed = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    static string Arg(string name, string def)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    public static void Run() => Main(int.Parse(Arg("-limit", "25"), CultureInfo.InvariantCulture), false);
    public static void RunFull() => Main(int.MaxValue, true);

    // ------------------------------------------------------------------ state

    internal sealed class Group
    {
        public string Name;
        public int Files, Chara, Coord, Rejected, NotCards;
        public readonly Dictionary<string, long> Counts = new Dictionary<string, long>(StringComparer.Ordinal);
        public void Add(ImportReport r) { foreach (var kv in r.Counts) { Counts.TryGetValue(kv.Key, out var v); Counts[kv.Key] = v + kv.Value; } }
        public long C(string k) => Counts.TryGetValue(k, out var v) ? v : 0;
    }

    static int pass, fail;
    static StringBuilder sb;
    static readonly List<string> Mismatches = new List<string>();
    static int mismatchCount;
    static readonly List<double> ImportMs = new List<double>(), SaveMs = new List<double>(), LoadMs = new List<double>();
    static readonly SortedDictionary<string, int> FindingCodes = new SortedDictionary<string, int>(StringComparer.Ordinal);
    static readonly SortedDictionary<string, int> RejectCodes = new SortedDictionary<string, int>(StringComparer.Ordinal);
    static readonly SortedDictionary<string, List<string>> Examples = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
    static int a1Fail, a3Fail, a6Fail, roundTripFail, migrationCases;

    static void Check(string name, bool ok, string detail = "")
    {
        if (ok) pass++; else fail++;
        sb.AppendLine($"{(ok ? "ok  " : "FAIL")} {name}{(detail.Length > 0 ? "  (" + detail + ")" : "")}");
    }

    static void Mismatch(string file, string what)
    {
        mismatchCount++;
        if (Mismatches.Count < 80) Mismatches.Add(file + ": " + what);
    }

    // ------------------------------------------------------------------ main

    static void Main(int limit, bool full)
    {
        var p = Begin(full, limit, CardResolveTest.BuildDb);
        foreach (var (g, dir) in p.Groups)
            foreach (var path in FilesOf(dir, limit)) One(p, g, path);
        End(p, true);
    }

    /// <summary>One pass over the folders (Run / RunFull; AllCardsTest calls <see cref="One"/> per file in its own loop).</summary>
    internal sealed class Pass
    {
        public bool Full;
        public Stopwatch Total;
        public ContentCatalog Gd;
        public Hs2Importer Importer;
        public Expect Expect;
        public string User;
        /// <summary>female, male (the card folders of Tools/targets.json) and coordinate (UserData/coordinate), in this order.</summary>
        public List<(Group g, string dir)> Groups;
        public Group CoordInCharaFolders, CharaInCoordFolder;
        public readonly List<string> Misplaced = new List<string>();
    }

    /// <summary>The files of one folder in the pass's order (ordinal by full path, at most <paramref name="limit"/>).</summary>
    internal static List<string> FilesOf(string dir, int limit = int.MaxValue) =>
        Directory.Exists(dir) ? Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).Take(limit).ToList() : new List<string>();

    /// <summary>Starts a pass: the report, the catalog (<paramref name="catalog"/> writes its head lines into the report), the
    /// importer with the fixed clock, the expectation and the unit checks.</summary>
    internal static Pass Begin(bool full, int limit, Func<StringBuilder, ContentCatalog> catalog)
    {
        sb = new StringBuilder();
        pass = fail = 0;
        var p = new Pass { Full = full, Total = Stopwatch.StartNew() };
        var gd = p.Gd = catalog(sb);
        var importer = p.Importer = new Hs2Importer(gd, new ImportOptions { Clock = () => Fixed });
        p.Expect = new Expect(gd);
        var targets = TestTargets.Load();
        string user = p.User = TestTargets.UserData;
        p.Groups = new List<(Group g, string dir)>
        {
            (new Group { Name = "female" }, TestTargets.Abs(targets.cards.female)),
            (new Group { Name = "male" }, TestTargets.Abs(targets.cards.male)),
            (new Group { Name = "coordinate" }, Path.Combine(user, "coordinate")),
        };
        p.CoordInCharaFolders = new Group { Name = "coordinates in the card folders" };
        p.CharaInCoordFolder = new Group { Name = "character cards in the coordinate folder" };
        sb.AppendLine($"mode {(full ? "full" : "sample, " + limit + " files per folder")}; importer {importer.ImporterVersion}; migration entries {importer.Resolve.MigrationCount}");
        if (targets.skip.Length > 0) sb.AppendLine($"skip list entries: {targets.skip.Length} (imported anyway: the list concerns rendering)");
        Units(importer, gd, targets);
        return p;
    }

    /// <summary>One file of folder group <paramref name="g"/>: import, save, reload, A2 / A3 / A6 (a file is imported by its marker).</summary>
    internal static void One(Pass p, Group g, string path)
    {
        g.Files++;
        string rel = path.Substring(p.User.Length).TrimStart('\\', '/').Replace('\\', '/');
        byte[] data = File.ReadAllBytes(path);
        var kind = Hs2Importer.Detect(data);
        try
        {
            var tg = kind == Hs2SourceKind.Coord ? (g.Name == "coordinate" ? g : p.CoordInCharaFolders)
                : kind == Hs2SourceKind.Chara && g.Name == "coordinate" ? p.CharaInCoordFolder : g;
            if (tg != g) { tg.Files++; p.Misplaced.Add(rel); }
            if (kind == Hs2SourceKind.Coord) OneCoord(p.Importer, p.Expect, data, rel, tg);
            else OneChara(p.Importer, p.Expect, data, rel, tg, kind == Hs2SourceKind.None);
        }
        catch (Exception e)
        {
            a1Fail++;
            Mismatch(rel, "EXCEPTION " + e.GetType().Name + ": " + e.Message + " @ " + e.StackTrace?.Split('\n').FirstOrDefault()?.Trim());
        }
    }

    /// <summary>The pass's (passed, failed) checks after <see cref="End"/>.</summary>
    internal static (int passed, int failed) Counts => (pass, fail);

    /// <summary>The verdicts, the census and the report (import_report_full.txt / import_report.txt, or <paramref name="reportFile"/>);
    /// <paramref name="exit"/>: end a batch-mode process with the verdict (Run / RunFull).</summary>
    internal static void End(Pass p, bool exit, string reportFile = null)
    {
        bool full = p.Full;
        var total = p.Total;
        var gd = p.Gd;
        var groups = p.Groups;
        var coordInCharaFolders = p.CoordInCharaFolders;
        var charaInCoordFolder = p.CharaInCoordFolder;
        var misplaced = p.Misplaced;
        // ---------- verdicts
        var head = new StringBuilder();
        foreach (var (g, _) in groups.Concat(new[] { (coordInCharaFolders, ""), (charaInCoordFolder, "") }))
            head.AppendLine($"{g.Name}: files {g.Files}, character cards {g.Chara}, coordinates {g.Coord}, rejected {g.Rejected} (not a card {g.NotCards})");
        if (charaInCoordFolder.Files > 0) head.AppendLine("character cards in the coordinate folder: " + string.Join(", ", misplaced.Where(x => x.StartsWith("coordinate/", StringComparison.Ordinal))));
        sb.Insert(0, head.ToString());
        int rejectedChara = groups[0].g.Rejected + groups[1].g.Rejected + charaInCoordFolder.Rejected - groups[0].g.NotCards - groups[1].g.NotCards - groups[2].g.NotCards;
        int rejectedCoord = groups[2].g.Rejected + coordInCharaFolders.Rejected;
        Check("A1 no exception, every accepted file saved and reloaded", a1Fail == 0, a1Fail + " failures");
        Check("A1 rejections only with card.* codes", RejectCodes.Keys.All(k => k.StartsWith("card.", StringComparison.Ordinal)), string.Join(", ", RejectCodes.Select(kv => kv.Key + " " + kv.Value)));
        if (full)
        {
            Check("A1 rejected character cards " + Arg("-expectRejectedChara", "0"), rejectedChara == int.Parse(Arg("-expectRejectedChara", "0")), rejectedChara.ToString());
            Check("A1 rejected coordinates " + Arg("-expectRejectedCoord", "0"), rejectedCoord == int.Parse(Arg("-expectRejectedCoord", "0")), rejectedCoord.ToString());
            Check("A1 character cards 859 female + 98 male", groups[0].g.Chara == 859 && groups[1].g.Chara == 98, groups[0].g.Chara + " + " + groups[1].g.Chara);
            Check("A1 coordinates 1,123 imported", groups[2].g.Coord == 1123, groups[2].g.Coord.ToString());
        }
        Check("A1 save → load → save byte-identical", roundTripFail == 0, roundTripFail + " differ");
        Check("A2 field-by-field equality", mismatchCount - a1Fail == 0, (mismatchCount - a1Fail) + " mismatches");
        Check("A3 raw copies and index", a3Fail == 0, a3Fail + " mismatches");
        Check("A6 re-import gives identical bytes", a6Fail == 0, a6Fail + " differ");

        var f = groups[0].g; var m = groups[1].g; var c = groups[2].g;
        long type120Cards = f.C("accessory.type120") + m.C("accessory.type120"), type120Coord = c.C("accessory.type120");
        long nz = f.C("float.negativeZero.vanilla") + m.C("float.negativeZero.vanilla") + c.C("float.negativeZero.vanilla");
        long dropped = f.C("hair.bundlesDropped") + m.C("hair.bundlesDropped");
        sb.AppendLine($"census: accessory.type120 cards {type120Cards}, coordinates {type120Coord}; coord.sex {c.C("coord.sex.coordinateSex")} / {c.C("coord.sex.resolveInfo")} / {c.C("coord.sex.default")}; " +
                      $"hair.bundlesDropped {dropped}; float.negativeZero vanilla {nz} (plugin {f.C("float.negativeZero.plugin") + m.C("float.negativeZero.plugin") + c.C("float.negativeZero.plugin")})");
        if (full)
        {
            Check("A5 accessory.type120 on 3 card slots and 39 coordinate slots", type120Cards == 3 && type120Coord == 39, type120Cards + " / " + type120Coord);
            Check("A5 coord.sex 616 / 397 / 110", c.C("coord.sex.coordinateSex") == 616 && c.C("coord.sex.resolveInfo") == 397 && c.C("coord.sex.default") == 110);
            Check("A5 hair.bundlesDropped 0", dropped == 0, dropped.ToString());
            Check("A5 float.negativeZero >= 899 (vanilla objects)", nz >= 899, nz.ToString());
        }
        else Check("A5 hair.bundlesDropped 0 (sample)", dropped == 0, dropped.ToString());

        // ---------- timing and counts
        sb.AppendLine();
        sb.AppendLine("# timing (ms; the first file, which carries the JIT warm-up, left out)");
        sb.AppendLine($"import  {Stats(ImportMs)}");
        sb.AppendLine($"save    {Stats(SaveMs)}   (ToBytes, placeholder thumbnail)");
        sb.AppendLine($"load    {Stats(LoadMs)}");
        sb.AppendLine($"total wall time {total.Elapsed.TotalSeconds:F1} s; migration cases (not verified by A2) {migrationCases}");
        sb.AppendLine();
        sb.AppendLine("# counts per folder");
        foreach (var (g, _) in groups.Concat(new[] { (coordInCharaFolders, ""), (charaInCoordFolder, "") }))
            sb.AppendLine($"{g.Name}: " + string.Join(", ", g.Counts.Select(kv => kv.Key + " " + kv.Value)));
        sb.AppendLine();
        sb.AppendLine("# finding codes (files)");
        foreach (var kv in FindingCodes) sb.AppendLine($"  {kv.Key,-34} {kv.Value}");
        sb.AppendLine();
        sb.AppendLine("# finding examples (first 3 per code)");
        foreach (var kv in Examples) foreach (var l in kv.Value) sb.AppendLine($"  {kv.Key}: {l}");
        sb.AppendLine();
        sb.AppendLine($"# mismatches (first {Mismatches.Count} of {mismatchCount})");
        foreach (var l in Mismatches) sb.AppendLine("  " + l);
        sb.Insert(0, $"{pass} passed, {fail} failed\n");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, reportFile ?? (full ? "import_report_full.txt" : "import_report.txt")), sb.ToString().Replace("\r\n", "\n"));
        gd.UnloadAll();
        Debug.Log($"IMPORT TEST DONE: {pass} passed, {fail} failed");
        if (exit && Application.isBatchMode) TestExit.Exit(fail == 0 ? 0 : 1);
    }

    // ------------------------------------------------------------------ unit checks (both modes)

    static Card.ResolveInfo Entry(string guid, string prop, int slot, int cat) => new Card.ResolveInfo { GUID = guid, Property = prop, Slot = slot, Category = cat };

    static void Units(Hs2Importer importer, ContentCatalog gd, TestTargets targets)
    {
        var none = new List<Card.ResolveInfo>();
        string R(int v, string p, int c, List<Card.ResolveInfo> e, ImportReport rep = null) => importer.ResolveRef(v, p, c, e, rep);
        // and 2
        Check("R: KCOX custom pattern 58947543 in category 348", R(58947543, "ChaFileClothes.ClothesTopPattern0", 348, none) == "hs2.kcox#58947543");
        Check("R: base-game row", R(0, "ChaFileFace.headId", 210, none) == "illusion.hs2#0");
        var rmv = new ImportReport();
        Check("R: id missing on this install -> base-game reference, resolve.missingVanilla", R(12345, null, 210, none, rmv) == "illusion.hs2#12345" && rmv.Findings.Has("resolve.missingVanilla"));
        var run = new ImportReport();
        Check("R: 99999999, >= 100000000 and < 0 -> hs2.unresolved", R(99999999, null, 210, none, run) == "hs2.unresolved#99999999" && R(100000005, null, 211, none) == "hs2.unresolved#100000005"
            && R(-1, null, 240, none) == "hs2.unresolved#-1" && run.Findings.Has("resolve.unresolved"));
        var gated = R(15, "ChaFileClothes.ClothesShoes", 247, none);
        // with the AI-Shoujo flags opened by the settings (C9 since Phase 4.0) the distributionNo-gated row is listed and
        // resolves as a base-game row (HS2 on the reference install, H1); with the gate closed it takes the compatibility path
        if (gd.TryGetEntry(247, ContentId.Vanilla(15), out _))
            Check("R: a distributionNo-gated base-game id listed by the open gate (C9) resolves to itself (247#15)", gated == "illusion.hs2#15", gated);
        else
            Check("R: a gated base-game id takes the compatibility path (247#15)", !gated.StartsWith("illusion.hs2#", StringComparison.Ordinal), gated);
        // step 1
        var rm = new ImportReport();
        Check("R: entry of a GUID not installed -> kept, resolve.missingMod", R(7, "P", 210, new List<Card.ResolveInfo> { Entry(" hs2next.test.missing ", "P", 7, 210) }, rm) == "hs2next.test.missing#7" && rm.Findings.Has("resolve.missingMod"));
        var some = gd.ZipmodsByGuid.Values.Where(z => z.Entries.Count > 0).OrderBy(z => z.Guid, StringComparer.Ordinal).First();
        var row = some.Entries[0];
        var rc = new ImportReport();
        Check("R: entry of an installed row; category mismatch reported, entry used; card value ≠ slot reported",
            R(123, "P", row.Category, new List<Card.ResolveInfo> { Entry(some.Guid, "P", row.Key.Id, row.Category + 1) }, rc) == some.Guid + "#" + row.Key.Id
            && rc.Findings.Has("resolve.categoryMismatch") && rc.Findings.Has("resolve.slotMismatch") && !rc.Findings.Has("resolve.missingMod"));
        Check("R: first entry with exactly the property (no category or prefix shortcut)",
            R(0, "outfit.ChaFileClothes.ClothesTop", 240, new List<Card.ResolveInfo> { Entry("a.first", "ChaFileClothes.ClothesTop", 1, 240), Entry("b.second", "outfit.ChaFileClothes.ClothesTop", 2, 240), Entry("c.third", "outfit.ChaFileClothes.ClothesTop", 3, 240) }) == "b.second#2");
        Check("R: blank ModID -> compatibility path", R(0, "P", 210, new List<Card.ResolveInfo> { Entry("  ", "P", 9, 210) }) == "illusion.hs2#0");
        // <migrationInfo> (open point 15.3), with the entries of this install where their mods are installed
        foreach (var m in gd.Migrations)
        {
            var rr = new ImportReport();
            if (m.Kind == MigrationKind.Migrate && gd.ZipmodsByGuid.ContainsKey(m.GuidNew))
                Check($"R: id migration {m}", R(m.IdOld, "ChaFileBody.skinId", m.Category, new List<Card.ResolveInfo> { Entry(m.GuidOld, "ChaFileBody.skinId", m.IdOld, m.Category) }, rr) == m.GuidNew + "#" + m.IdNew && rr.Findings.Has("resolve.migrated"));
            if (m.Kind == MigrationKind.MigrateAll)
            {
                var first = gd.Migrations.First(x => x.Kind == MigrationKind.MigrateAll && x.GuidOld == m.GuidOld && gd.ZipmodsByGuid.ContainsKey(x.GuidNew));
                Check($"R: GUID migration {m.GuidOld} -> the first installed target ({first.GuidNew})", R(4, "P", 210, new List<Card.ResolveInfo> { Entry(m.GuidOld, "P", 4, 210) }, rr) == first.GuidNew + "#4" && rr.Findings.Has("resolve.migrated"));
            }
        }

        // the scene hook (ICharaImporter) and the option on the female visual target
        var path = TestTargets.Abs(targets.visual.female);
        var bytes = File.ReadAllBytes(path);
        string sha;
        using (var h = SHA256.Create()) sha = string.Concat(h.ComputeHash(bytes).Select(b => b.ToString("x2")));
        var src = new HS2Next.Scenes.CardAppearanceSource { Hs2Path = "UserData/" + targets.visual.female, Sha256 = sha, Sex = 1, ReadBytes = () => bytes };
        var findings = new FindingList();
        HS2Next.Scenes.ICharaImporter hook = importer;
        var cc = hook.Import(src, findings);
        Check("hook: ICharaImporter imports the card source", cc != null && cc.Subtree.Import.Sha256 == sha && cc.Subtree.Import.FileName == Path.GetFileName(path));
        Check("hook: findings carry the file name in their paths, no sourceChanged", findings.All(f => f.Path.StartsWith(Path.GetFileName(path) + ": ", StringComparison.Ordinal)) && !findings.Has("import.sourceChanged"));
        Check("hook: accessory ids = CardAppearanceSource.AccessoryId(sha, slot)", cc != null && cc.Coord.Accessories.All(a => a.Id == HS2Next.Scenes.CardAppearanceSource.AccessoryId(sha, a.ImportSlot.Value)));
        var src2 = new HS2Next.Scenes.CardAppearanceSource { Hs2Path = src.Hs2Path, Sha256 = new string('0', 64), ReadBytes = () => bytes };
        var f2 = new FindingList();
        hook.Import(src2, f2);
        Check("hook: other bytes than the recorded SHA-256 -> import.sourceChanged", f2.Has("import.sourceChanged"));
        Check("hook: the 2.2 hook stays unassigned (wave 2 wires it)", HS2Next.Scenes.CharaImport.Importer == null);
        var card = Card.Load(bytes, path);
        var state = new CharaNodeState();
        Check("card state fills the scene state", cc != null && CardStateImport.Apply(card, cc.Coord, state)
            && state.State.Accessories.Count == cc.Coord.Accessories.Count(a => a.ImportSlot < 20)
            && state.Look.Neck == (card.Status.Get("neckLookPtn") is long nl ? (int)nl : 0)
            && state.Face.Blink == (card.Status.Get("eyesBlink") is bool eb ? eb : true));
        // requires details from the catalog's zipmod manifests (or the resolve entries)
        if (cc != null)
        {
            var saved = CharaContainer.Load(cc.ToBytes(SaveOpts())).Value;
            var req = saved.LoadedManifest.Requires;
            Check("requires: every installed GUID carries its manifest name", req.Count > 0 && req.All(r => !gd.ZipmodsByGuid.TryGetValue(r.Guid, out var z) || z.Name == null || r.Name == z.Name),
                req.Count + " entries");
        }
        // thumbnail hook
        var withThumb = new Hs2Importer(importer.Resolve, new ImportOptions { Clock = () => Fixed, Thumbnails = new FixedThumbnail() });
        var tc = withThumb.ImportChara(bytes, Path.GetFileName(path));
        Check("thumbnail hook: the renderer's PNG becomes the file's thumbnail", tc.Container != null && tc.Container.Thumbnail == FixedThumbnail.Png
            && CharaContainer.Load(tc.Container.ToBytes(new SaveOptions { Clock = () => Fixed, Thumbnail = ThumbnailMode.Keep })).Value.Thumbnail.SequenceEqual(FixedThumbnail.Png));
        // rejected sources
        var notCard = importer.ImportChara(Encoding.ASCII.GetBytes("not a card at all"), "x.png");
        Check("rejection: not a card -> card.notACard, no container", notCard.Container == null && notCard.Report.Findings.Has("card.notACard") && notCard.Report.Status == ImportStatus.Failed);
        var cut = importer.ImportChara(bytes.Take(Card.PngEnd(bytes) + 300).ToArray(), "cut.png");
        Check("rejection: a card cut in half -> card.truncated", cut.Container == null && cut.Report.Findings.Has("card.truncated"), string.Join(",", cut.Report.Findings.Select(x => x.Code)));
    }

    sealed class FixedThumbnail : IImportThumbnailRenderer
    {
        public static readonly byte[] Png = ThumbnailPlaceholder.Png();
        public byte[] RenderChara(CharaContainer chara) => Png;
        public byte[] RenderCoord(CoordContainer coord) => Png;
    }

    static string Stats(List<double> v)
    {
        if (v.Count < 2) return "n " + v.Count;
        var s = v.Skip(1).OrderBy(x => x).ToList();     // the first file carries the JIT warm-up
        return $"n {s.Count}, mean {s.Average():F1}, p95 {s[(int)Math.Min(s.Count - 1, Math.Ceiling(s.Count * 0.95) - 1)]:F1}, max {s[s.Count - 1]:F1}";
    }

    static SaveOptions SaveOpts() => new SaveOptions { Clock = () => Fixed, Thumbnail = ThumbnailMode.Placeholder, Verify = false };

    static void Codes(ImportReport r)
    {
        foreach (var code in r.Findings.Select(x => x.Code).Distinct())
        {
            FindingCodes.TryGetValue(code, out int n);
            FindingCodes[code] = n + 1;
        }
        foreach (var f in r.Findings)
        {
            if (!Examples.TryGetValue(f.Code, out var l)) Examples[f.Code] = l = new List<string>();
            if (l.Count < 3) l.Add($"{r.SourcePath}: {f.Path}: {f.Message}{(f.Count > 1 ? " (x" + f.Count + ")" : "")}");
        }
    }

    // ------------------------------------------------------------------ one character card

    static void OneChara(Hs2Importer importer, Expect ex, byte[] data, string rel, Group g, bool notCard)
    {
        var r = importer.ImportChara(data, Path.GetFileName(rel), rel);
        Codes(r.Report);
        if (r.Container == null)
        {
            g.Rejected++;
            if (notCard) g.NotCards++;
            foreach (var code in r.Report.Findings.Where(x => x.Severity == FindingSeverity.Error).Select(x => x.Code).Distinct())
            {
                RejectCodes.TryGetValue(code, out int n);
                RejectCodes[code] = n + 1;
            }
            return;
        }
        g.Chara++;
        g.Add(r.Report);
        ImportMs.Add(r.Report.Milliseconds);
        var sw = Stopwatch.StartNew();
        var bytes = r.Container.ToBytes(SaveOpts());
        SaveMs.Add(sw.Elapsed.TotalMilliseconds);
        sw.Restart();
        var loaded = CharaContainer.Load(bytes);
        LoadMs.Add(sw.Elapsed.TotalMilliseconds);
        if (!loaded.Ok) { a1Fail++; Mismatch(rel, "reload failed: " + loaded.Error.Message); return; }
        if (loaded.Findings.HasErrors) { a1Fail++; Mismatch(rel, "reload findings: " + loaded.Findings); }
        if (!loaded.Value.ToBytes(SaveOpts()).SequenceEqual(bytes)) { roundTripFail++; Mismatch(rel, "save → load → save differs"); }
        var again = importer.ImportChara(data, Path.GetFileName(rel), rel);
        if (again.Container == null || !again.Container.ToBytes(SaveOpts()).SequenceEqual(bytes)) { a6Fail++; Mismatch(rel, "re-import bytes differ"); }
        ex.Chara(r.Card, r.Ext, data, loaded.Value, rel, ref migrationCases);
        a3Fail += ex.Raw(r.Ext, r.Card.KKExVersion, loaded.Value.Subtree.CharaExt.Kkex, loaded.Value.Subtree.CoordExt.Kkex, "chara", rel);
    }

    static void OneCoord(Hs2Importer importer, Expect ex, byte[] data, string rel, Group g)
    {
        var r = importer.ImportCoord(data, Path.GetFileName(rel), rel);
        Codes(r.Report);
        if (r.Container == null)
        {
            g.Rejected++;
            foreach (var code in r.Report.Findings.Where(x => x.Severity == FindingSeverity.Error).Select(x => x.Code).Distinct())
            {
                RejectCodes.TryGetValue(code, out int n);
                RejectCodes[code] = n + 1;
            }
            return;
        }
        g.Coord++;
        g.Add(r.Report);
        ImportMs.Add(r.Report.Milliseconds);
        var sw = Stopwatch.StartNew();
        var bytes = r.Container.ToBytes(SaveOpts());
        SaveMs.Add(sw.Elapsed.TotalMilliseconds);
        sw.Restart();
        var loaded = CoordContainer.Load(bytes);
        LoadMs.Add(sw.Elapsed.TotalMilliseconds);
        if (!loaded.Ok) { a1Fail++; Mismatch(rel, "reload failed: " + loaded.Error.Message); return; }
        if (loaded.Findings.HasErrors) { a1Fail++; Mismatch(rel, "reload findings: " + loaded.Findings); }
        if (!loaded.Value.ToBytes(SaveOpts()).SequenceEqual(bytes)) { roundTripFail++; Mismatch(rel, "save → load → save differs"); }
        var again = importer.ImportCoord(data, Path.GetFileName(rel), rel);
        if (again.Container == null || !again.Container.ToBytes(SaveOpts()).SequenceEqual(bytes)) { a6Fail++; Mismatch(rel, "re-import bytes differ"); }
        ex.Coord(r.Card, r.Ext, data, loaded.Value, rel);
        a3Fail += ex.Raw(r.Ext, "3", loaded.Value.CoordExt.Kkex, loaded.Value.CoordExt.Kkex, "coord", rel);
    }

    // =====================================================================================================================
    // The independent evaluation (A2, A3): HS2's raw values from the card trees with MemberInit values, the mapping
    // written out per field, references through R(v, P, c) evaluated against the catalog here.
    // =====================================================================================================================

    internal sealed class Expect
    {
        readonly ContentCatalog gd;
        string file;
        ResStore res;

        public Expect(ContentCatalog gd) { this.gd = gd; }

        // ---------- raw value readers (MemberInit default when the key is missing)
        static bool IsNum(object o) => o is float || o is double || o is long || o is ulong;
        static float ToF(object o) => o is float f ? f : o is double d ? (float)d : o is long l ? l : o is ulong u ? u : 0f;
        static float F(object m, string k, float def) => m.Has(k) && IsNum(m.Get(k)) ? ToF(m.Get(k)) : def;
        static int I(object m, string k, int def) => m.Has(k) && m.Get(k) is long l ? (int)l : def;
        static bool B(object m, string k, bool def) => m.Has(k) && m.Get(k) is bool b ? b : def;
        static float[] Arr(object m, string k, int n, float[] def)
        {
            if (!(m.Get(k) is List<object> l) || l.Count < n) return def;
            var r = new float[n];
            for (int i = 0; i < n; i++) r[i] = ToF(l[i]);
            return r;
        }
        static object At(object m, string k, int i) => m.Get(k).At(i);

        // Bit-equal except that +0 and -0 are equal (the format normalises -0): float equality does exactly that.
        static bool Same(float a, float b) => a == b;

        void F(string p, float actual, float expected) { if (!Same(actual, expected)) Mismatch(file, $"{p}: {actual:R} != {expected:R}"); }
        void I(string p, long actual, long expected) { if (actual != expected) Mismatch(file, $"{p}: {actual} != {expected}"); }
        void B(string p, bool actual, bool expected) { if (actual != expected) Mismatch(file, $"{p}: {actual} != {expected}"); }
        void S(string p, string actual, string expected) { if (actual != expected) Mismatch(file, $"{p}: \"{actual}\" != \"{expected}\""); }
        void C(string p, Color4 a, float[] e) { F(p + ".r", a.R, e[0]); F(p + ".g", a.G, e[1]); F(p + ".b", a.B, e[2]); F(p + ".a", a.A, e[3]); }
        void C(string p, Color4 a, object m, string k, params float[] def) => C(p, a, Arr(m, k, 4, def));
        void V4(string p, Vec4 a, object m, string k, params float[] def) { var e = Arr(m, k, 4, def); F(p + ".x", a.X, e[0]); F(p + ".y", a.Y, e[1]); F(p + ".z", a.Z, e[2]); F(p + ".w", a.W, e[3]); }
        void V3(string p, Vec3 a, float[] e) { F(p + ".x", a.X, e[0]); F(p + ".y", a.Y, e[1]); F(p + ".z", a.Z, e[2]); }
        void V3(string p, Vec3 a, Vector3 e) { F(p + ".x", a.X, e.x); F(p + ".y", a.Y, e.y); F(p + ".z", a.Z, e.z); }
        static readonly float[] White = { 1, 1, 1, 1 };

        // ---------- R(v, P, c), from the catalog
        List<Card.ResolveInfo> resolve;
        HashSet<int> usedEntries;

        string Ref(int v, string prop, int c)
        {
            if (c == 348 && v == 58947543) return "hs2.kcox#58947543";
            int ei = prop == null ? -1 : resolve.FindIndex(e => e.Property == prop);
            if (ei >= 0)
            {
                usedEntries.Add(ei);
                var e = resolve[ei];
                string guid = e.GUID?.Trim();
                int slot = e.Slot;
                if (!string.IsNullOrEmpty(guid))
                {
                    var mig = gd.Migrations.Where(x => x.GuidOld == guid).ToList();
                    if (mig.Any(x => x.Kind == MigrationKind.StripAll)) guid = "";
                    else
                    {
                        var idm = mig.FirstOrDefault(x => x.Kind == MigrationKind.Migrate && x.IdOld == slot && x.Category == e.Category && gd.ZipmodsByGuid.ContainsKey(x.GuidNew));
                        if (idm != null) { guid = idm.GuidNew; slot = idm.IdNew; }
                        else
                        {
                            var all = mig.FirstOrDefault(x => x.Kind == MigrationKind.MigrateAll && gd.ZipmodsByGuid.ContainsKey(x.GuidNew));
                            if (all != null) guid = all.GuidNew;
                        }
                    }
                }
                if (!string.IsNullOrEmpty(guid)) return guid + "#" + slot.ToString(CultureInfo.InvariantCulture);
            }
            if (gd.TryGetEntry(c, ContentId.Vanilla(v), out _)) return "illusion.hs2#" + v.ToString(CultureInfo.InvariantCulture);
            var cand = gd.EntriesOf(c).Where(x => !x.IsVanilla && x.Key.Id == v).Select(x => x.Key.SourceGuid).ToList();
            if (cand.Count > 0) return cand.OrderBy(x => x, StudioCsv.LoadOrder).First() + "#" + v.ToString(CultureInfo.InvariantCulture);
            var byId = gd.Migrations.FirstOrDefault(x => x.IdOld == v && x.Category == c);
            if (byId != null && gd.TryGetEntry(c, new ContentId(byId.GuidNew, byId.IdNew), out _)) return byId.GuidNew + "#" + byId.IdNew.ToString(CultureInfo.InvariantCulture);
            if (v == 99999999 || v >= 100000000 || v < 0) return "hs2.unresolved#" + v.ToString(CultureInfo.InvariantCulture);
            return "illusion.hs2#" + v.ToString(CultureInfo.InvariantCulture);
        }

        void R(string p, string actual, object m, string k, string prop, int c) => S(p, actual, Ref(I(m, k, 0), prop, c));

        // ================================================================= character card

        public void Chara(Card card, CardExtData ext, byte[] data, CharaContainer c, string rel, ref int migrations)
        {
            file = rel;
            res = c.Res;
            resolve = card.KKEx == null || card.KKExVersion == "3" ? card.Resolve : new List<Card.ResolveInfo>();
            usedEntries = new HashSet<int>();
            if (OldVersion(card.Face, "0.0.2") || OldVersion(card.Body, "0.0.1") || OldVersion(card.Hair, "0.0.3") || OldVersion(card.Parameter, "0.0.1")) { migrations++; return; }
            var d = c.Chara;
            var par = card.Parameter;
            int sex = I(par, "sex", 0);
            if (sex != 0 && sex != 1) sex = 1;
            bool male = sex == 0;

            // profile
            S("profile.name", d.Profile.Name, par.Get("fullname") as string ?? "");
            I("profile.sex", d.Profile.Sex, sex);
            B("profile.futanari", d.Profile.Futanari, B(par, "futanari", false));
            I("profile.hs2.personality", d.Profile.Hs2.Personality, I(par, "personality", 0));
            I("profile.hs2.birthMonth", d.Profile.Hs2.BirthMonth, I(par, "birthMonth", 1));
            I("profile.hs2.birthDay", d.Profile.Hs2.BirthDay, I(par, "birthDay", 1));
            F("profile.hs2.voiceRate", d.Profile.Hs2.VoiceRate, F(par, "voiceRate", 0.5f));
            S("profile.hs2.wishes", string.Join(",", d.Profile.Hs2.Wishes.OrderBy(x => x)), string.Join(",", par.Get("hsWish").Items().Select(x => (int)(long)x).OrderBy(x => x)));
            card.Blocks.TryGetValue("Parameter2", out var p2);
            B("profile.hs2.parameter2 present", d.Profile.Hs2.Parameter2 != null, p2 != null);
            if (p2 != null && d.Profile.Hs2.Parameter2 != null)
            {
                var q = d.Profile.Hs2.Parameter2;
                I("parameter2.personality", q.Personality, I(p2, "personality", 0));
                F("parameter2.voiceRate", q.VoiceRate, F(p2, "voiceRate", 0.5f));
                I("parameter2.trait", q.Trait, I(p2, "trait", 0));
                I("parameter2.mind", q.Mind, I(p2, "mind", 0));
                I("parameter2.hAttribute", q.HAttribute, I(p2, "hAttribute", 0));
            }

            // face
            var fa = card.Face;
            var fd = d.Face;
            Shape("face.shape", fd.Shape, fa.Get("shapeValueFace"), 59, 0.5f);
            R("face.head", fd.Head, fa, "headId", "ChaFileFace.headId", male ? 110 : 210);
            R("face.skin", fd.Skin, fa, "skinId", "ChaFileFace.skinId", male ? 111 : 211);
            R("face.detail.item", fd.Detail.Item, fa, "detailId", "ChaFileFace.detailId", male ? 112 : 212);
            F("face.detail.power", fd.Detail.Power, F(fa, "detailPower", 1f));
            R("face.eyebrow.item", fd.Eyebrow.Item, fa, "eyebrowId", "ChaFileFace.eyebrowId", 314);
            C("face.eyebrow.color", fd.Eyebrow.Color, fa, "eyebrowColor", 0.05f, 0.05f, 0.05f, 1f);
            V4("face.eyebrow.layout", fd.Eyebrow.Layout, fa, "eyebrowLayout", 0.5f, 0.375f, 0.666f, 0.666f);
            F("face.eyebrow.tilt", fd.Eyebrow.Tilt, F(fa, "eyebrowTilt", 0.5f));
            for (int i = 0; i < 2; i++)
            {
                var pm = At(fa, "pupil", i);
                var e = i == 0 ? fd.Eyes.Left : fd.Eyes.Right;
                string p = "face.eyes." + (i == 0 ? "left" : "right");
                C(p + ".white", e.White, pm, "whiteColor", 0.846f, 0.846f, 0.846f, 1f);
                R(p + ".iris.item", e.Iris.Item, pm, "pupilId", "ChaFileFace.Eye" + (i + 1), 317);
                C(p + ".iris.color", e.Iris.Color, pm, "pupilColor", 0.33f, 0.33f, 0.33f, 1f);
                F(p + ".iris.size.x", e.Iris.Size.X, F(pm, "pupilW", 0.666f));
                F(p + ".iris.size.y", e.Iris.Size.Y, F(pm, "pupilH", 0.666f));
                F(p + ".iris.emission", e.Iris.Emission, F(pm, "pupilEmission", 0f));
                R(p + ".pupil.item", e.Pupil.Item, pm, "blackId", "ChaFileFace.EyeBlack" + (i + 1), 318);
                C(p + ".pupil.color", e.Pupil.Color, pm, "blackColor", 0f, 0f, 0f, 1f);
                F(p + ".pupil.size.x", e.Pupil.Size.X, F(pm, "blackW", 0.8333f));
                F(p + ".pupil.size.y", e.Pupil.Size.Y, F(pm, "blackH", 0.8333f));
            }
            B("face.eyes.linked", fd.Eyes.Linked, B(fa, "pupilSameSetting", true));
            F("face.eyes.offsetY", fd.Eyes.OffsetY, F(fa, "pupilY", 0.5f));
            F("face.eyes.shadowScale", fd.Eyes.ShadowScale, F(fa, "whiteShadowScale", 0.5f));
            R("face.eyes.highlight.item", fd.Eyes.Highlight.Item, fa, "hlId", "ChaFileFace.hlId", 319);
            C("face.eyes.highlight.color", fd.Eyes.Highlight.Color, fa, "hlColor", White);
            V4("face.eyes.highlight.layout", fd.Eyes.Highlight.Layout, fa, "hlLayout", 0.5f, 0.5f, 0.5f, 0.5f);
            F("face.eyes.highlight.tilt", fd.Eyes.Highlight.Tilt, F(fa, "hlTilt", 0.5f));
            R("face.eyelashes.item", fd.Eyelashes.Item, fa, "eyelashesId", "ChaFileFace.eyelashesId", 315);
            C("face.eyelashes.color", fd.Eyelashes.Color, fa, "eyelashesColor", 0.19f, 0.19f, 0.19f, 1f);
            R("face.mole.item", fd.Mole.Item, fa, "moleId", "ChaFileFace.moleId", 323);
            C("face.mole.color", fd.Mole.Color, fa, "moleColor", 0f, 0f, 0f, 1f);
            V4("face.mole.layout", fd.Mole.Layout, fa, "moleLayout", 0.5f, 0.5f, 0.25f, 0.5f);
            var mk = fa.Get("makeup");
            foreach (var (name, cat, layer) in new[] { ("eyeshadow", 316, fd.Makeup.Eyeshadow), ("cheek", 320, fd.Makeup.Cheek), ("lip", 322, fd.Makeup.Lip) })
            {
                R("face.makeup." + name + ".item", layer.Item, mk, name + "Id", "MakeupInfo." + name + "Id", cat);
                C("face.makeup." + name + ".color", layer.Color, mk, name + "Color", White);
                F("face.makeup." + name + ".gloss", layer.Gloss, F(mk, name + "Gloss", 0f));
            }
            for (int i = 0; i < 2; i++) Paint("face.makeup.paint[" + i + "]", fd.Makeup.Paint[i], At(mk, "paintInfo", i), "MakeupInfo", i, 7);
            R("face.beard.item", fd.Beard.Item, fa, "beardId", male ? "ChaFileFace.beardId" : null, 121);
            C("face.beard.color", fd.Beard.Color, fa, "beardColor", 0.19f, 0.19f, 0.19f, 1f);

            // body
            var bo = card.Body;
            var bd = d.Body;
            Shape("body.shape", bd.Shape, bo.Get("shapeValueBody"), 33, 0.5f);
            F("body.bust.softness", bd.Bust.Softness, F(bo, "bustSoftness", 0.5f));
            F("body.bust.weight", bd.Bust.Weight, F(bo, "bustWeight", 0.5f));
            R("body.skin.item", bd.Skin.Item, bo, "skinId", "ChaFileBody.skinId", male ? 131 : 231);
            C("body.skin.color", bd.Skin.Color, bo, "skinColor", 0.8f, 0.7f, 0.64f, 1f);
            F("body.skin.gloss", bd.Skin.Gloss, F(bo, "skinGlossPower", 0.7f));
            F("body.skin.metallic", bd.Skin.Metallic, F(bo, "skinMetallicPower", 0f));
            R("body.detail.item", bd.Detail.Item, bo, "detailId", "ChaFileBody.detailId", male ? 132 : 232);
            F("body.detail.power", bd.Detail.Power, F(bo, "detailPower", 0.5f));
            R("body.sunburn.item", bd.Sunburn.Item, bo, "sunburnId", "ChaFileBody.sunburnId", male ? 133 : 233);
            C("body.sunburn.color", bd.Sunburn.Color, bo, "sunburnColor", White);
            for (int i = 0; i < 2; i++) Paint("body.paint[" + i + "]", bd.Paint[i], At(bo, "paintInfo", i), "ChaFileBody", i, 8);
            R("body.nipple.item", bd.Nipple.Item, bo, "nipId", "ChaFileBody.nipId", 334);
            C("body.nipple.color", bd.Nipple.Color, bo, "nipColor", 0.76f, 0.52f, 0.52f, 1f);
            F("body.nipple.gloss", bd.Nipple.Gloss, F(bo, "nipGlossPower", 0.6f));
            F("body.nipple.areola", bd.Nipple.Areola, F(bo, "areolaSize", 0.7f));
            R("body.underhair.item", bd.Underhair.Item, bo, "underhairId", "ChaFileBody.underhairId", 335);
            C("body.underhair.color", bd.Underhair.Color, bo, "underhairColor", 0.05f, 0.05f, 0.05f, 1f);
            C("body.nail.color", bd.Nail.Color, bo, "nailColor", 1f, 0.92f, 0.92f, 1f);
            F("body.nail.gloss", bd.Nail.Gloss, F(bo, "nailGlossPower", 0.6f));

            // hair
            var ha = card.Hair;
            var hd = d.Hair;
            I("hair.shaderType", hd.ShaderType, I(ha, "shaderType", 0));
            B("hair.editor.sameSetting", hd.Editor.SameSetting, B(ha, "sameSetting", true));
            B("hair.editor.autoSetting", hd.Editor.AutoSetting, B(ha, "autoSetting", true));
            B("hair.editor.ctrlTogether", hd.Editor.CtrlTogether, B(ha, "ctrlTogether", false));
            string[] hp = { "HairBack", "HairFront", "HairSide", "HairOption" };
            for (int k = 0; k < 4; k++)
            {
                var pm = At(ha, "parts", k);
                var h = hd.Parts[k];
                string p = "hair." + HairData.PartNames[k];
                string item = Ref(I(pm, "id", 0), "ChaFileHair." + hp[k], 300 + k);
                S(p + ".item", h.Item, item);
                C(p + ".baseColor", h.BaseColor, pm, "baseColor", 0.2f, 0.2f, 0.2f, 1f);
                C(p + ".topColor", h.TopColor, pm, "topColor", 0.039f, 0.039f, 0.039f, 1f);
                C(p + ".underColor", h.UnderColor, pm, "underColor", 0.565f, 0.565f, 0.565f, 1f);
                C(p + ".specular", h.Specular, pm, "specular", 0.3f, 0.3f, 0.3f, 1f);
                F(p + ".metallic", h.Metallic, F(pm, "metallic", 0f));
                F(p + ".smoothness", h.Smoothness, F(pm, "smoothness", 0f));
                for (int i = 0; i < 4; i++) C(p + ".accessoryColors[" + i + "]", h.AccessoryColors[i], At(pm, "acsColorInfo", i), "color", White);
                var bundles = (pm.Get("dictBundle") as Dictionary<object, object>)?.OrderBy(kv => (long)kv.Key).ToList() ?? new List<KeyValuePair<object, object>>();
                bool drop = item.StartsWith("illusion.hs2#", StringComparison.Ordinal) && I(pm, "bundleId", -1) != I(pm, "id", 0);
                I(p + ".bundles.count", h.Bundles.Count, drop ? 0 : bundles.Count);
                for (int i = 0; i < h.Bundles.Count && i < bundles.Count && !drop; i++)
                {
                    var bm = bundles[i].Value;
                    I(p + ".bundles[" + i + "].index", h.Bundles[i].Index, (long)bundles[i].Key);
                    V3(p + ".bundles[" + i + "].move", h.Bundles[i].Move, Arr(bm, "moveRate", 3, new float[3]));
                    V3(p + ".bundles[" + i + "].rotate", h.Bundles[i].Rotate, Arr(bm, "rotRate", 3, new float[3]));
                    B(p + ".bundles[" + i + "].noShake", h.Bundles[i].NoShake, B(bm, "noShake", false));
                }
                R(p + ".mesh.item", h.Mesh.Item, pm, "meshType", "ChaFileHair." + hp[k] + "Mesh", 306);
                C(p + ".mesh.color", h.Mesh.Color, pm, "meshColor", White);
                V4(p + ".mesh.layout", h.Mesh.Layout, pm, "meshLayout", 1f, 1f, 0f, 0f);
            }

            // coordinate (sex = the character's) and plugins
            Clothes(card.Clothes, c.Coord, sex, "outfit.", "coord/");
            S("coord.name", c.Coord.Name, "");
            I("coord.sex", c.Coord.Sex, sex);
            Accessories(card.Accessory, ext, c.Coord, "outfit.", data, "coord/");
            Plugins(ext, d, c.Coord, false);
            var imp = c.Subtree.Import;
            S("import.from", imp.From, "hs2.chara");
            S("import.sha256", imp.Sha256, Sha(data));
            I("import.fileSize", imp.FileSize, data.LongLength);
            S("import.coordSex", imp.CoordSex, "character");
            S("import.hs2Version", imp.Hs2Version, card.Version);
            S("import.sideloaderGuids", string.Join("|", imp.SideloaderGuids), string.Join("|", Guids(card.Resolve)));
        }

        static bool OldVersion(object m, string current) =>
            m.Get("version") is string s && Version.TryParse(s, out var v) && v < new Version(current);

        static IEnumerable<string> Guids(List<Card.ResolveInfo> r) =>
            r.Select(e => e.GUID?.Trim()).Where(g => !string.IsNullOrEmpty(g)).Distinct(StringComparer.Ordinal).OrderBy(g => g, GuidOrder.Instance);

        static string Sha(byte[] d) { using (var s = SHA256.Create()) return string.Concat(s.ComputeHash(d).Select(b => b.ToString("x2"))); }

        void Shape(string p, float[] actual, object raw, int n, float def)
        {
            if (raw is List<object> l)
            {
                I(p + ".length", actual.Length, l.Count);
                for (int i = 0; i < l.Count && i < actual.Length; i++) F(p + "[" + i + "]", actual[i], ToF(l[i]));
            }
            else I(p + ".length", actual.Length, n);
        }

        void Paint(string p, Paint a, object m, string owner, int i, int layoutCat)
        {
            R(p + ".item", a.Item, m, "id", owner + ".PaintID" + (i + 1), 313);
            C(p + ".color", a.Color, m, "color", 1f, 0f, 0f, 1f);
            F(p + ".gloss", a.Gloss, F(m, "glossPower", 0.5f));
            F(p + ".metallic", a.Metallic, F(m, "metallicPower", 0.5f));
            R(p + ".layoutItem", a.LayoutItem, m, "layoutId", owner + ".PaintLayoutID" + (i + 1), layoutCat);
            V4(p + ".layout", a.Layout, m, "layout", 0.5f, 0.5f, 0.5f, 0.5f);
            F(p + ".rotation", a.Rotation, F(m, "rotation", 0.5f));
        }

        // ---------- clothes
        static readonly string[] FemaleProp = { "ClothesTop", "ClothesBot", "ClothesBra", "ClothesShorts", "ClothesGloves", "ClothesPantyhose", "ClothesSocks", "ClothesShoes" };
        static readonly string[] MaleProp = { "ClothesTopM", "ClothesBotM", "ClothesBra", "ClothesShorts", "ClothesGlovesM", "ClothesPantyhose", "ClothesSocks", "ClothesShoesM" };
        static readonly string[] Stem = { "Top", "Bot", "Bra", "Shorts", "Gloves", "Pantyhose", "Socks", "Shoes" };
        static readonly int[] FemaleCat = { 240, 241, 242, 243, 244, 245, 246, 247 }, MaleCat = { 140, 141, 242, 243, 144, 245, 246, 147 };

        void Clothes(object cl, CoordDocument d, int sex, string prefix, string pp)
        {
            for (int k = 0; k < 8; k++)
            {
                var pm = At(cl, "parts", k);
                var a = d.Clothes.Parts[k];
                string p = pp + "clothes." + ClothesSet.Names[k];
                R(p + ".item", a.Item, pm, "id", prefix + "ChaFileClothes." + (sex == 0 ? MaleProp : FemaleProp)[k], (sex == 0 ? MaleCat : FemaleCat)[k]);
                for (int j = 0; j < 3; j++)
                {
                    var cm = At(pm, "colorInfo", j);
                    var cc = a.Colors[j];
                    string q = p + ".colors[" + j + "]";
                    C(q + ".base", cc.Base, cm, "baseColor", White);
                    R(q + ".pattern", cc.Pattern, cm, "pattern", prefix + "ChaFileClothes.Clothes" + Stem[k] + "Pattern" + j, 348);
                    V4(q + ".layout", cc.Layout, cm, "layout", 1f, 1f, 0f, 0f);
                    F(q + ".rotation", cc.Rotation, F(cm, "rotation", 0.5f));
                    C(q + ".patternColor", cc.PatternColor, cm, "patternColor", White);
                    F(q + ".gloss", cc.Gloss, F(cm, "glossPower", 0.5f));
                    F(q + ".metallic", cc.Metallic, F(cm, "metallicPower", 0f));
                }
                F(p + ".breakRate", a.BreakRate, F(pm, "breakRate", 0f));
                for (int i = 0; i < 2; i++) B(p + ".hideOptions[" + i + "]", a.HideOptions[i], pm.Get("hideOpt").At(i) is bool b && b);
            }
        }

        // ---------- accessories
        void Accessories(object acc, CardExtData ext, CoordDocument d, string prefix, byte[] data, string pp)
        {
            string sha = Sha(data);
            var expected = new List<(int slot, int type, int id, object raw, MoreAccessoriesData.AccessoryPart more)>();
            var parts = acc.Get("parts") as List<object> ?? new List<object>();
            for (int s = 0; s < parts.Count; s++)
            {
                int type = I(parts[s], "type", 120);
                if (type >= 351 && type <= 363) expected.Add((s, type, I(parts[s], "id", 0), parts[s], null));
            }
            if (ext.MoreAccessories != null)
                foreach (var mp in ext.MoreAccessories.Parts)
                    if (mp.Type >= 351 && mp.Type <= 363) expected.Add((mp.Slot, mp.Type, mp.Id, null, mp));
            I(pp + "accessories.count", d.Accessories.Count, expected.Count);
            for (int n = 0; n < d.Accessories.Count && n < expected.Count; n++)
            {
                var a = d.Accessories[n];
                var (slot, type, id, raw, more) = expected[n];
                string p = pp + "accessories[" + n + "]";
                I(p + ".importSlot", a.ImportSlot ?? -1, slot);
                S(p + ".id", a.Id, JsonText.DerivedId(sha + "/acc/" + slot.ToString(CultureInfo.InvariantCulture)));
                I(p + ".category", a.Category, type);
                S(p + ".item", a.Item, Ref(id, prefix + "accessory" + slot.ToString(CultureInfo.InvariantCulture) + ".ChaFileAccessory.PartsInfo.id", type));
                if (raw != null)
                {
                    S(p + ".parent", a.Parent, raw.Get("parentKey") as string ?? "");
                    var am = raw.Get("addMove") as List<object>;
                    var flat = am?.At(2) as List<object>;
                    for (int j = 0; j < 2; j++)
                    {
                        V3(p + ".moves[" + j + "].position", a.Moves[j].Position, flat != null ? Arr3(flat[j * 3 + 0]) : new float[3]);
                        V3(p + ".moves[" + j + "].rotation", a.Moves[j].Rotation, flat != null ? Arr3(flat[j * 3 + 1]) : new float[3]);
                        V3(p + ".moves[" + j + "].scale", a.Moves[j].Scale, flat != null ? Arr3(flat[j * 3 + 2]) : new[] { 1f, 1f, 1f });
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        var cm = At(raw, "colorInfo", i);
                        C(p + ".colors[" + i + "].color", a.Colors[i].Color, cm, "color", White);
                        F(p + ".colors[" + i + "].gloss", a.Colors[i].Gloss, F(cm, "glossPower", 0.5f));
                        F(p + ".colors[" + i + "].metallic", a.Colors[i].Metallic, F(cm, "metallicPower", 0.5f));
                        F(p + ".colors[" + i + "].smoothness", a.Colors[i].Smoothness, F(cm, "smoothnessPower", 0.5f));
                    }
                    I(p + ".hideCategory", a.HideCategory, I(raw, "hideCategory", 0));
                    I(p + ".hideTiming", a.HideTiming, I(raw, "hideTiming", 1));
                    B(p + ".noShake", a.NoShake, B(raw, "noShake", false));
                }
                else
                {
                    S(p + ".parent", a.Parent, more.ParentKey ?? "");
                    for (int j = 0; j < 2; j++)
                    {
                        V3(p + ".moves[" + j + "].position", a.Moves[j].Position, more.AddMove[j, 0]);
                        V3(p + ".moves[" + j + "].rotation", a.Moves[j].Rotation, more.AddMove[j, 1]);
                        V3(p + ".moves[" + j + "].scale", a.Moves[j].Scale, more.AddMove[j, 2]);
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        C(p + ".colors[" + i + "].color", a.Colors[i].Color, new[] { more.Color[i].r, more.Color[i].g, more.Color[i].b, more.Color[i].a });
                        F(p + ".colors[" + i + "].gloss", a.Colors[i].Gloss, more.GlossPower[i]);
                        F(p + ".colors[" + i + "].metallic", a.Colors[i].Metallic, more.MetallicPower[i]);
                        F(p + ".colors[" + i + "].smoothness", a.Colors[i].Smoothness, more.SmoothnessPower[i]);
                    }
                    I(p + ".hideCategory", a.HideCategory, more.HideCategory);
                    I(p + ".hideTiming", a.HideTiming, more.HideTiming);
                    B(p + ".noShake", a.NoShake, more.NoShake);
                }
            }
        }

        static float[] Arr3(object v) => v is List<object> l && l.Count >= 3 ? new[] { ToF(l[0]), ToF(l[1]), ToF(l[2]) } : new float[3];

        AccessoryEntry BySlot(CoordDocument d, int slot) => d.Accessories.FirstOrDefault(a => a.ImportSlot == slot);

        // ---------- plugin sections: decoder output vs the documents, counts vs decoder counts minus drops
        void Plugins(CardExtData ext, CharaDocument chara, CoordDocument coord, bool isCoord)
        {
            const string P = "plugins";
            if (chara != null)
            {
                // ABMX
                var body = ext.Abmx?.Modifiers.Where(m => m.Location == 0 || m.Location == 1).ToList() ?? new List<AbmxData.BoneModifier>();
                I(P + ".abmx.body.count", chara.Body.BoneMods.Count, body.Count(m => !string.IsNullOrEmpty(m.BoneName)));
                int bi = 0;
                foreach (var m in body.Where(m => !string.IsNullOrEmpty(m.BoneName)))
                {
                    if (bi >= chara.Body.BoneMods.Count) break;
                    var b = chara.Body.BoneMods[bi++];
                    S(P + ".abmx.bone", b.Bone, m.BoneName);
                    S(P + ".abmx.location", b.Location, m.Location == 1 ? "bodyTop" : "unknown");
                    BoneValues(P + ".abmx." + m.BoneName, b, m.Data);
                }
                // uncensor, invisible body
                var u = ext.Uncensor;
                B(P + ".uncensor present", chara.Body.Uncensor != null, u != null);
                if (u != null && chara.Body.Uncensor != null)
                {
                    S(P + ".uncensor.body", chara.Body.Uncensor.Body, u.BodyGUID);
                    S(P + ".uncensor.penis", chara.Body.Uncensor.Penis, u.PenisGUID);
                    S(P + ".uncensor.balls", chara.Body.Uncensor.Balls, u.BallsGUID);
                    S(P + ".uncensor.displayBalls", chara.Body.Uncensor.DisplayBalls?.ToString(), u.DisplayBalls?.ToString());
                    S(P + ".uncensor.legacy", chara.Body.Uncensor.Legacy, u.LegacyUncensorGUID);
                }
                B(P + ".body.visible", chara.Body.Visible, ext.InvisibleBody?.Visible ?? true);
                // KSOX
                var k = ext.SkinOverlays;
                var want = k?.Textures.Where(kv => Enum.IsDefined(typeof(SkinOverlayTarget), (int)kv.Key)).OrderBy(kv => (int)kv.Key).ToList() ?? new List<KeyValuePair<SkinTexType, byte[]>>();
                I(P + ".ksox.count", chara.Body.SkinOverlays.Count, want.Count);
                for (int i = 0; i < want.Count && i < chara.Body.SkinOverlays.Count; i++)
                {
                    S(P + ".ksox.target", chara.Body.SkinOverlays[i].Target, ((SkinOverlayTarget)(int)want[i].Key).ToString());
                    B(P + ".ksox.image bytes", Bytes(chara.Body.SkinOverlays[i].Image)?.SequenceEqual(want[i].Value) == true, true);
                }
                B(P + ".studioOverlays.skin", chara.StudioOverlays.Skin, k != null && k.Version >= 2 ? k.EnableInStudioSkin : true);
                B(P + ".studioOverlays.eyes", chara.StudioOverlays.Eyes, k != null && k.Version >= 2 ? k.EnableInStudioIris : true);
                B(P + ".studioOverlays.clothes", chara.StudioOverlays.Clothes, ext.ClothesOverlays?.EnableInStudio ?? true);
            }
            // ABMX on accessories
            var accMods = ext.Abmx?.Modifiers.Where(m => m.AccessorySlot >= 0 && !string.IsNullOrEmpty(m.BoneName)).ToList() ?? new List<AbmxData.BoneModifier>();
            I(P + ".abmx.accessory.count", coord.Accessories.Sum(a => a.BoneMods.Count), accMods.Count(m => BySlot(coord, m.AccessorySlot) != null));

            // MaterialEditor: per target, the decoder's items in apply order
            var me = ext.MaterialEditor;
            var items = me?.AllItems().ToList() ?? new List<MaterialEditorData.Item>();
            void MeList(string p, List<MaterialOverride> actual, IEnumerable<MaterialEditorData.Item> exp)
            {
                var e = exp.ToList();
                I(p + ".count", actual.Count, e.Count);
                for (int i = 0; i < actual.Count && i < e.Count; i++)
                {
                    S(p + "[" + i + "].type", actual[i].Type, MeType(e[i]));
                    if (e[i] is MaterialEditorData.MaterialPropertyItem mp && actual[i] is MeMaterialProperty ap)
                    {
                        S(p + "[" + i + "].material", ap.Material, mp.MaterialName ?? "");
                        S(p + "[" + i + "].property", ap.Property, mp.Property ?? "");
                    }
                    if (e[i] is MaterialEditorData.TextureProperty tp && actual[i] is MeTexture at)
                    {
                        byte[] want = tp.TexID.HasValue && me.Textures.TryGetValue(tp.TexID.Value, out var tb) ? tb : null;
                        B(p + "[" + i + "].image bytes", want == null ? at.Image == null : Bytes(at.Image)?.SequenceEqual(want) == true, true);
                    }
                    if (e[i] is MaterialEditorData.FloatProperty fp && actual[i] is MeFloat af && af.ValueText == null)
                        F(p + "[" + i + "].value", af.Value, float.Parse(fp.Value, NumberStyles.Float, CultureInfo.InvariantCulture));
                    if (e[i] is MaterialEditorData.ColorProperty cp && actual[i] is MeColor ac)
                        C(p + "[" + i + "].value", ac.Value, new[] { cp.Value.r, cp.Value.g, cp.Value.b, cp.Value.a });
                }
            }
            bool Base(MaterialEditorData.Item it) => it.CoordinateIndex == 0;
            if (chara != null)
            {
                MeList(P + ".me.character", chara.MaterialOverrides, items.Where(it => Base(it) && it.ObjectType == MaterialEditorData.ObjectType.Character));
                for (int h = 0; h < 4; h++) MeList(P + ".me.hair" + h, chara.Hair.Parts[h].MaterialOverrides, items.Where(it => Base(it) && it.ObjectType == MaterialEditorData.ObjectType.Hair && it.Slot == h));
            }
            for (int k = 0; k < 8; k++) MeList(P + ".me.clothes" + k, coord.Clothes.Parts[k].MaterialOverrides, items.Where(it => Base(it) && it.ObjectType == MaterialEditorData.ObjectType.Clothing && it.Slot == k));
            foreach (var a in coord.Accessories) MeList(P + ".me.acc" + a.ImportSlot, a.MaterialOverrides, items.Where(it => Base(it) && it.ObjectType == MaterialEditorData.ObjectType.Accessory && it.Slot == a.ImportSlot));

            // KCOX
            var kc = ext.ClothesOverlays;
            for (int k = 0; k < 8; k++)
            {
                string n = ClothesOverlayData.MainClothesNames[k];
                var ov = coord.Clothes.Parts[k].Overlays;
                void Ov(string p, OverlayEntry a, string id)
                {
                    ClothesTexData t = null;
                    kc?.Overlays.TryGetValue(id, out t);
                    B(p + " present", a != null, t != null);
                    if (a == null || t == null) return;
                    B(p + ".clearTarget", a.ClearTarget, t.Override);
                    I(p + ".blend", a.Blend, t.BlendingMode);
                    B(p + ".image bytes", t.TextureBytes == null ? a.Image == null : Bytes(a.Image)?.SequenceEqual(t.TextureBytes) == true, true);
                }
                string pp = P + ".kcox." + ClothesSet.Names[k];
                Ov(pp + ".main", ov?.Main, n);
                Ov(pp + ".colorMask", ov?.ColorMask, "Colormask_" + n);
                Ov(pp + ".sourceReplacement", ov?.SourceReplacement, "Override_" + n);
                for (int j = 0; j < 3; j++) Ov(pp + ".patterns[" + j + "]", ov?.Patterns[j], "Pattern_" + j + "_" + n);
                int size = 0;
                bool hasSize = kc != null && kc.TextureSizeOverride.TryGetValue(n, out size);
                S(pp + ".textureSize", ov?.TextureSize?.ToString(), hasSize ? size.ToString() : null);
            }

            // pushUp
            var pu = ext.PushUp;
            B(P + ".pushUp present", coord.Clothes[ClothesKind.InnerTop].PushUp != null && coord.Clothes[ClothesKind.Top].PushUp != null, pu != null);
            if (pu != null)
            {
                PushUpEq(P + ".pushUp.innerTop", coord.Clothes[ClothesKind.InnerTop].PushUp, pu.Bra);
                PushUpEq(P + ".pushUp.top", coord.Clothes[ClothesKind.Top].PushUp, pu.Top);
            }

            // DynamicBoneEditor
            var dbe = ext.DynamicBoneEditor?.Entries.Where(e => e.CoordinateIndex == 0 && e.BoneName != null).ToList() ?? new List<DynamicBoneEditorData.Entry>();
            foreach (var a in coord.Accessories)
            {
                var e = dbe.Where(x => x.Slot == a.ImportSlot).ToList();
                I(P + ".dbe.acc" + a.ImportSlot + ".count", a.DynamicBones.Count, e.Count);
                for (int i = 0; i < e.Count && i < a.DynamicBones.Count; i++)
                {
                    S(P + ".dbe.root", a.DynamicBones[i].Root, e[i].BoneName);
                    S(P + ".dbe.weight", a.DynamicBones[i].Weight?.ToString("R"), e[i].Weight?.ToString("R"));
                    S(P + ".dbe.freezeAxis", a.DynamicBones[i].FreezeAxis?.ToString(), e[i].FreezeAxis?.ToString());
                    S(P + ".dbe.original.radius", a.DynamicBones[i].Original.Radius?.ToString("R"), e[i].RadiusOriginal?.ToString("R"));
                }
            }

            // AdditionalAccessoryControls
            var aac = ext.AdditionalAccessoryControls;
            foreach (var a in coord.Accessories)
            {
                var sd = aac?.Slots.FirstOrDefault(s => !s.IsEmpty && s.SlotNumber == a.ImportSlot);
                B(P + ".aac.acc" + a.ImportSlot + " present", a.Rules != null, sd != null);
                if (sd == null || a.Rules == null) continue;
                I(P + ".aac.rules.count", a.Rules.Visibility.Count, sd.VisibilityRules.Count);
                B(P + ".aac.characterAccessory", a.Rules.CharacterAccessory, sd.CharacterAccessory);
                B(P + ".aac.matchHairColor", a.Rules.MatchHairColor, sd.AutoMatchBackHairColor);
                S(P + ".aac.advancedParent", a.Rules.AdvancedParent, sd.AdvancedParent);
                S(P + ".aac.name", a.Rules.Name, sd.AccessoryName);
                for (int i = 0; i < sd.VisibilityRules.Count && i < a.Rules.Visibility.Count; i++)
                {
                    var r = sd.VisibilityRules[i];
                    var ar = a.Rules.Visibility[i];
                    S(P + ".aac.rule", ar.Rule, Enum.IsDefined(typeof(AdditionalAccessoryControlsData.Rule), r.Rule) ? r.Rule.ToString() : ((int)r.Rule).ToString());
                    bool link = r.Rule == AdditionalAccessoryControlsData.Rule.ACCESSORY_LINK || r.Rule == AdditionalAccessoryControlsData.Rule.ACCESSORY_INVERSE_LINK;
                    if (link && int.TryParse(r.Modifier, out int oneBased))
                        S(P + ".aac.link", ar.Link, BySlot(coord, oneBased - 1)?.Id);
                    else if (!link)
                        S(P + ".aac.modifier", ar.Modifier, r.Modifier != null && int.TryParse(r.Modifier, out int mv) && Enum.IsDefined(typeof(AdditionalAccessoryControlsData.Modifier), mv)
                            ? ((AdditionalAccessoryControlsData.Modifier)mv).ToString() : r.Modifier);
                }
            }
            B(P + ".aac.ruleOverrides present", coord.RuleOverrides != null, aac?.OverrideDataRaw != null);
            if (coord.RuleOverrides != null && aac != null)
                I(P + ".aac.ruleOverrides.count", coord.RuleOverrides.Suppressed.Count + coord.RuleOverrides.Overrides.Count, aac.SuppressedRules.Count + aac.OverrideRules.Count);
        }

        byte[] Bytes(string resName) => resName == null ? null : res.Get(resName);

        static string MeType(MaterialEditorData.Item it)
        {
            switch (it)
            {
                case MaterialEditorData.MaterialCopy _: return "copy";
                case MaterialEditorData.NameProperty _: return "name";
                case MaterialEditorData.ShaderEdit _: return "shader";
                case MaterialEditorData.RendererProperty _: return "renderer";
                case MaterialEditorData.FloatProperty _: return "float";
                case MaterialEditorData.KeywordProperty _: return "keyword";
                case MaterialEditorData.ColorProperty _: return "color";
                case MaterialEditorData.VectorProperty _: return "vector";
                case MaterialEditorData.TextureProperty _: return "texture";
                case MaterialEditorData.CubemapProperty _: return "cubemap";
                case MaterialEditorData.ProjectorProperty _: return "projector";
            }
            return "?";
        }

        void BoneValues(string p, BoneModBase b, AbmxData.BoneModifierData d)
        {
            V3(p + ".scale", b.Scale, d.Scale);
            F(p + ".length", b.Length, d.Length);
            V3(p + ".position", b.Position, d.Position);
            V3(p + ".rotation", b.Rotation, d.Rotation);
        }

        void PushUpEq(string p, PushUpSettings a, PushUpData.PushUpSet s)
        {
            if (a == null) return;
            B(p + ".enabled", a.Enabled, s.EnablePushup);
            F(p + ".firmness", a.Firmness, s.Firmness);
            F(p + ".lift", a.Lift, s.Lift);
            F(p + ".pushTogether", a.PushTogether, s.PushTogether);
            F(p + ".squeeze", a.Squeeze, s.Squeeze);
            F(p + ".centerNipples", a.CenterNipples, s.CenterNipples);
            F(p + ".corset", a.Corset, s.Corset);
            B(p + ".flattenNipples", a.FlattenNipples, s.FlattenNipples);
            B(p + ".hideNipples", a.HideNipples, s.HideNipples);
            B(p + ".hideAccessories", a.HideAccessories, s.HideAccessories);
            B(p + ".corsetHalf", a.CorsetHalf, s.CorsetHalf);
        }

        // ================================================================= coordinate card

        public void Coord(CoordCard card, CardExtData ext, byte[] data, CoordContainer c, string rel)
        {
            file = rel;
            res = c.Res;
            resolve = card.Resolve;
            usedEntries = new HashSet<int>();
            // written out from the census rules
            int sex = 1;
            string how = "default";
            var esd = card.Clothes.Get("ExtendedSaveData") as byte[];
            int? cs = null;
            if (esd != null)
            {
                var pd = MsgPack.Read(esd).Get("com.bepis.bepinex.sideloader") as List<object>;
                if (pd.At(1).Get("CoordinateSex") is long l) cs = (int)l;
            }
            if (cs == 0 || cs == 1) { sex = cs.Value; how = "coordinateSex"; }
            else
            {
                string[] m = { "ClothesTopM", "ClothesBotM", "ClothesGlovesM", "ClothesShoesM" }, f = { "ClothesTop", "ClothesBot", "ClothesGloves", "ClothesShoes" };
                bool male = card.Resolve.Any(e => m.Any(x => (e.Property ?? "").EndsWith("ChaFileClothes." + x, StringComparison.Ordinal)));
                bool female = card.Resolve.Any(e => f.Any(x => (e.Property ?? "").EndsWith("ChaFileClothes." + x, StringComparison.Ordinal)));
                if (male || female) { how = "resolveInfo"; sex = male && !female ? 0 : 1; }
            }
            I("coord.sex", c.Coord.Sex, sex);
            S("import.coordSex", c.Import.CoordSex, how);
            S("coord.name", c.Coord.Name, card.Name ?? "");
            S("import.from", c.Import.From, "hs2.coord");
            S("import.sha256", c.Import.Sha256, Sha(data));
            S("import.hs2Version", c.Import.Hs2Version, card.Version);
            Clothes(card.Clothes, c.Coord, sex, "", "");
            Accessories(card.Accessory, ext, c.Coord, "", data, "");
            Plugins(ext, null, c.Coord, true);
        }

        // ================================================================= A3 raw copies

        static readonly HashSet<string> Dropped = new HashSet<string>(StringComparer.Ordinal) { "TestCharaCustomFunctionController", "com.gebo.bepinex.translationhelper.chara", "com.bepis.xloader.universalautoresolver" };
        static readonly HashSet<string> CoordIds = new HashSet<string>(StringComparer.Ordinal) { "moreAccessories", "KCOX", "mikke.pushUpAI", "com.deathweasel.bepinex.dynamicboneeditor", "orange.spork.additionalaccessorycontrolsplugin", "orange.spork.outfitpainter", "mikke.BeaverAI" };

        static string Escape(string id)
        {
            var sbe = new StringBuilder();
            foreach (var b in Encoding.UTF8.GetBytes(id))
            {
                char ch = (char)b;
                if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '.' || ch == '_' || ch == '-') sbe.Append(ch);
                else sbe.Append('%').Append(b.ToString("X2"));
            }
            return sbe.ToString();
        }

        /// <summary>Returns the number of mismatches.</summary>
        public int Raw(CardExtData ext, string blockVersion, KkexStore chara, KkexStore coord, string form, string rel)
        {
            int bad = 0;
            void Bad(string what) { bad++; Mismatch(rel, "A3 " + what); }
            if (blockVersion != "3") return 0;
            var opaque = ext.Opaque.ToDictionary(o => o.Guid, o => o.Reason, StringComparer.Ordinal);
            var nil = new HashSet<string>(ext.NilIds, StringComparer.Ordinal);
            int expectedKkex = 0;
            foreach (var id in ext.Spans.Keys)
            {
                if (nil.Contains(id) || Dropped.Contains(id)) continue;
                expectedKkex++;
                var store = form == "chara" && CoordIds.Contains(id) ? coord : chara;
                var e = store?.Entries.FirstOrDefault(x => x.Origin == "kkex" && x.Id == id);
                if (e == null) { Bad("missing entry " + id); continue; }
                var seg = ext.RawPluginData(id);
                var want = seg.Array.Skip(seg.Offset).Take(seg.Count).ToArray();
                if (!store.Files.TryGetValue(e.File, out var got) || !got.SequenceEqual(want)) Bad("bytes differ " + id);
                if (e.File != Escape(id) + ".msgpack") Bad("file name " + e.File);
                bool isOpaque = opaque.ContainsKey(id) || !ext.Versions.ContainsKey(id);
                if (e.Role != (isOpaque ? "opaque" : "source")) Bad($"role {e.Role} of {id}");
                string reason = !isOpaque ? null : !opaque.TryGetValue(id, out var r) ? "malformed" : r == null ? "noDecoder" : r == "unsupported version" ? "unsupportedVersion" : r == "decode error" ? "decodeError" : "malformed";
                if (e.Reason != reason) Bad($"reason {e.Reason} of {id} (expected {reason})");
                if (e.Form != form) Bad("form " + e.Form);
                int? ver = ext.Versions.TryGetValue(id, out var v) && v >= 0 ? v : (int?)null;
                if (e.Version != ver) Bad($"version {e.Version} of {id}");
                if (e.Size != want.LongLength || e.Sha256 != Sha(want)) Bad("size/sha256 " + id);
            }
            int actualKkex = (chara?.Entries.Count(x => x.Origin == "kkex") ?? 0) + (form == "chara" ? coord?.Entries.Count(x => x.Origin == "kkex") ?? 0 : 0);
            if (actualKkex != expectedKkex) Bad($"kkex entries {actualKkex} != {expectedKkex}");
            foreach (var st in form == "chara" ? new[] { chara, coord } : new[] { chara })
            {
                if (st == null) continue;
                if (st.Source != (form == "chara" ? "hs2.chara" : "hs2.coord")) Bad("index source " + st.Source);
                if (st.BlockVersion != blockVersion) Bad("index blockVersion " + st.BlockVersion);
                foreach (var e in st.Entries.Where(x => x.Origin == "object"))
                {
                    if (!st.Files.TryGetValue(e.File, out var b) || b.LongLength != e.Size || Sha(b) != e.Sha256) Bad("object entry file " + e.File);
                    if (e.Role != (e.Id == "com.bepis.bepinex.sideloader" ? "source" : "opaque")) Bad("object entry role " + e.Id);
                    if (Dropped.Contains(e.Id)) Bad("dropped id copied " + e.Id);
                }
            }
            return bad;
        }
    }
}
