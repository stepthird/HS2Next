// The scene values of a character's physics chains (advanced pose): every member optional, null = the chain's own value stays.
// A chain reads them in front of the values it holds from the part and the card; nothing writes those. Removing a member, or the
// whole object, gives the card's value back at once.
using UnityEngine;

namespace HS2Next.Motion.AdvancedPose
{
    /// <summary>The scene values of one swing chain (DynamicBoneChain.Override).</summary>
    public sealed class ChainOverride
    {
        /// <summary>False stops the chain and its bones return to the rest pose; true does not override a card's no-shake.</summary>
        public bool? Enabled;
        public float? Weight, Damping, Elasticity, Stiffness, Inert, Radius;
        public Vector3? Gravity, Force;
        /// <summary>0 none, 1 x, 2 y, 3 z.</summary>
        public int? FreezeAxis;

        public bool IsEmpty => !Enabled.HasValue && !Weight.HasValue && !Damping.HasValue && !Elasticity.HasValue && !Stiffness.HasValue && !Inert.HasValue &&
                               !Radius.HasValue && !Gravity.HasValue && !Force.HasValue && !FreezeAxis.HasValue;
    }

    /// <summary>The scene values of one bust or hip chain (BustChain.Override).</summary>
    public sealed class BustOverride
    {
        /// <summary>False turns the chain off; true never overrides the FK breast or the animation switches.</summary>
        public bool? Enabled;
        public Vector3? Gravity, Force;
        public float? Damping, Elasticity, Stiffness, Inert;

        /// <summary>A member that lives in the chain's per-particle arrays is set.</summary>
        public bool HasSoft => Damping.HasValue || Elasticity.HasValue || Stiffness.HasValue || Inert.HasValue;
        public bool IsEmpty => !Enabled.HasValue && !Gravity.HasValue && !Force.HasValue && !HasSoft;
    }
}
