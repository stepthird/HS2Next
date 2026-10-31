// UiShellTest.play groups L, N, V, C, K, G, P, B, F, M (see UiShellTest.cs). Events are synthesised through the panel
// (UiCommands.Send, as ui.input does); expected values come from the formulas.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Bridge;
using HS2Next.UI.Core;
using HS2Next.UI.Gizmos;
using HS2Next.UI.Viewport;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

public static partial class UiShellTest
{
    static partial void AddMoreGroups()
    {
        Groups.Add(("L", Layout));
        Groups.Add(("N", Language));
        Groups.Add(("V", ViewportRect));
        Groups.Add(("K", Keys));
        Groups.Add(("C", CameraGroup));
        Groups.Add(("G", Gizmo));
        Groups.Add(("P", Picking));
        Groups.Add(("B", BridgeGroup));
        Groups.Add(("F", Fonts));
        Groups.Add(("U", V02Defects));   // UiShellTest_V02.cs (Phase 4.0 package U)
        Groups.Add(("TM", V03Measure));  // UiShellTest_V03.cs (v0.3 trial fixes): measurements
        Groups.Add(("T", V03Checks));    // UiShellTest_V03Checks.cs (v0.3 trial fixes): the checks
    }

    static void Ev(Ctx c, object o) => UiCommands.Send(c.Host, JObject.FromObject(o), out _);
    static void Pointer(Ctx c, string phase, Vector2 p, int button = 0, string[] mods = null) =>
        Ev(c, new { type = "pointer", phase, x = p.x, y = p.y, button, mods = mods ?? new string[0] });

    static bool Near(float a, float b, float tol) => Mathf.Abs(a - b) <= tol;

    // ---------------- L: layout

    static IEnumerator Layout(Ctx c)
    {
        var ui = c.Ui;
        var sh = ui.Shell;
        sh.SaveLayout();
        c.Check(File.Exists(sh.LayoutFile), "layout.json written (" + sh.LayoutFile + ")");
        var left = sh.Regions[LayoutRegions.Left];
        sh.SetSize(left, 333);
        yield return PlayModeHarness.WaitFrames(1); yield return PlayModeHarness.WaitFrames(1);
        sh.SaveLayout();
        var doc = LayoutDocument.Parse(File.ReadAllText(sh.LayoutFile));
        c.Check(doc != null && Math.Abs(doc.Region(LayoutRegions.Left).Size - 333) < 1.5, "left size 333 saved (" + doc?.Region(LayoutRegions.Left).Size + ")");
        sh.SetCollapsed(left, true, true);
        yield return PlayModeHarness.WaitFrames(1);
        c.Check(left.Collapsed && sh.Layout.Region(LayoutRegions.Left).Collapsed, "left collapsed");
        sh.TogglePanels();
        yield return PlayModeHarness.WaitFrames(1);
        bool allCollapsed = sh.Regions.Values.Where(r => r.Panels.Count > 0).All(r => r.Collapsed);
        sh.TogglePanels();
        yield return PlayModeHarness.WaitFrames(1);
        bool allOpen = sh.Regions.Values.Where(r => r.Panels.Count > 0).All(r => !r.Collapsed);
        c.Check(allCollapsed && allOpen, "view.panels collapses all, then restores all (" + allCollapsed + ", " + allOpen + ")");
        sh.ResetLayout();
        yield return PlayModeHarness.WaitFrames(1); yield return PlayModeHarness.WaitFrames(1);
        c.Check(Math.Abs(sh.Layout.Region(LayoutRegions.Left).Size - 360) < 0.1 && !sh.Regions[LayoutRegions.Left].Collapsed, "reset layout: defaults (left 360, open)");
        // an unreadable layout file gives defaults + ui.layoutInvalid at the next start (parse check here)
        var bad = LayoutDocument.Parse("{ not json", new List<string>());
        c.Check(bad == null, "unreadable layout.json → null (defaults used)");
    }

    // ---------------- N: language

    static IEnumerator Language(Ctx c)
    {
        var ui = c.Ui;
        var file = c.Host.Root.Q<Button>("tb-file");
        string en = file?.text;
        c.Host.SetLanguage("ja", save: false);
        yield return PlayModeHarness.WaitFrames(1); yield return PlayModeHarness.WaitFrames(1);
        string ja = file?.text;
        c.Check(en == "File" && ja == "ファイル", $"toolbar File: en '{en}' → ja '{ja}' ({c.Host.LastLanguageSwitchMs:0.0} ms)");
        c.Check(ui.Fonts.Primary == ui.Fonts.JpRegular, "ja primary font = Noto Sans JP");
        var status = c.Host.Root.Q<Label>("status-selection");
        c.Check(status != null && status.text == "選択なし", "status bar in ja (" + status?.text + ")");
        c.Host.SetLanguage("zh-Hans", save: false);
        yield return PlayModeHarness.WaitFrames(1); yield return PlayModeHarness.WaitFrames(1);
        c.Check(file?.text == "文件" && ui.Fonts.Primary == ui.Fonts.ScRegular, $"zh-Hans: '{file?.text}', primary SC ({c.Host.LastLanguageSwitchMs:0.0} ms)");
        c.Check(c.Host.LastLanguageSwitchMs <= 200, "language switch ≤ 200 ms (" + c.Host.LastLanguageSwitchMs.ToString("0.0") + ")");
        c.Host.SetLanguage("en", save: false);
        yield return PlayModeHarness.WaitFrames(1);
        c.Check(file?.text == "File", "back to en");
    }

    // ---------------- V: viewport rect

    static IEnumerator ViewportRect(Ctx c)
    {
        var vp = c.Ui.Viewport;
        var cam = vp.ViewCamera;
        c.Check(cam != null, "view camera");
        if (!cam) yield break;
        for (int k = 0; k < 3; k++)
        {
            if (k == 1) c.Ui.Shell.SetSize(c.Ui.Shell.Regions[LayoutRegions.Left], 320);
            if (k == 2) c.Ui.Shell.SetSize(c.Ui.Shell.Regions[LayoutRegions.Right], 320);
            // a layout rebuild settles within a few frames (the split views set themselves up on their first geometry)
            int frames = 0;
            bool ok = false;
            Rect wb = default, pr = default;
            float s = 1;
            for (; frames < 30 && !ok; frames++)
            {
                yield return PlayModeHarness.WaitFrames(1);
                wb = vp.Element.worldBound;
                s = vp.PanelScale;
                pr = cam.pixelRect;
                ok = c.Ui.Shell.SizeOf(c.Ui.Shell.Regions[LayoutRegions.Left]) > 0 && Near(pr.x, wb.x * s, 1) && Near(pr.width, wb.width * s, 1) && Near(pr.y, Screen.height - wb.yMax * s, 1) && Near(pr.height, wb.height * s, 1);
            }
            c.Info("settled after " + frames + " frames");
            c.Check(ok, $"step {k}: camera pixel rect {pr} = element {wb} × {s} (±1 px); left {c.Ui.Shell.SizeOf(c.Ui.Shell.Regions[LayoutRegions.Left])}, right {c.Ui.Shell.SizeOf(c.Ui.Shell.Regions[LayoutRegions.Right])}");
        }
        c.Ui.Shell.ResetLayout();
        for (int i = 0; i < 6; i++) yield return PlayModeHarness.WaitFrames(1);
        c.Info($"after reset: viewport {vp.Element.worldBound}, left {c.Ui.Shell.SizeOf(c.Ui.Shell.Regions[LayoutRegions.Left])}, right {c.Ui.Shell.SizeOf(c.Ui.Shell.Regions[LayoutRegions.Right])}, bottom {c.Ui.Shell.SizeOf(c.Ui.Shell.Regions[LayoutRegions.Bottom])}");
    }

    // ---------------- K: keys

    static IEnumerator Keys(Ctx c)
    {
        var ui = c.Ui;
        var r = c.Host.Router;
        ui.Viewport.Element.Focus();
        yield return PlayModeHarness.WaitFrames(1);
        Ev(c, new { type = "key", key = "E" });
        yield return PlayModeHarness.WaitFrames(1);
        c.Check(ui.Tool == GizmoTool.Rotate, "E (viewport focus) → tool.rotate (" + ui.Tool + ")");
        Ev(c, new { type = "key", key = "W" });
        yield return PlayModeHarness.WaitFrames(1);
        c.Check(ui.Tool == GizmoTool.Move, "W → tool.move");
        // a text field blocks shortcuts
        var tf = new TextField { name = "test-text" };
        ui.Shell.Overlay.Add(tf);
        tf.Focus();
        yield return PlayModeHarness.WaitFrames(1);
        Ev(c, new { type = "key", key = "E" });
        Ev(c, new { type = "text", text = "abc" });
        yield return PlayModeHarness.WaitFrames(1);
        c.Check(ui.Tool == GizmoTool.Move && tf.value.Contains("abc"), "text focus: E blocked, typing goes to the field ('" + tf.value + "', tool " + ui.Tool + ")");
        int undoBefore = ui.Session.Commands.UndoList.Count;
        Ev(c, new { type = "key", key = "Z", mods = new[] { "ctrl" } });
        yield return PlayModeHarness.WaitFrames(1);
        c.Check(r.Log.Last().Contains("None") || r.Log.Last().Contains("text"), "Ctrl+Z in a field: no edit.undo (" + r.Log.Last() + ")");
        Ev(c, new { type = "key", key = "Escape" });
        yield return PlayModeHarness.WaitFrames(1);
        c.Check(!ShortcutFocusText(c), "Esc blurs the field");
        tf.RemoveFromHierarchy();
        // exact modifiers: Shift+W is not tool.move
        ui.Tool = GizmoTool.Rotate;
        ui.Viewport.Element.Focus();
        r.Handle("W", KeyMods.Shift, false);
        c.Check(ui.Tool == GizmoTool.Rotate, "Shift+W ≠ W (exact modifiers)");
        r.Handle("W", KeyMods.None, false);
        // held keys: the camera moves while ← is held with the viewport focused (scripted input source, ten 0.1 s polls)
        var src = new HS2Next.UI.Input.ScriptedInputSource();
        var real = r.Source;
        r.Source = src;
        var vpc = ui.Viewport.Element.worldBound.center;
        src.Mouse = new Vector3(vpc.x * ui.Viewport.PanelScale, Screen.height - vpc.y * ui.Viewport.PanelScale, 0);
        ui.Viewport.Element.Focus();
        yield return PlayModeHarness.WaitFrames(1); yield return PlayModeHarness.WaitFrames(1);
        ui.Viewport.Camera.Flush();
        var before = ui.Viewport.Camera.Current;
        src.Down.Add(KeyCode.LeftArrow);
        for (int i = 0; i < 10; i++) r.Poll(0.1f);
        src.Down.Clear();
        var after = ui.Viewport.Camera.Current;
        ui.Viewport.Camera.Flush();
        float moved = (new Vector3(after.Pivot.X, after.Pivot.Y, after.Pivot.Z) - new Vector3(before.Pivot.X, before.Pivot.Y, before.Pivot.Z)).magnitude;
        c.Check(Near(moved, 1f, 0.01f), $"← held 1 s (10 × 0.1 s polls) moves the pivot 1 unit ({moved:0.000}; held {string.Join(",", r.LastHeld)}, over {ui.Viewport.PointerOver})");
        r.Source = real;
        // JIS: UI Toolkit Equals = the legacy Semicolon key
        c.Check(KeyNames.FromUiToolkit("Equals", KeyboardLayout.Jis) == "Semicolon" && KeyNames.FromUiToolkit("Equals", KeyboardLayout.Us) == "Equals", "JIS mapping via KeyNames.FromUiToolkit");
        c.Info("keyboard layout detected: " + ui.Keys.Layout);
    }

    static bool ShortcutFocusText(Ctx c) => HS2Next.UI.Input.ShortcutRouter.IsText(c.Host.Router.Focused);

    // ---------------- C: camera controller

    static IEnumerator CameraGroup(Ctx c)
    {
        var ui = c.Ui;
        var cc = ui.Viewport.Camera;
        cc.Reset();
        cc.Flush();
        yield return PlayModeHarness.WaitFrames(1); yield return PlayModeHarness.WaitFrames(1);
        var center = ui.Viewport.Element.worldBound.center;
        var v0 = cc.Current;
        // left drag 40 px right, 20 px down: yaw +10°, pitch +5° (0.25 °/px at speeds 1)
        Pointer(c, "down", center);
        Pointer(c, "move", center + new Vector2(20, 10));
        Pointer(c, "move", center + new Vector2(40, 20));
        Pointer(c, "up", center + new Vector2(40, 20));
        cc.Flush();
        yield return PlayModeHarness.WaitFrames(1);
        var v1 = cc.Current;
        c.Check(Near(v1.Rot.Y - v0.Rot.Y, 10f, 0.01f) && Near(v1.Rot.X - v0.Rot.X, 5f, 0.01f), $"left drag (40, 20) px → yaw +{v1.Rot.Y - v0.Rot.Y:0.###}, pitch +{v1.Rot.X - v0.Rot.X:0.###} (10, 5)");
        c.Check(ui.Selection.Count == 0, "a camera drag is not a click (selection unchanged)");
        // right drag 30 px right: distance += 30 × 0.0025 × max(d, 1)
        float d0 = v1.Distance;
        Pointer(c, "down", center, 1);
        Pointer(c, "move", center + new Vector2(30, 0), 1);
        Pointer(c, "up", center + new Vector2(30, 0), 1);
        cc.Flush();
        yield return PlayModeHarness.WaitFrames(1);
        var v2 = cc.Current;
        float expect = d0 + 30 * 0.0025f * Mathf.Max(d0, 1);
        c.Check(Near(v2.Distance, expect, 1e-3f), $"right drag 30 px → distance {v2.Distance:0.####} (expected {expect:0.####})");
        // middle drag pans in the screen plane by 0.0025 × d per px
        Pointer(c, "down", center, 2);
        Pointer(c, "move", center + new Vector2(0, 40), 2);
        Pointer(c, "up", center + new Vector2(0, 40), 2);
        cc.Flush();
        yield return PlayModeHarness.WaitFrames(1);
        var v3 = cc.Current;
        float pan = (new Vector3(v3.Pivot.X, v3.Pivot.Y, v3.Pivot.Z) - new Vector3(v2.Pivot.X, v2.Pivot.Y, v2.Pivot.Z)).magnitude;
        float pexp = 40 * 0.0025f * Mathf.Max(v2.Distance, 1);
        c.Check(Near(pan, pexp, 1e-3f), $"middle drag 40 px → pan {pan:0.####} (expected {pexp:0.####})");
        // wheel: one notch in → d / 1.1
        Ev(c, new { type = "wheel", x = center.x, y = center.y, delta = -3 });
        cc.Flush();
        yield return PlayModeHarness.WaitFrames(1);
        var v4 = cc.Current;
        c.Check(Near(v4.Distance, v3.Distance / 1.1f, 1e-3f), $"wheel 1 notch → distance {v4.Distance:0.####} (expected {v3.Distance / 1.1f:0.####})");
        // one write per frame at most, and no history entry
        c.Check(ui.Session.Commands.UndoList.Count == 0 || ui.Session.Commands.UndoList[0].Label != "Camera", "camera moves are not recorded");
        cc.Reset();
        cc.Flush();
        yield return PlayModeHarness.WaitFrames(1);
    }

    // ---------------- G: gizmo drags

    static IEnumerator Gizmo(Ctx c)
    {
        var ui = c.Ui;
        ui.Ops.NewScene();
        var r = ui.Ops.AddFolder(null, null, -1, UiContext.User());
        string id = r.NewIds.FirstOrDefault();
        c.Check(r.Ok && id != null, "folder added");
        if (id == null) yield break;
        ui.Ops.SetTransform(new[] { id }, Vector3.zero, Vector3.zero, null, UiContext.User());
        ui.Ops.Select(new[] { id });
        var cc = ui.Viewport.Camera;
        cc.SetView(Vector3.zero, new Vector3(20, 30, 0), 10, 23);
        cc.Flush();
        for (int i = 0; i < 5; i++) yield return PlayModeHarness.WaitFrames(1);
        var vcam = ui.Viewport.ViewCamera;
        c.Info($"view: pivot y {cc.Current.Pivot.Y}, d {cc.Current.Distance}, rx {cc.Current.Rot.X}, camera at {vcam.transform.position} rot {vcam.transform.eulerAngles}, enabled {cc.Enabled}, writes {cc.Writes}");
        var g = ui.Viewport.Gizmo;
        ui.Tool = GizmoTool.Move;
        ui.Space = GizmoSpace.Auto;
        ui.Snap = false;
        var holder = ui.Runtime.HolderOf(id);
        c.Check(holder != null, "folder holder realized");
        if (!holder) yield break;
        float L = ui.Options.Int(OptionDefs.GizmoSize) * g.WorldPerPx(holder.position);
        // move X: grab at 60 % of the arrow, drag 60 px along the projected axis
        Vector2 a0 = g.ToPanel(holder.position), aT = g.ToPanel(holder.position + Vector3.right * L);
        Vector2 dir = (aT - a0).normalized;
        Vector2 grab = Vector2.Lerp(a0, aT, 0.6f);
        var hit = g.HitTest(grab);
        c.Check(hit.Kind == HandleKind.MoveAxis && hit.Axis == 0, "hit test at the X arrow → " + hit);
        int u0 = ui.Session.Commands.UndoList.Count;
        Vector2 end = grab + dir * 60;
        var picked = c.Host.Root.panel.Pick(grab);
        c.Info($"grab {grab}, viewport {ui.Viewport.Element.worldBound}, picked {picked?.name} ({picked?.GetType().Name}), camera rect {ui.Viewport.ViewCamera.rect}, pixel {ui.Viewport.ViewCamera.pixelRect}, screen {Screen.width}x{Screen.height}, scale {ui.Viewport.PanelScale}");
        Pointer(c, "down", grab);
        c.Info($"after down: dragging {g.Dragging}, active {g.ActiveHandle}, tx {(ui.Session.Commands.ActiveTransaction != null ? ui.Session.Commands.ActiveTransaction.Label : "none")}, toasts [{string.Join(" | ", ui.Toasts.History.Skip(Math.Max(0, ui.Toasts.History.Count - 3)))}], focus {c.Host.Router.Focused?.name}");
        Pointer(c, "move", grab + dir * 30);
        Pointer(c, "move", end);
        float midX = ui.Model.Find(id).Doc.Transform.Pos.X;
        Pointer(c, "up", end);
        yield return PlayModeHarness.WaitFrames(1);
        // expected: the axis parameter under the cursor at the end minus the one at the grab point
        var rayA = ui.Viewport.RayAt(grab);
        var rayB = ui.Viewport.RayAt(end);
        float exp = AxisT(rayB, Vector3.zero, Vector3.right) - AxisT(rayA, Vector3.zero, Vector3.right);
        var pos = ui.Model.Find(id).Doc.Transform.Pos;
        c.Check(Near(pos.X, exp, 1e-4f) && Near(pos.Y, 0, 1e-5f) && Near(pos.Z, 0, 1e-5f), $"move X drag → pos ({pos.X:0.#####}, {pos.Y}, {pos.Z}), expected x {exp:0.#####} (preview mid {midX:0.###})");
        c.Check(ui.Session.Commands.UndoList.Count == u0 + 1 && ui.Session.Commands.UndoList[0].Label == "Transform", "one entry \"Transform\" (" + (ui.Session.Commands.UndoList.Count - u0) + ", " + ui.Session.Commands.UndoList.FirstOrDefault()?.Label + ")");
        // Esc cancels a drag
        yield return PlayModeHarness.WaitFrames(1);
        var p0 = ui.Model.Find(id).Doc.Transform.Pos;
        a0 = g.ToPanel(holder.position); aT = g.ToPanel(holder.position + Vector3.up * L);
        grab = Vector2.Lerp(a0, aT, 0.6f);
        Pointer(c, "down", grab);
        Pointer(c, "move", grab + (aT - a0).normalized * 40);
        bool moving = ui.Model.Find(id).Doc.Transform.Pos.Y != p0.Y;
        Ev(c, new { type = "key", key = "Escape" });
        Pointer(c, "up", grab + (aT - a0).normalized * 40);
        yield return PlayModeHarness.WaitFrames(1);
        var p1 = ui.Model.Find(id).Doc.Transform.Pos;
        c.Check(moving && p1.Y == p0.Y && ui.Session.Commands.UndoList.Count == u0 + 1, $"Esc cancels a Y drag (moved during: {moving}, after: {p1.Y}, entries +{ui.Session.Commands.UndoList.Count - u0})");
        // rotate Y ring (horizontal) by dragging along the ring
        ui.Tool = GizmoTool.Rotate;
        yield return PlayModeHarness.WaitFrames(1);
        float ang = 200 * Mathf.Deg2Rad;   // a front point of the ring (camera at yaw 30: the ring's near side)
        var ringPt = holder.position + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * L;
        var front = g.ToPanel(holder.position + Quaternion.Euler(0, 30, 0) * Vector3.back * L);
        Vector2 rp = g.ToPanel(holder.position + (Quaternion.Euler(0, 30, 0) * Vector3.back) * L);
        hit = g.HitTest(rp);
        c.Check(hit.Kind == HandleKind.RotateRing && hit.Axis == 1, "hit test on the Y ring's near point → " + hit);
        var rot0 = ui.Model.Find(id).Doc.Transform.Rot;
        Vector2 rEnd = rp + new Vector2(40, 0);
        Pointer(c, "down", rp);
        Pointer(c, "move", rEnd);
        Pointer(c, "up", rEnd);
        yield return PlayModeHarness.WaitFrames(1);
        PlaneHit(ui.Viewport.RayAt(rp), holder.position, Vector3.up, out var h0);
        PlaneHit(ui.Viewport.RayAt(rEnd), holder.position, Vector3.up, out var h1);
        float expAng = Vector3.SignedAngle(h0 - holder.position, h1 - holder.position, Vector3.up);
        var rot1 = ui.Model.Find(id).Doc.Transform.Rot;
        float got = Mathf.DeltaAngle(rot0.Y, rot1.Y);
        c.Check(Near(got, expAng, 0.01f), $"Y ring drag → rot.y {got:0.###}° (expected {expAng:0.###}°)");
        _ = ringPt; _ = front;
        ui.Tool = GizmoTool.Move;
        yield return PlayModeHarness.WaitFrames(1);
    }

    static float AxisT(Ray r, Vector3 p0, Vector3 a)
    {
        var w = r.origin - p0;
        float b = Vector3.Dot(r.direction, a), d = Vector3.Dot(w, a), e = Vector3.Dot(w, r.direction);
        float den = 1 - b * b;
        return Mathf.Abs(den) < 1e-6f ? d : (d - b * e) / den;
    }

    static bool PlaneHit(Ray r, Vector3 p0, Vector3 n, out Vector3 hit)
    {
        var pl = new Plane(n, p0);
        if (pl.Raycast(r, out float t)) { hit = r.GetPoint(t); return true; }
        hit = p0;
        return false;
    }

    // ---------------- P: picking (U-M4)

    static IEnumerator Picking(Ctx c)
    {
        var ui = c.Ui;
        ui.Ops.NewScene();
        var targets = TestTargets.Load();
        string card = TestTargets.Abs(targets.visual.female);
        var r = ui.Ops.AddChara(card, null, null, -1, null, UiContext.User());
        string id = r.NewIds.FirstOrDefault();
        c.Check(r.Ok && id != null, "BR-Chan added (" + r + ")");
        if (id == null) yield break;
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => ui.Runtime.FindChara(id)?.State == RealizationState.Built, 120, ok);
        c.Check(ok[0], "character built");
        var cc = ui.Viewport.Camera;
        cc.Frame(new[] { id });
        cc.Flush();
        yield return PlayModeHarness.WaitFrames(1); yield return PlayModeHarness.WaitFrames(1); yield return PlayModeHarness.WaitFrames(1);
        var b = NodeBounds.Of(ui, new[] { id }, out _);
        var g = ui.Viewport.Gizmo;
        Vector2 cp = g.ToPanel(b.center);
        var hit = ui.Viewport.Picker.Pick(ui.Viewport, cp);
        c.Check(hit.NodeId == id, $"pick at the character's centre → {hit.NodeId} (precise {hit.Precise}, {hit.Candidates} candidates, {hit.Ms:0.00} ms)");
        var wb = ui.Viewport.Element.worldBound;
        var empty = ui.Viewport.Picker.Pick(ui.Viewport, new Vector2(wb.xMin + 3, wb.yMin + 3));
        c.Check(empty.NodeId == null, "pick at the viewport corner → nothing (" + empty.NodeId + ")");
        // click rules
        ui.Viewport.Picker.Apply(id, KeyMods.None);
        c.Check(ui.Selection.Count == 1 && ui.Selection.Primary == id, "click selects the character");
        ui.Viewport.Picker.Apply(null, KeyMods.None);
        c.Check(ui.Selection.Count == 0, "click on empty space clears");
        // an item
        var ir = ui.Ops.AddItem(ContentId.Vanilla(821), null, null, -1, UiContext.User());
        string item = ir.NewIds.FirstOrDefault();
        if (item != null)
        {
            ui.Ops.SetTransform(new[] { item }, new Vector3(b.max.x + 3, b.center.y, b.center.z), null, null, UiContext.User());
            for (int i = 0; i < 30 && ui.Runtime.FindItem(item)?.State != RealizationState.Built; i++) yield return PlayModeHarness.WaitFrames(1);
            cc.Frame(new[] { item });
            cc.Flush();
            yield return PlayModeHarness.WaitFrames(1); yield return PlayModeHarness.WaitFrames(1);
            var ib = NodeBounds.Of(ui, new[] { item }, out bool anyItem);
            var ih = ui.Viewport.Picker.Pick(ui.Viewport, g.ToPanel(ib.center));
            c.Check(anyItem && ih.NodeId == item, $"pick at the item → {ih.NodeId} (precise {ih.Precise}, {ih.Ms:0.00} ms)");
        }
        else c.Warning("item 821 not added: " + ir);
        // U-M4: pick cost with 1 and 10 characters
        cc.Frame(new[] { id });
        cc.Flush();
        yield return PlayModeHarness.WaitFrames(1); yield return PlayModeHarness.WaitFrames(1);
        cp = g.ToPanel(NodeBounds.Of(ui, new[] { id }, out _).center);
        var t1 = new List<double>();
        for (int i = 0; i < 10; i++) t1.Add(ui.Viewport.Picker.Pick(ui.Viewport, cp).Ms);
        var dup = new List<string> { id };
        for (int i = 0; i < 9; i++)
        {
            var dr = ui.Ops.Duplicate(new[] { id }, UiContext.User());
            if (dr.Ok && dr.NewIds.Count > 0) { dup.Add(dr.NewIds[0]); ui.Ops.SetTransform(new[] { dr.NewIds[0] }, new Vector3((i % 3 - 1) * 0.3f, 0, -(i / 3 + 1) * 0.4f), null, null, UiContext.User()); }
        }
        yield return PlayModeHarness.WaitUntil(() => dup.All(x => ui.Runtime.FindChara(x)?.State == RealizationState.Built), 180, ok);
        yield return PlayModeHarness.WaitFrames(1); yield return PlayModeHarness.WaitFrames(1);
        var t10 = new List<double>();
        for (int i = 0; i < 10; i++) t10.Add(ui.Viewport.Picker.Pick(ui.Viewport, cp).Ms);
        t1.Sort(); t10.Sort();
        c.Info($"U-M4 pick ms: 1 character median {t1[5]:0.00} max {t1[9]:0.00}; {dup.Count} characters median {t10[5]:0.00} max {t10[9]:0.00} (target ≤ 30 ms)");
        c.Check(t10[5] <= 30, "pick ≤ 30 ms with 10 characters (median " + t10[5].ToString("0.00") + ")");
        ui.Ops.NewScene();
        yield return PlayModeHarness.WaitFrames(1);
    }

    // ---------------- B: ui.* through the bridge (in-process)

    static IEnumerator BridgeGroup(Ctx c)
    {
        var ui = c.Ui;
        // Execute returns a task completed by the pump; run frames until done
        IEnumerator Run(string json, string[] outp)
        {
            var t = c.App.Bridge.Execute(json);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 100000 && !t.IsCompleted && sw.Elapsed.TotalSeconds < 20; i++) yield return PlayModeHarness.WaitFrames(1);
            outp[0] = t.IsCompleted ? t.Result : "{\"ok\":false,\"code\":\"timeout\"}";
        }
        var o = new string[1];
        yield return Run("{\"cmd\":\"ui.state\"}", o);
        var j = JObject.Parse(o[0]);
        c.Check((bool?)j["ok"] == true && (bool?)j["enabled"] == true && (string)j["preset"] == "default", "ui.state: enabled, preset default (" + Trim(o[0]) + ")");
        yield return Run("{\"cmd\":\"ui.action\",\"action\":\"tool.scale\"}", o);
        c.Check(JObject.Parse(o[0])["ok"]?.Value<bool>() == true && ui.Tool == GizmoTool.Scale, "ui.action tool.scale → " + ui.Tool);
        yield return Run("{\"cmd\":\"ui.action\",\"action\":\"no.such\"}", o);
        c.Check((string)JObject.Parse(o[0])["code"] == "bad_args", "ui.action unknown id → bad_args (" + Trim(o[0]) + ")");
        yield return Run("{\"cmd\":\"ui.query\",\"name\":\"#tb-file\"}", o);
        j = JObject.Parse(o[0]);
        c.Check((string)j["elements"]?[0]?["text"] == "File" && (bool?)j["elements"]?[0]?["visible"] == true, "ui.query #tb-file → text File, visible");
        yield return Run("{\"cmd\":\"ui.input\",\"events\":[{\"type\":\"key\",\"key\":\"W\"}],\"frames\":1}", o);
        j = JObject.Parse(o[0]);
        c.Check((int?)j["sent"] == 1 && ui.Tool == GizmoTool.Move, "ui.input key W → tool move (" + Trim(o[0]) + ")");
        yield return Run("{\"cmd\":\"ui.panel\",\"panel\":\"history\",\"op\":\"focus\"}", o);
        c.Check((bool?)JObject.Parse(o[0])["panel"]?["active"] == true, "ui.panel history focus → active (" + Trim(o[0]) + ")");
    }

    static string Trim(string s) => s == null ? "" : s.Length > 240 ? s.Substring(0, 240) + "…" : s;

    // ---------------- F: font atlas (U-M5)

    static IEnumerator Fonts(Ctx c)
    {
        var ui = c.Ui;
        foreach (var (lang, fa) in new[] { ("ja", ui.Fonts.JpRegular), ("zh-Hans", ui.Fonts.ScRegular) })
        {
            if (!fa) { c.Check(false, lang + " font asset"); continue; }
            var sw = Stopwatch.StartNew();
            var cps = new List<uint>();
            for (uint cp = 0x4E00; cps.Count < 2000 && cp < 0x9FFF; cp++) cps.Add(cp);
            fa.TryAddCharacters(cps.ToArray(), out uint[] missing);
            double ms = sw.Elapsed.TotalMilliseconds;
            long bytes = UiFonts.AtlasBytes(fa);
            int pages = fa.atlasTextures?.Count(t => t && t.width > 1) ?? 0;
            c.Info($"U-M5 {lang}: 2,000 CJK code points (missing {missing?.Length ?? 0}) added in {ms:0} ms; atlas {pages} page(s), {bytes / 1048576.0:0.0} MB");
            c.Check(bytes <= 12L * 1048576 || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null, lang + " atlas ≤ 12 MB after 2,000 glyphs (" + (bytes / 1048576.0).ToString("0.0") + " MB)");
            yield return PlayModeHarness.WaitFrames(1);
        }
    }
}
