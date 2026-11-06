// The frame recorder's own late point, for an app that runs without the bridge (bridge.enabled false: no bridge frame clock).
// A hidden component whose LateUpdate runs at execution order 32000 - the place of the bridge's late point in the frame,
// after the characters' motion (1000), the items (1050) and the scene's late apply (1100) - so a recorded frame is captured
// at the same point of the frame with or without the bridge. One per process, created when a recording starts without the
// bridge, kept across scene loads; Late is raised with Time.frameCount. Play mode and the player only.
using System;
using UnityEngine;

namespace HS2Next.Screenshots
{
    [DefaultExecutionOrder(32000)]
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class FrameRecorderDriver : MonoBehaviour
    {
        static FrameRecorderDriver instance;

        /// <summary>Raised at every late point (main thread) with the frame number.</summary>
        public static event Action<int> Late;

        /// <summary>Creates the driver when it does not exist; false outside play mode (no frame loop to drive).</summary>
        public static bool Ensure()
        {
            if (instance) return true;
            if (!Application.isPlaying) return false;
            var go = new GameObject("HS2Next frame recorder driver") { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<FrameRecorderDriver>();
            return true;
        }

        void LateUpdate()
        {
            var h = Late;
            if (h == null) return;
            int frame = Time.frameCount;
            foreach (Action<int> d in h.GetInvocationList())
            {
                try { d(frame); }
                catch (Exception e) { Debug.LogWarning("[record] a late point handler threw: " + e); }
            }
        }
    }
}
