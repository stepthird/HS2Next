// The bridge in play mode over real HTTP: the app comes up through AppBootstrap with a
// launch override (port 17398, the tests' UserData and output root under Spike/out/bridge/), then the runtime parts the
// player's -bridgeAccept runs too (Runtime/App/BridgeAcceptance*.cs): the Phase 2 acceptance A1–A14
// (BridgeAcceptance.Run), the AutoStep checks S1–S6 (BridgeAcceptance.AutoStep), then the Editor-only HS2-shape checks
// H1–H5 (static IEnumerator Shape(AcceptanceCtx) in Editor/BridgeTest_Motion.cs: BridgeCoreTest's shape helpers and
// Cache/compare), the error and load checks E1–E3 (BridgeAcceptance.Errors) and the render-parity shots T5
// (BridgeAcceptance.Parity: Spike/out/bridge/out/shots/parity_*.png, the references of the player's comparison).
// Run: Tools/unity_batch.ps1 -Method BridgeTest.RunPlay -NoQuit (graphics: captures need a device).
// Report: Spike/out/bridge/bridge_play_report.txt, first (and last) line "SUMMARY: N PASS, M FAIL"; exit code 1 on a FAIL.
// HTTP requests run on worker tasks (BridgeClient) and the coroutine polls them each frame, so the main thread keeps
// pumping the bridge. A part that throws is a FAIL of that part; the run goes on.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HS2Next.App;
using HS2Next.Content;
using UnityEngine;

public static partial class BridgeTest
{
    public const int Port = 17398;
    public static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "bridge");
    public const string ReportFile = "bridge_play_report.txt";

    /// <summary>The launch override of the play session: the app on, the tests' UserData and output root, port 17398.</summary>
    public static string Launch =>
        "{\"app\":{\"autoStart\":true},\"userData\":\"Spike/out/bridge/userdata\",\"bridge\":{\"port\":" + Port + ",\"outputDir\":\"Spike/out/bridge/out\",\"allowQuit\":false}}";

    static string ReportPath => Path.Combine(OutDir, ReportFile);

    /// <summary>-executeMethod entry (with Tools/unity_batch.ps1 -NoQuit).</summary>
    public static void RunPlay()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(ReportPath, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("BridgeTest.PlayMain", ReportPath, 30, true, Launch);
    }

    /// <summary>UI1 alone with the Studio UI on (Phase 3.5 package A): -executeMethod BridgeTest.RunPlayUi (-NoQuit; graphics).</summary>
    public static void RunPlayUi()
    {
        Directory.CreateDirectory(OutDir);
        string report = Path.Combine(OutDir, "bridge_play_ui1_report.txt");
        File.WriteAllText(report, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("BridgeTest.PlayUi", report, 30, true, Launch.Replace("{\"app\":{\"autoStart\":true},", "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},"));
    }

    static IEnumerator PlayUi()
    {
        var c = new AcceptanceCtx();
        var total = System.Diagnostics.Stopwatch.StartNew();
        var targets = TestTargets.Load();
        c.FemaleRel = BridgeCoreTest.RelTo(TestTargets.Abs(targets.visual.female), Path.Combine(TestTargets.UserData, "chara", "female"));
        c.Client = new BridgeClient("http://127.0.0.1:" + Port + "/");
        c.Rep.AppendLine($"Bridge play test {(Ui9Only ? "UI9" : Ui8Only ? "UI8" : Ui7Only ? "UI7" : Ui6Only ? "UI6" : Ui5Only ? "UI5" : Ui4Only ? "UI4" : Ui3Only ? "UI3" : Ui2Only ? "UI2" : "UI1 + UI2 + UI3 + UI4 + UI5 + UI6 + UI7 + UI8 + UI9")} (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}); {c.Url}");
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting && HS2Next.UI.UiHost.Current != null && HS2Next.UI.UiHost.Current.IsReady, 300, ok);
        c.Check(ok[0], "app and UI ready");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (Ui9Only)
        {
            BridgeAcceptance.Ui2MaleRel = BridgeCoreTest.RelTo(TestTargets.Abs(targets.visual.male), Path.Combine(TestTargets.UserData, "chara", "male"));
            yield return BridgeAcceptance.Guard(c, "UI9", BridgeAcceptance.Ui9(c));
            c.Info($"UI9: {sw.Elapsed.TotalSeconds:F1} s");
            c.Dispose();
            File.WriteAllText(Path.Combine(OutDir, "bridge_play_ui9_report.txt"), c.Report(total.Elapsed.TotalSeconds), new UTF8Encoding(false));
            Debug.Log($"BRIDGE PLAY UI9 DONE: {c.Passed} PASS, {c.Failed} FAIL, {c.Warned} WARN");
            PlayModeHarness.ExitCode = c.Failed > 0 ? 1 : 0;
            yield break;
        }
        if (Ui8Only)
        {
            yield return BridgeAcceptance.Guard(c, "UI8", BridgeAcceptance.Ui8(c));
            c.Info($"UI8: {sw.Elapsed.TotalSeconds:F1} s");
            c.Dispose();
            File.WriteAllText(Path.Combine(OutDir, "bridge_play_ui8_report.txt"), c.Report(total.Elapsed.TotalSeconds), new UTF8Encoding(false));
            Debug.Log($"BRIDGE PLAY UI8 DONE: {c.Passed} PASS, {c.Failed} FAIL, {c.Warned} WARN");
            PlayModeHarness.ExitCode = c.Failed > 0 ? 1 : 0;
            yield break;
        }
        if (Ui7Only)
        {
            yield return BridgeAcceptance.Guard(c, "UI7", BridgeAcceptance.Ui7(c));
            c.Info($"UI7: {sw.Elapsed.TotalSeconds:F1} s");
            c.Dispose();
            File.WriteAllText(Path.Combine(OutDir, "bridge_play_ui7_report.txt"), c.Report(total.Elapsed.TotalSeconds), new UTF8Encoding(false));
            Debug.Log($"BRIDGE PLAY UI7 DONE: {c.Passed} PASS, {c.Failed} FAIL, {c.Warned} WARN");
            PlayModeHarness.ExitCode = c.Failed > 0 ? 1 : 0;
            yield break;
        }
        if (Ui6Only)
        {
            yield return BridgeAcceptance.Guard(c, "UI6", BridgeAcceptance.Ui6(c));
            c.Info($"UI6: {sw.Elapsed.TotalSeconds:F1} s");
            c.Dispose();
            File.WriteAllText(Path.Combine(OutDir, "bridge_play_ui6_report.txt"), c.Report(total.Elapsed.TotalSeconds), new UTF8Encoding(false));
            Debug.Log($"BRIDGE PLAY UI6 DONE: {c.Passed} PASS, {c.Failed} FAIL, {c.Warned} WARN");
            PlayModeHarness.ExitCode = c.Failed > 0 ? 1 : 0;
            yield break;
        }
        if (Ui5Only)
        {
            yield return BridgeAcceptance.Guard(c, "UI5", BridgeAcceptance.Ui5(c));
            c.Info($"UI5: {sw.Elapsed.TotalSeconds:F1} s");
            c.Dispose();
            File.WriteAllText(Path.Combine(OutDir, "bridge_play_ui5_report.txt"), c.Report(total.Elapsed.TotalSeconds), new UTF8Encoding(false));
            Debug.Log($"BRIDGE PLAY UI5 DONE: {c.Passed} PASS, {c.Failed} FAIL, {c.Warned} WARN");
            PlayModeHarness.ExitCode = c.Failed > 0 ? 1 : 0;
            yield break;
        }
        if (Ui4Only)
        {
            yield return BridgeAcceptance.Guard(c, "UI4", BridgeAcceptance.Ui4(c));
            c.Info($"UI4: {sw.Elapsed.TotalSeconds:F1} s");
            c.Dispose();
            File.WriteAllText(Path.Combine(OutDir, "bridge_play_ui4_report.txt"), c.Report(total.Elapsed.TotalSeconds), new UTF8Encoding(false));
            Debug.Log($"BRIDGE PLAY UI4 DONE: {c.Passed} PASS, {c.Failed} FAIL, {c.Warned} WARN");
            PlayModeHarness.ExitCode = c.Failed > 0 ? 1 : 0;
            yield break;
        }
        if (Ui3Only)
        {
            yield return BridgeAcceptance.Guard(c, "UI3", BridgeAcceptance.Ui3(c));
            c.Info($"UI3: {sw.Elapsed.TotalSeconds:F1} s");
            c.Dispose();
            File.WriteAllText(Path.Combine(OutDir, "bridge_play_ui3_report.txt"), c.Report(total.Elapsed.TotalSeconds), new UTF8Encoding(false));
            Debug.Log($"BRIDGE PLAY UI3 DONE: {c.Passed} PASS, {c.Failed} FAIL, {c.Warned} WARN");
            PlayModeHarness.ExitCode = c.Failed > 0 ? 1 : 0;
            yield break;
        }
        if (!Ui2Only) { yield return BridgeAcceptance.Guard(c, "UI1", BridgeAcceptance.Ui(c)); c.Info($"UI1: {sw.Elapsed.TotalSeconds:F1} s"); }
        sw.Restart();
        BridgeAcceptance.Ui2MaleRel = BridgeCoreTest.RelTo(TestTargets.Abs(targets.visual.male), Path.Combine(TestTargets.UserData, "chara", "male"));
        yield return BridgeAcceptance.Guard(c, "UI2", BridgeAcceptance.Ui2(c));   // Phase 3.6 integrator
        c.Info($"UI2: {sw.Elapsed.TotalSeconds:F1} s");
        sw.Restart();
        yield return BridgeAcceptance.Guard(c, "UI3", BridgeAcceptance.Ui3(c));   // Phase 4.0 integrator T
        c.Info($"UI3: {sw.Elapsed.TotalSeconds:F1} s");
        sw.Restart();
        yield return BridgeAcceptance.Guard(c, "UI4", BridgeAcceptance.Ui4(c));   // Phase 4.1 integrator T
        c.Info($"UI4: {sw.Elapsed.TotalSeconds:F1} s");
        sw.Restart();
        yield return BridgeAcceptance.Guard(c, "UI5", BridgeAcceptance.Ui5(c));   // Phase 4.2 integrator T
        c.Info($"UI5: {sw.Elapsed.TotalSeconds:F1} s");
        sw.Restart();
        yield return BridgeAcceptance.Guard(c, "UI6", BridgeAcceptance.Ui6(c));   // Phase 4.3 integrator T
        c.Info($"UI6: {sw.Elapsed.TotalSeconds:F1} s");
        sw.Restart();
        yield return BridgeAcceptance.Guard(c, "UI7", BridgeAcceptance.Ui7(c));   // Phase 4.4 integrator T (the Clothes page and the Costume tab)
        c.Info($"UI7: {sw.Elapsed.TotalSeconds:F1} s");
        sw.Restart();
        yield return BridgeAcceptance.Guard(c, "UI8", BridgeAcceptance.Ui8(c));   // Phase 4.5 integrator T (the Accessories page)
        c.Info($"UI8: {sw.Elapsed.TotalSeconds:F1} s");
        sw.Restart();
        yield return BridgeAcceptance.Guard(c, "UI9", BridgeAcceptance.Ui9(c));   // Phase 4.6 integrator T (the Character and File pages)
        c.Info($"UI9: {sw.Elapsed.TotalSeconds:F1} s");
        c.Dispose();
        string name = Ui2Only ? "bridge_play_ui2_report.txt" : "bridge_play_ui1_report.txt";
        File.WriteAllText(Path.Combine(OutDir, name), c.Report(total.Elapsed.TotalSeconds), new UTF8Encoding(false));
        Debug.Log($"BRIDGE PLAY {(Ui2Only ? "UI2" : "UI1 + UI2")} DONE: {c.Passed} PASS, {c.Failed} FAIL, {c.Warned} WARN");
        PlayModeHarness.ExitCode = c.Failed > 0 ? 1 : 0;
    }

    /// <summary>UI2 alone (Phase 3.6 integrator): -executeMethod BridgeTest.RunPlayUi2 (-NoQuit; graphics); report bridge_play_ui2_report.txt.</summary>
    public static void RunPlayUi2()
    {
        Directory.CreateDirectory(OutDir);
        string report = Path.Combine(OutDir, "bridge_play_ui2_report.txt");
        File.WriteAllText(report, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("BridgeTest.PlayUi2", report, 20, true, Launch.Replace("{\"app\":{\"autoStart\":true},", "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},"));
    }

    static bool Ui2Only, Ui3Only, Ui4Only, Ui5Only, Ui6Only, Ui7Only, Ui8Only, Ui9Only;

    /// <summary>UI9 alone (Phase 4.6 integrator T): -executeMethod BridgeTest.RunPlayUi9 (-NoQuit; graphics); report bridge_play_ui9_report.txt.</summary>
    public static void RunPlayUi9()
    {
        Directory.CreateDirectory(OutDir);
        string report = Path.Combine(OutDir, "bridge_play_ui9_report.txt");
        File.WriteAllText(report, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("BridgeTest.PlayUi9", report, 20, true, Launch.Replace("{\"app\":{\"autoStart\":true},", "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},"));
    }

    static IEnumerator PlayUi9()
    {
        Ui9Only = true;
        return PlayUi();
    }

    /// <summary>UI8 alone (Phase 4.5 integrator T): -executeMethod BridgeTest.RunPlayUi8 (-NoQuit; graphics); report bridge_play_ui8_report.txt.</summary>
    public static void RunPlayUi8()
    {
        Directory.CreateDirectory(OutDir);
        string report = Path.Combine(OutDir, "bridge_play_ui8_report.txt");
        File.WriteAllText(report, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("BridgeTest.PlayUi8", report, 20, true, Launch.Replace("{\"app\":{\"autoStart\":true},", "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},"));
    }

    static IEnumerator PlayUi8()
    {
        Ui8Only = true;
        return PlayUi();
    }

    /// <summary>UI7 alone (Phase 4.4 integrator T): -executeMethod BridgeTest.RunPlayUi7 (-NoQuit; graphics); report bridge_play_ui7_report.txt.</summary>
    public static void RunPlayUi7()
    {
        Directory.CreateDirectory(OutDir);
        string report = Path.Combine(OutDir, "bridge_play_ui7_report.txt");
        File.WriteAllText(report, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("BridgeTest.PlayUi7", report, 20, true, Launch.Replace("{\"app\":{\"autoStart\":true},", "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},"));
    }

    static IEnumerator PlayUi7()
    {
        Ui7Only = true;
        return PlayUi();
    }

    /// <summary>UI6 alone (Phase 4.3 integrator T): -executeMethod BridgeTest.RunPlayUi6 (-NoQuit; graphics); report bridge_play_ui6_report.txt.</summary>
    public static void RunPlayUi6()
    {
        Directory.CreateDirectory(OutDir);
        string report = Path.Combine(OutDir, "bridge_play_ui6_report.txt");
        File.WriteAllText(report, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("BridgeTest.PlayUi6", report, 20, true, Launch.Replace("{\"app\":{\"autoStart\":true},", "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},"));
    }

    static IEnumerator PlayUi6()
    {
        Ui6Only = true;
        return PlayUi();
    }

    /// <summary>UI5 alone (Phase 4.2 integrator T): -executeMethod BridgeTest.RunPlayUi5 (-NoQuit; graphics); report bridge_play_ui5_report.txt.</summary>
    public static void RunPlayUi5()
    {
        Directory.CreateDirectory(OutDir);
        string report = Path.Combine(OutDir, "bridge_play_ui5_report.txt");
        File.WriteAllText(report, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("BridgeTest.PlayUi5", report, 20, true, Launch.Replace("{\"app\":{\"autoStart\":true},", "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},"));
    }

    static IEnumerator PlayUi5()
    {
        Ui5Only = true;
        return PlayUi();
    }

    /// <summary>UI4 alone (Phase 4.1 integrator T): -executeMethod BridgeTest.RunPlayUi4 (-NoQuit; graphics); report bridge_play_ui4_report.txt.</summary>
    public static void RunPlayUi4()
    {
        Directory.CreateDirectory(OutDir);
        string report = Path.Combine(OutDir, "bridge_play_ui4_report.txt");
        File.WriteAllText(report, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("BridgeTest.PlayUi4", report, 20, true, Launch.Replace("{\"app\":{\"autoStart\":true},", "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},"));
    }

    static IEnumerator PlayUi4()
    {
        Ui4Only = true;
        return PlayUi();
    }

    /// <summary>UI3 alone (Phase 4.0 integrator T): -executeMethod BridgeTest.RunPlayUi3 (-NoQuit; graphics); report bridge_play_ui3_report.txt.</summary>
    public static void RunPlayUi3()
    {
        Directory.CreateDirectory(OutDir);
        string report = Path.Combine(OutDir, "bridge_play_ui3_report.txt");
        File.WriteAllText(report, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("BridgeTest.PlayUi3", report, 15, true, Launch.Replace("{\"app\":{\"autoStart\":true},", "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},"));
    }

    static IEnumerator PlayUi3()
    {
        Ui3Only = true;
        return PlayUi();
    }

    static IEnumerator PlayUi2()
    {
        Ui2Only = true;
        return PlayUi();
    }

    /// <summary>RT1 alone (Phase 3.6 package R): -executeMethod BridgeTest.RunPlayRoutes (-NoQuit; graphics).</summary>
    public static void RunPlayRoutes()
    {
        Directory.CreateDirectory(OutDir);
        string report = Path.Combine(OutDir, "bridge_play_rt1_report.txt");
        File.WriteAllText(report, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("BridgeTest.PlayRoutes", report, 20, true, Launch);
    }

    static IEnumerator PlayRoutes()
    {
        var c = new AcceptanceCtx();
        var total = System.Diagnostics.Stopwatch.StartNew();
        var targets = TestTargets.Load();
        c.FemaleRel = BridgeCoreTest.RelTo(TestTargets.Abs(targets.visual.female), Path.Combine(TestTargets.UserData, "chara", "female"));   // RT1's paused add
        c.Client = new BridgeClient("http://127.0.0.1:" + Port + "/");
        c.Rep.AppendLine($"Bridge play test RT1 (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}); {c.Url}");
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 300, ok);
        c.Check(ok[0], "app ready");
        yield return BridgeAcceptance.Guard(c, "RT1", BridgeAcceptance.Routes(c));
        c.Dispose();
        File.WriteAllText(Path.Combine(OutDir, "bridge_play_rt1_report.txt"), c.Report(total.Elapsed.TotalSeconds), new UTF8Encoding(false));
        Debug.Log($"BRIDGE PLAY RT1 DONE: {c.Passed} PASS, {c.Failed} FAIL, {c.Warned} WARN");
        PlayModeHarness.ExitCode = c.Failed > 0 ? 1 : 0;
    }

    static IEnumerator PlayMain()
    {
        var c = new AcceptanceCtx();
        var total = System.Diagnostics.Stopwatch.StartNew();
        var targets = TestTargets.Load();
        c.FemaleRel = BridgeCoreTest.RelTo(TestTargets.Abs(targets.visual.female), Path.Combine(TestTargets.UserData, "chara", "female"));
        c.Client = new BridgeClient("http://127.0.0.1:" + Port + "/");
        c.Rep.AppendLine($"Bridge play test (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}); {c.Url}; play mode entered after {PlayModeHarness.EnterSeconds:F1} s");

        // A1–A14: the runtime acceptance flow
        var o = new AcceptanceOptions { Url = c.Url, CardPath = c.FemaleRel, Sex = "female" };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        yield return BridgeAcceptance.Guard(c, "A", BridgeAcceptance.Run(o, c.Rep, c.CheckLine));
        c.Info($"acceptance: {sw.Elapsed.TotalSeconds:F1} s (ready after {o.Result.ReadySeconds:F1} s)");
        c.FemaleKey = o.Result.CharaKey;

        // S1–S6 (runtime) and H1–H5 (Editor helpers), after a completed acceptance
        var parts = new List<(string id, Func<AcceptanceCtx, IEnumerator> run)>
        {
            ("Motion", BridgeAcceptance.AutoStep),
            ("Shape", Shape),
        };
        foreach (var (id, run) in parts)
        {
            if (!o.Result.Completed) { c.Id = id; c.Warn(id + " skipped: the acceptance did not complete"); continue; }
            sw.Restart();
            yield return BridgeAcceptance.Guard(c, id, run(c));
            c.Info($"{id}: {sw.Elapsed.TotalSeconds:F1} s");
        }

        // E1–E3 and the render-parity shots T5 (runtime; the player compares its T5 shots with these)
        sw.Restart();
        yield return BridgeAcceptance.Errors(c);
        c.Info($"E1–E3: {sw.Elapsed.TotalSeconds:F1} s");
        sw.Restart();
        yield return BridgeAcceptance.Guard(c, "T5", BridgeAcceptance.Parity(c));
        c.Info($"T5: {sw.Elapsed.TotalSeconds:F1} s");
        sw.Restart();
        yield return BridgeAcceptance.Guard(c, "I1", BridgeAcceptance.Items(c));
        c.Info($"I1: {sw.Elapsed.TotalSeconds:F1} s");
        sw.Restart();
        yield return BridgeAcceptance.Guard(c, "M1", BridgeAcceptance.Maps(c));
        c.Info($"M1: {sw.Elapsed.TotalSeconds:F1} s");
        sw.Restart();
        yield return BridgeAcceptance.Guard(c, "LT1", BridgeAcceptance.Lights(c));   // Phase 3.3 package A
        c.Info($"LT1: {sw.Elapsed.TotalSeconds:F1} s");
        sw.Restart();
        yield return BridgeAcceptance.Guard(c, "FX1", BridgeAcceptance.Effects(c));   // Phase 3.3 package B
        c.Info($"FX1: {sw.Elapsed.TotalSeconds:F1} s");
        sw.Restart();
        yield return BridgeAcceptance.Guard(c, "IK1", BridgeAcceptance.Kinematics(c));   // Phase 3.4 package C
        c.Info($"IK1: {sw.Elapsed.TotalSeconds:F1} s");
        sw.Restart();
        yield return BridgeAcceptance.Guard(c, "UI1", BridgeAcceptance.Ui(c));   // Phase 3.5 package A (skipped without -ui; RunPlayUi runs it alone)
        c.Info($"UI1: {sw.Elapsed.TotalSeconds:F1} s");
        sw.Restart();
        yield return BridgeAcceptance.Guard(c, "RT1", BridgeAcceptance.Routes(c));   // Phase 3.6 package R (RunPlayRoutes runs it alone)
        c.Info($"RT1: {sw.Elapsed.TotalSeconds:F1} s");
        sw.Restart();
        BridgeAcceptance.Ui2MaleRel = BridgeCoreTest.RelTo(TestTargets.Abs(targets.visual.male), Path.Combine(TestTargets.UserData, "chara", "male"));
        yield return BridgeAcceptance.Guard(c, "UI2", BridgeAcceptance.Ui2(c));   // Phase 3.6 integrator (skipped without -ui; RunPlayUi runs it after UI1)
        c.Info($"UI2: {sw.Elapsed.TotalSeconds:F1} s");

        c.Dispose();
        File.WriteAllText(ReportPath, c.Report(total.Elapsed.TotalSeconds), new UTF8Encoding(false));
        Debug.Log($"BRIDGE PLAY TEST DONE: {c.Passed} PASS, {c.Failed} FAIL, {c.Warned} WARN");
        PlayModeHarness.ExitCode = c.Failed > 0 ? 1 : 0;
    }
}
