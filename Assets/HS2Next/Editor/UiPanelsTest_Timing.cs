// UiPanelsTest: the time checks of first shows, first groups, cached switches and the pose list's fill step, and what a garbage
// collection in the timed window does to them. One collection costs about 100 ms by itself (of 57 recorded runs, none went over
// 100 ms on a first show without one), so a window that holds one says nothing about the code it timed. No bar moves: a window
// without a collection is judged as before; a window with one gives a WARN carrying the number and the collection count.
//   TimedShow    one window: a first show, a first group, the longest fill step
//   TimedMedian  repeated windows of one operation: a cached switch
// Group XF (this file): fixtures that take the branches on purpose: a real GC.Collect inside a timed window, the bars around it, and
// the fixed order of the groups (UiPanelsTest.cs).
// Run alone: & Tools\unity_batch.ps1 -Method UiPanelsTest.Run -NoQuit -MethodArgs @('-only','XF')
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

public static partial class UiPanelsTest
{
    // The group joins UiPanelsTest.Groups after every domain reload (UiPanelsTest's static constructor runs first).
    [UnityEditor.InitializeOnLoadMethod]
    static void RegisterTimingGroup()
    {
        if (!Groups.Any(g => g.id == "XF")) Groups.Add(("XF", TimingFixtureGroup));
    }

    /// <summary>One timed window. <paramref name="gcs"/> = 0: FAIL above the bar (2 x <paramref name="targetMs"/>, or
    /// <paramref name="barMs"/> where a check has its own), WARN above the target. <paramref name="gcs"/> &gt; 0: the time is not
    /// judged, a WARN carries the number and the count. A window that recorded no time (a negative number) fails in both.
    /// <paramref name="detail"/> goes into the line's brackets.</summary>
    static void TimedShow(Ctx c, string name, double ms, int gcs, double targetMs, string detail = null, double barMs = 0)
    {
        double bar = barMs > 0 ? barMs : 2 * targetMs;
        string more = string.IsNullOrEmpty(detail) ? "" : detail + "; ";
        if (gcs > 0)
        {
            c.Check(ms >= 0, $"{name}: a time was recorded (not judged: {gcs} GC collection(s) in the window)");
            c.Warning($"{name} {ms:0.0} ms with {gcs} GC collection(s) in the window: the time is not judged ({more}bar {bar:0.###})");
            return;
        }
        c.Check(ms >= 0 && ms <= bar, $"{name} {ms:0.0} ms ({more}≤ {bar:0.###})");
        if (ms > targetMs && targetMs < bar) c.Warning($"{name} {ms:0.0} ms above the {targetMs:0.###} ms target");
    }

    /// <summary>Repeated windows of one operation. A window with a collection leaves the median and gives a WARN with its number and
    /// count; the median of the others is judged at the bars of <see cref="TimedShow"/>. Returns that median (NaN when no window is
    /// left: then nothing is judged, and a WARN says so).</summary>
    static double TimedMedian(Ctx c, string name, IReadOnlyList<(double ms, int gcs)> windows, double targetMs)
    {
        double bar = 2 * targetMs;
        foreach (var w in windows.Where(w => w.gcs > 0))
            c.Warning($"{name}: a window of {w.ms:0.000} ms with {w.gcs} GC collection(s) leaves the median");
        var clean = windows.Where(w => w.gcs == 0).Select(w => w.ms).ToList();
        if (clean.Count == 0)
        {
            c.Check(windows.Count > 0, $"{name}: {windows.Count} windows recorded (not judged: a GC collection in each)");
            c.Warning($"{name}: a GC collection in each of the {windows.Count} windows: the time is not judged (bar {bar:0.###})");
            return double.NaN;
        }
        double median = Median(clean);
        c.Check(median <= bar, $"{name} median {median:0.000} ms over {clean.Count} of {windows.Count} (max {clean.Max():0.000}; ≤ {bar:0.###})");
        if (median > targetMs) c.Warning($"{name} {median:0.00} ms above the {targetMs:0.###} ms target");
        return median;
    }

    static Ctx TimingFixture() => new Ctx { Quiet = true, Group = "fixture" };

    static IEnumerator TimingFixtureGroup(Ctx c)
    {
        // a real collection in a timed window: counted by what the call sites read, and the branch it takes
        int gc0 = GC.CollectionCount(0);
        var sw = Stopwatch.StartNew();
        GC.Collect();
        double ms = sw.Elapsed.TotalMilliseconds;
        int gcs = GC.CollectionCount(0) - gc0;
        c.Check(gcs >= 1, $"a forced GC.Collect is counted by GC.CollectionCount(0): {gcs} in a window of {ms:0.0} ms");
        var f = TimingFixture();
        TimedShow(f, "window", ms, gcs, 50);
        string line = f.Rep.ToString();
        c.Check(f.Fail == 0 && f.Pass == 1 && f.Warn == 1 && line.Contains($"{ms:0.0} ms") && line.Contains($"{gcs} GC collection(s)"),
            $"the window with the forced collection: no check on its time, a WARN with the number and the count ({f.Pass} / {f.Fail} / {f.Warn})");

        // the same numbers on both sides of a bar, with and without a collection
        f = TimingFixture();
        TimedShow(f, "window", 130, 1, 50);
        c.Check(f.Fail == 0 && f.Warn == 1 && f.Rep.ToString().Contains("130.0 ms") && f.Rep.ToString().Contains("1 GC collection(s)"), $"130 ms over a bar of 100 with a collection: not judged, WARN ({f.Pass} / {f.Fail} / {f.Warn})");
        f = TimingFixture();
        TimedShow(f, "window", 130, 0, 50);
        c.Check(f.Fail == 1 && f.Pass == 0 && f.Warn == 1, $"130 ms over a bar of 100 without a collection: FAIL, as before ({f.Pass} / {f.Fail} / {f.Warn})");
        f = TimingFixture();
        TimedShow(f, "window", 70, 0, 50);
        c.Check(f.Fail == 0 && f.Pass == 1 && f.Warn == 1, $"70 ms over a target of 50 without a collection: PASS and WARN, as before ({f.Pass} / {f.Fail} / {f.Warn})");
        f = TimingFixture();
        TimedShow(f, "window", 12, 0, 50);
        c.Check(f.Fail == 0 && f.Pass == 1 && f.Warn == 0, $"12 ms without a collection: PASS, nothing else ({f.Pass} / {f.Fail} / {f.Warn})");
        f = TimingFixture();
        TimedShow(f, "window", -1, 1, 50);
        c.Check(f.Fail == 1, $"no time recorded fails also with a collection in the window ({f.Pass} / {f.Fail} / {f.Warn})");
        f = TimingFixture();
        TimedShow(f, "window", 70, 0, 100, barMs: 100);
        TimedShow(f, "window", 130, 0, 100, barMs: 100);
        c.Check(f.Pass == 1 && f.Fail == 1 && f.Warn == 0, $"a check with its own bar and no target band: 70 passes, 130 fails, no WARN ({f.Pass} / {f.Fail} / {f.Warn})");

        // repeated windows
        f = TimingFixture();
        double med = TimedMedian(f, "switch", new List<(double, int)> { (4, 0), (140, 2), (5, 0), (3, 0) }, 5);
        c.Check(med == 4 && f.Fail == 0 && f.Warn == 1 && f.Rep.ToString().Contains("140.000 ms") && f.Rep.ToString().Contains("2 GC collection(s)"),
            $"a cached switch with one collection: that window leaves the median ({med}), a WARN with its number and count");
        f = TimingFixture();
        med = TimedMedian(f, "switch", new List<(double, int)> { (14, 0), (140, 1), (15, 0), (13, 0) }, 5);
        c.Check(med == 14 && f.Fail == 1, $"the other windows are still judged: a median of {med} over the bar of 10 fails");
        f = TimingFixture();
        med = TimedMedian(f, "switch", new List<(double, int)> { (140, 1), (150, 1) }, 5);
        c.Check(double.IsNaN(med) && f.Fail == 0 && f.Warn == 3, $"a collection in every window: nothing is judged ({f.Pass} / {f.Fail} / {f.Warn})");

        // the fixed order, whatever order the groups joined in; a group not in the list after it, by id
        Func<Ctx, IEnumerator> none = x => null;
        var joined = GroupOrder.Concat(new[] { "ZZ", "AA" }).Select(id => (id, none)).ToList();
        var first = InFixedOrder(joined).Select(g => g.id).ToList();
        var reversed = Enumerable.Reverse(joined).ToList();
        var rng = new Random(63);
        var shuffled = joined.OrderBy(x => rng.Next()).ToList();
        var wanted = GroupOrder.Concat(new[] { "AA", "ZZ" }).ToList();
        c.Check(first.SequenceEqual(wanted) && InFixedOrder(reversed).Select(g => g.id).SequenceEqual(wanted) && InFixedOrder(shuffled).Select(g => g.id).SequenceEqual(wanted),
            "the group order does not depend on the order the groups joined in, new groups after the list by id");
        var now = InFixedOrder(Groups).Select(g => g.id).ToList();
        c.Check(GroupOrder.All(now.Contains) && now.Take(GroupOrder.Length).SequenceEqual(GroupOrder), "every group of the fixed list exists and they run first, in the list's order");

        // ModShaderS1Explain: a 5.2 copy route's target is a tail target, an older route's is not
        var tail = ModShaderS1Explain.TailTargets();
        const string route52 = "HS2Next/Compatible/hooh/ClotheReplicaAlphaCullOff_(29-property_copy)", route37 = "HS2Next/Compatible/hooh/ClotheReplicaOpaque_(GlossMetalMap_copy)";
        string kind = ModShaderS1Explain.Classify("mat|x", "mat|x|shader=Universal_Render_Pipeline/Lit", "mat|x|shader=" + route52, tail);
        c.Check(tail.Contains(route52) && !tail.Contains(route37) && kind == ModShaderS1Explain.ClassTail, $"the S1 explain knows the 5.2 copy route's target and not the 3.7 one's ({kind}, {tail.Count} targets)");

        // which allocation counters this Mono has: a row's allocation can be compared only through one that moves
        c.Info("allocation counters over a new 8 MiB array: " + AllocationCounters());
        yield break;
    }

    /// <summary>What each candidate counter says over one allocation of 8 MiB (read by name: not every profile of the class library has them).</summary>
    static string AllocationCounters()
    {
        var current = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
        var total = typeof(GC).GetMethod("GetTotalAllocatedBytes", new[] { typeof(bool) });
        string Read(System.Reflection.MethodInfo m, object[] args, out long v)
        {
            v = 0;
            if (m == null) return "absent";
            try { v = Convert.ToInt64(m.Invoke(null, args)); return null; }
            catch (Exception e) { return (e.InnerException ?? e).GetType().Name; }
        }
        string e1 = Read(current, null, out long c0), e2 = Read(total, new object[] { true }, out long t0);
        long used0 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong(), heap0 = UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong();
        var keep = new byte[8 * 1024 * 1024];
        keep[keep.Length - 1] = 1;
        Read(current, null, out long c1);
        Read(total, new object[] { true }, out long t1);
        long used1 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong(), heap1 = UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong();
        GC.KeepAlive(keep);
        return "GC.GetAllocatedBytesForCurrentThread " + (e1 ?? (c1 - c0).ToString()) + ", GC.GetTotalAllocatedBytes(true) " + (e2 ?? (t1 - t0).ToString())
            + ", Profiler.GetMonoUsedSizeLong " + (used1 - used0) + ", Profiler.GetMonoHeapSizeLong " + (heap1 - heap0);
    }
}
