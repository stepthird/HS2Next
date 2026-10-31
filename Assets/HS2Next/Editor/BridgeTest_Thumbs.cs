// TS alone (v0.3 trial 2, package G; Runtime/App/BridgeAcceptance_ThumbScroll.cs): every thumbnail list of every Maker page
// scrolled from top to bottom through ui.* for the female and the male SFW target, in Editor play with the app and the UI.
// Run: & Tools\unity_batch.ps1 -Method BridgeTest.RunPlayThumbs -NoQuit -TimeoutMin 70 [-MethodArgs @('-thumbDiskCache','off')]
// (graphics; -thumbDiskCache off loads every thumbnail from its bundle, -thumbHeaderCheck off turns the header check off).
// Report: Spike/out/bridge/bridge_play_ts_report.txt ("SUMMARY: N PASS, M FAIL").
using System.Collections;
using System.IO;
using System.Text;
using HS2Next.App;
using UnityEngine;

public static partial class BridgeTest
{
    public static void RunPlayThumbs()
    {
        Directory.CreateDirectory(OutDir);
        string report = Path.Combine(OutDir, "bridge_play_ts_report.txt");
        File.WriteAllText(report, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("BridgeTest.PlayThumbs", report, 65, true, Launch.Replace("{\"app\":{\"autoStart\":true},", "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},"));
    }

    static IEnumerator PlayThumbs()
    {
        var c = new AcceptanceCtx();
        var total = System.Diagnostics.Stopwatch.StartNew();
        var targets = TestTargets.Load();
        c.FemaleRel = BridgeCoreTest.RelTo(TestTargets.Abs(targets.visual.female), Path.Combine(TestTargets.UserData, "chara", "female"));
        BridgeAcceptance.Ui2MaleRel = BridgeCoreTest.RelTo(TestTargets.Abs(targets.visual.male), Path.Combine(TestTargets.UserData, "chara", "male"));
        c.Client = new BridgeClient("http://127.0.0.1:" + Port + "/");
        c.Rep.AppendLine($"Bridge play test TS (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}); {c.Url}; header check {HS2Next.UI.Panels.Maker.PartThumbnails.HeaderCheck}, disk cache {HS2Next.UI.Panels.Maker.PartThumbnails.DiskCacheDefault}");
        var ok = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting && HS2Next.UI.UiHost.Current != null && HS2Next.UI.UiHost.Current.IsReady, 300, ok);
        c.Check(ok[0], "app and UI ready");
        yield return BridgeAcceptance.Guard(c, "TS", BridgeAcceptance.ThumbScroll(c));
        c.Info($"TS: {total.Elapsed.TotalSeconds:F1} s");
        c.Dispose();
        File.WriteAllText(Path.Combine(OutDir, "bridge_play_ts_report.txt"), c.Report(total.Elapsed.TotalSeconds), new UTF8Encoding(false));
        Debug.Log($"BRIDGE PLAY TS DONE: {c.Passed} PASS, {c.Failed} FAIL, {c.Warned} WARN");
        PlayModeHarness.ExitCode = c.Failed > 0 ? 1 : 0;
    }
}
