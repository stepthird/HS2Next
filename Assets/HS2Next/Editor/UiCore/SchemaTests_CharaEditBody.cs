// The Body pages of the Edit tab (InspectorSchemas
// .CharaEdit): every body row resolves on a character with stored documents with its declared leaf type (component rows on
// Vec4, PartList rows on `ref` strings, the swatch push on the skin colour, the futanari toggle on a bool) for every {k}
// expansion of the repeated paint section; counts as listed with the futanari toggle of Y9 (definitions 48 sliders — 36
// on the Shape page —, 6 colours, 7 PartList, 1 Push, 1 toggle; instances per sex female 55 / 7 / 9 / 1 + 1 toggle, male 47
// / 7 / 9 / 1, 48 sliders with maker.bodyHeight open); ColorAlpha false exactly for the skin; Default = the document defaults
//; LerpClamped exactly rows; list categories per sex; history labels; gates (chara.female on
// the female-only rows, maker.bodyHeight on the height); section ids parse into node / page / section and the pages are
// BodyPages in order; every label key (pages, sections, instances, rows, the tooltip) has en, ja and zh-Hans.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HS2Next.UI.Core.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;
    using HS2Next.Scenes;
    using HS2Next.UI.Core;

    public static class SchemaTests_CharaEditBody
    {
        public static void Run(TestRunner t)
        {
            Structure(t);
            Resolve(t);
            Counts(t);
            Rules(t);
            Defaults(t);
            Gates(t);
            Labels(t);
        }

        const string Root = "appearance.chara.";

        static IEnumerable<SchemaSection> BodySections => InspectorSchemas.CharaEdit.Where(s => InspectorSchemas.CharaEditTreeOf(s) == "body");
        static IEnumerable<FieldSpec> BodyRows => BodySections.SelectMany(s => s.Fields);
        static string Member(FieldSpec f) => f.Path.StartsWith(Root, StringComparison.Ordinal) ? f.Path.Substring(Root.Length) : f.Path;

        /// <summary>The expanded paths of a row: {k} → 0 / 1.</summary>
        static IEnumerable<string> Expansions(FieldSpec f)
        {
            if (f.Path == null) yield break;
            if (!f.IsTemplate) { yield return f.Path; yield break; }
            if (f.Placeholder == InspectorSchemas.PaintPlaceholder) { yield return f.Expand("0"); yield return f.Expand("1"); yield break; }
            yield return null;   // an unknown placeholder
        }

        static int InstanceCount(FieldSpec f) => f.IsTemplate ? 2 : 1;

        static void Structure(TestRunner t)
        {
            var secs = BodySections.ToList();
            var pages = secs.Select(InspectorSchemas.CharaEditPageOf).ToList();
            t.Check(secs.All(s => s.Id.Split('.').Length == 3), "body: section ids are <node>.<page>.<section>");
            var order = pages.Distinct().ToList();
            t.Check(order.SequenceEqual(InspectorSchemas.BodyPages), "body: the pages order (" + string.Join(", ", order) + ")");
            t.Check(InspectorSchemas.BodyPages.SequenceEqual(new[] { "shape", "skin", "sunburn", "nipple", "underhair", "nail", "paint", "special" }),
                "body: BodyPages = Shape · Skin · Tan · Nipples · Underhair · Nails · Paint (Y2) · Special (Y9)");
            bool contiguous = true;
            for (int i = 1; i < pages.Count; i++)
                if (pages[i] != pages[i - 1] && pages.Take(i).Contains(pages[i])) contiguous = false;
            t.Check(contiguous, "body: each page's sections are contiguous");
            t.Check(InspectorSchemas.CharaEditPagesOf("body").SequenceEqual(InspectorSchemas.BodyPages), "body: CharaEditPagesOf(body) = BodyPages");
            var expectSections = new[]
            {
                "body.shape.whole", "body.shape.breast", "body.shape.upper", "body.shape.lower", "body.shape.arm", "body.shape.leg",
                "body.skin.type", "body.skin.detail", "body.skin.colour", "body.sunburn.type", "body.nipple.type", "body.underhair.type",
                "body.nail.colour", "body.paint.p{k}", "body.special.futanari",
            };
            t.Check(secs.Select(s => s.Id).SequenceEqual(expectSections), "body: the sections (" + string.Join(", ", secs.Select(s => s.Id)) + ")");
            var paint = secs.Single(s => s.Id == "body.paint.p{k}");
            t.Check(paint.IsRepeated && paint.Placeholder == "k" && paint.Instances.Select(i => i.Value + "=" + i.Id + "=" + i.LabelKey).SequenceEqual(new[]
                { "0=body.paint.p1=inspector.charaEdit.page.body.paint.p1", "1=body.paint.p2=inspector.charaEdit.page.body.paint.p2" }),
                "body: the paint section repeats as body.paint.p1 / p2 with {k} = 0 / 1 (Y3)");
            t.Check(paint.Fields.All(f => f.Placeholder == "k"), "body: every paint row is a {k} template");
            t.Check(secs.Where(s => !s.IsRepeated).All(s => s.Fields.All(f => !f.IsTemplate)), "body: no template outside the paint section");
            var allIds = secs.SelectMany(s => s.IsRepeated ? s.Instances.Select(i => i.Id) : new[] { s.Id }).ToList();
            t.Check(allIds.Distinct().Count() == allIds.Count, "body: section (instance) ids are distinct");
            var rowIds = BodyRows.Select(f => f.Id).ToList();
            t.Check(rowIds.Distinct().Count() == rowIds.Count, "body: row ids are distinct");
            var faceIds = new HashSet<string>(InspectorSchemas.CharaEdit.Where(s => InspectorSchemas.CharaEditTreeOf(s) == "face").SelectMany(s => s.Fields).Select(f => f.Id));
            t.Check(rowIds.All(id => !faceIds.Contains(id)), "body: no row id is also a face row id");
            t.Equal("paint", InspectorSchemas.CharaEditPageOf("body.paint.p{k}"), "body: page of body.paint.p{k}");
            t.Equal("shape", InspectorSchemas.CharaEditPageOf("body.shape.breast"), "body: page of body.shape.breast");
            t.Equal(6, InspectorSchemas.CharaEditSections("body", "shape").Count(), "body: CharaEditSections(body, shape) = 6 sections");
            t.Check(InspectorSchemas.CharaEditPagesOf("face").SequenceEqual(InspectorSchemas.FacePages), "body: the Face node keeps FacePages");
            // the Shape page: 33 names, every shape index once plus softness, weight, areola size, in HS2's order
            t.Check(InspectorSchemas.BodyShapeNames.Count == BodyData.ShapeCount && InspectorSchemas.BodyShapeNames.Distinct().Count() == BodyData.ShapeCount,
                "body: 33 distinct shape names (BodyShapeIdx order)");
            var codes = InspectorSchemas.BodyShapeSections.SelectMany(x => x.rows).ToList();
            t.Check(codes.Count == 36 && codes.OrderBy(c => c).SequenceEqual(Enumerable.Range(0, 36)), "body: BodyShapeSections hold the 33 indices and the 3 bust rows once each");
            var shapeRows = BodySections.Where(s => InspectorSchemas.CharaEditPageOf(s) == "shape").SelectMany(s => s.Fields).Select(Member).ToList();
            var expectShape = codes.Select(c => c == InspectorSchemas.BodyBustSoftnessRow ? "body.bust.softness" : c == InspectorSchemas.BodyBustWeightRow ? "body.bust.weight"
                : c == InspectorSchemas.BodyAreolaSizeRow ? "body.nipple.areola" : "body.shape[" + c + "]").ToList();
            t.Check(shapeRows.SequenceEqual(expectShape), "body: the Shape page rows in HS2's UI order (" + string.Join(", ", shapeRows.Take(14)) + " …)");
            var breast = BodySections.Single(s => s.Id == "body.shape.breast").Fields.Select(Member).ToList();
            t.Check(breast.SequenceEqual(new[] { "body.shape[1]", "body.shape[2]", "body.shape[3]", "body.shape[4]", "body.shape[5]", "body.shape[6]",
                "body.bust.softness", "body.bust.weight", "body.shape[7]", "body.nipple.areola", "body.shape[8]", "body.shape[32]" }),
                "body: Breast = size, height, spread, position, angle, sharpness, softness, weight, areola bulge, areola size, nipple width, nipple stand");
            var shapeLabels = BodySections.Where(s => InspectorSchemas.CharaEditPageOf(s) == "shape").SelectMany(s => s.Fields)
                .Where(f => f.Path.StartsWith(Root + "body.shape[", StringComparison.Ordinal))
                .All(f => f.LabelKey == "inspector.charaEdit.body." + InspectorSchemas.BodyShapeNames[int.Parse(f.Path.Substring((Root + "body.shape[").Length).TrimEnd(']'))]);
            t.Check(shapeLabels, "body: shape rows are labelled inspector.charaEdit.body.<BodyShapeNames[i]>");
        }

        static void Resolve(TestRunner t)
        {
            var s = new SceneSession(new SessionOptions { Ids = new CountingIdSource(41) });
            var r = s.AddChara(new CharaAppearance { Stored = new CharaSubtree() });
            t.Check(r.Status == CommandStatus.Ok, "body resolve: a character with stored documents");
            string id = r.NewIds[0];
            int n = 0, fails = 0;
            foreach (var f in BodyRows)
            {
                if (f.Kind == FieldKind.Button) { fails++; t.Check(false, "body: unexpected button " + f.LabelKey); continue; }
                foreach (var path in Expansions(f))
                {
                    n++;
                    if (path == null) { fails++; t.Check(false, "body: " + f.Path + " has an unknown placeholder"); continue; }
                    try
                    {
                        var leaf = FieldAccessor.LeafType(s.Model, id, path);
                        if (!Fits(f, leaf)) { fails++; t.Check(false, "body: " + path + (f.Component >= 0 ? "#" + f.Component : "") + " is a " + leaf.Name + ", declared " + f.Kind); }
                        if (!FieldAccessor.IsWritable(s.Model, id, path, out string code)) { fails++; t.Check(false, "body: " + path + " is not writable (" + code + ")"); }
                        if (AppearancePaths.IsRebuildPath(path)) { fails++; t.Check(false, "body: " + path + " is a rebuild-only member"); }
                    }
                    catch (CommandException e) { fails++; t.Check(false, "body: " + path + " does not resolve: " + e.Code + " " + e.Message); }
                }
            }
            t.Check(fails == 0 && n > 0, "body resolve: " + n + " expanded rows resolve with their declared leaf types, are writable and no rebuild path");
            var w = InspectorSchemas.Find(InspectorSchemas.CharaEdit, InspectorSchemas.CharaEditPath("body.paint[{k}].layout"), 0);
            t.Check(w != null && w.LabelKey == "inspector.charaEdit.body.paintWidth", "body: «幅» = body.paint[k].layout component 0");
            // a typed 150 on a shape row stores 1.5 (Q23)
            var row = InspectorSchemas.Find(InspectorSchemas.CharaEdit, InspectorSchemas.CharaEditPath("body.shape[9]"));
            t.Check(row != null && s.Set(id, row.Path, (float)row.ParseTyped(150)).Status == CommandStatus.Ok && (float)FieldAccessor.Get(s.Model, id, row.Path) == 1.5f,
                "body: typed 150 into «頭サイズ» stores 1.5");
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
                case FieldKind.Color: return leaf == typeof(Color4);
                case FieldKind.PartList: return leaf == typeof(string);
                case FieldKind.Push: return PushSources.ColourCategory(f.PushSource) >= 0 && leaf == typeof(Color4);
            }
            return false;
        }

        static bool IsSlider(FieldSpec f) => f.Kind == FieldKind.Float && f.Scale == InspectorSchemas.MakerSliderScale;

        // The per-sex instance count of the rows of a kind; heightOpen = the maker.bodyHeight gate for males.
        static int PerSex(Func<FieldSpec, bool> kind, int sex, bool heightOpen, IEnumerable<FieldSpec> rows = null) =>
            (rows ?? BodyRows).Where(kind).Where(f => Shown(f, sex, heightOpen)).Sum(InstanceCount);

        static bool Shown(FieldSpec f, int sex, bool heightOpen)
        {
            if (f.Kind == FieldKind.PartList && f.CategoryFor(sex) < 0) return false;
            switch (f.Gate)
            {
                case null: return true;
                case SchemaGates.CharaMale: return sex == 0;
                case SchemaGates.CharaFemale: return sex == 1;
                case SchemaGates.MakerBodyHeight: return sex == 1 || heightOpen;
            }
            return SchemaGates.MakerListCategory(f.Gate) < 0 || f.CategoryFor(sex) >= 0;
        }

        static void Counts(TestRunner t)
        {
            var rows = BodyRows.ToList();
            Func<FieldSpec, bool> slider = IsSlider, colour = f => f.Kind == FieldKind.Color, list = f => f.Kind == FieldKind.PartList,
                push = f => f.Kind == FieldKind.Push, toggle = f => f.Kind == FieldKind.Bool, button = f => f.Kind == FieldKind.Button;
            var defs = new[] { rows.Count(slider), rows.Count(colour), rows.Count(list), rows.Count(push), rows.Count(toggle), rows.Count(button) };
            t.Check(defs.SequenceEqual(new[] { 48, 6, 7, 1, 1, 0 }), "body counts: definitions sliders / colours / lists / pushes / toggles / buttons = 48 / 6 / 7 / 1 / 1 / 0 (" + string.Join(" / ", defs) + ")");
            t.Equal(rows.Count, defs.Sum(), "body counts: no row of another kind");
            var shapePage = BodySections.Where(s => InspectorSchemas.CharaEditPageOf(s) == "shape").SelectMany(s => s.Fields).ToList();
            t.Equal(36, shapePage.Count(slider), "body counts: 36 of the sliders are Shape-page rows (33 shapes + softness, weight, areola size)");
            t.Equal(33, rows.Count(f => slider(f) && f.Path.StartsWith(Root + "body.shape[", StringComparison.Ordinal)), "body counts: 33 shape sliders");
            var female = new[] { PerSex(slider, 1, true), PerSex(colour, 1, true), PerSex(list, 1, true), PerSex(push, 1, true), PerSex(toggle, 1, true) };
            t.Check(female.SequenceEqual(new[] { 55, 7, 9, 1, 1 }), "body counts: female instances sliders / colours / lists / pushes / toggles = 55 / 7 / 9 / 1 / 1 (" + string.Join(" / ", female) + ")");
            var male = new[] { PerSex(slider, 0, false), PerSex(colour, 0, false), PerSex(list, 0, false), PerSex(push, 0, false), PerSex(toggle, 0, false) };
            t.Check(male.SequenceEqual(new[] { 47, 7, 9, 1, 0 }), "body counts: male instances with the height pinned = 47 / 7 / 9 / 1 / 0 (" + string.Join(" / ", male) + ")");
            t.Equal(48, PerSex(slider, 0, true), "body counts: male sliders with maker.bodyHeight open (the default, Y8) = 48");
            t.Equal(36, PerSex(slider, 1, true, shapePage), "body counts: the female Shape page shows 36 rows");
            t.Equal(28, PerSex(slider, 0, false, shapePage), "body counts: the male Shape page shows 28 rows with the height pinned (36 − 8)");
            t.Equal(29, PerSex(slider, 0, true, shapePage), "body counts: the male Shape page shows 29 rows by default (UI5 step 13)");
        }

        static readonly string[] LerpMembers =
        {
            "body.bust.weight", "body.shape[32]", "body.skin.gloss",
            "body.paint[{k}].layout#0", "body.paint[{k}].layout#1", "body.paint[{k}].layout#2", "body.paint[{k}].layout#3", "body.paint[{k}].rotation",
        };

        static string Key(FieldSpec f) => Member(f) + (f.Component >= 0 ? "#" + f.Component : "");

        static void Rules(TestRunner t)
        {
            var rows = BodyRows.ToList();
            var sliders = rows.Where(IsSlider).ToList();
            int bad = sliders.Count(f => f.Min != 0 || f.Max != 1 || f.TypedMin != -1 || f.TypedMax != 2 || f.Format != "0" || f.Applies != Applies.All || f.OpLabel != OpLabels.EditBody);
            t.Check(bad == 0, "body rules: every slider has Q23's ranges (0..1, typed −1..2, ×100, \"0\"), all selected, label Edit body (" + bad + " differ)");
            t.Check(rows.Where(f => f.Kind == FieldKind.Float).All(IsSlider), "body rules: every float row is a Maker slider");
            var noAlpha = rows.Where(f => f.Kind == FieldKind.Color && !f.ColorAlpha).Select(Member).ToList();
            t.Check(noAlpha.SequenceEqual(new[] { "body.skin.color" }), "body rules: ColorAlpha false exactly for the skin colour (" + string.Join(", ", noAlpha) + ")");
            t.Check(rows.Where(f => f.Kind == FieldKind.Color).All(f => f.OpLabel == OpLabels.EditBody), "body rules: colour rows are labelled Edit body");
            var lerp = sliders.Where(f => f.LerpClamped).Select(Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
            t.Check(lerp.SequenceEqual(LerpMembers.OrderBy(x => x, StringComparer.Ordinal)), "body rules: LerpClamped exactly on BS10, BS14, BS38, BS44–BS48 (" + string.Join(", ", lerp) + ")");
            var cats = rows.Where(f => f.Kind == FieldKind.PartList).ToDictionary(Member, f => (f.CategoryFemale, f.CategoryMale));
            var expect = new Dictionary<string, (int, int)>
            {
                ["body.skin.item"] = (231, 131), ["body.detail.item"] = (232, 132), ["body.sunburn.item"] = (233, 133), ["body.nipple.item"] = (334, 334),
                ["body.underhair.item"] = (335, 335), ["body.paint[{k}].item"] = (313, 313), ["body.paint[{k}].layoutItem"] = (8, 8),
            };
            t.Check(cats.Count == expect.Count && expect.All(kv => cats.TryGetValue(kv.Key, out var c) && c.Equals(kv.Value)),
                "body rules: PartList categories per sex as listed (" + string.Join(", ", cats.Select(kv => kv.Key + " " + kv.Value)) + ")");
            t.Check(rows.Where(f => f.Kind == FieldKind.PartList).All(f => f.OpLabel == OpLabels.ChangeItem && f.Default == null && f.ListContext == null && f.ThumbColumn == null),
                "body rules: lists are labelled Change item, have no default (no reset), no context and the ThumbTex thumbnails");
            var pushes = rows.Where(f => f.Kind == FieldKind.Push).Select(f => Member(f) + " " + f.PushSource + " " + f.OpLabel).ToList();
            t.Check(pushes.SequenceEqual(new[] { "body.skin.color colour:336 Edit body" }), "body rules: the one push is the skin swatch grid (colour:336, Edit body; " + string.Join("; ", pushes) + ")");
            t.Check(PushSources.Colour(336) == "colour:336" && PushSources.ColourCategory("colour:336") == 336 && PushSources.ColourCategory("colour:x") == -1 &&
                    PushSources.ColourCategory("layout:7") == -1 && PushSources.LayoutCategory("colour:336") == -1 && PushSources.ColourCategory(null) == -1 &&
                    PushSources.SkinColourCategory == 336 && PushSources.SkinColourPresetIds == 7 && PushSources.HairColourCategory == 305,
                "body rules: PushSources.Colour / ColourCategory, the skin list 336 with 7 cells");
            t.Check(MakerEdits.ColourPresetCategoryOf(rows.Single(f => f.Kind == FieldKind.Push).Path) == 336 && MakerEdits.SkinColourPresetIds == PushSources.SkinColourPresetIds,
                "body rules: the push path is the op's colour path (336) and both layers agree on 7 cells");
            var futa = rows.Single(f => f.Kind == FieldKind.Bool);
            t.Check(Member(futa) == "profile.futanari" && Equals(futa.Default, false) && futa.OpLabel == OpLabels.EditBody && futa.Gate == SchemaGates.CharaFemale &&
                    futa.TooltipKey == "inspector.charaEdit.body.futanariTip" && futa.Applies == Applies.All,
                "body rules: the futanari toggle (Y9): profile.futanari, default off, female only, Edit body, a tooltip key");
            t.Check(rows.Where(f => f.Kind != FieldKind.Bool).All(f => f.TooltipKey == null), "body rules: only the toggle has a tooltip key");
            t.Equal("body", MakerEdits.AreaOf(new[] { futa.Path }), "body rules: the toggle's member is in the body area (label Edit body through the bridge too)");
            t.Check(MakerEdits.CategoryOf(InspectorSchemas.CharaEditPath("body.paint[1].layoutItem"), 0) == 8 && MakerEdits.CategoryOf(InspectorSchemas.CharaEditPath("body.skin.item"), 0) == 131,
                "body rules: the region and skin rows are item changes of their categories (Change item)");
        }

        // "Default" = the document default for every value row.
        static void Defaults(TestRunner t)
        {
            var s = new SceneSession(new SessionOptions { Ids = new CountingIdSource(51) });
            string id = s.AddChara(new CharaAppearance { Stored = new CharaSubtree() }).NewIds[0];
            int n = 0;
            var bad = new List<string>();
            foreach (var f in BodyRows)
            {
                if (f.Kind == FieldKind.PartList || f.Kind == FieldKind.Push) { if (f.Default != null) bad.Add(f.Path + " has a default"); continue; }
                foreach (var path in Expansions(f))
                {
                    n++;
                    var v = FieldAccessor.Get(s.Model, id, path);
                    bool ok;
                    if (f.Kind == FieldKind.Bool) ok = Equals(f.Default, v);
                    else if (f.Kind == FieldKind.Color) ok = f.Default is double[] d && d.Length == 4 && v is Color4 c && (float)d[0] == c.R && (float)d[1] == c.G && (float)d[2] == c.B && (float)d[3] == c.A;
                    else if (f.Component >= 0) ok = f.Default is double x && v is Vec4 a && (float)x == (f.Component == 0 ? a.X : f.Component == 1 ? a.Y : f.Component == 2 ? a.Z : a.W);
                    else ok = f.Default is double y && v is float fv && (float)y == fv;
                    if (!ok) bad.Add(path + (f.Component >= 0 ? "#" + f.Component : "") + " " + FieldJson.ToText(v) + " vs " + Show(f.Default));
                }
            }
            t.Check(bad.Count == 0 && n > 0, "body defaults: " + n + " expanded value rows default to the document defaults (" + string.Join("; ", bad.Take(8)) + ")");
            Func<string, int, double> def = (p, c) => Convert.ToDouble(InspectorSchemas.Find(InspectorSchemas.CharaEdit, InspectorSchemas.CharaEditPath(p), c).Default);
            t.Check(def("body.shape[32]", -1) == 0 && def("body.shape[0]", -1) == 0.5 && def("body.nipple.areola", -1) == 0.7 && def("body.detail.power", -1) == 0.5 &&
                    def("body.skin.gloss", -1) == 0.7 && def("body.skin.metallic", -1) == 0 && def("body.nail.gloss", -1) == 0.6 && def("body.paint[{k}].layout", 3) == 0.5,
                "body defaults: nipple stand 0, height 0.5, areola 0.7, detail power 0.5, gloss 0.7, metallic 0, nail gloss 0.6, paint layout 0.5");
            var skin = InspectorSchemas.Find(InspectorSchemas.CharaEdit, InspectorSchemas.CharaEditPath("body.skin.color")).Default as double[];
            t.Check(skin != null && skin.SequenceEqual(new[] { 0.8, 0.7, 0.64, 1.0 }), "body defaults: skin colour (0.8, 0.7, 0.64, 1)");
        }

        static string Show(object d) => d is double[] a ? "[" + string.Join(", ", a) + "]" : Convert.ToString(d, System.Globalization.CultureInfo.InvariantCulture);

        static void Gates(TestRunner t)
        {
            var gated = BodyRows.Where(f => f.Gate != null).Select(f => Member(f) + "=" + f.Gate).ToList();
            t.Check(gated.SequenceEqual(new[]
                {
                    "body.shape[0]=maker.bodyHeight", "body.shape[5]=chara.female", "body.shape[6]=chara.female", "body.bust.softness=chara.female",
                    "body.bust.weight=chara.female", "body.shape[7]=chara.female", "body.shape[8]=chara.female", "body.shape[32]=chara.female",
                    "profile.futanari=chara.female",
                }),
                "body gates: the 8 † rows (height maker.bodyHeight, 7 chara.female) and the futanari toggle (" + string.Join(", ", gated) + ")");
            t.Check(BodyRows.Where(f => f.Gate != null).All(f => SchemaGates.IsKnown(f.Gate)), "body gates: every gate is a SchemaGates name");
            t.Check(SchemaGates.All.Contains(SchemaGates.MakerBodyHeight) && SchemaGates.MakerBodyHeight == "maker.bodyHeight", "body gates: maker.bodyHeight in All");
            for (int i = 0; i < BodyData.ShapeCount; i++)
            {
                bool female = InspectorSchemas.IsFemaleOnlyBodyShape(i);
                if (female != (i == 5 || i == 6 || i == 7 || i == 8 || i == 32)) t.Check(false, "body gates: IsFemaleOnlyBodyShape(" + i + ")");
            }
            // the Special page: nothing for males (its button hides); every other page shows rows for both sexes
            var special = BodySections.Where(s => InspectorSchemas.CharaEditPageOf(s) == "special").SelectMany(s => s.Fields).ToList();
            t.Check(special.Count == 1 && special.All(f => !Shown(f, 0, true)) && special.All(f => Shown(f, 1, true)), "body gates: the Special page shows its toggle to females only");
            t.Check(BodySections.Where(s => InspectorSchemas.CharaEditPageOf(s) != "special").GroupBy(InspectorSchemas.CharaEditPageOf)
                    .All(g => g.SelectMany(s => s.Fields).Any(f => Shown(f, 0, false)) && g.SelectMany(s => s.Fields).Any(f => Shown(f, 1, false))),
                "body gates: the seven other pages show rows for both sexes");
        }

        static void Labels(TestRunner t)
        {
            L10nTable tb;
            try { tb = UiKit.Strings(t); }
            catch (Exception e) { t.Check(false, "body labels: strings.tsv loads: " + e.Message); return; }
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var p in InspectorSchemas.BodyPages) keys.Add(InspectorSchemas.CharaEditPageLabel("body", p));
            foreach (var s in BodySections)
            {
                if (s.IsRepeated) { t.Check(s.LabelKey == null, "body labels: the repeated section " + s.Id + " is labelled by its instances"); foreach (var i in s.Instances) keys.Add(i.LabelKey); }
                else keys.Add(s.LabelKey);
                foreach (var f in s.Fields) { keys.Add(f.LabelKey); if (f.TooltipKey != null) keys.Add(f.TooltipKey); }
            }
            foreach (var lang in new[] { "en", "ja", "zh-Hans" })
            {
                var missing = keys.Where(k => string.IsNullOrEmpty(tb.Cell(k, lang))).ToList();
                t.Check(missing.Count == 0, "body labels: " + keys.Count + " keys have " + lang + " (" + string.Join(", ", missing.Take(12)) + ")");
            }
            t.Equal("身長", tb.Cell("inspector.charaEdit.body.height", "ja"), "body labels: «身長» (HS2's label)");
            t.Equal("特殊", tb.Cell(InspectorSchemas.CharaEditPageLabel("body", "special"), "ja"), "body labels: the Special page is «特殊»");
        }
    }
}
