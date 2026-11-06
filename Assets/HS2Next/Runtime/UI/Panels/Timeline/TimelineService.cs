// The Timeline panel's one way into the scene's Timeline: what the panel reads (the play state, the play head, per track
// whether it drives its channel now or is suspended by an edit while paused) and what it changes (the document, through
// the scene bus as one undo entry per call; playback). The panel builds every document edit itself (a key added, moved,
// eased or deleted, a track added, renamed, reordered, retargeted or removed) and hands the result over as one track or one
// whole document with its history label, so each gesture is one entry whatever it touches. The service the panel uses is the
// scene's Timeline service (the timeline.* commands call the same one, and it works with the bridge off); a runtime without the
// Timeline's playback leaves the panel on the bus alone (edits only).
using System;
using System.Collections.Generic;
using HS2Next.Save;
using HS2Next.Api.Bridge;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Timeline;
using Newtonsoft.Json.Linq;

namespace HS2Next.UI.Panels
{
    /// <summary>The outcome of a Timeline call (an error is shown as a toast).</summary>
    public readonly struct TimelineResult
    {
        public readonly bool Ok;
        public readonly string Code, Message;

        TimelineResult(bool ok, string code, string message)
        {
            Ok = ok;
            Code = code;
            Message = message;
        }

        public static readonly TimelineResult Success = new TimelineResult(true, null, null);
        public static TimelineResult Fail(string code, string message) => new TimelineResult(false, code ?? "error", message ?? "");
        public static TimelineResult Of(CommandResult r) => r == null ? Fail("internal", "no result") : r.Ok ? Success : Fail(r.Code, r.Message);
        public override string ToString() => Ok ? "ok" : Code + ": " + Message;
    }

    /// <summary>
    /// What the panel calls. Main thread. Every document change is one entry of the scene's history (or merges with the
    /// previous one of the same merge key: a drag of a value in the key editor).
    /// </summary>
    public abstract class TimelineUiService
    {
        // ------------------------------------------------------------ read-outs

        /// <summary>The play state (None without a Timeline).</summary>
        public abstract TimelinePlayState State { get; }
        /// <summary>The play head in seconds.</summary>
        public abstract double Time { get; }
        /// <summary>The global pause holds the play head (the state stays Playing).</summary>
        public virtual bool ScenePaused => false;
        /// <summary>Play, pause, stop and seek work (the scene runtime has the Timeline's playback).</summary>
        public abstract bool PlaybackAvailable { get; }
        /// <summary>The track drives its channel now (it holds a value).</summary>
        public abstract bool Drives(string trackId);
        /// <summary>The track's channel shows an edit made while paused until the play head moves.</summary>
        public abstract bool Suspended(string trackId);

        // ------------------------------------------------------------ playback (no history)

        public abstract TimelineResult Play(double? from = null);
        public abstract TimelineResult Pause();
        /// <summary>Stops (the model shows again); with <paramref name="bake"/> first writes the shown values into the model as
        /// one entry «Bake Timeline pose».</summary>
        public abstract TimelineResult Stop(bool bake = false);
        public abstract TimelineResult Seek(double t);

        // ------------------------------------------------------------ edits (one entry each)

        /// <summary>Replaces the whole Timeline (null removes it) under <paramref name="label"/>.</summary>
        public abstract TimelineResult ReplaceDocument(TimelineDocument after, string label, string mergeKey = null);
        /// <summary>Replaces the track with <paramref name="after"/>'s id under <paramref name="label"/>.</summary>
        public abstract TimelineResult ReplaceTrack(TimelineTrack after, string label, string mergeKey = null);
        /// <summary>
        /// Adds a track like <paramref name="track"/> (type, node, bone, slot, label; its keys as given) at <paramref name="index"/>
        /// (null = the end); with <paramref name="keyAt"/> and no keys it gets one key there holding the value its channel shows
        /// now. One entry «Add track». <paramref name="newId"/> is the new track's id.
        /// </summary>
        public abstract TimelineResult AddTrack(TimelineTrack track, int? index, double? keyAt, out string newId);
        /// <summary>
        /// Keys each track of <paramref name="trackIds"/> at <paramref name="t"/> with the value its channel shows now (the
        /// existing key's ease kept, else the default; step types store step). One entry «Key».
        /// </summary>
        public abstract TimelineResult KeyShown(IReadOnlyList<string> trackIds, double t);

        // ------------------------------------------------------------ helpers the panel uses

        /// <summary>The track's channel key and the node it names exist (the type takes the node's kind).</summary>
        public static bool TargetExists(SceneModel model, TimelineTrack track)
        {
            if (track == null || !track.IsKnownType) return false;
            if (TimelineTypes.IsSceneWide(track.Type)) return true;
            var n = track.Key != null ? model?.Find(track.Key) : null;
            if (n == null) return false;
            var type = TimelineTrackTypes.FromStored(track.Type, track.Bone);
            return TimelineTrackTypes.Takes(type, n.Kind, track.Slot.HasValue);
        }
    }

    /// <summary>
    /// The service on the scene bus alone: document edits as TimelineEditCommand (Source User), the play head read from the
    /// recorder's view of the Timeline. Playback and the shown value need the scene's Timeline playback: until it is there
    /// they answer unavailable.
    /// </summary>
    public sealed class TimelineBusService : TimelineUiService
    {
        readonly UiContext ctx;

        public TimelineBusService(UiContext ctx) { this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx)); }

        HS2Next.Screenshots.IRecordedTimeline Rec => ctx.Runtime?.GetPart<HS2Next.Screenshots.IRecordedTimeline>();

        public override TimelinePlayState State
        {
            get
            {
                if (ctx.Model?.Timeline == null) return TimelinePlayState.None;
                var r = Rec;
                return r != null && r.Ended ? TimelinePlayState.Ended : TimelinePlayState.Stopped;
            }
        }

        public override double Time => Rec?.Time ?? 0;
        public override bool PlaybackAvailable => false;
        public override bool Drives(string trackId) => false;
        public override bool Suspended(string trackId) => false;

        static TimelineResult NoPlayback() => TimelineResult.Fail("unavailable", "the Timeline's playback is not available");

        public override TimelineResult Play(double? from = null) => NoPlayback();
        public override TimelineResult Pause() => NoPlayback();
        public override TimelineResult Stop(bool bake = false) => NoPlayback();
        public override TimelineResult Seek(double t) => NoPlayback();

        public override TimelineResult ReplaceDocument(TimelineDocument after, string label, string mergeKey = null)
        {
            var bus = ctx.Session?.Commands;
            if (bus == null) return TimelineResult.Fail("unavailable", "no scene");
            var cmd = after == null && label == TimelineEditCommand.LabelClear ? TimelineEditCommand.Clear() : TimelineEditCommand.ReplaceDocument(after, label ?? TimelineEditCommand.LabelTimeline);
            return TimelineResult.Of(bus.Execute(cmd, UiContext.User(null, mergeKey)));
        }

        public override TimelineResult ReplaceTrack(TimelineTrack after, string label, string mergeKey = null)
        {
            var bus = ctx.Session?.Commands;
            if (bus == null) return TimelineResult.Fail("unavailable", "no scene");
            if (after == null) return TimelineResult.Fail("bad_args", "no track");
            return TimelineResult.Of(bus.Execute(TimelineEditCommand.ReplaceTrack(after, label ?? TimelineEditCommand.LabelTrack), UiContext.User(null, mergeKey)));
        }

        public override TimelineResult AddTrack(TimelineTrack track, int? index, double? keyAt, out string newId)
        {
            newId = track?.Id;
            var doc = ctx.Model?.Timeline;
            if (doc == null) return TimelineResult.Fail("not_found", "the scene has no Timeline");
            if (track == null) return TimelineResult.Fail("bad_args", "no track");
            var after = doc.Clone();
            int i = index.HasValue ? Math.Max(0, Math.Min(after.Tracks.Count, index.Value)) : after.Tracks.Count;
            after.Tracks.Insert(i, track);
            return ReplaceDocument(after, TimelineEditCommand.LabelAddTrack);
        }

        public override TimelineResult KeyShown(IReadOnlyList<string> trackIds, double t) =>
            TimelineResult.Fail("unavailable", "the shown value needs the Timeline's playback");
    }

    /// <summary>
    /// The panel on the scene's Timeline service (SceneRuntime part TimelinePlayback): playback, the read-outs, the shown value and
    /// the edits as the timeline.* commands make them (Source User); refusals come back as results. A runtime without the part
    /// leaves the panel on <see cref="TimelineBusService"/>.
    /// </summary>
    public sealed class TimelinePlaybackService : TimelineUiService
    {
        readonly UiContext ctx;
        readonly TimelineBusService bus;
        int viewsFrame = -1;
        List<TimelineTrackView> views;

        public TimelinePlaybackService(UiContext ctx)
        {
            this.ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            bus = new TimelineBusService(ctx);
        }

        /// <summary>The scene's service now (the runtime is replaced on a reload), or null.</summary>
        public TimelineService Inner => ctx.Runtime?.GetPart<TimelinePlayback>()?.Service;

        static TimelineResult Run(Action a)
        {
            try { a(); return TimelineResult.Success; }
            catch (BridgeException e) { return TimelineResult.Fail(e.Code, e.Message); }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is SaveFormatException) { return TimelineResult.Fail("bad_args", e.Message); }
        }

        static TimelineResult Bool(bool ok) => ok ? TimelineResult.Success : TimelineResult.Fail("unavailable", "the scene has no Timeline");

        public override TimelinePlayState State => Inner?.State ?? bus.State;
        public override double Time => Inner?.Time ?? bus.Time;
        public override bool ScenePaused => Inner?.ScenePaused ?? false;
        public override bool PlaybackAvailable => Inner != null;

        TimelineTrackView View(string trackId)
        {
            var s = Inner;
            if (s == null || trackId == null) return null;
            int f = UnityEngine.Time.frameCount;
            if (views == null || viewsFrame != f) { views = s.Tracks(); viewsFrame = f; }
            foreach (var v in views) if (v.Track.Id == trackId) return v;
            return null;
        }

        public override bool Drives(string trackId) => View(trackId)?.HasValue ?? false;
        public override bool Suspended(string trackId) => View(trackId)?.Suspended ?? false;

        public override TimelineResult Play(double? from = null)
        {
            var s = Inner;
            if (s == null) return bus.Play(from);
            bool ok = false;
            var r = Run(() => ok = s.Play(from));
            return r.Ok ? Bool(ok) : r;
        }

        public override TimelineResult Pause()
        {
            var s = Inner;
            if (s == null) return bus.Pause();
            return Run(() => s.Pause());
        }

        public override TimelineResult Stop(bool bake = false)
        {
            var s = Inner;
            if (s == null) return bus.Stop(bake);
            return Run(() => s.Stop(bake));
        }

        public override TimelineResult Seek(double t)
        {
            var s = Inner;
            if (s == null) return bus.Seek(t);
            bool ok = false;
            var r = Run(() => ok = s.Seek(t));
            return r.Ok ? Bool(ok) : r;
        }

        public override TimelineResult ReplaceDocument(TimelineDocument after, string label, string mergeKey = null)
        {
            var s = Inner;
            if (s == null) return bus.ReplaceDocument(after, label, mergeKey);
            if (after == null && label == TimelineEditCommand.LabelClear) return Run(() => s.Clear());
            return Run(() => s.SetDocument(after, label ?? TimelineEditCommand.LabelTimeline, mergeKey));
        }

        public override TimelineResult ReplaceTrack(TimelineTrack after, string label, string mergeKey = null)
        {
            var s = Inner;
            if (s == null) return bus.ReplaceTrack(after, label, mergeKey);
            var doc = s.Document;
            int i = after != null ? doc?.IndexOfTrack(after.Id) ?? -1 : -1;
            if (i < 0) return TimelineResult.Fail("not_found", "no such track in the Timeline");
            var d = doc.Clone();
            d.Tracks[i] = after;
            return Run(() => s.SetDocument(d, label ?? TimelineEditCommand.LabelTrack, mergeKey));
        }

        public override TimelineResult AddTrack(TimelineTrack track, int? index, double? keyAt, out string newId)
        {
            newId = null;
            var s = Inner;
            if (s == null) return bus.AddTrack(track, index, keyAt, out newId);
            if (track == null) return TimelineResult.Fail("bad_args", "no track");
            string id = null;
            JToken keys = track.Keys != null && track.Keys.Count > 0 ? JObject.Parse(track.ToJson())["keys"] : null;
            var r = Run(() => id = s.TrackAdd(track.Type, track.Key, track.Bone, track.Slot, track.Label, track.Enabled, index, keys, keyAt));
            newId = id;
            return r;
        }

        public override TimelineResult KeyShown(IReadOnlyList<string> trackIds, double t)
        {
            var s = Inner;
            if (s == null) return bus.KeyShown(trackIds, t);
            if (trackIds == null || trackIds.Count == 0) return TimelineResult.Success;
            float ft = (float)t;
            if (trackIds.Count == 1) return Run(() => s.KeySet(trackIds[0], ft, null));
            // several tracks: one document with a key of the shown value on each (one entry «Key»)
            var doc = s.Document;
            if (doc == null) return TimelineResult.Fail("not_found", "the scene has no Timeline");
            var jo = doc.ToJObject();
            var tracks = jo["tracks"] as JArray;
            foreach (var id in trackIds)
            {
                int i = doc.IndexOfTrack(id);
                if (i < 0 || tracks == null || i >= tracks.Count) return TimelineResult.Fail("not_found", "no track " + id);
                var tr = doc.Tracks[i];
                if (ft < 0 || ft > doc.Duration) return TimelineResult.Fail("bad_args", "the time is outside the Timeline");
                var v = s.ShownValue(tr, out string why);
                if (v == null) return TimelineResult.Fail("unavailable", "no value shown for " + tr.Describe() + ": " + why);
                int at = tr.IndexOfTime(ft);
                string ease = TimelineTypes.IsStepOnly(tr.Type) ? TimelineEases.Step : at >= 0 ? tr.Keys[at].Ease : TimelineEases.Default;
                var keys = tracks[i]["keys"] as JArray ?? new JArray();
                var key = new JObject { ["t"] = (double)ft, ["ease"] = ease, ["v"] = v.DeepClone() };
                if (at >= 0) keys[at] = key; else keys.Add(key);
                tracks[i]["keys"] = keys;
            }
            return Run(() => s.SetDocument(TimelineDocument.FromToken(jo), TimelineEditCommand.LabelKey));
        }
    }
}
