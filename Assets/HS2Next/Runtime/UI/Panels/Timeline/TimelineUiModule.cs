// The Timeline in the Studio UI: the panel "timeline" (default region bottom), the actions timeline.panel (open the panel's
// region with the panel shown, or collapse it when it is the shown panel; View menu, a toggle at the end of the toolbar's
// playback group), timeline.playPause, timeline.stop (args {bake}), timeline.key (the selected tracks at the play head),
// timeline.export (the Export frames dialog) — all unbound by default — the status-bar item while the scene has a Timeline
// ("Timeline ▶ 00:03.200", «Recording N frames» while frames are recorded; a click opens the panel) and the toast at the end
// of a recording started from the UI (with «Open folder»).
// The bottom region stays collapsed until the user opens the panel: the panel is registered at start only when layout.json
// already lists it (the user opened it before), otherwise by the first timeline.panel; a «Reset layout» that would open the
// bottom region for the panel alone leaves it collapsed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using HS2Next.Scenes.Timeline;
using HS2Next.Screenshots;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class TimelineUiModule : IUiModule
    {
        public const string PanelId = "timeline";
        public const string TitleKey = "panel.timeline";

        public int Order => 45;

        static readonly ConditionalWeakTable<UiContext, TimelineUiService> services = new ConditionalWeakTable<UiContext, TimelineUiService>();

        /// <summary>Makes the service of a UI context (replaced when the scene's Timeline playback offers its own).</summary>
        public static Func<UiContext, TimelineUiService> Factory = c => new TimelinePlaybackService(c);

        /// <summary>The Timeline service of <paramref name="ctx"/>.</summary>
        public static TimelineUiService ServiceOf(UiContext ctx) => services.GetValue(ctx, c => Factory(c));

        static UiContext readyCtx;
        static bool recorderHooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            if (recorderHooked) FrameRecorder.Finished -= OnRecordingFinished;
            recorderHooked = false;
            readyCtx = null;
            lastLayout = null;
            TimelineChrome.Reset();
        }

        public void Register(UiContext ctx)
        {
            var a = ctx.Registry.Actions;
            if (ActionManifest.Contains(ActionIds.TimelinePanel))
            {
                var d = a.Register(ActionIds.TimelinePanel, call => TogglePanel(ctx), () => ctx.Shell != null);
                d.IsChecked = () => PanelShown(ctx);
            }
            if (ActionManifest.Contains(ActionIds.TimelinePlayPause))
                a.Register(ActionIds.TimelinePlayPause, call =>
                {
                    var s = ServiceOf(ctx);
                    var r = s.State == TimelinePlayState.Playing ? s.Pause() : s.Play();
                    Toast(ctx, r);
                    return s.State.ToString();
                }, () => ctx.Model?.Timeline != null && ServiceOf(ctx).PlaybackAvailable);
            if (ActionManifest.Contains(ActionIds.TimelineStop))
                a.Register(ActionIds.TimelineStop, call =>
                {
                    var s = ServiceOf(ctx);
                    Toast(ctx, s.Stop(call.Arg("bake") is bool b && b));
                    return s.State.ToString();
                }, () => ctx.Model?.Timeline != null && ServiceOf(ctx).PlaybackAvailable);
            if (ActionManifest.Contains(ActionIds.TimelineKey))
                a.Register(ActionIds.TimelineKey, call => { TimelinePanel.Current?.KeySelected(); return null; }, () => TimelinePanel.Current != null && TimelinePanel.Current.CanKey);
            if (ActionManifest.Contains(ActionIds.TimelineExport))
                a.Register(ActionIds.TimelineExport, call => TimelineExportDialog.Show(ctx) != null, () => ctx.Dialogs != null && !ctx.Dialogs.IsModalOpen);

            if (LayoutListsPanel(ctx)) EnsurePanel(ctx);
            ctx.ViewStateChanged += what => { if (what == "layout") OnLayout(ctx); };
            readyCtx = ctx;
            UiHost.Ready -= OnReady;
            UiHost.Ready += OnReady;
            if (!recorderHooked)
            {
                recorderHooked = true;
                FrameRecorder.Finished += OnRecordingFinished;
            }
        }

        static void Toast(UiContext ctx, TimelineResult r)
        {
            if (!r.Ok) ctx.Toasts?.Show(L10n.Format("msg.opFailed", r.Code, r.Message), ToastKind.Error);
        }

        // ------------------------------------------------------------ the panel and its region

        /// <summary>True when layout.json lists the panel (in a region or hidden): the user had it before.</summary>
        static bool LayoutListsPanel(UiContext ctx)
        {
            try
            {
                string file = Path.Combine(ctx.Options.ConfigDir, LayoutDocument.FileName);
                if (!File.Exists(file)) return false;
                var d = LayoutDocument.Parse(File.ReadAllText(file, Encoding.UTF8));
                return d != null && (d.RegionOf(PanelId) != null || d.Hidden.Contains(PanelId));
            }
            catch (Exception) { return false; }
        }

        /// <summary>Registers the panel (default region bottom) when it is not registered yet.</summary>
        public static void EnsurePanel(UiContext ctx)
        {
            if (ctx.Registry.Panels.TryGet(PanelId, out _)) return;
            ctx.Registry.Panels.Register(PanelId, TitleKey, LayoutRegions.Bottom, c => new TimelinePanel(c), ActionScope.Global);
        }

        /// <summary>The panel is the shown tab of an open region.</summary>
        public static bool PanelShown(UiContext ctx)
        {
            var sh = ctx.Shell;
            string r = sh?.RegionOf(PanelId);
            if (r == null) return false;
            var reg = sh.Regions[r];
            return !reg.Collapsed && reg.Active == PanelId;
        }

        /// <summary>timeline.panel: shows the panel (its region opened, the panel active), or collapses the region when the panel
        /// is its shown tab. Returns true when the panel is shown afterwards.</summary>
        public static bool TogglePanel(UiContext ctx)
        {
            var sh = ctx.Shell;
            if (sh == null) return false;
            EnsurePanel(ctx);
            if (PanelShown(ctx))
            {
                sh.SetCollapsed(sh.Regions[sh.RegionOf(PanelId)], true, true);
                return false;
            }
            return sh.ShowPanel(PanelId);
        }

        static object lastLayout;

        /// <summary>A layout replaced as a whole («Reset layout»): the bottom region does not open for the Timeline panel alone.</summary>
        static void OnLayout(UiContext ctx)
        {
            var sh = ctx.Shell;
            if (sh?.Layout == null || ReferenceEquals(sh.Layout, lastLayout)) return;
            bool first = lastLayout == null;
            lastLayout = sh.Layout;
            if (first) return;
            if (!sh.Regions.TryGetValue(LayoutRegions.Bottom, out var bottom)) return;
            if (bottom.Panels.Count == 1 && bottom.Panels[0] == PanelId && !bottom.Collapsed) sh.SetCollapsed(bottom, true, true);
        }

        // ------------------------------------------------------------ toolbar toggle, status item

        static void OnReady(UiHost h)
        {
            UiHost.Ready -= OnReady;
            var ctx = h?.Context ?? readyCtx;
            if (ctx == null) return;
            lastLayout = ctx.Shell?.Layout;
            TimelineChrome.Attach(ctx);
        }

        // ------------------------------------------------------------ recordings started from the UI

        static void OnRecordingFinished(FrameRecorder rec)
        {
            var ctx = UiHost.Current?.Context;
            if (rec == null || ctx?.Toasts == null || rec.Origin != HS2Next.Api.Plugins.CaptureOrigin.Ui) return;
            if (rec.Error != null && rec.Written == 0)
            {
                ctx.Toasts.Show(L10n.Format("timeline.export.failed", rec.Error), ToastKind.Error, 10f);
                return;
            }
            var el = ctx.Toasts.Show(L10n.Format("timeline.recordDone", rec.Written), rec.Error != null ? ToastKind.Warning : ToastKind.Success, 10f);
            if (el == null) return;
            string dir = rec.Folder;
            var b = new Button { name = "toast-open-folder" };
            Loc.Bind(b, "capture.openFolder");
            b.AddToClassList("hs-tl__toastbtn");
            b.RegisterCallback<PointerDownEvent>(e =>
            {
                e.StopPropagation();
                OpenFolder(ctx, dir);
                el.RemoveFromHierarchy();
            }, TrickleDown.TrickleDown);
            el.Add(b);
        }

        /// <summary>Opens a folder in the OS file browser.</summary>
        public static void OpenFolder(UiContext ctx, string dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            try
            {
                if (!Directory.Exists(dir)) return;
                if (!Application.isBatchMode) Application.OpenURL(new Uri(dir).AbsoluteUri);
            }
            catch (Exception e) { ctx.Toasts?.Show(e.Message, ToastKind.Error); }
        }
    }

    /// <summary>The Timeline's toolbar toggle (after the scene-time read-out) and status-bar item, kept in the current shell.</summary>
    public static class TimelineChrome
    {
        public const string ToolbarName = "tb-" + ActionIds.TimelinePanel, StatusName = "status-timeline";
        static UiContext ctx;
        static float next, nextCheck;
        static bool ticking;

        internal static void Reset()
        {
            if (ticking) PanelTicker.Remove(Tick);
            ticking = false;
            ctx = null;
            next = nextCheck = 0;
        }

        public static void Attach(UiContext c)
        {
            ctx = c;
            Place();
            if (!ticking && Application.isPlaying) { PanelTicker.Add(Tick); ticking = true; }
            Refresh();
        }

        static void Place()
        {
            var tb = ctx?.Shell?.Toolbar?.Element;
            if (tb != null && tb.Q(ToolbarName) == null)
            {
                var b = new Button(() => ctx.Registry.Run(ActionIds.TimelinePanel, "toolbar")) { name = ToolbarName, text = "◆" };
                b.AddToClassList("hs-toolbar__button");
                Loc.Tooltip(b, ActionIds.LabelKey(ActionIds.TimelinePanel));
                var time = tb.Q("tb-scene-time");
                int i = time != null ? tb.IndexOf(time) + 1 : tb.childCount;
                tb.Insert(Math.Min(i, tb.childCount), b);
            }
            var sb = ctx?.Shell?.StatusBar?.Element;
            if (sb != null && sb.Q(StatusName) == null)
            {
                var l = new Label { name = StatusName };
                l.AddToClassList("hs-statusbar__item");
                l.AddToClassList("hs-statusbar__timeline");
                l.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button != 0) return;
                    TimelineUiModule.EnsurePanel(ctx);
                    ctx.Shell?.ShowPanel(TimelineUiModule.PanelId);
                });
                var paused = sb.Q("status-paused");
                int i = paused != null ? sb.IndexOf(paused) : sb.childCount;
                sb.Insert(Math.Min(i, sb.childCount), l);
                l.style.display = DisplayStyle.None;
            }
        }

        static void Tick()
        {
            if (ctx == null) return;
            TimelineOverrideUi.Tick(ctx);
            float now = Time.unscaledTime;
            if (now >= nextCheck) { nextCheck = now + 1f; Place(); }
            if (now < next) return;
            next = now + 0.1f;
            Refresh();
        }

        /// <summary>The status text now ("" = hidden).</summary>
        public static string StatusText(UiContext c)
        {
            var rec = FrameRecorder.Current;
            if (rec != null && rec.IsActive) return L10n.Format("timeline.recording", rec.Frames);
            if (c?.Model?.Timeline == null) return "";
            var s = TimelineUiModule.ServiceOf(c);
            string glyph;
            switch (s.State)
            {
                case TimelinePlayState.Playing: glyph = "▶"; break;
                case TimelinePlayState.Paused: glyph = "❚❚"; break;
                case TimelinePlayState.Ended: glyph = "⇥"; break;
                default: glyph = "■"; break;
            }
            return L10n.Format("status.timeline", glyph, TimelineText.Time(s.Time));
        }

        public static void Refresh()
        {
            if (ctx?.Shell == null) return;
            var b = ctx.Shell.Toolbar?.Element?.Q<Button>(ToolbarName);
            if (b != null) b.EnableInClassList("hs-toolbar__button--on", TimelineUiModule.PanelShown(ctx));
            var l = ctx.Shell.StatusBar?.Element?.Q<Label>(StatusName);
            if (l == null) return;
            string text = StatusText(ctx);
            l.text = text;
            l.style.display = text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
