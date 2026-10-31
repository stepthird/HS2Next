// The Edit tab tables, Face › Shape (InspectorSchemas.CharaEdit): every shape row
// resolves on a character with stored documents (FieldAccessor's appearance root) as a float; Face › Shape holds the 59 face
// sliders once each, in index order, on HS2's seven sections; Q23: slider 0..1, typed −1..2, display scale 100, default 0.5,
// label "Edit face"; FieldSpec.Scale maps display units both ways and never clamps a stored value; every label key (tree,
// sections, rows) has en. The other face pages (4.1): SchemaTests_CharaEditFace.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HS2Next.UI.Core.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;
    using HS2Next.Scenes;
    using HS2Next.UI.Core;

    public static class SchemaTests_CharaEdit
    {
        public static void Run(TestRunner t)
        {
            Paths(t);
            Rows(t);
            ScaleMath(t);
            Labels(t);
        }

        // The Face › Shape sections (4.0); the 4.1 face pages are SchemaTests_CharaEditFace's, the 4.2 Body › Shape page
        // SchemaTests_CharaEditBody's.
        static IEnumerable<SchemaSection> ShapeSections => InspectorSchemas.CharaEdit.Where(x => InspectorSchemas.CharaEditTreeOf(x) == "face" && InspectorSchemas.CharaEditPageOf(x) == "shape");

        static void Paths(TestRunner t)
        {
            var s = new SceneSession(new SessionOptions { Ids = new CountingIdSource(11) });
            var r = s.AddChara(new CharaAppearance { Stored = new CharaSubtree() });
            t.Check(r.Status == CommandStatus.Ok, "charaEdit: a character with stored documents for the walk");
            string id = r.NewIds[0];
            int fails = 0, n = 0;
            foreach (var f in ShapeSections.SelectMany(x => x.Fields))
            {
                n++;
                try
                {
                    var leaf = FieldAccessor.LeafType(s.Model, id, f.Path);
                    if (f.Kind == FieldKind.Float && leaf != typeof(float)) { fails++; t.Check(false, "charaEdit: " + f.Path + " is a " + leaf.Name); }
                    if (!FieldAccessor.IsWritable(s.Model, id, f.Path, out string code)) { fails++; t.Check(false, "charaEdit: " + f.Path + " is not writable (" + code + ")"); }
                }
                catch (CommandException e) { fails++; t.Check(false, "charaEdit: " + f.Path + " does not resolve: " + e.Code + " " + e.Message); }
            }
            t.Check(fails == 0 && n > 0, "charaEdit: " + n + " rows resolve on the character documents as writable floats");
            // a stored value outside 0..1 through a row: set as typed (the schema's typed clamp), stored unclamped by the bus
            var row = InspectorSchemas.Find(InspectorSchemas.CharaEdit, InspectorSchemas.CharaEditPath("face.shape[12]"));
            var v = (float)row.ParseTyped(150);
            t.Check(s.Set(id, row.Path, v).Status == CommandStatus.Ok && (float)FieldAccessor.Get(s.Model, id, row.Path) == 1.5f, "charaEdit: typed 150 stores 1.5");
        }

        static void Rows(TestRunner t)
        {
            var secs = ShapeSections.ToList();
            var ids = secs.Select(x => x.Id).ToArray();
            t.Check(ids.SequenceEqual(InspectorSchemas.FaceShapePages.Select(p => "face.shape." + p.page)), "charaEdit: the Face › Shape pages in HS2's order (" + string.Join(", ", ids) + ")");
            t.Check(secs.All(x => InspectorSchemas.CharaEditTree.Contains(InspectorSchemas.CharaEditTreeOf(x))), "charaEdit: every section belongs to a tree node");
            var rows = secs.SelectMany(x => x.Fields).ToList();
            t.Equal(59, rows.Count, "charaEdit: 59 face sliders");
            t.Check(rows.Select(f => f.Path).SequenceEqual(Enumerable.Range(0, 59).Select(i => "appearance.chara.face.shape[" + i + "]")), "charaEdit: index order, each index once");
            t.Equal(59, InspectorSchemas.FaceShapeNames.Distinct().Count(), "charaEdit: 59 distinct slider names");
            int bad = rows.Count(f => f.Kind != FieldKind.Float || f.Min != 0 || f.Max != 1 || f.TypedMin != -1 || f.TypedMax != 2 || f.Scale != 100 ||
                                      !Equals(f.Default, 0.5) || f.OpLabel != OpLabels.EditFace || f.Applies != Applies.All || f.Gate != null);
            t.Check(bad == 0, "charaEdit: every slider has Q23's ranges (0..1, typed −1..2, ×100), default 0.5, label Edit face, all selected (" + bad + " differ)");
            t.Check(OpLabels.All.Contains(OpLabels.EditFace), "charaEdit: Edit face is an OpLabels constant");
            t.Check(rows.Select(f => f.Id).Distinct().Count() == rows.Count, "charaEdit: row ids are distinct");
            var pageOf = secs.SelectMany(x => x.Fields.Select(f => (x.Id, f.Path))).ToDictionary(p => p.Path, p => p.Id);
            t.Equal("face.shape.eyes", pageOf["appearance.chara.face.shape[31]"], "charaEdit: eyelid shape 2 is on the eyes page");
            t.Equal("face.shape.ear", pageOf["appearance.chara.face.shape[54]"], "charaEdit: ear size opens the ear page");
        }

        static void ScaleMath(TestRunner t)
        {
            var f = ShapeSections.First().Fields[0];
            t.Equal(50.0, f.ToDisplay(0.5), "scale: 0.5 shows 50");
            t.Equal(150.0, f.ToDisplay(1.5), "scale: a stored 1.5 shows 150 (not clamped)");
            t.Equal(-100.0, f.ToDisplay(-1), "scale: −1 shows −100");
            t.Equal(1.5, f.ParseTyped(150), "scale: typed 150 → 1.5");
            t.Equal(2.0, f.ParseTyped(250), "scale: typed 250 → 2 (typed clamp)");
            t.Equal(-1.0, f.ParseTyped(-180), "scale: typed −180 → −1");
            t.Equal(1.0, f.KnobOf(1.5), "scale: the knob of 1.5 sits at the end (1)");
            t.Equal(0.0, f.KnobOf(-0.4), "scale: the knob of −0.4 sits at 0");
            t.Equal(1.0, f.ClampSlider(1.3), "scale: a drag clamps to 0..1");
            var plain = new FieldSpec { Kind = FieldKind.Float, Min = 0, Max = 1 };
            t.Check(plain.Scale == 1 && plain.ToDisplay(0.25) == 0.25 && plain.FromDisplay(0.25) == 0.25, "scale: the default scale 1 shows values as stored");
        }

        static void Labels(TestRunner t)
        {
            L10nTable tb;
            try { tb = UiKit.Strings(t); }
            catch (Exception e) { t.Check(false, "charaEdit labels: strings.tsv loads: " + e.Message); return; }
            var missing = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var k in InspectorSchemas.CharaEditTree.Select(x => "inspector.charaEdit.tree." + x)) if (tb.Cell(k, "en") == null) missing.Add(k);
            foreach (var sec in ShapeSections)
            {
                if (tb.Cell(sec.LabelKey, "en") == null) missing.Add(sec.LabelKey);
                foreach (var f in sec.Fields) if (tb.Cell(f.LabelKey, "en") == null) missing.Add(f.LabelKey);
            }
            t.Check(missing.Count == 0, "charaEdit labels: every key has en (" + string.Join(", ", missing.Take(20)) + ")");
            int ja = ShapeSections.SelectMany(x => x.Fields).Count(f => !string.IsNullOrEmpty(tb.Cell(f.LabelKey, "ja")));
            t.Equal(59, ja, "charaEdit labels: the 59 sliders have HS2's Japanese names");
        }
    }
}
