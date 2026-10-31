// C39 (Phase 5.3 package F): the commands of the folders, the first-run guide and the settings file (app.paths, app.setup,
// app.settings; CoreCommands.Phase53Names, group app, gate None) and the ui.* commands' gate None, on an edit-mode host.
// Nothing here writes a settings file: app.settings set is checked only for its refusals (the writes are SetupTest.play's, on a
// play session's own data root, and the mono group SettingsWriteTests').
using System;
using System.IO;
using System.Linq;
using HS2Next.Api.Bridge;
using HS2Next.App.Setup;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static partial class BridgeCoreTest
{
    static void C39(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        var names = HS2Next.App.Commands.CoreCommands.Phase53Names;
        c.Check(names.SequenceEqual(new[] { "app.paths", "app.setup", "app.settings" }), "Phase53Names: app.paths, app.setup, app.settings", string.Join(", ", names));
        c.Check(HS2Next.App.Commands.CoreCommands.AllNames.Count(n => names.Contains(n)) == 3, "CoreCommands.AllNames lists them once");
        foreach (var n in names)
        {
            var spec = reg.Find(n);
            c.Check(spec != null && spec.Group == "app" && spec.Gate == ReadyGate.None && spec.Since == "1.0.0", n + ": group app, gate None, Since 1.0.0",
                spec == null ? "missing" : spec.Group + " " + spec.Gate + " " + spec.Since);
        }
        foreach (var n in HS2Next.UI.Bridge.UiCommands.Names)
            c.Check(reg.Find(n)?.Gate == ReadyGate.None, n + ": gate None (drives the setup guide before the content stages)", reg.Find(n)?.Gate.ToString());
        c.Check(app.BridgeServices.Readiness.IsReady("setup"), "a test host's readiness has setup ready");

        // app.paths
        var p = c.Ok(new { cmd = "app.paths" }, "app.paths answers");
        c.Check((string)p["mode"] == "editor" && string.Equals((string)p["dataRoot"], HS2Next.Content.ContentSettings.ProjectRoot, StringComparison.OrdinalIgnoreCase),
            "app.paths: mode editor, the project as the data root", (string)p["mode"] + " " + (string)p["dataRoot"]);
        c.Check(string.Equals((string)p["userData"], app.Paths.UserData, StringComparison.OrdinalIgnoreCase) && (string)p["settingsFile"] == app.Paths.SettingsFile
            && p["cacheDir"]?.Type == JTokenType.String && p["sources"] is JObject && p["explicit"] is JArray && (int?)p["longestPath"] > 0,
            "app.paths: UserData, settings file, cache, sources, explicit, longestPath", Ctx.Short(p.ToString(Formatting.None)));

        // app.settings get and its refusals (the project's file is never written here)
        string file = app.Paths.SettingsFile;
        string before = File.Exists(file) ? File.ReadAllText(file) : null;
        var g = c.Ok(new { cmd = "app.settings" }, "app.settings answers");
        var values = g["values"] as JObject;
        var missing = SettingsSchema.All.Select(d => d.Key).Where(k => values?[k] == null).ToList();
        c.Check(values != null && missing.Count == 0, $"app.settings: every key of the table ({SettingsSchema.All.Count})", string.Join(", ", missing));
        c.Check(values?.Properties().All(pr => pr.Value["source"] != null && pr.Value["restart"] != null) == true, "every key has its source and restart");
        c.Check((bool?)values?["bridge.token"]?["value"] == false || (string)values?["bridge.token"]?["source"] != "default", "the bridge token's default in the Editor: off");
        c.Check(g["unknownKeys"] is JArray, "app.settings: unknownKeys is a list");
        c.Fails(new { cmd = "app.settings", op = "set", values = new JObject { ["bridge.tokn"] = true } }, "bad_args", "an unknown key is refused");
        var r1 = c.Call(new { cmd = "app.settings", op = "set", values = new JObject { ["bridge.tokn"] = true } });
        c.Check(((string)r1["error"] ?? "").Contains("did you mean bridge.token"), "the refusal names the nearest key", (string)r1["error"]);
        c.Fails(new { cmd = "app.settings", op = "set", values = new JObject { ["bridge.port"] = "17400" } }, "bad_args", "a wrong type is refused (bridge.port as a string)");
        c.Fails(new { cmd = "app.settings", op = "set", values = new JObject { ["maps.maxMirrors"] = 17 } }, "bad_args", "a value out of range is refused (maps.maxMirrors 17)");
        c.Fails(new { cmd = "app.settings", op = "nope" }, "bad_args", "an unknown op is refused");
        c.Check((File.Exists(file) ? File.ReadAllText(file) : null) == before, "the settings file is unchanged by the refusals");

        // app.setup (the test host has no setup stage: not needed; the install of the project's settings is checked)
        var s = c.Ok(new { cmd = "app.setup", suggestions = false }, "app.setup answers");
        c.Check((bool?)s["needed"] == false && s["page"]?.Type == JTokenType.Null, "app.setup: not needed, no page on a test host");
        c.Check((string)s["hs2"]?["level"] == "ok" && (int?)s["hs2"]?["zipmods"] > 0, "app.setup: the HS2 folder is ok with its zipmod count", Ctx.Short(s["hs2"]?.ToString(Formatting.None)));
        c.Check((double?)s["cache"]?["freeGB"] > 0, "app.setup: the cache's free space", s["cache"]?.ToString(Formatting.None));
        c.Fails(new { cmd = "app.setup", op = "set", hs2Root = Path.Combine(app.Paths.UserData, "no such folder") }, "bad_args", "app.setup set: a folder that is not an install is refused");
        c.Fails(new { cmd = "app.setup", op = "migrate", source = "" }, "bad_args", "app.setup migrate without an offer or a source is refused");
        c.Fails(new { cmd = "app.setup", op = "nope" }, "bad_args", "app.setup: an unknown op is refused");

        // ui.* on a test host: no UI
        var u = c.Ok(new { cmd = "ui.state" }, "ui.state answers on a test host");
        c.Check((bool?)u["enabled"] == false, "ui.state: enabled false without the UI");
        c.Fails(new { cmd = "ui.query", name = "setup.next" }, "unavailable", "ui.query: unavailable without the UI (a test host)");
    }
}
