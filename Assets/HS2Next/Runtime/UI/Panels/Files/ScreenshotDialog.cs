// Screenshots in the Studio UI (Phase 3.6 package C):
//   capture.screenshot (F11 in both presets, vanilla's key; the toolbar's camera-icon button): a screenshot with the
//                      current settings into <UserData>/cap (CaptureCommands.TakeScreenshot, the bridge `screenshot`'s
//                      path); a toast "Screenshot saved" with the file name once it is written (no sound, Q8); errors
//                      (the cap, a screenshot still being written) as an error toast. Inert while a modal dialog is open.
//   capture.dialog     (Shift+F11; the toolbar button's drop-down): the modal Screenshot dialog — P's option rows
//                      (InspectorSchemas.Capture: size mode, scale, custom W × H with the aspect lock, supersampling, include
//                      frame), the resulting output and render size and megapixels against the cap (a warning above 16.6
//                      Mpix; Take disabled above the cap), Take (stays open), Open folder (<UserData>/cap in the OS file
//                      browser), the last file written.
// The module also keeps CaptureSettings in step with the options (capture.*, images.embed) and makes sure the runtime has
// its SceneImages part.
using System;
using System.Collections.Generic;
using System.IO;
using HS2Next.App.Commands;
using HS2Next.Scenes.Runtime;
using HS2Next.Screenshots;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class ScreenshotModule : IUiModule
    {
        public int Order => 125;

        static readonly string[] Keys =
        {
            CaptureSettings.KeyMode, CaptureSettings.KeyScale, CaptureSettings.KeyWidth, CaptureSettings.KeyHeight, CaptureSettings.KeySupersample,
            CaptureSettings.KeyFrame, CaptureSettings.KeyMaxPixels, CaptureSettings.KeyEmbed,
        };

        static UiContext current;
        static bool hooked;

        /// <summary>The last screenshot file written from the UI (the dialog shows it), or null.</summary>
        public static string LastFile { get; private set; }

        public void Register(UiContext ctx)
        {
            current = ctx;
            if (ctx.Runtime != null) SceneImages.Ensure(ctx.Runtime);
            if (ctx.Options != null)
            {
                foreach (var k in Keys) { var v = ctx.Options.Get(k); if (v != null) CaptureSettings.Set(k, v); }
                ctx.Options.Changed += k => { if (Array.IndexOf(Keys, k) >= 0) CaptureSettings.Set(k, ctx.Options.Get(k)); };
            }
            if (!hooked)
            {
                hooked = true;
                CaptureService.Saved += r => { if (r?.Path != null) LastFile = r.Path; };
            }
            var a = ctx.Registry.Actions;
            if (ActionManifest.Contains(ActionIds.CaptureScreenshot))
                a.Register(ActionIds.CaptureScreenshot, call => Take(ctx), () => ctx.Dialogs == null || !ctx.Dialogs.IsModalOpen);
            if (ActionManifest.Contains(ActionIds.CaptureDialog))
                a.Register(ActionIds.CaptureDialog, _ => { ScreenshotDialog.Show(ctx); return null; }, () => ctx.Dialogs != null && !ctx.Dialogs.IsModalOpen);
        }

        /// <summary>
        /// A screenshot with the current settings; returns the path it will be written to (the toast follows when it is
        /// written), or "error: …" (shown as an error toast).
        /// </summary>
        public static object Take(UiContext ctx, CaptureRequest request = null)
        {
            try
            {
                var view = ctx.Runtime?.GetPart<SceneViewCamera>()?.Camera;
                var req = request ?? CaptureSettings.Current(view);
                return CaptureCommands.TakeScreenshot(ctx.App, req, null, res =>
                {
                    if (res.Ok)
                    {
                        LastFile = res.Path;
                        ctx.Toasts?.Show(L10n.Format("capture.saved", Path.GetFileName(res.Path)), ToastKind.Success);
                        foreach (var w in res.Warnings) ctx.Toasts?.Show(L10n.Get("capture.largeRender"), ToastKind.Warning);
                        ctx.RaiseView("capture");
                    }
                    else ctx.Toasts?.Show(L10n.Format("capture.failed", res.Error), ToastKind.Error);
                });
            }
            catch (Exception e)
            {
                ctx.Toasts?.Show(L10n.Format("capture.failed", e.Message), ToastKind.Error);
                return "error: " + e.Message;
            }
        }
    }

    public sealed class ScreenshotDialog : VisualElement
    {
        public const string DialogId = "screenshot";
        public static ScreenshotDialog Open { get; private set; }

        readonly UiContext ctx;
        readonly Label sizeLabel, warnLabel, lastLabel;
        bool adjusting;

        /// <summary>The output and render size of the current settings (what Take renders).</summary>
        public CaptureRequest Request => CaptureSettings.Current(ctx.Runtime?.GetPart<SceneViewCamera>()?.Camera);
        /// <summary>Null when the request passes the cap, else the reason.</summary>
        public string Refusal => CaptureLimits.Check(Request.Width, Request.Height, Request.Supersample, 0, out _);

        ScreenshotDialog(UiContext ctx)
        {
            this.ctx = ctx;
            name = "screenshot-dialog";
            AddToClassList("hs-screenshot");
            foreach (var s in InspectorSchemas.Capture)
                InspectorUi.AddSection(ctx, s, TargetSelector.Scene, this, f => f.Kind == FieldKind.Button ? Array.Empty<VisualElement>() : null);
            sizeLabel = new Label { name = "capture-size" };
            sizeLabel.AddToClassList("hs-row__label");
            Add(sizeLabel);
            warnLabel = new Label { name = "capture-warning" };
            warnLabel.AddToClassList("hs-hint");
            Add(warnLabel);
            lastLabel = new Label { name = "capture-last" };
            lastLabel.AddToClassList(InspectorUi.Dim);
            Add(lastLabel);
            ctx.Options.Changed += OnOption;
            ctx.ViewStateChanged += OnView;
            RegisterCallback<DetachFromPanelEvent>(_ => { ctx.Options.Changed -= OnOption; ctx.ViewStateChanged -= OnView; });
            Refresh();
        }

        void OnView(string what) { if (what == "capture") Refresh(); }

        void OnOption(string key)
        {
            if (!adjusting && (key == CaptureSettings.KeyWidth || key == CaptureSettings.KeyHeight) && ctx.Options.Bool(OptionDefs.CaptureAspectLock))
            {
                // the aspect lock keeps the viewport's aspect for the custom size
                var cam = ctx.Runtime?.GetPart<SceneViewCamera>()?.Camera;
                double a = cam && cam.pixelHeight > 0 ? cam.pixelWidth / (double)cam.pixelHeight : 16.0 / 9.0;
                adjusting = true;
                try
                {
                    if (key == CaptureSettings.KeyWidth) ctx.Options.Set(CaptureSettings.KeyHeight, Mathf.Clamp((int)Math.Round(CaptureSettings.Width / a), CaptureLimits.MinSide, CaptureLimits.MaxSide));
                    else ctx.Options.Set(CaptureSettings.KeyWidth, Mathf.Clamp((int)Math.Round(CaptureSettings.Height * a), CaptureLimits.MinSide, CaptureLimits.MaxSide));
                }
                finally { adjusting = false; }
            }
            Refresh();
        }

        /// <summary>Rewrites the size line, the warning and the last file.</summary>
        public void Refresh()
        {
            var r = Request;
            long px = (long)r.Width * r.Height * r.Supersample * r.Supersample;
            sizeLabel.text = L10n.Format("capture.size", r.Width, r.Height, r.Width * r.Supersample, r.Height * r.Supersample,
                (px / 1e6).ToString("F1", System.Globalization.CultureInfo.InvariantCulture), (CaptureLimits.MaxPixels / 1e6).ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            string refuse = Refusal;
            warnLabel.text = refuse != null ? L10n.Format("capture.overCap", refuse) : px > CaptureLimits.WarnPixels ? L10n.Get("capture.largeRender") : "";
            warnLabel.style.display = warnLabel.text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            lastLabel.text = ScreenshotModule.LastFile != null ? L10n.Format("capture.last", Path.GetFileName(ScreenshotModule.LastFile)) : "";
            var take = parent?.Q<Button>("dialog-button-take");
            take?.SetEnabled(refuse == null);
        }

        /// <summary>Shows the dialog (the buttons Take, Open folder and Close).</summary>
        public static ScreenshotDialog Show(UiContext ctx)
        {
            if (ctx.Dialogs == null) return null;
            var d = new ScreenshotDialog(ctx);
            var spec = new DialogSpec { Id = DialogId, Title = L10n.Get("inspector.capture.section"), Content = d };
            spec.Buttons.Add(DialogButton.Of("take", "inspector.capture.take", isDefault: true));
            spec.Buttons.Add(DialogButton.Of("folder", "capture.openFolder"));
            spec.Buttons.Add(DialogButton.Of("close", "common.close", isCancel: true));
            spec.CanClose = id =>
            {
                if (id == "take")
                {
                    if (d.Refusal == null) ScreenshotModule.Take(ctx, d.Request);
                    d.Refresh();
                    return false;
                }
                if (id == "folder")
                {
                    OpenFolder(ctx);
                    return false;
                }
                return true;
            };
            Open = d;
            ctx.Dialogs.Show(spec, _ => { if (Open == d) Open = null; ctx.Options.Save(); });
            d.Refresh();
            return d;
        }

        /// <summary>Opens &lt;UserData&gt;/cap in the OS file browser (created first).</summary>
        public static void OpenFolder(UiContext ctx)
        {
            string dir = ctx.App?.Paths?.CapDir;
            if (string.IsNullOrEmpty(dir)) return;
            try
            {
                Directory.CreateDirectory(dir);
                if (!Application.isBatchMode) Application.OpenURL(new Uri(dir).AbsoluteUri);
            }
            catch (Exception e) { ctx.Toasts?.Show(e.Message, ToastKind.Error); }
        }
    }
}
