// Sample sequencer for HS2Next (CC0-1.0: dedicated to the public domain).
// The seq.* bridge commands. They change only the plugin's own state (nothing here is undoable): the poses they imply
// are written by the next Update, outside any scene transaction a client may hold open.
using System;
using System.Collections.Generic;
using HS2Next.Api.Bridge;

namespace Stp3.Samples.Sequencer
{
    static class SequencerCommands
    {
        public static readonly IReadOnlyList<string> Names = new[]
        {
            "seq.set", "seq.get", "seq.play", "seq.pause", "seq.stop", "seq.seek", "seq.status", "seq.measure",
        };

        const string Example = "{\"cmd\":\"seq.set\",\"sequence\":{\"duration\":4,\"loop\":true,\"tracks\":[{\"kind\":\"transform\",\"key\":\"<node id>\"," +
            "\"keys\":[{\"t\":0,\"pos\":[0,0,0],\"rot\":[0,0,0]},{\"t\":4,\"pos\":[1,0,0],\"rot\":[0,90,0]}]}]," +
            "\"events\":[{\"t\":2,\"do\":{\"cmd\":\"obj.rename\",\"key\":\"<node id>\",\"name\":\"halfway\"}}]}}";

        public static void Register(SequencerPlugin p, ICommandRegistry r)
        {
            r.Register(new CommandSpec("seq.set", "Validates a sequence and makes it the scene's sequence: stopped at 0, its first pose written at the next frame. Not undoable.")
                .Arg("sequence", ArgTypes.Object,
                    "{duration (0-3600 s), loop?, autoplay?, tracks?: [{kind: transform, key, ease?: linear|step, keys: [{t, pos, rot}]} | {kind: camera, ease?, keys: [{t, target, rot, dist, fov}]}], events?: [{t, do: request, note?}]}",
                    required: true)
                .Return("tracks", ArgTypes.Int, "tracks").Return("events", ArgTypes.Int, "events").Return("duration", ArgTypes.Float, "seconds")
                .Example(Example)
                .WithFlags(CommandFlags.Mutates), c => Set(p, c));

            r.Register(new CommandSpec("seq.get", "The scene's sequence in its canonical form (null when none), the playback state and time.")
                .Return("sequence", ArgTypes.Any, "the sequence object, or null").Return("state", ArgTypes.Enum("stopped", "playing", "paused", "ended"), "")
                .Return("time", ArgTypes.Float, "seconds")
                .WithFlags(CommandFlags.ReadOnly), c => Get(p));

            r.Register(new CommandSpec("seq.play", "Plays from 'from', else from the current time (from 0 when stopped or ended). Events fire as playback passes their times.")
                .Arg("from", ArgTypes.Float, "start time in seconds (0 to the duration); events at exactly this time fire")
                .Return("state", ArgTypes.String, "").Return("time", ArgTypes.Float, "")
                .WithFlags(CommandFlags.Mutates), c => Play(p, c));

            r.Register(new CommandSpec("seq.pause", "Holds the current time (the poses stay as written).")
                .Return("state", ArgTypes.String, "").Return("time", ArgTypes.Float, "")
                .WithFlags(CommandFlags.Mutates), c => Simple(p, pl => pl.Pause()));

            r.Register(new CommandSpec("seq.stop", "Stops at time 0; the first pose is written at the next frame.")
                .Return("state", ArgTypes.String, "").Return("time", ArgTypes.Float, "")
                .WithFlags(CommandFlags.Mutates), c => Simple(p, pl => pl.Stop()));

            r.Register(new CommandSpec("seq.seek", "Moves to a time without firing events; the pose there is written at the next frame. A playing sequence goes on from there, otherwise it is paused there.")
                .Arg("t", ArgTypes.Float, "time in seconds (0 to the duration)", required: true)
                .Return("state", ArgTypes.String, "").Return("time", ArgTypes.Float, "")
                .WithFlags(CommandFlags.Mutates), c => Seek(p, c));

            r.Register(new CommandSpec("seq.status", "Playback state, tracks and the counters of events and writes.")
                .Return("state", ArgTypes.Enum("stopped", "playing", "paused", "ended"), "").Return("time", ArgTypes.Float, "seconds")
                .Return("duration", ArgTypes.Float, "seconds (0 without a sequence)").Return("loop", ArgTypes.Bool, "")
                .Return("tracks", ArgTypes.ArrayOf(ArgTypes.Object), "{kind, key (transform tracks), active}")
                .Return("eventsFired", ArgTypes.Int, "event requests sent").Return("eventErrors", ArgTypes.Int, "event requests that failed")
                .Return("writes", ArgTypes.Object, "{done, busy, missingNode, error}")
                .Return("updateMs", ArgTypes.Object, "{median, max} over the last 600 frames that played")
                .WithFlags(CommandFlags.ReadOnly), c => new RawJson(p.Player.StatusJson()));

            r.Register(new CommandSpec("seq.measure",
                    "Measures the cost of the tracks' inline calls on a temporary sequence over the given nodes (the scene's sequence is kept and waits); " +
                    "with keys it starts and answers at once, without keys it reports the state or the result. The nodes' transforms and the view are restored afterwards.")
                .Arg("keys", ArgTypes.ArrayOf(ArgTypes.Node), "the nodes (8 items for the standard measurement, 1-64); absent: report")
                .Arg("frames", ArgTypes.Int, "frames to measure (240-6000), in alternating 120-frame windows with and without the sequence", defaultValue: 600)
                .Return("state", ArgTypes.Enum("none", "running", "done", "failed"), "")
                .Return("pm1", ArgTypes.Object, "inline camera.get: {medianUs, p95Us, n}", false)
                .Return("pm1b", ArgTypes.Object, "inline read of the first node (scene.doc {key, capture: false}): {medianUs, p95Us, n, cmd}", false)
                .Return("pm2", ArgTypes.Object, "inline obj.transform: {medianUs, p95Us, n}", false)
                .Return("pm3", ArgTypes.Object, "one update of all tracks: {medianMs, p95Ms, n}", false)
                .Return("pm4", ArgTypes.Object, "queued camera.get: {medianFrames, max, n, lost}", false)
                .Return("pm5", ArgTypes.Object, "frame time medians: {withMs, withoutMs, nWith, nWithout}", false)
                .Return("frames", ArgTypes.Int, "", false).Return("uiOn", ArgTypes.Bool, "", false)
                .WithFlags(CommandFlags.Mutates | CommandFlags.Slow), c => Measure(p, c));
        }

        static BridgeException NoSequence() => new BridgeException(BridgeCodes.NotFound, "no sequence: set one with seq.set");

        static object Set(SequencerPlugin p, ICommandContext c)
        {
            if (c.Args.Kind("sequence") != ArgKind.Object) throw new BridgeException(BridgeError.BadArgs("sequence", ArgTypes.Object));
            Sequence seq;
            try { seq = Sequence.Parse(c.Args.RawJson("sequence")); }
            catch (SequenceException e)
            {
                throw new BridgeException(BridgeCodes.BadArgs, "bad sequence: " + e.Message, new BridgeErrorDetail
                {
                    Arg = "sequence",
                    Extra = new Dictionary<string, object> { ["path"] = e.Path, ["problem"] = e.Problem },
                });
            }
            p.Player.SetSequence(seq);
            p.Player.ResetCounters();
            return new BridgeReply().With("tracks", seq.Tracks.Count).With("events", seq.Events.Count).With("duration", seq.Duration);
        }

        static object Get(SequencerPlugin p)
        {
            var pl = p.Player;
            return new BridgeReply().With("sequence", new RawJson(pl.Sequence?.ToJson() ?? "null"))
                .With("state", Player.StateName(pl.State)).With("time", pl.Time);
        }

        static object StateReply(Player pl) => new BridgeReply().With("state", Player.StateName(pl.State)).With("time", pl.Time);

        static object Simple(SequencerPlugin p, Action<Player> act)
        {
            if (p.Player.Sequence == null) throw NoSequence();
            act(p.Player);
            return StateReply(p.Player);
        }

        static double TimeArg(ICommandContext c, string name, double duration)
        {
            double t = c.Args.Require<double>(name);
            if (double.IsNaN(t) || t < 0 || t > duration)
                throw new BridgeException(BridgeError.BadArgs(name, "0-" + Sequence.NumberText(duration)));
            return t;
        }

        static object Play(SequencerPlugin p, ICommandContext c)
        {
            var seq = p.Player.Sequence ?? throw NoSequence();
            if (c.Args.Has("from")) p.Player.Play(TimeArg(c, "from", seq.Duration));
            else p.Player.Play();
            return StateReply(p.Player);
        }

        static object Seek(SequencerPlugin p, ICommandContext c)
        {
            var seq = p.Player.Sequence ?? throw NoSequence();
            p.Player.Seek(TimeArg(c, "t", seq.Duration));
            return StateReply(p.Player);
        }

        static object Measure(SequencerPlugin p, ICommandContext c)
        {
            var running = p.Measurement;
            if (!c.Args.Has("keys"))
                return new RawJson(running?.ReportJson() ?? "{\"state\":\"none\"}");
            if (running != null && running.Running) throw new BridgeException(BridgeCodes.Busy, "a measurement is running");
            if (c.Args.Kind("keys") != ArgKind.Array) throw new BridgeException(BridgeError.BadArgs("keys", ArgTypes.ArrayOf(ArgTypes.Node)));
            var keys = c.Args.Get<string[]>("keys");
            if (keys == null || keys.Length < 1 || keys.Length > 64) throw new BridgeException(BridgeError.BadArgs("keys", "1-64 node ids"));
            int frames = c.Args.Get("frames", 600);
            if (frames < 240 || frames > 6000) throw new BridgeException(BridgeError.BadArgs("frames", "240-6000"));
            p.Measurement = Measurement.Start(p.Context, keys, frames);
            return new RawJson(p.Measurement.ReportJson());
        }
    }
}
