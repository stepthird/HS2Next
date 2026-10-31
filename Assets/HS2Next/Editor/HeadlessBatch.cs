// HeadlessBatch: the edit-mode headless test rows of the runner, one after the other in one Unity process.
// Run: -executeMethod HeadlessBatch.Run -plan <file> -out <dir> [-resume <file>] [-rows <id,id,...>] [-recycleMB <n>] [-diag]
//   plan: one member per line, tab-separated: id, method (Class.Method or Namespace.Class.Method, static, parameterless),
//     report (relative to the project, or "-"), time limit in minutes, options ("-" or ';'-separated: fresh = the member
//     builds its own catalog; env=NAME=VALUE = an environment variable for this member only), then the member's arguments.
//     Lines starting with '#' are comments.
//   out: each member's report is copied to <out>/<id>__<report file name> as soon as the member ends (the runner's naming),
//     so a later member that writes the same file cannot replace it.
//   resume (default <out>/batch_progress.tsv): appended while the batch runs, one line per event:
//       begin <id> <time>
//       end <id> <exit code> <seconds> <copied report or -> <hygiene: ok | FAIL: items> <notes>
//       timeout <id> <seconds>                (the member passed its own limit; the batch process ends)
//       recycle <members> <seconds> <MB>      (the managed heap grew past -recycleMB: the process ends, the runner starts
//                                             the next one)
//       done <members> <seconds> <the listed product states put back in this process>
//     A batch started with a progress file that already has lines skips the members it names (ended, timed out, or begun
//     and never ended: that member was running when the process died; the runner runs it alone).
//   rows: only these member ids of the plan (in plan order).
//   diag: each end line also counts the live Unity objects (the types that grew most), the log handlers and the undo
//     groups (TestHygiene.DiagText; costs a scan of every object per member).
//   recycleMB (default 2000, 0 = never): after a member, a process whose managed heap is above this ends the batch with a
//     recycle line, and the runner continues in a fresh process (-resume): what members leave referenced cannot slow the
//     members after them without a bound (the collector's cost grows with the heap). Each end line notes the managed heap
//     and Unity's native allocation after the member's hygiene and their change during the member.
// Each member runs exactly as its own row runs alone: its exit code is the code it passes to TestExit.Exit (0 when it
// returns without calling it, 1 when it throws), its arguments are the ones TestArgs.Get returns, its report is its own
// file. Between members TestHygiene puts the known process state back and checks for leaks (a leak FAILs the member and
// is cleaned up so the next member starts clean). A member that passes its own time limit ends the process (the runner
// resumes with the next member); the batch's own limit is the runner's process limit. The process exits 0 when the batch
// ran to its end (the members' verdicts are in the progress file) and 1 when the plan cannot be read.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using HS2Next.Content;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>Marks a test method's needs when it runs as a member of <see cref="HeadlessBatch"/>.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class BatchMemberAttribute : Attribute
{
    /// <summary>The member builds its own catalog instead of the batch's warm one.</summary>
    public bool FreshCatalog;
    /// <summary>The member never runs inside a batch (the batch records it as "alone"; the runner runs it as its own row).</summary>
    public bool Alone;
}

/// <summary>
/// The end of a test run. Outside a batch: ends Unity with the code (as EditorApplication.Exit). Inside a batch: records
/// the code as the member's exit code and ends the member (the batch goes on with the next member).
/// </summary>
public static class TestExit
{
    /// <summary>Unwinds a batch member after <see cref="Exit"/>; the batch catches it. Never catch it in a test.</summary>
    public sealed class MemberExit : Exception
    {
        public readonly int Code;
        public MemberExit(int code) : base("test member ended with exit code " + code) { Code = code; }
    }

    public static void Exit(int code)
    {
        if (!HeadlessBatch.InMember) { EditorApplication.Exit(code); return; }
        HeadlessBatch.NoteExit(code);
        throw new MemberExit(code);
    }
}

/// <summary>
/// The command line a test reads. Outside a batch: the process's command line. Inside a batch: the process's arguments up
/// to -executeMethod, then the member's method and its own arguments (what the member's row passes when it runs alone).
/// </summary>
public static class TestArgs
{
    static string[] current;
    public static string[] Get() => current != null ? (string[])current.Clone() : Environment.GetCommandLineArgs();

    internal static void Use(string method, IReadOnlyList<string> args)
    {
        if (method == null) { current = null; return; }
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, "-executeMethod");
        var l = new List<string>(i >= 0 ? a.Take(i) : a) { "-executeMethod", method };
        l.AddRange(args);
        current = l.ToArray();
    }
}

public static class HeadlessBatch
{
    public sealed class Member
    {
        public string Id, Method, Report;
        public double TimeoutMin;
        public bool Fresh;
        public readonly List<(string name, string value)> Env = new List<(string, string)>();
        public readonly List<string> Args = new List<string>();
    }

    /// <summary>True while a batch runs (between members too).</summary>
    public static bool Running { get; private set; }
    /// <summary>True while a member's method runs.</summary>
    public static bool InMember { get; private set; }
    /// <summary>The member that runs now, or null.</summary>
    public static Member Current { get; private set; }
    /// <summary>The running member builds its own catalog (plan option fresh or <see cref="BatchMemberAttribute.FreshCatalog"/>).</summary>
    public static bool CurrentFreshCatalog { get; private set; }

    static int? exitCode;
    internal static void NoteExit(int code) { if (exitCode == null) exitCode = code; }

    static readonly object fileLock = new object();
    static string progressFile;
    static void Append(string line)
    {
        lock (fileLock) File.AppendAllText(progressFile, line + "\n", new UTF8Encoding(false));
    }

    static string Arg(string name)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    public static List<Member> ReadPlan(string file)
    {
        var l = new List<Member>();
        foreach (var raw in File.ReadAllLines(file, Encoding.UTF8))
        {
            if (raw.Trim().Length == 0 || raw.TrimStart().StartsWith("#")) continue;
            var f = raw.Split('\t');
            if (f.Length < 5) throw new FormatException("plan line with fewer than 5 fields: " + raw);
            var m = new Member
            {
                Id = f[0].Trim(), Method = f[1].Trim(), Report = f[2].Trim() == "-" ? null : f[2].Trim(),
                TimeoutMin = double.Parse(f[3].Trim(), CultureInfo.InvariantCulture),
            };
            if (f[4].Trim() != "-")
                foreach (var o in f[4].Split(';').Select(x => x.Trim()).Where(x => x.Length > 0))
                {
                    if (o == "fresh") m.Fresh = true;
                    else if (o.StartsWith("env="))
                    {
                        var kv = o.Substring(4);
                        int eq = kv.IndexOf('=');
                        if (eq <= 0) throw new FormatException("plan option env=NAME=VALUE expected: " + o);
                        m.Env.Add((kv.Substring(0, eq), kv.Substring(eq + 1)));
                    }
                    else throw new FormatException("unknown plan option '" + o + "' in " + m.Id);
                }
            for (int i = 5; i < f.Length; i++) m.Args.Add(f[i]);
            if (l.Any(x => x.Id == m.Id)) throw new FormatException("member listed twice: " + m.Id);
            l.Add(m);
        }
        return l;
    }

    static MethodInfo Resolve(string name)
    {
        int dot = name.LastIndexOf('.');
        if (dot <= 0) return null;
        string typeName = name.Substring(0, dot), methodName = name.Substring(dot + 1);
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName)).FirstOrDefault(t => t != null);
        return type?.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
    }

    /// <summary>The ids the progress file already names (ended, timed out, or begun and never ended).</summary>
    public static HashSet<string> Seen(string file)
    {
        var s = new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(file)) return s;
        foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
        {
            var f = line.Split('\t');
            if (f.Length >= 2 && (f[0] == "begin" || f[0] == "end" || f[0] == "timeout")) s.Add(f[1]);
        }
        return s;
    }

    public static void Run()
    {
        string plan = Arg("-plan"), outDir = Arg("-out");
        if (plan == null || outDir == null) throw new ArgumentException("HeadlessBatch.Run needs -plan <file> -out <dir>");
        Directory.CreateDirectory(outDir);
        progressFile = Arg("-resume") ?? Path.Combine(outDir, "batch_progress.tsv");
        var members = ReadPlan(plan);
        var only = Arg("-rows");
        if (only != null)
        {
            var want = new HashSet<string>(only.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0), StringComparer.Ordinal);
            members = members.Where(m => want.Contains(m.Id)).ToList();
        }
        var seen = Seen(progressFile);
        bool diag = Array.IndexOf(Environment.GetCommandLineArgs(), "-diag") >= 0;
        long recycleMB = long.TryParse(Arg("-recycleMB") ?? "2000", NumberStyles.Integer, CultureInfo.InvariantCulture, out var rmb) ? rmb : 2000;
        var sw = Stopwatch.StartNew();
        Running = true;
        TestHygiene.Begin();
        Debug.Log($"[HeadlessBatch] {members.Count} members, {members.Count(m => seen.Contains(m.Id))} already in {progressFile}; hygiene baseline: {TestHygiene.BaselineText}");

        // the watchdog: a member past its own limit ends the process (the main thread cannot be interrupted)
        long memberStartTicks = 0; double memberLimitSec = 0; string memberId = null; bool finished = false;
        var watchdog = new Thread(() =>
        {
            while (!Volatile.Read(ref finished))
            {
                Thread.Sleep(2000);
                string id = Volatile.Read(ref memberId);
                long t0 = Interlocked.Read(ref memberStartTicks);
                if (id == null || t0 == 0) continue;
                double sec = (Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency;
                if (sec > Volatile.Read(ref memberLimitSec))
                {
                    try { Append($"timeout\t{id}\t{sec.ToString("F1", CultureInfo.InvariantCulture)}"); } catch { }
                    Process.GetCurrentProcess().Kill();
                }
            }
        }) { IsBackground = true, Name = "HeadlessBatch watchdog" };
        watchdog.Start();

        int ran = 0;
        bool recycled = false;
        try
        {
            foreach (var m in members)
            {
                if (seen.Contains(m.Id)) continue;
                var method = Resolve(m.Method);
                var attr = method?.GetCustomAttribute<BatchMemberAttribute>();
                if (method == null || attr?.Alone == true)
                {
                    Append($"end\t{m.Id}\t{(method == null ? 1 : 0)}\t0\t-\tok\t{(method == null ? "method not found: " + Clean(m.Method) : "alone (BatchMember.Alone): not run in the batch")}");
                    continue;
                }
                Append($"begin\t{m.Id}\t{DateTime.Now:yyyy-MM-ddTHH:mm:ss}");
                string reportPath = m.Report != null ? Path.Combine(ContentSettings.ProjectRoot, m.Report) : null;
                DateTime before = reportPath != null && File.Exists(reportPath) ? File.GetLastWriteTimeUtc(reportPath) : DateTime.MinValue;
                var envBefore = m.Env.Select(e => (e.name, Environment.GetEnvironmentVariable(e.name))).ToList();
                foreach (var (name, value) in m.Env) Environment.SetEnvironmentVariable(name, value);
                TestHygiene.BeforeMember();
                if (diag) TestHygiene.DiagMark();
                long heap0 = GC.GetTotalMemory(false) >> 20, native0 = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() >> 20;
                TestArgs.Use(m.Method, m.Args);
                Current = m;
                CurrentFreshCatalog = m.Fresh || attr?.FreshCatalog == true;
                exitCode = null;
                string fault = null;
                bool swallowed = false;
                int code;
                var msw = Stopwatch.StartNew();
                Volatile.Write(ref memberLimitSec, m.TimeoutMin * 60);
                Volatile.Write(ref memberId, m.Id);
                Interlocked.Exchange(ref memberStartTicks, Stopwatch.GetTimestamp());
                InMember = true;
                try
                {
                    method.Invoke(null, null);
                    code = exitCode ?? 0;
                    // an exit that did not end the member: a catch in the member took the unwind and the member went on
                    swallowed = exitCode != null;
                }
                catch (TargetInvocationException e) when (e.InnerException is TestExit.MemberExit me)
                {
                    code = exitCode ?? me.Code;
                }
                catch (TargetInvocationException e)
                {
                    code = exitCode ?? 1;
                    var x = e.InnerException ?? e;
                    fault = "threw " + x.GetType().Name + ": " + x.Message;
                    Debug.LogException(x);
                }
                finally
                {
                    InMember = false;
                    Interlocked.Exchange(ref memberStartTicks, 0);
                    Volatile.Write(ref memberId, null);
                    TestArgs.Use(null, null);
                    Current = null;
                    CurrentFreshCatalog = false;
                    foreach (var (name, value) in envBefore) Environment.SetEnvironmentVariable(name, value);
                }
                double seconds = msw.Elapsed.TotalSeconds;
                string copied = "-";
                if (reportPath != null && File.Exists(reportPath) && File.GetLastWriteTimeUtc(reportPath) > before)
                {
                    string leaf = SafeId(m.Id) + "__" + Path.GetFileName(reportPath);
                    File.Copy(reportPath, Path.Combine(outDir, leaf), true);
                    copied = leaf;
                }
                var h = TestHygiene.AfterMember();
                if (swallowed) h.Leaks.Insert(0, "the member went on after TestExit.Exit (a catch in the member took the unwind)");
                string hyg = h.Leaks.Count == 0 ? "ok" : "FAIL: " + string.Join("; ", h.Leaks);
                long heapMB = GC.GetTotalMemory(false) >> 20, nativeMB = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() >> 20;
                string notes = string.Join("; ", new[] { fault, h.Resets.Count > 0 ? "reset: " + string.Join(", ", h.Resets) : null,
                    $"hygiene {h.Seconds.ToString("F1", CultureInfo.InvariantCulture)} s", $"heap {heapMB} MB ({heapMB - heap0:+0;-0;0}), native {nativeMB} MB ({nativeMB - native0:+0;-0;0})",
                    $"gc {h.GcMs.ToString("F0", CultureInfo.InvariantCulture)} ms, unload {h.UnloadMs.ToString("F0", CultureInfo.InvariantCulture)} ms",
                    diag ? TestHygiene.DiagText() : null }.Where(x => x != null));
                Append($"end\t{m.Id}\t{code}\t{seconds.ToString("F1", CultureInfo.InvariantCulture)}\t{copied}\t{Clean(hyg)}\t{Clean(notes)}");
                Debug.Log($"[HeadlessBatch] {m.Id}: exit {code}, {seconds:F1} s, hygiene {hyg}{(fault != null ? ", " + fault : "")}");
                ran++;
                if (recycleMB > 0 && heapMB > recycleMB && members.Skip(members.IndexOf(m) + 1).Any(x => !seen.Contains(x.Id)))
                {
                    Append($"recycle\t{ran}\t{sw.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}\t{heapMB}");
                    Debug.Log($"[HeadlessBatch] recycle after {m.Id}: managed heap {heapMB} MB > {recycleMB} MB");
                    recycled = true;
                    break;
                }
            }
        }
        finally
        {
            Volatile.Write(ref finished, true);
            Running = false;
            TestCatalog.Drop();
        }
        Debug.Log($"[HeadlessBatch] listed product states put back: {TestHygiene.StatesReset}");
        if (recycled) return;
        Append($"done\t{ran}\t{sw.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}\tlisted product states put back {TestHygiene.StatesReset}");
        Debug.Log($"[HeadlessBatch] done: {ran} members in {sw.Elapsed.TotalMinutes:F1} min");
    }

    /// <summary>The runner's file-name form of a row id.</summary>
    public static string SafeId(string id) => new string(id.Select(c => char.IsLetterOrDigit(c) && c < 128 || c == '_' || c == '.' || c == '-' ? c : '_').ToArray());
}
