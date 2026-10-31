// UiShellTest.play (package A): the app with the UI in Editor play mode (PlayModeHarness;
// launch override app on + ui.enabled true; -nographics is possible per spike S7, pixel checks live in GizmoTest.render).
// Groups: H host (PanelRenderer, root, regions, splitters, fonts, strings), L layout (save / load / reset, move,
// collapse), N language switch (labels change, font switches), V viewport rect = element rect (±1 px) after resizes,
// C camera controller via synthetic pointer events (orbit / pan / dolly against the formulas), K keymap router with
// synthetic key events (text focus blocks, modal blocks), W widgets from schema rows (one entry per drag, typed merge,
// Esc cancels), G gizmo drags (move / rotate / scale: expected world delta ±1e-4, one entry, Esc cancels), P picking
// (character / item / empty space at known screen points), B ui.* bridge commands, U the v0.2 UI defects (UiShellTest_V02).
// Run: & Tools\unity_batch.ps1 -Method UiShellTest.Run -NoQuit [-NoGraphics] [-MethodArgs @('-only','H+L')]
// Report: Spike/out/ui/ui_shell_report.txt ("SUMMARY: N PASS, M FAIL").
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.Scenes;
using HS2Next.UI;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class UiShellTest
{
    public const int Port = 17397;
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "ui");
    static string ReportPath => Path.Combine(OutDir, "ui_shell_report.txt");
    public static string Launch =>
        "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},\"userData\":\"Spike/out/ui/userdata\",\"bridge\":{\"port\":" + Port + ",\"outputDir\":\"Spike/out/ui/out\",\"allowQuit\":false}}";

    public static void Run()
    {
        Directory.CreateDirectory(OutDir);
        // a fresh UserData/config each run (layout / options / keymap files of earlier runs would change the defaults)
        string cfg = Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "ui", "userdata", "config");
        try { if (Directory.Exists(cfg)) Directory.Delete(cfg, true); } catch (Exception) { }
        File.WriteAllText(ReportPath, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("UiShellTest.Play", ReportPath, 20, true, Launch);
    }

    sealed class Ctx
    {
        public readonly StringBuilder Rep = new StringBuilder();
        public int Pass, Fail, Warn;
        public string Group = "";
        public HashSet<string> Only;
        public UiHost Host;
        public UiContext Ui => Host.Context;
        public AppCore App;
        public bool Want(string g) => Only == null || Only.Contains(g);
        public void Check(bool ok, string what)
        {
            if (ok) Pass++; else Fail++;
            string l = (ok ? "PASS " : "FAIL ") + Group + " " + what;
            Rep.AppendLine(l);
            Debug.Log("UI SHELL " + l);
        }
        public void Warning(string what) { Warn++; Rep.AppendLine("WARN " + Group + " " + what); Debug.Log("UI SHELL WARN " + Group + " " + what); }
        public void Info(string what) { Rep.AppendLine("INFO " + Group + " " + what); Debug.Log("UI SHELL INFO " + Group + " " + what); }
    }

    static IEnumerator Play()
    {
        var c = new Ctx();
        var args = Environment.GetCommandLineArgs();
        int oi = Array.IndexOf(args, "-only");
        if (oi >= 0 && oi + 1 < args.Length) c.Only = new HashSet<string>(args[oi + 1].Split('+'));
        c.Rep.AppendLine($"UiShellTest.play (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, screen {Screen.width}x{Screen.height}, dpi {Screen.dpi})");
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ok);
        c.App = AppCore.Current;
        c.Group = "H";
        c.Check(ok[0] && c.App.Stage == AppStage.Ready, "app ready (" + (c.App?.Stage.ToString() ?? "none") + (c.App?.Error != null ? ": " + c.App.Error : "") + ")");
        yield return PlayModeHarness.WaitUntil(() => UiHost.Current != null && UiHost.Current.IsReady, 30, ok);
        c.Host = UiHost.Current;
        c.Check(ok[0], "UiHost ready (first UI frame " + (c.Host != null ? c.Host.FirstFrameMs.ToString("0") : "-") + " ms; problem " + (c.Host?.StartProblem ?? "none") + ")");
        if (!ok[0]) { Finish(c); yield break; }
        yield return PlayModeHarness.WaitFrames(5);
        foreach (var g in Groups)
        {
            if (!c.Want(g.id)) continue;
            c.Group = g.id;
            IEnumerator e = null;
            try { e = g.run(c); }
            catch (Exception ex) { c.Check(false, "threw " + ex); continue; }
            while (true)
            {
                bool more;
                try { more = e.MoveNext(); }
                catch (Exception ex) { c.Check(false, "threw " + ex); break; }
                if (!more) break;
                yield return e.Current;
            }
        }
        Finish(c);
    }

    static readonly List<(string id, Func<Ctx, IEnumerator> run)> Groups = new List<(string, Func<Ctx, IEnumerator>)>();

    static UiShellTest()
    {
        Groups.Add(("H", HostGroup));
        Groups.Add(("W", Widgets));
        AddMoreGroups();
    }

    static partial void AddMoreGroups();

    static void Finish(Ctx c)
    {
        c.Rep.Insert(0, $"SUMMARY: {c.Pass} PASS, {c.Fail} FAIL, {c.Warn} WARN\n");
        File.WriteAllText(ReportPath, c.Rep.ToString(), new UTF8Encoding(false));
        Debug.Log($"UI SHELL TEST DONE: {c.Pass} PASS, {c.Fail} FAIL");
        PlayModeHarness.ExitCode = c.Fail > 0 ? 1 : 0;
    }

    // ---------------- H: host

    static IEnumerator HostGroup(Ctx c)
    {
        var ui = c.Ui;
        var root = c.Host.Root;
        c.Check(c.Host.Renderer != null && root != null && root.panel != null, "PanelRenderer root on a panel");
        c.Check(c.Host.Settings != null && c.Host.Settings.scaleMode == PanelScaleMode.ConstantPhysicalSize && !c.Host.Settings.clearColor,
            $"panel settings: {c.Host.Settings?.scaleMode}, clear colour {c.Host.Settings?.clearColor}, scale {c.Host.Settings?.scale} (user ×{c.Host.UserScaleApplied}), panel scale {root.panel?.scaledPixelsPerPoint}");
        foreach (var r in LayoutRegions.All) c.Check(root.Q("region-" + r) != null, "region " + r);
        c.Check(root.Q<TwoPaneSplitView>("split-outer") != null && root.Q<TwoPaneSplitView>("split-inner") != null && root.Q<TwoPaneSplitView>("split-center") != null, "three splitters");
        c.Check(root.Q("toolbar") != null && root.Q("statusbar") != null && root.Q("viewport") != null, "toolbar, status bar, viewport");
        c.Check(L10n.Table.Count > 500, "strings.tsv loaded (" + L10n.Table.Count + " keys)");
        c.Check(ui.Fonts.Loaded, "bundled fonts loaded (" + (ui.Fonts.Problem ?? "ok") + ")");
        c.Check(ui.Keys.Resolver != null && ui.Keys.PresetName == Keymap.DefaultPreset, "key preset " + ui.Keys.PresetName);
        int actions = ui.Registry.Actions.All.Count();
        c.Check(actions >= ActionManifest.All.Count - 12, $"actions registered: {actions} of {ActionManifest.All.Count} manifest ids");
        var missing = ActionManifest.All.Where(a => a.Kind != ActionKind.Modifier && !ui.Registry.Actions.Contains(a.Id)).Select(a => a.Id).ToList();
        if (missing.Count > 0) c.Info("actions without behaviour (other packages): " + string.Join(", ", missing));
        c.Check(ui.Shell.RegionOf("history") == LayoutRegions.Right, "history panel in the right region (the sidebar layout)");
        // a panel registered at run time appears in its region
        ui.Registry.Panels.Register("test.panel", "panel.history", LayoutRegions.Right, x => new Label("test panel") { name = "test-panel-label" });
        yield return PlayModeHarness.WaitFrames(1);
        c.Check(ui.Shell.RegionOf("test.panel") == LayoutRegions.Right, "a panel registered later appears in its default region");
        c.Check(ui.Shell.FocusPanel("test.panel") && root.Q("test-panel-label") != null, "focus builds the panel's content");
        ui.Shell.MovePanel("test.panel", LayoutRegions.Left);
        yield return PlayModeHarness.WaitFrames(1);
        c.Check(ui.Shell.RegionOf("test.panel") == LayoutRegions.Left && ui.Shell.Layout.RegionOf("test.panel") == LayoutRegions.Left, "move to left");
        ui.Shell.SetPanelVisible("test.panel", false);
        c.Check(ui.Shell.RegionOf("test.panel") == null && ui.Shell.Layout.Hidden.Contains("test.panel"), "hide");
        // dialogs and toasts
        string pressed = null;
        ui.Dialogs.Confirm("dialog.delete.title", "dialog.delete.message", okk => pressed = okk ? "ok" : "cancel", 1);
        yield return PlayModeHarness.WaitFrames(1);
        c.Check(ui.Dialogs.IsModalOpen && root.Q("dialog-dialog.delete.title") != null, "a modal dialog is open");
        // a modal blocks shortcuts: W does not change the tool, Esc cancels
        var toolBefore = ui.Tool;
        c.Host.Router.Handle("E", KeyMods.None, false);
        c.Check(ui.Tool == toolBefore, "modal: E blocked (tool " + ui.Tool + ")");
        c.Host.Router.Handle("Escape", KeyMods.None, false);
        c.Check(!ui.Dialogs.IsModalOpen && pressed == "cancel", "Esc cancels the dialog (" + pressed + ")");
        ui.Toasts.Show("test toast");
        c.Check(ui.Toasts.Count >= 1 && root.Q("toast") != null, "toast shown");
        yield return PlayModeHarness.WaitFrames(1);
    }

    // ---------------- W: widgets from schema rows

    static IEnumerator Widgets(Ctx c)
    {
        var ui = c.Ui;
        // a folder node to edit
        var r = ui.Ops.AddFolder(null, null, -1, UiContext.User());
        c.Check(r.Ok && r.NewIds.Count == 1, "add folder: " + r);
        if (!r.Ok) yield break;
        string id = r.NewIds[0];
        ui.Ops.Select(new[] { id });
        yield return PlayModeHarness.WaitFrames(1);
        var posSpec = InspectorSchemas.Transform.Fields.First(f => f.Path == "transform.pos");
        var row = FieldWidgets.Create(ui, posSpec, new FieldContext { Selector = TargetSelector.Selection() });
        c.Ui.Shell.Overlay.Add(row);
        yield return PlayModeHarness.WaitFrames(1);
        var vf = row.Control as HS2Next.UI.Widgets.VectorField;
        c.Check(vf != null && row.Binding != null, "transform.pos row = VectorField with a binding");
        int undo0 = ui.Session.Commands.UndoList.Count;
        var pos0 = ui.Model.Find(id).Doc.Transform.Pos;   // the node lands at the view's pivot (Phase 3.6: SessionOptions.DefaultPivot)
        // typed value on X
        vf.Fields[0].value = 1.5f;
        yield return PlayModeHarness.WaitFrames(1);
        var pos = ui.Model.Find(id).Doc.Transform.Pos;
        c.Check(Mathf.Abs(pos.X - 1.5f) < 1e-5f && pos.Y == pos0.Y && pos.Z == pos0.Z, $"typed X 1.5 → model pos ({pos.X}, {pos.Y}, {pos.Z})");
        c.Check(ui.Session.Commands.UndoList.Count == undo0 + 1, "one history entry (" + (ui.Session.Commands.UndoList.Count - undo0) + ")");
        // model → widget: undo moves the field back
        ui.Registry.Run(ActionIds.Undo, "test");
        yield return PlayModeHarness.WaitFrames(1); yield return PlayModeHarness.WaitFrames(1);
        c.Check(Mathf.Abs(vf.Fields[0].value) < 1e-5f, "undo → field X back to 0 (" + vf.Fields[0].value + ")");
        // a drag transaction (Begin, previews, End) = one entry; Esc cancels
        var nameSpec = InspectorSchemas.Header.Fields.First(f => f.Path == "name");
        var scaleSpec = new FieldSpec { Path = "transform.pos", Kind = FieldKind.Float, Component = 1, Min = -10, Max = 10, OpLabel = OpLabels.Transform, LabelKey = "inspector.transform.pos" };
        var yRow = FieldWidgets.Create(ui, scaleSpec, new FieldContext { Selector = TargetSelector.Selection() });
        ui.Shell.Overlay.Add(yRow);
        var ns = (HS2Next.UI.Widgets.NumberSlider)yRow.Control;
        int u1 = ui.Session.Commands.UndoList.Count;
        ns.Begin();
        for (int i = 1; i <= 10; i++) { ns.Raise(HS2Next.UI.Widgets.EditPhase.Preview, i * 0.1); }
        float midY = ui.Model.Find(id).Doc.Transform.Pos.Y;
        ns.End();
        yield return PlayModeHarness.WaitFrames(1);
        c.Check(Mathf.Abs(midY - 1f) < 1e-5f && ui.Session.Commands.UndoList.Count == u1 + 1, $"slider drag: 10 previews → y {midY}, {ui.Session.Commands.UndoList.Count - u1} entry");
        ns.Begin();
        ns.Raise(HS2Next.UI.Widgets.EditPhase.Preview, 5);
        c.Host.Router.Handle("Escape", KeyMods.None, false);
        ns.End();
        yield return PlayModeHarness.WaitFrames(1);
        c.Check(Mathf.Abs(ui.Model.Find(id).Doc.Transform.Pos.Y - 1f) < 1e-5f && ui.Session.Commands.UndoList.Count == u1 + 1, "Esc during a drag cancels (y " + ui.Model.Find(id).Doc.Transform.Pos.Y + ")");
        // the name row (Text)
        var nrow = FieldWidgets.Create(ui, nameSpec, new FieldContext { Selector = TargetSelector.Selection() });
        ui.Shell.Overlay.Add(nrow);
        ((TextField)nrow.Control).value = "Folder A";
        yield return PlayModeHarness.WaitFrames(1);
        c.Check(ui.Model.Find(id).Doc.Name == "Folder A", "name row → Rename (" + ui.Model.Find(id).Doc.Name + ")");
        row.RemoveFromHierarchy(); yRow.RemoveFromHierarchy(); nrow.RemoveFromHierarchy();
        ui.Ops.NewScene();
        yield return PlayModeHarness.WaitFrames(1);
    }
}
