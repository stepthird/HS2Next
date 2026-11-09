// WetTest's commands part (Phase 6.6 package C). BridgeCoreTest's C45 has no device, so what a bake adds is here.
//   Headless_Commands  WR5  with no graphics device: reads, wet.dry, .set, .remove, .floor and every edit that needs no bake work; a bake answers unsupported
//   Render_Commands    WR1r wet.attach, .stain, .use, .status with a bake; WR2r wet.set: what bakes and what does not, values waiting for the first stain; WR3r rays on the
//                      character and an item's far end; WE1r the panel's operations
//   Play_Commands      WR4r ground.probe and the floor on a real map
// Judged by numbers; no picture is written.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2Next.App;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Wet;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class WetTest
{
    const string CmdMap = "illusion.hs2#10";

    static void CmdAdd(Host h, string key, Action<CharaWet> make)
    {
        var b = St(h, key).Wet.Clone();
        make(b);
        h.Session.Commands.Execute(new WetEditCommand(key, b), new ExecuteOptions { Record = HistoryMode.None });
        h.Rt.ApplyNow();
    }

    static int CmdUndo(Host h) => h.Session.Commands.UndoList.Count;
    static string CmdTop(Host h) => h.Session.Commands.UndoList.Count > 0 ? h.Session.Commands.UndoList[0].Label : null;
    static JObject CmdRow(Host h, string key) => h.Ok(new { cmd = "wet.status", key }, "wet.status")["characters"]?[0] as JObject;
    static string CmdCode(Host h, object req) => (string)h.Call(req)["code"];

    // ------------------------------------------------------------ WR5

    static void Headless_Commands()
    {
        Group("WR5", "with no graphics device", () =>
        {
            using (var h = NewHost())
            {
                string key = LoadChara(h);
                h.Frames(2);
                var part = Part(h);
                Check("this process has no device", !part.CanBake, SystemInfo.graphicsDeviceType.ToString());
                Check("a bake answers unsupported: wet.attach", CmdCode(h, new { cmd = "wet.attach", key }) == "unsupported", Short(h.LastText));
                Check("wet.stain", CmdCode(h, new { cmd = "wet.stain", key, name = "x" }) == "unsupported", Short(h.LastText));
                var ops = h.App.Ops;
                Check("the panel's Add stain", ops.WetAdd(key, WetPreset.Water).Code == "unsupported");
                Check("and nothing stands in the block", St(h, key).Wet.IsEmpty);

                // a stain from a file: maps and all
                var res = "res/" + new string('a', 64) + ".png";
                CmdAdd(h, key, b =>
                {
                    var s = WetEdits.NewStain(b, "water", WetPreset.Water);
                    s.Maps.Add(new WetMapEntry { Renderer = "clothes:1/o_bot_a", Slot = 0, Res = res, Reach = 2.9f, Mesh = "o_bot_a|4|00000000" });
                    WetEdits.Add(b, s);
                    b.Puddles.Add(new WetPuddleEntry { Stain = s.Id, Center = new Vec3(0f, 1f, 0f), Area = 1f });
                });
                var row = CmdRow(h, key);
                Check("wet.status reads", (string)row?["current"] == "water" && (int)row["puddles"] == 1, row?.ToString());
                Check("no part shows a stain", (int)row["targets"] == 0 && (int)row["maps"]["live"] == 0 && (int)row["maps"]["dormant"] == 1, row.ToString());
                Check("wet.status keeps stains", (h.Ok(new { cmd = "wet.status" }, "wet.status")["stains"] as JArray)?.Count == 1);
                h.Ok(new { cmd = "wet.set", key, values = new Dictionary<string, object> { ["shape.up"] = 2, ["maxRadius"] = 4, ["look.amount"] = 0.5 } }, "wet.set works");
                var cur = St(h, key).Wet.CurrentStain;
                Check("a value that would bake is stored", cur.Shape.Up == 2f && cur.Shape.Radius == 4f && cur.Look.Amount == 0.5f);
                Check("and the maps stay as they are", cur.Maps.Count == 1 && cur.Maps[0].Res == res);
                var f = h.Ok(new { cmd = "wet.floor", key, offset = 0.25 }, "wet.floor works");
                Check("the floor is the feet", (string)f["source"] == "feet", Short(h.LastText));
                Check("the puddle moves", Mathf.Abs(St(h, key).Wet.Puddles[0].Center.Y - 1.25f) < 1e-4f, St(h, key).Wet.Puddles[0].Center + "");
                h.Ok(new { cmd = "scene.pause", paused = true }, "the pause");
                h.Ok(new { cmd = "wet.start", key }, "wet.start works");
                h.Ok(new { cmd = "scene.step", n = 4, dt = 0.25 }, "a step");
                var moved = CmdRow(h, key);
                Check("the clock runs with no device", Math.Abs((double)moved["t"] - 1.0) < 1e-4, moved.ToString());
                h.Ok(new { cmd = "scene.pause", paused = false }, "the pause ends");
                h.Ok(new { cmd = "wet.dry", key }, "wet.dry works");
                Check("dry", (double)CmdRow(h, key)["t"] == 0 && St(h, key).Wet.Puddles.Count == 0);
                h.Ok(new { cmd = "wet.keep", key }, "wet.keep works");
                Check("a kept layer shares the maps with no bake", St(h, key).Wet.CurrentStain.Maps.Count == 1);
                Check("applying again needs a bake", CmdCode(h, new { cmd = "wet.stain", key, name = "water 2" }) == "unsupported", Short(h.LastText));
                Check("the rig holds no drawing", Part(h).RigOf(key)?.Slots.Count == 0);
                var r = h.Ok(new { cmd = "wet.remove", key, all = true }, "wet.remove works");
                Check("the block is gone", (int)r["removed"] == 2 && St(h, key).Wet.IsEmpty, Short(h.LastText));
                h.Rt.ApplyNow();
                Check("and its rig", Part(h).Rigs == 0);
            }
        });
    }

    // ------------------------------------------------------------ with a bake

    static void Render_Commands()
    {
        Group("WR1r", "the commands of scripts with a bake", () =>
        {
            using (var h = NewHost())
            {
                string key = LoadChara(h);
                string second = LoadChara(h, true);
                h.Frames(2);
                h.Session.Selection.Set(new string[0]);
                int undo = CmdUndo(h);
                var a = h.Ok(new { cmd = "wet.attach", key }, "wet.attach, the default origin");
                Check("reply: status, stain, floor", (string)a["status"] == "ok" && (string)a["stain"] == "stain" && a["floor"] != null, Short(h.LastText));
                Check("one entry «Add stain»", CmdUndo(h) == undo + 1 && CmdTop(h) == "Add stain");
                var wet = St(h, key).Wet;
                Check("a dry stain with the water preset and maps", wet.Stains.Count == 1 && wet.CurrentStain.Preset == WetPreset.Water && wet.CurrentStain.T == 0f && !wet.CurrentStain.Running && wet.CurrentStain.Maps.Count > 0, wet.CurrentStain.Maps.Count + " maps");
                Check("no fixed floor is written", wet.Floor.Mode == WetFloorValues.ModeMap);
                var row = CmdRow(h, key);
                Check("wet.status: maps live, targets, current", (int)row["maps"]["live"] > 0 && (int)row["targets"] > 0 && (string)row["current"] == "stain", row.ToString());
                Check("floorSource and floor agree with the attach", (string)row["floorSource"] == "feet" && Math.Abs((double)row["floor"] - (double)a["floor"]) < 1e-5, row.ToString());
                h.Ok(new { cmd = "undo" }, "undo");
                Check("undo: the block is empty", St(h, key).Wet.IsEmpty);
                h.Ok(new { cmd = "redo" }, "redo");
                Check("redo: the stain and its maps", St(h, key).Wet.CurrentStain.Maps.Count == wet.CurrentStain.Maps.Count);

                h.Ok(new { cmd = "wet.start" }, "a command with no key takes the character attached");
                Check("it runs", St(h, key).Wet.CurrentStain.Running);
                h.Ok(new { cmd = "wet.attach", key = second, preset = "blood" }, "a second character");
                Check("the first keeps its stain (a second attach does not clear it)", St(h, key).Wet.Stains.Count == 1 && St(h, second).Wet.CurrentStain.Preset == WetPreset.Blood);
                h.Ok(new { cmd = "wet.dry" }, "and is now the one meant");
                Check("the first still runs", St(h, key).Wet.CurrentStain.Running);
                h.Ok(new { cmd = "wet.remove", key = second, all = true }, "the second is dry again");

                // a later attach clears what was there
                h.Ok(new { cmd = "wet.dry", key, undo = false }, "dry");
                CmdAdd(h, key, b => b.Puddles.Add(new WetPuddleEntry { Stain = b.Current, Center = new Vec3(0f, 0f, 0f), Area = 1f }));
                h.Ok(new { cmd = "wet.stain", key, name = "extra" }, "a named stain");
                undo = CmdUndo(h);
                h.Ok(new { cmd = "wet.attach", key, preset = "blood" }, "wet.attach again");
                wet = St(h, key).Wet;
                Check("the stains and puddles are cleared, one stain stands", wet.Stains.Count == 1 && wet.Puddles.Count == 0 && wet.CurrentStain.Preset == WetPreset.Blood, wet.Stains.Count + " stains");
                Check("one entry", CmdUndo(h) == undo + 1);
                h.Ok(new { cmd = "undo" }, "undo");
                Check("undo brings both stains and the puddle back", St(h, key).Wet.Stains.Count == 2 && St(h, key).Wet.Puddles.Count == 1);
                h.Ok(new { cmd = "redo" }, "redo");

                // wet.stain
                undo = CmdUndo(h);
                var s = h.Ok(new { cmd = "wet.stain", key, name = "first", preset = "blood", skinDrips = true }, "wet.stain");
                Check("reply: status", (string)s["status"] == "ok", Short(h.LastText));
                Check("one entry «Add stain»", CmdUndo(h) == undo + 1 && CmdTop(h) == "Add stain");
                wet = St(h, key).Wet;
                var first = wet.FindByName("first");
                Check("on top, dry, not current", wet.Stains.Last() == first && first.T == 0f && !first.Running && wet.CurrentStain.Name == "stain");
                Check("the script's defaults", first.Shape.Radius == 1.8f && first.Shape.Spread == 3f && first.Drips.Tail == 5f, first.Shape.Radius + ", " + first.Shape.Spread + ", " + first.Drips.Tail);
                Check("skinDrips", first.Drips.Skin);
                Check("the default origin, with maps", first.Origin.Mode == WetOriginValues.ModeDefault && first.Maps.Count > 0);
                h.Ok(new { cmd = "wet.stain", key, radius = 2, spread = 4, tail = 6 }, "wet.stain with no name");
                var unnamed = St(h, key).Wet.FindByName("stain");
                Check("the name defaults to stain, which it replaces", unnamed != null && St(h, key).Wet.Stains.Count == 2 && unnamed.Shape.Radius == 2f && unnamed.Shape.Spread == 4f && unnamed.Drips.Tail == 6f, St(h, key).Wet.Stains.Count + " stains");
                string id = St(h, key).Wet.FindByName("first").Id;
                CmdAdd(h, key, b => b.Puddles.Add(new WetPuddleEntry { Stain = id, Center = new Vec3(0f, 0f, 0f), Area = 1f }));
                h.Ok(new { cmd = "wet.start", key, name = "first" }, "start it");
                h.Ok(new { cmd = "wet.stain", key, name = "first", preset = "water" }, "a stain replaced");
                wet = St(h, key).Wet;
                first = wet.FindByName("first");
                Check("keeps its id and place, is dry, and has the new preset", first.Id == id && wet.Stains.Last() == first && !first.Running && first.Preset == WetPreset.Water);
                Check("its puddles go", wet.Puddles.Count == 0);

                // wet.stain with nothing but the name of a stain: applied again
                float t0 = 0.5f;
                CmdAdd(h, key, b => b.FindByName("first").T = t0);
                undo = CmdUndo(h);
                var maps = St(h, key).Wet.FindByName("first").Maps.Select(m => m.Res).ToList();
                h.Ok(new { cmd = "wet.stain", key, name = "first" }, "only a name");
                first = St(h, key).Wet.FindByName("first");
                Check("is no replacement: the clock stays", first.T == t0 && first.Preset == WetPreset.Water);
                Check("every part has its map: nothing to add", first.Maps.Select(m => m.Res).SequenceEqual(maps) && CmdUndo(h) == undo, first.Maps.Count + " maps");
                var cut = St(h, key).Wet.Clone();
                var lost = cut.FindByName("first").Maps[0];
                cut.FindByName("first").Maps.RemoveAt(0);
                h.Session.Commands.Execute(new WetEditCommand(key, cut), new ExecuteOptions { Record = HistoryMode.None });
                h.Rt.ApplyNow();
                h.Ok(new { cmd = "wet.stain", key, name = "first" }, "with a part without a map");
                first = St(h, key).Wet.FindByName("first");
                Check("the part gets one, in one entry «Stain»", first.Maps.Count == maps.Count && first.Maps.Any(m => m.Renderer == lost.Renderer && m.Slot == lost.Slot) && CmdUndo(h) == undo + 1 && CmdTop(h) == "Stain", first.Maps.Count + " maps, top " + CmdTop(h));

                // wet.use
                h.Ok(new { cmd = "scene.pause", paused = true }, "the pause");
                h.Ok(new { cmd = "wet.use", key, name = "first" }, "wet.use");
                Check("current and running from 0", St(h, key).Wet.CurrentStain.Name == "first" && St(h, key).Wet.CurrentStain.Running && St(h, key).Wet.CurrentStain.T == 0f);
                h.Ok(new { cmd = "scene.step", n = 15, dt = 0.1 }, "a step");
                row = CmdRow(h, key);
                Check("it spreads", (double)row["radius"] > 0 && Math.Abs((double)row["t"] - 1.5) < 1e-3, row.ToString());
                h.Ok(new { cmd = "scene.pause", paused = false }, "the pause ends");
            }
        });

        Group("WR2r", "wet.set: what bakes, values waiting for the first stain", () =>
        {
            using (var h = NewHost())
            {
                string key = LoadChara(h);
                h.Frames(2);
                var ops = h.App.Ops;
                h.Ok(new { cmd = "wet.set", key, values = new Dictionary<string, object> { ["maxRadius"] = 3, ["spreadTime"] = 2, ["puddleScale"] = 2, ["film.gloss"] = 0.5 } }, "values with no stain yet");
                Check("they wait", ops.WetWaiting(key) == 4);
                int undo = CmdUndo(h);
                h.Ok(new { cmd = "wet.attach", key }, "wet.attach");
                var wet = St(h, key).Wet;
                Check("they reach the first stain", wet.CurrentStain.Shape.Radius == 3f && wet.CurrentStain.Shape.Spread == 2f && wet.CurrentStain.Puddle.Scale == 2f, wet.CurrentStain.Shape.Radius + "");
                Check("and the block", wet.Film.Gloss == 0.5f);
                Check("in one entry, baked at that radius", CmdUndo(h) == undo + 1 && wet.CurrentStain.Maps.Count > 0 && Mathf.Abs(wet.CurrentStain.Maps[0].Reach - (float)WetMath.Reach(3f, wet.CurrentStain.Shape.Feather)) < 1e-4f, wet.CurrentStain.Maps[0].Reach + "");
                Check("nothing waits now", ops.WetWaiting(key) == 0);
                h.Ok(new { cmd = "wet.remove", key, all = true }, "the block goes");
                h.Ok(new { cmd = "wet.set", key, values = new Dictionary<string, object> { ["maxRadius"] = 3, ["dripTail"] = 7 } }, "values again");
                h.Ok(new { cmd = "wet.stain", key, name = "n", radius = 2 }, "wet.stain");
                var n = St(h, key).Wet.FindByName("n");
                Check("an argument given wins, a waiting value beats the default", n.Shape.Radius == 2f && n.Drips.Tail == 7f && n.Shape.Spread == 3f, n.Shape.Radius + ", " + n.Drips.Tail + ", " + n.Shape.Spread);

                string Maps() => string.Join(",", St(h, key).Wet.CurrentStain.Maps.Select(m => m.Res + m.Reach));
                string m0 = Maps();
                h.Ok(new { cmd = "wet.set", key, values = new Dictionary<string, object> { ["look.amount"] = 0.5 } }, "a look");
                Check("a look bakes nothing", Maps() == m0);
                h.Ok(new { cmd = "wet.set", key, values = new { maxRadius = 1.5 } }, "a smaller radius");
                Check("inside the baked reach nothing bakes", Maps() == m0);
                h.Ok(new { cmd = "wet.set", key, values = new { maxRadius = 6 } }, "a larger radius");
                Check("past the reach it bakes", Maps() != m0);
                string m1 = Maps();
                h.Ok(new { cmd = "wet.set", key, values = new Dictionary<string, object> { ["shape.up"] = 1 } }, "a shape");
                Check("a shape bakes", Maps() != m1);
                h.Ok(new { cmd = "undo" }, "undo");
                Check("undo brings the maps of before", Maps() == m1);
            }
        });

        Group("WR3r", "rays on the character", () =>
        {
            using (var h = NewHost())
            {
                string key = LoadChara(h);
                h.Frames(2);
                WetTargets.Frame(h.Rt.FindChara(key).Context, out var f);
                var mid = WetTargets.V(f.Mid);
                var fwd = WetTargets.V(f.Forward);
                double[] V(Vector3 v) => new double[] { v.x, v.y, v.z };
                Vector3 Point()
                {
                    var o = St(h, key).Wet.CurrentStain.Origin;
                    var b = AdvancedPoseResolver.Resolve(h.Rt.FindChara(key), o.Bone);
                    return b ? b.TransformPoint(new Vector3(o.Local.X, o.Local.Y, o.Local.Z)) : new Vector3(float.NaN, 0f, 0f);
                }
                h.Ok(new { cmd = "wet.attach", key, from = V(mid + fwd * 10f), target = V(mid) }, "points");
                var o1 = St(h, key).Wet.CurrentStain.Origin;
                Check("a bone origin on the surface", o1.Mode == WetOriginValues.ModeBone && o1.Bone.Length > 0, o1.Bone);
                var p1 = Point();
                Check("in front of the hips", Vector3.Dot(p1 - mid, fwd) > 0f && (p1 - mid).magnitude < 3f, p1 + " against " + mid);
                h.Ok(new { cmd = "wet.attach", key, target = new { key, bone = "cf_J_Hips" }, from = V(mid + fwd * 10f) }, "{key, bone}");
                var hips = h.Rt.FindChara(key).Context.Hips;
                Check("aimed at the bone", Vector3.Cross((Point() - (mid + fwd * 10f)).normalized, (hips.position - (mid + fwd * 10f)).normalized).magnitude < 1e-3f, Point() + "");
                h.Ok(new { cmd = "wet.attach", key, from = V(mid + fwd * 10f) }, "from alone");
                Check("goes to the hips", (Point() - p1).magnitude < 1e-3f, Point() + " against " + p1);
                h.Ok(new { cmd = "wet.attach", key, target = V(mid) }, "target alone");
                Check("comes from in front", (Point() - p1).magnitude < 1e-3f, Point() + " against " + p1);
                var before = St(h, key).Wet.ToJson();
                Check("a ray that ends half past the target, short of the body, misses", CmdCode(h, new { cmd = "wet.attach", key, from = V(mid + fwd * 30f), target = V(mid + fwd * 28f) }) == "not_found", Short(h.LastText));
                Check("and changes nothing", St(h, key).Wet.ToJson() == before);

                string item = Key(h.Ok(new { cmd = "item.add", no = 399 }, "an item"));
                h.Rt.ApplyNow();
                h.Ok(new { cmd = "obj.transform", key = item, pos = V(mid + fwd * 10f) }, "in front of the character");
                h.Rt.ApplyNow();
                h.Ok(new { cmd = "wet.attach", key, from = item, target = V(mid) }, "an item's key");
                Check("the same surface from its far end", (Point() - p1).magnitude < 1e-2f, Point() + " against " + p1);
                h.Ok(new { cmd = "wet.stain", key, name = "aimed", from = V(mid + fwd * 10f), target = V(mid) }, "wet.stain with a ray");
                Check("wet.stain takes it too", St(h, key).Wet.FindByName("aimed").Origin.Mode == WetOriginValues.ModeBone);
                Check("wet.stain: a miss", CmdCode(h, new { cmd = "wet.stain", key, name = "lost", from = V(mid + fwd * 30f), target = V(mid + fwd * 28f) }) == "not_found", Short(h.LastText));
                Check("no stain was added", St(h, key).Wet.FindByName("lost") == null);
            }
        });

        Group("WE1r", "the panel's operations", () =>
        {
            using (var h = NewHost())
            {
                string key = LoadChara(h);
                h.Frames(2);
                var ops = h.App.Ops;
                int undo = CmdUndo(h);
                var add = ops.WetAdd(key, WetPreset.Blood);
                Check("add: one entry «Add stain»", add.Ok && CmdUndo(h) == undo + 1 && CmdTop(h) == "Add stain", add.ToString());
                var s = St(h, key).Wet.CurrentStain;
                Check("named by its preset, current, with the block's defaults, baked", s.Id == (string)add.Value && s.Name == "blood" && s.Shape.Radius == 2.2f && s.Shape.Spread == 6.04f && s.Maps.Count > 0);
                var add2 = ops.WetAdd(key, WetPreset.Water, "second");
                Check("a name given, made current", add2.Ok && St(h, key).Wet.CurrentStain.Name == "second");
                var add3 = ops.WetAdd(key, WetPreset.Blood);
                Check("a taken name gets a number", St(h, key).Wet.CurrentStain.Name == "blood 2");
                Check("an unknown preset", ops.WetAdd(key, "wine").Code == "bad_args");

                string sid = (string)add3.Value;
                var mv = ops.WetMove(key, sid, 0);
                Check("move: one entry «Stain»", mv.Ok && CmdTop(h) == "Stain" && St(h, key).Wet.Stains[0].Id == sid, mv.ToString());
                Check("its own place is no change", ops.WetMove(key, sid, 0).Status == CommandStatus.NoOp);
                Check("a place outside", ops.WetMove(key, sid, 9).Code == "bad_args");
                Check("a stain that is not there", ops.WetMove(key, "s99", 0).Code == "not_found");

                var full = ops.WetFull(key, sid);
                var run = St(h, key).Wet.Find(sid);
                Check("full: at the end of the spread, stopped", full.Ok && run.T == run.Shape.Spread && !run.Running && CmdTop(h) == "Stain");
                ops.WetUse(key, sid, false);
                Check("a stain made current", St(h, key).Wet.Current == sid && CmdTop(h) == "Stain");
                var start = ops.WetStart(key, null, true);
                Check("start with a loop: «Start stain»", start.Ok && St(h, key).Wet.CurrentStain.Loop && CmdTop(h) == "Start stain");
                Check("dry without puddles: «Dry stain»", ops.WetDry(key, sid, false).Ok && CmdTop(h) == "Dry stain");
                CmdAdd(h, key, b => b.Puddles.Add(new WetPuddleEntry { Stain = sid, Area = 1f }));
                Check("clear puddles: «Clear puddles»", ops.WetClearPuddles(key).Ok && CmdTop(h) == "Clear puddles" && St(h, key).Wet.Puddles.Count == 0);

                // origin
                string maps = string.Join(",", St(h, key).Wet.Find(sid).Maps.Select(m => m.Res));
                var o = ops.WetOrigin(key, sid, "cf_J_Spine03", new Vec3(0f, 0.1f, 0.2f));
                run = St(h, key).Wet.Find(sid);
                Check("origin from a pick: «Move origin», baked", o.Ok && CmdTop(h) == "Move origin" && run.Origin.Mode == WetOriginValues.ModeBone && run.Origin.Bone == "cf_J_Spine03" && string.Join(",", run.Maps.Select(m => m.Res)) != maps, o.ToString());
                var d = ops.WetOriginDefault(key, sid);
                run = St(h, key).Wet.Find(sid);
                Check("back to the default", d.Ok && CmdTop(h) == "Move origin" && run.Origin.Mode == WetOriginValues.ModeDefault && run.Origin.Bone == "" && run.Origin.Local.Equals(Vec3.Zero), d.ToString());
                Check("an address that does not read", ops.WetOrigin(key, sid, "a/b", Vec3.Zero).Code == "bad_args");

                // a value dragged
                undo = CmdUndo(h);
                string before = St(h, key).Wet.ToJson();
                foreach (float v in new[] { 0.5f, 0.6f, 0.7f }) Check("a look step: " + v, ops.WetDrag(key, sid, "look.amount", v, "wet:drag").Ok);
                Check("one entry", CmdUndo(h) == undo + 1 && St(h, key).Wet.Find(sid).Look.Amount == 0.7f);
                ops.WetSet(key, sid, "look.amount", 0.7f, new ExecuteOptions { MergeKey = "wet:drag" });
                h.Ok(new { cmd = "undo" }, "undo");
                Check("one undo takes the drag", St(h, key).Wet.ToJson() == before);
                undo = CmdUndo(h);
                var shape = ops.WetDrag(key, sid, "shape.up", 1.5f, "wet:shape");
                Check("a shape step changes no document", shape.Ok && St(h, key).Wet.ToJson() == before && CmdUndo(h) == undo, shape.ToString());
                var rig = RigOf(h, key);
                var runRow = rig.Runs.First(x => x.Doc.Id == sid);
                Check("and shows at once, with no picture", runRow.Preview.Count > 0, runRow.Preview.Count + " preview maps");
                ops.WetDragCancel(key);
                Check("a drag given up shows the document again", RigOf(h, key).Runs.First(x => x.Doc.Id == sid).Preview.Count == 0);
                var commit = ops.WetSet(key, sid, "shape.up", 1.5f, new ExecuteOptions { MergeKey = "wet:shape" });
                Check("the commit is one entry and bakes", commit.Ok && CmdUndo(h) == undo + 1 && St(h, key).Wet.Find(sid).Shape.Up == 1.5f && string.Join(",", St(h, key).Wet.Find(sid).Maps.Select(m => m.Res)) != string.Join(",", CharaWet.Parse(before).Find(sid).Maps.Select(m => m.Res)));

                // the floor, kept and removed
                var fl = ops.WetFloorSet(key, WetFloorValues.ModeFeet, 0.5f);
                Check("floor: «Floor» and its height", fl.Ok && CmdTop(h) == "Floor" && fl.Value is WetFloorInfo fi && fi.Source == "feet", fl.ToString());
                var keep = ops.WetKeep(key);
                Check("keep: «Keep layer», the new name", keep.Ok && CmdTop(h) == "Keep layer" && (string)keep.Value == St(h, key).Wet.CurrentStain.Name);
                var again = ops.WetApplyAgain(key, null);
                Check("apply again on what has its maps: nothing to do", again.Ok && again.Status == CommandStatus.NoOp, again.ToString());
                Check("a kept stain is not applied again", ops.WetApplyAgain(key, sid).Code == "bad_args");
                Check("remove one", ops.WetRemove(key, sid).Ok && CmdTop(h) == "Remove stain" && St(h, key).Wet.Find(sid) == null);
                int left = St(h, key).Wet.Stains.Count;
                var all = ops.WetRemoveAll(key);
                Check("remove all", all.Ok && (int)all.Value == left && St(h, key).Wet.IsEmpty, all.ToString());
                h.Rt.ApplyNow();
                Check("no rig is left", Part(h).Rigs == 0);
                h.Ok(new { cmd = "undo" }, "undo");
                Check("undo brings the block back", St(h, key).Wet.Stains.Count == left);
                h.Rt.ApplyNow();
                Check("and a rig", Part(h).Rigs == 1);
            }
        });
    }

    // ------------------------------------------------------------ on a real map

    static IEnumerator Play_Commands()
    {
        current = "Commands";
        if (!Want("WR4r")) yield break;
        var ready = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ready);
        var app = AppCore.Current;
        if (!Check("the app runs with its bridge and runtime", ready[0] && app != null && app.Stage == AppStage.Ready && app.Bridge != null && app.Runtime != null, app?.Stage.ToString())) yield break;
        if (!POpen("WR4r", "ground.probe and the floor on a real map")) yield break;
        yield return PCall(app, new { cmd = "scene.new" });
        yield return PCall(app, new { cmd = "map.load", @ref = CmdMap, wait = true }, 240);
        if (!Check("the map loads", POk, Short(pLastText))) yield break;
        yield return PCall(app, new { cmd = "chara.load", path = femaleRel, sex = "female" }, 240);
        if (!Check("chara.load", POk, Short(pLastText))) yield break;
        string key = Key(pLast);
        yield return PFrames(3);
        yield return PCall(app, new { cmd = "chara.motion", key, paused = true });

        var pos = app.Runtime.FindChara(key).Holder.position;
        var q = MapSurface.Of(app.Runtime);
        var pts = new[] { new Vector3(pos.x, 5f, pos.z), new Vector3(pos.x + 2f, 5f, pos.z + 1f), new Vector3(90000f, 0f, 90000f) };
        var want = new HS2Next.Api.Unity.MapSurfaceHit[pts.Length];
        int answers = q.Surfaces(pts, want, 6f, 30f);
        yield return PCall(app, new { cmd = "ground.probe", points = new object[] { new[] { pts[0].x, pts[0].z }, new[] { pts[1].x, pts[1].y, pts[1].z }, new[] { pts[2].x, pts[2].z } }, y = 5 });
        Check("ground.probe", POk, Short(pLastText));
        var hits = pLast["hits"] as JArray;
        Check("two answers and a miss", answers == 2 && hits?.Count == 3 && hits[2]["y"].Type == JTokenType.Null && hits[2]["normal"].Type == JTokenType.Null, Short(pLastText));
        Check("the query's heights", Mathf.Abs((float)hits[0]["y"] - want[0].Height) < 1e-5f && Mathf.Abs((float)hits[1]["y"] - want[1].Height) < 1e-5f, Short(pLastText));
        var n = hits[0]["normal"] as JArray;
        Check("the normal is a unit vector pointing up", n?.Count == 3 && (float)n[1] > 0.3f && Mathf.Abs(new Vector3((float)n[0], (float)n[1], (float)n[2]).magnitude - 1f) < 1e-3f, Short(pLastText));
        Check("source: the device", (string)pLast["source"] == "gpu", Short(pLastText));
        Check("no triangles", pLast.Property("triangles") == null);

        yield return PCall(app, new { cmd = "wet.attach", key });
        Check("wet.attach on a map", POk, Short(pLastText));
        double floor = (double?)pLast["floor"] ?? double.NaN;
        yield return PCall(app, new { cmd = "wet.floor", key });
        Check("wet.floor: the map's surface", POk && (string)pLast["source"] == "map" && Math.Abs((double)pLast["y"] - floor) < 1e-4, Short(pLastText));
        yield return PCall(app, new { cmd = "wet.status", key });
        var row = pLast["characters"]?[0];
        Check("wet.status says so", (string)row?["floorSource"] == "map" && Math.Abs((double)row["floor"] - floor) < 1e-4, Short(pLastText));
        yield return PCall(app, new { cmd = "wet.floor", key, mode = "fixed", offset = 0.5 });
        Check("a fixed floor takes the map's height", POk && (string)pLast["source"] == "fixed" && Math.Abs((double)pLast["y"] - (floor + 0.5)) < 1e-4, Short(pLastText));
        yield return PCall(app, new { cmd = "scene.new" });
    }
}
