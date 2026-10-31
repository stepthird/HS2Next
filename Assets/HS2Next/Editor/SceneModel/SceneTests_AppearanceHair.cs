// The Maker ops of the hair page (
// the Unity-free cores in MakerEdits, which StudioOps.Maker wraps):
//   presets 305: the 7 rows by id (the fixture: HSVToRGB = Unity's, the census colours within 1e-4) write base / top /
//     under / specular / metallic / smoothness on the part, or on all four parts with «前髪と後ろ髪の色を統一», as caller paths
//     (no auto derivation with auto on), each colour keeping the target's stored alpha; one entry each; other hair members
//     bad_args, ids outside 0..6 / mod / unknown rows not_found, unsupported without lists;
//   skin match: under = HSVToRGB(H, max(0, S − 0.06), max(0, V − 0.06)) of the skin colour (alpha kept), smoothness =
//     max(0, gloss − 0.3), the shown part only although same setting is on;
//   bundles: the facts (default rates = InverseLerp of the base pose, titles among the shown), the effective-list rule
//     (count / indices), EditHairBundle's materialization from an empty list, a count mismatch and a duplicate index, a valid
//     list edited in place, the together rule per component (move and rotate apart), resets to the prefab default (one axis,
//     all, noShake) that never propagate, noShake alone, unclamped values, a drag (three preview frames, one entry), index /
//     part / axis errors, plain Mono without facts (a complete stored list edits, otherwise unsupported), multi-selection
//     skips; ResetHairBundles (noShake kept); an item change clears the bundles and the next edit materializes again;
//   gates: maker.hair:<part>:<feature> on fixture facts (no service: prefab features hold; unknown part: none);
//   undo / redo of each restores the documents byte for byte.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_AppearanceHair
    {
        public static void Run(TestRunner t)
        {
            Presets(t);
            SkinMatch(t);
            Facts(t);
            Bundles(t);
            Together(t);
            Resets(t);
            NoFacts(t);
            Gates(t);
        }

        static string P(string m) => AppearancePaths.Chara(m);
        static readonly string[] Parts = HairData.PartNames;

        static MakerEditOptions Opts(SceneSession s, FakeMakerData data, bool multi = false) =>
            new MakerEditOptions { Rows = data, Defaults = data, NewId = s.NewId, SkipUnlistedRows = multi };

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

        static void UndoRedo(TestRunner t, SceneSession s, string[] ids, string[] start, string[] end, string what)
        {
            int depth = s.Commands.UndoList.Count;
            while (s.Commands.CanUndo) s.Commands.Undo();
            t.Check(ids.Select(i => Bytes(s, i)).SequenceEqual(start), what + ": undo all restores the documents byte for byte");
            while (s.Commands.CanRedo) s.Commands.Redo();
            t.Check(s.Commands.UndoList.Count == depth && ids.Select(i => Bytes(s, i)).SequenceEqual(end), what + ": redo all gives the end state byte for byte");
        }

        static void Do(TestRunner t, CommandResult r, string what) => t.Check(r != null && r.Ok, what + (r == null ? " (null result)" : r.Ok ? "" : " (" + r + ")"));

        static HairData Hair(SceneSession s, string id) => s.Model.Find(id).Appearance.Stored.Chara.Hair;

        static bool Near(Color4 c, float[] e, float tol) => Math.Abs(c.R - e[0]) <= tol && Math.Abs(c.G - e[1]) <= tol && Math.Abs(c.B - e[2]) <= tol;

        // ---------------------------------------------------------------- presets 305 (J11)

        // Base, Top, Under, Specular (r, g, b), Metallic, Swatch (r, g, b) — the census, rounded
        static readonly float[][][] Census =
        {
            new[] { new[] { 0.09f, 0.09f, 0.09f }, new[] { 0.113f, 0.113f, 0.113f }, new[] { 0.491f, 0.491f, 0.491f }, new[] { 0.56f, 0.5488f, 0.5488f }, new[] { 0.03f } },
            new[] { new[] { 0.294f, 0.2374f, 0.2234f }, new[] { 0.19f, 0.1541f, 0.1349f }, new[] { 0.604f, 0.5634f, 0.5388f }, new[] { 0.557f, 0.4647f, 0.4127f }, new[] { 0.314f } },
            new[] { new[] { 0.311f, 0.2521f, 0.1863f }, new[] { 0.387f, 0.3233f, 0.2794f }, new[] { 0.708f, 0.6295f, 0.5374f }, new[] { 0.726f, 0.6268f, 0.5104f }, new[] { 0f } },
            new[] { new[] { 0.585f, 0.4322f, 0.3393f }, new[] { 0.62f, 0.451f, 0.3534f }, new[] { 0.83f, 0.6722f, 0.5835f }, new[] { 0.962f, 0.7078f, 0.5493f }, new[] { 0.299f } },
            new[] { new[] { 0.623f, 0.5532f, 0.4672f }, new[] { 0.594f, 0.5224f, 0.4342f }, new[] { 0.821f, 0.7211f, 0.6469f }, new[] { 0.764f, 0.6574f, 0.5516f }, new[] { 0.185f } },
            new[] { new[] { 0.75f, 0.684f, 0.585f }, new[] { 0.783f, 0.7674f, 0.7642f }, new[] { 0.925f, 0.8321f, 0.7631f }, new[] { 0.84f, 0.745f, 0.6552f }, new[] { 0f } },
            new[] { new[] { 0.78f, 0.7673f, 0.7254f }, new[] { 0.78f, 0.7675f, 0.7215f }, new[] { 1.0f, 0.985f, 0.93f }, new[] { 0.75f, 0.7465f, 0.72f }, new[] { 0f } },
        };

        static bool Is(Color4 c, Color4 rgb, float alpha) => c.R == rgb.R && c.G == rgb.G && c.B == rgb.B && c.A == alpha;

        static void Presets(TestRunner t)
        {
            var data = new FakeMakerData();
            int near = 0;
            for (int i = 0; i < 7; i++)
            {
                var c = Census[i];
                bool good = data.HairColourPreset("illusion.hs2#" + i, out var v) && Near(v.Base, c[0], 1e-4f) && Near(v.Top, c[1], 1e-4f) && Near(v.Under, c[2], 1e-4f) &&
                          Near(v.Specular, c[3], 1e-4f) && v.Metallic == c[4][0] && v.Smoothness == 0f && v.Base.A == 1f && v.Specular.A == 1f;
                if (good) near++;
                else t.Check(false, "presets: row " + i + " " + v);
            }
            t.Equal(7, near, "presets: HSVToRGB of the 7 rows' Base / Top / Under / Specular = the census within 1e-4, metallic exact, smoothness 0, alpha 1");
            t.Check(MakerEdits.ColourPresetCategoryOf(P("hair.back.baseColor")) == 305 && MakerEdits.ColourPresetCategoryOf("chara.hair.option.baseColor") == 305 &&
                    MakerEdits.ColourPresetCategoryOf(P("hair.front.topColor")) == -1 && MakerEdits.ColourPresetCategoryOf(P("hair.front.mesh.color")) == -1 &&
                    MakerEdits.ColourPresetCategoryOf(P("hair.front.accessoryColors[0]")) == -1 && MakerEdits.ColourPresetCategoryOf(P("body.skin.color")) == 336,
                "presets: the hair base colours (full or relative) push from 305; other hair colours do not");
            t.Check(MakerEdits.ColourPresetRowAllowed(305, "illusion.hs2#0") && MakerEdits.ColourPresetRowAllowed(305, "illusion.hs2#6") && !MakerEdits.ColourPresetRowAllowed(305, "illusion.hs2#7") &&
                    !MakerEdits.ColourPresetRowAllowed(305, "com.some.mod#3") && !MakerEdits.ColourPresetRowAllowed(305, "x"), "presets: 305 offers its vanilla ids 0..6 only (HS2's seven buttons)");

            var s = TwoCharas(t, out string f, out string m);
            var nf = s.Model.Find(f);
            var nm = s.Model.Find(m);
            // auto on, same setting off; stored alphas below 1 on two targets; distinct starting colours
            Do(t, s.Set(f, P("hair.editor.sameSetting"), false), "presets: same setting off");
            Do(t, s.Set(f, P("hair.editor.autoSetting"), true), "presets: auto on");
            Do(t, s.Set(f, P("hair.front.topColor"), new Color4(0.1f, 0.2f, 0.3f, 0.5f)), "presets: front top colour alpha 0.5");
            Do(t, s.Set(f, P("hair.side.specular"), new Color4(0.4f, 0.4f, 0.4f, 0f)), "presets: side gloss alpha 0");
            s.Commands.ClearHistory();
            string[] ids = { f, m };
            var start = ids.Select(i => Bytes(s, i)).ToArray();
            var hair = Hair(s, f);
            int ok = 0;
            for (int i = 0; i < 7; i++)
            {
                int p = i % 4, ii = i;
                var before = Enumerable.Range(0, 4).Where(k => k != p).Select(k => FieldJson.ToText(hair.Parts[k])).ToList();
                var alphas = new[] { hair.Parts[p].BaseColor.A, hair.Parts[p].TopColor.A, hair.Parts[p].UnderColor.A, hair.Parts[p].Specular.A };
                var rep = new MakerEditReport();
                string path = i % 2 == 0 ? P("hair." + Parts[p] + ".baseColor") : "chara.hair." + Parts[p] + ".baseColor";
                var r = Tx(s, "Edit hair", tx => MakerEdits.ApplyColourPreset(tx, s.Model, nf, path, "illusion.hs2#" + ii, Opts(s, data), rep));
                data.HairColourPreset("illusion.hs2#" + i, out var v);
                var hp = hair.Parts[p];
                bool vals = Is(hp.BaseColor, v.Base, alphas[0]) && Is(hp.TopColor, v.Top, alphas[1]) && Is(hp.UnderColor, v.Under, alphas[2]) && Is(hp.Specular, v.Specular, alphas[3]) &&
                            hp.Metallic == v.Metallic && hp.Smoothness == v.Smoothness;
                bool others = Enumerable.Range(0, 4).Where(k => k != p).Select(k => FieldJson.ToText(hair.Parts[k])).SequenceEqual(before);
                if (r.Ok && vals && others && rep.Consequences.Count == 0) ok++;
                else t.Check(false, "presets: row " + i + " on " + Parts[p] + " → " + r + " vals " + vals + " others " + others + " consequences " + string.Join(", ", rep.Consequences));
            }
            t.Equal(7, ok, "presets: each row writes the six members on its part only (same setting off), the stored alphas kept (front top 0.5, side gloss 0), no consequence");
            t.Equal(7, s.Commands.UndoList.Count, "presets: one entry per push");
            // no derivation with auto on: row 5's top is the row's, not Derive(base)
            MakerEdits.Derive(hair.Parts[1].BaseColor, out var dTop, out _, out _);
            data.HairColourPreset("illusion.hs2#5", out var r5);
            t.Check(Is(hair.Parts[1].TopColor, r5.Top, 0.5f) && !(dTop.R == r5.Top.R && dTop.G == r5.Top.G), "presets: with auto on the top colour is the row's (no derivation, J11)");
            // same setting on: all four parts; the male (multi-selection, his own switch: on by default) too
            Kit.Ok(t, Tx(s, "Edit hair", tx => MakerEdits.Edit(tx, s.Model, nf, new List<(string, object)> { (P("hair.editor.sameSetting"), true) }, Opts(s, data), null)), "presets: same setting on");
            var rep2 = new MakerEditReport();
            Kit.Ok(t, Tx(s, "Edit hair", tx =>
            {
                var o = Opts(s, data, true);
                var a = MakerEdits.ApplyColourPreset(tx, s.Model, nf, P("hair.side.baseColor"), "illusion.hs2#3", o, rep2);
                return a.Status == CommandStatus.Error ? a : MakerEdits.ApplyColourPreset(tx, s.Model, nm, P("hair.side.baseColor"), "illusion.hs2#3", o, rep2);
            }), "presets: «赤毛» on side with same setting, both characters in one entry");
            data.HairColourPreset("illusion.hs2#3", out var r3);
            bool four = new[] { hair, Hair(s, m) }.All(h => h.Parts.All(hp => hp.BaseColor.R == r3.Base.R && hp.TopColor.G == r3.Top.G && hp.UnderColor.B == r3.Under.B &&
                hp.Specular.R == r3.Specular.R && hp.Metallic == r3.Metallic && hp.Smoothness == r3.Smoothness));
            t.Check(four && hair.Parts[1].TopColor.A == 0.5f && hair.Parts[2].Specular.A == 0f && rep2.Consequences.Count == 0 && rep2.Changed.Concat(rep2.NoOp).Distinct().Count(c => c.StartsWith(P("hair."), StringComparison.Ordinal) && !c.Contains("editor")) == 24,
                "presets: with same setting the six members on all four parts of both characters, alphas kept, all caller paths (no consequence; four " + four + ", consequences " + rep2.Consequences.Count + ", paths " + rep2.Changed.Concat(rep2.NoOp).Distinct().Count() + ")");
            t.Equal(9, s.Commands.UndoList.Count, "presets: the switch and the push are one entry each");
            t.Equal("hair", MakerEdits.AreaOf(new[] { P("hair.front.baseColor") }), "presets: the area is hair (StudioOps labels the entry Edit hair)");
            int depth = s.Commands.UndoList.Count;
            Tx(s, "Edit hair", tx => MakerEdits.ApplyColourPreset(tx, s.Model, nf, P("hair.side.baseColor"), "illusion.hs2#3", Opts(s, data), null));
            t.Equal(depth, s.Commands.UndoList.Count, "presets: re-pushing the current values records nothing");
            var end = ids.Select(i => Bytes(s, i)).ToArray();
            foreach (var bad in new[] { P("hair.front.topColor"), P("hair.back.metallic"), P("hair.back"), P("hair.editor.sameSetting"), "hair.back.baseColor", null })
            {
                var r = Tx(s, "Edit hair", tx => MakerEdits.ApplyColourPreset(tx, s.Model, nf, bad, "illusion.hs2#0", Opts(s, data), null));
                t.Check(r.Status == CommandStatus.Error && r.Code == CommandCodes.BadArgs, "presets: path " + (bad ?? "null") + " → bad_args (" + r + ")");
            }
            foreach (var row in new[] { "illusion.hs2#7", "illusion.hs2#-1", "fuzz.pool#30500", "com.some.mod#2", "nonsense", "", null })
            {
                var r = Tx(s, "Edit hair", tx => MakerEdits.ApplyColourPreset(tx, s.Model, nf, P("hair.back.baseColor"), row, Opts(s, data), null));
                t.Check(r.Status == CommandStatus.Error && r.Code == CommandCodes.NotFound, "presets: row " + (row ?? "null") + " → not_found (" + r + ")");
            }
            var nl = Tx(s, "Edit hair", tx => MakerEdits.ApplyColourPreset(tx, s.Model, nf, P("hair.back.baseColor"), "illusion.hs2#0", new MakerEditOptions(), null));
            t.Check(nl.Status == CommandStatus.Error && nl.Code == CommandCodes.Unsupported, "presets: without lists → unsupported");
            t.Check(ids.Select(i => Bytes(s, i)).SequenceEqual(end) && s.Commands.UndoList.Count == depth, "presets: refused pushes change nothing");
            UndoRedo(t, s, ids, start, end, "presets");
        }

        // ---------------------------------------------------------------- skin match (J12)

        static void SkinMatch(TestRunner t)
        {
            var data = new FakeMakerData();
            var s = TwoCharas(t, out string f, out string m);
            var nf = s.Model.Find(f);
            Do(t, s.Set(f, P("body.skin.color"), new Color4(0.8f, 0.7f, 0.64f, 1f)), "skin: skin colour (0.8, 0.7, 0.64)");
            Do(t, s.Set(f, P("body.skin.gloss"), 0.75f), "skin: gloss 0.75");
            Do(t, s.Set(f, P("hair.editor.sameSetting"), true), "skin: same setting on");
            Do(t, s.Set(f, P("hair.front.underColor"), new Color4(0.5f, 0.5f, 0.5f, 0.25f)), "skin: front under alpha 0.25");
            s.Commands.ClearHistory();
            string[] ids = { f, m };
            var start = ids.Select(i => Bytes(s, i)).ToArray();
            var hair = Hair(s, f);
            var others = new[] { 0, 2, 3 }.Select(k => FieldJson.ToText(hair.Parts[k])).ToList();
            var rep = new MakerEditReport();
            Kit.Ok(t, Tx(s, "Edit hair", tx => MakerEdits.MatchHairToSkin(tx, s.Model, nf, 1, Opts(s, data), rep)), "skin: match on the front hair");
            // RGBToHSV(0.8, 0.7, 0.64) = (0.0625, 0.2, 0.8) → HSV(0.0625, 0.14, 0.74) = (0.74, 0.67525, 0.6364)
            var u = hair.Parts[1].UnderColor;
            t.Check(Math.Abs(u.R - 0.74f) < 1e-5f && Math.Abs(u.G - 0.67525f) < 1e-5f && Math.Abs(u.B - 0.6364f) < 1e-5f && u.A == 0.25f,
                "skin: under = HSVToRGB(H, S − 0.06, V − 0.06) of the skin = (0.74, 0.67525, 0.6364), the stored alpha 0.25 kept (" + FieldJson.ToText(u) + ")");
            t.Check(Math.Abs(hair.Parts[1].Smoothness - 0.45f) < 1e-6f, "skin: smoothness = gloss − 0.3 = 0.45");
            t.Check(new[] { 0, 2, 3 }.Select(k => FieldJson.ToText(hair.Parts[k])).SequenceEqual(others) && rep.Consequences.Count == 0 &&
                    rep.Changed.SequenceEqual(new[] { P("hair.front.underColor"), P("hair.front.smoothness") }),
                "skin: the shown part only although same setting is on (no consequence; J12)");
            var sets = MakerEdits.SkinMatchSets(nf.Appearance.Stored.Chara, 1);
            t.Check(sets.Count == 2 && sets[0].path == P("hair.front.underColor") && sets[1].path == P("hair.front.smoothness"), "skin: SkinMatchSets names the two members");
            // clamps: a dark, unsaturated skin and a low gloss
            Kit.Ok(t, Tx(s, "Edit body", tx => MakerEdits.Edit(tx, s.Model, nf, new List<(string, object)> { (P("body.skin.color"), new Color4(0.03f, 0.03f, 0.03f, 1f)), (P("body.skin.gloss"), 0.1f) }, Opts(s, data), null)),
                "skin: a dark grey skin, gloss 0.1");
            Kit.Ok(t, Tx(s, "Edit hair", tx => MakerEdits.MatchHairToSkin(tx, s.Model, nf, 3, Opts(s, data), null)), "skin: match on the extension");
            var u3 = hair.Parts[3].UnderColor;
            t.Check(u3.R == 0f && u3.G == 0f && u3.B == 0f && hair.Parts[3].Smoothness == 0f, "skin: V − 0.06 and gloss − 0.3 clamp at 0");
            // NoHairLink does not leak: a later colour edit with same setting still links the parts
            Kit.Ok(t, Tx(s, "Edit hair", tx => MakerEdits.Edit(tx, s.Model, nf, new List<(string, object)> { (P("hair.back.smoothness"), 0.6f) }, Opts(s, data), null)), "skin: a smoothness edit");
            t.Check(hair.Parts.All(hp => hp.Smoothness == 0.6f), "skin: an ordinary edit still follows same setting (the option is per call)");
            var bad = Tx(s, "Edit hair", tx => MakerEdits.MatchHairToSkin(tx, s.Model, nf, 4, Opts(s, data), null));
            var bad2 = Tx(s, "Edit hair", tx => MakerEdits.MatchHairToSkin(tx, s.Model, nf, -1, Opts(s, data), null));
            t.Check(bad.Code == CommandCodes.BadArgs && bad2.Code == CommandCodes.BadArgs, "skin: part outside 0..3 → bad_args");
            t.Equal(4, s.Commands.UndoList.Count, "skin: one entry per edit");
            var end = ids.Select(i => Bytes(s, i)).ToArray();
            UndoRedo(t, s, ids, start, end, "skin");
            var o = new MakerEditOptions { Rows = data };
            var c = o.Clone();
            c.NoHairLink = true;
            t.Check(!o.NoHairLink && c.Rows == data, "skin: MakerEditOptions.Clone is a copy");
        }

        // ---------------------------------------------------------------- facts and the effective list

        static void Facts(TestRunner t)
        {
            // the example (vanilla back 0, bundle 0): posMin (−0.45, 0.3, 0.2), posMax (0.45, −0.3, −0.2), rotMin (−20, 25, −25),
            // rotMax (70, −25, 25), base 0 → default move (0.5, 0.5, 0.5), rotate (0.2222, 0.5, 0.5)
            var b = new HairBundleFacts(0, true, 1, new Vec3(-0.45f, 0.3f, 0.2f), new Vec3(0.45f, -0.3f, -0.2f), new Vec3(-20f, 25f, -25f), new Vec3(70f, -25f, 25f), Vec3.Zero, Vec3.Zero);
            t.Check(b.DefaultMove.Equals(new Vec3(0.5f, 0.5f, 0.5f)) && Math.Abs(b.DefaultRotate.X - 0.2222f) < 1e-4f && b.DefaultRotate.Y == 0.5f && b.DefaultRotate.Z == 0.5f,
                "facts: the example → default move (0.5, 0.5, 0.5), rotate (0.2222, 0.5, 0.5)");
            t.Check(MakerEdits.InverseLerp(1f, 1f, 5f) == 0f && MakerEdits.InverseLerp(0f, 2f, 3f) == 1f && MakerEdits.InverseLerp(0f, 2f, -1f) == 0f &&
                    MakerEdits.InverseLerp(2f, 0f, 0.5f) == 0.75f && MakerEdits.InverseLerp(1f, 1.0000001f, 1f) == 0f,
                "facts: InverseLerp clamps, reverses, and gives 0 for (approximately) equal limits (EditContext.Inv)");
            t.Check(MakerEdits.Approximately(0f, 0f) && MakerEdits.Approximately(100f, 100.00001f) && !MakerEdits.Approximately(1f, 1.001f),
                "facts: Approximately = Unity's rule");
            var list = HairBundleFacts.Number(new List<(bool, Vec3, Vec3, Vec3, Vec3, Vec3, Vec3)>
            {
                (false, Vec3.Zero, Vec3.One, Vec3.Zero, Vec3.One, Vec3.Zero, Vec3.Zero),
                (true, Vec3.Zero, Vec3.One, Vec3.Zero, Vec3.One, new Vec3(0.25f, 0.5f, 2f), Vec3.Zero),
                (true, Vec3.Zero, Vec3.One, Vec3.Zero, Vec3.One, Vec3.Zero, Vec3.Zero),
            });
            t.Check(list.Select(x => x.Index + ":" + x.Shown + ":" + x.Title).SequenceEqual(new[] { "0:False:0", "1:True:1", "2:True:2" }) &&
                    list[1].DefaultMove.Equals(new Vec3(0.25f, 0.5f, 1f)), "facts: Number titles the shown bundles 1.. (HS2's «調整NN»), the default rates clamp to 0..1");
            var db = list[1].DefaultBundle();
            t.Check(db.Index == 1 && db.Move.Equals(list[1].DefaultMove) && db.Rotate.Equals(list[1].DefaultRotate) && !db.NoShake, "facts: DefaultBundle");
            var pf = new HairPartFacts { HasPrefab = true, UseAcsColor01 = true, UseAcsColor02 = true, UseAcsColor03 = true, AcsDefColorCount = 2, Bundles = list };
            t.Check(pf.AcsShown(1) && pf.AcsShown(2) && !pf.AcsShown(3) && !pf.AcsShown(0) && pf.AnyBundleShown && !HairPartFacts.None().AnyBundleShown && !HairPartFacts.None().AcsShown(1),
                "facts: «カラー0N» needs its flag and N ≤ the acsDefColor count (GH5); a bundle with a correction transform shows the section");
            // the effective-list rule
            Func<int[], List<HairBundle>> L = ix => ix.Select(i => new HairBundle { Index = i, Move = new Vec3(i, i, i) }).ToList();
            t.Check(MakerEdits.BundlesComplete(L(new[] { 2, 0, 1 }), 3) && !MakerEdits.BundlesComplete(L(new[] { 0, 1 }), 3) && !MakerEdits.BundlesComplete(L(new[] { 0, 1, 1 }), 3) &&
                    !MakerEdits.BundlesComplete(L(new[] { 0, 1, 3 }), 3) && MakerEdits.BundlesComplete(L(new int[0]), 0) && !MakerEdits.BundlesComplete(null, 0) &&
                    !MakerEdits.BundlesComplete(new List<HairBundle> { null }, 1), "facts: BundlesComplete = exactly n entries with the distinct indices 0..n−1");
            var e1 = MakerEdits.EffectiveBundles(L(new[] { 2, 0, 1 }), list, out bool fs1);
            var e2 = MakerEdits.EffectiveBundles(L(new[] { 0, 1 }), list, out bool fs2);
            var e3 = MakerEdits.EffectiveBundles(L(new[] { 1, 0 }), null, out bool fs3);
            var e4 = MakerEdits.EffectiveBundles(L(new[] { 1, 2 }), null, out bool fs4);
            t.Check(fs1 && e1.Select(x => x.Index).SequenceEqual(new[] { 0, 1, 2 }) && e1[2].Move.X == 2f, "facts: a complete list is effective, sorted by index");
            t.Check(!fs2 && e2.Count == 3 && e2.Select((x, i) => x.Move.Equals(list[i].DefaultMove) && x.Rotate.Equals(list[i].DefaultRotate) && !x.NoShake).All(x => x),
                "facts: a count mismatch → every bundle at the prefab default (J6)");
            t.Check(fs3 && e3.Count == 2 && e4 == null && !fs4, "facts: without facts a list holding 0..m−1 is taken as it is, another one is unknown");
        }

        // ---------------------------------------------------------------- bundle edits (6, 7; J6)

        const string Item3 = "fuzz.pool#30005", Item5 = "fuzz.pool#30007", Item0 = "fuzz.pool#30009", Item3F = "fuzz.pool#30105";

        static FakeMakerData BundleData()
        {
            var d = new FakeMakerData();
            d.BundleCounts[Item3] = 3;
            d.BundleCounts[Item5] = 5;
            d.BundleCounts[Item0] = 0;
            d.BundleCounts[Item3F] = 3;
            return d;
        }

        static HairBundleFacts[] FactsOf(FakeMakerData d, string item) => d.TryHairBundles(300, item, 1, out var f) ? f : null;

        static SceneSession BundleScene(TestRunner t, FakeMakerData data, out string f, out string m)
        {
            var s = TwoCharas(t, out f, out m);
            foreach (var id in new[] { f, m })
            {
                Do(t, s.Set(id, P("hair.back.item"), Item3), "bundles: back item with 3 bundles");
                Do(t, s.Set(id, P("hair.back.bundles"), new List<HairBundle>()), "bundles: back bundles not stored");
                Do(t, s.Set(id, P("hair.front.item"), Item5), "bundles: front item with 5 bundles");
                Do(t, s.Set(id, P("hair.editor.ctrlTogether"), false), "bundles: together off");
            }
            s.Commands.ClearHistory();
            return s;
        }

        static CommandResult EB(SceneSession s, string id, FakeMakerData data, int part, int index, Vec3? move = null, Vec3? rotate = null, bool? noShake = null,
            bool reset = false, int axis = -1, MakerEditReport rep = null, bool multi = false) =>
            Tx(s, "Edit hair", tx => MakerEdits.EditHairBundle(tx, s.Model, s.Model.Find(id), part, index, move, rotate, noShake, reset, axis, Opts(s, data, multi), rep ?? new MakerEditReport()));

        static string Show(List<HairBundle> l) => string.Join("; ", l.Select(b => b.Index + " " + FieldJson.ToText(b.Move) + " " + FieldJson.ToText(b.Rotate) + (b.NoShake ? " ns" : "")));

        static void Bundles(TestRunner t)
        {
            var data = BundleData();
            var s = BundleScene(t, data, out string f, out string m);
            string[] ids = { f, m };
            var start = ids.Select(i => Bytes(s, i)).ToArray();
            var hair = Hair(s, f);
            var facts3 = FactsOf(data, Item3);
            t.Check(facts3 != null && facts3.Length == 3, "bundles: the fixture's back item has 3 bundles");
            // 1. materialization from an empty list
            var rep = new MakerEditReport();
            Kit.Ok(t, EB(s, f, data, 0, 1, move: new Vec3(0.1f, 0.2f, 0.3f), rep: rep), "bundles: edit bundle 1 of an empty list");
            var b = hair.Back.Bundles;
            t.Check(b.Count == 3 && b.Select(x => x.Index).SequenceEqual(new[] { 0, 1, 2 }) && b[1].Move.Equals(new Vec3(0.1f, 0.2f, 0.3f)) && b[1].Rotate.Equals(facts3[1].DefaultRotate) &&
                    b[0].Move.Equals(facts3[0].DefaultMove) && b[2].Rotate.Equals(facts3[2].DefaultRotate) && b.All(x => !x.NoShake),
                "bundles: the first edit materializes the full list (defaults) and sets bundle 1's move (" + Show(b) + ")");
            t.Check(rep.Changed.SequenceEqual(new[] { P("hair.back.bundles"), P("hair.back.bundles[1].move") }) && rep.Consequences.Count == 0,
                "bundles: changed = the materialized list and the edited member (" + string.Join(", ", rep.Changed) + ")");
            t.Equal(1, s.Commands.UndoList.Count, "bundles: one entry with the materialization");
            // 2. a valid list: edited in place, the other bundles keep their stored values
            Kit.Ok(t, EB(s, f, data, 0, 2, rotate: new Vec3(-0.5f, 1.5f, 2f)), "bundles: rotate of bundle 2 (out of range values)");
            t.Check(b == hair.Back.Bundles || hair.Back.Bundles.Count == 3, "bundles: the list stays");
            b = hair.Back.Bundles;
            t.Check(b[2].Rotate.Equals(new Vec3(-0.5f, 1.5f, 2f)) && b[1].Move.Equals(new Vec3(0.1f, 0.2f, 0.3f)), "bundles: stored unclamped; bundle 1 keeps its edit");
            // 3. noShake alone
            var rep3 = new MakerEditReport();
            Kit.Ok(t, EB(s, f, data, 0, 0, noShake: true, rep: rep3), "bundles: noShake of bundle 0");
            t.Check(hair.Back.Bundles[0].NoShake && rep3.Changed.SequenceEqual(new[] { P("hair.back.bundles[0].noShake") }), "bundles: noShake alone sets one member");
            // 4. a count mismatch: stored values discarded, defaults materialized
            Do(t, s.Set(f, P("hair.back.bundles"), new List<HairBundle> { new HairBundle { Index = 0, Move = Vec3.One }, new HairBundle { Index = 1, Move = Vec3.One } }), "bundles: a 2-entry list");
            Kit.Ok(t, EB(s, f, data, 0, 0, move: new Vec3(0.9f, 0.9f, 0.9f)), "bundles: edit after a count mismatch");
            b = hair.Back.Bundles;
            t.Check(b.Count == 3 && b[0].Move.Equals(new Vec3(0.9f, 0.9f, 0.9f)) && b[1].Move.Equals(facts3[1].DefaultMove) && b[2].Move.Equals(facts3[2].DefaultMove),
                "bundles: a count mismatch → the defaults are materialized, not the stored values (J6)");
            // 5. a duplicate index (right count): defaults too
            Do(t, s.Set(f, P("hair.back.bundles"), new List<HairBundle> { new HairBundle { Index = 0, Move = Vec3.One }, new HairBundle { Index = 1, Move = Vec3.One }, new HairBundle { Index = 1, Move = Vec3.One } }),
                "bundles: indices 0, 1, 1");
            Kit.Ok(t, EB(s, f, data, 0, 2, noShake: true), "bundles: noShake after a duplicate index");
            b = hair.Back.Bundles;
            t.Check(b.Count == 3 && b.Select(x => x.Index).SequenceEqual(new[] { 0, 1, 2 }) && b[1].Move.Equals(facts3[1].DefaultMove) && b[2].NoShake,
                "bundles: indices not 0..n−1 → defaults");
            // 6. a drag: three preview frames in one transaction → one entry; the first frame materializes
            Do(t, s.Set(f, P("hair.back.bundles"), new List<HairBundle>()), "bundles: cleared again");
            int depth = s.Commands.UndoList.Count;
            string before = Bytes(s, f);
            using (var tx = s.Commands.BeginOrJoin("Edit hair"))
            {
                for (int k = 1; k <= 3; k++)
                {
                    var r = MakerEdits.EditHairBundle(tx, s.Model, s.Model.Find(f), 0, 1, new Vec3(0.1f * k, 0.5f, 0.5f), null, null, false, -1, Opts(s, data), null);
                    if (!r.Ok && r.Status != CommandStatus.NoOp) t.Check(false, "bundles: drag frame " + k + " → " + r);
                }
                tx.Commit();
            }
            t.Check(s.Commands.UndoList.Count == depth + 1 && Math.Abs(hair.Back.Bundles[1].Move.X - 0.3f) < 1e-6f, "bundles: a drag of three frames is one entry with the last value");
            s.Commands.Undo();
            t.Check(Bytes(s, f) == before && hair.Back.Bundles.Count == 0, "bundles: undoing the drag restores the empty list byte for byte");
            s.Commands.Redo();
            // 7. errors
            foreach (var (part, index, axis, code, what) in new[]
            {
                (0, 3, -1, CommandCodes.NotFound, "index 3 of 3"), (0, -1, -1, CommandCodes.NotFound, "index −1"), (4, 0, -1, CommandCodes.BadArgs, "part 4"),
                (-1, 0, -1, CommandCodes.BadArgs, "part −1"), (0, 0, 3, CommandCodes.BadArgs, "axis 3"),
            })
            {
                var r = EB(s, f, data, part, index, move: Vec3.One, reset: axis == 3, axis: axis);
                t.Check(r.Status == CommandStatus.Error && r.Code == code, "bundles: " + what + " → " + code + " (" + r + ")");
            }
            var none = EB(s, f, data, 0, 0);
            t.Check(none.Status == CommandStatus.Error && none.Code == CommandCodes.BadArgs, "bundles: nothing given → bad_args");
            // a part whose prefab has no bundles: every index not_found
            Do(t, s.Set(f, P("hair.side.item"), Item0), "bundles: a side item without bundles");
            var r0 = EB(s, f, data, 2, 0, move: Vec3.One);
            t.Check(r0.Code == CommandCodes.NotFound, "bundles: a prefab without bundles → not_found");
            // multi-selection: a node whose index is out of range is skipped with a warning, the other is written
            Do(t, s.Set(m, P("hair.front.item"), Item3), "bundles: the male's front has 3 bundles");
            var repM = new MakerEditReport();
            Do(t, Tx(s, "Edit hair", tx =>
            {
                var o = Opts(s, data, true);
                var a = MakerEdits.EditHairBundle(tx, s.Model, s.Model.Find(f), 1, 4, new Vec3(0.2f, 0.2f, 0.2f), null, null, false, -1, o, repM);
                if (a.Status == CommandStatus.Error) return a;
                return MakerEdits.EditHairBundle(tx, s.Model, s.Model.Find(m), 1, 4, new Vec3(0.2f, 0.2f, 0.2f), null, null, false, -1, o, repM);
            }), "bundles: bundle 4 of the front hair on both characters");
            t.Check(Hair(s, f).Front.Bundles.Count == 5 && Hair(s, f).Front.Bundles[4].Move.Equals(new Vec3(0.2f, 0.2f, 0.2f)) && Hair(s, m).Front.Bundles.Count == 0 &&
                    repM.Warnings.Any(w => w.StartsWith("edit.bundleSkipped: node " + m, StringComparison.Ordinal)),
                "bundles: the male's 3-bundle front is skipped with edit.bundleSkipped (" + string.Join(", ", repM.Warnings) + ")");
            // an item change clears the bundles (4.0) and the next edit materializes the new prefab's list
            Kit.Ok(t, Tx(s, "Change item", tx => MakerEdits.Edit(tx, s.Model, s.Model.Find(f), new List<(string, object)> { (P("hair.front.item"), Item3F) }, Opts(s, data), null)), "bundles: front item change");
            t.Check(Hair(s, f).Front.Bundles.Count == 0, "bundles: the item change cleared the bundles");
            Kit.Ok(t, EB(s, f, data, 1, 2, noShake: true), "bundles: noShake on the new front");
            t.Check(Hair(s, f).Front.Bundles.Count == 3 && Hair(s, f).Front.Bundles[2].NoShake, "bundles: the new prefab's 3 bundles materialized");
            var end = ids.Select(i => Bytes(s, i)).ToArray();
            UndoRedo(t, s, ids, start, end, "bundles");
        }

        // ---------------------------------------------------------------- «髪の調整をまとめて行う» (C10, J5)

        static void Together(TestRunner t)
        {
            var data = BundleData();
            var s = BundleScene(t, data, out string f, out string m);
            string[] ids = { f, m };
            var hair = Hair(s, f);
            // a known starting list on the front (5 bundles, distinct values)
            var init = Enumerable.Range(0, 5).Select(i => new HairBundle { Index = i, Move = new Vec3(0.1f * i, 0.2f + 0.1f * i, 0.3f), Rotate = new Vec3(0.5f, 0.1f * i, 0.9f - 0.1f * i) }).ToList();
            Do(t, s.Set(f, P("hair.front.bundles"), init), "together: a stored 5-bundle list");
            Do(t, s.Set(f, P("hair.editor.ctrlTogether"), true), "together: on");
            s.Commands.ClearHistory();
            var start = ids.Select(i => Bytes(s, i)).ToArray();
            // move Y of bundle 2 changes: Y goes to every bundle, X / Z kept
            var cur = hair.Front.Bundles[2].Move;
            var rep = new MakerEditReport();
            Kit.Ok(t, EB(s, f, data, 1, 2, move: new Vec3(cur.X, 0.77f, cur.Z), rep: rep), "together: move Y of «調整03»");
            var b = hair.Front.Bundles;
            t.Check(b.All(x => x.Move.Y == 0.77f) && b.Select((x, i) => x.Move.X == init[i].Move.X && x.Move.Z == init[i].Move.Z).All(x => x) &&
                    b.Select((x, i) => x.Rotate.Equals(init[i].Rotate)).All(x => x), "together: only the edited component (Y) goes to every bundle; X, Z and the rotations kept (C10; " + Show(b) + ")");
            t.Equal(5, rep.Changed.Count, "together: five move sets (the edited bundle and four others)");
            // two components at once
            Kit.Ok(t, EB(s, f, data, 1, 0, move: new Vec3(0.66f, 0.77f, 0.11f)), "together: move X and Z of «調整01»");
            t.Check(b.All(x => x.Move.X == 0.66f && x.Move.Y == 0.77f && x.Move.Z == 0.11f), "together: both changed components propagate");
            // rotate apart from move
            Kit.Ok(t, EB(s, f, data, 1, 4, rotate: new Vec3(0.5f, 1.25f, b[4].Rotate.Z)), "together: rotate Y of «調整05» (unclamped)");
            t.Check(b.All(x => x.Rotate.Y == 1.25f) && b.Select((x, i) => x.Rotate.Z == init[i].Rotate.Z && x.Rotate.X == 0.5f).All(x => x) && b.All(x => x.Move.X == 0.66f),
                "together: rotate Y to every bundle, rotate Z per bundle kept, move untouched");
            // unchanged components do not propagate (equal value)
            var snap = FieldJson.ToText(hair.Front);
            Tx(s, "Edit hair", tx => MakerEdits.EditHairBundle(tx, s.Model, s.Model.Find(f), 1, 3, b[3].Move, b[3].Rotate, null, false, -1, Opts(s, data), null));
            t.Equal(snap, FieldJson.ToText(hair.Front), "together: a set of the current values changes nothing");
            // noShake never propagates; resets never propagate
            Kit.Ok(t, EB(s, f, data, 1, 1, noShake: true), "together: noShake of «調整02»");
            t.Check(b[1].NoShake && b.Where((x, i) => i != 1).All(x => !x.NoShake), "together: noShake does not propagate");
            var facts = FactsOf(data, Item5);
            Kit.Ok(t, EB(s, f, data, 1, 3, move: Vec3.Zero, reset: true, axis: 0), "together: ↺ of move X on «調整04»");
            t.Check(b[3].Move.X == facts[3].DefaultMove.X && b.Where((x, i) => i != 3).All(x => x.Move.X == 0.66f), "together: a reset does not propagate");
            // a drag with together: one entry
            int depth = s.Commands.UndoList.Count;
            using (var tx = s.Commands.BeginOrJoin("Edit hair"))
            {
                for (int k = 1; k <= 3; k++)
                    MakerEdits.EditHairBundle(tx, s.Model, s.Model.Find(f), 1, 0, null, new Vec3(0.1f * k, b[0].Rotate.Y, b[0].Rotate.Z), null, false, -1, Opts(s, data), null);
                tx.Commit();
            }
            t.Check(s.Commands.UndoList.Count == depth + 1 && b.All(x => Math.Abs(x.Rotate.X - 0.3f) < 1e-6f), "together: a three-frame drag of rotate X is one entry, every bundle at the last value");
            // switch off: the edit stays on its bundle
            Do(t, s.Set(f, P("hair.editor.ctrlTogether"), false), "together: off");
            Kit.Ok(t, EB(s, f, data, 1, 2, move: new Vec3(0.01f, 0.02f, 0.03f)), "together off: move of «調整03»");
            t.Check(b[2].Move.Equals(new Vec3(0.01f, 0.02f, 0.03f)) && b.Where((x, i) => i != 2).All(x => x.Move.Y == 0.77f), "together off: only the edited bundle");
            var end = ids.Select(i => Bytes(s, i)).ToArray();
            UndoRedo(t, s, ids, start, end, "together");
        }

        // ---------------------------------------------------------------- resets (6; J8)

        static void Resets(TestRunner t)
        {
            var data = BundleData();
            var s = BundleScene(t, data, out string f, out string m);
            string[] ids = { f, m };
            var hair = Hair(s, f);
            var facts = FactsOf(data, Item3);
            var init = Enumerable.Range(0, 3).Select(i => new HairBundle { Index = i, Move = new Vec3(0.9f, 0.8f, 0.7f), Rotate = new Vec3(0.1f, 0.2f, 0.3f), NoShake = i == 1 }).ToList();
            Do(t, s.Set(f, P("hair.back.bundles"), init), "resets: a stored list, bundle 1 noShake");
            s.Commands.ClearHistory();
            var start = ids.Select(i => Bytes(s, i)).ToArray();
            var b = hair.Back.Bundles;
            Kit.Ok(t, EB(s, f, data, 0, 0, move: Vec3.Zero, reset: true, axis: 1), "resets: ↺ of «移動Y» of «調整01»");
            t.Check(b[0].Move.Equals(new Vec3(0.9f, facts[0].DefaultMove.Y, 0.7f)) && b[0].Rotate.Equals(init[0].Rotate), "resets: one axis returns to the prefab default (J8), the others kept");
            Kit.Ok(t, EB(s, f, data, 0, 2, rotate: Vec3.One, reset: true), "resets: ↺ of the whole rotation of «調整03»");
            t.Check(b[2].Rotate.Equals(facts[2].DefaultRotate) && b[2].Move.Equals(init[2].Move), "resets: rotate → default, move kept (the value argument is ignored)");
            Kit.Ok(t, EB(s, f, data, 0, 1, reset: true), "resets: reset of «調整02» with nothing given");
            t.Check(b[1].Move.Equals(facts[1].DefaultMove) && b[1].Rotate.Equals(facts[1].DefaultRotate) && b[1].NoShake, "resets: nothing given → move and rotate, noShake kept");
            Kit.Ok(t, EB(s, f, data, 0, 1, noShake: true, reset: true), "resets: reset of noShake");
            t.Check(!b[1].NoShake, "resets: a given noShake returns to false");
            // «全て初期化»: the defaults, noShake kept
            Kit.Ok(t, EB(s, f, data, 0, 2, noShake: true), "resets: noShake on «調整03»");
            Kit.Ok(t, EB(s, f, data, 0, 0, move: new Vec3(1.5f, -0.5f, 0.4f)), "resets: an edit on «調整01»");
            var rep = new MakerEditReport();
            Kit.Ok(t, Tx(s, "Edit hair", tx => MakerEdits.ResetHairBundles(tx, s.Model, s.Model.Find(f), 0, Opts(s, data), rep)), "resets: «全て初期化»");
            b = hair.Back.Bundles;
            t.Check(b.Count == 3 && b.Select((x, i) => x.Index == i && x.Move.Equals(facts[i].DefaultMove) && x.Rotate.Equals(facts[i].DefaultRotate)).All(x => x) &&
                    b.Select(x => x.NoShake).SequenceEqual(new[] { false, false, true }) && rep.Changed.SequenceEqual(new[] { P("hair.back.bundles") }),
                "resets: «全て初期化» = one whole-list set of the defaults, each noShake kept (" + Show(b) + ")");
            int depth = s.Commands.UndoList.Count;
            Tx(s, "Edit hair", tx => MakerEdits.ResetHairBundles(tx, s.Model, s.Model.Find(f), 0, Opts(s, data), null));
            t.Equal(depth, s.Commands.UndoList.Count, "resets: resetting the defaults again records nothing");
            // from an incomplete list (noShake there is not effective: false)
            Do(t, s.Set(f, P("hair.front.bundles"), new List<HairBundle> { new HairBundle { Index = 0, NoShake = true } }), "resets: a 1-entry front list for a 5-bundle prefab");
            Kit.Ok(t, Tx(s, "Edit hair", tx => MakerEdits.ResetHairBundles(tx, s.Model, s.Model.Find(f), 1, Opts(s, data), null)), "resets: «全て初期化» of the front");
            t.Check(hair.Front.Bundles.Count == 5 && hair.Front.Bundles.All(x => !x.NoShake), "resets: an incomplete list resets to the full default list (its noShake was not effective)");
            var bp = Tx(s, "Edit hair", tx => MakerEdits.ResetHairBundles(tx, s.Model, s.Model.Find(f), 5, Opts(s, data), null));
            t.Check(bp.Code == CommandCodes.BadArgs, "resets: part 5 → bad_args");
            var end = ids.Select(i => Bytes(s, i)).ToArray();
            UndoRedo(t, s, ids, start, end, "resets");
        }

        // ---------------------------------------------------------------- plain Mono without facts

        static void NoFacts(TestRunner t)
        {
            var data = BundleData();
            data.NoBundleFacts = true;
            var s = BundleScene(t, data, out string f, out string m);
            var hair = Hair(s, f);
            var r1 = EB(s, f, data, 0, 0, move: Vec3.One);
            t.Check(r1.Code == CommandCodes.Unsupported, "no facts: an empty stored list → unsupported (" + r1 + ")");
            Do(t, s.Set(f, P("hair.back.bundles"), new List<HairBundle> { new HairBundle { Index = 1 }, new HairBundle { Index = 0 } }), "no facts: a stored list 0..1");
            Kit.Ok(t, EB(s, f, data, 0, 1, move: new Vec3(0.3f, 0.3f, 0.3f)), "no facts: an edit of a complete stored list");
            t.Check(hair.Back.Bundles.Count == 2 && hair.Back.Bundles.Single(x => x.Index == 1).Move.Equals(new Vec3(0.3f, 0.3f, 0.3f)), "no facts: edited in place (position = index)");
            var r2 = EB(s, f, data, 0, 2, move: Vec3.One);
            var r3 = EB(s, f, data, 0, 0, reset: true);
            var r4 = Tx(s, "Edit hair", tx => MakerEdits.ResetHairBundles(tx, s.Model, s.Model.Find(f), 0, Opts(s, data), null));
            var r5 = Tx(s, "Edit hair", tx => MakerEdits.EditHairBundle(tx, s.Model, s.Model.Find(f), 0, 0, Vec3.One, null, null, false, -1, new MakerEditOptions(), null));
            t.Check(r2.Code == CommandCodes.NotFound && r3.Code == CommandCodes.Unsupported && r4.Code == CommandCodes.Unsupported && r5.Ok,
                "no facts: index 2 not_found, resets unsupported, no defaults service at all still edits a complete list");
            var rep = new MakerEditReport();
            var r6 = EB(s, m, data, 0, 0, move: Vec3.One, rep: rep, multi: true);
            t.Check(r6.Status == CommandStatus.NoOp && rep.Warnings.Any(w => w.StartsWith("edit.bundleSkipped", StringComparison.Ordinal)), "no facts: a multi-selection skips the node with a warning");
            var doc = new SceneSession(new SessionOptions { Ids = new CountingIdSource(5) });
            string card = doc.AddChara(new CharaAppearance()).NewIds?.FirstOrDefault();
            if (card != null)
            {
                var r7 = Tx(doc, "Edit hair", tx => MakerEdits.EditHairBundle(tx, doc.Model, doc.Model.Find(card), 0, 0, Vec3.One, null, null, false, -1, Opts(doc, data), null));
                t.Check(r7.Code == CommandCodes.Unsupported, "no facts: a character without documents → unsupported");
            }
        }

        // ---------------------------------------------------------------- gates (J13)

        sealed class FakeHairInfo : IHairInfo
        {
            public readonly Dictionary<(string, int), HairPartFacts> Parts = new Dictionary<(string, int), HairPartFacts>();
            public bool TryPart(string nodeId, int part, out HairPartFacts facts) => Parts.TryGetValue((nodeId, part), out facts);
        }

        static void Gates(TestRunner t)
        {
            var hair = new HairData();
            hair.Editor.AutoSetting = true;
            Func<int, string, string> G = (p, feat) => MakerEdits.HairGatePrefix + Parts[p] + ":" + feat;
            // plain Mono (no service): every prefab feature holds; the document features follow the document
            t.Check(new[] { "prefab", "skin", "acs", "acs1", "acs2", "acs3", "bundles", "mesh" }.All(x => MakerEdits.HairGateHolds(G(0, x), "n", hair, null)),
                "gates: without the service every prefab feature holds");
            t.Check(!MakerEdits.HairGateHolds(G(0, "topRow"), "n", hair, null) && !MakerEdits.HairGateHolds(G(0, "underRow"), "n", hair, null) && !MakerEdits.HairGateHolds(G(0, "glossRow"), "n", hair, null),
                "gates: auto on hides the top / under / gloss rows");
            hair.Editor.AutoSetting = false;
            t.Check(MakerEdits.HairGateHolds(G(0, "topRow"), "n", hair, null) && MakerEdits.HairGateHolds(G(0, "glossRow"), "n", hair, null), "gates: auto off shows them");
            t.Check(!MakerEdits.HairGateHolds(G(1, "meshSet"), "n", hair, null), "gates: mesh pattern none → no mesh colour / layout rows");
            hair.Front.Mesh.Item = "illusion.hs2#10";
            t.Check(MakerEdits.HairGateHolds(G(1, "meshSet"), "n", hair, null) && !MakerEdits.HairGateHolds(G(0, "meshSet"), "n", hair, null), "gates: a pattern on the front shows its rows only");
            // with the service: facts of the realized parts
            var info = new FakeHairInfo();
            var bundles = HairBundleFacts.Number(new List<(bool, Vec3, Vec3, Vec3, Vec3, Vec3, Vec3)> { (false, Vec3.Zero, Vec3.One, Vec3.Zero, Vec3.One, Vec3.Zero, Vec3.Zero) });
            info.Parts[("n", 0)] = new HairPartFacts { HasPrefab = true, UseTopColor = true, UseUnderColor = false, UseMesh = true, UseAcsColor01 = true, UseAcsColor02 = true, AcsDefColorCount = 1, Bundles = bundles };
            info.Parts[("n", 1)] = new HairPartFacts { HasPrefab = true, UseSameSkinColorButton = true, UseMesh = true, Bundles = HairBundleFacts.Number(new List<(bool, Vec3, Vec3, Vec3, Vec3, Vec3, Vec3)> { (true, Vec3.Zero, Vec3.One, Vec3.Zero, Vec3.One, Vec3.Zero, Vec3.Zero) }) };
            info.Parts[("n", 2)] = HairPartFacts.None();
            Func<int, string, bool> H = (p, feat) => MakerEdits.HairGateHolds(G(p, feat), "n", hair, info);
            t.Check(H(0, "prefab") && H(0, "topRow") && !H(0, "underRow") && H(0, "glossRow") && !H(0, "skin") && H(0, "acs") && H(0, "acs1") && !H(0, "acs2") && !H(0, "acs3") &&
                    !H(0, "bundles") && H(0, "mesh") && !H(0, "meshSet"),
                "gates: back = flags (top, no under), acs2 hidden beyond the acsDefColor count (GH5), a bundle without a correction transform shows no section, mesh without a pattern");
            t.Check(H(1, "skin") && !H(1, "topRow") && !H(1, "acs") && H(1, "bundles") && H(1, "meshSet"), "gates: front = skin match, no top colour, no decoration, bundles, a pattern set");
            t.Check(new[] { "prefab", "topRow", "glossRow", "skin", "acs", "bundles", "mesh", "meshSet" }.All(x => !H(2, x)) && new[] { "prefab", "glossRow" }.All(x => !H(3, x)),
                "gates: a part without a prefab (None) or unknown to the service (no realization) has no rows but its style list");
            t.Check(!MakerEdits.HairGateHolds(G(0, "prefab"), null, hair, info) && !MakerEdits.HairGateHolds("maker.hair:back:nonsense", "n", hair, null) && !MakerEdits.HairGateHolds("maker.list:300", "n", hair, null),
                "gates: no node → none; unknown features → false");
            hair.Editor.AutoSetting = true;
            t.Check(!H(0, "topRow") && !H(0, "glossRow") && H(0, "prefab"), "gates: auto back on hides the derived rows again (live gate)");
            t.Check(MakerEdits.HairGateHolds(G(0, "prefab"), "n", null, null) && !MakerEdits.HairGateHolds(G(0, "topRow"), "n", null, null), "gates: no document → the format defaults (auto on)");
            // through GateHolds (StudioOps.MakerGateHolds' path)
            t.Check(MakerEdits.GateHolds(G(0, "acs1"), 1, null, false, null, "n", hair, info) && !MakerEdits.GateHolds(G(0, "acs2"), 1, null, false, null, "n", hair, info) &&
                    MakerEdits.GateHolds(G(0, "acs2"), 1, null, false) && MakerEdits.GateHolds("maker.bodyHeight", 0, null, false, null, "n", hair, info),
                "gates: GateHolds delegates the hair gates (without context: plain Mono's open gates) and keeps the others");
            t.Check(MakerEdits.HairPartIndex("option") == 3 && MakerEdits.HairPartIndex("top") == -1 && MakerEdits.HairPartIndex(null) == -1, "gates: HairPartIndex");
        }
    }
}
