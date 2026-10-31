// C38 (Phase 5.2 package O): the read-only commands content.stats (group content) and app.perf (group app), and the camera
// commands' settle skip. Edit-mode host: app.perf counts no frames here (play mode and the player sample them) and the catalog
// never evicts or unloads by itself (its upkeep ticks in play mode only).
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

public static partial class BridgeCoreTest
{
    static string Cut(string s) => s == null ? "" : s.Length > 300 ? s.Substring(0, 300) + "…" : s;

    static void C38(Ctx c)
    {
        var app = c.Host();
        // content.stats before and after a character
        var s0 = c.Ok(new { cmd = "content.stats" }, "content.stats answers");
        string[] members = { "open", "owned", "lru", "pinned", "thumbnailOnly", "unscoped", "pendingOpens", "ownedBytes", "lruBytes", "budgetBytes",
                             "evictions", "reloadedAssets", "unloadUnused", "owners", "unscopedLoads", "textureCheck", "quarantined" };
        var missing = members.Where(m => s0[m] == null).ToList();
        c.Check(missing.Count == 0, "content.stats holds every documented member", string.Join(", ", missing));
        c.Check(s0["textureCheck"]?["prescan"]?["total"] != null && s0["unloadUnused"]?["medianMs"] != null && s0["bundles"] == null,
            "content.stats: textureCheck.prescan and unloadUnused nested; no bundles list unless asked");
        c.Check((long?)s0["budgetBytes"] == app.Services.Catalog.BundleCacheMB * 1024L * 1024L, "content.stats budgetBytes = content.bundleCacheMB", "" + s0["budgetBytes"]);
        string key = W1LoadFemale(c, false);
        if (key != null)
        {
            var s1 = c.Ok(new { cmd = "content.stats", bundles = true, top = 5 }, "content.stats {bundles, top 5}");
            var list = s1["bundles"] as JArray;
            c.Check((int)s1["owned"] > 0 && (int)s1["owners"] > 0 && (long)s1["ownedBytes"] > 0, "a built character owns bundles (owned, owners, ownedBytes > 0)",
                $"owned {s1["owned"]}, owners {s1["owners"]}, ownedBytes {s1["ownedBytes"]}");
            c.Check(list != null && list.Count > 0 && list.Count <= 5 && list.All(b => b["key"] != null && b["owners"] is JArray && b["state"] != null && b["residentBytes"] != null),
                "content.stats bundles: at most top entries with key, owners, residentBytes, state", list?.Count.ToString() ?? "null");
            c.Check(list == null || list.All(b => !((string)b["key"]).Contains(":\\") && !((string)b["key"]).Contains(":/")), "content.stats bundle keys carry no folders of the zipmod files");
            c.Fails(new { cmd = "content.stats", bundles = true, top = 0 }, "bad_args", "content.stats top 0 is bad_args");
        }
        // app.perf
        var p0 = c.Ok(new { cmd = "app.perf" }, "app.perf answers");
        c.Check(p0["frames"] != null && p0["frameMs"]?["p95"] != null && (long?)p0["memory"]?["managedBytes"] > 0 && p0["memory"]?["graphicsDriverBytes"] != null
            && p0["unloadUnused"]?["calls"] != null && p0["motion"] == null && p0["objects"] == null,
            "app.perf: frames, frameMs, memory, unloadUnused; motion and objects only when asked");
        var p1 = c.Ok(new { cmd = "app.perf", motion = true, objects = true }, "app.perf {motion, objects}");
        c.Check(p1["motion"]?["stages"] is JObject && p1["motion"]?["dynamicBone"]?["particles"] != null && (int?)p1["objects"]?["meshes"] > 0,
            "app.perf: the motion section (stages, dynamicBone) and the object counts", Cut(p1.ToString(Newtonsoft.Json.Formatting.None)));
        c.Frames(3);
        var p2 = c.Ok(new { cmd = "app.perf", motion = true, reset = true }, "app.perf {motion, reset}");
        c.Check((bool?)p2["motion"]?["timed"] == true, "app.perf: the motion stages are timed once asked", "" + p2["motion"]?["timed"]);
        HS2Next.Motion.CharaMotion.TimingSink = null;   // back to the default (no timing) for the later checks
        // camera.get twice in a row: the second skips the settle and answers the same
        var g1 = c.Ok(new { cmd = "camera.get" }, "camera.get");
        var g2 = c.Ok(new { cmd = "camera.get" }, "camera.get again (settled)");
        c.Check(JToken.DeepEquals(g1["camera"], g2["camera"]), "camera.get answers the same without a change in between");
        var up = app.Runtime.GetPart<HS2Next.Scenes.Runtime.RuntimeUpkeep>();
        c.Check(up != null && up.Settled, "the runtime is settled after a camera.get");
        c.Ok(new { cmd = "camera.set", dist = 7.5 }, "camera.set dist 7.5");
        var g3 = c.Ok(new { cmd = "camera.get" }, "camera.get after camera.set");
        c.Check(Math.Abs(((double?)g3["camera"]?["dist"] ?? 0) - 7.5) < 1e-4,
            "camera.get shows the pose camera.set wrote", Cut(g3["camera"]?.ToString(Newtonsoft.Json.Formatting.None)));
    }
}
