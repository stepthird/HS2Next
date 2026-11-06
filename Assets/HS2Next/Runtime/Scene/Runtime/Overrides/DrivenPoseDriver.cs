// The driven bones of built characters (DrivenBones in Runtime/Motion): the per-character state behind the override layer's bone channels and
// the constraints evaluated inside a step. Nothing exists for a character until a value or an evaluator asks for it: no stage, no handler.
//   Bind        resolves an address on the character, the mosaic rule and the bone's writers included; the entry a slot or a constraint writes through
//   Sweep       the end of an Apply: entries with no value and nothing left to restore go; with the last one the three stages go
//   Bare        what a reader that stores a base sees: nested inside the corrections' bare read
//   RestoreNow  a flush, a revive, a controller's bind: the bones back to what the pose left, the corrections first
//   Suspend     character thumbnails: the bones back, the stages off until the handle is disposed
// A rebuild drops the state with the root: the new body starts from its own pose.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HS2Next.Motion;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Motion.Animation;
using HS2Next.Motion.DynamicBones;
using HS2Next.Motion.Face;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>The driven-bone state of one built character. It lives while the character has a driven bone and dies with the root.</summary>
    public sealed class DrivenCharacter
    {
        public readonly CharaRealization Realization;
        public readonly GameObject Root;
        public readonly DrivenBones Set;
        public bool Suspended { get; internal set; }

        internal DrivenRestoreStage RestoreStage;
        internal DrivenApplyStage ApplyStage;
        internal DrivenEndStage EndStage;
        internal Action<CharaAnimation> BeforeBindHandler;

        internal DrivenCharacter(CharaRealization r)
        {
            Realization = r;
            Root = r.Root;
            Set = new DrivenBones(r.Motion);
        }

        /// <summary>True while the three stages are on the character's motion.</summary>
        public bool HasStages => RestoreStage != null;
    }

    public sealed class DrivenPoseDriver : ICharaPoseDriver
    {
        public static readonly DrivenPoseDriver Instance = new DrivenPoseDriver();

        static readonly ConditionalWeakTable<GameObject, DrivenCharacter> states = new ConditionalWeakTable<GameObject, DrivenCharacter>();

        /// <summary>The state of a built character, or null.</summary>
        public static DrivenCharacter Of(CharaRealization r)
        {
            var root = r?.Root;
            if (!root || !states.TryGetValue(root, out var c)) return null;
            return c.Realization == r ? c : null;
        }

        static DrivenCharacter OfRoot(GameObject root) => root && states.TryGetValue(root, out var c) ? c : null;

        /// <summary>The state of a built character with its three stages, created when missing; null when it is not built.</summary>
        public static DrivenCharacter Ensure(CharaRealization r)
        {
            if (r == null || r.State != RealizationState.Built || !r.Root || !r.Motion) return null;
            var c = Of(r);
            if (c == null)
            {
                c = new DrivenCharacter(r);
                states.Remove(r.Root);
                states.Add(r.Root, c);
                r.Runtime.OverrideApp.Track(c);
                if (r.Animation)
                {
                    // the corrections first, the reverse of the writes: this handler may run after theirs or before
                    c.BeforeBindHandler = _ => { AdvancedPoseDriver.RestoreNow(r); c.Set.Restore(); };
                    r.Animation.BeforeBind += c.BeforeBindHandler;
                }
            }
            EnsureStages(c);
            return c;
        }

        /// <summary>
        /// The entry of a bone by its canonical address, made when missing: what a slot's channel and a constraint write through. Null when the character lacks
        /// the bone, or when the mosaic rule names it: such a bone is bound to nothing. A resolve is the only place the rule is asked, so a part swap that
        /// brings a member follows it.
        /// </summary>
        public static DrivenBone Bind(CharaRealization r, string address)
        {
            if (r == null || r.State != RealizationState.Built) return null;
            if (!PoseAddress.TryParse(PoseAddressForm.Bone, address, out var a, out _)) return null;
            var t = AdvancedPoseResolver.Resolve(r, a);
            var old = Of(r);
            if (!t || AdvancedPoseGuard.Bone(r, t))
            {
                var stale = old?.Set.Of(address);
                if (stale != null) Unbound(old, stale);
                return null;
            }
            var c = Ensure(r);
            if (c == null) return null;
            var e = c.Set.Of(address);
            if (e != null && e.Bone == t) return e;
            if (e != null) Unbound(c, e);
            e = new DrivenBone(address, t);
            FindWriters(r, e, t);
            c.Set.Add(e);
            return e;
        }

        // a bone that went or that the rule now names: the values go, and what was written stays taken off by the restore, as far as the bone still stands
        static void Unbound(DrivenCharacter c, DrivenBone e)
        {
            e.Clear(DrivenChannels.Position | DrivenChannels.Rotation | DrivenChannels.Scale);
            if (e.Written == DrivenChannels.None) c.Set.Remove(e);
        }

        static void FindWriters(CharaRealization r, DrivenBone e, Transform t)
        {
            bool chain = AdvancedPoseResolver.ChainBones(r).Contains(t);
            e.ChainBone = chain;
            // the chain bones' base is their rest pose, not the last write-back
            if (chain && r.Motion) r.Motion.Get<DynamicBoneStage>()?.ResetBeforeUpdate();
            if (r.Motion)
                foreach (var s in r.Motion.GetAll<ExpressionStage>())
                    if (s.MouthWidthBone == t) { e.WidthStage = s; break; }
            var eyes = r.Look?.Eyes;
            if (eyes?.Eyes != null)
                foreach (var x in eyes.Eyes)
                    if (x != null && x.Transform == t) { e.EyeStage = eyes; break; }
            var ik = IkDriver.Of(r);
            if (ik?.Bones != null)
                for (int s = 0; s < ik.Bones.Slots.Length; s++)
                    if (ik.Bones.Slots[s] == t) { e.Ik = ik.Stage; e.IkSlot = s; break; }
        }

        /// <summary>The end of an Apply: entries with no value and nothing written go unless an evaluator keeps them; a set with none left loses its stages.</summary>
        internal static bool Sweep(DrivenCharacter c)
        {
            var set = c.Set;
            if (set.Evaluator != null) return false;
            for (int i = set.Entries.Count - 1; i >= 0; i--)
            {
                var e = set.Entries[i];
                if (e.On == DrivenChannels.None && e.Written == DrivenChannels.None) set.Remove(e);
            }
            if (set.Entries.Count > 0) return false;
            RemoveStages(c);
            var r = c.Realization;
            if (c.BeforeBindHandler != null && r.Animation) r.Animation.BeforeBind -= c.BeforeBindHandler;
            if (c.Root) states.Remove(c.Root);
            return true;
        }

        static void EnsureStages(DrivenCharacter c)
        {
            if (c.HasStages) return;
            var m = c.Realization.Motion;
            if (!m) return;
            c.RestoreStage = new DrivenRestoreStage(c.Set);
            c.ApplyStage = new DrivenApplyStage(c.Set);
            c.EndStage = new DrivenEndStage(c.Set);
            if (c.Suspended) SetStagesEnabled(c, false);
            m.Add(c.RestoreStage);
            m.Add(c.ApplyStage);
            m.Add(c.EndStage);
        }

        static void RemoveStages(DrivenCharacter c)
        {
            if (!c.HasStages) return;
            var m = c.Realization.Motion;
            if (m)
            {
                m.Remove(c.RestoreStage);
                m.Remove(c.ApplyStage);
                m.Remove(c.EndStage);
            }
            c.RestoreStage = null; c.ApplyStage = null; c.EndStage = null;
        }

        static void SetStagesEnabled(DrivenCharacter c, bool on)
        {
            if (!c.HasStages) return;
            c.RestoreStage.Enabled = on;
            c.ApplyStage.Enabled = on;
            c.EndStage.Enabled = on;
        }

        // ------------------------------------------------------------ readers

        /// <summary>Runs <paramref name="read"/> with the bones as they are without the driven values.</summary>
        public static void Bare(CharaRealization r, Action read)
        {
            var c = Of(r);
            if (c != null) c.Set.Bare(read);
            else read();
        }

        /// <summary>The bones back to what the pose left; the caller takes the corrections off first. The next step applies again.</summary>
        public static void RestoreNow(CharaRealization r) => Of(r)?.Set.Restore();

        /// <summary>For a writer after the last pose stage: what it wrote on <paramref name="t"/> does not become the next input.</summary>
        public static void ForgetFinal(CharaRealization r, Transform t) => Of(r)?.Set.ForgetFinal(t);

        // ------------------------------------------------------------ the pose driver's suspend

        public void Apply(CharaRealization r, CharaNodeState state, PoseApplyReason reason) { }
        public void OnChanged(CharaRealization r, string path) { }

        public IDisposable Suspend(GameObject charaRoot)
        {
            var c = OfRoot(charaRoot);
            if (c == null || c.Suspended) return null;
            c.Suspended = true;
            c.Set.Restore();
            c.Set.Suspended = true;
            SetStagesEnabled(c, false);
            return new Resume(c);
        }

        sealed class Resume : IDisposable
        {
            DrivenCharacter c;
            public Resume(DrivenCharacter c) { this.c = c; }
            public void Dispose()
            {
                var x = c;
                c = null;
                if (x == null || !x.Root) return;
                x.Suspended = false;
                x.Set.Suspended = false;
                SetStagesEnabled(x, true);
            }
        }
    }
}
