// The dialogs of the panel «Scene scripts»:
//   «Record…»  the recorder's dialog fields: the size rows of the screenshot dialog, the frame rate (24 / 25 / 30 / 60 or custom 1–120) and the folder (empty: record/<date_time>
//              under the output folder; relative to the output folder, or absolute under the output folder or the user data folder, as the record command takes it). «Record» plays
//              the script with one recorded pass and closes the dialog; a folder that is not allowed, a recording that runs or no graphics device keep it open with the reason.
//   «Export…»  the file to write (relative to the output folder, or any folder outside HS2's); «.hs2nscript» is added when missing.
//   «Import…»  the file to read into the store (relative to the output folder, or absolute).
//   «Purge…»   the deleted rows it removes, with their number and size; nothing to purge or an unreadable index: only «Close».
// Element names: dialog-scripts-record (scripts-record-size, scripts-record-fps, scripts-record-custom, scripts-record-folder, scripts-record-note, scripts-record-problem),
// dialog-scripts-export (scripts-export-path), dialog-scripts-import (scripts-import-path), dialog-scripts-purge (scripts-purge-list, scripts-purge-row-<no>); buttons
// dialog-button-<id>.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HS2Next.Api.Bridge;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Scripts;
using HS2Next.Screenshots;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using HS2Next.UI.Plugins;
using HS2Next.UI.Widgets;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using ApiPhase = HS2Next.Api.Ui.EditPhase;

namespace HS2Next.UI.Panels
{
    public static class ScriptsDialogs
    {
        public const string RecordId = "scripts-record", ExportId = "scripts-export", ImportId = "scripts-import", PurgeId = "scripts-purge";
        public const string FileExtension = ".hs2nscript";

        static readonly int[] Rates = { 24, 25, 30, 60 };
        // the choices of the last dialogs (kept for the next one in this session)
        static int lastRate = 30, lastCustom = 30;
        static string lastFolder = "", lastImport = "";

        internal static void ResetStatics()
        {
            lastRate = 30;
            lastCustom = 30;
            lastFolder = "";
            lastImport = "";
        }

        static VisualElement Box(string id)
        {
            var b = new VisualElement { name = id };
            b.AddToClassList("hs-scripts__dialog");
            ScriptsUiModule.Style(b);
            return b;
        }

        static Label Problem(VisualElement parent, string id)
        {
            var l = new Label { name = id };
            l.AddToClassList("hs-hint");
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.display = DisplayStyle.None;
            parent.Add(l);
            return l;
        }

        static void Say(Label l, string text)
        {
            l.text = text ?? "";
            l.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // ------------------------------------------------------------ record

        /// <summary>The record dialog; <paramref name="start"/> gets record.start's arguments {path, width, height, fps, supersample, frame}.</summary>
        public static bool Record(UiContext ctx, Action<JObject> start)
        {
            if (ctx.Dialogs == null || ctx.Dialogs.IsModalOpen) return false;
            var box = Box(RecordId + "-content");
            foreach (var s in InspectorSchemas.Capture)
                InspectorUi.AddSection(ctx, s, TargetSelector.Scene, box, f => f.Kind == FieldKind.Button ? Array.Empty<VisualElement>() : null);
            var size = new Label { name = RecordId + "-size" };
            size.AddToClassList("hs-row__label");
            box.Add(size);
            int rateIndex = Array.IndexOf(Rates, lastRate);
            if (rateIndex < 0) rateIndex = Rates.Length;
            var labels = new List<(string, string)>();
            foreach (var r in Rates) labels.Add((null, r.ToString(CultureInfo.InvariantCulture)));
            labels.Add(("timeline.export.custom", null));
            HostWidget rate = null, custom = null;
            Label problem = null;
            int Fps() => rateIndex < Rates.Length ? Rates[rateIndex] : Mathf.Clamp(lastCustom, FrameRecorder.MinFps, FrameRecorder.MaxFps);
            void Refresh()
            {
                rate?.SetValue(rateIndex);
                custom?.SetValue((double)lastCustom);
                if (custom != null) custom.Element.style.display = rateIndex >= Rates.Length ? DisplayStyle.Flex : DisplayStyle.None;
                var view = ctx.Runtime?.GetPart<SceneViewCamera>()?.Camera;
                var req = CaptureSettings.Current(view);
                size.text = L10n.Format("timeline.export.size", req.Width, req.Height, req.Width * req.Supersample, req.Height * req.Supersample, Fps());
                string why = Refusal(ctx);
                if (problem != null) Say(problem, why);
                box.parent?.Q<Button>("dialog-button-record")?.SetEnabled(why == null);
            }
            rate = PluginWidgetKit.Choice(RecordId + "-fps", "timeline.export.fps", labels, i => { rateIndex = i; lastRate = i < Rates.Length ? Rates[i] : 0; Refresh(); });
            box.Add(rate.Element);
            custom = PluginWidgetKit.Number(RecordId + "-custom", "timeline.export.custom", FrameRecorder.MinFps, FrameRecorder.MaxFps, true, "0", null, (v, p, typed) =>
            {
                if (p == ApiPhase.Cancel) return;
                lastCustom = (int)Math.Round(Math.Max(FrameRecorder.MinFps, Math.Min(FrameRecorder.MaxFps, v)));
                Refresh();
            });
            box.Add(custom.Element);
            var folder = new TextField { name = RecordId + "-folder", isDelayed = true, value = lastFolder };
            Loc.Bind(folder, "timeline.export.folder", (e, s) => ((TextField)e).label = s);
            folder.textEdition.placeholder = "record/<yyyyMMdd_HHmmss>";
            folder.RegisterValueChangedCallback(e => { lastFolder = e.newValue ?? ""; Refresh(); });
            box.Add(folder);
            var note = Loc.Label("script.recordNote");
            note.name = RecordId + "-note";
            note.style.whiteSpace = WhiteSpace.Normal;
            note.AddToClassList("hs-scripts__status");
            box.Add(note);
            problem = Problem(box, RecordId + "-problem");

            void OnOption(string key) => Refresh();
            ctx.Options.Changed += OnOption;
            box.RegisterCallback<DetachFromPanelEvent>(_ => ctx.Options.Changed -= OnOption);

            var spec = new DialogSpec { Id = RecordId, Title = L10n.Get("script.record"), Content = box };
            spec.Buttons.Add(DialogButton.Of("record", "script.recordOk", isDefault: true));
            spec.Buttons.Add(DialogButton.Of("close", "common.close", isCancel: true));
            spec.CanClose = id =>
            {
                if (id != "record") return true;
                lastFolder = folder.value ?? "";
                string why = Refusal(ctx);
                JObject args = null;
                if (why == null)
                {
                    try { args = RecordArgs(ctx, lastFolder, Fps()); }
                    catch (BridgeException e) { why = e.Error?.Message ?? e.Message; }
                    catch (Exception e) { why = e.Message; }
                }
                if (why != null) { Say(problem, why); return false; }
                UiDefer.Frames(1, () => start(args));
                return true;
            };
            ctx.Dialogs.Show(spec, _ => ctx.Options.Save());
            Refresh();
            return true;
        }

        /// <summary>Why a recording cannot start now, or null.</summary>
        public static string Refusal(UiContext ctx)
        {
            if (FrameRecorder.IsBusy) return L10n.Get("timeline.export.busy");
            if (!CaptureService.Available) return L10n.Get("timeline.export.noDevice");
            var view = ctx.Runtime?.GetPart<SceneViewCamera>()?.Camera;
            var req = CaptureSettings.Current(view);
            string cap = CaptureLimits.Check(req.Width, req.Height, req.Supersample, 0, out _);
            return cap != null ? L10n.Format("capture.overCap", cap) : null;
        }

        /// <summary>record.start's arguments of the dialog's values: the folder resolved as the record command resolves it (throws BridgeException when it is not allowed).</summary>
        public static JObject RecordArgs(UiContext ctx, string folder, int fps)
        {
            var view = ctx.Runtime?.GetPart<SceneViewCamera>()?.Camera;
            var req = CaptureSettings.Current(view);
            string path = (folder ?? "").Trim();
            string full = FrameRecorder.ResolveFolder(ctx.App, ctx.App?.Paths?.OutputDir, path.Length == 0 ? null : path);
            return new JObject
            {
                ["path"] = full, ["width"] = req.Width, ["height"] = req.Height, ["fps"] = fps, ["supersample"] = req.Supersample, ["frame"] = req.Frame,
            };
        }

        // ------------------------------------------------------------ export and import

        /// <summary>The file a script is exported to: <paramref name="path"/> relative to the output folder, or absolute anywhere outside HS2's folders; «.hs2nscript» added when
        /// missing; its folder made. Throws BridgeException when the path is not allowed.</summary>
        public static string ExportPath(UiContext ctx, string path)
        {
            path = (path ?? "").Trim();
            if (path.Length == 0) throw new BridgeException(BridgeError.BadArgs("path", "a file"));
            if (!path.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase)) path += FileExtension;
            string file = Path.GetFileName(path);
            string dir = Path.GetDirectoryName(path);
            string folder = FrameRecorder.ResolveFolder(ctx.App, ctx.App?.Paths?.OutputDir, string.IsNullOrEmpty(dir) ? "." : dir, anyFolder: true);
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, file);
        }

        /// <summary>A file name made of a script's name: characters a file name cannot hold become "_".</summary>
        public static string FileNameOf(string name)
        {
            var bad = Path.GetInvalidFileNameChars();
            var c = (string.IsNullOrWhiteSpace(name) ? "script" : name.Trim()).ToCharArray();
            for (int i = 0; i < c.Length; i++) if (Array.IndexOf(bad, c[i]) >= 0) c[i] = '_';
            return new string(c) + FileExtension;
        }

        /// <summary>The export dialog; <paramref name="write"/> gets the checked full path and answers whether the file was written (else the dialog stays open).</summary>
        public static bool Export(UiContext ctx, string scriptName, Func<string, bool> write)
        {
            if (ctx.Dialogs == null || ctx.Dialogs.IsModalOpen) return false;
            var box = Box(ExportId + "-content");
            var field = new TextField { name = ExportId + "-path", value = "scripts/" + FileNameOf(scriptName) };
            Loc.Bind(field, "script.file", (e, s) => ((TextField)e).label = s);
            box.Add(field);
            var problem = Problem(box, ExportId + "-problem");
            var spec = new DialogSpec { Id = ExportId, Title = L10n.Get("script.export"), Content = box };
            spec.Buttons.Add(DialogButton.Of("export", "script.exportOk", isDefault: true));
            spec.Buttons.Add(DialogButton.Cancel());
            spec.CanClose = id =>
            {
                if (id != "export") return true;
                string full;
                try { full = ExportPath(ctx, field.value); }
                catch (BridgeException e) { Say(problem, e.Error?.Message ?? e.Message); return false; }
                catch (Exception e) { Say(problem, e.Message); return false; }
                return write(full);
            };
            ctx.Dialogs.Show(spec);
            return true;
        }

        /// <summary>The file a script is imported from: <paramref name="path"/> relative to the output folder, or absolute; null when it is empty.</summary>
        public static string ImportPath(UiContext ctx, string path)
        {
            path = (path ?? "").Trim().Trim('"');
            if (path.Length == 0) return null;
            if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
            string root = ctx.App?.Paths?.OutputDir;
            return string.IsNullOrEmpty(root) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(root, path));
        }

        /// <summary>The import dialog; <paramref name="read"/> gets the full path of an existing file and answers whether it was imported (else the dialog stays open).</summary>
        public static bool Import(UiContext ctx, Func<string, bool> read)
        {
            if (ctx.Dialogs == null || ctx.Dialogs.IsModalOpen) return false;
            var box = Box(ImportId + "-content");
            var field = new TextField { name = ImportId + "-path", value = lastImport };
            Loc.Bind(field, "script.file", (e, s) => ((TextField)e).label = s);
            field.textEdition.placeholder = "*" + FileExtension;
            box.Add(field);
            var problem = Problem(box, ImportId + "-problem");
            var spec = new DialogSpec { Id = ImportId, Title = L10n.Get("script.import"), Content = box };
            spec.Buttons.Add(DialogButton.Of("import", "script.importOk", isDefault: true));
            spec.Buttons.Add(DialogButton.Cancel());
            spec.CanClose = id =>
            {
                if (id != "import") return true;
                lastImport = field.value ?? "";
                string full;
                try { full = ImportPath(ctx, field.value); }
                catch (Exception e) { Say(problem, e.Message); return false; }
                if (full == null) { Say(problem, L10n.Get("script.fileFirst")); return false; }
                if (!File.Exists(full)) { Say(problem, L10n.Format("script.noFile", full)); return false; }
                return read(full);
            };
            ctx.Dialogs.Show(spec);
            return true;
        }

        // ------------------------------------------------------------ purge

        /// <summary>The purge dialog: the deleted rows <paramref name="gone"/> and what <paramref name="preview"/> says it removes; <paramref name="purge"/> runs on «Purge».</summary>
        public static bool Purge(UiContext ctx, IReadOnlyList<ScriptStoreRow> gone, ScriptPurgeResult preview, bool unreadable, Action purge)
        {
            if (ctx.Dialogs == null || ctx.Dialogs.IsModalOpen) return false;
            var list = new VisualElement { name = PurgeId + "-list" };
            ScriptsUiModule.Style(list);
            foreach (var r in gone)
            {
                var l = new Label(r.No.ToString(CultureInfo.InvariantCulture) + "  " + r.Name) { name = PurgeId + "-row-" + r.No.ToString(CultureInfo.InvariantCulture) };
                l.AddToClassList("hs-scripts__text");
                list.Add(l);
            }
            bool nothing = unreadable || (preview.Rows == 0 && preview.Files == 0);
            var spec = new DialogSpec
            {
                Id = PurgeId, Title = L10n.Get("script.purge"), Content = list,
                Message = unreadable ? L10n.Get("script.purgeUnreadable") : nothing ? L10n.Get("script.purgeNone") : L10n.Format("script.purgeWhat", preview.Rows, BakeUi.Size(preview.Bytes)),
            };
            if (!nothing) spec.Buttons.Add(DialogButton.Of("purge", "script.purgeOk", true));
            spec.Buttons.Add(nothing ? DialogButton.Of("cancel", "common.close", true, true) : DialogButton.Cancel());
            ctx.Dialogs.Show(spec, id => { if (id == "purge") purge(); });
            return true;
        }
    }
}
