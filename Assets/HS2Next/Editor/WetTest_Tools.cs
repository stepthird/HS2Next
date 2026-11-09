// WetTest.play, the viewport tools (package U): the app with the UI in Editor play, input through ui.input, judged by the tool's calls, the camera, the
// selection, the gizmo and the history; a picture stays in memory and is judged by numbers.
//   WI1  default mode: camera, wheel, double click, shortcuts, held keys, gizmo, selection and the right-click menu do nothing; a panel works; the end restores each
//   WI2  PassThrough: an unclaimed press drags the camera, a click selects, a key and the wheel go on; a claimed press, key and wheel stay the tool's
//   WI3  Esc: a held press first, then the tool; a popup, a drag edit, a text field and a dialog keep their turn; no repeat, no Esc reaches Key
//   WI4  the window's focus and the pointer capture lost during a press; a tool that ends during a press; a tool that throws
//   WI5  replaced, a new scene, a load, the owning node deleted, the Edit tab, the UI closing, refused begins, the hint
//   WI6  the pick: point, normal, UV and triangle against a known triangle, slot, bone and addresses; filters; a hidden renderer; a mesh that cannot be read
//   WI7  what a tool draws shows in the view and never in a capture
// Moves and presses come from ui.input: no real pointer is involved here.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next.Api.Unity;
using HS2Next.App;
using HS2Next.App.Commands;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Gizmos;
using HS2Next.UI.Input;
using HS2Next.UI.Panels;
using HS2Next.UI.Viewport;
using HS2Next.UI.Widgets;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class WetTest
{
    /// <summary>A tool that writes down its calls.</summary>
    sealed class ProbeTool : ViewportTool
    {
        public readonly List<string> Calls = new List<string>();
        public List<string> Shared;
        public string Name = "";
        public bool Claim, ClaimWheel, EndOnDown;
        public string ClaimKey, ThrowIn;
        public Vector3 DrawAt;
        public bool Draws;
        public int DrawCalls;
        public ToolPick Hit;
        public Action<ProbeTool> OnDown;

        void Note(string s) { Calls.Add(s); Shared?.Add(Name + " " + s); if (ThrowIn != null && s.StartsWith(ThrowIn, StringComparison.Ordinal)) throw new InvalidOperationException("the probe throws in " + ThrowIn); }
        public override void Started() => Note("Started");
        public override bool PointerDown(in ToolPointer p)
        {
            Note("Down " + p.Button + " " + p.Buttons + " " + p.Mods);
            Hit = p.Hit;
            if (EndOnDown) Tools.End(this);
            OnDown?.Invoke(this);
            return Claim;
        }
        public override void PointerMove(in ToolPointer p) => Note("Move " + p.Button + " " + p.Buttons);
        public override void PointerUp(in ToolPointer p) => Note("Up " + p.Button + " " + p.Buttons);
        public override void PointerCancel() => Note("Cancel");
        public override bool Wheel(float notches, in ToolPointer p) { Note("Wheel " + (notches > 0 ? "up" : "down")); return ClaimWheel; }
        public override bool Key(string key, ToolMods mods) { Note("Key " + key + " " + mods); return key == ClaimKey; }
        public override void Draw(IToolDraw draw)
        {
            DrawCalls++;
            if (!Draws) return;
            draw.Marker(DrawAt, 1.5f, Color.yellow);
            draw.Line(DrawAt, DrawAt + Vector3.up * 3, Color.cyan);
            draw.Label(DrawAt, "probe");
        }
        public override void Ended(ToolEnd reason) => Note("Ended " + reason);
        public int Count(string prefix) => Calls.Count(c => c.StartsWith(prefix, StringComparison.Ordinal));
        public string Last => Calls.Count > 0 ? Calls[Calls.Count - 1] : null;
        public string Text => string.Join(", ", Calls);
    }

    sealed class FakeKeys : IInputSource
    {
        public readonly HashSet<KeyCode> Down = new HashSet<KeyCode>();
        public bool GetKey(KeyCode k) => Down.Contains(k);
        public Vector3 MousePosition => Vector3.zero;
        public string CompositionString => "";
        public bool AnyKey => Down.Count > 0;
    }

    static AppCore TApp => AppCore.Current;
    static UiHost THost => UiHost.Current;
    static UiContext TUi => THost?.Context;
    static ToolHost Tools => TUi.Tools;
    static ViewportHost Vp => TUi.Viewport;
    static TransformTool TGizmo => Vp.Gizmo;
    static IPanel TPanel => THost?.Root?.panel;
    static int TUndos => TUi.Session.Commands.UndoList.Count;
    static string tA, tB;
    static Vector2 tEmpty, tOnA, tOnB;

    static IEnumerator TIn(params JObject[] events)
    {
        yield return PCall(TApp, new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(events.Cast<object>().ToArray()), ["frames"] = 1 });
        if (!POk) Check("ui.input", false, Short(pLastText));
        yield return PFrames(1);
    }

    static JObject TPt(Vector2 p, string phase, int button = 0, string mods = null, int clicks = 1)
    {
        var o = new JObject { ["type"] = "pointer", ["x"] = p.x, ["y"] = p.y, ["phase"] = phase, ["button"] = button, ["clicks"] = clicks };
        if (mods != null) o["mods"] = mods;
        return o;
    }

    static JObject TWheel(Vector2 p, float delta) => new JObject { ["type"] = "wheel", ["x"] = p.x, ["y"] = p.y, ["delta"] = delta };

    static JObject TKey(string key, string mods = null)
    {
        var o = new JObject { ["type"] = "key", ["key"] = key };
        if (mods != null) o["mods"] = mods;
        return o;
    }

    /// <summary>A drag: press, two moves, release.</summary>
    static JObject[] TDrag(Vector2 p, int button = 0) => new[] { TPt(p, "down", button), TPt(p + new Vector2(30, 12), "move", button), TPt(p + new Vector2(60, 24), "move", button), TPt(p + new Vector2(60, 24), "up", button) };

    static VisualElement TCapturing()
    {
        for (int id = 0; id < PointerId.maxPointers; id++) if (TPanel.GetCapturingElement(id) is VisualElement v) return v;
        return null;
    }

    static string TCam()
    {
        var t = Vp.ViewCamera.transform;
        return t.position.ToString("F4") + " " + t.rotation.ToString("F4");
    }

    /// <summary>One render of the view camera, so that the gizmo's state is this frame's.</summary>
    static void TRender(bool read = false, List<Color32[]> into = null)
    {
        var cam = Vp.ViewCamera;
        int w = read ? 480 : Mathf.Max(64, cam.pixelWidth), h = read ? 270 : Mathf.Max(64, cam.pixelHeight);
        var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        var prevT = cam.targetTexture;
        var prevR = cam.rect;
        THost.GizmosIntoTargets = true;
        try
        {
            cam.targetTexture = rt;
            cam.rect = new Rect(0, 0, 1, 1);
            cam.Render();
            if (read)
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
            THost.GizmosIntoTargets = false;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    /// <summary>A point of an arm of the shown handle that the hit test finds, on the view.</summary>
    static Vector2 THandleTip()
    {
        TRender();
        var gz = TGizmo;
        var vb = Vp.Element.worldBound;
        Vector2 c = gz.ToPanel(gz.PivotWorld);
        for (int i = 0; i < 3; i++)
        {
            var a = gz.Axes * (i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward);
            foreach (float f in new[] { 0.9f, 0.75f, 0.6f, 0.45f, 0.3f })
            {
                Vector2 q = gz.ToPanel(gz.PivotWorld + a * (gz.LengthWorld * f));
                if (gz.HitTest(q).Kind == HandleKind.MoveAxis && vb.Contains(q) && (q - c).magnitude >= 12) return q;
            }
        }
        Check("an arm of the handle can be found", false, "pivot " + c + ", length " + gz.LengthWorld + ", tool " + gz.ShownTool);
        return c;
    }

    static bool TIdle() => !Tools.Running && !Tools.PressHeld && TCapturing() == null;

    static IEnumerator Play_Tools()
    {
        if (!new[] { "WI1", "WI2", "WI3", "WI4", "WI5", "WI6", "WI7" }.Any(Want)) yield break;
        current = "tools";
        rep.AppendLine();
        rep.AppendLine("== tools setup");
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting && UiHost.Current != null && UiHost.Current.IsReady, 240, ok);
        if (!Check("app and UI ready", ok[0])) yield break;
        yield return TLoadScene();
        if (tA == null || tB == null) yield break;
        if (POpen("WI1", "default mode")) yield return WI1();
        if (POpen("WI2", "PassThrough")) yield return WI2();
        if (POpen("WI3", "Esc")) yield return WI3();
        if (POpen("WI4", "focus, capture, ending during a press, a tool that throws")) yield return WI4();
        if (POpen("WI6", "the pick")) yield return WI6();
        if (POpen("WI7", "drawing, never in a capture")) yield return WI7();
        if (POpen("WI5", "ending")) yield return WI5();
        if (Tools.Running) { Check("no tool left running", false, Tools.Current?.GetType().Name); Tools.Close(); }
    }

    static IEnumerator TLoadScene()
    {
        yield return PCall(TApp, new { cmd = "scene.new" });
        Check("scene.new", POk, Short(pLastText));
        yield return PCall(TApp, new { cmd = "chara.load", path = femaleRel, sex = "female" });
        Check("chara.load female", POk, Short(pLastText));
        tA = Key(pLast);
        yield return PCall(TApp, new { cmd = "chara.load", path = maleRel, sex = "male" });
        Check("chara.load male", POk, Short(pLastText));
        tB = Key(pLast);
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => TApp.Runtime.FindChara(tA)?.State == RealizationState.Built && TApp.Runtime.FindChara(tB)?.State == RealizationState.Built, 180, ok);
        if (!Check("both built", ok[0])) { tA = tB = null; yield break; }
        TUi.Shell.ResetLayout();
        var posA = TUi.Model.Find(tA).Doc.Transform.Pos;
        TUi.Ops.SetTransform(new[] { tB }, new Vector3(posA.X + 12, posA.Y, posA.Z), null, null, UiContext.User());
        yield return PFrames(3);
        TUi.Ops.Select(new[] { tA });
        TUi.Tool = GizmoTool.Move;
        yield return TLoadView();
        var wb = Vp.Element.worldBound;
        tEmpty = new Vector2(wb.xMin + 24, wb.yMin + 24);
        Info("screen " + Screen.width + "x" + Screen.height + ", viewport " + wb + ", on A " + tOnA + ", on B " + tOnB);
        Check("the three points lie on the view", TPanel.Pick(tEmpty) == Vp.Element && TPanel.Pick(tOnA) == Vp.Element && TPanel.Pick(tOnB) == Vp.Element);
        Check("the empty point hits nothing, the points on the characters hit them", Vp.Picker.Pick(Vp, tEmpty).NodeId == null && Vp.Picker.Pick(Vp, tOnA).NodeId == tA && Vp.Picker.Pick(Vp, tOnB).NodeId == tB,
            Vp.Picker.Pick(Vp, tEmpty).NodeId + " / " + Vp.Picker.Pick(Vp, tOnA).NodeId + " / " + Vp.Picker.Pick(Vp, tOnB).NodeId);
    }

    // ------------------------------------------------------------ WI1

    static IEnumerator WI1()
    {
        var router = THost.Router;
        var ui = TUi;
        Vector2 tip = THandleTip();
        Check("before: the gizmo shows", TGizmo.Visible);
        var probe = new ProbeTool();
        Check("Begin: true, Started, running in the default mode", Tools.Begin(probe) && probe.Text == "Started" && Tools.Running && Tools.Blocks, probe.Text);
        TRender();
        Check("handles hidden", !TGizmo.Visible);
        string cam = TCam();
        int undos = TUndos;
        var sel = ui.Selection.Ids.ToList();

        yield return TIn(TDrag(tEmpty));
        Check("a drag: the camera stays", TCam() == cam, TCam());
        Check("a drag: the tool hears the press and nothing else of it", probe.Count("Down 0 1 None") == 1 && probe.Count("Up") == 0 && probe.Count("Move") == 0, probe.Text);
        Check("the pointer is free after the release", TCapturing() == null);
        yield return TIn(TPt(tOnA, "down", 0, null, 2), TPt(tOnA, "up", 0, null, 2));
        Check("a double click frames nothing", TCam() == cam);
        yield return TIn(TWheel(tEmpty, 3));
        Check("the wheel: the tool hears it, no dolly", TCam() == cam && probe.Count("Wheel down") == 1, probe.Text);
        yield return TIn(TKey("E"));
        Check("a shortcut: the tool hears it, nothing runs", ui.Tool == GizmoTool.Move && probe.Count("Key E None") == 1, ui.Tool + "; " + probe.Text);
        var fake = new FakeKeys();
        var src = router.Source;
        router.Source = fake;
        fake.Down.Add(KeyCode.UpArrow);
        yield return PFrames(3);
        Check("a held camera key: no held action, no move", router.LastHeld.Count == 0 && TCam() == cam, router.LastHeld.Count + " held");
        fake.Down.Clear();
        router.Source = src;

        yield return TIn(TPt(tip, "move"));
        Check("a hover: the tool hears it, no handle lights", probe.Count("Move -1 0") >= 1 && TGizmo.HoverHandle.Kind == HandleKind.None, probe.Text + "; " + TGizmo.HoverHandle);
        var before = probe.Calls.Count;
        yield return TIn(TDrag(tip));
        Check("a press on a handle: no drag, no entry", !TGizmo.Dragging && TUndos == undos && TCam() == cam, TUndos - undos + " entries");
        yield return TIn(TPt(tEmpty, "click"));
        Check("a click in empty space keeps the selection", ui.Selection.Ids.SequenceEqual(sel));
        yield return TIn(TPt(tOnB, "click"));
        Check("a click on another node keeps the selection", ui.Selection.Ids.SequenceEqual(sel), string.Join(",", ui.Selection.Ids));
        yield return TIn(TPt(tOnB, "click", 1));
        Check("a right click opens no menu", !ui.Menus.IsOpen);

        int downs = probe.Count("Down");
        yield return PCall(TApp, new { cmd = "ui.input", frames = 2, events = new object[] { new { type = "pointer", target = "tb-tool.rotate", phase = "click", button = 0 } } });
        Check("a toolbar button works and is not the tool's", POk && ui.Tool == GizmoTool.Rotate && probe.Count("Down") == downs, ui.Tool.ToString());
        ui.Tool = GizmoTool.Move;
        yield return PFrames(1);

        Tools.End(probe);
        Check("End: Ended(Finished) is the last call, the host is idle", probe.Last == "Ended Finished" && TIdle(), probe.Last);
        int calls = probe.Calls.Count;

        TRender();
        Check("the end: the gizmo shows", TGizmo.Visible);
        yield return TIn(TDrag(tEmpty));
        Check("the end: a drag moves the camera", TCam() != cam);
        string cam2 = TCam();
        yield return TIn(TWheel(tEmpty, 3));
        Check("the end: the wheel dollies", TCam() != cam2);
        yield return TIn(TKey("E"));
        Check("the end: a shortcut runs", ui.Tool == GizmoTool.Rotate, ui.Tool.ToString());
        yield return TIn(TKey("W"));
        router.Source = fake;
        fake.Down.Add(KeyCode.UpArrow);
        string cam3 = TCam();
        yield return PFrames(3);
        Check("the end: a held key moves the camera", router.LastHeld.Count > 0 && TCam() != cam3, router.LastHeld.Count + " held");
        fake.Down.Clear();
        router.Source = src;
        yield return TIn(TPt(tOnB, "click"));
        yield return PFrames(1);
        Check("the end: a click selects", ui.Selection.Primary == tB, ui.Selection.Primary);
        ui.Ops.Select(new[] { tA });
        yield return TLoadView();
        tip = THandleTip();
        yield return TIn(TPt(tip, "down"));
        Check("the end: a handle drags", TGizmo.Dragging);
        yield return TIn(TPt(tip + new Vector2(20, 0), "move"), TPt(tip + new Vector2(20, 0), "up"));
        if (Check("the end: the drag is one entry", TUndos == undos + 1, (TUndos - undos).ToString())) yield return PCall(TApp, new { cmd = "undo" });
        Check("the tool heard none of it", probe.Calls.Count == calls, probe.Text);
        yield return TLoadView();
    }

    /// <summary>The view of the setup again.</summary>
    static IEnumerator TLoadView()
    {
        var hipsA = TApp.Runtime.FindChara(tA).Context.BodyBones["cf_J_Hips"].position;
        var hipsB = TApp.Runtime.FindChara(tB).Context.BodyBones["cf_J_Hips"].position;
        Vp.Camera.SetView((hipsA + hipsB) * .5f, new Vector3(8, 180, 0), 80, 23);
        Vp.Camera.Flush();
        yield return PFrames(2);
        tOnA = TGizmo.ToPanel(hipsA);
        tOnB = TGizmo.ToPanel(hipsB);
    }

    // ------------------------------------------------------------ WI2

    static IEnumerator WI2()
    {
        var router = THost.Router;
        var ui = TUi;
        var probe = new ProbeTool();
        Check("Begin PassThrough", Tools.Begin(probe, new ToolOptions { PassThrough = true }) && Tools.Running && !Tools.Blocks);
        TRender();
        Check("the gizmo shows", TGizmo.Visible, ui.Tool + ", selected " + ui.Selection.Primary);
        string cam = TCam();
        yield return TIn(TDrag(tEmpty));
        Check("an unclaimed press drags the camera", TCam() != cam, TCam());
        Check("the tool hears only the press", probe.Count("Down") == 1 && probe.Count("Up") == 0 && probe.Count("Move") == 0, probe.Text);
        cam = TCam();
        yield return TIn(TWheel(tEmpty, 3));
        Check("an unclaimed wheel dollies", TCam() != cam && probe.Count("Wheel down") == 1, probe.Text);
        yield return TIn(TKey("E"));
        Check("an unclaimed key runs its shortcut", ui.Tool == GizmoTool.Rotate && probe.Count("Key E None") == 1, ui.Tool + "; " + probe.Text);
        yield return TIn(TKey("W"));
        yield return TLoadView();
        yield return TIn(TPt(tOnB, "click"));
        Check("an unclaimed click selects", ui.Selection.Primary == tB, ui.Selection.Primary);
        ui.Ops.Select(new[] { tA });
        yield return PFrames(1);
        Vector2 tip = THandleTip();
        yield return TIn(TPt(tip, "move"));
        Check("a hover: both hear it", probe.Count("Move -1 0") >= 1 && TGizmo.HoverHandle.Kind != HandleKind.None, TGizmo.HoverHandle.ToString());
        int entries = TUndos;
        yield return TIn(TPt(tip, "down"));
        Check("an unclaimed press on a handle drags it", TGizmo.Dragging);
        yield return TIn(TPt(tip + new Vector2(14, 0), "move"), TPt(tip + new Vector2(14, 0), "up"));
        if (TUndos > entries) yield return PCall(TApp, new { cmd = "undo" });
        yield return TLoadView();
        tip = THandleTip();

        probe.Claim = true;
        probe.ClaimWheel = true;
        probe.ClaimKey = "T";
        cam = TCam();
        int n = probe.Calls.Count, undos = TUndos;
        yield return TIn(TPt(tip, "down"));
        Check("a claimed press: no gizmo drag, the tool holds the pointer", Tools.PressHeld && !TGizmo.Dragging && TCapturing() == Vp.Element, probe.Text);
        yield return TIn(TPt(tip + new Vector2(10, 0), "move"));
        Check("a claimed press: its moves are the tool's", probe.Calls.Last() == "Move -1 1", probe.Last);
        yield return TIn(TPt(tip + new Vector2(10, 0), "down", 1));
        Check("a second button: no Down", probe.Count("Down 1") == 0);
        yield return TIn(TPt(tip + new Vector2(20, 0), "move"));
        Check("a second button shows in Buttons", probe.Calls.Last() == "Move -1 3", probe.Last);
        yield return TIn(TWheel(tip, -3));
        Check("the wheel during a press is the tool's", probe.Calls.Last() == "Wheel up" && TCam() == cam, probe.Last);
        yield return TIn(TPt(tip + new Vector2(20, 0), "up", 1));
        Check("the second button's release ends nothing", Tools.PressHeld && probe.Count("Up") == 0, probe.Text);
        yield return TIn(TPt(tip + new Vector2(20, 0), "up"));
        Check("the release: Up with the claimed button, the pointer free", probe.Calls.Last() == "Up 0 0" && !Tools.PressHeld && TCapturing() == null, probe.Last);
        Check("a claimed press: the camera and the history stay", TCam() == cam && TUndos == undos, (TUndos - undos).ToString());
        yield return TIn(TDrag(tEmpty));
        Check("a second claimed press in a row works", probe.Count("Up") == 2 && TCam() == cam, probe.Text);

        yield return TIn(TWheel(tEmpty, 3));
        Check("a claimed wheel: no dolly", TCam() == cam);
        var fake = new FakeKeys();
        var src = router.Source;
        router.Source = fake;   // the poll forgets a key the input says is up
        fake.Down.Add(KeyCode.T);
        var keyDown = TKey("T");
        keyDown["phase"] = "down";
        yield return TIn(keyDown);
        Check("a claimed key is the tool's", probe.Calls.Last() == "Key T None");
        int keys = probe.Count("Key");
        Check("a repeat of a claimed key: consumed, never to Key", router.Handle("T", KeyMods.None, true) && probe.Count("Key") == keys, probe.Text);
        var keyUp = TKey("T");
        keyUp["phase"] = "up";
        yield return TIn(keyUp);
        fake.Down.Clear();
        Check("after its key up a repeat is the map's again", !router.Handle("T", KeyMods.None, true) && probe.Count("Key") == keys, probe.Text);
        Check("a repeat of another key never reaches Key", !router.Handle("Y", KeyMods.None, true) && probe.Count("Key") == keys);
        probe.ClaimKey = "UpArrow";
        // a key the map runs as a held action: the press through the router, the hold through the poll, its key up never sent
        Check("a claimed held key: the press is the tool's", router.Handle("UpArrow", KeyMods.None, false));
        fake.Down.Add(KeyCode.UpArrow);
        yield return PFrames(3);
        Check("a claimed held key runs no held action", router.LastHeld.Count == 0 && TCam() == cam, router.LastHeld.Count + " held");
        fake.Down.Clear();
        fake.Down.Add(KeyCode.LeftArrow);
        yield return PFrames(3);
        Check("another held key runs", router.LastHeld.Count > 0 && TCam() != cam, router.LastHeld.Count + " held");
        fake.Down.Clear();
        yield return PFrames(2);
        router.Source = src;
        Tools.End(probe);
        Check("End", probe.Last == "Ended Finished" && TIdle(), probe.Last);
        yield return TLoadView();
    }

    // ------------------------------------------------------------ WI3

    static IEnumerator WI3()
    {
        var ui = TUi;
        var probe = new ProbeTool { Claim = true };
        Tools.Begin(probe);
        yield return TIn(TPt(tEmpty, "down"), TPt(tEmpty + new Vector2(5, 0), "move"));
        Check("a held press", Tools.PressHeld && TCapturing() == Vp.Element);
        yield return TIn(TKey("Escape"));
        Check("Esc on a press: PointerCancel, the tool stays, the pointer free", probe.Last == "Cancel" && Tools.Running && !Tools.PressHeld && TCapturing() == null, probe.Text);
        int calls = probe.Calls.Count;
        yield return TIn(TPt(tEmpty + new Vector2(9, 0), "move"), TPt(tEmpty + new Vector2(9, 0), "up"));
        Check("moves and the release after it reach nothing", probe.Calls.Count == calls, probe.Text);
        yield return TIn(TDrag(tEmpty));
        Check("the next press works", probe.Calls.Last() == "Up 0 0", probe.Last);
        yield return TIn(TKey("Escape"));
        Check("Esc with no press: Ended(Cancelled) is the last call", probe.Last == "Ended Cancelled" && TIdle(), probe.Last);
        Check("Esc never reached Key", probe.Count("Key") == 0, probe.Text);

        // a popup first
        probe = new ProbeTool();
        Tools.Begin(probe);
        ui.Menus.Show(new List<MenuItem> { new MenuItem { Id = "x", Text = "x" } }, null, tEmpty, "wi3-menu");
        yield return PFrames(1);
        yield return TIn(TKey("Escape"));
        Check("Esc: a popup closes first, the tool stays", !ui.Menus.IsOpen && Tools.Running, probe.Text);

        // a drag edit first
        var row = new VisualElement { name = "wi3-holder" };
        row.style.position = Position.Absolute;
        row.style.width = 40;
        row.style.height = 40;
        ui.Shell.Overlay.Add(row);
        var folder = ui.Ops.AddFolder(null, null, -1, UiContext.User());
        string fid = folder.NewIds.FirstOrDefault();
        var edit = ui.BeginEdit("wi3");
        ui.Session.Set(fid, "transform.pos", new HS2Next.Save.Vec3(1, 2, 3), UiContext.User());
        row.CapturePointer(PointerId.mousePointerId);
        yield return TIn(TKey("Escape"));
        Check("Esc: a drag edit is cancelled before the tool, which stays", !edit.IsOpen && Tools.Running, probe.Text);
        row.RemoveFromHierarchy();
        ui.Ops.Delete(new[] { fid }, UiContext.User());

        // a text field keeps its rules
        var field = new TextField { name = "wi3-field" };
        field.style.position = Position.Absolute;
        field.style.width = 100;
        ui.Shell.Overlay.Add(field);
        yield return PFrames(2);
        field.Focus();
        yield return PFrames(2);
        yield return TIn(TKey("T"));
        Check("a key in a text field never reaches the tool", probe.Count("Key") == 0, probe.Text);
        yield return TIn(TKey("Escape"));
        Check("Esc in a text field blurs it, the tool stays", Tools.Running, probe.Text);
        field.RemoveFromHierarchy();
        Vp.Element.Focus();
        yield return PFrames(1);

        // a dialog keeps its rules
        var spec = new DialogSpec { Id = "wi3-dialog", Title = "t", Message = "m" };
        spec.Buttons.Add(DialogButton.Ok());
        spec.Buttons.Add(DialogButton.Cancel());
        ui.Dialogs.Show(spec);
        yield return PFrames(3);
        yield return TIn(TKey("T"));
        Check("a key in a dialog never reaches the tool", probe.Count("Key") == 0, probe.Text);
        yield return TIn(TKey("Escape"));
        Check("Esc in a dialog closes it, the tool stays", !ui.Dialogs.IsModalOpen && Tools.Running, probe.Text);
        Vp.Element.Focus();
        yield return PFrames(1);
        yield return TIn(TKey("Escape"));
        Check("then Esc ends the tool", probe.Last == "Ended Cancelled" && TIdle(), probe.Last);
    }

    // ------------------------------------------------------------ WI4

    static IEnumerator WI4()
    {
        var ui = TUi;
        var probe = new ProbeTool { Claim = true };
        Tools.Begin(probe);
        yield return TIn(TPt(tEmpty, "down"));
        Tools.FocusChanged(false);
        Check("the window loses focus: PointerCancel, the tool stays, the pointer free", probe.Last == "Cancel" && Tools.Running && !Tools.PressHeld && TCapturing() == null, probe.Text);
        yield return TIn(TPt(tEmpty, "up"));
        Tools.FocusChanged(false);
        Check("focus lost with no press: no call", probe.Count("Cancel") == 1, probe.Text);
        yield return TIn(TPt(tEmpty, "down"));
        Check("a new press holds", Tools.PressHeld);
        Vp.Element.ReleasePointer(PointerId.mousePointerId);
        yield return TIn(TPt(tEmpty + new Vector2(4, 0), "move"));
        Check("the capture lost: PointerCancel, the tool stays", probe.Count("Cancel") == 2 && Tools.Running && !Tools.PressHeld, probe.Text);
        yield return TIn(TPt(tEmpty, "up"));
        yield return TIn(TDrag(tEmpty));
        Check("the next press works", probe.Calls.Last() == "Up 0 0", probe.Last);

        yield return TIn(TPt(tEmpty, "down"));
        int cancels = probe.Count("Cancel"), ups = probe.Count("Up");
        Tools.End(probe);
        Check("End during a press: Ended is the last call, no PointerCancel, no PointerUp, the pointer free",
            probe.Last == "Ended Finished" && probe.Count("Cancel") == cancels && probe.Count("Up") == ups && TIdle(), probe.Text);
        string cam = TCam();
        yield return TIn(TPt(tEmpty + new Vector2(40, 0), "move"), TPt(tEmpty + new Vector2(40, 0), "up"));
        Check("the rest of that press does nothing", TCam() == cam);

        // a tool that ends itself on the press, in both modes: nothing else comes of that press
        foreach (bool through in new[] { false, true })
        {
            var self = new ProbeTool { Claim = true, EndOnDown = true };
            var sel = ui.Selection.Ids.ToList();
            Check("Begin, ending on its press, PassThrough " + through, Tools.Begin(self, new ToolOptions { PassThrough = through }));
            yield return TIn(TPt(tOnB, "down"));
            Check("the press ended it", self.Last == "Ended Finished" && !Tools.Running, self.Text);
            yield return TIn(TPt(tOnB, "up"));
            yield return PFrames(1);
            Check("its release selects nothing, the pointer is free", ui.Selection.Ids.SequenceEqual(sel) && TCapturing() == null, string.Join(",", ui.Selection.Ids));
        }

        var bad = new ProbeTool { Claim = true, ThrowIn = "Move" };
        Tools.Begin(bad);
        yield return TIn(TPt(tEmpty, "down"), TPt(tEmpty + new Vector2(4, 0), "move"));
        Check("a tool that throws ends with Cancelled, the pointer free", bad.Last == "Ended Cancelled" && TIdle(), bad.Text);
        yield return TIn(TPt(tEmpty, "up"));
        var bad2 = new ProbeTool { ThrowIn = "Started" };
        Check("a tool that throws in Started: Begin says true, it has ended", Tools.Begin(bad2) && bad2.Last == "Ended Cancelled" && TIdle(), bad2.Text);
    }

    // ------------------------------------------------------------ WI5

    static IEnumerator WI5()
    {
        var ui = TUi;
        var order = new List<string>();
        var a = new ProbeTool { Name = "a", Shared = order, Claim = true };
        var b = new ProbeTool { Name = "b", Shared = order };
        Tools.Begin(a, new ToolOptions { Hint = "panel.wet" });
        ui.Shell.StatusBar.Refresh();
        Check("the hint shows in the status line", ui.Shell.StatusBar.Hint.text == ui.L10n.Get("panel.wet") && ui.Shell.StatusBar.Hint.text.Length > 0 && ui.Shell.StatusBar.Hint.style.display == DisplayStyle.Flex, ui.Shell.StatusBar.Hint.text);
        yield return TIn(TPt(tEmpty, "down"));
        Tools.Begin(b);
        Check("another Begin: Ended(Replaced), then Started, no call for the press", string.Join(", ", order.Skip(1)) == "a Down 0 1 None, a Ended Replaced, b Started", string.Join(", ", order));
        Check("the press ended with it", !Tools.PressHeld && TCapturing() == null);
        yield return TIn(TPt(tEmpty, "up"));
        Check("the hint is gone with its tool", ui.Shell.StatusBar.Hint.text.Length == 0, ui.Shell.StatusBar.Hint.text);

        // the owning node
        var owned = new ProbeTool();
        Tools.Begin(owned, new ToolOptions { Filter = new ToolPickFilter { NodeId = tB } });
        Check("Begin replaced the last one", b.Last == "Ended Replaced", b.Last);
        var other = ui.Ops.AddFolder(null, null, -1, UiContext.User());
        yield return PFrames(1);
        Check("another node's change: it runs on", Tools.Running);
        ui.Ops.Delete(other.NewIds, UiContext.User());
        yield return PFrames(1);
        Check("another node deleted: it runs on", Tools.Running);
        ui.Ops.Delete(new[] { tB }, UiContext.User());
        yield return PFrames(1);
        Check("the owning node deleted: Ended(SceneChanged)", owned.Last == "Ended SceneChanged" && TIdle(), owned.Text);
        yield return PCall(TApp, new { cmd = "undo" });
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => TApp.Runtime.FindChara(tB)?.State == RealizationState.Built, 60, ok);
        Check("undo brings the node back", ok[0]);

        // the dialog
        var stays = new ProbeTool();
        Tools.Begin(stays);
        var spec = new DialogSpec { Id = "wi5-dialog", Title = "t", Message = "m" };
        spec.Buttons.Add(DialogButton.Ok());
        ui.Dialogs.Show(spec);
        yield return PFrames(2);
        var refused = new ProbeTool();
        Check("a dialog: Begin refused, the running tool stays, no call on the new one", !Tools.Begin(refused) && Tools.Current == stays && refused.Calls.Count == 0 && Tools.Refusal == ToolHost.RefusedDialog, Tools.Refusal);
        ui.Dialogs.Cancel();
        yield return PFrames(2);
        Vp.Element.Focus();

        // the Edit tab
        ui.Ops.Select(new[] { tA });
        yield return PFrames(2);
        var ran = ui.Registry.RunWith(ActionIds.EditShow, new ActionInvocation { Source = "test", Args = new Dictionary<string, object> { ["node"] = "body" } }, out _);
        yield return PFrames(3);
        Check("the Edit tab in view ends a tool: Ended(SceneChanged)", CharaEditTab.Current != null && stays.Last == "Ended SceneChanged" && TIdle(), ran + "; " + stays.Text);
        var refused2 = new ProbeTool();
        Check("the Edit tab: Begin refused", !Tools.Begin(refused2) && refused2.Calls.Count == 0 && Tools.Refusal == ToolHost.RefusedEdit, Tools.Refusal);
        var ip = ui.Shell.PanelRoot("inspector") as InspectorPanel ?? ui.Shell.PanelRoot("inspector")?.Q<InspectorPanel>();
        ip?.Chara?.ShowTab("anime");
        yield return PFrames(3);
        var again = new ProbeTool();
        Check("another tab: Begin goes through", CharaEditTab.Current == null && Tools.Begin(again) && Tools.Refusal == null, Tools.Refusal);

        // the UI closing
        Tools.Close();
        Check("the UI closes: Ended(UiClosed)", again.Last == "Ended UiClosed" && TIdle(), again.Last);

        // Begin, null and from an Ended
        bool threw = false;
        try { Tools.Begin(null); }
        catch (ArgumentNullException) { threw = true; }
        Check("Begin(null) throws", threw);
        var chain = new ProbeTool();
        Tools.End(chain);
        Check("End of a tool that does not run: nothing", chain.Calls.Count == 0);

        // a new scene and a load
        yield return PCall(TApp, new { cmd = "scene.save", name = "wi5", overwrite = true, thumb = "placeholder" });
        Check("scene.save", POk, Short(pLastText));
        string path = (string)pLast["path"];
        var loadTool = new ProbeTool();
        Tools.Begin(loadTool);
        yield return PCall(TApp, new { cmd = "scene.load", path });
        Check("scene.load", POk, Short(pLastText));
        yield return PFrames(2);
        Check("a load: Ended(SceneChanged)", loadTool.Last == "Ended SceneChanged" && TIdle(), loadTool.Text);
        var newTool = new ProbeTool();
        Tools.Begin(newTool);
        yield return PCall(TApp, new { cmd = "scene.new" });
        yield return PFrames(2);
        Check("a new scene: Ended(SceneChanged)", newTool.Last == "Ended SceneChanged" && TIdle(), newTool.Text);
    }

    // ------------------------------------------------------------ WI6

    static IEnumerator WI6()
    {
        var ui = TUi;
        var real = TApp.Runtime.FindChara(tA);
        var cc = real.Context;
        // a known triangle: the body's, skinned, facing the camera; the ray goes through a point of it by known weights
        var targets = WetTargets.Collect(real);
        var shown = targets.Where(t => t.Shown && t.Renderer is SkinnedMeshRenderer && t.State == WetTargetStates.Ok).ToList();
        if (!Check("the character has skinned targets", shown.Count > 0, shown.Count.ToString())) yield break;
        int tested = 0, exact = 0, partOk = 0, boneOk = 0, uvOk = 0, normalOk = 0;
        double worstPoint = 0, worstUv = 0;
        var seen = new HashSet<string>();
        var picked = new List<ToolPick>();
        var rayMs = new List<double>();
        foreach (var t in shown)
        {
            if (!seen.Add(t.Address)) continue;
            var smr = (SkinnedMeshRenderer)t.Renderer;
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            var v = baked.vertices;
            var uv = baked.uv;
            var tris = baked.triangles;
            for (int k = 0; k < 6 && tris.Length >= 3; k++)
            {
                int i = (tris.Length / 3 * (k + 1) / 7) * 3;
                Vector3 a = m.MultiplyPoint3x4(v[tris[i]]), b = m.MultiplyPoint3x4(v[tris[i + 1]]), c = m.MultiplyPoint3x4(v[tris[i + 2]]);
                var n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-10f) continue;
                n.Normalize();
                const float wa = .2f, wb = .3f, wc = .5f;
                var p = a * wa + b * wb + c * wc;
                var ray = new Ray(p + n * 30f, -n);
                var pick = ui.Tools.Pick(ray, new ToolPickFilter { NodeId = tA });
                rayMs.Add(Vp.Picker.LastSurfaceMs);
                if (!pick.Hit) continue;
                tested++;
                // something else may lie in front along this ray: only a hit on this renderer and triangle says anything
                if (pick.Renderer != smr || pick.Triangle != i / 3) continue;
                exact++;
                worstPoint = Math.Max(worstPoint, (pick.Point - p).magnitude);
                var want = uv[tris[i]] * wa + uv[tris[i + 1]] * wb + uv[tris[i + 2]] * wc;
                worstUv = Math.Max(worstUv, (pick.Uv - want).magnitude);
                if (pick.Precise) uvOk++;
                if (Mathf.Abs(Vector3.Dot(pick.Normal, n) - 1f) < 1e-3f) normalOk++;
                if (pick.Part == t.Address) partOk++;
                if (pick.Bone && pick.BoneAddress != null && AdvancedPoseResolver.Resolve(real, pick.BoneAddress) == pick.Bone) boneOk++;
                picked.Add(pick);
            }
            UnityEngine.Object.DestroyImmediate(baked);
        }
        double lastMs = Vp.Picker.LastSurfaceMs;
        rayMs.Sort();
        Measure($"WI6 picks on known triangles: {tested} rays, {exact} on the triangle, worst point {worstPoint:F5}, worst uv {worstUv:F6}, last {lastMs:F2} ms, over the rays median {rayMs[rayMs.Count / 2]:F2} ms, p95 {rayMs[rayMs.Count * 95 / 100]:F2} ms, max {rayMs[rayMs.Count - 1]:F2} ms");
        Check("known triangles were hit", exact >= 6, exact + " of " + tested);
        Check("the point within 0.01", worstPoint <= 0.01, worstPoint.ToString("F5"));
        Check("the UV by the known weights", worstUv <= 1e-4 && uvOk == exact, worstUv.ToString("F6") + ", " + uvOk + " of " + exact);
        Check("the normal is the triangle's, towards the ray", normalOk == exact, normalOk + " of " + exact);
        Check("the part's address is the target's", partOk == exact, partOk + " of " + exact);
        Check("the bone resolves from its address", boneOk == exact, boneOk + " of " + exact);
        var last = picked[picked.Count - 1];
        Check("the local point goes back to the point", (last.Bone.TransformPoint(last.BoneLocal) - last.Point).magnitude < 1e-3f);
        Check("a character's pick names its node and no map", last.NodeId == tA && !last.OnMap);

        // against a ray test of its own: every shown target of the character, posed triangles; a click tests 8 boxes and answers the box beyond them, a press must land on a triangle
        var cray = Vp.RayAt(tOnA);
        var click = Vp.Picker.PickRay(cray);
        var surf = ui.Tools.Pick(cray);
        Check("the ray test finds the same surface, within 0.01", WetTargets.Raycast(targets, cray, 1000f, out var own) && surf.Hit && surf.Precise && surf.NodeId == tA && surf.Renderer == own.Renderer && (surf.Point - own.Point).magnitude <= 0.01f, surf.Point + " / " + own.Point);
        Check("the click's own ray test agrees where it found a triangle", !click.Precise || (surf.NodeId == click.NodeId && (surf.Point - click.Point).magnitude <= 0.01f), click.Precise + " " + surf.Point + " / " + click.Point);
        var times = new List<double>();
        for (int i = 0; i < 21; i++)
        {
            ui.Tools.Pick(cray, new ToolPickFilter { NodeId = tA });
            times.Add(Vp.Picker.LastSurfaceMs);
        }
        times.Sort();
        Measure($"WI6 PickSurface on the character's hips, 21 picks: median {times[10]:F2} ms, max {times[20]:F2} ms (Editor; candidates in the box order)");
        var r0 = surf.Renderer;
        Check("the slot is a slot of the renderer", surf.Slot >= 0 && surf.Slot < r0.sharedMaterials.Length, surf.Slot.ToString());

        // filters
        var missRay = Vp.RayAt(tEmpty);
        var miss = ui.Tools.Pick(missRay);
        Check("a miss: no hit, every member at its default", !miss.Hit && miss.NodeId == null && miss.Renderer == null && miss.Point == default && miss.Normal == default && miss.Bone == null);
        var onB = Vp.RayAt(tOnB);
        Check("a NodeId filter leaves other nodes out", !ui.Tools.Pick(onB, new ToolPickFilter { NodeId = tA }).Hit && ui.Tools.Pick(onB, new ToolPickFilter { NodeId = tB }).NodeId == tB);
        var folder = ui.Ops.AddItem(HS2Next.Content.ContentId.Vanilla(821), null, null, -1, UiContext.User());
        string item = folder.Ok ? folder.NewIds.FirstOrDefault() : null;
        if (item != null)
        {
            ui.Ops.SetTransform(new[] { item }, new Vector3(-40, 0, 0), null, null, UiContext.User());
            yield return PFrames(3);
            var ih = TApp.Runtime.HolderOf(item);
            var ir = ih ? ih.GetComponentInChildren<Renderer>() : null;
            if (ir)
            {
                var iray = new Ray(ir.bounds.center + Vector3.up * 200, Vector3.down);
                var ip = ui.Tools.Pick(iray);
                var ic = ui.Tools.Pick(iray, new ToolPickFilter { CharactersOnly = true });
                Check("CharactersOnly leaves an item out", ip.NodeId == item && ic.NodeId != item, ip.NodeId + " / " + ic.NodeId);
                Check("an item has no part and no bone address", ip.Part == null && ip.BoneAddress == null, ip.Part + " / " + ip.BoneAddress);
            }
            else Warn("the item has no renderer");
            ui.Ops.Delete(new[] { item }, UiContext.User());
            yield return PFrames(2);
        }
        else Warn("no item to test CharactersOnly");

        // a hidden renderer
        var hit = ui.Tools.Pick(cray, new ToolPickFilter { NodeId = tA });
        var hr = hit.Renderer;
        bool on = hr.enabled;
        hr.enabled = false;
        var under = ui.Tools.Pick(cray, new ToolPickFilter { NodeId = tA });
        hr.enabled = on;
        Check("a hidden renderer is not hit", under.Renderer != hr, under.Renderer ? under.Renderer.name : "none");
        hr.forceRenderingOff = true;
        var under2 = ui.Tools.Pick(cray, new ToolPickFilter { NodeId = tA });
        hr.forceRenderingOff = false;
        Check("a renderer forced off is not hit", under2.Renderer != hr);

        // a mesh that cannot be read: the box answers, not precise
        var go = new GameObject("wi6 unreadable");
        go.transform.SetParent(TApp.Runtime.HolderOf(tA), false);
        var mesh = UnityEngine.Object.Instantiate(GizmoMeshes.Cube);
        mesh.UploadMeshData(true);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        go.transform.position = new Vector3(-30, 8, 0);
        go.transform.localScale = Vector3.one * 2;
        yield return PFrames(1);
        var bray = new Ray(new Vector3(-30, 8, 40), Vector3.back);
        var box = ui.Tools.Pick(bray, new ToolPickFilter { NodeId = tA });
        Check("a mesh that cannot be read: the box's point, not precise, no triangle's members", box.Hit && !box.Precise && box.Renderer == mr && box.Normal == default && box.Bone == null && Mathf.Abs(box.Point.z - 1f) < 0.01f, box.Hit + " " + box.Point);
        UnityEngine.Object.DestroyImmediate(go);
        UnityEngine.Object.DestroyImmediate(mesh);

        // through a tool: Hit is lazy, and uses the tool's filter
        var probe = new ProbeTool { Claim = true };
        Tools.Begin(probe, new ToolOptions { Filter = new ToolPickFilter { NodeId = tB } });
        yield return TIn(TPt(tOnA, "down"), TPt(tOnA, "up"));
        Check("a pointer's Hit follows the tool's filter", probe.Hit != null && !probe.Hit.Hit);
        yield return TIn(TPt(tOnB, "down"), TPt(tOnB, "up"));
        Check("a pointer's Hit on the allowed node", probe.Hit != null && probe.Hit.NodeId == tB);
        Tools.End(probe);

        // the map's surface: only when nothing else is hit, and only when asked for
        var mapTool = new ProbeTool();
        Tools.Begin(mapTool);
        yield return PCall(TApp, new { cmd = "map.load", @ref = MqFloorMap, wait = true }, 240);
        Check("map.load", POk, Short(pLastText));
        Check("a map's load ends no tool", Tools.Running && mapTool.Count("Ended") == 0, mapTool.Text);
        Tools.End(mapTool);
        yield return PFrames(3);
        var mq = MapSurface.Of(TApp.Runtime);
        var above = TApp.Runtime.HolderOf(tA).position + new Vector3(-25, 8, 0);
        var down = new Ray(above, Vector3.down);
        Check("the map's height under the point", mq.Surface(above, out var floor, 0, 60), above.ToString());
        var noMap = ui.Tools.Pick(down);
        var onMap = ui.Tools.Pick(down, new ToolPickFilter { MapSurface = true });
        Check("no map surface unless the filter asks", !noMap.Hit);
        Check("the map's surface: OnMap, the point of the height query, its normal, not precise, no node", onMap.Hit && onMap.OnMap && !onMap.Precise && onMap.NodeId == null && onMap.Renderer == null &&
            Mathf.Abs(onMap.Point.y - floor.Height) <= 0.01f && Mathf.Abs(onMap.Distance - (above.y - onMap.Point.y)) <= 0.01f && Vector3.Dot(onMap.Normal, Vector3.up) > 0.3f, onMap.Hit + " " + onMap.Point + " / " + floor.Height);
        var overA = ui.Tools.Pick(new Ray(TApp.Runtime.FindChara(tA).Context.BodyBones["cf_J_Hips"].position + Vector3.forward * 30, Vector3.back), new ToolPickFilter { MapSurface = true });
        Check("a node before the map: the node", overA.Hit && !overA.OnMap && overA.NodeId == tA, overA.NodeId);
    }

    // ------------------------------------------------------------ WI7

    static IEnumerator WI7()
    {
        var ui = TUi;
        var hips = TApp.Runtime.FindChara(tA).Context.BodyBones["cf_J_Hips"].position;
        var cam = Vp.ViewCamera;
        ui.Ops.Select(Array.Empty<string>());
        yield return PFrames(3);
        var imgs = new List<Color32[]>();
        TRender(true, imgs);
        var noTool = ViewCapture.Render(cam, 512, 288, ViewCapture.None);
        var noTool2 = ViewCapture.Render(cam, 512, 288, ViewCapture.None);
        Check("two captures with no tool agree (the base of the comparison)", noTool != null && noTool2 != null && noTool.SequenceEqual(noTool2), noTool?.Length + " / " + noTool2?.Length);
        var probe = new ProbeTool { Draws = true, DrawAt = hips };
        Tools.Begin(probe);
        int recorded = THost.GizmoPass.Recorded;
        TRender(true, imgs);
        Check("the view: Draw ran, its lines are in the pass", probe.DrawCalls >= 1 && Tools.LastDrawn == 4 && THost.GizmoPass.Recorded > recorded, probe.DrawCalls + " calls, " + Tools.LastDrawn + " items");
        int diff = 0;
        for (int i = 0; i < imgs[0].Length; i++)
        {
            var a = imgs[0][i]; var b = imgs[1][i];
            if (Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b) > 30) diff++;
        }
        Measure("WI7 view pixels that the tool's lines change: " + diff);
        Check("the view shows what the tool draws", diff > 20, diff.ToString());
        var label = Vp.Element.Q<Label>("tool-label-0");
        Check("a label shows over the view", label != null && label.text == "probe" && label.style.display == DisplayStyle.Flex, label?.text);
        int calls = probe.DrawCalls;
        var withTool = ViewCapture.Render(cam, 512, 288, ViewCapture.None);
        Check("a capture calls no Draw", probe.DrawCalls == calls, probe.DrawCalls + " / " + calls);
        Check("a capture holds nothing of what the tool draws", withTool != null && withTool.SequenceEqual(noTool), withTool?.Length + " / " + noTool.Length);
        var res = HS2Next.Screenshots.CaptureService.RenderSync(TApp.Runtime, cam, new HS2Next.Screenshots.CaptureRequest { Width = 320, Height = 180, Aa = HS2Next.Screenshots.CaptureService.None });
        Check("the capture path calls no Draw", res != null && res.Ok && probe.DrawCalls == calls, res?.Error);
        Tools.End(probe);
        var after = ViewCapture.Render(cam, 512, 288, ViewCapture.None);
        Check("after the end: the capture as before", after != null && after.SequenceEqual(noTool));
        Check("the label is hidden with its tool", label == null || label.style.display == DisplayStyle.None);

        // what shows with no tool: the view's own drawing
        int viewDraws = 0;
        Action<IToolDraw> vd = d => { viewDraws++; d.Marker(hips, 1, Color.red); };
        Tools.ViewDraw += vd;
        TRender();
        int v0 = viewDraws;
        var cap = ViewCapture.Render(cam, 512, 288, ViewCapture.None);
        Tools.ViewDraw -= vd;
        Check("ViewDraw: in the view, not in a capture", v0 >= 1 && viewDraws == v0 && Tools.LastDrawn == 3 && cap != null && cap.SequenceEqual(noTool), v0 + " / " + viewDraws);
        ui.Ops.Select(new[] { tA });
        yield return PFrames(1);
    }
}
