// C22 of BridgeCoreTest: the Phase 3.2 map commands (package A), further methods of
// the partial BridgeCoreTest (-only C22): registration (CoreCommands.Phase32Names), argument errors, map.set as one history
// entry with undo / redo restoring pose and flags, map.load re-selecting the scene's map (M3: no entry, pose kept), the
// edit-mode shapes of map.state / map.renderers (state Unsupported, empty lists) and the scene.lights additions. Maps load
// only in play mode: the loaded-map behaviour is MapTest.play and the acceptance case M1.
using System;
using System.Linq;
using HS2Next.App;
using Newtonsoft.Json.Linq;

public static partial class BridgeCoreTest
{
    static void C22(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        var names = HS2Next.App.Commands.CoreCommands.Phase32Names;
        c.Check(names.SequenceEqual(new[] { "map.state", "map.set", "map.renderers" }) && names.All(n => reg.Find(n) != null),
            "Phase32Names = map.state, map.set, map.renderers, all registered", string.Join(", ", names.Where(n => reg.Find(n) == null)));

        // ---------------- map.state / map.renderers without a map
        var st0 = c.Ok(new { cmd = "map.state" }, "map.state without a map");
        c.Check((int)st0["no"] == -1 && st0["ref"]?.Type == JTokenType.Null && (string)st0["state"] == "None" && (st0["roots"] as JArray)?.Count == 0,
            "map.state: no -1, ref null, state None, no roots", Ctx.Short(c.LastText));
        c.Check(st0["flags"]?["option"]?.Value<bool>() == true && st0["flags"]?["light"]?.Value<bool>() == true && st0["pose"]?["pos"] is JArray,
            "map.state: flags true / true and the pose of the new scene");
        c.Check(st0["renderSettings"]?["ambientMode"] != null && st0["lightmaps"]?["count"] != null && st0["probes"] != null && st0["counts"]?["renderers"]?.Value<int>() == 0
            && st0["mainLight"]?["reason"] != null && st0["blocks"]?["options"] is JArray && st0["features"]?["option"]?.Value<bool>() == false,
            "map.state: the members present (renderSettings, lightmaps, probes, counts, mainLight, blocks, features)");
        c.Check(st0["lights"] == null && st0["timing"] == null, "map.state: lights and timing only when asked");
        var st0b = c.Ok(new { cmd = "map.state", lights = true, timing = true, blocks = false }, "map.state {lights, timing, blocks: false}");
        c.Check(st0b["lights"] is JArray la && la.Count == 0 && st0b["timing"]?["steps"] != null && st0b["blocks"] == null, "… lights [] and timing present, blocks absent");
        var rr0 = c.Ok(new { cmd = "map.renderers" }, "map.renderers without a map");
        c.Check((int)rr0["count"] == 0 && (int)rr0["total"] == 0 && (rr0["renderers"] as JArray)?.Count == 0, "map.renderers: empty");
        c.Fails(new { cmd = "map.renderers", offset = -1 }, "bad_args", "map.renderers offset -1 → bad_args");
        c.Fails(new { cmd = "map.state", lights = "yes" }, "bad_args", "map.state lights \"yes\" → bad_args");

        // ---------------- map.set
        c.Fails(new { cmd = "map.set" }, "bad_args", "map.set without arguments → bad_args");
        c.Fails(new { cmd = "map.set", pos = new[] { 1, 2 } }, "bad_args", "map.set pos [1, 2] → bad_args");
        int n0 = W1UndoCount(c);
        c.Fails(new { cmd = "map.set", pos = new[] { 1, 2, 3 }, light = "off" }, "bad_args", "map.set with one bad argument → bad_args …");
        c.Check(W1UndoCount(c) == n0 && (double)c.Call(new { cmd = "map.state" })["pose"]["pos"][0] == 0, "… and nothing applied (all arguments checked first)");
        var set = c.Ok(new { cmd = "map.set", pos = new[] { 1, 2, 3 }, rot = new[] { 0, 90, 0 }, light = false }, "map.set {pos, rot, light: false}");
        c.Check(set["changed"] is JArray ch && ch.Select(x => (string)x).SequenceEqual(new[] { "pos", "rot", "light" }) && (set["noop"] as JArray)?.Count == 0,
            "map.set: changed [pos, rot, light], noop []", Ctx.Short(c.LastText));
        c.Check(W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Map pose", "map.set: one entry \"Map pose\"", W1UndoCount(c) + " " + W1TopLabel(c));
        var st1 = c.Call(new { cmd = "map.state" });
        c.Check(C22V(st1["pose"]["pos"], 1, 2, 3) && C22V(st1["pose"]["rot"], 0, 90, 0) && st1["flags"]["light"].Value<bool>() == false && st1["flags"]["option"].Value<bool>(),
            "map.state shows the new pose and flags", Ctx.Short(st1["pose"]?.ToString()));
        var noop = c.Ok(new { cmd = "map.set", pos = new[] { 1, 2, 3 }, option = true }, "map.set with the current values");
        c.Check((noop["noop"] as JArray)?.Count == 2 && (noop["changed"] as JArray)?.Count == 0 && W1UndoCount(c) == n0 + 1, "… both noop, no new entry", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "map.set", option = false }, "map.set {option: false}");
        c.Check(W1TopLabel(c) == "Map options", "map.set option alone: \"Map options\"", W1TopLabel(c));
        c.Ok(new { cmd = "map.set", light = true, label = "Lights on" }, "map.set {light, label}");
        c.Check(W1TopLabel(c) == "Lights on", "map.set label", W1TopLabel(c));
        c.Ok(new { cmd = "undo", n = 3 }, "undo 3");
        var st2 = c.Call(new { cmd = "map.state" });
        c.Check(C22V(st2["pose"]["pos"], 0, 0, 0) && C22V(st2["pose"]["rot"], 0, 0, 0) && st2["flags"]["light"].Value<bool>() && st2["flags"]["option"].Value<bool>(),
            "undo restores pose and flags", Ctx.Short(st2["pose"]?.ToString() + " " + st2["flags"]));
        c.Ok(new { cmd = "redo" }, "redo 1");
        var st3 = c.Call(new { cmd = "map.state" });
        c.Check(C22V(st3["pose"]["pos"], 1, 2, 3) && st3["flags"]["light"].Value<bool>() == false, "redo restores the first map.set", Ctx.Short(st3["pose"]?.ToString()));

        // ---------------- map.load: M3 re-select keeps the pose and records nothing
        c.Ok(new { cmd = "map.load", no = 10 }, "map.load {no:10} (edit mode: Unsupported)");
        int n1 = W1UndoCount(c);
        var st4 = c.Call(new { cmd = "map.state" });
        c.Check((string)st4["state"] == "Unsupported" && (int)st4["no"] == 10 && (string)st4["ref"] == "illusion.hs2#10" && (st4["roots"] as JArray)?.Count == 0,
            "map.state in edit mode: state Unsupported, the scene's ref, no roots", Ctx.Short(c.LastText));
        c.Check(C22V(st4["pose"]["pos"], 0, 0, 0), "a map change resets the pose (one composite entry \"Map\")");
        c.Ok(new { cmd = "map.set", pos = new[] { 0, 1, 0 } }, "map.set pos");
        int n2 = W1UndoCount(c);
        var re = c.Ok(new { cmd = "map.load", no = 10 }, "map.load of the scene's map again");
        c.Check(re["reselected"]?.Value<bool>() == true && W1UndoCount(c) == n2 && C22V(c.Call(new { cmd = "map.state" })["pose"]["pos"], 0, 1, 0),
            "M3: re-selecting records nothing and keeps the pose", Ctx.Short(c.LastText));
        c.Check(n1 >= 1 && re["features"] != null && re["mainLight"]?["reason"] != null, "map.load reports features and mainLight");
        var rr1 = c.Ok(new { cmd = "map.renderers", filter = "x", limit = 5, materials = false }, "map.renderers in edit mode with a map set");
        c.Check((int)rr1["count"] == 0, "… empty (nothing loaded in edit mode)");

        // ---------------- scene.lights additions
        var sl = c.Ok(new { cmd = "scene.lights" }, "scene.lights");
        c.Check(sl["studio"]?["mainLight"]?["reason"] != null, "scene.lights: studio.mainLight {name, reason}", Ctx.Short(sl["studio"]?.ToString()));
        var lights = sl["lights"] as JArray;
        if (lights != null && lights.Count > 0)
            c.Check(lights.All(l => l["renderingLayers"] != null && ((JObject)l).ContainsKey("original")), "scene.lights: every light has renderingLayers and original (null when not folded)");
        else c.Warn("scene.lights listed no light (no chara light in edit mode)");
    }

    static bool C22V(JToken t, double x, double y, double z) =>
        t is JArray a && a.Count == 3 && Math.Abs((double)a[0] - x) < 1e-6 && Math.Abs((double)a[1] - y) < 1e-6 && Math.Abs((double)a[2] - z) < 1e-6;
}
