// Package C's files module: the actions file.new, file.open,
// file.save, file.saveAs (args {name, title, overwrite} save without the prompt: ui.action automation and UI1),
// file.import (args {path}), file.options, app.quit, help.keys; the scene file state (SceneFiles: current file, recent
// files, the unsaved-changes prompt, the quit hook).
using System;
using System.IO;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;

namespace HS2Next.UI.Panels
{
    public sealed class FilesModule : IUiModule
    {
        public int Order => 120;

        public void Register(UiContext ctx)
        {
            var files = new SceneFiles(ctx);
            var a = ctx.Registry.Actions;
            a.Register(ActionIds.FileNew, call =>
            {
                if (call.Arg("force") is bool f && f) return files.NewNow();
                files.New();
                return null;
            });
            a.Register(ActionIds.FileOpen, call =>
            {
                if (call.Arg("path") is string p && p.Length > 0) { string full = Resolve(files, p); files.Load(full); return full; }
                files.Open(false);
                return null;
            });
            a.Register(ActionIds.FileSave, _ => { files.Save(); return files.CurrentPath; });
            a.Register(ActionIds.FileSaveAs, call =>
            {
                if (call.Arg("name") is string n && n.Length > 0)
                {
                    string full = SceneLibrary.ResolveSaveName(files.SceneDir, n, out var why);
                    if (full == null) return "bad name: " + why;
                    bool overwrite = call.Arg("overwrite") is bool o && o;
                    if (File.Exists(full) && !overwrite && !string.Equals(full, files.CurrentPath, StringComparison.OrdinalIgnoreCase)) return "exists";
                    return files.SaveTo(full, call.Arg("title") as string) ? full : files.LastOutcome;
                }
                files.SaveAs();
                return null;
            });
            a.Register(ActionIds.FileImport, call =>
            {
                if (call.Arg("path") is string p && p.Length > 0) return files.Import(Resolve(files, p))?.NewIds;
                files.Open(true);
                return null;
            });
            a.Register(ActionIds.FileOptions, call =>
            {
                var op = OptionsPanel.Show(ctx);
                if (op != null && call.Arg("option") is string key) op.Reveal(key);   // args {option}: scrolled to that row (Phase 3.6 G)
                return null;
            });
            a.Register(ActionIds.AppQuit, _ => { files.Quit(); return null; });
            a.Register(ActionIds.HelpKeys, _ => { KeyOverlay.Show(ctx); return null; });
            FilePicker.DefaultBrowser = (kind, current, done) => ImageBrowser.Show(ctx, kind, current, done);
            HookRecentMenu(files);
        }

        /// <summary>
        /// File ▸ Recent: the toolbar's static hook `UiToolbar.RecentItems` (Func&lt;List&lt;MenuItem&gt;&gt;, requested from package A in
        /// the questions file), set by reflection so this compiles before and after the hook exists.
        /// </summary>
        static void HookRecentMenu(SceneFiles files)
        {
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
            Func<System.Collections.Generic.List<MenuItem>> provider = files.RecentMenu;
            var t = typeof(UiToolbar);
            var field = t.GetField("RecentItems", F);
            if (field != null && field.FieldType.IsAssignableFrom(provider.GetType())) { field.SetValue(null, provider); return; }
            var prop = t.GetProperty("RecentItems", F);
            if (prop != null && prop.CanWrite && prop.PropertyType.IsAssignableFrom(provider.GetType())) prop.SetValue(null, provider);
        }

        /// <summary>A path argument: absolute, or relative to &lt;UserData&gt;/scene (extension appended when missing).</summary>
        static string Resolve(SceneFiles files, string p)
        {
            if (!p.EndsWith(HS2Next.Save.SaveFormats.SceneExtension, StringComparison.OrdinalIgnoreCase)) p += HS2Next.Save.SaveFormats.SceneExtension;
            return Path.IsPathRooted(p) ? p : Path.Combine(files.SceneDir ?? "", p);
        }
    }
}
