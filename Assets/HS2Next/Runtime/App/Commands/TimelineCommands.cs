// The Timeline's commands (group "timeline", owner core, Since 1.2.0). Nodes are given by key (node id); tracks by track (its id,
// or its 0-based index as an integer). Every change of the Timeline is one scene command (one history entry; undo: false applies
// it without one and keeps the history); playback, seeks and timed commands never change the scene.
// HS2's names, arguments and reply members (ParityNames):
//   timeline.build   {duration 10, tracks [], play true, append false, returnXml false, blockLength; loop, autoplay, events}:
//                    builds the Timeline from tracks in HS2's input vocabulary (replacing the tracks, or adding to them with
//                    append), keeps the events unless events is given, then stops and plays from 0 unless play is false; reply
//                    {tracks, keys, events, duration, state} (no XML length: HS2Next writes no Timeline XML)
//   timeline.events  {events, clear false}: replaces the timed commands ({t, do, note?, enabled?}); reply {events}
//   timeline.play    {from?}   timeline.pause   timeline.stop {bake false}   timeline.seek {t 0}: playback; reply {state, time}
//                    (+ baked: the values written into the scene by the bake, one history entry)
//   timeline.clear   stops and removes the Timeline; reply {cleared}
//   timeline.status  {perf false, reset false}: {playing, time, duration, interpolables, state, loop, autoplay, tracks, keys,
//                    events, activeTracks, eventsFired, eventErrors, lastEventError, scenePaused, suspended, perf?}
// HS2Next's editing commands: timeline.get, timeline.set, timeline.track.add / .set / .remove, timeline.key.set / .delete / .move.
// timeline.export and timeline.loadxml (HS2's Timeline XML) are not registered.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HS2Next.Api.Bridge;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Timeline;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.App.Commands
{
    public static class TimelineCommands
    {
        /// <summary>The commands of this file (Phase 6.2, Since 1.2.0), registered by CoreCommands in the group "timeline".</summary>
        public static readonly IReadOnlyList<string> Names = new[]
        {
            "timeline.build", "timeline.events", "timeline.play", "timeline.pause", "timeline.stop", "timeline.seek", "timeline.clear",
            "timeline.status", "timeline.get", "timeline.set", "timeline.track.add", "timeline.track.set", "timeline.track.remove",
            "timeline.key.set", "timeline.key.delete", "timeline.key.move",
        };

        /// <summary>The commands of this file that keep HS2's automation vocabulary (CoreCommands.ParityNames).</summary>
        public static readonly IReadOnlyList<string> ParityNames = new[]
        {
            "timeline.build", "timeline.clear", "timeline.events", "timeline.pause", "timeline.play", "timeline.seek", "timeline.status",
            "timeline.stop",
        };

        /// <summary>CommandSpec.Since of the Phase 6.2 commands (the Api version that adds them).</summary>
        public const string Since62 = "1.2.0";

        /// <summary>The help group of these commands.</summary>
        public const string GroupName = "timeline";

        /// <summary>The duration of a Timeline that a command creates without being given one (seconds).</summary>
        public const float DefaultDuration = 10f;

        const string TrackType = "a track id (string) or index (integer)";
        const string NoTimeline = "the scene has no Timeline";

        static CommandSpec Spec(string name, string summary) => CommandKit.Spec(name, summary, GroupName).WithSince(Since62);

        public static void Register(ICommandRegistry r, AppCore app)
        {
            // ---------------------------------------------------------------- HS2's names
            CommandKit.Register(r, app, Spec("timeline.build", "Builds the scene's Timeline from tracks in HS2's input vocabulary (one history entry): " +
                    "the tracks replace the Timeline's tracks, or are added to them with append; the timed commands are kept unless events is given. " +
                    "Then the Timeline stops and plays from 0 unless play is false. Nothing is built when any track or key is refused. " +
                    "The reply carries no XML length: HS2Next writes no Timeline XML.")
                    .WithFlags(CommandFlags.Mutates)
                    .Arg("duration", ArgTypes.Float, "the length in seconds (0 < duration ≤ 3600); with append the Timeline's own length is kept when absent", false, 10f)
                    .Arg("tracks", ArgTypes.ArrayOf(ArgTypes.Object), "tracks {type, key?, bone?, slot?, raw?, ease?, label?, enabled?, keys: [{t, ease?, …}]}: " +
                        "type camera, dof, pos, rot (with bone: an FK bone of a character), scale, anime, animeSpeed, animeTime, eyes, mouth, eyesOpen, mouthOpen or color; " +
                        "camera keys take target, rot, dist, fov (later keys carry missing members over); dof keys on, focus, size / aperture or sharp / full, blur, near; " +
                        "anime keys group, category, no (+ guid or ref); eyes keys v = the position in the character's eye list (the pattern id with raw); other keys v", false)
                    .Arg("play", ArgTypes.Bool, "stop, then play from 0 after building; false keeps the playback state", false, true)
                    .Arg("append", ArgTypes.Bool, "add the tracks to the Timeline's tracks instead of replacing them", false, false)
                    .Arg("returnXml", ArgTypes.Bool, "accepted for HS2 scripts: HS2Next has no Timeline XML (a warning)", false, false)
                    .Arg("blockLength", ArgTypes.Float, "accepted for HS2 scripts; no effect")
                    .Arg("loop", ArgTypes.Bool, "playback wraps at the end (absent: true; with append the Timeline's own)")
                    .Arg("autoplay", ArgTypes.Bool, "play from 0 after the scene is loaded (absent: false; with append the Timeline's own)")
                    .Arg("events", ArgTypes.ArrayOf(ArgTypes.Object), "timed commands {t, do, note?, enabled?} replacing the Timeline's (absent: kept)")
                    .Return("tracks", ArgTypes.Int, "tracks of the Timeline").Return("keys", ArgTypes.Int, "keys of every track")
                    .Return("events", ArgTypes.Int, "timed commands").Return("duration", ArgTypes.Float, "seconds")
                    .Return("state", ArgTypes.Enum("none", "stopped", "playing", "paused", "ended"), "the playback state after the build")
                    .Example("{\"cmd\":\"timeline.build\",\"duration\":10,\"tracks\":[{\"type\":\"camera\",\"keys\":[{\"t\":0,\"target\":[0,1,0],\"rot\":[10,0,0],\"dist\":3,\"fov\":40},{\"t\":5,\"rot\":[10,90,0]}]}]}"),
                Build);

            CommandKit.Register(r, app, Spec("timeline.events", "Replaces the Timeline's timed commands (one history entry). A timed command is a request {cmd, …} " +
                    "that runs when the playing Timeline passes its time, never on a seek, a pause, a stop or a load; it runs one frame after the time is passed, " +
                    "without an undo entry unless its request says undo: true. A scene without a Timeline gets one of max(10, the latest time) seconds.")
                    .WithFlags(CommandFlags.Mutates)
                    .Arg("events", ArgTypes.ArrayOf(ArgTypes.Object), "timed commands {t, do, note?, enabled?}: do is a request object with cmd and its arguments (no id); " +
                        "commands that build or edit the Timeline are refused")
                    .Arg("clear", ArgTypes.Bool, "remove every timed command (events, when given, then replace them)", false, false)
                    .Return("events", ArgTypes.Int, "timed commands of the Timeline")
                    .Example("{\"cmd\":\"timeline.events\",\"events\":[{\"t\":2.5,\"do\":{\"cmd\":\"chara.face\",\"key\":\"<node id>\",\"eyes\":3}}]}"),
                Events);

            CommandKit.Register(r, app, Spec("timeline.play", "Plays the Timeline on the scene clock: from 0 when it is stopped or has ended, from the paused time " +
                    "when it is paused, or from from (also while it plays: a restart). Playing again without from changes nothing. The global pause holds the time.")
                    .Arg("from", ArgTypes.Float, "the start time in seconds (clamped to the Timeline)")
                    .Return("state", ArgTypes.String, "none, stopped, playing, paused or ended").Return("time", ArgTypes.Float, "the play head in seconds")
                    .Example("{\"cmd\":\"timeline.play\"}"),
                Play);

            CommandKit.Register(r, app, Spec("timeline.pause", "Pauses a playing Timeline; its values stay shown. Otherwise nothing changes.")
                    .Return("state", ArgTypes.String, "").Return("time", ArgTypes.Float, "")
                    .Example("{\"cmd\":\"timeline.pause\"}"),
                Pause);

            CommandKit.Register(r, app, Spec("timeline.stop", "Stops the Timeline: the play head goes to 0 and the scene shows its own values again. With bake, the values " +
                    "the Timeline shows are first written into the scene (one history entry «Bake Timeline pose»; the camera as the current view, without history).")
                    .WithFlags(CommandFlags.Mutates)
                    .Arg("bake", ArgTypes.Bool, "keep the shown pose: write the Timeline's values into the scene before stopping", false, false)
                    .Return("state", ArgTypes.String, "").Return("time", ArgTypes.Float, "")
                    .Return("baked", ArgTypes.Int, "values written into the scene by the bake")
                    .Example("{\"cmd\":\"timeline.stop\",\"bake\":true}"),
                Stop);

            CommandKit.Register(r, app, Spec("timeline.seek", "Moves the play head and shows the Timeline's values at that time at once; no timed command runs. " +
                    "A playing Timeline plays on from there; a stopped or ended one is paused there.")
                    .Arg("t", ArgTypes.Float, "the time in seconds (clamped to the Timeline)", false, 0f)
                    .Return("state", ArgTypes.String, "").Return("time", ArgTypes.Float, "")
                    .Example("{\"cmd\":\"timeline.seek\",\"t\":2.5}"),
                Seek);

            CommandKit.Register(r, app, Spec("timeline.clear", "Stops the Timeline and removes it from the scene (one history entry); a scene without a Timeline " +
                    "is saved as it was before it had one.")
                    .WithFlags(CommandFlags.Mutates)
                    .Return("cleared", ArgTypes.Bool, "the scene had a Timeline")
                    .Example("{\"cmd\":\"timeline.clear\"}"),
                Clear);

            CommandKit.Register(r, app, Spec("timeline.status", "The Timeline and its playback: state, play head, length, tracks, keys, timed commands and their failures; " +
                    "with perf, the time the playback took per frame while playing.")
                    .WithFlags(CommandFlags.ReadOnly)
                    .Arg("perf", ArgTypes.Bool, "add perf: the playback's time per frame over the frames played since the last reset (at most the last 4,096)", false, false)
                    .Arg("reset", ArgTypes.Bool, "with perf: start a new window after this reply", false, false)
                    .Return("playing", ArgTypes.Bool, "the Timeline plays (also while the global pause holds it)")
                    .Return("time", ArgTypes.Float, "the play head in seconds (0 without a Timeline)")
                    .Return("duration", ArgTypes.Float, "seconds (0 without a Timeline)")
                    .Return("interpolables", ArgTypes.Int, "tracks showing a value now")
                    .Return("state", ArgTypes.Enum("none", "stopped", "playing", "paused", "ended"), "")
                    .Return("loop", ArgTypes.Bool, "playback wraps at the end (null without a Timeline)")
                    .Return("autoplay", ArgTypes.Bool, "plays after the scene is loaded (null without a Timeline)")
                    .Return("tracks", ArgTypes.Int, "").Return("keys", ArgTypes.Int, "").Return("events", ArgTypes.Int, "timed commands")
                    .Return("activeTracks", ArgTypes.Int, "tracks that can show a value (enabled, the first of their channel, their target in the scene)")
                    .Return("eventsFired", ArgTypes.Int, "timed commands started since the scene was opened")
                    .Return("eventErrors", ArgTypes.Int, "timed commands that failed since the scene was opened")
                    .Return("lastEventError", ArgTypes.Object, "{event, code, message} of the last failure, or null")
                    .Return("scenePaused", ArgTypes.Bool, "the global pause holds the scene clock")
                    .Return("suspended", ArgTypes.Int, "tracks showing your edit until the play head moves")
                    .Return("perf", ArgTypes.Object, "with perf: {frames, medianUs, p95Us, maxUs}", false)
                    .Example("{\"cmd\":\"timeline.status\"}"),
                Status);

            // ---------------------------------------------------------------- HS2Next's editing commands
            CommandKit.Register(r, app, Spec("timeline.get", "The scene's Timeline in its stored form (as the scene file holds it), with the playback state.")
                    .WithFlags(CommandFlags.ReadOnly)
                    .Return("timeline", ArgTypes.Object, "{duration, loop, autoplay, tracks, events}, or null when the scene has none")
                    .Return("state", ArgTypes.String, "").Return("time", ArgTypes.Float, "")
                    .Example("{\"cmd\":\"timeline.get\"}"),
                Get);

            CommandKit.Register(r, app, Spec("timeline.set", "Replaces the Timeline with one in its stored form, or changes its length, loop or autoplay (one history entry); " +
                    "a scene without a Timeline gets one. timeline: null removes it.")
                    .WithFlags(CommandFlags.Mutates)
                    .Arg("timeline", ArgTypes.Object, "the Timeline in its stored form (as timeline.get answers it), or null to remove it")
                    .Arg("duration", ArgTypes.Float, "the length in seconds (0 < duration ≤ 3600)")
                    .Arg("loop", ArgTypes.Bool, "playback wraps at the end")
                    .Arg("autoplay", ArgTypes.Bool, "play from 0 after the scene is loaded")
                    .Return("tracks", ArgTypes.Int, "").Return("keys", ArgTypes.Int, "").Return("events", ArgTypes.Int, "").Return("duration", ArgTypes.Float, "")
                    .Example("{\"cmd\":\"timeline.set\",\"timeline\":{\"duration\":10}}"),
                Set);

            CommandKit.Register(r, app, Spec("timeline.track.add", "Adds a track (one history entry); a scene without a Timeline gets one. One track per channel: " +
                    "a second track of the same target and value is refused.")
                    .WithFlags(CommandFlags.Mutates)
                    .Arg("type", ArgTypes.Enum(TimelineTrackTypes.StoredNames), "the value the track drives", true)
                    .Arg("key", ArgTypes.Node, "the node (none for camera and dof)")
                    .Arg("bone", ArgTypes.String, "rot of a character: the FK bone (its transform name)")
                    .Arg("slot", ArgTypes.Int, "color of an item: its colour slot 0-3 (none for a light)")
                    .Arg("label", ArgTypes.String, "a display label (absent: from the target and type)")
                    .Arg("enabled", ArgTypes.Bool, "a disabled track shows nothing", false, true)
                    .Arg("index", ArgTypes.Int, "the place in the track list (absent: the end)")
                    .Arg("keys", ArgTypes.ArrayOf(ArgTypes.Object), "keys {t, ease, v} in the stored form (absent: none)")
                    .Return("track", ArgTypes.String, "the new track's id").Return("index", ArgTypes.Int, "its place in the track list")
                    .Example("{\"cmd\":\"timeline.track.add\",\"type\":\"pos\",\"key\":\"<node id>\"}"),
                TrackAdd);

            CommandKit.Register(r, app, Spec("timeline.track.set", "Changes a track's target, bone, slot, label, enabled or place in the list (one history entry).")
                    .WithFlags(CommandFlags.Mutates)
                    .Arg("track", ArgTypes.Any, "the track: its id, or its index", true)
                    .Arg("key", ArgTypes.Node, "a new target node of a kind the track's type takes")
                    .Arg("bone", ArgTypes.String, "rot tracks: the FK bone (null: the node's own rotation)")
                    .Arg("slot", ArgTypes.Int, "color tracks of an item: the colour slot 0-3")
                    .Arg("label", ArgTypes.String, "the display label (null: from the target and type)")
                    .Arg("enabled", ArgTypes.Bool, "a disabled track shows nothing")
                    .Arg("index", ArgTypes.Int, "a new place in the track list")
                    .Return("track", ArgTypes.String, "the track's id").Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "the members changed")
                    .Example("{\"cmd\":\"timeline.track.set\",\"track\":0,\"enabled\":false}"),
                TrackSet);

            CommandKit.Register(r, app, Spec("timeline.track.remove", "Removes tracks with their keys (one history entry).")
                    .WithFlags(CommandFlags.Mutates)
                    .Arg("track", ArgTypes.Any, "a track: its id, or its index")
                    .Arg("tracks", ArgTypes.ArrayOf(ArgTypes.Any), "several tracks (ids or indexes)")
                    .Return("removed", ArgTypes.Int, "tracks removed")
                    .Example("{\"cmd\":\"timeline.track.remove\",\"track\":0}"),
                TrackRemove);

            CommandKit.Register(r, app, Spec("timeline.key.set", "Sets the key of a track at time t (one history entry «Key»; with merge, a second edit of the same key " +
                    "joins it). Without v the key takes the value the target shows now.")
                    .WithFlags(CommandFlags.Mutates)
                    .Arg("track", ArgTypes.Any, "the track: its id, or its index", true)
                    .Arg("t", ArgTypes.Float, "the key's time in seconds (within the Timeline)", true)
                    .Arg("v", ArgTypes.Any, "the value in the stored form of the track's type (absent: the value shown now)")
                    .Arg("ease", ArgTypes.Enum("step", "linear", "inout", "in", "out"), "the shape of the segment to the next key (absent: the key's own, else inout; anime, eyes and mouth always step)")
                    .Arg("merge", ArgTypes.Bool, "join the history entry of the previous edit of this key", false, false)
                    .Return("track", ArgTypes.String, "").Return("index", ArgTypes.Int, "the key's index in the track")
                    .Return("t", ArgTypes.Float, "").Return("v", ArgTypes.Any, "the stored value").Return("ease", ArgTypes.String, "")
                    .Return("keys", ArgTypes.Int, "keys of the track")
                    .Example("{\"cmd\":\"timeline.key.set\",\"track\":0,\"t\":2.5,\"v\":[0,1,0]}"),
                KeySet);

            CommandKit.Register(r, app, Spec("timeline.key.delete", "Deletes keys of a track by their times (one history entry).")
                    .WithFlags(CommandFlags.Mutates)
                    .Arg("track", ArgTypes.Any, "the track: its id, or its index", true)
                    .Arg("t", ArgTypes.Float, "the time of one key")
                    .Arg("times", ArgTypes.ArrayOf(ArgTypes.Float), "the times of several keys")
                    .Return("deleted", ArgTypes.Int, "keys deleted")
                    .Example("{\"cmd\":\"timeline.key.delete\",\"track\":0,\"t\":2.5}"),
                KeyDelete);

            CommandKit.Register(r, app, Spec("timeline.key.move", "Moves keys of a track in time (one history entry «Move keys»; with merge, consecutive moves of a drag " +
                    "join one entry). Two keys of a track never share a time, and every key stays within the Timeline.")
                    .WithFlags(CommandFlags.Mutates)
                    .Arg("track", ArgTypes.Any, "the track: its id, or its index", true)
                    .Arg("t", ArgTypes.Float, "the time of one key")
                    .Arg("times", ArgTypes.ArrayOf(ArgTypes.Float), "the times of several keys")
                    .Arg("to", ArgTypes.Float, "the new time of the one key")
                    .Arg("by", ArgTypes.Float, "seconds added to every key's time")
                    .Arg("merge", ArgTypes.Bool, "join the history entry of the previous move of this track", false, false)
                    .Return("times", ArgTypes.ArrayOf(ArgTypes.Float), "the new times, in the order given")
                    .Example("{\"cmd\":\"timeline.key.move\",\"track\":0,\"t\":2.5,\"to\":3}"),
                KeyMove);
        }

        // ================================================================ helpers

        /// <summary>The scene runtime's Timeline playback, or null.</summary>
        public static TimelinePlayback PlaybackOf(AppCore app) => app?.Runtime?.GetPart<TimelinePlayback>();

        /// <summary>The in-process Timeline service of the app's scene runtime (unavailable without one).</summary>
        static TimelineService Service(CoreContext c)
        {
            var pb = PlaybackOf(c.App) ?? throw new BridgeException(CommandKit.Unavailable, "no Timeline playback (the scene runtime has none)");
            pb.Sync();
            pb.CollectReplies();
            return pb.Service;
        }

        /// <summary>The options of a bridge edit: Source Bridge, the envelope's undo and strict, warnings into the reply.</summary>
        static TimelineCallOptions Options(CoreContext c) =>
            new TimelineCallOptions { Source = CommandSource.Bridge, Undo = c.Undo, Strict = c.Ctx.Strict, Warn = c.Warn };

        /// <summary>The runtime side of the commands: the Timeline playback submits its timed commands to this app's bridge.</summary>
        public static void RuntimeCreated(AppCore app)
        {
            var pb = PlaybackOf(app);
            if (pb == null) return;
            pb.SubmitRequest = json =>
            {
                var b = app.Bridge;
                return b != null && !b.IsDisposed ? b.Server.SubmitAsync(json) : null;
            };
        }

        /// <summary>The state names of the replies.</summary>
        public static string StateName(TimelinePlayState s)
        {
            switch (s)
            {
                case TimelinePlayState.Stopped: return "stopped";
                case TimelinePlayState.Playing: return "playing";
                case TimelinePlayState.Paused: return "paused";
                case TimelinePlayState.Ended: return "ended";
                default: return "none";
            }
        }

        static BridgeReply Transport(TimelineService s) => new BridgeReply().With("state", StateName(s.State)).With("time", s.Time);

        static BridgeReply Counts(TimelineDocument doc) => new BridgeReply()
            .With("tracks", doc?.Tracks.Count ?? 0).With("keys", doc?.KeyCount ?? 0).With("events", doc?.Events.Count ?? 0)
            .With("duration", doc != null ? (double)doc.Duration : 0.0);

        /// <summary>An argument's JSON (null when absent; JSON null as a null token). Dates stay strings.</summary>
        static JToken Json(CoreContext c, string arg)
        {
            if (c.Args.Kind(arg) == ArgKind.Missing) return null;
            try
            {
                using (var sr = new StringReader(c.Args.RawJson(arg) ?? "null"))
                using (var jr = new JsonTextReader(sr) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double })
                    return JToken.ReadFrom(jr);
            }
            catch (Exception e) { throw CommandKit.BadArgs(arg, ArgTypes.Any, e.Message); }
        }

        static float? Seconds(CoreContext c, string arg)
        {
            var v = CommandKit.OptFloat(c, arg);
            if (v.HasValue) CommandKit.Finite(v.Value, arg);
            return v;
        }

        static float RequiredSeconds(CoreContext c, string arg) => Seconds(c, arg) ?? throw new BridgeException(BridgeError.Missing(arg, ArgTypes.Float));

        /// <summary>True when the argument is in the request, a JSON null included (null sets a member to none).</summary>
        static bool Given(CoreContext c, string arg) => c.Args.Kind(arg) != ArgKind.Missing;

        /// <summary>A string argument that may be null on purpose (null when absent or null).</summary>
        static string NullableString(CoreContext c, string arg) => c.Args.Kind(arg) == ArgKind.Null ? null : CommandKit.OptString(c, arg);

        /// <summary>The id of the track argument <paramref name="arg"/>: an id, or a 0-based index.</summary>
        static string TrackId(CoreContext c, string arg = "track")
        {
            var doc = c.Session.Model.Timeline ?? throw CommandKit.NotFound("the scene has no Timeline", arg);
            switch (c.Args.Kind(arg))
            {
                case ArgKind.String: return c.Args.Get<string>(arg);
                case ArgKind.Integer: return TrackIdAt(doc, c.Args.Get<int>(arg), arg);
                case ArgKind.Missing:
                case ArgKind.Null: throw new BridgeException(BridgeError.Missing(arg, TrackType));
                default: throw CommandKit.BadArgs(arg, TrackType);
            }
        }

        static string TrackIdAt(TimelineDocument doc, int index, string arg)
        {
            if (index < 0 || index >= doc.Tracks.Count)
                throw CommandKit.NotFound("track " + index + " is outside 0.." + (doc.Tracks.Count - 1) + " (the Timeline has " + doc.Tracks.Count + " tracks)", arg);
            return doc.Tracks[index].Id;
        }

        /// <summary>The times of argument t or times.</summary>
        static List<float> Times(CoreContext c)
        {
            var list = new List<float>();
            if (FileRoots.OneOf(c, "t", "times") == "t")
            {
                list.Add(RequiredSeconds(c, "t"));
                return list;
            }
            var arr = Json(c, "times") as JArray ?? throw CommandKit.BadArgs("times", ArgTypes.ArrayOf(ArgTypes.Float));
            for (int k = 0; k < arr.Count; k++)
            {
                var e = arr[k];
                if (e.Type != JTokenType.Float && e.Type != JTokenType.Integer) throw CommandKit.BadArgs("times[" + k + "]", ArgTypes.Float);
                list.Add(CommandKit.Finite((float)e.Value<double>(), "times[" + k + "]"));
            }
            return list;
        }

        static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        // ================================================================ handlers

        static object Build(CoreContext c)
        {
            bool append = CommandKit.OptBool(c, "append") ?? false;
            float? duration = Seconds(c, "duration");
            bool? loop = CommandKit.OptBool(c, "loop"), autoplay = CommandKit.OptBool(c, "autoplay");
            bool play = CommandKit.OptBool(c, "play") ?? true;
            bool xml = CommandKit.OptBool(c, "returnXml") ?? false;
            CommandKit.OptFloat(c, "blockLength");
            var tracks = Json(c, "tracks");
            var events = Json(c, "events");
            var s = Service(c);
            s.Build(tracks, duration, append, loop, autoplay, events, play, Options(c));
            if (xml) c.Warn("no XML in HS2Next: the reply carries no Timeline XML");
            return Counts(s.Document).With("state", StateName(s.State));
        }

        static object Events(CoreContext c)
        {
            bool clear = CommandKit.OptBool(c, "clear") ?? false;
            int n = Service(c).SetEvents(Json(c, "events"), clear, Options(c));
            return new BridgeReply().With("events", n);
        }

        static object Play(CoreContext c)
        {
            var from = Seconds(c, "from");
            var s = Service(c);
            if (!s.Play(from)) c.Warn(NoTimeline);
            return Transport(s);
        }

        static object Pause(CoreContext c)
        {
            var s = Service(c);
            s.Pause();
            return Transport(s);
        }

        static object Seek(CoreContext c)
        {
            float t = Seconds(c, "t") ?? 0f;
            var s = Service(c);
            if (!s.Seek(t)) c.Warn(NoTimeline);
            return Transport(s);
        }

        static object Stop(CoreContext c)
        {
            bool bake = CommandKit.OptBool(c, "bake") ?? false;
            var s = Service(c);
            int baked = s.Stop(bake, Options(c));
            return Transport(s).With("baked", baked);
        }

        static object Clear(CoreContext c) => new BridgeReply().With("cleared", Service(c).Clear(Options(c)));

        static object Status(CoreContext c)
        {
            bool perf = CommandKit.OptBool(c, "perf") ?? false, reset = CommandKit.OptBool(c, "reset") ?? false;
            var s = Service(c);
            var pb = s.Playback;
            var p = pb.Player;
            var doc = s.Document;
            var st = p.State;
            var err = pb.LastEventError;
            var r = new BridgeReply()
                .With("playing", st == TimelinePlayState.Playing)
                .With("time", p.Time)
                .With("duration", doc != null ? (double)doc.Duration : 0.0)
                .With("interpolables", p.ValuesShown)
                .With("state", StateName(st))
                .With("loop", doc != null ? (object)doc.Loop : null)
                .With("autoplay", doc != null ? (object)doc.Autoplay : null)
                .With("tracks", doc?.Tracks.Count ?? 0).With("keys", doc?.KeyCount ?? 0).With("events", doc?.Events.Count ?? 0)
                .With("activeTracks", p.CountActiveTracks())
                .With("eventsFired", pb.EventsFired)
                .With("eventErrors", pb.EventErrors)
                .With("lastEventError", err == null ? null : new BridgeReply().With("event", err.EventId).With("code", err.Code).With("message", err.Message))
                .With("scenePaused", s.ScenePaused)
                .With("suspended", p.SuspendedCount);
            if (perf)
            {
                var w = pb.Perf;
                var o = new BridgeReply();
                if (w.Stats(out double median, out double p95, out double max)) o.With("frames", w.Count).With("medianUs", median).With("p95Us", p95).With("maxUs", max);
                else o.With("frames", 0).With("medianUs", null).With("p95Us", null).With("maxUs", null);
                r["perf"] = o;
                if (reset) w.Reset();
            }
            foreach (var w in p.Warnings) c.Warn(w);
            return r;
        }

        static object Get(CoreContext c)
        {
            var s = Service(c);
            var doc = s.Document;
            return new BridgeReply().With("timeline", doc != null ? new RawJson(doc.ToJson()) : null).With("state", StateName(s.State)).With("time", s.Time);
        }

        static object Set(CoreContext c)
        {
            var kind = c.Args.Kind("timeline");
            float? duration = Seconds(c, "duration");
            bool? loop = CommandKit.OptBool(c, "loop"), autoplay = CommandKit.OptBool(c, "autoplay");
            bool members = duration.HasValue || loop.HasValue || autoplay.HasValue;
            var s = Service(c);
            var o = Options(c);
            if (kind == ArgKind.Null)
            {
                if (members) throw CommandKit.BadArgs("timeline", null, "timeline: null removes the Timeline; duration, loop and autoplay need one");
                if (s.Document != null) s.SetDocument(null, TimelineEditCommand.LabelTimeline, null, o);
            }
            else if (kind == ArgKind.Object) { var doc = Json(c, "timeline"); (doc as JObject)?.Remove(TimelineScriptMarker.MemberName); s.SetStored(doc, duration, loop, autoplay, o); }   // a whole Timeline is no script's: its script mark goes
            else if (kind != ArgKind.Missing) throw CommandKit.BadArgs("timeline", ArgTypes.Object);
            else if (!members) throw CommandKit.BadArgs("timeline", null, "give timeline, duration, loop or autoplay");
            else s.SetProps(duration, loop, autoplay, o);
            return Counts(s.Document);
        }

        static object TrackAdd(CoreContext c)
        {
            string type = CommandKit.OptString(c, "type") ?? throw new BridgeException(BridgeError.Missing("type", ArgTypes.Enum(TimelineTrackTypes.StoredNames)));
            string key = null;
            if (!TimelineTypes.IsSceneWide(type) && !CommandKit.Absent(c, "key")) key = CommandKit.Node(c, "key").Id;
            else if (!CommandKit.Absent(c, "key")) throw CommandKit.BadArgs("key", null, type + " tracks name no node");
            string bone = CommandKit.OptString(c, "bone");
            int? slot = CommandKit.OptInt(c, "slot");
            string label = NullableString(c, "label");
            bool enabled = CommandKit.OptBool(c, "enabled") ?? true;
            int? index = CommandKit.OptInt(c, "index");
            var keys = Json(c, "keys");
            var s = Service(c);
            string id = s.TrackAdd(type, key, bone, slot, label, enabled, index, keys, null, Options(c));
            return new BridgeReply().With("track", id).With("index", s.Document.IndexOfTrack(id));
        }

        static object TrackSet(CoreContext c)
        {
            string id = TrackId(c);
            var ch = new TimelineTrackChange();
            if (Given(c, "key"))
            {
                ch.SetKey = true;
                ch.Key = CommandKit.Node(c, "key").Id;
            }
            if (Given(c, "bone"))
            {
                ch.SetBone = true;
                ch.Bone = NullableString(c, "bone");
            }
            if (Given(c, "slot"))
            {
                ch.SetSlot = true;
                ch.Slot = c.Args.Kind("slot") == ArgKind.Null ? null : CommandKit.OptInt(c, "slot");
            }
            if (Given(c, "label"))
            {
                ch.SetLabel = true;
                ch.Label = NullableString(c, "label");
            }
            ch.Enabled = CommandKit.OptBool(c, "enabled");
            ch.Index = CommandKit.OptInt(c, "index");
            var changed = Service(c).TrackSet(id, ch, Options(c));
            return new BridgeReply().With("track", id).With("changed", changed);
        }

        static object TrackRemove(CoreContext c)
        {
            var ids = new List<string>();
            if (FileRoots.OneOf(c, "track", "tracks") == "track") ids.Add(TrackId(c));
            else
            {
                var doc = c.Session.Model.Timeline ?? throw CommandKit.NotFound("the scene has no Timeline", "tracks");
                var arr = Json(c, "tracks") as JArray ?? throw CommandKit.BadArgs("tracks", ArgTypes.ArrayOf(ArgTypes.Any));
                for (int k = 0; k < arr.Count; k++)
                {
                    var e = arr[k];
                    if (e.Type == JTokenType.String) ids.Add((string)e);
                    else if (e.Type == JTokenType.Integer) ids.Add(TrackIdAt(doc, e.Value<int>(), "tracks[" + k + "]"));
                    else throw CommandKit.BadArgs("tracks[" + k + "]", TrackType);
                }
            }
            return new BridgeReply().With("removed", Service(c).TrackRemove(ids, Options(c)));
        }

        static object KeySet(CoreContext c)
        {
            string id = TrackId(c);
            float t = RequiredSeconds(c, "t");
            string ease = CommandKit.OptString(c, "ease");
            bool merge = CommandKit.OptBool(c, "merge") ?? false;
            var v = Given(c, "v") ? Json(c, "v") : null;
            if (v != null && v.Type == JTokenType.Null) throw CommandKit.BadArgs("v", "the stored form of the track's value (leave v out for the value shown now)");
            var r = Service(c).KeySet(id, t, v, ease, merge ? "timeline.key.set:" + id + ":" + F(t) : null, Options(c));
            return new BridgeReply().With("track", r.Track).With("index", r.Index).With("t", (double)r.T)
                .With("v", r.V != null ? new RawJson(r.V.ToString(Formatting.None)) : null).With("ease", r.Ease).With("keys", r.Keys);
        }

        static object KeyDelete(CoreContext c)
        {
            string id = TrackId(c);
            var times = Times(c);
            return new BridgeReply().With("deleted", Service(c).KeyDelete(id, times, Options(c)));
        }

        static object KeyMove(CoreContext c)
        {
            string id = TrackId(c);
            var times = Times(c);
            bool merge = CommandKit.OptBool(c, "merge") ?? false;
            bool to = FileRoots.OneOf(c, "to", "by") == "to";
            float? toT = to ? RequiredSeconds(c, "to") : (float?)null, by = to ? (float?)null : RequiredSeconds(c, "by");
            var moved = Service(c).KeyMove(id, times, toT, by, merge ? "timeline.key.move:" + id : null, Options(c));
            var reply = new List<double>();
            foreach (var x in moved) reply.Add(x);
            return new BridgeReply().With("times", reply);
        }
    }
}
