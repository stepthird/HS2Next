// Play-mode test harness for batch runs without the Unity Test Framework (open
// point D1). An -executeMethod entry calls Start("<Type>.<Method>", reportPath): the name of a static method returning
// IEnumerator is stored in SessionState (it survives the domain reload of entering play mode) and play mode is
// entered. After the reload this class (InitializeOnLoad) sees the request; on EnteredPlayMode it runs the coroutine
// from EditorApplication.update, one MoveNext per editor tick (= one player-loop frame in play mode), and ends the
// editor with EditorApplication.Exit(ExitCode) when the coroutine finishes (1 when it throws).
// The batch runner must start Unity without -quit: Tools/unity_batch.ps1 -NoQuit.
//
// Yield values: null (next tick), an AsyncOperation (until isDone), an IEnumerator (nested, including
// CustomYieldInstruction), WaitFrames(n) (until Time.frameCount advanced by n), WaitRealtime(s). Other values are
// treated as null. A driver MonoBehaviour is not used: MonoBehaviours of the Editor assembly cannot be relied on as
// components, and the pump needs none (the scene's own drivers run in the player loop as usual).
//
// Guards: play mode not entered within EnterTimeoutSec of the request → exit 3; the coroutine still running after
// the guard minutes (default 20) → exit 2; a background watchdog kills the process 3 minutes after that in case the
// main thread hangs. Guard exits append one line to the report file ("RESULT: FAIL (...)").
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

[InitializeOnLoad]
public static class PlayModeHarness
{
    const string KeyMethod = "HS2Next.PlayModeHarness.Method";
    const string KeyReport = "HS2Next.PlayModeHarness.Report";
    const string KeyRequestedUtc = "HS2Next.PlayModeHarness.RequestedUtc";
    const string KeyGuardMin = "HS2Next.PlayModeHarness.GuardMinutes";
    const string KeyStage = "HS2Next.PlayModeHarness.Stage";   // "requested" | "running"
    // The app launch override read by AppBootstrap.AutoStart: HS2Next.App.AppLaunch.SessionKey.
    const string KeyAppLaunch = HS2Next.App.AppLaunch.SessionKey;

    /// <summary>Seconds after the request within which play mode must have been entered.</summary>
    public const double EnterTimeoutSec = 300;

    /// <summary>Exit code used when the coroutine completes (the test sets it; default 0).</summary>
    public static int ExitCode;

    /// <summary>Realtime (seconds since startup) when the coroutine started; frame count then.</summary>
    public static float StartedRealtime { get; private set; }
    public static int StartedFrame { get; private set; }

    /// <summary>Wall-clock time from the request (executeMethod) to EnteredPlayMode, seconds (-1 before).</summary>
    public static double EnterSeconds { get; private set; } = -1;

    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static AsyncOperation waitingOp;
    static bool running, pumping, guardInstalled;
    static Thread watchdog;

    static PlayModeHarness()
    {
        if (string.IsNullOrEmpty(SessionState.GetString(KeyMethod, ""))) return;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        InstallGuard();
        // Entered play mode without a domain reload, or the event was raised before this ran: start from the tick.
        if (EditorApplication.isPlaying && SessionState.GetString(KeyStage, "") == "requested")
            EditorApplication.delayCall += () => { if (!running && EditorApplication.isPlaying) Begin(); };
    }

    /// <summary>
    /// Requests a play-mode run of the static method <paramref name="method"/> ("Type.Method" or "Namespace.Type.Method";
    /// no parameters, returns IEnumerator) in a new empty scene. Call from an -executeMethod entry of a batch run without -quit.
    /// <paramref name="appLaunchJson"/> is the HS2Next app's launch override for the session (
    /// the schema of the settings file's userData / app / bridge members): by default the app stays off, so play tests run
    /// without it; BridgeTest and BridgeServe pass one with "autoStart": true.
    /// </summary>
    public static void Start(string method, string reportPath, double guardMinutes = 20, bool emptyScene = true,
        string appLaunchJson = HS2Next.App.AppLaunch.Off)
    {
        if (Resolve(method) == null) throw new ArgumentException("PlayModeHarness: no static IEnumerator method " + method);
        // Play in a new empty scene, never in whatever scene the editor opened (e.g. the player smoke scene).
        if (emptyScene) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetString(KeyMethod, method);
        SessionState.SetString(KeyReport, reportPath ?? "");
        SessionState.SetString(KeyRequestedUtc, DateTime.UtcNow.Ticks.ToString());
        SessionState.SetString(KeyGuardMin, guardMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SessionState.SetString(KeyStage, "requested");
        SessionState.SetString(KeyAppLaunch, string.IsNullOrEmpty(appLaunchJson) ? HS2Next.App.AppLaunch.Off : appLaunchJson);
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        InstallGuard();
        Debug.Log($"PlayModeHarness: entering play mode for {method} (batch {Application.isBatchMode}, graphics {SystemInfo.graphicsDeviceType})");
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayModeStateChanged(PlayModeStateChange s)
    {
        Debug.Log("PlayModeHarness: " + s);
        if (s == PlayModeStateChange.EnteredPlayMode && !running && SessionState.GetString(KeyStage, "") == "requested") Begin();
        else if (s == PlayModeStateChange.EnteredEditMode && SessionState.GetString(KeyStage, "") == "running")
            Fail(4, "play mode was left before the test finished");
    }

    static DateTime RequestedUtc =>
        long.TryParse(SessionState.GetString(KeyRequestedUtc, ""), out var t) ? new DateTime(t, DateTimeKind.Utc) : DateTime.UtcNow;

    static double GuardMinutes =>
        double.TryParse(SessionState.GetString(KeyGuardMin, ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var m) ? m : 20;

    static void InstallGuard()
    {
        if (guardInstalled) return;
        guardInstalled = true;
        EditorApplication.update += Guard;
    }

    static void Guard()
    {
        string stage = SessionState.GetString(KeyStage, "");
        if (stage.Length == 0) return;
        double elapsed = (DateTime.UtcNow - RequestedUtc).TotalSeconds;
        if (stage == "requested" && !EditorApplication.isPlaying && elapsed > EnterTimeoutSec)
            Fail(3, $"play mode was not entered within {EnterTimeoutSec:F0} s");
        else if (elapsed > GuardMinutes * 60)
            Fail(2, $"play-mode guard: still running after {GuardMinutes:F0} min");
    }

    static void Begin()
    {
        var m = Resolve(SessionState.GetString(KeyMethod, ""));
        if (m == null) { Fail(1, "test method not found after the domain reload: " + SessionState.GetString(KeyMethod, "")); return; }
        running = true;
        SessionState.SetString(KeyStage, "running");
        EnterSeconds = (DateTime.UtcNow - RequestedUtc).TotalSeconds;
        StartedRealtime = Time.realtimeSinceStartup;
        StartedFrame = Time.frameCount;
        Application.runInBackground = true;
        StartWatchdog();
        Debug.Log($"PlayModeHarness: play mode entered after {EnterSeconds:F1} s; running {m.DeclaringType?.Name}.{m.Name}");
        stack.Clear();
        waitingOp = null;
        try { stack.Push((IEnumerator)m.Invoke(null, null)); }
        catch (Exception e) { Fail(1, "test method threw: " + (e.InnerException ?? e)); return; }
        EditorApplication.update += Pump;
    }

    static void Pump()
    {
        if (!running || pumping) return;
        pumping = true;
        try
        {
            if (waitingOp != null)
            {
                if (!waitingOp.isDone) return;
                waitingOp = null;
            }
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                bool more;
                try { more = top.MoveNext(); }
                catch (Exception e) { Fail(1, "test coroutine threw: " + e); return; }
                if (!more) { stack.Pop(); continue; }
                var y = top.Current;
                if (y is AsyncOperation op) { if (!op.isDone) waitingOp = op; return; }
                if (y is IEnumerator nested) { stack.Push(nested); continue; }
                return;   // null or anything else: next tick
            }
            Finish(ExitCode);
        }
        finally { pumping = false; }
    }

    /// <summary>Waits until <see cref="Time.frameCount"/> advanced by <paramref name="frames"/>.</summary>
    public static IEnumerator WaitFrames(int frames)
    {
        int target = Time.frameCount + Math.Max(0, frames);
        while (Time.frameCount < target) yield return null;
    }

    /// <summary>Waits <paramref name="seconds"/> of real time.</summary>
    public static IEnumerator WaitRealtime(double seconds)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < seconds) yield return null;
    }

    /// <summary>Waits until <paramref name="done"/> returns true or <paramref name="timeoutSec"/> passes; <paramref name="result"/>[0] = done.</summary>
    public static IEnumerator WaitUntil(Func<bool> done, double timeoutSec, bool[] result)
    {
        var sw = Stopwatch.StartNew();
        while (!done())
        {
            if (sw.Elapsed.TotalSeconds > timeoutSec) { if (result != null && result.Length > 0) result[0] = false; yield break; }
            yield return null;
        }
        if (result != null && result.Length > 0) result[0] = true;
    }

    /// <summary>Ends the run now with <paramref name="code"/> (also what the harness does when the coroutine ends).</summary>
    public static void Finish(int code)
    {
        Stop();
        Debug.Log($"PlayModeHarness: finished, exit {code}");
        EditorApplication.Exit(code);
    }

    static void Fail(int code, string why)
    {
        Debug.LogError("PlayModeHarness: " + why);
        string report = SessionState.GetString(KeyReport, "");
        if (report.Length > 0)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(report));
                File.AppendAllText(report, $"\nRESULT: FAIL (harness exit {code}: {why.Split('\n')[0]})\n", new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogWarning("PlayModeHarness: report append failed: " + e.Message); }
        }
        Finish(code);
    }

    static void Stop()
    {
        running = false;
        EditorApplication.update -= Pump;
        foreach (var k in new[] { KeyMethod, KeyReport, KeyRequestedUtc, KeyGuardMin, KeyStage, KeyAppLaunch }) SessionState.EraseString(k);
        stack.Clear();
        waitingOp = null;
    }

    static void StartWatchdog()
    {
        if (watchdog != null) return;
        double limitSec = GuardMinutes * 60 + 180 - (DateTime.UtcNow - RequestedUtc).TotalSeconds;
        int pid = Process.GetCurrentProcess().Id;
        watchdog = new Thread(() =>
        {
            try
            {
                Thread.Sleep(TimeSpan.FromSeconds(Math.Max(60, limitSec)));
                Process.GetProcessById(pid).Kill();
            }
            catch (ThreadAbortException) { }
            catch (ThreadInterruptedException) { }
            catch (Exception) { }
        }) { IsBackground = true, Name = "PlayModeHarness watchdog" };
        watchdog.Start();
    }

    static MethodInfo Resolve(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        int dot = name.LastIndexOf('.');
        if (dot <= 0) return null;
        string typeName = name.Substring(0, dot), method = name.Substring(dot + 1);
        // Tests live in the Editor assembly (this one) or in Assembly-CSharp.
        var t = typeof(PlayModeHarness).Assembly.GetType(typeName, false) ?? Type.GetType(typeName + ", Assembly-CSharp", false);
        var m = t?.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        return m != null && typeof(IEnumerator).IsAssignableFrom(m.ReturnType) ? m : null;
    }
}
