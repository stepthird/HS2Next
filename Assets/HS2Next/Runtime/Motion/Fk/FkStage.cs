// FK stages.
//   FkStage      MotionPhase.Fk (400): for every bone of an applied group with a value (Skirt bones only when Used)
//                localRotation = Euler(value), mirrored to the sync partner. Rotations only; deterministic, no history.
//                FkOptions.MissingBonesZero: a bone without a value is written as identity.
//                A bone with a runtime override (FkBone.HasOverride, the scene's override layer) is written with the override
//                instead of its value, wherever the stage writes it; without one the stage writes exactly what it wrote before.
//                Owned (Phase 3.4): the bones the IK solve owns this step; the per-step
//                write and WriteIfApplied skip them (IK wins; WriteApplied, the IK stage's base pass, writes them all).
//                Empty unless the IK stage sets it; the IK stage clears it when it stops.
//   FkNeckGate   MotionPhase.IkPrepare (150): while the Neck group is applied, holds the neck look on FIX (pattern 4)
//                before the neck look runs, so no step ever runs the neck look in another pattern.
//   FkHipsStage  MotionPhase.IkPrepare (150), registered before the gate: HS2_FKHeightAdjustUI's hips offset,
//                cf_J_Hips.localPosition = base + (0, offset, 0) in the cf_N_height frame while active. base = this
//                step's value from the earlier stages, the card's bone modifiers included (they run at phase 120 and rewrite a
//                modified hips position every step, so the two sum without feeding each other); when the bone still holds exactly
//                the value written last step, the previous base is kept (no accumulation when nothing rewrites the bone). When the stage stops,
//                the base is written back once if the bone still holds the stage's value.
//                Position (Phase 3.4, `fk.hipsPosition`): when set, the written value is
//                Position (+ (0, offset, 0)) instead of base + (0, offset, 0); base stays the latched input (written back
//                on release). The driver makes the stage active when the position or the offset applies.
// All three have Suspended (StudioDefaultPoseScope through FkDriver.Suspend).
using System;
using System.Collections.Generic;
using HS2Next.Motion.Look;
using UnityEngine;

namespace HS2Next.Motion.Fk
{
    public sealed class FkStage : IMotionStage
    {
        public MotionPhase Phase => MotionPhase.Fk;
        public string Name => "fk";
        /// <summary>The driver enables the stage iff at least one group is applied.</summary>
        public bool Enabled { get; set; }
        public readonly FkBoneTable Table;
        /// <summary>Applied(g) = fk.enabled AND fk.groups.g (set by the driver).</summary>
        public readonly bool[] Applied = new bool[FkGroupInfo.Count];
        /// <summary>No writes while suspended.</summary>
        public bool Suspended;
        /// <summary>The scene layer's per-character state (FkCharacter), for lookups through CharaMotion.Get&lt;FkStage&gt;().</summary>
        public object Owner;
        /// <summary>Bones the IK solve owns (set by the IK stage each step, cleared when it stops): the per-step write and
        /// <see cref="WriteIfApplied"/> skip them. Empty = 2.3 behaviour.</summary>
        public readonly HashSet<FkBone> Owned = new HashSet<FkBone>();

        public FkStage(FkBoneTable table) { Table = table; }

        public bool IsApplied(FkGroup g) => Applied[(int)g];

        public void Step(in MotionFrame frame)
        {
            if (Suspended) return;
            if (Owned.Count == 0) { WriteApplied(); return; }
            for (int g = 0; g < FkGroupInfo.Count; g++)
            {
                if (!Applied[g]) continue;
                var list = Table.Of((FkGroup)g);
                for (int i = 0; i < list.Count; i++) if (!Owned.Contains(list[i])) WriteBone(list[i]);
            }
        }

        /// <summary>Writes every applied bone once (also used outside a step so the FK pose shows at once).</summary>
        public void WriteApplied()
        {
            for (int g = 0; g < FkGroupInfo.Count; g++)
            {
                if (!Applied[g]) continue;
                var list = Table.Of((FkGroup)g);
                for (int i = 0; i < list.Count; i++) WriteBone(list[i]);
            }
        }

        /// <summary>Writes one bone when its group is applied (and it is Used) and IK does not own it; no-op otherwise.</summary>
        public void WriteIfApplied(FkBone b)
        {
            if (!Suspended && b != null && Applied[(int)b.Group] && !Owned.Contains(b)) WriteBone(b);
        }

        static void WriteBone(FkBone b)
        {
            if (!b.Used || !b.Bone) return;
            if (b.HasOverride) b.Write(b.Override);
            else if (b.HasValue) b.Write(b.Rotation);
            else if (FkOptions.MissingBonesZero) b.Write(Quaternion.identity);
        }

        /// <summary>No inertia.</summary>
        public void Reset() { }
    }

    public sealed class FkNeckGate : IMotionStage
    {
        public MotionPhase Phase => MotionPhase.IkPrepare;
        public string Name => "fkneck";
        public bool Enabled { get; set; } = true;
        public readonly CharaLook Look;
        /// <summary>The Neck group is applied.</summary>
        public bool Active;
        public bool Suspended;

        public FkNeckGate(CharaLook look) { Look = look; }

        public void Step(in MotionFrame frame)
        {
            if (Active && !Suspended) Hold();
        }

        /// <summary>Sets FIX when the neck look is in another pattern.</summary>
        public void Hold()
        {
            if (Look == null || Look.Neck == null) return;
            if (Look.NeckPattern != CharaLook.FkNeckPattern || Look.Neck.PtnNo != CharaLook.FkNeckPattern) Look.SetNeckPattern(CharaLook.FkNeckPattern);
        }

        public void Reset() { }
    }

    public sealed class FkHipsStage : IMotionStage
    {
        public MotionPhase Phase => MotionPhase.IkPrepare;
        public string Name => "fkhips";
        public bool Enabled { get; set; } = true;
        /// <summary>cf_J_Hips (parent cf_N_height).</summary>
        public readonly Transform Hips;
        /// <summary>fk.hipsOffset.</summary>
        public float Offset;
        /// <summary>fk.hipsPosition (cf_N_height frame) or null: when set, replaces the input as the written base.</summary>
        public Vector3? Position;
        /// <summary>FkOptions.HipsOffset AND fk.enabled AND fk.groups.body AND Offset ≠ 0 (set by the driver through SetActive).</summary>
        public bool Active { get; private set; }
        public bool Suspended { get; private set; }

        Vector3 basePos, written;
        bool hasWritten;

        public FkHipsStage(Transform hips) { Hips = hips; }

        /// <summary>The position the stage computed its last write from (diagnostics, F12).</summary>
        public Vector3 Base => basePos;
        public bool HasWritten => hasWritten;

        public void Step(in MotionFrame frame)
        {
            if (!Hips) return;
            if (!Active || Suspended) { Release(); return; }
            var cur = Hips.localPosition;
            if (!(hasWritten && Same(cur, written))) basePos = cur;
            var p = Position ?? basePos;
            Hips.localPosition = new Vector3(p.x, p.y + Offset, p.z);
            written = Hips.localPosition;
            hasWritten = true;
        }

        /// <summary>Changes the condition; ending it writes the base back once (if the bone still holds the stage's value).</summary>
        public void SetActive(bool active, float offset)
        {
            Offset = offset;
            Active = active;
            if (!active) Release();
        }

        /// <summary>As <see cref="SetActive(bool, float)"/> with the hips position (null = none). The caller passes
        /// active = fk.enabled AND fk.groups.body AND (position set OR the offset applies), and offset = 0 when the offset
        /// does not apply (FkOptions.HipsOffset off).</summary>
        public void SetActive(bool active, float offset, Vector3? position)
        {
            Position = position;
            SetActive(active, offset);
        }

        /// <summary>Suspend: base written back once; Resume: the next step re-applies.</summary>
        public void SetSuspended(bool suspended)
        {
            Suspended = suspended;
            if (suspended) Release();
        }

        void Release()
        {
            if (!hasWritten) return;
            if (Hips && Same(Hips.localPosition, written)) Hips.localPosition = basePos;
            hasWritten = false;
        }

        /// <summary>Applies the offset now (outside a step), from the bone's current value as base.</summary>
        public void ApplyNow()
        {
            if (Active && !Suspended) Step(new MotionFrame(0f, 0f, 0f, 0));
        }

        public void Reset() { }

        static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
    }
}
