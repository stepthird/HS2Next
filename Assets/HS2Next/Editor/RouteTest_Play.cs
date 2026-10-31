// RouteTest.play (Phase 3.6 package R; graphics): the app with the Studio UI in Editor
// play mode, real frames at a fixed 1/60 s (Time.captureFramerate).
//   P1 a looping route moves with the frame time: τ = frames × 1/60, the child root = the evaluator at τ
//   P2 the global pause holds it (bit-equal over 20 frames); resume continues
//   P3 the line and markers are in the view: the view camera rendered with view.routes on and off differs where the line is
//   P4 … and never in a capture: ViewCapture with view.routes on and off gives bit-equal PNGs (lines, markers, labels absent)
//   P5 UI: Add ▸ Route (add.route) selects the new route; the inspector's Route section; route.addPoint from the inspector's
//      action (one entry "Add route point"); the tree's "Points (n)" and point rows; a point row click sub-selects the point
//      and shows the Route point section with HS2's six eases + "More…"; route.play / route.stop actions; the scene panel's
//      route list row
//   P6 a point gizmo drag (move X handle) is one entry "Route point" and moves the point; the start point shows a rotate gizmo
// Run: -executeMethod RouteTest.RunPlay -NoQuit; report Spike/out/routes/route_play_report.txt.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.App;
using HS2Next.App.Commands;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Routes;
using HS2Next.Scenes.Runtime;
using HS2Next.UI;
using HS2Next.UI.Core;
using HS2Next.UI.Gizmos;
using HS2Next.UI.Panels;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class RouteTest
{
    public const int PlayPort = 17362;
    static string PlayReport => Path.Combine(OutDir, "route_play_report.txt");
    static string PlayLaunch =>
        "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},\"userData\":\"Spike/out/routes/play_userdata\",\"bridge\":{\"port\":" + PlayPort + ",\"outputDir\":\"Spike/out/routes/play_out\",\"allowQuit\":false}}";

    public static void RunPlay()
    {
        Directory.CreateDirectory(OutDir);
        string cfg = Path.Combine(OutDir, "play_userdata", "config");
        try { if (Directory.Exists(cfg)) Directory.Delete(cfg, true); } catch (Exception) { }
        File.WriteAllText(PlayReport, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("RouteTest.Play", PlayReport, 20, true, PlayLaunch);
    }

    static IEnumerator Play()
    {
        rep = new StringBuilder(); passes = fails = warns = 0; perTest.Clear(); measurements.Clear(); only = null;
        current = "P0";
        rep.AppendLine($"RouteTest.play (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, screen {Screen.width}x{Screen.height})");
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting && UiHost.Current != null && UiHost.Current.IsReady, 240, ok);
        Check("app and UI ready", ok[0]);
        int capture = Time.captureFramerate;
        Time.captureFramerate = 60;
        if (ok[0])
        {
            var e = PlayInner(UiHost.Current, UiHost.Current.Context);
            while (true)
            {
                bool more;
                try { more = e.MoveNext(); }
                catch (Exception ex) { Check("threw " + ex, false); break; }
                if (!more) break;
                yield return e.Current;
            }
        }
        Time.captureFramerate = capture;
        RouteSettings.Reset();
        var sum = new StringBuilder();
        sum.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL ({warns} WARN)");
        foreach (var kv in perTest.OrderBy(k => k.Key, StringComparer.Ordinal)) sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail");
        rep.Insert(0, sum.ToString());
        File.WriteAllText(PlayReport, rep.ToString(), new UTF8Encoding(false));
        Debug.Log($"ROUTE PLAY TEST DONE: {passes} PASS, {fails} FAIL");
        PlayModeHarness.ExitCode = fails > 0 ? 1 : 0;
    }

    static T PanelOf<T>(UiContext ui, string id) where T : VisualElement
    {
        ui.Shell?.FocusPanel(id);
        var root = ui.Shell?.PanelRoot(id);
        if (root is T t) return t;
        return root?.Q<T>();
    }

    static IEnumerator PlayInner(UiHost host, UiContext ui)
    {
        var ops = ui.Ops;
        var app = AppCore.Current;
        ops.NewScene();
        yield return PlayModeHarness.WaitFrames(2);

        // ---------------- P5 (first part): Add ▸ Route through the action
        current = "P5";
        int u0 = app.Session.Commands.UndoList.Count;
        ui.Registry.Run(ActionIds.AddRoute, "test");
        yield return PlayModeHarness.WaitFrames(2);
        string id = ui.Selection.Primary;
        Check("add.route adds a route (one entry \"Add route\") and selects it", id != null && app.Session.Model.Find(id)?.Kind == NodeKind.Route &&
            app.Session.Commands.UndoList.Count == u0 + 1 && app.Session.Commands.UndoList[0].Label == "Add route", id ?? "none");
        if (id == null) yield break;
        var insp = PanelOf<InspectorPanel>(ui, "inspector");
        insp?.Refresh(true);
        yield return PlayModeHarness.WaitFrames(2);
        Check("the inspector shows the Route section (state line, rows)", insp != null && insp.Q("section-route") != null && insp.Q<Label>("route-state") != null);
        for (int k = 0; k < 3; k++)
        {
            int n = app.Session.Commands.UndoList.Count;
            ui.Registry.Run(ActionIds.RouteAddPoint, "inspector");
            yield return PlayModeHarness.WaitFrames(1);
            Check($"route.addPoint #{k + 1}: one entry \"Add route point\"", app.Session.Commands.UndoList.Count == n + 1 && app.Session.Commands.UndoList[0].Label == "Add route point");
        }
        var st = (RouteNodeState)app.Session.Model.Find(id).State;
        Check("the route has 4 points continuing the path (R8)", st.Points.Count == 4 && st.Points[2].Pos.Z == 20 && st.Points[3].Pos.Z == 30, string.Join(" ", st.Points.Select(p => p.Pos.Z)));
        ops.SetRoutePoints(id, new[] { st.Points[1].Id }, new System.Collections.Generic.List<(string, object)> { ("connection", "curve") }, UiContext.User());
        ops.SetRoutePointPose(id, st.Points[2].Id, new Vector3(6, 0, 20), null, UiContext.User());
        var ws = PanelOf<WorkspacePanel>(ui, WorkspacePanel.PanelId) ?? WorkspacePanel.Current;
        ws?.RebuildNow();
        yield return PlayModeHarness.WaitFrames(2);
        var g = ws?.Rows.RowOfNode(id)?.Children.FirstOrDefault(r => r.Kind == TreeRowKind.RoutePoints);
        Check("the tree: a \"Points (4)\" row under the route with four point rows", g != null && g.Index == 4 && g.Children.Count == 4 && g.Children[0].Kind == TreeRowKind.RoutePoint && g.Children[0].Index == 0);
        ui.SubSelection = id + "/points/" + st.Points[2].Id;
        yield return PlayModeHarness.WaitFrames(3);
        insp?.Refresh();
        yield return PlayModeHarness.WaitFrames(1);
        var ease = insp?.Q<RouteEaseRow>();
        Check("a point row click (sub-selection) shows the Route point section with the ease row (six + More…)", insp?.Q("section-routePoint") != null && ease != null &&
            ease.Dropdown.choices.Count == 7 && ease.Dropdown.choices[6] == L10n.Get("inspector.route.point.easeMore"), ease == null ? "no ease row" : string.Join("|", ease.Dropdown.choices));
        Check("the selected point's tree row is reachable", ws?.Rows.RowOfRoutePoint(id, st.Points[2].Id) != null);

        // ---------------- P6 a point gizmo drag
        current = "P6";
        ui.Tool = GizmoTool.Move;
        ui.Viewport.Camera.SetView(new Vector3(3, 0, 15), new Vector3(60, 0, 0), 40, 40);
        ui.Viewport.Camera.Flush();
        yield return PlayModeHarness.WaitFrames(4);
        var gz = ui.Viewport.Gizmo;
        // batch play mode does not render the view every frame: render it once so that the gizmo pass is built
        void Build() { host.GizmosIntoTargets = true; RenderView(ui.Viewport.ViewCamera, 640, 360); host.GizmosIntoTargets = false; }
        Build();
        {
            var mi = typeof(TransformTool).GetMethod("TryTarget", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var args = new object[] { null };
            object res = mi?.Invoke(gz, args);
            var rr = ops.RouteReal(id);
            Info($"P6 diag: sub {ui.SubSelection}; gizmos {ui.Options.Bool(OptionDefs.ViewGizmos)}; tool {ui.Tool}; real {(rr != null)} holder {(rr != null && rr.Holder)} path {(rr?.Path != null)}; TryTarget {res}");
        }
        // the point's world position (the route lands at the view's pivot when it is added: SessionOptions.DefaultPivot, G)
        var rr6 = ops.RouteReal(id);
        var want6 = rr6 != null && rr6.Holder ? rr6.Holder.TransformPoint(new Vector3(st.Points[2].Pos.X, st.Points[2].Pos.Y, st.Points[2].Pos.Z)) : new Vector3(6, 0, 20);
        Check("a sub-selected point shows a move gizmo", gz.Visible && gz.ShownTool == GizmoTool.Move && (gz.PivotWorld - want6).magnitude < 1e-3f, $"visible {gz.Visible} tool {gz.ShownTool} pivot {gz.PivotWorld} (point {want6})");
        var tip = gz.ToPanel(gz.PivotWorld + gz.Axes * Vector3.right * gz.LengthWorld * 0.45f);
        int nd = app.Session.Commands.UndoList.Count;
        bool began = gz.TryBegin(tip, EventModifiers.None);
        var tip2 = gz.ToPanel(gz.PivotWorld + gz.Axes * Vector3.right * gz.LengthWorld * 1.6f);
        if (began) { gz.Drag(((Vector2)tip + (Vector2)tip2) * 0.5f, EventModifiers.None); gz.Drag(tip2, EventModifiers.None); gz.End(); }
        yield return PlayModeHarness.WaitFrames(2);
        var moved = st.Points[2].Pos;
        Check("the X-handle drag is one entry \"Route point\" and moves the point along X", began && app.Session.Commands.UndoList.Count == nd + 1 && app.Session.Commands.UndoList[0].Label == "Route point" &&
            moved.X > 6.1f && Math.Abs(moved.Z - 20) < 1e-3f, $"began {began}, entries {app.Session.Commands.UndoList.Count - nd}, pos ({moved.X}, {moved.Y}, {moved.Z})");
        ui.SubSelection = id + "/points/" + st.Points[0].Id;
        yield return PlayModeHarness.WaitFrames(3);
        Build();
        Check("the start point shows a rotate gizmo", gz.Visible && gz.ShownTool == GizmoTool.Rotate, $"{gz.ShownTool}");
        ui.SubSelection = null;
        ops.Select(new[] { id });

        // ---------------- P1 real frames
        current = "P1";
        ops.SetRoute(new[] { id }, new System.Collections.Generic.List<(string, object)> { ("loop", true) }, UiContext.User());
        ui.Registry.Run(ActionIds.RoutePlay, "inspector");
        var real = ops.RouteReal(id);
        Check("route.play (action): playing", real?.PlayState == RoutePlayState.Playing);
        double t0 = real.Time;
        yield return PlayModeHarness.WaitFrames(30);
        double t1 = real.Time;
        var path = RoutePath.Build(st, RouteSettings.Options());
        var s = path.Evaluate(t1);
        Check("30 real frames at 1/60 s: τ + 0.5 s", Math.Abs(t1 - t0 - 0.5) < 1e-6, $"Δτ {t1 - t0:R}");
        Check("the child root = the evaluator at τ (1e-5)", (real.ChildRoot.localPosition - V(s.Position)).magnitude < 1e-5);

        // ---------------- P2 pause
        current = "P2";
        app.Runtime.SetPaused(true);
        var p0 = real.ChildRoot.localPosition; double tp = real.Time;
        yield return PlayModeHarness.WaitFrames(20);
        Check("the global pause holds the route (τ and the child root bit-equal over 20 frames)", real.Time == tp && real.ChildRoot.localPosition == p0);
        app.Runtime.SetPaused(false);
        yield return PlayModeHarness.WaitFrames(5);
        Check("resume continues", real.Time > tp);
        ui.Registry.Run(ActionIds.RouteStop, "inspector");
        yield return PlayModeHarness.WaitFrames(1);
        Check("route.stop (action): stopped at the start point", real.PlayState == RoutePlayState.Stopped && real.ChildRoot.localPosition == Vector3.zero);

        // ---------------- P3 / P4 line in the view, not in captures
        current = "P3";
        ops.Select(new string[0]);
        ui.Viewport.Camera.SetView(new Vector3(3, 0, 15), new Vector3(60, 0, 0), 40, 40);
        ui.Viewport.Camera.Flush();
        yield return PlayModeHarness.WaitFrames(3);
        var cam = ui.Viewport.ViewCamera;
        host.GizmosIntoTargets = true;
        var on = RenderView(cam, 640, 360);
        int drawn = ui.Viewport.Gizmo.RouteLinesDrawn, verts = ui.Viewport.Gizmo.RouteLineVertices;
        ui.Options.Set(OptionDefs.ViewRoutes, false);
        yield return PlayModeHarness.WaitFrames(1);
        var off = RenderView(cam, 640, 360);
        int drawnOff = ui.Viewport.Gizmo.RouteLinesDrawn;
        ui.Options.Set(OptionDefs.ViewRoutes, true);
        yield return PlayModeHarness.WaitFrames(1);
        RenderView(cam, 640, 360);   // the labels of the last built frame
        host.GizmosIntoTargets = false;
        int diff = 0;
        for (int i = 0; i < on.Length; i++) if (Math.Abs(on[i].r - off[i].r) + Math.Abs(on[i].g - off[i].g) + Math.Abs(on[i].b - off[i].b) > 30) diff++;
        Info($"view: {diff} pixels differ with routes on / off; lines drawn {drawn} ({verts} vertices), with the toggle off {drawnOff}");
        Check("the line and markers are drawn in the view (routes on vs off differ; the toggle hides them)", diff > 500 && drawn == 1 && drawnOff == 0, $"{diff} px");
        var markers = ui.Viewport.Element.Query<Label>(className: "hs-viewport__route-marker").ToList().Where(l => l.style.display.value == DisplayStyle.Flex).ToList();
        Info("route marker labels: " + string.Join(", ", ui.Viewport.Element.Query<Label>(className: "hs-viewport__route-marker").ToList().Select(l => l.text + ":" + l.style.display.value)));
        Check("the point labels S, 1, 2, 3 are shown over the viewport (UI overlay)", markers.Count == 4 && markers.Any(l => l.text == "S"), string.Join(",", markers.Select(l => l.text)));
        current = "P4";
        var c1 = ViewCapture.Render(cam, 512, 288);
        yield return PlayModeHarness.WaitFrames(1);
        var c1b = ViewCapture.Render(cam, 512, 288);
        ui.Options.Set(OptionDefs.ViewRoutes, false);
        yield return PlayModeHarness.WaitFrames(1);
        var c2 = ViewCapture.Render(cam, 512, 288);
        ui.Options.Set(OptionDefs.ViewRoutes, true);
        bool same = c1 != null && c2 != null && c1.SequenceEqual(c2);
        int baseD = CaptureDiff(c1, c1b), routeD = CaptureDiff(c1, c2);
        Check("captures never contain the route line or markers (bit-equal with routes on / off, or no more difference than two captures)", same || routeD <= baseD + 20, $"bit-equal {same}, {baseD} / {routeD} px");

        // ---------------- P5 (rest): scene panel's route list
        current = "P5";
        var sp = PanelOf<ScenePanel>(ui, "scene");
        yield return PlayModeHarness.WaitFrames(20);
        Check("the scene panel's route list has the route's row", sp?.Q("route-row-" + id) != null);
        ui.Registry.Run(ActionIds.RoutePlayAll, "scene");
        yield return PlayModeHarness.WaitFrames(1);
        Check("route.playAll plays the stopped route", ops.RouteState(id) == RoutePlayState.Playing);
        ui.Registry.Run(ActionIds.RouteStopAll, "scene");
        yield return PlayModeHarness.WaitFrames(1);
        Check("route.stopAll stops it", ops.RouteState(id) == RoutePlayState.Stopped);
        // Delete on a sub-selected start point is refused, on another point deletes it
        ui.SubSelection = id + "/points/" + st.Points[0].Id;
        int nx = app.Session.Commands.UndoList.Count;
        ui.Registry.Run(ActionIds.Delete, "tree");
        Check("edit.delete on the start point: refused (nothing deleted)", app.Session.Model.Find(id) != null && st.Points.Count == 4 && app.Session.Commands.UndoList.Count == nx);
        ui.SubSelection = id + "/points/" + st.Points[3].Id;
        ui.Registry.Run(ActionIds.Delete, "tree");
        Check("edit.delete on a point deletes the point (one entry \"Delete route point\"), not the route", app.Session.Model.Find(id) != null && st.Points.Count == 3 &&
            app.Session.Commands.UndoList[0].Label == "Delete route point");
    }

    static int CaptureDiff(byte[] a, byte[] b)
    {
        if (a == null || b == null) return int.MaxValue;
        var ta = new Texture2D(2, 2); var tb = new Texture2D(2, 2);
        ta.LoadImage(a); tb.LoadImage(b);
        int n = 0;
        if (ta.width == tb.width && ta.height == tb.height)
        {
            var pa = ta.GetPixels32(); var pb = tb.GetPixels32();
            for (int i = 0; i < pa.Length; i++)
                if (Math.Abs(pa[i].r - pb[i].r) + Math.Abs(pa[i].g - pb[i].g) + Math.Abs(pa[i].b - pb[i].b) > 30) n++;
        }
        else n = int.MaxValue;
        UnityEngine.Object.Destroy(ta); UnityEngine.Object.Destroy(tb);
        return n;
    }

    static Color32[] RenderView(Camera cam, int w, int h)
    {
        var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        var prevT = cam.targetTexture;
        var prevR = cam.rect;
        cam.targetTexture = rt;
        cam.rect = new Rect(0, 0, 1, 1);
        cam.Render();
        cam.targetTexture = prevT;
        cam.rect = prevR;
        var prevA = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prevA;
        RenderTexture.ReleaseTemporary(rt);
        var px = tex.GetPixels32();
        UnityEngine.Object.Destroy(tex);
        return px;
    }
}
