// Maker edits through the scene (F (realization), L; package C).
// Every character here is a scene node built by its realization (SceneSession.AddCharaFromContainer → SceneRuntime.Apply →
// CharaRealization → the builder), edited through StudioOps (the Maker ops) and compared with CharaSnapshot (package S)
// against a fresh node built from the edited documents in a scene of its own (same default node state).
//   S2     (RunS2; headless): pairs card i → i+1 within sex (the sex in the card decides, not its folder: a card in
//          the other sex's folder joins its own sex, reported as info), cyclic (default: the visual targets + 20 sampled cards;
//          -all: every card, 957 pairs). A's node gets B's resources and every member of B's documents through one
//          EditAppearance (rebuild-only members allowed: uncensor, studioOverlays, Character MaterialEditor items — such
//          pairs rebuild by rule, counted apart); the documents must then equal B's (members the op's rule consequences
//          changed are set again on the bus, counted); flush; snapshot = the snapshot of a fresh node of B. The fresh node of
//          B is the start of the next pair. Counted apart as well: pairs whose flush rebuilt for another reason (units
//          missing: every pair before A's units).
//   F      (RunFuzz; headless): BR-Chan and HERO107, seeds × operations (default 40 × 40, -seeds / -ops; -full 200 × 60):
//          (Phase 4.1: one operation in five is a drag through the preview / commit seam, one in five a face operation)
//          random Maker edits (shape values −1.5..2.5, colours, item changes from a pool of 40 rows per category, accessory
//          add / remove / move / item and category changes; 1–3 edits per transaction, some cancelled); every 10 operations,
//          after undo-all and after redo-all: snapshot = a fresh node of the current documents; undo-all also restores the
//          initial documents byte for byte and the initial snapshot.
//   L      (RunLeak; graphics): BR-Chan, 200 random part swaps (head, hair, clothes, accessory items) and undo-all, twice
//          (the first pass warms the bundles); after the second: live RenderTextures (count, bytes), Materials, Texture2Ds,
//          Meshes, the character's owned materials, its object count and the graveyard back to the values after the first
//          pass ± 0 (no UnloadUnusedAssets in between; the values after it are reported).
//   T      (RunTiming; graphics, warm, median of -reps 20): U5 part swaps per kind (cold = the first load of a row, warm,
//          undo of a swap) as op time (ChangeItem incl. the flush) and flush time; U6 the face preset push (≈ 40 units)
//          and a morph to the other target of the sex (all units). Reported against the targets (WARN, not FAIL).
// Run: -executeMethod MakerEditTest.RunS2 [-all|-targets|-list <file of card paths>] [-limit N]; MakerEditTest.RunFuzz [-seeds N] [-ops N] [-full];
//      MakerEditTest.RunLeak [-swaps N]; MakerEditTest.RunTiming [-reps N].
// Reports: Spike/out/maker/maker_s2_report.txt, maker_fuzz_report.txt, maker_leak_report.txt, maker_timing_report.txt;
// first line "SUMMARY: N PASS, M FAIL"; EditorApplication.Exit(1) on any FAIL. Numbers only (nothing is rendered).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Chara;
using HS2Next.Chara.Edit;
using HS2Next.Content;
using HS2Next.Content.Maker;
using HS2Next.Import;
using HS2Next.Motion.Face;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Tests.MakerCore;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class MakerEditTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "maker");

    // ================================================================ scene host

    /// <summary>One scene with one character node built by its realization.</summary>
    public sealed class Node : IDisposable
    {
        public SceneSession Session;
        public SceneRuntime Runtime;
        public StudioOps Ops;
        public string Id, Label, Error;
        public double BuildMs;
        public CharaRealization Real => Runtime?.FindChara(Id);
        public bool Ok => Error == null && Real != null && Real.State == RealizationState.Built;
        public CharaSubtree Stored => Session?.Model.Find(Id)?.Appearance?.Stored;

        public CharaSnapshot Snapshot(SnapshotOptions o = null)
        {
            var r = Real;
            return r != null && r.Root ? CharaSnapshot.Take(r.Root, r.Context, o) : null;
        }

        public void Apply() => Runtime.Apply();

        public void Dispose()
        {
            try { Runtime?.Dispose(); } catch (Exception e) { Debug.LogWarning("MakerEditTest: dispose: " + e.Message); }
            Runtime = null;
        }
    }

    /// <summary>A new scene with the character of <paramref name="c"/> added and built.</summary>
    public static Node NewNode(MakerCoreTest.Harness h, CharaContainer c, string label)
    {
        var n = new Node { Label = label };
        var sw = Stopwatch.StartNew();
        try
        {
            n.Session = SceneRuntimeTest.NewSession(h.Services);
            n.Runtime = new SceneRuntime(n.Session, h.Services, null, false);
            n.Ops = new StudioOps(n.Runtime);
            var r = n.Session.AddCharaFromContainer(c);
            if (r.Status != CommandStatus.Ok) { n.Error = "add failed: " + r.Message; return n; }
            n.Id = n.Session.Model.PreOrder().Where(x => x.Kind == NodeKind.Chara).Select(x => x.Id).LastOrDefault();
            n.Runtime.Apply();
            var real = n.Real;
            if (real == null || real.State != RealizationState.Built) n.Error = "not built: " + (real?.State.ToString() ?? "no realization") + " " + real?.Detail;
        }
        catch (Exception e) { n.Error = "threw " + e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace); }
        n.BuildMs = sw.Elapsed.TotalMilliseconds;
        return n;
    }

    /// <summary>Imports a card with the harness's importer (fixed clock); null with <paramref name="error"/> when it is not a character.</summary>
    public static CharaContainer Import(MakerCoreTest.Harness h, string file, out string error)
    {
        error = null;
        try
        {
            var bytes = File.ReadAllBytes(file);
            if (Hs2Importer.Detect(bytes) != Hs2SourceKind.Chara) { error = "not a character card"; return null; }
            string sha = CharaSnapshot.Hash(bytes);
            var src = new CardAppearanceSource { FullPath = file, Sha256 = sha, ReadBytes = () => bytes, Name = Path.GetFileNameWithoutExtension(file) };
            var c = h.Importer.Import(src, new FindingList());
            if (c == null) error = "the importer returned nothing";
            return c;
        }
        catch (Exception e) { error = "import threw " + e.GetType().Name + ": " + e.Message; return null; }
    }

    /// <summary>The sex stored in a character card's Parameter block with the importer's rule (0 male, any other number
    /// female); -1 when the card cannot be read or has no number there (the caller imports it instead).</summary>
    static int CardSex(byte[] bytes, string file)
    {
        try
        {
            var card = HS2Next.Data.Card.Load(bytes, file);
            object v = card?.Parameter == null ? null : HS2Next.IO.Node.Get(card.Parameter, "sex");
            if (!(v is long || v is ulong || v is float || v is double)) return -1;
            return HS2Next.IO.Node.Int(v, 1) == 0 ? 0 : 1;
        }
        catch (Exception) { return -1; }
    }

    /// <summary>A fresh node of the current documents of <paramref name="n"/> (its character file: documents + resources).</summary>
    static Node FreshOf(MakerCoreTest.Harness h, Node n, string label) => NewNode(h, n.Session.Model.File.ExtractCharacter(n.Id), label);

    // ================================================================ documents

    static JObject Json(CharaDocument d) => JObject.Parse(Encoding.UTF8.GetString(CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, d.Write)));
    static JObject Json(CoordDocument d) => JObject.Parse(Encoding.UTF8.GetString(CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, d.Write)));

    static readonly string[] CharaRebuildMembers = { "body.uncensor", "studioOverlays", "materialOverrides" };

    /// <summary>
    /// The member sets that turn the node's documents into <paramref name="b"/>'s: every member of chara.json and
    /// coord.json whose JSON differs, as appearance paths with model values; profile.sex / profile.hs2 / coord sex and
    /// `ext` are not settable (sex: pairs are within sex; hs2 is not built; ext is not read by builds). Rebuild-only
    /// members that differ are reported in <paramref name="rebuild"/>.
    /// </summary>
    static List<(string path, object value)> MorphSets(Node n, CharaSubtree b, List<string> rebuild, List<string> notSettable)
    {
        var a = n.Stored;
        var sets = new List<(string, object)>();
        void Member(string root, JObject ja, JObject jb, string member)
        {
            var va = ja[member]; var vb = jb[member];
            if (JToken.DeepEquals(va, vb)) return;
            string path = root + "." + member;
            if (vb == null) { notSettable.Add(path + " (absent in B)"); return; }
            var type = FieldAccessor.LeafType(n.Session.Model, n.Id, path);
            sets.Add((path, FieldJson.FromText(vb.ToString(Newtonsoft.Json.Formatting.None), type)));
            string rel = path.Substring(path.IndexOf('.', "appearance.".Length) + 1);
            if (CharaRebuildMembers.Contains(rel) && root.StartsWith("appearance.chara", StringComparison.Ordinal)) rebuild.Add(rel);
        }
        var ca = Json(a.Chara); var cb = Json(b.Chara);
        foreach (var p in cb.Properties().Select(x => x.Name).Union(ca.Properties().Select(x => x.Name)).ToList())
        {
            if (p == "ext") { if (!JToken.DeepEquals(ca[p], cb[p])) notSettable.Add("chara.ext"); continue; }
            if (p == "profile" || p == "body")
            {
                var pa = (JObject)ca[p] ?? new JObject(); var pb = (JObject)cb[p] ?? new JObject();
                foreach (var m in pb.Properties().Select(x => x.Name).Union(pa.Properties().Select(x => x.Name)).ToList())
                {
                    if (p == "profile" && (m == "sex" || m == "hs2")) { if (m == "sex" && !JToken.DeepEquals(pa[m], pb[m])) notSettable.Add("chara.profile.sex"); continue; }
                    Member("appearance.chara." + p, pa, pb, m);
                }
                continue;
            }
            Member("appearance.chara", ca, cb, p);
        }
        var ka = Json(a.Coord); var kb = Json(b.Coord);
        foreach (var p in kb.Properties().Select(x => x.Name).Union(ka.Properties().Select(x => x.Name)).ToList())
        {
            if (p == "sex") { if (!JToken.DeepEquals(ka[p], kb[p])) notSettable.Add("coord.sex"); continue; }
            if (p == "ext") { if (!JToken.DeepEquals(ka[p], kb[p])) notSettable.Add("coord.ext"); continue; }
            Member("appearance.coord", ka, kb, p);
        }
        return sets;
    }

    /// <summary>The members of chara.json / coord.json that differ (profile.hs2, sex and ext left out).</summary>
    static List<string> DocDiff(CharaSubtree a, CharaSubtree b)
    {
        var l = new List<string>();
        void Cmp(string doc, JObject x, JObject y)
        {
            foreach (var p in x.Properties().Select(q => q.Name).Union(y.Properties().Select(q => q.Name)))
            {
                if (p == "ext" || p == "sex") continue;
                if (p == "profile" || p == "body")
                {
                    var px = (JObject)x[p] ?? new JObject(); var py = (JObject)y[p] ?? new JObject();
                    foreach (var m in px.Properties().Select(q => q.Name).Union(py.Properties().Select(q => q.Name)))
                        if (!(p == "profile" && (m == "hs2" || m == "sex")) && !JToken.DeepEquals(px[m], py[m])) l.Add(doc + "." + p + "." + m);
                    continue;
                }
                if (!JToken.DeepEquals(x[p], y[p])) l.Add(doc + "." + p);
            }
        }
        Cmp("chara", Json(a.Chara), Json(b.Chara));
        Cmp("coord", Json(a.Coord), Json(b.Coord));
        return l;
    }

    static byte[] DocBytes(CharaSubtree s) =>
        CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, s.Chara.Write).Concat(CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, s.Coord.Write)).ToArray();

    /// <summary>The flushes and rebuilds of the node's realization since <paramref name="f0"/> / <paramref name="r0"/>.</summary>
    static string FlushNote(CharaRealization r, int f0, int r0) =>
        $"flushes {r.EditFlushes - f0}, rebuilds {r.EditRebuilds - r0}" + (r.EditRebuilds > r0 ? " (" + r.LastEditRebuild + ")" : "") +
        (r.LastEditFlush != null && r.EditFlushes > f0 ? ", last " + r.LastEditFlush : "");

    // ================================================================ S2

    public static void RunS2()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        var cards = CardsFromArgs(out string what);
        rep.AppendLine($"maker edit S2 (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}), {cards.Count} files: {what}; pairs i → i+1 within sex, cyclic");
        int? seed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        var head = new StringBuilder();
        var st = new S2Run { Watch = sw };
        try
        {
            using (var h = MakerCoreTest.Harness.Open(head))
            {
                rep.Append(head);
                foreach (var gl in S2Groups(h, cards).Values)
                {
                    var g = gl.Select(f => (rel: RelOf(f), file: f)).ToList();
                    if (g.Count < 2) continue;
                    Node cur = S2Start(h, st, g[0].rel, g[0].file, out _);
                    if (cur == null) continue;
                    for (int i = 0; i < g.Count; i++)
                    {
                        var (relB, fileB) = g[(i + 1) % g.Count];
                        cur = S2Pair(h, st, cur, relB, fileB, out var cB);
                        if (cB == null) continue;
                        if (st.Done % 10 == 0 && i + 1 < g.Count)
                        {
                            // AssetBundle.Unload(true) destroys the assets a live character uses (meshes, textures): the
                            // current node is rebuilt from B after the release
                            cur.Dispose();
                            h.ReleaseBundles();
                            cur = NewNode(h, cB, relB);
                        }
                    }
                    cur.Dispose();
                }
            }
        }
        finally { FaceSetup.Seed = seed; }
        S2Verdicts(st);
        Finish("maker_s2_report.txt", "S2 morph by units vs a fresh build", sw);
    }

    /// <summary>The counters of an S2 run (RunS2; AllCardsTest runs the same pairs within its card walk).</summary>
    internal sealed class S2Run
    {
        public int Pairs, Done, Equal, Differ, Failed, ByRule, ByRuleEqual, RebuiltOther, Fixups, DocDiffer, NotSettable;
        public readonly List<double> FlushMs = new List<double>(), OpMs = new List<double>(), UnitCounts = new List<double>();
        public readonly SortedDictionary<string, int> UnitDiffLines = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public Stopwatch Watch = Stopwatch.StartNew();
    }

    /// <summary>
    /// The S2 pair lists: the character cards of <paramref name="cards"/> by the sex in the card (a card can sit in the other
    /// sex's folder; sex is not settable, so a cross-sex pair is not a morph), female first, each in the order of
    /// <paramref name="cards"/>; pairs are i → i+1 within a list, cyclic.
    /// </summary>
    internal static SortedDictionary<int, List<string>> S2Groups(MakerCoreTest.Harness h, List<string> cards)
    {
        var t = TestTargets.Load();
        string fem = t.cards?.female != null ? Path.GetFullPath(TestTargets.Abs(t.cards.female)) : null;
        string mal = t.cards?.male != null ? Path.GetFullPath(TestTargets.Abs(t.cards.male)) : null;
        var bySex = new SortedDictionary<int, List<string>>(Comparer<int>.Create((x, y) => y.CompareTo(x)));
        int otherFolder = 0;
        foreach (var f in cards)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(f); } catch (Exception) { continue; }
            if (Hs2Importer.Detect(bytes) != Hs2SourceKind.Chara) continue;
            int sex = CardSex(bytes, f);
            if (sex < 0) { var c0 = Import(h, f, out _); if (c0 == null) continue; sex = c0.Subtree.Chara.Profile.Sex; }
            string full = Path.GetFullPath(f);
            int folder = fem != null && full.StartsWith(fem, StringComparison.OrdinalIgnoreCase) ? 1
                       : mal != null && full.StartsWith(mal, StringComparison.OrdinalIgnoreCase) ? 0 : -1;
            if (folder >= 0 && folder != sex)
            {
                otherFolder++;
                Info($"{RelOf(f)}: a {(sex == 0 ? "male" : "female")} card in the {(folder == 0 ? "male" : "female")} folder: paired within its own sex");
            }
            if (!bySex.TryGetValue(sex, out var l)) bySex[sex] = l = new List<string>();
            l.Add(f);
        }
        Info($"pairs per sex: {string.Join(", ", bySex.Select(kv => (kv.Key == 0 ? "male " : "female ") + kv.Value.Count))}; cards in the other sex's folder {otherFolder}");
        return bySex;
    }

    /// <summary>The node the first pair of a sex starts from (its first card); null when the card does not import (counted).</summary>
    internal static Node S2Start(MakerCoreTest.Harness h, S2Run st, string rel, string file, out CharaContainer c0)
    {
        c0 = Import(h, file, out string e0);
        if (c0 == null) { st.Failed++; Check(rel + ": imported", false, e0); return null; }
        return NewNode(h, c0, rel);
    }

    /// <summary>
    /// One S2 pair: the node <paramref name="cur"/> (A) gets B's resources and every member of B's documents through one
    /// EditAppearance; documents = B's; snapshot = the snapshot of a fresh node of B. Returns the node the next pair starts
    /// from (the fresh node of B; A unchanged when B did not import, <paramref name="cB"/> null then). Counts into
    /// <paramref name="st"/> and checks into this class's report.
    /// </summary>
    internal static Node S2Pair(MakerCoreTest.Harness h, S2Run st, Node cur, string relB, string fileB, out CharaContainer cB)
    {
        string pair = cur.Label + " → " + relB;
        cB = Import(h, fileB, out string eB);
        if (cB == null) { st.Failed++; st.Pairs++; Check(pair + ": B imported", false, eB); return cur; }
        var b = cB;
        st.Pairs++;
        st.Done++;
        if (st.Done % 25 == 0) Debug.Log($"MakerEditTest S2: {st.Done} pairs, {st.Watch.Elapsed.TotalSeconds:F0} s");
        Node fresh = null;
        void Body()
        {
            if (!cur.Ok) { st.Failed++; Check(pair + ": A built", false, cur.Error); return; }
            var real = cur.Real;
            int f0 = real.EditFlushes, r0 = real.EditRebuilds;
            // B's resources into the scene carrier, then every member of B's documents in one entry
            cur.Session.Model.File.Res.CopyFrom(b.Res, b.Subtree.ReferencedResources());
            var rebuild = new List<string>(); var ns = new List<string>();
            var sets = MorphSets(cur, b.Subtree, rebuild, ns);
            if (ns.Count > 0) { st.NotSettable++; Info(pair + ": not settable, differs: " + string.Join(", ", ns)); }
            var opw = Stopwatch.StartNew();
            var op = sets.Count > 0 ? cur.Ops.EditAppearance(new[] { cur.Id }, sets, new ExecuteOptions { Source = CommandSource.User, Label = "Morph" }, allowRebuild: true) : OpResult.NothingToDo();
            if (!op.Ok) { st.Failed++; Check(pair + ": EditAppearance", false, op.Code + ": " + op.Message); }
            cur.Apply();
            var diff = DocDiff(cur.Stored, b.Subtree);
            if (diff.Count > 0)
            {
                // rule consequences of the op rewrote members: set B's values again on the bus
                st.Fixups++;
                var fix = MorphSets(cur, b.Subtree, new List<string>(), new List<string>());
                using (var tx = cur.Session.Commands.BeginOrJoin("Morph fix-up", new ExecuteOptions { Source = CommandSource.User }))
                {
                    foreach (var (p, v) in fix) tx.Set(cur.Id, p, v);
                    tx.Commit();
                }
                cur.Apply();
                var d2 = DocDiff(cur.Stored, b.Subtree);
                Info(pair + ": the op's consequences changed " + string.Join(", ", diff) + (d2.Count > 0 ? "; still differ: " + string.Join(", ", d2) : "; set again"));
                if (d2.Count > 0) { st.DocDiffer++; Check(pair + ": documents = B's", false, string.Join(", ", d2)); }
            }
            opw.Stop();
            real = cur.Real;
            bool rebuilt = real == null || real.EditRebuilds > r0;
            var lf = real?.LastEditFlush;
            if (!rebuilt && lf != null && real.EditFlushes > f0) { st.FlushMs.Add(lf.Milliseconds); st.UnitCounts.Add(lf.Units.Count); }
            st.OpMs.Add(opw.Elapsed.TotalMilliseconds);
            var sm = cur.Snapshot();
            fresh = NewNode(h, b, relB);
            if (!fresh.Ok) { st.Failed++; Check(relB + ": fresh node built", false, fresh.Error); return; }
            var sb = fresh.Snapshot();
            var d = CharaSnapshot.Compare(sb, sm, 20);
            bool rule = rebuild.Count > 0;
            if (rule) { st.ByRule++; if (d.Equal) st.ByRuleEqual++; }
            else if (rebuilt) st.RebuiltOther++;
            string note = (rule ? "rebuild by rule (" + string.Join(", ", rebuild) + "), " : "") + (real != null ? FlushNote(real, f0, r0) : "");
            if (d.Equal) { st.Equal++; Info($"{pair}: equal ({sm.Sha256.Substring(0, 16)}), {sets.Count} sets, op {opw.Elapsed.TotalMilliseconds:F0} ms; {note}"); }
            else
            {
                st.Differ++;
                foreach (var c in d.Counts) st.UnitDiffLines[c.Key] = (st.UnitDiffLines.TryGetValue(c.Key, out var k) ? k : 0) + c.Value;
                Check(pair + ": morph = fresh build of B", false, note + "; fresh vs morph: " + d);
            }
        }
        try { Body(); }
        catch (Exception e) { st.Failed++; Check(pair + ": no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace)); }
        finally
        {
            cur.Dispose();
            cur = fresh ?? NewNode(h, b, relB);
        }
        return cur;
    }

    /// <summary>The S2 verdicts of a run (three checks and the timing line).</summary>
    internal static void S2Verdicts(S2Run st)
    {
        Check("S2: morph(A → B) = fresh build of B on every pair (SHA-256)", st.Differ == 0 && st.Failed == 0 && st.Pairs > 0,
              $"{st.Equal} equal, {st.Differ} differ, {st.Failed} failed of {st.Pairs} pairs; rebuilt by rule {st.ByRule} ({st.ByRuleEqual} equal), rebuilt for another reason (units missing) {st.RebuiltOther}");
        Check("S2: the differing-unit report lists 0 lines", st.UnitDiffLines.Count == 0, string.Join(", ", st.UnitDiffLines.Select(kv => kv.Key + " " + kv.Value)));
        Check("S2: documents = B's after the morph", st.DocDiffer == 0, $"{st.DocDiffer} pairs differ; {st.Fixups} pairs needed the consequence fix-up; {st.NotSettable} pairs with members that are not settable (sex, ext)");
        Info($"op (EditAppearance + flush) median {Median(st.OpMs):F1} ms, max {(st.OpMs.Count > 0 ? st.OpMs.Max() : 0):F1} ms over {st.OpMs.Count} pairs; " +
             $"flush median {Median(st.FlushMs):F1} ms (units median {Median(st.UnitCounts):F0}) over {st.FlushMs.Count} flushes that ran units (U6 morph)");
    }

    /// <summary>A fresh report (AllCardsTest: the S2 checks of its walk), its text and counts.</summary>
    internal static void ReportBegin() => Reset();
    internal static string ReportText => rep?.ToString() ?? "";
    internal static (int passed, int failed) ReportCounts => (passes, fails);

    // ================================================================ pools

    /// <summary>Up to 40 rows per category for a sex: the rows the targets use first, then listed rows in list order.</summary>
    sealed class Pools
    {
        readonly MakerLists lists;
        readonly Dictionary<(int, int), List<string>> pools = new Dictionary<(int, int), List<string>>();
        readonly HashSet<string> preferred;
        public Pools(ContentCatalog cat, IEnumerable<CharaSubtree> targets)
        {
            lists = MakerCatalog.For(cat);
            preferred = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in targets)
                foreach (var tok in Json(t.Chara).Descendants().Concat(Json(t.Coord).Descendants()))
                    if (tok is JValue v && v.Type == JTokenType.String && JsonText.IsRef((string)v)) preferred.Add((string)v);
        }

        public List<string> Of(int category, int sex)
        {
            if (pools.TryGetValue((category, sex), out var l)) return l;
            var rows = lists.Rows(category, sex).Select(r => r.Key.ToString()).ToList();
            l = rows.Where(preferred.Contains).Concat(rows.Where(r => !preferred.Contains(r))).Take(40).ToList();
            pools[(category, sex)] = l;
            return l;
        }

        public List<string> Skins(int sex, string head)
        {
            if (!ContentId.TryParse(head, out var h)) return new List<string>();
            return lists.Rows(sex == 0 ? 111 : 211, sex, ListContext.ForHead(h)).Select(r => r.Key.ToString()).Take(40).ToList();
        }
    }

    static readonly int[] AccessoryCategories = { 351, 352, 353, 354, 355, 356, 357, 358, 359, 360, 361, 362 };
    static readonly string[] HairParts = { "back", "front", "side", "option" };

    // ================================================================ random edits (F, L, T)

    sealed class Editor
    {
        public readonly Node N;
        public readonly System.Random Rng;
        public readonly Pools Pools;
        public readonly int Sex;
        public readonly List<string> Log = new List<string>();
        public int Swaps, Sets, Cancelled, Refused;

        public Editor(Node n, int seed, Pools pools)
        {
            N = n; Rng = new System.Random(seed); Pools = pools;
            Sex = n.Stored.Chara.Profile.Sex;
        }

        T Pick<T>(IList<T> l) => l[Rng.Next(l.Count)];
        float F(float lo, float hi) => (float)(lo + Rng.NextDouble() * (hi - lo));
        ExecuteOptions O(string label = null) => new ExecuteOptions { Source = CommandSource.User, Label = label };
        string Id => N.Id;
        List<AccessoryEntry> Accs => N.Stored?.Coord?.Accessories ?? new List<AccessoryEntry>();

        /// <summary>One part swap (head, hair part, clothes kind, accessory item); returns the op.</summary>
        public OpResult Swap()
        {
            int k = Rng.Next(10);
            Swaps++;
            if (k == 0) return Item("appearance.chara.face.head", Pools.Of(Sex == 0 ? 110 : 210, Sex));
            if (k <= 3) { string p = Pick(HairParts); return Item("appearance.chara.hair." + p + ".item", Pools.Of(300 + Array.IndexOf(HairParts, p), Sex)); }
            if (k <= 6)
            {
                int kind = Rng.Next(8);
                int cat = (Sex == 0 ? ClothesSet.MaleCategories : ClothesSet.FemaleCategories)[kind];
                var pool = Pools.Of(cat, Sex);
                if (pool.Count == 0) return OpResult.NothingToDo();
                return Item("appearance.coord.clothes." + ClothesSet.Names[kind] + ".item", pool);
            }
            var accs = Accs;
            if (accs.Count == 0) return AddAccessory();
            var a = Pick(accs);
            var ap = Pools.Of(a.Category, Sex);
            if (ap.Count == 0) return OpResult.NothingToDo();
            return Item(AppearancePaths.Accessory(a.Id, "item"), ap);
        }

        OpResult Item(string path, List<string> pool)
        {
            if (pool.Count == 0) return OpResult.NothingToDo();
            string r = Pick(pool);
            Log.Add("item " + path + " = " + r);
            return N.Ops.ChangeItem(new[] { Id }, path, r, O());
        }

        OpResult AddAccessory()
        {
            int cat = Pick(AccessoryCategories);
            var pool = Pools.Of(cat, Sex);
            if (pool.Count == 0) return OpResult.NothingToDo();
            string r = Pick(pool);
            Log.Add("add accessory " + cat + " " + r);
            return N.Ops.AddAccessory(Id, cat, r, null, null, O());
        }

        /// <summary>One random Maker edit of any kind (F).</summary>
        public OpResult Any()
        {
            int k = Rng.Next(20);
            if (k < 5) return Swap();
            if (k < 11)
            {
                Sets++;
                string p;
                object v;
                switch (Rng.Next(6))
                {
                    case 0: p = "appearance.chara.face.shape[" + Rng.Next(59) + "]"; v = F(-1.5f, 2.5f); break;
                    case 1: p = "appearance.chara.body.shape[" + Rng.Next(33) + "]"; v = F(-1.5f, 2.5f); break;
                    case 2: p = "appearance.chara.body.skin.color"; v = new Color4(F(0, 1), F(0, 1), F(0, 1), 1); break;
                    case 3: p = "appearance.chara.hair." + Pick(HairParts) + ".baseColor"; v = new Color4(F(0, 1), F(0, 1), F(0, 1), 1); break;
                    case 4: p = "appearance.coord.clothes." + ClothesSet.Names[Rng.Next(8)] + ".colors[" + Rng.Next(3) + "].base"; v = new Color4(F(0, 1), F(0, 1), F(0, 1), 1); break;
                    default:
                        var accs = Accs;
                        if (accs.Count == 0) return AddAccessory();
                        p = AppearancePaths.Accessory(Pick(accs).Id, "colors[" + Rng.Next(4) + "].color"); v = new Color4(F(0, 1), F(0, 1), F(0, 1), F(0, 1)); break;
                }
                Log.Add("set " + p);
                return N.Ops.EditAppearance(new[] { Id }, new List<(string, object)> { (p, v) }, O());
            }
            if (k < 13) return AddAccessory();
            var list = Accs;
            if (list.Count == 0) return AddAccessory();
            var a = Pick(list);
            if (k < 15) { Log.Add("remove accessory " + a.Id); return N.Ops.RemoveAccessory(Id, a.Id, O()); }
            if (k < 17) { int i = Rng.Next(list.Count); Log.Add("move accessory " + a.Id + " to " + i); return N.Ops.MoveAccessory(Id, a.Id, i, O()); }
            if (k < 18) { int cat = Pick(AccessoryCategories); Log.Add("accessory category " + a.Id + " " + cat); return N.Ops.ChangeItem(new[] { Id }, AppearancePaths.Accessory(a.Id, "category"), cat.ToString(CultureInfo.InvariantCulture), O()); }
            return Swap();
        }

        /// <summary>1–3 random edits in one transaction; one in ten is cancelled.</summary>
        public void Transaction(Func<OpResult> step)
        {
            int n = 1 + Rng.Next(3);
            bool cancel = Rng.Next(10) == 0;
            var tx = N.Session.Commands.BeginOrJoin("Fuzz", O("Fuzz"));
            try
            {
                for (int i = 0; i < n; i++)
                {
                    var r = step();
                    if (r != null && !r.Ok) Refused++;
                }
                if (cancel) { tx.Cancel(); Cancelled++; Log.Add("(cancelled)"); }
                else tx.Commit();
            }
            finally { tx.Dispose(); }
            N.Apply();
        }

        // ---- Phase 4.1 package A: drags through the preview / commit
        // seam and the face operations
        /// <summary>The heads of FT2 for this character's sex (head changes of the face fuzz).</summary>
        public List<string> Heads = new List<string>();
        /// <summary>Heads with a preset (MakerLists.PresetHeads), the first of the list order (preset pushes).</summary>
        public List<string> PresetHeads = new List<string>();
        public int Drags, DragsCancelled, FaceOps;
        bool sessionBegun;

        /// <summary>A drag: 1–5 preview sets of one member in one transaction, each flushed in Preview mode
        /// (CharaEditFlush.ForcePreview), then commit or (one in four) cancel, then the Normal flush.</summary>
        public void Drag()
        {
            Drags++;
            string p; Func<int, object> v;
            switch (Rng.Next(6))
            {
                case 0: p = "appearance.chara.face.shape[" + Rng.Next(59) + "]"; { float a = F(-1f, 2f); v = i => a + 0.05f * i; } break;
                case 1: p = "appearance.chara.body.shape[" + Rng.Next(33) + "]"; { float a = F(-1f, 2f); v = i => a - 0.05f * i; } break;
                case 2: p = "appearance.chara.face.makeup." + Pick(new[] { "eyeshadow", "cheek", "lip" }) + ".color"; { float a = F(0, 1); v = i => new Color4(a, 0.1f * i, 0.5f, 0.8f); } break;
                case 3: p = "appearance.chara.face.makeup.paint[" + Rng.Next(2) + "].gloss"; { float a = F(-0.5f, 1.5f); v = i => a + 0.1f * i; } break;
                case 4: p = "appearance.chara.face.eyes.left.iris.size"; { float a = F(0, 1); v = i => new Vec2(a, 1 - 0.1f * i); } break;
                default: p = "appearance.chara.body.skin.color"; { float a = F(0, 1); v = i => new Color4(a, 0.6f, 0.05f * i, 1); } break;
            }
            int n = 1 + Rng.Next(5);
            bool cancel = Rng.Next(4) == 0;
            Log.Add($"drag {p} × {n}{(cancel ? " (cancelled)" : "")}");
            bool keep = CharaEditFlush.ForcePreview;
            CharaEditFlush.ForcePreview = true;
            try
            {
                var tx = N.Session.Commands.BeginOrJoin("Drag", O("Drag"));
                try
                {
                    for (int i = 0; i < n; i++)
                    {
                        var r = N.Ops.EditAppearance(new[] { Id }, new List<(string, object)> { (p, v(i)) }, O());
                        if (r != null && !r.Ok) Refused++;
                        N.Apply();
                    }
                    if (cancel) { tx.Cancel(); DragsCancelled++; } else tx.Commit();
                }
                finally { tx.Dispose(); }
            }
            finally { CharaEditFlush.ForcePreview = keep; }
            N.Apply();
        }

        /// <summary>One face operation: a head change (FT2's heads), a preset push (incl. the missing-asset fallback), a layout
        /// push, an eye copy, a row reset to the reference, a face member set (materials, makeup, eyes with the link).</summary>
        public OpResult AnyFace()
        {
            FaceOps++;
            int k = Rng.Next(10);
            if (k < 2 && Heads.Count > 0) return Item("appearance.chara.face.head", Heads);
            if (k < 3 && PresetHeads.Count > 0) { string h = Pick(PresetHeads); Log.Add("preset " + h); return N.Ops.ApplyFacePreset(new[] { Id }, h, O()); }
            if (k == 3)
            {
                int pk = Rng.Next(3);
                string lp = pk == 0 ? "appearance.chara.face.mole.layout" : "appearance.chara.face.makeup.paint[" + (pk - 1) + "].layout";
                string row = "illusion.hs2#" + Rng.Next(pk == 0 ? 8 : 6);
                Log.Add("layout " + lp + " " + row);
                return N.Ops.ApplyLayoutPreset(new[] { Id }, lp, row, O());
            }
            if (k == 4) { bool lr = Rng.Next(2) == 0; Log.Add("copy eye " + (lr ? "L->R" : "R->L")); return N.Ops.CopyEye(new[] { Id }, lr, O()); }
            if (k == 5)
            {
                if (!sessionBegun) { N.Ops.BeginEditSession(Id); sessionBegun = true; }   // the reference documents
                var paths = new[] { "appearance.chara.face.eyebrow.tilt", "appearance.chara.face.mole.layout#" + Rng.Next(4), "appearance.chara.face.eyes.left.pupil.size",
                                    "appearance.chara.face.makeup.lip.gloss", "appearance.chara.face.shape[" + Rng.Next(59) + "]", "appearance.chara.face.makeup.paint[1].layout#0" };
                var pick = paths.Where(_ => Rng.Next(2) == 0).ToList();
                if (pick.Count == 0) pick.Add(paths[0]);
                Log.Add("reset " + string.Join(",", pick));
                return N.Ops.ResetAppearance(new[] { Id }, pick, null, O());
            }
            string p; object val;
            switch (Rng.Next(8))
            {
                case 0: p = "appearance.chara.face.eyebrow.layout"; val = new Vec4(F(-0.5f, 1.5f), F(0, 1), F(0, 1), F(0, 1)); break;
                case 1: p = "appearance.chara.face.eyes." + Pick(new[] { "left", "right" }) + ".pupil.size"; val = new Vec2(F(0, 1), F(-0.5f, 1.5f)); break;
                case 2: p = "appearance.chara.face.eyes.linked"; val = Rng.Next(2) == 0; break;
                case 3: p = "appearance.chara.face.eyes.highlight.layout"; val = new Vec4(F(0, 1), F(0, 1), F(0, 1), F(0, 1)); break;
                case 4: p = "appearance.chara.face.makeup.lip.gloss"; val = F(-0.5f, 1.8f); break;
                case 5: p = "appearance.chara.face.mole.layout"; val = new Vec4(F(0, 1), F(0, 1), F(0, 1), F(0, 1)); break;
                case 6: p = "appearance.chara.face.makeup.paint[" + Rng.Next(2) + "].layout"; val = new Vec4(F(-0.2f, 1.2f), F(0, 1), F(0, 1), F(0, 1)); break;
                default: p = "appearance.chara.face.detail.power"; val = F(0, 1.5f); break;
            }
            Log.Add("face set " + p);
            return N.Ops.EditAppearance(new[] { Id }, new List<(string, object)> { (p, val) }, O());
        }

        // ---- Phase 4.2 package A: body drags through the preview / commit
        // seam, skin swatch pushes, paint region picks, bust-size sets crossing 0.5 (the breast normals), bust values
        public int BodyDrags, BodyOps;

        /// <summary>A body drag: 1–5 preview sets of a body shape, the skin colour, a paint layout or a paint colour in one
        /// transaction (CharaEditFlush.ForcePreview), then commit or (one in four) cancel.</summary>
        public void BodyDrag()
        {
            BodyDrags++;
            string p; Func<int, object> v;
            int pk = Rng.Next(2);
            switch (Rng.Next(5))
            {
                case 0: p = "appearance.chara.body.shape[" + Rng.Next(33) + "]"; { float a = F(-1f, 2f); v = i => a + 0.04f * i; } break;
                case 1: p = "appearance.chara.body.shape[1]"; { float a = F(0.2f, 0.7f); v = i => a + 0.03f * i; } break;   // crosses 0.5 at times
                case 2: p = "appearance.chara.body.skin.color"; { float a = F(0, 1); v = i => new Color4(a, 0.6f, 0.05f * i, 1); } break;
                case 3: p = "appearance.chara.body.paint[" + pk + "].layout"; { float a = F(-0.2f, 1.2f); v = i => new Vec4(a, 0.5f + 0.02f * i, F(0, 1), 0.5f); } break;
                default: p = "appearance.chara.body.paint[" + pk + "].color"; { float a = F(0, 1); v = i => new Color4(a, 0.1f * i, 0.5f, 0.8f); } break;
            }
            int n = 1 + Rng.Next(5);
            bool cancel = Rng.Next(4) == 0;
            Log.Add($"body drag {p} × {n}{(cancel ? " (cancelled)" : "")}");
            bool keep = CharaEditFlush.ForcePreview;
            CharaEditFlush.ForcePreview = true;
            try
            {
                var tx = N.Session.Commands.BeginOrJoin("Drag", O("Drag"));
                try
                {
                    for (int i = 0; i < n; i++)
                    {
                        var r = N.Ops.EditAppearance(new[] { Id }, new List<(string, object)> { (p, v(i)) }, O());
                        if (r != null && !r.Ok) Refused++;
                        N.Apply();
                    }
                    if (cancel) { tx.Cancel(); DragsCancelled++; } else tx.Commit();
                }
                finally { tx.Dispose(); }
            }
            finally { CharaEditFlush.ForcePreview = keep; }
            N.Apply();
        }

        /// <summary>One body operation: a skin swatch push (rows 0..6 of 336), a paint region pick (rows of category 8, the mod
        /// region 20 included when listed), a bust-size set (0.2..0.8: the breast-normal rate crosses its knee at 0.5), a bust
        /// softness / weight set (−0.5..1.5), a body material set.</summary>
        public OpResult AnyBody()
        {
            BodyOps++;
            int k = Rng.Next(8);
            if (k < 2) { string row = "illusion.hs2#" + Rng.Next(7); Log.Add("swatch " + row); return N.Ops.ApplyColourPreset(new[] { Id }, "appearance.chara.body.skin.color", row, O()); }
            if (k < 4)
            {
                var regions = Pools.Of(8, Sex);
                if (regions.Count == 0) return OpResult.NothingToDo();
                return Item("appearance.chara.body.paint[" + Rng.Next(2) + "].layoutItem", regions);
            }
            string p; object val;
            switch (k)
            {
                case 4: p = "appearance.chara.body.shape[1]"; val = F(0.2f, 0.8f); break;
                case 5: p = "appearance.chara.body.bust." + (Rng.Next(2) == 0 ? "softness" : "weight"); val = F(-0.5f, 1.5f); break;
                case 6: p = "appearance.chara.body.paint[" + Rng.Next(2) + "].rotation"; val = F(-0.5f, 1.5f); break;
                default: p = "appearance.chara.body.nipple.areola"; val = F(0, 1.2f); break;
            }
            Log.Add("body set " + p);
            return N.Ops.EditAppearance(new[] { Id }, new List<(string, object)> { (p, val) }, O());
        }

        // ---- Phase 4.3 package A: hair drags through the preview / commit
        // seam (colour, bundle rates, mesh layout), the 305 presets, skin matches, hair item swaps, noShake, bundle resets, the
        // edit-together switch, mesh patterns and the draw type
        public int HairDrags, HairOps;

        /// <summary>The bundle count of part <paramref name="k"/>'s realized prefab (0 without facts).</summary>
        int Bundles(int k) => HairInfo.TryPart(N.Runtime, Id, k, out var f) ? f.Bundles.Length : 0;

        /// <summary>A hair drag: 1–5 preview sets of a hair colour, a bundle's move / rotate (StudioOps.EditHairBundle) or a mesh
        /// layout in one transaction (CharaEditFlush.ForcePreview), then commit or (one in four) cancel.</summary>
        public void HairDrag()
        {
            HairDrags++;
            int k = Rng.Next(4), kind = Rng.Next(3), n = 1 + Rng.Next(5), nb = Bundles(k);
            if (kind == 1 && nb == 0) kind = 0;
            int b = nb > 0 ? Rng.Next(nb) : 0;
            bool rot = Rng.Next(2) == 0, cancel = Rng.Next(4) == 0;
            float a = F(-0.2f, 1.2f), c = F(0, 1);
            Log.Add($"hair drag {HairParts[k]} {(kind == 0 ? "baseColor" : kind == 1 ? "bundle " + b + (rot ? " rotate" : " move") : "mesh.layout")} × {n}{(cancel ? " (cancelled)" : "")}");
            bool keep = CharaEditFlush.ForcePreview;
            CharaEditFlush.ForcePreview = true;
            try
            {
                var tx = N.Session.Commands.BeginOrJoin("Drag", O("Drag"));
                try
                {
                    for (int i = 0; i < n; i++)
                    {
                        OpResult r;
                        if (kind == 0) r = N.Ops.EditAppearance(new[] { Id }, new List<(string, object)> { ("appearance.chara.hair." + HairParts[k] + ".baseColor", new Color4(c, 0.05f * i, a, 1)) }, O());
                        else if (kind == 1)
                        {
                            var v = new Vec3(a, c + 0.04f * i, 0.5f);
                            r = rot ? N.Ops.EditHairBundle(new[] { Id }, k, b, null, v, null, false, -1, O()) : N.Ops.EditHairBundle(new[] { Id }, k, b, v, null, null, false, -1, O());
                        }
                        else r = N.Ops.EditAppearance(new[] { Id }, new List<(string, object)> { ("appearance.chara.hair." + HairParts[k] + ".mesh.layout", new Vec4(a, c, 0.5f + 0.03f * i, 0.2f)) }, O());
                        if (r != null && !r.Ok) Refused++;
                        N.Apply();
                    }
                    if (cancel) { tx.Cancel(); DragsCancelled++; } else tx.Commit();
                }
                finally { tx.Dispose(); }
            }
            finally { CharaEditFlush.ForcePreview = keep; }
            N.Apply();
        }

        /// <summary>One hair operation: a 305 preset push, a skin match, a hair item swap, a noShake toggle, a bundle component
        /// reset or «全て初期化», the draw type, a mesh pattern pick, the edit-together switch.</summary>
        public OpResult AnyHair()
        {
            HairOps++;
            int k = Rng.Next(4), nb = Bundles(k);
            string part = HairParts[k];
            switch (Rng.Next(9))
            {
                case 0: { string row = "illusion.hs2#" + Rng.Next(7); Log.Add($"hair preset {part} {row}"); return N.Ops.ApplyColourPreset(new[] { Id }, "appearance.chara.hair." + part + ".baseColor", row, O()); }
                case 1: Log.Add("hair skin match " + part); return N.Ops.MatchHairToSkin(new[] { Id }, k, O());
                case 2: return Item("appearance.chara.hair." + part + ".item", Pools.Of(300 + k, Sex));
                case 3:
                    if (nb == 0) goto case 2;
                    { int b = Rng.Next(nb); bool ns = Rng.Next(2) == 0; Log.Add($"hair noShake {part} {b} {ns}"); return N.Ops.EditHairBundle(new[] { Id }, k, b, null, null, ns, false, -1, O()); }
                case 4:
                    if (nb == 0) goto case 2;
                    { int b = Rng.Next(nb), axis = Rng.Next(4) - 1; Log.Add($"hair bundle reset {part} {b} axis {axis}"); return N.Ops.EditHairBundle(new[] { Id }, k, b, null, null, null, true, axis, O()); }
                case 5:
                    if (nb == 0) goto case 2;
                    Log.Add("hair reset all " + part); return N.Ops.ResetHairBundles(new[] { Id }, k, O());
                case 6: { int st = Rng.Next(2); Log.Add("hair draw type " + st); return N.Ops.EditAppearance(new[] { Id }, new List<(string, object)> { ("appearance.chara.hair.shaderType", st) }, O()); }
                case 7: { string row = "illusion.hs2#" + Rng.Next(12); Log.Add($"hair mesh {part} {row}"); return N.Ops.ChangeItem(new[] { Id }, "appearance.chara.hair." + part + ".mesh.item", row, O()); }
                default: { bool on = Rng.Next(2) == 0; Log.Add("hair together " + on); return N.Ops.EditAppearance(new[] { Id }, new List<(string, object)> { ("appearance.chara.hair.editor.ctrlTogether", on) }, O()); }
            }
        }

        // ---- Phase 4.4 package A: clothes drags through the preview /
        // commit seam (main colour, gloss, pattern layout) and clothes operations (item swaps, pattern picks, option toggles,
        // break rates, two members of a set in one entry)
        public int ClothesDrags, ClothesOps;

        /// <summary>A clothes kind of the character's page (males: top, bottom, gloves, shoes).</summary>
        int ClothesKind()
        {
            var kinds = Sex == 0 ? new[] { 0, 1, 4, 7 } : new[] { 0, 1, 2, 3, 4, 5, 6, 7 };
            return kinds[Rng.Next(kinds.Length)];
        }

        static string ClothesPath(int k, string member) => "appearance.coord.clothes." + ClothesSet.Names[k] + "." + member;

        /// <summary>A clothes drag: 1–5 preview sets of a set's main colour, gloss or pattern layout in one transaction
        /// (CharaEditFlush.ForcePreview), then commit or (one in four) cancel.</summary>
        public void ClothesDrag()
        {
            ClothesDrags++;
            int k = ClothesKind(), set = Rng.Next(3), what = Rng.Next(3), n = 1 + Rng.Next(5);
            bool cancel = Rng.Next(4) == 0;
            float a = F(0, 1), b = F(-0.2f, 1.2f);
            string member = what == 0 ? "base" : what == 1 ? "gloss" : "layout";
            Log.Add($"clothes drag {ClothesSet.Names[k]} colors[{set}].{member} × {n}{(cancel ? " (cancelled)" : "")}");
            bool keep = CharaEditFlush.ForcePreview;
            CharaEditFlush.ForcePreview = true;
            try
            {
                var tx = N.Session.Commands.BeginOrJoin("Drag", O("Drag"));
                try
                {
                    for (int i = 0; i < n; i++)
                    {
                        object v = what == 0 ? new Color4(a, 0.05f * i, 1 - a, 1) : what == 1 ? (object)(b + 0.03f * i) : new Vec4(a, b, 0.5f + 0.03f * i, 0.2f);
                        var r = N.Ops.EditAppearance(new[] { Id }, new List<(string, object)> { (ClothesPath(k, "colors[" + set + "]." + member), v) }, O());
                        if (r != null && !r.Ok) Refused++;
                        N.Apply();
                    }
                    if (cancel) { tx.Cancel(); DragsCancelled++; } else tx.Commit();
                }
                finally { tx.Dispose(); }
            }
            finally { CharaEditFlush.ForcePreview = keep; }
            N.Apply();
        }

        // ---- Phase 4.5 package A: accessory drags and operations
        public int AccessoryDrags, AccessoryOps;
        static readonly string[] HairTypeAccessories = { "illusion.hs2#40", "illusion.hs2#41", "illusion.hs2#44", "illusion.hs2#47" };

        /// <summary>An accessory drag: 1–5 preview sets of a worn accessory's colour, gloss or one transform component in one
        /// transaction (CharaEditFlush.ForcePreview), then commit or (one in four) cancel.</summary>
        public void AccessoryDrag()
        {
            var accs = Accs;
            if (accs.Count == 0) { Transaction(AddAccessory); return; }
            AccessoryDrags++;
            var a = Pick(accs);
            int what = Rng.Next(3), c = Rng.Next(4), set = Rng.Next(2), comp = Rng.Next(3), n = 1 + Rng.Next(5);
            bool cancel = Rng.Next(4) == 0;
            float x = F(0, 1);
            string member = what == 0 ? "colors[" + c + "].color" : what == 1 ? "colors[" + c + "].gloss" : "moves[" + set + "]." + new[] { "position", "rotation", "scale" }[comp];
            Log.Add($"accessory drag {a.Id} {member} × {n}{(cancel ? " (cancelled)" : "")}");
            bool keep = CharaEditFlush.ForcePreview;
            CharaEditFlush.ForcePreview = true;
            try
            {
                var tx = N.Session.Commands.BeginOrJoin("Drag", O("Drag"));
                try
                {
                    for (int i = 0; i < n; i++)
                    {
                        object v = what == 0 ? new Color4(x, 0.05f * i, 1 - x, 1) : what == 1 ? (object)(x + 0.03f * i)
                                 : comp == 2 ? new Vec3(1 + 0.1f * i, 1, 1) : new Vec3(10 * x + i, 0, 0);
                        var r = N.Ops.EditAppearance(new[] { Id }, new List<(string, object)> { (AppearancePaths.Accessory(a.Id, member), v) }, O());
                        if (r != null && !r.Ok) Refused++;
                        N.Apply();
                    }
                    if (cancel) { tx.Cancel(); DragsCancelled++; } else tx.Commit();
                }
                finally { tx.Dispose(); }
            }
            finally { CharaEditFlush.ForcePreview = keep; }
            N.Apply();
        }

        /// <summary>One accessory operation: an add to a chosen free slot, a hair-type item, a parent of the 54, «初期親», a copy
        /// (duplicate, to a slot, a set with a mirror), «髪の毛の色を取得», «初期カラー», a draw-type switch with the hair-type
        /// accessories, a move to a free slot.</summary>
        public OpResult AnyAccessory()
        {
            AccessoryOps++;
            var accs = Accs;
            if (accs.Count == 0) return AddAccessory();
            var a = Pick(accs);
            var slots = StudioOps.AccessorySlotsOf(N.Session.Model.Find(Id));
            switch (Rng.Next(9))
            {
                case 0:
                    {
                        int cat = Pick(AccessoryCategories);
                        var pool = Pools.Of(cat, Sex);
                        if (pool.Count == 0 || slots == null) return OpResult.NothingToDo();
                        var free = slots.FreeSlots();
                        int slot = free[Rng.Next(Math.Min(free.Count, 24))];
                        string r = Pick(pool);
                        Log.Add($"add accessory {cat} {r} at slot {slot}");
                        return N.Ops.AddAccessory(Id, cat, r, null, null, O(), slot);
                    }
                case 1: { string r = Pick(HairTypeAccessories); Log.Add($"accessory hair-type item {a.Id} {r}"); return N.Ops.ChangeItem(new[] { Id }, AppearancePaths.Accessory(a.Id, a.Category == 351 ? "item" : "category"), a.Category == 351 ? r : "351", O()); }
                case 2:
                    {
                        var keys = AttachPointTable.Vanilla.Groups.SelectMany(g => g.Points).ToList();
                        string k = Pick(keys);
                        Log.Add($"accessory parent {a.Id} {k}");
                        return N.Ops.EditAppearance(new[] { Id }, new List<(string, object)> { (AppearancePaths.Accessory(a.Id, "parent"), k) }, O());
                    }
                case 3: Log.Add($"accessory default parent {a.Id}"); return N.Ops.ResetAccessoryParent(Id, a.Id, O());
                case 4:
                    {
                        int form = Rng.Next(3);
                        var parts = form == 2 ? (Rng.Next(2) == 0 ? AccessoryCopyParts.Move1 : AccessoryCopyParts.Move2) : AccessoryCopyParts.Slot;
                        int? to = null;
                        if (form >= 1 && slots != null)
                        {
                            var worn = slots.EntryBySlot.Keys.ToList();
                            var free = slots.FreeSlots();
                            to = form == 2 || Rng.Next(2) == 0 ? worn[Rng.Next(worn.Count)] : free[Rng.Next(Math.Min(free.Count, 24))];
                        }
                        bool mp = Rng.Next(2) == 0;
                        var mirror = (MirrorAxis)Rng.Next(3);
                        Log.Add($"copy accessory {a.Id} to {(to.HasValue ? "slot " + to.Value : "the lowest free slot")} {parts} mirrorParent {mp} {mirror}");
                        return N.Ops.CopyAccessory(Id, a.Id, to, null, parts, mp, mirror, O());
                    }
                case 5: { int part = Rng.Next(4); Log.Add($"accessory hair colour {a.Id} part {part}"); return N.Ops.MatchAccessoryHairColour(Id, a.Id, part, O()); }
                case 6: Log.Add($"accessory default colours {a.Id}"); return N.Ops.ResetAccessoryColours(Id, a.Id, O());
                case 7:
                    {
                        int st = N.Stored.Chara.Hair.ShaderType == 0 ? 1 : 0;
                        Log.Add($"draw type {st} (accessories)");
                        return N.Ops.EditAppearance(new[] { Id }, new List<(string, object)> { ("appearance.chara.hair.shaderType", st) }, O());
                    }
                default:
                    {
                        if (slots == null) return OpResult.NothingToDo();
                        var free = slots.FreeSlots();
                        int slot = free[Rng.Next(Math.Min(free.Count, 24))];
                        Log.Add($"move accessory {a.Id} to slot {slot}");
                        return N.Ops.MoveAccessoryToSlot(Id, a.Id, slot, O());
                    }
            }
        }

        /// <summary>The SFW coordinate cards of the character's sex (vanilla items), read once per sex.</summary>
        static readonly Dictionary<int, List<CoordContainer>> coordinates = new Dictionary<int, List<CoordContainer>>();

        List<CoordContainer> Coordinates()
        {
            if (coordinates.TryGetValue(Sex, out var l)) return l;
            l = new List<CoordContainer>();
            string folder = Path.Combine(TestTargets.UserData, "coordinate", Sex == 0 ? "male" : "female");
            foreach (var name in Sex == 0 ? new[] { "ロングコート.png", "作業着.png" } : new[] { "ガーリーブラウス.png", "ジャージ.png" })
            {
                string f = Path.Combine(folder, name);
                if (!File.Exists(f)) continue;
                var cc = N.Ops.LoadCoordinate(f, null, out _, out _);
                if (cc != null) l.Add(cc);
            }
            coordinates[Sex] = l;
            return l;
        }

        /// <summary>One clothes operation: an item swap, a pattern pick, an option-part toggle, a break rate, metallic and
        /// rotation of a set in one entry, a pattern colour, «初期カラー», a member's prefab-default reset, a coordinate apply
        /// (one of the three modes).</summary>
        public OpResult AnyClothes()
        {
            ClothesOps++;
            int k = ClothesKind(), set = Rng.Next(3);
            var cats = Sex == 0 ? ClothesSet.MaleCategories : ClothesSet.FemaleCategories;
            string kn = ClothesSet.Names[k];
            switch (Rng.Next(9))
            {
                case 6: Log.Add($"clothes default colours {kn}"); return N.Ops.ResetClothesColours(new[] { Id }, k, O());
                case 7:
                    {
                        string m = new[] { "gloss", "metallic", "layout", "rotation" }[Rng.Next(4)];
                        Log.Add($"clothes reset {kn} {set} {m}");
                        return N.Ops.ResetAppearance(new[] { Id }, new[] { ClothesPath(k, "colors[" + set + "]." + m) }, null, O());
                    }
                case 8:
                    {
                        var list = Coordinates();
                        if (list.Count == 0) goto default;
                        var cc = list[Rng.Next(list.Count)];
                        var mode = (CoordinateApplyMode)Rng.Next(3);
                        Log.Add($"coordinate {cc.Coord.Name} {mode}");
                        return N.Ops.ApplyCoordinate(new[] { Id }, cc, mode, O());
                    }
                case 0: return Item(ClothesPath(k, "item"), Pools.Of(cats[k], Sex));
                case 1: { string row = "illusion.hs2#" + Rng.Next(20); Log.Add($"clothes pattern {kn} {set} {row}"); return N.Ops.ChangeItem(new[] { Id }, ClothesPath(k, "colors[" + set + "].pattern"), row, O()); }
                case 2: { int o = Rng.Next(2); bool hide = Rng.Next(2) == 0; Log.Add($"clothes option {kn} {o} hide {hide}"); return N.Ops.EditAppearance(new[] { Id }, new List<(string, object)> { (ClothesPath(k, "hideOptions[" + o + "]"), hide) }, O()); }
                case 3: { float br = F(0, 1); Log.Add($"clothes break {kn} {br:0.##}"); return N.Ops.EditAppearance(new[] { Id }, new List<(string, object)> { (ClothesPath(k, "breakRate"), br) }, O()); }
                case 4:
                    Log.Add($"clothes metallic + rotation {kn} {set}");
                    return N.Ops.EditAppearance(new[] { Id }, new List<(string, object)> { (ClothesPath(k, "colors[" + set + "].metallic"), F(-0.5f, 1.5f)), (ClothesPath(k, "colors[" + set + "].rotation"), F(-0.5f, 1.5f)) }, O());
                default: { Log.Add($"clothes pattern colour {kn} {set}"); return N.Ops.EditAppearance(new[] { Id }, new List<(string, object)> { (ClothesPath(k, "colors[" + set + "].patternColor"), new Color4(F(0, 1), F(0, 1), F(0, 1), F(0, 1))) }, O()); }
            }
        }
    }

    /// <summary>FT2's heads for a sex (vanilla 0, 2, 3, a W10 head, the mod head with the most skins, 1; males: the first six
    /// listed heads) and the preset heads (list order, first 20).</summary>
    static void FaceFuzzLists(Editor ed, MakerLists lists)
    {
        int sex = ed.Sex == 0 ? 0 : 1;
        if (sex == 1)
        {
            ed.Heads.AddRange(new[] { "illusion.hs2#0", "illusion.hs2#2", "illusion.hs2#3" });
            foreach (var w in new[] { "chloe#24", "cunihinx.akamegdn#482", "cunihinx.akanelj#518" })
                if (ContentId.TryParse(w, out var wid) && lists.IsListed(210, 1, wid)) { ed.Heads.Add(w); break; }
            string best = null; int most = -1;
            foreach (var row in lists.Rows(210, 1))
                if (!row.IsVanilla) { int c = lists.Rows(211, 1, ListContext.ForHead(row.Key)).Count; if (c > most) { most = c; best = row.Key.ToString(); } }
            if (best != null) ed.Heads.Add(best);
            ed.Heads.Add("illusion.hs2#1");
        }
        else ed.Heads.AddRange(lists.Rows(110, 0).Take(6).Select(r => r.Key.ToString()));
        ed.PresetHeads.AddRange(lists.PresetHeads(sex).Take(20).Select(r => r.Key.ToString()));
        // heads whose preset asset is missing (the head-change fallback, F5)
        foreach (var m in new[] { "cunihinx.ada4#424", "Darkruler.ElenaFisher#166" })
            if (sex == 1 && ContentId.TryParse(m, out var mid) && lists.IsListed(210, 1, mid)) ed.PresetHeads.Add(m);
    }

    // ================================================================ F (realization)

    public static void RunFuzz()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        bool full = Flag("-full");
        int seeds = int.Parse(Arg("-seeds", full ? "200" : "40"), CultureInfo.InvariantCulture);
        int ops = int.Parse(Arg("-ops", full ? "60" : "40"), CultureInfo.InvariantCulture);
        rep.AppendLine($"maker edit fuzz with the realization (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}): BR-Chan and HERO107, {seeds} seeds × {ops} operations");
        int? fseed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        int checks = 0, equal = 0, differ = 0, failed = 0, undoDocs = 0;
        long totalOps = 0, swaps = 0, cancelled = 0, refused = 0, drags = 0, dragsCancelled = 0, faceOps = 0, bodyDrags = 0, bodyOps = 0, hairDrags = 0, hairOps = 0, clothesDrags = 0, clothesOps = 0, accDrags = 0, accOps = 0;
        var diffSections = new SortedDictionary<string, int>(StringComparer.Ordinal);
        try
        {
            using (var h = MakerCoreTest.Harness.Open())
            {
                var targets = new List<(string rel, CharaContainer c)>();
                foreach (var f in TestTargets.Load().VisualCards.Where(File.Exists))
                {
                    var ct = Import(h, f, out string err);
                    if (ct != null) targets.Add((RelOf(f), ct));
                    else Check(RelOf(f) + ": imported", false, err);
                }
                var pools = new Pools(h.Catalog, targets.Select(t => t.c.Subtree));
                for (int s = 0; s < seeds; s++)
                {
                    var (rel, c) = targets[s % targets.Count];
                    var n = NewNode(h, c, rel);
                    try
                    {
                        if (!n.Ok) { failed++; Check($"seed {s}: {rel} built", false, n.Error); continue; }
                        var ed = new Editor(n, 1000 + s, pools);
                        FaceFuzzLists(ed, MakerCatalog.For(h.Catalog));   // Phase 4.1: drags and face operations
                        int base0 = n.Session.Commands.UndoList.Count;   // "Add character" stays
                        var initDocs = DocBytes(n.Stored);
                        var initSnap = n.Snapshot();
                        void Compare(string when)
                        {
                            checks++;
                            var sm = n.Snapshot();
                            using (var fresh = FreshOf(h, n, rel + " (fresh)"))
                            {
                                if (!fresh.Ok) { failed++; Check($"seed {s} {when}: fresh node built", false, fresh.Error); return; }
                                var d = CharaSnapshot.Compare(fresh.Snapshot(), sm, 12);
                                // Phase 4.2 (package A): the drawn body normals (breast normals, not in the snapshot) too
                                string nl = FaceDigest.NormalsText(n.Real.Context, "body"), nf = FaceDigest.NormalsText(fresh.Real.Context, "body");
                                // Phase 4.3 (package A): the hair chains' captured rest poses (not in the snapshot) too
                                string hl = FaceDigest.ChainsText(n.Real.Context, "hair"), hf = FaceDigest.ChainsText(fresh.Real.Context, "hair");
                                // Phase 4.4 (package A): the clothes digest (materials, creators, objects, clothes chains) too
                                string cl = MakerClothesTest.ClothesDiff(fresh.Real.Context, n.Real.Context);
                                // Phase 4.5 (package A): the accessory digest (materials, objects, accessory chains) too
                                string al = MakerAccessoriesTest.AccDiff(fresh.Real.Context, n.Real.Context);
                                if (d.Equal && nl == nf && hl == hf && cl == null && al == null) equal++;
                                else
                                {
                                    differ++;
                                    foreach (var kv in d.Counts) diffSections[kv.Key] = (diffSections.TryGetValue(kv.Key, out var k) ? k : 0) + kv.Value;
                                    if (nl != nf) diffSections["normals"] = (diffSections.TryGetValue("normals", out var kn) ? kn : 0) + 1;
                                    if (hl != hf) diffSections["hair chains"] = (diffSections.TryGetValue("hair chains", out var kh) ? kh : 0) + 1;
                                    if (cl != null) diffSections["clothes digest"] = (diffSections.TryGetValue("clothes digest", out var kc) ? kc : 0) + 1;
                                    if (al != null) diffSections["accessory digest"] = (diffSections.TryGetValue("accessory digest", out var ka) ? ka : 0) + 1;
                                    Check($"seed {s} ({rel}) {when}: realized = fresh build of the documents", false, "last operations: " + string.Join("; ", ed.Log.Skip(Math.Max(0, ed.Log.Count - 6))) + "; fresh vs edited: " + d + (nl != nf ? "; body normals differ" : "") + (hl != hf ? "; hair chains differ" : "") + (cl != null ? "; clothes digest (fresh | edited): " + cl : "") + (al != null ? "; accessory digest (fresh | edited): " + al : "")
                                          + (MakerHairTest.HairMaterialsDiff(fresh.Real.Context, n.Real.Context) is string hm ? "; hair materials (fresh | edited): " + hm : ""));
                                }
                            }
                        }
                        int done = 0;
                        while (done < ops)
                        {
                            int kind = ed.Rng.Next(18);
                            if (kind < 2) ed.Drag();
                            else if (kind < 4) ed.Transaction(ed.AnyFace);
                            else if (kind == 10) ed.BodyDrag();              // Phase 4.2: body drags
                            else if (kind == 11) ed.Transaction(ed.AnyBody); // Phase 4.2: swatches, regions, bust sizes
                            else if (kind == 12) ed.HairDrag();              // Phase 4.3: hair drags (colour, bundles, mesh layout)
                            else if (kind == 13) ed.Transaction(ed.AnyHair); // Phase 4.3: presets, skin match, swaps, noShake, resets
                            else if (kind == 14) ed.ClothesDrag();           // Phase 4.4: clothes drags (colour, gloss, layout)
                            else if (kind == 15) ed.Transaction(ed.AnyClothes); // Phase 4.4: swaps, patterns, options, break
                            else if (kind == 16) ed.AccessoryDrag();         // Phase 4.5: accessory drags (colour, gloss, a transform component)
                            else if (kind == 17) ed.Transaction(ed.AnyAccessory); // Phase 4.5: slot adds, hair-type items, parents, copies, hair colours, draw types
                            else ed.Transaction(ed.Any);
                            done++;
                            if (done % 10 == 0) Compare("after " + done + " operations");
                        }
                        totalOps += done; swaps += ed.Swaps; cancelled += ed.Cancelled; refused += ed.Refused;
                        drags += ed.Drags; dragsCancelled += ed.DragsCancelled; faceOps += ed.FaceOps;
                        bodyDrags += ed.BodyDrags; bodyOps += ed.BodyOps;
                        hairDrags += ed.HairDrags; hairOps += ed.HairOps;
                        clothesDrags += ed.ClothesDrags; clothesOps += ed.ClothesOps;
                        accDrags += ed.AccessoryDrags; accOps += ed.AccessoryOps;
                        var bus = n.Session.Commands;
                        while (bus.CanUndo && bus.UndoList.Count > base0) bus.Undo();
                        n.Apply();
                        if (!DocBytes(n.Stored).SequenceEqual(initDocs)) { undoDocs++; Check($"seed {s} ({rel}): undo all restores the documents byte for byte", false, string.Join(", ", DocDiff(n.Stored, c.Subtree))); }
                        var su = n.Snapshot();
                        var du = CharaSnapshot.Compare(initSnap, su, 12);
                        checks++;
                        if (du.Equal) equal++; else { differ++; Check($"seed {s} ({rel}): undo all = the initial snapshot", false, du.ToString()); }
                        Compare("after undo all");
                        while (bus.CanRedo) bus.Redo();
                        n.Apply();
                        Compare("after redo all");
                    }
                    catch (Exception e) { failed++; Check($"seed {s} ({rel}): no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace)); }
                    finally { n.Dispose(); }
                    if (s % 4 == 3) h.ReleaseBundles();
                    if (s % 5 == 4) Debug.Log($"MakerEditTest fuzz: {s + 1}/{seeds} seeds, {sw.Elapsed.TotalSeconds:F0} s");
                }
            }
        }
        finally { FaceSetup.Seed = fseed; }
        Check("F: realized = fresh build at every check (every 10 operations, undo all, redo all)", differ == 0 && failed == 0 && checks > 0,
              $"{equal} equal, {differ} differ, {failed} failed of {checks} checks; sections differing: {string.Join(", ", diffSections.Select(kv => kv.Key + " " + kv.Value))}");
        Check("F: undo all restores the documents byte for byte", undoDocs == 0, $"{undoDocs} seeds differ");
        Info($"{totalOps} transactions, {swaps} part swaps, {cancelled} cancelled, {refused} ops refused by validation (kept as no-ops)");
        Info($"Phase 4.1: {drags} drags through the preview seam ({dragsCancelled} cancelled), {faceOps} face operations (head changes, preset pushes, face sets)");
        Info($"Phase 4.2: {bodyDrags} body drags (body shapes, bust size, skin colour, paint layout / colour), {bodyOps} body operations (swatch pushes, region picks, bust-size / bust sets); the body normals compared at every check");
        Info($"Phase 4.3: {hairDrags} hair drags (colour, bundle rates, mesh layout), {hairOps} hair operations (305 presets, skin matches, item swaps, noShake, bundle resets, draw type, mesh patterns, the edit-together switch); the hair digest (materials, bones, chains) compared at every check");
        Info($"Phase 4.4: {clothesDrags} clothes drags (main colour, gloss, pattern layout), {clothesOps} clothes operations (item swaps, pattern picks, option toggles, break rates, metallic + rotation, pattern colours, «初期カラー», prefab-default resets, coordinate applies in the three modes); the clothes digest (materials, creators, objects, clothes chains) compared at every check");
        Info($"Phase 4.5: {accDrags} accessory drags (colour, gloss, a transform component), {accOps} accessory operations (adds to a chosen slot, hair-type items, parents of the 54 and «初期親», copies with mirrors, «髪の毛の色を取得», «初期カラー», draw types, moves to a slot); the accessory digest (materials, objects, accessory chains) compared at every check");
        Finish("maker_fuzz_report.txt", "F realization fuzz", sw);
    }

    // ================================================================ L (leak)

    sealed class Census
    {
        public int Rts, Materials, Textures, Meshes, Owned, Objects, Graveyard;
        public long RtBytes;
        public static Census Take(Node n)
        {
            var c = new Census();
            foreach (var rt in Resources.FindObjectsOfTypeAll<RenderTexture>())
            {
                if (!rt || !rt.IsCreated()) continue;
                c.Rts++;
                c.RtBytes += (long)(UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(rt));
            }
            c.Materials = Resources.FindObjectsOfTypeAll<Material>().Length;
            c.Textures = Resources.FindObjectsOfTypeAll<Texture2D>().Length;
            c.Meshes = Resources.FindObjectsOfTypeAll<Mesh>().Length;
            var r = n.Real;
            c.Owned = r?.Context?.OwnedMaterials.Count ?? -1;
            c.Objects = r != null && r.Root ? r.Root.GetComponentsInChildren<Transform>(true).Length : -1;
            c.Graveyard = n.Runtime.GraveyardRoot ? n.Runtime.GraveyardRoot.childCount : -1;
            return c;
        }
        public override string ToString() => $"RTs {Rts} ({RtBytes / (1024.0 * 1024.0):F1} MiB), materials {Materials}, textures {Textures}, meshes {Meshes}, owned materials {Owned}, objects {Objects}, graveyard {Graveyard}";
        public string Delta(Census b) => $"RTs {b.Rts - Rts:+0;-0;0} ({(b.RtBytes - RtBytes) / (1024.0 * 1024.0):+0.0;-0.0;0} MiB), materials {b.Materials - Materials:+0;-0;0}, textures {b.Textures - Textures:+0;-0;0}, meshes {b.Meshes - Meshes:+0;-0;0}, owned {b.Owned - Owned:+0;-0;0}, objects {b.Objects - Objects:+0;-0;0}, graveyard {b.Graveyard - Graveyard:+0;-0;0}";
        public bool Same(Census b) => Rts == b.Rts && RtBytes == b.RtBytes && Materials == b.Materials && Textures == b.Textures && Meshes == b.Meshes && Owned == b.Owned && Objects == b.Objects && Graveyard == b.Graveyard;
    }

    public static void RunLeak()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        int swapsN = int.Parse(Arg("-swaps", "200"), CultureInfo.InvariantCulture);
        rep.AppendLine($"maker edit leak test L (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}): BR-Chan, {swapsN} random part swaps + undo all, twice (the first warms the bundles)");
        using (var h = MakerCoreTest.Harness.Open())
        {
            string file = TestTargets.Load().VisualCards.FirstOrDefault(File.Exists);
            var c = Import(h, file, out string err);
            if (c == null) { Check("BR-Chan imported", false, err); Finish("maker_leak_report.txt", "L swap leak", sw); return; }
            var pools = new Pools(h.Catalog, new[] { c.Subtree });
            var n = NewNode(h, c, RelOf(file));
            try
            {
                if (!n.Ok) { Check("BR-Chan built", false, n.Error); return; }
                // 200 swaps + the add entry exceed the default 200-entry history: room for all of them
                n.Session.Commands.Limits = new HistoryLimits { MaxEntries = swapsN + 50 };
                var init = DocBytes(n.Stored);
                int base0 = n.Session.Commands.UndoList.Count;   // "Add character" stays
                var c0 = Census.Take(n);
                Info("after the build: " + c0);
                Census after1 = null;
                var rebuilds = 0; var flushes = 0;
                for (int pass = 1; pass <= 2; pass++)
                {
                    var ed = new Editor(n, 77, pools);   // the same swap sequence both times
                    var real = n.Real;
                    int f0 = real.EditFlushes, r0 = real.EditRebuilds;
                    var ms = new List<double>();
                    for (int i = 0; i < swapsN; i++)
                    {
                        var t = Stopwatch.StartNew();
                        ed.Swap();
                        n.Apply();
                        ms.Add(t.Elapsed.TotalMilliseconds);
                    }
                    var mid = Census.Take(n);
                    var bus = n.Session.Commands;
                    int undone = 0;
                    while (bus.CanUndo && bus.UndoList.Count > base0) { bus.Undo(); n.Apply(); undone++; }
                    real = n.Real;
                    rebuilds = real.EditRebuilds - r0; flushes = real.EditFlushes - f0;
                    var cp = Census.Take(n);
                    Info($"pass {pass}: {ed.Swaps} swaps (median {Median(ms):F1} ms, max {ms.Max():F1} ms), {undone} undone; flushes {flushes}, rebuilds {rebuilds}; after the swaps: {mid}; after undo all: {cp}");
                    Check($"pass {pass}: undo all restores the documents byte for byte", DocBytes(n.Stored).SequenceEqual(init));
                    if (pass == 1) after1 = cp;
                    else Check("L: after the second pass = after the first ± 0 (RTs and bytes, materials, textures, meshes, owned materials, objects, graveyard)", after1.Same(cp), after1.Delta(cp));
                }
                Check("L: render textures and owned materials after the passes = after the build", c0.Rts == after1.Rts && c0.RtBytes == after1.RtBytes && c0.Owned == after1.Owned, c0.Delta(after1));
                EditorUtility.UnloadUnusedAssetsImmediate(true);
                GC.Collect();
                Info("after UnloadUnusedAssets: " + Census.Take(n) + " (delta to the build: " + c0.Delta(Census.Take(n)) + ")");

                // ---- Phase 4.3 package A: 200 hair item swaps, 100 mesh pattern
                // picks and 50 draw-type switches, undone, twice (the first pass warms the bundles): owned materials, RTs and the
                // rest back to the first pass ± 0; the mesh textures are the catalog's (not owned by the character)
                {
                    var bus = n.Session.Commands;
                    n.Session.Commands.Limits = new HistoryLimits { MaxEntries = 400 };
                    int hb = bus.UndoList.Count;
                    var hinit = DocBytes(n.Stored);
                    Census h1 = null;
                    var o = new ExecuteOptions { Source = CommandSource.User };
                    for (int pass = 1; pass <= 2; pass++)
                    {
                        var rng = new System.Random(43);   // the same sequence both times
                        var ms = new List<double>();
                        for (int i = 0; i < 350; i++)
                        {
                            var t = Stopwatch.StartNew();
                            if (i < 200)
                            {
                                int k = rng.Next(4);
                                var pool = pools.Of(300 + k, n.Stored.Chara.Profile.Sex);
                                if (pool.Count > 0) n.Ops.ChangeItem(new[] { n.Id }, "appearance.chara.hair." + HairParts[k] + ".item", pool[rng.Next(pool.Count)], o);
                            }
                            else if (i < 300) n.Ops.ChangeItem(new[] { n.Id }, "appearance.chara.hair." + HairParts[rng.Next(4)] + ".mesh.item", "illusion.hs2#" + rng.Next(12), o);
                            else n.Ops.EditAppearance(new[] { n.Id }, new List<(string, object)> { ("appearance.chara.hair.shaderType", i % 2) }, o);
                            n.Apply();
                            ms.Add(t.Elapsed.TotalMilliseconds);
                        }
                        var hm = Census.Take(n);
                        int hu = 0;
                        while (bus.CanUndo && bus.UndoList.Count > hb) { bus.Undo(); n.Apply(); hu++; }
                        var hp = Census.Take(n);
                        Info($"4.3 pass {pass}: 350 hair ops (200 swaps, 100 mesh picks, 50 draw types; median {Median(ms):F1} ms, max {ms.Max():F1} ms), {hu} undone; after the ops: {hm}; after undo all: {hp}");
                        Check($"L (4.3) pass {pass}: undo all restores the documents byte for byte", DocBytes(n.Stored).SequenceEqual(hinit));
                        // the catalog's pattern textures: at most one per 306 row (never copied per character or per pick)
                        int meshTex = Resources.FindObjectsOfTypeAll<Texture2D>().Count(t => t && t.name.StartsWith("mesh_t_", StringComparison.Ordinal));
                        Check($"L (4.3) pass {pass}: mesh pattern textures ≤ 11 (the catalog's, one per row)", meshTex <= 11, meshTex.ToString());
                        if (pass == 1) h1 = hp;
                        else Check("L (4.3): after the second pass = after the first ± 0 (RTs and bytes, materials, textures, meshes, owned materials, objects, graveyard)", h1.Same(hp), h1.Delta(hp));
                    }
                    Check("L (4.3): render textures and owned materials after the hair passes = after the build", c0.Rts == h1.Rts && c0.RtBytes == h1.RtBytes && c0.Owned == h1.Owned, c0.Delta(h1));
                }

                // ---- Phase 4.4 package A: on the KCOX-grown fixture (BR-Chan in
                // 240#2 with a synthetic main overlay twice its main texture), 200 clothes swaps, 100 pattern picks, 100 colour /
                // gloss edits of the grown top and 50 coordinate applies (three modes), undone, twice (the first pass warms the bundles): RTs and bytes, owned
                // materials and the rest back to the first pass ± 0, no edit rebuild; after the release every creator's
                // material and RT destroyed
                {
                    var kc = MakerClothesTest.KcoxFixture(h, c, out int kw, out int kh);
                    if (kc == null) Check("L (4.4) KCOX fixture (240#2)", false, "240#2 or its main texture not found");
                    else
                    {
                        var kn = NewNode(h, kc, "BR-Chan (KCOX)");
                        try
                        {
                            if (!kn.Ok) Check("L (4.4) KCOX fixture built", false, kn.Error);
                            else
                            {
                                var top = kn.Real.Context.Edit.ClothesColor[0, 0];
                                Check("L (4.4) the fixture's top colour creator is KCOX-grown (2× the main texture)", top != null && top.Creator.Target && top.Creator.Target.width == 2 * kw && top.Creator.Target.height == 2 * kh,
                                      top?.Creator.Target ? top.Creator.Target.width + "x" + top.Creator.Target.height : "no creator");
                                var bus = kn.Session.Commands;
                                bus.Limits = new HistoryLimits { MaxEntries = 450 };
                                int kb = bus.UndoList.Count;
                                var kinit = DocBytes(kn.Stored);
                                var o = new ExecuteOptions { Source = CommandSource.User };
                                var kids = new[] { kn.Id };
                                var k0 = Census.Take(kn);
                                // Unity's pool of temporary RTs keeps the KCOX compositing temporaries of other sizes alive after their
                                // release in a batch run without frames: counted apart from the RTs the characters own
                                (int n, long bytes, string sizes) Rts(bool temporaries)
                                {
                                    var l = Resources.FindObjectsOfTypeAll<RenderTexture>().Where(x => x && x.IsCreated() && x.name.StartsWith("TempBuffer", StringComparison.Ordinal) == temporaries).ToList();
                                    return (l.Count, l.Sum(x => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(x)), string.Join(", ", l.Select(x => x.width + "x" + x.height)));
                                }
                                var own0 = Rts(false); var tmp0 = Rts(true);
                                Census k1 = null;
                                int kRebuilds = 0;
                                for (int pass = 1; pass <= 2; pass++)
                                {
                                    var rng = new System.Random(44);   // the same sequence both times
                                    int r0 = kn.Real.EditRebuilds;
                                    var ms = new List<double>();
                                    var kcoords = new List<CoordContainer>();
                                    foreach (var name in new[] { "ガーリーブラウス.png", "ジャージ.png" })
                                    {
                                        string f = Path.Combine(TestTargets.UserData, "coordinate", "female", name);
                                        var cc = File.Exists(f) ? kn.Ops.LoadCoordinate(f, null, out _, out _) : null;
                                        if (cc != null) kcoords.Add(cc);
                                    }
                                    for (int i = 0; i < 450; i++)
                                    {
                                        var t = Stopwatch.StartNew();
                                        if (i < 200)
                                        {
                                            int kind = rng.Next(8);
                                            var pool = pools.Of(ClothesSet.FemaleCategories[kind], 1);
                                            if (pool.Count > 0) kn.Ops.ChangeItem(kids, "appearance.coord.clothes." + ClothesSet.Names[kind] + ".item", pool[rng.Next(pool.Count)], o);
                                            if (i % 25 == 24) kn.Ops.ChangeItem(kids, "appearance.coord.clothes.top.item", "illusion.hs2#2", o);   // back to the grown top now and then
                                        }
                                        else if (i < 300) kn.Ops.ChangeItem(kids, "appearance.coord.clothes." + ClothesSet.Names[rng.Next(8)] + ".colors[" + rng.Next(3) + "].pattern", "illusion.hs2#" + rng.Next(20), o);
                                        else if (i >= 400)
                                        {
                                            if (kcoords.Count > 0) kn.Ops.ApplyCoordinate(kids, kcoords[i % kcoords.Count], (CoordinateApplyMode)(i % 3), o);
                                        }
                                        else
                                        {
                                            if (i == 300) kn.Ops.ChangeItem(kids, "appearance.coord.clothes.top.item", "illusion.hs2#2", o);
                                            string member = i % 2 == 0 ? "base" : "gloss";
                                            object v = i % 2 == 0 ? (object)new Color4((float)rng.NextDouble(), 0.4f, 0.6f, 1) : (float)rng.NextDouble();
                                            kn.Ops.EditAppearance(kids, new List<(string, object)> { ("appearance.coord.clothes.top.colors[" + rng.Next(3) + "]." + member, v) }, o);
                                        }
                                        kn.Apply();
                                        ms.Add(t.Elapsed.TotalMilliseconds);
                                    }
                                    var km = Census.Take(kn);
                                    int ku = 0;
                                    while (bus.CanUndo && bus.UndoList.Count > kb) { bus.Undo(); kn.Apply(); ku++; }
                                    kRebuilds += kn.Real.EditRebuilds - r0;
                                    var kp = Census.Take(kn);
                                    Info($"4.4 pass {pass}: 450 clothes ops on the KCOX fixture (200 swaps, 100 pattern picks, 100 colour / gloss edits, 50 coordinate applies of {kcoords.Count} coordinates in the three modes; median {Median(ms):F1} ms, max {ms.Max():F1} ms), {ku} undone, edit rebuilds {kn.Real.EditRebuilds - r0}; after the ops: {km}; after undo all: {kp}");
                                    Check($"L (4.4) pass {pass}: undo all restores the documents byte for byte", DocBytes(kn.Stored).SequenceEqual(kinit));
                                    if (pass == 1) k1 = kp;
                                    else Check("L (4.4): after the second pass = after the first ± 0 (RTs and bytes, materials, textures, meshes, owned materials, objects, graveyard)", k1.Same(kp), k1.Delta(kp));
                                }
                                Check("L (4.4): no edit rebuild in 900 clothes ops", kRebuilds == 0, kRebuilds + " rebuilds");
                                var own1 = Rts(false); var tmp1 = Rts(true);
                                Info($"4.4: Unity's pooled temporary RTs after the build {tmp0.n} ({tmp0.bytes / 1048576.0:F1} MiB), after the passes {tmp1.n} ({tmp1.bytes / 1048576.0:F1} MiB: {tmp1.sizes})");
                                Check("L (4.4): render textures (count and bytes, Unity's pooled temporaries apart) and owned materials after the passes = after the build",
                                      own0.n == own1.n && own0.bytes == own1.bytes && k0.Owned == k1.Owned, $"RTs {own0.n} → {own1.n}, {own0.bytes / 1048576.0:F1} → {own1.bytes / 1048576.0:F1} MiB; " + k0.Delta(k1));
                                var kctx = kn.Real.Context;
                                var cmats = kctx.Edit.Composites().Select(x => x.Creator.Mat).ToList();
                                var crts = kctx.Edit.Composites().Select(x => x.Creator.Target).ToList();
                                kn.Dispose();   // ReleaseContent
                                Check("L (4.4): after the release every creator material and RT destroyed", cmats.Count > 0 && cmats.All(m => !m) && crts.All(x => !x), $"{cmats.Count(m => m)} materials, {crts.Count(x => x)} RTs alive of {cmats.Count} creators");
                            }
                        }
                        finally { kn.Dispose(); }
                    }
                }

                // ---- Phase 4.5 package A: 200 accessory swaps (items, some
                // hair-type), 100 parent changes, 100 colour / move edits and 50 copies on BR-Chan, undone, twice (the first pass warms
                // the bundles): RTs and bytes, owned materials and the rest back to the first pass ± 0; edit rebuilds counted
                {
                    var bus = n.Session.Commands;
                    bus.Limits = new HistoryLimits { MaxEntries = 500 };
                    int ab = bus.UndoList.Count;
                    var ainit = DocBytes(n.Stored);
                    var o = new ExecuteOptions { Source = CommandSource.User };
                    var ids = new[] { n.Id };
                    var keys = AttachPointTable.Vanilla.Groups.SelectMany(g => g.Points).ToList();
                    // the reference: the census at the start of this block (Unity's pooled temporary RTs of the KCOX block stay)
                    var a0 = Census.Take(n);
                    Census a1 = null;
                    int aRebuilds = 0;
                    for (int pass = 1; pass <= 2; pass++)
                    {
                        var rng = new System.Random(45);   // the same sequence both times
                        int r0 = n.Real.EditRebuilds;
                        var ms = new List<double>();
                        for (int i = 0; i < 450; i++)
                        {
                            var accs = n.Stored.Coord.Accessories;
                            if (accs.Count == 0) { n.Ops.AddAccessory(n.Id, 352, "illusion.hs2#0", null, null, o); n.Apply(); continue; }
                            var a = accs[rng.Next(accs.Count)];
                            var t = Stopwatch.StartNew();
                            if (i < 200)
                            {
                                string r = i % 5 == 0 && a.Category == 351 ? "illusion.hs2#" + (40 + rng.Next(9)) : null;
                                var pool = pools.Of(a.Category, 1);
                                if (r == null && pool.Count > 0) r = pool[rng.Next(pool.Count)];
                                if (r != null) n.Ops.ChangeItem(ids, AppearancePaths.Accessory(a.Id, "item"), r, o);
                            }
                            else if (i < 300) n.Ops.EditAppearance(ids, new List<(string, object)> { (AppearancePaths.Accessory(a.Id, "parent"), keys[rng.Next(keys.Count)]) }, o);
                            else if (i < 400)
                            {
                                object v = i % 2 == 0 ? (object)new Color4((float)rng.NextDouble(), 0.4f, 0.6f, 1) : new Vec3((float)rng.NextDouble() * 10, 0, 0);
                                n.Ops.EditAppearance(ids, new List<(string, object)> { (AppearancePaths.Accessory(a.Id, i % 2 == 0 ? "colors[" + rng.Next(4) + "].color" : "moves[0].position"), v) }, o);
                            }
                            else n.Ops.CopyAccessory(n.Id, a.Id, null, i % 2 == 0 ? null : accs[rng.Next(accs.Count)].Id, AccessoryCopyParts.Slot, rng.Next(2) == 0, MirrorAxis.None, o);
                            n.Apply();
                            ms.Add(t.Elapsed.TotalMilliseconds);
                        }
                        var am = Census.Take(n);
                        int au = 0;
                        while (bus.CanUndo && bus.UndoList.Count > ab) { bus.Undo(); n.Apply(); au++; }
                        aRebuilds += n.Real.EditRebuilds - r0;
                        var ap = Census.Take(n);
                        Info($"4.5 pass {pass}: 450 accessory ops (200 swaps, 100 parents, 100 colour / move edits, 50 copies; median {Median(ms):F1} ms, max {ms.Max():F1} ms), {au} undone, edit rebuilds {n.Real.EditRebuilds - r0}; after the ops: {am}; after undo all: {ap}");
                        Check($"L (4.5) pass {pass}: undo all restores the documents byte for byte", DocBytes(n.Stored).SequenceEqual(ainit));
                        if (pass == 1) a1 = ap;
                        else Check("L (4.5): after the second pass = after the first ± 0 (RTs and bytes, materials, textures, meshes, owned materials, objects, graveyard)", a1.Same(ap), a1.Delta(ap));
                    }
                    Info($"L (4.5): edit rebuilds in 900 accessory ops: {aRebuilds}");
                    Check("L (4.5): render textures and owned materials after the accessory passes = before them", a0.Rts == a1.Rts && a0.RtBytes == a1.RtBytes && a0.Owned == a1.Owned, a0.Delta(a1));
                }

                // ---- Phase 4.2 package A: 100 bust-size sets on the
                // female; the breast-normal mesh copies: one per applicable entry, none added by the sets, none after
                // ReleaseContent
                {
                    var ctx = n.Real.Context;
                    int applicable = ctx.BustNormals?.Entries.Count(e => e.Applicable) ?? 0;
                    int meshes0 = Resources.FindObjectsOfTypeAll<Mesh>().Length;
                    var copies0 = ctx.OwnedMeshes.ToList();
                    var rng = new System.Random(5);
                    var ms = new List<double>();
                    for (int i = 0; i < 100; i++)
                    {
                        var t = Stopwatch.StartNew();
                        n.Ops.EditAppearance(new[] { n.Id }, new List<(string, object)> { ("appearance.chara.body.shape[1]", (float)(rng.NextDouble() * 0.9)) }, new ExecuteOptions { Source = CommandSource.User });
                        n.Apply();
                        ms.Add(t.Elapsed.TotalMilliseconds);
                    }
                    int meshes1 = Resources.FindObjectsOfTypeAll<Mesh>().Length;
                    Info($"4.2: 100 bust-size sets (median {Median(ms):F2} ms); owned meshes {ctx.OwnedMeshes.Count} (applicable entries {applicable}); Mesh objects {meshes0} → {meshes1}");
                    Check("L (4.2): owned meshes = 1 per applicable breast-normal entry, the same copies, no Mesh object added by 100 bust-size sets",
                          applicable == 1 && ctx.OwnedMeshes.Count == applicable && ctx.OwnedMeshes.SequenceEqual(copies0) && meshes1 == meshes0, $"owned {ctx.OwnedMeshes.Count}, meshes {meshes1 - meshes0:+0;-0;0}");
                    n.Dispose();   // ReleaseContent
                    Check("L (4.2): 0 owned meshes after ReleaseContent (the copies destroyed)", ctx.OwnedMeshes.Count == 0 && copies0.All(m => !m) && Resources.FindObjectsOfTypeAll<Mesh>().Length <= meshes1 - copies0.Count,
                          $"owned {ctx.OwnedMeshes.Count}, alive copies {copies0.Count(m => m)}");
                }
            }
            catch (Exception e) { Check("no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace)); }
            finally { n.Dispose(); }
        }
        Finish("maker_leak_report.txt", "L swap leak", sw);
    }

    // ================================================================ T (U5, U6)

    public static void RunTiming()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        int reps = int.Parse(Arg("-reps", "20"), CultureInfo.InvariantCulture);
        rep.AppendLine($"maker edit timing U5 / U6 (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceName}), BR-Chan, warm medians of {reps}");
        int? fseed = FaceSetup.Seed;
        FaceSetup.Seed = 12345;
        try
        {
            using (var h = MakerCoreTest.Harness.Open())
            {
                var visual = TestTargets.Load().VisualCards.Where(File.Exists).ToList();
                var conts = new List<CharaContainer>();
                foreach (var f in visual) { var ct = Import(h, f, out _); if (ct != null) conts.Add(ct); }
                if (conts.Count == 0) { Check("targets imported", false); return; }
                var c = conts[0];
                var pools = new Pools(h.Catalog, new[] { c.Subtree });
                var n = NewNode(h, c, RelOf(visual[0]));
                try
                {
                    if (!n.Ok) { Check("BR-Chan built", false, n.Error); return; }
                    int sex = n.Stored.Chara.Profile.Sex;
                    string top = ClothesSet.Names[0];
                    var kinds = new List<(string name, string path, List<string> pool, double target)>
                    {
                        ("accessory", null, null, 30),
                        ("hair part (front)", "appearance.chara.hair.front.item", pools.Of(301, sex), 60),
                        ("clothes kind (top)", "appearance.coord.clothes." + top + ".item", pools.Of((sex == 0 ? ClothesSet.MaleCategories : ClothesSet.FemaleCategories)[0], sex), 100),
                        ("head", "appearance.chara.face.head", pools.Of(sex == 0 ? 110 : 210, sex), 200),
                    };
                    var acc = n.Stored.Coord.Accessories.FirstOrDefault();
                    foreach (var (name, path0, pool0, target) in kinds)
                    {
                        string path = path0;
                        var pool = pool0;
                        if (path == null)
                        {
                            if (acc == null) { Info(name + ": no accessory on the target"); continue; }
                            path = AppearancePaths.Accessory(acc.Id, "item");
                            pool = pools.Of(acc.Category, sex);
                        }
                        FieldAccessor.TryGet(n.Session.Model, n.Id, path, out var curObj, out _);
                        string cur = curObj as string;
                        var others = pool.Where(r => r != cur).ToList();
                        if (cur == null || others.Count < 2) { Info(name + ": not enough rows"); continue; }
                        var opCold = new List<double>(); var opWarm = new List<double>(); var flushWarm = new List<double>(); var undo = new List<double>();
                        var real = n.Real;
                        // cold: the first load of rows not used yet (bundles possibly shared with earlier rows)
                        foreach (var r in others.Skip(1).Take(3))
                        {
                            OpResult cr = null;
                        opCold.Add(TimeOp(n, () => cr = n.Ops.ChangeItem(new[] { n.Id }, path, r)));
                        if (cr == null || !cr.Ok || cr.Changed.Count == 0) Info($"U5 {name}: {r}: {cr?.Code} {cr?.Message} ({cr?.Changed.Count} changed)");
                            TimeOp(n, () => n.Ops.ChangeItem(new[] { n.Id }, path, cur));
                        }
                        string alt = others[0];
                        TimeOp(n, () => n.Ops.ChangeItem(new[] { n.Id }, path, alt));
                        TimeOp(n, () => n.Ops.ChangeItem(new[] { n.Id }, path, cur));
                        for (int i = 0; i < reps; i++)
                        {
                            opWarm.Add(TimeOp(n, () => n.Ops.ChangeItem(new[] { n.Id }, path, i % 2 == 0 ? alt : cur)));
                            real = n.Real;
                            if (real?.LastEditFlush != null) flushWarm.Add(real.LastEditFlush.Milliseconds);
                        }
                        for (int i = 0; i < Math.Min(reps, 10); i++)
                        {
                            TimeOp(n, () => n.Ops.ChangeItem(new[] { n.Id }, path, alt));
                            undo.Add(TimeOp(n, () => { n.Session.Commands.Undo(); return OpResult.Success(); }));
                        }
                        double med = Median(opWarm);
                        string verdict = med <= target ? "met" : med <= 2 * target ? "WARN" : "above 2x";
                        Info($"U5 {name}: warm op median {med:F1} ms (target ≤ {target} ms: {verdict}), flush median {Median(flushWarm):F1} ms; cold op median {Median(opCold):F1} ms (max {(opCold.Count > 0 ? opCold.Max() : 0):F1}); undo of a swap median {Median(undo):F1} ms; rebuilds so far {n.Real?.EditRebuilds}; last flush: {n.Real?.LastEditFlush}");
                    }
                    // U6: the face preset (≈ 40 units of one composite) and a morph to the other target of the sex
                    var presetMs = new List<double>(); var presetFlush = new List<double>();
                    string headNow = n.Stored.Chara.Face.Head;
                    for (int i = 0; i < Math.Min(reps, 10); i++)
                    {
                        OpResult r = null;
                        presetMs.Add(TimeOp(n, () => r = n.Ops.ApplyFacePreset(new[] { n.Id }, headNow)));
                        if (i == 0 && (r == null || !r.Ok || r.Changed.Count == 0)) { Info("U6 face preset: " + r?.Code + " " + r?.Message + " (" + r?.Changed.Count + " changed)"); break; }
                        if (n.Real?.LastEditFlush != null) presetFlush.Add(n.Real.LastEditFlush.Milliseconds);
                        n.Session.Commands.Undo(); n.Apply();
                    }
                    Info($"U6 face preset push: op median {Median(presetMs):F1} ms, flush median {Median(presetFlush):F1} ms (units {n.Real?.LastEditFlush?.Units.Count})");
                    Check("timing rows ran", true);
                }
                finally { n.Dispose(); }
            }
        }
        catch (Exception e) { Check("no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace)); }
        finally { FaceSetup.Seed = fseed; }
        Finish("maker_timing_report.txt", "U5 / U6 part swap and flush times", sw);
    }

    static double TimeOp(Node n, Func<OpResult> op)
    {
        var t = Stopwatch.StartNew();
        op();
        n.Apply();
        return t.Elapsed.TotalMilliseconds;
    }

    // ================================================================ report helpers

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
        Debug.Log($"MAKER EDIT TEST DONE ({file}): {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }

    static string Arg(string name, string def = null)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static bool Flag(string name) => TestArgs.Get().Contains(name);
    static string Short(string s, int n = 400) => s == null ? "" : s.Length > n ? s.Substring(0, n) + "…" : s;

    static double Median(List<double> l)
    {
        if (l == null || l.Count == 0) return double.NaN;
        var s = l.OrderBy(x => x).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }

    static string RelOf(string file)
    {
        if (file == null) return null;
        string full = Path.GetFullPath(file), ud = Path.GetFullPath(TestTargets.UserData).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return (full.StartsWith(ud, StringComparison.OrdinalIgnoreCase) ? full.Substring(ud.Length) : Path.GetFileName(full)).Replace('\\', '/');
    }

    static List<string> CardsFromArgs(out string what)
    {
        List<string> l;
        string list = Arg("-list", null);
        if (!string.IsNullOrEmpty(list))
        {
            // a text file of card paths (absolute or relative to UserData), one per line, in pair order
            l = File.ReadAllLines(list).Select(s => s.Trim()).Where(s => s.Length > 0 && !s.StartsWith("#", StringComparison.Ordinal))
                    .Select(s => Path.IsPathRooted(s) ? s : Path.Combine(TestTargets.UserData, s.Replace('/', Path.DirectorySeparatorChar)))
                    .Where(File.Exists).ToList();
            what = "the cards listed in " + Path.GetFileName(list);
        }
        else if (Flag("-all")) { l = MakerCoreTest.AllCards(); what = "every card of the card folders"; }
        else if (Flag("-targets")) { l = TestTargets.Load().VisualCards.Where(File.Exists).ToList(); what = "the visual targets"; }
        else { l = MakerCoreTest.SampleCards(); what = "the visual targets + 20 sampled cards"; }
        int limit = int.Parse(Arg("-limit", int.MaxValue.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        if (l.Count > limit) l = l.Take(limit).ToList();
        return l;
    }
}
