// The commands of constraints (group "constraint", owner core, Since 1.2.0, experimental): the scene's list of relations between nodes,
// bones and IK targets.
//   constraint.list                       the entries with how each stands now
//   constraint.add, .set, .remove, .clear, .move
//   constraint.status                     the plan: what runs in which order, with the evaluation's time per frame
// An end is {key, bone?} and a target may name {key, ik} instead. An entry is named by its id, in the member `constraint`. Writes go through StudioOps (one history
// entry each, `undo: false` and a client's batch as every property command; a loop, a refused component set, a bone the built node lacks
// and a target on a part that is not editable store nothing). `merge` chains the edits of one entry into one history entry. In
// constraint.set a label given as null is cleared, an offset member given as null goes back to the identity, and an offset given as null
// clears all three.
using System;
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;

namespace HS2Next.App.Commands
{
    public static class ConstraintCommands
    {
        /// <summary>The commands of this file (CoreCommands.Phase65Names).</summary>
        public static readonly string[] Names =
        {
            "constraint.list", "constraint.add", "constraint.set", "constraint.remove", "constraint.clear", "constraint.move", "constraint.status",
        };

        /// <summary>CommandSpec.Since of these commands.</summary>
        public const string Since65 = "1.2.0";
        /// <summary>Their help group.</summary>
        public const string GroupName = "constraint";

        // `id` is an envelope field, so an entry is named by this member, in requests and in the replies of add and set.
        const string EntryArg = "constraint";
        const string EndDoc = "{key, bone?}: a node, or a bone of a character or an item by its address";
        const string TargetDoc = "{key, bone?, ik?}: a node, a bone of a character by its address, or an IK target of a character by name; not bone and ik together";
        const string ComponentsDoc = "a set of position, rotation, scale, lookAt, parent; parent stands without position, rotation and lookAt, lookAt without rotation";
        const string OffsetDoc = "{pos, rot, scale}: rot in Euler degrees; a member no component uses is at its identity";
        const string StateDoc = "active, off, sourceMissing, targetMissing, cycle, protected, bad, pending or limit";

        static CommandSpec Spec(string name, string summary, CommandFlags flags) =>
            CommandKit.Spec(name, summary, GroupName).WithSince(Since65).WithFlags(flags | CommandFlags.Experimental);

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, Spec("constraint.list", "The scene's constraints in list order, each with how it stands now.", CommandFlags.ReadOnly)
                    .Arg("key", ArgTypes.Node, "only the entries with this node at either end")
                    .Return("constraints", ArgTypes.ArrayOf(ArgTypes.Object), "{id, label, on, weight, source {key, bone}, target {key, bone, ik}, components, offset {pos, rot, scale}, state (" + StateDoc + "), lag (1: it follows its source one frame behind, else 0), effective (the target takes the value now), reason (what the state names, or on an active entry the text key of the note its row shows; else null)}")
                    .Example("{\"cmd\":\"constraint.list\",\"key\":\"<id>\"}"),
                List);
            CommandKit.Register(r, app, Spec("constraint.add", "Adds a constraint (one history entry): the target follows the source in the components named, laid over the model, which stays as it is. A loop of constraints and a set of components the target cannot take are refused and nothing is stored; mosaic parts are not editable, so such a bone is refused as a target and allowed as a source. A bone of a character that is not built is stored without being checked.", CommandFlags.Mutates)
                    .Arg("source", ArgTypes.Object, EndDoc, true)
                    .Arg("target", ArgTypes.Object, TargetDoc, true)
                    .Arg("components", ArgTypes.ArrayOf(ArgTypes.String), ComponentsDoc, true)
                    .Arg("keepOffset", ArgTypes.Bool, "true: the offset that keeps the target where it shows now; false: the identity", false, true)
                    .Arg("weight", ArgTypes.Float, "0..1: how far the value is blended over what shows without the constraint", false, 1f)
                    .Arg("on", ArgTypes.Bool, "false: it is stored and does not act", false, true)
                    .Arg("label", ArgTypes.String, "the row's text, at most 256 characters")
                    .Arg("index", ArgTypes.Int, "its place in the list (default: the end)")
                    .Return(EntryArg, ArgTypes.String, "the new entry's id")
                    .Return("state", ArgTypes.String, StateDoc)
                    .Return("lag", ArgTypes.Int, "1: it follows its source one frame behind, else 0")
                    .Return("offset", ArgTypes.Object, OffsetDoc)
                    .Example("{\"cmd\":\"constraint.add\",\"source\":{\"key\":\"<id>\",\"bone\":\"cf_J_Hand_R\"},\"target\":{\"key\":\"<id>\"},\"components\":[\"parent\"]}"),
                Add);
            CommandKit.Register(r, app, Spec("constraint.set", "Changes a constraint (one history entry): what is given changes, the rest stays. A change that changes nothing adds no entry. A new source or target that closes a loop is refused. takeOffset needs both ends built and reads the values as shown, so on an active constraint it changes nothing: switch it off, pose the target, take the offset, switch it on.", CommandFlags.Mutates)
                    .Arg(EntryArg, ArgTypes.String, "the entry's id", true)
                    .Arg("on", ArgTypes.Bool, "the switch")
                    .Arg("weight", ArgTypes.Float, "0..1")
                    .Arg("label", ArgTypes.String, "the row's text; null clears it")
                    .Arg("source", ArgTypes.Object, EndDoc)
                    .Arg("target", ArgTypes.Object, TargetDoc)
                    .Arg("components", ArgTypes.ArrayOf(ArgTypes.String), ComponentsDoc)
                    .Arg("offset", ArgTypes.Object, "{pos?, rot?, scale?}: members left out keep their value, a member given as null goes back to its identity; null clears all three")
                    .Arg("takeOffset", ArgTypes.Bool, "true: the offset becomes what keeps the target where it shows now, after the other members of this request; not with offset", false, false)
                    .Arg("merge", ArgTypes.Bool, "join the history entry of the previous edit of this constraint", false, false)
                    .Return(EntryArg, ArgTypes.String, "the entry's id")
                    .Return("state", ArgTypes.String, StateDoc)
                    .Return("lag", ArgTypes.Int, "1: it follows its source one frame behind, else 0")
                    .Return("offset", ArgTypes.Object, OffsetDoc)
                    .Example("{\"cmd\":\"constraint.set\",\"constraint\":\"<id>\",\"weight\":0.5}"),
                Set);
            CommandKit.Register(r, app, Spec("constraint.remove", "Removes a constraint (one history entry); its target shows what it would without it.", CommandFlags.Mutates)
                    .Arg(EntryArg, ArgTypes.String, "the entry's id", true)
                    .Return("removed", ArgTypes.Int, "entries removed")
                    .Example("{\"cmd\":\"constraint.remove\",\"constraint\":\"<id>\"}"),
                Remove);
            CommandKit.Register(r, app, Spec("constraint.clear", "Removes every constraint of the scene (one history entry).", CommandFlags.Mutates)
                    .Return("removed", ArgTypes.Int, "entries removed")
                    .Example("{\"cmd\":\"constraint.clear\"}"),
                Clear);
            CommandKit.Register(r, app, Spec("constraint.move", "Moves a constraint in the list (one history entry). The list's order decides where two constraints write one target and where nothing else orders them.", CommandFlags.Mutates)
                    .Arg(EntryArg, ArgTypes.String, "the entry's id", true)
                    .Arg("index", ArgTypes.Int, "its new place", true)
                    .Return("index", ArgTypes.Int, "its place now")
                    .Example("{\"cmd\":\"constraint.move\",\"constraint\":\"<id>\",\"index\":0}"),
                Move);
            CommandKit.Register(r, app, Spec("constraint.status", "How the constraints run now: the counts, the order of a frame and the entries one frame behind; with perf, the evaluation's time per frame.", CommandFlags.ReadOnly)
                    .Arg("perf", ArgTypes.Bool, "add perf: the evaluation's time per frame over the frames since the last reset (at most the last 4,096)", false, false)
                    .Arg("reset", ArgTypes.Bool, "with perf: start a new window after this reply", false, false)
                    .Return("count", ArgTypes.Int, "entries of the list")
                    .Return("active", ArgTypes.Int, "entries that act now")
                    .Return("passActive", ArgTypes.Bool, "the scene steps its characters itself, in the order below")
                    .Return("order", ArgTypes.ArrayOf(ArgTypes.Object), "{unit (a node id or an entry id), kind (chara, item or constraint)} in the order of a frame, empty while the scene does not step its characters itself; a constraint on a bone runs inside its character's step and has no row")
                    .Return("lagged", ArgTypes.ArrayOf(ArgTypes.String), "the ids of the entries one frame behind")
                    .Return("perf", ArgTypes.Object, "with perf: {frames, medianUs, p95Us, maxUs, walkUs}: walkUs, the median of what a walk of the pass took beside the characters' steps", false)
                    .Example("{\"cmd\":\"constraint.status\",\"perf\":true}"),
                Status);
        }

        // ------------------------------------------------------------ arguments

        // The entry's id: a string, as stored.
        static string Id(CoreContext c)
        {
            var k = c.Args.Kind(EntryArg);
            if (CommandKit.Absent(k)) throw new BridgeException(BridgeError.Missing(EntryArg, ArgTypes.String));
            return CommandKit.OptString(c, EntryArg);
        }

        // A member a nested object should not hold: a warning, or bad_args under strict (as an unknown argument of a request).
        static void UnknownMember(CoreContext c, string arg, string name, string expected)
        {
            if (c.Ctx.Strict) throw CommandKit.BadArgs(arg + "." + name, expected, "unknown member '" + name + "'");
            c.Warn("unknown member '" + name + "' of " + arg + " ignored");
        }

        static string NestedText(ICommandArgs a, string arg, string member)
        {
            var k = a.Kind(member);
            if (CommandKit.Absent(k)) return null;
            if (k != ArgKind.String) throw CommandKit.BadArgs(arg + "." + member, ArgTypes.String);
            return a.Get<string>(member);
        }

        // An end: {key, bone?} and on a target {key, bone?, ik?}. The key takes what every node key takes.
        static ConstraintEnd End(CoreContext c, string arg, bool target)
        {
            string expected = target ? "key, bone or ik" : "key or bone";
            if (c.Args.Kind(arg) != ArgKind.Object) throw CommandKit.BadArgs(arg, "{" + (target ? "key, bone?, ik?" : "key, bone?") + "}");
            var a = c.Args.Nested(arg);
            foreach (string name in a.Names)
                if (name != "key" && name != "bone" && !(target && name == "ik"))
                {
                    if (name == "ik") throw CommandKit.BadArgs(arg + ".ik", "a node or a bone", "an IK target is no source");
                    UnknownMember(c, arg, name, expected);
                }
            string key = NestedText(a, arg, "key");
            if (key == null) throw new BridgeException(BridgeError.Missing(arg + ".key", ArgTypes.Node));
            var n = CommandKit.NodeOf(c, arg + ".key", key);
            return new ConstraintEnd(n.Id, NestedText(a, arg, "bone"), target ? NestedText(a, arg, "ik") : null);
        }

        static float? Weight(CoreContext c)
        {
            float? w = CommandKit.OptFloat(c, "weight");
            return w == null ? (float?)null : CommandKit.Finite(w.Value, "weight");
        }

        static ExecuteOptions Options(CoreContext c, string label, string mergeOf)
        {
            var o = CommandKit.Exec(c, label);
            if (mergeOf != null) o.MergeKey = "constraint:" + mergeOf;
            return o;
        }

        // ------------------------------------------------------------ replies

        static object OffsetReply(ConstraintOffset o) => new BridgeReply { ["pos"] = CommandKit.D3(o.Pos), ["rot"] = CommandKit.D3(o.Rot), ["scale"] = CommandKit.D3(o.Scale) };

        static object SourceReply(ConstraintEnd e) => new BridgeReply { ["key"] = e?.Node, ["bone"] = e?.Bone };

        static object TargetReply(ConstraintEnd e) => new BridgeReply { ["key"] = e?.Node, ["bone"] = e?.Bone, ["ik"] = e?.Ik };

        static object Row(ConstraintInfo i)
        {
            var e = i.Entry;
            return new BridgeReply
            {
                ["id"] = e.Id,
                ["label"] = e.Label,
                ["on"] = e.On,
                ["weight"] = (double)e.Weight,
                ["source"] = SourceReply(e.Source),
                ["target"] = TargetReply(e.Target),
                ["components"] = new List<string>(e.Components),
                ["offset"] = OffsetReply(e.Offset),
                ["state"] = i.State,
                ["lag"] = i.Lag,
                ["effective"] = i.Effective,
                ["reason"] = i.Reason,
            };
        }

        static object Stand(ConstraintInfo i) =>
            new BridgeReply().With(EntryArg, i.Entry.Id).With("state", i.State).With("lag", i.Lag).With("offset", OffsetReply(i.Entry.Offset));

        // ------------------------------------------------------------ handlers

        static object List(CoreContext c)
        {
            var n = CommandKit.OptionalNode(c, "key");
            c.Runtime.ApplyNow();
            var rows = new List<object>();
            foreach (var i in CommandKit.Ops(c).ConstraintsOf(n?.Id)) rows.Add(Row(i));
            return new BridgeReply().With("constraints", rows);
        }

        static object Add(CoreContext c)
        {
            var q = new ConstraintAddRequest
            {
                Source = End(c, "source", false),
                Target = End(c, "target", true),
                Components = CommandKit.OptStrings(c, "components"),
                KeepOffset = CommandKit.OptBool(c, "keepOffset") ?? true,
                Weight = Weight(c) ?? 1f,
                On = CommandKit.OptBool(c, "on") ?? true,
                Label = CommandKit.OptString(c, "label") ?? "",
                Index = CommandKit.OptInt(c, "index"),
            };
            var r = CommandKit.Check(c, CommandKit.Ops(c).AddConstraint(q, CommandKit.Exec(c, StudioOps.Labels.ConstraintAdd)));
            return Stand((ConstraintInfo)r.Value);
        }

        static object Set(CoreContext c)
        {
            string id = Id(c);
            var e = new ConstraintEdit
            {
                On = CommandKit.OptBool(c, "on"),
                Weight = Weight(c),
                Components = CommandKit.OptStrings(c, "components"),
                TakeOffset = CommandKit.OptBool(c, "takeOffset") ?? false,
            };
            if (c.Args.Kind("label") == ArgKind.Null) e.Label = "";
            else e.Label = CommandKit.OptString(c, "label");
            if (!CommandKit.Absent(c, "source")) e.Source = End(c, "source", false);
            if (!CommandKit.Absent(c, "target")) e.Target = End(c, "target", true);
            Offset(c, e);
            if (e.On == null && e.Weight == null && e.Components == null && e.Label == null && e.Source == null && e.Target == null && !e.TakeOffset &&
                e.OffsetPos == null && e.OffsetRot == null && e.OffsetScale == null)
                throw CommandKit.BadArgs(EntryArg, "something to change", "give on, weight, label, source, target, components, offset or takeOffset");
            bool merge = CommandKit.OptBool(c, "merge") ?? false;
            string label = e.TakeOffset ? StudioOps.Labels.ConstraintTakeOffset : StudioOps.Labels.Constraint;
            var r = CommandKit.Check(c, CommandKit.Ops(c).SetConstraint(id, e, Options(c, label, merge ? id : null)));
            return Stand(r.Value as ConstraintInfo ?? CommandKit.Ops(c).ConstraintOf(id));
        }

        // offset {pos?, rot?, scale?}: a member given as null, or the whole object as null, goes back to the identity.
        static void Offset(CoreContext c, ConstraintEdit e)
        {
            var k = c.Args.Kind("offset");
            if (k == ArgKind.Missing) return;
            if (k == ArgKind.Null)
            {
                e.OffsetPos = Vec3.Zero;
                e.OffsetRot = Vec3.Zero;
                e.OffsetScale = Vec3.One;
                return;
            }
            bool any = false;
            foreach (var m in AdvancedPoseCommands.JsonArg(c, "offset"))
            {
                string arg = "offset." + m.Key;
                switch (m.Key)
                {
                    case "pos": e.OffsetPos = AdvancedPoseCommands.JsonVec3(m.Value, arg) ?? Vec3.Zero; any = true; break;
                    case "rot": e.OffsetRot = AdvancedPoseCommands.JsonVec3(m.Value, arg) ?? Vec3.Zero; any = true; break;
                    case "scale": e.OffsetScale = AdvancedPoseCommands.JsonVec3(m.Value, arg) ?? Vec3.One; any = true; break;
                    default: UnknownMember(c, "offset", m.Key, "pos, rot or scale"); break;
                }
            }
            if (!any) throw CommandKit.BadArgs("offset", "pos, rot or scale", "give pos, rot or scale");
        }

        static object Remove(CoreContext c)
        {
            var r = CommandKit.Check(c, CommandKit.Ops(c).RemoveConstraint(Id(c), CommandKit.Exec(c, StudioOps.Labels.ConstraintRemove)));
            return new BridgeReply().With("removed", r.Value is int n ? n : 0);
        }

        static object Clear(CoreContext c)
        {
            var r = CommandKit.Check(c, CommandKit.Ops(c).ClearConstraints(CommandKit.Exec(c, StudioOps.Labels.ConstraintClear)));
            return new BridgeReply().With("removed", r.Value is int n ? n : 0);
        }

        static object Move(CoreContext c)
        {
            string id = Id(c);
            int index = CommandKit.OptInt(c, "index") ?? throw new BridgeException(BridgeError.Missing("index", ArgTypes.Int));
            var r = CommandKit.Check(c, CommandKit.Ops(c).MoveConstraint(id, index, CommandKit.Exec(c, StudioOps.Labels.ConstraintReorder)));
            return new BridgeReply().With("index", r.Value is int n ? n : index);
        }

        static object Status(CoreContext c)
        {
            bool perf = CommandKit.OptBool(c, "perf") ?? false, reset = CommandKit.OptBool(c, "reset") ?? false;
            c.Runtime.ApplyNow();
            var ops = CommandKit.Ops(c);
            var part = ops.Constraints;
            var order = new List<object>();
            if (part != null && part.PassActive)
                foreach (var row in part.Order) order.Add(new BridgeReply { ["unit"] = row.Unit, ["kind"] = row.Kind });
            var r = new BridgeReply()
                .With("count", ops.Model.Constraints?.Count ?? 0)
                .With("active", part?.Active ?? 0)
                .With("passActive", part?.PassActive ?? false)
                .With("order", order)
                .With("lagged", part != null ? new List<string>(part.Lagged) : new List<string>());
            if (perf)
            {
                var w = part?.Evaluator?.Perf;
                var o = new BridgeReply();
                if (w != null && w.Stats(out double median, out double p95, out double max)) o.With("frames", w.Count).With("medianUs", median).With("p95Us", p95).With("maxUs", max);
                else o.With("frames", 0).With("medianUs", null).With("p95Us", null).With("maxUs", null);
                var walk = c.Runtime?.PassWalk;
                o.With("walkUs", walk != null && walk.Stats(out double wm, out _, out _) ? wm : (double?)null);
                r["perf"] = o;
                if (reset) { w?.Reset(); walk?.Reset(); }
            }
            return r;
        }
    }
}
