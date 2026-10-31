// Play-mode / player late driver of a SceneRuntime: LateUpdate → LateApply()
// after CharaMotion (1000) stepped the characters and before captures (32000): world-scale enforcement of attached
// nodes, camera-node follow and the view.
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    [DefaultExecutionOrder(1100)]
    [DisallowMultipleComponent]
    public sealed class SceneRuntimeLateDriver : MonoBehaviour
    {
        public SceneRuntime Runtime;

        void LateUpdate()
        {
            if (Runtime != null && !Runtime.IsDisposed) Runtime.LateApply();
        }
    }
}
