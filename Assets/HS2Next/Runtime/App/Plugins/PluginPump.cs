// The plugin pump: once per frame in play mode and players, after the bridge pump (execution order -10000) and before the
// scene's apply (-5000), it runs every loaded plugin's Update with this frame's scene delta (0 while the scene is paused),
// so writes a plugin makes inline are realized by the same frame's apply and motion. Lives on the app's host object.
using HS2Next.App;
using UnityEngine;

namespace HS2Next.App.Plugins
{
    [DefaultExecutionOrder(-7500)]
    [DisallowMultipleComponent]
    public sealed class PluginPump : MonoBehaviour
    {
        PluginHost host;

        internal void Init(PluginHost h) => host = h;

        void Update()
        {
            var h = host;
            if (h == null || h.IsShutDown) return;
            var rt = h.App?.Runtime;
            bool paused = rt != null && rt.Paused;
            h.UpdateFrame(Time.frameCount, Time.unscaledDeltaTime, paused ? 0f : Time.deltaTime, rt?.Clock.Time ?? 0, paused);
        }
    }
}
