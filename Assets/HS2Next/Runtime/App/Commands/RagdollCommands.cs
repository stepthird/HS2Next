// The commands of ragdolls (group "ragdoll", owner core, Since 1.3.0, experimental): a character as a physics body, what is done to it, and shots.
//   ragdoll.build, .lock, .tie, .pin, .unpin, .follower, .turn, .settle, .pusher, .parts, .push, .release, fx.shoot   the names, arguments, defaults and replies scene scripts use
//   ragdoll.status, .set, .knock, .throw, .bake, .obstacle
// A key names a character; with none, the character of the last ragdoll.build, else the selection's first character. ragdoll.unpin and ragdoll.release with no key mean every character.
// `id` stays the envelope's: a member naming a pin is `pin`. What lasts goes through StudioOps (one history entry each, `undo: false` and a client's batch as every property command), so a timed
// command leaves the history alone; what acts once has no history. A point is [x, y, z] or {key, bone, offset}, as in the commands of stains: `from` also takes an item's key, whose far end
// towards the target starts the ray. Over a limit a command answers bad_args with the word ragdoll.limit in the text. Every command works without a graphics device.
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Api.Unity;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HS2Next.App.Commands
{
    public static class RagdollCommands
    {
        /// <summary>The commands of this file (CoreCommands.Phase67Names).</summary>
        public static readonly string[] Names =
        {
            "ragdoll.build", "ragdoll.lock", "ragdoll.tie", "ragdoll.pin", "ragdoll.unpin", "ragdoll.follower", "ragdoll.turn", "ragdoll.settle", "ragdoll.pusher", "ragdoll.parts", "ragdoll.push",
            "ragdoll.release", "fx.shoot", "ragdoll.status", "ragdoll.set", "ragdoll.knock", "ragdoll.throw", "ragdoll.bake", "ragdoll.obstacle",
        };

        /// <summary>The commands of this file that keep the automation vocabulary of scene scripts (CoreCommands.ParityNames).</summary>
        public static readonly string[] ParityNames =
        {
            "ragdoll.build", "ragdoll.lock", "ragdoll.tie", "ragdoll.pin", "ragdoll.unpin", "ragdoll.follower", "ragdoll.turn", "ragdoll.settle", "ragdoll.pusher", "ragdoll.parts", "ragdoll.push",
            "ragdoll.release", "fx.shoot",
        };

        /// <summary>CommandSpec.Since of these commands.</summary>
        public const string Since67 = "1.3.0";
        /// <summary>Their help group.</summary>
        public const string GroupName = "ragdoll";

        const string DefaultParts = "upperarm,forearm,hand", DefaultHand = "hand_R", DefaultOtherHand = "hand_L", DefaultPushPart = "chest", DefaultTargetBone = "cf_J_Spine02";
        const float DefaultStrength = 2f, DefaultSeconds = 2f, DefaultGain = 6f, DefaultDamp = 1.5f, DefaultPush = 100f, DefaultMomentum = 181f;

        const string KeyDoc = "a character; absent: the character of the last ragdoll.build, else the selection's first character";
        const string PartDoc = "a part: pelvis, abdomen, chest, head, upperarm_L, _R, forearm_L, _R, hand_L, _R, thigh_L, _R, shin_L, _R, foot_L, _R";
        const string DirDoc = "the direction the front of the trunk goes to, [x, y, z]; absent: up";
        const string Ok2Doc = "true when the character has a body";
        const string PointDoc = "[x, y, z] or {key, bone, offset}";
        const string RowDoc = "{key, state, awake, parts, pins [{pin, part, follow, lag, state}], ties, holds, overridden, protected, settled, weakest}: state up, down, gettingUp or limp, null with no body; awake true when a part is awake; parts how many; a pin's follow the node it follows or null, its lag 1 when a constraint of the same frame moves that node, its state ok, missing or off; ties and holds how many; overridden and protected the parts whose bone a driven bone writes and the parts that drive nothing; settled true when a pose at rest is kept; weakest what hits took from the weakest part, 0 to 1";
        const string WorldDoc = "{bodies, props, pushers, hz, steps, floor, floorSource, room, lastHit}: steps the substeps run; floor the room's floor and floorSource map, feet or fixed, both null with no room; room {center [x, z], size [w, h, d]} or null; lastHit the speed the last prop gave a body or null";

        static CommandSpec Spec(string name, string summary, CommandFlags flags) =>
            CommandKit.Spec(name, summary, GroupName).WithSince(Since67).WithFlags(flags | CommandFlags.Experimental);

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, Spec("ragdoll.build", "Makes a character a physics body that follows its pose until it is hit; one history entry. A character that has a body keeps it. The character becomes the one a command without a key means. The first body of a scene makes the physics world, the last one's end takes it away.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, KeyDoc)
                    .Arg("mapFloor", ArgTypes.Bool, "the floor comes from the map; false: from the feet, for the whole scene", false, true)
                    .Arg("room", ArgTypes.ArrayOf(ArgTypes.Float), "[x0, x1, z0, z1]: the room bodies stay in, written to the scene")
                    .Return("built", ArgTypes.Bool, "true when a body was made; false when it had one")
                    .Return("floor", ArgTypes.Float, "the height of the floor, with mapFloor true and a map that answers", false)
                    .Example("{\"cmd\":\"ragdoll.build\",\"key\":\"<id>\",\"room\":[-20,20,-10,10]}"),
                Build);
            CommandKit.Register(r, app, Spec("ragdoll.lock", "Each part whose key starts with one of the names holds its rotation against its parent as it is now, with a strong muscle, also on a limp body; one history entry.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, KeyDoc)
                    .Arg("parts", ArgTypes.String, "names, comma separated, that part keys start with", false, DefaultParts)
                    .Return("locked", ArgTypes.Int, "the number of joints")
                    .Example("{\"cmd\":\"ragdoll.lock\",\"parts\":\"forearm,hand\"}"),
                Lock);
            CommandKit.Register(r, app, Spec("ragdoll.tie", "Ties two parts where they are now, until a release; one history entry.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, KeyDoc)
                    .Arg("a", ArgTypes.String, PartDoc, false, DefaultOtherHand)
                    .Arg("b", ArgTypes.String, "the other part", false, DefaultHand)
                    .Return("tied", ArgTypes.Bool, "true")
                    .Example("{\"cmd\":\"ragdoll.tie\",\"a\":\"hand_L\",\"b\":\"hand_R\"}"),
                Tie);
            CommandKit.Register(r, app, Spec("ragdoll.pin", "Pins a part at its centre of mass where it is, or to something it then follows; one history entry.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, KeyDoc)
                    .Arg("part", ArgTypes.String, PartDoc, false, DefaultHand)
                    .Arg("follow", ArgTypes.Any, "what the pin follows: a node's key, or {key, bone}")
                    .Arg("snap", ArgTypes.Bool, "the pin goes onto what it follows; false: it keeps the distance it has", false, false)
                    .Return("pinned", ArgTypes.Bool, "true")
                    .Example("{\"cmd\":\"ragdoll.pin\",\"part\":\"hand_R\",\"follow\":{\"key\":\"<id>\",\"bone\":\"cf_J_Hand_L\"},\"snap\":true}"),
                Pin);
            CommandKit.Register(r, app, Spec("ragdoll.unpin", "Drops the pins of a character, or with no key of every character; one history entry.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, "a character; absent: every character")
                    .Return("unpinned", ArgTypes.Int, "how many pins went")
                    .Example("{\"cmd\":\"ragdoll.unpin\"}"),
                Unpin);
            CommandKit.Register(r, app, Spec("ragdoll.follower", "Makes a folder that follows a part's bone, by a constraint with no offset, in one history entry. The folder goes on following the bone after a release.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, KeyDoc)
                    .Arg("part", ArgTypes.String, PartDoc, false, DefaultHand)
                    .Return("folder", ArgTypes.String, "the folder's key")
                    .Return("constraint", ArgTypes.String, "the constraint's id")
                    .Example("{\"cmd\":\"ragdoll.follower\",\"part\":\"hand_R\"}"),
                Follower);
            CommandKit.Register(r, app, Spec("ragdoll.turn", "Once, a torque on the trunk turns its front towards a direction, in proportion to how far it is. No history.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, KeyDoc)
                    .Arg("dir", ArgTypes.Vec3, DirDoc, false, new[] { 0f, 1f, 0f })
                    .Arg("strength", ArgTypes.Float, "how hard", false, DefaultStrength)
                    .Return("ok2", ArgTypes.Bool, Ok2Doc)
                    .Example("{\"cmd\":\"ragdoll.turn\",\"dir\":[0,1,0],\"strength\":2}"),
                Turn);
            CommandKit.Register(r, app, Spec("ragdoll.settle", "For a time the trunk turns its front towards a direction, as a spring with a damper. No history, not saved.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, KeyDoc)
                    .Arg("dir", ArgTypes.Vec3, DirDoc, false, new[] { 0f, 1f, 0f })
                    .Arg("seconds", ArgTypes.Float, "how long, in scene seconds", false, DefaultSeconds)
                    .Arg("gain", ArgTypes.Float, "the spring", false, DefaultGain)
                    .Arg("damp", ArgTypes.Float, "the damper", false, DefaultDamp)
                    .Return("ok2", ArgTypes.Bool, Ok2Doc)
                    .Example("{\"cmd\":\"ragdoll.settle\",\"seconds\":5.5,\"gain\":16,\"damp\":3}"),
                Settle);
            CommandKit.Register(r, app, Spec("ragdoll.pusher", "A capsule along two bones of a character, with or without a body, that moves bodies and that bodies do not move; one history entry. A release with no key takes it away.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, KeyDoc)
                    .Arg("a", ArgTypes.String, "a bone", false, RagdollPusher.DefaultA)
                    .Arg("b", ArgTypes.String, "the other bone", false, RagdollPusher.DefaultB)
                    .Arg("radius", ArgTypes.Float, "its radius", false, RagdollPusher.DefaultRadius)
                    .Return("count", ArgTypes.Int, "how many pushers there are now")
                    .Example("{\"cmd\":\"ragdoll.pusher\",\"key\":\"<id>\",\"radius\":0.5}"),
                Pusher);
            CommandKit.Register(r, app, Spec("ragdoll.parts", "Where the parts of a body are.", CommandFlags.ReadOnly)
                    .Arg("key", ArgTypes.Node, KeyDoc)
                    .Return("parts", ArgTypes.Object, "{<part>: {pos, forward, up}}: the centre of mass and the bone's forward and up as the body sets them, to three decimals")
                    .Example("{\"cmd\":\"ragdoll.parts\"}"),
                Parts);
            CommandKit.Register(r, app, Spec("ragdoll.push", "An impulse on a part, given in world axes, in the character's frame (z is its front), or away from a node. Every part wakes. No history.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, KeyDoc)
                    .Arg("part", ArgTypes.String, PartDoc, false, DefaultPushPart)
                    .Arg("impulse", ArgTypes.Vec3, "the impulse in world axes")
                    .Arg("local", ArgTypes.Vec3, "the impulse in the character's frame")
                    .Arg("away", ArgTypes.Node, "a node: the impulse runs from it to the bone, level, by strength, plus lift upwards")
                    .Arg("bone", ArgTypes.String, "the bone of the character for away", false, DefaultTargetBone)
                    .Arg("strength", ArgTypes.Float, "the impulse for away", false, DefaultPush)
                    .Arg("lift", ArgTypes.Float, "the part of it upwards for away", false, 0f)
                    .Return("pushed", ArgTypes.Bool, "true")
                    .Example("{\"cmd\":\"ragdoll.push\",\"part\":\"chest\",\"local\":[0,0,-250]}"),
                Push);
            CommandKit.Register(r, app, Spec("ragdoll.release", "With no key every body, prop, pusher and effect goes and the physics world with them, so the next ragdoll.build starts in a fresh world: a loop's first event repeats a run this way. With a key that body goes and the rest stay. A body's pins, ties, holds and pose go with it, its values stay. One history entry; nothing to release makes none.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, "a character; absent: every character")
                    .Arg("blend", ArgTypes.Bool, "with a key and a running clock the bones go to the pose over the scene's blend time", false, false)
                    .Example("{\"cmd\":\"ragdoll.release\"}"),
                Release);
            CommandKit.Register(r, app, Spec("fx.shoot", "One bullet: it pushes the parts it passes through, ends at a wall or the ground, and draws a tracer and a flash, blood when that option is on. With no body in the scene it draws and hits nothing. No history.", CommandFlags.Mutates)
                    .Arg("from", ArgTypes.Any, "where it starts: " + PointDoc + ", {key, accessory} (the muzzle chara.accessoryAxis reads for that accessory slot of a character), or an item's key (its far end towards the target)", true)
                    .Arg("target", ArgTypes.Any, "what it aims at: " + PointDoc + "; on a character the bone defaults to " + DefaultTargetBone, true)
                    .Arg("spread", ArgTypes.Float, "a random offset in a disc of that angle, in degrees", false, 0f)
                    .Arg("kill", ArgTypes.Bool, "the first character hit goes limp", false, false)
                    .Arg("momentum", ArgTypes.Float, "how hard", false, DefaultMomentum)
                    .Arg("stain", ArgTypes.Bool, "the first character hit takes a stain", false, false)
                    .Return("hits", ArgTypes.Int, "the bodies hit")
                    .Return("muzzle", ArgTypes.Vec3, "where the tracer starts")
                    .Return("target", ArgTypes.Vec3, "where the bullet ended")
                    .Example("{\"cmd\":\"fx.shoot\",\"from\":[0,10,10],\"target\":{\"key\":\"<id>\",\"bone\":\"cf_J_Spine02\"},\"momentum\":40}"),
                Shoot);
            CommandKit.Register(r, app, Spec("ragdoll.status", "How the physics world stands, and with a key that character, with none every character that has a body.", CommandFlags.ReadOnly)
                    .Arg("key", ArgTypes.Node, "a character")
                    .Return("world", ArgTypes.Object, WorldDoc)
                    .Return("characters", ArgTypes.ArrayOf(ArgTypes.Object), RowDoc)
                    .Example("{\"cmd\":\"ragdoll.status\"}"),
                Status);
            CommandKit.Register(r, app, Spec("ragdoll.set", "Sets values in one history entry: a character's muscle, muscleDamping, pin, pinFreq, pinFalloff, pinHipsOnly, gravityComp, knockDistance, knockHit, downMuscle, minDown, getUp, relocate, faceCamera, legCollision, mass; the scene's gravity, timeScale, hz, friction, angularDrag, pushSpeed, grabStiffness, grabMax, followStiffness, followMax, blendOut, transfer, maxPartDv, hitWeaken, weakenRegain, weakMuscle, staggerAt, momentum, fireRate, propKind, propMass, propSpeed, propSize; room as [x0, x1, z0, z1] or {center, size}, room.center, room.size; floor as {mode, offset, height}, floor.mode, floor.offset, floor.height. Numbers are stored as given; the ranges act when a value is applied. A preset comes first, so a value given with it wins. snap puts every part on its target, at rest, standing.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, KeyDoc + "; needed for a character's values, preset and snap")
                    .Arg("values", ArgTypes.Object, "name: value")
                    .Arg("snap", ArgTypes.Bool, "every part goes to its target", false, false)
                    .Arg("preset", ArgTypes.Enum(RagdollPreset.Limp, RagdollPreset.Stiff, RagdollPreset.Puppet, RagdollPreset.HipsPin), "the values of a preset")
                    .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "the names given that changed something, with preset and snap")
                    .Example("{\"cmd\":\"ragdoll.set\",\"preset\":\"limp\",\"values\":{\"timeScale\":0.5,\"floor.mode\":\"feet\"}}"),
                Set);
            CommandKit.Register(r, app, Spec("ragdoll.knock", "The body goes down, and gets up by itself when its values say so. A limp body stays limp. No history.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, KeyDoc)
                    .Return("state", ArgTypes.String, "up, down, gettingUp or limp, after it")
                    .Example("{\"cmd\":\"ragdoll.knock\"}"),
                Knock);
            CommandKit.Register(r, app, Spec("ragdoll.throw", "Throws a prop along a ray, from where it enters the room. Props last 20 seconds, 40 at most, and are not saved. No history.", CommandFlags.Mutates)
                    .Arg("from", ArgTypes.Any, "where the ray starts: " + PointDoc + ", or an item's key")
                    .Arg("target", ArgTypes.Any, "what it aims at: " + PointDoc)
                    .Arg("ray", ArgTypes.Object, "{origin, dir}, in place of from and target")
                    .Arg("kind", ArgTypes.Enum(RagdollWorldValues.PropBall, RagdollWorldValues.PropBox, RagdollWorldValues.PropHeavy), "absent: the scene's propKind")
                    .Arg("mass", ArgTypes.Float, "absent: the scene's propMass")
                    .Arg("speed", ArgTypes.Float, "absent: the scene's propSpeed")
                    .Arg("size", ArgTypes.Float, "absent: the scene's propSize")
                    .Return("props", ArgTypes.Int, "how many props there are now")
                    .Example("{\"cmd\":\"ragdoll.throw\",\"from\":[0,10,30],\"target\":[0,8,0],\"kind\":\"box\"}"),
                Throw);
            CommandKit.Register(r, app, Spec("ragdoll.bake", "Writes the body's pose to FK and releases the body at once, in one history entry: the groups Body and Neck and the hips position take what the body wrote, the hips offset goes to 0, FK and those groups go on, IK goes off, the animation stops. What corrections lay on top stays on top, so the view does not move.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, KeyDoc)
                    .Return("bones", ArgTypes.Int, "the FK bones written")
                    .Example("{\"cmd\":\"ragdoll.bake\"}"),
                Bake);
            CommandKit.Register(r, app, Spec("ragdoll.obstacle", "An item takes part in the physics as a static collider that follows it, or stops; one history entry.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, "an item", true)
                    .Arg("on", ArgTypes.Bool, "false: it stops", false, true)
                    .Return("obstacles", ArgTypes.Int, "how many items take part now")
                    .Example("{\"cmd\":\"ragdoll.obstacle\",\"key\":\"<id>\"}"),
                Obstacle);
        }

        // ------------------------------------------------------------ arguments

        // The character of a command: a key, else the one a command without a key means.
        static TreeNode Character(CoreContext c)
        {
            var n = CommandKit.OptionalNode(c, "key");
            if (n != null) return CommandKit.KeyKind.Chara.Check(n, "key");
            string id = CommandKit.Ops(c).RagdollDefaultCharacter();
            if (id == null) throw CommandKit.NotFound("no character: give a key or select one", "key");
            return c.Session.Model.Find(id);
        }

        // The character a key names; null with no key, for the commands that then mean every character.
        static TreeNode KeyOf(CoreContext c)
        {
            var n = CommandKit.OptionalNode(c, "key");
            return n != null ? CommandKit.KeyKind.Chara.Check(n, "key") : null;
        }

        static Vector3 Vector(JToken t, string arg) => WetCommands.V(AdvancedPoseCommands.JsonVec3(t, arg).Value);

        // A direction, up when absent.
        static Vector3 DirOf(CoreContext c)
        {
            var t = WetCommands.Raw(c, "dir");
            if (t == null) return Vector3.up;
            var v = Vector(t, "dir");
            if (!(v.sqrMagnitude > 1e-10f)) throw CommandKit.BadArgs("dir", "a direction", "dir has no length");
            return v;
        }

        static float[] RoomOf(CoreContext c)
        {
            var t = WetCommands.Raw(c, "room");
            if (t == null) return null;
            if (!(t is JArray a) || a.Count != 4) throw CommandKit.BadArgs("room", "[x0, x1, z0, z1]");
            var v = new float[4];
            for (int i = 0; i < 4; i++)
            {
                if (a[i].Type != JTokenType.Float && a[i].Type != JTokenType.Integer) throw CommandKit.BadArgs("room[" + i + "]", ArgTypes.Float);
                v[i] = CommandKit.Finite((float)(double)a[i], "room[" + i + "]");
            }
            return v;
        }

        // The ray of two ends: from goes to target; `from` may be an item.
        static Ray RayOf(CoreContext c, JToken from, JToken target, out Vector3 aim, string bone)
        {
            aim = WetCommands.PointOf(c, "target", target, false, bone);
            var start = WetCommands.FromOf(c, from, aim);
            var d = aim - start;
            float span = d.magnitude;
            if (!(span > 1e-5f)) throw CommandKit.BadArgs("target", "a point apart from from", "from and target are the same point");
            return new Ray(start, d / span);
        }

        static double[] R3(Vector3 v) => new[] { System.Math.Round((double)v.x, 3), System.Math.Round((double)v.y, 3), System.Math.Round((double)v.z, 3) };

        // ------------------------------------------------------------ the thirteen

        static object Build(CoreContext c)
        {
            var n = Character(c);
            var r = CommandKit.Check(c, CommandKit.Ops(c).RagdollBuild(n.Id, CommandKit.OptBool(c, "mapFloor") ?? true, RoomOf(c), CommandKit.Exec(c, StudioOps.Labels.RagdollBuild)));
            var b = (RagdollBuildResult)r.Value;
            var reply = new BridgeReply().With("built", b.Built);
            if (b.Floor != null) reply["floor"] = (double)b.Floor.Value;
            return reply;
        }

        static object Lock(CoreContext c)
        {
            var n = Character(c);
            var r = CommandKit.Check(c, CommandKit.Ops(c).RagdollHold(n.Id, CommandKit.OptString(c, "parts") ?? DefaultParts, CommandKit.Exec(c, StudioOps.Labels.RagdollHold)));
            return new BridgeReply().With("locked", r.Value is int k ? k : 0);
        }

        static object Tie(CoreContext c)
        {
            var n = Character(c);
            CommandKit.Check(c, CommandKit.Ops(c).RagdollTie(n.Id, CommandKit.OptString(c, "a") ?? DefaultOtherHand, CommandKit.OptString(c, "b") ?? DefaultHand, CommandKit.Exec(c, StudioOps.Labels.RagdollTie)));
            return new BridgeReply().With("tied", true);
        }

        static object Pin(CoreContext c)
        {
            var n = Character(c);
            string follow = null, bone = null;
            var t = WetCommands.Raw(c, "follow");
            if (t != null && t.Type == JTokenType.String) follow = CommandKit.NodeOf(c, "follow", (string)t).Id;
            else if (t is JObject o)
            {
                foreach (var m in o.Properties())
                    if (m.Name != "key" && m.Name != "bone") WetCommands.UnknownMember(c, "follow", m.Name, "key or bone");
                if (o["key"]?.Type != JTokenType.String) throw new BridgeException(BridgeError.Missing("follow.key", ArgTypes.Node));
                follow = CommandKit.NodeOf(c, "follow.key", (string)o["key"]).Id;
                if (o["bone"] != null && o["bone"].Type != JTokenType.Null)
                {
                    if (o["bone"].Type != JTokenType.String) throw CommandKit.BadArgs("follow.bone", ArgTypes.String);
                    bone = (string)o["bone"];
                }
            }
            else if (t != null) throw CommandKit.BadArgs("follow", "a node's key or {key, bone}");
            var part = CommandKit.OptString(c, "part") ?? DefaultHand;
            CommandKit.Check(c, CommandKit.Ops(c).RagdollPin(n.Id, part, null, follow, bone, CommandKit.OptBool(c, "snap") ?? false, CommandKit.Exec(c, StudioOps.Labels.RagdollPin)));
            return new BridgeReply().With("pinned", true);
        }

        static object Unpin(CoreContext c)
        {
            var n = KeyOf(c);
            var ops = CommandKit.Ops(c);
            var o = CommandKit.Exec(c, StudioOps.Labels.RagdollUnpin);
            var r = CommandKit.Check(c, n != null ? ops.RagdollUnpin(n.Id, null, o) : ops.RagdollUnpinAll(o));
            return new BridgeReply().With("unpinned", r.Value is int k ? k : 0);
        }

        static object Follower(CoreContext c)
        {
            var n = Character(c);
            var r = CommandKit.Check(c, CommandKit.Ops(c).RagdollFollower(n.Id, CommandKit.OptString(c, "part") ?? DefaultHand, CommandKit.Exec(c, StudioOps.Labels.RagdollFollower)));
            var f = (RagdollFollowerResult)r.Value;
            return new BridgeReply().With("folder", f.Folder).With("constraint", f.Constraint);
        }

        static object Turn(CoreContext c)
        {
            var n = Character(c);
            var r = CommandKit.Check(c, CommandKit.Ops(c).RagdollTurn(n.Id, DirOf(c), WetCommands.Number(c, "strength") ?? DefaultStrength));
            return new BridgeReply().With("ok2", r.Value is bool b && b);
        }

        static object Settle(CoreContext c)
        {
            var n = Character(c);
            float seconds = WetCommands.Number(c, "seconds") ?? DefaultSeconds;
            if (seconds < 0f) throw CommandKit.BadArgs("seconds", "a number not below 0");
            var r = CommandKit.Check(c, CommandKit.Ops(c).RagdollSettle(n.Id, DirOf(c), seconds, WetCommands.Number(c, "gain") ?? DefaultGain, WetCommands.Number(c, "damp") ?? DefaultDamp));
            return new BridgeReply().With("ok2", r.Value is bool b && b);
        }

        static object Pusher(CoreContext c)
        {
            var n = Character(c);
            var r = CommandKit.Check(c, CommandKit.Ops(c).RagdollPusher(n.Id, CommandKit.OptString(c, "a") ?? RagdollPusher.DefaultA, CommandKit.OptString(c, "b") ?? RagdollPusher.DefaultB,
                WetCommands.Number(c, "radius") ?? RagdollPusher.DefaultRadius, CommandKit.Exec(c, StudioOps.Labels.RagdollPusher)));
            return new BridgeReply().With("count", r.Value is int k ? k : 0);
        }

        static object Parts(CoreContext c)
        {
            var n = Character(c);
            var ops = CommandKit.Ops(c);
            c.Runtime.ApplyNow();
            var body = ops.RagdollBodyOf(n.Id);
            if (body == null) throw CommandKit.NotRealized("this character has no ragdoll", "key", "NoBody");
            var parts = new BridgeReply();
            foreach (var p in body.Parts)
            {
                var rot = p.BoneRotation;
                parts[p.Key] = new BridgeReply { ["pos"] = R3(p.Rb.worldCenterOfMass), ["forward"] = R3(rot * Vector3.forward), ["up"] = R3(rot * Vector3.up) };
            }
            return new BridgeReply().With("parts", parts);
        }

        static object Push(CoreContext c)
        {
            var n = Character(c);
            var ops = CommandKit.Ops(c);
            string part = CommandKit.OptString(c, "part") ?? DefaultPushPart;
            JToken impulse = WetCommands.Raw(c, "impulse"), local = WetCommands.Raw(c, "local");
            var away = CommandKit.OptionalNode(c, "away");
            int given = (impulse != null ? 1 : 0) + (local != null ? 1 : 0) + (away != null ? 1 : 0);
            if (given != 1) throw CommandKit.BadArgs("impulse", "one of impulse, local and away", "a push takes one of impulse, local and away");
            var r = c.Runtime.FindChara(n.Id);
            Vector3 j;
            if (impulse != null) j = Vector(impulse, "impulse");
            else if (local != null)
            {
                r = CommandKit.Built(c, n);
                j = r.Root.transform.rotation * Vector(local, "local");
            }
            else
            {
                r = CommandKit.Built(c, n);
                float strength = WetCommands.Number(c, "strength") ?? DefaultPush, lift = WetCommands.Number(c, "lift") ?? 0f;
                string name = CommandKit.OptString(c, "bone") ?? DefaultTargetBone;
                var bone = AdvancedPoseResolver.Resolve(r, name);
                if (!bone) throw CommandKit.NotFound("character " + n.Id + " has no bone \"" + name + "\"", "bone");
                var from = c.Runtime.HolderOf(away.Id);
                if (!from) throw CommandKit.NotRealized("node " + away.Id + " has no place in the scene", "away", "NotRealized");
                var d = bone.position - from.position;
                d.y = 0f;
                j = d.normalized * strength + Vector3.up * lift;
            }
            CommandKit.Check(c, ops.RagdollPush(n.Id, part, j));
            return new BridgeReply().With("pushed", true);
        }

        static object Release(CoreContext c)
        {
            var n = KeyOf(c);
            var ops = CommandKit.Ops(c);
            var o = CommandKit.Exec(c, StudioOps.Labels.RagdollRelease);
            CommandKit.Check(c, n != null ? ops.RagdollRelease(n.Id, CommandKit.OptBool(c, "blend") ?? false, o) : ops.RagdollReleaseAll(o));
            return new BridgeReply();
        }

        static object Shoot(CoreContext c)
        {
            JToken from = WetCommands.Raw(c, "from"), target = WetCommands.Raw(c, "target");
            if (from == null) throw new BridgeException(BridgeError.Missing("from", ArgTypes.Any));
            if (target == null) throw new BridgeException(BridgeError.Missing("target", ArgTypes.Any));
            var ray = RayOf(c, from, target, out _, DefaultTargetBone);
            float spread = WetCommands.Number(c, "spread") ?? 0f;
            if (spread < 0f) throw CommandKit.BadArgs("spread", "a number not below 0");
            float momentum = WetCommands.Number(c, "momentum") ?? DefaultMomentum;
            var r = CommandKit.Check(c, CommandKit.Ops(c).RagdollShoot(ray.origin, ray.direction, ray.origin, spread, momentum, CommandKit.OptBool(c, "kill") ?? false, CommandKit.OptBool(c, "stain") ?? false));
            var s = (ShotResult)r.Value;
            return new BridgeReply().With("hits", s.Hits).With("muzzle", CommandKit.D3(s.Muzzle)).With("target", CommandKit.D3(s.End));
        }

        // ------------------------------------------------------------ the six added

        static object WorldRow(StudioOps ops)
        {
            var w = ops.RagdollWorldStatus();
            return new BridgeReply
            {
                ["bodies"] = w.Bodies,
                ["props"] = w.Props,
                ["pushers"] = w.Pushers,
                ["hz"] = (double)w.Hz,
                ["steps"] = w.Steps,
                ["floor"] = w.Floor != null ? (double)w.Floor.Value : (double?)null,
                ["floorSource"] = w.FloorSource,
                ["room"] = w.HasRoom ? new BridgeReply { ["center"] = new double[] { w.RoomCenter.x, w.RoomCenter.y }, ["size"] = CommandKit.D3(w.RoomSize) } : null,
                ["lastHit"] = w.LastHit != null ? (double)w.LastHit.Value : (double?)null,
            };
        }

        static object CharaRow(StudioOps ops, TreeNode n)
        {
            var s = ops.RagdollStatus(n.Id);
            var pins = new List<object>(s.Pins.Count);
            foreach (var p in s.Pins)
                pins.Add(new BridgeReply { ["pin"] = p.Pin, ["part"] = p.Part, ["follow"] = p.Follow, ["lag"] = p.Lag, ["state"] = p.State });
            return new BridgeReply
            {
                ["key"] = n.Id,
                ["state"] = s.State,
                ["awake"] = s.Awake,
                ["parts"] = s.Parts,
                ["pins"] = pins,
                ["ties"] = s.Ties,
                ["holds"] = s.Holds,
                ["overridden"] = s.Overridden,
                ["protected"] = s.Protected,
                ["settled"] = s.Settled,
                ["weakest"] = (double)s.Weakest,
            };
        }

        static object Status(CoreContext c)
        {
            var key = KeyOf(c);
            c.Runtime.ApplyNow();
            var ops = CommandKit.Ops(c);
            var rows = new List<object>();
            foreach (var n in key != null ? new[] { key } : (IEnumerable<TreeNode>)c.Session.Model.PreOrder())
            {
                var block = ops.RagdollBlock(n.Id);
                if (block == null || (key == null && !block.On && ops.RagdollBodyOf(n.Id) == null)) continue;
                rows.Add(CharaRow(ops, n));
            }
            return new BridgeReply().With("world", WorldRow(ops)).With("characters", rows);
        }

        static object Set(CoreContext c)
        {
            var key = KeyOf(c);
            var ops = CommandKit.Ops(c);
            var values = new List<KeyValuePair<string, JToken>>();
            if (!CommandKit.Absent(c, "values"))
                foreach (var m in AdvancedPoseCommands.JsonArg(c, "values")) values.Add(new KeyValuePair<string, JToken>(m.Key, m.Value));
            string preset = CommandKit.OptString(c, "preset");
            bool snap = CommandKit.OptBool(c, "snap") ?? false;
            if (values.Count == 0 && preset == null && !snap) throw CommandKit.BadArgs("values", "values, preset or snap", "nothing to set: give values, preset or snap");
            var r = CommandKit.Check(c, ops.RagdollSet(key?.Id ?? ops.RagdollDefaultCharacter(), values, preset, snap, CommandKit.Exec(c, StudioOps.Labels.RagdollSettings)));
            return new BridgeReply().With("changed", new List<string>(r.Changed));
        }

        static object Knock(CoreContext c)
        {
            var n = Character(c);
            var r = CommandKit.Check(c, CommandKit.Ops(c).RagdollKnock(n.Id));
            return new BridgeReply().With("state", r.Value as string);
        }

        static object Throw(CoreContext c)
        {
            JToken from = WetCommands.Raw(c, "from"), target = WetCommands.Raw(c, "target"), given = WetCommands.Raw(c, "ray");
            Ray ray;
            if (given != null)
            {
                if (from != null || target != null) throw CommandKit.BadArgs("ray", "ray, or from and target", "ray and from or target cannot stand together");
                if (!(given is JObject o)) throw CommandKit.BadArgs("ray", "{origin, dir}");
                foreach (var m in o.Properties())
                    if (m.Name != "origin" && m.Name != "dir") WetCommands.UnknownMember(c, "ray", m.Name, "origin or dir");
                if (o["origin"] == null) throw new BridgeException(BridgeError.Missing("ray.origin", ArgTypes.Vec3));
                if (o["dir"] == null) throw new BridgeException(BridgeError.Missing("ray.dir", ArgTypes.Vec3));
                var d = Vector(o["dir"], "ray.dir");
                if (!(d.sqrMagnitude > 1e-10f)) throw CommandKit.BadArgs("ray.dir", "a direction", "ray.dir has no length");
                ray = new Ray(Vector(o["origin"], "ray.origin"), d);
            }
            else
            {
                if (from == null) throw new BridgeException(BridgeError.Missing("from", ArgTypes.Any));
                if (target == null) throw new BridgeException(BridgeError.Missing("target", ArgTypes.Any));
                ray = RayOf(c, from, target, out _, null);
            }
            string kind = CommandKit.OptString(c, "kind");
            if (kind != null && kind != RagdollWorldValues.PropBall && kind != RagdollWorldValues.PropBox && kind != RagdollWorldValues.PropHeavy)
                throw CommandKit.BadArgs("kind", string.Join(", ", RagdollWorldValues.PropKinds));
            var r = CommandKit.Check(c, CommandKit.Ops(c).RagdollThrow(ray, kind, WetCommands.Number(c, "mass"), WetCommands.Number(c, "speed"), WetCommands.Number(c, "size")));
            return new BridgeReply().With("props", r.Value is int k ? k : 0);
        }

        static object Bake(CoreContext c)
        {
            var n = Character(c);
            var r = CommandKit.Check(c, CommandKit.Ops(c).RagdollBake(n.Id, CommandKit.Exec(c, StudioOps.Labels.RagdollBake)));
            return new BridgeReply().With("bones", r.Value is int k ? k : 0);
        }

        static object Obstacle(CoreContext c)
        {
            var n = CommandKit.KeyKind.Item.Check(CommandKit.Node(c, "key"), "key");
            var r = CommandKit.Check(c, CommandKit.Ops(c).RagdollObstacle(n.Id, CommandKit.OptBool(c, "on") ?? true, CommandKit.Exec(c, StudioOps.Labels.RagdollObstacle)));
            return new BridgeReply().With("obstacles", r.Value is int k ? k : 0);
        }
    }
}
