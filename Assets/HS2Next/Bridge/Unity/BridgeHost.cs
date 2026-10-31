// The entry Core uses to run the automation bridge in Unity,
// on by default in every build. BridgeHost.Start(options) creates the
// server, starts it (listening on 127.0.0.1, or in-process only), feeds Unity's log (all threads) into log.tail and,
// unless the pump is manual, adds BridgePump (Update, execution order −10000) and BridgeLatePump (LateUpdate, 32000)
// to the given parent or to an own DontDestroyOnLoad object. Outside play mode there is no player loop, so the pump
// is always manual there: the caller steps with PumpUpdate / PumpLate (or ManualFrame).
// The host is disposed on Application.quitting, when a pump component is disabled or destroyed (leaving play mode,
// the parent going away), before an assembly reload (AssemblyReloadEvents, Editor) and when play mode is left without
// a domain reload; a new play session without domain reload also disposes what an earlier session left
// (SubsystemRegistration). Main thread.
using System;
using System.Threading.Tasks;
using HS2Next.Api.Bridge;
using UnityEngine;

namespace HS2Next.Bridge
{
    /// <summary>What Core passes to <see cref="BridgeHost.Start"/>.</summary>
    public sealed class BridgeHostOptions
    {
        public int Port = BridgeProtocol.DefaultPort;
        /// <summary>False: no HttpListener (in-process Execute only; edit-mode tests).</summary>
        public bool Listen = true;
        /// <summary>Requires X-HS2Next-Token; the token (<see cref="Token"/> or a random one) is written to &lt;BridgeDir&gt;/token.</summary>
        public bool TokenRequired;
        public string Token;
        /// <summary>The token file's permissions (Auto: owner-only outside the user's profile folder; the app may pass OwnerOnly in its portable mode).</summary>
        public TokenAclMode TokenAcl = TokenAclMode.Auto;
        /// <summary>Absolute folder of endpoint.json and the token file (Core passes &lt;UserData&gt;/Bridge); null = none.</summary>
        public string BridgeDir;
        /// <summary>Absolute root of bridge file output (IOutputRoot.Root); null = <see cref="BridgeDir"/>.</summary>
        public string OutputRoot;
        public bool AllowQuit;
        /// <summary>bridge.version.app (default Application.version).</summary>
        public string AppVersion;
        /// <summary>bridge.version.build; null = the facts of this process (the player's build.json; kind editor in the Editor).</summary>
        public BuildFacts Build;
        /// <summary>True: no pump components; the caller calls PumpUpdate / PumpLate (always so outside play mode).</summary>
        public bool ManualPump;
        /// <summary>Where the pump components go (Core's app host); null = an own DontDestroyOnLoad object "HS2Next Bridge".</summary>
        public Transform PumpParent;
        /// <summary>Readiness stages declared at start (Core may declare more or re-declare).</summary>
        public string[] RequiredStages = { "catalog", "studioDb" };
        public string[] OptionalStages = { "scene" };
        /// <summary>Bridge events (bind, failures); null = Unity's log.</summary>
        public Action<BridgeLogLevel, string> Logger;
        /// <summary>Optional limits and timings (timeouts, body size, queue, pump budget); the fields above override it.</summary>
        public BridgeOptions Server;

        /// <summary>The server configuration these options give.</summary>
        public BridgeOptions ToServerOptions()
        {
            var o = Server != null ? Server.Clone() : new BridgeOptions();
            o.Enabled = true;
            o.Port = Port;
            o.RequireToken = TokenRequired;
            o.Token = Token;
            o.TokenAcl = TokenAcl;
            o.BridgeDir = BridgeDir;
            o.UserDataRoot = null;
            o.OutputDir = string.IsNullOrEmpty(OutputRoot) ? BridgeDir : OutputRoot;
            o.AllowQuit = AllowQuit;
            o.AppVersion = string.IsNullOrEmpty(AppVersion) ? Application.version : AppVersion;
            o.UnityVersion = Application.unityVersion;
            o.Build = Build ?? BuildFacts.Read(Application.streamingAssetsPath, Application.isEditor, o.AppVersion);
            o.RequiredStages = RequiredStages ?? new string[0];
            o.OptionalStages = OptionalStages ?? new string[0];
            o.Logger = Logger ?? UnityLog;
            return o;
        }

        internal static void UnityLog(BridgeLogLevel level, string message)
        {
            switch (level)
            {
                case BridgeLogLevel.Error: Debug.LogError(message); break;
                case BridgeLogLevel.Warning: Debug.LogWarning(message); break;
                default: Debug.Log(message); break;
            }
        }
    }

    public sealed class BridgeHost : IDisposable
    {
        static BridgeHost current;
        static bool quitHooked;

        readonly GameObject ownObject;
        readonly BridgePump pump;
        readonly BridgeLatePump latePump;
        readonly bool startedInPlayMode;
        int manualFrame;
        bool disposed;

        /// <summary>The running host, or null.</summary>
        public static BridgeHost Current => current;

        /// <summary>
        /// Starts the bridge (disposing the current host first). Never throws for a failed bind: Listening is then false
        /// and BindError says why; in-process Execute still works. Main thread.
        /// </summary>
        public static BridgeHost Start(BridgeHostOptions options)
        {
            current?.Dispose();
            var h = new BridgeHost(options ?? new BridgeHostOptions());
            current = h;
            return h;
        }

        BridgeHost(BridgeHostOptions o)
        {
            UnityJsonConverters.Install();
            Server = new BridgeServer(o.ToServerOptions());
            Application.logMessageReceivedThreaded += OnLog;
            if (!quitHooked)
            {
                quitHooked = true;
                Application.quitting += () => current?.Dispose();
            }
            Server.Start(o.Listen);
            Server.SetHostInfo(Time.realtimeSinceStartupAsDouble, Screen.width, Screen.height);
            startedInPlayMode = Application.isPlaying;
            ManualPump = o.ManualPump || !Application.isPlaying;
            if (!ManualPump)
            {
                GameObject go;
                if (o.PumpParent != null) go = o.PumpParent.gameObject;
                else
                {
                    go = ownObject = new GameObject("HS2Next Bridge");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                }
                pump = go.AddComponent<BridgePump>();
                pump.Host = this;
                latePump = go.AddComponent<BridgeLatePump>();
                latePump.Host = this;
            }
        }

        /// <summary>The server (registry, readiness, frames, output root, log ring, state).</summary>
        public BridgeServer Server { get; }
        public IBridgeServices Services => Server;
        /// <summary>"http://127.0.0.1:&lt;port&gt;/" while listening, else null.</summary>
        public string Url => Server.IsListening ? Server.Url : null;
        public bool Listening => Server.IsListening;
        /// <summary>Why the bind failed (null when listening or started without a listener).</summary>
        public string BindError => Server.LastError;
        /// <summary>True when the caller pumps (ManualPump, or not in play mode).</summary>
        public bool ManualPump { get; }
        public bool IsDisposed => disposed;

        /// <summary>A request without HTTP (no header checks; same parsing, gate and dispatcher). Completes with the reply JSON.</summary>
        public Task<string> Execute(string requestJson) => Server.SubmitAsync(requestJson);

        /// <summary>Manual pump: one pump of the next frame (the frame counter goes up by one). Main thread.</summary>
        public void PumpUpdate()
        {
            if (!ManualPump) throw new InvalidOperationException("the bridge is pumped by BridgePump; PumpUpdate is for ManualPump hosts");
            manualFrame = Math.Max(manualFrame + 1, Server.Frames.Frame + 1);
            Server.SetHostInfo(Time.realtimeSinceStartupAsDouble, Screen.width, Screen.height);
            Server.Pump(manualFrame);
        }

        /// <summary>Manual pump: the late point of the current frame. Main thread.</summary>
        public void PumpLate()
        {
            if (!ManualPump) throw new InvalidOperationException("the bridge is pumped by BridgeLatePump; PumpLate is for ManualPump hosts");
            Server.PumpLate(Server.Frames.Frame);
        }

        /// <summary>PumpUpdate + PumpLate; returns the frame number.</summary>
        public int ManualFrame()
        {
            PumpUpdate();
            PumpLate();
            return Server.Frames.Frame;
        }

        /// <summary>Stops the listener, answers queued requests (503), deletes endpoint.json, removes the pump components. Idempotent.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (ReferenceEquals(current, this)) current = null;
            Application.logMessageReceivedThreaded -= OnLog;
            Server.Stop();
            if (pump != null) pump.Host = null;
            if (latePump != null) latePump.Host = null;
            try
            {
                if (ownObject != null) Destroy(ownObject);
                else
                {
                    if (pump != null) Destroy(pump);
                    if (latePump != null) Destroy(latePump);
                }
            }
            catch (Exception) { }   // off the main thread, or already destroyed
        }

        static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            LogEntryType t;
            switch (type)
            {
                case LogType.Error: t = LogEntryType.Error; break;
                case LogType.Assert: t = LogEntryType.Assert; break;
                case LogType.Warning: t = LogEntryType.Warning; break;
                case LogType.Exception: t = LogEntryType.Exception; break;
                default: t = LogEntryType.Log; break;
            }
            Server.Log.Add(t, condition, stackTrace);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlaySession()
        {
            // Enter Play Mode without a domain reload keeps statics: drop a host an earlier session left behind.
            current?.Dispose();
            current = null;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void EditorHooks()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += () => current?.Dispose();
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            {
                if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode && current != null && current.startedInPlayMode) current.Dispose();
            };
        }
#endif
    }
}
