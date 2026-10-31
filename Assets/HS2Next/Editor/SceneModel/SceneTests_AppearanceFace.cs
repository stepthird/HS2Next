// The Maker ops of the face pages (the
// Unity-free cores in MakerEdits, which StudioOps.Maker wraps): layout pushes of the 8 mole and 6 face-paint rows by id
// (the fixture) write (S, S, X, Y) with rotation and layoutItem unchanged, other paths bad_args, unknown rows not_found,
// unsupported without lists; the missing-preset fallback (head change + skin snap + edit.presetMissing, one entry; an
// unlisted head not_found; skipped on multi-selections) and the StudioOps decision between preset and fallback; the row
// reset per node (reference values, the passed default without a reference, component rows, linked eyes, unclamped, bad
// paths); the eye copy buttons (whole-eye sets, link on and off); EditReferences (first capture wins, card-backed nodes
// through the importer, copies, forgotten on node removal but not on a cancelled one, cleared on scene replacement and on
// another session); undo / redo of each restores the documents byte for byte.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_AppearanceFace
    {
        public static void Run(TestRunner t)
        {
            LayoutPushes(t);
            PresetFallback(t);
            Reset(t);
            CopyEyes(t);
            LinkedSizes(t);
            References(t);
        }

        static string P(string m) => AppearancePaths.Chara(m);

        static MakerEditOptions Opts(SceneSession s, FakeMakerData data = null, bool multi = false) =>
            new MakerEditOptions { Rows = data ?? new FakeMakerData(), Defaults = data ?? new FakeMakerData(), NewId = s.NewId, SkipUnlistedRows = multi };

        /// <summary>One BeginOrJoin around <paramref name="body"/>, committed on success (StudioOps.Maker's pattern).</summary>
        static CommandResult Tx(SceneSession s, string label, Func<Transaction, CommandResult> body)
        {
            using (var tx = s.Commands.BeginOrJoin(label))
            {
                var r = body(tx);
                if (r == null || r.Status == CommandStatus.Error) return r;
                var c = tx.Commit();
                return c.Status == CommandStatus.Error ? c : r;
            }
        }

        static string Bytes(SceneSession s, string id) => Kit.Hash(SceneTests_Appearance.CharaBytes(s, id)) + "/" + Kit.Hash(SceneTests_Appearance.CoordBytes(s, id));

        static SceneSession TwoCharas(TestRunner t, out string female, out string male)
        {
            var s = SceneTests_Appearance.GoldenChara(t, out female);
            var m = Kit.GoldenContainer(t, true).Charas[Kit.GoldenChara].Clone();
            m.Chara.Profile.Sex = 0;
            m.Coord.Sex = 0;
            male = s.AddChara(new CharaAppearance { Stored = m }).NewIds[0];
            s.Commands.ClearHistory();
            return s;
        }

        // ---------------------------------------------------------------- layout pushes (F18)

        static void LayoutPushes(TestRunner t)
        {
            var s = SceneTests_Appearance.GoldenChara(t, out string id);
            s.Commands.ClearHistory();
            var node = s.Model.Find(id);
            var face = node.Appearance.Stored.Chara.Face;
            var data = new FakeMakerData();
            string start = Bytes(s, id);
            int ok = 0, n = 0;
            for (int i = 0; i < FakeMakerData.MoleLayouts.Length; i++)
            {
                n++;
                var (sc, x, y) = FakeMakerData.MoleLayouts[i];
                var r = Tx(s, "Edit face", tx => MakerEdits.ApplyLayoutPreset(tx, s.Model, node, P("face.mole.layout"), "illusion.hs2#" + i, Opts(s, data), new MakerEditReport()));
                if (r.Ok && face.Mole.Layout.Equals(new Vec4(sc, sc, x, y))) ok++;
                else t.Check(false, "layout: mole row " + i + " → " + r + " " + FieldJson.ToText(face.Mole.Layout));
            }
            for (int k = 0; k < 2; k++)
            {
                var paint = face.Makeup.Paint[k];
                var other = face.Makeup.Paint[1 - k];
                for (int i = 0; i < FakeMakerData.PaintLayouts.Length; i++)
                {
                    n++;
                    var (sc, x, y) = FakeMakerData.PaintLayouts[i];
                    float rot = paint.Rotation;
                    string item = paint.LayoutItem;
                    var otherLayout = other.Layout;
                    int kk = k;
                    // the relative form (bridge) is accepted too
                    var r = Tx(s, "Edit face", tx => MakerEdits.ApplyLayoutPreset(tx, s.Model, node, "chara.face.makeup.paint[" + kk + "].layout", "illusion.hs2#" + i, Opts(s, data), new MakerEditReport()));
                    if (r.Ok && paint.Layout.Equals(new Vec4(sc, sc, x, y)) && paint.Rotation == rot && paint.LayoutItem == item && other.Layout.Equals(otherLayout)) ok++;
                    else t.Check(false, "layout: paint " + k + " row " + i + " → " + r + " " + FieldJson.ToText(paint));
                }
            }
            t.Check(ok == n && n == 20, "layout: the 8 mole and 2 × 6 paint rows write (S, S, X, Y); rotation, layoutItem and the other paint unchanged (" + ok + " / " + n + ")");
            t.Equal(20, s.Commands.UndoList.Count, "layout: one entry per push");
            t.Equal("Edit face", s.Commands.UndoList[0].Label, "layout: label Edit face");
            // the UI4 values (9)
            Tx(s, "Edit face", tx => MakerEdits.ApplyLayoutPreset(tx, s.Model, node, P("face.mole.layout"), "illusion.hs2#4", Opts(s, data), null));
            Tx(s, "Edit face", tx => MakerEdits.ApplyLayoutPreset(tx, s.Model, node, P("face.makeup.paint[0].layout"), "illusion.hs2#0", Opts(s, data), null));
            t.Check(face.Mole.Layout.Equals(new Vec4(0.33f, 0.33f, 0.75f, 0.52f)) && face.Makeup.Paint[0].Layout.Equals(new Vec4(0.5f, 0.5f, 0.76f, 0.35f)),
                "layout: «左の目元» → (0.33, 0.33, 0.75, 0.52), «左頬» → (0.5, 0.5, 0.76, 0.35)");
            string end = Bytes(s, id);
            int depth = s.Commands.UndoList.Count;
            // refused pushes change nothing
            foreach (var bad in new[] { P("face.eyebrow.layout"), P("body.paint[0].layout"), P("face.makeup.paint[2].layout"), P("face.mole"), "face.mole.layout", null })
            {
                var r = Tx(s, "Edit face", tx => MakerEdits.ApplyLayoutPreset(tx, s.Model, node, bad, "illusion.hs2#0", Opts(s, data), null));
                t.Check(r.Status == CommandStatus.Error && r.Code == CommandCodes.BadArgs, "layout: path " + (bad ?? "null") + " → bad_args (" + r + ")");
            }
            foreach (var (path, row) in new[] { (P("face.mole.layout"), "illusion.hs2#8"), (P("face.makeup.paint[1].layout"), "illusion.hs2#6"), (P("face.mole.layout"), "fuzz.pool#600"), (P("face.mole.layout"), "nonsense") })
            {
                var r = Tx(s, "Edit face", tx => MakerEdits.ApplyLayoutPreset(tx, s.Model, node, path, row, Opts(s, data), null));
                t.Check(r.Status == CommandStatus.Error && r.Code == CommandCodes.NotFound, "layout: row " + row + " of " + path + " → not_found (" + r + ")");
            }
            var nl = Tx(s, "Edit face", tx => MakerEdits.ApplyLayoutPreset(tx, s.Model, node, P("face.mole.layout"), "illusion.hs2#0", new MakerEditOptions(), null));
            t.Check(nl.Status == CommandStatus.Error && nl.Code == CommandCodes.Unsupported, "layout: without lists → unsupported");
            t.Check(depth == s.Commands.UndoList.Count && Bytes(s, id) == end, "layout: refused pushes leave no entry and no change");
            t.Check(MakerEdits.LayoutCategoryOf(P("face.mole.layout")) == 6 && MakerEdits.LayoutCategoryOf("chara.face.makeup.paint[1].layout") == 7 &&
                    MakerEdits.LayoutCategoryOf(P("body.paint[0].layout")) == -1 && MakerEdits.LayoutCategoryOf(null) == -1, "layout: LayoutCategoryOf");
            while (s.Commands.CanUndo) s.Commands.Undo();
            t.Equal(start, Bytes(s, id), "layout: undo all restores the documents byte for byte");
            while (s.Commands.CanRedo) s.Commands.Redo();
            t.Equal(end, Bytes(s, id), "layout: redo all");
        }

        // ---------------------------------------------------------------- preset fallback (GF4, F5)

        sealed class FakePresets : IFacePresets
        {
            public readonly HashSet<string> Missing = new HashSet<string>(StringComparer.Ordinal);
            public FaceData Template;
            public bool TryLoad(int sex, string headRef, out FaceData face, IList<string> warnings)
            {
                face = null;
                if (Missing.Contains(headRef) || headRef == null || !headRef.StartsWith("fuzz.pool#", StringComparison.Ordinal)) return false;
                face = DocClone.Clone(Template);
                face.Head = headRef;
                return true;
            }
        }

        // StudioOps.ApplyFacePreset's step: the preset when it loads, else the head change.
        static CommandResult Push(Transaction tx, SceneSession s, TreeNode node, IFacePresets presets, string head, MakerEditOptions o, MakerEditReport rep)
        {
            var chara = node.Appearance.Stored.Chara;
            if (!presets.TryLoad(chara.Profile.Sex, head, out var face, rep.Warnings) || face == null)
                return MakerEdits.FacePresetFallback(tx, s.Model, node, head, o, rep);
            return MakerEdits.ApplyFacePreset(tx, s.Model, node, face, o, rep);
        }

        static void PresetFallback(TestRunner t)
        {
            var s = SceneTests_Appearance.GoldenChara(t, out string id);
            s.Commands.ClearHistory();
            var node = s.Model.Find(id);
            var st = node.Appearance.Stored;
            var data = new FakeMakerData();
            string start = Bytes(s, id);
            var before = DocClone.Clone(st.Chara.Face);
            string head = Enumerable.Range(0, FakeMakerData.PoolSize).Select(i => FakeMakerData.Row(210, i))
                .First(h => data.SkinsForHead(1, h).Count > 0 && !data.SkinsForHead(1, h).Contains(st.Chara.Face.Skin));
            var rep = new MakerEditReport();
            var r = Tx(s, "Face preset", tx => MakerEdits.FacePresetFallback(tx, s.Model, node, head, Opts(s, data), rep));
            Kit.Ok(t, r, "fallback: a listed head without a preset asset");
            t.Check(st.Chara.Face.Head == head && st.Chara.Face.Skin == data.SkinsForHead(1, head)[0], "fallback: the head changes and the skin snaps (HS2's push)");
            t.Check(rep.Warnings.Count(w => w.StartsWith("edit.presetMissing: head " + head, StringComparison.Ordinal)) == 1, "fallback: edit.presetMissing (" + string.Join("; ", rep.Warnings) + ")");
            t.Check(rep.Consequences.SequenceEqual(new[] { P("face.skin") }), "fallback: the skin snap is a consequence");
            var after = DocClone.Clone(st.Chara.Face);
            after.Head = before.Head;
            after.Skin = before.Skin;
            t.Equal(FieldJson.ToText(before), FieldJson.ToText(after), "fallback: every other face member is kept");
            t.Check(s.Commands.UndoList.Count == 1 && s.Commands.UndoList[0].Label == "Face preset", "fallback: one entry \"Face preset\"");
            string pushed = Bytes(s, id);
            // an unlisted head: not_found, nothing changes; multi-selection: skipped with edit.rowSkipped
            var nf = Tx(s, "Face preset", tx => MakerEdits.FacePresetFallback(tx, s.Model, node, "illusion.hs2#80", Opts(s, data), new MakerEditReport()));
            t.Check(nf.Status == CommandStatus.Error && nf.Code == CommandCodes.NotFound && Bytes(s, id) == pushed && s.Commands.UndoList.Count == 1, "fallback: an unlisted head → not_found, no change");
            rep = new MakerEditReport();
            var sk = Tx(s, "Face preset", tx => MakerEdits.FacePresetFallback(tx, s.Model, node, FakeMakerData.Row(110, 3).Replace("#11", "#99"), Opts(s, data, true), rep));
            t.Check(sk.Status == CommandStatus.NoOp && rep.Warnings.Any(w => w.StartsWith("edit.rowSkipped", StringComparison.Ordinal)) && s.Commands.UndoList.Count == 1,
                "fallback: multi-selection → the unlisted head is skipped (edit.rowSkipped), no entry");
            // the current head (null head = the current one in StudioOps): no change, the warning still says why
            rep = new MakerEditReport();
            var same = Tx(s, "Face preset", tx => MakerEdits.FacePresetFallback(tx, s.Model, node, head, Opts(s, data), rep));
            t.Check(same.Status == CommandStatus.NoOp && rep.Warnings.Any(w => w.StartsWith("edit.presetMissing", StringComparison.Ordinal)) && s.Commands.UndoList.Count == 1,
                "fallback: the current head → no change, edit.presetMissing");
            // StudioOps' decision: a loadable preset replaces the face, a missing one changes the head
            var presets = new FakePresets { Template = DocClone.Clone(before) };
            presets.Template.Shape[0] = 0.125f;
            string h2 = Enumerable.Range(0, FakeMakerData.PoolSize).Select(i => FakeMakerData.Row(210, i)).First(h => h != head);
            string h3 = Enumerable.Range(0, FakeMakerData.PoolSize).Select(i => FakeMakerData.Row(210, i)).First(h => h != head && h != h2);
            presets.Missing.Add(h3);
            rep = new MakerEditReport();
            Kit.Ok(t, Tx(s, "Face preset", tx => Push(tx, s, node, presets, h2, Opts(s, data), rep)), "preset push of a head with a preset");
            t.Check(st.Chara.Face.Head == h2 && st.Chara.Face.Shape[0] == 0.125f && !rep.Warnings.Any(w => w.StartsWith("edit.presetMissing", StringComparison.Ordinal)), "push: the preset's face block");
            rep = new MakerEditReport();
            Kit.Ok(t, Tx(s, "Face preset", tx => Push(tx, s, node, presets, h3, Opts(s, data), rep)), "preset push of a head without a preset asset");
            t.Check(st.Chara.Face.Head == h3 && st.Chara.Face.Shape[0] == 0.125f && rep.Warnings.Any(w => w.StartsWith("edit.presetMissing", StringComparison.Ordinal)), "push: the head change only");
            var bad = Tx(s, "Face preset", tx => Push(tx, s, node, presets, "illusion.hs2#80", Opts(s, data), new MakerEditReport()));
            t.Check(bad.Status == CommandStatus.Error && bad.Code == CommandCodes.NotFound, "push: an unlisted head → not_found");
            t.Equal(3, s.Commands.UndoList.Count, "push: one entry per push");
            // without lists: the head is set as given, both warnings
            rep = new MakerEditReport();
            Kit.Ok(t, Tx(s, "Face preset", tx => MakerEdits.FacePresetFallback(tx, s.Model, node, "illusion.hs2#80", new MakerEditOptions(), rep)), "fallback without lists");
            t.Check(st.Chara.Face.Head == "illusion.hs2#80" && rep.Warnings.Any(w => w.StartsWith("edit.listsUnavailable", StringComparison.Ordinal)) &&
                    rep.Warnings.Any(w => w.StartsWith("edit.presetMissing", StringComparison.Ordinal)), "fallback without lists: head as given, edit.listsUnavailable + edit.presetMissing");
            string end = Bytes(s, id);
            while (s.Commands.CanUndo) s.Commands.Undo();
            t.Equal(start, Bytes(s, id), "fallback: undo all restores the documents byte for byte");
            while (s.Commands.CanRedo) s.Commands.Redo();
            t.Equal(end, Bytes(s, id), "fallback: redo all");
        }

        // ---------------------------------------------------------------- row reset (F1)

        static void Reset(TestRunner t)
        {
            var s = TwoCharas(t, out string a, out string b);
            var na = s.Model.Find(a);
            var nb = s.Model.Find(b);
            var fa = na.Appearance.Stored.Chara.Face;
            var fb = nb.Appearance.Stored.Chara.Face;
            var golden = DocClone.Clone(fa);
            var refs = new EditReferences();
            t.Check(refs.CaptureNode(s, na) && refs.Has(a) && !refs.Has(b) && refs.Count == 1, "reset: the first session captures A's reference");
            var data = new FakeMakerData();
            // edit both characters the same way
            var edits = new List<(string, object)>
            {
                (P("face.eyebrow.tilt"), 0.9f), (P("face.eyebrow.color"), new Color4(0.7f, 0.1f, 0.2f, 0.5f)), (P("face.eyebrow.layout"), new Vec4(0.11f, 0.22f, 0.33f, 0.44f)),
                (P("face.eyes.linked"), false), (P("face.shape[3]"), 1.75f), (P("face.makeup.lip.gloss"), -0.5f),
            };
            foreach (var id in new[] { a, b })
                Kit.Ok(t, Tx(s, "Edit face", tx => MakerEdits.Edit(tx, s.Model, s.Model.Find(id), edits, Opts(s, data), null)), "reset: edit " + id.Substring(0, 6));
            string editedA = Bytes(s, a), editedB = Bytes(s, b);
            int depth = s.Commands.UndoList.Count;
            var paths = new[] { P("face.eyebrow.tilt"), P("face.eyebrow.color"), P("face.eyebrow.layout") + "#2", "chara.face.eyes.linked", P("face.shape[3]"), P("face.makeup.lip.gloss") };
            var defaults = new object[] { 0.5, new[] { 0.05, 0.05, 0.05, 1.0 }, 0.666, true, 0.5, 0.0 };
            var rep = new MakerEditReport();
            int fromRefA = 0;
            var r = Tx(s, "Reset value", tx =>
            {
                fromRefA = MakerEdits.ResetSets(s.Model, na, refs, paths, defaults, null, out int fr).Count == paths.Length ? fr : -1;
                var ra = MakerEdits.ResetAppearance(tx, s.Model, na, refs, paths, defaults, Opts(s, data, true), rep);
                if (ra.Status == CommandStatus.Error) return ra;
                return MakerEdits.ResetAppearance(tx, s.Model, nb, refs, paths, defaults, Opts(s, data, true), rep);
            });
            Kit.Ok(t, r, "reset: both characters in one call");
            t.Equal(6, fromRefA, "reset: A's six values come from its reference");
            t.Check(fa.Eyebrow.Tilt == golden.Eyebrow.Tilt && fa.Eyebrow.Color.Equals(golden.Eyebrow.Color) && fa.Eyes.Linked == golden.Eyes.Linked &&
                    fa.Shape[3] == golden.Shape[3] && fa.Makeup.Lip.Gloss == golden.Makeup.Lip.Gloss, "reset: A takes its reference values (the documents when its session opened)");
            t.Check(fa.Eyebrow.Layout.Equals(new Vec4(0.11f, 0.22f, golden.Eyebrow.Layout.Z, 0.44f)), "reset: A's component row resets only layout.z (" + FieldJson.ToText(fa.Eyebrow.Layout) + ")");
            t.Check(fb.Eyebrow.Tilt == 0.5f && fb.Eyebrow.Color.Equals(new Color4(0.05f, 0.05f, 0.05f, 1f)) && fb.Eyes.Linked && fb.Shape[3] == 0.5f && fb.Makeup.Lip.Gloss == 0f,
                "reset: B (no reference) takes the passed defaults");
            t.Check(fb.Eyebrow.Layout.Equals(new Vec4(0.11f, 0.22f, 0.666f, 0.44f)), "reset: B's component row takes the scalar default for z only");
            t.Check(s.Commands.UndoList.Count == depth + 1 && s.Commands.UndoList[0].Label == "Reset value", "reset: one entry \"Reset value\"");
            string resetA = Bytes(s, a), resetB = Bytes(s, b);
            s.Commands.Undo();
            t.Check(Bytes(s, a) == editedA && Bytes(s, b) == editedB, "reset: undo restores both characters byte for byte");
            s.Commands.Redo();
            t.Check(Bytes(s, a) == resetA && Bytes(s, b) == resetB, "reset: redo");
            // neither a reference nor a default: skipped with a warning
            rep = new MakerEditReport();
            var none = Tx(s, "Reset value", tx => MakerEdits.ResetAppearance(tx, s.Model, nb, refs, new[] { P("face.eyebrow.tilt") }, null, Opts(s, data), rep));
            t.Check(none.Status == CommandStatus.NoOp && rep.Warnings.Any(w => w.StartsWith("edit.resetUnavailable", StringComparison.Ordinal)), "reset: no reference, no default → skipped (edit.resetUnavailable)");
            // bad paths
            foreach (var bp in new[] { P("face.nope"), P("face.eyes.left.iris.size") + "#2", "transform.pos", P("face.eyebrow.tilt") + "#0" })
            {
                var e = Tx(s, "Reset value", tx => MakerEdits.ResetAppearance(tx, s.Model, na, refs, new[] { bp }, new object[] { 0.5 }, Opts(s, data), null));
                t.Check(e.Status == CommandStatus.Error && (e.Code == CommandCodes.BadArgs || (bp == "transform.pos" && e.Code == CommandCodes.Unsupported)),
                    "reset: " + bp + " → refused (" + e + ")");
            }
            // linked eyes: the reset of one eye's member resets both (the planner's consequence); values are not clamped
            Kit.Ok(t, Tx(s, "Edit face", tx => MakerEdits.Edit(tx, s.Model, na, new List<(string, object)> { (P("face.eyes.linked"), true), (P("face.eyes.left.iris.emission"), 0.3f), (P("face.eyes.right.iris.emission"), 0.4f) }, Opts(s, data), null)), "reset: eyes edited");
            Kit.Ok(t, Tx(s, "Reset value", tx => MakerEdits.ResetAppearance(tx, s.Model, na, refs, new[] { P("face.eyes.left.iris.emission") }, null, Opts(s, data), null)), "reset: one eye's emission, linked");
            t.Check(fa.Eyes.Left.Iris.Emission == golden.Eyes.Left.Iris.Emission && fa.Eyes.Right.Iris.Emission == golden.Eyes.Left.Iris.Emission, "reset: linked eyes reset both to the reference's left value");
            var s2 = SceneTests_Appearance.GoldenChara(t, out string c);
            var nc = s2.Model.Find(c);
            Kit.Ok(t, Tx(s2, "Edit face", tx => MakerEdits.Edit(tx, s2.Model, nc, new List<(string, object)> { (P("face.makeup.lip.gloss"), 1.5f) }, Opts(s2, data), null)), "reset: lip gloss 1.5");
            var refs2 = new EditReferences();
            refs2.CaptureNode(s2, nc);
            Kit.Ok(t, Tx(s2, "Edit face", tx => MakerEdits.Edit(tx, s2.Model, nc, new List<(string, object)> { (P("face.makeup.lip.gloss"), 0.2f) }, Opts(s2, data), null)), "reset: lip gloss 0.2");
            Kit.Ok(t, Tx(s2, "Reset value", tx => MakerEdits.ResetAppearance(tx, s2.Model, nc, refs2, new[] { "chara.face.makeup.lip.gloss" }, new object[] { 0.0 }, Opts(s2, data), null)), "reset: lip gloss");
            t.Equal(1.5f, nc.Appearance.Stored.Chara.Face.Makeup.Lip.Gloss, "reset: a reference value outside 0..1 is restored unclamped");
            // conversions of UI defaults
            t.Check(MakerEdits.ValueOf(typeof(float), 0.25) is float f1 && f1 == 0.25f && MakerEdits.ValueOf(typeof(Color4), new[] { 1.0, 0.5, 0.25, 1.0 }) is Color4 c4 && c4.G == 0.5f &&
                    MakerEdits.ValueOf(typeof(Vec2), new[] { 0.1, 0.2 }) is Vec2 v2 && v2.Y == 0.2f && MakerEdits.ValueOf(typeof(Color4), 0.5) == null && MakerEdits.ValueOf(typeof(bool), true) is bool bb && bb,
                "reset: ValueOf converts numbers, double[] colours and vectors, keeps bools, refuses mismatches");
            t.Check(MakerEdits.SplitComponent("appearance.chara.face.mole.layout#3", out int cc) == "appearance.chara.face.mole.layout" && cc == 3 &&
                    MakerEdits.SplitComponent("appearance.coord.accessories[\"a#b\"].parent", out int c2) == "appearance.coord.accessories[\"a#b\"].parent" && c2 == -1,
                "reset: SplitComponent");
        }

        // ---------------------------------------------------------------- eye copies (F10)

        static void CopyEyes(TestRunner t)
        {
            var s = SceneTests_Appearance.GoldenChara(t, out string id);
            s.Commands.ClearHistory();
            var node = s.Model.Find(id);
            var eyes = node.Appearance.Stored.Chara.Face.Eyes;
            var data = new FakeMakerData();
            string start = Bytes(s, id);
            Kit.Ok(t, Tx(s, "Edit face", tx => MakerEdits.Edit(tx, s.Model, node, new List<(string, object)>
            {
                (P("face.eyes.linked"), false), (P("face.eyes.right.white"), new Color4(0.9f, 0.8f, 0.7f, 1f)), (P("face.eyes.right.iris.size"), new Vec2(0.2f, 1.4f)),
                (P("face.eyes.right.pupil.item"), FakeMakerData.Row(318, 5)), (P("face.eyes.left.iris.emission"), 0.25f),
            }, Opts(s, data), null)), "copy: eyes made different, link off");
            var left0 = FieldJson.ToText(eyes.Left);
            var highlight = FieldJson.ToText(eyes.Highlight);
            float off = eyes.OffsetY, sh = eyes.ShadowScale;
            var rep = new MakerEditReport();
            Kit.Ok(t, Tx(s, "Edit face", tx => MakerEdits.CopyEye(tx, s.Model, node, false, Opts(s, data), rep)), "copy right → left");
            t.Check(FieldJson.ToText(eyes.Left) == FieldJson.ToText(eyes.Right) && FieldJson.ToText(eyes.Left) != left0, "copy: left = right (the whole eye)");
            t.Check(FieldJson.ToText(eyes.Highlight) == highlight && eyes.OffsetY == off && eyes.ShadowScale == sh && !eyes.Linked, "copy: highlight, offset, shadow and the link untouched");
            t.Check(rep.Changed.SequenceEqual(new[] { P("face.eyes.left") }) && rep.Consequences.Count == 0, "copy: one whole-eye set, no consequence with the link off");
            t.Check(!ReferenceEquals(eyes.Left, eyes.Right), "copy: the eyes are separate objects");
            // link on: copy left → right; the consequence re-sets the source to its own value
            Kit.Ok(t, Tx(s, "Edit face", tx => MakerEdits.Edit(tx, s.Model, node, new List<(string, object)> { (P("face.eyes.linked"), true), (P("face.eyes.left.iris.color"), new Color4(0.1f, 0.6f, 0.2f, 1f)) }, Opts(s, data), null)), "copy: link on, left iris colour (linked → both)");
            Kit.Ok(t, Tx(s, "Edit face", tx => MakerEdits.Edit(tx, s.Model, node, new List<(string, object)> { (P("face.eyes.linked"), false), (P("face.eyes.left.pupil.color"), new Color4(0.3f, 0.3f, 0.9f, 1f)), (P("face.eyes.linked"), true) }, Opts(s, data), null)), "copy: an unlinked difference, link on again");
            string leftNow = FieldJson.ToText(eyes.Left);
            rep = new MakerEditReport();
            Kit.Ok(t, Tx(s, "Edit face", tx => MakerEdits.CopyEye(tx, s.Model, node, true, Opts(s, data), rep)), "copy left → right, linked");
            t.Check(FieldJson.ToText(eyes.Right) == leftNow && FieldJson.ToText(eyes.Left) == leftNow, "copy: right = left, the source unchanged");
            t.Check(rep.Consequences.SequenceEqual(new[] { P("face.eyes.left") }), "copy: with the link on the consequence re-sets the source (a no-op)");
            int entries = s.Commands.UndoList.Count;
            t.Equal(5, entries, "copy: one entry per edit and copy");
            string end = Bytes(s, id);
            while (s.Commands.CanUndo) s.Commands.Undo();
            t.Equal(start, Bytes(s, id), "copy: undo all restores the documents byte for byte");
            while (s.Commands.CanRedo) s.Commands.Redo();
            t.Equal(end, Bytes(s, id), "copy: redo all");
        }

        // ---------------------------------------------------------------- linked size components (H's FC7 note)

        static void LinkedSizes(TestRunner t)
        {
            var s = SceneTests_Appearance.GoldenChara(t, out string id);
            var node = s.Model.Find(id);
            var eyes = node.Appearance.Stored.Chara.Face.Eyes;
            var data = new FakeMakerData();
            Kit.Ok(t, Tx(s, "Edit face", tx => MakerEdits.Edit(tx, s.Model, node, new List<(string, object)>
            {
                (P("face.eyes.linked"), false), (P("face.eyes.left.pupil.size"), new Vec2(0.5f, 0.6f)), (P("face.eyes.right.pupil.size"), new Vec2(0.7f, 0.8f)), (P("face.eyes.linked"), true),
            }, Opts(s, data), null)), "sizes: linked eyes that differ");
            var rep = new MakerEditReport();
            // a component row writes the whole vector with one component changed (width)
            Kit.Ok(t, Tx(s, "Edit face", tx => MakerEdits.Edit(tx, s.Model, node, new List<(string, object)> { (P("face.eyes.left.pupil.size"), new Vec2(0.9f, 0.6f)) }, Opts(s, data), rep)), "sizes: left pupil width");
            t.Check(eyes.Left.Pupil.Size.Equals(new Vec2(0.9f, 0.6f)) && eyes.Right.Pupil.Size.Equals(new Vec2(0.9f, 0.8f)),
                "sizes: linked → only the changed component reaches the other eye (right keeps its height 0.8; " + FieldJson.ToText(eyes.Right.Pupil.Size) + ")");
            Kit.Ok(t, Tx(s, "Edit face", tx => MakerEdits.Edit(tx, s.Model, node, new List<(string, object)> { (P("face.eyes.right.iris.size"), new Vec2(0.3f, 1.4f)) }, Opts(s, data), null)), "sizes: right iris both components");
            t.Check(eyes.Left.Iris.Size.Equals(new Vec2(0.3f, 1.4f)), "sizes: both components changed → both copied");
            Kit.Ok(t, Tx(s, "Edit face", tx => MakerEdits.Edit(tx, s.Model, node, new List<(string, object)> { (P("face.eyes.left.pupil"), DocClone.Clone(eyes.Right.Pupil)) }, Opts(s, data), null)), "sizes: a whole pupil set");
            t.Check(FieldJson.ToText(eyes.Left.Pupil) == FieldJson.ToText(eyes.Right.Pupil), "sizes: whole-member sets still copy path for path");
        }

        // ---------------------------------------------------------------- EditReferences

        sealed class FakeImporter : ICharaImporter
        {
            public CharaSubtree Subtree;
            public CharaContainer Import(CardAppearanceSource source, FindingList findings) => new CharaContainer { Subtree = Subtree.Clone() };
        }

        static void References(TestRunner t)
        {
            var s = TwoCharas(t, out string a, out string b);
            var na = s.Model.Find(a);
            var refs = new EditReferences();
            var data = new FakeMakerData();
            float tilt0 = na.Appearance.Stored.Chara.Face.Eyebrow.Tilt;
            t.Check(refs.CaptureNode(s, na), "refs: captured");
            Kit.Ok(t, Tx(s, "Edit face", tx => MakerEdits.Edit(tx, s.Model, na, new List<(string, object)> { (P("face.eyebrow.tilt"), 0.95f) }, Opts(s, data), null)), "refs: edit after the capture");
            t.Check(!refs.CaptureNode(s, na), "refs: a second session keeps the first reference");
            t.Check(refs.TryGet(a, P("face.eyebrow.tilt"), out var v) && v is float ft && ft == tilt0, "refs: TryGet gives the value at capture time");
            t.Check(refs.TryGet(a, P("face.eyes.left"), out var e1) && e1 is Eye eye1, "refs: objects resolve");
            ((Eye)e1).Iris.Emission = 9f;
            t.Check(refs.TryGet(a, P("face.eyes.left"), out var e2) && ((Eye)e2).Iris.Emission != 9f, "refs: TryGet returns copies");
            var accs = na.Appearance.Stored.Coord.Accessories;
            if (accs.Count > 0)
                t.Check(refs.TryGet(a, AppearancePaths.Accessory(accs[0].Id, "parent"), out var par) && (string)par == accs[0].Parent, "refs: accessories[\"id\"] paths resolve");
            t.Check(refs.TryGet(a, P("hair.front.item"), out _) && !refs.TryGet(a, P("face.nope"), out _) && !refs.TryGet(b, P("face.eyebrow.tilt"), out _), "refs: hair parts resolve; bad paths and nodes without a reference do not");
            var docs = refs.DocumentsOf(a);
            docs.Chara.Face.Eyebrow.Tilt = 0.01f;
            t.Check(refs.TryGet(a, P("face.eyebrow.tilt"), out var v2) && (float)v2 == tilt0, "refs: DocumentsOf is a copy");
            // a removal inside a cancelled transaction keeps the reference; a committed removal forgets it; undo does not bring it back
            using (var tx = s.Commands.Begin("Delete"))
            {
                tx.Execute(new RemoveNodesCommand(new[] { a }));
                tx.Cancel();
            }
            t.Check(refs.Has(a), "refs: a cancelled removal keeps the reference");
            Kit.Ok(t, s.Delete(new[] { a }), "refs: delete A");
            t.Check(!refs.Has(a), "refs: a removed node loses its reference");
            s.Commands.Undo();
            t.Check(s.Model.Find(a) != null && !refs.Has(a), "refs: undoing the removal does not bring the reference back (the next session captures anew)");
            t.Check(refs.CaptureNode(s, s.Model.Find(a)) && refs.TryGet(a, P("face.eyebrow.tilt"), out var v3) && (float)v3 == 0.95f, "refs: a new session captures the current documents");
            // card-backed node: captured from the importer's result
            var saved = CharaImport.Importer;
            try
            {
                var card = Kit.Card("refcard", "UserData/chara/female/refcard.png", 0);
                string cid = s.AddChara(new CharaAppearance { Card = card }).NewIds[0];
                CharaImport.Importer = null;
                t.Check(!refs.CaptureNode(s, s.Model.Find(cid)), "refs: a card-backed node without an importer is not captured");
                var golden = Kit.GoldenContainer(t, true).Charas[Kit.GoldenChara];
                CharaImport.Importer = new FakeImporter { Subtree = golden };
                t.Check(refs.CaptureNode(s, s.Model.Find(cid)) && refs.TryGet(cid, P("face.eyebrow.tilt"), out var cv) && (float)cv == golden.Chara.Face.Eyebrow.Tilt,
                    "refs: a card-backed node is captured from the importer's documents");
            }
            finally { CharaImport.Importer = saved; }
            // scene replacement and another session
            t.Check(refs.Count >= 2, "refs: two references held");
            s.NewScene();
            t.Equal(0, refs.Count, "refs: a new scene clears the references");
            refs.CaptureNode(s, s.Model.Find(s.AddChara(new CharaAppearance { Stored = Kit.GoldenContainer(t, true).Charas[Kit.GoldenChara].Clone() }).NewIds[0]));
            var other = SceneTests_Appearance.GoldenChara(t, out string o);
            t.Check(refs.Count == 1 && refs.Session == s, "refs: bound to the first session");
            refs.CaptureNode(other, other.Model.Find(o));
            t.Check(refs.Session == other && refs.Count == 1 && refs.Has(o), "refs: binding another session clears the old references");
            refs.Detach();
            t.Check(refs.Session == null && refs.Has(o), "refs: Detach keeps the references");
            t.Check(MakerServices.References != null, "refs: MakerServices.References is always present");
        }
    }
}
