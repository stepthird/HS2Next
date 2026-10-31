// Bridge tests: batch, help schema and the registry.
using System;
using System.Collections.Generic;
using System.Threading;
using HS2Next.Api.Bridge;
using Newtonsoft.Json.Linq;

namespace HS2Next.Bridge.Tests
{
    sealed class RecordingHook : IBatchHook
    {
        public readonly List<string> Events = new List<string>();
        public void BatchBegin(IBatchScope b) { b.Items["tx"] = "open"; Events.Add("begin " + b.Count + " " + b.Index + " " + b.StopOnError); }
        public void BatchEnd(IBatchScope b, bool ok, int failedAt) => Events.Add("end " + ok + " " + failedAt + " " + b.Items["tx"]);
    }

    public sealed class HelpDto
    {
        [BridgeArg("node id", Type = ArgTypes.Node, Required = true)] public string Key;
        [BridgeArg("position", Type = ArgTypes.Vec3)] public float[] Pos;
        public int Limit = 50;
        public TestMode Mode = TestMode.Fast;
        public List<int> Ids;
        public bool Loop;
        public string Q;
    }

    public static class BatchGroup
    {
        public static void Run(BridgeChecks t)
        {
            using (var s = Kit.NewServer(start: false))
            {
                var order = new List<string>();
                var hook = new RecordingHook();
                s.AddBatchHook(hook);
                s.Commands.Register(new CommandSpec("t.a") { AllowExtraArgs = true, Gate = ReadyGate.None }, ctx =>
                {
                    order.Add("a" + ctx.Args.Get("n", 0) + "@" + ctx.Frames.Frame + (ctx.Batch != null ? " tx=" + ctx.Batch.Items["tx"] + " i=" + ctx.Batch.Index : ""));
                    return new BridgeReply().With("n", ctx.Args.Get("n", 0)).With("key", "node-" + ctx.Args.Get("n", 0)).With("list", new[] { "x", "y" });
                });
                s.Commands.Register(new CommandSpec("t.fail") { Gate = ReadyGate.None }, ctx => throw new BridgeException(BridgeCodes.NotFound, "nope"));
                IDeferredReply pending = null;
                s.Commands.Register(new CommandSpec("t.defer") { Gate = ReadyGate.None }, ctx =>
                {
                    order.Add("defer@" + ctx.Frames.Frame);
                    pending = ctx.Defer();
                    return null;
                });
                s.Commands.Register(new CommandSpec("t.strictProbe").Arg("v", ArgTypes.Int).WithGate(ReadyGate.None), ctx => new BridgeReply().With("strict", ctx.Strict).With("undo", ctx.Undo));

                var r = Kit.Exec(s, "{\"cmd\":\"batch\",\"id\":5,\"cmds\":[{\"cmd\":\"t.a\",\"n\":1},{\"cmd\":\"t.a\",\"n\":2,\"id\":\"m2\"},{\"cmd\":\"ping\"}]}");
                t.Check(Kit.Ok(r), "batch ok");
                t.Equal("batch", (string)r?["cmd"], "batch cmd");
                t.Equal("5", Kit.Str(r, "id"), "batch id echoed");
                t.Equal(3, (r?["results"] as JArray)?.Count ?? -1, "three results");
                t.Equal("1", Kit.Str(r, "results[0].n"), "result 0");
                t.Equal("m2", Kit.Str(r, "results[1].id"), "member id echoed inside");
                t.Equal("ping", Kit.Str(r, "results[2].cmd"), "ping as a member runs on the main thread");
                t.Check(r?["failedAt"] == null, "no failedAt when ok");
                t.Check(order.Count == 2 && order[0].Split('@')[1].Split(' ')[0] == order[1].Split('@')[1].Split(' ')[0], "members ran within one frame: " + string.Join(" | ", order));
                t.Check(order.Count == 2 && order[0].Contains("tx=open i=0") && order[1].Contains("i=1"), "handlers see the batch scope (Items, Index)");
                t.Equal("begin 3 -1 True|end True -1 open", string.Join("|", hook.Events), "hooks: begin before, end after with ok");

                hook.Events.Clear();
                order.Clear();
                r = Kit.Exec(s, "{\"cmd\":\"batch\",\"cmds\":[{\"cmd\":\"t.a\",\"n\":1},{\"cmd\":\"t.fail\"},{\"cmd\":\"t.a\",\"n\":3}]}");
                t.Check(!Kit.Ok(r), "stopOnError: batch not ok");
                t.Equal("1", Kit.Str(r, "failedAt"), "failedAt = 1");
                t.Equal(2, (r?["results"] as JArray)?.Count ?? -1, "stopOnError: later members not run");
                t.Equal(BridgeCodes.NotFound, Kit.Code(r), "batch code repeats the member's code");
                t.Check(((string)r?["error"])?.StartsWith("member 1 (t.fail) failed") == true, "batch error names the member");
                t.Equal("end False 1 open", hook.Events.Count > 1 ? hook.Events[1] : "", "hook end with ok false, failedAt 1");

                order.Clear();
                r = Kit.Exec(s, "{\"cmd\":\"batch\",\"stopOnError\":false,\"cmds\":[{\"cmd\":\"t.fail\"},{\"cmd\":\"t.a\",\"n\":3},{\"cmd\":\"no.such\"}]}");
                t.Check(!Kit.Ok(r) && Kit.Str(r, "failedAt") == "0", "stopOnError false: failedAt = first failure");
                t.Equal(3, (r?["results"] as JArray)?.Count ?? -1, "stopOnError false: every member ran");
                t.Equal(BridgeCodes.UnknownCommand, Kit.Str(r, "results[2].code"), "unknown command inside a batch");

                // validation at submission
                foreach (var bad in new[]
                         {
                             "{\"cmd\":\"batch\"}", "{\"cmd\":\"batch\",\"cmds\":{}}", "{\"cmd\":\"batch\",\"cmds\":[1]}", "{\"cmd\":\"batch\",\"cmds\":[{\"x\":1}]}",
                             "{\"cmd\":\"batch\",\"cmds\":[{\"cmd\":\"batch\",\"cmds\":[]}]}", "{\"cmd\":\"batch\",\"stopOnError\":1,\"cmds\":[]}",
                             "{\"cmd\":\"batch\",\"cmds\":[{\"cmd\":\"t.a\",\"as\":\"x\"},{\"cmd\":\"t.a\",\"as\":\"x\"}]}", "{\"cmd\":\"batch\",\"cmds\":[{\"cmd\":\"t.a\",\"as\":\"1x\"}]}",
                         })
                {
                    var rr = JObject.Parse(s.SubmitAsync(bad).Result);
                    t.Equal(BridgeCodes.BadArgs, Kit.Code(rr), "batch validation: " + bad);
                }
                r = Kit.Exec(s, "{\"cmd\":\"batch\",\"cmds\":[]}");
                t.Check(Kit.Ok(r) && ((JArray)r["results"]).Count == 0, "empty batch ok");

                // "as" / "$name" substitution
                r = Kit.Exec(s, "{\"cmd\":\"batch\",\"cmds\":[{\"cmd\":\"t.a\",\"n\":7,\"as\":\"first\"},{\"cmd\":\"t.a\",\"n\":\"$first.n\"}]}");
                t.Check(Kit.Ok(r), "substitution batch ok: " + Kit.Str(r, "error"));
                t.Equal("node-7", Kit.Str(r, "results[1].key"), "the member saw the substituted number (\"$first.n\" -> 7)");
                Kit.RegisterEcho(s, "t.echo");
                r = Kit.Exec(s, "{\"cmd\":\"batch\",\"cmds\":[{\"cmd\":\"t.a\",\"n\":7,\"as\":\"first\"},{\"cmd\":\"t.echo\",\"n\":\"$first.n\",\"k\":\"$first.key\",\"l\":\"$first.list.1\"," +
                                "\"whole\":\"$first.cmd\",\"keep\":\"$undefined.x\",\"plain\":\"$\",\"deep\":{\"arr\":[\"$first.n\"]}}]}");
                t.Equal("7", Kit.Str(r, "results[1].n"), "number substituted");
                t.Equal("node-7", Kit.Str(r, "results[1].k"), "string substituted");
                t.Equal("y", Kit.Str(r, "results[1].l"), "array index in the path");
                t.Equal("t.a", Kit.Str(r, "results[1].whole"), "envelope field of the result");
                t.Equal("$undefined.x", Kit.Str(r, "results[1].keep"), "names not defined in the batch are left alone");
                t.Equal("$", Kit.Str(r, "results[1].plain"), "a lone $ is left alone");
                t.Equal("[7]", Kit.Str(r, "results[1].deep.arr"), "substitution inside nested arrays and objects");
                r = Kit.Exec(s, "{\"cmd\":\"batch\",\"cmds\":[{\"cmd\":\"t.a\",\"n\":7,\"as\":\"first\"},{\"cmd\":\"t.echo\",\"v\":\"$first.nope\"}]}");
                t.Check(!Kit.Ok(r) && Kit.Str(r, "results[1].code") == BridgeCodes.BadArgs && Kit.Str(r, "results[1].detail.arg") == "v", "a missing path = bad_args for that member");

                // strict / undo inheritance
                r = Kit.Exec(s, "{\"cmd\":\"batch\",\"strict\":true,\"undo\":false,\"cmds\":[{\"cmd\":\"t.strictProbe\",\"v\":1},{\"cmd\":\"t.strictProbe\",\"strict\":false,\"undo\":true,\"zz\":1}]}");
                t.Check(Kit.Str(r, "results[0].strict") == "true" && Kit.Str(r, "results[0].undo") == "false", "members inherit strict and undo");
                t.Check(Kit.Str(r, "results[1].strict") == "false" && Kit.Str(r, "results[1].undo") == "true", "a member's own strict / undo wins");
                r = Kit.Exec(s, "{\"cmd\":\"batch\",\"strict\":true,\"cmds\":[{\"cmd\":\"t.strictProbe\",\"zz\":1}]}");
                t.Equal(BridgeCodes.BadArgs, Kit.Str(r, "results[0].code"), "inherited strict rejects unknown arguments");

                // a deferred member: the next member runs at the next pump; the batch is one queue entry
                order.Clear();
                hook.Events.Clear();
                var tb = s.SubmitAsync("{\"cmd\":\"batch\",\"cmds\":[{\"cmd\":\"t.a\",\"n\":1},{\"cmd\":\"t.defer\"},{\"cmd\":\"t.a\",\"n\":2}]}");
                var other = s.SubmitAsync("{\"cmd\":\"t.a\",\"n\":9}");
                int f1 = s.ManualFrame();
                t.Check(pending != null && !tb.IsCompleted, "batch waits on the deferred member");
                s.ManualFrame();
                s.ManualFrame();
                t.Check(!other.IsCompleted, "a later request waits behind the batch (FIFO, one queue entry)");
                pending.Complete(new BridgeReply().With("late", true));
                t.Check(!tb.IsCompleted, "completion does not run the next member off the pump");
                int f4 = s.ManualFrame();
                t.Check(tb.IsCompleted, "batch finished at the next pump");
                t.Check(other.IsCompleted, "the waiting request ran right after the batch");
                t.Equal("a1@" + f1 + " tx=open i=0|defer@" + f1 + "|a2@" + f4 + " tx=open i=2|a9@" + f4, string.Join("|", order), "member frames");
                var br = JObject.Parse(tb.Result);
                t.Equal("true", Kit.Str(br, "results[1].late"), "deferred member result in results[]");

                // waitFrames on a member and on the batch
                order.Clear();
                var tw = s.SubmitAsync("{\"cmd\":\"batch\",\"waitFrames\":2,\"cmds\":[{\"cmd\":\"t.a\",\"n\":1,\"waitFrames\":3},{\"cmd\":\"t.a\",\"n\":2}]}");
                int g1 = s.ManualFrame();
                int frames = 1;
                while (!tw.IsCompleted && frames < 20) { s.ManualFrame(); frames++; }
                t.Equal("a1@" + g1 + "|a2@" + (g1 + 3), string.Join("|", order).Replace(" tx=open i=0", "").Replace(" tx=open i=1", ""), "member waitFrames 3 delays the next member by 3 frames");
                t.Equal(6, frames, "batch reply after member hold 3 + batch waitFrames 2 (frame g+5 = 6th pump)");
            }

            // batch timeout abandons and ends the scope
            using (var s = Kit.NewServer(o => o.ReaperIntervalMs = 10))
            {
                var hook = new RecordingHook();
                s.AddBatchHook(hook);
                s.Commands.Register(new CommandSpec("t.never") { Gate = ReadyGate.None }, ctx => { ctx.Defer(); return null; });
                var tb = s.SubmitAsync("{\"cmd\":\"batch\",\"timeout\":0.2,\"cmds\":[{\"cmd\":\"t.never\"}]}");
                s.ManualFrame();
                bool done = Kit.WaitFor(() => tb.IsCompleted, 3000);
                t.Check(done && Kit.Code(JObject.Parse(tb.Result)) == BridgeCodes.Timeout, "batch timeout answered");
                s.ManualFrame();
                t.Equal("end False 0 open", hook.Events.Count > 1 ? hook.Events[1] : string.Join("|", hook.Events), "timed-out batch ends its scope (ok false, failedAt 0)");
            }
        }
    }

    public static class HelpGroup
    {
        public static void Run(BridgeChecks t)
        {
            using (var s = Kit.NewServer(start: false))
            {
                var reg = s.Commands;
                var spec = CommandSpec.For<HelpDto>("chara.thing", "Does a thing.")
                    .WithFlags(CommandFlags.Mutates | CommandFlags.Slow).WithSince("0.2.0").WithTimeout(300).Alias("chara.oldThing")
                    .Return("added", ArgTypes.ArrayOf(ArgTypes.Object), "new nodes").Example("{\"cmd\":\"chara.thing\",\"key\":\"k\"}").Require("scene");
                spec.ReturnsDoc = "notes";
                reg.Register(spec, ctx => new BridgeReply().With("k", ctx.Args.Get<string>("key")));
                reg.Register(new CommandSpec("lab.go") { Owner = "stp3.lab", Summary = "plugin" }, ctx => null);

                var r = Kit.Exec(s, "{\"cmd\":\"help\"}");
                t.Check(Kit.Ok(r), "help ok");
                var cmds = r?["commands"] as JArray;
                var names = new List<string>();
                if (cmds != null) foreach (var c in cmds) names.Add((string)c["name"]);
                t.Equal("batch,bridge.version,chara.thing,help,lab.go,log.tail,ping", string.Join(",", names), "help lists every command by name");
                t.Equal(names.Count, (int?)r?["count"] ?? -1, "help count");
                JObject thing = null;
                if (cmds != null) foreach (JObject c in cmds) if ((string)c["name"] == "chara.thing") thing = c;
                t.Check(thing != null, "chara.thing listed");
                if (thing != null)
                {
                    t.Equal("chara", (string)thing["group"], "group from the name");
                    t.Equal("Does a thing.", (string)thing["summary"], "summary");
                    t.Equal("[\"mutates\",\"slow\"]", Kit.Str(thing, "flags"), "flags");
                    t.Equal("0.2.0", (string)thing["since"], "since");
                    t.Equal("core", (string)thing["owner"], "owner core by default");
                    t.Equal("[\"chara.oldThing\"]", Kit.Str(thing, "aliases"), "aliases");
                    var args = (JArray)thing["args"];
                    var an = new List<string>();
                    foreach (var a in args) an.Add((string)a["name"] + ":" + (string)a["type"] + ((bool)a["required"] ? "!" : "") + (a["default"] != null ? "=" + a["default"].ToString(Newtonsoft.Json.Formatting.None) : ""));
                    t.Equal("key:node!,pos:vec3,limit:int=50,mode:enum(Fast|Precise)=\"Fast\",ids:array<int>,loop:bool=false,q:string", string.Join(",", an), "args from the DTO (names, types, required, defaults)");
                    t.Equal("node id", Kit.Str(thing, "args[0].doc"), "arg doc from [BridgeArg]");
                    t.Equal("added", Kit.Str(thing, "returns[0].name"), "returns");
                    t.Check(thing["examples"] == null && thing["returnsDoc"] == null, "the list leaves out examples and reply notes");
                }
                r = Kit.Exec(s, "{\"cmd\":\"help\",\"name\":\"chara.oldThing\"}");
                var one = r?["command"] as JObject;
                t.Check(one != null && (string)one["name"] == "chara.thing", "help {name} through an alias");
                t.Equal("chara.thing", Kit.Str(one, "examples[0].cmd"), "examples parsed as JSON");
                t.Equal("notes", Kit.Str(one, "returnsDoc"), "returnsDoc");
                t.Equal(300.0, (double?)one?["defaultTimeoutSec"] ?? -1, "defaultTimeoutSec");
                t.Equal("[\"scene\"]", Kit.Str(one, "requires"), "requires");
                t.Equal("app", Kit.Str(one, "gate"), "gate");
                r = Kit.Exec(s, "{\"cmd\":\"help\",\"name\":\"chara.thng\"}");
                t.Check(Kit.Code(r) == BridgeCodes.NotFound && Kit.Str(r, "detail.candidates")?.Contains("chara.thing") == true, "help of an unknown name: not_found with candidates");
                r = Kit.Exec(s, "{\"cmd\":\"help\",\"group\":\"bridge\"}");
                t.Equal(4, (int?)r?["count"] ?? -1, "help {group: bridge}: ping, help, bridge.version, batch");
                r = Kit.Exec(s, "{\"cmd\":\"help\",\"group\":\"charaa\"}");
                t.Check(Kit.Code(r) == BridgeCodes.NotFound, "help of an empty group: not_found");
                s.Readiness.Declare("scene", false);
                s.Readiness.Set("scene", true);
                r = Kit.Exec(s, "{\"cmd\":\"chara.oldThing\",\"key\":\"q\"}");
                t.Check(Kit.Ok(r) && (string)r["cmd"] == "chara.thing" && Kit.Str(r, "warnings")?.Contains("deprecated: use chara.thing") == true, "an alias runs with a deprecation warning");
                r = Kit.Exec(s, "{\"cmd\":\"bridge.version\"}");
                t.Check(Kit.Ok(r) && (int)r["commands"] == 7 && (string)r["api"] == HS2Next.ApiVersion.Semver && (string)r["bridge"] == BridgeVersion.Semver, "bridge.version counts and versions");
                t.Equal("[]", Kit.Str(r, "plugins"), "bridge.version plugins (none)");
                s.PluginInfo = () => new[] { new BridgePluginInfo { Guid = "stp3.lab", Version = "1.0.0", State = "loaded" } };
                r = Kit.Exec(s, "{\"cmd\":\"bridge.version\"}");
                t.Equal("stp3.lab", Kit.Str(r, "plugins[0].guid"), "bridge.version plugin list from the provider");

                // registry rules
                t.Throws<ArgumentException>(() => reg.Register(new CommandSpec("chara.thing"), ctx => null), "duplicate name rejected");
                t.Throws<ArgumentException>(() => reg.Register(new CommandSpec("chara.oldThing"), ctx => null), "a name taken by an alias rejected");
                t.Throws<ArgumentException>(() => reg.Register(new CommandSpec("x.y").Alias("chara.thing"), ctx => null), "an alias taken by a name rejected");
                t.Throws<ArgumentException>(() => reg.Register(new CommandSpec("ping"), ctx => null), "reserved name rejected");
                t.Throws<ArgumentException>(() => reg.Register(new CommandSpec("bridge.extra"), ctx => null), "the bridge. group is reserved");
                t.Throws<ArgumentException>(() => reg.Register(new CommandSpec("x.z") { Owner = BridgeOwners.Bridge }, ctx => null), "owner bridge rejected");
                t.Throws<ArgumentException>(() => reg.Register(new CommandSpec("x.w").Alias("x.w"), ctx => null), "an alias equal to the name rejected");
                t.Throws<ArgumentException>(() => new CommandSpec("Bad.name"), "upper-case first letter rejected");
                t.Throws<ArgumentException>(() => new CommandSpec("a..b"), "empty name part rejected");
                t.Throws<ArgumentException>(() => new CommandSpec("a_b"), "underscore rejected");
                t.Throws<ArgumentException>(() => new CommandSpec("ok.x").Arg("waitFrames", ArgTypes.Int), "an envelope field cannot be an argument");
                t.Throws<ArgumentException>(() => new CommandSpec("ok.x").Arg("a", ArgTypes.Int).Arg("a", ArgTypes.Int), "an argument declared twice");
                t.Throws<InvalidOperationException>(() => spec.Summary = "changed", "a registered spec is frozen");
                t.Throws<ArgumentException>(() => reg.Register(spec, ctx => null), "a spec cannot be registered twice");
                t.Check(reg.Contains("chara.oldThing") && reg.Find("chara.oldThing") == spec, "Contains / Find through an alias");
                reg.RegisterAsync(new CommandSpec("lab.two") { Owner = "stp3.lab" }, ctx => System.Threading.Tasks.Task.FromResult<object>(null));
                t.Equal(2, reg.UnregisterOwner("stp3.lab"), "UnregisterOwner removes the owner's commands");
                t.Check(!reg.Contains("lab.go"), "removed");
                t.Check(!reg.Unregister("chara.oldThing"), "Unregister by alias does nothing");
                t.Check(!reg.Unregister("ping"), "the bridge's commands cannot be removed");
                t.Check(reg.Unregister("chara.thing") && !reg.Contains("chara.oldThing"), "Unregister removes the aliases too");
                t.Equal(0, reg.UnregisterOwner(BridgeOwners.Bridge), "UnregisterOwner(bridge) removes nothing");
                r = Kit.Exec(s, "{\"cmd\":\"help\"}");
                t.Equal(5, (int?)r?["count"] ?? -1, "help after removal: the five bridge commands");
                t.Equal("bridge", Kit.Str(r, "commands[0].owner"), "bridge commands are owned by bridge");
            }
            t.Equal("array<float>", ArgTypes.Infer(typeof(float[])), "ArgTypes.Infer(float[])");
            t.Equal("enum(Fast|Precise)", ArgTypes.Infer(typeof(TestMode)), "ArgTypes.Infer(enum)");
            t.Equal("object", ArgTypes.Infer(typeof(Dictionary<string, int>)), "ArgTypes.Infer(dictionary)");
            t.Equal("array<string>", ArgTypes.Infer(typeof(List<string>)), "ArgTypes.Infer(List<string>)");
            t.Equal("int", ArgTypes.Infer(typeof(long?)), "ArgTypes.Infer(long?)");
            t.Equal("contentId", ArgTypes.Infer(typeof(HS2Next.Content.ContentId)), "ArgTypes.Infer(ContentId)");
        }
    }
}
