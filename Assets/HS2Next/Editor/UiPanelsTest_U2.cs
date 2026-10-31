// UiPanelsTest.play group U2G (v0.3 trial 2, package U2): the Maker lists' cell states and thumbnail size, in the app with the UI
// in Editor play mode (same harness and Ctx as UiPanelsTest, helpers of the MF / MH / MC groups):
//   (2) per page kind — the face (eyebrow), body (skin), hair (back style), clothes (top) and accessory (the «＋» panel's
//     list) lists: the size slider (`partlist-zoom`) shown with the kind's option value, a slider move changes the cell width
//     and the view-state option view.thumbSize.<kind>, another list of the kind follows (face: the skin list of the head page),
//     the size kept when the page is shown again, the option's reset gives 72 px;
//   (1) the face eyebrow and the clothes top lists through synthesised pointer and key events: the applied cell carries the
//     current class, its 3 px accent frame and the check badge; hover and pressed classes; a click applies the row (one entry
//     "Change item"; the clicked cell becomes the applied one, the old one loses the mark, the list keeps the keyboard focus);
//     the arrow moves only the selection (selected, not applied: no check), Enter applies it; a double click on a row is one
//     entry; every cell's tooltip ends with the hint (what a click / Enter does); undo restores the items.
// Characters: the SFW female target of Tools/targets.json; thumbnails are drawn and counted, never viewed.
// Run alone: & Tools\unity_batch.ps1 -Method UiPanelsTest.Run -NoQuit -MethodArgs @('-only','U2G')
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.UI;
using HS2Next.UI.Bridge;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class UiPanelsTest
{
    [UnityEditor.InitializeOnLoadMethod]
    static void RegisterU2Group()
    {
        if (!Groups.Any(g => g.id == "U2G")) Groups.Add(("U2G", U2MakerGridsGroup));
    }

    static void U2Ev(Ctx c, object o) => UiCommands.Send(c.Host, JObject.FromObject(o), out _);

    static bool U2Shown(VisualElement e)
    {
        for (var x = e; x != null; x = x.hierarchy.parent)
            if (x.resolvedStyle.display == DisplayStyle.None) return false;
        return e != null && e.panel != null;
    }

    /// <summary>A cell's state: current / selected / check shown / frame width.</summary>
    static string U2State(VisualElement cell)
    {
        if (cell == null) return "no cell";
        var check = cell.Q(className: ThumbGrid<PartItem>.CheckClass);
        var frame = cell.Q(className: ThumbGrid<PartItem>.FrameClass);
        return $"{cell.name}: current {cell.ClassListContains(ThumbGrid<PartItem>.CurrentClass)}, selected {cell.ClassListContains(ThumbGrid<PartItem>.SelectedClass)}, check {(check != null && check.resolvedStyle.display == DisplayStyle.Flex ? "shown" : "hidden")}, frame {(frame != null ? frame.resolvedStyle.borderTopWidth : -1):0.#} px";
    }

    static bool U2Applied(VisualElement cell)
    {
        var check = cell?.Q(className: ThumbGrid<PartItem>.CheckClass);
        var frame = cell?.Q(className: ThumbGrid<PartItem>.FrameClass);
        return cell != null && cell.ClassListContains(ThumbGrid<PartItem>.CurrentClass) && check != null && check.resolvedStyle.display == DisplayStyle.Flex
            && frame != null && frame.resolvedStyle.borderTopWidth >= 2.5f;
    }

    /// <summary>The bound cell of position <paramref name="pos"/> (scrolled into view first).</summary>
    static IEnumerator U2Cell(PartList list, int pos, VisualElement[] into)
    {
        into[0] = list.Grid.CellAt(pos);
        if (into[0] != null && into[0].worldBound.height > 0) yield break;
        list.Grid.ScrollToPos(pos);
        yield return PlayModeHarness.WaitFrames(3);
        into[0] = list.Grid.CellAt(pos);
    }

    static IEnumerator U2MakerGridsGroup(Ctx c)
    {
        var ui = c.Ui;
        // the view state this group changes (the pages and parts shown) is put back at its end: later groups check it
        var lastPages = new Dictionary<string, string>(CharaEditTab.LastPage);
        int clothesPart = PartSelectorState.Clothes.Part, hairPart = PartSelectorState.Hair.Part;
        PartSelectorState.Clothes.Part = 0;
        PartSelectorState.Hair.Part = 0;
        yield return NewScene(c);
        var ids = new List<string>();
        yield return AddBuiltChara(c, c.FemaleCard, ids);
        if (ids.Count < 1) yield break;
        string f = ids[0];
        var tab = MfOpenEdit(c, f);
        yield return PlayModeHarness.WaitFrames(4);
        c.Check(tab != null && tab.panel != null, "the female's Edit tab is shown");
        if (tab == null) yield break;
        foreach (var k in OptionDefs.MakerThumbKinds) ui.Options.Set(OptionDefs.ThumbSizeKey(k), null);

        // ---------------------------------------------------------------- (2) the size slider per page kind
        var kinds = new (string kind, Func<CharaEditTab, PartList> show)[]
        {
            ("face", t => { t.Reveal("face", "eyebrow"); return MfQ<PartList>(t, "field-appearance.chara.face.eyebrow.item"); }),
            ("body", t => { t.Reveal("body", "skin"); return MfQ<PartList>(t, "field-appearance.chara.body.skin.item"); }),
            ("hair", t => { t.ShowNode("hair"); return MfQ<PartList>(t, "field-appearance.chara.hair." + InspectorSchemas.HairParts[0] + ".item"); }),
            ("clothes", t => { t.ShowNode("clothes"); return MfQ<PartList>(t, "field-appearance.coord.clothes." + InspectorSchemas.ClothesEditKinds[0] + ".item"); }),
            ("accessories", t => { t.ShowNode("accessories"); t.OpenAccessoryAdd(true); return t.ShowAccessoryAddList(351); }),
        };
        foreach (var (kind, show) in kinds)
        {
            var list = show(tab);
            yield return PlayModeHarness.WaitFrames(4);
            yield return MhSettle(c, tab);
            string key = OptionDefs.ThumbSizeKey(kind);
            c.Check(list != null && list.GridKind == kind && list.ZoomSlider != null && U2Shown(list.ZoomSlider) && Mathf.Abs(list.ZoomSlider.value - OptionDefs.MakerThumbDefault) < 0.5f && Mathf.Abs(list.Grid.CellWidth - OptionDefs.MakerThumbDefault) < 0.5f,
                $"U2-2 {kind}: the list ({list?.name}) has the size slider at the option's {OptionDefs.MakerThumbDefault} px (slider {list?.ZoomSlider?.value:0}, cells {list?.Grid.CellWidth:0} px, kind {list?.GridKind})");
            if (list == null) continue;
            int cols0 = list.Grid.Columns;
            float h0 = list.Grid.resolvedStyle.height;
            list.ZoomSlider.value = 100;
            yield return PlayModeHarness.WaitFrames(4);
            // a bound cell (the list shows its applied cell, not necessarily the first)
            var cell = Enumerable.Range(0, list.Items.Count).Select(i => list.Grid.CellAt(i)).FirstOrDefault(x => x != null && x.worldBound.width > 0);
            c.Check(Mathf.Abs(list.Grid.CellWidth - 100) < 0.5f && cell != null && Mathf.Abs(cell.resolvedStyle.width - 100) < 0.5f && ui.Options.Int(key) == 100,
                $"U2-2 {kind}: the slider sets the cells to 100 px (cell {cell?.resolvedStyle.width:0} px; columns {cols0} → {list.Grid.Columns}; list height {h0:0} → {list.Grid.resolvedStyle.height:0} px) and {key} = {ui.Options.Int(key)}");
            if (kind == "face")
            {
                tab.Reveal("face", "head");
                yield return PlayModeHarness.WaitFrames(4);
                var skin = MfQ<PartList>(tab, "field-appearance.chara.face.skin");
                c.Check(skin != null && Mathf.Abs(skin.Grid.CellWidth - 100) < 0.5f && Mathf.Abs(skin.ZoomSlider.value - 100) < 0.5f,
                    $"U2-2 face: another list of the kind (the skin list) follows ({skin?.Grid.CellWidth:0} px)");
                tab.Reveal("face", "eyebrow");
                yield return PlayModeHarness.WaitFrames(4);
                c.Check(Mathf.Abs(list.Grid.CellWidth - 100) < 0.5f && Mathf.Abs(list.ZoomSlider.value - 100) < 0.5f, $"U2-2 face: the size is kept when the page is shown again ({list.Grid.CellWidth:0} px)");
            }
            ui.Options.Set(key, null);
            yield return PlayModeHarness.WaitFrames(3);
            c.Check(Mathf.Abs(list.Grid.CellWidth - OptionDefs.MakerThumbDefault) < 0.5f && Mathf.Abs(list.ZoomSlider.value - OptionDefs.MakerThumbDefault) < 0.5f,
                $"U2-2 {kind}: the option's reset gives {OptionDefs.MakerThumbDefault} px again ({list.Grid.CellWidth:0})");
            if (kind == "accessories") tab.OpenAccessoryAdd(false);
        }

        // ---------------------------------------------------------------- (1) the cell states of two lists
        var cellBox = new VisualElement[1];
        foreach (var (what, show, member) in new (string, Func<CharaEditTab, PartList>, string)[]
        {
            ("face eyebrow", kinds[0].show, "appearance.chara.face.eyebrow.item"),
            ("clothes top", kinds[3].show, "appearance.coord.clothes." + InspectorSchemas.ClothesEditKinds[0] + ".item"),
        })
        {
            var list = show(tab);
            yield return PlayModeHarness.WaitFrames(4);
            yield return MhSettle(c, tab);
            if (list == null) { c.Check(false, "U2-1 " + what + ": list shown"); continue; }
            int n0 = UndoCount(c);
            string cur0 = list.CurrentRef;
            int pCur = list.PositionOf(cur0);
            yield return U2Cell(list, Math.Max(0, pCur), cellBox);
            var curCell = cellBox[0];
            c.Check(pCur >= 0 && U2Applied(curCell) && list.Grid.Hint.Length > 0 && curCell.tooltip != null && curCell.tooltip.EndsWith(list.Grid.Hint, StringComparison.Ordinal),
                $"U2-1 {what}: the applied item ({cur0}) is marked — {U2State(curCell)}; its tooltip ends with the hint '{list.Grid.Hint}'");
            // a row that is not applied, next to the applied one when possible
            int p = Enumerable.Range(0, list.Items.Count).Where(i => i != pCur && !list.Items[i].NotListed && list.Items[i].Row != null).OrderBy(i => Math.Abs(i - pCur)).FirstOrDefault();
            string target = list.Items[p].Ref;
            yield return U2Cell(list, p, cellBox);
            var cell = cellBox[0];
            if (cell == null) { c.Check(false, "U2-1 " + what + ": cell " + p + " bound"); continue; }
            var at = cell.worldBound.center;
            U2Ev(c, new { type = "pointer", phase = "move", x = at.x, y = at.y, button = 0 });
            yield return PlayModeHarness.WaitFrames(1);
            bool hover = cell.ClassListContains(ThumbGrid<PartItem>.HoverClass);
            U2Ev(c, new { type = "pointer", phase = "down", x = at.x, y = at.y, button = 0, clicks = 1 });
            yield return PlayModeHarness.WaitFrames(1);
            bool pressed = cell.ClassListContains(ThumbGrid<PartItem>.PressedClass);
            U2Ev(c, new { type = "pointer", phase = "up", x = at.x, y = at.y, button = 0, clicks = 1 });
            var ok = new bool[1];
            yield return WaitFor(() => list.CurrentRef == target && list.PendingRef == null, 30, ok);
            yield return PlayModeHarness.WaitFrames(3);
            yield return U2Cell(list, p, cellBox);
            cell = cellBox[0];
            c.Check(hover && pressed && cell != null && !cell.ClassListContains(ThumbGrid<PartItem>.PressedClass), $"U2-1 {what}: hover and pressed marks on the cell under the pointer (hover {hover}, pressed {pressed}, released)");
            c.Check(ok[0] && UndoCount(c) == n0 + 1 && c.UndoLabel == OpLabels.ChangeItem && U2Applied(cell) && cell.ClassListContains(ThumbGrid<PartItem>.SelectedClass) && list.Grid.HasKeyboardFocus,
                $"U2-1 {what}: a click applies {target} (one entry {c.UndoLabel}) and the clicked cell shows it — {U2State(cell)}; keyboard focus {list.Grid.HasKeyboardFocus}");
            yield return U2Cell(list, pCur, cellBox);
            c.Check(cellBox[0] != null && !cellBox[0].ClassListContains(ThumbGrid<PartItem>.CurrentClass), $"U2-1 {what}: the previously applied cell lost the mark — {U2State(cellBox[0])}");
            // the arrow moves only the selection; Enter applies it
            int n1 = UndoCount(c);
            int pNext = p + 1 < list.Items.Count && !list.Items[p + 1].NotListed ? p + 1 : p - 1;
            list.Grid.Focus();
            U2Ev(c, new { type = "key", key = pNext > p ? "RightArrow" : "LeftArrow" });
            yield return PlayModeHarness.WaitFrames(2);
            yield return U2Cell(list, pNext, cellBox);
            var nextCell = cellBox[0];
            c.Check(list.Grid.SelectedPos == pNext && nextCell != null && nextCell.ClassListContains(ThumbGrid<PartItem>.SelectedClass) && !U2Applied(nextCell) && UndoCount(c) == n1 && list.CurrentRef == target,
                $"U2-1 {what}: the arrow selects the next cell without applying it — {U2State(nextCell)} (entries {UndoCount(c) - n1})");
            string nextRef = list.Items[pNext].Ref;
            U2Ev(c, new { type = "key", key = "Enter" });
            yield return WaitFor(() => list.CurrentRef == nextRef && list.PendingRef == null, 30, ok);
            yield return PlayModeHarness.WaitFrames(3);
            yield return U2Cell(list, pNext, cellBox);
            c.Check(ok[0] && UndoCount(c) == n1 + 1 && U2Applied(cellBox[0]), $"U2-1 {what}: Enter applies the selected cell ({nextRef}; {U2State(cellBox[0])})");
            // a double click on the first applied row again: the first click applies it, the second (Enter-like) does nothing more
            int n2 = UndoCount(c);
            yield return U2Cell(list, p, cellBox);
            if (cellBox[0] != null)
            {
                at = cellBox[0].worldBound.center;
                U2Ev(c, new { type = "pointer", phase = "down", x = at.x, y = at.y, button = 0, clicks = 1 });
                U2Ev(c, new { type = "pointer", phase = "up", x = at.x, y = at.y, button = 0, clicks = 1 });
                U2Ev(c, new { type = "pointer", phase = "down", x = at.x, y = at.y, button = 0, clicks = 2 });
                U2Ev(c, new { type = "pointer", phase = "up", x = at.x, y = at.y, button = 0, clicks = 2 });
                yield return WaitFor(() => list.CurrentRef == target && list.PendingRef == null, 30, ok);
                yield return PlayModeHarness.WaitFrames(3);
                c.Check(ok[0] && UndoCount(c) == n2 + 1, $"U2-1 {what}: a double click applies the row once ({UndoCount(c) - n2} entry)");
            }
            yield return MfUndoTo(c, n0);
            yield return MhSettle(c, tab);
            c.Check(list.CurrentRef == cur0 || (MfGet(c, f, member) as string) == cur0, $"U2-1 {what}: undo restores {cur0} ({MfGet(c, f, member)})");
        }
        CharaEditTab.LastPage.Clear();
        foreach (var kv in lastPages) CharaEditTab.LastPage[kv.Key] = kv.Value;
        PartSelectorState.Clothes.Part = clothesPart;
        PartSelectorState.Hair.Part = hairPart;
        yield return NewScene(c);
    }
}
