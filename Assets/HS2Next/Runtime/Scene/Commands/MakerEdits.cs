// Maker edits on the command bus: the Unity-free core of StudioOps.Maker. An
// edit is a list of (appearance path, value) sets for one character node; the planner checks them (rebuild-only paths,
// item rows of the member's category for the character's sex) and adds the HS2 rule consequences as explicit sets right
// after the set that causes them (head → skin snap, hair item → bundles / decoration colours / MaterialEditor items,
// clothes item → default colours / MaterialEditor items, accessory type and item → parent, moves, item, colours, …, the
// "same setting" / "auto colour" hair switches, linked eyes). A consequence never overrides a path the caller sets in the
// same call (or an object containing it). The sets are executed one by one in the caller's transaction (CommandBus
// .BeginOrJoin), so a drag collapses per path and the commit records one entry; undo and redo replay the same sets.
// Accessory add / remove / move / copy are list commands on `appearance.coord.accessories` plus the `chara.state
// .accessories` fill / prune of the node. Facts that need the game data (prefab default colours, list rows, face presets)
// come through the interfaces below, implemented by the Unity layer; without them the planner leaves those values
// unchanged and reports `edit.defaultsUnavailable` / `edit.listsUnavailable`. The hair page (
// 8) adds the 305 colour presets, the skin match, the bundle edits (materialization, «髪の調整をまとめて行う»,
// resets to the prefab defaults), the hair gates and the facts the Unity layer serves for them (IHairInfo, TryHairBundles).
//
// The class is split by page (Step 0 of Phase 4.4): this file holds the
// categories, the planner (Plan, the row checks, the Consequences dispatcher, the clothes item consequence), the shared
// colour helpers, the execution and the labels; MakerServices.cs the service interfaces, facts and option / report types;
// MakerEdits.Face.cs, .Body.cs, .Hair.cs and .Accessories.cs each page's consequences and ops; MakerGates.cs the Maker
// gates; MakerEdits.Clothes.cs is reserved for the clothes pages (Phase 4.4).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Content;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public static partial class MakerEdits
    {
        public const int AccessoryCategoryNone = 350, AccessoryCategoryFirst = 351, AccessoryCategoryLast = 363;
        /// <summary>The scene path of a character's accessory visibility map.</summary>
        public const string StateAccessories = "chara.state.accessories";

        // Hair colour members that the «前髪と後ろ髪の色を統一» switch applies to all four parts.
        static readonly string[] HairColourMembers = { "baseColor", "topColor", "underColor", "specular", "metallic", "smoothness" };

        static CommandException Bad(string m) => new CommandException(CommandCodes.BadArgs, m);

        // ================================================================ categories

        /// <summary>
        /// The list category of a `ref` member for the sex (0 male, 1 female), or -1 when the path is not a ref member
        ///. Accessory items take the entry's category.
        /// </summary>
        public static int CategoryOf(string path, int sex, CoordDocument coord = null)
        {
            if (!AppearancePaths.TrySplit(path, out var doc, out string m)) return -1;
            bool male = sex == 0;
            if (doc == AppearanceDocument.Chara)
            {
                switch (m)
                {
                    case "face.head": return male ? 110 : 210;
                    case "face.skin": return male ? 111 : 211;
                    case "face.detail.item": return male ? 112 : 212;
                    case "face.eyebrow.item": return 314;
                    case "face.eyes.left.iris.item": case "face.eyes.right.iris.item": return 317;
                    case "face.eyes.left.pupil.item": case "face.eyes.right.pupil.item": return 318;
                    case "face.eyes.highlight.item": return 319;
                    case "face.eyelashes.item": return 315;
                    case "face.mole.item": return 323;
                    case "face.makeup.eyeshadow.item": return 316;
                    case "face.makeup.cheek.item": return 320;
                    case "face.makeup.lip.item": return 322;
                    case "face.makeup.paint[0].item": case "face.makeup.paint[1].item": return 313;
                    case "face.makeup.paint[0].layoutItem": case "face.makeup.paint[1].layoutItem": return 7;
                    case "face.beard.item": return 121;
                    case "body.skin.item": return male ? 131 : 231;
                    case "body.detail.item": return male ? 132 : 232;
                    case "body.sunburn.item": return male ? 133 : 233;
                    case "body.paint[0].item": case "body.paint[1].item": return 313;
                    case "body.paint[0].layoutItem": case "body.paint[1].layoutItem": return 8;
                    case "body.nipple.item": return 334;
                    case "body.underhair.item": return 335;
                }
                int part = AppearancePaths.HairPart(path, out string hm);
                if (part >= 0 && hm == "item") return 300 + part;
                if (part >= 0 && hm == "mesh.item") return 306;
                return -1;
            }
            int kind = AppearancePaths.ClothesKindOf(path, out string cm);
            if (kind >= 0)
            {
                if (cm == "item") return (male ? ClothesSet.MaleCategories : ClothesSet.FemaleCategories)[kind];
                if (cm.StartsWith("colors[", StringComparison.Ordinal) && cm.EndsWith("].pattern", StringComparison.Ordinal)) return 348;
                return -1;
            }
            if (AppearancePaths.TryAccessory(path, out string id, out string am) && am == "item")
                return coord?.FindAccessory(id)?.Category ?? -1;
            return -1;
        }

        // ================================================================ planning

        sealed class PlanState
        {
            public SceneModel Model;
            public TreeNode Node;
            public CharaSubtree Stored;
            public int Sex;
            public MakerEditOptions O;
            public MakerEditReport Report;
            public readonly List<AppearanceSet> Out = new List<AppearanceSet>();
            public readonly HashSet<string> CallerPaths = new HashSet<string>(StringComparer.Ordinal);
            // effective values of this call's sets (exact paths)
            public readonly Dictionary<string, object> Overlay = new Dictionary<string, object>(StringComparer.Ordinal);

            public object Current(string path)
            {
                if (Overlay.TryGetValue(path, out var v)) return v;
                return FieldAccessor.TryGet(Model, Node.Id, path, out var x, out _) ? x : null;
            }

            public bool Suppressed(string path)
            {
                foreach (var q in CallerPaths) if (FieldPath.IsPrefixOf(q, path)) return true;
                return false;
            }

            public void Add(string path, object value, bool consequence)
            {
                if (consequence && Suppressed(path)) return;
                Out.Add(new AppearanceSet(path, value, consequence));
                Overlay[path] = value;
            }
        }

        /// <summary>
        /// The sets of one edit of node <paramref name="node"/>, caller sets first-to-last, each followed by its consequences
        ///. Throws <see cref="CommandException"/>: bad_args (path or value), unsupported (rebuild-only path without
        /// AllowRebuild, a card-backed node), not_found (an item row not listed for the character).
        /// </summary>
        public static List<AppearanceSet> Plan(SceneModel model, TreeNode node, IReadOnlyList<(string path, object value)> sets, MakerEditOptions o, MakerEditReport report)
        {
            o = o ?? new MakerEditOptions();
            report = report ?? new MakerEditReport();
            if (node == null || node.Kind != NodeKind.Chara) throw new CommandException(CommandCodes.WrongKind, "Maker edits need a character node");
            var stored = node.Appearance?.Stored ?? throw new CommandException(CommandCodes.Unsupported, "character not imported (node " + node.Id + ")");
            var ps = new PlanState { Model = model, Node = node, Stored = stored, Sex = stored.Chara.Profile.Sex, O = o, Report = report };
            var canon = new List<(string, object)>();
            foreach (var (path, value) in sets ?? Array.Empty<(string, object)>())
            {
                string c = FieldPath.Canonical(path);
                if (c == null) throw Bad("bad field path \"" + path + "\"");
                if (!AppearancePaths.IsAppearance(c)) throw Bad(c + ": Maker edits take appearance paths (appearance.chara.… / appearance.coord.…)");
                canon.Add((c, value));
                ps.CallerPaths.Add(c);
            }
            foreach (var (path, value) in canon)
            {
                CheckRebuild(ps, path, value);
                if (!CheckAccessoryParent(ps, path, value)) continue;
                if (!CheckRow(ps, path, value)) continue;
                var before = ps.Current(path);
                ps.Add(path, value, false);
                if (FieldSchema.Same(before, value)) continue;   // no change, no consequence
                Consequences(ps, path, value, before);
            }
            return ps.Out;
        }

        static void CheckRebuild(PlanState ps, string path, object value)
        {
            if (ps.O.AllowRebuild) return;
            bool rebuild = AppearancePaths.IsRebuildPath(path);
            // a whole body carries body.uncensor
            if (!rebuild && path == AppearancePaths.CharaPrefix + ".body" && value is BodyData b && !FieldSchema.Same(b.Uncensor, ps.Stored.Chara.Body.Uncensor)) rebuild = true;
            if (rebuild)
                throw new CommandException(CommandCodes.Unsupported, path + ": this member rebuilds the whole character (not a Maker edit; the bridge allows it with rebuild: true)");
        }

        // The row check; false when the set is skipped (SkipUnlistedRows).
        static bool CheckRow(PlanState ps, string path, object value)
        {
            if (AppearancePaths.TryAccessory(path, out string aid, out string am) && am == "category" && value is int category && !FieldSchema.Same(ps.Current(path), value))
                CheckCategory(category);
            if (!(value is string reference) || ps.O.Rows == null) return true;
            int cat = CategoryOf(path, ps.Sex, ps.Stored.Coord);
            if (aid != null && am == "item" && ps.Current(AppearancePaths.Accessory(aid, "category")) is int ec) cat = ec;
            if (cat < 0) return true;
            if (FieldSchema.Same(ps.Current(path), value)) return true;   // keeping a stored value is always allowed
            if (ps.O.Rows.HasRow(cat, ps.Sex, reference)) return true;
            string msg = path + ": " + reference + " is not a listed row of category " + cat.ToString(CultureInfo.InvariantCulture) + " for this character (" + (ps.Sex == 0 ? "male" : "female") + ")";
            if (ps.O.SkipUnlistedRows)
            {
                ps.Report.Warn("edit.rowSkipped: node " + ps.Node.Id + ": " + msg);
                return false;
            }
            throw new CommandException(CommandCodes.NotFound, msg);
        }

        static void Consequences(PlanState ps, string path, object value, object before)
        {
            AppearancePaths.TrySplit(path, out var doc, out string m);
            if (doc == AppearanceDocument.Chara)
            {
                if (m == "face.head") { if (value is string head) HeadChange(ps, head); return; }
                int part = AppearancePaths.HairPart(path, out string hm);
                if (part >= 0)
                {
                    if (hm == "item") { if (value is string hi) HairItem(ps, part, hi); return; }
                    HairColours(ps, part, hm, value);
                    return;
                }
                if (m.StartsWith("face.eyes.left.", StringComparison.Ordinal) || m.StartsWith("face.eyes.right.", StringComparison.Ordinal) ||
                    m == "face.eyes.left" || m == "face.eyes.right")
                    LinkedEye(ps, m, value, before);
                return;
            }
            int kind = AppearancePaths.ClothesKindOf(path, out string cm);
            if (kind >= 0)
            {
                if (cm == "item" && value is string ci) ClothesItem(ps, kind, ci);
                return;
            }
            if (AppearancePaths.TryAccessory(path, out string id, out string am))
            {
                if (am == "category" && value is int cat) AccessoryCategory(ps, id, cat);
                else if (am == "item" && value is string ai) AccessoryItem(ps, id, ai, null);
            }
        }

        static bool Bool(PlanState ps, string path, bool fallback) => ps.Current(path) is bool b ? b : fallback;

        // The data keeps α (Q25 quirk 6): derived colours take the target's stored alpha.
        static Color4 KeepAlpha(Color4 c, object current) => new Color4(c.R, c.G, c.B, current is Color4 k ? k.A : 1f);

        /// <summary>«根本と毛先の色を自動設定»: from base (H, S, V): top = HSV(H, S, max(V − 0.15, 0)); under =
        /// HSV(H, max(S − 0.1, 0), min(V + 0.44, 1)); specular = HSV(H, S, min(V + 0.17, 1)).</summary>
        public static void Derive(Color4 baseColor, out Color4 top, out Color4 under, out Color4 specular)
        {
            RgbToHsv(baseColor.R, baseColor.G, baseColor.B, out float h, out float s, out float v);
            top = HsvToRgb(h, s, Math.Max(v - 0.15f, 0f));
            under = HsvToRgb(h, Math.Max(s - 0.1f, 0f), Math.Min(v + 0.44f, 1f));
            specular = HsvToRgb(h, s, Math.Min(v + 0.17f, 1f));
        }

        /// <summary>Unity's Color.RGBToHSV (h, s, v in 0..1).</summary>
        public static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
        {
            if (b > g && b > r) Hsv(4f, b, r, g, out h, out s, out v);
            else if (g > r) Hsv(2f, g, b, r, out h, out s, out v);
            else Hsv(0f, r, g, b, out h, out s, out v);
        }

        static void Hsv(float offset, float dominant, float c1, float c2, out float h, out float s, out float v)
        {
            v = dominant;
            if (v != 0f)
            {
                float small = c1 > c2 ? c2 : c1;
                float diff = v - small;
                if (diff != 0f)
                {
                    s = diff / v;
                    h = offset + (c1 - c2) / diff;
                }
                else
                {
                    s = 0f;
                    h = offset + (c1 - c2);
                }
                h /= 6f;
                if (h < 0f) h += 1f;
            }
            else
            {
                s = 0f;
                h = 0f;
            }
        }

        /// <summary>Unity's Color.HSVToRGB(h, s, v) (hdr: values are not clamped), α = 1.</summary>
        public static Color4 HsvToRgb(float h, float s, float v)
        {
            if (s == 0f) return new Color4(v, v, v, 1f);
            if (v == 0f) return new Color4(0f, 0f, 0f, 1f);
            float r, g, b;
            float h6 = h * 6f;
            int sector = (int)Math.Floor(h6);
            float f = h6 - sector;
            float p = v * (1f - s), q = v * (1f - s * f), t = v * (1f - s * (1f - f));
            switch (sector)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                case 5: r = v; g = p; b = q; break;
                case 6: r = v; g = t; b = p; break;
                case -1: r = v; g = p; b = q; break;
                default: r = g = b = v; break;
            }
            return new Color4(r, g, b, 1f);
        }

        // ---- clothes (C6, C7)

        static void ClothesItem(PlanState ps, int kind, string item)
        {
            string pre = AppearancePaths.Coord("clothes." + ClothesSet.Names[kind]);
            if (!ps.O.KeepColours)
            {
                int cat = (ps.Sex == 0 ? ClothesSet.MaleCategories : ClothesSet.FemaleCategories)[kind];
                if (ps.O.Defaults != null && ps.O.Defaults.TryClothesColors(cat, item, out var cols) && cols != null && cols.Length == ClothesPart.ColorCount)
                    ps.Add(pre + ".colors", CopyArray(cols), true);
                else ps.Report.Warn("edit.defaultsUnavailable: " + pre + ".colors kept (no prefab defaults for " + item + ")");
            }
            ps.Add(pre + ".materialOverrides", new List<MaterialOverride>(), true);
        }

        static T[] CopyArray<T>(T[] a) where T : DocObject
        {
            var r = new T[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = a[i] == null ? null : DocClone.Clone(a[i]);
            return r;
        }

        // ================================================================ execution

        /// <summary>
        /// Plans and executes one edit of <paramref name="node"/> inside <paramref name="tx"/> (open; the caller commits). On
        /// an error the sets of this call already applied stay in the transaction: the caller cancels it (StudioOps disposes
        /// its handle without committing, which reverts them).
        /// </summary>
        public static CommandResult Edit(Transaction tx, SceneModel model, TreeNode node, IReadOnlyList<(string path, object value)> sets, MakerEditOptions o, MakerEditReport report)
        {
            report = report ?? new MakerEditReport();
            List<AppearanceSet> plan;
            try { plan = Plan(model, node, sets, o, report); }
            catch (CommandException e) { return CommandResult.Error(e.Code, e.Message); }
            return Execute(tx, node.Id, plan, report);
        }

        /// <summary>Executes planned sets in <paramref name="tx"/>; the first error is returned.</summary>
        public static CommandResult Execute(Transaction tx, string nodeId, IReadOnlyList<AppearanceSet> plan, MakerEditReport report)
        {
            var last = CommandResult.NoOp(null);
            foreach (var s in plan)
            {
                var r = tx.Set(nodeId, s.Path, s.Value);
                if (r.Status == CommandStatus.Error) return r;
                report?.Record(nodeId, FieldPath.Canonical(s.Path) ?? s.Path, r.Status, s.Consequence);
                if (r.Status == CommandStatus.Ok) last = r;
            }
            return last;
        }

        /// <summary>
        /// The first edit of a card-backed node: imports the card (CharaImport.Importer, else the importer of
        /// the model's session, SessionOptions.Importer) and sets the appearance in <paramref name="tx"/> with the imported
        /// subtree placed by the command itself (SetAppearanceCommand's placeFrom), so undoing or cancelling the edit also
        /// removes the subtree from the scene carrier. Ok when the node already has documents; unsupported when it cannot be
        /// imported.
        /// </summary>
        public static CommandResult EnsureImported(Transaction tx, SceneModel model, TreeNode node, FindingList findings = null)
        {
            if (node == null || node.Kind != NodeKind.Chara) return CommandResult.Error(CommandCodes.WrongKind, "not a character");
            var a = node.Appearance;
            if (a?.Stored != null) return CommandResult.NoOp(null);
            if (a?.Card == null) return CommandResult.Error(CommandCodes.Unsupported, "character not imported (node " + node.Id + " has no appearance)");
            var importer = SceneSession.ImporterOf(model);
            if (importer == null) return CommandResult.Error(CommandCodes.Unsupported, "character not imported (no importer)");
            CharaContainer cc;
            try { cc = importer.Import(a.Card, findings ?? new FindingList()); }
            catch (Exception e) { return CommandResult.Error(CommandCodes.Unsupported, "character not imported: the importer threw " + e.GetType().Name + ": " + e.Message); }
            if (cc?.Subtree == null) return CommandResult.Error(CommandCodes.Unsupported, "character not imported: the importer returned nothing");
            var app = new CharaAppearance { Card = a.Card };
            return tx.Execute(new SetAppearanceCommand(node.Id, app, "Appearance", cc));
        }

        // ================================================================ labels

        /// <summary>The edit area of a call: the area of its first appearance path ("face", "body",
        /// "hair", "clothes", "accessory", "name", "character"). `profile.futanari` counts as "body": its toggle is on the Body
        /// node («特殊» of HS2's body menu, Y9), so the UI and the bridge both record "Edit body".</summary>
        public static string AreaOf(IEnumerable<string> paths)
        {
            foreach (var p in paths ?? Array.Empty<string>())
            {
                string c = FieldPath.Canonical(p) ?? p;
                if (c == FutanariPath) return "body";
                var a = AppearancePaths.Area(c);
                if (a != null) return a;
            }
            return "character";
        }

        /// <summary>The futanari toggle's member (female only, data only in Studio: no unit applies it).</summary>
        public static readonly string FutanariPath = AppearancePaths.Chara("profile.futanari");

        /// <summary>True when the call changes an item (a `ref` member with a list category, or an accessory category):
        /// the history label is then "Change item".</summary>
        public static bool IsItemChange(IEnumerable<string> paths, int sex)
        {
            foreach (var p in paths ?? Array.Empty<string>())
            {
                string c = FieldPath.Canonical(p) ?? p;
                if (AppearancePaths.TryAccessory(c, out _, out string m) && (m == "item" || m == "category")) return true;
                if (CategoryOf(c, sex) >= 0) return true;
            }
            return false;
        }
    }
}
