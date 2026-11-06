// UiEscTest.play: Esc during a pointer drag of any handle or row. The app with the UI in play mode,
// real pointer events through the panel, judged by the history, the model, the bones and the widgets' state; no pixel is read.
//   FE1  a FloatField's label drag with real deltas, Esc, moves, the release, a blur: no typed entry, the values as at the press
//   FE2  each handle kind: press, 3 moves, Esc, 3 moves, release: values as at the press, no entry, the camera still, a new drag works
//   FE3  each row kind (UiEscTest_Rows.cs)
//   FE4  the sites with their own Esc (UiEscTest_Rows.cs)
//   FE5  Esc with a text field focused and no drag, and in a wheel run (UiEscTest_Rows.cs)
// Moves carry the delta a hand's move has where a widget reads it (a FloatField's label): ui.input's moves have none, so those events
// are raised here through the panel in the same way, with a delta. Anything else goes through ui.input.
// Run: & Tools\unity_batch.ps1 -Method UiEscTest.Run -NoQuit [-MethodArgs @('-only','FE1+FE2')]
// Report: Spike/out/ui/ui_esc_report.txt ("SUMMARY: N PASS, M FAIL").
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Binding;
using HS2Next.UI.Bridge;
using HS2Next.UI.Core;
using HS2Next.UI.Gizmos;
using HS2Next.UI.Panels;
using HS2Next.UI.Widgets;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class UiEscTest
{
    public const int Port = 17398;
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "ui");
    static string ReportPath => Path.Combine(OutDir, "ui_esc_report.txt");
    static string Launch =>
        "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},\"userData\":\"Spike/out/ui/esc_userdata\",\"bridge\":{\"port\":" + Port +
        ",\"outputDir\":\"Spike/out/ui/esc_out\",\"allowQuit\":false}}";

    public static void Run()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(ReportPath, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("UiEscTest.Play", ReportPath, 15, true, Launch);
    }

    static StringBuilder rep;
    static int passes, fails, warns;
    static string current = "";
    static readonly Dictionary<string, (int pass, int fail)> perGroup = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
    static string charaA, charaB;

    static AppCore App => AppCore.Current;
    static UiHost Host => UiHost.Current;
    static UiContext Ui => Host?.Context;
    static IPanel Panel => Host?.Root?.panel;
    static TransformTool Gizmo => Ui.Viewport.Gizmo;
    static int Undos => Ui.Session.Commands.UndoList.Count;
    static string TopLabel => Ui.Session.Commands.UndoList.Count > 0 ? Ui.Session.Commands.UndoList[0].Label : null;

    static void Info(string s) => rep.AppendLine("   " + s);
    static void Warn(string s) { warns++; rep.AppendLine($"WARN  [{current}] {s}"); }

    static bool Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  [{current}] {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
        perGroup.TryGetValue(current, out var v);
        perGroup[current] = ok ? (v.pass + 1, v.fail) : (v.pass, v.fail + 1);
        return ok;
    }

    static IEnumerator Play()
    {
        rep = new StringBuilder();
        passes = fails = warns = 0;
        perGroup.Clear();
        var args = TestArgs.Get();
        int oi = Array.IndexOf(args, "-only");
        HashSet<string> only = oi >= 0 && oi + 1 < args.Length ? new HashSet<string>(args[oi + 1].Split('+', ',')) : null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        rep.AppendLine($"UiEscTest.play (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, screen {Screen.width}x{Screen.height})");
        var groups = new List<(string id, string title, Func<IEnumerator> run)>
        {
            ("setup", "setup: app, UI, scene", Prepare),
            ("FE1", "a label drag, Esc, moves, the release, a blur", FE1),
            ("FE2", "each handle kind", FE2),
        };
        AddGroups(groups);
        bool ready = true;
        foreach (var (id, title, run) in groups)
        {
            if (id != "setup" && (!ready || (only != null && !only.Contains(id)))) continue;
            current = id;
            rep.AppendLine();
            rep.AppendLine($"== {id} {title}");
            int before = fails;
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
                catch (Exception ex) { Check("exception", false, ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace); break; }
                if (cur is IEnumerator nested) { stack.Push(nested); continue; }
                yield return cur;
            }
            if (Ui != null && Ui.ActiveEdit != null && Ui.ActiveEdit.IsOpen) { Ui.ActiveEdit.Cancel(); Check("no edit left open after the group", false); }
            if (id == "setup" && fails > before) ready = false;
        }
        var sum = new StringBuilder();
        sum.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL, {warns} WARN ({sw.Elapsed.TotalSeconds:F0} s)");
        foreach (var kv in perGroup.OrderBy(k => k.Key, StringComparer.Ordinal)) sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail");
        rep.Insert(0, sum.ToString());
        File.WriteAllText(ReportPath, rep.ToString(), new UTF8Encoding(false));
        Debug.Log($"UI ESC TEST DONE: {passes} PASS, {fails} FAIL");
        PlayModeHarness.ExitCode = fails > 0 ? 1 : 0;
    }

    static partial void AddMoreGroups(List<(string id, string title, Func<IEnumerator> run)> groups);
    static void AddGroups(List<(string id, string title, Func<IEnumerator> run)> groups) => AddMoreGroups(groups);

    // ---------------------------------------------------------------- the bridge and the events

    static JObject last;
    static string lastText;
    static bool LastOk => last?.Value<bool?>("ok") == true;
    static string Short(string s) => s == null ? "null" : s.Length > 300 ? s.Substring(0, 300) + "..." : s;

    static IEnumerator Call(object request, double timeoutSec = 60)
    {
        string json = request is string s ? s : JsonConvert.SerializeObject(request);
        last = null;
        lastText = null;
        var t = App.Bridge.Execute(json);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!t.IsCompleted && sw.Elapsed.TotalSeconds < timeoutSec) yield return PlayModeHarness.WaitFrames(1);
        lastText = t.IsCompleted && !t.IsFaulted ? t.Result : "{\"ok\":false,\"code\":\"timeout\"}";
        try { last = JObject.Parse(lastText); }
        catch (Exception) { last = new JObject { ["ok"] = false }; }
    }

    static IEnumerator OkCall(object request, string what)
    {
        yield return Call(request);
        Check(what, LastOk, LastOk ? "" : Short(lastText));
    }

    static IEnumerator Frames(int n) => PlayModeHarness.WaitFrames(n);

    static JObject Pt(Vector2 p, string phase, int button = 0) => new JObject { ["type"] = "pointer", ["x"] = p.x, ["y"] = p.y, ["phase"] = phase, ["button"] = button };

    /// <summary>ui.input with these events, one a frame.</summary>
    static IEnumerator Input(params JObject[] events)
    {
        yield return Call(new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(events.Cast<object>().ToArray()), ["frames"] = 1 });
        if (!LastOk) Check("ui.input", false, Short(lastText));
        yield return Frames(1);
    }

    static IEnumerator Key(string key)
    {
        yield return Call(new JObject { ["cmd"] = "ui.input", ["events"] = new JArray(new JObject { ["type"] = "key", ["key"] = key }), ["frames"] = 1 });
        if (!LastOk) Check("ui.input key", false, Short(lastText));
        yield return Frames(1);
    }

    /// <summary>A pointer event through the panel as ui.input raises it, with the delta a hand's move has.</summary>
    static void Raw(EventType et, Vector2 p, Vector2 delta = default)
    {
        var se = new Event { type = et, mousePosition = p, delta = delta, button = 0, clickCount = 1, modifiers = EventModifiers.None };
        var tree = Panel.visualTree;
        if (et == EventType.MouseDown) using (var e = PointerDownEvent.GetPooled(se)) tree.SendEvent(e);
        else if (et == EventType.MouseUp) using (var e = PointerUpEvent.GetPooled(se)) tree.SendEvent(e);
        else using (var e = PointerMoveEvent.GetPooled(se)) tree.SendEvent(e);
    }

    /// <summary>A real Esc through the panel: to the focused element, as ui.input sends it.</summary>
    static void EscNow() => UiCommands.Send(Host, JObject.FromObject(new { type = "key", key = "Escape" }), out _);

    static VisualElement Capturing()
    {
        for (int id = 0; id < PointerId.maxPointers; id++) if (Panel.GetCapturingElement(id) is VisualElement v) return v;
        return null;
    }

    static string Name(VisualElement v) => v == null ? "none" : (v.name ?? "") + " (" + v.GetType().Name + ")";
    static VisualElement FocusedNow => Panel.focusController?.focusedElement as VisualElement;
    static string Log(int n = 3) => string.Join(" | ", Host.Router.Log.Skip(Math.Max(0, Host.Router.Log.Count - n)));

    static float[] Vals(params Vec3[] v) => v.SelectMany(x => new[] { x.X, x.Y, x.Z }).ToArray();
    static float[] Vals(params Vector3[] v) => v.SelectMany(x => new[] { x.x, x.y, x.z }).ToArray();
    static bool Same(float[] a, float[] b, float eps) => a.Length == b.Length && a.Zip(b, (x, y) => Mathf.Abs(x - y) <= eps).All(x => x);
    static string Show(float[] a) => "(" + string.Join(", ", a.Select(x => x.ToString("G7", System.Globalization.CultureInfo.InvariantCulture))) + ")";

    // ---------------------------------------------------------------- setup

    static IEnumerator Prepare()
    {
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ok);
        Check("app ready", ok[0] && App?.Stage == AppStage.Ready, App?.Stage.ToString());
        yield return PlayModeHarness.WaitUntil(() => UiHost.Current != null && UiHost.Current.IsReady, 60, ok);
        if (!Check("UI ready", ok[0])) yield break;
        var args = TestArgs.Get();
        int ti = Array.IndexOf(args, "-targets");
        var targets = TestTargets.Load(ti >= 0 && ti + 1 < args.Length ? args[ti + 1] : null);
        yield return OkCall(new { cmd = "scene.new" }, "scene.new");
        yield return Call(new { cmd = "chara.load", path = RelToCards(targets.visual.female), sex = "female" });
        Check("chara.load BR-Chan", LastOk, Short(lastText));
        charaA = (string)last?["key"] ?? (string)last?["added"]?[0]?["key"];
        yield return Call(new { cmd = "chara.load", path = RelToCards(targets.visual.male), sex = "male" });
        Check("chara.load HERO107", LastOk, Short(lastText));
        charaB = (string)last?["key"] ?? (string)last?["added"]?[0]?["key"];
        yield return PlayModeHarness.WaitUntil(() => Real(charaA)?.State == RealizationState.Built && Real(charaB)?.State == RealizationState.Built, 180, ok);
        Check("both built", ok[0]);
        Ui.Shell.ResetLayout();
        yield return Frames(3);
        Info("screen " + Screen.width + "x" + Screen.height + ", viewport " + Ui.Viewport.Element.worldBound);
    }

    static string RelToCards(string rel)
    {
        if (string.IsNullOrEmpty(rel)) return rel;
        foreach (var pre in new[] { "chara/female/", "chara/male/" })
            if (rel.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) return rel.Substring(pre.Length);
        return rel;
    }

    static CharaRealization Real(string id) => id == null ? null : App.Runtime.FindChara(id);
    static CharaNodeState StateOf(string id) => Ui.Model.Find(id)?.State as CharaNodeState;

    // ---------------------------------------------------------------- FE1

    static FieldRow PlaceRow(FieldSpec spec, TargetSelector sel)
    {
        var row = FieldWidgets.Create(Ui, spec, new FieldContext { Selector = sel });
        row.style.position = Position.Absolute;
        row.style.left = 300;
        row.style.top = 100;
        row.style.width = 460;
        Ui.Shell.Overlay.Add(row);
        return row;
    }

    static IEnumerator FE1()
    {
        var added = Ui.Ops.AddFolder(null, null, -1, UiContext.User());
        if (!Check("a folder to edit", added.Ok && added.NewIds.Count == 1, added.ToString())) yield break;
        string id = added.NewIds[0];
        Ui.Ops.Select(new[] { id });
        var spec = InspectorSchemas.Transform.Fields.First(f => f.Path == "transform.pos");
        var row = PlaceRow(spec, TargetSelector.Selection());
        // a value the field's format rounds: what a blur would give back as text differs from the value
        Ui.Session.Set(id, "transform.pos", new Vec3(1f, 2.1234567f, 3f), UiContext.User());
        yield return Frames(3);
        var vf = row.Control as VectorField;
        if (!Check("transform.pos is a vector row", vf != null)) yield break;
        var f = vf.Fields[1];
        var label = f.labelElement;
        Vec3 Pos() => (Vec3)FieldAccessor.Get(Ui.Model, id, "transform.pos");
        Vec3 p0 = Pos();
        Vector2 at = label.worldBound.center, away = new Vector2(at.x + 200, at.y + 300);
        Vector2 dx = new Vector2(3, 0);
        int u0 = Undos;
        Check("the label is where the pointer goes", Panel.Pick(at) == label, Name(Panel.Pick(at) as VisualElement) + " at " + at);

        // ---------------- A: the pointer stays over the label, the release lands on it
        Raw(EventType.MouseDown, at);
        Check("the press: the label holds the pointer, the focus in the field", Capturing() == label && FocusedNow == f && vf.Dragging, Name(Capturing()) + ", focus " + Name(FocusedNow));
        Check("the press: the row's edit is open", Ui.ActiveEdit != null && Ui.ActiveEdit.IsOpen);
        Raw(EventType.MouseDrag, at + dx, new Vector2(6, 0));
        Raw(EventType.MouseDrag, at + dx, new Vector2(6, 0));
        Vec3 moved = Pos();
        Check("moves with a delta drag the value (the row's previews)", Mathf.Abs(moved.Y - p0.Y) > 1e-3f && moved.X == p0.X && moved.Z == p0.Z, p0.Y + " -> " + moved.Y);
        EscNow();
        Check("Esc: no element holds the pointer", Capturing() == null, Name(Capturing()));
        Check("Esc: the drag has ended, the row's edit is closed", !vf.Dragging && Ui.ActiveEdit == null);
        Check("Esc: the value as at the press, no entry", Pos().Equals(p0) && Undos == u0, Pos().Y + ", entries " + (Undos - u0));
        Check("Esc: the field has given up the focus", FocusedNow != f, Name(FocusedNow));
        Check("Esc: the router cancelled and blurred", Log(2).Contains("Esc: edit cancelled") && Log(2).Contains("Esc: text field blurred"), Log(2));
        yield return Frames(2);
        Check("Esc: the display shows the model", Mathf.Abs(f.value - p0.Y) <= 1e-5f && !f.showMixedValue, f.value + " / " + p0.Y);
        string text0 = f.text;
        Raw(EventType.MouseDrag, at, new Vector2(3, 0));
        Raw(EventType.MouseDrag, at + new Vector2(1, 0), new Vector2(3, 0));
        Raw(EventType.MouseDrag, at, new Vector2(3, 0));
        Check("moves over the label after Esc: value, text and history as they were", Pos().Equals(p0) && f.text == text0 && Mathf.Abs(f.value - p0.Y) <= 1e-5f && Undos == u0, f.text + " / " + text0);
        Raw(EventType.MouseUp, at);
        Check("the release over the label: nothing", Pos().Equals(p0) && f.text == text0 && Undos == u0, f.text);
        Check("the release: the field takes typed text again", f.isDelayed);
        yield return Frames(2);
        f.Focus();
        yield return Frames(1);
        f.Blur();
        yield return Frames(2);
        Check("a later blur: no typed entry, the value as it was", Pos().Equals(p0) && Undos == u0, Pos().Y + ", entries " + (Undos - u0));

        // ---------------- B: the release lands elsewhere, the pointer comes back over the label
        Raw(EventType.MouseDown, at);
        Raw(EventType.MouseDrag, at + dx, new Vector2(6, 0));
        EscNow();
        Raw(EventType.MouseDrag, away, new Vector2(6, 0));
        Raw(EventType.MouseUp, away);
        yield return Frames(2);
        text0 = f.text;
        Raw(EventType.MouseDrag, at, new Vector2(6, 0));
        Raw(EventType.MouseDrag, at + new Vector2(1, 0), new Vector2(6, 0));
        Check("released elsewhere: a hover over the label later changes no text", f.text == text0 && Pos().Equals(p0) && Undos == u0, f.text + " / " + text0);
        f.Focus();
        yield return Frames(1);
        f.Blur();
        yield return Frames(2);
        Check("released elsewhere: a later blur types nothing", Pos().Equals(p0) && Undos == u0, Pos().Y + ", entries " + (Undos - u0));

        // ---------------- C: Esc reaching the router's key path with the focus left in the field
        Raw(EventType.MouseDown, at);
        Raw(EventType.MouseDrag, at + dx, new Vector2(6, 0));
        bool acted = Host.Router.Handle("Escape", KeyMods.None, false);
        Check("Handle(Escape) cancels", acted && Capturing() == null && !vf.Dragging && Pos().Equals(p0) && Undos == u0);
        yield return Frames(2);
        text0 = f.text;
        Raw(EventType.MouseDrag, at, new Vector2(6, 0));
        Raw(EventType.MouseDrag, at + new Vector2(1, 0), new Vector2(6, 0));
        Raw(EventType.MouseUp, at);
        Check("the focus left in the field: moves and the release change nothing", f.text == text0 && Pos().Equals(p0) && Undos == u0, f.text + " / " + text0);
        f.Blur();
        yield return Frames(2);
        Check("the focus left in the field: the blur types nothing", Pos().Equals(p0) && Undos == u0, Pos().Y + ", entries " + (Undos - u0));

        // ---------------- a held Esc: its repeats are not a new press (Esc with nothing to cancel clears the selection)
        Raw(EventType.MouseDown, at);
        Raw(EventType.MouseDrag, at + dx, new Vector2(6, 0));
        UiCommands.Send(Host, JObject.FromObject(new { type = "key", key = "Escape", phase = "down" }), out _);
        Check("a held Esc: the first press cancels", Capturing() == null && Pos().Equals(p0) && Undos == u0);
        UiCommands.Send(Host, JObject.FromObject(new { type = "key", key = "Escape", phase = "down" }), out _);
        Check("a held Esc: its repeat leaves the selection", Ui.Selection.Count == 1 && Ui.Selection.Primary == id, Ui.Selection.Count + ", " + Log(1));
        UiCommands.Send(Host, JObject.FromObject(new { type = "key", key = "Escape", phase = "up" }), out _);
        Raw(EventType.MouseUp, at);
        yield return Frames(2);
        f.Blur();
        yield return Frames(1);
        Check("a held Esc: nothing written", Pos().Equals(p0) && Undos == u0, Pos().Y + ", entries " + (Undos - u0));

        // ---------------- a new drag works; typing works
        Raw(EventType.MouseDown, at);
        Raw(EventType.MouseDrag, at + dx, new Vector2(6, 0));
        Raw(EventType.MouseDrag, at + dx, new Vector2(6, 0));
        Vec3 m2 = Pos();
        Raw(EventType.MouseUp, at + dx);
        yield return Frames(2);
        Check("a new drag afterwards: one entry, the value", Undos == u0 + 1 && Mathf.Abs(m2.Y - p0.Y) > 1e-3f && Pos().Equals(m2), (Undos - u0) + ", " + Pos().Y);
        f.Blur();
        yield return Frames(1);
        Ui.Session.Commands.Undo();
        yield return Frames(2);
        Check("undo: the value as at the press", Pos().Equals(p0) && Undos == u0);
        f.value = 7f;
        yield return Frames(2);
        Check("typing afterwards: one entry", Undos == u0 + 1 && Mathf.Abs(Pos().Y - 7f) < 1e-5f, (Undos - u0) + ", " + Pos().Y);
        Ui.Session.Commands.Undo();
        yield return Frames(2);

        row.RemoveFromHierarchy();
        Ui.Ops.Select(Array.Empty<string>());
        Ui.Ops.Delete(new[] { id }, UiContext.User());
        yield return Frames(2);
    }

    // ---------------------------------------------------------------- FE2

    static void BuildGizmo()
    {
        var cam = Ui.Viewport.ViewCamera;
        if (cam && Host.GizmoPass != null) Gizmo.Build(Host.GizmoPass, cam);
    }

    /// <summary>The view on the shown handle, far enough to see all of it, near enough to tell its arms apart.</summary>
    static IEnumerator CentreView(float minDistance = 12f)
    {
        yield return Frames(2);
        BuildGizmo();
        var cc = Ui.Viewport.Camera;
        var cv = cc.Current;
        cc.SetView(Gizmo.PivotWorld, new Vector3(cv.Rot.X, cv.Rot.Y, cv.Rot.Z), Mathf.Max(minDistance, Mathf.Min(cv.Distance, 40f)), cv.Fov);
        cc.Flush();
        yield return Frames(4);
        BuildGizmo();
    }

    /// <summary>A panel point of an arm of the shown handle the hit test finds, and the arm's direction there.</summary>
    static bool AxisGrab(HandleKind kind, out Vector2 p, out Vector2 dir)
    {
        var gz = Gizmo;
        var vb = Ui.Viewport.Element.worldBound;
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

    /// <summary>A panel point of a ring of the shown handle the hit test finds, and the ring's direction there.</summary>
    static bool RingGrab(out Vector2 p, out Vector2 dir)
    {
        var gz = Gizmo;
        var vb = Ui.Viewport.Element.worldBound;
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

    static float[] CameraNow()
    {
        var v = Ui.Viewport.Camera.Current;
        return new[] { v.Pivot.X, v.Pivot.Y, v.Pivot.Z, v.Rot.X, v.Rot.Y, v.Rot.Z, v.Distance, v.Fov };
    }

    /// <summary>
    /// FE2's sequence on the handle shown now. kind: MoveAxis, RotateRing or ScaleAxis. read: the values this handle writes.
    /// Press, 3 moves, Esc, 3 moves, release: values as at the press, no entry, not dragging, the camera still; then a new drag: one entry.
    /// </summary>
    static IEnumerator EscOnHandle(string what, HandleKind kind, Func<float[]> read, float eps, string label)
    {
        BuildGizmo();
        var gz = Gizmo;
        Vector2 p, d;
        bool grabbed = kind == HandleKind.RotateRing ? RingGrab(out p, out d) : AxisGrab(kind, out p, out d);
        if (!Check(what + ": the handle can be grabbed", grabbed, "pivot " + (Vector2)gz.ToPanel(gz.PivotWorld) + ", length " + gz.LengthWorld + ", tool " + gz.ShownTool + ", viewport " + Ui.Viewport.Element.worldBound)) yield break;
        var v0 = read();
        var cam0 = CameraNow();
        int u0 = Undos, cancels = gz.Cancels;
        yield return Input(Pt(p, "down"), Pt(p + d * 10, "move"), Pt(p + d * 20, "move"), Pt(p + d * 30, "move"));
        Check(what + ": the drag runs and writes", gz.Dragging && !Same(read(), v0, eps), Show(v0) + " -> " + Show(read()));
        yield return Key("Escape");
        Check(what + " Esc: not dragging, counted as cancelled, no pointer held", !gz.Dragging && gz.Cancels == cancels + 1 && Capturing() == null, gz.Dragging + ", cancels +" + (gz.Cancels - cancels) + ", " + Name(Capturing()));
        Check(what + " Esc: the values as at the press, no entry", Same(read(), v0, eps) && Undos == u0, Show(read()) + ", entries " + (Undos - u0));
        Check(what + " Esc: no open edit", Ui.ActiveEdit == null || !Ui.ActiveEdit.IsOpen);
        yield return Input(Pt(p + d * 35, "move"), Pt(p + d * 40, "move"), Pt(p + d * 45, "move"));
        Check(what + " moves after Esc write nothing", Same(read(), v0, eps) && Undos == u0 && !gz.Dragging, Show(read()) + ", entries " + (Undos - u0));
        yield return Input(Pt(p + d * 45, "up"));
        Check(what + " the release writes nothing", Same(read(), v0, eps) && Undos == u0 && !gz.Dragging, Show(read()) + ", entries " + (Undos - u0));
        Check(what + " the camera did not move", Same(CameraNow(), cam0, 1e-5f), Show(CameraNow()));
        yield return Frames(2);
        BuildGizmo();
        yield return Input(Pt(p, "down"), Pt(p + d * 10, "move"), Pt(p + d * 20, "move"), Pt(p + d * 20, "up"));
        yield return Frames(2);
        var v1 = read();
        Check(what + " a new drag afterwards: one entry «" + label + "», the value", Undos == u0 + 1 && TopLabel == label && !Same(v1, v0, eps), (Undos - u0) + " / " + TopLabel + ", " + Show(v1));
        Ui.Session.Commands.Undo();
        Ui.Runtime?.ApplyNow();
        yield return Frames(2);
        Check(what + " undo: the values as at the press", Same(read(), v0, eps) && Undos == u0, Show(read()));
    }

    static IEnumerator SetTool(GizmoTool t)
    {
        Ui.Tool = t;
        yield return Frames(2);
    }

    static IEnumerator FE2()
    {
        yield return OkCall(new { cmd = "scene.pause", paused = true }, "the global pause");
        yield return Fe2Node();
        yield return Fe2Fk();
        yield return Fe2Ik();
        yield return Fe2Look();
        yield return Fe2Bone();
        yield return Fe2Route();
        yield return Fe2Hair();
        yield return Fe2Accessory();
        Ui.Tool = GizmoTool.Move;
        Ui.SubSelection = null;
        yield return OkCall(new { cmd = "scene.pause", paused = false }, "the pause off");
        // not paused: the same on a playing character, by the FK handle and the bone handle
        yield return Fe2Fk(" (no pause)");
        yield return Fe2Bone(" (no pause)");
        Ui.Tool = GizmoTool.Move;
        Ui.SubSelection = null;
        yield return Frames(2);
    }

    static IEnumerator Fe2Node()
    {
        // an item: it has the three channels (a folder has no scale)
        yield return Call(new { cmd = "item.add", no = 399 });
        Check("item.add 399", LastOk, Short(lastText));
        string ia = (string)last?["key"] ?? (string)last?["added"]?[0]?["key"];
        var b = Ui.Ops.AddFolder(null, null, -1, UiContext.User());
        if (!Check("an item and a folder", ia != null && b.Ok)) yield break;
        string ib = b.NewIds[0];
        Ui.Session.Set(ib, "transform.pos", new Vec3(3f, 0f, 0f), UiContext.User());
        Ui.Ops.Select(new[] { ia });
        yield return SetTool(GizmoTool.Move);
        yield return CentreView();
        float[] One() => Vals((Vec3)FieldAccessor.Get(Ui.Model, ia, "transform.pos"), (Vec3)FieldAccessor.Get(Ui.Model, ia, "transform.rot"), (Vec3)FieldAccessor.Get(Ui.Model, ia, "transform.scale"));
        yield return EscOnHandle("node, move", HandleKind.MoveAxis, One, 1e-5f, OpLabels.Transform);
        yield return SetTool(GizmoTool.Rotate);
        BuildGizmo();
        yield return EscOnHandle("node, rotate", HandleKind.RotateRing, One, 0.01f, OpLabels.Transform);
        yield return SetTool(GizmoTool.Scale);
        BuildGizmo();
        yield return EscOnHandle("node, scale", HandleKind.ScaleAxis, One, 1e-5f, OpLabels.Transform);
        Ui.Ops.Select(new[] { ia, ib });
        yield return SetTool(GizmoTool.Move);
        yield return CentreView();
        float[] Two() => Vals((Vec3)FieldAccessor.Get(Ui.Model, ia, "transform.pos"), (Vec3)FieldAccessor.Get(Ui.Model, ib, "transform.pos"));
        yield return EscOnHandle("two nodes selected, move", HandleKind.MoveAxis, Two, 1e-5f, OpLabels.Transform);
        Ui.Ops.Select(Array.Empty<string>());
        Ui.Ops.Delete(new[] { ia, ib }, UiContext.User());
        yield return Frames(2);
    }

    static IEnumerator Fe2Fk(string suffix = "")
    {
        const string arm = "cf_J_ArmUp00_R";
        yield return OkCall(new { cmd = "select", keys = new[] { charaA } }, "select the character");
        yield return OkCall(new { cmd = "fk.enable", key = charaA, on = true }, "FK on");
        yield return OkCall(new { cmd = "fk.enable", key = charaA, groups = new[] { "Body" }, on = true }, "FK group Body");
        yield return Frames(3);
        yield return OkCall(new { cmd = "select", sub = charaA + "/fk/" + arm }, "select the FK handle");
        yield return SetTool(GizmoTool.Rotate);
        yield return CentreView();
        float[] Read() => StateOf(charaA).Fk.Bones.TryGetValue(arm, out var v) ? Vals(v) : new float[3];
        yield return EscOnHandle("FK handle" + suffix, HandleKind.RotateRing, Read, 0.01f, OpLabels.FkBones);
        yield return OkCall(new { cmd = "fk.enable", key = charaA, on = false }, "FK off");
        yield return OkCall(new { cmd = "select", keys = new[] { charaA } }, "select the character alone");
        yield return Frames(2);
    }

    static IEnumerator Fe2Ik()
    {
        yield return OkCall(new { cmd = "select", keys = new[] { charaA } }, "select the character");
        yield return OkCall(new { cmd = "ik.enable", key = charaA, on = true }, "IK on");
        yield return Frames(5);
        yield return OkCall(new { cmd = "select", sub = charaA + "/ik/LeftHand" }, "select the IK handle");
        yield return SetTool(GizmoTool.Move);
        yield return CentreView();
        var driver = IkDriver.Of(Real(charaA));
        float[] Read() { var e = driver.Effective(IkTargetId.LeftHand); return Vals(e.Pos, e.Rot); }
        yield return EscOnHandle("IK target, move", HandleKind.MoveAxis, Read, 1e-5f, OpLabels.IkTargets);
        yield return SetTool(GizmoTool.Rotate);
        BuildGizmo();
        yield return EscOnHandle("IK target, rotate", HandleKind.RotateRing, Read, 0.01f, OpLabels.IkTargets);
        yield return OkCall(new { cmd = "ik.enable", key = charaA, on = false }, "IK off");
        yield return OkCall(new { cmd = "select", keys = new[] { charaA } }, "select the character alone");
        yield return Frames(2);
    }

    static IEnumerator Fe2Look()
    {
        yield return OkCall(new { cmd = "select", keys = new[] { charaA } }, "select the character");
        int e0 = StateOf(charaA).Look.Eyes;
        Ui.ShowResult(Ui.Ops.SetLook(new[] { charaA }, 4, null, UiContext.User()));
        yield return Frames(3);
        yield return OkCall(new { cmd = "select", sub = charaA + "/look" }, "select the look handle");
        yield return SetTool(GizmoTool.Move);
        yield return CentreView();
        float[] Read() => Vals(StateOf(charaA).Look.Target);
        yield return EscOnHandle("look handle", HandleKind.MoveAxis, Read, 1e-5f, OpLabels.LookTarget);
        Ui.ShowResult(Ui.Ops.SetLook(new[] { charaA }, e0, null, UiContext.User()));
        yield return OkCall(new { cmd = "select", keys = new[] { charaA } }, "select the character alone");
        yield return Frames(2);
    }

    static IEnumerator Fe2Bone(string suffix = "")
    {
        const string finger = "cf_J_Hand_Index02_L";
        yield return OkCall(new { cmd = "select", sub = charaA + "/bone/" + finger }, "select the bone handle");
        yield return SetTool(GizmoTool.Move);
        yield return CentreView();
        var bone = AdvancedPoseResolver.Resolve(Real(charaA), finger);
        float[] Read()
        {
            var block = StateOf(charaA).AdvancedPose;
            return block != null && block.Bones.TryGetValue(finger, out var c) ? Vals(c.Pos, c.Rot, c.Scale) : Vals(Vec3.Zero, Vec3.Zero, Vec3.One);
        }
        yield return EscOnHandle("bone handle, move" + suffix, HandleKind.MoveAxis, Read, 1e-5f, OpLabels.BoneCorrection);
        yield return SetTool(GizmoTool.Rotate);
        BuildGizmo();
        yield return EscOnHandle("bone handle, rotate" + suffix, HandleKind.RotateRing, Read, 0.01f, OpLabels.BoneCorrection);
        yield return SetTool(GizmoTool.Scale);
        BuildGizmo();
        yield return EscOnHandle("bone handle, scale" + suffix, HandleKind.ScaleAxis, Read, 1e-5f, OpLabels.BoneCorrection);
        Check("the bone exists", bone);
        yield return OkCall(new { cmd = "select", keys = new[] { charaA } }, "select the character alone");
        yield return Frames(2);
    }

    static IEnumerator Fe2Route()
    {
        Ui.Registry.Run(ActionIds.AddRoute, "test");
        yield return Frames(2);
        string id = Ui.Selection.Primary;
        if (!Check("a route", id != null && Ui.Model.Find(id)?.Kind == NodeKind.Route, id)) yield break;
        for (int k = 0; k < 2; k++)
        {
            Ui.Registry.Run(ActionIds.RouteAddPoint, "inspector");
            yield return Frames(1);
        }
        var st = (RouteNodeState)Ui.Model.Find(id).State;
        if (!Check("the route has 3 points", st.Points.Count == 3, st.Points.Count.ToString())) yield break;
        string startId = st.Points[0].Id, pointId = st.Points[1].Id;
        float[] Read()
        {
            var s = (RouteNodeState)Ui.Model.Find(id).State;
            return s.Points.SelectMany(p => new[] { (float)p.Pos.X, (float)p.Pos.Y, (float)p.Pos.Z, p.Rot.X, p.Rot.Y, p.Rot.Z }).ToArray();
        }
        Ui.SubSelection = id + "/points/" + pointId;
        yield return SetTool(GizmoTool.Move);
        yield return CentreView(30f);
        yield return EscOnHandle("route point, move", HandleKind.MoveAxis, Read, 1e-4f, OpLabels.RoutePoint);
        Ui.SubSelection = id + "/points/" + startId;
        yield return SetTool(GizmoTool.Rotate);
        yield return CentreView(30f);
        yield return EscOnHandle("route start point, rotate", HandleKind.RotateRing, Read, 0.01f, OpLabels.RoutePoint);
        Ui.SubSelection = null;
        Ui.Ops.Select(Array.Empty<string>());
        Ui.Ops.Delete(new[] { id }, UiContext.User());
        yield return Frames(2);
    }

    static CharaEditTab OpenEdit(string id, string node)
    {
        if (Ui.Selection.Primary != id || Ui.Selection.Count != 1) Ui.Ops.Select(new[] { id });
        Ui.Shell.ShowPanel("inspector");
        var insp = Panel.visualTree.Q<InspectorPanel>();
        if (insp == null) return null;
        insp.Refresh(true);
        insp.Chara?.ShowTab(CharaEditTab.TabId);
        var tab = insp.Chara?.TabBodies.TryGetValue(CharaEditTab.TabId, out var b) == true ? b as CharaEditTab : null;
        tab?.ShowNode(node);
        return tab;
    }

    static IEnumerator Fe2Hair()
    {
        var tab = OpenEdit(charaA, "hair");
        yield return Frames(4);
        if (!Check("the Edit tab on the Hair page", tab != null && tab.panel != null)) yield break;
        var fold = tab.Query<Foldout>("edit-section-hair.hair.back.bundles").ToList().FirstOrDefault();
        if (fold != null) fold.value = true;
        yield return Frames(3);
        var list = HairBundleList.Current;
        int index = list != null ? list.HandleIndices().DefaultIfEmpty(-1).First() : -1;
        if (!Check("a bundle with a handle", index >= 0, list == null ? "no list" : "part " + list.Part)) yield break;
        Ui.SubSelection = StudioOps.HairSub(charaA, list.Part, index);
        yield return SetTool(GizmoTool.Move);
        yield return CentreView(10f);
        float[] Read() { var e = HairBundleList.Current.EffectiveOf(charaA)[index]; return Vals(e.Move, e.Rotate); }
        yield return EscOnHandle("hair bundle, move", HandleKind.MoveAxis, Read, 1e-5f, OpLabels.EditHair);
        yield return SetTool(GizmoTool.Rotate);
        BuildGizmo();
        yield return EscOnHandle("hair bundle, rotate", HandleKind.RotateRing, Read, 1e-5f, OpLabels.EditHair);
        Ui.SubSelection = null;
        yield return Frames(2);
    }

    static IEnumerator Fe2Accessory()
    {
        var tab = OpenEdit(charaA, "accessories");
        yield return Frames(4);
        if (!Check("the Edit tab on the Accessories page", tab != null && tab.panel != null)) yield break;
        var r = Real(charaA);
        // the first accessory that has a first transform set
        string acc = null;
        foreach (var slot in r.Context.Accessories)
        {
            if (slot == null || !slot.Object) continue;
            var cmp = slot.Object.GetComponent<AIChara.CmpAccessory>();
            string aid = r.AccessoryIdOf(slot);
            if (cmp != null && cmp.trfMove01 && aid != null) { acc = aid; break; }
        }
        if (!Check("BR-Chan has an accessory with a transform set", acc != null)) yield break;
        tab.SelectAccessory(acc);
        yield return Frames(3);
        int shown = tab.AccessoryShownSlot;
        var fold = tab.Query<Foldout>("edit-section-" + InspectorSchemas.AccessorySectionId(shown, "adjust")).ToList().FirstOrDefault();
        if (fold != null) fold.value = true;
        yield return Frames(3);
        Ui.SubSelection = StudioOps.AccessorySub(charaA, acc, 0);
        yield return SetTool(GizmoTool.Move);
        yield return CentreView(6f);
        string Path(string member) => InspectorSchemas.AccessoryEditPath(acc, "moves[0]." + member);
        float[] Read() => Vals((Vec3)FieldAccessor.Get(Ui.Model, charaA, Path("position")), (Vec3)FieldAccessor.Get(Ui.Model, charaA, Path("rotation")));
        yield return EscOnHandle("accessory set, move", HandleKind.MoveAxis, Read, 1e-5f, OpLabels.EditAccessory);
        yield return SetTool(GizmoTool.Rotate);
        BuildGizmo();
        yield return EscOnHandle("accessory set, rotate", HandleKind.RotateRing, Read, 1e-5f, OpLabels.EditAccessory);
        Ui.SubSelection = null;
        yield return Frames(2);
    }
}
