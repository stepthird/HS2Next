// The app starts by itself (open point B1): after the first scene of every play session and
// every player has loaded, AutoStart creates one "HS2Next App" object with HS2NextApp in the active scene, unless
// -noApp or -smoke is on the command line, the setting app.autoStart is false, or the Editor launch override of the
// play-mode harness says so (PlayModeHarness stores {"app":{"autoStart":false}} by default, so existing play tests
// run without the app; BridgeTest and BridgeServe pass an override with autoStart true). The object is not
// DontDestroyOnLoad: the map loader needs the start scene as the runtime's own scene. An Editor batch play session
// without a launch override (a test entering play mode by itself) gets no app either. When the settings cannot
// be read the host still starts with default app and bridge settings, so the bridge reports the failed settings stage.
// In a player, -bridgeAccept <outDir> also creates the acceptance driver (PlayerAcceptance, Phase 2.5) first.
// The player's app scene (Assets/HS2Next/Scenes/App.unity, written by Editor/PlayerBuild.cs) is empty: this is its one
// start path, as in Editor play mode.
// ResetStatics clears the statics of Runtime/App for play sessions without a domain reload.
using System;
using UnityEngine;

namespace HS2Next.App
{
    /// <summary>The Editor launch override of a play session.</summary>
    public static class AppLaunch
    {
        /// <summary>The SessionState key PlayModeHarness stores the override JSON under.</summary>
        public const string SessionKey = "HS2Next.App.Launch";
        /// <summary>The harness default: no app.</summary>
        public const string Off = "{\"app\":{\"autoStart\":false}}";

        /// <summary>The override of this Editor play session, or null (interactive play, players).</summary>
        public static string Read()
        {
#if UNITY_EDITOR
            var s = UnityEditor.SessionState.GetString(SessionKey, "");
            return string.IsNullOrEmpty(s) ? null : s;
#else
            return null;
#endif
        }
    }

    public static class AppBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            AppCore.Current = null;
            HS2NextApp.ResetStatics();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            var args = Environment.GetCommandLineArgs();
#if !UNITY_EDITOR
            // -bridgeAccept <outDir> (Phase 2.5): the acceptance driver, before the decision on the app, so that a player
            // whose app does not start reports it instead of idling (the Editor runs the acceptance as BridgeTest.play).
            PlayerAcceptance.StartIfRequested(args);
#endif
            if (AppCore.Current != null || HS2NextApp.Instance != null) return;
            if (Array.IndexOf(args, "-noApp") >= 0 || Array.IndexOf(args, "-smoke") >= 0) return;
            string launch = AppLaunch.Read();
#if UNITY_EDITOR
            // Editor batch play sessions are tests: the app starts only when a launch override asks (PlayModeHarness).
            if (launch == null && Application.isBatchMode) return;
#endif
            AppSettings s;
            string error = null;
            try { s = AppSettings.Load(args, launch); }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
                s = AppSettings.Defaults(args, launch);
            }
            if (!s.AutoStart || s.NoApp)
            {
                if (launch == null) Debug.Log("[app] automatic start off (app.autoStart false)");
                return;
            }
            Start(s, error);
        }

        /// <summary>Creates the "HS2Next App" host in the active scene with the given settings (null = read them in stage 1).</summary>
        public static HS2NextApp Start(AppSettings settings = null, string settingsError = null)
        {
            var go = new GameObject(HS2NextApp.ObjectName);
            var app = go.AddComponent<HS2NextApp>();
            app.Init(settings, settingsError);
            return app;
        }
    }
}
