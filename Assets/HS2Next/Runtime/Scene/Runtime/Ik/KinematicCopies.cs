// The copies between animation, FK and IK and "bake IK to FK" (Phase
// 3.4 package B). One history entry each; a copy never switches a mode or a group (HS2) unless `enable` asks for it, which
// then also applies the exclusive preset (IkOptions.Exclusive). A character that is not built fails ("unsupported",
// message "unavailable…", with ik.copyUnavailable / fk.captureUnavailable) and nothing is written.
//   FkFromAnimation  probe before IkPrepare (clip + hand patterns): Body / Neck / RightHand / LeftHand only   "FK from animation"
//   FkFromCurrent    the current final local rotations (HS2 "IK → FK"; FkOps.CaptureGroups), default 353      "FK from pose"
//   IkFromAnimation  probe before IkPrepare: the source bones of the groups' targets                         "IK from animation"
//   IkFromCurrent    the current final pose (HS2 "FK → IK")                                                   "IK from pose"
//   IkFromImport     the node's hs2.pose IK block (2.3 imports) converted as the import does (PoseImport.PlanIk) "Convert IK"
//   Bake             the solved pose → Body-group FK values + fk.hipsPosition, FK + Body on, IK off           "Bake IK"
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Motion.Fk;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>The outcome of a copy or bake.</summary>
    public sealed class KinematicResult
    {
        public CommandResult Command;
        public bool Ok => Command != null && Command.Ok;
        /// <summary>IK copies: the target names written; FK copies: the bone keys written.</summary>
        public IReadOnlyList<string> Copied = new string[0];
        /// <summary>Bake: the number of Body-group FK bones written.</summary>
        public int Bones;
        /// <summary>Bake: the hips position written.</summary>
        public Vector3? HipsPosition;
        /// <summary>IkFromImport: the conversion (scale, body shift, unreachable).</summary>
        public PoseIkResult Ik;
        public override string ToString() => Command?.ToString() ?? "null";
    }

    public sealed class KinematicCopies
    {
        readonly SceneRuntime rt;

        public KinematicCopies(SceneRuntime runtime) { rt = runtime ?? throw new ArgumentNullException(nameof(runtime)); }

        /// <summary>All five IK groups (the default of the IK copies).</summary>
        public static readonly IkGroup[] AllIkGroups = { IkGroup.Body, IkGroup.RightLeg, IkGroup.LeftLeg, IkGroup.RightArm, IkGroup.LeftArm };

        SceneSession Session => rt.Session;

        static KinematicResult Fail(CommandResult r) => new KinematicResult { Command = r };

        KinematicResult Unavailable(string id, string code)
        {
            rt.ReportOnce(FindingSeverity.Info, code, id, "character not built: nothing copied");
            return Fail(CommandResult.Error(CommandCodes.Unsupported, "unavailable: character not built"));
        }

        static bool FkFlag(FkGroups g, FkGroup x)
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

        // ------------------------------------------------------------ FK copies

        /// <summary>"Animation → FK" (default and allowed groups: Body, Neck, RightHand, LeftHand; others are bad_args).</summary>
        public KinematicResult FkFromAnimation(string id, IEnumerable<FkGroup> groups = null, bool enable = false, ExecuteOptions o = default)
        {
            rt.ApplyNow();
            var err = IkOps.NodeCheck(rt, id, out var st);
            if (err != null) return Fail(err);
            var list = (groups ?? FkOps.AnimationGroups).Distinct().ToList();
            var bad = list.Where(g => Array.IndexOf(FkOps.AnimationGroups, g) < 0).ToList();
            if (bad.Count > 0) return Fail(CommandResult.Error(CommandCodes.BadArgs, "animation → FK copies Body, Neck, RightHand and LeftHand only (not " + string.Join(", ", bad) + ")"));
            var values = new FkOps(rt).AnimationValues(id, list);
            if (values == null) return Fail(CommandResult.Error(CommandCodes.Unsupported, "unavailable: character not built"));
            return FkWrite(id, st, values, list, enable, o.Label ?? "FK from animation", o);
        }

        /// <summary>"Current pose → FK" (HS2 "IK → FK"; default Body, Neck, RightHand, LeftHand).</summary>
        public KinematicResult FkFromCurrent(string id, IEnumerable<FkGroup> groups = null, bool enable = false, ExecuteOptions o = default)
        {
            rt.ApplyNow();
            var err = IkOps.NodeCheck(rt, id, out var st);
            if (err != null) return Fail(err);
            var list = (groups ?? FkOps.AnimationGroups).Distinct().ToList();
            var table = FkDriver.TableFor(rt.FindChara(id));
            if (table == null) return Unavailable(id, "fk.captureUnavailable");
            var values = new Dictionary<string, Vec3>(StringComparer.Ordinal);
            AdvancedPoseDriver.Bare(rt.FindChara(id), () =>
            {
                foreach (var g in list)
                    foreach (var b in table.Of(g))
                        if (b.Bone) { var e = b.Bone.localEulerAngles; values[b.Key] = new Vec3(e.x, e.y, e.z); }
            });
            return FkWrite(id, st, values, list, enable, o.Label ?? "FK from pose", o);
        }

        KinematicResult FkWrite(string id, CharaNodeState st, Dictionary<string, Vec3> values, List<FkGroup> groups, bool enable, string label, ExecuteOptions o)
        {
            CommandResult res = null;
            using (var tx = Session.Commands.BeginOrJoin(label, o))
            {
                if (!tx.IsOpen) return Fail(tx.Failure);
                if (values.Count > 0)
                {
                    var map = new SortedDictionary<string, Vec3>(st.Fk.Bones, StringComparer.Ordinal);
                    foreach (var kv in values) map[kv.Key] = kv.Value;
                    res = tx.Set(id, "chara.fk.bones", map);
                }
                if (enable && (res == null || res.Ok))
                {
                    res = tx.Set(id, "chara.fk.enabled", true);
                    foreach (var g in groups)
                        if (res.Ok && !FkFlag(st.Fk.Groups, g)) res = tx.Set(id, "chara.fk.groups." + FkGroupInfo.MemberNames[(int)g], true);
                    if (res.Ok && IkOptions.Exclusive && st.Ik.Enabled) res = tx.Set(id, "chara.ik.enabled", false);
                }
                if (res == null) { tx.Cancel(); return new KinematicResult { Command = CommandResult.NoOp(null) }; }
                if (!res.Ok) { tx.Cancel(); rt.ApplyNow(); return Fail(res); }
                res = tx.Commit();
            }
            rt.ApplyNow();
            return new KinematicResult { Command = res, Copied = values.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList() };
        }

        // ------------------------------------------------------------ IK copies

        /// <summary>"Animation → IK" (default all five groups).</summary>
        public KinematicResult IkFromAnimation(string id, IEnumerable<IkGroup> groups = null, bool enable = false, ExecuteOptions o = default) =>
            IkCopy(id, groups, enable, true, o.Label ?? "IK from animation", o);

        /// <summary>"Current pose → IK" (HS2 "FK → IK"; default all five groups).</summary>
        public KinematicResult IkFromCurrent(string id, IEnumerable<IkGroup> groups = null, bool enable = false, ExecuteOptions o = default) =>
            IkCopy(id, groups, enable, false, o.Label ?? "IK from pose", o);

        KinematicResult IkCopy(string id, IEnumerable<IkGroup> groups, bool enable, bool animation, string label, ExecuteOptions o)
        {
            rt.ApplyNow();
            var err = IkOps.NodeCheck(rt, id, out var st);
            if (err != null) return Fail(err);
            var c = IkDriver.Of(rt.FindChara(id));
            if (c == null) return Unavailable(id, "ik.copyUnavailable");
            var v = animation ? c.AnimationPose() : c.CurrentPose();
            if (v == null) return Unavailable(id, "ik.copyUnavailable");
            var list = (groups ?? AllIkGroups).Distinct().ToList();
            var targets = list.SelectMany(g => IkGroupInfo.TargetsOf(g)).Select(t => (int)t).Distinct().OrderBy(i => i).ToList();
            var sets = new List<(string, object)>();
            var copied = new List<string>();
            foreach (int i in targets)
            {
                if (!v[i].Has) continue;
                sets.Add((IkOps.TargetPath(i), IkOps.ToModel(i, v[i])));
                copied.Add(IkTargets.Names[i]);
            }
            if (enable)
            {
                if (!st.Ik.Enabled) sets.Add(("chara.ik.enabled", true));
                foreach (var g in list) if (!IkCharacter.Flag(st.Ik.Groups, g)) sets.Add((IkOps.GroupPath(g), true));
                if (IkOptions.Exclusive && st.Fk.Enabled) sets.Add(("chara.fk.enabled", false));
            }
            var res = Write(id, sets, label, o);
            return new KinematicResult { Command = res, Copied = res.Ok ? copied : (IReadOnlyList<string>)new string[0] };
        }

        CommandResult Write(string id, List<(string path, object value)> sets, string label, ExecuteOptions o, ISceneCommand extra = null)
        {
            if (sets.Count == 0 && extra == null) return CommandResult.NoOp(null);
            CommandResult res = null;
            using (var tx = Session.Commands.BeginOrJoin(label, o))
            {
                if (!tx.IsOpen) return tx.Failure;
                foreach (var (path, value) in sets)
                {
                    res = tx.Set(id, path, value);
                    if (!res.Ok) break;
                }
                if (extra != null && (res == null || res.Ok)) res = tx.Execute(extra);
                if (!res.Ok) { tx.Cancel(); rt.ApplyNow(); return res; }
                res = tx.Commit();
            }
            rt.ApplyNow();
            return res;
        }

        /// <summary>
        /// "Convert IK" (K19): the node's hs2.pose IK block (raw targets of a 2.3 import) converted as the 3.4 import does
        /// (scale, Body conversion on the current pose): targets, ik.enabled and the groups from the block, and the block's
        /// scale / bodyShift, in one entry. bad_args when the node has no IK block.
        /// </summary>
        public KinematicResult IkFromImport(string id, ExecuteOptions o = default)
        {
            rt.ApplyNow();
            var err = IkOps.NodeCheck(rt, id, out var st);
            if (err != null) return Fail(err);
            var node = rt.Model.Find(id);
            var prov = PoseProvenanceExt.Of(node);
            if (prov?.Ik == null) return Fail(CommandResult.Error(CommandCodes.BadArgs, id + " has no imported IK block (hs2.pose.ik)"));
            var r = rt.FindChara(id);
            var body = PoseIkBody.Of(r);
            if (body == null) return Unavailable(id, "ik.copyUnavailable");
            var ik = PoseImport.PlanIk(prov.Ik.Targets, prov.Ik.Groups, body);
            var sets = new List<(string, object)>();
            var t = ik.Targets ?? new IkTarget[IkTargets.Count];
            for (int i = 0; i < IkTargets.Count; i++) sets.Add((IkOps.TargetPath(i), t[i]));
            sets.Add(("chara.ik.enabled", prov.Ik.Enabled));
            for (int g = 0; g < IkGroupInfo.Count; g++) sets.Add((IkOps.GroupPath((IkGroup)g), prov.Ik.Groups != null && g < prov.Ik.Groups.Length && prov.Ik.Groups[g]));
            PoseImport.SetIkConversion(prov.Ik, ik);
            var label = o.Label ?? "Convert IK";
            var res = Write(id, sets, label, o, new SetNodeExtCommand(id, PoseImport.ProvenanceNamespace, prov.ToJson(), label));
            var rep = new PoseImportReport();
            PoseImport.ReportIk(rep, ik);
            foreach (var f in rep.Findings) rt.Report(f.Severity, f.Code, id, f.Message);
            return new KinematicResult { Command = res, Ik = ik, Copied = ik.Targets == null ? new string[0] : IkTargets.Names.Where((n, i) => t[i] != null).ToList() };
        }

        // ------------------------------------------------------------ bake

        /// <summary>
        /// "Bake IK": fk.bones := the current final local Euler of every Body-group FK bone with a transform (merged into the
        /// map), fk.hipsPosition := cf_J_Hips' local position, fk.hipsOffset := 0, fk.enabled and fk.groups.body on,
        /// ik.enabled off (groups and targets kept); one entry.
        /// </summary>
        public KinematicResult Bake(string id, ExecuteOptions o = default)
        {
            rt.ApplyNow();
            var err = IkOps.NodeCheck(rt, id, out var st);
            if (err != null) return Fail(err);
            var r = rt.FindChara(id);
            var table = FkDriver.TableFor(r);
            Transform hips = null;
            if (r?.Context?.BodyBones != null) r.Context.BodyBones.TryGetValue("cf_J_Hips", out hips);
            if (table == null || !hips) return Unavailable(id, "ik.copyUnavailable");
            var map = new SortedDictionary<string, Vec3>(st.Fk.Bones, StringComparer.Ordinal);
            int n = 0;
            var hp = hips.localPosition;
            AdvancedPoseDriver.Bare(r, () =>
            {
                foreach (var b in table.Of(FkGroup.Body))
                {
                    if (!b.Bone) continue;
                    var e = b.Bone.localEulerAngles;
                    map[b.Key] = new Vec3(e.x, e.y, e.z);
                    n++;
                }
                hp = hips.localPosition;
            });
            var sets = new List<(string, object)>
            {
                ("chara.ik.enabled", false),
                ("chara.fk.bones", map),
                ("chara.fk.hipsPosition", new Vec3(hp.x, hp.y, hp.z)),
                ("chara.fk.hipsOffset", 0f),
                ("chara.fk.enabled", true),
                ("chara.fk.groups.body", true),
            };
            var res = Write(id, sets, o.Label ?? "Bake IK", o);
            return new KinematicResult { Command = res, Bones = res.Ok ? n : 0, HipsPosition = res.Ok ? hp : (Vector3?)null };
        }
    }
}
