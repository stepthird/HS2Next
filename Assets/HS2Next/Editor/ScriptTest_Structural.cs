// ScriptTest's structural part (Phase 6.10 package S): Headless_S_Structural, a part of ScriptTest.Run (headless).
//   YH2  the list behind the finding script.clearsHistory (ScriptCommands.StructuralCommands) against the run-time signal. One headless host with BR-Chan, an
//        item, a light, a camera and a route, its scene saved once for the commands that take a scene file, its own UserData and output folder (removed at
//        the end). Every registered command whose flags hold Mutates is sent once with undo false, in ordinal order of the names, after the fixture's nodes are
//        made again where a command took them, the character selected and at least two entries in the history; its arguments are its first help example with
//        the fixture's keys in place of the placeholders (the subject by the name's first part: item.* / obj.* the item, light.* the light, camera.* the camera,
//        route.* the route, else the character), or, for a command without an example, the fixture's arguments below. The command clears the history when
//        the history could be undone before it and cannot after it: with the reply's warning CommandKit.HistoryClearedWarning, or with none (by design:
//        scene.new, scene.load). Not run, printed with the reason: a command not sent (the app's own settings, set-up, end, options and windows), one that
//        answers an error, and one that changes nothing in the scene (its model version unchanged). Verdict: among the commands that ran, those that cleared
//        equal the list (a name in the set and not in the list is "added", a listed name that ran and did not clear is "removed"); a listed name that did not
//        run is a numbered WARN for the lead's ruling. Present: chara.load and delete answer the warning on the fixture.
//        In play mode (Play_S_Structural, the lead's ruling after the first headless run): bake.make on BR-Chan and cut.make of a bake item by a plane
//        marker, each once with undo false: both clear the history with the warning.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HS2Next.Api.Bridge;
using HS2Next.App;
using HS2Next.App.Commands;
using Newtonsoft.Json.Linq;

public static partial class ScriptTest
{
    static void Headless_S_Structural() => Group("YH2", "the structural list follows the run-time signal (undo: false)", SYh2);

    /// <summary>Commands not sent, with the reason (they act on the app, not on the scene).</summary>
    static readonly Dictionary<string, string> SYh2NotSent = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["app.quit"] = "not sent: ends the app",
        ["app.settings"] = "not sent: writes the app's settings file",
        ["app.setup"] = "not sent: the app's first-run set-up",
        ["app.options"] = "not sent: the app's options, not the scene",
        ["ui.action"] = "not sent: the app's windows (no UI headless)",
        ["ui.input"] = "not sent: the app's windows (no UI headless)",
        ["ui.panel"] = "not sent: the app's windows (no UI headless)",
    };

    static readonly Regex SYh2Placeholder = new Regex(@"^<[^<>]*>$");

    sealed class SYh2Fix
    {
        public string Chara, Item, Light, Camera, Route, ScenePath;

        /// <summary>The node a command's placeholders name: its subject by the name's first part.</summary>
        public string KeyFor(string cmd)
        {
            if (cmd.StartsWith("item.", StringComparison.Ordinal) || cmd.StartsWith("obj.", StringComparison.Ordinal)) return Item;
            if (cmd.StartsWith("light.", StringComparison.Ordinal)) return Light;
            if (cmd.StartsWith("camera.", StringComparison.Ordinal)) return Camera;
            if (cmd.StartsWith("route.", StringComparison.Ordinal)) return Route;
            return Chara;
        }
    }

    /// <summary>The arguments of the commands without a help example (the fixture's nodes and its saved scene).</summary>
    static JObject SYh2NoExample(string cmd, SYh2Fix f)
    {
        switch (cmd)
        {
            case "camera.add": return new JObject();
            case "chara.change": return new JObject { ["key"] = f.Chara, ["path"] = femaleRel, ["sex"] = "female" };
            case "delete": return new JObject { ["key"] = f.Chara };
            case "item.parent": return new JObject { ["key"] = f.Item, ["chara"] = f.Chara, ["point"] = "48" };
            case "obj.reorder": return new JObject { ["key"] = f.Route, ["index"] = 0 };
            case "scene.import": return new JObject { ["path"] = f.ScenePath };
            case "scene.load": return new JObject { ["path"] = f.ScenePath };
            case "scene.new": return new JObject();
            default: return null;
        }
    }

    static void SYh2Replace(JToken t, string key)
    {
        if (t is JObject o)
        {
            foreach (var p in o.Properties().ToList())
            {
                if (p.Value.Type == JTokenType.String && SYh2Placeholder.IsMatch((string)p.Value)) p.Value = key;
                else SYh2Replace(p.Value, key);
            }
        }
        else if (t is JArray a)
        {
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Type == JTokenType.String && SYh2Placeholder.IsMatch((string)a[i])) a[i] = key;
                else SYh2Replace(a[i], key);
            }
        }
    }

    /// <summary>The request of <paramref name="s"/> with undo false, or null with the reason when it cannot be made.</summary>
    static JObject SYh2Request(CommandSpec s, SYh2Fix f, out string why)
    {
        why = null;
        JObject b = null;
        if (s.Examples.Count > 0)
        {
            try { b = JObject.Parse(s.Examples[0]); }
            catch (Exception) { b = null; }
            if (b == null) { why = "its first example does not parse"; return null; }
        }
        else b = SYh2NoExample(s.Name, f);
        if (b == null)
        {
            // no example and no fixture arguments: the required node arguments from the fixture, else not run
            b = new JObject();
            foreach (var a in s.Args.Where(a => a.Required))
            {
                if (a.Type == ArgTypes.Node || a.Name == "key") b[a.Name] = f.KeyFor(s.Name);
                else { why = "no example and a required argument " + a.Name + " (" + a.Type + ") the fixture does not give"; return null; }
            }
        }
        b.Remove("cmd");
        b.Remove("id");
        SYh2Replace(b, f.KeyFor(s.Name));
        var r = new JObject { ["cmd"] = s.Name };
        foreach (var p in b.Properties()) r[p.Name] = p.Value.DeepClone();
        r["undo"] = false;
        return r;
    }

    /// <summary>Makes again every node of the fixture a command took (recorded calls); false when one cannot be made.</summary>
    static bool SYh2Ensure(Host h, SYh2Fix f)
    {
        var m = h.Session.Model;
        bool Has(string k) => k != null && m.Find(k) != null;
        string Add(object req, string what)
        {
            var k = Key(h.Call(req));
            if (k == null) Check("YH2 fixture: " + what, false, Short(h.LastText));
            return k;
        }
        if (!Has(f.Chara))
        {
            f.Chara = Add(new { cmd = "chara.load", path = femaleRel, sex = "female" }, "chara.load");
            if (f.Chara != null) h.Call(new { cmd = "chara.motion", key = f.Chara, physics = false });
        }
        if (!Has(f.Item)) f.Item = Add(new { cmd = "item.add", group = 0, category = 0, no = 399 }, "item.add");
        if (!Has(f.Light)) f.Light = Add(new { cmd = "light.add", no = 0 }, "light.add");
        if (!Has(f.Camera)) f.Camera = Add(new { cmd = "camera.add" }, "camera.add");
        if (!Has(f.Route)) f.Route = Add(new { cmd = "route.add", pos = new[] { 0f, 0f, 0f } }, "route.add");
        return Has(f.Chara) && Has(f.Item) && Has(f.Light) && Has(f.Camera) && Has(f.Route);
    }

    /// <summary>The fixture as each command meets it: its nodes, the clock running, the character selected, at least two undo entries.</summary>
    static bool SYh2Prepare(Host h, SYh2Fix f, ref int moves)
    {
        if (!SYh2Ensure(h, f)) return false;
        h.Call(new { cmd = "scene.pause", paused = false });
        h.Call(new { cmd = "select", keys = new[] { f.Chara } });
        var bus = h.Session.Commands;
        for (int i = 0; i < 6 && bus.UndoList.Count < 2; i++)
        {
            moves++;
            string k = i % 2 == 0 ? f.Item : f.Light;
            h.Call(new { cmd = "obj.transform", key = k, pos = new[] { 0.5f * (moves % 7), 1f, 0.25f * (moves % 5) } });
        }
        return bus.UndoList.Count >= 2 && bus.CanUndo;
    }

    static void SYh2()
    {
        string ud = Path.Combine(OutDir, "yh2_userdata"), outDir = Path.Combine(OutDir, "yh2_out");
        SYh2Remove(ud);
        SYh2Remove(outDir);
        var h = NewHost(DefaultSeed, s => { s.UserData = ud; s.Bridge.OutputDir = outDir; });
        try
        {
            var f = new SYh2Fix();
            if (!Check("YH2 fixture: BR-Chan, an item, a light, a camera and a route", SYh2Ensure(h, f), $"{f.Chara} / {f.Item} / {f.Light} / {f.Camera} / {f.Route}")) return;
            h.Frames(3);
            var sv = h.Ok(new { cmd = "scene.save", name = "yh2_fixture", overwrite = true, thumb = "placeholder", clock = "2026-11-08T00:00:00Z" }, "YH2 fixture: scene.save");
            f.ScenePath = (string)sv["path"];
            if (!Check("YH2 fixture: the scene saved for scene.import and scene.load", f.ScenePath != null && File.Exists(f.ScenePath), Short(h.LastText))) return;

            var reg = h.App.BridgeServices.Commands;
            var listed = ScriptCommands.StructuralCommands;
            var unknown = listed.Where(n => reg.Find(n) == null).ToList();
            Check($"YH2: every listed name is a registered command ({listed.Length} listed)", unknown.Count == 0, unknown.Count == 0 ? "" : "unknown: " + string.Join(", ", unknown));
            var specs = reg.Specs.Where(s => (s.Flags & CommandFlags.Mutates) != 0).OrderBy(s => s.Name, StringComparer.Ordinal).ToList();
            var unmarked = listed.Where(n => reg.Find(n) != null && (reg.Find(n).Flags & CommandFlags.Mutates) == 0).ToList();
            Check("YH2: every listed command's flags hold Mutates", unmarked.Count == 0, string.Join(", ", unmarked));

            var cleared = new List<string>();
            var warned = new HashSet<string>(StringComparer.Ordinal);
            var ran = new HashSet<string>(StringComparer.Ordinal);
            var notRun = new List<(string name, string why)>();
            int moves = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var s in specs)
            {
                if (SYh2NotSent.TryGetValue(s.Name, out var ns)) { notRun.Add((s.Name, ns)); continue; }
                if (!SYh2Prepare(h, f, ref moves)) { notRun.Add((s.Name, "the fixture could not be made again before it")); continue; }
                var req = SYh2Request(s, f, out var why);
                if (req == null) { notRun.Add((s.Name, why)); continue; }
                var bus = h.Session.Commands;
                bool before = bus.CanUndo;
                int undo0 = bus.UndoList.Count;
                long v0 = bus.Version;
                var t0 = sw.Elapsed.TotalMilliseconds;
                JObject r;
                try { r = h.Call(req.ToString(Newtonsoft.Json.Formatting.None), (s.Flags & (CommandFlags.Slow | CommandFlags.Defers)) != 0 ? 3000 : 600); }
                catch (Exception e) { notRun.Add((s.Name, "threw " + e.GetType().Name + ": " + e.Message)); continue; }
                double ms = sw.Elapsed.TotalMilliseconds - t0;
                bool ok = r.Value<bool?>("ok") == true;
                bool w = (r["warnings"] as JArray)?.Any(x => (string)x == CommandKit.HistoryClearedWarning) == true;
                var bus1 = h.Session.Commands;
                bool after = bus1.CanUndo;
                long v1 = bus1.Version;
                bool sameBus = ReferenceEquals(bus, bus1);
                string sent = Short(req.ToString(Newtonsoft.Json.Formatting.None));
                if (!ok)
                {
                    notRun.Add((s.Name, "answers " + ((string)r["code"] ?? "?") + ": " + Short((string)r["error"] ?? h.LastText)));
                    Info($"YH2 {s.Name}: not run ({(string)r["code"]}); sent {sent}");
                    continue;
                }
                bool clears = before && !after;
                if (!clears && sameBus && v1 == v0)
                {
                    notRun.Add((s.Name, "changes nothing in the scene on the fixture"));
                    Info($"YH2 {s.Name}: ran, the scene's model unchanged (version {v0}); sent {sent}");
                    continue;
                }
                ran.Add(s.Name);
                if (clears) cleared.Add(s.Name);
                if (w) warned.Add(s.Name);
                Info($"YH2 {s.Name}: {(clears ? (w ? "clears the history, with the warning" : "clears the history (no warning: by design)") : "keeps the history")}; undo {undo0} -> {bus1.UndoList.Count}, version {v0} -> {v1}{(sameBus ? "" : " (a new history)")}, {ms:F0} ms; sent {sent}");
            }
            var set = new HashSet<string>(cleared, StringComparer.Ordinal);
            var list = new HashSet<string>(listed, StringComparer.Ordinal);
            var added = set.Where(n => !list.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
            var removed = list.Where(n => ran.Contains(n) && !set.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
            var listedNotRun = notRun.Where(x => list.Contains(x.name)).ToList();
            Measure($"YH2: {specs.Count} commands with Mutates: {ran.Count} ran ({set.Count} cleared the history: {set.Count(warned.Contains)} with the warning, {set.Count(n => !warned.Contains(n))} by design), {notRun.Count} not run; {sw.Elapsed.TotalSeconds:F1} s");
            Check("YH2 Present: chara.load and delete answer the warning on the fixture", warned.Contains("chara.load") && warned.Contains("delete"),
                $"chara.load {(warned.Contains("chara.load") ? "warned" : "no warning")}, delete {(warned.Contains("delete") ? "warned" : "no warning")}");
            Check($"YH2: the commands that clear the history with undo false equal the list among those that ran ({set.Count} cleared, {list.Count} listed, {list.Count(ran.Contains)} of them ran)",
                added.Count == 0 && removed.Count == 0,
                $"added (clear, not listed): {(added.Count == 0 ? "none" : string.Join(", ", added))}; removed (listed, ran, kept the history): {(removed.Count == 0 ? "none" : string.Join(", ", removed))}; cleared: {string.Join(", ", cleared)}");
            Info("YH2 cleared with the warning: " + string.Join(", ", cleared.Where(warned.Contains)) + "; by design: " + string.Join(", ", cleared.Where(n => !warned.Contains(n))));
            foreach (var (name, why) in notRun) Info($"YH2 not run: {name}{(list.Contains(name) ? " (listed)" : "")}: {why}");
            if (listedNotRun.Count > 0)
                Warn($"YH2W1 listed commands not run on the fixture ({listedNotRun.Count}; the lead rules on each): " + string.Join("; ", listedNotRun.Select(x => x.name + ": " + x.why)));
        }
        finally
        {
            h.Dispose();
            try { SYh2Remove(ud); SYh2Remove(outDir); }
            catch (Exception e) { Info("YH2: the run's folders stay: " + e.Message); }
        }
    }

    // ------------------------------------------------------------ YH2 in play mode: the two listed commands that need a graphics device

    /// <summary>
    /// Play_S_Structural (ScriptTest.RunPlay; the lead's ruling after the headless run 20261108-2341): bake.make on BR-Chan (its first help example) and
    /// cut.make of one of the bake items by a plane marker on it (the example's mode and ragged edge), each sent once with undo false after two undo entries;
    /// each must clear the history with CommandKit.HistoryClearedWarning.
    /// </summary>
    static IEnumerator Play_S_Structural()
    {
        if (!WantAny("YH2")) yield break;
        current = "S_Structural";
        if (!POpen("YH2", "bake.make and cut.make with undo false clear the history (a graphics device)")) yield break;
        if (!Check("YH2 play: a graphics device", UnityEngine.SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null, UnityEngine.SystemInfo.graphicsDeviceType.ToString())) yield break;
        yield return PStartApp();
        var app = AppCore.Current;
        if (!Check("YH2 play: the app runs", app != null && app.Stage == AppStage.Ready && app.Bridge != null, app?.StageName ?? "none")) yield break;
        yield return PCall(app, new { cmd = "scene.new" });
        yield return PCall(app, new { cmd = "chara.load", path = femaleRel, sex = "female" });
        string k = Key(pLast);
        if (!Check("YH2 play: BR-Chan loads", POk && k != null, Short(pLastText))) yield break;
        yield return PCall(app, new { cmd = "chara.motion", key = k, physics = false });
        yield return PFrames(10);
        var bake = new JObject[1];
        yield return SYh2PlayOne(app, k, JObject.FromObject(new { cmd = "bake.make", key = k, parts = new[] { "clothes0", "clothes7" }, split = true, undo = false }), bake);
        var items = (bake[0]?["items"] as JArray)?.Select(x => (string)x).Where(x => x != null).ToList() ?? new List<string>();
        if (Check("YH2 play: bake.make made bake items", items.Count > 0, Short(bake[0]?.ToString(Newtonsoft.Json.Formatting.None))))
        {
            yield return PCall(app, new { cmd = "cut.marker", kind = "plane", target = items[0] });
            string marker = Key(pLast);
            if (Check("YH2 play: a plane marker on the first bake item", POk && marker != null, Short(pLastText)))
            {
                var cut = new JObject[1];
                yield return SYh2PlayOne(app, items[0], JObject.FromObject(new { cmd = "cut.make", key = items[0], marker, mode = "both", ragged = new { amp = 0.1f }, undo = false }), cut);
                Info("YH2 play: cut.make's pieces " + Short((cut[0]?["pieces"] as JArray)?.ToString(Newtonsoft.Json.Formatting.None)));
            }
        }
        yield return PCall(app, new { cmd = "scene.new" });
        yield return PStopApp();
    }

    /// <summary>One request with undo false after two recorded moves of <paramref name="key"/>: did it clear the history, with the warning.</summary>
    static IEnumerator SYh2PlayOne(AppCore app, string key, JObject req, JObject[] reply)
    {
        string cmd = (string)req["cmd"];
        for (int i = 0; i < 4 && app.Session.Commands.UndoList.Count < 2; i++)
            yield return PCall(app, new { cmd = "obj.transform", key, pos = new[] { 0.25f * (i + 1), 0f, 0f } });
        var bus = app.Session.Commands;
        int undo0 = bus.UndoList.Count;
        bool before = bus.CanUndo;
        yield return PCall(app, req.ToString(Newtonsoft.Json.Formatting.None), 600);
        reply[0] = pLast;
        bool warned = (pLast?["warnings"] as JArray)?.Any(x => (string)x == CommandKit.HistoryClearedWarning) == true;
        bool after = app.Session.Commands.CanUndo;
        Check($"YH2 play: {cmd} with undo false clears the history with the warning", POk && before && !after && warned,
            $"undo {undo0} -> {app.Session.Commands.UndoList.Count}, warned {warned}; sent {Short(req.ToString(Newtonsoft.Json.Formatting.None))}; {Short(pLastText)}");
    }

    /// <summary>Removes a folder of this part (none of its entries a reparse point; a folder that is one is left as it is).</summary>
    static void SYh2Remove(string dir)
    {
        if (!Directory.Exists(dir)) return;
        var di = new DirectoryInfo(dir);
        if ((di.Attributes & FileAttributes.ReparsePoint) != 0) return;
        foreach (var e in di.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            if ((e.Attributes & FileAttributes.ReparsePoint) != 0) { Info("YH2: " + dir + " holds a reparse point; left as it is"); return; }
        di.Delete(true);
    }
}
