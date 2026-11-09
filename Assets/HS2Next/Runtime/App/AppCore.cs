// The HS2Next application core: settings, paths, content catalog and Studio lists, the scene
// session and its runtime, and package B's bridge host with the Core commands (with bridge.enabled false the host runs
// without a listener: timed commands and plugins' calls still reach the commands). HS2NextApp (the host MonoBehaviour)
// runs Boot() as a coroutine in play mode and players; edit-mode tests use ForTests, which builds the session and the
// runtime on given services, starts the bridge without a listener and drives frames by hand (Frame, Call).
// Core references HS2Next.Bridge only here (start, pump, stop, in-process Execute); the handlers see the Api.
// The last startup stage loads the plugins (PluginHost); a test host loads them only when asked (LoadPlugins). The
// plugins unload before the bridge and the runtime are disposed.
// The setup stage (after the bridge, so that the first-run guide can be driven over it) shows the guide in the UI's setup mode
// when no valid HS2 folder is set (or -setup asks); the guide writes the settings, the content settings are read again and the
// boot builds the index while the guide shows its progress. A long catalog scan shows the same progress page on its own.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HS2Next.Api.Bridge;
using HS2Next.App.Commands;
using HS2Next.App.Plugins;
using HS2Next.App.Setup;
using HS2Next.Bridge;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Setup;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HS2Next.App
{
    /// <summary>Where the startup sequence is.</summary>
    public enum AppStage { Starting, Ready, Failed }

    public sealed class AppCore : IDisposable
    {
        /// <summary>The app of this play session or player (null when none; reset in SubsystemRegistration).</summary>
        public static AppCore Current { get; internal set; }

        public AppSettings Settings { get; private set; }
        public AppPaths Paths { get; private set; }
        public RuntimeServices Services { get; private set; }
        public ContentCatalog Catalog => Services?.Catalog;
        public StudioDb Db => Services?.Db;
        public SceneSession Session { get; private set; }
        public SceneRuntime Runtime { get; private set; }
        /// <summary>The operations layer shared by the UI and the bridge; null without a runtime.</summary>
        public StudioOps Ops => StudioOps.For(Runtime);
        /// <summary>
        /// Package B's host, or null before the bridge stage (and after a start that failed before it with bridge.enabled false).
        /// With bridge.enabled false the host runs without a listener (<see cref="BridgeHost.Listening"/> false): read Listening
        /// for "is there a listener".
        /// </summary>
        public BridgeHost Bridge { get; private set; }
        /// <summary>The plugin loader of this app (after the plugins stage; a test host's after LoadPlugins), or null.</summary>
        public PluginHost Plugins { get; private set; }
        public IBridgeServices BridgeServices => Bridge?.Services;

        public AppStage Stage { get; private set; } = AppStage.Starting;
        /// <summary>The setup stage's decision of this start (null before the stage ran).</summary>
        public SetupStage.Decision SetupDecision { get; private set; }
        /// <summary>The setup-mode UI while it runs (the guide, or the progress page of a long scan), else null.</summary>
        public SetupHost SetupScreen => setupHost != null && !setupHost.Closed ? setupHost : null;
        /// <summary>The guide was skipped in this start (the app runs without HS2 content).</summary>
        public bool SetupSkipped { get; private set; }
        /// <summary>The settings keys the guide wrote in this start (empty when it did not run or wrote nothing).</summary>
        public IReadOnlyList<string> SetupWritten => setupWritten;
        /// <summary>An extra source of the setup stage's -setup: the Editor launch override's "setup": true (a play test).</summary>
        public bool SetupForced { get; private set; }
        /// <summary>The current (or failed) startup stage.</summary>
        public string StageName { get; private set; } = "settings";
        /// <summary>"&lt;stage&gt;: &lt;type&gt;: &lt;message&gt;" after a failed stage, else null.</summary>
        public string Error { get; private set; }
        /// <summary>True for an edit-mode test host (ForTests).</summary>
        public bool IsTestHost { get; private set; }
        public bool IsDisposed => disposed;

        /// <summary>Per-app state of the command files (e.g. package W2's motion overrides), by a key of their choice.</summary>
        public IDictionary<string, object> Items { get; } = new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>The value of <see cref="Items"/>[<paramref name="key"/>], created by <paramref name="create"/> on first use.</summary>
        public T Item<T>(string key, Func<T> create) where T : class
        {
            if (Items.TryGetValue(key, out var o) && o is T t) return t;
            t = create();
            Items[key] = t;
            return t;
        }

        IDisposable importer;
        BatchTransactionHook batchHook;
        AppPipeline pipeline;
        Transform host;
        string settingsError;
        bool disposed;
        InstanceLock instanceLock;
        // another app holds this UserData: the bridge of this failed start writes no discovery files there
        bool lockRefused;

        AppCore() { }

        // ------------------------------------------------------------ play mode and players

        /// <summary>
        /// The app of a host MonoBehaviour (HS2NextApp): <paramref name="settings"/> as AppBootstrap read them (null = read in
        /// stage 1), <paramref name="settingsError"/> when they could not be read (the defaults are used so that the bridge
        /// comes up; the settings stage then fails). Becomes <see cref="Current"/>; run <see cref="Boot"/> as a coroutine.
        /// </summary>
        public static AppCore Create(Transform host, AppSettings settings = null, string settingsError = null)
        {
            Current?.Dispose();
            var app = new AppCore { host = host, Settings = settings, settingsError = settingsError };
            Current = app;
            Application.quitting += app.OnQuitting;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged += app.OnPlayModeChanged;
#endif
            return app;
        }

        void OnQuitting() => Dispose();

#if UNITY_EDITOR
        void OnPlayModeChanged(UnityEditor.PlayModeStateChange s)
        {
            if (s == UnityEditor.PlayModeStateChange.ExitingPlayMode) Dispose();
        }
#endif

        /// <summary>
        /// The startup sequence, one frame per <c>yield return null</c>: settings, pipeline, bridge, vanilla lists,
        /// the zipmod scan on a worker thread, mod rows and Studio lists, services / session / runtime. Each stage logs
        /// "[app] &lt;stage&gt;: &lt;ms&gt; ms; &lt;summary&gt;". A stage that throws sets Failed and Error and stops the
        /// sequence; the bridge stays up (it is started then if the failure came before its stage).
        /// </summary>
        public IEnumerator Boot()
        {
            var total = Stopwatch.StartNew();
            var stages = new (string name, Func<IEnumerator> run)[]
            {
                ("settings", StageSettings), ("pipeline", StagePipeline), ("bridge", StageBridge), ("setup", StageSetup), ("catalog.vanilla", StageVanilla),
                ("catalog.mods", StageMods), ("studioDb", StageStudioDb), ("scene", StageScene), ("plugins", StagePlugins),
            };
            // a restarted app (-waitPid): the app it replaces must have ended and released the UserData lock first
            int waitPid = CommandLine.WaitPid(Environment.GetCommandLineArgs());
            if (waitPid > 0)
            {
                var w = WaitForPrevious(waitPid);
                while (w.MoveNext()) { yield return w.Current; if (disposed) yield break; }
            }
            foreach (var (name, run) in stages)
            {
                if (disposed) yield break;
                StageName = name;
                stageSummary = "";
                var sw = Stopwatch.StartNew();
                IEnumerator e = null;
                Exception error = null;
                try { e = run(); }
                catch (Exception ex) { error = ex; }
                while (error == null && e != null)
                {
                    bool more;
                    try { more = e.MoveNext(); }
                    catch (Exception ex) { error = ex; break; }
                    if (!more) break;
                    yield return e.Current;
                    if (disposed) yield break;
                }
                if (error != null)
                {
                    Fail(name, error);
                    yield break;
                }
                Debug.Log($"[app] {name}: {sw.ElapsedMilliseconds} ms; {stageSummary}");
                if (stopBoot) { StageName = "quit"; yield break; }   // the guide's Quit: the app ends, the boot stops here
            }
            StageName = "done";
            Stage = AppStage.Ready;
            Debug.Log($"[app] ready in {total.ElapsedMilliseconds} ms");
            Plugins?.RaiseAppReady();
        }

        string stageSummary = "";

        /// <summary>The seconds a restarted app waits for the app it replaces, and then for the UserData lock.</summary>
        public const double RestartWaitSeconds = 30, RestartLockSeconds = 10;

        /// <summary>
        /// A restarted app (-waitPid &lt;pid&gt;): waits, bounded, until that process has ended, then until the UserData lock can be
        /// taken (probed and released at once; the settings stage takes it). On a timeout the settings stage fails with the lock's
        /// message and this log line says which process still runs.
        /// </summary>
        IEnumerator WaitForPrevious(int pid)
        {
            var sw = Stopwatch.StartNew();
            bool alive = true;
            while (sw.Elapsed.TotalSeconds < RestartWaitSeconds)
            {
                alive = ProcessAlive(pid);
                if (!alive) break;
                yield return null;
            }
            if (alive)
            {
                Debug.LogError("[app] restart: the previous HS2Next (PID " + pid + ") still runs after " + RestartWaitSeconds + " s; close it and start HS2Next again");
                yield break;
            }
            string ud = Settings?.UserData;
            double waitedPid = sw.Elapsed.TotalSeconds;
            if (!string.IsNullOrEmpty(ud) && System.IO.Directory.Exists(ud))
            {
                var lw = Stopwatch.StartNew();
                while (lw.Elapsed.TotalSeconds < RestartLockSeconds)
                {
                    var probe = InstanceLock.TryAcquire(ud, out _);
                    if (probe != null) { probe.Dispose(); break; }
                    yield return null;
                }
            }
            Debug.Log("[app] restart: the previous HS2Next (PID " + pid + ") ended after " + waitedPid.ToString("0.0") + " s; the UserData lock is free after " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s");
        }

        static bool ProcessAlive(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid)) return !p.HasExited;
            }
            catch (ArgumentException) { return false; }   // no such process
            catch (InvalidOperationException) { return false; }
            catch (Exception) { return true; }   // cannot tell: keep waiting (bounded)
        }

        void Fail(string stage, Exception e)
        {
            Stage = AppStage.Failed;
            Error = stage + ": " + e.GetType().Name + ": " + e.Message;
            Debug.LogError("[app] " + Error + "\n" + e.StackTrace);
            // The bridge reports the failure (ping.ready, log.tail): start it when the failure came before its stage.
            if (Bridge == null && Settings != null && Settings.Bridge.Enabled && Paths != null)
            {
                try { StartBridgeOfApp(); }
                catch (Exception be) { Debug.LogError("[app] bridge could not start: " + be.Message); }
            }
            try { Bridge?.Services.Readiness.SetDetail("scene", "startup failed: " + Error); } catch (Exception) { }
            try { setupHost?.BootFailed(Error); } catch (Exception) { }
        }

        IEnumerator StageSettings()
        {
            if (Settings == null)
            {
                try { Settings = AppSettings.Load(); }
                catch (Exception e)
                {
                    settingsError = e.GetType().Name + ": " + e.Message;
                    Settings = AppSettings.Defaults();
                }
            }
            // the folders of the data root, each proven writable; an unwritable one outside the per-user folder falls back to it
            var roots = Settings.Roots ?? ContentSettings.Roots;
            var fallbacks = new List<FolderFallback>();
            FolderException folderError = null;
            try { PrepareFolders(roots, fallbacks); }
            catch (FolderException e) { folderError = e; }
            Paths = new AppPaths(Settings, fallbacks);
            // one app per UserData (the lock lives as long as the app)
            string lockError = null;
            if (folderError == null)
            {
                instanceLock = InstanceLock.TryAcquire(Paths.UserData, out lockError);
                lockRefused = instanceLock == null;
            }
            Application.runInBackground = Settings.RunInBackground;
            // the player starts at the highest quality level whatever a stored player preference says (an Editor play session
            // keeps the Editor's level: an Editor test switches levels on purpose)
            if (!Application.isEditor)
            {
                int ultra = Array.IndexOf(QualitySettings.names, "Ultra");
                QualitySettings.SetQualityLevel(ultra >= 0 ? ultra : QualitySettings.count - 1, false);
            }
            if (Settings.FrameRate > 0) Application.targetFrameRate = Settings.FrameRate;
            foreach (var f in fallbacks) FolderFallbackNotice(f);
            if (roots.IsLong)
                Debug.LogWarning("[app] folders: the data root " + roots.DataRoot + " is " + roots.DataRoot.Length + " characters long; cache paths may pass the 260-character limit (portable mode in a shorter folder, or HS2NEXT_DATA_ROOT)");
            if (settingsError != null)
            {
                // Bring the bridge up with the defaults so that clients see the failed stage, then fail.
                if (Settings.Bridge.Enabled && folderError == null && !lockRefused) StartBridgeOfApp();
                throw new InvalidOperationException("settings not readable: " + settingsError);
            }
            if (folderError != null) throw folderError;
            if (lockError != null) throw new InvalidOperationException(lockError);
            stageSummary = roots + "; file " + (Settings.SettingsFile ?? "(none: " + Paths.SettingsFile + ")") + "; " + Paths +
                           (fallbacks.Count > 0 ? "; fallback " + string.Join(", ", fallbacks) : "") + "; frame rate " + (Settings.FrameRate > 0 ? Settings.FrameRate.ToString() : "unchanged") +
                           "; background " + (Settings.RunInBackground ? "runs" : "pauses") +
                           "; bridge " + BridgeSettingsSummary +
                           "; build " + BuildInfo.Current.Summary;
            return null;
        }

        /// <summary>
        /// Creates the folders of this run and proves each is writable: UserData with its subfolders (an unwritable one outside the
        /// per-user folder falls back to &lt;per-user folder&gt;/UserData, and a default output root follows it), an output root
        /// given apart from UserData, the cache (fallback &lt;per-user folder&gt;/Cache) and, for the default settings file, the
        /// data root's Config folder (a warning when it cannot be written outside the per-user folder). Throws FolderException when
        /// a folder under the per-user folder (or its fallback) cannot be written.
        /// </summary>
        void PrepareFolders(AppRoots roots, List<FolderFallback> fallbacks)
        {
            string pdp = roots.PersistentDataPath;
            string ud = AppFolders.Prepare("userData", Settings.UserData, roots.FallbackUserData, pdp, AppPaths.CreateUserDataFolders, fallbacks);
            if (!string.Equals(ud, Settings.UserData, StringComparison.OrdinalIgnoreCase)) Settings.MoveUserData(ud);
            string output = Settings.Bridge.OutputDir;
            if (AppRoots.IsUnder(output, Settings.UserData)) System.IO.Directory.CreateDirectory(System.IO.Path.Combine(output, "shots"));
            else AppFolders.Prepare("outputDir", output, null, pdp, d => System.IO.Directory.CreateDirectory(System.IO.Path.Combine(d, "shots")), fallbacks);
            if (Settings.Content != null)
            {
                string cache = Settings.Content.ResolvedCacheDir;
                string used = AppFolders.Prepare("cache", cache, roots.FallbackCacheDir, pdp, null, fallbacks);
                if (!string.Equals(used, cache, StringComparison.OrdinalIgnoreCase)) Settings.MoveCache(used);
            }
            if (Settings.SettingsSource == "default")
            {
                try { AppFolders.Probe(roots.ConfigDir); }
                catch (Exception e) when (AppFolders.IsFolderError(e))
                {
                    if (AppRoots.IsUnder(roots.ConfigDir, pdp)) throw new FolderException(roots.ConfigDir, e.GetType().Name + ": " + e.Message);
                    Debug.LogWarning("[app] folders: " + roots.ConfigDir + " is not writable (" + e.GetType().Name + ": " + e.Message + "); settings cannot be saved there");
                }
            }
        }

        // the log line and the warning toast of a folder that fell back to the per-user folder (shown when the UI is up)
        static void FolderFallbackNotice(FolderFallback f)
        {
            Debug.LogWarning("[app] folders: " + f.From + " is not writable (" + f.Reason + "); using " + f.To);
            HS2Next.UI.UiHost.Ready += ui => ui.Context?.Toasts?.Show(HS2Next.UI.Core.L10n.Format("msg.folderFallback", f.From, f.To), HS2Next.UI.Widgets.ToastKind.Warning, 12f);
        }

        IEnumerator StagePipeline()
        {
            pipeline = new AppPipeline();
            pipeline.Apply();
            stageSummary = pipeline.Summary;
            return null;
        }

        /// <summary>The bridge stage's line when bridge.enabled is false: the host runs, its commands run in process, nothing listens.</summary>
        public const string NoListenerSummary = "in-process (no listener)";

        /// <summary>The settings stage's words for the bridge settings: "port N[, token][, allowQuit]", or "disabled (no listener)".</summary>
        public string BridgeSettingsSummary =>
            Settings.Bridge.Enabled ? "port " + Settings.Bridge.Port + (Settings.Bridge.Token ? ", token" : "") + (Settings.Bridge.AllowQuit ? ", allowQuit" : "") : "disabled (no listener)";

        /// <summary>The bridge stage's line: "listening on &lt;url&gt;", "not listening" (bridge.enabled, the bind failed) or
        /// <see cref="NoListenerSummary"/> (bridge.enabled false); null before the host starts.</summary>
        public string BridgeStageSummary =>
            Bridge == null ? null : Bridge.Listening ? "listening on " + Bridge.Url : Settings.Bridge.Enabled ? "not listening" : NoListenerSummary;

        IEnumerator StageBridge()
        {
            StartBridgeOfApp();
            stageSummary = BridgeStageSummary;
            return null;
        }

        /// <summary>
        /// The app's bridge host: listening on the settings' port when bridge.enabled, else without a listener (no socket, no
        /// endpoint.json, no token file). Either way the host's pumps run and the core commands are registered, so timed
        /// commands and plugins' calls reach the app's commands.
        /// </summary>
        void StartBridgeOfApp()
        {
            if (Bridge != null) return;
            bool listen = Settings.Bridge.Enabled;
            StartBridge(listen, false, host);
            if (listen && !Bridge.Listening) Debug.LogWarning("[app] bridge not listening: " + Bridge.BindError);
        }

        // ------------------------------------------------------------ setup (the first-run guide)

        SetupHost setupHost;
        bool stopBoot;
        readonly List<string> setupWritten = new List<string>();
        Stopwatch catalogWatch;
        int expectedZipmods = -1;

        /// <summary>
        /// The setup stage: not needed (a valid hs2Root, or this guide version skipped) → ready at once with the reason; needed in a
        /// process without the UI (batch, -noUi) → fails with the message of SetupStage; else the guide runs in the UI's setup mode
        /// until it has written the settings (the content settings are read again; the guide shows the index's progress), was
        /// skipped (the app starts without HS2 content) or asked to quit (the app ends).
        /// </summary>
        IEnumerator StageSetup()
        {
            var ready = Bridge?.Services.Readiness;
            var args = Environment.GetCommandLineArgs();
            SetupForced = LaunchAsksSetup();
            var state = SetupState.Load(Paths.UserData);
            UnknownKeysNotice(state);
            var d = SetupStage.Decide(Paths.Hs2Root, SetupForced ? AppendArg(args, SetupStage.Flag) : args, state);
            SetupDecision = d;
            if (!d.Needed)
            {
                if (d.State.SkippedThisVersion && d.Install != null && !d.Install.Usable) ClearHs2Root();   // skipped with a failing folder
                ready?.Set("setup", true);
                stageSummary = "not needed (" + d.Reason + ")";
                return null;
            }
            if (!Settings.Ui.Run) throw new InvalidOperationException(Application.isBatchMode ? SetupStage.BatchMessage : SetupStage.NoUiMessage);
            return RunGuide(d);
        }

        IEnumerator RunGuide(SetupStage.Decision d)
        {
            var ready = Bridge?.Services.Readiness;
            setupHost = HS2Next.UI.UiHost.StartSetup(this, host, SetupContextOf(d, false));
            if (setupHost == null) throw new InvalidOperationException("the setup guide could not start (" + d.Reason + ")");
            Debug.Log("[app] setup: the guide opens (" + d.Reason + ")");
            string page = null;
            while (setupHost != null && !setupHost.Closed && setupHost.Outcome == SetupOutcome.None)
            {
                if (setupHost.Page != page)
                {
                    page = setupHost.Page;
                    ready?.SetDetail("setup", "page " + page);
                }
                yield return null;
            }
            var outcome = setupHost == null || setupHost.Closed ? SetupOutcome.Quit : setupHost.Outcome;
            ready?.SetDetail("setup", null);
            switch (outcome)
            {
                case SetupOutcome.Continue:
                    setupWritten.Clear();
                    setupWritten.AddRange(setupHost.Guide.Written);
                    expectedZipmods = setupHost.Guide.ExpectedZipmods;
                    ApplyGuideSettings();
                    ready?.Set("setup", true);
                    stageSummary = "the guide wrote " + (setupWritten.Count == 0 ? "nothing new" : string.Join(", ", setupWritten)) + " to " + Paths.SettingsFile + "; HS2 " + (Paths.Hs2Root ?? "(none)") + "; building the index";
                    break;
                case SetupOutcome.Skipped:
                    SetupSkipped = true;
                    if (Paths.Hs2Root != null && !Hs2InstallCheck.Check(Paths.Hs2Root, false, false).Usable) ClearHs2Root();
                    CloseSetupScreen();
                    ready?.Set("setup", true);
                    stageSummary = "skipped on page " + page + "; the app starts without HS2 content";
                    break;
                default:
                    CloseSetupScreen();
                    stopBoot = true;
                    ready?.SetDetail("setup", "quit on page " + page);
                    stageSummary = "quit on page " + page + " (the guide resumes at the next start)";
                    QuitFromSetup();
                    break;
            }
        }

        /// <summary>
        /// The settings file's members the runtime does not read, each with the nearest known key: one log line, and a warning
        /// toast when the UI is up (once per list: setup.json remembers the last one shown).
        /// </summary>
        void UnknownKeysNotice(SetupState state)
        {
            List<UnknownKey> unknown;
            try { unknown = SettingsSchema.UnknownKeys(SettingsFile.Read(Paths.SettingsFile)); }
            catch (Exception) { return; }   // the settings stage has reported a file that does not parse
            if (unknown.Count == 0) return;
            string list = string.Join(", ", unknown.Select(u => u.ToString()));
            Debug.LogWarning("[settings] " + Paths.SettingsFile + ": unknown keys (ignored): " + list);
            if (!Settings.Ui.Run || state.UnknownKeysShown == list) return;
            HS2Next.UI.UiHost.Ready += ui => ui.Context?.Toasts?.Show(HS2Next.UI.Core.L10n.Format("msg.unknownSettingsKeys", list, System.IO.Path.GetFileName(Paths.SettingsFile)), HS2Next.UI.Widgets.ToastKind.Warning, 12f);
            state.UnknownKeysShown = list;
            try { state.Save(Paths.UserData); } catch (Exception e) { Debug.LogWarning("[app] setup.json not written: " + e.Message); }
        }

        /// <summary>What the guide needs of this start: the folders, the current values, the copy offer.</summary>
        SetupContext SetupContextOf(SetupStage.Decision d, bool progressOnly)
        {
            var roots = Paths.Roots ?? Settings.Roots ?? ContentSettings.Roots;
            var c = new SetupContext
            {
                Mode = roots?.ModeName ?? "editor", DataRoot = roots?.DataRoot, ExeDir = roots?.ExeDir, UserData = Paths.UserData,
                SettingsFile = Paths.SettingsFile, DefaultCacheDir = roots?.DefaultCacheDir, CurrentHs2Root = Paths.Hs2Root,
                CurrentCacheDir = Paths.CacheDir, CurrentModsFolders = Settings.Content?.modsFolders ?? Array.Empty<string>(),
                Version = Application.version, Args = Environment.GetCommandLineArgs(), UiBlock = Settings.Ui?.Block, Decision = d,
                ProgressOnly = progressOnly, TokenFile = Settings.Bridge.Token ? System.IO.Path.Combine(Paths.BridgeDir, "token") : null,
            };
            if (SetupMigrate != null) c.RunMigration = (source, progress) => SetupMigrate(this, source, progress);
            SetupMigration?.Invoke(this, c);
            return c;
        }

        /// <summary>Fills the copy offer of the guide's page 1 (the one migration: the exe folder's earlier UserData in the per-user
        /// mode; re-read on every call, so a finished copy is no longer offered).</summary>
        public static Action<AppCore, SetupContext> SetupMigration = (app, c) => c.MigrationOffer = () => MigrationOfferOf(app);
        /// <summary>Copies an earlier UserData folder into this app's (the guide, app.setup migrate, the settings page), reporting
        /// (done, total) files; a refusal (missing source, a link, overlapping folders, too little free space) is the result's Error.</summary>
        public static Func<AppCore, string, Action<int, int>, MigrationResultInfo> SetupMigrate = (app, source, progress) => RunMigration(app, source, progress);

        /// <summary>A start without the guide that still finds the earlier UserData the one migration offers: a log line and, with
        /// the UI, a toast that points at File ▸ Options ▸ Folders (the copy there).</summary>
        void MigrationNotice()
        {
            if (setupWritten.Count > 0 || SetupDecision?.Needed == true) return;   // the guide's page 1 offered it in this start
            string src;
            try { src = UserDataMigration.Offered(Settings.Roots ?? ContentSettings.Roots, Settings.UserDataSource, Paths.UserData); }
            catch (Exception e) { Debug.LogWarning("[app] migration offer: " + e.Message); return; }
            if (src == null) return;
            Debug.Log("[app] an earlier UserData folder can be copied: " + src + " (File > Options > Folders)");
            if (Settings.Ui.Run)
                HS2Next.UI.UiHost.Ready += ui => ui.Context?.Toasts?.Show(HS2Next.UI.Core.L10n.Format("msg.migrationOffer", src), HS2Next.UI.Widgets.ToastKind.Info, 12f);
        }

        static MigrationOfferInfo MigrationOfferOf(AppCore app)
        {
            if (app?.Paths == null || app.Settings == null) return null;
            string src = UserDataMigration.Offered(app.Settings.Roots ?? ContentSettings.Roots, app.Settings.UserDataSource, app.Paths.UserData);
            if (src == null) return null;
            var plan = UserDataMigration.Plan(src);
            return new MigrationOfferInfo { Source = src, Files = plan.Files.Count, Bytes = plan.Bytes };
        }

        static MigrationResultInfo RunMigration(AppCore app, string source, Action<int, int> progress)
        {
            try
            {
                string src = UserDataMigration.NormaliseSource(source);
                string settings = app.Settings.SettingsSource == "default" ? app.Settings.SettingsPath : null;
                var run = UserDataMigration.Run(src, app.Paths.UserData, settings, progress);
                return new MigrationResultInfo { Copied = run.Copied, Same = run.Same, Conflicts = run.Conflicts.Count, Refused = run.Refused.Count + run.Failed.Count, Bytes = run.Bytes };
            }
            catch (MigrationException e) { return new MigrationResultInfo { Error = e.Message }; }
        }

        /// <summary>The content settings again from the file the guide wrote (command-line values still win); the cache it chose is
        /// created and proven writable as at the settings stage (fallback included).</summary>
        void ApplyGuideSettings()
        {
            Settings.Content = ContentSettings.LoadDefault(Environment.GetCommandLineArgs());
            var roots = Paths.Roots ?? ContentSettings.Roots;
            var fallbacks = new List<FolderFallback>(Paths.Fallbacks ?? Array.Empty<FolderFallback>());
            int before = fallbacks.Count;
            string cache = Settings.Content.ResolvedCacheDir;
            string used = AppFolders.Prepare("cache", cache, roots.FallbackCacheDir, roots.PersistentDataPath, null, fallbacks);
            if (!string.Equals(used, cache, StringComparison.OrdinalIgnoreCase)) Settings.MoveCache(used);
            for (int i = before; i < fallbacks.Count; i++) FolderFallbackNotice(fallbacks[i]);
            Paths = new AppPaths(Settings, fallbacks);
        }

        /// <summary>No HS2 content for this start (the guide was skipped while the folder fails its check).</summary>
        void ClearHs2Root()
        {
            if (Settings.Content == null || string.IsNullOrEmpty(Settings.Content.hs2Root)) return;
            Debug.LogWarning("[app] setup: the HS2 folder " + Settings.Content.hs2Root + " fails its check; this start runs without HS2 content");
            Settings.Content.hs2Root = "";
            Paths = new AppPaths(Settings, Paths.Fallbacks);
        }

        void CloseSetupScreen()
        {
            try { setupHost?.Close(); } catch (Exception e) { Debug.LogWarning("[app] setup: close: " + e.Message); }
            setupHost = null;
        }

        /// <summary>The guide's Quit: the player ends; an Editor play session stops.</summary>
        static void QuitFromSetup()
        {
            Debug.Log("[app] setup: quit (the guide resumes at the next start)");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>The Editor launch override asks for the guide ({"setup": true}; a play test's -setup).</summary>
        static bool LaunchAsksSetup()
        {
            var launch = AppLaunch.Read();
            if (string.IsNullOrEmpty(launch)) return false;
            try { return Newtonsoft.Json.Linq.JObject.Parse(launch)["setup"] is Newtonsoft.Json.Linq.JValue v && v.Type == Newtonsoft.Json.Linq.JTokenType.Boolean && (bool)v; }
            catch (Exception) { return false; }
        }

        static string[] AppendArg(string[] args, string a)
        {
            var l = new List<string>(args ?? Array.Empty<string>()) { a };
            return l.ToArray();
        }

        /// <summary>The catalog scan's progress for the setup screen; a GUI start whose catalog stages pass 2 s shows the progress
        /// page by itself.</summary>
        void SetupProgress(int visited, double seconds)
        {
            if (setupHost == null && !progressShown && Settings.Ui.Run && !Application.isBatchMode && catalogWatch != null && catalogWatch.ElapsedMilliseconds > 2000)
            {
                progressShown = true;
                setupHost = HS2Next.UI.UiHost.StartSetup(this, host, SetupContextOf(SetupDecision, true));
                if (setupHost != null) Debug.Log("[app] catalog: the scan takes long; the progress page opens");
            }
            if (setupHost != null && !setupHost.Closed) setupHost.Progress(visited, expectedZipmods, seconds);
        }

        bool progressShown;

        IEnumerator StageVanilla()
        {
            catalogWatch = Stopwatch.StartNew();
            var catalog = ContentCatalog.FromSettings(Settings.Content);
            Services = new RuntimeServices { Catalog = catalog, Hs2Root = Paths.Hs2Root, UserData = Paths.UserData, Settings = new SceneRuntimeSettings() };
            catalog.LoadVanillaLists();
            stageSummary = catalog.VanillaCount + " vanilla rows from " + catalog.Sources.Count + " sources; cache " + catalog.CacheDir +
                           ", texture guard " + HS2Next.Chara.TextureGuard.CacheDir + " (" + HS2Next.Chara.TextureGuard.RepairedCount + " repaired bundles)";
            yield return null;
        }

        IEnumerator StageMods()
        {
            var catalog = Catalog;
            int visited = 0;
            var scan = Task.Run(() => catalog.ScanModSources(n => Interlocked.Exchange(ref visited, n)));
            var sw = Stopwatch.StartNew();
            long nextReport = 5000;
            var ready = Bridge?.Services.Readiness;
            while (!scan.IsCompleted)
            {
                if (sw.ElapsedMilliseconds >= nextReport)
                {
                    nextReport += 5000;
                    string detail = "scanning zipmods: " + Volatile.Read(ref visited) + " visited";
                    Debug.Log("[app] catalog.mods: " + detail + " (" + sw.ElapsedMilliseconds / 1000 + " s)");
                    ready?.SetDetail("catalog", detail);
                }
                SetupProgress(Volatile.Read(ref visited), catalogWatch != null ? catalogWatch.Elapsed.TotalSeconds : sw.Elapsed.TotalSeconds);
                yield return null;
            }
            if (scan.IsFaulted) throw scan.Exception.GetBaseException();
            var results = scan.Result;
            long scanMs = sw.ElapsedMilliseconds;
            catalog.LoadModLists(results);
            // Phase 5.2: the crash quarantine of the user folder (a bundle that was loading when the last run ended is refused,
            // once announced) and the background texture check (the player, unless off; the Editor only with -texturePrescan)
            var lastCrash = catalog.EnableQuarantine(Paths.UserData);
            if (lastCrash != null) QuarantineNotice(lastCrash);
            var cmd = Environment.GetCommandLineArgs();
            if (Array.IndexOf(cmd, "-noTexturePrescan") < 0 && (Application.isEditor ? Array.IndexOf(cmd, "-texturePrescan") >= 0 : Settings.Content.content?.texturePrescan == true))
                catalog.StartTexturePrescan();
            if (Array.IndexOf(cmd, "-texturePrescanQuit") >= 0)   // Tools/texcheck_prescan.ps1: the scan alone, then quit
            {
                while (catalog.PrescanRunning) yield return null;
                Debug.Log("[app] texture prescan done; quitting (-texturePrescanQuit)");
                Application.Quit();
                yield break;
            }
            ready?.SetDetail("catalog", null);
            ready?.Set("catalog", true);
            AppWarmup.Start(catalog);   // the hair shader materials, loaded asynchronously and kept (nothing waits for them)
            int scanned = 0, zips = 0;
            foreach (var r in results) { scanned += r.Scanned; zips += r.Zipmods.Count; }
            stageSummary = $"scan {scanMs} ms on a worker ({zips} zipmods, {scanned} read from disk), {catalog.ModRowsLoaded} mod rows, {catalog.Count} rows";
        }

        // the log line and the one-time notice of a bundle the last run ended in (shown when the UI is up)
        static void QuarantineNotice(QuarantineEntry e)
        {
            string name = CrashQuarantine.ShortKey(e.Key);
            Debug.LogWarning("[app] content.quarantined: the last run ended while it loaded " + name + "; its loads are refused until the mod changes (content.quarantine clears it)");
            HS2Next.UI.UiHost.Ready += ui => ui.Context?.Toasts?.Show(HS2Next.UI.Core.L10n.Format("msg.contentQuarantined", name), HS2Next.UI.Widgets.ToastKind.Warning, 12f);
        }

        IEnumerator StageStudioDb()
        {
            // the AI install gate follows the content settings (HS2 on the reference install shows the AI content)
            Services.Db = Catalog.LoadStudioLists(new StudioLoadOptions
            {
                EmulateAIInstallGate = !(Settings?.Content?.aiInstall ?? true),
                AccessoryItems = Settings?.StudioAccessoryItems ?? true,   // group 647158 (settings key studio.accessoryItems)
            });
            AppItemRows.Register(Services.Db, Paths?.UserData);   // the app's generated group 731406 (bakes and cut markers), before the scene
            Bridge?.Services.Readiness.Set("studioDb", true);
            if (setupHost != null && !setupHost.Closed) setupHost.IndexDone();   // the guide shows the ready page; a progress page closes
            stageSummary = Db.Items.Count + " items, " + Db.Anime.Count + " anime, " + Db.Maps.Count + " maps";
            return null;
        }

        IEnumerator StageScene()
        {
            Session = new SceneSession(Services.NewSessionOptions());
            // SceneRuntime.CreateDefault, keeping the importer handle for Dispose.
            importer = ImportSetup.Register(Services);
            Runtime = new SceneRuntime(Session, Services, host).AddStandardParts();
            CoreCommands.RuntimeCreated(this);
            Bridge?.Services.Readiness.Set("scene", true);
            HS2Next.UI.UiHost.Start(this, host);   // Phase 3.5 package A: the Studio UI (off with -noUi; never throws)
            if (Paths.Hs2Root == null && Settings.Ui.Run) SetupBanner.ShowWhenReady();   // no HS2 content: the banner with the guide's button
            MigrationNotice();   // a later start that still finds an earlier UserData next to the exe: a toast points at the copy
            stageSummary = "runtime " + SceneRuntime.RootName + " under " + (host ? host.name : "(scene root)") + ", drivers " + Application.isPlaying;
            return null;
        }

        /// <summary>
        /// The plugins stage: the folder and switch of this run (-pluginsDir, -noPlugins, HS2NEXT_NO_PLUGINS, the settings), then
        /// discover, validate, order and load, the plugin pump on the host object, SceneLoaded(Initial), the bridge.version
        /// list; the readiness stage "plugins" becomes ready even when no plugin loads.
        /// </summary>
        IEnumerator StagePlugins()
        {
            var launch = Settings.PluginLaunch();
            Plugins = new PluginHost(Bridge?.Server, Paths.UserData, this);
            Plugins.Load(launch, logSummary: false);   // the stage line below is the summary
            if (host && Application.isPlaying)
            {
                var pump = host.gameObject.GetComponent<PluginPump>() ?? host.gameObject.AddComponent<PluginPump>();
                pump.Init(Plugins);
            }
            Bridge?.Services.Readiness.Set(PluginHost.ReadinessStage, true);
            stageSummary = Plugins.Summary;
            return null;
        }

        /// <summary>
        /// A write guard for one of Core's folders with the rules of the bridge output root (package B's OutputRoot,
        /// R9): rooted, drive-relative, '..', ':', device names and paths outside the root answer io_denied.
        /// </summary>
        public IOutputRoot RootFor(string absoluteDir) => OutputRoot.For(absoluteDir);

        // ------------------------------------------------------------ bridge

        /// <summary>Starts package B's host and registers everything Core adds to it.</summary>
        void StartBridge(bool listen, bool manualPump, Transform pumpParent)
        {
            var s = Settings.Bridge;
            Bridge = BridgeHost.Start(new BridgeHostOptions
            {
                Port = s.Port, Listen = listen, TokenRequired = s.Token, Token = s.Token ? s.TokenValue : null,
                BridgeDir = lockRefused ? null : Paths.BridgeDir, OutputRoot = lockRefused ? null : Paths.OutputDir,
                AllowQuit = s.AllowQuit, AppVersion = Application.version, ManualPump = manualPump, PumpParent = pumpParent,
                Build = BuildInfo.Current,
                // portable: owner-only also inside the profile folder (Auto makes it owner-only anywhere outside it)
                TokenAcl = Paths?.Roots?.Mode == DataRootMode.Portable ? HS2Next.Bridge.TokenAclMode.OwnerOnly : HS2Next.Bridge.TokenAclMode.Auto,
            });
            var services = Bridge.Services;
            services.Readiness.Declare("setup", true);
            services.Readiness.Declare("scene", true);
            services.Readiness.Declare(PluginHost.ReadinessStage, true);
            // in-process inline calls (a plugin's Call) never change the scene while another party's transaction is open, a
            // scene event or a capture event is being delivered
            Bridge.Server.InlineGate = spec =>
            {
                if (HS2Next.Screenshots.CaptureEvents.IsDelivering) return "a capture event is being delivered (the capture renders the scene as it is); try again in a later frame";
                var bus = Session?.Commands;
                if (bus == null) return null;
                if (bus.ActiveTransaction != null) return "a scene transaction is open (a drag or a batch); try again in a later frame";
                if (bus.Busy) return "the scene is busy (a command applies or a scene event is being delivered); try again in a later frame";
                return null;
            };
            CoreCommands.Register(services.Commands, this);
            batchHook = new BatchTransactionHook(this);
            services.AddBatchHook(batchHook);
        }

        // ------------------------------------------------------------ edit-mode test host

        /// <summary>
        /// A test host on <paramref name="services"/> (built by the test with CardResolveTest.BuildDb and LoadStudioLists):
        /// ManualStepping off (characters step with AutoStep as in play mode), a session with services.NewSessionOptions(),
        /// a runtime without drivers and with the standard parts, the HS2 importer registered, package B's host without a
        /// listener and with a manual pump, every readiness stage ready. Starting a host disposes the bridge of an earlier
        /// one (BridgeHost.Current is one per process).
        /// </summary>
        public static AppCore ForTests(RuntimeServices services, AppSettings settings = null, Transform parent = null) =>
            ForTests(services, settings, parent, null);

        /// <summary>As above with the session options given (e.g. Ids replaced by a counting source).</summary>
        public static AppCore ForTests(RuntimeServices services, AppSettings settings, Transform parent, SessionOptions options) =>
            NewTestHost(services, settings, parent, options, false);

        /// <summary>
        /// A test host as <see cref="ForTests(RuntimeServices, AppSettings, Transform, SessionOptions)"/>, except that the bridge
        /// starts as the app's bridge stage starts it: listening on the settings' port (discovery files in the settings' UserData)
        /// when bridge.enabled, else without a listener. Outside play mode the pump is manual (<see cref="Frame"/> pumps it); in
        /// play mode the pump components go on <paramref name="parent"/>'s object (null: an own object) and Frame leaves the bridge
        /// to them, so <see cref="Call"/> needs the edit-mode form.
        /// </summary>
        public static AppCore ForTestsAsApp(RuntimeServices services, AppSettings settings, Transform parent = null, SessionOptions options = null) =>
            NewTestHost(services, settings, parent, options, true);

        static AppCore NewTestHost(RuntimeServices services, AppSettings settings, Transform parent, SessionOptions options, bool bridgeAsApp)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            var app = new AppCore { IsTestHost = true };
            app.Settings = settings ?? AppSettings.Defaults(Array.Empty<string>());
            app.Paths = new AppPaths(app.Settings);
            app.Paths.CreateFolders();
            app.Services = services;
            services.UserData = app.Paths.UserData;
            services.Settings.ManualStepping = false;
            app.Session = new SceneSession(options ?? services.NewSessionOptions());
            app.importer = ImportSetup.Register(services);
            app.Runtime = new SceneRuntime(app.Session, services, parent, addDrivers: false).AddStandardParts();
            CoreCommands.RuntimeCreated(app);
            if (bridgeAsApp)
            {
                app.host = parent;
                app.StartBridgeOfApp();
            }
            else app.StartBridge(false, true, null);
            var ready = app.Bridge.Services.Readiness;
            foreach (var st in new[] { "setup", "catalog", "studioDb", "scene", PluginHost.ReadinessStage }) ready.Set(st, true);
            app.Stage = AppStage.Ready;
            app.StageName = "done";
            return app;
        }

        /// <summary>
        /// Loads the plugins of <paramref name="pluginsDir"/> into this test host (as -pluginsDir: the environment switch does not
        /// apply; <paramref name="disabled"/> is the disabled list) and returns the loader. Once per host.
        /// </summary>
        public PluginHost LoadPlugins(string pluginsDir, IEnumerable<string> disabled = null)
        {
            if (Plugins != null) throw new InvalidOperationException("the plugins of this host are loaded already");
            var launch = PluginLaunch.Resolve(pluginsDir, false, null, true, null, disabled, HS2Next.Content.ContentSettings.ProjectRoot);
            Plugins = new PluginHost(Bridge?.Server, Paths.UserData, this);
            Plugins.Load(launch);
            Plugins.RaiseAppReady();   // a test host is ready already
            return Plugins;
        }

        /// <summary>
        /// One play-mode frame in the order: the bridge pump, the plugins' Update, Runtime.Apply, CharaMotion.Step of every built
        /// character with AutoStep (pre-order, not in the graveyard), Runtime.LateApply, the bridge's late point.
        /// </summary>
        public void Frame(float dt = 1f / 60f)
        {
            if (disposed) return;
            if (Bridge != null && !Bridge.IsDisposed && Bridge.ManualPump) Bridge.PumpUpdate();
            if (Plugins != null)
            {
                // the plugin pump's place (between the bridge pump and the apply), with the delta the scene clock takes now
                bool paused = Runtime.Paused;
                Plugins.UpdateFrame(Bridge?.Server.Frames.Frame ?? 0, dt, paused ? 0f : dt, Runtime.Clock.Time, paused);
            }
            // the scene clock, edit mode only: a test host's frame begins a clock frame so that
            // routes move and the global pause holds; in play mode the drivers begin it (CharaMotion steps through DeltaSource
            // there), so play-mode hosts stay exactly as before
            bool edit = !UnityEngine.Application.isPlaying;
            if (edit) Runtime.BeginFrame(dt);
            Runtime.Apply();
            float fdt = edit ? Runtime.Clock.FrameDt : dt;
            Runtime.StepCharacters(fdt, false);
            Runtime.StepItems(fdt);   // items after the characters, before LateApply (ItemMotionDriver's place in play mode)
            if (edit) Runtime.RunPost(fdt);   // the scene post stage; in play mode ScenePostDriver has the frame
            Runtime.LateApply();
            if (Bridge != null && !Bridge.IsDisposed && Bridge.ManualPump) Bridge.PumpLate();
        }

        /// <summary>
        /// Executes one request in-process (package B's Execute: the dispatcher path of HTTP without the header checks),
        /// then pumps <see cref="Frame"/> until the reply is there or <paramref name="maxFrames"/> passed (then a timeout
        /// reply written by Core).
        /// </summary>
        public string Call(string requestJson, int maxFrames = 600, float dt = 1f / 60f)
        {
            if (Bridge == null || Bridge.IsDisposed) return "{\"ok\":false,\"code\":\"not_ready\",\"error\":\"the bridge is not running\"}";
            var t = Bridge.Execute(requestJson);
            for (int i = 0; i < maxFrames && !t.IsCompleted; i++) Frame(dt);
            if (!t.IsCompleted) return "{\"ok\":false,\"code\":\"timeout\",\"error\":\"AppCore.Call: no reply within " + maxFrames + " frames\"}";
            if (t.IsFaulted) return "{\"ok\":false,\"code\":\"internal\",\"error\":" + Newtonsoft.Json.JsonConvert.ToString(t.Exception?.GetBaseException().Message) + "}";
            return t.Result;
        }

        // ------------------------------------------------------------ teardown

        /// <summary>Plugins, bridge, runtime, bundles (not for test hosts: their services belong to the test), then Current. Idempotent.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CloseSetupScreen();
            try { Plugins?.Shutdown(); } catch (Exception e) { Debug.LogWarning("[app] plugins shutdown: " + e); }
            try { Bridge?.Dispose(); } catch (Exception e) { Debug.LogWarning("[app] bridge dispose: " + e.Message); }
            try { Runtime?.Dispose(); } catch (Exception e) { Debug.LogWarning("[app] runtime dispose: " + e.Message); }
            importer?.Dispose();
            if (!IsTestHost) CloseContentAtQuit();
            pipeline?.Restore();
            instanceLock?.Dispose();
            instanceLock = null;
            Application.quitting -= OnQuitting;
            // an armed restart (File > Setup guide, Restart now) starts only now, once everything above is released; its helper
            // waits for this process to end before it starts the new one
            if (!IsTestHost) { try { HS2Next.UI.Setup.AppRestart.StartArmed(); } catch (Exception e) { Debug.LogWarning("[app] restart: " + e.Message); } }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
#endif
            if (Current == this) Current = null;
        }

        // The app's quit and the end of an Editor play session: the part thumbnails stop scheduling and finish or drop their
        // loads, then every bundle is closed with Unload(false); the process end, or the scene teardown and the next
        // UnloadUnusedAssets, free the objects loaded from them.
        void CloseContentAtQuit()
        {
            var sw = Stopwatch.StartNew();
            // the background workers stop first (a worker the runtime aborts while it shuts down can crash the process)
            int workers = 0;
            try { workers = HS2Next.UI.Panels.CardNameIndex.StopAllForQuit(500); }
            catch (Exception e) { Debug.LogWarning("[app] quit: card names: " + e.Message); }
            bool checkStopped = true;
            try { if (Catalog != null) checkStopped = Catalog.StopTexturePrescanForQuit(1000); }
            catch (Exception e) { Debug.LogWarning("[app] quit: texture check: " + e.Message); }
            try { HS2Next.UI.Panels.Maker.PartThumbnails.ReleaseAllForQuit("app quit"); }
            catch (Exception e) { Debug.LogWarning("[app] quit: thumbnails: " + e.Message); }
            int closed = 0;
            try { if (Catalog != null) closed = Catalog.UnloadAll(false); }
            catch (Exception e) { Debug.LogWarning("[app] quit: bundles: " + e.Message); }
            Debug.Log($"[app] quit: {closed} bundles closed with Unload(false) in {sw.ElapsedMilliseconds} ms; pending thumbnails at quit: {HS2Next.UI.Panels.Maker.PartThumbnails.PendingAtQuit}; " +
                      $"workers still running: {workers}{(checkStopped ? "" : " + the texture check")}");
        }
    }
}
