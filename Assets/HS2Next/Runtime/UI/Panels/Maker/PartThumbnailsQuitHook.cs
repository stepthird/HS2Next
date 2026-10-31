// Quit hook of the Maker part thumbnails (Phase 4.2 package L): a hidden object made in
// play mode by the first PartThumbnails. Unity calls OnApplicationQuit and OnDisable before the shutdown — also when a batch
// run ends play mode with EditorApplication.Exit — so the thumbnails' in-flight AssetBundleRequests and bundle opens are
// completed, their disk loaders' UnityWebRequests disposed and loose reads cancelled first (an exit with thumbnail loads in
// flight hung at 0 % CPU after "CodeReloadManager destroyed").
using UnityEngine;

namespace HS2Next.UI.Panels.Maker
{
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class PartThumbnailsQuitHook : MonoBehaviour
    {
        static PartThumbnailsQuitHook instance;

        /// <summary>Makes the hook once per play session (no-op outside play mode).</summary>
        internal static void Ensure()
        {
            if (instance != null || !Application.isPlaying) return;
            var go = new GameObject("HS2Next part thumbnails (quit hook)") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<PartThumbnailsQuitHook>();
        }

        void OnApplicationQuit() { if (PartThumbnails.ReleaseOnQuit) PartThumbnails.ReleaseAllForQuit("OnApplicationQuit"); }
        void OnDisable() { if (PartThumbnails.ReleaseOnQuit) PartThumbnails.ReleaseAllForQuit("OnDisable"); }
        void OnDestroy() { if (instance == this) instance = null; }
    }
}
