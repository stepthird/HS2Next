// CU0: the strings of the Constraints panel and section: every key in en, ja and zh-Hans, the same placeholders in each, the history labels as the
// operations name them, the action of the panel in the manifest with no default key, and half of CU10: no cell of strings.tsv names a community
// plugin. The panel's title is the generic word.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HS2Next.UI.Core.Tests
{
    using HS2Next.Save.Tests;
    using HS2Next.UI.Core;

    public static class SchemaTests_Constraints
    {
        public static void Run(TestRunner t)
        {
            Action(t);
            Strings(t);
        }

        static void Action(TestRunner t)
        {
            t.Equal("constraints.panel", ActionIds.ConstraintsPanel, "CU0: the panel's action id");
            t.Check(ActionManifest.Contains(ActionIds.ConstraintsPanel), "CU0: the action stands in the manifest");
            foreach (var preset in new[] { Keymap.DefaultPreset, Keymap.Hs2StudioPreset })
            {
                string json = UiKit.ReadText(t, Path.Combine("Keymaps", preset + ".json"));
                t.Check(!json.Contains(ActionIds.ConstraintsPanel), "CU0: " + preset + " binds no key to it");
            }
        }

        static readonly string[] Keys =
        {
            "action.constraints.panel", "panel.constraints", "constraints.add", "constraints.clear", "constraints.source", "constraints.target",
            "constraints.kind.node", "constraints.kind.bone", "constraints.kind.ik",
            "constraints.comp.position", "constraints.comp.rotation", "constraints.comp.scale", "constraints.comp.lookAt", "constraints.comp.parent", "constraints.comp.parentTip",
            "constraints.offset", "constraints.takeOffset", "constraints.clearOffset", "constraints.weight", "constraints.label", "constraints.pickInView", "constraints.useSelection",
            "constraints.openPanel", "constraints.none", "constraints.state.off", "constraints.state.sourceMissing", "constraints.state.targetMissing", "constraints.state.cycle",
            "constraints.state.bad", "constraints.state.pending", "constraints.state.protected", "constraints.state.limit", "constraints.lag", "constraints.lagTip", "constraints.sameCharaTip",
            "constraints.ikOff", "constraints.notShown", "constraints.bandTip", "toast.constraintDriven",
            "constraints.moveUp", "constraints.moveDown", "constraints.remove", "constraints.noEntries", "constraints.selectHint", "constraints.newHint", "constraints.pickBone",
            "constraints.components", "constraints.primaryOnly", "constraints.cycleRefused",
        };

        static void Strings(TestRunner t)
        {
            L10nTable tb;
            try { tb = UiKit.Strings(t); }
            catch (Exception e) { t.Check(false, "CU0: strings.tsv loads: " + e.Message); return; }
            var history = new Dictionary<string, string>
            {
                ["add"] = OpLabels.ConstraintAdd, ["edit"] = OpLabels.Constraint, ["remove"] = OpLabels.ConstraintRemove,
                ["clear"] = OpLabels.ConstraintClear, ["reorder"] = OpLabels.ConstraintReorder, ["takeOffset"] = OpLabels.ConstraintTakeOffset,
            };
            var keys = new List<string>(Keys);
            foreach (var h in history) keys.Add("history.constraint." + h.Key);
            var missing = new List<string>();
            foreach (var k in keys)
                foreach (var lang in new[] { "en", "ja", "zh-Hans" })
                    if (string.IsNullOrEmpty(tb.Cell(k, lang))) missing.Add(k + " (" + lang + ")");
            t.Check(missing.Count == 0, "CU0: " + keys.Count + " keys in en, ja and zh-Hans (" + missing.Count + " missing: " + string.Join(", ", missing.Take(20)) + ")");
            foreach (var k in keys)
            {
                bool en = tb.Cell(k, "en")?.Contains("{0}") == true;
                t.Check(tb.Cell(k, "ja")?.Contains("{0}") == en && tb.Cell(k, "zh-Hans")?.Contains("{0}") == en, "CU0: " + k + ": {0} in every language or none");
            }
            foreach (var h in history)
            {
                t.Equal(h.Value, tb.Cell("history.constraint." + h.Key, "en"), "CU0: the history label " + h.Key + " in en");
                t.Check(OpLabels.All.Contains(h.Value), "CU0: OpLabels holds «" + h.Value + "»");
            }
            t.Equal("Constraints", tb.Cell("panel.constraints", "en"), "CU10: the feature is called Constraints");
            t.Equal("Mosaic parts are not editable.", tb.Cell("constraints.state.protected", "en"), "CU0: the row of the mosaic is the rule");
            t.Equal("Driven by a constraint. Edit its offset or switch it off.", tb.Cell("toast.constraintDriven", "en"), "CU0: the toast of a refused drag");
            t.Equal("This would close a loop ({0}).", tb.Cell("constraints.cycleRefused", "en"), "CU0: a loop refused at the edit is not the state of a suspended entry");
            t.Equal("Reads this character's pose before its pose corrections and the stages after them.", tb.Cell("constraints.sameCharaTip", "en"), "CU0: the note of a source on the same character");
            string text = UiKit.ReadText(t, Path.Combine("L10n", "strings.tsv"));
            var lines = text.Split('\n').Where(l => !l.StartsWith("#", StringComparison.Ordinal) && Names(l)).Select(l => l.Split('\t')[0]).ToList();
            t.Check(lines.Count == 0, "CU10: no row of strings.tsv names a community plugin (" + string.Join(", ", lines) + ")");
        }

        static bool Names(string text) => SchemaNames.Names(text);
    }

    public static class SchemaNames
    {
        /// <summary>The name of the community plugin of this feature, in any spelling a text would carry it.</summary>
        public static bool Names(string text)
        {
            if (text == null) return false;
            string t = text.Replace(" ", "").Replace("_", "").Replace("-", "");
            return t.IndexOf("Nodes" + "Constraints", StringComparison.OrdinalIgnoreCase) >= 0 || t.IndexOf("Node" + "Constraints", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
