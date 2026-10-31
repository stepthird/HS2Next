// FX1 of the bridge acceptance (Phase 3.3 package B): the screen effects through
// the bridge, in Editor play mode (BridgeTest.RunPlay) and in the app player (-bridgeAccept) after LT1.
//   scene.new; map.load vanilla 10; a fixed camera; capture "fx1_defaults" (HS2's default effects); effects.set a filter LUT
//   (row 10, blend 1), bloom 10, vignette 0.6, AO 1 / thickness 4, the camera probe (row 5 × 2) and sky pattern 3; capture
//   "fx1_on" (differs from the defaults); effects.state: the base grade read, the LUT found, the strip baked, URP bloom /
//   vignette / probe realized, AO's source reported; effects.set {colorGrading saturation 0, brightness 0, contrast 0,
//   bloom off, vignette off, AO off, probe off} capture "fx1_off"; scene.save → scene.new (defaults) → scene.load: the 12
//   blocks equal the saved ones and capture "fx1_reload" = "fx1_on" (mean ≤ 1 / 255); effects.reset, undo / redo.
//   Phase 5.R: fog on (capture "fx1_fog" differs from "fx1_defaults", realized.fog.drawn); sky pattern 0 and a camera looking
//   up, sun shafts on with no caster (capture "fx1_shafts" differs from "fx1_sky", realized.sunShafts.drawn, caster none).
using System;
using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HS2Next.App
{
    public static partial class BridgeAcceptance
    {
        public static IEnumerator Effects(AcceptanceCtx c)
        {
            c.Id = "FX1";
            yield return c.Call(new { cmd = "scene.new" });
            c.CheckOk("scene.new");
            yield return c.Call(new { cmd = "map.load", no = 10 }, 300);
            c.Check(c.Last != null && c.Last.Ok && (string)c.J["state"] == "Loaded", "map.load 10", c.Last != null && c.Last.Ok ? "" : AcceptanceCtx.Short(c.Last?.ToString()));
            yield return c.Call(new { cmd = "camera.set", target = new[] { 0, 10, 0 }, rot = new[] { 10, 180, 0 }, dist = 45, fov = 40 });
            c.CheckOk("camera.set");
            yield return c.Call(new { cmd = "effects.state", blocks = new[] { "bloom" } });
            bool post = c.J["realized"]?["renderer"]?["postProcessing"]?.Value<bool>() == true;
            if (!post) c.Warn("the URP renderer has no post-processing data (package C sets it): grading / bloom / vignette are not drawn, the image checks are reported only");
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "capture", name = "fx1_defaults" });
            string pDef = (string)c.J["path"];
            c.CheckOk("capture fx1_defaults");

            var on = new JObject
            {
                ["colorGrading.lut"] = "illusion.hs2#10", ["colorGrading.blend"] = 1, ["bloom.intensity"] = 10, ["vignette.intensity"] = 0.6,
                ["ambientOcclusion.intensity"] = 1, ["ambientOcclusion.thickness"] = 4, ["reflectionProbe.enabled"] = true,
                ["reflectionProbe.cubemap"] = "illusion.hs2#5", ["reflectionProbe.intensity"] = 2, ["sky.pattern"] = "illusion.hs2#3",
            };
            yield return c.Call(new { cmd = "effects.set", set = on });
            c.Check(c.Last.Ok && (c.J["changed"] as JArray)?.Count == on.Count, "effects.set {LUT 10, bloom 10, vignette 0.6, AO 1/4, probe 5 × 2, sky 3}", AcceptanceCtx.Short(c.Last?.ToString()));
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "capture", name = "fx1_on" });
            string pOn = (string)c.J["path"];
            if (c.CheckOk("capture fx1_on"))
            {
                var (w, h, fg, mad) = Png(pOn, pDef);
                c.Check(w == 1280 && h == 720 && fg >= 0.01, "fx1_on: PNG 1280 × 720, not empty", $"{w} × {h}, foreground {fg:P1}");
                if (post) c.Check(mad >= 2, "fx1_on differs from the defaults", $"mean |d| {mad:F2}");
                else c.Info($"fx1_on vs fx1_defaults: mean |d| {mad:F2} (post-processing off)");
            }
            yield return c.Call(new { cmd = "effects.state" });
            var re = c.J["realized"];
            c.Check(re?["colorGrading"]?["baseGrade"]?["ok"]?.Value<bool>() == true && re["colorGrading"]["lutFound"]?.Value<bool>() == true && (string)re["colorGrading"]["mode"] == "hs2"
                && re["colorGrading"]["lutHash"]?.Type == JTokenType.String && Math.Abs((double)re["colorGrading"]["contribution"] - 1) < 1e-6,
                "grading: base grade read from the install, LUT row 10 found, strip baked, contribution 1", AcceptanceCtx.Short(re?["colorGrading"]?.ToString()));
            c.Check((double?)re?["bloom"]?["intensity"] > 0 && Math.Abs((double)re["vignette"]["intensity"] - 0.6) < 1e-6 && re["reflectionProbe"]?["active"]?.Value<bool>() == true
                && (string)re["reflectionProbe"]["cubemap"] != null, "bloom, vignette 0.6 and the camera probe realized", AcceptanceCtx.Short(re?["bloom"] + " " + re?["reflectionProbe"]));
            string aoSrc = (string)re?["ambientOcclusion"]?["source"];
            c.Check(aoSrc != null && aoSrc != "none" && re["ambientOcclusion"]["active"]?.Value<bool>() == true && (string)re["ambientOcclusion"]["pass"] == "HS2AoFeature"
                && re["ambientOcclusion"]["urpSsaoActive"]?.Value<bool>() != true, "AO: HS2AoFeature active (" + aoSrc + "), URP's SSAO off", AcceptanceCtx.Short(re?["ambientOcclusion"]?.ToString()));
            var saved = c.J["effects"] as JObject;

            yield return c.Call(new
            {
                cmd = "effects.set",
                set = new JObject
                {
                    ["colorGrading.saturation"] = 0, ["colorGrading.brightness"] = 0, ["colorGrading.contrast"] = 0, ["colorGrading.lut"] = "illusion.hs2#0",
                    ["bloom.enabled"] = false, ["vignette.enabled"] = false, ["ambientOcclusion.enabled"] = false, ["reflectionProbe.enabled"] = false,
                },
            });
            c.CheckOk("effects.set: the sliders 0, bloom / vignette / AO / probe off");
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "capture", name = "fx1_off" });
            string pOff = (string)c.J["path"];
            if (c.CheckOk("capture fx1_off"))
            {
                var (_, _, _, madOff) = Png(pOff, pOn);
                if (post) c.Check(madOff >= 2, "fx1_off differs from fx1_on", $"mean |d| {madOff:F2}");
                else c.Info($"fx1_off vs fx1_on: mean |d| {madOff:F2} (post-processing off)");
            }
            yield return c.Call(new { cmd = "undo" });
            c.CheckOk("undo (back to the effects of fx1_on)");
            // a -batchmode player renders no frame of its own and URP's probe atlas takes the run-time camera probe only
            // sometimes (package S): there the reload is also compared with the probe off
            bool batch = Application.isBatchMode;
            string pOnNp = null;
            if (batch)
            {
                yield return c.Call(new { cmd = "effects.set", set = new JObject { ["reflectionProbe.enabled"] = false }, label = "probe off (batchmode)" });
                yield return c.Frames(3);
                yield return c.Call(new { cmd = "capture", name = "fx1_on_noprobe" });
                pOnNp = (string)c.J["path"];
                yield return c.Call(new { cmd = "undo" });
            }

            // save → new → load
            yield return c.Call(new { cmd = "scene.save", name = "fx1_effects", overwrite = true, thumb = "placeholder" });
            c.CheckOk("scene.save fx1_effects");
            yield return c.Call(new { cmd = "scene.new" });
            c.CheckOk("scene.new");
            yield return c.Call(new { cmd = "effects.state", blocks = new[] { "bloom", "colorGrading" } });
            c.Check(c.Last.Ok && (double)c.J["effects"]["bloom"]["intensity"] == 2 && (string)c.J["effects"]["colorGrading"]["lut"] == "illusion.hs2#0", "scene.new: the defaults");
            yield return c.Call(new { cmd = "scene.load", name = "fx1_effects" }, 300);
            c.CheckOk("scene.load fx1_effects");
            yield return c.Call(new { cmd = "effects.state" });
            var loaded = c.J["effects"] as JObject;
            c.Check(saved != null && JToken.DeepEquals(saved, loaded), "the 12 blocks after the load equal the saved ones", AcceptanceCtx.Short(loaded?.ToString(Newtonsoft.Json.Formatting.None)));
            yield return c.Call(new { cmd = "camera.set", target = new[] { 0, 10, 0 }, rot = new[] { 10, 180, 0 }, dist = 45, fov = 40 });
            yield return c.Frames(5);
            yield return c.Call(new { cmd = "capture", name = "fx1_reload" });
            string pRe = (string)c.J["path"];
            if (c.CheckOk("capture fx1_reload"))
            {
                var (_, _, _, madRe) = Png(pRe, pOn);
                if (!batch) c.Check(madRe <= 1, "fx1_reload = fx1_on (mean ≤ 1 / 255)", $"mean |d| {madRe:F2}");
                else c.Info($"fx1_reload vs fx1_on with the camera probe on (-batchmode: the probe may or may not show): mean |d| {madRe:F2}");
            }
            if (batch)
            {
                yield return c.Call(new { cmd = "effects.set", set = new JObject { ["reflectionProbe.enabled"] = false }, label = "probe off (batchmode)" });
                yield return c.Frames(3);
                yield return c.Call(new { cmd = "capture", name = "fx1_reload_noprobe" });
                string pReNp = (string)c.J["path"];
                if (c.CheckOk("capture fx1_reload_noprobe"))
                {
                    var (_, _, _, madNp) = Png(pReNp, pOnNp);
                    c.Check(madNp <= 1, "-batchmode: fx1_reload = fx1_on with the probe off (mean ≤ 1 / 255)", $"mean |d| {madNp:F2}");
                }
                yield return c.Call(new { cmd = "undo" });
            }

            // reset, undo / redo
            yield return c.Call(new { cmd = "effects.reset" });
            c.Check(c.Last.Ok && (c.J["changed"] as JArray)?.Count >= 4, "effects.reset (all): the changed blocks", AcceptanceCtx.Short(c.Last?.ToString()));
            yield return c.Call(new { cmd = "effects.state", blocks = new[] { "bloom" } });
            c.Check((double)c.J["effects"]["bloom"]["intensity"] == 2, "… bloom back to 2");
            yield return c.Call(new { cmd = "undo" });
            yield return c.Call(new { cmd = "effects.state", blocks = new[] { "bloom" } });
            c.Check((double)c.J["effects"]["bloom"]["intensity"] == 10, "undo of the reset: bloom 10");
            yield return c.Call(new { cmd = "redo" });
            yield return c.Call(new { cmd = "effects.state", blocks = new[] { "bloom" } });
            c.Check((double)c.J["effects"]["bloom"]["intensity"] == 2, "redo of the reset: bloom 2");

            // Phase 5.R: fog and sun shafts are drawn (one capture each against the state before; realized drawn)
            yield return c.Call(new { cmd = "effects.reset" });
            yield return c.Call(new { cmd = "effects.set", set = new JObject { ["fog.enabled"] = true }, label = "fog on" });
            c.CheckOk("effects.set fog on");
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "capture", name = "fx1_fog" });
            string pFog = (string)c.J["path"];
            if (c.CheckOk("capture fx1_fog"))
            {
                var (_, _, _, madFog) = Png(pFog, pDef);
                c.Check(madFog >= 0.5, "fx1_fog differs from fx1_defaults (the fog is drawn)", $"mean |d| {madFog:F2}");
            }
            yield return c.Call(new { cmd = "effects.state", blocks = new[] { "fog" } });
            c.Check(c.J["realized"]?["fog"]?["drawn"]?.Value<bool>() == true, "realized.fog.drawn", AcceptanceCtx.Short(c.J["realized"]?["fog"]?.ToString()));
            yield return c.Call(new { cmd = "effects.set", set = new JObject { ["fog.enabled"] = false, ["sky.enabled"] = true, ["sky.pattern"] = "illusion.hs2#0" } });
            // a camera at (0, 5, 30) looking up at the sky: HS2 draws shafts there with the sky pattern 0 and no caster (R0a)
            yield return c.Call(new { cmd = "camera.set", target = new[] { 0, 22.2, 5.4 }, rot = new[] { -35, 180, 0 }, dist = 30, fov = 60 });
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "capture", name = "fx1_sky" });
            string pSky = (string)c.J["path"];
            yield return c.Call(new { cmd = "effects.set", set = new JObject { ["sunShafts.enabled"] = true, ["sunShafts.caster"] = null }, label = "shafts on" });
            c.CheckOk("effects.set sun shafts on (caster none)");
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "capture", name = "fx1_shafts" });
            string pSh = (string)c.J["path"];
            if (c.CheckOk("capture fx1_shafts") && pSky != null)
            {
                var (_, _, _, madSh) = Png(pSh, pSky);
                c.Check(madSh >= 0.5, "fx1_shafts differs from the sky without shafts (the shafts are drawn)", $"mean |d| {madSh:F2}");
            }
            yield return c.Call(new { cmd = "effects.state", blocks = new[] { "sunShafts" } });
            c.Check(c.J["realized"]?["sunShafts"]?["drawn"]?.Value<bool>() == true && (string)c.J["realized"]["sunShafts"]["caster"]?["kind"] == "none",
                "realized.sunShafts.drawn, caster none", AcceptanceCtx.Short(c.J["realized"]?["sunShafts"]?.ToString()));
            yield return c.Call(new { cmd = "effects.reset" });
            yield return c.Call(new { cmd = "scene.new" });
        }
    }
}
