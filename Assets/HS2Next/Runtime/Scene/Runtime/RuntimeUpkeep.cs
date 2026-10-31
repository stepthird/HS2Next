// The runtime's per-frame upkeep (a scene runtime part; SceneRuntime.AddStandardParts registers it):
//   - the settle stamp: every change batch of the session (bus operations, transform and camera writes) and every new or
//     loaded realization marks the runtime unsettled; a LateApply after an Apply that saw every change marks it settled. The
//     camera commands bring the runtime up to date only when it is unsettled (their replies are the same either way).
//   - once per frame (the first LateApply of the frame, play mode and the player only): the frame time for app.perf, and
//     ContentCatalog.Tick, which evicts unowned bundles over the byte budget after a release or every 5 s and starts
//     Resources.UnloadUnusedAssets at a quiet point when the policy allows. Quiet: no open transaction (a drag, a batch), the
//     command bus not busy, no capture in flight. Edit-mode hosts never tick: their tests evict and unload through the
//     catalog's own calls.
using HS2Next.Screenshots;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class RuntimeUpkeep : ISceneRuntimePart
    {
        SceneRuntime runtime;
        int lastFrame = -1;
        long changes, appliedAt = -1, settledAt = -1;

        /// <summary>True when no change reached the runtime since its last LateApply (an Apply + LateApply now would change nothing).</summary>
        public bool Settled => settledAt == changes;

        /// <summary>Change batches (and realization resets) the runtime received.</summary>
        public long Changes => changes;

        public void Attach(SceneRuntime r) => runtime = r;
        public void OnChanged(SceneChangeBatch b) => changes++;
        public void Apply() => appliedAt = changes;
        public void Clear() => changes++;
        public void OnLoadedRealized() => changes++;

        public void LateApply()
        {
            if (appliedAt == changes) settledAt = changes;
            if (!Application.isPlaying) return;
            int frame = Time.frameCount;
            if (frame == lastFrame) return;
            lastFrame = frame;
            FrameStats.Sample(Time.unscaledDeltaTime * 1000.0);
            var cat = runtime?.Services?.Catalog;
            if (cat == null) return;
            var bus = runtime.Session?.Commands;
            bool quiet = (bus == null || (bus.ActiveTransaction == null && !bus.Busy)) && CaptureService.InFlight == 0;
            cat.Tick(Time.realtimeSinceStartupAsDouble, quiet, true);
        }
    }

    /// <summary>Frame times of play mode and the player since the last reset (app.perf), one sample per frame.</summary>
    public static class FrameStats
    {
        const int Cap = 1 << 17;
        static readonly System.Collections.Generic.List<double> ms = new System.Collections.Generic.List<double>(4096);

        /// <summary>Frames sampled since the last reset.</summary>
        public static int Frames { get; private set; }

        public static void Sample(double frameMs)
        {
            if (ms.Count >= Cap) ms.RemoveRange(0, Cap / 2);   // a long run without a reset keeps the newer half
            ms.Add(frameMs);
            Frames++;
        }

        /// <summary>(median, p95, max) of the samples since the last reset; zeros without samples.</summary>
        public static (double Median, double P95, double Max) Summary() => Percentiles(ms);

        public static void Reset() { ms.Clear(); Frames = 0; }

        /// <summary>The median, the 95th percentile (nearest rank) and the maximum of <paramref name="values"/>.</summary>
        public static (double Median, double P95, double Max) Percentiles(System.Collections.Generic.IReadOnlyList<double> values)
        {
            if (values == null || values.Count == 0) return (0, 0, 0);
            var s = new double[values.Count];
            for (int i = 0; i < s.Length; i++) s[i] = values[i];
            System.Array.Sort(s);
            int n = s.Length;
            double med = n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2;
            int r = (int)System.Math.Ceiling(0.95 * n) - 1;
            return (med, s[System.Math.Max(0, System.Math.Min(n - 1, r))], s[n - 1]);
        }
    }
}
