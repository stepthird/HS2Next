// Bridge tests in the Unity Editor (batch, edit mode, no player loop):
// the assembly HS2Next.Bridge.Tests in-process. Runs the Unity-free groups of BridgeTestGroups (the same groups as
// Tools/mono_tests.ps1 -Suite bridge) with Unity's Mono class library, then the Unity layer: the UnityEngine
// vector / quaternion / colour JSON forms, BridgeHost in edit mode (no pump object, manual frames, in-process only,
// a taken port), Unity's log
// (main and worker threads) in log.tail over HTTP, endpoint.json, stop and port release.
//   Run:     Tools/unity_batch.ps1 -Method HS2Next.Bridge.Tests.BridgeTest.Run -NoGraphics [-MethodArgs @('-groups','Batch,Help')]
//   Report:  Spike/out/bridge/bridge_report.txt, first line "N passed, M failed"; exit 1 when a check fails (inside a
//            HeadlessBatch the member ends with that code and the batch goes on; alone, Unity exits with it).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using HS2Next.Api.Bridge;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HS2Next.Bridge.Tests
{
    public static class BridgeTest
    {
        public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string groupsArg = null;
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-groups") groupsArg = args[i + 1];
            var filter = groupsArg?.Split(',').Select(g => g.Trim()).Where(g => g.Length > 0).ToList();

            var t = new BridgeChecks
            {
                TempDir = Path.Combine(Path.GetTempPath(), "hs2n_bridge_unity_" + Process.GetCurrentProcess().Id),
                Echo = line => Debug.Log("BridgeTest " + line.Trim()),
            };
            Directory.CreateDirectory(t.TempDir);
            UnityJsonConverters.Install();
            var timings = new List<string>();
            var total = Stopwatch.StartNew();
            var groups = BridgeTestGroups.All();
            groups.Add(new KeyValuePair<string, Action<BridgeChecks>>("Unity", UnityGroup.Run));
            foreach (var g in groups)
            {
                if (filter != null && !filter.Any(f => g.Key.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                t.Group = g.Key;
                int before = t.Failed, passedBefore = t.Passed;
                var sw = Stopwatch.StartNew();
                try { g.Value(t); }
                catch (Exception e) { t.Check(false, "group threw " + e); }
                timings.Add(g.Key + ": " + (t.Failed == before ? "ok" : (t.Failed - before) + " failed") + " (" + (t.Passed - passedBefore) + " passed, " + sw.ElapsedMilliseconds + " ms)");
            }
            try { Directory.Delete(t.TempDir, true); } catch (Exception) { }

            var sb = new StringBuilder();
            sb.AppendLine(t.Passed + " passed, " + t.Failed + " failed");
            sb.AppendLine("HS2Next bridge tests (Unity " + Application.unityVersion + ", edit mode, " + total.ElapsedMilliseconds + " ms)");
            sb.AppendLine();
            foreach (var l in timings) sb.AppendLine(l);
            sb.AppendLine();
            foreach (var l in t.Lines) sb.AppendLine(l);
            string report = Path.Combine(ProjectRoot, "Spike", "out", "bridge", "bridge_report.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(report));
            File.WriteAllText(report, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("BridgeTest: " + t.Passed + " passed, " + t.Failed + " failed -> " + report);
            if (t.Failed > 0 && Application.isBatchMode) Exit(1);
        }

        // The test end of the Editor assembly (TestExit: inside a HeadlessBatch it ends the member only, outside it ends Unity),
        // found by name because this assembly cannot reference the Editor assembly; without it, Unity ends with the code.
        static void Exit(int code)
        {
            var exit = Type.GetType("TestExit, Assembly-CSharp-Editor", false)
                ?.GetMethod("Exit", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null);
            if (exit == null) { EditorApplication.Exit(code); return; }
            try { exit.Invoke(null, new object[] { code }); }
            catch (TargetInvocationException e) when (e.InnerException != null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); }
        }
    }

    public sealed class UnityDto
    {
        public Vector3 Pos = new Vector3(0.1f, -2f, 3.5f);
        public Quaternion Rot = new Quaternion(0f, 0.70710677f, 0f, 0.70710677f);
        public Color Col = new Color(1f, 0.5f, 0.25f, 0.75f);
        public Color32 Col32 = new Color32(1, 2, 3, 255);
        public Vector2 V2 = new Vector2(1, 2);
        public Vector4 V4 = new Vector4(1, 2, 3, 4);
        public Vector2Int I2 = new Vector2Int(5, 6);
        public Vector3Int I3 = new Vector3Int(7, 8, 9);
    }

    /// <summary>The Unity layer (converters, BridgeHost in edit mode, Unity's log).</summary>
    public static class UnityGroup
    {
        public static void Run(BridgeChecks t)
        {
            using (var s = Kit.NewServer(start: false))
            {
                Vector3 pos = default;
                Quaternion rot = default;
                Color col3 = default, col4 = default;
                Exception err = null;
                s.Commands.Register(new CommandSpec("u.args") { AllowExtraArgs = true, Gate = ReadyGate.None }, ctx =>
                {
                    pos = ctx.Args.Get<Vector3>("pos");
                    rot = ctx.Args.Get<Quaternion>("rot");
                    col3 = ctx.Args.Get<Color>("col3");
                    col4 = ctx.Args.Get<Color>("col4");
                    err = null;
                    try { ctx.Args.Get<Vector3>("bad"); } catch (Exception e) { err = e; }
                    return null;
                });
                s.Commands.Register(new CommandSpec("u.dto") { Gate = ReadyGate.None }, ctx => new UnityDto());
                var r = Kit.Exec(s, "{\"cmd\":\"u.args\",\"pos\":[1,2.5,-3],\"rot\":[0,0,0,1],\"col3\":[1,0,0.5],\"col4\":[0.1,0.2,0.3,0.4],\"bad\":[1,2]}");
                t.Check(Kit.Ok(r), "Unity argument conversions ran");
                t.Check(pos == new Vector3(1, 2.5f, -3), "Get<Vector3> from [x,y,z]");
                t.Check(rot == Quaternion.identity, "Get<Quaternion> from [x,y,z,w]");
                t.Check(col3 == new Color(1, 0, 0.5f, 1), "Get<Color> from [r,g,b] (a = 1)");
                t.Check(col4 == new Color(0.1f, 0.2f, 0.3f, 0.4f), "Get<Color> from [r,g,b,a]");
                t.Check(err is BridgeException be && be.Code == BridgeCodes.BadArgs && be.Error.Detail?.Arg == "bad", "Vector3 from two numbers = bad_args");
                var text = s.Execute("{\"cmd\":\"u.dto\"}");
                t.Check(text != null && text.Contains("\"pos\":[0.1,-2.0,3.5]"), "Vector3 written as [x,y,z] in round-trip form: " + text);
                var dto = text != null ? JObject.Parse(text) : null;
                var q = dto?["rot"] as JArray;
                t.Check(q != null && q.Count == 4 && (float)(double)q[1] == 0.70710677f && (float)(double)q[3] == 0.70710677f && (double)q[0] == 0,
                    "Quaternion written as [x,y,z,w], float bits round trip: " + q?.ToString(Newtonsoft.Json.Formatting.None));
                t.Info("float text of 0.70710677f: " + Newtonsoft.Json.JsonConvert.ToString(0.70710677f) + ", of 0.1f: " + Newtonsoft.Json.JsonConvert.ToString(0.1f) +
                       ", of 1/3f: " + Newtonsoft.Json.JsonConvert.ToString(1f / 3f));
                t.Check(text != null && text.Contains("\"col\":[1.0,0.5,0.25,0.75]"), "Color written as [r,g,b,a]");
                t.Check(text != null && text.Contains("\"col32\":[1,2,3,255]") && text.Contains("\"i3\":[7,8,9]") && text.Contains("\"v4\":[1.0,2.0,3.0,4.0]"), "Color32, Vector3Int, Vector4");
                t.Equal("vec3", ArgTypes.Infer(typeof(Vector3)), "ArgTypes.Infer(Vector3) = vec3");
                t.Equal("quat", ArgTypes.Infer(typeof(Quaternion)), "ArgTypes.Infer(Quaternion) = quat");
                t.Equal("color", ArgTypes.Infer(typeof(Color)), "ArgTypes.Infer(Color) = color");
            }

            // BridgeHost in edit mode (manual pump)
            string root = Path.Combine(t.TempDir, "ud_runtime");
            string bridgeDir = Path.Combine(root, "Bridge");
            var host = BridgeHost.Start(new BridgeHostOptions { Port = 0, BridgeDir = bridgeDir, RequiredStages = new string[0], OptionalStages = new string[0] });
            try
            {
                var srv = host.Server;
                t.Check(host.Listening && BridgeHost.Current == host && host.Url != null && host.BindError == null, "BridgeHost.Start listens in edit mode (" + host.Url + ")");
                t.Check(host.ManualPump, "outside play mode the pump is manual");
                t.Check(GameObject.Find("HS2Next Bridge") == null, "no pump object outside play mode");
                t.Check(File.Exists(Path.Combine(bridgeDir, "endpoint.json")), "endpoint.json in BridgeDir");
                t.Equal(Path.GetFullPath(bridgeDir), host.Services.Output.Root, "OutputRoot defaults to BridgeDir");
                t.Equal(Application.unityVersion, srv.Options.UnityVersion, "UnityVersion filled from Application");
                string marker = "bridge-log-probe-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                Debug.Log(marker + " main");
                var th = new Thread(() => Debug.LogWarning(marker + " worker"));
                th.Start();
                th.Join();
                Debug.LogError(marker + " error");
                var lt = Http.Call(srv.Port, "{\"cmd\":\"log.tail\",\"n\":200}");
                var msgs = new List<string>();
                if (lt?["entries"] is JArray arr) foreach (var e in arr) msgs.Add((string)e["type"] + ":" + (string)e["message"]);
                t.Check(msgs.Contains("log:" + marker + " main"), "Debug.Log from the main thread in log.tail");
                t.Check(msgs.Contains("warning:" + marker + " worker"), "Debug.LogWarning from a worker thread in log.tail");
                var le = Http.Call(srv.Port, "{\"cmd\":\"log.tail\",\"level\":\"error\",\"n\":5}");
                t.Check(le != null && le.ToString().Contains(marker + " error"), "level error filter over HTTP");
                var ping = Http.Call(srv.Port, "{\"cmd\":\"ping\"}");
                t.Check(Kit.Ok(ping) && ((string)ping["screen"])?.Contains("x") == true, "ping over HTTP with the screen snapshot (" + ping?["screen"] + ")");
                // a main-thread command, pumped by hand on the main thread
                host.Services.Commands.Register(new CommandSpec("u.main") { Gate = ReadyGate.None }, ctx => new BridgeReply().With("main", Thread.CurrentThread.ManagedThreadId).With("frame", ctx.Frames.Frame));
                var task = host.Execute("{\"cmd\":\"u.main\"}");
                int f = host.ManualFrame();
                t.Check(task.IsCompleted && (int)JObject.Parse(task.Result)["main"] == Thread.CurrentThread.ManagedThreadId, "ManualFrame runs handlers on the calling (main) thread");
                t.Check(task.IsCompleted && (int)JObject.Parse(task.Result)["frame"] == f, "PumpUpdate advances the frame counter (" + f + ")");
                task = host.Execute("{\"cmd\":\"u.main\",\"waitFrames\":1}");
                host.PumpUpdate();
                t.Check(!task.IsCompleted, "waitFrames 1 through Execute: held");
                host.PumpLate();
                host.PumpUpdate();
                t.Check(task.IsCompleted, "waitFrames 1 through Execute: answered at the next PumpUpdate");
                host.PumpLate();
                int port = srv.Port;
                host.Dispose();
                t.Check(BridgeHost.Current == null && !host.Listening && host.IsDisposed, "Dispose");
                t.Check(LifecycleGroup.PortFree(port), "port free after Dispose");
                t.Check(!File.Exists(Path.Combine(bridgeDir, "endpoint.json")), "endpoint.json removed");
                Debug.Log(marker + " after stop");
                t.Check(srv.Log.Tail(10, LogEntryType.Log, 0, out _).All(e => !e.Message.Contains("after stop")), "the log hook is removed at Dispose");
                var afterDispose = host.Execute("{\"cmd\":\"u.main\"}");
                t.Check(afterDispose.IsCompleted && Kit.Code(JObject.Parse(afterDispose.Result)) == BridgeCodes.NotReady, "Execute of a main-thread command after Dispose answers at once (not_ready, shutting down)");
                var a = BridgeHost.Start(new BridgeHostOptions { Port = 0, RequiredStages = new string[0] });
                var b = BridgeHost.Start(new BridgeHostOptions { Port = 0, RequiredStages = new string[0] });
                t.Check(a.IsDisposed && !a.Listening && b.Listening && BridgeHost.Current == b, "a second Start disposes the first");
                b.Dispose();
                // in-process only
                var ip = BridgeHost.Start(new BridgeHostOptions { Listen = false, ManualPump = true, RequiredStages = new string[0] });
                t.Check(!ip.Listening && ip.Url == null && ip.BindError == null && ip.Server.State == BridgeState.InProcess, "Listen false: no listener, in-process state");
                var it = ip.Execute("{\"cmd\":\"ping\"}");
                t.Check(it.IsCompleted && Kit.Ok(JObject.Parse(it.Result)), "Listen false: ping in-process (answered off the pump)");
                ip.Services.Commands.Register(new CommandSpec("u.ip") { Gate = ReadyGate.None }, ctx => new BridgeReply().With("v", 1));
                it = ip.Execute("{\"cmd\":\"u.ip\"}");
                ip.ManualFrame();
                t.Check(it.IsCompleted && Kit.Str(JObject.Parse(it.Result), "v") == "1", "Listen false: a main-thread command in-process");
                ip.Dispose();
                // a taken port: Start does not throw
                var holder = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                holder.Start();
                int taken = ((System.Net.IPEndPoint)holder.LocalEndpoint).Port;
                var fh = BridgeHost.Start(new BridgeHostOptions { Port = taken, RequiredStages = new string[0], Logger = (l, m) => { }, Server = new BridgeOptions { BindRetries = 1, BindRetryDelayMs = 50 } });
                t.Check(!fh.Listening && fh.BindError != null && fh.Server.State == BridgeState.Failed, "a taken port: Listening false and BindError (" + fh.BindError + ")");
                var ft = fh.Execute("{\"cmd\":\"ping\"}");
                t.Check(ft.IsCompleted && Kit.Ok(JObject.Parse(ft.Result)), "a failed bind still serves in-process requests");
                fh.Dispose();
                holder.Stop();
            }
            finally { BridgeHost.Current?.Dispose(); }
            t.Check(Kit.WaitFor(() => HttpListenerTransport.LiveAcceptThreads == 0, 3000), "no accept thread left");
        }
    }
}
