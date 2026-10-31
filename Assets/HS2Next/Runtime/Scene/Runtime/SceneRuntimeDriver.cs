// Play-mode / player driver of a SceneRuntime: Update → BeginFrame(Time.deltaTime)
// (the scene clock's frame; not with ManualStepping, where Tick begins it) and Apply()
// early in the frame (after the bridge pump at -10000, before ordinary scripts and before CharaMotion's LateUpdate at
// 1000). Editor batch tests do not add it; they call Apply / Tick by hand.
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    [DefaultExecutionOrder(-5000)]
    [DisallowMultipleComponent]
    public sealed class SceneRuntimeDriver : MonoBehaviour
    {
        public SceneRuntime Runtime;

        void Update()
        {
            if (Runtime == null || Runtime.IsDisposed) return;
            if (!Runtime.Settings.ManualStepping) Runtime.BeginFrame(Time.deltaTime);
            Runtime.Apply();
        }
    }
}
