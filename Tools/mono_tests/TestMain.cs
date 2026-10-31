// Plain-Mono entry of the headless model tests (Tools/mono_tests.ps1).
// Runs every static class in the namespace given by --ns that has `public static void Run(TestRunner t)`;
// the remaining arguments filter groups by name (substring, case-insensitive). --fixtures sets
// TestRunner.FixturesDir. Exit code = number of failed checks (capped at 100).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HS2Next.Save.Tests;

public static class MonoTestMain
{
    public static int Main(string[] args)
    {
        string ns = "HS2Next.Scenes.Tests", fixtures = null;
        var filters = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--ns" && i + 1 < args.Length) ns = args[++i];
            else if (args[i] == "--fixtures" && i + 1 < args.Length) fixtures = args[++i];
            else filters.Add(args[i]);
        }
        var t = new TestRunner
        {
            TempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hs2n_mono_tests_" + Process.GetCurrentProcess().Id),
            FixturesDir = fixtures,
        };
        System.IO.Directory.CreateDirectory(t.TempDir);
        var assemblies = new List<Assembly> { typeof(MonoTestMain).Assembly };
        var groups = assemblies.SelectMany(a => a.GetTypes())
            .Where(ty => ty.Namespace == ns && ty.IsAbstract && ty.IsSealed)
            .Select(ty => new { ty.Name, M = ty.GetMethod("Run", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(TestRunner) }, null) })
            .Where(g => g.M != null)
            .OrderBy(g => g.Name, StringComparer.Ordinal)
            .ToList();
        var total = Stopwatch.StartNew();
        int run = 0;
        foreach (var g in groups)
        {
            if (filters.Count > 0 && !filters.Any(a => g.Name.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
            run++;
            t.Group = g.Name;
            int before = t.Failed, passedBefore = t.Passed;
            var sw = Stopwatch.StartNew();
            try { g.M.Invoke(null, new object[] { t }); }
            catch (TargetInvocationException e) { t.Check(false, "group threw " + e.InnerException); }
            Console.WriteLine(g.Name + ": " + (t.Failed == before ? "ok" : (t.Failed - before) + " failed") +
                " (" + (t.Passed - passedBefore) + " passed, " + sw.ElapsedMilliseconds + " ms)");
        }
        Console.WriteLine("TOTAL passed " + t.Passed + ", failed " + t.Failed + " (" + run + " groups of " + ns + ", " + total.ElapsedMilliseconds + " ms)");
        try { System.IO.Directory.Delete(t.TempDir, true); } catch { }
        return Math.Min(t.Failed, 100);
    }
}
