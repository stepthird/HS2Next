// Maps and items: maps, map.load (Session.SetMap; the reply waits for the load unless
// wait is false; in Editor edit mode the state becomes Unsupported and the model change stands; re-selecting the scene's
// map is a no-op that keeps the pose), item.search,
// item.add (SceneOps.AddItem with the prefab defaults; group 647158 addresses the accessories offered as items) and
// item.parent (SceneOps.MoveKeepWorld onto a character's attach point or under another node, keeping the world pose).
// Content references follow CommandKit's `ref` argument.
using System;
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;

namespace HS2Next.App.Commands
{
    public static class MapItemCommands
    {
        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("maps", "The visible Studio maps in load order.", "map/item")
                .Arg("q", ArgTypes.String, "case-insensitive substring of the name", defaultValue: "")
                .Arg("limit", ArgTypes.Int, "at most this many", defaultValue: 50)
                .Return("maps", ArgTypes.ArrayOf(ArgTypes.Object), "{no, name, id, guid}")
                .Return("truncated", ArgTypes.Bool, "more than limit", false)
                .WithFlags(CommandFlags.ReadOnly).WithTimeout(60), Maps);

            CommandKit.Register(r, app, CommandKit.Spec("map.load", "Sets the scene's map (one undo entry \"Map\"); replies when it has loaded or failed unless wait is false.", "map/item")
                .Arg("no", ArgTypes.Int, "map number: a vanilla map alone, a mod's own id with guid")
                .Arg("guid", ArgTypes.String, "the map's source GUID (with no)")
                .Arg(CommandKit.RefArg, ArgTypes.ContentId, "\"guid#no\" (the reply's id)")
                .Arg("name", ArgTypes.String, "the first visible map whose name equals it (ignoring case), else the first that contains it")
                .Arg("clear", ArgTypes.Bool, "no map", defaultValue: false)
                .Arg("wait", ArgTypes.Bool, "reply when the map is Loaded or failed", defaultValue: true)
                .Return("no", ArgTypes.Int, "local id (−1 none)").Return("name", ArgTypes.String, "map name").Return(CommandKit.RefArg, ArgTypes.ContentId, "\"guid#no\" (top-level id is the envelope's)")
                .Return("guid", ArgTypes.String, "source GUID").Return("state", ArgTypes.String, "MapState")
                .Return("loadMs", ArgTypes.Int, "load time").Return("findings", ArgTypes.ArrayOf(ArgTypes.Object), "map.* findings of this load")
                .Return("note", ArgTypes.String, "with wait false", false)
                .Return("features", ArgTypes.Object, "{option, light}: the MapComponent's blocks").Return("mainLight", ArgTypes.Object, "{name, reason}")
                .Return("reselected", ArgTypes.Bool, "the map was already the scene's: nothing loaded, the pose kept, no history entry (M3)", false)
                .Example("{\"cmd\":\"map.load\",\"no\":10}")
                .WithFlags(CommandFlags.Mutates | CommandFlags.Slow | CommandFlags.Defers).WithTimeout(300), MapLoad);

            CommandKit.Register(r, app, CommandKit.Spec("item.search", "Visible Studio items whose \"group / category / name\" contains q.", "map/item")
                .Arg("q", ArgTypes.String, "case-insensitive substring of \"<group name> / <category name> / <item name>\"", defaultValue: "")
                .Arg("group", ArgTypes.Int, "only this item group")
                .Arg("category", ArgTypes.Int, "only this item category")
                .Arg("limit", ArgTypes.Int, "at most this many", defaultValue: 60)
                .Return("items", ArgTypes.ArrayOf(ArgTypes.Object), "{group, category, no, groupName, categoryName, name, id, guid}")
                .Return("truncated", ArgTypes.Bool, "more than limit", false)
                .WithFlags(CommandFlags.ReadOnly).WithTimeout(60), ItemSearch);

            CommandKit.Register(r, app, CommandKit.Spec("item.add", "Adds a Studio item with its prefab defaults (one undo entry \"Add item\").", "map/item")
                .Arg("group", ArgTypes.Int, "item group (checked against the row for vanilla ints); 647158 = the accessories offered as items, with category")
                .Arg("category", ArgTypes.Int, "item category (checked against the row for vanilla ints); with group 647158 the accessory category 351-363")
                .Arg("no", ArgTypes.Int, "item number: a vanilla item alone, a mod's own id with guid")
                .Arg("guid", ArgTypes.String, "the item's source GUID (with no)")
                .Arg(CommandKit.RefArg, ArgTypes.ContentId, "\"guid#no\" (item.search's id)")
                .Arg("parent", ArgTypes.Node, "parent node (default the scene root)")
                .Arg("point", ArgTypes.String, "attach point when parent is a character (N_*)")
                .Return("added", ArgTypes.ArrayOf(ArgTypes.Object), "the new node as in scene.objects")
                .Example("{\"cmd\":\"item.add\",\"group\":0,\"category\":0,\"no\":399}")
                .WithFlags(CommandFlags.Mutates).WithTimeout(60), ItemAdd);

            CommandKit.Register(r, app, CommandKit.Spec("item.parent", "Moves a node onto a character's attach point, under another node or back to the root, keeping its world pose (one undo entry \"Parent\").", "map/item")
                .Arg("key", ArgTypes.Node, "the node to move", required: true)
                .Arg("chara", ArgTypes.Node, "the character to attach to")
                .Arg("point", ArgTypes.String, "attach point name N_* or its number \"1\"-\"54\" (\"48\" = N_Hand_R); empty or absent with chara = unparent")
                .Arg("parent", ArgTypes.Node, "any node that takes children (point then null)")
                .Return("parent", ArgTypes.String, "point name, parent label or \"\"").Return("local", ArgTypes.Vec3, "new parent-local position (R4)")
                .Return("localRot", ArgTypes.Euler, "new parent-local rotation (R4)").Return("bone", ArgTypes.String, "the attach transform's name", false)
                .WithFlags(CommandFlags.Mutates).WithTimeout(60), ItemParent);
        }

        // ------------------------------------------------------------ maps

        static object Maps(CoreContext c)
        {
            string q = c.Args.Get("q", "") ?? "";
            int limit = c.Args.Get("limit", 50);
            var list = new List<object>();
            bool truncated = false;
            foreach (var m in c.Db.Maps.Enumerate())
            {
                if (q.Length > 0 && (m.Name == null || m.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                if (list.Count >= limit) { truncated = true; break; }
                var row = new BridgeReply().With("no", m.No).With("name", m.Name).With("id", m.Key.ToString()).With("guid", m.Guid);
                list.Add(row);
            }
            var r = new BridgeReply().With("maps", list);
            if (truncated) r["truncated"] = true;
            return r;
        }

        static object MapLoad(CoreContext c)
        {
            bool clear = c.Args.Get("clear", false);
            bool wait = c.Args.Get("wait", true);
            string mref;
            if (clear) mref = null;
            else
            {
                var m = CommandKit.ResolveMap(c);
                if (m == null) throw new BridgeException(BridgeError.Missing("no", "one of no, ref, name, clear"));
                mref = m.Key.ToString();
            }
            int findingsBefore = c.Session.Findings.Count;
            // M3: re-selecting the loaded map loads nothing and keeps its pose (HS2 resets
            // the pose); nothing is recorded
            bool reselect = mref != null && string.Equals(mref, c.Session.Model.Settings.Map.Ref, StringComparison.Ordinal);
            if (!reselect) CommandKit.Check(c.Session.SetMap(mref, CommandKit.Exec(c, "Map")));
            c.Runtime.ApplyNow();
            BridgeReply Reply()
            {
                // `id` cannot be a top-level reply member (the envelope's request id): the map's "guid#no" is `ref` here.
                var m = SceneCommands.MapReply(c, true);
                var r = new BridgeReply().With("no", m["no"]).With("name", m["name"]).With(CommandKit.RefArg, m["id"]).With("guid", m["guid"])
                    .With("state", m["state"]).With("loadMs", m["loadMs"]);
                // Phase 3.2: what the map offers and the main light
                var part = c.Runtime.GetPart<MapRealization>();
                bool loaded = part != null && part.State == MapState.Loaded;
                r["features"] = new BridgeReply().With("option", loaded && part.Behaviour.FeatureOption).With("light", loaded && part.Behaviour.FeatureLight);
                r["mainLight"] = new BridgeReply().With("name", HS2Next.MainLight.Current ? HS2Next.MainLight.Current.name : null).With("reason", HS2Next.MainLight.Reason);
                if (reselect) r["reselected"] = true;
                var fs = new List<SceneFinding>();
                for (int i = findingsBefore; i < c.Session.Findings.Count; i++)
                    if (c.Session.Findings[i].Code != null && c.Session.Findings[i].Code.StartsWith("map.", StringComparison.Ordinal)) fs.Add(c.Session.Findings[i]);
                // the part's own list of this load (the session merges repeated codes, Phase 3.2)
                var mp = c.Runtime.GetPart<MapRealization>();
                if (mp != null && !reselect) { fs.Clear(); fs.AddRange(mp.MapFindingsOfLoad); }
                r["findings"] = CommandKit.Findings(fs);
                if ((string)r["state"] == MapState.Unsupported.ToString())
                    c.Warn("maps load only in play mode or a player: the map is set in the scene and loads when it is opened there");
                return r;
            }
            if (!wait)
            {
                var r = Reply();
                r["note"] = "loading asynchronously; poll scene.map.no";
                return r;
            }
            if (!SceneCommands.MapBusy(c)) return Reply();
            var d = c.Ctx.Defer();
            SceneCommands.WhenMapSettled(c, d, Reply);
            return null;
        }

        // ------------------------------------------------------------ items

        static object ItemSearch(CoreContext c)
        {
            string q = c.Args.Get("q", "") ?? "";
            int limit = c.Args.Get("limit", 60);
            bool hasG = c.Args.TryGet<int>("group", out int g), hasC = c.Args.TryGet<int>("category", out int cat);
            var list = new List<object>();
            bool truncated = false;
            var db = c.Db;
            foreach (var grp in db.ItemGroups())
            {
                if (truncated) break;
                if (hasG && grp.Number != g) continue;
                foreach (var ct in db.CategoriesOf(grp))
                {
                    if (truncated) break;
                    if (hasC && ct.Number != cat) continue;
                    foreach (var it in db.ItemsIn(ct))
                    {
                        if (q.Length > 0 && (grp.Name + " / " + ct.Name + " / " + it.Name).IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (list.Count >= limit) { truncated = true; break; }
                        list.Add(new BridgeReply().With("group", it.Group).With("category", it.Category).With("no", it.No)
                            .With("groupName", grp.Name).With("categoryName", ct.Name).With("name", it.Name)
                            .With("id", it.Key.ToString()).With("guid", it.Guid));
                    }
                }
            }
            var r = new BridgeReply().With("items", list);
            if (truncated) r["truncated"] = true;
            return r;
        }

        static object ItemAdd(CoreContext c)
        {
            var row = CommandKit.ResolveItem(c);
            var parent = CommandKit.OptionalNode(c, "parent");
            string point = c.Args.Get<string>("point");
            if (string.IsNullOrEmpty(point)) point = null;
            if (point != null) point = PointName(c, "point", point);
            var res = CommandKit.Check(c.Runtime.Ops.AddItem(row, parent?.Id, point, -1, CommandKit.Exec(c, "Add item")));
            c.Runtime.ApplyNow();
            var added = new List<object>();
            foreach (var id in res.NewIds)
            {
                var n = c.Session.Model.Find(id);
                if (n != null) added.Add(CommandKit.Describe(c.App, n));
            }
            return new BridgeReply().With("added", added);
        }

        /// <summary>An attach point argument: a name of DocEnum.AttachPoints or its number "1"–"54"; bad_args with the N_* candidates otherwise.</summary>
        public static string PointName(CoreContext c, string arg, string text)
        {
            text = text.Trim();
            if (int.TryParse(text, out int v))
            {
                if (v >= 1 && v <= DocEnum.AttachPoints.Length) return DocEnum.AttachPoints[v - 1];
            }
            else
            {
                foreach (var p in DocEnum.AttachPoints) if (string.Equals(p, text, StringComparison.OrdinalIgnoreCase)) return p;
            }
            var cand = new List<string>();
            foreach (var p in DocEnum.AttachPoints)
                if (p.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) cand.Add(p);
            if (cand.Count == 0) cand.AddRange(DocEnum.AttachPoints);
            throw new BridgeException(BridgeCodes.BadArgs, "unknown attach point \"" + text + "\" (HS2Next knows the N_* names and their numbers 1-54)",
                new BridgeErrorDetail { Arg = arg, Expected = "N_* name or 1-54", Candidates = cand });
        }

        static object ItemParent(CoreContext c)
        {
            var n = CommandKit.Node(c, "key");
            var chara = CommandKit.OptionalNode(c, "chara");
            var parent = CommandKit.OptionalNode(c, "parent");
            if (chara != null && parent != null) throw CommandKit.BadArgs("parent", "one of chara, parent", "give chara (with point) or parent, not both");
            string point = c.Args.Get<string>("point");
            string parentId = null;
            string label = "";
            if (chara != null)
            {
                if (chara.Kind != NodeKind.Chara) throw new BridgeException(BridgeError.WrongKind("node " + chara.Id + " is a " + CommandKit.KindName(chara) + ", not a character", "chara", "chara"));
                if (!string.IsNullOrEmpty(point))
                {
                    point = PointName(c, "point", point);
                    parentId = chara.Id;
                    label = point;
                }
                else point = null;
            }
            else if (parent != null)
            {
                parentId = parent.Id;
                point = null;
                label = parent.Label;
            }
            else point = null;
            CommandKit.Check(c.Runtime.Ops.MoveKeepWorld(n.Id, parentId, point, -1, CommandKit.Exec(c, "Parent")), "key");
            c.Runtime.ApplyNow();
            var t = (c.Session.Model.Find(n.Id) ?? n).Doc.Transform;
            var r = new BridgeReply().With("parent", label).With("local", CommandKit.R4(t.Pos)).With("localRot", CommandKit.R4(t.Rot));
            if (parentId != null)
            {
                var a = c.Runtime.AttachTransformFor(parentId, point);
                if (a) r["bone"] = a.name;
            }
            return r;
        }
    }
}
