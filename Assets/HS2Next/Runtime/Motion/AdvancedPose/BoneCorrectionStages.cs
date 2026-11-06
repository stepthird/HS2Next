// The three stages of a character's bone corrections (BoneCorrections). They exist only while the character has a correction.
//   pose.restore  phase -100  first in a step: the bones back to what the pose sources left, in every mode. Runs inside a probe as well.
//   pose.apply    phase 450   after FK (and any stage up to the eye look): the corrections on the pose of this frame
//   pose.end      phase 950   after everything that writes bones: what the frame shows
// The phases are casts: the numbers lie between the enum's names.
using UnityEngine;

namespace HS2Next.Motion.AdvancedPose
{
    public sealed class BoneRestoreStage : IMotionStage
    {
        readonly BoneCorrections set;
        public BoneRestoreStage(BoneCorrections set) { this.set = set; }
        public MotionPhase Phase => (MotionPhase)(-100);
        public string Name => "pose.restore";
        public bool Enabled { get; set; } = true;
        public void Step(in MotionFrame frame) => set.Restore();
        public void Reset() { }
    }

    public sealed class BoneApplyStage : IMotionStage
    {
        readonly BoneCorrections set;
        public BoneApplyStage(BoneCorrections set) { this.set = set; }
        public MotionPhase Phase => (MotionPhase)450;
        public string Name => "pose.apply";
        public bool Enabled { get; set; } = true;
        public void Step(in MotionFrame frame) => set.Apply();
        public void Reset() { }
    }

    public sealed class BoneEndStage : IMotionStage
    {
        readonly BoneCorrections set;
        public BoneEndStage(BoneCorrections set) { this.set = set; }
        public MotionPhase Phase => (MotionPhase)950;
        public string Name => "pose.end";
        public bool Enabled { get; set; } = true;
        public void Step(in MotionFrame frame) => set.End();
        public void Reset() { }
    }
}
