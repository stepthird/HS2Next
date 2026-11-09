// ScriptTest's executor part (Phase 6.9 package X): Headless_X_Executor, a part of ScriptTest.Run (headless,
// edit mode), and Play_X_Executor, a part of ScriptTest.RunPlay (JX4: play mode, graphics). With bridge.enabled false the app starts its
// bridge host without a listener and registers the core commands; the headless groups start test hosts as the app's bridge stage starts them
// (AppCore.ForTestsAsApp), not AppCore.ForTests' fixed in-process host, and JX4 starts the app itself. The run's UserData folders are under
// Spike/out/script/ and removed at the end of each group; port 17903.
//   JX1  bridge.enabled false: AppCore.Bridge exists, Listening false (state in-process), no endpoint.json and no token file in the run's
//        UserData, nothing accepts a connection on the settings' port, the core commands are registered and answer in process, app.paths
//        reads tokenAcl "none", the bridge stage's line reads "in-process (no listener)" and the settings stage's "bridge disabled (no
//        listener)". Present: the same host with bridge.enabled true listens, writes endpoint.json and the token file, its stage lines read
//        "listening on <url>" and "port 17903, token", and it removes endpoint.json at its end; both hosts hold the same command names.
//   JX2  bridge.enabled false: a Timeline's four timed commands (ping, camera.get, wet.status, camera.set with an integer array) fire with
//        0 errors. Present: on the same host with its bridge gone (the state of the bridge-off app before this phase) the same four count
//        4 errors "unavailable".
//   JX3  bridge.enabled false: a test plugin's ctx.Call and ctx.Request reach camera.get and obj.transform (the node moves); the plugin host
//        uses the app's server (OwnsBridge false). Present: the plugin's own command answers through ctx.Call too.
//   JX4  (ScriptTest.play, moved there by the integrator on the lead's ruling of 2026-11-08: headless, the sample's stain steps answer
//        unsupported) the app itself started with bridge.enabled false (launch settings {"bridge": {"enabled": false}}, as JX5r):
//        script.play {load: true} of the sample (Tests/SceneScripts/sample.hs2nscript; its character roles cast to the targets loaded by
//        chara.load, whose node is the reply's added[0].key) plays one pass with no failure but the sample's deliberate one (the steps'
//        failures of the reply plus the timed commands' errors of script.status: one in all); the failures are printed. Played at the
//        screen's rate; the Timeline is paused at its first wrap.
//   JX5  every Bridge null check (and alias of AppCore.Bridge) and every read of Settings.Bridge.Enabled in Runtime/** against X's list,
//        each with its reading after this phase and the check that covers a changed reading. Present: the list has at least 4 entries. A
//        listed site that is gone FAILs (the list is stale); a site the list does not name is the numbered WARN JX5W1 (a reading to review).
//        One narrow rule leaves a line out, printed in the row: `= CommandSource.Bridge;` is an enum value of CommandSource (the source of a
//        command), not a read of the host; a line excluded is one that no pattern matches once that text is taken out of it. The integrator
//        completed the list with the sites 6.9 added (the lead's ruling of 2026-11-08 after -Quick 20261108-1611's JX5W1).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using HS2Next.App;
using HS2Next.App.Plugins;
using HS2Next.Bridge;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static partial class ScriptTest
{
    /// <summary>X's port: the listening host of JX1's Present.</summary>
    const int XPort = 17903;
    const string XProbeGuid = "stp3.test.historyprobe";
    static string XOut => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "script");

    static void Headless_X_Executor()
    {
        Group("JX1", "bridge.enabled false: the app's bridge host without a listener", XJx1);
        Group("JX2", "bridge.enabled false: a Timeline's timed commands run", XJx2);
        Group("JX3", "bridge.enabled false: a plugin's Call and Request reach the app's commands", XJx3);
        Group("JX5", "every Bridge null check and bridge.enabled read in Runtime/** with its reading", XJx5);
    }

    // ------------------------------------------------------------ hosts

    /// <summary>Settings of a host in its own UserData under Spike/out/script (made empty first).</summary>
    static AppSettings XSettings(string name, bool enabled)
    {
        string ud = Path.Combine(XOut, "x_" + name + "_userdata");
        XRemove(ud);
        var s = AppSettings.Defaults(Array.Empty<string>());
        s.Content = CardResolveTest.Settings;
        s.UserData = ud;
        s.Bridge.OutputDir = Path.Combine(ud, "Bridge");
        s.Bridge.Enabled = enabled;
        s.Bridge.Port = XPort;
        s.Bridge.Token = true;   // a listening host writes the token file (JX1's Present); a host without a listener writes none
        s.Bridge.TokenValue = null;
        return s;
    }

    static SessionOptions XOptions(ulong seed)
    {
        var o = services.NewSessionOptions();
        o.Ids = new CountingIdSource(seed);
        return o;
    }

    /// <summary>Removes a folder this part made (under Spike/out/script only; no reparse point is followed: the folder holds plain files).</summary>
    static void XRemove(string dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        string full = Path.GetFullPath(dir);
        if (!full.StartsWith(Path.GetFullPath(XOut), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("not under Spike/out/script: " + full);
        if ((new DirectoryInfo(full).Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("a reparse point: " + full);
        Directory.Delete(full, true);
    }

    static JObject XCall(AppCore app, object req, int frames = 600)
    {
        string json = req is string s ? s : JsonConvert.SerializeObject(req);
        string r = app.Call(json, frames);
        try { return JObject.Parse(r); }
        catch (Exception) { return new JObject { ["ok"] = false, ["error"] = "unparsable: " + r }; }
    }

    static bool XOk(JObject o) => o?.Value<bool?>("ok") == true;

    static string XShort(JToken t)
    {
        string s = t?.ToString(Formatting.None) ?? "null";
        return s.Length > 300 ? s.Substring(0, 300) + "..." : s;
    }

    /// <summary>True when something accepts a TCP connection on 127.0.0.1:<paramref name="port"/> within 500 ms.</summary>
    static bool XConnects(int port)
    {
        try
        {
            using (var c = new TcpClient())
            {
                var t = c.ConnectAsync("127.0.0.1", port);
                return t.Wait(500) && c.Connected;
            }
        }
        catch (Exception) { return false; }
    }

    static string XFiles(string dir) => Directory.Exists(dir) ? string.Join(", ", Directory.GetFileSystemEntries(dir).Select(Path.GetFileName)) : "(no folder)";

    // ------------------------------------------------------------ JX1

    static void XJx1()
    {
        HashSet<string> namesOff;
        string udOff;
        var off = AppCore.ForTestsAsApp(services, XSettings("jx1off", false), null, XOptions(0x69a01UL));
        try
        {
            udOff = off.Paths.UserData;
            var b = off.Bridge;
            string dir = off.Paths.BridgeDir;
            Check("JX1: AppCore.Bridge exists with bridge.enabled false", b != null && !b.IsDisposed, b == null ? "null" : "disposed " + b.IsDisposed);
            if (b == null) return;
            Check("JX1: Listening false, the server in process, no URL", !b.Listening && b.Server.State == BridgeState.InProcess && b.Url == null, b.Server.State + ", url " + (b.Url ?? "null"));
            bool ep = File.Exists(Path.Combine(dir, "endpoint.json")), tok = File.Exists(Path.Combine(dir, "token"));
            var stray = Directory.Exists(udOff) ? Directory.GetFiles(udOff, "*", SearchOption.AllDirectories).Where(f => Path.GetFileName(f) == "endpoint.json" || Path.GetFileName(f) == "token").ToList() : new List<string>();
            Check("JX1: no endpoint.json and no token file in the run's UserData", !ep && !tok && stray.Count == 0, dir + ": " + XFiles(dir) + (stray.Count > 0 ? "; found " + string.Join(", ", stray) : ""));
            Check("JX1: nothing accepts a connection on the settings' port " + XPort, !XConnects(XPort), "");
            namesOff = new HashSet<string>(b.Server.Commands.Specs.Select(sp => sp.Name), StringComparer.Ordinal);
            var core = new[] { "camera.get", "obj.transform", "timeline.play", "wet.status", "item.add", "record.start" };
            var missing = core.Where(n => !namesOff.Contains(n)).ToList();
            Check("JX1: the core commands are registered (" + namesOff.Count + " names)", missing.Count == 0 && namesOff.Count >= 245, "missing " + string.Join(", ", missing));
            Check("JX1: the bridge stage's line reads \"" + AppCore.NoListenerSummary + "\"", off.BridgeStageSummary == AppCore.NoListenerSummary, off.BridgeStageSummary ?? "null");
            Check("JX1: the settings stage's line reads \"bridge disabled (no listener)\"", off.BridgeSettingsSummary == "disabled (no listener)", off.BridgeSettingsSummary);
            var cam = XCall(off, new { cmd = "camera.get" });
            Check("JX1: camera.get answers in process", XOk(cam) && cam["camera"] != null, XShort(cam));
            var paths = XCall(off, new { cmd = "app.paths" });
            Check("JX1: app.paths reads tokenAcl none (no token file written)", XOk(paths) && (string)paths["tokenAcl"] == "none", XShort(paths["tokenAcl"]));
        }
        finally { off.Dispose(); }

        // Present: the same host with the bridge on
        var on = AppCore.ForTestsAsApp(services, XSettings("jx1on", true), null, XOptions(0x69a02UL));
        string endpoint = null;
        try
        {
            var b = on.Bridge;
            string dir = on.Paths.BridgeDir;
            endpoint = Path.Combine(dir, "endpoint.json");
            bool ep = File.Exists(endpoint), tok = File.Exists(Path.Combine(dir, "token"));
            Check("JX1: Present: the same host with bridge.enabled true listens on " + XPort + " and writes endpoint.json and the token file",
                b != null && b.Listening && ep && tok && XConnects(XPort), (b == null ? "no host" : b.Server.State + " " + (b.Url ?? b.BindError)) + "; " + XFiles(dir));
            Check("JX1: Present: its stage lines read \"listening on http://127.0.0.1:" + XPort + "/\" and \"port " + XPort + ", token\"",
                on.BridgeStageSummary == "listening on http://127.0.0.1:" + XPort + "/" && on.BridgeSettingsSummary == "port " + XPort + ", token", on.BridgeStageSummary + " | " + on.BridgeSettingsSummary);
            var namesOn = new HashSet<string>(b.Server.Commands.Specs.Select(sp => sp.Name), StringComparer.Ordinal);
            Check("JX1: both hosts hold the same command names", namesOn.SetEquals(namesOff), $"{namesOn.Count} with a listener, {namesOff.Count} without; differ: {string.Join(", ", namesOn.Except(namesOff).Concat(namesOff.Except(namesOn)).Take(10))}");
        }
        finally { on.Dispose(); }
        Check("JX1: the listening host removes endpoint.json at its end", endpoint != null && !File.Exists(endpoint), endpoint ?? "");
        XRemove(udOff);
        XRemove(Path.Combine(XOut, "x_jx1on_userdata"));
    }

    // ------------------------------------------------------------ JX2

    /// <summary>The four timed commands of Step 0's S0-2 (camera.set with an integer array: a vec3 argument read through the host's converters).</summary>
    static JArray XTimed() => new JArray
    {
        new JObject { ["t"] = 0.05, ["do"] = new JObject { ["cmd"] = "ping" } },
        new JObject { ["t"] = 0.10, ["do"] = new JObject { ["cmd"] = "camera.get" } },
        new JObject { ["t"] = 0.15, ["do"] = new JObject { ["cmd"] = "wet.status" } },
        new JObject { ["t"] = 0.20, ["do"] = new JObject { ["cmd"] = "camera.set", ["target"] = new JArray(0, 10, 0), ["dist"] = 30 } },
    };

    static void XJx2()
    {
        var app = AppCore.ForTestsAsApp(services, XSettings("jx2", false), null, XOptions(0x69a03UL));
        try
        {
            if (!Check("JX2: the host runs without a listener", app.Bridge != null && !app.Bridge.Listening, "")) return;
            var build = XCall(app, new { cmd = "timeline.build", duration = 1.0, loop = false, tracks = new object[0], events = XTimed(), play = false });
            if (!Check("JX2: timeline.build with the four timed commands", XOk(build), XShort(build))) return;
            var pb = app.Runtime.GetPart<TimelinePlayback>();
            long fired0 = pb.EventsFired, errors0 = pb.EventErrors;
            var play = XCall(app, new { cmd = "timeline.play", from = 0.0 });
            Check("JX2: timeline.play from 0", XOk(play), XShort(play));
            for (int i = 0; i < 45; i++) app.Frame(1f / 60f);   // 0.75 s: every event crossed, every reply collected
            var st = XCall(app, new { cmd = "timeline.status" });
            long fired = pb.EventsFired - fired0, errors = pb.EventErrors - errors0;
            var cam = XCall(app, new { cmd = "camera.get" });
            Check("JX2: 4 timed commands fired, 0 errors", fired == 4 && errors == 0, $"fired {fired}, errors {errors}, last error {XShort(st["lastEventError"])}");
            Check("JX2: camera.set's integer array reached the view (target 0, 10, 0)", XOk(cam) && XVec(cam.SelectToken("camera.target")) is UnityEngine.Vector3 t && (t - new UnityEngine.Vector3(0, 10, 0)).magnitude < 1e-3f, XShort(cam.SelectToken("camera.target")));

            // Present: the same Timeline with the host's bridge gone, as the bridge-off app had it before this phase (no host: the submitter has no server)
            XCall(app, new { cmd = "timeline.stop" });
            app.Bridge.Dispose();
            long f1 = pb.EventsFired, e1 = pb.EventErrors;
            pb.Service.Play(0);
            for (int i = 0; i < 45; i++) app.Frame(1f / 60f);
            long cf = pb.EventsFired - f1, ce = pb.EventErrors - e1;
            Check("JX2: Present: without a host the same four count 4 errors unavailable", cf == 4 && ce == 4 && pb.LastEventError?.Code == "unavailable",
                $"fired {cf}, errors {ce}, last {pb.LastEventError?.Code}: {pb.LastEventError?.Message}");
        }
        finally { app.Dispose(); }
        XRemove(Path.Combine(XOut, "x_jx2_userdata"));
    }

    static UnityEngine.Vector3? XVec(JToken t)
    {
        if (t is JArray a && a.Count == 3) return new UnityEngine.Vector3((float)a[0], (float)a[1], (float)a[2]);
        if (t is JObject o && o["x"] != null) return new UnityEngine.Vector3((float)o["x"], (float)o["y"], (float)o["z"]);
        return null;
    }

    // ------------------------------------------------------------ JX3

    /// <summary>The folder holding the staged history probe: BridgeCoreTest's C37 folder when it has it (one assembly path per process), else this
    /// part's own folder, staged by Tools/plugins.ps1.</summary>
    static string XPluginDir()
    {
        string c37 = BridgeCoreTest.C37Dir;
        if (File.Exists(Path.Combine(c37, XProbeGuid, "manifest.json"))) return c37;
        string dir = Path.Combine(XOut, "x_plugins");
        if (File.Exists(Path.Combine(dir, XProbeGuid, "manifest.json"))) return dir;
        string tool = Path.Combine(ContentSettings.ProjectRoot, "Tools", "plugins.ps1");
        string project = Path.Combine(ContentSettings.ProjectRoot, "Tests", "Plugins", "HistoryProbe");
        var psi = new ProcessStartInfo("pwsh", "-NoProfile -File \"" + tool + "\" -Build \"" + project + "\" -Stage \"" + dir + "\"")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = ContentSettings.ProjectRoot,
        };
        var sw = Stopwatch.StartNew();
        using (var p = Process.Start(psi))
        {
            string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            bool exited = p.WaitForExit(300000);
            Info("JX3: staged the history probe: plugins.ps1 exit " + (exited ? p.ExitCode.ToString() : "timeout") + " in " + sw.Elapsed.TotalSeconds.ToString("F1") + " s");
            if (!exited || p.ExitCode != 0) { Check("JX3: plugins.ps1 -Build HistoryProbe -Stage", false, o.Length > 400 ? o.Substring(o.Length - 400) : o); return null; }
        }
        return dir;
    }

    static void XJx3()
    {
        string dir = XPluginDir();
        if (dir == null) return;
        var others = Directory.GetDirectories(dir).Select(Path.GetFileName).Where(g => !string.Equals(g, XProbeGuid, StringComparison.OrdinalIgnoreCase)).ToArray();
        var app = AppCore.ForTestsAsApp(services, XSettings("jx3", false), null, XOptions(0x69a04UL));
        try
        {
            if (!Check("JX3: the host runs without a listener", app.Bridge != null && !app.Bridge.Listening, "")) return;
            var host = app.LoadPlugins(dir, others);
            var e = host.Find(XProbeGuid);
            var ctx = e?.Context;
            if (!Check("JX3: the history probe is loaded from " + Path.GetFileName(dir), ctx != null && e.IsLoaded, e?.State + " " + e?.Reason)) return;
            Check("JX3: the plugin host uses the app's server (OwnsBridge false)", !host.OwnsBridge && ReferenceEquals(host.Bridge, app.Bridge.Server), "OwnsBridge " + host.OwnsBridge);
            var viaCall = JObject.Parse(ctx.Call("{\"cmd\":\"camera.get\"}"));
            var viaRequest = JObject.Parse(ctx.Request("camera.get", null).Json);
            Check("JX3: ctx.Call reaches camera.get", XOk(viaCall) && viaCall["camera"] != null, XShort(viaCall));
            Check("JX3: ctx.Request reaches camera.get", XOk(viaRequest) && viaRequest["camera"] != null, XShort(viaRequest));
            var add = XCall(app, new { cmd = "item.add", no = 399 });
            string key = (string)add["added"]?[0]?["key"];
            if (!Check("JX3: an item to move", key != null, XShort(add))) return;
            var t1 = JObject.Parse(ctx.Call(JsonConvert.SerializeObject(new { cmd = "obj.transform", key, pos = new[] { 1, 2, 3 } })));
            var p1 = app.Session.Model.Find(key)?.Doc?.Transform?.Pos;
            Check("JX3: ctx.Call reaches obj.transform: the node is at 1, 2, 3", XOk(t1) && p1 is Vec3 a && a.X == 1 && a.Y == 2 && a.Z == 3, XShort(t1) + "; model " + p1);
            var t2 = JObject.Parse(ctx.Request("obj.transform", new { key, pos = new[] { 4, 5, 6 } }).Json);
            var p2 = app.Session.Model.Find(key)?.Doc?.Transform?.Pos;
            Check("JX3: ctx.Request reaches obj.transform: the node is at 4, 5, 6", XOk(t2) && p2 is Vec3 b && b.X == 4 && b.Y == 5 && b.Z == 6, XShort(t2) + "; model " + p2);
            var own = JObject.Parse(ctx.Call("{\"cmd\":\"historyprobe.state\"}"));
            Check("JX3: Present: the plugin's own command answers through ctx.Call", XOk(own) && own["loads"] != null, XShort(own));
        }
        finally { app.Dispose(); }
        XRemove(Path.Combine(XOut, "x_jx3_userdata"));
    }

    // ------------------------------------------------------------ JX4 (play row)

    static string XSample => Path.Combine(ContentSettings.ProjectRoot, "Tests", "SceneScripts", "sample.hs2nscript");

    /// <summary>The app's launch settings of JX4 with bridge.enabled false (its own UserData and output folder under Spike/out/script).</summary>
    static string XJx4Launch =>
        "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":" + (HasArg("-noUi") ? "false" : "true") + ",\"language\":\"en\"},\"userData\":\"Spike/out/script/x_jx4_userdata\",\"bridge\":{\"enabled\":false,\"port\":" + XPort +
        ",\"outputDir\":\"Spike/out/script/x_jx4_out\",\"allowQuit\":false}}";

    static IEnumerator Play_X_Executor()
    {
        if (!WantAny("JX4")) yield break;
        current = "X_Executor";
        if (POpen("JX4", "bridge.enabled false: script.play of the sample")) yield return XJx4();
    }

    static IEnumerator XJx4()
    {
        if (pApp != null) yield return PStopApp();
        if (!Check("JX4: a graphics device", UnityEngine.SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null, UnityEngine.SystemInfo.graphicsDeviceType.ToString())) yield break;
        if (!Check("JX4: the sample is in this tree", File.Exists(XSample), XSample)) yield break;
        XRemove(Path.Combine(XOut, "x_jx4_userdata"));
        XRemove(Path.Combine(XOut, "x_jx4_out"));
        var args = Environment.GetCommandLineArgs();
        AppSettings s;
        try { s = AppSettings.Load(args, XJx4Launch); }
        catch (Exception e) { Check("JX4: the app's settings", false, e.Message); s = AppSettings.Defaults(args, XJx4Launch); }
        pApp = AppBootstrap.Start(s);
        float ready = UnityEngine.Time.realtimeSinceStartup + 180f;
        while (UnityEngine.Time.realtimeSinceStartup < ready && (AppCore.Current == null || AppCore.Current.Stage != AppStage.Ready)) yield return null;
        var app = AppCore.Current;
        bool started = app != null && app.Stage == AppStage.Ready && !app.Settings.Bridge.Enabled && app.Bridge != null && !app.Bridge.Listening;
        if (Check("JX4: the app started with bridge.enabled false, its host without a listener", started, app == null ? "none" : app.StageName + ", " + app.Bridge?.Server.State))
        {
            var reg = app.Bridge.Server.Commands;
            if (Check("JX4: script.import, script.play and script.status are registered", reg.Contains("script.import") && reg.Contains("script.play") && reg.Contains("script.status"), ""))
                yield return XJx4Play(app);
        }
        yield return PStopApp();
        yield return PFrames(5);
        try { XRemove(Path.Combine(XOut, "x_jx4_userdata")); XRemove(Path.Combine(XOut, "x_jx4_out")); }
        catch (Exception e) { Info("JX4: the run's folders stay: " + e.Message); }
    }

    static IEnumerator XJx4Play(AppCore app)
    {
        yield return PCall(app, new { cmd = "scene.new" });
        yield return PCall(app, new { cmd = "script.import", path = XSample, to = "scene" });
        string script = (string)pLast?["script"];
        if (!Check("JX4: script.import of the sample into the scene", POk && script != null, Short(pLastText))) yield break;
        // the character roles cast to the two targets loaded by chara.load (female roles BR-Chan, male roles HERO107; a role of either sex takes
        // BR-Chan); chara.load answers the node in added[0].key. Prop roles are left to the play's load.
        yield return PCall(app, new { cmd = "script.get", script });
        var roles = pLast?.SelectToken("script.roles") as JArray;
        string female = null, male = null;
        var map = new JObject();
        var charaRoles = new List<string>();
        foreach (var r in (roles ?? new JArray()).OfType<JObject>())
        {
            if ((string)r["kind"] != "chara") continue;
            charaRoles.Add((string)r["role"]);
            bool isMale = (string)r["sex"] == "male";
            if (isMale && male == null) { yield return PCall(app, new { cmd = "chara.load", path = maleRel, sex = "male" }); male = Key(pLast); }
            if (!isMale && female == null) { yield return PCall(app, new { cmd = "chara.load", path = femaleRel, sex = "female" }); female = Key(pLast); }
            map[(string)r["role"]] = isMale ? male : female;
        }
        if (map.Count > 0)
        {
            yield return PCall(app, new { cmd = "script.cast", script, roles = map });
            if (!Check("JX4: the sample's roles cast to the targets", POk, Short(pLastText))) yield break;
        }
        // every character role cast before script.play {load: true} (no hint card is loaded for a character role)
        yield return PCall(app, new { cmd = "script.list" });
        var row = (pLast?["scripts"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["script"] == script);
        var uncast = (row?["uncast"] as JArray)?.Select(x => (string)x).Where(charaRoles.Contains).ToList();
        if (!Check("JX4: every character role cast before the play (" + charaRoles.Count + ")", row != null && uncast != null && uncast.Count == 0, XShort(row?["uncast"]))) yield break;
        yield return PCall(app, new { cmd = "script.play", script, load = true }, 600);
        if (!Check("JX4: script.play answers", POk, Short(pLastText))) yield break;
        var failures = pLast["failures"] as JArray ?? new JArray();
        // one pass at the screen's rate: the Timeline paused at its first wrap (a timed command at t = 0 may fire once more there)
        double duration = 0, last = -1;
        bool wrapped = false;
        float until = UnityEngine.Time.realtimeSinceStartup + 120f;
        while (UnityEngine.Time.realtimeSinceStartup < until)
        {
            yield return PCall(app, new { cmd = "script.status" });
            duration = (double?)pLast?["duration"] ?? duration;
            double t = (double?)pLast?["time"] ?? 0;
            string state = (string)pLast?["state"];
            if (state != "playing" || (last >= 0 && t < last)) { wrapped = state == "playing" || state == "ended"; break; }
            last = t;
        }
        yield return PCall(app, new { cmd = "timeline.pause" });
        yield return PCall(app, new { cmd = "script.status" });
        var st = pLast ?? new JObject();
        long evErrors = (long?)st["eventErrors"] ?? -1;
        int stepFailures = failures.Count;
        Info($"JX4: one pass of {duration:0.###} s (wrapped {wrapped}, last time {last:0.###}); {stepFailures} step failures {XShort(failures)}; timed commands fired {st["eventsFired"]}, errors {evErrors}, last {XShort(st["lastEventError"])}");
        Check("JX4: no failure but the sample's deliberate one (one failure in all: steps and timed commands)", stepFailures + evErrors == 1, $"steps {stepFailures}, timed {evErrors}");
        yield return PCall(app, new { cmd = "script.stop" });
    }

    // ------------------------------------------------------------ JX5

    /// <summary>One site of X's list: the file under Runtime/, a text the line holds, how many lines hold it, its reading with the bridge off after
    /// this phase, and the check that covers a changed reading ("-" for an unchanged one).</summary>
    sealed class XSite
    {
        public string File, Text, Reading, Covered;
        public int Count = 1;
    }

    static readonly XSite[] XSites =
    {
        new XSite { File = "App/AppCore.cs", Text = "BridgeServices => Bridge?.Services", Reading = "unchanged: null only before the bridge stage; with bridge.enabled false the services of the host without a listener", Covered = "JX1" },
        new XSite { File = "App/AppCore.cs", Text = "if (Bridge == null && Settings != null && Settings.Bridge.Enabled && Paths != null)", Reading = "unchanged: a start that fails before the bridge stage starts the listening host only when bridge.enabled (no host otherwise: nothing would call it)", Covered = "-" },
        new XSite { File = "App/AppCore.cs", Text = "Bridge?.Services.Readiness.SetDetail(\"scene\", \"startup failed: \"", Reading = "changed: with bridge.enabled false a failure after the bridge stage sets the detail on the host without a listener (in-process ping reads it)", Covered = "JX6" },
        new XSite { File = "App/AppCore.cs", Text = "if (Settings.Bridge.Enabled && folderError == null && !lockRefused) StartBridgeOfApp();", Reading = "unchanged: unreadable settings bring the listening host up only when bridge.enabled", Covered = "-" },
        new XSite { File = "App/AppCore.cs", Text = "Settings.Bridge.Enabled ? \"port \" + Settings.Bridge.Port", Reading = "changed: the settings stage's log line (BridgeSettingsSummary) says \"bridge disabled (no listener)\"", Covered = "JX1" },
        new XSite { File = "App/AppCore.cs", Text = "Settings.Bridge.Enabled ? \"not listening\" : NoListenerSummary", Reading = "changed: the bridge stage line (BridgeStageSummary) reads \"in-process (no listener)\" with bridge.enabled false", Covered = "JX1" },
        new XSite { File = "App/AppCore.cs", Text = "var s = Settings.Bridge;", Reading = "unchanged: the settings block, not the host", Covered = "-" },
        new XSite { File = "App/AppCore.cs", Text = "Bridge == null ? null : Bridge.Listening", Reading = "new: the bridge stage line, null before the host starts", Covered = "JX1" },
        new XSite { File = "App/AppCore.cs", Text = "if (Bridge != null) return;", Reading = "unchanged: one host per app", Covered = "-" },
        new XSite { File = "App/AppCore.cs", Text = "bool listen = Settings.Bridge.Enabled;", Reading = "changed: bridge.enabled now means \"a listener\"; the host, its pumps and the core commands start either way", Covered = "JX1" },
        new XSite { File = "App/AppCore.cs", Text = "var ready = Bridge?.Services.Readiness;", Count = 3, Reading = "changed: with bridge.enabled false the setup and catalog stages set the host's readiness (the gate of in-process requests)", Covered = "JX6" },
        new XSite { File = "App/AppCore.cs", Text = "Bridge?.Services.Readiness.Set(\"studioDb\", true);", Reading = "changed: as above (studioDb)", Covered = "JX6" },
        new XSite { File = "App/AppCore.cs", Text = "Bridge?.Services.Readiness.Set(\"scene\", true);", Reading = "changed: as above (scene)", Covered = "JX6" },
        new XSite { File = "App/AppCore.cs", Text = "Plugins = new PluginHost(Bridge?.Server, Paths.UserData, this);", Count = 2, Reading = "changed: with bridge.enabled false the plugins get the app's server: ctx.Call, Request and CallAsync reach the core commands", Covered = "JX3" },
        new XSite { File = "App/AppCore.cs", Text = "Bridge?.Services.Readiness.Set(PluginHost.ReadinessStage, true);", Reading = "changed: as above (plugins)", Covered = "JX6" },
        new XSite { File = "App/AppCore.cs", Text = "if (Bridge != null && !Bridge.IsDisposed && Bridge.ManualPump) Bridge.PumpUpdate();", Reading = "test host: a host whose pump components run (ForTestsAsApp in play mode) is not pumped by hand; manual hosts as before", Covered = "JX1" },
        new XSite { File = "App/AppCore.cs", Text = "Plugins.UpdateFrame(Bridge?.Server.Frames.Frame ?? 0", Reading = "unchanged: the test host's frame number", Covered = "-" },
        new XSite { File = "App/AppCore.cs", Text = "if (Bridge != null && !Bridge.IsDisposed && Bridge.ManualPump) Bridge.PumpLate();", Reading = "test host: as PumpUpdate", Covered = "JX1" },
        new XSite { File = "App/AppCore.cs", Text = "if (Bridge == null || Bridge.IsDisposed) return", Reading = "unchanged: a test host's Call without a host (JX2's Present disposes it)", Covered = "-" },
        new XSite { File = "App/AppCore.cs", Text = "try { Bridge?.Dispose(); }", Reading = "changed: with bridge.enabled false the app disposes its host at the end", Covered = "JX1" },
        new XSite { File = "App/AppPaths.cs", Text = "s.Bridge?.OutputDir", Reading = "unchanged: the settings' block, not the host", Covered = "-" },
        new XSite { File = "App/Commands/CaptureCommands.cs", Text = "app.Bridge?.Server?.Frames?.Frame ?? Time.frameCount", Reading = "changed source, same value: with bridge.enabled false the screenshot's frame number comes from the host's pump, which numbers frames by Time.frameCount", Covered = "-" },
        new XSite { File = "App/Commands/SetupCommands.cs", Text = ".With(\"tokenAcl\", app.Bridge?.Server?.TokenAcl);", Reading = "changed: with bridge.enabled false app.paths reads \"none\" (no token file) where it could not run before", Covered = "JX1" },
        new XSite { File = "App/Commands/TimelineCommands.cs", Text = "var b = app.Bridge;", Reading = "changed: the timed-command submitter always has a server; with bridge.enabled false timed commands run", Covered = "JX2" },
        new XSite { File = "App/Commands/TimelineCommands.cs", Text = "return b != null && !b.IsDisposed ? b.Server.SubmitAsync(json) : null;", Reading = "changed: as above; null (unavailable) only without a host", Covered = "JX2" },
        new XSite { File = "App/PlayerAcceptance.cs", Text = "var bridge = app.Bridge;", Reading = "unchanged: the player's acceptance needs a listener and reads Listening", Covered = "-" },
        new XSite { File = "App/PlayerAcceptance.cs", Text = "bridge != null && bridge.Listening", Reading = "unchanged verdict; with bridge.enabled false its detail reads \"not listening: \" instead of \"bridge disabled\"", Covered = "-" },
        new XSite { File = "App/Plugins/PluginHost.cs", Text = "OwnsBridge = bridge == null;", Reading = "changed: false in a bridge-off app (the app's server is given)", Covered = "JX3" },
        new XSite { File = "App/Plugins/PluginHost.cs", Text = "public bool OwnsBridge { get; }", Reading = "changed: as above", Covered = "JX3" },
        new XSite { File = "App/Plugins/PluginHost.cs", Text = "if (OwnsBridge) Bridge.Dispose();", Reading = "changed: a bridge-off app's plugin host no longer disposes a private server; the app disposes its host", Covered = "JX3" },
        new XSite { File = "Rendering/Capture/FrameRecorder.cs", Text = "IBridgeFrames clock = app.Bridge?.Server?.Frames;", Reading = "changed: with bridge.enabled false the recorder takes the host's late point (32000, FrameRecorderDriver's order); the driver stays for a start that failed before the bridge stage", Covered = "JX5r" },
        // the sites 6.9 added (C's script commands, P's player), listed by the integrator on the lead's ruling
        new XSite { File = "App/Commands/ScriptCommands.cs", Text = "var server = app?.Bridge?.Server;", Reading = "new (6.9): the host exists with the bridge off: the import's step warnings read Server's registry; none only without a host", Covered = "JX4" },
        new XSite { File = "Scene/Scripts/ScriptPlayer.cs", Text = "public BridgeServer Server => App.Bridge?.Server;", Reading = "new (6.9): the host exists with the bridge off: a play reads Server (its steps run inline there)", Covered = "JX4" },
        new XSite { File = "Scene/Scripts/ScriptPlayer.cs", Text = "if (app.Bridge == null || app.Bridge.IsDisposed) throw new BridgeException(BridgeCodes.Unavailable", Reading = "new (6.9): the host exists with the bridge off: a play answers unavailable only without a host", Covered = "JX4" },
    };

    /// <summary>The one exclusion: an enum value of CommandSource, not a read of the host (a line is excluded only when no pattern matches without it).</summary>
    static readonly Regex XExcludePattern = new Regex(@"=\s*CommandSource\.Bridge\s*;", RegexOptions.Compiled);
    const string XExcludeRule = "`= CommandSource.Bridge;` (an enum value of CommandSource, not a read of the host)";

    /// <summary>A Bridge null check, an alias of a Bridge (and, in that file, the alias's null checks), a read of Settings.Bridge.Enabled (not a write), OwnsBridge.</summary>
    static readonly Regex XSitePattern = new Regex(@"\b[Bb]ridge\s*[!=]=\s*null|\bBridge\?\.|Bridge\.Enabled\b(?!\s*=[^=])|\bOwnsBridge\b|=\s*\w+\??\.Bridge\s*;", RegexOptions.Compiled);
    /// <summary>A local alias of a Bridge (var b = app.Bridge;): its null checks in the same file are sites too.</summary>
    static readonly Regex XAliasPattern = new Regex(@"\bvar\s+(\w+)\s*=\s*\w+\??\.Bridge\s*;", RegexOptions.Compiled);

    static void XJx5()
    {
        string root = Path.Combine(ContentSettings.ProjectRoot, "Assets", "HS2Next", "Runtime");
        var found = new List<(string file, int line, string text)>();
        var excluded = new List<(string file, int line, string text)>();
        foreach (var path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            string rel = path.Substring(root.Length + 1).Replace('\\', '/');
            var lines = File.ReadAllLines(path);
            var aliases = lines.Select(l => XAliasPattern.Match(l)).Where(m => m.Success).Select(m => m.Groups[1].Value).Distinct().ToList();
            var aliasNull = aliases.Count == 0 ? null : new Regex(@"\b(" + string.Join("|", aliases.Select(Regex.Escape)) + @")\s*[!=]=\s*null");
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.StartsWith("//")) continue;
                if (!(XSitePattern.IsMatch(lines[i]) || (aliasNull != null && aliasNull.IsMatch(lines[i])))) continue;
                string rest = XExcludePattern.Replace(lines[i], " ");
                bool still = XSitePattern.IsMatch(rest) || (aliasNull != null && aliasNull.IsMatch(rest));
                if (!still) excluded.Add((rel, i + 1, t));
                else found.Add((rel, i + 1, t));
            }
        }
        int listed = XSites.Sum(s => s.Count);
        Check("JX5: Present: X's list has at least 4 entries", XSites.Length >= 4, $"{XSites.Length} entries, {listed} lines");
        var used = new bool[found.Count];
        var stale = new List<string>();
        foreach (var s in XSites)
        {
            var at = Enumerable.Range(0, found.Count).Where(i => found[i].file == s.File && found[i].text.Contains(s.Text)).ToList();
            foreach (var i in at) used[i] = true;
            string lines = string.Join(",", at.Select(i => found[i].line));
            Info($"JX5 {s.File}:{(lines.Length > 0 ? lines : "?")} [{s.Covered}] {s.Reading}");
            if (at.Count != s.Count) stale.Add($"{s.File}: \"{s.Text}\" expected {s.Count}, found {at.Count}");
        }
        Check("JX5: every listed site is in the tree (the list is not stale)", stale.Count == 0, string.Join("; ", stale));
        var known = new HashSet<string>(new[] { "JX1", "JX2", "JX3", "JX4", "JX5r", "JX6", "-" }, StringComparer.Ordinal);
        Check("JX5: each changed reading names the check that covers it", XSites.All(s => known.Contains(s.Covered) && (s.Covered != "-" || !s.Reading.StartsWith("changed:", StringComparison.Ordinal))), "");
        var unlisted = Enumerable.Range(0, found.Count).Where(i => !used[i]).Select(i => $"{found[i].file}:{found[i].line}: {found[i].text}").ToList();
        foreach (var x in excluded) Info($"JX5 excluded by the rule {XExcludeRule}: {x.file}:{x.line}: {x.text}");
        Info($"JX5: {found.Count + excluded.Count} lines found in Runtime/**, {found.Count - unlisted.Count} listed + {excluded.Count} excluded, {unlisted.Count} unlisted");
        if (unlisted.Count > 0) Warn("JX5W1 a Bridge null check or a bridge.enabled read that X's list does not name (its reading to review): " + string.Join(" | ", unlisted));
    }
}
