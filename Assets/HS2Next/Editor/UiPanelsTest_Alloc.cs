// UiPanelsTest: what each group allocates, as information (Phase 6.3a, FI9): this Mono has no counter of
// allocated bytes, so a group's number is GC.CollectionCount(0) over the group and the sum of the rises of the used heap between Editor
// updates. A rise that a collection ends within one update is not counted, so the bytes are a lower bound. No check reads these numbers.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.Profiling;

public static partial class UiPanelsTest
{
    sealed class AllocWatch
    {
        long last, rises;
        int gc0;

        public void Begin()
        {
            rises = 0;
            gc0 = GC.CollectionCount(0);
            last = Profiler.GetMonoUsedSizeLong();
            EditorApplication.update += Sample;
        }

        void Sample()
        {
            long now = Profiler.GetMonoUsedSizeLong();
            if (now > last) rises += now - last;
            last = now;
        }

        public (int gcs, long bytes) End()
        {
            EditorApplication.update -= Sample;
            Sample();
            return (GC.CollectionCount(0) - gc0, rises);
        }
    }

    static readonly List<(string id, int gcs, long bytes)> AllocByGroup = new List<(string, int, long)>();

    /// <summary>One line per run: every group's collections and rises in MiB, then the sum.</summary>
    static void AllocReport(Ctx c)
    {
        if (AllocByGroup.Count == 0) return;
        const double MiB = 1024.0 * 1024.0;
        c.Info("allocation by group (collections of generation 0, the sum of the used heap's rises): "
            + string.Join("; ", AllocByGroup.Select(g => $"{g.id} {g.gcs} {g.bytes / MiB:0.0}"))
            + $"; all {AllocByGroup.Sum(g => g.gcs)} {AllocByGroup.Sum(g => g.bytes) / MiB:0.0} MiB");
    }
}
