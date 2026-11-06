// The three stages of a character's driven bones (DrivenBones). They exist only while the character has a driven bone.
//   bones.driven.restore  phase -99  right after pose.restore: the bones back to what the pose left, in every mode. Runs inside a probe as well.
//   bones.driven          phase 440  after FK, before pose.apply: a correction stays on top of a driven value
//   bones.driven.end      phase 955  after pose.end, before bones.modifiers.end: what the frame shows
// The phases are casts: the numbers lie between the enum's names.
namespace HS2Next.Motion.AdvancedPose
{
    public sealed class DrivenRestoreStage : IMotionStage
    {
        readonly DrivenBones set;
        public DrivenRestoreStage(DrivenBones set) { this.set = set; }
        public MotionPhase Phase => (MotionPhase)(-99);
        public string Name => "bones.driven.restore";
        public bool Enabled { get; set; } = true;
        public void Step(in MotionFrame frame) => set.Restore();
        public void Reset() { }
    }

    public sealed class DrivenApplyStage : IMotionStage
    {
        readonly DrivenBones set;
        public DrivenApplyStage(DrivenBones set) { this.set = set; }
        public MotionPhase Phase => (MotionPhase)440;
        public string Name => "bones.driven";
        public bool Enabled { get; set; } = true;
        public void Step(in MotionFrame frame) => set.Apply(frame);
        public void Reset() { }
    }

    public sealed class DrivenEndStage : IMotionStage
    {
        readonly DrivenBones set;
        public DrivenEndStage(DrivenBones set) { this.set = set; }
        public MotionPhase Phase => (MotionPhase)955;
        public string Name => "bones.driven.end";
        public bool Enabled { get; set; } = true;
        public void Step(in MotionFrame frame) => set.End();
        public void Reset() { }
    }
}
