// StudioOps, constraints: the edits of the scene's list, shared by the commands and the panel. Each edit is one ConstraintEditCommand, so
// one history entry, and every list a change stores is a fresh object. A request is checked against the built scene first; a refusal
// stores nothing and answers the command's code:
//   not_found    an id, a node, a bone a built character or item lacks
//   wrong_kind   a bone on a node without bones, an IK target on anything but a character
//   bad_args     a set of components that is unknown, refused or that the target cannot take, a weight outside 0..1, a limit, a loop, a
//                taken offset with an end that does not resolve, an index out of range
//   unsupported  a target on a bone of an item, a mosaic part as a bone target
//   busy         as every edit
// A loop is judged on the built hierarchy when an edit gives an end: the edit is refused and nothing changes. An end that is not built
// yet cannot be judged here, nor can a loop that appears later: the scene's compile suspends such an entry and keeps it.
// Options: a MergeKey chains the edits of one entry into one history entry; Record None applies without an entry and keeps the history;
// an open transaction (a UI drag, a bridge batch) takes the edit as a step.
// Result: Value = the entry's ConstraintInfo after the edit (add, set), the number removed (remove, clear), the entry's index (move).
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Scenes.Constraints;
using HS2Next.Scenes.Overrides;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>A new constraint.</summary>
    public sealed class ConstraintAddRequest
    {
        public ConstraintEnd Source, Target;
        public IReadOnlyList<string> Components;
        /// <summary>Take the offset that keeps the target where it shows now; false starts from the identity.</summary>
        public bool KeepOffset = true;
        public float Weight = 1f;
        public bool On = true;
        public string Label = "";
        /// <summary>Its place in the list; null = the end.</summary>
        public int? Index;
    }

    /// <summary>A change of one constraint: what is given changes, the rest stays.</summary>
    public sealed class ConstraintEdit
    {
        public bool? On;
        public float? Weight;
        /// <summary>Null keeps the label; "" clears it.</summary>
        public string Label;
        public ConstraintEnd Source, Target;
        public IReadOnlyList<string> Components;
        public Vec3? OffsetPos, OffsetRot, OffsetScale;
        /// <summary>The offset becomes what keeps the target where it shows now, after the other members of this edit. Not with an offset member.</summary>
        public bool TakeOffset;
    }

    /// <summary>How an entry stands: what <c>constraint.list</c> gives and what a row shows.</summary>
    public sealed class ConstraintInfo
    {
        /// <summary>The stored entry; it is never changed in place, and neither may a reader change it.</summary>
        public ConstraintEntry Entry;
        /// <summary>One of <see cref="ConstraintStates"/>.</summary>
        public string State;
        /// <summary>1 when the entry reads its source one frame behind, else 0.</summary>
        public int Lag;
        /// <summary>The target takes the value now.</summary>
        public bool Effective;
        /// <summary>What the state names (a node or an address, the rows of a loop, why the entry cannot act) or, on an active entry, a string
        /// key of the note its row shows; else null.</summary>
        public string Reason;
    }

    public sealed partial class StudioOps
    {
        /// <summary>The rows a refusal of a loop names, at most.</summary>
        const int MaxLoopRows = 8;

        /// <summary>The part that holds the compiled list; null on a runtime without the standard parts.</summary>
        public ConstraintPart Constraints => rt.GetPart<ConstraintPart>();

        // ------------------------------------------------------------ reads

        /// <summary>How the entry <paramref name="id"/> stands after the last compile; null for an unknown id.</summary>
        public ConstraintInfo ConstraintOf(string id)
        {
            var e = Model.Constraints?.Find(id);
            return e == null ? null : InfoOf(e);
        }

        /// <summary>The entries in list order; with <paramref name="nodeId"/> those that have this node at either end.</summary>
        public List<ConstraintInfo> ConstraintsOf(string nodeId = null)
        {
            var rows = new List<ConstraintInfo>();
            var list = Model.Constraints;
            if (list == null) return rows;
            foreach (var e in list.Entries)
                if (e != null && (nodeId == null || e.Source?.Node == nodeId || e.Target?.Node == nodeId)) rows.Add(InfoOf(e));
            return rows;
        }

        ConstraintInfo InfoOf(ConstraintEntry e)
        {
            var info = new ConstraintInfo { Entry = e, State = ConstraintStates.Pending };
            var part = Constraints;
            var row = part?.RowOf(e.Id);
            if (row == null) return info;
            info.State = row.State;
            info.Lag = row.Lag ? 1 : 0;
            info.Effective = part.IsEffective(row, out string key) && row.State == ConstraintStates.Active;
            info.Reason = row.State == ConstraintStates.Active ? key : row.State == ConstraintStates.Protected ? AdvancedPoseGuard.Refusal : row.Detail;
            return info;
        }

        // ------------------------------------------------------------ edits

        /// <summary>Adds a constraint (label «Add constraint»); Value = its ConstraintInfo. A taken offset needs both ends built: an end that is
        /// not leaves the identity, with a warning.</summary>
        public OpResult AddConstraint(ConstraintAddRequest q, ExecuteOptions o = default)
        {
            if (q == null) throw new ArgumentNullException(nameof(q));
            var list = Model.Constraints;
            int count = list?.Count ?? 0;
            if (count >= ConstraintLimits.MaxEntries) return OpResult.Fail(CommandCodes.BadArgs, "the scene has the most constraints it can hold: " + ConstraintLimits.MaxEntries);
            int index = q.Index ?? count;
            if (index < 0 || index > count) return OpResult.BadArgs("index", "0.." + count);
            var fail = CheckWeight(q.Weight) ?? CheckLabel(q.Label) ?? CheckComponents(q.Components);
            if (fail != null) return fail;
            rt.ApplyNow();
            var e = new ConstraintEntry
            {
                Id = Session.NewId(),
                Label = q.Label ?? "",
                On = q.On,
                Weight = q.Weight,
                Source = q.Source?.Clone() ?? new ConstraintEnd(),
                Target = q.Target?.Clone() ?? new ConstraintEnd(),
                Components = ConstraintComponents.Ordered(q.Components),
            };
            fail = CheckSource(e.Source) ?? CheckTarget(e.Target, e) ?? CheckCombination(e, null);
            if (fail != null) return fail;
            var after = new ConstraintList(list?.Entries ?? new List<ConstraintEntry>());
            after.Entries.Insert(index, e);
            fail = CheckLoop(after, e.Id);
            if (fail != null) return fail;
            string warning = null;
            if (q.KeepOffset)
            {
                var part = Constraints;
                if (part != null && part.TryTakeOffset(e, out var taken))
                {
                    e.Offset = taken;
                    fail = CheckOffset(e.Offset);
                    if (fail != null) return fail;
                }
                else warning = "an end is not built: the offset stays at the identity";
            }
            var r = RunConstraint(ConstraintEditCommand.ReplaceList(after, Labels.ConstraintAdd), Labels.ConstraintAdd, o);
            if (!r.Ok) return r;
            if (warning != null) r.Warnings.Add(warning);
            r.Value = ConstraintOf(e.Id);
            return r;
        }

        /// <summary>Changes the entry <paramref name="id"/> (label «Constraint», or «Take offset» for a taken offset); Value = its ConstraintInfo. No
        /// change is a no-op with no history entry; a taken offset equal to the stored one but for rounding is no change.</summary>
        public OpResult SetConstraint(string id, ConstraintEdit edit, ExecuteOptions o = default)
        {
            if (edit == null) throw new ArgumentNullException(nameof(edit));
            var list = Model.Constraints;
            int index = list?.IndexOf(id) ?? -1;
            if (index < 0) return OpResult.NotFound("no constraint " + id, "constraint");
            var before = list.Entries[index];
            var e = before.Clone();
            if (edit.TakeOffset && (edit.OffsetPos != null || edit.OffsetRot != null || edit.OffsetScale != null))
                return OpResult.BadArgs("takeOffset", "takeOffset or offset", "takeOffset and offset cannot stand together");
            if (edit.On != null) e.On = edit.On.Value;
            if (edit.Weight != null)
            {
                var bad = CheckWeight(edit.Weight.Value);
                if (bad != null) return bad;
                e.Weight = edit.Weight.Value;
            }
            if (edit.Label != null)
            {
                var bad = CheckLabel(edit.Label);
                if (bad != null) return bad;
                e.Label = edit.Label;
            }
            if (edit.Components != null)
            {
                var bad = CheckComponents(edit.Components);
                if (bad != null) return bad;
                e.Components = ConstraintComponents.Ordered(edit.Components);
            }
            rt.ApplyNow();
            bool moved = false;
            if (edit.Source != null && !edit.Source.SameAs(e.Source))
            {
                e.Source = edit.Source.Clone();
                var bad = CheckSource(e.Source);
                if (bad != null) return bad;
                moved = true;
            }
            if (edit.Target != null && !edit.Target.SameAs(e.Target))
            {
                e.Target = edit.Target.Clone();
                var bad = CheckTarget(e.Target, e);
                if (bad != null) return bad;
                moved = true;
            }
            if (moved || edit.Components != null)
            {
                var bad = CheckCombination(e, before);
                if (bad != null) return bad;
            }
            if (edit.OffsetPos != null) e.Offset.Pos = edit.OffsetPos.Value;
            if (edit.OffsetRot != null) e.Offset.Rot = edit.OffsetRot.Value;
            if (edit.OffsetScale != null) e.Offset.Scale = edit.OffsetScale.Value;
            if (moved)
            {
                var bad = CheckLoop(list.With(index, e), id);
                if (bad != null) return bad;
            }
            if (edit.TakeOffset)
            {
                var part = Constraints;
                if (part == null || !part.TryTakeOffset(e, out var taken)) return OpResult.BadArgs("takeOffset", null, "takeOffset needs both ends built");
                if (!SameOffset(taken, e.Offset))
                {
                    e.Offset.Pos = taken.Pos;
                    e.Offset.Rot = taken.Rot;
                    e.Offset.Scale = taken.Scale;
                }
            }
            var off = CheckOffset(e.Offset);
            if (off != null && !SameOffset(e.Offset, before.Offset)) return off;
            string label = edit.TakeOffset ? Labels.ConstraintTakeOffset : Labels.Constraint;
            var r = RunConstraint(ConstraintEditCommand.ReplaceEntry(e, label), label, o);
            if (r.Ok) r.Value = ConstraintOf(id);
            return r;
        }

        /// <summary>Removes the entry <paramref name="id"/> (label «Remove constraint»); Value = 1.</summary>
        public OpResult RemoveConstraint(string id, ExecuteOptions o = default)
        {
            var list = Model.Constraints;
            int index = list?.IndexOf(id) ?? -1;
            if (index < 0) return OpResult.NotFound("no constraint " + id, "constraint");
            var after = new ConstraintList(list.Entries);
            after.Entries.RemoveAt(index);
            var r = RunConstraint(ConstraintEditCommand.ReplaceList(after, Labels.ConstraintRemove), Labels.ConstraintRemove, o);
            if (r.Ok) r.Value = 1;
            return r;
        }

        /// <summary>Removes every entry (label «Clear constraints»); Value = the number removed.</summary>
        public OpResult ClearConstraints(ExecuteOptions o = default)
        {
            int count = Model.Constraints?.Count ?? 0;
            var r = RunConstraint(ConstraintEditCommand.Clear(), Labels.ConstraintClear, o);
            if (r.Ok) r.Value = count;
            return r;
        }

        /// <summary>Moves the entry <paramref name="id"/> to <paramref name="index"/> of the list (label «Reorder constraints»); Value = the index.</summary>
        public OpResult MoveConstraint(string id, int index, ExecuteOptions o = default)
        {
            var list = Model.Constraints;
            int from = list?.IndexOf(id) ?? -1;
            if (from < 0) return OpResult.NotFound("no constraint " + id, "constraint");
            if (index < 0 || index >= list.Count) return OpResult.BadArgs("index", "0.." + (list.Count - 1));
            var after = new ConstraintList(list.Entries);
            var e = after.Entries[from];
            after.Entries.RemoveAt(from);
            after.Entries.Insert(index, e);
            var r = RunConstraint(ConstraintEditCommand.ReplaceList(after, Labels.ConstraintReorder), Labels.ConstraintReorder, o);
            if (r.Ok) r.Value = index;
            return r;
        }

        // One command, so one entry, with the merge a caller asks for; the compile follows at once, so the result can say how the entry stands.
        OpResult RunConstraint(ConstraintEditCommand cmd, string label, ExecuteOptions o)
        {
            var r = OpResult.From(Session.Commands.Execute(cmd, Labelled(o, label)));
            if (r.Ok) rt.ApplyNow();
            return r;
        }

        // ------------------------------------------------------------ checks

        static OpResult CheckWeight(float w) => w >= 0f && w <= 1f ? null : OpResult.BadArgs("weight", "0..1");

        static OpResult CheckLabel(string label) =>
            label == null || label.Length <= ConstraintLimits.MaxLabel ? null : OpResult.BadArgs("label", "at most " + ConstraintLimits.MaxLabel + " characters");

        static OpResult CheckComponents(IReadOnlyList<string> components)
        {
            string p = ConstraintComponents.SetProblem(components);
            return p == null ? null : OpResult.BadArgs("components", string.Join(", ", ConstraintComponents.All), "bad components: " + p);
        }

        static OpResult CheckOffset(ConstraintOffset o) =>
            OffsetRange(o.Pos, "offset.pos") ?? OffsetRange(o.Rot, "offset.rot") ?? OffsetRange(o.Scale, "offset.scale");

        static OpResult OffsetRange(Vec3 v, string arg)
        {
            const float m = ConstraintLimits.MaxOffset;
            return v.X >= -m && v.X <= m && v.Y >= -m && v.Y <= m && v.Z >= -m && v.Z <= m ? null : OpResult.BadArgs(arg, "each component within +-" + m.ToString("0", System.Globalization.CultureInfo.InvariantCulture));
        }

        // The address in its canonical form, stored into the end; a text that does not read is refused.
        static OpResult CanonicalBone(ConstraintEnd end, string arg)
        {
            string canonical = PoseAddress.Canonical(PoseAddressForm.Bone, end.Bone);
            if (canonical != null)
            {
                end.Bone = canonical;
                return null;
            }
            PoseAddress.TryParse(PoseAddressForm.Bone, end.Bone, out _, out string why);
            return OpResult.BadArgs(arg, "an address", "bad address \"" + end.Bone + "\": " + why);
        }

        // True when the node's bones can be named now: a bone it lacks is then an error, not something still to come.
        bool BonesKnown(TreeNode n)
        {
            if (n.Kind == NodeKind.Chara) return PoseBuilt(n.Id) != null;
            return rt.Find(n.Id) is ItemRealization it && it.State == RealizationState.Built && it.Instance;
        }

        OpResult CheckSource(ConstraintEnd end)
        {
            var n = Find(end.Node, "source", out var fail);
            if (n == null) return fail;
            if (end.Bone == null) return null;
            if (n.Kind != NodeKind.Chara && n.Kind != NodeKind.Item)
                return OpResult.WrongKind("node " + n.Id + " is a " + KindName(n) + ", which has no bones", "source.bone", "chara or item");
            fail = CanonicalBone(end, "source.bone");
            if (fail != null) return fail;
            if (ConstraintResolver.Source(rt, end).State == ConstraintEndState.Missing && BonesKnown(n))
                return OpResult.NotFound("node " + n.Id + " has no bone \"" + end.Bone + "\"", "source.bone");
            return null;
        }

        OpResult CheckTarget(ConstraintEnd end, ConstraintEntry e)
        {
            var n = Find(end.Node, "target", out var fail);
            if (n == null) return fail;
            if (end.Bone != null && end.Ik != null) return OpResult.BadArgs("target", "bone or ik", "the target names a bone and an IK target");
            if (end.Ik != null)
            {
                if (n.Kind != NodeKind.Chara) return OpResult.WrongKind("node " + n.Id + " is a " + KindName(n) + ", not a character", "target.ik", "chara");
                return Array.IndexOf(IkTargets.Names, end.Ik) >= 0 ? null : OpResult.BadArgs("target.ik", string.Join(", ", IkTargets.Names), "unknown IK target \"" + end.Ik + "\"");
            }
            if (end.Bone == null) return null;
            if (n.Kind == NodeKind.Item) return OpResult.Unsupported("a bone of an item cannot be a target");
            if (n.Kind != NodeKind.Chara) return OpResult.WrongKind("node " + n.Id + " is a " + KindName(n) + ", which has no bones", "target.bone", "chara");
            fail = CanonicalBone(end, "target.bone");
            if (fail != null) return fail;
            var state = ConstraintResolver.Target(rt, end, ConstraintMath.PartsOf(e.Components)).State;
            if (state == ConstraintEndState.Protected) return OpResult.Unsupported(AdvancedPoseGuard.Refusal);
            if (state == ConstraintEndState.Missing && BonesKnown(n)) return OpResult.NotFound("node " + n.Id + " has no bone \"" + end.Bone + "\"", "target.bone");
            return null;
        }

        static readonly OverridePath ScaleChannel = OverridePath.Parse("transform.scale");

        // What the components ask of the target: what the entry had before an edit stays allowed.
        OpResult CheckCombination(ConstraintEntry e, ConstraintEntry before)
        {
            string p = e.BadReason();
            if (p != null && p != before?.BadReason()) return OpResult.BadArgs("components", null, "bad components: " + p);
            if (e.Target.IsNode && e.Has(ConstraintComponents.Scale))
            {
                var n = Model.Find(e.Target.Node);
                if (n != null && !ScaleChannel.Accepts(n.Kind)) return OpResult.BadArgs("components", null, "bad components: a " + KindName(n) + " has no scale");
            }
            return null;
        }

        // A loop through the entry `id` in the list as it would be: the edit is refused, naming the rows.
        OpResult CheckLoop(ConstraintList after, string id)
        {
            var part = Constraints;
            if (part == null) return null;
            var loop = part.LoopOf(after, id);
            if (loop.Length == 0) return null;
            var rows = new List<string>();
            for (int i = 0; i < loop.Length && rows.Count < MaxLoopRows - 1; i++) rows.Add(ConstraintResolver.RowText(rt, after.Entries[loop[i]]));
            rows.Add(rows[0]);
            return OpResult.BadArgs("source", null, "constraint cycle: " + string.Join(" -> ", rows));
        }

        // The same offset but for the rounding of a read: positions and scales within 1e-5 of their size, rotations within a thousandth of a degree.
        static bool SameOffset(ConstraintOffset a, ConstraintOffset b)
        {
            if (!Near(a.Pos, b.Pos) || !Near(a.Scale, b.Scale)) return false;
            var p = ConstraintMath.ValueOf(a).Rot;
            var q = ConstraintMath.ValueOf(b).Rot;
            double dot = p.X * q.X + p.Y * q.Y + p.Z * q.Z + p.W * q.W;
            return 2 * Math.Acos(Math.Min(1, Math.Abs(dot))) * (180 / Math.PI) <= 1e-3;
        }

        static bool Near(Vec3 a, Vec3 b) => Near(a.X, b.X) && Near(a.Y, b.Y) && Near(a.Z, b.Z);

        static bool Near(double a, double b) => Math.Abs(a - b) <= 1e-5 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
    }
}
