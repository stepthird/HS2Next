// UiShellTest.play groups for the field of view and the panels' wrapping texts (after 1.1.0, package S):
//   FV  the field of view of the current view: the cameras panel's «Field of view» row (a slider 1–60, live while dragging,
//       ↺ = 23; the free camera written as its view state without a history entry, as the keys write it), the viewport's
//       readout (its text, the tooltip naming the keys of the active key map, the wheel over it, a click opening the popup
//       slider, Esc closing it, a double click resetting), the Camera menu's rows «Widen / Narrow / Reset the field of view»
//       with the key map's shortcut texts (default and hs2studio), Ctrl+Home resetting it in the default key map, the key
//       overlay listing the three actions; with the view looking through a camera object: the row, the readout, the wheel and
//       the menu edit that camera's `camera.fov` (one entry "Camera" per drag and per run of wheel steps, which ends after a
//       pause or at the next click), undo restores it, the keys leave it alone, values stay in [1, 60].
//   WR  the empty-state and hint texts wrap: the cameras panel's «No cameras yet …» and its status line in en, ja and zh-Hans
//       at the sidebar's width, at its minimum (300 px) and in a 247 px column (the narrowest sidebar seen at the player's UI
//       scale 2.19; layout and text are measured in logical px, which do not depend on the scale): each inside the panel's
//       content box, its wrapped height within its own; then every panel of the registry at 300 and 247 px: no empty-state
//       or hint label (hs-panel__empty, hs-hint, hs-inspector__note, hs-browser__hint) runs out of its box (the other labels
//       whose single line is wider than their box are listed as INFO, by element name only).
// Events are synthesised through the panel (UiCommands.Send, as ui.input does).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Panels;
using HS2Next.UI.Viewport;
using HS2Next.UI.Widgets;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class UiShellTest
{
    [UnityEditor.InitializeOnLoadMethod]
    static void RegisterFovGroups()
    {
        if (!Groups.Any(g => g.id == "FV")) Groups.Add(("FV", FovGroup));
        if (!Groups.Any(g => g.id == "WR")) Groups.Add(("WR", WrapGroup));
    }

    static string Fv(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    static float ModelFreeFov(UiContext ui) => (FieldAccessor.Get(ui.Model, null, "camera.current") as CameraView)?.Fov ?? SceneViewCamera.DefaultFov;

    static float? CamFov(UiContext ui, string id) => (ui.Model.Find(id)?.State as CameraNodeState)?.Fov;

    static int Undos(UiContext ui) => ui.Session.Commands.UndoList.Count;

    static string TopLabel(UiContext ui) => ui.Session.Commands.UndoList.FirstOrDefault()?.Label;

    static IEnumerator OpenCameraMenu(Ctx c)
    {
        var ui = c.Ui;
        ui.Shell.Menus.CloseAll();
        var tb = ui.Shell.Toolbar.Element.Q<Button>("tb-camera");
        if (tb != null) { using (var e = NavigationSubmitEvent.GetPooled()) { e.target = tb; tb.SendEvent(e); } }
        yield return Frames(2);
    }

    static VisualElement MenuRow(Ctx c, string id) => c.Ui.Shell.Overlay.Q("tb-camera-menu")?.Q("menu-item-" + id);

    static IEnumerator ClickMenuRow(Ctx c, string id)
    {
        yield return OpenCameraMenu(c);
        if (MenuRow(c, id) != null) Ev(c, new { type = "pointer", target = "menu-item-" + id });
        else c.Ui.Shell.Menus.CloseAll();
        yield return Frames(2);
    }

    static IEnumerator Settle(Ctx c)
    {
        c.Ui.Viewport?.Camera?.Flush();
        yield return Frames(2);
        c.Ui.Viewport?.UpdateInfo(true);
    }

    // ---------------- FV: the field of view of the current view

    static IEnumerator FovGroup(Ctx c)
    {
        var ui = c.Ui;
        var vp = ui.Viewport;
        var cc = vp?.Camera;
        var view = c.App.Runtime.GetPart<SceneViewCamera>();
        c.Check(vp != null && cc != null && view?.Camera != null, "the viewport, its camera controller and the view camera exist");
        if (vp == null || cc == null || view?.Camera == null) yield break;
        string preset0 = ui.Keys.PresetName;
        if (preset0 != Keymap.DefaultPreset) { ui.Options.Set(OptionDefs.KeyPreset, Keymap.DefaultPreset); yield return Frames(2); }
        while (ui.Dialogs.IsModalOpen) ui.Dialogs.CloseCurrent();
        ui.Ops.NewScene();
        yield return Frames(3);
        c.Check(ViewFov.Target(ui) == null && Mathf.Abs(ViewFov.Current(ui) - 23f) < 1e-4f, $"a new scene: the free camera at {Fv(ViewFov.Current(ui))}° (23)");

        // the cameras panel's row
        ui.Shell.ShowPanel(CameraListPanel.PanelId);
        yield return Frames(3);
        var panel = ui.Shell.PanelRoot(CameraListPanel.PanelId)?.Q<CameraListPanel>();
        var row = panel?.Fov;
        var s = row?.Slider;
        c.Check(s != null && s.Slider != null && s.Min == 1 && s.Max == 60 && s.ResetButton.resolvedStyle.display == DisplayStyle.Flex
                && Mathf.Abs((float)s.Value - 23f) < 1e-4f && s.LabelElement.text == L10n.Get("viewfov.label"),
            $"the cameras panel: a «{L10n.Get("viewfov.label")}» row below its header, slider {s?.Min}–{s?.Max} showing {s?.Value}, with ↺");
        if (s == null) yield break;
        int u0 = Undos(ui);
        s.Begin();
        s.Raise(EditPhase.Preview, 30);
        float live = cc.Current.Fov;
        yield return Frames(2);
        float liveCam = view.Camera.fieldOfView;
        s.Raise(EditPhase.Preview, 41.5);
        s.End();
        yield return Settle(c);
        c.Check(Mathf.Abs(live - 30f) < 1e-4f && Mathf.Abs(liveCam - 30f) < 1e-3f && Mathf.Abs(ModelFreeFov(ui) - 41.5f) < 1e-4f && Mathf.Abs(view.Camera.fieldOfView - 41.5f) < 1e-3f,
            $"a drag on the row is live: 30 during the drag (controller {Fv(live)}, view camera {Fv(liveCam)}), 41.5 after it (camera.current {Fv(ModelFreeFov(ui))}, view camera {Fv(view.Camera.fieldOfView)})");
        c.Check(Undos(ui) == u0, $"the free camera's field of view is view state, as the keys write it: no history entry ({Undos(ui) - u0})");
        s.Begin(); s.Raise(EditPhase.Preview, 95); float hi = cc.Current.Fov; s.Raise(EditPhase.Preview, -4); float lo = cc.Current.Fov; s.End();
        s.Raise(EditPhase.Typed, 75); yield return Settle(c); float typedHi = ModelFreeFov(ui);
        s.Raise(EditPhase.Typed, 0.2); yield return Settle(c); float typedLo = ModelFreeFov(ui);
        c.Check(Mathf.Abs(hi - 60f) < 1e-4f && Mathf.Abs(lo - 1f) < 1e-4f && Mathf.Abs(typedHi - 60f) < 1e-4f && Mathf.Abs(typedLo - 1f) < 1e-4f,
            $"values stay in [1, 60]: drag 95 → {Fv(hi)}, −4 → {Fv(lo)}; typed 75 → {Fv(typedHi)}, 0.2 → {Fv(typedLo)}");
        Ev(c, new { type = "pointer", target = "cameras-fov-reset" });
        yield return Settle(c);
        c.Check(Mathf.Abs(ModelFreeFov(ui) - 23f) < 1e-4f && Undos(ui) == u0, $"↺ resets to 23 ({Fv(ModelFreeFov(ui))}), no entry");

        // the viewport's readout: text, tooltip with the default key map's keys, wheel, popup, double click
        var fl = vp.FovLabel;
        string wantText = L10n.Format("viewport.fov", "23");
        string tip = fl.tooltip ?? "";
        string kDown = ui.Keys.ChordText(ActionIds.CameraFovDown), kUp = ui.Keys.ChordText(ActionIds.CameraFovUp), kReset = ui.Keys.ChordText(ActionIds.CameraFovReset);
        c.Check(fl.text == wantText && fl.pickingMode == PickingMode.Position && fl.resolvedStyle.display == DisplayStyle.Flex,
            $"the readout reads «{fl.text}» ({wantText}) and takes the pointer");
        c.Check(kDown.Length > 0 && kUp.Length > 0 && kReset == "Ctrl+Home" && tip.Contains(kDown) && tip.Contains(kUp) && tip.Contains(kReset) && tip.Contains(L10n.Format("viewfov.tip.free", "23")),
            $"its tooltip names the default key map's keys ({kDown} / {kUp} / {kReset}): «{tip.Replace("\n", " | ")}»");
        float d0 = cc.Current.Distance;
        Ev(c, new { type = "wheel", target = "viewport-fov", delta = -3 });
        yield return Settle(c);
        float w1 = ModelFreeFov(ui);
        Ev(c, new { type = "wheel", target = "viewport-fov", delta = 3 });
        Ev(c, new { type = "wheel", target = "viewport-fov", delta = 3 });
        yield return Settle(c);
        float w2 = ModelFreeFov(ui);
        c.Check(Mathf.Abs(w1 - (23f - ViewFov.WheelStep)) < 1e-4f && Mathf.Abs(w2 - (23f + ViewFov.WheelStep)) < 1e-4f && Mathf.Abs(cc.Current.Distance - d0) < 1e-5f && Undos(ui) == u0,
            $"the wheel over the readout: up narrows to {Fv(w1)}, two notches down widen to {Fv(w2)} ({Fv(ViewFov.WheelStep)}° a notch), the camera does not dolly ({Fv(d0)} → {Fv(cc.Current.Distance)}), no entry");
        c.Check(row.Slider.Value == w2 || Mathf.Abs((float)row.Slider.Value - w2) < 1e-4f || Refreshed(row, w2), $"the panel's row follows ({row.Slider.Value})");
        Ev(c, new { type = "pointer", target = "viewport-fov" });
        yield return Frames(3);
        var pop = vp.FovPopup;
        var prow = pop?.Q<FovRow>();
        c.Check(pop != null && pop.panel != null && prow != null && Mathf.Abs((float)prow.Slider.Value - w2) < 1e-4f && pop.Q<Label>("viewport-fov-popup-title")?.text == L10n.Format("viewfov.popup.title", L10n.Get("camera.free")),
            $"a click on the readout opens the popup with the slider at {prow?.Slider.Value} («{pop?.Q<Label>("viewport-fov-popup-title")?.text}»)");
        if (pop != null)
        {
            var rb = ui.Shell.Root.worldBound;
            var pb = pop.worldBound;
            c.Check(pb.xMin >= rb.xMin - 0.5f && pb.xMax <= rb.xMax + 0.5f && pb.yMin >= fl.worldBound.yMax - 0.5f && pb.yMax <= rb.yMax + 0.5f && prow.Slider.Slider.worldBound.width >= 60f,
                $"the popup stands below the readout inside the window (popup {pb.xMin:0}–{pb.xMax:0} × {pb.yMin:0}–{pb.yMax:0}, readout bottom {fl.worldBound.yMax:0}, window {rb.width:0} × {rb.height:0}, slider {prow.Slider.Slider.worldBound.width:0} px)");
        }
        if (prow != null)
        {
            prow.Slider.Begin(); prow.Slider.Raise(EditPhase.Preview, 12); prow.Slider.End();
            yield return Settle(c);
            c.Check(Mathf.Abs(ModelFreeFov(ui) - 12f) < 1e-4f && fl.text == L10n.Format("viewport.fov", "12") && Undos(ui) == u0, $"the popup's slider sets 12 ({Fv(ModelFreeFov(ui))}, readout «{fl.text}»), no entry");
        }
        Ev(c, new { type = "key", key = "Escape" });
        yield return Frames(2);
        bool escClosed = vp.FovPopup == null;
        if (!escClosed) vp.CloseFovPopup();
        c.Check(escClosed && ui.Shell.Overlay.Q("viewport-fov-blocker") == null, "Esc closes the popup (and its click blocker)");
        Ev(c, new { type = "pointer", target = "viewport-fov", phase = "down", clicks = 1 });
        Ev(c, new { type = "pointer", target = "viewport-fov", phase = "up", clicks = 1 });
        Ev(c, new { type = "pointer", target = "viewport-fov", phase = "down", clicks = 2 });
        Ev(c, new { type = "pointer", target = "viewport-fov", phase = "up", clicks = 2 });
        yield return Settle(c);
        c.Check(Mathf.Abs(ModelFreeFov(ui) - 23f) < 1e-4f && vp.FovPopup == null, $"a double click on the readout resets to 23 ({Fv(ModelFreeFov(ui))}) and leaves no popup open");
        vp.CloseFovPopup();

        // the Camera menu's rows with the key map's shortcut texts
        yield return OpenCameraMenu(c);
        var rows = new[] { ActionIds.CameraFovUp, ActionIds.CameraFovDown, ActionIds.CameraFovReset };
        var found = rows.Select(id => (id, row: MenuRow(c, id))).ToList();
        string Sc(VisualElement r) => r?.Q<Label>(className: "hs-menu__shortcut")?.text;
        string Tx(VisualElement r) => r?.Q<Label>(className: "hs-menu__text")?.text;
        c.Check(found.All(f => f.row != null && Tx(f.row) == L10n.Get(ActionIds.LabelKey(f.id)) && Sc(f.row) == ui.Keys.ChordText(f.id) && !f.row.ClassListContains("hs-menu__item--disabled")),
            "the Camera menu has " + string.Join(", ", found.Select(f => $"«{Tx(f.row)}» [{Sc(f.row)}]")) + " (default key map)");
        ui.Shell.Menus.CloseAll();
        yield return ClickMenuRow(c, ActionIds.CameraFovUp); yield return Settle(c); float m1 = ModelFreeFov(ui);
        yield return ClickMenuRow(c, ActionIds.CameraFovDown); yield return ClickMenuRow(c, ActionIds.CameraFovDown); yield return Settle(c); float m2 = ModelFreeFov(ui);
        yield return ClickMenuRow(c, ActionIds.CameraFovReset); yield return Settle(c); float m3 = ModelFreeFov(ui);
        c.Check(Mathf.Abs(m1 - 28f) < 1e-4f && Mathf.Abs(m2 - 18f) < 1e-4f && Mathf.Abs(m3 - 23f) < 1e-4f && Undos(ui) == u0,
            $"the menu rows: widen 23 → {Fv(m1)}, narrow twice → {Fv(m2)}, reset → {Fv(m3)} ({Fv(ViewFov.MenuStep)}° a row), no entry");
        cc.SetFov(40); yield return Settle(c);
        bool ran = ui.Registry.RunWith(ActionIds.CameraFovReset, new ActionInvocation { Source = "test" }, out _) == ActionRunStatus.Ran;
        yield return Settle(c);
        c.Check(ran && Mathf.Abs(ModelFreeFov(ui) - 23f) < 1e-4f && ui.Keys.Map.ChordsOf(ActionIds.CameraFovReset).Any(ch => ch.ToString() == "Ctrl+Home"),
            $"camera.fovReset (bound to Ctrl+Home in the default key map) resets 40 → {Fv(ModelFreeFov(ui))}");
        // hs2studio: the rows show its keys
        ui.Options.Set(OptionDefs.KeyPreset, Keymap.Hs2StudioPreset);
        yield return Frames(3);
        yield return OpenCameraMenu(c);
        var hs = rows.Select(id => (id, sc: Sc(MenuRow(c, id)), want: ui.Keys.ChordText(id))).ToList();
        ui.Shell.Menus.CloseAll();
        vp.UpdateInfo(true);
        string tipH = fl.tooltip ?? "";
        c.Check(ui.Keys.PresetName == Keymap.Hs2StudioPreset && hs.All(h => h.want.Length > 0 && h.sc == h.want) && hs.All(h => tipH.Contains(h.want)),
            "hs2studio: the rows and the readout's tooltip show its keys " + string.Join(", ", hs.Select(h => h.id + " [" + h.sc + "]")));
        ui.Options.Set(OptionDefs.KeyPreset, Keymap.DefaultPreset);
        yield return Frames(3);

        // the key overlay lists the three actions with their keys
        var ko = KeyOverlay.Show(ui);
        yield return Frames(2);
        var search = ko?.Q<TextField>("keys-search");
        if (search != null) search.value = "fov";
        for (int i = 0; i < 20; i++) yield return PlayModeHarness.WaitFrames(1);
        var labels = ko?.Query<Label>("label").ToList().Where(l => Visible(l, ko)).Select(l => l.text).ToList() ?? new List<string>();
        var chords = ko?.Query<Label>("chords").ToList().Where(l => Visible(l, ko)).Select(l => l.text).ToList() ?? new List<string>();
        c.Check(ko != null && ko.RowCount == 3 && rows.All(id => labels.Contains(L10n.Get(ActionIds.LabelKey(id)))) && chords.Contains("Ctrl+Home"),
            $"the key overlay (help.keys) finds «fov»: {ko?.RowCount} actions ({string.Join(", ", labels)}; keys {string.Join(", ", chords)})");
        while (ui.Dialogs.IsModalOpen) ui.Dialogs.CloseCurrent();
        yield return Frames(2);

        // looking through a camera object: the row, the readout, the wheel and the menu edit its camera.fov
        var add = ui.Ops.AddCameraFromView(null, false, UiContext.User());
        string cam = add?.NewIds.FirstOrDefault();
        c.Check(add != null && add.Ok && cam != null && CamFov(ui, cam).HasValue, $"a camera object at the view (fov {CamFov(ui, cam ?? "")})");
        if (cam == null) yield break;
        ui.Ops.SetActiveCamera(cam, UiContext.User());
        yield return Settle(c);
        row.Refresh();
        float free0 = ModelFreeFov(ui);
        c.Check(ViewFov.Target(ui) == cam && Mathf.Abs((float)row.Slider.Value - 23f) < 1e-4f && (row.tooltip ?? "").Contains(L10n.Get("viewfov.tip.keysFreeOnly")),
            $"looking through it: the row shows its {row.Slider.Value}° and the tooltip says the keys change the free camera only");
        int u1 = Undos(ui);
        row.Slider.Begin();
        foreach (var v in new[] { 30.0, 35.0, 40.0 }) row.Slider.Raise(EditPhase.Preview, v);
        row.Slider.End();
        yield return Settle(c);
        c.Check(Mathf.Abs((CamFov(ui, cam) ?? -1) - 40f) < 1e-4f && Undos(ui) == u1 + 1 && TopLabel(ui) == OpLabels.Camera && Mathf.Abs(view.Camera.fieldOfView - 40f) < 1e-3f && Mathf.Abs(ModelFreeFov(ui) - free0) < 1e-5f,
            $"a drag (30, 35, 40) writes the camera's fov {CamFov(ui, cam)} in one entry «{TopLabel(ui)}» (+{Undos(ui) - u1}); the view shows {Fv(view.Camera.fieldOfView)}; the free camera keeps {Fv(ModelFreeFov(ui))}");
        ui.Registry.Run(ActionIds.Undo, "test");
        yield return Settle(c);
        float undone = CamFov(ui, cam) ?? -1;
        ui.Registry.Run(ActionIds.Redo, "test");
        yield return Settle(c);
        c.Check(Mathf.Abs(undone - 23f) < 1e-4f && Mathf.Abs((CamFov(ui, cam) ?? -1) - 40f) < 1e-4f, $"undo restores {Fv(undone)}, redo {CamFov(ui, cam)}");
        int u2 = Undos(ui);
        for (int i = 0; i < 3; i++) Ev(c, new { type = "wheel", target = "viewport-fov", delta = -3 });
        yield return Settle(c);
        float wc = CamFov(ui, cam) ?? -1;
        bool runOpen = vp.WheelRunOpen;
        int during = Undos(ui) - u2;
        var okRun = new bool[1];
        float t0 = Time.realtimeSinceStartup;
        yield return PlayModeHarness.WaitUntil(() => !vp.WheelRunOpen, 5, okRun);
        float idle = (Time.realtimeSinceStartup - t0) * 1000f;
        yield return Frames(1);
        c.Check(Mathf.Abs(wc - (40f - 3 * ViewFov.WheelStep)) < 1e-4f && runOpen && during == 0 && okRun[0] && Undos(ui) == u2 + 1 && TopLabel(ui) == OpLabels.Camera
                && Mathf.Abs((CamFov(ui, cam) ?? -1) - wc) < 1e-5f && vp.FovLabel.text == L10n.Format("viewport.fov", Fv(wc)),
            $"three wheel notches over the readout narrow the camera to {Fv(wc)}: one run (open during it, +{during}), written as one entry «{TopLabel(ui)}» (+{Undos(ui) - u2}) after a pause ({idle:0} ms; idle {ViewportHost.WheelRunIdleMs} ms); readout «{vp.FovLabel.text}»");
        // a click elsewhere ends a run at once (before the click is handled)
        int u2b = Undos(ui);
        Ev(c, new { type = "wheel", target = "viewport-fov", delta = 3 });
        yield return Frames(1);
        bool open2 = vp.WheelRunOpen;
        Ev(c, new { type = "pointer", target = "cameras-fov-label" });
        yield return Frames(2);
        c.Check(open2 && !vp.WheelRunOpen && Undos(ui) == u2b + 1 && Mathf.Abs((CamFov(ui, cam) ?? -1) - (wc + ViewFov.WheelStep)) < 1e-4f,
            $"a click elsewhere ends a run at once: one entry (+{Undos(ui) - u2b}), fov {CamFov(ui, cam)}");
        ui.Registry.Run(ActionIds.Undo, "test");
        yield return Settle(c);
        c.Check(Mathf.Abs((CamFov(ui, cam) ?? -1) - wc) < 1e-4f, $"undo takes the run back ({CamFov(ui, cam)})");
        int u3 = Undos(ui);
        yield return ClickMenuRow(c, ActionIds.CameraFovUp);
        yield return Settle(c);
        c.Check(Mathf.Abs((CamFov(ui, cam) ?? -1) - (wc + ViewFov.MenuStep)) < 1e-4f && Undos(ui) == u3 + 1, $"the menu's «widen» on the camera: {CamFov(ui, cam)} (+{Undos(ui) - u3} entry)");
        float before = CamFov(ui, cam) ?? -1;
        ui.Registry.RunWith(ActionIds.CameraFovUp, new ActionInvocation { Source = "test", DeltaTime = 1, Speed = 1 }, out _);
        ui.Registry.RunWith(ActionIds.CameraFovReset, new ActionInvocation { Source = "test" }, out _);
        yield return Settle(c);
        c.Check(Mathf.Abs((CamFov(ui, cam) ?? -1) - before) < 1e-5f && Mathf.Abs(ModelFreeFov(ui) - free0) < 1e-5f, $"the keys leave the camera ({CamFov(ui, cam)}) and the free camera ({Fv(ModelFreeFov(ui))}) alone while the view looks through it");
        row.Refresh();
        row.Slider.Raise(EditPhase.Typed, 99); yield return Settle(c); float tHi = CamFov(ui, cam) ?? -1;
        row.Slider.Raise(EditPhase.Typed, -3); yield return Settle(c); float tLo = CamFov(ui, cam) ?? -1;
        ViewFov.Reset(ui); yield return Settle(c); float tRe = CamFov(ui, cam) ?? -1;
        c.Check(Mathf.Abs(tHi - 60f) < 1e-4f && Mathf.Abs(tLo - 1f) < 1e-4f && Mathf.Abs(tRe - 23f) < 1e-4f, $"the camera's values stay in [1, 60] (typed 99 → {Fv(tHi)}, −3 → {Fv(tLo)}) and reset gives {Fv(tRe)}");
        ui.Ops.SetActiveCamera(null, UiContext.User());
        yield return Settle(c);
        c.Check(ViewFov.Target(ui) == null && Mathf.Abs(view.Camera.fieldOfView - free0) < 1e-3f, $"back to the free camera at {Fv(view.Camera.fieldOfView)}°");
        vp.CloseFovPopup();
        if (preset0 != ui.Keys.PresetName) ui.Options.Set(OptionDefs.KeyPreset, preset0);
        ui.Ops.NewScene();
        yield return Frames(2);
    }

    static bool Refreshed(FovRow row, float want) { row.Refresh(); return Mathf.Abs((float)row.Slider.Value - want) < 1e-4f; }

    // ---------------- WR: empty-state and hint texts wrap

    static readonly string[] WrapClasses = { "hs-panel__empty", "hs-hint", "hs-inspector__note", "hs-browser__hint" };

    /// <summary>A label's text measured against its box: the box inside the panel's content box, and a wrapping label's
    /// height holding its wrapped text; a non-wrapping one's single line within its content width.</summary>
    static string WrapProblem(Label l, VisualElement panel)
    {
        if (l == null || panel == null) return "missing";
        var rs = l.resolvedStyle;
        float cw = ContentWidth(l);
        var pb = panel.worldBound;
        float pl = panel.resolvedStyle.paddingLeft, pr = panel.resolvedStyle.paddingRight;
        var b = l.worldBound;
        if (b.xMin < pb.xMin + pl - 0.5f || b.xMax > pb.xMax - pr + 0.5f) return $"box {b.xMin:0.#}–{b.xMax:0.#} outside the panel's content {pb.xMin + pl:0.#}–{pb.xMax - pr:0.#}";
        bool wraps = rs.whiteSpace == WhiteSpace.Normal || rs.whiteSpace == WhiteSpace.PreWrap;
        float one = TextWidth(l, l.text);
        if (!wraps) return one > cw + 0.5f ? $"no wrap: a {one:0.#} px line in {cw:0.#} px" : null;
        float h = l.MeasureTextSize(l.text, Mathf.Max(1, cw), VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined).y;
        return h > ContentHeight(l) + 1f ? $"wrapped height {h:0.#} > {ContentHeight(l):0.#}" : null;
    }

    static int LineCount(Label l)
    {
        float one = l.MeasureTextSize("Ag", 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).y;
        float h = l.MeasureTextSize(l.text, Mathf.Max(1, ContentWidth(l)), VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined).y;
        return one > 0 ? Mathf.Max(1, Mathf.RoundToInt(h / one)) : 1;
    }

    static bool Visible(VisualElement e, VisualElement stop)
    {
        for (var v = e; v != null; v = v.hierarchy.parent)
        {
            if (v.resolvedStyle.display == DisplayStyle.None || v.resolvedStyle.visibility == Visibility.Hidden) return false;
            if (v == stop) break;
        }
        return e.worldBound.width > 0.5f;
    }

    static bool ClippedByAncestor(VisualElement e, VisualElement stop)
    {
        for (var v = e; v != null && v != stop; v = v.hierarchy.parent)
            if (Clips(v)) return true;
        return false;
    }

    static IEnumerator WrapGroup(Ctx c)
    {
        var ui = c.Ui;
        while (ui.Dialogs.IsModalOpen) ui.Dialogs.CloseCurrent();
        ui.Ops.NewScene();
        ui.Ops.Select(Array.Empty<string>());
        yield return Frames(3);
        string lang0 = L10n.Language;
        string region = ui.Shell.RegionOf(CameraListPanel.PanelId) ?? LayoutRegions.Right;
        var reg = ui.Shell.Regions[region];
        float size0 = reg.Pane.worldBound.width;
        foreach (var lang in new[] { "en", "ja", "zh-Hans" })
        {
            c.Host.SetLanguage(lang, save: false);
            yield return Frames(3);
            ui.Shell.ShowPanel(CameraListPanel.PanelId);
            yield return Frames(3);
            var root = ui.Shell.PanelRoot(CameraListPanel.PanelId);
            var p = root?.Q<CameraListPanel>();
            c.Check(p != null && p.Ids.Count == 0 && p.Empty.resolvedStyle.display == DisplayStyle.Flex && p.Empty.text == L10n.Get("cameras.none"),
                $"{lang}: an empty scene shows «{p?.Empty.text}»");
            if (p == null) continue;
            foreach (var w in new float?[] { null, 300f, 247f })
            {
                if (w == 300f) ui.Shell.SetSize(reg, 300);
                if (w == 247f) root.style.width = 247;
                yield return Frames(4);
                float pw = p.worldBound.width;
                string e1 = WrapProblem(p.Empty, p), e2 = WrapProblem(p.Status, p);
                float single = TextWidth(p.Empty, p.Empty.text);
                c.Check(e1 == null && p.Empty.resolvedStyle.whiteSpace == WhiteSpace.Normal,
                    $"{lang}, panel {pw:0} px: «{L10n.Get("cameras.none")}» wraps inside it ({LineCount(p.Empty)} line(s); one line would be {single:0} px of {ContentWidth(p.Empty):0})" + (e1 != null ? "; " + e1 : ""));
                c.Check(e2 == null, $"{lang}, panel {pw:0} px: the status line «{p.Status.text}» fits ({LineCount(p.Status)} line(s))" + (e2 != null ? "; " + e2 : ""));
                // the field-of-view row: label, slider, number field and ↺ side by side inside the panel, the slider usable
                var fs = p.Fov.Slider;
                var parts = new VisualElement[] { fs.LabelElement, fs.Slider, fs.Field, fs.ResetButton };
                var pc = p.worldBound;
                bool inside = parts.All(x => x != null && x.worldBound.xMin >= pc.xMin - 0.5f && x.worldBound.xMax <= pc.xMax + 0.5f);
                bool oneLine = parts.All(x => x != null && Mathf.Abs(x.worldBound.center.y - fs.Slider.worldBound.center.y) < 4f);
                c.Check(inside && oneLine && fs.Slider.worldBound.width >= 40f && TextWidth(fs.LabelElement, fs.LabelElement.text) <= ContentWidth(fs.LabelElement) + 0.5f,
                    $"{lang}, panel {pw:0} px: the «{fs.LabelElement.text}» row on one line inside it (label {fs.LabelElement.worldBound.width:0}, slider {fs.Slider.worldBound.width:0}, field {fs.Field.worldBound.width:0}, ↺ {fs.ResetButton.worldBound.width:0} px)");
            }
            root.style.width = StyleKeyword.Null;
            ui.Shell.SetSize(reg, size0);
            yield return Frames(3);
        }
        // every panel of the registry at 300 and 247 px, in ja (the CJK texts are the widest per character; en next)
        foreach (var lang in new[] { "ja", "en" })
        {
            c.Host.SetLanguage(lang, save: false);
            yield return Frames(3);
            var bad = new List<string>();
            var info = new List<string>();
            int measured = 0;
            foreach (var def in ui.Registry.Panels.All.ToList())
            {
                if (!ui.Shell.ShowPanel(def.Id)) continue;
                yield return Frames(3);
                var root = ui.Shell.PanelRoot(def.Id);
                if (root == null) continue;
                string rid = ui.Shell.RegionOf(def.Id);
                var r = rid != null && ui.Shell.Regions.TryGetValue(rid, out var rr) ? rr : null;
                float rs0 = r?.Pane.worldBound.width ?? 0;
                foreach (float w in new[] { 300f, 247f })
                {
                    if (r != null && w == 300f) ui.Shell.SetSize(r, 300);
                    if (w == 247f) root.style.width = 247;
                    yield return Frames(4);
                    foreach (var l in root.Query<Label>().ToList())
                    {
                        if (string.IsNullOrEmpty(l.text) || !Visible(l, root)) continue;
                        bool wrapClass = WrapClasses.Any(l.ClassListContains);
                        if (!wrapClass && ClippedByAncestor(l, root)) continue;   // ellipsis / clipped rows are by design
                        string prob = wrapClass ? WrapProblem(l, root) : (TextWidth(l, l.text) > ContentWidth(l) + 0.5f && l.resolvedStyle.whiteSpace == WhiteSpace.NoWrap ? $"no wrap: a {TextWidth(l, l.text):0} px line in {ContentWidth(l):0} px" : null);
                        if (wrapClass) measured++;
                        if (prob == null) continue;
                        string what = $"{def.Id} {w:0} px: {(string.IsNullOrEmpty(l.name) ? "(unnamed)" : l.name)} [{string.Join(" ", l.GetClasses().Where(x => x.StartsWith("hs-", StringComparison.Ordinal)))}] {prob}";
                        (wrapClass ? bad : info).Add(what);
                    }
                }
                root.style.width = StyleKeyword.Null;
                if (r != null) ui.Shell.SetSize(r, rs0);
                yield return Frames(2);
            }
            foreach (var i in info.Distinct().Take(40)) c.Info($"WR {lang} a label wider than its box (not an empty-state or hint text): {i}");
            c.Check(measured > 0 && bad.Count == 0, $"{lang}: every empty-state and hint label of the {ui.Registry.Panels.All.Count} panels wraps inside its box at 300 and 247 px ({measured} measured)"
                + (bad.Count > 0 ? "; " + string.Join("; ", bad.Distinct().Take(12)) : ""));
        }
        c.Host.SetLanguage(lang0, save: false);
        ui.Shell.ShowPanel(CameraListPanel.PanelId);
        yield return Frames(2);
    }
}
