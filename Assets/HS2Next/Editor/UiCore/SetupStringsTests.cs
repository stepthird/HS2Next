// FR9 (Phase 5.3 package F), plain Mono: Tools/mono_tests.ps1 -Suite ui (fixtures = Assets/HS2Next/Resources/UI).
//   SetupStringsTests  every key of the string table's block #F (the first-run guide, the folder picker, the settings sections)
//                      has en and ja; zh-Hans is present and marked zh? until reviewed; the guide's language list keeps its
//                      machine-translation label for zh-Hans; the keys are unique and the placeholders match
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace HS2Next.UI.Core.Tests
{
    using HS2Next.Save.Tests;

    public static class SetupStringsTests
    {
        public static void Run(TestRunner t)
        {
            string file = Path.Combine(t.FixturesDir ?? "", "L10n", "strings.tsv");
            t.Check(File.Exists(file), "strings.tsv exists");
            if (!File.Exists(file)) return;
            string text = File.ReadAllText(file, Encoding.UTF8);
            var table = L10nTable.Parse(text);
            var rows = new List<string[]>();
            bool inF = false;
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                if (raw.StartsWith("#", StringComparison.Ordinal))
                {
                    if (raw.StartsWith("#F ", StringComparison.Ordinal) || raw == "#F") inF = true;
                    else if (raw.Length > 1 && char.IsUpper(raw[1])) inF = false;
                    continue;
                }
                if (inF && raw.Length > 0) rows.Add(raw.Split('\t'));
            }
            t.Check(rows.Count >= 80, "block #F holds the guide's keys (" + rows.Count + ")");
            var noEn = rows.Where(r => r.Length < 2 || r[1].Length == 0).Select(r => r[0]).ToList();
            var noJa = rows.Where(r => r.Length < 3 || r[2].Length == 0).Select(r => r[0]).ToList();
            var noZh = rows.Where(r => r.Length < 4 || r[3].Length == 0).Select(r => r[0]).ToList();
            var unmarked = rows.Where(r => r.Length >= 4 && r[3].Length > 0 && !table.Unreviewed(r[0], "zh-Hans") && !(r.Length >= 5 && r[4].Contains("reviewed"))).Select(r => r[0]).ToList();
            t.Check(noEn.Count == 0, "every #F key has en" + (noEn.Count > 0 ? ": " + string.Join(", ", noEn) : ""));
            t.Check(noJa.Count == 0, "every #F key has ja" + (noJa.Count > 0 ? ": " + string.Join(", ", noJa) : ""));
            t.Check(noZh.Count == 0, "every #F key has zh-Hans" + (noZh.Count > 0 ? ": " + string.Join(", ", noZh) : ""));
            t.Check(unmarked.Count == 0, "every #F zh-Hans cell is marked zh? (machine translation)" + (unmarked.Count > 0 ? ": " + string.Join(", ", unmarked) : ""));
            var keys = rows.Select(r => r[0]).ToList();
            var dupes = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            t.Check(dupes.Count == 0 && table.Problems.Count == 0, "the keys are unique in the table" + (dupes.Count + table.Problems.Count > 0 ? ": " + string.Join("; ", dupes.Concat(table.Problems)) : ""));
            var mism = new HashSet<string>(table.PlaceholderMismatches().Select(x => x.ToString()));
            var mine = keys.Where(k => mism.Any(m => m.Contains(k))).ToList();
            t.Check(mine.Count == 0, "the #F placeholders match across the languages" + (mine.Count > 0 ? ": " + string.Join(", ", mine) : ""));
            string zhLabel = table.Cell("lang.zh-Hans", "en") ?? "";
            t.Check(zhLabel.IndexOf("machine", StringComparison.OrdinalIgnoreCase) >= 0, "the language list labels zh-Hans as machine translated (" + zhLabel + ")");
            foreach (var k in new[] { "setup.title.welcome", "setup.title.files", "setup.title.hs2", "setup.title.mods", "setup.title.cache", "setup.title.index", "setup.title.ready",
                                      "setup.nav.next", "setup.nav.back", "setup.nav.skip", "setup.nav.quit", "setup.nav.finish", "setup.ready.tipCamera", "setup.skip.message" })
                t.Check(keys.Contains(k), "block #F has " + k);
            t.Info("#F: " + rows.Count + " keys");
        }
    }
}
