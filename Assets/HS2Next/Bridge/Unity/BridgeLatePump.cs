// The bridge's late point in the frame loop: LateUpdate at execution order
// 32000, after CharaMotion (1000) and the scene's late apply (1100). Runs the actions scheduled with
// IBridgeFrames.AtLate (Core's captures) and completes WaitLate tasks. Disabling it disposes its BridgeHost.
using UnityEngine;

namespace HS2Next.Bridge
{
    [DefaultExecutionOrder(32000)]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class BridgeLatePump : MonoBehaviour
    {
        internal BridgeHost Host;

        void LateUpdate() => Host?.Server.PumpLate(Time.frameCount);

        void OnDisable() => Host?.Dispose();
    }
}
