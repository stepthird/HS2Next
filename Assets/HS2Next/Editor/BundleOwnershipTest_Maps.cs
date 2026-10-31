// BOM (play mode, headless): a map replaced by another map releases its owner (its scene bundle unloaded, the dependencies it
// opened waiting in the LRU) and counts as a destroyed realization, so the catalog's next quiet-point UnloadUnusedAssets runs
// (before 5.2 only a map clear called it: each replaced map's lightmaps and textures stayed until the next scene.new). Texture2D
// bytes after the replacement and that call come back to the level of the second map alone.
// Run: Tools/unity_batch.ps1 -Method BundleOwnershipTest.RunMaps -NoQuit -NoGraphics [-maps illusion.hs2#0,illusion.hs2#1].
// Report Spike/out/bundles/bundle_maps_report.txt (numbers only).
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static partial class BundleOwnershipTest
{
    static string MapsReport => Path.Combine(OutDir, "bundle_maps_report.txt");

    /// <summary>BOM in play mode (headless): -executeMethod BundleOwnershipTest.RunMaps (runner -NoQuit).</summary>
    public static void RunMaps()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(MapsReport, "SUMMARY: 0 PASS, 1 FAIL\nBOM: started, no result yet\n", new UTF8Encoding(false));
        PlayModeHarness.Start(nameof(BundleOwnershipTest) + "." + nameof(MapsPlay), MapsReport, 20);
    }

    static long TextureBytes() => Resources.FindObjectsOfTypeAll<Texture2D>().Where(t => t).Sum(t => UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t));

    static IEnumerator MapsPlay()
    {
        Reset();
        var swAll = Stopwatch.StartNew();
        var maps = (Arg("-maps") ?? "illusion.hs2#0,illusion.hs2#1").Split(',').Select(x => x.Trim()).ToArray();
        Line($"BOM (Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceType}, play mode): maps {string.Join(", ", maps)}");
        RuntimeServices sv = null;
        try { sv = SceneRuntimeTest.NewServices(new StringBuilder()); }
        catch (Exception e) { Check("setup", false, e.ToString()); }
        if (sv == null || maps.Length < 2) { FinishMaps(swAll); yield break; }
        var cat = sv.Catalog;
        cat.UnloadUnusedMinSec = 0;   // the test calls the upkeep itself; no 30-s wait
        var map = new MapRealization(cat, sv.Db, new SceneFindings(), sv.Hs2Root);
        bool[] ok = { false };
        map.SetFlags(true, true);

        // the second map alone: its texture bytes (after its load and an UnloadUnusedAssets)
        map.SetTarget(maps[1], Vec3.Zero, Vec3.Zero);
        yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return !map.IsBusy; }, 600, ok);
        Check($"map {maps[1]} loads", ok[0] && map.State == MapState.Loaded, map.State.ToString());
        yield return Resources.UnloadUnusedAssets();
        long bAlone = TextureBytes();
        map.Clear();
        yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return map.PendingUnloads == 0 && !map.IsBusy; }, 120, ok);
        int calls0 = cat.UnloadUnusedCalls;
        yield return null;
        cat.Tick(Time.realtimeSinceStartupAsDouble, true, true);   // completes the clear's own call (counted)
        yield return Resources.UnloadUnusedAssets();
        long empty = TextureBytes();

        // the first map, then replaced by the second
        map.SetTarget(maps[0], Vec3.Zero, Vec3.Zero);
        yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return !map.IsBusy; }, 600, ok);
        Check($"map {maps[0]} loads", ok[0] && map.State == MapState.Loaded, map.State.ToString());
        long aLoaded = TextureBytes();
        var statsA = cat.Ledger.Stats();
        map.SetTarget(maps[1], Vec3.Zero, Vec3.Zero);
        yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return !map.IsBusy && map.PendingUnloads == 0; }, 600, ok);
        Check($"map {maps[1]} replaces {maps[0]}", ok[0] && map.State == MapState.Loaded, map.State.ToString());
        long replacedBefore = TextureBytes();
        var statsR = cat.Ledger.Stats();
        int calls1 = cat.UnloadUnusedCalls;
        bool due = cat.UnloadUnusedDue(Time.realtimeSinceStartupAsDouble);
        // the upkeep's next quiet tick starts UnloadUnusedAssets; the next ticks see it end
        for (int i = 0; i < 300 && cat.UnloadUnusedCalls == calls1; i++)
        {
            cat.Tick(Time.realtimeSinceStartupAsDouble, true, true);
            yield return null;
        }
        long replacedAfter = TextureBytes();
        Info($"Texture2D MiB: empty {empty / 1048576.0:F1}, {maps[1]} alone {bAlone / 1048576.0:F1}, {maps[0]} loaded {aLoaded / 1048576.0:F1}, " +
             $"after the replacement {replacedBefore / 1048576.0:F1}, after the upkeep's UnloadUnusedAssets {replacedAfter / 1048576.0:F1}");
        Info($"ledger with {maps[0]}: open {statsA.Open}, owned {statsA.Owned}, lru {statsA.Lru}; after the replacement: open {statsR.Open}, owned {statsR.Owned}, lru {statsR.Lru}; " +
             $"UnloadUnusedAssets calls {calls0} -> {calls1} -> {cat.UnloadUnusedCalls} (last {cat.UnloadUnusedLastMs:F1} ms)");
        Check("the replaced map's release makes an UnloadUnusedAssets due and the upkeep's quiet tick runs it", due && cat.UnloadUnusedCalls == calls1 + 1,
              $"due {due}, calls {calls1} -> {cat.UnloadUnusedCalls}");
        Check("after it, Texture2D bytes are back to the second map alone (within 5 %, or 8 MiB)",
              replacedAfter <= Math.Max(bAlone * 1.05, bAlone + 8L * 1048576), $"{replacedAfter / 1048576.0:F1} vs {bAlone / 1048576.0:F1} MiB");
        map.Clear();
        yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return map.PendingUnloads == 0 && !map.IsBusy; }, 120, ok);

        // a zipmod map whose bundle is LZMA (-lzmaMap <ref>): its bundle opens off the main thread, frames go on meanwhile
        string lzma = Arg("-lzmaMap");
        if (!string.IsNullOrEmpty(lzma))
        {
            var sw = Stopwatch.StartNew();
            double maxFrameMs = 0, last = sw.Elapsed.TotalMilliseconds;
            int frames = 0;
            map.SetTarget(lzma, Vec3.Zero, Vec3.Zero);
            map.Apply();
            double firstApplyMs = sw.Elapsed.TotalMilliseconds;
            while (map.IsBusy && sw.Elapsed.TotalSeconds < 240)
            {
                yield return null;
                double now = sw.Elapsed.TotalMilliseconds;
                maxFrameMs = Math.Max(maxFrameMs, now - last);
                last = now;
                frames++;
                map.Apply();
            }
            Info($"map {lzma}: {map.State} after {sw.Elapsed.TotalSeconds:F1} s; the request's own frame {firstApplyMs:F0} ms; {frames} frames meanwhile, longest {maxFrameMs:F0} ms; bundle {map.BundleMs} ms");
            Check($"the LZMA map {lzma} loads with its bundle opened asynchronously (the request's frame < 2 s, frames go on while it opens)",
                  map.State == MapState.Loaded && firstApplyMs < 2000 && frames > 10, $"{map.State}, request frame {firstApplyMs:F0} ms, {frames} frames");
            map.Clear();
            yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return map.PendingUnloads == 0 && !map.IsBusy && map.PendingStaleOpens == 0; }, 120, ok);
        }
        try { cat.UnloadAll(); } catch (Exception) { }
        FinishMaps(swAll);
    }

    static void FinishMaps(Stopwatch sw)
    {
        rep.Insert(0, $"SUMMARY: {passes} PASS, {fails} FAIL ({sw.Elapsed.TotalSeconds:F0} s)\nBundleOwnershipTest BOM (a replaced map releases its owner and makes UnloadUnusedAssets due)\n");
        File.WriteAllText(MapsReport, rep.ToString(), new UTF8Encoding(false));
        Debug.Log($"BUNDLE OWNERSHIP MAPS: {passes} PASS, {fails} FAIL");
        PlayModeHarness.ExitCode = fails > 0 ? 1 : 0;
    }
}
