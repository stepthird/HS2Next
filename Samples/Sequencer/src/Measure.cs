// Sample sequencer for HS2Next (CC0-1.0: dedicated to the public domain).
// The cost measurement of the tracks' calls. A temporary looping sequence moves the given nodes and the view; each
// frame times one inline camera read (pm1) and one inline read of a node's transform (pm1b: the node's document, without
// the runtime capture); in alternating 120-frame windows the temporary sequence plays, timing each
// inline transform write (pm2) and its whole update (pm3); 60 queued camera reads are spread over the run and timed in
// frames to their reply (pm4); the frame times of the windows with and without the sequence are compared (pm5). The
// measurement replies at once and runs in the plugin's frame updates, because a reply that waits would hold every
// queued request (the queued reads of pm4 among them) behind it. The nodes' transforms and the view are put back at the end.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Stp3.Samples.Sequencer
{
    sealed class Measurement
    {
        public const int Window = 120;
        public const int AsyncReads = 60;
        /// <summary>Frames to wait for the last queued reads after the measured frames.</summary>
        public const int DrainFrames = 600;
        const string CameraGet = "{\"cmd\":\"camera.get\"}";

        readonly IPluginContext ctx;
        readonly string[] keys;
        readonly int frames;
        readonly string[] restoreTransforms;
        readonly string restoreCamera;
        readonly Player temp;
        readonly List<double> pm1 = new List<double>(), pm1b = new List<double>(), pm2 = new List<double>(), pm3 = new List<double>();
        readonly string nodeRead;
        readonly List<double> withDt = new List<double>(), withoutDt = new List<double>();
        readonly List<int> pm4 = new List<int>();
        readonly object gate = new object();
        int frame, asyncSent, drain;
        bool lastWith;
        string state = "running", error, result;

        Measurement(IPluginContext ctx, string[] keys, int frames, string[] restoreTransforms, string restoreCamera, Sequence sequence)
        {
            this.ctx = ctx;
            this.keys = keys;
            this.frames = frames;
            this.restoreTransforms = restoreTransforms;
            this.restoreCamera = restoreCamera;
            nodeRead = "{\"cmd\":\"scene.doc\",\"key\":" + JsonConvert.ToString(keys[0]) + ",\"capture\":false}";
            temp = new Player(new ContextHost(ctx));
            temp.SetSequence(sequence);
            temp.CallTimed = (cmd, ms) => { if (cmd == "obj.transform") pm2.Add(ms * 1000.0); };
            temp.Play();
        }

        public bool Running => state == "running";

        /// <summary>
        /// Reads the nodes' transforms and the view (to put them back), builds the temporary sequence and starts.
        /// not_found for a key that names no node.
        /// </summary>
        public static Measurement Start(IPluginContext ctx, string[] keys, int frames)
        {
            var restore = new string[keys.Length];
            var pos = new JArray[keys.Length];
            var rot = new JArray[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                var reply = JObject.Parse(ctx.Call("{\"cmd\":\"scene.doc\",\"key\":" + JsonConvert.ToString(keys[i]) + ",\"embed\":true,\"capture\":false}"));
                if (reply.Value<bool?>("ok") != true)
                    throw new BridgeException(BridgeCodes.NotFound, "keys[" + i + "]: " + (reply.Value<string>("error") ?? "no such node"),
                        new BridgeErrorDetail { Arg = "keys" });
                var node = reply["node"];
                if (node != null && node.Type == JTokenType.String) node = JToken.Parse((string)node);
                var tr = node?["transform"] as JObject;
                pos[i] = tr?["pos"] as JArray ?? new JArray(0.0, 0.0, 0.0);
                rot[i] = tr?["rot"] as JArray ?? new JArray(0.0, 0.0, 0.0);
                restore[i] = "{\"cmd\":\"obj.transform\",\"key\":" + JsonConvert.ToString(keys[i]) + ",\"pos\":" + pos[i].ToString(Formatting.None) +
                    ",\"rot\":" + rot[i].ToString(Formatting.None) + ",\"undo\":false}";
            }
            var cam = JObject.Parse(ctx.Call(CameraGet))["camera"] as JObject;
            var target = cam?["target"] as JArray ?? new JArray(0.0, 1.0, 0.0);
            var crot = cam?["rot"] as JArray ?? new JArray(10.0, 180.0, 0.0);
            double dist = cam?.Value<double?>("dist") ?? 3.0, fov = cam?.Value<double?>("fov") ?? 23.0;
            string restoreCamera = "{\"cmd\":\"camera.set\",\"target\":" + target.ToString(Formatting.None) + ",\"rot\":" + crot.ToString(Formatting.None) +
                ",\"dist\":" + Sequence.NumberText(dist) + ",\"fov\":" + Sequence.NumberText(fov) + ",\"undo\":false}";

            // A 2-second loop: each node slides 0.5 along x and turns 90 degrees about y and back; the view turns 30
            // degrees and backs off by 1.
            var sb = new StringBuilder("{\"duration\":2,\"loop\":true,\"tracks\":[");
            for (int i = 0; i < keys.Length; i++)
            {
                var p0 = V(pos[i]);
                var r0 = V(rot[i]);
                sb.Append("{\"kind\":\"transform\",\"key\":").Append(JsonConvert.ToString(keys[i])).Append(",\"keys\":[");
                Key(sb, 0, p0, r0).Append(',');
                Key(sb, 1, new Vec3(p0.X + 0.5, p0.Y, p0.Z), new Vec3(r0.X, r0.Y + 90, r0.Z)).Append(',');
                Key(sb, 2, p0, r0).Append("]},");
            }
            var t0 = V(target);
            var c0 = V(crot);
            sb.Append("{\"kind\":\"camera\",\"keys\":[");
            CamKey(sb, 0, t0, c0, dist, fov).Append(',');
            CamKey(sb, 1, t0, new Vec3(c0.X, c0.Y + 30, c0.Z), dist + 1, fov).Append(',');
            CamKey(sb, 2, t0, c0, dist, fov).Append("]}]}");
            var seq = Sequence.Parse(sb.ToString());
            return new Measurement(ctx, keys, frames, restore, restoreCamera, seq);
        }

        static Vec3 V(JArray a) => new Vec3((double)a[0], (double)a[1], (double)a[2]);

        static StringBuilder Key(StringBuilder sb, double t, Vec3 p, Vec3 r) =>
            sb.Append("{\"t\":").Append(Sequence.NumberText(t)).Append(",\"pos\":").Append(p).Append(",\"rot\":").Append(r).Append('}');

        static StringBuilder CamKey(StringBuilder sb, double t, Vec3 target, Vec3 r, double dist, double fov) =>
            sb.Append("{\"t\":").Append(Sequence.NumberText(t)).Append(",\"target\":").Append(target).Append(",\"rot\":").Append(r)
              .Append(",\"dist\":").Append(Sequence.NumberText(dist)).Append(",\"fov\":").Append(Sequence.NumberText(fov)).Append('}');

        /// <summary>One frame of the measurement (from the plugin's Update while running).</summary>
        public void Update(in PluginFrame f)
        {
            if (!Running) return;
            if (frame < frames)
            {
                bool with = (frame / Window) % 2 == 0;
                // A frame's work shows in the next frame's delta: the delta measured now belongs to the previous frame's window.
                if (frame > 0) (lastWith ? withDt : withoutDt).Add(f.UnscaledDt * 1000.0);

                long t0 = Stopwatch.GetTimestamp();
                ctx.Call(CameraGet);
                pm1.Add(Us(t0));
                t0 = Stopwatch.GetTimestamp();
                ctx.Call(nodeRead);
                pm1b.Add(Us(t0));

                int every = Math.Max(1, frames / AsyncReads);
                if (asyncSent < AsyncReads && frame % every == 0) SendQueuedRead(ctx.Frames.Frame);

                if (with)
                {
                    long t1 = Stopwatch.GetTimestamp();
                    temp.Update(f.UnscaledDt > 0 ? f.UnscaledDt : 1.0 / 60);
                    pm3.Add(Us(t1) / 1000.0);
                }
                lastWith = with;
                frame++;
                return;
            }
            int done;
            lock (gate) done = pm4.Count;
            if (done < asyncSent && drain++ < DrainFrames) return;
            Finish();
        }

        void SendQueuedRead(int sentFrame)
        {
            asyncSent++;
            Task<string> task;
            try { task = ctx.CallAsync(CameraGet); }
            catch (Exception) { return; }
            task.ContinueWith(t =>
            {
                int now = ctx.Frames.Frame;
                lock (gate) pm4.Add(now - sentFrame);
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        static double Us(long t0) => (Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency;

        void Restore()
        {
            foreach (var r in restoreTransforms) ctx.Call(r);
            ctx.Call(restoreCamera);
        }

        void Finish()
        {
            Restore();
            List<int> p4;
            lock (gate) p4 = new List<int>(pm4);
            var sb = new StringBuilder(512);
            sb.Append("{\"state\":\"done\"");
            sb.Append(",\"pm1\":{\"medianUs\":").Append(N(Pct(pm1, 0.5))).Append(",\"p95Us\":").Append(N(Pct(pm1, 0.95))).Append(",\"n\":").Append(pm1.Count).Append('}');
            sb.Append(",\"pm1b\":{\"medianUs\":").Append(N(Pct(pm1b, 0.5))).Append(",\"p95Us\":").Append(N(Pct(pm1b, 0.95))).Append(",\"n\":").Append(pm1b.Count)
              .Append(",\"cmd\":\"scene.doc {key, capture: false}\"}");
            sb.Append(",\"pm2\":{\"medianUs\":").Append(N(Pct(pm2, 0.5))).Append(",\"p95Us\":").Append(N(Pct(pm2, 0.95))).Append(",\"n\":").Append(pm2.Count).Append('}');
            sb.Append(",\"pm3\":{\"medianMs\":").Append(N(Pct(pm3, 0.5))).Append(",\"p95Ms\":").Append(N(Pct(pm3, 0.95))).Append(",\"n\":").Append(pm3.Count).Append('}');
            var f4 = p4.ConvertAll(x => (double)x);
            sb.Append(",\"pm4\":{\"medianFrames\":").Append(N(Pct(f4, 0.5))).Append(",\"max\":").Append(p4.Count > 0 ? Max(p4) : 0)
              .Append(",\"n\":").Append(p4.Count).Append(",\"lost\":").Append(asyncSent - p4.Count).Append('}');
            sb.Append(",\"pm5\":{\"withMs\":").Append(N(Pct(withDt, 0.5))).Append(",\"withoutMs\":").Append(N(Pct(withoutDt, 0.5)))
              .Append(",\"nWith\":").Append(withDt.Count).Append(",\"nWithout\":").Append(withoutDt.Count).Append('}');
            var w = temp.Writes;
            sb.Append(",\"writes\":{\"done\":").Append(w.Done).Append(",\"busy\":").Append(w.Busy).Append(",\"missingNode\":").Append(w.MissingNode)
              .Append(",\"error\":").Append(w.Error).Append('}');
            sb.Append(",\"keys\":").Append(keys.Length).Append(",\"frames\":").Append(frames);
            sb.Append(",\"uiOn\":").Append(ctx.Host.UiAvailable ? "true" : "false").Append('}');
            result = sb.ToString();
            state = "done";
            ctx.Log.Info("measure: " + result);
        }

        /// <summary>Stops a running measurement (the scene changed, the plugin unloads); optionally puts the nodes back.</summary>
        public void Abort(string why, bool restore)
        {
            if (!Running) return;
            if (restore)
            {
                try { Restore(); }
                catch (Exception) { }
            }
            state = "failed";
            error = why;
        }

        /// <summary>The state, the progress or the result as JSON text.</summary>
        public string ReportJson()
        {
            if (state == "done") return result;
            if (state == "failed") return "{\"state\":\"failed\",\"error\":" + JsonConvert.ToString(error) + "}";
            return "{\"state\":\"running\",\"frame\":" + frame + ",\"frames\":" + frames + ",\"keys\":" + keys.Length + "}";
        }

        static int Max(List<int> l)
        {
            int m = int.MinValue;
            foreach (int x in l) if (x > m) m = x;
            return m;
        }

        /// <summary>Nearest-rank percentile (0 when empty).</summary>
        static double Pct(List<double> l, double p)
        {
            if (l.Count == 0) return 0;
            var a = l.ToArray();
            Array.Sort(a);
            return a[TimingRing.NearestRank(a.Length, p)];
        }

        static string N(double d) => Sequence.NumberText(Math.Round(d, 3));
    }
}
