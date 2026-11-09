// WY10, the half with no window: the strings of Wet and stains and of the viewport tools: every key in en, ja and zh-Hans, the same placeholders in each, the history labels as the
// operations name them, the action of the panel in the manifest with no default key, and no row of the block naming anything beyond a stain, its origin, water and blood.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace HS2Next.UI.Core.Tests
{
    using HS2Next.Save.Tests;
    using HS2Next.UI.Core;

    public static class SchemaTests_Wet
    {
        public static void Run(TestRunner t)
        {
            Action(t);
            Strings(t);
        }

        static void Action(TestRunner t)
        {
            t.Equal("wet.panel", ActionIds.WetPanel, "WY10: the panel's action id");
            t.Check(ActionManifest.Contains(ActionIds.WetPanel), "WY10: the action stands in the manifest");
            foreach (var preset in new[] { Keymap.DefaultPreset, Keymap.Hs2StudioPreset })
            {
                string json = UiKit.ReadText(t, Path.Combine("Keymaps", preset + ".json"));
                t.Check(!json.Contains(ActionIds.WetPanel), "WY10: " + preset + " binds no key to it");
            }
        }

        // the block's rows as the file holds them
        static List<string[]> Block(TestRunner t)
        {
            var rows = new List<string[]>();
            bool inside = false;
            foreach (string line in UiKit.ReadText(t, Path.Combine("L10n", "strings.tsv")).Split('\n'))
            {
                if (line.StartsWith("#", StringComparison.Ordinal)) { inside = line.StartsWith("#U66", StringComparison.Ordinal); continue; }
                if (inside && line.Length > 0) rows.Add(line.Split('\t'));
            }
            return rows;
        }

        static readonly string[] Spec =
        {
            "panel.wet", "wet.addStain", "wet.pickOrigin", "wet.defaultOrigin", "wet.start", "wet.full", "wet.dry", "wet.clearPuddles", "wet.keep", "wet.preset.water", "wet.preset.blood",
            "wet.applyAgain", "wet.floor.map", "wet.floor.feet", "wet.floor.fixed", "wet.status.partChanged", "action.wet.panel", "tool.refused.dialog", "tool.refused.edit",
        };

        static void Strings(TestRunner t)
        {
            L10nTable tb;
            try { tb = UiKit.Strings(t); }
            catch (Exception e) { t.Check(false, "WY10: strings.tsv loads: " + e.Message); return; }
            var rows = Block(t);
            t.Check(rows.Count >= 90, "WY10: the block #U66 holds its rows (" + rows.Count + ")");
            var keys = new List<string>(rows.Select(r => r[0]));
            foreach (var k in Spec) t.Check(keys.Contains(k), "WY10: the block holds " + k);
            var missing = new List<string>();
            foreach (var k in keys)
                foreach (var lang in new[] { "en", "ja", "zh-Hans" })
                    if (string.IsNullOrEmpty(tb.Cell(k, lang))) missing.Add(k + " (" + lang + ")");
            t.Check(missing.Count == 0, "WY10: " + keys.Count + " keys in en, ja and zh-Hans (" + missing.Count + " missing: " + string.Join(", ", missing.Take(20)) + ")");
            var marks = new Regex(@"\{\d\}");
            foreach (var k in keys)
            {
                var en = marks.Matches(tb.Cell(k, "en") ?? "").Cast<Match>().Select(m => m.Value).OrderBy(x => x).ToList();
                t.Check(en.SequenceEqual(marks.Matches(tb.Cell(k, "ja") ?? "").Cast<Match>().Select(m => m.Value).OrderBy(x => x)) &&
                        en.SequenceEqual(marks.Matches(tb.Cell(k, "zh-Hans") ?? "").Cast<Match>().Select(m => m.Value).OrderBy(x => x)), "WY10: " + k + ": the same placeholders in every language");
            }
            foreach (var r in rows) t.Check(r.Length >= 5 && r[4] == "zh?", "WY10: " + r[0] + ": the zh-Hans cell awaits its review");
            var history = new Dictionary<string, string>
            {
                ["add"] = OpLabels.WetAdd, ["remove"] = OpLabels.WetRemove, ["edit"] = OpLabels.WetEdit, ["keep"] = OpLabels.WetKeep, ["start"] = OpLabels.WetStart, ["dry"] = OpLabels.WetDry,
                ["clearPuddles"] = OpLabels.WetClearPuddles, ["floor"] = OpLabels.WetFloor, ["origin"] = OpLabels.WetOrigin,
            };
            foreach (var h in history)
            {
                t.Equal(h.Value, tb.Cell("history.wet." + h.Key, "en"), "WY10: the history label " + h.Key + " in en");
                t.Check(OpLabels.All.Contains(h.Value), "WY10: OpLabels holds «" + h.Value + "»");
            }
            t.Equal("Wet and stains", tb.Cell("panel.wet", "en"), "WY10: the feature's name");
            t.Equal("濡れと染み", tb.Cell("panel.wet", "ja"), "WY10: the feature's name in ja");
            var bare = new[] { "Lab", "plugin", "HS2", "Studio" };
            foreach (var r in rows)
                foreach (string cell in r.Skip(1).Take(3))
                    foreach (var w in bare)
                        t.Check(cell.IndexOf(w, StringComparison.Ordinal) < 0, "WY10: " + r[0] + " names no other subject (" + w + ")");
        }
    }
}
