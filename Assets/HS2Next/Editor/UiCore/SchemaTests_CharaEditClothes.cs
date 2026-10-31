// The Clothes page of the Edit tab (Phase 4.4; InspectorSchemas.CharaEdit): one page `clothes` with the kind selector, per
// kind (top … shoes) Type · Colour 1 · Colour 2 · Colour 3 · Other with concrete paths under `appearance.coord.clothes.<kind>.`
// and the coordinate section. Checked: every clothes row resolves on a character with stored documents with its declared
// leaf type (refs, colours, floats, the layout components on Vec4, the option bools) and is writable; the counts per kind
// group (lists 4, colours 6, sliders 22, toggles 2, buttons 1), in all (lists 32, colours 48, sliders 176, toggles 16, buttons
// 10 since Phase 4.6's «保存…») and per sex (male: the four kinds the Maker offers); ColorAlphaGate exactly on the main colours (alpha<n> of their
// kind); LerpClamped exactly on the pattern layout and rotation; ResetKind PrefabDefault on gloss, metallic, layout and
// rotation, Constant on the break, Reference elsewhere; the list categories per sex; the defaults = the document defaults;
// the gates on the right rows (section gates implied by their rows' gates); the two gate parsers (SchemaGates and the scene
// layer's MakerGate) agree on every clothes gate name; watched paths; section order; every label key in en, ja and zh-Hans.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HS2Next.UI.Core.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;
    using HS2Next.Scenes;
    using HS2Next.UI.Core;

    public static class SchemaTests_CharaEditClothes
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

        const string Root = "appearance.coord.";
        static readonly string[] Kinds = { "top", "bottom", "innerTop", "innerBottom", "gloves", "pantyhose", "socks", "shoes" };

        static IEnumerable<SchemaSection> ClothesSections => InspectorSchemas.CharaEdit.Where(s => InspectorSchemas.CharaEditTreeOf(s) == "clothes");
        static IEnumerable<FieldSpec> ClothesRows => ClothesSections.SelectMany(s => s.Fields);
        static string Member(FieldSpec f) => f.Path != null && f.Path.StartsWith(Root, StringComparison.Ordinal) ? f.Path.Substring(Root.Length) : f.Path;
        static string Key(FieldSpec f) => (Member(f) ?? f.Action) + (f.Component >= 0 ? "#" + f.Component : "");
        static int KindOfRow(FieldSpec f)
        {
            string m = Member(f);
            if (m != null)
                for (int k = 0; k < Kinds.Length; k++) if (m.StartsWith("clothes." + Kinds[k] + ".", StringComparison.Ordinal)) return k;
            return f.Action != null ? InspectorSchemas.ClothesKindOfAction(f.Action) : -1;
        }

        static void Structure(TestRunner t)
        {
            var secs = ClothesSections.ToList();
            var expect = new List<string> { "clothes.clothes.kind" };
            foreach (var k in Kinds) foreach (var s in new[] { "type", "colour1", "colour2", "colour3", "other" }) expect.Add("clothes.clothes." + k + "." + s);
            expect.Add("clothes.clothes.coordinate");
            t.Check(secs.Select(s => s.Id).SequenceEqual(expect), "clothes: the sections in order (kind, per kind type, colour1–3, other, then coordinate; " +
                string.Join(", ", secs.Select(s => s.Id).Take(7)) + " …)");
            t.Check(InspectorSchemas.ClothesPages.SequenceEqual(new[] { "clothes" }) && InspectorSchemas.CharaEditPagesOf("clothes").SequenceEqual(new[] { "clothes" }),
                "clothes: one page `clothes` (no page strip)");
            t.Check(secs.All(s => InspectorSchemas.CharaEditPageOf(s) == "clothes" && InspectorSchemas.CharaEditTreeOf(s) == "clothes"), "clothes: every section parses into node clothes, page clothes");
            t.Equal(secs.Count, InspectorSchemas.CharaEditSections("clothes", "clothes").Count(), "clothes: CharaEditSections(clothes, clothes) = every clothes section");
            t.Check(InspectorSchemas.ClothesEditKinds.SequenceEqual(Kinds) && InspectorSchemas.ClothesEditKinds.SequenceEqual(ClothesSet.Names) && SchemaGates.ClothesKindNames.SequenceEqual(Kinds),
                "clothes: the kinds top … shoes = ClothesSet.Names = SchemaGates.ClothesKindNames");
            int bad = 0;
            for (int k = 0; k < 8; k++)
                foreach (var s in InspectorSchemas.ClothesKindSections)
                {
                    string id = InspectorSchemas.ClothesSectionId(k, s);
                    int set = s.StartsWith("colour", StringComparison.Ordinal) ? s[6] - '0' : -1;
                    if (id != "clothes.clothes." + Kinds[k] + "." + s || InspectorSchemas.ClothesKindOf(id) != k || InspectorSchemas.ClothesSetOf(id) != set || !secs.Any(x => x.Id == id)) bad++;
                }
            t.Check(bad == 0 && InspectorSchemas.ClothesKindOf(InspectorSchemas.ClothesKindSection) == -1 && InspectorSchemas.ClothesKindOf(InspectorSchemas.ClothesCoordinateSection) == -1 &&
                    InspectorSchemas.ClothesKindOf("hair.hair.back.style") == -1 && InspectorSchemas.ClothesKindOf(null) == -1 && InspectorSchemas.ClothesKindOf("clothes.clothes.bra.type") == -1,
                "clothes: ClothesSectionId / ClothesKindOf / ClothesSetOf round-trip on the 40 kind sections; the selector, coordinate and other ids → −1");
            t.Check(secs.Single(s => s.Id == InspectorSchemas.ClothesKindSection).Fields.Count == 0, "clothes: the kind selector section has no rows (the host puts the kind selector there)");
            var coord = secs.Single(s => s.Id == InspectorSchemas.ClothesCoordinateSection).Fields;
            t.Check(coord.Count == 2 && coord.All(f => f.Kind == FieldKind.Button && f.Gate == null) &&
                    coord[0].Action == InspectorSchemas.ActionApplyCoordinate && coord[0].OpLabel == OpLabels.LoadCoordinate &&
                    coord[1].Action == InspectorSchemas.ActionSaveCoordinate && coord[1].OpLabel == null,
                "clothes: the coordinate section is two ungated buttons (ops.ApplyCoordinate «Load coordinate»; Phase 4.6: ops.SaveCoordinate «保存…», not recorded)");
            t.Check(secs.All(s => !s.IsRepeated && s.Fields.All(f => !f.IsTemplate)), "clothes: no repeated section, no template (concrete paths per kind)");
            int foreign = 0;
            foreach (var s in secs)
            {
                int k = InspectorSchemas.ClothesKindOf(s.Id);
                if (k < 0) continue;
                foreach (var f in s.Fields)
                {
                    if (KindOfRow(f) != k) { foreign++; t.Check(false, "clothes: " + s.Id + " row " + Key(f) + " is not of kind " + Kinds[k]); }
                    if (f.Gate != null && !(SchemaGates.TryMakerClothes(f.Gate, out int gk, out _) && gk == k)) { foreign++; t.Check(false, "clothes: " + s.Id + " row " + Key(f) + " gate " + f.Gate); }
                    if (f.ColorAlphaGate != null && !(SchemaGates.TryMakerClothes(f.ColorAlphaGate, out int ak, out _) && ak == k)) { foreign++; t.Check(false, "clothes: " + Key(f) + " alpha gate " + f.ColorAlphaGate); }
                }
                if (s.Gate != null && !(SchemaGates.TryMakerClothes(s.Gate, out int sk, out _) && sk == k)) { foreign++; t.Check(false, "clothes: section " + s.Id + " gate " + s.Gate); }
            }
            t.Equal(0, foreign, "clothes: every row of a kind group addresses that kind and is gated on that kind");
            var ids = ClothesRows.Select(f => f.Id).ToList();
            t.Check(ids.Distinct().Count() == ids.Count, "clothes: row ids are distinct");
            // the File node (Phase 4.6) reuses the coordinate section's two buttons by design
            var others = new HashSet<string>(InspectorSchemas.CharaEdit.Where(s => InspectorSchemas.CharaEditTreeOf(s) != "clothes" && InspectorSchemas.CharaEditTreeOf(s) != "file").SelectMany(s => s.Fields).Select(f => f.Id));
            t.Check(ids.All(id => !others.Contains(id)), "clothes: no clothes row id is also a row id of another node (the File node's coordinate buttons aside)");
            var type = secs.Single(s => s.Id == "clothes.clothes.socks.type").Fields.Select(Key).ToList();
            t.Check(type.SequenceEqual(new[] { "clothes.socks.item", InspectorSchemas.ActionResetClothesColours(6) }), "clothes: Type = the item list and «初期カラー» (" + string.Join(", ", type) + ")");
            var col = secs.Single(s => s.Id == "clothes.clothes.gloves.colour2").Fields.Select(Key).ToList();
            string c = "clothes.gloves.colors[1].";
            t.Check(col.SequenceEqual(new[] { c + "base", c + "gloss", c + "metallic", c + "pattern", c + "patternColor", c + "layout#0", c + "layout#1", c + "layout#2", c + "layout#3", c + "rotation" }),
                "clothes: Colour n = «カラー», «ツヤ», «質感», the pattern, its colour, «幅» «高さ» «位置X» «位置Y», «回転» (" + string.Join(", ", col) + ")");
            var other = secs.Single(s => s.Id == "clothes.clothes.top.other").Fields.Select(Key).ToList();
            t.Check(other.SequenceEqual(new[] { "clothes.top.breakRate", "clothes.top.hideOptions[0]", "clothes.top.hideOptions[1]" }), "clothes: Other = «破れ具合» and the two option toggles (" + string.Join(", ", other) + ")");
            t.Check(InspectorSchemas.ClothesKindOfAction("ops.ResetClothesColours:innerBottom") == 3 && InspectorSchemas.ClothesKindOfAction("ops.ResetClothesColours:bra") == -1 &&
                    InspectorSchemas.ClothesKindOfAction("ops.ResetHairBundles:back") == -1 && InspectorSchemas.ClothesKindOfAction(null) == -1,
                "clothes: ClothesKindOfAction reads the kind of «初期カラー»");
            t.Check(InspectorSchemas.CoordinateModes.SequenceEqual(new[] { "clothes", "accessories", "all" }) && InspectorSchemas.ActionApplyCoordinateMode("all") == "ops.ApplyCoordinate:all" &&
                    Enum.GetNames(typeof(CoordinateApplyMode)).Select(x => x.ToLowerInvariant()).SequenceEqual(InspectorSchemas.CoordinateModes),
                "clothes: the three coordinate modes clothes / accessories / all = CoordinateApplyMode");
        }

        static void Resolve(TestRunner t)
        {
            var s = new SceneSession(new SessionOptions { Ids = new CountingIdSource(91) });
            var r = s.AddChara(new CharaAppearance { Stored = new CharaSubtree() });
            t.Check(r.Status == CommandStatus.Ok, "clothes resolve: a character with stored documents");
            string id = r.NewIds[0];
            int n = 0, fails = 0;
            foreach (var f in ClothesRows)
            {
                if (f.Kind == FieldKind.Button) { if (f.Path != null || f.Action == null) { fails++; t.Check(false, "clothes: button " + f.LabelKey + " needs an action and no path"); } continue; }
                n++;
                try
                {
                    var leaf = FieldAccessor.LeafType(s.Model, id, f.Path);
                    if (!Fits(f, leaf)) { fails++; t.Check(false, "clothes: " + Key(f) + " is a " + leaf.Name + ", declared " + f.Kind); }
                    if (!FieldAccessor.IsWritable(s.Model, id, f.Path, out string code)) { fails++; t.Check(false, "clothes: " + f.Path + " is not writable (" + code + ")"); }
                    if (AppearancePaths.IsRebuildPath(f.Path)) { fails++; t.Check(false, "clothes: " + f.Path + " is a rebuild-only member"); }
                }
                catch (CommandException e) { fails++; t.Check(false, "clothes: " + f.Path + " does not resolve: " + e.Code + " " + e.Message); }
            }
            t.Check(fails == 0 && n == 8 * 34, "clothes resolve: " + n + " value rows resolve with their declared leaf types, are writable and no rebuild path");
            var w = InspectorSchemas.Find(InspectorSchemas.CharaEdit, InspectorSchemas.CoordEditPath("clothes.top.colors[0].layout"), 0);
            t.Check(w != null && w.LabelKey == "inspector.charaEdit.clothes.patternWidth", "clothes: «幅» = colors[0].layout component 0");
            var gl = InspectorSchemas.Find(InspectorSchemas.CharaEdit, InspectorSchemas.CoordEditPath("clothes.shoes.colors[2].gloss"));
            t.Check(gl != null && s.Set(id, gl.Path, (float)gl.ParseTyped(150)).Status == CommandStatus.Ok && (float)FieldAccessor.Get(s.Model, id, gl.Path) == 1.5f,
                "clothes: typed 150 into «ツヤ» stores 1.5 (nothing clamps stored values)");
            var opt = InspectorSchemas.Find(InspectorSchemas.CharaEdit, InspectorSchemas.CoordEditPath("clothes.bottom.hideOptions[1]"));
            t.Check(opt != null && s.Set(id, opt.Path, true).Status == CommandStatus.Ok && (bool)FieldAccessor.Get(s.Model, id, opt.Path) &&
                    s.Model.Find(id).Appearance.Stored.Coord.Clothes.Parts[1].HideOptions[1], "clothes: the option toggle writes hideOptions[1] of the bottom");
        }

        static bool Fits(FieldSpec f, Type leaf)
        {
            if (f.Component >= 0) return f.Kind == FieldKind.Float && leaf == typeof(Vec4) && f.Component < 4;
            switch (f.Kind)
            {
                case FieldKind.Float: return leaf == typeof(float);
                case FieldKind.Bool: return leaf == typeof(bool);
                case FieldKind.Color: return leaf == typeof(Color4);
                case FieldKind.PartList: return leaf == typeof(string);
            }
            return false;
        }

        static bool IsSlider(FieldSpec f) => f.Kind == FieldKind.Float && f.Scale == InspectorSchemas.MakerSliderScale;

        static int[] CountOf(IEnumerable<FieldSpec> rows)
        {
            var l = rows.ToList();
            return new[] { l.Count(f => f.Kind == FieldKind.PartList), l.Count(f => f.Kind == FieldKind.Color), l.Count(IsSlider), l.Count(f => f.Kind == FieldKind.Bool), l.Count(f => f.Kind == FieldKind.Button) };
        }

        static void Counts(TestRunner t)
        {
            var rows = ClothesRows.ToList();
            var defs = CountOf(rows);
            t.Check(defs.SequenceEqual(new[] { 32, 48, 176, 16, 10 }),
                "clothes counts: definitions lists / colours / sliders / toggles / buttons = 32 / 48 / 176 / 16 / 10 (" + string.Join(" / ", defs) + ")");
            t.Equal(rows.Count, defs.Sum(), "clothes counts: no row of another kind");
            for (int k = 0; k < 8; k++)
            {
                var per = CountOf(ClothesSections.Where(s => InspectorSchemas.ClothesKindOf(s.Id) == k).SelectMany(s => s.Fields));
                t.Check(per.SequenceEqual(new[] { 4, 6, 22, 2, 1 }), "clothes counts: kind " + Kinds[k] + " lists / colours / sliders / toggles / buttons = 4 / 6 / 22 / 2 / 1 (" + string.Join(" / ", per) + ")");
            }
            // per sex: the kinds the selector shows (females 8, males top / bottom / gloves / shoes) and the coordinate button
            foreach (int sex in new[] { 1, 0 })
            {
                var shown = InspectorSchemas.ClothesKindsFor(sex);
                var sexRows = rows.Where(f => { int k = KindOfRow(f); return k < 0 || shown.Contains(k); });
                var c = CountOf(sexRows);
                var want = sex == 1 ? new[] { 32, 48, 176, 16, 10 } : new[] { 16, 24, 88, 8, 6 };
                t.Check(shown.Count == (sex == 1 ? 8 : 4) && c.SequenceEqual(want),
                    "clothes counts " + (sex == 1 ? "female" : "male") + ": kinds " + shown.Count + ", lists / colours / sliders / toggles / buttons = " + string.Join(" / ", c));
            }
            t.Check(InspectorSchemas.ClothesKindsFor(0).SequenceEqual(new[] { 0, 1, 4, 7 }) && InspectorSchemas.ClothesKindsFor(1).SequenceEqual(Enumerable.Range(0, 8)) &&
                    Enumerable.Range(0, 8).All(k => InspectorSchemas.ClothesKindShown(k, 0) == MakerEdits.ClothesKindListed(k, 0) && InspectorSchemas.ClothesKindShown(k, 1) == MakerEdits.ClothesKindListed(k, 1)),
                "clothes counts: the male selector shows top, bottom, gloves, shoes; both layers agree on the kinds per sex");
        }

        static void Rules(TestRunner t)
        {
            var rows = ClothesRows.ToList();
            var sliders = rows.Where(IsSlider).ToList();
            int bad = sliders.Count(f => f.Min != 0 || f.Max != 1 || f.TypedMin != -1 || f.TypedMax != 2 || f.Format != "0" || f.Applies != Applies.All || f.OpLabel != OpLabels.EditClothes);
            t.Check(bad == 0, "clothes rules: every slider has the Maker ranges (0..1, typed −1..2, ×100, \"0\"), all selected, label Edit clothes (" + bad + " differ)");
            t.Check(rows.Where(f => f.Kind == FieldKind.Float).All(IsSlider), "clothes rules: every float row is a Maker slider");
            var colours = rows.Where(f => f.Kind == FieldKind.Color).ToList();
            t.Check(colours.All(f => f.ColorAlpha && f.OpLabel == OpLabels.EditClothes), "clothes rules: every clothes colour may edit alpha (ColorAlpha on all 48), label Edit clothes");
            var alphaGated = colours.Where(f => f.ColorAlphaGate != null).Select(Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var mains = Kinds.SelectMany(k => Enumerable.Range(0, 3).Select(n => "clothes." + k + ".colors[" + n + "].base")).OrderBy(x => x, StringComparer.Ordinal).ToList();
            t.Check(alphaGated.SequenceEqual(mains), "clothes rules: ColorAlphaGate exactly on the 24 main colours (" + alphaGated.Count + ")");
            int alphaBad = 0;
            foreach (var f in colours.Where(x => x.ColorAlphaGate != null))
            {
                int k = KindOfRow(f), n = Member(f)[Member(f).IndexOf('[') + 1] - '0' + 1;
                if (f.ColorAlphaGate != SchemaGates.MakerClothes(k, SchemaGates.ClothesAlpha(n))) alphaBad++;
            }
            t.Check(alphaBad == 0 && rows.Where(f => f.Kind != FieldKind.Color).All(f => f.ColorAlphaGate == null) &&
                    InspectorSchemas.CharaEdit.Where(s => InspectorSchemas.CharaEditTreeOf(s) != "clothes").SelectMany(s => s.Fields).All(f => f.ColorAlphaGate == null),
                "clothes rules: the main colour of set n carries maker.clothes:<kind>:alpha<n>; no other row (of any node) has an alpha gate");
            var lerp = sliders.Where(f => f.LerpClamped).Select(Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var expectLerp = Kinds.SelectMany(k => Enumerable.Range(0, 3).SelectMany(n => Enumerable.Range(0, 4).Select(c => "clothes." + k + ".colors[" + n + "].layout#" + c)
                .Concat(new[] { "clothes." + k + ".colors[" + n + "].rotation" }))).OrderBy(x => x, StringComparer.Ordinal).ToList();
            t.Check(lerp.SequenceEqual(expectLerp), "clothes rules: LerpClamped exactly on the pattern layout and rotation (" + lerp.Count + " = 8 × 3 × 5)");
            var prefab = rows.Where(f => f.ResetKind == ResetKind.PrefabDefault).Select(Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var expectPrefab = Kinds.SelectMany(k => Enumerable.Range(0, 3).SelectMany(n => new[] { "gloss", "metallic", "layout#0", "layout#1", "layout#2", "layout#3", "rotation" }
                .Select(m => "clothes." + k + ".colors[" + n + "]." + m))).OrderBy(x => x, StringComparer.Ordinal).ToList();
            t.Check(prefab.SequenceEqual(expectPrefab), "clothes rules: ResetKind PrefabDefault exactly on gloss, metallic, layout and rotation (" + prefab.Count + " = 8 × 3 × 7)");
            var constant = rows.Where(f => f.ResetKind == ResetKind.Constant).Select(Key).ToList();
            t.Check(constant.SequenceEqual(Kinds.Select(k => "clothes." + k + ".breakRate")) && rows.Where(f => f.ResetKind == ResetKind.Constant).All(f => Equals(f.Default, 0.0)),
                "clothes rules: ResetKind Constant exactly on the eight break rates (↺ 0)");
            t.Check(rows.Where(f => f.ResetKind == ResetKind.Reference).All(f => f.Kind != FieldKind.Float), "clothes rules: the other rows (colours, lists, toggles, buttons) keep the reference reset");
            var cats = rows.Where(f => f.Kind == FieldKind.PartList && Member(f).EndsWith(".item", StringComparison.Ordinal)).Select(f => Key(f) + " " + f.CategoryFemale + "/" + f.CategoryMale).ToList();
            var expectCats = new[] { "140", "141", "-1", "-1", "144", "-1", "-1", "147" }.Select((m, k) => "clothes." + Kinds[k] + ".item " + (240 + k) + "/" + m).ToList();
            t.Check(cats.SequenceEqual(expectCats), "clothes rules: item lists 240–247 for females, 140 / 141 / 144 / 147 for males, none for the male inner / pantyhose / socks kinds (" +
                string.Join(", ", cats) + ")");
            int catBad = 0;
            for (int k = 0; k < 8; k++)
                for (int sex = 0; sex < 2; sex++)
                    if (InspectorSchemas.ClothesCategory(k, sex) != (sex == 1 ? 240 + k : MakerEdits.ClothesKindListed(k, 0) ? MakerEdits.ClothesItemCategory(k, 0) : -1)) catBad++;
            t.Equal(0, catBad, "clothes rules: InspectorSchemas.ClothesCategory = the scene layer's item category where the Maker offers a list");
            var patterns = rows.Where(f => f.Kind == FieldKind.PartList && Member(f).EndsWith(".pattern", StringComparison.Ordinal)).ToList();
            t.Check(patterns.Count == 24 && patterns.All(f => f.CategoryFemale == 348 && f.CategoryMale == (InspectorSchemas.ClothesKindShown(KindOfRow(f), 0) ? 348 : -1) &&
                    MakerEdits.CategoryOf(f.Path, 1) == 348 && MakerEdits.CategoryOf(f.Path, 0) == 348),
                "clothes rules: the 24 pattern lists are 348 (for males only on the four kinds the Maker offers them; the ops accept 348 on every kind)");
            t.Check(rows.Where(f => f.Kind == FieldKind.PartList).All(f => f.OpLabel == OpLabels.ChangeItem && f.Default == null && f.ListContext == null && f.ThumbColumn == null),
                "clothes rules: lists are labelled Change item, have no default and no context");
            t.Check(rows.Where(f => f.Kind == FieldKind.PartList && f.CategoryMale >= 0).All(f => MakerEdits.CategoryOf(f.Path, 1) == f.CategoryFemale && MakerEdits.CategoryOf(f.Path, 0) == f.CategoryMale),
                "clothes rules: the list rows are item changes of their categories in the ops (MakerEdits.CategoryOf)");
            var buttons = rows.Where(f => f.Kind == FieldKind.Button).ToList();
            t.Check(buttons.Take(8).Select(f => f.Action).SequenceEqual(Enumerable.Range(0, 8).Select(InspectorSchemas.ActionResetClothesColours)) &&
                    buttons.Take(8).All(f => f.OpLabel == OpLabels.EditClothes) && buttons[8].Action == InspectorSchemas.ActionApplyCoordinate && buttons[8].OpLabel == OpLabels.LoadCoordinate,
                "clothes rules: the buttons are «初期カラー» of each kind (Edit clothes) and the coordinate load (Load coordinate)");
            var toggles = rows.Where(f => f.Kind == FieldKind.Bool).ToList();
            t.Check(toggles.All(f => f.Inverted && Equals(f.Default, false) && f.OpLabel == OpLabels.EditClothes && Member(f).Contains(".hideOptions[")),
                "clothes rules: the 16 option toggles are inverted views of hideOptions (on = shown), default false, Edit clothes");
            t.Check(rows.All(f => f.TooltipKey == null && f.Applies == Applies.All), "clothes rules: no tooltip keys; every row writes all selected characters");
            t.Check(rows.Where(f => f.Path != null).All(f => MakerEdits.AreaOf(new[] { f.Path }) == "clothes"), "clothes rules: every clothes row's member is in the clothes area (label Edit clothes through the bridge too)");
            t.Check(OpLabels.All.Contains(OpLabels.LoadCoordinate) && OpLabels.LoadCoordinate == "Load coordinate", "clothes rules: Load coordinate is an OpLabels constant");
        }

        static void Defaults(TestRunner t)
        {
            var s = new SceneSession(new SessionOptions { Ids = new CountingIdSource(93) });
            string id = s.AddChara(new CharaAppearance { Stored = new CharaSubtree() }).NewIds[0];
            int n = 0;
            var bad = new List<string>();
            foreach (var f in ClothesRows)
            {
                if (f.Kind == FieldKind.PartList || f.Kind == FieldKind.Button) { if (f.Default != null) bad.Add(Key(f) + " has a default"); continue; }
                n++;
                var v = FieldAccessor.Get(s.Model, id, f.Path);
                bool ok;
                if (f.Kind == FieldKind.Bool) ok = Equals(f.Default, v);
                else if (f.Kind == FieldKind.Color) ok = f.Default is double[] d && d.Length == 4 && v is Color4 c && (float)d[0] == c.R && (float)d[1] == c.G && (float)d[2] == c.B && (float)d[3] == c.A;
                else if (f.Component >= 0) ok = f.Default is double x && v is Vec4 a && (float)x == (f.Component == 0 ? a.X : f.Component == 1 ? a.Y : f.Component == 2 ? a.Z : a.W);
                else ok = f.Default is double y && v is float fv && (float)y == fv;
                if (!ok) bad.Add(Key(f) + " " + FieldJson.ToText(v) + " vs " + f.Default);
            }
            t.Check(bad.Count == 0 && n == 8 * 30, "clothes defaults: " + n + " value rows default to the document defaults (" + string.Join("; ", bad.Take(8)) + ")");
            Func<string, int, double> def = (p, c) => Convert.ToDouble(InspectorSchemas.Find(InspectorSchemas.CharaEdit, InspectorSchemas.CoordEditPath(p), c).Default);
            t.Check(def("clothes.top.colors[0].gloss", -1) == 0.5 && def("clothes.top.colors[0].metallic", -1) == 0 && def("clothes.top.colors[0].rotation", -1) == 0.5 &&
                    Enumerable.Range(0, 4).Select(c => def("clothes.top.colors[0].layout", c)).SequenceEqual(new[] { 1.0, 1.0, 0.0, 0.0 }) && def("clothes.top.breakRate", -1) == 0,
                "clothes defaults: gloss 0.5, metallic 0, layout (1, 1, 0, 0), rotation 0.5, break 0");
        }

        // the gate a row carries, by its member (without the kind prefix) or action
        static string ExpectGate(FieldSpec f, int k)
        {
            if (f.Kind == FieldKind.Button) return "prefab";
            string m = Member(f).Substring(("clothes." + Kinds[k] + ".").Length);
            if (m == "item") return null;
            if (m == "breakRate") return "break";
            if (m == "hideOptions[0]") return "opt1";
            if (m == "hideOptions[1]") return "opt2";
            int n = m[7] - '0' + 1;
            string member = m.Substring(10);
            return member == "base" || member == "gloss" || member == "metallic" || member == "pattern" ? "set" + n : "pattern" + n;
        }

        // feature → the section gate it implies (rows of a gated section carry its gate or a narrower one)
        static string Implies(string feature) =>
            feature.StartsWith("set", StringComparison.Ordinal) || feature.StartsWith("pattern", StringComparison.Ordinal) ? "set" + feature[feature.Length - 1]
            : feature == "break" || feature.StartsWith("opt", StringComparison.Ordinal) || feature == "other" ? "other" : feature;

        static void Gates(TestRunner t)
        {
            int bad = 0;
            foreach (var s in ClothesSections)
            {
                int k = InspectorSchemas.ClothesKindOf(s.Id);
                if (k < 0) { if (s.Gate != null || s.Fields.Any(f => f.Gate != null)) { bad++; t.Check(false, "clothes gates: " + s.Id + " is ungated"); } continue; }
                foreach (var f in s.Fields)
                {
                    string e = ExpectGate(f, k);
                    string want = e == null ? null : SchemaGates.MakerClothes(k, e);
                    if (f.Gate != want) { bad++; t.Check(false, "clothes gates: " + Key(f) + " gate " + (f.Gate ?? "none") + ", expected " + (want ?? "none")); }
                }
                string sec = s.Id.Substring(s.Id.LastIndexOf('.') + 1);
                string secWant = sec == "type" ? null : SchemaGates.MakerClothes(k, sec == "other" ? "other" : "set" + sec[6]);
                if (s.Gate != secWant) { bad++; t.Check(false, "clothes gates: section " + s.Id + " gate " + (s.Gate ?? "none") + ", expected " + (secWant ?? "none")); }
                if (s.Gate != null)
                    foreach (var f in s.Fields)
                        if (f.Gate == null || !SchemaGates.TryMakerClothes(f.Gate, out _, out string ff) || SchemaGates.MakerClothes(k, Implies(ff)) != s.Gate)
                        { bad++; t.Check(false, "clothes gates: " + Key(f) + " (" + f.Gate + ") does not imply its section's gate " + s.Gate); }
            }
            t.Equal(0, bad, "clothes gates: every row and section carries its gate (type: the list ungated, «初期カラー» prefab; colour n: set<n> with pattern<n> on the pattern colour " +
                "and layout; other: break, opt1, opt2), rows imply their section's gate");
            t.Check(ClothesRows.Where(f => f.Gate != null).All(f => SchemaGates.IsKnown(f.Gate)) && ClothesRows.Where(f => f.ColorAlphaGate != null).All(f => SchemaGates.IsKnown(f.ColorAlphaGate)),
                "clothes gates: every gate and alpha gate is known to SchemaGates.IsKnown");
            // the two parsers agree on every clothes gate name (the scene layer keeps its own copy of the names)
            int same = 0, n = 0;
            for (int k = 0; k < 8; k++)
                foreach (var feat in SchemaGates.ClothesFeatures)
                {
                    n++;
                    string g = SchemaGates.MakerClothes(k, feat);
                    if (g == "maker.clothes:" + Kinds[k] + ":" + feat && SchemaGates.TryMakerClothes(g, out int k1, out string f1) && MakerEdits.TryClothesGate(g, out int k2, out string f2) &&
                        MakerGate.TryParse(g, out var mg) && mg.Family == MakerGate.Clothes && mg.Name == g && k1 == k && k2 == k && f1 == feat && f2 == feat) same++;
                }
            t.Check(same == n && n == 8 * 15 && SchemaGates.ClothesFeatures.SequenceEqual(MakerEdits.ClothesGateFeatures) && SchemaGates.MakerClothesPrefix == MakerEdits.ClothesGatePrefix &&
                    MakerGate.FeaturesOf(MakerGate.Clothes).SequenceEqual(SchemaGates.ClothesFeatures),
                "clothes gates: the 120 gate names parse alike in SchemaGates.TryMakerClothes, MakerEdits.TryClothesGate and MakerGate.TryParse (" + same + " / " + n + ")");
            foreach (var g in new[] { "maker.clothes:bra:prefab", "maker.clothes:top:colour1", "maker.clothes:top", "maker.clothes::prefab", "maker.clothes:top:set4", "maker.clothes:top:alpha0",
                         "maker.hair:back:prefab", "maker.clothes:Top:prefab", null })
                t.Check(!SchemaGates.TryMakerClothes(g, out _, out _) && !MakerEdits.TryClothesGate(g, out _, out _) && (g == "maker.hair:back:prefab" ? SchemaGates.IsKnown(g) : !SchemaGates.IsKnown(g)),
                    "clothes gates: " + (g ?? "null") + " is not a clothes gate");
            t.Check(!MakerGate.UnparsedHolds("maker.clothes:top:nonsense") && MakerGate.UnparsedHolds("chara.female"),
                "clothes gates: a malformed clothes gate does not hold (a gate of another family than maker.* does)");
            t.Check(SchemaGates.ClothesSet(2) == "set2" && SchemaGates.ClothesAlpha(3) == "alpha3" && SchemaGates.ClothesPattern(1) == "pattern1" && SchemaGates.ClothesOption(2) == "opt2" &&
                    SchemaGates.MakerClothes(5, SchemaGates.ClothesKind) == "maker.clothes:pantyhose:kind", "clothes gates: name helpers");
        }

        static void Watch(TestRunner t)
        {
            int bad = 0;
            foreach (var s in ClothesSections)
            {
                int k = InspectorSchemas.ClothesKindOf(s.Id);
                string[] want;
                if (s.Id == InspectorSchemas.ClothesKindSection) want = new[] { Root + "clothes.top.item", Root + "clothes.innerTop.item" };
                else if (k < 0) want = null;
                else
                {
                    string p = Root + "clothes." + Kinds[k] + ".";
                    int set = InspectorSchemas.ClothesSetOf(s.Id);
                    want = set > 0 ? new[] { p + "item", p + "colors[" + (set - 1) + "].pattern" } : new[] { p + "item" };
                }
                bool ok = want == null ? s.Watch == null : s.Watch != null && s.Watch.SequenceEqual(want);
                if (!ok) { bad++; t.Check(false, "clothes watch: " + s.Id + " watches " + (s.Watch == null ? "nothing" : string.Join(", ", s.Watch))); }
            }
            t.Equal(0, bad, "clothes watch: every kind section watches the kind's item, the colour sections also their pattern, the selector the top and inner-top items; the coordinate nothing");
            var sess = new SceneSession(new SessionOptions { Ids = new CountingIdSource(95) });
            string id = sess.AddChara(new CharaAppearance { Stored = new CharaSubtree() }).NewIds[0];
            int resolves = 0, total = 0;
            foreach (var s in ClothesSections.Where(x => x.Watch != null))
                foreach (var w in s.Watch)
                {
                    total++;
                    try { FieldAccessor.LeafType(sess.Model, id, w); resolves++; } catch (CommandException) { t.Check(false, "clothes watch: " + w + " does not resolve"); }
                }
            t.Check(resolves == total && total == 2 + 8 * (1 + 3 * 2 + 1), "clothes watch: the " + total + " watched paths resolve on a character");
        }

        static void Labels(TestRunner t)
        {
            L10nTable tb;
            try { tb = UiKit.Strings(t); }
            catch (Exception e) { t.Check(false, "clothes labels: strings.tsv loads: " + e.Message); return; }
            var keys = new SortedSet<string>(StringComparer.Ordinal) { InspectorSchemas.CharaEditPageLabel("clothes", "clothes"), InspectorSchemas.ClothesOnePieceTipKey };
            foreach (var s in ClothesSections)
            {
                keys.Add(s.LabelKey);
                foreach (var f in s.Fields) keys.Add(f.LabelKey);
            }
            for (int k = 0; k < 8; k++) keys.Add(InspectorSchemas.ClothesKindLabelKey(k));
            foreach (var m in InspectorSchemas.CoordinateModes) keys.Add(InspectorSchemas.CoordinateModeLabelKey(m));
            foreach (var lang in new[] { "en", "ja", "zh-Hans" })
            {
                var missing = keys.Where(k => string.IsNullOrEmpty(tb.Cell(k, lang))).ToList();
                t.Check(missing.Count == 0, "clothes labels: " + keys.Count + " keys have " + lang + " (" + string.Join(", ", missing.Take(12)) + ")");
            }
            t.Check(ClothesSections.Where(s => InspectorSchemas.ClothesKindOf(s.Id) >= 0).All(s => s.LabelKey == "inspector.charaEdit.page.clothes.clothes." + s.Id.Substring(s.Id.LastIndexOf('.') + 1)),
                "clothes labels: the kind sections share one label key per section kind");
            t.Equal("初期カラー", tb.Cell("inspector.charaEdit.clothes.defaultColours", "ja"), "clothes labels: «初期カラー» (HS2's label)");
            t.Equal("破れ具合", tb.Cell("inspector.charaEdit.clothes.breakRate", "ja"), "clothes labels: «破れ具合»");
            t.Equal("オプションパーツ02を表示", tb.Cell("inspector.charaEdit.clothes.option2", "ja"), "clothes labels: «オプションパーツ02を表示»");
            t.Equal("インナー上", tb.Cell(InspectorSchemas.ClothesKindLabelKey(2), "ja"), "clothes labels: the inner top is «インナー上»");
            t.Equal("ワンピースの上が選ばれています", tb.Cell(InspectorSchemas.ClothesOnePieceTipKey, "ja"), "clothes labels: the one-piece tooltip");
            t.Equal("全て読込み", tb.Cell(InspectorSchemas.CoordinateModeLabelKey("all"), "ja"), "clothes labels: «全て読込み»");
        }
    }
}
