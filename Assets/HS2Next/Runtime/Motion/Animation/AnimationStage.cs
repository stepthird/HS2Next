// Stage A of the character's motion step:
// samples the loaded motion with dt x speed, restores the shape bones, and runs the follow-ups that the game
// does in LateUpdate order 0. The work is done
// by the character's CharaAnimation; this class only places it at MotionPhase.Animation (= first).
//
// The stage evaluates in BOTH edit mode and play mode (decision 1): the engines never let Unity advance
// the controller on its own (AnimationDriver.cs), so pause, per-character speed and "set time while paused"
// are explicit, and CharaMotion.Step(dt) in tests drives exactly what LateUpdate drives in play mode.
// frame.Dt already carries CharaMotion's TimeScale / Paused / MaxStep; CharaAnimation.Speed multiplies it.
namespace HS2Next.Motion.Animation
{
    public sealed class AnimationStage : IMotionStage
    {
        public MotionPhase Phase => MotionPhase.Animation;
        public string Name => "anim";
        /// <summary>false: nothing is sampled; the bones keep whatever was last written (no rest pose restore).</summary>
        public bool Enabled { get; set; } = true;
        public readonly CharaAnimation Owner;

        public AnimationStage(CharaAnimation owner) { Owner = owner; }

        public void Step(in MotionFrame frame)
        {
            if (Owner) Owner.Tick(frame);
        }

        /// <summary>Animation has no inertia: nothing to drop.</summary>
        public void Reset() { }
    }
}
