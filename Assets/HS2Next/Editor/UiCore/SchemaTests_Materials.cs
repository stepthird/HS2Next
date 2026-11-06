// MU0: the Materials tab and sections in the schema: their place, the hair bone section after the part groups, the history labels of the
// edits, every string of the block in en, ja and zh-Hans, and MU9's half that needs no Unity: no cell of strings.tsv carries a community
// plugin's name.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HS2Next.UI.Core.Tests
{
    using HS2Next.Save.Tests;
    using HS2Next.UI.Core;

    public static class SchemaTests_Materials
    {
        public static void Run(TestRunner t)
        {
            Place(t);
            Labels(t);
            Strings(t);
        }

        static void Place(TestRunner t)
        {
            var tabs = InspectorSchemas.Chara.Select(s => s.Id).ToList();
            t.Equal(11, tabs.Count, "MU0: eleven tabs from the schema");
            int i = tabs.IndexOf(InspectorSchemas.MaterialsTabId);
            t.Check(i > 0 && tabs[i - 1] == InspectorSchemas.AdvancedPoseTabId && tabs[i + 1] == "character", "MU0: the tab stands between Advanced pose and Character (" + string.Join(", ", tabs) + ")");
            var tab = InspectorSchemas.Chara[i];
            t.Equal("inspector.tab.materials", tab.LabelKey, "MU0: the tab's title key");
            t.Equal(0, tab.Fields.Count, "MU0: the tab holds no schema rows: the view draws them from the live material");
            var item = InspectorSchemas.Item.Select(s => s.Id).ToList();
            t.Equal(InspectorSchemas.ItemMaterialEditSection, item[item.Count - 1], "MU0: the item's section comes last");
            t.Check(item.Contains("material"), "MU0: the section material stays HS2's fields");
            t.Equal("inspector.item.section.materialEdit", InspectorSchemas.Item.Last().LabelKey, "MU0: the item section's title key");
            var scene = InspectorSchemas.Scene.Select(s => s.Id).ToList();
            int m = scene.IndexOf("map");
            t.Check(m >= 0 && scene[m + 1] == InspectorSchemas.MapMaterialsSection, "MU0: the map's section stands under the map");
            t.Equal("inspector.scene.section.mapMaterials", InspectorSchemas.Scene[m + 1].LabelKey, "MU0: the map section's title key");
            var hair = InspectorSchemas.CharaEdit.Where(s => InspectorSchemas.CharaEditTreeOf(s) == "hair").ToList();
            t.Equal(InspectorSchemas.HairBoneModsSection, hair[hair.Count - 1].Id, "MU0: the hair bone section stands after the part groups");
            t.Equal(-1, InspectorSchemas.HairPartOf(InspectorSchemas.HairBoneModsSection), "MU0: it belongs to no part");
            t.Equal("hair", InspectorSchemas.CharaEditPageOf(hair[hair.Count - 1]), "MU0: it stands on the Hair page");
            t.Equal("inspector.charaEdit.hair.boneMods", hair[hair.Count - 1].LabelKey, "MU0: its title key");
        }

        static void Labels(TestRunner t)
        {
            foreach (var l in new[] { OpLabels.MaterialEdit, OpLabels.MaterialReset, OpLabels.MaterialClear, OpLabels.MaterialTexture, OpLabels.MaterialShader, OpLabels.HairBoneAdjust })
                t.Check(OpLabels.All.Contains(l), "MU0: OpLabels holds «" + l + "»");
        }

        static void Strings(TestRunner t)
        {
            L10nTable tb;
            try { tb = UiKit.Strings(t); }
            catch (Exception e) { t.Check(false, "MU0: strings.tsv loads: " + e.Message); return; }
            var keys = new List<string> { "inspector.tab.materials", "inspector.item.section.materialEdit", "inspector.scene.section.mapMaterials" };
            foreach (var part in new[] { "target", "character", "search", "editedOnly", "mode.materials", "mode.renderers", "searchHint", "slots", "shader", "queue", "keywords", "enabled",
                "tiling", "offset", "removeImage", "importing", "original", "notApplied", "remove", "resetAll", "why.notOnShader", "why.shaderUnavailable", "why.materialMissing", "why.objectMissing",
                "why.notImplemented", "why.fallbackFixed", "why.driven", "why.badValue", "why.undecodable", "mosaic", "drawsBody", "fallback", "overColour", "readOnly", "open" })
                keys.Add("inspector.materials." + part);
            foreach (var part in new[] { "", ".add", ".scale", ".length", ".position", ".rotation", ".note" }) keys.Add("inspector.charaEdit.hair.boneMods" + part);
            var history = new Dictionary<string, string>
            {
                ["edit"] = OpLabels.MaterialEdit, ["reset"] = OpLabels.MaterialReset, ["clear"] = OpLabels.MaterialClear,
                ["texture"] = OpLabels.MaterialTexture, ["shader"] = OpLabels.MaterialShader, ["hairBone"] = OpLabels.HairBoneAdjust,
            };
            foreach (var h in history) keys.Add("history.materials." + h.Key);
            var missing = new List<string>();
            foreach (var k in keys)
                foreach (var lang in new[] { "en", "ja", "zh-Hans" })
                    if (string.IsNullOrEmpty(tb.Cell(k, lang))) missing.Add(k + " (" + lang + ")");
            t.Check(missing.Count == 0, "MU0: " + keys.Count + " keys in en, ja and zh-Hans (" + missing.Count + " missing: " + string.Join(", ", missing.Take(20)) + ")");
            foreach (var h in history)
                t.Equal(h.Value, tb.Cell("history.materials." + h.Key, "en"), "MU0: the history label " + h.Key + " in en");
            foreach (var k in keys)
                foreach (var p in new[] { "{0}" })
                {
                    bool en = tb.Cell(k, "en")?.Contains(p) == true;
                    t.Check(tb.Cell(k, "ja")?.Contains(p) == en && tb.Cell(k, "zh-Hans")?.Contains(p) == en, "MU0: " + k + ": " + p + " in every language or none");
                }
            t.Equal("Mosaic parts and their materials cannot be edited.", tb.Cell("inspector.materials.mosaic", "en"), "MU0: the row of the mosaic as the spec gives it");
            var cells = new[] { "inspector.chara.meShaderUnavailable", "inspector.chara.meShaderUnavailable.tip" };
            foreach (var k in cells)
                foreach (var lang in new[] { "en", "ja", "zh-Hans" })
                    t.Check(!Names(tb.Cell(k, lang)), "MU9: " + k + " (" + lang + ") names no plugin");
            t.Check(tb.Cell("inspector.chara.meShaderUnavailable", "en").Contains("{0}"), "MU9: the reworded row keeps its names");
            string text = UiKit.ReadText(t, Path.Combine("L10n", "strings.tsv"));
            var lines = text.Split('\n').Where(l => !l.StartsWith("#", StringComparison.Ordinal) && Names(l)).Select(l => l.Split('\t')[0]).ToList();
            t.Check(lines.Count == 0, "MU9: no row of strings.tsv names the community plugin (" + string.Join(", ", lines) + ")");
        }

        // The plugin's name in any spelling a text would carry it.
        static bool Names(string text) => text != null && (text.IndexOf("MaterialEditor", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Material Editor", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("KK_", StringComparison.Ordinal) >= 0);
    }
}
