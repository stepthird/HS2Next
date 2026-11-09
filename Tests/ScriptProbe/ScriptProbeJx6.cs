// ScriptProbe's job "jx6" (a player started with -noBridge; spec 17.2 JX6, 17.3 JW4): every request goes to the app's own bridge host, which runs
// with no listener, inline (RunNow) or through its queue (SubmitAsync), never over HTTP. In order:
//   host     the host exists, Listening false, its stage line
//   targets  BR-Chan and HERO107 loaded (the cards the job names)
//   jw4      the inline no-op (ping) and the inline camera.get, rounds timed on the main thread
//   jx2      a Timeline of four timed commands (ping, camera.get, wet.status, camera.set with an integer array) played from 0: fired, errors, the view's target
//   jx4      the sample imported into the scene, its character roles cast to the targets, script.play {load: true} through the queue, its reply; then the
//            pass played to its end: the Timeline's timed commands fired and failed
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using HS2Next.App;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Stp3.Test.ScriptProbe
{
    public sealed partial class ScriptProbePlugin
    {
        static AppCore App => AppCore.Current;

        static JObject Inline(string json)
        {
            try { return JObject.Parse(App.Bridge.Server.RunNow(json)); }
            catch (Exception e) { return new JObject { ["ok"] = false, ["code"] = "probe", ["error"] = e.GetType().Name + ": " + e.Message }; }
        }

        static JObject Inline(object request) => Inline(JsonConvert.SerializeObject(request));

        /// <summary>A request through the host's queue; the reply lands in <paramref name="box"/>[0] (a timeout or a fault as an error reply).</summary>
        static IEnumerator Queued(object request, JObject[] box, double timeoutSec = 650)
        {
            string json = request is string s ? s : JsonConvert.SerializeObject(request);
            Task<string> t;
            try { t = App.Bridge.Server.SubmitAsync(json); }
            catch (Exception e) { box[0] = new JObject { ["ok"] = false, ["code"] = "probe", ["error"] = e.Message }; yield break; }
            float until = Time.realtimeSinceStartup + (float)timeoutSec;
            while (!t.IsCompleted && Time.realtimeSinceStartup < until) yield return null;
            if (!t.IsCompleted) { box[0] = new JObject { ["ok"] = false, ["code"] = "timeout", ["error"] = "no reply in " + timeoutSec + " s" }; yield break; }
            if (t.IsFaulted) { box[0] = new JObject { ["ok"] = false, ["code"] = "probe", ["error"] = t.Exception?.GetBaseException().Message }; yield break; }
            try { box[0] = JObject.Parse(t.Result); }
            catch (Exception e) { box[0] = new JObject { ["ok"] = false, ["code"] = "probe", ["error"] = "unreadable reply: " + e.Message }; }
        }

        static string KeyOf(JObject r) => (string)r?["key"] ?? (string)r?["added"]?[0]?["key"];

        /// <summary>Rounds of one inline request on the main thread: median, p95, max (µs) and the reply's ok of the first.</summary>
        static JObject TimeInline(string json, int rounds, int warm = 50)
        {
            var server = App.Bridge.Server;
            string first = null;
            for (int i = 0; i < warm; i++) first = server.RunNow(json);
            var us = new List<double>(rounds);
            for (int i = 0; i < rounds; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                server.RunNow(json);
                us.Add((Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency);
            }
            us.Sort();
            JObject r0 = null;
            try { r0 = JObject.Parse(first ?? "{}"); } catch (Exception) { }
            return new JObject
            {
                ["request"] = json, ["rounds"] = rounds, ["medianUs"] = us[us.Count / 2], ["p95Us"] = us[Math.Min(us.Count - 1, (int)Math.Ceiling(0.95 * us.Count) - 1)], ["maxUs"] = us[us.Count - 1],
                ["ok"] = r0?.Value<bool?>("ok") == true, ["code"] = (string)r0?["code"],
            };
        }

        IEnumerator Jx6Job()
        {
            var app = App;
            result["host"] = new JObject
            {
                ["exists"] = app?.Bridge != null, ["listening"] = app?.Bridge?.Listening ?? false, ["stage"] = app?.BridgeStageSummary, ["settings"] = app?.BridgeSettingsSummary,
                ["commands"] = app?.Bridge?.Server?.Commands?.Specs?.Count() ?? 0,
            };
            if (app?.Bridge == null) throw new InvalidOperationException("no bridge host");

            // targets
            var box = new JObject[1];
            string female = (string)job["female"], male = (string)job["male"];
            var fl = Inline(new { cmd = "chara.load", path = female, sex = "female" });
            var ml = Inline(new { cmd = "chara.load", path = male, sex = "male" });
            string a = KeyOf(fl), b = KeyOf(ml);
            result["targets"] = new JObject { ["female"] = a, ["male"] = b, ["femaleReply"] = fl.Value<bool?>("ok") == true ? "ok" : fl.ToString(Formatting.None), ["maleReply"] = ml.Value<bool?>("ok") == true ? "ok" : ml.ToString(Formatting.None) };
            if (b != null) Inline(new { cmd = "obj.transform", key = b, pos = new[] { 5, 0, 0 }, rot = new[] { 0, -90, 0 } });
            for (int i = 0; i < 30; i++) yield return null;

            // jw4 (off): the same scene as the session with the bridge on (both targets, no map)
            int rounds = (int?)job["inlineRounds"] ?? 2000;
            result["jw4"] = new JObject { ["noop"] = TimeInline("{\"cmd\":\"ping\"}", rounds), ["cameraGet"] = TimeInline("{\"cmd\":\"camera.get\"}", rounds) };

            // jx2
            var timed = new JArray
            {
                new JObject { ["t"] = 0.05, ["do"] = new JObject { ["cmd"] = "ping" } },
                new JObject { ["t"] = 0.10, ["do"] = new JObject { ["cmd"] = "camera.get" } },
                new JObject { ["t"] = 0.15, ["do"] = new JObject { ["cmd"] = "wet.status" } },
                new JObject { ["t"] = 0.20, ["do"] = new JObject { ["cmd"] = "camera.set", ["target"] = new JArray(0, 10, 0), ["dist"] = 30 } },
            };
            var pb = app.Runtime.GetPart<TimelinePlayback>();
            var build = Inline(new JObject { ["cmd"] = "timeline.build", ["duration"] = 1.0, ["loop"] = false, ["tracks"] = new JArray(), ["events"] = timed, ["play"] = false }.ToString(Formatting.None));
            long fired0 = pb.EventsFired, errors0 = pb.EventErrors;
            var play = Inline(new { cmd = "timeline.play", from = 0.0 });
            for (int i = 0; i < 90; i++) yield return null;   // 1.5 s at 60 fps; at least 0.75 s at 120
            var st = Inline(new { cmd = "timeline.status" });
            var cam = Inline(new { cmd = "camera.get" });
            result["jx2"] = new JObject
            {
                ["build"] = build.Value<bool?>("ok") == true ? "ok" : build.ToString(Formatting.None), ["play"] = play.Value<bool?>("ok") == true ? "ok" : play.ToString(Formatting.None),
                ["fired"] = pb.EventsFired - fired0, ["errors"] = pb.EventErrors - errors0, ["lastEventError"] = st["lastEventError"], ["cameraTarget"] = cam.SelectToken("camera.target"),
            };
            Inline(new { cmd = "timeline.stop" });
            Inline(new { cmd = "timeline.clear" });

            // jx4: the sample through the script commands, script.play through the queue (it defers)
            string sample = (string)job["sample"];
            if (string.IsNullOrEmpty(sample)) { result["jx4"] = new JObject { ["skipped"] = "no sample named in the job" }; yield break; }
            yield return Queued(new { cmd = "script.import", path = sample, to = "scene" }, box);
            var imp = box[0];
            string id = (string)imp?["script"];
            var jx4 = new JObject { ["import"] = imp };
            result["jx4"] = jx4;
            if (imp?.Value<bool?>("ok") != true || id == null) yield break;
            var roles = new JObject();
            foreach (var kv in (job["roles"] as JObject) ?? new JObject())
            {
                string sex = (string)kv.Value;
                if (sex == "female") roles[kv.Key] = a;
                else if (sex == "male") roles[kv.Key] = b;
            }
            yield return Queued(new JObject { ["cmd"] = "script.cast", ["script"] = id, ["roles"] = roles }.ToString(Formatting.None), box);
            jx4["cast"] = box[0];
            // the guard (the lead's rule): no play may let load: true load a character from its hint card, so every character role must be cast to a target first
            yield return Queued(new { cmd = "script.list" }, box);
            var row = (box[0]?["scripts"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["script"] == id);
            var uncastChara = ((row?["uncast"] as JArray) ?? new JArray()).Select(x => (string)x).Where(x => roles[x] != null || ((job["roles"] as JObject)?[x] != null)).ToList();
            jx4["uncast"] = row?["uncast"];
            if (row == null || uncastChara.Count > 0)
            {
                jx4["aborted"] = row == null ? "the script is not listed: the play was not sent" : "character roles uncast: " + string.Join(", ", uncastChara) + "; the play was not sent";
                yield break;
            }
            long f1 = pb.EventsFired, e1 = pb.EventErrors;
            var sw = Stopwatch.StartNew();
            yield return Queued(new { cmd = "script.play", script = id, load = true }, box);
            jx4["playMs"] = sw.Elapsed.TotalMilliseconds;
            jx4["play"] = box[0];
            double duration = pb.Service?.Document?.Duration ?? (double?)job["duration"] ?? 10.0;
            bool loop = pb.Service?.Document?.Loop ?? true;
            jx4["duration"] = duration;
            // one pass: paused at the loop's wrap (or at the end without a loop), so the counters below hold one pass
            float until = Time.realtimeSinceStartup + (float)duration + 5f;
            double prev = -1;
            string passEnd = "time limit";
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                double t = pb.Player.Time;
                var state = pb.Player.State;
                if (loop && prev >= 0 && t + 1e-6 < prev) { Inline(new { cmd = "timeline.pause" }); passEnd = "the loop's wrap at " + prev.ToString("F3"); break; }
                if (!loop && state != HS2Next.Scenes.Timeline.TimelinePlayState.Playing && prev >= 0) { passEnd = "the end (" + state + ")"; break; }
                prev = t;
            }
            for (int i = 0; i < 5; i++) yield return null;   // the replies of the last timed commands
            jx4["passEnd"] = passEnd;
            var st2 = Inline(new { cmd = "timeline.status" });
            jx4["eventsFired"] = pb.EventsFired - f1;
            jx4["eventErrors"] = pb.EventErrors - e1;
            jx4["lastEventError"] = st2["lastEventError"];
            jx4["timelineTime"] = st2["time"];
            yield return Queued(new { cmd = "script.status" }, box);
            jx4["status"] = box[0];
            yield return Queued(new { cmd = "script.stop", reset = true }, box);
            jx4["stop"] = box[0];
        }
    }
}
