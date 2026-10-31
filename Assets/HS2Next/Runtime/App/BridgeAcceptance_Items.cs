// I1 of the bridge acceptance (Phase 3.1 package A): Studio items with
// cookie lights in a map, captured through the bridge — the player case (URP reads the additional light
// data of a light with a cookie unchecked; item lights get it from BundleLights). Runs in Editor play mode
// (BridgeTest.RunPlay) and in the app player (-bridgeAccept) after T5.
//   scene.new, map.load 10, item.add 821 (portable lamp: a point light with a cookie) and 1017 when it exists, a fixed
//   camera, capture "item_lights" → PNG 1280 × 720 with a foreground ≥ 1 %; item.state: the lights prepared and folded.
using System.Collections;
using Newtonsoft.Json.Linq;

namespace HS2Next.App
{
    public static partial class BridgeAcceptance
    {
        public static IEnumerator Items(AcceptanceCtx c)
        {
            c.Id = "I1";
            yield return c.Call(new { cmd = "scene.new" });
            c.CheckOk("scene.new");
            yield return c.Call(new { cmd = "map.load", no = 10 }, 180);
            c.Check(c.Last != null && c.Last.Ok && (string)c.J["state"] == "Loaded", "map.load 10", c.Last != null && c.Last.Ok ? "" : AcceptanceCtx.Short(c.Last?.ToString()));
            foreach (var no in new[] { 821, 1017 })
            {
                yield return c.Call(new { cmd = "item.add", no });
                if (no != 821 && (c.Last == null || !c.Last.Ok)) { c.Info("item " + no + " not available: " + AcceptanceCtx.Short(c.Last?.ToString())); continue; }
                if (!c.CheckOk("item.add " + no)) continue;
                string key = (string)c.J["added"]?[0]?["key"];
                yield return c.Call(new { cmd = "obj.transform", key, pos = new[] { no == 821 ? -0.5 : 0.5, 1, 0 } });
                yield return c.Call(new { cmd = "item.state", key });
                var real = c.J["realized"];
                c.Check(c.Last.Ok && (int?)real?["lights"] > 0 && (int?)real?["lightsFolded"] == (int?)real?["lights"], no + ": item lights prepared (URP data) and folded", AcceptanceCtx.Short(real?.ToString()));
            }
            yield return c.Call(new { cmd = "camera.set", target = new[] { 0, 1, 0 }, rot = new[] { 10, 180, 0 }, dist = 4, fov = 40 });
            c.CheckOk("camera.set");
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "capture", name = "item_lights" });
            string path = (string)c.J["path"];
            if (c.CheckOk("capture item_lights (cookie lights in a map; no LightCookieManager exception)"))
            {
                var (w, h, fg, _) = Png(path, null);
                c.Check(w == 1280 && h == 720 && fg >= 0.01, "item_lights: PNG 1280 × 720 with a foreground ≥ 1 %", $"{w} × {h}, foreground {fg:P1}");
            }
            yield return c.Call(new { cmd = "scene.new" });
        }
    }
}
