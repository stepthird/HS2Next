// Execution order of the motion stages of a character (Runtime/Motion/CharaMotion.cs). Part of the stable API
// (assembly HS2Next.Api): plugins add stages at a phase.
namespace HS2Next.Motion
{
    /// <summary>
    /// Execution order of the stages within one step (lower first; equal phases keep registration order).
    /// Follows the deterministic order recommended (L1..L10), with two
    /// deliberate changes documented there and: the expression runs after
    /// the eye look (so an eyelid correction sees the same frame), and the bust/hip chains run after the
    /// joint correction instead of the game's "before everything" slot (removes a one-frame pose lag).
    /// </summary>
    public enum MotionPhase
    {
        /// <summary>Pose sources: animation sampling (Animation/AnimationStage evaluates the character's PlayableGraph with dt x speed).</summary>
        Animation = 0,
        /// <summary>Shape bones on change, hand patterns (Animation/HandShapeStage), per-motion sway toggles (L2).</summary>
        Shape = 100,
        /// <summary>IK target preparation (L3; later phase).</summary>
        IkPrepare = 150,
        /// <summary>Inverse kinematics (own solver; L4, later phase).</summary>
        Ik = 200,
        /// <summary>Neck look (L5).</summary>
        NeckLook = 300,
        /// <summary>Forward kinematics overrides: Studio FK groups override the neck look (L6; later phase).</summary>
        Fk = 400,
        /// <summary>Eye look (L7).</summary>
        EyeLook = 500,
        /// <summary>Expression blend shapes, blink, mouth, tears, cheek (bone-independent, placed after the eye look).</summary>
        Face = 600,
        /// <summary>Joint correction: the *_dam twist bones from the final pose (L8).</summary>
        Correct = 700,
        /// <summary>Bust / hip chains (DynamicBone_Ver02; L9).</summary>
        BustPhysics = 750,
        /// <summary>Dynamic bones of hair / clothes / accessories (L10).</summary>
        DynamicBone = 800,
        /// <summary>Anything that must see the final pose (bone modifiers, measurements).</summary>
        Final = 900,
    }
}
