// ScriptTest, package P's play-mode part (Phase 6.9), a part of ScriptTest.RunPlay: the app started on the row's port with the
// SFW targets BR-Chan and HERO107 in the roles.
//   JP9  failures: a step that fails is reported with its phase, index, command, code and message and the play goes on; strict stops before
//        the timeline is loaded and answers the step's code with detail {phase, index}.
//   JR3  a recorded play: script play with record {320 x 180, 30 fps} of a 2 s script: frames = round(duration x fps), written = frames,
//        stoppedBy "pass", the Timeline paused after the pass (no timed command after the last frame), record.json's timelineTimes k x dt.
//        The frames are counted, never opened, and deleted at the end.
//   JD3  same playback after save and load in the Editor: a script with tracks, timed commands, a stain and a body played with a
//        recorded pass, the scene saved and loaded, played again; digests at k x duration / 8 (k 1-7) compared with the bars. The
//        numbers are printed; they are not acceptance numbers (the player's JD1 is).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HS2Next.Api.Bridge;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Scripts;
using HS2Next.Scenes.Timeline;
using HS2Next.Screenshots;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class ScriptTest
{
    static IEnumerator Play_P_Player()
    {
        if (!WantAny("JP9", "JR3", "JD3", "JE2s", "JP8s")) yield break;
        yield return PStartApp();
        var app = AppCore.Current;
        if (app == null || app.Stage != AppStage.Ready) yield break;
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { current = "play"; Check("a graphics device", false, "the play row needs one"); yield break; }
        // the two targets
        yield return PCall(app, new { cmd = "scene.new" });
        yield return PCall(app, new { cmd = "chara.load", path = femaleRel, sex = "female" });
        string a = Key(pLast);
        yield return PCall(app, new { cmd = "chara.load", path = maleRel, sex = "male" });
        string b = Key(pLast);
        if (a == null || b == null) { current = "play"; Check("the targets loaded", false, Short(pLastText)); yield break; }
        yield return PCall(app, new { cmd = "obj.transform", key = b, pos = new[] { 5, 0, 0 }, rot = new[] { 0, -90, 0 } });
        yield return PCall(app, new { cmd = "camera.set", target = new[] { 2.5, 10, 0 }, rot = new[] { 5, 180, 0 }, dist = 35, fov = 30 });
        yield return PFrames(5);
        if (POpen("JP9", "a failed step is reported, the play goes on; strict stops before the load")) yield return PJP9(app, a, b);
        if (POpen("JR3", "a recorded play: one pass, every frame written, the Timeline paused after it")) yield return PJR3(app, a, b);
        if (POpen("JE2s", "a stain made in the preparation: the undo of «Prepare script» removes it")) yield return PJE2s(app, a, b);
        if (POpen("JP8s", "stop and the cast's stain: dried with reset, kept with reset: false")) yield return PJP8s(app, a, b);
        if (POpen("JD3", "same playback after save and load (Editor; printed, not acceptance)")) yield return PJD3(app, a, b);
        yield return PStopApp();
    }

    /// <summary>Waits until a play is done (real frames, bounded).</summary>
    static IEnumerator PWaitRun(ScriptPlayRun run, double timeoutSec = 120)
    {
        float until = Time.realtimeSinceStartup + (float)timeoutSec;
        while (run != null && !run.IsDone && Time.realtimeSinceStartup < until) yield return null;
    }

    static void PAddScripts(AppCore app, params SceneScript[] scripts)
    {
        var l = new SceneScriptList();
        if (app.Session.Model.Scripts != null) l.Scripts.AddRange(app.Session.Model.Scripts.Scripts);
        l.Scripts.AddRange(scripts);
        var r = app.Session.Commands.Execute(ScriptEditCommand.ReplaceScripts(l, ScriptEditCommand.LabelAdd));
        if (r.Status == CommandStatus.Error) throw new InvalidOperationException("the scripts were not added: " + r.Code + ": " + r.Message);
        app.Runtime.ApplyNow();
    }

    // ------------------------------------------------------------ JP9

    static IEnumerator PJP9(AppCore app, string a, string b)
    {
        var s = NewScript("failures", CharaRole("a", "female"), CharaRole("b", "male"));
        s.Cast["a"] = a;
        s.Cast["b"] = b;
        s.Prepare.Add(J("{\"cmd\":\"obj.transform\",\"key\":\"$a\",\"rot\":[0,10,0]}"));
        s.Prepare.Add(J("{\"cmd\":\"fk.enable\",\"key\":\"$a\",\"groups\":[\"NoSuchGroup\"],\"on\":true}"));   // the step that fails: bad_args
        s.Prepare.Add(J("{\"cmd\":\"obj.transform\",\"key\":\"$a\",\"rot\":[0,20,0]}"));
        s.Setup.Add(J("{\"cmd\":\"obj.transform\",\"key\":\"0000000000000000000000000000dead\",\"pos\":[0,0,0]}"));   // fails: not_found
        s.Setup.Add(J("{\"cmd\":\"obj.transform\",\"key\":\"$b\",\"pos\":[5,0,0]}"));
        s.Setup.Add(J("{\"cmd\":\"screenshot\"}"));   // fails unsent: a command that answers later
        s.Timeline = ScriptTimeline(2f, true, Track("pos", "$a", TimelineKey.OfVector(0f, new Vec3(0, 0, 0))));
        PAddScripts(app, s);
        ScriptPlayRun run = null;
        try { run = ScriptPlayer.Play(app, s.Id, new ScriptPlayOptions()); }
        catch (BridgeException e) { Check("JP9: the play started", false, e.Error.ToString()); yield break; }
        yield return PWaitRun(run);
        var r = run.Result;
        Check("JP9 Present: the script holds steps that fail (3 failures reported)", r.Failures.Count == 3, string.Join("; ", r.Failures.Select(f => f.Phase + " " + f.Index + " " + f.Cmd + " " + f.Code)));
        var f0 = r.Failures.FirstOrDefault(f => f.Phase == "prepare");
        Check("JP9: the preparation's failure: phase, index 1, command, code bad_args, a message", f0 != null && f0.Index == 1 && f0.Cmd == "fk.enable" && f0.Code == BridgeCodes.BadArgs && !string.IsNullOrEmpty(f0.Message),
            f0 == null ? "none" : f0.ToJson().ToString(Formatting.None));
        var fd = r.Failures.FirstOrDefault(f => f.Cmd == "screenshot");
        Check("JP9: a step whose command answers later: phase setup, index 2, unsupported, the rule named", fd != null && fd.Phase == "setup" && fd.Index == 2 && fd.Code == BridgeCodes.Unsupported &&
            fd.Message.StartsWith(ScriptPlayer.DeferringStepMessage, StringComparison.Ordinal), fd?.ToJson().ToString(Formatting.None));
        var f1 = r.Failures.FirstOrDefault(f => f.Phase == "setup");
        Check("JP9: the set-up's failure: phase, index 0, command, code not_found", f1 != null && f1.Index == 0 && f1.Cmd == "obj.transform" && f1.Code == BridgeCodes.NotFound, f1?.ToJson().ToString(Formatting.None));
        Check("JP9: the play went on: every other step sent, the timeline loaded and playing", r.Ok && r.PrepareSent == 3 && r.SetupSent == 3 && r.Loaded && r.State == "playing" &&
            app.Session.Model.Find(a).Doc.Transform.Rot.Y == 20f, "prepare " + r.PrepareSent + ", setup " + r.SetupSent + ", state " + r.State);
        Check("JP9: a failed preparation step keeps the mark unwritten", app.Session.Model.Scripts.Find(s.Id).Prepared == null);
        var reply = r.ToReply();
        Check("JP9: the reply's failures [{phase, index, cmd, code, message}]", ((JArray)reply["failures"]).Count == 3 &&
            ((JObject)reply["failures"][0]).Properties().Select(p => p.Name).SequenceEqual(new[] { "phase", "index", "cmd", "code", "message" }), reply["failures"].ToString(Formatting.None));
        ScriptPlayer.Stop(app, false);
        // strict: stops at the failed step, before the load
        app.Runtime.GetPart<TimelinePlayback>().Service.Clear();
        app.Runtime.ApplyNow();
        try { run = ScriptPlayer.Play(app, s.Id, new ScriptPlayOptions { Strict = true }); }
        catch (BridgeException e) { Check("JP9: the strict play started", false, e.Error.ToString()); yield break; }
        yield return PWaitRun(run);
        r = run.Result;
        var x = r.Error?.Detail?.Extra;
        Check("JP9: strict answers the step's code with detail {phase prepare, index 1}", r.Error != null && r.Error.Code == BridgeCodes.BadArgs && x != null && (string)x["phase"] == "prepare" && (int)x["index"] == 1,
            r.Error?.ToString());
        Check("JP9: strict stopped before the set-up and the load (the Timeline not loaded, the third step not sent)", !r.Loaded && app.Session.Model.Timeline == null && r.PrepareSent == 2 && !r.Order.Contains("setup"),
            "order " + string.Join(",", r.Order) + ", sent " + r.PrepareSent);
    }

    // ------------------------------------------------------------ JE2s, JP8s (stains need a graphics device)

    static List<string> PAdded(AppCore app, int before)
    {
        var l = app.Session.Commands.UndoList.Select(e => e.Label).ToList();
        return l.Take(Math.Max(0, l.Count - before)).Reverse().ToList();
    }

    static WetStain PStain(AppCore app, string id) => (app.Session.Model.Find(id)?.State as CharaNodeState)?.Wet?.Stains.FirstOrDefault();

    static IEnumerator PJE2s(AppCore app, string a, string b)
    {
        var s = NewScript("prepare a stain", CharaRole("a", "female"));
        s.Cast["a"] = a;
        s.Prepare.Add(J("{\"cmd\":\"wet.attach\",\"key\":\"$a\",\"preset\":\"water\"}"));
        s.Prepare.Add(J("{\"cmd\":\"wet.set\",\"key\":\"$a\",\"values\":{\"maxRadius\":3}}"));
        s.Timeline = ScriptTimeline(2f, true);
        PAddScripts(app, s);
        int stainsBefore = (app.Session.Model.Find(a).State as CharaNodeState).Wet.Stains.Count;
        int entries = app.Session.Commands.UndoList.Count;
        ScriptPlayRun run = null;
        try { run = ScriptPlayer.Play(app, s.Id, new ScriptPlayOptions()); }
        catch (BridgeException e) { Check("JE2s: the play started", false, e.Error.ToString()); yield break; }
        yield return PWaitRun(run);
        var r = run.Result;
        var added = PAdded(app, entries);
        int stainsAfter = (app.Session.Model.Find(a).State as CharaNodeState).Wet.Stains.Count;
        Check("JE2s Present: the stain exists after the preparation (stains " + stainsBefore + " -> " + stainsAfter + ")", r.Ok && r.PrepareFailed == 0 && stainsAfter > stainsBefore,
            r.Error?.ToString() ?? string.Join("; ", r.Failures.Select(f => f.Index + " " + f.Cmd + " " + f.Code + " " + f.Message)));
        Check("JE2s: one entry «Prepare script», then «Load script» (read [" + string.Join(", ", added) + "])", added.SequenceEqual(new[] { ScriptEditCommand.LabelPrepare, ScriptEditCommand.LabelLoad }));
        ScriptPlayer.Stop(app, false);
        app.Session.Commands.Undo();   // «Load script»
        app.Session.Commands.Undo();   // «Prepare script»
        app.Runtime.ApplyNow();
        yield return PFrames(2);
        int stainsUndone = (app.Session.Model.Find(a).State as CharaNodeState).Wet.Stains.Count;
        Check("JE2s: the undo of «Prepare script» removes the stain (stains " + stainsUndone + ") and the mark", stainsUndone == stainsBefore && app.Session.Model.Scripts.Find(s.Id).Prepared == null);
    }

    /// <summary>The cast character's stain as wet.status reads it (the scene part's running clock): running and t; null when it has no current stain.</summary>
    static (bool running, double t, string current)? PWetStatus(AppCore app, string id)
    {
        JObject r;
        try { r = JObject.Parse(app.Bridge.Server.RunNow("{\"cmd\":\"wet.status\",\"key\":\"" + id + "\"}")); }
        catch (Exception) { return null; }
        var c = (r["characters"] as JArray)?.FirstOrDefault(x => (string)x["key"] == id) ?? (r["characters"] as JArray)?.FirstOrDefault();
        if (r.Value<bool?>("ok") != true || c == null || c["current"] == null || c["current"].Type == JTokenType.Null) return null;
        return (c.Value<bool?>("running") == true, c.Value<double?>("t") ?? 0, (string)c["current"]);
    }

    static string PWetText(AppCore app, string id)
    {
        var w = PWetStatus(app, id);
        var m = PStain(app, id);
        return (w == null ? "wet.status: no current stain" : "wet.status: running " + w.Value.running + ", t " + F(w.Value.t)) + "; the model's WetStain.T " + (m == null ? "none" : F(m.T));
    }

    static IEnumerator PJP8s(AppCore app, string a, string b)
    {
        var s = NewScript("stop dries", CharaRole("a", "female"));
        s.Cast["a"] = a;
        s.Prepare.Add(J("{\"cmd\":\"wet.attach\",\"key\":\"$a\",\"preset\":\"water\"}"));
        s.Timeline = ScriptTimeline(4f, true);
        s.Timeline.Events.Add(Event(0.05f, "{\"cmd\":\"wet.start\",\"key\":\"$a\"}"));
        PAddScripts(app, s);
        foreach (bool reset in new[] { false, true })
        {
            string tag = reset ? "reset" : "reset: false";
            ScriptPlayRun run = null;
            try { run = ScriptPlayer.Play(app, s.Id, new ScriptPlayOptions { Prepare = ScriptPlayOptions.PrepareAlways }); }
            catch (BridgeException e) { Check("JP8s (" + tag + "): the play started", false, e.Error.ToString()); yield break; }
            yield return PWaitRun(run);
            yield return PFrames(30);
            var before = PWetStatus(app, a);
            string beforeText = PWetText(app, a);
            Check("JP8s Present (" + tag + "): before the stop the cast's stain runs with t > 0 (" + beforeText + ")", run.Result.Ok && before != null && before.Value.running && before.Value.t > 0,
                run.Result.Error?.ToString() ?? string.Join("; ", run.Result.Failures.Select(f => f.Cmd + " " + f.Code + " " + f.Message)));
            var stop = ScriptPlayer.Stop(app, reset);
            yield return PFrames(10);
            var after = PWetStatus(app, a);
            string afterText = PWetText(app, a);
            if (reset)
                Check("JP8s (reset): dried: running before with t > 0, after the stop no stain or not running (after: " + afterText + "), dried 1",
                    before != null && before.Value.running && before.Value.t > 0 && (after == null || !after.Value.running) && stop.Dried == 1,
                    "dried " + stop.Dried + "; " + string.Join("; ", stop.Warnings));
            else
                Check("JP8s (reset: false): kept: running before, still running after the stop with t larger (after: " + afterText + "), dried 0",
                    before != null && before.Value.running && after != null && after.Value.running && after.Value.t > before.Value.t && stop.Dried == 0);
        }
    }

    // ------------------------------------------------------------ JR3

    static string PRecordDir(string name) => Path.GetFullPath(Path.Combine(ContentSettings.ProjectRoot, PlayUserData, "record_" + name));

    static void PDeleteFrames(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            foreach (var p in Directory.GetFiles(dir)) File.Delete(p);
            Directory.Delete(dir);
        }
        catch (Exception e) { Info("the frames of " + dir + " were not all deleted: " + e.Message); }
    }

    static IEnumerator PJR3(AppCore app, string a, string b)
    {
        const float duration = 2f;
        const int fps = 30;
        var s = NewScript("recorded", CharaRole("a", "female"), CharaRole("b", "male"));
        s.Cast["a"] = a;
        s.Cast["b"] = b;
        s.Timeline = ScriptTimeline(duration, true,
            Track("pos", "$a", TimelineKey.OfVector(0f, new Vec3(0, 0, 0))),
            Track("rot", "$b", TimelineKey.OfVector(0f, new Vec3(0, -90, 0))));
        s.Timeline.Tracks[0].Keys.Add(TimelineKey.OfVector(duration, new Vec3(0, 0, 3)));
        foreach (var t in new[] { 0f, 0.5f, 1.0f, 1.5f }) s.Timeline.Events.Add(Event(t, "{\"cmd\":\"ping\"}"));
        PAddScripts(app, s);
        string dir = PRecordDir("jr3");
        PDeleteFrames(dir);
        var tlp = app.Runtime.GetPart<TimelinePlayback>();
        ScriptPlayRun run = null;
        try { run = ScriptPlayer.Play(app, s.Id, new ScriptPlayOptions { Record = new JObject { ["path"] = dir, ["width"] = 320, ["height"] = 180, ["fps"] = fps } }); }
        catch (BridgeException e) { Check("JR3: the recorded play started", false, e.Error.ToString()); yield break; }
        yield return PWaitRun(run);
        var r = run.Result;
        if (!Check("JR3: the play answered with record.start's reply", r.Ok && r.Record != null && (string)r.Record["path"] != null, r.Error?.ToString() ?? r.Record?.ToString(Formatting.None))) yield break;
        var sp = app.Runtime.GetPart<ScriptPlayback>();
        Check("JR3: script status reads recording while it records", sp.Recording);
        float until = Time.realtimeSinceStartup + 60;
        var rec = FrameRecorder.Current;
        while (rec != null && !rec.IsDone && Time.realtimeSinceStartup < until) yield return null;
        yield return PFrames(3);
        long firedAtEnd = tlp.EventsFired;
        double timeAtEnd = tlp.Player.Time;
        yield return PFrames(30);
        int want = (int)Math.Round(duration * fps, MidpointRounding.AwayFromZero);
        var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.png") : new string[0];
        Check("JR3: frames = round(duration x fps) = " + want + ", written = frames, stoppedBy pass", rec != null && rec.IsDone && rec.PassFrames == want && rec.Written == want && files.Length == want && rec.StoppedBy == FrameRecorder.ByPass,
            "pass frames " + rec?.PassFrames + ", written " + rec?.Written + ", files " + files.Length + ", stoppedBy " + rec?.StoppedBy + ", error " + rec?.Error);
        Check("JR3: the Timeline is paused after the pass (no timed command, no time after the last frame)", tlp.Player.State == TimelinePlayState.Paused && tlp.EventsFired == firedAtEnd && tlp.Player.Time == timeAtEnd && sp.PassesPaused >= 1,
            "state " + tlp.Player.State + ", events after " + (tlp.EventsFired - firedAtEnd) + ", time " + F(timeAtEnd) + " -> " + F(tlp.Player.Time));
        Check("JR3: script status no longer reads recording", !sp.Recording);
        JObject json = null;
        try { json = JObject.Parse(File.ReadAllText(Path.Combine(dir, FrameRecorder.RecordFile))); }
        catch (Exception e) { Check("JR3: record.json read", false, e.Message); }
        if (json != null)
        {
            double dt = (double)json["dt"];
            var times = (json["timelineTimes"] as JArray)?.Select(t => t.Type == JTokenType.Null ? double.NaN : (double)t).ToList() ?? new List<double>();
            double worst = 0;
            int worstK = -1;
            for (int k = 0; k < times.Count - 1; k++)
            {
                double d = Math.Abs(times[k] - (k + 1) * dt);
                if (d > worst || double.IsNaN(d)) { worst = double.IsNaN(d) ? double.PositiveInfinity : d; worstK = k; }
            }
            Check("JR3: record.json's timelineTimes: frame k at k x dt (within 1e-5 s; the last frame the wrap)", times.Count == want && worst <= 1e-5 && times.Count > 0 && times[times.Count - 1] < dt,
                "count " + times.Count + ", dt " + F(dt) + ", worst " + F(worst) + " at frame " + (worstK + 1) + ", last " + (times.Count > 0 ? F(times[times.Count - 1]) : "none"));
            Measure("JR3: frames " + want + ", maxPending " + rec.MaxPending + ", waits " + rec.Waits + ", syncFrames " + rec.SyncFrames);
        }
        PDeleteFrames(dir);
        Check("JR3: the frames were deleted", !Directory.Exists(dir));
        ScriptPlayer.Stop(app, false);
    }

    // ------------------------------------------------------------ JD3

    /// <summary>One digest: bones and holders of the cast characters, the camera, the slots, the stains, the ragdoll status.</summary>
    sealed class Digest
    {
        public double T;
        public readonly Dictionary<string, Vector3> Bones = new Dictionary<string, Vector3>(StringComparer.Ordinal);
        public readonly Dictionary<string, Vector3> Holders = new Dictionary<string, Vector3>(StringComparer.Ordinal);
        public Vector3 CamPos;
        public Quaternion CamRot;
        public float Fov;
        public JArray Slots;
        public readonly Dictionary<string, JObject> Stains = new Dictionary<string, JObject>(StringComparer.Ordinal);
        public JObject Ragdoll;
        public int BonesPerChara;
    }

    static Digest PTakeDigest(AppCore app, IReadOnlyDictionary<string, string> cast, double t)
    {
        var d = new Digest { T = t };
        foreach (var kv in cast)
        {
            var c = app.Runtime.FindChara(kv.Value);
            if (c == null || !c.Root) continue;
            d.Holders[kv.Key] = c.Holder.position;
            int n = 0;
            foreach (var tr in c.Root.GetComponentsInChildren<Transform>(true))
            {
                if (!tr.name.StartsWith("cf_J_", StringComparison.Ordinal)) continue;
                string k = kv.Key + "/" + tr.name;
                if (d.Bones.ContainsKey(k)) k += "#" + n;
                d.Bones[k] = tr.position;
                n++;
            }
            d.BonesPerChara = Math.Max(d.BonesPerChara, n);
            if (app.Session.Model.Find(kv.Value)?.State is CharaNodeState st && st.Wet != null && st.Wet.Stains.Count > 0)
            {
                var ws = JObject.Parse(app.Bridge.Server.RunNow("{\"cmd\":\"wet.status\",\"key\":\"" + kv.Value + "\"}"));
                d.Stains[kv.Key] = ws;
            }
        }
        var cam = Camera.main;
        if (cam)
        {
            d.CamPos = cam.transform.position;
            d.CamRot = cam.transform.rotation;
            d.Fov = cam.fieldOfView;
        }
        d.Slots = JObject.Parse(app.Bridge.Server.RunNow("{\"cmd\":\"overrides.list\"}"))["overrides"] as JArray;
        d.Ragdoll = JObject.Parse(app.Bridge.Server.RunNow("{\"cmd\":\"ragdoll.status\"}"));
        return d;
    }

    static IEnumerator PJD3(AppCore app, string a, string b)
    {
        // the sample when it exists, else this part's own script with a stain and a body
        const float duration = 4f;
        var s = NewScript("determinism", CharaRole("a", "female"), CharaRole("b", "male"));
        s.Cast["a"] = a;
        s.Cast["b"] = b;
        s.Prepare.Add(J("{\"cmd\":\"wet.attach\",\"key\":\"$a\",\"preset\":\"water\"}"));
        s.Timeline = ScriptTimeline(duration, true,
            Track("anime", "$b", TimelineKey.OfAnime(0f, new AnimeRef(DocDefaults.VanillaSource, 11, 5, 0))),
            Track("pos", "$a", TimelineKey.OfVector(0f, new Vec3(0, 0, 0))),
            Track("camera", null, TimelineKey.OfCamera(0f, new TimelineCamera(new Vec3(2.5f, 10, 0), new Vec3(5, 180, 0), 35, 30))));
        s.Timeline.Tracks[1].Keys.Add(TimelineKey.OfVector(duration, new Vec3(0, 0, 3)));
        s.Timeline.Events.Add(Event(0f, "{\"cmd\":\"ragdoll.release\",\"key\":\"$b\"}"));
        s.Timeline.Events.Add(Event(0.05f, "{\"cmd\":\"wet.start\",\"key\":\"$a\"}"));
        s.Timeline.Events.Add(Event(2.0f, "{\"cmd\":\"ragdoll.build\",\"key\":\"$b\"}"));
        PAddScripts(app, s);
        var cast = new Dictionary<string, string> { ["a"] = a, ["b"] = b };
        List<Digest> first = null, second = null;
        yield return PDigestPass(app, s.Id, cast, duration, "jd3a", d => first = d);
        // save and load
        yield return PCall(app, new { cmd = "scene.save", name = "script_jd3", overwrite = true, thumb = "placeholder" });
        if (!Check("JD3: scene.save", POk, Short(pLastText))) yield break;
        yield return PCall(app, new { cmd = "scene.load", name = "script_jd3" });
        if (!Check("JD3: scene.load", POk, Short(pLastText))) yield break;
        yield return PFrames(5);
        var loaded = app.Session.Model.Scripts?.Find(s.Id);
        Check("JD3 Present: the loaded scene holds the script with its cast and mark", loaded != null && loaded.Cast.Count == 2 && loaded.Prepared != null, loaded == null ? "none" : "prepared " + loaded.Prepared);
        yield return PDigestPass(app, s.Id, cast, duration, "jd3b", d => second = d);
        if (first == null || second == null || first.Count != 7 || second.Count != 7) { Check("JD3: digests at 7 times in both plays", false, (first?.Count ?? 0) + " / " + (second?.Count ?? 0)); yield break; }
        PCompareDigests("JD3", first, second);
    }

    /// <summary>One recorded pass of a script at 30 fps with digests at k x duration / 8 (k 1-7), each at the first frame whose Timeline time reaches it.</summary>
    static IEnumerator PDigestPass(AppCore app, string scriptId, IReadOnlyDictionary<string, string> cast, float duration, string name, Action<List<Digest>> done)
    {
        var digests = new List<Digest>();
        string dir = PRecordDir(name);
        PDeleteFrames(dir);
        var tlp = app.Runtime.GetPart<TimelinePlayback>();
        int next = 1;
        Action<int> onLate = frame =>
        {
            if (next > 7 || !FrameRecorder.IsRecording) return;
            double T = next * duration / 8.0;
            if (tlp.Player.State == TimelinePlayState.Playing && tlp.Player.Time >= T)
            {
                digests.Add(PTakeDigest(app, cast, tlp.Player.Time));
                next++;
            }
        };
        var frames = app.Bridge.Server.Frames;
        frames.LateFrame += onLate;
        ScriptPlayRun run = null;
        try
        {
            try { run = ScriptPlayer.Play(app, scriptId, new ScriptPlayOptions { Record = new JObject { ["path"] = dir, ["width"] = 160, ["height"] = 90, ["fps"] = 30 } }); }
            catch (BridgeException e) { Check(name + ": the recorded play started", false, e.Error.ToString()); }
            if (run != null)
            {
                float until = Time.realtimeSinceStartup + 120;
                while (!run.IsDone && Time.realtimeSinceStartup < until) yield return null;
                if (run.Result.Error != null) Check(name + ": the recorded play", false, run.Result.Error.ToString());
                var rec = FrameRecorder.Current;
                while (rec != null && !rec.IsDone && Time.realtimeSinceStartup < until) yield return null;
                Info(name + ": failures " + string.Join("; ", run.Result.Failures.Select(f => f.Phase + " " + f.Index + " " + f.Cmd + " " + f.Code + " " + f.Message)));
            }
        }
        finally { frames.LateFrame -= onLate; }
        PDeleteFrames(dir);
        ScriptPlayer.Stop(app, false);
        done(digests);
    }

    static void PCompareDigests(string id, List<Digest> x, List<Digest> y)
    {
        double bones = 0, holders = 0, cam = 0;
        string worstBone = "";
        int bonesCompared = 0, slotsEqual = 0, slotsCompared = 0, stainParts = 0, stainDiff = 0, ragdollEqual = 0;
        for (int i = 0; i < x.Count; i++)
        {
            var p = x[i];
            var q = y[i];
            foreach (var kv in p.Bones)
            {
                if (!q.Bones.TryGetValue(kv.Key, out var v)) continue;
                bonesCompared++;
                double d = (kv.Value - v).magnitude;
                if (d > bones) { bones = d; worstBone = kv.Key + " at " + F(p.T); }
            }
            foreach (var kv in p.Holders) if (q.Holders.TryGetValue(kv.Key, out var v)) holders = Math.Max(holders, (kv.Value - v).magnitude);
            cam = Math.Max(cam, Math.Max((p.CamPos - q.CamPos).magnitude, Math.Abs(p.Fov - q.Fov)));
            slotsCompared++;
            if (JToken.DeepEquals(p.Slots, q.Slots)) slotsEqual++;
            foreach (var kv in p.Stains)
            {
                stainParts++;
                if (!q.Stains.TryGetValue(kv.Key, out var w)) { stainDiff++; continue; }
                foreach (var m in new[] { "t", "radius", "running" })
                    if (!JToken.DeepEquals(kv.Value.SelectToken("stains[0]." + m) ?? kv.Value[m], w.SelectToken("stains[0]." + m) ?? w[m])) { stainDiff++; break; }
            }
            var ra = (JObject)p.Ragdoll.DeepClone();
            var rb = (JObject)q.Ragdoll.DeepClone();
            ra.Remove("id"); rb.Remove("id");
            if (JToken.DeepEquals(ra, rb)) ragdollEqual++;
        }
        Info(id + " Present: bones per character " + x[0].BonesPerChara + ", bone positions compared " + bonesCompared + ", slots at 7 times, stains in " + stainParts + " digests");
        Measure(id + ": bones <= " + F(bones) + " units (" + worstBone + "), holders " + F(holders) + ", camera " + F(cam) + ", slots equal " + slotsEqual + "/" + slotsCompared +
            ", stain parts differing " + stainDiff + "/" + stainParts + ", ragdoll status equal " + ragdollEqual + "/" + x.Count);
        Check(id + ": bones <= 0.05 units", bones <= 0.05 && bonesCompared > 0, F(bones));
        Check(id + ": holders and camera <= 1e-4", holders <= 1e-4 && cam <= 1e-4, F(holders) + ", " + F(cam));
        Check(id + ": slot values equal at every time", slotsEqual == slotsCompared);
        Check(id + ": stain clock, radius, running equal", stainDiff == 0 && stainParts > 0, stainDiff + "/" + stainParts);
        Check(id + ": ragdoll status equal", ragdollEqual == x.Count, ragdollEqual + "/" + x.Count);
    }
}
