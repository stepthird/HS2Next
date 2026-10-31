// ModShaderS1Explain (Phase 5.2 package S, check ST5; headless): the S1 snapshots of chosen cards against their baselines
// (Cache/maker/baseline, redirect as the baselines were taken), every differing line classified into the two classes the
// substitute-shader tail may move:
//   shader.tail  a material line whose shader went from the URP/Lit fallback to a replacement of a ShaderMap "5.2" block entry,
//                or whose shader is such a replacement on both sides (the router's or the alias's inputs);
//   me.keywords  a material line that differs only in its keywords, every removed keyword outside the declared keywords of the
//                MaterialEditor plugin's own copy of a name mapped to that shader (MaterialEditorApplier.PluginCopies), none added;
// anything else is printed as "other" (with the line's differing tokens and their values before => after) and counted per card.
// Run: -executeMethod ModShaderS1Explain.Run [-only a.png,b.png] [-me] [-tail <suite stamp>] [-limit N]
//   default cards: the S1 visual targets + 20 sampled cards; -me adds every card whose MaterialEditor shader edits name a shader
//   ShaderMap replaces; -tail adds the cards whose import reports of that whole-suite run list an unfitted shader the 5.2 blocks
//   now map (Spike/out/tests/<stamp>/CardImport.*import_report.txt). Report: Spike/out/modshaders/mod_shader_s1explain_report.txt.
// Content rule: numbers and names only (snapshot lines); nothing is rendered.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HS2Next;
using HS2Next.Chara;
using HS2Next.Chara.Ext;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.Data.Ext;
using HS2Next.Tests.MakerCore;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class ModShaderS1Explain
{
    public const string ClassTail = "shader.tail", ClassMeKeywords = "me.keywords";

    static string Arg(string name, string def = null)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }
    static bool Flag(string name) => Environment.GetCommandLineArgs().Contains(name);
    static string Snap(string shaderName) => shaderName?.Replace(' ', '_');

    /// <summary>The replacements the 5.2 blocks map to (snapshot spelling: spaces as '_').</summary>
    public static HashSet<string> TailTargets()
    {
        var s = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in ModShaderTest.Entries()) if (e.Phase == "5.2") s.Add(Snap(e.Target));
        return s;
    }

    /// <summary>The "k=v" tokens of a snapshot line after its key (the first two '|' fields).</summary>
    public static Dictionary<string, string> Tokens(string line)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (line == null) return d;
        int p1 = line.IndexOf('|'); int p2 = p1 < 0 ? -1 : line.IndexOf('|', p1 + 1);
        string rest = p2 < 0 ? "" : line.Substring(p2 + 1);
        foreach (var t in rest.Split(' '))
        {
            int e = t.IndexOf('=');
            if (e > 0) d[t.Substring(0, e)] = t.Substring(e + 1);
        }
        return d;
    }

    /// <summary>The class of one differing snapshot line pair (null when it is neither of the tail's classes).</summary>
    public static string Classify(string key, string before, string after, HashSet<string> targets)
    {
        if (!key.StartsWith("mat|", StringComparison.Ordinal) || before == null || after == null) return null;
        var a = Tokens(before); var c = Tokens(after);
        a.TryGetValue("shader", out var sa); c.TryGetValue("shader", out var sc);
        if (sc != null && targets.Contains(sc) && (sa == "Universal_Render_Pipeline/Lit" || sa == sc)) return ClassTail;
        if (sa != null && sa == sc)
        {
            var diff = a.Keys.Union(c.Keys).Where(k => !(a.TryGetValue(k, out var x) && c.TryGetValue(k, out var y) && x == y)).ToList();
            if (diff.Count == 1 && diff[0] == "kw")
            {
                var ka = new HashSet<string>((a.TryGetValue("kw", out var x) ? x : "").Split(',').Where(k => k.Length > 0 && k != "-"), StringComparer.Ordinal);
                var kc = new HashSet<string>((c.TryGetValue("kw", out var y) ? y : "").Split(',').Where(k => k.Length > 0 && k != "-"), StringComparer.Ordinal);
                if (kc.IsSubsetOf(ka))
                {
                    var removed = ka.Except(kc).ToList();
                    foreach (var kv in MaterialEditorApplier.PluginCopies)
                        if (ShaderMap.Fitted.TryGetValue(kv.Key, out var r) && Snap(r) == sc && removed.All(k => Array.IndexOf(kv.Value, k) < 0)) return ClassMeKeywords;
                }
            }
        }
        return null;
    }

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

    static List<string> MeCards(List<string> all)
    {
        var res = new List<string>();
        foreach (var f in all)
        {
            MaterialEditorData me = null;
            try { me = CardExtensions.Decode(Card.Load(f)).MaterialEditor; } catch (Exception) { }
            if (me != null && me.Shaders.Any(s => s.ShaderName != null && (ShaderMap.Fitted.ContainsKey(s.ShaderName) || ShaderMap.CopyRoutes.ContainsKey(s.ShaderName)))) res.Add(f);
        }
        return res;
    }

    // the cards whose import reports list an unfitted shader the 5.2 blocks now map ("== <rel path>  ..." then "unfitted shaders: A xN, B xM")
    static List<string> TailCards(List<string> all, string stamp)
    {
        var names = new HashSet<string>(ModShaderTest.Entries().Where(e => e.Phase == "5.2").Select(e => e.Orig), StringComparer.Ordinal);
        string dir = Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "tests", stamp);
        var rels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(dir))
            foreach (var f in Directory.GetFiles(dir, "CardImport.*import_report.txt"))
            {
                string cur = null;
                foreach (var line in File.ReadLines(f))
                {
                    if (line.StartsWith("== ", StringComparison.Ordinal)) { var m = Regex.Match(line, @"^== (.+?\.png)\s"); cur = m.Success ? m.Groups[1].Value : null; continue; }
                    int i = line.IndexOf("unfitted shaders: ", StringComparison.Ordinal);
                    if (i < 0 || cur == null) continue;
                    foreach (Match m in Regex.Matches(line.Substring(i + 18), @"(?:^|, )(.+?) x\d+"))
                        if (names.Contains(m.Groups[1].Value)) rels.Add(cur.Replace('\\', '/'));
                }
            }
        return all.Where(f => rels.Any(r => f.Replace('\\', '/').EndsWith("/" + r, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    public static void Run()
    {
        var sw = Stopwatch.StartNew();
        var rep = new StringBuilder();
        int pass = 0, fail = 0;
        void Check(string what, bool ok, string detail = "") { if (ok) pass++; else fail++; rep.AppendLine($"   {(ok ? "PASS" : "FAIL")} {what}{(string.IsNullOrEmpty(detail) ? "" : " - " + detail)}"); }
        void Info(string s) => rep.AppendLine("   " + s);
        var all = MakerCoreTest.AllCards();
        List<string> cards;
        string only = Arg("-only");
        if (!string.IsNullOrEmpty(only))
        {
            var want = new HashSet<string>(only.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
            cards = all.Where(f => want.Contains(Path.GetFileName(f))).ToList();
        }
        else cards = MakerCoreTest.SampleCards();
        int sample = cards.Count, meN = 0, tailN = 0;
        if (Flag("-me")) { var me = MeCards(all); meN = me.Count; cards = cards.Concat(me).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); }
        string stamp = Arg("-tail");
        if (stamp != null) { var tc = TailCards(all, stamp); tailN = tc.Count; cards = cards.Concat(tc).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); }
        int limit = int.Parse(Arg("-limit", int.MaxValue.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        if (cards.Count > limit) cards = cards.Take(limit).ToList();
        var targets = TailTargets();
        var lines = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var perCard = new SortedDictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var others = new List<string>();
        int equal = 0, differ = 0, missing = 0, failed = 0, notChara = 0, n = 0;
        rep.AppendLine($"ModShaderS1Explain (Unity {Application.unityVersion}): {cards.Count} cards ({sample} {(only != null ? "named" : "targets + 20")}, -me {meN}, -tail {tailN}); baselines {MakerCoreTest.BaselineDir}; 5.2 targets {targets.Count}");
        try
        {
            using (var h = MakerCoreTest.Harness.Open())
            {
                foreach (var file in cards)
                {
                    n++;
                    if (n % 25 == 0) Debug.Log($"ModShaderS1Explain: {n}/{cards.Count}, {sw.Elapsed.TotalSeconds:F0} s");
                    MakerCoreTest.Built b = null;
                    try
                    {
                        b = h.Build(file);
                        if (b.NotCharacter) { notChara++; continue; }
                        if (!b.Ok) { failed++; Check(Path.GetFileName(file) + ": built", false, b.Error); continue; }
                        var basis = MakerCoreTest.LoadBaseline(b.CardSha);
                        if (basis == null) { missing++; continue; }
                        var s = b.Snapshot();
                        if (s.Sha256 == basis.Sha256) { equal++; continue; }
                        differ++;
                        string card = b.CardSha.Substring(0, 12);
                        var ka = Keyed(basis.Lines); var kc = Keyed(s.Lines);
                        foreach (var kv in ka.Concat(kc.Where(x => !ka.ContainsKey(x.Key))))
                        {
                            ka.TryGetValue(kv.Key, out var la); kc.TryGetValue(kv.Key, out var lc);
                            if (la == lc) continue;
                            string klass = Classify(kv.Key, la, lc, targets) ?? "other";
                            lines[klass] = (lines.TryGetValue(klass, out var c) ? c : 0) + 1;
                            if (!perCard.TryGetValue(klass, out var set)) perCard[klass] = set = new HashSet<string>(StringComparer.Ordinal);
                            set.Add(card);
                            if (klass == "other" && others.Count < 240)
                            {
                                var ta = Tokens(la); var tc2 = Tokens(lc);
                                var dk = ta.Keys.Union(tc2.Keys).Where(k => !(ta.TryGetValue(k, out var x) && tc2.TryGetValue(k, out var y) && x == y)).Take(8);
                                others.Add($"{card} {kv.Key.Substring(0, Math.Min(140, kv.Key.Length))}: tokens [{string.Join(",", dk)}]{(la == null ? " (new line)" : lc == null ? " (gone)" : "")}");
                                // the values behind it: each differing token before => after, or the new / gone line's value
                                string Cut(string v) => v == null ? "-" : v.Length > 160 ? v.Substring(0, 160) + "..." : v;
                                if (la == null || lc == null) others.Add("    " + Cut((la ?? lc).Substring(Math.Min(kv.Key.Length, (la ?? lc).Length)).TrimStart('|')));
                                else foreach (var k in dk) others.Add($"    {k}: {Cut(ta.TryGetValue(k, out var x) ? x : null)} => {Cut(tc2.TryGetValue(k, out var y) ? y : null)}");
                            }
                        }
                    }
                    catch (Exception e) { failed++; Check(Path.GetFileName(file) + ": no exception", false, e.GetType().Name + ": " + e.Message); }
                    finally { b?.Dispose(); }
                    if (n % 10 == 0) h.ReleaseBundles();
                }
            }
        }
        catch (Exception e) { Check("no exception", false, e.GetType().Name + ": " + e.Message); }
        Info($"{equal} equal, {differ} differ, {missing} without a baseline, {failed} failed, {notChara} not character cards");
        foreach (var kv in lines) Info($"class {kv.Key}: {kv.Value} lines on {perCard[kv.Key].Count} cards ({string.Join(", ", perCard[kv.Key].Take(12))})");
        foreach (var o in others) Info("other: " + o);
        Check("every differing line is shader.tail or me.keywords (0 other)", failed == 0 && !lines.ContainsKey("other"), $"other {(lines.TryGetValue("other", out var on) ? on : 0)} lines");
        string dir = Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "modshaders");
        Directory.CreateDirectory(dir);
        rep.Insert(0, $"SUMMARY: {pass} PASS, {fail} FAIL\nMOD SHADER S1 EXPLAIN ({sw.Elapsed.TotalSeconds:F0} s)\n");
        string path = Path.Combine(dir, "mod_shader_s1explain_report.txt");
        File.WriteAllText(path, rep.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        Debug.Log($"MOD SHADER S1 EXPLAIN: {pass} PASS, {fail} FAIL -> {path}");
        EditorApplication.Exit(fail > 0 ? 1 : 0);
    }
}
