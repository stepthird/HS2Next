// C20 of BridgeCoreTest: the Phase 3.1 item commands, further methods of the
// partial BridgeCoreTest (-only C20). Package A's part here: item.state, item.set, item.materials, obj.visible and
// app.options items.gimmickIcons — argument errors, one history entry per call, undo / redo restore scene.doc and the
// dumped material values. Package B's part (item.anime, item.motion, fk.* and bones.dump on items) is C21 in
// Editor/BridgeCoreTest_ItemMotion.cs.
// Targets: vanilla 1854 (float mat: colours 1-3, patterns 1-3), 1398 (FK), 975 (panel); ordinary props only.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.App;
using Newtonsoft.Json.Linq;

public static partial class BridgeCoreTest
{
    static void C20(Ctx c)
    {
        c.Host();
        C20Appearance(c);
    }

    static string C20Add(Ctx c, int no)
    {
        var r = c.Ok(new { cmd = "item.add", no }, "item.add " + no);
        return (string)r["added"]?[0]?["key"];
    }

    /// <summary>The first slot of item.materials whose flags contain <paramref name="letter"/> and whose props have <paramref name="prop"/>.</summary>
    static JToken C20Prop(Ctx c, string key, char letter, string prop)
    {
        var r = c.Call(new { cmd = "item.materials", key });
        foreach (var rd in (JArray)r["renderers"] ?? new JArray())
            foreach (var s in (JArray)rd["slots"])
            {
                var f = (string)s["flags"];
                if (f != null && f.IndexOf(letter) >= 0 && s["props"]?[prop] != null) return s["props"][prop];
            }
        return null;
    }

    static bool C20Col(JToken t, params double[] v) =>
        t is JArray a && a.Count == v.Length && Enumerable.Range(0, v.Length).All(i => Math.Abs((double)a[i] - v[i]) <= 1e-6);

    static void C20Appearance(Ctx c)
    {
        var item = C20Add(c, 1854);
        var item2 = C20Add(c, 1854);
        if (item == null || item2 == null) return;

        // ---------------- item.state
        c.Fails(new { cmd = "item.state" }, "bad_args", "item.state without key: bad_args");
        var st = c.Ok(new { cmd = "item.state", key = item }, "item.state");
        c.Check((string)st["ref"] == "illusion.hs2#1854" && (int?)st["row"]?["no"] == 1854 && st["row"]?["fallback"]?.Value<bool>() == false, "item.state: ref and row", Ctx.Short(st["row"]?.ToString()));
        c.Check(st["state"]?["colors"] is JArray cols && cols.Count == 4 && st["state"]["options"] is JArray, "item.state: state = the item section (4 colours, options)");
        var feat = st["features"];
        c.Check(feat?["color"] is JArray fc && fc.All(x => (bool)x) && feat["pattern"] is JArray fp && fp.All(x => (bool)x) && (bool?)feat["lightCancel"] == false,
            "item.state: features (colours 1-3, patterns 1-3, no light cancel)", Ctx.Short(feat?.ToString()));
        c.Check(feat?["defaults"]?["color"] is JArray dc && dc.Count == 3 && feat["defaults"]["uv"] is JArray && feat["defaults"]["glass"] is JArray,
            "item.state: features.defaults (color, uv, glass, ...)");
        c.Check((int?)st["realized"]?["ownedMaterials"] > 0 && (string)st["realized"]?["state"] == "Built", "item.state: realized (owned materials, Built)", Ctx.Short(st["realized"]?.ToString()));

        // ---------------- item.set
        string doc0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        c.Fails(new { cmd = "item.set", key = item, set = new Dictionary<string, object> { ["colors[0].main"] = new[] { 1, 0, 0, 1 }, ["nosuch"] = 1 } }, "bad_args", "item.set with an unknown path: bad_args");
        c.Check((string)c.Call(new { cmd = "item.set", key = item, set = new Dictionary<string, object> { ["alpha"] = "x" } })["detail"]?["arg"] == "set.alpha", "item.set with a wrong value type names the path (set.alpha)", Ctx.Short(c.LastText));
        c.Fails(new { cmd = "item.set", key = item }, "bad_args", "item.set without set: bad_args");
        c.Fails(new { cmd = "item.set", key = item, keys = new[] { item2 }, set = new { alpha = 0.5 } }, "bad_args", "item.set with key and keys: bad_args");
        c.Check(W1DocSha(c) == doc0 && W1UndoCount(c) == n0, "failed item.set calls change nothing (all-or-nothing)");

        var setReq = new { cmd = "item.set", key = item, set = new Dictionary<string, object> { ["colors[0].main"] = new[] { 1, 0, 0, 1 }, ["alpha"] = 0.5, ["colors[1].pattern.uv"] = new[] { 2, 3, 0.25, -0.5 } } };
        var r1 = W1History1(c, setReq, "Item colour", "item.set colours / alpha / uv");
        var changed = (r1["changed"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
        c.Check(changed.Contains("item.colors[0].main") && changed.Contains("item.alpha") && changed.Contains("item.colors[1].pattern.uv"), "item.set: changed lists the canonical paths", string.Join(", ", changed));
        c.Check(C20Col(C20Prop(c, item, '1', "_Color"), 1, 0, 0, 1), "item.materials: _Color of a colour-1 slot = [1,0,0,1]", Ctx.Short(C20Prop(c, item, '1', "_Color")?.ToString()));
        c.Check(C20Col(C20Prop(c, item, 'b', "_patternuv2"), 2, 3, 0.25, -0.5), "item.materials: _patternuv2 of a pattern-2 slot = [2,3,0.25,-0.5]");
        var before = C20Prop(c, item2, '1', "_Color");
        c.Check(!C20Col(before, 1, 0, 0, 1), "the other instance keeps its colour (owned copies)");
        c.Ok(new { cmd = "undo" }, "undo the colour set");
        c.Check(!C20Col(C20Prop(c, item, '1', "_Color"), 1, 0, 0, 1), "undo restores the material value");
        c.Ok(new { cmd = "redo" }, "redo the colour set");
        c.Check(C20Col(C20Prop(c, item, '1', "_Color"), 1, 0, 0, 1), "redo applies the material value again");
        var r2 = c.Ok(setReq, "item.set again with the same values");
        c.Check((r2["changed"] as JArray)?.Count == 0 && (r2["noop"] as JArray)?.Count == 3, "the same values: noop lists the paths, nothing changed", Ctx.Short(c.LastText));

        int n1 = W1UndoCount(c);
        var r3 = c.Ok(new { cmd = "item.set", keys = new[] { item, item2 }, set = new Dictionary<string, object> { ["colors[2].main"] = new[] { 0, 1, 0, 1 } } }, "item.set on two keys");
        c.Check(W1UndoCount(c) == n1 + 1 && r3["keys"]?[item] != null && r3["keys"]?[item2] != null, "two keys: one history entry, per-key results", Ctx.Short(c.LastText));
        c.Check(C20Col(C20Prop(c, item2, '3', "_Color3"), 0, 1, 0, 1) && C20Col(C20Prop(c, item, '3', "_Color3"), 0, 1, 0, 1), "two keys: both items' _Color3 = [0,1,0,1]");
        var lab = c.Ok(new { cmd = "item.set", key = item, set = new Dictionary<string, object> { ["emission.power"] = 3.5 }, label = "My label" }, "item.set with a label");
        c.Check(W1TopLabel(c) == "My label", "item.set: the label names the history entry", W1TopLabel(c));
        c.Check((double?)c.Call(new { cmd = "item.state", key = item })["state"]?["emission"]?["power"] == 3.5, "emission.power stored");

        // panel and FK capture through item.set
        var panel = C20Add(c, 975);
        if (panel != null)
        {
            var dir = System.IO.Path.Combine(CardResolveTest.GameRoot, "DefaultData", "bg");
            var bg = System.IO.Directory.GetFiles(dir, "*.png").OrderBy(x => x, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (bg != null)
            {
                W1History1(c, new { cmd = "item.set", key = panel, set = new Dictionary<string, object> { ["panel"] = new { hs2 = "DefaultData/bg/" + System.IO.Path.GetFileName(bg) } } }, "Item panel", "item.set panel");
                var m = c.Call(new { cmd = "item.materials", key = panel });
                var tex = m["renderers"]?[0]?["slots"]?[0]?["textureSize"]?["_MainTex"];
                c.Check(tex is JArray ts && (int)ts[0] > 0, "item.materials: the panel's _MainTex has a size", Ctx.Short(m.ToString()));
            }
        }
        var fk = C20Add(c, 1398);
        if (fk != null)
        {
            var fs = c.Call(new { cmd = "item.state", key = fk });
            var found = (fs["realized"]?["fkBones"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();
            c.Check(found.Count > 0 && (fs["state"]?["fk"]?["bones"] as JObject)?.Count == found.Count, "1398: new add captures every found FK bone", found.Count + " found");
            c.Ok(new { cmd = "item.set", key = fk, set = new Dictionary<string, object> { ["fk.bones[\"" + found[0] + "\"]"] = new[] { 10, 20, 30 } } }, "item.set fk.bones[\"x\"]");
            var e = c.Call(new { cmd = "item.state", key = fk })["state"]?["fk"]?["bones"]?[found[0]];
            c.Check(C20Col(e, 10, 20, 30), "fk.bones[\"x\"] stored", Ctx.Short(e?.ToString()));
            // a stored entry removed, then FK enabled: the capture rule writes the missing entry first (one entry "Item FK")
            var bones = (JObject)c.Call(new { cmd = "item.state", key = fk })["state"]["fk"]["bones"];
            W1History1(c, new Dictionary<string, object> { ["cmd"] = "item.set", ["key"] = fk, ["set"] = new Dictionary<string, object> { ["fk.enabled"] = true } }, "Item FK", "item.set fk.enabled");
            var after = c.Call(new { cmd = "item.state", key = fk })["state"]["fk"];
            c.Check((bool)after["enabled"] && ((JObject)after["bones"]).Count == bones.Count, "fk.enabled true: every found bone has an entry", ((JObject)after["bones"]).Count + " / " + bones.Count);
        }

        // ---------------- item.materials
        c.Fails(new { cmd = "item.materials" }, "bad_args", "item.materials without key: bad_args");
        var mm = c.Ok(new { cmd = "item.materials", key = item, props = new[] { "_MainTex", "_NoSuchProperty" }, slotsOnly = true }, "item.materials slotsOnly with extra props");
        var slots = ((JArray)mm["renderers"]).SelectMany(x => (JArray)x["slots"]).ToList();
        c.Check(slots.Count > 0 && slots.All(s => !string.IsNullOrEmpty((string)s["flags"]) && (string)s["shaderOriginal"] != null && s["keywords"] is JArray && s["props"] is JObject),
            "item.materials: flagged slots with flags, original shader, keywords, props", slots.Count + " slots");
        c.Check(slots.All(s => ((JObject)s["props"]).Property("_NoSuchProperty") != null && s["props"]["_NoSuchProperty"].Type == JTokenType.Null), "item.materials: an undeclared extra prop is null");

        // ---------------- obj.visible
        c.Fails(new { cmd = "obj.visible", key = item }, "bad_args", "obj.visible without visible: bad_args");
        var v = W1History1(c, new { cmd = "obj.visible", key = item, visible = false }, "Visibility", "obj.visible false");
        c.Check(v["visible"]?.Value<bool>() == false && v["effective"]?.Value<bool>() == false, "obj.visible: own and effective flag false", Ctx.Short(c.LastText));
        var hid = c.Call(new { cmd = "item.materials", key = item });
        c.Check(((JArray)hid["renderers"]).All(x => (bool)x["hidden"]), "item.materials: every renderer hidden");
        c.Ok(new { cmd = "obj.visible", key = item, visible = true }, "obj.visible true");
        c.Check(((JArray)c.Call(new { cmd = "item.materials", key = item })["renderers"]).All(x => !(bool)x["hidden"]), "shown again");

        // ---------------- app.options items.gimmickIcons
        var o = c.Ok(new { cmd = "app.options", items = new { gimmickIcons = false } }, "app.options items.gimmickIcons false");
        c.Check(o["items"]?["gimmickIcons"]?.Value<bool>() == false && !HS2Next.Scenes.Runtime.ItemIcons.Visible, "app.options: the switch is off");
        c.Fails(new { cmd = "app.options", items = new { nosuch = true } }, "bad_args", "app.options items with an unknown member: bad_args");
        c.Ok(new { cmd = "app.options", items = new { gimmickIcons = true } }, "app.options items.gimmickIcons true");
    }
}
