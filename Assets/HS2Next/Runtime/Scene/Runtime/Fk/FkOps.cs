// Unity-side FK operations with capture: enabling FK or a group for bones that have
// no value freezes the pose you see — the bones of the groups that become applied and lack values are read from the
// built character (localEulerAngles of the current final pose) and set in the same transaction as the flag (one
// history entry; undo removes them again). FkOptions.MissingBonesZero writes (0, 0, 0) instead (vanilla). A character
// that is not built gets the flag without a capture (fk.captureUnavailable, info). Every operation applies pending
// model changes first (runtime.ApplyNow) and again after. Used by the UI (3.5), the bridge (2.4) and PoseImport. The
// transactions are opened with CommandBus.BeginOrJoin, so inside an open transaction (a bridge batch or command,
// 4) they become steps of it.
// 3.4 (package B): the exclusive preset (IkOptions.Exclusive, K3) — switching FK on
// also writes chara.ik.enabled = false in the same transaction; CopyFromAnimation ("animation → FK") reads the clip +
// hand-pattern pose through CharaMotion.Probe (before IkPrepare: no hips offset, IK, look or FK).
using System;
using System.Collections.Generic;
using HS2Next.Motion;
using HS2Next.Motion.Fk;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class FkOps
    {
        readonly SceneRuntime rt;

        public FkOps(SceneRuntime runtime) { rt = runtime ?? throw new ArgumentNullException(nameof(runtime)); }

        SceneSession Session => rt.Session;

        CommandResult NodeCheck(string id, out CharaNodeState st)
        {
            st = null;
            var n = rt.Model.Find(id);
            if (n == null) return CommandResult.Error(CommandCodes.NotFound, "no node " + id);
            if (n.Kind != NodeKind.Chara || !(n.State is CharaNodeState cs)) return CommandResult.Error(CommandCodes.WrongKind, id + " is not a character");
            st = cs;
            return null;
        }

        static bool Flag(FkGroups g, FkGroup x)
        {
            switch (x)
            {
                case FkGroup.Hair: return g.Hair;
                case FkGroup.Neck: return g.Neck;
                case FkGroup.Breast: return g.Breast;
                case FkGroup.Body: return g.Body;
                case FkGroup.RightHand: return g.RightHand;
                case FkGroup.LeftHand: return g.LeftHand;
                default: return g.Skirt;
            }
        }

        /// <summary>`fk.enabled` (capturing the missing values of the groups that become applied).</summary>
        public CommandResult SetEnabled(string id, bool on, ExecuteOptions o = default)
        {
            rt.ApplyNow();
            var err = NodeCheck(id, out var st);
            if (err != null) return err;
            var groups = new List<FkGroup>();
            if (on && !st.Fk.Enabled)
                for (int g = 0; g < FkGroupInfo.Count; g++) if (Flag(st.Fk.Groups, (FkGroup)g)) groups.Add((FkGroup)g);
            var extra = on && IkOptions.Exclusive && st.Ik.Enabled ? ("chara.ik.enabled", (object)false) : ((string, object)?)null;
            return WithCapture(id, st, groups, "chara.fk.enabled", on, o.Label ?? (on ? "FK on" : "FK off"), o, extra);
        }

        /// <summary>`fk.groups.&lt;g&gt;` (capturing the missing values when the group becomes applied).</summary>
        public CommandResult SetGroup(string id, FkGroup g, bool on, ExecuteOptions o = default)
        {
            rt.ApplyNow();
            var err = NodeCheck(id, out var st);
            if (err != null) return err;
            var groups = new List<FkGroup>();
            if (on && st.Fk.Enabled && !Flag(st.Fk.Groups, g)) groups.Add(g);
            return WithCapture(id, st, groups, "chara.fk.groups." + FkGroupInfo.MemberNames[(int)g], on, o.Label ?? ("FK " + g + (on ? " on" : " off")), o);
        }

        CommandResult WithCapture(string id, CharaNodeState st, List<FkGroup> groups, string path, bool value, string label, ExecuteOptions o, (string path, object value)? extra = null)
        {
            var add = groups.Count > 0 ? MissingValueSets(id, groups, st.Fk.Bones) : null;
            CommandResult res;
            using (var tx = Session.Commands.BeginOrJoin(label, o))
            {
                if (!tx.IsOpen) return tx.Failure;
                res = tx.Set(id, path, value);
                if (res.Ok && extra.HasValue) res = tx.Set(id, extra.Value.path, extra.Value.value);
                if (res.Ok && add != null && add.Count > 0)
                {
                    var map = new SortedDictionary<string, Vec3>(st.Fk.Bones, StringComparer.Ordinal);
                    foreach (var kv in add) map[kv.Key] = kv.Value;
                    var r2 = tx.Set(id, "chara.fk.bones", map);
                    if (!r2.Ok) res = r2;
                }
                if (!res.Ok) { tx.Cancel(); rt.ApplyNow(); return res; }
                res = tx.Commit();
            }
            rt.ApplyNow();
            return res;
        }

        /// <summary>Sets several bone values in one transaction (one history entry).</summary>
        public CommandResult SetBones(string id, IReadOnlyDictionary<string, Vec3> values, ExecuteOptions o = default)
        {
            rt.ApplyNow();
            var err = NodeCheck(id, out _);
            if (err != null) return err;
            if (values == null || values.Count == 0) return CommandResult.NoOp(null);
            CommandResult res;
            using (var tx = Session.Commands.BeginOrJoin(o.Label ?? "FK bones", o))
            {
                if (!tx.IsOpen) return tx.Failure;
                res = null;
                foreach (var kv in values)
                {
                    res = tx.Set(id, "chara.fk.bones[" + FieldPath.Quote(kv.Key) + "]", kv.Value);
                    if (!res.Ok) break;
                }
                if (res == null || !res.Ok) { tx.Cancel(); rt.ApplyNow(); return res; }
                res = tx.Commit();
            }
            rt.ApplyNow();
            return res;
        }

        /// <summary>"Current pose → FK" for the bones of <paramref name="groups"/> (overwriting; the building block of 3.4's copies).</summary>
        public CommandResult CaptureGroups(string id, IEnumerable<FkGroup> groups, ExecuteOptions o = default)
        {
            rt.ApplyNow();
            var err = NodeCheck(id, out _);
            if (err != null) return err;
            var r = rt.FindChara(id);
            var table = FkDriver.TableFor(r);
            if (table == null)
            {
                rt.ReportOnce(FindingSeverity.Info, "fk.captureUnavailable", id, "character not built: nothing captured");
                return CommandResult.NoOp(null);
            }
            var values = new Dictionary<string, Vec3>(StringComparer.Ordinal);
            AdvancedPoseDriver.Bare(r, () =>
            {
                foreach (var g in groups)
                    foreach (var b in table.Of(g))
                        if (b.Bone) values[b.Key] = Capture(b);
            });
            return SetBones(id, values, new ExecuteOptions { Label = o.Label ?? "FK from pose", Source = o.Source, Record = o.Record, MergeKey = o.MergeKey });
        }

        /// <summary>The FK groups "animation → FK" may copy (HS2's panel: Body, Neck, RightHand, LeftHand).</summary>
        public static readonly FkGroup[] AnimationGroups = { FkGroup.Body, FkGroup.Neck, FkGroup.RightHand, FkGroup.LeftHand };

        /// <summary>
        /// "Animation → FK" values: the local Euler angles of every FK bone of
        /// <paramref name="groups"/> that has a transform, read inside CharaMotion.Probe(before IkPrepare) — the clip at the
        /// current time and the hand patterns, without the hips offset, IK, look and FK; additional nodes take their Rest
        /// (identity with FkOptions.ResetToIdentity). Null (and fk.captureUnavailable) when the character is not built or has
        /// no motion. Groups outside <see cref="AnimationGroups"/> are ignored.
        /// </summary>
        public Dictionary<string, Vec3> AnimationValues(string id, IEnumerable<FkGroup> groups)
        {
            rt.ApplyNow();
            var r = rt.FindChara(id);
            var table = FkDriver.TableFor(r);
            var motion = r != null && r.State == RealizationState.Built ? r.Motion : null;
            if (table == null || !motion || motion.Probing)
            {
                rt.ReportOnce(FindingSeverity.Info, "fk.captureUnavailable", id, "character not built: nothing copied");
                return null;
            }
            var list = new List<FkBone>();
            foreach (var g in groups ?? AnimationGroups)
                if (Array.IndexOf(AnimationGroups, g) >= 0) foreach (var b in table.Of(g)) if (b.Bone) list.Add(b);
            var values = new Dictionary<string, Vec3>(StringComparer.Ordinal);
            motion.Probe(MotionPhase.IkPrepare, r.Root.transform, () =>
            {
                foreach (var b in list)
                {
                    var e = b.Additional ? (FkOptions.ResetToIdentity ? Vector3.zero : b.Rest.eulerAngles) : b.Bone.localEulerAngles;
                    values[b.Key] = new Vec3(e.x, e.y, e.z);
                }
            });
            return values;
        }

        /// <summary>"Animation → FK" as one entry "FK from animation" (overwriting those keys); never switches a mode.</summary>
        public CommandResult CopyFromAnimation(string id, IEnumerable<FkGroup> groups = null, ExecuteOptions o = default)
        {
            rt.ApplyNow();
            var err = NodeCheck(id, out _);
            if (err != null) return err;
            var values = AnimationValues(id, groups);
            if (values == null) return CommandResult.Error(CommandCodes.Unsupported, "unavailable: character not built");
            return SetBones(id, values, new ExecuteOptions { Label = o.Label ?? "FK from animation", Source = o.Source, Record = o.Record, MergeKey = o.MergeKey });
        }

        /// <summary>
        /// The current local Euler angles of every bone of <paramref name="groups"/> that has a transform, under its primary
        /// key, whatever FkOptions.MissingBonesZero says and without the Skirt usage filter: the "animation → FK" copy of the
        /// bridge's fk.capture (vanilla mask 353). Reads after ApplyNow; empty
        /// (and fk.captureUnavailable) when the character is not built.
        /// </summary>
        public Dictionary<string, Vec3> CurrentValues(string id, IEnumerable<FkGroup> groups)
        {
            rt.ApplyNow();
            var values = new Dictionary<string, Vec3>(StringComparer.Ordinal);
            var table = FkDriver.TableFor(rt.FindChara(id));
            if (table == null)
            {
                rt.ReportOnce(FindingSeverity.Info, "fk.captureUnavailable", id, "character not built: nothing captured");
                return values;
            }
            AdvancedPoseDriver.Bare(rt.FindChara(id), () =>
            {
                foreach (var g in groups)
                    foreach (var b in table.Of(g))
                        if (b.Bone)
                        {
                            var e = b.Bone.localEulerAngles;
                            values[b.Key] = new Vec3(e.x, e.y, e.z);
                        }
            });
            return values;
        }

        /// <summary>
        /// The values to add for the bones of <paramref name="groups"/> that have no value in <paramref name="bones"/>
        /// (primary or partner key): the current local Euler angles of the built character, or (0, 0, 0) with
        /// FkOptions.MissingBonesZero. Skirt bones count only when Used. Empty (and fk.captureUnavailable) when the
        /// character is not built.
        /// </summary>
        public Dictionary<string, Vec3> MissingValueSets(string id, IEnumerable<FkGroup> groups, IDictionary<string, Vec3> bones)
        {
            var add = new Dictionary<string, Vec3>(StringComparer.Ordinal);
            var r = rt.FindChara(id);
            var table = FkDriver.TableFor(r);
            if (table == null)
            {
                rt.ReportOnce(FindingSeverity.Info, "fk.captureUnavailable", id, "character not built: FK values of the newly applied groups were not captured");
                return add;
            }
            AdvancedPoseDriver.Bare(r, () =>
            {
                foreach (var g in groups)
                    foreach (var b in table.Of(g))
                    {
                        if (!b.Bone || !b.Used) continue;
                        if (bones != null && (bones.ContainsKey(b.Key) || (b.PartnerKey != null && bones.ContainsKey(b.PartnerKey)))) continue;
                        add[b.Key] = Capture(b);
                    }
            });
            return add;
        }

        static Vec3 Capture(FkBone b)
        {
            if (FkOptions.MissingBonesZero) return new Vec3(0f, 0f, 0f);
            var e = b.Bone.localEulerAngles;
            return new Vec3(e.x, e.y, e.z);
        }
    }
}
