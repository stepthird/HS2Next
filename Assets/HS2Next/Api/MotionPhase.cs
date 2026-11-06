// Execution order of the motion stages of a character (Runtime/Motion/CharaMotion.cs). Part of the stable API
// (assembly HS2Next.Api): plugins add stages at a phase.
namespace HS2Next.Motion
{
    /// <summary>
    /// Execution order of the stages within one step (lower first; equal phases keep registration order): pose sources,
    /// shapes, IK, neck look, FK, eye look, expression, joint correction, bust and hip chains, dynamic bones, final. The
    /// expression runs after the eye look (so an eyelid correction sees the same frame), and the bust / hip chains run after
    /// the joint correction (so the pose has no one-frame lag).
    /// </summary>
    public enum MotionPhase
    {
        /// <summary>Pose sources: animation sampling (the character's playable graph evaluated with dt × speed).</summary>
        Animation = 0,
        /// <summary>Shape bones on change, hand patterns, per-motion sway toggles.</summary>
        Shape = 100,
        /// <summary>IK target preparation.</summary>
        IkPrepare = 150,
        /// <summary>Inverse kinematics.</summary>
        Ik = 200,
        /// <summary>Neck look.</summary>
        NeckLook = 300,
        /// <summary>Forward kinematics overrides: Studio FK groups override the neck look.</summary>
        Fk = 400,
        /// <summary>Eye look.</summary>
        EyeLook = 500,
        /// <summary>Expression blend shapes, blink, mouth, tears, cheek (bone-independent, placed after the eye look).</summary>
        Face = 600,
        /// <summary>Joint correction: the twist bones from the final pose.</summary>
        Correct = 700,
        /// <summary>Bust / hip chains.</summary>
        BustPhysics = 750,
        /// <summary>Dynamic bones of hair / clothes / accessories.</summary>
        DynamicBone = 800,
        /// <summary>Anything that must see the final pose (measurements).</summary>
        Final = 900,
    }
}
