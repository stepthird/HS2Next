// Unity-side IK operations with capture (Phase 3.4 package B). The FK
// capture rule transposed (K4): enabling IK or a group, or editing targets, first writes every missing target of the
// groups that are or become applied (SetTargets: of all 13) from "the pose you see without IK" (IkCharacter.PoseWithoutIk)
// in the same transaction as the change: one history entry, undo removes them again. A character that is not built gets
// the flags only (ik.captureUnavailable, info). The exclusive preset (IkOptions.Exclusive, K3): switching IK on also
// writes chara.fk.enabled = false in the same transaction (FkOps does the reverse). Every operation applies pending
// model changes first (runtime.ApplyNow) and again after; transactions are opened with CommandBus.BeginOrJoin, so inside
// an open transaction (a bridge batch or command) they become steps of it. Labels: "IK" (enable, groups), "IK targets".
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>One target edit of <see cref="IkOps.SetTargets"/>: null fields keep the effective value.</summary>
    public struct IkTargetEdit
    {
        public Vector3? Pos;
        /// <summary>Euler degrees; used by hands and feet only (others store 0).</summary>
        public Vector3? Rot;
        public IkTargetEdit(Vector3? pos, Vector3? rot) { Pos = pos; Rot = rot; }
    }

    /// <summary>The space of target values: the character root's local space (as stored) or world space.</summary>
    public enum IkSpace { Local, World }

    public sealed class IkOps
    {
        readonly SceneRuntime rt;

        public IkOps(SceneRuntime runtime) { rt = runtime ?? throw new ArgumentNullException(nameof(runtime)); }

        /// <summary>Target names the last operation captured (empty when none).</summary>
        public IReadOnlyList<string> LastCaptured { get; private set; } = new string[0];

        SceneSession Session => rt.Session;

        internal static CommandResult NodeCheck(SceneRuntime rt, string id, out CharaNodeState st)
        {
            st = null;
            var n = rt.Model.Find(id);
            if (n == null) return CommandResult.Error(CommandCodes.NotFound, "no node " + id);
            if (n.Kind != NodeKind.Chara || !(n.State is CharaNodeState cs)) return CommandResult.Error(CommandCodes.WrongKind, id + " is not a character");
            st = cs;
            return null;
        }

        internal static string TargetPath(int i) => "chara.ik.targets." + IkTargets.Names[i];
        internal static string GroupPath(IkGroup g) => "chara.ik.groups." + IkGroupInfo.MemberNames[(int)g];

        internal static IkTarget ToModel(int i, Vector3 pos, Vector3 rot) =>
            new IkTarget { Pos = new Vec3(pos.x, pos.y, pos.z), Rot = IkGroupInfo.UsesRotation((IkTargetId)i) ? new Vec3(rot.x, rot.y, rot.z) : Vec3.Zero };

        internal static IkTarget ToModel(int i, IkTargetValue v) => ToModel(i, v.Pos, v.Rot);

        /// <summary>`chara.ik.enabled` (capturing the missing targets of the flagged groups when IK becomes on; exclusive preset).</summary>
        public CommandResult SetEnabled(string id, bool on, ExecuteOptions o = default)
        {
            rt.ApplyNow();
            var err = NodeCheck(rt, id, out var st);
            if (err != null) return err;
            var groups = new List<IkGroup>();
            if (on && !st.Ik.Enabled)
                for (int g = 0; g < IkGroupInfo.Count; g++) if (IkCharacter.Flag(st.Ik.Groups, (IkGroup)g)) groups.Add((IkGroup)g);
            var sets = new List<(string, object)> { ("chara.ik.enabled", on) };
            if (on && IkOptions.Exclusive && st.Fk.Enabled) sets.Add(("chara.fk.enabled", false));
            return Run(id, st, sets, Missing(id, st, TargetsOf(groups)), o.Label ?? "IK", o);
        }

        /// <summary>`chara.ik.groups.&lt;g&gt;` (capturing the group's missing targets when it becomes applied).</summary>
        public CommandResult SetGroup(string id, IkGroup g, bool on, ExecuteOptions o = default) => SetGroups(id, new[] { g }, on, o);

        /// <summary>Several groups to the same value in one entry (the bridge's `ik.enable {groups}`).</summary>
        public CommandResult SetGroups(string id, IEnumerable<IkGroup> groups, bool on, ExecuteOptions o = default)
        {
            rt.ApplyNow();
            var err = NodeCheck(rt, id, out var st);
            if (err != null) return err;
            var list = (groups ?? Enumerable.Empty<IkGroup>()).Distinct().ToList();
            var sets = list.Select(g => (GroupPath(g), (object)on)).ToList();
            var capture = new List<IkGroup>();
            if (on && st.Ik.Enabled) capture.AddRange(list.Where(g => !IkCharacter.Flag(st.Ik.Groups, g)));
            return Run(id, st, sets, Missing(id, st, TargetsOf(capture)), o.Label ?? "IK", o);
        }

        /// <summary>
        /// Sets target values (unspecified fields keep the effective value; a missing target's base is its capture value)
        /// and captures every other missing target, in one entry "IK targets". World space needs a built character.
        /// </summary>
        public CommandResult SetTargets(string id, IReadOnlyDictionary<IkTargetId, IkTargetEdit> edits, IkSpace space = IkSpace.Local, ExecuteOptions o = default)
        {
            rt.ApplyNow();
            var err = NodeCheck(rt, id, out var st);
            if (err != null) return err;
            if (edits == null || edits.Count == 0) return CommandResult.NoOp(null);
            var r = rt.FindChara(id);
            var c = IkDriver.Of(r);
            Transform root = r != null && r.State == RealizationState.Built && r.Root ? r.Root.transform : null;
            if (space == IkSpace.World && !root) return CommandResult.Error(CommandCodes.Unsupported, "world-space targets need a built character");
            var all = Enumerable.Range(0, IkGroupInfo.TargetCount).ToList();
            var add = Missing(id, st, all);
            var sets = new List<(string, object)>();
            foreach (var kv in edits)
            {
                int i = (int)kv.Key;
                if (i < 0 || i >= IkGroupInfo.TargetCount) return CommandResult.Error(CommandCodes.BadArgs, "unknown IK target " + kv.Key);
                Vector3 pos, rot;
                var cur = st.Ik.Targets[i];
                if (cur != null) { pos = new Vector3(cur.Pos.X, cur.Pos.Y, cur.Pos.Z); rot = new Vector3(cur.Rot.X, cur.Rot.Y, cur.Rot.Z); }
                else if (add.TryGetValue(i, out var cap)) { pos = new Vector3(cap.Pos.X, cap.Pos.Y, cap.Pos.Z); rot = new Vector3(cap.Rot.X, cap.Rot.Y, cap.Rot.Z); }
                else if (c != null && c.Effective(kv.Key).Has) { pos = c.Effective(kv.Key).Pos; rot = c.Effective(kv.Key).Rot; }
                else { pos = Vector3.zero; rot = Vector3.zero; }
                var e = kv.Value;
                if (e.Pos.HasValue) pos = space == IkSpace.World ? root.InverseTransformPoint(e.Pos.Value) : e.Pos.Value;
                if (e.Rot.HasValue) rot = space == IkSpace.World ? (Quaternion.Inverse(root.rotation) * Quaternion.Euler(e.Rot.Value)).eulerAngles : e.Rot.Value;
                add.Remove(i);
                sets.Add((TargetPath(i), ToModel(i, pos, rot)));
            }
            return Run(id, st, sets, add, o.Label ?? "IK targets", o);
        }

        static IEnumerable<int> TargetsOf(IEnumerable<IkGroup> groups) => groups.SelectMany(g => IkGroupInfo.TargetsOf(g)).Select(t => (int)t).Distinct();

        /// <summary>
        /// The capture values of the missing targets among <paramref name="targets"/> (model index → value); empty when none
        /// is missing; empty with ik.captureUnavailable when the character is not built.
        /// </summary>
        public Dictionary<int, IkTarget> Missing(string id, CharaNodeState st, IEnumerable<int> targets)
        {
            var add = new Dictionary<int, IkTarget>();
            var miss = targets.Where(i => st.Ik.Targets[i] == null).ToList();
            if (miss.Count == 0) return add;
            var c = IkDriver.Of(rt.FindChara(id));
            var v = c?.PoseWithoutIk();
            if (v == null)
            {
                rt.ReportOnce(FindingSeverity.Info, "ik.captureUnavailable", id, "character not built: the missing IK targets were not captured");
                return add;
            }
            foreach (int i in miss) if (v[i].Has) add[i] = ToModel(i, v[i]);
            return add;
        }

        CommandResult Run(string id, CharaNodeState st, List<(string path, object value)> sets, Dictionary<int, IkTarget> add, string label, ExecuteOptions o)
        {
            LastCaptured = add.Keys.OrderBy(i => i).Select(i => IkTargets.Names[i]).ToList();
            CommandResult res = null;
            using (var tx = Session.Commands.BeginOrJoin(label, o))
            {
                if (!tx.IsOpen) return tx.Failure;
                foreach (var kv in add.OrderBy(k => k.Key))
                {
                    res = tx.Set(id, TargetPath(kv.Key), kv.Value);
                    if (!res.Ok) break;
                }
                if (res == null || res.Ok)
                    foreach (var (path, value) in sets)
                    {
                        res = tx.Set(id, path, value);
                        if (!res.Ok) break;
                    }
                if (res == null) { tx.Cancel(); return CommandResult.NoOp(null); }
                if (!res.Ok) { tx.Cancel(); rt.ApplyNow(); LastCaptured = new string[0]; return res; }
                res = tx.Commit();
            }
            rt.ApplyNow();
            return res;
        }
    }
}
