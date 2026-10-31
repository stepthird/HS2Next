// Plain-Mono entry of the bridge tests (Tools/mono_tests.ps1 -Suite bridge): runs the
// groups of HS2Next.Bridge.Tests.BridgeTestGroups; the arguments filter groups by name (substring, case-insensitive).
// Exit code = number of failed checks (capped at 100).
// `--serve <seconds> <userDataDir> [--token]` instead runs a bridge with fake commands (test.echo, test.slow, test.fail)
// and a 16 ms pump thread until the time is up or <userDataDir>/stop exists, for client tests (Tools/hs2n_bridge.py).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HS2Next.Bridge.Tests;

public static class BridgeTestMain
{
    public static int Main(string[] args)
    {
        if (args.Length >= 3 && args[0] == "--serve")
            return BridgeTestServer.Serve(double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture), args[2], args.Contains("--token"));
        var t = new BridgeChecks { TempDir = Path.Combine(Path.GetTempPath(), "hs2n_bridge_tests_" + Process.GetCurrentProcess().Id) };
        Directory.CreateDirectory(t.TempDir);
        var groups = BridgeTestGroups.All();
        var total = Stopwatch.StartNew();
        int run = 0;
        foreach (var g in groups)
        {
            if (args.Length > 0 && !args.Any(a => g.Key.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
            run++;
            t.Group = g.Key;
            int before = t.Failed, passedBefore = t.Passed;
            var sw = Stopwatch.StartNew();
            try { g.Value(t); }
            catch (Exception e) { t.Check(false, "group threw " + e); }
            Console.WriteLine(g.Key + ": " + (t.Failed == before ? "ok" : (t.Failed - before) + " failed") + " (" + (t.Passed - passedBefore) + " passed, " + sw.ElapsedMilliseconds + " ms)");
        }
        Console.WriteLine("TOTAL passed " + t.Passed + ", failed " + t.Failed + " (" + run + " groups, " + total.ElapsedMilliseconds + " ms)");
        try { Directory.Delete(t.TempDir, true); } catch (Exception) { }
        return Math.Min(t.Failed, 100);
    }
}
