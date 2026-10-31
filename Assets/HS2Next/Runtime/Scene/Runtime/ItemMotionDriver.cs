// Play-mode / player item stepping of a SceneRuntime (T16): LateUpdate at order 1050 —
// after CharaMotion (1000), so an item in a hand sees the final hand pose of the same frame, and before
// SceneRuntimeLateDriver (1100: world-scale enforcement, camera follow, view) — steps every built item motion in model
// pre-order with the scene clock's FrameDt (SceneRuntime.StepItems; 0 while the scene is paused
// 2). With ManualStepping the runtime's Tick steps the items instead.
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    [DefaultExecutionOrder(1050)]
    [DisallowMultipleComponent]
    public sealed class ItemMotionDriver : MonoBehaviour
    {
        public SceneRuntime Runtime;

        void LateUpdate()
        {
            if (Runtime == null || Runtime.IsDisposed || Runtime.Settings.ManualStepping) return;
            Runtime.StepItems(Runtime.Clock.FrameDt);
        }
    }
}
