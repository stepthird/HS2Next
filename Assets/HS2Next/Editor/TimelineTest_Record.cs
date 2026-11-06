// TimelineTest's frame recording part (Phase 6.2 package W): Play_W_Record, a part of TimelineTest.RunPlay (play mode,
// graphics, the app with the UI). It runs on the main thread as a coroutine; the app is reached through its bridge in process
// (AppCore.Bridge.Execute) and, for TW9, through FrameRecorder itself. The recordings go under the run's bridge output root
// (record/tw<n>, Spike/out/timeline/play_out/record). PNG files are judged by their size and their header bytes read as
// numbers (signature, IHDR width / height, bit depth, colour type) - never decoded or shown.
//   TW1  bones9 (BR-Chan and HERO107, FK on with the Body and Neck groups): record.start {320 x 180, 30 fps, 60 frames,
//        timeline}: the folder holds frame_000001.png ... frame_000060.png and record.json only; every header reads 320 x 180,
//        8-bit RGB; stoppedBy "frames"; during the recording record.status says recording, a second record.start answers
//        busy, and a screenshot and a capture succeed (the frames of a recording do not block screenshots).
//   TW2  record.json of TW1: frames 60, written 60, dt = (double)(float)(1/30); consecutive sceneTimes differ by dt within 1e-9;
//        timelineTimes[k] = (k + 1) dt within 1e-9.
//   TW3  the capture events of TW1 (the Core's events, observed in process): 60 Starting, Rendered and Finished of kind Record,
//        origin Command; the Starting and the Finished paths are the files in order; per frame Starting, Rendered at once,
//        Finished later; every Finished ok with the file's size; the screenshot's events are kind Screenshot.
//   TW4  a recording until record.stop with the global pause for 20 frames in the middle: no frame is written while paused
//        (frames unchanged, held grows), scene.step answers busy while recording, the sceneTimes spacing stays dt, written =
//        frames after the deferred record.stop (at most 4 pending); record.stop again answers unavailable; scene.step works
//        again afterwards.
//   TW9  a recording without the bridge: FrameRecorder.Start (origin Ui) while the app's Bridge reads null (set aside for the
//        call, restored at once), bones9 with timeline, 30 frames: the recorder's own late point, 30 files 160 x 90, the
//        sceneTimes spacing dt and timelineTimes[k] = (k + 1) dt as with the bridge, the capture events of origin Ui.
//   TW5  camera_dof (10 s, looping): record.start {timeline, fps 10, 160 x 90} stops by itself with stoppedBy "pass" after 100
//        frames; the refusals: fixed false (unsupported), a side over 8192, fps 0 (bad_args), a folder outside the output root
//        (io_denied), timeline on a scene without a Timeline (unavailable).
// The part reports the recorder's maxPending, waits and syncFrames of each recording as measurements.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HS2Next.Api.Plugins;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.Screenshots;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class TimelineTest
{
    sealed class RecEvent
    {
        public char What;
        public CaptureKind Kind;
        public CaptureOrigin Origin;
        public string Path;
        public bool Ok;
        public long Bytes;
    }

    static readonly List<RecEvent> recEvents = new List<RecEvent>();
    static JObject recLast;
    static string recLastText;
    static JObject recTw1Json;
    static string recTw1Folder;

    static void RecStarting(CaptureInfo i) => recEvents.Add(new RecEvent { What = 'S', Kind = i.Kind, Origin = i.Origin, Path = i.Path });
    static void RecRendered(CaptureInfo i) => recEvents.Add(new RecEvent { What = 'R', Kind = i.Kind, Origin = i.Origin, Path = i.Path });
    static void RecFinished(CaptureResultInfo r) =>
        recEvents.Add(new RecEvent { What = 'F', Kind = r.Capture.Kind, Origin = r.Capture.Origin, Path = r.Capture.Path, Ok = r.Ok, Bytes = r.Bytes });

    static IEnumerator Play_W_Record()
    {
        var app = AppCore.Current;
        current = "W_Record";
        if (!Check("the app runs with its bridge and runtime", app != null && app.Bridge != null && app.Runtime != null)) yield break;
        CaptureEvents.Starting += RecStarting;
        CaptureEvents.Rendered += RecRendered;
        CaptureEvents.Finished += RecFinished;
        try
        {
            if (RecOpen("TW1", "a recording of bones9: the files and their headers; busy, screenshot and capture meanwhile")) yield return RecTw1(app);
            if (RecOpen("TW2", "record.json of TW1: the time step, sceneTimes and timelineTimes")) RecTw2();
            if (RecOpen("TW3", "the capture events of TW1 (kind Record, in frame order)")) RecTw3();
            if (RecOpen("TW4", "the global pause holds a recording; scene.step busy; the deferred record.stop")) yield return RecTw4(app);
            if (RecOpen("TW9", "a recording without the bridge (the recorder's own late point)")) yield return RecTw9(app);
            if (RecOpen("TW5", "one pass of a Timeline; the refusals")) yield return RecTw5(app);
        }
        finally
        {
            CaptureEvents.Starting -= RecStarting;
            CaptureEvents.Rendered -= RecRendered;
            CaptureEvents.Finished -= RecFinished;
            if (FrameRecorder.IsRecording) FrameRecorder.Current.Stop();
        }
    }

    /// <summary>Opens a group of this part (false when -only leaves it out).</summary>
    static bool RecOpen(string id, string title)
    {
        if (!Want(id)) return false;
        current = id;
        rep.AppendLine();
        rep.AppendLine($"== {id} {title}");
        return true;
    }

    // ------------------------------------------------------------ TW1-TW3

    static IEnumerator RecTw1(AppCore app)
    {
        recTw1Json = null;
        recTw1Folder = null;
        yield return RecBuild(app, "bones9");
        if (!RecBuilt) yield break;
        string folder = RecFolder(app, "tw1");
        recEvents.Clear();
        yield return RecCall(app, new { cmd = "record.start", path = "record/tw1", width = 320, height = 180, fps = 30, frames = 60, timeline = true, overwrite = true });
        if (!Check("record.start {320 x 180, 30 fps, 60 frames, timeline}", RecOk, Short(recLastText))) yield break;
        recTw1Folder = (string)recLast["path"];
        Check("the reply: the folder under the output root, pattern, render size, dt",
            RecSame(recTw1Folder, folder) && (string)recLast["pattern"] == "frame_%06d.png" && (int)recLast["renderWidth"] == 320 &&
            (int)recLast["renderHeight"] == 180 && (double)recLast["dt"] == (double)(1f / 30f), Short(recLastText));
        yield return RecCall(app, new { cmd = "record.status" });
        Check("record.status while recording: recording true", RecOk && (bool)recLast["recording"], Short(recLastText));
        yield return RecCall(app, new { cmd = "record.start", path = "record/tw1b", width = 320, height = 180 });
        Check("a second record.start answers busy", !RecOk && (string)recLast["code"] == "busy", Short(recLastText));
        yield return RecCall(app, new { cmd = "screenshot", mode = "custom", width = 160, height = 90, supersample = 1, name = "tw1_during_recording" });
        Check("a screenshot during the recording succeeds", RecOk, Short(recLastText));
        yield return RecCall(app, new { cmd = "capture", width = 64, height = 36, name = "tw1_during_recording" });
        Check("a capture during the recording succeeds", RecOk, Short(recLastText));
        yield return RecWaitDone(120);
        var rec = FrameRecorder.Current;
        if (!Check("the recording finished by itself", rec != null && rec.IsDone, rec == null ? "none" : rec.Frames + " frames, pending " + rec.Pending)) yield break;
        Check("stoppedBy frames, 60 frames, 60 written, no error", rec.StoppedBy == "frames" && rec.Frames == 60 && rec.Written == 60 && rec.Error == null,
            rec.StoppedBy + ", " + rec.Frames + " / " + rec.Written + ", " + (rec.Error ?? "no error"));
        RecMeasure("TW1", rec);
        RecFiles(folder, 60, 320, 180);
        try { recTw1Json = JObject.Parse(File.ReadAllText(Path.Combine(folder, "record.json"))); }
        catch (Exception e) { Check("record.json parses", false, e.Message); }
    }

    static void RecTw2()
    {
        var j = recTw1Json;
        if (!Check("record.json of TW1 is there", j != null)) return;
        double dt = (double)(1f / 30f);
        Check("members: format, fps 30, dt (float 1/30), frames 60, written 60, sizes, pattern, stoppedBy frames",
            (string)j["format"] == "hs2next.record/1" && (int)j["fps"] == 30 && (double)j["dt"] == dt && (int)j["frames"] == 60 && (int)j["written"] == 60 &&
            (int)j["width"] == 320 && (int)j["height"] == 180 && (int)j["supersample"] == 1 && (string)j["pattern"] == "frame_%06d.png" && (string)j["stoppedBy"] == "frames",
            "dt " + ((double?)j["dt"])?.ToString("R", CultureInfo.InvariantCulture) + ", frames " + j["frames"] + ", written " + j["written"]);
        RecTimes(j, 60, dt, true);
    }

    static void RecTw3()
    {
        if (!Check("TW1 recorded", recTw1Folder != null)) return;
        var files = Enumerable.Range(1, 60).Select(i => RecNorm(Path.Combine(recTw1Folder, "frame_" + i.ToString("D6", CultureInfo.InvariantCulture) + ".png"))).ToList();
        var rec = recEvents.Where(e => e.Kind == CaptureKind.Record).ToList();
        int s = rec.Count(e => e.What == 'S'), r = rec.Count(e => e.What == 'R'), f = rec.Count(e => e.What == 'F');
        Check("60 Starting, 60 Rendered, 60 Finished of kind Record", s == 60 && r == 60 && f == 60, s + " / " + r + " / " + f);
        Check("origin Command (record.start through the bridge)", rec.Count > 0 && rec.All(e => e.Origin == CaptureOrigin.Command), string.Join(",", rec.Select(e => e.Origin).Distinct()));
        var sp = rec.Where(e => e.What == 'S').Select(e => RecNorm(e.Path)).ToList();
        var fp = rec.Where(e => e.What == 'F').Select(e => RecNorm(e.Path)).ToList();
        Check("the Starting and the Finished paths are the files in order", sp.SequenceEqual(files) && fp.SequenceEqual(files),
            (sp.FirstOrDefault() ?? "none") + " ... " + (fp.LastOrDefault() ?? "none"));
        bool order = true;
        for (int i = 0; i < files.Count && order; i++)
        {
            int si = rec.FindIndex(e => e.What == 'S' && RecNorm(e.Path) == files[i]);
            int ri = rec.FindIndex(e => e.What == 'R' && RecNorm(e.Path) == files[i]);
            int fi = rec.FindIndex(e => e.What == 'F' && RecNorm(e.Path) == files[i]);
            order = si >= 0 && ri == si + 1 && fi > ri;
        }
        Check("per frame: Starting, Rendered at once, Finished later", order);
        int bad = rec.Where(e => e.What == 'F').Count(e => !e.Ok || !File.Exists(e.Path) || new FileInfo(e.Path).Length != e.Bytes);
        Check("every Finished is ok and carries the file's size", bad == 0, bad + " differ");
        Info("TW3 other capture events meanwhile: screenshot " + recEvents.Count(e => e.Kind == CaptureKind.Screenshot && e.What == 'F') +
             ", capture " + recEvents.Count(e => e.Kind == CaptureKind.Capture && e.What == 'F') + " (Finished)");
    }

    // ------------------------------------------------------------ TW4

    static IEnumerator RecTw4(AppCore app)
    {
        yield return RecBuild(app, "bones9");
        if (!RecBuilt) yield break;
        string folder = RecFolder(app, "tw4");
        yield return RecCall(app, new { cmd = "record.start", path = "record/tw4", width = 160, height = 90, fps = 30, overwrite = true });
        if (!Check("record.start until record.stop (160 x 90, 30 fps)", RecOk, Short(recLastText))) yield break;
        yield return PlayModeHarness.WaitFrames(25);
        yield return RecCall(app, new { cmd = "scene.pause", paused = true });
        Check("scene.pause {paused: true} during the recording", RecOk, Short(recLastText));
        yield return PlayModeHarness.WaitFrames(3);
        var rec = FrameRecorder.Current;
        int f0 = rec.Frames, h0 = rec.Held;
        yield return RecCall(app, new { cmd = "scene.step", n = 1 });
        Check("scene.step answers busy while recording", !RecOk && (string)recLast["code"] == "busy", Short(recLastText));
        yield return PlayModeHarness.WaitFrames(20);
        Check("no frame is written while paused (frames unchanged over 20 frames, held grows by 20 or more)", rec.Frames == f0 && rec.Held - h0 >= 20,
            "frames " + f0 + " -> " + rec.Frames + ", held " + h0 + " -> " + rec.Held);
        yield return RecCall(app, new { cmd = "scene.pause", paused = false });
        yield return PlayModeHarness.WaitFrames(25);
        yield return RecCall(app, new { cmd = "record.stop" }, 120);
        if (Check("record.stop answers after the pending frames", RecOk, Short(recLastText)))
        {
            Check("written = frames, stoppedBy stop, at most 4 pending, nothing pending now",
                (int)recLast["written"] == (int)recLast["frames"] && (string)recLast["stoppedBy"] == "stop" && (int)recLast["maxPending"] <= 4 && rec.Pending == 0,
                "frames " + recLast["frames"] + ", written " + recLast["written"] + ", maxPending " + recLast["maxPending"] + ", held " + recLast["held"]);
            RecMeasure("TW4", rec);
            try
            {
                var j = JObject.Parse(File.ReadAllText(Path.Combine(folder, "record.json")));
                RecTimes(j, (int)recLast["frames"], (double)(1f / 30f), false);
            }
            catch (Exception e) { Check("record.json parses", false, e.Message); }
            int pngs = Directory.GetFiles(folder, "frame_*.png").Length;
            Check("the folder holds the written frames", pngs == (int)recLast["written"], pngs + " files");
        }
        yield return RecCall(app, new { cmd = "record.stop" });
        Check("record.stop when not recording answers unavailable", !RecOk && (string)recLast["code"] == "unavailable", Short(recLastText));
        yield return RecCall(app, new { cmd = "scene.pause", paused = true });
        yield return RecCall(app, new { cmd = "scene.step", n = 1 });
        Check("scene.step works again after the recording", RecOk, Short(recLastText));
        yield return RecCall(app, new { cmd = "scene.pause", paused = false });
    }

    // ------------------------------------------------------------ TW9: without the bridge

    static IEnumerator RecTw9(AppCore app)
    {
        yield return RecBuild(app, "bones9");
        if (!RecBuilt) yield break;
        string folder = RecFolder(app, "tw9");
        recEvents.Clear();
        var prop = typeof(AppCore).GetProperty("Bridge");
        var bridge = app.Bridge;
        FrameRecorder rec = null;
        string startError = null;
        try
        {
            prop.SetValue(app, null);
            if (app.Bridge != null) startError = "the app's Bridge could not be set aside";
            else
                rec = FrameRecorder.Start(app, new RecordOptions
                {
                    Folder = FrameRecorder.ResolveFolder(app, app.Paths.OutputDir, "record/tw9", true),
                    Width = 160, Height = 90, Fps = 30, Frames = 30, Timeline = true, Overwrite = true, Origin = CaptureOrigin.Ui,
                });
        }
        catch (Exception e) { startError = e.GetType().Name + ": " + e.Message; }
        finally { prop.SetValue(app, bridge); }
        if (!Check("FrameRecorder.Start while the app has no bridge (origin Ui, 160 x 90, 30 frames, timeline)", rec != null, startError ?? "")) yield break;
        Check("the recorder takes its own late point", rec.OwnLatePoint);
        yield return RecWaitDone(120);
        Check("it finishes by itself: stoppedBy frames, 30 frames written", rec.IsDone && rec.StoppedBy == "frames" && rec.Frames == 30 && rec.Written == 30 && rec.Error == null,
            rec.StoppedBy + ", " + rec.Frames + " / " + rec.Written + ", " + (rec.Error ?? "no error"));
        RecMeasure("TW9", rec);
        RecFiles(folder, 30, 160, 90);
        try { RecTimes(JObject.Parse(File.ReadAllText(Path.Combine(folder, "record.json"))), 30, (double)(1f / 30f), true); }
        catch (Exception e) { Check("record.json parses", false, e.Message); }
        var ev = recEvents.Where(e => e.Kind == CaptureKind.Record).ToList();
        Check("30 Starting / Rendered / Finished of kind Record, origin Ui", ev.Count(e => e.What == 'S') == 30 && ev.Count(e => e.What == 'R') == 30 &&
            ev.Count(e => e.What == 'F') == 30 && ev.All(e => e.Origin == CaptureOrigin.Ui), ev.Count + " events");
    }

    // ------------------------------------------------------------ TW5

    static IEnumerator RecTw5(AppCore app)
    {
        yield return RecBuild(app, "camera_dof");
        if (!RecBuilt) yield break;
        string folder = RecFolder(app, "tw5");
        yield return RecCall(app, new { cmd = "record.start", path = "record/tw5", width = 160, height = 90, fps = 10, timeline = true, overwrite = true });
        if (Check("record.start {timeline, fps 10, 160 x 90} on camera_dof", RecOk, Short(recLastText)))
        {
            yield return RecWaitDone(180);
            var rec = FrameRecorder.Current;
            Check("it stops by itself after one pass: stoppedBy pass, 100 frames written",
                rec != null && rec.IsDone && rec.StoppedBy == "pass" && rec.Frames == 100 && rec.Written == 100,
                rec == null ? "none" : rec.StoppedBy + ", " + rec.Frames + " / " + rec.Written);
            if (rec != null) RecMeasure("TW5", rec);
            Check("100 files", Directory.Exists(folder) && Directory.GetFiles(folder, "frame_*.png").Length == 100);
        }
        yield return RecCall(app, new { cmd = "record.start", path = "record/tw5x", @fixed = false });
        Check("fixed: false answers unsupported", !RecOk && (string)recLast["code"] == "unsupported", Short(recLastText));
        yield return RecCall(app, new { cmd = "record.start", path = "record/tw5x", width = 9000, height = 90 });
        Check("a side over 8192 answers bad_args", !RecOk && (string)recLast["code"] == "bad_args", Short(recLastText));
        yield return RecCall(app, new { cmd = "record.start", path = "record/tw5x", fps = 0 });
        Check("fps 0 answers bad_args", !RecOk && (string)recLast["code"] == "bad_args", Short(recLastText));
        yield return RecCall(app, new { cmd = "record.start", path = "../outside", width = 160, height = 90 });
        Check("a folder outside the output root answers io_denied", !RecOk && (string)recLast["code"] == "io_denied", Short(recLastText));
        yield return RecCall(app, new { cmd = "timeline.clear" });
        Check("timeline.clear", RecOk, Short(recLastText));
        yield return RecCall(app, new { cmd = "record.start", path = "record/tw5x", timeline = true });
        Check("timeline: true without a Timeline answers unavailable", !RecOk && (string)recLast["code"] == "unavailable", Short(recLastText));
        Check("no recording runs after the refusals", !FrameRecorder.IsRecording);
    }

    // ------------------------------------------------------------ helpers

    static bool RecOk => recLast != null && recLast.Value<bool?>("ok") == true;

    /// <summary>One request through the app's bridge in process; the reply in recLast (frames pass while it waits).</summary>
    static IEnumerator RecCall(AppCore app, object req, double timeoutSec = 300)
    {
        string json = req is string s ? s : JsonConvert.SerializeObject(req);
        var t = app.Bridge.Execute(json);
        float until = Time.realtimeSinceStartup + (float)timeoutSec;
        while (!t.IsCompleted && Time.realtimeSinceStartup < until) yield return null;
        recLastText = t.IsCompleted ? (t.IsFaulted ? "{\"ok\":false,\"error\":\"faulted\"}" : t.Result) : "{\"ok\":false,\"code\":\"timeout\"}";
        try { recLast = JObject.Parse(recLastText); }
        catch (Exception) { recLast = new JObject { ["ok"] = false, ["error"] = "unparsable: " + recLastText }; }
    }

    static bool RecBuilt;

    /// <summary>scene.new, the composition's characters (FK on with Body and Neck for bones9), timeline.build {play: false}.</summary>
    static IEnumerator RecBuild(AppCore app, string name)
    {
        RecBuilt = false;
        string json = File.ReadAllText(Path.Combine(ContentSettings.ProjectRoot, "Tests", "Timeline", "compositions", name + ".json"));
        yield return RecCall(app, new { cmd = "scene.new" });
        if (!Check("scene.new (" + name + ")", RecOk, Short(recLastText))) yield break;
        foreach (var (ph, rel, sex) in new[] { ("$a", femaleRel, "female"), ("$b", maleRel, "male") })
        {
            if (!json.Contains("\"" + ph + "\"")) continue;
            yield return RecCall(app, new { cmd = "chara.load", path = rel, sex });
            if (!Check("chara.load " + sex + " visual target", RecOk, Short(recLastText))) yield break;
            string key = (string)recLast["added"]?[0]?["key"];
            json = json.Replace("\"" + ph + "\"", JsonConvert.ToString(key));
            if (name != "bones9") continue;
            yield return RecCall(app, new { cmd = "fk.enable", key, on = true });
            yield return RecCall(app, new { cmd = "fk.enable", key, groups = new[] { "Body", "Neck" }, on = true });
            if (!Check("FK on with the Body and Neck groups (" + sex + ")", RecOk, Short(recLastText))) yield break;
        }
        var c = JObject.Parse(json);
        var req = new JObject { ["cmd"] = "timeline.build", ["duration"] = c["duration"], ["tracks"] = c["tracks"], ["play"] = false };
        if (c["loop"] != null) req["loop"] = c["loop"];
        yield return RecCall(app, req.ToString(Formatting.None));
        if (!Check("timeline.build " + name + " {play: false}", RecOk, Short(recLastText))) yield break;
        yield return PlayModeHarness.WaitFrames(3);
        RecBuilt = true;
    }

    /// <summary>&lt;output root&gt;/record/&lt;name&gt;, emptied of an earlier run's frame_*.png and record.json (not recursive).</summary>
    static string RecFolder(AppCore app, string name)
    {
        string folder = Path.Combine(app.Paths.OutputDir, "record", name);
        if (Directory.Exists(folder))
        {
            foreach (var f in Directory.GetFiles(folder, "frame_*.png")) File.Delete(f);
            string j = Path.Combine(folder, "record.json");
            if (File.Exists(j)) File.Delete(j);
        }
        return folder;
    }

    /// <summary>Frames pass until the current recording is done (or the time is up).</summary>
    static IEnumerator RecWaitDone(double timeoutSec)
    {
        float until = Time.realtimeSinceStartup + (float)timeoutSec;
        while (Time.realtimeSinceStartup < until)
        {
            var r = FrameRecorder.Current;
            if (r == null || r.IsDone) yield break;
            yield return null;
        }
    }

    /// <summary>The folder holds frame_000001.png ... frame_&lt;n&gt;.png and record.json only; every PNG header w x h, 8-bit RGB.</summary>
    static void RecFiles(string folder, int n, int w, int h)
    {
        var names = Directory.Exists(folder) ? Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToList() : new List<string>();
        var want = Enumerable.Range(1, n).Select(i => "frame_" + i.ToString("D6", CultureInfo.InvariantCulture) + ".png").Concat(new[] { "record.json" })
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        Check("the folder holds frame_000001.png ... frame_" + n.ToString("D6", CultureInfo.InvariantCulture) + ".png and record.json only", names.SequenceEqual(want),
            names.Count + " files");
        int bad = 0;
        string first = null;
        long bytes = 0;
        foreach (var f in names.Where(x => x.EndsWith(".png", StringComparison.Ordinal)))
        {
            string p = Path.Combine(folder, f);
            bytes += new FileInfo(p).Length;
            string why = RecPngHeader(p, w, h);
            if (why != null) { bad++; first ??= f + ": " + why; }
        }
        Check("every PNG header reads " + w + " x " + h + ", 8-bit RGB (header bytes as numbers)", bad == 0, bad + " bad" + (first != null ? "; " + first : "") + "; " + bytes + " bytes in all");
    }

    /// <summary>record.json's sceneTimes spaced by dt (1e-9) and, with a Timeline, timelineTimes[k] = (k + 1) dt (1e-9).</summary>
    static void RecTimes(JObject j, int n, double dt, bool timeline)
    {
        var st = (j["sceneTimes"] as JArray)?.Select(x => (double)x).ToList();
        if (!Check("sceneTimes holds " + n + " values", st != null && st.Count == n, (st?.Count ?? -1).ToString())) return;
        double worst = 0;
        for (int k = 1; k < st.Count; k++) worst = Math.Max(worst, Math.Abs(st[k] - st[k - 1] - dt));
        Check("consecutive sceneTimes differ by dt (|delta - dt| <= 1e-9)", worst <= 1e-9, "worst " + worst.ToString("R", CultureInfo.InvariantCulture));
        if (!timeline) return;
        var tt = (j["timelineTimes"] as JArray)?.Select(x => x.Type == JTokenType.Null ? double.NaN : (double)x).ToList();
        if (!Check("timelineTimes holds " + n + " values", tt != null && tt.Count == n, (tt?.Count ?? -1).ToString())) return;
        double wt = 0;
        for (int k = 0; k < tt.Count; k++) wt = Math.Max(wt, double.IsNaN(tt[k]) ? double.PositiveInfinity : Math.Abs(tt[k] - (k + 1) * dt));
        Check("timelineTimes[k] = (k + 1) dt within 1e-9", wt <= 1e-9, "worst " + wt.ToString("R", CultureInfo.InvariantCulture) + "; first " + tt[0].ToString("R", CultureInfo.InvariantCulture));
    }

    static void RecMeasure(string id, FrameRecorder r) =>
        Measure($"{id} recording: {r.Frames} frames {r.Width} x {r.Height} at {r.Fps} fps in {r.Milliseconds:F0} ms ({r.Milliseconds / Math.Max(1, r.Frames):F1} ms per frame); " +
                $"maxPending {r.MaxPending}, waits {r.Waits} ({r.WaitMs:F0} ms), syncFrames {r.SyncFrames}, held {r.Held}, retries {r.Retries}, own late point {r.OwnLatePoint}");

    /// <summary>Null when the file starts with the PNG signature and an IHDR of w x h, bit depth 8, colour type 2; else what
    /// differs. Reads the first 26 bytes as numbers only.</summary>
    static string RecPngHeader(string path, int w, int h)
    {
        var b = new byte[26];
        using (var fs = File.OpenRead(path))
            if (fs.Read(b, 0, b.Length) != b.Length) return "shorter than a PNG header";
        byte[] sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        for (int i = 0; i < 8; i++) if (b[i] != sig[i]) return "no PNG signature";
        if (b[12] != (byte)'I' || b[13] != (byte)'H' || b[14] != (byte)'D' || b[15] != (byte)'R') return "no IHDR first";
        int fw = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
        int fh = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
        if (fw != w || fh != h) return fw + " x " + fh;
        if (b[24] != 8 || b[25] != 2) return "bit depth " + b[24] + ", colour type " + b[25];
        return null;
    }

    static string RecNorm(string p) => p == null ? null : Path.GetFullPath(p).TrimEnd('\\', '/').ToLowerInvariant();

    static bool RecSame(string a, string b) => a != null && b != null && RecNorm(a) == RecNorm(b);
}
