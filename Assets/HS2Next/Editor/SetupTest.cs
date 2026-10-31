// SetupTest.play (Phase 5.3 package F): the first-run guide in an Editor play session with the Studio UI, driven over the bridge
// (port 17398) as an automation client drives it. The session's data root is a fresh folder under Spike/out/setup (the launch
// override's dataRoot), so the project's settings file is never written. Run (graphics, -NoQuit):
//   & Tools\unity_batch.ps1 -Method SetupTest.Run -NoQuit      report Spike/out/setup/setup_play_report.txt
// Checks:
//   FR5a  no HS2 folder: the setup stage shows the guide (ui.state mode setup, page welcome; ping.ready.setup false with the page in
//         readyDetail; app.setup needed); pages by ui.action setup.next; the files page names the data root
//   SG2   the settings file's misspelled key: the log line with the nearest key, app.settings.unknownKeys with the suggestion, the
//         warning toast once the Studio is up
//   FR5b  Skip setup (the question answered): setup.json skipped, the boot continues, the Studio starts without HS2 content
//         (0 items), the banner shows; app.paths names the session's folders
//   SG4   app.settings set writes a key keeping the comment and the unknown member in their order, refuses a wrong type and an
//         unknown key without writing, reports restart and the source per key
//   FR5c  a new app with -setup (the launch override's "setup": true) reopens the guide on the page it was left; the HS2 folder
//         typed (validation ok with the zipmod count), the project's warm cache chosen, Build the index writes exactly hs2Root and
//         cacheDir, the progress page turns into the ready page at studioDb, Finish closes the guide, the Studio is ready with
//         content and without the banner, setup.json completed
//   FR5d  a third app with -setup and the valid settings file: the HS2 page shows and checks the folder without a keystroke and
//         its Next is enabled; the re-run reaches the ready page and leaves the settings file as it was
//   SG1   File ▸ Options' settings sections: a Content toggle and an Advanced number written through their controls land in the
//         settings file with the comment, the unknown members and their order kept; the changed rows say "applies after restart"
//         and Restart now shows; a key the command line sets is locked; File ▸ Setup guide is in the File menu
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.App;
using HS2Next.App.Setup;
using HS2Next.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static class SetupTest
{
    public const int Port = 17398;
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "setup");
    static string ReportPath => Path.Combine(OutDir, "setup_play_report.txt");
    static string RootFile => Path.Combine(OutDir, "setup_play_root.txt");

    static string LaunchFor(string dataRoot, bool setup) =>
        "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},\"bridge\":{\"allowQuit\":true,\"port\":" + Port + "},\"dataRoot\":" +
        JsonConvert.ToString(dataRoot) + (setup ? ",\"setup\":true" : "") + "}";

    /// <summary>-executeMethod entry (Tools/unity_batch.ps1 -NoQuit; graphics).</summary>
    public static void Run()
    {
        Directory.CreateDirectory(OutDir);
        string root = Path.Combine(OutDir, "root_" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(Path.Combine(root, "Config"));
        // a settings file without an HS2 folder, with a comment, an unknown member and a misspelled key
        File.WriteAllText(Path.Combine(root, "Config", "settings.local.json"),
            "{\n  \"_comment\": \"kept by the settings writer\",\n  \"futureKey\": {\"a\": 1},\n  \"bridge\": {\"tokn\": true}\n}\n", new UTF8Encoding(false));
        File.WriteAllText(RootFile, root, new UTF8Encoding(false));
        File.WriteAllText(ReportPath, "SUMMARY: 0 PASS, 1 FAIL (the play session did not finish)\n", new UTF8Encoding(false));
        PlayModeHarness.Start("SetupTest.Play", ReportPath, 20, true, LaunchFor(root, false));
    }

    static readonly List<string> logLines = new List<string>();
    static void OnLog(string message, string stack, LogType type)
    {
        if (message != null && (message.StartsWith("[settings]") || message.StartsWith("[setup]") || message.StartsWith("[app] setup"))) lock (logLines) logLines.Add(message);
    }

    static IEnumerator Play()
    {
        var c = new AcceptanceCtx();
        var total = System.Diagnostics.Stopwatch.StartNew();
        Application.logMessageReceived += OnLog;
        string root = File.ReadAllText(RootFile).Trim();
        string settingsFile = Path.Combine(root, "Config", "settings.local.json");
        string ud = Path.Combine(root, "UserData");
        c.Client = new BridgeClient("http://127.0.0.1:" + Port + "/");
        c.Rep.AppendLine($"Setup play test (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}); data root {root}");
        var ok = new bool[1];
        try
        {
            // ---------------- FR5a: the guide
            c.Id = "FR5a";
            yield return PlayModeHarness.WaitUntil(() => HS2Next.UI.Setup.SetupHost.Current != null && HS2Next.UI.Setup.SetupHost.Current.IsReady, 120, ok);
            c.Check(ok[0], "the setup stage shows the guide (setup host ready)");
            if (!ok[0]) yield break;
            yield return c.Call(new { cmd = "ui.state" });
            c.Check((string)c.J["mode"] == "setup" && (string)c.J["page"] == "welcome", "ui.state: mode setup, page welcome", AcceptanceCtx.Short(c.J.ToString(Formatting.None)));
            yield return c.Call(new { cmd = "ping" });
            c.Check(c.J["ready"]?["setup"]?.Value<bool>() == false && ((string)c.J["readyDetail"]?["setup"] ?? "").Contains("welcome") && c.J["appReady"]?.Value<bool>() == false,
                "ping: setup not ready, readyDetail.setup names the page, app not ready", AcceptanceCtx.Short(c.J.ToString(Formatting.None)));
            yield return c.Call(new { cmd = "app.setup", suggestions = false });
            c.Check(c.Last.Ok && (bool?)c.J["needed"] == true && (string)c.J["page"] == "welcome", "app.setup: needed, page welcome", AcceptanceCtx.Short(c.Last.ToString()));
            yield return c.Call(new { cmd = "ui.query", name = ".unity-button", all = true });
            var names = (c.J["elements"] as JArray)?.Select(e => (string)e["name"]).ToList() ?? new List<string>();
            c.Check(names.Contains("setup.next") && names.Contains("setup.quit") && names.Contains("setup.language.ja"), "the welcome page's buttons (setup.next, setup.quit, setup.language.*)", string.Join(", ", names.Take(20)));
            yield return c.Call(new { cmd = "ui.action", action = "setup.next" });
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "ui.query", name = "setup.files.root" });
            string shownRoot = (string)c.J["elements"]?[0]?["text"];
            c.Check(string.Equals(shownRoot, root, StringComparison.OrdinalIgnoreCase), "the files page names the data root", shownRoot);
            yield return c.Call(new { cmd = "ui.query", name = "setup.skip" });
            c.Check((bool?)c.J["elements"]?[0]?["visible"] == true && (bool?)c.J["elements"]?[0]?["enabled"] == true, "Skip setup is offered on page 1");

            // ---------------- SG2: the misspelled key
            c.Id = "SG2";
            // the line was logged at the setup stage, before this coroutine ran: the bridge's log tail holds it
            yield return c.Call(new { cmd = "log.tail", lines = 500 });
            var lines = (c.J["entries"] as JArray)?.Select(e => (string)e["message"] ?? "").Where(m => m.StartsWith("[settings]")).ToList() ?? new List<string>();
            c.Check(lines.Any(l => l.Contains("bridge.tokn (did you mean bridge.token?)")), "the log names the misspelled key with the nearest key", string.Join(" | ", lines.Take(5)));
            yield return c.Call(new { cmd = "app.settings" });
            var unk = c.J["unknownKeys"] as JArray;
            c.Check(unk != null && unk.Any(u => (string)u["key"] == "bridge.tokn" && (string)u["suggestion"] == "bridge.token") && unk.Any(u => (string)u["key"] == "futureKey"),
                "app.settings.unknownKeys: bridge.tokn with bridge.token, futureKey", AcceptanceCtx.Short(unk?.ToString(Formatting.None)));
            c.Check((string)c.J["file"] == settingsFile, "app.settings names the session's settings file", (string)c.J["file"]);

            // ---------------- FR5b: skip
            c.Id = "FR5b";
            yield return c.Call(new { cmd = "ui.action", action = "setup.skip" });
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "ui.state" });
            c.Check((string)c.J["modal"] == "setup.skip.title", "Skip setup asks first", (string)c.J["modal"]);
            yield return c.Call(new { cmd = "ui.action", action = "dialog-button-ok" });
            yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage == AppStage.Ready && HS2Next.UI.UiHost.Current != null && HS2Next.UI.UiHost.Current.IsReady, 180, ok);
            c.Check(ok[0] && AppCore.Current.SetupSkipped, "after the skip the boot ends ready with the Studio UI", AppCore.Current?.Error);
            var st = SetupState.Load(ud);
            c.Check(st.Skipped && st.GuideVersion == SetupStage.GuideVersion && st.SkippedAt != null, "setup.json: skipped for this guide version");
            c.Check(AppCore.Current?.Db != null && AppCore.Current.Db.Items.Count == 0 && AppCore.Current.Catalog.Count == 0, "the Studio starts without HS2 content (0 rows, 0 items)",
                AppCore.Current?.Db?.Items.Count + " items");
            yield return c.Frames(5);
            yield return c.Call(new { cmd = "ui.query", name = "setup.banner.label" });
            c.Check((bool?)c.J["elements"]?[0]?["visible"] == true && ((string)c.J["elements"]?[0]?["text"] ?? "").Length > 0, "the banner shows", AcceptanceCtx.Short(c.J.ToString(Formatting.None)));
            yield return c.Call(new { cmd = "ui.state" });
            var toasts = (c.J["toasts"] as JArray)?.Select(t => (string)t).ToList() ?? new List<string>();
            c.Check(toasts.Any(t => t != null && t.Contains("bridge.tokn") && t.Contains("bridge.token")), "SG2: the warning toast lists the misspelled key with the nearest key", string.Join(" | ", toasts));
            yield return c.Call(new { cmd = "ping" });
            c.Check(c.J["appReady"]?.Value<bool>() == true && c.J["ready"]?["setup"]?.Value<bool>() == true, "ping: setup ready, app ready");
            yield return c.Call(new { cmd = "app.paths" });
            c.Check((string)c.J["mode"] == "override" && string.Equals((string)c.J["dataRoot"], root, StringComparison.OrdinalIgnoreCase) && (string)c.J["settingsFile"] == settingsFile
                && string.Equals((string)c.J["userData"], ud, StringComparison.OrdinalIgnoreCase) && c.J["hs2Root"]?.Type == JTokenType.Null,
                "app.paths: mode override, the data root, settings file and UserData of the session, no HS2 folder", AcceptanceCtx.Short(c.J.ToString(Formatting.None)));
            c.Info("app.paths: " + AcceptanceCtx.Short(c.J.ToString(Formatting.None)));

            // ---------------- SG4: app.settings set on the session's file
            c.Id = "SG4";
            string before = File.ReadAllText(settingsFile);
            yield return c.Call(new { cmd = "app.settings", op = "set", values = new { maps_maxMirrors = 0 } });
            c.Check(!c.Last.Ok && c.Last.Code == "bad_args", "an unknown key is refused", c.Last.ToString());
            yield return c.Call("{\"cmd\":\"app.settings\",\"op\":\"set\",\"values\":{\"bridge.port\":\"17400\"}}");
            c.Check(!c.Last.Ok && c.Last.Code == "bad_args", "a wrong type is refused (bridge.port as a string)", c.Last.ToString());
            c.Check(File.ReadAllText(settingsFile) == before, "nothing is written when a value is refused");
            yield return c.Call("{\"cmd\":\"app.settings\",\"op\":\"set\",\"values\":{\"maps.maxMirrors\":6}}");
            c.Check(c.Last.Ok && (c.J["written"] as JArray)?.Select(x => (string)x).SequenceEqual(new[] { "maps.maxMirrors" }) == true, "set maps.maxMirrors 6 writes that key", c.Last.ToString());
            var o = JObject.Parse(File.ReadAllText(settingsFile));
            c.Check(string.Join(",", o.Properties().Select(p => p.Name)) == "_comment,futureKey,bridge,maps" && (string)o["_comment"] == "kept by the settings writer" && (bool?)o["bridge"]?["tokn"] == true && (int?)o["maps"]?["maxMirrors"] == 6,
                "the file keeps the comment, the unknown members and their order", o.ToString(Formatting.None));
            var v = c.J["values"]?["maps.maxMirrors"];
            c.Check((int?)v?["value"] == 6 && (string)v?["source"] == "file" && (bool?)v?["restart"] == true && (string)c.J["values"]?["bridge.port"]?["source"] == "default",
                "app.settings: value, source and restart per key", AcceptanceCtx.Short(c.J["values"]?["maps.maxMirrors"]?.ToString(Formatting.None)));

            // ---------------- FR5c: -setup reopens the guide; the guide walked to the end
            c.Id = "FR5c";
            var first = HS2NextApp.Instance;
            if (first != null) UnityEngine.Object.Destroy(first.gameObject);
            yield return PlayModeHarness.WaitUntil(() => HS2NextApp.Instance == null && AppCore.Current == null, 30, ok);
            c.Check(ok[0], "the first app is closed");
            yield return c.Frames(5);
            UnityEditor.SessionState.SetString(AppLaunch.SessionKey, LaunchFor(root, true));
            string launch = AppLaunch.Read();
            AppSettings s2 = null;
            string err = null;
            try { s2 = AppSettings.Load(Environment.GetCommandLineArgs(), launch); }
            catch (Exception e) { err = e.Message; }
            c.Check(s2 != null, "the second app's settings load", err);
            if (s2 == null) yield break;
            AppBootstrap.Start(s2);
            yield return PlayModeHarness.WaitUntil(() => HS2Next.UI.Setup.SetupHost.Current != null && HS2Next.UI.Setup.SetupHost.Current.IsReady, 120, ok);
            c.Check(ok[0], "-setup reopens the guide although it was skipped");
            if (!ok[0]) yield break;
            yield return c.Call(new { cmd = "ui.state" });
            c.Check((string)c.J["page"] == "files", "the guide resumes on the page it was left (files)", (string)c.J["page"]);
            yield return c.Call(new { cmd = "ui.action", action = "setup.next" });
            yield return c.Frames(3);
            string hs2 = null;
            try { hs2 = ContentSettings.Load(Path.Combine(ContentSettings.ProjectRoot, "Config", "settings.local.json")).hs2Root; }
            catch (Exception e) { c.Info("the project's settings file: " + e.Message); }
            hs2 = string.IsNullOrEmpty(hs2) ? null : Path.GetFullPath(Path.Combine(ContentSettings.ProjectRoot, hs2));
            c.Check(hs2 != null && Directory.Exists(hs2), "the reference install is known (project settings)", hs2);
            yield return c.Call(new { cmd = "ui.action", action = "setup.hs2.path", args = new { value = hs2 } });
            c.Check(c.Last.Ok, "the HS2 folder typed into setup.hs2.path", c.Last.ToString());
            string status = null;
            for (int i = 0; i < 300; i++)
            {
                yield return c.Call(new { cmd = "ui.query", name = "setup.hs2.status" });
                status = (string)c.J["elements"]?[0]?["text"];
                if (status != null && System.Text.RegularExpressions.Regex.IsMatch(status, @"\d+ mods")) break;
                yield return c.Frames(5);
            }
            c.Check(status != null && status.StartsWith("Honey Select 2 found") && System.Text.RegularExpressions.Regex.IsMatch(status, @"\d+ mods"), "validation ok with the zipmod count", status);
            yield return c.Call(new { cmd = "ui.query", name = "setup.hs2.status" });
            c.Check(((JArray)c.J["elements"]?[0]?["classes"])?.Any(x => (string)x == "hs-setup-status--ok") == true, "the status line is marked ok");
            yield return c.Call(new { cmd = "ui.action", action = "setup.next" });   // → mods
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "ui.query", name = "setup.mods.install" });
            c.Check(((string)c.J["elements"]?[0]?["text"] ?? "").Contains(Path.Combine(hs2, "mods")), "the mods page shows the install's mods folder", (string)c.J["elements"]?[0]?["text"]);
            yield return c.Call(new { cmd = "ui.action", action = "setup.next" });   // → cache
            yield return c.Frames(3);
            string warmCache = Path.Combine(ContentSettings.ProjectRoot, "Cache");
            yield return c.Call(new { cmd = "ui.action", action = "setup.cache.path", args = new { value = warmCache } });
            yield return c.Frames(2);
            yield return c.Call(new { cmd = "ui.query", name = "setup.cache.free" });
            c.Check(((string)c.J["elements"]?[0]?["text"] ?? "").Contains("GB"), "the cache page shows the free space", (string)c.J["elements"]?[0]?["text"]);
            yield return c.Call(new { cmd = "ui.action", action = "setup.next" });   // Build the index
            yield return c.Frames(2);
            var written = JObject.Parse(File.ReadAllText(settingsFile));
            c.Check(string.Equals((string)written["hs2Root"], hs2, StringComparison.OrdinalIgnoreCase) && string.Equals((string)written["cacheDir"], warmCache, StringComparison.OrdinalIgnoreCase)
                && written["modsFolders"] == null && (string)written["_comment"] == "kept by the settings writer" && (int?)written["maps"]?["maxMirrors"] == 6,
                "the guide wrote hs2Root and cacheDir and kept the other members", written.ToString(Formatting.None));
            yield return PlayModeHarness.WaitUntil(() => HS2Next.UI.Setup.SetupHost.Current == null || HS2Next.UI.Setup.SetupHost.Current.Page == "ready", 300, ok);
            c.Check(ok[0] && HS2Next.UI.Setup.SetupHost.Current?.Page == "ready", "the index page turns into the ready page at studioDb");
            yield return c.Call(new { cmd = "ui.query", name = "setup.ready.bridge" });
            c.Check(c.Last.Ok && (int?)c.J["count"] == 1, "the ready page has its tips");
            yield return c.Call(new { cmd = "ui.action", action = "setup.finish" });
            yield return PlayModeHarness.WaitUntil(() => HS2Next.UI.Setup.SetupHost.Current == null && AppCore.Current != null && AppCore.Current.Stage == AppStage.Ready && HS2Next.UI.UiHost.Current != null && HS2Next.UI.UiHost.Current.IsReady, 120, ok);
            c.Check(ok[0], "Finish closes the guide; the Studio is ready", AppCore.Current?.Error);
            c.Check(AppCore.Current?.Db != null && AppCore.Current.Db.Items.Count > 0 && AppCore.Current.Paths.Hs2Root != null, "the Studio has HS2 content", AppCore.Current?.Db?.Items.Count + " items");
            yield return c.Frames(5);
            yield return c.Call(new { cmd = "ui.state" });
            c.Check((string)c.J["mode"] == null && c.J["layout"] != null, "ui.state now answers for the Studio UI");
            yield return c.Call(new { cmd = "ui.query", name = "setup.banner" });
            c.Check((int?)c.J["count"] == 0, "no banner with an HS2 folder");
            var st2 = SetupState.Load(ud);
            c.Check(st2.CompletedAt != null && !st2.Skipped, "setup.json: completed");
            yield return c.Call(new { cmd = "ping" });
            c.Check(c.J["appReady"]?.Value<bool>() == true, "ping.appReady");
            lock (logLines) c.Info("log: " + string.Join(" | ", logLines.Where(l => l.StartsWith("[app] setup")).Take(8)));

            // ---------------- SG1: the settings sections of File ▸ Options
            c.Id = "SG1";
            HS2Next.UI.Panels.OptionsSettingsSections.ArgsOverride = new[] { "HS2Next", "-bridgePort", Port.ToString() };
            yield return c.Call(new { cmd = "ui.action", action = "file.options" });
            yield return c.Frames(5);
            var op = HS2Next.UI.Panels.OptionsPanel.Open;
            c.Check(op != null && op.SettingsControls.Count >= 15, "File ▸ Options has the settings sections", op?.SettingsControls.Count + " settings rows");
            if (op != null)
            {
                string sg1Before = File.ReadAllText(settingsFile);
                var names1 = JObject.Parse(sg1Before).Properties().Select(pp => pp.Name).ToList();
                var acc = op.SettingsControls.TryGetValue("studio.accessoryItems", out var a1) ? a1 as UnityEngine.UIElements.Toggle : null;
                var mirrors = op.SettingsControls.TryGetValue("maps.maxMirrors", out var a2) ? a2 as UnityEngine.UIElements.IntegerField : null;
                c.Check(acc != null && mirrors != null && acc.value && mirrors.value == 6, "the rows show the file's values and the defaults (accessory items true, mirrors 6)", acc?.value + " " + mirrors?.value);
                if (acc != null) acc.value = false;
                if (mirrors != null) mirrors.value = 3;
                yield return c.Frames(3);
                var after = JObject.Parse(File.ReadAllText(settingsFile));
                var names2 = after.Properties().Select(pp => pp.Name).ToList();
                c.Check(names2.Take(names1.Count).SequenceEqual(names1) && names2.Last() == "studio" && (string)after["_comment"] == "kept by the settings writer" && (bool?)after["bridge"]?["tokn"] == true
                    && (int?)after["futureKey"]?["a"] == 1, "the file keeps the comment, the unknown members and their order (a new block goes last)", string.Join(",", names2));
                c.Check((bool?)after["studio"]?["accessoryItems"] == false && (int?)after["maps"]?["maxMirrors"] == 3, "the two rows were written", after.ToString(Formatting.None));
                yield return c.Call(new { cmd = "ui.query", name = "setting-restart-maps.maxMirrors" });
                c.Check(((string)c.J["elements"]?[0]?["text"] ?? "").Contains("applies after restart") && ((string)c.J["elements"]?[0]?["text"] ?? "").StartsWith("changed"), "the changed row says it applies after a restart", (string)c.J["elements"]?[0]?["text"]);
                yield return c.Call(new { cmd = "ui.query", name = "settings-restart-advanced" });
                c.Check((bool?)c.J["elements"]?[0]?["visible"] == true, "Restart now shows after a change", AcceptanceCtx.Short(c.J.ToString(Formatting.None)));
                var port = op.SettingsControls.TryGetValue("bridge.port", out var a3) ? a3 : null;
                c.Check(port != null && !port.enabledSelf, "a key the command line sets is locked (bridge.port)");
                var token = op.SettingsControls.TryGetValue("bridge.token", out var a4) ? a4 : null;
                c.Check(token != null && token.enabledSelf, "the other rows are editable (bridge.token)");
                yield return c.Call(new { cmd = "ui.state" });
                c.Check((string)c.J["modal"] == "options", "the options dialog is open", (string)c.J["modal"]);
                HS2Next.UI.UiHost.Current?.Context?.Dialogs?.CloseCurrent();
            }
            HS2Next.UI.Panels.OptionsSettingsSections.ArgsOverride = null;
            var fileMenu = HS2Next.UI.UiHost.Current?.Context?.Registry?.Actions;
            c.Check(fileMenu != null && fileMenu.Contains(HS2Next.UI.Core.ActionIds.FileSetupGuide), "File ▸ Setup guide is registered");
            yield return c.Call(new { cmd = "ui.action", action = "file.setupGuide" });
            c.Check(c.Last.Ok, "File ▸ Setup guide runs (an Editor play session: a note on how to restart)", c.Last.ToString());

            // ---------------- FR5d: -setup with a valid settings file: the HS2 page lets Next through without a keystroke
            c.Id = "FR5d";
            var second = HS2NextApp.Instance;
            if (second != null) UnityEngine.Object.Destroy(second.gameObject);
            yield return PlayModeHarness.WaitUntil(() => HS2NextApp.Instance == null && AppCore.Current == null, 30, ok);
            c.Check(ok[0], "the second app is closed");
            yield return c.Frames(5);
            AppSettings s3 = null;
            try { s3 = AppSettings.Load(Environment.GetCommandLineArgs(), AppLaunch.Read()); }
            catch (Exception e) { err = e.Message; }
            c.Check(s3 != null, "the third app's settings load", err);
            if (s3 == null) yield break;
            string settingsBefore = File.ReadAllText(settingsFile);
            AppBootstrap.Start(s3);
            yield return PlayModeHarness.WaitUntil(() => HS2Next.UI.Setup.SetupHost.Current != null && HS2Next.UI.Setup.SetupHost.Current.IsReady, 120, ok);
            c.Check(ok[0], "-setup reopens the guide although the settings name a valid HS2 folder");
            if (!ok[0]) yield break;
            yield return c.Call(new { cmd = "ui.state" });
            c.Check((string)c.J["page"] == "welcome", "a completed guide reopens on the welcome page", (string)c.J["page"]);
            yield return c.Call(new { cmd = "ui.action", action = "setup.next" });
            yield return c.Frames(2);
            yield return c.Call(new { cmd = "ui.action", action = "setup.next" });
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "ui.state" });
            c.Check((string)c.J["page"] == "hs2", "two Nexts reach the HS2 page", (string)c.J["page"]);
            yield return c.Call(new { cmd = "ui.query", name = "setup.hs2.path" });
            c.Check(string.Equals((string)c.J["elements"]?[0]?["value"], hs2, StringComparison.OrdinalIgnoreCase), "the HS2 page shows the folder of the settings", (string)c.J["elements"]?[0]?["value"]);
            yield return c.Call(new { cmd = "ui.query", name = "setup.hs2.status" });
            c.Check(((string)c.J["elements"]?[0]?["text"] ?? "").StartsWith("Honey Select 2 found"), "the folder is checked without a keystroke", (string)c.J["elements"]?[0]?["text"]);
            yield return c.Call(new { cmd = "ui.query", name = "setup.next" });
            c.Check((bool?)c.J["elements"]?[0]?["enabled"] == true, "Next is enabled on the HS2 page (a manual walk-through: it stayed disabled)", AcceptanceCtx.Short(c.J.ToString(Formatting.None)));
            yield return c.Call(new { cmd = "ui.action", action = "setup.next" });   // → mods
            yield return c.Frames(2);
            yield return c.Call(new { cmd = "ui.action", action = "setup.next" });   // → cache
            yield return c.Frames(2);
            yield return c.Call(new { cmd = "ui.query", name = "setup.cache.path" });
            c.Check(string.Equals((string)c.J["elements"]?[0]?["value"], warmCache, StringComparison.OrdinalIgnoreCase), "the cache page shows the cache of the settings", (string)c.J["elements"]?[0]?["value"]);
            yield return c.Call(new { cmd = "ui.action", action = "setup.next" });   // Build the index
            yield return PlayModeHarness.WaitUntil(() => HS2Next.UI.Setup.SetupHost.Current == null || HS2Next.UI.Setup.SetupHost.Current.Page == "ready", 300, ok);
            c.Check(ok[0] && HS2Next.UI.Setup.SetupHost.Current?.Page == "ready", "the re-run reaches the ready page");
            c.Check(File.ReadAllText(settingsFile) == settingsBefore, "the unchanged choices leave the settings file as it was");
            yield return c.Call(new { cmd = "ui.action", action = "setup.finish" });
            yield return PlayModeHarness.WaitUntil(() => HS2Next.UI.Setup.SetupHost.Current == null && AppCore.Current != null && AppCore.Current.Stage == AppStage.Ready && HS2Next.UI.UiHost.Current != null && HS2Next.UI.UiHost.Current.IsReady, 120, ok);
            c.Check(ok[0] && AppCore.Current?.Db != null && AppCore.Current.Db.Items.Count > 0, "Finish: the Studio is ready with content", AppCore.Current?.Error);
        }
        finally
        {
            Application.logMessageReceived -= OnLog;
            File.WriteAllText(ReportPath, c.Report(total.Elapsed.TotalSeconds), new UTF8Encoding(false));
            Debug.Log($"SETUP PLAY DONE: {c.Passed} PASS, {c.Failed} FAIL, {c.Warned} WARN");
            PlayModeHarness.ExitCode = c.Failed > 0 ? 1 : 0;
        }
    }
}
