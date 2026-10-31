// C37: plugins in an edit-mode test host (the plugin rows). The row stages the plugin set into
// Spike/out/plugins/core/ (Tools/plugins.ps1 -Build all -Stage; the check stages it itself when a plugin is missing); the
// check adds the refusal fixtures (manifests only) beside them and loads the folder into a test host with
// AppCore.LoadPlugins. Parts (each with its own host): PL5 load states and refusal codes, PL6 order, PL7 lifecycle,
// PL8 events, PL9 the in-process calls, PL10 faults (with the ext block written back unchanged), PL11 the logical unload,
// PL12 the sample's commands counted. Then every static CK<n>(Ctx) and PX<n>(Ctx) of this partial class (other packages'
// checks of the command layer and the engine points), CK first, in number order. -c37 PL5,CK2 runs some parts only.
// The test plugins log their lifecycle to a process-wide list (AppDomain data "stp3.test.lifecycle").
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HS2Next.App;
using HS2Next.App.Plugins;
using HS2Next.Content;
using HS2Next.Save;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Debug = UnityEngine.Debug;

public static partial class BridgeCoreTest
{
    /// <summary>The staged plugin folder of the C37 row.</summary>
    public static string C37Dir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "plugins", "core");
    static readonly string[] C37Plugins = { "stp3.sample.sequencer", "stp3.test.probe", "stp3.test.probecore", "stp3.test.faulty", "stp3.test.dependent" };
    /// <summary>Staged test plugins with real assemblies that must not load: Load throws, no IPlugin type, a missing dependency.</summary>
    static readonly string[] C37Refusing = { "stp3.test.loadthrows", "stp3.test.noentry", "stp3.test.typeload" };
    const string C37Disabled = "stp3.test.disabled";

    /// <summary>The refusal fixtures: folder, manifest text (null = no manifest), the code the loader must give.</summary>
    static readonly (string folder, string manifest, string code)[] C37Fixtures =
    {
        ("stp3.test.badmanifest", "{ not json", PluginCodes.BadManifest),
        ("stp3.test.mismatch", C37Manifest("stp3.test.mismatched"), PluginCodes.FolderMismatch),
        ("stp3.test.dupa", C37Manifest("stp3.test.dup"), PluginCodes.DuplicateGuid),
        ("stp3.test.dup", C37Manifest("stp3.test.dup"), PluginCodes.DuplicateGuid),
        ("hs2next.reserved", C37Manifest("hs2next.reserved"), PluginCodes.ReservedGuid),
        ("stp3.test.wrongapi", C37Manifest("stp3.test.wrongapi", api: "0.3"), PluginCodes.ApiVersion),
        ("stp3.test.newapp", C37Manifest("stp3.test.newapp", extra: "\"minAppVersion\":\"9.0.0\""), PluginCodes.AppVersion),
        ("stp3.test.missingdll", C37Manifest("stp3.test.missingdll", asm: "Missing.dll"), PluginCodes.MissingAssembly),
        ("stp3.test.collision", C37Manifest("stp3.test.collision", asm: "Newtonsoft.Json.dll"), PluginCodes.AssemblyCollision),
        ("stp3.test.prefix", C37Manifest("stp3.test.prefix", extra: "\"commandPrefixes\":[\"scene.\"]"), PluginCodes.PrefixTaken),
        ("stp3.test.needsabsent", C37Manifest("stp3.test.needsabsent", extra: "\"dependencies\":[{\"guid\":\"stp3.test.absent\"}]"), PluginCodes.Dependency),
        ("stp3.test.cyclea", C37Manifest("stp3.test.cyclea", extra: "\"dependencies\":[{\"guid\":\"stp3.test.cycleb\"}]"), PluginCodes.DependencyCycle),
        ("stp3.test.cycleb", C37Manifest("stp3.test.cycleb", extra: "\"dependencies\":[{\"guid\":\"stp3.test.cyclea\"}]"), PluginCodes.DependencyCycle),
        (C37Disabled, C37Manifest(C37Disabled), PluginCodes.Disabled),
        ("stp3.test.nomanifest", null, null),
    };

    static string C37Manifest(string guid, string api = "1.0", string asm = "Fixture.dll", string extra = null) =>
        "{\"manifestVersion\":1,\"guid\":\"" + guid + "\",\"name\":\"fixture\",\"version\":\"0.1.0\",\"apiVersion\":\"" + api + "\",\"assemblies\":[\"" + asm + "\"]" +
        (extra != null ? "," + extra : "") + "}";

    static List<string> C37Log()
    {
        var l = AppDomain.CurrentDomain.GetData("stp3.test.lifecycle") as List<string>;
        if (l == null) return new List<string>();
        lock (l) return new List<string>(l);
    }

    static void C37ClearLog()
    {
        if (AppDomain.CurrentDomain.GetData("stp3.test.lifecycle") is List<string> l) lock (l) l.Clear();
    }

    /// <summary>Stages the plugin set when the folder lacks one (plugins.ps1 -Build all -Stage), then writes the fixtures.</summary>
    static bool C37Stage(Ctx c)
    {
        string dir = C37Dir;
        bool complete = C37Plugins.Concat(C37Refusing).All(g => File.Exists(Path.Combine(dir, g, "manifest.json")));
        if (!complete || Array.IndexOf(TestArgs.Get(), "-c37Stage") >= 0)
        {
            var sw = Stopwatch.StartNew();
            string tool = Path.Combine(ContentSettings.ProjectRoot, "Tools", "plugins.ps1");
            var psi = new ProcessStartInfo("pwsh", "-NoProfile -File \"" + tool + "\" -Build all -Stage \"" + dir + "\"")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = ContentSettings.ProjectRoot,
            };
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                bool exited = p.WaitForExit(300000);
                c.Info("staged by the check: plugins.ps1 exit " + (exited ? p.ExitCode.ToString() : "timeout") + " in " + sw.Elapsed.TotalSeconds.ToString("F1") + " s");
                if (!exited || p.ExitCode != 0) { c.Check(false, "plugins.ps1 -Build all -Stage " + dir, Ctx.Short(o)); return false; }
            }
        }
        foreach (var (folder, manifest, _) in C37Fixtures)
        {
            string d = Path.Combine(dir, folder);
            Directory.CreateDirectory(d);
            if (manifest == null) continue;
            File.WriteAllText(Path.Combine(d, "manifest.json"), manifest, new UTF8Encoding(false));
            if (manifest.Contains("Fixture.dll")) File.WriteAllBytes(Path.Combine(d, "Fixture.dll"), new byte[] { 0 });
            if (manifest.Contains("\"Newtonsoft.Json.dll\"")) File.WriteAllBytes(Path.Combine(d, "Newtonsoft.Json.dll"), new byte[] { 0 });
        }
        return true;
    }

    /// <summary>A host with the C37 folder loaded (the disabled fixture on the disabled list).</summary>
    static (AppCore app, PluginHost host) C37Host(Ctx c)
    {
        C37ClearLog();
        var app = c.Host();
        var host = app.LoadPlugins(C37Dir, new[] { C37Disabled });
        return (app, host);
    }

    static string[] C37Parts()
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, "-c37");
        if (i < 0 || i + 1 >= a.Length) return null;
        return a[i + 1].Split(',', '+', ';').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
    }

    static void C37(Ctx c)
    {
        var parts = C37Parts();
        bool Want(string id) => parts == null || parts.Contains(id, StringComparer.OrdinalIgnoreCase);
        bool staged = C37Stage(c);
        var mine = new (string id, Action<Ctx> run)[]
        {
            ("PL5", C37Load), ("PL6", C37Order), ("PL7", C37Lifecycle), ("PL8", C37Events), ("PL9", C37Calls), ("PL10", C37Faults), ("PL11", C37Unload),
            ("PL12", C37Counted),
        };
        var others = typeof(BridgeCoreTest).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(m => (m, mm: Regex.Match(m.Name, @"^(CK|PX)(\d+)$")))
            .Where(x => x.mm.Success && x.m.GetParameters().Length == 1 && x.m.GetParameters()[0].ParameterType == typeof(Ctx))
            .OrderBy(x => x.mm.Groups[1].Value == "CK" ? 0 : 1).ThenBy(x => int.Parse(x.mm.Groups[2].Value))
            .Select(x => (id: x.m.Name, run: (Action<Ctx>)(cc => x.m.Invoke(null, new object[] { cc })))).ToList();
        c.Info("C37 parts: " + string.Join(", ", mine.Select(x => x.id).Concat(others.Select(x => x.id))));
        foreach (var (id, run) in mine.Concat(others))
        {
            if (!Want(id)) continue;
            if (id.StartsWith("PL", StringComparison.Ordinal) && !staged) continue;
            c.Id = "C37." + id;
            var sw = Stopwatch.StartNew();
            try { run(c); }
            catch (Exception e)
            {
                var ie = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                c.Check(false, "exception", ie.GetType().Name + ": " + ie.Message + "\n" + ie.StackTrace);
            }
            c.Info($"C37.{id}: {sw.Elapsed.TotalSeconds:F1} s");
            // each part's hosts end here (a plugin set unloads with its host)
            foreach (var h in C37Hosts(c)) c.Dispose(h);
        }
        c.Id = "C37";
    }

    /// <summary>The hosts the running check created (the context's private list; disposed after each part).</summary>
    static List<AppCore> C37Hosts(Ctx c) =>
        new List<AppCore>(typeof(Ctx).GetField("hosts", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(c) as List<AppCore> ?? new List<AppCore>());

    static JObject C37Plugin(JObject list, string guid) => (list["plugins"] as JArray)?.OfType<JObject>().FirstOrDefault(p => (string)p["guid"] == guid);

    // ---------------------------------------------------------------- PL5 load states and refusal codes

    static void C37Load(Ctx c)
    {
        var (app, host) = C37Host(c);
        var list = c.Ok(new { cmd = "plugin.list" }, "plugin.list");
        c.Check((string)list["source"] == "-pluginsDir" && (bool?)list["enabled"] == true && string.Equals((string)list["root"], Path.GetFullPath(C37Dir), StringComparison.OrdinalIgnoreCase),
            "plugin.list: source -pluginsDir, enabled, the staged root", Ctx.Short(list.ToString(Formatting.None)));
        foreach (var g in C37Plugins)
        {
            var p = C37Plugin(list, g);
            c.Check((string)p?["state"] == PluginStates.Loaded, g + " loaded", (string)p?["state"] + " " + (string)p?["reason"]);
            c.Info($"{g}: loadMs {p?["loadMs"]}, assemblies {p?["assemblies"]?.ToString(Formatting.None)}");
        }
        foreach (var (folder, manifest, code) in C37Fixtures)
        {
            if (code == null) continue;
            var e = host.Entries.FirstOrDefault(x => string.Equals(x.Candidate.FolderName, folder, StringComparison.OrdinalIgnoreCase));
            string want = code == PluginCodes.Disabled ? PluginStates.Disabled : PluginStates.Refused;
            c.Check(e != null && e.Code == code && e.State == want, folder + " -> " + code, e == null ? "no entry" : e.State + " " + e.Code + ": " + e.Reason);
        }
        var infos = (list["infos"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
        c.Check(infos.Contains(PluginCodes.NoManifest + ": stp3.test.nomanifest"), "a folder without a manifest is reported (plugin.noManifest)", string.Join("; ", infos));
        var ver = c.Ok(new { cmd = "bridge.version" }, "bridge.version");
        var vp = (ver["plugins"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        c.Check(vp.Count == host.Entries.Count && C37Plugins.All(g => vp.Any(x => (string)x["guid"] == g && (string)x["state"] == "loaded")),
            "bridge.version.plugins lists every candidate with its state", vp.Count + " entries");
        var ping = c.Ok(new { cmd = "probe.ping", n = 4 }, "a plugin command through the bridge");
        c.Check((int?)ping["twice"] == 8 && (string)ping["dep"] == "dep ok", "probe.ping answers, its lib dependency resolves", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "probecore.hooks" }, "the app-assembly probe answers");
        var help = c.Ok(new { cmd = "help", name = "probe.ping" }, "help probe.ping");
        c.Check((string)help["command"]?["owner"] == "stp3.test.probe" && (string)help["command"]?["group"] == "stp3.test.probe" && (string)help["command"]?["since"] == "0.1.0",
            "help: owner and group = the GUID, since = the plugin version", Ctx.Short(c.LastText));

        // the refusals that need real assemblies
        var lt = host.Find("stp3.test.loadthrows");
        c.Check(lt != null && lt.State == PluginStates.Failed && lt.Code == PluginCodes.LoadFailed && (lt.Reason ?? "").Contains("load throws on purpose"),
            "a plugin whose Load throws is failed (plugin.loadFailed)", lt == null ? "no entry" : lt.State + " " + lt.Code + ": " + lt.Reason);
        var reg = app.Bridge.Services.Commands;
        c.Check(!reg.Contains("loadthrows.ping") && reg.Specs.All(s => s.Owner != "stp3.test.loadthrows"), "… the command it registered before throwing is removed");
        c.Check(ExtRegistry.Default.NamespacesOf("stp3.test.loadthrows").Count == 0, "… its ext block handler is removed");
        c.Check(lt?.Events != null && lt.Events.HandlerCount == 0 && lt.Events.IsClosed, "… its event handlers are dropped", lt?.Events?.HandlerCount.ToString());
        bool ctxGone = false;
        try { var _ = lt?.Context?.Commands; }
        catch (ObjectDisposedException) { ctxGone = true; }
        c.Check(ctxGone, "… and its context is closed");
        c.Ok(new { cmd = "scene.new" }, "scene.new (no event reaches the failed plugin)");
        var lp = C37Plugin(c.Ok(new { cmd = "plugin.list" }, "plugin.list"), "stp3.test.loadthrows");
        c.Check((string)lp?["state"] == "failed" && (lp?["commands"] as JArray)?.Count == 0 && (lp?["extNamespaces"] as JArray)?.Count == 0 && (int?)lp?["events"] == 0,
            "plugin.list: failed, no commands, no ext namespaces, no event handlers", Ctx.Short(lp?.ToString(Formatting.None)));
        var ne = host.Find("stp3.test.noentry");
        c.Check(ne != null && ne.State == PluginStates.Refused && ne.Code == PluginCodes.NoEntry, "an assembly without an IPlugin type -> plugin.noEntry",
            ne == null ? "no entry" : ne.State + " " + ne.Code + ": " + ne.Reason);
        var tl = host.Find("stp3.test.typeload");
        c.Check(tl != null && tl.State == PluginStates.Refused && tl.Code == PluginCodes.TypeLoad && (tl.Reason ?? "").Length > 0,
            "an assembly whose types need an assembly that is not shipped -> plugin.typeLoad", tl == null ? "no entry" : tl.State + " " + tl.Code + ": " + tl.Reason);
        c.Check(tl?.Assemblies.Count == 1 && ne?.Assemblies.Count == 1, "… both assemblies were loaded first (the refusal comes from their types)");
    }

    // ---------------------------------------------------------------- PL6 order

    static void C37Order(Ctx c)
    {
        var (app, host) = C37Host(c);
        var order = host.Entries.Where(e => e.IsLoaded).Select(e => e.Guid).ToList();
        var want = new[] { "stp3.sample.sequencer", "stp3.test.faulty", "stp3.test.probe", "stp3.test.dependent", "stp3.test.probecore", "stp3.test.probeui" };
        c.Check(order.SequenceEqual(want), "load order: dependencies, then loadAfter, ties by GUID", string.Join(" ", order));
        var loads = C37Log().Where(l => l.StartsWith("load ", StringComparison.Ordinal)).Select(l => l.Substring(5)).ToList();
        c.Check(loads.SequenceEqual(want.Where(g => g == "stp3.test.faulty" || g == "stp3.test.probe" || g == "stp3.test.dependent")), "the plugins' Load calls ran in that order", string.Join(" ", loads));
        var list = c.Ok(new { cmd = "plugin.list" }, "plugin.list");
        var listed = (list["plugins"] as JArray).OfType<JObject>().Where(p => (string)p["state"] == "loaded").Select(p => (string)p["guid"]).ToList();
        c.Check(listed.SequenceEqual(want), "plugin.list lists the loaded plugins in load order", string.Join(" ", listed));
    }

    // ---------------------------------------------------------------- PL7 lifecycle

    static void C37Lifecycle(Ctx c)
    {
        var (app, host) = C37Host(c);
        var log = C37Log();
        c.Check(log.Count(l => l == "load stp3.test.probe") == 1, "Load once", string.Join(" | ", log));
        c.Check(log.Contains("sceneLoaded stp3.test.probe Initial") && log.Contains("sceneLoaded stp3.test.faulty Initial") && log.Contains("sceneLoaded stp3.test.dependent Initial"),
            "SceneLoaded(Initial) to every plugin with the interface");
        int u0 = (int)c.Ok(new { cmd = "probe.ping" }, "ping")["updates"];
        c.Frames(30);
        int u1 = (int)c.Ok(new { cmd = "probe.ping" }, "ping")["updates"];
        c.Check(u1 - u0 == 31, "Update once per frame (30 frames + the frame of the call)", (u1 - u0).ToString());
        c.Ok(new { cmd = "scene.new" }, "scene.new");
        c.Check(C37Log().Contains("sceneLoaded stp3.test.probe New"), "SceneLoaded(New) after scene.new");
        foreach (var f in Directory.GetFiles(app.Paths.SceneDir, "c37_*")) File.Delete(f);
        c.Ok(new { cmd = "item.add", no = 399 }, "item.add");
        c.Ok(new { cmd = "scene.save", name = "c37_life" }, "scene.save");
        c.Ok(new { cmd = "scene.load", name = "c37_life" }, "scene.load");
        log = C37Log();
        c.Check(log.Contains("sceneLoaded stp3.test.probe Load") && log.Any(l => l.StartsWith("event SceneLoaded stp3.test.probe Load c37_life.hs2nscene nodes 1", StringComparison.Ordinal)),
            "SceneLoaded(Load) with the path relative to the scene folder and the node count", string.Join(" | ", log.Where(l => l.StartsWith("event", StringComparison.Ordinal))));
        C37ClearLog();
        host.Shutdown();
        var un = C37Log().Where(l => l.StartsWith("unload ", StringComparison.Ordinal)).Select(l => l.Substring(7)).ToList();
        c.Check(un.SequenceEqual(new[] { "stp3.test.dependent", "stp3.test.probe", "stp3.test.faulty" }), "Unload once each, in reverse load order", string.Join(" ", un));
        host.Shutdown();
        c.Check(C37Log().Count(l => l.StartsWith("unload ", StringComparison.Ordinal)) == 3, "a second shutdown unloads nothing");
    }

    // ---------------------------------------------------------------- PL8 events

    static void C37Events(Ctx c)
    {
        var (app, host) = C37Host(c);
        foreach (var f in Directory.GetFiles(app.Paths.SceneDir, "c37_*")) File.Delete(f);
        c.Ok(new { cmd = "scene.new" }, "scene.new");
        string i1 = W1AddItem(c), i2 = W1AddItem(c);
        c.Ok(new { cmd = "item.parent", key = i2, parent = i1 }, "item.parent (move)");
        c.Ok(new { cmd = "delete", key = i2 }, "delete");
        c.Ok(new { cmd = "undo" }, "undo");
        c.Ok(new { cmd = "redo" }, "redo");
        c.Ok(new { cmd = "scene.save", name = "c37_events" }, "scene.save");
        c.Ok(new { cmd = "scene.pause", paused = true }, "scene.pause true");
        c.Ok(new { cmd = "scene.step", n = 3 }, "scene.step 3");
        c.Ok(new { cmd = "scene.pause", paused = false }, "scene.pause false");
        c.Ok(new { cmd = "scene.new" }, "scene.new (the removed node is forgotten)");
        c.Ok(new { cmd = "scene.load", name = "c37_events" }, "scene.load");
        string chara = C30Load(c, c.FemaleRel, "female", "chara.load");
        c.Frames(2);
        c.Ok(new { cmd = "probe.arm", request = new { cmd = "scene.new" } }, "probe.arm (scene.new from inside an event handler)");
        W1AddItem(c, "item.add (fires the armed handler)");
        var ev = c.Ok(new { cmd = "probe.events" }, "probe.events");
        var e = ev["events"] as JObject ?? new JObject();
        int N(string k) => (int?)e[k] ?? 0;
        c.Info("events: " + e.ToString(Formatting.None));
        c.Check(N("SceneCleared") >= 3 && N("SceneLoaded.New") >= 2 && N("SceneLoaded.Load") == 1, "SceneCleared / SceneLoaded New, Load");
        c.Check(N("NodesAdded.Command") >= 3 && N("NodesAdded.Undo") == 1, "NodesAdded with origins Command and Undo");
        c.Check(N("NodesRemoved.Command") == 1 && N("NodesRemoved.Redo") == 1, "NodesRemoved with origins Command and Redo");
        c.Check(N("NodesMoved") == 1, "NodesMoved");
        c.Check(N("NodeForgotten") >= 1, "NodeForgotten when scene.new drops the history holding the removed node");
        c.Check(N("SceneSaving") == 1 && N("SceneSaved.ok") == 1, "SceneSaving / SceneSaved");
        c.Check(N("PausedChanged.True") == 1 && N("PausedChanged.False") == 1 && N("SceneStepped") == 1, "PausedChanged and SceneStepped");
        c.Check(chara == null || N("CharaBuilt") >= 1, "CharaBuilt after a character build");
        c.Check(N("AppReady") == 1, "AppReady once (after the test host's plugins loaded)");
        c.Check((string)ev["armedReply"] == "busy", "a change from inside an event handler answers busy (inline call)", (string)ev["armedReply"]);
        c.Check(app.Session.Model.Count >= 1, "… and the scene was not replaced", app.Session.Model.Count.ToString());
    }

    // ---------------------------------------------------------------- PL9 Call / CallAsync

    static void C37Calls(Ctx c)
    {
        var (app, host) = C37Host(c);
        c.Ok(new { cmd = "scene.new" }, "scene.new");
        string item = W1AddItem(c);
        var reqs = new object[]
        {
            new { cmd = "camera.get" }, new { cmd = "item.search", q = "light", limit = 2 }, new { cmd = "scene.lights" }, new { cmd = "effects.state" }, new { cmd = "map.state" },
            new { cmd = "item.state", key = item }, new { cmd = "route.list" }, new { cmd = "pose.list" }, new { cmd = "sound.list" }, new { cmd = "anime.groups" },
            new { cmd = "item.search", q = "chair", limit = 3 }, new { cmd = "help", name = "scene.new" }, new { cmd = "history" }, new { cmd = "selection" },
            new { cmd = "scene.list", q = "c37_" }, new { cmd = "sound.state" }, new { cmd = "faulty.ok" },
            new { cmd = "obj.transform", key = item, pos = new[] { 1, 2, 3 }, undo = false }, new { cmd = "obj.visible", key = item, visible = true, undo = false },
            new { cmd = "obj.transform", key = item, rot = new[] { 0, 90, 0 }, undo = false }, new { cmd = "item.state", key = item, strict = true },
        };
        int same = 0;
        foreach (var rq in reqs)
        {
            string json = JsonConvert.SerializeObject(rq);
            var q = c.Call(json);
            var inline = c.Ok(new { cmd = "probe.call", request = JObject.Parse(json) }, "probe.call " + (string)JObject.Parse(json)["cmd"]);
            var i = inline["reply"] as JObject;
            bool eq = JToken.DeepEquals(q, i);
            if (eq) same++;
            else c.Check(false, "inline = queued for " + json, "queued " + Ctx.Short(q.ToString(Formatting.None)) + " | inline " + Ctx.Short(i?.ToString(Formatting.None)));
        }
        c.Check(same == reqs.Length, $"inline Call gives the queued reply for {reqs.Length} read and write commands", same + "/" + reqs.Length);
        var us = c.Ok(new { cmd = "probe.call", request = new { cmd = "camera.get" } }, "probe.call camera.get")["us"];
        c.Info("inline camera.get: " + us + " µs");
        foreach (var (rq, what) in new (object, string)[]
        {
            (new { cmd = "scene.load", name = "c37_none" }, "scene.load (Defers)"), (new { cmd = "batch", cmds = new object[] { new { cmd = "camera.get" } } }, "batch"),
            (new { cmd = "camera.get", waitFrames = 2 }, "waitFrames 2"),
        })
        {
            var r = c.Ok(new { cmd = "probe.call", request = rq }, "probe.call " + what)["reply"] as JObject;
            c.Check((string)r?["code"] == "unsupported", what + " is refused inline (unsupported)", Ctx.Short(r?.ToString(Formatting.None)));
        }
        // nesting: probe.call of probe.call ... 9 levels deep; the 9th answers busy
        JObject req = JObject.FromObject(new { cmd = "camera.get" });
        for (int k = 0; k < 9; k++) req = new JObject { ["cmd"] = "probe.call", ["request"] = req };
        var nest = c.Call(req.ToString(Formatting.None));
        int depth = 0;
        JObject cur = nest;
        while (cur?["reply"] is JObject inner && (bool?)inner["ok"] == true) { cur = inner; depth++; }
        string innerCode = (string)(cur?["reply"] as JObject)?["code"];
        c.Check(innerCode == "busy" && depth == 8, "inline calls nest 8 deep, the 9th answers busy", depth + " " + innerCode);
        // the app's gate: a write inline inside a client's batch (another party's transaction) answers busy
        var b = c.Call(new { cmd = "batch", cmds = new object[] { new { cmd = "probe.call", request = new { cmd = "obj.transform", key = item, pos = new[] { 4, 4, 4 }, undo = false } } } });
        var br = b["results"]?[0]?["reply"] as JObject;
        c.Check((string)br?["code"] == "busy", "an inline write while a batch transaction is open answers busy", Ctx.Short(c.LastText));
        // CallAsync: queued behind the requests, completes at a later pump
        c.Ok(new { cmd = "probe.callAsync", request = new { cmd = "camera.get" } }, "probe.callAsync camera.get");
        JObject ar = null;
        for (int k = 0; k < 10; k++)
        {
            ar = c.Call(new { cmd = "probe.asyncResult" });
            if ((bool?)ar["done"] == true) break;
        }
        var queued = c.Call(new { cmd = "camera.get" });
        c.Check((bool?)ar?["done"] == true && JToken.DeepEquals(ar["reply"], queued), "CallAsync completes with the queued reply", Ctx.Short(ar?.ToString(Formatting.None)));
        var defers = c.Ok(new { cmd = "probe.callAsync", request = new { cmd = "scene.load", name = "c37_none" } }, "probe.callAsync scene.load (allowed queued)");
        for (int k = 0; k < 10; k++) { ar = c.Call(new { cmd = "probe.asyncResult" }); if ((bool?)ar["done"] == true) break; }
        c.Check((string)ar?["reply"]?["code"] == "not_found", "a Defers command runs through CallAsync (not_found for a missing name)", Ctx.Short(ar?.ToString(Formatting.None)));
    }

    // ---------------------------------------------------------------- PL10 faults

    static string C37ExtOf(string file, string ns, out string manifestEntry)
    {
        manifestEntry = null;
        using (var z = new ZipArchive(File.OpenRead(file), ZipArchiveMode.Read))
        {
            string Read(string name)
            {
                var e = z.GetEntry(name);
                if (e == null) return null;
                using (var r = new StreamReader(e.Open(), Encoding.UTF8)) return r.ReadToEnd();
            }
            var scene = JObject.Parse(Read("scene.json") ?? "{}");
            var man = JObject.Parse(Read("manifest.json") ?? "{}");
            manifestEntry = (man["ext"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["ns"] == ns)?.ToString(Formatting.None);
            return scene["ext"]?[ns]?.ToString(Formatting.None);
        }
    }

    static void C37Faults(Ctx c)
    {
        var (app, host) = C37Host(c);
        foreach (var f in Directory.GetFiles(app.Paths.SceneDir, "c37_*")) File.Delete(f);
        c.Ok(new { cmd = "scene.new" }, "scene.new");
        c.Ok(new { cmd = "faulty.set", v = 7 }, "faulty.set 7");
        var s1 = c.Ok(new { cmd = "scene.save", name = "c37_faulty_a" }, "scene.save a");
        string ext1 = C37ExtOf((string)s1["path"], "stp3.test.faulty", out string man1);
        c.Check(ext1 == "{\"v\":7}", "the plugin's scene ext block is written", ext1 + " | " + man1);
        c.Ok(new { cmd = "faulty.start" }, "faulty.start (Update throws from now on)");
        c.Frames(12);
        var list = c.Ok(new { cmd = "plugin.list" }, "plugin.list");
        var p = C37Plugin(list, "stp3.test.faulty");
        c.Check((string)p?["state"] == PluginStates.Faulted && (int?)p?["faults"]?["update"] == 10 && ((string)p?["lastError"])?.Contains("faulty update") == true,
            "10 faults of Update -> faulted", Ctx.Short(p?.ToString(Formatting.None)));
        var r = c.Fails(new { cmd = "faulty.ok" }, "plugin_fault", "a faulted plugin's command answers plugin_fault without running");
        c.Check((string)r["detail"]?["owner"] == "stp3.test.faulty" && (string)r["detail"]?["state"] == "faulted", "detail {owner, state: faulted}", Ctx.Short(c.LastText));
        c.Check(ExtRegistry.Default.NamespacesOf("stp3.test.faulty").Count == 0, "its ext handler is removed");
        c.Check(C37Log().Contains("unload stp3.test.faulty"), "its Unload was tried once");
        c.Ok(new { cmd = "probe.ping" }, "the other plugins keep running");
        var ld = c.Ok(new { cmd = "scene.load", name = "c37_faulty_a" }, "scene.load a (no handler for the block now)");
        var s2 = c.Ok(new { cmd = "scene.save", name = "c37_faulty_b" }, "scene.save b");
        string ext2 = C37ExtOf((string)s2["path"], "stp3.test.faulty", out string man2);
        c.Check(ext2 == ext1 && man2 == man1, "the block is kept as unknown and written back unchanged", ext2 + " | " + man2);
        // a plugin command that throws counts as a fault and answers plugin_fault
        var t = c.Fails(new { cmd = "probe.throw" }, "plugin_fault", "probe.throw answers plugin_fault");
        c.Check((string)t["detail"]?["owner"] == "stp3.test.probe", "detail.owner = the probe", Ctx.Short(c.LastText));
        var pl = C37Plugin(c.Ok(new { cmd = "plugin.list" }, "plugin.list"), "stp3.test.probe");
        c.Check((int?)pl?["faults"]?["command probe.throw"] == 1 && (string)pl?["state"] == "loaded", "counted for the probe, which stays loaded", Ctx.Short(pl?["faults"]?.ToString(Formatting.None)));
    }

    // ---------------------------------------------------------------- PL11 logical unload

    static void C37Unload(Ctx c)
    {
        var (app, host) = C37Host(c);
        var reg = app.Bridge.Services.Commands;
        int coreCount = reg.Specs.Count(s => s.Owner == "core" || s.Owner == "bridge");
        var owned = C37Plugins.ToDictionary(g => g, g => reg.Specs.Count(s => s.Owner == g));
        c.Info("commands before: core+bridge " + coreCount + ", " + string.Join(", ", owned.Select(kv => kv.Key + " " + kv.Value)));
        c.Check(owned["stp3.test.probe"] >= 10 && owned["stp3.sample.sequencer"] == 8, "the plugins' commands are registered");
        int nsBefore = ExtRegistry.Default.NamespacesOf("stp3.test.faulty").Count + ExtRegistry.Default.NamespacesOf("stp3.sample.sequencer").Count;
        var probe = host.Find("stp3.test.probe");
        int handlers = probe.Events.HandlerCount;
        c.Check(nsBefore == 2 && handlers >= 13, "ext handlers and event handlers registered", nsBefore + " ns, " + handlers + " handlers");
        var ctx = probe.Context;
        host.Shutdown();
        c.Check(C37Plugins.All(g => reg.Specs.Count(s => s.Owner == g) == 0), "every plugin command is removed");
        c.Check(reg.Specs.Count(s => s.Owner == "core" || s.Owner == "bridge") == coreCount, "the Core and bridge commands stay", coreCount.ToString());
        c.Check(ExtRegistry.Default.NamespacesOf("stp3.test.faulty").Count == 0 && ExtRegistry.Default.NamespacesOf("stp3.sample.sequencer").Count == 0, "their ext handlers are removed");
        c.Check(probe.Events.HandlerCount == 0 && probe.State == PluginStates.Unloaded, "event handlers dropped, state unloaded", probe.State);
        bool threw = false;
        try { var _ = ctx.Settings; }
        catch (ObjectDisposedException) { threw = true; }
        c.Check(threw && ctx.Guid == "stp3.test.probe", "the context throws after the unload (Guid and Log keep working)");
        ctx.Log.Info("log after unload works");
        int u0 = probe.UpdateTime.Count;
        c.Frames(3);
        c.Check(probe.UpdateTime.Count == u0, "no Update after the unload");
        var list = c.Ok(new { cmd = "plugin.list" }, "plugin.list after the unload");
        c.Check(C37Plugins.All(g => (string)C37Plugin(list, g)?["state"] == PluginStates.Unloaded), "plugin.list: every loaded plugin unloaded");
    }

    // ---------------------------------------------------------------- PL12 the counted row of the plan

    static void C37Counted(Ctx c)
    {
        var plain = c.Host();
        int coreAlone = plain.Bridge.Services.Commands.Specs.Count(s => s.Owner == "core");
        c.Dispose(plain);
        var (app, host) = C37Host(c);
        const string G = "stp3.sample.sequencer";
        var help = c.Ok(new { cmd = "help" }, "help");
        var seq = (help["commands"] as JArray).OfType<JObject>().Where(x => ((string)x["name"]).StartsWith("seq.", StringComparison.Ordinal)).ToList();
        var names = seq.Select(x => (string)x["name"]).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var want = new[] { "seq.get", "seq.measure", "seq.pause", "seq.play", "seq.seek", "seq.set", "seq.status", "seq.stop" };
        c.Check(names.SequenceEqual(want), "help lists exactly the sample's 8 seq.* commands", string.Join(" ", names));
        c.Check(seq.All(x => (string)x["owner"] == G && (string)x["group"] == G), "owner and group = the GUID");
        var ver = c.Ok(new { cmd = "bridge.version" }, "bridge.version");
        c.Check((ver["plugins"] as JArray).OfType<JObject>().Any(x => (string)x["guid"] == G && (string)x["state"] == "loaded" && (string)x["version"] == "0.1.0"), "bridge.version.plugins lists the sample loaded");
        var p = C37Plugin(c.Ok(new { cmd = "plugin.list" }, "plugin.list"), G);
        c.Check((p?["commands"] as JArray)?.Count == 8, "plugin.list.commands of the sample = 8", Ctx.Short(p?["commands"]?.ToString(Formatting.None)));
        c.Check((int?)p?["events"] >= 5, "the sample's event handlers (>= 5)", (string)p?["events"]);
        c.Check((p?["extNamespaces"] as JArray)?.Any(x => (string)x == G) == true, "the sample's scene ext block is registered");
        int core = app.Bridge.Services.Commands.Specs.Count(s => s.Owner == "core");
        c.Check(core == coreAlone, "the Core command count is unchanged by the plugins", core + " = " + coreAlone);
    }
}
