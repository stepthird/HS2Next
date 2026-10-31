// M1 of the bridge acceptance (Phase 3.2 package A): Studio maps through the
// bridge, in Editor play mode (BridgeTest.RunPlay) and in the app player (-bridgeAccept) after I1.
//   scene.new; map.load vanilla 32 (152 lights, 53 active); map.set {light: false} then {light: true}; map.set {pos, rot};
//   capture "map_32"; map.state {lights: true}: no exception (the player's LightCookieManager regression of 2.5), every light
//   folded (colour = original × intensity, intensity 1) with rendering layers = culling mask, the main light per the rule;
//   the capture non-empty. Then a lightmapped mod map (2155X PH Subway, when installed): lightmaps and probes, a capture;
//   undo of that map change reloads map 32, redo the mod map again.
using System;
using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace HS2Next.App
{
    public static partial class BridgeAcceptance
    {
        public const string M1ModMap = "2155x.map.phsubway#0";

        public static IEnumerator Maps(AcceptanceCtx c)
        {
            c.Id = "M1";
            yield return c.Call(new { cmd = "scene.new" });
            c.CheckOk("scene.new");
            yield return c.Call(new { cmd = "map.load", no = 32 }, 300);
            c.Check(c.Last != null && c.Last.Ok && (string)c.J["state"] == "Loaded", "map.load 32", c.Last != null && c.Last.Ok ? "" : AcceptanceCtx.Short(c.Last?.ToString()));
            yield return c.Call(new { cmd = "map.set", light = false });
            c.Check(c.Last.Ok && (c.J["changed"] as JArray)?.Any(x => (string)x == "light") == true, "map.set {light: false}", AcceptanceCtx.Short(c.Last?.ToString()));
            yield return c.Call(new { cmd = "map.state", blocks = false });
            int lightsOff = (int?)c.J["counts"]?["lightsActive"] ?? -1;
            yield return c.Call(new { cmd = "map.set", light = true });
            c.CheckOk("map.set {light: true}");
            yield return c.Call(new { cmd = "map.state", blocks = false });
            int lightsOn = (int?)c.J["counts"]?["lightsActive"] ?? -1;
            c.Check(lightsOn > lightsOff && lightsOff >= 0, "the light block switches map lights (active lights off < on)", $"{lightsOff} / {lightsOn}");
            yield return c.Call(new { cmd = "map.set", pos = new[] { 0.5, 0, -0.5 }, rot = new[] { 0, 15, 0 } });
            c.CheckOk("map.set {pos, rot}");
            yield return c.Call(new { cmd = "camera.set", target = new[] { 0, 1.2, 0 }, rot = new[] { 12, 180, 0 }, dist = 8, fov = 45 });
            c.CheckOk("camera.set");
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "capture", name = "map_32" });
            string path = (string)c.J["path"];
            if (c.CheckOk("capture map_32 (152 map lights, cookies; no LightCookieManager exception)"))
            {
                var (w, h, fg, _) = Png(path, null);
                c.Check(w == 1280 && h == 720 && fg >= 0.01, "map_32: PNG 1280 × 720, not empty", $"{w} × {h}, foreground {fg:P1}");
            }
            yield return c.Call(new { cmd = "map.state", lights = true, blocks = false });
            CheckLights(c, "map 32");

            // a lightmapped mod map
            yield return c.Call(new { cmd = "maps", q = "PH Subway", limit = 5 });
            bool hasMod = (c.J["maps"] as JArray)?.Any(m => (string)m["id"] == M1ModMap) == true;
            if (!hasMod) { c.Warn("the lightmapped mod map " + M1ModMap + " is not installed: its part is skipped"); yield return c.Call(new { cmd = "scene.new" }); yield break; }
            yield return c.Call(new { cmd = "map.load", @ref = M1ModMap }, 600);
            c.Check(c.Last != null && c.Last.Ok && (string)c.J["state"] == "Loaded", "map.load " + M1ModMap, AcceptanceCtx.Short(c.Last?.ToString()));
            yield return c.Call(new { cmd = "map.state", lights = true, blocks = false });
            c.Check((int?)c.J["lightmaps"]?["count"] > 0 && (int?)c.J["counts"]?["lightmapped"] > 0 && (int?)c.J["probes"]?["count"] > 0 && c.J["probes"]?["tetrahedralized"]?.Value<bool>() == true,
                "lightmaps, lightmapped renderers and tetrahedralized probes", AcceptanceCtx.Short(c.J["lightmaps"]?["count"] + " / " + c.J["counts"]?["lightmapped"] + " / " + c.J["probes"]));
            CheckLights(c, M1ModMap);
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "capture", name = "map_lightmapped" });
            string p2 = (string)c.J["path"];
            if (c.CheckOk("capture map_lightmapped"))
            {
                var (w, h, fg, _) = Png(p2, null);
                c.Check(w == 1280 && h == 720 && fg >= 0.01, "map_lightmapped: PNG 1280 × 720, not empty", $"{w} × {h}, foreground {fg:P1}");
            }
            // undo / redo of the map change reload the maps
            yield return c.Call(new { cmd = "undo" });
            c.CheckOk("undo the map change");
            yield return Settled(c, "illusion.hs2#32");
            c.Check((string)c.J["ref"] == "illusion.hs2#32" && (string)c.J["state"] == "Loaded", "undo reloads map 32", AcceptanceCtx.Short((string)c.J["ref"] + " " + c.J["state"]));
            yield return c.Call(new { cmd = "redo" });
            c.CheckOk("redo the map change");
            yield return Settled(c, M1ModMap);
            c.Check((string)c.J["ref"] == M1ModMap && (string)c.J["state"] == "Loaded", "redo reloads the mod map", AcceptanceCtx.Short((string)c.J["ref"] + " " + c.J["state"]));
            yield return c.Call(new { cmd = "scene.new" });
        }

        /// <summary>Polls map.state until the map <paramref name="mref"/> is loaded and settled (at most 600 s).</summary>
        static IEnumerator Settled(AcceptanceCtx c, string mref)
        {
            var t0 = DateTime.UtcNow;
            while ((DateTime.UtcNow - t0).TotalSeconds < 600)
            {
                yield return c.Call(new { cmd = "map.state", blocks = false });
                if (c.Last != null && c.Last.Ok && (string)c.J["ref"] == mref && (string)c.J["state"] == "Loaded" && c.J["loading"]?.Value<bool>() == false) yield break;
                yield return c.Frames(10);
            }
        }

        /// <summary>The map.state {lights: true} reply in c.J: every light folded with rendering layers; the main light per the rule.</summary>
        static void CheckLights(AcceptanceCtx c, string what)
        {
            var lights = c.J["lights"] as JArray;
            int n = lights?.Count ?? 0, bad = 0, layers = 0;
            string first = null;
            foreach (var l in lights ?? new JArray())
            {
                var col = (JArray)l["color"]; var oc = (JArray)l["original"]?["color"];
                double oi = (double?)l["original"]?["intensity"] ?? double.NaN, i = (double)l["intensity"];
                bool folded = col != null && oc != null && Math.Abs(i - 1) < 1e-6 &&
                              Enumerable.Range(0, 3).All(k => Math.Abs((double)col[k] - (double)oc[k] * oi) <= 1e-5 * Math.Max(1, Math.Abs((double)oc[k] * oi)));
                if (!folded) { bad++; first ??= (string)l["path"]; }
                if ((long?)l["renderingLayers"] != unchecked((uint)(int)l["cull"])) layers++;
            }
            c.Check(n > 0 && bad == 0, what + ": every map light folded (colour = original × intensity, intensity 1)", $"{n} lights, {bad} not folded{(first != null ? " (" + first + ")" : "")}");
            c.Check(layers == 0, what + ": rendering layers = culling mask on every light", layers + " differ");
            var ml = c.J["mainLight"];
            string reason = (string)ml?["reason"];
            bool ruleOk = reason == "chara" || reason == "none";
            if (reason == "brightest")
            {
                var m = lights?.FirstOrDefault(l => (string)l["path"] == (string)ml["path"]);
                ruleOk = m != null && (string)m["type"] == "Directional" && (string)m["shadows"] != "None";
            }
            c.Check(ruleOk && (string)c.J["renderSettings"]?["sun"] == (string)ml?["name"], what + ": the main light per the rule and RenderSettings.sun", AcceptanceCtx.Short(ml?.ToString()));
        }
    }
}
