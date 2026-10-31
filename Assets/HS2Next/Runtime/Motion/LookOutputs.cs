// Shared contract between the eye-look stage (Look/) and the expression stage (Face/):
// the eye-look stage defines the values, the expression stage consumes them for the
// eyelid correction (off by default in HS2Next, as in the game). The expression stage finds the
// provider with ctx.Motion.GetAll<IMotionStage>() and a type test for IEyeLookOutputs.
namespace HS2Next.Motion
{
    public interface IEyeLookOutputs
    {
        /// <summary>False until the first computed eye-look frame; all values are 0 until then.</summary>
        bool HasOutput { get; }
        /// <summary>Smoothed yaw of eye e (0 left, 1 right) in degrees, eye-parent frame; positive = towards the character's right.</summary>
        float AngleH(int eye);
        /// <summary>Smoothed pitch of eye e in degrees; positive = DOWN, negative = up.</summary>
        float AngleV(int eye);
        /// <summary>Normalised yaw of eye e in -1..1 (+1 = full right) from the current pattern's bending limits.</summary>
        float AngleHRate(int eye);
        /// <summary>Normalised pitch from eye 0 in -1..1: -1 = full down, +1 = full up.</summary>
        float AngleVRate { get; }
    }
}
