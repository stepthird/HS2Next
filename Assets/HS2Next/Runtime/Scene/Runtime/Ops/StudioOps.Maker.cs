// StudioOps, the Maker edits: character documents edited through `appearance.` paths.
// Every method validates per node, imports a card-backed node first (inside the same transaction), adds the
// rule consequences (MakerEdits, Unity-free), opens one BeginOrJoin (a UI drag or a bridge batch absorbs it) and
// returns an OpResult whose Changed / NoOp / PerTarget / Warnings come from the edit and whose Value is a MakerResult
// (consequence paths, new accessory ids). Values are stored unclamped (the UI clamps through the schema). The list
// facts, prefab defaults, face presets and edit sessions come from MakerServices (registered by the Unity layer); without
// them the ops leave those values unchanged and warn (`edit.defaultsUnavailable`, `edit.listsUnavailable`).
using System;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Save;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>The Value of the Maker ops.</summary>
    public sealed class MakerResult
    {
        /// <summary>Canonical paths the rule consequences wrote (a subset of Changed / NoOp).</summary>
        public readonly List<string> Consequences = new List<string>();
        /// <summary>Ids of the accessories the op created (AddAccessory, CopyAccessory to a new slot).</summary>
        public readonly List<string> NewAccessoryIds = new List<string>();
        /// <summary>The coordinate a coordinate load wore (StudioOps.ApplyCoordinateFile; null for other ops).</summary>
        public CoordinateSource Coordinate;
        /// <summary>What a character load read and did (StudioOps.LoadCharacter, Phase 4.6; null for other ops).</summary>
        public CharacterLoadSource Load;
    }

    /// <summary>What StudioOps.ApplyCoordinateFile read: the file, the coordinate's sex and name, whether it was an HS2 coordinate
    /// card (imported) or a .hs2ncoord file, the mode, and the reader's / importer's findings of warning and error severity.</summary>
    public sealed class CoordinateSource
    {
        public string File;
        public int Sex;
        public string Name;
        public bool Imported;
        public CoordinateApplyMode Mode;
        public readonly List<string> Warnings = new List<string>();
    }

    public sealed partial class StudioOps
    {
        MakerEditOptions MakerOptions(bool allowRebuild, bool multi)
        {
            var o = MakerEditOptions.FromServices(Session);
            o.AllowRebuild = allowRebuild;
            o.SkipUnlistedRows = multi;   // a row of the wrong sex is skipped with a warning on multi-selections
            return o;
        }

        static OpResult Fill(OpResult r, MakerEditReport rep)
        {
            r.Changed.AddRange(rep.Changed);
            r.NoOp.AddRange(rep.NoOp);
            foreach (var kv in rep.PerTarget) r.PerTarget[kv.Key] = kv.Value;
            r.Warnings.AddRange(rep.Warnings);
            var v = new MakerResult();
            v.Consequences.AddRange(rep.Consequences);
            v.NewAccessoryIds.AddRange(rep.NewAccessoryIds);
            r.Value = v;
            return r;
        }

        /// <summary>The argument at fault of an appearance error: the path the message names, else "set".</summary>
        static string ArgOf(CommandResult r, IReadOnlyList<(string path, object value)> sets)
        {
            if (r?.Message != null && sets != null)
                foreach (var (p, _) in sets)
                    if (p != null && r.Message.StartsWith(FieldPath.Canonical(p) ?? p, StringComparison.Ordinal)) return p;
            return "set";
        }

        /// <summary>The history label of a set list: "Change item" for item / type changes, else by area.</summary>
        static string MakerLabel(IReadOnlyList<(string path, object value)> sets, int sex)
        {
            var paths = new List<string>();
            foreach (var (p, _) in sets) paths.Add(p);
            return MakerEdits.IsItemChange(paths, sex) ? Labels.ChangeItem : Labels.ForAppearanceArea(MakerEdits.AreaOf(paths));
        }

        // The sex for list categories (StudioOps.SexOf; an unknown sex counts as female, the format's default).
        static int MakerSexOf(TreeNode n) => SexOf(n) < 0 ? 1 : SexOf(n);

        /// <summary>
        /// The core Maker edit: (appearance path, value) sets on every character of <paramref name="ids"/> in one entry,
        /// each followed by its rule consequences. Paths are full (`appearance.chara.face.shape[3]`); the bridge form
        /// relative to `appearance.` goes through AppearancePaths.FromRelative first. Rebuild-only members (body.uncensor,
        /// studioOverlays) need <paramref name="allowRebuild"/> (the bridge's `rebuild: true`). Label by area unless o.Label.
        /// </summary>
        public OpResult EditAppearance(IEnumerable<string> ids, IReadOnlyList<(string path, object value)> sets, ExecuteOptions o = default, bool allowRebuild = false)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            if (nodes.Count == 0) return OpResult.BadArgs("keys", "at least one character");
            if (sets == null || sets.Count == 0) return OpResult.NothingToDo();
            return RunMaker(nodes, MakerLabel(sets, MakerSexOf(nodes[0])), o, (tx, n, opts, rep) => MakerEdits.Edit(tx, Model, n, sets, opts, rep), allowRebuild, r => ArgOf(r, sets));
        }

        delegate CommandResult MakerStep(Transaction tx, TreeNode node, MakerEditOptions opts, MakerEditReport report);

        OpResult RunMaker(List<TreeNode> nodes, string label, ExecuteOptions o, MakerStep step, bool allowRebuild = false, Func<CommandResult, string> argOf = null)
        {
            MakerServices.UseSession(Session);   // the 4.1 accessor MakerServices.References follows the session the ops run on
            rt.ApplyNow();
            var tx = Open(label, o, out var fail);
            if (tx == null) return fail;
            var rep = new MakerEditReport();
            var opts = MakerOptions(allowRebuild, nodes.Count > 1);
            OpResult result;
            using (tx)
            {
                foreach (var n in nodes)
                {
                    var imp = MakerEdits.EnsureImported(tx, Model, n);
                    if (imp.Status == CommandStatus.Error) return OpResult.From(imp, "key");
                    var r = step(tx, n, opts, rep);
                    if (r.Status == CommandStatus.Error) return OpResult.From(r, argOf?.Invoke(r));
                }
                result = Commit(tx, Fill(OpResult.NothingToDo(), rep));
            }
            rt.ApplyNow();
            return result;
        }

        /// <summary>
        /// An item change: <paramref name="itemPath"/> is a `ref` member (face.head, face.skin, hair.&lt;part&gt;.item,
        /// clothes.&lt;kind&gt;.item, a pattern, mesh or accessory item …) and <paramref name="reference"/> the row (`guid#id`), or
        /// an accessory's `category` with the category number as text ("350" removes the accessory, as HS2's «なし»). One entry
        /// "Change item" with the consequences; rows are checked against the lists for each character's sex.
        /// </summary>
        public OpResult ChangeItem(IEnumerable<string> ids, string itemPath, string reference, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            if (nodes.Count == 0) return OpResult.BadArgs("keys", "at least one character");
            string path = FieldPath.Canonical(AppearancePaths.FromRelative(itemPath));
            if (path == null || !AppearancePaths.IsAppearance(path)) return OpResult.BadArgs("path", "an appearance ref member path");
            bool category = AppearancePaths.TryAccessory(path, out string accId, out string member) && member == "category";
            if (category)
            {
                if (!int.TryParse(reference, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cat)) return OpResult.BadArgs("ref", "a category number");
                if (cat == MakerEdits.AccessoryCategoryNone)
                    return RunMaker(nodes, o.Label ?? Labels.ChangeItem, o, (tx, n, opts, rep) => MakerEdits.RemoveAccessory(tx, Model, n, accId, rep));
                var csets = new List<(string, object)> { (path, cat) };
                return RunMaker(nodes, Labels.ChangeItem, o, (tx, n, opts, rep) => MakerEdits.Edit(tx, Model, n, csets, opts, rep), false, _ => "ref");
            }
            int sex = MakerSexOf(nodes[0]);
            bool accItem = AppearancePaths.TryAccessory(path, out _, out string am) && am == "item";
            if (!accItem && MakerEdits.CategoryOf(path, sex) < 0) return OpResult.BadArgs("path", "a ref member with a list (face.head, hair.front.item, clothes.top.item, …)");
            if (!JsonText.IsRef(reference)) return OpResult.BadArgs("ref", "a content ref \"<guid>#<id>\"");
            var sets = new List<(string, object)> { (path, reference) };
            return RunMaker(nodes, Labels.ChangeItem, o, (tx, n, opts, rep) => MakerEdits.Edit(tx, Model, n, sets, opts, rep), false, _ => "ref");
        }

        /// <summary>
        /// The face preset push: the preset of head row <paramref name="headRef"/> (null = each character's current head)
        /// replaces the `face` block (head included) as one entry "Face preset"; a member whose row is not listed for the
        /// character keeps its value and is reported (`edit.presetUnresolved`). Needs MakerServices.Presets.
        /// </summary>
        public OpResult ApplyFacePreset(IEnumerable<string> ids, string headRef = null, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            if (nodes.Count == 0) return OpResult.BadArgs("keys", "at least one character");
            if (headRef != null && !JsonText.IsRef(headRef)) return OpResult.BadArgs("head", "a head ref \"<guid>#<id>\"");
            if (MakerServices.Presets == null) return OpResult.Unsupported("face presets are not available (no preset service is registered)");
            return RunMaker(nodes, Labels.FacePreset, o, (tx, n, opts, rep) =>
            {
                var chara = n.Appearance.Stored.Chara;
                string head = headRef ?? chara.Face.Head;
                var warnings = new List<string>();
                // a head without a loadable preset asset: the head change, as HS2's push (F5)
                if (!MakerServices.Presets.TryLoad(chara.Profile.Sex, head, out var face, warnings) || face == null)
                    return MakerEdits.FacePresetFallback(tx, Model, n, head, opts, rep);
                foreach (var w in warnings) rep.Warnings.Add(w);
                // members whose rows are not listed keep the character's value (edit.presetUnresolved; a decision)
                return MakerEdits.ApplyFacePreset(tx, Model, n, face, opts, rep);
            }, false, _ => "head");
        }

        /// <summary>
        /// Adds an accessory of <paramref name="category"/> (351..363) with item <paramref name="reference"/> to character
        /// <paramref name="id"/> in build slot <paramref name="slot"/> (0-based; null = the lowest free slot; an occupied slot is
        /// bad_args "slot NN is worn by …"; a slot ≥ 20 while MoreAccessories is off is kept as data with the warning
        /// `acc.slotNotLoaded`): parent = <paramref name="parent"/> ("" or one of the 54 attach points) or the row's default,
        /// colours = the prefab defaults; the slot becomes the entry's preferred slot; the state map shows it. One entry "Add
        /// accessory"; the new id is MakerResult.NewAccessoryIds[0], its slot <see cref="AccessorySlotOf"/>.
        /// <paramref name="index"/> (deprecated, the bridge's list form): insert at that list index without a preferred slot.
        /// </summary>
        public OpResult AddAccessory(string id, int category, string reference, string parent = null, int? index = null, ExecuteOptions o = default, int? slot = null)
        {
            var nodes = Charas(new[] { id }, "key", out var fail);
            if (nodes == null) return fail;
            if (slot != null && index != null) return OpResult.BadArgs("slot", "a slot or a list index, not both");
            return RunMaker(nodes, Labels.AddAccessory, o, (tx, n, opts, rep) => MakerEdits.AddAccessory(tx, Model, n, category, reference, parent, slot, index, opts, rep), false,
                r => r?.Message != null && r.Message.StartsWith("slot", StringComparison.Ordinal) ? "slot" : r?.Message != null && r.Message.Contains("attach point") ? "parent" : "ref");
        }

        /// <summary>The build slot (0-based) of accessory <paramref name="accessoryId"/> of character node <paramref name="n"/> (the
        /// slot table of its coordinate, AccessorySlotRule), or -1 when the node has no such accessory or no documents.</summary>
        public static int AccessorySlotOf(TreeNode n, string accessoryId) =>
            n?.Appearance?.Stored?.Coord != null ? MakerEdits.AccessorySlots(n.Appearance.Stored.Coord).SlotOfId(accessoryId) : -1;

        /// <summary>The slot table of character node <paramref name="n"/>'s coordinate (build slot ↔ entry), or null without documents.</summary>
        public static AccessorySlotRule AccessorySlotsOf(TreeNode n) =>
            n?.Appearance?.Stored?.Coord != null ? MakerEdits.AccessorySlots(n.Appearance.Stored.Coord) : null;

        /// <summary>
        /// Moves accessory <paramref name="accessoryId"/> of character <paramref name="id"/> to build slot <paramref name="slot"/>
        /// (0-based; the bridge's `moveSlot`): the slot MUST be free; the accessory's preferred slot becomes it (the list order and
        /// the id stay). One entry "Move accessory"; a slot ≥ 20 while MoreAccessories is off warns `acc.slotNotLoaded`.
        /// </summary>
        public OpResult MoveAccessoryToSlot(string id, string accessoryId, int slot, ExecuteOptions o = default)
        {
            var nodes = Charas(new[] { id }, "key", out var fail);
            if (nodes == null) return fail;
            return RunMaker(nodes, o.Label ?? Labels.MoveAccessory, o, (tx, n, opts, rep) => MakerEdits.MoveAccessoryToSlot(tx, Model, n, accessoryId, slot, opts, rep), false,
                r => r?.Code == CommandCodes.NotFound ? "accessory" : "slot");
        }

        /// <summary>«初期カラー» of accessory <paramref name="accessoryId"/> (MakerEdits.ResetAccessoryColours): the used areas and the
        /// alpha colour ← the prefab's defaults. One entry "Edit accessory"; unsupported for a hair-type prefab or without facts.</summary>
        public OpResult ResetAccessoryColours(string id, string accessoryId, ExecuteOptions o = default)
        {
            var nodes = Charas(new[] { id }, "key", out var fail);
            if (nodes == null) return fail;
            return RunMaker(nodes, o.Label ?? Labels.EditAccessory, o, (tx, n, opts, rep) => MakerEdits.ResetAccessoryColours(tx, Model, n, accessoryId, opts, rep), false, _ => "accessory");
        }

        /// <summary>«髪の毛の色を取得» of hair-type accessory <paramref name="accessoryId"/> from hair part <paramref name="hairPart"/> (0 back,
        /// 1 front, 2 side, 3 option; MakerEdits.MatchAccessoryHairColour). One entry "Edit accessory"; bad_args for the part,
        /// unsupported for a prefab that is not hair-type.</summary>
        public OpResult MatchAccessoryHairColour(string id, string accessoryId, int hairPart, ExecuteOptions o = default)
        {
            var nodes = Charas(new[] { id }, "key", out var fail);
            if (nodes == null) return fail;
            if (hairPart < 0 || hairPart > 3) return PartArg(hairPart);
            return RunMaker(nodes, o.Label ?? Labels.EditAccessory, o, (tx, n, opts, rep) => MakerEdits.MatchAccessoryHairColour(tx, Model, n, accessoryId, hairPart, opts, rep), false, _ => "accessory");
        }

        /// <summary>«初期親» of accessory <paramref name="accessoryId"/>: its parent ← the row's default (the row's `Parent` column when it
        /// is one of the 54 attach points, else "", MakerEdits.AccessoryDefaultParent). One entry "Edit accessory"; a fixed row
        /// keeps its parent (acc.parentFixed).</summary>
        public OpResult ResetAccessoryParent(string id, string accessoryId, ExecuteOptions o = default)
        {
            var nodes = Charas(new[] { id }, "key", out var fail);
            if (nodes == null) return fail;
            var coord = nodes[0].Appearance?.Stored?.Coord;
            var e = coord?.FindAccessory(accessoryId);
            if (coord != null && e == null) return OpResult.From(CommandResult.Error(CommandCodes.NotFound, "no accessory " + accessoryId), "accessory");
            return RunMaker(nodes, o.Label ?? Labels.EditAccessory, o, (tx, n, opts, rep) =>
            {
                var entry = n.Appearance.Stored.Coord.FindAccessory(accessoryId);
                if (entry == null) return CommandResult.Error(CommandCodes.NotFound, "no accessory " + accessoryId);
                string parent = MakerEdits.AccessoryDefaultParent(opts.Rows, entry.Category, entry.Item);
                return MakerEdits.Edit(tx, Model, n, new List<(string, object)> { (AppearancePaths.Accessory(accessoryId, "parent"), parent) }, opts, rep);
            }, false, _ => "accessory");
        }

        /// <summary>Removes accessory <paramref name="accessoryId"/> (and its state-map key): one entry "Remove accessory".</summary>
        public OpResult RemoveAccessory(string id, string accessoryId, ExecuteOptions o = default)
        {
            var nodes = Charas(new[] { id }, "key", out var fail);
            if (nodes == null) return fail;
            return RunMaker(nodes, Labels.RemoveAccessory, o, (tx, n, opts, rep) => MakerEdits.RemoveAccessory(tx, Model, n, accessoryId, rep), false, _ => "accessory");
        }

        /// <summary>Moves accessory <paramref name="accessoryId"/> to list index <paramref name="index"/> (reorders the list; the list
        /// order decides only the build slots of entries without a free preferred slot — <see cref="MoveAccessoryToSlot"/> moves to
        /// a slot): one entry "Reorder accessories".</summary>
        public OpResult MoveAccessory(string id, string accessoryId, int index, ExecuteOptions o = default)
        {
            var nodes = Charas(new[] { id }, "key", out var fail);
            if (nodes == null) return fail;
            return RunMaker(nodes, Labels.ReorderAccessories, o, (tx, n, opts, rep) => MakerEdits.MoveAccessory(tx, Model, n, accessoryId, index, rep), false, _ => "index");
        }

        /// <summary>
        /// `CvsA_Copy`: copies the slot (its members) or one move set from
        /// <paramref name="from"/> to accessory <paramref name="to"/>, or with <paramref name="to"/> null to a new accessory in the
        /// lowest free slot («複製»); the parent mirrored by HS2's table of the 18 left / right attach points, the move set's
        /// rotation by the mirror axis. Plugin data per the option `maker.copyAccessoryPluginData`. One entry "Copy accessory".
        /// </summary>
        public OpResult CopyAccessory(string id, string from, string to, AccessoryCopyParts parts, bool mirrorParent = false, MirrorAxis mirror = MirrorAxis.None, ExecuteOptions o = default) =>
            CopyAccessory(id, from, null, to, parts, mirrorParent, mirror, o);

        /// <summary>
        /// The copy to a build slot: <paramref name="toSlot"/> (0-based; a free slot gets a new accessory with that preferred
        /// slot, an occupied one is overwritten — its id and visibility kept) or <paramref name="toId"/> (an existing accessory; not
        /// both; neither = «複製», the lowest free slot). Move1 / Move2 need an occupied target. One entry "Copy accessory"; the new
        /// id (when one is made) is MakerResult.NewAccessoryIds[0].
        /// </summary>
        public OpResult CopyAccessory(string id, string from, int? toSlot, string toId, AccessoryCopyParts parts, bool mirrorParent = false, MirrorAxis mirror = MirrorAxis.None,
            ExecuteOptions o = default)
        {
            var nodes = Charas(new[] { id }, "key", out var fail);
            if (nodes == null) return fail;
            if (toSlot != null && toId != null) return OpResult.BadArgs("toSlot", "a target slot or a target accessory, not both");
            return RunMaker(nodes, Labels.CopyAccessory, o, (tx, n, opts, rep) => MakerEdits.CopyAccessory(tx, Model, n, from, toSlot, toId, parts, mirrorParent, mirror, opts, rep), false,
                r => r?.Code == CommandCodes.NotFound && r.Message != null && toId != null && r.Message.EndsWith(" " + toId, StringComparison.Ordinal) ? "to"
                    : r?.Code == CommandCodes.NotFound ? "from" : toSlot != null ? "toSlot" : "parts");
        }

        /// <summary>
        /// A layout push (F18): <paramref name="layoutPath"/> (`appearance.chara.face.mole.layout` or
        /// `….face.makeup.paint[0|1].layout`, also relative to `appearance.`) ← (Scale, Scale, PosX, PosY) of row
        /// <paramref name="rowRef"/> of list 6 / 7 (by id); rotation and `layoutItem` untouched. One entry "Edit face";
        /// bad_args for another path, not_found for an unknown row; needs MakerServices.Rows.
        /// </summary>
        public OpResult ApplyLayoutPreset(IEnumerable<string> ids, string layoutPath, string rowRef, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            if (nodes.Count == 0) return OpResult.BadArgs("keys", "at least one character");
            int cat = MakerEdits.LayoutCategoryOf(layoutPath);
            if (cat < 0) return OpResult.BadArgs("path", "appearance.chara.face.mole.layout or appearance.chara.face.makeup.paint[0|1].layout");
            if (!JsonText.IsRef(rowRef ?? "")) return OpResult.BadArgs("ref", "a layout row ref \"<guid>#<id>\"");
            var rows = MakerServices.Rows;
            if (rows == null) return OpResult.Unsupported("layout presets are not available (no list service is registered)");
            if (!rows.LayoutValues(cat, rowRef, out _, out _, out _))
                return OpResult.From(CommandResult.Error(CommandCodes.NotFound, rowRef + " is not a row of layout list " + cat.ToString(CultureInfo.InvariantCulture)), "ref");
            return RunMaker(nodes, o.Label ?? Labels.EditFace, o, (tx, n, opts, rep) => MakerEdits.ApplyLayoutPreset(tx, Model, n, layoutPath, rowRef, opts, rep), false, _ => "ref");
        }

        /// <summary>
        /// The per-row reset (F1): per character, each path (full or relative to `appearance.`; a
        /// component row as "path#c") ← that character's reference value (this scene session's MakerReferences: its documents
        /// when its edit session first opened), else <paramref name="defaults"/>[i] (the UI passes
        /// FieldSpec.Default; null = none). One entry "Reset value"; values unclamped; a path with neither is skipped
        /// (`edit.resetUnavailable`). Clothes gloss, metallic, pattern layout (also one component) and rotation
        /// (`appearance.coord.clothes.&lt;kind&gt;.colors[n].…`) take the worn prefab's default instead (HS2's ↺; without the
        /// prefab's defaults they reset like the other rows and warn `edit.defaultsUnavailable`).
        /// </summary>
        public OpResult ResetAppearance(IEnumerable<string> ids, IReadOnlyList<string> paths, IReadOnlyList<object> defaults = null, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            if (nodes.Count == 0) return OpResult.BadArgs("keys", "at least one character");
            if (paths == null || paths.Count == 0) return OpResult.NothingToDo();
            if (defaults != null && defaults.Count != paths.Count) return OpResult.BadArgs("defaults", "one default per path (null = none)");
            var refs = Session.MakerReferences;
            // 4.4: the clothes gloss, metallic, pattern layout and rotation reset to the worn prefab's defaults (HS2's ↺ sources)
            return RunMaker(nodes, o.Label ?? Labels.ResetValue, o, (tx, n, opts, rep) => MakerEdits.ResetAppearanceWithPrefabDefaults(tx, Model, n, refs, paths, defaults, opts, rep), false, _ => "paths");
        }

        /// <summary>
        /// A colour swatch push (GB6): <paramref name="path"/> (`appearance.chara.body.skin.color`,
        /// also relative to `appearance.`) ← HSVToRGB(BaseH, BaseS, BaseV) of row <paramref name="rowRef"/> of list 336 (by id,
        /// vanilla ids 0..6: HS2's seven buttons) with alpha 1, on every character of <paramref name="ids"/> (no sex rule: 336
        /// is shared). One entry "Edit body" (by the path's area); the units `skin.body` and `skin.face` run. bad_args for another
        /// path, not_found for an unknown row or an id outside 0..6, unsupported without lists.
        /// 4.3 (J11): <paramref name="path"/> `appearance.chara.hair.&lt;part&gt;.baseColor` with a row of 305
        /// (vanilla ids 0..6) writes the row's base / top / under / specular (HSVToRGB, each target's stored alpha kept), metallic
        /// and smoothness on that part — on all four parts with each character's «前髪と後ろ髪の色を統一» — as caller paths (no
        /// auto derivation); one entry "Edit hair".
        /// </summary>
        public OpResult ApplyColourPreset(IEnumerable<string> ids, string path, string rowRef, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            if (nodes.Count == 0) return OpResult.BadArgs("keys", "at least one character");
            int cat = MakerEdits.ColourPresetCategoryOf(path);
            if (cat < 0) return OpResult.BadArgs("path", "appearance.chara.body.skin.color or appearance.chara.hair.<part>.baseColor");
            if (!JsonText.IsRef(rowRef ?? "")) return OpResult.BadArgs("ref", "a colour preset row ref \"<guid>#<id>\"");
            var rows = MakerServices.Rows;
            if (rows == null) return OpResult.Unsupported("colour presets are not available (no list service is registered)");
            bool known = cat == MakerEdits.HairColourCategory ? rows.HairColourPreset(rowRef, out _) : rows.ColourPreset(cat, rowRef, out _, out _);
            if (!MakerEdits.ColourPresetRowAllowed(cat, rowRef) || !known)
                return OpResult.From(CommandResult.Error(CommandCodes.NotFound, rowRef + " is not a swatch row of colour preset list " + cat.ToString(CultureInfo.InvariantCulture)), "ref");
            string label = o.Label ?? Labels.ForAppearanceArea(MakerEdits.AreaOf(new[] { AppearancePaths.FromRelative(path) }));
            return RunMaker(nodes, label, o, (tx, n, opts, rep) => MakerEdits.ApplyColourPreset(tx, Model, n, path, rowRef, opts, rep), false, _ => "ref");
        }

        /// <summary>The eye copy buttons (F10): `face.eyes.right` ← `face.eyes.left`
        /// (<paramref name="leftToRight"/>) or the reverse, as one whole-eye set per character; one entry "Edit face".</summary>
        public OpResult CopyEye(IEnumerable<string> ids, bool leftToRight, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            if (nodes.Count == 0) return OpResult.BadArgs("keys", "at least one character");
            return RunMaker(nodes, o.Label ?? Labels.EditFace, o, (tx, n, opts, rep) => MakerEdits.CopyEye(tx, Model, n, leftToRight, opts, rep));
        }

        /// <summary>
        /// The Maker gates of the Edit tab for character
        /// <paramref name="id"/>: `maker.headList` (females always; males when the male head list has more than one listed row,
        /// F3), `maker.bodyHeight` (females always; males while MakerServices.MaleHeightPinned — the app's
        /// CharaAssembler.MaleHeightPin075 — is off, Y8) and `maker.list:<c>` (the Maker offers list c to the character's sex).
        /// The 4.4 clothes gates and the 4.5 accessory gates (`maker.acc:<slot>:<feature>`, the facts of MakerServices.Accessories,
        /// else of the row's prefab) are evaluated on the same context.
        /// Other gates: true. Without lists `maker.headList` holds for females only. MakerEdits.GateHolds holds the rules; the
        /// gate is parsed once (MakerGate.TryParse) and, for every Maker gate, this runtime is tracked first so the facts
        /// services read its realization (HairInfo.Track).
        /// </summary>
        public bool MakerGateHolds(string gate, string id)
        {
            if (string.IsNullOrEmpty(gate)) return true;
            var n = id != null ? Model.Find(id) : null;
            if (n == null || n.Kind != NodeKind.Chara) return false;
            if (!MakerGate.TryParse(gate, out var g)) return MakerGate.UnparsedHolds(gate);
            HairInfo.Track(rt);   // the facts of this runtime's realization: hair, clothes and accessories
            ClothesInfo.Track(rt);
            AccessoryInfo.Track(rt);
            bool pinned = MakerServices.MaleHeightPinned?.Invoke() ?? false;
            return MakerEdits.GateHolds(g, new GateContext
            {
                Sex = MakerSexOf(n), Rows = MakerServices.Rows, MaleHeightPinned = pinned, HasList = HS2Next.Content.Maker.MakerLists.HasList,
                NodeId = n.Id, Hair = n.Appearance?.Stored?.Chara?.Hair, HairInfo = MakerServices.Hair,
                Coord = n.Appearance?.Stored?.Coord, ClothesInfo = MakerServices.Clothes,
                AccessoryInfo = MakerServices.Accessories, Defaults = MakerServices.Defaults,
            });
        }

        // ---------------- the clothes page (Phase 4.4)

        static OpResult KindArg() => OpResult.BadArgs("kind", "a clothes kind 0..7 (top, bottom, innerTop, innerBottom, gloves, pantyhose, socks, shoes)");

        /// <summary>
        /// «初期カラー» on every character of <paramref name="ids"/>: clothes kind <paramref name="kind"/>'s three colour sets take the
        /// worn prefab's defaults (main colour, pattern, pattern colour, gloss, metallic, layout, rotation). One entry "Edit
        /// clothes"; bad_args for a kind outside 0..7; unsupported without the prefab's defaults (a none row, an unknown row,
        /// no defaults service); on a multi-selection such characters are skipped with `edit.defaultsUnavailable`.
        /// </summary>
        public OpResult ResetClothesColours(IEnumerable<string> ids, int kind, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            if (nodes.Count == 0) return OpResult.BadArgs("keys", "at least one character");
            if (kind < 0 || kind >= ClothesSet.Count) return KindArg();
            return RunMaker(nodes, o.Label ?? Labels.EditClothes, o, (tx, n, opts, rep) => MakerEdits.ResetClothesColours(tx, Model, n, kind, opts, rep), false, _ => "kind");
        }

        /// <summary>
        /// Wears coordinate <paramref name="source"/> on every character of <paramref name="ids"/> in HS2's mode
        /// <paramref name="mode"/> (clothes: the eight clothes parts; accessories: the accessory list and its rules; all: the
        /// whole coordinate with its plugin data, the sex kept; the name follows the coordinate in every mode, as in HS2), as
        /// per-part changes the realization swaps (no rebuild), the
        /// accessory visibility map filled and pruned; one entry "Load coordinate". The resources the coordinate references are
        /// first copied from <paramref name="res"/> into the scene. A character of the other sex is refused (bad_args; skipped
        /// with `edit.coordinateSkipped` on a multi-selection). <paramref name="sourceExt"/>: the coordinate-scope ext data the
        /// all mode wears (null = none).
        /// </summary>
        public OpResult ApplyCoordinate(IEnumerable<string> ids, CoordDocument source, ResStore res, CoordinateApplyMode mode, ExecuteOptions o = default, ExtScopeData sourceExt = null)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            if (nodes.Count == 0) return OpResult.BadArgs("keys", "at least one character");
            if (source == null) return OpResult.BadArgs("coordinate", "a coordinate document");
            if (mode != CoordinateApplyMode.Clothes && mode != CoordinateApplyMode.Accessories && mode != CoordinateApplyMode.All) return OpResult.BadArgs("mode", "clothes, accessories or all");
            MakerEdits.CopyCoordinateResources(Model, source, res);
            return RunMaker(nodes, o.Label ?? Labels.LoadCoordinate, o, (tx, n, opts, rep) => MakerEdits.ApplyCoordinate(tx, Model, n, source, sourceExt, mode, opts, rep), false, _ => "coordinate");
        }

        /// <summary><see cref="ApplyCoordinate(IEnumerable{string}, CoordDocument, ResStore, CoordinateApplyMode, ExecuteOptions, ExtScopeData)"/>
        /// with a coordinate file read into memory (its document, resources and ext data).</summary>
        public OpResult ApplyCoordinate(IEnumerable<string> ids, CoordContainer source, CoordinateApplyMode mode, ExecuteOptions o = default) =>
            source == null ? OpResult.BadArgs("coordinate", "a coordinate") : ApplyCoordinate(ids, source.Coord, source.Res, mode, o, source.CoordExt);

        /// <summary>
        /// A coordinate load from a file (the Edit tab's coordinate browser, Studio's Costume tab, the bridge): an HS2 coordinate
        /// card (imported first, in memory) or a .hs2ncoord file, worn as <see cref="ApplyCoordinate(IEnumerable{string},
        /// CoordContainer, CoordinateApplyMode, ExecuteOptions)"/>. The result's MakerResult.Coordinate names the file, its sex and
        /// name, whether it was imported, and the reader's findings of warning and error severity. An HS2 card without sex
        /// evidence takes the sex of its folder (`coordinate/male` / `coordinate/female`), else the first character's sex with the
        /// warning coord.sexAssumed (<see cref="LoadCoordinate"/>). bad_args for a missing or unreadable file (or a character
        /// card); unsupported for an HS2 card without the importer.
        /// </summary>
        public OpResult ApplyCoordinateFile(IEnumerable<string> ids, string path, CoordinateApplyMode mode, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var bad);
            if (nodes == null) return bad;
            if (nodes.Count == 0) return OpResult.BadArgs("keys", "at least one character");
            var found = new FindingList();
            var c = LoadCoordinate(path, found, out bool imported, out var fail, MakerSexOf(nodes[0]));
            if (c == null) return fail;
            var r = ApplyCoordinate(ids, c, mode, o);
            var src = new CoordinateSource { File = path, Sex = c.Coord.Sex, Name = c.Coord.Name, Imported = imported, Mode = mode };
            foreach (var f in found)
                if (f.Severity != FindingSeverity.Info) src.Warnings.Add(f.ToString());
            if (r.Value is MakerResult mr) mr.Coordinate = src;
            else r.Value = new MakerResult { Coordinate = src };
            return r;
        }

        /// <summary>
        /// Reads a coordinate file into memory: a .hs2ncoord file with the container reader, any other file as an HS2
        /// coordinate card through the registered importer (findings into <paramref name="found"/>) as a card to be worn: without
        /// sex evidence in the card the folder it lies in decides (`…/coordinate/male` or `…/coordinate/female`, HS2's per-sex
        /// lists), else <paramref name="targetSex"/> (the character it is loaded into) with the warning coord.sexAssumed, else
        /// female; evidence in the card always wins. Null with <paramref name="fail"/> (bad_args "file"; unsupported without an
        /// importer) when it cannot be read.
        /// </summary>
        public CoordContainer LoadCoordinate(string path, FindingList found, out bool imported, out OpResult fail, int? targetSex = null)
        {
            fail = null;
            imported = false;
            found = found ?? new FindingList();
            if (string.IsNullOrEmpty(path)) { fail = OpResult.BadArgs("file", "an HS2 coordinate card or a .hs2ncoord file"); return null; }
            if (!System.IO.File.Exists(path)) { fail = OpResult.BadArgs("file", "an existing file", "coordinate file not found: " + path); return null; }
            if (path.EndsWith(SaveFormats.CoordExtension, StringComparison.OrdinalIgnoreCase))
            {
                LoadResult<CoordContainer> lr;
                try { lr = CoordContainer.Load(path); }
                catch (Exception e) { fail = OpResult.BadArgs("file", null, "coordinate file not readable: " + e.GetType().Name + ": " + e.Message); return null; }
                found.AddRange(lr.Findings);
                if (!lr.Ok) { fail = OpResult.BadArgs("file", null, "coordinate file not readable (" + System.IO.Path.GetFileName(path) + "): " + lr.Error.Message); return null; }
                return lr.Value;
            }
            var importer = CharaImport.Importer as HS2Next.Import.Hs2Importer;
            if (importer == null && Services?.Catalog != null) importer = ImportSetup.ImporterFor(Services.Catalog) as HS2Next.Import.Hs2Importer;
            if (importer == null) { fail = OpResult.Unsupported("HS2 coordinate cards need the card importer (none is registered)"); return null; }
            HS2Next.Import.CoordImportResult res;
            try { res = importer.ImportCoordToWear(path, targetSex); }
            catch (Exception e) { fail = OpResult.BadArgs("file", null, "coordinate card not readable: " + e.GetType().Name + ": " + e.Message); return null; }
            if (res?.Report != null) found.AddRange(res.Report.Findings);
            if (res == null || !res.Ok)
            {
                fail = OpResult.BadArgs("file", "an HS2 coordinate card or a .hs2ncoord file", "not a readable HS2 coordinate card: " + System.IO.Path.GetFileName(path));
                return null;
            }
            imported = true;
            return res.Container;
        }

        // ---------------- the hair page

        static OpResult PartArg(int part) => OpResult.BadArgs("part", "a hair part 0..3 (back, front, side, option)");

        /// <summary>
        /// «肌の色に合わせる» (J12): per character, hair part <paramref name="part"/>'s `underColor` =
        /// HSVToRGB(H, max(0, S − 0.06), max(0, V − 0.06)) of its `body.skin.color` (stored alpha kept) and `smoothness` = max(0,
        /// `body.skin.gloss` − 0.3) — that part only, also with «前髪と後ろ髪の色を統一» on (MakerEditOptions.NoHairLink). One entry
        /// "Edit hair". The prefab's `useSameSkinColorButton` is the page's gate; the op does not check it.
        /// </summary>
        public OpResult MatchHairToSkin(IEnumerable<string> ids, int part, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            if (nodes.Count == 0) return OpResult.BadArgs("keys", "at least one character");
            if (part < 0 || part > 3) return PartArg(part);
            return RunMaker(nodes, o.Label ?? Labels.EditHair, o, (tx, n, opts, rep) => MakerEdits.MatchHairToSkin(tx, Model, n, part, opts, rep), false, _ => "part");
        }

        /// <summary>
        /// A hair bundle edit (3, 6; J6, C10) on every character of <paramref name="ids"/>: bundle
        /// <paramref name="index"/> of part <paramref name="part"/> — the given members (<paramref name="move"/>,
        /// <paramref name="rotate"/> rates, stored unclamped; <paramref name="noShake"/>); the first edit of a part whose stored
        /// bundles are not the effective list materializes the whole list (the prefab defaults) in the same entry; with
        /// «髪の調整をまとめて行う» each changed move / rotate component goes to every bundle of the part. <paramref name="reset"/>:
        /// the given members (move and rotate when none is given; values ignored) return to the prefab default,
        /// <paramref name="axis"/> 0..2 narrowing move / rotate to one component (a slider row's ↺); resets never propagate.
        /// One entry "Edit hair" (a UI drag: preview sets in one transaction, BeginOrJoin). bad_args (part, axis, nothing
        /// given), not_found (index outside the prefab's bundles), unsupported (no prefab facts and no complete stored list,
        /// a reset without facts); on a multi-selection those nodes are skipped (`edit.bundleSkipped`).
        /// </summary>
        public OpResult EditHairBundle(IEnumerable<string> ids, int part, int index, Vec3? move = null, Vec3? rotate = null, bool? noShake = null, bool reset = false,
            int axis = -1, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            if (nodes.Count == 0) return OpResult.BadArgs("keys", "at least one character");
            if (part < 0 || part > 3) return PartArg(part);
            if (axis < -1 || axis > 2) return OpResult.BadArgs("axis", "a component 0..2, or -1 for all");
            if (!reset && move == null && rotate == null && noShake == null) return OpResult.BadArgs("move", "move, rotate, noShake or reset");
            return RunMaker(nodes, o.Label ?? Labels.EditHair, o,
                (tx, n, opts, rep) => MakerEdits.EditHairBundle(tx, Model, n, part, index, move, rotate, noShake, reset, axis, opts, rep), false, _ => "index");
        }

        /// <summary>«全て初期化»: per character, part <paramref name="part"/>'s whole bundle list = the
        /// prefab defaults, each bundle's `noShake` kept. One entry "Edit hair"; unsupported without prefab facts.</summary>
        public OpResult ResetHairBundles(IEnumerable<string> ids, int part, ExecuteOptions o = default)
        {
            var nodes = Charas(ids, "key", out var fail);
            if (nodes == null) return fail;
            if (nodes.Count == 0) return OpResult.BadArgs("keys", "at least one character");
            if (part < 0 || part > 3) return PartArg(part);
            return RunMaker(nodes, o.Label ?? Labels.EditHair, o, (tx, n, opts, rep) => MakerEdits.ResetHairBundles(tx, Model, n, part, opts, rep), false, _ => "part");
        }

        /// <summary>Starts the edit session of character <paramref name="id"/> (decoded layers and resolved inputs kept);
        /// not recorded. The first session of a character in this scene session also captures its reference documents for the
        /// row reset into the session's own store (SceneSession.MakerReferences).
        /// Without a registered session host only the reference is captured.</summary>
        public OpResult BeginEditSession(string id)
        {
            var nodes = Charas(new[] { id }, "key", out var fail);
            if (nodes == null) return fail;
            MakerServices.UseSession(Session);
            Session.MakerReferences.CaptureNode(Session, nodes[0]);
            MakerServices.Sessions?.Begin(nodes[0].Id);
            return OpResult.NothingToDo();
        }

        /// <summary>Ends the edit session of character <paramref name="id"/> (not recorded).</summary>
        public OpResult EndEditSession(string id)
        {
            var nodes = Charas(new[] { id }, "key", out var fail);
            if (nodes == null) return fail;
            MakerServices.Sessions?.End(nodes[0].Id);
            return OpResult.NothingToDo();
        }
    }
}
