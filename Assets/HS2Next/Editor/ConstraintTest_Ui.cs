// ConstraintTest, the UI (package U): the Constraints panel, the section, the marks and the bone popup on the app with the
// UI, driven through ui.action, ui.input and real pointer events, judged by element state, the history and the document: numbers and text
// only, nothing is rendered to a file, captured or opened. Its own scene: BR-Chan and HERO107, an item, a light, a folder, a camera, a route.
//   CU1   the panel is not registered until opened; the layout's version stands; a saved layout lists it; Reset layout leaves the bottom collapsed
//   CU2   Add by node, bone and IK target, one entry each; a cycle shows its text and adds nothing
//   CU3   a weight drag and an offset label drag are one entry, 40 steps
//   CU4   Esc during those drags: values as at the press, no entry, later moves ignored
//   CU5   every state's row and tooltip; a broken row keeps Remove and retarget
//   CU6   the mark on transform, IK target, FK and Advanced pose rows; the tooltip names Constraints
//   CU7   the section on each node kind and in the Kinematics tab; closed reads nothing
//   CU8   20 inspector rebuilds and 20 panel opens: subscribers and tickers do not grow
//   CU9   the bone popup: search, Esc, a part, pick in view, guarded rows greyed, an item's names
//   CU10  three languages: the word Constraints, no community name in the strings, the guides or the tooltips
//   CU11  widths 360 and 300: section and panel reachable
//   CD4   the gizmo's refusal per channel; a free channel works; a source works
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Bridge;
using HS2Next.UI.Core;
using HS2Next.UI.Gizmos;
using HS2Next.UI.Panels;
using HS2Next.UI.Widgets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class ConstraintTest
{
    static string cuA, cuB, cuItem, cuItem2, cuFolder, cuLight, cuCamera, cuRoute;
    static bool cuReady;
    static int cuToasts;

    static AppCore CuApp => AppCore.Current;
    static UiContext CuUi => UiHost.Current?.Context;
    static IPanel CuPanelRoot => UiHost.Current?.Root?.panel;
    static VisualElement CuTree => CuPanelRoot?.visualTree;
    static VisualElement CuQ(string name) => CuTree?.Q(name);
    static int CuUndos => CuUi.Session.Commands.UndoList.Count;
    static string CuTop => CuUi.Session.Commands.UndoList.Count > 0 ? CuUi.Session.Commands.UndoList[0].Label : null;
    static IEnumerator CuFrames(int n) => PFrames(n);
    static ConstraintsPanel CuPanel => ConstraintsPanel.Current;
    static ConstraintEditor CuEditor => CuPanel?.Editor;
    static InspectorPanel CuInsp => CuTree?.Q<InspectorPanel>();
    static TransformTool CuGizmo => CuUi.Viewport.Gizmo;
    static ConstraintList CuList => CuUi.Model.Constraints;
    static int CuCount => CuList?.Count ?? 0;

    static bool CuVisible(VisualElement e)
    {
        if (e == null || e.panel == null) return false;
        for (var x = e; x != null; x = x.hierarchy.parent) if (x.resolvedStyle.display == DisplayStyle.None) return false;
        return e.worldBound.width > 0;
    }

    static IEnumerator CuCall(object req) => PCall(CuApp, req);

    static IEnumerator CuOk(object req, string what)
    {
        yield return PCall(CuApp, req);
        Check(what, POk, POk ? "" : Short(pLastText));
    }

    static JObject CuPointer(Vector2 p, string phase, int button = 0) => new JObject { ["type"] = "pointer", ["x"] = p.x, ["y"] = p.y, ["phase"] = phase, ["button"] = button };

    static IEnumerator CuReveal(VisualElement e)
    {
        bool any = false;
        for (var x = e?.hierarchy.parent; x != null; x = x.hierarchy.parent)
            if (x is ScrollView sv) { sv.ScrollTo(e); any = true; }
        if (any) yield return CuFrames(3);
    }

    static IEnumerator CuClick(VisualElement e, string what)
    {
        yield return CuReveal(e);
        if (!Check("the element to click: " + what, e != null && e.panel != null)) yield break;
        var at = e.worldBound.center;
        yield return CuCall(new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(CuPointer(at, "click")), ["frames"] = 2 });
        Check("click " + what, POk, POk ? "" : Short(pLastText));
        yield return CuFrames(2);
    }

    static void CuRaw(EventType et, Vector2 p, Vector2 delta = default)
    {
        var se = new Event { type = et, mousePosition = p, delta = delta, button = 0, clickCount = 1, modifiers = EventModifiers.None };
        var tree = CuTree;
        if (et == EventType.MouseDown) using (var e = PointerDownEvent.GetPooled(se)) tree.SendEvent(e);
        else if (et == EventType.MouseUp) using (var e = PointerUpEvent.GetPooled(se)) tree.SendEvent(e);
        else using (var e = PointerMoveEvent.GetPooled(se)) tree.SendEvent(e);
    }

    static void CuEsc() => UiCommands.Send(UiHost.Current, JObject.FromObject(new { type = "key", key = "Escape" }), out _);

    static VisualElement CuCapturing()
    {
        for (int id = 0; id < PointerId.maxPointers; id++) if (CuPanelRoot.GetCapturingElement(id) is VisualElement v) return v;
        return null;
    }

    static string CuName(VisualElement v) => v == null ? "none" : (v.name ?? "") + " (" + v.GetType().Name + ")";
    static bool CuOpenEdit => CuUi.ActiveEdit != null && CuUi.ActiveEdit.IsOpen;

    static IEnumerator CuUndoTo(int to)
    {
        while (CuUndos > to) { CuUi.Session.Commands.Undo(); CuUi.Runtime?.ApplyNow(); }
        yield return CuFrames(3);
    }

    // ------------------------------------------------------------ the scene

    static IEnumerator CuPrepare()
    {
        if (cuReady) yield break;
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => UiHost.Current != null && UiHost.Current.IsReady, 90, ok);
        if (!Check("UI ready", ok[0])) yield break;
        yield return CuOk(new { cmd = "scene.pause", paused = false }, "the pause off");
        yield return CuOk(new { cmd = "scene.new" }, "scene.new");
        yield return CuOk(new { cmd = "chara.load", path = femaleRel, sex = "female" }, "chara.load the female target");
        cuA = Key(pLast);
        yield return CuOk(new { cmd = "chara.load", path = maleRel, sex = "male" }, "chara.load the male target");
        cuB = Key(pLast);
        yield return CuOk(new { cmd = "item.add", no = 399 }, "item.add");
        cuItem = Key(pLast);
        yield return CuOk(new { cmd = "item.add", no = 399 }, "a second item");
        cuItem2 = Key(pLast);
        var ops = CuUi.Ops;
        var folder = ops.AddFolder(null, null, -1, UiContext.User());
        cuFolder = folder.NewIds.FirstOrDefault();
        yield return CuOk(new { cmd = "light.add", no = 0 }, "light.add");
        cuLight = Key(pLast);
        var camera = ops.AddCamera(null, null, -1, UiContext.User());
        cuCamera = camera.NewIds.FirstOrDefault();
        var route = ops.AddRoute(null, null, -1, null, null, UiContext.User());
        cuRoute = route.NewIds.FirstOrDefault();
        Check("a folder, a light, a camera and a route", cuFolder != null && cuLight != null && cuCamera != null && cuRoute != null, cuFolder + " " + cuLight + " " + cuCamera + " " + cuRoute);
        yield return PlayModeHarness.WaitUntil(() => CuApp.Runtime.FindChara(cuA)?.State == RealizationState.Built && CuApp.Runtime.FindChara(cuB)?.State == RealizationState.Built && CuApp.Runtime.FindItem(cuItem)?.State == RealizationState.Built, 180, ok);
        if (!Check("everything built", ok[0])) yield break;
        // apart, so that the views and the handles of one do not cover the other
        CuUi.Session.Set(cuB, "transform.pos", new Vec3(1.5f, 0f, 0f), UiContext.User());
        CuUi.Session.Set(cuItem, "transform.pos", new Vec3(-1f, 1f, 0f), UiContext.User());
        CuUi.Session.Set(cuItem2, "transform.pos", new Vec3(-1.5f, 1f, 0f), UiContext.User());
        yield return CuOk(new { cmd = "scene.pause", paused = true }, "the global pause");
        CuUi.Shell.ResetLayout();
        yield return CuFrames(3);
        cuReady = true;
    }

    /// <summary>The panel shown, its list empty, no edit left over.</summary>
    static IEnumerator CuOpenPanel()
    {
        yield return CuPrepare();
        ConstraintsUiModule.Show(CuUi);
        yield return CuFrames(3);
        CuUi.Ops.ClearConstraints(UiContext.User());
        CuUi.Runtime?.ApplyNow();
        CuPanel?.Select(null);
        yield return CuFrames(3);
        CuPanel?.ReadNow();
        Check("the panel is shown, its list empty", CuPanel != null && CuVisible(CuPanel) && CuCount == 0, CuCount.ToString());
    }

    static string CuAdd(ConstraintEnd source, ConstraintEnd target, params string[] components)
    {
        var r = CuUi.Ops.AddConstraint(new ConstraintAddRequest { Source = source, Target = target, Components = components, KeepOffset = true }, UiContext.User(OpLabels.ConstraintAdd));
        Check("an entry for the check", r.Ok, r.Ok ? "" : r.Code + " " + r.Message);
        CuUi.Runtime?.ApplyNow();
        return (r.Value as ConstraintInfo)?.Entry?.Id;
    }

    static ConstraintEnd CuNode(string id) => new ConstraintEnd(id);

    static IEnumerator CuReadAll()
    {
        yield return CuFrames(2);
        CuPanel?.ReadNow();
        yield return CuFrames(2);
    }

    static ConstraintLine CuLine(int i) => CuPanel?.Lines.FirstOrDefault(l => l.name == "constraints-row-" + i);

    static IEnumerator Play_F_Ui()
    {
        var groups = new List<(string id, string title, Func<IEnumerator> run)>
        {
            ("CU1", "the panel and the layout", CU1),
            ("CU2", "Add by node, bone and IK target; a cycle", CU2),
            ("CU3", "a drag is one entry", CU3),
            ("CU4", "Esc during a drag", CU4),
            ("CU5", "every state's row", CU5),
            ("CU6", "the marks", CU6),
            ("CU7", "the section", CU7),
            ("CU8", "subscribers and tickers", CU8),
            ("CU9", "the bone popup", CU9),
            ("CU10", "three languages, no community name", CU10),
            ("CU11", "widths 360 and 300", CU11),
            ("CD4", "the gizmo's refusal", CD4),
        };
        foreach (var (id, title, run) in groups)
        {
            if (!POpen(id, title)) continue;
            // a group's exception is a FAIL of the group: the harness would end the whole row on it
            var stack = new Stack<IEnumerator>();
            stack.Push(run());
            while (stack.Count > 0)
            {
                object cur;
                try
                {
                    if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                    cur = stack.Peek().Current;
                }
                catch (Exception ex) { Check("exception", false, ex.GetType().Name + ": " + ex.Message + " " + ex.StackTrace); break; }
                if (cur is IEnumerator nested) { stack.Push(nested); continue; }
                yield return cur;
            }
            if (CuOpenEdit) { CuUi.ActiveEdit.Cancel(); Check("no edit left open after the group", false); }
            CuUi?.Menus?.CloseAll();
            var toasts = CuUi?.Toasts?.History.Where(t => t.StartsWith("Error", StringComparison.Ordinal)).ToList();
            if (toasts != null && toasts.Count > cuToasts) Info(id + " error toasts: " + string.Join(" | ", toasts.Skip(cuToasts)));
            cuToasts = toasts?.Count ?? 0;
        }
    }

    // ------------------------------------------------------------ CU1

    static IEnumerator CU1()
    {
        yield return CuPrepare();
        var ui = CuUi;
        var sh = ui.Shell;
        var a = ui.Registry;
        // an earlier run of this row left its layout: back to the state of a first start
        if (a.Panels.TryGet(ConstraintsUiModule.PanelId, out _))
        {
            Info("the panel stood registered from an earlier run's layout: taken off for this check");
            a.Panels.Unregister(ConstraintsUiModule.PanelId);
            sh.ResetLayout();
            yield return CuFrames(3);
        }
        var bottom = sh.Regions[LayoutRegions.Bottom];
        Check("not registered until opened", !a.Panels.TryGet(ConstraintsUiModule.PanelId, out _) && CuQ("tab-" + ConstraintsUiModule.PanelId) == null);
        Check("the bottom region stays collapsed", bottom.Collapsed && !bottom.Panels.Contains(ConstraintsUiModule.PanelId), "panels [" + string.Join(", ", bottom.Panels) + "]");
        Check("the layout's version stands", LayoutDocument.CurrentVersion == 2 && sh.Layout.Version == 2, sh.Layout.Version.ToString());
        yield return CuClick(CuQ("tb-view"), "the View menu");
        Check("the View menu lists the panel", CuQ("menu-item-" + ActionIds.ConstraintsPanel) != null);
        ui.Menus?.CloseAll();
        yield return CuFrames(2);
        yield return CuOk(new { cmd = "ui.action", action = ActionIds.ConstraintsPanel }, "ui.action constraints.panel (open)");
        yield return CuFrames(3);
        Check("it opens the bottom region with the panel shown", ConstraintsUiModule.PanelShown(ui) && !bottom.Collapsed && CuPanel != null && CuVisible(CuPanel), bottom.Collapsed.ToString());
        yield return CuCall(new { cmd = "ui.state" });
        var pe = (pLast?["panels"] as JArray)?.FirstOrDefault(x => (string)x["id"] == ConstraintsUiModule.PanelId);
        Check("ui.state lists it, active, in the bottom region", pe != null && (string)pe["region"] == "bottom" && pe.Value<bool>("active"), pe?.ToString(Formatting.None));
        yield return CuCall(new { cmd = "ui.query", name = "constraints-list" });
        Check("ui.query finds the list", POk && (pLast["elements"] as JArray)?.Count == 1, Short(pLastText));
        yield return CuOk(new { cmd = "ui.action", action = ActionIds.ConstraintsPanel }, "ui.action constraints.panel (collapse)");
        yield return CuFrames(3);
        Check("again collapses the region", bottom.Collapsed && !ConstraintsUiModule.PanelShown(ui));
        yield return CuOk(new { cmd = "ui.action", action = ActionIds.ConstraintsPanel }, "ui.action constraints.panel (open again)");
        yield return CuFrames(3);
        Check("a third time shows it", ConstraintsUiModule.PanelShown(ui) && CuVisible(CuPanel));
        yield return PlayModeHarness.WaitRealtime(1.6);
        Check("the saved layout lists it, so the next start registers it", ConstraintsUiModule.LayoutListsPanel(ui));
        string file = Path.Combine(ui.Options.ConfigDir, LayoutDocument.FileName);
        var doc = LayoutDocument.Parse(File.ReadAllText(file));
        Check("the saved layout keeps the version", doc != null && doc.Version == LayoutDocument.CurrentVersion && doc.RegionOf(ConstraintsUiModule.PanelId) == LayoutRegions.Bottom, doc?.Version + " / " + doc?.RegionOf(ConstraintsUiModule.PanelId));
        // with the Timeline's panel beside it
        TimelineUiModule.EnsurePanel(ui);
        yield return CuFrames(2);
        Check("two panels share the bottom region", bottom.Panels.Contains(TimelineUiModule.PanelId) && bottom.Panels.Contains(ConstraintsUiModule.PanelId), string.Join(", ", bottom.Panels));
        sh.ResetLayout();
        yield return CuFrames(3);
        bottom = sh.Regions[LayoutRegions.Bottom];
        Check("Reset layout does not open the bottom region for them", bottom.Collapsed, string.Join(", ", bottom.Panels));
        ConstraintsUiModule.Show(ui);
        yield return CuFrames(3);
        Check("the panel opens again after Reset layout", ConstraintsUiModule.PanelShown(ui) && CuVisible(CuPanel));
    }

    // ------------------------------------------------------------ CU2

    static IEnumerator CU2()
    {
        yield return CuOpenPanel();
        var p = CuPanel;
        if (p == null) yield break;
        var ed = p.Editor;
        Check("the hint stands while nothing is selected", CuVisible(CuQ("constraints-hint")) && !CuVisible(CuQ("constraints-editor-body")));
        Check("an empty scene says so, Clear is off", CuVisible(CuQ("constraints-none")) && !CuQ("constraints-clear").enabledSelf);

        // node on node, by the buttons
        int u0 = CuUndos;
        var held = CuApp.Runtime.HolderOf(cuFolder).position;
        yield return CuClick(CuQ("constraints-add"), "Add");
        Check("Add opens the editor empty", ed.Adding && CuVisible(CuQ("constraints-editor-body")) && !CuVisible(CuQ("constraints-offset")) && CuVisible(CuQ("constraints-new-hint")));
        yield return CuOk(new { cmd = "select", keys = new[] { cuItem } }, "select the item");
        yield return CuFrames(2);
        yield return CuClick(CuQ("constraints-source-use"), "Use selection of the source");
        Check("half a pair creates nothing", CuCount == 0 && CuUndos == u0 + 0 && ed.Adding, CuCount.ToString());
        Check("the picker shows the node", (CuQ("constraints-source-node") as RefPicker)?.Value == cuItem);
        ed.PickNode(true, cuFolder);
        yield return CuReadAll();
        string first = ed.Id;
        Check("the first complete pair creates the entry, one history entry «Add constraint»", CuCount == 1 && first != null && !ed.Adding && CuUndos == u0 + 1 && CuTop == OpLabels.ConstraintAdd, CuCount + ", " + (CuUndos - u0) + " / " + CuTop);
        Check("the offset was taken: the target stays where it was", (CuApp.Runtime.HolderOf(cuFolder).position - held).magnitude < 1e-5f, (CuApp.Runtime.HolderOf(cuFolder).position - held).magnitude.ToString("G4"));
        Check("the row shows it, active, source → target", p.Rows.Count == 1 && p.Rows[0].State == ConstraintStates.Active && CuLine(0)?.Text.text.Contains("→") == true, CuLine(0)?.Text.text);
        Check("position and rotation are on by default", CuList.Entries[0].Has(ConstraintComponents.Position) && CuList.Entries[0].Has(ConstraintComponents.Rotation));
        yield return CuCall(new { cmd = "ui.query", name = "constraints-row-0-weight" });
        Check("ui.query finds the row's slider", POk && (pLast["elements"] as JArray)?.Count == 1, Short(pLastText));
        CuUi.Session.Commands.Undo();
        CuUi.Runtime?.ApplyNow();
        yield return CuReadAll();
        Check("undo takes it away; the row and the editor go", CuCount == 0 && p.Rows.Count == 0 && ed.Id == null, CuCount.ToString());
        CuUi.Session.Commands.Redo();
        CuUi.Runtime?.ApplyNow();
        yield return CuReadAll();
        Check("redo brings it back", CuCount == 1 && p.Rows.Count == 1);

        // a bone as a source
        u0 = CuUndos;
        p.BeginAdd();
        yield return CuFrames(2);
        yield return CuClick(CuQ("constraints-source-kind")?.Q("seg-1"), "the source's kind Bone");
        Check("the bone field shows", CuVisible(CuQ("constraints-source-bone")));
        Check("the bone field is off until a node is picked", !CuQ("constraints-source-bone").enabledSelf);
        ed.PickNode(false, cuA);
        ed.PickBone(false, "cf_J_Hand_R");
        ed.PickNode(true, cuItem2);
        yield return CuReadAll();
        Check("a bone as source: one entry", CuCount == 2 && CuUndos == u0 + 1 && CuTop == OpLabels.ConstraintAdd && CuList.Entries[1].Source.Bone == "cf_J_Hand_R", CuCount + ", " + (CuUndos - u0));

        // a bone as a target
        u0 = CuUndos;
        p.BeginAdd();
        ed.PickNode(false, cuLight);
        ed.PickKind(true, 1);
        ed.PickNode(true, cuB);
        Check("a target bone waits for the bone", CuCount == 2 && ed.Adding);
        ed.PickBone(true, "cf_J_Neck");
        yield return CuReadAll();
        Check("a bone as target: one entry", CuCount == 3 && CuUndos == u0 + 1 && CuList.Entries[2].Target.Bone == "cf_J_Neck", CuCount + ", " + (CuUndos - u0));

        // an IK target
        u0 = CuUndos;
        p.BeginAdd();
        ed.PickNode(false, cuCamera);
        yield return CuClick(CuQ("constraints-target-kind")?.Q("seg-2"), "the target's kind IK target");
        var ik = CuQ("constraints-target-ik") as DropdownField;
        Check("the 13 targets", ik != null && CuVisible(ik) && ik.choices.Count == 13, ik?.choices.Count.ToString());
        ed.PickNode(true, cuA);
        ik.index = Array.IndexOf(IkTargets.Names, "LeftHand");
        yield return CuReadAll();
        Check("an IK target: one entry", CuCount == 4 && CuUndos == u0 + 1 && CuList.Entries[3].Target.Ik == "LeftHand", CuCount + ", " + (CuUndos - u0) + ", " + ed.ErrorText + ", adding " + ed.Adding);
        Check("a hand takes rotation", CuList.Entries[3].Has(ConstraintComponents.Rotation));
        u0 = CuUndos;
        p.BeginAdd();
        ed.PickNode(false, cuCamera);
        ed.PickKind(true, 2);
        ed.PickNode(true, cuB);
        ik = CuQ("constraints-target-ik") as DropdownField;
        ik.index = Array.IndexOf(IkTargets.Names, "LeftShoulder");
        yield return CuReadAll();
        Check("a target without rotation: the entry leaves it out", CuCount == 5 && CuList.Entries[4].Has(ConstraintComponents.Position) && !CuList.Entries[4].Has(ConstraintComponents.Rotation), CuCount.ToString());
        Check("rotation, look at and scale are off on such a target", !(CuQ("constraints-comp-rotation") as Toggle).enabledSelf && !(CuQ("constraints-comp-lookAt") as Toggle).enabledSelf && !(CuQ("constraints-comp-scale") as Toggle).enabledSelf);

        // a cycle: the folder follows the item, so the item cannot follow the folder
        u0 = CuUndos;
        int count = CuCount;
        p.BeginAdd();
        ed.PickNode(false, cuFolder);
        ed.PickNode(true, cuItem);
        yield return CuReadAll();
        Check("a cycle shows its text under the pickers, the rows in it", ed.ErrorText.StartsWith("This would close a loop (", StringComparison.Ordinal) && ed.ErrorText.Contains(" -> ") && CuVisible(CuQ("constraints-error")), ed.ErrorText);
        Check("the operation keeps its English text", ed.LastResult.Message.StartsWith("constraint cycle: ", StringComparison.Ordinal), ed.LastResult.Message);
        Check("a cycle adds nothing, the form stays", CuCount == count && CuUndos == u0 && ed.Adding, CuCount + ", " + (CuUndos - u0));
        ed.PickNode(false, cuLight);
        yield return CuReadAll();
        Check("a corrected pair goes through, the text goes", CuCount == count + 1 && !ed.Adding && ed.ErrorText.Length == 0, ed.ErrorText);
        yield return CuClick(CuQ("constraints-clear"), "Clear");
        yield return CuReadAll();
        Check("Clear: one entry «Clear constraints», the list empty", CuCount == 0 && CuTop == OpLabels.ConstraintClear && p.Rows.Count == 0, CuTop);
        CuUi.Session.Commands.Undo();
        CuUi.Runtime?.ApplyNow();
        yield return CuReadAll();
        Check("undo of Clear brings every row back", CuCount == count + 1 && p.Rows.Count == count + 1);

        // up, down, remove, the switch, the label, the components, take and clear offset: one entry each
        var line = CuLine(1);
        string id1 = line?.Id;
        u0 = CuUndos;
        yield return CuClick(line?.Up, "Up");
        yield return CuReadAll();
        Check("Up: one entry «Reorder constraints»", CuList.Entries[0].Id == id1 && CuUndos == u0 + 1 && CuTop == OpLabels.ConstraintReorder, CuTop);
        Check("the first row's Up is off, the last's Down too", !CuLine(0).Up.enabledSelf && CuLine(0).Down.enabledSelf);
        yield return CuClick(CuLine(0).Down, "Down");
        yield return CuReadAll();
        Check("Down: back", CuList.Entries[1].Id == id1 && CuUndos == u0 + 2);
        u0 = CuUndos;
        yield return CuClick(CuLine(1).On, "the switch");
        yield return CuReadAll();
        Check("the switch: one entry «Constraint», off", !CuList.Entries[1].On && CuUndos == u0 + 1 && CuTop == OpLabels.Constraint && p.Rows[1].State == ConstraintStates.Off, p.Rows[1].State);
        yield return CuClick(CuLine(1).Text, "a row's text");
        Check("a click on a row selects it", p.Selected == id1 && ed.Id == id1 && CuLine(1).ClassListContains("hs-constraints__line--selected"), p.Selected);
        u0 = CuUndos;
        yield return CuClick(CuQ("constraints-take-offset"), "Take offset");
        yield return CuReadAll();
        Check("Take offset on an entry that is off: one entry «Take offset» at most", CuUndos <= u0 + 1 && (CuUndos == u0 || CuTop == OpLabels.ConstraintTakeOffset), CuTop);
        u0 = CuUndos;
        (CuQ("constraints-comp-scale") as Toggle).value = true;
        yield return CuReadAll();
        Check("a component: one entry, the offset row shows", CuList.Find(id1).Has(ConstraintComponents.Scale) && CuUndos == u0 + 1 && CuVisible(CuQ("constraints-offset-scale")));
        Check("parent is off beside position and rotation", !(CuQ("constraints-comp-parent") as Toggle).enabledSelf);
        Check("its tooltip says what an attach point is for", CuQ("constraints-comp-parent").tooltip == L10n.Get("constraints.comp.parentTip"));
        u0 = CuUndos;
        (CuQ("constraints-label") as TextField).value = "a label";
        yield return CuReadAll();
        Check("the label: one entry, the row shows it", CuList.Find(id1).Label == "a label" && CuUndos == u0 + 1 && CuLine(1).Text.text == "a label", CuLine(1).Text.text);
        u0 = CuUndos;
        yield return CuClick(CuLine(1).Remove, "Remove");
        yield return CuReadAll();
        Check("Remove: one entry «Remove constraint», the editor empties", CuList.Find(id1) == null && CuUndos == u0 + 1 && CuTop == OpLabels.ConstraintRemove && p.Selected == null, CuTop);
    }

    // ------------------------------------------------------------ CU3, CU4

    static string CuEntryText(string id)
    {
        var e = CuList?.Find(id);
        return e == null ? null : string.Join("|", e.On, e.Weight.ToString("R"), e.Offset.Pos, e.Offset.Rot, e.Offset.Scale, e.Label);
    }

    static IEnumerator CU3()
    {
        yield return CuOpenPanel();
        var p = CuPanel;
        if (p == null) yield break;
        string id = CuAdd(CuNode(cuItem), CuNode(cuFolder), ConstraintComponents.Position, ConstraintComponents.Rotation);
        yield return CuReadAll();
        p.Select(id);
        yield return CuFrames(3);

        // the row's slider
        var grip = CuLine(0).Weight.Slider.Q("unity-drag-container");
        Vector2 at = grip.worldBound.center;
        int u0 = CuUndos;
        float w0 = CuList.Find(id).Weight;
        var seen = new HashSet<float>();
        CuRaw(EventType.MouseDown, at);
        for (int i = 1; i <= 40; i++)
        {
            CuRaw(EventType.MouseDrag, at + new Vector2(-i * 0.5f, 0), new Vector2(-0.5f, 0));
            yield return CuFrames(1);
            seen.Add(CuList.Find(id).Weight);
        }
        CuRaw(EventType.MouseUp, at + new Vector2(-20, 0));
        yield return CuReadAll();
        float w1 = CuList.Find(id).Weight;
        Check("the row's weight: 40 moves, a preview on each", seen.Count >= 20, seen.Count + " values");
        Check("the row's weight: one entry «Constraint»", CuUndos == u0 + 1 && CuTop == OpLabels.Constraint && Math.Abs(w1 - w0) > 0.01f, (CuUndos - u0) + " / " + CuTop + ", " + w0 + " -> " + w1);
        CuUi.Session.Commands.Undo();
        CuUi.Runtime?.ApplyNow();
        yield return CuReadAll();
        Check("one undo gives the weight back", CuList.Find(id).Weight == w0 && CuUndos == u0, CuList.Find(id).Weight.ToString());

        // the editor's label drag of the weight
        var ws = p.Editor.Weight;
        yield return CuReveal(ws.LabelElement);
        at = ws.LabelElement.worldBound.center;
        u0 = CuUndos;
        seen.Clear();
        CuRaw(EventType.MouseDown, at);
        for (int i = 1; i <= 40; i++)
        {
            CuRaw(EventType.MouseDrag, at + new Vector2(-i * 3, 0), new Vector2(-3, 0));
            yield return CuFrames(1);
            seen.Add(CuList.Find(id).Weight);
        }
        CuRaw(EventType.MouseUp, at + new Vector2(-120, 0));
        yield return CuReadAll();
        Check("the editor's weight label: 40 moves, one entry", seen.Count >= 20 && CuUndos == u0 + 1 && CuTop == OpLabels.Constraint, seen.Count + " values, " + (CuUndos - u0) + " entries");
        yield return CuUndoTo(u0);

        // an offset's axis label
        var vf = p.Editor.PosRow.Field;
        var f = vf.Fields[0];
        var label = f.labelElement;
        yield return CuReveal(label);
        at = label.worldBound.center;
        Check("the axis label is under the pointer", CuPanelRoot.Pick(at) == label, CuName(CuPanelRoot.Pick(at) as VisualElement) + " at " + at);
        u0 = CuUndos;
        string e0 = CuEntryText(id);
        var pos = new HashSet<float>();
        CuRaw(EventType.MouseDown, at);
        for (int i = 1; i <= 40; i++)
        {
            CuRaw(EventType.MouseDrag, at + new Vector2(i, 0), new Vector2(6, 0));
            yield return CuFrames(1);
            pos.Add(CuList.Find(id).Offset.Pos.X);
        }
        CuRaw(EventType.MouseUp, at + new Vector2(40, 0));
        yield return CuFrames(2);
        f.Blur();
        yield return CuReadAll();
        Check("the offset's label: 40 moves, a preview on each", pos.Count >= 30, pos.Count + " values");
        Check("the offset's label: one entry «Constraint»", CuUndos == u0 + 1 && CuTop == OpLabels.Constraint && CuEntryText(id) != e0, (CuUndos - u0) + " / " + CuTop);
        CuUi.Session.Commands.Undo();
        CuUi.Runtime?.ApplyNow();
        yield return CuReadAll();
        Check("one undo gives the offset back", CuEntryText(id) == e0 && CuUndos == u0, Short(CuEntryText(id)));

        // back to the start: no entry
        u0 = CuUndos;
        var line = CuLine(0);
        line.Weight.Begin();
        line.Weight.Raise(EditPhase.Preview, 0.4);
        yield return CuFrames(2);
        Check("a drag writes", CuList.Find(id).Weight != w0);
        line.Weight.Raise(EditPhase.Preview, w0);
        yield return CuFrames(2);
        line.Weight.End();
        yield return CuFrames(2);
        Check("a drag that ends where it began: no entry", CuUndos == u0 && CuEntryText(id) == e0, (CuUndos - u0).ToString());

        // typed values chain
        u0 = CuUndos;
        p.Editor.Weight.Raise(EditPhase.Typed, 0.7);
        yield return CuFrames(2);
        p.Editor.Weight.Raise(EditPhase.Typed, 0.6);
        yield return CuFrames(2);
        Check("two typed weights: one entry", Math.Abs(CuList.Find(id).Weight - 0.6f) < 1e-5f && CuUndos == u0 + 1, (CuUndos - u0).ToString());
        p.Editor.PosRow.Field.Fields[1].value = 0.25f;
        yield return CuFrames(2);
        Check("a typed offset keeps the other components, one entry more", Math.Abs(CuList.Find(id).Offset.Pos.Y - 0.25f) < 1e-5f && CuUndos == u0 + 2, CuList.Find(id).Offset.Pos.ToString());
        yield return CuUndoTo(u0);
    }

    static IEnumerator CuEscNumber(string what, NumberSlider ns, bool onSlider, string id)
    {
        VisualElement grip = onSlider ? (ns.Slider?.Q("unity-drag-container") ?? (VisualElement)ns.Slider) : ns.LabelElement;
        if (!Check(what + ": the element to press", grip != null)) yield break;
        yield return CuReveal(grip);
        Vector2 at = grip.worldBound.center;
        var picked = CuPanelRoot.Pick(at) as VisualElement;
        if (!Check(what + ": it is under the pointer", picked != null && (picked == grip || grip.Contains(picked)), CuName(picked) + " at " + at)) yield break;
        string a0 = CuEntryText(id);
        int u0 = CuUndos;
        var held = CuApp.Runtime.HolderOf(cuFolder).position;
        CuRaw(EventType.MouseDown, at);
        for (int i = 1; i <= 3; i++) { CuRaw(EventType.MouseDrag, at + new Vector2(-i * 7, 0), new Vector2(-7, 0)); yield return CuFrames(1); }
        Check(what + ": the drag runs and writes", ns.Dragging && CuCapturing() != null && CuEntryText(id) != a0, ns.Dragging + ", " + CuName(CuCapturing()));
        CuEsc();
        Check(what + " Esc: no pointer held, no drag, no open edit", CuCapturing() == null && !ns.Dragging && !CuOpenEdit, CuName(CuCapturing()) + ", dragging " + ns.Dragging);
        Check(what + " Esc: the entry as at the press, no history entry", CuEntryText(id) == a0 && CuUndos == u0, Short(CuEntryText(id)) + ", entries " + (CuUndos - u0));
        yield return CuFrames(3);
        CuRaw(EventType.MouseDrag, at + new Vector2(-30, 0), new Vector2(-3, 0));
        CuRaw(EventType.MouseDrag, at + new Vector2(-40, 0), new Vector2(-10, 0));
        yield return CuFrames(1);
        Check(what + " moves after Esc: nothing", CuEntryText(id) == a0 && CuUndos == u0, Short(CuEntryText(id)));
        CuRaw(EventType.MouseUp, at + new Vector2(-40, 0));
        yield return CuFrames(2);
        Check(what + " the release after Esc: nothing", CuEntryText(id) == a0 && CuUndos == u0 && !ns.Dragging, Short(CuEntryText(id)));
        Check(what + " the shown value is the stored one", Math.Abs((float)ns.Value - CuList.Find(id).AppliedWeight) < 1e-5f, ns.Value + " / " + CuList.Find(id).AppliedWeight);
        CuRaw(EventType.MouseDown, at);
        for (int i = 1; i <= 2; i++) { CuRaw(EventType.MouseDrag, at + new Vector2(-i * 7, 0), new Vector2(-7, 0)); yield return CuFrames(1); }
        CuRaw(EventType.MouseUp, at + new Vector2(-14, 0));
        yield return CuFrames(3);
        Check(what + " a new drag afterwards: one entry", CuUndos == u0 + 1 && CuEntryText(id) != a0 && CuTop == OpLabels.Constraint, (CuUndos - u0) + " / " + CuTop);
        yield return CuUndoTo(u0);
        Check(what + " undo: as at the press", CuEntryText(id) == a0, Short(CuEntryText(id)));
    }

    static IEnumerator CuEscVector(string what, VectorField vf, int c, string id)
    {
        var f = vf.Fields[c];
        var label = f.labelElement;
        yield return CuReveal(label);
        Vector2 at = label.worldBound.center;
        if (!Check(what + ": the axis label is under the pointer", CuPanelRoot.Pick(at) == label, CuName(CuPanelRoot.Pick(at) as VisualElement) + " at " + at)) yield break;
        string a0 = CuEntryText(id);
        int u0 = CuUndos;
        CuRaw(EventType.MouseDown, at);
        Check(what + ": the press: the label holds the pointer, an edit is open", CuCapturing() == label && vf.Dragging && CuOpenEdit, CuName(CuCapturing()));
        for (int i = 1; i <= 3; i++) { CuRaw(EventType.MouseDrag, at + new Vector2(i, 0), new Vector2(6, 0)); yield return CuFrames(1); }
        Check(what + ": the drag writes", CuEntryText(id) != a0, Short(CuEntryText(id)));
        CuEsc();
        Check(what + " Esc: no pointer held, no drag, no open edit", CuCapturing() == null && !vf.Dragging && !CuOpenEdit, CuName(CuCapturing()) + ", dragging " + vf.Dragging);
        Check(what + " Esc: the entry as at the press, no history entry", CuEntryText(id) == a0 && CuUndos == u0, Short(CuEntryText(id)) + ", entries " + (CuUndos - u0));
        yield return CuFrames(3);
        string text0 = f.text;
        CuRaw(EventType.MouseDrag, at + new Vector2(1, 0), new Vector2(4, 0));
        CuRaw(EventType.MouseDrag, at + new Vector2(300, 200), new Vector2(4, 0));
        CuRaw(EventType.MouseDrag, at, new Vector2(4, 0));
        yield return CuFrames(1);
        Check(what + " moves after Esc: nothing", CuEntryText(id) == a0 && CuUndos == u0 && f.text == text0, Short(CuEntryText(id)) + ", text " + f.text + " / " + text0);
        CuRaw(EventType.MouseUp, at);
        yield return CuFrames(1);
        f.Focus();
        yield return CuFrames(1);
        f.Blur();
        yield return CuFrames(2);
        Check(what + " the release and a later blur: nothing", CuEntryText(id) == a0 && CuUndos == u0, Short(CuEntryText(id)) + ", entries " + (CuUndos - u0));
        CuRaw(EventType.MouseDown, at);
        for (int i = 1; i <= 2; i++) { CuRaw(EventType.MouseDrag, at + new Vector2(i, 0), new Vector2(6, 0)); yield return CuFrames(1); }
        CuRaw(EventType.MouseUp, at);
        yield return CuFrames(2);
        f.Blur();
        yield return CuFrames(1);
        Check(what + " a new drag afterwards: one entry", CuUndos == u0 + 1 && CuEntryText(id) != a0 && CuTop == OpLabels.Constraint, (CuUndos - u0) + " / " + CuTop);
        yield return CuUndoTo(u0);
        Check(what + " undo: as at the press", CuEntryText(id) == a0, Short(CuEntryText(id)));
    }

    static IEnumerator CU4()
    {
        yield return CuOpenPanel();
        var p = CuPanel;
        if (p == null) yield break;
        string id = CuAdd(CuNode(cuItem), CuNode(cuFolder), ConstraintComponents.Position, ConstraintComponents.Rotation);
        yield return CuReadAll();
        p.Select(id);
        yield return CuFrames(3);
        yield return CuEscNumber("the row's slider", CuLine(0).Weight, true, id);
        yield return CuReadAll();
        yield return CuEscNumber("the editor's slider", p.Editor.Weight, true, id);
        yield return CuReadAll();
        yield return CuEscNumber("the editor's weight label", p.Editor.Weight, false, id);
        yield return CuReadAll();
        yield return CuEscVector("the position offset", p.Editor.PosRow.Field, 0, id);
        yield return CuReadAll();
        yield return CuEscVector("the rotation offset", p.Editor.RotRow.Field, 2, id);
        yield return CuReadAll();
        // the section's row
        yield return CuOk(new { cmd = "select", keys = new[] { cuFolder } }, "select the folder");
        yield return CuOk(new { cmd = "ui.panel", panel = "inspector", op = "show" }, "show the inspector");
        yield return CuFrames(3);
        CuInsp.Refresh(true);
        yield return CuFrames(3);
        var fo = CuQ(ConstraintSection.FoldName) as Foldout;
        if (Check("the folder's section", fo != null))
        {
            fo.value = true;
            yield return CuFrames(3);
            var v = fo.Q<ConstraintSectionView>();
            v?.ReadNow();
            yield return CuFrames(2);
            if (Check("its row", v != null && v.Lines.Count == 1, v?.Lines.Count.ToString()))
                yield return CuEscNumber("the section's slider", v.Lines[0].Weight, true, id);
            fo.value = false;
            yield return CuFrames(2);
        }
    }

    // ------------------------------------------------------------ CU5

    static ConstraintEntry CuEntry(ConstraintEnd s, ConstraintEnd t, params string[] comps) =>
        new ConstraintEntry { Id = CuUi.Session.NewId(), Source = s, Target = t, Components = new List<string>(comps) };

    static IEnumerator CU5()
    {
        yield return CuOpenPanel();
        var p = CuPanel;
        if (p == null) yield break;
        var ed = p.Editor;
        var ops = CuUi.Ops;

        // off
        string id = CuAdd(CuNode(cuItem), CuNode(cuItem2), ConstraintComponents.Position);
        yield return CuReadAll();
        Check("active: no mark, no tip", p.Rows[0].State == ConstraintStates.Active && !CuVisible(CuLine(0).Mark) && CuLine(0).Mark.tooltip.Length == 0, CuLine(0).Mark.text);
        ops.SetConstraint(id, new ConstraintEdit { On = false }, UiContext.User());
        yield return CuReadAll();
        Check("off: dim, the tooltip says so", p.Rows[0].State == ConstraintStates.Off && CuLine(0).Text.ClassListContains(InspectorUi.Dim) && CuLine(0).Text.tooltip.Contains(L10n.Get("constraints.state.off")), CuLine(0).Text.tooltip);
        Check("off is no broken row", !CuLine(0).Text.ClassListContains("hs-constraints__text--broken"));
        ops.SetConstraint(id, new ConstraintEdit { On = true }, UiContext.User());

        // a source that goes, a target that goes: the row stays, greyed, Remove and retarget work
        string tmp = ops.AddFolder(null, null, -1, UiContext.User()).NewIds[0];
        string src = CuAdd(CuNode(tmp), CuNode(cuLight), ConstraintComponents.Position);
        yield return CuReadAll();
        int u0 = CuUndos;
        ops.Delete(new[] { tmp }, UiContext.User());
        CuUi.Runtime.ApplyNow();
        yield return CuReadAll();
        var i1 = ops.ConstraintOf(src);
        var line = CuLine(1);
        Check("a deleted source: the entry stays, sourceMissing", i1.State == ConstraintStates.SourceMissing && line != null, i1.State);
        Check("its tooltip names what is missing", line.Text.tooltip.Contains(L10n.Format("constraints.state.sourceMissing", i1.Reason)) && line.Mark.text == "!" && line.Mark.ClassListContains("hs-constraints__mark--warning"), line.Text.tooltip);
        Check("the row is greyed", line.Text.ClassListContains("hs-constraints__text--broken"));
        Check("Remove stays on", line.Remove.enabledSelf && line.On.enabledSelf);
        p.Select(src);
        yield return CuFrames(3);
        var sp = CuQ("constraints-source-node") as RefPicker;
        Check("the picker shows the stored id in the warning colour", sp != null && sp.Button.ClassListContains("hs-text--warning") && sp.Value == tmp, sp?.Button.text);
        ed.PickNode(false, cuItem);
        yield return CuReadAll();
        i1 = ops.ConstraintOf(src);
        Check("retarget works: active, one entry more", i1.State == ConstraintStates.Active && CuUndos == u0 + 2 && !sp.Button.ClassListContains("hs-text--warning"), i1.State);
        yield return CuUndoTo(u0);
        yield return CuReadAll();
        Check("undo: the node back, active again with no user step", ops.ConstraintOf(src).State == ConstraintStates.Active, ops.ConstraintOf(src).State);
        ops.Delete(new[] { cuLight }, UiContext.User());
        CuUi.Runtime.ApplyNow();
        yield return CuReadAll();
        i1 = ops.ConstraintOf(src);
        Check("a deleted target: targetMissing, the tooltip", i1.State == ConstraintStates.TargetMissing && CuLine(1).Text.tooltip.Contains(L10n.Format("constraints.state.targetMissing", i1.Reason)), CuLine(1).Text.tooltip);
        yield return CuUndoTo(u0);
        ops.RemoveConstraint(src, UiContext.User());

        // a missing bone
        string bone = CuAdd(new ConstraintEnd(cuA, "cf_J_Hand_R"), CuNode(cuFolder), ConstraintComponents.Position);
        CuUi.Session.Commands.Execute(ConstraintEditCommand.ReplaceEntry(WithBone(CuList.Find(bone), "no_such_bone")), UiContext.User());
        CuUi.Runtime.ApplyNow();
        yield return CuReadAll();
        p.Select(bone);
        yield return CuFrames(3);
        i1 = ops.ConstraintOf(bone);
        var bf = CuQ("constraints-source-bone") as Button;
        Check("a bone the body lacks: sourceMissing with the address", i1.State == ConstraintStates.SourceMissing && i1.Reason == "no_such_bone", i1.State + " " + i1.Reason);
        Check("the bone field shows the stored name in the warning colour", bf != null && bf.text == "no_such_bone" && bf.ClassListContains("hs-text--warning"), bf?.text);
        ed.PickBone(false, "cf_J_Hand_L");
        yield return CuReadAll();
        Check("the bone field retargets it", ops.ConstraintOf(bone).State == ConstraintStates.Active && !bf.ClassListContains("hs-text--warning"));
        ops.RemoveConstraint(bone, UiContext.User());

        // a loop that appears later: the source moves under the target
        string a = ops.AddFolder(null, null, -1, UiContext.User()).NewIds[0], b = ops.AddFolder(null, null, -1, UiContext.User()).NewIds[0];
        string loop = CuAdd(CuNode(a), CuNode(b), ConstraintComponents.Position);
        u0 = CuUndos;
        ops.Move(a, b, null, -1, true, UiContext.User());
        CuUi.Runtime.ApplyNow();
        yield return CuReadAll();
        i1 = ops.ConstraintOf(loop);
        int li = CuList.IndexOf(loop);
        Check("a tree move: suspended, state cycle, the rows in the tooltip", i1.State == ConstraintStates.Cycle && CuLine(li).Text.tooltip.Contains(L10n.Format("constraints.state.cycle", i1.Reason)), i1.State + " / " + CuLine(li)?.Text.tooltip);
        Check("its row keeps Remove", CuLine(li).Remove.enabledSelf);
        yield return CuUndoTo(u0);
        Check("undo: active", ops.ConstraintOf(loop).State == ConstraintStates.Active);
        ops.RemoveConstraint(loop, UiContext.User());
        ops.Delete(new[] { a, b }, UiContext.User());

        // protected, bad: what a file can hold
        var r = CuApp.Runtime.FindChara(cuA);
        var slot = r.Context.Accessories.FirstOrDefault(x => x != null && x.Object && r.AccessoryIdOf(x) != null);
        Transform fixture = null;
        if (Check("an accessory on the target", slot != null))
        {
            string part = PoseAddressGrammar.PartText(PosePart.Accessory, -1, r.AccessoryIdOf(slot));
            fixture = new GameObject("cu_part").transform;
            fixture.SetParent(slot.Object.transform, false);
            var member = new GameObject("o_gomu").transform;
            member.SetParent(fixture, false);
            member.gameObject.AddComponent<MeshRenderer>();
            var kept = CuEntry(CuNode(cuItem), new ConstraintEnd(cuA, part + "/o_gomu"), ConstraintComponents.Position);
            var list = new ConstraintList(CuList.Entries);
            list.Entries.Add(kept);
            CuUi.Session.Commands.Execute(ConstraintEditCommand.ReplaceList(list), UiContext.User());
            CuUi.Runtime.ApplyNow();
            yield return CuReadAll();
            i1 = ops.ConstraintOf(kept.Id);
            int pi = CuList.IndexOf(kept.Id);
            Check("a file's entry on a mosaic part: protected, the rule as the tooltip", i1.State == ConstraintStates.Protected && CuLine(pi).Text.tooltip.Contains(L10n.Get("constraints.state.protected")), i1.State + " / " + CuLine(pi)?.Text.tooltip);
            ops.RemoveConstraint(kept.Id, UiContext.User());
            UnityEngine.Object.DestroyImmediate(fixture.gameObject);
        }
        var bad = CuEntry(CuNode(cuFolder), new ConstraintEnd(cuItem, "some_bone"), ConstraintComponents.Position);
        var l2 = new ConstraintList(CuList.Entries);
        l2.Entries.Add(bad);
        CuUi.Session.Commands.Execute(ConstraintEditCommand.ReplaceList(l2), UiContext.User());
        CuUi.Runtime.ApplyNow();
        yield return CuReadAll();
        i1 = ops.ConstraintOf(bad.Id);
        int bi = CuList.IndexOf(bad.Id);
        Check("a bone target on an item: bad, the reason in the tooltip", i1.State == ConstraintStates.Bad && CuLine(bi).Text.tooltip.Contains(L10n.Format("constraints.state.bad", i1.Reason)), i1.State + " / " + CuLine(bi)?.Text.tooltip);
        ops.RemoveConstraint(bad.Id, UiContext.User());

        // the declared lag: a left hand on an item in the same character's right hand
        string rod = ops.AddFolder(cuA, "N_Hand_R", -1, UiContext.User()).NewIds.FirstOrDefault();
        string lag = rod != null ? CuAdd(CuNode(rod), new ConstraintEnd(cuA, null, "LeftHand"), ConstraintComponents.Position) : null;
        yield return CuReadAll();
        if (Check("the two-hand grip", lag != null))
        {
            i1 = ops.ConstraintOf(lag);
            int gi = CuList.IndexOf(lag);
            Check("a lag: the note shows, the tooltip explains", i1.Lag == 1 && CuVisible(CuLine(gi).Lag) && CuLine(gi).Lag.text == L10n.Get("constraints.lag") && CuLine(gi).Lag.tooltip == L10n.Get("constraints.lagTip") && CuLine(gi).Text.tooltip.Contains(L10n.Get("constraints.lagTip")), i1.Lag + " / " + CuLine(gi)?.Lag.text);
            Check("IK off: not effective, the row says so", !i1.Effective && i1.Reason == "constraints.ikOff" && CuLine(gi).Mark.text == "!" && CuLine(gi).Mark.tooltip.Contains(L10n.Get("constraints.ikOff")), i1.Reason + " / " + CuLine(gi)?.Mark.tooltip);
            ops.RemoveConstraint(lag, UiContext.User());
            ops.Delete(new[] { rod }, UiContext.User());
        }

        // a source of the same character
        string same = CuAdd(new ConstraintEnd(cuA, "cf_J_Hand_R"), new ConstraintEnd(cuA, "cf_J_Neck"), ConstraintComponents.Rotation);
        yield return CuReadAll();
        i1 = ops.ConstraintOf(same);
        int si = CuList.IndexOf(same);
        Check("a bone source on the same character: the note", i1.State == ConstraintStates.Active && i1.Reason == "constraints.sameCharaTip" && CuLine(si).Mark.text == "i" && CuLine(si).Mark.tooltip.Contains(L10n.Get("constraints.sameCharaTip")), i1.Reason + " / " + CuLine(si)?.Mark.tooltip);
        ops.RemoveConstraint(same, UiContext.User());

        // the states no scene of this row reaches: the row's text
        yield return CuReadAll();
        var row = new ConstraintLine(CuUi, "cu-test-", null, () => { });
        var e = CuList.Entries[0];
        foreach (var (state, key, arg) in new[] { (ConstraintStates.Pending, "constraints.state.pending", (object)null), (ConstraintStates.Limit, "constraints.state.limit", ConstraintLimits.MaxEntries) })
        {
            row.Bind(new ConstraintInfo { Entry = e, State = state }, 0, 1, 0, false, true);
            string want = arg != null ? L10n.Format(key, arg) : L10n.Get(key);
            Check("the row of " + state, row.Text.tooltip.Contains(want), row.Text.tooltip);
        }
        row.Bind(new ConstraintInfo { Entry = e, State = ConstraintStates.Active, Effective = false, Reason = "constraints.notShown" }, 0, 1, 0, false, true);
        Check("a hidden writer: the reason", row.Mark.text == "!" && row.Mark.tooltip.Contains(L10n.Get("constraints.notShown")), row.Mark.tooltip);
    }

    static ConstraintEntry WithBone(ConstraintEntry e, string bone)
    {
        var c = e.Clone();
        c.Source = new ConstraintEnd(e.Source.Node, bone);
        return c;
    }

    // ------------------------------------------------------------ CU6

    static Label CuMark(VisualElement host) => host?.Children().FirstOrDefault(c => c.name == TimelineOverrideUi.MarkName) as Label;

    static IEnumerator CuScan()
    {
        yield return CuFrames(2);
        TimelineOverrideUi.Scan(CuUi, CuUi.Runtime.Overrides);
        yield return CuFrames(1);
    }

    static IEnumerator CU6()
    {
        yield return CuOpenPanel();
        var ops = CuUi.Ops;
        yield return CuOk(new { cmd = "ui.panel", panel = "inspector", op = "show" }, "show the inspector");
        string owner = L10n.Get("panel.constraints");

        // a node
        string id = CuAdd(CuNode(cuFolder), CuNode(cuItem), ConstraintComponents.Position);
        yield return CuOk(new { cmd = "select", keys = new[] { cuItem } }, "select the item");
        yield return CuFrames(3);
        CuInsp.Refresh(true);
        yield return CuFrames(3);
        yield return CuScan();
        var row = CuQ("field-transform.pos");
        var mark = CuMark(row);
        Check("a transform row: the mark, the tooltip names Constraints", mark != null && mark.tooltip == L10n.Format("ui.override.tip", owner), mark?.tooltip);
        Check("the row stays editable", row.enabledInHierarchy);
        Check("position only: rotation has no mark", CuMark(CuQ("field-transform.rot")) == null);
        ops.RemoveConstraint(id, UiContext.User());
        CuUi.Runtime.ApplyNow();
        yield return CuScan();
        Check("gone with the constraint", CuMark(CuQ("field-transform.pos")) == null);

        // an IK target
        yield return CuOk(new { cmd = "ik.enable", key = cuA, on = true }, "IK on");
        yield return CuOk(new { cmd = "fk.enable", key = cuA, on = true }, "FK on");
        yield return CuOk(new { cmd = "fk.enable", key = cuA, groups = new[] { "Body" }, on = true }, "FK group Body");
        yield return CuOk(new { cmd = "select", keys = new[] { cuA } }, "select the character");
        yield return CuFrames(4);
        CuInsp.Refresh(true);
        yield return CuFrames(3);
        CuInsp.Chara.ShowTab("kinematics");
        yield return CuFrames(3);
        id = CuAdd(CuNode(cuFolder), new ConstraintEnd(cuA, null, "LeftHand"), ConstraintComponents.Position);
        yield return CuScan();
        var ik = CuTree.Query<IkTargetRow>().ToList();
        var pos = ik.FirstOrDefault(r => r.Channel == "chara.ik.targets.LeftHand.pos");
        var rot = ik.FirstOrDefault(r => r.Channel == "chara.ik.targets.LeftHand.rot");
        Check("an IK target row: the mark, the tooltip names Constraints", pos != null && CuMark(pos.Field)?.tooltip == L10n.Format("ui.override.tip", owner), CuMark(pos?.Field)?.tooltip);
        Check("position only: the rotation row has none", rot != null && CuMark(rot.Field) == null);
        Check("another target has none", CuMark(ik.First(r => r.Channel == "chara.ik.targets.RightHand.pos").Field) == null);
        // the source moves: the driven value leaves the model's, the row keeps the model's
        var ikd = IkDriver.Of(CuApp.Runtime.FindChara(cuA));
        var hand = HS2Next.Motion.Ik.IkTargetId.LeftHand;
        var modelValue = pos.ValueOf(cuA);
        int u1 = CuUndos;
        CuUi.Session.Set(cuFolder, "transform.pos", new Vec3(0.3f, 0.2f, 0.1f), UiContext.User());
        CuUi.Runtime.ApplyNow();
        yield return CuFrames(4);
        var shownValue = ikd.Shown(hand).Pos;
        Check("the source moved: the handle's value left the row's", (shownValue - pos.ValueOf(cuA)).magnitude > 0.05f, shownValue + " / " + pos.ValueOf(cuA));
        Check("the row shows the model value, not the driven one", (pos.ValueOf(cuA) - modelValue).magnitude < 1e-5f && ((Vector3)pos.Field.Value - shownValue).magnitude > 0.05f, pos.ValueOf(cuA) + " / " + modelValue + ", field " + pos.Field.Value);
        yield return CuUndoTo(u1);
        ops.RemoveConstraint(id, UiContext.User());
        CuUi.Runtime.ApplyNow();

        // an FK bone and the Advanced pose rows
        const string arm = "cf_J_ArmUp00_R";
        id = CuAdd(CuNode(cuFolder), new ConstraintEnd(cuA, arm), ConstraintComponents.Rotation);
        yield return CuFrames(3);
        CuInsp.Refresh(true);
        yield return CuFrames(3);
        CuInsp.Chara.ShowTab("kinematics");
        yield return CuFrames(3);
        yield return CuScan();
        var fk = CuQ("fk-" + arm);
        var fm = CuMark(fk);
        Check("an FK row: the mark names Constraints and says what runs after what", fm != null && fm.tooltip.Contains(L10n.Format("ui.override.tip", owner)) && fm.tooltip.Contains(L10n.Get("constraints.bandTip")), fm?.tooltip);
        var other = CuMark(CuQ("fk-cf_J_ArmUp00_L"));
        Check("another FK row has none", other == null);
        yield return CuOk(new { cmd = "select", sub = cuA + "/bone/" + arm }, "select the bone");
        yield return CuFrames(3);
        CuInsp.Chara.ShowTab(AdvancedPoseTab.TabId);
        yield return CuFrames(4);
        var tab = CuTree.Q<AdvancedPoseTab>();
        tab?.Refresh();
        yield return CuScan();
        Check("the bone is selected in the tab", tab != null && tab.SelectedBone == arm, tab?.SelectedBone);
        Check("the Advanced pose row of rotation: the mark", tab?.RotRow != null && CuMark(tab.RotRow.Field)?.tooltip == L10n.Format("ui.override.tip", owner), CuMark(tab?.RotRow?.Field)?.tooltip);
        Check("position and scale rows have none", CuMark(tab?.PosRow?.Field) == null && CuMark(tab?.ScaleRow?.Field) == null);
        Check("the row stays editable", tab.RotRow.enabledInHierarchy);
        ops.RemoveConstraint(id, UiContext.User());
        CuUi.Runtime.ApplyNow();
        yield return CuScan();
        Check("gone with the constraint", CuMark(tab?.RotRow?.Field) == null);
        yield return CuOk(new { cmd = "select", keys = new[] { cuA } }, "select the character alone");
        yield return CuOk(new { cmd = "ik.enable", key = cuA, on = false }, "IK off");
        yield return CuOk(new { cmd = "fk.enable", key = cuA, on = false }, "FK off");
        CuInsp.Chara?.ShowTab("anime");
        yield return CuFrames(2);
    }

    // ------------------------------------------------------------ CU7

    static IEnumerator CU7()
    {
        yield return CuOpenPanel();
        yield return CuOk(new { cmd = "ui.panel", panel = "inspector", op = "show" }, "show the inspector");
        var ops = CuUi.Ops;
        string id = CuAdd(CuNode(cuItem), CuNode(cuFolder), ConstraintComponents.Position);
        string id2 = CuAdd(CuNode(cuFolder), CuNode(cuLight), ConstraintComponents.Position);
        foreach (var (node, kind, rows) in new[] { (cuItem, "item", 1), (cuLight, "light", 1), (cuFolder, "folder", 2), (cuCamera, "camera", 0), (cuRoute, "route", 0) })
        {
            yield return CuOk(new { cmd = "select", keys = new[] { node } }, "select the " + kind);
            yield return CuFrames(3);
            CuInsp.Refresh(true);
            yield return CuFrames(3);
            var sec = CuTree.Q<ConstraintSection>();
            if (!Check(kind + ": the section stands", sec != null)) continue;
            sec.Fold.value = false;
            yield return CuFrames(2);
            Check(kind + ": collapsed by default, nothing of it attached", !sec.Fold.value && (sec.View == null || sec.View.panel == null), sec.View?.panel?.ToString());
            int reads = sec.View?.Reads ?? 0;
            yield return PlayModeHarness.WaitRealtime(0.7);
            Check(kind + ": closed reads nothing", (sec.View?.Reads ?? 0) == reads);
            yield return CuClick(sec.Fold.Q<Toggle>(), kind + ": the section's title");
            Check(kind + ": a click opens it", sec.Fold.value && sec.View != null && sec.View.panel != null);
            yield return CuFrames(3);
            sec.View?.ReadNow();
            yield return CuFrames(2);
            Check(kind + ": it lists the entries with this node at either end", sec.View?.Lines.Count == rows, sec.View?.Lines.Count + " / " + rows);
            Check(kind + ": one line and Add when it has none", (rows == 0) == CuVisible(CuQ("constraint-section-none")) && CuVisible(CuQ("constraint-section-add")));
            yield return PlayModeHarness.WaitRealtime(1.2);
            int open = sec.View.Reads;
            Check(kind + ": it reads while open, at most twice a second", open >= 2 && open <= 8, open.ToString());
            sec.Fold.value = false;
            yield return CuFrames(2);
            reads = sec.View.Reads;
            yield return PlayModeHarness.WaitRealtime(0.7);
            Check(kind + ": closed again: detached, no read", sec.View.panel == null && sec.View.Reads == reads, reads + " -> " + sec.View.Reads);
        }

        // the row in the section edits, and follows
        yield return CuOk(new { cmd = "select", keys = new[] { cuFolder } }, "select the folder");
        yield return CuFrames(3);
        CuInsp.Refresh(true);
        yield return CuFrames(3);
        var s = CuTree.Q<ConstraintSection>();
        s.Fold.value = true;
        yield return CuFrames(3);
        s.View.ReadNow();
        yield return CuFrames(2);
        int u0 = CuUndos;
        yield return CuClick(s.View.Lines[0].On, "the section's switch");
        yield return CuFrames(2);
        s.View.ReadNow();
        Check("the switch of a section's row: one entry, the row follows", !CuList.Find(id).On && CuUndos == u0 + 1 && !s.View.Lines[0].On.value);
        ops.SetConstraint(id, new ConstraintEdit { On = true }, UiContext.User());
        CuUi.Runtime.ApplyNow();
        yield return CuFrames(3);
        Check("an edit elsewhere reaches the open section", s.View.Lines[0].On.value);

        // Add and Open in panel
        yield return CuClick(CuQ("constraint-section-add"), "the section's Add");
        yield return CuFrames(2);
        Check("Add opens the panel's form with this node as the target", ConstraintsUiModule.PanelShown(CuUi) && CuPanel.Editor.Adding && (CuQ("constraints-target-node") as RefPicker)?.Value == cuFolder, (CuQ("constraints-target-node") as RefPicker)?.Value);
        CuPanel.Select(null);
        yield return CuClick(CuQ("constraint-section-open"), "the section's Open in panel");
        yield return CuFrames(2);
        Check("Open in panel shows the panel and the node's first entry", ConstraintsUiModule.PanelShown(CuUi) && CuPanel.Selected == id, CuPanel.Selected);

        // two selected: lists the primary's, edits nothing
        yield return CuOk(new { cmd = "select", keys = new[] { cuFolder, cuItem } }, "select two");
        yield return CuFrames(3);
        CuInsp.Refresh(true);
        yield return CuFrames(3);
        s = CuTree.Q<ConstraintSection>();
        if (s != null && !s.Fold.value) s.Fold.value = true;
        yield return CuFrames(3);
        s?.View?.ReadNow();
        yield return CuFrames(2);
        Check("two selected: the primary's rows, nothing editable, the note", s?.View?.Lines.Count == 2 && !s.View.Lines[0].On.enabledSelf && !s.View.Lines[0].Remove.enabledSelf && CuVisible(CuQ("constraint-section-primary")) && !CuQ("constraint-section-add").enabledSelf, s?.View?.Lines.Count.ToString());
        s?.Fold.SetValueWithoutNotify(true);
        if (s != null) s.Fold.value = false;

        // the Kinematics tab
        yield return CuOk(new { cmd = "select", keys = new[] { cuA } }, "select the character");
        yield return CuFrames(3);
        CuInsp.Refresh(true);
        yield return CuFrames(3);
        CuInsp.Chara.ShowTab("anime");
        yield return CuFrames(2);
        Check("another tab: no section attached", CuTree.Q<ConstraintSection>() == null);
        CuInsp.Chara.ShowTab("kinematics");
        yield return CuFrames(3);
        var cs = CuTree.Q<ConstraintSection>();
        var body = CuInsp.Chara.TabBodies["kinematics"];
        Check("the Kinematics tab ends with the section", cs != null && body.ElementAt(body.childCount - 1) == cs, cs?.parent?.name);
        Check("the strip keeps its 13 buttons", CuQ("tabs")?.childCount == 13, CuQ("tabs")?.childCount.ToString());
        cs.Fold.value = true;
        yield return CuFrames(3);
        cs.View.ReadNow();
        yield return CuFrames(2);
        Check("the character has none: one line", CuVisible(CuQ("constraint-section-none")));
        cs.Fold.value = false;
        yield return CuFrames(2);
        CuInsp.Chara.ShowTab("anime");
        yield return CuFrames(2);
    }

    // ------------------------------------------------------------ CU8

    static int CuSubscribers()
    {
        int n = 0;
        foreach (var f in new[] { "SceneChanged", "SceneCleared", "SceneLoaded", "SelectionChanged", "SubSelectionChanged", "ViewStateChanged", "HistoryChanged" })
        {
            var fi = typeof(UiContext).GetField(f, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            n += (fi?.GetValue(CuUi) as Delegate)?.GetInvocationList().Length ?? 0;
        }
        return n;
    }

    static IEnumerator CU8()
    {
        yield return CuOpenPanel();
        yield return CuOk(new { cmd = "ui.panel", panel = "inspector", op = "show" }, "show the inspector");
        CuAdd(CuNode(cuItem), CuNode(cuFolder), ConstraintComponents.Position);
        yield return CuOk(new { cmd = "select", keys = new[] { cuItem } }, "select the item");
        yield return CuFrames(3);
        CuInsp.Refresh(true);
        yield return CuFrames(3);
        var first = CuTree.Q<ConstraintSection>();
        if (!Check("the section", first != null)) yield break;
        first.Fold.value = true;
        yield return CuFrames(3);
        int ticks0 = -1, subs0 = -1, lang0 = -1, bind0 = -1;
        var dropped = new List<WeakReference>();
        for (int i = 0; i < 20; i++)
        {
            var was = CuTree.Q<ConstraintSectionView>();
            if (was != null && dropped.Count < 2) dropped.Add(new WeakReference(was));
            CuInsp.Refresh(true);
            yield return CuFrames(2);
            if (i != 1) continue;
            ticks0 = PanelTicker.Count;
            subs0 = CuSubscribers();
            lang0 = L10n.Default.LanguageListeners;
        }
        Check("20 inspector rebuilds with the section open: the per-frame actions stand", PanelTicker.Count == ticks0, ticks0 + " -> " + PanelTicker.Count);
        Check("20 inspector rebuilds: the subscribers stand", CuSubscribers() == subs0, subs0 + " -> " + CuSubscribers());
        Check("20 inspector rebuilds: the language's listeners stand", L10n.Default.LanguageListeners == lang0, lang0 + " -> " + L10n.Default.LanguageListeners);
        CuTree.Q<ConstraintSection>().Fold.value = false;
        yield return CuFrames(2);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        yield return CuFrames(2);
        GC.Collect();
        Check("a section dropped by a rebuild is collected", dropped.Count(w => w.IsAlive) == 0, dropped.Count(w => w.IsAlive) + " of " + dropped.Count + " still alive");

        // 20 panel opens, by the action and by moving away
        ticks0 = subs0 = -1;
        for (int i = 0; i < 20; i++)
        {
            ConstraintsUiModule.TogglePanel(CuUi);
            yield return CuFrames(2);
            ConstraintsUiModule.TogglePanel(CuUi);
            yield return CuFrames(2);
            if (i != 1) continue;
            ticks0 = PanelTicker.Count;
            subs0 = CuSubscribers();
        }
        Check("20 panel opens: the per-frame actions stand", PanelTicker.Count == ticks0, ticks0 + " -> " + PanelTicker.Count);
        Check("20 panel opens: the subscribers stand", CuSubscribers() == subs0, subs0 + " -> " + CuSubscribers());
        if (!ConstraintsUiModule.PanelShown(CuUi)) ConstraintsUiModule.TogglePanel(CuUi);
        yield return CuFrames(3);

        // a panel that is hidden reads nothing, but stays attached: a tab of another panel in its region
        TimelineUiModule.EnsurePanel(CuUi);
        yield return CuFrames(2);
        CuUi.Shell.ShowPanel(TimelineUiModule.PanelId);
        yield return CuFrames(3);
        int reads = CuPanel.Reads;
        yield return PlayModeHarness.WaitRealtime(1.2);
        Check("a panel behind another tab reads nothing", CuPanel.Reads == reads, reads + " -> " + CuPanel.Reads);
        ConstraintsUiModule.Show(CuUi);
        yield return CuFrames(3);
        yield return PlayModeHarness.WaitRealtime(1.2);
        int shown = CuPanel.Reads - reads;
        Check("shown again it reads, at most twice a second", shown >= 2 && shown <= 8, shown.ToString());
    }

    // ------------------------------------------------------------ CU9

    static Dictionary<string, Vector2> CuPickPoints()
    {
        var l = new List<Dictionary<string, object>>();
        CuGizmo.BoneHandleStates(l);
        var d = new Dictionary<string, Vector2>();
        foreach (var h in l)
        {
            if ((string)h["kind"] != "bone") continue;
            var p = (double[])h["panel"];
            d[(string)h["sub"]] = new Vector2((float)p[0], (float)p[1]);
        }
        return d;
    }

    static IEnumerator CU9()
    {
        yield return CuOpenPanel();
        var p = CuPanel;
        if (p == null) yield break;
        var ed = p.Editor;
        var ui = CuUi;

        p.BeginAdd();
        ed.PickKind(false, 1);
        ed.PickNode(false, cuA);
        yield return CuFrames(2);
        yield return CuClick(CuQ("constraints-source-bone"), "the bone field");
        yield return CuFrames(3);
        var pop = ConstraintBonePopup.Current;
        if (!Check("the field opens the popup", pop != null && CuVisible(CuQ(ConstraintBonePopup.BoxName)))) yield break;
        Check("it opens on the body, with the parts of the character", pop.Part == AdvancedPoseResolver.BodyPart && pop.PartChoices.Count >= 2 && CuVisible(CuQ("constraints-bone-part")), pop.PartChoices.Count.ToString());
        Check("the search field has the focus", CuPanelRoot.focusController?.focusedElement == CuQ("constraints-bone-search") || CuPanelRoot.focusController?.focusedElement is VisualElement fe && CuQ("constraints-bone-search").Contains(fe));
        int all = pop.AllRows.Count;
        Measure("CU9: " + all + " rows on the body, " + pop.VisibleRows.Count + " listed at first");
        Check("the list is the tree of the part", all > 100 && pop.VisibleRows.Count > 0 && pop.VisibleRows.Count < all, all + " / " + pop.VisibleRows.Count);
        int elements = pop.List.Query<Label>("constraints-bone-text").ToList().Count;
        foreach (var r in pop.AllRows.ToList()) if (r.HasChildren) pop.SetExpanded(r.Address, true);
        yield return CuFrames(3);
        elements = pop.List.Query<Label>("constraints-bone-text").ToList().Count;
        Check("every row open: all of them listed, a screenful of elements", pop.VisibleRows.Count == all && elements > 0 && elements < 40, pop.VisibleRows.Count + " rows, " + elements + " elements");

        // search
        pop.SetQuery("cf_J_Hand_R");
        yield return CuFrames(3);
        var hand = pop.VisibleRows.Select(i => pop.AllRows[i]).FirstOrDefault(r => r.Address == "cf_J_Hand_R");
        Check("a search narrows the list, with the ancestors", hand != null && pop.VisibleRows.Count < all && pop.VisibleRows.Count >= 2, pop.VisibleRows.Count.ToString());

        // Esc with the focus in the search: one Esc closes the popup
        CuEsc();
        yield return CuFrames(3);
        Check("Esc closes the popup, in one press, with the focus in its search", ConstraintBonePopup.Current == null && CuQ(ConstraintBonePopup.BoxName) == null && !ui.Menus.IsOpen);
        Check("nothing was chosen", ed.Adding && CuCount == 0);

        // a click on a row chooses it
        yield return CuClick(CuQ("constraints-source-bone"), "the bone field again");
        yield return CuFrames(3);
        pop = ConstraintBonePopup.Current;
        pop.SetQuery("cf_J_Hand_R");
        yield return CuFrames(3);
        // the list is virtualised: a row out of view has no element
        var listed = pop.VisibleRows.Select(i => pop.AllRows[i].Address).ToList();
        pop.List.ScrollToItem(listed.IndexOf("cf_J_Hand_R"));
        yield return CuFrames(3);
        yield return CuClick(CuQ("constraints-bone-row-cf_J_Hand_R"), "the row of the hand");
        yield return CuFrames(2);
        Check("a click chooses the bone and closes the popup", ConstraintBonePopup.Current == null && (CuQ("constraints-source-bone") as Button)?.text == "cf_J_Hand_R", (CuQ("constraints-source-bone") as Button)?.text);

        // a part
        yield return CuClick(CuQ("constraints-source-bone"), "the bone field");
        yield return CuFrames(3);
        pop = ConstraintBonePopup.Current;
        pop.SetPart(AdvancedPoseResolver.HeadPart);
        yield return CuFrames(3);
        Check("the part switch lists the head", pop.Part == AdvancedPoseResolver.HeadPart && pop.AllRows.Any(r => r.Address == "cf_J_FaceRoot"), pop.AllRows.Count.ToString());
        string hairPart = pop.PartChoices.FirstOrDefault(x => x.StartsWith("hair:", StringComparison.Ordinal));
        if (hairPart != null)
        {
            pop.SetPart(hairPart);
            yield return CuFrames(3);
            Check("a hair part lists addresses of its form", pop.AllRows.Count > 0 && pop.AllRows[0].Address.StartsWith(hairPart + "/", StringComparison.Ordinal), pop.AllRows.FirstOrDefault()?.Address);
        }
        pop.SetPart(AdvancedPoseResolver.BodyPart);
        yield return CuFrames(2);

        // pick in view, on a node that is not selected
        ui.Ops.Select(new[] { cuItem });
        yield return CuOk(new { cmd = "ui.action", action = "tool.select" }, "the Select tool");
        var cc = ui.Viewport.Camera;
        var cv = cc.Current;
        var chest = AdvancedPoseResolver.Resolve(CuApp.Runtime.FindChara(cuA), "cf_J_Spine03").position;
        cc.SetView(chest, new Vector3(cv.Rot.X, cv.Rot.Y, cv.Rot.Z), 30f, cv.Fov);   // a character is some 16 units tall
        cc.Flush();
        yield return CuFrames(4);
        Check("the pick is off: no sphere", !ConstraintBonePick.On && CuPickPoints().Count == 0);
        pop.SetQuery("cf_J_");
        yield return CuFrames(2);
        var toggle = pop.PickToggle;
        toggle.value = true;
        yield return CuFrames(3);
        Check("the toggle turns the pick on, on the popup's node", ConstraintBonePick.On && ConstraintBonePick.Node == cuA && ConstraintBonePick.Rows.Count > 0 && ConstraintBonePick.Rows.Count <= AdvancedPoseTab.MaxPicks, ConstraintBonePick.Rows.Count.ToString());
        Check("the popup stays, the viewport takes the pointer", CuVisible(CuQ(ConstraintBonePopup.BoxName)) && CuQ("menu-blocker")?.resolvedStyle.display == DisplayStyle.None);
        var pts = CuPickPoints();
        Check("gizmo.handles lists the spheres of a node that is not selected", pts.Count > 0 && pts.Keys.All(k => k.StartsWith(cuA + "/bone/", StringComparison.Ordinal)), pts.Count.ToString());
        var box = CuQ(ConstraintBonePopup.BoxName).worldBound;
        var vb = ui.Viewport.Element.worldBound;
        string sub = null; Vector2 tp = default;
        int inView = 0, underBox = 0, hits = 0;
        foreach (var kv in pts)
        {
            if (!vb.Contains(kv.Value)) continue;
            inView++;
            if (box.Contains(kv.Value)) { underBox++; continue; }
            var h = CuGizmo.HitTest(kv.Value);
            if (h.Kind == HandleKind.Sub) hits++;
            if (h.Kind == HandleKind.Sub && h.Sub == kv.Key) { sub = kv.Key; tp = kv.Value; break; }
        }
        Info("CU9 pick: camera " + ui.Viewport.ViewCamera.transform.position + ", viewport " + vb + ", popup " + box + ", first point " + pts.Values.FirstOrDefault() + ", in view " + inView + ", under the popup " + underBox + ", hits " + hits);
        if (!Check("a sphere the hit test finds", sub != null, pts.Count + " points")) ConstraintBonePopup.Current?.Close();
        else
        {
            int u0 = CuUndos;
            var sel = ui.Selection.Primary;
            string subBefore = ui.SubSelection;
            yield return CuCall(new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(CuPointer(tp, "click")), ["frames"] = 2 });
            yield return CuFrames(3);
            string address = sub.Substring((cuA + "/bone/").Length);
            Check("a click takes the bone and closes the popup", ConstraintBonePopup.Current == null && (CuQ("constraints-source-bone") as Button)?.text == address, (CuQ("constraints-source-bone") as Button)?.text + " / " + address);
            Check("the selection stays", ui.Selection.Primary == sel && ui.SubSelection == subBefore, ui.Selection.Primary + " / " + ui.SubSelection);
            Check("the pick ends with the popup", !ConstraintBonePick.On && CuPickPoints().Count == 0);
        }

        // Esc ends the pick; the blocker is back for the next menu
        yield return CuClick(CuQ("constraints-source-bone"), "the bone field");
        yield return CuFrames(3);
        ConstraintBonePopup.Current?.SetPick(true);
        yield return CuFrames(2);
        Check("the pick is on again", ConstraintBonePick.On);
        ConstraintBonePopup.Current?.SetPick(false);
        yield return CuFrames(2);
        Check("off by the toggle: the blocker is back, the popup stays", !ConstraintBonePick.On && CuQ("menu-blocker")?.resolvedStyle.display == DisplayStyle.Flex && ConstraintBonePopup.Current != null);
        ConstraintBonePopup.Current?.SetPick(true);
        yield return CuFrames(2);
        ui.Viewport.Element.Focus();
        yield return CuFrames(1);
        CuEsc();
        yield return CuFrames(3);
        Check("Esc with the focus in the view ends the pick and the popup", ConstraintBonePopup.Current == null && !ConstraintBonePick.On && !ui.Menus.IsOpen);

        // guarded rows: the target's list greys them, the source's does not
        var r0 = CuApp.Runtime.FindChara(cuA);
        var slot = r0.Context.Accessories.FirstOrDefault(x => x != null && x.Object && r0.AccessoryIdOf(x) != null);
        if (Check("an accessory on the target", slot != null))
        {
            string part = PoseAddressGrammar.PartText(PosePart.Accessory, -1, r0.AccessoryIdOf(slot));
            var fixture = new GameObject("cu_part").transform;
            fixture.SetParent(slot.Object.transform, false);
            var member = new GameObject("o_gomu").transform;
            member.SetParent(fixture, false);
            member.gameObject.AddComponent<MeshRenderer>();
            var side = new GameObject("cu_side").transform;
            side.SetParent(slot.Object.transform, false);
            string memberAddress = part + "/o_gomu";
            p.BeginAdd();
            ed.PickNode(false, cuItem);
            ed.PickKind(true, 1);
            ed.PickNode(true, cuA);
            yield return CuFrames(2);
            yield return CuClick(CuQ("constraints-target-bone"), "the target's bone field");
            yield return CuFrames(3);
            pop = ConstraintBonePopup.Current;
            pop.SetPart(part);
            pop.SetQuery("o_gomu");
            yield return CuFrames(3);
            var row = pop.AllRows.FirstOrDefault(x => x.Address == memberAddress);
            var line = CuQ("constraints-bone-row-" + memberAddress);
            Check("the target's list greys a guarded row, with the rule as its tooltip", row != null && row.Guarded && line != null && line.tooltip == L10n.Get("constraints.state.protected") && line.Q<Label>("constraints-bone-text").ClassListContains(InspectorUi.Dim), line?.tooltip);
            Check("a row beside it is not greyed", pop.AllRows.First(x => x.Address == part + "/cu_side").Guarded == false);
            Check("a click on it does nothing", !pop.Choose(row) && ConstraintBonePopup.Current != null && CuCount == 0);
            yield return CuClick(line, "the greyed row");
            Check("a real click neither: the popup stays, nothing is added", ConstraintBonePopup.Current != null && CuCount == 0);
            pop.SetPick(true);
            yield return CuFrames(2);
            Check("the pick offers no guarded bone", ConstraintBonePick.Rows.All(b => b.Address != memberAddress));
            pop.Close();
            yield return CuFrames(2);
            // a guarded address given all the same is refused, under the pickers
            ed.PickBone(true, memberAddress);
            yield return CuReadAll();
            Check("a guarded bone chosen by another way: refused, the rule under the pickers", CuCount == 0 && ed.Adding && ed.ErrorText == L10n.Get("constraints.state.protected"), ed.ErrorText);
            Check("the operation keeps its text", ed.LastResult.Message == AdvancedPoseGuard.Refusal, ed.LastResult.Message);
            p.BeginAdd();
            ed.PickKind(false, 1);
            ed.PickNode(false, cuA);
            yield return CuFrames(2);
            yield return CuClick(CuQ("constraints-source-bone"), "the source's bone field");
            yield return CuFrames(3);
            pop = ConstraintBonePopup.Current;
            pop.SetPart(part);
            yield return CuFrames(2);
            Check("the source's list greys nothing: any bone can be read", pop.AllRows.Count >= 2 && pop.AllRows.All(x => !x.Guarded), pop.AllRows.Count.ToString());
            pop.Close();
            yield return CuFrames(2);
            UnityEngine.Object.DestroyImmediate(fixture.gameObject);
            UnityEngine.Object.DestroyImmediate(side.gameObject);
        }

        // an item: the names of its transforms
        p.BeginAdd();
        ed.PickKind(false, 1);
        ed.PickNode(false, cuItem);
        yield return CuFrames(2);
        yield return CuClick(CuQ("constraints-source-bone"), "the item's bone field");
        yield return CuFrames(3);
        pop = ConstraintBonePopup.Current;
        Check("an item: no part, the names of its transforms", pop != null && !CuVisible(CuQ("constraints-bone-part")) && pop.AllRows.Count >= 1, pop?.AllRows.Count.ToString());
        if (pop != null)
        {
            var it = CuApp.Runtime.FindItem(cuItem);
            Check("each row's address finds its transform", pop.AllRows.All(x => ConstraintResolver.ItemBone(it, x.Address) == x.Bone), pop.AllRows.FirstOrDefault(x => ConstraintResolver.ItemBone(it, x.Address) != x.Bone)?.Address);
            var first = pop.AllRows[pop.AllRows.Count - 1];
            pop.Choose(first);
            yield return CuFrames(2);
            ed.PickNode(true, cuFolder);
            yield return CuReadAll();
            Check("an item's bone as a source: the entry", CuCount == 1 && CuList.Entries[0].Source.Bone == first.Address && CuPanel.Rows[0].State == ConstraintStates.Active, ed.ErrorText);
        }
        yield return CuOk(new { cmd = "ui.action", action = "tool.move" }, "the Move tool");
    }

    // ------------------------------------------------------------ CU10

    static IEnumerable<string> CuTexts(VisualElement root)
    {
        foreach (var e in root.Query<VisualElement>().ToList())
        {
            if (!string.IsNullOrEmpty(e.tooltip)) yield return e.tooltip;
            if (e is TextElement t && !string.IsNullOrEmpty(t.text)) yield return t.text;
            if (e is Foldout f && !string.IsNullOrEmpty(f.text)) yield return f.text;
        }
    }

    static IEnumerator CU10()
    {
        yield return CuOpenPanel();
        var p = CuPanel;
        if (p == null) yield break;
        string id = CuAdd(CuNode(cuItem), CuNode(cuFolder), ConstraintComponents.Position);
        yield return CuReadAll();
        p.Select(id);
        yield return CuOk(new { cmd = "ui.panel", panel = "inspector", op = "show" }, "show the inspector");
        yield return CuOk(new { cmd = "select", keys = new[] { cuFolder } }, "select the folder");
        yield return CuFrames(3);
        var host = UiHost.Current;
        var want = new Dictionary<string, string> { ["en"] = "Constraints", ["ja"] = "拘束", ["zh-Hans"] = "约束" };
        foreach (var lang in want.Keys)
        {
            host.SetLanguage(lang, save: false);
            yield return CuFrames(4);
            CuInsp.Refresh(true);
            yield return CuFrames(4);
            var fold = CuQ(ConstraintSection.FoldName) as Foldout;
            Check(lang + ": the section's title", fold?.text == want[lang], fold?.text);
            var tab = CuQ("tab-" + ConstraintsUiModule.PanelId);
            Check(lang + ": the panel's tab", tab != null && CuTexts(tab).Contains(want[lang]), tab == null ? "none" : string.Join(" | ", CuTexts(tab)));
            CuPanel.ReadNow();
            yield return CuFrames(2);
            var ed = new List<string>();
            ed.AddRange(CuTexts(CuPanel));
            if (fold != null) ed.AddRange(CuTexts(fold));
            Check(lang + ": no text of the panel or the section names a community plugin", ed.Count > 10 && !ed.Any(HS2Next.UI.Core.Tests.SchemaNames.Names), ed.Count + " texts");
            Check(lang + ": the editor's text follows the language", (CuQ("constraints-take-offset") as Button)?.text == L10n.Get("constraints.takeOffset"), (CuQ("constraints-take-offset") as Button)?.text);

            // a refusal under the pickers stands in the UI's language, with the rows; the operation keeps its English
            var form = CuPanel.Editor;
            CuPanel.BeginAdd();
            form.PickNode(false, cuFolder);
            form.PickNode(true, cuItem);
            yield return CuFrames(2);
            string head = L10n.Get("constraints.cycleRefused").Split(new[] { "{0}" }, StringSplitOptions.None)[0];
            Check(lang + ": a loop's refusal in this language, with the rows", form.ErrorText.StartsWith(head, StringComparison.Ordinal) && form.ErrorText.Contains(" -> "), form.ErrorText);
            Check(lang + ": the operation's text stays English", form.LastResult.Message.StartsWith("constraint cycle: ", StringComparison.Ordinal), form.LastResult.Message);
            CuPanel.Select(id);
            yield return CuFrames(2);
        }
        host.SetLanguage("en", save: false);
        yield return CuFrames(3);
        foreach (var f in new[] { "Reference/COMMANDS.md", "Reference/PLUGINS.md", "Reference/PLUGINS_ja.md", "Reference/PLUGINS_zh-Hans.md", "Assets/HS2Next/Resources/UI/L10n/strings.tsv" })
        {
            string path = Path.Combine(ContentSettings.ProjectRoot, f);
            if (!Check(f + " is there", File.Exists(path))) continue;
            Check(f + " names no community plugin", !HS2Next.UI.Core.Tests.SchemaNames.Names(File.ReadAllText(path)));
        }
    }

    // ------------------------------------------------------------ CU11

    static IEnumerator CU11()
    {
        yield return CuOpenPanel();
        var p = CuPanel;
        if (p == null) yield break;
        var ui = CuUi;
        var sh = ui.Shell;
        var host = UiHost.Current;
        string id = CuAdd(new ConstraintEnd(cuA, "cf_J_Hand_R"), CuNode(cuFolder), ConstraintComponents.Position, ConstraintComponents.Rotation);
        CuAdd(CuNode(cuItem), CuNode(cuItem2), ConstraintComponents.Scale);
        sh.MovePanel(ConstraintsUiModule.PanelId, LayoutRegions.Left);
        sh.ShowPanel(ConstraintsUiModule.PanelId);
        yield return CuFrames(4);
        p = CuPanel;
        var left = sh.Regions[LayoutRegions.Left];
        foreach (float width in new[] { 360f, 300f })
        {
            sh.SetSize(left, width);
            yield return CuFrames(4);
            foreach (var lang in new[] { "en", "ja", "zh-Hans" })
            {
                host.SetLanguage(lang, save: false);
                yield return CuFrames(4);
                string tag = width.ToString("0") + " px " + lang;
                sh.ShowPanel(ConstraintsUiModule.PanelId);
                yield return CuFrames(3);
                CuPanel.ReadNow();
                CuPanel.Select(id);
                yield return CuFrames(4);
                var root = CuPanel.worldBound;
                Check(tag + ": the panel fits its region", root.width > 100 && root.width <= width + 0.5f, root.ToString());
                var sp = CuPanel.Q<AdaptiveSplit>();
                Check(tag + ": the panes stand one above the other", sp != null && sp.Stacked);
                var line = CuLine(0);
                var hidden = new List<string>();
                foreach (var e in new VisualElement[] { line?.On, line?.Text, line?.Weight, line?.Up, line?.Down, line?.Remove, CuQ("constraints-add"), CuQ("constraints-clear") })
                {
                    var b = e?.worldBound ?? default;
                    if (e == null || b.width < 4 || b.xMin < root.xMin - 0.5f || b.xMax > root.xMax + 0.5f) hidden.Add(e?.name ?? "?");
                }
                Check(tag + ": the row's controls and the bar lie inside the panel", hidden.Count == 0, string.Join(", ", hidden));
                var outside = new List<string>();
                foreach (var name in new[] { "constraints-source-node", "constraints-source-use", "constraints-source-bone", "constraints-target-node", "constraints-target-use", "constraints-comp-position", "constraints-comp-parent", "constraints-offset-pos", "constraints-take-offset", "constraints-clear-offset", "constraints-weight", "constraints-label" })
                {
                    var e = CuQ(name);
                    yield return CuReveal(e);
                    var b = e?.worldBound ?? default;
                    var editor = CuPanel.Editor.worldBound;
                    bool inside = e != null && b.width >= 4 && b.xMin >= editor.xMin - 0.5f && b.xMax <= editor.xMax + 0.5f && b.yMin >= editor.yMin - 1f && b.yMin < editor.yMax;
                    var at = new Vector2(b.xMin + Mathf.Min(6, b.width / 2), b.yMin + Mathf.Min(6, b.height / 2));
                    var hit = inside ? CuPanelRoot.Pick(at) as VisualElement : null;
                    if (!inside || hit == null || !(hit == e || e.Contains(hit))) outside.Add(name + " " + b + " in " + editor + " hit " + CuName(hit));
                }
                Check(tag + ": every row of the editor can be scrolled to and hit", outside.Count == 0, string.Join("; ", outside));

                // the section
                sh.ShowPanel("inspector");
                yield return CuOk(new { cmd = "select", keys = new[] { cuFolder } }, "select the folder");
                yield return CuFrames(3);
                CuInsp.Refresh(true);
                yield return CuFrames(3);
                var sec = CuTree.Q<ConstraintSection>();
                if (sec != null && !sec.Fold.value) sec.Fold.value = true;
                yield return CuFrames(3);
                sec?.View?.ReadNow();
                yield return CuFrames(3);
                var sl = sec?.View?.Lines.FirstOrDefault();
                var sb = CuInsp.worldBound;
                var missing = new List<string>();
                foreach (var e in new VisualElement[] { sl?.On, sl?.Text, sl?.Weight, sl?.Remove, CuQ("constraint-section-add"), CuQ("constraint-section-open") })
                {
                    yield return CuReveal(e);
                    var b = e?.worldBound ?? default;
                    if (e == null || b.width < 4 || b.xMin < sb.xMin - 0.5f || b.xMax > sb.xMax + 0.5f) missing.Add(e?.name ?? "?");
                }
                Check(tag + ": the section's row and buttons lie inside the inspector", missing.Count == 0, string.Join(", ", missing));
                if (sec != null) sec.Fold.value = false;
                yield return CuFrames(2);
            }
        }
        host.SetLanguage("en", save: false);
        sh.SetSize(left, 360f);
        sh.MovePanel(ConstraintsUiModule.PanelId, LayoutRegions.Bottom);
        yield return CuFrames(4);
        sh.ResetLayout();
        yield return CuFrames(3);
    }

    // ------------------------------------------------------------ CD4

    static void CuBuildGizmo()
    {
        var cam = CuUi.Viewport.ViewCamera;
        if (cam && UiHost.Current.GizmoPass != null) CuGizmo.Build(UiHost.Current.GizmoPass, cam);
    }

    static IEnumerator CuCentre(float distance = 6f)
    {
        yield return CuFrames(2);
        CuBuildGizmo();
        var cc = CuUi.Viewport.Camera;
        var cv = cc.Current;
        cc.SetView(CuGizmo.PivotWorld, new Vector3(cv.Rot.X, cv.Rot.Y, cv.Rot.Z), distance, cv.Fov);
        cc.Flush();
        yield return CuFrames(4);
        CuBuildGizmo();
    }

    static bool CuAxisGrab(HandleKind kind, out Vector2 p, out Vector2 dir)
    {
        var gz = CuGizmo;
        var vb = CuUi.Viewport.Element.worldBound;
        Vector2 c = gz.ToPanel(gz.PivotWorld);
        p = default; dir = default;
        for (int i = 0; i < 3; i++)
        {
            var a = gz.Axes * (i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward);
            foreach (float f in new[] { 0.9f, 0.75f, 0.6f, 0.45f, 0.3f })
            {
                Vector2 q = gz.ToPanel(gz.PivotWorld + a * (gz.LengthWorld * f));
                var h = gz.HitTest(q);
                if (h.Kind != kind || h.Axis != i || !vb.Contains(q) || (q - c).magnitude < 12) continue;
                p = q; dir = (q - c).normalized;
                return true;
            }
        }
        return false;
    }

    static bool CuRingGrab(out Vector2 p, out Vector2 dir)
    {
        var gz = CuGizmo;
        var vb = CuUi.Viewport.Element.worldBound;
        p = default; dir = default;
        for (int i = 0; i < 3; i++)
        {
            var n = i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;
            var u = i == 0 ? Vector3.up : Vector3.right;
            var v = Vector3.Cross(n, u);
            for (int s = 0; s < 32; s++)
            {
                float ang = s * Mathf.PI * 2 / 32;
                var w0 = gz.PivotWorld + gz.Axes * (u * Mathf.Cos(ang) + v * Mathf.Sin(ang)) * gz.LengthWorld;
                var w1 = gz.PivotWorld + gz.Axes * (u * Mathf.Cos(ang + 0.1f) + v * Mathf.Sin(ang + 0.1f)) * gz.LengthWorld;
                Vector2 q = gz.ToPanel(w0), q1 = gz.ToPanel(w1);
                var h = gz.HitTest(q);
                if (h.Kind != HandleKind.RotateRing || h.Axis != i || !vb.Contains(q) || (q1 - q).magnitude < 1) continue;
                p = q; dir = (q1 - q).normalized;
                return true;
            }
        }
        return false;
    }

    /// <summary>A drag on the shown handle; refused: no drag, no entry, the toast; else one entry.</summary>
    static IEnumerator CuTryDrag(string what, HandleKind kind, bool refused, string label, Func<string> read)
    {
        CuBuildGizmo();
        Vector2 p, d;
        bool grabbed = kind == HandleKind.RotateRing ? CuRingGrab(out p, out d) : CuAxisGrab(kind, out p, out d);
        if (!Check(what + ": the handle can be grabbed", grabbed, "tool " + CuGizmo.ShownTool + ", pivot " + (Vector2)CuGizmo.ToPanel(CuGizmo.PivotWorld))) yield break;
        string v0 = read();
        int u0 = CuUndos;
        yield return CuCall(new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(CuPointer(p, "down"), CuPointer(p + d * 15, "move"), CuPointer(p + d * 30, "move"), CuPointer(p + d * 30, "up")), ["frames"] = 1 });
        yield return CuFrames(3);
        if (refused)
        {
            Check(what + ": refused, nothing changes, no entry", read() == v0 && CuUndos == u0 && !CuGizmo.Dragging, read() + " / " + v0 + ", entries " + (CuUndos - u0));
            yield return CuCall(new { cmd = "ui.state" });
            Check(what + ": the toast", (pLast["toasts"]?.ToString() ?? "").Contains(L10n.Get("toast.constraintDriven")), pLast["toasts"]?.ToString(Formatting.None));
        }
        else
        {
            Check(what + ": the drag works, one entry", read() != v0 && CuUndos == u0 + 1 && CuTop == label, read() + " / " + v0 + ", " + (CuUndos - u0) + " / " + CuTop);
            CuUi.Session.Commands.Undo();
            CuUi.Runtime?.ApplyNow();
            yield return CuFrames(3);
            Check(what + ": undo", read() == v0 && CuUndos == u0, read());
        }
    }

    static string CuVec(object v) => v?.ToString();

    static IEnumerator CD4()
    {
        yield return CuOpenPanel();
        var ui = CuUi;
        var ops = ui.Ops;
        Func<string, Func<string>> tr = path => () => CuVec(FieldAccessor.Get(ui.Model, cuItem, path));

        // a node: position driven, rotation free
        string id = CuAdd(CuNode(cuFolder), CuNode(cuItem), ConstraintComponents.Position);
        ops.Select(new[] { cuItem });
        ui.Tool = GizmoTool.Move;
        yield return CuCentre();
        yield return CuTryDrag("a node's position", HandleKind.MoveAxis, true, null, tr("transform.pos"));
        ui.Tool = GizmoTool.Rotate;
        yield return CuFrames(2);
        yield return CuTryDrag("the free channel of the node", HandleKind.RotateRing, false, OpLabels.Transform, tr("transform.rot"));
        ui.Tool = GizmoTool.Scale;
        yield return CuFrames(2);
        yield return CuTryDrag("the scale of the node", HandleKind.ScaleAxis, false, OpLabels.Transform, tr("transform.scale"));

        // at any weight above 0
        ui.Tool = GizmoTool.Move;
        ops.SetConstraint(id, new ConstraintEdit { Weight = 0.25f }, UiContext.User());
        ui.Runtime.ApplyNow();
        yield return CuFrames(3);
        yield return CuTryDrag("weight 0.25", HandleKind.MoveAxis, true, null, tr("transform.pos"));
        ops.SetConstraint(id, new ConstraintEdit { On = false }, UiContext.User());
        ui.Runtime.ApplyNow();
        yield return CuFrames(3);
        yield return CuTryDrag("switched off", HandleKind.MoveAxis, false, OpLabels.Transform, tr("transform.pos"));
        ops.SetConstraint(id, new ConstraintEdit { On = true, Weight = 1f }, UiContext.User());
        ui.Runtime.ApplyNow();
        yield return CuFrames(3);

        // a source works, and the target follows in the same frame
        ops.Select(new[] { cuFolder });
        yield return CuCentre();
        var before = CuApp.Runtime.HolderOf(cuItem).position;
        Func<string> fp = () => CuVec(FieldAccessor.Get(ui.Model, cuFolder, "transform.pos"));
        string v0 = fp();
        int u0 = CuUndos;
        CuBuildGizmo();
        if (Check("the source's handle", CuAxisGrab(HandleKind.MoveAxis, out var p, out var d)))
        {
            yield return CuCall(new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(CuPointer(p, "down"), CuPointer(p + d * 15, "move"), CuPointer(p + d * 30, "move"), CuPointer(p + d * 30, "up")), ["frames"] = 1 });
            yield return CuFrames(3);
            Check("a drag of a source works, one entry", fp() != v0 && CuUndos == u0 + 1 && CuTop == OpLabels.Transform, fp() + ", " + (CuUndos - u0));
            Check("the target followed", (CuApp.Runtime.HolderOf(cuItem).position - before).magnitude > 1e-4f, (CuApp.Runtime.HolderOf(cuItem).position - before).magnitude.ToString("G4"));
            yield return CuUndoTo(u0);
        }
        ops.RemoveConstraint(id, UiContext.User());
        ui.Runtime.ApplyNow();

        // an IK target: position driven, rotation free; the handle stands where the solve takes it
        yield return CuOk(new { cmd = "ik.enable", key = cuA, on = true }, "IK on");
        yield return CuFrames(5);
        string ik = CuAdd(CuNode(cuFolder), new ConstraintEnd(cuA, null, "LeftHand"), ConstraintComponents.Position);
        ui.Runtime.ApplyNow();
        yield return CuFrames(3);
        yield return CuOk(new { cmd = "select", sub = cuA + "/ik/LeftHand" }, "select the IK handle");
        ui.Tool = GizmoTool.Move;
        yield return CuCentre();
        var driver = IkDriver.Of(CuApp.Runtime.FindChara(cuA));
        var root = CuApp.Runtime.FindChara(cuA).Root.transform;
        var shown = root.TransformPoint(driver.Shown(HS2Next.Motion.Ik.IkTargetId.LeftHand).Pos);
        Check("the handle stands where the solve takes the target", (CuGizmo.PivotWorld - shown).magnitude < 1e-4f, (CuGizmo.PivotWorld - shown).magnitude.ToString("G4"));
        Func<string> eff = () => { var e = driver.Effective(HS2Next.Motion.Ik.IkTargetId.LeftHand); return e.Pos + " " + e.Rot; };
        yield return CuTryDrag("an IK target's position", HandleKind.MoveAxis, true, null, eff);
        ui.Tool = GizmoTool.Rotate;
        yield return CuFrames(2);
        yield return CuTryDrag("the IK target's free rotation", HandleKind.RotateRing, false, OpLabels.IkTargets, eff);
        ui.Tool = GizmoTool.Move;
        ops.RemoveConstraint(ik, UiContext.User());
        ui.Runtime.ApplyNow();
        yield return CuOk(new { cmd = "ik.enable", key = cuA, on = false }, "IK off");

        // an FK handle: rotation driven; the bone's own handle stays free
        const string arm = "cf_J_ArmUp00_R";
        yield return CuOk(new { cmd = "select", keys = new[] { cuA } }, "select the character");
        yield return CuOk(new { cmd = "fk.enable", key = cuA, on = true }, "FK on");
        yield return CuOk(new { cmd = "fk.enable", key = cuA, groups = new[] { "Body" }, on = true }, "FK group Body");
        yield return CuFrames(4);
        string fk = CuAdd(CuNode(cuFolder), new ConstraintEnd(cuA, arm), ConstraintComponents.Rotation);
        yield return CuFrames(3);
        yield return CuOk(new { cmd = "select", sub = cuA + "/fk/" + arm }, "select the FK handle");
        ui.Tool = GizmoTool.Rotate;
        yield return CuCentre();
        Func<string> fkv = () => (ui.Model.Find(cuA).State as CharaNodeState).Fk.Bones.TryGetValue(arm, out var v) ? v.ToString() : "none";
        yield return CuTryDrag("an FK bone's rotation", HandleKind.RotateRing, true, null, fkv);
        yield return CuOk(new { cmd = "select", sub = cuA + "/bone/" + arm }, "select the bone's own handle");
        yield return CuCentre();
        Func<string> cor = () => (ui.Model.Find(cuA).State as CharaNodeState)?.AdvancedPose?.Bones.TryGetValue(arm, out var c) == true ? c.Rot.ToString() : "none";
        yield return CuTryDrag("the Advanced pose handle on a driven bone: allowed", HandleKind.RotateRing, false, OpLabels.BoneCorrection, cor);
        ops.RemoveConstraint(fk, UiContext.User());
        ui.Runtime.ApplyNow();
        yield return CuFrames(3);
        yield return CuOk(new { cmd = "select", sub = cuA + "/fk/" + arm }, "select the FK handle");
        yield return CuCentre();
        yield return CuTryDrag("the FK bone without the constraint", HandleKind.RotateRing, false, OpLabels.FkBones, fkv);
        yield return CuOk(new { cmd = "fk.enable", key = cuA, on = false }, "FK off");
        yield return CuOk(new { cmd = "select", keys = new[] { cuA } }, "select the character alone");
        ui.Tool = GizmoTool.Move;
        yield return CuFrames(2);
    }
}
