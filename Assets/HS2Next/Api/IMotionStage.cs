// One motion stage of a character (Runtime/Motion/CharaMotion.cs steps them in MotionPhase order). Part of the
// stable API (assembly HS2Next.Api).
namespace HS2Next.Motion
{
    /// <summary>One motion stage. Implementations are plain classes (not MonoBehaviours) owned by the character's CharaMotion.</summary>
    public interface IMotionStage
    {
        /// <summary>Where the stage runs within a step.</summary>
        MotionPhase Phase { get; }
        /// <summary>Short name for logs ("dynbone", "face", ...).</summary>
        string Name { get; }
        /// <summary>Disabled stages are skipped (state is kept).</summary>
        bool Enabled { get; set; }
        /// <summary>Advance by one frame. Called in phase order after the Animator has written the pose.</summary>
        void Step(in MotionFrame frame);
        /// <summary>Drop all history (teleport, re-enable, pose jump): the next Step must start from the current transforms without any inertia.</summary>
        void Reset();
    }
}
