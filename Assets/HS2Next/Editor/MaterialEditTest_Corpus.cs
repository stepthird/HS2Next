// MaterialEditTest_Corpus: the materialOverrides arrays of every card and coordinate, as the importer writes them
// (the all-card rows). The files of the card folders of Tools/targets.json and of
// UserData/coordinate are imported as data: nothing is built, no image is decoded.
//   MY0 (-record) one line per file and non-empty array into Cache/maker/me_corpus_7b8c37c.tsv: the file by hash, its folder
//       group and kind, the array's field path, the item count, the sha256 of the array's canonical JSON, the res names it
//       holds. Taken once, on the import code of 1.2.0 (no diff of the import path since the tag). A record that exists is
//       never written over: a difference is a defect, not a reason to take it again.
//   MY1 (no -record) the same walk compared with that file: every line equal.
// The hash is the first 12 hex digits of the sha256 of the path relative to UserData (no file name in any report or list).
// Run: -executeMethod MaterialEditTest_Corpus.Run [-record] [-limit N] [-base <file name under Cache/maker>] (needs -nographics).
//      -limit takes the first N files of each folder: a compare then looks at those files only, and a -record writes
//      Spike/out/materialedit/me_corpus_sample.tsv, never the record.
// The row MaterialEditTest.corpus calls Run with StayOpen, and Walk with steps of its own (MY2-MY4 walk the same files; MaterialEditTest_CorpusRows.cs).
// Report: Spike/out/materialedit/corpus_report.txt, first line "SUMMARY: N PASS, M FAIL".
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HS2Next.Content;
using HS2Next.Import;
using HS2Next.Save;
using HS2Next.Scenes;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class MaterialEditTest_Corpus
{
    public const string RecordName = "me_corpus_7b8c37c.tsv";
    public const string Columns = "file\tgroup\tkind\tarray\titems\tsha256\tres";
    /// <summary>Run is one step of the row MaterialEditTest.corpus: it leaves the process open.</summary>
    public static bool StayOpen;
    static readonly DateTime FixedTime = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "materialedit");
    static string RecordDir => Path.Combine(ContentSettings.ProjectRoot, "Cache", "maker");

    /// <summary>The first 12 hex digits of the sha256 of a path relative to UserData: how reports name a file.</summary>
    public static string FileHash(string rel)
    {
        using (var sha = SHA256.Create())
            return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(rel))).Substring(0, 12);
    }

    static string Hex(byte[] b)
    {
        var s = new StringBuilder(b.Length * 2);
        foreach (var x in b) s.Append(x.ToString("x2", CultureInfo.InvariantCulture));
        return s.ToString();
    }

    /// <summary>One imported file: <see cref="Subtree"/> for a character card, <see cref="Coord"/> for a coordinate.</summary>
    public sealed class CorpusFile
    {
        public string Path, Rel, Hash, Group, Kind;
        public CharaSubtree Subtree;
        public CoordDocument Coord;
        public ResStore Res;
    }

    /// <summary>The folder group of a path relative to UserData: female, male, coordinate/female, coordinate/male.</summary>
    static string GroupOf(string rel)
    {
        var p = rel.Split('/');
        if (p.Length >= 2 && p[0] == "chara") return p[1];
        return p.Length >= 3 ? p[0] + "/" + p[1] : p[0];
    }

    /// <summary>Every array of a coordinate document with its field path, in document order.</summary>
    public static IEnumerable<(string path, List<MaterialOverride> list)> ArraysOf(CoordDocument d)
    {
        for (int k = 0; k < ClothesSet.Count; k++) yield return ("clothes." + ClothesSet.Names[k] + ".materialOverrides", d.Clothes.Parts[k].MaterialOverrides);
        foreach (var a in d.Accessories) yield return ("accessories[\"" + a.Id + "\"].materialOverrides", a.MaterialOverrides);
    }

    /// <summary>Every array of a file with its field path, in document order.</summary>
    public static IEnumerable<(string path, List<MaterialOverride> list)> ArraysOf(CorpusFile f)
    {
        if (f.Subtree != null)
        {
            yield return ("materialOverrides", f.Subtree.Chara.MaterialOverrides);
            var hair = f.Subtree.Chara.Hair;
            yield return ("hair.back.materialOverrides", hair.Back.MaterialOverrides);
            yield return ("hair.front.materialOverrides", hair.Front.MaterialOverrides);
            yield return ("hair.side.materialOverrides", hair.Side.MaterialOverrides);
            yield return ("hair.option.materialOverrides", hair.Option.MaterialOverrides);
        }
        foreach (var a in ArraysOf(f.Subtree != null ? f.Subtree.Coord : f.Coord)) yield return a;
    }

    /// <summary>Imports each file of the folders in turn; <paramref name="refused"/> gets a file that gave no container.</summary>
    public static void Walk(Hs2Importer importer, int limit, Action<CorpusFile> step, Action<string, string> refused)
    {
        var targets = TestTargets.Load();
        string user = TestTargets.UserData;
        var dirs = new[] { TestTargets.Abs(targets.cards.female), TestTargets.Abs(targets.cards.male), Path.Combine(user, "coordinate") };
        foreach (var dir in dirs)
            foreach (var path in ImportTest.FilesOf(dir, limit))
            {
                string rel = path.Substring(user.Length).TrimStart('\\', '/').Replace('\\', '/');
                var f = new CorpusFile { Path = path, Rel = rel, Hash = FileHash(rel), Group = GroupOf(rel) };
                try
                {
                    byte[] data = File.ReadAllBytes(path);
                    if (Hs2Importer.Detect(data) == Hs2SourceKind.Coord)
                    {
                        var c = importer.ImportCoord(data, Path.GetFileName(path), rel);
                        if (!c.Ok) { refused(f.Hash, FirstError(c.Report)); continue; }
                        f.Kind = "coord"; f.Coord = c.Container.Coord; f.Res = c.Container.Res;
                    }
                    else
                    {
                        var c = importer.ImportChara(data, Path.GetFileName(path), rel);
                        if (!c.Ok) { refused(f.Hash, FirstError(c.Report)); continue; }
                        f.Kind = "chara"; f.Subtree = c.Container.Subtree; f.Res = c.Container.Res;
                    }
                }
                catch (Exception e) { refused(f.Hash, e.GetType().Name + ": " + e.Message); continue; }
                step(f);
            }
    }

    static string FirstError(ImportReport r)
    {
        foreach (var x in r.Findings) if (x.Severity == FindingSeverity.Error) return x.Code;
        return "no container";
    }

    /// <summary>The line of one array (<see cref="Columns"/>).</summary>
    public static string Line(CorpusFile f, string path, List<MaterialOverride> list)
    {
        byte[] text = Encoding.UTF8.GetBytes(FieldJson.ToText(list));
        var names = new SortedSet<string>(StringComparer.Ordinal);
        ResStore.ScanReferences(text, names);
        string sha;
        using (var h = SHA256.Create()) sha = Hex(h.ComputeHash(text));
        return string.Join("\t", f.Hash, f.Group, f.Kind, path, list.Count.ToString(CultureInfo.InvariantCulture), sha, names.Count == 0 ? "-" : string.Join(",", names));
    }

    sealed class Census
    {
        public int Files, WithItems, Arrays, Items, QueueOnly, TextValues;
        public readonly SortedDictionary<string, int> Types = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly HashSet<string> Res = new HashSet<string>(StringComparer.Ordinal);
    }

    public static void Run()
    {
        var a = TestArgs.Get();
        bool record = a.Contains("-record");
        int li = Array.IndexOf(a, "-limit");
        int limit = li >= 0 && li + 1 < a.Length ? int.Parse(a[li + 1], CultureInfo.InvariantCulture) : int.MaxValue;
        int bi = Array.IndexOf(a, "-base");
        string name = bi >= 0 && bi + 1 < a.Length ? a[bi + 1] : RecordName;
        Directory.CreateDirectory(OutDir);
        string report = Path.Combine(OutDir, "corpus_report.txt");
        File.WriteAllText(report, "SUMMARY: 0 PASS, 1 FAIL\nMaterialEditTest_Corpus: started, no result yet\n", new UTF8Encoding(false));

        var sb = new StringBuilder();
        int pass = 0, fail = 0;
        void Check(string what, bool ok, string detail = "")
        {
            sb.AppendLine($"{(ok ? "PASS" : "FAIL")}  {what}{(detail.Length > 0 ? "  " + detail : "")}");
            if (ok) pass++; else fail++;
        }
        var total = Stopwatch.StartNew();
        string recordPath = limit == int.MaxValue ? Path.Combine(RecordDir, name) : Path.Combine(OutDir, "me_corpus_sample.tsv");
        if (record && limit == int.MaxValue && File.Exists(recordPath))
        {
            Check("the record does not exist yet", false, name + " is there: it is never taken again");
            Finish(sb, report, pass, fail, total);
            return;
        }

        var gd = CardResolveTest.BuildDb(sb);
        var importer = new Hs2Importer(gd, new ImportOptions { Clock = () => FixedTime });
        var census = new SortedDictionary<string, Census>(StringComparer.Ordinal);
        var lines = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var refusedFiles = new List<string>();
        int read = 0;
        Walk(importer, limit, f =>
        {
            read++;
            seen.Add(f.Hash);
            if (!census.TryGetValue(f.Group, out var c)) census[f.Group] = c = new Census();
            c.Files++;
            bool any = false;
            foreach (var (path, list) in ArraysOf(f))
            {
                if (list.Count == 0) continue;
                any = true;
                c.Arrays++;
                c.Items += list.Count;
                foreach (var e in list)
                {
                    c.Types.TryGetValue(e.Type, out int n);
                    c.Types[e.Type] = n + 1;
                    if (e is MeShader s && s.Shader == null) c.QueueOnly++;
                    if (e is MeFloat x && (x.ValueText != null || x.OriginalText != null)) c.TextValues++;
                    if (e is MeRenderer r && (r.ValueText != null || r.OriginalText != null)) c.TextValues++;
                }
                string line = Line(f, path, list);
                lines.Add(line);
                string res = line.Substring(line.LastIndexOf('\t') + 1);
                if (res != "-") foreach (var x in res.Split(',')) c.Res.Add(x);
            }
            if (any) c.WithItems++;
            if (read % 200 == 0) Debug.Log("[corpus] " + read + " files read");
        }, (hash, why) => refusedFiles.Add(hash + " " + why));
        lines.Sort(string.CompareOrdinal);

        sb.AppendLine($"files read {read}, refused {refusedFiles.Count}, with items {census.Values.Sum(c => c.WithItems)}, arrays {lines.Count}, items {census.Values.Sum(c => c.Items)}; importer {importer.ImporterVersion}");
        foreach (var kv in census)
        {
            var c = kv.Value;
            sb.AppendLine($"   {kv.Key}: files {c.Files}, with items {c.WithItems}, arrays {c.Arrays}, items {c.Items}, " +
                $"res {c.Res.Count}, shader queue only {c.QueueOnly}, text values {c.TextValues}; " + string.Join(", ", c.Types.Select(t => t.Key + " " + t.Value)));
        }
        foreach (var x in refusedFiles.Take(40)) sb.AppendLine("   refused " + x);
        Check("every array has a line and none is empty", lines.Count > 0 && lines.All(l => l.Split('\t').Length == 7), lines.Count + " lines");

        if (record)
        {
            string text = "# " + Columns + "\n" + string.Join("\n", lines) + "\n";
            Directory.CreateDirectory(Path.GetDirectoryName(recordPath));
            File.WriteAllText(recordPath, text, new UTF8Encoding(false));
            Check("MY0 record written", true, $"{Path.GetFileName(recordPath)}: {lines.Count} lines, sha256 {Sha(text)}");
        }
        else Compare(Path.Combine(RecordDir, name), lines, seen, limit != int.MaxValue, sb, Check);

        gd.UnloadAll();
        Finish(sb, report, pass, fail, total);
    }

    static string Sha(string text)
    {
        using (var h = SHA256.Create()) return Hex(h.ComputeHash(new UTF8Encoding(false).GetBytes(text)));
    }

    static void Compare(string file, List<string> lines, HashSet<string> seen, bool partial, StringBuilder sb, Action<string, bool, string> check)
    {
        if (!File.Exists(file)) { check("MY1 the record exists", false, Path.GetFileName(file)); return; }
        // (file, array) -> line; a partial walk looks at the files it read
        string Key(string l) { var p = l.Split('\t'); return p[0] + "\t" + p[3]; }
        var was = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var l in File.ReadAllLines(file, new UTF8Encoding(false)))
        {
            if (l.Length == 0 || l[0] == '#') continue;
            if (partial && !seen.Contains(l.Substring(0, l.IndexOf('\t')))) continue;
            was[Key(l)] = l;
        }
        var now = lines.ToDictionary(Key, l => l, StringComparer.Ordinal);
        int equal = 0;
        var differ = new List<string>();
        var gone = new List<string>();
        foreach (var kv in was)
        {
            if (!now.TryGetValue(kv.Key, out var l)) gone.Add(kv.Key.Replace('\t', ' '));
            else if (l == kv.Value) equal++;
            else differ.Add(kv.Key.Replace('\t', ' '));
        }
        var added = now.Keys.Where(k => !was.ContainsKey(k)).Select(k => k.Replace('\t', ' ')).ToList();
        foreach (var x in differ.Take(20)) sb.AppendLine("   differs " + x);
        foreach (var x in gone.Take(20)) sb.AppendLine("   gone " + x);
        foreach (var x in added.Take(20)) sb.AppendLine("   new " + x);
        check("MY1 every line equal", differ.Count == 0 && gone.Count == 0 && added.Count == 0,
            $"{equal} equal of {was.Count}; differ {differ.Count}, gone {gone.Count}, new {added.Count}");
    }

    static void Finish(StringBuilder sb, string report, int pass, int fail, Stopwatch total)
    {
        sb.Insert(0, $"SUMMARY: {pass} PASS, {fail} FAIL ({total.Elapsed.TotalSeconds:F0} s)\n");
        File.WriteAllText(report, sb.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        Debug.Log($"ME CORPUS DONE: {pass} PASS, {fail} FAIL");
        if (Application.isBatchMode && !StayOpen) TestExit.Exit(fail == 0 ? 0 : 1);
    }
}
