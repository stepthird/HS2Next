// The Timeline's playback state machine, without the engine. States: none (the scene has no Timeline), stopped (no value
// shown, time 0), playing (the time advances by the scene clock's frame time; the values are set every tick), paused (the
// values of the paused time held) and ended (not looping: the values at the duration held).
//   play {from?}: from stopped or ended at 0, from paused at the paused time, or at `from` (also while playing: a restart);
//     while playing without `from` nothing changes. The first advance after a start at s runs the events with s ≤ t; a
//     resume runs (t, n] only.
//   pause: playing → paused. stop: any state → stopped at 0, every Timeline value released.
//   seek {t}: t clamped to [0, duration]; playing stays playing, stopped and ended become paused; evaluated at once; no
//     event runs.
//   A document change keeps the state, clamps the time, releases the channels no track drives any more and evaluates
//     again at the next tick.
// Timed commands run only on the crossings of the advancing play head (never on seek, pause, stop, a resume's start, a
// load, an edit or an undo); they are handed to the sink after the frame's values are set, in crossing order.
// Authoring: while paused or ended, a model edit of a channel a track drives suspends that channel (its value is cleared
// so the edit shows) until the play head moves (play, seek, stop) or that track's keys change. While playing nothing is
// suspended.
using System;
using System.Collections.Generic;

namespace HS2Next.Scenes.Timeline
{
    public enum TimelinePlayState { None = 0, Stopped = 1, Playing = 2, Paused = 3, Ended = 4 }

    public sealed class TimelinePlayer
    {
        readonly ITimelineSink sink;
        readonly Dictionary<string, TimelineChannel> channels = new Dictionary<string, TimelineChannel>(StringComparer.Ordinal);
        readonly List<int> fired = new List<int>();
        readonly List<string> warnings = new List<string>();
        CompiledTimeline tl;
        TimelinePlayState state;
        double time;
        bool inclusiveStart, dirty;

        public TimelinePlayer(ITimelineSink sink)
        {
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        public TimelinePlayState State => state;
        /// <summary>The play head in seconds (0 when stopped or none).</summary>
        public double Time => time;
        /// <summary>The timeline played (null: none).</summary>
        public CompiledTimeline Timeline => tl;
        public double Duration => tl?.Duration ?? 0;
        /// <summary>The values need to be set again at the next tick (a document change, a model change of a target).</summary>
        public bool Dirty => dirty;
        /// <summary>Timed commands handed to the sink since this player was made.</summary>
        public long EventsFired { get; private set; }
        /// <summary>Loop passes the cap left out (their events did not run) since the last play, stop or document change.</summary>
        public long SkippedPasses { get; private set; }
        /// <summary>Playback warnings since the last stop or document replacement (distinct texts).</summary>
        public IReadOnlyList<string> Warnings => warnings;
        /// <summary>The channels of the current timeline's active tracks.</summary>
        public IEnumerable<TimelineChannel> Channels => channels.Values;

        /// <summary>Channels showing a Timeline value now (the tracks holding a slot).</summary>
        public int ValuesShown
        {
            get
            {
                int n = 0;
                foreach (var c in channels.Values) if (c.HasValue) n++;
                return n;
            }
        }

        /// <summary>Suspended channels.</summary>
        public int SuspendedCount
        {
            get
            {
                int n = 0;
                foreach (var c in channels.Values) if (c.Suspended) n++;
                return n;
            }
        }

        /// <summary>Active tracks whose target exists now (asks the sink).</summary>
        public int CountActiveTracks()
        {
            if (tl == null) return 0;
            int n = 0;
            foreach (var t in tl.Tracks) if (t.Active && t.Channel != null && TargetExists(t)) n++;
            return n;
        }

        /// <summary>The channel of an active track of the current timeline (null otherwise).</summary>
        public TimelineChannel ChannelOf(CompiledTrack track) => track?.Channel;

        /// <summary>The active track that drives a channel, or null.</summary>
        public CompiledTrack TrackOf(string nodeId, string channelPath)
        {
            if (tl == null) return null;
            foreach (var t in tl.Tracks)
                if (t.Active && t.Channel != null && string.Equals(t.NodeId, nodeId, StringComparison.Ordinal) && t.ChannelPath == channelPath) return t;
            return null;
        }

        // ---------- the document ----------

        /// <summary>
        /// A document change (an edit, an undo or redo of one, a build): <paramref name="next"/> replaces the timeline (null:
        /// the scene has no Timeline any more: every value released, state none). The state is kept (none becomes stopped),
        /// the time clamped to the new duration; channels no active track drives are released; a suspended channel stays
        /// suspended only while its track's keys are unchanged; the values are set again at the next tick.
        /// </summary>
        public void SetTimeline(CompiledTimeline next)
        {
            if (next == null)
            {
                if (tl != null) ReleaseEverything();
                tl = null;
                channels.Clear();
                state = TimelinePlayState.None;
                time = 0;
                inclusiveStart = dirty = false;
                warnings.Clear();
                SkippedPasses = 0;
                return;
            }
            var old = tl;
            var used = new Dictionary<string, TimelineChannel>(StringComparer.Ordinal);
            foreach (var t in next.Tracks)
            {
                t.Channel = null;
                if (!t.Active) continue;
                string key = TimelineChannel.KeyOf(t.NodeId, t.ChannelPath);
                if (!channels.TryGetValue(key, out var ch)) ch = new TimelineChannel(t.NodeId, t.ChannelPath, t.Type, t.ModelPath);
                if (ch.Suspended)
                {
                    var before = old?.Tracks != null ? FindActive(old, key) : null;
                    if (before == null || !before.SameKeys(t)) ch.Suspended = false;
                }
                t.Channel = ch;
                used[key] = ch;
            }
            foreach (var kv in channels)
            {
                if (used.ContainsKey(kv.Key)) continue;
                var ch = kv.Value;
                if (ch.HasValue) sink.Release(ch);
                ch.HasValue = false;
                ch.Suspended = false;
            }
            channels.Clear();
            foreach (var kv in used) channels[kv.Key] = kv.Value;
            tl = next;
            if (state == TimelinePlayState.None)
            {
                state = TimelinePlayState.Stopped;
                time = 0;
            }
            if (time > next.Duration) time = next.Duration;
            if (state != TimelinePlayState.Stopped) dirty = true;
        }

        static CompiledTrack FindActive(CompiledTimeline c, string key)
        {
            foreach (var t in c.Tracks)
                if (t.Active && TimelineChannel.KeyOf(t.NodeId, t.ChannelPath) == key) return t;
            return null;
        }

        /// <summary>
        /// The scene was cleared (new, load) with <paramref name="next"/> as its Timeline (null: none): stopped (or none) at 0,
        /// nothing suspended; the sink drops what it held (<see cref="ITimelineSink.ReleaseAll"/>; the host may have released
        /// the slots already). Autoplay is a separate call after the load.
        /// </summary>
        public void Reset(CompiledTimeline next)
        {
            if (tl != null || channels.Count > 0) sink.ReleaseAll();
            foreach (var ch in channels.Values) ch.HasValue = ch.Suspended = false;
            channels.Clear();
            tl = null;
            state = TimelinePlayState.None;
            time = 0;
            inclusiveStart = dirty = false;
            warnings.Clear();
            SkippedPasses = 0;
            if (next != null) SetTimeline(next);
        }

        // ---------- transport ----------

        /// <summary>
        /// Plays: from <paramref name="from"/> (clamped; also while playing, a restart), else from 0 when stopped or ended,
        /// from the paused time when paused; while playing without <paramref name="from"/> nothing changes. False when there
        /// is no Timeline.
        /// </summary>
        public bool Play(double? from = null)
        {
            if (tl == null) return false;
            if (from.HasValue)
            {
                time = Clamp(from.Value);
                inclusiveStart = true;
            }
            else if (state == TimelinePlayState.Stopped || state == TimelinePlayState.Ended)
            {
                time = 0;
                inclusiveStart = true;
            }
            else if (state == TimelinePlayState.Paused) inclusiveStart = false;
            else return true;
            if (state != TimelinePlayState.Playing) SkippedPasses = 0;
            state = TimelinePlayState.Playing;
            UnsuspendAll();
            dirty = true;
            return true;
        }

        /// <summary>Playing → paused (the values stay); otherwise nothing. True when it paused.</summary>
        public bool Pause()
        {
            if (state != TimelinePlayState.Playing) return false;
            state = TimelinePlayState.Paused;
            return true;
        }

        /// <summary>Any state but none → stopped at 0 with every Timeline value released.</summary>
        public void Stop()
        {
            if (tl == null) return;
            ReleaseEverything();
            state = TimelinePlayState.Stopped;
            time = 0;
            inclusiveStart = dirty = false;
            warnings.Clear();
            SkippedPasses = 0;
        }

        /// <summary>
        /// Moves the play head to <paramref name="t"/> (clamped to [0, duration]) and sets the values at once: playing stays
        /// playing, paused stays paused, stopped and ended become paused. No event runs. False when there is no Timeline.
        /// </summary>
        public bool Seek(double t)
        {
            if (tl == null) return false;
            time = Clamp(t);
            if (state == TimelinePlayState.Playing) inclusiveStart = false;
            else state = TimelinePlayState.Paused;
            UnsuspendAll();
            EvaluateAll();
            dirty = false;
            return true;
        }

        /// <summary>After a scene load: a Timeline with autoplay plays from 0 (its first advance runs the events at 0).</summary>
        public bool Autoplay()
        {
            if (tl == null || !tl.Autoplay) return false;
            return Play(0);
        }

        /// <summary>The values are set again at the next tick (a rebuild, a target that may have come back).</summary>
        public void MarkDirty() => dirty = true;

        /// <summary>
        /// One clock frame or step of <paramref name="dt"/> seconds. Playing: advances (dt ≤ 0 holds the time), sets every
        /// active track's value and hands the crossed events to the sink. Paused or ended: sets the values again only when
        /// dirty. Stopped: nothing.
        /// </summary>
        public void Tick(double dt)
        {
            if (tl == null) return;
            if (state == TimelinePlayState.Playing)
            {
                fired.Clear();
                var c = tl;
                if (dt > 0)
                {
                    time = TimelineCrossings.Advance(time, dt, c.Duration, c.Loop, inclusiveStart, c.RunTimes, c.RunTimes.Length, fired, out bool ended, out long skipped);
                    inclusiveStart = false;
                    if (skipped > 0)
                    {
                        SkippedPasses += skipped;
                        Warn(TimelineTexts.SkippedPasses);
                    }
                    if (ended) state = TimelinePlayState.Ended;
                }
                EvaluateAll();
                dirty = false;
                var runs = c.Runs;
                for (int i = 0; i < fired.Count; i++)
                {
                    EventsFired++;
                    sink.Fire(runs[fired[i]]);
                }
                return;
            }
            if (dirty && (state == TimelinePlayState.Paused || state == TimelinePlayState.Ended)) EvaluateAll();
            dirty = false;
        }

        // ---------- authoring ----------

        /// <summary>
        /// Suspends <paramref name="channel"/> (cleared: the model shows) while paused or ended; false while playing or
        /// stopped, for a channel this timeline does not drive, or when it is suspended already.
        /// </summary>
        public bool Suspend(TimelineChannel channel)
        {
            if (channel == null || (state != TimelinePlayState.Paused && state != TimelinePlayState.Ended)) return false;
            if (!channels.TryGetValue(channel.Key, out var ch) || !ReferenceEquals(ch, channel) || ch.Suspended) return false;
            ch.Suspended = true;
            if (ch.HasValue)
            {
                sink.Clear(ch);
                ch.HasValue = false;
            }
            return true;
        }

        /// <summary>
        /// A batch of model changes: structural and appearance changes make the values dirty; while paused or ended, a field
        /// write (any origin but a save capture) whose path overlaps a driven channel's model path suspends that channel.
        /// Returns the channels suspended.
        /// </summary>
        public int OnModelChanged(SceneChangeBatch batch)
        {
            if (tl == null || batch == null || batch.Origin == ChangeOrigin.Capture) return 0;
            int n = 0;
            bool paused = state == TimelinePlayState.Paused || state == TimelinePlayState.Ended;
            foreach (var c in batch.Changes)
            {
                if (c.Kind != SceneChangeKind.NodeChanged && c.Kind != SceneChangeKind.SceneChanged)
                {
                    dirty = true;
                    continue;
                }
                if (!paused || c.Path == null) continue;
                foreach (var t in tl.Tracks)
                {
                    var ch = t.Channel;
                    if (ch == null || ch.Suspended || t.ModelPath == null) continue;
                    if (!string.Equals(ch.NodeId, c.NodeId, StringComparison.Ordinal)) continue;
                    if (FieldPath.Overlaps(t.ModelPath, c.Path) && Suspend(ch)) n++;
                }
            }
            return n;
        }

        // ---------- evaluation ----------

        /// <summary>Sets every active track's value at the current time (paused, ended or playing; nothing when stopped).</summary>
        public void EvaluateNow()
        {
            if (tl == null || state == TimelinePlayState.Stopped || state == TimelinePlayState.None) return;
            EvaluateAll();
            dirty = false;
        }

        void EvaluateAll()
        {
            var c = tl;
            if (c == null) return;
            var tracks = c.Tracks;
            for (int i = 0; i < tracks.Count; i++)
            {
                var t = tracks[i];
                var ch = t.Channel;
                if (ch == null) continue;
                if (!TargetExists(t))
                {
                    if (ch.HasValue)
                    {
                        sink.Release(ch);
                        ch.HasValue = false;
                    }
                    continue;
                }
                if (ch.Suspended) continue;
                if (t.KeyCount == 0)
                {
                    if (ch.HasValue)
                    {
                        sink.Clear(ch);
                        ch.HasValue = false;
                    }
                    continue;
                }
                if (t.Evaluate(time, sink, ch)) ch.HasValue = true;
            }
        }

        /// <summary>True when the track's target is in the model and of a kind its type takes (scene-wide: always).</summary>
        bool TargetExists(CompiledTrack t)
        {
            if (TimelineTrackTypes.IsSceneWide(t.Type)) return true;
            var k = sink.KindOf(t.NodeId);
            return k.HasValue && t.Takes(k.Value);
        }

        void ReleaseEverything()
        {
            sink.ReleaseAll();
            foreach (var ch in channels.Values) ch.HasValue = ch.Suspended = false;
        }

        void UnsuspendAll()
        {
            foreach (var ch in channels.Values) ch.Suspended = false;
        }

        double Clamp(double t)
        {
            if (double.IsNaN(t) || t < 0) return 0;
            return t > tl.Duration ? tl.Duration : t;
        }

        void Warn(string text)
        {
            if (!warnings.Contains(text)) warnings.Add(text);
        }
    }

    /// <summary>Texts of the playback's warnings.</summary>
    public static class TimelineTexts
    {
        public const string SkippedPasses = "more than 100 loop passes in one frame: the time moved on and the events of the passes beyond 100 did not run";
    }
}
