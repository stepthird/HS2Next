// UiCoreTest (package A; headless, edit mode): the UI built off-panel on a test app host
// (UiHost.BuildOffPanel): U1 every registered panel builds without a panel (no error label), U2 every schema row makes a
// widget (InspectorSchemas.AllFields, templates expanded with "0"), U3 LocBinding resolves in en / ja / zh-Hans, U4 theme
// files import (tokens, components, theme) and every var(--x) used is defined, U5 actions: every registered action has an
// en label, every manifest action except the Modifier kind has a behaviour (C's tree.* allowed missing), U6 the strings of
// package A's block have en and ja, U7 the shell's element tree (regions, splitters, toolbar, status bar, viewport).
// Run: & Tools\unity_batch.ps1 -Method UiCoreTest.Run -NoGraphics [-MethodArgs @('-only','U1+U2')]
// Report: Spike/out/ui/ui_core_report.txt ("SUMMARY: N PASS, M FAIL").
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Binding;
using HS2Next.UI.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using L10n = HS2Next.UI.Core.L10n;

public static class UiCoreTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "ui");
    static readonly StringBuilder rep = new StringBuilder();
    static int pass, fail, warn;
    static string group = "";
    static string[] only;

    static void Check(bool ok, string what, string detail = "")
    {
        if (ok) pass++; else fail++;
        rep.AppendLine((ok ? "PASS " : "FAIL ") + group + " " + what + (detail.Length > 0 ? "  " + detail : ""));
    }

    static void Warn(string what) { warn++; rep.AppendLine("WARN " + group + " " + what); }
    static void Info(string what) => rep.AppendLine("INFO " + group + " " + what);
    static bool Want(string g) => only == null || only.Contains(g);

    public static void Run()
    {
        rep.Clear(); pass = fail = warn = 0;
        var a = TestArgs.Get();
        int oi = Array.IndexOf(a, "-only");
        only = oi >= 0 && oi + 1 < a.Length ? a[oi + 1].Split('+', ',') : null;
        Directory.CreateDirectory(OutDir);
        var head = new StringBuilder();
        AppCore app = null;
        try
        {
            var services = SceneRuntimeTest.NewServices(head);
            var s = AppSettings.Defaults(Array.Empty<string>());
            s.Content = CardResolveTest.Settings;
            s.UserData = Path.Combine(OutDir, "core_userdata");
            try { Directory.Delete(Path.Combine(s.UserData, "config"), true); } catch (Exception) { }
            s.Bridge.OutputDir = Path.Combine(OutDir, "core_out");
            app = AppCore.ForTests(services, s);
            var ctx = UiHost.BuildOffPanel(app);
            if (Want("U1")) { group = "U1"; U1(ctx); }
            if (Want("U2")) { group = "U2"; U2(ctx); }
            if (Want("U3")) { group = "U3"; U3(ctx); }
            if (Want("U4")) { group = "U4"; U4(); }
            if (Want("U5")) { group = "U5"; U5(ctx); }
            if (Want("U6")) { group = "U6"; U6(); }
            if (Want("U7")) { group = "U7"; U7(ctx); }
        }
        catch (Exception e) { group = "run"; Check(false, "exception", e.ToString()); }
        finally { try { app?.Dispose(); } catch (Exception) { } }
        rep.Insert(0, $"SUMMARY: {pass} PASS, {fail} FAIL, {warn} WARN\nUiCoreTest (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})\n" + head);
        File.WriteAllText(Path.Combine(OutDir, "ui_core_report.txt"), rep.ToString(), new UTF8Encoding(false));
        Debug.Log($"UI CORE TEST DONE: {pass} PASS, {fail} FAIL");
        if (Application.isBatchMode) TestExit.Exit(fail > 0 ? 1 : 0);
    }

    static void U1(UiContext ctx)
    {
        var panels = ctx.Registry.Panels.All;
        Info(panels.Count + " panels: " + string.Join(", ", panels.Select(p => p.Id)));
        foreach (var d in panels)
        {
            VisualElement root = null;
            Exception ex = null;
            try { root = UiShell.BuildPanel(ctx, d); }
            catch (Exception e) { ex = e; }
            var err = root?.Q<Label>(className: "hs-panel__empty");
            bool ok = ex == null && root != null && root.childCount > 0 && !(err != null && err.text.StartsWith(d.Id + ":"));
            Check(ok, "panel " + d.Id + " builds off-panel", ex?.Message ?? (err != null ? err.text : root == null ? "null" : root.childCount + " children"));
        }
        foreach (var p in LayoutDocument.DefaultPanels)
            if (!ctx.Registry.Panels.TryGet(p.Id, out _)) Warn("default panel " + p.Id + " is not registered (another package's)");
    }

    static void U2(UiContext ctx)
    {
        int n = 0, bad = 0;
        var kinds = new Dictionary<FieldKind, int>();
        foreach (var spec in InspectorSchemas.AllFields())
        {
            n++;
            try
            {
                var row = FieldWidgets.Create(ctx, spec, new FieldContext { Placeholder = "0" });
                if (row == null || (row.Control == null)) { bad++; Check(false, "row " + spec.Id, "no control"); continue; }
                kinds[spec.Kind] = kinds.TryGetValue(spec.Kind, out var k) ? k + 1 : 1;
            }
            catch (Exception e) { bad++; Check(false, "row " + spec.Id, e.GetType().Name + ": " + e.Message); }
        }
        Check(n > 200 && bad == 0, $"{n} schema rows make widgets ({string.Join(", ", kinds.Select(kv => kv.Key + " " + kv.Value))})");
    }

    static void U3(UiContext ctx)
    {
        var lbl = Loc.Label("toolbar.file");
        Check(lbl.text == "File", "Loc.Label en → File", lbl.text);
        var b = new LocBinding("toolbar.file");
        L10n.SetLanguage("ja");
        Check(b.Text == "ファイル", "LocBinding ja → ファイル", b.Text);
        L10n.SetLanguage("zh-Hans");
        Check(b.Text == "文件", "LocBinding zh-Hans → 文件", b.Text);
        L10n.SetLanguage("en");
        var f = new LocBinding("status.selection", 2, "x");
        Check(f.Text == "2 selected, primary: x", "formatted binding", f.Text);
        Check(new LocBinding("no.such.key").Text == "no.such.key", "missing key falls back to the key");
    }

    static void U4()
    {
        foreach (var p in new[] { "Assets/HS2Next/Resources/UI/tokens-dark.uss", "Assets/HS2Next/Resources/UI/components.uss" })
            Check(AssetDatabase.LoadAssetAtPath<StyleSheet>(p) != null, "imports " + p);
        Check(AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/HS2Next/Resources/UI/HS2NextDark.tss") != null, "theme HS2NextDark.tss imports");
        string root = Path.Combine(Application.dataPath, "HS2Next", "Resources", "UI");
        string tokens = File.ReadAllText(Path.Combine(root, "tokens-dark.uss"));
        string comps = File.ReadAllText(Path.Combine(root, "components.uss"));
        var defined = new HashSet<string>(Regex.Matches(tokens, @"(--[a-z0-9-]+)\s*:").Cast<Match>().Select(m => m.Groups[1].Value));
        var used = Regex.Matches(comps, @"var\((--[a-z0-9-]+)\)").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
        var unknown = used.Where(u => !defined.Contains(u)).ToList();
        Check(unknown.Count == 0, $"every var() of components.uss is a token ({used.Count} used, {defined.Count} defined)", string.Join(", ", unknown));
        Check(AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/HS2Next/Resources/UI/HS2NextPanelSettings.asset") != null, "PanelSettings asset");
        Check(AssetDatabase.LoadAssetAtPath<Material>("Assets/HS2Next/Resources/UI/HS2NextGizmo.mat")?.shader?.name == "HS2Next/UI/Gizmo", "gizmo material and shader");
        foreach (var n in UiFonts.AssetNames)
            Check(Resources.Load<UnityEngine.TextCore.Text.FontAsset>(UiFonts.ResourceDir + n) != null, "font asset " + n);
    }

    static void U5(UiContext ctx)
    {
        var acts = ctx.Registry.Actions.All.ToList();
        var noLabel = acts.Where(d => !L10n.Table.Has(d.LabelKey)).Select(d => d.Id).ToList();
        Check(noLabel.Count == 0, acts.Count + " actions have an en label", string.Join(", ", noLabel));
        var missing = ActionManifest.All.Where(x => x.Kind != ActionKind.Modifier && !ctx.Registry.Actions.Contains(x.Id)).Select(x => x.Id).ToList();
        var notTree = missing.Where(x => !x.StartsWith("tree.", StringComparison.Ordinal)).ToList();
        Check(notTree.Count == 0, "every manifest action has a behaviour (tree.* belong to the workspace panel)", string.Join(", ", notTree));
        if (missing.Count > 0) Info("without behaviour: " + string.Join(", ", missing));
        // a few behaviours off-panel
        ctx.Registry.Run(ActionIds.ToolRotate, "test");
        Check(ctx.Tool == GizmoTool.Rotate, "tool.rotate runs off-panel");
        ctx.Registry.Run(ActionIds.ToolSpace, "test");
        Check(ctx.Space == GizmoSpace.World, "tool.space cycles Auto → World");
        var r = ctx.Registry.Run(ActionIds.CameraRelease, "test");
        Check(r == ActionRunStatus.Unavailable, "camera.release unavailable without an active camera (" + r + ")");
    }

    static void U6()
    {
        var t = L10n.Table;
        string text = File.ReadAllText(Path.Combine(Application.dataPath, "HS2Next", "Resources", "UI", "L10n", "strings.tsv"), Encoding.UTF8);
        var lines = text.Split('\n');
        int start = Array.FindIndex(lines, l => l.TrimEnd('\r') == "#A");
        Check(start >= 0, "strings.tsv has the #A block");
        if (start < 0) return;
        int n = 0, missingJa = 0, missingEn = 0;
        for (int i = start + 1; i < lines.Length; i++)
        {
            var l = lines[i].TrimEnd('\r');
            if (l.StartsWith("#")) break;
            if (l.Length == 0) continue;
            string key = l.Split('\t')[0];
            n++;
            if (t.Cell(key, L10nTable.En) == null) missingEn++;
            if (t.Cell(key, L10nTable.Ja) == null) missingJa++;
        }
        Check(n > 30 && missingEn == 0 && missingJa == 0, $"#A block: {n} keys, missing en {missingEn}, ja {missingJa}");
    }

    static void U7(UiContext ctx)
    {
        var root = ctx.Shell.Root;
        foreach (var name in new[] { "toolbar", "statusbar", "viewport", "region-left", "region-right", "region-bottom", "split-outer", "split-inner", "split-center", "overlay", "dialogs" })
            Check(root.Q(name) != null, "element " + name);
        Check(ctx.Shell.RegionOf("history") == LayoutRegions.Right && ctx.Shell.RegionOf("inspector") == LayoutRegions.Left && ctx.Shell.RegionOf("workspace") == LayoutRegions.Left,
            "the sidebar layout: workspace and inspector on the left, history on the right (v0.3 trial)");
        Check(ctx.Shell.Regions[LayoutRegions.Bottom].Panels.Count == 0 && ctx.Shell.Regions[LayoutRegions.Bottom].Collapsed, "the bottom region empty and collapsed");
#if UNITY_EDITOR
        Check(UnityEditor.PlayerSettings.resizableWindow, "ProjectSettings: the player window is resizable (v0.3 trial)");
#endif
        Check(root.Q<Button>("tb-edit.undo") != null && root.Q<Button>("tb-tool.move") != null, "toolbar buttons tb-<action>");
    }
}
