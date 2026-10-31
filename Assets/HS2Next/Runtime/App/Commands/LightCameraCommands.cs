// Phase 3.3 package A commands: Studio lights (light.add / state / set), the chara light
// (charaLight.set), camera slots and camera objects (camera.slots / slot / add / activate) and the sound frame
// (sound.state / set / list). Every `set` map is validated before anything is applied (all-or-nothing; bad_args naming the
// path) and every mutation is one history entry; camera.slot load is a view change (not recorded, as camera.set). The writes
// of light.set, charaLight.set, camera.slot and sound.set are StudioOps methods (Phase 3.5 extraction).
// Ranges are not clamped (HS2's setters and Import do not clamp; the 3.5 UI clamps).
using System;
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace HS2Next.App.Commands
{
    public static class LightCameraCommands
    {
        public static readonly IReadOnlyList<string> Names = new[]
        {
            "light.add", "light.state", "light.set", "charaLight.set", "camera.slots", "camera.slot", "camera.add", "camera.activate",
            "sound.state", "sound.set", "sound.list",
        };

        static readonly string[] LightMembers = StudioOps.LightMembers;
        static readonly string[] CharaLightMembers = StudioOps.CharaLightMembers;
        static readonly string[] SoundMembers = StudioOps.SoundMembers;

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("light.add", "Adds a Studio light of a Light list row (one undo entry \"Add light\").", "lights/cameras/sound")
                .Arg(CommandKit.RefArg, ArgTypes.ContentId, "the Light row \"guid#no\"; one of ref, no")
                .Arg("no", ArgTypes.Int, "a vanilla Light row (0-8: All / Chara / Map x directional / point / spot)")
                .Arg("parent", ArgTypes.Node, "parent node (default the scene root)")
                .Arg("point", ArgTypes.String, "attach point when parent is a character (N_*)")
                .Arg("pos", ArgTypes.Vec3, "local position (default the scene's placement rule)")
                .Arg("rot", ArgTypes.Euler, "local rotation (Euler degrees)")
                .Return("key", ArgTypes.String, "the new node id").Return("ref", ArgTypes.ContentId, "the row")
                .Return("type", ArgTypes.String, "Directional / Point / Spot (null when the prefab did not build)").Return("target", ArgTypes.String, "all / chara / map")
                .Example("{\"cmd\":\"light.add\",\"no\":2,\"pos\":[0,15,5],\"rot\":[60,180,0]}")
                .WithFlags(CommandFlags.Mutates).WithTimeout(60), Add);

            CommandKit.Register(r, app, CommandKit.Spec("light.state", "A Studio light: its row, its state section and the realized Light (folded values, original values, masks, main light, helpers).", "lights/cameras/sound")
                .Arg("key", ArgTypes.Node, "the light node", required: true)
                .Return("key", ArgTypes.String, "").Return("ref", ArgTypes.ContentId, "")
                .Return("row", ArgTypes.Object, "{no, name, type, target} (null when the row is missing)")
                .Return("state", ArgTypes.Object, "the light section as in scene.doc")
                .Return("realized", ArgTypes.Object, "{state, path, type, enabled, color, intensity, original {color, intensity}, range, spotAngle, shadows, shadowStrength, cullingMask, prefabCullingMask, renderingLayers, mainLight, helpersShown, helpersVisible} or null")
                .WithFlags(CommandFlags.ReadOnly).WithTimeout(60), State);

            CommandKit.Register(r, app, CommandKit.Spec("light.set", "Sets light-section fields of one or several Studio lights (validated all-or-nothing, one undo entry).", "lights/cameras/sound")
                .Arg("key", ArgTypes.Node, "the light node")
                .Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "several light nodes (one entry for all)")
                .Arg("set", ArgTypes.Object, "{color: [r,g,b,a], intensity, range, spotAngle, shadow, enabled, showTarget}", required: true)
                .Arg("label", ArgTypes.String, "history label (default \"Light\")")
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "canonical paths that changed (on any key)")
                .Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "paths that already had the value (on every key)")
                .Example("{\"cmd\":\"light.set\",\"key\":\"<id>\",\"set\":{\"intensity\":1.5,\"range\":30}}")
                .WithFlags(CommandFlags.Mutates).WithTimeout(60), Set);

            CommandKit.Register(r, app, CommandKit.Spec("charaLight.set", "Sets the chara light (scene.charaLight: color, intensity, rot [x, y], shadow); validated all-or-nothing, one undo entry.", "lights/cameras/sound")
                .Arg("set", ArgTypes.Object, "{color: [r,g,b,a], intensity, rot: [x, y], shadow}", required: true)
                .Arg("label", ArgTypes.String, "history label (default \"Chara light\")")
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "").Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "")
                .Example("{\"cmd\":\"charaLight.set\",\"set\":{\"rot\":[30,45],\"intensity\":0.8}}")
                .WithFlags(CommandFlags.Mutates).WithTimeout(60), CharaLightSet);

            CommandKit.Register(r, app, CommandKit.Spec("camera.slots", "The 10 saved views of the scene (null = an empty slot, which loads the reset view).", "lights/cameras/sound")
                .Return("slots", ArgTypes.ArrayOf(ArgTypes.Object), "Camera {pivot, rot, distance, fov} or null, 10 entries")
                .Return("reset", ArgTypes.Object, "the reset view")
                .WithFlags(CommandFlags.ReadOnly).WithTimeout(60), Slots);

            CommandKit.Register(r, app, CommandKit.Spec("camera.slot", "Saves the current view into a slot (one undo entry \"Camera slot\") or loads a slot into the view (not recorded).", "lights/cameras/sound")
                .Arg("index", ArgTypes.Int, "0-9", required: true)
                .Arg("op", ArgTypes.Enum("save", "load"), "save or load", required: true)
                .Return("camera", ArgTypes.Object, "the view saved or loaded {pivot, rot, distance, fov}")
                .WithFlags(CommandFlags.Mutates).WithTimeout(60), Slot);

            CommandKit.Register(r, app, CommandKit.Spec("camera.add", "Adds a camera object (one undo entry \"Add camera\"); with active, it also becomes the active camera.", "lights/cameras/sound")
                .Arg("parent", ArgTypes.Node, "parent node (default the scene root)")
                .Arg("pos", ArgTypes.Vec3, "local position").Arg("rot", ArgTypes.Euler, "local rotation (Euler degrees)")
                .Arg("name", ArgTypes.String, "node name (default the numbered camera name)")
                .Arg("active", ArgTypes.Bool, "activate it", defaultValue: false)
                .Return("key", ArgTypes.String, "the new node id")
                .WithFlags(CommandFlags.Mutates).WithTimeout(60), CameraAdd);

            CommandKit.Register(r, app, CommandKit.Spec("camera.activate", "Makes one camera object the active camera (the others inactive) or, with key null, releases the view (one undo entry).", "lights/cameras/sound")
                .Arg("key", ArgTypes.Node, "the camera node; null releases (required)")
                .Return("active", ArgTypes.String, "the active camera node or null")
                .WithFlags(CommandFlags.Mutates).WithTimeout(60), Activate);

            CommandKit.Register(r, app, CommandKit.Spec("sound.state", "The BGM / user-audio state with the resolved row and file (state only; no playback).", "lights/cameras/sound")
                .Return("bgm", ArgTypes.Object, "{ref, name, play, repeat, resolved}").Return("userAudio", ArgTypes.Object, "{file, exists, path, play, repeat}")
                .Return("playing", ArgTypes.Bool, "false (no playback in v0.2)").Return("channel", ArgTypes.String, "what HS2 would play: bgm, userAudio or null")
                .WithFlags(CommandFlags.ReadOnly).WithTimeout(60), SoundState);

            CommandKit.Register(r, app, CommandKit.Spec("sound.set", "Sets scene.sound fields with HS2's one-channel rule (bgm.play true clears userAudio.play and the reverse); one undo entry.", "lights/cameras/sound")
                .Arg("set", ArgTypes.Object, "{\"bgm.ref\": \"guid#no\" | null, \"bgm.play\", \"bgm.repeat\": none|all, \"userAudio.file\": {user|hs2: path} | null, \"userAudio.play\", \"userAudio.repeat\"}", required: true)
                .Arg("label", ArgTypes.String, "history label (default \"Sound\")")
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "").Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "")
                .WithFlags(CommandFlags.Mutates).WithTimeout(60), SoundSet);

            CommandKit.Register(r, app, CommandKit.Spec("sound.list", "The BGM rows (the Studio list's visible rows) and the user-audio *.wav files (HS2Next UserData/audio, then the HS2 install's).", "lights/cameras/sound")
                .Arg("kind", ArgTypes.Enum("bgm", "audio"), "only one list")
                .Return("bgm", ArgTypes.ArrayOf(ArgTypes.Object), "{ref, no, name, source}").Return("audio", ArgTypes.ArrayOf(ArgTypes.Object), "{name, file, source}")
                .WithFlags(CommandFlags.ReadOnly).WithTimeout(60), SoundList);
        }

        // ------------------------------------------------------------ helpers

        static string UserDir(CoreContext c) => c.Services?.UserData ?? c.Paths?.UserData;
        static string Hs2Root(CoreContext c) => c.Services?.Hs2Root ?? c.Paths?.Hs2Root;

        static TreeNode LightNode(CoreContext c, string arg, TreeNode n) => CommandKit.KeyKind.Light.Check(n, arg);

        static string TargetName(StudioLightTarget t) => t == StudioLightTarget.Chara ? "chara" : t == StudioLightTarget.Map ? "map" : "all";

        /// <summary>
        /// Validates a `set` map against a section: each name (with or without the section prefix) must be a field path under one
        /// of <paramref name="members"/> and its JSON must convert to the field's type. Returns (canonical path, value).
        /// </summary>
        static List<(string path, object value)> Plan(CoreContext c, string nodeId, string section, string[] members, string arg = "set")
        {
            if (c.Args.Kind(arg) != ArgKind.Object) throw CommandKit.BadArgs(arg, ArgTypes.Object);
            var set = c.Args.Nested(arg);
            if (set == null || set.Names.Count == 0) throw CommandKit.BadArgs(arg, ArgTypes.Object, "set names no field");
            var plan = new List<(string, object)>();
            var model = c.Session.Model;
            foreach (var name in set.Names)
            {
                var bad = StudioOps.SectionPath(section, members, name, arg, out string full);
                if (bad != null) CommandKit.Check(c, bad);
                object v;
                try { v = FieldJson.FromText(set.RawJson(name), FieldAccessor.LeafType(model, nodeId, full)); }
                catch (CommandException e) { throw new BridgeException(e.Code == CommandCodes.NotFound ? BridgeCodes.BadArgs : e.Code, arg + "." + name + ": " + e.Message, new BridgeErrorDetail { Arg = arg + "." + name }); }
                catch (Exception e) { throw CommandKit.BadArgs(arg + "." + name, "the save-format JSON form of " + full, e.Message); }
                if (v is float f && (float.IsNaN(f) || float.IsInfinity(f))) throw CommandKit.BadArgs(arg + "." + name, "a finite number");
                plan.Add((full, v));
            }
            return plan;
        }

        /// <summary>The changed / noop reply of a section set.</summary>
        static BridgeReply Reply(OpResult r) => new BridgeReply().With("changed", r.Changed).With("noop", r.NoOp);

        static object Json(object docValue) => docValue == null ? null : new RawJson(FieldJson.ToText(docValue));

        // ------------------------------------------------------------ light.add / state / set

        static StudioLight ResolveLight(CoreContext c)
        {
            var db = c.Db;
            if (db == null) throw CommandKit.Unsupported("no Studio lists");
            string arg = FileRoots.OneOf(c, CommandKit.RefArg, "no");
            ContentId id;
            if (arg == "no") id = ContentId.Vanilla(c.Args.Get<int>("no"));
            else
            {
                var text = c.Args.Get<string>(CommandKit.RefArg);
                if (!ContentId.TryParse(text, out id) || id.IsEmpty) throw CommandKit.BadArgs(CommandKit.RefArg, ArgTypes.ContentId, "bad content id \"" + text + "\"");
            }
            if (db.TryGetLight(id, out var row) && row != null) return row;
            var cand = new List<string>();
            foreach (var l in db.Lights.Enumerate()) if (cand.Count < 12) cand.Add(l.Key + " " + l.Name);
            throw CommandKit.NotFound("no Light row " + id, arg, cand);
        }

        static object Add(CoreContext c)
        {
            var row = ResolveLight(c);
            var parent = CommandKit.OptionalNode(c, "parent");
            string point = c.Args.Get<string>("point");
            if (string.IsNullOrEmpty(point)) point = null;
            if (point != null) point = MapItemCommands.PointName(c, "point", point);
            bool hasPos = c.Args.TryGet<Vector3>("pos", out var pos), hasRot = c.Args.TryGet<Vector3>("rot", out var rot);
            string id;
            c.Runtime.ApplyNow();
            using (var tx = CommandKit.Tx(c, "Add light"))
            {
                var res = CommandKit.Check(c.Session.AddLight(row.Key.ToString(), parent?.Id, point, -1, CommandKit.Exec(c, "Add light")));
                id = res.NewIds != null && res.NewIds.Count > 0 ? res.NewIds[0] : null;
                if (id == null) throw new BridgeException(BridgeCodes.Internal, "the light was not added");
                if (hasPos) CommandKit.Set(tx, id, "transform.pos", UnityObjects.V(pos), "pos");
                if (hasRot) CommandKit.Set(tx, id, "transform.rot", UnityObjects.V(rot), "rot");
                CommandKit.Commit(tx);
            }
            c.Runtime.ApplyNow();
            var real = c.Runtime.Find(id) as LightRealization;
            var r = new BridgeReply().With("key", id).With("ref", row.Key.ToString())
                .With("type", real?.Light ? real.Light.type.ToString() : null).With("target", TargetName(row.TargetKind));
            return r;
        }

        static object State(CoreContext c)
        {
            var n = LightNode(c, "key", CommandKit.Node(c, "key"));
            c.Runtime.ApplyNow();
            var st = n.State as LightNodeState;
            var real = c.Runtime.Find(n.Id) as LightRealization;
            StudioLight row = null;
            if (st != null && ContentId.TryParse(st.Ref, out var id) && !id.IsEmpty) c.Db?.TryGetLight(id, out row);
            var r = new BridgeReply().With("key", n.Id).With("ref", st?.Ref);
            r["row"] = row == null ? null : new BridgeReply().With("no", row.No).With("name", row.Name)
                .With("type", real?.Light ? real.Light.type.ToString() : null).With("target", TargetName(row.TargetKind));
            r["state"] = Json(st);
            r["realized"] = Realized(real);
            return r;
        }

        /// <summary>The realized part of light.state (also scene.lights' studio.lights entries).</summary>
        public static BridgeReply Realized(LightRealization real)
        {
            if (real == null) return null;
            var rr = new BridgeReply().With("state", real.State.ToString());
            var l = real.Light;
            if (!l) return rr;
            var o = BundleLights.OriginalOf(l);
            var t = l.transform;
            var parts = new List<string>();
            for (var x = t; x; x = x.parent) parts.Add(x.name);
            parts.Reverse();
            rr.With("path", string.Join("/", parts)).With("type", l.type.ToString()).With("enabled", l.enabled)
                .With("color", CommandKit.C(l.color)).With("intensity", (double)l.intensity)
                .With("original", o ? new BridgeReply().With("color", CommandKit.C(o.Color)).With("intensity", (double)o.Intensity) : null)
                .With("range", (double)l.range).With("spotAngle", (double)l.spotAngle).With("shadows", l.shadows.ToString())
                .With("shadowStrength", (double)l.shadowStrength).With("cullingMask", l.cullingMask).With("prefabCullingMask", real.PrefabCullingMask)
                .With("renderingLayers", l.TryGetComponent<UniversalAdditionalLightData>(out var ld) ? (object)unchecked((uint)ld.renderingLayers) : null)
                .With("mainLight", MainLight.Current == l)
                .With("position", CommandKit.D3(t.position)).With("forward", CommandKit.D3(t.forward))
                .With("helpersShown", real.Helpers?.Shown ?? false).With("helpersVisible", real.Helpers?.Visible ?? false);
            return rr;
        }

        static object Set(CoreContext c)
        {
            var ids = CommandKit.Ids(CommandKit.Keys(c, CommandKit.KeyKind.Light));
            var plan = Plan(c, ids[0], "light", LightMembers);
            return Reply(CommandKit.Check(c, CommandKit.Ops(c).SetLight(ids, plan, CommandKit.Exec(c, c.Args.Get<string>("label")))));
        }

        // ------------------------------------------------------------ charaLight.set

        static object CharaLightSet(CoreContext c)
        {
            var plan = Plan(c, null, "charaLight", CharaLightMembers);
            return Reply(CommandKit.Check(c, CommandKit.Ops(c).SetCharaLight(plan, CommandKit.Exec(c, c.Args.Get<string>("label")))));
        }

        // ------------------------------------------------------------ camera slots and objects

        static object Slots(CoreContext c)
        {
            var slots = c.Session.Model.Settings.Camera.Slots ?? new List<CameraView>();
            var l = new List<object>();
            for (int i = 0; i < Math.Max(CameraState.SlotCount, slots.Count); i++) l.Add(i < slots.Count ? Json(slots[i]) : null);
            return new BridgeReply().With("slots", l).With("reset", Json(SceneViewCamera.ResetView()));
        }

        static object Slot(CoreContext c)
        {
            int index = c.Args.Require<int>("index");
            if (index < 0 || index >= CameraState.SlotCount) throw CommandKit.BadArgs("index", "0-9");
            string op = c.Args.Require<string>("op");
            if (op == "save")
                return new BridgeReply().With("camera", Json(CommandKit.Check(c, CommandKit.Ops(c).SaveCameraSlot(index, CommandKit.Exec(c, StudioOps.Labels.CameraSlot))).Value));
            if (op == "load")   // an empty slot = the reset view (Q-i); a view change
                return new BridgeReply().With("camera", Json(CommandKit.Check(c, CommandKit.Ops(c).LoadCameraSlot(index, CommandKit.Exec(c, StudioOps.Labels.Camera))).Value));
            throw CommandKit.BadArgs("op", ArgTypes.Enum("save", "load"));
        }

        static object CameraAdd(CoreContext c)
        {
            var parent = CommandKit.OptionalNode(c, "parent");
            bool hasPos = c.Args.TryGet<Vector3>("pos", out var pos), hasRot = c.Args.TryGet<Vector3>("rot", out var rot);
            string name = c.Args.Get<string>("name");
            bool active = c.Args.Get("active", false);
            string id;
            c.Runtime.ApplyNow();
            using (var tx = CommandKit.Tx(c, "Add camera"))
            {
                var res = CommandKit.Check(c.Session.AddCamera(parent?.Id, null, -1, CommandKit.Exec(c, "Add camera")));
                id = res.NewIds != null && res.NewIds.Count > 0 ? res.NewIds[0] : null;
                if (id == null) throw new BridgeException(BridgeCodes.Internal, "the camera was not added");
                if (hasPos) CommandKit.Set(tx, id, "transform.pos", UnityObjects.V(pos), "pos");
                if (hasRot) CommandKit.Set(tx, id, "transform.rot", UnityObjects.V(rot), "rot");
                if (!string.IsNullOrEmpty(name)) CommandKit.Set(tx, id, "name", name, "name");
                if (active) CommandKit.Check(c.Session.SetActiveCamera(id, CommandKit.Exec(c, "Add camera")), "active");
                CommandKit.Commit(tx);
            }
            c.Runtime.ApplyNow();
            c.Runtime.LateApply();
            return new BridgeReply().With("key", id);
        }

        static object Activate(CoreContext c)
        {
            var k = c.Args.Kind("key");
            if (k == ArgKind.Missing) throw new BridgeException(BridgeError.Missing("key", ArgTypes.Node));
            string id = null;
            if (k != ArgKind.Null)
            {
                id = CommandKit.KeyKind.Camera.Check(CommandKit.Node(c, "key"), "key").Id;
            }
            CommandKit.Check(c.Session.SetActiveCamera(id, CommandKit.Exec(c, id == null ? "Release camera" : "Active camera")));
            c.Runtime.ApplyNow();
            c.Runtime.LateApply();
            return new BridgeReply().With("active", c.Runtime.GetPart<SceneViewCamera>()?.ActiveCameraId);
        }

        // ------------------------------------------------------------ sound

        static object SoundState(CoreContext c)
        {
            c.Runtime.ApplyNow();
            var s = c.Session.Model.Settings.Sound;
            var row = SceneSound.Bgm(c.Db, s.Bgm.Ref);
            string user = UserDir(c), hs2 = Hs2Root(c);
            bool exists = SceneSound.FileExists(s.UserAudio.File, user, hs2);
            string channel = s.Bgm.Play && s.UserAudio.Play ? SceneSound.LoadWinner(s, user, hs2)
                : s.UserAudio.Play && exists ? "userAudio" : s.Bgm.Play && row != null ? "bgm" : null;
            return new BridgeReply()
                .With("bgm", new BridgeReply().With("ref", s.Bgm.Ref).With("name", row?.Name).With("play", s.Bgm.Play).With("repeat", s.Bgm.Repeat).With("resolved", row != null))
                .With("userAudio", new BridgeReply().With("file", Json(s.UserAudio.File)).With("exists", exists).With("path", SceneSound.PathOf(s.UserAudio.File, user, hs2))
                    .With("play", s.UserAudio.Play).With("repeat", s.UserAudio.Repeat))
                .With("playing", false).With("channel", channel);
        }

        static object SoundSet(CoreContext c)
        {
            var plan = Plan(c, null, "sound", SoundMembers);
            // HS2's one channel: starting one stops the other (one entry with the set; StudioOps.SetSound)
            return Reply(CommandKit.Check(c, CommandKit.Ops(c).SetSound(plan, CommandKit.Exec(c, c.Args.Get<string>("label")), UserDir(c), Hs2Root(c))));
        }

        static object SoundList(CoreContext c)
        {
            string kind = c.Args.Get<string>("kind");
            if (kind != null && kind != "bgm" && kind != "audio") throw CommandKit.BadArgs("kind", ArgTypes.Enum("bgm", "audio"));
            var r = new BridgeReply();
            if (kind == null || kind == "bgm")
            {
                var l = new List<object>();
                if (c.Db != null)
                    foreach (var b in c.Db.Bgm.Enumerate())
                        l.Add(new BridgeReply().With("ref", b.Key.ToString()).With("no", b.No).With("name", b.Name).With("source", b.Zipmod != null ? "mod" : "vanilla"));
                r["bgm"] = l;
            }
            if (kind == null || kind == "audio")
            {
                var l = new List<object>();
                foreach (var f in SceneSound.AudioFiles(UserDir(c), Hs2Root(c)))
                    l.Add(new BridgeReply().With("name", f.Name).With("file", Json(f.Ref)).With("source", f.Source));
                r["audio"] = l;
            }
            return r;
        }
    }
}
