// The Options panel's sections of the settings file (Folders, Content, Automation, Advanced): one row per key of the key table
// (SettingsSchema), each written into the settings file the app reads (Config/settings.local.json under the data root, or the
// file -settings names) with every other member - comments, unknown keys, their order - kept; a value is checked with the
// settings stage's rules first. Every key applies at the next start: a changed row says so and the section foot offers
// Restart now (a player restarts with the same arguments; an Editor play session says how). A value given on the command line
// is shown locked. Folders also shows the data root and its mode (Open folder), the HS2 folder (Change… opens the guide's HS2
// folder box in a dialog), the mods folders, UserData (Change… with "copy my files there"), the cache (its size on disk, Open,
// Change…), the fallback banner while a folder fell back to the per-user folder, and "Copy files from an earlier HS2Next
// folder…". Automation lists the plugins with a toggle each (plugins.disabled) and the token file's place with Copy path.
// Advanced ends with the about part: the build (kind, version, commit), the notice on the compatible shaders (HS2Next's own
// replacements of mod and asset-pack shaders, written from measurements, not the original authors' work; the shader families
// with the number of their HS2Next/Compatible/ shaders from ShaderMap.CompatibleFamilies), the notice on the plugin data it
// reads for compatibility (the plugins of PluginDataNames) and Unity's trademark and copyright lines, in English in every
// language.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HS2Next.App;
using HS2Next.App.Setup;
using HS2Next.UI.Core;
using HS2Next.UI.Setup;
using HS2Next.UI.Widgets;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI.Panels
{
    public sealed class OptionsSettingsSections
    {
        /// <summary>The settings sections, after the options sections of the panel.</summary>
        public static readonly string[] Sections = SettingsSchema.Sections;

        readonly UiContext ctx;
        readonly OptionsPanel panel;
        readonly List<Action> refreshers = new List<Action>();
        readonly List<Button> restartButtons = new List<Button>();
        readonly HashSet<string> changed = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The command line the rows read their locks from (tests may set it; null = the process's).</summary>
        public static string[] ArgsOverride;
        static string[] CommandLineArgs() => ArgsOverride ?? Environment.GetCommandLineArgs();

        /// <summary>The keys written in this session (they apply at the next start).</summary>
        public static readonly HashSet<string> ChangedThisSession = new HashSet<string>(StringComparer.Ordinal);

        AppCore App => ctx.App;
        string File => App?.Paths?.SettingsFile ?? App?.Settings?.SettingsPath;

        public OptionsSettingsSections(UiContext ctx, OptionsPanel panel)
        {
            this.ctx = ctx;
            this.panel = panel;
            foreach (var k in ChangedThisSession) changed.Add(k);
        }

        public static bool IsSettingsSection(string s) => Array.IndexOf(Sections, s) >= 0;

        JObject ReadFile()
        {
            try { return SettingsFile.Read(File); }
            catch (Exception e)
            {
                ctx.Toasts?.Show(L10n.Format("settings.unreadable", File, e.Message), ToastKind.Error, 8f);
                return new JObject();
            }
        }

        /// <summary>The value shown for a key: the command line's, the file's, else the default.</summary>
        JToken ValueOf(SettingDef d, JObject file, out string source)
        {
            var a = SettingsSchema.ArgValue(d, CommandLineArgs());
            if (a != null) { source = "arg"; return a; }
            var f = SettingsFile.Get(file, d.Key);
            if (f != null) { source = "file"; return f; }
            source = "default";
            return HS2Next.App.Commands.SetupCommands.DefaultOf(d);
        }

        /// <summary>Writes one key (null removes it); false with a toast when the value is refused or the file cannot be written.</summary>
        public bool Write(string key, JToken value)
        {
            if (!SettingsSchema.TryGet(key, out var d)) return false;
            JToken v;
            try { v = SettingsSchema.Validate(d, value); }
            catch (FormatException e)
            {
                ctx.Toasts?.Show(e.Message, ToastKind.Error, 6f);
                return false;
            }
            try
            {
                var w = SettingsFile.Write(File, new[] { new KeyValuePair<string, JToken>(key, v) });
                if (w.Count > 0)
                {
                    changed.Add(key);
                    ChangedThisSession.Add(key);
                    Debug.Log("[ui] settings: " + key + " written to " + File + " (applies after a restart)");
                }
                Refresh();
                return true;
            }
            catch (Exception e)
            {
                ctx.Toasts?.Show(L10n.Format("settings.writeFailed", File, e.Message), ToastKind.Error, 8f);
                return false;
            }
        }

        public void Refresh()
        {
            foreach (var r in refreshers) UiContext.Guard(r);
            foreach (var b in restartButtons) b.style.display = changed.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ---------------------------------------------------------------- sections

        public VisualElement Build(string s)
        {
            var box = new VisualElement { name = "options-section-" + s };
            box.AddToClassList("hs-options__section");
            var title = Loc.Label("option.section." + s);
            title.AddToClassList("hs-options__section-title");
            box.Add(title);
            var note = Loc.Label("settings.sectionNote");
            note.AddToClassList("hs-hint");
            box.Add(note);
            var file = ReadFile();
            switch (s)
            {
                case "folders": BuildFolders(box, file); break;
                case "automation":
                    foreach (var d in SettingsSchema.InSection(s).Where(x => x.Key != "plugins.disabled")) box.Add(Row(d));
                    BuildPlugins(box);
                    break;
                default:
                    foreach (var d in SettingsSchema.InSection(s)) box.Add(Row(d));
                    if (s == "advanced") BuildAbout(box);
                    break;
            }
            var restart = Loc.Button("settings.restartNow", RestartNow);
            restart.name = "settings-restart-" + s;
            restart.AddToClassList("hs-setup__default");
            var hint = Loc.Label("settings.restartPending");
            hint.AddToClassList("hs-hint");
            var foot = PanelKit.Row("hs-options__buttons", restart);
            box.Add(foot);
            restartButtons.Add(restart);
            restart.style.display = changed.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            return box;
        }

        void RestartNow()
        {
            if (!AppRestart.CanRestart) { ctx.Toasts?.Show(L10n.Get("setup.restart.editor"), ToastKind.Info, 8f); return; }
            ctx.Options.Flush();
            // the Options dialog closes first (a second modal dialog would wait behind it); the quit's own prompt asks about
            // unsaved changes, and Cancel keeps the app running
            ctx.Dialogs.CloseCurrent();
            ctx.Dialogs.Confirm("setup.restart.title", "settings.restartMessage", ok =>
            {
                if (!ok) return;
                string why = AppRestart.Request(ctx, new[] { SetupStage.Flag });
                if (why != null) ctx.Toasts?.Show(why, ToastKind.Error, 8f);
            });
        }

        /// <summary>One row: label, the control for the key's kind, the restart mark; locked when the command line sets it.</summary>
        VisualElement Row(SettingDef d)
        {
            var row = new VisualElement { name = "setting-" + d.Key };
            row.AddToClassList("hs-options__row");
            VisualElement control;
            Action<JToken> show;
            switch (d.Kind)
            {
                case SettingKind.Bool:
                {
                    var t = new Toggle { name = "setting-control-" + d.Key };
                    Loc.Bind(t, d.LabelKey, (e, x) => ((Toggle)e).label = x);
                    t.RegisterValueChangedCallback(e => Write(d.Key, e.newValue));
                    show = v => t.SetValueWithoutNotify(v?.Type == JTokenType.Boolean && (bool)v);
                    control = t;
                    break;
                }
                case SettingKind.Int:
                {
                    var f = new IntegerField { name = "setting-control-" + d.Key, isDelayed = true };
                    Loc.Bind(f, d.LabelKey, (e, x) => ((IntegerField)e).label = x);
                    f.RegisterValueChangedCallback(e => { if (!Write(d.Key, e.newValue)) Refresh(); });
                    show = v => f.SetValueWithoutNotify(v != null && v.Type == JTokenType.Integer ? (int)v : 0);
                    control = f;
                    break;
                }
                case SettingKind.Float:
                {
                    var f = new FloatField { name = "setting-control-" + d.Key, isDelayed = true };
                    Loc.Bind(f, d.LabelKey, (e, x) => ((FloatField)e).label = x);
                    f.RegisterValueChangedCallback(e => { if (!Write(d.Key, Math.Round(e.newValue, 4))) Refresh(); });
                    show = v => f.SetValueWithoutNotify(v != null && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? (float)v : 0f);
                    control = f;
                    break;
                }
                case SettingKind.Token:
                {
                    var f = new DropdownField { name = "setting-control-" + d.Key, choices = new List<string> { "off", "required" } };
                    Loc.Bind(f, d.LabelKey, (e, x) => ((DropdownField)e).label = x);
                    f.formatListItemCallback = c => L10n.Get("settings.token." + c);
                    f.formatSelectedValueCallback = f.formatListItemCallback;
                    f.RegisterValueChangedCallback(e => Write(d.Key, e.newValue == "required"));
                    show = v => f.SetValueWithoutNotify(v != null && ((v.Type == JTokenType.Boolean && (bool)v) || (v.Type == JTokenType.String && ((string)v).Length > 0)) ? "required" : "off");
                    control = f;
                    break;
                }
                case SettingKind.Choice:
                {
                    var f = new DropdownField { name = "setting-control-" + d.Key, choices = d.Choices.ToList() };
                    Loc.Bind(f, d.LabelKey, (e, x) => ((DropdownField)e).label = x);
                    f.RegisterValueChangedCallback(e => Write(d.Key, e.newValue));
                    show = v => f.SetValueWithoutNotify((string)v ?? (string)d.Default);
                    control = f;
                    break;
                }
                default:
                {
                    var f = new TextField { name = "setting-control-" + d.Key, isDelayed = true };
                    Loc.Bind(f, d.LabelKey, (e, x) => ((TextField)e).label = x);
                    f.RegisterValueChangedCallback(e => { if (!Write(d.Key, string.IsNullOrWhiteSpace(e.newValue) ? null : (JToken)e.newValue.Trim())) Refresh(); });
                    show = v => f.SetValueWithoutNotify(v?.Type == JTokenType.String ? (string)v : "");
                    control = f;
                    break;
                }
            }
            control.AddToClassList("hs-grow");
            row.Add(control);
            var mark = new Label { name = "setting-restart-" + d.Key };
            mark.AddToClassList("hs-hint");
            row.Add(mark);
            panel.SettingsControls[d.Key] = control;
            refreshers.Add(() =>
            {
                var v = ValueOf(d, ReadFile(), out string source);
                show(v);
                bool locked = source == "arg";
                control.SetEnabled(!locked);
                mark.text = locked ? L10n.Get("settings.locked") : changed.Contains(d.Key) ? L10n.Get("settings.afterRestart") : L10n.Get("settings.restartMark");
                row.tooltip = L10n.Get("settings.afterRestartTip") + (locked ? "\n" + L10n.Get("options.lockedByCommandLine") : "") + "\n" + d.Key;
            });
            refreshers[refreshers.Count - 1]();
            return row;
        }

        // ---------------------------------------------------------------- Folders

        void BuildFolders(VisualElement box, JObject file)
        {
            var p = App?.Paths;
            var roots = p?.Roots;
            if (p?.Fallbacks != null && p.Fallbacks.Count > 0)
            {
                var banner = new Label(string.Join("\n", p.Fallbacks.Select(f => L10n.Format("msg.folderFallback", f.From, f.To)))) { name = "settings-fallback" };
                banner.AddToClassList("hs-setup-status");
                banner.AddToClassList("hs-setup-status--warning");
                box.Add(banner);
            }
            var rootLine = new Label(L10n.Format("settings.dataRoot", roots?.DataRoot ?? "?", L10n.Get("settings.mode." + (roots?.ModeName ?? "editor")))) { name = "settings-dataRoot" };
            rootLine.AddToClassList("hs-setup-text");
            box.Add(PanelKit.Row("hs-options__row", rootLine, Named(Loc.Button("setup.files.open", () => SetupGuide.OpenFolder(roots?.DataRoot)), "settings-dataRoot-open")));
            var fileLine = new Label(L10n.Format("settings.file", File)) { name = "settings-file" };
            fileLine.AddToClassList("hs-hint");
            box.Add(fileLine);

            // the HS2 folder: the guide's box in a dialog
            SettingsSchema.TryGet("hs2Root", out var hs2Def);
            box.Add(FolderRow(hs2Def, () =>
            {
                Hs2FolderBox fb = null;
                var spec = new DialogSpec { Id = "settings-hs2", Title = L10n.Get("setup.title.hs2") };
                fb = new Hs2FolderBox(() => ctx.Dialogs, (string)SettingsFile.Get(ReadFile(), "hs2Root") ?? p?.Hs2Root);
                spec.Content = fb;
                spec.BoxClass = "hs-setup-picker-box";
                spec.Buttons.Add(DialogButton.Of("ok", "common.ok", isDefault: true));
                spec.Buttons.Add(DialogButton.Cancel());
                spec.CanClose = id => id != "ok" || (fb.Verdict != null && fb.Verdict.Usable);
                ctx.Dialogs.Show(spec, id => { if (id == "ok" && fb.Verdict != null && fb.Verdict.Usable) Write("hs2Root", fb.Verdict.Path); });
            }, null));

            // mods folders
            SettingsSchema.TryGet("modsFolders", out var modsDef);
            box.Add(ModsRows(modsDef));

            // UserData: Change… with "copy my files there"
            SettingsSchema.TryGet("userData", out var udDef);
            box.Add(FolderRow(udDef, () => FolderPicker.Open(ctx.Dialogs, "settings.userData.pickTitle", p?.UserData, picked =>
            {
                if (picked == null) return;
                var copy = new Toggle { name = "settings-userData-copy", value = true };
                Loc.Bind(copy, "settings.userData.copy", (e, x) => ((Toggle)e).label = x);
                var spec = new DialogSpec { Id = "settings-userData", Title = L10n.Get("settings.userData.title"), Message = L10n.Format("settings.userData.message", picked), Content = copy };
                spec.Buttons.Add(DialogButton.Ok());
                spec.Buttons.Add(DialogButton.Cancel());
                ctx.Dialogs.Show(spec, id =>
                {
                    if (id != "ok") return;
                    if (copy.value && p?.UserData != null) RunCopy(p.UserData, picked, () => Write("userData", picked));
                    else Write("userData", picked);
                });
            }), () => SetupGuide.OpenFolder(p?.UserData)));

            // the cache: its size, Open, Change…
            SettingsSchema.TryGet("cacheDir", out var cacheDef);
            var size = new Label { name = "settings-cache-size" };
            size.AddToClassList("hs-hint");
            box.Add(FolderRow(cacheDef, () => FolderPicker.Open(ctx.Dialogs, "setup.cache.pickTitle", p?.CacheDir, picked => { if (picked != null) Write("cacheDir", picked); }),
                () => SetupGuide.OpenFolder(p?.CacheDir)));
            box.Add(size);
            string cacheDir = p?.CacheDir;
            size.text = L10n.Get("settings.cache.measuring");
            var measure = Task.Run(() => FolderBytes(cacheDir));
            size.schedule.Execute(() =>
            {
                if (!measure.IsCompleted) return;
                size.text = L10n.Format("settings.cache.size", (measure.IsFaulted ? 0 : measure.Result / (1024.0 * 1024 * 1024)).ToString("0.00"), cacheDir ?? "");
            }).Until(() => measure.IsCompleted && size.text != L10n.Get("settings.cache.measuring")).Every(200);

            // the copy from an earlier folder
            var copyBtn = Loc.Button("settings.migrate.button", () => FolderPicker.Open(ctx.Dialogs, "settings.migrate.pickTitle", roots?.ExeDir, picked =>
            {
                if (picked == null || p?.UserData == null) return;
                RunCopy(UserDataMigration.NormaliseSource(picked), p.UserData, null);
            }));
            copyBtn.name = "settings-migrate";
            box.Add(PanelKit.Row("hs-options__buttons", copyBtn));
        }

        static T Named<T>(T e, string n) where T : VisualElement { e.name = n; return e; }

        /// <summary>A folder key's row: its text field (typed paths written as given), Change… and, when given, Open.</summary>
        VisualElement FolderRow(SettingDef d, Action change, Action open)
        {
            var row = (VisualElement)Row(d);
            var c = Named(Loc.Button("setup.cache.changeButton", change), "setting-change-" + d.Key);
            row.Insert(row.childCount - 1, c);
            if (open != null) row.Insert(row.childCount - 1, Named(Loc.Button("setup.files.open", open), "setting-open-" + d.Key));
            if (SettingsSchema.FromCommandLine(d, CommandLineArgs())) c.SetEnabled(false);
            return row;
        }

        VisualElement ModsRows(SettingDef d)
        {
            var box = new VisualElement { name = "setting-" + d.Key };
            var title = Loc.Label(d.LabelKey);
            title.AddToClassList("hs-options__label");
            box.Add(title);
            var list = new VisualElement { name = "setting-list-" + d.Key };
            box.Add(list);
            var mark = new Label { name = "setting-restart-" + d.Key };
            mark.AddToClassList("hs-hint");
            bool locked = SettingsSchema.FromCommandLine(d, CommandLineArgs());
            List<string> Current() => (SettingsFile.Get(ReadFile(), d.Key) as JArray)?.Select(t => (string)t).Where(x => !string.IsNullOrEmpty(x)).ToList() ?? new List<string>();
            void Fill()
            {
                list.Clear();
                var cur = Current();
                for (int i = 0; i < cur.Count; i++)
                {
                    int k = i;
                    var l = new Label(cur[i]) { name = "setting-mods-item-" + i };
                    l.AddToClassList("hs-grow");
                    var rm = new Button(() => { var c2 = Current(); if (k < c2.Count) { c2.RemoveAt(k); Write(d.Key, new JArray(c2)); } }) { name = "setting-mods-remove-" + i, text = L10n.Get("setup.mods.remove") };
                    rm.SetEnabled(!locked);
                    list.Add(PanelKit.Row("hs-options__row", l, rm));
                }
                if (cur.Count == 0) { var none = Loc.Label("setup.mods.none"); none.AddToClassList("hs-hint"); list.Add(none); }
                mark.text = locked ? L10n.Get("settings.locked") : changed.Contains(d.Key) ? L10n.Get("settings.afterRestart") : L10n.Get("settings.restartMark");
            }
            refreshers.Add(Fill);
            Fill();
            var add = Named(Loc.Button("setup.mods.addButton", () => FolderPicker.Open(ctx.Dialogs, "setup.mods.pickTitle", null, picked =>
            {
                if (picked == null) return;
                var c2 = Current();
                if (c2.Any(x => SetupGuide.SameFolder(x, picked))) return;
                c2.Add(picked);
                Write(d.Key, new JArray(c2));
            })), "setting-mods-add");
            add.SetEnabled(!locked);
            box.Add(PanelKit.Row("hs-options__buttons", add, mark));
            return box;
        }

        /// <summary>Copies an earlier UserData folder (the one migration's rules) into <paramref name="dest"/> on a worker, with a
        /// toast for the result; <paramref name="then"/> runs after a copy without an error.</summary>
        void RunCopy(string source, string dest, Action then)
        {
            ctx.Toasts?.Show(L10n.Format("settings.migrate.started", source), ToastKind.Info, 4f);
            string settings = App?.Settings?.SettingsSource == "default" && SetupGuide.SameFolder(dest, App?.Paths?.UserData) ? App.Settings.SettingsPath : null;
            var task = Task.Run(() =>
            {
                try
                {
                    var r = UserDataMigration.Run(source, dest, settings, null);
                    return L10n.Format("setup.migrate.done", r.Copied, (r.Bytes / 1048576.0).ToString("0.0"), r.Conflicts.Count) + (r.Refused.Count > 0 ? " " + L10n.Format("setup.migrate.refused", r.Refused.Count) : "");
                }
                catch (MigrationException e) { return "!" + L10n.Format("setup.migrate.failed", e.Message); }
                catch (Exception e) { return "!" + L10n.Format("setup.migrate.failed", e.GetType().Name + ": " + e.Message); }
            });
            panel.schedule.Execute(() =>
            {
                if (!task.IsCompleted) return;
                string msg = task.Result;
                bool failed = msg.StartsWith("!", StringComparison.Ordinal);
                ctx.Toasts?.Show(failed ? msg.Substring(1) : msg, failed ? ToastKind.Error : ToastKind.Success, 10f);
                Debug.Log("[ui] settings: copy from " + source + " to " + dest + ": " + (failed ? msg.Substring(1) : msg));
                if (!failed) then?.Invoke();
            }).Until(() => task.IsCompleted).Every(200);
        }

        static long FolderBytes(string dir)
        {
            long n = 0;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
            var stack = new Stack<string>();
            stack.Push(dir);
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                try
                {
                    foreach (var f in Directory.EnumerateFiles(d)) { try { n += new FileInfo(f).Length; } catch (Exception) { } }
                    foreach (var s in Directory.EnumerateDirectories(d))
                    {
                        try { if ((File_GetAttributes(s) & FileAttributes.ReparsePoint) != 0) continue; } catch (Exception) { continue; }
                        stack.Push(s);
                    }
                }
                catch (Exception) { }
            }
            return n;
        }

        static FileAttributes File_GetAttributes(string p) => System.IO.File.GetAttributes(p);

        // ---------------------------------------------------------------- Automation: plugins and the token file

        void BuildPlugins(VisualElement box)
        {
            string token = App?.Paths?.BridgeDir != null ? Path.Combine(App.Paths.BridgeDir, "token") : null;
            if (token != null)
            {
                var line = new Label(L10n.Format("settings.tokenFile", token)) { name = "settings-tokenFile" };
                line.AddToClassList("hs-hint");
                var copy = Named(Loc.Button("settings.copyPath", () => { GUIUtility.systemCopyBuffer = token; ctx.Toasts?.Show(L10n.Get("settings.copied"), ToastKind.Info, 3f); }), "settings-tokenFile-copy");
                box.Add(PanelKit.Row("hs-options__row", line, copy));
            }
            var title = Loc.Label("settings.plugins");
            title.AddToClassList("hs-options__label");
            box.Add(title);
            var entries = App?.Plugins?.Entries;
            if (entries == null || entries.Count == 0)
            {
                var none = Loc.Label("settings.plugins.none");
                none.AddToClassList("hs-hint");
                box.Add(none);
                return;
            }
            SettingsSchema.TryGet("plugins.disabled", out var d);
            bool locked = SettingsSchema.FromCommandLine(d, CommandLineArgs());
            foreach (var e in entries)
            {
                string guid = e.Guid;
                if (string.IsNullOrEmpty(guid)) continue;
                var t = new Toggle { name = "setting-plugin-" + guid, label = guid + " (" + e.State + ")" };
                List<string> Disabled() => (SettingsFile.Get(ReadFile(), "plugins.disabled") as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
                t.SetValueWithoutNotify(!Disabled().Any(x => string.Equals(x, guid, StringComparison.OrdinalIgnoreCase)));
                t.RegisterValueChangedCallback(ev =>
                {
                    var l = Disabled().Where(x => !string.Equals(x, guid, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (!ev.newValue) l.Add(guid);
                    Write("plugins.disabled", new JArray(l));
                });
                t.SetEnabled(!locked);
                box.Add(t);
            }
        }

        // ---------------------------------------------------------------- Advanced: the about part

        /// <summary>The community plugins whose data HS2Next reads with its own code: those whose character-card, outfit-card
        /// or pose-file data it decodes, ExtendedSave (the container format that holds that data) and Sideloader (its zipmod
        /// packages and resolve entries); a plugin whose data is only kept as opaque bytes is not listed. Ordinal order.</summary>
        public static IReadOnlyList<string> PluginDataNames { get; } = Array.AsReadOnly(new[]
        {
            "ABMX", "AdditionalAccessoryControls", "DynamicBoneEditor", "ExtendedSave", "InvisibleBody", "KCOX", "KSOX",
            "MaterialEditor", "MoreAccessories", "PoseTools", "PushUp", "Sideloader", "UncensorSelector",
        });

        /// <summary>Unity's notice, shown in English in every language (a trademark notice is not translated).</summary>
        public const string UnityTrademark = "HS2Next was made with Unity®. Unity is a trademark or registered trademark of Unity Technologies";
        public const string UnityCopyright = "Copyright © 2005-2026 Unity Technologies. All rights reserved.";

        void BuildAbout(VisualElement box)
        {
            var b = BuildInfo.Current;
            var l = new Label(AboutText()) { name = "settings-build" };
            l.AddToClassList("hs-hint");
            box.Add(l);
            box.Add(NoticeBlock("settings-compat", "about.compat.title", "about.compat.text",
                ShaderMap.CompatibleFamilies.Select(f => f.Key + " (" + f.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")")));
            box.Add(NoticeBlock("settings-plugindata", "about.plugins.title", "about.plugins.text", PluginDataNames));
            var unity = new VisualElement { name = "settings-unity" };
            unity.Add(Line(UnityTrademark, "settings-unity-trademark"));
            unity.Add(Line(UnityCopyright, "settings-unity-copyright"));
            box.Add(unity);
        }

        /// <summary>A notice of the about part: its heading, one paragraph and one line per entry.</summary>
        static VisualElement NoticeBlock(string name, string titleKey, string textKey, IEnumerable<string> entries)
        {
            var block = new VisualElement { name = name };
            var title = Loc.Label(titleKey);
            title.name = name + "-title";
            title.AddToClassList("hs-options__section-title");
            title.style.marginTop = 8;
            block.Add(title);
            var text = Loc.Label(textKey);
            text.name = name + "-text";
            text.AddToClassList("hs-hint");
            block.Add(text);
            var list = new VisualElement { name = name + "-list" };
            foreach (var s in entries) list.Add(Line(s, name + "-entry"));
            block.Add(list);
            return block;
        }

        /// <summary>One line of a notice (no padding above or below, so a list stays compact).</summary>
        static Label Line(string text, string name)
        {
            var l = new Label(text) { name = name };
            l.AddToClassList("hs-hint");
            l.style.paddingTop = 0;
            l.style.paddingBottom = 0;
            return l;
        }

        /// <summary>"Version x · Build <kind> · Commit <c>" (Help ▸ About and the Advanced section).</summary>
        public static string AboutText()
        {
            var b = BuildInfo.Current;
            string kind = b == null ? "?" : b.Kind == "distribution" ? L10n.Get("about.build.dist") : b.Kind == "editor" ? L10n.Get("about.build.editor") : L10n.Get("about.build.dev");
            return L10n.Format("about.line", b?.Version ?? Application.version, L10n.Get("about.build"), kind, L10n.Get("about.commit"),
                string.IsNullOrEmpty(b?.Commit) ? "-" : b.Commit + (b.Dirty == true ? "+" : ""));
        }
    }
}
