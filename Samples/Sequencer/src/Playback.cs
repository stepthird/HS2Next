// Sample sequencer for HS2Next (CC0-1.0: dedicated to the public domain).
// Playback without the engine: a small vector and quaternion type, key interpolation, the playback clock with its
// event crossings (loop wrap, pause, seek, step), and the player that turns the sequence into command requests through
// a host interface (the plugin passes its context's inline and queued calls; the tests pass a recording fake).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Stp3.Samples.Sequencer
{
    public readonly struct Vec3
    {
        public readonly double X, Y, Z;

        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }

        public static Vec3 Lerp(Vec3 a, Vec3 b, double u) => new Vec3(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u, a.Z + (b.Z - a.Z) * u);

        public override string ToString() => "[" + Sequence.NumberText(X) + ", " + Sequence.NumberText(Y) + ", " + Sequence.NumberText(Z) + "]";
    }

    /// <summary>
    /// A rotation quaternion. Euler angles follow the engine's convention: degrees, applied as z about the forward
    /// axis, then x about the right axis, then y about the up axis (q = qy * qx * qz).
    /// </summary>
    public readonly struct Quat
    {
        public readonly double X, Y, Z, W;

        public Quat(double x, double y, double z, double w) { X = x; Y = y; Z = z; W = w; }

        const double Deg = Math.PI / 180.0;

        public static Quat operator *(Quat a, Quat b) => new Quat(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        public static double Dot(Quat a, Quat b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

        public Quat Normalized()
        {
            double n = Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
            return n > 0 ? new Quat(X / n, Y / n, Z / n, W / n) : new Quat(0, 0, 0, 1);
        }

        /// <summary>The rotation of Euler angles in degrees.</summary>
        public static Quat FromEuler(Vec3 e)
        {
            double hx = e.X * Deg * 0.5, hy = e.Y * Deg * 0.5, hz = e.Z * Deg * 0.5;
            var qx = new Quat(Math.Sin(hx), 0, 0, Math.Cos(hx));
            var qy = new Quat(0, Math.Sin(hy), 0, Math.Cos(hy));
            var qz = new Quat(0, 0, Math.Sin(hz), Math.Cos(hz));
            return qy * qx * qz;
        }

        /// <summary>
        /// Euler angles in degrees, each in [0, 360). Pitch (x) is taken from the rotation matrix; at a pitch of ±90°
        /// the roll (z) is 0 and the yaw carries the whole turn about the vertical.
        /// </summary>
        public Vec3 ToEuler()
        {
            var q = Normalized();
            double x = q.X, y = q.Y, z = q.Z, w = q.W;
            double m00 = 1 - 2 * (y * y + z * z), m01 = 2 * (x * y - z * w), m02 = 2 * (x * z + y * w);
            double m10 = 2 * (x * y + z * w), m11 = 1 - 2 * (x * x + z * z), m12 = 2 * (y * z - x * w);
            double m22 = 1 - 2 * (x * x + y * y);
            // Pitch from its sine and cosine (well conditioned near ±90°, unlike an arcsine).
            double sp = -m12, cp = Math.Sqrt(m10 * m10 + m11 * m11);
            double ex = Math.Atan2(sp, cp), ey, ez;
            if (cp > 1e-9)
            {
                ey = Math.Atan2(m02, m22);
                ez = Math.Atan2(m10, m11);
            }
            else
            {
                ey = sp > 0 ? Math.Atan2(m01, m00) : Math.Atan2(-m01, m00);
                ez = 0;
            }
            return new Vec3(Wrap360(ex / Deg), Wrap360(ey / Deg), Wrap360(ez / Deg));
        }

        /// <summary>An angle in degrees mapped into [0, 360).</summary>
        public static double Wrap360(double deg)
        {
            double r = deg % 360.0;
            if (r < 0) r += 360.0;
            if (r >= 360.0) r -= 360.0;
            return r == 0 ? 0.0 : r;
        }

        /// <summary>Spherical interpolation along the shorter arc (a normalized linear blend for nearly equal rotations).</summary>
        public static Quat Slerp(Quat a, Quat b, double u)
        {
            double d = Dot(a, b);
            if (d < 0) { b = new Quat(-b.X, -b.Y, -b.Z, -b.W); d = -d; }
            if (d > 0.9995)
                return new Quat(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u, a.Z + (b.Z - a.Z) * u, a.W + (b.W - a.W) * u).Normalized();
            double th = Math.Acos(Math.Min(1.0, d));
            double s = Math.Sin(th);
            double wa = Math.Sin((1 - u) * th) / s, wb = Math.Sin(u * th) / s;
            return new Quat(a.X * wa + b.X * wb, a.Y * wa + b.Y * wb, a.Z * wa + b.Z * wb, a.W * wa + b.W * wb);
        }

        /// <summary>The angle between two rotations in degrees (0 … 180).</summary>
        public static double AngleBetween(Quat a, Quat b)
        {
            // The relative rotation's half angle from its vector and scalar parts (precise near 0, unlike an arccosine).
            a = a.Normalized();
            var r = new Quat(-a.X, -a.Y, -a.Z, a.W) * b.Normalized();
            double v = Math.Sqrt(r.X * r.X + r.Y * r.Y + r.Z * r.Z);
            return 2 * Math.Atan2(v, Math.Abs(r.W)) / Deg;
        }
    }

    public struct TransformPose
    {
        public Vec3 Pos;
        public Vec3 Rot;
    }

    public struct CameraPose
    {
        public Vec3 Target;
        public Vec3 Rot;
        public double Dist;
        public double Fov;
    }

    /// <summary>Key interpolation of a track at a time.</summary>
    public static class Interp
    {
        /// <summary>
        /// The key segment at <paramref name="t"/>: the index i of the last key with time ≤ t (0 before the first key)
        /// and the blend u toward key i + 1 (0 at or after the last key, before the first key, and for step tracks).
        /// </summary>
        public static int Segment(Track tr, double t, out double u)
        {
            int n = tr.KeyCount;
            u = 0;
            if (n == 0 || t <= tr.KeyTime(0)) return 0;
            if (t >= tr.KeyTime(n - 1)) return n - 1;
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (tr.KeyTime(mid) <= t) lo = mid; else hi = mid;
            }
            if (tr.Ease == Ease.Linear)
            {
                double t0 = tr.KeyTime(lo), t1 = tr.KeyTime(lo + 1);
                u = (t - t0) / (t1 - t0);
            }
            return lo;
        }

        /// <summary>
        /// A transform track's pose. At a key (and before the first, after the last, and on step tracks) the key's own
        /// Euler angles are returned unchanged; between keys the rotation is the shortest-arc blend, as Euler angles in
        /// [0, 360).
        /// </summary>
        public static TransformPose EvalTransform(Track tr, double t)
        {
            int i = Segment(tr, t, out double u);
            var a = tr.TransformKeys[i];
            if (u <= 0) return new TransformPose { Pos = a.Pos, Rot = a.Rot };
            var b = tr.TransformKeys[i + 1];
            return new TransformPose
            {
                Pos = Vec3.Lerp(a.Pos, b.Pos, u),
                Rot = Quat.Slerp(Quat.FromEuler(a.Rot), Quat.FromEuler(b.Rot), u).ToEuler(),
            };
        }

        /// <summary>The camera track's view; rotation as in <see cref="EvalTransform"/>, the other members linear.</summary>
        public static CameraPose EvalCamera(Track tr, double t)
        {
            int i = Segment(tr, t, out double u);
            var a = tr.CameraKeys[i];
            if (u <= 0) return new CameraPose { Target = a.Target, Rot = a.Rot, Dist = a.Dist, Fov = a.Fov };
            var b = tr.CameraKeys[i + 1];
            return new CameraPose
            {
                Target = Vec3.Lerp(a.Target, b.Target, u),
                Rot = Quat.Slerp(Quat.FromEuler(a.Rot), Quat.FromEuler(b.Rot), u).ToEuler(),
                Dist = a.Dist + (b.Dist - a.Dist) * u,
                Fov = a.Fov + (b.Fov - a.Fov) * u,
            };
        }
    }

    /// <summary>The playback clock: advancing a time over a sequence and collecting the events passed on the way.</summary>
    public static class PlayClock
    {
        /// <summary>Full loop passes taken with events in one advance; further passes only move the time.</summary>
        public const int MaxWraps = 100;

        /// <summary>
        /// Advances <paramref name="time"/> by <paramref name="dt"/> ≥ 0. Events with a time in (time, end] are added
        /// to <paramref name="fired"/> in time order ([time, end] when <paramref name="inclusiveStart"/>, for the first
        /// advance after playback starts). With <paramref name="loop"/> the time wraps at the duration and each pass
        /// collects its events (an event at 0 fires on every wrap); without it the time stops at the duration and
        /// <paramref name="ended"/> is set. <paramref name="order"/> lists event indices sorted by time.
        /// </summary>
        public static double Advance(double time, double dt, double duration, bool loop, bool inclusiveStart,
            IReadOnlyList<double> eventTimes, IReadOnlyList<int> order, List<int> fired, out bool ended)
        {
            ended = false;
            if (dt < 0 || double.IsNaN(dt)) dt = 0;
            double cur = time, remaining = dt;
            bool incl = inclusiveStart;
            int wraps = 0;
            while (true)
            {
                double room = duration - cur;
                if (!loop || remaining < room)
                {
                    double end = Math.Min(cur + remaining, duration);
                    Collect(cur, end, incl, eventTimes, order, fired);
                    if (!loop && end >= duration) ended = true;
                    return end;
                }
                Collect(cur, duration, incl, eventTimes, order, fired);
                remaining -= room;
                cur = 0;
                incl = true;
                if (++wraps >= MaxWraps)
                {
                    remaining %= duration;
                    Collect(0, remaining, true, eventTimes, order, fired);
                    return remaining;
                }
            }
        }

        static void Collect(double from, double to, bool inclusive, IReadOnlyList<double> eventTimes, IReadOnlyList<int> order, List<int> fired)
        {
            if (fired == null || order == null) return;
            foreach (int i in order)
            {
                double t = eventTimes[i];
                if (t > to) break;
                if (inclusive ? t >= from : t > from) fired.Add(i);
            }
        }
    }

    /// <summary>What the player needs from its host.</summary>
    public interface ISequenceHost
    {
        /// <summary>Runs a request now and returns the reply text (the plugin context's inline call).</summary>
        string Call(string requestJson);
        /// <summary>Queues a request; <paramref name="onReply"/> receives the reply text, on any thread.</summary>
        void CallAsync(string requestJson, Action<string> onReply);
        /// <summary>A log line.</summary>
        void Log(string line);
    }

    public enum PlayState { Stopped, Playing, Paused, Ended }

    /// <summary>Counters of the writes the tracks made.</summary>
    public struct WriteCounts
    {
        public long Done, Busy, MissingNode, Error;
    }

    /// <summary>Median and maximum of the last N samples (milliseconds).</summary>
    public sealed class TimingRing
    {
        readonly double[] samples;
        int count, next;

        public TimingRing(int capacity = 600) { samples = new double[capacity]; }

        public int Count => count;

        public void Add(double ms)
        {
            samples[next] = ms;
            next = (next + 1) % samples.Length;
            if (count < samples.Length) count++;
        }

        public void Clear() { count = 0; next = 0; }

        public double Median => Percentile(0.5);

        public double Max
        {
            get
            {
                double m = 0;
                for (int i = 0; i < count; i++) if (samples[i] > m) m = samples[i];
                return m;
            }
        }

        /// <summary>The nearest-rank percentile (0 when empty).</summary>
        public double Percentile(double p)
        {
            if (count == 0) return 0;
            var copy = new double[count];
            Array.Copy(samples, copy, count);
            Array.Sort(copy);
            return copy[NearestRank(count, p)];
        }

        /// <summary>The index of the nearest-rank p-quantile in a sorted list of n values.</summary>
        public static int NearestRank(int n, double p)
        {
            int k = (int)Math.Ceiling(p * n) - 1;
            return Math.Max(0, Math.Min(n - 1, k));
        }
    }

    /// <summary>
    /// Plays one sequence. Everything runs on the host's main thread except the replies of queued event requests,
    /// which only touch the event counters and a queue drained by <see cref="Update"/>. Track writes are inline calls
    /// with <c>undo: false</c>; a busy reply skips the write for this frame and keeps a pending apply, so the pose is
    /// written as soon as the scene accepts writes again.
    /// </summary>
    public sealed class Player
    {
        readonly ISequenceHost host;
        Sequence seq;
        PlayState state;
        double time;
        bool pending, startInclusive;
        int[] order = new int[0];
        double[] eventTimes = new double[0];
        bool[] active = new bool[0], missingCounted = new bool[0];
        string[] keyJson = new string[0];
        readonly List<int> fired = new List<int>();
        readonly List<int> queuedEvents = new List<int>();
        readonly object gate = new object();
        readonly Queue<KeyValuePair<int, string>> eventFailures = new Queue<KeyValuePair<int, string>>();
        readonly HashSet<int> loggedEvents = new HashSet<int>();
        readonly HashSet<int> loggedTracks = new HashSet<int>();
        readonly Stopwatch sw = new Stopwatch();
        long eventsFired, eventErrors;
        WriteCounts writes;
        int generation;

        public Player(ISequenceHost host) { this.host = host ?? throw new ArgumentNullException(nameof(host)); }

        public Sequence Sequence => seq;
        public PlayState State => state;
        public double Time => time;
        public bool PendingApply => pending;
        public long EventsFired => Interlocked.Read(ref eventsFired);
        public long EventErrors => Interlocked.Read(ref eventErrors);
        public WriteCounts Writes => writes;
        /// <summary>Update times (ms) of the frames in which the player wrote or fired something.</summary>
        public readonly TimingRing UpdateMs = new TimingRing();

        /// <summary>Optional: measures each inline call (milliseconds), used by the measurement.</summary>
        public Action<string, double> CallTimed;

        public bool TrackActive(int i) => i >= 0 && i < active.Length && active[i];

        /// <summary>Replaces the sequence (null clears it): stopped at time 0, every track active, a pending apply.</summary>
        public void SetSequence(Sequence s)
        {
            seq = s;
            generation++;
            state = PlayState.Stopped;
            time = 0;
            startInclusive = false;
            queuedEvents.Clear();
            int nt = s?.Tracks.Count ?? 0, ne = s?.Events.Count ?? 0;
            active = new bool[nt];
            missingCounted = new bool[nt];
            keyJson = new string[nt];
            for (int i = 0; i < nt; i++)
            {
                active[i] = true;
                if (s.Tracks[i].Kind == TrackKind.Transform) keyJson[i] = JsonConvert.ToString(s.Tracks[i].Key);
            }
            eventTimes = new double[ne];
            order = new int[ne];
            for (int i = 0; i < ne; i++) { eventTimes[i] = s.Events[i].T; order[i] = i; }
            var times = eventTimes;
            Array.Sort(order, (a, b) => times[a] != times[b] ? times[a].CompareTo(times[b]) : a.CompareTo(b));
            pending = s != null && nt > 0;
            lock (gate)
            {
                eventFailures.Clear();
                loggedEvents.Clear();
            }
            loggedTracks.Clear();
        }

        /// <summary>Plays from <paramref name="from"/>, else from the current time (0 when stopped or ended).</summary>
        public void Play(double? from = null)
        {
            if (seq == null) return;
            if (from.HasValue)
            {
                time = ClampTime(from.Value);
                startInclusive = true;
            }
            else if (state == PlayState.Stopped || state == PlayState.Ended)
            {
                time = 0;
                startInclusive = true;
            }
            else if (state == PlayState.Playing) return;
            state = PlayState.Playing;
            pending = true;
        }

        /// <summary>Holds the current time (a stopped or ended sequence stays as it is).</summary>
        public void Pause()
        {
            if (state == PlayState.Playing) state = PlayState.Paused;
        }

        /// <summary>Stopped at time 0 with a pending apply of the first pose.</summary>
        public void Stop()
        {
            if (seq == null) return;
            state = PlayState.Stopped;
            time = 0;
            startInclusive = false;
            queuedEvents.Clear();
            pending = true;
        }

        /// <summary>
        /// Moves to <paramref name="t"/> without firing events. A playing sequence goes on from there; otherwise it is
        /// paused at <paramref name="t"/> (a stopped sequence is at 0 by definition).
        /// </summary>
        public void Seek(double t)
        {
            if (seq == null) return;
            time = ClampTime(t);
            startInclusive = false;
            if (state != PlayState.Playing) state = PlayState.Paused;
            pending = true;
        }

        double ClampTime(double t) => double.IsNaN(t) ? 0 : Math.Max(0, Math.Min(seq.Duration, t));

        /// <summary>
        /// Steps taken while the scene is paused (<paramref name="count"/> frames of <paramref name="dt"/>): a playing
        /// sequence advances by count × dt; the events passed and the pose are sent at the next update.
        /// </summary>
        public void Step(int count, double dt)
        {
            if (seq == null || state != PlayState.Playing || count <= 0 || !(dt > 0)) return;
            Advance(count * dt, queuedEvents);
            pending = true;
        }

        void Advance(double dt, List<int> into)
        {
            time = PlayClock.Advance(time, dt, seq.Duration, seq.Loop, startInclusive, eventTimes, order, into, out bool ended);
            startInclusive = false;
            if (ended)
            {
                state = PlayState.Ended;
                time = seq.Duration;
            }
        }

        /// <summary>
        /// One frame: queued step events are sent, a playing sequence advances by <paramref name="sceneDt"/> (0 while the
        /// scene is paused) and sends the events it passed, then every active track is written when the time moved or
        /// an apply is pending.
        /// </summary>
        public void Update(double sceneDt)
        {
            DrainFailures();
            if (seq == null) return;
            sw.Restart();
            bool worked = false;
            if (queuedEvents.Count > 0)
            {
                foreach (int i in queuedEvents) Fire(i);
                queuedEvents.Clear();
                worked = true;
            }
            bool moved = false;
            if (state == PlayState.Playing && sceneDt > 0)
            {
                fired.Clear();
                double before = time;
                Advance(sceneDt, fired);
                foreach (int i in fired) Fire(i);
                moved = time != before || fired.Count > 0 || state == PlayState.Ended;
                worked |= fired.Count > 0;
            }
            if (moved || pending)
            {
                pending = !WriteAll();
                worked = true;
            }
            sw.Stop();
            if (worked) UpdateMs.Add(sw.Elapsed.TotalMilliseconds);
        }

        void Fire(int index)
        {
            var ev = seq.Events[index];
            int gen = generation;
            Interlocked.Increment(ref eventsFired);
            host.CallAsync(ev.RequestJson, reply => OnEventReply(gen, index, reply));
        }

        void OnEventReply(int gen, int index, string reply)
        {
            if (IsOk(reply)) return;
            Interlocked.Increment(ref eventErrors);
            lock (gate) { if (gen == generation) eventFailures.Enqueue(new KeyValuePair<int, string>(index, reply)); }
        }

        void DrainFailures()
        {
            List<KeyValuePair<int, string>> list = null;
            lock (gate)
            {
                while (eventFailures.Count > 0)
                {
                    var f = eventFailures.Dequeue();
                    if (loggedEvents.Add(f.Key)) (list ?? (list = new List<KeyValuePair<int, string>>())).Add(f);
                }
            }
            if (list == null) return;
            foreach (var f in list)
                host.Log("event " + f.Key + " (" + seq?.Events[f.Key].Cmd + ") failed: " + Code(f.Value) + " " + Message(f.Value));
        }

        /// <summary>Writes every active track at the current time; false when a write was refused as busy.</summary>
        bool WriteAll()
        {
            bool allTaken = true;
            for (int i = 0; i < seq.Tracks.Count; i++)
            {
                if (!active[i]) continue;
                var tr = seq.Tracks[i];
                string req = tr.Kind == TrackKind.Transform ? TransformRequest(i, Interp.EvalTransform(tr, time)) : CameraRequest(Interp.EvalCamera(tr, time));
                string reply;
                if (CallTimed != null)
                {
                    long t0 = Stopwatch.GetTimestamp();
                    reply = host.Call(req);
                    CallTimed(tr.Kind == TrackKind.Transform ? "obj.transform" : "camera.set", (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
                }
                else reply = host.Call(req);
                if (IsOk(reply)) { writes.Done++; continue; }
                string code = Code(reply);
                if (code == "busy") { writes.Busy++; allTaken = false; }
                else if (code == "not_found" && tr.Kind == TrackKind.Transform)
                {
                    active[i] = false;
                    if (!missingCounted[i]) { missingCounted[i] = true; writes.MissingNode++; }
                }
                else
                {
                    writes.Error++;
                    if (loggedTracks.Add(i)) host.Log("track " + i + " write failed: " + code + " " + Message(reply));
                }
            }
            return allTaken;
        }

        string TransformRequest(int track, TransformPose p)
        {
            var sb = new StringBuilder(160);
            sb.Append("{\"cmd\":\"obj.transform\",\"key\":").Append(keyJson[track]);
            sb.Append(",\"pos\":"); AppendV3(sb, p.Pos);
            sb.Append(",\"rot\":"); AppendV3(sb, p.Rot);
            sb.Append(",\"undo\":false}");
            return sb.ToString();
        }

        static string CameraRequest(CameraPose p)
        {
            var sb = new StringBuilder(160);
            sb.Append("{\"cmd\":\"camera.set\",\"target\":"); AppendV3(sb, p.Target);
            sb.Append(",\"rot\":"); AppendV3(sb, p.Rot);
            sb.Append(",\"dist\":").Append(Sequence.NumberText(p.Dist));
            sb.Append(",\"fov\":").Append(Sequence.NumberText(p.Fov));
            sb.Append(",\"undo\":false}");
            return sb.ToString();
        }

        static void AppendV3(StringBuilder sb, Vec3 v) =>
            sb.Append('[').Append(Sequence.NumberText(v.X)).Append(',').Append(Sequence.NumberText(v.Y)).Append(',').Append(Sequence.NumberText(v.Z)).Append(']');

        // ------------------------------------------------------------------ scene changes

        /// <summary>Transform tracks of removed nodes stop writing (they are kept).</summary>
        public void NodesRemoved(IEnumerable<string> ids)
        {
            if (seq == null || ids == null) return;
            foreach (var id in ids)
                for (int i = 0; i < seq.Tracks.Count; i++)
                    if (seq.Tracks[i].Kind == TrackKind.Transform && seq.Tracks[i].Key == id) active[i] = false;
        }

        /// <summary>Transform tracks of nodes that came back (an undo) write again, starting with a pending apply.</summary>
        public void NodesAdded(IEnumerable<string> ids)
        {
            if (seq == null || ids == null) return;
            foreach (var id in ids)
                for (int i = 0; i < seq.Tracks.Count; i++)
                    if (seq.Tracks[i].Kind == TrackKind.Transform && seq.Tracks[i].Key == id && !active[i])
                    {
                        active[i] = true;
                        missingCounted[i] = false;
                        pending = true;
                    }
        }

        /// <summary>A node that can never come back: its tracks stay inactive and count once as a missing node.</summary>
        public void NodeForgotten(string id)
        {
            if (seq == null || id == null) return;
            for (int i = 0; i < seq.Tracks.Count; i++)
                if (seq.Tracks[i].Kind == TrackKind.Transform && seq.Tracks[i].Key == id)
                {
                    active[i] = false;
                    if (!missingCounted[i]) { missingCounted[i] = true; writes.MissingNode++; }
                }
        }

        /// <summary>Clears the counters and timings (a new sequence keeps counting unless this is called).</summary>
        public void ResetCounters()
        {
            Interlocked.Exchange(ref eventsFired, 0);
            Interlocked.Exchange(ref eventErrors, 0);
            writes = default;
            UpdateMs.Clear();
        }

        public static string StateName(PlayState s)
        {
            switch (s)
            {
                case PlayState.Playing: return "playing";
                case PlayState.Paused: return "paused";
                case PlayState.Ended: return "ended";
                default: return "stopped";
            }
        }

        /// <summary>The status object as compact JSON text.</summary>
        public string StatusJson()
        {
            var sb = new StringBuilder(256);
            sb.Append("{\"state\":\"").Append(StateName(state)).Append('"');
            sb.Append(",\"time\":").Append(Sequence.NumberText(time));
            sb.Append(",\"duration\":").Append(Sequence.NumberText(seq?.Duration ?? 0));
            sb.Append(",\"loop\":").Append(seq != null && seq.Loop ? "true" : "false");
            sb.Append(",\"tracks\":[");
            if (seq != null)
                for (int i = 0; i < seq.Tracks.Count; i++)
                {
                    var tr = seq.Tracks[i];
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"kind\":\"").Append(tr.Kind == TrackKind.Transform ? "transform" : "camera").Append('"');
                    if (tr.Kind == TrackKind.Transform) sb.Append(",\"key\":").Append(keyJson[i]);
                    sb.Append(",\"active\":").Append(active[i] ? "true" : "false").Append('}');
                }
            sb.Append("],\"eventsFired\":").Append(EventsFired);
            sb.Append(",\"eventErrors\":").Append(EventErrors);
            sb.Append(",\"writes\":{\"done\":").Append(writes.Done).Append(",\"busy\":").Append(writes.Busy)
              .Append(",\"missingNode\":").Append(writes.MissingNode).Append(",\"error\":").Append(writes.Error).Append('}');
            sb.Append(",\"updateMs\":{\"median\":").Append(Sequence.NumberText(UpdateMs.Median))
              .Append(",\"max\":").Append(Sequence.NumberText(UpdateMs.Max)).Append("}}");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ replies

        /// <summary>True for a successful reply (the bridge writes <c>"ok"</c> first; anything else is parsed).</summary>
        public static bool IsOk(string reply)
        {
            if (reply == null) return false;
            if (reply.StartsWith("{\"ok\":true", StringComparison.Ordinal)) return true;
            if (reply.StartsWith("{\"ok\":false", StringComparison.Ordinal)) return false;
            try { return JObject.Parse(reply).Value<bool?>("ok") == true; }
            catch (Exception) { return false; }
        }

        /// <summary>The error code of a reply ("" when it has none or cannot be read).</summary>
        public static string Code(string reply)
        {
            try { return JObject.Parse(reply ?? "{}").Value<string>("code") ?? ""; }
            catch (Exception) { return ""; }
        }

        static string Message(string reply)
        {
            try { return JObject.Parse(reply ?? "{}").Value<string>("error") ?? ""; }
            catch (Exception) { return ""; }
        }
    }
}
