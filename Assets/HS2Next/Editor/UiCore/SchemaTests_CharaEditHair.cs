// The Hair page of the Edit tab (InspectorSchemas
// .CharaEdit): one page `hair` with the options, the part selector and per part (back, front, side, option) Style · Colour ·
// Decoration · Bundles · Mesh with concrete paths; every hair row resolves on a character with stored documents with its
// declared leaf type (bools, the shader type int, refs, colours, the mesh layout components on Vec4, the 305 swatch push on
// the base colour, the bundle list on List<HairBundle>) and is writable; counts as listed (lists 8, colours 32, sliders 24,
// push grids 4, buttons 8, bundle lists 4, toggles 3, choices 1; per sex identical); ColorAlpha false on all; LerpClamped
// exactly on the mesh layout; ResetKind (Constant on the mesh layout, PrefabDefault on the bundle lists,
// Reference elsewhere); defaults = the document defaults; gates on the right rows (section gates implied by
// their rows' gates); the gate names parse alike in SchemaGates and MakerEdits; watched paths; section order;
// every label key in en, ja and zh-Hans.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HS2Next.UI.Core.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;
    using HS2Next.Scenes;
    using HS2Next.UI.Core;

    public static class SchemaTests_CharaEditHair
    {
        public static void Run(TestRunner t)
        {
            Structure(t);
            Resolve(t);
            Counts(t);
            Rules(t);
            Defaults(t);
            Gates(t);
            Watch(t);
            Labels(t);
        }

        const string Root = "appearance.chara.";
        static readonly string[] Parts = { "back", "front", "side", "option" };

        static IEnumerable<SchemaSection> HairSections => InspectorSchemas.CharaEdit.Where(s => InspectorSchemas.CharaEditTreeOf(s) == "hair");
        static IEnumerable<FieldSpec> HairRows => HairSections.SelectMany(s => s.Fields);
        static string Member(FieldSpec f) => f.Path != null && f.Path.StartsWith(Root, StringComparison.Ordinal) ? f.Path.Substring(Root.Length) : f.Path;
        static string Key(FieldSpec f) => (Member(f) ?? f.Action) + (f.Component >= 0 ? "#" + f.Component : "");

        static void Structure(TestRunner t)
        {
            var secs = HairSections.ToList();
            var expect = new List<string> { "hair.hair.options", "hair.hair.part" };
            foreach (var p in Parts) foreach (var s in new[] { "style", "colour", "decoration", "bundles", "mesh" }) expect.Add("hair.hair." + p + "." + s);
            t.Check(secs.Select(s => s.Id).SequenceEqual(expect), "hair: the sections in order (options, part, then per part style, colour, decoration, bundles, mesh; " +
                string.Join(", ", secs.Select(s => s.Id).Take(8)) + " …)");
            t.Check(InspectorSchemas.HairPages.SequenceEqual(new[] { "hair" }) && InspectorSchemas.CharaEditPagesOf("hair").SequenceEqual(new[] { "hair" }),
                "hair: one page `hair` (no page strip, J2)");
            t.Check(secs.All(s => InspectorSchemas.CharaEditPageOf(s) == "hair" && InspectorSchemas.CharaEditTreeOf(s) == "hair"),
                "hair: every section parses into node hair, page hair (the rest may hold a dot: hair.hair.back.style)");
            t.Equal(secs.Count, InspectorSchemas.CharaEditSections("hair", "hair").Count(), "hair: CharaEditSections(hair, hair) = every hair section");
            t.Check(InspectorSchemas.HairParts.SequenceEqual(Parts) && InspectorSchemas.HairParts.SequenceEqual(HairData.PartNames) && SchemaGates.HairPartNames.SequenceEqual(Parts),
                "hair: the parts back, front, side, option = HairData.PartNames = SchemaGates.HairPartNames");
            int bad = 0;
            for (int k = 0; k < 4; k++)
                foreach (var s in InspectorSchemas.HairPartSections)
                {
                    string id = InspectorSchemas.HairSectionId(k, s);
                    if (id != "hair.hair." + Parts[k] + "." + s || InspectorSchemas.HairPartOf(id) != k || !secs.Any(x => x.Id == id)) bad++;
                }
            t.Check(bad == 0 && InspectorSchemas.HairPartOf("hair.hair.options") == -1 && InspectorSchemas.HairPartOf("hair.hair.part") == -1 &&
                    InspectorSchemas.HairPartOf("body.paint.p1") == -1 && InspectorSchemas.HairPartOf(null) == -1 && InspectorSchemas.HairPartOf("hair.hair.top.style") == -1,
                "hair: HairSectionId / HairPartOf round-trip on the 20 part sections; options, part and other ids → −1");
            t.Check(secs.Single(s => s.Id == InspectorSchemas.HairPartSection).Fields.Count == 0, "hair: the part selector section has no rows (the host puts the PartSelector there)");
            t.Check(secs.All(s => !s.IsRepeated && s.Fields.All(f => !f.IsTemplate)), "hair: no repeated section, no template (concrete paths per part)");
            // each part group's rows address its own part only
            int foreign = 0;
            foreach (var s in secs)
            {
                int k = InspectorSchemas.HairPartOf(s.Id);
                if (k < 0) continue;
                foreach (var f in s.Fields)
                {
                    string m = Member(f);
                    bool own = m != null ? m.StartsWith("hair." + Parts[k] + ".", StringComparison.Ordinal) : InspectorSchemas.HairPartOfAction(f.Action, f.Action.StartsWith(InspectorSchemas.ActionMatchHairToSkinPrefix, StringComparison.Ordinal)
                        ? InspectorSchemas.ActionMatchHairToSkinPrefix : InspectorSchemas.ActionResetHairBundlesPrefix) == k;
                    if (!own) { foreign++; t.Check(false, "hair: " + s.Id + " row " + Key(f) + " is not of part " + Parts[k]); }
                    if (f.Gate != null && !(SchemaGates.TryMakerHair(f.Gate, out int gp, out _) && gp == k)) { foreign++; t.Check(false, "hair: " + s.Id + " row " + Key(f) + " gate " + f.Gate); }
                }
            }
            t.Equal(0, foreign, "hair: every row of a part group addresses that part and is gated on that part");
            var ids = HairRows.Select(f => f.Id).ToList();
            t.Check(ids.Distinct().Count() == ids.Count, "hair: row ids are distinct (push:colour:305:<base colour path> beside the colour row)");
            var others = new HashSet<string>(InspectorSchemas.CharaEdit.Where(s => InspectorSchemas.CharaEditTreeOf(s) != "hair").SelectMany(s => s.Fields).Select(f => f.Id));
            t.Check(ids.All(id => !others.Contains(id)), "hair: no hair row id is also a face / body row id");
            var options = secs[0].Fields.Select(Key).ToList();
            t.Check(options.SequenceEqual(new[] { "hair.editor.sameSetting", "hair.editor.autoSetting", "hair.editor.ctrlTogether", "hair.shaderType" }),
                "hair: the options HX1–HX4 in order (" + string.Join(", ", options) + ")");
            var colour = secs.Single(s => s.Id == "hair.hair.front.colour").Fields.Select(f => f.Kind == FieldKind.Push ? "push" : Key(f)).ToList();
            t.Check(colour.SequenceEqual(new[] { "hair.front.baseColor", "hair.front.topColor", "hair.front.underColor", "hair.front.specular", "hair.front.metallic",
                "hair.front.smoothness", "push", InspectorSchemas.ActionMatchHairToSkin(1) }), "hair: Colour = HK1–HK4, HS1, HS2, HP1, HX6 in order (" + string.Join(", ", colour) + ")");
            var mesh = secs.Single(s => s.Id == "hair.hair.side.mesh").Fields.Select(Key).ToList();
            t.Check(mesh.SequenceEqual(new[] { "hair.side.mesh.item", "hair.side.mesh.color", "hair.side.mesh.layout#0", "hair.side.mesh.layout#1", "hair.side.mesh.layout#2", "hair.side.mesh.layout#3" }),
                "hair: Mesh = HL2, HK8, HS9–HS12 (" + string.Join(", ", mesh) + ")");
            var bundles = secs.Single(s => s.Id == "hair.hair.option.bundles").Fields.Select(f => f.Kind + " " + Key(f)).ToList();
            t.Check(bundles.SequenceEqual(new[] { "HairBundles hair.option.bundles", "Button " + InspectorSchemas.ActionResetHairBundles(3) }),
                "hair: Bundles = the bundle list and «全て初期化» (" + string.Join(", ", bundles) + ")");
            t.Check(InspectorSchemas.HairPartOfAction("ops.MatchHairToSkin:side", InspectorSchemas.ActionMatchHairToSkinPrefix) == 2 &&
                    InspectorSchemas.HairPartOfAction("ops.ResetHairBundles:option", InspectorSchemas.ActionResetHairBundlesPrefix) == 3 &&
                    InspectorSchemas.HairPartOfAction("ops.ResetHairBundles:x", InspectorSchemas.ActionResetHairBundlesPrefix) == -1 &&
                    InspectorSchemas.HairPartOfAction("ops.CopyEye:lr", InspectorSchemas.ActionResetHairBundlesPrefix) == -1,
                "hair: HairPartOfAction reads the part of the two button actions");
        }

        static void Resolve(TestRunner t)
        {
            var s = new SceneSession(new SessionOptions { Ids = new CountingIdSource(61) });
            var r = s.AddChara(new CharaAppearance { Stored = new CharaSubtree() });
            t.Check(r.Status == CommandStatus.Ok, "hair resolve: a character with stored documents");
            string id = r.NewIds[0];
            int n = 0, fails = 0;
            foreach (var f in HairRows)
            {
                if (f.Kind == FieldKind.Button) { if (f.Path != null || f.Action == null) { fails++; t.Check(false, "hair: button " + f.LabelKey + " needs an action and no path"); } continue; }
                n++;
                try
                {
                    var leaf = FieldAccessor.LeafType(s.Model, id, f.Path);
                    if (!Fits(f, leaf)) { fails++; t.Check(false, "hair: " + Key(f) + " is a " + leaf.Name + ", declared " + f.Kind); }
                    if (!FieldAccessor.IsWritable(s.Model, id, f.Path, out string code)) { fails++; t.Check(false, "hair: " + f.Path + " is not writable (" + code + ")"); }
                    if (AppearancePaths.IsRebuildPath(f.Path)) { fails++; t.Check(false, "hair: " + f.Path + " is a rebuild-only member"); }
                }
                catch (CommandException e) { fails++; t.Check(false, "hair: " + f.Path + " does not resolve: " + e.Code + " " + e.Message); }
            }
            t.Check(fails == 0 && n == 4 + 4 * 18, "hair resolve: " + n + " value rows resolve with their declared leaf types, are writable and no rebuild path");
            var w = InspectorSchemas.Find(InspectorSchemas.CharaEdit, InspectorSchemas.CharaEditPath("hair.front.mesh.layout"), 0);
            t.Check(w != null && w.LabelKey == "inspector.charaEdit.hair.meshWidth", "hair: «幅» = hair.front.mesh.layout component 0");
            var met = InspectorSchemas.Find(InspectorSchemas.CharaEdit, InspectorSchemas.CharaEditPath("hair.back.metallic"));
            t.Check(met != null && s.Set(id, met.Path, (float)met.ParseTyped(150)).Status == CommandStatus.Ok && (float)FieldAccessor.Get(s.Model, id, met.Path) == 1.5f,
                "hair: typed 150 into «質感» stores 1.5 (nothing clamps stored values)");
            var bl = HairRows.First(f => f.Kind == FieldKind.HairBundles);
            t.Check(FieldAccessor.LeafType(s.Model, id, bl.Path) == typeof(List<HairBundle>), "hair: the bundle list row is the List<HairBundle> member");
        }

        static bool Fits(FieldSpec f, Type leaf)
        {
            if (f.Component >= 0)
            {
                int n = leaf == typeof(Vec4) || leaf == typeof(Color4) ? 4 : leaf == typeof(Vec3) ? 3 : leaf == typeof(Vec2) ? 2 : 0;
                return f.Kind == FieldKind.Float && f.Component < n;
            }
            switch (f.Kind)
            {
                case FieldKind.Float: return leaf == typeof(float);
                case FieldKind.Bool: return leaf == typeof(bool);
                case FieldKind.Enum: return leaf == typeof(int);
                case FieldKind.Color: return leaf == typeof(Color4);
                case FieldKind.PartList: return leaf == typeof(string);
                case FieldKind.Push: return PushSources.ColourCategory(f.PushSource) >= 0 && leaf == typeof(Color4);
                case FieldKind.HairBundles: return leaf == typeof(List<HairBundle>);
            }
            return false;
        }

        static bool IsSlider(FieldSpec f) => f.Kind == FieldKind.Float && f.Scale == InspectorSchemas.MakerSliderScale;

        static void Counts(TestRunner t)
        {
            var rows = HairRows.ToList();
            Func<FieldSpec, bool> list = f => f.Kind == FieldKind.PartList, colour = f => f.Kind == FieldKind.Color, slider = IsSlider,
                push = f => f.Kind == FieldKind.Push, button = f => f.Kind == FieldKind.Button, bundles = f => f.Kind == FieldKind.HairBundles,
                toggle = f => f.Kind == FieldKind.Bool, choice = f => f.Kind == FieldKind.Enum;
            var defs = new[] { rows.Count(list), rows.Count(colour), rows.Count(slider), rows.Count(push), rows.Count(button), rows.Count(bundles), rows.Count(toggle), rows.Count(choice) };
            t.Check(defs.SequenceEqual(new[] { 8, 32, 24, 4, 8, 4, 3, 1 }),
                "hair counts: definitions lists / colours / sliders / push grids / buttons / bundle lists / toggles / choices = 8 / 32 / 24 / 4 / 8 / 4 / 3 / 1 (" + string.Join(" / ", defs) + ")");
            t.Equal(rows.Count, defs.Sum(), "hair counts: no row of another kind");
            for (int k = 0; k < 4; k++)
            {
                var pr = HairSections.Where(s => InspectorSchemas.HairPartOf(s.Id) == k).SelectMany(s => s.Fields).ToList();
                var per = new[] { pr.Count(list), pr.Count(colour), pr.Count(slider), pr.Count(push), pr.Count(button), pr.Count(bundles) };
                t.Check(per.SequenceEqual(new[] { 2, 8, 6, 1, 2, 1 }), "hair counts: part " + Parts[k] + " lists / colours / sliders / push / buttons / bundle lists = 2 / 8 / 6 / 1 / 2 / 1 (" + string.Join(" / ", per) + ")");
            }
            // per sex: every list has a category for both sexes and no row is gated by sex
            t.Check(rows.Where(list).All(f => f.CategoryFemale >= 0 && f.CategoryFemale == f.CategoryMale) && rows.All(f => f.Gate != SchemaGates.CharaFemale && f.Gate != SchemaGates.CharaMale),
                "hair counts: per sex identical (the same list categories, no sex gate)");
        }

        static void Rules(TestRunner t)
        {
            var rows = HairRows.ToList();
            var sliders = rows.Where(IsSlider).ToList();
            int bad = sliders.Count(f => f.Min != 0 || f.Max != 1 || f.TypedMin != -1 || f.TypedMax != 2 || f.Format != "0" || f.Applies != Applies.All || f.OpLabel != OpLabels.EditHair);
            t.Check(bad == 0, "hair rules: every slider has Q23's ranges (0..1, typed −1..2, ×100, \"0\"), all selected, label Edit hair (" + bad + " differ)");
            t.Check(rows.Where(f => f.Kind == FieldKind.Float).All(IsSlider), "hair rules: every float row is a Maker slider");
            t.Check(rows.Where(f => f.Kind == FieldKind.Color).All(f => !f.ColorAlpha && f.OpLabel == OpLabels.EditHair), "hair rules: no hair colour edits alpha (ColorAlpha false on all 32), label Edit hair");
            var lerp = sliders.Where(f => f.LerpClamped).Select(Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var expectLerp = Parts.SelectMany(p => Enumerable.Range(0, 4).Select(c => "hair." + p + ".mesh.layout#" + c)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            t.Check(lerp.SequenceEqual(expectLerp), "hair rules: LerpClamped exactly on HS9–HS12 of the four parts (" + lerp.Count + ")");
            var constant = rows.Where(f => f.ResetKind == ResetKind.Constant).Select(Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
            t.Check(constant.SequenceEqual(expectLerp), "hair rules: ResetKind Constant exactly on the mesh layout (↺ = (1, 1, 0, 0), J8)");
            var prefab = rows.Where(f => f.ResetKind == ResetKind.PrefabDefault).Select(f => f.Kind + " " + Key(f)).ToList();
            t.Check(prefab.SequenceEqual(Parts.Select(p => "HairBundles hair." + p + ".bundles")), "hair rules: ResetKind PrefabDefault exactly on the four bundle lists (J8)");
            t.Check(InspectorSchemas.CharaEdit.Where(s => InspectorSchemas.CharaEditTreeOf(s) == "face" || InspectorSchemas.CharaEditTreeOf(s) == "body").SelectMany(s => s.Fields)
                    .All(f => f.ResetKind == ResetKind.Reference),
                "hair rules: face and body rows keep the reference reset (the clothes rows: SchemaTests_CharaEditClothes)");
            var cats = rows.Where(f => f.Kind == FieldKind.PartList).Select(f => Key(f) + " " + f.CategoryFemale + "/" + f.CategoryMale).ToList();
            var expectCats = new List<string>();
            for (int k = 0; k < 4; k++) { expectCats.Add("hair." + Parts[k] + ".item " + (300 + k) + "/" + (300 + k)); expectCats.Add("hair." + Parts[k] + ".mesh.item 306/306"); }
            t.Check(cats.SequenceEqual(expectCats), "hair rules: PartList categories 300–303 per part and 306 for the mesh, both sexes (" + string.Join(", ", cats) + ")");
            t.Check(rows.Where(f => f.Kind == FieldKind.PartList).All(f => f.OpLabel == OpLabels.ChangeItem && f.Default == null && f.ListContext == null && f.ThumbColumn == null),
                "hair rules: lists are labelled Change item, have no default and no context");
            t.Check(rows.Where(f => f.Kind == FieldKind.PartList).All(f => MakerEdits.CategoryOf(f.Path, 1) == f.CategoryFemale && MakerEdits.CategoryOf(f.Path, 0) == f.CategoryMale),
                "hair rules: the list rows are item changes of their categories in the ops (MakerEdits.CategoryOf)");
            var pushes = rows.Where(f => f.Kind == FieldKind.Push).ToList();
            t.Check(pushes.Count == 4 && pushes.All(f => f.PushSource == "colour:305" && f.OpLabel == OpLabels.EditHair && MakerEdits.ColourPresetCategoryOf(f.Path) == 305 &&
                    Member(f).EndsWith(".baseColor", StringComparison.Ordinal)),
                "hair rules: the four swatch grids push colour:305 onto the part's base colour (the op's 305 path), Edit hair");
            t.Check(PushSources.HairColourPresetIds == 7 && MakerEdits.HairColourPresetIds == 7 && PushSources.ColourPresetIds(305) == 7 && PushSources.ColourPresetIds(336) == 7 &&
                    PushSources.ColourPresetIds(6) == -1 && MakerEdits.HairColourCategory == PushSources.HairColourCategory,
                "hair rules: both layers agree on the seven cells of 305 (ids 0..6)");
            var buttons = rows.Where(f => f.Kind == FieldKind.Button).Select(f => f.Action).ToList();
            t.Check(buttons.SequenceEqual(Enumerable.Range(0, 4).SelectMany(k => new[] { InspectorSchemas.ActionMatchHairToSkin(k), InspectorSchemas.ActionResetHairBundles(k) })) &&
                    rows.Where(f => f.Kind == FieldKind.Button).All(f => f.OpLabel == OpLabels.EditHair),
                "hair rules: the buttons are the skin match and «全て初期化» of each part, label Edit hair");
            var shader = rows.Single(f => f.Kind == FieldKind.Enum);
            t.Check(Member(shader) == "hair.shaderType" && shader.Choices.SequenceEqual(new[] { "type01", "type02" }) && shader.ChoiceLabelKey(1) == "inspector.charaEdit.hair.shaderType.type02" &&
                    Equals(shader.ChoiceValue(1, false), 1) && Equals(shader.Default, 0) && shader.OpLabel == OpLabels.EditHair,
                "hair rules: the draw type is a choice on the int hair.shaderType (0 «タイプ01», 1 «タイプ02»), Edit hair");
            var toggles = rows.Where(f => f.Kind == FieldKind.Bool).ToList();
            t.Check(toggles.All(f => f.OpLabel == OpLabels.EditHair && f.Gate == null && f.TooltipKey == null), "hair rules: the three switches are ungated toggles labelled Edit hair");
            t.Check(rows.All(f => f.TooltipKey == null && f.Applies == Applies.All), "hair rules: no tooltip keys; every row writes all selected characters");
            t.Check(rows.Where(f => f.Path != null).All(f => MakerEdits.AreaOf(new[] { f.Path }) == "hair"), "hair rules: every hair row's member is in the hair area (label Edit hair through the bridge too)");
        }

        // "Default" = the document default for every value row; the bundle list has none.
        static void Defaults(TestRunner t)
        {
            var s = new SceneSession(new SessionOptions { Ids = new CountingIdSource(71) });
            string id = s.AddChara(new CharaAppearance { Stored = new CharaSubtree() }).NewIds[0];
            int n = 0;
            var bad = new List<string>();
            foreach (var f in HairRows)
            {
                if (f.Kind == FieldKind.PartList || f.Kind == FieldKind.Push || f.Kind == FieldKind.Button || f.Kind == FieldKind.HairBundles) { if (f.Default != null) bad.Add(Key(f) + " has a default"); continue; }
                n++;
                var v = FieldAccessor.Get(s.Model, id, f.Path);
                bool ok;
                if (f.Kind == FieldKind.Bool) ok = Equals(f.Default, v);
                else if (f.Kind == FieldKind.Enum) ok = Equals(f.Default, v);
                else if (f.Kind == FieldKind.Color) ok = f.Default is double[] d && d.Length == 4 && v is Color4 c && (float)d[0] == c.R && (float)d[1] == c.G && (float)d[2] == c.B && (float)d[3] == c.A;
                else if (f.Component >= 0) ok = f.Default is double x && v is Vec4 a && (float)x == (f.Component == 0 ? a.X : f.Component == 1 ? a.Y : f.Component == 2 ? a.Z : a.W);
                else ok = f.Default is double y && v is float fv && (float)y == fv;
                if (!ok) bad.Add(Key(f) + " " + FieldJson.ToText(v) + " vs " + Show(f.Default));
            }
            t.Check(bad.Count == 0 && n == 4 + 4 * 14, "hair defaults: " + n + " value rows default to the document defaults (" + string.Join("; ", bad.Take(8)) + ")");
            Func<string, double[]> col = p => InspectorSchemas.Find(InspectorSchemas.CharaEdit, InspectorSchemas.CharaEditPath(p)).Default as double[];
            t.Check(col("hair.back.baseColor").SequenceEqual(new[] { 0.2, 0.2, 0.2, 1.0 }) && col("hair.front.topColor").SequenceEqual(new[] { 0.039, 0.039, 0.039, 1.0 }) &&
                    col("hair.side.underColor").SequenceEqual(new[] { 0.565, 0.565, 0.565, 1.0 }) && col("hair.option.specular").SequenceEqual(new[] { 0.3, 0.3, 0.3, 1.0 }) &&
                    col("hair.back.accessoryColors[2]").SequenceEqual(new[] { 1.0, 1.0, 1.0, 1.0 }) && col("hair.front.mesh.color").SequenceEqual(new[] { 1.0, 1.0, 1.0, 1.0 }),
                "hair defaults: base (0.2), top (0.039), under (0.565), gloss (0.3), decoration and mesh white");
            var lay = Enumerable.Range(0, 4).Select(c => Convert.ToDouble(InspectorSchemas.Find(InspectorSchemas.CharaEdit, InspectorSchemas.CharaEditPath("hair.back.mesh.layout"), c).Default)).ToList();
            t.Check(lay.SequenceEqual(new[] { 1.0, 1.0, 0.0, 0.0 }), "hair defaults: the mesh layout (1, 1, 0, 0) — the constants of its ↺ (J8)");
        }

        static string Show(object d) => d is double[] a ? "[" + string.Join(", ", a) + "]" : Convert.ToString(d, System.Globalization.CultureInfo.InvariantCulture);

        // the gate a row carries, by its member (without the part prefix) or action
        static string ExpectGate(FieldSpec f, int k)
        {
            if (f.Kind == FieldKind.Button) return f.Action.StartsWith(InspectorSchemas.ActionMatchHairToSkinPrefix, StringComparison.Ordinal) ? "skin" : "bundles";
            if (f.Kind == FieldKind.Push) return "prefab";
            string m = Member(f).Substring(("hair." + Parts[k] + ".").Length);
            switch (m)
            {
                case "item": return null;
                case "baseColor": case "metallic": case "smoothness": return "prefab";
                case "topColor": return "topRow";
                case "underColor": return "underRow";
                case "specular": return "glossRow";
                case "accessoryColors[0]": return "acs1";
                case "accessoryColors[1]": return "acs2";
                case "accessoryColors[2]": return "acs3";
                case "bundles": return "bundles";
                case "mesh.item": return "mesh";
                case "mesh.color": case "mesh.layout": return "meshSet";
            }
            return "?";
        }

        // feature → the section gate it implies (rows of a gated section carry its gate or a narrower one)
        static readonly Dictionary<string, string> Implies = new Dictionary<string, string>
        {
            ["prefab"] = "prefab", ["topRow"] = "prefab", ["underRow"] = "prefab", ["glossRow"] = "prefab", ["skin"] = "prefab",
            ["acs"] = "acs", ["acs1"] = "acs", ["acs2"] = "acs", ["acs3"] = "acs", ["bundles"] = "bundles", ["mesh"] = "mesh", ["meshSet"] = "mesh",
        };

        static void Gates(TestRunner t)
        {
            int bad = 0;
            foreach (var s in HairSections)
            {
                int k = InspectorSchemas.HairPartOf(s.Id);
                if (k < 0) { if (s.Gate != null || s.Fields.Any(f => f.Gate != null)) { bad++; t.Check(false, "hair gates: " + s.Id + " is ungated"); } continue; }
                foreach (var f in s.Fields)
                {
                    string e = ExpectGate(f, k);
                    string want = e == null ? null : SchemaGates.MakerHair(k, e);
                    if (f.Gate != want) { bad++; t.Check(false, "hair gates: " + Key(f) + " gate " + (f.Gate ?? "none") + ", expected " + (want ?? "none")); }
                }
                string sec = s.Id.Substring(s.Id.LastIndexOf('.') + 1);
                string secWant = sec == "style" ? null : SchemaGates.MakerHair(k, sec == "colour" ? "prefab" : sec == "decoration" ? "acs" : sec);
                if (s.Gate != secWant) { bad++; t.Check(false, "hair gates: section " + s.Id + " gate " + (s.Gate ?? "none") + ", expected " + (secWant ?? "none")); }
                if (s.Gate != null)
                    foreach (var f in s.Fields)
                        if (f.Gate == null || !SchemaGates.TryMakerHair(f.Gate, out _, out string ff) || SchemaGates.MakerHair(k, Implies[ff]) != s.Gate)
                        { bad++; t.Check(false, "hair gates: " + Key(f) + " (" + f.Gate + ") does not imply its section's gate " + s.Gate); }
            }
            t.Equal(0, bad, "hair gates: every row and section carries its gate (style ungated; colour prefab with topRow / underRow / glossRow / skin; decoration acs with acs1–3; bundles; mesh with meshSet), rows imply their section's gate");
            t.Check(HairRows.Where(f => f.Gate != null).All(f => SchemaGates.IsKnown(f.Gate)), "hair gates: every gate is known to SchemaGates.IsKnown");
            // the names parse alike in both layers (the scene layer has its own copy of the names)
            int same = 0, n = 0;
            for (int k = 0; k < 4; k++)
                foreach (var feat in SchemaGates.HairFeatures)
                {
                    n++;
                    string g = SchemaGates.MakerHair(k, feat);
                    if (g == "maker.hair:" + Parts[k] + ":" + feat && SchemaGates.TryMakerHair(g, out int p1, out string f1) && MakerEdits.TryHairGate(g, out int p2, out string f2) &&
                        p1 == k && p2 == k && f1 == feat && f2 == feat) same++;
                }
            t.Check(same == n && n == 48 && SchemaGates.HairFeatures.SequenceEqual(MakerEdits.HairGateFeatures) && SchemaGates.MakerHairPrefix == MakerEdits.HairGatePrefix,
                "hair gates: the 48 gate names parse alike in SchemaGates.TryMakerHair and MakerEdits.TryHairGate (" + same + " / " + n + ")");
            foreach (var g in new[] { "maker.hair:top:prefab", "maker.hair:back:colour", "maker.hair:back", "maker.hair::prefab", "maker.list:300", "maker.hair:back:acs4", null })
                t.Check(!SchemaGates.TryMakerHair(g, out _, out _) && !MakerEdits.TryHairGate(g, out _, out _) && (g == "maker.list:300" ? SchemaGates.IsKnown(g) : !SchemaGates.IsKnown(g)),
                    "hair gates: " + (g ?? "null") + " is not a hair gate");
            t.Check(SchemaGates.HairAcsN(2) == "acs2" && SchemaGates.MakerHair(1, SchemaGates.HairMeshSet) == "maker.hair:front:meshSet", "hair gates: name helpers");
            // 4.5: the accessory family parses alike in both gate parsers too (SchemaGates.TryMakerAcc and MakerGate.TryParse)
            int accSame = 0, accN = 0;
            for (int slot = 0; slot < 62; slot++)
                foreach (var feat in SchemaGates.AccFeatures)
                {
                    accN++;
                    string g = SchemaGates.MakerAcc(slot, feat);
                    if (SchemaGates.TryMakerAcc(g, out int s1, out string f1) && MakerGate.TryParse(g, out var mg) && mg.Family == MakerGate.Acc && mg.Index == slot && mg.Feature == feat &&
                        mg.Name == g && s1 == slot && f1 == feat && SchemaGates.IsKnown(g)) accSame++;
                }
            t.Check(accSame == accN && accN == 62 * 13 && MakerGate.FeaturesOf(MakerGate.Acc).SequenceEqual(SchemaGates.AccFeatures),
                "hair gates: the accessory family (4.5) parses alike in SchemaGates.TryMakerAcc and MakerGate.TryParse on slots 0..61 (" + accSame + " / " + accN + ")");
            foreach (var g in new[] { "maker.acc:x:prefab", "maker.acc:4", "maker.acc:4:nonsense", "maker.acc:12345:hair" })
                t.Check(!SchemaGates.TryMakerAcc(g, out _, out _) && !MakerGate.TryParse(g, out _), "hair gates: " + g + " is refused by both parsers");
        }

        static void Watch(TestRunner t)
        {
            int bad = 0;
            foreach (var s in HairSections)
            {
                int k = InspectorSchemas.HairPartOf(s.Id);
                string[] want;
                string p = k >= 0 ? Root + "hair." + Parts[k] + "." : null;
                string sec = s.Id.Substring(s.Id.LastIndexOf('.') + 1);
                if (k < 0 || sec == "style") want = null;
                else if (sec == "colour") want = new[] { Root + "hair.editor.autoSetting", p + "item" };
                else if (sec == "mesh") want = new[] { p + "item", p + "mesh.item" };
                else want = new[] { p + "item" };
                bool ok = want == null ? s.Watch == null : s.Watch != null && s.Watch.SequenceEqual(want);
                if (!ok) { bad++; t.Check(false, "hair watch: " + s.Id + " watches " + (s.Watch == null ? "nothing" : string.Join(", ", s.Watch))); }
            }
            t.Equal(0, bad, "hair watch: colour watches auto + the item, decoration / bundles the item, mesh the item and the mesh item; others nothing");
            t.Check(InspectorSchemas.CharaEdit.Where(s => InspectorSchemas.CharaEditTreeOf(s) == "face" || InspectorSchemas.CharaEditTreeOf(s) == "body").All(s => s.Watch == null && s.Gate == null),
                "hair watch: face and body sections have no Watch / Gate");
            var sess = new SceneSession(new SessionOptions { Ids = new CountingIdSource(81) });
            string id = sess.AddChara(new CharaAppearance { Stored = new CharaSubtree() }).NewIds[0];
            int resolves = 0, total = 0;
            foreach (var s in HairSections.Where(x => x.Watch != null))
                foreach (var w in s.Watch)
                {
                    total++;
                    try { FieldAccessor.LeafType(sess.Model, id, w); resolves++; } catch (CommandException) { t.Check(false, "hair watch: " + w + " does not resolve"); }
                }
            t.Check(resolves == total && total == 4 * 6, "hair watch: the " + total + " watched paths resolve on a character");
        }

        static void Labels(TestRunner t)
        {
            L10nTable tb;
            try { tb = UiKit.Strings(t); }
            catch (Exception e) { t.Check(false, "hair labels: strings.tsv loads: " + e.Message); return; }
            var keys = new SortedSet<string>(StringComparer.Ordinal) { InspectorSchemas.CharaEditPageLabel("hair", "hair") };
            foreach (var s in HairSections)
            {
                keys.Add(s.LabelKey);
                foreach (var f in s.Fields)
                {
                    keys.Add(f.LabelKey);
                    if (f.Choices != null) for (int i = 0; i < f.Choices.Count; i++) keys.Add(f.ChoiceLabelKey(i));
                }
            }
            for (int k = 0; k < 4; k++) keys.Add(InspectorSchemas.HairPartLabelKey(k));
            foreach (var n in InspectorSchemas.HairBundleComponentNames) keys.Add("inspector.charaEdit.hair." + n);
            keys.Add("inspector.charaEdit.hair." + InspectorSchemas.HairBundleNoShakeName);
            keys.Add("inspector.charaEdit.hair." + InspectorSchemas.HairBundleTitleName);
            foreach (var lang in new[] { "en", "ja", "zh-Hans" })
            {
                var missing = keys.Where(k => string.IsNullOrEmpty(tb.Cell(k, lang))).ToList();
                t.Check(missing.Count == 0, "hair labels: " + keys.Count + " keys have " + lang + " (" + string.Join(", ", missing.Take(12)) + ")");
            }
            t.Check(HairSections.Where(s => InspectorSchemas.HairPartOf(s.Id) >= 0).All(s => s.LabelKey == "inspector.charaEdit.page.hair.hair." + s.Id.Substring(s.Id.LastIndexOf('.') + 1)),
                "hair labels: the part sections share one label key per section kind");
            t.Equal("前髪と後ろ髪の色を統一", tb.Cell("inspector.charaEdit.hair.sameSetting", "ja"), "hair labels: «前髪と後ろ髪の色を統一» (HS2's label)");
            t.Equal("エクステ", tb.Cell(InspectorSchemas.HairPartLabelKey(3), "ja"), "hair labels: the extension part is «エクステ»");
            t.Equal("調整{0}", tb.Cell("inspector.charaEdit.hair.bundleTitle", "ja"), "hair labels: the bundle title «調整NN» with the number as {0}");
            t.Equal(6, InspectorSchemas.HairBundleComponentNames.Count, "hair labels: six bundle component sliders (move X / Y / Z, rotate X / Y / Z)");
        }
    }
}
