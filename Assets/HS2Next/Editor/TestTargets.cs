// Test targets of the unattended suite (Tools/targets.json; Tools/README_tests.md): the SFW visual cards (female, male),
// the animation rows, expression and look cases of the motion tests, the card directories of the full import and the
// cards a graphics run skips. Card paths in the file are relative to the game's UserData folder (game root from
// Config/settings.local.json via CardResolveTest.GameRoot); nothing machine-specific is stored in the file.
// Load(): the parsed file. Run: -executeMethod TestTargets.Run checks the file against the installation: visual cards
// exist and load with the expected sex, animation rows resolve to vanilla Anime rows with IsH == false and the listed
// clip name, card directories exist, skip entries exist; writes the skip files for CardImportTest (relative to each
// card directory) to Spike/out/tests/skip_<female|male>.txt.
// Report: Spike/out/tests/targets_report.txt (PASS / FAIL lines); EditorApplication.Exit(1) on any FAIL.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.IO;
using HS2Next.Motion.Animation;
using UnityEditor;
using UnityEngine;

[Serializable]
public class TestTargets
{
    [Serializable] public class Visual { public string female, male; }
    [Serializable] public class AnimRow { public int group, category, no; public string name, pose; }
    [Serializable] public class Cards { public string female, male; }

    public int version;
    public Visual visual;
    public AnimRow[] animation;
    public string[] expression, look;
    public Cards cards;
    /// <summary>Cards (relative to UserData) that a graphics run does not build: they crash the device even with the texture guard.</summary>
    public string[] skip;

    public static string FilePath => Path.Combine(ContentSettings.ProjectRoot, "Tools", "targets.json");
    public static string UserData => Path.Combine(CardResolveTest.GameRoot, "UserData");
    public static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "tests");

    /// <summary>Tools/targets.json (or <paramref name="file"/>), parsed.</summary>
    public static TestTargets Load(string file = null)
    {
        var t = JsonUtility.FromJson<TestTargets>(File.ReadAllText(file ?? FilePath));
        t.animation ??= Array.Empty<AnimRow>(); t.expression ??= Array.Empty<string>(); t.look ??= Array.Empty<string>(); t.skip ??= Array.Empty<string>();
        return t;
    }

    /// <summary>Absolute path of a UserData-relative path.</summary>
    public static string Abs(string rel) => Path.Combine(UserData, rel.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The visual target cards (female, male) as absolute paths.</summary>
    public string[] VisualCards => new[] { visual?.female, visual?.male }.Where(s => !string.IsNullOrEmpty(s)).Select(Abs).ToArray();

    /// <summary>Skip entries under the card directory <paramref name="cardsRel"/>, relative to it (CardImportTest -skip format).</summary>
    public IEnumerable<string> SkipUnder(string cardsRel)
    {
        string pre = cardsRel.TrimEnd('/') + "/";
        return skip.Where(s => s.StartsWith(pre, StringComparison.Ordinal)).Select(s => s.Substring(pre.Length));
    }

    // ------------------------------------------------------------------ check

    public static void Run()
    {
        var rep = new StringBuilder();
        int fails = 0;
        void Check(string name, bool ok, string detail = "")
        {
            if (!ok) fails++;
            rep.AppendLine($"{(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? "  (" + detail + ")" : "")}");
        }
        Directory.CreateDirectory(OutDir);
        TestTargets t = null;
        try { t = Load(); } catch (Exception e) { Check("Tools/targets.json parses", false, e.Message); }
        if (t != null)
        {
            Check("Tools/targets.json parses", true, $"version {t.version}");
            foreach (var (label, rel, sex) in new[] { ("female", t.visual?.female, 1), ("male", t.visual?.male, 0) })
            {
                bool exists = !string.IsNullOrEmpty(rel) && File.Exists(Abs(rel));
                Check($"visual {label} card exists: {rel}", exists);
                if (!exists) continue;
                try
                {
                    var c = Card.Load(Abs(rel));
                    int s = c.Parameter.Get("sex").Int(1);
                    Check($"visual {label} card loads with sex {sex}", s == sex, $"sex {s}");
                }
                catch (Exception e) { Check($"visual {label} card loads", false, e.Message); }
            }
            var gd = CardResolveTest.BuildDb(null);
            var db = gd.LoadStudioLists();
            foreach (var a in t.animation)
            {
                var r = CharaAnimation.Resolve(db, ContentIds.Vanilla, a.group, a.category, a.no);
                Check($"animation {a.group}/{a.category}/{a.no} {a.name}: vanilla Anime row, IsH false, clip {a.name}",
                      r != null && !r.IsH && (string.IsNullOrEmpty(a.name) || r.Clip == a.name),
                      r == null ? "not found" : $"IsH {r.IsH}, clip {r.Clip}");
            }
            Check("expression cases listed", t.expression.Length > 0, string.Join(" ", t.expression));
            Check("look cases listed", t.look.Length > 0, string.Join(" ", t.look));
            foreach (var (label, rel) in new[] { ("female", t.cards?.female), ("male", t.cards?.male) })
            {
                bool ok = !string.IsNullOrEmpty(rel) && Directory.Exists(Abs(rel));
                Check($"card directory {label} exists: {rel}", ok, ok ? $"{Directory.GetFiles(Abs(rel), "*.png", SearchOption.AllDirectories).Length} PNG files" : "");
                if (!ok) continue;
                var sk = t.SkipUnder(rel).ToArray();
                File.WriteAllLines(Path.Combine(OutDir, $"skip_{label}.txt"), sk);
                foreach (var s in sk) Check($"skip entry exists: {rel}/{s}", File.Exists(Path.Combine(Abs(rel), s)));
            }
            Check("every skip entry lies under a card directory", t.skip.All(s => (t.cards?.female != null && s.StartsWith(t.cards.female + "/")) || (t.cards?.male != null && s.StartsWith(t.cards.male + "/"))));
            Check("no absolute paths in targets.json", new[] { t.visual?.female, t.visual?.male, t.cards?.female, t.cards?.male }.Concat(t.skip)
                .All(p => string.IsNullOrEmpty(p) || !(Path.IsPathRooted(p) || p.Contains(":"))));
            gd.UnloadAll();
        }
        rep.Insert(0, $"test targets: {(fails == 0 ? "PASS" : $"FAIL ({fails} failed)")}\n");
        File.WriteAllText(Path.Combine(OutDir, "targets_report.txt"), rep.ToString());
        Debug.Log("TEST TARGETS DONE: " + (fails == 0 ? "PASS" : "FAIL"));
        if (fails > 0) TestExit.Exit(1);
    }
}
