// The three stages of a character with a ragdoll: a second set of driven bones, written by the body (DrivenBones, Runtime/Scene/Runtime/Ragdoll). They exist only while the
// character has a body.
//   ragdoll.restore  phase -98  after the driven bones' restore: the reverse of the writes, -100 takes off 450, -99 takes off 440, -98 takes off 410
//   ragdoll          phase 410  after FK, before the driven bones: reads what the pose sources left, then writes the body's bones
//   ragdoll.end      phase 956  after the driven bones' end, before the modifiers' end: what the frame shows
// The phases are casts: the numbers lie between the enum's names. A stage asks its body for the set in every step: a rebuild gives the body a new set on a new motion.
namespace HS2Next.Motion.AdvancedPose
{
    /// <summary>What the stages of a ragdoll work on.</summary>
    public interface IRagdollStages
    {
        /// <summary>The body's set of driven bones.</summary>
        DrivenBones Set { get; }
        /// <summary>The set's writes come off, and what finds its own write by the bits is told what the bones hold.</summary>
        void RestoreBones();
        /// <summary>After the set wrote: what must see the bones as written (a jump's reset of the chains).</summary>
        void AfterWrite();
        /// <summary>Reads the pose sources as they stand at the stage, before the set writes.</summary>
        void ReadTargets(in MotionFrame frame);
        /// <summary>The next read starts from the pose as it stands, with no speed.</summary>
        void DropTargets();
    }

    public sealed class RagdollRestoreStage : IMotionStage
    {
        readonly IRagdollStages body;
        public RagdollRestoreStage(IRagdollStages body) { this.body = body; }
        public MotionPhase Phase => (MotionPhase)(-98);
        public string Name => "ragdoll.restore";
        public bool Enabled { get; set; } = true;
        public void Step(in MotionFrame frame) => body.RestoreBones();
        public void Reset() { }
    }

    public sealed class RagdollApplyStage : IMotionStage
    {
        readonly IRagdollStages body;
        public RagdollApplyStage(IRagdollStages body) { this.body = body; }
        public MotionPhase Phase => (MotionPhase)410;
        public string Name => "ragdoll";
        public bool Enabled { get; set; } = true;
        public void Step(in MotionFrame frame)
        {
            body.ReadTargets(frame);
            body.Set?.Apply(frame);
            body.AfterWrite();
        }
        public void Reset() => body.DropTargets();
    }

    public sealed class RagdollEndStage : IMotionStage
    {
        readonly IRagdollStages body;
        public RagdollEndStage(IRagdollStages body) { this.body = body; }
        public MotionPhase Phase => (MotionPhase)956;
        public string Name => "ragdoll.end";
        public bool Enabled { get; set; } = true;
        public void Step(in MotionFrame frame) => body.Set?.End();
        public void Reset() { }
    }
}
