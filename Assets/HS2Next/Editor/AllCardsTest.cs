// AllCards: one headless row that walks every file of the card folders once and then the coordinates, with one shared
// build per card (AX1–AX10; package Q). The rows it replaces keep their own entries for
// targeted runs; each step here calls the same per-file code they run:
//   import   ImportTest's per-file step (import, save, reload byte-equal, A2 / A3 / A6); after the cards the files of
//            UserData/coordinate go through it too (ImportTest.full).
//   P1       CharaDocBuildTest's P1 of the card: document trees, plugin data and part-key rows against the S1 baseline
//            (CharaDocBuildTest.all's P1; its P2 / P3 stay in the CharaDocBuildTest rows).
//   S1       the scene build of the card (MakerCoreTest.Harness: import → stored documents → SubtreeCharaBuilder), its snapshot
//            against the stored baseline of the pinned corpus, the G1 counts (MakerCoreTest.all).
//   stats    CardImportTest's build of the card (the assembler's document build; the scene build's URP mapping hides the
//            unfitted shaders): failures by class, unfitted shaders, missing parts, the texture guard, mip assertions and
//            other logged errors, the motion stages, 30 motion steps of 1/60 s with stage exceptions and non-finite values
//            (CardImport.female / CardImport.male: one import-report file per card folder, same format).
//   S2       MakerEditTest's pair step: the node of the previous card of the same sex is morphed into this card and compared
//            with a fresh node of it; each sex's cyclic last pair runs after its last card (MakerEditTest.all). The node of the
//            previous card is disposed before this card's builds and rebuilt for the pair (a live character keeps the
//            decoded MaterialEditor textures it shares with the next card under its own names).
//   loose    the references of the card's documents to rows with a loose-PNG main texture (LooseTextureTest.cards).
//   coords   every coordinate of UserData/coordinate/<sex> worn in mode All on the visual target of that sex, = a fresh build,
//            saved and reloaded (CoordApply.all); -coordEvery k takes every k-th file (impact runs: 3).
// The walk: the female card folder, then the male one (Tools/targets.json `cards`), each ordinal by relative path. Bundles
// are released every 10 files; the live S2 nodes are rebuilt from their card after a release (as MakerEditTest does).
// Resuming (AX6): Spike/out/allcards/allcards_current.txt names the file in progress (removed at the end). -from N starts
// at the N-th file (0-based) of the walk, -limit N stops after N files, -only <relative path> runs one file; a partial walk
// runs the S2 pairs of its cards (a chain starts from the previous card of the sex; a sex's cyclic pair runs after its
// last card) and no coordinates unless -coords is given; -coordsOnly runs only the coordinates, -coordPart import | female
// | male one part of them. -out <dir> writes the reports there (the parts of a chunked run; Tools-side merge).
// Run: -executeMethod AllCardsTest.Run [-from N] [-limit N] [-only <rel>] [-coordEvery k] [-noCoords] [-coords]
//      [-coordsOnly] [-coordPart all|import|female|male] [-out <dir>]
// Reports (Spike/out/allcards/): allcards_report.txt (first line "SUMMARY: N PASS, M FAIL"; the numbers of every replaced
// row and the minutes per step), allcards_import.txt, allcards_p1.txt, allcards_s2.txt, allcards_cardimport_female.txt /
// _male.txt (CardImportTest's report format), allcards_loose.txt, allcards_coords.txt. TestExit.Exit(1) on any FAIL.
// Numbers and ids only: nothing is rendered.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Chara;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.Import;
using HS2Next.IO;
using HS2Next.Motion.Face;
using HS2Next.Save;
using HS2Next.Tests.MakerCore;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class AllCardsTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "allcards");
    /// <summary>This run's report folder (-out, default <see cref="OutDir"/>).</summary>
    static string Out;

    static StringBuilder rep;
    static int passes, fails;

    static void Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
    }

    static void Info(string s) => rep.AppendLine("   " + s);

    static string Arg(string name, string def = null)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static bool Flag(string name) => TestArgs.Get().Contains(name);

    static string RelOf(string file)
    {
        string full = Path.GetFullPath(file), ud = Path.GetFullPath(TestTargets.UserData).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return (full.StartsWith(ud, StringComparison.OrdinalIgnoreCase) ? full.Substring(ud.Length) : Path.GetFileName(full)).Replace('\\', '/');
    }

    static double Median(List<double> l)
    {
        if (l.Count == 0) return double.NaN;
        var s = l.OrderBy(x => x).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }

    // ================================================================ the import statistics of one card folder

    /// <summary>CardImportTest's summary numbers over one card folder, written in its report format.</summary>
    sealed class FolderStats
    {
        public string Name;
        public int Files, Ok, Males, WithMissing, MissingRows, MissingMods, WithTexAssert, WithGuard, WithLogErrors, WithEditMat;
        public int WithMotionFail, WithStepErrors, WithNaN, WithNaNValues, StagesTotal;
        public long StepsRun, StageExceptions, NonFiniteBones, NonFiniteOther, NonFiniteWeights;
        public double StepMsTotal, BuildMs;
        public readonly SortedDictionary<string, int> Failures = new SortedDictionary<string, int>();
        public readonly SortedDictionary<string, int> ShaderMats = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, int> ShaderCards = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> StageCensus = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly StringBuilder Body = new StringBuilder();

        public void Fail(string cls) => Failures[cls] = Failures.TryGetValue(cls, out var n) ? n + 1 : 1;

        public string Text(int steps)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"card import statistics (AllCardsTest, the assembler's document build): {Name} ({Files} cards, motion steps {steps}, graphics {SystemInfo.graphicsDeviceType})");
            sb.AppendLine();
            sb.AppendLine("summary");
            sb.AppendLine($"  {"cards",-34} {Files,8}");
            sb.AppendLine($"  {"ok",-34} {Ok,8}");
            sb.AppendLine($"  {"male cards (sex 0, male path)",-34} {Males,8}");
            sb.AppendLine($"  {"failed",-34} {Files - Ok,8}");
            foreach (var kv in Failures.OrderByDescending(kv => kv.Value)) sb.AppendLine($"    {kv.Key,-60} {kv.Value,6}");
            sb.AppendLine($"  {"cards with missing rows/mods",-34} {WithMissing,8}");
            sb.AppendLine($"  {"cards with texture mip assertions",-34} {WithTexAssert,8}");
            sb.AppendLine($"  {"cards with the texture guard engaged",-34} {WithGuard,8}");
            sb.AppendLine($"  {"cards with other logged errors",-34} {WithLogErrors,8}");
            sb.AppendLine($"  {"cards with edit-mode material notes",-34} {WithEditMat,8}");
            sb.AppendLine($"  {"motion stages attached (total)",-34} {StagesTotal,8}");
            sb.AppendLine($"  {"motion stages by name (cards)",-34} {(StageCensus.Count == 0 ? "-" : string.Join(", ", StageCensus.Select(kv => $"{kv.Key} {kv.Value}")))}");
            sb.AppendLine($"  {"cards with motion setup failures",-34} {WithMotionFail,8}");
            sb.AppendLine($"  {"motion steps per card",-34} {steps,8}  (1/60 s each)");
            sb.AppendLine($"  {"motion steps run (total)",-34} {StepsRun,8}  ({(StepsRun > 0 ? StepMsTotal / StepsRun : 0):F3} ms per step, all stages)");
            sb.AppendLine($"  {"motion stage exceptions (total)",-34} {StageExceptions,8}");
            sb.AppendLine($"  {"cards with motion step errors",-34} {WithStepErrors,8}");
            sb.AppendLine($"  {"cards with non-finite bones",-34} {WithNaN,8}");
            sb.AppendLine($"  {"cards with other non-finite values",-34} {WithNaNValues,8}");
            sb.AppendLine($"  {"non-finite values (total)",-34} {NonFiniteBones + NonFiniteOther + NonFiniteWeights,8}  (body bones {NonFiniteBones}, other transforms {NonFiniteOther}, blend-shape weights {NonFiniteWeights})");
            sb.AppendLine($"  {"MISSING rows / missing mods",-34} {MissingRows,8} / {MissingMods}");
            sb.AppendLine($"  {"build time total (s)",-34} {BuildMs / 1000.0,8:F1}  (the assembler's document build, as CardImportTest)");
            sb.AppendLine();
            sb.AppendLine("shaders without a fitted URP replacement (materials over all cards / cards)");
            if (ShaderMats.Count == 0) sb.AppendLine("  none");
            foreach (var kv in ShaderMats.OrderByDescending(kv => kv.Value)) sb.AppendLine($"  {kv.Key,-50} {kv.Value,7} {ShaderCards[kv.Key],6}");
            sb.AppendLine();
            sb.Append(Body);
            return sb.ToString();
        }

        /// <summary>The run_tests summary line of a CardImport row from these numbers.</summary>
        public string Line(int nonCards) =>
            $"cards {Files}, ok {Ok}, failed {Files - Ok} (non-character files {nonCards}), guard engaged {WithGuard}, mip asserts {WithTexAssert}, " +
            $"step errors {WithStepErrors}, non-finite {WithNaN}, motion: stages {StagesTotal}, steps {StepsRun}, stage exceptions {StageExceptions}, " +
            $"non-finite values {NonFiniteBones + NonFiniteOther + NonFiniteWeights}, unfitted shaders {ShaderMats.Count}, missing rows / mods {MissingRows} / {MissingMods}";
    }

    // ================================================================ entry

    const int Steps = 30;
    const string EditModeMaterial = "Instantiating material due to calling renderer.material during edit mode";

    public static void Run()
    {
        rep = new StringBuilder(); passes = fails = 0;
        var total = Stopwatch.StartNew();
        Out = Arg("-out") is string od ? Path.GetFullPath(od) : OutDir;
        Directory.CreateDirectory(OutDir);
        Directory.CreateDirectory(Out);
        string current = Path.Combine(OutDir, "allcards_current.txt");
        var t = TestTargets.Load();
        var folders = new List<(string name, string dir)>();
        foreach (var (name, rel) in new[] { ("female", t.cards?.female), ("male", t.cards?.male) })
            if (!string.IsNullOrEmpty(rel) && Directory.Exists(TestTargets.Abs(rel))) folders.Add((name, Path.GetFullPath(TestTargets.Abs(rel))));
        var walk = new List<(string folder, string root, string file)>();
        foreach (var (name, dir) in folders)
            walk.AddRange(Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories)
                .OrderBy(f => f.Substring(dir.Length).TrimStart('\\', '/').Replace('\\', '/'), StringComparer.Ordinal)
                .Select(f => (name, dir, f)));
        int all = walk.Count;
        var fullWalk = walk.Select(w => w.file).ToList();
        int from = int.Parse(Arg("-from", "0"), CultureInfo.InvariantCulture);
        int limit = int.Parse(Arg("-limit", int.MaxValue.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        string only = Arg("-only");
        bool coordsOnly = Flag("-coordsOnly");
        if (coordsOnly) walk = new List<(string folder, string root, string file)>();
        else if (only != null) walk = walk.Where(w => RelOf(w.file) == only.Replace('\\', '/')).ToList();
        else walk = walk.Skip(from).Take(limit).ToList();
        bool whole = only == null && !coordsOnly && from == 0 && walk.Count == all;
        bool coords = (whole || coordsOnly || Flag("-coords")) && !Flag("-noCoords");
        string coordPart = Arg("-coordPart", "all");   // all | import | female | male
        int coordEvery = Math.Max(1, int.Parse(Arg("-coordEvery", "1"), CultureInfo.InvariantCulture));
        rep.AppendLine($"all cards (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}): {walk.Count} of {all} files of the card folders" +
                       (whole ? "" : coordsOnly ? " (-coordsOnly)" : only != null ? $" (-only {only})" : $" (from {from}, limit {limit})") +
                       (coords ? $"; coordinates {(coordEvery > 1 ? "every " + coordEvery + "th" : "all")}, part {coordPart}" : "; no coordinates"));

        var time = new SortedDictionary<string, double>(StringComparer.Ordinal);
        void Time(string step, Stopwatch w) { time[step] = (time.TryGetValue(step, out var v) ? v : 0) + w.Elapsed.TotalMinutes; }

        // per-card Unity log lines (mip assertions, errors) of the builds
        var cardLog = new List<(LogType type, string msg)>();
        Application.LogCallback onLog = (msg, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception) cardLog.Add((type, msg.Split('\n')[0]));
        };
        Application.logMessageReceived += onLog;
        int? faceSeed = FaceSetup.Seed;
        var stats = folders.ToDictionary(f => f.name, f => new FolderStats { Name = f.name });
        var s2 = new MakerEditTest.S2Run();
        int s1Equal = 0, s1Differ = 0, s1Missing = 0, s1Failed = 0, s1Chara = 0, s1New = 0, s1Changed = 0, g1Fail = 0;
        long g1Slots = 0, g1Unowned = 0, g1Dead = 0, g1Bundle = 0, g1Owned = 0, g1Drawn = 0;
        int bodyDead = 0, faceDead = 0;
        var s1BuildMs = new List<double>();
        var s1Fails = new List<string>();
        var looseLines = new List<string>();
        var looseBySlot = new Dictionary<string, int>(StringComparer.Ordinal);
        int looseChars = 0, looseWithLoose = 0, looseSlots = 0, looseFailed = 0;
        int nonCharacter = 0;
        var head = new StringBuilder();
        var p1 = new CharaDocBuildTest.P1Run();
        try
        {
            using (var h = MakerCoreTest.Harness.Open(head))
            {
                rep.Append(head);
                var gd = h.Catalog;
                var ip = ImportTest.Begin(whole && coords && coordPart == "all", int.MaxValue, _ => gd);
                CharaDocBuildTest.ReportBegin();
                p1 = CharaDocBuildTest.P1Begin(walk.Count);
                MakerEditTest.ReportBegin();
                string baseDir = MakerCoreTest.BaselineDir;
                var corpus = MakerCoreTest.CorpusIndex(baseDir, out string corpusCommit);
                Info(corpus == null ? "S1 corpus: no index.tsv (every card compared)" : $"S1 corpus: the baselines' index ({corpus.Count} cards, commit {corpusCommit ?? "?"}); new or changed cards are counted apart");
                var importGroup = ip.Groups.ToDictionary(g => g.g.Name, g => g.g);

                // S2: the pair lists by the sex in the card, the position of each card in its list
                var sw = Stopwatch.StartNew();
                FaceSetup.Seed = 12345;
                // over the whole walk, so that a part of it (-from / -limit / -only) runs the same pairs as the whole
                var groups = MakerEditTest.S2Groups(h, fullWalk);
                FaceSetup.Seed = faceSeed;
                var pos = new Dictionary<string, (int sex, int index)>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in groups) for (int k = 0; k < kv.Value.Count; k++) pos[kv.Value[k]] = (kv.Key, k);
                var cur = new Dictionary<int, MakerEditTest.Node>();
                var curFrom = new Dictionary<int, (CharaContainer c, string rel)>();
                Time("S2", sw);

                for (int i = 0; i < walk.Count; i++)
                {
                    var (folder, root, file) = walk[i];
                    string rel = RelOf(file);
                    File.WriteAllText(current, (from + i).ToString(CultureInfo.InvariantCulture) + "\t" + rel + "\n");
                    if (i > 0 && i % 25 == 0) Debug.Log($"AllCardsTest: {i}/{walk.Count} files, {total.Elapsed.TotalMinutes:F1} min");
                    var fs = stats[folder];
                    fs.Files++;

                    // import, save, reload, A2 / A3 / A6
                    sw.Restart();
                    ImportTest.One(ip, importGroup[folder], file);
                    Time("import", sw);

                    // P1
                    sw.Restart();
                    CharaDocBuildTest.P1One(gd, h.Importer, p1, root, file);
                    Time("P1", sw);

                    // the card as CardImportTest loads it (a file that is not a character card is a failure by class)
                    Card card = null;
                    try { card = Card.Load(file); }
                    catch (Exception e)
                    {
                        nonCharacter++;
                        string cls = $"card load: {CardImportTest.CardKind(file)} ({e.GetType().Name}: {e.Message.Split('.')[0]})";
                        fs.Fail(cls);
                        fs.Body.AppendLine($"== {rel}  0 ms  parts 0  FAILED {cls}");
                    }
                    if (card == null) { Release(i); continue; }
                    bool male = card.Parameter.Get("sex").Int(1) == 0;
                    if (male) fs.Males++;

                    // the S2 node of the previous card of this sex goes before this card's builds (a live character keeps the
                    // decoded MaterialEditor textures it shares with this card under its own names); it is rebuilt for the pair
                    if (pos.TryGetValue(file, out var ps0) && cur.TryGetValue(ps0.sex, out var held) && held != null) { held.Dispose(); cur[ps0.sex] = null; }

                    // S1: the scene build, its snapshot against the baseline
                    sw.Restart();
                    MakerCoreTest.Built b = null;
                    // the card's Unity log lines from here on (the S1 build loads the card's bundles first: an error logged once per
                    // load, e.g. a missing audio clip, belongs to the card although the statistics build comes second)
                    cardLog.Clear();
                    try
                    {
                        b = h.Build(file);
                        if (b.NotCharacter) { nonCharacter++; fs.Fail("not a character card"); }
                        else if (!b.Ok) { s1Chara++; s1Failed++; s1Fails.Add(rel + ": " + b.Error); fs.Fail("build: " + b.Error); fs.Body.AppendLine($"== {rel}  {b.BuildMs:F0} ms  FAILED build: {b.Error}"); }
                        else
                        {
                            s1Chara++;
                            string state = CorpusState(corpus, rel, b.CardSha);
                            s1BuildMs.Add(b.BuildMs);
                            var snap = b.Snapshot();
                            if (state != "pinned") { if (state == "new") s1New++; else s1Changed++; Info($"S1 corpus: {rel} ({state}), not compared"); }
                            else
                            {
                                g1Slots += snap.G1Slots; g1Unowned += snap.G1UnownedSlots; g1Dead += snap.G1Dead; g1Bundle += snap.G1BundleSlots; g1Owned += snap.G1Owned; g1Drawn += snap.G1Drawn;
                                if (!snap.G1BodyDrawLive) bodyDead++;
                                if (!snap.G1FaceDrawLive) faceDead++;
                                if (!snap.G1Pass) g1Fail++;
                                var basis = MakerCoreTest.LoadBaseline(b.CardSha, baseDir);
                                if (basis == null) { s1Missing++; s1Fails.Add(rel + ": no baseline " + b.CardSha + ".snap"); }
                                else
                                {
                                    var d = CharaSnapshot.Compare(basis, snap, 20);
                                    if (d.Equal) s1Equal++;
                                    else { s1Differ++; s1Fails.Add(rel + ": snapshot = baseline: baseline vs now: " + d); }
                                }
                            }
                            Time("S1", sw);

                            // the import statistics on the build CardImportTest makes: the assembler's document build
                            // (TestCharaBuild; the scene build's URP mapping hides the unfitted shaders)
                            sw.Restart();
                            RawStats(fs, rel, card, gd, cardLog);
                            Time("stats", sw);

                            // loose-PNG references of the documents
                            sw.Restart();
                            if (b.Container != null)
                            {
                                looseChars++;
                                int sex = b.Container.Chara.Profile.Sex;
                                var hits = LooseTextureTest.LooseHits(gd, b.Container, sex, looseBySlot);
                                if (hits.Count > 0)
                                {
                                    looseWithLoose++;
                                    looseSlots += hits.Count;
                                    foreach (var x in hits) looseLines.Add($"{rel}\t{(sex == 0 ? "male" : "female")}\t{x}");
                                }
                            }
                            else looseFailed++;
                            Time("loose", sw);
                        }
                    }
                    catch (Exception e) { s1Failed++; s1Fails.Add(rel + ": " + e.GetType().Name + ": " + e.Message); }
                    finally { b?.Dispose(); }

                    // S2: the pair (previous card of the same sex → this card)
                    sw.Restart();
                    if (pos.TryGetValue(file, out var ps) && groups[ps.sex].Count >= 2)
                    {
                        var g = groups[ps.sex];
                        FaceSetup.Seed = 12345;
                        try
                        {
                            if (ps.index == 0)
                            {
                                var n0 = MakerEditTest.S2Start(h, s2, rel, file, out var c0);
                                if (n0 != null) { cur[ps.sex] = n0; curFrom[ps.sex] = (c0, rel); }
                            }
                            else
                            {
                                // a part of the walk starts the chain from the previous card of the sex: its fresh node is
                                // what the whole walk holds there (the node after a release is rebuilt the same way)
                                if ((!cur.TryGetValue(ps.sex, out var node) || node == null) && curFrom.TryGetValue(ps.sex, out var from0))
                                    node = MakerEditTest.NewNode(h, from0.c, from0.rel);
                                if (node == null)
                                {
                                    string prev = g[ps.index - 1];
                                    node = MakerEditTest.S2Start(h, s2, RelOf(prev), prev, out var cp);
                                    if (node != null) curFrom[ps.sex] = (cp, RelOf(prev));
                                }
                                if (node != null)
                                {
                                    cur[ps.sex] = MakerEditTest.S2Pair(h, s2, node, rel, file, out var cB);
                                    if (cB != null) curFrom[ps.sex] = (cB, rel);
                                }
                            }
                            // the cyclic pair of the sex (its last card -> its first) right after its last card
                            if (ps.index == g.Count - 1 && cur.TryGetValue(ps.sex, out var last) && last != null)
                            {
                                last = MakerEditTest.S2Pair(h, s2, last, RelOf(g[0]), g[0], out _);
                                last.Dispose();
                                cur.Remove(ps.sex);
                                curFrom.Remove(ps.sex);
                            }
                        }
                        finally { FaceSetup.Seed = faceSeed; }
                    }
                    Time("S2", sw);
                    Release(i);
                }

                // the chains of a part of the walk that did not reach the last card of their sex
                foreach (var node in cur.Values) node?.Dispose();
                cur.Clear();
                h.ReleaseBundles();

                // the coordinates: import (A1–A6), then worn on the visual targets
                if (coords)
                {
                    var swc = Stopwatch.StartNew();
                    var cg = ip.Groups.First(g => g.g.Name == "coordinate");
                    if (coordPart == "all" || coordPart == "import")
                        foreach (var f in ImportTest.FilesOf(cg.dir)) ImportTest.One(ip, cg.g, f);
                    Time("import (coordinates)", swc);
                    swc.Restart();
                    MakerTestKit.Reset();
                    FaceSetup.Seed = 12345;
                    try
                    {
                        foreach (var (sex, cardFile, label) in new[] { ("female", MakerTestKit.FemaleCard, "BR-Chan"), ("male", MakerTestKit.MaleCard, "HERO107") })
                            if (coordPart == "all" || coordPart == sex)
                                MakerTestKit.Guard("CoordApply " + sex, () => MakerFileTest.CoordApplySex(h, sex, cardFile, label, 0, null, coordEvery));
                    }
                    finally { FaceSetup.Seed = faceSeed; }
                    Time("coordinates", swc);
                    File.WriteAllText(Path.Combine(Out, "allcards_coords.txt"), $"SUMMARY: {MakerTestKit.Passes} PASS, {MakerTestKit.Fails} FAIL\n" + MakerTestKit.Rep.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
                }

                // the step reports and verdicts
                ImportTest.End(ip, false, Path.Combine(Out, "allcards_import.txt"));
                CharaDocBuildTest.P1End(p1);
                MakerEditTest.S2Verdicts(s2);
                File.WriteAllText(Path.Combine(Out, "allcards_p1.txt"), CharaDocBuildTest.ReportText, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(Out, "allcards_s2.txt"), MakerEditTest.ReportText, new UTF8Encoding(false));

                void Release(int i)
                {
                    if ((i + 1) % 10 != 0) return;
                    // AssetBundle.Unload(true) destroys the assets a live character uses: the S2 nodes are rebuilt after it
                    foreach (var k in cur.Keys.ToList()) { cur[k]?.Dispose(); cur[k] = null; }
                    h.ReleaseBundles();
                    FaceSetup.Seed = 12345;
                    try { foreach (var k in curFrom.Keys.ToList()) if (cur.ContainsKey(k)) cur[k] = MakerEditTest.NewNode(h, curFrom[k].c, curFrom[k].rel); }
                    finally { FaceSetup.Seed = faceSeed; }
                }
            }
        }
        catch (Exception e) { Check("run (exception)", false, e.GetType().Name + ": " + e.Message + " " + e.StackTrace); }
        finally
        {
            Application.logMessageReceived -= onLog;
            FaceSetup.Seed = faceSeed;
        }

        // ---------------------------------------------------------------- summary
        foreach (var fs in stats.Values)
            File.WriteAllText(Path.Combine(Out, "allcards_cardimport_" + fs.Name + ".txt"), fs.Text(Steps), new UTF8Encoding(false));
        var lh = $"# cards whose documents reference rows with a Sideloader loose-PNG main texture (AllCardsTest)\n# {looseWithLoose} of {looseChars} character cards, {looseSlots} slots; by slot: " +
                 string.Join(", ", looseBySlot.OrderBy(k => k.Key).Select(k => k.Key + " " + k.Value)) + "\n# card (relative to UserData)\tsex\tslot (document.member)\treference\tcategory\tloose texture pairs\n";
        File.WriteAllText(Path.Combine(Out, "allcards_loose.txt"), lh + string.Join("\n", looseLines) + "\n", new UTF8Encoding(false));

        var (ipPass, ipFail) = ImportTest.Counts;
        var (p1Pass, p1Fail) = CharaDocBuildTest.ReportCounts;
        var (s2Pass, s2Fail) = MakerEditTest.ReportCounts;
        rep.AppendLine();
        rep.AppendLine("== import (ImportTest.full): allcards_import.txt");
        Check("import: every file imported, saved, reloaded byte-equal, A2 / A3 / A6 (ImportTest's checks)", ipFail == 0, $"{ipPass} passed, {ipFail} failed");
        rep.AppendLine("== P1 (CharaDocBuildTest.all P1): allcards_p1.txt");
        Check("P1: document trees, plugin data and part-key rows vs the S1 baseline (CharaDocBuildTest's checks)", p1Fail == 0, $"{p1Pass} passed, {p1Fail} failed; cards {p1.Files}, compared {p1.Results?.Count ?? 0}, not character {p1.NotChara}, import failed {p1.ImportFailed}, unclassified cards {p1.UnclassifiedCards}, rows {p1.Rows}, rows that differ {p1.RowDiffs}");
        rep.AppendLine("== S1 (MakerCoreTest.all)");
        foreach (var l in s1Fails.Take(40)) Info("FAILED " + l);
        Check("S1: snapshot = baseline on every character card of the pinned corpus (SHA-256)", s1Differ == 0 && s1Missing == 0 && s1Failed == 0 && (s1Equal > 0 || walk.Count == 0),
              $"{s1Equal} equal, {s1Differ} differ, {s1Missing} without a baseline, {s1Failed} failed, {nonCharacter} not character cards, of {walk.Count} files; outside the corpus (not compared): {s1New} new, {s1Changed} changed");
        Check("G1: materials drawn = OwnedMaterials, 0 unowned slots, 0 dead materials, body / face draw live", g1Fail == 0,
              $"cards failing {g1Fail} of {s1Equal + s1Differ + s1Missing}; slots {g1Slots}, unowned slots {g1Unowned}, bundle slots {g1Bundle}, drawn materials {g1Drawn}, owned materials {g1Owned}, dead (owned, not drawn) {g1Dead}; body draw dead on {bodyDead} cards, face draw dead on {faceDead}");
        Info($"S1 median build {Median(s1BuildMs):F0} ms over {s1BuildMs.Count} cards");
        rep.AppendLine("== S2 (MakerEditTest.all): allcards_s2.txt");
        Check("S2: morph(A → B) = fresh build of B, documents = B's (MakerEditTest's checks)", (s2Fail == 0 && s2.Pairs > 0) || walk.Count == 0,
              $"{s2Pass} passed, {s2Fail} failed; {s2.Equal} equal, {s2.Differ} differ, {s2.Failed} failed of {s2.Pairs} pairs; rebuilt by rule {s2.ByRule} ({s2.ByRuleEqual} equal), rebuilt for another reason {s2.RebuiltOther}; {s2.Fixups} consequence fix-ups, {s2.NotSettable} with members not settable");
        rep.AppendLine("== import statistics (CardImport.female / CardImport.male): allcards_cardimport_<folder>.txt");
        foreach (var fs in stats.Values)
        {
            int nonCards = fs.Failures.Where(kv => kv.Key.StartsWith("card load: ", StringComparison.Ordinal) && !kv.Key.Contains("HS2/AIS chara payload")).Sum(kv => kv.Value);
            Info($"CardImport.{fs.Name}: {fs.Line(nonCards)}");
            Check($"CardImport.{fs.Name}: every character card builds (failures only for files that are not character cards), no motion step error, no non-finite value",
                  fs.Files - fs.Ok == nonCards && fs.WithStepErrors == 0 && fs.WithNaN == 0 && fs.WithNaNValues == 0,
                  string.Join(", ", fs.Failures.Select(kv => kv.Key + " x" + kv.Value)));
        }
        rep.AppendLine("== loose-PNG references (LooseTextureTest.cards): allcards_loose.txt");
        Info($"{looseChars} character cards imported ({looseFailed} failed); {looseWithLoose} reference loose-PNG rows, {looseSlots} slots; by slot: {string.Join(", ", looseBySlot.OrderBy(k => k.Key).Select(k => k.Key + " " + k.Value))}");
        Check("loose: every character card imported", looseFailed == 0, looseFailed + " failed");
        if (coords)
        {
            rep.AppendLine("== coordinates (CoordApply.all): allcards_coords.txt");
            if (coordPart != "import")
                Check("coordinates: every coordinate worn = a fresh build, saved and reloaded (MakerFileTest's checks)", MakerTestKit.Fails == 0, $"{MakerTestKit.Passes} passed, {MakerTestKit.Fails} failed");
        }
        rep.AppendLine();
        rep.AppendLine("minutes per step: " + string.Join(", ", time.Select(kv => $"{kv.Key} {kv.Value:F1}")) + $"; wall {total.Elapsed.TotalMinutes:F1}");
        rep.Insert(0, $"SUMMARY: {passes} PASS, {fails} FAIL ({total.Elapsed.TotalSeconds:F0} s)\nall cards: import, P1, S1, import statistics, S2, loose-PNG references, coordinates\n");
        File.WriteAllText(Path.Combine(Out, "allcards_report.txt"), rep.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        try { File.Delete(current); } catch (Exception) { }
        Debug.Log($"ALL CARDS TEST DONE: {passes} PASS, {fails} FAIL in {total.Elapsed.TotalMinutes:F1} min");
        if (fails > 0) TestExit.Exit(1);
    }

    /// <summary>The card's place in the pinned corpus (MakerCoreTest's rule): "pinned", "new" or "changed".</summary>
    static string CorpusState(Dictionary<string, string> index, string rel, string cardSha)
    {
        if (index == null || rel == null) return "pinned";
        if (!index.TryGetValue(rel, out var sha)) return "new";
        return cardSha == null || sha == cardSha ? "pinned" : "changed";
    }

    /// <summary>CardImportTest's build of one card (asm.BuildFromDocuments), its numbers and the motion steps; the build is
    /// destroyed afterwards.</summary>
    static void RawStats(FolderStats fs, string rel, Card card, ContentCatalog gd, List<(LogType type, string msg)> cardLog)
    {
        var roots = new HashSet<GameObject>(SceneManager.GetActiveScene().GetRootGameObjects());
        int logStart = gd.Log.Count;
        var asm = new CharaAssembler(gd);
        var sw = Stopwatch.StartNew();
        GameObject chara = null;
        string fail = null;
        try { chara = asm.BuildFromDocuments(card); }
        catch (Exception e) { fail = CardImportTest.Classify(e); }
        double ms = sw.Elapsed.TotalMilliseconds;
        try
        {
            if (fail != null || !chara) { fs.Fail(fail ?? "build: no root"); fs.Body.AppendLine($"== {rel}  {ms:F0} ms  parts {asm.PartsLoaded}  FAILED {fail ?? "build: no root"}"); }
            else Stats(fs, rel, chara, asm.Context, asm, ms, gd, logStart, cardLog);
        }
        finally
        {
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (roots.Contains(go)) continue;
                UnityObjects.ReleaseRenderTextures(go.transform);
                Object.DestroyImmediate(go);
            }
        }
    }

    /// <summary>CardImportTest's per-card numbers of one build, then the motion steps.</summary>
    static void Stats(FolderStats fs, string rel, GameObject root, CharaContext ctx, CharaAssembler asm, double buildMs, ContentCatalog gd, int logStart, List<(LogType type, string msg)> cardLog)
    {
        fs.Ok++;
        fs.BuildMs += buildMs;
        var log = asm != null ? asm.Log : new List<string>();
        var shaders = CardImportTest.Unfitted(root);
        foreach (var kv in shaders)
        {
            fs.ShaderMats[kv.Key] = fs.ShaderMats.TryGetValue(kv.Key, out var n) ? n + kv.Value : kv.Value;
            fs.ShaderCards[kv.Key] = fs.ShaderCards.TryGetValue(kv.Key, out var c) ? c + 1 : 1;
        }
        var missing = log.Where(l => l.StartsWith("MISSING") || l.Contains("prefab not found")).ToList();
        // the document resolver's missing parts (Context.Missing part.*), counted as CardImportTest counts them
        if (ctx != null) missing.AddRange(ctx.Missing.Where(m => m.Kind.StartsWith("part.", StringComparison.Ordinal)).Select(m => "MISSING " + m));
        var catLog = gd.Log.Skip(logStart).Distinct().ToList();
        int mods = catLog.Count(l => l.StartsWith("missing mod"));
        fs.MissingRows += missing.Count; fs.MissingMods += mods;
        if (missing.Count + mods > 0) fs.WithMissing++;
        fs.Body.AppendLine($"== {rel}  {buildMs:F0} ms  parts {asm?.PartsLoaded ?? 0}  OK");
        bool texAssert = cardLog.Any(l => l.type == LogType.Assert && l.msg.Contains("mipLevel"));
        if (texAssert) fs.WithTexAssert++;
        if (cardLog.Any(l => l.msg.StartsWith(EditModeMaterial, StringComparison.Ordinal))) fs.WithEditMat++;
        var other = cardLog.Where(l => !l.msg.StartsWith(EditModeMaterial, StringComparison.Ordinal) && !(l.type == LogType.Assert && l.msg.Contains("mipLevel"))).ToList();
        if (other.Count > 0) fs.WithLogErrors++;
        foreach (var g in other.GroupBy(l => (l.type, l.msg))) fs.Body.AppendLine($"   log {g.Key.type}: {g.Key.msg} x{g.Count()}");
        foreach (var l in missing) fs.Body.AppendLine("   " + l);
        foreach (var l in catLog) fs.Body.AppendLine("   " + l);
        if (shaders.Count > 0) fs.Body.AppendLine("   unfitted shaders: " + string.Join(", ", shaders.Select(kv => $"{kv.Key} x{kv.Value}")));
        if (ctx == null) return;
        foreach (var m in ctx.Missing) if (!m.Kind.StartsWith("part.", StringComparison.Ordinal)) fs.Body.AppendLine("   MISSING content " + m);
        if (ctx.Missing.Any(m => m.Kind == "texture.badmip")) fs.WithGuard++;
        var motionFail = log.Where(l => l.StartsWith("motion:") && l.Contains("failed")).ToList();
        if (motionFail.Count > 0) fs.WithMotionFail++;
        foreach (var l in motionFail) fs.Body.AppendLine("   MOTION ERROR " + l);
        if (ctx.Motion == null) { fs.Body.AppendLine("   motion: no CharaMotion on the character"); return; }
        var stages = ctx.Motion.Stages;
        fs.StagesTotal += stages.Count;
        foreach (var s in stages)
        {
            string key = s.Enabled ? s.Name : s.Name + " (off)";
            fs.StageCensus[key] = fs.StageCensus.TryGetValue(key, out var sc) ? sc + 1 : 1;
        }
        string names = string.Join(" ", stages.Select(s => s.Enabled ? s.Name : s.Name + "(off)"));
        int ran = 0, bad = 0;
        int errBefore = ctx.Motion.Errors;
        var swm = Stopwatch.StartNew();
        try { for (int k = 0; k < Steps; k++) { ctx.Motion.Step(1f / 60f); ran++; } }
        catch (Exception e) { fs.Body.AppendLine("   MOTION ERROR step threw " + e.GetType().Name + ": " + e.Message); fs.WithStepErrors++; }
        double stepMs = swm.Elapsed.TotalMilliseconds;
        int errs = ctx.Motion.Errors - errBefore;
        foreach (var tr in ctx.BodyBones.Values)
        {
            if (!tr) continue;
            var p = tr.position; var r = tr.localRotation;
            if (!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z) ||
                !float.IsFinite(r.x) || !float.IsFinite(r.y) || !float.IsFinite(r.z) || !float.IsFinite(r.w)) bad++;
        }
        CardImportTest.NonFinite(root, ctx.BodyBones.Values, out int badOther, out int badWeights);
        if (errs > 0) { fs.WithStepErrors++; foreach (var l in log.Where(l => l.StartsWith("motion:") && !motionFail.Contains(l))) fs.Body.AppendLine("   MOTION ERROR " + l); }
        if (bad > 0) { fs.WithNaN++; fs.Body.AppendLine($"   MOTION ERROR {bad} body bones non-finite after {Steps} steps"); }
        if (badOther + badWeights > 0) { fs.WithNaNValues++; fs.Body.AppendLine($"   MOTION ERROR {badOther} other transforms, {badWeights} blend-shape weights non-finite after {Steps} steps"); }
        fs.StepsRun += ran; fs.StepMsTotal += stepMs; fs.StageExceptions += errs;
        fs.NonFiniteBones += bad; fs.NonFiniteOther += badOther; fs.NonFiniteWeights += badWeights;
        fs.Body.AppendLine($"   motion: {stages.Count} stages [{names}], {ran} steps{(ran > 0 ? $" ({stepMs / ran:F3} ms/step)" : "")}, " +
                           $"stage exceptions {errs}, non-finite: bones {bad}, other transforms {badOther}, blend-shape weights {badWeights}");
    }
}
