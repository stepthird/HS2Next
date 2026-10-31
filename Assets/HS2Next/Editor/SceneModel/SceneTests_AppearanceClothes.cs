// The Maker ops of the clothes page (Phase 4.4; the Unity-free cores in MakerEdits / SetCoordinateCommand, which
// StudioOps.Maker wraps):
//   «初期カラー»: the kind's three colour sets ← the fixture prefab's defaults as one set; bad kind → bad_args; no facts →
//     unsupported, on a multi-selection a skip with edit.defaultsUnavailable;
//   ↺ of gloss / metallic / layout (whole and one component) / rotation → the prefab default, colours and other members →
//     the reference value; without facts the reference with edit.defaultsUnavailable;
//   coordinate load in the three modes: what each mode replaces and keeps (clothes parts with KCOX / ME / pushUp,
//     accessories, rules, name, inline ext, the coordinate-scope ext data with hs2.kkex), no item-change consequences (raw
//     sets), the accessory visibility map filled / pruned (the normalizer has nothing left to do), coord.extKept, the
//     resources copied, a source of the other sex refused (multi: skipped), a repeated load a no-op, the change events
//     (member paths only, never the whole coordinate or a rebuild member, no appearance replacement: the realization swaps
//     parts and does not rebuild in any mode);
//   gates: maker.clothes:<kind>:<feature> on fixture facts (no service: prefab features hold; male kinds without a list:
//     nothing; one-piece tops disable the bottom kind; the pattern rows follow the stored pattern);
//   undo / redo of each restores the documents (and the ext data) byte for byte.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Api.Save;
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_AppearanceClothes
    {
        public static void Run(TestRunner t)
        {
            DefaultColours(t);
            PrefabResets(t);
            CoordinateClothes(t);
            CoordinateAccessories(t);
            CoordinateAll(t);
            CoordinateRules(t);
            CoordinateSexOfCard(t);
            Gates(t);
        }

        static string K(string m) => AppearancePaths.Coord(m);

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

        static void Do(TestRunner t, CommandResult r, string what) => t.Check(r != null && r.Ok, what + (r == null ? " (null result)" : r.Ok ? "" : " (" + r + ")"));

        static CharaSubtree St(SceneSession s, string id) => s.Model.Find(id).Appearance.Stored;
        static CoordDocument Coord(SceneSession s, string id) => St(s, id).Coord;
        static string Doc(DocObject o) => o == null ? "null" : System.Text.Encoding.UTF8.GetString(DocClone.Bytes(o));
        static string Accs(CoordDocument c) => string.Join("\n", c.Accessories.Select(Doc));
        static string State(SceneSession s, string id) => string.Join(",", ((CharaNodeState)s.Model.Find(id).Doc.State).State.Accessories.Select(kv => kv.Key + "=" + kv.Value));
        static string Full(SceneSession s, string id) =>
            Kit.Hash(SceneTests_Appearance.CharaBytes(s, id)) + "/" + Kit.Hash(SceneTests_Appearance.CoordBytes(s, id)) + "/" + SetCoordinateCommand.ExtFingerprint(St(s, id).CoordExt) + "/" + State(s, id);

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

        static void UndoRedo(TestRunner t, SceneSession s, string[] ids, string[] start, string[] end, string what)
        {
            int depth = s.Commands.UndoList.Count;
            while (s.Commands.CanUndo) s.Commands.Undo();
            t.Check(ids.Select(i => Full(s, i)).SequenceEqual(start), what + ": undo all restores the documents, the ext data and the visibility map byte for byte");
            while (s.Commands.CanRedo) s.Commands.Redo();
            t.Check(s.Commands.UndoList.Count == depth && ids.Select(i => Full(s, i)).SequenceEqual(end), what + ": redo all gives the end state byte for byte");
        }

        // ---------------------------------------------------------------- «初期カラー»

        static void DefaultColours(TestRunner t)
        {
            var data = new FakeMakerData();
            var s = TwoCharas(t, out string f, out string m);
            string[] ids = { f, m };
            // a top row with facts for the female, a socks row without (hash % 8 == 0) for the male
            string withFacts = FakeMakerData.Pool(240).First(r => data.TryClothesColors(240, r, out _));
            string without = FakeMakerData.Pool(246).First(r => !data.TryClothesColors(246, r, out _));
            Do(t, s.Set(f, K("clothes.top.item"), withFacts), "colours: the female top is a row with defaults");
            Do(t, s.Set(m, K("clothes.socks.item"), without), "colours: the male socks a row without");
            s.Commands.ClearHistory();
            var start = ids.Select(i => Full(s, i)).ToArray();
            data.TryClothesColors(240, withFacts, out var cols);
            var rep = new MakerEditReport();
            var r = Tx(s, "Edit clothes", tx => MakerEdits.ResetClothesColours(tx, s.Model, s.Model.Find(f), 0, Opts(s, data), rep));
            Do(t, r, "colours: «初期カラー» of the top");
            var top = Coord(s, f).Clothes.Parts[0];
            t.Check(Enumerable.Range(0, 3).All(n => Doc(top.Colors[n]) == Doc(cols[n])) && rep.Changed.SequenceEqual(new[] { K("clothes.top.colors") }) && rep.Consequences.Count == 0,
                "colours: the top's three sets = the prefab defaults (main, pattern, pattern colour, gloss, metallic, layout, rotation), one whole-array set");
            t.Equal(1, s.Commands.UndoList.Count, "colours: one history entry");
            t.Equal("Edit clothes", s.Commands.UndoList.Last().Label, "colours: labelled Edit clothes");
            var before = Doc(Coord(s, m).Clothes.Parts[6]);
            var e = Tx(s, "Edit clothes", tx => MakerEdits.ResetClothesColours(tx, s.Model, s.Model.Find(m), 6, Opts(s, data), new MakerEditReport()));
            t.Check(e != null && e.Status == CommandStatus.Error && e.Code == CommandCodes.Unsupported && Doc(Coord(s, m).Clothes.Parts[6]) == before, "colours: no prefab defaults → unsupported, nothing changed");
            var rep2 = new MakerEditReport();
            var multi = Tx(s, "Edit clothes", tx =>
            {
                var a = MakerEdits.ResetClothesColours(tx, s.Model, s.Model.Find(f), 6, Opts(s, data, true), rep2);
                return a.Status == CommandStatus.Error ? a : MakerEdits.ResetClothesColours(tx, s.Model, s.Model.Find(m), 6, Opts(s, data, true), rep2);
            });
            t.Check(multi != null && multi.Status != CommandStatus.Error && rep2.Warnings.Any(w => w.StartsWith("edit.defaultsUnavailable: node " + m, StringComparison.Ordinal)),
                "colours: on a multi-selection a character without defaults is skipped with edit.defaultsUnavailable");
            var bad = Tx(s, "Edit clothes", tx => MakerEdits.ResetClothesColours(tx, s.Model, s.Model.Find(f), 8, Opts(s, data), null));
            t.Check(bad != null && bad.Code == CommandCodes.BadArgs, "colours: kind 8 → bad_args");
            var nodefaults = Tx(s, "Edit clothes", tx => MakerEdits.ResetClothesColours(tx, s.Model, s.Model.Find(f), 0, new MakerEditOptions(), null));
            t.Check(nodefaults != null && nodefaults.Code == CommandCodes.Unsupported, "colours: plain Mono without a defaults service → unsupported");
            UndoRedo(t, s, ids, start, ids.Select(i => Full(s, i)).ToArray(), "colours");
        }

        // ---------------------------------------------------------------- ↺ to the prefab defaults

        static void PrefabResets(TestRunner t)
        {
            var data = new FakeMakerData();
            var s = TwoCharas(t, out string f, out string m);
            string item = FakeMakerData.Pool(241).First(r => data.TryClothesColors(241, r, out _));
            Do(t, s.Set(f, K("clothes.bottom.item"), item), "resets: the bottom is a row with defaults");
            var refs = s.MakerReferences;
            refs.Capture(f, St(s, f));
            // move every member away from both the reference and the prefab default
            Do(t, s.Set(f, K("clothes.bottom.colors[1].gloss"), 1.75f), "resets: gloss");
            Do(t, s.Set(f, K("clothes.bottom.colors[1].metallic"), -0.5f), "resets: metallic");
            Do(t, s.Set(f, K("clothes.bottom.colors[2].layout"), new Vec4(1.5f, -0.25f, 0.125f, 1.25f)), "resets: layout");
            Do(t, s.Set(f, K("clothes.bottom.colors[0].layout"), new Vec4(0.5f, 0.5f, 0.5f, 0.5f)), "resets: layout of set 1");
            Do(t, s.Set(f, K("clothes.bottom.colors[0].rotation"), 1.9f), "resets: rotation");
            Do(t, s.Set(f, K("clothes.bottom.colors[0].base"), new Color4(0.1f, 0.2f, 0.3f, 0.4f)), "resets: main colour");
            Do(t, s.Set(f, K("clothes.bottom.breakRate"), 0.8f), "resets: break");
            s.Commands.ClearHistory();
            var start = new[] { Full(s, f) };
            data.TryClothesColors(241, item, out var cols);
            var refCoord = refs.TryGet(f, K("clothes.bottom.colors[0].base"), out var refBase) ? refBase : null;
            var paths = new[]
            {
                K("clothes.bottom.colors[1].gloss"), "coord.clothes.bottom.colors[1].metallic", K("clothes.bottom.colors[2].layout") + "#2", K("clothes.bottom.colors[0].layout"),
                K("clothes.bottom.colors[0].rotation"), K("clothes.bottom.colors[0].base"),
            };
            var rep = new MakerEditReport();
            var r = Tx(s, "Reset value", tx => MakerEdits.ResetAppearanceWithPrefabDefaults(tx, s.Model, s.Model.Find(f), refs, paths, null, Opts(s, data), rep));
            Do(t, r, "resets: one call over six rows");
            var bot = Coord(s, f).Clothes.Parts[1];
            t.Check(bot.Colors[1].Gloss == cols[1].Gloss && bot.Colors[1].Metallic == cols[1].Metallic, "resets: «ツヤ» / «質感» → the prefab's defGloss / defMetallic of their set (also a relative path)");
            t.Check(bot.Colors[2].Layout.Equals(new Vec4(1.5f, -0.25f, cols[2].Layout.Z, 1.25f)), "resets: a layout component → that component of the prefab layout, the others kept (" + bot.Colors[2].Layout + ")");
            t.Check(bot.Colors[0].Layout.Equals(cols[0].Layout) && bot.Colors[0].Rotation == cols[0].Rotation, "resets: the whole layout and «回転» → the prefab's");
            t.Check(refCoord is Color4 rc && bot.Colors[0].Base.Equals(rc), "resets: the main colour → the reference value (colours keep the reference reset)");
            t.Check(bot.BreakRate == 0.8f && !rep.Warnings.Any(), "resets: other members untouched, no warnings");
            t.Equal(1, s.Commands.UndoList.Count, "resets: one history entry");
            // without the prefab's defaults: the reference value, warned
            Do(t, s.Set(f, K("clothes.bottom.colors[1].gloss"), 1.25f), "resets: gloss moved again");
            var rep2 = new MakerEditReport();
            var r2 = Tx(s, "Reset value", tx => MakerEdits.ResetAppearanceWithPrefabDefaults(tx, s.Model, s.Model.Find(f), refs, new[] { K("clothes.bottom.colors[1].gloss") }, null, new MakerEditOptions(), rep2));
            refs.TryGet(f, K("clothes.bottom.colors[1].gloss"), out var refGloss);
            t.Check(r2.Ok && Coord(s, f).Clothes.Parts[1].Colors[1].Gloss == (float)refGloss && rep2.Warnings.Any(w => w.StartsWith("edit.defaultsUnavailable", StringComparison.Ordinal)),
                "resets: without the prefab defaults the reference value, warned edit.defaultsUnavailable");
            // a non-clothes path and a malformed one behave as ResetAppearance
            var r3 = Tx(s, "Reset value", tx => MakerEdits.ResetAppearanceWithPrefabDefaults(tx, s.Model, s.Model.Find(f), refs, new[] { K("clothes.bottom.colors[1].gloss") + "#1" }, null, Opts(s, data), null));
            t.Check(r3 != null && r3.Code == CommandCodes.BadArgs, "resets: a component of a scalar member → bad_args (as ResetAppearance)");
            t.Check(MakerEdits.TryClothesPrefabDefaultMember(K("clothes.socks.colors[2].rotation"), out int k, out int n, out string mem) && k == 6 && n == 2 && mem == "rotation" &&
                    !MakerEdits.TryClothesPrefabDefaultMember(K("clothes.socks.colors[2].base"), out _, out _, out _) &&
                    !MakerEdits.TryClothesPrefabDefaultMember(K("clothes.socks.breakRate"), out _, out _, out _) &&
                    !MakerEdits.TryClothesPrefabDefaultMember(K("clothes.socks.colors[3].gloss"), out _, out _, out _) &&
                    !MakerEdits.TryClothesPrefabDefaultMember(AppearancePaths.Chara("hair.back.metallic"), out _, out _, out _),
                "resets: the prefab-default members are gloss, metallic, layout and rotation of colors[0..2]");
            UndoRedo(t, s, new[] { f }, start, new[] { Full(s, f) }, "resets");
        }

        // ---------------------------------------------------------------- coordinate load

        static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0, 0x1F, 0x15, 0xC4, 0x89 };

        /// <summary>A source coordinate of <paramref name="sex"/> unlike the golden one in every part: other items and colours,
        /// a KCOX image in `res`, one kept and one new accessory, other rules, name and inline ext; its ext data carries a
        /// plugin block and an hs2.kkex store.</summary>
        static CoordDocument Source(TestRunner t, int sex, ResStore res, out ExtScopeData ext, out string image, out string keptAcc, out string newAcc)
        {
            var c = CharaSubtree.CloneCoord(Kit.GoldenContainer(t, true).Charas[Kit.GoldenChara].Coord);
            c.Sex = sex;
            c.Name = "Source outfit";
            image = res.Add(Png);
            for (int k = 0; k < ClothesSet.Count; k++)
            {
                var p = c.Clothes.Parts[k];
                p.Item = k == 1 ? DocDefaults.NoneRef : "illusion.hs2#" + (10 + k);
                p.Colors[0].Base = new Color4(0.25f, 0.5f, 0.75f, 0.5f);
                p.Colors[2].Gloss = 1.5f;
                p.BreakRate = 0.25f * (k % 3);
                p.HideOptions = new[] { k % 2 == 0, false };
            }
            c.Clothes.Parts[0].Overlays = new ClothesOverlaySet { Main = new OverlayEntry { Image = image, Blend = 1 } };
            keptAcc = c.Accessories[0].Id;
            newAcc = JsonText.DerivedId("clothes-test/new-accessory");
            var extra = DocClone.Clone(c.Accessories[1]);
            extra.Id = newAcc;
            extra.Item = "illusion.hs2#21";
            c.Accessories = new List<AccessoryEntry> { c.Accessories[0], extra };
            c.Accessories[0].Parent = "N_Neck";
            c.RuleOverrides = null;
            c.Ext = new ExtInline();
            c.Ext.Set("example.plugin", new Newtonsoft.Json.Linq.JArray(4, 5));
            ext = new ExtScopeData(ExtScope.Coord);
            var b = ext.GetOrAdd("example.coordplugin");
            b.Version = 2;
            b.Files["data.bin"] = new byte[] { 1, 2, 3 };
            ext.Kkex = new KkexStore { Source = "hs2.coord", BlockVersion = "3" };
            ext.Kkex.Add("KCOX", "kkex", null, 1, "coord", "source", null, new byte[] { 0x92, 1, 0x80 });
            return c;
        }

        /// <summary>The character's own coordinate-scope ext data before a load: another kkex store (the "kept" side).</summary>
        static void OwnExt(SceneSession s, string id)
        {
            var st = St(s, id);
            st.CoordExt.Kkex = new KkexStore { Source = "hs2.coord", BlockVersion = "3" };
            st.CoordExt.Kkex.Add("com.deathweasel.bepinex.materialeditor", "kkex", null, 0, "coord", "source", null, new byte[] { 0x92, 0, 0x80 });
        }

        sealed class Events
        {
            public readonly List<SceneChange> All = new List<SceneChange>();
            public void On(SceneChangeBatch b) { All.AddRange(b.Changes); }
            public IEnumerable<string> Paths(string id) => All.Where(c => c.NodeId == id && c.Kind == SceneChangeKind.NodeChanged).Select(c => c.Path);
        }

        /// <summary>The realization rebuilds a character on an appearance replacement or a change of a rebuild-only member (the
        /// whole coordinate path includes its sex): none may come from a coordinate load in any mode.</summary>
        static void NoRebuild(TestRunner t, Events ev, string id, string what)
        {
            bool replaced = ev.All.Any(c => c.NodeId == id && c.Kind == SceneChangeKind.AppearanceChanged);
            var rebuild = ev.Paths(id).Where(p => p == AppearancePaths.Root || p == AppearancePaths.CoordPrefix || p == AppearancePaths.CharaPrefix ||
                AppearancePaths.RebuildPaths.Any(r => FieldPath.Overlaps(r, p))).ToList();
            t.Check(!replaced && rebuild.Count == 0, what + ": no appearance replacement and no rebuild member or whole document among the changes (rebuildRequested false; " +
                string.Join(", ", rebuild) + ")");
        }

        static CommandResult Load(SceneSession s, string id, CoordDocument src, ExtScopeData ext, CoordinateApplyMode mode, MakerEditOptions o, MakerEditReport rep) =>
            Tx(s, "Load coordinate", tx => MakerEdits.ApplyCoordinate(tx, s.Model, s.Model.Find(id), src, ext, mode, o, rep));

        static void CoordinateClothes(TestRunner t)
        {
            var data = new FakeMakerData();
            var s = TwoCharas(t, out string f, out string m);
            OwnExt(s, f);
            var res = new ResStore();
            var src = Source(t, 1, res, out var ext, out string image, out _, out _);
            s.Commands.ClearHistory();
            var before = Coord(s, f);
            string accBefore = Accs(before), rulesBefore = Doc(before.RuleOverrides), nameBefore = before.Name, extInlineBefore = before.Ext.ToString(), stateBefore = State(s, f);
            string kkexBefore = SetCoordinateCommand.ExtFingerprint(St(s, f).CoordExt);
            var start = new[] { Full(s, f) };
            t.Equal(1, MakerEdits.CopyCoordinateResources(s.Model, src, res), "clothes mode: the KCOX image the coordinate references is copied into the scene's store");
            t.Check(s.Model.File.Res.Contains(image) && MakerEdits.CopyCoordinateResources(s.Model, src, res) == 0, "clothes mode: present once (a second copy adds nothing)");
            var ev = new Events();
            s.Events.Changed += ev.On;
            var rep = new MakerEditReport();
            CommandResult r;
            try { r = Load(s, f, src, ext, CoordinateApplyMode.Clothes, Opts(s, data), rep); }
            finally { s.Events.Changed -= ev.On; }
            Do(t, r, "clothes mode: load");
            var c = Coord(s, f);
            t.Check(Enumerable.Range(0, 8).All(k => Doc(c.Clothes.Parts[k]) == Doc(src.Clothes.Parts[k])),
                "clothes mode: the eight parts = the source's (items, colours, break, options, KCOX overlays, ME items, pushUp)");
            t.Check(rep.Consequences.Count == 0 && Doc(c.Clothes.Parts[2].Colors[0]) == Doc(src.Clothes.Parts[2].Colors[0]),
                "clothes mode: raw sets — no item-change consequence overwrote the source's colours (the defaults service was there)");
            t.Check(Accs(c) == accBefore && Doc(c.RuleOverrides) == rulesBefore && c.Ext.ToString() == extInlineBefore && State(s, f) == stateBefore &&
                    SetCoordinateCommand.ExtFingerprint(St(s, f).CoordExt) == kkexBefore,
                "clothes mode: accessories, rules, inline ext, the visibility map and the coordinate's ext data (hs2.kkex) kept");
            t.Check(c.Name == src.Name && nameBefore != src.Name, "clothes mode: the name follows the coordinate (HS2's three buttons all take the file's name)");
            t.Check(rep.Warnings.Any(w => w.StartsWith(MakerEdits.CoordExtKept + ":", StringComparison.Ordinal)), "clothes mode: the finding coord.extKept");
            var changed = ev.Paths(f).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            var expect = Enumerable.Range(0, 8).Where(k => Doc(src.Clothes.Parts[k]) != Doc(Kit.GoldenContainer(t, true).Charas[Kit.GoldenChara].Coord.Clothes.Parts[k]))
                .Select(k => K("clothes." + ClothesSet.Names[k])).Concat(new[] { K("name") }).OrderBy(x => x, StringComparer.Ordinal).ToList();
            t.Check(changed.SequenceEqual(expect), "clothes mode: one change per part that differs and the name (" + string.Join(", ", changed) + ")");
            NoRebuild(t, ev, f, "clothes mode");
            t.Check(s.Commands.UndoList.Count == 1 && s.Commands.UndoList.Last().Label == "Load coordinate", "clothes mode: one entry Load coordinate");
            var end = new[] { Full(s, f) };
            s.Commands.Undo();
            t.Check(Full(s, f) == start[0] && SetCoordinateCommand.ExtFingerprint(St(s, f).CoordExt) == kkexBefore, "clothes mode: undo restores the documents; the kkex bytes equal before and after");
            s.Commands.Redo();
            UndoRedo(t, s, new[] { f }, start, end, "clothes mode");
        }

        static void CoordinateAccessories(TestRunner t)
        {
            var data = new FakeMakerData();
            var s = TwoCharas(t, out string f, out string m);
            var res = new ResStore();
            var src = Source(t, 1, res, out var ext, out _, out string kept, out string fresh);
            var golden = Coord(s, f);
            string gone = golden.Accessories.Select(a => a.Id).First(i => i != kept);
            Do(t, s.Set(f, MakerEdits.StateAccessories + "[" + FieldPath.Quote(kept) + "]", false), "accessories mode: the kept accessory hidden in the scene");
            s.Commands.ClearHistory();
            string clothesBefore = Doc(golden.Clothes), kkexBefore = SetCoordinateCommand.ExtFingerprint(St(s, f).CoordExt);
            var start = new[] { Full(s, f) };
            var ev = new Events();
            s.Events.Changed += ev.On;
            var rep = new MakerEditReport();
            CommandResult r;
            try { r = Load(s, f, src, ext, CoordinateApplyMode.Accessories, Opts(s, data), rep); }
            finally { s.Events.Changed -= ev.On; }
            Do(t, r, "accessories mode: load");
            var c = Coord(s, f);
            var map = ((CharaNodeState)s.Model.Find(f).Doc.State).State.Accessories;
            t.Check(Accs(c) == Accs(src) && c.RuleOverrides == null, "accessories mode: the accessory list and the rules (none) = the source's");
            t.Check(Doc(c.Clothes) == clothesBefore && SetCoordinateCommand.ExtFingerprint(St(s, f).CoordExt) == kkexBefore && c.Name == src.Name,
                "accessories mode: clothes and ext data kept, the name follows the coordinate");
            t.Check(map.Count == 2 && map[kept] == false && map[fresh] && !map.ContainsKey(gone),
                "accessories mode: the visibility map pruned of the gone id, filled (shown) for the new one, the kept id keeps its hidden state (" + State(s, f) + ")");
            t.Check(AccessoryNormalizer.Normalized(map, AccessoryNormalizer.KnownIds(s.Model.Find(f))) == null, "accessories mode: the normalizer has nothing left to do");
            t.Check(rep.Warnings.Any(w => w.StartsWith(MakerEdits.CoordExtKept + ":", StringComparison.Ordinal)) && rep.Changed.Contains(AppearancePaths.AccessoryList) &&
                    rep.Changed.Contains(K("ruleOverrides")), "accessories mode: coord.extKept; the list and the rules changed");
            NoRebuild(t, ev, f, "accessories mode");
            t.Check(s.Commands.UndoList.Count == 1, "accessories mode: one entry");
            UndoRedo(t, s, new[] { f }, start, new[] { Full(s, f) }, "accessories mode");
        }

        static void CoordinateAll(TestRunner t)
        {
            var data = new FakeMakerData();
            var s = TwoCharas(t, out string f, out string m);
            OwnExt(s, f);
            var res = new ResStore();
            var src = Source(t, 1, res, out var ext, out _, out string kept, out string fresh);
            s.Commands.ClearHistory();
            var start = new[] { Full(s, f) };
            string kkexBefore = SetCoordinateCommand.ExtFingerprint(St(s, f).CoordExt);
            var coordObject = Coord(s, f);
            var ev = new Events();
            s.Events.Changed += ev.On;
            var rep = new MakerEditReport();
            CommandResult r;
            try { r = Load(s, f, src, ext, CoordinateApplyMode.All, Opts(s, data), rep); }
            finally { s.Events.Changed -= ev.On; }
            Do(t, r, "all mode: load");
            var c = Coord(s, f);
            var want = CharaSubtree.CloneCoord(src);
            want.Sex = 1;
            t.Check(ReferenceEquals(c, coordObject), "all mode: the character keeps its coordinate document object (the built editor reads it), its contents replaced");
            t.Check(Kit.Hash(SceneTests_Appearance.CoordBytes(s, f)) == Kit.Hash(CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, want.Write)),
                "all mode: the coordinate = the source's byte for byte (name, clothes, accessories, rules, inline ext), the sex the character's");
            t.Check(SetCoordinateCommand.ExtFingerprint(St(s, f).CoordExt) == SetCoordinateCommand.ExtFingerprint(ext) && kkexBefore != SetCoordinateCommand.ExtFingerprint(ext),
                "all mode: the coordinate-scope ext data (plugin block, hs2.kkex) replaced by the source's");
            var map = ((CharaNodeState)s.Model.Find(f).Doc.State).State.Accessories;
            t.Check(map.Count == 2 && map.ContainsKey(kept) && map[fresh] && AccessoryNormalizer.Normalized(map, AccessoryNormalizer.KnownIds(s.Model.Find(f))) == null,
                "all mode: the visibility map follows the new list (normalizer: nothing to do)");
            var paths = ev.Paths(f).Where(p => !p.StartsWith(MakerEdits.StateAccessories, StringComparison.Ordinal)).Distinct().ToList();
            t.Check(paths.All(p => SetCoordinateCommand.MemberPaths.Contains(p) || p == SetCoordinateCommand.ExtPath) && paths.Contains(K("clothes")) && paths.Contains(K("accessories")) &&
                    paths.Contains(K("name")) && paths.Contains(K("ruleOverrides")) && paths.Contains(SetCoordinateCommand.ExtPath),
                "all mode: the member paths name, clothes, accessories, ruleOverrides and the ext path are reported (" + string.Join(", ", paths) + ")");
            NoRebuild(t, ev, f, "all mode");
            t.Check(!rep.Warnings.Any(w => w.StartsWith(MakerEdits.CoordExtKept, StringComparison.Ordinal)) && s.Commands.UndoList.Count == 1, "all mode: no coord.extKept, one entry");
            t.Check(rep.Changed.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(paths.OrderBy(x => x, StringComparer.Ordinal)),
                "all mode: the report's changed paths = the paths the command reported (" + string.Join(", ", rep.Changed) + ")");
            var end = new[] { Full(s, f) };
            s.Commands.Undo();
            t.Check(Full(s, f) == start[0] && SetCoordinateCommand.ExtFingerprint(St(s, f).CoordExt) == kkexBefore && ReferenceEquals(Coord(s, f), coordObject),
                "all mode: undo restores the coordinate, the visibility map and the kkex bytes (same document object)");
            s.Commands.Redo();
            t.Check(Full(s, f) == end[0], "all mode: redo replaces them again");
            // a second load of the same coordinate changes nothing: no entry
            var again = Load(s, f, src, ext, CoordinateApplyMode.All, Opts(s, data), new MakerEditReport());
            t.Check(again != null && again.Status == CommandStatus.NoOp && s.Commands.UndoList.Count == 1, "all mode: loading the same coordinate again is a no-op (no entry)");
            // only the ext data differ: the ext path alone
            var ext2 = ext.Clone();
            ext2.GetOrAdd("example.coordplugin").Files["data.bin"] = new byte[] { 9 };
            var ev2 = new Events();
            s.Events.Changed += ev2.On;
            try { Do(t, Load(s, f, src, ext2, CoordinateApplyMode.All, Opts(s, data), new MakerEditReport()), "all mode: the same coordinate with other ext data"); }
            finally { s.Events.Changed -= ev2.On; }
            t.Check(ev2.Paths(f).Distinct().SequenceEqual(new[] { SetCoordinateCommand.ExtPath }), "all mode: only the ext data changed → only the ext path is reported");
            // the command on its own: the sex kept, another sex refused, a missing source refused
            var male = Source(t, 0, new ResStore(), out var mext, out _, out _, out _);
            var bad = s.Commands.Execute(new SetCoordinateCommand(f, male, mext));
            t.Check(bad.Status == CommandStatus.Error && bad.Code == CommandCodes.BadArgs, "all mode: SetCoordinateCommand refuses a coordinate of the other sex");
            t.Check(s.Commands.Execute(new SetCoordinateCommand(f, null, null)).Code == CommandCodes.BadArgs, "all mode: SetCoordinateCommand without a coordinate → bad_args");
            Do(t, s.Commands.Execute(new SetCoordinateCommand(m, male, null)), "all mode: the male takes a male coordinate");
            t.Check(Coord(s, m).Sex == 0 && St(s, m).CoordExt.IsEmpty, "all mode: the male keeps sex 0; no ext data → empty ext data");
        }

        static void CoordinateRules(TestRunner t)
        {
            var data = new FakeMakerData();
            var s = TwoCharas(t, out string f, out string m);
            var src = Source(t, 1, new ResStore(), out var ext, out _, out _, out _);
            s.Commands.ClearHistory();
            var mb = Full(s, m);
            foreach (var mode in new[] { CoordinateApplyMode.Clothes, CoordinateApplyMode.Accessories, CoordinateApplyMode.All })
            {
                var r = Load(s, m, src, ext, mode, Opts(s, data), new MakerEditReport());
                t.Check(r != null && r.Code == CommandCodes.BadArgs && Full(s, m) == mb, "rules: a female coordinate on a male → bad_args, nothing changed (" + mode + ")");
            }
            var rep = new MakerEditReport();
            var multi = Tx(s, "Load coordinate", tx =>
            {
                var a = MakerEdits.ApplyCoordinate(tx, s.Model, s.Model.Find(f), src, ext, CoordinateApplyMode.All, Opts(s, data, true), rep);
                return a.Status == CommandStatus.Error ? a : MakerEdits.ApplyCoordinate(tx, s.Model, s.Model.Find(m), src, ext, CoordinateApplyMode.All, Opts(s, data, true), rep);
            });
            t.Check(multi != null && multi.Ok && rep.Warnings.Any(w => w.StartsWith("edit.coordinateSkipped: node " + m, StringComparison.Ordinal)) && Full(s, m) == mb && Coord(s, f).Name == src.Name,
                "rules: on a multi-selection the other sex is skipped with edit.coordinateSkipped, the female wears it");
            t.Check(Tx(s, "x", tx => MakerEdits.ApplyCoordinate(tx, s.Model, s.Model.Find(f), null, null, CoordinateApplyMode.All, Opts(s, data), null)).Code == CommandCodes.BadArgs &&
                    Tx(s, "x", tx => MakerEdits.ApplyCoordinate(tx, s.Model, s.Model.Find(f), src, null, (CoordinateApplyMode)7, Opts(s, data), null)).Code == CommandCodes.BadArgs,
                "rules: no coordinate / an unknown mode → bad_args");
            var card = s.AddChara(new CharaAppearance { Card = Kit.Card("c", "x.png", 0) }).NewIds[0];
            t.Check(Tx(s, "x", tx => MakerEdits.ApplyCoordinate(tx, s.Model, s.Model.Find(card), src, null, CoordinateApplyMode.All, Opts(s, data), null)).Code == CommandCodes.Unsupported,
                "rules: a card-backed character without documents → unsupported (StudioOps imports it first)");
            var cs = MakerEdits.CoordinateSets(src, CoordinateApplyMode.Clothes);
            t.Check(cs.Count == 9 && cs.Last().Path == K("name") && MakerEdits.CoordinateSets(src, CoordinateApplyMode.Accessories).Select(x => x.Path)
                    .SequenceEqual(new[] { AppearancePaths.AccessoryList, K("ruleOverrides"), K("name") }) && MakerEdits.CoordinateSets(src, CoordinateApplyMode.All).Count == 0 &&
                    cs.All(x => !x.Consequence),
                "rules: the raw sets of the modes (8 parts / the list and the rules, then the name), none a consequence");
        }

        // ---------------------------------------------------------------- the sex of a coordinate card to be worn

        static void CoordinateSexOfCard(TestRunner t)
        {
            // (1) the folder: the nearest ancestor named male / female directly in a folder named coordinate (any case, both separators)
            t.Check(MakerEdits.CoordinateSexFromPath(@"X:\HS2\UserData\coordinate\male\作業着.png") == 0 &&
                    MakerEdits.CoordinateSexFromPath("X:/HS2/UserData/coordinate/female/ガーリーブラウス.png") == 1 &&
                    MakerEdits.CoordinateSexFromPath("UserData/Coordinate/MALE/sub/deeper/a.png") == 0 &&
                    MakerEdits.CoordinateSexFromPath("coordinate/female/male/b.png") == 1 &&
                    MakerEdits.CoordinateSexFromPath("coordinate/male/coordinate/female/c.png") == 1,
                "card sex (1): the folder coordinate/male or coordinate/female decides (sub-folders inside it count; the nearest such folder wins)");
            t.Check(MakerEdits.CoordinateSexFromPath(@"D:\cards\male\a.png") == null && MakerEdits.CoordinateSexFromPath("coordinate/males/a.png") == null &&
                    MakerEdits.CoordinateSexFromPath("male.png") == null && MakerEdits.CoordinateSexFromPath("coordinate/male") == null &&
                    MakerEdits.CoordinateSexFromPath(null) == null && MakerEdits.CoordinateSexFromPath("") == null,
                "card sex (1): a male / female folder outside a coordinate folder, other names and no path give no folder sex");
            string male = @"X:\HS2\UserData\coordinate\male\作業着.png", female = "X:/HS2/UserData/coordinate/female/a.png", loose = @"D:\downloads\outfit.png";
            t.Check(MakerEdits.CoordinateSexOf(null, male, 1, out string f1) == 0 && f1 == MakerEdits.CoordinateSexFromFolder &&
                    MakerEdits.CoordinateSexOf(null, female, 0, out string f2) == 1 && f2 == MakerEdits.CoordinateSexFromFolder,
                "card sex (1): without evidence the folder wins over the target character's sex");
            t.Check(MakerEdits.CoordinateSexOf(null, loose, 0, out string f3) == 0 && f3 == MakerEdits.CoordinateSexFromCharacter &&
                    MakerEdits.CoordinateSexOf(null, loose, 1, out string f4) == 1 && f4 == MakerEdits.CoordinateSexFromCharacter,
                "card sex (2): without evidence and folder the target character's sex (the caller warns coord.sexAssumed)");
            t.Check(MakerEdits.CoordinateSexOf(null, loose, null, out string f5) == 1 && f5 == MakerEdits.CoordinateSexFromDefault &&
                    MakerEdits.CoordinateSexOf(null, null, null, out string f6) == 1 && f6 == MakerEdits.CoordinateSexFromDefault,
                "card sex: nothing at all → female (the importer's default)");
            t.Check(MakerEdits.CoordinateSexOf(1, male, 0, out string f7) == 1 && f7 == MakerEdits.CoordinateSexFromCard &&
                    MakerEdits.CoordinateSexOf(0, female, 1, out string f8) == 0 && f8 == MakerEdits.CoordinateSexFromCard &&
                    MakerEdits.CoordinateSexOf(0, loose, 1, out _) == 0 && MakerEdits.CoordinateSexOf(1, null, 0, out _) == 1,
                "card sex: the card's evidence always wins over the folder and the character");
            t.Check(MakerEdits.CoordinateSexOf(-1, male, 1, out string f9) == 0 && f9 == MakerEdits.CoordinateSexFromFolder,
                "card sex: an evidence value other than 0 / 1 is no evidence");
        }

        // ---------------------------------------------------------------- gates

        sealed class FakeClothesInfo : IClothesInfo
        {
            public readonly Dictionary<(string, int), ClothesKindFacts> Facts = new Dictionary<(string, int), ClothesKindFacts>();
            public bool TryKind(string nodeId, int kind, out ClothesKindFacts facts) => Facts.TryGetValue((nodeId, kind), out facts);
        }

        static void Gates(TestRunner t)
        {
            string G(int k, string f) => MakerEdits.ClothesGatePrefix + ClothesSet.Names[k] + ":" + f;
            var coord = new CoordDocument();
            coord.Clothes.Parts[0].Colors[1].Pattern = "illusion.hs2#3";
            // plain Mono: every prefab feature holds for the female; the male kinds without a list hold nothing
            int fem = 0, maleOn = 0, maleOff = 0;
            for (int k = 0; k < 8; k++)
                foreach (var feat in MakerEdits.ClothesGateFeatures)
                {
                    bool pat = feat.StartsWith("pattern", StringComparison.Ordinal);
                    if (MakerEdits.ClothesGateHolds(G(k, feat), 1, "n", coord, null) == (!pat || (k == 0 && feat == "pattern2"))) fem++;
                    bool listed = k == 0 || k == 1 || k == 4 || k == 7;
                    bool hm = MakerEdits.ClothesGateHolds(G(k, feat), 0, "n", coord, null);
                    if (listed && hm == (!pat || (k == 0 && feat == "pattern2"))) maleOn++;
                    if (!listed && !hm) maleOff++;
                }
            t.Check(fem == 120 && maleOn == 60 && maleOff == 60, "gates: no service — every prefab feature holds (patterns follow the document); the male inner / pantyhose / socks kinds hold nothing (" +
                fem + " / " + maleOn + " / " + maleOff + ")");
            var info = new FakeClothesInfo();
            info.Facts[("n", 0)] = new ClothesKindFacts { HasPrefab = true, UseColorN01 = true, UseColorA02 = true, UseBreak = false, Option02Count = 1, Coordinate = "1" };
            info.Facts[("n", 2)] = new ClothesKindFacts { HasPrefab = true, UseColorN01 = true, UseBreak = true, Coordinate = "0" };
            info.Facts[("n", 1)] = new ClothesKindFacts { Skipped = true };
            bool H(int k, string f, int sex = 1) => MakerEdits.ClothesGateHolds(G(k, f), sex, "n", coord, info);
            t.Check(H(0, "prefab") && H(0, "set1") && H(0, "set2") && !H(0, "set3") && !H(0, "alpha1") && H(0, "alpha2") && !H(0, "alpha3"),
                "gates: setN = useColorN0N or useColorA0N; alphaN = useColorA0N");
            t.Check(!H(0, "pattern1") && H(0, "pattern2") && !H(0, "pattern3"), "gates: patternN = set shown and a stored pattern (set 2 has one)");
            t.Check(!H(0, "break") && !H(0, "opt1") && H(0, "opt2") && H(0, "other") && H(2, "break") && H(2, "other") && !H(2, "opt2"),
                "gates: break = useBreak, optN = objOpt0N not empty, other = either");
            t.Check(!H(1, "kind") && H(3, "kind") && H(0, "kind") && H(2, "kind") && !H(1, "prefab") && !H(1, "set1"),
                "gates: a one-piece top (Coordinate 1) disables the bottom kind; a two-piece inner top keeps the inner bottom; the skipped bottom has no prefab");
            t.Check(!H(5, "prefab") && !H(5, "set1") && H(5, "kind") && !H(5, "pattern1"), "gates: a kind the service does not know has no prefab (but its button is enabled)");
            info.Facts[("n", 0)].Coordinate = null;
            t.Check(H(1, "kind") && new ClothesKindFacts { Coordinate = "" }.OnePiece == false && new ClothesKindFacts { Coordinate = "2" }.OnePiece,
                "gates: an unknown Coordinate counts as two-piece; any value but 0 is a one-piece");
            t.Check(!H(2, "kind", 0) && !H(2, "prefab", 0) && H(0, "prefab", 0), "gates: for a male the inner top holds nothing, the top its facts");
            t.Check(!MakerEdits.ClothesGateHolds("maker.clothes:top:nonsense", 1, "n", coord, info) && !MakerEdits.ClothesGateHolds("maker.hair:back:prefab", 1, "n", coord, info) &&
                    MakerEdits.GateHolds("maker.clothes:top:set1", new GateContext { Sex = 1, NodeId = "n", Coord = coord, ClothesInfo = info }) &&
                    !MakerEdits.GateHolds("maker.clothes:top:set3", new GateContext { Sex = 1, NodeId = "n", Coord = coord, ClothesInfo = info }) &&
                    !MakerEdits.GateHolds("maker.clothes:top:nonsense", new GateContext()),
                "gates: GateHolds(name, GateContext) evaluates the clothes family; malformed clothes gates do not hold");
            var facts = new ClothesKindFacts { HasPrefab = true, Groups = new[] { new ClothesGroupFacts { Group = 1, Renderers = 2 } }, Defaults = new[] { new ClothesColor { Gloss = 0.25f } } };
            var copy = facts.Clone();
            copy.Groups[0].Renderers = 5;
            copy.Defaults[0].Gloss = 0.75f;
            t.Check(facts.Groups[0].Renderers == 2 && facts.Defaults[0].Gloss == 0.25f && copy.HasPrefab, "gates: ClothesKindFacts.Clone copies groups and defaults");
            var data = new FakeMakerData();
            string row = FakeMakerData.Pool(240).First(r => data.TryClothesColors(240, r, out _));
            bool had = data.TryClothesFacts(240, row, 1, out var ff) && ff.HasPrefab && ff.Defaults != null && ff.SetShown(1);
            data.NoClothesFacts = true;
            t.Check(had && !data.TryClothesFacts(240, row, 1, out _), "gates: the fixture's prefab facts (IPartDefaults.TryClothesFacts) and their absence");
        }
    }
}
