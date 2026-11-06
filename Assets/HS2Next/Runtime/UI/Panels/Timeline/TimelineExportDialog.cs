// «Export frames…» of the Timeline panel (action timeline.export): a modal dialog for a frame sequence — the size rows of
// the screenshot dialog (size mode, scale, custom size, supersampling, the scene's frame image), the frame rate (24 / 25 /
// 30 / 60 or custom 1–120), «One pass of the Timeline» (on by default when the scene has a Timeline) or a frame count
// (0 = until stopped), the folder (empty = record/<date_time> under the output folder), the note that the frames are PNG
// files and a video is made with an external tool, with the command line to copy. «Start» starts the recorder in process
// (the UI as the capture origin) and closes the dialog; while it records, the status bar and the panel header show the
// count and a Stop button; at the end a toast with «Open folder». When the recorder cannot start, the reason is shown and
// nothing starts.
using System;
using System.Globalization;
using System.IO;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using HS2Next.Scenes.Runtime;
using HS2Next.Screenshots;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Plugins;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;
using ApiPhase = HS2Next.Api.Ui.EditPhase;

namespace HS2Next.UI.Panels
{
    public sealed class TimelineExportDialog : VisualElement
    {
        public const string DialogId = "timeline-export";
        public static TimelineExportDialog Open { get; private set; }

        static readonly int[] Rates = { 24, 25, 30, 60 };
        /// <summary>The choices of the last dialog (kept for the next one in this session).</summary>
        static int lastRate = 30, lastCustom = 30, lastFrames;
        static bool lastOnePass = true;
        static string lastFolder = "";

        readonly UiContext ctx;
        readonly HostWidget rateChoice, customRate, onePass, frames;
        readonly TextField folder, command;
        readonly Label sizeLabel, problemLabel;
        int rateIndex;

        public int Fps => rateIndex < Rates.Length ? Rates[rateIndex] : Mathf.Clamp(lastCustom, FrameRecorder.MinFps, FrameRecorder.MaxFps);
        public bool OnePass => lastOnePass && ctx.Model?.Timeline != null;

        TimelineExportDialog(UiContext ctx)
        {
            this.ctx = ctx;
            name = DialogId;
            AddToClassList("hs-tl__export");
            foreach (var s in InspectorSchemas.Capture)
                InspectorUi.AddSection(ctx, s, TargetSelector.Scene, this, f => f.Kind == FieldKind.Button ? Array.Empty<VisualElement>() : null);
            sizeLabel = new Label { name = "timeline-export-size" };
            sizeLabel.AddToClassList("hs-row__label");
            Add(sizeLabel);

            rateIndex = Array.IndexOf(Rates, lastRate);
            if (rateIndex < 0) rateIndex = Rates.Length;
            var labels = new System.Collections.Generic.List<(string, string)>();
            foreach (var r in Rates) labels.Add((null, r.ToString(CultureInfo.InvariantCulture)));
            labels.Add(("timeline.export.custom", null));
            rateChoice = PluginWidgetKit.Choice("timeline-export-fps", "timeline.export.fps", labels, i => { rateIndex = i; lastRate = i < Rates.Length ? Rates[i] : 0; Refresh(); });
            Add(rateChoice.Element);
            customRate = PluginWidgetKit.Number("timeline-export-custom", "timeline.export.custom", FrameRecorder.MinFps, FrameRecorder.MaxFps, true, "0", null, (v, p, typed) =>
            {
                if (p == ApiPhase.Cancel) return;
                lastCustom = (int)Math.Round(Math.Max(FrameRecorder.MinFps, Math.Min(FrameRecorder.MaxFps, v)));
                Refresh();
            });
            Add(customRate.Element);
            onePass = PluginWidgetKit.Toggle("timeline-export-onepass", "timeline.onePass", b => { lastOnePass = b; Refresh(); });
            Add(onePass.Element);
            frames = PluginWidgetKit.Number("timeline-export-frames", "timeline.export.frames", null, null, true, "0", null, (v, p, typed) =>
            {
                if (p == ApiPhase.Cancel) return;
                lastFrames = (int)Math.Round(Math.Max(0, Math.Min(FrameRecorder.MaxFrameCount, v)));
                Refresh();
            });
            Add(frames.Element);

            folder = new TextField { name = "timeline-export-folder", isDelayed = true, value = lastFolder };
            Loc.Bind(folder, "timeline.export.folder", (e, s) => ((TextField)e).label = s);
            folder.textEdition.placeholder = "record/<yyyyMMdd_HHmmss>";
            folder.RegisterValueChangedCallback(e => { lastFolder = e.newValue ?? ""; Refresh(); });
            Add(folder);

            var note = Loc.Label("timeline.export.note");
            note.name = "timeline-export-note";
            note.style.whiteSpace = WhiteSpace.Normal;
            note.AddToClassList("hs-tl__hint");
            Add(note);
            command = new TextField { name = "timeline-export-command", isReadOnly = true };
            command.AddToClassList("hs-tl__command");
            Add(command);
            problemLabel = new Label { name = "timeline-export-problem" };
            problemLabel.AddToClassList("hs-hint");
            problemLabel.style.whiteSpace = WhiteSpace.Normal;
            Add(problemLabel);
            ctx.Options.Changed += OnOption;
            RegisterCallback<DetachFromPanelEvent>(_ => ctx.Options.Changed -= OnOption);
        }

        void OnOption(string key) => Refresh();

        /// <summary>The recorder's options of the dialog's current values (the folder resolved); throws BridgeException for a folder
        /// that is not allowed.</summary>
        public RecordOptions Options()
        {
            var view = ctx.Runtime?.GetPart<SceneViewCamera>()?.Camera;
            var req = CaptureSettings.Current(view);
            var o = new RecordOptions
            {
                Width = req.Width, Height = req.Height, Supersample = req.Supersample, Frame = req.Frame, Fps = Fps,
                Timeline = OnePass, Frames = OnePass ? 0 : lastFrames, Origin = CaptureOrigin.Ui,
            };
            string path = (lastFolder ?? "").Trim();
            o.Folder = FrameRecorder.ResolveFolder(ctx.App, ctx.App?.Paths?.OutputDir, path.Length == 0 ? null : path, anyFolder: true);
            return o;
        }

        /// <summary>Why Start cannot run now, or null.</summary>
        public string Refusal()
        {
            if (FrameRecorder.IsBusy) return L10n.Get("timeline.export.busy");
            if (!CaptureService.Available) return L10n.Get("timeline.export.noDevice");
            var view = ctx.Runtime?.GetPart<SceneViewCamera>()?.Camera;
            var req = CaptureSettings.Current(view);
            string cap = CaptureLimits.Check(req.Width, req.Height, req.Supersample, 0, out _);
            return cap != null ? L10n.Format("capture.overCap", cap) : null;
        }

        public void Refresh()
        {
            bool hasTl = ctx.Model?.Timeline != null;
            rateChoice.SetValue(rateIndex);
            customRate.SetValue((double)lastCustom);
            customRate.Element.style.display = rateIndex >= Rates.Length ? DisplayStyle.Flex : DisplayStyle.None;
            onePass.SetValue(OnePass);
            onePass.Enabled = hasTl;
            frames.SetValue((double)lastFrames);
            frames.Enabled = !OnePass;
            var view = ctx.Runtime?.GetPart<SceneViewCamera>()?.Camera;
            var r = CaptureSettings.Current(view);
            sizeLabel.text = L10n.Format("timeline.export.size", r.Width, r.Height, r.Width * r.Supersample, r.Height * r.Supersample, Fps);
            command.SetValueWithoutNotify("ffmpeg -framerate " + Fps.ToString(CultureInfo.InvariantCulture) + " -i frame_%06d.png -pix_fmt yuv420p out.mp4");
            string why = Refusal();
            problemLabel.text = why ?? "";
            problemLabel.style.display = why != null ? DisplayStyle.Flex : DisplayStyle.None;
            parent?.Q<Button>("dialog-button-start")?.SetEnabled(why == null);
        }

        /// <summary>Starts the recording of the dialog's values; null when it started, else the reason (also shown as a toast).</summary>
        public string Start()
        {
            string why = Refusal();
            if (why == null)
            {
                try
                {
                    FrameRecorder.Start(ctx.App, Options());
                    return null;
                }
                catch (BridgeException e) { why = e.Message; }
                catch (Exception e) { why = e.Message; }
            }
            ctx.Toasts?.Show(L10n.Format("timeline.export.failed", why), ToastKind.Error);
            Refresh();
            problemLabel.text = L10n.Format("timeline.export.failed", why);
            problemLabel.style.display = DisplayStyle.Flex;
            return why;
        }

        /// <summary>Shows the dialog (Start, Close).</summary>
        public static TimelineExportDialog Show(UiContext ctx)
        {
            if (ctx.Dialogs == null || ctx.Dialogs.IsModalOpen) return null;
            var d = new TimelineExportDialog(ctx);
            var spec = new DialogSpec { Id = DialogId, Title = L10n.Get("timeline.export"), Content = d };
            spec.Buttons.Add(DialogButton.Of("start", "timeline.export.start", isDefault: true));
            spec.Buttons.Add(DialogButton.Of("close", "common.close", isCancel: true));
            spec.CanClose = id => id != "start" || d.Start() == null;
            Open = d;
            ctx.Dialogs.Show(spec, _ => { if (Open == d) Open = null; ctx.Options.Save(); });
            d.Refresh();
            return d;
        }
    }
}
