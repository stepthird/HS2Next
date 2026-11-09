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
//   Animation restart (Phase 6.9): at the first tick after a play from 0 (a play from stopped or ended, a play with from 0, the
//   recorder's restart; a resume at time 0 counts as one) every anime channel whose row its character already plays loads that row
//   again at time 0 (the winning time slot's time when one wins), as a switch to another row loads; later switches load only when
//   the row differs. A seek to 0 restarts nothing.
//   The same restart resets the dynamic chains (dynamic bones and the bust chains) of every cast character of the loaded scene script
//   (the Timeline's script marker) and of every character whose anime channel the restart loads, in that tick's motion step: before the
//   pose sources every active chain's bones go back to their start pose (stage "timeline.chains.restore", phase -1), so the pose sources
//   write over it; after the pose is written and before the chains run (stage "timeline.chains", phase 740) the chains drop their
//   history and start from the posed bones. Both stages are taken off again after they ran. A seek, a pause, a resume after time 0 and
//   a loop pass reset nothing.
// The frame recorder reaches the playback as IRecordedTimeline; the UI and the commands edit through TimelineService (Service).
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using HS2Next.Motion;
using HS2Next.Motion.DynamicBones;
using HS2Next.Save;
using HS2Next.Scenes.Overrides;
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
        TimelinePlayState tickState;   // the state and the time at the end of the last tick (the animation restart)
        double tickTime;

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
        /// <summary>Animations loaded again at time 0 by the restart of a play from 0 since the scene was opened.</summary>
        public long AnimationRestarts { get; private set; }
        /// <summary>Characters whose dynamic chains the restart of a play from 0 reset since the scene was opened.</summary>
        public long ChainResets { get; private set; }
        /// <summary>The characters (node ids) whose dynamic chains the last restart reset, in the order they were reset.</summary>
        public IReadOnlyList<string> LastChainResets => lastChainResets;

        readonly List<ChainResetStage> chainStages = new List<ChainResetStage>();
        readonly List<string> lastChainResets = new List<string>();

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
            DropChainStages(false);
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

        /// <summary>After the frame's motion: the chain resets that ran come off their characters' motion (never under a walk of its stages).</summary>
        public void LateApply() => DropChainStages(true);

        /// <summary>The realization was cleared (a rebuild of every node, or before a new or loaded scene): the values are set again.</summary>
        public void Clear() => player.MarkDirty();

        public void OnLoadedRealized() { }

        void OnSceneCleared()
        {
            try { player.Reset(null); } catch (Exception e) { Fault("Cleared", e); }
            compiledFrom = null;
            docDirty = true;
            EventsFired = EventErrors = AnimationRestarts = ChainResets = 0;
            DropChainStages(false);
            lastChainResets.Clear();
            LastEventError = null;
            tickState = TimelinePlayState.None;
            tickTime = 0;
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
            // a play from 0 since the last tick: playing at 0 with the values not set since (a seek sets them at once), and the last tick
            // did not end playing at 0 already
            bool fromZero = playing && player.Time == 0 && player.Dirty && !(tickState == TimelinePlayState.Playing && tickTime == 0);
            if (!(dt >= 0f) || float.IsInfinity(dt)) dt = 0f;
            player.Tick(dt);
            if (fromZero)
            {
                RestartAnimations();
                ResetChainsAtRestart();
            }
            tickState = player.State;
            tickTime = player.Time;
            if (playing) perf.Add((Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency);
        }

        /// <summary>
        /// The animation restart of a play from 0: every anime channel whose winning slot shows the row its character plays already loads
        /// that row again at time 0 (the winning time slot's time when one wins), as the layer loads a row that differs.
        /// </summary>
        void RestartAnimations()
        {
            var layer = Layer;
            var db = rt?.Services?.Db;
            if (layer == null || db == null) return;
            foreach (var ch in bound)
            {
                if (ch.Type != TimelineTrackType.Anime || !(ch.Tag is Held h) || !h.Has || h.Slot == null || h.Slot.IsReleased) continue;
                if (!ReferenceEquals(layer.WinnerOf(ch.NodeId, OverrideChannel.AnimeRef), h.Slot)) continue;
                var c = rt.FindChara(ch.NodeId);
                if (c == null || c.State != RealizationState.Built || !rt.IsInModel(c) || !c.Animation || !c.Animation.IsLoaded) continue;
                var a = h.Anime;
                var row = a.Source != null ? (db.TryGetAnime(a.Source, a.Group, a.Category, a.No, out var r) ? r : null) : db.FindAnime(a.Group, a.Category, a.No);
                if (row == null || c.Animation.Current != row) continue;   // another row loads at time 0 through the layer
                var tw = layer.WinnerOf(ch.NodeId, OverrideChannel.AnimeTime);
                try
                {
                    c.Animation.Load(row, tw != null ? (float)tw[0] : 0f);
                    c.WantSettle();
                    AnimationRestarts++;
                }
                catch (Exception e) { Fault("Restart", e); }
            }
        }

        /// <summary>
        /// The restart's reset of the dynamic chains: every cast character of the loaded scene script (the Timeline's script marker) and every
        /// character whose anime channel the restart loads gets two one-shot stages for this tick's motion step: before the pose sources its
        /// active chains' bones go back to their start pose; after the pose is written and before the chains run its dynamic bones and bust
        /// chains drop their history, so they start from the posed bones.
        /// </summary>
        void ResetChainsAtRestart()
        {
            lastChainResets.Clear();
            if (rt == null) return;
            var ids = new List<string>();
            var marker = rt.Model.Timeline?.Script;
            var script = marker != null ? rt.Model.Scripts?.Find(marker.Id) : null;
            if (script != null)
                foreach (var role in script.Roles)
                {
                    if (role == null || !role.IsChara) continue;
                    string n = script.CastOf(role.Role);
                    if (n != null && !ids.Contains(n)) ids.Add(n);
                }
            var layer = Layer;
            if (layer != null)
                foreach (var ch in bound)
                {
                    if (ch.Type != TimelineTrackType.Anime || !(ch.Tag is Held h) || !h.Has || h.Slot == null || h.Slot.IsReleased) continue;
                    if (!ReferenceEquals(layer.WinnerOf(ch.NodeId, OverrideChannel.AnimeRef), h.Slot)) continue;
                    if (!ids.Contains(ch.NodeId)) ids.Add(ch.NodeId);
                }
            foreach (string id in ids)
            {
                var c = rt.FindChara(id);
                if (c == null || c.State != RealizationState.Built || !rt.IsInModel(c) || c.InGraveyard) continue;
                var m = c.Motion;
                if (!m || (m.Get<DynamicBoneStage>() == null && m.Get<BustStage>() == null)) continue;
                Arm(id, m, true);
                Arm(id, m, false);
            }
        }

        void Arm(string id, CharaMotion m, bool restore)
        {
            ChainResetStage s = null;
            foreach (var x in chainStages)
                if (ReferenceEquals(x.Motion, m) && x.Restore == restore) { s = x; break; }
            if (s == null)
            {
                s = new ChainResetStage(this, id, m, restore);
                try { m.Add(s); }
                catch (Exception e) { Fault("ChainReset", e); return; }
                chainStages.Add(s);
            }
            s.Armed = true;
        }

        void ChainsReset(string nodeId)
        {
            ChainResets++;
            lastChainResets.Add(nodeId);
        }

        /// <summary>Takes the chain resets off their motion: those that ran (<paramref name="ranOnly"/>) or all; a motion that is gone just drops its own.</summary>
        void DropChainStages(bool ranOnly)
        {
            for (int i = chainStages.Count - 1; i >= 0; i--)
            {
                var s = chainStages[i];
                bool alive = s.Motion;
                if (ranOnly && alive && !s.Ran) continue;
                if (alive)
                {
                    try { s.Motion.Remove(s); }
                    catch (Exception e) { Fault("ChainReset", e); }
                }
                chainStages.RemoveAt(i);
            }
        }

        /// <summary>
        /// One of a character's two chain-reset stages of a restart, each run in the first step after it was armed, then nothing until the playback
        /// takes it off after the frame's motion. The restore stage (phase -1: after the restore stages, before the pose sources) puts every active
        /// chain's bones back to their start pose, so the pose sources write over it and no bone keeps a chain's last write. The reset stage
        /// (phase 740: after the joint correction, before the bust chains) drops the dynamic bones' and the bust chains' history: particles at
        /// the posed bones, no velocity.
        /// </summary>
        sealed class ChainResetStage : IMotionStage
        {
            public const string RestoreName = "timeline.chains.restore", ResetName = "timeline.chains";

            readonly TimelinePlayback owner;
            public readonly string NodeId;
            public readonly CharaMotion Motion;
            public readonly bool Restore;
            public bool Armed, Ran;

            public ChainResetStage(TimelinePlayback owner, string nodeId, CharaMotion motion, bool restore)
            {
                this.owner = owner;
                NodeId = nodeId;
                Motion = motion;
                Restore = restore;
            }

            public MotionPhase Phase => Restore ? (MotionPhase)(-1) : (MotionPhase)740;
            public string Name => Restore ? RestoreName : ResetName;
            public bool Enabled { get; set; } = true;

            public void Step(in MotionFrame frame)
            {
                if (!Armed) return;
                Armed = false;
                Ran = true;
                if (Restore)
                {
                    foreach (var st in Motion.GetAll<DynamicBoneStage>())
                        foreach (var c in st.Chains)
                            if (c.Active) c.RestorePose();
                    return;
                }
                foreach (var st in Motion.GetAll<DynamicBoneStage>()) st.Reset();
                foreach (var b in Motion.GetAll<BustStage>()) b.Reset();
                owner.ChainsReset(NodeId);
            }

            public void Reset() { }
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
