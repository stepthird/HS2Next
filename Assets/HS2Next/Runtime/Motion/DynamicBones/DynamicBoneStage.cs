// Motion stage for the ordinary DynamicBone chains of one character: hair, clothes, accessories and any
// other part carrying a DynamicBone. Runs last (MotionPhase.DynamicBone,
// the game's execution order 13100) on the final pose. Owns the shared collider table (evaluated once per
// step), the time mode (frame-locked or fixed rate), the AnimatePhysics fixed-tick emulation
// and the Studio switches (hair FK group, skirt FK group, accessory slots, per chain), applied with
// OnEnable / OnDisable semantics.
using System.Collections.Generic;
using HS2Next.Chara;
using UnityEngine;

namespace HS2Next.Motion.DynamicBones
{
    public sealed class DynamicBoneStage : IMotionStage
    {
        public MotionPhase Phase => MotionPhase.DynamicBone;
        public string Name => "dynbone";

        public readonly DynamicBoneOptions Options;
        public readonly ColliderTable Colliders = new ColliderTable();
        public readonly List<DynamicBoneChain> Chains = new List<DynamicBoneChain>();
        /// <summary>The character the stage was attached to (null for a stage built by hand); Rebuild rescans its parts.</summary>
        public CharaContext Context;

        // ---- Studio switches
        /// <summary>FK group Hair active: all hair chains off (FK owns the bones).</summary>
        public bool HairFkActive;
        /// <summary>FK group Skirt active: the skirt chains of the top / bottom clothes off.</summary>
        public bool SkirtFkActive;
        /// <summary>Extra switches (not in the game, default on): every clothes / accessory / other chain.</summary>
        public bool ClothesEnabled = true, AccessoriesEnabled = true, OtherEnabled = true;

        bool enabled = true;
        float fixedAcc;

        public DynamicBoneStage(DynamicBoneOptions options = null)
        {
            Options = options ?? new DynamicBoneOptions();
            Colliders.LegacySphereBug = Options.LegacySphereBug;
        }

        /// <summary>Disabling restores the Start pose of every active chain (OnDisable); enabling resets particles on the next step.</summary>
        public bool Enabled
        {
            get => enabled;
            set
            {
                if (enabled == value) return;
                enabled = value;
                if (!value) foreach (var c in Chains) c.SetActive(false);
            }
        }

        public int ParticleCount { get { int k = 0; foreach (var c in Chains) k += c.ParticleCount; return k; } }
        public int ActiveChains { get { int k = 0; foreach (var c in Chains) if (c.Active) k++; return k; } }

        /// <summary>Adds one component as a chain (built from its fields and the current pose).</summary>
        public DynamicBoneChain Add(global::DynamicBone component, DynamicBoneCategory category, int kind = -1, int slot = -1, IList<MonoBehaviour> colliderList = null)
        {
            var c = new DynamicBoneChain(component, Options, Colliders, colliderList) { Category = category, Kind = kind, Slot = slot };
            Chains.Add(c);
            return c;
        }

        // ------------------------------------------------------------ switches
        public void SetHairFk(bool fkActive) => HairFkActive = fkActive;
        public void SetSkirtFk(bool fkActive) => SkirtFkActive = fkActive;

        /// <summary>Accessory slot shake on / off (the card's noShake as Studio would set it).</summary>
        public void SetAccessoryShake(int slot, bool shake)
        {
            foreach (var c in Chains) if (c.Category == DynamicBoneCategory.Accessory && c.Slot == slot) c.NoShake = !shake;
        }

        /// <summary>Hair bundle shake on / off (card dictBundle[bundle].noShake).</summary>
        public void SetHairBundleShake(int kind, int bundle, bool shake)
        {
            foreach (var c in Chains) if (c.Category == DynamicBoneCategory.Hair && c.Kind == kind && c.Bundle == bundle) c.NoShake = !shake;
        }

        /// <summary>Effective state of a chain: per-chain switch, card noShake, category / FK switches, component active and enabled.</summary>
        public bool WantActive(DynamicBoneChain c)
        {
            if (!enabled || !c.Enabled || c.NoShake || c.ParticleCount == 0) return false;
            if (c.Source == null || !c.Source.enabled || !c.Source.gameObject.activeInHierarchy) return false;
            switch (c.Category)
            {
                case DynamicBoneCategory.Hair: return !HairFkActive;
                case DynamicBoneCategory.Clothes: return ClothesEnabled && !(c.IsSkirt && SkirtFkActive);
                case DynamicBoneCategory.Accessory: return AccessoriesEnabled;
                default: return OtherEnabled;
            }
        }

        /// <summary>Applies the switches now (otherwise done at the start of each step).</summary>
        public void ApplySwitches()
        {
            for (int i = 0; i < Chains.Count; i++) Chains[i].SetActive(WantActive(Chains[i]));
        }

        // ------------------------------------------------------------ IMotionStage
        public void Step(in MotionFrame frame)
        {
            Colliders.LegacySphereBug = Options.LegacySphereBug;
            Colliders.Update();
            bool fixedTick = false;
            if (frame.Dt > 0f && Options.FixedDeltaTime > 0f)
            {
                fixedAcc += frame.Dt;
                if (fixedAcc >= Options.FixedDeltaTime)
                {
                    fixedTick = true;
                    fixedAcc %= Options.FixedDeltaTime;
                }
            }
            for (int i = 0; i < Chains.Count; i++)
            {
                var c = Chains[i];
                c.SetActive(WantActive(c));
                if (!c.Active) continue;
                if (frame.Dt <= 0f) { c.Hold(); continue; }   // no time: hold (Phase 3.6 pause; was SkipUpdate, which creeps)
                int steps = Options.FrameLocked ? (frame.Dt > 0f ? 1 : 0) : c.Accumulate(frame.Dt);
                c.Frame(steps, fixedTick);
            }
        }

        /// <summary>
        /// The game's Update-phase reset of every chain, done now (DynamicBoneChain.ResetBeforeUpdate): between two frames the
        /// chain bones still hold the last write-back, which HS2 never shows during its Update phase. Called before the Animator
        /// binds a controller (CharaAnimation.BeforeBind, wired by DynamicBoneSetup.Attach).
        /// </summary>
        public void ResetBeforeUpdate()
        {
            for (int i = 0; i < Chains.Count; i++) Chains[i].ResetBeforeUpdate();
        }

        /// <summary>Drops all history: chains back to the frame's rest pose, particles at the bones, no velocity.</summary>
        public void Reset()
        {
            foreach (var c in Chains) if (c.Active) c.ResetAll();
            fixedAcc = 0f;
        }

        /// <summary>
        /// Re-reads every component's fields and rebuilds the chains from the Start pose (after DBE / Studio edits);
        /// removes chains whose component is gone and, for an attached character, adds components of parts loaded since.
        /// </summary>
        public void Rebuild()
        {
            Chains.RemoveAll(c => c.Source == null);
            Colliders.Clear();
            foreach (var c in Chains) c.Rebuild();
            if (Context != null) DynamicBoneSetup.AddParts(this, Context);
        }

        /// <summary>
        /// Phase 4.3: <see cref="Rebuild()"/> with a step between the restore and the
        /// capture — every chain first returns its bones to the captured rest pose, then <paramref name="afterRestore"/> runs
        /// (the edit's chain step re-applies the hair bundle corrections there), then every chain captures the current pose
        /// without a second restore; components of parts loaded since are added as in Rebuild. Null = Rebuild().
        /// </summary>
        public void Rebuild(System.Action afterRestore)
        {
            if (afterRestore == null) { Rebuild(); return; }
            Chains.RemoveAll(c => c.Source == null);
            Colliders.Clear();
            var was = new bool[Chains.Count];
            for (int i = 0; i < Chains.Count; i++) was[i] = Chains[i].RestoreForRebuild();
            afterRestore();
            for (int i = 0; i < Chains.Count; i++) Chains[i].BuildAfterRestore(was[i]);
            if (Context != null) DynamicBoneSetup.AddParts(this, Context);
        }

        public override string ToString() => $"dynbone: {Chains.Count} chains, {ParticleCount} particles, {Colliders.Count} colliders, {(Options.FrameLocked ? "frame-locked" : "fixed " + Options.DefaultUpdateRate + " Hz")}";
    }
}
