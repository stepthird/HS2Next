// The kinematic bust pose of a character whose physics the bridge switched off (
// `chara.motion {physics: false}`): HS2's bridge copies the reference transforms of every DynamicBone_Ver02 chain into
// the chain bones every Update while physics is off, so the breast and hip chains show the pose of the current sliders
// and no simulation history. This stage does the same at MotionPhase.BustPhysics, registered after BustStage (equal
// phases keep registration order), by calling BustChain.InitTransforms on every chain of BustStage.All; CompareTest's
// StepOnce does it by hand after each step. MotionOverrides adds it when physics goes off and removes it when physics
// comes back; it never changes a chain's Active state or particles.
using HS2Next.Motion;
using HS2Next.Motion.DynamicBones;

namespace HS2Next.App.Commands
{
    public sealed class BustKinematicStage : IMotionStage
    {
        public const string StageName = "bustKinematic";

        public BustKinematicStage(BustStage bust) { Bust = bust; }

        /// <summary>The bust stage whose chains are reset (null: the stage does nothing).</summary>
        public BustStage Bust { get; }
        public MotionPhase Phase => MotionPhase.BustPhysics;
        public string Name => StageName;
        public bool Enabled { get; set; } = true;

        public void Step(in MotionFrame frame) => Apply();

        /// <summary>Writes the reference pose into every chain once (also outside a step, so a switch shows at once).</summary>
        public void Apply()
        {
            if (Bust == null) return;
            var all = Bust.All;
            for (int i = 0; i < all.Count; i++) all[i]?.InitTransforms();
        }

        /// <summary>No history.</summary>
        public void Reset() { }
    }
}
