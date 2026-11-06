// AdvancedPoseTest.ReconPlay (Phase 6.3a package M; opt-in row AdvancedPoseTest.recon): the census of a card's bone modifiers in REAL
// frames: does every modifier show? Each card is built twice, the modifiers off and on, in the same order (the load, the T-pose row, frames, the row), and per modifier and
// channel the value with the modifiers on must be the value with them off with the modifier composed (1e-4, 0.01 deg). So a base that holds the modifier once more fails here,
// where a second pass of the applier would not. Rows: 19/0/0 at 0.5, dance_00, back on 19/0/0, Chair_A (the same controller as 19/0/0), back on 19/0/0; Chair_A and attack_01 as first binds and a scene save and load on a sample of
// 50 cards. Exceptions: Tools/modifier_known.json, each with a reason and a count. Report Spike/out/advancedpose/recon_play.txt, SUMMARY first. Arguments: -minutes N (the
// budget, default 54), -limit N (a trial on the first N cards), -only <hash>+<hash> (these cards, by the first 12 hex of their SHA-256). Cards by hash, bones by name, no image opened.
// AdvancedPoseTest.ReconStep0 (FM0, by hand, not a row): where a base of this frame's input and a base frozen on the T-pose row differ.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.App;
using HS2Next.Chara;
using HS2Next.Chara.Ext;
using HS2Next.Content;
using HS2Next.Data.Ext;
using HS2Next.Motion.DynamicBones;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static partial class AdvancedPoseTest
{
    static string ReconPlayPath => Path.Combine(OutDir, "recon_play.txt");

    public static void ReconPlay()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(ReconPlayPath, "SUMMARY: 0 PASS, 1 FAIL\nAdvancedPoseTest.recon: started, no result yet\n", new UTF8Encoding(false));
        string launch = "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":false},\"userData\":\"Spike/out/advancedpose/recon_userdata\",\"bridge\":{\"port\":17772,\"outputDir\":\"Spike/out/advancedpose/recon_out\",\"allowQuit\":false}}";
        PlayModeHarness.Start("AdvancedPoseTest.ReconPlayMain", ReconPlayPath, 58, true, launch);
    }

    /// <summary>H's order: the load, the T-pose row at speed 0, face and look fixed, physics and look off, frames, the row. Returns the key in pA.</summary>
    static IEnumerator ReconPlayLoad(AppCore app, string rel, string sex, bool row = true)
    {
        pA = null;
        yield return PCall(app, new { cmd = "scene.new" });
        yield return PCall(app, new { cmd = "chara.load", path = rel, sex });
        pA = Key(pLast);
        if (pA == null) yield break;
        yield return PFrames(5);
        yield return PCall(app, new { cmd = "chara.anime", key = pA, group = 0, category = 0, no = 0, time = 0f, speed = 0f });
        yield return PCall(app, new { cmd = "chara.face", key = pA, eyesLook = 0, neckLook = 4, blink = false });
        yield return PFrames(5);
        yield return PCall(app, new { cmd = "chara.motion", key = pA, physics = false, look = false });
        yield return PFrames(10);
        if (!row) yield break;
        yield return PCall(app, new { cmd = "chara.anime", key = pA, group = 19, category = 0, no = 0, time = 0.5f, speed = 0f });
        yield return PFrames(10);
    }

    /// <summary>A card's modifiers on a built character, by name as the applier finds them; several on one transform combine.</summary>
    sealed class RecMod
    {
        public AbmxData.BoneModifier M;
        public Transform T;
        public bool[] Has;   // rotation, position / length, scale
        public Vector3 S = Vector3.one, P = Vector3.zero, R = Vector3.zero;
        public float L = 1f;
        public (Vector3 p, Quaternion r, Vector3 s) Compose((Vector3 p, Quaternion r, Vector3 s) off) =>
            (L != 1f ? off.p * L + P : off.p + P, R != Vector3.zero ? off.r * Quaternion.Euler(R) : off.r, Vector3.Scale(off.s, S));
    }

    static List<RecMod> RecMods(CharaRealization r, ReconCard c)
    {
        var dict = AbmxApplier.BodyDictionary(r.Context);
        var list = new List<RecMod>();
        foreach (var m in c.List)
        {
            var d = m.Data;
            if (d.IsIdentity) continue;
            var t = S0Resolve(r, dict, m);
            if (!t) continue;
            list.Add(new RecMod { M = m, T = t, Has = new[] { d.Rotation != Vector3.zero, d.Position != Vector3.zero || d.Length != 1f, d.Scale != Vector3.one } });
        }
        foreach (var g in list.GroupBy(x => x.T))
        {
            var s = Vector3.one; var p = Vector3.zero; var q = Vector3.zero; float l = 1f;
            foreach (var x in g)
            {
                var d = x.M.Data;
                s = Vector3.Scale(s, d.Scale); p += d.Position; q += d.Rotation; l *= d.Length;
            }
            foreach (var x in g) { x.S = s; x.P = p; x.R = q; x.L = l; }
        }
        return list;
    }

    sealed class Tally
    {
        public readonly int[] With = new int[3], Not = new int[3];
        public readonly HashSet<string>[] Cards = { new HashSet<string>(), new HashSet<string>(), new HashSet<string>() };
        public readonly SortedDictionary<string, int>[] Bones = { new SortedDictionary<string, int>(StringComparer.Ordinal), new SortedDictionary<string, int>(StringComparer.Ordinal), new SortedDictionary<string, int>(StringComparer.Ordinal) };
        public readonly double[] Worst = new double[3];
        public readonly string[] Where = { "", "", "" };

        public void Add(string hash, List<RecMod> mods, (Vector3 p, Quaternion r, Vector3 s)[] off, (Vector3 p, Quaternion r, Vector3 s)[] on)
        {
            for (int i = 0; i < mods.Count; i++)
            {
                var m = mods[i];
                var want = m.Compose(off[i]);
                var got = on[i];
                double[] e = { Angle(want.r, got.r), Dist(want.p, got.p), Dist(want.s, got.s) };
                for (int ch = 0; ch < 3; ch++)
                {
                    if (!m.Has[ch]) continue;
                    With[ch]++;
                    if (e[ch] <= (ch == 0 ? ShowDeg : ShowPos)) continue;
                    Not[ch]++;
                    Cards[ch].Add(hash);
                    Bones[ch].TryGetValue(m.M.BoneName, out int n);
                    Bones[ch][m.M.BoneName] = n + 1;
                    if (e[ch] > Worst[ch]) { Worst[ch] = e[ch]; Where[ch] = hash + " " + m.M.BoneName; }
                }
            }
        }
    }

    static readonly string[] ChanNames = { "rotation", "position / length", "scale" };

    /// <summary>The rows of one pass over a card, in the order both passes take. The first is 19/0/0 at 0.5, reached by the load.</summary>
    static IEnumerator ReconPass(AppCore app, ReconCard c, bool on, List<(int g, int cat, int no)> rows, List<(Vector3 p, Quaternion r, Vector3 s)[]> into)
    {
        pA = null;
        into.Clear();
        yield return PCall(app, new { cmd = "app.options", ext = new { abmx = on, accessoryControls = false }, fk = new { hipsOffset = false }, correctionAllOn = true });
        yield return ReconPlayLoad(app, c.Rel, c.Sex, false);
        if (pA == null) yield break;
        var r = PR(app, pA);
        if (r == null || r.State != RealizationState.Built) { pA = null; yield break; }
        var mods = RecMods(r, c);
        reconMods = mods;
        foreach (var (g, cat, no) in rows)
        {
            yield return PCall(app, new { cmd = "chara.anime", key = pA, group = g, category = cat, no, time = 0.5f, speed = 0f });
            yield return PFrames(10);
            into.Add(mods.Select(m => Local(m.T)).ToArray());
        }
    }

    static List<RecMod> reconMods;

    /// <summary>Why a card did not build: the load's code and error, the card's file name masked, or that it never reached the built state.</summary>
    static string ReconFailure(ReconCard c, bool on)
    {
        string code = (string)pLast?["code"], error = (string)pLast?["error"];
        string why = code == null && error == null ? "the character did not reach the built state" : $"code {code ?? "-"}, error {error ?? "-"}";
        return $"card {c.Hash}, modifiers {(on ? "on" : "off")}: not built: " + why.Replace(c.Rel, "<card>").Replace(Path.GetFileName(c.Rel), "<card>");
    }

    /// <summary>Tools/modifier_known.json: entries {name: "bone channel", reason, maxCount}; the count of a name is over every row and card.</summary>
    static List<(string name, string reason, int maxCount)> ReconKnown(StringBuilder o)
    {
        var res = new List<(string, string, int)>();
        string path = Path.Combine(ContentSettings.ProjectRoot, "Tools", "modifier_known.json");
        if (!File.Exists(path)) { o.AppendLine("no Tools/modifier_known.json"); return res; }
        foreach (var e in JArray.Parse(File.ReadAllText(path)).OfType<JObject>())
            res.Add(((string)e["name"], (string)e["reason"], (int)e["maxCount"]));
        return res;
    }

    static IEnumerator ReconPlayMain()
    {
        var o = new StringBuilder();
        var ready = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ready);
        var app = AppCore.Current;
        rep = new StringBuilder();
        passes = fails = warns = 0; perGroup.Clear(); measurements.Clear(); current = "recon";
        LoadTargets();
        var head = new StringBuilder();
        var cards = ReconScan(head);
        var known = ReconKnown(head);
        double minutes = double.TryParse(Arg("-minutes"), out var mv) ? mv : 54.0;
        int limit = int.TryParse(Arg("-limit"), out var lv) ? lv : int.MaxValue;
        float old = Time.captureDeltaTime;
        Time.captureDeltaTime = Dt;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ordered = cards.OrderBy(x => x.File, StringComparer.Ordinal).ToList();
        if (Arg("-only") is string onlyCards)
        {
            var pick = new HashSet<string>(onlyCards.Split('+'), StringComparer.OrdinalIgnoreCase);
            ordered = ordered.Where(x => pick.Contains(x.Hash)).ToList();
        }
        (int, int, int) idle = (19, 0, 0), dance = (11, 5, 0), chair = (19, 1, 11), attack = (15, 31, 1);
        var mainRows = new List<(int g, int cat, int no)> { idle, dance, idle, chair, idle };
        var tallies = new[] { new Tally(), new Tally(), new Tally(), new Tally(), new Tally() };
        var chairT = new Tally(); var attackT = new Tally(); var loadT = new Tally();
        int measured = 0, failed = 0;
        int sampleEvery = Math.Max(1, ordered.Count / 50);
        var off = new List<(Vector3 p, Quaternion r, Vector3 s)[]>();
        var on = new List<(Vector3 p, Quaternion r, Vector3 s)[]>();

        void Flush(string state)
        {
            var s = new StringBuilder();
            s.AppendLine(state);
            s.Append(head);
            s.AppendLine($"measured {measured} of {ordered.Count} cards in {sw.Elapsed.TotalMinutes:F1} min, {failed} failed. Real frames, headless play mode, UI off; each card twice, modifiers off and on, the same order; physics and look off, FK and IK off, joint correction rows on. NOT SHOWN = with the modifiers on the value is not the value with them off with the modifier composed (1e-4, 0.01 deg).");
            void Line(string name, Tally t)
            {
                s.AppendLine($"== {name}");
                for (int ch = 0; ch < 3; ch++) s.AppendLine($"   {ChanNames[ch]}: {t.With[ch]} modifiers with a value; NOT SHOWN {t.Not[ch]} on {t.Cards[ch].Count} cards; worst {t.Worst[ch]:G3} {t.Where[ch]}{(t.Bones[ch].Count > 0 ? "; " + S0Top(t.Bones[ch], 24) : "")}");
            }
            Line("row 19/0/0 at 0.5 after the T-pose row, every card", tallies[0]);
            Line("dance_00 as the second bind, every card", tallies[1]);
            Line("back on 19/0/0, every card", tallies[2]);
            Line("Chair_A, the same controller, every card", tallies[3]);
            Line("back on 19/0/0 after Chair_A, every card", tallies[4]);
            Line($"Chair_A as the first bind, every {sampleEvery}th card", chairT);
            Line($"attack_01 as the first bind, every {sampleEvery}th card", attackT);
            Line($"a scene save and load on 19/0/0, every {sampleEvery}th card", loadT);
            s.Append(rep);
            File.WriteAllText(ReconPlayPath, s.ToString(), new UTF8Encoding(false));
        }

        for (int ci = 0; ci < ordered.Count && measured < limit; ci++)
        {
            var c = ordered[ci];
            if (sw.Elapsed.TotalMinutes > minutes) break;
            yield return ReconPass(app, c, false, mainRows, off);
            if (pA == null) { failed++; Warn(ReconFailure(c, false)); continue; }
            yield return ReconPass(app, c, true, mainRows, on);
            if (pA == null) { failed++; Warn(ReconFailure(c, true)); continue; }
            var mods = reconMods;
            for (int k = 0; k < mainRows.Count; k++) tallies[k].Add(c.Hash, mods, off[k], on[k]);
            if (ci % sampleEvery == 0)
            {
                var firstOff = off[0].ToArray();
                // the same character saved and loaded: the bind then happens in the build
                yield return ReconPass(app, c, true, new List<(int g, int cat, int no)> { idle }, on);
                if (pA != null)
                {
                    yield return PCall(app, new { cmd = "scene.save", name = "recon_" + c.Hash, overwrite = true, thumb = "placeholder", clock = SaveClock });
                    string path = (string)pLast["path"];
                    yield return PCall(app, new { cmd = "scene.new" });
                    yield return PCall(app, new { cmd = "scene.load", path });
                    yield return PFrames(15);
                    var rl = app.Runtime.Charas().FirstOrDefault();
                    if (rl != null && rl.State == RealizationState.Built)
                    {
                        var ml = RecMods(rl, c);
                        if (ml.Count == mods.Count) loadT.Add(c.Hash, ml, firstOff, ml.Select(m => Local(m.T)).ToArray());
                        else Warn($"card {c.Hash}: after the scene load {ml.Count} modifiers, before {mods.Count}");
                    }
                    if (path != null) File.Delete(path);
                }
                // the other rows as first binds
                foreach (var (row, t) in new[] { (chair, chairT), (attack, attackT) })
                {
                    var one = new List<(int g, int cat, int no)> { row };
                    yield return ReconPass(app, c, false, one, off);
                    if (pA == null) continue;
                    yield return ReconPass(app, c, true, one, on);
                    if (pA == null) continue;
                    t.Add(c.Hash, reconMods, off[0], on[0]);
                }
            }
            measured++;
            if (measured % 25 == 0) { Debug.Log($"recon: {measured} cards, {sw.Elapsed.TotalMinutes:F1} min"); Flush("SUMMARY: 0 PASS, 1 FAIL (running)"); }
        }
        Time.captureDeltaTime = old;
        yield return PCall(app, new { cmd = "app.options", ext = new { abmx = true, accessoryControls = true }, fk = new { hipsOffset = true } });
        yield return PCall(app, new { cmd = "scene.new" });

        // the verdict: every not-shown channel lies under a known entry within its count
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in tallies.Concat(new[] { chairT, attackT, loadT }))
            for (int ch = 0; ch < 3; ch++)
                foreach (var kv in t.Bones[ch])
                {
                    string name = kv.Key + " " + ChanNames[ch];
                    counts.TryGetValue(name, out int n);
                    counts[name] = n + kv.Value;
                }
        Check("every card was measured", limit < ordered.Count || (measured == ordered.Count && failed == 0), $"{measured} of {ordered.Count}, {failed} failed");
        foreach (var kv in counts)
        {
            var k = known.Where(x => x.name == kv.Key).ToList();
            if (k.Count == 0) Check("not shown: " + kv.Key, false, kv.Value + " over all rows; no entry in Tools/modifier_known.json");
            else if (kv.Value > k[0].maxCount) Check("not shown: " + kv.Key, false, $"{kv.Value} over its count {k[0].maxCount}; {k[0].reason}");
            else Warn($"known: {kv.Key}: {kv.Value} of at most {k[0].maxCount}; {k[0].reason}");
        }
        int total = tallies.Concat(new[] { chairT, attackT, loadT }).Sum(t => t.Not.Sum());
        Check("no modifier unshown but for the known entries", counts.Keys.All(n => known.Any(x => x.name == n)), total + " not shown in all");
        Flush($"SUMMARY: {passes} PASS, {fails} FAIL ({warns} WARN, {sw.Elapsed.TotalSeconds:F0} s)");
        Debug.Log("ADVANCED POSE RECON PLAY DONE: " + measured);
        PlayModeHarness.ExitCode = fails > 0 ? 1 : 0;
    }
}

public static partial class AdvancedPoseTest
{
    // ------------------------------------------------------------ FM0 (Phase 6.3a, by hand, not a row): where the two possible bases of the card modifiers differ
    // Each card is built WITHOUT its modifiers (ext abmx false), so what is read is what the pose sources give. A modifier's channel "differs" when, on a row at one of
    // five times, that value is over 1e-4 (0.01 deg for a rotation) from the value on the T-pose row: there "this frame's input" and "a baseline frozen on the T-pose
    // row" give different results. It "moves" when the value changes between the row's five times. Numbers, bone names and card hashes only.
    // Arguments: -out <file>, -minutes N (the cards' budget), -limit N (the first N cards, a trial).

    static string Step0Out => Arg("-out") ?? Path.Combine(OutDir, "recon_step0.txt");
    static readonly float[] Step0Times = { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f };
    static readonly string[] Step0Kinds = { "rotation", "position value", "length value", "position or length", "scale" };

    public static void ReconStep0()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Step0Out, "started, no result yet\n", new UTF8Encoding(false));
        string launch = "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":false},\"userData\":\"Spike/out/advancedpose/recon_userdata\",\"bridge\":{\"port\":17781,\"outputDir\":\"Spike/out/advancedpose/recon_out\",\"allowQuit\":false}}";
        PlayModeHarness.Start("AdvancedPoseTest.ReconStep0Main", Step0Out, 39, true, launch);
    }

    sealed class S0Mod
    {
        public AbmxData.BoneModifier M;
        public AbmxData.BoneModifierData D;
        public Transform T;
        public (Vector3 p, Quaternion r, Vector3 s) TPose;
        public readonly (Vector3 p, Quaternion r, Vector3 s)[] At05 = new (Vector3, Quaternion, Vector3)[4];
        public readonly bool[,] Diff = new bool[4, 3], Move = new bool[4, 3];
        public readonly double[] Shown = new double[3];
        public bool HasRot => D.Rotation != Vector3.zero;
        public bool HasPosValue => D.Position != Vector3.zero;
        public bool HasLength => D.Length != 1f;
        public bool HasPos => HasPosValue || HasLength;
        public bool HasScale => D.Scale != Vector3.one;
        public bool Has(int ch) => ch == 0 ? HasRot : ch == 1 ? HasPos : HasScale;
    }

    sealed class S0Stat
    {
        public readonly int[] With = new int[5], AnyDiff = new int[5], AnyMove = new int[5];
        public readonly int[,] RowDiff = new int[4, 5], RowMove = new int[4, 5];
        public readonly HashSet<string>[] WithCards = S0Sets(), DiffCards = S0Sets(), MoveCards = S0Sets();
        public readonly HashSet<string>[,] RowDiffCards = new HashSet<string>[4, 5], RowMoveCards = new HashSet<string>[4, 5];
        public readonly SortedDictionary<string, int>[] DiffBones = S0Dicts(), MoveBones = S0Dicts();
        public readonly List<double>[] Shown = { new List<double>(), new List<double>(), new List<double>(), new List<double>(), new List<double>() };
        public S0Stat() { for (int i = 0; i < 4; i++) for (int k = 0; k < 5; k++) { RowDiffCards[i, k] = new HashSet<string>(); RowMoveCards[i, k] = new HashSet<string>(); } }
        public void Add(string hash, S0Mod m)
        {
            for (int k = 0; k < 5; k++)
            {
                bool has = k == 0 ? m.HasRot : k == 1 ? m.HasPosValue : k == 2 ? m.HasLength : k == 3 ? m.HasPos : m.HasScale;
                if (!has) continue;
                int ch = k == 0 ? 0 : k == 4 ? 2 : 1;
                With[k]++; WithCards[k].Add(hash);
                bool anyD = false, anyM = false;
                for (int row = 0; row < 4; row++)
                {
                    if (m.Diff[row, ch]) { anyD = true; RowDiff[row, k]++; RowDiffCards[row, k].Add(hash); }
                    if (m.Move[row, ch]) { anyM = true; RowMove[row, k]++; RowMoveCards[row, k].Add(hash); }
                }
                if (anyD)
                {
                    AnyDiff[k]++; DiffCards[k].Add(hash);
                    DiffBones[k].TryGetValue(m.M.BoneName, out int n); DiffBones[k][m.M.BoneName] = n + 1;
                    Shown[k].Add(m.Shown[ch]);
                }
                if (anyM)
                {
                    AnyMove[k]++; MoveCards[k].Add(hash);
                    MoveBones[k].TryGetValue(m.M.BoneName, out int n); MoveBones[k][m.M.BoneName] = n + 1;
                }
            }
        }
    }

    static HashSet<string>[] S0Sets() => new[] { new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>() };
    static SortedDictionary<string, int>[] S0Dicts() => new[] { S0Dict(), S0Dict(), S0Dict(), S0Dict(), S0Dict() };
    static SortedDictionary<string, int> S0Dict() => new SortedDictionary<string, int>(StringComparer.Ordinal);

    static Transform S0Resolve(CharaRealization r, Dictionary<string, Transform> body, AbmxData.BoneModifier m)
    {
        if (m.Location == (int)AbmxData.BoneLocation.BodyTop) return AbmxApplier.Find(body, m.BoneName);
        if (m.AccessorySlot >= 0)
        {
            var o = r.Context.AccessoryObject(m.AccessorySlot);
            return o ? CharaAssembler.Find(o.transform, m.BoneName) : null;
        }
        var t = AbmxApplier.Find(body, m.BoneName);
        if (t) return t;
        for (int s = 0; s < 20 && !t; s++)
        {
            var o = r.Context.AccessoryObject(s);
            if (o) t = CharaAssembler.Find(o.transform, m.BoneName);
        }
        return t;
    }

    static HashSet<Transform> S0Chains(CharaRealization r)
    {
        var set = AdvancedPoseResolver.ChainBones(r);
        var bust = r.Motion ? r.Motion.Get<BustStage>() : null;
        if (bust != null)
            foreach (var c in bust.All)
                for (int i = 0; i < c.ParticleCount; i++)
                {
                    var t = c.ParticleTransform(i);
                    if (t) set.Add(t);
                }
        return set;
    }

    static string S0Top(SortedDictionary<string, int> d, int n) =>
        string.Join(", ", d.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).Take(n).Select(x => x.Key + " " + x.Value)) + (d.Count > n ? $", ... ({d.Count} bones)" : "");

    static string S0Quant(List<double> v)
    {
        if (v.Count == 0) return "-";
        var s = v.OrderBy(x => x).ToList();
        return $"median {s[s.Count / 2]:G3}, 90% {s[(int)(s.Count * 0.9)]:G3}, max {s[s.Count - 1]:G3}";
    }

    static string S0Value(AbmxData.BoneModifierData d, int ch) =>
        ch == 0 ? $"rotation ({d.Rotation.x:F2}, {d.Rotation.y:F2}, {d.Rotation.z:F2}) deg"
        : ch == 1 ? $"position ({d.Position.x:F3}, {d.Position.y:F3}, {d.Position.z:F3}) length {d.Length:F3}"
        : $"scale ({d.Scale.x:F3}, {d.Scale.y:F3}, {d.Scale.z:F3})";

    static bool S0Mosaic(string b) => b == "cf_J_Kokan" || b == "cf_J_Kosi01" || b == "cf_J_Kosi02" || b.StartsWith("cf_J_LegUp00_", StringComparison.Ordinal) || b.StartsWith("cf_J_LegUp01_s_", StringComparison.Ordinal);

    static double S0MosaicSize(AbmxData.BoneModifierData d, int ch) =>
        ch == 0 ? Math.Max(Mathf.Abs(d.Rotation.x), Math.Max(Mathf.Abs(d.Rotation.y), Mathf.Abs(d.Rotation.z))) / 45.0
        : ch == 1 ? Math.Max(Mathf.Abs(d.Length - 1f), Math.Max(Mathf.Abs(d.Position.x), Math.Max(Mathf.Abs(d.Position.y), Mathf.Abs(d.Position.z))) / 0.5)
        : Math.Max(Mathf.Abs(d.Scale.x - 1f), Math.Max(Mathf.Abs(d.Scale.y - 1f), Mathf.Abs(d.Scale.z - 1f)));

    static IEnumerator ReconStep0Main()
    {
        var progress = new StringBuilder();
        var ready = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ready);
        var app = AppCore.Current;
        rep = new StringBuilder();
        LoadTargets();
        var scan = new StringBuilder();
        var cards = ReconScan(scan);
        double minutes = double.TryParse(Arg("-minutes"), out var mv) ? mv : 33.0;
        int limit = int.TryParse(Arg("-limit"), out var lv) ? lv : int.MaxValue;
        float old = Time.captureDeltaTime;
        Time.captureDeltaTime = Dt;
        // the modifiers off: the bones hold what the pose sources give; the same switches as the census otherwise
        yield return PCall(app, new { cmd = "app.options", ext = new { abmx = false, accessoryControls = false }, fk = new { hipsOffset = false }, correctionAllOn = true });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var total = new S0Stat();
        var ordered = cards.OrderBy(x => x.File, StringComparer.Ordinal).ToList();
        int measured = 0, failed = 0, unresolved = 0, identity = 0, withBones = 0, rowFails = 0, tDrift = 0, tChannels = 0;
        var sanity = new List<string>();
        // static tallies
        var heightPos = new List<string>();
        var heelCards = new HashSet<string>();
        var heelChan = new int[3]; var heelChanCards = S0Sets();
        var heelBones = new[] { S0Dict(), S0Dict(), S0Dict() };
        var heelCardRows = new List<string>();
        var hipsRows = new List<string>(); var hipsChan = new int[3]; var hipsCards = new HashSet<string>();
        var chainChan = new int[3]; var chainCards = S0Sets(); var chainBones = new[] { S0Dict(), S0Dict(), S0Dict() };
        var chainDiff = new int[3];
        var mosaic = new List<(string hash, string bone, int ch, double size, string value)>();
        var neck = new List<(string hash, double size, string value)>();
        var sample = new Dictionary<string, List<S0Mod>>();
        int sampleEvery = Math.Max(1, ordered.Count / 30);

        string Report()
        {
            var o = new StringBuilder();
            o.AppendLine($"FM0 census, real frames, headless play mode, UI off, every card built WITHOUT its modifiers. Order of every card: load, T-pose row, frames, physics and look off, then per row: the row at 5 times (normalised {string.Join(", ", Step0Times.Select(t => t.ToString("F1")))}), speed 0, back on the T-pose row.");
            o.Append(scan);
            o.AppendLine($"measured {measured} of {ordered.Count} cards in {sw.Elapsed.TotalMinutes:F1} min, {failed} failed to build; modifiers whose bone the card lacks {unresolved}; identity modifiers left out {identity}; cards that still had an applier with modifiers off {withBones} (must be 0); row commands refused {rowFails} (must be 0)");
            o.AppendLine("A modifier's channel DIFFERS: on that row, at one of the five times, the value without modifiers is over 1e-4 (0.01 deg) from its value on the T-pose row. There option A (base = this frame's input) and option B (base frozen on the T-pose row) give different values. MOVES: the value changes between the row's five times, so option B would freeze a motion. A channel that differs and does not move is a constant other than the T-pose's.");
            o.AppendLine("kind                 | with a value (cards) | differs on any row (cards) | moves on any row (cards) | size of the difference on the bone, among those that differ");
            for (int k = 0; k < 5; k++)
                o.AppendLine($"{Step0Kinds[k],-20} | {total.With[k],6} ({total.WithCards[k].Count,3})         | {total.AnyDiff[k],6} ({total.DiffCards[k].Count,3})               | {total.AnyMove[k],6} ({total.MoveCards[k].Count,3})             | {S0Quant(total.Shown[k])}{(k == 0 ? " deg" : "")}");
            o.AppendLine("per row: differs (cards) / moves (cards)");
            for (int row = 0; row < 4; row++)
                o.AppendLine($"  {ReconRows[row].name,-10} " + string.Join("; ", Enumerable.Range(0, 5).Select(k => $"{Step0Kinds[k]} {total.RowDiff[row, k]} ({total.RowDiffCards[row, k].Count}) / {total.RowMove[row, k]} ({total.RowMoveCards[row, k].Count})")));
            o.AppendLine("by bone, differs on any row:");
            for (int k = 0; k < 5; k++) o.AppendLine($"  {Step0Kinds[k]}: {S0Top(total.DiffBones[k], 30)}");
            o.AppendLine("by bone, moves on any row:");
            for (int k = 0; k < 5; k++) o.AppendLine($"  {Step0Kinds[k]}: {S0Top(total.MoveBones[k], 30)}");
            o.AppendLine($"T-pose row read again after a row, against the first read: {tDrift} of {tChannels} channels differ over the tolerance (what a row leaves behind on the T-pose row)");
            o.AppendLine("the check that the rows did run, first card, every body bone, channels differing from the T-pose row / moving between the times:");
            foreach (var s in sanity) o.AppendLine("  " + s);
            o.AppendLine($"(c) position values on cf_N_height: {heightPos.Count}");
            foreach (var s in heightPos) o.AppendLine("  " + s);
            o.AppendLine($"(d) cards wearing shoes with heel data (with the heel data found on the built card): {heelCards.Count}. Values on the six heel joints of those cards, by channel: rotation {heelChan[0]} on {heelChanCards[0].Count} cards, position or length {heelChan[1]} on {heelChanCards[1].Count} cards, scale {heelChan[2]} on {heelChanCards[2].Count} cards");
            string[] chn = { "rotation", "position or length", "scale" };
            for (int ch = 0; ch < 3; ch++) o.AppendLine($"  {chn[ch]}: {S0Top(heelBones[ch], 8)}");
            foreach (var s in heelCardRows) o.AppendLine("  " + s);
            o.AppendLine($"(e) values on cf_J_Hips: rotation {hipsChan[0]}, position or length {hipsChan[1]}, scale {hipsChan[2]}, on {hipsCards.Count} cards");
            foreach (var s in hipsRows) o.AppendLine("  " + s);
            o.AppendLine($"(f) values on chain bones (swing chains and bust chains of the built card): rotation {chainChan[0]} on {chainCards[0].Count} cards, position or length {chainChan[1]} on {chainCards[1].Count} cards, scale {chainChan[2]} on {chainCards[2].Count} cards; of them differing as above: rotation {chainDiff[0]}, position or length {chainDiff[1]}, scale {chainDiff[2]}");
            for (int ch = 0; ch < 3; ch++) o.AppendLine($"  {chn[ch]}: {S0Top(chainBones[ch], 20)}");
            o.AppendLine("(g) the largest modifiers on the mosaic's bones (cf_J_Kokan, cf_J_Kosi01, cf_J_Kosi02, cf_J_LegUp00_*, cf_J_LegUp01_s_*); size: scale and length as |value - 1|, position / 0.5, rotation / 45 deg:");
            var perGroup = mosaic.GroupBy(x => (x.bone.TrimEnd('L', 'R').TrimEnd('_'), x.ch)).Select(g => g.OrderByDescending(x => x.size).First()).OrderByDescending(x => x.size).ToList();
            foreach (var x in perGroup) o.AppendLine($"  {x.hash} {x.bone} {x.value} (size {x.size:F3})");
            var chosen = new List<string>();
            foreach (var x in perGroup) { if (chosen.Count >= 8) break; if (!chosen.Contains(x.hash)) chosen.Add(x.hash); }
            foreach (var x in mosaic.OrderByDescending(x => x.size)) { if (chosen.Count >= 8) break; if (!chosen.Contains(x.hash)) chosen.Add(x.hash); }
            o.AppendLine($"  modifiers on these bones {mosaic.Count} on {mosaic.Select(x => x.hash).Distinct().Count()} cards. FM20's cards (the largest per bone family and channel, then the largest overall, at most 8): {string.Join(" ", chosen)}");
            o.AppendLine("  the largest position values on cf_J_Neck (for the comparison's neck card):");
            foreach (var x in neck.OrderByDescending(x => x.size).Take(3)) o.AppendLine($"  {x.hash} {x.value}");
            o.Append(progress);
            return o.ToString();
        }
        void Flush() => File.WriteAllText(Step0Out, Report(), new UTF8Encoding(false));

        IEnumerator Sample(CharaRealization r, List<S0Mod> mods, int row, bool all, List<PoseBoneRow> body)
        {
            var (g, c, n, name) = ReconRows[row];
            var tBody = all ? body.Select(x => Local(x.Bone)).ToList() : null;
            var firstBody = (List<(Vector3 p, Quaternion r, Vector3 s)>)null;
            var dBody = all ? new bool[body.Count, 3] : null; var mBody = all ? new bool[body.Count, 3] : null;
            var first = new (Vector3 p, Quaternion r, Vector3 s)[mods.Count];
            for (int ti = 0; ti < Step0Times.Length; ti++)
            {
                yield return PCall(app, new { cmd = "chara.anime", key = pA, group = g, category = c, no = n, time = Step0Times[ti], speed = 0f });
                if (!POk) rowFails++;
                yield return PFrames(ti == 0 ? 3 : 2);
                for (int i = 0; i < mods.Count; i++)
                {
                    var m = mods[i];
                    if (!m.T) continue;
                    var v = Local(m.T);
                    if (ti == 0) first[i] = v;
                    if (ti == 2) m.At05[row] = v;
                    if (Angle(v.r, m.TPose.r) > ShowDeg) { m.Diff[row, 0] = true; m.Shown[0] = Math.Max(m.Shown[0], Angle(v.r, m.TPose.r)); }
                    double dp = Dist(v.p, m.TPose.p);
                    if (dp > ShowPos) { m.Diff[row, 1] = true; m.Shown[1] = Math.Max(m.Shown[1], dp * (m.HasLength ? Mathf.Abs(m.D.Length) : 1f)); }
                    var d = m.D.Scale;
                    double ds = Dist(v.s, m.TPose.s);
                    if (ds > ShowPos)
                    {
                        m.Diff[row, 2] = true;
                        m.Shown[2] = Math.Max(m.Shown[2], Math.Max(Math.Abs(v.s.x - m.TPose.s.x) * Mathf.Abs(d.x), Math.Max(Math.Abs(v.s.y - m.TPose.s.y) * Mathf.Abs(d.y), Math.Abs(v.s.z - m.TPose.s.z) * Mathf.Abs(d.z))));
                    }
                    if (Angle(v.r, first[i].r) > ShowDeg) m.Move[row, 0] = true;
                    if (Dist(v.p, first[i].p) > ShowPos) m.Move[row, 1] = true;
                    if (Dist(v.s, first[i].s) > ShowPos) m.Move[row, 2] = true;
                }
                if (all)
                {
                    var now = body.Select(x => Local(x.Bone)).ToList();
                    if (ti == 0) firstBody = now;
                    for (int i = 0; i < body.Count; i++)
                    {
                        if (Angle(now[i].r, tBody[i].r) > ShowDeg) dBody[i, 0] = true;
                        if (Dist(now[i].p, tBody[i].p) > ShowPos) dBody[i, 1] = true;
                        if (Dist(now[i].s, tBody[i].s) > ShowPos) dBody[i, 2] = true;
                        if (Angle(now[i].r, firstBody[i].r) > ShowDeg) mBody[i, 0] = true;
                        if (Dist(now[i].p, firstBody[i].p) > ShowPos) mBody[i, 1] = true;
                        if (Dist(now[i].s, firstBody[i].s) > ShowPos) mBody[i, 2] = true;
                    }
                }
            }
            if (all)
            {
                int C(bool[,] a, int ch) { int k = 0; for (int i = 0; i < body.Count; i++) if (a[i, ch]) k++; return k; }
                sanity.Add($"{name}: {body.Count} body bones; differ rotation {C(dBody, 0)} position {C(dBody, 1)} scale {C(dBody, 2)}; move rotation {C(mBody, 0)} position {C(mBody, 1)} scale {C(mBody, 2)}");
            }
        }

        foreach (var c in ordered)
        {
            if (measured >= limit || sw.Elapsed.TotalMinutes > minutes) break;
            yield return ReconPlayLoad(app, c.Rel, c.Sex, false);
            var r = pA == null ? null : PR(app, pA);
            if (r == null || r.State != RealizationState.Built) { failed++; progress.AppendLine($"   card {c.Hash}: did not build"); continue; }
            if (r.Context.Bones && r.Context.Bones.Modifiers.Count > 0) withBones++;
            var dict = AbmxApplier.BodyDictionary(r.Context);
            var mods = new List<S0Mod>();
            foreach (var m in c.List)
            {
                var d = m.Data;
                if (d.IsIdentity) { identity++; continue; }
                var t = S0Resolve(r, dict, m);
                if (!t) { unresolved++; continue; }
                mods.Add(new S0Mod { M = m, D = d, T = t, TPose = Local(t) });
            }
            // static tallies on the built card
            var foot = r.Motion ? r.Motion.Get<HS2Next.Motion.Heels.HeelsFoot>() : null;
            bool heel = foot?.State.Heel != null;
            if (heel) heelCards.Add(c.Hash);
            var joints = heel ? new HashSet<Transform>(foot.Joints.Where(x => x)) : new HashSet<Transform>();
            var chains = S0Chains(r);
            foreach (var m in mods)
            {
                string bone = m.M.BoneName;
                if (bone == "cf_N_height" && m.HasPosValue) heightPos.Add($"{c.Hash} {S0Value(m.D, 1)}");
                for (int ch = 0; ch < 3; ch++)
                {
                    if (!m.Has(ch)) continue;
                    if (joints.Contains(m.T))
                    {
                        heelChan[ch]++; heelChanCards[ch].Add(c.Hash);
                        heelBones[ch].TryGetValue(bone, out int n); heelBones[ch][bone] = n + 1;
                        heelCardRows.Add($"{c.Hash} {bone} {S0Value(m.D, ch)}");
                    }
                    if (bone == "cf_J_Hips")
                    {
                        hipsChan[ch]++; hipsCards.Add(c.Hash);
                        hipsRows.Add($"{c.Hash} {S0Value(m.D, ch)}");
                    }
                    if (chains.Contains(m.T))
                    {
                        chainChan[ch]++; chainCards[ch].Add(c.Hash);
                        chainBones[ch].TryGetValue(bone, out int n); chainBones[ch][bone] = n + 1;
                    }
                    if (S0Mosaic(bone)) mosaic.Add((c.Hash, bone, ch, S0MosaicSize(m.D, ch), S0Value(m.D, ch)));
                    if (bone == "cf_J_Neck" && ch == 1 && m.HasPosValue) neck.Add((c.Hash, m.D.Position.magnitude, S0Value(m.D, 1)));
                }
            }
            var body = measured == 0 ? AdvancedPoseResolver.Bones(r, "body") : null;
            for (int row = 0; row < 4; row++)
            {
                yield return Sample(r, mods, row, measured == 0, body);
                yield return PCall(app, new { cmd = "chara.anime", key = pA, group = 0, category = 0, no = 0, time = 0f, speed = 0f });
                if (!POk) rowFails++;
                yield return PFrames(3);
                foreach (var m in mods)
                {
                    var v = Local(m.T);
                    for (int ch = 0; ch < 3; ch++)
                    {
                        if (!m.Has(ch)) continue;
                        tChannels++;
                        if (ch == 0 ? Angle(v.r, m.TPose.r) > ShowDeg : ch == 1 ? Dist(v.p, m.TPose.p) > ShowPos : Dist(v.s, m.TPose.s) > ShowPos) tDrift++;
                    }
                }
            }
            foreach (var m in mods)
            {
                total.Add(c.Hash, m);
                if (chains.Contains(m.T)) for (int ch = 0; ch < 3; ch++) if (m.Has(ch) && (m.Diff[0, ch] || m.Diff[1, ch] || m.Diff[2, ch] || m.Diff[3, ch])) chainDiff[ch]++;
            }
            if (measured % sampleEvery == 0) sample[c.Hash] = mods;
            measured++;
            if (measured % 25 == 0)
            {
                Debug.Log($"FM0: {measured} cards, {sw.Elapsed.TotalMinutes:F1} min");
                Flush();
            }
        }

        // the sequence above against a fresh load per row: each row as the first bind after the T-pose row, on a sample
        int seqAll = 0, seqSame = 0;
        double seqWorstP = 0, seqWorstR = 0, seqWorstS = 0;
        var seqRow = new int[4]; var seqWrong = S0Dict();
        int sampled = 0;
        foreach (var c in ordered)
        {
            if (!sample.TryGetValue(c.Hash, out var ms) || sw.Elapsed.TotalMinutes > minutes + 2.5) continue;
            for (int row = 0; row < 4; row++)
            {
                yield return ReconPlayLoad(app, c.Rel, c.Sex, false);
                var r = pA == null ? null : PR(app, pA);
                if (r == null || r.State != RealizationState.Built) continue;
                var (g, cat, no, _) = ReconRows[row];
                yield return PCall(app, new { cmd = "chara.anime", key = pA, group = g, category = cat, no, time = 0.5f, speed = 0f });
                yield return PFrames(3);
                var dict = AbmxApplier.BodyDictionary(r.Context);
                foreach (var m in ms)
                {
                    var t = S0Resolve(r, dict, m.M);
                    if (!t) continue;
                    var v = Local(t); var w = m.At05[row];
                    double dr = Angle(v.r, w.r), dp = Dist(v.p, w.p), ds = Dist(v.s, w.s);
                    for (int ch = 0; ch < 3; ch++)
                    {
                        if (!m.Has(ch)) continue;
                        seqAll++;
                        double e = ch == 0 ? dr : ch == 1 ? dp : ds;
                        if (e <= (ch == 0 ? ShowDeg : ShowPos)) { seqSame++; continue; }
                        seqRow[row]++;
                        seqWrong.TryGetValue(m.M.BoneName, out int n); seqWrong[m.M.BoneName] = n + 1;
                    }
                    if (m.HasRot) seqWorstR = Math.Max(seqWorstR, dr);
                    if (m.HasPos) seqWorstP = Math.Max(seqWorstP, dp);
                    if (m.HasScale) seqWorstS = Math.Max(seqWorstS, ds);
                }
            }
            sampled++;
        }
        progress.AppendLine($"the sequence against a fresh load per row, {sampled} cards (every {sampleEvery}th), value at time 0.5: {seqSame} of {seqAll} channels equal within the tolerance; per row {string.Join(", ", Enumerable.Range(0, 4).Select(i => ReconRows[i].name + " " + seqRow[i]))}; worst rotation {seqWorstR:G3} deg, position {seqWorstP:G3}, scale {seqWorstS:G3}; bones: {S0Top(seqWrong, 12)}");
        Time.captureDeltaTime = old;
        yield return PCall(app, new { cmd = "scene.new" });
        Flush();
        Debug.Log("FM0 DONE: " + measured);
        PlayModeHarness.ExitCode = 0;
    }
}
