// Editor serve entry: keeps a play-mode app with its bridge running for external clients
// (Tools/hs2_compare.py --target hs2next, Tools/hs2n_bridge.py, agents). Enters play mode through PlayModeHarness with a
// launch override that turns the app on and allows app.quit, waits until the app is ready, then until app.quit ends
// the process or -serveMinutes (default 60) passed. The port is the settings' (default 17322; -bridgePort N on the
// command line). Graphics are on, so captures work.
// Run: & Tools\unity_batch.ps1 -Method BridgeServe.Run -NoQuit [-MethodArgs @('-serveMinutes','90')] (long runs: detached)
// Stop: {"cmd":"app.quit"} (exit code = its code), or the time limit (exit 0). Report: Spike/out/bridge/serve_report.txt.
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using HS2Next.App;
using HS2Next.Content;
using UnityEngine;

public static class BridgeServe
{
    const string Launch = "{\"app\":{\"autoStart\":true},\"bridge\":{\"allowQuit\":true}}";
    static string ReportPath => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "bridge", "serve_report.txt");

    static double ServeMinutes
    {
        get
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, "-serveMinutes");
            return i >= 0 && i + 1 < a.Length && double.TryParse(a[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var m) && m > 0 ? m : 60;
        }
    }

    /// <summary>-executeMethod entry (Tools/unity_batch.ps1 -NoQuit).</summary>
    public static void Run()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        double minutes = ServeMinutes;
        File.WriteAllText(ReportPath, $"BridgeServe: requested {DateTime.Now:yyyy-MM-dd HH:mm:ss}, {minutes:F0} min\n", new UTF8Encoding(false));
        PlayModeHarness.Start("BridgeServe.Serve", ReportPath, minutes + 5, true, Launch);
    }

    static void Line(string s)
    {
        Debug.Log("BridgeServe: " + s);
        try { File.AppendAllText(ReportPath, s + "\n", new UTF8Encoding(false)); } catch (Exception) { }
    }

    static IEnumerator Serve()
    {
        double minutes = ServeMinutes;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        AppCore app = null;
        while (sw.Elapsed.TotalSeconds < 60 && (app = AppCore.Current) == null) yield return null;
        if (app == null)
        {
            Line("RESULT: FAIL (no app started; is app.autoStart off in the settings?)");
            PlayModeHarness.Finish(1);
            yield break;
        }
        while (app.Stage == AppStage.Starting && sw.Elapsed.TotalMinutes < minutes) yield return null;
        if (app.Stage == AppStage.Ready)
            Line($"ready after {sw.Elapsed.TotalSeconds:F1} s; listening on {app.Bridge?.Url ?? "(not listening: " + app.Bridge?.BindError + ")"}; serving up to {minutes:F0} min (stop with app.quit)");
        else Line($"startup failed after {sw.Elapsed.TotalSeconds:F1} s: {app.Error}; the bridge stays up for log.tail");
        while (sw.Elapsed.TotalMinutes < minutes && !app.IsDisposed) yield return null;
        Line(app.IsDisposed ? "the app was disposed; ending" : $"time limit ({minutes:F0} min) reached; ending");
        PlayModeHarness.Finish(0);
    }
}
