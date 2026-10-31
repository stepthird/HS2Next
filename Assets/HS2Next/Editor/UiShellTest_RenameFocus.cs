// UiShellTest.play group RF (Phase 5.3 package U): the tree's in-place rename field and the shortcut router. UI Toolkit reports
// the outermost composite (the tree) as the focused element while the rename field inside it has the keyboard, so the router
// treated typing in the rename as typing on the tree: Ctrl shortcuts (undo) and held keys ran, the IME candidate-window fix did
// not apply and ui.state.focus named the tree. Both ways to start a rename are driven with synthesised pointer and key events (as ui.input
// sends them): a click on a new folder's row, then F2; a right click on the row, then the menu's Rename. Each checks a few frames
// later that the rename field holds the innermost focus, that the router (and ui.state.focus) name the field, that the tool keys
// E and R typed into it go in as letters without switching the tool, that Ctrl+Z there runs no shortcut (the scene's undo ran
// before), and that Esc cancels the rename (the name unchanged).
using System;
using System.Collections;
using System.Linq;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class UiShellTest
{
    [UnityEditor.InitializeOnLoadMethod]
    static void RegisterRenameFocusGroup()
    {
        if (!Groups.Any(g => g.id == "RF")) Groups.Add(("RF", RenameFocus));
    }

    static bool Within(VisualElement e, VisualElement ancestor)
    {
        for (var x = e; x != null; x = x.hierarchy.parent) if (x == ancestor) return true;
        return false;
    }

    /// <summary>The innermost focused element (UI Toolkit's internal getter; the test reads it on its own).</summary>
    static VisualElement LeafOf(FocusController fc) =>
        typeof(FocusController).GetMethod("GetLeafFocusedElement", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic, null, Type.EmptyTypes, null)?.Invoke(fc, null) as VisualElement;

    /// <summary>The rename field shows its text: the input's text element holds text, has a size, overlaps the input's box, is
    /// displayed, visible and not transparent (the field and its ancestors too).</summary>
    static bool TextShown(TextField f, out string info)
    {
        var input = f.Q(className: TextField.inputUssClassName);
        var te = input?.Q<TextElement>();
        var fb = f.worldBound;
        var ib = input?.worldBound ?? default;
        var tb = te?.worldBound ?? default;
        float ow = Mathf.Min(ib.xMax, tb.xMax) - Mathf.Max(ib.xMin, tb.xMin), oh = Mathf.Min(ib.yMax, tb.yMax) - Mathf.Max(ib.yMin, tb.yMin);
        bool shown = te != null;
        for (var x = (VisualElement)te; x != null && shown; x = x.hierarchy.parent)
            shown = x.resolvedStyle.display == DisplayStyle.Flex && x.resolvedStyle.visibility == Visibility.Visible && x.resolvedStyle.opacity > 0.1f;
        var col = te?.resolvedStyle.color ?? Color.clear;
        var tr = te?.resolvedStyle.translate ?? Vector3.zero;
        info = $"text '{te?.text}', field {R(fb)}, input {R(ib)}, text element {R(tb)}, overlap {ow:0.#} x {oh:0.#}, shown {shown}, " +
               $"colour ({col.r:0.##}, {col.g:0.##}, {col.b:0.##}, {col.a:0.##}), translate ({tr.x:0.#}, {tr.y:0.#}), font size {te?.resolvedStyle.fontSize:0.#}";
        return te != null && !string.IsNullOrEmpty(te.text) && !float.IsNaN(fb.width) && fb.width > 20 && ib.width > 10 && tb.width > 0 && ow > 4 && oh > 4
               && shown && col.a > 0.3f;
    }

    static string NameOf(VisualElement e) => e == null ? "none" : string.IsNullOrEmpty(e.name) ? e.GetType().Name : e.name;

    static string Excerpt(string s, string key)
    {
        int i = s.IndexOf(key, StringComparison.Ordinal);
        return i < 0 ? "absent" : s.Substring(i, Math.Min(40, s.Length - i));
    }

    static IEnumerator RenameFocus(Ctx c)
    {
        var ui = c.Ui;
        var sh = ui.Shell;
        ui.Ops.NewScene();
        yield return Frames(2);
        sh.ShowPanel("workspace");
        yield return Frames(2);
        ui.Registry.Run(ActionIds.AddFolder, "test");
        yield return Frames(3);
        string id = ui.Selection.Primary;
        var ws = sh.PanelRoot("workspace")?.Q<WorkspacePanel>() ?? WorkspacePanel.Current;
        c.Check(id != null && ws != null, "a folder added and selected (" + (id ?? "none") + ")");
        if (id == null || ws == null) yield break;
        string label = ui.Model.Find(id).Label;
        var fc = c.Host.Root.panel.focusController;
        var search = ws.Q<TextField>("workspace-search");
        if (search != null)
        {
            search.value = "Fol";
            yield return Frames(2);
            TextShown(search, out var sInfo);
            c.Info("for comparison, the tree's search field: " + sInfo);
            search.value = "";
            yield return Frames(2);
        }
        foreach (var way in new[] { "F2", "menu" })
        {
            ws.RebuildNow();
            yield return Frames(3);
            var row = TreeRowOf(ws, label);
            c.Check(row != null, way + ": the folder's row");
            if (row == null) continue;
            yield return ClickAt(c, row.worldBound.center);
            if (way == "F2") Ev(c, new { type = "key", key = "F2" });
            else
            {
                Ev(c, new { type = "pointer", x = row.worldBound.center.x, y = row.worldBound.center.y, button = 1 });
                yield return Frames(2);
                c.Check(c.Host.Root.Q("menu-item-ctx-rename") != null, "menu: the context menu has Rename");
                Ev(c, new { type = "pointer", target = "menu-item-ctx-rename" });
            }
            yield return Frames(4);
            var f = ws.RenameField;
            var leaf = LeafOf(fc);
            var routed = c.Host.Router.Focused;
            c.Check(f != null && Within(leaf, f), way + ": the rename field is open and holds the innermost focus (field " + (f != null ? "open" : "none") +
                    ", innermost " + NameOf(leaf) + ", UI Toolkit's focused element " + NameOf(fc.focusedElement as VisualElement) + ")");
            if (f == null) continue;
            c.Check(TextShown(f, out var shown0) && f.text == label, way + ": the rename field shows the current name (" + shown0 + ")");
            c.Check(routed == f, way + ": the shortcut router names the rename field as focused (" + NameOf(routed) + ", kind " + c.Host.Router.FocusKindOf(routed) + ")");
            var t = c.App.Bridge.Execute("{\"cmd\":\"ui.state\"}");
            for (int i = 0; i < 100 && !t.IsCompleted; i++) yield return PlayModeHarness.WaitFrames(1);
            string state = t.IsCompleted ? t.Result : "";
            c.Check(state.Contains("\"focus\":\"workspace-rename\""), way + ": ui.state.focus names the field (" + Excerpt(state, "\"focus\"") + ")");
            // the tool keys E and R typed into the field: letters in the name, the tool unchanged
            var tool0 = ui.Tool;
            Ev(c, new { type = "key", key = "E" });
            Ev(c, new { type = "text", text = "e" });
            Ev(c, new { type = "key", key = "R" });
            Ev(c, new { type = "text", text = "r" });
            yield return Frames(2);
            c.Check(TextShown(f, out var shown1) && f.text.EndsWith("er", StringComparison.Ordinal), way + ": the rename field shows the typed text (" + shown1 + ")");
            c.Check(ui.Tool == tool0 && ws.RenameField == f && f.text.EndsWith("er", StringComparison.Ordinal),
                way + ": E and R typed into the rename go in as letters, the tool stays " + tool0 + " (tool " + ui.Tool + ", text " + f.text + ")");
            // Ctrl+Z in the field: a text field's own key (the scene's undo is not run; the field does not stop it, the router decides)
            int ran0 = c.Host.Router.Ran;
            Ev(c, new { type = "key", key = "Z", mods = new[] { "ctrl" } });
            yield return Frames(2);
            c.Check(c.Host.Router.Ran == ran0 && ui.Model.Find(id) != null && ws.RenameField == f,
                way + ": Ctrl+Z in the rename runs no shortcut (actions run by keys +" + (c.Host.Router.Ran - ran0) + ", folder " + (ui.Model.Find(id) != null ? "kept" : "gone") + ")");
            if (ui.Model.Find(id) == null) yield break;
            Ev(c, new { type = "key", key = "Escape" });
            yield return Frames(3);
            string after = ui.Model.Find(id).Label;
            c.Check(ws.RenameField == null && after == label, way + ": Esc cancels the rename (rename " + (ws.RenameField != null ? "open" : "closed") + ", name " + after + ")");
            if (ws.RenameField != null) ws.CommitRename(false);
            if (after != label) { ui.Ops.Rename(id, null, UiContext.User()); yield return Frames(2); }
            ui.Tool = tool0;
            label = ui.Model.Find(id).Label;
        }
    }
}
