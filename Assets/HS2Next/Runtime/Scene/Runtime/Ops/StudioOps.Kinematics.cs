// StudioOps, FK / IK / pose area: thin wrappers of FkOps, IkOps,
// KinematicCopies, PoseImport and PoseFragmentOps with the labels (the operation classes already
// open BeginOrJoin transactions; the bridge handlers of Phase 3.4 call them directly and stay as they are).
// Value of a copy / bake = its KinematicResult; of a pose load = the PoseImportReport / PoseFragmentLoadResult.
using System.Collections.Generic;
using HS2Next.Motion.Fk;
using HS2Next.Save;
using HS2Next.Motion.Ik;

namespace HS2Next.Scenes.Runtime
{
    public sealed partial class StudioOps
    {
        public OpResult SetFkEnabled(string id, bool on, ExecuteOptions o = default) => OpResult.From(Fk.SetEnabled(id, on, Labelled(o, Labels.Fk)));
        public OpResult SetFkGroup(string id, FkGroup g, bool on, ExecuteOptions o = default) => OpResult.From(Fk.SetGroup(id, g, on, Labelled(o, Labels.Fk)));
        public OpResult SetFkBones(string id, IReadOnlyDictionary<string, Vec3> values, ExecuteOptions o = default) => OpResult.From(Fk.SetBones(id, values, Labelled(o, Labels.FkBones)), "bones");

        public OpResult SetIkEnabled(string id, bool on, ExecuteOptions o = default) => OpResult.From(Ik.SetEnabled(id, on, Labelled(o, Labels.Ik)));
        public OpResult SetIkGroups(string id, IEnumerable<IkGroup> groups, bool on, ExecuteOptions o = default) => OpResult.From(Ik.SetGroups(id, groups, on, Labelled(o, Labels.Ik)));
        public OpResult SetIkTargets(string id, IReadOnlyDictionary<IkTargetId, IkTargetEdit> edits, IkSpace space = IkSpace.Local, ExecuteOptions o = default) =>
            OpResult.From(Ik.SetTargets(id, edits, space, Labelled(o, Labels.IkTargets)), "targets");

        static OpResult From(KinematicResult k)
        {
            var r = OpResult.From(k?.Command);
            r.Value = k;
            if (k?.Copied != null) r.Changed.AddRange(k.Copied);
            return r;
        }

        public OpResult FkFromAnimation(string id, IEnumerable<FkGroup> groups = null, bool enable = false, ExecuteOptions o = default) =>
            From(Copies.FkFromAnimation(id, groups, enable, Labelled(o, Labels.FkFromAnimation)));
        public OpResult FkFromPose(string id, IEnumerable<FkGroup> groups = null, bool enable = false, ExecuteOptions o = default) =>
            From(Copies.FkFromCurrent(id, groups, enable, Labelled(o, Labels.FkFromPose)));
        public OpResult IkFromAnimation(string id, IEnumerable<IkGroup> groups = null, bool enable = false, ExecuteOptions o = default) =>
            From(Copies.IkFromAnimation(id, groups, enable, Labelled(o, Labels.IkFromAnimation)));
        public OpResult IkFromPose(string id, IEnumerable<IkGroup> groups = null, bool enable = false, ExecuteOptions o = default) =>
            From(Copies.IkFromCurrent(id, groups, enable, Labelled(o, Labels.IkFromPose)));
        public OpResult ConvertImportedIk(string id, ExecuteOptions o = default) => From(Copies.IkFromImport(id, Labelled(o, Labels.ConvertIk)));
        public OpResult BakeIk(string id, ExecuteOptions o = default) => From(Copies.Bake(id, Labelled(o, Labels.BakeIk)));

        /// <summary>An HS2 pose file (.dat / .png) on a character ("Load pose"); Value = the PoseImportReport.</summary>
        public OpResult LoadPose(string id, string fullPath, PoseImportOptions options = null, ExecuteOptions o = default)
        {
            var rep = PoseImport.ApplyFile(rt, id, fullPath, options, Labelled(o, Labels.LoadPose));
            var r = OpResult.Success();
            r.Value = rep;
            return r;
        }

        /// <summary>An HS2Next pose fragment (.hs2npose) on a character ("Load pose"); Value = the PoseFragmentLoadResult.</summary>
        public OpResult LoadPoseFragment(string id, string fullPath, IReadOnlyList<string> parts = null, ExecuteOptions o = default)
        {
            var res = PoseFragmentOps.Load(rt, id, fullPath, parts, Labelled(o, Labels.LoadPose));
            var r = OpResult.Success();
            r.Value = res;
            return r;
        }
    }
}
