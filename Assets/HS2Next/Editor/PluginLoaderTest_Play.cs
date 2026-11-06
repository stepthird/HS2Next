// Plugin loading in Editor play mode (the loader's first measurement and the play row). Two play
// sessions of the app with a staged plugin folder (-pluginsDir) on one bridge port: the first with a domain reload, the
// second without one (Enter Play Mode Options). The checks run over HTTP from a worker thread (PlayCtx) while the editor
// plays frames.
//   Step0: T3a / T3b / T3f / T3g with the probes (the first Unity measurement; -nographics is fine).
//   Run (the row PluginLoaderTest.play, graphics, port 17398, the UI on): session 1 runs T3A (the probe loads, answers, adds
//   its MonoBehaviour, resolves its lib dependency, uses the host's JSON library; the app-assembly probe runs), PL13 (UI
//   registration: a panel in ui.state with its localised tab, an inspector section on a selected item, an action through
//   ui.action), every static SQ<n>(PlayCtx) of this partial class (the sample's checks, package S's file), PL14 (leak rows:
//   two warm-up and 20 measured cycles of scene.load of the sequence fixture, seq.play, 60 frames, seq.stop, scene.new with
//   plugin.list diagnostics; each cycle's info line names the GameObjects and Materials that differ from cycle -1 (app.perf census);
//   gameObjects must not grow, and a cycle below cycle 0's count is allowed only where the upkeep's UnloadUnusedAssets started);
//   session 2 runs T3B (the images are reused without a domain reload). Phase 6.1: after PL13 every static KU<n>, KE<n>, KD<n>,
//   KB<n>(PlayCtx) of PluginLoaderTest_Forms.cs (forms, widgets, capture events, the Options toggle, the device body checks).
// Args: -pluginsDir <dir> (default Spike/out/plugins/play; staged with Tools/plugins.ps1 when a plugin is missing or with
// -stage), -port N, -report <file>, -only T3A,PL13,SQ5,... (Run). Report: first line "SUMMARY: N PASS, M FAIL"; exit 0 / 1.
//   & Tools\unity_batch.ps1 -Method PluginLoaderTest.Run -NoQuit -TimeoutMin 20 [-MethodArgs @('-only','PL14')]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HS2Next.App;
using HS2Next.App.Plugins;
using HS2Next.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

[InitializeOnLoad]
public static partial class PluginLoaderTest
{
    const string K = "HS2Next.PluginLoaderTest.";
    /// <summary>The plugins the play row stages (the sample, the probes).</summary>
    public static readonly string[] PlayPlugins = { "stp3.sample.sequencer", "stp3.test.probe", "stp3.test.probecore", "stp3.test.probeui", "stp3.test.dependent",
        "stp3.test.formprobe", "stp3.test.historyprobe", "stp3.test.bodyprobe" };
    /// <summary>The play row touches this plugin's DLL between the sessions: the second session refuses it as stale.</summary>
    const string StaleGuid = "stp3.test.dependent";
    static bool hooked;
    static int step;
    static Task<List<string>> work;
    static double sessionStart;
    static Thread watchdog;

    static PluginLoaderTest()
    {
        if (!SessionState.GetBool(K + "active", false)) return;
        Hook();
        StartWatchdog();
    }

    static string ProjectOut => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "plugins");
    static string Report => SessionState.GetString(K + "report", Path.Combine(ProjectOut, "step0_editor.txt"));
    static int Port => SessionState.GetInt(K + "port", 17491);
    static string Mode => SessionState.GetString(K + "mode", "step0");
    static int Phase { get => SessionState.GetInt(K + "phase", 0); set => SessionState.SetInt(K + "phase", value); }

    static void Line(string s)
    {
        Debug.Log("PluginLoaderTest: " + s);
        try { File.AppendAllText(Report, s + "\n", new UTF8Encoding(false)); } catch (Exception) { }
    }

    static string Arg(string[] a, string name, string fallback)
    {
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : fallback;
    }

    /// <summary>-executeMethod entry of the loader's first measurement (without -quit).</summary>
    public static void Step0() => Begin("step0", 17491, Path.Combine(ProjectOut, "step0_editor.txt"), null, false);

    /// <summary>-executeMethod entry of the play row (without -quit).</summary>
    public static void Run() => Begin("play", 17398, Path.Combine(ProjectOut, "plugin_loader_play_report.txt"), Path.Combine(ProjectOut, "play"), true);

    static void Begin(string mode, int defaultPort, string defaultReport, string defaultDir, bool ui)
    {
        var a = Environment.GetCommandLineArgs();
        string dir = Arg(a, "-pluginsDir", defaultDir);
        int port = int.Parse(Arg(a, "-port", defaultPort.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
        string report = Path.GetFullPath(Arg(a, "-report", defaultReport));
        Directory.CreateDirectory(Path.GetDirectoryName(report));
        File.WriteAllText(report, "", new UTF8Encoding(false));
        SessionState.SetString(K + "report", report);
        SessionState.SetString(K + "mode", mode);
        SessionState.SetString(K + "only", Arg(a, "-only", ""));
        SessionState.SetInt(K + "port", port);
        SessionState.SetBool(K + "active", true);
        SessionState.SetInt(K + "pass", 0);
        SessionState.SetInt(K + "fail", 0);
        SessionState.SetString(K + "startedUtc", DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
        SessionState.SetBool(K + "optEnabled", EditorSettings.enterPlayModeOptionsEnabled);
        SessionState.SetInt(K + "options", (int)EditorSettings.enterPlayModeOptions);
        Line($"PluginLoaderTest {mode} (Editor play mode): Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, -pluginsDir {dir}, port {port}, load call {PluginHost.LoadCall}");
        if (string.IsNullOrEmpty(dir)) { Count(false, "setup", "-pluginsDir is required"); Finish(); return; }
        if (mode == "play" && !StageIfNeeded(dir, PlayPlugins, Array.IndexOf(a, "-stage") >= 0)) { Count(false, "setup", "staging the plugins failed"); Finish(); return; }
        SessionState.SetString(K + "dir", Path.GetFullPath(dir));
        string userData = Path.Combine(Path.GetDirectoryName(report), mode + "_userdata");
        SessionState.SetString(K + "userData", userData);
        string launch = new JObject
        {
            ["userData"] = userData,
            ["pluginsDir"] = Path.GetFullPath(dir),
            ["app"] = new JObject { ["autoStart"] = true },
            ["bridge"] = new JObject { ["port"] = port, ["allowQuit"] = false },
            ["ui"] = new JObject { ["enabled"] = ui },
        }.ToString(Formatting.None);
        SessionState.SetString(AppLaunch.SessionKey, launch);
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.None;
        Phase = 1;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        Hook();
        StartWatchdog();
        EditorApplication.EnterPlaymode();
    }

    /// <summary>Runs Tools/plugins.ps1 -Build all -Stage &lt;dir&gt; -Only &lt;guids&gt; when a plugin is missing (or when forced).</summary>
    internal static bool StageIfNeeded(string dir, string[] guids, bool force)
    {
        if (!force && guids.All(g => File.Exists(Path.Combine(dir, g, "manifest.json")))) return true;
        string tool = Path.Combine(ContentSettings.ProjectRoot, "Tools", "plugins.ps1");
        var psi = new ProcessStartInfo("pwsh", "-NoProfile -File \"" + tool + "\" -Build all -Stage \"" + dir + "\" -Only " + string.Join(",", guids))
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = ContentSettings.ProjectRoot,
        };
        using (var p = Process.Start(psi))
        {
            string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            bool exited = p.WaitForExit(300000);
            Line("staged: plugins.ps1 exit " + (exited ? p.ExitCode.ToString() : "timeout") + (exited && p.ExitCode == 0 ? "" : ": " + o));
            return exited && p.ExitCode == 0;
        }
    }

    static void Hook()
    {
        if (hooked) return;
        hooked = true;
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.update += Tick;
    }

    static void StartWatchdog()
    {
        if (watchdog != null) return;
        long.TryParse(SessionState.GetString(K + "startedUtc", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out long t0);
        var start = t0 > 0 ? new DateTime(t0, DateTimeKind.Utc) : DateTime.UtcNow;
        watchdog = new Thread(() =>
        {
            while ((DateTime.UtcNow - start).TotalMinutes < 18) Thread.Sleep(1000);
            try { File.AppendAllText(Report, "RESULT: FAIL (watchdog: 18 minutes)\n"); } catch (Exception) { }
            Process.GetCurrentProcess().Kill();
        }) { IsBackground = true };
        watchdog.Start();
    }

    static void Count(bool ok, string id, string what)
    {
        string key = ok ? "pass" : "fail";
        SessionState.SetInt(K + key, SessionState.GetInt(K + key, 0) + 1);
        Line((ok ? "PASS " : "FAIL ") + id + ": " + what);
    }

    static void OnPlayMode(PlayModeStateChange s)
    {
        if (!SessionState.GetBool(K + "active", false)) return;
        if (s == PlayModeStateChange.EnteredPlayMode)
        {
            step = 0;
            work = null;
            sessionStart = EditorApplication.timeSinceStartup;
            Line("session " + Phase + " entered play mode (domain reload " + ((EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0 ? "on" : "off") + ")");
        }
        else if (s == PlayModeStateChange.EnteredEditMode)
        {
            if (Phase == 1 && Wants("T3B"))
            {
                Phase = 2;
                if (Mode == "play")
                {
                    // the play row changes one loaded plugin's file between the sessions (its write time): without a domain reload
                    // the loaded image no longer matches, and the second session must refuse it (plugin.staleAssembly)
                    string dll = Path.Combine(SessionState.GetString(K + "dir", ""), StaleGuid, "Stp3.Test.Dependent.dll");
                    try
                    {
                        if (File.Exists(dll)) File.SetLastWriteTimeUtc(dll, DateTime.UtcNow);
                        Line("touched " + dll + " (write time) for the stale check");
                    }
                    catch (Exception e) { Line("could not touch " + dll + ": " + e.Message); }
                }
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
                EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
            }
            else Finish();
        }
    }

    /// <summary>A part is wanted when -only is absent or names it (Step0 runs everything).</summary>
    static bool Wants(string id)
    {
        string only = SessionState.GetString(K + "only", "");
        if (Mode != "play" || string.IsNullOrEmpty(only)) return true;
        return only.Split(',', '+', ';').Select(x => x.Trim()).Contains(id, StringComparer.OrdinalIgnoreCase);
    }

    static void Tick()
    {
        if (!SessionState.GetBool(K + "active", false) || !EditorApplication.isPlaying) return;
        PumpMain();   // main-thread work of the checks (PluginLoaderTest_Forms.cs)
        double t = EditorApplication.timeSinceStartup - sessionStart;
        var app = AppCore.Current;
        switch (step)
        {
            case 0:
                if (app != null && app.Stage != AppStage.Starting)
                {
                    Line($"session {Phase}: app {app.Stage} after {t:F1} s{(app.Error != null ? " (" + app.Error + ")" : "")}; {app.Plugins?.Summary}");
                    if (app.Stage != AppStage.Ready) { Count(false, "app", "the app did not start: " + app.Error); step = 3; break; }
                    var reg = app.Bridge?.Services.Commands;
                    if (reg != null && !reg.Contains("plugin.list")) { PluginCommands.Register(reg, app); Line("plugin.list registered by the test (CoreCommands does not register it)"); }
                    string only = SessionState.GetString(K + "only", ""), mode = Mode;
                    var pc = new PlayCtx
                    {
                        Port = Port, Phase = Phase, PluginsDir = SessionState.GetString(K + "dir", ""), UserData = SessionState.GetString(K + "userData", ""),
                        OutDir = Path.GetDirectoryName(Report), ProjectRoot = ContentSettings.ProjectRoot,
                        Wanted = id => mode != "play" || string.IsNullOrEmpty(only) || only.Split(',', '+', ';').Select(x => x.Trim()).Contains(id, StringComparer.OrdinalIgnoreCase),
                    };
                    work = Task.Run(() => mode == "play" ? PlayChecks(pc) : Step0Checks(pc));
                    step = 1;
                }
                else if (t > 180) { Count(false, "app", "the app was not ready after 180 s"); step = 3; }
                break;
            case 1:
                if (work.IsCompleted)
                {
                    if (work.IsFaulted) Count(false, "session " + Phase, "the checks threw " + work.Exception?.GetBaseException());
                    else foreach (var l in work.Result)
                    {
                        if (l.StartsWith("PASS ", StringComparison.Ordinal) || l.StartsWith("FAIL ", StringComparison.Ordinal)) Count(l.StartsWith("PASS", StringComparison.Ordinal), "session " + Phase, l.Substring(5));
                        else Line("   " + l);
                    }
                    step = 3;
                }
                else if (t > 600) { Count(false, "session " + Phase, "the checks did not end within 600 s"); step = 3; }
                break;
            case 3:
                step = 4;
                EditorApplication.ExitPlaymode();
                break;
        }
    }

    static void Finish()
    {
        EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(K + "optEnabled", true);
        EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(K + "options", 0);
        int pass = SessionState.GetInt(K + "pass", 0), fail = SessionState.GetInt(K + "fail", 0);
        SessionState.SetBool(K + "active", false);
        SessionState.EraseString(AppLaunch.SessionKey);
        string body = File.Exists(Report) ? File.ReadAllText(Report) : "";
        File.WriteAllText(Report, $"SUMMARY: {pass} PASS, {fail} FAIL\n" + body, new UTF8Encoding(false));
        Debug.Log($"PLUGIN LOADER {Mode.ToUpperInvariant()} DONE: {pass} PASS, {fail} FAIL");
        EditorApplication.Exit(fail > 0 || pass == 0 ? 1 : 0);
    }

    // ================================================================ the worker side

    /// <summary>
    /// What a play check works with (on a worker thread; HTTP to the session's bridge only, no Unity API): Call / Ok / Fails send
    /// one request; Check / Info write report lines (the part's id is prefixed); WaitFrames waits for the app's frames.
    /// </summary>
    public sealed class PlayCtx
    {
        readonly List<string> lines = new List<string>();
        /// <summary>The session's bridge port.</summary>
        public int Port;
        /// <summary>1 = the session with a domain reload, 2 = without.</summary>
        public int Phase;
        /// <summary>The staged plugin folder.</summary>
        public string PluginsDir;
        /// <summary>The app's UserData of this run (scene files go to UserData/scene).</summary>
        public string UserData;
        /// <summary>The report's folder (for a part's own files).</summary>
        public string OutDir;
        /// <summary>The project folder (fixtures are under Tests/).</summary>
        public string ProjectRoot;
        /// <summary>The running part ("PL13", "SQ5").</summary>
        public string Id = "";
        internal Func<string, bool> Wanted = _ => true;
        public string LastText { get; private set; }
        public int Passed { get; private set; }
        public int Failed { get; private set; }

        public List<string> Lines { get { lock (lines) return new List<string>(lines); } }

        /// <summary>The scene folder of this run's UserData.</summary>
        public string SceneDir => Path.Combine(UserData, "scene");

        public JObject Call(object request, int timeoutMs = 60000) => Call(request is string s ? s : JsonConvert.SerializeObject(request), timeoutMs);

        public JObject Call(string json, int timeoutMs = 60000)
        {
            var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + Port + "/");
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.Proxy = null;
            var body = Encoding.UTF8.GetBytes(json);
            req.ContentLength = body.Length;
            using (var s = req.GetRequestStream()) s.Write(body, 0, body.Length);
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                LastText = r.ReadToEnd();
                return JObject.Parse(LastText);
            }
        }

        public static bool IsOk(JObject r) => r != null && r["ok"]?.Type == JTokenType.Boolean && (bool)r["ok"];

        public JObject Ok(object request, string what)
        {
            var r = Call(request);
            Check(IsOk(r), what, IsOk(r) ? "" : Short(LastText));
            return r;
        }

        public JObject Fails(object request, string code, string what)
        {
            var r = Call(request);
            Check(!IsOk(r) && (string)r["code"] == code, what, "code " + (string)r["code"] + " (" + Short(LastText) + ")");
            return r;
        }

        public bool Check(bool ok, string what, string detail = "")
        {
            if (ok) Passed++; else Failed++;
            lock (lines) lines.Add((ok ? "PASS " : "FAIL ") + Id + " " + what + (string.IsNullOrEmpty(detail) ? "" : "  " + detail));
            return ok;
        }

        public void Info(string line) { lock (lines) lines.Add(Id + " " + line); }

        /// <summary>The app's frame (ping).</summary>
        public int Frame() => (int?)Call("{\"cmd\":\"ping\"}")["frame"] ?? 0;

        /// <summary>Waits until <paramref name="n"/> frames passed (polls ping; false after <paramref name="timeoutSec"/>).</summary>
        public bool WaitFrames(int n, double timeoutSec = 60)
        {
            int f0 = Frame();
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < timeoutSec)
            {
                if (Frame() >= f0 + n) return true;
                Thread.Sleep(10);
            }
            return false;
        }

        public static string Short(string s) => s == null ? "null" : s.Length > 300 ? s.Substring(0, 300) + "…" : s;
    }

    static JObject PluginOf(JObject list, string guid) => (list?["plugins"] as JArray)?.OfType<JObject>().FirstOrDefault(p => (string)p["guid"] == guid);

    static List<string> Step0Checks(PlayCtx c)
    {
        c.Id = c.Phase == 1 ? "T3a" : "T3b";
        ProbeChecks(c);
        return c.Lines;
    }

    /// <summary>The probe checks of T3a (session 1) and T3b (session 2, the images reused).</summary>
    static void ProbeChecks(PlayCtx c)
    {
        var list = c.Call("{\"cmd\":\"plugin.list\"}");
        var probe = PluginOf(list, "stp3.test.probe");
        var core = PluginOf(list, "stp3.test.probecore");
        c.Check(PlayCtx.IsOk(list) && (string)list["source"] == "-pluginsDir", "plugin.list answers: source " + list["source"] + ", root " + list["root"] + ", loadCall " + list["loadCall"] + ", stage " + list["loadMs"] + " ms");
        c.Check((string)probe?["state"] == "loaded", "the probe loaded: state " + probe?["state"] + (probe?["reason"] != null ? " (" + probe["reason"] + ")" : "") +
          "; assemblies " + probe?["assemblies"]?.ToString(Formatting.None) + "; plugin loadMs " + probe?["loadMs"]);
        var ping = c.Call(new { cmd = "probe.ping", n = 21 });
        c.Check(PlayCtx.IsOk(ping) && (int?)ping["twice"] == 42 && (string)ping["dep"] == "dep ok", "probe.ping over HTTP: twice " + ping["twice"] + ", dep \"" + ping["dep"] + "\" (the lib dependency resolved), api " +
          ping["api"] + ", updates " + ping["updates"] + ", sceneLoads " + ping["sceneLoads"]?.ToString(Formatting.None));
        var help = c.Call(new { cmd = "help", name = "probe.ping" });
        c.Check(PlayCtx.IsOk(help) && (string)help["command"]?["owner"] == "stp3.test.probe" && (string)help["command"]?["group"] == "stp3.test.probe",
          "help: owner " + help["command"]?["owner"] + ", group " + help["command"]?["group"] + ", since " + help["command"]?["since"]);
        var spawn = c.Call(new { cmd = "probe.spawn" });
        int spawnFrame = (int?)spawn["spawnFrame"] ?? -1;
        JObject frames = null;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < 30)
        {
            frames = c.Call(new { cmd = "probe.frames" });
            if (PlayCtx.IsOk(frames) && (int)frames["frame"] >= spawnFrame + 10) break;
            Thread.Sleep(20);
        }
        c.Check(PlayCtx.IsOk(spawn) && PlayCtx.IsOk(frames) && (int)frames["behaviourFrames"] > 0, "AddComponent(Type) of the probe's MonoBehaviour: Update ran " + frames?["behaviourFrames"] + " times in " +
          ((int?)frames?["frame"] - spawnFrame) + " frames (spawn frame " + spawnFrame + "); plugin updates " + frames?["updates"]);
        var asm = c.Call(new { cmd = "probe.assemblies" });
        c.Check(PlayCtx.IsOk(asm) && (int?)asm["apiCopies"] == 1 && (int?)asm["probeCopies"] == 1, "one HS2Next.Api and one probe assembly in the domain (" + asm["apiCopies"] + ", " + asm["probeCopies"] +
          "); CurrentAssemblies lists the probe: " + asm["listedByUnity"] + " (of " + asm["unityLoaded"] + ")");
        var json = c.Call(new { cmd = "probe.json", text = "{\"a\":1,\"b\":[2,3]}" });
        c.Check(PlayCtx.IsOk(json) && (int?)json["members"] == 2 && (int?)json["copies"] == 1, "the host's Newtonsoft parses for the plugin: members " + json["members"] + ", " + json["library"] + " " + json["version"] + ", copies " + json["copies"]);
        var hooks = c.Call(new { cmd = "probecore.hooks" });
        c.Check((string)core?["state"] == "loaded" && PlayCtx.IsOk(hooks) && hooks["hooks"]?.Type == JTokenType.Integer, "the app-assembly probe loads and runs: state " + core?["state"] +
          (core?["reason"] != null ? " (" + core["reason"] + ")" : "") + ", hooks " + hooks["hooks"] + ", " + hooks["appAssembly"]);
        var ver = c.Call(new { cmd = "bridge.version" });
        c.Check(PlayCtx.IsOk(ver) && (ver["plugins"] as JArray)?.Count == (list["plugins"] as JArray)?.Count, "bridge.version.plugins: " + ver["plugins"]?.ToString(Formatting.None));
        if (c.Phase == 2)
        {
            bool reused = (probe?["assemblies"] as JArray)?.All(x => (bool?)x["reused"] == true) == true;
            c.Check(reused && (string)probe?["state"] == "loaded" &&
                    (list["plugins"] as JArray).All(p => (string)p["guid"] == StaleGuid || !((string)p["reason"] ?? "").Contains("staleAssembly")),
              "without a domain reload the assemblies are reused (reused " + reused + "), no plugin.staleAssembly");
            var stale = PluginOf(list, StaleGuid);
            if (stale != null)
                c.Check((string)stale["state"] == "refused" && ((string)stale["reason"] ?? "").StartsWith("plugin.staleAssembly", StringComparison.Ordinal),
                  "a plugin whose file changed since the domain loaded it is refused (plugin.staleAssembly)", PlayCtx.Short(stale.ToString(Formatting.None)));
        }
    }

    /// <summary>The parts of the play row in session order.</summary>
    static List<string> PlayChecks(PlayCtx c)
    {
        var parts = new List<(string id, Action<PlayCtx> run)>();
        if (c.Phase == 1)
        {
            parts.Add(("T3A", ProbeChecks));
            parts.Add(("PL13", UiChecks));
            // Phase 6.1: the forms, widgets, capture events, the Options toggle and the device body checks (PluginLoaderTest_Forms.cs)
            parts.AddRange(typeof(PluginLoaderTest).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(m => (m, mm: Regex.Match(m.Name, @"^(KU|KE|KD|KB)(\d+)$")))
                .Where(x => x.mm.Success && x.m.GetParameters().Length == 1 && x.m.GetParameters()[0].ParameterType == typeof(PlayCtx))
                .OrderBy(x => Array.IndexOf(new[] { "KU", "KE", "KD", "KB" }, x.mm.Groups[1].Value)).ThenBy(x => int.Parse(x.mm.Groups[2].Value))
                .Select(x => (x.m.Name, (Action<PlayCtx>)(cc => x.m.Invoke(null, new object[] { cc })))));
            parts.AddRange(typeof(PluginLoaderTest).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(m => (m, mm: Regex.Match(m.Name, @"^SQ(\d+)$")))
                .Where(x => x.mm.Success && x.m.GetParameters().Length == 1 && x.m.GetParameters()[0].ParameterType == typeof(PlayCtx))
                .OrderBy(x => int.Parse(x.mm.Groups[1].Value))
                .Select(x => (x.m.Name, (Action<PlayCtx>)(cc => x.m.Invoke(null, new object[] { cc })))));
            parts.Add(("PL14", LeakRows));
        }
        else parts.Add(("T3B", ProbeChecks));
        foreach (var (id, run) in parts)
        {
            if (!c.Wanted(id)) continue;
            c.Id = id;
            var sw = Stopwatch.StartNew();
            try { run(c); }
            catch (Exception e)
            {
                var ie = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                c.Check(false, "exception", ie.GetType().Name + ": " + ie.Message + " " + ie.StackTrace);
            }
            c.Info($"{sw.Elapsed.TotalSeconds:F1} s");
        }
        return c.Lines;
    }

    // ---------------------------------------------------------------- PL13 UI registration

    static void UiChecks(PlayCtx c)
    {
        const string G = "stp3.test.probeui";
        var st = c.Ok(new { cmd = "probeui.state" }, "probeui.state");
        c.Check((bool?)st["unity"] == true && (bool?)st["ui"] == true, "the plugin reaches the engine services and the UI (ctx.Unity().Ui)", PlayCtx.Short(c.LastText));
        var p = PluginOf(c.Call(new { cmd = "plugin.list" }), G);
        c.Check((int?)p?["ui"]?["panels"] == 1 && (int?)p?["ui"]?["sections"] == 1 && (int?)p?["ui"]?["actions"] == 1, "plugin.list counts its panel, section and action", PlayCtx.Short(p?["ui"]?.ToString(Formatting.None)));
        var ui = c.Ok(new { cmd = "ui.state" }, "ui.state");
        var panel = (ui["panels"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["id"] == G + "/panel");
        c.Check(panel != null && (string)panel["region"] == "right", "the panel is in ui.state in the right region (id <guid>/panel)", PlayCtx.Short(panel?.ToString(Formatting.None)));
        c.Ok(new { cmd = "ui.panel", panel = G + "/panel", op = "show" }, "ui.panel show");
        c.WaitFrames(3);
        st = c.Call(new { cmd = "probeui.state" });
        var q = c.Call(new { cmd = "ui.query", name = "stp3probeui-panel" });
        c.Check((bool?)st["panelBuilt"] == true && PlayCtx.IsOk(q) && c.LastText.Contains("probe ui panel"), "showing the panel runs its factory; the element is in the UI", PlayCtx.Short(c.LastText));
        var tab = c.Call(new { cmd = "ui.query", name = "tab-" + G + "/panel" });
        c.Check(PlayCtx.IsOk(tab) && c.LastText.Contains("Probe panel"), "the tab shows the plugin's string (strings.tsv)", PlayCtx.Short(c.LastText));
        string item = (string)c.Ok(new { cmd = "item.add", no = 399 }, "item.add")["added"]?[0]?["key"];
        c.Ok(new { cmd = "select", keys = new[] { item } }, "select the item");
        c.Ok(new { cmd = "ui.panel", panel = "inspector", op = "show" }, "show the inspector");
        c.WaitFrames(5);
        st = c.Call(new { cmd = "probeui.state" });
        var sec = c.Call(new { cmd = "ui.query", name = "stp3probeui-section" });
        c.Check((int?)st["sections"] >= 1 && (string)st["sectionNode"] == item && (string)st["sectionKind"] == "item" && PlayCtx.IsOk(sec) && c.LastText.Contains(item),
            "the inspector section is built for the selected item", PlayCtx.Short(c.LastText));
        var act = c.Ok(new { cmd = "ui.action", action = G + "/hello" }, "ui.action <guid>/hello");
        st = c.Call(new { cmd = "probeui.state" });
        c.Check((bool?)act["ran"] == true && (int?)st["actionRuns"] == 1, "the action runs through ui.action", PlayCtx.Short(c.LastText));
        c.Ok(new { cmd = "delete", key = item }, "delete the item");
    }

    // ---------------------------------------------------------------- PL14 leak rows

    // The objects by name from app.perf's census (counted on the main thread, as plugin.list diagnostics counts them).
    static Dictionary<string, int> NameCensus(JObject perf, string kind)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        if (perf?["objects"]?["census"]?[kind] is JObject o)
            foreach (var kv in o) d[kv.Key] = (int?)kv.Value ?? 0;
        return d;
    }

    // "-name, +name x2" from a to b ("=" when equal).
    static string CensusDiff(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        var parts = new List<string>();
        foreach (var k in a.Keys.Union(b.Keys).OrderBy(x => x, StringComparer.Ordinal))
        {
            a.TryGetValue(k, out int x);
            b.TryGetValue(k, out int y);
            if (x == y) continue;
            int n = Math.Abs(y - x);
            parts.Add((y > x ? "+" : "-") + k + (n > 1 ? " x" + n : ""));
        }
        return parts.Count == 0 ? "=" : string.Join(", ", parts);
    }

    static void LeakRows(PlayCtx c)
    {
        const string G = "stp3.sample.sequencer";
        string fixture = Path.Combine(c.ProjectRoot, "Tests", "Plugins", "fixtures", "sequence_scene.hs2nscene");
        if (!c.Check(File.Exists(fixture), "the sequence fixture exists", fixture)) return;
        Directory.CreateDirectory(c.SceneDir);
        File.Copy(fixture, Path.Combine(c.SceneDir, "plugin_leak_fixture.hs2nscene"), true);
        var tail = c.Call(new { cmd = "log.tail", n = 1 });
        int since = (int?)(tail["entries"] as JArray)?.LastOrDefault()?["seq"] ?? 0;
        var rows = new List<JObject>();
        Dictionary<string, int> goBase = null, matBase = null;   // the census of cycle -1 (the info lines show each cycle's difference)
        var started = new List<int>();   // UnloadUnusedAssets calls started up to each cycle's sample, cycle -1 first
        bool tracksEmpty = true, played = true;
        string firstProblem = null;
        for (int cycle = -1; cycle <= 20; cycle++)
        {
            var ld = c.Call(new { cmd = "scene.load", name = "plugin_leak_fixture" });
            var play = c.Call(new { cmd = "seq.play" });
            if (!PlayCtx.IsOk(ld) || !PlayCtx.IsOk(play)) { played = false; firstProblem ??= "cycle " + cycle + ": " + PlayCtx.Short(c.LastText); }
            c.WaitFrames(60);
            var status = c.Call(new { cmd = "seq.status" });
            if ((string)status["state"] != "playing" && (string)status["state"] != "ended") { played = false; firstProblem ??= "cycle " + cycle + " status " + PlayCtx.Short(c.LastText); }
            c.Call(new { cmd = "seq.stop" });
            c.Call(new { cmd = "scene.new" });
            c.WaitFrames(2);
            var after = c.Call(new { cmd = "seq.status" });
            if ((after["tracks"] as JArray)?.Count > 0) { tracksEmpty = false; firstProblem ??= "cycle " + cycle + " tracks after scene.new " + PlayCtx.Short(c.LastText); }
            var list = c.Call(new { cmd = "plugin.list", diagnostics = true });
            var p = PluginOf(list, G);
            var d = list["diagnostics"] as JObject ?? new JObject();
            var row = new JObject
            {
                ["cycle"] = cycle, ["managedBytes"] = d["managedBytes"], ["gameObjects"] = d["gameObjects"], ["materials"] = d["materials"], ["meshes"] = d["meshes"],
                ["textures"] = d["textures"], ["renderTextures"] = d["renderTextures"], ["commands"] = (p?["commands"] as JArray)?.Count, ["ext"] = (p?["extNamespaces"] as JArray)?.Count,
                ["events"] = p?["events"], ["state"] = p?["state"], ["eventsFired"] = status["eventsFired"],
            };
            // which objects differ from cycle -1 (names; " [asset]" for a GameObject outside every scene), and the upkeep's
            // UnloadUnusedAssets calls started so far (app.perf on the main thread)
            var perf = c.Call(new { cmd = "app.perf", objects = true, census = true });
            var goNow = NameCensus(perf, "gameObjects");
            var matNow = NameCensus(perf, "materials");
            goBase ??= goNow;
            matBase ??= matNow;
            int st = (int?)perf["unloadUnused"]?["started"] ?? (int?)perf["unloadUnused"]?["calls"] ?? 0;
            started.Add(st);
            row["uuaStarted"] = st;
            row["census"] = "gameObjects " + CensusDiff(goBase, goNow) + "; materials " + CensusDiff(matBase, matNow);
            if (cycle >= 0) rows.Add(row);
            c.Info(row.ToString(Formatting.None));
        }
        var r0 = rows.First();
        var r20 = rows.Last();
        long mb0 = (long)r0["managedBytes"], mb20 = (long)r20["managedBytes"];
        c.Check(played, "every cycle loads the fixture and plays", firstProblem ?? "");
        c.Check(mb20 <= mb0 + 2 * 1024 * 1024, "managed bytes after cycle 20 <= after cycle 0 + 2 MiB", mb0 + " -> " + mb20 + " (" + (mb20 - mb0) / 1024 + " KiB)");
        foreach (var k in new[] { "materials", "meshes", "textures", "renderTextures" })
            c.Check((int)r20[k] <= (int)r0[k] + 2, k + " after cycle 20 <= after cycle 0 + 2", r0[k] + " -> " + r20[k]);
        // gameObjects: a leak grows - no cycle above cycle 0's count, cycle 20 not above it. A cycle below it is the upkeep's
        // rate-limited UnloadUnusedAssets freeing the unreferenced prefab asset between that cycle's scene.new and its sample (the
        // next load brings it back): allowed (info) only when a call started in that cycle or the one before, else a FAIL.
        var gos = rows.Select(r => (int)r["gameObjects"]).ToList();
        int g0 = gos[0];
        // cycle 0 itself dipped (a call started by its sample): cycle 1 is the reference
        if (gos.Count > 1 && gos[1] > gos[0] && started[1] > started[0]) g0 = gos[1];
        var above = rows.Where(r => (int)r["gameObjects"] > g0).Select(r => (int)r["cycle"]).ToList();
        c.Check(above.Count == 0 && gos[gos.Count - 1] <= g0, "gameObjects never above cycle 0's (the reference) after the scene.new of a cycle (no growth)",
            string.Join(",", gos) + (above.Count > 0 ? "; above in cycles " + string.Join(",", above) : ""));
        var dips = new List<string>();
        var unexplained = new List<string>();
        for (int i = 0; i < rows.Count; i++)
        {
            if (gos[i] >= g0) continue;
            int s = started[i + 1], before = started[Math.Max(0, i - 1)];   // started[0] is cycle -1's
            string what = "cycle " + rows[i]["cycle"] + " " + gos[i] + " (" + rows[i]["census"] + ")";
            if (s > before) dips.Add(what + ", UnloadUnusedAssets started " + before + " -> " + s);
            else unexplained.Add(what + ", no UnloadUnusedAssets started");
        }
        if (dips.Count > 0) c.Info("PL14 gameObjects below cycle 0's where the upkeep's UnloadUnusedAssets ran (not a leak): " + string.Join(" | ", dips));
        c.Check(unexplained.Count == 0, "gameObjects below cycle 0's only in a cycle where the upkeep's UnloadUnusedAssets started",
            string.Join(" | ", unexplained));
        c.Check(tracksEmpty, "the sequencer's status.tracks is empty after each scene.new", firstProblem ?? "");
        foreach (var k in new[] { "commands", "ext", "events" })
            c.Check(rows.Select(r => r[k]?.ToString()).Distinct().Count() == 1, "the sample's " + k + " count is constant", string.Join(",", rows.Select(r => r[k]?.ToString()).Distinct()));
        c.Check(rows.All(r => (string)r["state"] == "loaded"), "the sample stays loaded");
        var warn = c.Call(new { cmd = "log.tail", n = 1000, level = "warning", since });
        var plugWarn = (warn["entries"] as JArray)?.OfType<JObject>().Where(e => ((string)e["message"] ?? "").Contains("[plugin ") || ((string)e["message"] ?? "").Contains("[app] plugin")).ToList() ?? new List<JObject>();
        c.Check(plugWarn.Count == 0, "no plugin warning in log.tail during the cycles", string.Join(" | ", plugWarn.Take(3).Select(e => (string)e["message"])));
    }
}
