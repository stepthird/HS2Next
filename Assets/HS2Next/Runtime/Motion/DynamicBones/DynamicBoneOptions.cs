// Switches of the dynamic-bone simulation.
// Defaults follow the spec's recommendations: fixed 60 Hz stepping (cap 3) for DynamicBone, the
// IllusionFixes chain construction and Ver02 "hold" on frames without a step, the job-path collider
// behaviour of the shipped game (legacy sphere bug, disabled colliders as zero records, no FreezeAxis,
// no plane colliders), faithful AnimatePhysics resets, male bust chains running.
namespace HS2Next.Motion.DynamicBones
{
    /// <summary>which chain-construction routine to use.</summary>
    public enum ChainVariant
    {
        /// <summary>IllusionFixes HS2_Fix_DynamicBones (the user's install; recommended).</summary>
        IllusionFixes,
        /// <summary>Vanilla Assembly-CSharp (one tip per skipped child, notRoll one level deep).</summary>
        Vanilla,
    }

    /// <summary>Options of the ordinary DynamicBone chains (hair, clothes, accessories).</summary>
    public sealed class DynamicBoneOptions
    {
        /// <summary>true: one step per rendered frame (HS2 parity; motion speed scales with fps).
        /// false (default): the asset's intended accumulator at m_UpdateRate (60) with at most MaxStepsPerFrame steps.</summary>
        public bool FrameLocked = false;
        /// <summary>Step cap per frame in fixed-rate mode (the asset's 3).</summary>
        public int MaxStepsPerFrame = 3;
        /// <summary>Rate used when a component's m_UpdateRate is not positive (fixed-rate mode).</summary>
        public float DefaultUpdateRate = 60f;
        /// <summary></summary>
        public ChainVariant Variant = ChainVariant.IllusionFixes;
        /// <summary>"intended behaviour": FreezeAxis projection, plane colliders, correct sphere centres, disabled
        /// colliders skipped. false (default) = what HS2 actually runs (job path).</summary>
        public bool IntendedBehaviour = false;
        /// <summary>job-path sphere colliders centred at R·S·m_Center from the world origin (only without IntendedBehaviour).</summary>
        public bool LegacySphereBug = true;
        /// <summary>m_UpdateMode 1 (AnimatePhysics) chains reset their bones only on frames with a fixed step.</summary>
        public bool FaithfulAnimatePhysics = true;
        /// <summary>Fixed step used for the AnimatePhysics reset emulation (Unity default 0.02 s).</summary>
        public float FixedDeltaTime = 0.02f;
        /// <summary>honour m_DistantDisable (reference null -> Camera.main; no camera -> no check).</summary>
        public bool DistantDisable = true;
        /// <summary>Performance: chains whose particle bones are direct parent/child compose world matrices from local poses
        /// and write local poses (same result up to float rounding); false = world-space transform access throughout.</summary>
        public bool LocalWriteBack = true;
        /// <summary>Performance: per chain and step, skip colliders no particle can reach (exact; false = test every collider).</summary>
        public bool ColliderBroadPhase = true;

        public DynamicBoneOptions Clone() => (DynamicBoneOptions)MemberwiseClone();
    }

    /// <summary>Options of the bust / hip DynamicBone_Ver02 chains.</summary>
    public sealed class BustOptions
    {
        /// <summary>true (default): IllusionFixes, frames without a step keep the particles.
        /// false: vanilla skip update (object motion carried, stiffness cone, keep-length window, move limit).</summary>
        public bool HoldOnSkipFrames = true;
        /// <summary>true (default, faithful): the four chains keep running on males, only the
        /// CmpBoneBody.dbcBust colliders are off. false: male chains disabled (the evident intent).</summary>
        public bool MaleBustPhysics = true;
        /// <summary>the hip chains keep the rest offsets captured at Awake from the bind pose (the game never
        /// re-sets them up). true (default): taken from the p_cf_anim prefab; false: from the pose at attach.</summary>
        public bool HipRestFromBindPose = true;
        /// <summary>apply the card's bust softness / weight to the breast chains.</summary>
        public bool ApplyCardValues = true;

        public BustOptions Clone() => (BustOptions)MemberwiseClone();
    }
}
