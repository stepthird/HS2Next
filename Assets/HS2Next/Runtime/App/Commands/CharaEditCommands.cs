// Phase 4.0 package E: the bridge side of the Maker edits (
// deferral of shape.info / shape.set). Group "chara edit" (CoreCommands.Phase40Names, owner core, Since 0.2.0):
//   chara.edit.get        document values under `appearance.` (paths relative to it: "chara.face.shape[3]"), the save-format
//                         JSON forms; default the whole chara.json and coord.json (not recorded)
//   chara.edit.set        {path: json} sets on one or several characters through StudioOps.EditAppearance (one entry, the
//                         rule consequences added by the op; values stored as given, never clamped)
//   chara.edit.item       an item / skin / head / pattern / mesh / accessory item or category change (StudioOps.ChangeItem)
//   chara.edit.list       the Maker list of a category for a character or a sex (MakerLists over the catalog)
//   chara.edit.preset     the face preset push of a head row (StudioOps.ApplyFacePreset)
//   chara.edit.accessory  accessory add / remove / move / copy (StudioOps.Add/Remove/Move/CopyAccessory)
//   chara.edit.info       the editor of a built character: dirty units, last flush, creators, owned materials, decoded
//                         layers, part rows; `session` opens / closes a bridge edit session (view state)
//   shape.info, shape.set HS2's names, arguments and reply members; values from / into the documents
//                         (shape.set = one entry "Edit body" / "Edit face"; the flush runs before the reply, so settled)
// Phase 4.1 package E (CharaEditCommands.Phase41Names, same group):
//   chara.edit.layout     a mole / face-paint layout push (StudioOps.ApplyLayoutPreset: (Scale, Scale, PosX, PosY) of a row of 6 / 7)
//   chara.edit.reset      the per-row reset to the reference values (StudioOps.ResetAppearance; schema defaults when a node
//                         has no reference); `reference` per node
//   chara.edit.materials  the drawn materials of named renderers (read-only; the reply shape of the HS2 side's chara.materials)
//   chara.edit.rtstats    the face / body creators' RT statistics from a GPU read-back of mip 0 (read-only; HS2's chara.rtStats
//                         shape: names "colour" / "detail", cells from the bottom row up, SHA-256 of the RGBA32 bytes, props)
//   chara.edit.digest     SHA-256 per section of the canonical realization text (FaceDigest; read-only)
//   and the extensions: chara.edit.list columns / current / preset, chara.edit.preset's edit.presetMissing fallback (the op),
//   chara.edit.info deferred / creators lastRebuildMs, size / reference / lastFlush.mode.
// Phase 4.2 package E (CharaEditCommands.Phase42Names, same group):
//   chara.edit.swatch     a skin colour swatch push (StudioOps.ApplyColourPreset: HSVToRGB(Base) of a row of 336 by id, alpha 1)
//   chara.edit.bust       the bust of a built character (read-only; BustReport): stored values, the breast chains' pattern-0
//                         values (the HS2 side's chara.bustPhysics) and the breast normals (chara.meshNormals: rate, entries,
//                         SHA-256 of the drawn body mesh's normals as little-endian float32 x, y, z per vertex)
//   and the extensions: chara.edit.materials part (face / body renderer sets), chara.edit.digest sections normals / bust
//   (parts body / all), chara.edit.info throttle / lastFlush.throttleWeight / bustNormals.
// Phase 4.3 package E (J16; CharaEditCommands.Phase43Names, same group):
//   chara.edit.hairBundle a hair bundle edit (StudioOps.EditHairBundle: set / reset one bundle, the first edit materializes
//                         the part's list, «髪の調整をまとめて行う» copies changed components) or «全て初期化» (ResetHairBundles)
//   chara.edit.hairSkin   «肌の色に合わせる» (StudioOps.MatchHairToSkin: the part's under colour and smoothness from the skin)
//   chara.edit.hair       the hair of a built character (read-only; HairReport): per part the row, the CmpHair facts, the
//                         stored / effective bundles with their correction transforms and chains, the mesh state, the hair
//                         and decoration materials — the reply shape of the HS2 side's chara.hairInfo
//   and the extensions: chara.edit.swatch hair colour presets (305 on chara.hair.<part>.baseColor), chara.edit.materials
//   part hair (+ hairPart; texture scale / offset as "<name>_ST"), chara.edit.digest parts hair (section chains),
//   chara.edit.info hair {parts facts, lastFlush.dynBone, deferred} and lastFlush.dynBone.
// Phase 4.4 package E (the clothes pages; CharaEditCommands.Phase44Names, same group):
//   chara.edit.clothes    the clothes of a built character (read-only; ClothesReport): per kind the row and its columns, the
//                         realized prefab's facts, its default colour sets, the compositing passes, the materials as written,
//                         the def / half / option objects and the copy the catalog served the prefab from; the masks' inputs
//                         and values — the member names and order of the HS2 side's chara.clothesInfo, then HS2Next's
//   chara.edit.clothesDefaults  «初期カラー» (StudioOps.ResetClothesColours: a kind's three colour sets ← the prefab defaults)
//   chara.edit.coordinate a coordinate load (StudioOps.ApplyCoordinateFile: an HS2 coordinate card or a .hs2ncoord file in the
//                         modes clothes / accessories / all, as per-part changes; the character's sex only)
//   and the extensions: chara.edit.materials / .rtstats part clothes (+ kind), chara.edit.digest parts clothes (section
//   objects), chara.edit.info clothes {kinds facts, lastFlush {clothesSwapped, objectsReset, creatorsRebuilt}}; a clothes kind
//   argument is a name (top … shoes) or 0..7 (KindArg). chara.edit.info {session: true} opens the session only for a built
//   character (unavailable otherwise, nothing opened).
// Phase 4.5 package E (the accessory pages; CharaEditCommands.Phase45Names, same group). Slots are the 0-based build slots
// of the slot table (the page shows slot + 1; SlotArg):
//   chara.edit.accessories      the accessories of a built character (read-only; AccessoryReport and the slot objects): per
//                               build slot the stored part, the row and its columns, the realized prefab's CmpAccessory facts
//                               and raw defaults, the anchor, the slot object's pose, the two transform sets, every material
//                               slot as written and the dynamic-bone chains — the member names and order of the HS2 side's
//                               chara.accessoryInfo, then HS2Next's (the accessory id, the stored parent, the preferred slot,
//                               the plugin rules, the served copy, …); the hair parts' activity at the top level
//   chara.edit.accessoryDefaults «初期カラー» (StudioOps.ResetAccessoryColours: the used areas and the alpha colour ← the
//                               prefab's defaults)
//   chara.edit.accessoryHairColour «髪の毛の色を取得» (StudioOps.MatchAccessoryHairColour: colours 1–4, smoothness and metallic
//                               of a hair-type accessory ← a hair part's)
//   and the extensions: chara.edit.accessory add / copy to a build slot (slot / toSlot) and moveSlot, replies with the build
//   slot; chara.edit.materials part accessories (+ slot); chara.edit.digest parts accessories (sections materials, objects);
//   chara.edit.info accessories {slots facts, lastFlush {accessorySlotsSwapped, accessoryHairShaded}}; chara.edit.reset of the
//   accessory rows' constant defaults (transform components, the hair sliders) without a reference.
// Phase 4.6 package E (the File node; CharaEditCommands.Phase46Names, same group):
//   chara.edit.load       a character load with HS2's five load flags (StudioOps.LoadCharacter: «顔» face, «体» body, «髪»
//                         hair, «コーデ» coordinate, «キャラ設定» profile from an HS2 card or a .hs2nchara file; all five = the
//                         whole appearance as Change character, fewer = the chosen sections; a partial load from the other
//                         sex is bad_args with reason load.otherSex); the reply names the source, the reported member paths
//                         and the target's import provenance after the load
//   and the extension: chara.edit.get's pseudo path "import" (the stored documents' import provenance, null for a
//   character made in HS2Next).
// Replies give paths relative to `appearance.` (`chara.…`, `coord.…`); scene paths a Maker op also writes (the accessory
// visibility map `chara.state.accessories[…]`) keep their scene form. The ops' warnings (`edit.defaultsUnavailable`,
// `edit.presetUnresolved`, …) are the envelope's `warnings` (a result member of that name would clash with it, bridge
// 1). Every Maker write goes through StudioOps.Maker (validation, consequences, card import, one BeginOrJoin), so a
// batch absorbs it and undo / redo replay it.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HS2Next.Api.Bridge;
using HS2Next.Chara.Edit;
using HS2Next.Content;
using HS2Next.Content.Maker;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using CmpAccessory = AIChara.CmpAccessory;
using CmpClothes = AIChara.CmpClothes;
using CmpHair = AIChara.CmpHair;
using ClothesPartUnit = HS2Next.Chara.Edit.Units.ClothesPartUnit;
using HairUnit = HS2Next.Chara.Edit.Units.HairUnit;

namespace HS2Next.App.Commands
{
    public static class CharaEditCommands
    {
        /// <summary>The commands of this file (CoreCommands.Phase40Names), in the order.</summary>
        public static readonly IReadOnlyList<string> Names = new[]
        {
            "chara.edit.get", "chara.edit.set", "chara.edit.item", "chara.edit.list", "chara.edit.preset", "chara.edit.accessory",
            "chara.edit.info", "shape.info", "shape.set",
        };

        /// <summary>The commands Phase 4.1 adds to this file (CoreCommands.Phase41Names), in its order.</summary>
        public static readonly IReadOnlyList<string> Phase41Names = new[]
        {
            "chara.edit.layout", "chara.edit.reset", "chara.edit.materials", "chara.edit.rtstats", "chara.edit.digest",
        };

        /// <summary>The commands Phase 4.2 adds to this file (CoreCommands.Phase42Names), in its order.</summary>
        public static readonly IReadOnlyList<string> Phase42Names = new[] { "chara.edit.swatch", "chara.edit.bust" };

        /// <summary>The commands Phase 4.3 adds to this file (CoreCommands.Phase43Names), in its order.</summary>
        public static readonly IReadOnlyList<string> Phase43Names = new[] { "chara.edit.hairBundle", "chara.edit.hairSkin", "chara.edit.hair" };

        /// <summary>The commands Phase 4.4 adds to this file (CoreCommands.Phase44Names, with ContentCommands.Names), in the order of
        /// the clothes spec's bridge table.</summary>
        public static readonly IReadOnlyList<string> Phase44Names = new[] { "chara.edit.clothes", "chara.edit.clothesDefaults", "chara.edit.coordinate" };

        /// <summary>The commands Phase 4.5 adds to this file (CoreCommands.Phase45Names), in its order.</summary>
        public static readonly IReadOnlyList<string> Phase45Names = new[] { "chara.edit.accessories", "chara.edit.accessoryDefaults", "chara.edit.accessoryHairColour" };

        /// <summary>The commands Phase 4.6 adds to this file (part of CoreCommands.Phase46Names; the File node's character load).</summary>
        public static readonly IReadOnlyList<string> Phase46Names = new[] { "chara.edit.load" };

        /// <summary>chara.edit.get's pseudo path of the stored documents' import provenance (not a document member).</summary>
        public const string ImportPath = "import";

        /// <summary>chara.edit.accessories: the list columns of a slot's row (the HS2 side's set, in its order; null when the row
        /// lacks one).</summary>
        public static readonly string[] AccessoryRowColumns = { "Name", "MainManifest", "MainAB", "MainData", "Parent", "TexManifest", "TexAB", "TexD", "TexC" };

        /// <summary>The head attach points (the first 14 of the 54 keys, under the head skeleton): a slot parented to one is a part of
        /// the head (HS2's partsOfHead).</summary>
        public const int HeadAttachPoints = 14;

        const string SlotExpected = "a build slot (0-based integer ≥ 0; the page shows slot + 1)";

        /// <summary>The operations of chara.edit.accessory.</summary>
        public static readonly string[] AccessoryOps = { "add", "remove", "move", "moveSlot", "copy" };

        /// <summary>The renderer sets of chara.edit.materials' part and the parts of chara.edit.digest.</summary>
        public static readonly string[] MaterialParts = { "face", "body", "hair", "clothes", "accessories" },
            DigestParts = { "face", "body", "hair", "clothes", "accessories", "all" };

        /// <summary>chara.edit.clothes: the properties of a clothes material slot the reply lists (the HS2 side's chara.clothesInfo
        /// set; those the slot's shader declares). Textures name the creator whose RT they are ("colour j" / "detail j").</summary>
        public static readonly string[] ClothesMaterialPropNames =
        {
            "_AlphaEx", "_Cutoff", "_alpha_a", "_alpha_b", "_alpha_c", "_alpha_d", "_Color", "_Glossiness", "_Metallic", "_MainTex", "_DetailMainTex",
        };

        /// <summary>chara.edit.clothes: the list columns of a kind's row, the HS2 side's set first (null when the row lacks one),
        /// then the other mask columns.</summary>
        public static readonly string[] ClothesRowColumns =
        {
            "Name", "MainAB", "MainData", "MainData02", "Coordinate", "NotBra", "StateType", "BreakDisableMask", "KokanHide",
            "MainTex", "MainTex02", "MainTex03", "ColorMaskTex", "ColorMask02Tex", "ColorMask03Tex",
            "OverBodyMask", "OverBraMask", "OverBodyBMask", "OverInnerTBMask", "OverInnerBMask", "OverPanstMask",
            "OverBodyMaskAB", "OverBraMaskAB", "OverBodyBMaskAB", "OverInnerTBMaskAB", "OverInnerBMaskAB", "OverPanstMaskAB",
        };

        const string KindExpected = "a clothes kind: \"top\", \"bottom\", \"innerTop\", \"innerBottom\", \"gloves\", \"pantyhose\", \"socks\", \"shoes\" or 0..7";

        /// <summary>The coordinate load modes of chara.edit.coordinate (HS2's three buttons).</summary>
        public static readonly string[] CoordinateModes = { "clothes", "accessories", "all" };

        /// <summary>chara.edit.materials part "body": the body renderers (female / male body object).</summary>
        public static readonly string[] BodyRendererNames = { "o_body_cf", "o_body_cm" };

        /// <summary>chara.edit.hair: the properties of a hair material slot the reply lists (the HS2 side's chara.hairInfo set, in
        /// its order; those the slot's shader declares); a texture is followed by its scale / offset "&lt;name&gt;_ST".</summary>
        public static readonly string[] HairPropNames =
        {
            "_Color", "_TopColor", "_UnderColor", "_Specular", "_MeshColor_01", "_Metallic", "_Smoothness", "_Ringoff", "_Mesh_onoff",
            "_Ringcontrolr_onoff", "_Cutoff", "_ExGloss", "_MainTex", "_MeshColorMask", "_MeshColorMask_B", "_BumpMap",
        };

        const string HairPartExpected = "a hair part: \"back\", \"front\", \"side\", \"option\" or 0..3";

        /// <summary>chara.edit.rtstats: the largest grid (cells per side).</summary>
        public const int MaxRtGrid = 64;

        /// <summary>chara.edit.list: rows per reply by default and at most.</summary>
        public const int DefaultListLimit = 200, MaxListLimit = 10000;

        const string RelDoc = "a path relative to `appearance.` (chara.face.shape[3], coord.clothes.top.item, coord.accessories[\"<id>\"].parent)";

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.get", "Values of a character's documents under `appearance.` in the save-format JSON forms (default: the whole chara.json and coord.json).", "chara edit")
                .Arg("key", ArgTypes.Node, "a character (imported)", true)
                .Arg("paths", ArgTypes.ArrayOf(ArgTypes.String), "paths relative to `appearance.` (\"chara.face.head\", \"coord.clothes.top.colors[0]\"; \"chara\" / \"coord\" = a whole document; \"import\" = the stored documents' import provenance, null for a character made in HS2Next); default both documents")
                .Return("values", ArgTypes.Object, "{path: value} (JSON values; key = the path as given)")
                .Example("{\"cmd\":\"chara.edit.get\",\"key\":\"<id>\",\"paths\":[\"chara.face.shape[3]\",\"chara.face.head\"]}")
                .WithFlags(CommandFlags.ReadOnly), Get);
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.set", "Sets document members of characters (Maker edit: values stored as given, never clamped; the consequences of HS2's edit rules are added; one undo entry).", "chara edit")
                .Arg("key", ArgTypes.Node, "a character").Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "several characters (one entry)")
                .Arg("set", ArgTypes.Object, "{path: JSON value}: " + RelDoc, true)
                .Arg("rebuild", ArgTypes.Bool, "allow the members whose change rebuilds the character (chara.body.uncensor, chara.studioOverlays)", false, false)
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "paths that changed (relative to `appearance.`; consequences included)")
                .Return("consequences", ArgTypes.ArrayOf(ArgTypes.String), "the rule consequences among changed / noop")
                .Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "paths that already had the value")
                .Example("{\"cmd\":\"chara.edit.set\",\"key\":\"<id>\",\"set\":{\"chara.face.shape[3]\":1.5,\"chara.body.skin.color\":[1,0.9,0.8,1]}}")
                .WithFlags(CommandFlags.Mutates), Set);
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.item", "Changes an item of characters (a `ref` member: head, skin, hair / clothes / accessory item, pattern, mesh …; or an accessory's category, 350 = remove) with HS2's rule consequences (one undo entry \"Change item\").", "chara edit")
                .Arg("key", ArgTypes.Node, "a character").Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "several characters (one entry)")
                .Arg("path", ArgTypes.String, "the ref member, relative to `appearance.` (chara.face.head, chara.hair.front.item, coord.clothes.top.item, coord.accessories[\"<id>\"].item or .category)", true)
                .Arg("ref", ArgTypes.Any, "the row \"<guid>#<id>\" (a category number for an accessory's category)", true)
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "").Return("consequences", ArgTypes.ArrayOf(ArgTypes.String), "").Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "")
                .Example("{\"cmd\":\"chara.edit.item\",\"key\":\"<id>\",\"path\":\"chara.face.head\",\"ref\":\"illusion.hs2#1\"}")
                .WithFlags(CommandFlags.Mutates | CommandFlags.Slow).WithTimeout(120), Item);
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.list", "The rows a Maker list offers: by a character's ref member (its sex; face skins filtered by its head) or by category and sex.", "chara edit")
                .Arg("key", ArgTypes.Node, "a character: its sex (and head for face skins)")
                .Arg("path", ArgTypes.String, "a ref member relative to `appearance.` (with key): decides the category")
                .Arg("category", ArgTypes.Int, "a list category (ChaListDefine numbers: 210, 211, 240, 351, …)")
                .Arg("sex", ArgTypes.Any, "0 / \"male\" or 1 / \"female\" (without key)", false, "female")
                .Arg("head", ArgTypes.String, "face skins: only the rows of this head ref (default: the character's head with key, else every row)")
                .Arg("search", ArgTypes.String, "text the row's names or ref must contain (normalised as the UI's search)")
                .Arg("language", ArgTypes.Enum("ja", "en", "zh-Hans"), "the names' language (Name, EN_US, ZH_CN)", false, "ja")
                .Arg("offset", ArgTypes.Int, "first row", false, 0)
                .Arg("limit", ArgTypes.Int, "rows at most (1.." + MaxListLimit + ")", false, DefaultListLimit)
                .Arg("columns", ArgTypes.ArrayOf(ArgTypes.String), "list columns to include per row as values {column: text} (e.g. [\"Scale\",\"PosX\",\"PosY\"] of the layout lists 6 / 7; a missing column gives null)")
                .Arg("current", ArgTypes.Bool, "with key + path: mark the row the member holds (rows[].current; currentRef / currentIndex)", false, false)
                .Return("total", ArgTypes.Int, "rows of the list (after search)").Return("category", ArgTypes.Int, "").Return("sex", ArgTypes.Int, "")
                .Return("rows", ArgTypes.ArrayOf(ArgTypes.Object), "{ref, name, possess, gated, headRef? (face skins), preset? (head lists: the row names a face preset), values? (columns), current? (current)} in HS2's Maker order; face skins without a head filter: one row per definition, as HS2's list control")
                .Return("currentRef", ArgTypes.String, "current: the member's value", false)
                .Return("currentIndex", ArgTypes.Int, "current: the index of its row among the rows after search (-1: not listed)", false)
                .Example("{\"cmd\":\"chara.edit.list\",\"key\":\"<id>\",\"path\":\"chara.face.skin\"}")
                .WithFlags(CommandFlags.ReadOnly), List);
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.preset", "Pushes the face preset of a head row onto characters (the whole face block, head included; one undo entry \"Face preset\"). A head whose preset asset is missing gets the head change only, as HS2's push (warning edit.presetMissing); a preset naming an unlisted head ends on the first listed head (edit.presetUnresolved).", "chara edit")
                .Arg("key", ArgTypes.Node, "a character").Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "several characters (one entry)")
                .Arg("head", ArgTypes.String, "the head ref \"<guid>#<id>\" (default: each character's current head)")
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "").Return("consequences", ArgTypes.ArrayOf(ArgTypes.String), "").Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "")
                .WithFlags(CommandFlags.Mutates | CommandFlags.Slow).WithTimeout(120), Preset);
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.accessory", "Adds, removes, reorders, moves to a build slot or copies accessories of a character (one undo entry). Slots are the 0-based build slots of the slot table (the Accessories page shows slot + 1): add / copy may name the target slot, which becomes the new entry's preferred slot (importSlot); moveSlot changes it; a slot ≥ 20 while MoreAccessories is off is kept as data (warning acc.slotNotLoaded).", "chara edit")
                .Arg("key", ArgTypes.Node, "a character", true)
                .Arg("op", ArgTypes.Enum(AccessoryOps), "the operation (move = reorder the list; moveSlot = move to a free build slot)", true)
                .Arg("category", ArgTypes.Int, "add: the accessory category (351–363)")
                .Arg("ref", ArgTypes.String, "add: the item row \"<guid>#<id>\"")
                .Arg("parent", ArgTypes.String, "add: the anchor key (\"\" or one of the 54 attach points; default: the row's Parent column)")
                .Arg("slot", ArgTypes.Int, "add: the free build slot to add to (default: the lowest free slot; an occupied slot is bad_args); moveSlot: the free build slot to move to")
                .Arg("index", ArgTypes.Int, "add (deprecated; not with slot): list index without a preferred slot; move: the new list index")
                .Arg("accessory", ArgTypes.String, "remove / move / moveSlot: the accessory id")
                .Arg("from", ArgTypes.String, "copy: the source accessory id")
                .Arg("to", ArgTypes.String, "copy: the target accessory id (overwrite its members; not with toSlot)")
                .Arg("toSlot", ArgTypes.Int, "copy: the target build slot (free: a new accessory there; occupied: that accessory's members overwritten, its id kept); neither to nor toSlot = «複製», the lowest free slot")
                .Arg("parts", ArgTypes.ArrayOf(ArgTypes.String), "copy: \"slot\", \"move1\", \"move2\" (default [\"slot\"]; the sets need an occupied target)")
                .Arg("mirrorParent", ArgTypes.Bool, "copy: mirror the parent («左右の親を変更»: HS2's 18 left / right attach point pairs, other values kept)", false, false)
                .Arg("mirror", ArgTypes.Enum("none", "leftRight", "upDown"), "copy: mirror the copied move rotation (y or x + 180°, wrapped into [0, 360))", false, "none")
                .Return("accessory", ArgTypes.String, "add / copy to a new slot: the new accessory id", false)
                .Return("slot", ArgTypes.Int, "add / copy / move / moveSlot: the build slot of the new, copied-to or moved accessory (-1: none)", false)
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "").Return("consequences", ArgTypes.ArrayOf(ArgTypes.String), "").Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "")
                .Example("{\"cmd\":\"chara.edit.accessory\",\"key\":\"<id>\",\"op\":\"add\",\"category\":353,\"ref\":\"illusion.hs2#0\",\"slot\":4}")
                .WithFlags(CommandFlags.Mutates | CommandFlags.Slow).WithTimeout(120), Accessory);
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.info", "The editor of a built character: dirty and deferred units, last flush, creators (sizes, bytes, rebuilds), owned materials, decoded layers, part rows, whether a reset reference exists; session opens / closes a bridge edit session (the first one also captures the reset reference).", "chara edit")
                .Arg("key", ArgTypes.Node, "a built character", true)
                .Arg("session", ArgTypes.Bool, "true opens a bridge edit session (kept decoded layers; ends 60 s after the last edit; captures the reference of chara.edit.reset; only for a built character: unavailable, and nothing opened, otherwise), false closes it")
                .Return("dirty", ArgTypes.ArrayOf(ArgTypes.String), "dirty unit ids").Return("rebuildRequested", ArgTypes.Bool, "")
                .Return("deferred", ArgTypes.ArrayOf(ArgTypes.String), "unit ids a preview flush deferred to the commit flush (anim.refresh, the light dynamic-bone pass, bust)")
                .Return("reference", ArgTypes.Bool, "the character has reset reference documents (its edit session opened in this scene session)")
                .Return("inSession", ArgTypes.Bool, "").Return("flushes", ArgTypes.Int, "flushes of this editor")
                .Return("lastFlush", ArgTypes.Object, "{units, creators, creatorsRebuilt, ms, errors, build, mode (normal / preview / build), deferred, faceDrawChanged, throttleWeight (the body rebuild's cost weight when the flush started: Σ over the body creators it rebuilt of 1 + their skin layers; 0 for the build), dynBone (the dynamic-bone chain step it ran: none / light / full)} or null")
                .Return("hair", ArgTypes.Object, "the hair: {parts [{part, name, item, prefab, facts (as chara.edit.hair)}] (back, front, side, option), lastFlush {dynBone (none / light / full), deferred (the flush deferred the light chain re-derivation to the commit)} or null, deferred (a light re-derivation is pending now)}")
                .Return("clothes", ArgTypes.Object, "the clothes: {kinds [{kind (top … shoes), index (0..7), item, prefab, realized, skipped, facts (as chara.edit.clothes)}], lastFlush {clothesSwapped [8], objectsReset [8] (kinds whose part unit ran without a swap, e.g. an option toggle, and so re-applied the stored clothes state), creatorsRebuilt [{kind, group (0..2), colour, detail}] (the clothes compositing passes the flush rebuilt)} or null}")
                .Return("throttle", ArgTypes.Object, "the body throttle of drags: {mode (auto / always / off = maker.bodyThrottle), enabled (the master switch), weight (the pending body rebuild's weight now), judged (the weight the throttle last judged on a drag frame), throttled (drag frames this character waited), total (all characters)}")
                .Return("bustNormals", ArgTypes.Object, "the breast normals: {rate (HS2's rate of the stored bust size; null for males), blendRate (the rate last written; null: none), applied, meshCopied}")
                .Return("creators", ArgTypes.ArrayOf(ArgTypes.Object), "{name (create_skin_face, create_skin detail_face, create_skin_body, create_skin detail_body), width, height, size [w, h], mips, bytes, rebuilds, lastRebuildMs, layers}")
                .Return("creatorBytes", ArgTypes.Int, "").Return("ownedMaterials", ArgTypes.Int, "").Return("decodedLayers", ArgTypes.Int, "")
                .Return("parts", ArgTypes.Object, "{head, skin, hair[4], clothes[8], accessories[{accessory, slot (the build slot), ref, loaded}]}")
                .Return("accessories", ArgTypes.Object, "the accessories: {slots [{slot (0-based build slot), accessory (the id; null for a card build), category, item, loaded, notLoaded (≥ 20 with MoreAccessories off), facts (as chara.edit.accessories)}] (build-slot order), lastFlush {accessorySlotsSwapped [slots whose object the flush replaced, loaded or removed], accessoryHairShaded [hair-type slots whose materials got the hair shader pass]} or null}")
                .WithFlags(CommandFlags.ReadOnly), Info);
            CommandKit.Register(r, app, CommandKit.Spec("shape.info", "HS2's shape.info: the body (33) and face (59) slider values of a character from its documents, its head and versions.", "chara edit")
                .Arg("key", ArgTypes.Node, "a character (imported)", true)
                .Return("bodyValues", ArgTypes.ArrayOf(ArgTypes.Float), "").Return("faceValues", ArgTypes.ArrayOf(ArgTypes.Float), "")
                .Return("headId", ArgTypes.Int, "the head's local id").Return("headRef", ArgTypes.String, "the head \"<guid>#<id>\" (HS2Next)")
                .WithFlags(CommandFlags.ReadOnly), ShapeInfo);
            CommandKit.Register(r, app, CommandKit.Spec("shape.set", "HS2's shape.set: sets body / face slider values (whole arrays from index 0, or one index), stored as given; the flush runs before the reply (one undo entry).", "chara edit")
                .Arg("key", ArgTypes.Node, "a character", true)
                .Arg("body", ArgTypes.ArrayOf(ArgTypes.Float), "body values from index 0 (at most 33)")
                .Arg("face", ArgTypes.ArrayOf(ArgTypes.Float), "face values from index 0 (at most 59)")
                .Arg("bodyIndex", ArgTypes.Int, "one body index (0..32)").Arg("bodyValue", ArgTypes.Float, "its value", false, 0.5)
                .Arg("faceIndex", ArgTypes.Int, "one face index (0..58)").Arg("faceValue", ArgTypes.Float, "its value", false, 0.5)
                .Arg("minFrames", ArgTypes.Int, "HS2's settle wait (accepted; HS2Next applies before replying)", false, 3)
                .Arg("maxFrames", ArgTypes.Int, "HS2's settle wait (accepted; HS2Next applies before replying)", false, 120)
                .Return("bodySet", ArgTypes.Int, "").Return("faceSet", ArgTypes.Int, "").Return("framesWaited", ArgTypes.Int, "0")
                .Return("settled", ArgTypes.Bool, "the edit is realised (built, nothing dirty)").Return("changedTransforms", ArgTypes.Int, "transforms under the body rig whose local TRS changed")
                .Return("bodyValues", ArgTypes.ArrayOf(ArgTypes.Float), "").Return("faceValues", ArgTypes.ArrayOf(ArgTypes.Float), "")
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "paths that changed (HS2Next)")
                .Example("{\"cmd\":\"shape.set\",\"key\":\"<id>\",\"faceIndex\":3,\"faceValue\":1.5}")
                .WithFlags(CommandFlags.Mutates), ShapeSet);
            RegisterPhase41(r, app);
            RegisterPhase42(r, app);
            RegisterPhase43(r, app);
            RegisterPhase44(r, app);
            RegisterPhase45(r, app);
            RegisterPhase46(r, app);
        }

        /// <summary>The commands of <see cref="Phase46Names"/> (the File node's character load).</summary>
        static void RegisterPhase46(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.load", "Loads parts of a character file onto characters with HS2's five load flags «顔» face, «体» body, «髪» hair, «コーデ» coordinate, «キャラ設定» profile (the Edit tab's File node). All five replace the whole appearance as Change character does (both documents, the plugin data of both scopes, the resources and the import provenance; the node keeps its id, transform, children, animation, FK / IK, face, look, state and name). Fewer replace the chosen sections of the stored documents (face: the face block, the face-group skin overlays, the bone modifiers of head bones, the MaterialEditor items of the head; body: the rest of the body block and the other character items; hair: the hair block; coordinate: the worn coordinate with its plugin data as the coordinate load «全て読込み»; profile: name, futanari and the imported game parameters) and keep the character's own chara-scope plugin data and import provenance. A partial load needs a character of the same sex (bad_args `path`, reason load.otherSex); a card-backed target is imported first in the same entry. An HS2 card is imported in memory. One undo entry \"Load character\".", "chara edit")
                .Arg("key", ArgTypes.Node, "a character").Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "several characters (one entry)")
                .Arg("path", ArgTypes.Path, "an HS2 card (.png) or a .hs2nchara file: absolute; a relative .hs2nchara under <UserData>/chara; any other relative path under <hs2Root>/UserData/chara/female or /male (sex)", true)
                .Arg("flags", ArgTypes.Any, "\"all\" or a list of the load flags \"face\", \"body\", \"hair\", \"coordinate\", \"profile\" (at least one; a list of all five = \"all\")", false, "all")
                .Arg("sex", ArgTypes.Any, "0 / \"male\" or 1 / \"female\": the HS2 card folder of a relative card path (default: the first key's sex)")
                .Return("flags", ArgTypes.ArrayOf(ArgTypes.String), "the load flags applied, in the order face, body, hair, coordinate, profile")
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "the member paths the load reported (relative to `appearance.`; \"appearance\" for all five)")
                .Return("consequences", ArgTypes.ArrayOf(ArgTypes.String), "").Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "")
                .Return("imported", ArgTypes.Bool, "the file was an HS2 card (imported in memory)")
                .Return("importedTargets", ArgTypes.ArrayOf(ArgTypes.String), "the card-backed targets that were imported first (node ids)")
                .Return("source", ArgTypes.Object, "{file (absolute), sex (0 / 1), name}: the character read")
                .Return("import", ArgTypes.Object, "the import provenance of the (first) key's stored documents after the load ({from, fileName, sha256, fileSize, importedUtc, importer, hs2Version, coordSex, sideloaderGuids}; null for a character made in HS2Next; all five take the file's, a partial load keeps the character's own)")
                .Return("findings", ArgTypes.ArrayOf(ArgTypes.String), "the reader's / importer's findings of warning and error severity")
                .Example("{\"cmd\":\"chara.edit.load\",\"key\":\"<id>\",\"path\":\"BR-Chan HS2 Rev2.png\",\"flags\":[\"face\",\"hair\"]}")
                .WithFlags(CommandFlags.Mutates | CommandFlags.Slow).WithTimeout(300), Load);
        }

        /// <summary>The commands of <see cref="Phase41Names"/>.</summary>
        static void RegisterPhase41(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.layout", "A layout push of the Maker (mole / face paint «配置»): the vector (Scale, Scale, PosX, PosY) of a row of list 6 (mole) or 7 (face paint), looked up by id, into the layout member; rotation and layoutItem untouched (one undo entry \"Edit face\").", "chara edit")
                .Arg("key", ArgTypes.Node, "a character").Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "several characters (one entry)")
                .Arg("path", ArgTypes.String, "chara.face.mole.layout (list 6) or chara.face.makeup.paint[0|1].layout (list 7), relative to `appearance.`", true)
                .Arg("ref", ArgTypes.ContentId, "the layout row \"<guid>#<id>\" (vanilla: illusion.hs2#0..7 of 6, #0..5 of 7)", true)
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "").Return("consequences", ArgTypes.ArrayOf(ArgTypes.String), "").Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "")
                .Example("{\"cmd\":\"chara.edit.layout\",\"key\":\"<id>\",\"path\":\"chara.face.mole.layout\",\"ref\":\"illusion.hs2#4\"}")
                .WithFlags(CommandFlags.Mutates), Layout);
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.reset", "Resets members to each character's reference value (its documents when its edit session first opened in this scene session, HS2's edit-mode reset), else to the default (the Edit tab row's default or `defaults`); the clothes members gloss, metallic, layout (or one component) and rotation reset to the worn prefab's default (the Maker's ↺; without the prefab's defaults the rule above with warning edit.defaultsUnavailable), and so do an accessory's gloss / metallic of colours 1–4; an accessory member's default is its Accessories row's (transform components 0 / 0 / 1 as \"…moves[c].position#i\", the hair sliders 0.5, noShake false, parent \"\"); values unclamped, one undo entry \"Reset value\".", "chara edit")
                .Arg("key", ArgTypes.Node, "a character").Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "several characters (one entry)")
                .Arg("paths", ArgTypes.ArrayOf(ArgTypes.String), "members relative to `appearance.` (chara.face.eyebrow.tilt); one component of a vector or colour as \"path#c\" (chara.face.eyebrow.layout#2)", true)
                .Arg("defaults", ArgTypes.ArrayOf(ArgTypes.Any), "one default per path (number, [numbers], bool, string or null) for characters without a reference; default: the Edit tab row's default of the path, none for paths without a row")
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "").Return("consequences", ArgTypes.ArrayOf(ArgTypes.String), "").Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "")
                .Return("reference", ArgTypes.Object, "{<node id>: bool} the character had a reference (else the defaults applied); a path with neither is skipped (warning edit.resetUnavailable)")
                .Example("{\"cmd\":\"chara.edit.reset\",\"key\":\"<id>\",\"paths\":[\"chara.face.eyebrow.tilt\",\"chara.face.eyebrow.layout#2\"]}")
                .WithFlags(CommandFlags.Mutates), Reset);
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.materials", "The drawn materials of a character's renderers by name (read-only): per slot the material, shader, queue, sorted keywords and every property the shader declares (the reply shape of the HS2 Studio Bridge's chara.materials).", "chara edit")
                .Arg("key", ArgTypes.Node, "a built character", true)
                .Arg("renderers", ArgTypes.ArrayOf(ArgTypes.String), "renderer object names (default: the set of part)")
                .Arg("part", ArgTypes.Enum(MaterialParts), "the default renderer set: face = o_head, o_eyebase_L, o_eyebase_R, o_eyeshadow, o_eyelashes; body = o_body_cf / o_body_cm; hair = every renderer under the four hair part objects (part order back, front, side, option; rendHair, rendAccessory and others), each with hairPart; clothes = every renderer under the eight clothes kind objects (kind order top … shoes, hierarchy order), each with kind; accessories = every renderer under the accessory slot objects (build-slot order, hierarchy order), each with slot; with hair, clothes and accessories texture properties are followed by \"<name>_ST\" [scale x, y, offset x, y] (renderers overrides the set)", false, "face")
                .Arg("hairPart", ArgTypes.Any, "part hair only: one hair part (\"back\", \"front\", \"side\", \"option\" or 0..3)")
                .Arg("kind", ArgTypes.Any, "part clothes only: one clothes kind (\"top\", \"bottom\", \"innerTop\", \"innerBottom\", \"gloves\", \"pantyhose\", \"socks\", \"shoes\" or 0..7)")
                .Arg("slot", ArgTypes.Int, "part accessories only: one build slot (0-based)")
                .Arg("props", ArgTypes.ArrayOf(ArgTypes.String), "only these property names (default all; with parts hair, clothes and accessories \"<name>_ST\" selects a texture's scale / offset)")
                .Return("renderers", ArgTypes.ArrayOf(ArgTypes.Object), "{name, enabled, active, parent, slots [{material, shader, queue, keywords, props {name: value}}], hairPart (part hair), kind (part clothes), slot (part accessories: the build slot)} in hierarchy order (every renderer under the character with one of the names): floats / ints as numbers, colours and vectors as [4] numbers, textures as {name, w, h, format} (parts hair, clothes and accessories: + wrapU, wrapV, mips) or null")
                .Example("{\"cmd\":\"chara.edit.materials\",\"key\":\"<id>\",\"renderers\":[\"o_head\"],\"props\":[\"_Texture3UV\",\"_Gloss\"]}")
                .WithFlags(CommandFlags.ReadOnly), Materials);
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.rtstats", "Statistics of a character's face, body or clothes creators' render textures from a GPU read-back of mip 0 (read-only, numbers only; unavailable without a graphics device).", "chara edit")
                .Arg("key", ArgTypes.Node, "a built character", true)
                .Arg("part", ArgTypes.Enum("face", "body", "clothes"), "which creators (clothes: per kind and colour group the colour creator, then the detail creator)", false, "face")
                .Arg("kind", ArgTypes.Any, "part clothes only: one clothes kind (name or 0..7; default every kind)")
                .Arg("grid", ArgTypes.Int, "cells per side (1.." + MaxRtGrid + ")", false, 8)
                .Return("part", ArgTypes.String, "").Return("grid", ArgTypes.Int, "")
                .Return("creators", ArgTypes.ArrayOf(ArgTypes.Object), "{name (colour / detail), creator (the HS2Next name), kind and group (0..2; part clothes), material, mainTex, w, h, srgb, format, mips, mean [r,g,b,a], std [4], cells [grid × grid × 4] (rows from the texture's bottom row v = 0 upwards, u ascending), sha256 (of the RGBA32 mip-0 bytes), props (the create material's properties as chara.edit.materials)}; values are the stored 8-bit values / 255")
                .Example("{\"cmd\":\"chara.edit.rtstats\",\"key\":\"<id>\",\"part\":\"face\",\"grid\":8}")
                .WithFlags(CommandFlags.ReadOnly | CommandFlags.Slow).WithTimeout(120), RtStatsCmd);
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.digest", "SHA-256 per section of the canonical text of a character's realized face / body / hair / clothes / accessories (read-only): drawn materials, creators (create materials and input rows), head-bone or body shape-bone local transforms (the destinations of the body shape map; never the eye-look bones) or the hair bundles' correction transforms, with a graphics device the creators' RT read-backs, for the body the mesh normals and the bust stage, for the hair its dynamic-bone chains, for the clothes their def / half / option objects, for the accessories their slot objects (activity, parent, the two transform sets). The same text in the Editor and the player.", "chara edit")
                .Arg("key", ArgTypes.Node, "a built character", true)
                .Arg("parts", ArgTypes.Enum(DigestParts), "which part (hair: the four hair parts; no creators, so creators / rts hash empty texts; clothes: the eight clothes kinds, no bones, so bones hashes an empty text; accessories: every slot object, no creators and no bones; all: every part)", false, "face")
                .Arg("text", ArgTypes.Bool, "also return the canonical texts (texts {materials, creators, bones, normals?, bust?, chains?, objects?}) for diagnosis", false, false)
                .Return("parts", ArgTypes.String, "")
                .Return("materials", ArgTypes.String, "").Return("creators", ArgTypes.String, "").Return("bones", ArgTypes.String, "")
                .Return("rts", ArgTypes.String, "with a graphics device only", false)
                .Return("normals", ArgTypes.String, "parts body / all: the body part's mesh normals (per renderer: mesh name / vertex count and the SHA-256 of its normals as float32 bits)", false)
                .Return("bust", ArgTypes.String, "parts body / all: the bust stage (card values; per chain pattern, activity, gravity and per particle damping / elasticity / stiffness / inert as float bits)", false)
                .Return("objects", ArgTypes.String, "parts clothes / accessories / all: per clothes kind the part object's activity and its def / half / option objects' activity; per accessory slot the slot object's activity, its parent path and the local transforms of trfMove01 / trfMove02", false)
                .Return("chains", ArgTypes.String, "parts hair / all: the hair chains of the dynamic-bone stage (part, bundle, root, owner, active, noShake, particles and the captured rest local pose of their transforms as float bits)", false)
                .Return("texts", ArgTypes.Object, "with text", false)
                .Example("{\"cmd\":\"chara.edit.digest\",\"key\":\"<id>\",\"parts\":\"face\"}")
                .WithFlags(CommandFlags.ReadOnly | CommandFlags.Slow).WithTimeout(120), Digest);
        }

        /// <summary>The commands of <see cref="Phase42Names"/>.</summary>
        static void RegisterPhase42(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.swatch", "A colour swatch push of the Maker («カラーサンプル»), a row looked up by id (HS2's seven buttons: the vanilla ids 0..6). Skin (list 336): body.skin.color = HSVToRGB(BaseH, BaseS, BaseV), alpha 1; the face and body colour composites follow (one undo entry \"Edit body\"). Hair (list 305, on chara.hair.<part>.baseColor): base / top / under / specular = HSVToRGB of the row's Base / Top / Under / Specular columns (each stored alpha kept), metallic / smoothness = its columns, on that part and with «前髪と後ろ髪の色を統一» on all four; no auto derivation (one undo entry \"Edit hair\").", "chara edit")
                .Arg("key", ArgTypes.Node, "a character").Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "several characters (one entry; 336 and 305 are shared by both sexes)")
                .Arg("path", ArgTypes.String, "chara.body.skin.color (336) or chara.hair.<back|front|side|option>.baseColor (305), relative to `appearance.`", true)
                .Arg("ref", ArgTypes.ContentId, "the swatch row \"<guid>#<id>\" of 336 / 305 (illusion.hs2#0..6)", true)
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "").Return("consequences", ArgTypes.ArrayOf(ArgTypes.String), "").Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "")
                .Example("{\"cmd\":\"chara.edit.swatch\",\"key\":\"<id>\",\"path\":\"chara.hair.back.baseColor\",\"ref\":\"illusion.hs2#5\"}")
                .WithFlags(CommandFlags.Mutates), Swatch);
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.bust", "The bust of a built character (read-only, numbers only): the stored size / softness / weight, the breast chains' values as the bust stage holds them (the HS2 Studio Bridge's chara.bustPhysics: pattern-0 damping / elasticity / stiffness, inert per particle, gravity) and the breast normals (chara.meshNormals: source, entries, rate, SHA-256 of the drawn body mesh's normals).", "chara edit")
                .Arg("key", ArgTypes.Node, "a built character", true)
                .Return("sex", ArgTypes.Int, "0 male, 1 female (HS2's numbers)")
                .Return("size", ArgTypes.Float, "body.shape[1] as stored").Return("softness", ArgTypes.Float, "body.bust.softness as stored").Return("weight", ArgTypes.Float, "body.bust.weight as stored")
                .Return("physics", ArgTypes.Object, "{enabled (the bust stage runs; males: and BustOptions.MaleBustPhysics), stageEnabled, maleBustPhysics (males), softness, weight, size (the values the stage applied), chains [{name (Mune_L / Mune_R), kind (0 / 1), pattern, active, particles, damping, elasticity, stiffness (particle 0; the stage writes pattern 0 alike), inert [per particle], gravity [3]}]} (the breast chains, left then right)")
                .Return("normals", ArgTypes.Object, "{source {kind (vanilla / replaced: a body mesh a build hook replaced), hook (its id or null), guid (the id it gives the body or null), asset, bundle} or null (males), missing, missingReason, entries [{object, vertices, min, max, applicable, applied}], rate (HS2's rate of the stored size: 1 for size ≥ 0.5, else InverseLerp(0, 0.5, size); null for males), blendRate (the rate last written; null: none), applied, meshCopied, sha256 (of the drawn o_body_cf / o_body_cm normals as little-endian float32 x, y, z per vertex, lower-case hex), mesh (name/vertices), blends, copies, lastBlendMs, lastCopyMs, copyBytes}")
                .Example("{\"cmd\":\"chara.edit.bust\",\"key\":\"<id>\"}")
                .WithFlags(CommandFlags.ReadOnly), Bust);
        }

        /// <summary>The commands of <see cref="Phase43Names"/>.</summary>
        static void RegisterPhase43(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.hairBundle", "A hair bundle edit of the Maker («調整»): sets the given members of one bundle (rates stored unclamped) or resets them to the prefab default; the first edit of a part whose stored bundles are not the effective list (none stored, a count other than the prefab's) stores the whole list first (the prefab defaults); with «髪の調整をまとめて行う» (chara.hair.editor.ctrlTogether) each changed move / rotate component is set on every other bundle of the part (resets and noShake never propagate). all + reset = «全て初期化» (every bundle's rates to the defaults, noShake kept). One undo entry \"Edit hair\".", "chara edit")
                .Arg("key", ArgTypes.Node, "a character").Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "several characters (one entry; a character whose prefab lacks the bundle is skipped with warning edit.bundleSkipped)")
                .Arg("part", ArgTypes.Any, "the hair part: \"back\", \"front\", \"side\", \"option\" or 0..3", true)
                .Arg("index", ArgTypes.Int, "the bundle (CmpHair.boneInfo index, = the position in the materialized list); not with all")
                .Arg("all", ArgTypes.Bool, "with reset: every bundle of the part («全て初期化»)", false, false)
                .Arg("move", ArgTypes.Vec3, "the move rates [x, y, z] (0..1 between the prefab limits; stored as given)")
                .Arg("rotate", ArgTypes.Vec3, "the rotate rates [x, y, z]")
                .Arg("noShake", ArgTypes.Bool, "«揺らさない» (the bundle's chains inactive; a full chain rebuild)")
                .Arg("reset", ArgTypes.Bool, "reset to the prefab default instead of setting: the given members only select what is reset (their values are ignored; none = move and rotate; noShake given → false)", false, false)
                .Arg("axis", ArgTypes.Int, "with reset: only this component (0..2) of move / rotate (a slider row's ↺)")
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "paths that changed (the materialized list as chara.hair.<part>.bundles)").Return("consequences", ArgTypes.ArrayOf(ArgTypes.String), "").Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "")
                .Example("{\"cmd\":\"chara.edit.hairBundle\",\"key\":\"<id>\",\"part\":\"back\",\"index\":0,\"move\":[0.5,0.7,0.3]}")
                .WithFlags(CommandFlags.Mutates), HairBundle);
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.hairSkin", "«肌の色に合わせる» of the Maker: the part's underColor = HSVToRGB(H, max(0, S − 0.06), max(0, V − 0.06)) of body.skin.color (stored alpha kept) and smoothness = max(0, body.skin.gloss − 0.3); that part only, also with «前髪と後ろ髪の色を統一» on (HS2). The prefab's skin-match flag is the page's gate, not checked here. One undo entry \"Edit hair\".", "chara edit")
                .Arg("key", ArgTypes.Node, "a character").Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "several characters (one entry)")
                .Arg("part", ArgTypes.Any, "the hair part: \"back\", \"front\", \"side\", \"option\" or 0..3", true)
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "").Return("consequences", ArgTypes.ArrayOf(ArgTypes.String), "").Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "")
                .Example("{\"cmd\":\"chara.edit.hairSkin\",\"key\":\"<id>\",\"part\":\"back\"}")
                .WithFlags(CommandFlags.Mutates), HairSkin);
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.hair", "The hair of a built character (read-only, numbers only; HairReport, the reply shape of the HS2 Studio Bridge's chara.hairInfo): per part the row, the realized prefab's CmpHair facts, the stored and effective bundles with their limits, default rates, correction transforms (local / world) and dynamic-bone chains, the hair materials (shader, queue, main texture, the hair properties), the decoration colours per slot and the mesh pattern as written.", "chara edit")
                .Arg("key", ArgTypes.Node, "a built character", true)
                .Arg("part", ArgTypes.Any, "one part (\"back\", \"front\", \"side\", \"option\" or 0..3; default all four)")
                .Return("parts", ArgTypes.ArrayOf(ArgTypes.Object), "{part (0..3), item, object (the part object), name, mainAB, mainData, mainData02, ringOff, texAB, texD, texC (the row's columns), facts {hasCmpHair, useTopColor, useUnderColor, useSameSkinColorButton, useMesh (HS2's instance flag: the prefab's or, with HairUnit.MeshOnEveryHair = the HairMeshforAll rule, any live rendHair), useAcsColor01..03, acsDefColor [[4]], rendHair (live renderers), rendHairLength, rendAccessory, bundles (boneInfo count), prefabUseMesh, meshWritten (the unit writes the mesh values; the Maker's mesh gate), rendHairEmpty, acsDefColorCount, shownBundles; a part without CmpHair: {hasCmpHair: false} only}, stored (stored bundle count), bundles [{index, shown, title, trfCorrect, parent, limits {posMin, posMax, rotMin, rotMax}, base {pos, rot}, default {move, rotate}, stored {move, rotate, noShake} or null, effective {move, rotate, noShake}, local {position, euler, rotation} or null, world {position, rotation} or null, chains [{name (owner), root, enabled (the chain's own state: its Studio switch on and not noShake = HS2's DynamicBone.enabled after the FK hair re-application), active (it simulates now; needs physics on), relation, particles, noShake, userEnabled, bundle, rest {position, rotation} (the root particle's captured local pose)}]}], otherChains (hair chains in no bundle), materials [{renderer, slot, material, shader, renderQueue, mainTex, props, keywords}] (every rendHair slot), decoration [{renderer, slot, material, colors [_Color, _Color2, _Color3: [4] or null], shader}] (every rendAccessory slot), otherRenderers (names), partName, row, prefab (the realized prefab), realized, storedValid (the stored list applies), mesh {item, texture, scale [2], offset [2], color (_MeshColor_01 as written), written, format, w, h, onOff (_Mesh_onoff), uniform, storedColor, layout}}")
                .Return("shaderType", ArgTypes.Int, "hair.shaderType (0 hair_dithering, 1 hair_cutout)")
                .Return("meshOnEveryHair", ArgTypes.Bool, "HairUnit.MeshOnEveryHair (the HairMeshforAll rule)")
                .Example("{\"cmd\":\"chara.edit.hair\",\"key\":\"<id>\",\"part\":\"front\"}")
                .WithFlags(CommandFlags.ReadOnly), Hair);
        }

        /// <summary>The commands of <see cref="Phase44Names"/> (the clothes pages).</summary>
        static void RegisterPhase44(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.clothes", "The clothes of a built character (read-only, numbers only; ClothesReport), in the member names and order of the HS2 Studio Bridge's chara.clothesInfo, then HS2Next's: per kind the row and its columns, the realized prefab's facts (colour sets, main-colour alpha, break, option parts, def / half objects, colour groups), its default colour sets as document values, the compositing passes with their create-material properties, every material slot of the part as written, the objects' activity and the copy the catalog served the prefab from; the masks' flags, values and reference renderers.", "chara edit")
                .Arg("key", ArgTypes.Node, "a built character", true)
                .Arg("kind", ArgTypes.Any, "one clothes kind (\"top\", \"bottom\", \"innerTop\", \"innerBottom\", \"gloves\", \"pantyhose\", \"socks\", \"shoes\" or 0..7; default all eight)")
                .Arg("served", ArgTypes.Bool, "also name the copy the catalog serves each prefab from (served)", false, true)
                .Return("kinds", ArgTypes.ArrayOf(ArgTypes.Object), "{kind (top … shoes), index (0..7), category, item, realized, object, active, activeInHierarchy, skipped (a one-piece top / inner top skipped the part), state (the stored clothes state), stateKinds (the states the kind offers), row {id, Name, MainAB, MainData, MainData02, Coordinate, NotBra, StateType, BreakDisableMask, KokanHide, MainTex*, ColorMask*Tex, Over*Mask, then the Over*MaskAB columns} or null, facts {hasCmpClothes, useColorN [3], useColorA [3], useBreak, objOpt01, objOpt02 (live option objects), objTopDef, objTopHalf, objBotDef, objBotHalf (present), uvScalePattern [4], defMainColor04 [4], defGloss04, defMetallic04, defLayout [3 × [4]], defRotation [3] (the prefab's raw values), groups [{renderers, length, slots, names, mainTex [w, h] or null, hasCreators}]; a kind without CmpClothes: {hasCmpClothes: false} only}, creators [{group (0..2), creator (colour / detail), material, mainTex, rt [w, h], props (the create material's properties as chara.edit.materials), name (the HS2Next name), rebuilds, draw}], defaults [3 × {base, pattern, patternColor, layout, rotation, gloss, metallic}] (the prefab's sets as document values: «初期カラー») or null, objects {topDef, topHalf, botDef, botHalf (activity, null = absent), opt1 [], opt2 []}, materials [{renderer, group (1..3; 0 = no colour group), slot, slots, enabled, active, material, shader, queue, keywords, props {_AlphaEx, _Cutoff, _alpha_a.._alpha_d, _Color, _Glossiness, _Metallic, _MainTex, _DetailMainTex: textures {name, w, h, format, creator (\"colour j\" / \"detail j\" when it is the group's creator RT)}}, mainTex, detailMainTex, alphaEx}] (every slot of every renderer under the part object), then rowRef (\"category:key\" the part unit resolved), prefab, onePiece, served {MainData {kind (zipmod / abdata / none), guid}} (served)}")
                .Return("flags", ArgTypes.Object, "{notBot, notInnerT, notInnerB (the one-piece couplings), underMaskReflectionType (where the lower masks come from: 0 top, 1 bottom, -1 none), underMaskBreakDisable (that row's BreakDisableMask), lowerOk0, lowerOk1}")
                .Return("masks", ArgTypes.Object, "{body, bra, innerTB, innerB, panst: {material, _alpha_a, _alpha_b, _alpha_c, _alpha_d, _AlphaMask, _AlphaMask2 (texture names)} or null (no reference renderer)}")
                .Return("maskRenderers", ArgTypes.Object, "{bra, innerTB, innerB, panst}: the reference renderers of the masks (null = none)")
                .Return("subMenu", ArgTypes.Object, "{bottom, innerBottom}: the kind can be edited (false while a one-piece top / inner top is worn)")
                .Return("male", ArgTypes.Bool, "")
                .Example("{\"cmd\":\"chara.edit.clothes\",\"key\":\"<id>\",\"kind\":\"top\"}")
                .WithFlags(CommandFlags.ReadOnly), Clothes);
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.clothesDefaults", "«初期カラー» of the Maker: a clothes kind's three colour sets take the worn prefab's defaults (main colour, pattern, pattern colour, gloss, metallic, layout, rotation as document values). One undo entry \"Edit clothes\"; unsupported without the prefab's defaults (a none row; several keys: such characters are skipped with warning edit.defaultsUnavailable).", "chara edit")
                .Arg("key", ArgTypes.Node, "a character").Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "several characters (one entry)")
                .Arg("kind", ArgTypes.Any, "the clothes kind (\"top\" … \"shoes\" or 0..7)", true)
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "").Return("consequences", ArgTypes.ArrayOf(ArgTypes.String), "").Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "")
                .Example("{\"cmd\":\"chara.edit.clothesDefaults\",\"key\":\"<id>\",\"kind\":\"top\"}")
                .WithFlags(CommandFlags.Mutates), ClothesDefaults);
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.coordinate", "Loads a coordinate onto characters as the Maker's «衣装カード» buttons: clothes (the eight clothes parts, the accessories kept), accessories (the accessory list and its rules, the clothes kept) or all (the whole coordinate with its plugin data); the coordinate name follows the file in every mode; per-part changes (no rebuild), the accessory visibility map filled and pruned. An HS2 coordinate card is imported first (in memory); characters of the other sex are refused (several keys: skipped with warning edit.coordinateSkipped); the clothes / accessories modes add warning coord.extKept; the reader's findings are warnings. One undo entry \"Load coordinate\".", "chara edit")
                .Arg("key", ArgTypes.Node, "a character").Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "several characters (one entry)")
                .Arg("file", ArgTypes.Path, "an HS2 coordinate card (.png; a relative path under <hs2Root>/UserData/coordinate/<sex of the first key>, .png appended when missing) or a .hs2ncoord file (relative: under <UserData>/coordinate); absolute paths of either", true)
                .Arg("mode", ArgTypes.Enum(CoordinateModes), "what is loaded", false, "all")
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "").Return("consequences", ArgTypes.ArrayOf(ArgTypes.String), "").Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "")
                .Return("source", ArgTypes.Object, "{file (absolute), sex (0 / 1), name, mode}: the coordinate read")
                .Return("imported", ArgTypes.Bool, "the file was an HS2 coordinate card (imported in memory)")
                .Example("{\"cmd\":\"chara.edit.coordinate\",\"key\":\"<id>\",\"file\":\"coordinate.png\",\"mode\":\"clothes\"}")
                .WithFlags(CommandFlags.Mutates | CommandFlags.Slow).WithTimeout(300), Coordinate);
        }

        /// <summary>The commands of <see cref="Phase45Names"/> (the accessory pages).</summary>
        static void RegisterPhase45(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.accessories", "The accessories of a built character (read-only, numbers only; AccessoryReport and the slot objects), per build slot (0-based; the Accessories page shows slot + 1) in the member names, meaning and order of the HS2 Studio Bridge's chara.accessoryInfo, then HS2Next's: the stored part (type, id = the item ref, parentKey in effect, addMove, colorInfo, …), the scene visibility (show), the slot object (loaded, activity), the row and its columns, the realized prefab's CmpAccessory facts and raw defaults, the anchor (the slot object's parent), the object's pose, the two transform sets, every material slot under the object as written, the dynamic-bone chains; then the accessory id, the stored parent, the preferred slot, the plugin rules, the defaults «初期カラー» writes, the served copy. The top level adds the hair parts' activity.", "chara edit")
                .Arg("key", ArgTypes.Node, "a built character", true)
                .Arg("slot", ArgTypes.Int, "one build slot (0-based; a slot without an accessory reports type 350 and loaded false)")
                .Arg("accessory", ArgTypes.String, "one accessory by id (not with slot)")
                .Arg("served", ArgTypes.Bool, "also name the copy the catalog serves each prefab from (served)", false, true)
                .Return("slots", ArgTypes.ArrayOf(ArgTypes.Object), "per slot holding an accessory (ascending; or the one asked for): {slot, extra (≥ 20), type (the category, 350 = none), id (the item ref \"<guid>#<id>\" or null), rawId (its local id), parentKey (the key in effect: the stored parent, else the row's Parent column when it is one of the 54 attach points, else \"\"), addMove [2 × {position, rotation, scale}] (stored), colorInfo [4 × {color, glossPower, metallicPower, smoothnessPower}] (stored), hideCategory, hideTiming, noShake, partsOfHead (the key in effect is a head attach point), show (the scene's visibility state.accessories), loaded, object, active, activeInHierarchy, row {category, id, ref, Name, MainManifest, MainAB, MainData, Parent, TexManifest, TexAB, TexD, TexC} or null, facts {hasCmpAccessory, parentColumn, headWeighted, fixed; with an object: cmpComponents, cmpOnRoot, cmpUsed, particleSystems, dynamicBones, colliders; with a CmpAccessory: typeHair, useColor [3], useGloss [4], useMetallic [4], alphaRenderers, alphaLength, normalRenderers, normalLength, move1, move2, moveNames [2]}, particleDummy, defaults [4 × {color, gloss, metallic}] (the prefab's raw values), anchor {name, path} (the slot object's parent), world {position, rotation, lossyScale, localPosition, localRotation}, moves [2 × null | {name, position, rotation (local Euler), localRotation, scale, worldPosition}], shaderType (the character's hair.shaderType), materials [{renderer, kind (normal / alpha / normal+alpha / other), slot, slots, enabled, active, activeInHierarchy, bounds {center, size}, material, shader, queue, mainTex, props {_Color, _Color2, _Color3, _TopColor, _UnderColor, _Specular, _Glossiness…4, _Metallic…4, _Smoothness: those the shader declares}}] (every renderer under the object), chains [{object, root, colliders, enabled, active, noShake, particles}]; then accessory (the id; null for a card build), category, item, parent (stored; \"\" = the row default), importSlot (the preferred slot), notLoaded (≥ 20 with MoreAccessories off), showFlag (the read MoreAccessories show), anchorUsed (the attach point the loader used, \"head bones\" or \"none\"), hairShaderType (the hair shader type applied to a hair-type slot, -1 otherwise), colourDefaults [4 × {color, gloss, metallic, smoothness}] (the document values «初期カラー» writes) or null, rules (Additional Accessory Controls' data as saved) or null, rowRef, served {MainData {kind, guid}} (served)}")
                .Return("slotCount", ArgTypes.Int, "the realized slots (20 + the loaded MoreAccessories slots)")
                .Return("shaderType", ArgTypes.Int, "hair.shaderType (0 hair_dithering, 1 hair_cutout)")
                .Return("hairActive", ArgTypes.ArrayOf(ArgTypes.Any), "the four hair part objects' own activity (null: none)")
                .Return("hairChildren", ArgTypes.ArrayOf(ArgTypes.Any), "per hair part [active children, all children] (Additional Accessory Controls' hair rules switch the children)")
                .Return("male", ArgTypes.Bool, "").Return("extraSlotsOn", ArgTypes.Bool, "the build loads MoreAccessories slots")
                .Return("freeSlots", ArgTypes.ArrayOf(ArgTypes.Int), "the free build slots the slot pickers offer (0..19, then up to the highest used + 10); null for a card build")
                .Example("{\"cmd\":\"chara.edit.accessories\",\"key\":\"<id>\",\"slot\":4}")
                .WithFlags(CommandFlags.ReadOnly), Accessories);
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.accessoryDefaults", "«初期カラー» of the Maker's accessory page: the used colour areas and the alpha colour of a non-hair accessory take the worn prefab's defaults (colour, gloss, metallic; unused areas kept). One undo entry \"Edit accessory\"; unsupported for a hair-type prefab or without the prefab's facts.", "chara edit")
                .Arg("key", ArgTypes.Node, "a character", true)
                .Arg("accessory", ArgTypes.String, "the accessory id (or slot)")
                .Arg("slot", ArgTypes.Int, "the accessory's build slot (0-based; instead of accessory)")
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "").Return("consequences", ArgTypes.ArrayOf(ArgTypes.String), "").Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "")
                .Example("{\"cmd\":\"chara.edit.accessoryDefaults\",\"key\":\"<id>\",\"slot\":4}")
                .WithFlags(CommandFlags.Mutates), AccessoryDefaults);
            CommandKit.Register(r, app, CommandKit.Spec("chara.edit.accessoryHairColour", "«髪の毛の色を取得» of the Maker's accessory page: a hair-type accessory's colours 1–4 take a hair part's base / top / under / specular colours and its colour 1's smoothness / metallic the part's (the stored hair values). One undo entry \"Edit accessory\"; unsupported for a prefab that is not hair-type.", "chara edit")
                .Arg("key", ArgTypes.Node, "a character", true)
                .Arg("accessory", ArgTypes.String, "the accessory id (or slot)")
                .Arg("slot", ArgTypes.Int, "the accessory's build slot (0-based; instead of accessory)")
                .Arg("part", ArgTypes.Any, "the hair part: \"back\", \"front\", \"side\", \"option\" or 0..3 («後ろ髪», «前髪», «横髪», «エクステ»)", true)
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "").Return("consequences", ArgTypes.ArrayOf(ArgTypes.String), "").Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "")
                .Example("{\"cmd\":\"chara.edit.accessoryHairColour\",\"key\":\"<id>\",\"slot\":5,\"part\":\"front\"}")
                .WithFlags(CommandFlags.Mutates), AccessoryHairColour);
        }

        // ------------------------------------------------------------ helpers

        /// <summary>The character nodes of key / keys (one of them; wrong_kind for other nodes).</summary>
        static List<TreeNode> CharaKeys(CoreContext c) => CommandKit.Keys(c, CommandKit.KeyKind.Chara);

        /// <summary>The stored documents of a character node; unsupported for a card-backed node.</summary>
        static CharaSubtree Stored(TreeNode n, string arg = "key")
        {
            var st = n.Appearance?.Stored;
            if (st == null)
                throw new BridgeException(BridgeCodes.Unsupported, "character not imported (node " + n.Id + " has no stored documents)", new BridgeErrorDetail { Arg = arg });
            return st;
        }

        /// <summary>A path of the reply: relative to `appearance.` for appearance paths, else the scene path as is.</summary>
        static string Rel(string path) => AppearancePaths.ToRelative(path) ?? path;

        static List<string> Rels(IEnumerable<string> paths)
        {
            var l = new List<string>();
            if (paths != null) foreach (var p in paths) l.Add(Rel(p));
            return l;
        }

        /// <summary>The full canonical appearance path of a bridge path (relative to `appearance.` or full); bad_args otherwise.</summary>
        static string FullPath(string text, string arg)
        {
            string full = string.IsNullOrEmpty(text) ? null : FieldPath.Canonical(AppearancePaths.FromRelative(text));
            if (full == null || !AppearancePaths.IsAppearance(full) || full == AppearancePaths.Root)
                throw CommandKit.BadArgs(arg, RelDoc, "\"" + text + "\" is not a path under `appearance.`");
            return full;
        }

        /// <summary>The reply of a Maker op: changed / consequences / noop (relative paths).</summary>
        static BridgeReply EditReply(OpResult res)
        {
            var v = res.Value as MakerResult;
            return new BridgeReply().With("changed", Rels(res.Changed)).With("consequences", Rels(v?.Consequences)).With("noop", Rels(res.NoOp));
        }

        static BridgeException FromCommand(CommandException e, string arg) =>
            new BridgeException(e.Code ?? BridgeCodes.BadArgs, arg + ": " + e.Message, new BridgeErrorDetail { Arg = arg });

        static string DocText(string entry, Action<CanonicalJsonWriter> write) => Encoding.UTF8.GetString(CanonicalJsonWriter.ToBytes(entry, write));

        // ------------------------------------------------------------ chara.edit.get

        static object Get(CoreContext c)
        {
            var n = CommandKit.Chara(c, "key");
            var st = Stored(n);
            var values = new BridgeReply();
            var k = c.Args.Kind("paths");
            if (CommandKit.Absent(k))
            {
                values["chara"] = new RawJson(DocText(CharaDocument.EntryName, st.Chara.Write));
                values["coord"] = new RawJson(DocText(CoordDocument.EntryName, st.Coord.Write));
                return new BridgeReply().With("values", values);
            }
            if (k != ArgKind.Array) throw CommandKit.BadArgs("paths", ArgTypes.ArrayOf(ArgTypes.String));
            foreach (var p in c.Args.Get<string[]>("paths"))
            {
                if (p == null) throw CommandKit.BadArgs("paths", ArgTypes.ArrayOf(ArgTypes.String), "a path is null");
                if (p == AppearancePaths.CharaDoc) { values[p] = new RawJson(DocText(CharaDocument.EntryName, st.Chara.Write)); continue; }
                if (p == AppearancePaths.CoordDoc) { values[p] = new RawJson(DocText(CoordDocument.EntryName, st.Coord.Write)); continue; }
                if (p == ImportPath) { values[p] = ImportJson(st); continue; }
                string full = FullPath(p, "paths");
                try { values[p] = new RawJson(FieldAccessor.GetJson(c.Session.Model, n.Id, full)); }
                catch (CommandException e) { throw FromCommand(e, "paths"); }
            }
            return new BridgeReply().With("values", values);
        }

        // ------------------------------------------------------------ chara.edit.set / item / preset

        static object Set(CoreContext c)
        {
            var nodes = CharaKeys(c);
            if (c.Args.Kind("set") != ArgKind.Object) throw CommandKit.BadArgs("set", ArgTypes.Object);
            var set = c.Args.Nested("set");
            if (set == null || set.Names.Count == 0) throw CommandKit.BadArgs("set", ArgTypes.Object, "set names no member");
            bool rebuild = c.Args.Get("rebuild", false);
            var model = c.Session.Model;
            Stored(nodes[0], "key");   // the value types come from the first character's documents
            var sets = new List<(string, object)>();
            foreach (var name in set.Names)
            {
                string arg = "set." + name;
                string full = FullPath(name, arg);
                Type t;
                try { t = FieldAccessor.LeafType(model, nodes[0].Id, full); }
                catch (CommandException e) { throw FromCommand(e, arg); }
                object v;
                try { v = FieldJson.FromText(set.RawJson(name), t); }
                catch (CommandException e) { throw FromCommand(e, arg); }
                catch (Exception e) { throw CommandKit.BadArgs(arg, "the save-format JSON form of " + full, e.Message); }
                sets.Add((full, v));
            }
            var res = CommandKit.Check(c, CommandKit.Ops(c).EditAppearance(CommandKit.Ids(nodes), sets, CommandKit.Exec(c, null), rebuild), "set");
            return EditReply(res);
        }

        static object Item(CoreContext c)
        {
            var nodes = CharaKeys(c);
            string path = c.Args.Require<string>("path");
            string full = FullPath(path, "path");
            string reference = CommandKit.TextOrInt(c, "ref", ArgTypes.ContentId, "\"<guid>#<id>\" or a category number");
            var res = CommandKit.Check(c, CommandKit.Ops(c).ChangeItem(CommandKit.Ids(nodes), full, reference, CommandKit.Exec(c, null)), "ref");
            return EditReply(res);
        }

        static object Preset(CoreContext c)
        {
            var nodes = CharaKeys(c);
            string head = CommandKit.OptString(c, "head", "a head ref \"<guid>#<id>\"");
            var res = CommandKit.Check(c, CommandKit.Ops(c).ApplyFacePreset(CommandKit.Ids(nodes), head, CommandKit.Exec(c, null)), "head");
            return EditReply(res);
        }

        // ------------------------------------------------------------ chara.edit.layout / reset (Phase 4.1)

        static object Layout(CoreContext c)
        {
            var nodes = CharaKeys(c);
            string full = FullPath(c.Args.Require<string>("path"), "path");
            if (c.Args.Kind("ref") != ArgKind.String) throw CommandKit.BadArgs("ref", "a layout row ref \"<guid>#<id>\"");
            string reference = c.Args.Get<string>("ref");
            var res = CommandKit.Check(c, CommandKit.Ops(c).ApplyLayoutPreset(CommandKit.Ids(nodes), full, reference, CommandKit.Exec(c, null)), "ref");
            return EditReply(res);
        }

        static object Reset(CoreContext c)
        {
            var nodes = CharaKeys(c);
            if (c.Args.Kind("paths") != ArgKind.Array) throw CommandKit.BadArgs("paths", ArgTypes.ArrayOf(ArgTypes.String));
            JArray pa;
            try { pa = JArray.Parse(c.Args.RawJson("paths")); }
            catch (Exception e) { throw CommandKit.BadArgs("paths", ArgTypes.ArrayOf(ArgTypes.String), e.Message); }
            if (pa.Count == 0) throw CommandKit.BadArgs("paths", ArgTypes.ArrayOf(ArgTypes.String), "paths names no member");
            var paths = new List<string>(pa.Count);
            for (int i = 0; i < pa.Count; i++)
            {
                if (pa[i].Type != JTokenType.String) throw CommandKit.BadArgs("paths[" + i + "]", ArgTypes.String);
                string text = (string)pa[i];
                int hash = text.LastIndexOf('#');
                string comp = null;
                if (hash > 0 && hash < text.Length - 1 && text.IndexOf('"', hash) < 0)
                {
                    comp = text.Substring(hash + 1);
                    if (!int.TryParse(comp, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                        throw CommandKit.BadArgs("paths[" + i + "]", "a member path or \"path#<component>\"", "\"" + text + "\": the component is not a number");
                    text = text.Substring(0, hash);
                }
                paths.Add(FullPath(text, "paths[" + i + "]") + (comp != null ? "#" + comp : ""));
            }
            var defaults = new List<object>(paths.Count);
            var dk = c.Args.Kind("defaults");
            if (CommandKit.Absent(dk))
            {
                var table = SchemaDefaults();
                foreach (var p in paths) defaults.Add(table.TryGetValue(p, out var d) ? d : AccessoryRowDefault(p));
            }
            else
            {
                if (dk != ArgKind.Array) throw CommandKit.BadArgs("defaults", ArgTypes.ArrayOf(ArgTypes.Any));
                var da = JArray.Parse(c.Args.RawJson("defaults"));
                if (da.Count != paths.Count) throw CommandKit.BadArgs("defaults", "one default per path (null = none)", "defaults has " + da.Count + " values for " + paths.Count + " paths");
                for (int i = 0; i < da.Count; i++) defaults.Add(DefaultOf(da[i], "defaults[" + i + "]"));
            }
            var had = new BridgeReply();
            foreach (var n in nodes) had[n.Id] = HasReference(c, n.Id);   // read before the op (B's note)
            var res = CommandKit.Check(c, CommandKit.Ops(c).ResetAppearance(CommandKit.Ids(nodes), paths, defaults, CommandKit.Exec(c, null)), "paths");
            var reply = EditReply(res);
            reply["reference"] = had;
            return reply;
        }

        /// <summary>The node has reset reference documents of this app's scene session (MakerServices.References is bound to
        /// one session; references of another session's node with the same id do not count).</summary>
        static bool HasReference(CoreContext c, string id)
        {
            var refs = MakerServices.References;
            return refs != null && ReferenceEquals(refs.Session, c.Session) && refs.Has(id);
        }

        /// <summary>A `defaults` element in the op's form: number → double, [numbers] → double[], bool, string, null.</summary>
        static object DefaultOf(JToken t, string arg)
        {
            switch (t.Type)
            {
                case JTokenType.Null: return null;
                case JTokenType.Integer: case JTokenType.Float: return (double)t;
                case JTokenType.Boolean: return (bool)t;
                case JTokenType.String: return (string)t;
                case JTokenType.Array:
                {
                    var a = (JArray)t;
                    var d = new double[a.Count];
                    for (int i = 0; i < a.Count; i++)
                    {
                        if (a[i].Type != JTokenType.Integer && a[i].Type != JTokenType.Float) throw CommandKit.BadArgs(arg, "a number, [numbers], bool, string or null");
                        d[i] = (double)a[i];
                    }
                    return d;
                }
            }
            throw CommandKit.BadArgs(arg, "a number, [numbers], bool, string or null");
        }

        static Dictionary<string, object> schemaDefaults;

        /// <summary>The default of an accessory member's Accessories row (the rows are generated per accessory, so they are not in
        /// <see cref="SchemaDefaults"/>): "…accessories[\"&lt;id&gt;\"].&lt;member&gt;" or "…#&lt;component&gt;" of a vector row (a transform
        /// component: 0, 0, 1); null for other paths and rows without a default.</summary>
        static object AccessoryRowDefault(string path)
        {
            string text = path;
            int comp = -1;
            int hash = text.LastIndexOf('#');
            if (hash > 0 && text.IndexOf('"', hash) < 0 && int.TryParse(text.Substring(hash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int cv))
            {
                comp = cv;
                text = text.Substring(0, hash);
            }
            if (!AppearancePaths.TryAccessory(text, out string id, out string member) || string.IsNullOrEmpty(member)) return null;
            // the slot and the category only decide gates, section ids and the item list: the defaults are the same for all
            foreach (var s in InspectorSchemas.AccessoryRows(id, 0, InspectorSchemas.AccessoryCategoryFirst))
                foreach (var f in s.Fields)
                {
                    if (f.Default == null || f.Path == null || f.Kind == FieldKind.Button || f.Kind == FieldKind.PartList || f.Kind == FieldKind.Push) continue;
                    if (FieldPath.Canonical(f.Path) != text) continue;
                    if (comp < 0) return f.Default;
                    return f.Default is double[] v && comp < v.Length ? (object)v[comp] : null;
                }
            return null;
        }

        /// <summary>The Edit tab rows' defaults by full path ("path#c" for component rows), templates expanded ({side}: left /
        /// right; {k}: 0 / 1): what the row's ↺ passes to StudioOps.ResetAppearance.</summary>
        static Dictionary<string, object> SchemaDefaults()
        {
            if (schemaDefaults != null) return schemaDefaults;
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var s in InspectorSchemas.CharaEdit)
                foreach (var f in s.Fields)
                {
                    if (f.Default == null || f.Path == null || f.Kind == FieldKind.Button || f.Kind == FieldKind.PartList || f.Kind == FieldKind.Push) continue;
                    string ph = f.Placeholder;
                    string[] values = ph == null ? new string[] { null } : ph == InspectorSchemas.SidePlaceholder ? new[] { "left", "right" } : ph == InspectorSchemas.PaintPlaceholder ? new[] { "0", "1" } : new string[0];
                    foreach (var v in values)
                    {
                        string p = FieldPath.Canonical(v == null ? f.Path : f.Expand(v));
                        if (p == null) continue;
                        string key = f.Component >= 0 ? p + "#" + f.Component.ToString(CultureInfo.InvariantCulture) : p;
                        if (!d.ContainsKey(key)) d[key] = f.Default;
                    }
                }
            return schemaDefaults = d;
        }

        // ------------------------------------------------------------ chara.edit.accessory

        static object Accessory(CoreContext c)
        {
            var n = CommandKit.Chara(c, "key");
            string op = c.Args.Require<string>("op");
            var ops = CommandKit.Ops(c);
            var o = CommandKit.Exec(c, null);
            OpResult res;
            string slotOf = null;   // the accessory whose build slot the reply reports
            switch (op)
            {
                case "add":
                {
                    if (c.Args.Kind("category") != ArgKind.Integer) throw CommandKit.BadArgs("category", "an accessory category 351–363");
                    string reference = c.Args.Require<string>("ref");
                    string parent = CommandKit.OptString(c, "parent");
                    int? index = CommandKit.OptInt(c, "index");
                    int? slot = CommandKit.Opt(c, "slot", SlotArg);
                    if (slot != null && index != null) throw CommandKit.BadArgs("slot", "slot or index", "give a build slot or a list index, not both");
                    res = CommandKit.Check(c, ops.AddAccessory(n.Id, c.Args.Get<int>("category"), reference, parent, index, o, slot), "ref");
                    break;
                }
                case "remove":
                    res = CommandKit.Check(c, ops.RemoveAccessory(n.Id, c.Args.Require<string>("accessory"), o), "accessory");
                    break;
                case "move":
                {
                    string acc = c.Args.Require<string>("accessory");
                    int? index = CommandKit.OptInt(c, "index");
                    if (index == null) throw new BridgeException(BridgeError.Missing("index", ArgTypes.Int));
                    res = CommandKit.Check(c, ops.MoveAccessory(n.Id, acc, index.Value, o), "index");
                    slotOf = acc;
                    break;
                }
                case "moveSlot":
                {
                    string acc = c.Args.Require<string>("accessory");
                    int? slot = CommandKit.Opt(c, "slot", SlotArg);
                    if (slot == null) throw new BridgeException(BridgeError.Missing("slot", SlotExpected));
                    if (c.Args.Has("index")) throw CommandKit.BadArgs("index", "slot (moveSlot)", "moveSlot takes the target build slot; move reorders by index");
                    res = CommandKit.Check(c, ops.MoveAccessoryToSlot(n.Id, acc, slot.Value, o), "slot");
                    slotOf = acc;
                    break;
                }
                case "copy":
                {
                    string from = c.Args.Require<string>("from");
                    string to = CommandKit.OptString(c, "to");
                    int? toSlot = CommandKit.Opt(c, "toSlot", SlotArg);
                    if (to != null && toSlot != null) throw CommandKit.BadArgs("toSlot", "to or toSlot", "give a target accessory or a target build slot, not both");
                    var parts = AccessoryCopyParts.None;
                    var pk = c.Args.Kind("parts");
                    if (CommandKit.Absent(pk)) parts = AccessoryCopyParts.Slot;
                    else if (pk != ArgKind.Array) throw CommandKit.BadArgs("parts", ArgTypes.ArrayOf(ArgTypes.String));
                    else
                        foreach (var p in c.Args.Get<string[]>("parts"))
                        {
                            switch (p)
                            {
                                case "slot": parts |= AccessoryCopyParts.Slot; break;
                                case "move1": parts |= AccessoryCopyParts.Move1; break;
                                case "move2": parts |= AccessoryCopyParts.Move2; break;
                                default: throw CommandKit.BadArgs("parts", "\"slot\", \"move1\", \"move2\"", "\"" + p + "\" is not a copy part");
                            }
                        }
                    if (parts == AccessoryCopyParts.None) throw CommandKit.BadArgs("parts", "\"slot\", \"move1\", \"move2\"", "parts names nothing to copy");
                    MirrorAxis mirror;
                    switch (c.Args.Get("mirror", "none"))
                    {
                        case "none": mirror = MirrorAxis.None; break;
                        case "leftRight": mirror = MirrorAxis.LeftRight; break;
                        case "upDown": mirror = MirrorAxis.UpDown; break;
                        default: throw CommandKit.BadArgs("mirror", ArgTypes.Enum("none", "leftRight", "upDown"));
                    }
                    res = CommandKit.Check(c, ops.CopyAccessory(n.Id, from, toSlot, to, parts, c.Args.Get("mirrorParent", false), mirror, o), toSlot != null ? "toSlot" : "from");
                    // the target: the new accessory, else the overwritten one (by id, or the one in the target slot)
                    slotOf = to ?? (toSlot != null ? StudioOps.AccessoryIdOfSlot(c.Session.Model.Find(n.Id) ?? n, toSlot.Value) : null);
                    break;
                }
                default: throw CommandKit.BadArgs("op", ArgTypes.Enum(AccessoryOps), "\"" + op + "\" is not an accessory operation");
            }
            var r = new BridgeReply();
            var v = res.Value as MakerResult;
            if (v != null && v.NewAccessoryIds.Count > 0) { r["accessory"] = v.NewAccessoryIds[0]; slotOf = v.NewAccessoryIds[0]; }
            // the build slot after the op (the slot table of the stored coordinate)
            if (slotOf != null) r["slot"] = StudioOps.AccessorySlotOf(c.Session.Model.Find(n.Id) ?? n, slotOf);
            foreach (var kv in EditReply(res)) r[kv.Key] = kv.Value;
            return r;
        }

        /// <summary>A build slot argument: a 0-based integer ≥ 0 (the page shows slot + 1);
        /// bad_args otherwise (missing: bad_args "missing argument").</summary>
        static int SlotArg(CoreContext c, string arg)
        {
            switch (c.Args.Kind(arg))
            {
                case ArgKind.Integer:
                {
                    int s = c.Args.Get<int>(arg);
                    if (s < 0) throw CommandKit.BadArgs(arg, SlotExpected, "slot " + s + " is negative");
                    return s;
                }
                case ArgKind.Missing:
                case ArgKind.Null: throw new BridgeException(BridgeError.Missing(arg, SlotExpected));
                default: throw CommandKit.BadArgs(arg, SlotExpected);
            }
        }

        /// <summary>The accessory of `accessory` (an id) or `slot` (a build slot of the stored coordinate), exactly one of them;
        /// not_found for a slot without an accessory.</summary>
        static string AccessoryOfArgs(CoreContext c, TreeNode n)
        {
            var ak = c.Args.Kind("accessory");
            bool hasAcc = !CommandKit.Absent(ak);
            int? slot = CommandKit.Opt(c, "slot", SlotArg);
            if (hasAcc && slot != null) throw CommandKit.BadArgs("slot", "accessory or slot", "give an accessory id or a build slot, not both");
            if (hasAcc)
            {
                if (ak != ArgKind.String) throw CommandKit.BadArgs("accessory", ArgTypes.String);
                return c.Args.Get<string>("accessory");
            }
            if (slot == null) throw new BridgeException(BridgeError.Missing("accessory", "an accessory id or a build slot (slot)"));
            Stored(n);
            string id = StudioOps.AccessoryIdOfSlot(n, slot.Value);
            if (id == null) throw CommandKit.NotFound("character " + n.Id + " has no accessory in build slot " + slot.Value + " (shown " + InspectorSchemas.AccessorySlotLabel(slot.Value) + ")", "slot");
            return id;
        }

        // ------------------------------------------------------------ chara.edit.list

        static object List(CoreContext c)
        {
            var catalog = c.Services?.Catalog;
            var lists = catalog != null ? MakerCatalog.For(catalog) : null;
            if (lists == null) throw new BridgeException(BridgeCodes.Unavailable, "no content catalog (the Maker lists need the game data)");
            var node = CommandKit.OptionalNode(c, "key");
            if (node != null && node.Kind != NodeKind.Chara)
                throw new BridgeException(BridgeError.WrongKind("node " + node.Id + " is a " + CommandKit.KindName(node) + ", not a character", "key", "chara"));
            bool hasPath = c.Args.Has("path"), hasCat = c.Args.Has("category");
            if (hasPath == hasCat) throw CommandKit.BadArgs(hasPath ? "path" : "category", "one of path (with key), category", "give path or category, not " + (hasPath ? "both" : "neither"));
            int sex;
            var stored = node?.Appearance?.Stored;
            if (node != null)
            {
                if (c.Args.Has("sex")) throw CommandKit.BadArgs("sex", "sex without key", "the character's sex decides with key");
                sex = StudioOps.SexOf(node) == 0 ? 0 : 1;
            }
            else sex = CommandKit.Sex(c, "sex", 1);
            int category;
            string memberPath = null;
            if (hasPath)
            {
                if (node == null) throw new BridgeException(BridgeError.Missing("key", ArgTypes.Node));
                string full = FullPath(c.Args.Require<string>("path"), "path");
                memberPath = full;
                category = MakerEdits.CategoryOf(full, sex, stored?.Coord);
                if (category < 0 || !MakerLists.HasList(category, sex)) throw CommandKit.BadArgs("path", "a ref member with a list (chara.face.skin, coord.clothes.top.item, …)", "\"" + c.Args.Get<string>("path") + "\" has no list for this character");
            }
            else
            {
                if (c.Args.Kind("category") != ArgKind.Integer) throw CommandKit.BadArgs("category", ArgTypes.Int);
                category = c.Args.Get<int>("category");
            }
            // face skins: the head filter
            ContentId head = default;
            string headText = CommandKit.OptString(c, "head");
            if (headText != null)
            {
                if (!ContentId.TryParse(headText, out head)) throw CommandKit.BadArgs("head", "a head ref \"<guid>#<id>\"");
            }
            else if (stored != null && FaceSkinHeads.IsSkinCategory(category)) ContentId.TryParse(stored.Chara.Face.Head, out head);
            var rows = lists.Rows(category, sex, head.IsEmpty ? null : ListContext.ForHead(head));
            string language = c.Args.Get("language", "ja");
            if (language != "ja" && language != "en" && language != "zh-Hans") throw CommandKit.BadArgs("language", ArgTypes.Enum("ja", "en", "zh-Hans"));
            int offset = c.Args.Get("offset", 0), limit = c.Args.Get("limit", DefaultListLimit);
            if (offset < 0) throw CommandKit.BadArgs("offset", "an index ≥ 0");
            if (limit < 1 || limit > MaxListLimit) throw CommandKit.BadArgs("limit", "1.." + MaxListLimit);
            string search = CommandKit.OptString(c, "search");
            string key = string.IsNullOrEmpty(search) ? null : SearchText.Normalize(search);
            bool skins = FaceSkinHeads.IsSkinCategory(category);
            // face skins filtered by a head: one row per listed reference, headRef = that head (a later definition of a repeated
            // skin may be what lists it there, H3). Unfiltered: one row per definition as HS2's list control holds them
            // (Sideloader keeps every definition of a reference repeated inside its zipmod): the first with its head after
            // faceSkinInfo, the later ones right after it with their raw HeadID.
            var rep = skins && head.IsEmpty ? lists.Source.RepeatedDefinitions(category) : null;
            var shown = new List<(ListRow row, ContentId head)>(rows.Count);
            foreach (var row in rows)
            {
                if (key != null && !Matches(row, key)) continue;
                if (!skins) { shown.Add((row, default)); continue; }
                shown.Add((row, head.IsEmpty ? lists.HeadOf(row) : head));
                if (rep != null && !row.IsVanilla && rep.TryGetValue(row.Key, out var defs) && defs != null)
                    for (int d = 1; d < defs.Count; d++) shown.Add((row, FaceSkinHeads.HeadOf(defs[d], lists.Source, null, out _, out _)));
            }
            // Phase 4.1: columns, current, preset
            string[] columns = CommandKit.OptStrings(c, "columns");
            bool wantCurrent = CommandKit.OptBool(c, "current") ?? false;
            string currentRef = null;
            if (wantCurrent)
            {
                if (memberPath == null) throw CommandKit.BadArgs("current", "current with key + path", "current marks the row of a member: give key and path");
                if (stored == null) throw new BridgeException(BridgeCodes.Unsupported, "character not imported (node " + node.Id + " has no stored documents)", new BridgeErrorDetail { Arg = "key" });
                try { currentRef = FieldAccessor.Get(c.Session.Model, node.Id, memberPath)?.ToString(); }
                catch (CommandException e) { throw FromCommand(e, "path"); }
            }
            bool heads = category == 210 || category == 110;
            int currentIndex = -1;
            if (wantCurrent && currentRef != null)
                for (int i = 0; i < shown.Count; i++) if (shown[i].row.Key.ToString() == currentRef) { currentIndex = i; break; }
            var outRows = new List<object>();
            for (int i = offset; i < shown.Count && outRows.Count < limit; i++)
            {
                var (row, h) = shown[i];
                var o = new BridgeReply().With("ref", row.Key.ToString()).With("name", MakerLists.NameOf(row, language))
                    .With("possess", row.Possess).With("gated", row.Gated);
                if (skins) o["headRef"] = h.IsEmpty ? null : h.ToString();
                if (heads) o["preset"] = HeadPresets.PresetName(row) != null;
                if (columns != null)
                {
                    var values = new BridgeReply();
                    foreach (var col in columns) values[col] = row[col];
                    o["values"] = values;
                }
                if (wantCurrent) o["current"] = i == currentIndex;
                outRows.Add(o);
            }
            var reply = new BridgeReply().With("total", shown.Count).With("category", category).With("sex", sex).With("rows", outRows);
            if (wantCurrent) { reply["currentRef"] = currentRef; reply["currentIndex"] = currentIndex; }
            return reply;
        }

        static bool Matches(ListRow r, string key)
        {
            foreach (var col in new[] { "Name", "EN_US", "ZH_CN" })
            {
                var v = r[col];
                if (!string.IsNullOrEmpty(v) && SearchText.Normalize(v).Contains(key)) return true;
            }
            return SearchText.Normalize(r.Key.ToString()).Contains(key);
        }

        // ------------------------------------------------------------ chara.edit.info

        static object Info(CoreContext c)
        {
            var n = CommandKit.Chara(c, "key");
            bool? session = CommandKit.OptBool(c, "session");
            if (session != null)
            {
                bool open = session.Value;
                // a session opens only on a built character: an unavailable reply must not leave a session open behind it
                if (open) CommandKit.Built(c, n, "key");
                var ops = CommandKit.Ops(c);
                CommandKit.Check(c, open ? ops.BeginEditSession(n.Id) : ops.EndEditSession(n.Id));
            }
            var real = CommandKit.Built(c, n, "key");   // applies the runtime (flush; the session reaches the editor)
            var ctx = real.Context;
            var ed = real.Editor;
            var dirty = new List<string>();
            if (ed != null) { dirty.AddRange(ed.Dirty); dirty.Sort(string.CompareOrdinal); }
            object last = null;
            var lf = ed?.LastFlush;
            if (lf != null)
                last = new BridgeReply().With("units", new List<string>(lf.Units)).With("creators", lf.Creators)
                    .With("creatorsRebuilt", new List<string>(lf.CreatorsRebuilt))
                    .With("ms", Math.Round(lf.Milliseconds, 4)).With("errors", new List<string>(lf.Errors)).With("build", lf.Build)
                    .With("mode", ModeName(lf.Mode)).With("deferred", new List<string>(lf.Deferred)).With("faceDrawChanged", lf.FaceDrawChanged)
                    .With("throttleWeight", lf.ThrottleWeight).With("dynBone", lf.DynBone);
            var deferred = ed != null ? new List<string>(ed.Deferred) : new List<string>();
            var creators = new List<object>();
            long total = 0;
            int decoded = 0;
            if (ctx?.Edit != null)
                foreach (var ci in ctx.Edit.Composites())
                {
                    if (ci?.Creator == null) continue;
                    var rt = ci.Creator.Target;
                    long bytes = RtBytes(rt);
                    total += bytes;
                    int layers = ci.Creator.Under.Count + ci.Creator.Over.Count;
                    foreach (var l in ci.Creator.Under) if (l?.Texture != null) decoded++;
                    foreach (var l in ci.Creator.Over) if (l?.Texture != null) decoded++;
                    creators.Add(new BridgeReply().With("name", ci.Name).With("width", rt ? rt.width : 0).With("height", rt ? rt.height : 0)
                        .With("size", new[] { rt ? rt.width : 0, rt ? rt.height : 0 })
                        .With("mips", rt && rt.useMipMap).With("bytes", bytes).With("rebuilds", ci.Rebuilds)
                        .With("lastRebuildMs", Math.Round(ci.LastRebuildMs, 4)).With("layers", layers));
                }
            var parts = new BridgeReply();
            var st = n.Appearance?.Stored;
            if (st != null)
            {
                parts["head"] = st.Chara.Face.Head;
                parts["skin"] = st.Chara.Face.Skin;
                parts["hair"] = new List<object> { st.Chara.Hair.Back?.Item, st.Chara.Hair.Front?.Item, st.Chara.Hair.Side?.Item, st.Chara.Hair.Option?.Item };
            }
            var clothes = new List<object>();
            for (int k = 0; k < 8; k++)
            {
                var e = ctx?.ClothesEntries != null && k < ctx.ClothesEntries.Length ? ctx.ClothesEntries[k] : null;
                clothes.Add(e != null ? e.Key.ToString() : null);
            }
            parts["clothes"] = clothes;
            var accs = new List<object>();
            if (ctx?.Accessories != null)
                foreach (var a in ctx.Accessories)
                {
                    if (a == null || a.IsEmpty) continue;
                    accs.Add(new BridgeReply().With("accessory", ctx.Edit?.Input?.Slots != null && ctx.Edit.Input.Slots.IdBySlot.TryGetValue(a.Slot, out var aid) ? aid : null)
                        .With("slot", a.Slot).With("ref", a.Entry != null ? a.Entry.Key.ToString() : null).With("loaded", (bool)a.Object));
                }
            parts["accessories"] = accs;
            // Phase 4.2: the C5 throttle and the breast normals
            var throttle = new BridgeReply().With("mode", CharaEditFlush.BodyThrottleName(CharaEditFlush.BodyThrottle))
                .With("enabled", CharaEditFlush.ThrottleBodyDuringDrags).With("weight", ed != null ? ed.PendingBodyWeight() : 0)
                .With("judged", real.LastPendingBodyWeight).With("throttled", real.EditThrottled).With("total", CharaEditFlush.Throttled);
            var bn = ctx?.BustNormals;
            bool female = ctx != null && !ctx.IsMale;
            var bustNormals = new BridgeReply().With("rate", female ? Num(BustNormalsRate(ctx)) : null)
                .With("blendRate", bn != null ? Num(bn.Rate) : null).With("applied", bn != null && bn.AnyApplied).With("meshCopied", bn != null && bn.MeshCopied);
            return new BridgeReply()
                .With("dirty", dirty).With("rebuildRequested", ed != null && ed.RebuildRequested)
                .With("deferred", deferred).With("reference", HasReference(c, n.Id))
                .With("inSession", ed != null && ed.InSession).With("flushes", ed?.Flushes ?? 0)
                .With("lastFlush", last)
                .With("creators", creators).With("creatorBytes", total)
                .With("ownedMaterials", ctx?.OwnedMaterials.Count ?? 0).With("decodedLayers", decoded)
                .With("parts", parts)
                .With("editFlushes", real.EditFlushes).With("editRebuilds", real.EditRebuilds).With("lastEditRebuild", real.LastEditRebuild)
                .With("throttle", throttle).With("bustNormals", bustNormals)
                .With("hair", HairInfoReply(ctx, ed))
                .With("clothes", ClothesInfoReply(ctx, ed))
                .With("accessories", AccessoryInfoReply(ctx, ed));
        }

        /// <summary>chara.edit.info's accessories block: per slot holding an accessory its id, item and
        /// facts (as chara.edit.accessories), and what the last flush did to the accessories.</summary>
        static BridgeReply AccessoryInfoReply(HS2Next.Chara.CharaContext ctx, CharaEditor ed)
        {
            var slots = new List<object>();
            if (ctx != null)
                foreach (int s in AccessoryReport.SlotNumbers(ctx))
                {
                    var p = AccessoryReport.SlotOf(ctx, s, false, false);
                    if (p == null) continue;
                    var go = ctx.AccessoryObject(s);
                    var cmp = go ? go.GetComponent<CmpAccessory>() : null;
                    var f = new BridgeReply().With("hasCmpAccessory", cmp != null).With("parentColumn", p.Facts.ParentColumn)
                        .With("headWeighted", p.Facts.HeadWeighted).With("fixed", p.Facts.Fixed);
                    if (cmp != null)
                    {
                        f["typeHair"] = p.Facts.TypeHair; f["useColor"] = new List<bool>(p.Facts.UseColor); f["alphaRenderers"] = p.Facts.AlphaRenderers;
                        f["move1"] = p.Facts.Move1; f["move2"] = p.Facts.Move2;
                    }
                    if (go)
                    {
                        f["dynamicBones"] = go.GetComponentsInChildren<global::DynamicBone>(true).Length;
                        f["colliders"] = go.GetComponentsInChildren<global::DynamicBoneCollider>(true).Length;   // as chara.edit.accessories (HS2's count)
                    }
                    slots.Add(new BridgeReply().With("slot", s).With("accessory", p.Id).With("category", p.Category).With("item", p.Item)
                        .With("loaded", p.Loaded).With("notLoaded", p.NotLoaded).With("facts", f));
                }
            var lf = ed?.LastFlush;
            object last = lf == null ? null : new BridgeReply()
                .With("accessorySlotsSwapped", lf.AccessorySlotsSwapped != null ? new List<int>(lf.AccessorySlotsSwapped) : new List<int>())
                .With("accessoryHairShaded", lf.AccessoryHairShaded != null ? new List<int>(lf.AccessoryHairShaded) : new List<int>());
            return new BridgeReply().With("slots", slots).With("lastFlush", last);
        }

        /// <summary>chara.edit.info's clothes block: the facts per kind (as chara.edit.clothes) and what the last flush did to
        /// the clothes (swapped kinds, kinds whose objects were reset without a swap, the compositing passes it rebuilt).</summary>
        static BridgeReply ClothesInfoReply(HS2Next.Chara.CharaContext ctx, CharaEditor ed)
        {
            var kinds = new List<object>();
            if (ctx != null)
                for (int k = 0; k < ClothesSet.Count; k++)
                {
                    var p = ClothesReport.KindOf(ctx, k);
                    if (p == null) continue;
                    var go = ClothesObject(ctx, k);
                    kinds.Add(new BridgeReply().With("kind", ClothesSet.Names[k]).With("index", k).With("item", p.Item).With("prefab", p.Prefab)
                        .With("realized", p.Realized).With("skipped", p.Skipped)
                        .With("facts", ClothesFacts(p, go ? go.GetComponent<CmpClothes>() : null)));
                }
            var lf = ed?.LastFlush;
            object last = null;
            if (lf != null)
            {
                var runs = new List<object>();
                foreach (var x in lf.ClothesCreatorsRebuilt)
                    runs.Add(new BridgeReply().With("kind", ClothesSet.Names[x.Kind]).With("group", x.Group).With("colour", x.Colour).With("detail", x.Detail));
                last = new BridgeReply().With("clothesSwapped", new List<bool>(lf.ClothesSwapped)).With("objectsReset", new List<bool>(lf.ClothesObjectsReset))
                    .With("creatorsRebuilt", runs);
            }
            return new BridgeReply().With("kinds", kinds).With("lastFlush", last);
        }

        /// <summary>chara.edit.info's hair block: the facts per part, the chain step of the last flush
        /// (FlushReport.DynBone) and whether a light re-derivation waits for the commit.</summary>
        static BridgeReply HairInfoReply(HS2Next.Chara.CharaContext ctx, CharaEditor ed)
        {
            var parts = new List<object>();
            var rep = HairReport.Of(ctx);
            if (rep != null)
                for (int k = 0; k < rep.Parts.Length; k++)
                {
                    var p = rep.Parts[k];
                    if (p == null) continue;
                    var go = HairObject(ctx, k);
                    parts.Add(new BridgeReply().With("part", k).With("name", p.Name).With("item", p.Item).With("prefab", p.Prefab)
                        .With("facts", HairFacts(p, go, go ? go.GetComponent<CmpHair>() : null)));
                }
            var lf = ed?.LastFlush;
            object last = lf == null ? null : new BridgeReply().With("dynBone", lf.DynBone).With("deferred", lf.Deferred.Contains(UnitMap.DynBone));
            bool pending = false;
            if (ed != null) foreach (var u in ed.Deferred) if (u == UnitMap.DynBone) { pending = true; break; }
            return new BridgeReply().With("parts", parts).With("lastFlush", last).With("deferred", pending);
        }

        /// <summary>HS2's breast-normal rate of a character's stored bust size (BustNormalsUnit.RateOf of body.shape[1]); NaN
        /// without documents.</summary>
        static float BustNormalsRate(HS2Next.Chara.CharaContext ctx)
        {
            var shape = ctx?.Chara?.Body?.Shape;
            return shape != null && shape.Length > 1 ? HS2Next.Chara.Edit.Units.BustNormalsUnit.RateOf(shape[1]) : float.NaN;
        }

        /// <summary>Bytes of a render texture: width × height × bytes per pixel, × 4/3 with mips.</summary>
        static long RtBytes(RenderTexture rt)
        {
            if (!rt) return 0;
            int bpp;
            switch (rt.format)
            {
                case RenderTextureFormat.ARGBHalf: bpp = 8; break;
                case RenderTextureFormat.ARGBFloat: bpp = 16; break;
                case RenderTextureFormat.R8: bpp = 1; break;
                case RenderTextureFormat.RHalf: case RenderTextureFormat.RG16: bpp = 2; break;
                default: bpp = 4; break;
            }
            long b = (long)rt.width * rt.height * bpp;
            return rt.useMipMap ? b * 4 / 3 : b;
        }

        static string ModeName(FlushMode m) => m == FlushMode.Preview ? "preview" : m == FlushMode.Build ? "build" : "normal";

        // ------------------------------------------------------------ chara.edit.materials / rtstats / digest (Phase 4.1)

        static object Materials(CoreContext c)
        {
            var n = CommandKit.Chara(c, "key");
            string part = CommandKit.OptString(c, "part", ArgTypes.Enum(MaterialParts)) ?? "face";
            if (Array.IndexOf(MaterialParts, part) < 0) throw CommandKit.BadArgs("part", ArgTypes.Enum(MaterialParts));
            bool hair = part == "hair", clothes = part == "clothes", accessories = part == "accessories";
            int accSlot = -1;
            if (!CommandKit.Absent(c, "slot"))
            {
                if (!accessories) throw CommandKit.BadArgs("slot", "slot with part \"accessories\"", "slot narrows part accessories to one build slot");
                accSlot = SlotArg(c, "slot");
            }
            int hairPart = -1;
            if (!CommandKit.Absent(c, "hairPart"))
            {
                if (!hair) throw CommandKit.BadArgs("hairPart", "hairPart with part \"hair\"", "hairPart narrows part hair to one hair part");
                hairPart = HairPartArg(c, "hairPart");
            }
            int kind = -1;
            if (!CommandKit.Absent(c, "kind"))
            {
                if (!clothes) throw CommandKit.BadArgs("kind", "kind with part \"clothes\"", "kind narrows part clothes to one clothes kind");
                kind = KindArg(c, "kind");
            }
            var given = CommandKit.OptStrings(c, "renderers");
            var po = CommandKit.OptStrings(c, "props");
            var only = po != null ? new HashSet<string>(po, StringComparer.Ordinal) : null;
            var real = CommandKit.Built(c, n, "key");
            var list = new List<object>();
            if (hair && given == null)
            {
                // Phase 4.3: every renderer under the hair part objects, part order, hierarchy order
                var ctx = real.Context;
                for (int k = 0; k < UnitMap.HairParts.Length; k++)
                {
                    if (hairPart >= 0 && k != hairPart) continue;
                    var go = HairObject(ctx, k);
                    if (!go) continue;
                    foreach (var rd in go.GetComponentsInChildren<Renderer>(true))
                        if (rd) list.Add(RendererOf(rd, only, true).With("hairPart", UnitMap.HairParts[k]));
                }
                return new BridgeReply().With("renderers", list);
            }
            if (clothes && given == null)
            {
                // every renderer under the clothes kind objects, kind order, hierarchy order
                var ctx = real.Context;
                for (int k = 0; k < ClothesSet.Count; k++)
                {
                    if (kind >= 0 && k != kind) continue;
                    var go = ClothesObject(ctx, k);
                    if (!go) continue;
                    foreach (var rd in go.GetComponentsInChildren<Renderer>(true))
                        if (rd) list.Add(RendererOf(rd, only, true).With("kind", ClothesSet.Names[k]));
                }
                return new BridgeReply().With("renderers", list);
            }
            if (accessories && given == null)
            {
                // Phase 4.5: every renderer under the accessory slot objects, build-slot order, hierarchy order
                var ctx = real.Context;
                if (ctx?.Accessories != null)
                    for (int s = 0; s < ctx.Accessories.Count; s++)
                    {
                        if (accSlot >= 0 && s != accSlot) continue;
                        var go = ctx.Accessories[s]?.Object;
                        if (!go) continue;
                        foreach (var rd in go.GetComponentsInChildren<Renderer>(true))
                            if (rd) list.Add(RendererOf(rd, only, true).With("slot", s));
                    }
                return new BridgeReply().With("renderers", list);
            }
            var names = new HashSet<string>(given ?? (part == "body" ? BodyRendererNames : FaceDigest.FaceRendererNames), StringComparer.Ordinal);
            var root = real.Root;
            if (root)
                foreach (var rd in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!rd || !names.Contains(rd.gameObject.name)) continue;
                    list.Add(RendererOf(rd, only, hair || clothes || accessories));
                }
            return new BridgeReply().With("renderers", list);
        }

        /// <summary>One renderer of chara.edit.materials: name, enabled, active, parent, slots.</summary>
        static BridgeReply RendererOf(Renderer rd, HashSet<string> only, bool hair)
        {
            Material[] mats;
            try { mats = rd.sharedMaterials; } catch (Exception) { mats = new Material[0]; }
            var slots = new List<object>(mats.Length);
            foreach (var m in mats) slots.Add(SlotOf(m, only, hair));
            return new BridgeReply().With("name", rd.gameObject.name).With("enabled", rd.enabled).With("active", rd.gameObject.activeInHierarchy)
                .With("parent", rd.transform.parent ? rd.transform.parent.name : null).With("slots", slots);
        }

        /// <summary>One material slot: material, shader, queue, sorted keywords, props (null material: material null).</summary>
        static BridgeReply SlotOf(Material m, HashSet<string> only, bool hair = false)
        {
            if (!m) return new BridgeReply().With("material", null);
            return new BridgeReply().With("material", m.name).With("shader", m.shader ? m.shader.name : null).With("queue", m.renderQueue)
                .With("keywords", Keywords(m)).With("props", PropsOf(m, only, hair));
        }

        static List<string> Keywords(Material m)
        {
            var kw = new List<string>(m.shaderKeywords);
            kw.Sort(StringComparer.Ordinal);
            return kw;
        }

        /// <summary>Every property the material's shader declares (or those of <paramref name="only"/>), by name: floats, ranges
        /// and ints as numbers, colours and vectors as [4] numbers (non-finite as null), textures as {name, w, h, format} or null.
        /// <paramref name="hair"/> (part hair, the HS2 side's chara.hairInfo props): a texture is followed by "&lt;name&gt;_ST"
        /// [scale x, y, offset x, y] and its object also gives wrapU, wrapV, mips.</summary>
        static BridgeReply PropsOf(Material m, HashSet<string> only, bool hair = false)
        {
            var props = new BridgeReply();
            var sh = m ? m.shader : null;
            if (!sh) return props;
            int count = sh.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                string name = sh.GetPropertyName(i);
                if (props.ContainsKey(name)) continue;
                if (only == null || only.Contains(name)) props[name] = PropValue(m, sh, i, hair);
                if (hair && sh.GetPropertyType(i) == ShaderPropertyType.Texture && (only == null || only.Contains(name + "_ST")) && !props.ContainsKey(name + "_ST"))
                    props[name + "_ST"] = TexST(m, name);
            }
            return props;
        }

        /// <summary>The value of shader property <paramref name="i"/> of <paramref name="m"/> in chara.edit.materials' forms.</summary>
        static object PropValue(Material m, Shader sh, int i, bool hair)
        {
            string name = sh.GetPropertyName(i);
            switch (sh.GetPropertyType(i))
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: return Num(m.GetFloat(name));
                case ShaderPropertyType.Int: return m.GetInteger(name);
                case ShaderPropertyType.Color: { var x = m.GetColor(name); return new[] { Num(x.r), Num(x.g), Num(x.b), Num(x.a) }; }
                case ShaderPropertyType.Vector: { var v = m.GetVector(name); return new[] { Num(v.x), Num(v.y), Num(v.z), Num(v.w) }; }
                case ShaderPropertyType.Texture:
                {
                    var t = m.GetTexture(name);
                    if (!t) return null;
                    var r = new BridgeReply().With("name", t.name).With("w", t.width).With("h", t.height).With("format", TexFormat(t));
                    if (hair) { r["wrapU"] = t.wrapModeU.ToString(); r["wrapV"] = t.wrapModeV.ToString(); r["mips"] = t.mipmapCount; }
                    return r;
                }
            }
            return null;
        }

        /// <summary>A texture property's scale and offset [scale x, y, offset x, y] (the material's "&lt;name&gt;_ST").</summary>
        static object[] TexST(Material m, string name)
        {
            var s = m.GetTextureScale(name);
            var o = m.GetTextureOffset(name);
            return new[] { Num(s.x), Num(s.y), Num(o.x), Num(o.y) };
        }

        static object Num(float f) => float.IsNaN(f) || float.IsInfinity(f) ? null : (object)(double)f;

        /// <summary>A texture's format as the HS2 side writes it: the Texture2D format, "RT:&lt;format&gt;" for render textures.</summary>
        static string TexFormat(Texture t) =>
            t is Texture2D t2 ? t2.format.ToString() : t is RenderTexture rt ? "RT:" + rt.format : t.GetType().Name;

        static object RtStatsCmd(CoreContext c)
        {
            var n = CommandKit.Chara(c, "key");
            string part = c.Args.Get("part", "face");
            if (part != "face" && part != "body" && part != "clothes") throw CommandKit.BadArgs("part", ArgTypes.Enum("face", "body", "clothes"));
            int kind = -1;
            if (!CommandKit.Absent(c, "kind"))
            {
                if (part != "clothes") throw CommandKit.BadArgs("kind", "kind with part \"clothes\"", "kind narrows part clothes to one clothes kind");
                kind = KindArg(c, "kind");
            }
            var gk = c.Args.Kind("grid");
            if (gk != ArgKind.Missing && gk != ArgKind.Integer) throw CommandKit.BadArgs("grid", ArgTypes.Int);
            int grid = c.Args.Get("grid", 8);
            if (grid < 1 || grid > MaxRtGrid) throw CommandKit.BadArgs("grid", "1.." + MaxRtGrid);
            var real = CommandKit.Built(c, n, "key");
            if (!FaceDigest.HasDevice)
                throw new BridgeException(BridgeCodes.Unavailable, "no graphics device: the render textures cannot be read back (-nographics)", new BridgeErrorDetail { Arg = "key" });
            var list = new List<object>();
            void Add(CompositeInputs ci, string kindName, int group)
            {
                var cr = ci.Creator;
                var rt = cr?.Target;
                var e = new BridgeReply().With("name", ci.Name != null && ci.Name.Contains("detail") ? "detail" : "colour").With("creator", ci.Name);
                if (kindName != null) { e["kind"] = kindName; e["group"] = group; }
                e["material"] = cr?.Mat ? cr.Mat.name : null;
                e["mainTex"] = cr?.Main ? cr.Main.name : null;
                if (!rt) { e["unavailable"] = "no render texture"; e["props"] = PropsOf(cr?.Mat, null); list.Add(e); return; }
                e["w"] = rt.width; e["h"] = rt.height; e["srgb"] = rt.sRGB; e["format"] = rt.format.ToString(); e["mips"] = rt.useMipMap;
                var px = ReadRgba32(rt, out string via);
                if (px == null || px.Length < rt.width * rt.height * 4) e["unavailable"] = "read-back failed";
                else RtStatsInto(e, px, rt.width, rt.height, grid);
                e["readback"] = via;
                e["props"] = PropsOf(cr.Mat, null);
                list.Add(e);
            }
            if (part == "clothes")
            {
                // per kind and colour group: the colour creator, then the detail creator
                var x = real.Context?.Edit;
                if (x != null)
                    for (int k = 0; k < ClothesSet.Count; k++)
                    {
                        if (kind >= 0 && k != kind) continue;
                        for (int j = 0; j < 3; j++)
                        {
                            if (x.ClothesColor[k, j] != null) Add(x.ClothesColor[k, j], ClothesSet.Names[k], j);
                            if (x.ClothesGloss[k, j] != null) Add(x.ClothesGloss[k, j], ClothesSet.Names[k], j);
                        }
                    }
            }
            else foreach (var ci in FaceDigest.CreatorsOf(real.Context, part)) Add(ci, null, -1);
            return new BridgeReply().With("part", part).With("grid", grid).With("creators", list);
        }

        /// <summary>Mip 0 of <paramref name="rt"/> as RGBA32 bytes, rows from the texture's bottom row (v = 0) up (Unity's texture
        /// memory order, as Texture2D.ReadPixels gives on the HS2 side): read in the RT's own 8-bit format (BGRA swizzled) so no
        /// colour conversion touches the stored values; other formats through a read-back conversion to RGBA32.</summary>
        static byte[] ReadRgba32(RenderTexture rt, out string via)
        {
            var gf = rt.graphicsFormat;
            bool rgba = gf == GraphicsFormat.R8G8B8A8_SRGB || gf == GraphicsFormat.R8G8B8A8_UNorm;
            bool bgra = gf == GraphicsFormat.B8G8R8A8_SRGB || gf == GraphicsFormat.B8G8R8A8_UNorm;
            via = (rgba || bgra ? "raw " : "converted ") + gf;
            var req = rgba || bgra ? AsyncGPUReadback.Request(rt, 0) : AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
            req.WaitForCompletion();
            if (req.hasError) return null;
            var data = req.GetData<byte>().ToArray();
            if (bgra)
                for (int i = 0; i + 3 < data.Length; i += 4) { byte t = data[i]; data[i] = data[i + 2]; data[i + 2] = t; }
            return data;
        }

        /// <summary>mean, std, cells (row-major, rows from the first = bottom row, 4 values per cell) and sha256 of RGBA32 bytes,
        /// values / 255 (the HS2 side's chara.rtStats arithmetic: integer sums, then divided).</summary>
        static void RtStatsInto(BridgeReply e, byte[] px, int w, int h, int grid)
        {
            grid = Math.Max(1, Math.Min(grid, Math.Min(w, h)));
            var sum = new double[4]; var sq = new double[4];
            var cells = new double[grid * grid * 4]; var count = new long[grid * grid];
            for (int y = 0; y < h; y++)
            {
                int gy = (int)((long)y * grid / h), row = y * w * 4;
                for (int x = 0; x < w; x++)
                {
                    int gi = gy * grid + (int)((long)x * grid / w), o = row + x * 4;
                    count[gi]++;
                    for (int ch = 0; ch < 4; ch++)
                    {
                        double v = px[o + ch];
                        sum[ch] += v; sq[ch] += v * v; cells[gi * 4 + ch] += v;
                    }
                }
            }
            double n = (double)w * h;
            var mean = new double[4]; var std = new double[4];
            for (int ch = 0; ch < 4; ch++)
            {
                double mu = sum[ch] / n;
                mean[ch] = mu / 255.0;
                std[ch] = Math.Sqrt(Math.Max(0, sq[ch] / n - mu * mu)) / 255.0;
            }
            var cl = new double[cells.Length];
            for (int i = 0; i < cells.Length; i++) cl[i] = count[i / 4] > 0 ? cells[i] / count[i / 4] / 255.0 : 0;
            e["mean"] = mean; e["std"] = std; e["cells"] = cl;
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(px, 0, w * h * 4);
                var sb = new StringBuilder(64);
                foreach (var b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                e["sha256"] = sb.ToString();
            }
        }

        static object Digest(CoreContext c)
        {
            var n = CommandKit.Chara(c, "key");
            string parts = c.Args.Get("parts", "face");
            if (!FaceDigest.IsPart(parts) || parts == null || Array.IndexOf(DigestParts, parts) < 0) throw CommandKit.BadArgs("parts", ArgTypes.Enum(DigestParts));
            bool text = CommandKit.OptBool(c, "text") ?? false;
            var real = CommandKit.Built(c, n, "key");
            var ctx = real.Context;
            var d = FaceDigest.Compute(ctx, parts, true);
            var r = new BridgeReply().With("parts", parts).With("materials", d.Materials).With("creators", d.Creators).With("bones", d.Bones);
            if (d.Rts != null) r["rts"] = d.Rts;
            if (d.Normals != null) r["normals"] = d.Normals;   // Phase 4.2: parts body / all
            if (d.Bust != null) r["bust"] = d.Bust;
            if (d.Chains != null) r["chains"] = d.Chains;   // Phase 4.3: parts hair / all
            if (d.Objects != null) r["objects"] = d.Objects;   // Phase 4.4: parts clothes / all
            if (text)
            {
                var t = new BridgeReply().With("materials", FaceDigest.MaterialsText(ctx, parts)).With("creators", FaceDigest.CreatorsText(ctx, parts))
                    .With("bones", FaceDigest.BonesText(ctx, parts));
                if (d.Normals != null) t["normals"] = FaceDigest.NormalsText(ctx, parts);
                if (d.Bust != null) t["bust"] = FaceDigest.BustText(ctx, parts);
                if (d.Chains != null) t["chains"] = FaceDigest.ChainsText(ctx, parts);
                if (d.Objects != null) t["objects"] = FaceDigest.ObjectsText(ctx, parts);
                r["texts"] = t;
            }
            return r;
        }

        // ------------------------------------------------------------ chara.edit.swatch / bust (Phase 4.2)

        static object Swatch(CoreContext c)
        {
            var nodes = CharaKeys(c);
            string full = FullPath(c.Args.Require<string>("path"), "path");
            if (c.Args.Kind("ref") != ArgKind.String) throw CommandKit.BadArgs("ref", "a swatch row ref \"<guid>#<id>\" of list 336");
            string reference = c.Args.Get<string>("ref");
            var res = CommandKit.Check(c, CommandKit.Ops(c).ApplyColourPreset(CommandKit.Ids(nodes), full, reference, CommandKit.Exec(c, null)), "ref");
            return EditReply(res);
        }

        static object Bust(CoreContext c)
        {
            var n = CommandKit.Chara(c, "key");
            var real = CommandKit.Built(c, n, "key");
            var ctx = real.Context;
            var rep = BustReport.Of(ctx);
            if (rep == null) throw new BridgeException(BridgeCodes.Unavailable, "character " + n.Id + " has no build context", new BridgeErrorDetail { Arg = "key" });
            bool male = ctx.IsMale;
            var stage = ctx.Motion != null ? ctx.Motion.Get<HS2Next.Motion.DynamicBones.BustStage>() : null;
            var ph = rep.Physics;
            // the breast chains (kinds 0 = left, 1 = right), as the HS2 side's chara.bustPhysics; inert per particle of pattern 0
            var chains = new List<object>();
            for (int kind = 0; kind < 2; kind++)
            {
                foreach (var ch in ph.Chains)
                {
                    if (ch.Kind != kind) continue;
                    var inert = new List<object>();
                    if (ch.Inerts != null) foreach (var x in ch.Inerts) inert.Add(Num(x));
                    chains.Add(new BridgeReply().With("name", ch.Name).With("kind", ch.Kind).With("pattern", ch.Pattern).With("active", ch.Active)
                        .With("particles", ch.Particles).With("damping", Num(ch.Damping)).With("elasticity", Num(ch.Elasticity)).With("stiffness", Num(ch.Stiffness))
                        .With("inert", inert).With("gravity", new[] { Num(ch.Gravity.x), Num(ch.Gravity.y), Num(ch.Gravity.z) }));
                    break;
                }
            }
            var physics = new BridgeReply().With("enabled", ph.Enabled).With("stageEnabled", stage != null && stage.Enabled);
            if (male) physics["maleBustPhysics"] = stage != null && stage.Options.MaleBustPhysics;
            physics["softness"] = Num(ph.StageSoftness); physics["weight"] = Num(ph.StageWeight); physics["size"] = Num(ph.StageSize);
            physics["chains"] = chains;
            var nr = rep.Normals;
            object source = null;
            if (nr.Source != null)
            {
                // "vanilla", or "<hook id>:<id>" for a body mesh a build hook replaced
                int colon = nr.Source == "vanilla" ? -1 : nr.Source.IndexOf(':');
                bool replaced = nr.Source != "vanilla";
                source = new BridgeReply().With("kind", replaced ? "replaced" : nr.Source)
                    .With("hook", replaced ? (colon >= 0 ? nr.Source.Substring(0, colon) : nr.Source) : null)
                    .With("guid", replaced && colon >= 0 ? nr.Source.Substring(colon + 1) : null)
                    .With("asset", nr.Asset).With("bundle", nr.Bundle);
            }
            var entries = new List<object>();
            foreach (var e in nr.Entries)
                entries.Add(new BridgeReply().With("object", e.Object).With("vertices", e.Vertices).With("min", e.Min).With("max", e.Max)
                    .With("applicable", e.Applicable).With("applied", e.Applied));
            var normals = new BridgeReply().With("source", source).With("missing", nr.Missing).With("missingReason", nr.MissingReason)
                .With("entries", entries).With("rate", male ? null : Num(BustNormalsRate(ctx))).With("blendRate", Num(nr.Rate))
                .With("applied", nr.Applied).With("meshCopied", nr.MeshCopied).With("sha256", nr.Sha256).With("mesh", nr.Mesh)
                .With("blends", nr.Blends).With("copies", nr.Copies).With("lastBlendMs", NumD(nr.LastBlendMs)).With("lastCopyMs", NumD(nr.LastCopyMs))
                .With("copyBytes", nr.CopyBytes);
            return new BridgeReply().With("sex", male ? 0 : 1).With("size", Num(rep.Size)).With("softness", Num(rep.Softness)).With("weight", Num(rep.Weight))
                .With("physics", physics).With("normals", normals);
        }

        static object NumD(double d) => double.IsNaN(d) || double.IsInfinity(d) ? null : (object)Math.Round(d, 4);

        // ------------------------------------------------------------ chara.edit.hairBundle / hairSkin / hair (Phase 4.3)

        /// <summary>A hair part argument: "back" / "front" / "side" / "option" (MakerEdits.HairPartIndex) or 0..3; bad_args
        /// otherwise (missing: bad_args "missing argument").</summary>
        static int HairPartArg(CoreContext c, string arg) => CommandKit.NameOrIndex(c, arg, HairData.PartNames, HairPartExpected, "a hair part", "part");

        /// <summary>The [x, y, z] rates of a hair bundle edit (null when absent).</summary>
        static Vec3? RatesArg(CoreContext c, string arg) => CommandKit.OptVec3(c, arg, "[x, y, z] rates");

        static object HairBundle(CoreContext c)
        {
            var nodes = CharaKeys(c);
            int part = HairPartArg(c, "part");
            var move = RatesArg(c, "move");
            var rotate = RatesArg(c, "rotate");
            bool? noShake = CommandKit.OptBool(c, "noShake");
            bool reset = CommandKit.OptBool(c, "reset") ?? false;
            bool all = CommandKit.OptBool(c, "all") ?? false;
            int? axis = CommandKit.OptInt(c, "axis");
            int? index = CommandKit.OptInt(c, "index");
            var ops = CommandKit.Ops(c);
            OpResult res;
            if (all)
            {
                if (!reset) throw CommandKit.BadArgs("all", "all: true with reset: true", "all names every bundle of the part for «全て初期化»: give reset: true");
                if (index != null) throw CommandKit.BadArgs("index", "index or all", "give index or all, not both");
                string extra = move != null ? "move" : rotate != null ? "rotate" : noShake != null ? "noShake" : axis != null ? "axis" : null;
                if (extra != null) throw CommandKit.BadArgs(extra, "nothing beside all + reset", "«全て初期化» resets every bundle's rates and keeps noShake");
                res = CommandKit.Check(c, ops.ResetHairBundles(CommandKit.Ids(nodes), part, CommandKit.Exec(c, null)), "part");
            }
            else
            {
                if (index == null) throw new BridgeException(BridgeError.Missing("index", ArgTypes.Int + " (or all: true with reset: true)"));
                if (axis != null && !reset) throw CommandKit.BadArgs("axis", "axis with reset: true", "axis narrows a reset to one component");
                if (axis != null && (axis < 0 || axis > 2)) throw CommandKit.BadArgs("axis", "a component 0..2", "axis " + axis + " outside 0..2");
                if (!reset && move == null && rotate == null && noShake == null) throw CommandKit.BadArgs("move", "move, rotate, noShake or reset", "nothing to set");
                res = CommandKit.Check(c, ops.EditHairBundle(CommandKit.Ids(nodes), part, index.Value, move, rotate, noShake, reset, axis ?? -1, CommandKit.Exec(c, null)), "index");
            }
            return EditReply(res);
        }

        static object HairSkin(CoreContext c)
        {
            var nodes = CharaKeys(c);
            int part = HairPartArg(c, "part");
            var res = CommandKit.Check(c, CommandKit.Ops(c).MatchHairToSkin(CommandKit.Ids(nodes), part, CommandKit.Exec(c, null)), "part");
            return EditReply(res);
        }

        static object Hair(CoreContext c)
        {
            var n = CommandKit.Chara(c, "key");
            int only = -1;
            if (!CommandKit.Absent(c, "part")) only = HairPartArg(c, "part");
            var real = CommandKit.Built(c, n, "key");
            var ctx = real.Context;
            var rep = HairReport.Of(ctx);
            if (rep == null) throw new BridgeException(BridgeCodes.Unavailable, "character " + n.Id + " has no build context", new BridgeErrorDetail { Arg = "key" });
            var parts = new List<object>();
            for (int k = 0; k < rep.Parts.Length; k++)
                if ((only < 0 || k == only) && rep.Parts[k] != null) parts.Add(HairPartReply(ctx, rep.Parts[k]));
            return new BridgeReply().With("parts", parts).With("shaderType", rep.ShaderType).With("meshOnEveryHair", HairUnit.MeshOnEveryHair);
        }

        /// <summary>The realized object of hair part <paramref name="k"/> (null: none).</summary>
        static GameObject HairObject(HS2Next.Chara.CharaContext ctx, int k) => ctx?.Hair != null && k >= 0 && k < ctx.Hair.Length ? ctx.Hair[k] : null;

        /// <summary>The list row hair part <paramref name="k"/>'s unit built (HairUnit.Row; null before it ran).</summary>
        static ListEntry HairRow(HS2Next.Chara.CharaContext ctx, int k) => (ctx?.Editor?.Unit(UnitMap.HairUnits[k]) as HairUnit)?.Row;

        static object IntColumn(ListEntry row, string column) =>
            row != null && int.TryParse(row[column], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? (object)v : null;

        static List<object> D3(Vector3 v) => new List<object> { (double)v.x, (double)v.y, (double)v.z };
        static List<object> D4(Quaternion q) => new List<object> { (double)q.x, (double)q.y, (double)q.z, (double)q.w };
        static object[] Col4(Color x) => new[] { Num(x.r), Num(x.g), Num(x.b), Num(x.a) };

        /// <summary>The CmpHair facts of a part in the HS2 side's names (chara.hairInfo facts), then HS2Next's additions.</summary>
        static BridgeReply HairFacts(HairReport.PartReport p, GameObject go, CmpHair cmp)
        {
            if (!p.HasCmpHair) return new BridgeReply().With("hasCmpHair", false);   // as the HS2 side (none rows, failed loads)
            int live = cmp != null ? HairUnit.HairRenderers(go, cmp).Count : 0;
            var acs = new List<object>();
            foreach (var x in p.AcsDefColor) acs.Add(Col4(x));
            int shown = 0;
            foreach (var b in p.Bundles) if (b.Shown) shown++;
            return new BridgeReply().With("hasCmpHair", p.HasCmpHair)
                .With("useTopColor", p.UseTopColor).With("useUnderColor", p.UseUnderColor).With("useSameSkinColorButton", p.UseSameSkinColorButton)
                // HS2's instance flag: the prefab's, set by HairMeshforAll on every part with a rendHair (HairUnit.MeshOnEveryHair)
                .With("useMesh", p.HasCmpHair && (p.UseMesh || (HairUnit.MeshOnEveryHair && live > 0)))
                .With("useAcsColor01", p.UseAcsColor01).With("useAcsColor02", p.UseAcsColor02).With("useAcsColor03", p.UseAcsColor03)
                .With("acsDefColor", acs)
                .With("rendHair", live).With("rendHairLength", cmp?.rendHair?.Count ?? 0).With("rendAccessory", cmp?.rendAccessory?.Count ?? 0)
                .With("bundles", p.Bundles.Count)
                .With("prefabUseMesh", p.UseMesh).With("meshWritten", p.Mesh.Written).With("rendHairEmpty", p.RendHairEmpty)
                .With("acsDefColorCount", p.AcsDefColorCount).With("shownBundles", shown);
        }

        /// <summary>HairReport's chain relation in the census' / HS2 side's words.</summary>
        static string RelationText(string relation)
        {
            switch (relation)
            {
                case HairReport.RelationRoot: return "root = trfCorrect";
                case HairReport.RelationInside: return "trfCorrect inside the chain";
                case HairReport.RelationAbove: return "trfCorrect above the root";
                default: return relation;
            }
        }

        /// <summary>A chain of chara.edit.hair in the HS2 side's members: `enabled` = the chain's own state (its Studio switch on
        /// and not noShake: HS2's DynamicBone.enabled as Studio's FK hair re-application sets it), `active` = it simulates now
        /// (also needs the dynamic-bone stage / physics on); then HS2Next's additions.</summary>
        static BridgeReply ChainReply(HairReport.Chain ch) =>
            new BridgeReply().With("name", ch.Owner).With("root", ch.Root).With("enabled", ch.Enabled && !ch.NoShake).With("active", ch.Active)
                .With("relation", RelationText(ch.Relation)).With("particles", ch.Particles).With("noShake", ch.NoShake).With("userEnabled", ch.Enabled).With("bundle", ch.Bundle)
                .With("rest", new BridgeReply().With("position", D3(ch.RestRootPosition)).With("rotation", D4(ch.RestRootRotation)));

        /// <summary>One part of chara.edit.hair: the HS2 side's chara.hairInfo members in its order, then HS2Next's additions.</summary>
        static BridgeReply HairPartReply(HS2Next.Chara.CharaContext ctx, HairReport.PartReport p)
        {
            var go = HairObject(ctx, p.Part);
            var cmp = go ? go.GetComponent<CmpHair>() : null;
            var row = HairRow(ctx, p.Part);
            // bundles
            var bundles = new List<object>();
            foreach (var e in p.Bundles)
            {
                var bi = cmp?.boneInfo != null && e.Index < cmp.boneInfo.Count ? cmp.boneInfo[e.Index] : null;
                var trf = bi?.trfCorrect;
                var chains = new List<object>();
                foreach (var ch in e.Chains) chains.Add(ChainReply(ch));
                bundles.Add(new BridgeReply().With("index", e.Index).With("shown", e.Shown).With("title", e.Title).With("trfCorrect", e.Transform)
                    .With("parent", trf && trf.parent ? trf.parent.name : null)
                    .With("limits", new BridgeReply().With("posMin", D3(e.PosMin)).With("posMax", D3(e.PosMax)).With("rotMin", D3(e.RotMin)).With("rotMax", D3(e.RotMax)))
                    .With("base", new BridgeReply().With("pos", D3(e.BasePos)).With("rot", D3(e.BaseRot)))
                    .With("default", new BridgeReply().With("move", D3(e.DefaultMove)).With("rotate", D3(e.DefaultRotate)))
                    .With("stored", e.HasStored ? new BridgeReply().With("move", D3(e.StoredMove)).With("rotate", D3(e.StoredRotate)).With("noShake", e.StoredNoShake) : null)
                    .With("effective", new BridgeReply().With("move", D3(e.EffectiveMove)).With("rotate", D3(e.EffectiveRotate)).With("noShake", e.EffectiveNoShake))
                    .With("local", trf ? new BridgeReply().With("position", D3(e.LocalPosition)).With("euler", D3(e.LocalEuler)).With("rotation", D4(e.LocalRotation)) : null)
                    .With("world", trf ? new BridgeReply().With("position", D3(trf.position)).With("rotation", D4(trf.rotation)) : null)
                    .With("chains", chains));
            }
            var others = new List<object>();
            foreach (var ch in p.OtherChains) others.Add(ChainReply(ch));
            // materials: every slot of every rendHair renderer (HairReport's order), the hair properties of the HS2 side
            var mats = new List<object>();
            var known = new HashSet<Renderer>();
            if (cmp != null)
                foreach (var rd in HairUnit.HairRenderers(go, cmp))
                {
                    known.Add(rd);
                    Material[] ms;
                    try { ms = rd.sharedMaterials; } catch (Exception) { ms = new Material[0]; }
                    for (int i = 0; i < ms.Length; i++)
                    {
                        var m = ms[i];
                        var e = new BridgeReply().With("renderer", rd.name).With("slot", i).With("material", m ? m.name : null)
                            .With("shader", m && m.shader ? m.shader.name : null).With("renderQueue", m ? m.renderQueue : 0);
                        var mt = m && m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                        e["mainTex"] = mt ? mt.name : null;
                        e["props"] = m ? HairProps(m) : new BridgeReply();
                        e["keywords"] = m ? Keywords(m) : new List<string>();
                        mats.Add(e);
                    }
                }
            var deco = new List<object>();
            foreach (var d in p.Decoration)
            {
                var cols = new List<object>(3);
                foreach (var x in d.Colors) cols.Add(x.HasValue ? Col4(x.Value) : null);
                deco.Add(new BridgeReply().With("renderer", d.Renderer).With("slot", d.Slot).With("material", d.Material).With("colors", cols).With("shader", d.Shader));
            }
            if (cmp?.rendAccessory != null) foreach (var rd in cmp.rendAccessory) if (rd) known.Add(rd);
            var otherRenderers = new List<object>();
            if (cmp != null)
                foreach (var rd in go.GetComponentsInChildren<Renderer>(true))
                    if (rd && !known.Contains(rd)) otherRenderers.Add(rd.name);
            var ms0 = p.Mesh;
            var mesh = new BridgeReply().With("item", ms0.Item).With("texture", ms0.Texture)
                .With("scale", new[] { Num(ms0.Scale.x), Num(ms0.Scale.y) }).With("offset", new[] { Num(ms0.Offset.x), Num(ms0.Offset.y) })
                .With("color", Col4(ms0.MeshColor)).With("written", ms0.Written).With("format", ms0.TextureFormat).With("w", ms0.TextureWidth).With("h", ms0.TextureHeight)
                .With("onOff", Num(ms0.MeshOnOff)).With("uniform", ms0.Uniform)
                .With("storedColor", Col4(ms0.Color)).With("layout", new[] { Num(ms0.Layout.x), Num(ms0.Layout.y), Num(ms0.Layout.z), Num(ms0.Layout.w) });
            return new BridgeReply().With("part", p.Part).With("item", p.Item).With("object", go ? go.name : null)
                .With("name", row?["Name"]).With("mainAB", row?["MainAB"]).With("mainData", row?["MainData"]).With("mainData02", row?["MainData02"])
                .With("ringOff", IntColumn(row, "RingOff")).With("texAB", row?["TexAB"]).With("texD", row?["TexD"]).With("texC", row?["TexC"])
                .With("facts", HairFacts(p, go, cmp)).With("stored", p.StoredCount).With("bundles", bundles).With("otherChains", others)
                .With("materials", mats).With("decoration", deco).With("otherRenderers", otherRenderers)
                // HS2Next additions
                .With("partName", p.Name).With("row", p.Row).With("prefab", p.Prefab).With("realized", p.Realized).With("storedValid", p.StoredValid)
                .With("mesh", mesh);
        }

        /// <summary>The hair properties of a material slot (<see cref="HairPropNames"/> its shader declares; a texture followed by
        /// "&lt;name&gt;_ST"), in chara.edit.materials' forms with part hair.</summary>
        static BridgeReply HairProps(Material m)
        {
            var props = new BridgeReply();
            var sh = m.shader;
            if (!sh) return props;
            foreach (var name in HairPropNames)
            {
                int i = sh.FindPropertyIndex(name);
                if (i < 0) continue;
                props[name] = PropValue(m, sh, i, true);
                if (sh.GetPropertyType(i) == ShaderPropertyType.Texture) props[name + "_ST"] = TexST(m, name);
            }
            return props;
        }

        // ------------------------------------------------------------ chara.edit.clothes / clothesDefaults / coordinate (Phase 4.4)

        /// <summary>A clothes kind argument: a name of the coordinate format ("top" … "shoes") or 0..7; bad_args otherwise
        /// (missing: bad_args "missing argument").</summary>
        static int KindArg(CoreContext c, string arg) => CommandKit.NameOrIndex(c, arg, ClothesSet.Names, KindExpected, "a clothes kind", "kind");

        /// <summary>The realized object of clothes kind <paramref name="k"/> (null: none).</summary>
        static GameObject ClothesObject(HS2Next.Chara.CharaContext ctx, int k) => ctx?.Clothes != null && k >= 0 && k < ctx.Clothes.Length ? ctx.Clothes[k] : null;

        /// <summary>The list row clothes kind <paramref name="k"/>'s part unit built (null before it ran).</summary>
        static ListEntry ClothesRow(HS2Next.Chara.CharaContext ctx, int k) => (ctx?.Editor?.Unit(UnitMap.ClothesUnits[k]) as ClothesPartUnit)?.Row;

        /// <summary>A row's Coordinate column marks a one-piece top / inner top (anything but empty or "0", as the Maker's kind
        /// gate reads it).</summary>
        static bool OnePieceColumn(ListEntry row)
        {
            string v = row?["Coordinate"];
            return !string.IsNullOrEmpty(v) && v != "0";
        }

        static object[] Vec4Of(Vector4 v) => new[] { Num(v.x), Num(v.y), Num(v.z), Num(v.w) };

        static object Clothes(CoreContext c)
        {
            var n = CommandKit.Chara(c, "key");
            int only = (CommandKit.Opt(c, "kind", KindArg) ?? -1);
            bool served = CommandKit.OptBool(c, "served") ?? true;
            var real = CommandKit.Built(c, n, "key");
            var ctx = real.Context;
            if (ctx == null) throw new BridgeException(BridgeCodes.Unavailable, "character " + n.Id + " has no build context", new BridgeErrorDetail { Arg = "key" });
            var st = ((c.Session.Model.Find(n.Id) ?? n).State as CharaNodeState)?.State;
            var rules = real.ClothesStates;
            var kinds = new List<object>();
            for (int k = 0; k < ClothesSet.Count; k++)
                if (only < 0 || k == only) kinds.Add(ClothesKindReply(ctx, ClothesReport.KindOf(ctx, k, served), st, rules, served));
            // the masks: flags, values on the reference renderers, the renderers themselves
            var x = ctx.Edit;
            int lower = x?.MaskLowerType ?? -1;
            object breakDisable = null;
            if (lower == 0 || lower == 1) breakDisable = ClothesRow(ctx, lower)?["BreakDisableMask"] == "1";
            var flags = new BridgeReply().With("notBot", rules.NotBot).With("notInnerT", rules.NotInnerT).With("notInnerB", rules.NotInnerB)
                .With("underMaskReflectionType", lower).With("underMaskBreakDisable", breakDisable)
                .With("lowerOk0", x != null && x.MaskLowerOk0).With("lowerOk1", x != null && x.MaskLowerOk1);
            var masks = new BridgeReply();
            var targets = real.Assembler?.ClothesMaskTargets();
            string[] maskNames = { "body", "bra", "innerTB", "innerB", "panst" };
            for (int t = 0; t < maskNames.Length; t++)
            {
                var mats = targets != null && t < targets.Count ? targets[t].mats : null;
                masks[maskNames[t]] = mats != null && mats.Length > 0 ? MaskReply(mats) : null;
            }
            var maskRenderers = new BridgeReply();
            string[] rendererNames = { "bra", "innerTB", "innerB", "panst" };
            for (int i = 0; i < rendererNames.Length; i++)
            {
                var rd = x?.MaskRenderers != null && i < x.MaskRenderers.Length ? x.MaskRenderers[i] : null;
                maskRenderers[rendererNames[i]] = rd ? rd.name : null;
            }
            var subMenu = new BridgeReply().With("bottom", !OnePieceColumn(ClothesRow(ctx, 0))).With("innerBottom", !OnePieceColumn(ClothesRow(ctx, 2)));
            return new BridgeReply().With("kinds", kinds).With("flags", flags).With("masks", masks).With("maskRenderers", maskRenderers)
                .With("subMenu", subMenu).With("male", ctx.IsMale);
        }

        /// <summary>One mask target of chara.edit.clothes: the first material's name and its mask values (HS2's member set).</summary>
        static BridgeReply MaskReply(Material[] mats)
        {
            var r = new BridgeReply().With("material", mats[0] ? mats[0].name : null);
            foreach (var p in new[] { "_alpha_a", "_alpha_b", "_alpha_c", "_alpha_d" })
            {
                object v = null;
                foreach (var m in mats) if (m && m.HasProperty(p)) { v = Num(m.GetFloat(p)); break; }
                r[p] = v;
            }
            foreach (var p in new[] { "_AlphaMask", "_AlphaMask2" })
            {
                object v = null;
                foreach (var m in mats)
                    if (m && m.HasProperty(p)) { var t = m.GetTexture(p); v = t ? t.name : null; break; }
                r[p] = v;
            }
            return r;
        }

        /// <summary>The facts of a kind's prefab in the HS2 side's names ({hasCmpClothes: false} only without a CmpClothes).</summary>
        static BridgeReply ClothesFacts(ClothesReport.KindReport p, CmpClothes cmp)
        {
            if (p == null || !p.HasCmpClothes || cmp == null) return new BridgeReply().With("hasCmpClothes", false);
            var lists = new[] { cmp.rendNormal01, cmp.rendNormal02, cmp.rendNormal03 };
            var groups = new List<object>(3);
            for (int j = 0; j < 3; j++)
            {
                var g = p.Groups[j];
                var names = new List<object>();
                if (lists[j] != null) foreach (var rd in lists[j]) if (rd) names.Add(rd.name);
                groups.Add(new BridgeReply().With("renderers", g.Renderers).With("length", lists[j]?.Count ?? 0).With("slots", g.Slots).With("names", names)
                    .With("mainTex", g.MainWidth > 0 ? new[] { g.MainWidth, g.MainHeight } : null).With("hasCreators", g.HasCreators));
            }
            return new BridgeReply().With("hasCmpClothes", true)
                .With("useColorN", new List<bool>(p.UseColorN)).With("useColorA", new List<bool>(p.UseColorA)).With("useBreak", p.UseBreak)
                .With("objOpt01", p.Opt01).With("objOpt02", p.Opt02)
                .With("objTopDef", p.HasTopDef).With("objTopHalf", p.HasTopHalf).With("objBotDef", p.HasBotDef).With("objBotHalf", p.HasBotHalf)
                .With("uvScalePattern", Vec4Of(p.UvScalePattern)).With("defMainColor04", Col4(p.DefMainColor04))
                .With("defGloss04", Num(p.DefGloss04)).With("defMetallic04", Num(p.DefMetallic04))
                .With("defLayout", new List<object> { Vec4Of(cmp.defLayout01), Vec4Of(cmp.defLayout02), Vec4Of(cmp.defLayout03) })
                .With("defRotation", new List<object> { Num(cmp.defRotation01), Num(cmp.defRotation02), Num(cmp.defRotation03) })
                .With("groups", groups);
        }

        /// <summary>One kind of chara.edit.clothes: the HS2 side's chara.clothesInfo members in its order, then HS2Next's.</summary>
        static BridgeReply ClothesKindReply(HS2Next.Chara.CharaContext ctx, ClothesReport.KindReport p, HS2Next.Save.CharaState st, ClothesStateSet rules, bool served)
        {
            int k = p.Kind;
            var go = ClothesObject(ctx, k);
            var cmp = go ? go.GetComponent<CmpClothes>() : null;
            var row = ClothesRow(ctx, k);
            object rowObj = null;
            if (row != null)
            {
                var ro = new BridgeReply().With("id", row.Key.Id);
                foreach (var col in ClothesRowColumns) ro[col] = row[col];
                rowObj = ro;
            }
            // the compositing passes: per colour group the colour creator, then the detail creator
            var x = ctx.Edit;
            var creators = new List<object>();
            var creatorOf = new Dictionary<Texture, string>();
            if (x != null)
                for (int j = 0; j < 3; j++)
                    foreach (var (ci, which) in new[] { (x.ClothesColor[k, j], "colour"), (x.ClothesGloss[k, j], "detail") })
                    {
                        if (ci?.Creator == null) continue;
                        var cr = ci.Creator;
                        var rt = cr.Target;
                        if (rt) creatorOf[rt] = which + " " + j;
                        int draw = 0;
                        foreach (var m in ci.Draw) if (m) draw++;
                        creators.Add(new BridgeReply().With("group", j).With("creator", which).With("material", cr.Mat ? cr.Mat.name : null)
                            .With("mainTex", cr.Main ? cr.Main.name : null).With("rt", rt ? new[] { rt.width, rt.height } : null)
                            .With("props", PropsOf(cr.Mat, null)).With("name", ci.Name).With("rebuilds", ci.Rebuilds).With("draw", draw));
                    }
            // the defaults as document values
            List<object> defaults = null;
            if (p.Defaults != null)
            {
                defaults = new List<object>(3);
                foreach (var d in p.Defaults)
                    defaults.Add(d == null ? null : new BridgeReply().With("base", CommandKit.C(d.Base)).With("pattern", d.Pattern).With("patternColor", CommandKit.C(d.PatternColor))
                        .With("layout", new[] { Num(d.Layout.X), Num(d.Layout.Y), Num(d.Layout.Z), Num(d.Layout.W) }).With("rotation", Num(d.Rotation))
                        .With("gloss", Num(d.Gloss)).With("metallic", Num(d.Metallic)));
            }
            var ob = p.Objects;
            var objects = new BridgeReply().With("topDef", ob.TopDef).With("topHalf", ob.TopHalf).With("botDef", ob.BotDef).With("botHalf", ob.BotHalf)
                .With("opt1", new List<bool>(ob.Opt01)).With("opt2", new List<bool>(ob.Opt02));
            // every slot of every renderer under the part object
            var materials = new List<object>();
            if (go)
            {
                var lists = cmp != null ? new[] { cmp.rendNormal01, cmp.rendNormal02, cmp.rendNormal03 } : null;
                foreach (var rd in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (!rd) continue;
                    int group = 0;
                    if (lists != null) for (int j = 0; j < 3 && group == 0; j++) if (lists[j] != null && lists[j].Contains(rd)) group = j + 1;
                    Material[] ms;
                    try { ms = rd.sharedMaterials; } catch (Exception) { ms = new Material[0]; }
                    for (int i = 0; i < ms.Length; i++)
                        materials.Add(ClothesSlotReply(rd, group, i, ms.Length, ms[i], creatorOf));
                }
            }
            int[] state = st?.Clothes;
            var e = new BridgeReply().With("kind", ClothesSet.Names[k]).With("index", k).With("category", p.Category).With("item", p.Item)
                .With("realized", p.Realized).With("object", go ? go.name : null).With("active", go ? (object)go.activeSelf : null)
                .With("activeInHierarchy", go ? (object)go.activeInHierarchy : null).With("skipped", p.Skipped)
                .With("state", state != null && k < state.Length ? (object)state[k] : null).With("stateKinds", new List<int>(rules.Supported(k)))
                .With("row", rowObj).With("facts", ClothesFacts(p, cmp)).With("creators", creators).With("defaults", defaults)
                .With("objects", objects).With("materials", materials)
                // HS2Next additions
                .With("rowRef", p.Row).With("prefab", p.Prefab).With("onePiece", OnePieceColumn(row));
            if (served) e["served"] = new BridgeReply().With("MainData", new BridgeReply().With("kind", p.ServedKind).With("guid", p.ServedGuid));
            return e;
        }

        /// <summary>One material slot of chara.edit.clothes: the HS2 side's members, the clothes property set with textures naming
        /// the creator whose RT they are, then the flat mainTex / detailMainTex / alphaEx.</summary>
        static BridgeReply ClothesSlotReply(Renderer rd, int group, int slot, int slots, Material m, Dictionary<Texture, string> creatorOf)
        {
            var e = new BridgeReply().With("renderer", rd.name).With("group", group).With("slot", slot).With("slots", slots)
                .With("enabled", rd.enabled).With("active", rd.gameObject.activeInHierarchy).With("material", m ? m.name : null)
                .With("shader", m && m.shader ? m.shader.name : null).With("queue", m ? m.renderQueue : 0).With("keywords", m ? Keywords(m) : new List<string>());
            var props = new BridgeReply();
            var sh = m ? m.shader : null;
            if (sh)
                foreach (var name in ClothesMaterialPropNames)
                {
                    int i = sh.FindPropertyIndex(name);
                    if (i < 0) continue;
                    var v = PropValue(m, sh, i, false);
                    if (v is BridgeReply tex && sh.GetPropertyType(i) == ShaderPropertyType.Texture)
                    {
                        var t = m.GetTexture(name);
                        tex["creator"] = t && creatorOf.TryGetValue(t, out var cn) ? cn : null;
                    }
                    props[name] = v;
                }
            e["props"] = props;
            e["mainTex"] = TexName(m, "_MainTex");
            e["detailMainTex"] = TexName(m, "_DetailMainTex");
            e["alphaEx"] = m && m.HasProperty("_AlphaEx") ? Num(m.GetFloat("_AlphaEx")) : null;
            return e;
        }

        static string TexName(Material m, string prop)
        {
            if (!m || !m.HasProperty(prop)) return null;
            var t = m.GetTexture(prop);
            return t ? t.name : null;
        }

        static object ClothesDefaults(CoreContext c)
        {
            var nodes = CharaKeys(c);
            int kind = KindArg(c, "kind");
            var res = CommandKit.Check(c, CommandKit.Ops(c).ResetClothesColours(CommandKit.Ids(nodes), kind, CommandKit.Exec(c, null)), "kind");
            return EditReply(res);
        }

        static object Coordinate(CoreContext c)
        {
            var nodes = CharaKeys(c);
            string text = c.Args.Require<string>("file");
            string modeText = c.Args.Get("mode", "all");
            int mi = Array.IndexOf(CoordinateModes, modeText);
            if (mi < 0) throw CommandKit.BadArgs("mode", ArgTypes.Enum(CoordinateModes), "\"" + modeText + "\" is not a coordinate load mode");
            var mode = mi == 0 ? CoordinateApplyMode.Clothes : mi == 1 ? CoordinateApplyMode.Accessories : CoordinateApplyMode.All;
            bool hs2n = text.EndsWith(SaveFormats.CoordExtension, StringComparison.OrdinalIgnoreCase);
            string full;
            if (hs2n) full = FileRoots.ForRead(c, "file", text, c.Paths.CoordDir, new[] { SaveFormats.CoordExtension }, null);
            else
            {
                int sex = StudioOps.SexOf(nodes[0]) == 0 ? 0 : 1;
                string root = c.Paths.Hs2UserData == null ? null : System.IO.Path.Combine(c.Paths.Hs2UserData, "coordinate", sex == 0 ? "male" : "female");
                full = FileRoots.ForRead(c, "file", text, root, new[] { ".png" }, ".png");
            }
            var res = CommandKit.Check(c, CommandKit.Ops(c).ApplyCoordinateFile(CommandKit.Ids(nodes), full, mode, CommandKit.Exec(c, null)), "file");
            var reply = EditReply(res);
            var src = (res.Value as MakerResult)?.Coordinate;
            if (src != null)
                foreach (var w in src.Warnings) c.Warn(w);   // the reader's / importer's findings (the envelope's warnings)
            reply["source"] = src == null ? null : new BridgeReply().With("file", src.File).With("sex", src.Sex).With("name", src.Name)
                .With("mode", CoordinateModes[src.Mode == CoordinateApplyMode.Clothes ? 0 : src.Mode == CoordinateApplyMode.Accessories ? 1 : 2]);
            reply["imported"] = src != null && src.Imported;
            return reply;
        }

        // ------------------------------------------------------------ chara.edit.load (Phase 4.6)

        /// <summary>The import provenance of stored documents as save-format JSON (null when the character was made in HS2Next).</summary>
        static object ImportJson(CharaSubtree st) =>
            st?.Import == null ? null : (object)new RawJson(DocText(ImportPath, st.Import.Write));

        /// <summary>
        /// The load flags of argument <paramref name="arg"/>: absent or null = all five; "all" or one flag name; a list of flag
        /// names (at least one, each once at most; unknown names are bad_args with the five as candidates).
        /// </summary>
        static LoadFlags FlagsArg(CoreContext c, string arg = "flags")
        {
            const string expected = "\"all\" or a list of \"face\", \"body\", \"hair\", \"coordinate\", \"profile\"";
            var k = c.Args.Kind(arg);
            if (CommandKit.Absent(k)) return LoadFlags.All;
            var candidates = new List<string>(LoadFlagNames.Names) { LoadFlagNames.AllName };
            if (k == ArgKind.String)
            {
                string s = c.Args.Get<string>(arg);
                if (LoadFlagNames.TryParse(s, out var one)) return one;
                throw new BridgeException(BridgeCodes.BadArgs, "unknown load flag \"" + s + "\"", new BridgeErrorDetail { Arg = arg, Expected = expected, Candidates = candidates });
            }
            if (k != ArgKind.Array) throw CommandKit.BadArgs(arg, expected);
            string[] names;
            try { names = c.Args.Get<string[]>(arg); }
            catch (Exception) { throw CommandKit.BadArgs(arg, expected, "flags is a list of flag names"); }
            if (names == null || names.Length == 0) throw CommandKit.BadArgs(arg, expected, "no load flag: give at least one of face, body, hair, coordinate, profile");
            foreach (var x in names)
                if (x == null) throw CommandKit.BadArgs(arg, expected, "a flag name is null");
            if (!LoadFlagNames.TryParse(names, out var flags, out string unknown))
                throw new BridgeException(BridgeCodes.BadArgs, "unknown load flag \"" + unknown + "\"", new BridgeErrorDetail { Arg = arg, Expected = expected, Candidates = candidates });
            if (flags == LoadFlags.None) throw CommandKit.BadArgs(arg, expected, "no load flag: give at least one of face, body, hair, coordinate, profile");
            return flags;
        }

        static object Load(CoreContext c)
        {
            var nodes = CharaKeys(c);
            var flags = FlagsArg(c);
            string text = c.Args.Require<string>("path");
            int sex = CommandKit.Sex(c, "sex", StudioOps.SexOf(nodes[0]) == 0 ? 0 : 1);
            bool hs2n = text.EndsWith(CharaCommands.CharaExtension, StringComparison.OrdinalIgnoreCase);
            string root = hs2n ? c.Paths.CharaDir : c.Paths.Hs2CharaDir(sex);
            string full = FileRoots.ForRead(c, "path", text, root, new[] { hs2n ? CharaCommands.CharaExtension : CharaCommands.CardExtension }, null);
            var res = CommandKit.Check(c, CommandKit.Ops(c).LoadCharacter(CommandKit.Ids(nodes), full, flags, CommandKit.Exec(c, null)), "path");
            var reply = EditReply(res);
            var src = (res.Value as MakerResult)?.Load;
            var first = c.Session.Model.Find(nodes[0].Id) ?? nodes[0];
            var findings = new List<object>();
            if (src != null) foreach (var w in src.Warnings) findings.Add(w);
            var result = new BridgeReply()
                .With("flags", LoadFlagNames.ToNames(src?.Flags ?? flags))
                .With("changed", reply["changed"]).With("consequences", reply["consequences"]).With("noop", reply["noop"])
                .With("imported", src != null && src.Imported)
                .With("importedTargets", src != null ? new List<string>(src.ImportedTargets) : new List<string>())
                .With("source", src == null ? null : new BridgeReply().With("file", src.File).With("sex", src.Sex).With("name", src.Name))
                .With("import", ImportJson(first.Appearance?.Stored))
                .With("findings", findings);
            foreach (var n in nodes)
            {
                var r = c.Runtime.FindChara(n.Id);
                if (r == null || r.State != RealizationState.Built)
                    c.Warn("character " + n.Id + " loaded but not built (" + (r?.State.ToString() ?? "NotRealized") + (r?.Detail != null ? ": " + r.Detail : "") + ")");
            }
            return result;
        }

        // ------------------------------------------------------------ chara.edit.accessories / accessoryDefaults / accessoryHairColour (Phase 4.5)

        static object Accessories(CoreContext c)
        {
            var n = CommandKit.Chara(c, "key");
            var sk = c.Args.Kind("slot");
            var ak = c.Args.Kind("accessory");
            bool hasSlot = !CommandKit.Absent(sk), hasAcc = !CommandKit.Absent(ak);
            if (hasSlot && hasAcc) throw CommandKit.BadArgs("slot", "slot or accessory", "give a build slot or an accessory id, not both");
            int only = hasSlot ? SlotArg(c, "slot") : -1;
            string accId = hasAcc ? CommandKit.OptString(c, "accessory") : null;
            bool served = CommandKit.OptBool(c, "served") ?? true;
            var real = CommandKit.Built(c, n, "key");
            var ctx = real.Context;
            if (ctx == null) throw new BridgeException(BridgeCodes.Unavailable, "character " + n.Id + " has no build context", new BridgeErrorDetail { Arg = "key" });
            var cur = c.Session.Model.Find(n.Id) ?? n;
            var shown = ((cur.State as CharaNodeState)?.State)?.Accessories;
            List<int> numbers;
            if (accId != null)
            {
                int s = AccessoryReport.SlotOfAccessory(ctx, accId);
                if (s < 0) throw CommandKit.NotFound("character " + n.Id + " wears no accessory \"" + accId + "\"", "accessory");
                numbers = new List<int> { s };
            }
            else numbers = only >= 0 ? new List<int> { only } : AccessoryReport.SlotNumbers(ctx);
            var slots = new List<object>(numbers.Count);
            foreach (int s in numbers) slots.Add(AccessorySlotReply(c, cur, ctx, AccessoryReport.SlotOf(ctx, s, served, true), shown, served));
            var hairActive = new List<object>(4);
            var hairChildren = new List<object>(4);
            for (int k = 0; k < 4; k++)
            {
                var ho = HairObject(ctx, k);
                hairActive.Add(ho ? (object)ho.activeSelf : null);
                int on = 0, all = 0;
                if (ho) foreach (Transform t in ho.transform) { all++; if (t.gameObject.activeSelf) on++; }
                hairChildren.Add(new List<object> { on, all });
            }
            var table = StudioOps.AccessorySlotsOf(cur);
            return new BridgeReply().With("slots", slots).With("slotCount", ctx.Accessories.Count).With("shaderType", ctx.Chara?.Hair?.ShaderType ?? 0)
                .With("hairActive", hairActive).With("hairChildren", hairChildren)
                .With("male", ctx.IsMale).With("extraSlotsOn", AccessoryReport.Of(ctx, false, false)?.ExtraSlotsOn ?? false)
                .With("freeSlots", table != null ? (object)table.FreeSlots() : null);
        }

        static object[] V3(Vector3 v) => new[] { Num(v.x), Num(v.y), Num(v.z) };
        static object[] Q4(Quaternion q) => new[] { Num(q.x), Num(q.y), Num(q.z), Num(q.w) };
        static object[] V3(Vec3 v) => new[] { Num(v.X), Num(v.Y), Num(v.Z) };

        /// <summary>One slot of chara.edit.accessories: the HS2 side's chara.accessoryInfo members in its order and meaning, then
        /// HS2Next's (see the command's help).</summary>
        static BridgeReply AccessorySlotReply(CoreContext c, TreeNode n, HS2Next.Chara.CharaContext ctx, AccessoryReport.SlotReport p, IReadOnlyDictionary<string, bool> shown, bool served)
        {
            var e = n.Appearance?.Stored?.Coord?.FindAccessory(p.Id);
            var acc = p.Slot < ctx.Accessories.Count ? ctx.Accessories[p.Slot] : null;
            var go = acc != null ? acc.Object : null;
            var row = acc?.Entry;
            bool empty = p.Category < 351 || p.Category > 363;
            string parentColumn = row != null ? NullIfEmpty(row["Parent"]) : null;
            // the key in effect: the stored parent, else the row's Parent column when it is one of the 54 attach points (HS2 keeps
            // no other text as the key: "null" / "0" rows and texts outside the 54 leave it as it was)
            string parentKey = !string.IsNullOrEmpty(p.Parent) ? p.Parent
                : !empty && parentColumn != null && Array.IndexOf(DocEnum.AttachPoints, parentColumn) >= 0 ? parentColumn : "";
            int keyIndex = Array.IndexOf(DocEnum.AttachPoints, parentKey);
            bool headWeighted = parentColumn == "null";
            // rawId: the item's local id (0 for an empty slot as HS2's part; a card build's slot carries the raw id as its item)
            object rawId = empty ? (object)0 : null;
            if (!empty && p.Item != null)
            {
                if (ContentId.TryParse(p.Item, out var item) && !item.IsEmpty) rawId = item.Id;
                else if (int.TryParse(p.Item, NumberStyles.Integer, CultureInfo.InvariantCulture, out int raw)) rawId = raw;
            }
            var r = new BridgeReply().With("slot", p.Slot).With("extra", p.Extra).With("type", p.Category)
                .With("id", empty ? null : p.Item).With("rawId", rawId).With("parentKey", parentKey);
            if (e != null)
            {
                var mv = new List<object>(2);
                foreach (var m in e.Moves) mv.Add(new BridgeReply().With("position", V3(m.Position)).With("rotation", V3(m.Rotation)).With("scale", V3(m.Scale)));
                var cols = new List<object>(4);
                foreach (var x in e.Colors)
                    cols.Add(new BridgeReply().With("color", CommandKit.C(x.Color)).With("glossPower", Num(x.Gloss)).With("metallicPower", Num(x.Metallic)).With("smoothnessPower", Num(x.Smoothness)));
                r["addMove"] = mv; r["colorInfo"] = cols;
                r["hideCategory"] = e.HideCategory; r["hideTiming"] = e.HideTiming;
            }
            else
            {
                // a card build's slot: the realized values; an empty slot: none
                r["addMove"] = null;
                if (acc != null && !empty)
                {
                    var mv = new List<object>(2);
                    for (int i = 0; i < 2; i++)
                        mv.Add(new BridgeReply().With("position", V3(acc.AddMove[i, 0])).With("rotation", V3(acc.AddMove[i, 1])).With("scale", V3(acc.AddMove[i, 2])));
                    r["addMove"] = mv;
                    var cols = new List<object>(4);
                    for (int l = 0; l < 4; l++)
                        cols.Add(new BridgeReply().With("color", CommandKit.C(acc.Color[l])).With("glossPower", Num(acc.Gloss[l])).With("metallicPower", Num(acc.Metallic[l]))
                            .With("smoothnessPower", Num(acc.Smoothness[l])));
                    r["colorInfo"] = cols;
                    r["hideCategory"] = acc.HideCategory; r["hideTiming"] = acc.HideTiming;
                }
                else { r["colorInfo"] = null; r["hideCategory"] = null; r["hideTiming"] = null; }
            }
            r["noShake"] = p.NoShake;
            r["partsOfHead"] = !empty && !headWeighted && keyIndex >= 0 && keyIndex < HeadAttachPoints;
            r["show"] = empty ? (object)null : p.Id == null || shown == null || !shown.TryGetValue(p.Id, out bool vis) || vis;
            r["loaded"] = (bool)go;
            r["object"] = go ? go.name : null;
            r["active"] = go ? (object)go.activeSelf : null;
            r["activeInHierarchy"] = go ? (object)go.activeInHierarchy : null;
            if (row != null)
            {
                var ro = new BridgeReply().With("category", row.Category).With("id", row.Key.Id).With("ref", row.Key.ToString());
                foreach (var col in AccessoryRowColumns) ro[col] = NullIfEmpty(row[col]);
                r["row"] = ro;
            }
            else r["row"] = null;
            var cmp = go ? go.GetComponent<CmpAccessory>() : null;
            var f = new BridgeReply().With("hasCmpAccessory", cmp != null).With("parentColumn", parentColumn).With("headWeighted", headWeighted).With("fixed", parentColumn == "0");
            r["facts"] = f;
            r["particleDummy"] = false;   // HS2Next keeps a prefab with particle systems as loaded (no dummy object)
            if (go)
            {
                var root = go.transform.parent;
                var cmps = new List<object>();
                foreach (var x in go.GetComponentsInChildren<CmpAccessory>(true)) cmps.Add(AccessoryReport.NamePath(x.transform, root));
                f["cmpComponents"] = cmps;
                f["cmpOnRoot"] = cmp != null;
                f["cmpUsed"] = cmp != null ? AccessoryReport.NamePath(cmp.transform, root) : null;
                int nps = 0;
                foreach (var x in go.GetComponentsInChildren<Component>(true)) if (x != null && x.GetType().Name == "ParticleSystem") nps++;
                f["particleSystems"] = nps;
                f["dynamicBones"] = go.GetComponentsInChildren<global::DynamicBone>(true).Length;
                f["colliders"] = go.GetComponentsInChildren<global::DynamicBoneCollider>(true).Length;
            }
            if (cmp != null)
            {
                f["typeHair"] = cmp.typeHair != 0;
                f["useColor"] = new List<object> { cmp.useColor01 != 0, cmp.useColor02 != 0, cmp.useColor03 != 0 };
                f["useGloss"] = new List<object> { cmp.useGloss01 != 0, cmp.useGloss02 != 0, cmp.useGloss03 != 0, cmp.useGloss04 != 0 };
                f["useMetallic"] = new List<object> { cmp.useMetallic01 != 0, cmp.useMetallic02 != 0, cmp.useMetallic03 != 0, cmp.useMetallic04 != 0 };
                int na = 0, nn = 0;
                if (cmp.rendAlpha != null) foreach (var x in cmp.rendAlpha) if (x) na++;
                if (cmp.rendNormal != null) foreach (var x in cmp.rendNormal) if (x) nn++;
                f["alphaRenderers"] = na; f["alphaLength"] = cmp.rendAlpha?.Count ?? 0;
                f["normalRenderers"] = nn; f["normalLength"] = cmp.rendNormal?.Count ?? 0;
                f["move1"] = (bool)cmp.trfMove01; f["move2"] = (bool)cmp.trfMove02;
                f["moveNames"] = new List<object> { cmp.trfMove01 ? cmp.trfMove01.name : null, cmp.trfMove02 ? cmp.trfMove02.name : null };
                var defs = new List<object>(4);
                var dc = new[] { cmp.defColor01, cmp.defColor02, cmp.defColor03, cmp.defColor04 };
                var dg = new[] { cmp.defGlossPower01, cmp.defGlossPower02, cmp.defGlossPower03, cmp.defGlossPower04 };
                var dm = new[] { cmp.defMetallicPower01, cmp.defMetallicPower02, cmp.defMetallicPower03, cmp.defMetallicPower04 };
                for (int k = 0; k < 4; k++) defs.Add(new BridgeReply().With("color", CommandKit.C(dc[k])).With("gloss", Num(dg[k])).With("metallic", Num(dm[k])));
                r["defaults"] = defs;
            }
            if (go)
            {
                var t = go.transform;
                r["anchor"] = t.parent ? new BridgeReply().With("name", t.parent.name).With("path", AccessoryReport.NamePath(t.parent, ctx.Root ? ctx.Root.transform : null)) : null;
                r["world"] = new BridgeReply().With("position", V3(t.position)).With("rotation", Q4(t.rotation)).With("lossyScale", V3(t.lossyScale))
                    .With("localPosition", V3(t.localPosition)).With("localRotation", Q4(t.localRotation));
                var moves = new List<object>(2);
                foreach (var mt in new[] { cmp ? cmp.trfMove01 : null, cmp ? cmp.trfMove02 : null })
                    moves.Add(!mt ? null : new BridgeReply().With("name", mt.name).With("position", V3(mt.localPosition)).With("rotation", V3(mt.localEulerAngles))
                        .With("localRotation", Q4(mt.localRotation)).With("scale", V3(mt.localScale)).With("worldPosition", V3(mt.position)));
                r["moves"] = moves;
                r["shaderType"] = ctx.Chara?.Hair?.ShaderType ?? 0;
                // every renderer under the slot object with its material slots
                var normal = new HashSet<Renderer>();
                var alpha = new HashSet<Renderer>();
                if (cmp?.rendNormal != null) foreach (var x in cmp.rendNormal) if (x) normal.Add(x);
                if (cmp?.rendAlpha != null) foreach (var x in cmp.rendAlpha) if (x) alpha.Add(x);
                var mats = new List<object>();
                foreach (var rd in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (!rd) continue;
                    string kind = normal.Contains(rd) ? (alpha.Contains(rd) ? "normal+alpha" : "normal") : alpha.Contains(rd) ? "alpha" : "other";
                    Material[] sm;
                    try { sm = rd.sharedMaterials; } catch (Exception) { sm = new Material[0]; }
                    for (int k = 0; k < sm.Length; k++) mats.Add(AccessoryMaterialReply(rd, kind, k, sm.Length, sm[k]));
                }
                r["materials"] = mats;
                // the slot's chains of the dynamic-bone stage (one per DynamicBone component it runs), stage order
                var chains = new List<object>();
                var stage = ctx.Motion != null ? ctx.Motion.Get<HS2Next.Motion.DynamicBones.DynamicBoneStage>() : null;
                if (stage != null)
                    foreach (var ch in stage.Chains)
                    {
                        if (ch == null || ch.Category != HS2Next.Motion.DynamicBones.DynamicBoneCategory.Accessory || ch.Slot != p.Slot) continue;
                        chains.Add(new BridgeReply().With("object", ch.Source ? ch.Source.gameObject.name : null).With("root", ch.RootName).With("colliders", ch.ColliderCount)
                            .With("enabled", ch.Enabled && !ch.NoShake).With("active", ch.Active).With("noShake", ch.NoShake).With("particles", ch.ParticleCount));
                    }
                r["chains"] = chains;
            }
            // HS2Next additions
            r["accessory"] = p.Id;
            r["category"] = p.Category;
            r["item"] = empty ? null : p.Item;
            r["parent"] = p.Parent;
            r["importSlot"] = p.ImportSlot;
            r["notLoaded"] = p.NotLoaded;
            r["showFlag"] = p.Show;
            r["anchorUsed"] = p.AnchorName;
            r["hairShaderType"] = p.ShaderType;
            if (p.Defaults != null)
            {
                var cd = new List<object>(4);
                foreach (var d in p.Defaults)
                    cd.Add(new BridgeReply().With("color", CommandKit.C(d.Color)).With("gloss", Num(d.Gloss)).With("metallic", Num(d.Metallic)).With("smoothness", Num(d.Smoothness)));
                r["colourDefaults"] = cd;
            }
            else r["colourDefaults"] = null;
            r["rules"] = e?.Rules != null ? (object)new RawJson(FieldAccessor.GetJson(c.Session.Model, n.Id, AppearancePaths.Accessory(e.Id, "rules"))) : null;
            r["rowRef"] = p.Row;
            if (served) r["served"] = new BridgeReply().With("MainData", new BridgeReply().With("kind", p.ServedKind).With("guid", p.ServedGuid));
            return r;
        }

        /// <summary>One material slot of chara.edit.accessories: the HS2 side's members (bounds = where the renderer draws, world;
        /// props = the accessory colour properties the shader declares).</summary>
        static BridgeReply AccessoryMaterialReply(Renderer rd, string kind, int slot, int slots, Material m)
        {
            var e = new BridgeReply().With("renderer", rd.gameObject.name).With("kind", kind).With("slot", slot).With("slots", slots)
                .With("enabled", rd.enabled).With("active", rd.gameObject.activeSelf).With("activeInHierarchy", rd.gameObject.activeInHierarchy);
            var bd = rd.bounds;
            e["bounds"] = new BridgeReply().With("center", V3(bd.center)).With("size", V3(bd.size));
            e["material"] = m ? m.name : null;
            if (!m) return e;
            e["shader"] = m.shader ? m.shader.name : null;
            e["queue"] = m.renderQueue;
            e["mainTex"] = TexName(m, "_MainTex");
            var props = new BridgeReply();
            var sh = m.shader;
            if (sh)
            {
                foreach (var name in AccessoryReport.ColorProperties)
                    if (sh.FindPropertyIndex(name) >= 0) props[name] = Col4(m.GetColor(name));
                foreach (var name in AccessoryReport.FloatProperties)
                    if (sh.FindPropertyIndex(name) >= 0) props[name] = Num(m.GetFloat(name));
            }
            e["props"] = props;
            return e;
        }

        static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

        static object AccessoryDefaults(CoreContext c)
        {
            var n = CommandKit.Chara(c, "key");
            string acc = AccessoryOfArgs(c, n);
            var res = CommandKit.Check(c, CommandKit.Ops(c).ResetAccessoryColours(n.Id, acc, CommandKit.Exec(c, null)), "accessory");
            return EditReply(res);
        }

        static object AccessoryHairColour(CoreContext c)
        {
            var n = CommandKit.Chara(c, "key");
            int part = HairPartArg(c, "part");
            string acc = AccessoryOfArgs(c, n);
            var res = CommandKit.Check(c, CommandKit.Ops(c).MatchAccessoryHairColour(n.Id, acc, part, CommandKit.Exec(c, null)), "accessory");
            return EditReply(res);
        }

        // ------------------------------------------------------------ shape.info / shape.set (HS2 names)

        static List<object> Doubles(float[] a)
        {
            var l = new List<object>(a?.Length ?? 0);
            if (a != null) foreach (var f in a) l.Add((double)f);
            return l;
        }

        static object ShapeInfo(CoreContext c)
        {
            var n = CommandKit.Chara(c, "key");
            var st = Stored(n);
            c.Runtime.ApplyNow();
            var real = c.Runtime.FindChara(n.Id);
            var ctx = real != null && real.State == RealizationState.Built ? real.Context : null;
            var chara = st.Chara;
            int sex = chara.Profile.Sex;
            var r = new BridgeReply()
                .With("sex", sex)
                .With("bodyCount", chara.Body.Shape.Length)
                .With("faceCount", chara.Face.Shape.Length)
                .With("bodyValues", Doubles(chara.Body.Shape))
                .With("faceValues", Doubles(chara.Face.Shape))
                .With("updatePending", real?.Editor != null && real.Editor.IsDirty);
            ContentId.TryParse(chara.Face.Head, out var head);
            r["headId"] = head.IsEmpty ? (object)null : head.Id;
            var row = head.IsEmpty ? null : c.Services?.Catalog?.GetEntry(sex == 0 ? 110 : 210, head);
            if (row != null)
            {
                r["headName"] = row["Name"];
                r["headMainAB"] = row["MainAB"];
                r["headShapeAnime"] = row["ShapeAnime"];
            }
            r["gameVersion"] = Application.version;
            r["unityVersion"] = Application.unityVersion;
            r["product"] = Application.productName;
            var body = ctx?.Anim;
            var hb = ctx?.HeadBone;
            r["bodyRoot"] = body ? body.name : null;
            r["bodyRootParent"] = body && body.transform.parent ? body.transform.parent.name : null;
            r["headRoot"] = hb ? hb.name : null;
            r["headRootParent"] = hb && hb.transform.parent ? hb.transform.parent.name : null;
            r["headRef"] = chara.Face.Head;   // HS2Next addition
            return r;
        }

        static object ShapeSet(CoreContext c)
        {
            var n = CommandKit.Chara(c, "key");
            var model = c.Session.Model;
            // the counts are the format's (33 / 59); a card-backed node is imported by the op
            int nb = CharaBodyShapeCount, nf = CharaFaceShapeCount;
            var sets = new List<(string, object)>();
            int setB = 0, setF = 0;
            foreach (var (arg, count, member, isBody) in new[] { ("body", nb, "body.shape", true), ("face", nf, "face.shape", false) })
            {
                var k = c.Args.Kind(arg);
                if (CommandKit.Absent(k)) continue;
                if (k != ArgKind.Array) throw CommandKit.BadArgs(arg, ArgTypes.ArrayOf(ArgTypes.Float));
                JArray a;
                try { a = JArray.Parse(c.Args.RawJson(arg)); }
                catch (Exception e) { throw CommandKit.BadArgs(arg, ArgTypes.ArrayOf(ArgTypes.Float), e.Message); }
                if (a.Count > count) throw CommandKit.BadArgs(arg, "at most " + count + " values", arg + " has " + a.Count + " values, the character has " + count);
                for (int i = 0; i < a.Count; i++)
                {
                    if (a[i].Type != JTokenType.Float && a[i].Type != JTokenType.Integer) throw CommandKit.BadArgs(arg + "[" + i + "]", ArgTypes.Float);
                    sets.Add((AppearancePaths.Chara(member + "[" + i.ToString(CultureInfo.InvariantCulture) + "]"), CommandKit.Finite((float)(double)a[i], arg + "[" + i + "]")));
                    if (isBody) setB++; else setF++;
                }
            }
            foreach (var (iArg, vArg, count, member, isBody) in new[] { ("bodyIndex", "bodyValue", nb, "body.shape", true), ("faceIndex", "faceValue", nf, "face.shape", false) })
            {
                if (!c.Args.Has(iArg)) continue;
                if (c.Args.Kind(iArg) != ArgKind.Integer) throw CommandKit.BadArgs(iArg, ArgTypes.Int);
                int i = c.Args.Get<int>(iArg);
                if (i < 0 || i >= count) throw CommandKit.BadArgs(iArg, "0.." + (count - 1), iArg + " out of range 0.." + (count - 1));
                var vk = c.Args.Kind(vArg);
                if (vk != ArgKind.Missing && vk != ArgKind.Float && vk != ArgKind.Integer) throw CommandKit.BadArgs(vArg, ArgTypes.Float);
                float v = vk == ArgKind.Missing ? 0.5f : (float)c.Args.Get<double>(vArg);
                sets.Add((AppearancePaths.Chara(member + "[" + i.ToString(CultureInfo.InvariantCulture) + "]"), CommandKit.Finite(v, vArg)));
                if (isBody) setB++; else setF++;
            }
            foreach (var a in new[] { "minFrames", "maxFrames" })
                if (c.Args.Has(a) && c.Args.Kind(a) != ArgKind.Integer) throw CommandKit.BadArgs(a, ArgTypes.Int);
            c.Runtime.ApplyNow();
            var before = Signature(c.Runtime.FindChara(n.Id));
            OpResult res = null;
            if (sets.Count > 0) res = CommandKit.Check(c, CommandKit.Ops(c).EditAppearance(new[] { n.Id }, sets, CommandKit.Exec(c, null)), "key");
            c.Runtime.ApplyNow();
            var real = c.Runtime.FindChara(n.Id);
            var after = Signature(real);
            int changed = 0;
            if (before != null && after != null)
                foreach (var kv in after)
                    if (!before.TryGetValue(kv.Key, out var b) || !Same(b, kv.Value)) changed++;
            bool settled = real != null && real.State == RealizationState.Built && real.Editor != null && !real.Editor.IsDirty;
            var st = (model.Find(n.Id) ?? n).Appearance?.Stored;
            return new BridgeReply()
                .With("bodySet", setB).With("faceSet", setF)
                .With("framesWaited", 0)
                .With("settled", settled)
                .With("changedTransforms", changed)
                .With("bodyValues", Doubles(st?.Chara.Body.Shape))
                .With("faceValues", Doubles(st?.Chara.Face.Shape))
                .With("changed", res != null ? Rels(res.Changed) : new List<string>());
        }

        static readonly int CharaBodyShapeCount = new CharaDocument().Body.Shape.Length, CharaFaceShapeCount = new CharaDocument().Face.Shape.Length;

        /// <summary>Local TRS of every transform under the body rig (head included), by hierarchy path; null when not built.</summary>
        static Dictionary<string, float[]> Signature(CharaRealization real)
        {
            if (real == null || real.State != RealizationState.Built) return null;
            var root = real.Context?.Anim ? real.Context.Anim.transform : real.Root ? real.Root.transform : null;
            if (!root) return null;
            var d = new Dictionary<string, float[]>(StringComparer.Ordinal);
            Walk(root, "", d);
            return d;
        }

        static void Walk(Transform t, string path, Dictionary<string, float[]> d)
        {
            var p = t.localPosition; var q = t.localRotation; var s = t.localScale;
            string key = path;
            int dup = 1;
            while (d.ContainsKey(key)) key = path + "[" + (++dup) + "]";
            d[key] = new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w, s.x, s.y, s.z };
            for (int i = 0; i < t.childCount; i++)
            {
                var ch = t.GetChild(i);
                Walk(ch, path.Length == 0 ? ch.name : path + "/" + ch.name, d);
            }
        }

        static bool Same(float[] a, float[] b)
        {
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
