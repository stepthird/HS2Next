// -bridgeAccept <outDir> (Phase 2.5): the bridge acceptance against the app's own bridge in a
// player. AppBootstrap.AutoStart creates this driver (object "HS2Next Acceptance" in the start scene) when the argument is
// on a player's command line, before it decides on the app, so a player whose app does not start reports that instead of
// waiting. The driver waits for the app (60 s for the host, then until it is ready or failed), then runs over HTTP at the
// app's actual URL (and token): BridgeAcceptance.Run (A1–A14), AutoStep (S1–S6, when A completed), Errors (E1–E3) and
// Parity (T5) — the flow of the Editor's BridgeAcceptance.play without its HS2-shape checks H1–H5 (Editor helpers). It writes
// <outDir>/bridge_accept_report.txt (first and last line "SUMMARY: N PASS, M FAIL (K WARN, S s)"; a placeholder FAIL
// report is written first, so a crash leaves a FAIL) and quits with Application.Quit(code): 0 = every check passed, 1 = a
// check failed or the app did not come up, 3 = the watchdog.
// Arguments: -bridgeAccept <outDir> (relative = to the current directory); -acceptTimeoutSec N (default 900, from the start
// of the driver: a check in Update writes the report and quits with 3; a thread ends the process with code 3 two minutes
// later when the main thread hangs); -acceptCard <path relative to the HS2 female card folder> (default
// AcceptanceOptions.CardPath, the female visual target of Tools/targets.json); -acceptCompare <dir> (render parity, bridge
// prep T5: the shots of BridgeAcceptance.Parity against <dir>/<name>.png of an Editor BridgeAcceptance.play run, reported
// per image as differing pixels, largest channel difference and mean: PASS when equal, a WARN when any pixel differs,
// never a FAIL; A9 / A14 against the Editor's as information); -acceptOnly TS runs only the thumbnail scroll of every Maker
// list (v0.3 trial 2; with -ui; give -acceptTimeoutSec 3600). The app
// takes its usual arguments (-settings, -cacheDir, -userData, -bridgeOutputDir, -bridgePort, ...); the captures go to
// <bridge output>/shots.
using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HS2Next.App
{
    public sealed class PlayerAcceptance : MonoBehaviour
    {
        public const string ObjectName = "HS2Next Acceptance";
        public const string ReportName = "bridge_accept_report.txt";
        public const double DefaultTimeoutSec = 900;
        /// <summary>Exit codes: pass, fail, watchdog.</summary>
        public const int ExitPass = 0, ExitFail = 1, ExitTimeout = 3;

        string outDir, reportPath, card, compareDir, only;
        double timeoutSec = DefaultTimeoutSec;
        readonly Stopwatch clock = new Stopwatch();
        AcceptanceCtx ctx;
        volatile bool finished;
        string header = "";

        /// <summary>Creates the driver when -bridgeAccept is on the command line (players; AppBootstrap.AutoStart).</summary>
        public static PlayerAcceptance StartIfRequested(string[] args)
        {
            int i = Array.IndexOf(args, "-bridgeAccept");
            if (i < 0) return null;
            var go = new GameObject(ObjectName);
            var d = go.AddComponent<PlayerAcceptance>();
            string dir = i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal) ? args[i + 1] : null;
            d.outDir = Path.GetFullPath(string.IsNullOrEmpty(dir) ? "bridge_accept" : dir);
            int t = Array.IndexOf(args, "-acceptTimeoutSec");
            if (t >= 0 && t + 1 < args.Length && double.TryParse(args[t + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0) d.timeoutSec = s;
            int c = Array.IndexOf(args, "-acceptCard");
            d.card = c >= 0 && c + 1 < args.Length ? args[c + 1] : new AcceptanceOptions().CardPath;
            int k = Array.IndexOf(args, "-acceptCompare");
            if (k >= 0 && k + 1 < args.Length) d.compareDir = Path.GetFullPath(args[k + 1]);
            int n = Array.IndexOf(args, "-acceptOnly");
            if (n >= 0 && n + 1 < args.Length) d.only = args[n + 1];
            return d;
        }

        void Awake()
        {
            clock.Start();
            Application.runInBackground = true;
        }

        IEnumerator Start()
        {
            reportPath = Path.Combine(outDir, ReportName);
            Directory.CreateDirectory(outDir);
            File.WriteAllText(reportPath, "SUMMARY: 0 PASS, 1 FAIL (the acceptance did not finish)\n", new UTF8Encoding(false));
            StartHardWatchdog();
            ctx = new AcceptanceCtx { FemaleRel = card };
            double sinceProcess = -1;
            try { using (var p = Process.GetCurrentProcess()) sinceProcess = (DateTime.Now - p.StartTime).TotalSeconds; } catch (Exception) { }
            header = $"Bridge acceptance in the player (Unity {Application.unityVersion}, {(Debug.isDebugBuild ? "development" : "release")} player, batch mode {Application.isBatchMode}, " +
                     $"graphics {SystemInfo.graphicsDeviceType} '{SystemInfo.graphicsDeviceName}', scripting {(Type.GetType("Mono.Runtime") != null ? "Mono" : "not Mono")}); " +
                     $"report {reportPath}; watchdog {timeoutSec:F0} s; card '{card}'; driver started {sinceProcess:F1} s after the process";

            // the app: host, then ready or failed
            AppCore app = null;
            while ((app = AppCore.Current) == null && clock.Elapsed.TotalSeconds < 60) yield return null;
            ctx.Id = "app";
            if (app == null)
            {
                ctx.Check(false, "the app started", "no HS2Next app within 60 s (-noApp, -smoke or app.autoStart false?)");
                Finish(ExitFail);
                yield break;
            }
            while (app.Stage == AppStage.Starting && !app.IsDisposed) yield return null;
            double readyDriver = clock.Elapsed.TotalSeconds;
            double readyProcess = -1;
            try { using (var p = Process.GetCurrentProcess()) readyProcess = (DateTime.Now - p.StartTime).TotalSeconds; } catch (Exception) { }
            ctx.Info($"app {app.Stage} {readyProcess:F2} s after the process start ({readyDriver:F2} s after the driver, realtimeSinceStartup {Time.realtimeSinceStartup:F2} s, frame {Time.frameCount}); " +
                     $"target frame rate {Application.targetFrameRate}, screen {Screen.width}x{Screen.height}");
            if (!ctx.Check(app.Stage == AppStage.Ready, "the app is ready", app.Error ?? ""))
            {
                Finish(ExitFail);
                yield break;
            }
            var bridge = app.Bridge;
            if (!ctx.Check(bridge != null && bridge.Listening, "the app's bridge listens", bridge == null ? "bridge disabled" : bridge.Url ?? ("not listening: " + bridge.BindError)))
            {
                Finish(ExitFail);
                yield break;
            }
            string url = bridge.Url;
            string token = app.Settings.Bridge.Token ? bridge.Server.Token : null;
            ctx.Client = new BridgeClient(url, token);
            ctx.Info($"bridge {url}{(token != null ? " (token)" : "")}; UserData {app.Paths.UserData}; output {app.Paths.OutputDir}");

            // -acceptOnly TS (v0.3 trial 2): the thumbnail scroll of every Maker list alone (needs -ui; BridgeAcceptance_ThumbScroll.cs)
            if (only == "TS")
            {
                var swOnly = Stopwatch.StartNew();
                yield return BridgeAcceptance.Guard(ctx, "TS", BridgeAcceptance.ThumbScroll(ctx));
                ctx.Info($"TS: {swOnly.Elapsed.TotalSeconds:F1} s");
                Finish(ctx.Failed > 0 ? ExitFail : ExitPass);
                yield break;
            }

            // A1–A14, S1–S6, E1–E3
            var o = new AcceptanceOptions { Url = url, Token = token, CardPath = card, Sex = "female" };
            var sw = Stopwatch.StartNew();
            yield return BridgeAcceptance.Guard(ctx, "A", BridgeAcceptance.Run(o, ctx.Rep, ctx.CheckLine));
            ctx.Info($"acceptance A1–A14: {sw.Elapsed.TotalSeconds:F1} s; captures {o.Result.ShotBefore ?? "-"}, {o.Result.ShotAfter ?? "-"}; D1 {o.Result.D1}");
            ctx.FemaleKey = o.Result.CharaKey;
            sw.Restart();
            if (o.Result.Completed) yield return BridgeAcceptance.Guard(ctx, "S", BridgeAcceptance.AutoStep(ctx));
            else { ctx.Id = "S1"; ctx.Warn("S1–S6 skipped: the acceptance did not complete"); }
            ctx.Info($"S1–S6: {sw.Elapsed.TotalSeconds:F1} s");
            sw.Restart();
            yield return BridgeAcceptance.Errors(ctx);
            ctx.Info($"E1–E3: {sw.Elapsed.TotalSeconds:F1} s");
            sw.Restart();
            yield return BridgeAcceptance.Guard(ctx, "T5", BridgeAcceptance.Parity(ctx));
            ctx.Info($"T5: {sw.Elapsed.TotalSeconds:F1} s");
            sw.Restart();
            yield return BridgeAcceptance.Guard(ctx, "I1", BridgeAcceptance.Items(ctx));
            ctx.Info($"I1: {sw.Elapsed.TotalSeconds:F1} s");
            sw.Restart();
            yield return BridgeAcceptance.Guard(ctx, "M1", BridgeAcceptance.Maps(ctx));
            ctx.Info($"M1: {sw.Elapsed.TotalSeconds:F1} s");
            sw.Restart();
            yield return BridgeAcceptance.Guard(ctx, "LT1", BridgeAcceptance.Lights(ctx));   // Phase 3.3 package A
            ctx.Info($"LT1: {sw.Elapsed.TotalSeconds:F1} s");
            sw.Restart();
            yield return BridgeAcceptance.Guard(ctx, "FX1", BridgeAcceptance.Effects(ctx));   // Phase 3.3 package B
            ctx.Info($"FX1: {sw.Elapsed.TotalSeconds:F1} s");
            sw.Restart();
            yield return BridgeAcceptance.Guard(ctx, "IK1", BridgeAcceptance.Kinematics(ctx));   // Phase 3.4 package C
            ctx.Info($"IK1: {sw.Elapsed.TotalSeconds:F1} s");
            sw.Restart();
            yield return BridgeAcceptance.Guard(ctx, "UI1", BridgeAcceptance.Ui(ctx));   // Phase 3.5 package A (needs -ui)
            ctx.Info($"UI1: {sw.Elapsed.TotalSeconds:F1} s");
            sw.Restart();
            yield return BridgeAcceptance.Guard(ctx, "RT1", BridgeAcceptance.Routes(ctx));   // Phase 3.6 package R
            ctx.Info($"RT1: {sw.Elapsed.TotalSeconds:F1} s");
            sw.Restart();
            yield return BridgeAcceptance.Guard(ctx, "UI2", BridgeAcceptance.Ui2(ctx));   // Phase 3.6 integrator (needs -ui; the male card HERO107.png)
            ctx.Info($"UI2: {sw.Elapsed.TotalSeconds:F1} s");
            sw.Restart();
            yield return BridgeAcceptance.Guard(ctx, "UI3", BridgeAcceptance.Ui3(ctx));   // Phase 4.0 integrator T (needs -ui)
            ctx.Info($"UI3: {sw.Elapsed.TotalSeconds:F1} s");
            sw.Restart();
            yield return BridgeAcceptance.Guard(ctx, "UI4", BridgeAcceptance.Ui4(ctx));   // Phase 4.1 integrator T (needs -ui)
            ctx.Info($"UI4: {sw.Elapsed.TotalSeconds:F1} s");
            sw.Restart();
            yield return BridgeAcceptance.Guard(ctx, "UI5", BridgeAcceptance.Ui5(ctx));   // Phase 4.2 integrator T (needs -ui)
            ctx.Info($"UI5: {sw.Elapsed.TotalSeconds:F1} s");
            sw.Restart();
            yield return BridgeAcceptance.Guard(ctx, "UI6", BridgeAcceptance.Ui6(ctx));   // Phase 4.3 integrator T (needs -ui)
            ctx.Info($"UI6: {sw.Elapsed.TotalSeconds:F1} s");
            sw.Restart();
            yield return BridgeAcceptance.Guard(ctx, "UI7", BridgeAcceptance.Ui7(ctx));   // Phase 4.4 integrator T (needs -ui; the Clothes page and the Costume tab)
            ctx.Info($"UI7: {sw.Elapsed.TotalSeconds:F1} s");
            sw.Restart();
            yield return BridgeAcceptance.Guard(ctx, "UI8", BridgeAcceptance.Ui8(ctx));   // Phase 4.5 integrator T (needs -ui; the Accessories page)
            ctx.Info($"UI8: {sw.Elapsed.TotalSeconds:F1} s");
            sw.Restart();
            yield return BridgeAcceptance.Guard(ctx, "UI9", BridgeAcceptance.Ui9(ctx));   // Phase 4.6 integrator T (needs -ui; the Character and File pages)
            ctx.Info($"UI9: {sw.Elapsed.TotalSeconds:F1} s");
            Compare(o, app.Paths.OutputDir);
            Finish(ctx.Failed > 0 ? ExitFail : ExitPass);
        }

        /// <summary>
        /// T5: the parity shots against the Editor run's of <c>-acceptCompare</c>: PASS when every pixel is equal, else a WARN
        /// with the numbers (a finding to explain, never a FAIL); shots with a known reason to differ (ParityShots) and A9 / A14
        /// (the paused animation time of A8 depends on frame timing) are reported as information.
        /// </summary>
        void Compare(AcceptanceOptions o, string outputDir)
        {
            if (compareDir == null) return;
            ctx.Id = "T5";
            const string timing = "information: the paused time of A8 depends on frame timing";
            var shots = new System.Collections.Generic.List<(string name, string path, string known)>
            {
                (o.ShotBefore, o.Result.ShotBefore, timing), (o.ShotAfter, o.Result.ShotAfter, timing),
            };
            foreach (var (name, _, _, known) in BridgeAcceptance.ParityShots) shots.Add((name, Path.Combine(outputDir, "shots", name + ".png"), known));
            foreach (var (name, shot, known) in shots)
            {
                string reference = Path.Combine(compareDir, name + ".png");
                var d = BridgeAcceptance.PngDiff(shot, reference, out var why);
                string when = File.Exists(reference) ? File.GetLastWriteTime(reference).ToString("yyyy-MM-dd HH:mm:ss") : "?";
                if (d == null)
                {
                    if (known == null) ctx.Warn($"render parity {name}: not compared ({why})"); else ctx.Info($"render parity {name}: not compared ({why})");
                    continue;
                }
                var v = d.Value;
                string line = $"render parity {name} vs the Editor's ({reference}, written {when}): {v.w}x{v.h}, {v.differing} of {(long)v.w * v.h} pixels differ, " +
                              $"max channel difference {v.maxDiff}/255, mean |d| {v.meanAbs:F4}";
                if (known != null) ctx.Info(line + " (" + known + ")");
                else if (v.differing == 0) ctx.Check(true, line);
                else ctx.Warn(line);
            }
        }

        void Update()
        {
            if (finished || clock.Elapsed.TotalSeconds <= timeoutSec) return;
            StopAllCoroutines();
            if (ctx != null) { ctx.Id = "watchdog"; ctx.Check(false, $"the acceptance finished within {timeoutSec:F0} s"); }
            Finish(ExitTimeout, $"WATCHDOG: -acceptTimeoutSec {timeoutSec:F0} s passed; exit {ExitTimeout}");
        }

        void Finish(int code, string trailer = null)
        {
            if (finished) return;
            finished = true;
            Write(code, trailer);
            ctx?.Dispose();
            Debug.Log($"[accept] done: {ctx?.Passed} PASS, {ctx?.Failed} FAIL, {ctx?.Warned} WARN; exit {code}; report {reportPath}");
            Application.Quit(code);
        }

        void Write(int code, string trailer)
        {
            try
            {
                string tail = (trailer != null ? trailer + "\n" : "") + "exit code " + code;
                var text = (ctx ?? new AcceptanceCtx()).Report(clock.Elapsed.TotalSeconds, tail, header);
                File.WriteAllText(reportPath, text, new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogError("[accept] cannot write the report: " + e.Message); }
        }

        // ------------------------------------------------------------ the hard watchdog (main thread hung)

        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process, uint exitCode);

        void StartHardWatchdog()
        {
            double limit = timeoutSec + 120;
            string report = reportPath;
            var t = new Thread(() =>
            {
                try
                {
                    Thread.Sleep(TimeSpan.FromSeconds(limit));
                    if (finished) return;
                    try { File.AppendAllText(report, $"\nWATCHDOG: the main thread did not finish within {limit:F0} s; process ended with exit {ExitTimeout}\nSUMMARY: 0 PASS, 1 FAIL (watchdog)\n", new UTF8Encoding(false)); } catch (Exception) { }
                    TerminateProcess(GetCurrentProcess(), ExitTimeout);
                }
                catch (Exception) { }
            }) { IsBackground = true, Name = "HS2Next acceptance watchdog" };
            t.Start();
        }
    }
}
