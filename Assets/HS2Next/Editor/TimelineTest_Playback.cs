// TimelineTest, the playback's part (Phase 6.2 package P). Headless, edit mode,
// on BR-Chan ($a), HERO107 ($b, at (5, 0, 0)), the vanilla item 399 ($p) and a light ($l); the compositions of
// Tests/Timeline/compositions built with timeline.build after their placeholders are replaced (FK on with the Body and Neck groups
// for both characters, the animation rows of Tools/targets.json's animation cases at speed 0); frames pumped by hand (AppCore.Frame):
//   TM2  every timeline.* command's arguments, replies and errors: the no-Timeline answers, the HS2 reply members, the build
//        input and its refusals (nothing built), one history entry per edit with its label, undo: false, the editing commands,
//        the 10 HS2 names in ParityNames, timeline.export / .loadxml unknown
//   TM3  timed commands: a crossing in frame k shows in frame k + 1; undo false by default (no entry), undo: true one entry; an
//        unknown command counts in eventErrors; a resume does not run its start's events again; a seek runs none; a wrap runs
//        the event at the end, then the one at 0
//   TM4  the authoring suspension: an edit while paused shows until the play head moves; the Key without v stores it; while
//        playing nothing is suspended
//   TA1  camera_dof, walk, bones9: build, play 120 frames, 5 seeks (values recorded), save, scene.new, load: the Timeline equal
//        and the 5 seeks give the same values (1e-6)
//   TA6  a save while the Timeline plays (scene paused) equals, entry for entry, the save after timeline.stop, and its scene.json
//        the one saved before playback
//   TA7  the bake: one history entry, the scene's values = the shown values (1e-5 units, 0.01 deg), undo restores the scene.json
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using HS2Next.App.Commands;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Timeline;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class TimelineTest
{
    const string Clock = "2026-11-03T00:00:00Z";

    static void Headless_P_Playback()
    {
        Group("TM2", "the timeline.* commands: arguments, replies, errors, history", TM2);
        Group("TM3", "timed commands", TM3);
        Group("TM4", "the authoring suspension through commands", TM4);
        Group("TA1", "the three compositions round trip (play, seek, save, load)", TA1);
        Group("TA6", "a save during playback", TA6);
        Group("TA7", "the bake", TA7);
    }

    // ------------------------------------------------------------ the fixture

    sealed class PFix
    {
        public Host H;
        public string A, B, P, L;
        public TimelinePlayback Pb => H.Rt.GetPart<TimelinePlayback>();
        public TimelineService S => Pb.Service;
        public void Dispose() => H?.Dispose();
    }

    /// <summary>A host with BR-Chan, HERO107 at (5, 0, 0), item 399 and a light; FK on (Body, Neck) for both; their rows at speed 0.</summary>
    static PFix NewFix(ulong seed)
    {
        var f = new PFix { H = NewHost(seed) };
        var h = f.H;
        f.A = h.Ok(new { cmd = "chara.load", path = femaleRel, sex = "female" }, "fixture: chara.load BR-Chan").Key();
        f.B = h.Ok(new { cmd = "chara.load", path = maleRel, sex = "male" }, "fixture: chara.load HERO107").Key();
        f.P = h.Ok(new { cmd = "item.add", no = 399 }, "fixture: item.add 399").Key();
        f.L = h.Ok(new { cmd = "light.add", no = 0, pos = new[] { 0, 12, 3 } }, "fixture: light.add 0").Key();
        if (f.A == null || f.B == null || f.P == null || f.L == null) throw new InvalidOperationException("the fixture's nodes were not added");
        h.Ok(new { cmd = "obj.transform", key = f.B, pos = new[] { 5, 0, 0 } }, "fixture: HERO107 at (5, 0, 0)");
        h.Ok(new { cmd = "chara.anime", key = f.A, group = 19, category = 0, no = 0, speed = 0 }, "fixture: BR-Chan's row at speed 0");
        h.Ok(new { cmd = "chara.anime", key = f.B, group = 11, category = 5, no = 0, speed = 0 }, "fixture: HERO107's row at speed 0");
        foreach (var c in new[] { f.A, f.B })
        {
            h.Ok(new { cmd = "fk.enable", key = c, on = true }, "fixture: FK on");
            h.Ok(new { cmd = "fk.enable", key = c, groups = new[] { "Body", "Neck" }, on = true }, "fixture: FK groups Body and Neck");
        }
        h.Frames(3);
        Check("fixture: the Timeline playback part exists", f.Pb != null);
        return f;
    }

    /// <summary>A composition of Tests/Timeline/compositions with its placeholders replaced by the fixture's node ids.</summary>
    static JObject Composition(PFix f, string name)
    {
        string json = File.ReadAllText(Path.Combine(ContentSettings.ProjectRoot, "Tests", "Timeline", "compositions", name + ".json"));
        json = json.Replace("\"$a\"", "\"" + f.A + "\"").Replace("\"$b\"", "\"" + f.B + "\"").Replace("\"$p\"", "\"" + f.P + "\"").Replace("\"$l\"", "\"" + f.L + "\"");
        return JObject.Parse(json);
    }

    /// <summary>timeline.build of a composition (its duration, loop and tracks) with <paramref name="play"/>.</summary>
    static JObject BuildComposition(PFix f, string name, bool play)
    {
        var c = Composition(f, name);
        var req = new JObject { ["cmd"] = "timeline.build", ["duration"] = c["duration"], ["tracks"] = c["tracks"], ["play"] = play };
        if (c["loop"] != null) req["loop"] = c["loop"];
        return f.H.Ok(req.ToString(Formatting.None), "timeline.build " + name);
    }

    static JObject Err(Host h, object req, string code, string what)
    {
        var r = h.Call(req);
        Check(what + " -> " + code, r.Value<bool?>("ok") == false && r.Value<string>("code") == code, Short(h.LastText));
        return r;
    }

    static List<string> UndoLabels(Host h) => (h.Call(new { cmd = "history" })["undo"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();

    static string TimelineJson(Host h) => h.Session.Model.Timeline?.ToJson();

    static Vector3 HolderPos(Host h, string id) => h.Rt.Find(id)?.Holder ? h.Rt.Find(id).Holder.localPosition : new Vector3(float.NaN, 0, 0);

    static Vec3 ModelPos(Host h, string id) => h.Session.Model.Find(id)?.Doc.Transform?.Pos ?? new Vec3(float.NaN, 0, 0);

    static bool Near(Vector3 a, float x, float y, float z, float tol = 1e-5f) => Math.Abs(a.x - x) <= tol && Math.Abs(a.y - y) <= tol && Math.Abs(a.z - z) <= tol;

    static string V3(Vector3 v) => "(" + v.x.ToString("R", CultureInfo.InvariantCulture) + ", " + v.y.ToString("R", CultureInfo.InvariantCulture) + ", " + v.z.ToString("R", CultureInfo.InvariantCulture) + ")";

    // ------------------------------------------------------------ TM2

    static void TM2()
    {
        var f = NewFix(0x7a2a0001UL);
        try
        {
            var h = f.H;
            // the names
            var parity = new HashSet<string>(CoreCommands.ParityNames, StringComparer.Ordinal);
            var ten = new[] { "timeline.build", "timeline.events", "timeline.play", "timeline.pause", "timeline.stop", "timeline.seek", "timeline.clear", "timeline.status", "record.start", "record.stop" };
            Check("ParityNames holds the 10 HS2 names of timeline.* and record.*", ten.All(parity.Contains), string.Join(", ", ten.Where(n => !parity.Contains(n))));
            Check("ParityNames: 89 names", CoreCommands.ParityNames.Count == 89, "" + CoreCommands.ParityNames.Count);
            Check("Phase62Names: 20 commands", CoreCommands.Phase62Names.Count == 20, "" + CoreCommands.Phase62Names.Count);
            var badHelp = new List<string>();
            foreach (var n in TimelineCommands.Names)
            {
                var r = h.Call(new { cmd = "help", name = n });
                var cmd = r["command"];
                if (r.Value<bool?>("ok") != true || (string)cmd?["since"] != "1.2.0" || (string)cmd?["group"] != "timeline") badHelp.Add(n + " " + Short(h.LastText));
            }
            Check("help: the 16 timeline commands, Since 1.2.0, group timeline", badHelp.Count == 0, string.Join("; ", badHelp.Take(3)));
            Err(h, new { cmd = "timeline.export" }, "unknown_command", "timeline.export is not registered");
            Err(h, new { cmd = "timeline.loadxml" }, "unknown_command", "timeline.loadxml is not registered");

            // no Timeline
            var st = h.Ok(new { cmd = "timeline.status" }, "timeline.status without a Timeline");
            Check("no Timeline: status none, playing false, time 0, duration 0, interpolables 0",
                (string)st["state"] == "none" && (bool)st["playing"] == false && (double)st["time"] == 0 && (double)st["duration"] == 0 && (int)st["interpolables"] == 0, Short(h.LastText));
            var pl = h.Ok(new { cmd = "timeline.play" }, "timeline.play without a Timeline");
            Check("no Timeline: play is a no-op with a warning", (string)pl["state"] == "none" && (pl["warnings"] as JArray)?.Count > 0, Short(h.LastText));
            Check("no Timeline: pause is a no-op", (string)h.Ok(new { cmd = "timeline.pause" }, "timeline.pause")["state"] == "none");
            Check("no Timeline: seek is a no-op", (string)h.Ok(new { cmd = "timeline.seek", t = 1 }, "timeline.seek")["state"] == "none");
            var sp = h.Ok(new { cmd = "timeline.stop", bake = true }, "timeline.stop {bake} without a Timeline");
            Check("no Timeline: stop bakes nothing", (int)sp["baked"] == 0 && (string)sp["state"] == "none", Short(h.LastText));
            Check("no Timeline: clear answers cleared false", (bool)h.Ok(new { cmd = "timeline.clear" }, "timeline.clear")["cleared"] == false);
            Check("no Timeline: get answers timeline null", h.Ok(new { cmd = "timeline.get" }, "timeline.get")["timeline"]?.Type == JTokenType.Null);
            Check("no Timeline: events {} answers 0 and creates none", (int)h.Ok(new { cmd = "timeline.events" }, "timeline.events {}")["events"] == 0 && h.Session.Model.Timeline == null);
            Err(h, new { cmd = "timeline.key.set", track = 0, t = 0, v = new[] { 0, 0, 0 } }, "not_found", "key.set without a Timeline");
            Err(h, new { cmd = "timeline.track.set", track = 0, enabled = false }, "not_found", "track.set without a Timeline");
            Check("no Timeline after the no-ops", h.Session.Model.Timeline == null);

            // build
            var labels0 = UndoLabels(h);
            var b = BuildComposition(f, "camera_dof", true);
            Check("build camera_dof: {tracks 2, keys 10, events 0, duration 10, state playing}",
                (int)b["tracks"] == 2 && (int)b["keys"] == 10 && (int)b["events"] == 0 && (double)b["duration"] == 10 && (string)b["state"] == "playing", Short(h.LastText));
            var labels1 = UndoLabels(h);
            Check("build: one history entry «Build Timeline»", labels1.Count == labels0.Count + 1 && labels1[0] == "Build Timeline", string.Join(", ", labels1.Take(3)));
            st = h.Ok(new { cmd = "timeline.status" }, "timeline.status playing");
            var members = new[] { "playing", "time", "duration", "interpolables", "state", "loop", "autoplay", "tracks", "keys", "events", "activeTracks", "eventsFired", "eventErrors", "lastEventError", "scenePaused", "suspended" };
            Check("status carries every member (HS2's playing, time, duration, interpolables first)", members.All(m => st.Property(m) != null), Short(h.LastText));
            Check("status while playing: playing, 2 tracks hold a value", (bool)st["playing"] && (int)st["interpolables"] == 2 && (int)st["activeTracks"] == 2, Short(h.LastText));
            var xml = h.Ok(new { cmd = "timeline.build", duration = 5, tracks = new object[0], returnXml = true, blockLength = 2, play = false }, "build returnXml");
            Check("returnXml warns (no XML in HS2Next); an empty build keeps the state", (xml["warnings"] as JArray)?.Any(w => ((string)w).Contains("XML")) == true && (string)xml["state"] == "playing", Short(h.LastText));
            h.Ok(new { cmd = "undo" }, "undo the empty build");
            string doc0 = TimelineJson(h);

            // build refusals: nothing built
            Err(h, new { cmd = "timeline.build", tracks = new object[] { new { type = "zoom", keys = new object[0] } } }, "bad_args", "build: an unknown type");
            Err(h, new { cmd = "timeline.build", tracks = new object[] { new { type = "pos", key = f.P, keys = new object[] { new { t = 1, v = new[] { 0, 0, 0 } }, new { t = 1, v = new[] { 1, 0, 0 } } } } } }, "bad_args", "build: two keys at one time");
            Err(h, new { cmd = "timeline.build", tracks = new object[] { new { type = "pos", key = 3, keys = new object[0] } } }, "bad_args", "build: a number as key");
            Err(h, new { cmd = "timeline.build", tracks = new object[] { new { type = "pos", key = "00000000000000000000000000000abc", keys = new object[0] } } }, "not_found", "build: a node not in the scene");
            Err(h, new { cmd = "timeline.build", tracks = new object[] { new { type = "anime", key = f.P, keys = new object[0] } } }, "wrong_kind", "build: an animation track on an item");
            Err(h, new { cmd = "timeline.build", tracks = new object[] { new { type = "pos", key = f.P }, new { type = "pos", key = f.P } } }, "bad_args", "build: a second track on a channel");
            Err(h, new { cmd = "timeline.build", duration = 0 }, "bad_args", "build: duration 0");
            Err(h, new { cmd = "timeline.build", tracks = new object[] { new { type = "camera", keys = new object[] { new { t = 0, target = new[] { 0, 0, 0 } } } } } }, "bad_args", "build: a first camera key without rot");
            Check("refused builds change nothing", TimelineJson(h) == doc0);

            // undo: false
            int n0 = UndoLabels(h).Count;
            h.Ok(new { cmd = "timeline.set", duration = 12, undo = false }, "timeline.set {duration} undo false");
            Check("undo: false applies without an entry and keeps the history", UndoLabels(h).Count == n0 && h.Session.Model.Timeline.Duration == 12f && h.Session.Commands.CanUndo);

            // transport
            h.Ok(new { cmd = "timeline.stop" }, "stop");
            var p1 = h.Ok(new { cmd = "timeline.play" }, "play");
            double t1 = (double)p1["time"];
            var p2 = h.Ok(new { cmd = "timeline.play" }, "play while playing");
            Check("play is idempotent (playing stays playing, the time goes on)", (string)p2["state"] == "playing" && (double)p2["time"] >= t1, Short(h.LastText));
            var pa = h.Ok(new { cmd = "timeline.pause" }, "pause");
            double tp = (double)pa["time"];
            h.Frames(5);
            Check("pause: paused, the time holds", (string)pa["state"] == "paused" && f.Pb.Player.Time == tp, tp + " -> " + f.Pb.Player.Time);
            var sk = h.Ok(new { cmd = "timeline.seek", t = 3 }, "seek 3 while paused");
            Check("seek while paused: paused at 3", (string)sk["state"] == "paused" && Math.Abs((double)sk["time"] - 3) < 1e-9, Short(h.LastText));
            var pf = h.Ok(new { cmd = "timeline.play", from = 2 }, "play from 2");
            Check("play {from}: playing from 2", (string)pf["state"] == "playing" && (double)pf["time"] >= 2 && (double)pf["time"] < 2.1, Short(h.LastText));
            var so = h.Ok(new { cmd = "timeline.stop" }, "stop");
            Check("stop: stopped at 0, no value shown", (string)so["state"] == "stopped" && (double)so["time"] == 0 && f.Pb.Player.ValuesShown == 0, Short(h.LastText));
            var sk2 = h.Ok(new { cmd = "timeline.seek", t = 50 }, "seek past the end from stopped");
            Check("seek from stopped: paused, clamped to the duration", (string)sk2["state"] == "paused" && Math.Abs((double)sk2["time"] - 12) < 1e-9, Short(h.LastText));
            var perf = h.Ok(new { cmd = "timeline.status", perf = true, reset = true }, "status {perf, reset}");
            Check("status {perf}: perf {frames, medianUs, p95Us, maxUs}", perf["perf"] is JObject po && po.Property("frames") != null && po.Property("medianUs") != null && po.Property("p95Us") != null && po.Property("maxUs") != null, Short(h.LastText));

            // get / set
            var g = h.Ok(new { cmd = "timeline.get" }, "timeline.get");
            Check("get: the stored form, the state and the time", g["timeline"] is JObject gt && (double)gt["duration"] == 12 && (gt["tracks"] as JArray)?.Count == 2 && (string)g["state"] == "paused", Short(h.LastText));
            n0 = UndoLabels(h).Count;
            h.Ok(new JObject { ["cmd"] = "timeline.set", ["timeline"] = g["timeline"] }.ToString(Formatting.None), "set the same Timeline");
            Check("set of the same Timeline: no entry", UndoLabels(h).Count == n0);
            var bad = (JObject)g["timeline"].DeepClone();
            ((JArray)bad["tracks"])[0]["keys"][1]["t"] = ((JArray)bad["tracks"])[0]["keys"][0]["t"];
            Err(h, new JObject { ["cmd"] = "timeline.set", ["timeline"] = bad }.ToString(Formatting.None), "bad_args", "set: a stored form with two keys at one time");
            string before = TimelineJson(h);
            h.Ok(new { cmd = "timeline.set", timeline = (object)null }, "set {timeline: null}");
            Check("set {timeline: null} removes the Timeline (one entry «Timeline»)", h.Session.Model.Timeline == null && UndoLabels(h)[0] == "Timeline");
            h.Ok(new { cmd = "undo" }, "undo");
            Check("undo restores it byte-equal", TimelineJson(h) == before);

            // tracks
            var add = h.Ok(new { cmd = "timeline.track.add", type = "pos", key = f.P, label = "Item" }, "track.add pos of the item");
            string tid = (string)add["track"];
            Check("track.add: {track, index} at the end, one entry «Add track»", tid?.Length == 32 && (int)add["index"] == 2 && UndoLabels(h)[0] == "Add track", Short(h.LastText));
            Err(h, new { cmd = "timeline.track.add", type = "pos", key = f.P }, "bad_args", "track.add: a second track on the channel");
            Err(h, new { cmd = "timeline.track.add", type = "eyes", key = f.P }, "wrong_kind", "track.add: eyes of an item");
            Err(h, new { cmd = "timeline.track.add", type = "rot", key = f.P, bone = "cf_J_Neck" }, "wrong_kind", "track.add: a bone of an item");
            Err(h, new { cmd = "timeline.track.add", type = "camera", key = f.P }, "bad_args", "track.add: a camera track with a node");
            Err(h, new { cmd = "timeline.track.add", type = "color", key = f.P }, "bad_args", "track.add: an item colour without slot");
            var add2 = h.Ok(new { cmd = "timeline.track.add", type = "color", key = f.P, slot = 0, index = 0, keys = new object[] { new { t = 0, ease = "linear", v = new[] { 1, 0, 0, 1 } } } }, "track.add color slot 0 with a stored key at index 0");
            Check("track.add at index 0 with a key", (int)add2["index"] == 0 && h.Session.Model.Timeline.Tracks[0].Keys.Count == 1);
            var ts = h.Ok(new { cmd = "timeline.track.set", track = tid, enabled = false, label = "Item 2" }, "track.set enabled false, label");
            Check("track.set: changed [label, enabled], one entry «Track»", (ts["changed"] as JArray)?.Select(x => (string)x).OrderBy(x => x).SequenceEqual(new[] { "enabled", "label" }) == true && UndoLabels(h)[0] == "Track", Short(h.LastText));
            var ti = h.Ok(new { cmd = "timeline.track.set", track = tid, index = 0 }, "track.set index 0");
            Check("track.set {index} moves the track", h.Session.Model.Timeline.Tracks[0].Id == tid && (ti["changed"] as JArray)?.Count == 1);
            var tn = h.Ok(new { cmd = "timeline.track.set", track = tid, enabled = false }, "track.set without a change");
            Check("track.set without a change: changed [] and no entry", (tn["changed"] as JArray)?.Count == 0 && UndoLabels(h)[0] == "Track");
            var rm = h.Ok(new { cmd = "timeline.track.remove", tracks = new object[] { 1 } }, "track.remove by index");
            Check("track.remove: removed 1, one entry «Remove track»", (int)rm["removed"] == 1 && UndoLabels(h)[0] == "Remove track", Short(h.LastText));
            Err(h, new { cmd = "timeline.track.remove", track = "ffffffffffffffffffffffffffffffff" }, "not_found", "track.remove: an unknown id");

            // keys
            h.Ok(new { cmd = "timeline.track.set", track = tid, enabled = true }, "enable the item track");
            h.Ok(new { cmd = "timeline.stop" }, "stop");
            var ks = h.Ok(new { cmd = "timeline.key.set", track = tid, t = 2, v = new[] { 1, 2, 3 } }, "key.set with v");
            Check("key.set: {track, index, t, v, ease inout, keys}", (int)ks["index"] == 0 && (double)ks["t"] == 2 && (ks["v"] as JArray)?.Select(x => (double)x).SequenceEqual(new double[] { 1, 2, 3 }) == true
                && (string)ks["ease"] == "inout" && (int)ks["keys"] == 1 && UndoLabels(h)[0] == "Key", Short(h.LastText));
            var kn = h.Ok(new { cmd = "timeline.key.set", track = tid, t = 0 }, "key.set without v (the shown value: the item's own position)");
            var mp = ModelPos(h, f.P);
            Check("key.set without v stores the shown value", (kn["v"] as JArray)?.Select(x => (float)(double)x).SequenceEqual(new[] { mp.X, mp.Y, mp.Z }) == true, Short(h.LastText));
            n0 = UndoLabels(h).Count;
            h.Ok(new { cmd = "timeline.key.set", track = tid, t = 4, v = new[] { 0, 1, 0 }, merge = true }, "key.set merge 1");
            h.Ok(new { cmd = "timeline.key.set", track = tid, t = 4, v = new[] { 0, 2, 0 }, merge = true, ease = "out" }, "key.set merge 2");
            Check("two key.set {merge} of one key: one entry", UndoLabels(h).Count == n0 + 1);
            Err(h, new { cmd = "timeline.key.set", track = tid, t = 99, v = new[] { 0, 0, 0 } }, "bad_args", "key.set: t outside the Timeline");
            Err(h, new { cmd = "timeline.key.set", track = tid, t = 1, v = "x" }, "bad_args", "key.set: v of another form");
            var mv = h.Ok(new { cmd = "timeline.key.move", track = tid, t = 4, to = 5 }, "key.move to");
            Check("key.move {to}: times [5], one entry «Move keys»", (mv["times"] as JArray)?.Select(x => (double)x).SequenceEqual(new double[] { 5 }) == true && UndoLabels(h)[0] == "Move keys", Short(h.LastText));
            Err(h, new { cmd = "timeline.key.move", track = tid, t = 5, to = 2 }, "bad_args", "key.move onto another key");
            Err(h, new { cmd = "timeline.key.move", track = tid, times = new[] { 0, 2 }, by = -1 }, "bad_args", "key.move before 0");
            n0 = UndoLabels(h).Count;
            h.Ok(new { cmd = "timeline.key.move", track = tid, times = new[] { 2, 5 }, by = 0.5, merge = true }, "key.move by, merge 1");
            h.Ok(new { cmd = "timeline.key.move", track = tid, times = new[] { 2.5, 5.5 }, by = 0.5, merge = true }, "key.move by, merge 2");
            Check("two key.move {merge}: one entry", UndoLabels(h).Count == n0 + 1);
            Err(h, new { cmd = "timeline.key.delete", track = tid, t = 1.5 }, "not_found", "key.delete: no key there");
            var kd = h.Ok(new { cmd = "timeline.key.delete", track = tid, times = new[] { 3, 6 } }, "key.delete");
            Check("key.delete: deleted 2, one entry «Delete keys»", (int)kd["deleted"] == 2 && UndoLabels(h)[0] == "Delete keys" && h.Session.Model.Timeline.FindTrack(tid).Keys.Count == 1, Short(h.LastText));

            // events
            var ev = h.Ok(new { cmd = "timeline.events", events = new object[] { new { t = 1, @do = new { cmd = "obj.transform", key = f.P, pos = new[] { 0, 1, 0 } }, note = "up" } } }, "timeline.events");
            Check("events: {events 1}, one entry «Timeline events»", (int)ev["events"] == 1 && UndoLabels(h)[0] == "Timeline events", Short(h.LastText));
            Err(h, new { cmd = "timeline.events", events = new object[] { new { t = 1, @do = new { cmd = "timeline.build" } } } }, "bad_args", "events: a do that builds the Timeline");
            Err(h, new { cmd = "timeline.events", events = new object[] { new { t = 1, @do = new { cmd = "scene", id = 3 } } } }, "bad_args", "events: a do with an id");
            Err(h, new { cmd = "timeline.events", events = new object[] { new { t = 1 } } }, "bad_args", "events: no do");
            Check("events {clear} empties them", (int)h.Ok(new { cmd = "timeline.events", clear = true }, "events clear")["events"] == 0 && h.Session.Model.Timeline.Events.Count == 0);

            // clear
            var cl = h.Ok(new { cmd = "timeline.clear" }, "timeline.clear");
            Check("clear: cleared, one entry «Clear Timeline», state none", (bool)cl["cleared"] && h.Session.Model.Timeline == null && UndoLabels(h)[0] == "Clear Timeline" && f.S.State == TimelinePlayState.None);
            var e2 = h.Ok(new { cmd = "timeline.events", events = new object[] { new { t = 25, @do = new { cmd = "scene" } } } }, "events without a Timeline");
            Check("events without a Timeline: one of max(10, the latest time) seconds", (int)e2["events"] == 1 && h.Session.Model.Timeline?.Duration == 25f);

            // tracks without a node, a bone or a label (camera, dof; a character's own values)
            var cam = h.Ok(new { cmd = "timeline.track.add", type = "camera" }, "track.add camera (no key)");
            string camId = (string)cam["track"];
            var ct = camId != null ? h.Session.Model.Timeline.FindTrack(camId) : null;
            Check("track.add camera without a key: key, bone and label null, one entry «Add track»", ct != null && ct.Key == null && ct.Bone == null && ct.Label == null && ct.Slot == null
                && UndoLabels(h)[0] == "Add track", Short(h.LastText));
            Err(h, new { cmd = "timeline.track.add", type = "camera" }, "bad_args", "track.add: a second camera track");
            Err(h, new { cmd = "timeline.track.add", type = "dof", key = f.P }, "bad_args", "track.add: a dof track with a node");
            n0 = UndoLabels(h).Count;
            string dofId = f.S.TrackAdd("dof", null, null, null, firstKeyAt: 1);
            var dt = h.Session.Model.Timeline.FindTrack(dofId);
            Check("TrackAdd dof (the panel's call, no key) with its first key at 1 = the shown value: one entry «Add track», one key",
                dt != null && dt.Key == null && dt.Keys.Count == 1 && dt.Keys[0].T == 1f && dt.Keys[0].Dof != null && UndoLabels(h).Count == n0 + 1 && UndoLabels(h)[0] == "Add track");
            var ck = h.Ok(new { cmd = "timeline.key.set", track = camId, t = 2, v = new { target = new[] { 0, 1, 0 }, rot = new[] { 10, 20, 0 }, dist = 3, fov = 40 } }, "key.set with v on the camera track");
            Check("key.set with v on the camera track (a track without a key)", (int)ck["keys"] == 1 && (double)ck["v"]?["dist"] == 3, Short(h.LastText));
            var ck2 = h.Ok(new { cmd = "timeline.key.set", track = camId, t = 3 }, "key.set without v on the camera track (the shown view)");
            Check("key.set without v on the camera track", (int)ck2["keys"] == 2 && ck2["v"]?["target"] is JArray, Short(h.LastText));
            var ckd = h.Ok(new { cmd = "timeline.key.set", track = dofId, t = 2 }, "key.set without v on the dof track");
            Check("key.set without v on the dof track", (int)ckd["keys"] == 2 && ckd["v"]?["on"] != null, Short(h.LastText));
            var asp = h.Ok(new { cmd = "timeline.track.add", type = "animeSpeed", key = f.A, label = (string)null }, "track.add animeSpeed, label null");
            var at = h.Session.Model.Timeline.FindTrack((string)asp["track"]);
            Check("track.add of a character value without bone or label", at != null && at.Key == f.A && at.Bone == null && at.Label == null, Short(h.LastText));
            var rr = h.Ok(new { cmd = "timeline.track.add", type = "rot", key = f.A }, "track.add rot of a character without a bone");
            var rt = h.Session.Model.Timeline.FindTrack((string)rr["track"]);
            Check("track.add rot without a bone: the node's own rotation", rt != null && rt.Bone == null && rt.Key == f.A, Short(h.LastText));
            var lb = h.Ok(new { cmd = "timeline.track.set", track = camId, label = "View" }, "track.set label");
            var lb0 = h.Ok(new { cmd = "timeline.track.set", track = camId, label = (string)null }, "track.set label null");
            Check("track.set label, then label null (generated again)", (lb0["changed"] as JArray)?.Select(x => (string)x).SequenceEqual(new[] { "label" }) == true
                && h.Session.Model.Timeline.FindTrack(camId).Label == null, Short(h.LastText));
            string rid = (string)rr["track"];
            h.Ok(new { cmd = "timeline.track.set", track = rid, bone = "cf_J_Neck" }, "track.set bone");
            var bn = h.Ok(new { cmd = "timeline.track.set", track = rid, bone = (string)null }, "track.set bone null");
            Check("track.set bone, then bone null (the node's own rotation again)", (bn["changed"] as JArray)?.Select(x => (string)x).SequenceEqual(new[] { "bone" }) == true
                && h.Session.Model.Timeline.FindTrack(rid).Bone == null, Short(h.LastText));
            Err(h, new { cmd = "timeline.key.set", track = camId, t = 4, v = (object)null }, "bad_args", "key.set {v: null}");
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ TM3

    static void TM3()
    {
        var f = NewFix(0x7a3a0001UL);
        try
        {
            var h = f.H;
            var s = f.S;
            var pb = f.Pb;
            h.Ok(new
            {
                cmd = "timeline.events",
                events = new object[]
                {
                    new { t = 0.5, @do = new { cmd = "obj.transform", key = f.P, pos = new[] { 1, 0, 0 } } },
                    new { t = 0.7, @do = new { cmd = "obj.transform", key = f.P, pos = new[] { 2, 0, 0 }, undo = true } },
                    new { t = 0.8, @do = new { cmd = "zz.unknown" } },
                },
            }, "three timed commands");
            h.Ok(new { cmd = "timeline.set", duration = 1 }, "duration 1");
            var labels0 = UndoLabels(h);
            long fired0 = pb.EventsFired;
            s.Play();
            // frame by frame to the crossing of 0.5
            int k = 0;
            while (pb.Player.Time < 0.5 && k < 200) { h.App.Frame(Dt); k++; }
            Check("the crossing frame: the event's effect not shown yet", ModelPos(h, f.P).X == 0f && pb.EventsFired == fired0 + 1, "frame " + k + ", time " + pb.Player.Time + ", x " + ModelPos(h, f.P).X);
            h.App.Frame(Dt);
            Check("the next frame: the effect shows (frame k + 1)", ModelPos(h, f.P).X == 1f, "x " + ModelPos(h, f.P).X);
            Check("undo false by default: no history entry", UndoLabels(h).Count == labels0.Count);
            while (pb.Player.Time < 0.85 && k < 400) { h.App.Frame(Dt); k++; }
            h.App.Frame(Dt);
            pb.CollectReplies();
            var labels1 = UndoLabels(h);
            Check("undo: true in the request: one entry", labels1.Count == labels0.Count + 1, string.Join(", ", labels1.Take(3)));
            Check("an unknown command counts in eventErrors (unknown_command)", pb.EventErrors == 1 && pb.LastEventError?.Code == "unknown_command", pb.EventErrors + " " + pb.LastEventError?.Code);
            // a resume does not run its start's events again; a seek runs none
            s.Pause();
            long fired1 = pb.EventsFired;
            s.Play();
            h.App.Frame(Dt);
            h.App.Frame(Dt);
            Check("a resume runs no event of [0, now] again", pb.EventsFired == fired1, fired1 + " -> " + pb.EventsFired);
            s.Pause();
            s.Seek(0.4);
            s.Seek(0.9);
            s.Seek(0.1);
            h.App.Frame(Dt);
            Check("seeks run no event", pb.EventsFired == fired1);
            var st = h.Ok(new { cmd = "timeline.status" }, "status");
            Check("status: eventsFired, eventErrors, lastEventError {event, code, message}", (long)st["eventsFired"] == pb.EventsFired && (long)st["eventErrors"] == 1 && (string)st["lastEventError"]?["code"] == "unknown_command", Short(h.LastText));

            // a wrap: the event at the end, then the one at 0
            h.Ok(new
            {
                cmd = "timeline.events",
                events = new object[]
                {
                    new { t = 0, @do = new { cmd = "obj.transform", key = f.P, pos = new[] { 20, 0, 0 } } },
                    new { t = 1, @do = new { cmd = "obj.transform", key = f.P, pos = new[] { 10, 0, 0 } } },
                },
            }, "events at 0 and at the end");
            s.Play(0.9);
            long fired2 = pb.EventsFired;
            k = 0;
            while (pb.EventsFired < fired2 + 2 && k < 60) { h.App.Frame(Dt); k++; }
            h.App.Frame(Dt);
            Check("a wrap runs the end's event, then 0's (x 10 then 20)", pb.EventsFired == fired2 + 2 && ModelPos(h, f.P).X == 20f, "fired " + (pb.EventsFired - fired2) + ", x " + ModelPos(h, f.P).X);
            s.Stop();
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ TM4

    static void TM4()
    {
        var f = NewFix(0x7a4a0001UL);
        try
        {
            var h = f.H;
            var b = h.Ok(new { cmd = "timeline.build", duration = 4, play = false, tracks = new object[] { new { type = "pos", key = f.P, ease = "linear", keys = new object[] { new { t = 0, v = new[] { 0, 0, 0 } }, new { t = 2, v = new[] { 2, 0, 0 } } } } } }, "a pos track on the item");
            h.Ok(new { cmd = "timeline.seek", t = 1 }, "seek 1");
            Check("paused at 1: the item shows (1, 0, 0)", Near(HolderPos(h, f.P), 1, 0, 0), V3(HolderPos(h, f.P)));
            h.Ok(new { cmd = "obj.transform", key = f.P, pos = new[] { 5, 5, 5 } }, "move the item while paused");
            var st = h.Ok(new { cmd = "timeline.status" }, "status");
            Check("the edit suspends the track (suspended 1) and shows", (int)st["suspended"] == 1 && Near(HolderPos(h, f.P), 5, 5, 5), Short(h.LastText) + " " + V3(HolderPos(h, f.P)));
            string tid = h.Session.Model.Timeline.Tracks[0].Id;
            var ks = h.Ok(new { cmd = "timeline.key.set", track = tid, t = 1 }, "Key without v");
            Check("Key without v stores the shown (edited) value", (ks["v"] as JArray)?.Select(x => (double)x).SequenceEqual(new double[] { 5, 5, 5 }) == true, Short(h.LastText));
            h.Ok(new { cmd = "timeline.seek", t = 0.5 }, "seek 0.5");
            Check("the play head moved: the track shows again, between the keys (2.5, 2.5, 2.5)", Near(HolderPos(h, f.P), 2.5f, 2.5f, 2.5f) && (int)h.Ok(new { cmd = "timeline.status" }, "status")["suspended"] == 0, V3(HolderPos(h, f.P)));
            h.Ok(new { cmd = "timeline.play" }, "play");
            h.Ok(new { cmd = "obj.transform", key = f.P, pos = new[] { 7, 7, 7 } }, "move the item while playing");
            h.Frames(1);
            Check("while playing nothing is suspended (the Timeline shows)", f.Pb.Player.SuspendedCount == 0 && !Near(HolderPos(h, f.P), 7, 7, 7), V3(HolderPos(h, f.P)));
            h.Ok(new { cmd = "timeline.stop" }, "stop");
            h.Frames(1);
            Check("stop: the model shows (7, 7, 7)", Near(HolderPos(h, f.P), 7, 7, 7), V3(HolderPos(h, f.P)));
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ TA1

    /// <summary>The shown value of every track (stored form) at the play head, as one text.</summary>
    static List<double> ShownValues(PFix f)
    {
        var l = new List<double>();
        foreach (var t in f.S.Document.Tracks)
        {
            var v = f.S.ShownValue(t, out _);
            Flatten(v, l);
        }
        return l;
    }

    static void Flatten(JToken t, List<double> into)
    {
        if (t == null) { into.Add(double.NaN); return; }
        switch (t.Type)
        {
            case JTokenType.Integer:
            case JTokenType.Float: into.Add((double)t); break;
            case JTokenType.Boolean: into.Add((bool)t ? 1 : 0); break;
            case JTokenType.Array: foreach (var x in t) Flatten(x, into); break;
            case JTokenType.Object: foreach (var p in ((JObject)t).Properties()) Flatten(p.Value, into); break;
            case JTokenType.String: into.Add(((string)t).GetHashCode()); break;
            default: into.Add(-1e30); break;
        }
    }

    static bool Same(List<double> a, List<double> b, double tol, out string why)
    {
        why = null;
        if (a.Count != b.Count) { why = "count " + a.Count + " vs " + b.Count; return false; }
        for (int i = 0; i < a.Count; i++)
            if (!(Math.Abs(a[i] - b[i]) <= tol) && !(double.IsNaN(a[i]) && double.IsNaN(b[i]))) { why = "value " + i + ": " + a[i] + " vs " + b[i]; return false; }
        return true;
    }

    static void TA1()
    {
        var f = NewFix(0x7a1a0001UL);
        try
        {
            var h = f.H;
            var want = new Dictionary<string, (int tracks, int keys)> { ["camera_dof"] = (2, 10), ["walk"] = (5, 28), ["bones9"] = (10, 82) };
            foreach (var name in new[] { "camera_dof", "walk", "bones9" })
            {
                var b = BuildComposition(f, name, true);
                Check(name + ": built (" + want[name].tracks + " tracks, " + want[name].keys + " keys), playing", (int)b["tracks"] == want[name].tracks && (int)b["keys"] == want[name].keys && (string)b["state"] == "playing", Short(h.LastText));
                f.Pb.Perf.Reset();
                h.Frames(120);
                if (f.Pb.Perf.Stats(out double med, out double p95, out double max))
                    Measure(name + ": the playback's tick while playing, edit mode, 120 frames: median " + med.ToString("F1", CultureInfo.InvariantCulture) + " us, p95 " +
                        p95.ToString("F1", CultureInfo.InvariantCulture) + " us, max " + max.ToString("F1", CultureInfo.InvariantCulture) + " us");
                f.S.Pause();
                double d = f.S.Duration;
                var times = new[] { 0.13, d * 0.17, d * 0.43, d * 0.71, d - 0.01 };
                var before = new List<List<double>>();
                foreach (var t in times)
                {
                    f.S.Seek(t);
                    h.Frames(1);
                    before.Add(ShownValues(f));
                }
                string doc = TimelineJson(h);
                var sv = h.Ok(new { cmd = "scene.save", name = "ta1_" + name, overwrite = true, thumb = "placeholder", clock = Clock }, name + ": scene.save");
                h.Ok(new { cmd = "scene.new" }, name + ": scene.new");
                Check(name + ": scene.new leaves no Timeline", h.Session.Model.Timeline == null && f.S.State == TimelinePlayState.None);
                h.Ok(new { cmd = "scene.load", name = "ta1_" + name }, name + ": scene.load");
                h.Frames(3);
                Check(name + ": the loaded Timeline equals the saved one (stored form byte-equal)", TimelineJson(h) == doc);
                Check(name + ": loaded stopped (no autoplay)", f.S.State == TimelinePlayState.Stopped);
                bool all = true;
                string why = null;
                for (int i = 0; i < times.Length; i++)
                {
                    f.S.Seek(times[i]);
                    h.Frames(1);
                    if (!Same(before[i], ShownValues(f), 1e-6, out var w)) { all = false; why = "t " + times[i] + ": " + w; }
                }
                Check(name + ": the 5 seeks give the same values after the load (1e-6)", all, why);
                f.S.Stop();
            }
            // autoplay: a loaded scene whose Timeline has autoplay plays from 0
            h.Ok(new { cmd = "timeline.set", autoplay = true }, "autoplay on");
            h.Ok(new { cmd = "scene.save", name = "ta1_autoplay", overwrite = true, thumb = "placeholder", clock = Clock }, "save with autoplay");
            h.Ok(new { cmd = "scene.load", name = "ta1_autoplay" }, "load it");
            h.Frames(2);
            Check("autoplay: the loaded Timeline plays from 0", f.S.State == TimelinePlayState.Playing && f.S.Time > 0 && f.S.Time < 2, f.S.State + " at " + f.S.Time);
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ TA6

    static Dictionary<string, byte[]> Entries(string path)
    {
        var d = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using (var ms = new MemoryStream(File.ReadAllBytes(path)))
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
            foreach (var e in zip.Entries)
                using (var s = e.Open())
                using (var o = new MemoryStream())
                {
                    s.CopyTo(o);
                    d[e.FullName] = o.ToArray();
                }
        return d;
    }

    static void TA6()
    {
        var f = NewFix(0x7a6a0001UL);
        try
        {
            var h = f.H;
            BuildComposition(f, "walk", false);
            h.Frames(2);
            string p0 = (string)h.Ok(new { cmd = "scene.save", name = "ta6_before", overwrite = true, thumb = "placeholder", clock = Clock }, "save before playback")["path"];
            h.Ok(new { cmd = "timeline.play" }, "play");
            h.Frames(30);
            h.Ok(new { cmd = "scene.pause", paused = true }, "the global pause");
            h.Frames(2);
            Check("the Timeline plays under the global pause", f.S.State == TimelinePlayState.Playing && f.S.ScenePaused && f.Pb.Player.ValuesShown > 0);
            string pa = (string)h.Ok(new { cmd = "scene.save", name = "ta6_a", overwrite = true, thumb = "placeholder", clock = Clock }, "save A (playing)")["path"];
            h.Ok(new { cmd = "timeline.stop" }, "stop");
            h.Frames(2);
            string pB = (string)h.Ok(new { cmd = "scene.save", name = "ta6_b", overwrite = true, thumb = "placeholder", clock = Clock }, "save B (stopped)")["path"];
            var a = Entries(pa);
            var b = Entries(pB);
            var b0 = Entries(p0);
            Info("entries: " + string.Join(", ", a.Keys.OrderBy(x => x, StringComparer.Ordinal)));
            var diff = new List<string>();
            foreach (var n in a.Keys.Union(b.Keys))
            {
                if (n.IndexOf("thumb", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (!a.TryGetValue(n, out var x) || !b.TryGetValue(n, out var y) || !x.SequenceEqual(y)) diff.Add(n);
            }
            Check("TA6: save A (playing, paused scene) equals save B (after stop), entry for entry", diff.Count == 0, string.Join(", ", diff));
            // (the save before playback was taken while the scene ran: only the characters' own motion may differ from A)
            Info("A against the save before playback (scene running in between): scene.json " + (a.TryGetValue("scene.json", out var sa) && b0.TryGetValue("scene.json", out var s0) && sa.SequenceEqual(s0) ? "equal" : "differs (the characters' own motion)"));
            // the rule against the save before playback: the scene paused first (the characters' own motion, e.g. the eyes' look,
            // goes on while the scene runs), then the Timeline plays (held by the pause) and is paused by itself at another time
            h.Ok(new { cmd = "timeline.seek", t = 0 }, "seek 0");
            h.Ok(new { cmd = "timeline.stop" }, "stop");
            h.Frames(2);
            string q0 = (string)h.Ok(new { cmd = "scene.save", name = "ta6_before2", overwrite = true, thumb = "placeholder", clock = Clock }, "save before playback (scene paused)")["path"];
            h.Ok(new { cmd = "timeline.play" }, "play under the global pause");
            h.Frames(30);
            Check("the Timeline plays held by the global pause, its values shown", f.S.State == TimelinePlayState.Playing && f.Pb.Player.ValuesShown > 0);
            string q1 = (string)h.Ok(new { cmd = "scene.save", name = "ta6_a1", overwrite = true, thumb = "placeholder", clock = Clock }, "save A1 (playing)")["path"];
            h.Ok(new { cmd = "timeline.seek", t = 6 }, "the Timeline paused at 6");
            h.Frames(3);
            string q2 = (string)h.Ok(new { cmd = "scene.save", name = "ta6_a2", overwrite = true, thumb = "placeholder", clock = Clock }, "save A2 (Timeline paused at 6)")["path"];
            var e0 = Entries(q0)["scene.json"];
            Check("TA6: the scene.json saved while it plays equals the one saved before playback", Entries(q1)["scene.json"].SequenceEqual(e0));
            Check("TA6: the scene.json saved while it is paused at 6 equals the one saved before playback", Entries(q2)["scene.json"].SequenceEqual(e0));
            h.Ok(new { cmd = "timeline.stop" }, "stop");
            h.Ok(new { cmd = "scene.pause", paused = false }, "resume the scene");
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ TA7

    static void TA7()
    {
        var f = NewFix(0x7a7a0001UL);
        try
        {
            var h = f.H;
            var c = Composition(f, "bones9");
            var tracks = new JArray(((JArray)c["tracks"]).Where(t => (string)t["type"] != "camera"));
            tracks.Add(JObject.FromObject(new { type = "pos", key = f.P, ease = "linear", keys = new object[] { new { t = 0, v = new[] { 0, 0, 0 } }, new { t = 10, v = new[] { 1, 2, 3 } } } }));
            tracks.Add(JObject.FromObject(new { type = "rot", key = f.P, ease = "linear", keys = new object[] { new { t = 0, v = new[] { 0, 0, 0 } }, new { t = 10, v = new[] { 30, 60, 10 } } } }));
            h.Ok(new JObject { ["cmd"] = "timeline.build", ["duration"] = c["duration"], ["tracks"] = tracks, ["play"] = false }.ToString(Formatting.None), "bones9 (no camera) + the item's pos and rot");
            string sceneBefore = (string)h.Ok(new { cmd = "scene.doc", capture = false }, "scene.doc before")["scene"];
            h.Ok(new { cmd = "timeline.seek", t = 3.3 }, "seek 3.3");
            h.Frames(2);
            var bones = new List<(string chara, string bone, Quaternion q)>();
            foreach (var t in ((JArray)c["tracks"]).Where(t => (string)t["type"] == "rot" && t["bone"] != null))
            {
                string chara = (string)t["key"], bone = (string)t["bone"];
                var tr = Bone(h, chara, bone);
                bones.Add((chara, bone, tr ? tr.localRotation : new Quaternion(float.NaN, 0, 0, 0)));
            }
            var holder = h.Rt.Find(f.P).Holder;
            Vector3 pos = holder.localPosition;
            Quaternion rot = holder.localRotation;
            var labels0 = UndoLabels(h);
            var r = h.Ok(new { cmd = "timeline.stop", bake = true }, "timeline.stop {bake: true}");
            Check("bake: baked 11 (9 bones, pos, rot), stopped", (int)r["baked"] == 11 && (string)r["state"] == "stopped", Short(h.LastText));
            var labels1 = UndoLabels(h);
            Check("bake: one history entry «Bake Timeline pose»", labels1.Count == labels0.Count + 1 && labels1[0] == "Bake Timeline pose", string.Join(", ", labels1.Take(3)));
            h.Frames(2);
            Check("bake: the item's holder shows the baked pose (1e-5, 0.01 deg)", (holder.localPosition - pos).magnitude <= 1e-5f && Quaternion.Angle(holder.localRotation, rot) <= 0.01f,
                V3(holder.localPosition) + " vs " + V3(pos) + ", " + Quaternion.Angle(holder.localRotation, rot) + " deg");
            double worst = 0;
            string worstName = null;
            foreach (var (chara, bone, q) in bones)
            {
                var tr = Bone(h, chara, bone);
                double a = tr ? Quaternion.Angle(tr.localRotation, q) : 999;
                if (a > worst) { worst = a; worstName = bone; }
            }
            Check("bake: the FK bones show the baked rotations (0.01 deg)", worst <= 0.01, worst + " deg at " + worstName);
            Check("bake: no Timeline value is shown after the stop", f.Pb.Player.ValuesShown == 0);
            h.Ok(new { cmd = "undo" }, "undo the bake");
            string sceneAfter = (string)h.Ok(new { cmd = "scene.doc", capture = false }, "scene.doc after the undo")["scene"];
            Check("undo restores the scene.json of before", sceneAfter == sceneBefore, sceneAfter?.Length + " vs " + sceneBefore?.Length + " chars");
            Check("the Timeline stays stopped after the undo", f.S.State == TimelinePlayState.Stopped);
        }
        finally { f.Dispose(); }
    }
}
