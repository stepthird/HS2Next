// The Timeline's playback in the scene runtime: a scene part (ISceneRuntimePart, ISceneTickPart; registered by
// SceneRuntime.AddStandardParts after the standard parts) that plays the scene's Timeline on the scene clock and lays its values
// over the scene through the override layer (owner "timeline", priority 0). It never writes the model: stopping shows the
// scene's own values again; only the bake (TimelineService.Stop with bake) writes the shown values, as one history entry.
//   Document: compiled again after the "timeline" change record (an edit, an undo or redo of one; the state is kept and the time
//   clamped); scene new / load: stopped (or none), then a Timeline with autoplay plays from 0 after the load.
//   Tick (once per clock frame with the clock's FrameDt, once per step with its dt, inside Apply): playing advances the play
//   head, sets every track's value and submits the crossed timed commands to the bridge queue (they run in the next pump, so
//   their effect shows one frame later); paused or ended sets the values again only after a change; stopped does nothing.
//   Timed commands: SubmitRequest (set by the app) queues the request; a reply with ok false counts in EventErrors and is logged
//   once per event.
//   Authoring: while paused or ended, a model edit of a channel a track drives clears that channel's value (the edit shows)
//   until the play head moves or the track's keys change.
//   Perf: the wall time of every tick while playing (the last 4,096), for timeline.status {perf}.
// The frame recorder reaches the playback as IRecordedTimeline; the UI and the commands edit through TimelineService (Service).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using HS2Next.Save;
using HS2Next.Scenes.Timeline;
using Newtonsoft.Json.Linq;
using Debug = UnityEngine.Debug;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>The last failure of a timed command.</summary>
    public sealed class TimelineEventError
    {
        public TimelineEventError(string eventId, string code, string message)
        {
            EventId = eventId;
            Code = code;
            Message = message;
        }

        /// <summary>The event's id.</summary>
        public string EventId { get; }
        /// <summary>The reply's error code.</summary>
        public string Code { get; }
        /// <summary>The reply's error message.</summary>
        public string Message { get; }
    }

    /// <summary>The playback's tick times while playing: a window of the last <see cref="Capacity"/> ticks, in microseconds.</summary>
    public sealed class TimelinePerfWindow
    {
        /// <summary>The ticks the window keeps.</summary>
        public const int Capacity = 4096;

        readonly double[] us = new double[Capacity];
        int count, next;

        /// <summary>Ticks in the window.</summary>
        public int Count => count;
        /// <summary>Ticks counted since the last reset (also those that left the window).</summary>
        public long Total { get; private set; }

        public void Add(double microseconds)
        {
            us[next] = microseconds;
            next = (next + 1) % Capacity;
            if (count < Capacity) count++;
            Total++;
        }

        public void Reset()
        {
            count = next = 0;
            Total = 0;
        }

        /// <summary>The median, the 95th percentile (nearest rank) and the largest tick time of the window; false when it is empty.</summary>
        public bool Stats(out double median, out double p95, out double max)
        {
            median = p95 = max = 0;
            if (count == 0) return false;
            var a = new double[count];
            Array.Copy(us, 0, a, 0, count);
            Array.Sort(a);
            median = (count & 1) == 1 ? a[count / 2] : (a[count / 2 - 1] + a[count / 2]) / 2;
            int rank = (int)Math.Ceiling(0.95 * count);
            p95 = a[Math.Max(0, Math.Min(count - 1, rank - 1))];
            max = a[count - 1];
            return true;
        }
    }

    public sealed partial class TimelinePlayback : ISceneRuntimePart, ISceneTickPart, HS2Next.Screenshots.IRecordedTimeline, ITimelineSink, IDisposable
    {
        /// <summary>The override owner of the Timeline's values.</summary>
        public const string Owner = "timeline";

        readonly TimelinePlayer player;
        readonly TimelinePerfWindow perf = new TimelinePerfWindow();
        readonly ConcurrentQueue<KeyValuePair<string, string>> replies = new ConcurrentQueue<KeyValuePair<string, string>>();
        readonly HashSet<string> loggedEvents = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> loggedFaults = new HashSet<string>(StringComparer.Ordinal);
        SceneRuntime rt;
        TimelineDocument compiledFrom;
        bool docDirty = true;
        TimelineService service;

        public TimelinePlayback()
        {
            player = new TimelinePlayer(this);
        }

        /// <summary>The scene runtime (null before Attach).</summary>
        public SceneRuntime Runtime => rt;
        /// <summary>The playback state machine (state, time, the compiled Timeline, channels, counters).</summary>
        public TimelinePlayer Player => player;
        /// <summary>The edits, the playback calls and the read-outs of the Timeline for the commands and the UI.</summary>
        public TimelineService Service => service ??= new TimelineService(this);
        /// <summary>The tick times while playing (timeline.status {perf}).</summary>
        public TimelinePerfWindow Perf => perf;

        /// <summary>
        /// Queues a timed command's request on the bridge (the app sets it; null: no bridge, the command fails). The task completes
        /// with the reply JSON.
        /// </summary>
        public Func<string, Task<string>> SubmitRequest { get; set; }

        /// <summary>Timed commands handed to the bridge since the scene was opened.</summary>
        public long EventsFired { get; private set; }
        /// <summary>Timed commands that failed since the scene was opened.</summary>
        public long EventErrors { get; private set; }
        /// <summary>The last failure of a timed command, or null.</summary>
        public TimelineEventError LastEventError { get; private set; }

        // ------------------------------------------------------------ ISceneRuntimePart

        public void Attach(SceneRuntime r)
        {
            rt = r;
            docDirty = true;
            var ev = r.Session.Events;
            ev.Cleared += OnSceneCleared;
            ev.Loaded += OnSceneLoaded;
            AttachSink();
        }

        public void Dispose()
        {
            if (rt == null) return;
            var ev = rt.Session.Events;
            ev.Cleared -= OnSceneCleared;
            ev.Loaded -= OnSceneLoaded;
            try { player.Reset(null); } catch (Exception e) { Fault("Dispose", e); }
            DetachSink();
        }

        public void OnChanged(SceneChangeBatch b)
        {
            if (b == null) return;
            foreach (var c in b.Changes)
                if (c.Kind == SceneChangeKind.SceneChanged && c.Path == TimelineEditCommand.ChangePath) docDirty = true;
            // structural and appearance changes re-evaluate; while paused or ended an edit of a driven channel suspends it
            try { player.OnModelChanged(b); } catch (Exception e) { Fault("OnModelChanged", e); }
        }

        public void Apply()
        {
            Sync();
            CollectReplies();
        }

        public void LateApply() { }

        /// <summary>The realization was cleared (a rebuild of every node, or before a new or loaded scene): the values are set again.</summary>
        public void Clear() => player.MarkDirty();

        public void OnLoadedRealized() { }

        void OnSceneCleared()
        {
            try { player.Reset(null); } catch (Exception e) { Fault("Cleared", e); }
            compiledFrom = null;
            docDirty = true;
            EventsFired = EventErrors = 0;
            LastEventError = null;
            loggedEvents.Clear();
            while (replies.TryDequeue(out _)) { }
            perf.Reset();
        }

        void OnSceneLoaded(SceneLoadedArgs a)
        {
            docDirty = true;
            Sync();
            try { player.Autoplay(); } catch (Exception e) { Fault("Autoplay", e); }
        }

        // ------------------------------------------------------------ the document

        /// <summary>Compiles the scene's Timeline again when it changed since the last compile (cheap otherwise).</summary>
        public void Sync()
        {
            if (rt == null) return;
            var doc = rt.Model.Timeline;
            if (!docDirty && ReferenceEquals(doc, compiledFrom)) return;
            docDirty = false;
            compiledFrom = doc;
            CompiledTimeline c = null;
            if (doc != null)
            {
                try { c = TimelineCompiler.Compile(doc); }
                catch (Exception e) { Fault("Compile", e); }
            }
            try { player.SetTimeline(c); } catch (Exception e) { Fault("SetTimeline", e); }
        }

        // ------------------------------------------------------------ ISceneTickPart

        public void Tick(float dt)
        {
            long t0 = Stopwatch.GetTimestamp();
            Sync();
            bool playing = player.State == TimelinePlayState.Playing;
            if (!(dt >= 0f) || float.IsInfinity(dt)) dt = 0f;
            player.Tick(dt);
            if (playing) perf.Add((Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency);
        }

        // ------------------------------------------------------------ timed commands

        /// <summary>Submits a crossed timed command to the bridge queue (ITimelineSink).</summary>
        public void Fire(TimelineEventRun run)
        {
            EventsFired++;
            string id = run.Id;
            Task<string> t;
            try { t = SubmitRequest?.Invoke(run.RequestJson); }
            catch (Exception e)
            {
                Failed(id, "internal", e.GetType().Name + ": " + e.Message);
                return;
            }
            if (t == null)
            {
                Failed(id, "unavailable", "the bridge is off: timed commands cannot run");
                return;
            }
            t.ContinueWith(x => replies.Enqueue(new KeyValuePair<string, string>(id, x.Status == TaskStatus.RanToCompletion ? x.Result : null)),
                TaskContinuationOptions.ExecuteSynchronously);
        }

        /// <summary>Counts the failures among the replies of the timed commands that arrived since the last call.</summary>
        public void CollectReplies()
        {
            while (replies.TryDequeue(out var kv))
            {
                string json = kv.Value;
                if (json == null)
                {
                    Failed(kv.Key, "internal", "no reply");
                    continue;
                }
                JObject o;
                try { o = JObject.Parse(json); }
                catch (Exception e)
                {
                    Failed(kv.Key, "internal", "unreadable reply: " + e.Message);
                    continue;
                }
                if (o.Value<bool?>("ok") == false) Failed(kv.Key, o.Value<string>("code") ?? "internal", o.Value<string>("error") ?? "");
            }
        }

        void Failed(string eventId, string code, string message)
        {
            EventErrors++;
            LastEventError = new TimelineEventError(eventId, code, message);
            if (loggedEvents.Add(eventId ?? "")) Debug.LogWarning("[timeline] timed command " + eventId + " failed: " + code + ": " + message);
        }

        // ------------------------------------------------------------ IRecordedTimeline

        bool HS2Next.Screenshots.IRecordedTimeline.Exists
        {
            get
            {
                Sync();
                return player.Timeline != null;
            }
        }

        double HS2Next.Screenshots.IRecordedTimeline.Duration => player.Duration;
        bool HS2Next.Screenshots.IRecordedTimeline.Loop => player.Timeline?.Loop ?? false;
        double HS2Next.Screenshots.IRecordedTimeline.Time => player.Time;
        bool HS2Next.Screenshots.IRecordedTimeline.Ended => player.State == TimelinePlayState.Ended;

        bool HS2Next.Screenshots.IRecordedTimeline.RestartForRecording()
        {
            Sync();
            if (player.Timeline == null) return false;
            player.Stop();
            return player.Play(0);
        }

        // ------------------------------------------------------------ faults

        void Fault(string what, Exception e)
        {
            if (loggedFaults.Add(what + "|" + e.GetType().Name)) Debug.LogWarning("[timeline] " + what + " failed: " + e);
        }
    }
}
