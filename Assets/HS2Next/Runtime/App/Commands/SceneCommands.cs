// Scene and document commands: scene (HS2's map / camera / objects), scene.doc (the canonical
// texts a save would write now — the save → reload oracle), scene.new, scene.save, scene.load, scene.list, delete,
// obj.transform, undo, redo, history. Mutations go through the session and SceneOps with Source Bridge (CommandKit.Exec,
// one Tx per command); scene.new / scene.load clear the history by design.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using HS2Next.Api.Bridge;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS2Next.App.Commands
{
    public static class SceneCommands
    {
        public const string SceneExt = ".hs2nscene";
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("scene", "The scene in HS2's shape: map, view camera and every node (pre-order) with HS2Next additions.", "scene")
                .Return("map", ArgTypes.Object, "{no, name, id, guid, state}")
                .Return("camera", ArgTypes.Object, "camera.get's camera")
                .Return("objects", ArgTypes.ArrayOf(ArgTypes.Object), "{key, name, kind, pos, rot, scale, worldPos?, parentName?, fullname?, sex?, anime?, parent?, point?, state, visible}")
                .WithFlags(CommandFlags.ReadOnly).WithTimeout(60), Scene);

            CommandKit.Register(r, app, CommandKit.Spec("scene.doc", "The canonical documents a save would write now (scene.json, every character's chara.json / coord.json) with their SHA-256.", "scene")
                .Arg("key", ArgTypes.Node, "one node only (its node object and documents)")
                .Arg("capture", ArgTypes.Bool, "write the runtime-owned values (animation time, look, ...) into the model first, as a save does", defaultValue: true)
                .Arg("embed", ArgTypes.Bool, "documents as embedded JSON values instead of strings", defaultValue: false)
                .Return("title", ArgTypes.String, "scene title", false).Return("scene", ArgTypes.Any, "scene.json text (or object with embed)", false)
                .Return("charas", ArgTypes.Object, "{<node id>: {chara, coord} | null (card-backed)}", false)
                .Return("res", ArgTypes.ArrayOf(ArgTypes.String), "res/ names the characters' documents reference", false)
                .Return("node", ArgTypes.Any, "with key: the node object", false)
                .Return("sha256", ArgTypes.String, "hex SHA-256 of the UTF-8 texts in node order")
                .Return("nodes", ArgTypes.Int, "node count", false).Return("version", ArgTypes.Int, "model version", false).Return("dirty", ArgTypes.Bool, "unsaved changes", false)
                .WithFlags(CommandFlags.ReadOnly).WithTimeout(60), Doc);

            CommandKit.Register(r, app, CommandKit.Spec("scene.new", "An empty scene (history cleared, map unloaded, default view; runtime overrides cleared). Not undoable.", "scene")
                .Return("nodes", ArgTypes.Int, "0")
                .WithFlags(CommandFlags.Mutates).WithTimeout(60), New);

            CommandKit.Register(r, app, CommandKit.Spec("scene.save", "Saves the scene as .hs2nscene under <UserData>/scene/.", "scene")
                .Arg("name", ArgTypes.Path, "file name in <UserData>/scene/ (.hs2nscene appended unless present); one of name, path")
                .Arg("path", ArgTypes.Path, "relative path under <UserData>/scene/ (subfolders created); one of name, path")
                .Arg("overwrite", ArgTypes.Bool, "replace an existing file", defaultValue: false)
                .Arg("thumb", ArgTypes.Enum("render", "keep", "placeholder"), "thumbnail: render the view (needs a graphics device, else keep), keep the previous one, or the grey placeholder", defaultValue: "render")
                .Arg("title", ArgTypes.String, "sets the scene title first (recorded \"Title\")")
                .Arg("clock", ArgTypes.String, "ISO-8601 UTC time for createdUtc / modifiedUtc (byte-identical test saves)")
                .Return("path", ArgTypes.String, "absolute path").Return("name", ArgTypes.String, "relative to the scene folder ('/')")
                .Return("bytes", ArgTypes.Int, "file size").Return("thumbnail", ArgTypes.String, "mode used")
                .Return("findings", ArgTypes.ArrayOf(ArgTypes.Object), "{severity, code, path, message}")
                .Return("version", ArgTypes.Int, "model version").Return("dirty", ArgTypes.Bool, "false")
                .Example("{\"cmd\":\"scene.save\",\"name\":\"my scene\",\"overwrite\":true}")
                .WithFlags(CommandFlags.WritesFiles | CommandFlags.Slow).WithTimeout(120), Save);

            CommandKit.Register(r, app, CommandKit.Spec("scene.load", "Opens a .hs2nscene (history cleared); replies after the map has loaded unless wait is false.", "scene")
                .Arg("name", ArgTypes.Path, "file under <UserData>/scene/ (.hs2nscene appended when missing); one of name, path")
                .Arg("path", ArgTypes.Path, "relative path under <UserData>/scene/, or an absolute .hs2nscene path; one of name, path")
                .Arg("wait", ArgTypes.Bool, "reply after the map has loaded or failed (play mode / player)", defaultValue: true)
                .Return("path", ArgTypes.String, "absolute path").Return("nodes", ArgTypes.Int, "node count")
                .Return("map", ArgTypes.Object, "{no, name, id, state, loadMs}")
                .Return("findings", ArgTypes.ArrayOf(ArgTypes.Object), "load findings {severity, code, path, message}")
                .Return("newerFormat", ArgTypes.Bool, "written by a newer HS2Next").Return("missingRequiredExt", ArgTypes.Bool, "a required extension block is not known")
                .Return("built", ArgTypes.Int, "characters built").Return("failed", ArgTypes.Int, "characters not built")
                .WithFlags(CommandFlags.Mutates | CommandFlags.Slow | CommandFlags.Defers).WithTimeout(300), Load);

            CommandKit.Register(r, app, CommandKit.Spec("scene.list", "The .hs2nscene files under <UserData>/scene/ (recursive, by name).", "scene")
                .Arg("q", ArgTypes.String, "case-insensitive substring of the relative path", defaultValue: "")
                .Arg("limit", ArgTypes.Int, "at most this many", defaultValue: 200)
                .Return("scenes", ArgTypes.ArrayOf(ArgTypes.Object), "{name (relative, '/'), bytes, modifiedUtc}")
                .Return("truncated", ArgTypes.Bool, "more than limit", false)
                .WithFlags(CommandFlags.ReadOnly).WithTimeout(60), List);

            CommandKit.Register(r, app, CommandKit.Spec("delete", "Deletes nodes with their subtrees (one undo entry \"Delete\").", "scene")
                .Arg("key", ArgTypes.Node, "the node; one of key, keys")
                .Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "several nodes; one of key, keys")
                .Return("deleted", ArgTypes.ArrayOf(ArgTypes.String), "every id of the removed subtrees, pre-order")
                .WithFlags(CommandFlags.Mutates).WithTimeout(60), Delete);

            CommandKit.Register(r, app, CommandKit.Spec("obj.transform", "Sets a node's parent-local transform (one undo entry \"Transform\").", "scene")
                .Arg("key", ArgTypes.Node, "the node", required: true)
                .Arg("pos", ArgTypes.Vec3, "transform.pos (parent-local)")
                .Arg("rot", ArgTypes.Euler, "transform.rot (Euler degrees)")
                .Arg("scale", ArgTypes.Vec3, "transform.scale; kinds without a scale channel accept only [1, 1, 1]")
                .Return("object", ArgTypes.Object, "the node as in scene.objects")
                .Example("{\"cmd\":\"obj.transform\",\"key\":\"<id>\",\"pos\":[1,0,2],\"rot\":[0,30,0]}")
                .WithFlags(CommandFlags.Mutates).WithTimeout(60), Transform);

            CommandKit.Register(r, app, CommandKit.Spec("undo", "Undoes up to n history entries (busy inside a batch).", "scene")
                .Arg("n", ArgTypes.Int, "entries to undo (1-200)", defaultValue: 1)
                .Return("undone", ArgTypes.Int, "entries undone").Return("labels", ArgTypes.ArrayOf(ArgTypes.String), "their labels")
                .Return("canUndo", ArgTypes.Bool, "").Return("canRedo", ArgTypes.Bool, "").Return("version", ArgTypes.Int, "model version")
                .WithFlags(CommandFlags.Mutates).WithTimeout(60), c => UndoRedo(c, true));

            CommandKit.Register(r, app, CommandKit.Spec("redo", "Redoes up to n history entries (busy inside a batch).", "scene")
                .Arg("n", ArgTypes.Int, "entries to redo (1-200)", defaultValue: 1)
                .Return("redone", ArgTypes.Int, "entries redone").Return("labels", ArgTypes.ArrayOf(ArgTypes.String), "their labels")
                .Return("canUndo", ArgTypes.Bool, "").Return("canRedo", ArgTypes.Bool, "").Return("version", ArgTypes.Int, "model version")
                .WithFlags(CommandFlags.Mutates).WithTimeout(60), c => UndoRedo(c, false));

            CommandKit.Register(r, app, CommandKit.Spec("history", "The undo and redo history (labels, newest first) and the model version.", "scene")
                .Return("undo", ArgTypes.ArrayOf(ArgTypes.String), "undo labels, newest first").Return("redo", ArgTypes.ArrayOf(ArgTypes.String), "redo labels, next first")
                .Return("version", ArgTypes.Int, "model version").Return("dirty", ArgTypes.Bool, "unsaved changes")
                .WithFlags(CommandFlags.ReadOnly).WithTimeout(60), History);
        }

        // ------------------------------------------------------------ scene

        static object Scene(CoreContext c)
        {
            c.Runtime.ApplyNow();
            var objects = new List<object>();
            foreach (var n in c.Session.Model.PreOrder()) objects.Add(CommandKit.Describe(c.App, n));
            return new BridgeReply().With("map", MapReply(c, false)).With("camera", CommandKit.CameraState(c.App)).With("objects", objects);
        }

        /// <summary>The scene's map: {no (−1 none), name ("" none), id, guid, state[, loadMs]}.</summary>
        public static BridgeReply MapReply(CoreContext c, bool withLoadMs)
        {
            var r = new BridgeReply();
            ContentId.TryParse(c.Session.Model.Settings.Map.Ref, out var id);
            string name = "";
            if (!id.IsEmpty && c.Db != null && c.Db.TryGetMap(id, out var m) && m != null) name = m.Name ?? "";
            r["no"] = id.IsEmpty ? -1 : id.Id;
            r["name"] = name;
            r["id"] = id.IsEmpty ? null : id.ToString();
            r["guid"] = id.IsEmpty ? null : id.SourceGuid;
            var part = c.Runtime.GetPart<MapRealization>();
            r["state"] = (part?.State ?? MapState.None).ToString();
            if (withLoadMs) r["loadMs"] = part?.LoadMs ?? 0;
            return r;
        }

        // ------------------------------------------------------------ scene.doc

        static object Doc(CoreContext c)
        {
            var key = CommandKit.OptionalNode(c, "key");
            bool capture = c.Args.Get("capture", true), embed = c.Args.Get("embed", false);
            if (capture) c.Runtime.CaptureNow();
            using (var hash = new MemoryStream())
            {
                object Text(string s)
                {
                    if (s == null) return null;
                    var b = Utf8.GetBytes(s);
                    hash.Write(b, 0, b.Length);
                    return embed ? (object)new RawJson(s) : s;
                }
                var r = new BridgeReply();
                if (key != null)
                {
                    r["node"] = Text(c.Session.NodeJson(key.Id));
                    var st = key.Kind == NodeKind.Chara ? key.Appearance?.Stored : null;
                    if (st != null)
                    {
                        r["chara"] = Text(CharaText(st));
                        r["coord"] = Text(CoordText(st));
                    }
                    r["sha256"] = Sha256(hash);
                    return r;
                }
                var doc = c.Session.ExportDocument();
                r["title"] = c.Session.Model.Title ?? "";
                r["scene"] = Text(Utf8.GetString(CanonicalJsonWriter.ToBytes(SceneDocument.EntryName, doc.Write)));
                var charas = new BridgeReply();
                var res = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var n in c.Session.Model.PreOrder())
                {
                    if (n.Kind != NodeKind.Chara) continue;
                    var st = n.Appearance?.Stored;
                    if (st == null) { charas[n.Id] = null; continue; }
                    charas[n.Id] = new BridgeReply().With("chara", Text(CharaText(st))).With("coord", Text(CoordText(st)));
                    foreach (var x in st.ReferencedResources()) res.Add(x);
                }
                r["charas"] = charas;
                r["res"] = new List<string>(res);
                r["sha256"] = Sha256(hash);
                r["nodes"] = c.Session.Model.Count;
                r["version"] = c.Session.Commands.Version;
                r["dirty"] = c.Session.Commands.IsDirty;
                return r;
            }
        }

        static string CharaText(CharaSubtree t) => Utf8.GetString(CanonicalJsonWriter.ToBytes(CharaDocument.EntryName, t.Chara.Write));
        static string CoordText(CharaSubtree t) => Utf8.GetString(CanonicalJsonWriter.ToBytes(CoordDocument.EntryName, t.Coord.Write));

        static string Sha256(MemoryStream m)
        {
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(m.GetBuffer(), 0, (int)m.Length));
        }

        static string Hex(byte[] b)
        {
            var sb = new StringBuilder(b.Length * 2);
            foreach (var x in b) sb.Append(x.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        // ------------------------------------------------------------ scene.new / save / load / list

        static object New(CoreContext c)
        {
            c.ClearsHistory = true;
            try { c.Session.NewScene(); }
            catch (InvalidOperationException e) { throw new BridgeException(BridgeError.Busy(e.Message)); }
            c.Runtime.ApplyNow();
            return new BridgeReply().With("nodes", c.Session.Model.Count);
        }

        static object Save(CoreContext c)
        {
            bool overwrite = c.Args.Get("overwrite", false);
            string full = FileRoots.ForWrite(c, c.Paths.SceneDir, SceneExt, overwrite, out string rel);
            string thumb = c.Args.Get("thumb", "render");
            var o = new SaveOptions();
            switch (thumb)
            {
                case "render": o.Thumbnail = ThumbnailMode.Render; break;
                case "keep": o.Thumbnail = ThumbnailMode.Keep; break;
                case "placeholder": o.Thumbnail = ThumbnailMode.Placeholder; break;
                default: throw CommandKit.BadArgs("thumb", ArgTypes.Enum("render", "keep", "placeholder"));
            }
            if (c.Args.Has("clock"))
            {
                string s = c.Args.Get<string>("clock");
                if (!DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t))
                    throw CommandKit.BadArgs("clock", "ISO-8601 time", "clock is not an ISO-8601 time: " + s);
                o.Clock = () => t;
            }
            if (c.Args.Has("title"))
                CommandKit.Check(c.Session.Set(null, "title", c.Args.Get<string>("title"), CommandKit.Exec(c, "Title")), "title");
            string used = thumb;
            if (o.Thumbnail == ThumbnailMode.Render && (c.Runtime.Ops.ThumbnailRenderer == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null))
                used = "keep";
            var res = c.Runtime.Ops.Save(full, o);
            if (!res.Ok)
                throw new BridgeException(BridgeCodes.BadArgs, "save failed: " + res.Error.Message,
                    new BridgeErrorDetail { Arg = c.Args.Has("name") ? "name" : "path", Extra = new Dictionary<string, object> { ["code"] = res.Error.Code.ToString() } });
            return new BridgeReply().With("path", full).With("name", rel).With("bytes", res.Size).With("thumbnail", used)
                .With("findings", CommandKit.Findings(res.Findings)).With("version", c.Session.Commands.Version).With("dirty", c.Session.Commands.IsDirty);
        }

        static object Load(CoreContext c)
        {
            c.ClearsHistory = true;
            string arg = FileRoots.OneOf(c, "name", "path");
            string text = c.Args.Get<string>(arg);
            if (arg == "name" && (text.IndexOf('/') >= 0 || text.IndexOf('\\') >= 0))
                throw CommandKit.BadArgs("name", "a file name", "name is a file name; give subfolders or absolute paths with path");
            string full = FileRoots.ForRead(c, arg, text, c.Paths.SceneDir, new[] { SceneExt }, SceneExt);
            bool wait = c.Args.Get("wait", true);
            LoadResult<SceneContainer> lr;
            try { lr = c.Runtime.Ops.Load(full); }
            catch (InvalidOperationException e) { throw new BridgeException(BridgeError.Busy(e.Message)); }
            if (!lr.Ok)
                throw new BridgeException(BridgeCodes.BadArgs, "load failed: " + lr.Error.Message,
                    new BridgeErrorDetail { Arg = arg, Extra = new Dictionary<string, object> { ["code"] = lr.ErrorCode?.ToString() } });
            BridgeReply Reply()
            {
                int built = 0, failed = 0;
                foreach (var n in c.Session.Model.PreOrder())
                {
                    if (n.Kind != NodeKind.Chara) continue;
                    if (c.Runtime.FindChara(n.Id)?.State == RealizationState.Built) built++; else failed++;
                }
                var m = MapReply(c, true);
                m.Remove("guid");
                return new BridgeReply().With("path", full).With("nodes", c.Session.Model.Count).With("map", m)
                    .With("findings", CommandKit.Findings(lr.Findings)).With("newerFormat", lr.NewerFormat).With("missingRequiredExt", lr.MissingRequiredExt)
                    .With("built", built).With("failed", failed);
            }
            if (!wait || !MapBusy(c)) return Reply();
            var d = c.Ctx.Defer();
            WhenMapSettled(c, d, Reply);
            return null;
        }

        /// <summary>True while the map part still has a load to do or in flight.</summary>
        public static bool MapBusy(CoreContext c) => c.Runtime.GetPart<MapRealization>()?.IsBusy ?? false;

        /// <summary>Completes <paramref name="d"/> with <paramref name="reply"/> at the first pump the map is no longer busy (re-armed each frame).</summary>
        public static void WhenMapSettled(CoreContext c, IDeferredReply d, Func<object> reply)
        {
            var frames = c.Frames;
            void Poll()
            {
                if (d.IsCompleted || d.IsAbandoned) return;
                if (!MapBusy(c))
                {
                    try { d.Complete(reply()); }
                    catch (Exception e) { d.Fail(e); }
                    return;
                }
                frames.AtUpdate(1, Poll);
            }
            frames.AtUpdate(1, Poll);
        }

        static object List(CoreContext c)
        {
            string q = c.Args.Get("q", "") ?? "";
            int limit = c.Args.Get("limit", 200);
            if (limit < 0) throw CommandKit.BadArgs("limit", "≥ 0");
            var dir = c.Paths.SceneDir;
            var rows = new List<(string name, FileInfo fi)>();
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*" + SceneExt, SearchOption.AllDirectories))
                {
                    if (!f.EndsWith(SceneExt, StringComparison.OrdinalIgnoreCase)) continue;
                    string rel = FileRoots.Relative(f, dir);
                    if (q.Length > 0 && rel.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    rows.Add((rel, new FileInfo(f)));
                }
            rows.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            var list = new List<object>();
            foreach (var x in rows)
            {
                if (list.Count >= limit) break;
                list.Add(new BridgeReply().With("name", x.name).With("bytes", x.fi.Length)
                    .With("modifiedUtc", x.fi.LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)));
            }
            var r = new BridgeReply().With("scenes", list);
            if (rows.Count > list.Count) r["truncated"] = true;
            return r;
        }

        // ------------------------------------------------------------ delete, obj.transform

        static object Delete(CoreContext c)
        {
            // any kind; duplicates kept, an empty keys array deletes nothing
            var nodes = CommandKit.Keys(c, CommandKit.KeyKind.Any, out bool many, CommandKit.KeysRule.KeepDuplicates | CommandKit.KeysRule.AllowEmpty);
            string arg = many ? "keys" : "key";
            var removed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var n in nodes) foreach (var x in c.Session.Model.Subtree(n)) removed.Add(x.Id);
            var order = new List<string>();
            foreach (var x in c.Session.Model.PreOrder()) if (removed.Contains(x.Id)) order.Add(x.Id);
            var ids = new List<string>();
            foreach (var n in nodes) ids.Add(n.Id);
            CommandKit.Check(c.Runtime.Ops.Delete(ids, CommandKit.Exec(c, "Delete")), arg);
            return new BridgeReply().With("deleted", order);
        }

        static object Transform(CoreContext c)
        {
            var n = CommandKit.Node(c, "key");
            Vector3? pos = c.Args.TryGet<Vector3>("pos", out var p) ? p : (Vector3?)null;
            Vector3? rot = c.Args.TryGet<Vector3>("rot", out var q) ? q : (Vector3?)null;
            Vector3? scale = c.Args.TryGet<Vector3>("scale", out var sc) ? sc : (Vector3?)null;
            CommandKit.Check(c, CommandKit.Ops(c).SetTransform(new[] { n.Id }, pos, rot, scale, CommandKit.Exec(c, StudioOps.Labels.Transform)));
            return new BridgeReply().With("object", CommandKit.Describe(c.App, c.Session.Model.Find(n.Id) ?? n));
        }

        // ------------------------------------------------------------ history

        static object UndoRedo(CoreContext c, bool undo)
        {
            int n = c.Args.Get("n", 1);
            if (n < 1 || n > 200) throw CommandKit.BadArgs("n", "1-200");
            var bus = c.Session.Commands;
            var labels = new List<string>();
            for (int i = 0; i < n; i++)
            {
                var list = undo ? bus.UndoList : bus.RedoList;
                string label = list.Count > 0 ? list[0].Label : null;
                var r = undo ? bus.Undo() : bus.Redo();
                foreach (var w in bus.TakeWarnings()) c.Warn(w);
                CommandKit.Check(r);
                if (r.Status == CommandStatus.NoOp) break;
                labels.Add(label);
            }
            c.Runtime.ApplyNow();
            return new BridgeReply().With(undo ? "undone" : "redone", labels.Count).With("labels", labels)
                .With("canUndo", bus.CanUndo).With("canRedo", bus.CanRedo).With("version", bus.Version);
        }

        static object History(CoreContext c)
        {
            var bus = c.Session.Commands;
            var u = new List<string>();
            foreach (var e in bus.UndoList) u.Add(e.Label);
            var rd = new List<string>();
            foreach (var e in bus.RedoList) rd.Add(e.Label);
            return new BridgeReply().With("undo", u).With("redo", rd).With("version", bus.Version).With("dirty", bus.IsDirty);
        }
    }
}
