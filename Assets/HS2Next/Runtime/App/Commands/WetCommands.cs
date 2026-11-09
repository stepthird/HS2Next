// The commands of stains (group "wet", owner core, Since 1.3.0, experimental): a character's stains and puddles, and the height of the map under a point.
//   wet.attach, .stain, .status, .use   the names, arguments and replies scene scripts use
//   wet.start, .dry, .set, .keep, .remove, .floor
//   ground.probe                        the map's surface under points
// A key names a character; with none, the character of the last wet.attach, else the selection's first character. A stain is named by the member `name`. Writes go through
// StudioOps (one history entry each, `undo: false` and a client's batch as every property command), so a timed command leaves the history alone. A point is [x, y, z] or
// {key, bone, offset}; `from` also takes an item's key, whose far end towards the target starts the ray, which runs past the target by half. Without a graphics device
// reads and what needs no bake work, and a bake answers unsupported.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Api.Bridge;
using HS2Next.Api.Unity;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Wet;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HS2Next.App.Commands
{
    public static class WetCommands
    {
        /// <summary>The commands of this file (CoreCommands.Phase66Names).</summary>
        public static readonly string[] Names =
        {
            "wet.attach", "wet.stain", "wet.status", "wet.use", "wet.start", "wet.dry", "wet.set", "wet.keep", "wet.remove", "wet.floor", "ground.probe",
        };

        /// <summary>The commands of this file that keep the automation vocabulary of scene scripts (CoreCommands.ParityNames).</summary>
        public static readonly string[] ParityNames = { "wet.attach", "wet.stain", "wet.status", "wet.use", "ground.probe" };

        /// <summary>CommandSpec.Since of these commands.</summary>
        public const string Since66 = "1.3.0";
        /// <summary>Their help group.</summary>
        public const string GroupName = "wet";

        /// <summary>What a reply's status says of a call that did what it was asked.</summary>
        const string Done = "ok";
        const string KeyDoc = "a character; absent: the character of the last wet.attach, else the selection's first character";
        const string NameDoc = "a stain of the character by its name";
        const string FromDoc = "where the ray starts: [x, y, z], {key, bone, offset}, {key, accessory} (the muzzle chara.accessoryAxis reads for that accessory slot of a character), or an item's key (its far end towards the target); alone, the ray goes to the character's hips";
        const string TargetDoc = "what the ray aims at: [x, y, z] or {key, bone, offset}; alone, the ray comes from 1.5 units in front of it; the ray runs past it by half";
        const string PresetDoc = "water or blood";
        const string RowDoc = "{key, current, t, running, radius, targets, unreadable, tiled, noFilm, puddles, floor, floorSource, maps {live, dormant}}";

        static CommandSpec Spec(string name, string summary, CommandFlags flags) =>
            CommandKit.Spec(name, summary, GroupName).WithSince(Since66).WithFlags(flags | CommandFlags.Experimental);

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, Spec("wet.attach", "Clears the character's stains and puddles, then adds one stain, dry, from where the ray first meets the character, or from the default origin with neither from nor target; one history entry. A ray that meets nothing changes nothing. The character becomes the one a command without a key means.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, KeyDoc)
                    .Arg("from", ArgTypes.Any, FromDoc)
                    .Arg("target", ArgTypes.Any, TargetDoc)
                    .Arg("preset", ArgTypes.String, PresetDoc, false, WetPreset.Water)
                    .Return("status", ArgTypes.String, "ok")
                    .Return("floor", ArgTypes.Float, "the height drops land on, as last measured: here at the attach, under the character; absent when nothing gives one", false)
                    .Return("stain", ArgTypes.String, "the stain's name")
                    .Example("{\"cmd\":\"wet.attach\",\"key\":\"<id>\",\"preset\":\"water\"}"),
                Attach);
            CommandKit.Register(r, app, Spec("wet.stain", "Adds a named stain, dry, or replaces the one of that name, on top of what the character has; one history entry. With nothing but a name that a stain has, that stain is applied again on what the character wears now, for the parts without a live map. Values given by wet.set while the character had no stain count here unless an argument says otherwise.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, KeyDoc)
                    .Arg("name", ArgTypes.String, "the stain's name, at most 64 characters", false, WetEdits.DefaultName)
                    .Arg("preset", ArgTypes.String, PresetDoc, false, WetPreset.Water)
                    .Arg("from", ArgTypes.Any, FromDoc)
                    .Arg("target", ArgTypes.Any, TargetDoc)
                    .Arg("radius", ArgTypes.Float, "how far it spreads, in units", false, WetEdits.ScriptRadius)
                    .Arg("spread", ArgTypes.Float, "the seconds it takes", false, WetEdits.ScriptSpread)
                    .Arg("tail", ArgTypes.Float, "the seconds the dripping takes to end after the spread", false, WetEdits.ScriptTail)
                    .Arg("skinDrips", ArgTypes.Bool, "skin and face shed drops too", false, false)
                    .Return("status", ArgTypes.String, "ok")
                    .Example("{\"cmd\":\"wet.stain\",\"key\":\"<id>\",\"name\":\"first\",\"preset\":\"blood\",\"target\":{\"key\":\"<id>\",\"bone\":\"cf_J_Spine03\",\"offset\":[0,0,0.2]},\"from\":[0,10,10]}"),
                Stain);
            CommandKit.Register(r, app, Spec("wet.status", "How the stains stand: with a key that character, with none every character that has a stain.", CommandFlags.ReadOnly)
                    .Arg("key", ArgTypes.Node, "a character")
                    .Return("stains", ArgTypes.ArrayOf(ArgTypes.String), "the names of the stains of the characters below, each once")
                    .Return("characters", ArgTypes.ArrayOf(ArgTypes.Object), RowDoc + ": current the name of the current stain or null, t its clock in seconds, radius its radius now, targets the parts that show a stain, unreadable the parts that cannot show one, tiled the parts a tiled image leaves out, noFilm the clothes with colour and no gloss, puddles, floor the height drops land on, as last measured (when the character's stains change or one starts, when the character is built or loaded, when the map changes, and while drops fall once the origin moved more than 0.05 units, at most every 0.25 s of scene time): while no stain runs it keeps the last measured value; it is not saved, and a load measures it again in the loaded pose; null with floorSource none, floorSource map, feet, fixed or none, maps.live the maps that act and maps.dormant those kept for a part that is not there")
                    .Example("{\"cmd\":\"wet.status\"}"),
                Status);
            CommandKit.Register(r, app, Spec("wet.use", "The named stain becomes current; with start it runs from 0. What is on the cloth stays.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, KeyDoc)
                    .Arg("name", ArgTypes.String, NameDoc, true)
                    .Arg("start", ArgTypes.Bool, "run it from 0", false, true)
                    .Return("status", ArgTypes.String, "ok")
                    .Example("{\"cmd\":\"wet.use\",\"name\":\"first\"}"),
                Use);
            CommandKit.Register(r, app, Spec("wet.start", "A stain runs from 0. Its clock is the scene clock: the pause stops it, a step steps it, a Timeline seek moves nothing.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, KeyDoc)
                    .Arg("name", ArgTypes.String, NameDoc + "; absent: the current stain")
                    .Arg("loop", ArgTypes.Bool, "spread, drip, hold, dry, again; absent: as it is")
                    .Return("t", ArgTypes.Float, "the stain's clock")
                    .Example("{\"cmd\":\"wet.start\",\"loop\":true}"),
                Start);
            CommandKit.Register(r, app, Spec("wet.dry", "A stain goes back to 0 and stops; its drops go, and with puddles its puddles. What a kept stain froze stays.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, KeyDoc)
                    .Arg("name", ArgTypes.String, NameDoc + "; absent: every stain that is not kept")
                    .Arg("puddles", ArgTypes.Bool, "their puddles go too", false, true)
                    .Example("{\"cmd\":\"wet.dry\",\"puddles\":false}"),
                Dry);
            CommandKit.Register(r, app, Spec("wet.set", "Sets values of a stain or of the character's block in one history entry: by the names maxRadius, spreadTime, dripTail, dripRate, puddleGrowth, puddleScale and showMarker, or by the member's path, as look.amount, shape.up, drips.color, puddle.edge, origin.down, hold, loop, preset, film.gloss, mirror.scale, targets.hair, floor.offset. Numbers are stored as given; the ranges act when a value is applied. A change of shape or origin bakes the stain again. While the character has no stain the values wait for its next wet.attach or wet.stain; they are not saved. A kept stain takes no value. The clock, the maps and the lists have their own commands.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, KeyDoc)
                    .Arg("name", ArgTypes.String, NameDoc + "; absent: the current stain")
                    .Arg("values", ArgTypes.Object, "name or path: value", true)
                    .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "the names given that changed something")
                    .Example("{\"cmd\":\"wet.set\",\"values\":{\"maxRadius\":3,\"look.amount\":0.6}}"),
                Set);
            CommandKit.Register(r, app, Spec("wet.keep", "Freezes the current stain as a layer: its clock stops where it is, it takes no further edit and its puddles keep their colour. A new current stain with the same origin and values starts dry on top.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, KeyDoc)
                    .Return("stain", ArgTypes.String, "the new stain's name")
                    .Example("{\"cmd\":\"wet.keep\"}"),
                Keep);
            CommandKit.Register(r, app, Spec("wet.remove", "Takes a stain and its puddles away, or with all the character's whole block.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, KeyDoc)
                    .Arg("name", ArgTypes.String, NameDoc + "; absent: the current stain")
                    .Arg("all", ArgTypes.Bool, "every stain and puddle, and the block's switches", false, false)
                    .Return("removed", ArgTypes.Int, "stains removed")
                    .Example("{\"cmd\":\"wet.remove\",\"all\":true}"),
                Remove);
            CommandKit.Register(r, app, Spec("wet.floor", "Reads the height drops land on, or sets it: map (the map's surface under the origin as it stands, else the feet), feet or fixed; then the offset. The puddles move by the difference; a character that moves moves none. A fixed floor with no height takes the floor of now. While the character has no stain the values wait, as those of wet.set.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, KeyDoc)
                    .Arg("mode", ArgTypes.Enum("map", "feet", "fixed"), "how the height is found")
                    .Arg("offset", ArgTypes.Float, "added to the height in every mode")
                    .Arg("height", ArgTypes.Float, "the world height of a fixed floor")
                    .Return("y", ArgTypes.Float, "the height now, offset included")
                    .Return("source", ArgTypes.String, "map, feet, fixed or none")
                    .Example("{\"cmd\":\"wet.floor\",\"mode\":\"feet\",\"offset\":0.1}"),
                Floor);
            CommandKit.Register(r, app, Spec("ground.probe", "The highest upward surface of the map under each point, between its height plus above and its height minus below. Characters and items are not the map.", CommandFlags.ReadOnly)
                    .Arg("points", ArgTypes.ArrayOf(ArgTypes.Any), "each [x, z] or [x, y, z]", true)
                    .Arg("y", ArgTypes.Float, "the height of a point given as [x, z]", false, 0f)
                    .Arg("above", ArgTypes.Float, "how far above a point the search starts", false, 6f)
                    .Arg("below", ArgTypes.Float, "how far below a point it ends", false, 30f)
                    .Return("hits", ArgTypes.ArrayOf(ArgTypes.Object), "{x, z, y, normal} per point: y and normal are null where nothing answers")
                    .Return("source", ArgTypes.String, "what the first answer came from: gpu, mesh, terrain or collider; null with no answer")
                    .Example("{\"cmd\":\"ground.probe\",\"points\":[[0,0],[1,0,2]]}"),
                Probe);
        }

        // ------------------------------------------------------------ arguments

        // The character of a command: a key, else the one a command without a key means.
        static TreeNode Character(CoreContext c)
        {
            var n = CommandKit.OptionalNode(c, "key");
            if (n != null) return CommandKit.KeyKind.Chara.Check(n, "key");
            string id = CommandKit.Ops(c).WetDefaultCharacter();
            if (id == null) throw CommandKit.NotFound("no character: give a key or select one", "key");
            return c.Session.Model.Find(id);
        }

        // The id of the stain the argument `name` gives; null when it is absent.
        static string StainOf(CoreContext c, TreeNode n)
        {
            string name = CommandKit.OptString(c, "name");
            if (name == null) return null;
            return CommandKit.Ops(c).WetStainId(n.Id, name) ?? throw CommandKit.NotFound("no stain \"" + name + "\" on this character", "name");
        }

        internal static JToken Raw(CoreContext c, string arg)
        {
            if (CommandKit.Absent(c, arg)) return null;
            try { return JToken.Parse(c.Args.RawJson(arg)); }
            catch (Exception e) { throw CommandKit.BadArgs(arg, ArgTypes.Any, e.Message); }
        }

        internal static Vector3 V(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        // A member of a nested object that is not known: a warning, or bad_args under strict.
        internal static void UnknownMember(CoreContext c, string arg, string name, string expected)
        {
            if (c.Ctx.Strict) throw CommandKit.BadArgs(arg + "." + name, expected, "unknown member '" + name + "'");
            c.Warn("unknown member '" + name + "' of " + arg + " ignored");
        }

        // [x, y, z] or {key, bone, offset}: a world point. A bone's offset is in the bone's space, a node's in the node's. A character named with no bone takes defaultBone when one is given.
        // A `from` may also be {key, accessory}: the muzzle of that accessory slot of the character (chara.accessoryAxis).
        internal static Vector3 PointOf(CoreContext c, string arg, JToken t, bool from, string defaultBone = null)
        {
            if (t is JArray) return V(AdvancedPoseCommands.JsonVec3(t, arg).Value);
            if (!(t is JObject o)) throw CommandKit.BadArgs(arg, "[x, y, z] or {key, bone, offset}");
            if (from && o.Property("accessory") != null) return AccessoryPoint(c, arg, o);
            foreach (var m in o.Properties())
                if (m.Name != "key" && m.Name != "bone" && m.Name != "offset") UnknownMember(c, arg, m.Name, "key, bone or offset");
            if (o["key"]?.Type != JTokenType.String) throw new BridgeException(BridgeError.Missing(arg + ".key", ArgTypes.Node));
            var n = CommandKit.NodeOf(c, arg + ".key", (string)o["key"]);
            string bone = null;
            if (o["bone"] != null && o["bone"].Type != JTokenType.Null)
            {
                if (o["bone"].Type != JTokenType.String) throw CommandKit.BadArgs(arg + ".bone", ArgTypes.String);
                bone = (string)o["bone"];
            }
            if (bone == null && n.Kind == NodeKind.Chara) bone = defaultBone;
            var offset = V(AdvancedPoseCommands.JsonVec3(o["offset"], arg + ".offset") ?? Vec3.Zero);
            c.Runtime.ApplyNow();
            var end = ConstraintResolver.Source(c.Runtime, new ConstraintEnd(n.Id, bone, null));
            if (end.State == ConstraintEndState.Ok && end.Transform) return end.Transform.TransformPoint(offset);
            if (end.State == ConstraintEndState.Pending) throw CommandKit.NotRealized("node " + n.Id + " is not built", arg + ".key", "Pending");
            throw CommandKit.NotFound(bone != null ? "node " + n.Id + " has no bone \"" + bone + "\"" : "node " + n.Id + " has no place in the scene", bone != null ? arg + ".bone" : arg + ".key");
        }

        // {key, accessory}: the muzzle of a character's accessory slot.
        static Vector3 AccessoryPoint(CoreContext c, string arg, JObject o)
        {
            foreach (var m in o.Properties())
                if (m.Name != "key" && m.Name != "accessory") UnknownMember(c, arg, m.Name, "key or accessory");
            if (o["key"]?.Type != JTokenType.String) throw new BridgeException(BridgeError.Missing(arg + ".key", ArgTypes.Node));
            var a = o["accessory"];
            if (a == null || a.Type != JTokenType.Integer) throw CommandKit.BadArgs(arg + ".accessory", ArgTypes.Int, "an accessory slot number");
            long slot = (long)a;
            if (slot < 0 || slot > int.MaxValue) throw CommandKit.BadArgs(arg + ".accessory", "a slot number not below 0");
            var n = CommandKit.NodeOf(c, arg + ".key", (string)o["key"]);
            return ScriptedCommands.Muzzle(c, arg, n, (int)slot);
        }

        // An item's end that lies furthest towards the target.
        static Vector3 FarEnd(CoreContext c, TreeNode n, Vector3 target)
        {
            c.Runtime.ApplyNow();
            if (!(c.Runtime.Find(n.Id) is ItemRealization it) || it.State != RealizationState.Built || !it.Instance)
                throw CommandKit.NotRealized("item " + n.Id + " is not built", "from", "NotRealized");
            bool any = false;
            var box = new Bounds();
            foreach (var r in it.Instance.GetComponentsInChildren<Renderer>())
            {
                if (!any) { box = r.bounds; any = true; }
                else box.Encapsulate(r.bounds);
            }
            if (!any) return it.Instance.transform.position;
            var dir = (target - box.center).normalized;
            var e = box.extents;
            return box.center + dir * (Mathf.Abs(dir.x) * e.x + Mathf.Abs(dir.y) * e.y + Mathf.Abs(dir.z) * e.z);
        }

        internal static Vector3 FromOf(CoreContext c, JToken t, Vector3 target)
        {
            if (t.Type != JTokenType.String) return PointOf(c, "from", t, true);
            var n = CommandKit.KeyKind.Item.Check(CommandKit.NodeOf(c, "from", (string)t), "from");
            return FarEnd(c, n, target);
        }

        static WetFrame FrameOf(CoreContext c, TreeNode n)
        {
            var r = CommandKit.Built(c, n);
            if (!WetTargets.Frame(r.Context, out var f)) throw CommandKit.Unsupported("wet.noFrame: this body has no frame for a stain");
            return f;
        }

        // The arguments from and target as a ray and its length; false with neither.
        static bool RayOf(CoreContext c, TreeNode n, out Ray ray, out float length)
        {
            ray = default;
            length = StudioOps.WetRayLength;
            JToken from = Raw(c, "from"), target = Raw(c, "target");
            if (from == null && target == null) return false;
            var aim = target != null ? PointOf(c, "target", target, false) : WetTargets.V(FrameOf(c, n).Mid);
            var start = from != null ? FromOf(c, from, aim) : aim + WetTargets.V(FrameOf(c, n).Forward) * 1.5f;
            var d = aim - start;
            float span = d.magnitude;
            if (!(span > 1e-5f)) throw CommandKit.BadArgs("target", "a point apart from from", "from and target are the same point");
            ray = new Ray(start, d / span);
            length = span * 1.5f;
            return true;
        }

        static object Plain(JToken t)
        {
            switch (t.Type)
            {
                case JTokenType.Integer: return (long)t;
                case JTokenType.Float: return (double)t;
                case JTokenType.Boolean: return (bool)t;
                case JTokenType.String: return (string)t;
                case JTokenType.Array:
                    var l = new List<object>();
                    foreach (var x in (JArray)t) l.Add(Plain(x));
                    return l;
                default: return null;
            }
        }

        // ------------------------------------------------------------ handlers

        static object Attach(CoreContext c)
        {
            var n = Character(c);
            string preset = CommandKit.OptString(c, "preset") ?? WetPreset.Water;
            bool aimed = RayOf(c, n, out var ray, out float length);
            var r = CommandKit.Check(c, CommandKit.Ops(c).WetAttach(n.Id, aimed ? ray : (Ray?)null, length, preset, CommandKit.Exec(c, StudioOps.Labels.WetAdd)));
            var a = (WetAttached)r.Value;
            var reply = new BridgeReply().With("status", Done).With("stain", a.Stain);
            if (a.Floor != null) reply["floor"] = (double)a.Floor.Value;
            return reply;
        }

        static object Stain(CoreContext c)
        {
            var n = Character(c);
            var q = new WetStainRequest
            {
                Preset = CommandKit.OptString(c, "preset") ?? WetPreset.Water,
                Radius = Number(c, "radius"), Spread = Number(c, "spread"), Tail = Number(c, "tail"), SkinDrips = CommandKit.OptBool(c, "skinDrips"),
            };
            q.Name = CommandKit.OptString(c, "name") ?? q.Name;
            if (RayOf(c, n, out var ray, out float length))
            {
                q.Ray = ray;
                q.Length = length;
            }
            q.NameOnly = q.Ray == null && q.Radius == null && q.Spread == null && q.Tail == null && q.SkinDrips == null && CommandKit.Absent(c, "preset");
            CommandKit.Check(c, CommandKit.Ops(c).WetNamedStain(n.Id, q, CommandKit.Exec(c, null)));   // the operation names the entry: a stain applied again is not an added one
            return new BridgeReply().With("status", Done);
        }

        internal static float? Number(CoreContext c, string arg)
        {
            float? f = CommandKit.OptFloat(c, arg);
            return f == null ? (float?)null : CommandKit.Finite(f.Value, arg);
        }

        static object Row(StudioOps ops, TreeNode n)
        {
            var s = ops.WetStatus(n.Id);
            bool floor = s.FloorSource != WetFloorSources.None;
            return new BridgeReply
            {
                ["key"] = n.Id,
                ["current"] = s.Current,
                ["t"] = (double)s.T,
                ["running"] = s.Running,
                ["radius"] = s.Radius,
                ["targets"] = s.Targets,
                ["unreadable"] = s.Unreadable,
                ["tiled"] = s.Tiled,
                ["noFilm"] = s.NoFilm,
                ["puddles"] = s.Puddles,
                ["floor"] = floor ? (double)s.Floor : (double?)null,
                ["floorSource"] = s.FloorSource,
                ["maps"] = new BridgeReply { ["live"] = s.Live, ["dormant"] = s.Dormant },
            };
        }

        static object Status(CoreContext c)
        {
            var key = CommandKit.OptionalNode(c, "key");
            if (key != null) CommandKit.KeyKind.Chara.Check(key, "key");
            c.Runtime.ApplyNow();
            var ops = CommandKit.Ops(c);
            var names = new List<string>();
            var rows = new List<object>();
            foreach (var n in key != null ? new[] { key } : (IEnumerable<TreeNode>)c.Session.Model.PreOrder())
            {
                var block = ops.WetBlock(n.Id);
                if (block == null || (key == null && block.IsEmpty)) continue;
                rows.Add(Row(ops, n));
                foreach (var s in block.Stains)
                    if (!names.Contains(s.Name)) names.Add(s.Name);
            }
            return new BridgeReply().With("stains", names).With("characters", rows);
        }

        static object Use(CoreContext c)
        {
            var n = Character(c);
            if (CommandKit.Absent(c, "name")) throw new BridgeException(BridgeError.Missing("name", ArgTypes.String));
            bool start = CommandKit.OptBool(c, "start") ?? true;
            CommandKit.Check(c, CommandKit.Ops(c).WetUse(n.Id, StainOf(c, n), start, CommandKit.Exec(c, start ? StudioOps.Labels.WetStart : StudioOps.Labels.WetEdit)));
            return new BridgeReply().With("status", Done);
        }

        static object Start(CoreContext c)
        {
            var n = Character(c);
            CommandKit.Check(c, CommandKit.Ops(c).WetStart(n.Id, StainOf(c, n), CommandKit.OptBool(c, "loop"), CommandKit.Exec(c, StudioOps.Labels.WetStart)));
            return new BridgeReply().With("t", 0.0);
        }

        static object Dry(CoreContext c)
        {
            var n = Character(c);
            CommandKit.Check(c, CommandKit.Ops(c).WetDry(n.Id, StainOf(c, n), CommandKit.OptBool(c, "puddles") ?? true, CommandKit.Exec(c, StudioOps.Labels.WetDry)));
            return new BridgeReply();
        }

        static object Set(CoreContext c)
        {
            var n = Character(c);
            string stain = StainOf(c, n);
            if (CommandKit.Absent(c, "values")) throw new BridgeException(BridgeError.Missing("values", ArgTypes.Object));
            var values = new List<KeyValuePair<string, object>>();
            foreach (var m in AdvancedPoseCommands.JsonArg(c, "values")) values.Add(new KeyValuePair<string, object>(m.Key, Plain(m.Value)));
            var r = CommandKit.Check(c, CommandKit.Ops(c).WetSet(n.Id, stain, values, CommandKit.Exec(c, null)));   // the operation names the entry by what changed
            return new BridgeReply().With("changed", new List<string>(r.Changed));
        }

        static object Keep(CoreContext c)
        {
            var n = Character(c);
            var r = CommandKit.Check(c, CommandKit.Ops(c).WetKeep(n.Id, CommandKit.Exec(c, StudioOps.Labels.WetKeep)));
            return new BridgeReply().With("stain", r.Value as string);
        }

        static object Remove(CoreContext c)
        {
            var n = Character(c);
            var ops = CommandKit.Ops(c);
            var o = CommandKit.Exec(c, StudioOps.Labels.WetRemove);
            bool all = CommandKit.OptBool(c, "all") ?? false;
            if (all && !CommandKit.Absent(c, "name")) throw CommandKit.BadArgs("all", "name or all", "name and all cannot stand together");
            var r = CommandKit.Check(c, all ? ops.WetRemoveAll(n.Id, o) : ops.WetRemove(n.Id, StainOf(c, n), o));
            return new BridgeReply().With("removed", r.Value is int k ? k : 0);
        }

        static object Floor(CoreContext c)
        {
            var n = Character(c);
            string mode = CommandKit.OptString(c, "mode");
            if (mode != null && !WetFloorValues.Modes.Contains(mode)) throw CommandKit.BadArgs("mode", string.Join(", ", WetFloorValues.Modes));
            var r = CommandKit.Check(c, CommandKit.Ops(c).WetFloorSet(n.Id, mode, Number(c, "offset"), Number(c, "height"), CommandKit.Exec(c, StudioOps.Labels.WetFloor)));
            var f = (WetFloorInfo)r.Value;
            return new BridgeReply().With("y", (double)f.Y).With("source", f.Source);
        }

        static object Probe(CoreContext c)
        {
            if (CommandKit.Absent(c, "points")) throw new BridgeException(BridgeError.Missing("points", ArgTypes.ArrayOf(ArgTypes.Any)));
            if (!(Raw(c, "points") is JArray list)) throw CommandKit.BadArgs("points", ArgTypes.ArrayOf(ArgTypes.Any));
            float y = Number(c, "y") ?? 0f, above = Number(c, "above") ?? 6f, below = Number(c, "below") ?? 30f;
            var points = new Vector3[list.Count];
            for (int i = 0; i < points.Length; i++) points[i] = PointAt(list[i], "points[" + i + "]", y);
            var hits = new MapSurfaceHit[points.Length];
            c.Runtime.ApplyNow();
            MapSurface.Of(c.Runtime).Surfaces(points, hits, above, below);
            var rows = new List<object>(points.Length);
            string source = null;
            for (int i = 0; i < points.Length; i++)
            {
                bool hit = hits[i].Source != null;
                if (hit && source == null) source = hits[i].Source;
                rows.Add(new BridgeReply
                {
                    ["x"] = (double)points[i].x,
                    ["z"] = (double)points[i].z,
                    ["y"] = hit ? (double)hits[i].Height : (double?)null,
                    ["normal"] = hit ? CommandKit.D3(hits[i].Normal) : null,
                });
            }
            return new BridgeReply().With("hits", rows).With("source", source);
        }

        // [x, z] or [x, y, z].
        static Vector3 PointAt(JToken t, string arg, float y)
        {
            if (!(t is JArray a) || (a.Count != 2 && a.Count != 3)) throw CommandKit.BadArgs(arg, "[x, z] or [x, y, z]");
            var v = new float[a.Count];
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Type != JTokenType.Float && a[i].Type != JTokenType.Integer) throw CommandKit.BadArgs(arg + "[" + i + "]", ArgTypes.Float);
                v[i] = CommandKit.Finite((float)(double)a[i], arg + "[" + i + "]");
            }
            return a.Count == 2 ? new Vector3(v[0], y, v[1]) : new Vector3(v[0], v[1], v[2]);
        }
    }
}
