// The UI host in setup mode: the first-run guide (or, during a long catalog scan, a single progress page) as the only root of
// its own panel - the same panel settings, theme, fonts, strings and language as the Studio UI, without the shell or the scene
// runtime (neither exists yet when the setup stage runs). AppCore's setup stage starts it through UiHost.StartSetup and yields
// frames until the guide has written the settings (the boot then builds the index while the guide shows its progress), was
// skipped, or asked to quit; the index page turns into the ready page when the Studio lists are ready, and the guide closes
// when the user finishes it. The panel sorts above the Studio UI, so the Studio can start underneath. The bridge's ui.state,
// ui.query, ui.action and ui.input drive it while it runs (Bridge.UiCommands).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using HS2Next.App.Setup;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace HS2Next.UI.Setup
{
    /// <summary>What the guide wants of the boot.</summary>
    public enum SetupOutcome
    {
        /// <summary>Still on pages 0–4.</summary>
        None,
        /// <summary>The settings are written: build the index (the guide shows its progress, then the ready page).</summary>
        Continue,
        /// <summary>Skipped: start without HS2 content.</summary>
        Skipped,
        /// <summary>Quit: end the app (the guide resumes at the next start).</summary>
        Quit,
    }

    /// <summary>A copy offer of an earlier UserData folder (the migration), as page 1 shows it.</summary>
    public sealed class MigrationOfferInfo
    {
        public string Source;
        public int Files;
        public long Bytes;
    }

    /// <summary>The result of a copy, as page 1 shows it.</summary>
    public sealed class MigrationResultInfo
    {
        public int Copied, Same, Conflicts, Refused;
        public long Bytes;
        /// <summary>Set when nothing could be copied (for example too little free space).</summary>
        public string Error;
    }

    /// <summary>What the boot gives the guide (folders, current values, the migration).</summary>
    public sealed class SetupContext
    {
        /// <summary>editor | portable | override | perUser.</summary>
        public string Mode = "editor";
        public string DataRoot, ExeDir, UserData, SettingsFile, DefaultCacheDir;
        public string CurrentHs2Root, CurrentCacheDir;
        public string[] CurrentModsFolders = Array.Empty<string>();
        public string Version;
        public string[] Args = Array.Empty<string>();
        /// <summary>The settings file's `ui` block (the language of the dev defaults), or null.</summary>
        public Newtonsoft.Json.Linq.JObject UiBlock;
        public SetupStage.Decision Decision;
        /// <summary>The copy offer of this start, or null (none applies).</summary>
        public Func<MigrationOfferInfo> MigrationOffer;
        /// <summary>Copies from a source on a worker thread, reporting (done, total) files; null when the boot offers no migration.</summary>
        public Func<string, Action<int, int>, MigrationResultInfo> RunMigration;
        /// <summary>A progress page only (a long catalog scan without the guide).</summary>
        public bool ProgressOnly;
        /// <summary>The bridge token file (shown on the ready page), or null.</summary>
        public string TokenFile;
    }

    [DisallowMultipleComponent]
    public sealed class SetupHost : MonoBehaviour
    {
        public const string ObjectName = "HS2Next Setup";
        public const int SortingOrder = 100;
        public const string StyleResource = "UI/F/setup";

        /// <summary>The running setup host (null when none).</summary>
        public static SetupHost Current { get; private set; }

        public SetupContext Context { get; private set; }
        public SetupGuide Guide { get; private set; }
        public PanelSettings Settings { get; private set; }
        public PanelRenderer Renderer { get; private set; }
        public VisualElement Root { get; private set; }
        public Dialogs Dialogs { get; private set; }
        public Toasts Toasts { get; private set; }
        public UiFonts Fonts { get; private set; }
        public bool IsReady { get; private set; }
        public double FirstFrameMs { get; private set; } = -1;
        public string StartProblem { get; private set; }
        /// <summary>The user finished the ready page (or the progress page closed): the host can go.</summary>
        public bool Closed { get; private set; }

        public SetupOutcome Outcome => Guide?.Outcome ?? SetupOutcome.None;
        public string Page => Guide?.Page;

        Stopwatch startWatch;
        int uiVersion = -1;

        /// <summary>Creates the host (never throws; null on failure).</summary>
        internal static SetupHost Create(SetupContext context, Transform parent)
        {
            try
            {
                if (Current != null) Destroy(Current.gameObject);
                var go = new GameObject(ObjectName);
                go.SetActive(false);
                if (parent) go.transform.SetParent(parent, false);
                var h = go.AddComponent<SetupHost>();
                h.Context = context;
                go.SetActive(true);
                return h;
            }
            catch (Exception e)
            {
                Debug.LogError("[setup] start failed: " + e);
                return null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Current = null;

        void Awake() => Current = this;

        void OnEnable()
        {
            if (Context == null || Settings != null) return;
            startWatch = Stopwatch.StartNew();
            try { Init(); }
            catch (Exception e)
            {
                StartProblem = e.GetType().Name + ": " + e.Message;
                Debug.LogError("[setup] init failed: " + e);
            }
        }

        void Init()
        {
            var ta = Resources.Load<TextAsset>(UiHost.StringsResource);
            if (ta != null) L10n.LoadText(ta.text);
            else Debug.LogWarning("[setup] Resources/" + UiHost.StringsResource + " not found");
            Fonts = UiFonts.Load();
            L10n.SetLanguage(SetupLanguage.Initial(Context));
            var asset = Resources.Load<PanelSettings>(UiHost.PanelSettingsResource);
            Settings = asset != null ? Instantiate(asset) : ScriptableObject.CreateInstance<PanelSettings>();
            Settings.name = "HS2Next Setup PanelSettings";
            var theme = Resources.Load<ThemeStyleSheet>(UiHost.ThemeResource);
            if (theme != null) Settings.themeStyleSheet = theme;
            Settings.renderMode = PanelRenderMode.ScreenSpaceOverlay;
            Settings.sortingOrder = SortingOrder;
            Settings.clearColor = false;
            Settings.clearDepthStencil = true;
            Settings.scaleMode = PanelScaleMode.ConstantPhysicalSize;
            Settings.referenceDpi = 96;
            Settings.fallbackDpi = 96;
            float dpiScale = Mathf.Max(0.5f, (Screen.dpi > 0 ? Screen.dpi : 96f) / 96f);
            float max = Mathf.Min(Screen.width / (float)UiHost.MinLogicalWidth, Screen.height / (float)UiHost.MinLogicalHeight) / dpiScale;
            Settings.scale = Mathf.Clamp(1f, 0.25f, Mathf.Max(0.25f, max));
            Renderer = gameObject.AddComponent<PanelRenderer>();
            Renderer.panelSettings = Settings;
            Renderer.RegisterUIReloadCallback(OnUiReload);
            HS2Next.UI.Bridge.UiCommands.AttachSetup(this);
        }

        void OnUiReload(PanelRenderer r, VisualElement rootElement, int version)
        {
            if (version == uiVersion || Context == null) return;
            uiVersion = version;
            Root = rootElement;
            Root.Clear();
            Root.AddToClassList("hs-root");
            Root.AddToClassList("hs-setup-root");
            Root.style.flexGrow = 1;
            var sheet = Resources.Load<StyleSheet>(StyleResource);
            if (sheet != null) Root.styleSheets.Add(sheet);
            var keep = Guide;
            Guide = keep ?? new SetupGuide(this);
            Root.Add(Guide);
            var overlay = new VisualElement { name = "setup.overlay", pickingMode = PickingMode.Ignore };
            overlay.AddToClassList("hs-overlay");
            Root.Add(overlay);
            var toasts = new VisualElement { name = "setup.toasts" };
            overlay.Add(toasts);
            Toasts = new Toasts(toasts);
            var dl = new VisualElement { name = "setup.dialogs" };
            Root.Add(dl);
            Dialogs = new Dialogs(dl);
            Root.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.NoTrickleDown);
            HS2Next.UI.Input.ImeCommitFix.Attach(Root);   // the caret after an IME conversion in the guide's text fields
            Fonts?.Apply(Root, L10n.Language);
            if (!IsReady)
            {
                IsReady = true;
                FirstFrameMs = startWatch?.Elapsed.TotalMilliseconds ?? -1;
                Debug.Log("[setup] ready in " + FirstFrameMs.ToString("0") + " ms (page " + Guide.Page + ", language " + L10n.Language + (Context.ProgressOnly ? ", progress only" : "") + ")");
            }
            UiDefer.Frames(2, () => Guide?.FocusDefault());
        }

        void OnKey(KeyDownEvent e)
        {
            if (Dialogs != null && Dialogs.IsModalOpen)
            {
                if (e.keyCode == KeyCode.Escape) { Dialogs.Cancel(); e.StopPropagation(); }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { if (!(e.target is TextField)) { Dialogs.Accept(); e.StopPropagation(); } }
                return;
            }
            if (e.keyCode == KeyCode.Escape) { Guide?.Back(); e.StopPropagation(); }
            else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { Guide?.PressDefault(); e.StopPropagation(); }
        }

        /// <summary>The focused element of the setup panel, or null.</summary>
        public VisualElement Focused => Root?.panel?.focusController?.focusedElement as VisualElement;

        void Update()
        {
            if (UiHost.Current == null) UiDefer.Pump();   // the Studio host pumps it once it runs
            try
            {
                Toasts?.Tick();
                Guide?.Tick();
            }
            catch (Exception e) { if (logged.Add(e.GetType().Name)) Debug.LogError("[setup] update: " + e); }
        }

        readonly HashSet<string> logged = new HashSet<string>();

        // ---------------- the boot's side

        /// <summary>The catalog scan's progress (page 5): zipmods visited, the expected total (≤ 0 unknown), the elapsed seconds.</summary>
        public void Progress(int visited, int total, double seconds) => Guide?.SetProgress(visited, total, seconds);

        /// <summary>The Studio lists are ready: the index page becomes the ready page (a progress-only host closes).</summary>
        public void IndexDone()
        {
            if (Context.ProgressOnly) { Close(); return; }
            Guide?.ShowReady();
        }

        /// <summary>The boot failed or was stopped while the guide showed the index: the page says so.</summary>
        public void BootFailed(string message) => Guide?.ShowFailure(message);

        /// <summary>Closes the host (the guide finished or the progress page is no longer needed).</summary>
        public void Close()
        {
            if (Closed) return;
            Closed = true;
            Debug.Log("[setup] closed (page " + Page + ")");
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            HS2Next.UI.Bridge.UiCommands.DetachSetup(this);
            if (Settings != null) Destroy(Settings);
            if (Current == this) Current = null;
            Closed = true;
        }
    }

    /// <summary>The language of the setup panel: the command line, options.json, the settings' `ui` block, the system language.</summary>
    public static class SetupLanguage
    {
        public static readonly string[] Codes = { "en", "ja", "zh-Hans" };

        public static string Initial(SetupContext c)
        {
            var a = c.Args ?? Array.Empty<string>();
            int i = Array.IndexOf(a, "-uiLanguage");
            if (i >= 0 && i + 1 < a.Length) return a[i + 1];
            var doc = ReadOptions(c.UserData);
            if (doc != null && doc.Get(OptionDefs.Language) is string fromFile && fromFile.Length > 0) return fromFile;
            string block = (string)c.UiBlock?["language"];
            if (!string.IsNullOrEmpty(block)) return block;
            return FromSystem(Application.systemLanguage);
        }

        public static string FromSystem(SystemLanguage l) =>
            l == SystemLanguage.Japanese ? "ja" : l == SystemLanguage.ChineseSimplified || l == SystemLanguage.Chinese ? "zh-Hans" : "en";

        static string OptionsPath(string userData) => System.IO.Path.Combine(userData ?? "", "config", OptionsDocument.FileName);

        static OptionsDocument ReadOptions(string userData)
        {
            try
            {
                string f = OptionsPath(userData);
                return System.IO.File.Exists(f) ? OptionsDocument.Parse(System.IO.File.ReadAllText(f)) : null;
            }
            catch (Exception) { return null; }
        }

        /// <summary>Stores the language in options.json (general.language), as the Options panel does.</summary>
        public static void Store(string userData, string language)
        {
            try
            {
                var doc = ReadOptions(userData) ?? new OptionsDocument();
                doc.Set(OptionDefs.Language, language);
                SettingsFile.WriteAtomic(OptionsPath(userData), doc.ToJson());
            }
            catch (Exception e) { Debug.LogWarning("[setup] options.json not written: " + e.Message); }
        }
    }
}
