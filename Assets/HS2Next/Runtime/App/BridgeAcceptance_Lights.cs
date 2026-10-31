// LT1 of the bridge acceptance (Phase 3.3 package A): Studio lights, the chara light,
// camera slots and a camera object through the bridge, in Editor play mode (BridgeTest.RunPlay) and in the app player
// (-bridgeAccept) after M1. It also carries the play-mode checks LightCameraTest.play (Studio lights on map 32
// with the chara light shadow on / off and the main light per the rule; a camera object following a moving parent).
//   scene.new; map.load 32; light.add a spot (row 2) and a point (row 1); light.set; light.state (folded, masks = HS2's,
//   rendering layers = mask, not the main light); capture with the lights and with them disabled (the images differ);
//   undo / redo; a Studio directional (row 0) at intensity 5: the chara light is the main light while shadowed, the Studio
//   directional (brightest) when the chara shadow is off; charaLight.set rot (30, 45) → Cam Light local Euler (30, 45, 0);
//   camera.slot save / load; camera.add under an item, active: the view follows the item when it moves.
using System;
using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace HS2Next.App
{
    public static partial class BridgeAcceptance
    {
        static bool LtV(JToken t, double eps, params double[] v) =>
            t is JArray a && a.Count >= v.Length && v.Select((x, i) => Math.Abs((double)a[i] - x) <= eps).All(b => b);

        public static IEnumerator Lights(AcceptanceCtx c)
        {
            c.Id = "LT1";
            yield return c.Call(new { cmd = "scene.new" });
            c.CheckOk("scene.new");
            yield return c.Call(new { cmd = "map.load", no = 32 }, 300);
            c.Check(c.Last != null && c.Last.Ok && (string)c.J["state"] == "Loaded", "map.load 32", c.Last != null && c.Last.Ok ? "" : AcceptanceCtx.Short(c.Last?.ToString()));
            yield return c.Call(new { cmd = "light.add", no = 2, pos = new[] { 0, 18, 8 }, rot = new[] { 60, 180, 0 } });
            string spot = (string)c.J["key"];
            c.Check(c.Last.Ok && (string)c.J["type"] == "Spot" && (string)c.J["target"] == "all", "light.add spot (row 2)", AcceptanceCtx.Short(c.Last?.ToString()));
            yield return c.Call(new { cmd = "light.add", no = 1, pos = new[] { 3, 6, 3 } });
            string point = (string)c.J["key"];
            c.Check(c.Last.Ok && (string)c.J["type"] == "Point", "light.add point (row 1)", AcceptanceCtx.Short(c.Last?.ToString()));
            yield return c.Call(new { cmd = "light.set", key = spot, set = new { intensity = 1.5, range = 40, spotAngle = 60, color = new[] { 1, 0.9, 0.7, 1 } } });
            c.CheckOk("light.set spot {intensity 1.5, range 40, spotAngle 60, color}");
            yield return c.Call(new { cmd = "light.set", key = point, set = new { range = 20 } });
            c.CheckOk("light.set point {range 20}");
            yield return c.Call(new { cmd = "light.state", key = spot });
            var re = c.J["realized"];
            int all = 1 << 4 | 1 << 10 | 1 << 11 | 1 << 12;
            c.Check(re != null && LtV(re["color"], 1e-5, 1.5, 1.35, 1.05) && Math.Abs((double)re["intensity"] - 1) < 1e-6 && LtV(re["original"]?["color"], 1e-5, 1, 0.9, 0.7)
                    && Math.Abs((double)re["original"]["intensity"] - 1.5) < 1e-6 && (int)re["cullingMask"] == all && (long)re["renderingLayers"] == all
                    && Math.Abs((double)re["range"] - 40) < 1e-5 && Math.Abs((double)re["spotAngle"] - 60) < 1e-5 && (string)re["shadows"] == "Soft" && re["mainLight"]?.Value<bool>() == false,
                "spot realized: folded (1.5, 1.35, 1.05) × 1, original kept, mask 4,10,11,12 = rendering layers, range 40, spot 60, Soft, not the main light", AcceptanceCtx.Short(re?.ToString()));
            yield return c.Call(new { cmd = "light.state", key = point });
            c.Check(c.J["realized"]?["helpersShown"]?.Value<bool>() == true && c.J["realized"]?["helpersVisible"]?.Value<bool>() == true, "point: helpers shown in the view");
            yield return c.Call(new { cmd = "camera.set", target = new[] { 0, 8, 0 }, rot = new[] { 15, 180, 0 }, dist = 30, fov = 40 });
            c.CheckOk("camera.set");
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "capture", name = "lt1_lights" });
            string pathOn = (string)c.J["path"];
            if (c.CheckOk("capture lt1_lights"))
            {
                var (w, h, fg, _) = Png(pathOn, null);
                c.Check(w == 1280 && h == 720 && fg >= 0.01, "lt1_lights: PNG 1280 × 720, not empty", $"{w} × {h}, foreground {fg:P1}");
            }
            yield return c.Call(new { cmd = "light.set", keys = new[] { spot, point }, set = new { enabled = false } });
            c.CheckOk("light.set both {enabled: false}");
            yield return c.Frames(2);
            yield return c.Call(new { cmd = "capture", name = "lt1_off" });
            string pathOff = (string)c.J["path"];
            if (c.CheckOk("capture lt1_off"))
            {
                var d = PngDiff(pathOn, pathOff, out var why);
                c.Check(d.HasValue && d.Value.differing > 1000, "the Studio lights light the map (lights on vs off differ)", d.HasValue ? $"{d.Value.differing} px differ, max {d.Value.maxDiff}, mean {d.Value.meanAbs:F2}" : why);
            }
            yield return c.Call(new { cmd = "undo" });
            yield return c.Call(new { cmd = "light.state", key = spot });
            c.Check(c.J["realized"]?["enabled"]?.Value<bool>() == true, "undo: the spot is enabled again");
            yield return c.Call(new { cmd = "redo" });
            yield return c.Call(new { cmd = "light.state", key = point });
            c.Check(c.J["realized"]?["enabled"]?.Value<bool>() == false, "redo: the point is disabled again");

            // main light with a Studio directional
            yield return c.Call(new { cmd = "light.add", no = 0, rot = new[] { 50, 30, 0 } });
            string dir = (string)c.J["key"];
            yield return c.Call(new { cmd = "light.set", key = dir, set = new { intensity = 5 } });
            yield return c.Call(new { cmd = "scene.lights" });
            c.Check((string)c.J["studio"]?["mainLight"]?["reason"] == "chara", "a shadowed Studio directional with the chara light shadowed: the chara light is the main light",
                AcceptanceCtx.Short(c.J["studio"]?["mainLight"]?.ToString()));
            yield return c.Call(new { cmd = "charaLight.set", set = new { shadow = false } });
            c.CheckOk("charaLight.set {shadow: false}");
            yield return c.Call(new { cmd = "light.state", key = dir });
            bool isMain = c.J["realized"]?["mainLight"]?.Value<bool>() == true;
            yield return c.Call(new { cmd = "scene.lights" });
            c.Check(isMain && (string)c.J["studio"]?["mainLight"]?["reason"] == "brightest" && (string)c.J["renderSettings"]?["sun"] == (string)c.J["studio"]?["mainLight"]?["name"],
                "chara shadow off: the Studio directional (5 × white, brightest) is the main light and RenderSettings.sun", AcceptanceCtx.Short(c.J["studio"]?["mainLight"]?.ToString()));
            yield return c.Call(new { cmd = "charaLight.set", set = new { shadow = true, rot = new[] { 30, 45 } } });
            c.CheckOk("charaLight.set {shadow: true, rot: [30, 45]}");
            yield return c.Call(new { cmd = "scene.lights" });
            var cl = c.J["studio"]?["charaLight"]?["realized"];
            c.Check(LtV(cl?["localEuler"], 1e-3, 30, 45, 0) && cl?["mainLight"]?.Value<bool>() == true && (string)c.J["studio"]?["mainLight"]?["reason"] == "chara",
                "chara light: local Euler (30, 45, 0) under the view camera, the main light again", AcceptanceCtx.Short(cl?.ToString()));

            // slots
            yield return c.Call(new { cmd = "camera.slot", index = 2, op = "save" });
            c.CheckOk("camera.slot save 2");
            yield return c.Call(new { cmd = "camera.set", target = new[] { 5, 5, 5 }, rot = new[] { 0, 90, 0 }, dist = 10, fov = 60 });
            yield return c.Call(new { cmd = "camera.slot", index = 2, op = "load" });
            c.CheckOk("camera.slot load 2");
            yield return c.Call(new { cmd = "camera.get" });
            c.Check(LtV(c.J["camera"]?["target"], 1e-4, 0, 8, 0) && Math.Abs((double)c.J["camera"]["fov"] - 40) < 1e-4, "slot 2 restores the saved view", AcceptanceCtx.Short(c.J["camera"]?.ToString()));

            // a camera object under a moving item
            yield return c.Call(new { cmd = "item.add", no = 399 });
            string item = (string)c.J["added"]?[0]?["key"];
            yield return c.Call(new { cmd = "obj.transform", key = item, pos = new[] { 0, 0, 0 }, rot = new[] { 0, 0, 0 } });
            yield return c.Call(new { cmd = "camera.add", parent = item, pos = new[] { 0, 10, -20 }, active = true });
            string cam = (string)c.J["key"];
            c.Check(c.Last.Ok && cam != null, "camera.add under an item, active");
            yield return c.Frames(2);
            yield return c.Call(new { cmd = "camera.state" });
            var p0 = c.J["camera"]?["position"];
            c.Check((string)c.J["camera"]?["activeCamera"] == cam && LtV(p0, 1e-3, 0, 10, -20), "the view at the camera object's world pose", AcceptanceCtx.Short(p0?.ToString()));
            yield return c.Call(new { cmd = "obj.transform", key = item, pos = new[] { 4, 0, 0 } });
            yield return c.Frames(2);
            yield return c.Call(new { cmd = "camera.state" });
            c.Check(LtV(c.J["camera"]?["position"], 1e-3, 4, 10, -20), "the view follows when the parent moves", AcceptanceCtx.Short(c.J["camera"]?["position"]?.ToString()));
            yield return c.Call(new { cmd = "camera.activate", key = (string)null });
            c.CheckOk("camera.activate null");
            yield return c.Call(new { cmd = "scene.new" });
        }
    }
}
