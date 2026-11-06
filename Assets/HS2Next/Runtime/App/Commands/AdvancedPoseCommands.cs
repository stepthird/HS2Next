// The commands of advanced pose (group "advanced pose", owner core, Since 1.2.0, experimental): scene-only values of a character node.
//   pose.bones, .set, .clear                bone corrections by address: added to the pose in the bone's local space
//   pose.chains, .set, .clear               scene values of the swing chains, read in front of the card's values
//   pose.bust, .set, .clear                 scene values of the two breast and two hip chains
//   pose.blendShapes, .set, .clear          blend-shape weights by renderer and shape name
// `key` is a character node. A character that is not built: the reads answer unavailable, the writes store the value without resolving
// it. Writes go through StudioOps (one history entry each, `undo: false` and a client's batch as every property command); `merge` chains
// the edits of one address into one entry. In a set command an explicit JSON null removes a member or a weight.
using System;
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Motion.DynamicBones;
using HS2Next.Motion.Fk;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HS2Next.App.Commands
{
    public static class AdvancedPoseCommands
    {
        /// <summary>The commands of this file (CoreCommands.Phase63Names).</summary>
        public static readonly string[] Names =
        {
            "pose.bones", "pose.bones.set", "pose.bones.clear",
            "pose.chains", "pose.chains.set", "pose.chains.clear",
            "pose.bust", "pose.bust.set", "pose.bust.clear",
            "pose.blendShapes", "pose.blendShapes.set", "pose.blendShapes.clear",
        };

        /// <summary>CommandSpec.Since of these commands.</summary>
        public const string Since63 = "1.2.0";
        /// <summary>Their help group.</summary>
        public const string GroupName = "advanced pose";

        const string V3Doc = "[x, y, z]";

        static CommandSpec Spec(string name, string summary, CommandFlags flags) =>
            CommandKit.Spec(name, summary, GroupName).WithSince(Since63).WithFlags(flags | CommandFlags.Experimental);

        public static void Register(ICommandRegistry r, AppCore app)
        {
            // ---- bones
            CommandKit.Register(r, app, Spec("pose.bones", "The bones of a built character that a correction can address, with their corrections. A correction whose bone this character lacks is listed by stale.", CommandFlags.ReadOnly)
                    .Arg("key", ArgTypes.Node, "a built character", true)
                    .Arg("part", ArgTypes.String, "only this part: body, head, hair:<k>, clothes:<k> or acc:<id> (default: every part)")
                    .Arg("filter", ArgTypes.String, "only bones whose name holds this text, ignoring case")
                    .Arg("correctedOnly", ArgTypes.Bool, "only bones with a correction", false, false)
                    .Arg("limit", ArgTypes.Int, "at most this many rows", false, 2000)
                    .Return("bones", ArgTypes.ArrayOf(ArgTypes.Object), "{bone (the address), name, parent (its address, null at the top of a part), depth, part, fkGroup (null when the bone has no FK row), corrected, pos, rot, scale, shown (false: a later stage writes this bone, so the value does not show now), physics (a swing chain writes this bone), protected (a part that keeps the mosaic in place)}")
                    .Return("stale", ArgTypes.ArrayOf(ArgTypes.String), "stored addresses that act on nothing here")
                    .Return("count", ArgTypes.Int, "the bones that match, before the limit")
                    .Example("{\"cmd\":\"pose.bones\",\"key\":\"<id>\",\"part\":\"head\",\"filter\":\"Mouth\"}"),
                Bones);
            CommandKit.Register(r, app, Spec("pose.bones.set", "Sets corrections of bones (one history entry): pos is added to the local position, the local rotation is multiplied by rot (Euler degrees) and each axis of the local scale by scale. Members left out keep their value; a correction that changes nothing removes the entry. A part that keeps the mosaic in place answers unsupported.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, "a character", true)
                    .Arg("bone", ArgTypes.String, "one bone's address: its name, or hair:<k>/<name>, clothes:<k>/<name>, acc:<id>/<name> with #<n> for the n-th of a repeated name")
                    .Arg("pos", ArgTypes.Vec3, "with bone: the position offset, each component within +-1000")
                    .Arg("rot", ArgTypes.Euler, "with bone: the rotation offset, Euler degrees")
                    .Arg("scale", ArgTypes.Vec3, "with bone: the scale factor, each component in 0.001..1000")
                    .Arg("bones", ArgTypes.Object, "several bones instead of bone: {address: {pos, rot, scale}}, each member optional")
                    .Arg("keepMissing", ArgTypes.Bool, "store a correction for a bone a built character lacks (it acts when the bone is there); without it such a bone answers not_found", false, false)
                    .Arg("merge", ArgTypes.Bool, "with one bone: join the history entry of the previous edit of this bone", false, false)
                    .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "the addresses whose correction changed")
                    .Return("removed", ArgTypes.ArrayOf(ArgTypes.String), "the addresses whose entry left")
                    .Return("bones", ArgTypes.Object, "{address: {pos, rot, scale}} of the named bones that have a correction now")
                    .Example("{\"cmd\":\"pose.bones.set\",\"key\":\"<id>\",\"bone\":\"cf_J_Hand_Index02_L\",\"rot\":[0,0,15]}"),
                BonesSet);
            CommandKit.Register(r, app, Spec("pose.bones.clear", "Removes corrections (one history entry): one bone, several, or all of them.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, "a character", true)
                    .Arg("bone", ArgTypes.String, "one address")
                    .Arg("bones", ArgTypes.ArrayOf(ArgTypes.String), "several addresses")
                    .Arg("all", ArgTypes.Bool, "true: every correction of this character")
                    .Return("removed", ArgTypes.Int, "entries removed")
                    .Example("{\"cmd\":\"pose.bones.clear\",\"key\":\"<id>\",\"all\":true}"),
                BonesClear);

            // ---- chains
            CommandKit.Register(r, app, Spec("pose.chains", "The swing chains of a built character with their values: base as the prefab and the card give them, override as the scene stores them, values as they act. A stored chain this character lacks is listed by stale.", CommandFlags.ReadOnly)
                    .Arg("key", ArgTypes.Node, "a built character", true)
                    .Return("chains", ArgTypes.ArrayOf(ArgTypes.Object), "{chain (the address), part, root, category, particles, active, noShake, values, base, override (null: none)}; values and base: {enabled, weight, damping, elasticity, stiffness, inert, radius, gravity, force, freezeAxis}")
                    .Return("stale", ArgTypes.ArrayOf(ArgTypes.String), "stored addresses that act on nothing here")
                    .Example("{\"cmd\":\"pose.chains\",\"key\":\"<id>\"}"),
                Chains);
            var chainSet = Spec("pose.chains.set", "Sets scene values of one swing chain (one history entry). They are read in front of the card's values, which stay as they are; a member given as null goes back to the card's value.", CommandFlags.Mutates)
                .Arg("key", ArgTypes.Node, "a character", true)
                .Arg("chain", ArgTypes.String, "the chain's address: hair:<k>/<root>, clothes:<k>/<root>, acc:<id>/<root> or other/<root>, with #<n> for the n-th chain of that root in the part", true);
            ChainMembers(chainSet);
            CommandKit.Register(r, app, chainSet
                    .Arg("merge", ArgTypes.Bool, "join the history entry of the previous edit of this chain", false, false)
                    .Return("chain", ArgTypes.String, "the address")
                    .Return("override", ArgTypes.Object, "the members stored now (null: none)")
                    .Return("values", ArgTypes.Object, "the values as they act (null when the character is not built)")
                    .Example("{\"cmd\":\"pose.chains.set\",\"key\":\"<id>\",\"chain\":\"hair:1/<root>\",\"damping\":0.2,\"gravity\":null}"),
                ChainsSet);
            CommandKit.Register(r, app, Spec("pose.chains.clear", "Removes the scene values of one swing chain or of all of them (one history entry); the card's values act again.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, "a character", true)
                    .Arg("chain", ArgTypes.String, "one address")
                    .Arg("all", ArgTypes.Bool, "true: every chain of this character")
                    .Return("removed", ArgTypes.Int, "entries removed")
                    .Example("{\"cmd\":\"pose.chains.clear\",\"key\":\"<id>\",\"all\":true}"),
                ChainsClear);

            // ---- bust and hips
            CommandKit.Register(r, app, Spec("pose.bust", "The two breast and two hip chains of a built character with their values: base as the card gives them, override as the scene stores them, values as they act.", CommandFlags.ReadOnly)
                    .Arg("key", ArgTypes.Node, "a built character", true)
                    .Return("chains", ArgTypes.ArrayOf(ArgTypes.Object), "{chain (breastL, breastR, hipL, hipR), active, values, base, override (null: none)}; values and base: {enabled, gravity, force, damping, elasticity, stiffness, inert}")
                    .Example("{\"cmd\":\"pose.bust\",\"key\":\"<id>\"}"),
                Bust);
            var bustSet = Spec("pose.bust.set", "Sets scene values of one breast or hip chain (one history entry). The card's values stay as they are; a member given as null goes back to the card's value.", CommandFlags.Mutates)
                .Arg("key", ArgTypes.Node, "a character", true)
                .Arg("chain", ArgTypes.Enum("breastL", "breastR", "hipL", "hipR"), "the chain", true);
            BustMembers(bustSet);
            CommandKit.Register(r, app, bustSet
                    .Arg("merge", ArgTypes.Bool, "join the history entry of the previous edit of this chain", false, false)
                    .Return("chain", ArgTypes.String, "the chain")
                    .Return("override", ArgTypes.Object, "the members stored now (null: none)")
                    .Return("values", ArgTypes.Object, "the values as they act (null when the character is not built or the body has no such chain)")
                    .Example("{\"cmd\":\"pose.bust.set\",\"key\":\"<id>\",\"chain\":\"breastL\",\"damping\":0.3}"),
                BustSet_);
            CommandKit.Register(r, app, Spec("pose.bust.clear", "Removes the scene values of one breast or hip chain or of all four (one history entry); the card's values act again.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, "a character", true)
                    .Arg("chain", ArgTypes.Enum("breastL", "breastR", "hipL", "hipR"), "one chain")
                    .Arg("all", ArgTypes.Bool, "true: all four")
                    .Return("removed", ArgTypes.Int, "chains cleared")
                    .Example("{\"cmd\":\"pose.bust.clear\",\"key\":\"<id>\",\"all\":true}"),
                BustClear);

            // ---- blend shapes
            CommandKit.Register(r, app, Spec("pose.blendShapes", "The skinned renderers with blend shapes of a built character, with each shape's current weight and stored override. What this character lacks is listed by stale.", CommandFlags.ReadOnly)
                    .Arg("key", ArgTypes.Node, "a built character", true)
                    .Arg("renderer", ArgTypes.String, "only this renderer's address: head/<name>, body/<name>, hair:<k>/<name>, clothes:<k>/<name> or acc:<id>/<name>, with #<n> for the n-th of a repeated name")
                    .Arg("part", ArgTypes.String, "only this part: head, body, hair:<k>, clothes:<k> or acc:<id>")
                    .Arg("filter", ArgTypes.String, "only shapes whose name holds this text, ignoring case; a renderer without one is left out")
                    .Return("renderers", ArgTypes.ArrayOf(ArgTypes.Object), "{renderer (the address), part, name, protected (a part that keeps the mosaic in place), shapes: [{name, weight, override (null: none)}]}")
                    .Return("stale", ArgTypes.ArrayOf(ArgTypes.String), "stored renderers this character lacks (the address), stored shapes a renderer lacks (<address>:<shape>)")
                    .Example("{\"cmd\":\"pose.blendShapes\",\"key\":\"<id>\",\"renderer\":\"head/o_tooth\"}"),
                Blend);
            CommandKit.Register(r, app, Spec("pose.blendShapes.set", "Sets blend-shape weights of one renderer (one history entry). A weight wins over the face on every step; a weight given as null is removed, and the face or the mesh value shows again. A part that keeps the mosaic in place answers unsupported.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, "a character", true)
                    .Arg("renderer", ArgTypes.String, "the renderer's address: head/<name>, body/<name>, hair:<k>/<name>, clothes:<k>/<name> or acc:<id>/<name>, with #<n> for the n-th of a repeated name", true)
                    .Arg("shape", ArgTypes.String, "one shape's name, with weight")
                    .Arg("weight", ArgTypes.Float, "with shape: its weight, -200..300 (0..100 on a slider); null removes it")
                    .Arg("weights", ArgTypes.Object, "several shapes instead of shape: {shape: weight}; null removes that weight")
                    .Arg("keepMissing", ArgTypes.Bool, "store a weight for a renderer or a shape a built character lacks (it acts when that is there); without it such a target answers not_found", false, false)
                    .Arg("merge", ArgTypes.Bool, "join the history entry of the previous edit of this renderer", false, false)
                    .Return("renderer", ArgTypes.String, "the address")
                    .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "the shapes whose weight changed")
                    .Return("removed", ArgTypes.ArrayOf(ArgTypes.String), "the shapes whose weight left")
                    .Example("{\"cmd\":\"pose.blendShapes.set\",\"key\":\"<id>\",\"renderer\":\"head/o_tooth\",\"shape\":\"<shape>\",\"weight\":50}"),
                BlendSet);
            CommandKit.Register(r, app, Spec("pose.blendShapes.clear", "Removes blend-shape weights (one history entry): one shape of a renderer, a renderer's, or all of them.", CommandFlags.Mutates)
                    .Arg("key", ArgTypes.Node, "a character", true)
                    .Arg("renderer", ArgTypes.String, "one renderer's address")
                    .Arg("shape", ArgTypes.String, "with renderer: only this shape")
                    .Arg("all", ArgTypes.Bool, "true: every weight of this character")
                    .Return("removed", ArgTypes.Int, "weights removed")
                    .Example("{\"cmd\":\"pose.blendShapes.clear\",\"key\":\"<id>\",\"all\":true}"),
                BlendClear);
        }

        static void ChainMembers(CommandSpec s)
        {
            s.Arg("enabled", ArgTypes.Bool, "false stops the chain and its bones return to the rest pose; true does not override a card's no-shake")
                .Arg("weight", ArgTypes.Float, "0..1")
                .Arg("damping", ArgTypes.Float, "0..1")
                .Arg("elasticity", ArgTypes.Float, "0..1")
                .Arg("stiffness", ArgTypes.Float, "0..1")
                .Arg("inert", ArgTypes.Float, "0..1")
                .Arg("radius", ArgTypes.Float, "from 0")
                .Arg("gravity", ArgTypes.Vec3, V3Doc)
                .Arg("force", ArgTypes.Vec3, V3Doc)
                .Arg("freezeAxis", ArgTypes.Int, "0 none, 1 x, 2 y, 3 z");
        }

        static void BustMembers(CommandSpec s)
        {
            s.Arg("enabled", ArgTypes.Bool, "false stops the chain; true never overrides the FK breast or the animation's switches")
                .Arg("gravity", ArgTypes.Vec3, V3Doc)
                .Arg("force", ArgTypes.Vec3, V3Doc)
                .Arg("damping", ArgTypes.Float, "0..1")
                .Arg("elasticity", ArgTypes.Float, "0..1")
                .Arg("stiffness", ArgTypes.Float, "0..1")
                .Arg("inert", ArgTypes.Float, "0..1");
        }

        // ------------------------------------------------------------ shared

        static CharaAdvancedPose Block(TreeNode n) => (n.State as CharaNodeState)?.AdvancedPose;

        // the built character of a node, or null (a write stores the value without resolving it)
        static CharaRealization BuiltOrNull(CoreContext c, TreeNode n)
        {
            var r = c.Runtime.FindChara(n.Id);
            return r != null && r.State == RealizationState.Built && !r.InGraveyard && r.Root ? r : null;
        }

        /// <summary>A member of a set command: false when it is absent. An explicit JSON null gives <paramref name="isNull"/> true.</summary>
        static bool Given(CoreContext c, string arg, out bool isNull)
        {
            var k = c.Args.Kind(arg);
            isNull = k == ArgKind.Null;
            return k != ArgKind.Missing;
        }

        static void Member(CoreContext c, string arg, ref bool? slot, ref bool any)
        {
            if (!Given(c, arg, out bool isNull)) return;
            any = true;
            slot = isNull ? null : CommandKit.OptBool(c, arg);
        }

        static void Member(CoreContext c, string arg, ref float? slot, ref bool any)
        {
            if (!Given(c, arg, out bool isNull)) return;
            any = true;
            slot = isNull ? null : CommandKit.Finite(CommandKit.OptFloat(c, arg).Value, arg);
        }

        static void Member(CoreContext c, string arg, ref int? slot, ref bool any)
        {
            if (!Given(c, arg, out bool isNull)) return;
            any = true;
            slot = isNull ? null : CommandKit.OptInt(c, arg);
        }

        static void Member(CoreContext c, string arg, ref Vec3? slot, ref bool any)
        {
            if (!Given(c, arg, out bool isNull)) return;
            any = true;
            slot = isNull ? null : CommandKit.OptVec3(c, arg, "[x, y, z]");
        }

        static ExecuteOptions Options(CoreContext c, string label, string mergeOf)
        {
            var o = CommandKit.Exec(c, label);
            if (mergeOf != null) o.MergeKey = mergeOf;
            return o;
        }

        // the merge key of an edit: one per node, kind of value and address
        static string MergeKey(CoreContext c, string kind, TreeNode n, string address) =>
            (CommandKit.OptBool(c, "merge") ?? false) ? "pose." + kind + ":" + n.Id + ":" + address : null;

        internal static JObject JsonArg(CoreContext c, string arg)
        {
            if (c.Args.Kind(arg) != ArgKind.Object) throw CommandKit.BadArgs(arg, ArgTypes.Object);
            try { return JObject.Parse(c.Args.RawJson(arg)); }
            catch (Exception e) { throw CommandKit.BadArgs(arg, ArgTypes.Object, e.Message); }
        }

        // An [x, y, z] of finite numbers inside a nested object.
        internal static Vec3? JsonVec3(JToken t, string arg)
        {
            if (t == null || t.Type == JTokenType.Null) return null;
            if (!(t is JArray a) || a.Count != 3) throw CommandKit.BadArgs(arg, "[x, y, z]");
            var v = new float[3];
            for (int i = 0; i < 3; i++)
            {
                if (a[i].Type != JTokenType.Float && a[i].Type != JTokenType.Integer) throw CommandKit.BadArgs(arg + "[" + i + "]", ArgTypes.Float);
                v[i] = CommandKit.Finite((float)(double)a[i], arg + "[" + i + "]");
            }
            return new Vec3(v[0], v[1], v[2]);
        }

        // A member a nested object should not hold: a warning, or bad_args under strict (as an unknown argument of a request).
        static void UnknownMember(CoreContext c, string arg, string name)
        {
            if (c.Ctx.Strict) throw CommandKit.BadArgs(arg + "." + name, "pos, rot or scale", "unknown member '" + name + "'");
            c.Warn("unknown member '" + name + "' of " + arg + " ignored");
        }

        static string Part(CoreContext c, bool bones)
        {
            string part = CommandKit.OptString(c, "part");
            if (part == null) return null;
            string expected = bones ? "body, head, hair:<k>, clothes:<k> or acc:<id>" : "head, body, hair:<k>, clothes:<k> or acc:<id>";
            if (!PoseAddressGrammar.TryParsePart(part, out var kind, out _, out _, out string why)) throw CommandKit.BadArgs("part", expected, "bad part \"" + part + "\": " + why);
            if (kind == PosePart.Other) throw CommandKit.BadArgs("part", expected, "bad part \"" + part + "\"");
            return part;
        }

        static double[] Num3(Vec3 v) => CommandKit.D3(v);
        static double[] Num3(Vector3 v) => CommandKit.D3(v);

        // ------------------------------------------------------------ pose.bones

        static object Bones(CoreContext c)
        {
            var r = CommandKit.Built(c);
            string part = Part(c, true);
            string filter = CommandKit.OptString(c, "filter");
            bool correctedOnly = CommandKit.OptBool(c, "correctedOnly") ?? false;
            int limit = CommandKit.OptInt(c, "limit") ?? 2000;
            if (limit < 0) throw CommandKit.BadArgs("limit", "a number from 0");
            var block = Block(r.Node);
            var state = AdvancedPoseDriver.Of(r);
            var chain = AdvancedPoseResolver.ChainBones(r);
            var fk = FkGroups(r);
            var bones = new List<object>();
            int count = 0;
            foreach (var row in AdvancedPoseResolver.Bones(r, part))
            {
                var t = row.Bone;
                if (filter != null && t.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                BoneCorrection v = null;
                bool corrected = block != null && block.Bones.TryGetValue(row.Address, out v);
                if (correctedOnly && !corrected) continue;
                count++;
                if (bones.Count >= limit) continue;
                var e = state?.Set.Of(row.Address);
                bones.Add(new BridgeReply
                {
                    ["bone"] = row.Address,
                    ["name"] = t.name,
                    ["parent"] = row.Parent,
                    ["depth"] = row.Depth,
                    ["part"] = row.Part,
                    ["fkGroup"] = fk.TryGetValue(t, out var g) ? g.ToString() : null,
                    ["corrected"] = corrected,
                    ["pos"] = Num3(v?.Pos ?? Vec3.Zero),
                    ["rot"] = Num3(v?.Rot ?? Vec3.Zero),
                    ["scale"] = Num3(v?.Scale ?? Vec3.One),
                    ["shown"] = e != null ? BoneCorrections.Shown(e) : !corrected || v.IsIdentity,
                    ["physics"] = chain.Contains(t),
                    ["protected"] = AdvancedPoseGuard.Bone(r, t),
                });
            }
            return new BridgeReply().With("bones", bones).With("stale", state != null ? new List<string>(state.StaleBones) : new List<string>()).With("count", count);
        }

        static Dictionary<Transform, FkGroup> FkGroups(CharaRealization r)
        {
            var d = new Dictionary<Transform, FkGroup>();
            var table = FkDriver.TableFor(r);
            if (table == null) return d;
            foreach (var b in table.Bones)
            {
                if (b == null) continue;
                if (b.Bone) d[b.Bone] = b.Group;
                if (b.Partner) d[b.Partner] = b.Group;
            }
            return d;
        }

        static object CorrectionReply(BoneCorrection v) => new BridgeReply { ["pos"] = Num3(v.Pos), ["rot"] = Num3(v.Rot), ["scale"] = Num3(v.Scale) };

        static object BonesSet(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            bool many = FileRoots.OneOf(c, "bone", "bones") == "bones";
            var edits = new List<BoneCorrectionEdit>();
            if (many)
            {
                foreach (var a in new[] { "pos", "rot", "scale" })
                    if (c.Args.Has(a)) throw CommandKit.BadArgs(a, "the bones map", a + " goes with bone; in bones each address holds its own");
                foreach (var kv in JsonArg(c, "bones"))
                {
                    string arg = "bones." + kv.Key;
                    if (!(kv.Value is JObject o)) throw CommandKit.BadArgs(arg, "{pos, rot, scale}");
                    var e = new BoneCorrectionEdit { Address = kv.Key, Arg = arg, Pos = JsonVec3(o["pos"], arg + ".pos"), Rot = JsonVec3(o["rot"], arg + ".rot"), Scale = JsonVec3(o["scale"], arg + ".scale") };
                    foreach (var m in o)
                        if (m.Key != "pos" && m.Key != "rot" && m.Key != "scale") UnknownMember(c, arg, m.Key);
                    if (e.Pos == null && e.Rot == null && e.Scale == null) throw CommandKit.BadArgs(arg, "pos, rot or scale");
                    edits.Add(e);
                }
                if (edits.Count == 0) throw CommandKit.BadArgs("bones", "at least one bone", "no bones");
            }
            else
            {
                var e = new BoneCorrectionEdit
                {
                    Address = CommandKit.OptString(c, "bone", "a bone address"),
                    Pos = CommandKit.OptVec3(c, "pos", "[x, y, z]"),
                    Rot = CommandKit.OptVec3(c, "rot", "[x, y, z]"),
                    Scale = CommandKit.OptVec3(c, "scale", "[x, y, z]"),
                };
                if (e.Pos == null && e.Rot == null && e.Scale == null) throw CommandKit.BadArgs("pos", "pos, rot or scale", "give pos, rot or scale");
                edits.Add(e);
            }
            bool merge = CommandKit.OptBool(c, "merge") ?? false;
            if (merge && edits.Count > 1) throw CommandKit.BadArgs("merge", "one bone", "merge chains the edits of one bone");
            string mergeKey = merge ? MergeKey(c, "bones", n, PoseAddress.Canonical(PoseAddressForm.Bone, edits[0].Address) ?? edits[0].Address) : null;
            var res = CommandKit.Check(c, CommandKit.Ops(c).SetBoneCorrections(n.Id, edits, CommandKit.OptBool(c, "keepMissing") ?? false, Options(c, StudioOps.Labels.BoneCorrection, mergeKey)));
            var state = new BridgeReply();
            var block = Block(n);
            foreach (var e in edits)
                if (block != null && block.Bones.TryGetValue(e.Address, out var v)) state[e.Address] = CorrectionReply(v);
            return new BridgeReply().With("changed", new List<string>(res.Changed)).With("removed", res.Value as List<string> ?? new List<string>()).With("bones", state);
        }

        static object BonesClear(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            var o = CommandKit.Exec(c, StudioOps.Labels.ClearBoneCorrections);
            bool all = All(c, "bone", "bones");
            IEnumerable<string> addresses = null;
            if (!all) addresses = c.Args.Has("bones") ? CommandKit.OptStrings(c, "bones") : new[] { CommandKit.OptString(c, "bone", "a bone address") };
            var res = CommandKit.Check(c, CommandKit.Ops(c).ClearBoneCorrections(n.Id, addresses, o));
            return new BridgeReply().With("removed", (res.Value as List<string>)?.Count ?? 0);
        }

        // Exactly one of the named arguments or `all: true`; returns whether all was given.
        internal static bool All(CoreContext c, params string[] others)
        {
            bool all = c.Args.Kind("all") != ArgKind.Missing && c.Args.Kind("all") != ArgKind.Null;
            if (all)
            {
                if (CommandKit.OptBool(c, "all") != true) throw CommandKit.BadArgs("all", "true", "all is true or left out");
            }
            int given = all ? 1 : 0;
            foreach (var a in others) if (c.Args.Has(a)) given++;
            if (given == 0) throw CommandKit.BadArgs("all", "one of " + string.Join(", ", others) + " or all: true", "say what to clear: " + string.Join(", ", others) + " or all: true");
            if (given > 1) throw CommandKit.BadArgs("all", "one of " + string.Join(", ", others) + " or all: true", "give one of " + string.Join(", ", others) + " or all: true");
            return all;
        }

        // ------------------------------------------------------------ pose.chains

        static object ChainValuesReply(DynamicBoneChain ch, ChainValues over)
        {
            var s = ch.Source;
            float weight = ch.BaseWeight;
            bool enabled = ch.BaseEnabled;
            var a = over?.Applied();
            return new BridgeReply
            {
                ["enabled"] = a?.Enabled ?? enabled,
                ["weight"] = (double)(a?.Weight ?? weight),
                ["damping"] = (double)(a?.Damping ?? (s ? s.m_Damping : 0f)),
                ["elasticity"] = (double)(a?.Elasticity ?? (s ? s.m_Elasticity : 0f)),
                ["stiffness"] = (double)(a?.Stiffness ?? (s ? s.m_Stiffness : 0f)),
                ["inert"] = (double)(a?.Inert ?? (s ? s.m_Inert : 0f)),
                ["radius"] = (double)(a?.Radius ?? (s ? s.m_Radius : 0f)),
                ["gravity"] = a?.Gravity != null ? Num3(a.Gravity.Value) : Num3(s ? s.m_Gravity : Vector3.zero),
                ["force"] = a?.Force != null ? Num3(a.Force.Value) : Num3(s ? s.m_Force : Vector3.zero),
                ["freezeAxis"] = a?.FreezeAxis ?? (s ? s.m_FreezeAxis : 0),
            };
        }

        static object ChainOverride(ChainValues v)
        {
            if (v == null || v.IsEmpty) return null;
            var o = new BridgeReply();
            if (v.Enabled.HasValue) o["enabled"] = v.Enabled.Value;
            if (v.Weight.HasValue) o["weight"] = (double)v.Weight.Value;
            if (v.Damping.HasValue) o["damping"] = (double)v.Damping.Value;
            if (v.Elasticity.HasValue) o["elasticity"] = (double)v.Elasticity.Value;
            if (v.Stiffness.HasValue) o["stiffness"] = (double)v.Stiffness.Value;
            if (v.Inert.HasValue) o["inert"] = (double)v.Inert.Value;
            if (v.Radius.HasValue) o["radius"] = (double)v.Radius.Value;
            if (v.Gravity.HasValue) o["gravity"] = Num3(v.Gravity.Value);
            if (v.Force.HasValue) o["force"] = Num3(v.Force.Value);
            if (v.FreezeAxis.HasValue) o["freezeAxis"] = v.FreezeAxis.Value;
            return o;
        }

        static string CategoryName(DynamicBoneCategory x)
        {
            switch (x)
            {
                case DynamicBoneCategory.Hair: return "hair";
                case DynamicBoneCategory.Clothes: return "clothes";
                case DynamicBoneCategory.Accessory: return "accessory";
                default: return "other";
            }
        }

        static object Chains(CoreContext c)
        {
            var r = CommandKit.Built(c);
            var block = Block(r.Node);
            var rows = AdvancedPoseParts.Chains(r);
            var found = new HashSet<string>(StringComparer.Ordinal);
            var chains = new List<object>(rows.Count);
            foreach (var row in rows)
            {
                found.Add(row.Address);
                ChainValues over = null;
                block?.Chains.TryGetValue(row.Address, out over);
                chains.Add(new BridgeReply
                {
                    ["chain"] = row.Address,
                    ["part"] = row.Part,
                    ["root"] = row.Root,
                    ["category"] = CategoryName(row.Chain.Category),
                    ["particles"] = row.Chain.ParticleCount,
                    ["active"] = row.Chain.Active,
                    ["noShake"] = row.Chain.NoShake,
                    ["values"] = ChainValuesReply(row.Chain, over),
                    ["base"] = ChainValuesReply(row.Chain, null),
                    ["override"] = ChainOverride(over),
                });
            }
            var stale = new List<string>();
            if (block != null)
                foreach (var k in block.Chains.Keys)
                    if (!found.Contains(k)) stale.Add(k);
            return new BridgeReply().With("chains", chains).With("stale", stale);
        }

        static object ChainsSet(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            string text = CommandKit.OptString(c, "chain", "a chain address");
            string address = PoseAddress.Canonical(PoseAddressForm.Chain, text) ?? text;
            var block = Block(n);
            var v = block != null ? AdvancedPoseEdits.ChainOf(block, address) : new ChainValues();
            bool any = false;
            Member(c, "enabled", ref v.Enabled, ref any);
            Member(c, "weight", ref v.Weight, ref any);
            Member(c, "damping", ref v.Damping, ref any);
            Member(c, "elasticity", ref v.Elasticity, ref any);
            Member(c, "stiffness", ref v.Stiffness, ref any);
            Member(c, "inert", ref v.Inert, ref any);
            Member(c, "radius", ref v.Radius, ref any);
            Member(c, "gravity", ref v.Gravity, ref any);
            Member(c, "force", ref v.Force, ref any);
            Member(c, "freezeAxis", ref v.FreezeAxis, ref any);
            if (!any) throw CommandKit.BadArgs("chain", "a member to set", "give a member to set (null removes it)");
            CommandKit.Check(c, CommandKit.Ops(c).SetChainValues(n.Id, address, v, Options(c, StudioOps.Labels.JiggleValues, MergeKey(c, "chains", n, address))));
            var r = BuiltOrNull(c, n);
            var ch = r != null ? AdvancedPoseParts.Chain(r, address) : null;
            ChainValues now = null;
            block?.Chains.TryGetValue(address, out now);
            return new BridgeReply { ["chain"] = address, ["override"] = ChainOverride(now), ["values"] = ch != null ? ChainValuesReply(ch, now) : null };
        }

        static object ChainsClear(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            bool all = All(c, "chain");
            var o = CommandKit.Exec(c, StudioOps.Labels.ClearJiggleValues);
            var res = CommandKit.Check(c, CommandKit.Ops(c).ClearChainValues(n.Id, all ? null : new[] { CommandKit.OptString(c, "chain", "a chain address") }, o));
            return new BridgeReply().With("removed", (res.Value as List<string>)?.Count ?? 0);
        }

        // ------------------------------------------------------------ pose.bust

        static object BustValuesReply(BustChain ch, BustValues over)
        {
            ch.BaseSoft(0, out float d, out float e, out float s, out float i);
            var a = over?.Applied();
            return new BridgeReply
            {
                ["enabled"] = a?.Enabled ?? true,
                ["gravity"] = a?.Gravity != null ? Num3(a.Gravity.Value) : Num3(ch.Gravity),
                ["force"] = a?.Force != null ? Num3(a.Force.Value) : Num3(ch.Source ? ch.Source.Force : Vector3.zero),
                ["damping"] = (double)(a?.Damping ?? d),
                ["elasticity"] = (double)(a?.Elasticity ?? e),
                ["stiffness"] = (double)(a?.Stiffness ?? s),
                ["inert"] = (double)(a?.Inert ?? i),
            };
        }

        static object BustOverride(BustValues v)
        {
            if (v == null || v.IsEmpty) return null;
            var o = new BridgeReply();
            if (v.Enabled.HasValue) o["enabled"] = v.Enabled.Value;
            if (v.Gravity.HasValue) o["gravity"] = Num3(v.Gravity.Value);
            if (v.Force.HasValue) o["force"] = Num3(v.Force.Value);
            if (v.Damping.HasValue) o["damping"] = (double)v.Damping.Value;
            if (v.Elasticity.HasValue) o["elasticity"] = (double)v.Elasticity.Value;
            if (v.Stiffness.HasValue) o["stiffness"] = (double)v.Stiffness.Value;
            if (v.Inert.HasValue) o["inert"] = (double)v.Inert.Value;
            return o;
        }

        static BustChain BustChainOf(CharaRealization r, int kind)
        {
            var stage = r?.Motion ? r.Motion.Get<BustStage>() : null;
            return stage != null && kind >= 0 && kind < stage.Chains.Length ? stage.Chains[kind] : null;
        }

        static object Bust(CoreContext c)
        {
            var r = CommandKit.Built(c);
            var block = Block(r.Node);
            var chains = new List<object>();
            for (int k = 0; k < HS2Next.Save.BustSet.Names.Count; k++)
            {
                var ch = BustChainOf(r, k);
                if (ch == null) continue;
                var over = block?.Bust[k];
                chains.Add(new BridgeReply
                {
                    ["chain"] = HS2Next.Save.BustSet.Names[k],
                    ["active"] = ch.Active,
                    ["values"] = BustValuesReply(ch, over),
                    ["base"] = BustValuesReply(ch, null),
                    ["override"] = BustOverride(over),
                });
            }
            return new BridgeReply().With("chains", chains);
        }

        static object BustSet_(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            string chain = CommandKit.OptString(c, "chain", "breastL, breastR, hipL or hipR");
            int kind = HS2Next.Save.BustSet.KindOf(chain);
            if (kind < 0) throw CommandKit.BadArgs("chain", "breastL, breastR, hipL or hipR");
            var block = Block(n);
            var v = block != null ? AdvancedPoseEdits.BustOf(block, chain) : new BustValues();
            bool any = false;
            Member(c, "enabled", ref v.Enabled, ref any);
            Member(c, "gravity", ref v.Gravity, ref any);
            Member(c, "force", ref v.Force, ref any);
            Member(c, "damping", ref v.Damping, ref any);
            Member(c, "elasticity", ref v.Elasticity, ref any);
            Member(c, "stiffness", ref v.Stiffness, ref any);
            Member(c, "inert", ref v.Inert, ref any);
            if (!any) throw CommandKit.BadArgs("chain", "a member to set", "give a member to set (null removes it)");
            CommandKit.Check(c, CommandKit.Ops(c).SetBustValues(n.Id, chain, v, Options(c, StudioOps.Labels.JiggleValues, MergeKey(c, "bust", n, chain))));
            var ch = BustChainOf(BuiltOrNull(c, n), kind);
            var now = block?.Bust[kind];
            return new BridgeReply { ["chain"] = chain, ["override"] = BustOverride(now), ["values"] = ch != null ? BustValuesReply(ch, now) : null };
        }

        static object BustClear(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            bool all = All(c, "chain");
            var o = CommandKit.Exec(c, StudioOps.Labels.ClearJiggleValues);
            var res = CommandKit.Check(c, CommandKit.Ops(c).ClearBustValues(n.Id, all ? null : CommandKit.OptString(c, "chain", "breastL, breastR, hipL or hipR"), o));
            return new BridgeReply().With("removed", (res.Value as List<string>)?.Count ?? 0);
        }

        // ------------------------------------------------------------ pose.blendShapes

        static object Blend(CoreContext c)
        {
            var r = CommandKit.Built(c);
            string only = CommandKit.OptString(c, "renderer", "a renderer address");
            string part = Part(c, false);
            string filter = CommandKit.OptString(c, "filter");
            if (only != null)
            {
                string canonical = PoseAddress.Canonical(PoseAddressForm.Blend, only);
                if (canonical == null)
                {
                    PoseAddress.TryParse(PoseAddressForm.Blend, only, out _, out string why);
                    throw CommandKit.BadArgs("renderer", "a renderer address", "bad address \"" + only + "\": " + why);
                }
                only = canonical;
                if (!AdvancedPoseParts.Renderer(r, only)) throw CommandKit.NotFound("this character has no renderer \"" + only + "\"", "renderer");
            }
            var block = Block(r.Node);
            var renderers = new List<object>();
            foreach (var row in AdvancedPoseParts.Renderers(r, part))
            {
                if (only != null && row.Address != only) continue;
                var s = row.Renderer;
                var mesh = s.sharedMesh;
                BlendValues over = null;
                block?.BlendShapes.TryGetValue(row.Address, out over);
                var shapes = new List<object>(mesh.blendShapeCount);
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string name = mesh.GetBlendShapeName(i);
                    if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    object stored = null;
                    if (over != null && over.Weights.TryGetValue(name, out float w)) stored = (double)w;
                    shapes.Add(new BridgeReply { ["name"] = name, ["weight"] = (double)s.GetBlendShapeWeight(i), ["override"] = stored });
                }
                if (filter != null && shapes.Count == 0) continue;
                renderers.Add(new BridgeReply
                {
                    ["renderer"] = row.Address,
                    ["part"] = row.Part,
                    ["name"] = row.Name,
                    ["protected"] = AdvancedPoseGuard.Renderer(r, s),
                    ["shapes"] = shapes,
                });
            }
            var stale = new List<string>();
            if (block != null)
                foreach (var kv in block.BlendShapes)
                {
                    var s = PoseAddress.IsCanonical(PoseAddressForm.Blend, kv.Key) ? AdvancedPoseParts.Renderer(r, kv.Key) : null;
                    if (!s) { stale.Add(kv.Key); continue; }
                    foreach (var shape in kv.Value.Weights.Keys)
                        if (!s.sharedMesh || s.sharedMesh.GetBlendShapeIndex(shape) < 0) stale.Add(kv.Key + ":" + shape);
                }
            return new BridgeReply().With("renderers", renderers).With("stale", stale);
        }

        static object BlendSet(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            string text = CommandKit.OptString(c, "renderer", "a renderer address");
            string address = PoseAddress.Canonical(PoseAddressForm.Blend, text) ?? text;
            bool many = FileRoots.OneOf(c, "shape", "weights") == "weights";
            var set = new List<KeyValuePair<string, float>>();
            var remove = new List<string>();
            if (many)
            {
                if (c.Args.Kind("weight") != ArgKind.Missing) throw CommandKit.BadArgs("weight", "the weights map", "weight goes with shape");
                foreach (var kv in JsonArg(c, "weights"))
                {
                    string arg = "weights." + kv.Key;
                    if (kv.Value.Type == JTokenType.Null) { remove.Add(kv.Key); continue; }
                    if (kv.Value.Type != JTokenType.Float && kv.Value.Type != JTokenType.Integer) throw CommandKit.BadArgs(arg, ArgTypes.Float);
                    set.Add(new KeyValuePair<string, float>(kv.Key, CommandKit.Finite((float)(double)kv.Value, arg)));
                }
                if (set.Count == 0 && remove.Count == 0) throw CommandKit.BadArgs("weights", "at least one shape", "no weights");
            }
            else
            {
                string shape = CommandKit.OptString(c, "shape");
                if (!Given(c, "weight", out bool isNull)) throw new BridgeException(BridgeError.Missing("weight", ArgTypes.Float));
                if (isNull) remove.Add(shape);
                else set.Add(new KeyValuePair<string, float>(shape, CommandKit.Finite(CommandKit.OptFloat(c, "weight").Value, "weight")));
            }
            var ops = CommandKit.Ops(c);
            var res = ops.SetBlendWeights(n.Id, address, set, remove, CommandKit.OptBool(c, "keepMissing") ?? false, Options(c, StudioOps.Labels.BlendShapes, MergeKey(c, "blendShapes", n, address)));
            // the single form has no weights map to name
            if (!many && res.Arg != null && res.Arg.StartsWith("weights.", StringComparison.Ordinal))
                res.Arg = res.Code == CommandCodes.BadArgs ? "weight" : "shape";
            CommandKit.Check(c, res);
            return new BridgeReply().With("renderer", address).With("changed", new List<string>(res.Changed)).With("removed", res.Value as List<string> ?? new List<string>());
        }

        static object BlendClear(CoreContext c)
        {
            var n = CommandKit.Chara(c);
            bool all = All(c, "renderer");
            string shape = CommandKit.OptString(c, "shape");
            if (all && shape != null) throw CommandKit.BadArgs("shape", "a renderer", "shape goes with renderer");
            var o = CommandKit.Exec(c, StudioOps.Labels.ClearBlendShapes);
            var res = CommandKit.Check(c, CommandKit.Ops(c).ClearBlendWeights(n.Id, all ? null : CommandKit.OptString(c, "renderer", "a renderer address"), shape, o));
            return new BridgeReply().With("removed", (res.Value as List<string>)?.Count ?? 0);
        }
    }
}
