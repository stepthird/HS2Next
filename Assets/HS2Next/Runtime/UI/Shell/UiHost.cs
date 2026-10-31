// The UI host (spike S3a / S3b / S2b): a MonoBehaviour on the GameObject "HS2Next UI"
// (child of "HS2Next App"), started by AppCore at the end of its scene stage (UiHost.Start; off with -noUi / ui.enabled
// false and by default in batch mode, -smoke and -bridgeAccept unless -ui). It owns one PanelSettings — an instance of
// the Editor-made asset Resources/UI/HS2NextPanelSettings (CreateInstance has no ICU data; spike S3) — Screen Space
// Overlay, no colour clear, depth / stencil clear, Constant Physical Size (reference / fallback dpi 96), the user scale
// in PanelSettings.scale clamped so the window keeps ≥ 1280×720 logical px; theme Resources/UI/HS2NextDark.tss; one
// PanelRenderer whose root arrives through the UI reload callback (never cached across reloads). Start order: strings
// (Resources/UI/L10n/strings), language (options), fonts, key preset, the shell's actions, the IUiModules (C, I, …),
// then the panel; on each root: the shell, the router, bindings. Update: held keys, viewport (rect, camera), toasts,
// tooltips, status, debounced layout / options writes. LateUpdate: ImeCaretFix. Quit / destroy: layout and options
// written. The bridge's ui.* commands are registered by UiCommands while a host runs.
// StartSetup (Phase 5.3) starts the setup mode instead: the first-run guide or the progress page of a long catalog scan, on a
// panel of its own with the same panel settings, theme, fonts and strings, before the Studio UI exists (Setup.SetupHost).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using HS2Next.App;
using HS2Next.Scenes;
using HS2Next.UI.Core;
using HS2Next.UI.Input;
using HS2Next.UI.Viewport;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace HS2Next.UI
{
    [DisallowMultipleComponent]
    public sealed class UiHost : MonoBehaviour
    {
        public const string ObjectName = "HS2Next UI";
        public const string PanelSettingsResource = "UI/HS2NextPanelSettings";
        public const string ThemeResource = "UI/HS2NextDark";
        public const string StringsResource = "UI/L10n/strings";
        public const string GizmoMaterialResource = "UI/HS2NextGizmo";
        public const int MinLogicalWidth = 1280, MinLogicalHeight = 720;

        /// <summary>The running host (null when the UI is off).</summary>
        public static UiHost Current { get; private set; }
        /// <summary>Raised once per host when its first root is built (tests, plugins).</summary>
        public static event Action<UiHost> Ready;

        public UiContext Context { get; private set; }
        public PanelSettings Settings { get; private set; }
        public PanelRenderer Renderer { get; private set; }
        public ShortcutRouter Router { get; private set; }
        public ImeCaretFix CaretFix { get; private set; }
        public VisualElement Root { get; private set; }
        public bool IsReady { get; private set; }
        /// <summary>Milliseconds from Start to the first built root (the "first UI frame" timing line).</summary>
        public double FirstFrameMs { get; private set; } = -1;
        /// <summary>The effective panel scale factor applied (user scale × clamp).</summary>
        public float UserScaleApplied { get; private set; } = 1f;
        public string StartProblem { get; private set; }
        /// <summary>The gizmo pass (view camera only).</summary>
        public Gizmos.GizmoPass GizmoPass { get; private set; }

        AppCore app;
        Stopwatch startWatch;
        int uiVersion = -1;
        int lastW, lastH;

        /// <summary>Starts the UI of an app (one call in AppCore's scene stage). Never throws; null when the UI is off.</summary>
        public static UiHost Start(AppCore app, Transform parent)
        {
            if (app == null) return null;
            try
            {
                if (app.Settings == null || !app.Settings.Ui.Run)
                {
                    Debug.Log("[ui] off (" + (app.Settings == null ? "no settings" : app.Settings.Ui.Off ? "-noUi" : "ui.enabled false or batch / headless; -ui turns it on") + ")");
                    return null;
                }
                if (Current != null) Destroy(Current.gameObject);
                var go = new GameObject(ObjectName);
                go.SetActive(false);
                if (parent) go.transform.SetParent(parent, false);
                var h = go.AddComponent<UiHost>();
                h.app = app;
                go.SetActive(true);
                return h;
            }
            catch (Exception e)
            {
                Debug.LogError("[ui] start failed: " + e);
                return null;
            }
        }

        /// <summary>
        /// Starts the UI in setup mode (the setup boot stage, or a long catalog scan): the first-run guide, or with
        /// <c>context.ProgressOnly</c> a single progress page, as the only root of its own panel (no shell, no scene runtime).
        /// Never throws; null when the UI is off or the host could not start.
        /// </summary>
        public static Setup.SetupHost StartSetup(AppCore app, Transform parent, Setup.SetupContext context)
        {
            if (app == null || context == null) return null;
            if (app.Settings == null || !app.Settings.Ui.Run)
            {
                Debug.Log("[setup] no setup UI (the UI is off)");
                return null;
            }
            return Setup.SetupHost.Create(context, parent);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Current = null; Ready = null; }

        void Awake() => Current = this;

        void OnEnable()
        {
            if (app == null || Context != null) return;
            startWatch = Stopwatch.StartNew();
            try { Init(); }
            catch (Exception e)
            {
                StartProblem = e.GetType().Name + ": " + e.Message;
                Debug.LogError("[ui] init failed: " + e);
            }
        }

        void Init()
        {
            var ctx = Context = new UiContext(app) { Host = this };
            // strings
            var ta = Resources.Load<TextAsset>(StringsResource);
            if (ta != null) L10n.LoadText(ta.text);
            else ctx.Findings.Error("ui.l10nMissing", null, "Resources/" + StringsResource + " not found (strings.tsv importer)");
            L10n.Default.Missing += (key, lang) => ctx.Findings.Info("ui.l10nMissing", null, "missing text " + key + " (" + lang + ")");
            ColorPicker.ColorsFile = System.IO.Path.Combine(ctx.Options.ConfigDir, "colors.json");
            // language, fonts, keys
            ctx.Fonts = UiFonts.Load();
            if (!ctx.Fonts.Loaded) ctx.Findings.Error("ui.fontMissing", null, ctx.Fonts.Problem);
            L10n.SetLanguage(ctx.Options.String(OptionDefs.Language));
            ctx.Keys.Use(ctx.Options.String(OptionDefs.KeyPreset));
            ctx.Options.Changed += OnOption;
            ApplyPlacement();
            // viewport (before the actions: camera actions use it), actions, modules
            ctx.Viewport = new ViewportHost(ctx);
            ctx.Viewport.Gizmo = new Gizmos.TransformTool(ctx);
            Router = new ShortcutRouter(ctx);
            CaretFix = new ImeCaretFix(ctx);
            UiActions.Register(ctx);
            HistoryPanel.Register(ctx);
            foreach (var m in UiModules.Create())
            {
                try { m.Register(ctx); }
                catch (Exception e) { Debug.LogError("[ui] module " + m.GetType().FullName + " failed to register: " + e); }
            }
            // panel
            var asset = Resources.Load<PanelSettings>(PanelSettingsResource);
            if (asset != null) Settings = Instantiate(asset);
            else
            {
                Settings = ScriptableObject.CreateInstance<PanelSettings>();
                ctx.Findings.Warning("ui.panelSettingsMissing", null, "Resources/" + PanelSettingsResource + " not found: a run-time PanelSettings is used (no ICU data)");
            }
            Settings.name = "HS2Next PanelSettings";
            var theme = Resources.Load<ThemeStyleSheet>(ThemeResource);
            if (theme != null) Settings.themeStyleSheet = theme;
            else ctx.Findings.Error("ui.themeMissing", null, "Resources/" + ThemeResource + " not found");
            Settings.renderMode = PanelRenderMode.ScreenSpaceOverlay;
            Settings.sortingOrder = 0;
            Settings.clearColor = false;
            Settings.clearDepthStencil = true;
            Settings.scaleMode = PanelScaleMode.ConstantPhysicalSize;
            Settings.referenceDpi = 96;
            Settings.fallbackDpi = 96;
            ApplyScale();
            Renderer = gameObject.AddComponent<PanelRenderer>();
            Renderer.panelSettings = Settings;
            Renderer.RegisterUIReloadCallback(OnUiReload);
            Bridge.UiCommands.Attach(this);
            // gizmo pass: after post-processing, view camera only (never in captures)
            var gm = Resources.Load<Material>(GizmoMaterialResource);
            if (gm == null)
            {
                var sh = Shader.Find("HS2Next/UI/Gizmo");
                if (sh != null) gm = new Material(sh);
                else ctx.Findings.Warning("ui.gizmoShaderMissing", null, "HS2Next/UI/Gizmo shader not found: no gizmos");
            }
            GizmoPass = new Gizmos.GizmoPass { Always = gm, Depth = gm };
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        /// <summary>Tests only: draw the gizmos also when the view camera renders into a target texture.</summary>
        public bool GizmosIntoTargets;

        /// <summary>Gizmo build time of the last view-camera frame (ms; U-M3).</summary>
        public double LastGizmoMs { get; private set; }

        void OnBeginCamera(UnityEngine.Rendering.ScriptableRenderContext rc, Camera cam)
        {
            var ctx = Context;
            if (ctx == null || !IsReady || GizmoPass?.Always == null || ctx.Viewport == null) return;
            if (cam != ctx.Viewport.ViewCamera) return;
            // captures render the view camera into a target texture (ViewCapture, thumbnails): never with gizmos
            if (cam.targetTexture != null && !GizmosIntoTargets) return;
            try
            {
                var sw = Stopwatch.StartNew();
                ctx.Viewport.Gizmo.Build(GizmoPass, cam);
                LastGizmoMs = sw.Elapsed.TotalMilliseconds;
                if (GizmoPass.Count > 0)
                {
                    var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
                    data?.scriptableRenderer?.EnqueuePass(GizmoPass);
                }
            }
            catch (Exception e) { LogOnce("gizmo", e); }
        }

        /// <summary>
        /// A UI context without a host or panel (edit-mode tests, UiCoreTest): strings, fonts, key preset, the shell's actions,
        /// every IUiModule, and the shell built on a detached root element (layout from defaults; no files read or written
        /// beyond options / layout of the app's UserData). Panels and widgets can be built off-panel from it.
        /// </summary>
        public static UiContext BuildOffPanel(AppCore app, VisualElement root = null)
        {
            var ctx = new UiContext(app);
            var ta = Resources.Load<TextAsset>(StringsResource);
            if (ta != null) L10n.LoadText(ta.text);
            ctx.Fonts = UiFonts.Load();
            L10n.SetLanguage(ctx.Options.String(OptionDefs.Language));
            ctx.Keys.Use(ctx.Options.String(OptionDefs.KeyPreset));
            ctx.Viewport = new ViewportHost(ctx);
            ctx.Viewport.Gizmo = new Gizmos.TransformTool(ctx);
            UiActions.Register(ctx);
            HistoryPanel.Register(ctx);
            foreach (var m in UiModules.Create())
            {
                try { m.Register(ctx); }
                catch (Exception e) { Debug.LogError("[ui] module " + m.GetType().FullName + " failed to register: " + e); }
            }
            ctx.Shell = new UiShell(ctx, root ?? new VisualElement { name = "offpanel-root" });
            ctx.Shell.Build();
            return ctx;
        }

        void OnUiReload(PanelRenderer r, VisualElement rootElement, int version)
        {
            if (version == uiVersion || Context == null) return;
            uiVersion = version;
            Root = rootElement;
            var ctx = Context;
            ctx.Shell = new UiShell(ctx, rootElement);
            ctx.Shell.Build();
            ctx.Fonts.Apply(rootElement, ctx.L10n.Language);
            Router.Attach(rootElement);
            ImeCommitFix.Attach(rootElement);   // the caret after an IME conversion (the committed text before the composition ends)
            UiDefer.Frames(2, () => ctx.Viewport.Element.Focus());
            if (!IsReady)
            {
                IsReady = true;
                FirstFrameMs = startWatch?.Elapsed.TotalMilliseconds ?? -1;
                Debug.Log("[ui] ready in " + FirstFrameMs.ToString("0") + " ms (language " + ctx.L10n.Language + ", preset " + ctx.Keys.PresetName +
                          ", " + ctx.Registry.Panels.All.Count + " panels, " + System.Linq.Enumerable.Count(ctx.Registry.Actions.All) + " actions, fonts " + (ctx.Fonts.Loaded ? "bundled" : "OS") + ")");
                UiContext.Guard(() => Ready?.Invoke(this));
            }
        }

        // ---------------- options that the host applies

        void OnOption(string key)
        {
            if (key == OptionDefs.Language) SetLanguage(Context.Options.String(OptionDefs.Language), save: false);
            else if (key == OptionDefs.UiScale) ApplyScale();
            else if (key == OptionDefs.KeyPreset) Context.Keys.Use(Context.Options.String(OptionDefs.KeyPreset));
            else if (key == OptionDefs.AddPlacement) ApplyPlacement();
        }

        void ApplyPlacement()
        {
            Context.Session.Options.NewNodePlacement = Context.Options.String(OptionDefs.AddPlacement) == "origin" ? NewNodePlacement.Origin : NewNodePlacement.CameraPivot;
        }

        /// <summary>Switches the language at run time (strings, fonts); with save the option is written.</summary>
        public void SetLanguage(string language, bool save)
        {
            var ctx = Context;
            var sw = Stopwatch.StartNew();
            string set = L10n.SetLanguage(language);
            if (save && ctx.Options.String(OptionDefs.Language) != set) { ctx.Options.Layers.File.Set(OptionDefs.Language, set); ctx.Options.Save(); }
            ctx.Fonts.Apply(Root, set);
            int n = Loc.Refresh();
            ctx.Shell?.Toolbar?.Refresh();
            ctx.Shell?.StatusBar?.Refresh();
            LastLanguageSwitchMs = sw.Elapsed.TotalMilliseconds;
            ctx.RaiseView("language");
            Debug.Log("[ui] language " + set + ": " + n + " texts in " + LastLanguageSwitchMs.ToString("0.0") + " ms");
        }

        public double LastLanguageSwitchMs { get; private set; }

        /// <summary>The user scale (general.uiScale %) in PanelSettings.scale, clamped so the window keeps ≥ 1280×720 logical px.</summary>
        void ApplyScale()
        {
            if (Settings == null) return;
            float user = Mathf.Clamp(Context.Options.Int(OptionDefs.UiScale), 75, 200) / 100f;
            float dpiScale = Mathf.Max(0.5f, (Screen.dpi > 0 ? Screen.dpi : 96f) / 96f);
            float maxW = Screen.width / (float)MinLogicalWidth, maxH = Screen.height / (float)MinLogicalHeight;
            float max = Mathf.Min(maxW, maxH) / dpiScale;
            float s = Mathf.Clamp(user, 0.25f, Mathf.Max(0.25f, max));
            UserScaleApplied = s;
            if (!Mathf.Approximately(Settings.scale, s)) Settings.scale = s;
            lastW = Screen.width; lastH = Screen.height;
        }

        // ---------------- frame

        /// <summary>CPU of the host's own per-frame work (Update + LateUpdate + gizmo build) in the last frame, ms (U-M3).</summary>
        public double LastHostMs => lastUpdateMs + lastLateMs + LastGizmoMs;
        double lastUpdateMs, lastLateMs;
        readonly Stopwatch frameWatch = new Stopwatch();

        void Update()
        {
            frameWatch.Restart();
            try { UpdateInner(); }
            finally { lastUpdateMs = frameWatch.Elapsed.TotalMilliseconds; }
        }

        void UpdateInner()
        {
            UiDefer.Pump();
            var ctx = Context;
            if (ctx == null || !IsReady) return;
            if (Screen.width != lastW || Screen.height != lastH) ApplyScale();
            float dt = Time.unscaledDeltaTime;
            try
            {
                Router.Poll(dt);
                ctx.Viewport.Update(dt);
                ctx.Shell.Tick();
                ctx.Options.Tick();
                Bridge.UiCommands.Tick(this);
            }
            catch (Exception e) { LogOnce("update", e); }
        }

        void LateUpdate()
        {
            frameWatch.Restart();
            try { LateInner(); }
            finally { lastLateMs = frameWatch.Elapsed.TotalMilliseconds; }
        }

        void LateInner()
        {
            if (Context == null || !IsReady) return;
            try
            {
                CaretFix.LateUpdate();
                Context.Viewport.SyncRect();
            }
            catch (Exception e) { LogOnce("late", e); }
        }

        readonly HashSet<string> logged = new HashSet<string>();
        void LogOnce(string where, Exception e)
        {
            if (logged.Add(where + e.GetType().Name)) Debug.LogError("[ui] " + where + ": " + e);
        }

        /// <summary>The viewport's right-click menu (no camera drag happened).</summary>
        public void ShowViewportMenu(Vector2 at)
        {
            var ctx = Context;
            var items = new List<MenuItem>
            {
                ctx.Shell.Toolbar.Item(ActionIds.CameraFrame), ctx.Shell.Toolbar.Item(ActionIds.CameraReset), MenuItem.Sep(),
                ctx.Shell.Toolbar.Item(ActionIds.Duplicate), ctx.Shell.Toolbar.Item(ActionIds.Delete), ctx.Shell.Toolbar.Item(ActionIds.ViewHideSelected),
                ctx.Shell.Toolbar.Item(ActionIds.ViewShowAll), MenuItem.Sep(), ctx.Shell.Toolbar.Item(ActionIds.ViewPhysics),
            };
            ctx.Shell.Menus.Show(items, null, at, "viewport-menu");
        }

        void OnApplicationQuit() => Flush();

        void Flush()
        {
            try { Context?.Shell?.Flush(); Context?.Options.Flush(); }
            catch (Exception e) { Debug.LogWarning("[ui] flush: " + e.Message); }
        }

        void OnDestroy()
        {
            Flush();
            Bridge.UiCommands.Detach(this);
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            Context?.Viewport?.Dispose();
            Context?.Dispose();
            if (Settings != null) Destroy(Settings);
            if (Current == this) Current = null;
        }
    }
}
