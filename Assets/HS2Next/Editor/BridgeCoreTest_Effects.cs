// C24 of BridgeCoreTest: the Phase 3.3 screen-effect commands of package B,
// further methods of the partial BridgeCoreTest (-only C24): registration (CoreCommands.Phase33EffectNames), argument
// errors (all-or-nothing validation), effects.set routing to the realized state in edit mode (grading strip, bloom,
// vignette, self shadow, fog's RenderSettings, the probe, the DOF focus), one history entry per call with undo / redo,
// effects.reset to ScreenEffectDefine's defaults, and the edit-mode shape of effects.state.
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

public static partial class BridgeCoreTest
{
    static void C24(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        var names = HS2Next.App.Commands.CoreCommands.Phase33EffectNames;
        c.Check(names.SequenceEqual(new[] { "effects.state", "effects.set", "effects.reset" }) && names.All(n => reg.Find(n) != null),
            "Phase33EffectNames = effects.state, effects.set, effects.reset, all registered", string.Join(", ", names.Where(n => reg.Find(n) == null)));

        // ---------------- effects.state shape
        var st = c.Ok(new { cmd = "effects.state" }, "effects.state");
        var blocks = HS2Next.Scenes.Runtime.SceneEffects.BlockNames;
        c.Check(st["effects"] is JObject fx && blocks.All(b => fx[b] is JObject), "effects.state: the 12 blocks' state values", Ctx.Short(c.LastText));
        c.Check((double)st["effects"]["bloom"]["intensity"] == 2 && (int)st["effects"]["colorGrading"]["contrast"] == 40 && (string)st["effects"]["colorGrading"]["lut"] == "illusion.hs2#0",
            "… the defaults (bloom 2, contrast 40, lut row 0)");
        var re = st["realized"];
        c.Check(re?["colorGrading"]?["mode"] != null && re["colorGrading"]["baseGrade"]?["source"] != null && re["bloom"]?["threshold"] != null && re["vignette"]?["intensity"] != null
            && re["ssr"]?["drawn"]?.Value<bool>() == false && re["fog"]?["drawn"]?.Value<bool>() == false && re["fog"]["renderSettings"]?["fog"] != null
            && re["sunShafts"]?["drawn"]?.Value<bool>() == false && (string)re["sunShafts"]["caster"]?["kind"] == "none" && re["sunShafts"]["sunViewport"] is JArray
            && re["sunShafts"]["behind"]?.Value<bool>() == false && re["sunShafts"]["resolution"] is JArray && re["fog"]["pass"] is JObject && re["fog"]["mode"] != null
            && re["depthOfField"]?["focus"]?["point"] is JArray && re["shadows"]?["renderShadows"]?.Value<bool>() == true
            && re["reflectionProbe"]?["active"]?.Value<bool>() == false && re["ambientOcclusion"]?["source"] != null,
            "effects.state realized: the members and the 5.R fog / shaft members (pass, mode; caster kind, sunViewport, behind, resolution)", Ctx.Short(re?.ToString()));
        var st2 = c.Ok(new { cmd = "effects.state", blocks = new[] { "bloom", "fog" }, timing = true }, "effects.state {blocks [bloom, fog], timing}");
        c.Check((st2["effects"] as JObject)?.Count == 2 && st2["realized"]?["bloom"] != null && st2["realized"]?["vignette"] == null && st2["timing"]?["bakeMs"] != null,
            "… only the asked blocks, timing present", Ctx.Short(c.LastText));
        c.Fails(new { cmd = "effects.state", blocks = new[] { "nope" } }, "bad_args", "effects.state blocks [nope] → bad_args");

        // ---------------- effects.set errors (nothing applied)
        c.Fails(new { cmd = "effects.set" }, "bad_args", "effects.set without set → bad_args");
        c.Fails(new { cmd = "effects.set", set = new { } }, "bad_args", "effects.set {} → bad_args");
        c.Fails(new { cmd = "effects.set", set = new JObject { ["nope.x"] = 1 } }, "bad_args", "effects.set unknown block → bad_args");
        c.Fails(new { cmd = "effects.set", set = new JObject { ["bloom.nope"] = 1 } }, "bad_args", "effects.set unknown member → bad_args");
        int n0 = W1UndoCount(c);
        c.Fails(new { cmd = "effects.set", set = new JObject { ["bloom.intensity"] = 5, ["vignette.intensity"] = "big" } }, "bad_args", "effects.set with one bad value → bad_args …");
        c.Check(W1UndoCount(c) == n0 && (double)c.Call(new { cmd = "effects.state", blocks = new[] { "bloom" } })["effects"]["bloom"]["intensity"] == 2, "… and nothing applied (bloom still 2)");

        // ---------------- effects.set routing
        string lutHash0 = (string)c.Call(new { cmd = "effects.state", blocks = new[] { "colorGrading" } })["realized"]["colorGrading"]["lutHash"];
        var set = c.Ok(new
        {
            cmd = "effects.set",
            set = new JObject
            {
                ["bloom.intensity"] = 5, ["vignette.intensity"] = 0.6, ["colorGrading.saturation"] = 50, ["shadows.enabled"] = false,
                ["fog.enabled"] = true, ["fog.density"] = 0.0009, ["effects.reflectionProbe.enabled"] = true, ["reflectionProbe.cubemap"] = "illusion.hs2#2",
            },
        }, "effects.set {bloom, vignette, saturation, shadows, fog, probe}");
        c.Check(set["changed"] is JArray ch && ch.Count == 8 && (set["noop"] as JArray)?.Count == 0, "effects.set: 8 changed, 0 noop", Ctx.Short(c.LastText));
        c.Check(W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Effects", "effects.set: one entry \"Effects\"", W1UndoCount(c) + " " + W1TopLabel(c));
        var s1 = c.Call(new { cmd = "effects.state" });
        var r1 = s1["realized"];
        c.Check((double)s1["effects"]["bloom"]["intensity"] == 5 && (double)r1["bloom"]["intensity"] > (double)re["bloom"]["intensity"], "bloom 5: the URP intensity rises", Ctx.Short(r1["bloom"]?.ToString()));
        c.Check(Math.Abs((double)r1["vignette"]["intensity"] - 0.6) < 1e-6, "vignette 0.6 on the volume");
        c.Check((string)r1["colorGrading"]["lutHash"] != lutHash0, "saturation 50: the strip was rebaked (hash changed)", lutHash0 + " → " + r1["colorGrading"]["lutHash"]);
        c.Check(r1["shadows"]["renderShadows"].Value<bool>() == false, "self shadow off → renderShadows false");
        c.Check(r1["fog"]["renderSettings"]["fog"].Value<bool>() == false && Math.Abs((double)r1["fog"]["renderSettings"]["density"] - 0.0009) < 1e-7, "fog: density written, RenderSettings.fog off");
        c.Check(r1["reflectionProbe"]["active"].Value<bool>() && r1["reflectionProbe"]["cubemap"]?.Type == JTokenType.String && (int)r1["reflectionProbe"]["importance"] == 10,
            "probe: active with Probe row 2's cubemap, importance 10", Ctx.Short(r1["reflectionProbe"]?.ToString()));
        c.Check((s1["findings"] as JArray)?.Any(f => (string)f["code"] == "effects.fogNotDrawn" || (string)f["code"] == "effects.sunShaftsNotDrawn") != true,
            "no effects.fogNotDrawn / sunShaftsNotDrawn finding (fog and shafts are drawn since 5.R)", Ctx.Short(s1["findings"]?.ToString()));
        var noop = c.Ok(new { cmd = "effects.set", set = new JObject { ["bloom.intensity"] = 5 } }, "effects.set with the current value");
        c.Check((noop["noop"] as JArray)?.Count == 1 && (noop["changed"] as JArray)?.Count == 0 && W1UndoCount(c) == n0 + 1, "… noop, no new entry");
        c.Ok(new { cmd = "effects.set", set = new JObject { ["depthOfField.focus"] = null, ["depthOfField.enabled"] = true }, label = "DOF on" }, "effects.set {depthOfField: focus null, enabled} with a label");
        c.Check(W1TopLabel(c) == "DOF on", "effects.set label", W1TopLabel(c));
        var s2 = c.Call(new { cmd = "effects.state", blocks = new[] { "depthOfField" } });
        c.Check((string)s2["realized"]["depthOfField"]["focus"]["resolved"] == "pivot" && s2["realized"]["depthOfField"]["active"].Value<bool>(), "DOF focus null → the pivot, active", Ctx.Short(c.LastText));

        // ---------------- undo / redo
        c.Ok(new { cmd = "undo", n = 2 }, "undo 2");
        var s3 = c.Call(new { cmd = "effects.state" });
        c.Check((double)s3["effects"]["bloom"]["intensity"] == 2 && s3["realized"]["shadows"]["renderShadows"].Value<bool>() && !s3["realized"]["reflectionProbe"]["active"].Value<bool>()
            && (string)s3["realized"]["colorGrading"]["lutHash"] == lutHash0, "undo restores values and the realized state (strip hash back)", Ctx.Short(s3["realized"]?["colorGrading"]?.ToString()));
        c.Ok(new { cmd = "redo" }, "redo 1");
        c.Check((double)c.Call(new { cmd = "effects.state", blocks = new[] { "bloom" } })["effects"]["bloom"]["intensity"] == 5, "redo restores effects.set");

        // ---------------- effects.reset
        c.Fails(new { cmd = "effects.reset", blocks = new[] { "nope" } }, "bad_args", "effects.reset blocks [nope] → bad_args");
        int n1 = W1UndoCount(c);
        var rs = c.Ok(new { cmd = "effects.reset", blocks = new[] { "bloom", "vignette", "ssr" } }, "effects.reset {bloom, vignette, ssr}");
        c.Check(rs["changed"] is JArray rc && rc.Select(x => (string)x).SequenceEqual(new[] { "bloom", "vignette" }) && W1UndoCount(c) == n1 + 1 && W1TopLabel(c) == "Reset effects",
            "effects.reset: bloom and vignette changed (ssr was at its default), one entry", Ctx.Short(c.LastText));
        var s4 = c.Call(new { cmd = "effects.state" });
        c.Check((double)s4["effects"]["bloom"]["intensity"] == 2 && Math.Abs((double)s4["effects"]["vignette"]["intensity"] - 0.35) < 1e-6 && s4["effects"]["shadows"]["enabled"].Value<bool>() == false,
            "… the two blocks at their defaults, self shadow untouched");
        c.Ok(new { cmd = "effects.reset" }, "effects.reset (all)");
        var s5 = c.Call(new { cmd = "effects.state" });
        c.Check(s5["effects"]["shadows"]["enabled"].Value<bool>() && !s5["effects"]["fog"]["enabled"].Value<bool>() && (int)s5["effects"]["colorGrading"]["saturation"] == 15
            && s5["realized"]["shadows"]["renderShadows"].Value<bool>(), "reset all: every block at ScreenEffectDefine's defaults, realized");
        c.Ok(new { cmd = "undo" }, "undo the reset");
        c.Check(c.Call(new { cmd = "effects.state" })["effects"]["shadows"]["enabled"].Value<bool>() == false, "undo of the reset restores self shadow off");
        c.Ok(new { cmd = "effects.reset" }, "effects.reset again (clean state for the next group)");

        // ---------------- render.ssr (Phase 5.R): the option, its validation and effects.state realized.ssr
        var ro = c.Ok(new { cmd = "app.options" }, "app.options (read)");
        c.Check((string)ro["render"]?["ssr"] == "off", "render.ssr is listed and off by default", Ctx.Short(ro["render"]?.ToString()));
        c.Fails(new { cmd = "app.options", render = new JObject { ["ssr"] = "maybe" } }, "bad_args", "render.ssr \"maybe\" → bad_args");
        var sr0 = c.Call(new { cmd = "effects.state", blocks = new[] { "ssr" } })["realized"]?["ssr"];
        c.Check(sr0?["drawn"]?.Value<bool>() == false && (string)sr0["option"] == "off" && sr0["sceneFlag"]?.Value<bool>() == true
            && ((string)sr0["reason"] == "option-off" || (string)sr0["reason"] == "define-missing") && sr0["receivers"] != null,
            "realized.ssr with the defaults: not drawn, option off, the scene flag on (HS2's default), reason option-off", Ctx.Short(sr0?.ToString()));
        var ron = c.Ok(new { cmd = "app.options", render = new JObject { ["ssr"] = "on" } }, "app.options render.ssr on");
        c.Check((string)ron["render"]?["ssr"] == "on", "… the reply lists ssr on", Ctx.Short(ron["render"]?.ToString()));
        var sr1 = c.Call(new { cmd = "effects.state", blocks = new[] { "ssr" } })["realized"]?["ssr"];
        c.Check((string)sr1?["option"] == "on" && (sr1["drawn"]?.Value<bool>() == true || (string)sr1["reason"] == "define-missing"),
            "realized.ssr with the option on and the flag on: drawn (or define-missing in a build without SSR)", Ctx.Short(sr1?.ToString()));
        c.Ok(new { cmd = "effects.set", set = new JObject { ["ssr.enabled"] = false } }, "effects.set ssr off");
        var sr2 = c.Call(new { cmd = "effects.state", blocks = new[] { "ssr" } })["realized"]?["ssr"];
        c.Check(sr2?["drawn"]?.Value<bool>() == false && sr2["sceneFlag"]?.Value<bool>() == false && ((string)sr2["reason"] == "scene-off" || (string)sr2["reason"] == "define-missing"),
            "… the scene flag off: not drawn, reason scene-off", Ctx.Short(sr2?.ToString()));
        c.Ok(new { cmd = "app.options", render = new JObject { ["ssr"] = "off" } }, "app.options render.ssr off (back to the default)");
        c.Ok(new { cmd = "effects.reset" }, "effects.reset (clean state)");
    }
}
