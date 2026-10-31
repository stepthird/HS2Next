// Maker core tests of Phase 4.0 (package S: the baselines and the S1 skeleton).
//   baseline (RunBaseline; headless): every character card → importer → stored documents → today's scene builder path
//            (SubtreeCharaBuilder.Build → CharaBuilders.Run: CharaAssembler.Build, root under the holder, stray roots,
//            ShaderMap.ApplyUrp) → CharaSnapshot (after one CharaMotion.Step(0)) written to
//            Cache/maker/baseline/<card sha>.snap (gzip text, gitignored) with index.tsv (card, sha, snapshot sha, lines,
//            build ms, error) and the commit the baselines were taken on. Existing snapshots are kept unless -overwrite
//            (re-take after a deliberate delta). Pass: one snapshot per character card, 0 build failures.
//   S1       (RunS1; headless): the same build path vs the stored baselines: SHA-256 equal on every card (a difference
//            prints the first 20 differing entries); the G1 check (materials drawn = OwnedMaterials, 0 unowned slots,
//            0 dead materials, the context's body / face draw materials drawn), counted per card and judged unless
//            -g1 info (today's ApplyUrp(copy: true) fails it by design; package A's C2 makes it pass). -twice: every card
//            is also built a second time and snapshotted twice (warm build = cold build; a second Take of the same
//            character = the first).
//   U8       (RunU8; graphics): read-back hashing time of every render texture of the visual targets (S3 cost).
// Card sets: default = the visual targets (Tools/targets.json) + 20 cards spread over the card folders (CharaDocBuildTest's
// pick); -targets = the visual targets; -all = every card of the card folders (female, then male, ordinal by path);
// -limit N cuts the list. -dir <folder> uses another baseline folder (determinism runs).
// Run: -executeMethod MakerCoreTest.RunBaseline [-all|-targets] [-overwrite] [-dir d] [-limit N]
//      -executeMethod MakerCoreTest.RunS1 [-all|-targets] [-g1 info] [-twice] [-dir d] [-limit N]
//      -executeMethod MakerCoreTest.RunU8 [-reps N]          (graphics device; no -nographics)
// Reports: Spike/out/maker/maker_baseline_report.txt, maker_s1_report.txt, maker_u8_report.txt; first line
// "SUMMARY: N PASS, M FAIL"; EditorApplication.Exit(1) on any FAIL. Nothing is rendered or shown (numbers only).
// API for packages A, C, T: MakerCoreTest.Harness (Open, Build(card file), Build(container), ReleaseBundles), Built
// (Root, Context, Result, Snapshot(options), Dispose), BaselineDir, BaselinePath(sha), LoadBaseline(sha), SampleCards(),
// AllCards(); the snapshot itself: HS2Next.Tests.MakerCore.CharaSnapshot.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HS2Next.Chara;
using HS2Next.Content;
using HS2Next.Import;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Tests.MakerCore;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class MakerCoreTest
{
    // ================================================================ paths

    public static string BaselineDir => Path.Combine(ContentSettings.ProjectRoot, "Cache", "maker", "baseline");
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "maker");
    static string Dir => Arg("-dir") is string d ? Path.GetFullPath(d) : BaselineDir;
    public static string BaselinePath(string cardSha, string dir = null) => Path.Combine(dir ?? BaselineDir, cardSha + ".snap");
    public static CharaSnapshot LoadBaseline(string cardSha, string dir = null)
    {
        string p = BaselinePath(cardSha, dir);
        return File.Exists(p) ? CharaSnapshot.Load(p) : null;
    }

    // ================================================================ harness

    /// <summary>A built character of the harness. Dispose releases its render textures and destroys it.</summary>
    public sealed class Built : IDisposable
    {
        public string File, Rel, CardSha, Error;
        /// <summary>The file is not a character card (skipped, not an error).</summary>
        public bool NotCharacter;
        public CharaContainer Container;
        public CharaBuildResult Result;
        public SceneRuntime Runtime;
        public double ImportMs, BuildMs;
        public GameObject Root => Result?.Root;
        public CharaContext Context => Result?.Context;
        public bool Ok => Error == null && Result != null && Result.Ok;

        public CharaSnapshot Snapshot(SnapshotOptions o = null) => CharaSnapshot.Take(Root, Context, o);

        public void Dispose()
        {
            if (Root) UnityObjects.ReleaseRenderTextures(Root.transform);
            if (Root) Object.DestroyImmediate(Root);
            Runtime?.Dispose();
            Runtime = null;
        }
    }

    /// <summary>
    /// The build path of the scene: catalog, services (default extension options, as the app), the importer
    /// with the round trip's fixed clock, and per character a fresh session + runtime whose scene file holds the
    /// character's documents and resources (SceneContainer.PlaceCharacter, as SceneSession.AddCharaFromContainer does),
    /// built by SubtreeCharaBuilder under a holder at the origin. No node is added, so no realization runs.
    /// </summary>
    public sealed class Harness : IDisposable
    {
        public ContentCatalog Catalog;
        public RuntimeServices Services;
        public ICharaImporter Importer;

        public static Harness Open(StringBuilder head = null)
        {
            var h = new Harness();
            h.Catalog = CardResolveTest.BuildDb(head ?? new StringBuilder());
            h.Services = new RuntimeServices { Catalog = h.Catalog, Hs2Root = CardResolveTest.GameRoot, Settings = new SceneRuntimeSettings { ManualStepping = true } };
            h.Importer = CharaDocBuildTest.NewImporter(h.Catalog);
            return h;
        }

        /// <summary>Card file → import → build. Never throws: failures are in Built.Error.</summary>
        public Built Build(string cardFile)
        {
            var b = new Built { File = cardFile, Rel = RelOf(cardFile) };
            byte[] bytes;
            try { bytes = System.IO.File.ReadAllBytes(cardFile); }
            catch (Exception e) { b.Error = "card not readable: " + e.GetType().Name + ": " + e.Message; return b; }
            b.CardSha = Sha(bytes);
            if (Hs2Importer.Detect(bytes) != Hs2SourceKind.Chara) { b.NotCharacter = true; return b; }
            var sw = Stopwatch.StartNew();
            var src = new CardAppearanceSource { FullPath = cardFile, Sha256 = b.CardSha, ReadBytes = () => bytes, Name = Path.GetFileNameWithoutExtension(cardFile) };
            try { b.Container = Importer.Import(src, new FindingList()); }
            catch (Exception e) { b.Error = "import threw " + e.GetType().Name + ": " + e.Message; return b; }
            b.ImportMs = sw.Elapsed.TotalMilliseconds;
            if (b.Container == null) { b.Error = "the importer returned nothing"; return b; }
            return BuildInto(b);
        }

        /// <summary>A character container (e.g. edited documents) → build.</summary>
        public Built Build(CharaContainer container, string label = null)
        {
            var b = new Built { File = label, Rel = label, Container = container };
            return BuildInto(b);
        }

        Built BuildInto(Built b)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var session = SceneRuntimeTest.NewSession(Services);
                b.Runtime = new SceneRuntime(session, Services, null, false);
                string id = JsonText.DerivedId("makercore/" + (b.CardSha ?? b.Rel ?? "container"));
                session.Model.File.PlaceCharacter(id, b.Container);
                var appearance = new CharaAppearance { Stored = session.Model.File.Charas[id] };
                var holder = new GameObject("MakerCoreTest holder").transform;
                holder.SetParent(b.Runtime.NodesRoot, false);
                var builder = CharaBuilders.For(appearance);
                if (!(builder is SubtreeCharaBuilder)) { b.Error = "the builder of stored documents is " + (builder?.GetType().Name ?? "none"); return b; }
                b.Result = builder.Build(appearance, new BuildContext { Runtime = b.Runtime, Services = Services, Holder = holder });
                if (!b.Result.Ok) b.Error = "build failed: " + (b.Result.Error ?? "no root");
            }
            catch (Exception e) { b.Error = "build threw " + e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace); }
            b.BuildMs = sw.Elapsed.TotalMilliseconds;
            return b;
        }

        /// <summary>Unloads the catalog's bundles and unused assets (every few cards in a long run).</summary>
        public void ReleaseBundles()
        {
            Catalog.UnloadAll();
            EditorUtility.UnloadUnusedAssetsImmediate(true);
            GC.Collect();
        }

        public void Dispose() => Catalog?.UnloadAll();
    }

    // ================================================================ card sets

    static string UserData => TestTargets.UserData;
    static string RelOf(string file)
    {
        if (file == null) return null;
        string full = Path.GetFullPath(file), ud = Path.GetFullPath(UserData).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return (full.StartsWith(ud, StringComparison.OrdinalIgnoreCase) ? full.Substring(ud.Length) : Path.GetFileName(full)).Replace('\\', '/');
    }

    /// <summary>Every card of the card folders of Tools/targets.json: female, then male, recursive, ordinal by relative path.</summary>
    public static List<string> AllCards()
    {
        var t = TestTargets.Load();
        var l = new List<string>();
        foreach (var rel in new[] { t.cards?.female, t.cards?.male })
        {
            if (string.IsNullOrEmpty(rel)) continue;
            string d = TestTargets.Abs(rel);
            if (!Directory.Exists(d)) continue;
            l.AddRange(Directory.GetFiles(d, "*.png", SearchOption.AllDirectories)
                .OrderBy(f => f.Substring(d.Length).TrimStart('\\', '/').Replace('\\', '/'), StringComparer.Ordinal));
        }
        return l;
    }

    /// <summary>The visual targets + 20 cards spread over the card folders (CharaDocBuildTest's P2 pick).</summary>
    public static List<string> SampleCards(int n = 20)
    {
        var t = TestTargets.Load();
        var sample = new List<string>(t.VisualCards.Where(File.Exists));
        var pool = AllCards().Where(f => !sample.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();
        n = Math.Min(n, pool.Count);
        for (int i = 0; i < n; i++) sample.Add(pool[(int)((long)i * pool.Count / n)]);
        return sample;
    }

    /// <summary>
    /// The corpus the baselines of <paramref name="dir"/> were taken on (its index.tsv: card path relative to UserData → card
    /// SHA-256) and the commit in its header; null without an index. The card folders are the local HS2 UserData in use, so a
    /// card saved or replaced since then is new or changed: S1 counts such cards apart (not compared) instead of failing on
    /// them or absorbing them silently.
    /// </summary>
    public static Dictionary<string, string> CorpusIndex(string dir, out string commit)
    {
        commit = null;
        string p = Path.Combine(dir ?? BaselineDir, "index.tsv");
        if (!File.Exists(p)) return null;
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(p))
        {
            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                int i = line.IndexOf("commit ", StringComparison.Ordinal);
                if (commit == null && i >= 0) commit = new string(line.Substring(i + 7).TakeWhile(Uri.IsHexDigit).ToArray());
                continue;
            }
            var f = line.Split('\t');
            if (f.Length >= 2 && f[1] != "-") d[f[0]] = f[1];
        }
        return d;
    }

    /// <summary>The card's place in the pinned corpus: "pinned", "new" (not in the index) or "changed" (another SHA-256).</summary>
    static string CorpusState(Dictionary<string, string> index, string rel, string cardSha)
    {
        if (index == null || rel == null) return "pinned";
        if (!index.TryGetValue(rel, out var sha)) return "new";
        return cardSha == null || sha == cardSha ? "pinned" : "changed";
    }

    static List<string> CardsFromArgs(out string what)
    {
        List<string> l;
        if (Flag("-all")) { l = AllCards(); what = "every card of the card folders"; }
        else if (Flag("-targets")) { l = TestTargets.Load().VisualCards.Where(File.Exists).ToList(); what = "the visual targets"; }
        else { l = SampleCards(); what = "the visual targets + 20 sampled cards"; }
        int limit = int.Parse(Arg("-limit", int.MaxValue.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        if (l.Count > limit) l = l.Take(limit).ToList();
        return l;
    }

    // ================================================================ report

    static StringBuilder rep;
    static int passes, fails;

    static void Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
    }

    static void Info(string s) => rep.AppendLine("   " + s);

    static void Reset() { rep = new StringBuilder(); passes = fails = 0; }

    static void Finish(string file, string title, Stopwatch sw)
    {
        rep.Insert(0, $"SUMMARY: {passes} PASS, {fails} FAIL ({sw.Elapsed.TotalSeconds:F0} s)\n{title}\n");
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, file), rep.ToString());
        Debug.Log($"MAKER CORE TEST DONE ({file}): {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }

    static string Arg(string name, string def = null)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static bool Flag(string name) => TestArgs.Get().Contains(name);

    static string Sha(byte[] b)
    {
        using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-", "").ToLowerInvariant();
    }

    static string Short(string s, int n = 300) => s == null ? "" : s.Length > n ? s.Substring(0, n) + "…" : s;

    static double Median(List<double> l)
    {
        if (l.Count == 0) return double.NaN;
        var s = l.OrderBy(x => x).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }

    /// <summary>HEAD of the project and the number of changed paths under Assets outside Assets/HS2Next/Editor (runtime code, shaders, data).</summary>
    static string Commit(out int dirty)
    {
        dirty = -1;
        string Git(string args)
        {
            try
            {
                var p = new Process { StartInfo = new ProcessStartInfo("git", "-C \"" + ContentSettings.ProjectRoot + "\" " + args) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true } };
                p.Start();
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit(20000);
                return o;
            }
            catch (Exception e) { return "git failed: " + e.Message; }
        }
        string head = Git("rev-parse HEAD").Trim();
        string st = Git("status --porcelain -- Assets Packages ProjectSettings \":(exclude)Assets/HS2Next/Editor\"");
        dirty = st.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
        return head;
    }

    // ================================================================ baseline

    public static void RunBaseline()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        var cards = CardsFromArgs(out string what);
        string dir = Dir;
        bool overwrite = Flag("-overwrite");
        string commit = Commit(out int dirty);
        rep.AppendLine($"maker core baseline (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}), {cards.Count} files: {what}");
        rep.AppendLine($"commit {commit}, changed paths outside the Editor tests {dirty}; folder {(dir == BaselineDir ? "Cache/maker/baseline" : dir)}{(overwrite ? ", -overwrite" : "")}");
        Directory.CreateDirectory(dir);
        string prevCommit = null;
        var prev = Flag("-all") ? CorpusIndex(dir, out prevCommit) : null;
        int writtenNew = 0, writtenChanged = 0;
        var head = new StringBuilder();
        var index = new List<string>();
        int written = 0, kept = 0, notChara = 0, failed = 0, n = 0;
        var buildMs = new List<double>(); var takeMs = new List<double>();
        long bytesText = 0;
        var failures = new List<string>();
        using (var h = Harness.Open(head))
        {
            rep.Append(head);
            foreach (var file in cards)
            {
                n++;
                if (n % 25 == 0) Debug.Log($"MakerCoreTest baseline: {n}/{cards.Count}, {sw.Elapsed.TotalSeconds:F0} s");
                string rel = RelOf(file);
                Built b = null;
                try
                {
                    // An existing snapshot is kept unless -overwrite (the file name is the card's SHA-256).
                    if (!overwrite)
                    {
                        string sha0 = Sha(File.ReadAllBytes(file));
                        string p0 = BaselinePath(sha0, dir);
                        if (File.Exists(p0)) { kept++; index.Add(string.Join("\t", rel, sha0, CharaSnapshot.Load(p0).Sha256, "kept")); continue; }
                    }
                    b = h.Build(file);
                    if (b.NotCharacter) { notChara++; Info(rel + ": not a character card, skipped"); continue; }
                    if (!b.Ok) { failed++; failures.Add(rel + ": " + b.Error); index.Add(string.Join("\t", rel, b.CardSha ?? "-", "-", "error " + b.Error)); continue; }
                    var s = b.Snapshot();
                    s.Write(BaselinePath(b.CardSha, dir));
                    written++;
                    string st = CorpusState(prev, rel, b.CardSha);
                    if (!overwrite && st != "pinned") { if (st == "new") writtenNew++; else writtenChanged++; Info($"{rel}: {st} card (not in the previous index's corpus)"); }
                    bytesText += Encoding.UTF8.GetByteCount(s.Text);
                    buildMs.Add(b.BuildMs); takeMs.Add(s.TakeMs);
                    index.Add(string.Join("\t", rel, b.CardSha, s.Sha256, s.Lines.Count.ToString(CultureInfo.InvariantCulture), b.BuildMs.ToString("F0", CultureInfo.InvariantCulture), "G1 " + s.G1Text));
                    Info($"{rel}: {s.Lines.Count} lines, sha {s.Sha256.Substring(0, 16)}, build {b.BuildMs:F0} ms, snapshot {s.TakeMs:F0} ms");
                }
                catch (Exception e) { failed++; failures.Add(rel + ": " + e.GetType().Name + ": " + e.Message); index.Add(string.Join("\t", rel, b?.CardSha ?? "-", "-", "exception " + e.GetType().Name + ": " + e.Message)); }
                finally { b?.Dispose(); }
                if (n % 10 == 0) h.ReleaseBundles();
            }
        }
        var idx = new StringBuilder();
        // the header names the commit the kept baselines were taken on (a run that only keeps them does not move it); new
        // snapshots of new or changed cards are named with this run's commit
        if (!overwrite && prevCommit != null && kept > 0)
            idx.AppendLine($"# maker core baselines: commit {prevCommit} (kept {kept}); checked at commit {commit}, changed paths outside the Editor tests {dirty}; {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC; Unity {Application.unityVersion}; {what}" +
                (written > 0 ? $"; {written} snapshots written at {commit} ({writtenNew} new, {writtenChanged} changed cards)" : ""));
        else idx.AppendLine($"# maker core baselines: commit {commit}; changed paths outside the Editor tests {dirty}; {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC; Unity {Application.unityVersion}; {what}");
        idx.AppendLine("# card (relative to UserData)\tcard sha256\tsnapshot sha256\tlines | kept | error\tbuild ms\tG1");
        foreach (var l in index) idx.AppendLine(l);
        File.WriteAllText(Path.Combine(dir, Flag("-all") ? "index.tsv" : "index_" + (Flag("-targets") ? "targets" : "sample") + ".tsv"), idx.ToString());
        foreach (var f in failures) Info("build failure: " + f);
        Check("one snapshot per character card (written or kept), 0 build failures", failed == 0 && written + kept + notChara == cards.Count,
              $"{written} written, {kept} kept, {notChara} not character cards, {failed} failed of {cards.Count} files" +
              (prev != null && !overwrite ? $"; written for cards outside the previous corpus: {writtenNew} new, {writtenChanged} changed" : ""));
        Info($"median build {Median(buildMs):F0} ms, median snapshot {Median(takeMs):F0} ms, text {bytesText / Math.Max(1, written) / 1024} KiB per snapshot (gzip on disk)");
        Finish("maker_baseline_report.txt", "S1 baselines", sw);
    }

    // ================================================================ S1

    public static void RunS1()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        var cards = CardsFromArgs(out string what);
        string dir = Dir;
        bool g1Judged = Arg("-g1", "check") != "info";
        bool twice = Flag("-twice");
        string commit = Commit(out int dirty);
        rep.AppendLine($"maker core S1 (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}), {cards.Count} files: {what}; commit {commit}, changed paths outside the Editor tests {dirty}");
        var corpus = CorpusIndex(dir, out string corpusCommit);
        var outside = new List<string>();
        int newCards = 0, changedCards = 0;
        rep.AppendLine(corpus == null ? "corpus: no index.tsv in the baseline folder (every card compared)" : $"corpus: the index of the baselines ({corpus.Count} cards, commit {corpusCommit ?? "?"}); new or changed cards are counted apart, not compared");
        var head = new StringBuilder();
        int equal = 0, differ = 0, missing = 0, notChara = 0, failed = 0, g1Fail = 0, n = 0, warmDiffer = 0, retakeDiffer = 0;
        long unowned = 0, dead = 0, bundle = 0, slots = 0, owned = 0, drawn = 0;
        int bodyDead = 0, faceDead = 0;
        var buildMs = new List<double>();
        using (var h = Harness.Open(head))
        {
            rep.Append(head);
            foreach (var file in cards)
            {
                n++;
                if (n % 25 == 0) Debug.Log($"MakerCoreTest S1: {n}/{cards.Count}, {sw.Elapsed.TotalSeconds:F0} s");
                string rel = RelOf(file);
                Built b = null;
                try
                {
                    b = h.Build(file);
                    if (b.NotCharacter) { notChara++; continue; }
                    string state = CorpusState(corpus, rel, b.CardSha);
                    if (state != "pinned")
                    {
                        if (state == "new") newCards++; else changedCards++;
                        outside.Add(rel + " (" + state + ")");
                        continue;
                    }
                    if (!b.Ok) { failed++; Check(rel + ": built", false, b.Error); continue; }
                    buildMs.Add(b.BuildMs);
                    var s = b.Snapshot();
                    slots += s.G1Slots; unowned += s.G1UnownedSlots; dead += s.G1Dead; bundle += s.G1BundleSlots; owned += s.G1Owned; drawn += s.G1Drawn;
                    if (!s.G1BodyDrawLive) bodyDead++;
                    if (!s.G1FaceDrawLive) faceDead++;
                    if (!s.G1Pass) g1Fail++;
                    var basis = LoadBaseline(b.CardSha, dir);
                    if (basis == null) { missing++; Check(rel + ": baseline exists", false, "no " + b.CardSha + ".snap"); }
                    else
                    {
                        var d = CharaSnapshot.Compare(basis, s, 20);
                        if (d.Equal) { equal++; Info($"{rel}: equal ({s.Sha256.Substring(0, 16)}), G1 {s.G1Text}"); }
                        else { differ++; Check(rel + ": snapshot = baseline", false, "baseline vs now: " + d); }
                    }
                    if (twice)
                    {
                        var s2 = b.Snapshot();
                        var d2 = CharaSnapshot.Compare(s, s2, 10);
                        if (!d2.Equal) { retakeDiffer++; Check(rel + ": a second snapshot of the same character equals the first", false, d2.ToString()); }
                        b.Dispose();
                        b = h.Build(file);
                        if (!b.Ok) { failed++; Check(rel + ": built again", false, b.Error); continue; }
                        var s3 = b.Snapshot();
                        var d3 = CharaSnapshot.Compare(s, s3, 10);
                        if (!d3.Equal) { warmDiffer++; Check(rel + ": a second (warm) build equals the first", false, d3.ToString()); }
                    }
                }
                catch (Exception e) { failed++; Check(rel + ": no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace)); }
                finally { b?.Dispose(); }
                if (n % 10 == 0) h.ReleaseBundles();
            }
        }
        int built = cards.Count - notChara - failed - newCards - changedCards;
        int removed = 0;
        if (corpus != null && Flag("-all"))
        {
            var seen = new HashSet<string>(cards.Select(RelOf), StringComparer.Ordinal);
            foreach (var k in corpus.Keys.Where(k => !seen.Contains(k)).OrderBy(k => k, StringComparer.Ordinal)) { removed++; outside.Add(k + " (removed)"); }
        }
        foreach (var o in outside) Info("corpus: " + o);
        Check("S1: snapshot = baseline on every character card of the pinned corpus (SHA-256)", differ == 0 && missing == 0 && failed == 0 && built > 0,
              $"{equal} equal, {differ} differ, {missing} without a baseline, {failed} failed, {notChara} not character cards, of {cards.Count} files; " +
              $"outside the corpus (not compared): {newCards} new, {changedCards} changed" + (corpus != null && Flag("-all") ? $", {removed} removed" : ""));
        if (twice)
        {
            Check("determinism: a second snapshot of the same character equals the first", retakeDiffer == 0, $"{retakeDiffer} differ of {built}");
            Check("determinism: a second (warm) build equals the first", warmDiffer == 0, $"{warmDiffer} differ of {built}");
        }
        string g1 = $"cards failing {g1Fail} of {built}; slots {slots}, unowned slots {unowned}, bundle slots {bundle}, drawn materials {drawn}, owned materials {owned}, dead (owned, not drawn) {dead}; body draw dead on {bodyDead} cards, face draw dead on {faceDead}";
        if (g1Judged) Check("G1: materials drawn = OwnedMaterials, 0 unowned slots, 0 dead materials, body / face draw live (C2; fails before package A's ApplyUrp(copy: false))", g1Fail == 0, g1);
        else Info("G1 (not judged, -g1 info): " + g1);
        Info($"median build {Median(buildMs):F0} ms");
        Finish("maker_s1_report.txt", "S1 refactored build vs baseline", sw);
    }

    // ================================================================ U8

    public static void RunU8()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        int reps = int.Parse(Arg("-reps", "5"), CultureInfo.InvariantCulture);
        rep.AppendLine($"maker core U8 (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName}), read-back hashing of every render texture, {reps} repetitions per card");
        bool device = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
        Check("graphics device present", device, SystemInfo.graphicsDeviceType.ToString());
        if (device)
            using (var h = Harness.Open())
                foreach (var file in TestTargets.Load().VisualCards.Where(File.Exists))
                {
                    Built b = null;
                    try
                    {
                        b = h.Build(file);
                        if (!b.Ok) { Check(RelOf(file) + ": built", false, b.Error); continue; }
                        var ms = new List<double>(); var take = new List<double>();
                        CharaSnapshot s = null, first = null;
                        bool same = true;
                        for (int i = 0; i < reps; i++)
                        {
                            s = b.Snapshot(new SnapshotOptions { ReadBack = true, StepZero = i == 0 });
                            ms.Add(s.ReadBackMs); take.Add(s.TakeMs);
                            if (first == null) first = s; else if (s.Sha256 != first.Sha256) same = false;
                        }
                        Check(RelOf(file) + ": read-back hashes stable over the repetitions", same);
                        var plain = b.Snapshot(new SnapshotOptions { StepZero = false });
                        Info($"{RelOf(file)}: {s.ReadBackTextures} render textures, {s.ReadBackBytes / (1024.0 * 1024.0):F1} MiB read back; read-back + hash median {Median(ms):F1} ms " +
                             $"(min {ms.Min():F1}, max {ms.Max():F1}); snapshot without read-back median {Median(take):F1} ms; text lines {s.Lines.Count} (without read-back {plain.Lines.Count})");
                    }
                    catch (Exception e) { Check(RelOf(file) + ": no exception", false, e.GetType().Name + ": " + e.Message); }
                    finally { b?.Dispose(); }
                }
        Finish("maker_u8_report.txt", "U8 read-back hashing time", sw);
    }
}
