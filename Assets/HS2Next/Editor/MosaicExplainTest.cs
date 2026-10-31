// The S1 explain of Phase 5.M (the mosaic set, the release policy; integrator T), headless: S1's build path on the cards
// against the stored baselines, every line that differs classified; any other line FAILs. The check before the one S1
// re-take of 5.M (with re-taken baselines every card is equal).
//   mosaic.material      a material line of a mosaic-set member whose shader is now the mosaic shader (the slot drew the
//                        earlier flat replacement, or the renderer carries a member name)
//   mosaic.mesh          a renderer line of a member whose only difference is the drawn mesh, now its "#bodyuv" copy (the
//                        female patches drawn at the body's UVs)
//   policy.meMosaicSkip  a member's renderer line whose difference is the enabled flag (with or without the mesh copy):
//                        MaterialEditor items on members are kept in the documents but not applied; and the summary line
//                        whose MaterialEditor part now counts such skipped items
//   policy.extSummary    the extension summary line whose only difference is the build-hook text in place of the
//                        replaced-body text
//   policy.uncensorOff   every other line of a card whose baseline built a replaced body from card data (body and part
//                        meshes, bones, implanted bones, skin creator layers, the context lines, the summary); and the
//                        missing-content records of a card whose replaced body is not installed: that record is gone, the
//                        others are unchanged in order (their indices shift)
//   other                anything else (FAIL; printed with the differing tokens)
// Card sets as MakerCoreTest: default the visual targets + 20 sampled cards, -targets, -all, -only <file names>, -limit N.
// Run: -executeMethod MosaicExplainTest.RunS1Explain [-all] [-other]. Report: Spike/out/maker/mosaic_s1explain_report.txt
// (first line "SUMMARY: N PASS, M FAIL"); per card and class: mosaic_s1explain_cards.tsv beside it. Numbers and names only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HS2Next.Chara.Mosaic;
using HS2Next.Tests.MakerCore;
using UnityEngine;
using static MakerTestKit;
using Debug = UnityEngine.Debug;

public static class MosaicExplainTest
{
    public const string Material = "mosaic.material", Mesh = "mosaic.mesh", MeSkip = "policy.meMosaicSkip",
        ExtSummary = "policy.extSummary", ReplacedOff = "policy.uncensorOff", Other = "other";

    static readonly string[] ClassOrder = { Material, Mesh, MeSkip, ExtSummary, ReplacedOff, Other };

    // the replaced-body part of the extension summary (spaces are written as '_' in the snapshot text) and its successor
    static readonly Regex ReplacedText = new Regex("_uncensor_[^;]*;_", RegexOptions.CultureInvariant);
    const string HooksText = "_hooks_-;_";
    const string MosaicShader = "HS2Next/Mosaic";
    // the snapshot writes shader names with '_' for spaces: the flat replacement before 5.M
    const string LegacyShader = "HS2Next/AIT_mnpb";

    sealed class Classes
    {
        public readonly SortedDictionary<string, int> Lines = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, HashSet<string>> Cards = new SortedDictionary<string, HashSet<string>>(StringComparer.Ordinal);
        public readonly SortedDictionary<string, int> Sections = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public void Add(string klass, string card, string section)
        {
            Lines[klass] = (Lines.TryGetValue(klass, out var n) ? n : 0) + 1;
            if (!Cards.TryGetValue(klass, out var s)) Cards[klass] = s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            s.Add(card);
            string k = klass + " " + section;
            Sections[k] = (Sections.TryGetValue(k, out var m) ? m : 0) + 1;
        }
        public int LinesOf(string k) => Lines.TryGetValue(k, out var n) ? n : 0;
        public int CardsOf(string k) => Cards.TryGetValue(k, out var s) ? s.Count : 0;
    }

    public static void RunS1Explain()
    {
        Reset();
        var sw = Stopwatch.StartNew();
        List<string> cards; string what;
        string only = Arg("-only");
        if (!string.IsNullOrEmpty(only))
        {
            var names = new HashSet<string>(only.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
            cards = MakerCoreTest.AllCards().Where(f => names.Contains(Path.GetFileName(f))).ToList();
            what = "the cards of -only (" + names.Count + " named)";
        }
        else if (Flag("-all")) { cards = MakerCoreTest.AllCards(); what = "every card"; }
        else if (Flag("-targets")) { cards = TestTargets.Load().VisualCards.Where(File.Exists).ToList(); what = "the visual targets"; }
        else { cards = MakerCoreTest.SampleCards(); what = "the visual targets + 20 sampled cards"; }
        int limit = int.Parse(Arg("-limit", int.MaxValue.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        if (cards.Count > limit) cards = cards.Take(limit).ToList();
        var cls = new Classes();
        var others = new List<string>();
        var perCard = new List<string>();
        int equal = 0, differ = 0, missing = 0, failed = 0, notChara = 0, n = 0, replaced = 0, notInstalled = 0;
        try
        {
            using (var h = MakerCoreTest.Harness.Open())
            {
                Rep.AppendLine($"5.M S1 explain (Unity {Application.unityVersion}): {cards.Count} files, {what}; baselines {MakerCoreTest.BaselineDir}");
                foreach (var file in cards)
                {
                    n++;
                    if (n % 25 == 0) Debug.Log($"MosaicExplainTest S1 explain: {n}/{cards.Count}, {sw.Elapsed.TotalSeconds:F0} s");
                    MakerCoreTest.Built b = null;
                    try
                    {
                        b = h.Build(file);
                        if (b.NotCharacter) { notChara++; continue; }
                        if (!b.Ok) { failed++; Check(Path.GetFileName(file) + ": built", false, b.Error); continue; }
                        var basis = MakerCoreTest.LoadBaseline(b.CardSha);
                        if (basis == null) { missing++; Info(Path.GetFileName(file) + ": no baseline"); continue; }
                        var s = b.Snapshot();
                        if (s.Sha256 == basis.Sha256) { equal++; continue; }
                        differ++;
                        var one = Classify(b.Rel ?? Path.GetFileName(file), basis.Lines, s.Lines, cls, others, out bool rep, out bool ni);
                        if (rep) replaced++;
                        if (ni) notInstalled++;
                        perCard.Add((b.Rel ?? Path.GetFileName(file)) + "\t" + b.CardSha + "\t" + string.Join(",", one.Select(kv => kv.Key + ":" + kv.Value)));
                    }
                    catch (Exception e) { failed++; Check(Path.GetFileName(file) + ": no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace)); }
                    finally { b?.Dispose(); }
                    if (n % 10 == 0) h.ReleaseBundles();
                }
            }
        }
        catch (Exception e) { Check("S1 explain: no exception", false, e.GetType().Name + ": " + e.Message + " " + Short(e.StackTrace)); }
        Info($"{equal} equal, {differ} differ, {missing} without a baseline, {failed} failed, {notChara} not character cards");
        Info($"cards whose baseline built a replaced body: {replaced}; cards whose replaced body is not installed: {notInstalled}");
        foreach (var k in ClassOrder) Info($"class {k}: {cls.LinesOf(k)} lines on {cls.CardsOf(k)} cards");
        foreach (var kv in cls.Sections) Info($"  {kv.Key}: {kv.Value} lines");
        foreach (var o in Flag("-other") ? others : others.Take(40)) Info("other: " + o);
        try
        {
            Directory.CreateDirectory(OutDir);
            File.WriteAllLines(Path.Combine(OutDir, "mosaic_s1explain_cards.tsv"),
                new[] { "# card (relative to UserData)\tcard sha256\tclass:lines,..." }.Concat(perCard), new UTF8Encoding(false));
        }
        catch (Exception e) { Info("cards file not written: " + e.Message); }
        Check("S1 explain: no build failure, every card has a baseline", failed == 0 && missing == 0, $"failed {failed}, without a baseline {missing}");
        Check("S1 explain: every differing line is classified (mosaic.material, mosaic.mesh, policy.meMosaicSkip, policy.extSummary, policy.uncensorOff); 0 other",
              cls.LinesOf(Other) == 0, $"other {cls.LinesOf(Other)} lines on {cls.CardsOf(Other)} cards");
        Finish("mosaic_s1explain_report.txt", "MosaicExplainTest.RunS1Explain (5.M S1 classes)", sw);
    }

    /// <summary>Classifies the differing lines of one card (baseline lines <paramref name="before"/>, the build's lines
    /// <paramref name="after"/>); returns the lines per class of this card.</summary>
    static SortedDictionary<string, int> Classify(string card, List<string> before, List<string> after, Classes cls, List<string> others,
                                                  out bool replacedCard, out bool notInstalledCard)
    {
        var a = Keyed(before); var c = Keyed(after);
        // the card facts from the baseline: a replaced body was built from card data; a replaced body named by the card is
        // not installed (its missing-content record)
        replacedCard = a.TryGetValue("ctx|uncensor", out var u) && Value(u) != "null";
        var missA = a.Where(kv => kv.Key.StartsWith("ctx|missing[", StringComparison.Ordinal)).Select(kv => Value(kv.Value)).ToList();
        var missC = c.Where(kv => kv.Key.StartsWith("ctx|missing[", StringComparison.Ordinal)).Select(kv => Value(kv.Value)).ToList();
        notInstalledCard = missA.Any(IsReplacedMissing);
        // the missing records are explained when exactly the replaced-body records are gone and the rest keep their order
        bool missingExplained = notInstalledCard && missA.Where(m => !IsReplacedMissing(m)).SequenceEqual(missC);

        var mine = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in a.Keys.Concat(c.Keys.Where(k => !a.ContainsKey(k))))
        {
            a.TryGetValue(key, out var la); c.TryGetValue(key, out var lc);
            if (la == lc) continue;
            string section = key.Substring(0, Math.Max(0, key.IndexOf('|')));
            string klass = LineClass(key, section, la, lc, replacedCard, missingExplained);
            if (klass == Other)
                others.Add($"{card} {Short(key, 160)}: " + (la != null && lc != null ? "tokens [" + string.Join(",", TokenNames(Value(la), Value(lc))) + "] " : "") +
                           $"{Short(la, 200)} <> {Short(lc, 200)}");
            cls.Add(klass, card, section);
            mine[klass] = (mine.TryGetValue(klass, out var m) ? m : 0) + 1;
        }
        return mine;
    }

    static string LineClass(string key, string section, string la, string lc, bool replacedCard, bool missingExplained)
    {
        string name = Leaf(key);
        if (section == "mat" && lc != null && Token(Value(lc), "shader") == MosaicShader)
        {
            string was = la != null ? Token(Value(la), "shader") : null;
            if (was == LegacyShader || MosaicSet.IsMemberName(name)) return Material;
        }
        if (section == "rend" && la != null && lc != null && MosaicSet.IsMemberName(name))
        {
            var t = TokenNames(Value(la), Value(lc));
            string mesh = Token(Value(lc), "mesh") ?? "";
            if (t.Count > 0 && t.All(x => x == "mesh") && mesh.Contains("#bodyuv")) return Mesh;
            if (t.Contains("enabled") && t.All(x => x == "enabled" || x == "mesh")) return MeSkip;
        }
        if (key == "ctx|ext" && la != null && lc != null)
        {
            string was = ReplacedText.Replace(Value(la), HooksText), now = Value(lc);
            if (was == now) return replacedCard ? ReplacedOff : ExtSummary;
            // only the MaterialEditor part differs and it now counts items skipped on members
            if (now.Contains(MosaicSet.MeSkipReason) && !was.Contains(MosaicSet.MeSkipReason) &&
                WithoutMe(was) == WithoutMe(now)) return MeSkip;
        }
        if (missingExplained && key.StartsWith("ctx|missing[", StringComparison.Ordinal)) return ReplacedOff;
        if (replacedCard) return ReplacedOff;
        return Other;
    }

    static bool IsReplacedMissing(string value) => value.StartsWith("uncensor.body", StringComparison.Ordinal);

    // the summary without its MaterialEditor segment ("; me <applied>/<items> (...) ..." up to the next "; ")
    static string WithoutMe(string summary) =>
        string.Join(";_", summary.Split(new[] { ";_" }, StringSplitOptions.None).Where(p => !p.StartsWith("me_", StringComparison.Ordinal)));

    // ---- snapshot lines: "<section>|<key>|<values>", keyed by section and key (the k-th repeat as "#k")

    static Dictionary<string, string> Keyed(List<string> lines)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        var n = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var x in lines)
        {
            int p1 = x.IndexOf('|'); int p2 = p1 < 0 ? -1 : x.IndexOf('|', p1 + 1);
            string k = p2 < 0 ? x : x.Substring(0, p2);
            int c = n.TryGetValue(k, out var v) ? v + 1 : 0;
            n[k] = c;
            d[c == 0 ? k : k + "#" + c] = x;
        }
        return d;
    }

    static string Value(string line)
    {
        if (line == null) return null;
        int p1 = line.IndexOf('|'); int p2 = p1 < 0 ? -1 : line.IndexOf('|', p1 + 1);
        return p2 < 0 ? "" : line.Substring(p2 + 1);
    }

    // the object name of a key "<section>|a/b/name[slot]#k"
    static string Leaf(string key)
    {
        int p = key.IndexOf('|');
        string k = p < 0 ? key : key.Substring(p + 1);
        int hash = k.LastIndexOf('#'); if (hash > 0 && k.Substring(hash + 1).All(char.IsDigit)) k = k.Substring(0, hash);
        if (k.EndsWith("]", StringComparison.Ordinal)) { int br = k.LastIndexOf('['); if (br > 0) k = k.Substring(0, br); }
        int slash = k.LastIndexOf('/');
        return slash < 0 ? k : k.Substring(slash + 1);
    }

    // "name=value" tokens split on spaces outside parentheses; a token without '=' is its own name
    static Dictionary<string, string> Tokens(string s)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (s == null) return d;
        int depth = 0, start = 0;
        for (int i = 0; i <= s.Length; i++)
        {
            char ch = i < s.Length ? s[i] : ' ';
            if (ch == '(') depth++;
            else if (ch == ')') depth = Math.Max(0, depth - 1);
            else if (ch == ' ' && depth == 0)
            {
                if (i > start) { string t = s.Substring(start, i - start); int e = t.IndexOf('='); d[e > 0 ? t.Substring(0, e) : t] = e > 0 ? t.Substring(e + 1) : t; }
                start = i + 1;
            }
        }
        return d;
    }

    static string Token(string s, string name) => Tokens(s).TryGetValue(name, out var v) ? v : null;

    static List<string> TokenNames(string a, string b)
    {
        var ta = Tokens(a); var tb = Tokens(b);
        var l = new List<string>();
        foreach (var k in ta.Keys.Union(tb.Keys))
        {
            ta.TryGetValue(k, out var x); tb.TryGetValue(k, out var y);
            if (x != y) l.Add(k);
        }
        return l;
    }
}
