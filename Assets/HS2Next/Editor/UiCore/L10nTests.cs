// L10nTests: TSV parse (BOM, tabs, quotes, comments, escapes); every key has en;
// placeholders consistent across languages; fallback chain; report counts of missing / unreviewed ja and zh-Hans; every
// manifest action and option has its label key.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HS2Next.UI.Core.Tests
{
    using HS2Next.Save.Tests;
    using HS2Next.UI.Core;

    public static class L10nTests
    {
        public static void Run(TestRunner t)
        {
            Parse(t);
            Fallback(t);
            File(t);
        }

        static void Parse(TestRunner t)
        {
            string tsv = "﻿# comment row\r\n" +
                         "key\ten\tja\tzh-Hans\tnote\r\n" +
                         "#P\n" +
                         "a.plain\tHello\tこんにちは\t你好\tzh?\n" +
                         "\n" +
                         "a.quoted\t\"say \"\"hi\"\"\"\t\"\"\t\tja?\n" +
                         "a.escapes\tline1\\nline2\\ttab\\\\back\t\t\t\n" +
                         "a.fmt\t{0} of {1}\t{1} 件中 {0} 件\t{0}/{1}\t\n" +
                         "a.short\tOnly en\n" +
                         "a.plain\tDuplicate\t\t\t\n" +
                         "#C\n" +
                         "c.key\tFrom C\tC から\t\tzh? reviewed later\n";
            var tb = L10nTable.Parse(tsv);
            t.Equal(5 + 1, tb.Count, "parse: 6 keys (comments, blank and duplicate rows skipped)");
            t.Check(tb.Problems.Count == 1 && tb.Problems[0].Contains("duplicate key a.plain"), "parse: the duplicate is reported (" + string.Join("; ", tb.Problems) + ")");
            t.Equal("Hello", tb.Cell("a.plain", "en"), "parse: BOM skipped, first row wins");
            t.Equal("say \"hi\"", tb.Cell("a.quoted", "en"), "parse: quoted cell unquoted");
            t.Equal(null, tb.Cell("a.quoted", "ja"), "parse: \"\" = empty = missing");
            t.Equal("line1\nline2\ttab\\back", tb.Cell("a.escapes", "en"), "parse: escapes");
            t.Equal(null, tb.Cell("a.short", "ja"), "parse: short row = missing cells");
            t.Check(tb.Unreviewed("a.plain", "zh-Hans") && !tb.Unreviewed("a.plain", "ja"), "parse: zh? flags zh-Hans only");
            t.Check(tb.Unreviewed("a.quoted", "ja"), "parse: ja? flags ja");
            t.Equal("From C", tb.Cell("c.key", "en"), "parse: rows of later blocks");
            t.Check(new[] { "en", "ja", "zh-Hans" }.SequenceEqual(tb.Languages), "parse: language columns");
            t.Equal(0, tb.PlaceholderMismatches().Count, "parse: placeholders consistent");
            var bad = L10nTable.Parse("key\ten\tja\nx\t{0} items\t件数\n");
            t.Equal(1, bad.PlaceholderMismatches().Count, "parse: a missing placeholder is found");
            var noEn = L10nTable.Parse("key\tja\nx\ty\n");
            t.Check(noEn.Problems.Count > 0, "parse: a header without en is a problem");
        }

        static void Fallback(TestRunner t)
        {
            var tb = L10nTable.Parse("key\ten\tja\tzh-Hans\tnote\n" +
                                     "k.all\tAll\tすべて\t全部\t\n" +
                                     "k.enOnly\tEnglish only\t\t\t\n" +
                                     "k.fmt\t{0} of {1}\t{1} 件中 {0} 件\t\t\n");
            var l = new Localizer();
            var missing = new List<string>();
            l.Missing += (k, lang) => missing.Add(lang + ":" + k);
            string changed = null;
            l.LanguageChanged += lang => changed = lang;
            l.Load(tb);
            t.Equal("All", l.Get("k.all"), "fallback: en by default");
            t.Equal("ja", l.SetLanguage("ja-JP"), "fallback: ja-JP → ja");
            t.Equal("ja", changed, "fallback: LanguageChanged raised");
            t.Equal("すべて", l.Get("k.all"), "fallback: ja cell");
            t.Equal("English only", l.Get("k.enOnly"), "fallback: missing ja → en");
            t.Equal("k.unknown", l.Get("k.unknown"), "fallback: unknown key → the key");
            l.Get("k.enOnly");
            l.Get("k.unknown");
            t.Check(missing.SequenceEqual(new[] { "ja:k.enOnly", "ja:k.unknown" }), "fallback: each missing key reported once (" + string.Join(", ", missing) + ")");
            t.Equal("10 件中 3 件", l.Format("k.fmt", 3, 10), "fallback: Format");
            t.Equal("zh-Hans", l.SetLanguage("zh-CN"), "fallback: zh-CN → zh-Hans");
            t.Equal("全部", l.Get("k.all"), "fallback: zh-Hans cell");
            t.Equal("3 of 10", l.Format("k.fmt", 3, 10), "fallback: zh-Hans missing → en format");
            t.Equal("en", l.SetLanguage("fr"), "fallback: unknown language → en");
        }

        static void File(TestRunner t)
        {
            L10nTable tb;
            try { tb = UiKit.Strings(t); }
            catch (Exception e) { t.Check(false, "strings.tsv loads: " + e.Message); return; }
            t.Check(tb.Problems.Count == 0, "strings.tsv: no parse problems (" + string.Join("; ", tb.Problems.Take(10)) + ")");
            var noEn = tb.Keys.Where(k => tb.Cell(k, "en") == null).ToList();
            t.Check(noEn.Count == 0, "strings.tsv: every key has en (" + noEn.Count + " missing: " + string.Join(", ", noEn.Take(10)) + ")");
            var ph = tb.PlaceholderMismatches();
            t.Check(ph.Count == 0, "strings.tsv: placeholders consistent (" + string.Join(", ", ph.Take(10)) + ")");
            var noAction = ActionManifest.All.Where(a => !tb.Has(a.LabelKey)).Select(a => a.Id).ToList();
            foreach (var id in new[] { ActionIds.DialogAccept, ActionIds.DialogCancel, ActionIds.TextBlur })
                if (!tb.Has(ActionIds.LabelKey(id))) noAction.Add(id);
            t.Check(noAction.Count == 0, "strings.tsv: every action has its label key (" + string.Join(", ", noAction.Take(10)) + ")");
            var noOption = OptionDefs.All.Where(o => !tb.Has(o.LabelKey)).Select(o => o.Key).ToList();
            t.Check(noOption.Count == 0, "strings.tsv: every option has its label key (" + string.Join(", ", noOption.Take(10)) + ")");
            var rep = tb.Report();
            t.Info("strings.tsv: " + rep);
        }
    }
}
