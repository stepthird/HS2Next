// The Face pages of the Edit tab (InspectorSchemas.CharaEdit): every face
// row resolves on a character with stored documents with its declared leaf type (component rows on Vec2 / Vec4, PartList
// rows on `ref` strings, the face-preset push on the face block, layout pushes on Vec4) for every template expansion
// ({side} ∈ left, right; {k} ∈ 0, 1 of the repeated paint section); counts as listed (definitions 91 sliders, 12
// colours, 14 PartList, 3 Push, 1 toggle, 2 buttons; instances per sex female 103 / 15 / 16 / 4, male 103 / 16 / 16 or 17 /
// 4); ColorAlpha false exactly for the sclera; Default = the document defaults; the lerp-clamped flags (F12); list
// categories per sex; history labels; gates (chara.male, maker.headList, maker.list:<c>); section ids parse into node
// / page / section and the pages are the in order; every label key (pages, sections, instances, rows) has en, ja and
// zh-Hans.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HS2Next.UI.Core.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;
    using HS2Next.Scenes;
    using HS2Next.UI.Core;

    public static class SchemaTests_CharaEditFace
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

        static IEnumerable<SchemaSection> FaceSections => InspectorSchemas.CharaEdit.Where(s => InspectorSchemas.CharaEditTreeOf(s) == "face");
        static IEnumerable<FieldSpec> FaceRows => FaceSections.SelectMany(s => s.Fields);
        static IEnumerable<FieldSpec> NewRows => FaceSections.Where(s => InspectorSchemas.CharaEditPageOf(s) != "shape").SelectMany(s => s.Fields);

        /// <summary>The expanded paths of a row: {side} → left / right, {k} → 0 / 1.</summary>
        static IEnumerable<string> Expansions(FieldSpec f)
        {
            if (f.Path == null) yield break;
            if (!f.IsTemplate) { yield return f.Path; yield break; }
            if (f.Placeholder == InspectorSchemas.SidePlaceholder) { foreach (var e in InspectorSchemas.EyeSides) yield return f.Expand(e); yield break; }
            if (f.Placeholder == InspectorSchemas.PaintPlaceholder) { yield return f.Expand("0"); yield return f.Expand("1"); yield break; }
            yield return null;   // an unknown placeholder
        }

        static int InstanceCount(FieldSpec f) => f.IsTemplate ? 2 : 1;

        static void Structure(TestRunner t)
        {
            var secs = FaceSections.ToList();
            var pages = secs.Select(InspectorSchemas.CharaEditPageOf).ToList();
            t.Check(secs.All(s => s.Id.Split('.').Length == 3), "face: section ids are <node>.<page>.<section>");
            var order = pages.Distinct().ToList();
            t.Check(order.SequenceEqual(InspectorSchemas.FacePages), "face: the pages order (" + string.Join(", ", order) + ")");
            bool contiguous = true;
            for (int i = 1; i < pages.Count; i++)
                if (pages[i] != pages[i - 1] && pages.Take(i).Contains(pages[i])) contiguous = false;
            t.Check(contiguous, "face: each page's sections are contiguous");
            t.Check(InspectorSchemas.CharaEditPagesOf("face").SequenceEqual(InspectorSchemas.FacePages), "face: CharaEditPagesOf(face) = FacePages");
            var expectSections = new[]
            {
                "face.head.type", "face.head.preset", "face.head.skin", "face.head.detail",
                "face.shape.whole", "face.shape.chin", "face.shape.cheek", "face.shape.eyes", "face.shape.nose", "face.shape.mouth", "face.shape.ear",
                "face.eyebrow.type", "face.eyebrow.layout",
                "face.eyes.side", "face.eyes.iris", "face.eyes.pupil", "face.eyes.sclera", "face.eyes.both", "face.eyes.highlight",
                "face.eyelashes.type", "face.makeup.eyeshadow", "face.makeup.cheek", "face.makeup.lip", "face.paint.p{k}",
                "face.mole.type", "face.mole.layout", "face.beard.type",
            };
            t.Check(secs.Select(s => s.Id).SequenceEqual(expectSections), "face: the sections (" + string.Join(", ", secs.Select(s => s.Id)) + ")");
            var paint = secs.Single(s => s.Id == "face.paint.p{k}");
            t.Check(paint.IsRepeated && paint.Placeholder == "k" && paint.Instances.Select(i => i.Value + "=" + i.Id).SequenceEqual(new[] { "0=face.paint.p1", "1=face.paint.p2" }),
                "face: the paint section repeats as face.paint.p1 / p2 with {k} = 0 / 1");
            t.Check(paint.Fields.All(f => f.Placeholder == "k"), "face: every paint row is a {k} template");
            t.Check(secs.Where(s => !s.IsRepeated).All(s => s.Fields.All(f => !f.IsTemplate || f.Placeholder == InspectorSchemas.SidePlaceholder)),
                "face: outside the paint section only {side} templates");
            t.Check(secs.Where(s => s.Fields.Any(f => f.Placeholder == "side")).All(s => InspectorSchemas.CharaEditPageOf(s) == "eyes"), "face: {side} rows only on the Eyes page");
            var allIds = secs.SelectMany(s => s.IsRepeated ? s.Instances.Select(i => i.Id) : new[] { s.Id }).ToList();
            t.Check(allIds.Distinct().Count() == allIds.Count, "face: section (instance) ids are distinct");
            var rowIds = FaceRows.Select(f => f.Id).ToList();
            t.Check(rowIds.Distinct().Count() == rowIds.Count, "face: row ids are distinct");
            t.Equal("paint", InspectorSchemas.CharaEditPageOf("face.paint.p{k}"), "face: page of face.paint.p{k}");
            t.Equal("shape", InspectorSchemas.CharaEditPageOf("face.shape.chin"), "face: page of face.shape.chin");
            t.Check(InspectorSchemas.CharaEditSections("face", "eyes").Count() == 6, "face: CharaEditSections(face, eyes) = 6 sections");
        }

        static void Resolve(TestRunner t)
        {
            var s = new SceneSession(new SessionOptions { Ids = new CountingIdSource(21) });
            var r = s.AddChara(new CharaAppearance { Stored = new CharaSubtree() });
            t.Check(r.Status == CommandStatus.Ok, "face resolve: a character with stored documents");
            string id = r.NewIds[0];
            int n = 0, fails = 0;
            foreach (var f in NewRows)
            {
                if (f.Kind == FieldKind.Button) { if (f.Path != null || string.IsNullOrEmpty(f.Action)) { fails++; t.Check(false, "face: button " + f.LabelKey + " has a path or no action"); } continue; }
                foreach (var path in Expansions(f))
                {
                    n++;
                    if (path == null) { fails++; t.Check(false, "face: " + f.Path + " has an unknown placeholder"); continue; }
                    try
                    {
                        var leaf = FieldAccessor.LeafType(s.Model, id, path);
                        if (!Fits(f, leaf)) { fails++; t.Check(false, "face: " + path + (f.Component >= 0 ? "#" + f.Component : "") + " is a " + leaf.Name + ", declared " + f.Kind); }
                        if (!FieldAccessor.IsWritable(s.Model, id, path, out string code)) { fails++; t.Check(false, "face: " + path + " is not writable (" + code + ")"); }
                    }
                    catch (CommandException e) { fails++; t.Check(false, "face: " + path + " does not resolve: " + e.Code + " " + e.Message); }
                }
            }
            t.Check(fails == 0 && n > 0, "face resolve: " + n + " expanded rows resolve with their declared leaf types and are writable");
            // component rows: the vector member of a row (eyebrow width = layout.z)
            var w = InspectorSchemas.Find(InspectorSchemas.CharaEdit, InspectorSchemas.CharaEditPath("face.eyebrow.layout"), 2);
            t.Check(w != null && w.LabelKey == "inspector.charaEdit.face.eyebrowWidth", "face: «眉の幅» = face.eyebrow.layout component 2");
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
                case FieldKind.Push: return f.PushSource == PushSources.FacePreset ? leaf == typeof(FaceData) : leaf == typeof(Vec4);
            }
            return false;
        }

        static bool IsSlider(FieldSpec f) => f.Kind == FieldKind.Float && f.Scale == InspectorSchemas.MakerSliderScale;

        // The per-sex instance count of the rows of a kind; maleHeadList = the maker.headList gate for males.
        static int PerSex(Func<FieldSpec, bool> kind, int sex, bool maleHeadList) =>
            FaceRows.Where(kind).Where(f => Shown(f, sex, maleHeadList)).Sum(InstanceCount);

        static bool Shown(FieldSpec f, int sex, bool maleHeadList)
        {
            if (f.Kind == FieldKind.PartList && f.CategoryFor(sex) < 0) return false;
            switch (f.Gate)
            {
                case null: return true;
                case SchemaGates.CharaMale: return sex == 0;
                case SchemaGates.CharaFemale: return sex == 1;
                case SchemaGates.MakerHeadList: return sex == 1 || maleHeadList;
            }
            int c = SchemaGates.MakerListCategory(f.Gate);
            return c < 0 || f.CategoryFor(sex) >= 0;
        }

        static void Counts(TestRunner t)
        {
            var rows = FaceRows.ToList();
            Func<FieldSpec, bool> slider = IsSlider, colour = f => f.Kind == FieldKind.Color, list = f => f.Kind == FieldKind.PartList,
                push = f => f.Kind == FieldKind.Push, toggle = f => f.Kind == FieldKind.Bool, button = f => f.Kind == FieldKind.Button;
            var defs = new[] { rows.Count(slider), rows.Count(colour), rows.Count(list), rows.Count(push), rows.Count(toggle), rows.Count(button) };
            t.Check(defs.SequenceEqual(new[] { 91, 12, 14, 3, 1, 2 }), "face counts: definitions sliders / colours / lists / pushes / toggles / buttons = 91 / 12 / 14 / 3 / 1 / 2 (" + string.Join(" / ", defs) + ")");
            t.Equal(rows.Count, defs.Sum(), "face counts: no row of another kind");
            t.Equal(59, rows.Count(f => slider(f) && f.Path.StartsWith("appearance.chara.face.shape[", StringComparison.Ordinal)), "face counts: 59 of the sliders are 4.0's shapes");
            var female = new[] { PerSex(slider, 1, false), PerSex(colour, 1, false), PerSex(list, 1, false), PerSex(push, 1, false) };
            t.Check(female.SequenceEqual(new[] { 103, 15, 16, 4 }), "face counts: female instances sliders / colours / lists / pushes = 103 / 15 / 16 / 4 (" + string.Join(" / ", female) + ")");
            var male = new[] { PerSex(slider, 0, false), PerSex(colour, 0, false), PerSex(list, 0, false), PerSex(push, 0, false) };
            t.Check(male.SequenceEqual(new[] { 103, 16, 16, 4 }), "face counts: male instances without the head list = 103 / 16 / 16 / 4 (" + string.Join(" / ", male) + ")");
            t.Equal(17, PerSex(list, 0, true), "face counts: male lists with the head list (F3) = 17");
            t.Equal(1 + 2, PerSex(toggle, 1, false) + PerSex(button, 1, false), "face counts: the link toggle and two copy buttons");
        }

        static void Rules(TestRunner t)
        {
            var rows = FaceRows.ToList();
            var sliders = rows.Where(IsSlider).ToList();
            int bad = sliders.Count(f => f.Min != 0 || f.Max != 1 || f.TypedMin != -1 || f.TypedMax != 2 || f.Format != "0" || f.Applies != Applies.All || f.OpLabel != OpLabels.EditFace);
            t.Check(bad == 0, "face rules: every slider has Q23's ranges (0..1, typed −1..2, ×100, \"0\"), all selected, label Edit face (" + bad + " differ)");
            t.Check(rows.Where(f => f.Kind == FieldKind.Float).All(IsSlider), "face rules: every float row is a Maker slider");
            // alpha: every face colour but the sclera
            var noAlpha = rows.Where(f => f.Kind == FieldKind.Color && !f.ColorAlpha).Select(f => f.Path).ToList();
            t.Check(noAlpha.SequenceEqual(new[] { "appearance.chara.face.eyes.{side}.white" }), "face rules: ColorAlpha false exactly for the sclera (" + string.Join(", ", noAlpha) + ")");
            t.Check(rows.Where(f => f.Kind == FieldKind.Color).All(f => f.OpLabel == OpLabels.EditFace), "face rules: colour rows are labelled Edit face");
            // lerp-clamped members (F12): layouts, tilts, sizes, offset, shadow, rotation; direct: detail power, emission, gloss, metallic; shapes extrapolate
            var lerp = sliders.Where(f => f.LerpClamped).ToList();
            t.Equal(25, lerp.Count, "face rules: 25 lerp-clamped sliders");
            var direct = new HashSet<string> { "face.detail.power", "face.eyes.{side}.iris.emission", "face.makeup.eyeshadow.gloss", "face.makeup.cheek.gloss",
                "face.makeup.lip.gloss", "face.makeup.paint[{k}].gloss", "face.makeup.paint[{k}].metallic" };
            var notLerp = sliders.Where(f => !f.LerpClamped && !f.Path.StartsWith("appearance.chara.face.shape[", StringComparison.Ordinal)).Select(f => f.Path.Substring("appearance.chara.".Length)).ToList();
            t.Check(notLerp.Count == 7 && notLerp.All(direct.Contains), "face rules: the 7 direct sliders are not lerp-clamped (" + string.Join(", ", notLerp) + ")");
            t.Check(sliders.Where(f => f.Path.StartsWith("appearance.chara.face.shape[", StringComparison.Ordinal)).All(f => !f.LerpClamped), "face rules: shapes are not lerp-clamped (they extrapolate)");
            // lists: categories per sex, labels, contexts
            var cats = rows.Where(f => f.Kind == FieldKind.PartList).ToDictionary(f => f.Path.Substring("appearance.chara.".Length), f => (f.CategoryFemale, f.CategoryMale));
            var expect = new Dictionary<string, (int, int)>
            {
                ["face.head"] = (210, 110), ["face.skin"] = (211, 111), ["face.detail.item"] = (212, 112), ["face.eyebrow.item"] = (314, 314),
                ["face.eyes.{side}.iris.item"] = (317, 317), ["face.eyes.{side}.pupil.item"] = (318, 318), ["face.eyes.highlight.item"] = (319, 319),
                ["face.eyelashes.item"] = (315, 315), ["face.makeup.eyeshadow.item"] = (316, 316), ["face.makeup.cheek.item"] = (320, 320),
                ["face.makeup.lip.item"] = (322, 322), ["face.makeup.paint[{k}].item"] = (313, 313), ["face.mole.item"] = (323, 323), ["face.beard.item"] = (-1, 121),
            };
            t.Check(cats.Count == expect.Count && expect.All(kv => cats.TryGetValue(kv.Key, out var c) && c.Equals(kv.Value)),
                "face rules: PartList categories per sex as listed (" + string.Join(", ", cats.Select(kv => kv.Key + " " + kv.Value)) + ")");
            t.Check(rows.Where(f => f.Kind == FieldKind.PartList).All(f => f.OpLabel == OpLabels.ChangeItem && f.Default == null), "face rules: lists are labelled Change item and have no default (no reset)");
            t.Check(rows.Where(f => f.Kind == FieldKind.PartList).All(f => (f.ListContext == FieldSpec.ListContextHead) == (f.Path == "appearance.chara.face.skin")),
                "face rules: only the skin list is filtered by the head");
            t.Equal(1, InspectorSchemas.CharaEdit.SelectMany(x => x.Fields).Count(f => f.Kind == FieldKind.PartList && f.CategoryFor(1) == 210), "face rules: one female head list");
            // pushes
            var pushes = rows.Where(f => f.Kind == FieldKind.Push).Select(f => f.Path.Substring("appearance.chara.".Length) + " " + f.PushSource + " " + f.OpLabel).ToList();
            t.Check(pushes.SequenceEqual(new[] { "face facePreset Face preset", "face.makeup.paint[{k}].layout layout:7 Edit face", "face.mole.layout layout:6 Edit face" }),
                "face rules: pushes = face presets, paint layout (7), mole layout (6) (" + string.Join("; ", pushes) + ")");
            t.Equal("Preset", rows.Single(f => f.PushSource == PushSources.FacePreset).ThumbColumn, "face rules: the preset grid shows the Preset column's texture");
            t.Check(PushSources.LayoutCategory("layout:7") == 7 && PushSources.LayoutCategory("layout:x") == -1 && PushSources.LayoutCategory(PushSources.FacePreset) == -1 &&
                    PushSources.Layout(6) == "layout:6", "face rules: PushSources.Layout / LayoutCategory");
            // the toggle and the buttons
            var linked = rows.Single(f => f.Kind == FieldKind.Bool);
            t.Check(linked.Path == "appearance.chara.face.eyes.linked" && Equals(linked.Default, true) && linked.OpLabel == OpLabels.EditFace, "face rules: the link toggle (default on, Edit face)");
            var buttons = rows.Where(f => f.Kind == FieldKind.Button).Select(f => f.Action).ToList();
            t.Check(buttons.SequenceEqual(new[] { InspectorSchemas.ActionCopyEyeLeftToRight, InspectorSchemas.ActionCopyEyeRightToLeft }) && buttons.All(a => a.StartsWith("ops.CopyEye:", StringComparison.Ordinal)),
                "face rules: the copy buttons run ops.CopyEye:lr / :rl");
            t.Check(OpLabels.All.Contains(OpLabels.ResetValue) && OpLabels.ResetValue == "Reset value", "face rules: Reset value is an OpLabels constant");
        }

        // "Default" = the document default for every value row.
        static void Defaults(TestRunner t)
        {
            var s = new SceneSession(new SessionOptions { Ids = new CountingIdSource(31) });
            string id = s.AddChara(new CharaAppearance { Stored = new CharaSubtree() }).NewIds[0];
            int n = 0;
            var bad = new List<string>();
            foreach (var f in NewRows)
            {
                if (f.Kind == FieldKind.Button || f.Kind == FieldKind.PartList || f.Kind == FieldKind.Push) continue;
                foreach (var path in Expansions(f))
                {
                    n++;
                    var v = FieldAccessor.Get(s.Model, id, path);
                    bool ok;
                    if (f.Kind == FieldKind.Bool) ok = Equals(f.Default, v);
                    else if (f.Kind == FieldKind.Color) ok = f.Default is double[] d && d.Length == 4 && v is Color4 c && (float)d[0] == c.R && (float)d[1] == c.G && (float)d[2] == c.B && (float)d[3] == c.A;
                    else if (f.Component >= 0) ok = f.Default is double x && (float)x == Component(v, f.Component);
                    else ok = f.Default is double y && v is float fv && (float)y == fv;
                    if (!ok) bad.Add(path + (f.Component >= 0 ? "#" + f.Component : "") + " " + FieldJson.ToText(v) + " vs " + Show(f.Default));
                }
            }
            t.Check(bad.Count == 0 && n > 0, "face defaults: " + n + " expanded value rows default to the document defaults (" + string.Join("; ", bad.Take(8)) + ")");
            // spot checks
            Func<string, int, double> def = (p, c) => Convert.ToDouble(InspectorSchemas.Find(InspectorSchemas.CharaEdit, InspectorSchemas.CharaEditPath(p), c).Default);
            t.Check(def("face.mole.layout", 2) == 0.25 && def("face.eyebrow.layout", 1) == 0.375 && def("face.eyes.{side}.pupil.size", 0) == 0.8333 &&
                    def("face.detail.power", -1) == 1.0 && def("face.makeup.paint[{k}].metallic", -1) == 0.5, "face defaults: mole x 0.25, eyebrow y 0.375, pupil 0.8333, detail power 1, paint metallic 0.5");
        }

        static float Component(object v, int c)
        {
            switch (v)
            {
                case Vec4 a: return c == 0 ? a.X : c == 1 ? a.Y : c == 2 ? a.Z : a.W;
                case Vec2 b: return c == 0 ? b.X : b.Y;
            }
            return float.NaN;
        }

        static string Show(object d) => d is double[] a ? "[" + string.Join(", ", a) + "]" : Convert.ToString(d, System.Globalization.CultureInfo.InvariantCulture);

        static void Gates(TestRunner t)
        {
            var gated = FaceRows.Where(f => f.Gate != null).Select(f => f.Path.Substring("appearance.chara.".Length) + "=" + f.Gate).ToList();
            t.Check(gated.SequenceEqual(new[] { "face.head=maker.headList", "face.beard.item=chara.male", "face.beard.color=chara.male" }),
                "face gates: the head list (maker.headList) and the beard rows (chara.male) (" + string.Join(", ", gated) + ")");
            t.Check(FaceRows.Where(f => f.Gate != null).All(f => SchemaGates.IsKnown(f.Gate)), "face gates: every gate is a SchemaGates name");
            t.Check(SchemaGates.All.Contains(SchemaGates.MakerHeadList) && SchemaGates.IsKnown(SchemaGates.MakerList(121)) && !SchemaGates.IsKnown("maker.nope"),
                "face gates: maker.headList in All, maker.list:<c> known, an unknown name not");
            t.Check(SchemaGates.MakerList(121) == "maker.list:121" && SchemaGates.MakerListCategory("maker.list:121") == 121 && SchemaGates.MakerListCategory("maker.list:") == -1 &&
                    SchemaGates.MakerListCategory("chara.male") == -1, "face gates: MakerList / MakerListCategory");
            // the Beard page: every row gated off for females (its page button hides)
            t.Check(FaceSections.Where(s => InspectorSchemas.CharaEditPageOf(s) == "beard").SelectMany(s => s.Fields).All(f => !Shown(f, 1, false)), "face gates: the Beard page shows nothing for females");
            t.Check(FaceSections.Where(s => InspectorSchemas.CharaEditPageOf(s) != "beard").All(s => s.Fields.Any(f => Shown(f, 1, false))), "face gates: every other page shows rows for females");
        }

        static void Labels(TestRunner t)
        {
            L10nTable tb;
            try { tb = UiKit.Strings(t); }
            catch (Exception e) { t.Check(false, "face labels: strings.tsv loads: " + e.Message); return; }
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var p in InspectorSchemas.FacePages) keys.Add(InspectorSchemas.CharaEditPageLabel("face", p));
            foreach (var s in FaceSections)
            {
                if (s.IsRepeated) { t.Check(s.LabelKey == null, "face labels: the repeated section " + s.Id + " is labelled by its instances"); foreach (var i in s.Instances) keys.Add(i.LabelKey); }
                else keys.Add(s.LabelKey);
                foreach (var f in s.Fields) keys.Add(f.LabelKey);
            }
            foreach (var lang in new[] { "en", "ja", "zh-Hans" })
            {
                var missing = keys.Where(k => string.IsNullOrEmpty(tb.Cell(k, lang))).ToList();
                t.Check(missing.Count == 0, "face labels: " + keys.Count + " keys have " + lang + " (" + string.Join(", ", missing.Take(12)) + ")");
            }
        }
    }
}
