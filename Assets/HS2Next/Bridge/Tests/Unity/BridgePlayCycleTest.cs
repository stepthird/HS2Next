// T2: enter and leave Editor play mode repeatedly with the bridge on, five times
// with domain reload and five times without (Enter Play Mode Options), on one fixed port. In every play session:
// BridgeHost.Start → listening, the DontDestroyOnLoad pump object exists, ping over HTTP, a main-thread command pumped by
// the host's Update, a waitFrames reply, a late-point action run by the late host; after leaving play mode: the
// server is gone, the port can be bound again (no "address in use", no orphaned listener), no accept thread is left
// (in the same domain), and the bridge log has no bind failure. The cycle state survives domain reloads in
// SessionState; EditorSettings' play-mode options are restored at the end.
//   Run:     Tools/unity_batch.ps1 -Method HS2Next.Bridge.Tests.BridgePlayCycleTest.Run -NoQuit -NoGraphics
//            (-MethodArgs @('-cycles','5') per mode; the method ends the editor itself)
//   Report:  Spike/out/bridge/bridge_play_cycle_report.txt, first line "N passed, M failed"; exit 0 / 1, 2 = guard.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HS2Next.Api.Bridge;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HS2Next.Bridge.Tests
{
    [InitializeOnLoad]
    public static class BridgePlayCycleTest
    {
        const string K = "HS2Next.BridgePlayCycle.";
        static bool hooked, busy;
        static Task<string> pingTask, mainTask, waitTask, lateTask;
        static int sessionFrame, ticks;
        static double sessionStart;
        static readonly List<string> sessionLog = new List<string>();

        static string ReportPath => Path.Combine(BridgeTest.ProjectRoot, "Spike", "out", "bridge", "bridge_play_cycle_report.txt");

        static BridgePlayCycleTest()
        {
            if (!SessionState.GetBool(K + "active", false)) return;
            Hook();
            // a domain reload ends the watchdog thread: start it again with the time that is left
            if (long.TryParse(SessionState.GetString(K + "startedUtc", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out long t0))
                StartWatchdog(Math.Max(0.5, 15 - (DateTime.UtcNow - new DateTime(t0, DateTimeKind.Utc)).TotalMinutes));
        }

        /// <summary>Entry (-executeMethod, without -quit).</summary>
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            int cycles = 5;
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-cycles") int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out cycles);
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            SessionState.SetBool(K + "active", true);
            SessionState.SetInt(K + "port", port);
            SessionState.SetInt(K + "cycles", Math.Max(1, cycles));
            SessionState.SetInt(K + "done", 0);
            SessionState.SetInt(K + "passed", 0);
            SessionState.SetInt(K + "failed", 0);
            SessionState.SetString(K + "lines", "");
            SessionState.SetBool(K + "origEnabled", EditorSettings.enterPlayModeOptionsEnabled);
            SessionState.SetInt(K + "origOptions", (int)EditorSettings.enterPlayModeOptions);
            SessionState.SetString(K + "startedUtc", DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
            try { File.Delete(ReportPath); } catch (Exception) { }
            StartWatchdog(15);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Hook();
            NextCycle();
        }

        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.update += Tick;
        }

        static int Done => SessionState.GetInt(K + "done", 0);
        static int Cycles => SessionState.GetInt(K + "cycles", 5);
        static bool ReloadPhase(int cycle) => cycle < Cycles;   // first half with domain reload

        static void Check(bool ok, string what)
        {
            if (ok) SessionState.SetInt(K + "passed", SessionState.GetInt(K + "passed", 0) + 1);
            else
            {
                SessionState.SetInt(K + "failed", SessionState.GetInt(K + "failed", 0) + 1);
                Line("FAIL " + what);
                Debug.LogError("BridgePlayCycleTest: FAIL " + what);
            }
        }

        static void Line(string s) => SessionState.SetString(K + "lines", SessionState.GetString(K + "lines", "") + s + "\n");

        static void NextCycle()
        {
            int c = Done;
            if (c >= 2 * Cycles) { Finish(); return; }
            bool reload = ReloadPhase(c);
            EditorSettings.enterPlayModeOptionsEnabled = !reload;
            EditorSettings.enterPlayModeOptions = reload ? EnterPlayModeOptions.None : EnterPlayModeOptions.DisableDomainReload;
            Line("cycle " + (c + 1) + ": domain reload " + (reload ? "on" : "off"));
            EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
        }

        static void OnState(PlayModeStateChange s)
        {
            if (!SessionState.GetBool(K + "active", false)) return;
            if (s == PlayModeStateChange.EnteredPlayMode) BeginSession();
            else if (s == PlayModeStateChange.EnteredEditMode) EndSession();
        }

        static void BeginSession()
        {
            int port = SessionState.GetInt(K + "port", 0);
            Application.runInBackground = true;
            sessionLog.Clear();
            var host = BridgeHost.Start(new BridgeHostOptions
            {
                Port = port,
                RequiredStages = new string[0],
                OptionalStages = new string[0],
                Logger = (lvl, msg) => { lock (sessionLog) sessionLog.Add(lvl + ": " + msg); },
            });
            var srv = host.Server;
            int c = Done + 1;
            Check(host.Listening && !host.ManualPump, "cycle " + c + ": listening on " + port + " with the pump components (" + host.BindError + ")");
            Check(GameObject.Find("HS2Next Bridge") != null, "cycle " + c + ": pump object present in play mode");
            if (srv == null || !srv.IsListening) { busy = false; EditorApplication.ExitPlaymode(); return; }
            srv.Commands.Register(new CommandSpec("cycle.frame") { Gate = ReadyGate.None }, ctx => new BridgeReply().With("frame", Time.frameCount).With("ctxFrame", ctx.Frames.Frame));
            srv.Commands.Register(new CommandSpec("cycle.late") { Gate = ReadyGate.None }, ctx =>
            {
                var d = ctx.Defer();
                int asked = Time.frameCount;
                ctx.Frames.AtLate(0, () => d.Complete(new BridgeReply().With("asked", asked).With("late", Time.frameCount)));
                return null;
            });
            pingTask = Task.Run(() => Http.Post(port, "{\"cmd\":\"ping\"}", null, 10000).Body);
            mainTask = Task.Run(() => Http.Post(port, "{\"cmd\":\"cycle.frame\"}", null, 10000).Body);
            waitTask = Task.Run(() => Http.Post(port, "{\"cmd\":\"cycle.frame\",\"waitFrames\":3}", null, 10000).Body);
            lateTask = Task.Run(() => Http.Post(port, "{\"cmd\":\"cycle.late\"}", null, 10000).Body);
            sessionFrame = Time.frameCount;
            sessionStart = EditorApplication.timeSinceStartup;
            ticks = 0;
            busy = true;
        }

        static void Tick()
        {
            if (!busy || !EditorApplication.isPlaying) return;
            ticks++;
            bool all = pingTask.IsCompleted && mainTask.IsCompleted && waitTask.IsCompleted && lateTask.IsCompleted;
            if (!all && EditorApplication.timeSinceStartup - sessionStart < 20) return;
            busy = false;
            int c = Done + 1;
            var ping = Parse(pingTask);
            var main = Parse(mainTask);
            var wait = Parse(waitTask);
            var late = Parse(lateTask);
            Check(Kit.Ok(ping), "cycle " + c + ": ping over HTTP in play mode");
            Check(Kit.Ok(main) && (int)main["frame"] == (int)main["ctxFrame"], "cycle " + c + ": a main-thread command pumped by BridgeHost.Update (frame " + main?["frame"] + ")");
            Check(Kit.Ok(wait), "cycle " + c + ": a waitFrames reply");
            Check(Kit.Ok(late) && (int)late["late"] == (int)late["asked"], "cycle " + c + ": AtLate(0) ran in the same frame by BridgeLateHost (" + late?["asked"] + " -> " + late?["late"] + ")");
            Line("  play session: " + ticks + " editor ticks, frames " + sessionFrame + " -> " + Time.frameCount);
            EditorApplication.ExitPlaymode();
        }

        static JObject Parse(Task<string> t)
        {
            try { return t.IsCompleted && t.Result != null ? JObject.Parse(t.Result) : null; }
            catch (Exception) { return null; }
        }

        static void EndSession()
        {
            int c = Done + 1;
            int port = SessionState.GetInt(K + "port", 0);
            Check(BridgeHost.Current == null, "cycle " + c + ": no host after leaving play mode");
            bool free = false;
            for (int i = 0; i < 20 && !free; i++)
            {
                try
                {
                    var l = new TcpListener(IPAddress.Loopback, port);
                    l.Start();
                    l.Stop();
                    free = true;
                }
                catch (SocketException) { Thread.Sleep(50); }
            }
            Check(free, "cycle " + c + ": port " + port + " can be bound after leaving play mode (no orphaned listener)");
            bool refused = Http.Post(port, "{\"cmd\":\"ping\"}", null, 2000).Status == 0;
            Check(refused, "cycle " + c + ": connection refused after leaving play mode");
            bool reload = ReloadPhase(c - 1);
            if (!reload) Check(Kit.WaitFor(() => HttpListenerTransport.LiveAcceptThreads == 0, 3000), "cycle " + c + ": no accept thread left in the domain (" + HttpListenerTransport.LiveAcceptThreads + ")");
            lock (sessionLog)
            {
                foreach (var m in sessionLog) if (m.Contains("cannot listen") || m.Contains("not listening")) Check(false, "cycle " + c + ": bind problem: " + m);
                Line("  bridge log: " + string.Join(" | ", sessionLog));
            }
            if (reload) Line("  (domain reloaded on leaving play mode; the session log above is from this domain only)");
            SessionState.SetInt(K + "done", c);
            NextCycle();
        }

        static void Finish()
        {
            EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(K + "origEnabled", false);
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(K + "origOptions", 0);
            int passed = SessionState.GetInt(K + "passed", 0), failed = SessionState.GetInt(K + "failed", 0);
            var sb = new StringBuilder();
            sb.AppendLine(passed + " passed, " + failed + " failed");
            sb.AppendLine("T2: " + 2 * Cycles + " play sessions (" + Cycles + " with domain reload, " + Cycles + " without), port " + SessionState.GetInt(K + "port", 0) +
                          ", Unity " + Application.unityVersion);
            sb.AppendLine();
            sb.Append(SessionState.GetString(K + "lines", ""));
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
            foreach (var k in new[] { "active", "origEnabled" }) SessionState.EraseBool(K + k);
            foreach (var k in new[] { "port", "cycles", "done", "passed", "failed", "origOptions" }) SessionState.EraseInt(K + k);
            foreach (var k in new[] { "lines", "startedUtc" }) SessionState.EraseString(K + k);
            Debug.Log("BridgePlayCycleTest: " + passed + " passed, " + failed + " failed -> " + ReportPath);
            EditorApplication.Exit(failed > 0 ? 1 : 0);
        }

        static void StartWatchdog(double minutes)
        {
            int pid = Process.GetCurrentProcess().Id;
            new Thread(() =>
            {
                try
                {
                    Thread.Sleep(TimeSpan.FromMinutes(minutes));
                    try { File.AppendAllText(ReportPath, "\nRESULT: FAIL (watchdog after " + minutes + " min)\n"); } catch (Exception) { }
                    Process.GetProcessById(pid).Kill();
                }
                catch (Exception) { }
            }) { IsBackground = true, Name = "BridgePlayCycleTest watchdog" }.Start();
        }
    }
}
