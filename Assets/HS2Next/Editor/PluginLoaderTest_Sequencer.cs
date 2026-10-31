// The sample sequencer's checks in the Editor play test (SQ5-SQ11), synchronous: they run on
// the play test's worker thread and talk to the play session's bridge like any client (blocking calls; frames are waited
// with a ping held for n frames). Two vanilla items carry two linear transform tracks whose rotations turn about one axis
// only (so the expected pose is a plain linear blend, independent of the plugin's own interpolation code) and three rename
// events. SQ5 the 8 commands with owner, group and prefix; SQ6 the sampled frames of play within 1e-4; SQ7 events once each
// at their times; SQ8 save -> new -> load keeps the sequence byte for byte, with the manifest ext entry and formatVersion 1;
// SQ9 delete / undo switch a track off and on; SQ10 the scene pause holds the sequence and scene.step {n: 30} advances it by
// 30 x dt; SQ11 seq.measure gives all five numbers (recorded for the comparison with the player's PA8). Each check sets up
// what it needs, so any subset can run alone. No image is written or opened.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// The play test runs every static SQ<n>(PlayCtx) of its partial class on its worker thread (one part per check id).
public static partial class PluginLoaderTest
{
    static SequencerPlayChecks.Io SeqIo(PlayCtx c) => new SequencerPlayChecks.Io
    {
        Call = json => { c.Call(json, 300000); return c.LastText; },
        Check = (id, what, ok, detail) => c.Check(ok, what, detail ?? ""),
        Info = c.Info,
    };

    static void SQ5(PlayCtx c) => SequencerPlayChecks.SQ5(SeqIo(c));
    static void SQ6(PlayCtx c) => SequencerPlayChecks.SQ6(SeqIo(c));
    static void SQ7(PlayCtx c) => SequencerPlayChecks.SQ7(SeqIo(c));
    static void SQ8(PlayCtx c) => SequencerPlayChecks.SQ8(SeqIo(c));
    static void SQ9(PlayCtx c) => SequencerPlayChecks.SQ9(SeqIo(c));
    static void SQ10(PlayCtx c) => SequencerPlayChecks.SQ10(SeqIo(c));
    static void SQ11(PlayCtx c) => SequencerPlayChecks.SQ11(SeqIo(c));
}

public static class SequencerPlayChecks
{
    public const string Guid = "stp3.sample.sequencer";
    const int ItemNo = 399;
    static readonly string[] Commands = { "seq.get", "seq.measure", "seq.pause", "seq.play", "seq.seek", "seq.set", "seq.status", "seq.stop" };

    /// <summary>What the checks need from the play test: a blocking bridge call (reply text), the check and info sinks.</summary>
    public sealed class Io
    {
        /// <summary>Request JSON -> reply JSON text (blocking; never on the main thread).</summary>
        public Func<string, string> Call;
        /// <summary>(id, what, ok, detail) -> ok.</summary>
        public Func<string, string, bool, string, bool> Check;
        public Action<string> Info;
    }

    static Io io;
    static string lastText;
    static JObject last;
    static string itemA, itemB;
    static bool Ok => last != null && last.Value<bool?>("ok") == true;

    static JObject Call(object req)
    {
        string json = req is string s ? s : req is JToken t ? t.ToString(Formatting.None) : JsonConvert.SerializeObject(req);
        try
        {
            lastText = io.Call(json);
            last = JObject.Parse(lastText);
        }
        catch (Exception e)
        {
            lastText = null;
            last = new JObject { ["ok"] = false, ["error"] = e.GetType().Name + ": " + e.Message };
        }
        if (!Ok) io.Info("call " + Short(json) + " -> " + Short(last.ToString(Formatting.None)));
        return last;
    }

    /// <summary>
    /// Waits until n frames passed by polling ping (answered at once); a reply held for n frames would hold the bridge's
    /// queue too, and with it the sequencer's queued event and measurement requests.
    /// </summary>
    static void WaitFrames(int n)
    {
        int f0 = Frame();
        var sw = Stopwatch.StartNew();
        while (Frame() < f0 + n && sw.Elapsed.TotalSeconds < 30) System.Threading.Thread.Sleep(5);
    }

    static int Frame()
    {
        try { return (int?)JObject.Parse(io.Call("{\"cmd\":\"ping\"}"))["frame"] ?? 0; }
        catch (Exception) { return 0; }
    }

    static string Short(string s) => s == null ? "null" : s.Length > 300 ? s.Substring(0, 300) + "..." : s;

    static bool Check(string id, string what, bool ok, string detail = null) => io.Check(id, what, ok, detail);

    static double Wrap360(double d)
    {
        double r = d % 360.0;
        if (r < 0) r += 360.0;
        return r;
    }

    static double AngleDiff(double a, double b)
    {
        double d = Math.Abs(Wrap360(a) - Wrap360(b));
        return d > 180 ? 360 - d : d;
    }

    static double[] V(JToken t) => new[] { (double)t[0], (double)t[1], (double)t[2] };

    static JObject NodeOf(JToken reply)
    {
        var node = reply?["node"];
        if (node == null) return null;
        return node.Type == JTokenType.String ? JObject.Parse((string)node) : node as JObject;
    }

    // The sequence: 2 s; a: pos (0,0,0)->(2,4,-6), yaw 10->70; b: pos (1,0,0)->(1,2,0), yaw 350->10 (through 0); 3 renames.
    static JObject Sequence(string a, string b) => JObject.Parse(
        "{\"duration\":2,\"loop\":false,\"tracks\":[" +
        "{\"kind\":\"transform\",\"key\":" + JsonConvert.ToString(a) + ",\"keys\":[{\"t\":0,\"pos\":[0,0,0],\"rot\":[0,10,0]},{\"t\":2,\"pos\":[2,4,-6],\"rot\":[0,70,0]}]}," +
        "{\"kind\":\"transform\",\"key\":" + JsonConvert.ToString(b) + ",\"keys\":[{\"t\":0,\"pos\":[1,0,0],\"rot\":[0,350,0]},{\"t\":2,\"pos\":[1,2,0],\"rot\":[0,10,0]}]}]," +
        "\"events\":[{\"t\":0.5,\"do\":{\"cmd\":\"obj.rename\",\"key\":" + JsonConvert.ToString(a) + ",\"name\":\"sq7-a\"}}," +
        "{\"t\":1.0,\"do\":{\"cmd\":\"obj.rename\",\"key\":" + JsonConvert.ToString(b) + ",\"name\":\"sq7-b\"}}," +
        "{\"t\":1.5,\"do\":{\"cmd\":\"obj.rename\",\"key\":" + JsonConvert.ToString(a) + ",\"name\":\"sq7-c\"}}]}");

    static double[] ExpectPosA(double t) { double u = Clamp01(t / 2); return new[] { 2 * u, 4 * u, -6 * u }; }
    static double ExpectYawA(double t) => 10 + 60 * Clamp01(t / 2);
    static double[] ExpectPosB(double t) { double u = Clamp01(t / 2); return new[] { 1.0, 2 * u, 0.0 }; }
    static double ExpectYawB(double t) => Wrap360(350 + 20 * Clamp01(t / 2));
    static double Clamp01(double u) => Math.Min(1, Math.Max(0, u));

    /// <summary>A new scene with the two items and the sequence set and applied (stopped at 0).</summary>
    static bool Setup(string id)
    {
        Call(new { cmd = "scene.new" });
        Call(new { cmd = "item.add", no = ItemNo });
        itemA = Ok ? (string)last["added"][0]["key"] : null;
        Call(new { cmd = "item.add", no = ItemNo });
        itemB = Ok ? (string)last["added"][0]["key"] : null;
        if (!Check(id, "two items added", itemA != null && itemB != null)) return false;
        Call(new JObject { ["cmd"] = "seq.set", ["sequence"] = Sequence(itemA, itemB) });
        bool set = Ok && (int)last["tracks"] == 2 && (int)last["events"] == 3 && (double)last["duration"] == 2;
        WaitFrames(2);
        return Check(id, "seq.set: 2 tracks, 3 events, 2 s", set, Short(lastText));
    }

    static JArray Batch(params JObject[] cmds)
    {
        Call(new JObject { ["cmd"] = "batch", ["cmds"] = new JArray(cmds) });
        var res = last["results"] as JArray;
        return Ok && res != null && res.Count == cmds.Length ? res : null;
    }

    static Dictionary<string, string> Names(JToken sceneReply) =>
        (sceneReply?["objects"] as JArray ?? new JArray()).ToDictionary(o => (string)o["key"], o => (string)o["name"]);

    public static void SQ5(Io x)
    {
        io = x;
        Call(new { cmd = "help" });
        var seq = (last["commands"] as JArray ?? new JArray()).Where(c => ((string)c["name"]).StartsWith("seq.", StringComparison.Ordinal))
            .OrderBy(c => (string)c["name"], StringComparer.Ordinal).ToList();
        var names = seq.Select(c => (string)c["name"]).ToArray();
        Check("SQ5", "help lists exactly the 8 seq.* commands", names.SequenceEqual(Commands), string.Join(",", names));
        Check("SQ5", "owner and group = the GUID", seq.Count == 8 && seq.All(c => (string)c["owner"] == Guid && (string)c["group"] == Guid),
            string.Join(";", seq.Select(c => c["owner"] + "/" + c["group"])));
        Call(new { cmd = "bridge.version" });
        var pl = (last["plugins"] as JArray ?? new JArray()).FirstOrDefault(p => (string)p["guid"] == Guid);
        Check("SQ5", "bridge.version lists the sample loaded", pl != null && (string)pl["state"] == "loaded" && (string)pl["version"] == "0.1.0",
            Short(last["plugins"]?.ToString(Formatting.None)));
    }

    /// <summary>SQ6 and SQ7 share one playback: SQ6 judges the poses, SQ7 the events.</summary>
    public static void SQ6(Io x) => Play(x, true, false);

    public static void SQ7(Io x) => Play(x, false, true);

    static void Play(Io x, bool poses, bool events)
    {
        io = x;
        string id = poses ? "SQ6" : "SQ7";
        if (!Setup(id)) return;
        string a = itemA, b = itemB;
        Call(new { cmd = "seq.play" });
        double worstPos = 0, worstRot = 0;
        var times = new List<double>();
        bool orderOk = true;
        JObject st = null;
        var sw = Stopwatch.StartNew();
        // A batch is one pump: the sequencer's time and the poses it wrote for that time. Every few frames to the end.
        for (int k = 0; k < 600 && sw.Elapsed.TotalSeconds < 60; k++)
        {
            WaitFrames(3);
            var res = Batch(new JObject { ["cmd"] = "seq.status" },
                new JObject { ["cmd"] = "scene.doc", ["key"] = a, ["capture"] = false, ["embed"] = true },
                new JObject { ["cmd"] = "scene.doc", ["key"] = b, ["capture"] = false, ["embed"] = true },
                new JObject { ["cmd"] = "scene" });
            if (res == null) { Check(id, "sample " + k + ": batch answered", false, Short(lastText)); return; }
            st = (JObject)res[0];
            double t = (double)st["time"];
            string state = (string)st["state"];
            if (state != "playing" && state != "ended") { Check(id, "sample " + k + ": playing", false, state); return; }
            if (state == "playing")
            {
                times.Add(t);
                var ta = NodeOf(res[1])?["transform"];
                var tb = NodeOf(res[2])?["transform"];
                var ga = V(ta["pos"]); var gra = V(ta["rot"]);
                var gb = V(tb["pos"]); var grb = V(tb["rot"]);
                var pa = ExpectPosA(t); var pb = ExpectPosB(t);
                for (int i = 0; i < 3; i++) worstPos = Math.Max(worstPos, Math.Max(Math.Abs(ga[i] - pa[i]), Math.Abs(gb[i] - pb[i])));
                worstRot = Math.Max(worstRot, Math.Max(AngleDiff(gra[1], ExpectYawA(t)), AngleDiff(grb[1], ExpectYawB(t))));
                worstRot = Math.Max(worstRot, Math.Max(Math.Max(AngleDiff(gra[0], 0), AngleDiff(gra[2], 0)), Math.Max(AngleDiff(grb[0], 0), AngleDiff(grb[2], 0))));
            }
            // A rename shows at the pump after its time at the latest (0.25 s allowed); never before its time.
            var names = Names(res[3]);
            names.TryGetValue(a, out var na);
            names.TryGetValue(b, out var nb);
            if (t < 0.5 && na != null && na.StartsWith("sq7", StringComparison.Ordinal)) orderOk = false;
            if (t > 0.75 && t < 1.5 && na != "sq7-a") orderOk = false;
            if (t < 1.0 && nb == "sq7-b") orderOk = false;
            if (t > 1.25 && nb != "sq7-b") orderOk = false;
            if (t > 1.75 && state == "playing" && na != "sq7-c") orderOk = false;
            if (state == "ended") break;
        }
        if (poses)
        {
            Check("SQ6", "at least three frames sampled while playing, at distinct times", times.Distinct().Count() >= 3,
                times.Count + " samples, t " + (times.Count > 0 ? times.Min().ToString("R") + "-" + times.Max().ToString("R") : "-"));
            Check("SQ6", "positions within 1e-4 of the interpolation", worstPos <= 1e-4, "worst " + worstPos.ToString("G3"));
            Check("SQ6", "rotations within 1e-3 degrees (350 -> 10 through 0)", worstRot <= 1e-3, "worst " + worstRot.ToString("G3"));
            io.Info("SQ6: " + times.Count + " samples, worst position " + worstPos.ToString("G3") + ", rotation " + worstRot.ToString("G3") + " deg");
        }
        if (events)
        {
            Check("SQ7", "the sequence ended at its duration", st != null && (string)st["state"] == "ended" && (double)st["time"] == 2.0, Short(st?.ToString(Formatting.None)));
            Check("SQ7", "eventsFired 3, eventErrors 0", st != null && (long)st["eventsFired"] == 3 && (long)st["eventErrors"] == 0, Short(st?["eventsFired"] + "/" + st?["eventErrors"]));
            Call(new { cmd = "scene" });
            var fin = Names(last);
            fin.TryGetValue(a, out var fa);
            fin.TryGetValue(b, out var fb);
            Check("SQ7", "the renames ran in order (a: sq7-c last, b: sq7-b)", fa == "sq7-c" && fb == "sq7-b", Short(fa + " / " + fb));
            Check("SQ7", "each sampled frame showed exactly the renames due by its time", orderOk);
            Check("SQ7", "no write failed", st != null && (long)st["writes"]["error"] == 0 && (long)st["writes"]["missingNode"] == 0, Short(st?["writes"]?.ToString(Formatting.None)));
        }
    }

    public static void SQ8(Io x)
    {
        io = x;
        if (!Setup("SQ8")) return;
        Call(new { cmd = "seq.get" });
        string before = lastText;
        Call(new { cmd = "scene.save", name = "sq8_sequence", overwrite = true, thumb = "placeholder" });
        string path = Ok ? (string)last["path"] : null;
        Call(new { cmd = "scene.new" });
        Call(new { cmd = "seq.get" });
        bool none = Ok && last["sequence"].Type == JTokenType.Null;
        Call(new { cmd = "scene.load", name = "sq8_sequence" });
        Call(new { cmd = "seq.get" });
        string after = lastText;
        Check("SQ8", "seq.get after save -> new -> load is byte-equal", before != null && before == after, Short(before) + " | " + Short(after));
        Check("SQ8", "no sequence after scene.new", none);
        JObject man = null;
        try
        {
            using (var z = ZipFile.OpenRead(path))
            using (var r = new StreamReader(z.GetEntry("manifest.json").Open(), Encoding.UTF8))
                man = JObject.Parse(r.ReadToEnd());
        }
        catch (Exception e) { io.Info("SQ8 manifest not read: " + e.Message); }
        var ext = (man?["ext"] as JArray ?? new JArray()).Where(e => (string)e["ns"] == Guid).ToList();
        Check("SQ8", "manifest: one ext entry {ns, scope scene, root \"\", version 1, required false}",
            ext.Count == 1 && (string)ext[0]["scope"] == "scene" && (string)ext[0]["root"] == "" && (int)ext[0]["version"] == 1 && (bool)ext[0]["required"] == false,
            Short(man?["ext"]?.ToString(Formatting.None)));
        Check("SQ8", "manifest formatVersion 1", man != null && (int)man["formatVersion"] == 1, Short(man?["formatVersion"]?.ToString()));
    }

    public static void SQ9(Io x)
    {
        io = x;
        if (!Setup("SQ9")) return;
        Call(new { cmd = "delete", key = itemA });
        Call(new { cmd = "seq.status" });
        bool off = Ok && (last["tracks"] as JArray).Any(t => (string)t["key"] == itemA && (bool)t["active"] == false);
        Call(new { cmd = "undo" });
        Call(new { cmd = "seq.status" });
        bool on = Ok && (last["tracks"] as JArray).Any(t => (string)t["key"] == itemA && (bool)t["active"] == true);
        Check("SQ9", "delete turns the track off, undo turns it on", off && on, Short(lastText));
        Check("SQ9", "no missing node counted", Ok && (long)last["writes"]["missingNode"] == 0);
    }

    public static void SQ10(Io x)
    {
        io = x;
        if (!Setup("SQ10")) return;
        Call(new { cmd = "seq.play", from = 0.25 });
        WaitFrames(3);
        Call(new { cmd = "scene.pause", paused = true });
        WaitFrames(2);
        Call(new { cmd = "seq.status" });
        double t0 = Ok ? (double)last["time"] : -1;
        WaitFrames(10);
        Call(new { cmd = "seq.status" });
        double t1 = Ok ? (double)last["time"] : -2;
        Check("SQ10", "the scene pause holds the sequence's time", t0 >= 0.25 && t1 == t0 && (string)last["state"] == "playing", t0.ToString("R") + " -> " + t1.ToString("R"));
        float dt = 1f / 60f;
        Call(new { cmd = "scene.step", n = 30, dt });
        Call(new { cmd = "seq.status" });
        double t2 = Ok ? (double)last["time"] : -3;
        double want = Math.Min(2.0, t1 + 30 * (double)dt);
        Check("SQ10", "scene.step {n: 30} advances by 30 x dt", Math.Abs(t2 - want) <= 1e-6, t2.ToString("R") + " vs " + want.ToString("R"));
        WaitFrames(3);
        Call(new { cmd = "scene.doc", key = itemB, capture = false, embed = true });
        var tb = NodeOf(last)?["transform"];
        Check("SQ10", "the stepped pose is written at the next frame", tb != null && Math.Abs(V(tb["pos"])[1] - ExpectPosB(t2)[1]) <= 1e-4,
            Short(tb?.ToString(Formatting.None)));
        Call(new { cmd = "scene.pause", paused = false });
        Call(new { cmd = "seq.stop" });
    }

    public static void SQ11(Io x)
    {
        io = x;
        if (!Setup("SQ11")) return;
        var keys = new List<string> { itemA, itemB };
        for (int i = 0; i < 6; i++)
        {
            Call(new { cmd = "item.add", no = ItemNo });
            if (Ok) keys.Add((string)last["added"][0]["key"]);
        }
        Call(new { cmd = "seq.measure", keys, frames = 600 });
        Check("SQ11", "seq.measure starts", Ok && (string)last["state"] == "running", Short(lastText));
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < 180)
        {
            System.Threading.Thread.Sleep(250);
            Call(new { cmd = "seq.measure" });
            if ((string)last["state"] != "running") break;
        }
        var m = last;
        bool five = (string)m["state"] == "done" && new[] { "pm1", "pm2", "pm3", "pm4", "pm5" }.All(k => m[k] is JObject)
                    && (int)m["pm1"]["n"] > 0 && (int)m["pm2"]["n"] > 0 && (int)m["pm3"]["n"] > 0 && (int)m["pm4"]["n"] > 0 && (int)m["pm4"]["lost"] == 0
                    && m["pm5"]["withMs"] != null && m["pm5"]["withoutMs"] != null;
        Check("SQ11", "seq.measure gives all five numbers", five, Short(lastText));
        io.Info("SQ11 measurement (Editor play mode): " + m.ToString(Formatting.None));
    }
}
