// The real frame's caller of the scene post stage: LateUpdate at order 1075, after every character, the motion pass (1010) and the items
// (1050), before SceneRuntimeLateDriver (1100: world-scale enforcement, the camera, the depth of field). The component exists only while something
// wants the stage (SceneRuntime.PostStage). With ManualStepping the runtime's Tick calls it instead.
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    [DefaultExecutionOrder(1075)]
    [DisallowMultipleComponent]
    public sealed class ScenePostDriver : MonoBehaviour
    {
        public SceneRuntime Runtime;

        void LateUpdate()
        {
            if (Runtime != null && !Runtime.IsDisposed && !Runtime.Settings.ManualStepping) Runtime.RunPost(Runtime.Clock.FrameDt);
        }
    }
}
