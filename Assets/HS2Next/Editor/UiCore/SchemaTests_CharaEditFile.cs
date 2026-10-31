// The Character and File pages of the Edit tab (Phase 4.6; InspectorSchemas.CharaEdit): the Character node's one section
// («名前», then the read-only lines of sex, futanari, the imported game parameters and the import provenance) and the File
// node's two sections (character: save, thumbnail framing, preview retake, load; coordinate: save, load), plus the Clothes
// node's coordinate save button. Checked: section ids, pages and order; every tree node has at least one row for a female
// and for a male character (no node shows the "later" note any more); the counts of the spec's control tables (Character 17
// rows, 16 for males; File 4 + 2); kinds, actions and history labels (saves and the preview not recorded, the loads "Load
// character" / "Load coordinate"); the read-only rows (no op label, never the name row); the document rows resolve with their
// declared leaf types on a character with imported game parameters (the name writable, the sex and the parameters not); the
// framing option; every label key in en, ja and zh-Hans.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HS2Next.UI.Core.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;
    using HS2Next.Scenes;
    using HS2Next.UI.Core;

    public static class SchemaTests_CharaEditFile
    {
        public static void Run(TestRunner t)
        {
            Structure(t);
            EveryNode(t);
            CharacterRows(t);
            FileRows(t);
            Resolve(t);
            Labels(t);
        }

        static List<SchemaSection> NodeSections(string node) => InspectorSchemas.CharaEdit.Where(s => InspectorSchemas.CharaEditTreeOf(s) == node).ToList();

        static void Structure(TestRunner t)
        {
            t.Check(InspectorSchemas.CharacterPages.SequenceEqual(new[] { "character" }) && InspectorSchemas.CharaEditPagesOf("character").SequenceEqual(new[] { "character" }),
                "character: one page `character`");
            t.Check(InspectorSchemas.FilePages.SequenceEqual(new[] { "file" }) && InspectorSchemas.CharaEditPagesOf("file").SequenceEqual(new[] { "file" }), "file: one page `file`");
            t.Check(NodeSections("character").Select(s => s.Id).SequenceEqual(new[] { InspectorSchemas.CharacterProfileSection }) &&
                    InspectorSchemas.CharacterProfileSection == "character.character.profile", "character: the one section character.character.profile");
            t.Check(NodeSections("file").Select(s => s.Id).SequenceEqual(new[] { InspectorSchemas.FileCharacterSection, InspectorSchemas.FileCoordinateSection }) &&
                    InspectorSchemas.FileCharacterSection == "file.file.character" && InspectorSchemas.FileCoordinateSection == "file.file.coordinate",
                "file: the sections file.file.character, file.file.coordinate in order");
            var order = InspectorSchemas.CharaEdit.Select(InspectorSchemas.CharaEditTreeOf).Distinct().ToList();
            t.Check(order.SequenceEqual(InspectorSchemas.CharaEditTree), "the sections follow the tree order (" + string.Join(", ", order) + ")");
            t.Check(NodeSections("character").Concat(NodeSections("file")).All(s => !s.IsRepeated && s.Fields.All(f => !f.IsTemplate)), "character / file: no repeated section, no template");
        }

        // A row shows for a sex unless a sex gate excludes it (other gates depend on the character and are not judged here).
        static bool Shows(FieldSpec f, int sex) => f.Gate == null || !(f.Gate == SchemaGates.CharaFemale && sex == 0 || f.Gate == SchemaGates.CharaMale && sex == 1);

        static void EveryNode(TestRunner t)
        {
            foreach (var node in InspectorSchemas.CharaEditTree)
                foreach (int sex in new[] { 1, 0 })
                {
                    var secs = NodeSections(node);
                    bool any = secs.Count > 0 && (node == "hair" || node == "clothes" || node == "accessories" || secs.Any(s => s.Fields.Any(f => Shows(f, sex))));
                    t.Check(any, "node " + node + " has sections with rows for a " + (sex == 1 ? "female" : "male") + " character (no \"later\" note)");
                }
        }

        static void CharacterRows(TestRunner t)
        {
            var rows = NodeSections("character").SelectMany(s => s.Fields).ToList();
            t.Equal(17, rows.Count, "character: 17 rows (name, sex, futanari, 9 game parameters, 5 provenance lines)");
            t.Equal(16, rows.Count(f => Shows(f, 0)), "character: 16 rows for a male (no futanari line)");
            var name = rows[0];
            t.Check(name.Path == "appearance.chara.profile.name" && name.Kind == FieldKind.Text && !name.ReadOnly && name.OpLabel == OpLabels.RenameCharacter && name.Applies == Applies.Primary,
                "character: «名前» edits profile.name (Rename character, the primary)");
            var ro = rows.Skip(1).ToList();
            t.Check(ro.All(f => f.ReadOnly && f.OpLabel == null && f.Applies == Applies.Primary && f.Kind != FieldKind.Button), "character: every other row is read-only (no op label)");
            t.Check(ro[0].Path == "appearance.chara.profile.sex" && ro[0].Kind == FieldKind.Enum && ro[0].Choices.SequenceEqual(new[] { "male", "female" }), "character: the sex line (0 male, 1 female)");
            t.Check(ro[1].Path == "appearance.chara.profile.futanari" && ro[1].Kind == FieldKind.Bool && ro[1].Gate == SchemaGates.CharaFemale && ro[1].TooltipKey == "inspector.charaEdit.character.futanariHint",
                "character: the futanari line (female, the hint to Body › Special)");
            var game = ro.Skip(2).Take(9).ToList();
            t.Check(game.Select(f => f.Path).SequenceEqual(InspectorSchemas.CharacterGameParameters.Select(g => "appearance.chara.profile.hs2." + g.member)),
                "character: the nine game parameter lines under profile.hs2 (" + string.Join(", ", game.Select(f => f.Path)) + ")");
            t.Check(game.Single(f => f.Path.EndsWith(".hs2.voiceRate", StringComparison.Ordinal)).Format == "0.00" && game.Single(f => f.Path.EndsWith(".wishes", StringComparison.Ordinal)).Kind == FieldKind.Text,
                "character: voice rate with two decimals, wishes as a list text");
            var prov = ro.Skip(11).ToList();
            t.Check(prov.Count == 5 && prov.Select(f => f.Path).SequenceEqual(InspectorSchemas.CharacterProvenance.Select(p => InspectorSchemas.ProvenancePrefix + p.member)) && prov.All(f => !f.Path.StartsWith("appearance.", StringComparison.Ordinal)),
                "character: the five provenance lines (not document paths)");
            t.Check(rows.Select(f => f.Id).Distinct().Count() == rows.Count, "character: row ids distinct");
        }

        static void FileRows(TestRunner t)
        {
            var ch = NodeSections("file")[0].Fields;
            var co = NodeSections("file")[1].Fields;
            t.Check(ch.Count == 4 && co.Count == 2, "file: 4 rows in the character section (VN1–VN4), 2 in the coordinate section (VN10, VN11)");
            t.Check(ch[0].Kind == FieldKind.Button && ch[0].Action == InspectorSchemas.ActionSaveCharacter && ch[0].Action == "ops.SaveCharacter" && ch[0].OpLabel == null,
                "file: «名前を付けて保存…» = ops.SaveCharacter, not recorded");
            var fr = ch[1];
            t.Check(fr.Kind == FieldKind.Enum && fr.Target == FieldTarget.Option && fr.Path == InspectorSchemas.ThumbnailFramingOption && fr.Path == "maker.thumbnailFraming" &&
                    fr.Choices.SequenceEqual(new[] { "front", "view" }) && Equals(fr.Default, "view") && fr.OpLabel == null && InspectorSchemas.ThumbnailFramings.SequenceEqual(fr.Choices),
                "file: the framing choice is the view option maker.thumbnailFraming (front / view, default view)");
            t.Check(ch[2].Kind == FieldKind.Button && ch[2].Action == "ops.RetakeThumbnail" && ch[2].OpLabel == null, "file: «撮影し直す» = ops.RetakeThumbnail, not recorded");
            t.Check(ch[3].Kind == FieldKind.Button && ch[3].Action == "ops.LoadCharacter" && ch[3].OpLabel == OpLabels.LoadCharacter && OpLabels.LoadCharacter == "Load character" && OpLabels.All.Contains(OpLabels.LoadCharacter),
                "file: «キャラを読込…» = ops.LoadCharacter, Load character");
            t.Check(co[0].Kind == FieldKind.Button && co[0].Action == "ops.SaveCoordinate" && co[0].OpLabel == null, "file: «コーデを保存…» = ops.SaveCoordinate, not recorded");
            t.Check(co[1].Kind == FieldKind.Button && co[1].Action == InspectorSchemas.ActionApplyCoordinate && co[1].OpLabel == OpLabels.LoadCoordinate, "file: «コーデを読込…» = 4.4's ops.ApplyCoordinate, Load coordinate");
            t.Check(ch.Concat(co).All(f => (f.Target == FieldTarget.Option || f.Applies == Applies.Primary) && f.Gate == null && !f.ReadOnly), "file: every button acts on the primary; every row ungated");
            t.Check(InspectorSchemas.LoadFlagsOption == "maker.loadFlags", "file: the load dialog's flags are the view option maker.loadFlags");
            var clothes = InspectorSchemas.CharaEdit.Single(s => s.Id == InspectorSchemas.ClothesCoordinateSection).Fields;
            t.Check(clothes.Any(f => f.Kind == FieldKind.Button && f.Action == InspectorSchemas.ActionSaveCoordinate && f.OpLabel == null && f.LabelKey == "inspector.charaEdit.clothes.saveCoordinate"),
                "clothes: the coordinate section gains «保存…» (ops.SaveCoordinate)");
        }

        static void Resolve(TestRunner t)
        {
            var s = new SceneSession(new SessionOptions { Ids = new CountingIdSource(98) });
            var sub = new CharaSubtree();
            sub.Chara.Profile.Sex = 1;
            sub.Chara.Profile.Hs2 = new Hs2Profile { Parameter2 = new Hs2Parameter2() };
            var r = s.AddChara(new CharaAppearance { Stored = sub });
            string id = r.NewIds[0];
            int n = 0, fails = 0;
            foreach (var f in NodeSections("character").SelectMany(x => x.Fields).Where(f => f.Path.StartsWith("appearance.", StringComparison.Ordinal)))
            {
                n++;
                try
                {
                    var leaf = FieldAccessor.LeafType(s.Model, id, f.Path);
                    bool fits = f.Kind == FieldKind.Text ? leaf == typeof(string) || leaf == typeof(List<int>) : f.Kind == FieldKind.Int || f.Kind == FieldKind.Enum ? leaf == typeof(int) :
                        f.Kind == FieldKind.Float ? leaf == typeof(float) : f.Kind == FieldKind.Bool && leaf == typeof(bool);
                    if (!fits) { fails++; t.Check(false, "character: " + f.Path + " is a " + leaf.Name + ", declared " + f.Kind); }
                }
                catch (CommandException e) { fails++; t.Check(false, "character: " + f.Path + " does not resolve: " + e.Code + " " + e.Message); }
            }
            t.Check(fails == 0 && n == 12, "character: the 12 document rows resolve with their declared leaf types (" + n + ")");
            t.Check(FieldAccessor.IsWritable(s.Model, id, "appearance.chara.profile.name", out _), "character: the name is writable");
            t.Check(!FieldAccessor.IsWritable(s.Model, id, "appearance.chara.profile.sex", out _) && !FieldAccessor.IsWritable(s.Model, id, "appearance.chara.profile.hs2.personality", out _),
                "character: the sex and the game parameters are not writable by a set");
        }

        static void Labels(TestRunner t)
        {
            L10nTable tb;
            try { tb = UiKit.Strings(t); }
            catch (Exception e) { t.Check(false, "character / file labels: strings.tsv loads: " + e.Message); return; }
            var keys = new List<string>();
            foreach (var sec in NodeSections("character").Concat(NodeSections("file")))
            {
                keys.Add(sec.LabelKey);
                foreach (var f in sec.Fields)
                {
                    keys.Add(f.LabelKey);
                    if (f.TooltipKey != null) keys.Add(f.TooltipKey);
                    if (f.Choices != null && f.ChoiceLabelPrefix != null) keys.AddRange(f.Choices.Select(c => f.ChoiceLabelPrefix + c));
                }
            }
            keys.Add("inspector.charaEdit.page.character.character");
            keys.Add("inspector.charaEdit.page.file.file");
            keys.Add("inspector.charaEdit.clothes.saveCoordinate");
            foreach (var lang in new[] { "en", "ja", "zh-Hans" })
            {
                var missing = keys.Where(k => string.IsNullOrEmpty(tb.Cell(k, lang))).Distinct().ToList();
                t.Check(missing.Count == 0, "character / file labels: every key has " + lang + " (" + string.Join(", ", missing.Take(10)) + ")");
            }
        }
    }
}
