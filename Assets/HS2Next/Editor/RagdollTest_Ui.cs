// RagdollTest.play, the tool and the panel (Phase 6.7 package U): the app with the UI in Editor play, a body on BR-Chan, HERO107 beside it, input through ui.input and a pointer raised through
// the panel; judged by the tool, the world, the blocks, the history and the widgets; a picture stays in memory and is judged by numbers. No real pointer is involved here.
//   RY11  the panel appears when asked for: lazily registered, region bottom, the layout's version as it was, a «Reset layout» leaves the region collapsed
//   RI8   the new files draw by the tool's own drawing only
//   RI1   a press on a part claims it and the pointer moves the goal; a miss orbits, a click selects
//   RI2   the wheel: the depth of a grab, at least 1; with no grab the camera
//   RI3   Space, T, N during a grab: one entry each, the press stays with nothing; with nothing selected a note
//   RI4   Ctrl pushes along the ray, Shift throws anywhere and brings the world, a prop can be grabbed and takes no pin
//   RI5   Esc: the press first, then the tool; a pause holds the part while the goal moves
//   RI6   the overlays: wire lines on the colliders by kind and colour, in the view, never in a capture
//   RI7   the shoot mode: rate, spread, Alt, a hit, a stain by its switch
//   RI9   the Edit tab ends a running tool and refuses a new one
//   RY15  every row edits and goes back, a drag is one entry, Esc takes it back; the room's sliders write when they stop; the buttons; pins, ties, holds
//   RY12  the section of the State tab
//   RY13  a closed panel and a closed section read nothing
//   RY14  the blood option round trip: the Options row, the panel's switch, options.json, a shot
//   RY16  the panel at widths 360 and 300 in the three languages
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Api.Unity;
using HS2Next.App;
using HS2Next.App.Commands;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Ragdoll;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Gizmos;
using HS2Next.UI.Panels;
using HS2Next.UI.Viewport;
using HS2Next.UI.Widgets;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class RagdollTest
{
    static AppCore UApp => AppCore.Current;
    static UiHost UHost => UiHost.Current;
    static UiContext UUi => UHost?.Context;
    static ToolHost UTools => UUi.Tools;
    static ViewportHost UVp => UUi.Viewport;
    static IPanel UPanel => UHost?.Root?.panel;
    static SceneRuntime URt => UApp.Runtime;
    static RagdollPart UPart => URt.GetPart<RagdollPart>();
    static RagdollWorld UWorld => UPart.World;
    static RagdollPanel UP => RagdollPanel.Current;
    static RagdollTool UTool => UTools.Current as RagdollTool;
    static int UUndos => UUi.Session.Commands.UndoList.Count;
    static string UTop => UUi.Session.Commands.UndoList.Count > 0 ? UUi.Session.Commands.UndoList[0].Label : null;
    static CharaRagdoll UBlock => UUi.Ops.RagdollBlock(uA);
    static string uA, uB;
    static Vector2 uEmpty;

    // ------------------------------------------------------------ input

    static IEnumerator UIn(params JObject[] events)
    {
        yield return PCall(UApp, new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(events.Cast<object>().ToArray()), ["frames"] = 1 });
        if (!POk) Check("ui.input", false, Short(pLastText));
        yield return PFrames(1);
    }

    static JObject UPt(Vector2 p, string phase, int button = 0, string mods = null)
    {
        var o = new JObject { ["type"] = "pointer", ["x"] = p.x, ["y"] = p.y, ["phase"] = phase, ["button"] = button, ["clicks"] = 1 };
        if (mods != null) o["mods"] = mods;
        return o;
    }

    static JObject UWheel(Vector2 p, float delta) => new JObject { ["type"] = "wheel", ["x"] = p.x, ["y"] = p.y, ["delta"] = delta };

    static JObject UKey(string key) => new JObject { ["type"] = "key", ["key"] = key };

    static JObject[] UDrag(Vector2 p) => new[] { UPt(p, "down"), UPt(p + new Vector2(30, 12), "move"), UPt(p + new Vector2(60, 24), "move"), UPt(p + new Vector2(60, 24), "up") };

    /// <summary>A pointer through the panel as ui.input raises it, with the delta a hand's move has.</summary>
    static void URaw(EventType et, Vector2 p, Vector2 delta = default)
    {
        var se = new Event { type = et, mousePosition = p, delta = delta, button = 0, clickCount = 1, modifiers = EventModifiers.None };
        var tree = UPanel.visualTree;
        if (et == EventType.MouseDown) using (var e = PointerDownEvent.GetPooled(se)) tree.SendEvent(e);
        else if (et == EventType.MouseUp) using (var e = PointerUpEvent.GetPooled(se)) tree.SendEvent(e);
        else using (var e = PointerMoveEvent.GetPooled(se)) tree.SendEvent(e);
    }

    static string UCam()
    {
        var t = UVp.ViewCamera.transform;
        return t.position.ToString("F4") + " " + t.rotation.ToString("F4");
    }

    static Vector2 UOn(Vector3 world) => UVp.Gizmo.ToPanel(world);

    static bool UVisible(VisualElement e)
    {
        if (e == null || e.panel == null) return false;
        for (var x = e; x != null; x = x.hierarchy.parent) if (x.resolvedStyle.display == DisplayStyle.None) return false;
        return e.worldBound.width > 0;
    }

    static IEnumerator UReveal(VisualElement e)
    {
        for (var x = e?.hierarchy.parent; x != null; x = x.hierarchy.parent)
            if (x is Foldout f && !f.value) f.value = true;
        yield return PFrames(2);
        for (var x = e?.hierarchy.parent; x != null; x = x.hierarchy.parent)
            if (x is ScrollView sv) sv.ScrollTo(e);
        yield return PFrames(3);
    }

    static IEnumerator UClick(string name)
    {
        var e = UP?.Q(name);
        yield return UReveal(e);
        if (!Check("the element " + name, e != null && e.panel != null)) yield break;
        yield return UIn(UPt(e.worldBound.center, "click"));
        yield return PFrames(2);
    }

    /// <summary>One render of the view camera with what the gizmo pass holds.</summary>
    static void URender(List<Color32[]> into = null)
    {
        var cam = UVp.ViewCamera;
        int w = into != null ? 480 : Mathf.Max(64, cam.pixelWidth), h = into != null ? 270 : Mathf.Max(64, cam.pixelHeight);
        var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        var prevT = cam.targetTexture;
        var prevR = cam.rect;
        UHost.GizmosIntoTargets = true;
        try
        {
            cam.targetTexture = rt;
            cam.rect = new Rect(0, 0, 1, 1);
            cam.Render();
            if (into != null)
            {
                var prevA = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prevA;
                into.Add(tex.GetPixels32());
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }
        finally
        {
            cam.targetTexture = prevT;
            cam.rect = prevR;
            UHost.GizmosIntoTargets = false;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    /// <summary>Runs a group's coroutine by hand: an exception is a FAIL of the group, and the row goes on to write its report.</summary>
    static IEnumerator USafe(IEnumerator body)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(body);
        while (stack.Count > 0)
        {
            object cur;
            try
            {
                if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                cur = stack.Peek().Current;
            }
            catch (Exception e)
            {
                Check("the group ran without an exception", false, e.GetType().Name + ": " + e.Message + " " + e.StackTrace);
                yield break;
            }
            if (cur is IEnumerator inner) stack.Push(inner);
            else yield return cur;
        }
    }

    // ------------------------------------------------------------ the scene

    static IEnumerator UPause(bool paused)
    {
        yield return PCall(UApp, new { cmd = "scene.pause", paused });
        yield return PFrames(1);
    }

    static IEnumerator ULoad()
    {
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting && UiHost.Current != null && UiHost.Current.IsReady, 240, ok);
        if (!Check("app and UI ready", ok[0])) yield break;
        yield return PCall(UApp, new { cmd = "scene.new" });
        Check("scene.new", POk, Short(pLastText));
        yield return PCall(UApp, new { cmd = "chara.load", path = femaleRel, sex = "female" });
        Check("chara.load female", POk, Short(pLastText));
        uA = Key(pLast);
        yield return PCall(UApp, new { cmd = "chara.load", path = maleRel, sex = "male" });
        Check("chara.load male", POk, Short(pLastText));
        uB = Key(pLast);
        yield return PlayModeHarness.WaitUntil(() => URt.FindChara(uA)?.State == RealizationState.Built && URt.FindChara(uB)?.State == RealizationState.Built, 180, ok);
        if (!Check("both built", ok[0])) { uA = uB = null; yield break; }
        foreach (string k in new[] { uA, uB }) yield return PCall(UApp, new { cmd = "chara.anime", key = k, group = 19, category = 0, no = 0, time = 0.5f, speed = 0f });
        UUi.Shell.ResetLayout();
        var pos = UUi.Model.Find(uA).Doc.Transform.Pos;
        UUi.Ops.SetTransform(new[] { uB }, new Vector3(pos.X + 14, pos.Y, pos.Z), null, null, UiContext.User());
        var w = new SceneRagdollWorld();
        w.Floor.Mode = RagdollFloorValues.ModeFixed;
        w.Floor.Height = pos.Y;
        URt.Session.Commands.Execute(new RagdollWorldEditCommand(w), ExecuteOptions.NotRecorded);
        URt.ApplyNow();
        UUi.Ops.Select(new[] { uA });
        UUi.Tool = GizmoTool.Select;
        yield return PFrames(3);
    }

    /// <summary>The panel in the tall left region, the view on the character. It stands there for height only: its default region is the bottom. It shares that region with the inspector, and a
    /// selection brings the inspector forward, so every group shows the panel again.</summary>
    static IEnumerator UShowPanel()
    {
        var ui = UUi;
        Info("the panel stands in the left region for height only; its default region is the bottom");
        RagdollUiModule.Show(ui);
        yield return PFrames(3);
        ui.Shell.MovePanel(RagdollUiModule.PanelId, LayoutRegions.Left);
        ui.Shell.ShowPanel(RagdollUiModule.PanelId);
        ui.Shell.SetSize(ui.Shell.Regions[LayoutRegions.Left], 420f);
        yield return PFrames(4);
        yield return UView();
    }

    // far enough that a corner of the view is empty for the view's pick: it falls back to the box of a mesh it cannot read, and a character's boxes are up to about 24 units wide
    const float ViewDistance = 120f;

    static IEnumerator UView()
    {
        var hips = URt.FindChara(uA).Context.BodyBones["cf_J_Hips"].position;
        UVp.Camera.SetView(hips, new Vector3(8, 180, 0), ViewDistance, 23);
        UVp.Camera.Flush();
        yield return PFrames(2);
        var wb = UVp.Element.worldBound;
        bool found = false;
        foreach (var p in new[] { new Vector2(wb.xMin + 24, wb.yMin + 24), new Vector2(wb.xMax - 24, wb.yMin + 24), new Vector2(wb.xMin + 24, wb.yMax - 24), new Vector2(wb.xMax - 24, wb.yMax - 24) })
        {
            bool part = UWorld != null && UWorld.Pick(UVp.RayAt(p), out _);
            if (part || UPanel.Pick(p) != UVp.Element || UVp.Picker.Pick(UVp, p).NodeId != null) continue;
            uEmpty = p;
            found = true;
            break;
        }
        Check("Present: a corner of the view where the view's pick finds no node and the world's no part", found, uEmpty.ToString());
    }

    /// <summary>A fresh body on A, the clock running, no tool.</summary>
    static IEnumerator UBody()
    {
        var ui = UUi;
        if (UTools.Running) UTools.End(UTools.Current);
        ui.ShowResult(ui.Ops.RagdollReleaseAll(UiContext.User(OpLabels.RagdollRelease)));
        yield return UPause(false);
        ui.Ops.Select(new[] { uA });
        yield return PFrames(2);
        ui.ShowResult(ui.Ops.RagdollBuild(uA, true, null, UiContext.User(OpLabels.RagdollBuild)));
        ui.Shell.ShowPanel(RagdollUiModule.PanelId);
        yield return PFrames(4);
        Check("a body stands", ui.Ops.RagdollBodyOf(uA) != null);
        yield return UView();
    }

    /// <summary>The tool by the panel's «Grab».</summary>
    static IEnumerator UGrabOn()
    {
        if (UTool == null) yield return UClick("ragdoll-grab");
        Check("the tool runs", UTool != null && UP.Tool == UTool);
    }

    static RagdollBody UBodyA => UUi.Ops.RagdollBodyOf(uA);

    /// <summary>A panel point on a part where the ray meets that part.</summary>
    static bool UHit(string part, out Vector2 at, out RaycastHit hit)
    {
        var p = UBodyA.Part(part);
        at = UOn(p.Rb.worldCenterOfMass);
        return UWorld.Pick(UVp.RayAt(at), out hit) && UWorld.PartOf(hit.rigidbody) != null;
    }

    // ------------------------------------------------------------ the first thing the row does: the panel comes when asked for

    static IEnumerator Play_U_1_Lazy()
    {
        if (!Want("RY11")) yield break;
        current = "RY11";
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting && UiHost.Current != null && UiHost.Current.IsReady, 240, ok);
        if (!POpen("RY11", "the panel appears when asked for")) yield break;
        if (!Check("app and UI ready", ok[0])) yield break;
        var ui = UUi;
        var sh = ui.Shell;
        string file = Path.Combine(ui.Options.ConfigDir, LayoutDocument.FileName);
        bool listed = File.Exists(file) && File.ReadAllText(file, Encoding.UTF8).Contains("\"" + RagdollUiModule.PanelId + "\"");
        Info("layout.json lists the panel at the start: " + listed);
        bool registered = ui.Registry.Panels.TryGet(RagdollUiModule.PanelId, out _);
        Check("at the start the panel is registered only when the layout lists it", registered == listed, registered + " / " + listed);
        Check("the layout's version is as it was", LayoutDocument.CurrentVersion == 2, LayoutDocument.CurrentVersion.ToString());
        Check("the action stands in the manifest and in the registry", ActionManifest.Contains(ActionIds.RagdollPanel) && ui.Registry.Actions.TryGet(ActionIds.RagdollPanel, out _));
        if (RagdollUiModule.PanelShown(ui))
        {
            yield return PCall(UApp, new { cmd = "ui.action", action = ActionIds.RagdollPanel });
            yield return PFrames(3);
        }
        Check("the panel is not shown before it is asked for", !RagdollUiModule.PanelShown(ui));
        var menu = UPanel.visualTree.Q("tb-view");
        yield return UIn(UPt(menu.worldBound.center, "click"));
        yield return PFrames(2);
        Check("the View menu lists the panel", UPanel.visualTree.Q("menu-item-" + ActionIds.RagdollPanel) != null);
        ui.Menus?.CloseAll();
        yield return PFrames(2);
        yield return PCall(UApp, new { cmd = "ui.action", action = ActionIds.RagdollPanel });
        Check("ui.action ragdoll.panel opens it", POk && RagdollUiModule.PanelShown(ui), Short(pLastText));
        yield return PFrames(4);
        Check("the panel is shown, its title the feature's name", UP != null && UVisible(UP) && ui.L10n.Get(RagdollUiModule.TitleKey) == "Ragdoll");
        Check("its default region is the bottom", sh.RegionOf(RagdollUiModule.PanelId) == LayoutRegions.Bottom, sh.RegionOf(RagdollUiModule.PanelId));
        yield return PCall(UApp, new { cmd = "ui.action", action = ActionIds.RagdollPanel });
        yield return PFrames(3);
        Check("the action again collapses it", !RagdollUiModule.PanelShown(ui));
        sh.ResetLayout();
        yield return PFrames(3);
        Check("«Reset layout» leaves the bottom region collapsed", sh.Regions[LayoutRegions.Bottom].Collapsed && !RagdollUiModule.PanelShown(ui));
    }

    // ------------------------------------------------------------ the tool

    static IEnumerator Play_U_2_Tool()
    {
        if (Want("RI8")) { if (POpen("RI8", "no drawing of its own")) RI8(); }
        if (!new[] { "RI1", "RI2", "RI3", "RI4", "RI5", "RI6", "RI7", "RI9" }.Any(Want)) yield break;
        current = "tool setup";
        yield return ULoad();
        if (uA == null) yield break;
        yield return UShowPanel();
        if (POpen("RI1", "a press, a move, a miss")) yield return USafe(RI1());
        if (POpen("RI2", "the wheel")) yield return USafe(RI2());
        if (POpen("RI3", "Space, T, N")) yield return USafe(RI3());
        if (POpen("RI4", "Ctrl, Shift")) yield return USafe(RI4());
        if (POpen("RI5", "Esc, a pause")) yield return USafe(RI5());
        if (POpen("RI6", "the overlays")) yield return USafe(RI6());
        if (POpen("RI7", "the shoot mode")) yield return USafe(RI7());
        if (POpen("RI9", "the Edit tab")) yield return USafe(RI9());
        if (UTools.Running) UTools.End(UTools.Current);
        UUi.Ops.RagdollReleaseAll(UiContext.User(OpLabels.RagdollRelease));
    }

    static void RI8()
    {
        string dir = Path.Combine(ContentSettings.ProjectRoot, "Assets", "HS2Next", "Runtime", "UI", "Panels", "Ragdoll");
        var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);
        int bad = 0, draws = 0;
        var where = new List<string>();
        foreach (string f in files)
        {
            string text = File.ReadAllText(f, Encoding.UTF8);
            foreach (string w in new[] { "OnGUI", "GL.", "OnRenderObject" })
                if (text.Contains(w)) { bad++; where.Add(Path.GetFileName(f) + ": " + w); }
            if (text.Contains("IToolDraw")) draws++;
        }
        Check("Present: the search reads the new files and finds the tool's drawing", files.Length >= 6 && draws >= 2, files.Length + " files, " + draws + " with IToolDraw");
        Check("no OnGUI, GL. or OnRenderObject in them", bad == 0, string.Join(", ", where));
    }

    static IEnumerator RI1()
    {
        yield return UBody();
        var ui = UUi;
        Check("the mode is «Off»", UP.Mode == RagdollPanel.ModeOff, UP.Mode.ToString());
        yield return UGrabOn();
        Check("«Grab» begins the tool in grab mode, its hint in the status line, the hint line shows", UTool != null && !UTool.Shooting && !UTools.Blocks && ui.Shell.StatusBar.Hint.text == ui.L10n.Get(RagdollTool.HintKey) && UVisible(UP.Q("ragdoll-hint")), ui.Shell.StatusBar.Hint.text);
        if (!Check("Present: a ray through the chest hits a part", UHit("chest", out var on, out var hit), on.ToString())) yield break;
        string cam = UCam();
        int undos = UUndos;
        var tool = UTool;
        yield return UIn(UPt(on, "down"));
        Check("a press on a part claims it: a grab at the hit point", UTools.PressHeld && tool.Held != null && tool.Held.Target == hit.rigidbody, UTools.PressHeld + " " + (tool.Held != null));
        if (tool.Held == null) yield break;
        Check("the depth is the hit's distance", Mathf.Abs(tool.Depth - hit.distance) < 1e-3f, tool.Depth + " / " + hit.distance);
        var target = tool.Held.Target;
        var to = on + new Vector2(90, -60);
        yield return UIn(UPt(on + new Vector2(45, -30), "move"), UPt(to, "move"));
        var want = UVp.RayAt(to).GetPoint(tool.Depth);
        Check("a move: the goal is the ray's point at the grab's depth", (tool.Held.Goal - want).magnitude < 1e-3f, (tool.Held.Goal - want).magnitude.ToString("G4"));
        float before = (tool.Held.Point - want).magnitude;
        yield return PFrames(40);
        float after = tool.Held != null ? (tool.Held.Point - want).magnitude : float.NaN;
        Check("the part goes towards the goal", after < before * 0.9f, before.ToString("F3") + " to " + after.ToString("F3"));
        Check("the camera stays during a claimed press", UCam() == cam, UCam());
        yield return UIn(UPt(to, "up"));
        Check("the release drops the grab", !UTools.PressHeld && tool.Held == null && UWorld.Grabs.Count == 0, UWorld.Grabs.Count.ToString());
        Check("a plain grab makes no entry", UUndos == undos, (UUndos - undos).ToString());

        cam = UCam();
        Check("the corner of the view misses every part", !UWorld.Pick(UVp.RayAt(uEmpty), out _));
        yield return UIn(UDrag(uEmpty));
        Check("a press that hits nothing is not claimed: the camera orbits", UCam() != cam && !UTools.PressHeld && tool.Held == null, UCam());
        yield return UView();
        var hipsB = URt.FindChara(uB).Context.BodyBones["cf_J_Hips"].position;
        UVp.Camera.SetView((URt.FindChara(uA).Context.BodyBones["cf_J_Hips"].position + hipsB) * 0.5f, new Vector3(8, 180, 0), 90, 23);
        UVp.Camera.Flush();
        yield return PFrames(2);
        var onB = UOn(hipsB);
        yield return UIn(UPt(onB, "click"));
        Check("a click on what has no body selects as ever", ui.Selection.Primary == uB, ui.Selection.Primary);
        ui.Ops.Select(new[] { uA });
        yield return PFrames(2);
        yield return UView();
    }

    static IEnumerator RI2()
    {
        yield return UBody();
        yield return UGrabOn();
        if (!Check("Present: a ray through the chest hits a part", UHit("chest", out var on, out _))) yield break;
        var tool = UTool;
        yield return UIn(UPt(on, "down"));
        if (!Check("the press took a grab", tool.Held != null)) yield break;
        string cam = UCam();
        float d0 = tool.Depth;
        yield return UIn(UWheel(on, -3));
        Check("a notch: depth x 0.9", Mathf.Abs(tool.Depth - d0 * 0.9f) < 1e-3f, d0 + " to " + tool.Depth);
        Check("the goal goes with it", (tool.Held.Goal - UVp.RayAt(on).GetPoint(tool.Depth)).magnitude < 1e-3f);
        Check("the wheel is claimed: the camera stays", UCam() == cam);
        float d1 = tool.Depth;
        yield return UIn(UWheel(on, 6));
        Check("two notches the other way: depth x 1.2", Mathf.Abs(tool.Depth - d1 * 1.2f) < 1e-3f, d1 + " to " + tool.Depth);
        yield return UIn(UWheel(on, -30));
        Check("ten notches: the depth stops at 1", tool.Depth == RagdollTool.MinDepth, tool.Depth.ToString());
        yield return UIn(UPt(on, "up"));
        cam = UCam();
        yield return UIn(UWheel(on, -3));
        Check("with no grab the wheel goes to the camera", UCam() != cam, UCam());
        yield return UView();
    }

    static IEnumerator RI3()
    {
        yield return UBody();
        yield return UGrabOn();
        var ui = UUi;
        var tool = UTool;
        if (!Check("Present: rays through the chest and the pelvis hit parts", UHit("chest", out var on, out _) & UHit("pelvis", out var on2, out _))) yield break;

        // Space
        int u = UUndos;
        yield return UIn(UPt(on, "down"));
        if (!Check("the press took a grab", tool.Held != null)) yield break;
        var at = tool.Held.Point;
        string part = tool.Held.Part.Key;
        yield return UIn(UKey("Space"));
        yield return PFrames(2);
        Check("Space: the grab is a pin, one entry «Pin»", UBlock.Pins.Count == 1 && UUndos == u + 1 && UTop == OpLabels.RagdollPin, UBlock.Pins.Count + ", " + (UUndos - u) + ", " + UTop);
        Check("the pin stands where the held point was, a frame or two before", UBlock.Pins.Count == 1 && UBlock.Pins[0].Part == part && UBlock.Pins[0].At.HasValue && (UnityObjects.V(UBlock.Pins[0].At.Value) - at).magnitude < 0.05f);
        Check("the clock did not hear Space", !UApp.Runtime.Clock.Paused);
        Check("the press stays claimed, with nothing", tool.Held == null && UTools.PressHeld);
        var goal = UWorld.Grabs.Count == 1 ? UWorld.Grabs[0].Goal : default;
        yield return UIn(UPt(on + new Vector2(50, 0), "move"));
        Check("a move then moves nothing", UWorld.Grabs.Count == 1 && UWorld.Grabs[0].Goal == goal, UWorld.Grabs.Count.ToString());
        yield return UIn(UPt(on + new Vector2(50, 0), "up"));
        Check("the release leaves the pin", !UTools.PressHeld && UBlock.Pins.Count == 1 && UUndos == u + 1);
        ui.ShowResult(ui.Ops.RagdollUnpin(uA, null, UiContext.User(OpLabels.RagdollUnpin)));
        yield return PFrames(2);

        // with no grab Space is the app's
        yield return UIn(UKey("Space"));
        Check("Space with no grab is not claimed: the shortcut runs", UApp.Runtime.Clock.Paused);
        yield return UPause(false);

        // T
        ui.Ops.Select(new[] { uB });
        yield return PFrames(2);
        u = UUndos;
        Check("Present: the pelvis still under the point", UHit("pelvis", out on2, out _));
        yield return UIn(UPt(on2, "down"));
        if (!Check("the press took a grab", tool.Held != null)) yield break;
        var held = tool.Held.Point;
        yield return UIn(UKey("T"));
        yield return PFrames(2);
        var pins = UBlock.Pins;
        Check("T: a pin that follows the selected node, one entry", pins.Count == 1 && pins[0].Follow == uB && UUndos == u + 1 && UTop == OpLabels.RagdollPin, pins.Count + " " + (pins.Count > 0 ? pins[0].Follow : "") + " " + (UUndos - u));
        var holder = URt.HolderOf(uB);
        Check("the offset is kept: the anchor stays where the point was", pins.Count == 1 && pins[0].Offset.HasValue && (holder.TransformPoint(UnityObjects.V(pins[0].Offset.Value)) - held).magnitude < 0.05f);
        Check("the press stays claimed, with nothing", tool.Held == null && UTools.PressHeld);
        yield return UIn(UPt(on2, "up"));
        ui.ShowResult(ui.Ops.RagdollUnpin(uA, null, UiContext.User(OpLabels.RagdollUnpin)));
        yield return PFrames(2);

        // T with nothing selected
        ui.Ops.Select(Array.Empty<string>());
        yield return PFrames(2);
        u = UUndos;
        int toasts = ui.Toasts.History.Count;
        Check("Present: the pelvis still under the point", UHit("pelvis", out on2, out _));
        yield return UIn(UPt(on2, "down"));
        yield return UIn(UKey("T"));
        Check("T with nothing selected: a note", ui.Toasts.History.Count == toasts + 1 && ui.Toasts.History.Last() == "Info: " + ui.L10n.Get("ragdoll.tool.noSelection"), ui.Toasts.History.Count - toasts + " " + ui.Toasts.History.LastOrDefault());
        Check("T with nothing selected: no pin", UBlock.Pins.Count == 0, UBlock.Pins.Count.ToString());
        Check("T with nothing selected: no entry", UUndos == u, (UUndos - u).ToString());
        Check("T with nothing selected: the grab goes on", tool.Held != null);

        // N
        int nodes = ui.Model.PreOrder().Count();
        int constraints = ui.Model.Constraints?.Entries.Count ?? 0;
        u = UUndos;
        yield return UIn(UKey("N"));
        yield return PFrames(2);
        Check("N: a folder and a constraint, one entry «Follower»", ui.Model.PreOrder().Count() == nodes + 1 && (ui.Model.Constraints?.Entries.Count ?? 0) == constraints + 1 && UUndos == u + 1 && UTop == OpLabels.RagdollFollower, UTop + " " + (UUndos - u));
        Check("the grab goes on", tool.Held != null && UTools.PressHeld);
        yield return UIn(UPt(on2, "up"));
        Check("the release drops it", tool.Held == null && !UTools.PressHeld);
        yield return PCall(UApp, new { cmd = "undo" });
        yield return PFrames(2);
        Check("undo takes the follower back in one step", ui.Model.PreOrder().Count() == nodes && (ui.Model.Constraints?.Entries.Count ?? 0) == constraints);
        ui.Ops.Select(new[] { uA });
        yield return PFrames(2);
    }

    static IEnumerator RI4()
    {
        yield return UBody();
        yield return UGrabOn();
        var ui = UUi;
        var tool = UTool;
        if (!Check("Present: a ray through the chest hits a part", UHit("chest", out var on, out _))) yield break;
        var chest = UBodyA.Part("chest");
        var start = chest.Rb.worldCenterOfMass;
        var dir = UVp.RayAt(on).direction;
        int u = UUndos;
        string cam = UCam();
        yield return UIn(UPt(on, "down", 0, "ctrl"));
        Check("Ctrl + press on a part is claimed, and takes no grab", UTools.PressHeld && tool.Held == null);
        yield return UIn(UPt(on, "up", 0, "ctrl"));
        float along = 0f;
        for (int i = 0; i < 30; i++)
        {
            yield return PFrames(1);
            along = Mathf.Max(along, Vector3.Dot(chest.Rb.worldCenterOfMass - start, dir));
        }
        Check("the part goes along the ray", along >= 0.3f, along.ToString("F3"));
        Check("a push makes no entry, the camera stays", UUndos == u && UCam() == cam, (UUndos - u).ToString());
        yield return UIn(UPt(uEmpty, "down", 0, "ctrl"));
        Check("Ctrl + press on nothing is not claimed", !UTools.PressHeld);
        yield return UIn(UPt(uEmpty, "up", 0, "ctrl"));
        Check("a press on nothing selects nothing", ui.Selection.Count == 1 && ui.Selection.Primary == uA, ui.Selection.Count.ToString());

        int props = UWorld.Props.Count;
        u = UUndos;
        yield return UIn(UPt(uEmpty, "down", 0, "shift"));
        Check("Shift + press on nothing throws a prop, claimed", UWorld.Props.Count == props + 1 && UTools.PressHeld, UWorld.Props.Count.ToString());
        yield return UIn(UPt(uEmpty, "up", 0, "shift"));
        if (!Check("Present: a ray through the pelvis hits a part", UHit("pelvis", out var onP, out _))) yield break;
        yield return UIn(UPt(onP, "down", 0, "shift"), UPt(onP, "up", 0, "shift"));
        Check("Shift + press on a part throws too", UWorld.Props.Count == props + 2 && tool.Held == null, UWorld.Props.Count.ToString());
        Check("a throw makes no entry", UUndos == u, (UUndos - u).ToString());
        ui.ShowResult(ui.Ops.RagdollClearProps());

        // a prop can be grabbed and takes no pin; the clock stands, so the prop stays under the point
        yield return UPause(true);
        yield return UIn(UPt(on, "down", 0, "shift"), UPt(on, "up", 0, "shift"));
        var prop = UWorld.Props.All.LastOrDefault();
        if (Check("the prop of the throw", prop != null))
        {
            var onProp = UOn(prop.Rb.position);
            yield return UIn(UPt(onProp, "down"));
            bool took = tool.Held != null && tool.Held.Part == null && tool.Held.Target == prop.Rb;
            Check("a press on a prop grabs it", took, (tool.Held != null) + "");
            int toasts = ui.Toasts.History.Count;
            u = UUndos;
            yield return UIn(UKey("Space"));
            Check("Space on a prop: a note, no pin, the grab goes on", took && ui.Toasts.History.Count == toasts + 1 && UBlock.Pins.Count == 0 && UUndos == u && tool.Held != null);
            yield return UIn(UPt(onProp, "up"));
        }
        yield return UPause(false);

        // the first prop brings the world
        if (UTools.Running) UTools.End(UTools.Current);
        ui.ShowResult(ui.Ops.RagdollReleaseAll(UiContext.User(OpLabels.RagdollRelease)));
        yield return PFrames(2);
        Check("Present: no world now", UWorld == null);
        yield return UGrabOn();
        yield return UIn(UPt(uEmpty, "down", 0, "shift"), UPt(uEmpty, "up", 0, "shift"));
        Check("with no world the first prop makes one", UWorld != null && UWorld.Props.Count == 1, (UWorld != null) + "");
        ui.ShowResult(ui.Ops.RagdollClearProps());
        yield return PFrames(2);
    }

    static IEnumerator RI5()
    {
        yield return UBody();
        yield return UGrabOn();
        var tool = UTool;
        if (!Check("Present: a ray through the chest hits a part", UHit("chest", out var on, out _))) yield break;
        yield return UIn(UPt(on, "down"));
        if (!Check("the press took a grab", tool.Held != null)) yield break;
        yield return UIn(UKey("Escape"));
        Check("Esc during a grab drops it, the tool goes on", tool.Held == null && !UTools.PressHeld && UTool == tool && UWorld.Grabs.Count == 0);
        yield return UIn(UPt(on, "up"));
        yield return PFrames(2);
        yield return UIn(UKey("Escape"));
        yield return PFrames(2);
        Check("Esc with no press ends the tool, the mode goes back to «Off»", UTool == null && UP.Tool == null && UP.Mode == RagdollPanel.ModeOff && !UVisible(UP.Q("ragdoll-hint")));

        // a press held when the tool ends
        yield return UGrabOn();
        tool = UTool;
        Check("Present: a ray through the chest hits a part", UHit("chest", out on, out _));
        yield return UIn(UPt(on, "down"));
        var body = UBodyA;
        UTools.End(tool);
        Check("the end drops what the press holds", tool.Held == null && UWorld.Grabs.Count == 0);
        yield return UIn(UPt(on, "up"));

        // a pause
        yield return UGrabOn();
        tool = UTool;
        yield return UPause(true);
        Check("Present: a ray through the chest hits a part", UHit("chest", out on, out _));
        yield return UIn(UPt(on, "down"));
        if (!Check("a grab in a pause", tool.Held != null)) yield break;
        var rb = tool.Held.Target;
        var was = rb.position;
        var to = on + new Vector2(80, -40);
        yield return UIn(UPt(to, "move"));
        var goal = tool.Held.Goal;
        yield return PFrames(30);
        Check("Present: the goal moved", (goal - was).magnitude >= 1f, (goal - was).magnitude.ToString("F3"));
        Check("in a pause nothing moves", rb.position == was, (rb.position - was).magnitude.ToString("G4"));
        yield return UPause(false);
        yield return PFrames(30);
        Check("the clock runs: the part moves", (rb.position - was).magnitude >= 0.1f, (rb.position - was).magnitude.ToString("F3"));
        yield return UIn(UPt(to, "up"));
    }

    sealed class DrawLog : IToolDraw
    {
        public readonly List<(Vector3 a, Vector3 b, Color c)> Lines = new List<(Vector3, Vector3, Color)>();
        public void Line(Vector3 a, Vector3 b, Color c) => Lines.Add((a, b, c));
        public void Marker(Vector3 p, float size, Color c) { }
        public void Label(Vector3 p, string text) { }
        public int Of(Color c) => Lines.Count(l => l.c == c);
    }

    static int WireLines(Collider c) => c is BoxCollider ? 12 : c is CapsuleCollider ? 36 : c is SphereCollider ? 48 : 0;

    // how many pixels differ, and where
    static int UDiffer(Color32[] a, Color32[] b, out string where)
    {
        int n = 0, x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].r == b[i].r && a[i].g == b[i].g && a[i].b == b[i].b) continue;
            n++;
            int x = i % 480, y = i / 480;
            x0 = Math.Min(x0, x);
            y0 = Math.Min(y0, y);
            x1 = Math.Max(x1, x);
            y1 = Math.Max(y1, y);
        }
        where = n == 0 ? "none" : "x " + x0 + "-" + x1 + ", y " + y0 + "-" + y1;
        return n;
    }

    static IEnumerator RI6()
    {
        yield return UBody();
        var ui = UUi;
        yield return PCall(UApp, new { cmd = "chara.face", key = uA, blink = false });
        yield return PCall(UApp, new { cmd = "chara.face", key = uB, blink = false });
        yield return UPause(true);
        // the view with no tool, before it begins; the first render of a scene may hold undefined pixels on a character, so it is not compared
        var noTool = new List<Color32[]>();
        URender(noTool);
        noTool.Clear();
        URender(noTool);
        URender(noTool);
        int same = UDiffer(noTool[0], noTool[1], out string sameAt);
        Measure("RI6 two renders with no tool differ by " + same + " px, " + sameAt);
        Check("two renders with no tool agree (the base of the comparison)", same == 0, same + " px, " + sameAt);
        yield return UGrabOn();
        var tool = UTool;
        if (!Check("Present: a ray through the chest hits a part", UHit("chest", out var on, out _))) yield break;
        yield return UIn(UPt(on, "down"));
        if (!Check("a grab for the line", tool.Held != null)) yield break;
        // paused, the anchor stays on the held point, and a line of no length draws nothing (ToolHost.Line): whether it had a length was the float rounding's luck. It goes 2 units up
        tool.Held.Anchor.position = tool.Held.Point + Vector3.up * 2f;
        Check("Present: the anchor lies apart from the held point", (tool.Held.Anchor.position - tool.Held.Point).magnitude > 1f, (tool.Held.Anchor.position - tool.Held.Point).magnitude.ToString("G4"));

        var body = UBodyA;
        int kinds = body.Parts.Select(p => p.Col.GetType()).Distinct().Count();
        int wires = body.Parts.Sum(p => WireLines(p.Col));
        Check("Present: the body's colliders are of three kinds", kinds == 3, kinds.ToString());
        var heldPart = tool.Held.Part;
        foreach (var p in body.Parts) if (p != heldPart) p.Rb.WakeUp();
        var asleep = body.Part("foot_L");
        asleep.Rb.Sleep();
        var log = new DrawLog();
        RagdollOverlay.Draw(log, UWorld, false, false);
        Check("always: a line from the held point to its anchor, in its colour", log.Lines.Count == 1 && log.Of(RagdollOverlay.Anchor) == 1 && (log.Lines[0].a - tool.Held.Point).magnitude < 1e-4f, log.Lines.Count.ToString());
        log = new DrawLog();
        RagdollOverlay.Draw(log, UWorld, true, true);
        int held = WireLines(heldPart.Col), sleeping = WireLines(asleep.Col);
        Check("with both toggles: every collider by its kind, the room's 12 edges, the anchor's line", log.Lines.Count == wires + 12 + 1, log.Lines.Count + " / " + (wires + 13));
        Check("the held part in its colour", log.Of(RagdollOverlay.Held) == held, log.Of(RagdollOverlay.Held) + " / " + held);
        Check("a part asleep in its colour", log.Of(RagdollOverlay.Asleep) == sleeping, log.Of(RagdollOverlay.Asleep) + " / " + sleeping);
        Check("the awake parts in theirs", log.Of(RagdollOverlay.Awake) == wires - held - sleeping, log.Of(RagdollOverlay.Awake) + " / " + (wires - held - sleeping));
        Check("the room in its colour", log.Of(RagdollOverlay.Room) == 12);
        // the wire lies on the collider
        double worst = 0;
        string worstPart = "";
        foreach (var p in body.Parts)
        {
            var one = new DrawLog();
            RagdollOverlayTestDoor(one, p);
            foreach (var l in one.Lines)
                foreach (var q in new[] { l.a, l.b })
                {
                    double d = (p.Col.ClosestPoint(q) - q).magnitude;
                    if (d > worst) { worst = d; worstPart = p.Key; }
                }
        }
        Check("every wire point lies on its collider", worst <= 2e-3, worst.ToString("G4") + " " + worstPart);

        // the view and a capture
        var cam = UVp.ViewCamera;
        var imgs = new List<Color32[]>();
        URender(imgs);   // a first render of a scene may hold undefined pixels on a character: it is not compared
        imgs.Clear();
        RagdollTool.ShowColliders = RagdollTool.ShowRoom = false;
        URender(imgs);
        int lines0 = UTools.LastDrawn;
        var off1 = ViewCapture.Render(cam, 512, 288, ViewCapture.None);
        var off2 = ViewCapture.Render(cam, 512, 288, ViewCapture.None);
        Check("two captures with the toggles off agree (the base of the comparison)", off1 != null && off2 != null && off1.SequenceEqual(off2), off1?.Length + " / " + off2?.Length);
        RagdollTool.ShowColliders = RagdollTool.ShowRoom = true;
        URender(imgs);
        int lines1 = UTools.LastDrawn;
        Check("the tool draws in the view through the pass: the line alone, then every wire", lines0 == 1 && lines1 == wires + 13, lines0 + " / " + lines1);
        int diff = 0;
        for (int i = 0; i < imgs[0].Length; i++)
        {
            var a = imgs[0][i];
            var b = imgs[1][i];
            if (Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b) > 30) diff++;
        }
        Measure("RI6 view pixels the overlays change: " + diff);
        Check("Present: with the colliders on the view differs", diff > 100, diff.ToString());
        var on1 = ViewCapture.Render(cam, 512, 288, ViewCapture.None);
        Check("a capture holds nothing of the overlays", on1 != null && on1.SequenceEqual(off1), on1?.Length + " / " + off1?.Length);
        var res = HS2Next.Screenshots.CaptureService.RenderSync(URt, cam, new HS2Next.Screenshots.CaptureRequest { Width = 320, Height = 180, Aa = HS2Next.Screenshots.CaptureService.None });
        Check("the capture path answers", res != null && res.Ok, res?.Error);
        int shown = UDiffer(noTool[0], imgs[1], out string shownAt);
        Measure("RI6 the overlays against the view with no tool: " + shown + " px, " + shownAt);
        Check("Present: the render with the overlays on differs from the one before the tool", shown >= 100, shown + " px, " + shownAt);
        RagdollTool.ShowColliders = RagdollTool.ShowRoom = false;
        yield return UIn(UPt(on, "up"));
        UTools.End(UTool);
        yield return PFrames(2);
        var after = new List<Color32[]>();
        URender(after);
        int left = UDiffer(noTool[0], after[0], out string leftAt);
        Measure("RI6 after the end against before the tool: " + left + " px, " + leftAt);
        Check("after the end the view is the view before the tool: nothing of it draws", left == 0, left + " px, " + leftAt);

        // with no tool the panel draws the outlines while it is shown (the owner's trial 2026-11-09: the switches showed nothing without the tool)
        var colliders = UP.Q<Toggle>("ragdoll-colliders");
        var room = UP.Q<Toggle>("ragdoll-room");
        yield return PFrames(2);
        if (!Check("Present: no tool, the panel shown, both switches off and live", UTool == null && UVisible(UP) && UWorld != null && UWorld.HasRoom && colliders != null && room != null && !colliders.value && !room.value
            && colliders.enabledInHierarchy && room.enabledInHierarchy, (UTool != null) + " " + colliders?.value + " " + room?.value + " " + colliders?.enabledInHierarchy + " " + room?.enabledInHierarchy)) yield break;
        var bare = new List<Color32[]>();
        URender(bare);
        int bareOff = UTools.LastDrawn;
        var capOff = ViewCapture.Render(cam, 512, 288, ViewCapture.None);
        yield return UClick("ragdoll-colliders");
        yield return UClick("ragdoll-room");
        Check("a click on each switch turns its outline on", colliders.value && room.value && RagdollTool.ShowColliders && RagdollTool.ShowRoom, colliders.value + " " + room.value);
        RagdollTool.ShowColliders = RagdollTool.ShowRoom = false;
        var capOff2 = ViewCapture.Render(cam, 512, 288, ViewCapture.None);   // the base for the capture, in this frame: the switches are back on before the frame ends
        RagdollTool.ShowColliders = RagdollTool.ShowRoom = true;
        URender(bare);
        int bareOn = UTools.LastDrawn;
        var capOn = ViewCapture.Render(cam, 512, 288, ViewCapture.None);
        Check("with no tool the panel draws every wire and the room's edges, no anchor line", bareOff == 0 && bareOn == wires + 12, bareOff + " / " + bareOn + " / " + (wires + 12));
        int bareShown = UDiffer(bare[0], bare[1], out string bareAt);
        Measure("RI6 the outlines with no tool against the switches off: " + bareShown + " px, " + bareAt);
        Check("Present: with no tool and the switches on the view differs", bareShown >= 100, bareShown + " px, " + bareAt);
        Check("a capture with no tool holds nothing of the outlines", capOn != null && capOff2 != null && capOn.SequenceEqual(capOff2), capOn?.Length + " / " + capOff2?.Length + " / " + (capOff != null && capOff2 != null && capOff.SequenceEqual(capOff2)));
        yield return UClick("ragdoll-colliders");
        yield return UClick("ragdoll-room");
        URender(bare);
        Check("the switches off again: nothing draws", !RagdollTool.ShowColliders && !RagdollTool.ShowRoom && UTools.LastDrawn == 0, UTools.LastDrawn.ToString());
        RagdollTool.ShowColliders = RagdollTool.ShowRoom = false;
        yield return PCall(UApp, new { cmd = "chara.face", key = uA, blink = true });
        yield return PCall(UApp, new { cmd = "chara.face", key = uB, blink = true });
        yield return UPause(false);
    }

    // the wire of one part, by itself
    static void RagdollOverlayTestDoor(IToolDraw d, RagdollBodyPart p) => RagdollOverlay.Wire(d, p.Col, p.Tr, Color.white);

    static IEnumerator RI7()
    {
        var ui = UUi;
        yield return UBody();
        yield return UClick("ragdoll-shoot");
        var tool = UTool;
        if (!Check("«Shoot» with no tool begins it in shoot mode, and only «Shoot» is chosen", tool != null && tool.Shooting && UP.Mode == RagdollPanel.ModeShoot, UP.Mode.ToString())) yield break;

        // a miss fires, and repeats at the world's rate
        int n0 = tool.Shots;
        string cam = UCam();
        yield return UIn(UPt(uEmpty, "down"));
        Check("a press on nothing fires one and is claimed", tool.Shots == n0 + 1 && UTools.PressHeld, tool.Shots - n0 + "");
        var sw = Stopwatch.StartNew();
        int s0 = tool.Shots;
        yield return PlayModeHarness.WaitRealtime(1.0);
        double held = sw.Elapsed.TotalSeconds;
        int repeats = tool.Shots - s0;
        Measure("RI7 at 10 a second: " + repeats + " in " + held.ToString("F3") + " s");
        Check("held it repeats at the rate, in real time", Math.Abs(repeats - held * 10) <= 2, repeats + " in " + held.ToString("F3"));
        yield return UIn(UPt(uEmpty, "up"));
        int s1 = tool.Shots;
        yield return PFrames(20);
        Check("the release stops it", tool.Shots == s1 && !UTools.PressHeld);
        Check("the camera stayed", UCam() == cam);

        ui.ShowResult(ui.Ops.RagdollValue(null, "fireRate", new JValue(5f), UiContext.User(OpLabels.RagdollSettings)));
        yield return UIn(UPt(uEmpty, "down"));
        sw.Restart();
        s0 = tool.Shots;
        yield return PlayModeHarness.WaitRealtime(1.0);
        held = sw.Elapsed.TotalSeconds;
        repeats = tool.Shots - s0;
        yield return UIn(UPt(uEmpty, "up"));
        Measure("RI7 at 5 a second: " + repeats + " in " + held.ToString("F3") + " s");
        Check("the rate is the world's value", Math.Abs(repeats - held * 5) <= 2, repeats + " in " + held.ToString("F3"));
        ui.ShowResult(ui.Ops.RagdollValue(null, "fireRate", new JValue(10f), UiContext.User(OpLabels.RagdollSettings)));

        // Alt
        s0 = tool.Shots;
        cam = UCam();
        yield return UIn(UPt(uEmpty, "down", 0, "alt"));
        Check("Alt + press passes through", tool.Shots == s0 && !UTools.PressHeld);
        yield return UIn(UPt(uEmpty + new Vector2(40, 0), "move", 0, "alt"), UPt(uEmpty + new Vector2(40, 0), "up", 0, "alt"));
        Check("the camera can orbit", UCam() != cam, UCam());
        yield return UView();

        // a hit, with and without a stain
        if (!Check("Present: a ray through the chest hits a part", UHit("chest", out var on, out _))) yield break;
        RagdollTool.StainOnHit = false;
        s0 = tool.Shots;
        yield return UIn(UPt(on, "down"), UPt(on, "up"));
        yield return PFrames(2);
        var fx = RagdollShots.Of(URt)?.Fx;
        Check("Present: bullets counted, a hit makes droplets", tool.Shots == s0 + 1 && fx != null && fx.Droplets > 0, tool.Shots - s0 + ", " + fx?.Droplets);
        Check("the shot weakens what it hit", ui.Ops.RagdollStatus(uA).Weakest > 0f, ui.Ops.RagdollStatus(uA).Weakest.ToString("G4"));
        Check("«Stain on hit» off: no stain", (ui.Ops.WetBlock(uA)?.Stains.Count ?? 0) == 0);
        RagdollTool.StainOnHit = true;
        yield return PFrames(40);
        Check("Present: a ray through the chest hits a part", UHit("chest", out on, out _));
        yield return UIn(UPt(on, "down"), UPt(on, "up"));
        yield return PFrames(3);
        Check("«Stain on hit» on: a stain", (ui.Ops.WetBlock(uA)?.Stains.Count ?? 0) >= 1, (ui.Ops.WetBlock(uA)?.Stains.Count ?? 0).ToString());

        // the spread, with no world, so that a bullet flies its whole way
        ui.ShowResult(ui.Ops.WetRemoveAll(uA, UiContext.User(OpLabels.WetRemove)));
        ui.ShowResult(ui.Ops.RagdollReleaseAll(UiContext.User(OpLabels.RagdollRelease)));
        yield return PFrames(2);
        Check("Present: no world now, the tool still runs", UWorld == null && UTool == tool);
        double most = 0, least = 90;
        for (int i = 0; i < 20; i++)
        {
            s0 = tool.Shots;
            yield return UIn(UPt(uEmpty, "down"), UPt(uEmpty, "up"));
            if (tool.Shots != s0 + 1) { Check("press " + i + " fired once", false, (tool.Shots - s0).ToString()); break; }
            double a = UAngle(tool.LastEnd - tool.LastMuzzle, tool.LastRay.direction);
            most = Math.Max(most, a);
            least = Math.Min(least, a);
        }
        Measure("RI7 spread of 20 shots: " + least.ToString("F4") + " to " + most.ToString("F4") + " degrees");
        Check("a shot leaves the ray by at most the spread", most <= RagdollTool.Spread + 0.01, most.ToString("F4"));
        Check("Present: the spread is there", most >= 0.1, most.ToString("F4"));

        // the mode: one of three (the owner's trial 2026-11-09: the two switches read as independent)
        yield return UClick("ragdoll-grab");
        Check("«Grab» while shooting: grab mode, the same tool goes on, only «Grab» is chosen", UTool == tool && !tool.Shooting && UP.Mode == RagdollPanel.ModeGrab, UP.Mode.ToString());
        yield return UClick("ragdoll-shoot");
        Check("«Shoot» while grabbing: shoot mode, the same tool", UTool == tool && tool.Shooting && UP.Mode == RagdollPanel.ModeShoot, UP.Mode.ToString());
        yield return UClick("ragdoll-shoot");
        Check("«Shoot» again: it stays chosen, the tool goes on", UTool == tool && tool.Shooting && UP.Mode == RagdollPanel.ModeShoot);
        yield return UClick("ragdoll-mode-off");
        Check("«Off»: the tool ends, only «Off» is chosen", UTool == null && UP.Mode == RagdollPanel.ModeOff, UP.Mode.ToString());
    }

    // the angle between two directions in degrees, in double: a small angle reads 0 by the float routes
    static double UAngle(Vector3 a, Vector3 b)
    {
        double x = (double)a.y * b.z - (double)a.z * b.y, y = (double)a.z * b.x - (double)a.x * b.z, z = (double)a.x * b.y - (double)a.y * b.x;
        double dot = (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z;
        return Math.Atan2(Math.Sqrt(x * x + y * y + z * z), dot) * 180.0 / Math.PI;
    }

    static IEnumerator RI9()
    {
        var ui = UUi;
        yield return UBody();
        // the panel in its default region, beside the inspector: the Edit tab brings the inspector forward, and in the left region, which the two share in these groups, it would hide the panel
        ui.Shell.MovePanel(RagdollUiModule.PanelId, LayoutRegions.Bottom);
        ui.Shell.ShowPanel(RagdollUiModule.PanelId);
        yield return PFrames(4);
        yield return UGrabOn();
        var ran = ui.Registry.RunWith(ActionIds.EditShow, new ActionInvocation { Source = "test", Args = new Dictionary<string, object> { ["node"] = "body" } }, out _);
        yield return PFrames(3);
        Check("the Edit tab in view ends a running tool, the mode goes back to «Off»", CharaEditTab.Current != null && UTool == null && UP.Tool == null && UP.Mode == RagdollPanel.ModeOff, ran + " " + (CharaEditTab.Current != null));
        Check("Present: the panel and the Edit tab are both in view", UVisible(UP) && CharaEditTab.Current != null, ui.Shell.RegionOf(RagdollUiModule.PanelId));
        int toasts = ui.Toasts.History.Count;
        yield return UClick("ragdoll-grab");
        Check("the Edit tab refuses the tool: a note, the mode stays «Off»", UTool == null && UP.Mode == RagdollPanel.ModeOff && UTools.Refusal == ToolHost.RefusedEdit && ui.Toasts.History.Count == toasts + 1, UTools.Refusal + " " + UP.Mode);
        yield return UClick("ragdoll-shoot");
        Check("«Shoot» too", UTool == null && UP.Mode == RagdollPanel.ModeOff, UP.Mode.ToString());
        var ip = ui.Shell.PanelRoot("inspector") as InspectorPanel ?? ui.Shell.PanelRoot("inspector")?.Q<InspectorPanel>();
        ip?.Chara?.ShowTab("anime");
        yield return PFrames(3);
        yield return UClick("ragdoll-grab");
        Check("another tab: the tool begins", CharaEditTab.Current == null && UTool != null, UTools.Refusal);
        UTools.End(UTool);
        ui.Shell.MovePanel(RagdollUiModule.PanelId, LayoutRegions.Left);
        ui.Shell.ShowPanel(RagdollUiModule.PanelId);
        yield return PFrames(4);
    }

    // ------------------------------------------------------------ the panel

    static IEnumerator Play_U_3_Panel()
    {
        if (!new[] { "RY12", "RY13", "RY14", "RY15", "RY16" }.Any(Want)) yield break;
        current = "panel setup";
        if (uA == null || UUi?.Model.Find(uA) == null) yield return ULoad();
        if (uA == null) yield break;
        yield return UShowPanel();
        if (POpen("RY15", "the panel edits")) yield return USafe(RY15());
        if (POpen("RY14", "the blood option")) yield return USafe(RY14());
        if (POpen("RY16", "widths 360 and 300, three languages")) yield return USafe(RY16());
        if (POpen("RY13", "a closed panel reads nothing")) yield return USafe(RY13());
        if (POpen("RY12", "the section of the State tab")) yield return USafe(RY12());
    }

    static RagdollNumberRow UNumber(string value) => UP.Rows.OfType<RagdollNumberRow>().FirstOrDefault(r => r.Value == value);

    /// <summary>A slider's drag: press, two steps, release or none.</summary>
    static IEnumerator UDragSlider(NumberSlider s, float to, bool release = true)
    {
        var track = s.Slider;
        yield return UReveal(track);
        var b = track.worldBound;
        float x0 = b.xMin + b.width * 0.5f, y = b.center.y;
        float x1 = b.xMin + b.width * Mathf.InverseLerp((float)s.Min, (float)s.Max, to);
        URaw(EventType.MouseDown, new Vector2(x0, y));
        yield return PFrames(1);
        URaw(EventType.MouseDrag, new Vector2((x0 + x1) / 2, y), new Vector2((x1 - x0) / 2, 0));
        yield return PFrames(1);
        URaw(EventType.MouseDrag, new Vector2(x1, y), new Vector2((x1 - x0) / 2, 0));
        yield return PFrames(1);
        if (release)
        {
            URaw(EventType.MouseUp, new Vector2(x1, y));
            yield return PFrames(3);
        }
    }

    static object UValue(RagdollRow r) => r.OfBody ? UBlock.Values.Get(r.Value) : (UUi.Ops.RagdollWorldBlock?.Values ?? new RagdollWorldValues()).Get(r.Value);

    static IEnumerator RY15()
    {
        var ui = UUi;
        yield return UBody();
        var panel = UP;
        panel.MarkDirty();
        yield return PFrames(3);
        // every value of the two blocks has a row
        var names = RagdollBodyValues.Names.Concat(RagdollWorldValues.Names).ToList();
        var missing = names.Where(n => panel.Rows.All(r => r.Value != n)).ToList();
        Check("every value of the blocks has a row", missing.Count == 0, string.Join(", ", missing));
        Check("Present: the rows", panel.Rows.Count >= 40, panel.Rows.Count.ToString());

        int checkedRows = 0, bad = 0;
        string detail = "";
        foreach (var row in panel.Rows.OfType<RagdollNumberRow>().ToList())
        {
            yield return UReveal(row);
            var s = row.Slider;
            double before = s.Value, def = s.DefaultValue() ?? before;
            double to = s.Min.Value + (s.Max.Value - s.Min.Value) * 0.37;
            int u = UUndos;
            s.Field.value = (float)to;
            yield return PFrames(2);
            double read = Convert.ToDouble(UValue(row));
            bool wrote = Math.Abs(read - to) < 1e-3 * Math.Max(1, Math.Abs(to)) && UUndos == u + 1 && UTop == OpLabels.RagdollSettings;
            panel.MarkDirty();
            yield return PFrames(2);
            bool lit = s.ResetButton.enabledSelf;
            yield return UIn(UPt(s.ResetButton.worldBound.center, "click"));
            yield return PFrames(3);
            bool back = Math.Abs(Convert.ToDouble(UValue(row)) - def) < 1e-4 && !s.ResetButton.enabledSelf;
            checkedRows++;
            if (!wrote || !lit || !back) { bad++; detail += " " + row.name + "(" + read + "/" + to + " " + (UUndos - u) + " " + lit + " " + UValue(row) + ")"; }
        }
        Measure("RY15 number rows typed and reset: " + checkedRows);
        Check("every number row: a typed value is one entry, its default button lights, goes back and greys", bad == 0, bad + " of " + checkedRows + ":" + detail);

        foreach (var row in panel.Rows.OfType<RagdollSwitchRow>().ToList())
        {
            yield return UReveal(row);
            bool before = row.Toggle.value;
            int u = UUndos;
            row.Toggle.value = !before;
            yield return PFrames(3);
            Check(row.Value + ": a switch writes, one entry", Equals(UValue(row), !before) && UUndos == u + 1, (UUndos - u).ToString());
            row.Toggle.value = before;
            yield return PFrames(3);
        }
        Check("«Face the camera» is off by default", Equals(RagdollBodyValues.DefaultOf("faceCamera"), false));

        var kind = panel.Rows.OfType<RagdollChoiceRow>().First(r => r.Value == "propKind");
        yield return UReveal(kind);
        yield return UIn(UPt(kind.Buttons.Buttons[2].worldBound.center, "click"));
        yield return PFrames(3);
        Check("the prop's kind", ui.Ops.RagdollWorldBlock?.Values.PropKind == RagdollWorldValues.PropHeavy && UTop == OpLabels.RagdollSettings, ui.Ops.RagdollWorldBlock?.Values.PropKind);
        yield return UIn(UPt(kind.Buttons.Buttons[0].worldBound.center, "click"));
        yield return PFrames(2);

        // a drag: one entry, the value at once; Esc takes it back
        var muscle = UNumber("muscle");
        float m0 = UBlock.Values.Muscle;
        int u0 = UUndos;
        yield return UDragSlider(muscle.Slider, 45f, false);
        Check("a value shows during the drag", Mathf.Abs(UBlock.Values.Muscle - m0) > 1f && ui.ActiveEdit != null && ui.ActiveEdit.IsOpen, UBlock.Values.Muscle.ToString());
        URaw(EventType.MouseUp, muscle.Slider.worldBound.center);
        yield return PFrames(3);
        Check("a drag is one entry", UUndos == u0 + 1 && UTop == OpLabels.RagdollSettings, (UUndos - u0) + " " + UTop);
        yield return PCall(UApp, new { cmd = "undo" });
        yield return PFrames(3);
        Check("undo: the value back", Mathf.Abs(UBlock.Values.Muscle - m0) < 1e-5f, UBlock.Values.Muscle.ToString());
        u0 = UUndos;
        yield return UDragSlider(muscle.Slider, 50f, false);
        HS2Next.UI.Bridge.UiCommands.Send(UHost, JObject.FromObject(new { type = "key", key = "Escape" }), out _);
        yield return PFrames(3);
        Check("Esc during a drag: the value back, no entry, the edit closed", Mathf.Abs(UBlock.Values.Muscle - m0) < 1e-5f && UUndos == u0 && (ui.ActiveEdit == null || !ui.ActiveEdit.IsOpen), UBlock.Values.Muscle.ToString());
        URaw(EventType.MouseUp, muscle.Slider.worldBound.center);
        yield return PFrames(3);
        Check("after the release the slider shows the value again", Mathf.Abs((float)muscle.Slider.Value - m0) < 1e-3f, muscle.Slider.Value.ToString());

        // the room's sliders write when they stop
        var w = panel.Rows.OfType<RagdollPlaceRow>().First(r => r.name == "ragdoll-field-room.size-0");
        yield return UReveal(w);
        var s0 = UWorld.RoomSize;
        u0 = UUndos;
        var track = w.Slider.Slider;
        var b = track.worldBound;
        float y = b.center.y, x = b.xMin + b.width * 0.5f;
        URaw(EventType.MouseDown, new Vector2(x, y));
        yield return PFrames(1);
        var sw = Stopwatch.StartNew();
        bool moved = false;
        int rebuilt = 0;
        while (sw.Elapsed.TotalSeconds < 0.7)
        {
            x += 1.5f;
            URaw(EventType.MouseDrag, new Vector2(x, y), new Vector2(1.5f, 0));
            yield return PFrames(1);
            if (UWorld.RoomSize != s0) moved = true;
            rebuilt++;
        }
        Check("while the slider moves the room stays", !moved && panel.Delayed, rebuilt + " steps, " + UWorld.RoomSize + " " + panel.Delayed);
        double last = sw.Elapsed.TotalSeconds;
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => UWorld.RoomSize != s0, 2, ok);
        double took = sw.Elapsed.TotalSeconds - last;
        Measure("RY15 a stopped slider wrote after " + took.ToString("F3") + " s");
        Check("the slider stopped, held: the room moves about 0.3 s after the last change", ok[0] && took >= 0.25 && took <= 0.6, took.ToString("F3"));
        URaw(EventType.MouseUp, new Vector2(x, y));
        yield return PFrames(3);
        Check("one entry for the drag", UUndos == u0 + 1 && UTop == OpLabels.RagdollSettings, (UUndos - u0) + " " + UTop);
        Check("the block holds the size the slider shows", ui.Ops.RagdollWorldBlock != null && Mathf.Abs(ui.Ops.RagdollWorldBlock.Room.Size.X - (float)w.Slider.Value) < 1e-3f && Mathf.Abs(UWorld.RoomSize.x - (float)w.Slider.Value) < 1e-3f, ui.Ops.RagdollWorldBlock?.Room.Size.X + " / " + w.Slider.Value);

        // a typed value goes in at once, and the default button; each one entry
        u0 = UUndos;
        w.Slider.Field.value = 64f;
        yield return PFrames(2);
        Check("a typed size: at once, one entry", Mathf.Abs(UWorld.RoomSize.x - 64f) < 1e-3f && UUndos == u0 + 1, UWorld.RoomSize.x + " " + (UUndos - u0));
        panel.MarkDirty();
        yield return PFrames(2);
        Check("its default button lights", w.Slider.ResetButton.enabledSelf);
        yield return UIn(UPt(w.Slider.ResetButton.worldBound.center, "click"));
        yield return PFrames(3);
        Check("it goes back and greys, one entry", Mathf.Abs(UWorld.RoomSize.x - RagdollRoomValues.DefaultSize.X) < 1e-3f && !w.Slider.ResetButton.enabledSelf && UUndos == u0 + 2, UWorld.RoomSize.x + " " + (UUndos - u0));

        // released at once: the write comes at its time, into the same entry
        u0 = UUndos;
        var s1 = UWorld.RoomSize;
        yield return UDragSlider(w.Slider, 120f);
        Check("a quick drag: nothing yet", panel.Delayed || UWorld.RoomSize != s1, panel.Delayed + "");
        yield return PlayModeHarness.WaitRealtime(0.6);
        Check("then the room moves, one entry", UWorld.RoomSize != s1 && !panel.Delayed && UUndos == u0 + 1, UWorld.RoomSize + " " + (UUndos - u0));
        yield return PCall(UApp, new { cmd = "undo" });
        yield return PFrames(3);
        Check("undo: the room as it was", UWorld.RoomSize == s1, UWorld.RoomSize.ToString());

        // Esc
        u0 = UUndos;
        yield return UDragSlider(w.Slider, 150f, false);
        HS2Next.UI.Bridge.UiCommands.Send(UHost, JObject.FromObject(new { type = "key", key = "Escape" }), out _);
        yield return PFrames(3);
        URaw(EventType.MouseUp, track.worldBound.center);
        yield return PlayModeHarness.WaitRealtime(0.5);
        Check("Esc during a room's drag: nothing written, no entry", UWorld.RoomSize == s1 && UUndos == u0 && !panel.Delayed, UWorld.RoomSize + " " + (UUndos - u0));

        // the floor and the room at the selection
        var mode = panel.Rows.OfType<RagdollChoiceRow>().First(r => r.Value == RagdollEdits.FloorMode);
        yield return UReveal(mode);
        var worldBlock = ui.Ops.RagdollWorldBlock;
        Check("a fixed floor shows its height row", worldBlock.Floor.Mode == RagdollFloorValues.ModeFixed && UVisible(panel.Q("ragdoll-field-floor.height")));
        yield return UIn(UPt(mode.Buttons.Buttons[1].worldBound.center, "click"));
        yield return PFrames(3);
        Check("the floor's mode: feet, one entry, the height row gone", ui.Ops.RagdollWorldBlock.Floor.Mode == RagdollFloorValues.ModeFeet && UTop == OpLabels.RagdollSettings && !UVisible(panel.Q("ragdoll-field-floor.height")));
        yield return UIn(UPt(mode.Buttons.Buttons[2].worldBound.center, "click"));
        yield return PFrames(3);
        var pelvis = UBodyA.Part("pelvis").Tr.position;
        yield return UClick("ragdoll-room-at-selection");
        yield return PFrames(2);
        var c = ui.Ops.RagdollWorldBlock.Room.Center;
        Check("«Room at selection»: the centre at the character", c.HasValue && Mathf.Abs(c.Value.X - pelvis.x) < 0.5f && Mathf.Abs(c.Value.Y - pelvis.z) < 0.5f, c?.X + " " + c?.Y);

        // the body's buttons
        yield return UBody();
        panel.MarkDirty();
        yield return PFrames(16);
        var state = panel.Q<Label>("ragdoll-state");
        Check("the state line", state.text == ui.L10n.Get("ragdoll.state.up"), state.text);
        Check("the buttons by the body: no second body, a release", !panel.Q<Button>("ragdoll-make").enabledSelf && panel.Q<Button>("ragdoll-release").enabledSelf);
        foreach (var p in RagdollPreset.Names)
        {
            yield return UClick("ragdoll-preset-" + p);
            var want = new RagdollBodyValues();
            RagdollPreset.ApplyTo(want, p);
            Check("preset " + p, UBlock.Values.Muscle == want.Muscle && UBlock.Values.Pin == want.Pin && UBlock.Values.PinHipsOnly == want.PinHipsOnly && UTop == OpLabels.RagdollSettings, UBlock.Values.Muscle + " " + UBlock.Values.Pin);
        }
        yield return UClick("ragdoll-preset-puppet");
        yield return UClick("ragdoll-knock");
        Check("«Knock down»", UBodyA.State == RagdollState.Down, UBodyA.State.ToString());
        yield return PFrames(16);
        Check("the state line follows", state.text == ui.L10n.Get("ragdoll.state.down"), state.text);
        yield return UClick("ragdoll-snap");
        Check("«Snap to pose»", UBodyA.State == RagdollState.Up, UBodyA.State.ToString());
        yield return UClick("ragdoll-reseat");
        Check("«Reseat twisted» answers", UBodyA != null);

        // pins, ties, holds
        int u1 = UUndos;
        yield return UClick("ragdoll-add-pin");
        Check("«Pin»: a pin of the chosen part, one entry", UBlock.Pins.Count == 1 && UBlock.Pins[0].Part == "hand_L" && UUndos == u1 + 1, UBlock.Pins.Count.ToString());
        yield return UClick("ragdoll-add-tie");
        Check("«Tie»: the two chosen parts", UBlock.Ties.Count == 1 && UTop == OpLabels.RagdollTie, UBlock.Ties.Count.ToString());
        yield return UClick("ragdoll-add-hold");
        Check("«Hold pose»: the chosen part", UBlock.Holds.Count == 1 && UBlock.Holds[0].Part == "hand_L" && UTop == OpLabels.RagdollHold, UBlock.Holds.Count.ToString());
        yield return PFrames(16);
        Check("a row each", UVisible(panel.Q("ragdoll-pin-0")) && UVisible(panel.Q("ragdoll-tie-0")) && UVisible(panel.Q("ragdoll-hold-0")));
        ui.Ops.Select(new[] { uB });
        yield return PFrames(16);
        Check("something else selected: a character shows that character", panel.Node == uB);
        var added = ui.Session.AddFolder(null, null, -1, UiContext.User());
        string folder = added.Ok ? added.NewIds[0] : null;
        ui.Ops.Select(new[] { uA });
        yield return PFrames(16);
        if (folder != null)
        {
            ui.Ops.Select(new[] { folder });
            yield return PFrames(16);
            Check("a folder selected: the panel stays on the last character", panel.Node == uA, panel.Node);
            u1 = UUndos;
            yield return UClick("ragdoll-pin-0-follow");
            yield return PFrames(16);
            Check("«Follow selected»: the pin follows it, one entry", UBlock.Pins.Count == 1 && UBlock.Pins[0].Follow == folder && UUndos == u1 + 1, UBlock.Pins.Count + " " + (UUndos - u1));
            yield return UClick("ragdoll-pin-0-stay");
            yield return PFrames(16);
            Check("«Stay»: a plain pin", UBlock.Pins.Count == 1 && !UBlock.Pins[0].IsFollowing);
        }
        else Check("a folder for the pin to follow", false, added.Code + ": " + added.Message);
        ui.Ops.Select(new[] { uA });
        yield return PFrames(16);
        yield return UClick("ragdoll-tie-0-remove");
        Check("a tie goes", UBlock.Ties.Count == 0);
        yield return PFrames(16);
        yield return UClick("ragdoll-hold-0-remove");
        Check("a hold goes", UBlock.Holds.Count == 0);
        yield return PFrames(16);
        yield return UClick("ragdoll-pin-0-unpin");
        Check("a pin goes", UBlock.Pins.Count == 0);

        // an item
        yield return PCall(UApp, new { cmd = "item.add", group = 0, category = 0, no = 1 });
        string item = Key(pLast);
        Info("item: " + Short(pLastText));
        if (item != null)
        {
            ui.Ops.Select(new[] { item });
            yield return PFrames(16);
            var ob = panel.Q<Toggle>("ragdoll-obstacle");
            yield return UReveal(ob);
            Check("an item selected: its switch shows", UVisible(ob) && !ob.value);
            int u2 = UUndos;
            ob.value = true;
            yield return PFrames(3);
            Check("«Obstacle»: the block lists it, one entry", ui.Ops.RagdollWorldBlock.Obstacles.Contains(item) && UUndos == u2 + 1 && UTop == OpLabels.RagdollObstacle, UTop);
            yield return PCall(UApp, new { cmd = "undo" });
            yield return PFrames(3);
        }
        ui.Ops.Select(new[] { uA, uB });
        yield return PFrames(16);
        Check("two selected: the primary's, its rows greyed", panel.Node == ui.Selection.Primary && UVisible(panel.Q("ragdoll-primary")) && !UNumber("muscle").enabledInHierarchy && UNumber("gravity").enabledInHierarchy);
        ui.Ops.Select(Array.Empty<string>());
        yield return PFrames(16);
        Check("no selection: the note, the scene's rows stay", panel.Node == null && UVisible(panel.Q("ragdoll-none")) && !UVisible(UNumber("muscle")) && UNumber("gravity") != null);
        ui.Ops.Select(new[] { uA });
        yield return PFrames(16);

        // the body's end
        int u3 = UUndos;
        yield return UClick("ragdoll-release");
        yield return PlayModeHarness.WaitRealtime(0.8);
        Check("«Release»: the body goes after its blend, one entry", ui.Ops.RagdollBodyOf(uA) == null && UUndos == u3 + 1 && UTop == OpLabels.RagdollRelease, (ui.Ops.RagdollBodyOf(uA) != null) + " " + UTop);
        yield return UClick("ragdoll-make");
        Check("«Ragdoll»: a body, one entry", ui.Ops.RagdollBodyOf(uA) != null && UTop == OpLabels.RagdollBuild, UTop);
        yield return UClick("ragdoll-release-all");
        Check("«Release all»: no world", UWorld == null);
        Check("no world: the outline switches are greyed", !UP.Q<Toggle>("ragdoll-colliders").enabledSelf && !UP.Q<Toggle>("ragdoll-room").enabledSelf);
        yield return UClick("ragdoll-make");
        Check("a body again: «Colliders» is live", UP.Q<Toggle>("ragdoll-colliders").enabledSelf);
        var fkBefore = UBlock.On;
        yield return PFrames(8);
        yield return UClick("ragdoll-bake");
        Check("«Bake pose to FK»: the body goes, FK on, one entry", ui.Ops.RagdollBodyOf(uA) == null && UTop == OpLabels.RagdollBake, UTop);
        yield return PCall(UApp, new { cmd = "undo" });
        yield return PFrames(3);
        ui.ShowResult(ui.Ops.RagdollReleaseAll(UiContext.User(OpLabels.RagdollRelease)));
    }

    static IEnumerator RY14()
    {
        var ui = UUi;
        var panel = UP;
        yield return UBody();
        string key = OptionDefs.StudioBlood;
        Check("the option is on by default, the effects with it", ui.Options.Bool(key) && RagdollFx.ShowBlood, ui.Options.Bool(key) + " " + RagdollFx.ShowBlood);
        OptionsPanel.Show(ui);
        yield return PFrames(3);
        var op = OptionsPanel.Open;
        var row = op?.Controls.TryGetValue(key, out var c) == true ? c as Toggle : null;
        if (!Check("the Options row", row != null)) yield break;
        Check("it reads «Show blood»", row.label == "Show blood", row.label);
        int undos = UUndos;
        string doc = ui.Model.Ragdoll?.ToJson();
        row.value = false;
        yield return PFrames(2);
        Check("the row writes the option, the effects follow", !ui.Options.Bool(key) && !RagdollFx.ShowBlood);
        ui.Dialogs.CloseCurrent();
        yield return PFrames(2);
        ui.Options.Save();
        var saved = OptionsDocument.Parse(File.ReadAllText(ui.Options.FilePath, Encoding.UTF8));
        Check("options.json holds it", Equals(saved.Get(key), false), saved.ToJson());
        Check("no entry, no block changed: it is no part of the scene", UUndos == undos && ui.Model.Ragdoll?.ToJson() == doc);
        panel.MarkDirty();
        yield return PFrames(3);
        var mirror = panel.Q<Toggle>("ragdoll-blood");
        Check("the panel's switch shows it", !mirror.value);

        yield return UGrabOn();
        var tool = UTool;
        tool.Shooting = true;
        Check("Present: a ray through the chest hits a part", UHit("chest", out var on, out _));
        var shots = RagdollShots.For(URt);
        shots.Clear();
        RagdollTool.StainOnHit = false;
        yield return UIn(UPt(on, "down"), UPt(on, "up"));
        yield return PFrames(1);
        var fx = shots.Fx;
        Check("with it off a hit makes no droplet", tool.Shots == 1 && fx != null && fx.Droplets == 0, tool.Shots + " " + fx?.Droplets);

        yield return UReveal(mirror);
        mirror.value = true;
        yield return PFrames(2);
        Check("the panel's switch writes the option", ui.Options.Bool(key) && RagdollFx.ShowBlood);
        Check("Present: a ray through the chest hits a part", UHit("chest", out on, out _));
        yield return UIn(UPt(on, "down"), UPt(on, "up"));
        yield return PFrames(1);
        Check("Present: with it on the same hit makes droplets", shots.Fx != null && shots.Fx.Droplets >= 6, shots.Fx?.Droplets.ToString());
        ui.Options.Save();
        saved = OptionsDocument.Parse(File.ReadAllText(ui.Options.FilePath, Encoding.UTF8));
        Check("options.json follows", Equals(saved.Get(key), true), saved.ToJson());
        RagdollTool.StainOnHit = true;
        UTools.End(tool);
        ui.ShowResult(ui.Ops.RagdollReleaseAll(UiContext.User(OpLabels.RagdollRelease)));
        yield return PFrames(2);
    }

    static IEnumerator RY16()
    {
        var ui = UUi;
        var sh = ui.Shell;
        var host = UHost;
        yield return UBody();
        RagdollUiModule.Show(ui);
        yield return PFrames(3);
        sh.MovePanel(RagdollUiModule.PanelId, LayoutRegions.Left);
        sh.ShowPanel(RagdollUiModule.PanelId);
        yield return PFrames(4);
        ui.ShowResult(ui.Ops.RagdollPin(uA, "hand_R", null, null, null, false, UiContext.User(OpLabels.RagdollPin)));
        ui.ShowResult(ui.Ops.RagdollTie(uA, "hand_L", "foot_L", UiContext.User(OpLabels.RagdollTie)));
        ui.ShowResult(ui.Ops.RagdollHold(uA, "forearm_L", UiContext.User(OpLabels.RagdollHold)));
        yield return PFrames(20);
        var left = sh.Regions[LayoutRegions.Left];
        foreach (var f in UP.Query<Foldout>().ToList()) f.value = true;
        foreach (float width in new[] { 360f, 300f })
        {
            sh.SetSize(left, width);
            yield return PFrames(4);
            foreach (var lang in new[] { "en", "ja", "zh-Hans" })
            {
                host.SetLanguage(lang, save: false);
                yield return PFrames(4);
                sh.ShowPanel(RagdollUiModule.PanelId);
                yield return PFrames(4);
                string tag = width.ToString("0") + " px " + lang;
                var root = UP.worldBound;
                Check(tag + ": the panel fits its region", root.width > 100 && root.width <= width + 0.5f, root.ToString());
                var outside = new List<string>();
                foreach (var name in new[] { "ragdoll-make", "ragdoll-release", "ragdoll-release-all", "ragdoll-bake", "ragdoll-snap", "ragdoll-preset-limp", "ragdoll-preset-stiff", "ragdoll-preset-puppet", "ragdoll-preset-hipsPin", "ragdoll-mode-off", "ragdoll-grab", "ragdoll-shoot", "ragdoll-colliders", "ragdoll-room", "ragdoll-pin-0-follow", "ragdoll-pin-0-unpin", "ragdoll-tie-0-remove", "ragdoll-hold-0-remove", "ragdoll-add-pin", "ragdoll-add-tie", "ragdoll-add-hold", "ragdoll-unpin-all", "ragdoll-part-a", "ragdoll-part-b", "ragdoll-knock", "ragdoll-reseat", "ragdoll-clear-props", "ragdoll-room-at-selection" })
                {
                    var e = UP.Q(name);
                    var b = e?.worldBound ?? default;
                    if (e == null || b.width < 4 || b.xMin < root.xMin - 0.5f || b.xMax > root.xMax + 0.5f) outside.Add(name);
                }
                Check(tag + ": every control lies within the panel", outside.Count == 0, string.Join(", ", outside));
                var cut = new List<string>();
                var sizes = new List<string>();
                float leastNeed = float.MaxValue;
                foreach (var name in new[] { "ragdoll-mode-off", "ragdoll-grab", "ragdoll-shoot" })
                {
                    var mb = UP.Q<Button>(name);
                    float need = mb?.MeasureTextSize(mb.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x ?? float.MaxValue;
                    float has = mb?.contentRect.width ?? 0;
                    leastNeed = Math.Min(leastNeed, need);
                    sizes.Add(has.ToString("0.#") + "/" + need.ToString("0.#"));
                    if (mb == null || has + 0.5f < need) cut.Add(name + " " + has.ToString("0.#") + " < " + need.ToString("0.#"));
                }
                Check(tag + ": Present: the three texts measure", leastNeed >= 10f && leastNeed < float.MaxValue, string.Join(", ", sizes));
                Check(tag + ": the three choices of the tool show their whole text", cut.Count == 0, string.Join(", ", sizes) + (cut.Count > 0 ? "; cut: " + string.Join(", ", cut) : ""));
                var narrow = UP.Rows.Where(r => UVisible(r) && r.worldBound.xMax > root.xMax + 0.5f).Select(r => r.name).ToList();
                Check(tag + ": the value rows lie within the panel", narrow.Count == 0, string.Join(", ", narrow));
            }
        }
        host.SetLanguage("en", save: false);
        sh.ResetLayout();
        yield return PFrames(4);
        ui.ShowResult(ui.Ops.RagdollReleaseAll(UiContext.User(OpLabels.RagdollRelease)));
    }

    static IEnumerator RY13()
    {
        var ui = UUi;
        yield return UBody();
        RagdollUiModule.Show(ui);
        yield return PFrames(4);
        var panel = UP;
        int r0 = panel.Reads;
        var bodyAt = UBodyA.Part("chest").Rb.position;
        ui.ShowResult(ui.Ops.RagdollKnock(uA));
        yield return PlayModeHarness.WaitRealtime(0.8);
        Measure("RY13 reads in 0.8 s while shown: " + (panel.Reads - r0));
        Check("Present: shown, the panel reads", panel.Reads - r0 >= 2, (panel.Reads - r0).ToString());
        yield return PCall(UApp, new { cmd = "ui.action", action = ActionIds.RagdollPanel });
        yield return PFrames(3);
        int reads = panel.Reads;
        var chest = UBodyA.Part("chest");
        var was = chest.Rb.position;
        ui.Session.Set(uB, "transform.pos", new Vec3(20, 0, 0), UiContext.User());
        yield return PlayModeHarness.WaitRealtime(0.8);
        Check("Present: the body moved and the model changed meanwhile", (chest.Rb.position - bodyAt).magnitude > 0.01f, (chest.Rb.position - bodyAt).magnitude.ToString("F3"));
        Check("a closed panel reads nothing", !RagdollUiModule.PanelShown(ui) && panel.Reads == reads, (panel.Reads - reads) + " reads");
    }

    static IEnumerator RY12()
    {
        var ui = UUi;
        yield return UBody();
        ui.Shell.ResetLayout();
        yield return PFrames(3);
        ui.Shell.FocusPanel("inspector");
        yield return PFrames(3);
        var root = ui.Shell.PanelRoot("inspector");
        var insp = root as InspectorPanel ?? root?.Q<InspectorPanel>();
        insp?.Chara?.ShowTab("state");
        yield return PFrames(4);
        var section = UPanel.visualTree.Q<RagdollSection>();
        if (!Check("the section stands in the State tab, closed", section != null && !section.Fold.value, (insp?.Chara != null) + " " + (section != null))) yield break;
        var wet = UPanel.visualTree.Q<WetSection>();
        Check("it stands at the end of the tab, before the stains'", section.parent == wet.parent && section.parent.IndexOf(section) == section.parent.childCount - 2, section.parent.IndexOf(section) + " of " + section.parent.childCount);
        Check("the strip stays at 13 tabs", insp.Chara.TabBodies.Count == 13, insp.Chara.TabBodies.Count.ToString());
        int sr = section.Reads;
        yield return PFrames(30);
        Check("a closed section reads nothing", section.Reads == sr, (section.Reads - sr).ToString());
        section.Fold.value = true;
        yield return PFrames(3);
        var state = section.Q<Label>("ragdoll-section-state");
        Check("opened: the state", state != null && state.text == ui.L10n.Get("ragdoll.state.up"), state?.text);
        Check("Present: open, it reads", section.Reads > sr, (section.Reads - sr).ToString());
        var make = section.Q<Button>("ragdoll-section-make");
        var release = section.Q<Button>("ragdoll-section-release");
        Check("the buttons by the body", !make.enabledSelf && release.enabledSelf);
        yield return UReveal(release);
        int u = UUndos;
        yield return UIn(UPt(release.worldBound.center, "click"));
        yield return PlayModeHarness.WaitRealtime(0.8);
        Check("«Release»: the body goes, one entry", ui.Ops.RagdollBodyOf(uA) == null && UUndos == u + 1, (ui.Ops.RagdollBodyOf(uA) != null) + " " + (UUndos - u));
        yield return UIn(UPt(make.worldBound.center, "click"));
        yield return PFrames(3);
        Check("«Ragdoll»: a body, one entry", ui.Ops.RagdollBodyOf(uA) != null && UUndos == u + 2, (UUndos - u).ToString());
        var open = section.Q<Button>("ragdoll-section-open");
        yield return UReveal(open);
        yield return UIn(UPt(open.worldBound.center, "click"));
        yield return PFrames(4);
        Check("«Open in panel» shows the panel", RagdollUiModule.PanelShown(ui) && UP != null);
        section.Fold.value = false;
        yield return PFrames(2);
        ui.ShowResult(ui.Ops.RagdollReleaseAll(UiContext.User(OpLabels.RagdollRelease)));
        ui.Shell.ResetLayout();
        yield return PFrames(3);
    }
}
