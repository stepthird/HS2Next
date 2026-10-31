// The bridge's own commands: ping, help, bridge.version, batch and log.tail.
// All but batch are thread-safe and answered on the listener's worker thread when sent alone; batch is run by the
// dispatcher (its handler here only exists for help). help is generated from the registered specs.
using System;
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.Bridge
{
    static class BuiltinCommands
    {
        internal static void Register(BridgeServer s)
        {
            var since = "1.0.0";
            s.Commands.RegisterBuiltin(new CommandSpec("ping", "Liveness check; also the app's readiness stages, frame, clock and screen size (an empty body is a ping).")
                {
                    Group = "bridge", Flags = CommandFlags.ReadOnly, Gate = ReadyGate.None, Since = since,
                }
                .Return("protocol", ArgTypes.Int, "envelope version")
                .Return("app", ArgTypes.String, "\"HS2Next\" (HS2's Studio Bridge has no such field)")
                .Return("bridge", ArgTypes.String, "bridge version")
                .Return("api", ArgTypes.String, "HS2Next.Api version")
                .Return("ready", ArgTypes.Object, "readiness stages {stage: bool}")
                .Return("appReady", ArgTypes.Bool, "every required stage is ready")
                .Return("readyDetail", ArgTypes.Object, "progress texts of stages that have one {stage: text}", false)
                .Return("studio", ArgTypes.Bool, "= appReady (HS2 Studio Bridge member)")
                .Return("version", ArgTypes.Int, "1 (HS2 Studio Bridge member)")
                .Return("frame", ArgTypes.Int, "frame of the last pump")
                .Return("realtime", ArgTypes.Float, "seconds since startup")
                .Return("screen", ArgTypes.String, "\"<width>x<height>\"")
                .Return("queued", ArgTypes.Int, "requests waiting or running"),
                ctx => Ping(s), inline: true);

            s.Commands.RegisterBuiltin(new CommandSpec("help", "Self-description: every command, or one command with examples.")
                {
                    Group = "bridge", Flags = CommandFlags.ReadOnly, Gate = ReadyGate.None, Since = since,
                }
                .Arg("name", ArgTypes.String, "one command (name or alias): adds examples, reply notes and timeouts")
                .Arg("group", ArgTypes.String, "only the commands of this group")
                .Return("protocol", ArgTypes.Int)
                .Return("commands", ArgTypes.ArrayOf(ArgTypes.Object), "{name, group, summary, args[{name, type, required, default, doc}], returns[], flags[], since, owner}", false)
                .Return("command", ArgTypes.Object, "with name: the one command", false)
                .Example("{\"cmd\":\"help\"}").Example("{\"cmd\":\"help\",\"name\":\"batch\"}"),
                ctx => Help(s, ctx), inline: true);

            s.Commands.RegisterBuiltin(new CommandSpec("bridge.version", "Versions of the protocol, bridge, Api, app and Unity; command and plugin counts.")
                {
                    Group = "bridge", Flags = CommandFlags.ReadOnly, Gate = ReadyGate.None, Since = since,
                }
                .Return("protocol", ArgTypes.Int).Return("bridge", ArgTypes.String).Return("api", ArgTypes.String)
                .Return("app", ArgTypes.String, "app version").Return("appName", ArgTypes.String).Return("unity", ArgTypes.String)
                .Return("commands", ArgTypes.Int).Return("plugins", ArgTypes.ArrayOf(ArgTypes.Object), "{guid, version, state}")
                .Return("allowQuit", ArgTypes.Bool).Return("tokenRequired", ArgTypes.Bool)
                .Return("build", ArgTypes.Object, "{kind (editor | dev | distribution), version, commit, dirty, builtUtc}: the build this app runs", false),
                ctx => Version(s), inline: true);

            s.Commands.RegisterBuiltin(new CommandSpec("batch", "Runs commands in order as one queue entry (one model transaction in Core).")
                {
                    Group = "bridge", Gate = ReadyGate.None, Since = since,
                    ReturnsDoc = "results[] holds each member's own reply ({ok, cmd, ...}). On failure ok is false and failedAt is the index of the " +
                                 "first failed member (error / code repeat its error). Members run within one frame unless one defers (capture, waits, " +
                                 "loads); the next member then runs at the next pump. A member's \"as\": \"name\" lets later members use \"$name\" or " +
                                 "\"$name.path.0.key\" in any argument value (only names defined earlier in the same batch are replaced). Members inherit " +
                                 "strict and undo from the batch; the batch's timeout covers all members (default: the sum of the members' defaults, at most 600 s).",
                }
                .Arg("cmds", ArgTypes.ArrayOf(ArgTypes.Object), "the member requests {cmd, ..., as?}", required: true)
                .Arg("stopOnError", ArgTypes.Bool, "stop at the first failed member", defaultValue: true)
                .Return("results", ArgTypes.ArrayOf(ArgTypes.Object), "member replies in order")
                .Return("failedAt", ArgTypes.Int, "index of the first failed member", false)
                .Example("{\"cmd\":\"batch\",\"cmds\":[{\"cmd\":\"ping\",\"as\":\"p\"},{\"cmd\":\"help\",\"name\":\"$p.cmd\"}]}"),
                ctx => throw new BridgeException(BridgeCodes.Internal, "batch is run by the dispatcher"), inline: false);

            s.Commands.RegisterBuiltin(new CommandSpec("log.tail", "The newest log entries of the app (Unity's log, all threads).")
                {
                    Group = "log", Flags = CommandFlags.ReadOnly, Gate = ReadyGate.None, Since = since,
                }
                .Arg("n", ArgTypes.Int, "entries to return (1-1000)", defaultValue: 50)
                .Arg("level", ArgTypes.Enum("log", "warning", "error"), "lowest severity (error includes exceptions and asserts)", defaultValue: "log")
                .Arg("since", ArgTypes.Int, "only entries with seq greater than this (a previous reply's last)", defaultValue: 0)
                .Arg("stack", ArgTypes.Bool, "include stack traces", defaultValue: false)
                .Return("entries", ArgTypes.ArrayOf(ArgTypes.Object), "{seq, time (UTC), type (log|warning|error|exception|assert), message, stack?}, oldest first")
                .Return("last", ArgTypes.Int, "seq of the newest entry in the buffer (pass as since next time)")
                .Return("gap", ArgTypes.Bool, "entries after since were already dropped from the buffer")
                .Return("capacity", ArgTypes.Int),
                ctx => LogTail(s, ctx), inline: true);
        }

        static object Ping(BridgeServer s)
        {
            var ready = new JObject();
            foreach (var kv in s.Readiness.Snapshot()) ready[kv.Key] = kv.Value;
            bool appReady = s.Readiness.AppReady;
            var o = new JObject
            {
                ["protocol"] = BridgeProtocol.Version,
                ["app"] = s.Options.AppName,
                ["bridge"] = BridgeVersion.Semver,
                ["api"] = HS2Next.ApiVersion.Semver,
                ["ready"] = ready,
                ["appReady"] = appReady,
                ["frame"] = s.Frames.Frame,
                ["realtime"] = s.Realtime,
                ["screen"] = s.Screen,
                ["queued"] = s.QueueLength,
                ["studio"] = appReady,
                ["version"] = 1,
            };
            var details = s.Readiness.Details();
            if (details.Count > 0)
            {
                var d = new JObject();
                foreach (var kv in details) d[kv.Key] = kv.Value;
                o["readyDetail"] = d;
            }
            return o;
        }

        static object Version(BridgeServer s)
        {
            var plugins = new JArray();
            try
            {
                var list = s.PluginInfo?.Invoke();
                if (list != null)
                    foreach (var p in list) plugins.Add(new JObject { ["guid"] = p.Guid, ["version"] = p.Version, ["state"] = p.State });
            }
            catch (Exception e) { s.Report(e, "plugin list"); }
            return new JObject
            {
                ["protocol"] = BridgeProtocol.Version,
                ["bridge"] = BridgeVersion.Semver,
                ["api"] = HS2Next.ApiVersion.Semver,
                ["app"] = s.Options.AppVersion,
                ["appName"] = s.Options.AppName,
                ["unity"] = s.Options.UnityVersion,
                ["commands"] = s.Commands.Specs.Count,
                ["plugins"] = plugins,
                ["allowQuit"] = s.Options.AllowQuit,
                ["tokenRequired"] = s.Token != null,
                ["build"] = s.Options.Build?.ToJson(),
            };
        }

        static object Help(BridgeServer s, ICommandContext ctx)
        {
            string name = ctx.Args.Get<string>("name");
            string group = ctx.Args.Get<string>("group");
            if (name != null)
            {
                var spec = s.Commands.Find(name);
                if (spec == null)
                    throw new BridgeException(BridgeError.NotFound("no command " + name, "name", NameMatch.Candidates(name, s.Commands.AllNames())));
                return new JObject { ["protocol"] = BridgeProtocol.Version, ["command"] = HelpSchema.Describe(spec, true) };
            }
            var arr = new JArray();
            foreach (var spec in s.Commands.Specs)
                if (group == null || string.Equals(spec.Group, group, StringComparison.Ordinal)) arr.Add(HelpSchema.Describe(spec, false));
            if (group != null && arr.Count == 0)
            {
                var groups = new HashSet<string>();
                foreach (var spec in s.Commands.Specs) groups.Add(spec.Group);
                throw new BridgeException(BridgeError.NotFound("no command in group " + group, "group", NameMatch.Candidates(group, groups)));
            }
            return new JObject { ["protocol"] = BridgeProtocol.Version, ["count"] = arr.Count, ["commands"] = arr };
        }

        static object LogTail(BridgeServer s, ICommandContext ctx)
        {
            int n = ctx.Args.Get("n", 50);
            if (n < 1 || n > 1000) throw new BridgeException(BridgeError.BadArgs("n", "int (1..1000)"));
            string level = ctx.Args.Get("level", "log");
            LogEntryType min;
            switch ((level ?? "log").ToLowerInvariant())
            {
                case "log": case "all": case "info": min = LogEntryType.Log; break;
                case "warning": case "warn": min = LogEntryType.Warning; break;
                case "error": case "exception": case "assert": min = LogEntryType.Error; break;
                default: throw new BridgeException(BridgeError.BadArgs("level", ArgTypes.Enum("log", "warning", "error")));
            }
            long since = ctx.Args.Get("since", 0L);
            bool stack = ctx.Args.Get("stack", false);
            var entries = new JArray();
            var list = s.Log.Tail(n, min, since, out bool gap);
            foreach (var e in list)
            {
                var o = new JObject
                {
                    ["seq"] = e.Seq,
                    ["time"] = e.TimeUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture),
                    ["type"] = e.Type.ToString().ToLowerInvariant(),
                    ["message"] = e.Message,
                };
                if (stack && e.Stack != null) o["stack"] = e.Stack;
                entries.Add(o);
            }
            return new JObject { ["entries"] = entries, ["last"] = s.Log.LastSeq, ["gap"] = gap, ["capacity"] = s.Log.Capacity };
        }
    }

    /// <summary>The JSON form of a command spec for help.</summary>
    static class HelpSchema
    {
        internal static JObject Describe(CommandSpec spec, bool full)
        {
            var o = new JObject
            {
                ["name"] = spec.Name,
                ["group"] = spec.Group,
                ["summary"] = spec.Summary ?? "",
                ["args"] = Fields(spec.Args, true),
                ["returns"] = Fields(spec.Returns, false),
                ["flags"] = Flags(spec.Flags),
                ["since"] = spec.Since,
                ["owner"] = spec.Owner,
            };
            if (spec.Aliases.Count > 0) o["aliases"] = new JArray(new List<string>(spec.Aliases).ToArray());
            if (!full) return o;
            if (spec.ReturnsDoc != null) o["returnsDoc"] = spec.ReturnsDoc;
            var ex = new JArray();
            foreach (var e in spec.Examples)
            {
                try { ex.Add(BridgeJson.Parse(e)); }
                catch (Exception) { ex.Add(e); }
            }
            o["examples"] = ex;
            o["defaultTimeoutSec"] = spec.DefaultTimeoutSec > 0 ? spec.DefaultTimeoutSec : BridgeProtocol.DefaultTimeoutSec;
            o["gate"] = spec.Gate == ReadyGate.App ? "app" : "none";
            if (spec.Requires.Count > 0) o["requires"] = new JArray(new List<string>(spec.Requires).ToArray());
            o["allowExtraArgs"] = spec.AllowExtraArgs;
            o["handlesWaitFrames"] = spec.HandlesWaitFrames;
            return o;
        }

        static JArray Fields(IReadOnlyList<FieldSpec> fields, bool args)
        {
            var a = new JArray();
            foreach (var f in fields)
            {
                var o = new JObject { ["name"] = f.Name, ["type"] = f.Type, [args ? "required" : "always"] = f.Required };
                if (args && f.Default != null)
                {
                    try { o["default"] = BridgeJson.FromResult(f.Default); }
                    catch (Exception) { o["default"] = f.Default.ToString(); }
                }
                if (f.Doc != null) o["doc"] = f.Doc;
                a.Add(o);
            }
            return a;
        }

        static JArray Flags(CommandFlags f)
        {
            var a = new JArray();
            if ((f & CommandFlags.ReadOnly) != 0) a.Add("readOnly");
            if ((f & CommandFlags.Mutates) != 0) a.Add("mutates");
            if ((f & CommandFlags.WritesFiles) != 0) a.Add("writesFiles");
            if ((f & CommandFlags.Slow) != 0) a.Add("slow");
            if ((f & CommandFlags.Defers) != 0) a.Add("defers");
            if ((f & CommandFlags.Experimental) != 0) a.Add("experimental");
            return a;
        }
    }
}
