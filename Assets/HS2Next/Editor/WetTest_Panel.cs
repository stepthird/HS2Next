// WetTest.play, the Wet and stains panel (package U): the app with the UI in Editor play, real bakes on BR-Chan, input through ui.input and a pointer raised through the panel; judged by
// the block, the history and the widgets; no pixel is read.
//   WY10  the panel at widths 360 and 300 in the three languages: its controls within the region
//   WY11  every row edits and its default button goes back; one entry per drag; Esc takes a drag back; a shape drag previews and commits once; a typed value; the run buttons, the order,
//         the presets, keep; a kept stain takes no edit; «Pick origin» through the tool; the marker in the view and not in a capture; a closed panel reads nothing; «Open in panel»
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2Next.App;
using HS2Next.App.Commands;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Wet;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using HS2Next.UI.Widgets;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class WetTest
{
    static WetPanel WPanel => WetPanel.Current;
    static string WTop => TUi.Session.Commands.UndoList.Count > 0 ? TUi.Session.Commands.UndoList[0].Label : null;
    static CharaWet WBlock => TUi.Ops.WetBlock(tA);
    static WetStain WStain => WBlock?.CurrentStain;

    static bool WVisible(VisualElement e)
    {
        if (e == null || e.panel == null) return false;
        for (var x = e; x != null; x = x.hierarchy.parent) if (x.resolvedStyle.display == DisplayStyle.None) return false;
        return e.worldBound.width > 0;
    }

    static IEnumerator WReveal(VisualElement e)
    {
        for (var x = e?.hierarchy.parent; x != null; x = x.hierarchy.parent)
            if (x is Foldout f && !f.value) f.value = true;
        yield return PFrames(2);
        for (var x = e?.hierarchy.parent; x != null; x = x.hierarchy.parent)
            if (x is ScrollView sv) sv.ScrollTo(e);
        yield return PFrames(3);
    }

    static IEnumerator WClick(string name)
    {
        var e = WPanel.Q(name);
        yield return WReveal(e);
        if (!Check("the element " + name, e != null && e.panel != null)) yield break;
        yield return TIn(TPt(e.worldBound.center, "click"));
        yield return PFrames(2);
    }

    /// <summary>A pointer through the panel as ui.input raises it, with the delta a hand's move has.</summary>
    static void WRaw(EventType et, Vector2 p, Vector2 delta = default)
    {
        var se = new Event { type = et, mousePosition = p, delta = delta, button = 0, clickCount = 1, modifiers = EventModifiers.None };
        var tree = TPanel.visualTree;
        if (et == EventType.MouseDown) using (var e = PointerDownEvent.GetPooled(se)) tree.SendEvent(e);
        else if (et == EventType.MouseUp) using (var e = PointerUpEvent.GetPooled(se)) tree.SendEvent(e);
        else using (var e = PointerMoveEvent.GetPooled(se)) tree.SendEvent(e);
    }

    static WetNumberRow WNumber(string path, int component = -1) => WPanel.Rows.OfType<WetNumberRow>().FirstOrDefault(r => r.Path == path && r.name == "wet-field-" + path + (component >= 0 ? "-" + component : ""));

    /// <summary>A slider's drag from its label: press, two steps, release or none.</summary>
    static IEnumerator WDrag(NumberSlider s, float to, bool release = true)
    {
        var track = s.Slider;
        yield return WReveal(track);
        var b = track.worldBound;
        float x0 = b.xMin + b.width * 0.5f, y = b.center.y;
        float t = Mathf.InverseLerp((float)s.Min, (float)s.Max, to);
        float x1 = b.xMin + b.width * t;
        WRaw(EventType.MouseDown, new Vector2(x0, y));
        yield return PFrames(1);
        WRaw(EventType.MouseDrag, new Vector2((x0 + x1) / 2, y), new Vector2((x1 - x0) / 2, 0));
        yield return PFrames(1);
        WRaw(EventType.MouseDrag, new Vector2(x1, y), new Vector2((x1 - x0) / 2, 0));
        yield return PFrames(1);
        if (release)
        {
            WRaw(EventType.MouseUp, new Vector2(x1, y));
            yield return PFrames(3);
        }
    }

    static IEnumerator Play_Wy()
    {
        if (!new[] { "WY10", "WY11" }.Any(Want)) yield break;
        current = "panel";
        rep.AppendLine();
        rep.AppendLine("== panel setup");
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting && UiHost.Current != null && UiHost.Current.IsReady, 240, ok);
        if (!Check("app and UI ready", ok[0])) yield break;
        yield return TLoadScene();
        if (tA == null) yield break;
        TUi.Ops.Select(new[] { tA });
        yield return PFrames(2);
        if (POpen("WY11", "the panel edits")) yield return WY11();
        if (POpen("WY10", "widths 360 and 300, three languages")) yield return WY10();
    }

    static IEnumerator WY11()
    {
        var ui = TUi;
        // a layout saved by an earlier run may list the panel: it then stands in its region from the start
        if (WetUiModule.PanelShown(ui))
        {
            yield return PCall(TApp, new { cmd = "ui.action", action = ActionIds.WetPanel });
            yield return PFrames(3);
        }
        Check("the panel is not shown before it is asked for", !WetUiModule.PanelShown(ui));
        yield return PCall(TApp, new { cmd = "ui.action", action = ActionIds.WetPanel });
        Check("ui.action wet.panel opens it", POk && WetUiModule.PanelShown(ui), Short(pLastText));
        yield return PFrames(4);
        if (!Check("the panel is shown", WPanel != null && WVisible(WPanel))) yield break;
        Check("its default region is the bottom", ui.Shell.RegionOf(WetUiModule.PanelId) == LayoutRegions.Bottom, ui.Shell.RegionOf(WetUiModule.PanelId));
        // the tall left region: at the batch's window the bottom region shows a few rows only
        ui.Shell.MovePanel(WetUiModule.PanelId, LayoutRegions.Left);
        ui.Shell.ShowPanel(WetUiModule.PanelId);
        ui.Shell.SetSize(ui.Shell.Regions[LayoutRegions.Left], 420f);
        yield return PFrames(4);
        var panel = WPanel;
        Check("no stain yet: the note, no run buttons", WVisible(panel.Q("wet-no-stain")) && !WVisible(panel.Q("wet-run")));

        int undo0 = TUndos;
        yield return WClick("wet-add");
        Check("«Add stain»: one entry, a current stain", WStain != null && TUndos == undo0 + 1 && WTop == OpLabels.WetAdd, WTop + ", " + (TUndos - undo0));
        if (WStain == null) yield break;
        yield return PFrames(3);
        Check("the run buttons show", WVisible(panel.Q("wet-run")) && WVisible(panel.Q("wet-row-0")));
        Check("a row per value, every row the block's path", panel.Rows.Count >= 40 && panel.Rows.All(r => WetEdits.Paths.Contains(r.Path)), panel.Rows.Count.ToString());
        var missing = WetEdits.Paths.Where(p => panel.Rows.All(r => r.Path != p)).ToList();
        Check("every value of the block has a row, but what no row edits", missing.All(p => p == "name" || p == "preset" || p == "seed" || p == "origin.mode" || p == "origin.bone" || p == "origin.local"), string.Join(", ", missing));

        // every number by its typed value, one entry each, and back by its default button
        int checkedRows = 0, bad = 0;
        string detail = "";
        foreach (var row in panel.Rows.OfType<WetNumberRow>().Where(r => r.Path != "floor.height").ToList())
        {
            yield return WReveal(row);
            var s = row.Slider;
            double before = s.Value, def = s.DefaultValue() ?? before;
            double to = s.Min.HasValue ? s.Min.Value + (s.Max.Value - s.Min.Value) * 0.37 : before + 0.25;
            int u = TUndos;
            s.Field.value = (float)to;
            yield return PFrames(2);
            double read = s.Value;
            bool wrote = Math.Abs(read - to) < 1e-3 && TUndos == u + 1;
            bool shows = s.ResetButton.enabledSelf;
            yield return TIn(TPt(s.ResetButton.worldBound.center, "click"));
            yield return PFrames(3);
            bool back = Math.Abs(s.Value - def) < 1e-3 && !s.ResetButton.enabledSelf;
            checkedRows++;
            if (!wrote || !shows || !back) { bad++; detail += " " + row.name + "(" + read + "/" + to + " " + (TUndos - u) + " " + shows + " " + s.Value + ")"; }
        }
        Measure("WY11 number rows typed and reset: " + checkedRows);
        Check("every number row: a typed value is one entry, its default button lights, goes back, and greys", bad == 0, bad + " of " + checkedRows + ":" + detail);

        foreach (var row in panel.Rows.OfType<WetSwitchRow>().ToList())
        {
            yield return WReveal(row);
            bool before = row.Toggle.value;
            int u = TUndos;
            row.Toggle.value = !before;
            yield return PFrames(3);
            Check(row.Path + ": a switch writes, one entry", Equals(WetEdits.Get(WBlock, WStain, row.Path), !before) && TUndos == u + 1, (TUndos - u).ToString());
            row.Toggle.value = before;
            yield return PFrames(3);
        }

        // the colours: a change through the picker, one entry at its close; its cancel takes it back; the default button
        foreach (var row in panel.Rows.OfType<WetColourRow>().ToList())
        {
            yield return WReveal(row);
            var before = WetEdits.Get(WBlock, WStain, row.Path);
            int u = TUndos;
            yield return TIn(TPt(row.Swatch.Button.worldBound.center, "click"));
            yield return PFrames(3);
            var picker = ui.Shell.Overlay.Q<ColorPicker>();
            if (!Check(row.Path + ": the swatch opens the picker", picker != null)) continue;
            picker.Q<TextField>("hex").value = "336699";
            yield return PFrames(2);
            Check(row.Path + ": the colour shows while the picker is open", !Equals(WetEdits.Get(WBlock, WStain, row.Path), before));
            picker.Close(false);
            yield return PFrames(3);
            Check(row.Path + ": the picker's cancel takes it back, no entry", Equals(WetEdits.Get(WBlock, WStain, row.Path), before) && TUndos == u, (TUndos - u).ToString());
            yield return TIn(TPt(row.Swatch.Button.worldBound.center, "click"));
            yield return PFrames(3);
            picker = ui.Shell.Overlay.Q<ColorPicker>();
            picker.Q<TextField>("hex").value = "336699";
            yield return PFrames(2);
            picker.Close(true);
            yield return PFrames(3);
            Check(row.Path + ": closing it is one entry", !Equals(WetEdits.Get(WBlock, WStain, row.Path), before) && TUndos == u + 1, (TUndos - u).ToString());
            var reset = row.Q<Button>("reset");
            bool lit = reset.enabledSelf;
            yield return TIn(TPt(reset.worldBound.center, "click"));
            yield return PFrames(3);
            Check(row.Path + ": its default button goes back and greys", lit && Equals(WetEdits.Get(WBlock, WStain, row.Path), WetEdits.Default(row.Path)) && !reset.enabledSelf, lit + " " + WetEdits.Get(WBlock, WStain, row.Path));
        }

        // a look drag: the value shows at once, one entry
        var amount = WNumber("look.amount");
        float a0 = WStain.Look.Amount;
        int u0 = TUndos;
        yield return WDrag(amount.Slider, 0.8f, false);
        Check("a look value shows during the drag", Mathf.Abs(WStain.Look.Amount - a0) > 0.01f && ui.ActiveEdit != null && ui.ActiveEdit.IsOpen, WStain.Look.Amount.ToString());
        WRaw(EventType.MouseUp, amount.Slider.worldBound.center);
        yield return PFrames(3);
        Check("a look drag is one entry", TUndos == u0 + 1 && WTop == OpLabels.WetEdit, (TUndos - u0) + " " + WTop);

        // a shape drag: a preview, no document, one entry with the last value at the release
        var side = WNumber("shape.side");
        float s0 = WStain.Shape.Side;
        var maps0 = WStain.Maps.Select(m => m.Res).ToList();
        u0 = TUndos;
        yield return WDrag(side.Slider, 2f, false);
        Check("a shape drag writes no document", Mathf.Abs(WStain.Shape.Side - s0) < 1e-6f, WStain.Shape.Side.ToString());
        float shown = (float)side.Slider.Value;
        WRaw(EventType.MouseUp, side.Slider.worldBound.center);
        yield return PFrames(4);
        Check("the release: the last value, one entry, the pictures made anew", Mathf.Abs(WStain.Shape.Side - shown) < 1e-3f && TUndos == u0 + 1 && !WStain.Maps.Select(m => m.Res).SequenceEqual(maps0), WStain.Shape.Side + " / " + shown + ", " + (TUndos - u0));
        yield return PCall(TApp, new { cmd = "undo" });
        yield return PFrames(3);
        Check("undo: the value and the pictures back", Mathf.Abs(WStain.Shape.Side - s0) < 1e-6f && WStain.Maps.Select(m => m.Res).SequenceEqual(maps0));

        // Esc during a drag
        u0 = TUndos;
        a0 = WStain.Look.Amount;
        yield return WDrag(amount.Slider, 0.9f, false);
        UiCommandsEsc();
        yield return PFrames(3);
        Check("Esc during a look drag: the value back, no entry, the edit closed", Mathf.Abs(WStain.Look.Amount - a0) < 1e-6f && TUndos == u0 && (ui.ActiveEdit == null || !ui.ActiveEdit.IsOpen), WStain.Look.Amount.ToString());
        WRaw(EventType.MouseUp, amount.Slider.worldBound.center);
        yield return PFrames(2);
        u0 = TUndos;
        s0 = WStain.Shape.Side;
        yield return WDrag(side.Slider, 2.4f, false);
        UiCommandsEsc();
        yield return PFrames(3);
        Check("Esc during a shape drag: no document, no entry", Mathf.Abs(WStain.Shape.Side - s0) < 1e-6f && TUndos == u0);
        WRaw(EventType.MouseUp, side.Slider.worldBound.center);
        yield return PFrames(2);

        // the run buttons
        u0 = TUndos;
        yield return WClick("wet-start");
        Check("«Start»: running, one entry", WStain.Running && TUndos == u0 + 1 && WTop == OpLabels.WetStart, WTop);
        yield return WClick("wet-full");
        Check("«Full»: at the end of the spread, stopped", !WStain.Running && WStain.T >= WStain.Shape.Spread - 1e-3f, WStain.T + "");
        yield return WClick("wet-dry");
        Check("«Dry»: t 0", WStain.T == 0 && !WStain.Running && WTop == OpLabels.WetDry, WTop);
        yield return WClick("wet-clear-puddles");
        Check("«Clear puddles» answers", WBlock.Puddles.All(p => p.Color != null));

        // presets
        yield return WClick("wet-preset-blood");
        Check("«Blood» writes the look", Mathf.Abs(WStain.Look.Amount - 0.85f) < 1e-5f && WTop == OpLabels.WetEdit, WStain.Look.Amount.ToString());
        yield return WClick("wet-preset-water");
        Check("«Water» writes the look", Mathf.Abs(WStain.Look.Amount - 0.3f) < 1e-5f);

        // the floor
        var mode = panel.Rows.OfType<WetFloorModeRow>().First();
        yield return WReveal(mode);
        yield return TIn(TPt(mode.Buttons.Buttons[2].worldBound.center, "click"));
        yield return PFrames(3);
        Check("the floor's mode: fixed, one entry, its height row shows", WBlock.Floor.Mode == WetFloorValues.ModeFixed && WTop == OpLabels.WetFloor && WVisible(panel.Q("wet-field-floor.height")), WBlock.Floor.Mode + " " + WTop);
        var height = WNumber("floor.height");
        u0 = TUndos;
        height.Slider.Field.value = 1.25f;
        yield return PFrames(3);
        Check("the height of a fixed floor: one entry «Floor»", Mathf.Abs(WBlock.Floor.Height - 1.25f) < 1e-5f && TUndos == u0 + 1 && WTop == OpLabels.WetFloor, WBlock.Floor.Height + " " + WTop);
        yield return TIn(TPt(mode.Buttons.Buttons[0].worldBound.center, "click"));
        yield return PFrames(3);
        Check("the floor's mode back to the map's, the height row gone", WBlock.Floor.Mode == WetFloorValues.ModeMap && !WVisible(panel.Q("wet-field-floor.height")));

        // the origin through the tool
        u0 = TUndos;
        yield return WClick("wet-pick-origin");
        Check("«Pick origin»: a tool runs, its hint in the status line", Tools.Running && ui.Shell.StatusBar.Hint.text == ui.L10n.Get("wet.pickOrigin.hint"), ui.Shell.StatusBar.Hint.text);
        yield return TIn(TPt(tEmpty, "down"), TPt(tEmpty, "up"));
        Check("a press beside the character: the tool stays", Tools.Running && TUndos == u0);
        yield return TIn(TPt(tOnA, "down"), TPt(tOnA, "up"));
        yield return PFrames(3);
        Check("a press on the character: the origin, one entry, the tool ended", !Tools.Running && WStain.Origin.Mode == WetOriginValues.ModeBone && WStain.Origin.Bone.Length > 0 && TUndos == u0 + 1 && WTop == OpLabels.WetOrigin, WStain.Origin.Mode + " " + WStain.Origin.Bone + " " + WTop);
        yield return WClick("wet-pick-origin");
        yield return TIn(TKey("Escape"));
        Check("Esc leaves «Pick origin»", !Tools.Running);
        yield return WClick("wet-default-origin");
        Check("«Default origin»", WStain.Origin.Mode == WetOriginValues.ModeDefault && WTop == OpLabels.WetOrigin, WStain.Origin.Mode + " " + WTop);

        // the marker: in the view, not in a capture
        var cam = Vp.ViewCamera;
        bool was = ui.Ops.WetShowMarker;
        ui.Ops.WetShowMarker = false;
        var off = ViewCapture.Render(cam, 512, 288, ViewCapture.None);
        TRender();
        int noMarker = Tools.LastDrawn;
        ui.Ops.WetShowMarker = true;
        TRender();
        Check("the marker: a cross in the view with its toggle", Tools.LastDrawn == 3 && noMarker == 0, noMarker + " / " + Tools.LastDrawn);
        var on = ViewCapture.Render(cam, 512, 288, ViewCapture.None);
        Check("the marker is not in a capture", on != null && off != null && on.SequenceEqual(off));
        ui.Ops.WetShowMarker = was;

        // two stains, the order, keep
        yield return WClick("wet-add");
        yield return PFrames(3);
        Check("a second stain, made current", WBlock.Stains.Count == 2 && WBlock.Current == WBlock.Stains[1].Id);
        string second = WBlock.Stains[1].Id;
        Check("the last stain stands first in the list", panel.Q("wet-stains").IndexOf(panel.Q("wet-row-1")) == 0);
        yield return WClick("wet-row-1-down");
        Check("▼ moves it below, one place", WBlock.Stains[0].Id == second, string.Join(",", WBlock.Stains.Select(s => s.Id)));
        yield return WClick("wet-row-0-up");
        Check("▲ moves it back", WBlock.Stains[1].Id == second);
        yield return WClick("wet-row-0-name");
        Check("a name makes its stain current", WBlock.Current == WBlock.Stains[0].Id);
        yield return WClick("wet-row-1-name");
        int stains = WBlock.Stains.Count;
        yield return WClick("wet-keep");
        Check("«Keep as layer»: the stain kept, a new current one", WBlock.Stains.Count == stains + 1 && WBlock.Stains[stains - 1].Kept && !WStain.Kept && WTop == OpLabels.WetKeep, WTop);
        yield return WClick("wet-row-" + (stains - 1) + "-name");
        yield return PFrames(3);
        Check("a kept stain: its rows greyed, a block's row not", !amount.enabledInHierarchy && panel.Rows.First(r => r.Path == "film.gloss").enabledInHierarchy, amount.enabledInHierarchy + "");
        yield return WClick("wet-row-" + (stains - 1) + "-remove");
        Check("✕ removes a stain", WBlock.Stains.Count == stains && WTop == OpLabels.WetRemove, WTop);

        // «Apply again» with every part live, «Remove all» and its undo
        u0 = TUndos;
        yield return WClick("wet-apply-again");
        Check("«Apply again»: every part has its map, no entry", TUndos == u0, (TUndos - u0).ToString());
        int stainCount = WBlock.Stains.Count;
        yield return WClick("wet-remove-all");
        yield return PFrames(2);
        Check("«Remove all»: the block empty, one entry, the note again", WBlock.IsEmpty && TUndos == u0 + 1 && WTop == OpLabels.WetRemove && WVisible(panel.Q("wet-no-stain")), WTop);
        yield return PCall(TApp, new { cmd = "undo" });
        yield return PFrames(4);
        Check("undo brings the stains back", WBlock.Stains.Count == stainCount, WBlock.Stains.Count.ToString());

        // several selected
        ui.Ops.Select(new[] { tA, tB });
        yield return PFrames(3);
        panel.MarkDirty();
        yield return PFrames(2);
        Check("two selected: the primary's, nothing edits", panel.Node == tA && WVisible(panel.Q("wet-primary")) && !panel.Q("wet-body").enabledInHierarchy);
        ui.Ops.Select(new[] { tB });
        yield return PFrames(3);
        Check("another character: its block", panel.Node == tB && WVisible(panel.Q("wet-no-stain")));
        ui.Ops.Select(Array.Empty<string>());
        yield return PFrames(3);
        Check("no character: the note", panel.Node == null && WVisible(panel.Q("wet-none")));
        ui.Ops.Select(new[] { tA });
        yield return PFrames(3);

        // a closed panel reads nothing, the section reads nothing while closed
        yield return PCall(TApp, new { cmd = "ui.action", action = ActionIds.WetPanel });
        yield return PFrames(3);
        int reads = panel.Reads;
        ui.Session.Set(tA, "transform.pos", new Vec3(0, 10, 0), UiContext.User());
        yield return PFrames(30);
        Check("a closed panel reads nothing", !WetUiModule.PanelShown(ui) && panel.Reads == reads, panel.Reads - reads + " reads");

        // the section of the State tab
        ui.Shell.ResetLayout();
        yield return PFrames(3);
        ui.Shell.FocusPanel("inspector");
        yield return PFrames(3);
        var root = ui.Shell.PanelRoot("inspector");
        var insp = root as InspectorPanel ?? root?.Q<InspectorPanel>();
        insp?.Chara?.ShowTab("state");
        yield return PFrames(4);
        var section = TPanel.visualTree.Q<WetSection>();
        if (Check("the section stands in the State tab, closed", section != null && !section.Fold.value, (insp != null) + " " + (insp?.Chara != null) + " " + (section != null)))
        {
            Check("it is the last of the tab", section.parent.IndexOf(section) == section.parent.childCount - 1);
            int sr = section.Reads;
            yield return PFrames(10);
            Check("a closed section reads nothing", section.Reads == sr);
            section.Fold.value = true;
            yield return PFrames(3);
            var list = section.Q<Label>("wet-section-list");
            Check("opened: the stains' names", list != null && WBlock.Stains.All(s => list.text.Contains(s.Name)), list?.text);
            var open = section.Q<Button>("wet-section-open");
            yield return WReveal(open);
            yield return TIn(TPt(open.worldBound.center, "click"));
            yield return PFrames(4);
            Check("«Open in panel» shows the panel", WetUiModule.PanelShown(ui) && WPanel != null);
            section.Fold.value = false;
            yield return PFrames(2);
        }
        WetUiModule.Show(ui);
        yield return PFrames(3);
    }

    static void UiCommandsEsc() => HS2Next.UI.Bridge.UiCommands.Send(THost, JObject.FromObject(new { type = "key", key = "Escape" }), out _);

    static IEnumerator WY10()
    {
        var ui = TUi;
        var sh = ui.Shell;
        var host = THost;
        WetUiModule.Show(ui);
        yield return PFrames(3);
        if (ui.Ops.WetBlock(tA).Stains.Count == 0)
        {
            ui.ShowResult(ui.Ops.WetAdd(tA, WetPreset.Water, null, UiContext.User(OpLabels.WetAdd)));
            yield return PFrames(3);
        }
        sh.MovePanel(WetUiModule.PanelId, LayoutRegions.Left);
        sh.ShowPanel(WetUiModule.PanelId);
        yield return PFrames(4);
        var left = sh.Regions[LayoutRegions.Left];
        foreach (float width in new[] { 360f, 300f })
        {
            sh.SetSize(left, width);
            yield return PFrames(4);
            foreach (var lang in new[] { "en", "ja", "zh-Hans" })
            {
                host.SetLanguage(lang, save: false);
                yield return PFrames(4);
                sh.ShowPanel(WetUiModule.PanelId);
                yield return PFrames(4);
                string tag = width.ToString("0") + " px " + lang;
                var root = WPanel.worldBound;
                Check(tag + ": the panel fits its region", root.width > 100 && root.width <= width + 0.5f, root.ToString());
                var outside = new List<string>();
                foreach (var name in new[] { "wet-add", "wet-remove-all", "wet-start", "wet-full", "wet-dry", "wet-clear-puddles", "wet-keep", "wet-pick-origin", "wet-default-origin", "wet-apply-again", "wet-show-marker", "wet-preset-water", "wet-preset-blood", "wet-row-0-name", "wet-row-0-remove" })
                {
                    var e = WPanel.Q(name);
                    var b = e?.worldBound ?? default;
                    if (e == null || b.width < 4 || b.xMin < root.xMin - 0.5f || b.xMax > root.xMax + 0.5f) outside.Add(name);
                }
                Check(tag + ": every control lies within the panel", outside.Count == 0, string.Join(", ", outside));
                var narrow = WPanel.Rows.Where(r => WVisible(r) && (r.worldBound.xMax > root.xMax + 0.5f)).Select(r => r.name).ToList();
                Check(tag + ": the value rows lie within the panel", narrow.Count == 0, string.Join(", ", narrow));
            }
        }
        host.SetLanguage("en", save: false);
        sh.ResetLayout();
        yield return PFrames(4);
    }
}
