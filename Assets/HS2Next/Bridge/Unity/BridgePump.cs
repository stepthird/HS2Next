// The bridge pump in the frame loop: Update at execution order −10000,
// before the scene's apply (−5000) and everything else. Publishes the clock and screen size for ping, then runs
// queued requests (30 ms budget for new queue entries). Disabling or destroying it disposes its BridgeHost.
using UnityEngine;

namespace HS2Next.Bridge
{
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class BridgePump : MonoBehaviour
    {
        internal BridgeHost Host;

        void Update()
        {
            var h = Host;
            if (h == null) return;
            h.Server.SetHostInfo(Time.realtimeSinceStartupAsDouble, Screen.width, Screen.height);
            h.Server.Pump(Time.frameCount);
        }

        void OnDisable() => Host?.Dispose();
        void OnDestroy() => Host?.Dispose();
    }
}
