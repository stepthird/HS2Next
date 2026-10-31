// MapPpv2Check (headless, no device): the PPv2 volume reader of the map finding map.ppv2Ignored
// (Runtime/Scene/Runtime/Maps/MapPpv2Volumes.cs) against the map census (Cache/maps/maps_mods.jsonl, Tools/survey/map_census.py:
// every PPv2 volume of every mod map scene with its layer, global flag, active-in-hierarchy state and weight). For every
// map row of the catalog the census lists with volumes: the reader's volume count equals the census', and its "reaching"
// count (global, enabled, active, layer 8) equals the census' global + active + layer 8 count. Rows the reader cannot read
// (an LZMA bundle) are listed with the reason and do not fail; a row the census has and the catalog does not is listed.
//   & Tools\unity_batch.ps1 -Method MapPpv2Check.Run -NoGraphics   -> Spike/out/maps/map_ppv2_check.txt
// Map names and ids only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Content;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class MapPpv2Check
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "maps");

    public static void Run()
    {
        var rep = new StringBuilder();
        int pass = 0, fail = 0, unreadable = 0;
        var sw = Stopwatch.StartNew();
        void Check(string what, bool ok, string detail = "")
        {
            if (ok) pass++; else fail++;
            rep.AppendLine($"   {(ok ? "PASS" : "FAIL")} {what}{(string.IsNullOrEmpty(detail) ? "" : " - " + detail)}");
        }
        try
        {
            string census = Path.Combine(ContentSettings.ProjectRoot, "Cache", "maps", "maps_mods.jsonl");
            Check("the map census exists", File.Exists(census), census);
            var expect = new Dictionary<(string, int), (int volumes, int reaching, int weighted)>();
            if (File.Exists(census))
                foreach (var line in File.ReadLines(census))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var t = JObject.Parse(line);
                    var scenes = t["scenes"] as JObject;
                    foreach (var row in (t["rows"] as JArray) ?? new JArray())
                    {
                        var sp = (string)row["scenePath"];
                        var vols = sp != null ? scenes?[sp]?["ppv2"] as JArray : null;
                        if (vols == null || vols.Count == 0) continue;
                        int reach = 0, w = 0;
                        foreach (var v in vols)
                            if ((int?)v["layer"] == MapPpv2Volumes.StudioLayer && (bool?)v["global"] == true && (bool?)v["active"] == true)
                            {
                                reach++;
                                if (((double?)v["weight"] ?? 0) > 0) w++;
                            }
                        expect[(((string)row["guid"] ?? "").ToLowerInvariant(), (int)row["no"])] = (vols.Count, reach, w);
                    }
                }
            rep.AppendLine($"census rows with PPv2 volumes: {expect.Count}, reaching rows {expect.Values.Count(e => e.reaching > 0)}, " +
                           $"volumes {expect.Values.Sum(e => e.volumes)}, reaching {expect.Values.Sum(e => e.reaching)} (weight > 0: {expect.Values.Sum(e => e.weighted)})");
            var services = SceneRuntimeTest.NewServices(new StringBuilder());
            var cat = services.Catalog;
            var maps = services.Db.Maps.All;
            int found = 0, readRows = 0, reachRows = 0, reachSum = 0, weightedSum = 0;
            double ms = 0, msMax = 0;
            foreach (var kv in expect.OrderBy(k => k.Key.Item1, StringComparer.Ordinal).ThenBy(k => k.Key.Item2))
            {
                var row = maps.FirstOrDefault(m => string.Equals(m.Guid ?? "", kv.Key.Item1, StringComparison.OrdinalIgnoreCase) && m.No == kv.Key.Item2);
                if (row == null) { rep.AppendLine($"   - {kv.Key.Item1}#{kv.Key.Item2}: not in the catalog"); continue; }
                found++;
                MapPpv2Volumes.ForgetCache();
                var r = MapPpv2Volumes.Read(row, null, cat, services.Hs2Root);
                ms += r.Ms; msMax = Math.Max(msMax, r.Ms);
                if (!r.Ok)
                {
                    unreadable++;
                    rep.AppendLine($"   - {kv.Key.Item1}#{kv.Key.Item2}: not read ({r.Error}); census volumes {kv.Value.volumes}, reaching {kv.Value.reaching}");
                    continue;
                }
                readRows++;
                if (r.Reaching > 0) { reachRows++; reachSum += r.Reaching; weightedSum += r.ReachingWeighted; }
                Check($"{kv.Key.Item1}#{kv.Key.Item2}: volumes {r.Volumes} (census {kv.Value.volumes}), reaching {r.Reaching} (census {kv.Value.reaching}), " +
                      $"weight > 0 {r.ReachingWeighted} (census {kv.Value.weighted}), unreadable {r.Unreadable}, {r.Ms:F0} ms",
                    r.Volumes == kv.Value.volumes && r.Reaching == kv.Value.reaching && r.ReachingWeighted == kv.Value.weighted && r.Unreadable == 0);
            }
            rep.AppendLine($"catalog rows found {found} of {expect.Count}; read {readRows}, not read {unreadable}; reader: reaching rows {reachRows}, " +
                           $"volumes {reachSum} (weight > 0: {weightedSum}); read time total {ms:F0} ms, max {msMax:F0} ms");
            // a vanilla map without PPv2: nothing
            if (services.Db.TryGetMap(ContentId.Vanilla(10), out var v10))
            {
                var r10 = MapPpv2Volumes.Read(v10, null, cat, services.Hs2Root);
                Check("vanilla map 10: read, no volumes", r10.Ok && r10.Volumes == 0 && r10.Reaching == 0, r10.Error);
            }
        }
        catch (Exception e) { Check("no exception", false, e.GetType().Name + ": " + e.Message + " " + e.StackTrace); }
        Directory.CreateDirectory(OutDir);
        rep.Insert(0, $"SUMMARY: {pass} PASS, {fail} FAIL\nMapPpv2Check ({sw.Elapsed.TotalSeconds:F0} s; rows not read {unreadable})\n");
        File.WriteAllText(Path.Combine(OutDir, "map_ppv2_check.txt"), rep.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        Debug.Log($"MAP PPV2 CHECK: {pass} PASS, {fail} FAIL");
        if (Application.isBatchMode) EditorExit(fail > 0 ? 1 : 0);
    }

    static void EditorExit(int code) => UnityEditor.EditorApplication.Exit(code);
}
