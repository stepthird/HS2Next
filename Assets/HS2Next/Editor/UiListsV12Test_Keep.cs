// UiListsV12Test.play groups for owner item 6: the Costume tab's «読込» keeps the coordinate
// list's order, choice and scroll; the coordinate dialog opened again keeps the order; the character browser keeps its order,
// selection and scroll across an add and «キャラ変更». The character is the SFW female target; outfit cards are counted and
// chosen by file name (the base game's «ジャージ» when the install has it), never looked at.
//   M6 the Costume tab across «読込» (and a double click), then the coordinate dialog
//   CB the character browser across an add and a change of character
using System;
using System.Collections;
using System.IO;
using System.Linq;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Panels;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class UiListsV12Test
{
    static string femaleChara;

    static IEnumerator FemaleOnStage(Ctx c, bool[] ok)
    {
        var ui = c.Ui;
        ok[0] = false;
        if (femaleChara != null && ui.Model.Find(femaleChara) != null) { ok[0] = true; yield break; }
        while (ui.Dialogs.IsModalOpen) ui.Dialogs.CloseCurrent();
        SceneFiles.Current?.NewNow();
        yield return Frames(2);
        var t = TestTargets.Load();
        string card = TestTargets.Abs(t.visual.female);
        var r = ui.Ops.AddChara(card, null, null, -1, null, UiContext.User());
        femaleChara = r?.NewIds.FirstOrDefault();
        var w = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => femaleChara != null && c.App.Runtime.FindChara(femaleChara)?.State == RealizationState.Built, 90, w);
        ok[0] = w[0];
    }

    static InspectorPanel Inspector(Ctx c)
    {
        c.Ui.Shell.FocusPanel(UiShell.InspectorId);
        var root = c.Ui.Shell.PanelRoot(UiShell.InspectorId);
        return root as InspectorPanel ?? root?.Q<InspectorPanel>();
    }

    static CharaCostumeTab CostumeTab(Ctx c)
    {
        var insp = Inspector(c);
        insp?.Chara?.ShowTab(CharaCostumeTab.TabId);
        return insp?.Chara?.TabBodies.TryGetValue(CharaCostumeTab.TabId, out var b) == true ? b as CharaCostumeTab : null;
    }

    static ScrollView ScrollOf(CoordinateBrowser b) => b?.Grid.List.Q<ScrollView>();

    static System.Collections.Generic.List<string> Order(CoordinateBrowser b) => b.Index.Indices.Select(i => b.Index.Rows[i].FullPath).ToList();

    static IEnumerator CostumeGroup(Ctx c)
    {
        var ui = c.Ui;
        var ok = new bool[1];
        yield return FemaleOnStage(c, ok);
        c.Check(ok[0], "the SFW female target is built");
        if (!ok[0]) yield break;
        ui.Selection.Set(new[] { femaleChara });
        yield return Frames(4);
        var ct = CostumeTab(c);
        yield return PlayModeHarness.WaitUntil(() => ct?.Browser != null && ct.Browser.Ready, 60, ok);
        c.Check(ok[0] && ct.Browser.Index.Indices.Count > 60, "the Costume tab lists the female outfit cards (" + ct?.Browser?.Index?.Indices.Count + ")");
        if (!ok[0]) yield break;
        var b = ct.Browser;
        // sort by date, newest first
        var sort = b.Q<DropdownField>("coord-sort");
        sort.value = "date";
        var dir = b.Q<Button>("coord-sort-dir");
        if (dir != null && dir.text == "▲")
        {
            // the browser's header in view of the inspector's scroll, then a click on the direction button
            dir.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(dir);
            yield return Frames(3);
            Click(c, dir);
        }
        yield return Frames(3);
        var orderBefore = Order(b);
        int n = orderBefore.Count;
        string jersey = CoordinateBrowser.Hs2Dir(ui, 1) is string hd ? Path.Combine(hd, "ジャージ.png") : null;
        string pick = jersey != null && File.Exists(jersey) && orderBefore.Contains(jersey) ? jersey : orderBefore[Math.Min(45, n - 1)];
        b.Choose(pick);
        yield return Frames(4);
        var sv = ScrollOf(b);
        if (sv != null && sv.scrollOffset.y < 1f && sv.verticalScroller.highValue > 200) { sv.scrollOffset = new Vector2(0, Mathf.Min(sv.verticalScroller.highValue, 3 * b.Grid.RowHeight + 17)); yield return Frames(2); }
        float off = sv?.scrollOffset.y ?? -1;
        var insp = Inspector(c);
        int builds = insp?.Builds ?? -1;
        var isv = insp?.Q<ScrollView>("inspector-scroll");
        float inspOff = isv?.scrollOffset.y ?? -1;
        c.Info($"before «読込»: sort {sort.value} {(dir?.text)}, chosen {Path.GetFileName(pick)} at {orderBefore.IndexOf(pick)} of {n}, scroll {off:0.#} px (row {b.Grid.RowHeight:0.#} px, {b.Grid.Columns} columns), inspector builds {builds}");
        var r = ct.Load(b.Selected?.FullPath);
        c.Check(r != null && r.Ok, "«読込» wore the outfit (" + r?.Message + ")");
        yield return PlayModeHarness.WaitUntil(() => c.App.Runtime.FindChara(femaleChara)?.State == RealizationState.Built, 60, ok);
        yield return Frames(30);
        insp = Inspector(c);
        var ct2 = CostumeTab(c);
        yield return PlayModeHarness.WaitUntil(() => ct2?.Browser != null && ct2.Browser.Ready, 30, ok);
        yield return Frames(20);
        var b2 = ct2?.Browser;
        var sv2 = ScrollOf(b2);
        bool rebuilt = !ReferenceEquals(b2, b);
        c.Info($"after «読込»: inspector builds {insp?.Builds} (before {builds}), the browser {(rebuilt ? "built again" : "the same")}; the inspector's own scroll {inspOff:0.#} → {insp?.Q<ScrollView>("inspector-scroll")?.scrollOffset.y:0.#} px");
        var sort2 = b2?.Q<DropdownField>("coord-sort");
        var dir2 = b2?.Q<Button>("coord-sort-dir");
        c.Check(b2 != null && sort2?.value == "date" && dir2?.text == "▼" && Order(b2).SequenceEqual(orderBefore), $"after «読込» the order is kept (sort {sort2?.value} {dir2?.text})");
        c.Check(b2?.Selected?.FullPath == pick, "after «読込» the chosen card is kept (" + Path.GetFileName(b2?.Selected?.FullPath ?? "none") + ")");
        c.Check(sv2 != null && Math.Abs(sv2.scrollOffset.y - off) < 1f, $"after «読込» the scroll is kept ({off:0.#} → {sv2?.scrollOffset.y:0.#} px)");
        // a double click (activation) does the same
        float off2 = sv2?.scrollOffset.y ?? -1;
        b2?.Grid.Activate();
        yield return PlayModeHarness.WaitUntil(() => c.App.Runtime.FindChara(femaleChara)?.State == RealizationState.Built, 60, ok);
        yield return Frames(40);
        var b3 = CostumeTab(c)?.Browser;
        var sv3 = ScrollOf(b3);
        c.Check(b3 != null && Order(b3).SequenceEqual(orderBefore) && b3.Selected?.FullPath == pick && sv3 != null && Math.Abs(sv3.scrollOffset.y - off2) < 1f,
            $"a double click keeps them too (scroll {off2:0.#} → {sv3?.scrollOffset.y:0.#})");
        // a cell's right click in the Costume tab opens the file menu
        if (b3 != null)
        {
            b3.Grid.ScrollToPos(b3.Grid.SelectedPos);
            yield return Frames(3);
            var cell = b3.Grid.CellAt(b3.Grid.SelectedPos);
            if (cell != null) { cell.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(cell); yield return Frames(2); }
            ui.Shell.Menus.CloseAll();
            if (cell != null) Click(c, cell, 1);
            yield return Frames(3);
            c.Check(cell != null && ui.Shell.Menus.IsOpen && ListMenus.LastMenuName == "coord-context" && ListMenus.LastItems.Any(i => i.Id == "list-delete"),
                "the Costume tab: a cell's right click opens the file menu (" + ListMenus.LastMenuName + ")");
            ui.Shell.Menus.CloseAll();
        }
        // the coordinate dialog of the same sex opens with the same order
        var dlg = CoordinateDialog.Open(ui, 1, (f, m) => HS2Next.Scenes.Runtime.OpResult.NothingToDo());
        yield return PlayModeHarness.WaitUntil(() => dlg?.Browser != null && dlg.Browser.Ready, 30, ok);
        yield return Frames(4);
        c.Check(dlg?.Browser != null && dlg.Browser.Q<DropdownField>("coord-sort")?.value == "date" && Order(dlg.Browser).SequenceEqual(orderBefore),
            "the coordinate dialog opened again lists in the same order");
        dlg?.Press("cancel");
        yield return Frames(2);
    }

    static IEnumerator CharaKeepGroup(Ctx c)
    {
        var ui = c.Ui;
        var ok = new bool[1];
        yield return FemaleOnStage(c, ok);
        ui.Shell.FocusPanel(CharaBrowser.PanelId);
        var cb = CharaBrowser.Current;
        yield return PlayModeHarness.WaitUntil(() => { cb = CharaBrowser.Current; return cb != null && cb.Ready && cb.Sex == 1; }, 60, ok);
        c.Check(ok[0], "the character browser lists the female cards (" + cb?.Index?.Indices.Count + ")");
        if (!ok[0]) yield break;
        var sort = cb.Q<DropdownField>("chara-sort");
        sort.value = "date";
        yield return Frames(3);
        var t = TestTargets.Load();
        string card = TestTargets.Abs(t.visual.female);
        int pos = cb.Grid.PositionOf(e => string.Equals(e.FullPath, card, StringComparison.OrdinalIgnoreCase));
        c.Check(pos >= 0, "the SFW card is listed (" + pos + ")");
        if (pos < 0) yield break;
        cb.Grid.Select(pos);
        yield return Frames(3);
        var sv = cb.Grid.List.Q<ScrollView>();
        float off = sv?.scrollOffset.y ?? -1;
        var order = cb.Index.Indices.ToList();
        var r = cb.AddCard(cb.SelectedEntry, null, null);
        string added = r?.NewIds.FirstOrDefault();
        yield return PlayModeHarness.WaitUntil(() => added != null && c.App.Runtime.FindChara(added)?.State == RealizationState.Built, 90, ok);
        yield return Frames(10);
        c.Check(r != null && r.Ok && cb.Index.Indices.SequenceEqual(order) && cb.Grid.SelectedPos == pos && sv != null && Math.Abs(sv.scrollOffset.y - off) < 1f && sort.value == "date",
            $"add: order, selection and scroll kept (scroll {off:0.#} → {sv?.scrollOffset.y:0.#}, selected {cb.Grid.SelectedPos})");
        ui.Selection.Set(new[] { added });
        yield return Frames(2);
        var r2 = cb.ChangeTo(cb.SelectedEntry);
        yield return PlayModeHarness.WaitUntil(() => c.App.Runtime.FindChara(added)?.State == RealizationState.Built, 90, ok);
        yield return Frames(10);
        c.Check(r2 != null && r2.Ok && cb.Index.Indices.SequenceEqual(order) && cb.Grid.SelectedPos == pos && Math.Abs(sv.scrollOffset.y - off) < 1f,
            $"«キャラ変更»: order, selection and scroll kept (scroll {off:0.#} → {sv?.scrollOffset.y:0.#})");
        sort.value = "name";
        yield return Frames(2);
    }
}
