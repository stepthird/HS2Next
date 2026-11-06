// A Timeline ready for playback: duration, loop and autoplay, the tracks (the first track of a channel drives it, a later
// one stays inactive) and the timed commands that can run, in firing order (by time, then by list order). An event runs
// when it is enabled and its request is valid: a `do` object with a non-empty `cmd`, no `id`, at most 64 KiB of JSON and a
// command that does not replace or edit the Timeline document itself.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Timeline
{
    /// <summary>A timed command as stored, the input of <see cref="CompiledTimeline"/>.</summary>
    public sealed class TimelineEventInput
    {
        public string Id;
        /// <summary>The stored time.</summary>
        public float T;
        public bool Enabled = true;
        /// <summary>The request object (any JSON as stored; an invalid one never runs).</summary>
        public JToken Do;
    }

    /// <summary>The rules of a timed command's request.</summary>
    public static class TimelineEventRules
    {
        /// <summary>The request id prefix of a timed command's run (followed by the event id).</summary>
        public const string RequestIdPrefix = "timeline:";

        /// <summary>
        /// Why <paramref name="doToken"/> cannot run as a timed command, or null when it can: not an object, no
        /// <c>cmd</c> (a non-empty string), an <c>id</c> member, a command that replaces or edits the Timeline, or more
        /// than 64 KiB of JSON.
        /// </summary>
        public static string Problem(JToken doToken)
        {
            if (!(doToken is JObject o)) return "the request is not an object";
            string why = TimelineEvent.CheckDo(o);
            if (why != null) return why;
            if (new TimelineEvent(null, 0, o).DoBytes > TimelineLimits.MaxDoBytes) return "the request is over " + TimelineLimits.MaxDoBytes + " bytes";
            return null;
        }

        /// <summary>
        /// The bridge request of a run: the members of <paramref name="doObject"/> in order, then <c>undo: false</c> unless it
        /// carries <c>undo</c>, then <c>id</c> = <c>timeline:&lt;event id&gt;</c>.
        /// </summary>
        public static string Request(JObject doObject, string eventId)
        {
            var r = new JObject();
            foreach (var p in doObject.Properties()) r.Add(p.Name, p.Value.DeepClone());
            if (r.Property("undo") == null) r.Add("undo", false);
            r["id"] = RequestIdPrefix + eventId;
            return r.ToString(Formatting.None);
        }
    }

    public sealed class CompiledTimeline
    {
        readonly List<CompiledTrack> tracks;
        readonly List<TimelineEventRun> runs;

        /// <summary>
        /// A timeline of <paramref name="duration"/> seconds (&gt; 0) with <paramref name="tracks"/> in display order (keys
        /// ascending) and the stored <paramref name="events"/> in list order.
        /// </summary>
        public CompiledTimeline(double duration, bool loop, bool autoplay, IEnumerable<CompiledTrack> tracks, IEnumerable<TimelineEventInput> events)
        {
            if (!(duration > 0) || double.IsInfinity(duration)) throw new ArgumentOutOfRangeException(nameof(duration), "a Timeline lasts more than 0 s");
            Duration = duration;
            Loop = loop;
            Autoplay = autoplay;
            this.tracks = new List<CompiledTrack>(tracks ?? new CompiledTrack[0]);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < this.tracks.Count; i++)
            {
                var t = this.tracks[i] ?? throw new ArgumentException("track " + i + " is null");
                t.Index = i;
                KeyCount += t.KeyCount;
                if (t.State == TimelineTrackState.UnknownType || t.State == TimelineTrackState.BadTarget) continue;
                // One track per channel: a later track of a channel (enabled or not) stays inactive.
                if (!seen.Add(TimelineChannel.KeyOf(t.NodeId, t.ChannelPath))) t.State = TimelineTrackState.DuplicateChannel;
            }
            runs = new List<TimelineEventRun>();
            int index = 0;
            foreach (var e in events ?? new TimelineEventInput[0])
            {
                if (e != null)
                {
                    EventCount++;
                    if (e.Enabled && e.Id != null && TimelineEventRules.Problem(e.Do) == null)
                        runs.Add(new TimelineEventRun(index, e.Id, e.T, TimelineEventRules.Request((JObject)e.Do, e.Id)));
                }
                index++;
            }
            // Firing order: by time, then by list order (a stable sort).
            runs.Sort((a, b) => a.T != b.T ? a.T.CompareTo(b.T) : a.Index.CompareTo(b.Index));
            RunTimes = new double[runs.Count];
            for (int i = 0; i < runs.Count; i++) RunTimes[i] = runs[i].T;
        }

        public double Duration { get; }
        public bool Loop { get; }
        public bool Autoplay { get; }
        public IReadOnlyList<CompiledTrack> Tracks => tracks;
        /// <summary>Keys of every track.</summary>
        public int KeyCount { get; }
        /// <summary>Stored events (every one, also those that never run).</summary>
        public int EventCount { get; }
        /// <summary>The events that can run, in firing order.</summary>
        public IReadOnlyList<TimelineEventRun> Runs => runs;

        internal double[] RunTimes { get; }
    }
}
