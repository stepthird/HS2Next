// The host MonoBehaviour of the HS2Next app: one per play session or player, on the
// "HS2Next App" object of the start scene (AppBootstrap.AutoStart; the player's app scene of Phase 2.5 holds none). Start
// creates the AppCore and runs its Boot coroutine; the runtime root "HS2Next Scene" and the bridge's pump components
// live on this object. OnDestroy (and Application.quitting, through AppCore) disposes the app: bridge, runtime,
// bundles, the Editor's render-pipeline switch. A second host destroys itself.
using UnityEngine;

namespace HS2Next.App
{
    [DisallowMultipleComponent]
    public sealed class HS2NextApp : MonoBehaviour
    {
        public const string ObjectName = "HS2Next App";

        /// <summary>The running host, or null.</summary>
        public static HS2NextApp Instance { get; private set; }

        /// <summary>The app core (null before Start).</summary>
        public AppCore Core { get; private set; }

        AppSettings settings;
        string settingsError;

        internal static void ResetStatics() => Instance = null;

        /// <summary>Settings read by AppBootstrap (null = read them in the settings stage).</summary>
        internal void Init(AppSettings s, string error)
        {
            settings = s;
            settingsError = error;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[app] a second " + ObjectName + " was destroyed");
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void Start()
        {
            if (Instance != this) return;
            Core = AppCore.Create(transform, settings, settingsError);
            StartCoroutine(Core.Boot());
        }

        void OnDestroy()
        {
            Core?.Dispose();
            if (Instance == this) Instance = null;
        }
    }
}
