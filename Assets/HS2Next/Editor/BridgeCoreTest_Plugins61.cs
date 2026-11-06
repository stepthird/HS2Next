// C40: the Phase 6.1 host points in edit-mode test hosts (the spec names the group
// C38, a number Phase 5.2 already used, as C39 is Phase 5.3's), on the plugin folder of C37 (Spike/out/plugins/core, staged
// by C37Stage: the 5.1 set built against the Api 1.0.0 assemblies, the 6.1 probes against the current Api). Parts (each with
// its own host, disposed after it): KC1 Request = Call (the same reply text) for 20 commands, KC2 the argument forms and
// envelope members, KC3 the refusals, KC4 ICommandReply, KC5 RequestAsync = CallAsync; KR2 plugin entries interleaved with
// host commands called through Request, KR3 Busy (a client batch, an event handler, an undo callback), KR4 a throwing
// callback (a fault, the history kept, the reply's warning), KR5 the entries leave with the plugin, KR6 scene.new clears
// them, KR7 history labels and dirty; KD1 plugin.disable, KD2 plugin.enable, KD3 the sequencer's ext data across disable /
// enable and a save, KD4 20 cycles (counts, managed growth, enable time), KD5 the refusals, KD6 persist (a settings file of
// the check's own), KD8 the rebuild of characters the probe's hook touched; KX4a the 1.0-built 5.1 set as C37 loads it. Then
// every static KB<n>(Ctx) of this partial class (package B's skin layers and body parts) in number order. -c40 KC1,KD2
// runs some parts only. Content: the clothed SFW female target only for KD8; numbers and ids only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using HS2Next.App;
using HS2Next.App.Plugins;
using HS2Next.Content;
using HS2Next.Save;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Debug = UnityEngine.Debug;

public static partial class BridgeCoreTest
{
    const string HpGuid = "stp3.test.historyprobe";
    /// <summary>The 6.1 plugins C40 needs in the staged folder (C37Stage stages when one is missing).</summary>
    static readonly string[] C40Plugins = { HpGuid };
    /// <summary>
    /// A manifest-only fixture whose prefix the loader probe holds: on the disabled list, its enable is refused
    /// (plugin.prefixTaken). KD5 writes it and removes it again (in C37's hosts it would compete for the prefix; its GUID sorts
    /// after the probe's, so the probe keeps the prefix even when a removal failed).
    /// </summary>
    const string C40PrefixLater = "stp3.test.zprefixlater";

    static string[] C40Parts()
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, "-c40");
        if (i < 0 || i + 1 >= a.Length) return null;
        return a[i + 1].Split(',', '+', ';').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
    }

    static void C40(Ctx c)
    {
        var parts = C40Parts();
        bool Want(string id) => parts == null || parts.Contains(id, StringComparer.OrdinalIgnoreCase);
        bool staged = C37Stage(c);
        var mine = new (string id, Action<Ctx> run)[]
        {
            ("KC1", C40Kc1), ("KC2", C40Kc2), ("KC3", C40Kc3), ("KC4", C40Kc4), ("KC5", C40Kc5),
            ("KR2", C40Kr2), ("KR3", C40Kr3), ("KR4", C40Kr4), ("KR5", C40Kr5), ("KR6", C40Kr6), ("KR7", C40Kr7),
            ("KD1", C40Kd1), ("KD2", C40Kd2), ("KD3", C40Kd3), ("KD4", C40Kd4), ("KD5", C40Kd5), ("KD6", C40Kd6), ("KD8", C40Kd8),
            ("KX4a", C40Kx4a),
        };
        var others = typeof(BridgeCoreTest).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(m => (m, mm: Regex.Match(m.Name, @"^KB(\d+)$")))
            .Where(x => x.mm.Success && x.m.GetParameters().Length == 1 && x.m.GetParameters()[0].ParameterType == typeof(Ctx))
            .OrderBy(x => int.Parse(x.mm.Groups[1].Value))
            .Select(x => (id: x.m.Name, run: (Action<Ctx>)(cc => x.m.Invoke(null, new object[] { cc })))).ToList();
        c.Info("C40 parts: " + string.Join(", ", mine.Select(x => x.id).Concat(others.Select(x => x.id))));
        foreach (var (id, run) in mine.Concat(others))
        {
            if (!Want(id)) continue;
            if (!staged) { c.Check(false, id + ": the plugin folder could not be staged"); continue; }
            c.Id = "C40." + id;
            var sw = Stopwatch.StartNew();
            try { run(c); }
            catch (Exception e)
            {
                var ie = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                c.Check(false, "exception", ie.GetType().Name + ": " + ie.Message + "\n" + ie.StackTrace);
            }
            c.Info($"C40.{id}: {sw.Elapsed.TotalSeconds:F1} s");
            foreach (var h in C37Hosts(c)) c.Dispose(h);
        }
        c.Id = "C40";
    }

    // ---------------------------------------------------------------- hosts and helpers

    /// <summary>A host with the C37 folder loaded; the C37 disabled fixture, the prefix fixture and <paramref name="disabled"/> on the disabled list.</summary>
    static (AppCore app, PluginHost host) C40Host(Ctx c, params string[] disabled)
    {
        C37ClearLog();
        var app = c.Host();
        var host = app.LoadPlugins(C37Dir, new[] { C37Disabled, C40PrefixLater }.Concat(disabled ?? Array.Empty<string>()).ToArray());
        return (app, host);
    }

    static JObject C40Plugin(Ctx c, string guid) => C37Plugin(c.Ok(new { cmd = "plugin.list" }, "plugin.list"), guid);

    static object C40Field(object o, string name) => o?.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(o);

    static int C40Int(object o, string name) => C40Field(o, name) is int i ? i : -1;

    static string Env(object request) => JsonConvert.SerializeObject(request);

    /// <summary>The counts of plugin.list that a disable / enable round trip must restore.</summary>
    static string C40Counts(JObject p) => p == null ? "absent" :
        "commands " + (p["commands"] as JArray)?.Count + ", ext " + (p["extNamespaces"] as JArray)?.Count + ", events " + p["events"] + ", hooks " + (p["hooks"] as JArray)?.Count +
        ", supplies " + p["supplies"] + ", ui " + p["ui"]?.ToString(Formatting.None);

    // ---------------------------------------------------------------- KC1 Request = Call

    static void C40Kc1(Ctx c)
    {
        var (app, host) = C40Host(c);
        var ctx = host.Find(HpGuid)?.Context;
        if (!c.Check(ctx != null && host.Find(HpGuid).IsLoaded, "the history probe is loaded", host.Find(HpGuid)?.State + " " + host.Find(HpGuid)?.Reason)) return;
        W1AddItem(c);
        var requests = new (string cmd, object args)[]
        {
            ("scene", null), ("history", null), ("camera.get", null), ("plugin.list", null), ("help", new { name = "scene" }),
            ("help", new { name = "plugin.disable" }), ("maps", null), ("item.search", new { query = "chair" }), ("scene.list", null), ("app.paths", null),
            ("historyprobe.state", null), ("seq.get", null), ("help", new { name = "undo" }), ("history", new { strict = false }), ("obj.transform", new { key = "nosuchnode", pos = new[] { 1, 2, 3 } }),
            ("nosuch.command", null), ("scene.doc", new { capture = false }), ("history", new { id = 42 }), ("camera.state", null), ("help", new { name = "nosuch", strict = true }),
            ("probe.ping", new { n = 21 }), ("seq.status", null),
        };
        int same = 0;
        var diff = new List<string>();
        foreach (var (cmd, args) in requests)
        {
            string viaRequest = ctx.Request(cmd, args).Json;
            var env = args == null ? new JObject() : JObject.FromObject(args);
            env.AddFirst(new JProperty("cmd", cmd));
            string viaCall = ctx.Call(env.ToString(Formatting.None));
            if (viaRequest == viaCall) same++;
            else diff.Add(cmd + ": " + Ctx.Short(viaRequest) + " | " + Ctx.Short(viaCall));
        }
        c.Check(same >= 20 && diff.Count == 0, "KC1: Request = Call (the same reply text) for " + requests.Length + " envelopes (" + same + " equal)", string.Join(" || ", diff.Take(3)));
    }

    // ---------------------------------------------------------------- KC2 argument forms and envelope members

    public sealed class Kc2Poco
    {
        [BridgeArg(Name = "name")] public string Command = "scene";
        [BridgeIgnore] public string Hidden = "x";
        public int? Absent;
    }

    enum Kc2Kind { First, Second }

    static void C40Kc2(Ctx c)
    {
        var (app, host) = C40Host(c);
        var ctx = host.Find(HpGuid).Context;
        string call = ctx.Call("{\"cmd\":\"help\",\"name\":\"scene\"}");
        c.Check(ctx.Request("help", new { name = "scene" }).Json == call, "KC2: an anonymous object");
        c.Check(ctx.Request("help", new Dictionary<string, object> { ["name"] = "scene" }).Json == call, "KC2: an IDictionary<string, object>");
        c.Check(ctx.Request("help", (object)new JObject { ["name"] = "scene" }).Json == call, "KC2: a JObject");
        c.Check(ctx.Request("help", new CommandRequest("help").Arg("name", "scene").Args).Json == call, "KC2: an IReadOnlyDictionary (CommandRequest.Args)");
        c.Check(ctx.Request(new CommandRequest("help").Arg("name", "scene")).Json == call, "KC2: a CommandRequest");
        string env = PluginRequests.Envelope("x", new Kc2Poco());
        c.Check(env == "{\"cmd\":\"x\",\"name\":\"scene\",\"absent\":null}", "KC2: a POCO: BridgeArg(Name), BridgeIgnore, camelCase, nulls written", env);
        env = PluginRequests.Envelope("x", new { f = 0.1f, d = 0.1, i = 3, b = true, s = (string)null, v = new UnityEngine.Vector3(1, 2.5f, -3), k = Kc2Kind.Second, l = new[] { 1, 2 } });
        c.Check(env == "{\"cmd\":\"x\",\"f\":0.1,\"d\":0.1,\"i\":3,\"b\":true,\"s\":null,\"v\":[1.0,2.5,-3.0],\"k\":\"Second\",\"l\":[1,2]}",
            "KC2: floats round-trip, nulls, a vector as an array, an enum by name", env);
        env = PluginRequests.Envelope("x", new { nan = double.NaN, raw = new RawJson("{\"a\":[1]}") });
        c.Check(env == "{\"cmd\":\"x\",\"nan\":null,\"raw\":{\"a\":[1]}}", "KC2: NaN as null, RawJson verbatim", env);
        // envelope members: id echoed, strict, undo
        var r = ctx.Request("history", new { id = "abc" });
        c.Check(r.Ok && r.GetJson("id") == "\"abc\"", "KC2: id as a member of args is echoed", r.Json);
        r = ctx.Request(new CommandRequest("history") { Id = 7 });
        c.Check(r.Ok && r.Get<int>("id") == 7, "KC2: CommandRequest.Id", r.Json);
        r = ctx.Request(new CommandRequest("history") { Strict = true }.Arg("nosucharg", 1));
        c.Check(!r.Ok && r.Code == BridgeCodes.BadArgs, "KC2: Strict = true makes an unknown argument an error", r.Json);
        r = ctx.Request("history", new { nosucharg = 1 });
        c.Check(r.Ok && r.Warnings.Count > 0, "KC2: without strict an unknown argument is a warning", r.Json);
        string item = W1AddItem(c);
        int u0 = W1UndoCount(c);
        r = ctx.Request(new CommandRequest("obj.transform") { Undo = false }.Arg("key", item).Arg("pos", new[] { 2, 0, 0 }));
        c.Check(r.Ok && W1UndoCount(c) == u0, "KC2: Undo = false: no undo entry", r.Json);
        r = ctx.Request("obj.transform", new { key = item, pos = new[] { 3, 0, 0 } });
        c.Check(r.Ok && W1UndoCount(c) == u0 + 1, "KC2: a mutating Request records one undo entry", r.Json);
    }

    // ---------------------------------------------------------------- KC3 refusals

    static void C40Kc3(Ctx c)
    {
        var (app, host) = C40Host(c);
        var ctx = host.Find(HpGuid).Context;
        bool Throws<T>(Action a) where T : Exception
        {
            try { a(); return false; }
            catch (T) { return true; }
            catch (Exception) { return false; }
        }
        c.Check(Throws<ArgumentException>(() => ctx.Request("help", "name")), "KC3: a string as args -> ArgumentException");
        c.Check(Throws<ArgumentException>(() => ctx.Request("help", 5)), "KC3: a number as args -> ArgumentException");
        c.Check(Throws<ArgumentException>(() => ctx.Request("help", true)), "KC3: a bool as args -> ArgumentException");
        c.Check(Throws<ArgumentException>(() => ctx.Request("help", new[] { 1, 2 })), "KC3: an array as args -> ArgumentException");
        c.Check(Throws<ArgumentException>(() => ctx.Request("help", new JArray(1))), "KC3: a JArray as args -> ArgumentException");
        c.Check(Throws<ArgumentException>(() => ctx.Request("help", new { cmd = "scene" })), "KC3: a member named cmd -> ArgumentException");
        c.Check(Throws<ArgumentException>(() => ctx.Request("", null)), "KC3: an empty command -> ArgumentException");
        c.Check(Throws<ArgumentException>(() => new CommandRequest("help").Arg("id", 1)), "KC3: CommandRequest.Arg refuses an envelope name");
        c.Check(Throws<ArgumentNullException>(() => ctx.Request((CommandRequest)null)), "KC3: a null CommandRequest -> ArgumentNullException");
        c.Check(Throws<ArgumentException>(() => ctx.RequestAsync("help", "x")), "KC3: RequestAsync: misuse throws at once");
        var r = ctx.Request("batch", new { cmds = new object[] { new { cmd = "history" } } });
        c.Check(!r.Ok && r.Code == BridgeCodes.Unsupported, "KC3: batch inline -> unsupported", r.Json);
        r = ctx.Request("screenshot");
        c.Check(!r.Ok && r.Code == BridgeCodes.Unsupported, "KC3: a deferring command inline -> unsupported", r.Json);
        r = ctx.Request(new CommandRequest("history") { WaitFrames = 2 });
        c.Check(!r.Ok && r.Code == BridgeCodes.Unsupported, "KC3: WaitFrames > 0 inline -> unsupported", r.Json);
        r = ctx.Request(new CommandRequest("history") { Timeout = 5 });
        c.Check(r.Ok && r.Warnings.Any(w => w.Contains("timeout")), "KC3: Timeout inline -> ignored with Call's warning", r.Json);
        Exception off = null;
        try { Task.Run(() => ctx.Request("history")).Wait(); }
        catch (AggregateException e) { off = e.InnerException; }
        c.Check(off is InvalidOperationException, "KC3: Request off the main thread -> InvalidOperationException", off?.GetType().Name ?? "no exception");
    }

    // ---------------------------------------------------------------- KC4 ICommandReply

    public sealed class Kc4Reply
    {
        public bool Ok;
        public string Cmd;
        public List<string> Undo;
        public long Version;
    }

    static void C40Kc4(Ctx c)
    {
        var (app, host) = C40Host(c);
        var ctx = host.Find(HpGuid).Context;
        W1AddItem(c);
        var r = ctx.Request("scene");
        c.Check(r.Ok && r.Command == "scene" && r.Code == null && r.Error == null && r.Warnings.Count == 0, "KC4: ok, cmd, no code / error / warnings", Ctx.Short(r.Json));
        var objects = r.Get<List<object>>("objects");
        c.Check(objects?.Count == 1 && objects[0] is Dictionary<string, object> o0 && o0["key"] is string, "KC4: Get<List<object>>: plain dictionaries", objects?.FirstOrDefault()?.GetType().Name);
        c.Check(r.Get<object>("map") is Dictionary<string, object> map && map["no"] is long, "KC4: Get<object>: a dictionary with long numbers");
        c.Check(r.Get<Dictionary<string, object>>("camera") != null, "KC4: Get<Dictionary<string, object>>");
        var h = ctx.Request("history");
        c.Check(h.Get<int>("version") >= 1 && h.Get<long>("version") >= 1 && h.Get<double>("version") >= 1 && h.Get<float>("version") >= 1, "KC4: numbers as int, long, double, float", h.Json);
        c.Check(h.Get<string[]>("undo")?.Length == 1 && h.Get<List<string>>("undo")?.Count == 1, "KC4: arrays as T[] and List<T>", h.Json);
        c.Check(h.Get<bool>("dirty"), "KC4: a bool");
        c.Check(!h.Has("nosuch") && h.Get("nosuch", 17) == 17 && h.GetJson("nosuch") == null, "KC4: an absent member: Has false, the fallback, GetJson null");
        c.Check(h.GetJson("undo") == "[\"Add item\"]" || h.GetJson("undo")?.StartsWith("[\"") == true, "KC4: GetJson gives the member's text", h.GetJson("undo"));
        bool fe = false;
        try { h.Get<int>("undo"); }
        catch (FormatException e) { fe = e.Message.Contains("undo"); }
        c.Check(fe, "KC4: a value that does not convert -> FormatException naming the member");
        var poco = h.As<Kc4Reply>();
        c.Check(poco.Ok && poco.Cmd == "history" && poco.Undo?.Count == 1 && poco.Version >= 1, "KC4: As<T> (a plain class by member names)");
        h.ThrowIfError();
        var bad = ctx.Request("obj.transform", new { key = "nosuchnode", pos = new[] { 0, 0, 0 } });
        BridgeException be = null;
        try { bad.ThrowIfError(); }
        catch (BridgeException e) { be = e; }
        c.Check(!bad.Ok && bad.Code == BridgeCodes.NotFound && be?.Code == BridgeCodes.NotFound && be.Message == bad.Error, "KC4: ThrowIfError: a BridgeException with the code and the message", bad.Json);
        var miss = ctx.Request("help", new { name = "nosuch.cmd" });
        c.Check(!miss.Ok && miss.Error != null && miss.Code != null, "KC4: an error reply: Code and Error set", miss.Json);
    }

    // ---------------------------------------------------------------- KC5 RequestAsync = CallAsync

    static void C40Kc5(Ctx c)
    {
        var (app, host) = C40Host(c);
        var ctx = host.Find(HpGuid).Context;
        W1AddItem(c);
        var pairs = new (Task<ICommandReply> req, Task<string> call, string what)[]
        {
            (ctx.RequestAsync("history"), ctx.CallAsync("{\"cmd\":\"history\"}"), "history"),
            (ctx.RequestAsync("help", new { name = "scene" }), ctx.CallAsync("{\"cmd\":\"help\",\"name\":\"scene\"}"), "help"),
            (ctx.RequestAsync(new CommandRequest("historyprobe.state")), ctx.CallAsync("{\"cmd\":\"historyprobe.state\"}"), "historyprobe.state"),
            (ctx.RequestAsync("obj.transform", new { key = "nosuchnode", pos = new[] { 0, 0, 0 } }), ctx.CallAsync("{\"cmd\":\"obj.transform\",\"key\":\"nosuchnode\",\"pos\":[0,0,0]}"), "obj.transform error"),
        };
        for (int i = 0; i < 20 && !pairs.All(p => p.req.IsCompleted && p.call.IsCompleted); i++) c.Frames(1);
        foreach (var p in pairs)
            c.Check(p.req.IsCompleted && p.call.IsCompleted && p.req.Result.Json == p.call.Result, "KC5: RequestAsync = CallAsync: " + p.what,
                p.req.IsCompleted && p.call.IsCompleted ? Ctx.Short(p.req.Result.Json) + " | " + Ctx.Short(p.call.Result) : "not completed");
        int f0 = app.Bridge.Server.Frames.Frame;
        var w = ctx.RequestAsync(new CommandRequest("history") { WaitFrames = 3 });
        int frames = 0;
        while (!w.IsCompleted && frames < 20) { c.Frames(1); frames++; }
        c.Check(w.IsCompleted && w.Result.Ok && frames >= 3, "KC5: WaitFrames applies to a queued request", frames + " frames");
    }

    // ---------------------------------------------------------------- KR2 interleaving

    static void C40Kr2(Ctx c)
    {
        var (app, host) = C40Host(c);
        var ctx = host.Find(HpGuid).Context;
        string item = W1AddItem(c);
        var p0 = (JArray)c.Call(new { cmd = "scene" })["objects"][0]["pos"];
        c.Check(ctx.Request("obj.transform", new { key = item, pos = new[] { 1, 0, 0 } }).Ok, "KR2: obj.transform through Request");
        var s = c.Ok(new { cmd = "historyprobe.set", value = 5 }, "KR2: historyprobe.set 5 (a plugin entry)");
        c.Check((string)s["result"] == "Recorded", "KR2: Recorded", Ctx.Short(c.LastText));
        c.Check(ctx.Request("obj.transform", new { key = item, pos = new[] { 2, 0, 0 } }).Ok, "KR2: obj.transform again");
        var hist = c.Ok(new { cmd = "history" }, "history");
        var undo = (hist["undo"] as JArray)?.Select(x => (string)x).ToList();
        c.Check(undo != null && undo.Count >= 4 && undo[1] == "Probe value", "KR2: the plugin entry between the two host entries (call order)", string.Join(" | ", undo ?? new List<string>()));
        double X() => (double)c.Call(new { cmd = "scene" })["objects"][0]["pos"][0];
        int V() => (int)c.Ok(new { cmd = "historyprobe.state" }, "state")["value"];
        c.Ok(new { cmd = "undo" }, "undo 1");
        c.Check(Math.Abs(X() - 1) < 1e-4 && V() == 5, "KR2: undo 1 restores the second transform only", X() + " / " + V());
        c.Ok(new { cmd = "undo" }, "undo 2");
        c.Check(Math.Abs(X() - 1) < 1e-4 && V() == 0, "KR2: undo 2 restores the plugin value", X() + " / " + V());
        c.Ok(new { cmd = "undo" }, "undo 3");
        c.Check(Math.Abs(X() - (double)p0[0]) < 1e-4 && V() == 0, "KR2: undo 3 restores the first transform", X() + " / " + V());
        c.Ok(new { cmd = "redo", n = 3 }, "redo 3");
        c.Check(Math.Abs(X() - 2) < 1e-4 && V() == 5, "KR2: redo 3 restores both", X() + " / " + V());
        // the state form and a merge
        c.Ok(new { cmd = "historyprobe.set", value = 6, form = "state", mergeKey = "k" }, "set 6 (state, key k)");
        var m = c.Ok(new { cmd = "historyprobe.set", value = 7, form = "state", mergeKey = "k" }, "set 7 (state, key k)");
        c.Check((string)m["result"] == "Merged", "KR2: the same key merges", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "undo" }, "undo the merged entry");
        c.Check(V() == 5, "KR2: the merged entry undoes to the first before state", "" + V());
        c.Ok(new { cmd = "redo" }, "redo");
        c.Check(V() == 7, "KR2: and redoes to the last after state", "" + V());
    }

    // ---------------------------------------------------------------- KR3 busy

    static void C40Kr3(Ctx c)
    {
        var (app, host) = C40Host(c);
        var b = c.Ok(new { cmd = "batch", cmds = new object[] { new { cmd = "historyprobe.set", value = 3 } } }, "KR3: a batch running the probe's command");
        string inBatch = (string)b["results"]?[0]?["result"];
        c.Check(inBatch == "Busy", "KR3: a record inside a client's batch -> Busy", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "historyprobe.arm", what = "recordInEvent" }, "arm recordInEvent");
        W1AddItem(c);
        var st = c.Ok(new { cmd = "historyprobe.state" }, "state");
        c.Check((string)st["armedResult"] == "Busy", "KR3: a record inside an event handler -> Busy", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "historyprobe.set", value = 4 }, "set 4");
        c.Ok(new { cmd = "historyprobe.arm", what = "recordInUndo" }, "arm recordInUndo");
        c.Ok(new { cmd = "undo" }, "undo the probe entry");
        st = c.Ok(new { cmd = "historyprobe.state" }, "state");
        c.Check((string)st["armedResult"] == "Busy" && (int)st["value"] == 0, "KR3: a record inside an undo callback -> Busy", Ctx.Short(c.LastText));
        var ctx = host.Find(HpGuid).Context;
        c.Check(ctx.History.CanRecord, "KR3: CanRecord outside");
    }

    // ---------------------------------------------------------------- KR4 a throwing callback

    static void C40Kr4(Ctx c)
    {
        var (app, host) = C40Host(c);
        string item = W1AddItem(c);
        c.Ok(new { cmd = "historyprobe.set", value = 9, form = "throw" }, "set 9 (callbacks that throw)");
        int faults0 = (int?)C40Plugin(c, HpGuid)?["faults"]?["history undo"] ?? 0;
        var u = c.Ok(new { cmd = "undo" }, "KR4: undo of the throwing entry answers ok");
        var w = (u["warnings"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
        c.Check(w.Contains("plugin " + HpGuid + ": undo failed (InvalidOperationException)"), "KR4: the undo reply's warning", Ctx.Short(c.LastText));
        var p = C40Plugin(c, HpGuid);
        c.Check(((int?)p?["faults"]?["history undo"] ?? 0) == faults0 + 1, "KR4: the throw counts one fault of the plugin", p?["faults"]?.ToString(Formatting.None));
        var hist = c.Ok(new { cmd = "history" }, "history");
        c.Check((hist["undo"] as JArray)?.Count == 1 && (hist["redo"] as JArray)?.Count == 1, "KR4: the history is kept (the item's entry stays, the entry moved to redo)", Ctx.Short(c.LastText));
        var rr = c.Ok(new { cmd = "redo" }, "redo");
        c.Check(((rr["warnings"] as JArray)?.Select(x => (string)x) ?? new string[0]).Contains("plugin " + HpGuid + ": redo failed (FormatException)"), "KR4: the redo warning", Ctx.Short(c.LastText));
        c.Check(host.Find(HpGuid).IsLoaded, "KR4: the plugin stays loaded");
    }

    // ---------------------------------------------------------------- KR5 the entries leave with the plugin

    static void C40Kr5(Ctx c)
    {
        var (app, host) = C40Host(c);
        c.Ok(new { cmd = "historyprobe.set", value = 1 }, "set 1");
        string item = W1AddItem(c);
        c.Ok(new { cmd = "historyprobe.set", value = 2 }, "set 2");
        c.Ok(new { cmd = "undo" }, "undo 1 (a plugin entry to redo)");
        c.Check((int?)C40Plugin(c, HpGuid)?["history"] == 2, "KR5: plugin.list history 2 (undo and redo lists)");
        c.Ok(new { cmd = "plugin.disable", guid = HpGuid }, "plugin.disable");
        var hist = c.Ok(new { cmd = "history" }, "history");
        var undo = (hist["undo"] as JArray)?.Select(x => (string)x).ToList();
        c.Check(undo?.Count == 1 && undo[0] != "Probe value" && (hist["redo"] as JArray)?.Count == 0, "KR5: only the host entry is left", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "plugin.enable", guid = HpGuid }, "plugin.enable");
        c.Check((int?)C40Plugin(c, HpGuid)?["history"] == 0, "KR5: the new instance has no entries");
        c.Ok(new { cmd = "undo" }, "undo the host entry");
        c.Check((c.Call(new { cmd = "scene" })["objects"] as JArray)?.Count == 0, "KR5: the host entry still undoes");
    }

    // ---------------------------------------------------------------- KR6 scene.new clears

    static void C40Kr6(Ctx c)
    {
        var (app, host) = C40Host(c);
        c.Ok(new { cmd = "historyprobe.set", value = 1 }, "set 1");
        c.Ok(new { cmd = "historyprobe.set", value = 2 }, "set 2");
        c.Ok(new { cmd = "scene.new" }, "scene.new");
        var hist = c.Ok(new { cmd = "history" }, "history");
        c.Check((hist["undo"] as JArray)?.Count == 0 && (hist["redo"] as JArray)?.Count == 0 && (int?)C40Plugin(c, HpGuid)?["history"] == 0, "KR6: scene.new clears the plugin's entries");
    }

    // ---------------------------------------------------------------- KR7 labels and dirty

    static void C40Kr7(Ctx c)
    {
        var (app, host) = C40Host(c);
        foreach (var f in Directory.GetFiles(app.Paths.SceneDir, "c40_*")) File.Delete(f);
        c.Ok(new { cmd = "scene.save", name = "c40_kr7" }, "scene.save");
        c.Check(!(bool)c.Ok(new { cmd = "history" }, "history")["dirty"], "KR7: clean after a save");
        c.Ok(new { cmd = "historyprobe.set", value = 3, label = "Set the probe" }, "set 3 with a label");
        var hist = c.Ok(new { cmd = "history" }, "history");
        c.Check((string)(hist["undo"] as JArray)?.FirstOrDefault() == "Set the probe" && (bool)hist["dirty"], "KR7: history lists the label; dirty", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "undo" }, "undo");
        hist = c.Ok(new { cmd = "history" }, "history");
        c.Check((string)(hist["redo"] as JArray)?.FirstOrDefault() == "Set the probe", "KR7: the redo list shows the label", Ctx.Short(c.LastText));
    }

    // ---------------------------------------------------------------- KD1 disable

    static void C40Kd1(Ctx c)
    {
        var (app, host) = C40Host(c);
        var e = host.Find(HpGuid);
        var inst = e.Instance;
        var before = C40Plugin(c, HpGuid);
        c.Check((before["commands"] as JArray)?.Count == 5 && (before["extNamespaces"] as JArray)?.Count == 1 && (int)before["events"] >= 5 && (before["hooks"] as JArray)?.Count >= 1,
            "KD1: the probe registered commands, an ext namespace, event handlers and a hook", C40Counts(before));
        c.Ok(new { cmd = "historyprobe.set", value = 4 }, "set 4");
        c.Ok(new { cmd = "historyprobe.schedule", frames = 5 }, "a frame action 5 frames ahead");
        var ctx = e.Context;
        var d = c.Ok(new { cmd = "plugin.disable", guid = HpGuid }, "KD1: plugin.disable");
        c.Check((string)d["state"] == PluginStates.Disabled && (d["disabled"] as JArray)?.Count == 1 && (bool)d["persisted"] == false, "KD1: state disabled, disabled [guid], not persisted", Ctx.Short(c.LastText));
        var p = C40Plugin(c, HpGuid);
        c.Check(p != null && (string)p["state"] == "disabled" && (p["commands"] as JArray)?.Count == 0 && (p["extNamespaces"] as JArray)?.Count == 0 && (int)p["events"] == 0 &&
                (p["hooks"] as JArray)?.Count == 0 && (int)p["supplies"] == 0 && (int)p["history"] == 0, "KD1: commands, ext namespaces, events, hooks, supplies, undo entries 0", C40Counts(p) + " history " + p?["history"]);
        c.Check(!app.Bridge.Services.Commands.Contains("historyprobe.state") && ExtRegistry.Default.NamespacesOf(HpGuid).Count == 0, "KD1: the registry and the ext registry have none of it");
        c.Check(C40Int(inst, "unloads") == 1, "KD1: Unload once", "" + C40Int(inst, "unloads"));
        c.Check(!ctx.IsActive, "KD1: IsActive false");
        bool disposed = false;
        try { var _ = ctx.Commands; }
        catch (ObjectDisposedException) { disposed = true; }
        c.Check(disposed, "KD1: the old context throws ObjectDisposedException");
        int runs = C40Int(inst, "frameRuns");
        c.Frames(10);
        c.Check(C40Int(inst, "frameRuns") == runs && runs == 0, "KD1: the frame action scheduled 5 frames ahead never runs", "" + C40Int(inst, "frameRuns"));
        var r = c.Call(new { cmd = "historyprobe.state" });
        c.Check((string)r["code"] == BridgeCodes.UnknownCommand, "KD1: its command is gone (unknown_command)", Ctx.Short(c.LastText));
        var ver = c.Ok(new { cmd = "bridge.version" }, "bridge.version");
        c.Check((ver["plugins"] as JArray)?.OfType<JObject>().Any(x => (string)x["guid"] == HpGuid && (string)x["state"] == "disabled") == true, "KD1: bridge.version shows disabled");
    }

    // ---------------------------------------------------------------- KD2 enable

    static void C40Kd2(Ctx c)
    {
        var (app, host) = C40Host(c);
        foreach (var f in Directory.GetFiles(app.Paths.SceneDir, "c40_*")) File.Delete(f);
        W1AddItem(c);
        c.Ok(new { cmd = "scene.save", name = "c40_kd2" }, "scene.save");
        c.Ok(new { cmd = "scene.load", name = "c40_kd2" }, "scene.load");
        var e = host.Find(HpGuid);
        var before = C40Plugin(c, HpGuid);
        var old = e.Instance;
        c.Ok(new { cmd = "plugin.disable", guid = HpGuid }, "plugin.disable");
        var en = c.Ok(new { cmd = "plugin.enable", guid = HpGuid }, "KD2: plugin.enable");
        c.Check((string)en["state"] == "loaded" && en["ms"] != null && en["extBlocks"] != null && en["rebuilt"] != null, "KD2: reply state loaded, ms, extBlocks, rebuilt", Ctx.Short(c.LastText));
        var after = C40Plugin(c, HpGuid);
        c.Check(C40Counts(after) == C40Counts(before), "KD2: every count of plugin.list as before the disable", C40Counts(before) + " -> " + C40Counts(after));
        c.Check(e.Instance != null && !ReferenceEquals(e.Instance, old) && e.Instance.GetType() == old.GetType(), "KD2: a new instance of the same type");
        var st = c.Ok(new { cmd = "historyprobe.state" }, "state");
        var loads = (st["sceneLoads"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
        c.Check(loads.Count == 1 && loads[0] == "Initial|c40_kd2.hs2nscene|1", "KD2: SceneLoaded once: Initial, the open scene's path and node count", string.Join(" ; ", loads));
        c.Check((int)st["appReady"] == 1 && (int)st["loads"] == 1 && (bool)st["isActive"], "KD2: AppReady once, Load once on the new instance, IsActive", Ctx.Short(c.LastText));
        c.Check((int)st["instance"] == (int)st["instances"] && (int)st["instances"] >= 2, "KD2: the newest instance answers", Ctx.Short(c.LastText));
        var en2 = c.Call(new { cmd = "plugin.enable", guid = HpGuid });
        c.Check((string)en2["code"] == BridgeCodes.Unsupported, "KD2: enable of a loaded plugin -> unsupported", Ctx.Short(c.LastText));
    }

    // ---------------------------------------------------------------- KD3 ext data across disable / enable

    static void C40Kd3(Ctx c)
    {
        var (app, host) = C40Host(c);
        foreach (var f in Directory.GetFiles(app.Paths.SceneDir, "c40_*")) File.Delete(f);
        string item = W1AddItem(c);
        c.Ok(new
        {
            cmd = "seq.set",
            sequence = new { duration = 4, tracks = new object[] { new { kind = "transform", key = item, keys = new object[] { new { t = 0, pos = new[] { 0, 0, 0 }, rot = new[] { 0, 0, 0 } }, new { t = 4, pos = new[] { 1, 0, 0 }, rot = new[] { 0, 90, 0 } } } } } },
        }, "seq.set (one transform track)");
        c.Ok(new { cmd = "historyprobe.set", value = 12 }, "the probe's value 12 (its own block)");
        var s1 = c.Ok(new { cmd = "scene.save", name = "c40_kd3a" }, "scene.save a");
        string block1 = C37ExtOf((string)s1["path"], "stp3.sample.sequencer", out string man1);
        string hp1 = C37ExtOf((string)s1["path"], HpGuid, out _);
        c.Check(block1 != null && hp1 == "{\"value\":12}", "KD3: both blocks written", Ctx.Short(block1) + " | " + hp1);
        c.Ok(new { cmd = "scene.load", name = "c40_kd3a" }, "scene.load (the fixture)");
        string get0 = c.Ok(new { cmd = "seq.get" }, "seq.get")["sequence"]?.ToString(Formatting.None);
        c.Ok(new { cmd = "plugin.disable", guid = "stp3.sample.sequencer" }, "disable the sequencer");
        c.Ok(new { cmd = "plugin.disable", guid = HpGuid }, "disable the probe");
        var en = c.Ok(new { cmd = "plugin.enable", guid = "stp3.sample.sequencer" }, "enable the sequencer");
        c.Check((int)en["extBlocks"] == 1, "KD3: the sequencer's block read again", Ctx.Short(c.LastText));
        var en2 = c.Ok(new { cmd = "plugin.enable", guid = HpGuid }, "enable the probe");
        string get1 = c.Ok(new { cmd = "seq.get" }, "seq.get")["sequence"]?.ToString(Formatting.None);
        c.Check(get0 != null && get1 == get0, "KD3: seq.get byte-equal to before the disable", Ctx.Short(get0) + " | " + Ctx.Short(get1));
        c.Check((int)c.Ok(new { cmd = "historyprobe.state" }, "state")["value"] == 12 && (int)en2["extBlocks"] == 1, "KD3: the probe's new instance read its value 12");
        var s2 = c.Ok(new { cmd = "scene.save", name = "c40_kd3b" }, "scene.save b");
        string block2 = C37ExtOf((string)s2["path"], "stp3.sample.sequencer", out string man2);
        c.Check(block2 == block1 && man2 == man1, "KD3: the saved block byte-equal", Ctx.Short(block2));
        c.Check(C37ExtOf((string)s2["path"], HpGuid, out _) == hp1, "KD3: the probe's block written back");
        // a block saved while its plugin is disabled stays as loaded (no handler: not visited)
        c.Ok(new { cmd = "plugin.disable", guid = HpGuid }, "disable the probe again");
        var s3 = c.Ok(new { cmd = "scene.save", name = "c40_kd3c" }, "scene.save c (probe disabled)");
        c.Check(C37ExtOf((string)s3["path"], HpGuid, out _) == hp1, "KD3: a disabled plugin's block is kept on save");
    }

    // ---------------------------------------------------------------- KD4 20 cycles

    static void C40Kd4(Ctx c)
    {
        var (app, host) = C40Host(c);
        var before = C40Counts(C40Plugin(c, HpGuid));
        for (int i = 0; i < 2; i++)
        {
            c.Call(new { cmd = "plugin.disable", guid = HpGuid });
            c.Call(new { cmd = "plugin.enable", guid = HpGuid });
        }
        long m0 = (long)c.Ok(new { cmd = "plugin.list", diagnostics = true }, "diagnostics before")["diagnostics"]["managedBytes"];
        var ms = new List<double>();
        int bad = 0;
        for (int i = 0; i < 20; i++)
        {
            var d = c.Call(new { cmd = "plugin.disable", guid = HpGuid });
            var en = c.Call(new { cmd = "plugin.enable", guid = HpGuid });
            if ((bool?)d["ok"] != true || (bool?)en["ok"] != true) bad++;
            else ms.Add((double)en["ms"]);
        }
        long m1 = (long)c.Ok(new { cmd = "plugin.list", diagnostics = true }, "diagnostics after")["diagnostics"]["managedBytes"];
        c.Check(bad == 0, "KD4: 20 disable / enable cycles answer ok", bad + " failed");
        c.Check(C40Counts(C40Plugin(c, HpGuid)) == before, "KD4: the counts are constant", before);
        double growth = (m1 - m0) / 20.0 / 1024.0;
        c.Check(growth <= 16, "KD4: managed growth per cycle <= 16 KiB", growth.ToString("F2") + " KiB");
        ms.Sort();
        double median = ms.Count > 0 ? ms[ms.Count / 2] : double.NaN, max = ms.Count > 0 ? ms[ms.Count - 1] : double.NaN;
        c.Check(median <= 5, "KD4: enable median <= 5 ms", median.ToString("F3") + " ms (max " + max.ToString("F3") + ")");
        c.Info($"KD4: plugin.enable median {median:F3} ms, max {max:F3} ms over 20 cycles; managed {growth:F2} KiB per cycle");
    }

    // ---------------------------------------------------------------- KD5 refusals

    static void C40Kd5(Ctx c)
    {
        var (app, host) = C40Host(c);
        c.Fails(new { cmd = "plugin.disable", guid = "stp3.test.nosuch" }, BridgeCodes.NotFound, "KD5: disable of an unknown GUID -> not_found");
        c.Fails(new { cmd = "plugin.enable", guid = "stp3.test.nosuch" }, BridgeCodes.NotFound, "KD5: enable of an unknown GUID -> not_found");
        c.Fails(new { cmd = "plugin.enable", guid = HpGuid }, BridgeCodes.Unsupported, "KD5: enable of a loaded plugin -> unsupported");
        c.Fails(new { cmd = "plugin.disable", guid = "stp3.test.wrongapi" }, BridgeCodes.Unsupported, "KD5: disable of a refused plugin -> unsupported");
        c.Fails(new { cmd = "plugin.enable", guid = "stp3.test.wrongapi" }, BridgeCodes.Unsupported, "KD5: enable of a refused plugin -> unsupported");
        var dep = c.Fails(new { cmd = "plugin.disable", guid = "stp3.test.probe" }, BridgeCodes.Unsupported, "KD5: disable with a loaded dependent -> unsupported");
        c.Check((dep["detail"]?["dependents"] as JArray)?.Select(x => (string)x).Contains("stp3.test.dependent") == true && ((string)dep["error"]).Contains("stp3.test.dependent"),
            "KD5: the refusal names the dependent", Ctx.Short(c.LastText));
        var wd = c.Ok(new { cmd = "plugin.disable", guid = "stp3.test.probe", withDependents = true }, "KD5: withDependents");
        c.Check(string.Join(",", (wd["disabled"] as JArray)?.Select(x => (string)x) ?? new string[0]) == "stp3.test.dependent,stp3.test.probe", "KD5: dependents first", Ctx.Short(c.LastText));
        var dEn = c.Fails(new { cmd = "plugin.enable", guid = "stp3.test.dependent" }, BridgeCodes.Unavailable, "KD5: enable before its dependency -> unavailable");
        c.Check((string)dEn["detail"]?["code"] == PluginCodes.Dependency, "KD5: detail.code plugin.dependency", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "plugin.enable", guid = "stp3.test.probe" }, "enable the probe");
        c.Ok(new { cmd = "plugin.enable", guid = "stp3.test.dependent" }, "enable its dependent");
        // self-disable inside its own command
        var self = c.Ok(new { cmd = "historyprobe.self" }, "historyprobe.self");
        c.Check((string)self["selfCode"] == BridgeCodes.Busy && host.Find(HpGuid).IsLoaded, "KD5: self-disable inside its own command -> busy", Ctx.Short(c.LastText));
        // a changed DLL: plugin.staleAssembly, the plugin stays disabled; the old write time back -> it loads
        var e = host.Find(HpGuid);
        string dll = e.Assemblies.FirstOrDefault()?.Path;
        var t0 = File.GetLastWriteTimeUtc(dll);
        c.Ok(new { cmd = "plugin.disable", guid = HpGuid }, "disable the probe");
        try
        {
            File.SetLastWriteTimeUtc(dll, t0.AddMinutes(1));
            var st = c.Fails(new { cmd = "plugin.enable", guid = HpGuid }, BridgeCodes.Unavailable, "KD5: a touched DLL -> unavailable");
            c.Check((string)st["detail"]?["code"] == PluginCodes.StaleAssembly && e.State == PluginStates.Disabled && ((string)st["error"]).Contains("restart"),
                "KD5: plugin.staleAssembly, the state stays disabled, the message says restart", Ctx.Short(c.LastText));
            c.Check(((string)C40Plugin(c, HpGuid)?["enableError"] ?? "").StartsWith(PluginCodes.StaleAssembly), "KD5: plugin.list enableError");
        }
        finally { File.SetLastWriteTimeUtc(dll, t0); }
        c.Ok(new { cmd = "plugin.enable", guid = HpGuid }, "KD5: with the old write time back it loads");
        c.Check(C40Plugin(c, HpGuid)?["enableError"] == null, "KD5: enableError cleared");
        // a probe disabled at start: the skipped checks run, then it loads; a prefix another loaded plugin holds is refused
        c.Dispose(app);
        string fx = Path.Combine(C37Dir, C40PrefixLater);
        Directory.CreateDirectory(fx);
        File.WriteAllText(Path.Combine(fx, "manifest.json"), C37Manifest(C40PrefixLater, extra: "\"commandPrefixes\":[\"probe.\"]"), new UTF8Encoding(false));
        File.WriteAllBytes(Path.Combine(fx, "Fixture.dll"), new byte[] { 0 });
        try { C40Kd5StartDisabled(c); }
        finally
        {
            foreach (var f in new[] { "manifest.json", "Fixture.dll" }) File.Delete(Path.Combine(fx, f));
            Directory.Delete(fx);
        }
    }

    static void C40Kd5StartDisabled(Ctx c)
    {
        var (app2, host2) = C40Host(c, HpGuid);
        var e2 = host2.Find(HpGuid);
        c.Check(e2?.State == PluginStates.Disabled && e2.Assemblies.Count == 0, "KD5: the probe is disabled at start (never loaded)", e2?.State + " " + e2?.Code);
        var ok = c.Ok(new { cmd = "plugin.enable", guid = HpGuid }, "KD5: enable of a start-disabled probe");
        c.Check((string)ok["state"] == "loaded" && c.Ok(new { cmd = "historyprobe.state" }, "state") != null, "KD5: it loads and its commands answer");
        var pt = c.Fails(new { cmd = "plugin.enable", guid = C40PrefixLater }, BridgeCodes.Unavailable, "KD5: a prefix another loaded plugin holds -> unavailable");
        c.Check((string)pt["detail"]?["code"] == PluginCodes.PrefixTaken && host2.Find(C40PrefixLater)?.State == PluginStates.Disabled, "KD5: plugin.prefixTaken, stays disabled", Ctx.Short(c.LastText));
    }

    // ---------------------------------------------------------------- KD6 persist

    static void C40Kd6(Ctx c)
    {
        string dir = Path.Combine(c.Out, "c38");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "settings.json");
        File.WriteAllText(file, "{\n  \"bridge\": {\"port\": 17722},\n  \"plugins\": {\"disabled\": [\"stp3.other\"]}\n}\n", new UTF8Encoding(false));
        var o = c.Services.NewSessionOptions();
        o.Ids = new HS2Next.Scenes.CountingIdSource(DefaultSeed);
        var s = c.HostSettings();
        s.SettingsPath = file;
        var app = AppCore.ForTests(c.Services, s, null, o);
        (typeof(Ctx).GetField("hosts", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(c) as List<AppCore>)?.Add(app);
        C37ClearLog();
        app.LoadPlugins(C37Dir, new[] { C37Disabled, C40PrefixLater });
        c.Check(string.Equals(app.Paths.SettingsFile, file, StringComparison.OrdinalIgnoreCase), "KD6: the host's settings file is the check's own", app.Paths.SettingsFile);
        List<string> Listed() => (HS2Next.App.Setup.SettingsFile.Get(HS2Next.App.Setup.SettingsFile.Read(file), "plugins.disabled") as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
        byte[] b0 = File.ReadAllBytes(file);
        var r = JObject.Parse(app.Call(Env(new { cmd = "plugin.disable", guid = HpGuid })));
        c.Check((bool?)r["ok"] == true && (bool)r["persisted"] == false && File.ReadAllBytes(file).SequenceEqual(b0), "KD6: without persist the file is unchanged", Ctx.Short(r.ToString(Formatting.None)));
        r = JObject.Parse(app.Call(Env(new { cmd = "plugin.enable", guid = HpGuid })));
        c.Check((bool?)r["ok"] == true && File.ReadAllBytes(file).SequenceEqual(b0), "KD6: enable without persist: unchanged");
        r = JObject.Parse(app.Call(Env(new { cmd = "plugin.disable", guid = HpGuid, persist = true })));
        c.Check((bool?)r["ok"] == true && (bool)r["persisted"] && Listed().SequenceEqual(new[] { "stp3.other", HpGuid }), "KD6: persist adds the GUID", string.Join(",", Listed()));
        var j = JObject.Parse(File.ReadAllText(file));
        c.Check((int?)j["bridge"]?["port"] == 17722, "KD6: the other members are kept");
        c.Check(!Directory.GetFiles(dir).Any(f => f.Contains(".tmp")), "KD6: no temporary file left (atomic replace)");
        r = JObject.Parse(app.Call(Env(new { cmd = "plugin.enable", guid = HpGuid, persist = true })));
        c.Check((bool?)r["ok"] == true && (bool)r["persisted"] && Listed().SequenceEqual(new[] { "stp3.other" }), "KD6: persist removes the GUID", string.Join(",", Listed()));
    }

    // ---------------------------------------------------------------- KD8 hooks on disable

    static void C40Kd8(Ctx c)
    {
        var (app, host) = C40Host(c);
        string chara = C30Load(c, c.FemaleRel, "female", "KD8: chara.load BR-Chan");
        c.Frames(3);
        var inst = host.Find(HpGuid).Instance;
        c.Check(C40Int(inst, "hookRuns") >= 1, "KD8: the probe's Final hook ran in the build", "" + C40Int(inst, "hookRuns"));
        int rebuilds = 0, builds = 0;
        Action<HS2Next.Scenes.Runtime.CharaRealization> h = r => { if (r.NodeId == chara) builds++; };
        app.Runtime.CharaBuilt += h;
        try
        {
            var d = c.Ok(new { cmd = "plugin.disable", guid = HpGuid }, "plugin.disable");
            rebuilds = (int?)d["rebuilt"] ?? -1;
            c.Frames(5);
        }
        finally { app.Runtime.CharaBuilt -= h; }
        c.Check(rebuilds == 1, "KD8: the reply queues one rebuild (the character its hook touched)", "" + rebuilds);
        c.Check(builds == 1, "KD8: the character is rebuilt once", "" + builds);
        var en = c.Ok(new { cmd = "plugin.enable", guid = HpGuid }, "plugin.enable");
        c.Check((int?)en["rebuilt"] == 1, "KD8: enable queues the rebuild of the built character (the probe has a hook)", Ctx.Short(c.LastText));
    }

    // ---------------------------------------------------------------- KX4a the 1.0-built 5.1 set

    static void C40Kx4a(Ctx c)
    {
        var (app, host) = C40Host(c);
        var set = C37Plugins.Concat(C37Refusing).ToList();
        var refs = new List<string>();
        foreach (var g in set)
        {
            var e = host.Find(g);
            string dll = e?.Assemblies.FirstOrDefault()?.Path ?? Directory.GetFiles(Path.Combine(C37Dir, g), "*.dll").FirstOrDefault();
            string api = ReferencedApi(dll);
            refs.Add(g + " " + (api ?? "?"));
        }
        c.Check(refs.All(x => x.EndsWith(" 1.0.0.0") || x.EndsWith(" ?")) && refs.Count(x => x.EndsWith(" 1.0.0.0")) >= C37Plugins.Length,
            "KX4a: the staged 5.1 set references HS2Next.Api 1.0.0.0 (built against fixtures/api10)", string.Join("; ", refs));
        var sum = File.Exists(Path.Combine(C37Api10, "HS2Next.Api.dll")) ? new FileInfo(Path.Combine(C37Api10, "HS2Next.Api.dll")).Length : 0;
        c.Check(sum == 33792, "KX4a: the 1.0.0 reference assembly is the fixture (33,792 bytes)", "" + sum);
        foreach (var g in C37Plugins)
            c.Check(host.Find(g)?.State == PluginStates.Loaded, "KX4a: " + g + " (1.0-built) loads on the Api " + HS2Next.ApiVersion.Semver + " host", host.Find(g)?.State + " " + host.Find(g)?.Reason);
        foreach (var g in new[] { "stp3.test.loadthrows", "stp3.test.noentry", "stp3.test.typeload" })
        {
            var e = host.Find(g);
            string want = g == "stp3.test.loadthrows" ? PluginCodes.LoadFailed : g == "stp3.test.noentry" ? PluginCodes.NoEntry : PluginCodes.TypeLoad;
            c.Check(e?.Code == want, "KX4a: " + g + " keeps its 5.1 code " + want, e?.State + " " + e?.Code);
        }
    }

    /// <summary>The version of the HS2Next.Api reference of a plugin DLL as the loaded image in the domain holds it, or null.</summary>
    static string ReferencedApi(string dll)
    {
        if (dll == null) return null;
        string full = Path.GetFullPath(dll);
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            string loc;
            try { loc = a.Location; }
            catch (Exception) { continue; }
            if (string.IsNullOrEmpty(loc) || !string.Equals(Path.GetFullPath(loc), full, StringComparison.OrdinalIgnoreCase)) continue;
            return a.GetReferencedAssemblies().FirstOrDefault(r => r.Name == "HS2Next.Api")?.Version?.ToString();
        }
        return null;
    }
}
