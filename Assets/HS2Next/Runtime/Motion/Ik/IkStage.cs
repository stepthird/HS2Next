// IK stage: one per character at MotionPhase.Ik (200), created by the scene layer's
// IkDriver at build. Step (not suspended, at least one group applied):
//   1. latch   for each bone of the previous step's owned set whose local rotation (pelvis: local position) still equals
//              exactly what this stage wrote, restore the input it read last step (bones no earlier stage rewrites stay
//              deterministic; replaces FBBIK's fixTransforms without a stage before the animation, K17)
//   2. FK base FkStage.WriteApplied() when an FK group is applied and FK is not suspended (IK over an FK base, K2)
//   3. solve   IkSolver on the effective targets (missing targets of applied groups are snapshot from this input pose), with the
//              channels of TargetOverrides laid over them; Targets itself is never written by an override
//   4. mask    FkStage.Owned := the FK bones of the owned set, so FK at 400 skips them
// When the stage stops (no group applied, disabled, suspended) the inputs are written back once to the bones that still
// hold the stage's values and FkStage.Owned is cleared (K24). The FkStage is found lazily through CharaMotion each step
// (the FK driver installs it after the IK driver).
using System.Collections.Generic;
using HS2Next.Motion.Fk;
using UnityEngine;

namespace HS2Next.Motion.Ik
{
    /// <summary>A value laid over one target for the solve (a runtime override), by channel. The rotation is Euler degrees, as a target's.</summary>
    public struct IkTargetOverride
    {
        public bool HasPos, HasRot;
        public Vector3 Pos, Rot;
    }

    public sealed class IkStage : IMotionStage
    {
        public MotionPhase Phase => MotionPhase.Ik;
        public string Name => "ik";

        /// <summary>The driver enables the stage iff at least one group is applied; disabling releases (K24).</summary>
        public bool Enabled
        {
            get => enabled;
            set { if (enabled && !value) Release(); enabled = value; }
        }

        /// <summary>The character root (targets are in its local space, scale included).</summary>
        public readonly Transform Root;
        public readonly IkBones Bones;
        /// <summary>The character's motion (FkStage lookup); may be null (tests: no FK base / mask).</summary>
        public readonly CharaMotion Motion;

        /// <summary>Effective targets in root space, indexed by IkTargetId (set by the driver from the model; Has = false
        /// → the stage snapshots the target from the step's input pose the first time its group runs).</summary>
        public readonly IkTargetValue[] Targets = new IkTargetValue[IkGroupInfo.TargetCount];
        /// <summary>Values laid over <see cref="Targets"/> for the solve, indexed by IkTargetId; set through <see cref="SetOverridePos"/>, <see cref="SetOverrideRot"/>.</summary>
        public readonly IkTargetOverride[] TargetOverrides = new IkTargetOverride[IkGroupInfo.TargetCount];
        /// <summary>Applied(g) = ik.enabled AND ik.groups.g, indexed by IkGroup (set by the driver; call Refresh after a change).</summary>
        public readonly bool[] Applied = new bool[IkGroupInfo.Count];
        /// <summary>No writes while suspended (StudioDefaultPoseScope).</summary>
        public bool Suspended { get; private set; }
        /// <summary>Solve information of the last step that ran a solve.</summary>
        public readonly IkSolveInfo LastSolve = new IkSolveInfo();
        /// <summary>The bones the last solve wrote (the owned set S); empty when the stage is stopped.</summary>
        public readonly HashSet<Transform> Owned = new HashSet<Transform>();
        /// <summary>The 13 source-bone values of the last step's input pose (after latch and FK base, before the solve;
        /// the capture source while IK runs). Valid when <see cref="HasLastInput"/>.</summary>
        public readonly IkTargetValue[] LastInput = new IkTargetValue[IkGroupInfo.TargetCount];
        public bool HasLastInput { get; private set; }
        /// <summary>The scene layer's per-character state (IkCharacter), for lookups through CharaMotion.Get&lt;IkStage&gt;().</summary>
        public object Owner;

        bool enabled, anyOverride;
        IkTargetValue[] overlaid;

        public IkStage(Transform root, IkBones bones, CharaMotion motion)
        {
            Root = root;
            Bones = bones;
            Motion = motion;
            LastSolve.Clear();
        }

        /// <summary>Applied(g) AND the group's bones are present.</summary>
        public bool IsApplied(IkGroup g) => Applied[(int)g] && Bones != null && Bones.GroupAvailable[(int)g];

        /// <summary>At least one group is applied (and available).</summary>
        public bool AnyApplied
        {
            get { for (int g = 0; g < IkGroupInfo.Count; g++) if (IsApplied((IkGroup)g)) return true; return false; }
        }

        /// <summary>Sets a target's model value (drops its snapshot).</summary>
        public void SetTarget(IkTargetId t, Vector3 pos, Vector3 rot) => Targets[(int)t] = new IkTargetValue(pos, rot);

        /// <summary>Marks a target missing (a snapshot is taken the next time its group runs).</summary>
        public void ClearTarget(IkTargetId t) => Targets[(int)t] = default;

        /// <summary>Lays a position over a target (root space) for the solves to come, or takes it off.</summary>
        public void SetOverridePos(IkTargetId t, bool on, Vector3 pos)
        {
            TargetOverrides[(int)t].HasPos = on;
            TargetOverrides[(int)t].Pos = pos;
            NoteOverrides();
        }

        /// <summary>Lays a rotation (root space) over a hand's or a foot's target for the solves to come, or takes it off.</summary>
        public void SetOverrideRot(IkTargetId t, bool on, Quaternion rot)
        {
            TargetOverrides[(int)t].HasRot = on;
            TargetOverrides[(int)t].Rot = on ? rot.eulerAngles : Vector3.zero;
            NoteOverrides();
        }

        void NoteOverrides()
        {
            anyOverride = false;
            for (int i = 0; i < TargetOverrides.Length; i++) anyOverride |= TargetOverrides[i].HasPos || TargetOverrides[i].HasRot;
        }

        /// <summary>
        /// A later writer took its value off the bone in <paramref name="slot"/> and gave back <paramref name="wrote"/>. Unity's setter may store other last bits than it
        /// was given: when <paramref name="wrote"/> is what this stage left, the latch takes the bits the bone holds now, so it still finds its own write.
        /// </summary>
        public void Relatch(int slot, Quaternion wrote)
        {
            if (slot < 0 || !ownedPrev[slot] || !Same(wrote, outRot[slot])) return;
            var t = Bones.Slots[slot];
            if (t) outRot[slot] = t.localRotation;
        }

        /// <summary>After a change of Applied: Enabled := any applied; releases when nothing is applied.</summary>
        public void Refresh()
        {
            bool any = AnyApplied;
            Enabled = any;
            if (!any) Release();
        }

        /// <summary>Suspend: inputs written back once, FkStage.Owned cleared; resume: the next step solves again.</summary>
        public void SetSuspended(bool suspended)
        {
            Suspended = suspended;
            if (suspended) Release();
        }

        // latch (slots of IkBones): inputs read and values written last step
        readonly Quaternion[] inRot = new Quaternion[IkBones.SlotCount], outRot = new Quaternion[IkBones.SlotCount];
        readonly bool[] ownedPrev = new bool[IkBones.SlotCount], ownedNow = new bool[IkBones.SlotCount], restored = new bool[IkBones.SlotCount];
        readonly bool[] run = new bool[IkGroupInfo.Count];
        Vector3 inPos, outPos;
        bool posHeld;
        // FK hooks
        FkStage fk;
        FkBoneTable fkTable;
        readonly FkBone[] fkOfSlot = new FkBone[IkBones.SlotCount];
        readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

        public void Step(in MotionFrame frame)
        {
            if (Suspended || Bones == null || !AnyApplied) { Release(); return; }
            sw.Restart();
            var slots = Bones.Slots;
            var hips = Bones.Hips;

            // 1. latch. A restored bone keeps its latched input (Unity's rotation setter normalises, and setting a value read
            // back from a transform does not always store it bit for bit, so re-reading it would let the input drift).
            for (int s = 0; s < IkBones.SlotCount; s++)
            {
                restored[s] = ownedPrev[s] && slots[s] && Same(slots[s].localRotation, outRot[s]);
                if (restored[s]) slots[s].localRotation = inRot[s];
                else if (slots[s]) inRot[s] = slots[s].localRotation;
            }
            if (hips)
            {
                if (posHeld && Same(hips.localPosition, outPos)) hips.localPosition = inPos;
                else inPos = hips.localPosition;
            }

            // 2. FK base
            var f = Motion ? Motion.Get<FkStage>() : null;
            if (f != fk) { if (fk != null) fk.Owned.Clear(); fk = f; fkTable = null; }
            if (fk != null && fk.Enabled && !fk.Suspended) fk.WriteApplied();

            // input pose: capture source and snapshots of missing targets of the groups that run
            Bones.ReadTargets(LastInput);
            HasLastInput = true;
            for (int g = 0; g < IkGroupInfo.Count; g++)
            {
                run[g] = IsApplied((IkGroup)g);
                if (!run[g]) continue;
                foreach (var t in IkGroupInfo.TargetsOf((IkGroup)g))
                {
                    int i = (int)t;
                    if (!Targets[i].Has && LastInput[i].Has) { Targets[i] = LastInput[i]; Targets[i].Snapshot = true; }
                    if (!Targets[i].Has) run[g] = false;
                }
            }

            // 3. solve
            System.Array.Clear(ownedNow, 0, ownedNow.Length);
            double t0 = sw.Elapsed.TotalMilliseconds;
            IkSolver.Solve(Bones, Root, anyOverride ? Overlaid() : Targets, run, LastSolve, ownedNow);
            LastSolve.Milliseconds = sw.Elapsed.TotalMilliseconds - t0;
            LastSolve.Step = frame.Index;
            Owned.Clear();
            for (int s = 0; s < IkBones.SlotCount; s++)
            {
                ownedPrev[s] = ownedNow[s];
                if (!ownedNow[s]) continue;
                outRot[s] = slots[s].localRotation;
                Owned.Add(slots[s]);
            }
            posHeld = ownedNow[IkBones.SlotHips];
            if (posHeld) outPos = hips.localPosition;

            // 4. owned mask
            if (fk != null)
            {
                if (fkTable != fk.Table) MapFk();
                fk.Owned.Clear();
                for (int s = 0; s < IkBones.SlotCount; s++) if (ownedNow[s] && fkOfSlot[s] != null) fk.Owned.Add(fkOfSlot[s]);
            }
            LastSolve.StageMilliseconds = sw.Elapsed.TotalMilliseconds;
        }

        // the array the solve reads while an override is set: the override's channels over the targets
        IkTargetValue[] Overlaid()
        {
            overlaid = overlaid ?? new IkTargetValue[IkGroupInfo.TargetCount];
            for (int i = 0; i < overlaid.Length; i++)
            {
                var v = Targets[i];
                ref var o = ref TargetOverrides[i];
                if (o.HasPos) v.Pos = o.Pos;
                if (o.HasRot) v.Rot = o.Rot;
                overlaid[i] = v;
            }
            return overlaid;
        }

        void MapFk()
        {
            fkTable = fk.Table;
            System.Array.Clear(fkOfSlot, 0, fkOfSlot.Length);
            if (fkTable == null || Bones == null) return;
            foreach (var b in fkTable.Bones)
            {
                if (b == null || !b.Bone) continue;
                for (int s = 0; s < IkBones.SlotCount; s++)
                    if (Bones.Slots[s] == b.Bone && fkOfSlot[s] == null) fkOfSlot[s] = b;
            }
        }

        /// <summary>Writes the latched inputs back to the bones that still hold the stage's values and clears the owned
        /// set and FkStage.Owned (no-op when nothing is held).</summary>
        public void Release()
        {
            if (Bones != null)
            {
                var slots = Bones.Slots;
                for (int s = 0; s < IkBones.SlotCount; s++)
                {
                    if (ownedPrev[s] && slots[s] && Same(slots[s].localRotation, outRot[s])) slots[s].localRotation = inRot[s];
                    ownedPrev[s] = false;
                }
                var hips = Bones.Hips;
                if (posHeld && hips && Same(hips.localPosition, outPos)) hips.localPosition = inPos;
            }
            posHeld = false;
            Owned.Clear();
            LastSolve.Valid = false;
            if (fk != null) fk.Owned.Clear();
        }

        /// <summary>Drops the latch without writing (the next step starts from the current transforms).</summary>
        public void Reset()
        {
            System.Array.Clear(ownedPrev, 0, ownedPrev.Length);
            posHeld = false;
        }

        static bool Same(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
    }
}
