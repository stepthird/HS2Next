// Time information handed to every motion stage (Runtime/Motion/CharaMotion.cs). Part of the stable API
// (assembly HS2Next.Api).
namespace HS2Next.Motion
{
    /// <summary>Time information for one step.</summary>
    public readonly struct MotionFrame
    {
        /// <summary>Scaled step length in seconds (0 when paused; stages must then hold their state).</summary>
        public readonly float Dt;
        /// <summary>Unscaled step length in seconds (for effects that keep running while paused, e.g. blink while the scene is paused is NOT one of them).</summary>
        public readonly float UnscaledDt;
        /// <summary>Accumulated scaled time of this character in seconds.</summary>
        public readonly float Time;
        /// <summary>Step counter (first step after a reset is 0).</summary>
        public readonly int Index;
        public bool Paused => Dt <= 0f;
        public MotionFrame(float dt, float unscaledDt, float time, int index) { Dt = dt; UnscaledDt = unscaledDt; Time = time; Index = index; }
    }
}
