// Batch test of the Unity-free scene model (tests M1–M8). The checks live in
// Editor/SceneModel/ as test groups (static classes in namespace HS2Next.Scenes.Tests with
// `public static void Run(TestRunner t)`); the same files run under plain Mono through Tools/mono_tests.ps1.
// No game data: fixtures are the save-format goldens in Tests/SaveFormat/golden_src and synthetic values.
// Run: -executeMethod SceneModelTest.Run [-groups A,B]   (substring filter on group names; HS2N_SCENE_FUZZ=n for the fuzz)
// Report: Spike/out/scene/scene_model_report.txt; first line "N passed, M failed"; EditorApplication.Exit(1) on a failure.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HS2Next.Content;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class SceneModelTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "scene");

    static string Arg(string name, string def)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    public static void Run()
    {
        var t = new HS2Next.Save.Tests.TestRunner
        {
            TempDir = Path.Combine(Path.GetTempPath(), "hs2n_scenemodel_" + Process.GetCurrentProcess().Id),
            FixturesDir = Path.Combine(ContentSettings.ProjectRoot, "Tests"),
        };
        Directory.CreateDirectory(t.TempDir);
        var prevLog = HS2Next.Scenes.SceneEvents.Log;
        HS2Next.Scenes.SceneEvents.Log = m => Debug.Log("[scene events] " + m);
        var filter = Arg("-groups", null)?.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        var groups = typeof(SceneModelTest).Assembly.GetTypes()
            .Where(ty => ty.Namespace == "HS2Next.Scenes.Tests" && ty.IsAbstract && ty.IsSealed)
            .Select(ty => new { ty.Name, M = ty.GetMethod("Run", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(HS2Next.Save.Tests.TestRunner) }, null) })
            .Where(g => g.M != null)
            .OrderBy(g => g.Name, StringComparer.Ordinal)
            .ToList();
        var body = new StringBuilder();
        var total = Stopwatch.StartNew();
        foreach (var g in groups)
        {
            if (filter != null && !filter.Any(f => g.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
            t.Group = g.Name;
            int before = t.Failed, passed = t.Passed, lines = t.Lines.Count;
            var sw = Stopwatch.StartNew();
            try { g.M.Invoke(null, new object[] { t }); }
            catch (TargetInvocationException e) { t.Check(false, "group threw " + e.InnerException); }
            body.AppendLine($"{(t.Failed == before ? "PASS" : "FAIL")} {g.Name} ({t.Passed - passed} passed, {sw.ElapsedMilliseconds} ms{(t.Failed == before ? "" : ", " + (t.Failed - before) + " failed")})");
            for (int i = lines; i < t.Lines.Count; i++) body.AppendLine("   " + t.Lines[i]);
        }
        HS2Next.Scenes.SceneEvents.Log = prevLog;
        try { Directory.Delete(t.TempDir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }

        Directory.CreateDirectory(OutDir);
        var sb = new StringBuilder();
        sb.AppendLine($"{t.Passed} passed, {t.Failed} failed");
        sb.AppendLine("SceneModelTest (M1–M8)");
        sb.AppendLine($"runtime: Unity {Application.unityVersion}, {Environment.Version}, {total.ElapsedMilliseconds} ms");
        sb.Append(body);
        if (t.Failures.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("FAILURES:");
            foreach (var f in t.Failures) sb.AppendLine("  " + f);
        }
        string path = Path.Combine(OutDir, "scene_model_report.txt");
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        Debug.Log($"SceneModelTest: {t.Passed} passed, {t.Failed} failed -> {path}");
        if (t.Failed > 0) TestExit.Exit(1);
    }
}
