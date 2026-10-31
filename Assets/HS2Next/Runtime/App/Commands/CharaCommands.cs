// Character, animation and face commands of the bridge (HS2 reply shapes: Appendix B):
//   chara.load        an HS2 card (imported at once) or a .hs2nchara file as a new character node ("Add character")
//   chara.save        a character node as a .hs2nchara file under <UserData>/chara (rendered thumbnail when possible:
//                     the front shot, or with thumb "view" the display camera's; a fixed clock for byte-identical saves)
//   coord.save        a character's worn coordinate as a .hs2ncoord file under <UserData>/coordinate/<female|male> (the
//                     coordinate thumbnail of the Maker pose when possible; the typed coordinate name in the file only;
//                     not recorded; Phase 4.6)
//   anime.groups      the visible animation groups and categories (display order)
//   anime.search      the listed animation rows (global winners per category; H rows only with includeH)
//   chara.anime       chara.anime.ref + time (+ speed, loop) in one entry ("Animation"); plain and H rows; the clip always
//                     starts at `time` (HS2's LoadAnime), also when the row and the time equal the model's
//   chara.speed       chara.anime.speed / forceLoop ("Animation speed")
//   chara.animeOption chara.anime.optionVisible ("Animation options") and the option items' transforms
//   chara.animeState  read-only: the realized motion in the members of HS2's private bridge (row, H flags, PV flags, the
//                     animator's controller chain, layers and parameters)
//   chara.yure        read-only: the sway table of the H row playing (lines kept, the matched line, the four enables, the
//                     bust shape flags and masks, the chains)
//   chara.face        face patterns, opens, blink, look patterns, tears, cheek ("Face"); eyebrow / eyes as the UI's
//                     list positions (raw: ids), as HS2's UiToId / IdToUi
//   chara.hands       chara.hands [left, right] ("Hands")
//   look.target       the look-at target's world position → chara.look.target (local to its parent; "Look target")
//   chara.mosaic      (5.M, read only) the mosaic of a built character: its mosaic-set members (mode, source, the last
//                     camera's block), the MaterialEditor items kept but not applied, the wide mosaic option; no setter
// Every mutation goes through the bus with Source Bridge and one history entry (CommandKit.Exec); the writes of
// chara.anime, chara.speed, chara.animeOption, chara.face, chara.hands and look.target are StudioOps methods (Phase 3.5
// extraction: the handlers parse, call and reply); replies read the realized state after Runtime.ApplyNow. Also CommandKit.FaceStateCore (the body of CommandKit.FaceState).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next.Api.Bridge;
using HS2Next.Content.Studio;
using HS2Next.Motion.Animation;
using HS2Next.Motion.Face;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;

namespace HS2Next.App.Commands
{
    public static class CharaCommands
    {
        public const string CardExtension = ".png", CharaExtension = ".hs2nchara";

        /// <summary>The commands Phase 4.6 adds to this file (part of CoreCommands.Phase46Names).</summary>
        public static readonly IReadOnlyList<string> Phase46Names = new[] { "coord.save" };

        /// <summary>The commands Phase 5.M adds to this file (CoreCommands.Phase5MNames).</summary>
        public static readonly IReadOnlyList<string> Phase5MNames = new[] { "chara.animeState", "chara.yure", "chara.mosaic" };

        /// <summary>The thumbnail modes of chara.save: render = the front shot (Studio default pose, HS2's coordinate camera),
        /// view = the display camera's view of the character as shown (front without a display camera), placeholder.</summary>
        public static readonly string[] CharaThumbModes = { "render", "view", "placeholder" };

        /// <summary>The thumbnail modes of coord.save: render = the coordinate thumbnail (fresh documents wearing it, the Maker
        /// pose, hair hidden), placeholder.</summary>
        public static readonly string[] CoordThumbModes = { "render", "placeholder" };
        /// <summary>The content-id argument ("guid#no"); not `id`, which is the envelope's request id.</summary>
        const string RefArg = CommandKit.RefArg;

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("chara.load", "Adds a character from an HS2 card (imported at once) or a .hs2nchara file; it builds in this call.", "chara")
                    .Arg("path", ArgTypes.Path, "an absolute path; a relative .hs2nchara path under <UserData>/chara; any other relative path under <hs2Root>/UserData/chara/female or /male (by sex)", true)
                    .Arg("sex", ArgTypes.Any, "0 / \"male\" or 1 / \"female\": the HS2 card folder of a relative card path; otherwise only checked against the file (warning)", false, "female")
                    .Arg("parent", ArgTypes.Node, "the node to add under (default: the scene root)")
                    .Arg("point", ArgTypes.String, "attach point N_* when the parent is a character")
                    .Return("added", ArgTypes.ArrayOf(ArgTypes.Object), "[the new node as in scene.objects]")
                    .Return("imported", ArgTypes.Bool, "the node stores imported documents")
                    .Return("buildMs", ArgTypes.Float).Return("findings", ArgTypes.ArrayOf(ArgTypes.Object))
                    .Example("{\"cmd\":\"chara.load\",\"path\":\"BR-Chan HS2 Rev2.png\",\"sex\":\"female\"}")
                    .WithFlags(CommandFlags.Mutates | CommandFlags.Slow).WithTimeout(300),
                CharaLoad);
            CommandKit.Register(r, app, CommandKit.Spec("chara.save", "Saves a character node as a .hs2nchara file under <UserData>/chara.", "chara")
                    .Arg("key", ArgTypes.Node, "a character", true)
                    .Arg("name", ArgTypes.Path, "file name under <UserData>/chara (.hs2nchara appended); one of name / path")
                    .Arg("path", ArgTypes.Path, "relative path under <UserData>/chara (subfolders created); one of name / path")
                    .Arg("overwrite", ArgTypes.Bool, "replace an existing file", false, false)
                    .Arg("thumb", ArgTypes.Enum(CharaThumbModes), "render: the front shot (the Studio default pose and HS2's coordinate camera relative to the node, in an isolation layer); view: the display camera's position, rotation and field of view with the character as shown, isolated (front with warning thumbnail.noView without a display camera); both need a built character and a graphics device, else the placeholder", false, "render")
                    .Arg("clock", ArgTypes.String, "ISO-8601 UTC time for createdUtc / modifiedUtc (byte-identical test saves; default now)")
                    .Return("path", ArgTypes.Path).Return("bytes", ArgTypes.Int).Return("thumbnail", ArgTypes.Enum("render", "placeholder"))
                    .Return("framing", ArgTypes.String, "the framing rendered: \"front\", \"view\" or null (placeholder)")
                    .WithFlags(CommandFlags.WritesFiles | CommandFlags.Slow).WithTimeout(120),
                CharaSave);
            CommandKit.Register(r, app, CommandKit.Spec("coord.save", "Saves the coordinate a character wears as a .hs2ncoord file under <UserData>/coordinate/<female|male> (the character's sex): the coordinate with its plugin data and the resources it references; the coordinate name written is coordName (in the file only: the character keeps its documents); the thumbnail is the Maker's coordinate shot (fresh documents of the sex wearing it, the Maker pose list's row 0, hair hidden, isolated) or the placeholder. Not recorded.", "chara")
                    .Arg("key", ArgTypes.Node, "a character", true)
                    .Arg("name", ArgTypes.Path, "file name under <UserData>/coordinate/<female|male> (.hs2ncoord appended); one of name / path")
                    .Arg("path", ArgTypes.Path, "relative path under <UserData>/coordinate/<female|male> (subfolders created); one of name / path")
                    .Arg("coordName", ArgTypes.String, "the coordinate name written into the file (coord.name; default: the character's)")
                    .Arg("overwrite", ArgTypes.Bool, "replace an existing file", false, false)
                    .Arg("thumb", ArgTypes.Enum(CoordThumbModes), "render: the coordinate thumbnail (needs a graphics device, else the placeholder with a warning); placeholder", false, "render")
                    .Arg("clock", ArgTypes.String, "ISO-8601 UTC time for createdUtc / modifiedUtc (byte-identical test saves; default now)")
                    .Return("path", ArgTypes.Path, "absolute path").Return("bytes", ArgTypes.Int)
                    .Return("thumbnail", ArgTypes.Enum("render", "placeholder"), "the thumbnail written")
                    .Return("sex", ArgTypes.Int, "the coordinate's sex (0 male, 1 female: the folder)").Return("coordName", ArgTypes.String, "the coordinate name written")
                    .Example("{\"cmd\":\"coord.save\",\"key\":\"<id>\",\"name\":\"my coordinate\",\"coordName\":\"Summer\"}")
                    .WithFlags(CommandFlags.WritesFiles | CommandFlags.Slow).WithTimeout(120),
                CoordSave);
            CommandKit.Register(r, app, CommandKit.Spec("anime.groups", "The visible animation groups and their categories in display order (HS2 shape).", "chara")
                    .Return("groups", ArgTypes.ArrayOf(ArgTypes.Object), "[{group, name, categories: [{category, name}]}]")
                    .WithFlags(CommandFlags.ReadOnly),
                AnimeGroups);
            CommandKit.Register(r, app, CommandKit.Spec("anime.search", "Animation rows as the lists show them (the global winner of each key), filtered by a text and group / category.", "chara")
                    .Arg("q", ArgTypes.String, "case-insensitive substring of \"<group> / <category> / <name>\"", false, "")
                    .Arg("group", ArgTypes.Int, "only this group")
                    .Arg("category", ArgTypes.Int, "only this category")
                    .Arg("limit", ArgTypes.Int, "maximum rows (1–10000)", false, 60)
                    .Arg("includeH", ArgTypes.Bool, "also list H rows (they play like the others)", false, false)
                    .Return("anime", ArgTypes.ArrayOf(ArgTypes.Object), "[{group, category, no, groupName, categoryName, name, guid, id, isH}]")
                    .Return("truncated", ArgTypes.Bool, "more rows match", false)
                    .WithFlags(CommandFlags.ReadOnly),
                AnimeSearch);
            CommandKit.Register(r, app, CommandKit.Spec("chara.anime", "Plays an animation row (plain or H) on a character from a normalized time (one history entry).", "chara")
                    .Arg("key", ArgTypes.Node, "a character", true)
                    .Arg("group", ArgTypes.Int, "animation group (absent: 0, as HS2)", false, 0)
                    .Arg("category", ArgTypes.Int, "animation category (absent: 0)", false, 0)
                    .Arg("no", ArgTypes.Int, "row number (absent: 0); ints alone resolve to the game's last-wins record", false, 0)
                    .Arg("guid", ArgTypes.String, "the source of the row (with group / category / no)")
                    .Arg(RefArg, ArgTypes.ContentId, "\"guid#no\" of the row (with group / category); `id` is the envelope's request id")
                    .Arg("time", ArgTypes.Float, "normalized time to start at", false, 0f)
                    .Arg("speed", ArgTypes.Float, "chara.anime.speed (≥ 0; absent: kept)")
                    .Arg("loop", ArgTypes.Bool, "chara.anime.forceLoop (absent: kept)")
                    .Return("object", ArgTypes.Object, "the character as in scene.objects")
                    .Example("{\"cmd\":\"chara.anime\",\"key\":\"<id>\",\"group\":19,\"category\":0,\"no\":0,\"time\":0.5}")
                    .WithFlags(CommandFlags.Mutates),
                CharaAnime);
            CommandKit.Register(r, app, CommandKit.Spec("chara.speed", "Animation speed and forced loop of a character (one history entry).", "chara")
                    .Arg("key", ArgTypes.Node, "a character", true)
                    .Arg("speed", ArgTypes.Float, "chara.anime.speed (≥ 0; absent: kept)")
                    .Arg("loop", ArgTypes.Bool, "chara.anime.forceLoop (absent: kept)")
                    .Return("speed", ArgTypes.Float).Return("loop", ArgTypes.Bool)
                    .WithFlags(CommandFlags.Mutates),
                CharaSpeed);
            CommandKit.Register(r, app, CommandKit.Spec("chara.animeOption", "Shows or hides the items an animation brings along and lists their transforms.", "chara")
                    .Arg("key", ArgTypes.Node, "a character", true)
                    .Arg("visible", ArgTypes.Bool, "chara.anime.optionVisible (absent: kept)")
                    .Return("visible", ArgTypes.Bool)
                    .Return("items", ArgTypes.ArrayOf(ArgTypes.Object), "[{name, bone, localPos, localRot, pos, rot, lossyScale}] (rounded to 4 decimals); empty unless built")
                    .WithFlags(CommandFlags.Mutates),
                CharaAnimeOption);
            CommandKit.Register(r, app, CommandKit.Spec("chara.animeState", "The realized motion of a character in the members of HS2's private bridge: row, H flags, PV flags, option parameters, and the animator (controller, override chain, layers with their states and weights, parameters).", "chara")
                    .Arg("key", ArgTypes.Node, "a character", true)
                    .Return("row", ArgTypes.Object, "{group, category, no} of the row requested")
                    .Return("isHAnime", ArgTypes.Bool, "an H row plays").Return("isAnimeMotion", ArgTypes.Bool)
                    .Return("breastLayer", ArgTypes.Int, "the last H row's breast layer column (kept through plain rows)")
                    .Return("pv", ArgTypes.ArrayOf(ArgTypes.Bool), "the eight PV flags")
                    .Return("animator", ArgTypes.Object, "{controller, override, baseController (with override), baseChain, layers [{index, name, weight, shortNameHash, normalizedTime, isRowClip, ...}], parameters [{name, type, value}]} (empty when no motion is loaded)")
                    .WithFlags(CommandFlags.ReadOnly),
                CharaAnimeState);
            CommandKit.Register(r, app, CommandKit.Spec("chara.yure", "The sway table of the H row a character plays, in the members of HS2's private bridge: lines kept for its motion id, the matched line, the breast and hip enables, the bust shape flags and masks, the chains.", "chara")
                    .Arg("key", ArgTypes.Node, "a character", true)
                    .Return("isInit", ArgTypes.Bool, "a table is loaded")
                    .Return("lineCount", ArgTypes.Int, "lines kept for the row's motion id (and owner)")
                    .Return("matchedLine", ArgTypes.Int, "index of the line applied (-1 = none: everything enabled)")
                    .Return("active", ArgTypes.ArrayOf(ArgTypes.Bool), "[breast L, breast R, hip L, hip R] enables")
                    .Return("shapeL", ArgTypes.Object, "{breast [7 bools: body shape ids 2..8], nip (id 32)}: true = enabled (left)")
                    .Return("shapeR", ArgTypes.Object, "the same for the right side")
                    .Return("disableBustShapeMask", ArgTypes.ArrayOf(ArgTypes.Any), "[left, right] x 8 bools (ids 2..8, 32): true = masked")
                    .WithFlags(CommandFlags.ReadOnly),
                CharaYure);
            CommandKit.Register(r, app, CommandKit.Spec("chara.face", "Face patterns, opens, blink, look patterns, tears and cheek of a character (one history entry).", "chara")
                    .Arg("key", ArgTypes.Node, "a character", true)
                    .Arg("eyebrow", ArgTypes.Int, "position in the character's eyebrow list (the UI number, clamped to the list); with raw: the pattern id")
                    .Arg("eyes", ArgTypes.Int, "position in the character's eye list (clamped); with raw: the pattern id")
                    .Arg("mouth", ArgTypes.Int, "mouth pattern id")
                    .Arg("eyesOpen", ArgTypes.Float, "chara.face.eyesOpen")
                    .Arg("mouthOpen", ArgTypes.Float, "chara.face.mouthOpen")
                    .Arg("blink", ArgTypes.Bool, "chara.face.blink")
                    .Arg("eyesLook", ArgTypes.Int, "eye look pattern 0–4 (4 = the look target)")
                    .Arg("neckLook", ArgTypes.Int, "neck look pattern 0–4")
                    .Arg("tears", ArgTypes.Float, "chara.face.tears")
                    .Arg("blush", ArgTypes.Float, "chara.face.cheek")
                    .Arg("raw", ArgTypes.Bool, "eyebrow / eyes are pattern ids", false, false)
                    .Return("face", ArgTypes.Object, "{eyebrow, eyes, eyebrowId, eyesId, mouth, eyesOpen, mouthOpen, blink, eyesLook, neckLook, tears, blush}")
                    .WithFlags(CommandFlags.Mutates),
                CharaFace);
            CommandKit.Register(r, app, CommandKit.Spec("chara.hands", "Hand patterns of a character: 0 = from the animation, n = hand pattern n − 1 (one history entry).", "chara")
                    .Arg("key", ArgTypes.Node, "a character", true)
                    .Arg("left", ArgTypes.Int, "left hand (absent: kept)")
                    .Arg("right", ArgTypes.Int, "right hand (absent: kept)")
                    .Return("hands", ArgTypes.ArrayOf(ArgTypes.Int), "[left, right]")
                    .WithFlags(CommandFlags.Mutates),
                CharaHands);
            CommandKit.Register(r, app, CommandKit.Spec("look.target", "The look-at target of a built character; pos moves it (stored local to its parent, one history entry).", "chara")
                    .Arg("key", ArgTypes.Node, "a built character", true)
                    .Arg("pos", ArgTypes.Vec3, "world position of the target (absent: only read)")
                    .Return("position", ArgTypes.Vec3).Return("local", ArgTypes.Vec3).Return("parent", ArgTypes.String)
                    .Return("active", ArgTypes.Bool).Return("eyesLookPtn", ArgTypes.Int)
                    .WithFlags(CommandFlags.Mutates),
                LookTarget);
            CommandKit.Register(r, app, CommandKit.Spec("chara.mosaic", "The mosaic of a built character (read only): its mosaic-set members with their mode, source and the last camera's block, the MaterialEditor items kept but not applied, and the wide mosaic option.", "chara")
                    .Arg("key", ArgTypes.Node, "a built character", true)
                    .Return("members", ArgTypes.ArrayOf(ArgTypes.Object), "[{path, mode (texture | procedural), source (texture name or null), sourceKind (body | own | null), bodySurface, restored, added, blockUv, blockPx, cellPx, camera (the last camera's values; null before a render), materials: [{name, shader, queue}]}]")
                    .Return("protected", ArgTypes.ArrayOf(ArgTypes.Object), "[{kind, object, target, property}]: MaterialEditor items kept in the documents but not applied to members (finding me.mosaicProtected)")
                    .Return("wide", ArgTypes.Bool, "the wide screen-space mosaic is on (option render.mosaicWide)")
                    .Return("findings", ArgTypes.ArrayOf(ArgTypes.Object), "the mosaic stage's findings (mosaic.patchesRestored, mosaic.transferUnavailable) and me.mosaicProtected per protected item")
                    .Example("{\"cmd\":\"chara.mosaic\",\"key\":\"<id>\"}")
                    .WithFlags(CommandFlags.ReadOnly),
                CharaMosaic);
        }

        // ------------------------------------------------------------ shared argument helpers (package W2)

        /// <summary>A [x, y, z] argument (bad_args for anything else); null when absent.</summary>
        internal static Vector3? Vec(CoreContext c, string arg)
        {
            if (!c.Args.Has(arg)) return null;
            var a = c.Args.Get<float[]>(arg);
            if (a == null || a.Length != 3) throw CommandKit.BadArgs(arg, ArgTypes.Vec3, "argument '" + arg + "': expected [x, y, z]");
            return new Vector3(a[0], a[1], a[2]);
        }

        internal static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        /// <summary>A float argument that must be finite (and ≥ <paramref name="min"/>); null when absent.</summary>
        internal static float? Float(CoreContext c, string arg, float min = float.NegativeInfinity)
        {
            if (!c.Args.Has(arg)) return null;
            float f = c.Args.Get<float>(arg);
            if (!Finite(f) || f < min) throw CommandKit.BadArgs(arg, min > float.NegativeInfinity ? "a finite number ≥ " + min : "a finite number");
            return f;
        }

        static CharaNodeState StateOf(TreeNode n) => n.State as CharaNodeState ?? throw new BridgeException(BridgeCodes.Internal, "character node without character state");

        // ------------------------------------------------------------ chara.load

        static object CharaLoad(CoreContext c)
        {
            string path = c.Args.Require<string>("path");
            bool sexGiven = c.Args.Has("sex");
            int sex = CommandKit.Sex(c, "sex", 1);
            var parent = CommandKit.OptionalNode(c, "parent");
            string point = c.Args.Get<string>("point");
            if (string.IsNullOrEmpty(point)) point = null;
            bool hs2n = path.EndsWith(CharaExtension, StringComparison.OrdinalIgnoreCase);
            string root = hs2n ? c.Paths.CharaDir : c.Paths.Hs2CharaDir(sex);
            string full = FileRoots.ForRead(c, "path", path, root, new[] { hs2n ? CharaExtension : CardExtension }, null);

            var found = new FindingList();
            var o = CommandKit.Exec(c, "Add character");
            var res = hs2n
                ? c.Runtime.Ops.AddCharaFromFile(full, parent?.Id, point, -1, o, found)
                : c.Runtime.Ops.AddCharaFromCard(full, parent?.Id, point, -1, o, found);
            CommandKit.Check(res, "path");
            string id = res.NewId;
            c.Runtime.ApplyNow();
            var n = id != null ? c.Session.Model.Find(id) : null;
            if (n == null) throw new BridgeException(BridgeCodes.Internal, "the character was not added");
            var r = c.Runtime.FindChara(id);
            if (r == null || r.State != RealizationState.Built)
                c.Warn("character added but not built (" + (r?.State.ToString() ?? "NotRealized") + (r?.Detail != null ? ": " + r.Detail : "") + ")");
            int fileSex = SexOf(n);
            if (sexGiven && fileSex >= 0 && fileSex != sex)
                c.Warn("sex: the file is " + (fileSex == 0 ? "male" : "female") + ", the request said " + (sex == 0 ? "male" : "female"));

            // the import's or load's warnings and errors, then the node's scene findings (without the copies the add made)
            var findings = CommandKit.Findings(found, infos: false);
            var copies = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in found)
                if (f.Severity != FindingSeverity.Info) copies.Add(f.Severity + "|" + f.Code + "|" + (string.IsNullOrEmpty(f.Path) ? "" : f.Path + ": ") + f.Message);
            findings.AddRange(CommandKit.Findings(c.Session.Findings, f => f.NodeId == id && !copies.Contains(f.Severity + "|" + f.Code + "|" + f.Message)));
            return new BridgeReply
            {
                ["added"] = new List<object> { CommandKit.Describe(c.App, n) },
                ["imported"] = n.Appearance?.Stored != null,
                ["buildMs"] = r?.BuildMilliseconds ?? 0.0,
                ["findings"] = findings,
            };
        }

        /// <summary>0 male, 1 female, −1 unknown: the stored profile, else the card.</summary>
        internal static int SexOf(TreeNode n) => StudioOps.SexOf(n);

        // ------------------------------------------------------------ chara.save

        static object CharaSave(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            bool overwrite = c.Args.Get("overwrite", false);
            string thumb = c.Args.Get("thumb", "render");
            if (Array.IndexOf(CharaThumbModes, thumb) < 0) throw CommandKit.BadArgs("thumb", ArgTypes.Enum(CharaThumbModes));
            var clock = ClockArg(c);
            string full = FileRoots.ForWrite(c, c.Paths.CharaDir, CharaExtension, overwrite, out _);
            // the write is StudioOps.SaveChara (container, thumbnail, save; its warnings and errors as before)
            var framing = thumb == "view" ? CharaThumbnailFraming.View : CharaThumbnailFraming.Front;
            var saved = (CharaSaveResult)CommandKit.Check(c, CommandKit.Ops(c).SaveChara(n.Id, full, thumb != "placeholder", framing, clock)).Value;
            return new BridgeReply { ["path"] = saved.Path, ["bytes"] = saved.Bytes, ["thumbnail"] = saved.Thumbnail, ["framing"] = saved.Framing };
        }

        /// <summary>The `clock` argument (ISO-8601, read as UTC) as a save clock; null when absent.</summary>
        internal static Func<DateTime> ClockArg(CoreContext c, string arg = "clock")
        {
            var k = c.Args.Kind(arg);
            if (CommandKit.Absent(k)) return null;
            string s = k == ArgKind.String ? c.Args.Get<string>(arg) : null;
            if (s == null || !DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t))
                throw CommandKit.BadArgs(arg, "ISO-8601 time", arg + " is not an ISO-8601 time: " + (s ?? c.Args.RawJson(arg)));
            return () => t;
        }

        // ------------------------------------------------------------ coord.save (Phase 4.6)

        static object CoordSave(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            bool overwrite = c.Args.Get("overwrite", false);
            string thumb = c.Args.Get("thumb", "render");
            if (Array.IndexOf(CoordThumbModes, thumb) < 0) throw CommandKit.BadArgs("thumb", ArgTypes.Enum(CoordThumbModes));
            string coordName = CommandKit.OptString(c, "coordName");
            var clock = ClockArg(c);
            int sex = SexOf(n);
            if (sex < 0) throw new BridgeException(BridgeCodes.Unsupported, "character " + n.Id + " has no appearance to save", new BridgeErrorDetail { Arg = "key" });
            string root = Path.Combine(c.Paths.CoordDir, sex == 0 ? "male" : "female");
            string full = FileRoots.ForWrite(c, root, SaveFormats.CoordExtension, overwrite, out _);
            var saved = (CoordSaveResult)CommandKit.Check(c, CommandKit.Ops(c).SaveCoordinate(n.Id, full, coordName, thumb == "render", clock), "key").Value;
            return new BridgeReply().With("path", saved.Path).With("bytes", saved.Bytes).With("thumbnail", saved.Thumbnail)
                .With("sex", saved.Sex).With("coordName", saved.Name);
        }

        // ------------------------------------------------------------ anime.groups, anime.search

        static object AnimeGroups(CoreContext c)
        {
            var db = c.Db;
            var groups = new List<object>();
            foreach (var g in db.AnimeGroups())
            {
                var cats = new List<object>();
                foreach (var cat in db.CategoriesOf(g)) cats.Add(new BridgeReply { ["category"] = cat.Number, ["name"] = cat.Name ?? "" });
                groups.Add(new BridgeReply { ["group"] = g.Number, ["name"] = g.Name ?? "", ["categories"] = cats });
            }
            return new BridgeReply { ["groups"] = groups };
        }

        static object AnimeSearch(CoreContext c)
        {
            var db = c.Db;
            string q = c.Args.Get("q", "") ?? "";
            bool hasG = c.Args.TryGet<int>("group", out int gf), hasC = c.Args.TryGet<int>("category", out int cf);
            int limit = c.Args.Get("limit", 60);
            if (limit < 1 || limit > 10000) throw CommandKit.BadArgs("limit", "1–10000");
            bool includeH = c.Args.Get("includeH", false);
            var list = new List<object>();
            bool truncated = false;
            foreach (var g in db.AnimeGroups())
            {
                if (hasG && g.Number != gf) continue;
                foreach (var cat in db.CategoriesOf(g))
                {
                    if (hasC && cat.Number != cf) continue;
                    foreach (var a in db.AnimeIn(cat))
                    {
                        if (a.IsH && !includeH) continue;
                        string hay = (g.Name ?? "") + " / " + (cat.Name ?? "") + " / " + (a.Name ?? "");
                        if (q.Length > 0 && hay.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (list.Count >= limit) { truncated = true; break; }
                        list.Add(new BridgeReply
                        {
                            ["group"] = a.Group, ["category"] = a.Category, ["no"] = a.No,
                            ["groupName"] = g.Name ?? "", ["categoryName"] = cat.Name ?? "", ["name"] = a.Name ?? "",
                            ["guid"] = a.Guid, ["id"] = a.Key.ToString(), ["isH"] = a.IsH,
                        });
                    }
                    if (truncated) break;
                }
                if (truncated) break;
            }
            var rep = new BridgeReply { ["anime"] = list };
            if (truncated) rep["truncated"] = true;
            return rep;
        }

        // ------------------------------------------------------------ chara.anime, chara.speed, chara.animeOption

        static object CharaAnime(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            var row = CommandKit.ResolveAnime(c, out string described);
            if (row == null)
                throw CommandKit.NotFound("no animation " + described, c.Args.Has(RefArg) ? RefArg : "no",
                    CommandKit.AnimeCandidates(c.Db, c.Args.Get("group", 0), c.Args.Get("category", 0), c.Args.Get("no", 0)));
            var warnings = new List<string>();
            var bad = StudioOps.CheckAnimeRow(row, described, warnings);   // hidden rows warned (before the numbers)
            if (bad != null) throw CommandKit.Unsupported(bad.Message);
            foreach (var w in warnings) c.Warn(w);
            float time = Float(c, "time") ?? 0f;
            float? speed = Float(c, "speed", 0f);
            bool hasLoop = c.Args.TryGet<bool>("loop", out bool loop);
            // HS2's LoadAnime always starts the clip at `time` (StudioOps.SetAnimation jumps when row and time equal the model's)
            var res = CommandKit.Ops(c).SetAnimation(new[] { n.Id }, row, time, speed, hasLoop ? loop : (bool?)null, described, CommandKit.Exec(c, StudioOps.Labels.Animation));
            res.Warnings.Clear();   // already forwarded above
            CommandKit.Check(c, res);
            return new BridgeReply { ["object"] = CommandKit.Describe(c.App, n) };
        }

        static object CharaSpeed(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            float? speed = Float(c, "speed", 0f);
            bool hasLoop = c.Args.TryGet<bool>("loop", out bool loop);
            CommandKit.Check(c, CommandKit.Ops(c).SetAnimeSpeed(new[] { n.Id }, speed, hasLoop ? loop : (bool?)null, CommandKit.Exec(c, StudioOps.Labels.AnimationSpeed)));
            var an = StateOf(n).Anime;
            return new BridgeReply { ["speed"] = an.Speed, ["loop"] = an.ForceLoop };
        }

        static object CharaAnimeOption(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            if (c.Args.TryGet<bool>("visible", out bool vis))
                CommandKit.Check(c, CommandKit.Ops(c).SetAnimeOption(new[] { n.Id }, vis, null, null, CommandKit.Exec(c, StudioOps.Labels.AnimationOptions)), "visible");
            c.Runtime.ApplyNow();
            var items = new List<object>();
            var r = c.Runtime.FindChara(n.Id);
            var anim = r != null && r.State == RealizationState.Built ? r.Animation : null;
            if (!anim) c.Warn("character not built: no option items listed");
            else
                foreach (var it in anim.Items.Items)
                {
                    if (it == null || !it.Root) continue;
                    var parts = new List<Transform>();
                    if (it.Moved.Count > 0) { foreach (var m in it.Moved) if (m.moved) parts.Add(m.moved); }
                    else parts.Add(it.Root.transform);
                    foreach (var t in parts)
                        items.Add(new BridgeReply
                        {
                            ["name"] = t.name,
                            ["bone"] = t.parent ? t.parent.name : "",
                            ["localPos"] = CommandKit.R4(t.localPosition),
                            ["localRot"] = CommandKit.R4(t.localEulerAngles),
                            ["pos"] = CommandKit.R4(t.position),
                            ["rot"] = CommandKit.R4(t.eulerAngles),
                            ["lossyScale"] = CommandKit.R4(t.lossyScale),
                        });
                }
            return new BridgeReply { ["visible"] = StateOf(n).Anime.OptionVisible, ["items"] = items };
        }

        // ------------------------------------------------------------ chara.animeState, chara.yure (read-only, Phase 5.M)

        static CharaAnimation BuiltAnimation(CoreContext c, TreeNode n)
        {
            c.Runtime.ApplyNow();
            var r = c.Runtime.FindChara(n.Id);
            return r != null && r.State == RealizationState.Built ? r.Animation : null;
        }

        static object CharaAnimeState(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            var anim = BuiltAnimation(c, n);
            var rep = new BridgeReply { ["key"] = n.Id };
            if (!anim) { c.Warn("character not built"); rep["built"] = false; return rep; }
            var row = anim.Current;
            var drv = anim.Driver;
            bool loaded = anim.IsLoaded && drv != null;
            rep["built"] = loaded;
            rep["row"] = new BridgeReply { ["group"] = row != null ? row.Group : -1, ["category"] = row != null ? row.Category : -1, ["no"] = row != null ? row.No : -1 };
            rep["guid"] = row?.Guid;
            rep["isHAnime"] = anim.IsHPlaying;
            rep["isAnimeMotion"] = anim.IsAnimeMotion;
            rep["breastLayer"] = anim.LastHBreastLayer;
            rep["extraLayer"] = anim.ExtraLayer;
            var pv = new List<object>();
            foreach (var f in anim.PvFlags) pv.Add(f);
            rep["pv"] = pv;
            rep["pvCopy"] = new List<object>(pv);   // the PV copy is not wired yet: the flags as stored
            rep["animePattern"] = anim.Pattern;
            rep["animeOptionParam"] = new List<object> { anim.OptionParam1, anim.OptionParam2 };
            rep["animeParamNames"] = new List<object> { anim.HeightParameter, anim.BreastParameter };
            rep["animeSpeed"] = anim.Speed;
            int clipHash = Animator.StringToHash(row?.Clip ?? "");
            rep["clipHash"] = clipHash;
            rep["clip"] = row?.Clip ?? "";
            rep["optionItems"] = anim.Items.Count;
            var an = new BridgeReply();
            rep["animator"] = an;
            if (!loaded) return rep;
            var top = drv.Controller;
            an["controller"] = top ? top.name : "";
            bool isOverride = top is AnimatorOverrideController;
            an["override"] = isOverride;
            var chain = new List<object>();
            if (isOverride)
            {
                an["baseController"] = anim.BaseController ? anim.BaseController.name : "";
                for (var x = ((AnimatorOverrideController)top).runtimeAnimatorController; x; x = (x as AnimatorOverrideController)?.runtimeAnimatorController)
                {
                    chain.Add(x.name);
                    if (!(x is AnimatorOverrideController)) break;
                }
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                ((AnimatorOverrideController)top).GetOverrides(pairs);
                int overridden = 0;
                foreach (var pr in pairs) if (pr.Value) overridden++;
                an["overriddenClips"] = overridden;
                an["clipPairs"] = pairs.Count;
            }
            else if (top) chain.Add(top.name);
            an["baseChain"] = chain;
            an["speed"] = anim.Speed;   // HS2Next keeps the Animator at 1 and scales the step by the character's speed
            an["enabled"] = anim.Animator && anim.Animator.enabled;
            an["layerCount"] = drv.LayerCount;
            var layers = new List<object>();
            for (int i = 0; i < drv.LayerCount; i++)
            {
                var st = drv.State(i);
                var clips = new List<object>();
                foreach (var ci in drv.ClipInfo(i)) clips.Add(new BridgeReply { ["clip"] = ci.clip ? ci.clip.name : "", ["weight"] = ci.weight });
                layers.Add(new BridgeReply
                {
                    ["index"] = i, ["name"] = drv.LayerName(i), ["weight"] = drv.GetLayerWeight(i),
                    ["shortNameHash"] = st.shortNameHash, ["fullPathHash"] = st.fullPathHash, ["normalizedTime"] = st.normalizedTime,
                    ["length"] = st.length, ["speed"] = st.speed, ["speedMultiplier"] = st.speedMultiplier, ["loop"] = st.loop,
                    ["inTransition"] = false, ["isRowClip"] = st.shortNameHash == clipHash, ["clips"] = clips,
                });
            }
            an["layers"] = layers;
            var ps = new List<object>();
            foreach (var p in drv.Parameters)
            {
                object v;
                switch (p.type)
                {
                    case AnimatorControllerParameterType.Float: v = drv.GetFloat(p.nameHash); break;
                    case AnimatorControllerParameterType.Int: v = drv.GetInteger(p.nameHash); break;
                    default: v = drv.GetBool(p.nameHash); break;
                }
                ps.Add(new BridgeReply { ["name"] = p.name, ["type"] = p.type.ToString(), ["value"] = v });
            }
            an["parameters"] = ps;
            return rep;
        }

        static List<object> ShapeFlags(int mask)
        {
            var breast = new List<object>();
            for (int k = 0; k < HSwayTable.SideFlags - 2; k++) breast.Add((mask & (1 << k)) == 0);
            return new List<object> { breast, (mask & (1 << (HSwayTable.SideFlags - 2))) == 0 };
        }

        static BridgeReply Shape(int mask)
        {
            var f = ShapeFlags(mask);
            return new BridgeReply { ["breast"] = f[0], ["nip"] = f[1] };
        }

        static object CharaYure(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            var anim = BuiltAnimation(c, n);
            var rep = new BridgeReply { ["key"] = n.Id };
            if (!anim) { c.Warn("character not built"); rep["isInit"] = false; return rep; }
            var y = anim.Sway;
            rep["isInit"] = y.Loaded;
            rep["ownerIndex"] = y.Owner;
            rep["lineCount"] = y.Lines.Count;
            var lines = new List<object>();
            foreach (var l in y.Lines)
                lines.Add(new BridgeReply
                {
                    ["state"] = l.State, ["owner"] = l.Owner < 0 ? 0 : l.Owner,
                    ["active"] = new List<object> { l.Left[0], l.Right[0], l.HipL, l.HipR },
                    ["shapeL"] = Shape(l.MaskL), ["shapeR"] = Shape(l.MaskR),
                });
            rep["lines"] = lines;
            rep["active"] = new List<object> { y.BustL, y.BustR, y.HipL, y.HipR };
            rep["shapeL"] = Shape(y.MaskL);
            rep["shapeR"] = Shape(y.MaskR);
            rep["matchedLine"] = y.MatchedIndex;
            var bust = anim.BustStage;
            if (bust != null)
            {
                rep["dynamicBust"] = new List<object> { bust.AnimeBustL, bust.AnimeBustR, !bust.FkBreastActive, !bust.FkBreastActive };
                var chains = new List<object>();
                for (int k = 0; k < 4; k++)
                    chains.Add(bust.Chains[k] != null ? new BridgeReply { ["type"] = "DynamicBone_Ver02", ["enabled"] = bust.Chains[k].Active } : null);
                rep["dynamicBones"] = chains;   // breast L, breast R, hip L, hip R
            }
            var shape = anim.Context?.BodyShape;
            var masks = new List<object>();
            for (int side = 0; side < 2; side++)
            {
                int m = shape != null ? shape.BustMask(side) : 0;
                var l = new List<object>();
                for (int k = 0; k < HS2Next.Chara.ShapeEngine.BustMaskIds.Length; k++) l.Add((m & (1 << k)) != 0);
                masks.Add(l);
            }
            rep["disableBustShapeMask"] = masks;   // per side (L, R), shape ids 2..8, 32; true = disabled
            rep["bundle"] = y.Bundle ?? "";
            rep["file"] = y.File ?? "";
            rep["motionId"] = y.MotionId;
            rep["tableRows"] = y.TableRows;
            rep["state"] = y.Matched != null ? y.Matched.State : "";
            return rep;
        }

        // ------------------------------------------------------------ chara.face

        /// <summary>The expression lists of a character: the built character's, else the catalog's by the appearance's sex.</summary>
        internal static ExpressionLists ListsOf(AppCore app, TreeNode n)
        {
            if (app.Runtime != null) return StudioOps.For(app.Runtime).ListsOf(n);
            return ExpressionLists.For(app.Services?.Catalog, SexOf(n) == 0);
        }

        /// <summary>HS2's UiToId: the id at a UI position clamped to the list (the position itself for an empty list).</summary>
        internal static int UiToId(int[] keys, int ui) => StudioOps.UiToId(keys, ui);

        static object CharaFace(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            bool raw = c.Args.Get("raw", false);
            var e = new FaceEdit { ListPositions = !raw };
            if (c.Args.TryGet<int>("eyebrow", out int eb)) e.Eyebrow = eb;
            if (c.Args.TryGet<int>("eyes", out int ey)) e.Eyes = ey;
            if (c.Args.TryGet<int>("mouth", out int mo)) e.Mouth = mo;
            e.EyesOpen = Float(c, "eyesOpen");
            e.MouthOpen = Float(c, "mouthOpen");
            if (c.Args.TryGet<bool>("blink", out bool bl)) e.Blink = bl;
            if (c.Args.TryGet<int>("eyesLook", out int el))
            {
                if (el < 0 || el > 4) throw CommandKit.BadArgs("eyesLook", "0–4");
                e.EyesLook = el;
            }
            if (c.Args.TryGet<int>("neckLook", out int nl))
            {
                if (nl < 0 || nl > 4) throw CommandKit.BadArgs("neckLook", "0–4");
                e.NeckLook = nl;
            }
            e.Tears = Float(c, "tears");
            e.Cheek = Float(c, "blush");
            CommandKit.Check(c, CommandKit.Ops(c).SetFace(new[] { n.Id }, e, CommandKit.Exec(c, StudioOps.Labels.Face)));
            return new BridgeReply { ["face"] = CommandKit.FaceState(c.App, n) };
        }

        // ------------------------------------------------------------ chara.hands, look.target

        static object CharaHands(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            StateOf(n);
            bool hl = c.Args.TryGet<int>("left", out int nl), hr = c.Args.TryGet<int>("right", out int nr);
            CommandKit.Check(c, CommandKit.Ops(c).SetHands(new[] { n.Id }, hl ? nl : (int?)null, hr ? nr : (int?)null, CommandKit.Exec(c, StudioOps.Labels.Hands)));
            var st = StateOf(n);
            return new BridgeReply { ["hands"] = new List<object> { st.Hands[0], st.Hands[1] } };
        }

        // ------------------------------------------------------------ chara.mosaic (5.M)

        static object CharaMosaic(CoreContext c)
        {
            var r = CommandKit.Built(c);
            var ctx = r.Context;
            var st = HS2Next.Chara.Mosaic.MosaicSet.StateOf(ctx);
            var members = new List<object>();
            if (st != null)
                foreach (var m in st.Members)
                {
                    if (!m.Renderer) continue;
                    var mats = new List<object>();
                    foreach (var mat in m.Renderer.sharedMaterials)
                        mats.Add(new Dictionary<string, object> { ["name"] = mat ? mat.name : null, ["shader"] = mat && mat.shader ? mat.shader.name : null, ["queue"] = mat ? mat.renderQueue : 0 });
                    bool rendered = m.LastCamera != null;
                    members.Add(new Dictionary<string, object>
                    {
                        ["path"] = m.Path,
                        ["mode"] = m.Mode == HS2Next.Chara.Mosaic.MosaicMode.Texture ? "texture" : "procedural",
                        ["source"] = m.Source ? m.Source.name : null,
                        ["sourceKind"] = m.SourceKind,
                        ["bodySurface"] = m.BodySurface,
                        ["restored"] = m.Restored,
                        ["added"] = m.Added,
                        ["blockUv"] = rendered && m.Mode == HS2Next.Chara.Mosaic.MosaicMode.Texture ? (object)m.BlockUv : null,
                        ["blockPx"] = rendered ? (object)Math.Round(m.BlockPx, 2) : null,
                        ["cellPx"] = rendered ? (object)Math.Round(m.CellPx, 2) : null,
                        ["camera"] = m.LastCamera,
                        ["materials"] = mats,
                    });
                }
            var prot = HS2Next.Chara.Mosaic.MosaicSet.ProtectedItems(ctx);
            var findings = new FindingList();
            if (st != null) findings.AddRange(st.Findings);
            foreach (var p in prot) findings.Warning(HS2Next.Chara.Mosaic.MosaicSet.MeProtectedCode, p.Object, "MaterialEditor item kept but not applied (mosaic member): " + p);
            return new BridgeReply
            {
                ["members"] = members,
                ["protected"] = prot.Select(p => (object)new Dictionary<string, object> { ["kind"] = p.Kind, ["object"] = p.Object, ["target"] = p.Target, ["property"] = p.Property }).ToList(),
                ["wide"] = MosaicWide.Enabled,
                ["findings"] = CommandKit.Findings(findings),
            };
        }

        static object LookTarget(CoreContext c)
        {
            var r = CommandKit.Built(c);
            var look = r.Look;
            var t = look ? look.LookAtTarget : null;
            if (!t) throw CommandKit.NotRealized("character " + r.NodeId + " has no look-at target", "key", r.State.ToString());
            var pos = Vec(c, "pos");
            if (pos != null) CommandKit.Check(c, CommandKit.Ops(c).SetLookTarget(r.NodeId, pos.Value, CommandKit.Exec(c, StudioOps.Labels.LookTarget)), "pos");
            return new BridgeReply
            {
                ["position"] = CommandKit.D3(t.position),
                ["local"] = CommandKit.D3(t.localPosition),
                ["parent"] = t.parent ? t.parent.name : null,
                ["active"] = look.Targets != null && look.Targets.LookAtGuideVisible,
                ["eyesLookPtn"] = look.EyePattern,
            };
        }
    }

    public static partial class CommandKit
    {
        // HS2's `face` object: from the model, or with realized from the built character's expression and look.
        static partial void FaceStateCore(AppCore app, TreeNode node, bool realized, BridgeReply into)
        {
            if (node == null) return;
            var r = realized ? app.Runtime?.FindChara(node.Id) : null;
            var ex = r != null && r.State == RealizationState.Built ? r.Expression : null;
            if (ex != null)
            {
                var s = ex.Status;
                var look = r.Look;
                into["eyebrow"] = ExpressionLists.Position(ex.Lists.Eyebrow, s.EyebrowPtn);
                into["eyes"] = ExpressionLists.Position(ex.Lists.Eyes, s.EyesPtn);
                into["eyebrowId"] = s.EyebrowPtn;
                into["eyesId"] = s.EyesPtn;
                into["mouth"] = s.MouthPtn;
                into["eyesOpen"] = s.EyesOpenMax;
                into["mouthOpen"] = ex.MouthOpen;
                into["blink"] = s.EyesBlink;
                into["eyesLook"] = look ? look.EyePattern : 0;
                into["neckLook"] = look ? look.NeckPattern : 0;
                into["tears"] = s.TearsRate;
                into["blush"] = s.HohoAkaRate;
                return;
            }
            if (!(node.State is CharaNodeState st)) return;
            var f = st.Face ?? new CharaFaceState();
            var l = st.Look ?? new CharaLookState();
            var lists = CharaCommands.ListsOf(app, node);
            into["eyebrow"] = ExpressionLists.Position(lists.Eyebrow, f.Eyebrow);
            into["eyes"] = ExpressionLists.Position(lists.Eyes, f.Eyes);
            into["eyebrowId"] = f.Eyebrow;
            into["eyesId"] = f.Eyes;
            into["mouth"] = f.Mouth;
            into["eyesOpen"] = f.EyesOpen;
            into["mouthOpen"] = f.MouthOpen;
            into["blink"] = f.Blink;
            into["eyesLook"] = l.Eyes;
            into["neckLook"] = l.Neck;
            into["tears"] = f.Tears;
            into["blush"] = f.Cheek;
        }
    }

    public static partial class CoreCommands
    {
        static partial void RegisterChara(ICommandRegistry r, AppCore app) => CharaCommands.Register(r, app);
    }
}
