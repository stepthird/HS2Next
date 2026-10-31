// MakerEdits, the accessories (7; the accessory pages of Phase 4.5): the
// accessory category / item consequences, the parent rule, the accessory gates, and the list commands.
//
// Slots: the slot a user sees is the accessory's build slot (HS2Next.Save.AccessorySlotRule: the preferred slot
// `importSlot` when free, else the lowest free slot in list order). Add and copy write the target slot as the new entry's
// `importSlot`; move-to-slot rewrites it; the list order decides only the slots of entries without a free preferred slot.
// Consequences of a type change (category): parent ← the row default (""), both transform sets ← identity, item ← the
// category's first listed row, noShake ← off, MaterialEditor / ABMX / DynamicBoneEditor data cleared, the
// AdditionalAccessoryControls record cleared. Of an item change: the new row's default parent unless the user re-parented,
// noShake ← off, MaterialEditor / DynamicBoneEditor data and the AdditionalAccessoryControls record cleared, ABMX and the
// transform sets kept. Colours on both (HS2's hair rule): a non-hair → hair-type change copies the back hair's colours,
// hair-type → hair-type keeps them, otherwise the new prefab's defaults (unless «keep colours»).
// Parent writes take "" (the row default) or one of the 54 attach points; a row whose `Parent` column is "0" cannot be
// re-parented. Unity-free.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Content;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    /// <summary>Which parts of an accessory a copy takes.</summary>
    [Flags]
    public enum AccessoryCopyParts { None = 0, Slot = 1, Move1 = 2, Move2 = 4 }

    /// <summary>Mirror of a copied move set: left-right = rotation.y + 180°, up-down = rotation.x + 180° (mod 360).</summary>
    public enum MirrorAxis { None, LeftRight, UpDown }

    /// <summary>HS2's left / right table of the attach points: the 18 pairs of `_L` / `_R` keys among the 54.</summary>
    public static class AccessoryMirror
    {
        /// <summary>The 18 pairs (left, right).</summary>
        public static readonly IReadOnlyList<(string left, string right)> Pairs = new[]
        {
            ("N_Hair_twin_L", "N_Hair_twin_R"), ("N_Hair_pin_L", "N_Hair_pin_R"), ("N_Earring_L", "N_Earring_R"), ("N_Tikubi_L", "N_Tikubi_R"),
            ("N_Back_L", "N_Back_R"), ("N_Waist_L", "N_Waist_R"), ("N_Leg_L", "N_Leg_R"), ("N_Knee_L", "N_Knee_R"), ("N_Ankle_L", "N_Ankle_R"),
            ("N_Foot_L", "N_Foot_R"), ("N_Shoulder_L", "N_Shoulder_R"), ("N_Elbo_L", "N_Elbo_R"), ("N_Arm_L", "N_Arm_R"), ("N_Wrist_L", "N_Wrist_R"),
            ("N_Hand_L", "N_Hand_R"), ("N_Index_L", "N_Index_R"), ("N_Middle_L", "N_Middle_R"), ("N_Ring_L", "N_Ring_R"),
        };

        static readonly Dictionary<string, string> reverse = Build();

        static Dictionary<string, string> Build()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (l, r) in Pairs) { d[l] = r; d[r] = l; }
            return d;
        }

        /// <summary>The other side's key of a paired attach point; any other value (another key, "", a text that is not a key,
        /// null) unchanged.</summary>
        public static string ReverseParent(string parent) => parent != null && reverse.TryGetValue(parent, out var r) ? r : parent;
    }

    public static partial class MakerEdits
    {
        // ================================================================ accessory gates

        /// <summary>The accessory gates' prefix and features (the names of HS2Next.UI.Core.SchemaGates, which the scene layer does
        /// not reference): "maker.acc:&lt;slot&gt;:&lt;feature&gt;" with the 0-based build slot as decimal digits. The order is fixed
        /// (SchemaTests_CharaEditAccessories.Gates).</summary>
        public const string AccessoryGatePrefix = MakerGate.Prefix + MakerGate.Acc + ":";
        public static readonly IReadOnlyList<string> AccessoryGateFeatures = new[]
        {
            "prefab", "colour", "area1", "area2", "area3", "alpha", "hair", "top", "under", "parent", "move1", "move2", "head",
        };

        /// <summary>The part parser of the accessory gate family: a build slot written as 1–4 decimal digits.</summary>
        public static bool TryAccessorySlotPart(string part, out int index)
        {
            index = -1;
            return part != null && part.Length > 0 && part.Length <= 4 && int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out index);
        }

        /// <summary>Splits "maker.acc:&lt;slot&gt;:&lt;feature&gt;" (<see cref="MakerGate.TryParse"/>); false for other gates, a
        /// malformed slot or an unknown feature.</summary>
        public static bool TryAccessoryGate(string gate, out int slot, out string feature)
        {
            if (MakerGate.TryParse(gate, out var g) && g.Family == MakerGate.Acc)
            {
                slot = g.Index;
                feature = g.Feature;
                return true;
            }
            slot = -1;
            feature = null;
            return false;
        }

        /// <summary>The facts of build slot <paramref name="slot"/> for the gates: the realization's (ctx.AccessoryInfo), else the
        /// row's prefab facts (ctx.Defaults) of the document's entry there. <paramref name="known"/> is false when neither
        /// service exists or answers (plain Mono): the gates then assume a normal accessory.</summary>
        static AccessoryFacts GateFacts(GateContext ctx, int slot, AccessoryEntry entry, out bool known)
        {
            AccessoryFacts f = null;
            if (ctx.AccessoryInfo != null && ctx.NodeId != null && ctx.AccessoryInfo.TrySlot(ctx.NodeId, slot, out var rf) && rf != null) f = rf;
            if (f == null && entry != null && ctx.Defaults != null && ctx.Defaults.TryAccessoryFacts(entry.Category, entry.Item, out var df) && df != null) f = df;
            known = f != null || ctx.AccessoryInfo != null;
            return known && f == null ? AccessoryFacts.None() : f;
        }

        /// <summary>
        /// The rules of the accessory gates for a parsed gate (build slot ≥ 0, a feature of <see cref="AccessoryGateFeatures"/>):
        /// with a coordinate document (ctx.Coord) a slot without an accessory holds nothing; the prefab facts are the
        /// realization's, else the row's (<see cref="GateFacts"/>; neither — plain Mono — every prefab feature holds except the
        /// hair-type ones): prefab = a CmpAccessory; colour = not hair-type and an area or alpha renderers; areaN = not
        /// hair-type and `useColor0N`; alpha = not hair-type and alpha renderers; hair = `typeHair`; top / under = hair-type and
        /// `useColor02` / `useColor03`; move1 / move2 = `trfMove01` / `trfMove02`; parent = the row's `Parent` column is not "0";
        /// head = it is "null" (head-weighted: the parent is stored only). The `Parent` column comes from the facts, else from
        /// ctx.Rows.
        /// </summary>
        public static bool AccessoryFeatureHolds(int slot, string feature, GateContext ctx)
        {
            ctx = ctx ?? new GateContext();
            if (slot < 0) return false;
            AccessoryEntry entry = null;
            if (ctx.Coord != null)
            {
                entry = AccessorySlotRule.Assign(ctx.Coord.Accessories).EntryAt(slot);
                if (entry == null) return false;
            }
            var f = GateFacts(ctx, slot, entry, out bool known);
            string column = f?.ParentColumn;
            if (column == null && entry != null && ctx.Rows != null) column = ctx.Rows.AccessoryParent(entry.Category, entry.Item);
            switch (feature)
            {
                case "prefab": return !known || f.HasCmpAccessory;
                case "colour": return !known || f.ColourShown;
                case "area1": case "area2": case "area3": return !known || f.AreaShown(feature[4] - '0');
                case "alpha": return !known || f.AlphaShown;
                case "hair": return known && f.HairShown;
                case "top": return known && f.HairAreaShown(2);
                case "under": return known && f.HairAreaShown(3);
                case "move1": return !known || f.HasCmpAccessory && f.Move1;
                case "move2": return !known || f.HasCmpAccessory && f.Move2;
                case "parent": return column != "0";
                case "head": return column == "null";
            }
            return false;
        }

        /// <summary>An accessory gate of node <paramref name="nodeId"/> with coordinate <paramref name="coord"/> against the
        /// realization's facts <paramref name="info"/>, the row facts <paramref name="defaults"/> and the lists
        /// <paramref name="rows"/> (each may be null); <see cref="AccessoryFeatureHolds"/>'s rules. An unknown accessory gate
        /// does not hold.</summary>
        public static bool AccessoryGateHolds(string gate, string nodeId, CoordDocument coord, IAccessoryInfo info, IPartDefaults defaults = null, IAppearanceRows rows = null) =>
            TryAccessoryGate(gate, out int slot, out string feature) &&
            AccessoryFeatureHolds(slot, feature, new GateContext { NodeId = nodeId, Coord = coord, AccessoryInfo = info, Defaults = defaults, Rows = rows });

        // ================================================================ facts, parent rule

        /// <summary>The prefab facts of an accessory row (IPartDefaults.TryAccessoryFacts), or null.</summary>
        public static AccessoryFacts AccessoryRowFacts(MakerEditOptions o, int category, string item) =>
            o?.Defaults != null && item != null && o.Defaults.TryAccessoryFacts(category, item, out var f) ? f : null;

        static bool IsHairType(MakerEditOptions o, int category, string item)
        {
            var f = AccessoryRowFacts(o, category, item);
            return f != null && f.HasCmpAccessory && f.TypeHair;
        }

        /// <summary>A parent value a write may store: "" (the row's default) or one of the 54 attach points.</summary>
        public static bool AccessoryParentAllowed(string parent) => parent != null && (parent.Length == 0 || AttachPointTable.Vanilla.IsKnown(parent));

        /// <summary>The parent a new accessory or «初期親» stores: the row's `Parent` column when it is one of the 54 attach points,
        /// else "" (the row default: a head-weighted "null" row, a fixed "0" row, a text that is not a key, an unknown row).</summary>
        public static string AccessoryDefaultParent(IAppearanceRows rows, int category, string item)
        {
            string p = rows != null && item != null ? rows.AccessoryParent(category, item) : null;
            return p != null && p.Length > 0 && AttachPointTable.Vanilla.IsKnown(p) ? p : "";
        }

        // The parent check of a caller's set (Plan): bad_args for a value that is neither "" nor an attach point; a row whose
        // `Parent` column is "0" keeps its parent (acc.parentFixed, the set is skipped). Keeping the stored value is allowed.
        static bool CheckAccessoryParent(PlanState ps, string path, object value)
        {
            if (!AppearancePaths.TryAccessory(path, out string id, out string m) || m != "parent") return true;
            if (FieldSchema.Same(ps.Current(path), value)) return true;
            if (value is string v && !AccessoryParentAllowed(v))
                throw Bad(path + ": unknown attach point \"" + v + "\" (one of the 54 keys, or \"\" for the row's default)");
            var entry = ps.Stored.Coord.FindAccessory(id);
            int category = ps.Current(AppearancePaths.Accessory(id, "category")) is int c ? c : entry?.Category ?? -1;
            string item = ps.Current(AppearancePaths.Accessory(id, "item")) as string ?? entry?.Item;
            if (ps.O.Rows != null && item != null && ps.O.Rows.AccessoryParent(category, item) == "0")
            {
                ps.Report.Warn("acc.parentFixed: " + path + " kept (the row's Parent is \"0\": this accessory cannot be re-parented)");
                return false;
            }
            return true;
        }

        // ---- accessories (7; C6, C7, C11)

        static AccessoryMove[] IdentityMoves() => new[] { new AccessoryMove(), new AccessoryMove() };

        static void AccessoryCategory(PlanState ps, string id, int category)
        {
            string pre = AppearancePaths.Accessory(id);
            var entry = ps.Stored.Coord.FindAccessory(id);
            int oldCategory = entry?.Category ?? category;
            string oldItem = entry?.Item;
            ps.Add(pre + ".parent", "", true);
            ps.Add(pre + ".moves", IdentityMoves(), true);
            string item = null;
            if (ps.O.Rows != null) item = ps.O.Rows.FirstRow(category, ps.Sex);
            if (item != null) ps.Add(pre + ".item", item, true);
            else if (!ps.Suppressed(pre + ".item")) ps.Report.Warn("edit.listsUnavailable: " + pre + ".item kept (no first row of category " + category + ")");
            var effectiveItem = ps.Current(pre + ".item") as string;
            AccessoryKindColours(ps, pre, oldCategory, oldItem, category, effectiveItem);
            ps.Add(pre + ".noShake", false, true);
            ps.Add(pre + ".materialOverrides", new List<MaterialOverride>(), true);
            ps.Add(pre + ".boneMods", new List<AccessoryBoneMod>(), true);
            ps.Add(pre + ".dynamicBones", new List<DynamicBoneOverride>(), true);
            ps.Add(pre + ".rules", null, true);
        }

        static void AccessoryColours(PlanState ps, string pre, int category, string item)
        {
            if (item != null && ps.O.Defaults != null && ps.O.Defaults.TryAccessoryColors(category, item, out var cols) && cols != null && cols.Length == AccessoryEntry.ColorCount)
                ps.Add(pre + ".colors", CopyArray(cols), true);
            else ps.Report.Warn("edit.defaultsUnavailable: " + pre + ".colors kept (no prefab defaults for " + item + ")");
        }

        // The colours of a type or item change (HS2's hair rule): non-hair → hair-type copies the back hair's colours (also with
        // «keep colours»), hair-type → hair-type keeps them, otherwise the new prefab's defaults unless «keep colours».
        static void AccessoryKindColours(PlanState ps, string pre, int oldCategory, string oldItem, int category, string item)
        {
            bool to = IsHairType(ps.O, category, item), from = IsHairType(ps.O, oldCategory, oldItem);
            if (to && !from) { BackHairColours(ps, pre); return; }
            if (to) return;
            if (!ps.O.KeepColours) AccessoryColours(ps, pre, category, item);
        }

        /// <summary>The members «髪の毛の色を取得» writes from hair part <paramref name="part"/>: colours 1–4 ← its base, top, under
        /// and specular colour, colour 1's smoothness and metallic ← its own (as stored: alpha and values unchanged).</summary>
        public static List<(string member, object value)> AccessoryHairColourSets(HairPart part) => new List<(string, object)>
        {
            ("colors[0].color", part.BaseColor), ("colors[1].color", part.TopColor), ("colors[2].color", part.UnderColor), ("colors[3].color", part.Specular),
            ("colors[0].smoothness", part.Smoothness), ("colors[0].metallic", part.Metallic),
        };

        // The hair rule on a new entry (an add of a hair-type item: the empty slot counts as non-hair).
        static void BackHairColours(AccessoryEntry e, HairPart back)
        {
            e.Colors[0].Color = back.BaseColor;
            e.Colors[1].Color = back.TopColor;
            e.Colors[2].Color = back.UnderColor;
            e.Colors[3].Color = back.Specular;
            e.Colors[0].Smoothness = back.Smoothness;
            e.Colors[0].Metallic = back.Metallic;
        }

        static void BackHairColours(PlanState ps, string pre)
        {
            var back = ps.Stored.Chara.Hair.Parts[0];
            foreach (var (m, v) in AccessoryHairColourSets(back)) ps.Add(pre + "." + m, v, true);
        }

        static void AccessoryItem(PlanState ps, string id, string item, string oldItemOverride)
        {
            string pre = AppearancePaths.Accessory(id);
            var entry = ps.Stored.Coord.FindAccessory(id);
            int category = ps.Current(pre + ".category") is int c ? c : entry?.Category ?? 0;
            int oldCategory = entry?.Category ?? category;
            string oldItem = oldItemOverride ?? entry?.Item;
            string parent = ps.Current(pre + ".parent") as string ?? "";
            if (ps.O.Rows != null)
            {
                string oldDefault = oldItem != null ? ps.O.Rows.AccessoryParent(oldCategory, oldItem) : null;
                string newDefault = ps.O.Rows.AccessoryParent(category, item);
                // C11: a parent the user moved away from the old row's default is kept; a default that is not one of the 54
                // keys is stored as "" (the row default)
                if (newDefault != null && (parent.Length == 0 || string.Equals(parent, oldDefault, StringComparison.Ordinal)))
                    ps.Add(pre + ".parent", AccessoryParentAllowed(newDefault) ? newDefault : "", true);
            }
            else ps.Report.Warn("edit.listsUnavailable: " + pre + ".parent kept (no list row for " + item + ")");
            AccessoryKindColours(ps, pre, oldCategory, oldItem, category, item);
            ps.Add(pre + ".noShake", false, true);
            ps.Add(pre + ".materialOverrides", new List<MaterialOverride>(), true);
            ps.Add(pre + ".dynamicBones", new List<DynamicBoneOverride>(), true);
            ps.Add(pre + ".rules", null, true);
        }

        // ================================================================ accessories

        static CoordDocument Coord(TreeNode n) =>
            n?.Kind == NodeKind.Chara ? n.Appearance?.Stored?.Coord ?? throw new CommandException(CommandCodes.Unsupported, "character not imported (node " + n.Id + ")")
                : throw new CommandException(CommandCodes.WrongKind, "accessories need a character node");

        static string StateKey(string id) => StateAccessories + "[" + FieldPath.Quote(id) + "]";

        /// <summary>The build slots of a character's coordinate (AccessorySlotRule.Assign of its accessory list).</summary>
        public static AccessorySlotRule AccessorySlots(CoordDocument coord) => AccessorySlotRule.Assign(coord?.Accessories);

        static string SlotText(int slot) => AccessorySlotRule.Label(slot) + " (slot " + slot.ToString(CultureInfo.InvariantCulture) + ")";

        static void CheckFreeSlot(AccessorySlotRule rule, int slot, AccessoryEntry self = null)
        {
            if (slot < 0) throw Bad("slot " + slot.ToString(CultureInfo.InvariantCulture) + " is negative (build slots are 0-based)");
            var at = rule.EntryAt(slot);
            if (at != null && !ReferenceEquals(at, self)) throw Bad("slot " + SlotText(slot) + " is worn by accessory " + at.Id + " (" + at.Item + ")");
        }

        static void WarnNotLoaded(MakerEditReport report, MakerEditOptions o, int slot)
        {
            if (AccessorySlotRule.IsExtraSlot(slot) && o != null && !o.MoreAccessories)
                report?.Warn("acc.slotNotLoaded: slot " + SlotText(slot) + " is a MoreAccessories slot and MoreAccessories is off: the accessory is kept as data and not loaded");
        }

        /// <summary>A new accessory entry of <paramref name="category"/> (351..363) with item <paramref name="reference"/>:
        /// parent = <paramref name="parent"/> or the row's default (<see cref="AccessoryDefaultParent"/>), colours = the prefab
        /// defaults when known.</summary>
        public static AccessoryEntry NewAccessory(string id, int category, string reference, string parent, MakerEditOptions o, MakerEditReport report)
        {
            var e = new AccessoryEntry { Id = id, Category = category, Item = reference, Parent = parent ?? AccessoryDefaultParent(o?.Rows, category, reference) };
            if (parent == null && o?.Rows == null) report?.Warn("edit.listsUnavailable: parent of the new accessory left empty (row default)");
            if (o?.Defaults != null && o.Defaults.TryAccessoryColors(category, reference, out var cols) && cols != null && cols.Length == AccessoryEntry.ColorCount)
                e.Colors = CopyArray(cols);
            else report?.Warn("edit.defaultsUnavailable: colours of the new accessory are the format defaults");
            return e;
        }

        static void CheckCategory(int category)
        {
            if (category < AccessoryCategoryFirst || category > AccessoryCategoryLast)
                throw Bad("accessory category " + category.ToString(CultureInfo.InvariantCulture) + " outside " + AccessoryCategoryFirst + ".." + AccessoryCategoryLast);
        }

        /// <summary>Adds an accessory at list index <paramref name="index"/> (the form before the build slots; the new entry has no
        /// preferred slot). New code names the slot: <see cref="AddAccessory(Transaction, SceneModel, TreeNode, int, string, string, int?, int?, MakerEditOptions, MakerEditReport)"/>.</summary>
        public static CommandResult AddAccessory(Transaction tx, SceneModel model, TreeNode node, int category, string reference, string parent, int? index,
            MakerEditOptions o, MakerEditReport report) =>
            AddAccessory(tx, model, node, category, reference, parent, null, index, o, report);

        /// <summary>
        /// Adds an accessory in <paramref name="tx"/> (insert + state fill; the new id is in report.NewAccessoryIds): into build
        /// slot <paramref name="slot"/> (0-based; it MUST be free, else bad_args naming the accessory there), or with neither
        /// slot nor index into the lowest free slot; the new entry's `importSlot` is that slot and it is appended to the list. A
        /// slot ≥ 20 while MoreAccessories is off is data with the warning `acc.slotNotLoaded`. <paramref name="index"/> (the
        /// form before the build slots, bridge only) inserts at that list index without a preferred slot; slot and index together
        /// are bad_args. The parent is <paramref name="parent"/> ("" or one of the 54 keys) or the row's default. Colours: the
        /// prefab defaults; a hair-type item takes the hair rule of an item change from a non-hair row (HS2 reaches it from the
        /// empty slot through type row 0 and the item pick): colours 1–4 ← the back hair's base, top, under and specular colour,
        /// colour 1's smoothness and metallic ← the back hair's, also with «keep colours».
        /// </summary>
        public static CommandResult AddAccessory(Transaction tx, SceneModel model, TreeNode node, int category, string reference, string parent, int? slot, int? index,
            MakerEditOptions o, MakerEditReport report)
        {
            report = report ?? new MakerEditReport();
            try
            {
                var coord = Coord(node);
                CheckCategory(category);
                if (!JsonText.IsRef(reference)) throw Bad("not a ref \"<guid>#<id>\": \"" + reference + "\"");
                if (parent != null && !AccessoryParentAllowed(parent)) throw Bad("unknown attach point \"" + parent + "\" (one of the 54 keys, or \"\" for the row's default)");
                if (slot != null && index != null) throw Bad("a slot or a list index, not both");
                int sex = node.Appearance.Stored.Chara.Profile.Sex;
                if (o?.Rows != null && !o.Rows.HasRow(category, sex, reference))
                    throw new CommandException(CommandCodes.NotFound, reference + " is not a listed row of category " + category + " for this character");
                int at = coord.Accessories.Count, target = -1;
                if (index != null)
                {
                    at = index.Value;
                    if (at < 0 || at > coord.Accessories.Count) throw Bad("index " + at + " outside 0.." + coord.Accessories.Count);
                }
                else
                {
                    var rule = AccessorySlots(coord);
                    target = slot ?? rule.LowestFree();
                    CheckFreeSlot(rule, target);
                }
                string id = o?.NewId?.Invoke() ?? throw Bad("no id source for new accessories");
                var e = NewAccessory(id, category, reference, parent, o, report);
                if (target >= 0) e.ImportSlot = target;
                if (IsHairType(o, category, reference)) BackHairColours(e, node.Appearance.Stored.Chara.Hair.Parts[0]);
                var r = tx.Execute(new ListInsertCommand(node.Id, AppearancePaths.AccessoryList, at, e));
                if (r.Status == CommandStatus.Error) return r;
                report.Record(node.Id, AppearancePaths.AccessoryList, r.Status, false);
                var s = tx.Set(node.Id, StateKey(id), true);
                if (s.Status == CommandStatus.Error) return s;
                report.NewAccessoryIds.Add(id);
                if (target >= 0) WarnNotLoaded(report, o, target);
                return r;
            }
            catch (CommandException e) { return CommandResult.Error(e.Code, e.Message); }
        }

        /// <summary>Removes an accessory (remove + state prune) in <paramref name="tx"/>.</summary>
        public static CommandResult RemoveAccessory(Transaction tx, SceneModel model, TreeNode node, string accessoryId, MakerEditReport report)
        {
            try
            {
                var coord = Coord(node);
                int i = coord.Accessories.FindIndex(a => a.Id == accessoryId);
                if (i < 0) throw new CommandException(CommandCodes.NotFound, "no accessory " + accessoryId);
                var r = tx.Execute(new ListRemoveCommand(node.Id, AppearancePaths.AccessoryList, i));
                if (r.Status == CommandStatus.Error) return r;
                report?.Record(node.Id, AppearancePaths.AccessoryList, r.Status, false);
                var map = ((CharaNodeState)node.Doc.State).State.Accessories;
                if (map != null && map.ContainsKey(accessoryId))
                {
                    var s = tx.Execute(new MapRemoveCommand(node.Id, StateAccessories, accessoryId));
                    if (s.Status == CommandStatus.Error) return s;
                }
                return r;
            }
            catch (CommandException e) { return CommandResult.Error(e.Code, e.Message); }
        }

        /// <summary>Moves an accessory to list index <paramref name="index"/> (reorders the list; the list order decides only the
        /// build slots of entries without a free preferred slot — <see cref="MoveAccessoryToSlot"/> moves to a slot).</summary>
        public static CommandResult MoveAccessory(Transaction tx, SceneModel model, TreeNode node, string accessoryId, int index, MakerEditReport report)
        {
            try
            {
                var coord = Coord(node);
                int i = coord.Accessories.FindIndex(a => a.Id == accessoryId);
                if (i < 0) throw new CommandException(CommandCodes.NotFound, "no accessory " + accessoryId);
                if (index < 0 || index >= coord.Accessories.Count) throw Bad("index " + index + " outside 0.." + (coord.Accessories.Count - 1));
                if (index == i) return CommandResult.NoOp(null);
                var r = tx.Execute(new ListMoveCommand(node.Id, AppearancePaths.AccessoryList, i, index));
                if (r.Status != CommandStatus.Error) report?.Record(node.Id, AppearancePaths.AccessoryList, r.Status, false);
                return r;
            }
            catch (CommandException e) { return CommandResult.Error(e.Code, e.Message); }
        }

        /// <summary>
        /// Moves accessory <paramref name="accessoryId"/> to build slot <paramref name="slot"/> (0-based) in <paramref name="tx"/>:
        /// the slot MUST be free (or the accessory's own), the entry's preferred slot `importSlot` becomes <paramref name="slot"/>
        /// (the entry is replaced at its list index by a copy with the new preferred slot: the list order and the id stay); the
        /// accessory's own slot with that preferred slot already is a no-op. Entries without a preferred slot may take the
        /// slot it frees. A slot ≥ 20 while MoreAccessories is off warns `acc.slotNotLoaded`.
        /// </summary>
        public static CommandResult MoveAccessoryToSlot(Transaction tx, SceneModel model, TreeNode node, string accessoryId, int slot, MakerEditOptions o, MakerEditReport report)
        {
            report = report ?? new MakerEditReport();
            try
            {
                var coord = Coord(node);
                int i = coord.Accessories.FindIndex(a => a.Id == accessoryId);
                if (i < 0) throw new CommandException(CommandCodes.NotFound, "no accessory " + accessoryId);
                var entry = coord.Accessories[i];
                var rule = AccessorySlots(coord);
                CheckFreeSlot(rule, slot, entry);
                if (rule.SlotOfId(accessoryId) == slot && entry.ImportSlot == slot) return CommandResult.NoOp(null);
                var moved = DocClone.Clone(entry);
                moved.ImportSlot = slot;
                var r = tx.Execute(new ListRemoveCommand(node.Id, AppearancePaths.AccessoryList, i));
                if (r.Status == CommandStatus.Error) return r;
                r = tx.Execute(new ListInsertCommand(node.Id, AppearancePaths.AccessoryList, i, moved));
                if (r.Status == CommandStatus.Error) return r;
                report.Record(node.Id, AppearancePaths.AccessoryList, r.Status, false);
                WarnNotLoaded(report, o, slot);
                return r;
            }
            catch (CommandException e) { return CommandResult.Error(e.Code, e.Message); }
        }

        /// <summary>The mirrored parent key (HS2's table of the 18 `_L` ↔ `_R` pairs, <see cref="AccessoryMirror.ReverseParent"/>);
        /// any other value unchanged.</summary>
        public static string MirrorParent(string parent) => AccessoryMirror.ReverseParent(parent);

        /// <summary>
        /// The parent a slot copy with «左右の親を変更» stores (HS2 stores the row's key where HS2Next stores "" for the row
        /// default, and mirrors that key): the source's parent — "" resolved to the source row's default key
        /// (<see cref="AccessoryDefaultParent"/>) — mirrored by HS2's 18-pair table; stored as "" when the result is the copied
        /// row's own default (an unpaired default, a mirror back onto the default, no lists), else the mirrored key. A stored text
        /// that is not one of the 54 keys is not mirrored and stays as it is.
        /// </summary>
        public static string MirroredCopyParent(AccessoryEntry src, IAppearanceRows rows)
        {
            if (src == null) return "";
            string def = AccessoryDefaultParent(rows, src.Category, src.Item);
            string key = string.IsNullOrEmpty(src.Parent) ? def : src.Parent;
            string mirrored = MirrorParent(key) ?? "";
            return string.Equals(mirrored, def, StringComparison.Ordinal) ? "" : mirrored;
        }

        static float Wrap360(float deg)
        {
            float r = deg % 360f;
            if (r < 0f) r += 360f;
            return r == 0f ? 0f : r;
        }

        /// <summary>A move set mirrored (left-right: rotation.y + 180°, up-down: rotation.x + 180°, mod 360; position and scale
        /// unchanged).</summary>
        public static AccessoryMove Mirrored(AccessoryMove m, MirrorAxis axis)
        {
            var r = DocClone.Clone(m);
            if (axis == MirrorAxis.LeftRight) r.Rotation = new Vec3(r.Rotation.X, Wrap360(r.Rotation.Y + 180f), r.Rotation.Z);
            else if (axis == MirrorAxis.UpDown) r.Rotation = new Vec3(Wrap360(r.Rotation.X + 180f), r.Rotation.Y, r.Rotation.Z);
            return r;
        }

        static List<T> CloneList<T>(List<T> l) where T : DocObject
        {
            var r = new List<T>(l?.Count ?? 0);
            if (l != null) foreach (var x in l) r.Add(x == null ? null : DocClone.Clone(x));
            return r;
        }

        /// <summary>The form before the build slots: <paramref name="to"/> = an existing accessory id, or null = «複製» (a new
        /// accessory in the lowest free slot).</summary>
        public static CommandResult CopyAccessory(Transaction tx, SceneModel model, TreeNode node, string from, string to, AccessoryCopyParts parts,
            bool mirrorParent, MirrorAxis mirror, MakerEditOptions o, MakerEditReport report) =>
            CopyAccessory(tx, model, node, from, null, to, parts, mirrorParent, mirror, o, report);

        /// <summary>
        /// `CvsA_Copy` in <paramref name="tx"/>: the target is build slot <paramref name="toSlot"/> or accessory
        /// <paramref name="toId"/> (not both); neither = «複製», the lowest free slot. <see cref="AccessoryCopyParts.Slot"/> copies
        /// the slot's members (category, item, parent — mirrored by HS2's 18-pair table with <paramref name="mirrorParent"/>, a
        /// row-default parent "" first resolved to the row's key (<see cref="MirroredCopyParent"/>) —,
        /// both move sets, the colours, noShake, hideCategory, hideTiming): to a free slot as a new accessory (new id,
        /// `importSlot` = the slot, appended, the state map shows it), to an occupied slot by overwriting the target's members (its
        /// id and visibility kept). Plugin data: with <see cref="MakerEditOptions.CopyAccessoryPluginData"/> the source's
        /// MaterialEditor items, DynamicBoneEditor entries and AdditionalAccessoryControls rules replace the target's; without it
        /// (vanilla) the target's are cleared, as on an item change; ABMX modifiers never follow. Move1 / Move2 copy one move set
        /// (mirrored by <paramref name="mirror"/>: rotation.y or .x + 180°, mod 360) to an occupied target. Errors: not_found
        /// (source, target id), bad_args (nothing to copy, the source as target, a move set to a free slot, slot and id together,
        /// a negative slot).
        /// </summary>
        public static CommandResult CopyAccessory(Transaction tx, SceneModel model, TreeNode node, string from, int? toSlot, string toId, AccessoryCopyParts parts,
            bool mirrorParent, MirrorAxis mirror, MakerEditOptions o, MakerEditReport report)
        {
            report = report ?? new MakerEditReport();
            try
            {
                var coord = Coord(node);
                var src = coord.FindAccessory(from) ?? throw new CommandException(CommandCodes.NotFound, "no accessory " + from);
                if (parts == AccessoryCopyParts.None) throw Bad("nothing to copy");
                if (toSlot != null && toId != null) throw Bad("a target slot or a target accessory, not both");
                if (toSlot < 0) throw Bad("slot " + toSlot.Value.ToString(CultureInfo.InvariantCulture) + " is negative (build slots are 0-based)");
                var rule = AccessorySlots(coord);
                string target = toId ?? (toSlot != null ? rule.EntryAt(toSlot.Value)?.Id : null);
                bool plugin = o != null && o.CopyAccessoryPluginData;
                string parent = mirrorParent ? MirroredCopyParent(src, o?.Rows) : src.Parent;
                if (target == null)
                {
                    if ((parts & AccessoryCopyParts.Slot) == 0) throw Bad("a move set is copied to an occupied slot; a copy to a free slot copies the whole slot");
                    int slot = toSlot ?? rule.LowestFree();
                    string id = o?.NewId?.Invoke() ?? throw Bad("no id source for new accessories");
                    var e = new AccessoryEntry
                    {
                        Id = id, Category = src.Category, Item = src.Item, Parent = parent,
                        Moves = new[] { DocClone.Clone(src.Moves[0]), DocClone.Clone(src.Moves[1]) }, Colors = CopyArray(src.Colors), NoShake = src.NoShake,
                        HideCategory = src.HideCategory, HideTiming = src.HideTiming, ImportSlot = slot,
                    };
                    if (plugin)
                    {
                        e.MaterialOverrides = CloneList(src.MaterialOverrides);
                        e.DynamicBones = CloneList(src.DynamicBones);
                        e.Rules = src.Rules == null ? null : DocClone.Clone(src.Rules);
                    }
                    var r = tx.Execute(new ListInsertCommand(node.Id, AppearancePaths.AccessoryList, coord.Accessories.Count, e));
                    if (r.Status == CommandStatus.Error) return r;
                    report.Record(node.Id, AppearancePaths.AccessoryList, r.Status, false);
                    var s = tx.Set(node.Id, StateKey(id), true);
                    if (s.Status == CommandStatus.Error) return s;
                    report.NewAccessoryIds.Add(id);
                    WarnNotLoaded(report, o, slot);
                    return r;
                }
                if (string.Equals(from, target, StringComparison.Ordinal)) throw Bad("source and destination are the same slot");
                if (coord.FindAccessory(target) == null) throw new CommandException(CommandCodes.NotFound, "no accessory " + target);
                var sets = new List<AppearanceSet>();
                string pre = AppearancePaths.Accessory(target);
                if ((parts & AccessoryCopyParts.Slot) != 0)
                {
                    sets.Add(new AppearanceSet(pre + ".category", src.Category, false));
                    sets.Add(new AppearanceSet(pre + ".item", src.Item, false));
                    sets.Add(new AppearanceSet(pre + ".parent", parent, false));
                    sets.Add(new AppearanceSet(pre + ".moves", new[] { DocClone.Clone(src.Moves[0]), DocClone.Clone(src.Moves[1]) }, false));
                    sets.Add(new AppearanceSet(pre + ".colors", CopyArray(src.Colors), false));
                    sets.Add(new AppearanceSet(pre + ".noShake", src.NoShake, false));
                    sets.Add(new AppearanceSet(pre + ".hideCategory", src.HideCategory, false));
                    sets.Add(new AppearanceSet(pre + ".hideTiming", src.HideTiming, false));
                    sets.Add(new AppearanceSet(pre + ".materialOverrides", plugin ? CloneList(src.MaterialOverrides) : new List<MaterialOverride>(), false));
                    sets.Add(new AppearanceSet(pre + ".dynamicBones", plugin ? CloneList(src.DynamicBones) : new List<DynamicBoneOverride>(), false));
                    sets.Add(new AppearanceSet(pre + ".rules", plugin && src.Rules != null ? DocClone.Clone(src.Rules) : null, false));
                }
                for (int c = 0; c < 2; c++)
                {
                    var flag = c == 0 ? AccessoryCopyParts.Move1 : AccessoryCopyParts.Move2;
                    if ((parts & flag) == 0) continue;
                    sets.Add(new AppearanceSet(pre + ".moves[" + c + "]", Mirrored(src.Moves[c], mirror), false));
                }
                return Execute(tx, node.Id, sets, report);
            }
            catch (CommandException e) { return CommandResult.Error(e.Code, e.Message); }
        }

        // ================================================================ «初期カラー», «髪の毛の色を取得», prefab-default resets

        static AccessoryEntry Entry(TreeNode node, string accessoryId) =>
            Coord(node).FindAccessory(accessoryId) ?? throw new CommandException(CommandCodes.NotFound, "no accessory " + accessoryId);

        /// <summary>
        /// «初期カラー» of accessory <paramref name="accessoryId"/> in <paramref name="tx"/>: for a non-hair prefab, the colour, gloss and
        /// metallic of every area it uses (`useColor01..03`) and of the alpha colour (alpha renderers) ← the prefab's defaults;
        /// unused areas and every smoothness kept (HS2). One edit (the caller's entry, "Edit accessory"); a prefab that uses no
        /// area is a no-op. Errors: not_found (accessory), unsupported (a hair-type prefab, no facts or defaults for the row; on
        /// a multi-selection the node is skipped with `edit.defaultsUnavailable`).
        /// </summary>
        public static CommandResult ResetAccessoryColours(Transaction tx, SceneModel model, TreeNode node, string accessoryId, MakerEditOptions o, MakerEditReport report)
        {
            try
            {
                var e = Entry(node, accessoryId);
                var f = AccessoryRowFacts(o, e.Category, e.Item);
                string pre = AppearancePaths.Accessory(accessoryId);
                if (f == null) return DefaultsUnavailable(o, report, node, pre + ": no prefab facts for " + e.Item);
                if (f.HasCmpAccessory && f.TypeHair) return CommandResult.Error(CommandCodes.Unsupported, pre + ": " + e.Item + " is a hair-type accessory (no default colours; HS2 shows no «初期カラー» for it)");
                var defs = AccessoryDefaultColours(o, e.Category, e.Item, f);
                if (defs == null) return DefaultsUnavailable(o, report, node, pre + ": no prefab defaults for " + e.Item);
                var sets = new List<(string, object)>();
                for (int n = 0; n < AccessoryEntry.ColorCount; n++)
                {
                    bool used = n < 3 ? f.AreaShown(n + 1) : f.AlphaShown;
                    if (!used || defs[n] == null) continue;
                    string c = pre + ".colors[" + n.ToString(CultureInfo.InvariantCulture) + "].";
                    sets.Add((c + "color", defs[n].Color));
                    sets.Add((c + "gloss", defs[n].Gloss));
                    sets.Add((c + "metallic", defs[n].Metallic));
                }
                if (sets.Count == 0) return CommandResult.NoOp(null);
                return Edit(tx, model, node, sets, o, report);
            }
            catch (CommandException e) { return CommandResult.Error(e.Code, e.Message); }
        }

        /// <summary>The four default colours of an accessory row: the facts' DefaultColors, else IPartDefaults.TryAccessoryColors;
        /// null without either.</summary>
        static AccessoryColor[] AccessoryDefaultColours(MakerEditOptions o, int category, string item, AccessoryFacts f)
        {
            if (f?.DefaultColors != null && f.DefaultColors.Length == AccessoryEntry.ColorCount) return f.DefaultColors;
            return o?.Defaults != null && item != null && o.Defaults.TryAccessoryColors(category, item, out var cols) && cols != null && cols.Length == AccessoryEntry.ColorCount ? cols : null;
        }

        /// <summary>
        /// «髪の毛の色を取得» of accessory <paramref name="accessoryId"/> from hair part <paramref name="hairPart"/> (0 back, 1 front,
        /// 2 side, 3 option) in <paramref name="tx"/>: colours 1–4 ← the part's base, top, under and specular colour, colour 1's
        /// smoothness and metallic ← the part's (the document's stored values). One edit (the caller's entry, "Edit accessory").
        /// Errors: bad_args (part), not_found (accessory), unsupported (not a hair-type prefab, or no facts for the row).
        /// </summary>
        public static CommandResult MatchAccessoryHairColour(Transaction tx, SceneModel model, TreeNode node, string accessoryId, int hairPart, MakerEditOptions o, MakerEditReport report)
        {
            try
            {
                if (hairPart < 0 || hairPart > 3) throw Bad("hair part " + hairPart.ToString(CultureInfo.InvariantCulture) + " outside 0..3 (back, front, side, option)");
                var e = Entry(node, accessoryId);
                string pre = AppearancePaths.Accessory(accessoryId);
                var f = AccessoryRowFacts(o, e.Category, e.Item);
                if (f == null) return CommandResult.Error(CommandCodes.Unsupported, pre + ": no prefab facts for " + e.Item + " (the hair colour fetch is for hair-type accessories)");
                if (!(f.HasCmpAccessory && f.TypeHair)) return CommandResult.Error(CommandCodes.Unsupported, pre + ": " + e.Item + " is not a hair-type accessory");
                var part = node.Appearance.Stored.Chara.Hair.Parts[hairPart];
                var sets = new List<(string, object)>();
                foreach (var (m, v) in AccessoryHairColourSets(part)) sets.Add((pre + "." + m, v));
                return Edit(tx, model, node, sets, o, report);
            }
            catch (CommandException e) { return CommandResult.Error(e.Code, e.Message); }
        }

        /// <summary>The accessory members whose ↺ is the worn prefab's default (HS2's reset sources): «ツヤ» and «質感» of the four
        /// colours.</summary>
        public static readonly IReadOnlyList<string> AccessoryPrefabDefaultMembers = new[] { "gloss", "metallic" };

        /// <summary>True when <paramref name="path"/> (canonical, full) is `appearance.coord.accessories["&lt;id&gt;"].colors[&lt;n&gt;].gloss`
        /// or `.metallic` (n = 0..3): the accessory id, the colour index and the member.</summary>
        public static bool TryAccessoryPrefabDefaultMember(string path, out string id, out int colour, out string member)
        {
            colour = -1;
            member = null;
            if (!AppearancePaths.TryAccessory(path, out id, out string m) || m == null || m.Length < 11 || !m.StartsWith("colors[", StringComparison.Ordinal) || m[8] != ']' || m[9] != '.')
            {
                id = null;
                return false;
            }
            int n = m[7] - '0';
            string rest = m.Substring(10);
            if (n < 0 || n >= AccessoryEntry.ColorCount || !((IList<string>)AccessoryPrefabDefaultMembers).Contains(rest)) { id = null; return false; }
            colour = n;
            member = rest;
            return true;
        }

        /// <summary>The prefab default of an accessory member of <see cref="AccessoryPrefabDefaultMembers"/>, or null without facts.</summary>
        static object AccessoryPrefabDefault(CharaSubtree st, string id, int colour, string member, MakerEditOptions o)
        {
            var e = st?.Coord?.FindAccessory(id);
            if (e == null) return null;
            var defs = AccessoryDefaultColours(o, e.Category, e.Item, AccessoryRowFacts(o, e.Category, e.Item));
            if (defs == null || defs[colour] == null) return null;
            return member == "gloss" ? defs[colour].Gloss : (object)defs[colour].Metallic;
        }
    }
}
