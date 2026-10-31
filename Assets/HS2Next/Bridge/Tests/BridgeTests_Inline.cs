// The in-process inline call (BridgeServer.RunNow, the path of a plugin's synchronous call): the same reply as the queued
// path for ordinary commands, the refusals (batch, a command that may defer, a command reading waitFrames itself, an
// async handler, waitFrames, nesting deeper than 8, the app's gate for mutating commands), the readiness gate, aliases,
// a handler that defers anyway, and the "defers" name in help.
using System;
using System.Threading.Tasks;
using HS2Next.Api.Bridge;
using Newtonsoft.Json.Linq;

namespace HS2Next.Bridge.Tests
{
    public static class InlineGroup
    {
        public static void Run(BridgeChecks t)
        {
            using (var s = Kit.NewServer(start: false))
            {
                s.Start(false);
                s.ManualFrame();
                int ran = 0;
                s.Commands.Register(new CommandSpec("t.echo").Arg("v", ArgTypes.Int).Alias("t.old").WithGate(ReadyGate.None).WithFlags(CommandFlags.ReadOnly),
                    ctx => { ran++; return new BridgeReply().With("v", ctx.Args.Get("v", 0)).With("undo", ctx.Undo).With("frame", ctx.Frames.Frame); });
                s.Commands.Register(new CommandSpec("t.write").WithGate(ReadyGate.None).WithFlags(CommandFlags.Mutates), ctx => new BridgeReply().With("wrote", true));
                s.Commands.Register(new CommandSpec("t.later").WithGate(ReadyGate.None).WithFlags(CommandFlags.Defers), ctx => { ctx.Defer().Complete(1); return null; });
                s.Commands.Register(new CommandSpec("t.waits") { HandlesWaitFrames = true, Gate = ReadyGate.None }, ctx => 1);
                s.Commands.RegisterAsync(new CommandSpec("t.async").WithGate(ReadyGate.None), ctx => Task.FromResult<object>(1));
                s.Commands.Register(new CommandSpec("t.sneaky").WithGate(ReadyGate.None), ctx => { ctx.Defer(); return null; });
                s.Commands.Register(new CommandSpec("t.gated"), ctx => 1);
                s.Commands.Register(new CommandSpec("t.nest").Arg("n", ArgTypes.Int).WithGate(ReadyGate.None),
                    ctx =>
                    {
                        int n = ctx.Args.Get("n", 0);
                        string inner = s.RunNow("{\"cmd\":\"t.nest\",\"n\":" + (n + 1) + "}");
                        return new BridgeReply().With("n", n).With("inner", new RawJson(inner));
                    });

                string q = s.Execute("{\"cmd\":\"t.echo\",\"v\":3,\"id\":7}");
                string r = s.RunNow("{\"cmd\":\"t.echo\",\"v\":3,\"id\":7}");
                var qo = JObject.Parse(q);
                var ro = JObject.Parse(r);
                qo.Remove("frame");
                ro.Remove("frame");
                t.Equal(qo.ToString(), ro.ToString(), "inline reply = queued reply (frame aside)");
                int before = ran;
                r = s.RunNow("{\"cmd\":\"t.echo\",\"v\":4}");
                t.Check(ran == before + 1 && Kit.Str(JObject.Parse(r), "v") == "4", "runs at once, on this thread");
                var a = JObject.Parse(s.RunNow("{\"cmd\":\"t.old\",\"v\":1}"));
                t.Check(Kit.Ok(a) && a["warnings"]?.ToString().Contains("deprecated: use t.echo") == true, "an alias warns as on the queued path");
                t.Equal("false", Kit.Str(JObject.Parse(s.RunNow("{\"cmd\":\"t.echo\",\"undo\":false}")), "undo"), "the undo envelope field reaches the handler");
                var bad = JObject.Parse(s.RunNow("{\"cmd\":\"t.echo\",\"v\":\"x\"}"));
                t.Equal(BridgeCodes.BadArgs, Kit.Code(bad), "argument errors as on the queued path");
                var strict = JObject.Parse(s.RunNow("{\"cmd\":\"t.echo\",\"zz\":1,\"strict\":true}"));
                t.Equal(BridgeCodes.BadArgs, Kit.Code(strict), "strict unknown argument");
                var tw = JObject.Parse(s.RunNow("{\"cmd\":\"t.echo\",\"timeout\":5}"));
                t.Check(Kit.Ok(tw) && tw["warnings"]?.ToString().Contains("timeout is ignored") == true, "timeout ignored with a warning");

                foreach (var (req, what) in new[]
                {
                    ("{\"cmd\":\"batch\",\"cmds\":[{\"cmd\":\"t.echo\"}]}", "batch"),
                    ("{\"cmd\":\"t.later\"}", "a Defers command"),
                    ("{\"cmd\":\"t.waits\"}", "a command reading waitFrames itself"),
                    ("{\"cmd\":\"t.async\"}", "an async command"),
                    ("{\"cmd\":\"t.echo\",\"waitFrames\":2}", "waitFrames > 0"),
                })
                {
                    var o = JObject.Parse(s.RunNow(req));
                    t.Check(Kit.Code(o) == BridgeCodes.Unsupported && ((string)o["error"]).Contains("use the queued call"), "refused before running: " + what + " (" + Kit.Code(o) + ")");
                }
                var sn = JObject.Parse(s.RunNow("{\"cmd\":\"t.sneaky\"}"));
                t.Check(Kit.Code(sn) == BridgeCodes.Internal && ((string)sn["error"]).Contains("called inline and cannot defer"), "a handler deferring anyway answers internal: " + sn["error"]);
                var nest = JObject.Parse(s.RunNow("{\"cmd\":\"t.nest\",\"n\":0}"));
                var deepest = nest;
                int depth = 0;
                while (deepest?["inner"] is JObject inner && Kit.Ok(inner)) { deepest = inner; depth++; }
                t.Check(depth == 7 && Kit.Code((JObject)deepest["inner"]) == BridgeCodes.Busy, "nesting: 8 levels run, the 9th answers busy (" + depth + ")");
                t.Equal(BridgeCodes.UnknownCommand, Kit.Code(JObject.Parse(s.RunNow("{\"cmd\":\"t.nope\"}"))), "unknown command");
                t.Equal(BridgeCodes.BadJson, Kit.Code(JObject.Parse(s.RunNow("{"))), "bad JSON");
                t.Equal(BridgeCodes.BadArgs, Kit.Code(JObject.Parse(s.RunNow("{\"v\":1}"))), "missing cmd");

                s.InlineGate = spec => spec.Name == "t.write" ? "a scene transaction is open" : null;
                var g = JObject.Parse(s.RunNow("{\"cmd\":\"t.write\"}"));
                t.Check(Kit.Code(g) == BridgeCodes.Busy && (string)g["error"] == "a scene transaction is open", "the app's gate answers busy for a mutating command");
                t.Check(Kit.Ok(JObject.Parse(s.RunNow("{\"cmd\":\"t.echo\"}"))), "the gate does not apply to a read");
                t.Check(Kit.Ok(JObject.Parse(s.Execute("{\"cmd\":\"t.write\"}"))), "the gate does not apply to the queued path");
                s.InlineGate = null;
                t.Check(Kit.Ok(JObject.Parse(s.RunNow("{\"cmd\":\"t.write\"}"))), "no gate: the write runs");

                s.Readiness.Declare("app", true);
                var nr = JObject.Parse(s.RunNow("{\"cmd\":\"t.gated\"}"));
                t.Equal(BridgeCodes.NotReady, Kit.Code(nr), "the readiness gate applies");
                s.Readiness.Set("app", true);
                t.Check(Kit.Ok(JObject.Parse(s.RunNow("{\"cmd\":\"t.gated\"}"))), "ready: runs");

                var h = JObject.Parse(s.Execute("{\"cmd\":\"help\",\"name\":\"t.later\"}"));
                t.Check(h["command"]?["flags"]?.ToString(Newtonsoft.Json.Formatting.None) == "[\"defers\"]", "help lists defers: " + h["command"]?["flags"]);

                Exception off = null;
                var th = new System.Threading.Thread(() => { try { s.RunNow("{\"cmd\":\"t.echo\"}"); } catch (Exception e) { off = e; } });
                th.Start();
                th.Join();
                t.Check(off is InvalidOperationException, "off the main thread: InvalidOperationException");
            }
        }
    }
}
