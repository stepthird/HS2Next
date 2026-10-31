// Build parity of characters built from documents (P1–P3; package C).
// Every case goes card bytes → importer → character file bytes → load → document adapter (DocumentRoundTrip). The Phase 1
// card path is retired (5.2): the part-key rows are compared with the S1 baselines (MakerCoreTest, built from the documents).
//   P1 (headless, every card of Tools/targets.json `cards`): card trees vs adapter trees for every key the adapter writes
//      (floats ==, ±0 equal; arrays by length and element; references through their rows), CardExtensions.Decode(card) vs
//      the adapter's decoder objects per plugin (counts, values, image bytes by SHA-256, ME float values by parsed value,
//      TexIDs through the bytes they name), and the document row of every part key against the S1 baseline of the card
//      (its "ctx|ref.<part key>" lines: category, ref, resolved row; a part only one side has is a difference). The tree
//      differences are classified (memberInit, negativeZero, documentedDrop); a row difference is never classified (the
//      baseline is the reference; a deliberate change re-takes it). PASS = 0 unclassified. Class counts in the report.
//   P2 (headless, the visual targets + 20 sampled cards; -all every card): builds from the documents: the first (cold) and
//      a later build equal (same list row per part key and slot, same accessory slots and objects, equal ExtStats.Summary(),
//      every transform's local TRS equal (==) after the build and after one Step(0), same renderers, per material the shader
//      and every float / colour / vector property equal, texture names and sizes equal); a build with the hs2.kkex stores
//      emptied equals the document build.
//      Every build: no renderer keeps a material the build did not make (CharaContext.OwnedMaterials; shared bundle
//      materials). Materials whose keywords disagree with their [Toggle] / [MaterialToggle] / [ToggleOff] values
//      (what the Unity Editor's property drawers would switch, Runtime/Rendering/MaterialCopies.cs) are counted.
//      State-leak regression: with the bundles kept loaded, the visual targets and up to 12 other cards (cards with such
//      materials first, else spread over the run, or with -targets over the card folders; -leakcards <list file> adds cards)
//      are built from the documents, the others once more, every loaded material's properties are read (the Editor then runs
//      its drawers on them; the keywords of materials the catalog did not record - the engine's Default-Material, run-time
//      materials - are put back, as no player switches them), and all are built again: identical. A unit check: a copy of a
//      loaded bundle material keeps the loaded keywords. (-targets: P1 and P2 on the visual targets; the leak regression still builds other cards.)
//      Timings: CharaDocumentInput.From median ≤ 5 ms (fails at 10 ms); the warm document build's median is reported
//      (the card build it was measured against is retired).
//   P3 (graphics, RunRender, the visual targets): Built-in renders (CharaRenderTest views: full, full34, face) of two
//      document builds (two round trips) byte-identical; URP renders max channel difference ≤ 8/255; the import thumbnails
//      of the visual targets written to Spike/out/import/thumbs/ (CharaFileImport, HS2's coordinate shot).
// Run: -executeMethod CharaDocBuildTest.Run [-cards <dir>] [-limit N] [-all] [-targets] [-p1only] [-p2only] [-leakcards <file>]
//      [-phase1materials]
//      (-targets: P1 and P2 on the visual targets only; -phase1materials: the earlier material handling, i.e.
//      CharaAssembler.OwnAllMaterials and MaterialCopies.KeepSerializedKeywords off, to show what the material checks catch)
//      -executeMethod CharaDocBuildTest.RunRender (graphics device; no -nographics)
// Reports: Spike/out/import/docbuild_report.txt, docbuild_render_report.txt; first line "SUMMARY: N PASS, M FAIL";
// EditorApplication.Exit(1) on any FAIL. Only the SFW visual targets are rendered; no other card is rendered or shown.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HS2Next.Chara;
using HS2Next.Chara.Documents;
using HS2Next.Chara.Ext;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.Data.Ext;
using HS2Next.Import;
using HS2Next.IO;
using HS2Next.Save;
using HS2Next.Scenes;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class CharaDocBuildTest
{
    // ================================================================ report

    static StringBuilder rep;
    static int passes, fails;
    static string current = "setup";
    static readonly Dictionary<string, (int pass, int fail)> perGroup = new Dictionary<string, (int, int)>();

    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "import");

    static void Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  {current} {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
        perGroup.TryGetValue(current, out var c);
        perGroup[current] = ok ? (c.pass + 1, c.fail) : (c.pass, c.fail + 1);
    }

    static void Info(string s) => rep.AppendLine("   " + s);
    static void Section(string id, string title) { current = id; rep.AppendLine(); rep.AppendLine("== " + id + " " + title); }

    static string Arg(string name, string def = null)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static bool Flag(string name) => Environment.GetCommandLineArgs().Contains(name);

    static void Finish(string file, string title, Stopwatch sw)
    {
        var sum = new StringBuilder();
        sum.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL ({sw.Elapsed.TotalSeconds:F0} s)");
        foreach (var kv in perGroup.OrderBy(k => k.Key, StringComparer.Ordinal)) sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail");
        sum.AppendLine(title);
        rep.Insert(0, sum.ToString());
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, file), rep.ToString());
        Debug.Log($"CHARA DOC BUILD TEST DONE: {passes} PASS, {fails} FAIL");
        if (fails > 0) EditorApplication.Exit(1);
    }

    static void Reset()
    {
        rep = new StringBuilder(); passes = fails = 0; perGroup.Clear(); current = "setup";
    }

    // ================================================================ shared

    /// <summary>The importer of the tests: package A's importer over the catalog with the round trip's fixed clock.</summary>
    public static ICharaImporter NewImporter(ContentCatalog gd) => new Hs2Importer(gd, new ImportOptions { Clock = () => DocumentRoundTrip.FixedTime });

    static string Sha(byte[] b)
    {
        if (b == null) return "null";
        using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-", "").ToLowerInvariant().Substring(0, 16);
    }

    static string Rel(string root, string file) => file.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');

    static double Median(List<double> l)
    {
        if (l.Count == 0) return double.NaN;
        var s = l.OrderBy(x => x).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }

    static string RowText(ListEntry e) => e == null ? "null" : e.Category + ":" + e.Key;
    static bool SameRow(ListEntry a, ListEntry b) => a == null ? b == null : b != null && a.Category == b.Category && a.Key == b.Key;

    static void Cleanup(HashSet<GameObject> before)
    {
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (before.Contains(go)) continue;
            var rts = new HashSet<RenderTexture>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (!m) continue;
                    foreach (var id in m.GetTexturePropertyNameIDs())
                        if (m.GetTexture(id) is RenderTexture rt) rts.Add(rt);
                }
            Object.DestroyImmediate(go);
            foreach (var rt in rts) { rt.Release(); Object.DestroyImmediate(rt); }
        }
    }

    // ================================================================ entry (P1, P2)

    public static void Run()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        var head = new StringBuilder();
        var gd = CardResolveTest.BuildDb(head);
        rep.AppendLine($"chara doc build test (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
        rep.Append(head);
        var importer = NewImporter(gd);
        var targets = TestTargets.Load();
        bool all = Flag("-all");
        int limit = int.Parse(Arg("-limit", int.MaxValue.ToString()), CultureInfo.InvariantCulture);
        var files = new List<(string root, string file)>();
        bool targetsOnly = Flag("-targets");
        if (targetsOnly)
            files.AddRange(targets.VisualCards.Where(File.Exists).Select(f => (TestTargets.UserData, f)));
        else if (Arg("-cards") is string dir)
            files.AddRange(Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories).OrderBy(f => Rel(dir, f), StringComparer.Ordinal).Select(f => (dir, f)));
        else
            foreach (var rel in new[] { targets.cards?.female, targets.cards?.male })
            {
                if (string.IsNullOrEmpty(rel)) continue;
                string d = TestTargets.Abs(rel);
                if (!Directory.Exists(d)) continue;
                files.AddRange(Directory.GetFiles(d, "*.png", SearchOption.AllDirectories).OrderBy(f => Rel(d, f), StringComparer.Ordinal).Select(f => (d, f)));
            }
        if (files.Count > limit) files = files.Take(limit).ToList();
        var p1 = new Dictionary<string, P1Card>(StringComparer.OrdinalIgnoreCase);
        if (Flag("-phase1materials"))
        {
            CharaAssembler.OwnAllMaterials = false;
            HS2Next.MaterialCopies.KeepSerializedKeywords = false;
            rep.AppendLine("-phase1materials: CharaAssembler.OwnAllMaterials and MaterialCopies.KeepSerializedKeywords off");
        }
        try
        {
            if (!Flag("-p2only")) P1(gd, importer, files, p1);
            if (!Flag("-p2only")) BigCoordinate(gd);
            if (!Flag("-p1only"))
            {
                // The visual targets and 20 cards spread over the list come first and get the full P2 with the timing judge;
                // -all then appends every other card for the lean P2, so the timing median covers the same 22 cards in the
                // quick row and in -all.
                var sample = new List<string>(targets.VisualCards.Where(File.Exists));
                if (!targetsOnly)
                {
                    var pool = files.Select(f => f.file).Where(f => !sample.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();
                    int n = Math.Min(20, pool.Count);
                    for (int i = 0; i < n; i++) sample.Add(pool[(int)((long)i * pool.Count / n)]);
                }
                int fullCount = all ? sample.Count : int.MaxValue;
                if (all && !targetsOnly) sample.AddRange(files.Select(f => f.file).Where(f => !sample.Contains(f, StringComparer.OrdinalIgnoreCase)));
                P2(gd, importer, sample, p1, fullCount);
            }
        }
        catch (Exception e) { Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        finally { CharaAssembler.OwnAllMaterials = true; HS2Next.MaterialCopies.KeepSerializedKeywords = true; }
        foreach (var l in gd.Log.Where(l => !l.StartsWith("vanilla") && !l.StartsWith("mod list")).Distinct().Take(40)) Info("catalog: " + l);
        gd.UnloadAll();
        Finish("docbuild_report.txt", "build parity (P2)", sw);
    }

    // ================================================================ P1 structural parity

    /// <summary>The differences of one card, by class (counted) and unclassified (listed).</summary>
    internal sealed class P1Card
    {
        public string Rel;
        public readonly SortedDictionary<string, int> Classes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly List<string> Unclassified = new List<string>();
        public readonly List<string> Notes = new List<string>();
        public int Leaves, RefKeys, Rows, RowDiffs;
        public FindingList Import;

        /// <summary>Counts per class and reason (the parenthesised tag at the end of the note, e.g. "aac.slotEmpty").</summary>
        public readonly SortedDictionary<string, int> Reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);

        public void Class(string cls, string what)
        {
            Classes[cls] = Classes.TryGetValue(cls, out var n) ? n + 1 : 1;
            string reason = cls;
            int open = what.LastIndexOf('('), close = what.LastIndexOf(')');
            if (cls.StartsWith("resolution.", StringComparison.Ordinal) || cls == "negativeZero") reason = cls;
            else if (open >= 0 && close > open && close == what.Length - 1)
            {
                string tag = what.Substring(open + 1, close - open - 1);
                if (tag.StartsWith("type ", StringComparison.Ordinal)) tag = "empty accessory slot";
                reason = cls + " / " + (tag.Length <= 60 ? tag : tag.Substring(0, 60));
            }
            else reason = cls + (cls == "memberInit" ? " / key absent" : " / other");
            Reasons[reason] = Reasons.TryGetValue(reason, out var r) ? r + 1 : 1;
            if (Notes.Count < 400) Notes.Add(cls + " " + what);
        }
        public void Diff(string what) { if (Unclassified.Count < 50) Unclassified.Add(what); else if (Unclassified.Count == 50) Unclassified.Add("…"); }

        /// <summary>True when the import report has <paramref name="code"/> (optionally at a path ending in <paramref name="pathEnd"/>).</summary>
        public bool Has(string code, string pathEnd = null)
        {
            if (Import == null) return false;
            foreach (var f in Import)
                if (f.Code == code && (pathEnd == null || (f.Path ?? "").EndsWith(pathEnd, StringComparison.Ordinal))) return true;
            return false;
        }
        public bool HasPrefix(string prefix) => Import != null && Import.Any(f => f.Code.StartsWith(prefix, StringComparison.Ordinal));
    }

    static void P1(ContentCatalog gd, ICharaImporter importer, List<(string root, string file)> files, Dictionary<string, P1Card> results)
    {
        var run = P1Begin(files.Count, results);
        for (int i = 0; i < files.Count; i++)
        {
            P1One(gd, importer, run, files[i].root, files[i].file);
            if ((i + 1) % 50 == 0)
            {
                gd.UnloadAll();
                GC.Collect();
                Debug.Log($"CharaDocBuildTest P1: {i + 1}/{files.Count} cards, {run.Watch.Elapsed.TotalSeconds:F0} s");
            }
        }
        P1End(run);
    }

    /// <summary>The counts of a P1 pass (Run; AllCardsTest per card).</summary>
    internal sealed class P1Run
    {
        public int Files, Ok, NotChara, ImportFailed, UnclassifiedCards, Leaves, RefKeys, Rows, RowDiffs;
        public readonly SortedDictionary<string, int> Totals = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly List<double> AdapterMs = new List<double>();
        public readonly List<string> FailLines = new List<string>();
        public Dictionary<string, P1Card> Results;
        public readonly Stopwatch Watch = Stopwatch.StartNew();
    }

    /// <summary>Starts P1 (its section in this class's report) over <paramref name="files"/> cards.</summary>
    internal static P1Run P1Begin(int files, Dictionary<string, P1Card> results = null)
    {
        Section("P1", "structural parity (" + files + " cards)");
        return new P1Run { Files = files, Results = results ?? new Dictionary<string, P1Card>(StringComparer.OrdinalIgnoreCase) };
    }

    /// <summary>P1 of one card file (<paramref name="root"/>: the folder its relative name is taken from).</summary>
    internal static void P1One(ContentCatalog gd, ICharaImporter importer, P1Run r, string root, string file)
    {
        string rel = Rel(root, file);
        byte[] bytes;
        try { bytes = File.ReadAllBytes(file); } catch (Exception e) { r.FailLines.Add(rel + ": unreadable " + e.Message); return; }
        if (Hs2Importer.Detect(bytes) != Hs2SourceKind.Chara) { r.NotChara++; return; }
        Card card;
        try { card = Card.Load(bytes, file); }
        catch (Exception) { r.NotChara++; return; }
        var rt = DocumentRoundTrip.Run(importer, bytes, file, gd);
        if (!rt.Ok) { r.ImportFailed++; r.FailLines.Add(rel + ": " + rt.Error); return; }
        r.AdapterMs.Add(rt.AdapterMs);
        var pc = new P1Card { Rel = rel, Import = rt.ImportFindings };
        try { CompareCard(gd, card, rt, pc); }
        catch (Exception e) { pc.Diff("comparison threw " + e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()); }
        r.Results[file] = pc;
        foreach (var kv in pc.Classes) r.Totals[kv.Key] = r.Totals.TryGetValue(kv.Key, out var n) ? n + kv.Value : kv.Value;
        r.Leaves += pc.Leaves; r.RefKeys += pc.RefKeys; r.Rows += pc.Rows; r.RowDiffs += pc.RowDiffs;
        if (pc.Unclassified.Count > 0)
        {
            r.UnclassifiedCards++;
            if (r.FailLines.Count < 200) r.FailLines.Add(rel + ": " + pc.Unclassified.Count + " unclassified: " + string.Join(" | ", pc.Unclassified.Take(12)));
        }
        else r.Ok++;
    }

    /// <summary>The P1 lines and its three checks.</summary>
    internal static void P1End(P1Run r)
    {
        var results = r.Results;
        var totals = r.Totals;
        Info($"cards {r.Files}: compared {results.Count}, not character cards {r.NotChara}, import failed {r.ImportFailed}; {r.Watch.Elapsed.TotalSeconds:F0} s");
        Info($"tree leaves compared {r.Leaves}, reference keys (compared through rows) {r.RefKeys}, part-key rows compared {r.Rows}, rows that differ {r.RowDiffs}");
        Info("classified differences: " + (totals.Count == 0 ? "none" : string.Join(", ", totals.Select(kv => kv.Key + " " + kv.Value))));
        var reasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var x in results.Values) foreach (var kv in x.Reasons) reasons[kv.Key] = reasons.TryGetValue(kv.Key, out var n) ? n + kv.Value : kv.Value;
        foreach (var kv in reasons) Info($"  {kv.Key}: {kv.Value} (cards {results.Values.Count(x => x.Reasons.ContainsKey(kv.Key))})");
        foreach (var cls in totals.Keys)
        {
            var ex = results.Values.Where(x => x.Classes.ContainsKey(cls)).Take(3).Select(x => x.Rel + ": " + string.Join("; ", x.Notes.Where(n => n.StartsWith(cls + " ", StringComparison.Ordinal)).Take(3)));
            Info("  " + cls + " e.g. " + string.Join(" || ", ex));
        }
        foreach (var l in r.FailLines) Info("FAILED " + l);
        Check("every character card imports and reloads", r.ImportFailed == 0, r.ImportFailed + " failed");
        Check("0 unclassified differences (trees, plugin data, part-key rows)", r.UnclassifiedCards == 0, r.UnclassifiedCards + " cards with unclassified differences");
        double med = Median(r.AdapterMs);
        Check("CharaDocumentInput.From median ≤ 5 ms (fails at 10 ms)", r.AdapterMs.Count == 0 || med <= 10, $"median {med:F2} ms, max {(r.AdapterMs.Count > 0 ? r.AdapterMs.Max() : 0):F1} ms over {r.AdapterMs.Count} cards");
    }

    /// <summary>A fresh report (AllCardsTest: the P1 checks of its walk), its text and counts.</summary>
    internal static void ReportBegin() => Reset();
    internal static string ReportText => rep?.ToString() ?? "";
    internal static (int passed, int failed) ReportCounts => (passes, fails);

    static void CompareCard(ContentCatalog gd, Card card, DocumentRoundTripResult rt, P1Card pc)
    {
        var a = rt.Input.Card;
        CompareNode(a.Parameter, card.Parameter, "Parameter", pc);
        CompareNode(a.Face, card.Face, "Face", pc);
        CompareNode(a.Body, card.Body, "Body", pc);
        CompareNode(a.Hair, card.Hair, "Hair", pc);
        CompareNode(a.Clothes, card.Clothes, "Clothes", pc);
        CompareAccessories(a.Accessory, card.Accessory, pc);
        CardExtData ce = null;
        try { ce = CardExtensions.Decode(card); } catch (Exception e) { pc.Diff("card ext decode threw " + e.Message); }
        if (ce != null) CompareExt(ce, rt.Input.Ext, rt, pc);
        CompareRows(gd, card, ce, rt, pc);
    }

    // ---------------------------------------------------------------- trees

    /// <summary>Keys whose values are references: compared through the rows of their part keys, not by raw id.</summary>
    static readonly HashSet<string> RefKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "headId", "skinId", "detailId", "eyebrowId", "pupilId", "blackId", "hlId", "eyelashesId", "moleId", "eyeshadowId", "cheekId", "lipId",
        "id", "layoutId", "beardId", "nipId", "underhairId", "sunburnId", "meshType", "pattern",
    };

    static string KeyText(object k) => k is string s ? s : Convert.ToString(k, CultureInfo.InvariantCulture);

    static void CompareNode(object a, object c, string path, P1Card pc)
    {
        if (a is Dictionary<object, object> am)
        {
            if (c == null) { pc.Class("memberInit", path + " (object absent)"); return; }
            if (!(c is Dictionary<object, object> cm)) { pc.Diff(path + ": card value is not a map"); return; }
            bool bundles = path.EndsWith(".dictBundle", StringComparison.Ordinal);
            foreach (var kv in am)
            {
                string p = path + "." + KeyText(kv.Key);
                if (!cm.TryGetValue(kv.Key, out var cv))
                {
                    if (bundles) { pc.Diff(p + ": bundle not in the card"); continue; }
                    pc.Class("memberInit", p);
                    continue;
                }
                if (!bundles && kv.Key is string ks && RefKeys.Contains(ks)) { pc.RefKeys++; continue; }
                CompareNode(kv.Value, cv, p, pc);
            }
            if (bundles)
                foreach (var ck in cm.Keys)
                    if (!am.ContainsKey(ck))
                    {
                        string part = path.Replace("Hair.parts[", "").Split(']')[0];
                        string name = int.TryParse(part, out int k) && k >= 0 && k < 4 ? PartKeys.HairNames[k] : "?";
                        if (pc.Has("hair.bundlesDropped", "hair." + name + ".bundles")) pc.Class("documentedDrop", path + " bundle " + KeyText(ck) + " (hair.bundlesDropped)");
                        else pc.Diff(path + "." + KeyText(ck) + ": card bundle missing in the documents");
                    }
            return;
        }
        if (a is List<object> al)
        {
            if (c == null) { pc.Class("memberInit", path + " (array absent)"); return; }
            if (!(c is List<object> cl)) { pc.Diff(path + ": card value is not an array"); return; }
            if (cl.Count != al.Count) { pc.Diff($"{path}: length {al.Count} vs card {cl.Count}"); return; }
            for (int i = 0; i < al.Count; i++) CompareNode(al[i], cl[i], path + "[" + i + "]", pc);
            return;
        }
        pc.Leaves++;
        if (c == null) { pc.Class("memberInit", path + " (nil)"); return; }
        switch (a)
        {
            case float af:
            {
                if (!(c is float || c is double || c is long || c is ulong)) { pc.Diff(path + ": card value " + c + " is not a number"); return; }
                float cf = c.Float();
                if (af == cf)
                {
                    if (BitConverter.SingleToInt32Bits(af) != BitConverter.SingleToInt32Bits(cf)) pc.Class("negativeZero", path);
                    return;
                }
                if (float.IsNaN(af) && float.IsNaN(cf)) return;
                pc.Diff($"{path}: {af.ToString("R", CultureInfo.InvariantCulture)} vs card {cf.ToString("R", CultureInfo.InvariantCulture)}");
                return;
            }
            case long al2:
                if (!(c is long || c is ulong)) { pc.Diff(path + ": card value " + c + " is not an integer"); return; }
                if (c.Int() != (int)al2) pc.Diff($"{path}: {al2} vs card {c}");
                return;
            case bool ab:
                if (!(c is bool cb)) { pc.Diff(path + ": card value is not a bool"); return; }
                if (ab != cb) pc.Diff($"{path}: {ab} vs card {cb}");
                return;
            case string s:
                if (!(c is string cs)) { pc.Diff(path + ": card value is not a string"); return; }
                if (s != cs) pc.Diff($"{path}: \"{s}\" vs card \"{cs}\"");
                return;
        }
        pc.Diff(path + ": unexpected adapter value type " + a?.GetType().Name);
    }

    static void CompareAccessories(object a, object c, P1Card pc)
    {
        var ap = a.Get("parts") as List<object>;
        var cp = c.Get("parts") as List<object>;
        if (cp == null) { pc.Class("memberInit", "Accessory.parts (absent)"); return; }
        for (int s = 0; s < ap.Count; s++)
        {
            var ae = ap[s];
            var ce = s < cp.Count ? cp[s] : null;
            int at = ae.Get("type").Int(350), ct = ce.Get("type").Int(120);
            bool cUsed = ct >= 351 && ct <= 363, aUsed = at >= 351 && at <= 363;
            if (!cUsed && !aUsed) { pc.Class("documentedDrop", $"Accessory.parts[{s}] empty slot (type {ct}; empty slots are not stored)"); continue; }
            if (cUsed != aUsed) { pc.Diff($"Accessory.parts[{s}]: type {at} vs card {ct}"); continue; }
            CompareNode(ae, ce, "Accessory.parts[" + s + "]", pc);
        }
        if (cp.Count > ap.Count)
            for (int s = ap.Count; s < cp.Count; s++)
            {
                int ct = cp[s].Get("type").Int(120);
                if (ct >= 351 && ct <= 363) pc.Diff($"Accessory.parts[{s}]: card slot beyond 20 not built");
            }
    }

    // ---------------------------------------------------------------- plugin data

    static bool Eq(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
    static bool Eq(Vector4 a, Vector4 b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
    static bool Eq(Color a, Color b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
    static bool Eq(Vector2? a, Vector2? b) => a.HasValue == b.HasValue && (!a.HasValue || (a.Value.x == b.Value.x && a.Value.y == b.Value.y));
    static bool Eq(Color? a, Color? b) => a.HasValue == b.HasValue && (!a.HasValue || Eq(a.Value, b.Value));
    static bool Eq(Vector4? a, Vector4? b) => a.HasValue == b.HasValue && (!a.HasValue || Eq(a.Value, b.Value));
    static bool StrEq(string a, string b) => (a ?? "") == (b ?? "");

    static void Field(P1Card pc, string path, bool equal, string detail = "")
    {
        pc.Leaves++;
        if (!equal) pc.Diff(path + (detail.Length > 0 ? ": " + detail : ""));
    }

    static void CompareExt(CardExtData c, CardExtData d, DocumentRoundTripResult rt, P1Card pc)
    {
        var slots = CharaDocumentInput.Slots(rt.Reloaded.Subtree);
        var used = new HashSet<int>(slots.EntryBySlot.Keys);
        CompareMoreAccessories(c.MoreAccessories, d.MoreAccessories, pc);
        CompareAbmx(c.Abmx, d.Abmx, used, pc);
        CompareUncensor(c.Uncensor, d.Uncensor, pc);
        CompareMe(c.MaterialEditor, d.MaterialEditor, used, pc);
        CompareKsox(c.SkinOverlays, d.SkinOverlays, pc);
        CompareKcox(c.ClothesOverlays, d.ClothesOverlays, pc);
        ComparePushUp(c.PushUp, d.PushUp, pc);
        CompareDbe(c.DynamicBoneEditor, d.DynamicBoneEditor, used, pc);
        Field(pc, "ext.InvisibleBody.Visible", (c.InvisibleBody?.Visible ?? true) == (d.InvisibleBody?.Visible ?? true));
        CompareAac(c.AdditionalAccessoryControls, d.AdditionalAccessoryControls, used, slots, pc);
        if (d.Opaque.Count != 0 || d.Resolve.Count != 0) pc.Diff("ext: the adapter's Opaque / Resolve are not empty");
        if (c.Opaque.Count > 0) pc.Class("documentedDrop", "ext opaque entries " + string.Join(",", c.Opaque.Select(o => o.Guid)) + " (hs2.kkex only, never applied)");
    }

    static void CompareMoreAccessories(MoreAccessoriesData c, MoreAccessoriesData d, P1Card pc)
    {
        var cs = (c?.Parts ?? new List<MoreAccessoriesData.AccessoryPart>()).Where(p => !p.IsEmpty).ToDictionary(p => p.Slot);
        var ds = (d?.Parts ?? new List<MoreAccessoriesData.AccessoryPart>()).Where(p => !p.IsEmpty).ToDictionary(p => p.Slot);
        foreach (var kv in cs)
        {
            string p = "ext.MoreAccessories[" + kv.Key + "]";
            if (!ds.TryGetValue(kv.Key, out var x))
            {
                if (pc.Has("accessory.duplicateSlot")) pc.Class("documentedDrop", p + " (duplicate slot)");
                else pc.Diff(p + ": slot missing in the documents");
                continue;
            }
            var y = kv.Value;
            Field(pc, p + ".Type", x.Type == y.Type, x.Type + " vs " + y.Type);
            if (y.ParentKey == null && x.ParentKey == "") pc.Class("memberInit", p + ".ParentKey");
            else Field(pc, p + ".ParentKey", x.ParentKey == y.ParentKey, x.ParentKey + " vs " + y.ParentKey);
            for (int j = 0; j < 2; j++) for (int k = 0; k < 3; k++) Field(pc, $"{p}.AddMove[{j},{k}]", Eq(x.AddMove[j, k], y.AddMove[j, k]), x.AddMove[j, k] + " vs " + y.AddMove[j, k]);
            for (int l = 0; l < 4; l++)
            {
                Field(pc, $"{p}.Color[{l}]", Eq(x.Color[l], y.Color[l]), x.Color[l] + " vs " + y.Color[l]);
                Field(pc, $"{p}.Gloss[{l}]", x.GlossPower[l] == y.GlossPower[l]);
                Field(pc, $"{p}.Metallic[{l}]", x.MetallicPower[l] == y.MetallicPower[l]);
                Field(pc, $"{p}.Smoothness[{l}]", x.SmoothnessPower[l] == y.SmoothnessPower[l]);
            }
            Field(pc, p + ".HideCategory", x.HideCategory == y.HideCategory);
            Field(pc, p + ".HideTiming", x.HideTiming == y.HideTiming);
            Field(pc, p + ".NoShake", x.NoShake == y.NoShake);
            if (y.Show.HasValue) pc.Class("documentedDrop", p + ".Show (MoreAccessories show)");
        }
        foreach (var k in ds.Keys) if (!cs.ContainsKey(k)) pc.Diff("ext.MoreAccessories[" + k + "]: slot not in the card");
    }

    static void CompareAbmx(AbmxData c, AbmxData d, HashSet<int> used, P1Card pc)
    {
        var cl = new List<AbmxData.BoneModifier>();
        if (c != null)
            foreach (var m in c.Modifiers)
            {
                if (string.IsNullOrEmpty(m.BoneName)) { pc.Class("documentedDrop", "ext.Abmx modifier without a bone (abmx.noBone)"); continue; }
                if (m.Location == 0 || m.Location == 1) { cl.Add(m); continue; }
                if (m.AccessorySlot >= 0 && used.Contains(m.AccessorySlot)) { cl.Add(m); continue; }
                pc.Class("documentedDrop", $"ext.Abmx {m.BoneName} at location {m.Location} (abmx.slotEmpty / badLocation)");
            }
        var dl = d?.Modifiers ?? new List<AbmxData.BoneModifier>();
        string Key(AbmxData.BoneModifier m) => m.Location + "|" + m.BoneName;
        var cg = cl.GroupBy(Key).ToDictionary(g => g.Key, g => g.ToList());
        var dg = dl.GroupBy(Key).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var kv in cg)
        {
            string p = "ext.Abmx[" + kv.Key + "]";
            if (!dg.TryGetValue(kv.Key, out var dm) || dm.Count != kv.Value.Count) { pc.Diff($"{p}: {kv.Value.Count} card modifiers vs {(dm?.Count ?? 0)}"); continue; }
            for (int i = 0; i < dm.Count; i++)
            {
                var x = dm[i].Data; var y = kv.Value[i].Data;
                Field(pc, p + ".Scale", Eq(x.Scale, y.Scale), x.Scale + " vs " + y.Scale);
                Field(pc, p + ".Length", x.Length == y.Length);
                Field(pc, p + ".Position", Eq(x.Position, y.Position));
                Field(pc, p + ".Rotation", Eq(x.Rotation, y.Rotation));
                if (kv.Value[i].CoordinateModifiers.Count > 1) pc.Class("documentedDrop", p + " CoordinateModifiers beyond [0] (abmx.extraSets)");
            }
        }
        foreach (var k in dg.Keys) if (!cg.ContainsKey(k)) pc.Diff("ext.Abmx[" + k + "]: not in the card");
    }

    static void CompareUncensor(HS2Next.Data.Ext.UncensorData c, HS2Next.Data.Ext.UncensorData d, P1Card pc)
    {
        if (c == null && d == null) return;
        if (c == null || d == null) { pc.Diff("ext.Uncensor: " + (c == null ? "card has none" : "documents have none")); return; }
        Field(pc, "ext.Uncensor.BodyGUID", c.BodyGUID == d.BodyGUID, c.BodyGUID + " vs " + d.BodyGUID);
        Field(pc, "ext.Uncensor.PenisGUID", c.PenisGUID == d.PenisGUID);
        Field(pc, "ext.Uncensor.BallsGUID", c.BallsGUID == d.BallsGUID);
        Field(pc, "ext.Uncensor.DisplayBalls", c.DisplayBalls == d.DisplayBalls);
        Field(pc, "ext.Uncensor.Legacy", c.LegacyUncensorGUID == d.LegacyUncensorGUID);
        if (c.DisplayPenis.HasValue) pc.Class("documentedDrop", "ext.Uncensor.DisplayPenis (derived at load)");
    }

    static string MeKind(MaterialEditorData.Item i) => i.GetType().Name;

    static void CompareMe(MaterialEditorData c, MaterialEditorData d, HashSet<int> used, P1Card pc)
    {
        var cl = new List<MaterialEditorData.Item>();
        if (c != null)
            foreach (var it in c.AllItems())
            {
                bool keep = it.CoordinateIndex == 0 && (
                    it.ObjectType == MaterialEditorData.ObjectType.Character ||
                    it.ObjectType == MaterialEditorData.ObjectType.Hair && it.Slot >= 0 && it.Slot < 4 ||
                    it.ObjectType == MaterialEditorData.ObjectType.Clothing && it.Slot >= 0 && it.Slot < 8 ||
                    it.ObjectType == MaterialEditorData.ObjectType.Accessory && used.Contains(it.Slot));
                if (keep) cl.Add(it);
                else pc.Class("documentedDrop", $"ext.ME {MeKind(it)} {it.ObjectType}/{it.Slot}/coord {it.CoordinateIndex} (me.*)");
            }
        var dl = d != null ? d.AllItems().ToList() : new List<MaterialEditorData.Item>();
        string Key(MaterialEditorData.Item i) => (int)i.ObjectType + "|" + i.Slot + "|" + MeKind(i);
        var cg = cl.GroupBy(Key).ToDictionary(g => g.Key, g => g.ToList());
        var dg = dl.GroupBy(Key).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var kv in cg)
        {
            string p = "ext.ME[" + kv.Key + "]";
            if (!dg.TryGetValue(kv.Key, out var dm) || dm.Count != kv.Value.Count) { pc.Diff($"{p}: {kv.Value.Count} card items vs {(dm?.Count ?? 0)}"); continue; }
            for (int i = 0; i < dm.Count; i++) CompareMeItem(kv.Value[i], c, dm[i], d, p + "[" + i + "]", pc);
        }
        foreach (var k in dg.Keys) if (!cg.ContainsKey(k)) pc.Diff("ext.ME[" + k + "]: not in the card");
    }

    static string Tex(MaterialEditorData me, int? id) => id.HasValue && me != null && me.Textures.TryGetValue(id.Value, out var b) && b != null && b.Length > 0 ? Sha(b) : null;

    static void CompareMeItem(MaterialEditorData.Item ci, MaterialEditorData c, MaterialEditorData.Item di, MaterialEditorData d, string p, P1Card pc)
    {
        if (ci is MaterialEditorData.MaterialItem cm && di is MaterialEditorData.MaterialItem dm2) Field(pc, p + ".Material", StrEq(cm.MaterialName, dm2.MaterialName));
        if (ci is MaterialEditorData.MaterialPropertyItem cp && di is MaterialEditorData.MaterialPropertyItem dp) Field(pc, p + ".Property", StrEq(cp.Property, dp.Property));
        switch (ci)
        {
            case MaterialEditorData.MaterialCopy x: { var y = (MaterialEditorData.MaterialCopy)di; Field(pc, p + ".Copy", StrEq(x.MaterialCopyName, y.MaterialCopyName)); break; }
            case MaterialEditorData.NameProperty x:
            {
                var y = (MaterialEditorData.NameProperty)di;
                Field(pc, p + ".Renderer", StrEq(x.Renderer, y.Renderer)); Field(pc, p + ".Name", StrEq(x.MaterialName, y.MaterialName));
                Field(pc, p + ".Value", StrEq(x.Value, y.Value)); Field(pc, p + ".Original", x.ValueOriginal == y.ValueOriginal);
                break;
            }
            case MaterialEditorData.ShaderEdit x:
            {
                var y = (MaterialEditorData.ShaderEdit)di;
                Field(pc, p + ".Shader", x.ShaderName == y.ShaderName, x.ShaderName + " vs " + y.ShaderName);
                Field(pc, p + ".ShaderOriginal", x.ShaderNameOriginal == y.ShaderNameOriginal);
                Field(pc, p + ".RenderQueue", x.RenderQueue == y.RenderQueue && x.RenderQueueOriginal == y.RenderQueueOriginal);
                break;
            }
            case MaterialEditorData.RendererProperty x:
            {
                var y = (MaterialEditorData.RendererProperty)di;
                Field(pc, p + ".Renderer", StrEq(x.RendererName, y.RendererName)); Field(pc, p + ".RendererProperty", x.Property == y.Property);
                Field(pc, p + ".Value", IntText(x.Value) == IntText(y.Value), x.Value + " vs " + y.Value);
                Field(pc, p + ".Original", IntText(x.ValueOriginal) == IntText(y.ValueOriginal), x.ValueOriginal + " vs " + y.ValueOriginal);
                break;
            }
            case MaterialEditorData.FloatProperty x:
            {
                var y = (MaterialEditorData.FloatProperty)di;
                Field(pc, p + ".Value", FloatText(x.Value) == FloatText(y.Value), x.Value + " vs " + y.Value);
                Field(pc, p + ".Original", FloatText(x.ValueOriginal) == FloatText(y.ValueOriginal), x.ValueOriginal + " vs " + y.ValueOriginal);
                break;
            }
            case MaterialEditorData.KeywordProperty x: { var y = (MaterialEditorData.KeywordProperty)di; Field(pc, p + ".Value", x.Value == y.Value && x.ValueOriginal == y.ValueOriginal); break; }
            case MaterialEditorData.ColorProperty x: { var y = (MaterialEditorData.ColorProperty)di; Field(pc, p + ".Value", Eq(x.Value, y.Value) && Eq(x.ValueOriginal, y.ValueOriginal)); break; }
            case MaterialEditorData.VectorProperty x: { var y = (MaterialEditorData.VectorProperty)di; Field(pc, p + ".Value", Eq(x.Value, y.Value) && Eq(x.ValueOriginal, y.ValueOriginal)); break; }
            case MaterialEditorData.TextureProperty x:
            {
                var y = (MaterialEditorData.TextureProperty)di;
                string tc = Tex(c, x.TexID), td = Tex(d, y.TexID);
                if (tc != td)
                {
                    if (x.TexID.HasValue && tc == null && !y.TexID.HasValue) pc.Class("documentedDrop", p + " texture missing from the card's TextureDictionary (me.missingTexture)");
                    else pc.Diff(p + ".Texture: " + tc + " vs " + td);
                }
                else pc.Leaves++;
                Field(pc, p + ".Offset", Eq(x.Offset, y.Offset) && Eq(x.OffsetOriginal, y.OffsetOriginal));
                Field(pc, p + ".Scale", Eq(x.Scale, y.Scale) && Eq(x.ScaleOriginal, y.ScaleOriginal));
                if (x.TexAnimationDef != null) pc.Class("documentedDrop", p + " TexAnimationDef (me.texAnimation)");
                break;
            }
            case MaterialEditorData.CubemapProperty x:
            {
                var y = (MaterialEditorData.CubemapProperty)di;
                string tc = Tex(c, x.TexID), td = Tex(d, y.TexID);
                if (tc != td)
                {
                    if (x.TexID.HasValue && tc == null && !y.TexID.HasValue) pc.Class("documentedDrop", p + " cubemap missing from the card's TextureDictionary (me.missingTexture)");
                    else pc.Diff(p + ".Cubemap: " + tc + " vs " + td);
                }
                else pc.Leaves++;
                break;
            }
            case MaterialEditorData.ProjectorProperty x:
            {
                var y = (MaterialEditorData.ProjectorProperty)di;
                Field(pc, p + ".Projector", StrEq(x.ProjectorName, y.ProjectorName) && x.Property == y.Property && StrEq(x.Value, y.Value) && x.ValueOriginal == y.ValueOriginal);
                break;
            }
        }
    }

    static string IntText(string s) => s != null && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? "i" + v : s;
    static string FloatText(string s) => s != null && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && !float.IsNaN(v) && !float.IsInfinity(v)
        ? "f" + BitConverter.SingleToInt32Bits(v == 0 ? 0f : v) : s;

    static void CompareKsox(SkinOverlayData c, SkinOverlayData d, P1Card pc)
    {
        var ct = c?.Textures ?? new Dictionary<SkinTexType, byte[]>();
        var dt = d?.Textures ?? new Dictionary<SkinTexType, byte[]>();
        foreach (var kv in ct)
        {
            string p = "ext.KSOX[" + kv.Key + "]";
            if (!Enum.IsDefined(typeof(SkinOverlayTarget), (int)kv.Key)) { pc.Class("documentedDrop", p + " (TexType without a target: ksox.unknownType)"); continue; }
            if (!dt.TryGetValue(kv.Key, out var b)) { pc.Diff(p + ": missing in the documents"); continue; }
            Field(pc, p, Sha(kv.Value) == Sha(b));
        }
        foreach (var k in dt.Keys) if (!ct.ContainsKey(k)) pc.Diff("ext.KSOX[" + k + "]: not in the card");
        bool cSkin = c == null || c.Version < 2 || c.EnableInStudioSkin, cIris = c == null || c.Version < 2 || c.EnableInStudioIris;
        Field(pc, "ext.KSOX.EnableInStudioSkin", cSkin == (d?.EnableInStudioSkin ?? true));
        Field(pc, "ext.KSOX.EnableInStudioIris", cIris == (d?.EnableInStudioIris ?? true));
    }

    static void CompareKcox(ClothesOverlayData c, ClothesOverlayData d, P1Card pc)
    {
        var co = c?.Overlays ?? new Dictionary<string, ClothesTexData>();
        var dov = d?.Overlays ?? new Dictionary<string, ClothesTexData>();
        bool Known(string id) => ClothesOverlayData.MainClothesNames.Any(n => id == n || id == "Colormask_" + n || id == "Override_" + n || id == "Pattern_0_" + n || id == "Pattern_1_" + n || id == "Pattern_2_" + n);
        foreach (var kv in co)
        {
            string p = "ext.KCOX[" + kv.Key + "]";
            if (!Known(kv.Key)) { pc.Class("documentedDrop", p + " (id names no clothes kind: kcox.unknownId)"); continue; }
            if (!dov.TryGetValue(kv.Key, out var y)) { pc.Diff(p + ": missing in the documents"); continue; }
            Field(pc, p + ".Texture", Sha(kv.Value.TextureBytes) == Sha(y.TextureBytes));
            Field(pc, p + ".Override", kv.Value.Override == y.Override);
            Field(pc, p + ".BlendingMode", kv.Value.BlendingMode == y.BlendingMode);
        }
        foreach (var k in dov.Keys) if (!co.ContainsKey(k)) pc.Diff("ext.KCOX[" + k + "]: not in the card");
        var cs = c?.TextureSizeOverride ?? new Dictionary<string, int>();
        var ds = d?.TextureSizeOverride ?? new Dictionary<string, int>();
        foreach (var kv in cs)
        {
            if (Array.IndexOf(ClothesOverlayData.MainClothesNames, kv.Key) < 0) { pc.Class("documentedDrop", "ext.KCOX size " + kv.Key + " (kcox.unknownId)"); continue; }
            Field(pc, "ext.KCOX.size[" + kv.Key + "]", ds.TryGetValue(kv.Key, out int v) && v == kv.Value);
        }
        foreach (var k in ds.Keys) if (!cs.ContainsKey(k)) pc.Diff("ext.KCOX.size[" + k + "]: not in the card");
        Field(pc, "ext.KCOX.EnableInStudio", (c?.EnableInStudio ?? true) == (d?.EnableInStudio ?? true));
    }

    static void ComparePushUp(PushUpData c, PushUpData d, P1Card pc)
    {
        if (c == null && d == null) return;
        if (c == null || d == null) { pc.Diff("ext.PushUp: " + (c == null ? "card has none" : "documents have none")); return; }
        Field(pc, "ext.PushUp.Bra", c.Bra.Equals(d.Bra));
        Field(pc, "ext.PushUp.Top", c.Top.Equals(d.Top));
        if (c.OtherKeys.Count > 0) pc.Class("documentedDrop", "ext.PushUp other keys (pushup.otherKeys)");
    }

    static void CompareDbe(DynamicBoneEditorData c, DynamicBoneEditorData d, HashSet<int> used, P1Card pc)
    {
        var cl = new List<DynamicBoneEditorData.Entry>();
        if (c != null)
            foreach (var e in c.Entries)
            {
                if (e.CoordinateIndex != 0 || !used.Contains(e.Slot) || e.BoneName == null) { pc.Class("documentedDrop", $"ext.DBE slot {e.Slot} coord {e.CoordinateIndex} (dbe.*)"); continue; }
                cl.Add(e);
            }
        var dl = d?.Entries ?? new List<DynamicBoneEditorData.Entry>();
        string Key(DynamicBoneEditorData.Entry e) => e.Slot + "|" + e.BoneName;
        var cg = cl.GroupBy(Key).ToDictionary(g => g.Key, g => g.ToList());
        var dg = dl.GroupBy(Key).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var kv in cg)
        {
            string p = "ext.DBE[" + kv.Key + "]";
            if (!dg.TryGetValue(kv.Key, out var dm) || dm.Count != kv.Value.Count) { pc.Diff($"{p}: {kv.Value.Count} card entries vs {(dm?.Count ?? 0)}"); continue; }
            for (int i = 0; i < dm.Count; i++)
            {
                var x = kv.Value[i]; var y = dm[i];
                Field(pc, p + ".values", x.FreezeAxis == y.FreezeAxis && x.Weight == y.Weight && x.Damping == y.Damping && x.Elasticity == y.Elasticity
                    && x.Stiffness == y.Stiffness && x.Inertia == y.Inertia && x.Radius == y.Radius);
                Field(pc, p + ".originals", x.FreezeAxisOriginal == y.FreezeAxisOriginal && x.WeightOriginal == y.WeightOriginal && x.DampingOriginal == y.DampingOriginal
                    && x.ElasticityOriginal == y.ElasticityOriginal && x.StiffnessOriginal == y.StiffnessOriginal && x.InertiaOriginal == y.InertiaOriginal && x.RadiusOriginal == y.RadiusOriginal);
            }
        }
        foreach (var k in dg.Keys) if (!cg.ContainsKey(k)) pc.Diff("ext.DBE[" + k + "]: not in the card");
    }

    static void CompareAac(AdditionalAccessoryControlsData c, AdditionalAccessoryControlsData d, HashSet<int> used, AccessorySlotTable slots, P1Card pc)
    {
        var cl = new Dictionary<int, AdditionalAccessoryControlsData.SlotData>();
        if (c != null)
            foreach (var s in c.Slots)
            {
                if (s.IsEmpty) continue;
                if (!used.Contains(s.SlotNumber)) { pc.Class("documentedDrop", "ext.AAC slot " + s.SlotNumber + " without an accessory (aac.slotEmpty)"); continue; }
                cl[s.SlotNumber] = s;
            }
        var dl = (d?.Slots ?? new List<AdditionalAccessoryControlsData.SlotData>()).ToDictionary(s => s.SlotNumber);
        foreach (var kv in cl)
        {
            string p = "ext.AAC[" + kv.Key + "]";
            if (!dl.TryGetValue(kv.Key, out var y)) { pc.Diff(p + ": missing in the documents"); continue; }
            var x = kv.Value;
            Field(pc, p + ".CharacterAccessory", x.CharacterAccessory == y.CharacterAccessory);
            Field(pc, p + ".AccessoryName", x.AccessoryName == y.AccessoryName, x.AccessoryName + " vs " + y.AccessoryName);
            Field(pc, p + ".MatchHairColor", x.AutoMatchBackHairColor == y.AutoMatchBackHairColor);
            Field(pc, p + ".AdvancedParent", x.AdvancedParent == y.AdvancedParent);
            if (x.VisibilityRules.Count != y.VisibilityRules.Count) { pc.Diff($"{p}: {x.VisibilityRules.Count} card rules vs {y.VisibilityRules.Count}"); continue; }
            for (int i = 0; i < x.VisibilityRules.Count; i++)
            {
                var a = x.VisibilityRules[i]; var b = y.VisibilityRules[i];
                Field(pc, $"{p}.rule[{i}]", a.Rule == b.Rule, a + " vs " + b);
                if (a.Modifier == b.Modifier) { pc.Leaves++; continue; }
                bool link = a.Rule == AdditionalAccessoryControlsData.Rule.ACCESSORY_LINK || a.Rule == AdditionalAccessoryControlsData.Rule.ACCESSORY_INVERSE_LINK;
                if (link && b.Modifier == "0" && int.TryParse(a.Modifier, out int t) && !used.Contains(t - 1)) pc.Class("documentedDrop", $"{p}.rule[{i}] link to empty slot {a.Modifier} (aac.linkEmpty)");
                else pc.Diff($"{p}.rule[{i}]: modifier {a.Modifier} vs {b.Modifier}");
            }
        }
        foreach (var k in dl.Keys) if (!cl.ContainsKey(k)) pc.Diff("ext.AAC[" + k + "]: not in the card");
        CompareRules("suppressed", c?.SuppressedRules, d?.SuppressedRules, pc);
        CompareRules("overrides", c?.OverrideRules, d?.OverrideRules, pc);
    }

    static void CompareRules(string what, List<AdditionalAccessoryControlsData.CoordinateRule> c, List<AdditionalAccessoryControlsData.CoordinateRule> d, P1Card pc)
    {
        c ??= new List<AdditionalAccessoryControlsData.CoordinateRule>(); d ??= new List<AdditionalAccessoryControlsData.CoordinateRule>();
        if (c.Count != d.Count) { pc.Diff($"ext.AAC.{what}: {c.Count} vs {d.Count}"); return; }
        for (int i = 0; i < c.Count; i++)
            Field(pc, $"ext.AAC.{what}[{i}]", c[i].Rule == d[i].Rule && c[i].RuleModifier == d[i].RuleModifier && c[i].OverrideRule == d[i].OverrideRule && c[i].OverrideRuleModifier == d[i].OverrideRuleModifier);
    }

    // ---------------------------------------------------------------- part-key rows

    /// <summary>
    /// The document row of every part key against the S1 baseline of the card: its "ctx|ref.&lt;part key&gt;" lines (category,
    /// ref, resolved row), written from the same document path. A row that differs, a part the baseline lacks, or a baseline
    /// part the documents lack is an unclassified difference; a card without a baseline is one.
    /// </summary>
    static void CompareRows(ContentCatalog gd, Card card, CardExtData ce, DocumentRoundTripResult rt, P1Card pc)
    {
        var docRes = (DocumentPartResolver)rt.Input.Parts;
        string sha = null;   // the baselines are named by the card file's full SHA-256
        if (card?.Path != null && File.Exists(card.Path))
            using (var h = SHA256.Create()) sha = BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(card.Path))).Replace("-", "").ToLowerInvariant();
        var baseline = sha != null ? MakerCoreTest.LoadBaseline(sha) : null;
        if (baseline == null) { pc.Diff("no S1 baseline for the card (MakerCoreTest.BaselinePath " + (sha ?? "?") + ")"); return; }
        var want = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var l in baseline.Lines)
        {
            if (!l.StartsWith("ctx|ref.", StringComparison.Ordinal)) continue;
            int bar = l.IndexOf('|', 8);
            if (bar > 0) want[l.Substring(8, bar - 8)] = l.Substring(bar + 1);
        }
        string Esc(string x) => x == null ? "null" : x.Replace('|', '/').Replace(' ', '_').Replace('\n', '_');
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in docRes.Parts.Values.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            pc.Rows++;
            ListEntry row = null;
            if (ContentId.TryParse(p.Ref, out var id) && !id.IsEmpty) row = gd.GetEntry(p.Category, id);
            string key = Esc(p.Key);
            string now = "cat=" + p.Category.ToString(CultureInfo.InvariantCulture) + " ref=" + Esc(p.Ref) + " row=" + (row == null ? "null" : row.Category.ToString(CultureInfo.InvariantCulture) + ":" + row.Key);
            seen.Add(key);
            if (want.TryGetValue(key, out var was) && was == now) continue;
            pc.RowDiffs++;
            pc.Diff($"{p.Key}: document {now} vs S1 baseline {was ?? "(absent)"}");
        }
        foreach (var k in want.Keys)
            if (!seen.Contains(k)) { pc.RowDiffs++; pc.Diff($"{k}: in the S1 baseline, not among the document parts"); }
    }

    /// <summary>
    /// The part-key rows of a document build that differ from the card's S1 baseline (CardImportTest -source document); null
    /// when every row equals the baseline's. The differences are listed as "UNCLASSIFIED".
    /// </summary>
    public static string RowSummary(ContentCatalog gd, Card card, DocumentRoundTripResult rt, out int unclassified)
    {
        var pc = new P1Card { Import = rt.ImportFindings };
        CompareRows(gd, card, null, rt, pc);
        unclassified = pc.Unclassified.Count;
        if (pc.RowDiffs == 0 && pc.Unclassified.Count == 0) return null;
        return pc.RowDiffs + " rows differ from the S1 baseline"
            + (pc.Unclassified.Count > 0 ? "; UNCLASSIFIED " + string.Join(" | ", pc.Unclassified) : "");
    }

    // ---------------------------------------------------------------- adapter cost on the 45.5 MB synthetic coordinate

    /// <summary>CharaDocumentInput.From ≤ 30 ms for the 45.5 MB synthetic coordinate of SaveFormatTest T14 (one 40 MB and
    /// 100 × 55 KB MaterialEditor textures on 101 accessories); byte arrays are referenced, not copied or decoded.</summary>
    static void BigCoordinate(ContentCatalog gd)
    {
        Section("P1", "adapter cost");
        var c = new CharaContainer();
        c.Chara.Profile.Sex = 1;
        byte[] Png(int size, int seed)
        {
            var b = new byte[size];
            new System.Random(seed).NextBytes(b);
            Array.Copy(PngInfo.Signature, b, 8);
            return b;
        }
        var acc = new AccessoryEntry { Id = JsonText.DerivedId("big/1"), Category = 352, Item = "test.mod#1", ImportSlot = 0 };
        acc.MaterialOverrides.Add(new MeTexture { Material = "mat", Property = "MainTex", Image = c.Res.Add(Png(40 * 1000 * 1000, 1)) });
        c.Coord.Accessories.Add(acc);
        for (int i = 0; i < 100; i++)
        {
            var a = new AccessoryEntry { Id = JsonText.DerivedId("big/" + (10 + i)), Category = 353, Item = "test.mod#" + (i + 2), ImportSlot = i + 1 };
            a.MaterialOverrides.Add(new MeTexture { Material = "m" + i, Property = "OverTex", Image = c.Res.Add(Png(55 * 1000, 100 + i)) });
            c.Coord.Accessories.Add(a);
        }
        CharaDocumentInput.From(c, gd, new FindingList());   // warm-up
        var ms = new List<double>();
        CharaBuildInput input = null;
        for (int i = 0; i < 5; i++)
        {
            var sw = Stopwatch.StartNew();
            input = CharaDocumentInput.From(c, gd, new FindingList());
            ms.Add(sw.Elapsed.TotalMilliseconds);
        }
        long res = c.Res.TotalBytes;
        bool referenced = input?.Ext?.MaterialEditor != null && input.Ext.MaterialEditor.Textures.Values.Any(b => ReferenceEquals(b, c.Res.Get(acc.MaterialOverrides[0] is MeTexture t ? t.Image : null)));
        Check("CharaDocumentInput.From ≤ 30 ms for the 45.5 MB synthetic coordinate (fails at 60 ms)", Median(ms) <= 60,
              $"median {Median(ms):F2} ms, {res / 1e6:F1} MB of res, {c.Coord.Accessories.Count} accessories, MoreAccessories slots {input?.Ext?.MoreAccessories?.Parts.Count ?? 0}");
        Check("the adapter references the res bytes (no copy)", referenced);
    }

    // ================================================================ P2 build parity

    /// <summary>Records every lookup of a build (part key → row) and delegates.</summary>
    sealed class RecordingResolver : ICharaPartResolver
    {
        public readonly ICharaPartResolver Inner;
        public readonly List<string> Calls = new List<string>();
        public RecordingResolver(ICharaPartResolver inner) { Inner = inner; }
        public ListEntry Resolve(int category, int rawId, string property, string part)
        {
            var r = Inner.Resolve(category, rawId, property, part);
            Calls.Add((part ?? "(default " + category + ":" + rawId + ")") + " -> " + RowText(r));
            return r;
        }
    }

    /// <summary>What P2 compares of one build.</summary>
    sealed class Snapshot
    {
        public List<string> Rows, Slots, Transforms, TransformsAfterStep, Renderers, Materials;
        public string ExtSummary, Error;
        public double Ms;
        public int PartsLoaded;
        // MaterialEditor totals (the documented-drop rule) and the material checks
        public int MeItems, MeApplied;
        public SortedDictionary<string, int> MeSkipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Renderer materials the build did not make (shared), by name.</summary>
        public List<string> Shared = new List<string>();
        /// <summary>Renderer materials whose keywords disagree with their toggle drawers (all / keyword declared by the shader).</summary>
        public int DrawerMaterials, DrawerValid;
    }

    // Keyword states the shader's toggle drawers imply for the material's values ([Toggle] / [MaterialToggle]: NAME_ON or the
    // given keyword while the value is not 0; [ToggleOff]: NAME_OFF or the given keyword while it is 0) that the material does
    // not have: what the Unity Editor's property drawers would switch (Runtime/Rendering/MaterialCopies.cs). valid: the
    // keyword is declared by the shader (the switch changes the variant).
    static int DrawerMismatches(Material m, out int valid)
    {
        valid = 0;
        var sh = m ? m.shader : null;
        if (!sh) return 0;
        var on = new HashSet<string>(m.shaderKeywords, StringComparer.Ordinal);
        int n = 0;
        for (int p = 0; p < sh.GetPropertyCount(); p++)
        {
            var t = sh.GetPropertyType(p);
            if (t != ShaderPropertyType.Float && t != ShaderPropertyType.Range) continue;
            string name = sh.GetPropertyName(p);
            foreach (var a in sh.GetPropertyAttributes(p))
            {
                bool off = a.StartsWith("ToggleOff", StringComparison.Ordinal);
                if (!off && !a.StartsWith("Toggle", StringComparison.Ordinal) && !a.StartsWith("MaterialToggle", StringComparison.Ordinal)) continue;
                if (a.StartsWith("ToggleUI", StringComparison.Ordinal)) continue;
                int open = a.IndexOf('(');
                string kw = open > 0 && a.EndsWith(")", StringComparison.Ordinal) ? a.Substring(open + 1, a.Length - open - 2).Trim() : name.ToUpperInvariant() + (off ? "_OFF" : "_ON");
                if (kw.Length == 0) continue;
                bool want = (m.GetFloat(name) != 0) != off;
                if (want == on.Contains(kw)) continue;
                n++;
                if (sh.keywordSpace.FindKeyword(kw).isValid) valid++;
            }
        }
        return n;
    }

    static string T(float f) => BitConverter.SingleToInt32Bits(f).ToString("x8");
    static string T(Vector3 v) => T(v.x) + "," + T(v.y) + "," + T(v.z);
    static string T(Quaternion q) => T(q.x) + "," + T(q.y) + "," + T(q.z) + "," + T(q.w);
    static string T(Vector4 v) => T(v.x) + "," + T(v.y) + "," + T(v.z) + "," + T(v.w);
    static string T(Color c) => T(c.r) + "," + T(c.g) + "," + T(c.b) + "," + T(c.a);

    static List<string> Transforms(Transform root)
    {
        var l = new List<string>();
        void Walk(Transform t, string path)
        {
            l.Add(path + " " + T(t.localPosition) + " " + T(t.localRotation) + " " + T(t.localScale) + (t.gameObject.activeSelf ? "" : " inactive"));
            for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), path + "/" + i + ":" + t.GetChild(i).name);
        }
        Walk(root, root.name);
        return l;
    }

    static string PathOf(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (var x = t; x && x != root; x = x.parent) parts.Add(x.GetSiblingIndex() + ":" + x.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    static void MaterialsOf(GameObject root, List<string> renderers, List<string> mats)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            string rp = PathOf(r.transform, root.transform);
            var shared = r.sharedMaterials;
            renderers.Add(rp + " " + r.GetType().Name + " enabled " + r.enabled + " materials " + shared.Length);
            for (int i = 0; i < shared.Length; i++)
            {
                var m = shared[i];
                if (!m) { mats.Add(rp + "[" + i + "] null"); continue; }
                var sb = new StringBuilder(rp + "[" + i + "] " + m.name + " shader " + (m.shader ? m.shader.name : "none") + " queue " + m.renderQueue + " keywords " + string.Join(",", m.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal)));
                var sh = m.shader;
                if (sh)
                    for (int p = 0; p < sh.GetPropertyCount(); p++)
                    {
                        string n = sh.GetPropertyName(p);
                        switch (sh.GetPropertyType(p))
                        {
                            case ShaderPropertyType.Float: case ShaderPropertyType.Range: sb.Append(" " + n + "=" + T(m.GetFloat(n))); break;
                            case ShaderPropertyType.Int: sb.Append(" " + n + "=" + m.GetInteger(n)); break;
                            case ShaderPropertyType.Color: sb.Append(" " + n + "=" + T(m.GetColor(n))); break;
                            case ShaderPropertyType.Vector: sb.Append(" " + n + "=" + T(m.GetVector(n))); break;
                            case ShaderPropertyType.Texture:
                                var t = m.GetTexture(n);
                                // MaterialEditor textures are named after their TexID ("me tex <id>"), which the documents renumber
                                // (first use): compared without the number (P1 compares the bytes).
                                string tn = t ? (t.name.StartsWith("me tex ", StringComparison.Ordinal) ? "me tex" : t.name) : null;
                                sb.Append(" " + n + "=" + (t ? tn + "(" + t.width + "x" + t.height + ")" : "null"));
                                break;
                        }
                    }
                mats.Add(sb.ToString());
            }
        }
    }

    static Snapshot Build(ContentCatalog gd, CharaBuildInput input)
    {
        var snap = new Snapshot();
        var before = new HashSet<GameObject>(SceneManager.GetActiveScene().GetRootGameObjects());
        var rec = new RecordingResolver(input.Parts);
        var inner = input.Parts;
        input.Parts = rec;
        var asm = new CharaAssembler(gd);
        var sw = Stopwatch.StartNew();
        try
        {
            var root = asm.Build(input);
            snap.Ms = sw.Elapsed.TotalMilliseconds;
            snap.PartsLoaded = asm.PartsLoaded;
            snap.Rows = rec.Calls;
            snap.Slots = asm.Context.Accessories.Where(a => !a.IsEmpty).Select(a => $"{a.Slot} type {a.Type} row {RowText(a.Entry)} parent {a.ParentKey} object {(a.Object ? PathOf(a.Object.transform, root.transform) : "-")} show {a.Show}").ToList();
            var st = asm.Context.Stats;
            snap.ExtSummary = st.Summary();
            snap.MeItems = st.MeItems; snap.MeApplied = st.MeApplied;
            foreach (var kv in st.MeSkipped) snap.MeSkipped[kv.Key] = kv.Value;
            snap.Transforms = Transforms(root.transform);
            snap.Renderers = new List<string>(); snap.Materials = new List<string>();
            // First the material snapshot: reading a shared material's properties (below, and in MaterialsOf after its
            // keywords) makes the Unity Editor switch its toggle keywords, which must not happen before they are recorded.
            MaterialsOf(root, snap.Renderers, snap.Materials);
            var own = asm.Context.OwnedMaterials;
            var rendererMats = root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).Distinct().ToList();
            snap.Shared = rendererMats.Where(m => !own.Contains(m)).Select(m => m.name).ToList();
            foreach (var m in rendererMats)
                if (DrawerMismatches(m, out int v) > 0) { snap.DrawerMaterials++; if (v > 0) snap.DrawerValid++; }
            asm.Context.Motion?.Step(0f);
            snap.TransformsAfterStep = Transforms(root.transform);
        }
        catch (Exception e) { snap.Error = e.GetType().Name + ": " + e.Message; }
        finally
        {
            input.Parts = inner;
            Cleanup(before);
        }
        return snap;
    }

    static string FirstDiff(List<string> a, List<string> b)
    {
        if (a == null || b == null) return a == b ? null : "missing";
        int n = Math.Min(a.Count, b.Count);
        for (int i = 0; i < n; i++) if (a[i] != b[i]) return $"[{i}] {Tokens(a[i], b[i])} ({Count(a, b)} differ)";
        return a.Count == b.Count ? null : $"count {a.Count} vs {b.Count}";
    }

    /// <summary>The first space-separated token (the path) and the tokens that differ, card vs documents.</summary>
    static string Tokens(string a, string b)
    {
        var ta = a.Split(' '); var tb = b.Split(' ');
        if (ta.Length != tb.Length) return Short(a) + "  <>  " + Short(b);
        var d = new List<string>();
        for (int i = 1; i < ta.Length && d.Count < 6; i++) if (ta[i] != tb[i]) d.Add(ta[i] + " <> " + tb[i]);
        return Short(ta[0]) + ": " + string.Join("; ", d);
    }

    static int Count(List<string> a, List<string> b)
    {
        int n = Math.Abs(a.Count - b.Count);
        for (int i = 0; i < Math.Min(a.Count, b.Count); i++) if (a[i] != b[i]) n++;
        return n;
    }

    static string Short(string s) => s.Length > 300 ? s.Substring(0, 300) + "…" : s;

    static List<string> Differences(Snapshot a, Snapshot b)
    {
        var l = new List<string>();
        if (a.Error != null || b.Error != null) { l.Add("build error: " + a.Error + " | " + b.Error); return l; }
        void D(string what, string d) { if (d != null) l.Add(what + ": " + d); }
        D("rows", FirstDiff(a.Rows, b.Rows));
        D("accessory slots", FirstDiff(a.Slots, b.Slots));
        if (a.ExtSummary != b.ExtSummary) l.Add("ExtStats: " + a.ExtSummary + " <> " + b.ExtSummary);
        D("transforms after build", FirstDiff(a.Transforms, b.Transforms));
        D("transforms after Step(0)", FirstDiff(a.TransformsAfterStep, b.TransformsAfterStep));
        D("renderers", FirstDiff(a.Renderers, b.Renderers));
        D("materials", FirstDiff(a.Materials, b.Materials));
        if (a.PartsLoaded != b.PartsLoaded) l.Add($"parts loaded {a.PartsLoaded} vs {b.PartsLoaded}");
        return l;
    }

    // fullCount: the first fullCount cards get the full P2 (cold and warm card builds, timing); the rest (-all) get the lean
    // P2: card, documents and documents without hs2.kkex, a second card build only when the first differs from the documents,
    // no timing, bundles released every 10 cards. The full P2 over every card took more than three hours (six builds and a
    // cold bundle load per card).
    static void P2(ContentCatalog gd, ICharaImporter importer, List<string> cards, Dictionary<string, P1Card> p1, int fullCount = int.MaxValue)
    {
        Section("P2", "document builds (" + cards.Count + " cards" + (fullCount < cards.Count ? $", the first {fullCount} in full, the rest lean" : "") + ")");
        var docMs = new List<double>();
        int equal = 0, failed = 0, kkexFail = 0;
        int builds = 0, sharedBuilds = 0, drawerMats = 0, drawerValid = 0;
        var sharedExamples = new List<string>();
        var drawerCards = new List<string>();
        int drawerValidCards = 0;
        int index = -1;
        var swAll = Stopwatch.StartNew();
        foreach (var file in cards)
        {
            index++;
            if (index > 0 && index % 25 == 0) Debug.Log($"CharaDocBuildTest P2: {index}/{cards.Count} cards, {swAll.Elapsed.TotalSeconds:F0} s");
            bool lean = index >= fullCount;
            string name = Path.GetFileName(file);
            byte[] bytes = File.ReadAllBytes(file);
            if (Hs2Importer.Detect(bytes) != Hs2SourceKind.Chara) { Info(name + ": not a character card, skipped"); continue; }
            var rt = DocumentRoundTrip.Run(importer, bytes, file, gd);
            if (!rt.Ok) { Check(name + ": round trip", false, rt.Error); failed++; continue; }
            // Build order: the documents cold (bundles load), the documents without hs2.kkex, the documents again warm; in full
            // mode one more warm build for the timing (minimum of two warm builds).
            var sDoc = Build(gd, rt.Input);
            var rt3 = DocumentRoundTrip.Run(importer, bytes, file, gd, DocumentRoundTrip.StripKkex);
            var sStrip = rt3.Ok ? Build(gd, rt3.Input) : new Snapshot { Error = rt3.Error };
            var rt2 = DocumentRoundTrip.Run(importer, bytes, file, gd);
            var sDoc2 = rt2.Ok ? Build(gd, rt2.Input) : new Snapshot { Error = rt2.Error };
            double tDoc = sDoc2.Ms;
            if (!lean)
            {
                var rt4 = DocumentRoundTrip.Run(importer, bytes, file, gd);
                tDoc = Math.Min(sDoc2.Ms, rt4.Ok ? Build(gd, rt4.Input).Ms : double.MaxValue);
                docMs.Add(tDoc);
            }
            // A document build that differs between its first and a later run is state left in shared objects (the keyword
            // leak through bundle materials, fixed in 2.3 by Runtime/Rendering/MaterialCopies.cs).
            var repro = Differences(sDoc, sDoc2);
            var kk = Differences(sDoc, sStrip);
            foreach (var sn in new[] { sDoc, sStrip, sDoc2 })
            {
                if (sn.Error != null) continue;
                builds++;
                if (sn.Shared.Count > 0) { sharedBuilds++; if (sharedExamples.Count < 5) sharedExamples.Add(name + ": " + string.Join(", ", sn.Shared.Take(4))); }
            }
            if (sDoc.DrawerMaterials > 0)
            {
                drawerMats += sDoc.DrawerMaterials; drawerValid += sDoc.DrawerValid;
                // cards with a switch that changes the variant first (the leak regression takes the first six)
                if (sDoc.DrawerValid > 0) drawerCards.Insert(drawerValidCards++, file); else drawerCards.Add(file);
            }
            string where = $"{name}: documents {sDoc.Ms:F0} ms cold / {tDoc:F0} ms warm, adapter {rt.AdapterMs:F1} ms";
            if (sDoc.Error != null || sDoc2.Error != null) { failed++; Check(name + ": document builds", false, sDoc.Error ?? sDoc2.Error); }
            else if (repro.Count == 0) { equal++; Info(where + ": reproducible"); }
            else { failed++; Check(name + ": first document build = later document build", false, string.Join(" | ", repro)); }
            if (kk.Count > 0) { kkexFail++; Check(name + ": build with the hs2.kkex stores emptied = document build", false, string.Join(" | ", kk)); }
            if (!lean || index % 10 == 9 || index == cards.Count - 1)
            {
                gd.UnloadAll();
                EditorUtility.UnloadUnusedAssetsImmediate(true);
                GC.Collect();
            }
        }
        Check("document builds reproducible (the first and a later build of the same card equal)", failed == 0,
              $"{equal} equal, {failed} failing of {cards.Count}");
        Check("hs2.kkex is never applied (emptied stores build identically)", kkexFail == 0, kkexFail + " differ");
        Check("no renderer keeps a material the build did not make (shared bundle materials)", sharedBuilds == 0,
              $"{sharedBuilds} of {builds} builds{(sharedExamples.Count > 0 ? ": " + string.Join(" | ", sharedExamples) : "")}");
        Info($"materials whose keywords disagree with their toggle drawers (kept as loaded; the Unity Editor's drawers would switch them): {drawerMats} on {drawerCards.Count} cards, {drawerValid} with a keyword the shader declares (on {drawerValidCards} cards)");
        LeakCheck(gd, importer, cards, drawerCards);
        Info($"warm document build median {Median(docMs):F0} ms over {docMs.Count} cards (the card build it was compared with is retired)");
        Info($"P2 wall time {swAll.Elapsed.TotalSeconds:F0} s for {cards.Count} cards");
    }

    // ================================================================ P2 state-leak regression

    static Snapshot BuildDocumentFile(ContentCatalog gd, ICharaImporter importer, string file)
    {
        var rt = DocumentRoundTrip.Run(importer, File.ReadAllBytes(file), file, gd);
        return rt.Ok ? Build(gd, rt.Input) : new Snapshot { Error = "round trip: " + rt.Error };
    }

    // Characters built again after other characters' builds and after every loaded material's properties were read, with
    // the bundles kept loaded, must build as before (shared bundle materials; the Editor runs its property
    // drawers on a material when its properties are first read, Runtime/Rendering/MaterialCopies.cs).
    static void LeakCheck(ContentCatalog gd, ICharaImporter importer, List<string> cards, List<string> drawerCards)
    {
        var tt = TestTargets.Load();
        var targets = tt.VisualCards.Where(File.Exists).Select(Path.GetFullPath).ToList();
        var pool = cards.Select(Path.GetFullPath).Where(f => !targets.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();
        if (pool.Count == 0)   // -targets: other cards from the card folders (in the order of Run's list)
            foreach (var rel in new[] { tt.cards?.female, tt.cards?.male })
            {
                if (string.IsNullOrEmpty(rel) || !Directory.Exists(TestTargets.Abs(rel))) continue;
                string d = TestTargets.Abs(rel);
                pool.AddRange(Directory.GetFiles(d, "*.png", SearchOption.AllDirectories).OrderBy(f => Rel(d, f), StringComparer.Ordinal)
                    .Select(Path.GetFullPath).Where(f => !targets.Contains(f, StringComparer.OrdinalIgnoreCase)));
            }
        var others = new List<string>();
        void Add(string f)
        {
            f = Path.GetFullPath(f);
            if (others.Count >= 12 || targets.Contains(f, StringComparer.OrdinalIgnoreCase) || others.Contains(f, StringComparer.OrdinalIgnoreCase)) return;
            if (Hs2Importer.Detect(File.ReadAllBytes(f)) == Hs2SourceKind.Chara) others.Add(f);
        }
        if (Arg("-leakcards") is string lf && File.Exists(lf))
            foreach (var l in File.ReadAllLines(lf, Encoding.UTF8).Select(x => x.Trim().TrimStart('\uFEFF')))
                if (l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal) && File.Exists(l)) Add(l);
        foreach (var f in drawerCards.Take(6)) Add(f);
        int step = Math.Max(1, pool.Count / 12);
        for (int i = 0; i < pool.Count && others.Count < 12; i += step) Add(pool[i]);
        var again = targets.Concat(others).ToList();

        gd.UnloadAll();
        EditorUtility.UnloadUnusedAssetsImmediate(true);
        GC.Collect();
        // Unit: a copy of a loaded bundle material keeps the loaded keywords after its original's properties were read.
        var probe = gd.LoadAsset<Material>("chara/hair_shader_mat.unity3d", "hair_dithering");
        if (probe)
        {
            string Kw(Material m) => string.Join(",", m.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal));
            string k0 = Kw(probe);
            probe.HasProperty("_MainTex");
            string k1 = Kw(probe);
            var copy = HS2Next.MaterialCopies.Copy(probe);
            string kc = Kw(copy);
            HS2Next.MaterialCopies.SetShader(copy, copy.shader);
            string ks = Kw(copy);
            Object.DestroyImmediate(copy);
            Check("a copy of a loaded bundle material keeps the loaded keywords (also after a shader assignment)", kc == k0 && ks == k0,
                  $"loaded [{k0}], original after a property read [{k1}]{(k1 != k0 ? " (switched by the Editor's property drawers)" : "")}, copy [{kc}], after SetShader [{ks}]");
        }
        else Info("leak check: hair_dithering not loaded; unit check skipped");

        var first = new Dictionary<string, Snapshot>(StringComparer.OrdinalIgnoreCase);
        int builds = 0;
        foreach (var f in again) { first[f] = BuildDocumentFile(gd, importer, f); builds++; }
        foreach (var f in others)
        {
            var rt = DocumentRoundTrip.Run(importer, File.ReadAllBytes(f), f, gd);
            if (rt.Ok) { Build(gd, rt.Input); builds++; }
        }
        // The read runs the Editor's property drawers on every loaded material (a player runs none). Bundle materials the
        // catalog recorded at their load keep what the drawers switched: their copies must still carry the loaded keywords
        // (Runtime/Rendering/MaterialCopies.cs). A material the catalog did not record (the engine's built-in Default-Material
        // that an empty renderer slot is copied from, materials made at run time) is copied with the keywords it has, as in a
        // player, where nothing switches them: its keywords are put back after the read (reading keywords runs no drawer).
        int read = 0, switchedRecorded = 0;
        var restored = new List<string>();
        foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
        {
            if (!m) continue;
            var kw = m.shaderKeywords;
            m.HasProperty("_MainTex");
            read++;
            var now = m.shaderKeywords;
            if (now.Length == kw.Length && !now.Except(kw, StringComparer.Ordinal).Any()) continue;
            if (HS2Next.MaterialCopies.IsRecorded(m)) { switchedRecorded++; continue; }
            m.shaderKeywords = kw;
            restored.Add($"{m.name} [{string.Join(",", kw)}] -> [{string.Join(",", now)}]");
        }
        Info($"leak check: the property read switched keywords on {switchedRecorded} recorded bundle materials (kept) and on {restored.Count} materials the catalog did not record (put back{(restored.Count > 0 ? ": " + string.Join("; ", restored.Take(5)) : "")})");
        int same = 0;
        var bad = new List<string>();
        foreach (var f in again)
        {
            var d = Differences(first[f], BuildDocumentFile(gd, importer, f));
            if (d.Count == 0) same++;
            else bad.Add(Path.GetFileName(f) + ": " + string.Join(" | ", d.Take(2)));
        }
        foreach (var b in bad) Info("leak check: " + b);
        Check("state-leak regression: characters built again after other builds and after reading every loaded material build identically (bundles kept loaded)",
              bad.Count == 0, $"{same} of {again.Count} identical ({targets.Count} visual targets, {others.Count} other cards; {builds} builds and {read} materials read in between)");
        gd.UnloadAll();
        EditorUtility.UnloadUnusedAssetsImmediate(true);
        GC.Collect();
    }

    // ================================================================ P3 renders (graphics)

    public static void RunRender()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        ShaderUtil.allowAsyncCompilation = false;
        Section("P3", "renders of the visual targets");
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Check("graphics device present (run without -nographics)", false); Finish("docbuild_render_report.txt", "build parity renders", sw); return; }
        var head = new StringBuilder();
        var gd = CardResolveTest.BuildDb(head);
        rep.Append(head);
        var importer = NewImporter(gd);
        var targets = TestTargets.Load();
        string dir = Path.Combine(OutDir, "render");
        string thumbs = Path.Combine(OutDir, "thumbs");
        Directory.CreateDirectory(dir); Directory.CreateDirectory(thumbs);
        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HS2Next.App.AppPipeline.MeasureAssetPath);
        try
        {
            foreach (var file in targets.VisualCards.Where(File.Exists))
            {
                string name = Safe(Path.GetFileNameWithoutExtension(file));
                byte[] bytes = File.ReadAllBytes(file);
                var rt = DocumentRoundTrip.Run(importer, bytes, file, gd);
                if (!rt.Ok) { Check(name + ": round trip", false, rt.Error); continue; }
                var before = new HashSet<GameObject>(SceneManager.GetActiveScene().GetRootGameObjects());
                GameObject a = null, b = null;
                try
                {
                    // Built-in: two document builds (two round trips), each alone in the scene.
                    var rtA = DocumentRoundTrip.Run(importer, bytes, file, gd);
                    if (!rtA.Ok) { Check(name + ": round trip (first build)", false, rtA.Error); continue; }
                    a = new CharaAssembler(gd).Build(rtA.Input);
                    CharaRenderTest.RenderViews(a, Path.Combine(dir, name + "_first_builtin"), null);
                    a.SetActive(false);
                    b = new CharaAssembler(gd).Build(rt.Input);
                    CharaRenderTest.RenderViews(b, Path.Combine(dir, name + "_doc_builtin"), null);
                    foreach (var v in new[] { "full", "full34", "face" })
                    {
                        var pa = File.ReadAllBytes(Path.Combine(dir, $"{name}_first_builtin_{v}.png"));
                        var pb = File.ReadAllBytes(Path.Combine(dir, $"{name}_doc_builtin_{v}.png"));
                        Check($"{name} {v}: Built-in render byte-identical", pa.SequenceEqual(pb), $"{pa.Length} / {pb.Length} bytes");
                    }
                    // URP: both mapped, rendered one at a time.
                    HS2Next.ShaderMap.ApplyUrp(b);
                    CharaRenderTest.RenderViews(b, Path.Combine(dir, name + "_doc_urp"), asset);
                    b.SetActive(false);
                    a.SetActive(true);
                    HS2Next.ShaderMap.ApplyUrp(a);
                    CharaRenderTest.RenderViews(a, Path.Combine(dir, name + "_first_urp"), asset);
                    foreach (var v in new[] { "full", "full34", "face" })
                    {
                        int max = MaxChannelDiff(Path.Combine(dir, $"{name}_first_urp_{v}.png"), Path.Combine(dir, $"{name}_doc_urp_{v}.png"));
                        Check($"{name} {v}: URP render max channel difference ≤ 8/255", max <= 8, max + "/255");
                    }
                }
                finally
                {
                    GraphicsSettings.defaultRenderPipeline = null; QualitySettings.renderPipeline = null;
                    Cleanup(before);
                }
                // The import thumbnail of the target (HS2's coordinate shot of the document build at the origin).
                string outFile = Path.Combine(thumbs, name + ".hs2nchara");
                var r = CharaFileImport.ImportToFile(importer, file, outFile, gd, new ThumbnailSettings { Pipeline = asset });
                Check(name + ": import thumbnail written", r.Ok && r.Thumbnail != null, r.ToString());
                if (r.Thumbnail != null) File.WriteAllBytes(Path.Combine(thumbs, name + ".png"), r.Thumbnail);
                gd.UnloadAll();
            }
        }
        catch (Exception e) { Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        finally { GraphicsSettings.defaultRenderPipeline = null; QualitySettings.renderPipeline = null; }
        Info("renders: Spike/out/import/render/, thumbnails: Spike/out/import/thumbs/");
        gd.UnloadAll();
        Finish("docbuild_render_report.txt", "build parity renders", sw);
    }

    static int MaxChannelDiff(string fa, string fb)
    {
        var a = new Texture2D(2, 2); var b = new Texture2D(2, 2);
        try
        {
            a.LoadImage(File.ReadAllBytes(fa)); b.LoadImage(File.ReadAllBytes(fb));
            if (a.width != b.width || a.height != b.height) return 255;
            var pa = a.GetPixels32(); var pb = b.GetPixels32();
            int max = 0;
            for (int i = 0; i < pa.Length; i++)
            {
                max = Math.Max(max, Math.Abs(pa[i].r - pb[i].r));
                max = Math.Max(max, Math.Abs(pa[i].g - pb[i].g));
                max = Math.Max(max, Math.Abs(pa[i].b - pb[i].b));
            }
            return max;
        }
        finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); }
    }

    static string Safe(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
}
