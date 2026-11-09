// The commands of scene scripts (group "script", owner core, Since 1.3.0, experimental): the scene's scripts, their roles, playing and recording them, and
// the store of scripts in the user's data.
//   script.list, script.get, script.set, script.remove, script.cast, script.play, script.stop, script.status, script.capture, script.borrow,
//   script.keep, script.add, script.import, script.export, script.delete, script.purge
// A scene script is named by `script`: its id, its name when one script has it, or its 0-based index; a store row by `no`; `id` stays the envelope's.
// What changes the scene's scripts goes through StudioOps (one history entry each, `undo: false` and a client's batch as every command); playing,
// stopping, the state and writing a loaded Timeline back are the script player's (ScriptPlayer). The store is not the scene: its changes make no entry.
// Errors: no such script not_found; a refused step or role bad_args naming it; over a limit bad_args with the limit's name; the store unwritable
// unavailable (script.storeWrite); a recording of another caller busy. script.set and script.import warn about a step or timed command whose command the
// app does not know (script.unknownCommand), a set-up or preparation step whose command answers later (script.deferringStep) and a set-up step that adds,
// removes, moves or swaps nodes, which clears the history at every play (script.clearsHistory). A play's `strict` is the envelope's: with it, a failed
// step stops the play before its Timeline is loaded. The generic commands of scripts (ScriptedCommands) register in the same group.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HS2Next.Api.Bridge;
using HS2Next.Bridge;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Scripts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.App.Commands
{
    public static class ScriptCommands
    {
        /// <summary>The commands of this file, in the order of the spec's table.</summary>
        public static readonly string[] Names =
        {
            "script.list", "script.get", "script.set", "script.remove", "script.cast", "script.play", "script.stop", "script.status", "script.capture",
            "script.borrow", "script.keep", "script.add", "script.import", "script.export", "script.delete", "script.purge",
        };

        /// <summary>CommandSpec.Since of the commands of the group "script" (these and the generic ones of ScriptedCommands): one constant.</summary>
        public const string Since = "1.3.0";
        /// <summary>Their help group.</summary>
        public const string GroupName = "script";

        /// <summary>The commands whose run with undo false clears the history (they add, remove, move or swap nodes, or load a pose): as a set-up step
        /// (sent with undo false at every play) they clear it. The list is the measured set (ScriptTest's YH2).</summary>
        public static readonly string[] StructuralCommands =
        {
            "bake.make", "camera.add", "chara.change", "chara.edit.load", "chara.load", "cut.make", "cut.marker", "delete", "item.add", "item.parent",
            "light.add", "obj.reorder", "pose.load", "ragdoll.follower", "route.add", "route.addPoint", "scene.import", "scene.load", "scene.new",
        };

        /// <summary>The values of script.play's prepare.</summary>
        public static readonly string[] PrepareModes = { ScriptPlayOptions.PrepareAuto, ScriptPlayOptions.PrepareAlways, ScriptPlayOptions.PrepareNever };
        /// <summary>The values of script.import's to.</summary>
        public static readonly string[] ImportTargets = { "store", "scene" };

        const string ScriptType = "a scene script's id, its name or its index";
        const string ScriptDoc = "a scene script: its id, its name when one script has it, or its index from 0";
        const string NoDoc = "a store row's number";
        const string Ext = SceneScript.FileExtension;

        static CommandSpec Spec(string name, string summary, CommandFlags flags) =>
            CommandKit.Spec(name, summary, GroupName).WithSince(Since).WithFlags(flags | CommandFlags.Experimental);

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, Spec("script.list", "The scene's scripts with their roles and cast, or with store the rows of the script store.", CommandFlags.ReadOnly)
                    .Arg("store", ArgTypes.Bool, "list the store's rows instead of the scene's scripts", false, false)
                    .Arg("q", ArgTypes.String, "text the name (or the id, or a row's number) holds, ignoring case; empty: every one", false, "")
                    .Arg("deleted", ArgTypes.Bool, "with store: also the deleted rows", false, false)
                    .Return("scripts", ArgTypes.ArrayOf(ArgTypes.Object), "the scene's scripts: {script, name, roles, cast, uncast, prepared, loaded, store}: roles the role names, cast {role: key} of the cast roles, uncast the others, prepared the preparation's mark or null, loaded true for the script the Timeline was loaded from, store the store row it came from or was kept as")
                    .Return("loaded", ArgTypes.String, "the script the Timeline was loaded from, or null")
                    .Return("rows", ArgTypes.ArrayOf(ArgTypes.Object), "with store: {no, id, name, created, modified, deleted}, times in UTC")
                    .Return("lastNumber", ArgTypes.Int, "with store: the last number given; numbers are never given again")
                    .Example("{\"cmd\":\"script.list\"}"),
                List);
            CommandKit.Register(r, app, Spec("script.get", "A scene script's document with its cast, or the document of a store row.", CommandFlags.ReadOnly)
                    .Arg("script", ArgTypes.Any, ScriptDoc)
                    .Arg("no", ArgTypes.Int, NoDoc + ", in place of script")
                    .Return("script", ArgTypes.Object, "the script's document (format hs2next.script): name, map, roles, setup, prepare, timeline")
                    .Return("cast", ArgTypes.Object, "{role: key}: the scene script's cast; empty for a store row")
                    .Return("prepared", ArgTypes.String, "the preparation's mark, null when it was not prepared")
                    .Return("loaded", ArgTypes.Bool, "the Timeline was loaded from this script")
                    .Example("{\"cmd\":\"script.get\",\"script\":0}"),
                Get);
            CommandKit.Register(r, app, Spec("script.set", "Puts a script document into the scene: a new script, or with replace the scene script of its id (which keeps its cast). Tracks and timed commands may be given in the form timeline.build takes. One history entry.", CommandFlags.Mutates)
                    .Arg("script", ArgTypes.Object, "the document: {format: \"hs2next.script\", version: 1, id, name, description, map, roles, setup, prepare, timeline}", true)
                    .Arg("replace", ArgTypes.Bool, "replace the scene script of the document's id instead of adding a new script", false, false)
                    .Return("script", ArgTypes.String, "the script's id")
                    .Return("roles", ArgTypes.Int, "its roles")
                    .Return("steps", ArgTypes.Int, "its set-up and preparation steps")
                    .Return("tracks", ArgTypes.Int, "its Timeline's tracks")
                    .Return("keys", ArgTypes.Int, "their keys")
                    .Return("events", ArgTypes.Int, "its timed commands")
                    .Example("{\"cmd\":\"script.set\",\"script\":{\"format\":\"hs2next.script\",\"version\":1,\"name\":\"Walk\",\"roles\":[{\"role\":\"a\",\"kind\":\"chara\"}],\"timeline\":{\"duration\":5,\"tracks\":[],\"events\":[]}}}"),
                Set);
            CommandKit.Register(r, app, Spec("script.remove", "Removes a script from the scene. When the Timeline was loaded from it, the Timeline stays as it is without its mark. One history entry.", CommandFlags.Mutates)
                    .Arg("script", ArgTypes.Any, ScriptDoc, true)
                    .Return("removed", ArgTypes.String, "the removed script's id")
                    .Example("{\"cmd\":\"script.remove\",\"script\":\"Walk\"}"),
                Remove);
            CommandKit.Register(r, app, Spec("script.cast", "Casts a scene script's roles to nodes of the scene: a character role to a character, a prop role to an item; null leaves a role uncast. One history entry.", CommandFlags.Mutates)
                    .Arg("script", ArgTypes.Any, ScriptDoc, true)
                    .Arg("roles", ArgTypes.Object, "{role: key or null}", true)
                    .Return("cast", ArgTypes.Object, "{role: key} of the cast roles after the change")
                    .Return("uncast", ArgTypes.ArrayOf(ArgTypes.String), "the roles not cast")
                    .Example("{\"cmd\":\"script.cast\",\"script\":0,\"roles\":{\"a\":\"<id>\"}}"),
                Cast);
            CommandKit.Register(r, app, Spec("script.play", "Plays a scene script: casts its roles (with load: loads the map and the cast that is missing), runs its preparation when it is due (one history entry), sends its set-up, loads its Timeline into the scene's (one history entry when it changes) and plays it, or records one pass with record. A failed step is reported and the play goes on; with the envelope's strict it stops before the Timeline is loaded.", CommandFlags.Mutates | CommandFlags.Slow | CommandFlags.Defers)
                    .WithTimeout(600)
                    .Arg("script", ArgTypes.Any, ScriptDoc, true)
                    .Arg("from", ArgTypes.Float, "the Timeline's time to play from", false, 0f)
                    .Arg("prepare", ArgTypes.Enum(PrepareModes), "auto: the preparation runs when it was not done for this cast and these steps; always; never", false, ScriptPlayOptions.PrepareAuto)
                    .Arg("load", ArgTypes.Bool, "load the script's map and the roles that are not in the scene", false, true)
                    .Arg("partial", ArgTypes.Bool, "play with roles uncast: the steps, timed commands and tracks that name them are left out", false, false)
                    .Arg("reload", ArgTypes.Bool, "load the script's Timeline again even when the scene's holds edits of it", false, false)
                    .Arg("discard", ArgTypes.Bool, "replace a Timeline that holds edits of another script", false, false)
                    .Arg("record", ArgTypes.Object, "record one pass instead of playing: record.start's arguments (path, width, height, fps, ...)")
                    .Return("script", ArgTypes.String, "the script's id")
                    .Return("roles", ArgTypes.Object, "{role: key} of the cast")
                    .Return("loaded", ArgTypes.Bool, "the Timeline was loaded (it changed)")
                    .Return("prepared", ArgTypes.Bool, "the preparation ran")
                    .Return("setup", ArgTypes.Object, "{sent, failed}")
                    .Return("prepare", ArgTypes.Object, "{sent, failed}")
                    .Return("skipped", ArgTypes.Int, "steps and timed commands left out for an uncast role")
                    .Return("failures", ArgTypes.ArrayOf(ArgTypes.Object), "{phase, index, cmd, code, message} of each failed step")
                    .Return("state", ArgTypes.String, "the Timeline's state: playing, paused, stopped, ended")
                    .Return("time", ArgTypes.Float, "the Timeline's time")
                    .Return("record", ArgTypes.Object, "with record: record.start's reply", false)
                    .Example("{\"cmd\":\"script.play\",\"script\":0}"),
                Play);
            CommandKit.Register(r, app, Spec("script.stop", "Stops the scene script: the Timeline stops and what the moves, the camera follow and the dof command hold is released; with reset the cast's ragdolls are released and the cast's stains dried. No history.", CommandFlags.Mutates)
                    .Arg("reset", ArgTypes.Bool, "release the cast's ragdolls and dry the cast's stains", false, true)
                    .Return("state", ArgTypes.String, "the Timeline's state")
                    .Return("released", ArgTypes.Int, "what the moves, the follow and the dof command held, released")
                    .Return("ragdolls", ArgTypes.Int, "cast characters whose ragdoll was released")
                    .Return("dried", ArgTypes.Int, "cast characters dried")
                    .Example("{\"cmd\":\"script.stop\"}"),
                Stop);
            CommandKit.Register(r, app, Spec("script.status", "The state of the scene script the Timeline was loaded from (else the one last played): the Timeline's state and time, the cast, the last play's failures, the timed commands fired, what the moves, the follow and the dof command hold, a recording.", CommandFlags.ReadOnly)
                    .Return("script", ArgTypes.String, "the script's id, or null")
                    .Return("name", ArgTypes.String, "its name")
                    .Return("state", ArgTypes.String, "the Timeline's state: none, stopped, playing, paused, ended")
                    .Return("time", ArgTypes.Float, "the Timeline's time")
                    .Return("duration", ArgTypes.Float, "the Timeline's length")
                    .Return("loop", ArgTypes.Bool, "the Timeline loops")
                    .Return("roles", ArgTypes.Object, "{role: key} of the cast roles")
                    .Return("uncast", ArgTypes.ArrayOf(ArgTypes.String), "the roles not cast")
                    .Return("prepared", ArgTypes.String, "the preparation's mark, or null")
                    .Return("lastPlay", ArgTypes.Object, "{setup, prepare, skipped, failures} of the last play, or null")
                    .Return("eventsFired", ArgTypes.Int, "timed commands fired")
                    .Return("eventErrors", ArgTypes.Int, "timed commands that failed")
                    .Return("lastEventError", ArgTypes.Object, "{event, code, message}, or null")
                    .Return("slots", ArgTypes.Object, "{moves, follow, dof}: the moves running, the camera follow and the dof command holding")
                    .Return("recording", ArgTypes.Bool, "a recording of the script runs")
                    .Example("{\"cmd\":\"script.status\"}"),
                Status);
            CommandKit.Register(r, app, Spec("script.capture", "Writes the edits of the loaded Timeline back into its script: track keys, focus nodes and timed commands' strings that name a cast node become the role again. One history entry.", CommandFlags.Mutates)
                    .Arg("script", ArgTypes.Any, ScriptDoc + "; absent: the one the Timeline was loaded from")
                    .Return("script", ArgTypes.String, "the script's id")
                    .Return("tracks", ArgTypes.Int, "its Timeline's tracks")
                    .Return("keys", ArgTypes.Int, "their keys")
                    .Return("events", ArgTypes.Int, "its timed commands")
                    .Example("{\"cmd\":\"script.capture\"}"),
                Capture);
            CommandKit.Register(r, app, Spec("script.borrow", "Borrows timed commands and track keys of a time window from another scene script or a store row into a scene script, moved by shift, roles renamed; camera and depth-of-field keys only with camera. A borrowed key at a time the track has replaces it. One history entry.", CommandFlags.Mutates)
                    .Arg("script", ArgTypes.Any, ScriptDoc + ": the script that borrows", true)
                    .Arg("from", ArgTypes.Any, "the scene script borrowed from (as script)")
                    .Arg("no", ArgTypes.Int, "the store row borrowed from, in place of from")
                    .Arg("window", ArgTypes.ArrayOf(ArgTypes.Float), "[t0, t1): the times borrowed; absent: all")
                    .Arg("shift", ArgTypes.Float, "seconds added to every borrowed time", false, 0f)
                    .Arg("roles", ArgTypes.Object, "{role of the source: role of the script}; a role left out keeps its name")
                    .Arg("events", ArgTypes.Bool, "borrow timed commands", false, true)
                    .Arg("tracks", ArgTypes.Bool, "borrow track keys", false, true)
                    .Arg("camera", ArgTypes.Bool, "also the camera and depth-of-field keys", false, false)
                    .Return("events", ArgTypes.Int, "timed commands borrowed")
                    .Return("keys", ArgTypes.Int, "keys borrowed")
                    .Return("tracks", ArgTypes.Int, "tracks that took keys, new ones included")
                    .Return("replaced", ArgTypes.Int, "keys of the script replaced by a borrowed key at their time")
                    .Example("{\"cmd\":\"script.borrow\",\"script\":0,\"from\":1,\"window\":[2,5],\"shift\":-2,\"roles\":{\"x\":\"a\"}}"),
                Borrow);
            CommandKit.Register(r, app, Spec("script.keep", "Keeps a scene script in the script store: the row of its id is replaced (same number), else a new row. No history.", CommandFlags.Mutates)
                    .Arg("script", ArgTypes.Any, ScriptDoc, true)
                    .Return("no", ArgTypes.Int, "the store row")
                    .Example("{\"cmd\":\"script.keep\",\"script\":0}"),
                Keep);
            CommandKit.Register(r, app, Spec("script.add", "Adds a copy of a store row to the scene as a new script. One history entry.", CommandFlags.Mutates)
                    .Arg("no", ArgTypes.Int, NoDoc, true)
                    .Return("script", ArgTypes.String, "the new scene script's id")
                    .Example("{\"cmd\":\"script.add\",\"no\":3}"),
                Add);
            CommandKit.Register(r, app, Spec("script.import", "Imports a script file (" + Ext + ", at most 16 MiB) into the store or into the scene; a script of the same id is replaced only with replace, else the copy gets a new id. One history entry with to scene.", CommandFlags.Mutates)
                    .Arg("path", ArgTypes.Path, "the file: relative to the output root, or absolute", true)
                    .Arg("to", ArgTypes.Enum(ImportTargets), "where the script goes", false, "store")
                    .Arg("replace", ArgTypes.Bool, "replace the store row or the scene script of the file's id", false, false)
                    .Return("no", ArgTypes.Int, "to store: the store row")
                    .Return("script", ArgTypes.String, "to scene: the scene script's id")
                    .Example("{\"cmd\":\"script.import\",\"path\":\"walk" + Ext + "\",\"to\":\"scene\"}"),
                Import);
            CommandKit.Register(r, app, Spec("script.export", "Writes a scene script (without its cast) or a store row to a script file. No history.", CommandFlags.WritesFiles)
                    .Arg("script", ArgTypes.Any, ScriptDoc)
                    .Arg("no", ArgTypes.Int, NoDoc + ", in place of script")
                    .Arg("path", ArgTypes.Path, "the file: relative to the output root, or absolute under the output root or the user data folder; " + Ext + " is added when missing; a file there is replaced", true)
                    .Return("path", ArgTypes.String, "the file written")
                    .Return("bytes", ArgTypes.Int, "its size")
                    .Example("{\"cmd\":\"script.export\",\"script\":0,\"path\":\"walk" + Ext + "\"}"),
                Export);
            CommandKit.Register(r, app, Spec("script.delete", "Hides a store row, or with restore shows it again; no file is touched. No history.", CommandFlags.Mutates)
                    .Arg("no", ArgTypes.Int, NoDoc, true)
                    .Arg("restore", ArgTypes.Bool, "show a deleted row again", false, false)
                    .Return("deleted", ArgTypes.Bool, "the row is deleted now")
                    .Example("{\"cmd\":\"script.delete\",\"no\":3}"),
                Delete);
            CommandKit.Register(r, app, Spec("script.purge", "Removes the files of the store's deleted rows and those rows; nothing else. Nothing is removed while the store's index cannot be read. No history.", CommandFlags.Mutates)
                    .Return("rows", ArgTypes.Int, "rows removed")
                    .Return("files", ArgTypes.Int, "files removed")
                    .Return("bytes", ArgTypes.Int, "their bytes")
                    .Example("{\"cmd\":\"script.purge\"}"),
                Purge);
        }

        // ------------------------------------------------------------ arguments

        /// <summary>The scene script of argument <paramref name="arg"/> (an id or a name as a string, an index as an integer); -1 when absent and not required.</summary>
        static int Index(CoreContext c, string arg = "script", bool required = true)
        {
            var ops = CommandKit.Ops(c);
            OpResult fail;
            int i;
            switch (c.Args.Kind(arg))
            {
                case ArgKind.String: i = ops.ScriptIndex(c.Args.Get<string>(arg), out fail, arg); break;
                case ArgKind.Integer: i = ops.ScriptIndex(c.Args.Get<int>(arg), out fail, arg); break;
                case ArgKind.Missing:
                case ArgKind.Null:
                    if (!required) return -1;
                    throw new BridgeException(BridgeError.Missing(arg, ScriptType));
                default: throw CommandKit.BadArgs(arg, ScriptType);
            }
            if (i < 0) CommandKit.Check(c, fail, arg);
            return i;
        }

        static int RequiredInt(CoreContext c, string arg) => CommandKit.OptInt(c, arg) ?? throw new BridgeException(BridgeError.Missing(arg, ArgTypes.Int));

        /// <summary>An argument's JSON (null when absent; JSON null as a null token). Dates stay strings, numbers doubles.</summary>
        static JToken Json(CoreContext c, string arg)
        {
            if (c.Args.Kind(arg) == ArgKind.Missing) return null;
            try
            {
                using (var sr = new StringReader(c.Args.RawJson(arg) ?? "null"))
                using (var jr = new JsonTextReader(sr) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double })
                    return JToken.ReadFrom(jr);
            }
            catch (Exception e) { throw CommandKit.BadArgs(arg, ArgTypes.Any, e.Message); }
        }

        /// <summary>The bus options of an edit whose label the operation chooses.</summary>
        static ExecuteOptions Exec(CoreContext c) => CommandKit.Exec(c, null);

        /// <summary>True when an edit made for this request is recorded (undo, or a client's batch is open).</summary>
        static bool Recorded(CoreContext c) => c.Undo || c.Session?.Commands?.ActiveTransaction != null;

        static string Utc(DateTime d) => d.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        static Dictionary<string, object> Map(IEnumerable<KeyValuePair<string, string>> m)
        {
            var d = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var kv in m) d[kv.Key] = kv.Value;
            return d;
        }

        // ------------------------------------------------------------ warnings of a script's steps

        /// <summary>
        /// The warnings of script.set and script.import about the steps and timed commands of <paramref name="s"/>: a command the app's registry does not
        /// know (script.unknownCommand), a set-up or preparation step whose command answers later (script.deferringStep), a set-up step that adds,
        /// removes, moves or swaps nodes (script.clearsHistory). None is refused.
        /// </summary>
        public static List<string> StepWarnings(AppCore app, SceneScript s)
        {
            var l = new List<string>();
            if (s == null || s.IsOpaque) return l;
            var server = app?.Bridge?.Server;
            var reg = server?.Commands;
            void Steps(List<JObject> steps, string phase)
            {
                if (steps == null) return;
                for (int i = 0; i < steps.Count; i++)
                {
                    string cmd = ScriptSteps.CommandOf(steps[i]);
                    if (cmd == null) continue;
                    string at = phase + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                    if (reg != null && !reg.Contains(cmd))
                        l.Add(ScriptFindings.UnknownCommand + ": " + at + ": " + cmd + " is not a command of this app; it fails when sent unless a plugin adds it");
                    else if (server != null && ScriptPlayer.AnswersLater(server, cmd))
                        l.Add(StudioOps.ScriptOpFindings.DeferringStep + ": " + at + ": " + cmd + " answers later; a step runs at once, so it fails unsent at every play");
                    if (phase == "setup" && Array.IndexOf(StructuralCommands, cmd) >= 0)
                        l.Add(ScriptFindings.ClearsHistory + ": " + at + ": " + cmd + " clears the history when it runs with undo false; as a set-up step it clears the history at every play");
                }
            }
            Steps(s.Setup, "setup");
            Steps(s.Prepare, "prepare");
            var events = s.Timeline?.Events;
            if (events != null && reg != null)
                for (int i = 0; i < events.Count; i++)
                {
                    string cmd = events[i]?.Command;
                    if (cmd != null && !reg.Contains(cmd))
                        l.Add(ScriptFindings.UnknownCommand + ": timeline.events[" + i.ToString(CultureInfo.InvariantCulture) + "]: " + cmd + " is not a command of this app; it fails when it fires unless a plugin adds it");
                }
            return l;
        }

        // ------------------------------------------------------------ handlers

        static object List(CoreContext c)
        {
            bool store = CommandKit.OptBool(c, "store") ?? false;
            string q = CommandKit.OptString(c, "q");
            bool deleted = CommandKit.OptBool(c, "deleted") ?? false;
            var ops = CommandKit.Ops(c);
            if (store)
            {
                var res = (ScriptStoreListResult)CommandKit.Check(c, ops.ScriptStoreList(q, deleted)).Value;
                var rows = new List<object>();
                foreach (var x in res.Rows)
                    rows.Add(new BridgeReply { ["no"] = x.No, ["id"] = x.Id, ["name"] = x.Name, ["created"] = Utc(x.Created), ["modified"] = Utc(x.Modified), ["deleted"] = x.Deleted });
                return new BridgeReply().With("rows", rows).With("lastNumber", res.LastNumber);
            }
            if (deleted) c.Warn("deleted: only the store has deleted rows (give store: true)");
            var sl = (ScriptListResult)CommandKit.Check(c, ops.ScriptList(q)).Value;
            var scripts = new List<object>();
            foreach (var s in sl.Scripts)
                scripts.Add(new BridgeReply
                {
                    ["script"] = s.Script, ["name"] = s.Name, ["roles"] = new List<string>(s.Roles), ["cast"] = Map(s.Cast), ["uncast"] = new List<string>(s.Uncast),
                    ["prepared"] = s.Prepared, ["loaded"] = s.Loaded, ["store"] = s.Store,
                });
            return new BridgeReply().With("scripts", scripts).With("loaded", sl.Loaded);
        }

        static object Get(CoreContext c)
        {
            var ops = CommandKit.Ops(c);
            if (FileRoots.OneOf(c, "script", "no") == "no")
            {
                int no = RequiredInt(c, "no");
                var doc = (SceneScript)CommandKit.Check(c, ops.ScriptStoreRead(no)).Value;
                return new BridgeReply().With("script", new RawJson(DocumentText(doc))).With("cast", new Dictionary<string, object>()).With("prepared", null).With("loaded", false);
            }
            var s = ops.ScriptAt(Index(c));
            string text = s.IsOpaque ? s.Raw.ToString(Formatting.None) : DocumentText(s.ToDocument());
            return new BridgeReply().With("script", new RawJson(text)).With("cast", Map(s.Cast)).With("prepared", s.Prepared).With("loaded", s.Id != null && s.Id == ops.LoadedScriptId);
        }

        static string DocumentText(SceneScript doc)
        {
            var b = doc.ToDocumentBytes();
            return System.Text.Encoding.UTF8.GetString(b, 0, b.Length - 1);
        }

        static object Set(CoreContext c)
        {
            if (c.Args.Kind("script") == ArgKind.Missing || c.Args.Kind("script") == ArgKind.Null) throw new BridgeException(BridgeError.Missing("script", ArgTypes.Object));
            if (c.Args.Kind("script") != ArgKind.Object) throw CommandKit.BadArgs("script", ArgTypes.Object, "script is a script document (an object)");
            var doc = (JObject)Json(c, "script");
            bool replace = CommandKit.OptBool(c, "replace") ?? false;
            var ops = CommandKit.Ops(c);
            var res = (ScriptSetResult)CommandKit.Check(c, ops.ScriptSet(doc, replace, c.Ctx.Strict, Exec(c))).Value;
            foreach (var w in StepWarnings(c.App, ops.ScriptAt(res.Index))) c.Warn(w);
            return new BridgeReply().With("script", res.Script).With("roles", res.Roles).With("steps", res.Steps).With("tracks", res.Tracks).With("keys", res.Keys).With("events", res.Events);
        }

        static object Remove(CoreContext c)
        {
            int i = Index(c);
            var r = CommandKit.Check(c, CommandKit.Ops(c).ScriptRemove(i, Exec(c)), "script");
            return new BridgeReply().With("removed", (string)r.Value);
        }

        static object Cast(CoreContext c)
        {
            int i = Index(c);
            var k = c.Args.Kind("roles");
            if (k == ArgKind.Missing || k == ArgKind.Null) throw new BridgeException(BridgeError.Missing("roles", ArgTypes.Object));
            if (!(Json(c, "roles") is JObject ro)) throw CommandKit.BadArgs("roles", ArgTypes.Object, "roles is {role: key or null}");
            var roles = new List<KeyValuePair<string, string>>();
            foreach (var p in ro.Properties())
            {
                string arg = "roles." + p.Name;
                if (p.Value.Type == JTokenType.Null) { roles.Add(new KeyValuePair<string, string>(p.Name, null)); continue; }
                if (p.Value.Type != JTokenType.String) throw CommandKit.BadArgs(arg, ArgTypes.Node, "keys are node ids (strings)");
                roles.Add(new KeyValuePair<string, string>(p.Name, CommandKit.NodeOf(c, arg, (string)p.Value).Id));
            }
            var res = (ScriptCastResult)CommandKit.Check(c, CommandKit.Ops(c).ScriptCast(i, roles, Exec(c)), "roles").Value;
            return new BridgeReply().With("cast", Map(res.Cast)).With("uncast", new List<string>(res.Uncast));
        }

        static object Play(CoreContext c)
        {
            int i = Index(c);
            string id = CommandKit.Ops(c).ScriptAt(i).Id;
            float? from = CommandKit.OptFloat(c, "from");
            if (from.HasValue) CommandKit.Finite(from.Value, "from");
            string prepare = CommandKit.OptString(c, "prepare") ?? ScriptPlayOptions.PrepareAuto;
            if (Array.IndexOf(PrepareModes, prepare) < 0) throw CommandKit.BadArgs("prepare", ArgTypes.Enum(PrepareModes));
            JObject record = null;
            var rk = c.Args.Kind("record");
            if (rk == ArgKind.Object) record = (JObject)Json(c, "record");
            else if (rk != ArgKind.Missing && rk != ArgKind.Null) throw CommandKit.BadArgs("record", ArgTypes.Object, "record is record.start's arguments (an object)");
            var o = new ScriptPlayOptions
            {
                From = from ?? 0f,
                Prepare = prepare,
                Load = CommandKit.OptBool(c, "load") ?? true,
                Partial = CommandKit.OptBool(c, "partial") ?? false,
                Strict = c.Ctx.Strict,
                Reload = CommandKit.OptBool(c, "reload") ?? false,
                Discard = CommandKit.OptBool(c, "discard") ?? false,
                Record = record,
                Undo = Recorded(c),
                Source = CommandSource.Bridge,
            };
            var run = ScriptPlayer.Play(c.App, id, o);
            if (run.IsDone) return PlayReply(c, run.Result);
            var d = c.Ctx.Defer();
            run.WhenDone(x =>
            {
                try
                {
                    foreach (var w in x.Result.Warnings) c.Warn(w);
                    if (x.Result.Error != null) d.Fail(x.Result.Error);
                    else d.Complete(x.Result.ToReply());
                }
                catch (Exception e) { d.Fail(e); }
            });
            return null;
        }

        static object PlayReply(CoreContext c, ScriptPlayResult res)
        {
            foreach (var w in res.Warnings) c.Warn(w);
            if (res.Error != null) throw new BridgeException(res.Error);
            return res.ToReply();
        }

        static object Stop(CoreContext c)
        {
            bool reset = CommandKit.OptBool(c, "reset") ?? true;
            var res = ScriptPlayer.Stop(c.App, reset);
            foreach (var w in res.Warnings) c.Warn(w);
            return res.ToReply();
        }

        static object Status(CoreContext c) => ScriptPlayer.Status(c.App);

        static object Capture(CoreContext c)
        {
            int i = Index(c, "script", false);
            string id = i >= 0 ? CommandKit.Ops(c).ScriptAt(i).Id : null;
            return ScriptPlayer.Capture(c.App, id, Recorded(c), CommandSource.Bridge);
        }

        static object Borrow(CoreContext c)
        {
            var ops = CommandKit.Ops(c);
            int target = Index(c);
            SceneScript source;
            if (FileRoots.OneOf(c, "from", "no") == "no") source = (SceneScript)CommandKit.Check(c, ops.ScriptStoreRead(RequiredInt(c, "no"))).Value;
            else source = ops.ScriptAt(Index(c, "from"));
            var req = new ScriptBorrowRequest { Source = source };
            var w = Json(c, "window");
            if (w != null && w.Type != JTokenType.Null)
            {
                if (!(w is JArray wa) || wa.Count != 2) throw CommandKit.BadArgs("window", "[t0, t1]", "window is two times [t0, t1)");
                for (int k = 0; k < 2; k++)
                    if (wa[k].Type != JTokenType.Float && wa[k].Type != JTokenType.Integer) throw CommandKit.BadArgs("window[" + k + "]", ArgTypes.Float);
                req.From = CommandKit.Finite((float)(double)wa[0], "window[0]");
                req.To = CommandKit.Finite((float)(double)wa[1], "window[1]");
                if (!(req.From.Value < req.To.Value)) throw CommandKit.BadArgs("window", "[t0, t1] with t0 < t1", "window " + wa.ToString(Formatting.None) + " holds no time");
            }
            float? shift = CommandKit.OptFloat(c, "shift");
            req.Shift = shift.HasValue ? CommandKit.Finite(shift.Value, "shift") : 0f;
            var rt = Json(c, "roles");
            if (rt != null && rt.Type != JTokenType.Null)
            {
                if (!(rt is JObject ro)) throw CommandKit.BadArgs("roles", ArgTypes.Object, "roles is {role of the source: role of the script}");
                req.Roles = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var p in ro.Properties())
                {
                    if (p.Value.Type != JTokenType.String) throw CommandKit.BadArgs("roles." + p.Name, "a role name");
                    req.Roles[p.Name] = (string)p.Value;
                }
            }
            req.Events = CommandKit.OptBool(c, "events") ?? true;
            req.Tracks = CommandKit.OptBool(c, "tracks") ?? true;
            req.Camera = CommandKit.OptBool(c, "camera") ?? false;
            var res = (ScriptBorrowResult)CommandKit.Check(c, ops.ScriptBorrow(target, req, Exec(c))).Value;
            return new BridgeReply().With("events", res.Events).With("keys", res.Keys).With("tracks", res.Tracks).With("replaced", res.Replaced);
        }

        static object Keep(CoreContext c)
        {
            int i = Index(c);
            var r = CommandKit.Check(c, CommandKit.Ops(c).ScriptKeep(i, Exec(c)), "script");
            return new BridgeReply().With("no", (int)r.Value);
        }

        static object Add(CoreContext c)
        {
            int no = RequiredInt(c, "no");
            var res = (ScriptSetResult)CommandKit.Check(c, CommandKit.Ops(c).ScriptAdd(no, Exec(c)), "no").Value;
            return new BridgeReply().With("script", res.Script);
        }

        static object Import(CoreContext c)
        {
            string text = CommandKit.OptString(c, "path", ArgTypes.Path) ?? throw new BridgeException(BridgeError.Missing("path", ArgTypes.Path));
            string to = CommandKit.OptString(c, "to") ?? ImportTargets[0];
            if (Array.IndexOf(ImportTargets, to) < 0) throw CommandKit.BadArgs("to", ArgTypes.Enum(ImportTargets));
            bool replace = CommandKit.OptBool(c, "replace") ?? false;
            string full = FileRoots.ForRead(c, "path", text, c.Output?.Root, new[] { Ext }, Ext);
            var r = CommandKit.Check(c, CommandKit.Ops(c).ScriptImport(full, to == "scene", replace, c.Ctx.Strict, Exec(c)), "path");
            var res = (ScriptImportResult)r.Value;
            foreach (var w in StepWarnings(c.App, res.Read)) c.Warn(w);
            return res.No != null ? new BridgeReply().With("no", res.No.Value) : new BridgeReply().With("script", res.Script);
        }

        static object Export(CoreContext c)
        {
            var ops = CommandKit.Ops(c);
            int? index = null, no = null;
            if (FileRoots.OneOf(c, "script", "no") == "no") no = RequiredInt(c, "no");
            else index = Index(c);
            string text = CommandKit.OptString(c, "path", ArgTypes.Path) ?? throw new BridgeException(BridgeError.Missing("path", ArgTypes.Path));
            string full = ExportPath(c, text);
            var res = (ScriptExportResult)CommandKit.Check(c, ops.ScriptExport(index, no, full), "path").Value;
            return new BridgeReply().With("path", res.Path).With("bytes", res.Bytes);
        }

        /// <summary>
        /// The file an export writes: relative to the output root, or absolute under the output root or the user data folder (as the recorder's folders);
        /// the extension added when missing; never under HS2's folders; io_denied otherwise.
        /// </summary>
        static string ExportPath(CoreContext c, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw CommandKit.BadArgs("path", ArgTypes.Path, "empty path");
            string named = FileRoots.WithExt(text, Ext);
            string root, rel;
            string outRoot = c.Output?.Root, ud = c.App?.Paths?.UserData;
            if (Path.IsPathRooted(named))
            {
                if (!Path.IsPathFullyQualified(named)) throw new BridgeException(BridgeError.IoDenied("a rooted path must start with its drive: " + named, "path"));
                string full;
                try { full = Path.GetFullPath(named); }
                catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
                {
                    throw new BridgeException(BridgeError.IoDenied("not a file path: " + e.Message, "path"));
                }
                root = Within(full, outRoot) ? outRoot : Within(full, ud) ? ud : null;
                if (root == null) throw new BridgeException(BridgeError.IoDenied("an absolute file must lie under the output root or the user data folder: " + full, "path"));
                rel = FileRoots.Relative(full, root);
            }
            else
            {
                if (string.IsNullOrEmpty(outRoot)) throw new BridgeException(BridgeError.IoDenied("no output root for a relative path", "path"));
                root = outRoot;
                rel = named.Replace('\\', '/');
            }
            string resolved;
            try { resolved = c.App.RootFor(root).Resolve(rel, overwrite: true, createDirectory: true); }
            catch (BridgeException e) when (e.Code == BridgeCodes.IoDenied || e.Code == BridgeCodes.BadArgs)
            {
                throw new BridgeException(e.Code, e.Error.Message, new BridgeErrorDetail { Arg = "path" });
            }
            string hs2 = c.App?.Paths?.Hs2Root;
            if (!string.IsNullOrEmpty(hs2) && Within(resolved, hs2)) throw new BridgeException(BridgeError.IoDenied("HS2's folders are never written: " + resolved, "path"));
            return resolved;
        }

        static bool Within(string full, string root)
        {
            if (string.IsNullOrEmpty(root)) return false;
            string r = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return Path.GetFullPath(full).StartsWith(r, StringComparison.OrdinalIgnoreCase);
        }

        static object Delete(CoreContext c)
        {
            int no = RequiredInt(c, "no");
            var r = CommandKit.Check(c, CommandKit.Ops(c).ScriptStoreDelete(no, CommandKit.OptBool(c, "restore") ?? false), "no");
            return new BridgeReply().With("deleted", r.Value is bool b && b);
        }

        static object Purge(CoreContext c)
        {
            var p = (ScriptPurgeResult)CommandKit.Check(c, CommandKit.Ops(c).ScriptPurge()).Value;
            return new BridgeReply().With("rows", p.Rows).With("files", p.Files).With("bytes", p.Bytes);
        }
    }
}
