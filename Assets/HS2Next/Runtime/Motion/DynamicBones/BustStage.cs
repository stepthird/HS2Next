// Motion stage for the four DynamicBone_Ver02 chains Mune_L, Mune_R, Siri_L, Siri_R on p_cf_anim.
// Runs at MotionPhase.BustPhysics, i.e. after the Animator and the shapes,
// before FK / IK / twist correction. Card bust softness / weight -> breast parameters,
// ResetPosition after shape changes (breasts only, hips keep their bind-pose rest offsets), the Studio
// switches (FK Breast group, the H-animation shake flags) and the male path.
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Motion.DynamicBones
{
    public sealed class BustStage : IMotionStage
    {
        public MotionPhase Phase => MotionPhase.BustPhysics;
        public string Name => "bust";

        public readonly BustOptions Options;
        /// <summary>By ChaControlDefine.DynamicBoneKind: 0 BreastL, 1 BreastR, 2 HipL, 3 HipR (null when missing).</summary>
        public readonly BustChain[] Chains = new BustChain[4];
        /// <summary>Every DynamicBone_Ver02 under the skeleton, in component order.</summary>
        public readonly List<BustChain> All = new List<BustChain>();
        public readonly bool IsMale;

        /// <summary>Card values: fileBody.bustSoftness, fileBody.bustWeight, shapeValueBody[1] (BustSize).</summary>
        public float BustSoftness = 0.5f, BustWeight = 0.5f, BustSize = 0.5f;

        // ---- Studio switches; all true / inactive initially
        /// <summary>FK group Breast active: both breast chains off.</summary>
        public bool FkBreastActive;
        /// <summary>OCIChar.DynamicAnimeBustL/R (H-animation shake table).</summary>
        public bool AnimeBustL = true, AnimeBustR = true;
        /// <summary>Hip chains (H-animation shake table; no FK interlock).</summary>
        public bool HipL = true, HipR = true;

        bool enabled = true;

        /// <param name="anim">The body skeleton (p_cf_anim instance).</param>
        /// <param name="bindPose">The p_cf_anim prefab (for the hips' Awake rest offsets); null = current pose.</param>
        public BustStage(GameObject anim, BustOptions options, bool male, GameObject bindPose)
        {
            Options = options ?? new BustOptions();
            IsMale = male;
            if (!anim) return;
            var prefabChains = bindPose ? bindPose.GetComponentsInChildren<global::DynamicBone_Ver02>(true) : null;
            foreach (var v in anim.GetComponentsInChildren<global::DynamicBone_Ver02>(true))
            {
                var c = new BustChain(v, Options);
                All.Add(c);
                if (c.Kind >= 0 && Chains[c.Kind] == null) Chains[c.Kind] = c;
                if (c.Kind >= 2 && Options.HipRestFromBindPose && prefabChains != null)
                    foreach (var pv in prefabChains)
                        if (pv.Comment == v.Comment) { c.CaptureLocalPositionFrom(pv); break; }
            }
        }

        public bool Enabled
        {
            get => enabled;
            set
            {
                if (enabled == value) return;
                enabled = value;
                if (!value) foreach (var c in All) c.SetActive(false);
            }
        }

        // ------------------------------------------------------------
        /// <summary>BustSoft.ReCalc: v = clamp01(softness · size + 0.01), three-point interpolation.</summary>
        public static void BustSoftParams(float softness, float size, out float damping, out float elasticity, out float stiffness)
        {
            float v = Mathf.Clamp01(softness * size + 0.01f);
            stiffness = Tri(1.0f, 0.1f, 0.01f, v);
            elasticity = Tri(0.2f, 0.15f, 0.05f, v);
            damping = Tri(0.2f, 0.1f, 0.1f, v);
        }

        /// <summary>
        /// Phase 4.2 (Y10): the gravity lerp of a weight outside 0..1 — false (default) =
        /// HS2's code, `Mathf.Lerp` (the weight is clamped in effect); true = extrapolated (`LerpUnclamped`, the rule before
        /// 4.2, kept for an install whose slider extender patches this lerp, HB2).
        /// </summary>
        public static bool ExtrapolateGravity = false;

        /// <summary>BustGravity.ReCalc: y = lerp(0, −0.05, weight) · size · softness · 0.5 (lerp clamped unless
        /// <see cref="ExtrapolateGravity"/>; softness and size enter unclamped).</summary>
        public static float BustGravityY(float softness, float weight, float size) =>
            (ExtrapolateGravity ? Mathf.LerpUnclamped(0f, -0.05f, weight) : Mathf.Lerp(0f, -0.05f, weight)) * (size * softness * 0.5f);

        static float Tri(float a0, float a1, float a2, float v) => v < 0.5f ? Mathf.LerpUnclamped(a0, a1, 2f * v) : Mathf.LerpUnclamped(a1, a2, 2f * v - 1f);

        /// <summary>Sets the card values and applies them (UpdateBustSoftness then UpdateBustGravity).</summary>
        public void SetCardValues(float softness, float weight, float size)
        {
            BustSoftness = softness; BustWeight = weight; BustSize = size;
            ApplyBustSoftnessAndGravity();
        }

        /// <summary>on BreastL / BreastR: pattern 0 and the live particles (all particles get the same D / E / S).</summary>
        public void ApplyBustSoftnessAndGravity()
        {
            if (!Options.ApplyCardValues) return;
            BustSoftParams(BustSoftness, BustSize, out float d, out float e, out float s);
            float gy = BustGravityY(BustSoftness, BustWeight, BustSize);
            for (int k = 0; k < 2; k++)
            {
                var c = Chains[k];
                if (c == null) continue;
                c.SetSoftParams(0, -1, d, e, s);
                c.SetGravity(0, new Vector3(0f, gy, 0f));
            }
        }

        /// <summary>ReSetupDynamicBoneBust + UpdateBustSoftnessAndGravity: call after body-shape changes (breasts only).</summary>
        public void ResetPosition()
        {
            for (int k = 0; k < 2; k++) Chains[k]?.ResetPosition();
            ApplyBustSoftnessAndGravity();
        }

        /// <summary>Re-derives the chains from the components' fields (as Awake), then re-applies the card values.</summary>
        public void Rebuild()
        {
            foreach (var c in All) c.Build();
            ResetPosition();
        }

        // ------------------------------------------------------------ switches
        public bool WantActive(BustChain c)
        {
            if (!enabled || c.Source == null || !c.Source.enabled || !c.Source.gameObject.activeInHierarchy) return false;
            if (IsMale && !Options.MaleBustPhysics) return false;
            if (c.Override?.Enabled == false) return false;   // a scene value turns a chain off; true never overrides the switches below
            switch (c.Kind)
            {
                case 0: return AnimeBustL && !FkBreastActive;
                case 1: return AnimeBustR && !FkBreastActive;
                case 2: return HipL;
                case 3: return HipR;
                default: return true;
            }
        }

        public void ApplySwitches() { foreach (var c in All) c.SetActive(WantActive(c)); }

        /// <summary>
        /// The four enables an H animation's sway table sets (the breast chains through their animation switch, the hip chains
        /// directly), applied to the chains at once when one changed. False when all four were already set so.
        /// </summary>
        public bool SetAnimeSwitches(bool bustL, bool bustR, bool hipL, bool hipR)
        {
            if (AnimeBustL == bustL && AnimeBustR == bustR && HipL == hipL && HipR == hipR) return false;
            AnimeBustL = bustL; AnimeBustR = bustR; HipL = hipL; HipR = hipR;
            ApplySwitches();
            return true;
        }

        // ------------------------------------------------------------ IMotionStage
        public void Step(in MotionFrame frame)
        {
            for (int i = 0; i < All.Count; i++)
            {
                var c = All[i];
                c.SetActive(WantActive(c));
                if (c.Active) c.Frame(frame.Dt);
            }
        }

        /// <summary>Drops all history: chains at the reference pose, particles at the bones (ResetDynamicBoneAll).</summary>
        public void Reset()
        {
            foreach (var c in All)
            {
                if (!c.Active) continue;
                c.InitTransforms();
                c.ResetParticlesPosition();
            }
        }

        public override string ToString() => $"bust: {All.Count} Ver02, softness {BustSoftness:0.###} weight {BustWeight:0.###} size {BustSize:0.###}{(IsMale ? ", male" + (Options.MaleBustPhysics ? " (chains on)" : " (chains off)") : "")}";
    }
}
