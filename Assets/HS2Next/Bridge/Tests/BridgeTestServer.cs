// A bridge with fake commands for client tests (Tools/hs2n_bridge.py; started by Tools/mono_tests/BridgeMain.cs
// --serve): ephemeral port, discovery files under the given user-data folder, a 16 ms pump thread, the stages
// catalog / studioDb ready after 0.5 s (so clients see not_ready first). Commands: test.echo (returns its arguments),
// test.slow (deferred, completes after `ms` milliseconds from a timer), test.fail (not_found with candidates).
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using HS2Next.Api.Bridge;

namespace HS2Next.Bridge.Tests
{
    public static class BridgeTestServer
    {
        public static int Serve(double seconds, string userDataDir, bool token)
        {
            var o = new BridgeOptions
            {
                Port = 0,
                UserDataRoot = userDataDir,
                RequireToken = token,
                AppVersion = "serve",
                UnityVersion = "none",
                Logger = (l, m) => Console.WriteLine(l + ": " + m),
            };
            using (var s = new BridgeServer(o))
            {
                Kit.RegisterEcho(s);
                s.Commands.Register(new CommandSpec("test.slow").Arg("ms", ArgTypes.Int, defaultValue: 200).WithGate(ReadyGate.None), ctx =>
                {
                    var d = ctx.Defer();
                    int ms = ctx.Args.Get("ms", 200);
                    var timer = new Timer(_ => d.Complete(new BridgeReply().With("slept", ms)), null, ms, Timeout.Infinite);
                    ctx.Cancelled.Register(() => timer.Dispose());
                    return null;
                });
                s.Commands.Register(new CommandSpec("test.fail") { Gate = ReadyGate.None }, ctx =>
                    throw new BridgeException(BridgeError.NotFound("no node x", "key", new[] { "a", "b" })));
                s.Commands.Register(new CommandSpec("test.gated"), ctx => new BridgeReply().With("ready", true));
                if (!s.Start()) { Console.WriteLine("not listening: " + s.LastError); return 2; }
                Console.WriteLine("serving " + s.Url + (s.Token != null ? " (token)" : ""));
                var ready = new Timer(_ => { s.Readiness.Set("catalog", true); s.Readiness.Set("studioDb", true); }, null, 500, Timeout.Infinite);
                using (new PumpThread(s, 16))
                {
                    var sw = Stopwatch.StartNew();
                    string stop = Path.Combine(userDataDir, "stop");
                    while (sw.Elapsed.TotalSeconds < seconds && !File.Exists(stop)) Thread.Sleep(50);
                }
                ready.Dispose();
                s.Stop();
                Console.WriteLine("stopped");
            }
            return 0;
        }
    }
}
