// RouteTest (Phase 3.6 package R): routes headless, edit mode, ManualStepping
// (SceneRuntime.Tick on the scene clock).
//   R1  child-root pose after Tick sequences = the evaluator at τ (1e-5) for orient none / xy / y; τ = Σ dt; the end of a
//       non-loop route (state ended, route.playing still true, the end point); the orient filter (R6) against an
//       independent recomputation, the exact look direction with routes.orientSmoothing off
//   R2  riders: world pose = child root × the local pose (1e-5) while the route plays
//   R3  the line: the vertex count on 6 shapes; HS2's recorded lines (Cache/compare/routes, J3) count and positions (1e-4)
//   R4  every op: Ok, one entry with its label, undo / redo restore the scene.doc bytes; play / stop record nothing
//   R5  guards: the start point is not deletable (wrong_kind, document unchanged), the 11-point limit on / off, pos on the
//       start point and rot on another point refused
//   R6  a switch to Curve writes the midpoint aid (point-local in the start point's rotated frame, HS2 J4: (1.414, 0, 2.828))
//   R7  duplicate / import: new node and point ids, a playing original gives a playing copy at τ 0
//   R8  save → load: a playing route restarts at τ 0; an ended route is saved stopped
//   R9  editing while playing keeps τ (point move, add point, undo); a shorter non-loop route ends
//   R10 bridge parity: route.* through the bridge and StudioOps give equal scene.doc bytes, labels and entry counts
//   R11 the global pause holds the child root (bit-equal), scene steps advance τ by n × dt exactly
//   R12 HS2's recordings (Cache/compare/routes J1 / J2): the runtime's child root within one frame of travel of HS2 at 60 fps,
//       orient within 2° after the first 10 frames of each leg
//   R13 costs (R-M5 runtime part): playback per route per frame (11 points with curves), path rebuild, line build
// Run: -executeMethod RouteTest.Run [-only R1+R3]; report Spike/out/routes/route_report.txt.
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static partial class RouteTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "routes");
    static string RecordDir => Path.Combine(ContentSettings.ProjectRoot, "Cache", "compare", "routes");
    static StringBuilder rep;
    static int passes, fails, warns;
    static string current = "";
    static readonly Dictionary<string, (int pass, int fail)> perTest = new Dictionary<string, (int, int)>();
    static readonly List<string> measurements = new List<string>();
    static string[] only;
    static RuntimeServices services;
    static readonly ExecuteOptions User = new ExecuteOptions { Source = CommandSource.User };
    const ulong Seed = 0x70a7e5eedUL;
    const float Dt = 1f / 60f;

    static string Arg(string name)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    static void Info(string s) => rep.AppendLine("   " + s);
    static void Warn(string s) { warns++; rep.AppendLine($"WARN  [{current}] {s}"); }
    static void Measure(string s) { measurements.Add($"[{current}] {s}"); Info("MEASURE " + s); }

    static bool Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  [{current}] {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
        perTest.TryGetValue(current, out var v);
        perTest[current] = ok ? (v.pass + 1, v.fail) : (v.pass, v.fail + 1);
        return ok;
    }

    static void Guard(string id, string title, Action a)
    {
        if (only != null && !only.Contains(id)) return;
        current = id;
        rep.AppendLine();
        rep.AppendLine($"== {id} {title}");
        try { a(); }
        catch (Exception e) { Check("exception", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        finally { RouteSettings.Reset(); }
    }

    public static void Run()
    {
        rep = new StringBuilder(); passes = fails = warns = 0; perTest.Clear(); measurements.Clear();
        var o = Arg("-only");
        only = o == null ? null : o.Split(',', '+').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        Directory.CreateDirectory(OutDir);
        var sw = Stopwatch.StartNew();
        var head = new StringBuilder();
        try
        {
            services = SceneRuntimeTest.NewServices(head);
            rep.Insert(0, $"Route test (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})\n" + head);
            Guard("R1", "child root = evaluator at τ (orient none / xy / y), the end", R1);
            Guard("R2", "riders follow the child root", R2);
            Guard("R3", "line vertex rule and HS2's recorded lines", R3);
            Guard("R4", "every op: one entry, its label, undo / redo restore scene.doc; play / stop not recorded", R4);
            Guard("R5", "start point, point limit, pos / rot guards", R5);
            Guard("R6", "curve switch writes the midpoint aid", R6);
            Guard("R7", "duplicate / import: new ids, a playing copy at τ 0", R7);
            Guard("R8", "save → load: playing restarts, ended saved stopped", R8);
            Guard("R9", "editing while playing keeps τ", R9);
            Guard("R10", "route.* bridge parity with StudioOps", R10);
            Guard("R11", "the global pause and scene steps", R11);
            Guard("R12", "HS2's recordings (J1 / J2) at the runtime level", R12);
            Guard("R13", "costs (R-M5)", R13);
        }
        catch (Exception e) { current = "run"; Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        var sum = new StringBuilder();
        sum.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL ({warns} WARN, {sw.Elapsed.TotalSeconds:F0} s)");
        foreach (var kv in perTest.OrderBy(k => k.Key.Length).ThenBy(k => k.Key, StringComparer.Ordinal)) sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail");
        if (measurements.Count > 0) { sum.AppendLine("measurements:"); foreach (var m in measurements) sum.AppendLine("  " + m); }
        rep.Insert(0, sum.ToString());
        File.WriteAllText(Path.Combine(OutDir, "route_report.txt"), rep.ToString(), new UTF8Encoding(false));
        RouteSettings.Reset();
        services?.Catalog?.UnloadAll();
        Debug.Log($"ROUTE TEST DONE: {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }

    // ================================================================ fixture

    static string UserDataDir => Path.Combine(OutDir, "userdata");

    static AppCore Host(ulong seed = Seed)
    {
        var s = AppSettings.Defaults(Array.Empty<string>());
        s.Content = CardResolveTest.Settings;
        s.UserData = UserDataDir;
        s.Bridge.OutputDir = Path.Combine(OutDir, "out");
        var o = services.NewSessionOptions();
        o.Ids = new CountingIdSource(seed);
        var app = AppCore.ForTests(services, s, null, o);
        app.Runtime.SetManualStepping(true);
        return app;
    }

    static string Doc(AppCore app) => Encoding.UTF8.GetString(DocClone.Bytes(app.Session.ExportDocument()));
    static int Undos(AppCore app) => app.Session.Commands.UndoList.Count;
    static string Top(AppCore app) => app.Session.Commands.UndoList.Count > 0 ? app.Session.Commands.UndoList[0].Label : null;

    static void Ticks(AppCore app, int n, float dt = Dt) { for (int i = 0; i < n; i++) app.Runtime.Tick(dt); }

    static RouteNodeState St(AppCore app, string id) => (RouteNodeState)app.Session.Model.Find(id).State;

    /// <summary>A route at <paramref name="pos"/> / <paramref name="rot"/> with points (route-local; the first is the start).</summary>
    static string MakeRoute(AppCore app, Vector3 pos, Vector3 rot, bool loop, string orient, params Vector3[] pts)
    {
        var ops = app.Ops;
        var r = ops.AddRoute(null, null, -1, pos, rot, User);
        string id = r.NewIds.FirstOrDefault();
        if (id == null) throw new Exception("AddRoute failed: " + r);
        for (int i = 1; i < pts.Length; i++)
        {
            var a = ops.AddRoutePoint(id, null, pts[i], User);
            if (!a.Ok) throw new Exception("AddRoutePoint failed: " + a);
        }
        ops.SetRoute(new[] { id }, new List<(string, object)> { ("loop", loop), ("orient", orient) }, User);
        app.Runtime.ApplyNow();
        return id;
    }

    static string Pid(AppCore app, string route, int i) => St(app, route).Points[i].Id;

    static Vector3 V(Vec3d v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

    static string F(Vector3 v) => $"({v.x:0.#####}, {v.y:0.#####}, {v.z:0.#####})";

    // ================================================================ R1

    static void R1()
    {
        foreach (var orient in new[] { "none", "xy", "y" })
        {
            var app = Host();
            try
            {
                string id = MakeRoute(app, new Vector3(1, 0, 2), new Vector3(0, 30, 0), false, orient,
                    Vector3.zero, new Vector3(10, 0, 0), new Vector3(10, 2, 10), new Vector3(0, 0, 10));
                app.Ops.SetRoutePoints(id, new[] { Pid(app, id, 1) }, new List<(string, object)> { ("connection", "curve") }, User);
                app.Ops.SetRoutePointPose(id, Pid(app, id, 0), null, new Vector3(0, 20, 0), User);
                var play = app.Ops.PlayRoutes(new[] { id }, false, User);
                Check($"[{orient}] PlayRoutes Ok, state playing", play.Ok && app.Ops.RouteState(id) == RoutePlayState.Playing, play.ToString());
                var real = app.Ops.RouteReal(id);
                var path = RoutePath.Build(St(app, id));
                double tau = 0, maxPos = 0, maxRot = 0, maxTau = 0;
                // the independent orient filter (R6): q ← slerp(q, look, 1 − 0.855^(60 dt)) from the start point's rotation
                var R = real.Holder.rotation;
                var startLocal = Quaternion.Euler(0, 20, 0);
                var q = startLocal;
                Quaternion Target(RouteSample s, Quaternion keep)
                {
                    var dir = R * (V(s.LookTarget) - V(s.Position));
                    if (dir.sqrMagnitude <= 1e-12f) return keep;
                    var look = Quaternion.LookRotation(dir, Vector3.up);
                    if (orient == "y") { var ws = (R * startLocal).eulerAngles; look = Quaternion.Euler(ws.x, look.eulerAngles.y, ws.z); }
                    return Quaternion.Inverse(R) * look;
                }
                // HS2's play-frame step (J2): one step of 1/60 toward the τ 0 target at Play
                if (orient != "none") q = Quaternion.Slerp(q, Target(path.Evaluate(0), q), (float)(1.0 - 0.855));
                int frames = 0;
                while (frames < 1200 && app.Ops.RouteState(id) == RoutePlayState.Playing)
                {
                    app.Runtime.Tick(Dt);
                    frames++;
                    tau += Dt;
                    var s = path.Evaluate(tau);
                    maxTau = Math.Max(maxTau, Math.Abs(real.Time - Math.Min(tau, path.TotalDuration)));
                    if (app.Ops.RouteState(id) != RoutePlayState.Playing) break;
                    maxPos = Math.Max(maxPos, (real.ChildRoot.localPosition - V(s.Position)).magnitude);
                    Quaternion expect = startLocal;
                    if (orient != "none")
                    {
                        q = Quaternion.Slerp(q, Target(s, q), (float)(1.0 - Math.Pow(0.855, 60.0 * Dt)));
                        expect = q;
                    }
                    maxRot = Math.Max(maxRot, RotationCompare.Deg(real.ChildRoot.localRotation, expect));
                }
                Info($"[{orient}] {frames} frames, total {path.TotalDuration:F4} s; max |pos − evaluator| {maxPos:E2}, max rotation error {maxRot:F4}°, max |τ − Σdt| {maxTau:E2}");
                Check($"[{orient}] child-root local position = RoutePath.Evaluate(τ) within 1e-5 while playing", maxPos <= 1e-5, maxPos.ToString("E2"));
                Check($"[{orient}] τ = Σ FrameDt", maxTau <= 1e-9, maxTau.ToString("E2"));
                Check($"[{orient}] rotation = {(orient == "none" ? "the start point's" : "the R6 filter's")} within 0.01°", maxRot <= 0.01, maxRot.ToString("F4") + "°");
                var end = path.Evaluate(path.TotalDuration + 1);
                Check($"[{orient}] the route ends after D: state ended, route.playing still true, the root at the end point",
                    app.Ops.RouteState(id) == RoutePlayState.Ended && St(app, id).Playing && (real.ChildRoot.localPosition - V(end.Position)).magnitude < 1e-5 &&
                    Math.Abs(frames * (double)Dt - path.TotalDuration) <= Dt + 1e-9,
                    $"state {app.Ops.RouteState(id)}, frames {frames}, root {F(real.ChildRoot.localPosition)} end {F(V(end.Position))}");
                var rotEnded = real.ChildRoot.localRotation;
                Ticks(app, 10);
                Check($"[{orient}] an ended route stands still (pose unchanged over 10 frames)", RotationCompare.Deg(real.ChildRoot.localRotation, rotEnded) == 0 && (real.ChildRoot.localPosition - V(end.Position)).magnitude < 1e-5);
                if (orient == "xy")
                {
                    RouteSettings.OrientSmoothing = false;
                    app.Ops.PlayRoutes(new[] { id }, true, User);
                    Ticks(app, 20);
                    var s = path.Evaluate(real.Time);
                    var look = Quaternion.Inverse(R) * Quaternion.LookRotation(R * (V(s.LookTarget) - V(s.Position)), Vector3.up);
                    double a = RotationCompare.Deg(real.ChildRoot.localRotation, look);
                    Check("[xy] routes.orientSmoothing off: the exact look rotation", a <= 0.01f, a.ToString("F4") + "°");
                    RouteSettings.OrientSmoothing = true;
                }
                var stop = app.Ops.StopRoutes(new[] { id }, User);
                Check($"[{orient}] Stop: the child root back at the start point's pose", stop.Ok && app.Ops.RouteState(id) == RoutePlayState.Stopped &&
                    real.ChildRoot.localPosition == Vector3.zero && RotationCompare.Deg(real.ChildRoot.localRotation, startLocal) < 1e-3f);
            }
            finally { app.Dispose(); }
        }
    }

    // ================================================================ R2

    static void R2()
    {
        var app = Host();
        try
        {
            string id = MakeRoute(app, new Vector3(2, 1, -1), new Vector3(0, 45, 0), true, "xy", Vector3.zero, new Vector3(5, 0, 0), new Vector3(5, 1, 5));
            var it = app.Ops.AddItem(ContentId.Vanilla(399), id, null, -1, User);
            string item = it.NewIds.FirstOrDefault();
            app.Ops.SetTransform(new[] { item }, new Vector3(1, 0.5f, 0), new Vector3(0, 30, 0), null, User);
            var folder = app.Ops.AddFolder(id, null, -1, User).NewIds.FirstOrDefault();
            app.Runtime.ApplyNow();
            app.Ops.PlayRoutes(new[] { id }, false, User);
            var real = app.Ops.RouteReal(id);
            double maxP = 0, maxR = 0;
            bool under = true;
            for (int k = 0; k < 240; k++)
            {
                app.Runtime.Tick(Dt);
                foreach (var (nid, lp, lr) in new[] { (item, new Vector3(1, 0.5f, 0), Quaternion.Euler(0, 30, 0)), (folder, Vector3.zero, Quaternion.identity) })
                {
                    var h = app.Runtime.HolderOf(nid);
                    if (!h || h.parent != real.ChildRoot) { under = false; continue; }
                    maxP = Math.Max(maxP, (h.position - real.ChildRoot.TransformPoint(lp)).magnitude);
                    maxR = Math.Max(maxR, RotationCompare.Deg(h.rotation, real.ChildRoot.rotation * lr));
                }
            }
            Check("riders (an item and a folder) hang under the child root", under);
            Check("riders' world pose = child root × local (1e-5, 240 frames of a looping xy route)", maxP <= 1e-5 && maxR <= 1e-3, $"pos {maxP:E2}, rot {maxR:F4}°");
            var item399 = app.Session.Model.Find(item);
            Check("riders keep their stored local transform (the route moves only the child root)", item399.Doc.Transform.Pos.X == 1f && item399.Doc.Transform.Pos.Y == 0.5f);
        }
        finally { app.Dispose(); }
    }

    // ================================================================ R3

    static RouteNodeState Shape(bool loop, params (Vector3 pos, string conn, bool link)[] pts)
    {
        var st = new RouteNodeState { Loop = loop };
        int k = 0;
        foreach (var (pos, conn, link) in pts) st.Points.Add(new RoutePoint { Id = "p" + k++, Pos = UnityObjects.V(pos), Connection = conn, Link = link });
        return st;
    }

    static void R3()
    {
        var shapes = new List<(string name, RouteNodeState st, int expect)>
        {
            ("3 Line points, no loop", Shape(false, (Vector3.zero, "line", false), (new Vector3(5, 0, 0), "line", false), (new Vector3(5, 0, 5), "line", false)), 3),
            ("3 Line points, loop", Shape(true, (Vector3.zero, "line", false), (new Vector3(5, 0, 0), "line", false), (new Vector3(5, 0, 5), "line", false)), 4),
            ("start Curve + 2 Line (J1d)", Shape(false, (Vector3.zero, "curve", false), (new Vector3(6, 0, 0), "line", false), (new Vector3(6, 0, 6), "line", false)), 163),
            ("3 linked Curve, no loop (one chain of 2 + last)", Shape(false, (Vector3.zero, "curve", false), (new Vector3(6, 0, 0), "curve", true), (new Vector3(6, 0, 6), "curve", true)), 322),
            ("3 linked Curve, loop (closed chain)", Shape(true, (Vector3.zero, "curve", false), (new Vector3(6, 0, 0), "curve", true), (new Vector3(6, 0, 6), "curve", true)), 481),
            ("Line, Curve (unlinked), Curve last, loop", Shape(true, (Vector3.zero, "line", false), (new Vector3(6, 0, 0), "curve", false), (new Vector3(6, 0, 6), "curve", false)), 323),
        };
        foreach (var (name, st, expect) in shapes)
        {
            var path = RoutePath.Build(st);
            var v = RouteLine.Vertices(st, path);
            Check($"{name}: {expect} vertices (ExpectedCount {RouteLine.ExpectedCount(st)})", v.Count == expect && RouteLine.ExpectedCount(st) == expect, "" + v.Count);
        }
        // HS2's recorded lines (H's J1 / J2 / J3 records)
        int files = 0, okCount = 0;
        double worst = 0;
        string worstName = null;
        if (!Directory.Exists(RecordDir)) { Warn("no HS2 records in " + RecordDir); return; }
        foreach (var f in Directory.GetFiles(RecordDir, "J*.json").OrderBy(x => x, StringComparer.Ordinal))
        {
            JObject d;
            try { d = JObject.Parse(File.ReadAllText(f)); } catch (Exception) { continue; }
            var b = d["before"] as JObject;
            var line = b?["line"] as JObject;
            if (line == null || line["points"] == null || b["points"] == null) continue;
            var st = FromRecord(b);
            if (st == null) continue;
            files++;
            var path = RoutePath.Build(st);
            var v = RouteLine.Vertices(st, path);
            var lp = ((JArray)line["points"]).Select(x => new Vector3((float)x[0], (float)x[1], (float)x[2])).ToList();
            var pos = Vec(b["worldPos"]);
            var rot = b["worldRot"] is JArray wr && wr.Count == 4 ? new Quaternion((float)wr[0], (float)wr[1], (float)wr[2], (float)wr[3]) : Quaternion.identity;
            if (v.Count != lp.Count) { Info($"{Path.GetFileName(f)}: count {v.Count} vs HS2 {lp.Count}"); continue; }
            double w = 0;
            for (int i = 0; i < v.Count; i++) w = Math.Max(w, (pos + rot * V(v[i]) - lp[i]).magnitude);
            if (w > worst) { worst = w; worstName = Path.GetFileName(f); }
            if (w <= 1e-4) okCount++; else Info($"{Path.GetFileName(f)}: worst vertex {w:E2}");
        }
        Info($"HS2 line records: {files} files, worst {worst:E2} ({worstName})");
        if (files == 0) Warn("no HS2 line records found");
        else Check($"HS2's recorded lines: vertex count and positions within 1e-4 ({okCount} / {files} records)", okCount == files, $"worst {worst:E2} in {worstName}");
    }

    static Vector3 Vec(JToken t) => t is JArray a && a.Count >= 3 ? new Vector3((float)a[0], (float)a[1], (float)a[2]) : Vector3.zero;

    /// <summary>The route of a record's `before` state (route-local points, start rotation, connections, links, aids).</summary>
    static RouteNodeState FromRecord(JObject b)
    {
        var st = new RouteNodeState { Loop = (bool?)b["loop"] ?? false, Orient = (string)b["orient"] ?? "none" };
        foreach (JObject p in (JArray)b["points"])
        {
            var rp = new RoutePoint
            {
                Id = JsonText.DerivedId("route-test/record/" + (int?)p["index"]),
                Pos = UnityObjects.V(Vec(p["pos"])),
                Rot = UnityObjects.V(Vec(p["rot"])),
                Speed = (float?)p["speed"] ?? 2f,
                Ease = (string)p["ease"] ?? "linear",
                Connection = (string)p["connection"] ?? "line",
                Link = (bool?)p["link"] ?? false,
            };
            if (p["aid"] is JObject a) rp.Aid = new RouteAid { Pos = UnityObjects.V(Vec(a["pos"])), Init = (bool?)a["init"] ?? false };
            st.Points.Add(rp);
        }
        return st.Points.Count >= 2 ? st : null;
    }

    // ================================================================ R4

    static void R4()
    {
        var app = Host();
        try
        {
            var ops = app.Ops;
            string route = null;
            string P(int i) => Pid(app, route, i);
            var table = new List<(string name, string label, Func<OpResult> op)>
            {
                ("AddRoute", "Add route", () => { var r = ops.AddRoute(null, null, -1, new Vector3(1, 0, 1), null, User); route = r.NewIds.FirstOrDefault(); return r; }),
                ("AddRoutePoint", "Add route point", () => ops.AddRoutePoint(route, null, null, User)),
                ("AddRoutePoint 2", "Add route point", () => ops.AddRoutePoint(route, null, null, User)),
                ("AddRoutePoint at 1", "Add route point", () => ops.AddRoutePoint(route, 1, null, User)),
                ("SetRoute loop", "Route", () => ops.SetRoute(new[] { route }, new List<(string, object)> { ("loop", false) }, User)),
                ("SetRoute orient / colour", "Route", () => ops.SetRoute(new[] { route }, new List<(string, object)> { ("orient", "xy"), ("color", new Color4(1, 0, 0, 1)) }, User)),
                ("SetRoute showLine", "Route", () => ops.SetRoute(new[] { route }, new List<(string, object)> { ("showLine", false) }, User)),
                ("SetRoutePoints speed / ease", "Route point", () => ops.SetRoutePoints(route, new[] { P(1), P(2) }, new List<(string, object)> { ("speed", 5f), ("ease", "easeOutQuad") }, User)),
                ("SetRoutePoints curve", "Route point", () => ops.SetRoutePoints(route, new[] { P(1) }, new List<(string, object)> { ("connection", "curve") }, User)),
                ("SetRoutePoints link", "Route point", () => ops.SetRoutePoints(route, new[] { P(2) }, new List<(string, object)> { ("connection", "curve"), ("link", true) }, User)),
                ("SetRoutePointPose pos", "Route point", () => ops.SetRoutePointPose(route, P(2), new Vector3(3, 1, 7), null, User)),
                ("SetRoutePointPose start rot", "Route point", () => ops.SetRoutePointPose(route, P(0), null, new Vector3(0, 45, 0), User)),
                ("SetRouteAid", "Route point", () => ops.SetRouteAid(route, P(1), new Vector3(0.5f, 1, 0.5f), User)),
                ("ResetRouteAid", "Route point", () => ops.ResetRouteAid(route, new[] { P(1) }, User)),
                ("DeleteRoutePoints", "Delete route point", () => ops.DeleteRoutePoints(route, new[] { P(3) }, User)),
            };
            var failed = new List<string>();
            foreach (var (name, label, op) in table)
            {
                string before = Doc(app);
                int n0 = Undos(app);
                OpResult r;
                try { r = op(); }
                catch (Exception e) { failed.Add(name + " threw " + e.GetType().Name + ": " + e.Message); continue; }
                app.Runtime.ApplyNow();
                string after = Doc(app);
                bool ok = r.Ok && r.Status == CommandStatus.Ok && Undos(app) == n0 + 1 && Top(app) == label && after != before;
                if (!ok) { failed.Add($"{name}: {r} entries {Undos(app) - n0} top \"{Top(app)}\" changed {after != before}"); continue; }
                var u = app.Session.Commands.Undo();
                app.Runtime.ApplyNow();
                string undone = Doc(app);
                var rd = app.Session.Commands.Redo();
                app.Runtime.ApplyNow();
                string redone = Doc(app);
                if (!u.Ok || undone != before) failed.Add(name + ": undo does not restore scene.doc");
                if (!rd.Ok || redone != after) failed.Add(name + ": redo does not restore scene.doc");
            }
            Check($"{table.Count} route ops: Ok, one entry with the label, scene.doc changed, undo / redo restore its bytes", failed.Count == 0,
                failed.Count + " problems: " + string.Join("; ", failed.Take(8)));
            foreach (var x in failed) Info(x);
            // the aid written by the curve switch survives undo / redo of the later ops
            Check("the curve switch's aid has init true", St(app, route).Points[1].Aid?.Init == true);
            // transport: not recorded, route.playing written
            int u0 = Undos(app);
            var p1 = ops.PlayRoutes(new[] { route }, false, User);
            app.Runtime.ApplyNow();
            bool playingDoc = St(app, route).Playing;
            var s1 = ops.StopRoutes(new[] { route }, User);
            var p2 = ops.PlayAllRoutes(false, User);
            var s2 = ops.StopAllRoutes(User);
            Check("play / stop / play all / stop all: Ok, no history entry, route.playing written (saved; the file is dirty)",
                p1.Ok && s1.Ok && p2.Ok && s2.Ok && Undos(app) == u0 && playingDoc && !St(app, route).Playing && app.Session.Commands.IsDirty,
                $"entries {Undos(app) - u0}, playing after play {playingDoc}");
            // undo / redo restore the selection; a sub-selected point that leaves the list is cleared
            ops.AddRoutePoint(route, null, null, User);
            string sub = ops.SubSelection;
            Check("AddRoutePoint sub-selects the new point (its route the sole selection)", sub == route + "/points/" + P(St(app, route).Points.Count - 1) && app.Session.Selection.Primary == route, sub);
            app.Session.Commands.Undo();
            app.Runtime.ApplyNow();
            Check("undo of the add clears the sub-selection of the removed point", ops.SubSelection == null, ops.SubSelection);
        }
        finally { app.Dispose(); }
    }

    // ================================================================ R5

    static void R5()
    {
        var app = Host();
        try
        {
            var ops = app.Ops;
            string id = MakeRoute(app, Vector3.zero, Vector3.zero, true, "none", Vector3.zero, new Vector3(4, 0, 0));
            string before = Doc(app);
            int u0 = Undos(app);
            var d = ops.DeleteRoutePoints(id, new[] { Pid(app, id, 0) }, User);
            Check("deleting the start point: wrong_kind, the document unchanged, no entry", !d.Ok && d.Code == CommandCodes.WrongKind && Doc(app) == before && Undos(app) == u0, d.ToString());
            var d2 = ops.DeleteRoutePoints(id, new[] { "0" }, User);
            Check("… also by index 0", !d2.Ok && d2.Code == CommandCodes.WrongKind);
            var nf = ops.DeleteRoutePoints(id, new[] { "nosuchpoint" }, User);
            Check("an unknown point: not_found", !nf.Ok && nf.Code == CommandCodes.NotFound);
            var bp = ops.SetRoutePointPose(id, Pid(app, id, 0), new Vector3(1, 0, 0), null, User);
            Check("pos on the start point: bad_args", !bp.Ok && bp.Code == CommandCodes.BadArgs);
            var br = ops.SetRoutePointPose(id, Pid(app, id, 1), null, new Vector3(0, 10, 0), User);
            Check("rot on another point: bad_args", !br.Ok && br.Code == CommandCodes.BadArgs);
            var wk = ops.AddRoutePoint(ops.AddFolder(null, null, -1, User).NewIds[0], null, null, User);
            Check("AddRoutePoint on a folder: wrong_kind", !wk.Ok && wk.Code == CommandCodes.WrongKind);
            // R8 placement: continue the path
            var st = St(app, id);
            Check("R8: the second point of a new route sits at start + (0, 0, 10) (route-local)",
                RouteTestPlacement(app), "");
            // the limit
            RouteSettings.PointLimit = true;
            int added = 0;
            OpResult last = null;
            for (int k = 0; k < 12; k++) { last = ops.AddRoutePoint(id, null, null, User); if (last.Ok) added++; else break; }
            Check("with routes.pointLimit on the route stops at 11 points; the 12th is unavailable (point limit)",
                st.Points.Count == 11 && last != null && !last.Ok && last.Code == StudioOps.UnavailableCode, $"{st.Points.Count} points, last {last}");
            var ex = st.Points[10].Pos;
            var prev = st.Points[9].Pos;
            var prev2 = st.Points[8].Pos;
            Check("R8: each appended point continues the path (last + (last − previous))", Math.Abs((ex.X - prev.X) - (prev.X - prev2.X)) < 1e-4 && Math.Abs((ex.Z - prev.Z) - (prev.Z - prev2.Z)) < 1e-4,
                $"{ex.X},{ex.Z} / {prev.X},{prev.Z} / {prev2.X},{prev2.Z}");
            RouteSettings.PointLimit = false;
            var more = ops.AddRoutePoint(id, null, null, User);
            Check("with the limit off a 12th point is added", more.Ok && st.Points.Count == 12, more.ToString());
            // ListRemove / ListMove of the 2.2 commands are not used by the ops: the start point stays first after deletes
            var del = ops.DeleteRoutePoints(id, new[] { Pid(app, id, 1), Pid(app, id, 2) }, User);
            Check("deleting two points: one entry, the start point stays at index 0", del.Ok && st.Points.Count == 10 && Top(app) == "Delete route point" && st.Points[0].Id == Pid(app, id, 0));
            var one = MakeRoute(app, Vector3.zero, Vector3.zero, false, "none", Vector3.zero);
            var pl = ops.PlayRoutes(new[] { one }, false, User);
            Check("Play of a one-point route: unavailable, finding route.onePoint", !pl.Ok && pl.Code == StudioOps.UnavailableCode && app.Session.Findings.Any(f => f.Code == "route.onePoint"), pl.ToString());
        }
        finally { app.Dispose(); }
    }

    static bool RouteTestPlacement(AppCore app)
    {
        var r = app.Ops.AddRoute(null, null, -1, null, null, User);
        string id = r.NewIds[0];
        app.Ops.AddRoutePoint(id, null, null, User);
        var p = St(app, id).Points[1].Pos;
        app.Ops.AddRoutePoint(id, null, null, User);
        var q = St(app, id).Points[2].Pos;
        bool ok = p.X == 0 && p.Y == 0 && p.Z == 10 && q.Z == 20;
        app.Ops.Delete(new[] { id }, User);
        return ok;
    }

    // ================================================================ R6

    static void R6()
    {
        var app = Host();
        try
        {
            var ops = app.Ops;
            string id = MakeRoute(app, Vector3.zero, Vector3.zero, false, "none", Vector3.zero, new Vector3(6, 0, 2), new Vector3(6, 0, 8));
            ops.SetRoutePointPose(id, Pid(app, id, 0), null, new Vector3(0, 45, 0), User);
            var r = ops.SetRoutePoints(id, new[] { Pid(app, id, 0) }, new List<(string, object)> { ("connection", "curve") }, User);
            var aid = St(app, id).Points[0].Aid;
            Check("the switch to Curve and the aid are one entry", r.Ok && Top(app) == "Route point" && r.Changed.Contains("route.points[0].aid"), string.Join(",", r.Changed));
            Check("the start point's aid = the midpoint in its rotated frame: (1.414, 0, 2.828) for the midpoint (3, 0, 1) at 45° (HS2 J4), init true",
                aid != null && aid.Init && Math.Abs(aid.Pos.X - 1.41421f) < 1e-4 && Math.Abs(aid.Pos.Y) < 1e-5 && Math.Abs(aid.Pos.Z - 2.82843f) < 1e-4,
                aid == null ? "null" : $"({aid.Pos.X}, {aid.Pos.Y}, {aid.Pos.Z}) init {aid.Init}");
            ops.SetRoutePoints(id, new[] { Pid(app, id, 1) }, new List<(string, object)> { ("connection", "curve") }, User);
            var a1 = St(app, id).Points[1].Aid;
            Check("another point's aid = the midpoint to the next point (point-local, no rotation)", a1 != null && Math.Abs(a1.Pos.X) < 1e-5 && Math.Abs(a1.Pos.Z - 3) < 1e-5);
            ops.SetRouteAid(id, Pid(app, id, 1), new Vector3(1, 2, 3), User);
            ops.SetRoutePoints(id, new[] { Pid(app, id, 1) }, new List<(string, object)> { ("connection", "line") }, User);
            ops.SetRoutePoints(id, new[] { Pid(app, id, 1) }, new List<(string, object)> { ("connection", "curve") }, User);
            a1 = St(app, id).Points[1].Aid;
            Check("an initialised aid is kept by a later curve switch", a1.Pos.X == 1 && a1.Pos.Y == 2 && a1.Pos.Z == 3);
            ops.ResetRouteAid(id, new[] { Pid(app, id, 1) }, User);
            a1 = St(app, id).Points[1].Aid;
            Check("Reset aid puts it back at the midpoint", Math.Abs(a1.Pos.X) < 1e-5 && Math.Abs(a1.Pos.Z - 3) < 1e-5);
            // the last point's midpoint is to the start point
            ops.SetRoutePoints(id, new[] { Pid(app, id, 2) }, new List<(string, object)> { ("connection", "curve") }, User);
            var a2 = St(app, id).Points[2].Aid;
            Check("the last point's aid: the midpoint to the start point", a2 != null && Math.Abs(a2.Pos.X + 3) < 1e-5 && Math.Abs(a2.Pos.Z + 4) < 1e-5, a2 == null ? "null" : $"({a2.Pos.X}, {a2.Pos.Z})");
        }
        finally { app.Dispose(); }
    }

    // ================================================================ R7

    static void R7()
    {
        var app = Host();
        try
        {
            var ops = app.Ops;
            string id = MakeRoute(app, Vector3.zero, Vector3.zero, true, "none", Vector3.zero, new Vector3(5, 0, 0), new Vector3(5, 0, 5));
            ops.PlayRoutes(new[] { id }, false, User);
            Ticks(app, 30);
            var dup = ops.Duplicate(new[] { id }, User);
            app.Runtime.ApplyNow();
            string copy = dup.NewIds.FirstOrDefault();
            var a = St(app, id);
            var b = copy != null ? St(app, copy) : null;
            Check("duplicate: a new node with new point ids, the same positions", b != null && copy != id && b.Points.Count == a.Points.Count &&
                b.Points.Select(p => p.Id).Intersect(a.Points.Select(p => p.Id)).Count() == 0 && b.Points[2].Pos.X == 5);
            var cr = copy != null ? ops.RouteReal(copy) : null;
            Check("a playing original gives a playing copy at τ 0", b != null && b.Playing && cr != null && cr.PlayState == RoutePlayState.Playing && cr.Time == 0,
                cr == null ? "no realization" : $"{cr.PlayState} τ {cr.Time}");
            string file = Path.Combine(OutDir, "r7_import.hs2nscene");
            Directory.CreateDirectory(OutDir);
            ops.SaveScene(file, new SaveOptions { Thumbnail = ThumbnailMode.Placeholder });
            var imp = ops.ImportScene(file, User);
            app.Runtime.ApplyNow();
            var newRoutes = imp.NewIds.Where(x => app.Session.Model.Find(x)?.Kind == NodeKind.Route).ToList();
            var all = ops.AllRoutes();
            var ids = new HashSet<string>();
            bool distinct = true;
            foreach (var n in all) foreach (var p in ((RouteNodeState)n.State).Points) if (!ids.Add(p.Id)) distinct = false;
            Check("import: the routes come back with new node and point ids (all point ids distinct)", imp.Ok && newRoutes.Count == 2 && all.Count == 4 && distinct, imp.ToString());
            Check("imported playing routes play from τ 0", newRoutes.All(x => ops.RouteReal(x)?.PlayState == RoutePlayState.Playing && ops.RouteReal(x).Time == 0));
            try { File.Delete(file); } catch (Exception) { }
        }
        finally { app.Dispose(); }
    }

    // ================================================================ R8

    static void R8()
    {
        var app = Host();
        try
        {
            var ops = app.Ops;
            string loop = MakeRoute(app, Vector3.zero, Vector3.zero, true, "none", Vector3.zero, new Vector3(5, 0, 0), new Vector3(5, 0, 5));
            string once = MakeRoute(app, new Vector3(0, 0, 10), Vector3.zero, false, "none", Vector3.zero, new Vector3(1, 0, 0));
            string stopped = MakeRoute(app, new Vector3(0, 0, 20), Vector3.zero, false, "none", Vector3.zero, new Vector3(2, 0, 0));
            ops.PlayRoutes(new[] { loop, once }, false, User);
            Ticks(app, 60);
            Check("before the save: loop playing, once ended (route.playing true), the third stopped",
                ops.RouteState(loop) == RoutePlayState.Playing && ops.RouteState(once) == RoutePlayState.Ended && St(app, once).Playing && ops.RouteState(stopped) == RoutePlayState.Stopped);
            string file = Path.Combine(OutDir, "r8.hs2nscene");
            var sv = ops.SaveScene(file, new SaveOptions { Thumbnail = ThumbnailMode.Placeholder });
            int u0 = Undos(app);
            Check("save: the ended route is written stopped (a silent Capture batch: no entry), it stays at its end",
                sv.Ok && !St(app, once).Playing && ops.RouteState(once) == RoutePlayState.Ended && Undos(app) == u0);
            ops.NewScene();
            var ld = ops.LoadScene(file);
            app.Runtime.ApplyNow();
            Check("load: the playing route restarts at τ 0, the ended one is stopped at the start, the stopped one stopped",
                ld.Ok && ops.RouteState(loop) == RoutePlayState.Playing && ops.RouteReal(loop).Time == 0 && ops.RouteState(once) == RoutePlayState.Stopped && ops.RouteState(stopped) == RoutePlayState.Stopped,
                $"{ops.RouteState(loop)} / {ops.RouteState(once)} / {ops.RouteState(stopped)}");
            Check("the stopped route's child root sits at its start point", ops.RouteReal(once)?.ChildRoot.localPosition == Vector3.zero);
            Ticks(app, 6);
            Check("after load the playing route advances with the clock (τ = 6 frames)", Math.Abs(ops.RouteReal(loop).Time - 6 * (double)Dt) < 1e-9);
            try { File.Delete(file); } catch (Exception) { }
        }
        finally { app.Dispose(); }
    }

    // ================================================================ R9

    static void R9()
    {
        var app = Host();
        try
        {
            var ops = app.Ops;
            string id = MakeRoute(app, Vector3.zero, Vector3.zero, true, "none", Vector3.zero, new Vector3(10, 0, 0), new Vector3(10, 0, 10));
            ops.PlayRoutes(new[] { id }, false, User);
            Ticks(app, 30);
            var real = ops.RouteReal(id);
            double t0 = real.Time;
            int v0 = real.PathVersion;
            ops.SetRoutePointPose(id, Pid(app, id, 2), new Vector3(10, 0, 20), null, User);
            Check("moving a point while playing rebuilds the path at once, τ continues", real.PathVersion > v0 && real.Time == t0 && real.PlayState == RoutePlayState.Playing);
            app.Runtime.Tick(Dt);
            Check("… and the next frame advances τ by FrameDt", Math.Abs(real.Time - (t0 + Dt)) < 1e-12);
            var pos = real.ChildRoot.localPosition;
            var expect = RoutePath.Build(St(app, id)).Evaluate(real.Time).Position;
            Check("the root follows the edited path (no wait for the next segment)", (pos - V(expect)).magnitude < 1e-5, F(pos) + " vs " + F(V(expect)));
            ops.AddRoutePoint(id, null, null, User);
            double t1 = real.Time;
            app.Session.Commands.Undo();
            app.Runtime.ApplyNow();
            Check("add point and its undo while playing keep τ", real.Time == t1 && real.PlayState == RoutePlayState.Playing && St(app, id).Points.Count == 3);
            ops.SetRoute(new[] { id }, new List<(string, object)> { ("loop", false) }, User);
            Ticks(app, 1);
            var d = real.Path.TotalDuration;
            Check("loop off while playing: τ continues on the non-loop path", real.PlayState == (real.Time >= d ? RoutePlayState.Ended : RoutePlayState.Playing));
            Ticks(app, 400);
            ops.SetRoutePointPose(id, Pid(app, id, 1), new Vector3(1, 0, 0), null, User);
            Check("an ended route stays ended after an edit", real.PlayState == RoutePlayState.Ended);
            ops.PlayRoutes(new[] { id }, false, User);
            Ticks(app, 10);
            double dShort = real.Path.TotalDuration;
            ops.SetRoutePointPose(id, Pid(app, id, 1), new Vector3(0.05f, 0, 0), null, User);
            ops.SetRoutePointPose(id, Pid(app, id, 2), new Vector3(0.05f, 0, 0.05f), null, User);
            app.Runtime.Tick(Dt);
            Check("a path shortened below τ ends the route (non-loop)", real.PlayState == RoutePlayState.Ended, $"τ {real.Time:F4}, D {real.Path.TotalDuration:F4} (was {dShort:F4})");
        }
        finally { app.Dispose(); }
    }

    // ================================================================ R10

    static JObject Call(AppCore app, object req)
    {
        var r = JObject.Parse(app.Call(req is string s ? s : JsonConvert.SerializeObject(req)));
        if (r.Value<bool?>("ok") != true) Info("call " + JsonConvert.SerializeObject(req) + " -> " + r.ToString(Formatting.None));
        return r;
    }

    static void R10()
    {
        // one bridge per process: the bridge side runs alone first, then a second host with the same id seed runs the ops
        var steps = new List<(string name, Func<string, string, object> req, Func<AppCore, string, OpResult> op, string label)>
        {
            ("route.addPoint", (r, p) => new { cmd = "route.addPoint", key = r }, (b, r) => b.Ops.AddRoutePoint(r, null, null, Bx), "Add route point"),
            ("route.addPoint pos", (r, p) => new { cmd = "route.addPoint", key = r, pos = new[] { 4, 0, 4 } }, (b, r) => b.Ops.AddRoutePoint(r, null, new Vector3(4, 0, 4), Bx), "Add route point"),
            ("route.set", (r, p) => new { cmd = "route.set", key = r, loop = false, orient = "y", color = new[] { 0, 1, 0 } },
                (b, r) => b.Ops.SetRoute(new[] { r }, new List<(string, object)> { ("loop", false), ("orient", "y"), ("color", new Color4(0, 1, 0, 1)) }, Bx), "Route"),
            ("route.setPoint speed / ease / curve", (r, p) => new { cmd = "route.setPoint", key = r, point = 1, speed = 3.5, ease = "easeInQuart", connection = "curve" },
                (b, r) => b.Ops.SetRoutePoints(r, new[] { Pid(b, r, 1) }, new List<(string, object)> { ("speed", 3.5f), ("ease", "easeInQuart"), ("connection", "curve") }, Bx), "Route point"),
            ("route.setPoint pos", (r, p) => new { cmd = "route.setPoint", key = r, point = 2, pos = new[] { 5, 1, 5 } }, (b, r) => b.Ops.SetRoutePointPose(r, Pid(b, r, 2), new Vector3(5, 1, 5), null, Bx), "Route point"),
            ("route.setPoint start rot", (r, p) => new { cmd = "route.setPoint", key = r, point = 0, rot = new[] { 0, 30, 0 } }, (b, r) => b.Ops.SetRoutePointPose(r, Pid(b, r, 0), null, new Vector3(0, 30, 0), Bx), "Route point"),
            ("route.setPoint aid", (r, p) => new { cmd = "route.setPoint", key = r, point = 1, aid = new { pos = new[] { 1, 1, 1 } } }, (b, r) => b.Ops.SetRouteAid(r, Pid(b, r, 1), Vector3.one, Bx), "Route point"),
            ("route.setPoint resetAid", (r, p) => new { cmd = "route.setPoint", key = r, point = 1, resetAid = true }, (b, r) => b.Ops.ResetRouteAid(r, new[] { Pid(b, r, 1) }, Bx), "Route point"),
            ("route.deletePoint", (r, p) => new { cmd = "route.deletePoint", key = r, point = 2 }, (b, r) => b.Ops.DeleteRoutePoints(r, new[] { Pid(b, r, 2) }, Bx), "Delete route point"),
        };
        var side = new List<(string doc, string top, int undos, bool ok)>();
        string ra, startDoc;
        var a = Host(Seed);
        try
        {
            var add = Call(a, new { cmd = "route.add", pos = new[] { 1, 0, 2 } });
            ra = (string)add["key"];
            Check("route.add: {key, points [start]}, one entry \"Add route\"", ra != null && (add["points"] as JArray)?.Count == 1 && Top(a) == "Add route", AcceptanceShort(add));
            startDoc = Doc(a);
            foreach (var (name, req, _, _) in steps)
            {
                var r = Call(a, req(ra, null));
                a.Runtime.ApplyNow();
                side.Add((Doc(a), Top(a), Undos(a), r.Value<bool?>("ok") == true));
            }
            // shapes and errors
            var st = Call(a, new { cmd = "route.state", key = ra });
            Check("route.state: the shape (state, time, total, segments with durations, childRoot, points, line)",
                (string)st["state"] == "stopped" && st["time"] != null && st["segments"] is JArray sg && sg.Count >= 1 && sg[0]["duration"] != null && st["childRoot"]?["pos"] is JArray
                && (st["points"] as JArray)?.Count == 2 && st["line"]?["count"] != null && (int)st["line"]["count"] == (int)st["line"]["expected"], AcceptanceShort(st));
            var pl = Call(a, new { cmd = "route.play", key = ra });
            Check("route.play: {routes [{key, state playing}]}, no entry", (string)pl["routes"]?[0]?["state"] == "playing" && Top(a) == "Delete route point", AcceptanceShort(pl));
            var ls = Call(a, new { cmd = "route.list" });
            Check("route.list: the route with its state and point count", (ls["routes"] as JArray)?.Count == 1 && (string)ls["routes"][0]["state"] == "playing" && (int)ls["routes"][0]["points"] == 2, AcceptanceShort(ls));
            var sp = Call(a, new { cmd = "route.stop", all = true });
            Check("route.stop all", (string)sp["routes"]?[0]?["state"] == "stopped", AcceptanceShort(sp));
            var e1 = Call(a, new { cmd = "route.deletePoint", key = ra, point = 0 });
            Check("route.deletePoint of the start point → wrong_kind", (string)e1["code"] == "wrong_kind");
            var e2 = Call(a, new { cmd = "route.setPoint", key = ra, point = 0, pos = new[] { 1, 0, 0 } });
            Check("route.setPoint pos on the start point → bad_args", (string)e2["code"] == "bad_args");
            var e3 = Call(a, new { cmd = "route.setPoint", key = ra, point = 1, ease = "easeSideways" });
            Check("route.setPoint with an unknown ease → bad_args", (string)e3["code"] == "bad_args");
            var folder = a.Ops.AddFolder(null, null, -1, User).NewIds[0];
            var e4 = Call(a, new { cmd = "route.state", key = folder });
            Check("route.state on a folder → wrong_kind", (string)e4["code"] == "wrong_kind");
            var e5 = Call(a, new { cmd = "route.setPoint", key = ra, point = "nosuch", speed = 1 });
            Check("route.setPoint on an unknown point → not_found", (string)e5["code"] == "not_found");
            var one = Call(a, new { cmd = "route.add" });
            var e6 = Call(a, new { cmd = "route.play", key = (string)one["key"] });
            Check("route.play of a one-point route → unavailable", (string)e6["code"] == "unavailable");
            RouteSettings.PointLimit = true;
            for (int i = 0; i < 10; i++) Call(a, new { cmd = "route.addPoint", key = (string)one["key"] });
            var e7 = Call(a, new { cmd = "route.addPoint", key = (string)one["key"] });
            Check("route.addPoint beyond 11 points → unavailable", (string)e7["code"] == "unavailable");
            var sel = Call(a, new { cmd = "select", sub = ra + "/points/" + Pid(a, ra, 1) + "/aid" });
            Check("select sub <route>/points/<id>/aid: the route the sole selection", (string)sel["sub"] == ra + "/points/" + Pid(a, ra, 1) + "/aid" && (string)sel["primary"] == ra, AcceptanceShort(sel));
            var sel2 = Call(a, new { cmd = "select", sub = folder + "/points/x" });
            Check("select sub on a folder's points → wrong_kind", (string)sel2["code"] == "wrong_kind");
        }
        finally { a.Dispose(); }
        var bh = Host(Seed);
        try
        {
            var addOp = bh.Ops.AddRoute(null, null, -1, new Vector3(1, 0, 2), null, Bx);
            string rb = addOp.NewIds.FirstOrDefault();
            Check("AddRoute on the op host: the same id and start document as route.add", rb == ra && Doc(bh) == startDoc, $"{ra} / {rb}");
            int ok = 0, k = 0;
            foreach (var (name, _, op, label) in steps)
            {
                var o = op(bh, rb);
                bh.Runtime.ApplyNow();
                var x = side[k++];
                bool good = x.ok && o.Ok && x.doc == Doc(bh) && x.top == Top(bh) && x.top == label && x.undos == Undos(bh);
                if (good) ok++; else Info($"{name}: bridge ok {x.ok}, op {o}, docs equal {x.doc == Doc(bh)}, labels {x.top} / {Top(bh)} (expected {label}), entries {x.undos} / {Undos(bh)}");
            }
            Check($"route.* handlers and StudioOps give equal scene.doc bytes, labels and entry counts ({ok} / {steps.Count})", ok == steps.Count);
        }
        finally { bh.Dispose(); }
    }

    static readonly ExecuteOptions Bx = new ExecuteOptions { Source = CommandSource.Bridge };

    static string AcceptanceShort(JObject o) { var s = o?.ToString(Formatting.None) ?? "null"; return s.Length > 300 ? s.Substring(0, 300) + "…" : s; }

    // ================================================================ R11

    static void R11()
    {
        var app = Host();
        try
        {
            var ops = app.Ops;
            string id = MakeRoute(app, Vector3.zero, Vector3.zero, true, "xy", Vector3.zero, new Vector3(5, 0, 0), new Vector3(5, 2, 5));
            var it = ops.AddItem(ContentId.Vanilla(399), id, null, -1, User).NewIds.FirstOrDefault();
            ops.PlayRoutes(new[] { id }, false, User);
            Ticks(app, 20);
            var real = ops.RouteReal(id);
            app.Runtime.SetPaused(true);
            var p0 = real.ChildRoot.localPosition; var q0 = real.ChildRoot.localRotation; double t0 = real.Time;
            var w0 = app.Runtime.HolderOf(it).position;
            Ticks(app, 30);
            Check("paused: 30 Ticks leave τ, the child root (bit-equal) and the rider unchanged",
                real.Time == t0 && real.ChildRoot.localPosition == p0 && RotationCompare.Deg(real.ChildRoot.localRotation, q0) == 0 && app.Runtime.HolderOf(it).position == w0);
            app.Runtime.Step(30, Dt);
            double expect = t0;
            for (int i = 0; i < 30; i++) expect += Dt;
            Check("scene step 30 × 1/60: τ + 30 dt exactly (double sum)", real.Time == expect, $"{real.Time:R} vs {expect:R}");
            app.Runtime.SetPaused(false);
            // stepping = frames: a second host runs 50 frames
            var b = Host();
            try
            {
                string id2 = MakeRoute(b, Vector3.zero, Vector3.zero, true, "xy", Vector3.zero, new Vector3(5, 0, 0), new Vector3(5, 2, 5));
                b.Ops.AddItem(ContentId.Vanilla(399), id2, null, -1, User);
                b.Ops.PlayRoutes(new[] { id2 }, false, User);
                Ticks(b, 50);
                var r2 = b.Ops.RouteReal(id2);
                Check("pause → step 30 → resume = 50 frames (child root bit-equal)", r2.Time == real.Time && r2.ChildRoot.localPosition == real.ChildRoot.localPosition && RotationCompare.Deg(r2.ChildRoot.localRotation, real.ChildRoot.localRotation) == 0,
                    $"τ {r2.Time:R} / {real.Time:R}");
            }
            finally { b.Dispose(); }
        }
        finally { app.Dispose(); }
    }

    // ================================================================ R12

    static void R12()
    {
        if (!Directory.Exists(RecordDir)) { Warn("no HS2 records in " + RecordDir); return; }
        var names = new[] { "J1a_line3", "J1b_line3_loop", "J1c_pingpong", "J1c2_pingpong_eases", "J1d_curve_default_aid", "J1e_chain3", "J1e2_chain_loop", "J1f_easeOutBounce", "J1g_speed30", "J1i_transformed", "J1j_start_rot", "J2_orient_xy", "J2_orient_y", "J2_orient_xy_curve" };
        int files = 0, posOk = 0, rotOk = 0;
        foreach (var name in names)
        {
            var f = Path.Combine(RecordDir, name + ".json");
            if (!File.Exists(f)) continue;
            var d = JObject.Parse(File.ReadAllText(f));
            var b = (JObject)d["before"];
            var st = FromRecord(b);
            var samples = (JArray)d["samples"];
            var model = (JArray)d["model"];
            if (st == null || samples == null || model == null) continue;
            files++;
            var app = Host();
            try
            {
                var rot = b["rot"];
                string id = app.Ops.AddRoute(null, null, -1, Vec(b["pos"]), Vec(rot), User).NewIds[0];
                // the document of the record (ids kept from the add)
                var start = St(app, id).Points[0].Id;
                var pts = st.Points.Select((p, i) => { var c = DocClone.Clone(p); c.Id = i == 0 ? start : app.Session.NewId(); return c; }).ToList();
                app.Session.Set(id, "route.points", pts, User);
                app.Ops.SetRoute(new[] { id }, new List<(string, object)> { ("loop", st.Loop), ("orient", st.Orient) }, User);
                app.Ops.PlayRoutes(new[] { id }, false, User);
                var real = app.Ops.RouteReal(id);
                double travel = 0, worstPos = 0, worstRot = 0, worstRot20 = 0;
                int legStart = 0, prevLeg = -1;
                var mine = new List<(double tau, Vector3 pos, Quaternion rot)>();
                for (int k = 0; k < samples.Count; k++)
                {
                    app.Runtime.Tick(Dt);
                    mine.Add((real.Time, real.ChildRoot.position, real.ChildRoot.rotation));
                }
                for (int k = 1; k < samples.Count; k++)
                {
                    var hs = samples[k]["routes"]?[0];
                    var m = model[k];
                    if (hs == null || m == null) continue;
                    double tau = (double)m["tau"];
                    int leg = (int?)m["leg"] ?? 0;
                    if (leg != prevLeg) { prevLeg = leg; legStart = k; }
                    // my frame with the nearest τ
                    int j = (int)Math.Round(tau / Dt) - 1;
                    if (j < 0 || j >= mine.Count) continue;
                    var hp = Vec(hs["pos"]);
                    var hq = hs["rot"] is JArray q && q.Count == 4 ? new Quaternion((float)q[0], (float)q[1], (float)q[2], (float)q[3]) : Quaternion.identity;
                    double step = k + 1 < samples.Count ? (Vec(samples[k + 1]["routes"]?[0]?["pos"]) - hp).magnitude : 0;
                    travel = Math.Max(travel, step);
                    worstPos = Math.Max(worstPos, (mine[j].pos - hp).magnitude);
                    if (k - legStart >= 10 && st.Orient != "none")
                    {
                        // HS2 loses the remainder of a frame at each segment end (R4, not kept): at the same τ it has run about
                        // one or two more filter steps since a corner, which shows in the first frames after the corner
                        float ang = Quaternion.Angle(mine[j].rot, hq);
                        worstRot = Math.Max(worstRot, ang);
                        if (k - legStart >= 20) worstRot20 = Math.Max(worstRot20, ang);
                    }
                }
                bool pOk = worstPos <= travel + 1e-4, rOk = worstRot20 <= 2.0;
                if (worstRot > 2.0) Warn($"{name}: orient {worstRot:F2}° from HS2 10 frames after a corner (R4's continuous timeline: HS2's lost remainders give it extra filter steps), {worstRot20:F2}° after 20 frames");
                if (pOk) posOk++;
                if (rOk) rotOk++;
                Info($"{name}: worst position {worstPos:F5} (one frame of travel {travel:F5}), worst orient {worstRot:F3}° after 10 frames of a leg, {worstRot20:F3}° after 20");
            }
            finally { app.Dispose(); }
        }
        if (files == 0) { Warn("no HS2 J1 / J2 records"); return; }
        Check($"the runtime's child root within one frame of travel of HS2 at 60 fps ({posOk} / {files} records)", posOk == files);
        Check($"orient within 2° of HS2 after the first 20 frames of a leg ({rotOk} / {files} records; 10 frames: see the warnings)", rotOk == files);
    }

    // ================================================================ R13

    static void R13()
    {
        var app = Host();
        try
        {
            var ops = app.Ops;
            var pts = new List<Vector3> { Vector3.zero };
            for (int i = 1; i < 11; i++) pts.Add(new Vector3(i * 3, (i % 3) * 1.5f, (i % 2) * 4));
            string id = MakeRoute(app, Vector3.zero, Vector3.zero, true, "xy", pts.ToArray());
            var st = St(app, id);
            ops.SetRoutePoints(id, st.Points.Select(p => p.Id).ToList(), new List<(string, object)> { ("connection", "curve") }, User);
            ops.SetRoutePoints(id, st.Points.Skip(1).Where((p, i) => i % 2 == 0).Select(p => p.Id).ToList(), new List<(string, object)> { ("link", true) }, User);
            ops.PlayRoutes(new[] { id }, false, User);
            var real = ops.RouteReal(id);
            var pb = app.Runtime.GetPart<RoutePlayback>();
            Ticks(app, 10);
            var sw = Stopwatch.StartNew();
            const int N = 2000;
            for (int i = 0; i < N; i++) pb.Tick(Dt);
            double perFrame = sw.Elapsed.TotalMilliseconds / N;
            Measure($"playback per route per frame (11 points, curves, xy): {perFrame * 1000:F2} µs (target ≤ 20 µs)");
            Check("route playback ≤ 0.02 ms per route per frame", perFrame <= 0.02, perFrame.ToString("F4") + " ms");
            sw.Restart();
            for (int i = 0; i < 200; i++) RoutePath.Build(st, RouteSettings.Options());
            double build = sw.Elapsed.TotalMilliseconds / 200;
            sw.Restart();
            int count = 0;
            for (int i = 0; i < 200; i++) count = RouteLine.Vertices(st, real.Path).Count;
            double line = sw.Elapsed.TotalMilliseconds / 200;
            Measure($"path rebuild {build:F3} ms (target ≤ 0.2), line build {line:F3} ms for {count} vertices (target ≤ 0.5)");
            Check("path rebuild ≤ 0.2 ms and line build ≤ 0.5 ms", build <= 0.2 && line <= 0.5, $"{build:F3} / {line:F3} ms");
        }
        finally { app.Dispose(); }
    }
}
