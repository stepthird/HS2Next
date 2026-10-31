// MakerEdits, the clothes page (Phase 4.4): the clothes gates' rules, «初期カラー» (the kind's three colour sets take the worn
// prefab's defaults), the ↺ of the members whose reset is the prefab default (gloss, metallic, pattern layout, rotation), and
// the coordinate load in HS2's three modes. The clothes item consequence (ClothesItem) stays in MakerEdits.cs next to the
// Consequences dispatcher.
//
// Coordinate load: an HS2 coordinate card (imported first) or a .hs2ncoord file is worn by a character of the same sex as
// one command of per-member changes, never a whole-character rebuild:
//   clothes      the eight clothes parts (with their KCOX overlays, MaterialEditor items and pushUp) and the coordinate's
//                name ← the source's; the accessories, the AdditionalAccessoryControls rules and the coordinate's plugin data
//                kept (HS2 «服読込み»)
//   accessories  the accessory list, the rules and the name ← the source's; clothes and plugin data kept (HS2 «装飾読込み»)
//   all          the whole coordinate (name, clothes, accessories, rules, inline ext) and the coordinate-scope plugin data
//                (blocks and the imported hs2.kkex copies) ← the source's through SetCoordinateCommand (HS2 «全て読込み»,
//                Studio's Costume tab)
// The name follows the file in every mode, as HS2's three buttons do (measured on HS2: the current coordinate takes the
// file's name whatever the mode).
// The source's values are written as they are (no item-change consequences: a coordinate brings its own colours), except
// AdditionalAccessoryControls' `AutoMatchBackHairColor` (4.5: the hair-type accessories the load brought with the flag take
// the back hair's colours). The scene's accessory visibility map is filled for new accessory ids (hidden where the plugin's
// Studio startup pass hides them, AccessoryStartupFold) and pruned of gone ones in the same transaction. The
// resources the source references (KCOX images) are copied into the scene's store before the command
// (CopyCoordinateResources; a save keeps only reachable resources, so undo needs no removal). Unity-free.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Content;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    /// <summary>What a coordinate load replaces (HS2's three load buttons; Studio's Costume tab loads <see cref="All"/>).</summary>
    public enum CoordinateApplyMode
    {
        /// <summary>The eight clothes parts and the name; accessories, rules and the coordinate's plugin data kept.</summary>
        Clothes,
        /// <summary>The accessory list, the AdditionalAccessoryControls rules and the name; clothes and plugin data kept.</summary>
        Accessories,
        /// <summary>Everything of the coordinate (name, clothes, accessories, rules, inline ext, plugin data); the sex kept.</summary>
        All,
    }

    public static partial class MakerEdits
    {
        // ================================================================ clothes gates

        /// <summary>The clothes gates' prefix and features (the names of HS2Next.UI.Core.SchemaGates, which the scene layer does
        /// not reference): "maker.clothes:&lt;kind&gt;:&lt;feature&gt;". The order is fixed (SchemaTests_CharaEditClothes.Gates).</summary>
        public const string ClothesGatePrefix = MakerGate.Prefix + MakerGate.Clothes + ":";
        public static readonly IReadOnlyList<string> ClothesGateFeatures = new[]
        {
            "prefab", "set1", "set2", "set3", "alpha1", "alpha2", "alpha3", "pattern1", "pattern2", "pattern3", "break", "opt1", "opt2", "other", "kind",
        };

        /// <summary>The kind index (0..7) of a clothes kind name ("top" … "shoes", ClothesSet.Names), or -1.</summary>
        public static int ClothesKindIndex(string name) => name == null ? -1 : Array.IndexOf(ClothesSet.Names, name);

        /// <summary>The part parser of the clothes gate family: the kind index of a kind name; false for an unknown name.</summary>
        public static bool TryClothesKind(string part, out int index)
        {
            index = ClothesKindIndex(part);
            return index >= 0;
        }

        /// <summary>Splits "maker.clothes:&lt;kind&gt;:&lt;feature&gt;" (<see cref="MakerGate.TryParse"/>); false for other gates, an
        /// unknown kind or feature.</summary>
        public static bool TryClothesGate(string gate, out int kind, out string feature)
        {
            if (MakerGate.TryParse(gate, out var g) && g.Family == MakerGate.Clothes)
            {
                kind = g.Index;
                feature = g.Feature;
                return true;
            }
            kind = -1;
            feature = null;
            return false;
        }

        /// <summary>HS2's Maker offers kind <paramref name="kind"/> to the sex (0 male, 1 female): females all eight; males the top,
        /// bottom, gloves and shoes (their inner, pantyhose and socks ids still load from the female lists).</summary>
        public static bool ClothesKindListed(int kind, int sex) => kind >= 0 && kind < ClothesSet.Count && (sex != 0 || kind == 0 || kind == 1 || kind == 4 || kind == 7);

        /// <summary>The list category of kind <paramref name="kind"/>'s stored item for the sex (ClothesSet's categories: the male inner,
        /// pantyhose and socks kinds use the female lists).</summary>
        public static int ClothesItemCategory(int kind, int sex) => (sex == 0 ? ClothesSet.MaleCategories : ClothesSet.FemaleCategories)[kind];

        /// <summary>
        /// A clothes gate of node <paramref name="nodeId"/> (sex <paramref name="sex"/>, coordinate <paramref name="coord"/>, null: the
        /// format defaults) against the realization's facts <paramref name="info"/> (null — plain Mono — every prefab feature
        /// holds); <see cref="ClothesFeatureHolds"/>'s rules. An unknown clothes gate does not hold.
        /// </summary>
        public static bool ClothesGateHolds(string gate, int sex, string nodeId, CoordDocument coord, IClothesInfo info) =>
            TryClothesGate(gate, out int kind, out string feature) &&
            ClothesFeatureHolds(kind, feature, new GateContext { Sex = sex, NodeId = nodeId, Coord = coord, ClothesInfo = info });

        static ClothesKindFacts KindFacts(IClothesInfo info, string nodeId, int kind) =>
            nodeId != null && info.TryKind(nodeId, kind, out var f) && f != null ? f : ClothesKindFacts.None();

        /// <summary>
        /// The rules of the clothes gates for a parsed gate (kind 0..7, a feature of <see cref="ClothesGateFeatures"/>): a kind the
        /// Maker does not offer the sex holds nothing; otherwise, with the realization's facts (ctx.ClothesInfo; a kind it does not
        /// know has no prefab; null — plain Mono — every prefab feature holds): prefab = a CmpClothes; setN = `useColorN0N` or
        /// `useColorA0N`; alphaN = `useColorA0N`; patternN = setN and the stored pattern of set N is not the none row; break =
        /// `useBreak`; optN = `objOpt0N` holds an object; other = break or an option; kind = no one-piece top (inner top) disables
        /// the bottom (inner bottom).
        /// </summary>
        public static bool ClothesFeatureHolds(int kind, string feature, GateContext ctx)
        {
            ctx = ctx ?? new GateContext();
            if (!ClothesKindListed(kind, ctx.Sex)) return false;
            var info = ctx.ClothesInfo;
            bool known = info != null;
            var f = known ? KindFacts(info, ctx.NodeId, kind) : null;
            switch (feature)
            {
                case "prefab": return !known || f.HasPrefab;
                case "set1": case "set2": case "set3": return !known || f.SetShown(feature[3] - '0');
                case "alpha1": case "alpha2": case "alpha3": return !known || f.AlphaShown(feature[5] - '0');
                case "pattern1": case "pattern2": case "pattern3":
                {
                    int n = feature[7] - '0';
                    string pattern = ctx.Coord?.Clothes?.Parts[kind]?.Colors?[n - 1]?.Pattern ?? DocDefaults.NoneRef;
                    return (!known || f.SetShown(n)) && !string.Equals(pattern, DocDefaults.NoneRef, StringComparison.Ordinal);
                }
                case "break": return !known || f.HasPrefab && f.UseBreak;
                case "opt1": case "opt2": return !known || f.OptionShown(feature[3] - '0');
                case "other": return !known || f.HasPrefab && (f.UseBreak || f.OptionShown(1) || f.OptionShown(2));
                case "kind":
                {
                    if (!known || (kind != 1 && kind != 3)) return true;
                    return !KindFacts(info, ctx.NodeId, kind - 1).OnePiece;
                }
            }
            return false;
        }

        // ================================================================ «初期カラー» and the prefab-default resets

        static CommandResult DefaultsUnavailable(MakerEditOptions o, MakerEditReport report, TreeNode node, string msg)
        {
            if (o != null && o.SkipUnlistedRows)
            {
                report?.Warn("edit.defaultsUnavailable: node " + node.Id + ": " + msg);
                return CommandResult.NoOp(null);
            }
            return CommandResult.Error(CommandCodes.Unsupported, msg);
        }

        /// <summary>The prefab defaults of the three colour sets of kind <paramref name="kind"/>'s stored item (as document values),
        /// or null without facts.</summary>
        static ClothesColor[] ClothesDefaults(CharaSubtree st, int kind, MakerEditOptions o)
        {
            if (o?.Defaults == null) return null;
            var part = st.Coord.Clothes.Parts[kind];
            return o.Defaults.TryClothesColors(ClothesItemCategory(kind, st.Chara.Profile.Sex), part.Item, out var cols) && cols != null && cols.Length == ClothesPart.ColorCount
                ? cols : null;
        }

        /// <summary>
        /// «初期カラー» of kind <paramref name="kind"/> (0..7) on node <paramref name="node"/>: the kind's three colour sets ← the worn
        /// prefab's defaults (main colour, pattern as a 348 reference, pattern colour, gloss, metallic, layout and rotation as
        /// the slider values of HS2's lerps; IPartDefaults.TryClothesColors), one whole-array set (the caller's entry, "Edit
        /// clothes"). bad_args for a kind outside 0..7; without the prefab's defaults unsupported, on a multi-selection
        /// (<see cref="MakerEditOptions.SkipUnlistedRows"/>) the node is skipped with `edit.defaultsUnavailable`.
        /// </summary>
        public static CommandResult ResetClothesColours(Transaction tx, SceneModel model, TreeNode node, int kind, MakerEditOptions o, MakerEditReport report)
        {
            var st = node?.Appearance?.Stored;
            if (st == null) return CommandResult.Error(CommandCodes.Unsupported, "character not imported");
            if (kind < 0 || kind >= ClothesSet.Count) return CommandResult.Error(CommandCodes.BadArgs, "kind " + kind + " outside 0..7 (top … shoes)");
            var cols = ClothesDefaults(st, kind, o);
            string pre = AppearancePaths.Coord("clothes." + ClothesSet.Names[kind]);
            if (cols == null) return DefaultsUnavailable(o, report, node, pre + ": no prefab defaults for " + st.Coord.Clothes.Parts[kind].Item);
            return Edit(tx, model, node, new List<(string, object)> { (pre + ".colors", CopyArray(cols)) }, o, report);
        }

        /// <summary>The colour-set members whose ↺ is the worn prefab's default (HS2's reset sources): «ツヤ», «質感», the four
        /// pattern layout components and «回転».</summary>
        public static readonly IReadOnlyList<string> ClothesPrefabDefaultMembers = new[] { "gloss", "metallic", "layout", "rotation" };

        /// <summary>
        /// True when <paramref name="path"/> (canonical, full) is `appearance.coord.clothes.&lt;kind&gt;.colors[&lt;n&gt;].&lt;member&gt;` with
        /// a member of <see cref="ClothesPrefabDefaultMembers"/>: the kind (0..7), the set index (0..2) and the member.
        /// </summary>
        public static bool TryClothesPrefabDefaultMember(string path, out int kind, out int set, out string member)
        {
            set = -1;
            member = null;
            kind = AppearancePaths.ClothesKindOf(path, out string cm);
            if (kind < 0 || cm == null || !cm.StartsWith("colors[", StringComparison.Ordinal) || cm.Length < 11 || cm[8] != ']' || cm[9] != '.') { kind = -1; return false; }
            int n = cm[7] - '0';
            string m = cm.Substring(10);
            if (n < 0 || n >= ClothesPart.ColorCount || !((IList<string>)ClothesPrefabDefaultMembers).Contains(m)) { kind = -1; return false; }
            set = n;
            member = m;
            return true;
        }

        static object PrefabMemberValue(ClothesColor d, string member) =>
            member == "gloss" ? d.Gloss : member == "metallic" ? d.Metallic : member == "rotation" ? (object)d.Rotation : d.Layout;

        /// <summary>
        /// The sets of a row reset (<see cref="ResetSets"/>) where the clothes members of <see cref="ClothesPrefabDefaultMembers"/>
        /// and the accessory members of <see cref="AccessoryPrefabDefaultMembers"/> (gloss and metallic of the four colours, 4.5)
        /// take the worn prefab's default instead of the reference value: per path (full or relative to `appearance.`, a
        /// component row as "path#c"), gloss / metallic / rotation ← the default of their set, a layout component ← that
        /// component of the default layout (the other components kept), the whole layout ← the default layout. Without the
        /// prefab's defaults (no service, an unknown row) such a path resets like any other row (reference, else
        /// <paramref name="defaults"/>[i]) and warns `edit.defaultsUnavailable`. Values are not clamped.
        /// </summary>
        public static List<(string path, object value)> ResetSetsWithPrefabDefaults(SceneModel model, TreeNode node, EditReferences references, IReadOnlyList<string> paths,
            IReadOnlyList<object> defaults, MakerEditOptions o, MakerEditReport report)
        {
            var sets = new List<(string, object)>();
            if (paths == null) return sets;
            var st = node?.Appearance?.Stored;
            for (int i = 0; i < paths.Count; i++)
            {
                string vec = SplitComponent(paths[i], out int comp);
                string path = FieldPath.Canonical(AppearancePaths.FromRelative(vec ?? ""));
                if (st != null && path != null && TryClothesPrefabDefaultMember(path, out int kind, out int set, out string member) && (comp < 0 || member == "layout"))
                {
                    var cols = ClothesDefaults(st, kind, o);
                    if (cols != null)
                    {
                        object v = PrefabMemberValue(cols[set], member);
                        if (comp >= 0) v = WithComponent(FieldAccessor.Get(model, node.Id, path), comp, ComponentOf(v, comp) ?? 0f);
                        sets.Add((path, v));
                        continue;
                    }
                    report?.Warn("edit.defaultsUnavailable: node " + node.Id + ": " + paths[i] + " reset to the reference value (no prefab defaults for " + st.Coord.Clothes.Parts[kind].Item + ")");
                }
                // 4.5: the gloss and metallic of the four accessory colours reset to the worn prefab's defaults
                if (st != null && path != null && comp < 0 && TryAccessoryPrefabDefaultMember(path, out string accId, out int colour, out string accMember))
                {
                    var v = AccessoryPrefabDefault(st, accId, colour, accMember, o);
                    if (v != null)
                    {
                        sets.Add((path, v));
                        continue;
                    }
                    report?.Warn("edit.defaultsUnavailable: node " + node.Id + ": " + paths[i] + " reset to the reference value (no prefab defaults for " + (st.Coord.FindAccessory(accId)?.Item ?? accId) + ")");
                }
                var one = defaults != null && i < defaults.Count ? new[] { defaults[i] } : null;
                sets.AddRange(ResetSets(model, node, references, new[] { paths[i] }, one, report, out _));
            }
            return sets;
        }

        /// <summary>A row reset of node <paramref name="node"/> in <paramref name="tx"/> with the clothes prefab-default members
        /// (<see cref="ResetSetsWithPrefabDefaults"/>), then the planner; every other path as <see cref="ResetAppearance"/>.</summary>
        public static CommandResult ResetAppearanceWithPrefabDefaults(Transaction tx, SceneModel model, TreeNode node, EditReferences references, IReadOnlyList<string> paths,
            IReadOnlyList<object> defaults, MakerEditOptions o, MakerEditReport report)
        {
            if (node?.Appearance?.Stored == null) return CommandResult.Error(CommandCodes.Unsupported, "character not imported");
            List<(string, object)> sets;
            try { sets = ResetSetsWithPrefabDefaults(model, node, references, paths, defaults, o, report); }
            catch (CommandException e) { return CommandResult.Error(e.Code, e.Message); }
            if (sets.Count == 0) return CommandResult.NoOp(null);
            return Edit(tx, model, node, sets, o, report);
        }

        // ================================================================ coordinate load

        /// <summary>The info finding of the clothes and accessories modes: the character's coordinate-scope plugin data stay.</summary>
        public const string CoordExtKept = "coord.extKept";

        /// <summary>
        /// Copies the resources <paramref name="source"/> references (KCOX images, MaterialEditor textures) from
        /// <paramref name="res"/> into the scene's store (no-op without either). Called before the load command and outside it:
        /// a save keeps only reachable resources, so an undone load leaves nothing behind in a saved file.
        /// </summary>
        public static int CopyCoordinateResources(SceneModel model, CoordDocument source, ResStore res)
        {
            var dst = model?.File?.Res;
            if (dst == null || res == null || source == null || ReferenceEquals(dst, res)) return 0;
            int before = dst.Count;
            dst.CopyFrom(res, CharaSubtree.ReferencedResources(source));
            return dst.Count - before;
        }

        /// <summary>
        /// Wears coordinate <paramref name="source"/> (with its coordinate-scope ext data <paramref name="sourceExt"/>, null = none)
        /// on node <paramref name="node"/> in mode <paramref name="mode"/> inside <paramref name="tx"/> (the caller's entry, "Load
        /// coordinate"): the clothes mode sets the eight `appearance.coord.clothes.&lt;kind&gt;` parts, the accessories mode the
        /// accessory list and `ruleOverrides`, both also `name` (HS2), as the source's values without consequences and with the
        /// info finding `coord.extKept`; the all mode runs <see cref="SetCoordinateCommand"/> (everything but the sex, plus the ext data). The
        /// accessories and all modes then add `chara.state.accessories` keys (shown) for new accessory ids and remove the keys of
        /// gone ones. Errors: wrong_kind, unsupported (no documents), bad_args (no source, a source of the other sex — on a
        /// multi-selection the node is skipped with `edit.coordinateSkipped`).
        /// </summary>
        public static CommandResult ApplyCoordinate(Transaction tx, SceneModel model, TreeNode node, CoordDocument source, ExtScopeData sourceExt, CoordinateApplyMode mode,
            MakerEditOptions o, MakerEditReport report)
        {
            report = report ?? new MakerEditReport();
            if (node == null || node.Kind != NodeKind.Chara) return CommandResult.Error(CommandCodes.WrongKind, "a coordinate is worn by a character node");
            var st = node.Appearance?.Stored;
            if (st == null) return CommandResult.Error(CommandCodes.Unsupported, "character not imported (node " + node.Id + ")");
            if (source == null) return CommandResult.Error(CommandCodes.BadArgs, "no coordinate");
            if (mode != CoordinateApplyMode.Clothes && mode != CoordinateApplyMode.Accessories && mode != CoordinateApplyMode.All)
                return CommandResult.Error(CommandCodes.BadArgs, "mode " + (int)mode + " is not clothes, accessories or all");
            int sex = st.Chara.Profile.Sex;
            if (source.Sex != sex)
            {
                string msg = "the coordinate is for " + SexName(source.Sex) + " characters; character " + node.Id + " is " + SexName(sex);
                if (o != null && o.SkipUnlistedRows)
                {
                    report.Warn("edit.coordinateSkipped: node " + node.Id + ": " + msg);
                    return CommandResult.NoOp(null);
                }
                return CommandResult.Error(CommandCodes.BadArgs, msg);
            }
            var before = AccessoryIds(st.Coord);
            CommandResult r;
            if (mode == CoordinateApplyMode.All)
            {
                var cmd = new SetCoordinateCommand(node.Id, source, sourceExt);
                r = tx.Execute(cmd);
                if (r.Status == CommandStatus.Error) return r;
                foreach (var p in r.Status == CommandStatus.Ok ? cmd.Reported : (IReadOnlyList<string>)SetCoordinateCommand.MemberPaths) report.Record(node.Id, p, r.Status, false);
            }
            else
            {
                var sets = CoordinateSets(source, mode);
                r = Execute(tx, node.Id, sets, report);
                if (r.Status == CommandStatus.Error) return r;
                report.Warn(CoordExtKept + ": node " + node.Id + ": the coordinate's plugin data (ext blocks, hs2.kkex) are kept; only the mode all replaces them");
            }
            if (mode == CoordinateApplyMode.Clothes) return r;
            var m = WornCoordinateAccessories(tx, node, before, o, report);
            if (m.Status == CommandStatus.Error) return m;
            var s = SyncAccessoryState(tx, node, AccessoryControlsOn(o) ? StartupHidden(node, before) : null);
            if (s.Status == CommandStatus.Error) return s;
            return r.Status == CommandStatus.Ok ? r : m.Status == CommandStatus.Ok ? m : s;
        }

        static bool AccessoryControlsOn(MakerEditOptions o) => o == null || o.AccessoryControls;

        static HashSet<string> AccessoryIds(CoordDocument coord)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (coord?.Accessories != null) foreach (var a in coord.Accessories) if (a?.Id != null) ids.Add(a.Id);
            return ids;
        }

        /// <summary>
        /// AdditionalAccessoryControls' `AutoMatchBackHairColor` after a coordinate load (accessories / all modes): every accessory the
        /// load brought (an id not in <paramref name="before"/>) whose rules set the flag and whose prefab is hair-type takes the
        /// back hair's colours (colours 1–4 ← base, top, under, specular; colour 1's smoothness and metallic ← the hair's), in the
        /// same transaction. Only while the AdditionalAccessoryControls module is on; rows without prefab facts are left as
        /// loaded. CharacterAccessory is not honoured on a coordinate load (the flag stays on the entry).
        /// </summary>
        static CommandResult WornCoordinateAccessories(Transaction tx, TreeNode node, HashSet<string> before, MakerEditOptions o, MakerEditReport report)
        {
            var last = CommandResult.NoOp(null);
            var st = node.Appearance?.Stored;
            if (st?.Coord == null || !AccessoryControlsOn(o)) return last;
            var back = st.Chara.Hair.Parts[0];
            foreach (var a in st.Coord.Accessories)
            {
                if (a?.Id == null || before.Contains(a.Id) || a.Rules == null || !a.Rules.MatchHairColor) continue;
                if (!IsHairType(o, a.Category, a.Item)) continue;
                string pre = AppearancePaths.Accessory(a.Id);
                foreach (var (member, value) in AccessoryHairColourSets(back))
                {
                    var r = tx.Set(node.Id, pre + "." + member, value);
                    if (r.Status == CommandStatus.Error) return r;
                    report?.Record(node.Id, FieldPath.Canonical(pre + "." + member) ?? pre + "." + member, r.Status, true);
                    if (r.Status == CommandStatus.Ok) last = r;
                }
            }
            return last;
        }

        // The accessories the Studio startup pass of AdditionalAccessoryControls hides among those a coordinate load brought.
        static HashSet<string> StartupHidden(TreeNode node, HashSet<string> before)
        {
            var coord = node.Appearance?.Stored?.Coord;
            var state = (node.Doc?.State as CharaNodeState)?.State?.Clothes;
            var hidden = AccessoryStartupFold.Hidden(coord, state);
            hidden.RemoveWhere(id => before.Contains(id));
            return hidden;
        }

        static string SexName(int sex) => sex == 0 ? "male" : "female";

        // ---- the sex of an HS2 coordinate card worn by a character (the coordinate load path)

        /// <summary>Where <see cref="CoordinateSexOf"/> took the sex from: the card's own evidence, the file's folder, the target
        /// character, or the importer's default (female).</summary>
        public const string CoordinateSexFromCard = "card", CoordinateSexFromFolder = "folder", CoordinateSexFromCharacter = "character",
            CoordinateSexFromDefault = "default";

        /// <summary>
        /// The sex HS2's coordinate lists give a file: the nearest ancestor folder of <paramref name="path"/> named `male` (0) or
        /// `female` (1) that lies directly in a folder named `coordinate` (case-insensitive; HS2 lists UserData/coordinate/&lt;sex&gt;
        /// with its sub-folders); null when there is none. Both separators are accepted.
        /// </summary>
        public static int? CoordinateSexFromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var parts = path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            // parts[^1] is the file; walk its folders from the nearest one up
            for (int i = parts.Length - 2; i >= 1; i--)
            {
                bool male = string.Equals(parts[i], "male", StringComparison.OrdinalIgnoreCase), female = string.Equals(parts[i], "female", StringComparison.OrdinalIgnoreCase);
                if ((male || female) && string.Equals(parts[i - 1], "coordinate", StringComparison.OrdinalIgnoreCase)) return male ? 0 : 1;
            }
            return null;
        }

        /// <summary>
        /// The sex a coordinate card is worn as: the card's own evidence (<paramref name="evidence"/>: its CoordinateSex block or the
        /// clothes tables of its resolve entries; it always wins), else the folder of the file (<see cref="CoordinateSexFromPath"/>,
        /// HS2's per-sex coordinate lists), else the target character's sex (HS2 loads any file into the current character), else
        /// female (the importer's default). <paramref name="from"/> names the source (CoordinateSexFrom*).
        /// </summary>
        public static int CoordinateSexOf(int? evidence, string path, int? targetSex, out string from)
        {
            if (evidence == 0 || evidence == 1) { from = CoordinateSexFromCard; return evidence.Value; }
            var folder = CoordinateSexFromPath(path);
            if (folder.HasValue) { from = CoordinateSexFromFolder; return folder.Value; }
            if (targetSex == 0 || targetSex == 1) { from = CoordinateSexFromCharacter; return targetSex.Value; }
            from = CoordinateSexFromDefault;
            return 1;
        }

        /// <summary>The raw sets of the clothes and accessories modes (copies of the source's values, no consequences): the
        /// eight parts, or the accessory list and the rules; then the name (HS2 takes the file's name in every mode).</summary>
        public static List<AppearanceSet> CoordinateSets(CoordDocument source, CoordinateApplyMode mode)
        {
            var sets = new List<AppearanceSet>();
            if (mode == CoordinateApplyMode.Clothes)
            {
                for (int k = 0; k < ClothesSet.Count; k++)
                    sets.Add(new AppearanceSet(AppearancePaths.Coord("clothes." + ClothesSet.Names[k]), DocClone.Clone(source.Clothes.Parts[k]), false));
            }
            else if (mode == CoordinateApplyMode.Accessories)
            {
                var list = new List<AccessoryEntry>(source.Accessories.Count);
                foreach (var a in source.Accessories) list.Add(DocClone.Clone(a));
                sets.Add(new AppearanceSet(AppearancePaths.AccessoryList, list, false));
                sets.Add(new AppearanceSet(AppearancePaths.Coord("ruleOverrides"), source.RuleOverrides == null ? null : DocClone.Clone(source.RuleOverrides), false));
            }
            if (mode == CoordinateApplyMode.Clothes || mode == CoordinateApplyMode.Accessories)
                sets.Add(new AppearanceSet(AppearancePaths.Coord("name"), source.Name ?? "", false));
            return sets;
        }

        /// <summary>
        /// The accessory visibility map of node <paramref name="node"/> follows its accessory list inside <paramref name="tx"/>: a key
        /// is removed for every id the list no longer has, a key added for every new id — shown, or hidden when it is in
        /// <paramref name="hidden"/> (the startup pass of a coordinate load); kept ids keep their value. Without hides the result
        /// is what the normalizer would give (AccessoryNormalizer.Normalized is null afterwards).
        /// </summary>
        public static CommandResult SyncAccessoryState(Transaction tx, TreeNode node, ISet<string> hidden = null)
        {
            var coord = node?.Appearance?.Stored?.Coord;
            if (coord == null || !(node.Doc.State is CharaNodeState cs)) return CommandResult.NoOp(null);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in coord.Accessories) ids.Add(a.Id);
            var map = cs.State.Accessories;
            var had = map != null ? new List<string>(map.Keys) : new List<string>();
            var last = CommandResult.NoOp(null);
            foreach (var key in had)
            {
                if (ids.Contains(key)) continue;
                var r = tx.Execute(new MapRemoveCommand(node.Id, StateAccessories, key));
                if (r.Status == CommandStatus.Error) return r;
                if (r.Status == CommandStatus.Ok) last = r;
            }
            var present = new HashSet<string>(had, StringComparer.Ordinal);
            foreach (var a in coord.Accessories)
            {
                if (present.Contains(a.Id)) continue;
                present.Add(a.Id);
                var r = tx.Set(node.Id, StateKey(a.Id), hidden == null || !hidden.Contains(a.Id));
                if (r.Status == CommandStatus.Error) return r;
                if (r.Status == CommandStatus.Ok) last = r;
            }
            return last;
        }
    }
}
