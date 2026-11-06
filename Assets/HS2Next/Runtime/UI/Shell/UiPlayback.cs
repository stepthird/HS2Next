// The global pause in the UI (Phase 3.6 package K): the toolbar's playback group
// (hidden in 3.5; between the camera and view menus): Pause / Resume toggle (action time.pause),
// Step (action time.step: one step of the option time.stepDt, default 1/60; ui.action args {n, dt}) and the scene-time
// read-out (mm:ss.ff); the status bar's "Paused" item and a small "Paused" badge in the viewport's corner (UI overlay:
// never in captures). The actions are registered by the module below (Order 40, after the shell's own actions); Space is
// bound to time.pause in both key presets by the UI core (package P), time.step is unbound. The pause itself is
// SceneRuntime.SetPaused / Step (the scene clock); the UI, the camera, gizmos and edits keep working while paused.
using System;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI
{
    public sealed class UiPlaybackModule : IUiModule
    {
        public const string PauseId = "time.pause", StepId = "time.step", StepDtOption = "time.stepDt";

        public int Order => 40;

        public void Register(UiContext ctx)
        {
            var a = ctx.Registry.Actions;
            if (ActionManifest.Contains(PauseId))
            {
                var d = a.Register(PauseId, call =>
                {
                    var rt = ctx.Runtime;
                    if (rt == null) return null;
                    bool want = call.Arg("paused") is bool p ? p : !rt.Paused;
                    rt.SetPaused(want);
                    return rt.Paused;
                }, () => ctx.Runtime != null);
                d.IsChecked = () => ctx.Runtime != null && ctx.Runtime.Paused;
            }
            else Debug.LogWarning("[ui] time.pause is not in the action manifest yet: the playback group has no pause action");
            if (ActionManifest.Contains(StepId))
            {
                a.Register(StepId, call =>
                {
                    var rt = ctx.Runtime;
                    if (rt == null || !rt.Paused) return null;
                    // Phase 6.2: no step while frames are recorded (a step runs inside one frame and could not be recorded)
                    if (HS2Next.Screenshots.FrameRecorder.IsRecording) { ctx.Toasts?.Show(L10n.Get("toast.stepRecording"), HS2Next.UI.Widgets.ToastKind.Warning); return "busy: a recording is running"; }
                    int n = call.Arg("n") is int ni ? ni : call.Arg("n") is long nl ? (int)nl : call.Arg("n") is double nd ? (int)nd : 1;
                    float dt = call.Arg("dt") is double dd ? (float)dd : call.Arg("dt") is float df ? df : StepDt(ctx);
                    return rt.Step(n, dt) ? (object)rt.Clock.Time : null;
                }, () => ctx.Runtime != null && ctx.Runtime.Paused);
            }
            else Debug.LogWarning("[ui] time.step is not in the action manifest yet: the playback group has no step action");
        }

        /// <summary>The step button's dt: the option time.stepDt (0 &lt; dt ≤ 1), else 1/60.</summary>
        public static float StepDt(UiContext ctx)
        {
            float v = ctx.Options.Float(StepDtOption);
            return v > 0f && v <= 1f ? v : 1f / 60f;
        }
    }

    /// <summary>The toolbar's playback group, the status-bar item and the viewport badge.</summary>
    public sealed class UiPlayback
    {
        readonly UiContext ctx;
        public Button Pause { get; }
        public Button Step { get; }
        public Label Time { get; }
        Label status, badge;
        SceneRuntimeHook hook;

        /// <summary>Builds the group into <paramref name="toolbar"/> (the toolbar's element, at the current position).</summary>
        public UiPlayback(UiContext ctx, VisualElement toolbar)
        {
            this.ctx = ctx;
            Pause = new Button(() => ctx.Registry.Run(UiPlaybackModule.PauseId, "toolbar")) { name = "tb-" + UiPlaybackModule.PauseId, text = "❚❚" };
            Pause.AddToClassList("hs-toolbar__button");
            Loc.Tooltip(Pause, ActionIds.LabelKey(UiPlaybackModule.PauseId));
            Step = new Button(() => ctx.Registry.Run(UiPlaybackModule.StepId, "toolbar")) { name = "tb-" + UiPlaybackModule.StepId, text = "▶❙" };
            Step.AddToClassList("hs-toolbar__button");
            Loc.Tooltip(Step, ActionIds.LabelKey(UiPlaybackModule.StepId));
            Time = new Label { name = "tb-scene-time", pickingMode = PickingMode.Ignore };
            Time.AddToClassList("hs-toolbar__time");
            Time.style.unityTextAlign = TextAnchor.MiddleLeft;
            Time.style.minWidth = 64;
            Time.style.marginLeft = 4;
            toolbar.Add(Pause);
            toolbar.Add(Step);
            toolbar.Add(Time);
            hook = new SceneRuntimeHook(this);
            Time.schedule.Execute(Refresh).Every(100);
            Refresh();
        }

        /// <summary>Adds the status bar's "Paused" item (called by UiStatusBar).</summary>
        public static Label StatusItem(UiContext ctx, VisualElement statusBar)
        {
            var l = new Label { name = "status-paused", pickingMode = PickingMode.Ignore };
            l.AddToClassList("hs-statusbar__item");
            l.AddToClassList("hs-statusbar__paused");
            Loc.Bind(l, "status.paused");
            l.style.display = ctx.Runtime != null && ctx.Runtime.Paused ? DisplayStyle.Flex : DisplayStyle.None;
            statusBar.Add(l);
            return l;
        }

        /// <summary>mm:ss.ff of scene seconds.</summary>
        public static string Format(double t)
        {
            if (double.IsNaN(t) || t < 0) t = 0;
            long cs = (long)Math.Floor(t * 100.0 + 1e-6);
            long m = cs / 6000, s = cs / 100 % 60, f = cs % 100;
            return m.ToString("00") + ":" + s.ToString("00") + "." + f.ToString("00");
        }

        public void Refresh()
        {
            var rt = ctx.Runtime;
            bool paused = rt != null && rt.Paused;
            Pause.text = paused ? "▶" : "❚❚";
            Pause.EnableInClassList("hs-toolbar__button--on", paused);
            Pause.SetEnabled(rt != null && ctx.Registry.Actions.Contains(UiPlaybackModule.PauseId));
            Step.SetEnabled(paused && ctx.Registry.Actions.Contains(UiPlaybackModule.StepId) && !HS2Next.Screenshots.FrameRecorder.IsRecording);
            Time.text = rt != null ? Format(rt.Clock.Time) : "";
            if (status == null || status.panel == null) status = ctx.Shell?.StatusBar?.Element?.Q<Label>("status-paused") ?? status;
            if (status != null) status.style.display = paused ? DisplayStyle.Flex : DisplayStyle.None;
            EnsureBadge();
            if (badge != null) badge.style.display = paused ? DisplayStyle.Flex : DisplayStyle.None;
            hook.Follow(rt);
        }

        void EnsureBadge()
        {
            var vp = ctx.Viewport?.Element;
            if (vp == null) return;
            if (badge != null && badge.parent == vp) return;
            badge = vp.Q<Label>("viewport-paused");
            if (badge == null)
            {
                badge = new Label { name = "viewport-paused", pickingMode = PickingMode.Ignore };
                badge.AddToClassList("hs-viewport__paused");
                var s = badge.style;
                s.position = Position.Absolute;
                s.right = 8;
                s.top = 8;
                s.paddingLeft = s.paddingRight = 6;
                s.paddingTop = s.paddingBottom = 2;
                s.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
                s.color = new Color(1f, 0.85f, 0.3f, 1f);
                s.borderTopLeftRadius = s.borderTopRightRadius = s.borderBottomLeftRadius = s.borderBottomRightRadius = 3;
                Loc.Bind(badge, "viewport.paused");
                vp.Add(badge);
            }
        }

        /// <summary>Follows the app's runtime (it is replaced on reload) for immediate refreshes.</summary>
        sealed class SceneRuntimeHook
        {
            readonly UiPlayback owner;
            HS2Next.Scenes.Runtime.SceneRuntime rt;
            public SceneRuntimeHook(UiPlayback o) { owner = o; }

            public void Follow(HS2Next.Scenes.Runtime.SceneRuntime r)
            {
                if (r == rt) return;
                if (rt != null) { rt.PausedChanged -= OnPaused; rt.Stepped -= OnStepped; }
                rt = r;
                if (rt != null) { rt.PausedChanged += OnPaused; rt.Stepped += OnStepped; }
            }

            void OnPaused(bool _) => owner.Refresh();
            void OnStepped(int n, float dt) => owner.Refresh();
        }
    }
}
