// The Maker ops of the accessory pages (Phase 4.5; the Unity-free cores in MakerEdits / AccessoryStartupFold, which
// StudioOps.Maker wraps), on the golden character (accessory a1 in build slot 0 without a preferred slot, a0 in slot 3):
//   add with and without a slot (the lowest free slot, a free slot, an occupied one → bad_args, a MoreAccessories slot with
//     MoreAccessories off → acc.slotNotLoaded, slot and index together → bad_args, an unknown parent → bad_args); an add of a
//     hair-type item takes the back hair's colours (the empty slot counts as non-hair), a copy keeps the source's;
//   copy «複製» / to a free slot / to an occupied slot (overwrite, id and visibility kept) / the move sets and their mirrors /
//     a set to a free slot → bad_args; the parent mirror on all 54 keys (18 pairs swapped, the rest and non-keys unchanged);
//     a row-default parent "" resolved to the row's key before the mirror, stored "" again when it lands on the row's default;
//     the plugin data with `maker.copyAccessoryPluginData` off (the target's cleared, ABMX kept) and on (copied);
//   move to a slot (the preferred slot rewritten, the list order kept; occupied → bad_args; its own slot → no-op);
//   the consequences of a type and an item change (parent, sets, item, colours by HS2's hair rule in its three cases and with
//     «keep colours», noShake, AdditionalAccessoryControls rules, MaterialEditor / DynamicBoneEditor / ABMX data);
//   «初期カラー» (used areas and the alpha colour only; hair-type / no facts → unsupported, skipped on a multi-selection);
//   «髪の毛の色を取得» for the four parts; the parent rule (54 keys or "", unknown → bad_args, a stored unknown kept, a fixed
//     row warns acc.parentFixed, «初期親»); `importSlot` refused by a set; the ↺ of gloss / metallic to the prefab defaults;
//   the accessory gates (no service, row facts, a realization stub, empty slots, "0" / "null" rows);
//   AccessoryStartupFold on the census's rule kinds (STARTUP with / without STUDIO_LOAD, clothes rules with overrides and
//     suppressions, the half rule of gloves / socks / shoes, links incl. a loop); a coordinate load with the fold and
//     AutoMatchBackHairColor (and both off without AdditionalAccessoryControls);
//   undo / redo of each restores the documents and the visibility map byte for byte; the normalizer has nothing to do.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save;
    using HS2Next.Save.Tests;

    public static class SceneTests_AppearanceAccessories
    {
        public static void Run(TestRunner t)
        {
            Add(t);
            AddHairType(t);
            Copy(t);
            CopyMirrorParent(t);
            CopyPluginData(t);
            MoveToSlot(t);
            TypeAndItem(t);
            DefaultColours(t);
            HairColourFetch(t);
            Parent(t);
            PrefabResets(t);
            Gates(t);
            Fold(t);
            CoordinateLoad(t);
        }

        const string A0 = "3f2a6b1c9d8e4f7a0b1c2d3e4f5a6b7c", A1 = "0a1b2c3d4e5f60718293a4b5c6d7e8f9";

        static string P(string id, string member = null) => AppearancePaths.Accessory(id, member);

        static MakerEditOptions Opts(SceneSession s, FakeMakerData data, bool keep = false, bool multi = false, bool plugin = false, bool more = true, bool aac = true) =>
            new MakerEditOptions { Rows = data, Defaults = data, NewId = s.NewId, KeepColours = keep, SkipUnlistedRows = multi, CopyAccessoryPluginData = plugin, MoreAccessories = more, AccessoryControls = aac };

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

        static void Do(TestRunner t, CommandResult r, string what) => t.Check(r != null && r.Status != CommandStatus.Error, what + (r == null ? " (null result)" : r.Status != CommandStatus.Error ? "" : " (" + r + ")"));
        static void Fails(TestRunner t, CommandResult r, string code, string what) => t.Check(r != null && r.Status == CommandStatus.Error && r.Code == code, what + " (" + r + ")");

        static TreeNode N(SceneSession s, string id) => s.Model.Find(id);
        static CoordDocument Coord(SceneSession s, string id) => N(s, id).Appearance.Stored.Coord;
        static AccessoryEntry Acc(SceneSession s, string id, string acc) => Coord(s, id).FindAccessory(acc);
        static SortedDictionary<string, bool> Map(SceneSession s, string id) => ((CharaNodeState)N(s, id).Doc.State).State.Accessories;
        static string Doc(DocObject o) => o == null ? "null" : System.Text.Encoding.UTF8.GetString(DocClone.Bytes(o));
        static string State(SceneSession s, string id) => string.Join(",", Map(s, id).Select(kv => kv.Key + "=" + kv.Value));
        static string Full(SceneSession s, string id) =>
            Kit.Hash(SceneTests_Appearance.CharaBytes(s, id)) + "/" + Kit.Hash(SceneTests_Appearance.CoordBytes(s, id)) + "/" + State(s, id);
        static int SlotOf(SceneSession s, string id, string acc) => MakerEdits.AccessorySlots(Coord(s, id)).SlotOfId(acc);

        static void UndoRedo(TestRunner t, SceneSession s, string id, string start, string what)
        {
            string end = Full(s, id);
            int depth = s.Commands.UndoList.Count;
            while (s.Commands.CanUndo) s.Commands.Undo();
            t.Check(Full(s, id) == start, what + ": undo all restores the documents and the visibility map byte for byte");
            while (s.Commands.CanRedo) s.Commands.Redo();
            t.Check(s.Commands.UndoList.Count == depth && Full(s, id) == end, what + ": redo all gives the end state byte for byte");
            t.Check(AccessoryNormalizer.Normalized(Map(s, id), N(s, id).Appearance.WornAccessoryIds()) == null, what + ": the visibility map follows the list (the normalizer has nothing to do)");
            Kit.Valid(t, s, what);
        }

        static SceneSession Golden(TestRunner t, out string id)
        {
            var s = SceneTests_Appearance.GoldenChara(t, out id);
            s.Commands.ClearHistory();
            return s;
        }

        static AccessoryFacts Facts(int category, string item, bool hair, bool area2 = true, bool area3 = false, int alpha = 0, string parent = "N_Neck", bool move2 = true) => new AccessoryFacts
        {
            HasCmpAccessory = true, Category = category, Item = item, TypeHair = hair, UseColor = new[] { true, area2, area3 }, AlphaRenderers = alpha, Move1 = true, Move2 = move2,
            ParentColumn = parent, DefaultColors = Enumerable.Range(0, 4).Select(i => new AccessoryColor { Color = new Color4(0.1f * (i + 1), 0.2f, 0.3f, 0.5f + 0.1f * i), Gloss = 0.11f * (i + 1), Metallic = 0.07f * (i + 1), Smoothness = 0.9f }).ToArray(),
        };

        // ---------------------------------------------------------------- add

        static void Add(TestRunner t)
        {
            var data = new FakeMakerData();
            var s = Golden(t, out string id);
            string start = Full(s, id);
            t.Check(SlotOf(s, id, A1) == 0 && SlotOf(s, id, A0) == 3, "add: the golden accessories are in build slots 0 (no preferred slot) and 3 (the card's slot)");
            var rep = new MakerEditReport();
            string row = FakeMakerData.Row(353, 2);
            var r = Tx(s, "Add accessory", tx => MakerEdits.AddAccessory(tx, s.Model, N(s, id), 353, row, null, null, null, Opts(s, data), rep));
            Do(t, r, "add: without a slot");
            string n1 = rep.NewAccessoryIds.Single();
            t.Check(Acc(s, id, n1).ImportSlot == 1 && SlotOf(s, id, n1) == 1 && Coord(s, id).Accessories.Last().Id == n1 && Map(s, id)[n1],
                "add: the lowest free slot (01 → slot 1) becomes the preferred slot, appended, shown");
            t.Check(Acc(s, id, n1).Parent == MakerEdits.AccessoryDefaultParent(data, 353, row) && rep.Warnings.All(w => !w.StartsWith("acc.", StringComparison.Ordinal)),
                "add: the row's default parent, no slot warning");
            rep = new MakerEditReport();
            r = Tx(s, "Add accessory", tx => MakerEdits.AddAccessory(tx, s.Model, N(s, id), 355, FakeMakerData.Row(355, 3), "N_Neck", 7, null, Opts(s, data), rep));
            Do(t, r, "add: into slot 7");
            string n2 = rep.NewAccessoryIds.Single();
            t.Check(SlotOf(s, id, n2) == 7 && Acc(s, id, n2).ImportSlot == 7 && Acc(s, id, n2).Parent == "N_Neck", "add: into the named free slot 7 (08), with the given parent");
            r = Tx(s, "Add accessory", tx => MakerEdits.AddAccessory(tx, s.Model, N(s, id), 355, FakeMakerData.Row(355, 3), null, 3, null, Opts(s, data), null));
            Fails(t, r, CommandCodes.BadArgs, "add: an occupied slot → bad_args");
            t.Check(r.Message.Contains("04") && r.Message.Contains(A0), "add: the message names the slot and the accessory there (" + r.Message + ")");
            rep = new MakerEditReport();
            r = Tx(s, "Add accessory", tx => MakerEdits.AddAccessory(tx, s.Model, N(s, id), 351, FakeMakerData.Row(351, 4), null, 24, null, Opts(s, data, more: false), rep));
            string n3 = rep.NewAccessoryIds.Single();
            t.Check(r.Ok && SlotOf(s, id, n3) == 24 && rep.Warnings.Any(w => w.StartsWith("acc.slotNotLoaded:", StringComparison.Ordinal)),
                "add: slot 24 with MoreAccessories off is kept as data with acc.slotNotLoaded");
            rep = new MakerEditReport();
            r = Tx(s, "Add accessory", tx => MakerEdits.AddAccessory(tx, s.Model, N(s, id), 351, FakeMakerData.Row(351, 5), null, 25, null, Opts(s, data), rep));
            t.Check(r.Ok && !rep.Warnings.Any(w => w.StartsWith("acc.", StringComparison.Ordinal)), "add: slot 25 with MoreAccessories on: no warning");
            Fails(t, Tx(s, "Add accessory", tx => MakerEdits.AddAccessory(tx, s.Model, N(s, id), 351, FakeMakerData.Row(351, 5), null, 9, 0, Opts(s, data), null)), CommandCodes.BadArgs,
                "add: slot and index together → bad_args");
            Fails(t, Tx(s, "Add accessory", tx => MakerEdits.AddAccessory(tx, s.Model, N(s, id), 351, FakeMakerData.Row(351, 5), "N_Nowhere", null, null, Opts(s, data), null)), CommandCodes.BadArgs,
                "add: an unknown parent → bad_args");
            Fails(t, Tx(s, "Add accessory", tx => MakerEdits.AddAccessory(tx, s.Model, N(s, id), 351, FakeMakerData.Row(351, 5), null, -2, null, Opts(s, data), null)), CommandCodes.BadArgs,
                "add: a negative slot → bad_args");
            rep = new MakerEditReport();
            r = Tx(s, "Add accessory", tx => MakerEdits.AddAccessory(tx, s.Model, N(s, id), 352, FakeMakerData.Row(352, 1), null, 0, Opts(s, data), rep));
            string n4 = rep.NewAccessoryIds.Single();
            t.Check(r.Ok && Coord(s, id).Accessories[0].Id == n4 && Acc(s, id, n4).ImportSlot == null, "add: the deprecated list-index form inserts at the index without a preferred slot");
            t.Equal(5, s.Commands.UndoList.Count, "add: one entry per add (the refused ones none)");
            UndoRedo(t, s, id, start, "add");
        }

        // ---------------------------------------------------------------- add of a hair-type item (a decision)

        static void AddHairType(TestRunner t)
        {
            var data = new FakeMakerData();
            var withColours = FakeMakerData.Pool(351).Skip(20).Where(r => data.TryAccessoryColors(351, r, out _)).ToList();
            string hair = withColours[0], normal = withColours[1];
            data.AccessoryFactsOf[hair] = Facts(351, hair, true);
            data.AccessoryFactsOf[normal] = Facts(351, normal, false);
            var s = Golden(t, out string id);
            Do(t, s.Set(id, AppearancePaths.Chara("hair.back.baseColor"), new Color4(0.11f, 0.22f, 0.33f, 0.9f)), "add hair-type: the back hair's base colour");
            Do(t, s.Set(id, AppearancePaths.Chara("hair.back.smoothness"), 0.37f), "add hair-type: the back hair's smoothness");
            Do(t, s.Set(id, AppearancePaths.Chara("hair.back.metallic"), 0.19f), "add hair-type: the back hair's metallic");
            Do(t, s.Set(id, AppearancePaths.Chara("hair.front.baseColor"), new Color4(0.9f, 0.8f, 0.7f, 1f)), "add hair-type: another front hair colour");
            s.Commands.ClearHistory();
            string start = Full(s, id);
            var back = N(s, id).Appearance.Stored.Chara.Hair.Parts[0];
            data.TryAccessoryColors(351, hair, out var hairDefs);
            foreach (bool keep in new[] { false, true })
            {
                var rep = new MakerEditReport();
                var r = Tx(s, "Add accessory", tx => MakerEdits.AddAccessory(tx, s.Model, N(s, id), 351, hair, null, null, null, Opts(s, data, keep: keep), rep));
                var c = Acc(s, id, rep.NewAccessoryIds.Single()).Colors;
                t.Check(r.Ok && c[0].Color.Equals(back.BaseColor) && c[1].Color.Equals(back.TopColor) && c[2].Color.Equals(back.UnderColor) && c[3].Color.Equals(back.Specular) &&
                        c[0].Smoothness == back.Smoothness && c[0].Metallic == back.Metallic,
                    "add hair-type" + (keep ? " (keep colours)" : "") + ": colours 1–4, colour 1's smoothness and metallic ← the back hair (the empty slot counts as non-hair)");
                t.Check(c[1].Gloss == hairDefs[1].Gloss && c[3].Gloss == hairDefs[3].Gloss && c[3].Metallic == hairDefs[3].Metallic && c[1].Smoothness == hairDefs[1].Smoothness,
                    "add hair-type: the members the hair rule does not write keep the prefab defaults");
            }
            var rn = new MakerEditReport();
            var rr = Tx(s, "Add accessory", tx => MakerEdits.AddAccessory(tx, s.Model, N(s, id), 351, normal, null, 30, null, Opts(s, data), rn));
            data.TryAccessoryColors(351, normal, out var normalDefs);
            string nid = rn.NewAccessoryIds.Single();
            t.Check(rr.Ok && Acc(s, id, nid).Colors.Select(Doc).SequenceEqual(normalDefs.Select(Doc)), "add: a normal item takes the prefab defaults");
            // a copy of a hair-type accessory copies the source's colours (no hair rule)
            var rh = new MakerEditReport();
            Do(t, Tx(s, "Add accessory", tx => MakerEdits.AddAccessory(tx, s.Model, N(s, id), 351, hair, null, 31, null, Opts(s, data), rh)), "add hair-type: into slot 31");
            string hid = rh.NewAccessoryIds.Single();
            Do(t, s.Set(id, P(hid, "colors[0].color"), new Color4(0.5f, 0.5f, 0.5f, 1f)), "add hair-type: its base colour edited");
            var rc = new MakerEditReport();
            var cr = Tx(s, "Copy accessory", tx => MakerEdits.CopyAccessory(tx, s.Model, N(s, id), hid, 32, null, AccessoryCopyParts.Slot, false, MirrorAxis.None, Opts(s, data), rc));
            t.Check(cr.Ok && Acc(s, id, rc.NewAccessoryIds.Single()).Colors.Select(Doc).SequenceEqual(Acc(s, id, hid).Colors.Select(Doc)),
                "add hair-type: a copy to a slot copies the source's colours (no hair rule)");
            // the deprecated list-index form takes the same rule
            var ri = new MakerEditReport();
            Do(t, Tx(s, "Add accessory", tx => MakerEdits.AddAccessory(tx, s.Model, N(s, id), 351, hair, null, 0, Opts(s, data), ri)), "add hair-type: the list-index form");
            t.Check(Acc(s, id, ri.NewAccessoryIds.Single()).Colors[0].Color.Equals(back.BaseColor), "add hair-type: the list-index form takes the back hair's colours too");
            // without facts (plain Mono) the prefab defaults
            var rp = new MakerEditReport();
            var nf = new FakeMakerData { NoAccessoryFacts = true };
            Do(t, Tx(s, "Add accessory", tx => MakerEdits.AddAccessory(tx, s.Model, N(s, id), 351, hair, null, null, null, Opts(s, nf), rp)), "add hair-type: without facts");
            t.Check(Acc(s, id, rp.NewAccessoryIds.Single()).Colors.Select(Doc).SequenceEqual(hairDefs.Select(Doc)), "add hair-type: without facts the prefab defaults (not known to be hair-type)");
            UndoRedo(t, s, id, start, "add hair-type");
        }

        // ---------------------------------------------------------------- copy

        static void Copy(TestRunner t)
        {
            var data = new FakeMakerData();
            var s = Golden(t, out string id);
            string start = Full(s, id);
            var rep = new MakerEditReport();
            var r = Tx(s, "Copy accessory", tx => MakerEdits.CopyAccessory(tx, s.Model, N(s, id), A0, null, null, AccessoryCopyParts.Slot, true, MirrorAxis.None, Opts(s, data), rep));
            Do(t, r, "copy: «複製»");
            string d = rep.NewAccessoryIds.Single();
            var src = Acc(s, id, A0);
            var dup = Acc(s, id, d);
            t.Check(SlotOf(s, id, d) == 1 && dup.ImportSlot == 1 && Coord(s, id).Accessories.Last() == dup && Map(s, id)[d],
                "copy: «複製» lands in the lowest free slot (02) as a new accessory with that preferred slot, shown");
            t.Check(dup.Parent == (MakerEdits.AccessoryDefaultParent(data, src.Category, src.Item) == "N_Earring_R" ? "" : "N_Earring_R") && dup.Item == src.Item && dup.Category == src.Category && Doc(dup.Moves[1]) == Doc(src.Moves[1]) && dup.Colors.Select(Doc).SequenceEqual(src.Colors.Select(Doc)) &&
                    dup.NoShake == src.NoShake && dup.HideCategory == src.HideCategory && dup.HideTiming == src.HideTiming,
                "copy: the members copied, the parent mirrored by the table (N_Earring_L → N_Earring_R)");
            t.Check(dup.MaterialOverrides.Count == 0 && dup.DynamicBones.Count == 0 && dup.Rules == null && dup.BoneMods.Count == 0, "copy: without the switch no plugin data follows");
            rep = new MakerEditReport();
            r = Tx(s, "Copy accessory", tx => MakerEdits.CopyAccessory(tx, s.Model, N(s, id), A0, 12, null, AccessoryCopyParts.Slot, false, MirrorAxis.None, Opts(s, data), rep));
            string c12 = rep.NewAccessoryIds.Single();
            t.Check(r.Ok && SlotOf(s, id, c12) == 12 && Acc(s, id, c12).Parent == "N_Earring_L", "copy: to the free slot 12 (13) as a new accessory, parent kept");
            // to an occupied slot: overwrite, id and visibility kept
            Do(t, s.Set(id, MakerEdits.StateAccessories + "[\"" + A1 + "\"]", false), "copy: hide the target");
            int before = Coord(s, id).Accessories.Count;
            r = Tx(s, "Copy accessory", tx => MakerEdits.CopyAccessory(tx, s.Model, N(s, id), A0, 0, null, AccessoryCopyParts.Slot, false, MirrorAxis.None, Opts(s, data), null));
            var tgt = Acc(s, id, A1);
            t.Check(r.Ok && Coord(s, id).Accessories.Count == before && tgt.Category == 352 && tgt.Item == src.Item && tgt.Parent == src.Parent && tgt.HideTiming == src.HideTiming &&
                    tgt.NoShake == src.NoShake && !Map(s, id)[A1] && SlotOf(s, id, A1) == 0,
                "copy: to the occupied slot 0 the target's members are overwritten (its id, slot and visibility kept)");
            // move sets and mirrors
            r = Tx(s, "Copy accessory", tx => MakerEdits.CopyAccessory(tx, s.Model, N(s, id), A0, 0, null, AccessoryCopyParts.Move2, false, MirrorAxis.LeftRight, Opts(s, data), null));
            var m = Acc(s, id, A1).Moves[1];
            t.Check(r.Ok && m.Rotation.Y == 270f && m.Rotation.Z == 360f && m.Position.Y == -0.5f && m.Scale.X == 1.5f, "copy: set 2 mirrored left-right: rotation.y 90 → 270, position / scale as copied");
            Do(t, s.Set(id, P(A0, "moves[0].rotation"), new Vec3(200f, 30f, 359.5f)), "copy: source set 1 rotation (200, 30, 359.5)");
            r = Tx(s, "Copy accessory", tx => MakerEdits.CopyAccessory(tx, s.Model, N(s, id), A0, 0, null, AccessoryCopyParts.Move1, false, MirrorAxis.UpDown, Opts(s, data), null));
            var m1 = Acc(s, id, A1).Moves[0].Rotation;
            t.Check(r.Ok && m1.X == 20f && m1.Y == 30f && m1.Z == 359.5f, "copy: set 1 mirrored up-down: rotation.x 200 → 20 (mod 360)");
            r = Tx(s, "Copy accessory", tx => MakerEdits.CopyAccessory(tx, s.Model, N(s, id), A0, 0, null, AccessoryCopyParts.Move1, false, MirrorAxis.LeftRight, Opts(s, data), null));
            t.Check(r.Ok && Acc(s, id, A1).Moves[0].Rotation.Y == 210f, "copy: set 1 mirrored left-right: rotation.y 30 → 210");
            Fails(t, Tx(s, "Copy accessory", tx => MakerEdits.CopyAccessory(tx, s.Model, N(s, id), A0, 30, null, AccessoryCopyParts.Move1, false, MirrorAxis.None, Opts(s, data), null)), CommandCodes.BadArgs,
                "copy: a move set to a free slot → bad_args");
            Fails(t, Tx(s, "Copy accessory", tx => MakerEdits.CopyAccessory(tx, s.Model, N(s, id), A0, 3, null, AccessoryCopyParts.Slot, false, MirrorAxis.None, Opts(s, data), null)), CommandCodes.BadArgs,
                "copy: the source's own slot → bad_args");
            Fails(t, Tx(s, "Copy accessory", tx => MakerEdits.CopyAccessory(tx, s.Model, N(s, id), A0, 0, A1, AccessoryCopyParts.Slot, false, MirrorAxis.None, Opts(s, data), null)), CommandCodes.BadArgs,
                "copy: a slot and an id together → bad_args");
            Fails(t, Tx(s, "Copy accessory", tx => MakerEdits.CopyAccessory(tx, s.Model, N(s, id), A0, null, "nobody", AccessoryCopyParts.Slot, false, MirrorAxis.None, Opts(s, data), null)), CommandCodes.NotFound,
                "copy: an unknown target id → not_found");
            Fails(t, Tx(s, "Copy accessory", tx => MakerEdits.CopyAccessory(tx, s.Model, N(s, id), "nobody", null, null, AccessoryCopyParts.Slot, false, MirrorAxis.None, Opts(s, data), null)), CommandCodes.NotFound,
                "copy: an unknown source → not_found");
            rep = new MakerEditReport();
            r = Tx(s, "Copy accessory", tx => MakerEdits.CopyAccessory(tx, s.Model, N(s, id), A0, 22, null, AccessoryCopyParts.Slot, false, MirrorAxis.None, Opts(s, data, more: false), rep));
            t.Check(r.Ok && rep.Warnings.Any(w => w.StartsWith("acc.slotNotLoaded:", StringComparison.Ordinal)), "copy: to slot 22 with MoreAccessories off: acc.slotNotLoaded");
            // the parent mirror on all 54 keys: 18 pairs swap, the others and texts that are not keys stay
            int pairs = 0, kept = 0, wrong = 0;
            foreach (var key in DocEnum.AttachPoints)
            {
                string rv = AccessoryMirror.ReverseParent(key);
                if (rv == key) kept++;
                else if (DocEnum.AttachPointValue(rv) > 0 && AccessoryMirror.ReverseParent(rv) == key && key.Substring(0, key.Length - 1) == rv.Substring(0, rv.Length - 1)) pairs++;
                else wrong++;
            }
            t.Check(pairs == 36 && kept == 18 && wrong == 0 && AccessoryMirror.Pairs.Count == 18, "copy: the parent mirror swaps the 18 left / right pairs of the 54 keys and keeps the other 18 (" + pairs / 2 + " pairs, " + kept + " kept)");
            t.Check(AccessoryMirror.ReverseParent("N_Hand_X_L") == "N_Hand_X_L" && AccessoryMirror.ReverseParent("clothes_corneo_diff") == "clothes_corneo_diff" && AccessoryMirror.ReverseParent("") == "" &&
                    AccessoryMirror.ReverseParent(null) == null && MakerEdits.MirrorParent("N_Back_R") == "N_Back_L" && MakerEdits.MirrorParent("N_Back") == "N_Back",
                "copy: a text that is not a key is not mirrored (HS2's table, not the suffix rule)");
            t.Equal(9, s.Commands.UndoList.Count, "copy: one entry per copy and per set");
            UndoRedo(t, s, id, start, "copy");
        }

        // The parent of a slot copy with «左右の親を変更» (decided on T's UI8 finding): a row-default parent ""
        // is resolved to the row's key first (HS2 stores the key on a type change, XH3) and mirrored (XH7, UI8 step 12); the
        // result is stored as "" when it is the copied row's own default.
        static void CopyMirrorParent(TestRunner t)
        {
            var data = new FakeMakerData();
            string ear0 = FakeMakerData.Row(352, 0), neck0 = FakeMakerData.Row(355, 0);
            var rows = new OddParentRows(data, new Dictionary<string, string> { [ear0] = "N_Earring_L", [neck0] = "N_Chest" });
            var s = Golden(t, out string id);
            s.Commands.ClearHistory();
            string start = Full(s, id);
            var o = Opts(s, data).WithRows(rows);
            // UI8 step 11 / 12: a type change to «耳» (parent "" = the row's N_Earring_L), then a mirrored copy
            Do(t, Tx(s, "Change item", tx => MakerEdits.Edit(tx, s.Model, N(s, id), new List<(string, object)> { (P(A1, "category"), 352) }, o, null)), "mirror: a1's type → 352 (row 0)");
            t.Check(Acc(s, id, A1).Item == ear0 && Acc(s, id, A1).Parent == "", "mirror: the type change stores the row default as \"\"");
            var rep = new MakerEditReport();
            var r = Tx(s, "Copy accessory", tx => MakerEdits.CopyAccessory(tx, s.Model, N(s, id), A1, 10, null, AccessoryCopyParts.Slot, true, MirrorAxis.None, o, rep));
            string c10 = rep.NewAccessoryIds.Single();
            t.Check(r.Ok && Acc(s, id, c10).Parent == "N_Earring_R", "mirror: a copy to slot 10 with «左右の親を変更» resolves \"\" to N_Earring_L and stores N_Earring_R (" + Acc(s, id, c10).Parent + ")");
            rep = new MakerEditReport();
            r = Tx(s, "Copy accessory", tx => MakerEdits.CopyAccessory(tx, s.Model, N(s, id), A1, 11, null, AccessoryCopyParts.Slot, false, MirrorAxis.None, o, rep));
            t.Check(r.Ok && Acc(s, id, rep.NewAccessoryIds.Single()).Parent == "", "mirror: without «左右の親を変更» the stored \"\" is copied as it is");
            r = Tx(s, "Copy accessory", tx => MakerEdits.CopyAccessory(tx, s.Model, N(s, id), A1, 3, null, AccessoryCopyParts.Slot, true, MirrorAxis.None, o, null));
            t.Check(r.Ok && Acc(s, id, A0).Parent == "N_Earring_R" && Acc(s, id, A0).Item == ear0, "mirror: an overwrite of the occupied slot 3 stores the mirrored key too");
            // a mirror back onto the row's default is stored as "" (the row default)
            r = Tx(s, "Copy accessory", tx => MakerEdits.CopyAccessory(tx, s.Model, N(s, id), A0, 12, null, AccessoryCopyParts.Slot, true, MirrorAxis.None, o, rep = new MakerEditReport()));
            t.Check(r.Ok && Acc(s, id, rep.NewAccessoryIds.Single()).Parent == "", "mirror: N_Earring_R mirrored back onto the row's default N_Earring_L is stored as \"\"");
            // an unpaired default stays ""
            Do(t, Tx(s, "Change item", tx => MakerEdits.Edit(tx, s.Model, N(s, id), new List<(string, object)> { (P(A1, "category"), 355) }, o, null)), "mirror: a1's type → 355 (default N_Chest)");
            r = Tx(s, "Copy accessory", tx => MakerEdits.CopyAccessory(tx, s.Model, N(s, id), A1, 13, null, AccessoryCopyParts.Slot, true, MirrorAxis.None, o, rep = new MakerEditReport()));
            t.Check(r.Ok && Acc(s, id, rep.NewAccessoryIds.Single()).Parent == "", "mirror: an unpaired row default (N_Chest) stays \"\"");
            // an explicit key is mirrored; a text that is not a key stays
            Do(t, s.Set(id, P(A1, "parent"), "N_Hand_L"), "mirror: a1 re-parented to N_Hand_L");
            r = Tx(s, "Copy accessory", tx => MakerEdits.CopyAccessory(tx, s.Model, N(s, id), A1, 14, null, AccessoryCopyParts.Slot, true, MirrorAxis.None, o, rep = new MakerEditReport()));
            t.Check(r.Ok && Acc(s, id, rep.NewAccessoryIds.Single()).Parent == "N_Hand_R", "mirror: an explicit N_Hand_L → N_Hand_R");
            Do(t, s.Set(id, P(A1, "parent"), "N_head"), "mirror: a1 with an imported text");
            r = Tx(s, "Copy accessory", tx => MakerEdits.CopyAccessory(tx, s.Model, N(s, id), A1, 15, null, AccessoryCopyParts.Slot, true, MirrorAxis.None, o, rep = new MakerEditReport()));
            t.Check(r.Ok && Acc(s, id, rep.NewAccessoryIds.Single()).Parent == "N_head", "mirror: a text that is not one of the 54 keys stays as it is");
            // without lists: "" has no key to resolve (stays ""), an explicit key is mirrored
            var noRows = Opts(s, data).WithRows(null);
            t.Check(MakerEdits.MirroredCopyParent(new AccessoryEntry { Category = 352, Item = ear0, Parent = "" }, null) == "" &&
                    MakerEdits.MirroredCopyParent(new AccessoryEntry { Category = 352, Item = ear0, Parent = "N_Earring_L" }, null) == "N_Earring_R" &&
                    MakerEdits.MirroredCopyParent(new AccessoryEntry { Category = 352, Item = ear0, Parent = "" }, rows) == "N_Earring_R" &&
                    MakerEdits.MirroredCopyParent(new AccessoryEntry { Category = 355, Item = neck0, Parent = "N_Chest" }, rows) == "" &&
                    MakerEdits.MirroredCopyParent(new AccessoryEntry { Category = 355, Item = neck0, Parent = "N_Waist_R" }, rows) == "N_Waist_L" &&
                    MakerEdits.MirroredCopyParent(null, rows) == "",
                "mirror: MirroredCopyParent — without lists \"\" stays, an explicit key mirrors; with lists \"\" resolves to the row's key; the row's own default → \"\"");
            r = Tx(s, "Copy accessory", tx => MakerEdits.CopyAccessory(tx, s.Model, N(s, id), A0, 16, null, AccessoryCopyParts.Slot, true, MirrorAxis.None, noRows, rep = new MakerEditReport()));
            t.Check(r.Ok && Acc(s, id, rep.NewAccessoryIds.Single()).Parent == "N_Earring_L", "mirror: without lists an explicit N_Earring_R is mirrored to N_Earring_L");
            UndoRedo(t, s, id, start, "mirror");
        }

        static void CopyPluginData(TestRunner t)
        {
            var data = new FakeMakerData();
            foreach (bool on in new[] { false, true })
            {
                var s = Golden(t, out string id);
                // the target a1 gets plugin data of its own
                Do(t, s.Set(id, P(A1, "materialOverrides"), new List<MaterialOverride> { DocClone.Clone(Acc(s, id, A0).MaterialOverrides[0]) }), "plugin data: target ME item");
                Do(t, s.Set(id, P(A1, "boneMods"), new List<AccessoryBoneMod> { DocClone.Clone(Acc(s, id, A0).BoneMods[0]) }), "plugin data: target ABMX");
                Do(t, s.Set(id, P(A1, "rules"), new AccessoryRules { Name = "target" }), "plugin data: target AAC record");
                s.Commands.ClearHistory();
                string start = Full(s, id);
                var src = Acc(s, id, A0);
                var r = Tx(s, "Copy accessory", tx => MakerEdits.CopyAccessory(tx, s.Model, N(s, id), A0, 0, null, AccessoryCopyParts.Slot, false, MirrorAxis.None, Opts(s, data, plugin: on), null));
                var tg = Acc(s, id, A1);
                if (on)
                    t.Check(r.Ok && Doc(tg.MaterialOverrides.Single()) == Doc(src.MaterialOverrides.Single()) && Doc(tg.DynamicBones.Single()) == Doc(src.DynamicBones.Single()) &&
                            Doc(tg.Rules) == Doc(src.Rules) && !ReferenceEquals(tg.Rules, src.Rules) && tg.BoneMods.Count == 1,
                        "plugin data on: the source's ME items, DBE entries and AAC rules replace the target's (copies); the target's ABMX kept");
                else
                    t.Check(r.Ok && tg.MaterialOverrides.Count == 0 && tg.DynamicBones.Count == 0 && tg.Rules == null && tg.BoneMods.Count == 1,
                        "plugin data off (vanilla): the target's ME / DBE / AAC data cleared as on an item change; ABMX kept");
                var rep = new MakerEditReport();
                r = Tx(s, "Copy accessory", tx => MakerEdits.CopyAccessory(tx, s.Model, N(s, id), A0, null, null, AccessoryCopyParts.Slot, false, MirrorAxis.None, Opts(s, data, plugin: on), rep));
                var dup = Acc(s, id, rep.NewAccessoryIds.Single());
                t.Check(r.Ok && dup.MaterialOverrides.Count == (on ? 1 : 0) && dup.DynamicBones.Count == (on ? 1 : 0) && (dup.Rules != null) == on && dup.BoneMods.Count == 0,
                    "plugin data " + (on ? "on" : "off") + ": «複製» " + (on ? "copies" : "does not copy") + " ME / DBE / AAC; ABMX never");
                UndoRedo(t, s, id, start, "plugin data " + (on ? "on" : "off"));
            }
        }

        // ---------------------------------------------------------------- move to a slot

        static void MoveToSlot(TestRunner t)
        {
            var data = new FakeMakerData();
            var s = Golden(t, out string id);
            string start = Full(s, id);
            var order = string.Join(",", Coord(s, id).Accessories.Select(a => a.Id));
            var r = Tx(s, "Move accessory", tx => MakerEdits.MoveAccessoryToSlot(tx, s.Model, N(s, id), A0, 9, Opts(s, data), null));
            t.Check(r.Ok && SlotOf(s, id, A0) == 9 && Acc(s, id, A0).ImportSlot == 9 && string.Join(",", Coord(s, id).Accessories.Select(a => a.Id)) == order && Map(s, id).ContainsKey(A0),
                "move to slot: a0 to slot 9 (10): the preferred slot rewritten, the list order and the visibility kept");
            t.Check(Acc(s, id, A0).MaterialOverrides.Count == 1 && Acc(s, id, A0).Parent == "N_Earring_L", "move to slot: the other members unchanged");
            Fails(t, Tx(s, "Move accessory", tx => MakerEdits.MoveAccessoryToSlot(tx, s.Model, N(s, id), A1, 9, Opts(s, data), null)), CommandCodes.BadArgs, "move to slot: an occupied slot → bad_args");
            var noop = Tx(s, "Move accessory", tx => MakerEdits.MoveAccessoryToSlot(tx, s.Model, N(s, id), A0, 9, Opts(s, data), null));
            t.Check(noop != null && noop.Status == CommandStatus.NoOp, "move to slot: its own slot with that preferred slot → no-op");
            r = Tx(s, "Move accessory", tx => MakerEdits.MoveAccessoryToSlot(tx, s.Model, N(s, id), A1, 0, Opts(s, data), null));
            t.Check(r.Ok && Acc(s, id, A1).ImportSlot == 0 && SlotOf(s, id, A1) == 0, "move to slot: its own slot without a preferred slot makes it the preferred slot");
            var rep = new MakerEditReport();
            r = Tx(s, "Move accessory", tx => MakerEdits.MoveAccessoryToSlot(tx, s.Model, N(s, id), A1, 40, Opts(s, data, more: false), rep));
            t.Check(r.Ok && SlotOf(s, id, A1) == 40 && rep.Warnings.Any(w => w.StartsWith("acc.slotNotLoaded:", StringComparison.Ordinal)), "move to slot: slot 40 with MoreAccessories off warns");
            Fails(t, Tx(s, "Move accessory", tx => MakerEdits.MoveAccessoryToSlot(tx, s.Model, N(s, id), "nobody", 2, Opts(s, data), null)), CommandCodes.NotFound, "move to slot: an unknown accessory → not_found");
            Fails(t, Tx(s, "Move accessory", tx => MakerEdits.MoveAccessoryToSlot(tx, s.Model, N(s, id), A1, -1, Opts(s, data), null)), CommandCodes.BadArgs, "move to slot: a negative slot → bad_args");
            // the preferred slot is not a settable member
            var rs = s.Set(id, P(A0, "importSlot"), 5);
            Fails(t, rs, CommandCodes.Unsupported, "move to slot: a set of importSlot is refused");
            t.Check(rs.Message != null && rs.Message.Contains("the preferred slot is set by add / copy / move-to-slot") && !FieldAccessor.IsWritable(s.Model, id, P(A0, "importSlot"), out _),
                "move to slot: importSlot is not writable through paths; the refusal names the slot ops (" + rs.Message + ")");
            t.Equal(3, s.Commands.UndoList.Count, "move to slot: one entry per move");
            UndoRedo(t, s, id, start, "move to slot");
        }

        // ---------------------------------------------------------------- type and item consequences

        static void TypeAndItem(TestRunner t)
        {
            var data = new FakeMakerData();
            var withColours = FakeMakerData.Pool(351).Skip(10).Where(r => data.TryAccessoryColors(351, r, out _)).ToList();
            string normal = withColours[0], normal2 = withColours[1], hair = withColours[2], hair2 = withColours[3];
            data.AccessoryFactsOf[normal] = Facts(351, normal, false);
            data.AccessoryFactsOf[normal2] = Facts(351, normal2, false);
            data.AccessoryFactsOf[hair] = Facts(351, hair, true);
            data.AccessoryFactsOf[hair2] = Facts(351, hair2, true);
            var s = Golden(t, out string id);
            Do(t, s.Set(id, P(A1, "item"), normal), "consequences: a1 is a normal row");
            Do(t, s.Set(id, P(A1, "rules"), new AccessoryRules { MatchHairColor = true }), "consequences: a1 has an AAC record");
            Do(t, s.Set(id, P(A1, "moves[0].position"), new Vec3(1f, 2f, 3f)), "consequences: a1 set 1 moved");
            Do(t, s.Set(id, P(A1, "boneMods"), new List<AccessoryBoneMod> { new AccessoryBoneMod { Bone = "N_Hair_pony" } }), "consequences: a1 ABMX");
            s.Commands.ClearHistory();
            string start = Full(s, id);
            var back = N(s, id).Appearance.Stored.Chara.Hair.Parts[0];
            // non-hair → hair: the back hair's colours, also with keep colours
            var rep = new MakerEditReport();
            var r = Tx(s, "Change item", tx => MakerEdits.Edit(tx, s.Model, N(s, id), new List<(string, object)> { (P(A1, "item"), hair) }, Opts(s, data, keep: true), rep));
            var e = Acc(s, id, A1);
            t.Check(r.Ok && e.Colors[0].Color.Equals(back.BaseColor) && e.Colors[1].Color.Equals(back.TopColor) && e.Colors[2].Color.Equals(back.UnderColor) && e.Colors[3].Color.Equals(back.Specular) &&
                    e.Colors[0].Smoothness == back.Smoothness && e.Colors[0].Metallic == back.Metallic,
                "consequences: non-hair → hair-type takes the back hair's base / top / under / specular, smoothness and metallic (also with keep colours)");
            t.Check(!e.NoShake && e.Rules == null && e.MaterialOverrides.Count == 0 && e.DynamicBones.Count == 0 && e.BoneMods.Count == 1 && e.Moves[0].Position.X == 1f,
                "consequences: an item change sets noShake off, clears the AAC record and ME / DBE, keeps ABMX and the sets");
            t.Check(rep.Consequences.Contains(FieldPath.Canonical(P(A1, "colors[0].color"))) && rep.Consequences.Contains(FieldPath.Canonical(P(A1, "rules"))), "consequences: reported as consequences");
            // hair → hair: kept
            Do(t, s.Set(id, P(A1, "colors[1].color"), new Color4(0.5f, 0.25f, 0.125f, 1f)), "consequences: a hair colour edited");
            string kept = Doc(Acc(s, id, A1).Colors[1]);
            r = Tx(s, "Change item", tx => MakerEdits.Edit(tx, s.Model, N(s, id), new List<(string, object)> { (P(A1, "item"), hair2) }, Opts(s, data), null));
            t.Check(r.Ok && Doc(Acc(s, id, A1).Colors[1]) == kept, "consequences: hair-type → hair-type keeps the colours");
            // hair → normal: the prefab defaults
            data.TryAccessoryColors(351, normal2, out var defs);
            r = Tx(s, "Change item", tx => MakerEdits.Edit(tx, s.Model, N(s, id), new List<(string, object)> { (P(A1, "item"), normal2) }, Opts(s, data), null));
            t.Check(r.Ok && Acc(s, id, A1).Colors.Select(Doc).SequenceEqual(defs.Select(Doc)), "consequences: hair-type → normal takes the prefab defaults");
            // normal → normal with keep colours: kept
            Do(t, s.Set(id, P(A1, "colors[2].gloss"), 1.75f), "consequences: a gloss edited");
            r = Tx(s, "Change item", tx => MakerEdits.Edit(tx, s.Model, N(s, id), new List<(string, object)> { (P(A1, "item"), normal) }, Opts(s, data, keep: true), null));
            t.Check(r.Ok && Acc(s, id, A1).Colors[2].Gloss == 1.75f, "consequences: normal → normal with keep colours keeps them");
            // type change
            Do(t, s.Set(id, P(A1, "noShake"), true), "consequences: noShake on");
            Do(t, s.Set(id, P(A1, "rules"), new AccessoryRules { Name = "again" }), "consequences: an AAC record again");
            rep = new MakerEditReport();
            r = Tx(s, "Change item", tx => MakerEdits.Edit(tx, s.Model, N(s, id), new List<(string, object)> { (P(A1, "category"), 355) }, Opts(s, data), rep));
            e = Acc(s, id, A1);
            bool has355 = data.TryAccessoryColors(355, FakeMakerData.Row(355, 0), out var defs355);
            t.Check(r.Ok && e.Category == 355 && e.Item == FakeMakerData.Row(355, 0) && e.Parent == "" && e.Moves.All(m => Doc(m) == Doc(new AccessoryMove())) && !e.NoShake && e.Rules == null &&
                    e.BoneMods.Count == 0 && e.MaterialOverrides.Count == 0 && e.DynamicBones.Count == 0 &&
                    (has355 ? e.Colors.Select(Doc).SequenceEqual(defs355.Select(Doc)) : rep.Warnings.Any(w => w.StartsWith("edit.defaultsUnavailable", StringComparison.Ordinal))),
                "consequences: a type change: item ← the first row, parent \"\", sets identity, colours the defaults, noShake off, AAC / ME / DBE / ABMX cleared");
            // an item change from the row default to a row whose default parent is not one of the 54 keys stores ""
            string odd = FakeMakerData.Row(355, 17);
            var oddRows = new OddParentRows(data, new Dictionary<string, string> { [FakeMakerData.Row(355, 0)] = "N_Neck", [odd] = "N_head" });
            Do(t, s.Set(id, P(A1, "parent"), "N_Neck"), "consequences: a1 on its row's default parent");
            r = Tx(s, "Change item", tx => MakerEdits.Edit(tx, s.Model, N(s, id), new List<(string, object)> { (P(A1, "item"), odd) }, Opts(s, data).WithRows(oddRows), null));
            t.Check(r.Ok && Acc(s, id, A1).Parent == "", "consequences: a new default parent that is not one of the 54 keys is stored as \"\" (the row default)");
            UndoRedo(t, s, id, start, "consequences");
        }

        sealed class OddParentRows : IAppearanceRows
        {
            readonly FakeMakerData d;
            readonly Dictionary<string, string> parents;
            public OddParentRows(FakeMakerData d, string row, string parent) : this(d, new Dictionary<string, string> { [row] = parent }) { }
            public OddParentRows(FakeMakerData d, Dictionary<string, string> parents) { this.d = d; this.parents = parents; }
            public bool HasRow(int category, int sex, string reference) => d.HasRow(category, sex, reference);
            public string FirstRow(int category, int sex) => d.FirstRow(category, sex);
            public IReadOnlyList<string> SkinsForHead(int sex, string headRef) => d.SkinsForHead(sex, headRef);
            public string AccessoryParent(int category, string reference) => reference != null && parents.TryGetValue(reference, out var p) ? p : d.AccessoryParent(category, reference);
            public bool LayoutValues(int category, string reference, out float scale, out float posX, out float posY) => d.LayoutValues(category, reference, out scale, out posX, out posY);
            public bool ListHasMoreThanOne(int category, int sex) => d.ListHasMoreThanOne(category, sex);
            public bool ColourPreset(int category, string reference, out Color4 value, out Color4 swatch) => d.ColourPreset(category, reference, out value, out swatch);
            public bool HairColourPreset(string reference, out HairColourPresetValues values) => d.HairColourPreset(reference, out values);
        }

        static MakerEditOptions WithRows(this MakerEditOptions o, IAppearanceRows rows)
        {
            var c = o.Clone();
            c.Rows = rows;
            return c;
        }

        // ---------------------------------------------------------------- «初期カラー»

        static void DefaultColours(TestRunner t)
        {
            var data = new FakeMakerData();
            string row = FakeMakerData.Row(353, 21), hairRow = FakeMakerData.Row(351, 22), noFacts = FakeMakerData.Row(353, 23);
            data.AccessoryFactsOf[row] = Facts(353, row, false, area2: false, area3: true, alpha: 2);
            data.AccessoryFactsOf[hairRow] = Facts(351, hairRow, true);
            data.AccessoryFactsOf[noFacts] = null;
            var s = Golden(t, out string id);
            Do(t, s.Set(id, P(A0, "item"), row), "«初期カラー»: a0 is a row with areas 1, 3 and alpha renderers");
            var mixed = Enumerable.Range(0, 4).Select(i => new AccessoryColor { Color = new Color4(0.9f, 0.9f, 0.9f, 1f), Gloss = 1.5f, Metallic = -0.5f, Smoothness = 0.25f }).ToArray();
            Do(t, s.Set(id, P(A0, "colors"), mixed), "«初期カラー»: colours moved away");
            s.Commands.ClearHistory();
            string start = Full(s, id);
            var defs = data.AccessoryFactsOf[row].DefaultColors;
            var rep = new MakerEditReport();
            var r = Tx(s, "Edit accessory", tx => MakerEdits.ResetAccessoryColours(tx, s.Model, N(s, id), A0, Opts(s, data), rep));
            var c = Acc(s, id, A0).Colors;
            t.Check(r.Ok && c[0].Color.Equals(defs[0].Color) && c[0].Gloss == defs[0].Gloss && c[0].Metallic == defs[0].Metallic && c[2].Color.Equals(defs[2].Color) &&
                    c[3].Color.Equals(defs[3].Color) && c[3].Gloss == defs[3].Gloss && c[3].Metallic == defs[3].Metallic,
                "«初期カラー»: areas 1, 3 and the alpha colour ← the prefab defaults (colour, gloss, metallic)");
            t.Check(Doc(c[1]) == Doc(mixed[1]) && c.All(x => x.Smoothness == 0.25f), "«初期カラー»: the unused area 2 and every smoothness kept");
            t.Check(rep.Consequences.Count == 0 && rep.Changed.Count == 9 && s.Commands.UndoList.Count == 1, "«初期カラー»: one entry of nine member sets, no consequences");
            Do(t, s.Set(id, P(A1, "item"), hairRow), "«初期カラー»: a1 is hair-type");
            Fails(t, Tx(s, "Edit accessory", tx => MakerEdits.ResetAccessoryColours(tx, s.Model, N(s, id), A1, Opts(s, data), null)), CommandCodes.Unsupported, "«初期カラー»: hair-type → unsupported");
            Do(t, s.Set(id, P(A1, "item"), noFacts), "«初期カラー»: a1 without facts");
            Fails(t, Tx(s, "Edit accessory", tx => MakerEdits.ResetAccessoryColours(tx, s.Model, N(s, id), A1, Opts(s, data), null)), CommandCodes.Unsupported, "«初期カラー»: no facts → unsupported");
            var mrep = new MakerEditReport();
            var mr = Tx(s, "Edit accessory", tx => MakerEdits.ResetAccessoryColours(tx, s.Model, N(s, id), A1, Opts(s, data, multi: true), mrep));
            t.Check(mr != null && mr.Status == CommandStatus.NoOp && mrep.Warnings.Any(w => w.StartsWith("edit.defaultsUnavailable", StringComparison.Ordinal)), "«初期カラー»: on a multi-selection a row without facts is skipped with a warning");
            Fails(t, Tx(s, "Edit accessory", tx => MakerEdits.ResetAccessoryColours(tx, s.Model, N(s, id), "nobody", Opts(s, data), null)), CommandCodes.NotFound, "«初期カラー»: unknown accessory → not_found");
            Fails(t, Tx(s, "Edit accessory", tx => MakerEdits.ResetAccessoryColours(tx, s.Model, N(s, id), A0, new MakerEditOptions(), null)), CommandCodes.Unsupported, "«初期カラー»: plain Mono without a defaults service → unsupported");
            UndoRedo(t, s, id, start, "«初期カラー»");
        }

        // ---------------------------------------------------------------- «髪の毛の色を取得»

        static void HairColourFetch(TestRunner t)
        {
            var data = new FakeMakerData();
            string hairRow = FakeMakerData.Row(351, 31), normal = FakeMakerData.Row(351, 32);
            data.AccessoryFactsOf[hairRow] = Facts(351, hairRow, true);
            data.AccessoryFactsOf[normal] = Facts(351, normal, false);
            var s = Golden(t, out string id);
            Do(t, s.Set(id, P(A1, "item"), hairRow), "hair fetch: a1 is hair-type");
            for (int k = 0; k < 4; k++)
            {
                string part = HairData.PartNames[k];
                Do(t, s.Set(id, AppearancePaths.Chara("hair." + part + ".baseColor"), new Color4(0.1f * (k + 1), 0.2f, 0.3f, 0.8f)), "hair fetch: part " + part + " base");
                Do(t, s.Set(id, AppearancePaths.Chara("hair." + part + ".smoothness"), 0.15f * (k + 1)), "hair fetch: part " + part + " smoothness");
                Do(t, s.Set(id, AppearancePaths.Chara("hair." + part + ".metallic"), 0.05f * (k + 1)), "hair fetch: part " + part + " metallic");
            }
            s.Commands.ClearHistory();
            string start = Full(s, id);
            for (int k = 0; k < 4; k++)
            {
                var rep = new MakerEditReport();
                var r = Tx(s, "Edit accessory", tx => MakerEdits.MatchAccessoryHairColour(tx, s.Model, N(s, id), A1, k, Opts(s, data), rep));
                var hp = N(s, id).Appearance.Stored.Chara.Hair.Parts[k];
                var c = Acc(s, id, A1).Colors;
                t.Check(r.Ok && c[0].Color.Equals(hp.BaseColor) && c[1].Color.Equals(hp.TopColor) && c[2].Color.Equals(hp.UnderColor) && c[3].Color.Equals(hp.Specular) &&
                        c[0].Smoothness == hp.Smoothness && c[0].Metallic == hp.Metallic && rep.Consequences.Count == 0,
                    "hair fetch: part " + HairData.PartNames[k] + " → colours 1–4, smoothness, metallic (alpha as stored)");
            }
            t.Equal(4, s.Commands.UndoList.Count, "hair fetch: one entry per press");
            Fails(t, Tx(s, "Edit accessory", tx => MakerEdits.MatchAccessoryHairColour(tx, s.Model, N(s, id), A1, 4, Opts(s, data), null)), CommandCodes.BadArgs, "hair fetch: part 4 → bad_args");
            Do(t, s.Set(id, P(A0, "item"), normal), "hair fetch: a0 is a normal row");
            Fails(t, Tx(s, "Edit accessory", tx => MakerEdits.MatchAccessoryHairColour(tx, s.Model, N(s, id), A0, 0, Opts(s, data), null)), CommandCodes.Unsupported, "hair fetch: a normal row → unsupported");
            UndoRedo(t, s, id, start, "hair fetch");
        }

        // ---------------------------------------------------------------- parent

        static void Parent(TestRunner t)
        {
            var data = new FakeMakerData();
            string fixedRow = FakeMakerData.Row(352, 33), nullRow = FakeMakerData.Row(351, 34);
            var rows = new OddParentRows(data, fixedRow, "0");
            var s = Golden(t, out string id);
            string start = Full(s, id);
            var edit = (Func<string, object, MakerEditOptions, CommandResult>)((acc, v, o) => Tx(s, "Edit accessory", tx => MakerEdits.Edit(tx, s.Model, N(s, id), new List<(string, object)> { (P(acc, "parent"), v) }, o, null)));
            Do(t, edit(A0, "N_Hitai", Opts(s, data)), "parent: a key of the 54");
            t.Equal("N_Hitai", Acc(s, id, A0).Parent, "parent: stored");
            Do(t, edit(A0, "", Opts(s, data)), "parent: \"\" (the row default)");
            var bad = edit(A0, "N_Hitai2", Opts(s, data));
            Fails(t, bad, CommandCodes.BadArgs, "parent: a text that is not one of the 54 keys → bad_args");
            t.Check(bad.Message.Contains("unknown attach point") && Acc(s, id, A0).Parent == "", "parent: the message says unknown attach point; nothing changed");
            // a stored unknown value (imported) may be kept
            Do(t, s.Set(id, P(A1, "parent"), "N_head"), "parent: a raw set stores an imported text (bridge raw / import)");
            Do(t, edit(A1, "N_head", Opts(s, data)), "parent: keeping a stored unknown value is allowed");
            // a fixed row keeps its parent
            var opts = Opts(s, data).WithRows(rows);
            Do(t, s.Set(id, P(A0, "item"), fixedRow), "parent: a0 is a fixed row (Parent \"0\")");
            var rep = new MakerEditReport();
            var r = Tx(s, "Edit accessory", tx => MakerEdits.Edit(tx, s.Model, N(s, id), new List<(string, object)> { (P(A0, "parent"), "N_Neck") }, opts, rep));
            t.Check(r != null && r.Status != CommandStatus.Error && Acc(s, id, A0).Parent == "" && rep.Warnings.Any(w => w.StartsWith("acc.parentFixed:", StringComparison.Ordinal)),
                "parent: a fixed row keeps its parent with the warning acc.parentFixed");
            // «初期親»: the row's key, else ""
            t.Check(MakerEdits.AccessoryDefaultParent(data, 352, FakeMakerData.Row(352, 1)) == (data.AccessoryParent(352, FakeMakerData.Row(352, 1)) ?? "") &&
                    MakerEdits.AccessoryDefaultParent(new OddParentRows(data, nullRow, "null"), 351, nullRow) == "" && MakerEdits.AccessoryDefaultParent(rows, 352, fixedRow) == "" &&
                    MakerEdits.AccessoryDefaultParent(new OddParentRows(data, nullRow, "N_head"), 351, nullRow) == "" && MakerEdits.AccessoryDefaultParent(null, 351, nullRow) == "",
                "parent: «初期親» = the row's Parent when it is one of the 54 keys, else \"\" (a head-weighted \"null\" row, a fixed row, a text, no lists)");
            t.Check(MakerEdits.AccessoryParentAllowed("") && MakerEdits.AccessoryParentAllowed("N_Kokan") && !MakerEdits.AccessoryParentAllowed("null") && !MakerEdits.AccessoryParentAllowed("0") &&
                    !MakerEdits.AccessoryParentAllowed(null) && DocEnum.AttachPoints.All(MakerEdits.AccessoryParentAllowed), "parent: the allowed values are \"\" and the 54 keys");
            UndoRedo(t, s, id, start, "parent");
        }

        // ---------------------------------------------------------------- ↺ to the prefab defaults

        static void PrefabResets(TestRunner t)
        {
            var data = new FakeMakerData();
            string row = FakeMakerData.Row(353, 41), noFacts = FakeMakerData.Row(353, 42);
            data.AccessoryFactsOf[row] = Facts(353, row, false, alpha: 1);
            var s = Golden(t, out string id);
            Do(t, s.Set(id, P(A0, "item"), row), "resets: a0 is a row with facts");
            Do(t, s.Set(id, P(A1, "item"), noFacts), "resets: a1 a row whose facts come from TryAccessoryColors only");
            var refs = s.MakerReferences;
            refs.Capture(id, N(s, id).Appearance.Stored);
            Do(t, s.Set(id, P(A0, "colors[2].gloss"), 1.75f), "resets: gloss away");
            Do(t, s.Set(id, P(A0, "colors[3].metallic"), -0.5f), "resets: alpha metallic away");
            Do(t, s.Set(id, P(A0, "colors[0].smoothness"), 0.01f), "resets: smoothness away");
            s.Commands.ClearHistory();
            string start = Full(s, id);
            var defs = data.AccessoryFactsOf[row].DefaultColors;
            var rep = new MakerEditReport();
            var r = Tx(s, "Reset value", tx => MakerEdits.ResetAppearanceWithPrefabDefaults(tx, s.Model, N(s, id), refs,
                new[] { P(A0, "colors[2].gloss"), P(A0, "colors[3].metallic"), P(A0, "colors[0].smoothness") }, null, Opts(s, data), rep));
            var c = Acc(s, id, A0).Colors;
            float refSmooth = refs.TryGet(id, P(A0, "colors[0].smoothness"), out var rv) && rv is float f0 ? f0 : float.NaN;
            t.Check(r.Ok && c[2].Gloss == defs[2].Gloss && c[3].Metallic == defs[3].Metallic && c[0].Smoothness == refSmooth,
                "resets: ↺ of «ツヤ» / «質感» (also of the alpha colour) → the prefab default; of the smoothness → the reference");
            Do(t, s.Set(id, P(A1, "colors[1].metallic"), 3f), "resets: a1 metallic away");
            data.TryAccessoryColors(353, noFacts, out var cols);
            rep = new MakerEditReport();
            r = Tx(s, "Reset value", tx => MakerEdits.ResetAppearanceWithPrefabDefaults(tx, s.Model, N(s, id), refs, new[] { P(A1, "colors[1].metallic") }, null, Opts(s, data), rep));
            t.Check(r.Ok && cols != null && Acc(s, id, A1).Colors[1].Metallic == cols[1].Metallic, "resets: without facts the defaults of TryAccessoryColors");
            rep = new MakerEditReport();
            r = Tx(s, "Reset value", tx => MakerEdits.ResetAppearanceWithPrefabDefaults(tx, s.Model, N(s, id), refs, new[] { P(A1, "colors[1].metallic") }, null, new MakerEditOptions(), rep));
            t.Check(r != null && r.Status != CommandStatus.Error && rep.Warnings.Any(w => w.StartsWith("edit.defaultsUnavailable", StringComparison.Ordinal)),
                "resets: without a defaults service the reference value and edit.defaultsUnavailable");
            t.Check(MakerEdits.TryAccessoryPrefabDefaultMember(FieldPath.Canonical(P(A0, "colors[3].gloss")), out string aid, out int n, out string m) && aid == A0 && n == 3 && m == "gloss" &&
                    !MakerEdits.TryAccessoryPrefabDefaultMember(FieldPath.Canonical(P(A0, "colors[3].color")), out _, out _, out _) &&
                    !MakerEdits.TryAccessoryPrefabDefaultMember(FieldPath.Canonical(P(A0, "colors[0].smoothness")), out _, out _, out _) &&
                    !MakerEdits.TryAccessoryPrefabDefaultMember(AppearancePaths.Coord("clothes.top.colors[0].gloss"), out _, out _, out _),
                "resets: the prefab-default members are gloss and metallic of the four accessory colours");
            UndoRedo(t, s, id, start, "resets");
        }

        // ---------------------------------------------------------------- gates

        sealed class InfoStub : IAccessoryInfo
        {
            public readonly Dictionary<int, AccessoryFacts> BySlot = new Dictionary<int, AccessoryFacts>();
            public bool TrySlot(string nodeId, int slot, out AccessoryFacts facts) => BySlot.TryGetValue(slot, out facts);
            public bool TryAccessory(string nodeId, string accessoryId, out int slot, out AccessoryFacts facts) { slot = -1; facts = null; return false; }
        }

        static void Gates(TestRunner t)
        {
            var data = new FakeMakerData();
            var s = Golden(t, out string id);
            var coord = Coord(s, id);
            bool G(int slot, string f, IAccessoryInfo info = null, IPartDefaults defs = null, IAppearanceRows rows = null) =>
                MakerEdits.AccessoryGateHolds(MakerEdits.AccessoryGatePrefix + slot + ":" + f, id, coord, info, defs, rows);
            // plain Mono: every prefab feature holds except the hair-type ones; an empty slot nothing
            t.Check(new[] { "prefab", "colour", "area1", "area2", "area3", "alpha", "move1", "move2", "parent" }.All(f => G(3, f)) && !G(3, "hair") && !G(3, "top") && !G(3, "under") && !G(3, "head"),
                "gates: no service — the prefab features hold, the hair-type ones and head do not");
            t.Check(MakerEdits.AccessoryGateFeatures.All(f => !G(1, f)) && MakerEdits.AccessoryGateFeatures.All(f => !G(20, f)), "gates: a slot without an accessory holds nothing");
            // row facts (no realization): a hair-type row
            string hairRow = FakeMakerData.Row(351, 51);
            data.AccessoryFactsOf[hairRow] = Facts(351, hairRow, true, area2: true, area3: false, move2: false, parent: "null");
            Do(t, s.Set(id, P(A1, "item"), hairRow), "gates: a1 (slot 0) is a hair-type row");
            t.Check(G(0, "hair", null, data) && G(0, "top", null, data) && !G(0, "under", null, data) && !G(0, "colour", null, data) && !G(0, "area1", null, data) && !G(0, "alpha", null, data) &&
                    G(0, "move1", null, data) && !G(0, "move2", null, data) && G(0, "head", null, data) && G(0, "parent", null, data) && G(0, "prefab", null, data),
                "gates: the row's facts — hair, top (useColor02), not under / colour / areas / alpha, set 1 only, head-weighted (parent stays)");
            // a normal row with alpha renderers via the realization stub
            var info = new InfoStub();
            info.BySlot[3] = Facts(352, "x", false, area2: false, area3: true, alpha: 1, parent: "0");
            t.Check(G(3, "colour", info) && G(3, "area1", info) && !G(3, "area2", info) && G(3, "area3", info) && G(3, "alpha", info) && !G(3, "hair", info) && !G(3, "parent", info) && !G(3, "head", info),
                "gates: the realization's facts win — areas 1 and 3, alpha, a fixed row (no parent section)");
            t.Check(MakerEdits.AccessoryGateFeatures.Where(f => f != "parent" && f != "head").All(f => !G(0, f, info)) && G(0, "parent", info, null, null),
                "gates: a registered realization without facts for a slot — no prefab feature (the row's Parent column still decides the parent section)");
            var nofacts = new AccessoryFacts();
            info.BySlot[0] = nofacts;
            t.Check(!G(0, "prefab", info) && !G(0, "move1", info) && !G(0, "colour", info), "gates: a prefab without a CmpAccessory holds no prefab feature");
            // the gate context of MakerGate (both entry points agree)
            var ctx = new GateContext { Sex = 1, NodeId = id, Coord = coord, AccessoryInfo = info };
            t.Check(MakerEdits.GateHolds("maker.acc:3:alpha", ctx) && !MakerEdits.GateHolds("maker.acc:3:parent", ctx) && !MakerEdits.GateHolds("maker.acc:3:bogus", ctx) && MakerEdits.GateHolds("maker.bogus", ctx),
                "gates: MakerEdits.GateHolds evaluates the acc family; a malformed acc gate does not hold");
        }

        // ---------------------------------------------------------------- the startup fold

        static AccessoryEntry R(string id, params (string rule, string mod, string link)[] rules)
        {
            var e = new AccessoryEntry { Id = id, Category = 351, Item = "illusion.hs2#0" };
            if (rules.Length > 0) e.Rules = new AccessoryRules { Visibility = rules.Select(r => new AacRule { Rule = r.rule, Modifier = r.mod, Link = r.link }).ToList() };
            return e;
        }

        static string Hidden(CoordDocument c, int[] state, out bool loop) => string.Join(",", AccessoryStartupFold.Hidden(c, state, null, out loop).OrderBy(x => x, StringComparer.Ordinal));

        static void Fold(TestRunner t)
        {
            var c = new CoordDocument();
            c.Clothes.Parts[0].Item = "test.top#1";          // the top is worn
            c.Clothes.Parts[4].Item = "test.gloves#1";       // the gloves too; the bottom is the none row
            c.Accessories.Add(R("startupOnly", ("STARTUP", "NONE", null)));
            c.Accessories.Add(R("startupStudio", ("STARTUP", "NONE", null), ("STUDIO_LOAD", "NONE", null)));
            c.Accessories.Add(R("topOn", ("TOP", "ON", null), ("STUDIO_LOAD", "NONE", null)));
            c.Accessories.Add(R("topOff", ("TOP", "OFF", null), ("STUDIO_LOAD", "NONE", null)));
            c.Accessories.Add(R("bottomOn", ("BOT", "ON", null), ("STUDIO_LOAD", "NONE", null)));
            c.Accessories.Add(R("gloveHalf", ("GLOVE", "HALF", null), ("STUDIO_LOAD", "NONE", null)));
            c.Accessories.Add(R("topAllNoStudio", ("TOP", "ALL", null)));
            c.Accessories.Add(R("linked", ("ACCESSORY_LINK", null, "startupStudio")));
            c.Accessories.Add(R("inverse", ("ACCESSORY_INVERSE_LINK", null, "topOff")));
            c.Accessories.Add(R("plain"));
            var dressed = new int[8];
            t.Equal("inverse,linked,startupStudio,topOn", Hidden(c, dressed, out bool loop), "fold: dressed — STARTUP with STUDIO_LOAD hides, a matching TOP:ON hides, a link copies the hide, " +
                "an inverse link of a shown slot hides; STARTUP without STUDIO_LOAD and rules of absent kinds do nothing");
            t.Check(!loop, "fold: the links settle");
            var off = new int[8];
            off[0] = 2;
            off[4] = 2;
            t.Equal("gloveHalf,linked,startupStudio,topOff", Hidden(c, off, out _), "fold: top and gloves off — TOP:OFF hides (its inverse link shows), GLOVE:HALF also matches off (the half rule)");
            var half = new int[8];
            half[0] = 1;
            t.Equal("inverse,linked,startupStudio", Hidden(c, half, out _), "fold: top half — neither TOP:ON nor TOP:OFF matches");
            // overrides and suppressions
            c.RuleOverrides = new AacRuleOverrides();
            c.RuleOverrides.Suppressed.Add(new AacOverride { Rule = "TOP", Modifier = "ON", OverrideRule = "NONE" });
            t.Equal("inverse,linked,startupStudio", Hidden(c, dressed, out _), "fold: a suppressed TOP:ON never hides");
            c.RuleOverrides.Suppressed.Clear();
            c.RuleOverrides.Overrides.Add(new AacOverride { Rule = "GLOVE", Modifier = "ON", OverrideRule = "TOP", OverrideModifier = "ON" });
            var glovesOn = new int[8];
            glovesOn[0] = 2;
            t.Equal("linked,startupStudio,topOff,topOn", Hidden(c, glovesOn, out _), "fold: an override GLOVE:ON → TOP:ON makes TOP:ON react to the gloves (top off, gloves on)");
            c.RuleOverrides.Overrides.Clear();
            c.RuleOverrides.Overrides.Add(new AacOverride { Rule = "TOP", Modifier = "ON", OverrideRule = "BOT", OverrideModifier = "ON" });
            t.Equal("bottomOn,inverse,linked,startupStudio", Hidden(c, dressed, out _), "fold: an override TOP:ON → BOT:ON stops the direct TOP:ON test and makes BOT:ON test the top");
            c.RuleOverrides = null;
            // decimal modifiers from an older writer
            c.Accessories.Add(R("decimalOn", ("TOP", "10", null), ("STUDIO_LOAD", "0", null)));
            t.Check(Hidden(c, dressed, out _).Contains("decimalOn"), "fold: a decimal modifier (10 = ON) reads as its name");
            // a loop
            var l = new CoordDocument();
            l.Accessories.Add(R("a", ("ACCESSORY_LINK", null, "b")));
            l.Accessories.Add(R("b", ("ACCESSORY_INVERSE_LINK", null, "a")));
            Hidden(l, dressed, out bool looped);
            t.Check(looped, "fold: a link loop stops after 25 rounds (reported)");
            // initial states: a Status-hidden slot copied by a link, a clothes rule that shows it again
            var init = new Dictionary<string, bool> { ["plain"] = false, ["topOff"] = false };
            var h = AccessoryStartupFold.Hidden(c, dressed, init, out _);
            t.Check(h.Contains("plain") && !h.Contains("topOff") && h.Contains("inverse"), "fold: with initial states a clothes rule that does not match shows the slot (forced shown), others keep theirs");
            t.Check(AccessoryStartupFold.Hidden((CoordDocument)null, (int[])null).Count == 0 && AccessoryStartupFold.Hidden(c, new byte[] { 2, 0, 0, 0, 2 }).Contains("topOff"),
                "fold: null coordinate → nothing; the byte form reads the card's clothes state");
        }

        // ---------------------------------------------------------------- coordinate load: fold and AutoMatchBackHairColor

        static void CoordinateLoad(TestRunner t)
        {
            var data = new FakeMakerData();
            string hairRow = FakeMakerData.Row(351, 61);
            data.AccessoryFactsOf[hairRow] = Facts(351, hairRow, true);
            foreach (bool aac in new[] { true, false })
            {
                var s = Golden(t, out string id);
                string start = Full(s, id);
                var src = DocClone.Clone(Coord(s, id));
                src.Accessories.Clear();
                var hidden = R("c0000000000000000000000000000001", ("STARTUP", "NONE", null), ("STUDIO_LOAD", "NONE", null));
                hidden.ImportSlot = 2;
                var match = new AccessoryEntry { Id = "c0000000000000000000000000000002", Category = 351, Item = hairRow, ImportSlot = 5, Rules = new AccessoryRules { MatchHairColor = true } };
                var other = new AccessoryEntry { Id = "c0000000000000000000000000000003", Category = 351, Item = FakeMakerData.Row(351, 62), ImportSlot = 6, Rules = new AccessoryRules { MatchHairColor = true } };
                data.AccessoryFactsOf[other.Item] = Facts(351, other.Item, false);
                src.Accessories.Add(hidden);
                src.Accessories.Add(match);
                src.Accessories.Add(other);
                var rep = new MakerEditReport();
                var r = Tx(s, "Load coordinate", tx => MakerEdits.ApplyCoordinate(tx, s.Model, N(s, id), src, null, CoordinateApplyMode.Accessories, Opts(s, data, aac: aac), rep));
                Do(t, r, "coordinate (" + (aac ? "AAC on" : "AAC off") + "): accessories mode");
                var back = N(s, id).Appearance.Stored.Chara.Hair.Parts[0];
                var m = Acc(s, id, match.Id);
                bool matched = m.Colors[0].Color.Equals(back.BaseColor) && m.Colors[3].Color.Equals(back.Specular) && m.Colors[0].Smoothness == back.Smoothness;
                t.Check(Map(s, id)[hidden.Id] == !aac && Map(s, id)[match.Id] && Map(s, id)[other.Id], "coordinate: the startup pass hides the STARTUP + STUDIO_LOAD accessory " + (aac ? "(AAC on)" : "only with AAC on"));
                t.Check(matched == aac && Doc(Acc(s, id, other.Id).Colors[0]) == Doc(other.Colors[0]),
                    "coordinate: AutoMatchBackHairColor gives the hair-type accessory the back hair's colours " + (aac ? "(AAC on)" : "only with AAC on") + "; a normal row keeps its colours");
                t.Check(SlotOf(s, id, hidden.Id) == 2 && SlotOf(s, id, match.Id) == 5 && s.Commands.UndoList.Count == 1, "coordinate: the coordinate's slots kept; one entry");
                UndoRedo(t, s, id, start, "coordinate " + (aac ? "AAC on" : "AAC off"));
            }
        }
    }
}
