// The Maker ops of the body pages (
// SceneTests_AppearanceBody; the Unity-free cores in MakerEdits, which StudioOps.Maker wraps): skin colour swatch pushes of
// the 7 rows of 336 by id (the fixture) write HSVToRGB(Base) with alpha 1 (the census values within 1e-4; one entry each,
// both sexes), other paths bad_args, ids outside 0..6 / unknown / mod rows not_found, unsupported without lists; paint region
// picks (`layoutItem`, category 8) keep the layout and the rotation; body shape and bust values are stored unclamped; the
// futanari toggle (Y9) is one entry in the body area and marks no rebuild; the Maker gates (maker.bodyHeight with the male
// height pin, maker.headList, maker.list:<c>); the reset references per scene session (two sessions with the same node ids
// keep separate references, new / open clear one session's only, removal forgets, the kept MakerServices.References
// accessor follows the last session); undo / redo of each restores the documents byte for byte.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_AppearanceBody
    {
        public static void Run(TestRunner t)
        {
            Swatches(t);
            Regions(t);
            BodyValues(t);
            Futanari(t);
            Gates(t);
            References(t);
        }

        static string P(string m) => AppearancePaths.Chara(m);
        static readonly string SkinColor = AppearancePaths.Chara("body.skin.color");

        static MakerEditOptions Opts(SceneSession s, FakeMakerData data = null, bool multi = false) =>
            new MakerEditOptions { Rows = data ?? new FakeMakerData(), Defaults = data ?? new FakeMakerData(), NewId = s.NewId, SkipUnlistedRows = multi };

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

        static CommandResult Edit(SceneSession s, string id, string label, FakeMakerData data, params (string path, object value)[] sets) =>
            Tx(s, label, tx => MakerEdits.Edit(tx, s.Model, s.Model.Find(id), sets.ToList(), Opts(s, data), new MakerEditReport()));

        static string Bytes(SceneSession s, string id) => Kit.Hash(SceneTests_Appearance.CharaBytes(s, id)) + "/" + Kit.Hash(SceneTests_Appearance.CoordBytes(s, id));

        static SceneSession TwoCharas(TestRunner t, out string female, out string male, ulong seed = 1)
        {
            var s = SceneTests_Appearance.GoldenChara(t, out female, seed);
            var m = Kit.GoldenContainer(t, true).Charas[Kit.GoldenChara].Clone();
            m.Chara.Profile.Sex = 0;
            m.Coord.Sex = 0;
            male = s.AddChara(new CharaAppearance { Stored = m }).NewIds[0];
            s.Commands.ClearHistory();
            return s;
        }

        /// <summary>Undo every entry (state = <paramref name="start"/>), redo every entry (state = <paramref name="end"/>).</summary>
        static void UndoRedo(TestRunner t, SceneSession s, string[] ids, string[] start, string[] end, string what)
        {
            int depth = s.Commands.UndoList.Count;
            while (s.Commands.CanUndo) s.Commands.Undo();
            t.Check(ids.Select(i => Bytes(s, i)).SequenceEqual(start), what + ": undo all restores the documents byte for byte");
            while (s.Commands.CanRedo) s.Commands.Redo();
            t.Check(s.Commands.UndoList.Count == depth && ids.Select(i => Bytes(s, i)).SequenceEqual(end), what + ": redo all gives the end state byte for byte");
        }

        // ---------------------------------------------------------------- skin colour swatches

        // "Value (r, g, b)" and "Swatch (r, g, b)" columns (the census, rounded to 4 decimals)
        static readonly float[][] CensusValue =
        {
            new[] { 0.78f, 0.683f, 0.624f }, new[] { 0.74f, 0.6506f, 0.592f }, new[] { 0.75f, 0.6426f, 0.5925f }, new[] { 0.7f, 0.6042f, 0.553f },
            new[] { 0.65f, 0.5635f, 0.5135f }, new[] { 0.6f, 0.5201f, 0.474f }, new[] { 0.74f, 0.6607f, 0.6216f },
        };
        static readonly float[][] CensusSwatch =
        {
            new[] { 0.88f, 0.8034f, 0.7568f }, new[] { 0.81f, 0.6975f, 0.6237f }, new[] { 0.8f, 0.6527f, 0.584f }, new[] { 0.7f, 0.5813f, 0.518f },
            new[] { 0.6f, 0.5087f, 0.456f }, new[] { 0.5f, 0.4271f, 0.385f }, new[] { 0.85f, 0.8158f, 0.799f },
        };

        static bool Near(Color4 c, float[] e, float tol) => Math.Abs(c.R - e[0]) <= tol && Math.Abs(c.G - e[1]) <= tol && Math.Abs(c.B - e[2]) <= tol;

        static void Swatches(TestRunner t)
        {
            var data = new FakeMakerData();
            // the fixture's colours = the census (HsvToRgb is Unity's Color.HSVToRGB; checked against Unity in MakerBodyTest BT5)
            int near = 0;
            for (int i = 0; i < 7; i++)
            {
                bool ok = data.ColourPreset(336, "illusion.hs2#" + i, out var v, out var sw) && Near(v, CensusValue[i], 1e-4f) && Near(sw, CensusSwatch[i], 1e-4f) && v.A == 1f && sw.A == 1f;
                if (ok) near++;
                else t.Check(false, "swatch: row " + i + " value " + FieldJson.ToText(v) + " swatch " + FieldJson.ToText(sw));
            }
            t.Equal(7, near, "swatch: HSVToRGB(Base) / HSVToRGB(Sample) of the 7 rows = the census values within 1e-4, alpha 1");
            var tan = MakerEdits.HsvToRgb(0.058f, 0.21f, 0.7f);
            t.Check(Near(tan, new[] { 0.7f, 0.6042f, 0.553f }, 1e-4f), "swatch: «タン» = HSVToRGB(0.058, 0.21, 0.7) (UI5 step 5)");
            t.Check(MakerEdits.ColourPresetCategoryOf(SkinColor) == 336 && MakerEdits.ColourPresetCategoryOf("chara.body.skin.color") == 336 &&
                    MakerEdits.ColourPresetCategoryOf(P("body.nail.color")) == -1 && MakerEdits.ColourPresetCategoryOf(null) == -1,
                "swatch: the colour path of 336 (full or relative), others −1 (4.3 adds hair base colours → 305: SceneTests_AppearanceHair)");
            t.Check(MakerEdits.ColourPresetRowAllowed(336, "illusion.hs2#0") && MakerEdits.ColourPresetRowAllowed(336, "illusion.hs2#6") && !MakerEdits.ColourPresetRowAllowed(336, "illusion.hs2#7") &&
                    !MakerEdits.ColourPresetRowAllowed(336, "com.some.mod#3") && !MakerEdits.ColourPresetRowAllowed(336, "x") && MakerEdits.ColourPresetRowAllowed(999, "com.some.mod#3"),
                "swatch: 336 offers its vanilla ids 0..6 only (HS2's seven buttons); other lists any reference");

            var s = TwoCharas(t, out string f, out string m);
            var nf = s.Model.Find(f);
            var nm = s.Model.Find(m);
            // a stored skin alpha below 1: the push writes alpha 1
            Kit.Ok(t, Edit(s, f, "Edit body", data, (SkinColor, new Color4(0.1f, 0.2f, 0.3f, 0.5f))), "swatch: a skin colour with alpha 0.5 first");
            s.Commands.ClearHistory();
            string[] ids = { f, m };
            var start = ids.Select(i => Bytes(s, i)).ToArray();
            int ok2 = 0;
            for (int i = 0; i < 7; i++)
            {
                var rep = new MakerEditReport();
                int ii = i;
                // the relative path form of the bridge for odd rows
                string path = i % 2 == 0 ? SkinColor : "chara.body.skin.color";
                var r = Tx(s, "Edit body", tx => MakerEdits.ApplyColourPreset(tx, s.Model, nf, path, "illusion.hs2#" + ii, Opts(s, data), rep));
                var p = FakeMakerData.SkinPresets[i];
                var expect = MakerEdits.HsvToRgb(p.bh, p.bs, p.bv);
                var got = nf.Appearance.Stored.Chara.Body.Skin.Color;
                if (r.Ok && got.Equals(new Color4(expect.R, expect.G, expect.B, 1f)) && rep.Consequences.Count == 0 && rep.Changed.SequenceEqual(new[] { SkinColor })) ok2++;
                else t.Check(false, "swatch: row " + i + " → " + r + " " + FieldJson.ToText(got));
            }
            t.Equal(7, ok2, "swatch: each of the 7 rows writes HSVToRGB(Base) with alpha 1 to body.skin.color, no consequence");
            t.Equal(7, s.Commands.UndoList.Count, "swatch: one entry per push");
            t.Equal("body", MakerEdits.AreaOf(new[] { SkinColor }), "swatch: the area is body (StudioOps labels the entry Edit body)");
            // multi-selection: both sexes get the value (336 is shared)
            Kit.Ok(t, Tx(s, "Edit body", tx =>
            {
                var o = Opts(s, data, true);
                var r1 = MakerEdits.ApplyColourPreset(tx, s.Model, nf, SkinColor, "illusion.hs2#3", o, null);
                return r1.Status == CommandStatus.Error ? r1 : MakerEdits.ApplyColourPreset(tx, s.Model, nm, SkinColor, "illusion.hs2#3", o, null);
            }), "swatch: «タン» on the female and the male in one entry");
            var tanA = new Color4(tan.R, tan.G, tan.B, 1f);
            t.Check(nf.Appearance.Stored.Chara.Body.Skin.Color.Equals(tanA) && nm.Appearance.Stored.Chara.Body.Skin.Color.Equals(tanA) && s.Commands.UndoList.Count == 8,
                "swatch: both characters hold «タン», one entry");
            // the same push again changes nothing (no entry)
            Tx(s, "Edit body", tx => MakerEdits.ApplyColourPreset(tx, s.Model, nf, SkinColor, "illusion.hs2#3", Opts(s, data), null));
            t.Equal(8, s.Commands.UndoList.Count, "swatch: re-pushing the current value records nothing");
            var end = ids.Select(i => Bytes(s, i)).ToArray();
            // refused pushes change nothing
            foreach (var bad in new[] { P("body.nail.color"), P("hair.front.topColor"), P("face.skin"), P("body.skin"), "body.skin.color", null })
            {
                var r = Tx(s, "Edit body", tx => MakerEdits.ApplyColourPreset(tx, s.Model, nf, bad, "illusion.hs2#0", Opts(s, data), null));
                t.Check(r.Status == CommandStatus.Error && r.Code == CommandCodes.BadArgs, "swatch: path " + (bad ?? "null") + " → bad_args (" + r + ")");
            }
            foreach (var row in new[] { "illusion.hs2#7", "illusion.hs2#8", "illusion.hs2#-1", "fuzz.pool#33600", "com.some.mod#2", "nonsense", "", null })
            {
                var r = Tx(s, "Edit body", tx => MakerEdits.ApplyColourPreset(tx, s.Model, nf, SkinColor, row, Opts(s, data), null));
                t.Check(r.Status == CommandStatus.Error && r.Code == CommandCodes.NotFound, "swatch: row " + (row ?? "null") + " → not_found (" + r + ")");
            }
            var nl = Tx(s, "Edit body", tx => MakerEdits.ApplyColourPreset(tx, s.Model, nf, SkinColor, "illusion.hs2#0", new MakerEditOptions(), null));
            t.Check(nl.Status == CommandStatus.Error && nl.Code == CommandCodes.Unsupported, "swatch: without lists → unsupported");
            t.Check(ids.Select(i => Bytes(s, i)).SequenceEqual(end) && s.Commands.UndoList.Count == 8, "swatch: refused pushes change nothing");
            UndoRedo(t, s, ids, start, end, "swatch");
        }

        // ---------------------------------------------------------------- paint regions

        static void Regions(TestRunner t)
        {
            var data = new FakeMakerData();
            var s = TwoCharas(t, out string f, out string m);
            var body = s.Model.Find(f).Appearance.Stored.Chara.Body;
            // non-default layouts and rotations, so "kept" is visible
            Kit.Ok(t, Edit(s, f, "Edit body", data, (P("body.paint[0].layout"), new Vec4(0.1f, 1.3f, -0.4f, 0.7f)), (P("body.paint[0].rotation"), 0.85f),
                (P("body.paint[1].layout"), new Vec4(0.6f, 0.2f, 0.9f, 1.8f)), (P("body.paint[1].rotation"), -0.25f)), "regions: layouts and rotations set");
            s.Commands.ClearHistory();
            string[] ids = { f, m };
            var start = ids.Select(i => Bytes(s, i)).ToArray();
            int ok = 0, n = 0;
            for (int k = 0; k < 2; k++)
                foreach (int i in new[] { 0, 2, 20, 5 })
                {
                    n++;
                    var paint = body.Paint[k];
                    var other = body.Paint[1 - k];
                    var layout = paint.Layout;
                    float rot = paint.Rotation;
                    string otherText = FieldJson.ToText(other);
                    string row = FakeMakerData.Row(8, i);
                    var rep = new MakerEditReport();
                    int kk = k;
                    var r = Tx(s, "Change item", tx => MakerEdits.Edit(tx, s.Model, s.Model.Find(f), new List<(string, object)> { (P("body.paint[" + kk + "].layoutItem"), row) }, Opts(s, data), rep));
                    if (r.Ok && paint.LayoutItem == row && paint.Layout.Equals(layout) && paint.Rotation == rot && FieldJson.ToText(other) == otherText && rep.Consequences.Count == 0) ok++;
                    else t.Check(false, "regions: paint " + k + " region " + row + " → " + r + " " + FieldJson.ToText(paint));
                }
            t.Check(ok == n && n == 8, "regions: picks on both paints set layoutItem only (layout, rotation and the other paint kept, no consequence; " + ok + " / " + n + ")");
            t.Equal(8, s.Commands.UndoList.Count, "regions: one entry per pick");
            t.Check(MakerEdits.IsItemChange(new[] { P("body.paint[1].layoutItem") }, 1) && MakerEdits.CategoryOf(P("body.paint[0].layoutItem"), 0) == 8,
                "regions: a region pick is an item change of list 8 for both sexes (label Change item)");
            // a row not listed in 8: not_found, or skipped on a multi-selection; re-picking the current row records nothing
            var bad = Edit(s, f, "Change item", data, (P("body.paint[0].layoutItem"), FakeMakerData.Row(7, 1)));
            t.Check(bad.Status == CommandStatus.Error && bad.Code == CommandCodes.NotFound, "regions: a row of another list → not_found (" + bad + ")");
            var skip = new MakerEditReport();
            Tx(s, "Change item", tx => MakerEdits.Edit(tx, s.Model, s.Model.Find(f), new List<(string, object)> { (P("body.paint[0].layoutItem"), "illusion.hs2#999") }, Opts(s, data, true), skip));
            t.Check(skip.Warnings.Any(w => w.StartsWith("edit.rowSkipped", StringComparison.Ordinal)), "regions: an unlisted region on a multi-selection is skipped with edit.rowSkipped");
            Edit(s, f, "Change item", data, (P("body.paint[1].layoutItem"), body.Paint[1].LayoutItem));
            t.Equal(8, s.Commands.UndoList.Count, "regions: refused, skipped and repeated picks record nothing");
            // the male's region list is the same list (8 / 8)
            Kit.Ok(t, Edit(s, m, "Change item", data, (P("body.paint[0].layoutItem"), FakeMakerData.Row(8, 3))), "regions: a pick on the male");
            var end = ids.Select(i => Bytes(s, i)).ToArray();
            UndoRedo(t, s, ids, start, end, "regions");
        }

        // ---------------------------------------------------------------- body values (the user's value rule)

        static void BodyValues(TestRunner t)
        {
            var data = new FakeMakerData();
            var s = TwoCharas(t, out string f, out string m);
            string[] ids = { f, m };
            var start = ids.Select(i => Bytes(s, i)).ToArray();
            var b = s.Model.Find(f).Appearance.Stored.Chara.Body;
            Kit.Ok(t, Edit(s, f, "Edit body", data, (P("body.shape[0]"), 1.5f), (P("body.shape[1]"), -0.5f), (P("body.shape[32]"), 2f), (P("body.bust.softness"), -1f),
                (P("body.bust.weight"), 1.75f), (P("body.nipple.areola"), 1.9f), (P("body.paint[1].layout"), new Vec4(-1f, 2f, 1.5f, -0.5f)), (P("body.skin.gloss"), 1.6f)),
                "values: out-of-range body values in one entry");
            t.Check(b.Shape[0] == 1.5f && b.Shape[1] == -0.5f && b.Shape[32] == 2f && b.Bust.Softness == -1f && b.Bust.Weight == 1.75f && b.Nipple.Areola == 1.9f &&
                    b.Paint[1].Layout.Equals(new Vec4(-1f, 2f, 1.5f, -0.5f)) && b.Skin.Gloss == 1.6f, "values: stored as given (nothing clamps stored values)");
            // the male's hidden rows take values too (HS2: they still apply)
            Kit.Ok(t, Edit(s, m, "Edit body", data, (P("body.shape[5]"), 0.9f), (P("body.shape[0]"), 1.05f), (P("body.bust.weight"), 0.05f)), "values: a male's hidden rows and height");
            var mb = s.Model.Find(m).Appearance.Stored.Chara.Body;
            t.Check(mb.Shape[5] == 0.9f && mb.Shape[0] == 1.05f && mb.Bust.Weight == 0.05f, "values: the male's stored values");
            t.Check(new[] { "body.shape[3]", "body.bust.softness", "body.nipple.gloss", "body.paint[0].rotation" }.All(x => MakerEdits.AreaOf(new[] { P(x) }) == "body") &&
                    !MakerEdits.IsItemChange(new[] { P("body.shape[3]"), P("body.skin.color") }, 1), "values: body members are in the body area, not item changes (label Edit body)");
            t.Equal(2, s.Commands.UndoList.Count, "values: one entry per edit");
            var end = ids.Select(i => Bytes(s, i)).ToArray();
            UndoRedo(t, s, ids, start, end, "values");
        }

        // ---------------------------------------------------------------- futanari (Y9)

        static void Futanari(TestRunner t)
        {
            var data = new FakeMakerData();
            var s = TwoCharas(t, out string f, out string m);
            string[] ids = { f, m };
            var start = ids.Select(i => Bytes(s, i)).ToArray();
            var prof = s.Model.Find(f).Appearance.Stored.Chara.Profile;
            bool before = prof.Futanari;
            t.Check(MakerEdits.FutanariPath == "appearance.chara.profile.futanari" && !AppearancePaths.IsRebuildPath(MakerEdits.FutanariPath),
                "futanari: profile.futanari is not a rebuild member");
            t.Equal("body", MakerEdits.AreaOf(new[] { MakerEdits.FutanariPath }), "futanari: the area is body (one label, Edit body, for the UI row and the bridge)");
            t.Equal("name", MakerEdits.AreaOf(new[] { P("profile.name") }), "futanari: profile.name keeps its own area");
            var rep = new MakerEditReport();
            Kit.Ok(t, Tx(s, "Edit body", tx => MakerEdits.Edit(tx, s.Model, s.Model.Find(f), new List<(string, object)> { (MakerEdits.FutanariPath, !before) }, Opts(s, data), rep)), "futanari: toggle");
            t.Check(prof.Futanari == !before && rep.Consequences.Count == 0 && rep.Changed.SequenceEqual(new[] { MakerEdits.FutanariPath }), "futanari: one set, no consequence");
            t.Equal(1, s.Commands.UndoList.Count, "futanari: one entry per change");
            Kit.Ok(t, Edit(s, f, "Edit body", data, (MakerEdits.FutanariPath, before)), "futanari: toggle back");
            t.Check(prof.Futanari == before && s.Commands.UndoList.Count == 2, "futanari: two changes, two entries");
            var end = ids.Select(i => Bytes(s, i)).ToArray();
            UndoRedo(t, s, ids, start, end, "futanari");
            s.Commands.Undo();
            t.Check(prof.Futanari == !before, "futanari: undo restores the toggled value");
        }

        // ---------------------------------------------------------------- gates (Y8)

        static void Gates(TestRunner t)
        {
            var data = new FakeMakerData();
            Func<int, int, bool> lists = (c, sex) => sex == 0 ? c != 231 : c != 131;
            t.Check(MakerEdits.GateHolds("maker.bodyHeight", 1, data, false) && MakerEdits.GateHolds("maker.bodyHeight", 1, data, true), "gates: the height row is shown for females, pinned or not");
            t.Check(MakerEdits.GateHolds("maker.bodyHeight", 0, data, false) && !MakerEdits.GateHolds("maker.bodyHeight", 0, data, true),
                "gates: the male height row is shown while MaleHeightPin075 is off (the default) and hidden when on (Y8)");
            t.Check(MakerEdits.GateHolds("maker.bodyHeight", 0, null, false), "gates: maker.bodyHeight needs no lists");
            data.MaleHeads = 1;
            t.Check(MakerEdits.GateHolds("maker.headList", 1, data, false) && !MakerEdits.GateHolds("maker.headList", 0, data, false), "gates: maker.headList with one male head");
            data.MaleHeads = 10;
            t.Check(MakerEdits.GateHolds("maker.headList", 0, data, false) && !MakerEdits.GateHolds("maker.headList", 0, null, false), "gates: maker.headList with ten male heads; males without lists not");
            t.Check(MakerEdits.GateHolds("maker.list:131", 0, data, false, lists) && !MakerEdits.GateHolds("maker.list:131", 1, data, false, lists) &&
                    !MakerEdits.GateHolds("maker.list:231", 0, data, false, lists) && MakerEdits.GateHolds("maker.list:231", 0, data, false) &&
                    !MakerEdits.GateHolds("maker.list:x", 1, data, false, lists), "gates: maker.list:<c> through the list predicate (open without one)");
            t.Check(MakerEdits.GateHolds(null, 0, null, true) && MakerEdits.GateHolds("", 0, null, true) && MakerEdits.GateHolds("chara.female", 0, null, true),
                "gates: no gate and other gates hold (the inspector evaluates chara.*)");
            var saved = MakerServices.MaleHeightPinned;
            try
            {
                MakerServices.MaleHeightPinned = null;
                t.Check(!(MakerServices.MaleHeightPinned?.Invoke() ?? false), "gates: no registered pin (plain Mono) = not pinned (the gate holds)");
            }
            finally { MakerServices.MaleHeightPinned = saved; }
        }

        // ---------------------------------------------------------------- references per scene session (Y13)

        static void References(TestRunner t)
        {
            var data = new FakeMakerData();
            var s1 = TwoCharas(t, out string a1, out string b1, 7);
            var s2 = TwoCharas(t, out string a2, out string b2, 7);
            t.Check(a1 == a2 && b1 == b2, "refs: two sessions with the same node ids");
            var r1 = s1.MakerReferences;
            var r2 = s2.MakerReferences;
            t.Check(r1 != null && r2 != null && !ReferenceEquals(r1, r2) && ReferenceEquals(r1, s1.MakerReferences) && r1.Owner == s1 && r1.Session == s1 && r2.Session == s2,
                "refs: each session owns one store, bound to it");
            t.Check(ReferenceEquals(MakerServices.ReferencesOf(s1), r1) && MakerServices.ReferencesOf(null) == null, "refs: MakerServices.ReferencesOf(session)");
            float size0 = s1.Model.Find(a1).Appearance.Stored.Chara.Body.Shape[1];
            t.Check(r1.CaptureNode(s1, s1.Model.Find(a1)), "refs: session 1 captures A");
            Kit.Ok(t, Edit(s1, a1, "Edit body", data, (P("body.shape[1]"), 0.95f), (P("body.paint[0].layout"), new Vec4(0.2f, 0.3f, 0.4f, 0.6f))), "refs: edit A in session 1");
            t.Check(!r2.Has(a2), "refs: session 2 holds no reference of its A (same id)");
            Kit.Ok(t, Edit(s2, a2, "Edit body", data, (P("body.shape[1]"), 0.11f)), "refs: edit A in session 2 before its capture");
            t.Check(r2.CaptureNode(s2, s2.Model.Find(a2)), "refs: session 2 captures its A");
            t.Check(r1.TryGet(a1, P("body.shape[1]"), out var v1) && (float)v1 == size0 && r2.TryGet(a2, P("body.shape[1]"), out var v2) && (float)v2 == 0.11f,
                "refs: the two sessions keep separate references for the same node id");
            // the row reset reads its own session's store
            s1.Commands.ClearHistory();
            string before = Bytes(s1, a1);
            Kit.Ok(t, Tx(s1, "Reset value", tx => MakerEdits.ResetAppearance(tx, s1.Model, s1.Model.Find(a1), s1.MakerReferences,
                new[] { P("body.shape[1]"), P("body.paint[0].layout") + "#2", P("body.skin.color") }, new object[] { 0.5, 0.5, new[] { 0.8, 0.7, 0.64, 1.0 } }, Opts(s1, data), null)),
                "refs: reset body rows in session 1");
            var body1 = s1.Model.Find(a1).Appearance.Stored.Chara.Body;
            var golden = Kit.GoldenContainer(t, true).Charas[Kit.GoldenChara].Chara.Body;
            t.Check(body1.Shape[1] == size0 && body1.Paint[0].Layout.Equals(new Vec4(0.2f, 0.3f, golden.Paint[0].Layout.Z, 0.6f)) && body1.Skin.Color.Equals(golden.Skin.Color),
                "refs: the reset values are session 1's reference values (component rows keep the other components)");
            s1.Commands.Undo();
            t.Equal(before, Bytes(s1, a1), "refs: undoing the reset restores the documents byte for byte");
            // the kept accessor follows the last session the ops ran on
            var savedRefs = MakerServices.References;
            try
            {
                MakerServices.UseSession(s1);
                t.Check(ReferenceEquals(MakerServices.References, r1), "refs: MakerServices.References = the store of the session in use (1)");
                MakerServices.UseSession(s2);
                t.Check(ReferenceEquals(MakerServices.References, r2), "refs: … and (2)");
                r2.Attach(s1);
                t.Check(r2.Session == s2 && ReferenceEquals(MakerServices.References, r1), "refs: Attach(other) keeps a session's store bound and selects the other session's store (4.1 call form)");
                r1.Detach();
                t.Check(r1.Session == s1, "refs: a session's store cannot be detached");
                MakerServices.UseSession(null);
                var legacy = MakerServices.References;
                t.Check(legacy != null && legacy.Owner == null && legacy.Count == 0 && !ReferenceEquals(legacy, r1), "refs: without a session the accessor is an empty store");
                legacy.Attach(s2);
                t.Check(ReferenceEquals(MakerServices.References, r2) && legacy.Count == 0, "refs: Attach on the empty store selects that session's store");
                MakerServices.UseSession(null);
                t.Check(MakerServices.References.CaptureNode(s2, s2.Model.Find(b2)) && r2.Has(b2) && !MakerServices.ReferencesOf(s1).Has(b1) && ReferenceEquals(MakerServices.References, r2),
                    "refs: a capture through the accessor lands in the node's own session store");
                t.Check(r1.CaptureNode(s2, s2.Model.Find(b2)) == false && r2.Has(b2) && !r1.Has(b2), "refs: a capture of another session's node goes to that session's store (already held there)");
            }
            finally { MakerServices.UseSession(null); }
            t.Check(savedRefs != null, "refs: the accessor was always present");
            // removal forgets in its own session only; new / open clear one session only
            Kit.Ok(t, s2.Delete(new[] { a2 }), "refs: delete A in session 2");
            t.Check(!r2.Has(a2) && r1.Has(a1), "refs: a removal forgets the reference in its own session only");
            s1.NewScene();
            t.Check(r1.Count == 0 && r2.Has(b2), "refs: a new scene in session 1 clears session 1's references only");
            t.Check(ReferenceEquals(s1.MakerReferences, r1) && r1.Session == s1, "refs: the store survives the new scene, still bound");
            var bytes = s2.ToBytes();
            var load = s2.Load(bytes);
            t.Check(load.Ok && r2.Count == 0, "refs: opening a scene in session 2 clears its references");
            // a session whose store was never used creates none for holders (EditReferences.Capture's holder sessions)
            var s3 = Kit.Session(9);
            string c3 = s3.AddChara(new CharaAppearance { Stored = Kit.GoldenContainer(t, true).Charas[Kit.GoldenChara].Clone() }).NewIds[0];
            t.Check(s3.MakerReferences.Capture(c3, s3.Model.Find(c3).Appearance.Stored) && s3.MakerReferences.Count == 1, "refs: Capture into a fresh session's store");
        }
    }
}
