// Scene file flows (U4, U17, U18, U28): New (Ctrl+N), Open (the in-app scene browser), Save
// (Ctrl+S: overwrite the current file; untitled → Save As, or in the hs2studio preset straight to the timestamped name),
// Save As (Ctrl+Shift+S: file name prompt, default yyyy_MMdd_HHmm_ss, plus the manifest title; overwrite confirmation),
// Import scene (merge, one entry), recent files (recent.json, 8), quit (window close, Alt+F4, File ▸ Quit) — every one
// that would lose changes asks first (Save / Don't save / Cancel). Esc never quits. Every document change goes through
// StudioOps (NewScene, LoadScene, SaveScene, ImportScene, SetTitle).
using System;
using System.Collections.Generic;
using System.IO;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class SceneFiles
    {
        readonly UiContext ctx;
        bool quitConfirmed, prompting;

        /// <summary>The file the scene was loaded from / last saved to (null = untitled).</summary>
        public string CurrentPath { get => currentPath; private set { currentPath = value; ctx.CurrentFile = value; } }
        string currentPath;
        public RecentFiles Recent { get; }
        /// <summary>The last operation's outcome for tests ("saved", "loaded", "imported", "new", "cancelled", "failed: …").</summary>
        public string LastOutcome { get; private set; }
        public event Action Changed;
        public static SceneFiles Current { get; private set; }

        public string SceneDir => ctx.App.Paths?.SceneDir;
        public bool IsDirty => ctx.Session.Commands.IsDirty;
        public string DisplayName => CurrentPath == null ? L10n.Get("files.untitled") : Path.GetFileNameWithoutExtension(CurrentPath);

        public SceneFiles(UiContext ctx)
        {
            this.ctx = ctx;
            Current = this;
            Recent = RecentFiles.Load(Path.Combine(ctx.Options.ConfigDir, "recent.json"), Math.Max(0, ctx.Options.Int(OptionDefs.RecentCount)));
            ctx.Options.Changed += k => { if (k == OptionDefs.RecentCount) Recent.Max = Math.Max(0, ctx.Options.Int(k)); };
            if (!Application.isBatchMode) Application.wantsToQuit += WantsToQuit;
        }

        internal void Dispose()
        {
            Application.wantsToQuit -= WantsToQuit;
        }

        void Done(string outcome)
        {
            LastOutcome = outcome;
            UiContext.Guard(() => Changed?.Invoke());
        }

        // ------------------------------------------------------------------ the unsaved-changes prompt

        /// <summary>Runs <paramref name="then"/> when the scene has no unsaved changes, or after Save / Don't save; Cancel drops it.</summary>
        public void IfSaved(Action then, string reasonKey = "dialog.unsaved.message")
        {
            if (!IsDirty || ctx.Dialogs == null) { then(); return; }
            var spec = new DialogSpec { Id = "unsaved", Title = L10n.Get("dialog.unsaved.title"), Message = L10n.Format(reasonKey, DisplayName) };
            spec.Buttons.Add(DialogButton.Of("save", "dialog.save", isDefault: true));
            spec.Buttons.Add(DialogButton.Of("discard", "dialog.dontSave"));
            spec.Buttons.Add(DialogButton.Cancel());
            prompting = true;
            ctx.Dialogs.Show(spec, id =>
            {
                prompting = false;
                if (id == "save") Save(ok => { if (ok) then(); });
                else if (id == "discard") then();
                else Done("cancelled");
            });
        }

        // ------------------------------------------------------------------ new / open / import

        public void New() => IfSaved(() => NewNow());

        public OpResult NewNow()
        {
            var r = ctx.ShowResult(ctx.Ops.NewScene());
            CurrentPath = null;
            Done("new");
            return r;
        }

        /// <summary>Opens the scene browser (File ▸ Open…; also Import with <paramref name="import"/>).</summary>
        public void Open(bool import = false) => SceneBrowser.Show(ctx, this, import);

        /// <summary>Loads a scene file (asks about unsaved changes first).</summary>
        public void Load(string path) => IfSaved(() => LoadNow(path));

        public OpResult LoadNow(string path)
        {
            var r = ctx.Ops.LoadScene(path);
            if (!r.Ok) { ctx.ShowResult(r); Recent.Remove(path); Done("failed: " + r.Message); return r; }
            CurrentPath = path;
            Recent.Touch(path);
            var lr = r.Value as LoadResult<SceneContainer>;
            int warn = 0;
            if (lr?.Findings != null) foreach (var f in lr.Findings) if (f.Severity >= FindingSeverity.Warning) warn++;
            ctx.Toasts?.Show(warn > 0 ? L10n.Format("files.loadedWithFindings", DisplayName, warn) : L10n.Format("files.loaded", DisplayName), warn > 0 ? ToastKind.Warning : ToastKind.Success);
            Done("loaded");
            return r;
        }

        /// <summary>Import scene (U28): the file's nodes added at the root with new ids, one entry, the imported top nodes selected.</summary>
        public OpResult Import(string path)
        {
            var r = ctx.ShowResult(ctx.Ops.ImportScene(path, UiContext.User()));
            if (r != null && r.Ok)
            {
                ctx.Toasts?.Show(L10n.Format("files.imported", Path.GetFileNameWithoutExtension(path), r.NewIds.Count), ToastKind.Success);
                Done("imported");
            }
            else Done("failed: " + r?.Message);
            return r;
        }

        // ------------------------------------------------------------------ save

        bool Hs2Preset => ctx.Keys?.PresetName == Keymap.Hs2StudioPreset;

        /// <summary>Save (Ctrl+S): overwrite the current file; untitled → Save As (hs2studio: the timestamped name at once, U4 / U17).</summary>
        public void Save(Action<bool> done = null)
        {
            if (CurrentPath != null) { bool ok0 = SaveTo(CurrentPath, null); done?.Invoke(ok0); return; }
            if (Hs2Preset)
            {
                string p = SceneLibrary.ResolveSaveName(SceneDir, SceneLibrary.DefaultName(DateTime.Now), out _);
                bool ok1 = p != null && SaveTo(p, null); done?.Invoke(ok1);
                return;
            }
            SaveAs(done);
        }

        /// <summary>Save As (Ctrl+Shift+S): name and title prompt under &lt;UserData&gt;/scene, overwrite confirmation.</summary>
        public void SaveAs(Action<bool> done = null)
        {
            if (ctx.Dialogs == null) { done?.Invoke(false); return; }
            var box = new VisualElement { name = "save-as" };
            box.AddToClassList("hs-form");
            var nameField = new TextField { name = "save-as-name", value = CurrentPath != null ? Path.GetFileNameWithoutExtension(CurrentPath) : SceneLibrary.DefaultName(DateTime.Now) };
            Loc.Bind(nameField, "files.fileName", (e, t) => ((TextField)e).label = t);
            var titleField = new TextField { name = "save-as-title", value = ctx.Model.Title ?? "" };
            Loc.Bind(titleField, "files.title", (e, t) => ((TextField)e).label = t);
            var problem = new Label { name = "save-as-problem" };
            problem.AddToClassList("hs-form__problem");
            box.Add(nameField);
            box.Add(titleField);
            box.Add(problem);
            var spec = new DialogSpec { Id = "saveAs", Title = L10n.Get("action.file.saveAs"), Content = box };
            spec.Buttons.Add(DialogButton.Of("save", "dialog.save", isDefault: true));
            spec.Buttons.Add(DialogButton.Cancel());
            string target = null;
            spec.CanClose = id =>
            {
                if (id != "save") return true;
                target = SceneLibrary.ResolveSaveName(SceneDir, nameField.value, out var why);
                if (target == null) { problem.text = L10n.Get("files.badName." + why); return false; }
                return true;
            };
            ctx.Dialogs.Show(spec, id =>
            {
                if (id != "save" || target == null) { Done("cancelled"); done?.Invoke(false); return; }
                string title = titleField.value ?? "";
                if (File.Exists(target) && !string.Equals(target, CurrentPath, StringComparison.OrdinalIgnoreCase))
                    ctx.Dialogs.Confirm("dialog.overwrite.title", "dialog.overwrite.message", ok =>
                    {
                        if (ok) { bool ok2 = SaveTo(target, title); done?.Invoke(ok2); }
                        else { Done("cancelled"); done?.Invoke(false); }
                    }, Path.GetFileName(target));
                else { bool ok3 = SaveTo(target, title); done?.Invoke(ok3); }
            });
        }

        /// <summary>Writes the scene (title first when given and changed: one entry "Title"). True on success.</summary>
        public bool SaveTo(string fullPath, string title)
        {
            if (title != null && !string.Equals(title, ctx.Model.Title ?? "", StringComparison.Ordinal))
                ctx.ShowResult(ctx.Ops.SetTitle(title, UiContext.User()));
            try { Directory.CreateDirectory(Path.GetDirectoryName(fullPath)); }
            catch (Exception e) { ctx.Toasts?.Show(L10n.Format("msg.saveFailed", e.Message), ToastKind.Error); Done("failed: " + e.Message); return false; }
            var r = ctx.Ops.SaveScene(fullPath, new SaveOptions { Thumbnail = ThumbnailMode.Render });
            if (!r.Ok)
            {
                ctx.Toasts?.Show(L10n.Format("msg.saveFailed", r.Message), ToastKind.Error);
                Done("failed: " + r.Message);
                return false;
            }
            CurrentPath = fullPath;
            Recent.Touch(fullPath);
            ctx.Toasts?.Show(L10n.Format("files.saved", Path.GetFileName(fullPath)), ToastKind.Success);
            Done("saved");
            return true;
        }

        // ------------------------------------------------------------------ recent / quit

        /// <summary>The File ▸ Recent submenu (missing files disabled).</summary>
        public List<MenuItem> RecentMenu()
        {
            var l = new List<MenuItem>();
            int i = 0;
            foreach (var f in Recent.Files)
            {
                string p = f;
                l.Add(new MenuItem { Id = "recent-" + i++, Text = Path.GetFileNameWithoutExtension(p), Disabled = !File.Exists(p), Run = () => Load(p) });
            }
            if (l.Count == 0) l.Add(new MenuItem { Id = "recent-none", Text = L10n.Get("files.noRecent"), Disabled = true });
            return l;
        }

        /// <summary>File ▸ Quit / app.quit: asks about unsaved changes, then quits.</summary>
        public void Quit() => IfSaved(() => { quitConfirmed = true; Done("quit"); Application.Quit(); }, "dialog.unsaved.quit");

        /// <summary>Application.wantsToQuit: true lets the quit through. A changed scene asks first (Save / Don't save /
        /// Cancel), except for the bridge's unattended app.quit (AppCommands.QuitRequested).</summary>
        public bool WantsToQuit()
        {
            if (quitConfirmed || HS2Next.App.Commands.AppCommands.QuitRequested || !IsDirty || ctx.Dialogs == null) return true;
            if (!prompting) Quit();
            return false;
        }
    }
}
