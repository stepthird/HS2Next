// AU0: the Advanced pose tab's rows in the schema: the tab's place, the paths through the block with their kinds, the ranges, the history
// labels of its edits, and every string of its block in en, ja and zh-Hans.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HS2Next.UI.Core.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;
    using HS2Next.Scenes;
    using HS2Next.UI.Core;

    public static class SchemaTests_AdvancedPose
    {
        public static void Run(TestRunner t)
        {
            Place(t);
            Rows(t);
            Labels(t);
            Strings(t);
        }

        static void Place(TestRunner t)
        {
            var ids = InspectorSchemas.Chara.Select(s => s.Id).ToList();
            int i = ids.IndexOf(InspectorSchemas.AdvancedPoseTabId);
            t.Check(i > 0 && ids[i - 1] == "correction", "AU0: the tab comes after Correction (" + string.Join(", ", ids) + ")");
            var tab = InspectorSchemas.Chara[i];
            t.Equal("inspector.tab.advancedPose", tab.LabelKey, "AU0: the tab's title key");
            t.Check(tab.Fields.All(f => f.Applies == Applies.Primary), "AU0: every row of the tab edits the primary");
            t.Check(tab.Fields.All(f => f.Kind == FieldKind.Vec3 || f.Kind == FieldKind.Float || f.Kind == FieldKind.Bool || f.Kind == FieldKind.Enum || f.Kind == FieldKind.Button),
                "AU0: no new kind of row: vectors, numbers, toggles, choices and buttons");
            t.Check(tab.Fields.Where(f => f.Group != InspectorSchemas.AdvancedPoseTabId).Count() == 0, "AU0: the rows hold no nested group: the tab draws its three sections");
        }

        static void Rows(TestRunner t)
        {
            var rows = InspectorSchemas.AdvancedPoseRows;
            var s = new SceneSession(new SessionOptions { Ids = new CountingIdSource(7) });
            var r = s.AddNodes(new[] { new NodeSpec { Doc = NodeDefaults.Chara(null, null) } });
            t.Check(r.Status == CommandStatus.Ok && r.NewIds.Count == 1, "AU0: a character for the walk");
            if (r.NewIds.Count != 1) return;
            string id = r.NewIds[0];
            // the walk goes through entries that exist
            s.Set(id, AdvancedPosePaths.Bone("cf_J_Hips"), new BoneCorrection());
            s.Set(id, AdvancedPosePaths.Chain("hair:0/root"), new ChainValues());
            var blend = new BlendValues();
            blend.Weights["mouth_a"] = 1f;
            s.Set(id, AdvancedPosePaths.Blend("head/o_head"), blend);
            var expected = new Dictionary<string, Type>(StringComparer.Ordinal)
            {
                [InspectorSchemas.AdvBonePos] = typeof(Vec3), [InspectorSchemas.AdvBoneRot] = typeof(Vec3), [InspectorSchemas.AdvBoneScale] = typeof(Vec3),
                [InspectorSchemas.AdvChainPrefix + "enabled"] = typeof(bool?), [InspectorSchemas.AdvChainPrefix + "weight"] = typeof(float?),
                [InspectorSchemas.AdvChainPrefix + "damping"] = typeof(float?), [InspectorSchemas.AdvChainPrefix + "elasticity"] = typeof(float?),
                [InspectorSchemas.AdvChainPrefix + "stiffness"] = typeof(float?), [InspectorSchemas.AdvChainPrefix + "inert"] = typeof(float?),
                [InspectorSchemas.AdvChainPrefix + "radius"] = typeof(float?), [InspectorSchemas.AdvChainPrefix + "gravity"] = typeof(Vec3?),
                [InspectorSchemas.AdvChainPrefix + "force"] = typeof(Vec3?), [InspectorSchemas.AdvChainPrefix + "freezeAxis"] = typeof(int?),
                [InspectorSchemas.AdvBustPrefix + "enabled"] = typeof(bool?), [InspectorSchemas.AdvBustPrefix + "gravity"] = typeof(Vec3?),
                [InspectorSchemas.AdvBustPrefix + "force"] = typeof(Vec3?), [InspectorSchemas.AdvBustPrefix + "damping"] = typeof(float?),
                [InspectorSchemas.AdvBustPrefix + "elasticity"] = typeof(float?), [InspectorSchemas.AdvBustPrefix + "stiffness"] = typeof(float?),
                [InspectorSchemas.AdvBustPrefix + "inert"] = typeof(float?), [InspectorSchemas.AdvBlendWeight] = typeof(float),
            };
            var withPath = rows.Where(f => f.Path != null).ToList();
            t.Equal(expected.Count, withPath.Count, "AU0: the rows with a path");
            foreach (var f in withPath)
            {
                if (!expected.TryGetValue(f.Path, out var type)) { t.Check(false, "AU0: an unexpected row " + f.Path); continue; }
                string sample = f.Placeholder == "bone" ? "cf_J_Hips" : f.Placeholder == "chain" ? "hair:0/root" : f.Placeholder == "bust" ? "breastL" : "head/o_head";
                string path = f.Expand(sample);
                if (path.IndexOf("{shape}", StringComparison.Ordinal) >= 0) path = path.Replace("{shape}", "mouth_a");
                Type leaf;
                try { leaf = FieldAccessor.LeafType(s.Model, id, path); }
                catch (CommandException e) { t.Check(false, "AU0: " + path + " does not resolve: " + e.Code + " " + e.Message); continue; }
                t.Check(leaf == type, "AU0: " + path + " is a " + type.Name + " (" + leaf.Name + ")");
            }
            var chainMembers = withPath.Count(f => f.Path.StartsWith(InspectorSchemas.AdvChainPrefix, StringComparison.Ordinal));
            var bustMembers = withPath.Count(f => f.Path.StartsWith(InspectorSchemas.AdvBustPrefix, StringComparison.Ordinal));
            t.Equal(10, chainMembers, "AU0: a chain has its ten members");
            t.Equal(7, bustMembers, "AU0: a bust chain has its seven members");
            // the ranges a command takes are what the rows offer
            var pos = rows.First(f => f.Path == InspectorSchemas.AdvBonePos);
            t.Check(pos.TypedMin == -AdvancedPoseRanges.MaxPos && pos.TypedMax == AdvancedPoseRanges.MaxPos, "AU0: position offset within +-1000");
            var scale = rows.First(f => f.Path == InspectorSchemas.AdvBoneScale);
            t.Check(Math.Abs(scale.TypedMin.Value - AdvancedPoseRanges.MinScale) < 1e-6 && scale.TypedMax == AdvancedPoseRanges.MaxScale, "AU0: scale factor within 0.001..1000");
            var weight = rows.First(f => f.Path == InspectorSchemas.AdvBlendWeight);
            t.Check(weight.Min == 0 && weight.Max == 100 && weight.TypedMin == AdvancedPoseRanges.MinBlendWeight && weight.TypedMax == AdvancedPoseRanges.MaxBlendWeight, "AU0: a blend slider 0..100, typed -200..300");
            t.Check(rows.Where(f => f.Kind == FieldKind.Float && f.Path != InspectorSchemas.AdvBlendWeight && !f.Path.EndsWith("radius", StringComparison.Ordinal)).All(f => f.Min == 0 && f.Max == 1 && f.TypedMin == 0 && f.TypedMax == 1), "AU0: the chains' values 0..1");
            var radius = rows.First(f => f.Path == InspectorSchemas.AdvChainPrefix + "radius");
            t.Check(radius.TypedMin == 0 && radius.TypedMax == null, "AU0: the radius from 0");
            var freeze = rows.First(f => f.Path == InspectorSchemas.AdvChainPrefix + "freezeAxis");
            t.Check(freeze.Choices.SequenceEqual(new[] { "none", "x", "y", "z" }) && freeze.ChoiceLabelPrefix == "enum.freezeAxis.", "AU0: the freeze axes");
        }

        static void Labels(TestRunner t)
        {
            foreach (var l in new[] { OpLabels.BoneCorrection, OpLabels.ClearBoneCorrections, OpLabels.JiggleValues, OpLabels.ClearJiggleValues, OpLabels.BlendShapes, OpLabels.ClearBlendShapes })
                t.Check(OpLabels.All.Contains(l), "AU0: OpLabels holds «" + l + "»");
            var rows = InspectorSchemas.AdvancedPoseRows;
            t.Check(rows.Where(f => f.Path != null && f.Path.StartsWith("chara.advancedPose.bones", StringComparison.Ordinal)).All(f => f.OpLabel == OpLabels.BoneCorrection), "AU0: bone rows: «Bone correction»");
            t.Check(rows.Where(f => f.Path != null && (f.Path.StartsWith(InspectorSchemas.AdvChainPrefix, StringComparison.Ordinal) || f.Path.StartsWith(InspectorSchemas.AdvBustPrefix, StringComparison.Ordinal))).All(f => f.OpLabel == OpLabels.JiggleValues), "AU0: chain and bust rows: «Jiggle values»");
            t.Check(rows.First(f => f.Path == InspectorSchemas.AdvBlendWeight).OpLabel == OpLabels.BlendShapes, "AU0: a blend row: «Blend shapes»");
        }

        // Every row of the block in the three languages; the history labels equal OpLabels; the rows the owner fixed.
        static void Strings(TestRunner t)
        {
            L10nTable tb;
            try { tb = UiKit.Strings(t); }
            catch (Exception e) { t.Check(false, "AU0: strings.tsv loads: " + e.Message); return; }
            var keys = new List<string> { "inspector.tab.advancedPose" };
            foreach (var part in new[] { "primaryOnly", "bones", "jiggle", "blend", "part", "search", "pick", "corrected", "part.body", "part.head", "part.hair", "part.clothes", "part.acc", "part.other",
                "pos", "rot", "scale", "reset", "resetAll", "selectBone", "stale", "remove", "fkMark", "fkTip", "notShownTip", "why.eyeLook", "why.mouthWidth", "why.physics", "why.jointCorrection", "why.other",
                "ikTip", "protectedTip", "noChains", "chain.enabled", "chain.weight", "chain.damping", "chain.elasticity", "chain.stiffness", "chain.inert", "chain.radius", "chain.gravity", "chain.force",
                "chain.freezeAxis", "breastL", "breastR", "hipL", "hipR", "chainOverride", "blendRenderer" })
                keys.Add("inspector.chara.adv." + part);
            foreach (var a in InspectorSchemas.FreezeAxes) keys.Add("enum.freezeAxis." + a);
            var history = new Dictionary<string, string>
            {
                ["bones"] = OpLabels.BoneCorrection, ["clearBones"] = OpLabels.ClearBoneCorrections, ["jiggle"] = OpLabels.JiggleValues,
                ["clearJiggle"] = OpLabels.ClearJiggleValues, ["blend"] = OpLabels.BlendShapes, ["clearBlend"] = OpLabels.ClearBlendShapes,
            };
            foreach (var h in history) keys.Add("history.advancedPose." + h.Key);
            var missing = new List<string>();
            foreach (var k in keys)
                foreach (var lang in new[] { "en", "ja", "zh-Hans" })
                    if (string.IsNullOrEmpty(tb.Cell(k, lang))) missing.Add(k + " (" + lang + ")");
            t.Check(missing.Count == 0, "AU0: " + keys.Count + " keys in en, ja and zh-Hans (" + missing.Count + " missing: " + string.Join(", ", missing.Take(20)) + ")");
            foreach (var h in history)
                t.Equal(h.Value, tb.Cell("history.advancedPose." + h.Key, "en"), "AU0: the history label " + h.Key + " in en");
            foreach (var f in InspectorSchemas.AdvancedPoseRows)
                t.Check(!string.IsNullOrEmpty(tb.Cell(f.LabelKey, "ja")) && !string.IsNullOrEmpty(tb.Cell(f.LabelKey, "zh-Hans")), "AU0: the label of " + f.Id + " in ja and zh-Hans");
            t.Equal("尻 左", tb.Cell("inspector.chara.adv.hipL", "ja"), "AU0: the hip chains' word in ja");
            t.Equal("尻 右", tb.Cell("inspector.chara.adv.hipR", "ja"), "AU0: the hip chains' word in ja");
        }
    }
}
