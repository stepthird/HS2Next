// C23 of BridgeCoreTest: the Phase 3.3 light, camera and sound commands of package A (
// 2), further methods of the partial BridgeCoreTest (-only C23): registration (CoreCommands.Phase33LightNames), argument
// errors, one history entry per mutating call with undo / redo, the edit-mode shapes of light.state, scene.lights,
// camera.state / camera.slots, sound.state / sound.list and app.options render. HS2's numbers checked here: the target
// masks of the nine vanilla Light rows (H6: All 4,10,11,12; Chara 4,10; Map 4,11,12), the folding (colour × intensity,
// intensity 1) and the reset view (H2).
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

public static partial class BridgeCoreTest
{
    static readonly int[] C23All = { 4, 10, 11, 12 }, C23Chara = { 4, 10 }, C23Map = { 4, 11, 12 };

    static int C23Mask(params int[] layers) => layers.Aggregate(0, (m, l) => m | 1 << l);

    static bool C23V(JToken t, params double[] v) => C23E(t, 1e-4, v);

    static bool C23E(JToken t, double eps, params double[] v) =>
        t is JArray a && a.Count >= v.Length && v.Select((x, i) => Math.Abs((double)a[i] - x) <= eps).All(b => b);

    static void C23(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        var names = HS2Next.App.Commands.CoreCommands.Phase33LightNames;
        c.Check(names.SequenceEqual(HS2Next.App.Commands.LightCameraCommands.Names) && names.Count == 11 && names.All(n => reg.Find(n) != null),
            "Phase33LightNames = the 11 commands of LightCameraCommands, all registered", string.Join(", ", names.Where(n => reg.Find(n) == null)));
        C23Lights(c);
        C23CharaLight(c);
        C23Camera(c);
        C23Sound(c);
        C23Options(c);
    }

    static void C23Lights(Ctx c)
    {
        // ---------------- light.add errors
        c.Fails(new { cmd = "light.add" }, "bad_args", "light.add without ref / no → bad_args");
        c.Fails(new { cmd = "light.add", no = 999 }, "not_found", "light.add no 999 → not_found");
        c.Fails(new { cmd = "light.add", @ref = "nonsense" }, "bad_args", "light.add ref \"nonsense\" → bad_args");
        c.Fails(new { cmd = "light.add", no = 0, @ref = "illusion.hs2#0" }, "bad_args", "light.add with ref and no → bad_args");

        // ---------------- the nine vanilla rows: type, target, masks (H6), folding
        int n0 = W1UndoCount(c);
        var keys = new List<string>();
        var seen = new List<string>();
        bool masksOk = true, foldOk = true;
        for (int no = 0; no < 9; no++)
        {
            var add = c.Ok(new { cmd = "light.add", no, pos = new[] { no, 12, 3 }, rot = new[] { 60, 180, 0 } }, "light.add no " + no);
            string key = (string)add["key"];
            keys.Add(key);
            string target = (string)add["target"], type = (string)add["type"];
            seen.Add(target + "/" + type);
            var st = c.Call(new { cmd = "light.state", key });
            var re = st["realized"];
            int mask = re?["cullingMask"]?.Value<int>() ?? -1;
            int want = target == "chara" ? C23Mask(C23Chara) : target == "map" ? C23Mask(C23Map) : C23Mask(C23All);
            if (mask != want || re?["prefabCullingMask"]?.Value<int>() != C23Mask(C23All) || re?["renderingLayers"]?.Value<long>() != want) { masksOk = false; c.Info($"no {no} {target}/{type}: mask {mask} want {want}; {Ctx.Short(re?.ToString())}"); }
            if (!(re != null && Math.Abs((double)re["intensity"] - 1) < 1e-6 && C23V(re["color"], 1, 1, 1) && C23V(re["original"]?["color"], 1, 1, 1, 1) && Math.Abs((double)re["original"]["intensity"] - 1) < 1e-6
                  && (string)re["shadows"] == "Soft" && Math.Abs((double)re["shadowStrength"] - 0.8) < 1e-4 && (bool)re["helpersShown"]))
            { foldOk = false; c.Info($"no {no}: {Ctx.Short(re?.ToString())}"); }
            c.Check(st["state"]?["intensity"]?.Value<double>() == 1 && (double)st["state"]["range"] == 10 && (double)st["state"]["spotAngle"] == 30 && st["row"]?["no"]?.Value<int>() == no,
                "light.state " + no + ": state = the new light's defaults, row no", Ctx.Short(st["state"]?.ToString()));
        }
        c.Check(seen.Distinct().Count() == 9 && seen.All(x => !x.EndsWith("/")), "the nine rows: All / Chara / Map × Directional / Point / Spot, all built", string.Join(", ", seen));
        c.Check(masksOk, "target masks of the nine rows = HS2's (All 4,10,11,12; Chara 4,10; Map 4,11,12), rendering layers = mask, prefab mask 4,10,11,12");
        c.Check(foldOk, "new lights: folded white × 1, original white × 1, Soft, strength 0.8 (prefab), helpers shown");
        c.Check(W1UndoCount(c) == n0 + 9 && W1TopLabel(c) == "Add light", "light.add: one entry \"Add light\" each (pos / rot in the same entry)", W1UndoCount(c) + " " + W1TopLabel(c));
        var holderPos = W1V3(c.Call(new { cmd = "light.state", key = keys[4] })["realized"]?["position"]);
        c.Check(W1Dist(holderPos, new UnityEngine.Vector3(4, 12, 3)) < 1e-3f, "light.add pos is the node's position", holderPos.ToString());

        // ---------------- light.set
        string k0 = keys[0], k1 = keys[1];
        c.Fails(new { cmd = "light.set", key = k0 }, "bad_args", "light.set without set → bad_args");
        c.Fails(new { cmd = "light.set", key = k0, set = new { @ref = "illusion.hs2#1" } }, "bad_args", "light.set ref → bad_args (not settable)");
        int n1 = W1UndoCount(c);
        c.Fails(new { cmd = "light.set", key = k0, set = new Dictionary<string, object> { ["intensity"] = 2, ["range"] = "far" } }, "bad_args", "light.set with one bad value → bad_args …");
        c.Check(W1UndoCount(c) == n1 && (double)c.Call(new { cmd = "light.state", key = k0 })["state"]["intensity"] == 1, "… and nothing applied");
        var item = c.Call(new { cmd = "item.add", no = 399 });
        string itemKey = (string)item["added"]?[0]?["key"];
        if (itemKey != null) c.Fails(new { cmd = "light.set", key = itemKey, set = new { intensity = 2 } }, "wrong_kind", "light.set on an item → wrong_kind");
        int n2 = W1UndoCount(c);
        var set = c.Ok(new { cmd = "light.set", key = k0, set = new Dictionary<string, object> { ["intensity"] = 2, ["color"] = new[] { 1, 0.5, 0.25, 1 }, ["light.range"] = 30, ["shadow"] = false } }, "light.set {intensity, color, light.range, shadow}");
        c.Check(set["changed"] is JArray ch && ch.Select(x => (string)x).SequenceEqual(new[] { "light.intensity", "light.color", "light.range", "light.shadow" }) && W1UndoCount(c) == n2 + 1 && W1TopLabel(c) == "Light",
            "light.set: changed the four canonical paths, one entry \"Light\"", Ctx.Short(c.LastText) + " " + W1TopLabel(c));
        var re0 = c.Call(new { cmd = "light.state", key = k0 })["realized"];
        c.Check(C23V(re0?["color"], 2, 1, 0.5) && Math.Abs((double)re0["intensity"] - 1) < 1e-6 && C23V(re0["original"]?["color"], 1, 0.5, 0.25) && Math.Abs((double)re0["original"]["intensity"] - 2) < 1e-6
                && Math.Abs((double)re0["range"] - 30) < 1e-6 && (string)re0["shadows"] == "None",
            "folded: colour = rgb × 2, intensity 1, original = the state's (1, 0.5, 0.25) × 2, range 30, shadows None", Ctx.Short(re0?.ToString()));
        var noop = c.Ok(new { cmd = "light.set", key = k0, set = new { intensity = 2 } }, "light.set with the current value");
        c.Check((noop["noop"] as JArray)?.Count == 1 && (noop["changed"] as JArray)?.Count == 0 && W1UndoCount(c) == n2 + 1, "… noop, no new entry");
        c.Ok(new { cmd = "light.set", keys = new[] { k0, k1 }, set = new { enabled = false, showTarget = false }, label = "Lights off" }, "light.set keys [2] {enabled, showTarget}");
        var re1 = c.Call(new { cmd = "light.state", key = k1 })["realized"];
        c.Check(W1TopLabel(c) == "Lights off" && re1?["enabled"]?.Value<bool>() == false && re1["helpersShown"]?.Value<bool>() == false && re1["helpersVisible"]?.Value<bool>() == false,
            "keys: both lights off, helpers hidden, one entry with the label", Ctx.Short(re1?.ToString()));
        c.Ok(new { cmd = "undo", n = 2 }, "undo 2");
        var re2 = c.Call(new { cmd = "light.state", key = k0 })["realized"];
        c.Check(C23V(re2?["color"], 1, 1, 1) && C23V(re2["original"]?["color"], 1, 1, 1) && (string)re2["shadows"] == "Soft" && re2["enabled"]?.Value<bool>() == true && Math.Abs((double)re2["range"] - 10) < 1e-6,
            "undo restores the realized light (white × 1, Soft, range 10, enabled)", Ctx.Short(re2?.ToString()));
        c.Ok(new { cmd = "redo" }, "redo");
        c.Check(C23V(c.Call(new { cmd = "light.state", key = k0 })["realized"]?["color"], 2, 1, 0.5), "redo re-applies the folded colour");

        // ---------------- scene.lights additions
        var sl = c.Ok(new { cmd = "scene.lights" }, "scene.lights");
        var studio = sl["studio"];
        c.Check(studio?["lights"] is JArray la && la.Count == 9 && la.All(x => x["key"] != null && x["realized"]?["cullingMask"] != null && x["state"]?["range"] != null)
                && studio["charaLight"]?["realized"] != null && studio["ambient"]?["rule"] != null && studio["sky"]?["enabled"] != null && studio["mainLight"]?["reason"] != null,
            "scene.lights: studio.lights (9, with state and realized), charaLight.realized, ambient.rule, sky", Ctx.Short(studio?.ToString()));
        c.Fails(new { cmd = "light.state", key = itemKey ?? "x" }, itemKey != null ? "wrong_kind" : "not_found", "light.state on a non-light → error");
        c.Ok(new { cmd = "delete", keys = keys.Concat(itemKey != null ? new[] { itemKey } : new string[0]).ToArray() }, "delete the lights");
    }

    static void C23CharaLight(Ctx c)
    {
        c.Fails(new { cmd = "charaLight.set", set = new { rot = new[] { 1 } } }, "bad_args", "charaLight.set rot [1] → bad_args");
        c.Fails(new { cmd = "charaLight.set", set = new { range = 3 } }, "bad_args", "charaLight.set range → bad_args");
        int n0 = W1UndoCount(c);
        c.Ok(new { cmd = "charaLight.set", set = new Dictionary<string, object> { ["rot"] = new[] { 30, 45 }, ["intensity"] = 0.5, ["charaLight.color"] = new[] { 1, 0.8, 0.6, 1 } } }, "charaLight.set {rot, intensity, color}");
        c.Check(W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Chara light", "one entry \"Chara light\"", W1TopLabel(c));
        var cl = c.Call(new { cmd = "scene.lights" })["studio"]?["charaLight"];
        var re = cl?["realized"];
        c.Check(C23V(cl?["rot"], 30, 45) && C23E(re?["localEuler"], 1e-3, 30, 45, 0) && C23V(re?["color"], 0.5, 0.4, 0.3) && Math.Abs((double)re["intensity"] - 1) < 1e-6
                && Math.Abs((double)re["shadowStrength"] - 1) < 1e-6 && (string)re["shadows"] == "Soft" && re["mainLight"]?.Value<bool>() == true,
            "chara light: local Euler (30, 45, 0) (H3), folded colour (0.5, 0.4, 0.3) × 1, strength 1, Soft, the main light", Ctx.Short(cl?.ToString()));
        c.Ok(new { cmd = "undo" }, "undo");
        var re2 = c.Call(new { cmd = "scene.lights" })["studio"]?["charaLight"]?["realized"];
        c.Check(C23V(re2?["color"], 1, 1, 1) && C23E(re2?["localEuler"], 1e-3, 0, 0, 0), "undo: white, (0, 0)", Ctx.Short(re2?.ToString()));
    }

    static void C23Camera(Ctx c)
    {
        var st = c.Ok(new { cmd = "camera.state" }, "camera.state");
        var cam = st["camera"];
        var q = UnityEngine.Quaternion.Euler(15, 180, 0);
        var want = new UnityEngine.Vector3(0, 10, 0) + q * new UnityEngine.Vector3(0, 0, -25);
        c.Check(C23V(cam?["reset"]?["target"], 0, 10, 0) && C23V(cam["reset"]["rot"], 15, 180, 0) && (double)cam["reset"]["dist"] == 25 && (double)cam["reset"]["fov"] == 23
                && (int)cam["slots"] == 10 && W1Dist(W1V3(cam["position"]), want) < 1e-3f && Math.Abs((double)cam["fov"] - 23) < 1e-4,
            "camera.state: reset view (0,10,0)/(15,180,0)/25/23 (E4, H2), 10 slots, the view at the reset view", Ctx.Short(cam?.ToString()));
        c.Check((double)cam["far"] == 15000 && cam["cullingMask"]?.Value<int>() == (HS2Next.ViewSetup.Hs2MainCullingMask | 1 << HS2Next.ViewSetup.HelperLayer)
                && (string)cam["clearFlags"] == "SolidColor" && cam["hdr"]?.Value<bool>() == true && (string)cam["aa"] == "SubpixelMorphologicalAntiAliasing" && cam["postProcessing"]?.Value<bool>() == true,
            "view camera: far 15000 (H10), HS2's culling mask + the helper layer (E10), SolidColor, HDR, SMAA, post-processing", Ctx.Short(cam?.ToString()));

        var slots = c.Ok(new { cmd = "camera.slots" }, "camera.slots");
        c.Check(slots["slots"] is JArray sa && sa.Count == 10 && sa.All(x => x.Type == JTokenType.Null) && C23V(slots["reset"]?["rot"], 15, 180, 0),
            "camera.slots of a new scene: 10 × null, reset view given", Ctx.Short(c.LastText));
        c.Fails(new { cmd = "camera.slot", index = 10, op = "save" }, "bad_args", "camera.slot index 10 → bad_args");
        c.Fails(new { cmd = "camera.slot", index = 1, op = "keep" }, "bad_args", "camera.slot op keep → bad_args");
        int n0 = W1UndoCount(c);
        var sv = c.Ok(new { cmd = "camera.slot", index = 3, op = "save" }, "camera.slot save 3 (current null → the reset view)");
        c.Check(W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Camera slot" && C23V(sv["camera"]?["rot"], 15, 180, 0), "save: one entry \"Camera slot\", the reset view", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "camera.set", target = new[] { 1, 11, 2 }, rot = new[] { 5, 170, 0 }, dist = 20, fov = 30 }, "camera.set");
        c.Ok(new { cmd = "camera.slot", index = 4, op = "save" }, "camera.slot save 4");
        var s4 = c.Call(new { cmd = "camera.slots" })["slots"][4];
        c.Check(C23V(s4?["pivot"], 1, 11, 2) && C23V(s4?["rot"], 5, 170, 0) && (double)s4["distance"] == 20 && (double)s4["fov"] == 30, "slot 4 = the current view", Ctx.Short(s4?.ToString()));
        int n1 = W1UndoCount(c);
        c.Ok(new { cmd = "camera.slot", index = 3, op = "load" }, "camera.slot load 3");
        var g3 = c.Call(new { cmd = "camera.get" })["camera"];
        c.Check(C23V(g3?["rot"], 15, 180, 0) && (double)g3["dist"] == 25 && W1UndoCount(c) == n1, "load 3: the reset view, not recorded", Ctx.Short(g3?.ToString()));
        c.Ok(new { cmd = "camera.slot", index = 4, op = "load" }, "camera.slot load 4");
        c.Check(C23V(c.Call(new { cmd = "camera.get" })["camera"]?["target"], 1, 11, 2), "load 4: the saved view");
        c.Ok(new { cmd = "camera.slot", index = 7, op = "load" }, "camera.slot load 7 (empty)");
        c.Check(C23V(c.Call(new { cmd = "camera.get" })["camera"]?["rot"], 15, 180, 0) && W1UndoCount(c) == n1, "an empty slot loads the reset view (Q-i)");
        c.Ok(new { cmd = "undo" }, "undo the slot 4 save");
        c.Check(c.Call(new { cmd = "camera.slots" })["slots"][4]?.Type == JTokenType.Null, "undo empties slot 4 again");

        // ---------------- camera objects
        int n2 = W1UndoCount(c);
        var add = c.Ok(new { cmd = "camera.add", pos = new[] { 3, 12, 20 }, rot = new[] { 0, 200, 0 }, name = "cam A", active = true }, "camera.add {pos, rot, name, active}");
        string k = (string)add["key"];
        var cs = c.Call(new { cmd = "camera.state" })["camera"];
        c.Check(k != null && W1UndoCount(c) == n2 + 1 && W1TopLabel(c) == "Add camera" && (string)cs["activeCamera"] == k && W1Dist(W1V3(cs["position"]), new UnityEngine.Vector3(3, 12, 20)) < 1e-3f
                && Math.Abs((double)cs["fov"] - 23) < 1e-4,
            "camera.add: one entry, active, the view at its pose, FOV unchanged (H14)", Ctx.Short(cs?.ToString()));
        var add2 = c.Ok(new { cmd = "camera.add" }, "camera.add (second, inactive)");
        string k2 = (string)add2["key"];
        c.Ok(new { cmd = "camera.activate", key = k2 }, "camera.activate the second");
        c.Check((string)c.Call(new { cmd = "camera.state" })["camera"]["activeCamera"] == k2 && W1TopLabel(c) == "Active camera", "single active: the second", W1TopLabel(c));
        c.Ok(new { cmd = "camera.activate", key = (string)null }, "camera.activate null");
        c.Check(c.Call(new { cmd = "camera.state" })["camera"]["activeCamera"]?.Type == JTokenType.Null, "released");
        c.Fails(new { cmd = "camera.activate" }, "bad_args", "camera.activate without key → bad_args");
        c.Ok(new { cmd = "undo" }, "undo the release");
        c.Check((string)c.Call(new { cmd = "camera.state" })["camera"]["activeCamera"] == k2, "undo: the second active again");
        c.Ok(new { cmd = "delete", keys = new[] { k2 } }, "delete the active camera");
        c.Check(c.Call(new { cmd = "camera.state" })["camera"]["activeCamera"]?.Type == JTokenType.Null, "deleting the active camera releases the view");
        c.Ok(new { cmd = "delete", keys = new[] { k } }, "delete the first camera");
    }

    static void C23Sound(Ctx c)
    {
        var ls = c.Ok(new { cmd = "sound.list" }, "sound.list");
        c.Check(ls["bgm"] is JArray b && b.Count >= 21 && b.All(x => x["ref"] != null && x["source"] != null) && ls["audio"] is JArray,
            "sound.list: BGM rows (>= 21) with ref / source and the audio list", "bgm " + (ls["bgm"] as JArray)?.Count + ", audio " + (ls["audio"] as JArray)?.Count);
        c.Info($"sound.list: {(ls["bgm"] as JArray)?.Count} BGM rows (HS2 lists 80 on this install, H11), {(ls["audio"] as JArray)?.Count} audio files");
        c.Check(c.Ok(new { cmd = "sound.list", kind = "bgm" }, "sound.list bgm")["audio"] == null, "kind bgm: no audio list");
        c.Fails(new { cmd = "sound.list", kind = "x" }, "bad_args", "sound.list kind x → bad_args");
        var st0 = c.Ok(new { cmd = "sound.state" }, "sound.state");
        c.Check((string)st0["bgm"]?["ref"] == "illusion.hs2#0" && st0["bgm"]["play"]?.Value<bool>() == false && (string)st0["bgm"]["repeat"] == "all" && st0["userAudio"]?["file"]?.Type == JTokenType.Null
                && st0["playing"]?.Value<bool>() == false,
            "sound.state of a new scene: BGM 0, not playing, repeat all, no file", Ctx.Short(c.LastText));
        c.Fails(new { cmd = "sound.set", set = new Dictionary<string, object> { ["bgm.play"] = true, ["userAudio.play"] = true } }, "bad_args", "sound.set both play flags → bad_args");
        c.Fails(new { cmd = "sound.set", set = new Dictionary<string, object> { ["bgm.repeat"] = "x" } }, "bad_args", "sound.set repeat x → bad_args");
        c.Fails(new { cmd = "sound.set", set = new Dictionary<string, object> { ["volume"] = 1 } }, "bad_args", "sound.set volume → bad_args");
        int n0 = W1UndoCount(c);
        c.Ok(new { cmd = "sound.set", set = new Dictionary<string, object> { ["userAudio.file"] = new { user = "audio/none.wav" }, ["userAudio.play"] = true } }, "sound.set user audio play");
        var st1 = c.Call(new { cmd = "sound.state" });
        c.Check(st1["userAudio"]["play"].Value<bool>() && !st1["bgm"]["play"].Value<bool>() && st1["userAudio"]["exists"]?.Value<bool>() == false && W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Sound",
            "user audio play (file missing: exists false), one entry \"Sound\"", Ctx.Short(st1.ToString()));
        c.Ok(new { cmd = "sound.set", set = new Dictionary<string, object> { ["bgm.play"] = true, ["bgm.ref"] = "illusion.hs2#1" } }, "sound.set bgm play");
        var st2 = c.Call(new { cmd = "sound.state" });
        c.Check(st2["bgm"]["play"].Value<bool>() && !st2["userAudio"]["play"].Value<bool>() && st2["bgm"]["resolved"]?.Value<bool>() == true && (string)st2["channel"] == "bgm" && W1UndoCount(c) == n0 + 2,
            "the channel rule: bgm.play clears userAudio.play (one entry), BGM 1 resolved", Ctx.Short(st2.ToString()));
        c.Ok(new { cmd = "undo" }, "undo");
        var st3 = c.Call(new { cmd = "sound.state" });
        c.Check(st3["userAudio"]["play"].Value<bool>() && !st3["bgm"]["play"].Value<bool>() && (string)st3["bgm"]["ref"] == "illusion.hs2#0", "undo restores both flags and the row");
        c.Ok(new { cmd = "undo" }, "undo");
    }

    static void C23Options(Ctx c)
    {
        var o = c.Ok(new { cmd = "app.options" }, "app.options");
        c.Check((string)o["render"]?["aa"] == "smaa" && (string)o["render"]["grading"] == "hs2" && (string)o["render"]["ambient"] == "hs2" && (string)o["render"]["lightFalloff"] == "hs2"
                && (double)o["render"]["shadowDistance"] == 100 && C23V(o["render"]["backgroundColor"], 0, 0, 0, 1),
            "app.options render defaults (smaa, hs2, hs2, hs2, 100, black)", Ctx.Short(o["render"]?.ToString()));
        c.Fails(new { cmd = "app.options", render = new { aa = "msaa8" } }, "bad_args", "render.aa msaa8 → bad_args");
        c.Fails(new { cmd = "app.options", render = new { shadowDistance = 5 } }, "bad_args", "render.shadowDistance 5 → bad_args");
        c.Fails(new { cmd = "app.options", render = new { aa = "taa", foo = 1 } }, "bad_args", "render with an unknown member → bad_args …");
        c.Check((string)c.Call(new { cmd = "app.options" })["render"]["aa"] == "smaa", "… nothing applied");
        var t = c.Ok(new { cmd = "app.options", render = new { aa = "taa", shadowDistance = 200 } }, "app.options render {aa taa, shadowDistance 200}");
        c.Check((string)t["render"]?["aa"] == "taa" && (double)t["render"]["shadowDistance"] == 200 && t["render"]["taaExperimental"]?.Value<bool>() == true, "the reply shows the new values");
        c.Ok(new { cmd = "app.options", render = new { aa = "smaa", shadowDistance = 100 } }, "back to the defaults");
    }
}
