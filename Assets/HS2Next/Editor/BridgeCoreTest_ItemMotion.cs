// C21 of BridgeCoreTest: package B's part of the Phase 3.1 item checks (the C20 group,
// package A's part is C20 in Editor/BridgeCoreTest_Items.cs): item.anime, item.motion, fk.list / fk.set / fk.enable /
// fk.capture on items and bones.dump on items — argument errors, one history entry per recorded call (none for restart,
// item.motion and reads), undo / redo restore scene.doc, the realized values follow (driver time, FK rotations in
// bones.dump). Test hosts step the items in AppCore.Frame after the characters (SceneRuntime.StepItems).
// Targets: vanilla 957 (animated gimmick), 1399 (FK, 3 bones), 1854 (a plain prop without motion), the mod FK item with
// dynamic bones kky.AIStudio.JamSession#7 (package C's meta.targets fkDynamicBones; skipped with a WARN when absent).
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.App;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class BridgeCoreTest
{
    static void C21(Ctx c)
    {
        c.Host();
        string Add(object req, string what)
        {
            var r = c.Ok(req, what);
            return (string)r["added"]?[0]?["key"];
        }
        var gim = Add(new { cmd = "item.add", no = 957 }, "item.add 957");
        var fk = Add(new { cmd = "item.add", no = 1399 }, "item.add 1399");
        var plain = Add(new { cmd = "item.add", no = 1854 }, "item.add 1854");
        if (gim == null || fk == null || plain == null) return;

        // ---------------- item.anime
        c.Fails(new { cmd = "item.anime" }, "bad_args", "item.anime without key: bad_args");
        c.Fails(new { cmd = "item.anime", key = gim, speed = 5 }, "bad_args", "item.anime speed 5 (> 3): bad_args");
        c.Fails(new { cmd = "item.anime", key = gim, pattern = -1 }, "bad_args", "item.anime pattern -1: bad_args");
        c.Fails(new { cmd = "item.anime", key = gim, pattern = 1.5 }, "bad_args", "item.anime pattern 1.5: bad_args");
        int n0 = W1UndoCount(c);
        var a0 = c.Ok(new { cmd = "item.anime", key = gim }, "item.anime (read)");
        c.Check(W1UndoCount(c) == n0 && a0["driven"]?.Value<bool>() == true && a0["patterns"] is JArray, "item.anime without changes: no history entry, 957 driven", Ctx.Short(c.LastText));
        var a1 = W1History1(c, new { cmd = "item.anime", key = gim, speed = 0.5, time = 0.25 }, "Item animation", "item.anime speed 0.5, time 0.25");
        c.Check(Math.Abs((double?)a1["speed"] ?? -1) == 0.5 && Math.Abs(((double?)a1["time"] ?? -1) - 0.25) <= 1e-5, "item.anime reply: speed 0.5, live time 0.25", Ctx.Short(a1.ToString(Newtonsoft.Json.Formatting.None)));
        c.Frames(10);
        var a2 = c.Ok(new { cmd = "item.anime", key = gim }, "item.anime after 10 frames");
        double expect = 0.25 + 10 * Dt * 0.5 / Math.Max(1e-6, ClipLength(c, gim));
        c.Check((double?)a2["time"] > 0.25, "10 frames at speed 0.5 advance the time (the test host steps the items)", $"time {(double?)a2["time"]}, ~{expect:F4} expected");
        int n1 = W1UndoCount(c);
        var a3 = c.Ok(new { cmd = "item.anime", key = gim, restart = true }, "item.anime restart");
        c.Check(W1UndoCount(c) == n1 && (double?)a3["time"] == 0.0, "restart: time 0, no history entry", Ctx.Short(c.LastText));
        var a4 = c.Ok(new { cmd = "item.anime", key = plain, speed = 0.75 }, "item.anime on an item without a driven Animator");
        c.Check(a4["driven"]?.Value<bool>() == false && Math.Abs(((double?)a4["speed"] ?? 0) - 0.75) <= 1e-6 && (a4["warnings"] as JArray)?.Count > 0,
            "no driven Animator: stored, driven false, a warning", Ctx.Short(c.LastText));

        // ---------------- item.motion
        c.Fails(new { cmd = "item.motion", key = gim, timeScale = -1 }, "bad_args", "item.motion timeScale -1: bad_args");
        int n2 = W1UndoCount(c);
        var m1 = c.Ok(new { cmd = "item.motion", key = gim, paused = true }, "item.motion paused");
        var t0 = (double?)c.Call(new { cmd = "item.anime", key = gim })["time"];
        c.Frames(5);
        var t1 = (double?)c.Call(new { cmd = "item.anime", key = gim })["time"];
        c.Check(m1["paused"]?.Value<bool>() == true && m1["motion"]?.Value<bool>() == true && t0 == t1, "paused: the item's time stands still", $"{t0} -> {t1}");
        var m2 = c.Ok(new { cmd = "item.motion", key = gim, paused = false, timeScale = 2 }, "item.motion timeScale 2");
        c.Frames(3);
        var t2 = (double?)c.Call(new { cmd = "item.anime", key = gim })["time"];
        c.Check(m2["paused"]?.Value<bool>() == false && Math.Abs(((double?)m2["timeScale"] ?? 0) - 2) <= 1e-6 && t2 > t1 && W1UndoCount(c) == n2,
            "unpaused, time scale 2: the time moves; item.motion records nothing", $"{t1} -> {t2}");
        var m3 = c.Ok(new { cmd = "item.motion", key = plain, paused = true }, "item.motion on an item without motion");
        c.Check(m3["motion"]?.Value<bool>() == false && (m3["warnings"] as JArray)?.Count > 0, "no motion: motion false and a warning", Ctx.Short(c.LastText));

        // ---------------- fk.* on an item
        var l0 = c.Ok(new { cmd = "fk.list", key = fk, exact = true }, "fk.list on 1399");
        var bones = (l0["bones"] as JArray) ?? new JArray();
        c.Check(bones.Count == 3 && bones.All(b => (string)b["group"] == "Item" && b["stored"]?.Value<bool>() == true) && l0["fkEnabled"]?.Value<bool>() == false,
            "fk.list: 3 item bones (group Item, stored by the new add), FK off", Ctx.Short(c.LastText));
        string b0 = bones.Count > 0 ? (string)bones[0]["bone"] : "?";
        c.Fails(new { cmd = "fk.enable", key = fk, groups = new[] { "Body" } }, "bad_args", "fk.enable with groups on an item: bad_args");
        var s1 = W1History1(c, new { cmd = "fk.set", key = fk, bones = new Dictionary<string, object> { [b0] = new[] { 10, 20, 30 }, ["no_such_bone"] = new[] { 0, 0, 0 } } }, "Item FK", "fk.set on an item");
        c.Check((int?)s1["set"] == 1 && (s1["missing"] as JArray)?.Select(x => (string)x).Contains("no_such_bone") == true, "fk.set: 1 set, the unknown name reported missing", Ctx.Short(s1.ToString(Newtonsoft.Json.Formatting.None)));
        W1History1(c, new { cmd = "fk.enable", key = fk, on = true }, "Item FK", "fk.enable on an item");
        c.Frames(2);
        var dump = c.Ok(new { cmd = "bones.dump", key = fk }, "bones.dump on an item");
        var entry = (dump["bones"] as JArray)?.FirstOrDefault(e => (string)e["name"] == b0);
        var q = Quaternion.Euler(10, 20, 30);
        bool qok = entry?["lq"] is JArray lq && lq.Count == 4 &&
                   (Enumerable.Range(0, 4).All(i => Math.Abs((double)lq[i] - q[i]) <= 1e-6) || Enumerable.Range(0, 4).All(i => Math.Abs((double)lq[i] + q[i]) <= 1e-6));
        c.Check(qok, "FK on: bones.dump shows the bone at Euler(10, 20, 30)", Ctx.Short(entry?.ToString()));
        c.Check((string)dump["bones"]?[0]?["path"] == "" && (int?)dump["count"] >= 4 && dump["root"] != null && dump.Property("anim") != null,
            "bones.dump on an item: root entry path \"\", root / count / anim members", Ctx.Short(dump.ToString()));
        c.Ok(new { cmd = "undo" }, "undo fk.enable");
        c.Frames(1);
        var dump2 = c.Ok(new { cmd = "bones.dump", key = fk }, "bones.dump after the undo");
        var e2 = (dump2["bones"] as JArray)?.FirstOrDefault(e => (string)e["name"] == b0);
        c.Check(e2 != null && !JToken.DeepEquals(e2["lq"], entry?["lq"]) && c.Call(new { cmd = "fk.list", key = fk })["fkEnabled"]?.Value<bool>() == false,
            "undo: FK off, the bone back at its base", Ctx.Short(e2?.ToString()));

        var jam = Add(new { cmd = "item.add", @ref = "kky.AIStudio.JamSession#7" }, "item.add kky.AIStudio.JamSession#7 (FK + dynamic bones)");
        if (jam == null) { c.Warn("the mod FK item with dynamic bones is not installed: fk.capture on it skipped"); return; }
        var lj = c.Ok(new { cmd = "fk.list", key = jam }, "fk.list on the mod item");
        c.Check(lj["dynamicBonesActive"]?.Value<bool>() == true, "FK off: its dynamic bones are active", Ctx.Short(c.LastText));
        W1History1(c, new { cmd = "fk.capture", key = jam }, "FK from pose", "fk.capture on an item");
        var lj2 = c.Call(new { cmd = "fk.list", key = jam });
        var aj = c.Call(new { cmd = "item.anime", key = jam });
        c.Check(lj2["fkEnabled"]?.Value<bool>() == true && lj2["dynamicBonesActive"]?.Value<bool>() == false && (double?)aj["speed"] == 0.0,
            "fk.capture: FK on, dynamic bones off while FK is on, anime speed 0", Ctx.Short(lj2.ToString()));
        var mr = c.Ok(new { cmd = "item.motion", key = jam, resetPhysics = true }, "item.motion resetPhysics");
        c.Check(mr["dynamicBones"] != null && mr["fk"]?["on"]?.Value<bool>() == true, "item.motion reply: fk and dynamicBones members", Ctx.Short(c.LastText));
    }

    /// <summary>The layer-0 clip length of an item (item.state realized.animator.length), 1 when unknown.</summary>
    static double ClipLength(Ctx c, string key)
    {
        var l = (double?)c.Call(new { cmd = "item.state", key })["realized"]?["animator"]?["length"];
        return l != null && l > 0 && !double.IsInfinity(l.Value) ? l.Value : 1.0;
    }
}
