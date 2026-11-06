// Bone corrections of one character (advanced pose): deltas in the bone's local space, composed on what the pose sources left
// and taken off again before anything else reads the bone, so no other writer ever sees a correction.
//   Apply     (stage pose.apply, phase 450) for each entry: Base := the bone's local values; bone := Base composed with the
//             correction on the channels that are not identity; W := the values as stored; E := W
//             position  Base + pos      rotation  Base * Euler(rot)      scale  Base x scale per axis
//   End       (stage pose.end, phase 950, after everything that writes bones) E := what the frame shows
//   Restore   (stage pose.restore, phase -100, first in a step, so before the card's bone modifiers at phase 120) when applied: a channel
//             within 1e-6 of E gets Base back; any other value stays (somebody wrote the bone between two frames: a command's FK
//             write, a Maker flush; it is the next input). Idempotent. Inside a probe the channels are written but the set stays
//             applied, because the probe writes the corrected values back when it ends.
// Two writers after the apply need a word. The eye look sets the eye bones from its own values and, while no time passes, holds them
// by not writing: a restore would take its output off for good, so while it is on a rotation correction of an eye bone writes nothing
// (it does not show either way). A swing chain writes position and rotation of its bones and takes them back to rest only when both
// still hold its write: on a chain bone the restore covers both channels, whichever the correction names.
// Nothing here runs unless a character has a correction: the driver creates the stages with the first entry and removes them with
// the last.
using System;
using System.Collections.Generic;
using HS2Next.Motion.Face;
using HS2Next.Motion.Look;
using UnityEngine;

namespace HS2Next.Motion.AdvancedPose
{
    /// <summary>One corrected transform of a character. The D_ members are plain values, so another source can feed them.</summary>
    public sealed class CorrectedBone
    {
        public readonly string Address;
        public readonly Transform Bone;

        /// <summary>The correction: position added, Euler rotation post-multiplied, scale multiplied per axis.</summary>
        public Vector3 D_pos, D_rot, D_scale = Vector3.one;
        /// <summary>The channels that are not identity (only those are written).</summary>
        public bool HasPos, HasRot, HasScale;
        internal Quaternion Dq = Quaternion.identity;

        /// <summary>The local values the last apply composed on: what the pose sources left.</summary>
        public Vector3 BasePos, BaseScale;
        public Quaternion BaseRot;
        /// <summary>The corrected channels as the last apply stored them.</summary>
        public Vector3 WPos, WScale;
        public Quaternion WRot;
        /// <summary>The corrected channels at the end of the last frame (a later stage's write included).</summary>
        public Vector3 EPos, EScale;
        public Quaternion ERot;
        /// <summary>The face stage that writes this bone's scale x (the mouth width), or null.</summary>
        internal ExpressionStage WidthStage;
        /// <summary>The eye look that writes this bone's rotation, or null.</summary>
        internal EyeLookStage EyeStage;
        /// <summary>A swing chain writes this bone's position and rotation.</summary>
        internal bool ChainBone;
        /// <summary>The last apply wrote the rotation channel (not while the eye look owns it).</summary>
        internal bool RotOn;

        // what a bare read took off
        internal Vector3 SPos, SScale;
        internal Quaternion SRot;
        internal bool SP, SR, SS;

        public CorrectedBone(string address, Transform bone) { Address = address; Bone = bone; }

        /// <summary>Sets the correction; identity members write nothing.</summary>
        public void Set(Vector3 pos, Vector3 rot, Vector3 scale)
        {
            D_pos = pos; D_rot = rot; D_scale = scale;
            HasPos = pos.x != 0f || pos.y != 0f || pos.z != 0f;
            HasRot = rot.x != 0f || rot.y != 0f || rot.z != 0f;
            HasScale = scale.x != 1f || scale.y != 1f || scale.z != 1f;
            Dq = HasRot ? Quaternion.Euler(rot) : Quaternion.identity;
        }

        public bool IsIdentity => !HasPos && !HasRot && !HasScale;
    }

    /// <summary>The corrected bones of one character and the three operations of a frame.</summary>
    public sealed class BoneCorrections
    {
        const float Tolerance = 1e-6f;
        const float ShownTolerance = 1e-5f;

        readonly List<CorrectedBone> entries = new List<CorrectedBone>();
        readonly CharaMotion motion;

        public BoneCorrections(CharaMotion motion) { this.motion = motion; }

        public IReadOnlyList<CorrectedBone> Entries => entries;
        /// <summary>True from an apply until the next restore: the bones hold the corrected values.</summary>
        public bool Applied { get; private set; }
        /// <summary>No apply while suspended (character thumbnails).</summary>
        public bool Suspended;

        /// <summary>Replaces the entries; call between <see cref="Restore"/> and <see cref="Apply"/>.</summary>
        public void Replace(IEnumerable<CorrectedBone> next)
        {
            entries.Clear();
            entries.AddRange(next);
        }

        /// <summary>The entry of a transform, or null.</summary>
        public CorrectedBone Of(Transform t)
        {
            for (int i = 0; i < entries.Count; i++) if (entries[i].Bone == t) return entries[i];
            return null;
        }

        /// <summary>The entry of an address, or null.</summary>
        public CorrectedBone Of(string address)
        {
            for (int i = 0; i < entries.Count; i++) if (entries[i].Address == address) return entries[i];
            return null;
        }

        // ------------------------------------------------------------ the frame

        /// <summary>Base := the bones' local values; the bones := Base composed with the corrections; W and E := as stored.</summary>
        public void Apply()
        {
            if (Suspended) return;
            bool probing = motion && motion.Probing;
            for (int i = 0; i < entries.Count; i++) if (entries[i].Bone) ApplyEntry(entries[i], probing);
            if (!probing) Applied = true;
        }

        static void ApplyEntry(CorrectedBone e, bool probing)
        {
            var t = e.Bone;
            t.GetLocalPositionAndRotation(out var lp, out var lr);
            var ls = t.localScale;
            e.RotOn = e.HasRot && !EyeLookOwns(e);
            if (probing)
            {
                // the probe puts every transform back and the set's values stay those of the frame outside it
                Write(e, t, lp, lr, ls);
                return;
            }
            e.BasePos = lp; e.BaseRot = lr; e.BaseScale = ls;
            Write(e, t, lp, lr, ls);
            t.GetLocalPositionAndRotation(out var wp, out var wr);
            e.WPos = e.EPos = wp;
            e.WRot = e.ERot = wr;
            if (e.HasScale) e.WScale = e.EScale = t.localScale;
        }

        static bool EyeLookOwns(CorrectedBone e)
        {
            var s = e.EyeStage;
            return s != null && s.Enabled && EyeLookStage.GlobalEnabled && s.LookEnabled;
        }

        static void Write(CorrectedBone e, Transform t, in Vector3 lp, in Quaternion lr, in Vector3 ls)
        {
            if (e.HasPos && e.RotOn) t.SetLocalPositionAndRotation(lp + e.D_pos, lr * e.Dq);
            else if (e.HasPos) t.localPosition = lp + e.D_pos;
            else if (e.RotOn) t.localRotation = lr * e.Dq;
            if (e.HasScale)
            {
                var d = e.D_scale;
                t.localScale = new Vector3(ls.x * d.x, ls.y * d.y, ls.z * d.z);
            }
        }

        /// <summary>E := the corrected channels as they stand now (what the frame shows).</summary>
        public void End()
        {
            if (!Applied || (motion && motion.Probing)) return;
            for (int i = 0; i < entries.Count; i++) Remember(entries[i]);
        }

        static void Remember(CorrectedBone e)
        {
            var t = e.Bone;
            if (!t) return;
            t.GetLocalPositionAndRotation(out var p, out var r);
            e.EPos = p; e.ERot = r;
            if (e.HasScale) e.EScale = t.localScale;
        }

        /// <summary>Every bone that still holds the frame's value back to its base; nothing when not applied.</summary>
        public void Restore()
        {
            if (!Applied) return;
            bool probing = motion && motion.Probing;
            for (int i = 0; i < entries.Count; i++) RestoreEntry(entries[i]);
            if (!probing) Applied = false;
        }

        static void RestoreEntry(CorrectedBone e)
        {
            var t = e.Bone;
            if (!t) return;
            t.GetLocalPositionAndRotation(out var lp, out var lr);
            if ((e.HasPos || e.ChainBone) && Near(lp, e.EPos)) t.localPosition = e.BasePos;
            if ((e.RotOn || e.ChainBone) && Near(lr, e.ERot)) t.localRotation = e.BaseRot;
            if (e.HasScale)
            {
                if (Near(t.localScale, e.EScale)) t.localScale = e.BaseScale;
                // the face stage writes the width only when it changes: this write would otherwise stand
                if (e.WidthStage != null) e.WidthStage.InvalidateMouthWidth();
            }
        }

        /// <summary>
        /// Runs <paramref name="read"/> with the bones as they are without the corrections and puts the shown values back; the set's
        /// state does not change, so this works inside a step as well. For readers that store a base (a capture).
        /// </summary>
        public void Bare(Action read)
        {
            if (!Applied) { read(); return; }
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var t = e.Bone;
                e.SP = e.SR = e.SS = false;
                if (!t) continue;
                t.GetLocalPositionAndRotation(out var lp, out var lr);
                if ((e.HasPos || e.ChainBone) && Near(lp, e.EPos)) { e.SPos = lp; e.SP = true; t.localPosition = e.BasePos; }
                if ((e.RotOn || e.ChainBone) && Near(lr, e.ERot)) { e.SRot = lr; e.SR = true; t.localRotation = e.BaseRot; }
                if (e.HasScale)
                {
                    var ls = t.localScale;
                    if (Near(ls, e.EScale)) { e.SScale = ls; e.SS = true; t.localScale = e.BaseScale; }
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

        /// <summary>For a writer after <see cref="End"/>: E := the entry's current values, so the next restore takes that write off
        /// as well and the writer's value does not become the next base.</summary>
        public void ForgetFinal(Transform t)
        {
            if (!Applied) return;
            var e = Of(t);
            if (e != null) Remember(e);
        }

        /// <summary>The entry's bone holds the values the last apply stored, on every corrected channel: nothing wrote over them.</summary>
        public static bool Shown(CorrectedBone e)
        {
            var t = e.Bone;
            if (!t) return false;
            t.GetLocalPositionAndRotation(out var p, out var r);
            if (e.HasPos && !Near(p, e.WPos, ShownTolerance)) return false;
            if (e.HasRot && (!e.RotOn || !Near(r, e.WRot, ShownTolerance))) return false;
            if (e.HasScale && !Near(t.localScale, e.WScale, ShownTolerance)) return false;
            return true;
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
