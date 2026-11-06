// The motion pass of a scene runtime: while a pass is active the scene steps its characters itself, in dependency order, instead of
// each character stepping itself in its own LateUpdate. IMotionPass is what the runtime calls, once per frame and once per scene step;
// MotionPassDriver is the real frame's caller: LateUpdate at order 1010, after every CharaMotion (1000, which return at once while
// Scheduled) and before the items (1050). The component exists only while a pass is active (SceneRuntime.SetPass).
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>One walk over the units of a frame. The implementer steps each character through <see cref="SceneRuntime.StepInPass"/>.</summary>
    public interface IMotionPass
    {
        /// <summary>
        /// Walks the units once. <paramref name="dt"/> null: a real frame, each character takes its own delta (<c>StepAuto</c>); a value:
        /// every character steps by it. <paramref name="all"/> true steps characters with AutoStep off as well (manual stepping).
        /// </summary>
        void Run(float? dt, bool all);
    }

    [DefaultExecutionOrder(1010)]
    [DisallowMultipleComponent]
    public sealed class MotionPassDriver : MonoBehaviour
    {
        public SceneRuntime Runtime;

        void LateUpdate()
        {
            if (Runtime != null && !Runtime.IsDisposed && !Runtime.Settings.ManualStepping) Runtime.RunPass(null, false);
        }
    }
}
