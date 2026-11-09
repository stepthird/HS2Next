// The generic commands scene scripts use (help group "script", owner core, Since 1.3.0, experimental), with the names, arguments, defaults and reply members
// of the automation vocabulary:
//   obj.moveTo           a node walks a straight line over the ground towards a target (ScriptedMoves: a slot while it moves, the end into the scene)
//   camera.follow        the view camera follows a node or a bone for some seconds (ScriptedFollow: a slot, never saved)
//   dof                  the depth of field shown, field by field, with focus blends (ScriptedDof: a slot; the scene's own depth of field is never written)
//   chara.accessoryAxis  the axis, centre, muzzle and anchor of an accessory slot from its meshes (AccessoryAxis)
// The moves, the follow and the dof command write no history. Register is called by the group "script" (CoreCommands.Register), beside the script commands.
using System;
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Overrides;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Timeline;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HS2Next.App.Commands
{
    public static class ScriptedCommands
    {
        /// <summary>The commands of this file.</summary>
        public static readonly string[] Names = { "obj.moveTo", "camera.follow", "dof", "chara.accessoryAxis" };

        /// <summary>The commands of this file that keep the automation vocabulary (CoreCommands.ParityNames).</summary>
        public static readonly string[] ParityNames = { "obj.moveTo", "camera.follow", "dof", "chara.accessoryAxis" };

        /// <summary>Their help group: the group of the script commands.</summary>
        public const string GroupName = "script";
        /// <summary>CommandSpec.Since of these commands.</summary>
        public const string Since69 = ScriptCommands.Since;

        /// <summary>The defaults of the automation vocabulary.</summary>
        public const float DefaultStop = 4f, DefaultTime = 1f, DefaultStance = 0f, DefaultDist = 12f, DefaultSeconds = 2f;
        public const string DefaultBone = "cf_J_Spine02";
        public static readonly Vector3 DefaultRot = new Vector3(60f, 0f, 0f);

        /// <summary>The dof command's members that HS2Next's depth of field has no setting for (accepted, a warning each).</summary>
        public static readonly string[] DofIgnored = { "overlap", "highRes", "blurType", "samples", "visualize", "maxBlurSize" };
        public const string DofIgnoredWarning = "not a setting of HS2Next's depth of field";
        public const string NearWarning = "HS2Next always blurs the near side";

        const string PointForms = "[x, y, z], {key, bone, offset} or a node's key";

        static CommandSpec Spec(string name, string summary, CommandFlags flags) =>
            CommandKit.Spec(name, summary, GroupName).WithSince(Since69).WithFlags(flags | CommandFlags.Experimental);

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, Spec("obj.moveTo", "Moves a node in a straight line over the ground towards a target on the scene clock, stopping some units before it: the node turns to face the target at once, then its position runs from where it is shown to the end over the given seconds (the pause holds it). The end goes into the scene; nothing enters the history. A new move of the node replaces the running one.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, "the node that moves", true)
                    .Arg("target", ArgTypes.Any, "where it goes: " + PointForms + "; a character's bone defaults to " + DefaultBone + "; the height is not used", true)
                    .Arg("stop", ArgTypes.Float, "the distance at which it stops before the target", false, DefaultStop)
                    .Arg("time", ArgTypes.Float, "the seconds the move takes (at least 0.05)", false, DefaultTime)
                    .Arg("stanceSpeed", ArgTypes.Float, "above 0: a character's animation speed becomes the speed of the move divided by this, between 0.2 and 2", false, DefaultStance)
                    .Return("to", ArgTypes.Vec3, "where the move ends")
                    .Return("distance", ArgTypes.Float, "the distance it covers")
                    .Example("{\"cmd\":\"obj.moveTo\",\"key\":\"<id>\",\"target\":{\"key\":\"<id>\"},\"stop\":4,\"time\":2}"),
                MoveTo);
            CommandKit.Register(r, app, Spec("camera.follow", "The view camera follows a node or one of its bones for some seconds of the scene clock, over the Timeline's camera: the followed point is smoothed, the angles and the distance run from rot and dist to rotTo and distTo; then the camera shows what it showed before. Never saved; no history. seconds 0 or less ends a follow.", CommandFlags.Mutates)
                    .Arg("target", ArgTypes.Any, "{key, bone, offset} or a node's key: a character's bone defaults to " + DefaultBone + ", the offset is in world axes; not needed to end a follow")
                    .Arg("rot", ArgTypes.Euler, "the camera's angles at the start (absent: [60, 0, 0])")
                    .Arg("dist", ArgTypes.Float, "the distance at the start", false, DefaultDist)
                    .Arg("fov", ArgTypes.Float, "the field of view; 0: the one shown", false, 0f)
                    .Arg("seconds", ArgTypes.Float, "how long it follows; 0 or less ends a follow", false, DefaultSeconds)
                    .Arg("rotTo", ArgTypes.Euler, "the angles at the end; absent: rot")
                    .Arg("distTo", ArgTypes.Float, "the distance at the end; absent: dist")
                    .Arg("above", ArgTypes.Bool, "the distance never goes below 1", false, false)
                    .Return("following", ArgTypes.Bool, "a follow runs")
                    .Return("until", ArgTypes.Float, "the scene clock's time at which it ends; null when none runs")
                    .Example("{\"cmd\":\"camera.follow\",\"target\":{\"key\":\"<id>\",\"bone\":\"cf_J_Head\"},\"rot\":[10,180,0],\"dist\":8,\"seconds\":3,\"distTo\":5}"),
                Follow);
            CommandKit.Register(r, app, Spec("dof", "Sets the depth of field shown, over the Timeline's and the scene's, changing only what is given: on, the focus (a node, a bone, a point, a distance or the camera's pivot, with a blend), the focal size and aperture (or sharp and full), the blur. release and reset hand it back; the scene's own depth of field is never changed. Never saved; no history.", CommandFlags.Mutates)
                    .Arg("on", ArgTypes.Bool, "the depth of field on or off")
                    .Arg("focus", ArgTypes.Any, "\"target\" or null: the camera's pivot; a node's key (a character between the eyes, an item its point); {key, bone, offset}; {point: [x, y, z]}; {dist: d} along the view")
                    .Arg("blend", ArgTypes.Float, "seconds of the scene clock over which the focus goes from where it is to the new one", false, 0f)
                    .Arg("ease", ArgTypes.Enum(ScriptedDof.Eases), "the blend's curve", false, ScriptedDof.EaseInOut)
                    .Arg("size", ArgTypes.Float, "the focal size, 0 to 2")
                    .Arg("aperture", ArgTypes.Float, "the aperture, 0 to 0.99")
                    .Arg("sharp", ArgTypes.Float, "with full instead of size and aperture: the sharp range (alone: full 3)")
                    .Arg("full", ArgTypes.Float, "the distance factor of the full blur (alone: sharp 0.1); clearly larger than 1 + sharp")
                    .Arg("blur", ArgTypes.Float, "the largest blur on a 1080-line frame; 0 or less: the renderer's")
                    .Arg("near", ArgTypes.Bool, "false is not supported: the near side is always blurred")
                    .Arg("release", ArgTypes.Bool, "hand the depth of field back to the Timeline or the scene", false, false)
                    .Arg("reset", ArgTypes.Bool, "the same as release: the scene's own depth of field stays as it is", false, false)
                    .Arg("overlap", ArgTypes.Any, DofIgnoredWarning)
                    .Arg("highRes", ArgTypes.Any, DofIgnoredWarning)
                    .Arg("blurType", ArgTypes.Any, DofIgnoredWarning)
                    .Arg("samples", ArgTypes.Any, DofIgnoredWarning)
                    .Arg("visualize", ArgTypes.Any, DofIgnoredWarning)
                    .Arg("maxBlurSize", ArgTypes.Any, DofIgnoredWarning)
                    .Return("dof", ArgTypes.Object, "{on, size, aperture, sharp, full, blur, focus, driver, blending}: sharp and full when the size and aperture came from them, focus as given (\"target\" for the pivot), driver dof, timeline or scene")
                    .Example("{\"cmd\":\"dof\",\"on\":true,\"focus\":\"<id>\",\"blend\":1.5,\"sharp\":0.1,\"full\":3}"),
                Dof);
            CommandKit.Register(r, app, Spec("chara.accessoryAxis", "The axis of a character's accessory slot read from its meshes: the centre, the main direction pointing away from where the slot hangs, the muzzle at its far end and the anchor, in world coordinates.", CommandFlags.ReadOnly)
                    .Arg("key", ArgTypes.Node, "a built character", true)
                    .Arg("slot", ArgTypes.Int, "the accessory slot, 0-based, as chara.accessories", false, 0)
                    .Return("axis", ArgTypes.Vec3, "the unit direction")
                    .Return("center", ArgTypes.Vec3, "the mean of the points")
                    .Return("muzzle", ArgTypes.Vec3, "the mean of the points within 0.3 units of the far end")
                    .Return("anchor", ArgTypes.Vec3, "where the slot hangs")
                    .Example("{\"cmd\":\"chara.accessoryAxis\",\"key\":\"<id>\",\"slot\":2}"),
                AccessoryAxisOf);
        }

        // ------------------------------------------------------------ arguments

        static float Num(CoreContext c, string arg, float absent) => WetCommands.Number(c, arg) ?? absent;

        static Vector3 V(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

        static Vector3? Vec(CoreContext c, string arg)
        {
            var t = WetCommands.Raw(c, arg);
            var v = AdvancedPoseCommands.JsonVec3(t, arg);
            return v.HasValue ? V(v.Value) : (Vector3?)null;
        }

        /// <summary>A node and a bone of `{key, bone, offset}` or a key; a character's bone defaults to <see cref="DefaultBone"/>.</summary>
        static TreeNode NodeBone(CoreContext c, string arg, JToken t, out string bone, out Vector3 offset)
        {
            bone = null;
            offset = Vector3.zero;
            TreeNode n;
            if (t.Type == JTokenType.String) n = CommandKit.NodeOf(c, arg, (string)t);
            else if (t is JObject o)
            {
                foreach (var m in o.Properties())
                    if (m.Name != "key" && m.Name != "bone" && m.Name != "offset") WetCommands.UnknownMember(c, arg, m.Name, "key, bone or offset");
                var k = o["key"];
                if (k == null || k.Type == JTokenType.Null) throw new BridgeException(BridgeError.Missing(arg + ".key", ArgTypes.Node));
                if (k.Type != JTokenType.String) throw CommandKit.BadArgs(arg + ".key", ArgTypes.Node, "keys are node ids");
                n = CommandKit.NodeOf(c, arg + ".key", (string)k);
                var b = o["bone"];
                if (b != null && b.Type != JTokenType.Null)
                {
                    if (b.Type != JTokenType.String) throw CommandKit.BadArgs(arg + ".bone", ArgTypes.String);
                    bone = (string)b;
                }
                var off = AdvancedPoseCommands.JsonVec3(o["offset"], arg + ".offset");
                if (off.HasValue) offset = V(off.Value);
            }
            else if (t.Type == JTokenType.Integer || t.Type == JTokenType.Float) throw CommandKit.BadArgs(arg, PointForms, "keys are node ids");
            else throw CommandKit.BadArgs(arg, PointForms);
            if (bone == null && n.Kind == NodeKind.Chara) bone = DefaultBone;
            return n;
        }

        /// <summary>The transform of a node and a bone (the holder without a bone); not_found or unavailable when it does not resolve now.</summary>
        static Transform TransformOf(CoreContext c, string arg, TreeNode n, string bone)
        {
            c.Runtime.ApplyNow();
            var end = ConstraintResolver.Source(c.Runtime, new ConstraintEnd(n.Id, bone));
            if (end.State == ConstraintEndState.Ok && end.Transform) return end.Transform;
            if (end.State == ConstraintEndState.Pending) throw CommandKit.NotRealized("node " + n.Id + " is not built", arg, "Pending");
            throw CommandKit.NotFound(bone != null ? "node " + n.Id + " has no bone \"" + bone + "\"" : "node " + n.Id + " has no place in the scene", arg);
        }

        // ------------------------------------------------------------ obj.moveTo

        static object MoveTo(CoreContext c)
        {
            var n = CommandKit.Node(c, "key");
            var t = WetCommands.Raw(c, "target") ?? throw new BridgeException(BridgeError.Missing("target", ArgTypes.Any));
            float stop = Num(c, "stop", DefaultStop), time = Num(c, "time", DefaultTime), stance = Num(c, "stanceSpeed", DefaultStance);
            var rt = c.Runtime;
            rt.ApplyNow();
            var from = ScriptedMoves.Shown(rt, n.Id) ?? throw CommandKit.NotFound("no node \"" + n.Id + "\" in the scene", "key");
            Vector3 target;
            if (t is JArray) target = V(AdvancedPoseCommands.JsonVec3(t, "target").Value);
            else
            {
                var tn = NodeBone(c, "target", t, out string bone, out var offset);
                var world = TransformOf(c, "target", tn, bone).position + offset;
                target = InPositionSpace(c, n, world);
            }
            var plan = ScriptedMoves.Plan(from, target, stop);
            var tf = n.Doc.Transform;
            float yaw = plan.Yaw ?? (tf != null ? tf.Rot.Y : 0f);
            var o = new ExecuteOptions { Record = HistoryMode.None, Source = CommandSource.Bridge };
            CommandKit.Check(c.Session.Set(n.Id, "transform.rot", new Vec3(0f, yaw, 0f), o), "key");
            if (stance > 0f)
            {
                if (n.Kind == NodeKind.Chara) CommandKit.Check(c.Session.Set(n.Id, "chara.anime.speed", ScriptedMoves.StanceSpeed(plan.Distance, time, stance), o), "stanceSpeed");
                else c.Warn("stanceSpeed: only a character has an animation speed");
            }
            if (!ScriptedMoves.Of(rt).Start(n.Id, from, plan.End, time)) throw CommandKit.NotFound("node " + n.Id + " cannot move now", "key");
            rt.ApplyNow();
            return new BridgeReply().With("to", CommandKit.D3(plan.End)).With("distance", (double)plan.Distance);
        }

        /// <summary>A world point in the space of the node's position (its parent's attach point; the world for a node at the scene's root).</summary>
        static Vector3 InPositionSpace(CoreContext c, TreeNode n, Vector3 world)
        {
            if (n.Parent == null) return world;
            var h = c.Runtime.HolderOf(n.Id);
            var parent = h ? h.parent : null;
            if (!parent) throw CommandKit.NotRealized("node " + n.Id + " is not built", "key", "Pending");
            return parent.InverseTransformPoint(world);
        }

        // ------------------------------------------------------------ camera.follow

        static object Follow(CoreContext c)
        {
            var rt = c.Runtime;
            float seconds = Num(c, "seconds", DefaultSeconds);
            var part = ScriptedFollow.Of(rt);
            if (!(seconds > 0f))
            {
                part.Stop();
                return new BridgeReply().With("following", false).With("until", null);
            }
            var t = WetCommands.Raw(c, "target") ?? throw new BridgeException(BridgeError.Missing("target", ArgTypes.Any));
            var n = NodeBone(c, "target", t, out string bone, out var offset);
            var followed = TransformOf(c, "target", n, bone);
            var rot = Vec(c, "rot") ?? DefaultRot;
            float dist = Num(c, "dist", DefaultDist);
            var spec = new FollowSpec
            {
                NodeId = n.Id, Bone = bone, Offset = offset,
                Rot = rot, RotTo = Vec(c, "rotTo") ?? rot,
                Dist = dist, DistTo = Num(c, "distTo", dist),
                Fov = Num(c, "fov", 0f), Seconds = seconds,
                Above = CommandKit.OptBool(c, "above") ?? false,
            };
            if (!part.Start(spec, followed, FovShown(rt))) throw CommandKit.NotFound("the camera cannot be followed now");
            return new BridgeReply().With("following", true).With("until", rt.Clock.Time + seconds);
        }

        static float FovShown(SceneRuntime rt)
        {
            var cam = rt.GetPart<SceneViewCamera>()?.Camera;
            if (cam) return cam.fieldOfView;
            return rt.Model.Settings.Camera.Current?.Fov ?? SceneViewCamera.DefaultFov;
        }

        // ------------------------------------------------------------ dof

        static object Dof(CoreContext c)
        {
            var rt = c.Runtime;
            var part = ScriptedDof.Of(rt);
            foreach (var name in DofIgnored)
                if (c.Args.Kind(name) != ArgKind.Missing) c.Warn(name + ": " + DofIgnoredWarning);
            if (CommandKit.OptBool(c, "near") == false) c.Warn("near: " + NearWarning);
            bool release = CommandKit.OptBool(c, "release") ?? false, reset = CommandKit.OptBool(c, "reset") ?? false;
            if (release || reset)
            {
                part.Release();
                rt.ApplyNow();
                return Reply(rt, part);
            }
            var ch = new DofChange { On = CommandKit.OptBool(c, "on") };
            bool direct = !CommandKit.Absent(c, "size") || !CommandKit.Absent(c, "aperture");
            bool range = !CommandKit.Absent(c, "sharp") || !CommandKit.Absent(c, "full");
            if (direct && range) throw CommandKit.BadArgs("sharp", "size / aperture or sharp / full", "give size / aperture or sharp / full, not both");
            if (range)
            {
                double sharp = WetCommands.Number(c, "sharp") ?? TimelineBuildInput.DefaultSharp, full = WetCommands.Number(c, "full") ?? TimelineBuildInput.DefaultFull;
                if (sharp < 0) throw CommandKit.BadArgs("sharp", "a number not below 0");
                TimelineBuildInput.FromRange(sharp, full, out double size, out double aperture, "full");
                ch.Size = Math.Max(0, Math.Min(2, size));
                ch.Aperture = Math.Max(0, Math.Min(0.99, aperture));
                ch.Sharp = sharp;
                ch.Full = full;
            }
            else
            {
                float? size = WetCommands.Number(c, "size"), aperture = WetCommands.Number(c, "aperture");
                if (size.HasValue) ch.Size = Math.Max(0, Math.Min(2, (double)size.Value));
                if (aperture.HasValue) ch.Aperture = Math.Max(0, Math.Min(0.99, (double)aperture.Value));
            }
            float? blur = WetCommands.Number(c, "blur");
            if (blur.HasValue) ch.Blur = blur.Value;
            var fk = c.Args.Kind("focus");
            if (fk != ArgKind.Missing)
            {
                ch.HasFocus = true;
                ch.Focus = FocusOf(c, fk == ArgKind.Null ? null : WetCommands.Raw(c, "focus"));
            }
            ch.Blend = Math.Max(0f, Num(c, "blend", 0f));
            string ease = CommandKit.OptString(c, "ease") ?? ScriptedDof.EaseInOut;
            if (Array.IndexOf(ScriptedDof.Eases, ease) < 0) throw CommandKit.BadArgs("ease", ArgTypes.Enum(ScriptedDof.Eases));
            ch.Ease = ease;
            if (part.Change(ch) == null) throw CommandKit.NotFound("the depth of field cannot be taken now");
            rt.ApplyNow();
            return Reply(rt, part);
        }

        static OverrideFocus FocusOf(CoreContext c, JToken f)
        {
            if (f == null || f.Type == JTokenType.Null) return OverrideFocus.Pivot;
            if (f.Type == JTokenType.String)
            {
                string s = (string)f;
                if (s == "target") return OverrideFocus.Pivot;
                return OverrideFocus.OfNode(CommandKit.NodeOf(c, "focus", s).Id);
            }
            if (f.Type == JTokenType.Integer || f.Type == JTokenType.Float) throw CommandKit.BadArgs("focus", "\"target\", a node's key, {key, bone, offset}, {point} or {dist}", "keys are node ids");
            if (!(f is JObject o)) throw CommandKit.BadArgs("focus", "\"target\", a node's key, {key, bone, offset}, {point} or {dist}");
            if (o["point"] != null && o["point"].Type != JTokenType.Null)
            {
                foreach (var m in o.Properties())
                    if (m.Name != "point") WetCommands.UnknownMember(c, "focus", m.Name, "point");
                var p = AdvancedPoseCommands.JsonVec3(o["point"], "focus.point").Value;
                return OverrideFocus.OfPoint(p.X, p.Y, p.Z);
            }
            if (o["dist"] != null && o["dist"].Type != JTokenType.Null)
            {
                foreach (var m in o.Properties())
                    if (m.Name != "dist") WetCommands.UnknownMember(c, "focus", m.Name, "dist");
                var d = o["dist"];
                if (d.Type != JTokenType.Float && d.Type != JTokenType.Integer) throw CommandKit.BadArgs("focus.dist", ArgTypes.Float);
                return OverrideFocus.OfDist(CommandKit.Finite((float)(double)d, "focus.dist"));
            }
            foreach (var m in o.Properties())
                if (m.Name != "key" && m.Name != "bone" && m.Name != "offset") WetCommands.UnknownMember(c, "focus", m.Name, "key, bone or offset");
            var k = o["key"];
            if (k == null || k.Type == JTokenType.Null) throw new BridgeException(BridgeError.Missing("focus.key", ArgTypes.Node));
            if (k.Type != JTokenType.String) throw CommandKit.BadArgs("focus.key", ArgTypes.Node, "keys are node ids");
            var n = CommandKit.NodeOf(c, "focus.key", (string)k);
            var b = o["bone"];
            var off = AdvancedPoseCommands.JsonVec3(o["offset"], "focus.offset");
            if (b == null || b.Type == JTokenType.Null)
            {
                if (off.HasValue) c.Warn("focus.offset without a bone is ignored: the node's point is the focus");
                return OverrideFocus.OfNode(n.Id);
            }
            if (b.Type != JTokenType.String || string.IsNullOrEmpty((string)b)) throw CommandKit.BadArgs("focus.bone", ArgTypes.String);
            var v = off ?? Vec3.Zero;
            return OverrideFocus.OfBone(n.Id, (string)b, v.X, v.Y, v.Z);
        }

        static object FocusJson(OverrideFocus f)
        {
            if (f == null) return "target";
            switch (f.Kind)
            {
                case OverrideFocusKind.Node: return f.Node;
                case OverrideFocusKind.NodeBone: return new BridgeReply().With("key", f.Node).With("bone", f.Bone).With("offset", new[] { f.X, f.Y, f.Z });
                case OverrideFocusKind.Point: return new BridgeReply().With("point", new[] { f.X, f.Y, f.Z });
                case OverrideFocusKind.Dist: return new BridgeReply().With("dist", f.Dist);
                default: return "target";
            }
        }

        static BridgeReply Reply(SceneRuntime rt, ScriptedDof part)
        {
            var v = part.Value ?? ScriptedDof.Shown(rt);
            string driver = part.Holding ? ScriptedDof.Owner : rt.Overrides.WinnerOf(null, OverrideChannel.DepthOfField)?.Owner ?? "scene";
            var dof = new BridgeReply
            {
                ["on"] = v.Enabled,
                ["size"] = v.FocalSize,
                ["aperture"] = v.Aperture,
                ["sharp"] = part.Sharp,
                ["full"] = part.Full,
                ["blur"] = v.Blur,
                ["focus"] = FocusJson(part.Holding ? part.Focus : v.FocusTo ?? v.Focus),
                ["driver"] = driver,
                ["blending"] = part.Blending,
            };
            return new BridgeReply().With("dof", dof);
        }

        // ------------------------------------------------------------ chara.accessoryAxis

        static object AccessoryAxisOf(CoreContext c)
        {
            var r = CommandKit.Built(c);
            int slot = CommandKit.OptInt(c, "slot") ?? 0;
            if (slot < 0) throw CommandKit.BadArgs("slot", "a slot number not below 0");
            var a = AccessoryAxis.Of(r, slot, out string error) ?? throw CommandKit.NotFound("character " + r.NodeId + ": " + error, "slot");
            return new BridgeReply()
                .With("axis", CommandKit.D3(a.Axis))
                .With("center", CommandKit.D3(a.Center))
                .With("muzzle", CommandKit.D3(a.Muzzle))
                .With("anchor", CommandKit.D3(a.Anchor));
        }

        /// <summary>The muzzle of a character's accessory slot (the `from: {key, accessory}` of the stain and shot commands); not_found when it cannot be read.</summary>
        internal static Vector3 Muzzle(CoreContext c, string arg, TreeNode n, int slot)
        {
            var r = CommandKit.Built(c, CommandKit.KeyKind.Chara.Check(n, arg + ".key"), arg + ".key");
            var a = AccessoryAxis.Of(r, slot, out string error) ?? throw CommandKit.NotFound("character " + n.Id + ": " + error, arg + ".accessory");
            return a.Muzzle;
        }
    }
}
