// The toolbar and the status bar. Toolbar, left to right: File menu · Undo /
// Redo (labels "Undo <entry>", history drop-down) · Add menu · tools Select / Move / Rotate / Scale · space · pivot ·
// snap · camera menu (reset, frame, slots, active camera / free camera) · view menu (toggles, panels) ·
// language · help. Every button runs an action of the registry (one behaviour for keys, menus, toolbar and ui.action);
// buttons are named "tb-<action id>" for ui.query. Status bar: selection summary, the latest finding ≥ warning (click
// shows the findings), dirty mark and file name, FPS / frame ms (option view.fps).
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Scenes;
using HS2Next.UI.Core;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

namespace HS2Next.UI
{
    public sealed class UiToolbar
    {
        readonly UiContext ctx;
        public VisualElement Element { get; }
        readonly Dictionary<string, Button> buttons = new Dictionary<string, Button>(StringComparer.Ordinal);
        Button undo, redo;
        float nextTick;

        /// <summary>The playback group (Phase 3.6 package K).</summary>
        public UiPlayback Playback { get; }

        /// <summary>The File ▸ Recent submenu (package C's scene files set it); null = the row is disabled.</summary>
        public static Func<List<MenuItem>> RecentItems;

        public UiToolbar(UiContext ctx)
        {
            this.ctx = ctx;
            Element = new VisualElement { name = "toolbar" };
            Element.AddToClassList("hs-toolbar");
            Menu("tb-file", "toolbar.file", FileMenu);
            Sep();
            undo = Act(ActionIds.Undo, "↶");
            redo = Act(ActionIds.Redo, "↷");
            Menu("tb-history", "toolbar.history", HistoryMenu, "▾");
            Sep();
            Menu("tb-add", "toolbar.add", AddMenu);
            Sep();
            Act(ActionIds.ToolSelect, "⬚");
            Act(ActionIds.ToolMove, "✥");
            Act(ActionIds.ToolRotate, "⟳");
            Act(ActionIds.ToolScale, "⤢");
            Act(ActionIds.ToolSpace, null);
            Act(ActionIds.ToolPivot, null);
            Act(ActionIds.ToolSnapToggle, null);
            Sep();
            Menu("tb-camera", "toolbar.camera", CameraMenu);
            Act(ActionIds.CaptureScreenshot, "◉"); Act(ActionIds.CaptureDialog, "▾");   // Phase 3.6 package C: screenshot (F11) and its dialog (Shift+F11)
            Playback = new UiPlayback(ctx, Element);   // Phase 3.6 package K: Pause / Resume, Step, scene time (UiPlayback.cs)
            Menu("tb-view", "toolbar.view", ViewMenu);
            var sp = new VisualElement();
            sp.AddToClassList("hs-toolbar__spacer");
            Element.Add(sp);
            Menu("tb-language", "toolbar.language", LanguageMenu);
            Menu("tb-help", "toolbar.help", HelpMenu);
            ctx.HistoryChanged += _ => Refresh();
            ctx.ViewStateChanged += _ => Refresh();
            ctx.SelectionChanged += _ => Refresh();
            Refresh();
        }

        void Sep()
        {
            var s = new VisualElement();
            s.AddToClassList("hs-toolbar__sep");
            Element.Add(s);
        }

        Button Act(string id, string glyph)
        {
            var b = new Button(() => ctx.Registry.Run(id, "toolbar")) { name = "tb-" + id };
            b.AddToClassList("hs-toolbar__button");
            if (glyph != null) { b.text = glyph; Loc.Tooltip(b, ActionIds.LabelKey(id)); }
            else Loc.Bind(b, ActionIds.LabelKey(id));
            buttons[id] = b;
            Element.Add(b);
            return b;
        }

        void Menu(string name, string labelKey, Func<List<MenuItem>> items, string glyph = null)
        {
            Button b = null;
            b = new Button(() => ctx.Shell.Menus.Show(items(), b, null, name + "-menu")) { name = name };
            b.AddToClassList("hs-toolbar__button");
            if (glyph != null) { b.text = glyph; Loc.Tooltip(b, labelKey); }
            else Loc.Bind(b, labelKey);
            Element.Add(b);
        }

        /// <summary>A menu row for an action (label, chord text, checked state, availability).</summary>
        public MenuItem Item(string id, IReadOnlyDictionary<string, object> args = null, string text = null)
        {
            ctx.Registry.Actions.TryGet(id, out var d);
            return new MenuItem
            {
                Id = id,
                Text = text ?? ctx.L10n.Get(ActionIds.LabelKey(id)),
                Shortcut = ctx.Keys.ChordText(id),
                Checked = d?.IsChecked?.Invoke() ?? false,
                Disabled = d == null || !d.Available,
                Run = () => ctx.Registry.Run(id, "menu", args),
            };
        }

        List<MenuItem> FileMenu() => new List<MenuItem>
        {
            Item(ActionIds.FileNew), Item(ActionIds.FileOpen), Item(ActionIds.FileSave), Item(ActionIds.FileSaveAs), Item(ActionIds.FileImport),
            Recent(),
            MenuItem.Sep(), Item(ActionIds.FileOptions), Item(ActionIds.FileSetupGuide), MenuItem.Sep(), Item(ActionIds.AppQuit),
        };

        MenuItem Recent()
        {
            List<MenuItem> items = null;
            try { items = RecentItems?.Invoke(); }
            catch (Exception e) { Debug.LogWarning("[ui] recent files: " + e.Message); }
            return new MenuItem { Id = "file-recent", Text = ctx.L10n.Get("files.recent"), Submenu = items ?? new List<MenuItem>(), Disabled = items == null || items.Count == 0 };
        }

        List<MenuItem> AddMenu()
        {
            var light = new List<MenuItem>();
            var db = ctx.App.Db;
            if (db != null)
            {
                try
                {
                    foreach (var row in db.Lights.Enumerate())
                    {
                        string r = row.Key.ToString();
                        light.Add(new MenuItem { Id = "light-" + r, Text = row.Name, Run = () => ctx.Registry.Run(ActionIds.AddLight, "menu", new Dictionary<string, object> { ["ref"] = r }) });
                    }
                }
                catch (Exception) { }
            }
            var addLight = Item(ActionIds.AddLight);
            if (light.Count > 0) addLight.Submenu = light;
            var route = Item(ActionIds.AddRoute);   // Phase 3.6: routes are in (the 3.5 placeholder kept the item disabled)
            return new List<MenuItem> { Item(ActionIds.AddFemale), Item(ActionIds.AddMale), Item(ActionIds.AddItem), addLight, Item(ActionIds.AddCamera), Item(ActionIds.AddCameraFromView), Item(ActionIds.AddFolder), route };
        }

        List<MenuItem> HistoryMenu()
        {
            var l = new List<MenuItem>();
            var bus = ctx.Session.Commands;
            var undoList = bus.UndoList;
            var redoList = bus.RedoList;
            for (int i = redoList.Count - 1; i >= 0; i--)
            {
                int steps = i + 1;
                l.Add(new MenuItem { Id = "redo-" + i, Text = "↷ " + redoList[i].Label, Run = () => { for (int k = 0; k < steps; k++) ctx.Registry.Run(ActionIds.Redo, "history"); } });
            }
            for (int i = 0; i < undoList.Count && i < 30; i++)
            {
                int steps = i + 1;
                l.Add(new MenuItem { Id = "undo-" + i, Text = undoList[i].Label, Checked = i == 0, Run = () => { for (int k = 0; k < steps; k++) ctx.Registry.Run(ActionIds.Undo, "history"); } });
            }
            if (l.Count == 0) l.Add(new MenuItem { Id = "empty", Text = ctx.L10n.Get("history.empty"), Disabled = true });
            return l;
        }

        List<MenuItem> CameraMenu()
        {
            var slots = new List<MenuItem>();
            for (int i = 0; i < 10; i++) slots.Add(Item(ActionIds.CameraSlotLoad(i)));
            var saves = new List<MenuItem>();
            for (int i = 0; i < 10; i++) saves.Add(Item(ActionIds.CameraSlotSave(i)));
            // the camera in use is checked (v0.3 trial 2)
            string active = ctx.Runtime?.GetPart<HS2Next.Scenes.Runtime.SceneViewCamera>()?.ActiveCameraId;
            var cams = new List<MenuItem> { new MenuItem { Id = "free", Text = ctx.L10n.Get("camera.free"), Checked = active == null, Run = () => ctx.Registry.Run(ActionIds.CameraRelease, "menu") } };
            foreach (var n in ctx.Model.PreOrder())
            {
                if (n.Kind != NodeKind.Camera) continue;
                string id = n.Id;
                cams.Add(new MenuItem { Id = "cam-" + id, Text = n.Label, Checked = id == active, Run = () => ctx.ShowResult(ctx.Ops.SetActiveCamera(id, UiContext.User())) });
            }
            return new List<MenuItem>
            {
                Item(ActionIds.CameraReset), Item(ActionIds.CameraFrame), Item(ActionIds.CameraResetAngles),
                new MenuItem { Id = "slots", Text = ctx.L10n.Get("camera.slots"), Submenu = slots },
                new MenuItem { Id = "slotsSave", Text = ctx.L10n.Get("camera.slotsSave"), Submenu = saves },
                new MenuItem { Id = "active", Text = ctx.L10n.Get("camera.active"), Submenu = cams },
                MenuItem.Sep(), Item(ActionIds.AddCameraFromView), Item(ActionIds.CameraLookThrough),
            };
        }

        List<MenuItem> ViewMenu() => new List<MenuItem>
        {
            Item(ActionIds.ViewGizmos), Item(ActionIds.ViewPlanes), Item(ActionIds.ViewCentre), Item(ActionIds.ViewPivotMarker),
            Item(ActionIds.ViewGimmickIcons), Item(ActionIds.ViewLightHelpers), Item(ActionIds.ViewCameraIcons), Item(ActionIds.ViewKinematicHandles),
            Item(ActionIds.ViewPhysics), MenuItem.Sep(), Item(ActionIds.ViewPanels), Item(ActionIds.ViewFps), Item(ActionIds.UiResetLayout),
        };

        List<MenuItem> LanguageMenu()
        {
            var l = new List<MenuItem>();
            foreach (var lang in new[] { L10nTable.En, L10nTable.Ja, L10nTable.ZhHans })
            {
                string code = lang;
                l.Add(new MenuItem { Id = "lang-" + code, Text = ctx.L10n.Get("lang." + code), Checked = ctx.L10n.Language == code, Run = () => ctx.Registry.Run(ActionIds.UiLanguage, "menu", new Dictionary<string, object> { ["language"] = code }) });
            }
            return l;
        }

        List<MenuItem> HelpMenu() => new List<MenuItem> { Item(ActionIds.HelpKeys), Item(ActionIds.HelpCheckUpdates), Item(ActionIds.HelpAbout) };

        public void Refresh()
        {
            var bus = ctx.Session.Commands;
            var ul = bus.CanUndo && bus.UndoList.Count > 0 ? bus.UndoList[0].Label : null;
            var rl = bus.CanRedo && bus.RedoList.Count > 0 ? bus.RedoList[0].Label : null;
            undo.SetEnabled(bus.CanUndo);
            redo.SetEnabled(bus.CanRedo);
            undo.tooltip = ul != null ? ctx.L10n.Format("toolbar.undoLabel", ul) : ctx.L10n.Get("action.edit.undo");
            redo.tooltip = rl != null ? ctx.L10n.Format("toolbar.redoLabel", rl) : ctx.L10n.Get("action.edit.redo");
            foreach (var kv in buttons)
            {
                if (!ctx.Registry.Actions.TryGet(kv.Key, out var d)) { kv.Value.SetEnabled(false); continue; }
                if (kv.Key != ActionIds.Undo && kv.Key != ActionIds.Redo) kv.Value.SetEnabled(d.Available);
                kv.Value.EnableInClassList("hs-toolbar__button--on", d.IsChecked?.Invoke() ?? false);
            }
            if (buttons.TryGetValue(ActionIds.ToolSpace, out var sb)) sb.text = ctx.L10n.Get("space." + ctx.Space.ToString().ToLowerInvariant());
            if (buttons.TryGetValue(ActionIds.ToolPivot, out var pb)) pb.text = ctx.L10n.Get("pivot." + (ctx.Pivot == GizmoPivot.Individual ? "individual" : "centre"));
        }

        public void Tick()
        {
            if (Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + 0.5f;
            Refresh();
        }

        public Button ButtonOf(string id) => buttons.TryGetValue(id, out var b) ? b : null;
    }

    public sealed class UiStatusBar
    {
        readonly UiContext ctx;
        public VisualElement Element { get; }
        public readonly Label Selection, Finding, File, Fps;
        float next;
        float fpsAccum;
        int fpsFrames;
        float fpsShown, msShown;

        public UiStatusBar(UiContext ctx)
        {
            this.ctx = ctx;
            Element = new VisualElement { name = "statusbar" };
            Element.AddToClassList("hs-statusbar");
            Selection = Item("status-selection");
            Finding = Item("status-finding");
            Finding.AddToClassList("hs-statusbar__finding");
            Finding.RegisterCallback<PointerDownEvent>(_ => ShowFindings());
            var sp = new VisualElement();
            sp.AddToClassList("hs-statusbar__spacer");
            Element.Add(sp);
            UiPlayback.StatusItem(ctx, Element);   // Phase 3.6 package K: "Paused"
            File = Item("status-file");
            Fps = Item("status-fps");
            ctx.SelectionChanged += _ => Refresh();
            ctx.FindingAdded += _ => Refresh();
            ctx.SceneLoaded += _ => Refresh();
            ctx.SceneCleared += Refresh;
            Refresh();
        }

        Label Item(string name)
        {
            var l = new Label { name = name };
            l.AddToClassList("hs-statusbar__item");
            Element.Add(l);
            return l;
        }

        public void Refresh()
        {
            var sel = ctx.Selection;
            if (sel.Count == 0) Selection.text = ctx.L10n.Get("status.noSelection");
            else
            {
                var p = ctx.Model.Find(sel.Primary);
                Selection.text = ctx.L10n.Format("status.selection", sel.Count, p != null ? p.Label : sel.Primary);
            }
            SceneFinding last = null;
            foreach (var f in ctx.Findings) if (f.Severity >= HS2Next.Save.FindingSeverity.Warning) last = f;
            Finding.text = last != null ? "⚠ " + last.Code + ": " + Trim(last.Message, 80) : "";
            var bus = ctx.Session.Commands;
            string name = ctx.CurrentFile != null ? System.IO.Path.GetFileName(ctx.CurrentFile) : ctx.L10n.Get("status.untitled");
            File.text = (bus.IsDirty ? "● " : "") + name;
        }

        static string Trim(string s, int n) => s == null ? "" : s.Length <= n ? s : s.Substring(0, n) + "…";

        void ShowFindings()
        {
            var items = new List<MenuItem>();
            foreach (var f in ctx.Findings.Reverse().Take(30))
                items.Add(new MenuItem { Id = "finding-" + f.Code, Text = f.Severity + " " + f.Code + (f.NodeId != null ? " (" + f.NodeId + ")" : "") + ": " + Trim(f.Message, 100), Disabled = f.NodeId == null, Run = () => { if (f.NodeId != null && ctx.Model.Find(f.NodeId) != null) ctx.Ops.Select(new[] { f.NodeId }); } });
            if (items.Count == 0) items.Add(new MenuItem { Id = "none", Text = ctx.L10n.Get("status.noFindings"), Disabled = true });
            ctx.Shell.Menus.Show(items, Finding, null, "findings-menu");
        }

        public void Tick()
        {
            fpsAccum += Time.unscaledDeltaTime;
            fpsFrames++;
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            if (fpsFrames > 0 && fpsAccum > 0) { fpsShown = fpsFrames / fpsAccum; msShown = fpsAccum * 1000f / fpsFrames; }
            fpsAccum = 0; fpsFrames = 0;
            Fps.style.display = ctx.Options.Bool(OptionDefs.ViewFps) ? DisplayStyle.Flex : DisplayStyle.None;
            Fps.text = fpsShown.ToString("0") + " fps  " + msShown.ToString("0.0") + " ms";
            Refresh();
        }

        public float FpsValue => fpsShown;
    }
}
