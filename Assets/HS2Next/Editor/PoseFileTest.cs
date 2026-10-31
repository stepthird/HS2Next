// Batch test of the HS2 pose-file reader/writer (runtime in
// Runtime/Import/Pose, namespace HS2Next.Import.Pose).
//
// PoseFileTest.Run [-groups A,B]       synthetic files only (no game data): the groups of Editor/PoseFileTest_*.cs
//   (static classes of namespace HS2Next.Import.Pose.Tests with Run(TestRunner), PoseCorpusGroup excluded; substring
//   filter on group names). The same groups run under plain Mono: Tools/mono_tests.ps1 -Suite pose.
//   Report: Spike/out/pose/posefile_report.txt, first line "N passed, M failed".
// PoseFileTest.RunCorpus [-poseRoot <dir>] [-nokeys] [-nonodes]   the installed pose library (default: hs2Root of
//   Config/settings.local.json + UserData/studio/pose): every payload parses and is rewritten byte for byte, the census
//   equals Tests/Pose/corpus_census.json, and the key coverage per family with the Studio list DB's
//   Bone lists (vanilla + zipmods; -nokeys skips the DB) and the FK node rows of the build hooks' providers (the node keys;
//   none in the official build; -nonodes skips them). Report: Spike/out/pose/pose_corpus_report.txt (+ the
//   measured census corpus_census_measured.json); aggregate numbers only, no file names.
// Verdict: EditorApplication.Exit(1) when a check fails.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HS2Next.Content;
using HS2Next.Import.Pose;
using HS2Next.Import.Pose.Tests;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class PoseFileTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "pose");

    static string Arg(string name, string def)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static bool Has(string name) => Array.IndexOf(TestArgs.Get(), name) >= 0;

    static HS2Next.Save.Tests.TestRunner NewRunner(string tag) => new HS2Next.Save.Tests.TestRunner
    {
        TempDir = Path.Combine(Path.GetTempPath(), "hs2n_pose_" + tag + "_" + Process.GetCurrentProcess().Id),
        FixturesDir = Path.Combine(ContentSettings.ProjectRoot, "Tests"),
    };

    public static void Run()
    {
        var t = NewRunner("synthetic");
        Directory.CreateDirectory(t.TempDir);
        var filter = Arg("-groups", null)?.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        var groups = typeof(PoseFileTest).Assembly.GetTypes()
            .Where(ty => ty.Namespace == "HS2Next.Import.Pose.Tests" && ty.IsAbstract && ty.IsSealed && ty != typeof(PoseCorpusGroup))
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
        try { Directory.Delete(t.TempDir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        Report(t, "posefile_report.txt", "PoseFileTest (synthetic files)", body, total.ElapsedMilliseconds);
    }

    public static void RunCorpus()
    {
        var t = NewRunner("corpus");
        Directory.CreateDirectory(t.TempDir);
        var body = new StringBuilder();
        var total = Stopwatch.StartNew();
        string rootArg = Arg("-poseRoot", null);
        string root = rootArg ?? Path.Combine(CardResolveTest.GameRoot, "UserData", "studio", "pose");
        body.AppendLine("pose root: " + (rootArg != null ? "-poseRoot" : "<hs2Root>/UserData/studio/pose"));
        PoseKeyMap map = null;
        t.Group = "BoneList";
        if (!Has("-nokeys"))
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var cat = CardResolveTest.BuildDb(null);
                var db = cat.LoadStudioLists();
                var boneRows = db.Bones.All.OrderBy(b => b.Order).Select(b => new PoseBoneInfo
                {
                    No = b.No, TransformName = b.TransformName, DisplayName = b.Name, Column = b.Group, Level = b.Level, SyncNo = b.SyncNo, Source = b.Guid,
                }).ToList();
                body.AppendLine($"Bone lists: {db.Bones.Count} rows from the Studio list DB ({sw.ElapsedMilliseconds} ms)");
                List<PoseBoneInfo> nodeRows = null;
                if (!Has("-nonodes")) nodeRows = NodeRows(t, cat, body);
                map = new PoseKeyMap(boneRows, nodeRows);
                t.Check(map.Count > 0, "Studio list DB Bone lists loaded");
            }
            catch (Exception e) { t.Check(false, "Studio list DB: " + e.GetType().Name + ": " + e.Message); }
        }
        else body.AppendLine("Bone lists: skipped (-nokeys)");
        t.Group = "PoseCorpus";
        int lines = t.Lines.Count;
        try { PoseCorpus.Run(t, root, map, ContentIds.Vanilla, OutDir); }
        catch (Exception e) { t.Check(false, "corpus test threw " + e); }
        for (int i = 0; i < t.Lines.Count; i++) body.AppendLine("   " + t.Lines[i]);
        try { Directory.Delete(t.TempDir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        Report(t, "pose_corpus_report.txt", "PoseFileTest.RunCorpus (installed pose library)", body, total.ElapsedMilliseconds);
    }

    /// <summary>
    /// The FK node rows of the build hooks' providers (HS2Next.Chara.Ext.CharaBuildHooks.FkNodes); null when
    /// no provider is registered (the official build: the node keys stay unresolved and are reported).
    /// </summary>
    static List<PoseBoneInfo> NodeRows(HS2Next.Save.Tests.TestRunner t, ContentCatalog cat, StringBuilder body)
    {
        var sw = Stopwatch.StartNew();
        var set = HS2Next.Chara.Ext.CharaBuildHooks.FkNodes(cat);
        if (set == null) { body.AppendLine("node tables: none (no FK node provider is registered: the official build)"); return null; }
        var rows = HS2Next.Scenes.Runtime.FkRows.ToPoseBones(set.Rows, "node table");
        body.AppendLine($"node tables: {set.Rows.Count} rows from the providers {string.Join(", ", HS2Next.Chara.Ext.CharaBuildHooks.FkNodeProviders)}, " +
                        $"{set.Findings.Count} findings ({sw.ElapsedMilliseconds} ms)");
        t.Check(rows.Count > 0, "the registered FK node providers give rows");
        return rows;
    }

    static void Report(HS2Next.Save.Tests.TestRunner t, string file, string title, StringBuilder body, long ms)
    {
        Directory.CreateDirectory(OutDir);
        var sb = new StringBuilder();
        sb.AppendLine($"{t.Passed} passed, {t.Failed} failed");
        sb.AppendLine(title);
        sb.AppendLine($"runtime: Unity {Application.unityVersion}, {Environment.Version}, {ms} ms");
        sb.Append(body);
        if (t.Failures.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("FAILURES:");
            foreach (var f in t.Failures) sb.AppendLine("  " + f);
        }
        string path = Path.Combine(OutDir, file);
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        Debug.Log($"PoseFileTest: {t.Passed} passed, {t.Failed} failed -> {path}");
        if (t.Failed > 0) TestExit.Exit(1);
    }
}
