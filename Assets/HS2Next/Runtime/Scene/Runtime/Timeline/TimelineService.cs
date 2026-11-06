// The Timeline in process: every edit, the playback calls and the read-outs that the timeline.* commands and the Timeline panel
// share (the panel works with the bridge off). Found as SceneRuntime.GetPart<TimelinePlayback>().Service; main thread.
//   Edits: each is one TimelineEditCommand on the scene bus (one history entry with its label; undo false: applied without one,
//   the history kept; inside an open transaction, a step of it). A refusal throws BridgeException with its code (bad_args,
//   not_found, wrong_kind, unavailable, unsupported, busy) and message, and nothing changes. Times are seconds as float32 (the
//   stored form); a key is matched by its stored time.
//   Playback: Play, Pause, Stop (with bake: the shown values written into the scene first, one history entry), Seek.
//   Reads: State, Time, Duration, Loop, Autoplay, ScenePaused, Tracks() (per track the compiled state, whether its target
//   exists, whether it shows a value, whether it is suspended, a missing bone, FK off for the bone), ShownValue (what a key made
//   now would store).
using System;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Api.Bridge;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes.Timeline;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>How an edit is made: who makes it, whether it is recorded, the strictness of the input, where warnings go.</summary>
    public sealed class TimelineCallOptions
    {
        /// <summary>The source of the bus operation (the panel: User; the commands: Bridge).</summary>
        public CommandSource Source = CommandSource.User;
        /// <summary>False: applied without a history entry (the history is kept).</summary>
        public bool Undo = true;
        /// <summary>An unknown member of a build input is refused instead of warned about.</summary>
        public bool Strict;
        /// <summary>Receives the warnings of the call (null: dropped).</summary>
        public Action<string> Warn;

        /// <summary>The panel's options: Source User, recorded.</summary>
        public static TimelineCallOptions User => new TimelineCallOptions();

        internal void Warning(string text) => Warn?.Invoke(text);
    }

    /// <summary>One track as the panel shows it.</summary>
    public sealed class TimelineTrackView
    {
        /// <summary>The track's index in the document.</summary>
        public int Index;
        /// <summary>The stored track (do not change it: edits go through the service).</summary>
        public TimelineTrack Track;
        /// <summary>The track as compiled for playback, or null (the scene has no compiled Timeline).</summary>
        public CompiledTrack Compiled;
        /// <summary>Active, Disabled, UnknownType, BadTarget, DuplicateChannel (the compiled track's state).</summary>
        public TimelineTrackState State;
        /// <summary>The track's node is in the scene (always true for camera and dof).</summary>
        public bool TargetExists;
        /// <summary>The track shows a value now.</summary>
        public bool HasValue;
        /// <summary>An edit made while paused shows instead of the track until the play head moves.</summary>
        public bool Suspended;
        /// <summary>A bone track whose character is built without that bone.</summary>
        public bool BoneMissing;
        /// <summary>A bone track whose bone the FK pose does not write now (FK off, or the bone's group off).</summary>
        public bool FkOff;
    }

    /// <summary>The result of <see cref="TimelineService.KeySet"/>.</summary>
    public sealed class TimelineKeySetResult
    {
        public string Track;
        /// <summary>The key's index in the track after the edit.</summary>
        public int Index;
        public float T;
        public string Ease;
        /// <summary>The stored value.</summary>
        public JToken V;
        /// <summary>Keys of the track after the edit.</summary>
        public int Keys;
    }

    /// <summary>The members <see cref="TimelineService.TrackSet"/> changes (a Set… flag false leaves that member as it is).</summary>
    public sealed class TimelineTrackChange
    {
        public bool SetKey;
        /// <summary>The new target node (an id of a node in the scene).</summary>
        public string Key;
        public bool SetBone;
        /// <summary>A rot track's FK bone; null: the node's own rotation.</summary>
        public string Bone;
        public bool SetSlot;
        /// <summary>An item colour track's slot 0-3.</summary>
        public int? Slot;
        public bool SetLabel;
        /// <summary>The display label; null: generated.</summary>
        public string Label;
        /// <summary>Enables or disables the track (null: unchanged).</summary>
        public bool? Enabled;
        /// <summary>The new place in the track list (null: unchanged).</summary>
        public int? Index;
    }

    public sealed class TimelineService
    {
        /// <summary>The length of a Timeline that an edit creates without being given one (seconds).</summary>
        public const float DefaultDuration = 10f;

        readonly TimelinePlayback pb;

        internal TimelineService(TimelinePlayback playback)
        {
            pb = playback;
        }

        public TimelinePlayback Playback => pb;
        SceneRuntime Rt => pb.Runtime ?? throw new BridgeException(BridgeCodes.NotReady, "the Timeline playback is not attached to a scene runtime");
        SceneSession Session => Rt.Session;
        SceneModel Model => Rt.Model;

        // ================================================================ reads

        /// <summary>The scene's Timeline as stored (null: none). Read only: edits go through this service.</summary>
        public TimelineDocument Document => pb.Runtime?.Model.Timeline;

        public TimelinePlayState State
        {
            get
            {
                pb.Sync();
                return pb.Player.State;
            }
        }

        /// <summary>The play head in seconds (0 when stopped or without a Timeline).</summary>
        public double Time => pb.Player.Time;
        /// <summary>Seconds (0 without a Timeline).</summary>
        public double Duration => Document?.Duration ?? 0;
        public bool Loop => Document?.Loop ?? false;
        public bool Autoplay => Document?.Autoplay ?? false;
        /// <summary>The global pause holds the scene clock (a playing Timeline stays playing and does not move).</summary>
        public bool ScenePaused => pb.Runtime?.Paused ?? false;

        /// <summary>Every track of the document in order, with its playback marks.</summary>
        public List<TimelineTrackView> Tracks()
        {
            pb.Sync();
            var list = new List<TimelineTrackView>();
            var doc = Document;
            if (doc == null) return list;
            var compiled = pb.Player.Timeline;
            for (int i = 0; i < doc.Tracks.Count; i++)
            {
                var t = doc.Tracks[i];
                var ct = compiled != null && i < compiled.Tracks.Count && compiled.Tracks[i].Id == t.Id ? compiled.Tracks[i] : null;
                var v = new TimelineTrackView { Index = i, Track = t, Compiled = ct, State = ct?.State ?? TimelineTrackState.UnknownType };
                v.TargetExists = t.Key == null || Model.Find(t.Key) != null;
                var ch = ct?.Channel;
                v.HasValue = ch != null && ch.HasValue;
                v.Suspended = ch != null && ch.Suspended;
                if (t.IsKnownType && t.Type == TimelineTypes.Rot && t.Bone != null && t.Key != null)
                    pb.BoneMarks(t.Key, t.Bone, out v.BoneMissing, out v.FkOff);
                list.Add(v);
            }
            return list;
        }

        /// <summary>The view of the track with this id, or null.</summary>
        public TimelineTrackView Track(string trackId)
        {
            foreach (var v in Tracks()) if (v.Track.Id == trackId) return v;
            return null;
        }

        /// <summary>
        /// The value a key of <paramref name="track"/> made now would store, in the stored form of its type (the winning override's
        /// value when one shows, else the scene's value), or null with <paramref name="why"/> when it cannot be read.
        /// </summary>
        public JToken ShownValue(TimelineTrack track, out string why)
        {
            pb.Sync();
            var k = pb.ShownKey(track, out why);
            if (k == null) return null;
            var t = new TimelineTrack(track.Id ?? JsonText.DerivedId("timeline/shown"), track.Type, track.Key) { Bone = track.Bone, Slot = track.Slot };
            k.T = 0f;
            t.Keys.Add(k);
            return JObject.Parse(t.ToJson())["keys"]?[0]?["v"];
        }

        /// <summary><see cref="ShownValue(TimelineTrack, out string)"/> of the track with this id (null when there is none).</summary>
        public JToken ShownValue(string trackId) => Document?.FindTrack(trackId) is TimelineTrack t ? ShownValue(t, out _) : null;

        // ================================================================ playback

        /// <summary>Plays from <paramref name="from"/>, else from 0 (stopped or ended) or the paused time; false without a Timeline.</summary>
        public bool Play(double? from = null)
        {
            pb.Sync();
            return pb.Player.Play(from);
        }

        /// <summary>Pauses a playing Timeline; false otherwise.</summary>
        public bool Pause()
        {
            pb.Sync();
            return pb.Player.Pause();
        }

        /// <summary>Moves the play head and shows the values at once (no timed command runs); false without a Timeline.</summary>
        public bool Seek(double t)
        {
            pb.Sync();
            return pb.Player.Seek(t);
        }

        /// <summary>
        /// Stops: the play head to 0, the scene's own values shown. With <paramref name="bake"/> (while playing, paused or ended) the
        /// values the Timeline shows are first written into the scene, one history entry «Bake Timeline pose» (the camera as the
        /// current view, without history). Returns the values written.
        /// </summary>
        public int Stop(bool bake = false, TimelineCallOptions o = null)
        {
            o ??= TimelineCallOptions.User;
            pb.Sync();
            int baked = 0;
            var st = pb.Player.State;
            if (bake && (st == TimelinePlayState.Playing || st == TimelinePlayState.Paused || st == TimelinePlayState.Ended)) baked = Bake(o);
            pb.Player.Stop();
            return baked;
        }

        int Bake(TimelineCallOptions o)
        {
            var warnings = new List<string>();
            var plan = pb.BakePlan(warnings);
            foreach (var w in warnings) o.Warning(w);
            bool any = false;
            foreach (var w in plan) if (!w.IsView) { any = true; break; }
            if (any)
            {
                var label = TimelineEditCommand.LabelBake;
                using (var tx = Session.Commands.BeginOrJoin(label, Exec(o, label)))
                {
                    if (!tx.IsOpen) throw new BridgeException(tx.Failure?.Code ?? BridgeCodes.Busy, tx.Failure?.Message ?? "the bus is busy");
                    foreach (var w in plan)
                        if (!w.IsView) Check(tx.Set(w.NodeId, w.Path, w.Value));
                    Check(tx.Commit());
                }
            }
            int n = 0;
            foreach (var w in plan)
            {
                if (w.IsView) Check(Session.Set(w.NodeId, w.Path, w.Value, Exec(o, "Camera")));
                n += w.Values;
            }
            return n;
        }

        // ================================================================ whole-document edits

        /// <summary>
        /// Replaces the Timeline with <paramref name="after"/> (null removes it), one history entry labelled <paramref name="label"/>
        /// (a TimelineEditCommand label: «Key», «Delete keys», «Move keys» for edits over several tracks). The service takes the
        /// object: build a fresh one (Document.Clone(), change it).
        /// </summary>
        public void SetDocument(TimelineDocument after, string label = TimelineEditCommand.LabelTimeline, string mergeKey = null, TimelineCallOptions o = null) =>
            Execute(TimelineEditCommand.ReplaceDocument(after, label), label, mergeKey, o);

        /// <summary>A new, empty Timeline of <paramref name="duration"/> seconds (replacing any), one entry «Timeline».</summary>
        public void New(float duration = DefaultDuration, TimelineCallOptions o = null)
        {
            CheckDuration(duration, "duration");
            SetDocument(new TimelineDocument { Duration = duration }, TimelineEditCommand.LabelTimeline, null, o);
        }

        /// <summary>Changes the length, loop or autoplay (one entry «Timeline»); a scene without a Timeline gets one.</summary>
        public void SetProps(float? duration, bool? loop, bool? autoplay, TimelineCallOptions o = null)
        {
            if (!duration.HasValue && !loop.HasValue && !autoplay.HasValue) return;
            var before = Document;
            var doc = before != null ? before.Clone() : new TimelineDocument { Duration = DefaultDuration };
            Apply(doc, duration, loop, autoplay);
            SetDocument(doc, TimelineEditCommand.LabelTimeline, null, o);
        }

        /// <summary>
        /// Replaces the Timeline with one in its stored form (<paramref name="timeline"/> a JSON object), the given members then
        /// applied over it; one entry «Timeline». What the file reader would repair is refused here (bad_args).
        /// </summary>
        public void SetStored(JToken timeline, float? duration, bool? loop, bool? autoplay, TimelineCallOptions o = null)
        {
            o ??= TimelineCallOptions.User;
            var doc = ReadStored(timeline, "timeline", o);
            for (int i = 0; i < doc.Tracks.Count; i++)
            {
                var t = doc.Tracks[i];
                if (t.Key != null && Model.Find(t.Key) == null)
                    o.Warning("timeline.tracks[" + I(i) + "] names node " + t.Key + ", which is not in the scene: kept, it shows nothing until the node is there");
            }
            Apply(doc, duration, loop, autoplay);
            SetDocument(doc, TimelineEditCommand.LabelTimeline, null, o);
        }

        static void Apply(TimelineDocument doc, float? duration, bool? loop, bool? autoplay)
        {
            if (duration.HasValue)
            {
                CheckDuration(duration.Value, "duration");
                doc.Duration = duration.Value;
            }
            if (loop.HasValue) doc.Loop = loop.Value;
            if (autoplay.HasValue) doc.Autoplay = autoplay.Value;
        }

        /// <summary>Stops and removes the Timeline (one entry «Clear Timeline»); false when the scene has none.</summary>
        public bool Clear(TimelineCallOptions o = null)
        {
            if (Document == null) return false;
            pb.Sync();
            pb.Player.Stop();
            Execute(TimelineEditCommand.Clear(), TimelineEditCommand.LabelClear, null, o);
            return true;
        }

        /// <summary>
        /// timeline.build: tracks in HS2's input vocabulary replace the Timeline's tracks (or are added with <paramref name="append"/>);
        /// <paramref name="events"/> (null: kept) replace the timed commands; then, with <paramref name="play"/>, stop and play from 0.
        /// One entry «Build Timeline»; any refused track or key builds nothing.
        /// </summary>
        public void Build(JToken tracks, float? duration, bool append, bool? loop, bool? autoplay, JToken events, bool play, TimelineCallOptions o = null)
        {
            o ??= TimelineCallOptions.User;
            var before = Document;
            var keep = append ? before : null;
            float d = duration ?? keep?.Duration ?? DefaultDuration;
            CheckDuration(d, "duration");
            var warnings = new List<string>();
            var ids = Session.Options.Ids;
            var host = new BuildHost(this, o);
            var parsed = TimelineBuildInput.ParseTracks(tracks, d, host, ids, o.Strict, warnings, keep?.Tracks);
            List<TimelineEvent> evs = null;
            if (events != null && events.Type != JTokenType.Null) evs = TimelineBuildInput.ParseEvents(events, d, ids, o.Strict, warnings);
            var doc = new TimelineDocument
            {
                Duration = d,
                Loop = loop ?? keep?.Loop ?? true,
                Autoplay = autoplay ?? keep?.Autoplay ?? false,
            };
            if (keep != null) foreach (var t in keep.Tracks) doc.Tracks.Add(t.Clone());
            doc.Tracks.AddRange(parsed);
            if (evs != null) doc.Events = evs;
            else if (before != null) foreach (var e in before.Events) doc.Events.Add(e.Clone());
            foreach (var w in warnings) o.Warning(w);
            SetDocument(doc, TimelineEditCommand.LabelBuild, null, o);
            pb.Sync();
            if (play)
            {
                pb.Player.Stop();
                pb.Player.Play(0);
            }
        }

        /// <summary>
        /// timeline.events: replaces the timed commands (<paramref name="clear"/> empties them first); a scene without a Timeline gets
        /// one of max(10, the latest time) seconds unless the result is empty. One entry «Timeline events». Returns their count.
        /// </summary>
        public int SetEvents(JToken events, bool clear, TimelineCallOptions o = null)
        {
            o ??= TimelineCallOptions.User;
            var before = Document;
            bool given = events != null && events.Type != JTokenType.Null;
            if (!clear && !given) return before?.Events.Count ?? 0;
            var warnings = new List<string>();
            var evs = given ? TimelineBuildInput.ParseEvents(events, before?.Duration, Session.Options.Ids, o.Strict, warnings) : new List<TimelineEvent>();
            foreach (var w in warnings) o.Warning(w);
            if (before == null && evs.Count == 0) return 0;
            TimelineDocument doc;
            if (before != null) doc = before.Clone();
            else
            {
                float last = 0f;
                foreach (var e in evs) last = Math.Max(last, e.T);
                if (last > TimelineLimits.MaxDuration)
                    throw Bad("events", "times within [0, " + F(TimelineLimits.MaxDuration) + "]", "a timed command at " + F(last) + " s is past the longest Timeline");
                doc = new TimelineDocument { Duration = Math.Max(DefaultDuration, last) };
            }
            doc.Events = evs;
            int n = evs.Count;
            SetDocument(doc, TimelineEditCommand.LabelEvents, null, o);
            return n;
        }

        // ================================================================ tracks

        /// <summary>The index of the track with this id (not_found naming <paramref name="arg"/> otherwise).</summary>
        public int IndexOf(string trackId, string arg = "track")
        {
            var doc = Required(arg);
            int i = doc.IndexOfTrack(trackId);
            if (i >= 0) return i;
            var cand = new List<string>();
            foreach (var t in doc.Tracks) { if (cand.Count == 5) break; cand.Add(t.Id + " " + t.Describe()); }
            throw new BridgeException(BridgeError.NotFound("the Timeline has no track " + trackId, arg, cand));
        }

        /// <summary>
        /// Adds a track (one entry «Add track»; a scene without a Timeline gets one): <paramref name="keys"/> in the stored form, or,
        /// with <paramref name="firstKeyAt"/> and no keys, one key at that time with the value shown now. One track per channel.
        /// Returns the new track's id; <paramref name="index"/> is its place (null: the end).
        /// </summary>
        public string TrackAdd(string type, string key, string bone, int? slot, string label = null, bool enabled = true, int? index = null,
            JToken keys = null, double? firstKeyAt = null, TimelineCallOptions o = null)
        {
            o ??= TimelineCallOptions.User;
            if (type == null) throw new BridgeException(BridgeError.Missing("type", ArgTypes.Enum(TimelineTrackTypes.StoredNames)));
            if (!TimelineTypes.IsKnown(type)) throw Bad("type", ArgTypes.Enum(TimelineTrackTypes.StoredNames), "unknown track type \"" + type + "\"");
            if (bone != null && type != TimelineTypes.Rot) throw Bad("bone", null, "only rot tracks name a bone");
            if (bone != null && bone.Length == 0) throw Bad("bone", "a bone (transform) name");
            if (slot != null && type != TimelineTypes.Color) throw Bad("slot", null, "only color tracks name a slot");
            if (slot != null && (slot < 0 || slot > TimelineTrackTypes.MaxColorSlot)) throw Bad("slot", "an integer 0-3");
            if (TimelineTypes.IsSceneWide(type))
            {
                if (key != null) throw Bad("key", null, type + " tracks name no node");
            }
            else
            {
                if (key == null) throw new BridgeException(BridgeError.Missing("key", ArgTypes.Node));
                var n = Model.Find(key) ?? throw new BridgeException(BridgeError.NotFound("no node " + key + " in the scene", "key"));
                CheckTarget(type, bone, slot, n, "key");
            }
            if (label != null && label.Length > TimelineLimits.MaxText) throw Bad("label", null, "a label holds at most " + TimelineLimits.MaxText + " characters");
            if (keys != null && keys.Type != JTokenType.Null && keys.Type != JTokenType.Array) throw Bad("keys", ArgTypes.ArrayOf(ArgTypes.Object));
            var before = Document;
            var doc = before != null ? before.Clone() : new TimelineDocument { Duration = DefaultDuration };
            var obj = new JObject { ["id"] = Session.NewId(), ["type"] = type, ["key"] = J(key), ["bone"] = J(bone), ["enabled"] = enabled, ["label"] = J(label) };
            if (slot != null) obj["slot"] = slot.Value;
            obj["keys"] = keys is JArray ka ? (JArray)ka.DeepClone() : new JArray();
            var track = ReadTrack(obj, doc.Duration, "keys", o);
            CheckKeyTimes(track, doc, "keys");
            if (track.Keys.Count == 0 && firstKeyAt.HasValue)
            {
                float t = (float)firstKeyAt.Value;
                CheckTime(t, doc, "t");
                pb.Sync();
                var k = pb.ShownKey(track, out string why) ?? throw new BridgeException(BridgeCodes.Unavailable, "no value shown for the new track (" + track.Describe() + "): " + why);
                k.T = t;
                k.Ease = TimelineTypes.IsStepOnly(type) ? TimelineEases.Step : TimelineEases.Default;
                track.Keys.Add(k);
            }
            if (doc.IndexOfChannel(track.ChannelKey) >= 0) throw Bad("type", null, "the channel has a track (" + track.Describe() + ")");
            int at = index ?? doc.Tracks.Count;
            if (at < 0 || at > doc.Tracks.Count) throw Bad("index", "0-" + I(doc.Tracks.Count));
            doc.Tracks.Insert(at, track);
            string id = track.Id;
            SetDocument(doc, TimelineEditCommand.LabelAddTrack, null, o);
            return id;
        }

        /// <summary>Changes a track's members (one entry «Track»); returns the names of the members changed (none: nothing done).</summary>
        public List<string> TrackSet(string trackId, TimelineTrackChange change, TimelineCallOptions o = null)
        {
            var doc = Required("track");
            int i = IndexOf(trackId);
            var t = KnownTrack(doc, i, "track");
            var nt = t.Clone();
            var changed = new List<string>();
            bool target = false;
            if (change.SetKey)
            {
                if (TimelineTypes.IsSceneWide(t.Type)) throw Bad("key", null, t.Type + " tracks name no node");
                if (change.Key == null) throw new BridgeException(BridgeError.Missing("key", ArgTypes.Node));
                if (Model.Find(change.Key) == null) throw new BridgeException(BridgeError.NotFound("no node " + change.Key + " in the scene", "key"));
                if (change.Key != nt.Key) { nt.Key = change.Key; changed.Add("key"); target = true; }
            }
            if (change.SetBone)
            {
                if (t.Type != TimelineTypes.Rot) throw Bad("bone", null, "only rot tracks name a bone");
                if (change.Bone != null && change.Bone.Length == 0) throw Bad("bone", "a bone (transform) name");
                if (!string.Equals(change.Bone, nt.Bone, StringComparison.Ordinal)) { nt.Bone = change.Bone; changed.Add("bone"); target = true; }
            }
            if (change.SetSlot)
            {
                if (t.Type != TimelineTypes.Color) throw Bad("slot", null, "only color tracks name a slot");
                if (change.Slot != null && (change.Slot < 0 || change.Slot > TimelineTrackTypes.MaxColorSlot)) throw Bad("slot", "an integer 0-3");
                if (change.Slot != nt.Slot) { nt.Slot = change.Slot; changed.Add("slot"); target = true; }
            }
            if (target && nt.Key != null && Model.Find(nt.Key) is TreeNode n) CheckTarget(nt.Type, nt.Bone, nt.Slot, n, "key");
            if (change.SetLabel)
            {
                if (change.Label != null && change.Label.Length > TimelineLimits.MaxText) throw Bad("label", null, "a label holds at most " + TimelineLimits.MaxText + " characters");
                if (!string.Equals(change.Label, nt.Label, StringComparison.Ordinal)) { nt.Label = change.Label; changed.Add("label"); }
            }
            if (change.Enabled.HasValue && change.Enabled.Value != nt.Enabled) { nt.Enabled = change.Enabled.Value; changed.Add("enabled"); }
            if (change.Index.HasValue && (change.Index < 0 || change.Index >= doc.Tracks.Count)) throw Bad("index", "0-" + I(doc.Tracks.Count - 1));
            bool move = change.Index.HasValue && change.Index.Value != i;
            if (move) changed.Add("index");
            if (changed.Count == 0) return changed;
            if (move)
            {
                var d2 = doc.Clone();
                d2.Tracks.RemoveAt(i);
                d2.Tracks.Insert(change.Index.Value, nt);
                SetDocument(d2, TimelineEditCommand.LabelTrack, null, o);
            }
            else Execute(TimelineEditCommand.ReplaceTrack(nt, TimelineEditCommand.LabelTrack), TimelineEditCommand.LabelTrack, null, o);
            return changed;
        }

        /// <summary>Removes tracks with their keys (one entry «Remove track»); returns the count.</summary>
        public int TrackRemove(IEnumerable<string> trackIds, TimelineCallOptions o = null)
        {
            var doc = Required("track");
            var which = new SortedSet<int>();
            foreach (var id in trackIds) which.Add(IndexOf(id, "tracks"));
            if (which.Count == 0) return 0;
            var d2 = doc.Clone();
            foreach (int k in which.Reverse()) d2.Tracks.RemoveAt(k);
            SetDocument(d2, TimelineEditCommand.LabelRemoveTrack, null, o);
            return which.Count;
        }

        // ================================================================ keys

        /// <summary>
        /// Sets the key at <paramref name="t"/> (one entry «Key»; consecutive edits with one <paramref name="mergeKey"/> merge):
        /// <paramref name="v"/> in the stored form, or null for the value shown now. <paramref name="ease"/> null keeps the key's
        /// own, else inout; anime, eyes and mouth always store step.
        /// </summary>
        public TimelineKeySetResult KeySet(string trackId, float t, JToken v, string ease = null, string mergeKey = null, TimelineCallOptions o = null)
        {
            o ??= TimelineCallOptions.User;
            var doc = Required("track");
            int i = IndexOf(trackId);
            var tr = KnownTrack(doc, i, "track");
            CheckTime(t, doc, "t");
            if (ease != null && !TimelineEases.IsKnown(ease)) throw Bad("ease", ArgTypes.Enum("step", "linear", "inout", "in", "out"), "unknown ease \"" + ease + "\"");
            var nt = tr.Clone();
            int at = nt.IndexOfTime(t);
            if (TimelineTypes.IsStepOnly(tr.Type)) ease = TimelineEases.Step;
            else if (ease == null) ease = at >= 0 ? nt.Keys[at].Ease : TimelineEases.Default;
            TimelineKey key;
            if (v != null && v.Type != JTokenType.Undefined)
            {
                var obj = StoredTrack(tr, new JArray(new JObject { ["t"] = (double)t, ["ease"] = ease, ["v"] = v.DeepClone() }));
                var read = ReadTrack(obj, doc.Duration, "v", o);
                if (read.Keys.Count != 1) throw Bad("v", "the stored form of a " + tr.Type + " value");
                key = read.Keys[0];
            }
            else
            {
                pb.Sync();
                key = pb.ShownKey(tr, out string why) ?? throw new BridgeException(BridgeCodes.Unavailable, "no value shown for track " + I(i) + " (" + tr.Describe() + "): " + why);
            }
            key.T = t;
            key.Ease = ease;
            if (at >= 0) nt.Keys[at] = key; else nt.Keys.Add(key);
            nt.SortKeys();
            int index = nt.IndexOfTime(t);
            var vOut = JObject.Parse(nt.ToJson())["keys"]?[index]?["v"];
            int count = nt.Keys.Count;
            Execute(TimelineEditCommand.ReplaceTrack(nt, TimelineEditCommand.LabelKey), TimelineEditCommand.LabelKey, mergeKey, o);
            return new TimelineKeySetResult { Track = tr.Id, Index = index, T = t, Ease = ease, V = vOut, Keys = count };
        }

        /// <summary>Deletes the keys at <paramref name="times"/> (one entry «Delete keys»); returns the count.</summary>
        public int KeyDelete(string trackId, IReadOnlyList<float> times, TimelineCallOptions o = null)
        {
            var doc = Required("track");
            int i = IndexOf(trackId);
            var tr = KnownTrack(doc, i, "track");
            CheckKeys(tr, i, times);
            var nt = tr.Clone();
            foreach (var x in times) nt.Keys.RemoveAt(nt.IndexOfTime(x));
            Execute(TimelineEditCommand.ReplaceTrack(nt, TimelineEditCommand.LabelDeleteKeys), TimelineEditCommand.LabelDeleteKeys, null, o);
            return times.Count;
        }

        /// <summary>
        /// Moves the keys at <paramref name="times"/> to <paramref name="to"/> (one key) or by <paramref name="by"/> seconds (one entry
        /// «Move keys»; consecutive moves with one <paramref name="mergeKey"/> merge, as a drag). Two keys never meet; every key stays
        /// within the Timeline. Returns the new times in the order given.
        /// </summary>
        public List<float> KeyMove(string trackId, IReadOnlyList<float> times, float? to, float? by, string mergeKey = null, TimelineCallOptions o = null)
        {
            var doc = Required("track");
            int i = IndexOf(trackId);
            var tr = KnownTrack(doc, i, "track");
            CheckKeys(tr, i, times);
            if (to.HasValue == by.HasValue) throw Bad("to", "one of to, by", to.HasValue ? "give to or by, not both" : "missing argument 'to' or 'by'");
            string arg = to.HasValue ? "to" : "by";
            var targets = new List<float>(times.Count);
            if (to.HasValue)
            {
                if (times.Count != 1) throw Bad("to", null, "to moves one key: give by to move several");
                targets.Add(to.Value);
            }
            else foreach (var x in times) targets.Add((float)((double)x + by.Value));
            foreach (var x in targets) CheckTime(x, doc, arg);
            var nt = tr.Clone();
            var moving = new List<TimelineKey>();
            foreach (var x in times) moving.Add(nt.Keys[nt.IndexOfTime(x)]);
            for (int k = 0; k < moving.Count; k++) moving[k].T = targets[k];
            var seen = new HashSet<float>();
            foreach (var k in nt.Keys)
                if (!seen.Add(k.T)) throw Bad(arg, null, "two keys of track " + I(i) + " would meet at " + F(k.T) + " s");
            nt.SortKeys();
            Execute(TimelineEditCommand.ReplaceTrack(nt, TimelineEditCommand.LabelMoveKeys), TimelineEditCommand.LabelMoveKeys, mergeKey, o);
            return targets;
        }

        /// <summary>
        /// Sets the ease of several keys over any tracks (track id, stored time) as one entry «Key»; anime, eyes and mouth keys stay
        /// step. Returns the keys changed.
        /// </summary>
        public int SetEase(IEnumerable<KeyValuePair<string, float>> keys, string ease, TimelineCallOptions o = null)
        {
            if (ease == null || !TimelineEases.IsKnown(ease)) throw Bad("ease", ArgTypes.Enum("step", "linear", "inout", "in", "out"), "unknown ease \"" + ease + "\"");
            var doc = Required("track");
            var d2 = doc.Clone();
            int n = 0;
            foreach (var kv in keys)
            {
                int i = IndexOf(kv.Key);
                var tr = KnownTrack(d2, i, "track");
                int at = tr.IndexOfTime(kv.Value);
                if (at < 0) throw new BridgeException(BridgeError.NotFound("track " + I(i) + " (" + tr.Describe() + ") has no key at " + F(kv.Value) + " s", "times"));
                if (TimelineTypes.IsStepOnly(tr.Type) || tr.Keys[at].Ease == ease) continue;
                tr.Keys[at].Ease = ease;
                n++;
            }
            if (n > 0) SetDocument(d2, TimelineEditCommand.LabelKey, null, o);
            return n;
        }

        // ================================================================ helpers

        TimelineDocument Required(string arg) => Document ?? throw new BridgeException(BridgeError.NotFound("the scene has no Timeline", arg));

        static TimelineTrack KnownTrack(TimelineDocument doc, int i, string arg)
        {
            var t = doc.Tracks[i];
            if (!t.IsKnownType) throw Bad(arg, null, "track " + I(i) + " has the unknown type \"" + t.Type + "\": it can only be removed");
            return t;
        }

        static void CheckKeys(TimelineTrack t, int index, IReadOnlyList<float> times)
        {
            if (times == null || times.Count == 0) throw Bad("times", ArgTypes.ArrayOf(ArgTypes.Float), "no times");
            var seen = new HashSet<float>();
            foreach (var x in times)
            {
                if (t.IndexOfTime(x) < 0) throw new BridgeException(BridgeError.NotFound("track " + I(index) + " (" + t.Describe() + ") has no key at " + F(x) + " s", times.Count == 1 ? "t" : "times"));
                if (!seen.Add(x)) throw Bad("times", null, "the time " + F(x) + " s is given twice");
            }
        }

        static void CheckDuration(float d, string arg)
        {
            if (!(d > 0f) || d > TimelineLimits.MaxDuration)
                throw Bad(arg, "seconds in (0, " + F(TimelineLimits.MaxDuration) + "]", "duration " + F(d) + " s is outside (0, " + F(TimelineLimits.MaxDuration) + "] s");
        }

        static void CheckTime(float t, TimelineDocument doc, string arg)
        {
            if (!(t >= 0f) || t > doc.Duration)
                throw Bad(arg, "a time within [0, " + F(doc.Duration) + "]", "time " + F(t) + " s is outside the Timeline [0, " + F(doc.Duration) + "] s");
        }

        static void CheckKeyTimes(TimelineTrack t, TimelineDocument doc, string arg)
        {
            foreach (var k in t.Keys) CheckTime(k.T, doc, arg);
        }

        /// <summary>The kind check of a track's target: wrong_kind when the type does not take the node.</summary>
        static void CheckTarget(string type, string bone, int? slot, TreeNode n, string arg)
        {
            var tt = TimelineTrackTypes.FromStored(type, bone);
            if (TimelineTrackTypes.IsSceneWide(tt)) return;
            if (type == TimelineTypes.Color)
            {
                if (n.Kind == NodeKind.Item && slot == null) throw Bad("slot", "an integer 0-3", "a colour track of an item names its colour slot (0-3)");
                if (n.Kind == NodeKind.Light && slot != null) throw Bad("slot", null, "a light has one colour: its track names no slot");
            }
            if (!TimelineTrackTypes.Takes(tt, n.Kind, slot.HasValue))
                throw new BridgeException(BridgeError.WrongKind("a " + type + (bone != null ? " (bone)" : "") + " track drives " + TimelineTrackTypes.KindsText(tt, slot.HasValue) +
                    ", not " + KindName(n.Kind), arg, TimelineTrackTypes.KindsText(tt, slot.HasValue)));
        }

        static string KindName(NodeKind k)
        {
            switch (k)
            {
                case NodeKind.Chara: return "a character";
                case NodeKind.Item: return "an item";
                case NodeKind.Light: return "a light";
                case NodeKind.Folder: return "a folder";
                case NodeKind.Route: return "a route";
                case NodeKind.Camera: return "a camera";
                default: return "a node of another kind";
            }
        }

        static JObject StoredTrack(TimelineTrack t, JArray keys)
        {
            var o = new JObject { ["id"] = t.Id ?? JsonText.DerivedId("timeline/shown"), ["type"] = t.Type, ["key"] = J(t.Key), ["bone"] = J(t.Bone) };
            if (t.Slot != null) o["slot"] = t.Slot.Value;
            o["keys"] = keys;
            return o;
        }

        /// <summary>
        /// Reads a Timeline in its stored form with the file reader's rules: a type error is bad_args; a repair that would drop or
        /// change data (a warning of the reader) is bad_args too, since an edit never stores what a reader would repair; the reader's
        /// notes (a derived track id, a time outside the Timeline) become warnings.
        /// </summary>
        static TimelineDocument ReadStored(JToken token, string arg, TimelineCallOptions o, string what = "a Timeline in its stored form")
        {
            var findings = new FindingList();
            TimelineDocument doc;
            try { doc = TimelineDocument.FromToken(token, findings, TimelineDocument.MemberName); }
            catch (SaveFormatException e) { throw Bad(arg, what, e.Detail ?? e.Message); }
            foreach (var f in findings)
                if (f.Severity != FindingSeverity.Info)
                    throw Bad(arg, what, f.Message + " (" + f.Code + (string.IsNullOrEmpty(f.Path) ? "" : " at " + f.Path) + ")");
            foreach (var f in findings)
                if (f.Code != TimelineFindings.KeyOrder) o.Warning(arg + ": " + f.Message);
            return doc;
        }

        /// <summary>One track in its stored form, read with the reader's rules inside a Timeline of <paramref name="duration"/>.</summary>
        static TimelineTrack ReadTrack(JObject track, float duration, string arg, TimelineCallOptions o)
        {
            var wrap = new JObject { ["duration"] = (double)duration, ["tracks"] = new JArray(track) };
            var doc = ReadStored(wrap, arg, o, "keys {t, ease, v} in the stored form of a " + (string)track["type"] + " track");
            return doc.Tracks[0];
        }

        void Execute(TimelineEditCommand cmd, string label, string mergeKey, TimelineCallOptions o)
        {
            o ??= TimelineCallOptions.User;
            var e = Exec(o, label);
            e.MergeKey = mergeKey;
            Check(Session.Commands.Execute(cmd, e));
            pb.Sync();
        }

        /// <summary>The bus options of an edit: recorded unless undo is false (inside an open transaction always a step of it).</summary>
        ExecuteOptions Exec(TimelineCallOptions o, string label) => new ExecuteOptions
        {
            Record = o.Undo || Session.Commands.ActiveTransaction != null ? HistoryMode.Record : HistoryMode.None,
            Source = o.Source,
            Label = label,
        };

        static CommandResult Check(CommandResult r)
        {
            if (r == null) throw new BridgeException(BridgeCodes.Internal, "no result");
            if (r.Status == CommandStatus.Error) throw new BridgeException(r.Code ?? BridgeCodes.Internal, r.Message);
            return r;
        }

        /// <summary>A string member's JSON: the text, or JSON null (an implicit null string would be a string token holding null).</summary>
        static JToken J(string s) => s != null ? (JToken)s : JValue.CreateNull();

        static BridgeException Bad(string arg, string expected, string message = null) => new BridgeException(BridgeError.BadArgs(arg, expected, message));

        static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        static string I(int v) => v.ToString(CultureInfo.InvariantCulture);

        /// <summary>What the build input needs from the scene: node kinds, animation rows, eye patterns.</summary>
        sealed class BuildHost : ITimelineBuildHost
        {
            readonly TimelineService s;
            readonly TimelineCallOptions o;

            public BuildHost(TimelineService s, TimelineCallOptions o)
            {
                this.s = s;
                this.o = o;
            }

            public NodeKind? KindOf(string nodeId) => s.Model.Find(nodeId)?.Kind;

            public AnimeRef ResolveAnime(string charaKey, JObject key, string arg)
            {
                var db = s.Rt.Services?.Db ?? throw new BridgeException(BridgeCodes.Unavailable, "no content database: animation rows cannot be resolved");
                int g = Int(key, "group"), cat = Int(key, "category"), no = Int(key, "no");
                string guid = Str(key, "guid") ?? Str(key, "source");
                string rf = Str(key, "ref");
                if (rf != null)
                {
                    if (!ContentId.TryParse(rf, out var id) || id.IsEmpty) throw Bad(arg + ".ref", ArgTypes.ContentId, "bad content id \"" + rf + "\": expected \"guid#no\"");
                    guid = id.SourceGuid;
                    no = id.Id;
                }
                string described = (guid != null ? guid + " " : "") + I(g) + "/" + I(cat) + "/" + I(no);
                StudioAnime row = guid != null ? (db.TryGetAnime(guid, g, cat, no, out var a) ? a : null) : db.FindAnime(g, cat, no);
                if (row == null) return null;
                var warnings = new List<string>();
                var bad = StudioOps.CheckAnimeRow(row, described, warnings);
                if (bad != null) throw new BridgeException(BridgeCodes.Unsupported, arg + ": " + bad.Message);
                foreach (var w in warnings) o.Warning(arg + ": " + w);
                return new AnimeRef(row.Guid, row.Group, row.Category, row.No);
            }

            public int EyePatternAt(string charaKey, int position, string arg)
            {
                var rt = s.Rt;
                var n = s.Model.Find(charaKey) ?? throw new BridgeException(BridgeError.NotFound("no node " + charaKey, arg));
                rt.ApplyNow();
                var r = rt.FindChara(n.Id);
                if (r == null || r.State != RealizationState.Built || r.InGraveyard || !r.Root)
                    throw new BridgeException(BridgeCodes.Unavailable, "character " + n.Id + " is not built: an eye position cannot be read (give raw: true and the pattern id)");
                var lists = StudioOps.For(rt).ListsOf(n);
                return StudioOps.UiToId(lists.Eyes, position);
            }

            static int Int(JObject o, string name)
            {
                var t = o[name];
                if (t == null || t.Type == JTokenType.Null) return 0;
                return t.Type == JTokenType.Integer ? t.Value<int>() : (int)t.Value<double>();
            }

            static string Str(JObject o, string name)
            {
                var t = o[name];
                return t != null && t.Type == JTokenType.String ? (string)t : null;
            }
        }
    }
}
