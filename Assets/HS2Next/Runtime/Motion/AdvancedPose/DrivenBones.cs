// Driven bones of one character: values laid over a bone inside the character's step, so what runs after them sees them (twist bones, the eye
// look, physics, skinning), and taken off again before anything reads the bone in the next step. Nothing here runs unless a character has
// a driven bone: the scene creates the stages with the first and removes them after the last.
//   Restore   (stage bones.driven.restore, phase -99, right after the corrections' restore) a written channel within 1e-6 of W or E gets Base back;
//             any other value stays (somebody wrote the bone between two frames: it is the next input). W as well as E, because the corrections'
//             restore at -100 puts their base back, and their base is what this set wrote. Inside a probe the channels are written but the set
//             stays applied, because the probe writes the shown values back when it ends.
//   Apply     (stage bones.driven, phase 440, after FK, before the corrections) the evaluator first: it writes one bone at a time, each after the one
//             it reads, so the next reads a bone already written; then every channel with a value it did not write, parents first
//             Base := the bone's local values at the first write of the step; bone := the channel's value; W := E := as stored
//   End       (stage bones.driven.end, phase 955, after the corrections' end) E := what the frame shows
// Two writers after the apply need a word, as for a correction. While the eye look is on, a rotation on an eye bone writes nothing: it holds
// the bone by not writing, so a restore would take its output off for good. A swing chain writes position and rotation and takes them back
// to rest only when both still hold its write: on a chain bone the restore covers both channels, whichever channel is driven. IK finds its own
// write by the bits, and the rotation setter may store other last bits than it is given: a restore that gives IK's value back tells IK what the bone holds.
using System;
using System.Collections.Generic;
using HS2Next.Motion.Face;
using HS2Next.Motion.Ik;
using HS2Next.Motion.Look;
using UnityEngine;

namespace HS2Next.Motion.AdvancedPose
{
    /// <summary>The channels of a bone.</summary>
    [Flags]
    public enum DrivenChannels
    {
        None = 0,
        Position = 1,
        Rotation = 2,
        Scale = 4,
    }

    /// <summary>What writes a character's bones one at a time, inside stage 440: the owner of the values that depend on the frame's pose.</summary>
    public interface IDrivenEvaluator
    {
        /// <summary>Computes each value, sets its slot and calls <see cref="DrivenBones.Write"/> before the next one, in the order wanted.</summary>
        void EvaluateBones(DrivenBones set, in MotionFrame frame);
    }

    /// <summary>One driven transform of a character.</summary>
    public sealed class DrivenBone
    {
        public readonly string Address;
        public readonly Transform Bone;
        /// <summary>How deep the bone stands under the scene's root: a parent is always less deep than its child.</summary>
        public readonly int Depth;

        /// <summary>The channels that have a value now, local to the bone's parent. A value reaches the bone at stage 440.</summary>
        public DrivenChannels On { get; private set; }
        public Vector3 Pos, Scale;
        public Quaternion Rot;

        /// <summary>The channels written since the last restore.</summary>
        public DrivenChannels Written { get; internal set; }
        /// <summary>The local values at the first write of the step: what the pose left.</summary>
        public Vector3 BasePos, BaseScale;
        public Quaternion BaseRot;
        /// <summary>The written values as stored.</summary>
        public Vector3 WPos, WScale;
        public Quaternion WRot;
        /// <summary>The values at the end of the last frame (a later stage's write included).</summary>
        public Vector3 EPos, EScale;
        public Quaternion ERot;

        /// <summary>The face stage that writes this bone's scale x (the mouth width), or null.</summary>
        internal ExpressionStage WidthStage;
        /// <summary>The eye look that writes this bone's rotation, or null.</summary>
        internal EyeLookStage EyeStage;
        /// <summary>A swing chain writes this bone's position and rotation.</summary>
        internal bool ChainBone;
        /// <summary>The IK stage with this bone in its slots, and the slot; null, -1 otherwise.</summary>
        internal IkStage Ik;
        internal int IkSlot = -1;
        /// <summary>The step that wrote the channels of <see cref="Done"/>.</summary>
        internal long Walk;
        internal DrivenChannels Done;

        // what a bare read took off
        internal Vector3 SPos, SScale;
        internal Quaternion SRot;
        internal bool SP, SR, SS;

        public DrivenBone(string address, Transform bone)
        {
            Address = address;
            Bone = bone;
            for (var p = bone; p; p = p.parent) Depth++;
        }

        public void SetPos(Vector3 v) { Pos = v; On |= DrivenChannels.Position; }
        public void SetRot(Quaternion v) { Rot = v; On |= DrivenChannels.Rotation; }
        public void SetScale(Vector3 v) { Scale = v; On |= DrivenChannels.Scale; }
        /// <summary>The channel has no value any more; the next step's restore takes what is written off.</summary>
        public void Clear(DrivenChannels c) => On &= ~c;
    }

    /// <summary>The driven bones of one character and the three operations of a frame.</summary>
    public sealed class DrivenBones
    {
        const float Tolerance = 1e-6f;
        const float ShownTolerance = 1e-5f;

        readonly List<DrivenBone> entries = new List<DrivenBone>();
        readonly CharaMotion motion;
        long walk;

        public DrivenBones(CharaMotion motion) { this.motion = motion; }

        /// <summary>The bones, parents before their children.</summary>
        public IReadOnlyList<DrivenBone> Entries => entries;
        /// <summary>True from a write until the next restore.</summary>
        public bool Applied { get; private set; }
        /// <summary>No write while suspended (character thumbnails).</summary>
        public bool Suspended;
        /// <summary>Runs first in the apply; null when only plain values drive this character.</summary>
        public IDrivenEvaluator Evaluator;

        /// <summary>The entry of an address, or null.</summary>
        public DrivenBone Of(string address)
        {
            for (int i = 0; i < entries.Count; i++) if (entries[i].Address == address) return entries[i];
            return null;
        }

        /// <summary>The entry of a transform, or null.</summary>
        public DrivenBone Of(Transform t)
        {
            for (int i = 0; i < entries.Count; i++) if (entries[i].Bone == t) return entries[i];
            return null;
        }

        internal void Add(DrivenBone e)
        {
            int i = entries.Count;
            while (i > 0 && entries[i - 1].Depth > e.Depth) i--;
            entries.Insert(i, e);
        }

        internal bool Remove(DrivenBone e) => entries.Remove(e);

        // ------------------------------------------------------------ the frame

        /// <summary>Stage 440: the evaluator's bones one at a time, then the channels with a value nothing wrote in this step.</summary>
        public void Apply(in MotionFrame frame)
        {
            if (Suspended) return;
            walk++;
            Evaluator?.EvaluateBones(this, frame);
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var left = e.Walk == walk ? e.On & ~e.Done : e.On;
                if (left != DrivenChannels.None) Write(e, left);
            }
        }

        /// <summary>
        /// Writes <paramref name="channels"/> of <paramref name="e"/> with their values now. The first write of a step keeps the bone's values as Base; a channel
        /// written again in the same step keeps that Base.
        /// </summary>
        public void Write(DrivenBone e, DrivenChannels channels)
        {
            var t = e.Bone;
            if (!t) return;
            channels &= e.On;
            if ((channels & DrivenChannels.Rotation) != 0 && EyeLookOwns(e)) channels &= ~DrivenChannels.Rotation;
            if (channels == DrivenChannels.None) return;
            if (e.Walk != walk) { e.Walk = walk; e.Done = DrivenChannels.None; }
            t.GetLocalPositionAndRotation(out var lp, out var lr);
            var ls = t.localScale;
            if (e.Written == DrivenChannels.None) { e.BasePos = lp; e.BaseRot = lr; e.BaseScale = ls; }
            bool pos = (channels & DrivenChannels.Position) != 0, rot = (channels & DrivenChannels.Rotation) != 0;
            if (pos && rot) t.SetLocalPositionAndRotation(e.Pos, e.Rot);
            else if (pos) t.localPosition = e.Pos;
            else if (rot) t.localRotation = e.Rot;
            if ((channels & DrivenChannels.Scale) != 0)
            {
                t.localScale = e.Scale;
                e.WScale = e.EScale = t.localScale;
            }
            t.GetLocalPositionAndRotation(out var wp, out var wr);
            e.WPos = e.EPos = wp;
            e.WRot = e.ERot = wr;
            e.Written |= channels;
            e.Done |= channels;
            Applied = true;
        }

        static bool EyeLookOwns(DrivenBone e)
        {
            var s = e.EyeStage;
            return s != null && s.Enabled && EyeLookStage.GlobalEnabled && s.LookEnabled;
        }

        /// <summary>Stage 955: E := the written channels as they stand now.</summary>
        public void End()
        {
            if (!Applied || (motion && motion.Probing)) return;
            for (int i = 0; i < entries.Count; i++) Remember(entries[i]);
        }

        static void Remember(DrivenBone e)
        {
            var t = e.Bone;
            if (!t || e.Written == DrivenChannels.None) return;
            t.GetLocalPositionAndRotation(out var p, out var r);
            e.EPos = p; e.ERot = r;
            if ((e.Written & DrivenChannels.Scale) != 0) e.EScale = t.localScale;
        }

        /// <summary>For a writer after <see cref="End"/>: E := the entry's current values, so the next restore takes that write off as well and the writer's
        /// value does not outlive the driven value as the next input.</summary>
        public void ForgetFinal(Transform t)
        {
            if (!Applied) return;
            var e = Of(t);
            if (e != null) Remember(e);
        }

        /// <summary>Every bone that still holds the frame's value back to its base; nothing when nothing is applied. Idempotent.</summary>
        public void Restore()
        {
            if (!Applied) return;
            bool probing = motion && motion.Probing;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                RestoreEntry(e);
                if (!probing) e.Written = DrivenChannels.None;
            }
            if (!probing) Applied = false;
        }

        static void RestoreEntry(DrivenBone e)
        {
            var t = e.Bone;
            if (!t || e.Written == DrivenChannels.None) return;
            t.GetLocalPositionAndRotation(out var lp, out var lr);
            if (((e.Written & DrivenChannels.Position) != 0 || e.ChainBone) && (Near(lp, e.EPos) || Near(lp, e.WPos))) t.localPosition = e.BasePos;
            if (((e.Written & DrivenChannels.Rotation) != 0 || e.ChainBone) && (Near(lr, e.ERot) || Near(lr, e.WRot)))
            {
                t.localRotation = e.BaseRot;
                e.Ik?.Relatch(e.IkSlot, e.BaseRot);
            }
            if ((e.Written & DrivenChannels.Scale) != 0)
            {
                var ls = t.localScale;
                if (Near(ls, e.EScale) || Near(ls, e.WScale)) t.localScale = e.BaseScale;
                // the face stage writes the width only when it changes: this write would otherwise stand
                if (e.WidthStage != null) e.WidthStage.InvalidateMouthWidth();
            }
        }

        /// <summary>
        /// Runs <paramref name="read"/> with the bones as they are without the driven values and puts the shown values back; the set's state does not change, so
        /// this works inside a step as well. For readers that store a base (a capture).
        /// </summary>
        public void Bare(Action read)
        {
            if (!Applied) { read(); return; }
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var t = e.Bone;
                e.SP = e.SR = e.SS = false;
                if (!t || e.Written == DrivenChannels.None) continue;
                t.GetLocalPositionAndRotation(out var lp, out var lr);
                if (((e.Written & DrivenChannels.Position) != 0 || e.ChainBone) && (Near(lp, e.EPos) || Near(lp, e.WPos))) { e.SPos = lp; e.SP = true; t.localPosition = e.BasePos; }
                if (((e.Written & DrivenChannels.Rotation) != 0 || e.ChainBone) && (Near(lr, e.ERot) || Near(lr, e.WRot))) { e.SRot = lr; e.SR = true; t.localRotation = e.BaseRot; }
                if ((e.Written & DrivenChannels.Scale) != 0)
                {
                    var ls = t.localScale;
                    if (Near(ls, e.EScale) || Near(ls, e.WScale)) { e.SScale = ls; e.SS = true; t.localScale = e.BaseScale; }
                }
            }
            try { read(); }
            finally
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    var t = e.Bone;
                    if (!t) continue;
                    if (e.SP) t.localPosition = e.SPos;
                    if (e.SR) t.localRotation = e.SRot;
                    if (e.SS) t.localScale = e.SScale;
                }
            }
        }

        /// <summary>
        /// True when the bone holds what was written on <paramref name="channel"/>: no later stage wrote over it. A value not written yet counts as shown, unless the
        /// eye look holds the bone. <paramref name="corrected"/> is the same answer for the correction on this bone, null when none: a correction stays on top.
        /// </summary>
        public static bool Shown(DrivenBone e, DrivenChannels channel, CorrectedBone corrected)
        {
            var t = e.Bone;
            if (!t) return false;
            if (channel == DrivenChannels.Rotation && EyeLookOwns(e)) return false;
            if ((e.Written & channel) == 0) return true;
            t.GetLocalPositionAndRotation(out var p, out var r);
            switch (channel)
            {
                case DrivenChannels.Position: return corrected != null && corrected.HasPos ? BoneCorrections.Shown(corrected) : Near(p, e.WPos, ShownTolerance);
                case DrivenChannels.Rotation: return corrected != null && corrected.HasRot ? BoneCorrections.Shown(corrected) : Near(r, e.WRot, ShownTolerance);
                default: return corrected != null && corrected.HasScale ? BoneCorrections.Shown(corrected) : Near(t.localScale, e.WScale, ShownTolerance);
            }
        }

        // ------------------------------------------------------------ comparisons

        static bool Near(in Vector3 a, in Vector3 b, float tol = Tolerance) =>
            Mathf.Abs(a.x - b.x) <= tol && Mathf.Abs(a.y - b.y) <= tol && Mathf.Abs(a.z - b.z) <= tol;

        static bool Near(in Quaternion a, in Quaternion b, float tol = Tolerance)
        {
            bool q = Mathf.Abs(a.x - b.x) <= tol && Mathf.Abs(a.y - b.y) <= tol && Mathf.Abs(a.z - b.z) <= tol && Mathf.Abs(a.w - b.w) <= tol;
            return q || (Mathf.Abs(a.x + b.x) <= tol && Mathf.Abs(a.y + b.y) <= tol && Mathf.Abs(a.z + b.z) <= tol && Mathf.Abs(a.w + b.w) <= tol);
        }
    }
}
