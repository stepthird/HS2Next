// Bridge tests: deferred and async replies, the frame clock with update and late hooks, waitFrames, timeouts
//, the output-root guard and the log ring.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HS2Next.Api.Bridge;
using Newtonsoft.Json.Linq;

namespace HS2Next.Bridge.Tests
{
    public static class FramesGroup
    {
        public static void Run(BridgeChecks t)
        {
            using (var s = Kit.NewServer(start: false))
            {
                var trace = new List<string>();
                IDeferredReply held = null;
                s.Commands.Register(new CommandSpec("t.later") { Gate = ReadyGate.None }, ctx => { held = ctx.Defer(); trace.Add("later@" + ctx.Frames.Frame); return null; });
                s.Commands.Register(new CommandSpec("t.capture") { Gate = ReadyGate.None, HandlesWaitFrames = true }, ctx =>
                {
                    int wait = ctx.WaitFrames < 0 ? 1 : ctx.WaitFrames;
                    var d = ctx.Defer();
                    int asked = ctx.Frames.Frame;
                    ctx.Frames.AtLate(wait - 1, () => { trace.Add("late@" + ctx.Frames.Frame); d.Complete(new BridgeReply().With("asked", asked).With("frame", ctx.Frames.Frame)); });
                    return null;
                });
                s.Commands.Register(new CommandSpec("t.sync") { Gate = ReadyGate.None }, ctx => new BridgeReply().With("frame", ctx.Frames.Frame));
                s.Commands.Register(new CommandSpec("t.selfComplete") { Gate = ReadyGate.None }, ctx => { var d = ctx.Defer(); d.Complete(new BridgeReply().With("v", 1)); return null; });
                s.Commands.RegisterAsync(new CommandSpec("t.async") { Gate = ReadyGate.None }, async ctx =>
                {
                    int f0 = ctx.Frames.Frame;
                    await ctx.Frames.WaitFrames(2);
                    return new BridgeReply().With("from", f0).With("to", ctx.Frames.Frame);
                });
                var tcsHolder = new TaskCompletionSource<object>[1];
                s.Commands.RegisterAsync(new CommandSpec("t.asyncExternal") { Gate = ReadyGate.None }, ctx => (tcsHolder[0] = new TaskCompletionSource<object>()).Task);
                s.Commands.RegisterAsync(new CommandSpec("t.asyncFault") { Gate = ReadyGate.None }, async ctx =>
                {
                    // ConfigureAwait(false): in the Editor, continuations posted to Unity's synchronization context only run
                    // when the editor loop ticks, which a test that pumps by hand on the main thread never lets happen
                    await Task.Delay(1).ConfigureAwait(false);
                    throw new BridgeException(BridgeCodes.WrongKind, "not a chara");
                });
                s.Commands.RegisterAsync(new CommandSpec("t.asyncSync") { Gate = ReadyGate.None }, ctx => Task.FromResult<object>(new BridgeReply().With("v", 2)));
                s.Commands.Register(new CommandSpec("t.deferTwice") { Gate = ReadyGate.None }, ctx => { var a = ctx.Defer(); var b = ctx.Defer(); a.Complete(new BridgeReply().With("same", ReferenceEquals(a, b))); return null; });
                s.Commands.Register(new CommandSpec("t.deferThrow") { Gate = ReadyGate.None }, ctx => { ctx.Defer(); throw new InvalidOperationException("after defer"); });

                // a deferred reply completed at a later frame, on the main thread
                var task = s.SubmitAsync("{\"cmd\":\"t.later\"}");
                int f = s.ManualFrame();
                t.Check(held != null && !task.IsCompleted, "deferred: no reply after the handler returned");
                var next = s.SubmitAsync("{\"cmd\":\"t.sync\"}");
                s.ManualFrame();
                t.Check(!next.IsCompleted, "deferred: the job holds the queue");
                t.Check(held.Complete(new BridgeReply().With("x", 1)), "Complete returns true");
                t.Check(task.IsCompleted, "single command: the reply leaves at completion (no pump needed)");
                t.Equal("1", Kit.Str(JObject.Parse(task.Result), "x"), "deferred result");
                t.Check(!held.Complete(null) && !held.Fail(new BridgeError(BridgeCodes.Internal, "x")), "a second completion is ignored");
                int f3 = s.ManualFrame();
                t.Check(next.IsCompleted && (int)JObject.Parse(next.Result)["frame"] == f3, "the next request runs at the next pump");

                // completion from another thread
                task = s.SubmitAsync("{\"cmd\":\"t.later\"}");
                s.ManualFrame();
                var th = new Thread(() => held.Complete(new BridgeReply().With("thread", true)));
                th.Start();
                th.Join();
                t.Check(task.IsCompleted && Kit.Str(JObject.Parse(task.Result), "thread") == "true", "completion from another thread replies");
                task = s.SubmitAsync("{\"cmd\":\"t.later\"}");
                s.ManualFrame();
                held.Fail(new BridgeError(BridgeCodes.NotFound, "gone"));
                t.Equal(BridgeCodes.NotFound, Kit.Code(JObject.Parse(task.Result)), "Fail(BridgeError)");
                task = s.SubmitAsync("{\"cmd\":\"t.later\"}");
                s.ManualFrame();
                held.Fail(new ArgumentException("bad"));
                t.Equal(BridgeCodes.Internal, Kit.Code(JObject.Parse(task.Result)), "Fail(Exception) = internal");
                s.ManualFrame();

                t.Check(Kit.Ok(Kit.Exec(s, "{\"cmd\":\"t.selfComplete\"}")), "Defer + Complete inside the handler");
                var r = Kit.Exec(s, "{\"cmd\":\"t.deferTwice\"}");
                t.Equal("true", Kit.Str(r, "same"), "Defer is idempotent");
                r = Kit.Exec(s, "{\"cmd\":\"t.deferThrow\"}");
                t.Equal(BridgeCodes.Internal, Kit.Code(r), "a throw after Defer fails the deferred reply");

                // capture-like: late point of the same frame, waitFrames consumed by the handler
                trace.Clear();
                task = s.SubmitAsync("{\"cmd\":\"t.capture\"}");
                f = s.ManualFrame();
                t.Check(task.IsCompleted, "AtLate(0): the capture ran at the late point of the request's frame");
                r = JObject.Parse(task.Result);
                t.Check((int)r["asked"] == f && (int)r["frame"] == f, "AtLate(0) ran in frame " + f + " (" + r["frame"] + ")");
                task = s.SubmitAsync("{\"cmd\":\"t.capture\",\"waitFrames\":3}");
                f = s.ManualFrame();
                s.ManualFrame();
                t.Check(!task.IsCompleted, "AtLate(2): not yet after 2 frames");
                s.ManualFrame();
                t.Check(task.IsCompleted && (int)JObject.Parse(task.Result)["frame"] == f + 2, "AtLate(2) ran in frame f+2; HandlesWaitFrames: no extra delay");

                // envelope waitFrames on a synchronous command
                task = s.SubmitAsync("{\"cmd\":\"t.sync\",\"waitFrames\":2}");
                f = s.ManualFrame();
                t.Check(!task.IsCompleted, "waitFrames 2: no reply in the handler's frame");
                s.ManualFrame();
                t.Check(!task.IsCompleted, "waitFrames 2: no reply one frame later");
                int f2 = s.ManualFrame();
                t.Check(task.IsCompleted && f2 == f + 2, "waitFrames 2: reply at the pump of frame f+2");
                t.Equal(f, (int)JObject.Parse(task.Result)["frame"], "the handler ran in frame f");
                task = s.SubmitAsync("{\"cmd\":\"t.sync\",\"waitFrames\":0}");
                s.ManualFrame();
                t.Check(task.IsCompleted, "waitFrames 0: same frame");
                task = s.SubmitAsync("{\"cmd\":\"t.later\",\"waitFrames\":1}");
                f = s.ManualFrame();
                held.Complete(null);
                t.Check(!task.IsCompleted, "deferred + waitFrames 1: held after completion (frame " + f + ")");
                s.ManualFrame();
                t.Check(task.IsCompleted, "deferred + waitFrames 1: counted from the completion frame, answered at pump f+1");
                // completed at the late point of frame g with waitFrames 2: answered at the pump of g+2
                s.Commands.Register(new CommandSpec("t.lateDone") { Gate = ReadyGate.None }, ctx => { var d = ctx.Defer(); ctx.Frames.AtLate(0, () => d.Complete(null)); return null; });
                task = s.SubmitAsync("{\"cmd\":\"t.lateDone\",\"waitFrames\":2}");
                int g = s.ManualFrame();
                s.ManualFrame();
                t.Check(!task.IsCompleted, "late completion + waitFrames 2: not at g+1");
                int g2 = s.ManualFrame();
                t.Check(task.IsCompleted && g2 == g + 2, "late completion + waitFrames 2: at the pump of g+2");

                // async handlers
                task = s.SubmitAsync("{\"cmd\":\"t.async\"}");
                f = s.ManualFrame();
                s.ManualFrame();
                t.Check(!task.IsCompleted, "async: waiting for WaitFrames(2)");
                s.ManualFrame();
                t.Check(task.IsCompleted, "async: completed at the pump of frame f+2 (continuation without a sync context runs in the pump)");
                if (task.IsCompleted)
                {
                    r = JObject.Parse(task.Result);
                    t.Check((int)r["from"] == f && (int)r["to"] == f + 2, "async frames " + r["from"] + " -> " + r["to"]);
                }
                task = s.SubmitAsync("{\"cmd\":\"t.asyncExternal\"}");
                s.ManualFrame();
                t.Check(!task.IsCompleted && tcsHolder[0] != null, "async external task pending");
                var th2 = new Thread(() => tcsHolder[0].SetResult(new BridgeReply().With("ext", 1)));
                th2.Start();
                th2.Join();
                t.Check(task.IsCompleted && Kit.Str(JObject.Parse(task.Result), "ext") == "1", "async task completed on another thread replies");
                s.ManualFrame();
                task = s.SubmitAsync("{\"cmd\":\"t.asyncExternal\"}");
                s.ManualFrame();
                tcsHolder[0].SetCanceled();
                t.Equal(BridgeCodes.Timeout, Kit.Code(JObject.Parse(task.Result)), "a cancelled task answers timeout");
                s.ManualFrame();
                r = Kit.Exec(s, "{\"cmd\":\"t.asyncFault\"}");
                t.Equal(BridgeCodes.WrongKind, Kit.Code(r), "a faulted task answers with its BridgeException");
                r = Kit.Exec(s, "{\"cmd\":\"t.asyncSync\"}");
                t.Equal("2", Kit.Str(r, "v"), "an already completed task");

                // the clock's own hooks
                var order = new List<string>();
                int b = s.Frames.Frame;
                s.Frames.AtUpdate(2, () => order.Add("u2@" + s.Frames.Frame));
                s.Frames.AtUpdate(1, () => order.Add("u1@" + s.Frames.Frame));
                s.Frames.AtLate(0, () => order.Add("l0@" + s.Frames.Frame));
                s.Frames.AtLate(1, () => order.Add("l1@" + s.Frames.Frame));
                Action<int> started = fr => order.Add("S" + fr);
                Action<int> late = fr => order.Add("L" + fr);
                s.Frames.FrameStarted += started;
                s.Frames.LateFrame += late;
                var w1 = s.Frames.WaitFrames(1);
                var wl = s.Frames.WaitLate(0);
                s.Commands.Register(new CommandSpec("t.endOfPass") { Gate = ReadyGate.None }, ctx => { ctx.Frames.AtUpdate(0, () => order.Add("eop@" + ctx.Frames.Frame)); order.Add("h@" + ctx.Frames.Frame); return null; });
                task = s.SubmitAsync("{\"cmd\":\"t.endOfPass\"}");
                s.Frames.AtUpdate(1, () => order.Add("throws")); s.Frames.AtUpdate(1, () => throw new InvalidOperationException("scheduled action failure"));
                s.ManualFrame();
                s.ManualFrame();
                s.Frames.FrameStarted -= started;
                s.Frames.LateFrame -= late;
                t.Equal("S" + (b + 1) + ",u1@" + (b + 1) + ",throws,h@" + (b + 1) + ",eop@" + (b + 1) + ",l0@" + (b + 1) + ",L" + (b + 1) + ",S" + (b + 2) + ",u2@" + (b + 2) + ",l1@" + (b + 2) + ",L" + (b + 2),
                    string.Join(",", order), "frame order: FrameStarted, due updates, requests, AtUpdate(0) at the end of the pass, late actions, LateFrame");
                t.Check(w1.IsCompleted && wl.IsCompleted, "WaitFrames / WaitLate tasks completed");
                t.Throws<ArgumentOutOfRangeException>(() => s.Frames.AtLate(-1, () => { }), "negative afterFrames rejected");
                t.Check(s.Frames.Realtime > 0, "Realtime advances");
            }

            // timeouts
            using (var s = Kit.NewServer(o => o.ReaperIntervalMs = 10))
            {
                IDeferredReply never = null;
                CancellationToken token = default;
                s.Commands.Register(new CommandSpec("t.never") { Gate = ReadyGate.None }, ctx => { never = ctx.Defer(); token = ctx.Cancelled; return null; });
                s.Commands.Register(new CommandSpec("t.short") { Gate = ReadyGate.None, DefaultTimeoutSec = 0.25 }, ctx => { ctx.Defer(); return null; });
                var ran = new int[1];
                s.Commands.Register(new CommandSpec("t.count") { Gate = ReadyGate.None }, ctx => { ran[0]++; return null; });
                using (var pump = new PumpThread(s, 2))
                {
                    var sw = Stopwatch.StartNew();
                    var r = Http.Call(s.Port, "{\"cmd\":\"t.never\",\"timeout\":0.3,\"id\":1}");
                    long ms = sw.ElapsedMilliseconds;
                    t.Equal(BridgeCodes.Timeout, Kit.Code(r), "timeout 0.3 s answered timeout");
                    t.Check(ms >= 250 && ms < 2000, "timeout after about 0.3 s (" + ms + " ms)");
                    t.Equal("1", Kit.Str(r, "id"), "timeout reply echoes the id");
                    t.Equal("running", Kit.Str(r, "detail.state"), "timeout detail.state running");
                    // the reaper replies first, then retires the job and cancels its token: the reply can arrive before the cancel
                    Kit.WaitFor(() => token.IsCancellationRequested && never.IsAbandoned, 1000);
                    t.Check(token.IsCancellationRequested && never.IsAbandoned, "the handler's token is cancelled and the reply abandoned");
                    t.Check(!never.Complete(null), "completing after the timeout is ignored");
                    var after = Http.Call(s.Port, "{\"cmd\":\"t.count\"}");
                    t.Check(Kit.Ok(after) && ran[0] == 1, "the queue moved on after the timeout");
                    sw.Restart();
                    r = Http.Call(s.Port, "{\"cmd\":\"t.short\"}");
                    ms = sw.ElapsedMilliseconds;
                    t.Check(Kit.Code(r) == BridgeCodes.Timeout && ms < 2000, "a command's DefaultTimeoutSec (0.25 s) applies (" + ms + " ms)");
                }
                // a queued request expiring before it starts never runs
                var blockerTask = s.SubmitAsync("{\"cmd\":\"t.never\",\"timeout\":5}");
                s.ManualFrame();
                var queued = s.SubmitAsync("{\"cmd\":\"t.count\",\"timeout\":0.1}");
                Kit.WaitFor(() => queued.IsCompleted, 3000);
                t.Check(queued.IsCompleted && Kit.Code(JObject.Parse(queued.Result)) == BridgeCodes.Timeout && Kit.Str(JObject.Parse(queued.Result), "detail.state") == "queued", "queued request expired (state queued)");
                never.Complete(null);
                s.ManualFrame();
                s.ManualFrame();
                t.Equal(1, ran[0], "an expired queued request is never run");
                t.Check(blockerTask.IsCompleted, "blocker answered");
            }
        }
    }

    public static class OutputGroup
    {
        public static void Run(BridgeChecks t)
        {
            string root = Path.Combine(t.TempDir, "out_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var o = new OutputRoot(root);
            string full = o.Resolve("shots/a.png");
            t.Equal(Path.Combine(Path.GetFullPath(root), "shots", "a.png"), full, "relative path under the root");
            t.Check(Directory.Exists(Path.Combine(root, "shots")), "parent directory created");
            t.Equal("shots/a.png", o.Relative(full), "Relative");
            t.Equal(Path.Combine(Path.GetFullPath(root), "b", "c.txt"), o.Resolve("b\\c.txt"), "backslashes accepted");
            t.Equal(Path.Combine(Path.GetFullPath(root), "x.txt"), o.Resolve("./x.txt"), "./ accepted");
            File.WriteAllText(full, "x");
            var e = t.Throws<BridgeException>(() => o.Resolve("shots/a.png"), "existing file without overwrite");
            t.Check(e?.Code == BridgeCodes.IoDenied && e.Error.Detail?.Arg == "overwrite", "existing file -> io_denied (arg overwrite)");
            t.Equal(full, o.Resolve("shots/a.png", overwrite: true), "existing file with overwrite");
            t.Throws<BridgeException>(() => o.Resolve("shots"), "a directory is not a file");
            foreach (var bad in new[]
                     {
                         "C:\\Windows\\x.txt", "C:x.txt", "\\x.txt", "/x.txt", "\\\\server\\share\\x", "..\\x", "../x", "a/../../x", "a/..", "a/b/../../../x",
                         "x.txt:stream", "CON", "con.txt", "nul", "a/AUX.png", "lpt1", "a/b.", "a /b", "a?b", "a*b", "a<b", "a|b", "a\"b", "a\u0001b",
                     })
            {
                var ex = t.Throws<BridgeException>(() => o.Resolve(bad, overwrite: true), "denied: " + bad.Replace("\u0001", "\\u0001"));
                t.Check(ex?.Code == BridgeCodes.IoDenied, "io_denied: " + bad.Replace("\u0001", "\\u0001") + " (" + ex?.Code + ")");
            }
            t.Equal(BridgeCodes.BadArgs, t.Throws<BridgeException>(() => o.Resolve(""), "empty name")?.Code, "empty name = bad_args");
            t.Equal(BridgeCodes.IoDenied, t.Throws<BridgeException>(() => new OutputRoot(null).Resolve("a.png"), "no root")?.Code, "no root = io_denied");
            t.Check(o.Resolve("a.b.c.png").EndsWith("a.b.c.png") && o.Resolve("日本語.png").EndsWith("日本語.png") && o.Resolve("con-x.png").EndsWith("con-x.png"), "ordinary names pass");
            var sibling = new OutputRoot(root + "x");
            t.Throws<BridgeException>(() => new OutputRoot(root).Check("..\\" + Path.GetFileName(root) + "x\\a"), "a sibling with the root as prefix is outside");
            t.Check(sibling.Root != o.Root, "sibling root distinct");

            // log ring
            var ring = new LogRing(16);
            for (int i = 1; i <= 20; i++) ring.Add(i % 5 == 0 ? LogEntryType.Error : i % 3 == 0 ? LogEntryType.Warning : LogEntryType.Log, "m" + i, i == 20 ? "stack" : null);
            t.Equal(20L, ring.LastSeq, "LastSeq");
            var all = ring.Tail(100, LogEntryType.Log, 0, out bool gap);
            t.Equal(16, all.Count, "ring keeps its capacity");
            t.Check(gap, "gap: since 0 but entries 1-4 were dropped");
            t.Equal("m5", all[0].Message, "oldest kept first");
            var errs = ring.Tail(100, LogEntryType.Error, 0, out _);
            t.Equal("m5,m10,m15,m20", string.Join(",", errs.ConvertAll(x => x.Message)), "level error");
            var warn = ring.Tail(100, LogEntryType.Warning, 14, out gap);
            t.Equal("m15,m18,m20", string.Join(",", warn.ConvertAll(x => x.Message)), "level warning since 14");
            t.Check(!gap, "no gap when since is inside the buffer");
            var last2 = ring.Tail(2, LogEntryType.Log, 0, out _);
            t.Equal("m19,m20", string.Join(",", last2.ConvertAll(x => x.Message)), "n = newest n");
            t.Equal(0, ring.Tail(10, LogEntryType.Log, 20, out gap).Count, "nothing after the newest");
            t.Check(!gap, "no gap at the newest");
            ring.Add(LogEntryType.Log, new string('x', LogRing.MaxText + 10));
            t.Equal(LogRing.MaxText + 1, ring.Tail(1, LogEntryType.Log, 0, out _)[0].Message.Length, "long messages cut");

            using (var s = Kit.NewServer(o2 => o2.LogCapacity = 50, start: false))
            {
                for (int i = 0; i < 10; i++) s.Log.Add(i == 7 ? LogEntryType.Exception : LogEntryType.Log, "line " + i, "at X");
                var r = Kit.Exec(s, "{\"cmd\":\"log.tail\",\"n\":3}");
                t.Equal(3, (r?["entries"] as JArray)?.Count ?? -1, "log.tail n");
                t.Equal("line 9", Kit.Str(r, "entries[2].message"), "log.tail newest last");
                t.Equal("10", Kit.Str(r, "last"), "log.tail last");
                t.Check(r?["entries"]?[0]?["stack"] == null, "no stack by default");
                r = Kit.Exec(s, "{\"cmd\":\"log.tail\",\"level\":\"error\",\"stack\":true}");
                t.Check(Kit.Str(r, "entries[0].type") == "exception" && Kit.Str(r, "entries[0].stack") == "at X", "level error includes exceptions; stack on request");
                r = Kit.Exec(s, "{\"cmd\":\"log.tail\",\"since\":8}");
                t.Equal(2, (r?["entries"] as JArray)?.Count ?? -1, "since 8 -> entries 9 and 10");
                t.Equal(BridgeCodes.BadArgs, Kit.Code(Kit.Exec(s, "{\"cmd\":\"log.tail\",\"n\":0}")), "n 0 = bad_args");
                t.Equal(BridgeCodes.BadArgs, Kit.Code(Kit.Exec(s, "{\"cmd\":\"log.tail\",\"level\":\"loud\"}")), "unknown level = bad_args");
            }
        }
    }
}
