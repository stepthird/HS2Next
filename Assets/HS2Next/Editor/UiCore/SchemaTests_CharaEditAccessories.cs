// The Accessories page of the Edit tab (Phase 4.5; InspectorSchemas.CharaEdit and InspectorSchemas.AccessoryRows): one page
// `accessories` with the slot list section and the Copy section, and per accessory (generated for its build slot and
// category) Type · Colour · Hair colour · Parent · Adjust with concrete paths under `appearance.coord.accessories["<id>"].`.
// Checked: the sections in order and their ids ↔ build slots; every value row resolves on a character with stored documents
// with its declared leaf type and is writable; the counts per slot (lists 1, choices 1, colours 8, sliders 10, vector rows 6,
// toggles 1, buttons 8, the parent picker 1) and of the page (the copy section's toggle and 8 buttons), equal for both sexes;
// the slider ranges, the colour alpha (colour 4 only), the reset sources (gloss / metallic the prefab default, the hair
// sliders and the colours the reference, the transform components their constants), the transform grids and limits
// (typed 12.34 → 12.3, 400 → 100; rotation 359.6 → 359, −30 → 0, 359 + 5 → 4; scale 0 → 0.01; the stored value is never
// rewritten), the type choice (14 values 350..363), the labels of the ops; the gates on the right rows (section gates implied
// by their rows' gates) and the `acc` family parsing alike in SchemaGates and the scene layer's MakerGate; the watched paths;
// the action helpers; every label key in en, ja and zh-Hans.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HS2Next.UI.Core.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;
    using HS2Next.Scenes;
    using HS2Next.UI.Core;

    public static class SchemaTests_CharaEditAccessories
    {
        public static void Run(TestRunner t)
        {
            Structure(t);
            Resolve(t);
            Counts(t);
            Rules(t);
            Grids(t);
            Gates(t);
            Watch(t);
            Actions(t);
            Labels(t);
        }

        const string AccId = "acc0001";
        const int Slot = 4, Category = 353;

        static IEnumerable<SchemaSection> PageSections => InspectorSchemas.CharaEdit.Where(s => InspectorSchemas.CharaEditTreeOf(s) == "accessories");
        static IReadOnlyList<SchemaSection> SlotSections(string id = AccId, int slot = Slot, int category = Category) => InspectorSchemas.AccessoryRows(id, slot, category);
        static IEnumerable<FieldSpec> SlotRows(string id = AccId, int slot = Slot) => SlotSections(id, slot).SelectMany(s => s.Fields);
        static string Pre(string id = AccId) => "appearance.coord.accessories[\"" + id + "\"].";
        static string Member(FieldSpec f, string id = AccId) => f.Path != null && f.Path.StartsWith(Pre(id), StringComparison.Ordinal) ? f.Path.Substring(Pre(id).Length) : f.Path;
        static string Key(FieldSpec f) => Member(f) ?? f.Action;
        static FieldSpec Row(string member) => SlotRows().Single(f => Member(f) == member);

        static void Structure(TestRunner t)
        {
            t.Check(InspectorSchemas.AccessoriesPages.SequenceEqual(new[] { "accessories" }) && InspectorSchemas.CharaEditPagesOf("accessories").SequenceEqual(new[] { "accessories" }),
                "accessories: one page `accessories` (no page strip)");
            var page = PageSections.ToList();
            t.Check(page.Select(s => s.Id).SequenceEqual(new[] { InspectorSchemas.AccessorySlotsSection, InspectorSchemas.AccessoryCopySection }) &&
                    InspectorSchemas.AccessorySlotsSection == "accessories.accessories.slots" && InspectorSchemas.AccessoryCopySection == "accessories.accessories.copy",
                "accessories: the page's fixed sections are the slot list and the copy section (" + string.Join(", ", page.Select(s => s.Id)) + ")");
            t.Check(page.All(s => InspectorSchemas.CharaEditPageOf(s) == "accessories") && InspectorSchemas.CharaEditSections("accessories", "accessories").Count() == 2,
                "accessories: the fixed sections parse into node accessories, page accessories");
            t.Equal(0, page[0].Fields.Count, "accessories: the slot list section has no rows (the host puts the slot list with its «＋» row there)");
            var secs = SlotSections();
            var want = InspectorSchemas.AccessorySlotSections.Select(s => "accessories.accessories.05." + s).ToList();
            t.Check(secs.Select(s => s.Id).SequenceEqual(want) && InspectorSchemas.AccessorySlotSections.SequenceEqual(new[] { "type", "colour", "hairColour", "parent", "adjust" }),
                "accessories: a slot's sections in HS2's tab order type, colour, hair colour, parent, adjust, ids with the slot number the user sees (" + string.Join(", ", secs.Select(s => s.Id)) + ")");
            t.Check(secs.All(s => InspectorSchemas.CharaEditTreeOf(s) == "accessories" && InspectorSchemas.CharaEditPageOf(s) == "accessories" && !s.IsRepeated && s.Fields.All(f => !f.IsTemplate)),
                "accessories: the slot sections parse into node accessories, page accessories; no template (concrete paths)");
            int bad = 0;
            foreach (int slot in new[] { 0, 4, 9, 19, 20, 61, 98 })
                foreach (var s in InspectorSchemas.AccessorySlotSections)
                {
                    string id = InspectorSchemas.AccessorySectionId(slot, s);
                    if (InspectorSchemas.AccessorySlotOf(id) != slot || !id.Contains("." + InspectorSchemas.AccessorySlotLabel(slot) + ".")) bad++;
                }
            t.Check(bad == 0 && InspectorSchemas.AccessorySlotLabel(0) == "01" && InspectorSchemas.AccessorySlotLabel(19) == "20" && InspectorSchemas.AccessorySlotLabel(20) == "21" &&
                    InspectorSchemas.AccessorySlotLabel(99) == "100" && InspectorSchemas.AccessorySlotOf(InspectorSchemas.AccessorySlotsSection) == -1 &&
                    InspectorSchemas.AccessorySlotOf(InspectorSchemas.AccessoryCopySection) == -1 && InspectorSchemas.AccessorySlotOf("accessories.accessories.00.type") == -1 &&
                    InspectorSchemas.AccessorySlotOf("clothes.clothes.top.type") == -1 && InspectorSchemas.AccessorySlotOf(null) == -1 && AccessorySlotRule.Label(20) == InspectorSchemas.AccessorySlotLabel(20),
                "accessories: AccessorySectionId / AccessorySlotOf round-trip (slot + 1 with two digits, as AccessorySlotRule.Label); the fixed sections and other ids → −1");
            t.Check(secs.All(s => s.Fields.Select(f => f.Id).Distinct().Count() == s.Fields.Count), "accessories: row ids of a section are distinct");
            var ids = SlotRows().Select(f => f.Id).ToList();
            var shared = ids.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key.Substring(g.Key.IndexOf('.', g.Key.IndexOf("accessories[", StringComparison.Ordinal)) + 1)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            t.Check(shared.SequenceEqual(new[] { "colors[0].color", "colors[0].metallic", "colors[1].color", "colors[2].color", "colors[3].color" }),
                "accessories: the normal and the hair colour sections share exactly the members both write (colours 1–4, colour 1's metallic; the gates show one of the two) (" + string.Join(", ", shared) + ")");
            ids = ids.Distinct().ToList();
            var other = SlotRows("acc0002", 5).Select(f => f.Id);
            t.Check(!ids.Intersect(other).Any(), "accessories: two accessories' rows have distinct ids (paths and actions carry the accessory id)");
            var type = secs[0].Fields.Select(Key).ToList();
            t.Check(type.SequenceEqual(new[] { "category", "item" }), "accessories: Type = the type choice and the item list (" + string.Join(", ", type) + ")");
            var col = secs[1].Fields.Select(Key).ToList();
            var expectCol = Enumerable.Range(0, 4).SelectMany(n => new[] { "colors[" + n + "].color", "colors[" + n + "].gloss", "colors[" + n + "].metallic" }).ToList();
            expectCol.Add(InspectorSchemas.ActionResetAccessoryColours(AccId));
            t.Check(col.SequenceEqual(expectCol), "accessories: Colour = per colour 1–4 «カラー», «ツヤ», «質感», then «初期カラー» (" + string.Join(", ", col) + ")");
            var hair = secs[2].Fields.Select(Key).ToList();
            var expectHair = new List<string> { "colors[0].color", "colors[1].color", "colors[2].color", "colors[3].color", "colors[0].metallic", "colors[0].smoothness" };
            expectHair.AddRange(Enumerable.Range(0, 4).Select(k => InspectorSchemas.ActionMatchAccessoryHairColour(k, AccId)));
            t.Check(hair.SequenceEqual(expectHair), "accessories: Hair colour = «基本» «根本» «毛先» «ツヤ» colours, «質感», «滑らかさ», «髪の毛の色を取得» × 4 (" + string.Join(", ", hair) + ")");
            var parent = secs[3].Fields.Select(Key).ToList();
            t.Check(parent.SequenceEqual(new[] { "parent", InspectorSchemas.ActionResetAccessoryParent(AccId) }), "accessories: Parent = the attach point picker and «初期親»");
            var adjust = secs[4].Fields.Select(Key).ToList();
            var expectAdj = new List<string> { "noShake" };
            for (int c = 0; c < 2; c++) expectAdj.AddRange(new[] { "moves[" + c + "].position", "moves[" + c + "].rotation", "moves[" + c + "].scale", InspectorSchemas.ActionResetAccessoryMove(c + 1, AccId) });
            t.Check(adjust.SequenceEqual(expectAdj), "accessories: Adjust = «揺らさない», per set «位置» «回転» «拡縮» and «初期化» (" + string.Join(", ", adjust) + ")");
            var copy = page[1].Fields;
            t.Check(copy.Count == 9 && copy[0].Kind == FieldKind.Bool && copy[0].Target == FieldTarget.Option && copy[0].Path == InspectorSchemas.AccessoryMirrorParentOption &&
                    copy[0].OpLabel == null && copy.Skip(1).All(f => f.Kind == FieldKind.Button && f.OpLabel == OpLabels.CopyAccessory && f.Gate == null) &&
                    copy.Skip(1).Select(f => f.Action).SequenceEqual(InspectorSchemas.AccessoryCopyButtons.Select(b => b.action)),
                "accessories: Copy = «左右の親を変更» (an option, no history) and the 8 copy buttons (Copy accessory, ungated) in HS2's order");
        }

        static CharaSubtree WithAccessory(int sex)
        {
            var st = new CharaSubtree();
            st.Chara.Profile.Sex = sex;
            st.Coord.Sex = sex;
            st.Coord.Accessories.Add(new AccessoryEntry { Id = AccId, Category = Category, Item = "illusion.hs2#0", ImportSlot = Slot });
            return st;
        }

        static bool Fits(FieldSpec f, Type leaf)
        {
            switch (f.Kind)
            {
                case FieldKind.Float: return leaf == typeof(float);
                case FieldKind.Bool: return leaf == typeof(bool);
                case FieldKind.Color: return leaf == typeof(Color4);
                case FieldKind.Vec3: return leaf == typeof(Vec3);
                case FieldKind.Enum: return leaf == typeof(int);
                case FieldKind.PartList: case FieldKind.AttachPoint: return leaf == typeof(string);
            }
            return false;
        }

        static void Resolve(TestRunner t)
        {
            var s = new SceneSession(new SessionOptions { Ids = new CountingIdSource(97) });
            var r = s.AddChara(new CharaAppearance { Stored = WithAccessory(1) });
            t.Check(r.Status == CommandStatus.Ok, "accessories resolve: a character with one accessory in slot 05");
            string id = r.NewIds[0];
            int n = 0, fails = 0;
            foreach (var f in SlotRows())
            {
                if (f.Kind == FieldKind.Button) { if (f.Path != null || f.Action == null) { fails++; t.Check(false, "accessories: button " + f.LabelKey + " needs an action and no path"); } continue; }
                n++;
                try
                {
                    var leaf = FieldAccessor.LeafType(s.Model, id, f.Path);
                    if (!Fits(f, leaf)) { fails++; t.Check(false, "accessories: " + Key(f) + " is a " + leaf.Name + ", declared " + f.Kind); }
                    if (!FieldAccessor.IsWritable(s.Model, id, f.Path, out string code)) { fails++; t.Check(false, "accessories: " + f.Path + " is not writable (" + code + ")"); }
                    if (FieldPath.Canonical(f.Path) != f.Path || f.Path != AppearancePaths.Accessory(AccId, Member(f))) { fails++; t.Check(false, "accessories: " + f.Path + " is not canonical"); }
                }
                catch (CommandException e) { fails++; t.Check(false, "accessories: " + f.Path + " does not resolve: " + e.Code + " " + e.Message); }
            }
            t.Check(fails == 0 && n == 2 + 12 + 6 + 1 + 1 + 6, "accessories resolve: " + n + " value rows resolve with their declared leaf types, are writable and canonical");
            t.Check(!FieldAccessor.IsWritable(s.Model, id, AppearancePaths.Accessory(AccId, "importSlot"), out string ic) && ic == CommandCodes.Unsupported,
                "accessories resolve: the preferred slot importSlot is not writable by a set (the slot ops write it)");
            var gloss = Row("colors[1].gloss");
            t.Check(s.Set(id, gloss.Path, (float)gloss.ParseTyped(150)).Status == CommandStatus.Ok && (float)FieldAccessor.Get(s.Model, id, gloss.Path) == 1.5f,
                "accessories: typed 150 into «ツヤ» stores 1.5 (nothing clamps stored values)");
            var pos = Row("moves[0].position");
            t.Check(s.Set(id, pos.Path, new Vec3(150f, -0.05f, 3f)).Status == CommandStatus.Ok && FieldAccessor.Get(s.Model, id, pos.Path) is Vec3 v && v.X == 150f && v.Y == -0.05f,
                "accessories: a raw set keeps a position outside the UI limits and off the grid (the bridge writes raw)");
            var male = SlotRows().Select(f => f.Id + " " + f.CategoryFemale + "/" + f.CategoryMale).ToList();
            t.Check(SlotRows().Where(f => f.Kind == FieldKind.PartList).All(f => f.CategoryFemale == Category && f.CategoryMale == Category && f.ListContext == FieldSpec.ListContextAccessory),
                "accessories: the item list is the accessory's category for both sexes");
        }

        static bool IsSlider(FieldSpec f) => f.Kind == FieldKind.Float && f.Scale == InspectorSchemas.MakerSliderScale;

        static int[] CountOf(IEnumerable<FieldSpec> rows)
        {
            var l = rows.ToList();
            return new[]
            {
                l.Count(f => f.Kind == FieldKind.PartList), l.Count(f => f.Kind == FieldKind.Enum), l.Count(f => f.Kind == FieldKind.Color), l.Count(IsSlider),
                l.Count(f => f.Kind == FieldKind.Vec3), l.Count(f => f.Kind == FieldKind.Bool), l.Count(f => f.Kind == FieldKind.Button), l.Count(f => f.Kind == FieldKind.AttachPoint),
            };
        }

        static void Counts(TestRunner t)
        {
            var per = CountOf(SlotRows());
            t.Check(per.SequenceEqual(new[] { 1, 1, 8, 10, 6, 1, 8, 1 }),
                "accessories counts: per slot lists / choices / colours / sliders / vector rows / toggles / buttons / parent pickers = 1 / 1 / 8 / 10 / 6 / 1 / 8 / 1 (" + string.Join(" / ", per) + ")");
            t.Equal(SlotRows().Count(), per.Sum(), "accessories counts: no other kind of row in a slot");
            t.Equal(18, SlotRows().Count(f => f.Kind == FieldKind.Vec3) * 3, "accessories counts: 18 transform values (2 sets × 3 vectors × 3)");
            var page = CountOf(PageSections.SelectMany(s => s.Fields));
            t.Check(page.SequenceEqual(new[] { 0, 0, 0, 0, 0, 1, 8, 0 }), "accessories counts: page level = the copy section's 1 toggle and 8 buttons (" + string.Join(" / ", page) + ")");
            // both sexes: the rows do not depend on the sex (the same page, lists 351..363 for both)
            t.Check(SlotRows().All(f => f.Gate == null || (!f.Gate.Contains("chara.female") && !f.Gate.Contains("chara.male"))) &&
                    SlotSections(AccId, Slot, 361).SelectMany(s => s.Fields).Select(f => f.Id).SequenceEqual(SlotRows().Select(f => f.Id)),
                "accessories counts: equal for both sexes (no sex gate) and for every category (only the list's category differs)");
        }

        static void Rules(TestRunner t)
        {
            var rows = SlotRows().ToList();
            var sliders = rows.Where(IsSlider).ToList();
            int bad = sliders.Count(f => f.Min != 0 || f.Max != 1 || f.TypedMin != -1 || f.TypedMax != 2 || f.Format != "0" || f.Applies != Applies.All || f.OpLabel != OpLabels.EditAccessory || f.LerpClamped);
            t.Check(bad == 0 && rows.Where(f => f.Kind == FieldKind.Float).All(IsSlider),
                "accessories rules: every slider has the Maker ranges (0..1, typed −1..2, ×100, \"0\", direct), label Edit accessory (" + bad + " differ)");
            var alpha = rows.Where(f => f.Kind == FieldKind.Color && f.ColorAlpha).Select(Key).ToList();
            t.Check(alpha.SequenceEqual(new[] { "colors[3].color" }) && rows.Single(f => f.Kind == FieldKind.Color && f.ColorAlpha).Gate == SchemaGates.MakerAcc(Slot, SchemaGates.AccAlpha) &&
                    rows.Where(f => f.Kind == FieldKind.Color).All(f => f.ColorAlphaGate == null),
                "accessories rules: alpha only on colour 4 of the normal section (the alpha renderers); areas 1–3 and the hair colours without alpha");
            var prefab = rows.Where(f => f.ResetKind == ResetKind.PrefabDefault).Select(Key).ToList();
            var expectPrefab = Enumerable.Range(0, 4).SelectMany(n => new[] { "colors[" + n + "].gloss", "colors[" + n + "].metallic" }).ToList();
            t.Check(prefab.SequenceEqual(expectPrefab), "accessories rules: ResetKind PrefabDefault exactly on «ツヤ» / «質感» of colours 1–4 (" + string.Join(", ", prefab) + ")");
            var constant = rows.Where(f => f.ResetKind == ResetKind.Constant).ToList();
            t.Check(constant.Count == 6 && constant.All(f => f.Kind == FieldKind.Vec3) &&
                    constant.All(f => f.Default is double[] d && d.Length == 3 && d.All(x => x == (Member(f).EndsWith(".scale", StringComparison.Ordinal) ? 1.0 : 0.0))),
                "accessories rules: ResetKind Constant on the six vector rows (position 0, rotation 0, scale 1)");
            t.Check(rows.Where(f => f.ResetKind == ResetKind.Reference).All(f => f.Kind != FieldKind.Vec3) && Row("colors[0].smoothness").ResetKind == ResetKind.Reference &&
                    rows.Count(f => Member(f) == "colors[0].metallic" && f.ResetKind == ResetKind.Reference) == 1,
                "accessories rules: the hair sliders and every colour reset to the reference");
            var type = Row("category");
            t.Check(type.Kind == FieldKind.Enum && type.Choices.SequenceEqual(InspectorSchemas.AccessoryTypeNames) && type.ChoiceValues.SequenceEqual(Enumerable.Range(350, 14)) &&
                    type.OpLabel == OpLabels.ChangeItem && type.Gate == null && Enumerable.Range(0, 14).All(i => type.ChoiceLabelKey(i) == InspectorSchemas.AccessoryTypeLabelKey(i)),
                "accessories rules: the type choice has the 14 types (values 350..363, «なし» first), label Change item");
            var item = Row("item");
            t.Check(item.OpLabel == OpLabels.ChangeItem && item.Gate == null && item.Default == null, "accessories rules: the item list is labelled Change item, ungated");
            var parent = Row("parent");
            t.Check(parent.OpLabel == OpLabels.EditAccessory && Equals(parent.Default, "") && parent.Gate == SchemaGates.MakerAcc(Slot, SchemaGates.AccParent), "accessories rules: the parent picker writes Edit accessory");
            var noShake = Row("noShake");
            t.Check(Equals(noShake.Default, false) && !noShake.Inverted && noShake.Gate == null && noShake.OpLabel == OpLabels.EditAccessory, "accessories rules: «揺らさない» = noShake, default off, ungated");
            t.Check(rows.Where(f => f.Kind != FieldKind.Button && f.Kind != FieldKind.PartList && f.Kind != FieldKind.Enum).All(f => f.OpLabel == OpLabels.EditAccessory) &&
                    rows.Where(f => f.Kind == FieldKind.Button).All(f => f.OpLabel == OpLabels.EditAccessory) && rows.All(f => f.Applies == Applies.All && f.TooltipKey == null),
                "accessories rules: values and buttons are labelled Edit accessory");
            t.Check(rows.Where(f => f.Path != null).All(f => MakerEdits.AreaOf(new[] { f.Path }) == "accessory"), "accessories rules: every row's member is in the accessory area (Edit accessory through the bridge too)");
            t.Check(OpLabels.All.Contains(OpLabels.MoveAccessory) && OpLabels.MoveAccessory == "Move accessory" && OpLabels.EditAccessory == "Edit accessory", "accessories rules: Move accessory is an OpLabels constant");
        }

        static void Grids(TestRunner t)
        {
            var pos = Row("moves[0].position");
            var rot = Row("moves[1].rotation");
            var scl = Row("moves[0].scale");
            t.Check(pos.Grid == 0.1 && pos.TypedMin == -100 && pos.TypedMax == 100 && !pos.Wrap360 && !pos.GridTruncate && pos.Format == "0.0" &&
                    rot.Grid == 1 && rot.GridTruncate && rot.Wrap360 && rot.TypedMin == 0 && rot.TypedMax == 360 && scl.Grid == 0.01 && scl.TypedMin == 0.01 && scl.TypedMax == 100 &&
                    scl.Format == "0.00" && pos.Min == null && pos.Max == null,
                "accessories grids: position −100..100 on 0.1, rotation 0..360 truncated and wrapped, scale 0.01..100 on 0.01 (HS2's input rules)");
            t.Check(Close(pos.ClampTyped(12.34), 12.3) && pos.ClampTyped(400) == 100 && pos.ClampTyped(-400) == -100 && Close(pos.ClampTyped(-12.35), -12.4) && Close(pos.ClampTyped(0.05), 0.1),
                "accessories grids: typed position 12.34 → 12.3, 400 → 100, −400 → −100 (half away from zero)");
            t.Check(rot.ClampTyped(359.6) == 359 && rot.ClampTyped(-30) == 0 && rot.ClampTyped(400) == 0 && rot.ClampTyped(360) == 0 && rot.ClampTyped(12.9) == 12,
                "accessories grids: typed rotation 359.6 → 359, −30 → 0, 400 → 0 (HS2 clamps typed text to 0..360, truncates, wraps)");
            t.Check(scl.ClampTyped(0) == 0.01 && scl.ClampTyped(1000) == 100 && Close(scl.ClampTyped(1.234), 1.23), "accessories grids: typed scale 0 → 0.01, 1000 → 100, 1.234 → 1.23");
            t.Check(rot.Step(359, 5) == 4 && rot.Step(2, -5) == 357 && rot.Step(10.7, 1) == 11 && Close(pos.Step(99.95, 0.1), 100) && pos.Step(100, 10) == 100 && Close(pos.Step(1.25, 0.1), 1.4) &&
                    scl.Step(0.01, -0.1) == 0.01 && Close(scl.Step(1, 0.1), 1.1),
                "accessories grids: steps wrap the rotation (359 + 5 → 4, 2 − 5 → 357) and clamp position / scale to their limits");
            t.Check(pos.OnGrid(12.3456) is double g && Close(g, 12.3) && new FieldSpec().OnGrid(1.2345) == 1.2345 && new FieldSpec().Step(1, 2) == 3,
                "accessories grids: OnGrid / Step without a grid leave the value as it is");
            // existing Wrap360 rows without a grid keep their old typed rule (v % 360)
            var plain = new FieldSpec { Wrap360 = true, Whole = true };
            t.Check(plain.ClampTyped(370) == 10 && plain.ClampTyped(-30) == -30, "accessories grids: a Wrap360 row without a grid keeps the % 360 rule of the map pose");
        }

        static bool Close(double a, double b) => Math.Abs(a - b) < 1e-9;

        static string ExpectGate(FieldSpec f)
        {
            string m = Member(f) ?? "";
            string a = f.Action ?? "";
            if (m == "category" || m == "item" || m == "noShake") return null;
            if (a.StartsWith(InspectorSchemas.ActionResetAccessoryColoursPrefix, StringComparison.Ordinal)) return "colour";
            if (a.StartsWith(InspectorSchemas.ActionMatchAccessoryHairColourPrefix, StringComparison.Ordinal)) return "hair";
            if (a.StartsWith(InspectorSchemas.ActionResetAccessoryParentPrefix, StringComparison.Ordinal) || m == "parent") return "parent";
            if (a.StartsWith(InspectorSchemas.ActionResetAccessoryMovePrefix, StringComparison.Ordinal)) return "move" + a.Substring(InspectorSchemas.ActionResetAccessoryMovePrefix.Length, 1);
            if (m.StartsWith("moves[", StringComparison.Ordinal)) return "move" + (m[6] - '0' + 1);
            return null;
        }

        static void Gates(TestRunner t)
        {
            int bad = 0;
            var secs = SlotSections();
            foreach (var s in secs)
            {
                string sec = s.Id.Substring(s.Id.LastIndexOf('.') + 1);
                foreach (var f in s.Fields)
                {
                    string e = ExpectGate(f);
                    if (sec == "colour" && f.Kind != FieldKind.Button)
                    {
                        int n = Member(f)[7] - '0';
                        e = n < 3 ? "area" + (n + 1) : "alpha";
                    }
                    if (sec == "hairColour" && f.Kind != FieldKind.Button) e = Member(f) == "colors[1].color" ? "top" : Member(f) == "colors[2].color" ? "under" : "hair";
                    string want = e == null ? null : SchemaGates.MakerAcc(Slot, e);
                    if (f.Gate != want) { bad++; t.Check(false, "accessories gates: " + Key(f) + " gate " + (f.Gate ?? "none") + ", expected " + (want ?? "none")); }
                }
                string secWant = sec == "colour" ? "colour" : sec == "hairColour" ? "hair" : sec == "parent" ? "parent" : null;
                if (s.Gate != (secWant == null ? null : SchemaGates.MakerAcc(Slot, secWant))) { bad++; t.Check(false, "accessories gates: section " + s.Id + " gate " + (s.Gate ?? "none")); }
                if (s.Gate != null)
                    foreach (var f in s.Fields)
                    {
                        // the rows of a gated section carry its gate or a narrower one
                        string ff = SchemaGates.TryMakerAcc(f.Gate, out int gs, out string feat) && gs == Slot ? feat : null;
                        bool implies = ff == secWant || (secWant == "colour" && (ff == "area1" || ff == "area2" || ff == "area3" || ff == "alpha")) || (secWant == "hair" && (ff == "top" || ff == "under"));
                        if (!implies) { bad++; t.Check(false, "accessories gates: " + Key(f) + " (" + f.Gate + ") does not imply its section's gate " + s.Gate); }
                    }
            }
            t.Equal(0, bad, "accessories gates: type ungated; colour = colour (areas 1–3, alpha, «初期カラー» colour); hair colour = hair (top, under); parent = parent; adjust: the sets move1 / move2, «揺らさない» ungated");
            t.Check(SlotRows().Where(f => f.Gate != null).All(f => SchemaGates.IsKnown(f.Gate)), "accessories gates: every gate is known to SchemaGates.IsKnown");
            // the two parsers agree on every accessory gate name (the scene layer keeps its own copy of the names)
            int same = 0, n2 = 0;
            foreach (int slot in new[] { 0, 1, 4, 19, 20, 25, 61, 9999 })
                foreach (var feat in SchemaGates.AccFeatures)
                {
                    n2++;
                    string g = SchemaGates.MakerAcc(slot, feat);
                    if (g == "maker.acc:" + slot + ":" + feat && SchemaGates.TryMakerAcc(g, out int s1, out string f1) && MakerEdits.TryAccessoryGate(g, out int s2, out string f2) &&
                        MakerGate.TryParse(g, out var mg) && mg.Family == MakerGate.Acc && mg.Name == g && mg.Index == slot && s1 == slot && s2 == slot && f1 == feat && f2 == feat) same++;
                }
            t.Check(same == n2 && n2 == 8 * 13 && SchemaGates.AccFeatures.SequenceEqual(MakerEdits.AccessoryGateFeatures) && SchemaGates.MakerAccPrefix == MakerEdits.AccessoryGatePrefix &&
                    MakerGate.FeaturesOf(MakerGate.Acc).SequenceEqual(SchemaGates.AccFeatures),
                "accessories gates: the gate names parse alike in SchemaGates.TryMakerAcc, MakerEdits.TryAccessoryGate and MakerGate.TryParse (" + same + " / " + n2 + ")");
            foreach (var g in new[] { "maker.acc:x:prefab", "maker.acc:-1:prefab", "maker.acc:4", "maker.acc::prefab", "maker.acc:4:nonsense", "maker.acc:12345:prefab", "maker.acc:4:area4",
                         "maker.acc:4:move3", "maker.acc: 4:prefab", "maker.clothes:top:prefab", null })
            {
                bool other = g == "maker.clothes:top:prefab";
                t.Check(!SchemaGates.TryMakerAcc(g, out _, out _) && !MakerEdits.TryAccessoryGate(g, out _, out _) && (other ? SchemaGates.IsKnown(g) : !SchemaGates.IsKnown(g)) &&
                        (g == null || other || (!MakerGate.TryParse(g, out _) && !MakerGate.UnparsedHolds(g))),
                    "accessories gates: " + (g ?? "null") + " is not an accessory gate (a malformed one does not hold)");
            }
            t.Check(SchemaGates.AccArea(2) == "area2" && SchemaGates.AccMove(1) == "move1" && SchemaGates.MakerAcc(20, SchemaGates.AccHead) == "maker.acc:20:head",
                "accessories gates: name helpers");
        }

        static void Watch(TestRunner t)
        {
            int bad = 0;
            string p = Pre();
            foreach (var s in SlotSections())
            {
                string sec = s.Id.Substring(s.Id.LastIndexOf('.') + 1);
                var want = sec == "colour" || sec == "hairColour" ? new[] { p + "item", p + "category", "appearance.chara.hair.shaderType" } : new[] { p + "item", p + "category" };
                if (s.Watch == null || !s.Watch.SequenceEqual(want)) { bad++; t.Check(false, "accessories watch: " + s.Id + " watches " + (s.Watch == null ? "nothing" : string.Join(", ", s.Watch))); }
            }
            var page = PageSections.ToList();
            t.Check(bad == 0 && page[0].Watch != null && page[0].Watch.SequenceEqual(new[] { "appearance.coord.accessories" }) && page[1].Watch == null,
                "accessories watch: every slot section watches the item and the category, the colour sections also hair.shaderType; the slot list the accessory list; the copy section nothing");
            var sess = new SceneSession(new SessionOptions { Ids = new CountingIdSource(99) });
            string id = sess.AddChara(new CharaAppearance { Stored = WithAccessory(0) }).NewIds[0];
            int resolves = 0, total = 0;
            foreach (var w in SlotSections().Concat(page).Where(x => x.Watch != null).SelectMany(x => x.Watch))
            {
                total++;
                try { FieldAccessor.LeafType(sess.Model, id, w); resolves++; } catch (CommandException) { t.Check(false, "accessories watch: " + w + " does not resolve"); }
            }
            t.Check(resolves == total && total == 5 * 2 + 2 + 1, "accessories watch: the " + total + " watched paths resolve on a character (male)");
        }

        static void Actions(TestRunner t)
        {
            string a = InspectorSchemas.ActionMatchAccessoryHairColour(2, "x:y");
            t.Check(InspectorSchemas.AccessoryIdOfAction(a, InspectorSchemas.ActionMatchAccessoryHairColourPrefix, out int part) == "x:y" && part == 2,
                "accessories actions: the hair colour fetch carries the part and the accessory id (" + a + ")");
            t.Check(InspectorSchemas.AccessoryIdOfAction(InspectorSchemas.ActionResetAccessoryMove(2, AccId), InspectorSchemas.ActionResetAccessoryMovePrefix, out int set) == AccId && set == 2 &&
                    InspectorSchemas.AccessoryIdOfAction(InspectorSchemas.ActionResetAccessoryColours(AccId), InspectorSchemas.ActionResetAccessoryColoursPrefix, out int none) == AccId && none == -1 &&
                    InspectorSchemas.AccessoryIdOfAction(InspectorSchemas.ActionResetAccessoryParent(AccId), InspectorSchemas.ActionResetAccessoryParentPrefix, out _) == AccId &&
                    InspectorSchemas.AccessoryIdOfAction("ops.ResetAccessoryMove:3:" + AccId, InspectorSchemas.ActionResetAccessoryMovePrefix, out _) == null &&
                    InspectorSchemas.AccessoryIdOfAction("ops.MatchAccessoryHairColour:top:" + AccId, InspectorSchemas.ActionMatchAccessoryHairColourPrefix, out _) == null &&
                    InspectorSchemas.AccessoryIdOfAction("ops.ResetClothesColours:top", InspectorSchemas.ActionResetAccessoryColoursPrefix, out _) == null &&
                    InspectorSchemas.AccessoryIdOfAction(null, InspectorSchemas.ActionResetAccessoryColoursPrefix, out _) == null,
                "accessories actions: AccessoryIdOfAction reads the accessory id (and the set / part) of each slot button; other actions → null");
            var b = InspectorSchemas.AccessoryCopyButtons;
            t.Check(b.Count == 8 && b.Select(x => x.set).SequenceEqual(new[] { 0, 0, 1, 2, 1, 2, 1, 2 }) && b.Select(x => x.mirror).SequenceEqual(new[] { "none", "none", "none", "none", "leftRight", "leftRight", "upDown", "upDown" }) &&
                    b.Select(x => x.target).SequenceEqual(new[] { false, true, true, true, true, true, true, true }) && b.Select(x => x.action).Distinct().Count() == 8,
                "accessories actions: the copy buttons (duplicate without a target; copy to a slot; the set copies and their left-right / up-down mirrors)");
            t.Check(InspectorSchemas.AccessoryEditPath("a\"b", "parent") == AppearancePaths.Accessory("a\"b", "parent") && InspectorSchemas.AccessoryEditPath(AccId, null) == AppearancePaths.Accessory(AccId),
                "accessories actions: AccessoryEditPath = the scene layer's accessory path (quoting included)");
            t.Check(InspectorSchemas.AccessoryCategoryNone == MakerEdits.AccessoryCategoryNone && InspectorSchemas.AccessoryCategoryFirst == MakerEdits.AccessoryCategoryFirst &&
                    InspectorSchemas.AccessoryCategoryLast == MakerEdits.AccessoryCategoryLast, "accessories actions: the category bounds agree with the scene layer");
        }

        static void Labels(TestRunner t)
        {
            L10nTable tb;
            try { tb = UiKit.Strings(t); }
            catch (Exception e) { t.Check(false, "accessories labels: strings.tsv loads: " + e.Message); return; }
            var keys = new SortedSet<string>(StringComparer.Ordinal)
            {
                InspectorSchemas.CharaEditPageLabel("accessories", "accessories"), InspectorSchemas.AccessoryHeadWeightedNoteKey, InspectorSchemas.AccessoryUnknownParentKey,
                InspectorSchemas.AccessoryMoveLabelKey(1), InspectorSchemas.AccessoryMoveLabelKey(2), "inspector.charaEdit.accessories.hairFetch",
            };
            foreach (var s in SlotSections().Concat(PageSections))
            {
                keys.Add(s.LabelKey);
                foreach (var f in s.Fields)
                {
                    keys.Add(f.LabelKey);
                    if (f.Choices != null) for (int i = 0; i < f.Choices.Count; i++) keys.Add(f.ChoiceLabelKey(i));
                }
            }
            for (int i = 0; i < 14; i++) keys.Add(InspectorSchemas.AccessoryTypeLabelKey(i));
            foreach (var lang in new[] { "en", "ja", "zh-Hans" })
            {
                var missing = keys.Where(k => string.IsNullOrEmpty(tb.Cell(k, lang))).ToList();
                t.Check(missing.Count == 0, "accessories labels: " + keys.Count + " keys have " + lang + " (" + string.Join(", ", missing.Take(12)) + ")");
            }
            t.Equal("初期カラー", tb.Cell("inspector.charaEdit.accessories.defaultColours", "ja"), "accessories labels: «初期カラー»");
            t.Equal("髪の毛の色を取得", tb.Cell("inspector.charaEdit.accessories.hairFetch", "ja"), "accessories labels: «髪の毛の色を取得»");
            t.Equal("揺らさない", tb.Cell("inspector.charaEdit.accessories.noShake", "ja"), "accessories labels: «揺らさない»");
            t.Equal("下腹部", tb.Cell(InspectorSchemas.AccessoryTypeLabelKey(13), "ja"), "accessories labels: type 363 «下腹部»");
            t.Equal("なし", tb.Cell(InspectorSchemas.AccessoryTypeLabelKey(0), "ja"), "accessories labels: «なし»");
            t.Equal("調整①を上下反転", tb.Cell("inspector.charaEdit.accessories.copyMove1UpDown", "ja"), "accessories labels: «調整①を上下反転»");
            t.Equal("不明な親: {0}", tb.Cell(InspectorSchemas.AccessoryUnknownParentKey, "ja"), "accessories labels: the unknown parent with its placeholder");
        }
    }
}
