// C35 of BridgeCoreTest: the bridge of the accessory pages (package E of Phase 4.5), further methods of the partial
// BridgeCoreTest (-only C35), edit mode, headless: registration (CoreCommands.Phase45Names, help of the new and extended
// commands, the chara edit group); chara.edit.accessories on BR-Chan and HERO107 (slots = the build slots, the HS2 side's
// member order and meaning, then HS2Next's members; = AccessoryReport and the slot objects; = the HS2 record of BR-Chan's five
// card slots in Cache/compare/maker/accessories when present; the slot / accessory filters; an empty slot; errors; read-only);
// chara.edit.accessory to a build slot (add with slot, occupied / slot + index / negative errors, a MoreAccessories slot, the
// reply's slot), moveSlot, copy to a free / an occupied slot, «複製», the transform-set copy with its mirror, the parent mirror;
// chara.edit.accessoryDefaults («初期カラー» = the reply's colourDefaults, unsupported for a hair-type row);
// chara.edit.accessoryHairColour (the four parts' colours, the draw-type switch re-shading a hair-type slot);
// chara.accessories by build slot (an entry without a preferred slot); chara.edit.set refusing importSlot and an unknown
// attach point; chara.edit.reset of transform components; chara.edit.materials part accessories (+ slot); chara.edit.digest
// parts accessories (which sections an edit changes); chara.edit.info accessories; a head-weighted row on HERO107; a batch is
// one entry. C1–C34 unchanged. Numbers only; the SFW targets and vanilla items only (nothing captured).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HS2Next.Chara.Edit;
using HS2Next.Content;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class BridgeCoreTest
{
    /// <summary>The HS2 side's chara.accessoryInfo slot members in their order (H's bridge), then HS2Next's additions.</summary>
    static readonly string[] C35Hs2Members =
    {
        "slot", "extra", "type", "id", "rawId", "parentKey", "addMove", "colorInfo", "hideCategory", "hideTiming", "noShake", "partsOfHead",
        "show", "loaded", "object", "active", "activeInHierarchy", "row", "facts", "particleDummy", "defaults", "anchor", "world", "moves",
        "shaderType", "materials", "chains",
    };
    static readonly string[] C35Additions =
    {
        "accessory", "category", "item", "parent", "importSlot", "notLoaded", "showFlag", "anchorUsed", "hairShaderType", "colourDefaults",
        "rules", "rowRef", "served",
    };

    /// <summary>Vanilla rows of the checks: 353#0 «スクエア» (alpha renderers), 359#0 «リュック» (three areas), 351#40 «お団子髪»
    /// (hair-type), 351#202 «袋頭巾» (Parent "null"), 352#0 «スター».</summary>
    const string C35Square = "illusion.hs2#0", C35HairBun = "illusion.hs2#40", C35Bag = "illusion.hs2#202", C35Star = "illusion.hs2#0";

    static void C35(Ctx c)
    {
        var app = c.Host();
        C35Registration(c, app);
        c.Ok(new { cmd = "scene.new" }, "scene.new");
        string f = C30Load(c, c.FemaleRel, "female", "chara.load BR-Chan");
        string m = C30Load(c, c.MaleRel, "male", "chara.load HERO107");
        if (f == null || m == null) return;
        string item = W1AddItem(c);
        c.Frames(2);
        C35Read(c, f, item);
        C35Hs2Record(c, f);
        C35Slots(c, f);
        C35Defaults(c, f);
        C35HairColour(c, f);
        C35Visibility(c, f);
        C35Edits(c, f);
        C35Materials(c, f);
        C35Digest(c, f);
        C35Info(c, f);
        C35Male(c, m);
        C35Batch(c, f);
    }

    // ---------------------------------------------------------------- helpers

    static string C35J(object o) => o == null ? "null" : o is JToken t ? C34J(t) : Newtonsoft.Json.JsonConvert.SerializeObject(o);
    static List<JObject> C35Slots(JObject r) => C34Objs(r?["slots"]);
    static JObject C35Slot(Ctx c, string key, int slot) => C35Slots(c.Call(new { cmd = "chara.edit.accessories", key, slot, served = false })).FirstOrDefault();
    static string C35Acc(string id, string member) => "coord.accessories[\"" + id + "\"]." + member;
    static int C35SlotOfReply(JObject r) => (int?)r?["slot"] ?? -99;

    /// <summary>The accessory ids of the stored coordinate by build slot (StudioOps.AccessorySlotsOf).</summary>
    static Dictionary<int, string> C35Table(Ctx c, string key)
    {
        var n = c.App.Session.Model.Find(key);
        var t = StudioOps.AccessorySlotsOf(n);
        return t != null ? new Dictionary<int, string>(t.IdBySlot) : new Dictionary<int, string>();
    }

    // ---------------------------------------------------------------- registration and help

    static void C35Registration(Ctx c, HS2Next.App.AppCore app)
    {
        var reg = app.BridgeServices.Commands;
        var names = HS2Next.App.Commands.CoreCommands.Phase45Names;
        c.Check(names.SequenceEqual(HS2Next.App.Commands.CharaEditCommands.Phase45Names)
                && names.SequenceEqual(new[] { "chara.edit.accessories", "chara.edit.accessoryDefaults", "chara.edit.accessoryHairColour" })
                && names.All(n => reg.Find(n) != null), "Phase45Names = chara.edit.accessories, .accessoryDefaults, .accessoryHairColour, all registered",
            string.Join(", ", names.Where(n => reg.Find(n) == null)));
        c.Check(HS2Next.App.Commands.CoreCommands.AllNames.Count(n => names.Contains(n)) == 3, "CoreCommands.AllNames lists them once");
        foreach (var n in names)
        {
            var h = c.Ok(new { cmd = "help", name = n }, "help " + n);
            c.Check(h.ToString().Contains(n) && reg.Find(n)?.Group == "chara edit", "help describes " + n + " (group chara edit)", Ctx.Short(c.LastText));
        }
        foreach (var (cmd, word) in new[]
        {
            ("chara.edit.accessory", "moveSlot"), ("chara.edit.accessory", "toSlot"), ("chara.edit.materials", "accessories"), ("chara.edit.digest", "accessories"),
            ("chara.edit.info", "accessorySlotsSwapped"), ("chara.edit.reset", "moves[c].position#i"), ("chara.accessories", "build slot"), ("ui.action", "accessory build slot"),
        })
        {
            var h = c.Ok(new { cmd = "help", name = cmd }, "help " + cmd);
            c.Check(h.ToString().Contains(word), cmd + " documents " + word, Ctx.Short(c.LastText));
        }
        var edit = HS2Next.App.Commands.CharaEditCommands.Names.Concat(HS2Next.App.Commands.CharaEditCommands.Phase41Names).Concat(HS2Next.App.Commands.CharaEditCommands.Phase42Names)
            .Concat(HS2Next.App.Commands.CharaEditCommands.Phase43Names).Concat(HS2Next.App.Commands.CharaEditCommands.Phase44Names)
            .Concat(HS2Next.App.Commands.CharaEditCommands.Phase45Names).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var he = c.Ok(new { cmd = "help", group = "chara edit" }, "help {group: \"chara edit\"}");
        var hen = (he["commands"] as JArray)?.Select(x => (string)x["name"]).OrderBy(x => x, StringComparer.Ordinal).ToList() ?? new List<string>();
        // later phases add to the group (4.6: C36 pins the whole list): here the 25 commands of 4.0–4.5, all listed, and nothing
        // else besides the later phases' lists
        var later = new HashSet<string>(HS2Next.App.Commands.CharaEditCommands.Phase46Names);
        var hen45 = hen.Where(x => !later.Contains(x)).ToList();
        c.Check(hen45.SequenceEqual(edit) && edit.Count == 25, "help {group: \"chara edit\"} lists the 25 chara edit commands of 4.0–4.5 (and only later phases' others)", hen.Count + ": " + string.Join(", ", hen45.Except(edit).Concat(edit.Except(hen45))));
        c.Check(HS2Next.App.Commands.CoreCommands.RegistrationErrors.Count == 0, "no registration error", string.Join("; ", HS2Next.App.Commands.CoreCommands.RegistrationErrors));
    }

    // ---------------------------------------------------------------- chara.edit.accessories (read)

    static void C35Read(Ctx c, string f, string item)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var r = c.Ok(new { cmd = "chara.edit.accessories", key = f }, "chara.edit.accessories BR-Chan");
        var slots = C35Slots(r);
        var table = C35Table(c, f);
        c.Check(slots.Count == table.Count && slots.Select(x => (int)x["slot"]).SequenceEqual(table.Keys.OrderBy(k => k)) && slots.All(x => (string)x["accessory"] == table[(int)x["slot"]]),
            "slots = the stored coordinate's build slots in order, each with its accessory id (" + slots.Count + ")", string.Join(", ", slots.Select(x => x["slot"] + ":" + x["accessory"])));
        // the HS2 side's members in order, then HS2Next's
        var first = slots.FirstOrDefault(x => (bool?)x["loaded"] == true && (bool?)x["facts"]?["hasCmpAccessory"] == true);
        var props = first?.Properties().Select(p => p.Name).ToList() ?? new List<string>();
        c.Check(props.SequenceEqual(C35Hs2Members.Concat(C35Additions)), "slot members: the HS2 side's chara.accessoryInfo order, then HS2Next's", string.Join(", ", props));
        var factNames = (first?["facts"] as JObject)?.Properties().Select(p => p.Name).ToList() ?? new List<string>();
        c.Check(factNames.SequenceEqual(new[] { "hasCmpAccessory", "parentColumn", "headWeighted", "fixed", "cmpComponents", "cmpOnRoot", "cmpUsed", "particleSystems", "dynamicBones",
                "colliders", "typeHair", "useColor", "useGloss", "useMetallic", "alphaRenderers", "alphaLength", "normalRenderers", "normalLength", "move1", "move2", "moveNames" }),
            "facts in the HS2 side's order", string.Join(", ", factNames));
        var envelope = new HashSet<string> { "ok", "cmd", "id", "protocol", "warnings" };
        c.Check(r.Properties().Select(p => p.Name).Where(x => !envelope.Contains(x)).SequenceEqual(new[] { "slots", "slotCount", "shaderType", "hairActive", "hairChildren", "male", "extraSlotsOn", "freeSlots" })
                && (r["hairActive"] as JArray)?.Count == 4 && (r["hairChildren"] as JArray)?.Count == 4 && (bool?)r["male"] == false && r["freeSlots"] is JArray,
            "top level: slots, slotCount, shaderType, hairActive [4], hairChildren [4], then male, extraSlotsOn, freeSlots", Ctx.Short(C35J(new JObject { ["hairActive"] = r["hairActive"], ["hairChildren"] = r["hairChildren"], ["slotCount"] = r["slotCount"] })));
        // = the Unity-side report and the slot objects
        var ctx = C34Ctx(c, f);
        bool same = ctx != null;
        string why = "";
        foreach (var x in slots)
        {
            if (!same) break;
            int s = (int)x["slot"];
            var p = AccessoryReport.SlotOf(ctx, s, false, true);
            var go = ctx.AccessoryObject(s);
            var fx = x["facts"] as JObject;
            same &= (int?)x["type"] == p.Category && (string)x["id"] == p.Item && (string)x["accessory"] == p.Id && (string)x["parent"] == p.Parent && (bool?)x["loaded"] == p.Loaded
                    && (string)x["anchorUsed"] == p.AnchorName && (int?)x["hairShaderType"] == p.ShaderType && (string)x["rowRef"] == p.Row && (bool?)x["notLoaded"] == p.NotLoaded
                    && (bool?)fx?["hasCmpAccessory"] == p.Facts.HasCmpAccessory && (string)fx?["parentColumn"] == p.Facts.ParentColumn;
            if (same && p.Facts.HasCmpAccessory)
            {
                same &= (bool?)fx["typeHair"] == p.Facts.TypeHair && fx["useColor"].Select(v => (bool)v).SequenceEqual(p.Facts.UseColor) && (int?)fx["alphaRenderers"] == p.Facts.AlphaRenderers
                        && (bool?)fx["move1"] == p.Facts.Move1 && (bool?)fx["move2"] == p.Facts.Move2 && (int?)fx["dynamicBones"] == p.Facts.DynamicBones;
                for (int k = 0; same && k < 2; k++)
                {
                    var mv = x["moves"]?[k];
                    var pm = p.Moves[k];
                    same &= pm == null ? mv?.Type == JTokenType.Null : C34Near(C34F(mv?["position"]), new[] { pm.Position.x, pm.Position.y, pm.Position.z }, 0)
                                                                         && C34Near(C34F(mv?["scale"]), new[] { pm.Scale.x, pm.Scale.y, pm.Scale.z }, 0);
                }
                int mats = 0;
                foreach (var rd in go.GetComponentsInChildren<Renderer>(true)) if (rd) mats += rd.sharedMaterials.Length;
                same &= C34Objs(x["materials"]).Count == mats && C34Objs(x["chains"]).Count == p.Chains.Count;
                var cd = C34Objs(x["colourDefaults"]);
                for (int k = 0; same && k < 4; k++) same &= (float)cd[k]["gloss"] == p.Defaults[k].Gloss && (float)cd[k]["metallic"] == p.Defaults[k].Metallic;
            }
            if (same && go) same &= (string)x["object"] == go.name && (bool?)x["active"] == go.activeSelf && (string)x["anchor"]?["name"] == (go.transform.parent ? go.transform.parent.name : null);
            if (!same) why = "slot " + s + ": " + Ctx.Short(C35J(x));
        }
        c.Check(same, "every slot = AccessoryReport.SlotOf (type, item, id, parent, loaded, anchor used, hair shader, row, facts, moves, colour defaults, chains) and the slot object (name, activity, parent, every material slot)", why);
        var stored = C30Value(c, f, "coord.accessories") as JArray;
        var e0 = stored?.OfType<JObject>().FirstOrDefault(x => (string)x["id"] == (string)first?["accessory"]);
        c.Check(e0 != null && C34Near(C34F(first["addMove"]?[0]?["position"]), C34F(e0["moves"]?[0]?["position"]), 0) && C34Near(C34F(first["colorInfo"]?[0]?["color"]), C34F(e0["colors"]?[0]?["color"]), 0)
                && (float?)first["colorInfo"]?[0]?["glossPower"] == (float?)e0["colors"]?[0]?["gloss"] && (bool?)first["noShake"] == (bool?)e0["noShake"] && (int?)first["importSlot"] == (int?)e0["importSlot"],
            "addMove / colorInfo / noShake / importSlot = the stored entry (HS2's names)", Ctx.Short(C35J(e0)));
        c.Check(slots.All(x => (bool?)x["show"] == true), "show = the scene's visibility (all shown after the load)", string.Join(", ", slots.Select(x => (string)x["show"])));
        c.Check(slots.All(x => (string)x["served"]?["MainData"]?["kind"] != null), "served.MainData.kind on every slot (served by default)");
        // filters
        if (first != null)
        {
            int s0 = (int)first["slot"];
            var one = c.Ok(new { cmd = "chara.edit.accessories", key = f, slot = s0 }, "slot " + s0);
            c.Check(C35Slots(one).Count == 1 && JToken.DeepEquals(C35Slots(one)[0], first), "slot " + s0 + ": that slot only, equal to the all-slots reply's");
            var byId = c.Ok(new { cmd = "chara.edit.accessories", key = f, accessory = (string)first["accessory"] }, "accessory " + first["accessory"]);
            c.Check(C35Slots(byId).Count == 1 && JToken.DeepEquals(C35Slots(byId)[0], first), "accessory id: that slot only, equal");
            var ns = c.Ok(new { cmd = "chara.edit.accessories", key = f, slot = s0, served = false }, "served false");
            c.Check(C35Slots(ns).FirstOrDefault()?["served"] == null, "served false: no served member");
        }
        int free = Enumerable.Range(0, 20).FirstOrDefault(s => !table.ContainsKey(s));
        var empty = C35Slot(c, f, free);
        c.Check(empty != null && (int?)empty["type"] == 350 && (bool?)empty["loaded"] == false && empty["id"]?.Type == JTokenType.Null && empty["accessory"]?.Type == JTokenType.Null
                && (empty["facts"] as JObject)?.Properties().Count() == 4,
            "an empty slot (" + free + "): type 350, loaded false, id and accessory null, facts {hasCmpAccessory, parentColumn, headWeighted, fixed}", Ctx.Short(C35J(empty)));
        c.Check(((JArray)r["freeSlots"]).Select(x => (int)x).Contains(free) && !((JArray)r["freeSlots"]).Select(x => (int)x).Any(table.ContainsKey), "freeSlots: the free slots, no worn one");
        c.Fails(new { cmd = "chara.edit.accessories", key = f, slot = -1 }, "bad_args", "slot -1 → bad_args");
        c.Fails(new { cmd = "chara.edit.accessories", key = f, slot = 1.5 }, "bad_args", "slot 1.5 → bad_args");
        c.Fails(new { cmd = "chara.edit.accessories", key = f, slot = "05" }, "bad_args", "slot \"05\" (text) → bad_args");
        c.Fails(new { cmd = "chara.edit.accessories", key = f, slot = 0, accessory = "x" }, "bad_args", "slot and accessory → bad_args");
        c.Fails(new { cmd = "chara.edit.accessories", key = f, accessory = "nosuchaccessory" }, "not_found", "an unknown accessory → not_found");
        c.Fails(new { cmd = "chara.edit.accessories", key = item }, "wrong_kind", "an item key → wrong_kind");
        c.Fails(new { cmd = "chara.edit.accessories" }, "bad_args", "no key → bad_args");
        c.Check(W1UndoCount(c) == n0 && W1DocSha(c) == sha0, "chara.edit.accessories records and changes nothing");
    }

    // ---------------------------------------------------------------- = the HS2 record of BR-Chan's card slots (H's XC10 fresh case)

    static void C35Hs2Record(Ctx c, string f)
    {
        string file = Path.Combine(HS2Next.Content.ContentSettings.ProjectRoot, "Cache", "compare", "maker", "accessories", "xc10.json");
        if (!File.Exists(file)) { c.Warn("no HS2 record " + file + ": the HS2 comparison of the reply is skipped"); return; }
        JObject info = null;
        try
        {
            foreach (var cs in JArray.Parse(File.ReadAllText(file)).OfType<JObject>())
            {
                string card = (string)cs["card"];
                if (card == null || !card.StartsWith("BR-Chan", StringComparison.Ordinal)) continue;
                foreach (var st in C34Objs(cs["steps"]))
                    if (C34Objs(st["info"]?["slots"]).Count >= 5) { info = (JObject)st["info"]; break; }
                if (info != null) break;
            }
        }
        catch (Exception e) { c.Warn("the HS2 record does not read (" + e.Message + ")"); return; }
        if (info == null) { c.Warn("the HS2 record has no fresh BR-Chan step with five slots"); return; }
        var mine = C35Slots(c.Call(new { cmd = "chara.edit.accessories", key = f, served = false }));
        var hs2 = C35Slots(info);
        c.Check(mine.Select(x => (int)x["slot"]).SequenceEqual(hs2.Select(x => (int)x["slot"])), "the worn slots = HS2's (" + string.Join(", ", hs2.Select(x => x["slot"])) + ")",
            string.Join(", ", mine.Select(x => x["slot"])));
        foreach (var h in hs2)
        {
            var x = mine.FirstOrDefault(y => (int)y["slot"] == (int)h["slot"]);
            string s = "slot " + h["slot"];
            if (x == null) { c.Check(false, s + ": present"); continue; }
            var hp = h.Properties().Select(p => p.Name).ToList();
            c.Check(x.Properties().Select(p => p.Name).Take(hp.Count).SequenceEqual(hp), s + ": the members start with HS2's in its order", string.Join(", ", hp));
            bool stored = (int)x["type"] == (int)h["type"] && (string)x["id"] == (string)h["id"] && (int?)x["rawId"] == (int?)h["rawId"] && (string)x["parentKey"] == (string)h["parentKey"]
                          && (int?)x["hideCategory"] == (int?)h["hideCategory"] && (int?)x["hideTiming"] == (int?)h["hideTiming"] && (bool?)x["noShake"] == (bool?)h["noShake"]
                          && (bool?)x["partsOfHead"] == (bool?)h["partsOfHead"];
            for (int k = 0; stored && k < 2; k++)
                foreach (var v in new[] { "position", "rotation", "scale" }) stored &= C34Near(C34F(x["addMove"]?[k]?[v]), C34F(h["addMove"]?[k]?[v]), 1e-6f);
            for (int k = 0; stored && k < 4; k++)
            {
                stored &= C34Near(C34F(x["colorInfo"]?[k]?["color"]), C34F(h["colorInfo"]?[k]?["color"]), 1e-6f);
                foreach (var v in new[] { "glossPower", "metallicPower", "smoothnessPower" }) stored &= Math.Abs((float)x["colorInfo"][k][v] - (float)h["colorInfo"][k][v]) <= 1e-6f;
            }
            c.Check(stored, s + ": stored part = HS2's (type, id, rawId, parentKey, addMove, colorInfo, hide*, noShake, partsOfHead)", Ctx.Short(C35J(x["colorInfo"])) + " | " + Ctx.Short(C35J(h["colorInfo"])));
            var xr = x["row"] as JObject; var hr = h["row"] as JObject;
            c.Check(xr != null && hr != null && new[] { "category", "id", "ref", "Name", "MainAB", "MainData", "Parent", "TexAB", "TexD", "TexC" }.All(k => JToken.DeepEquals(xr[k], hr[k])),
                s + ": row = HS2's (category, id, ref, Name, MainAB, MainData, Parent, Tex*)", Ctx.Short(C35J(xr)) + " | " + Ctx.Short(C35J(hr)));
            var xf = x["facts"] as JObject; var hf = h["facts"] as JObject;
            var keys = new[] { "hasCmpAccessory", "parentColumn", "headWeighted", "fixed", "cmpOnRoot", "dynamicBones", "typeHair", "useColor", "useGloss", "useMetallic", "alphaRenderers",
                "alphaLength", "normalRenderers", "normalLength", "move1", "move2", "moveNames" };
            var off = keys.Where(k => !JToken.DeepEquals(xf?[k], hf?[k])).ToList();
            c.Check(off.Count == 0 && (xf?["cmpComponents"] as JArray)?.Count == (hf?["cmpComponents"] as JArray)?.Count, s + ": facts = HS2's (incl. the CmpAccessory count, XH9)",
                string.Join(", ", off.Select(k => k + " " + C35J(xf?[k]) + " vs " + C35J(hf?[k]))));
            bool defs = true;
            for (int k = 0; k < 4; k++)
                defs &= C34Near(C34F(x["defaults"]?[k]?["color"]), C34F(h["defaults"]?[k]?["color"]), 1e-6f) && Math.Abs(((float?)x["defaults"]?[k]?["gloss"] ?? 9f) - ((float?)h["defaults"]?[k]?["gloss"] ?? -9f)) <= 1e-6f;
            c.Check(defs, s + ": defaults = HS2's (the prefab's raw values)");
            c.Check((string)x["anchor"]?["name"] == (string)h["anchor"]?["name"] && (bool?)x["loaded"] == (bool?)h["loaded"] && (bool?)x["active"] == (bool?)h["active"],
                s + ": anchor name, loaded, active = HS2's (" + h["anchor"]?["name"] + ")", Ctx.Short(C35J(x["anchor"])));
            bool moves = true;
            for (int k = 0; k < 2; k++)
            {
                if (h["moves"]?[k]?.Type == JTokenType.Null || h["moves"]?[k] == null) { moves &= x["moves"]?[k]?.Type == JTokenType.Null; continue; }
                moves &= (string)x["moves"]?[k]?["name"] == (string)h["moves"][k]["name"];
                foreach (var v in new[] { "position", "scale" }) moves &= C34Near(C34F(x["moves"]?[k]?[v]), C34F(h["moves"][k][v]), 1e-4f);
            }
            c.Check(moves, s + ": the transform sets' names and local position / scale = HS2's", Ctx.Short(C35J(x["moves"])) + " | " + Ctx.Short(C35J(h["moves"])));
            var xm = C34Objs(x["materials"]); var hm = C34Objs(h["materials"]);
            c.Check(xm.Count == hm.Count && xm.Zip(hm, (a, b) => (string)a["renderer"] == (string)b["renderer"] && (string)a["kind"] == (string)b["kind"] && (int)a["slot"] == (int)b["slot"]
                    && (string)a["mainTex"] == (string)b["mainTex"] && (bool?)a["active"] == (bool?)b["active"]).All(v => v),
                s + ": materials = HS2's (renderer, kind, slot, mainTex, active)", Ctx.Short(C35J(xm.Select(a => a["renderer"] + "/" + a["kind"] + "/" + a["mainTex"])) + " | " + C35J(hm.Select(a => a["renderer"] + "/" + a["kind"] + "/" + a["mainTex"]))));
            var xc = C34Objs(x["chains"]); var hc = C34Objs(h["chains"]);
            c.Check(xc.Select(a => (string)a["root"]).SequenceEqual(hc.Select(a => (string)a["root"])), s + ": chains' roots = HS2's (" + hc.Count + ")",
                Ctx.Short(C35J(x["chains"])) + " | " + Ctx.Short(C35J(h["chains"])));
            if (!xc.Select(a => (int)a["colliders"]).SequenceEqual(hc.Select(a => (int)a["colliders"])))
                c.Info(s + ": chain collider counts " + C35J(xc.Select(a => a["colliders"])) + " vs HS2's " + C35J(hc.Select(a => a["colliders"])) + " (Z18: HS2Next's build rule, not HS2's snapshot)");
        }
        var me = c.Call(new { cmd = "chara.edit.accessories", key = f, served = false });
        if (!JToken.DeepEquals(me["hairActive"], info["hairActive"]) || !JToken.DeepEquals(me["hairChildren"], info["hairChildren"]))
            c.Warn("hairActive / hairChildren " + C35J(me["hairActive"]) + " " + C35J(me["hairChildren"]) + " vs HS2's " + C35J(info["hairActive"]) + " " + C35J(info["hairChildren"]));
        else c.Check(true, "hairActive / hairChildren = HS2's");
    }

    // ---------------------------------------------------------------- chara.edit.accessory to build slots

    static void C35Slots(Ctx c, string f)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var table = C35Table(c, f);
        var free = Enumerable.Range(0, 20).Where(s => !table.ContainsKey(s)).ToList();
        if (free.Count < 4) { c.Warn("BR-Chan has fewer than 4 free slots: the slot checks are skipped"); return; }
        int sA = free[1], sB = free[2], sC = free[3];
        // add to a named free slot
        var a = c.Ok(new { cmd = "chara.edit.accessory", key = f, op = "add", category = 353, @ref = C35Square, slot = sA }, "add 353#0 «スクエア» to slot " + sA);
        string id = (string)a["accessory"];
        c.Check(id != null && C35SlotOfReply(a) == sA && W1TopLabel(c) == "Add accessory" && W1UndoCount(c) == n0 + 1, "add: the new id, reply slot " + sA + ", one entry \"Add accessory\"", Ctx.Short(C35J(a)));
        c.Check((int?)C30Value(c, f, C35Acc(id, "importSlot")) == sA, "the slot is the entry's preferred slot (importSlot " + sA + ")");
        var sq = C35Slot(c, f, sA);
        c.Check((string)sq?["accessory"] == id && (int?)sq?["facts"]?["alphaRenderers"] > 0 && (bool?)sq?["facts"]?["move1"] == true,
            "slot " + sA + " holds it: alpha renderers, set 1 (UI8 step 2)", Ctx.Short(C35J(sq?["facts"])));
        c.Check(C34Near(C34F(sq?["colorInfo"]?[3]?["color"]), C34F(sq?["colourDefaults"]?[3]?["color"]), 0) && Math.Abs(((float?)sq?["colorInfo"]?[3]?["color"]?[3] ?? 0) - 0.102f) < 1e-3f,
            "colour 4 = the prefab default (alpha 0.102, XH4)", Ctx.Short(C35J(sq?["colorInfo"]?[3])));
        c.Fails(new { cmd = "chara.edit.accessory", key = f, op = "add", category = 353, @ref = C35Square, slot = sA }, "bad_args", "add to the occupied slot " + sA + " → bad_args");
        c.Check((string)c.Call(new { cmd = "chara.edit.accessory", key = f, op = "add", category = 353, @ref = C35Square, slot = sA })["detail"]?["arg"] == "slot", "… naming the argument slot");
        c.Fails(new { cmd = "chara.edit.accessory", key = f, op = "add", category = 353, @ref = C35Square, slot = sB, index = 0 }, "bad_args", "add with slot and index → bad_args");
        c.Fails(new { cmd = "chara.edit.accessory", key = f, op = "add", category = 353, @ref = C35Square, slot = -2 }, "bad_args", "add to slot -2 → bad_args");
        c.Fails(new { cmd = "chara.edit.accessory", key = f, op = "add", category = 353, @ref = C35Square, parent = "N_nope" }, "bad_args", "add with an unknown attach point → bad_args");
        // a MoreAccessories slot
        var x25 = c.Ok(new { cmd = "chara.edit.accessory", key = f, op = "add", category = 352, @ref = C35Star, slot = 25 }, "add 352#0 to slot 25 (MoreAccessories)");
        var s25 = C35Slot(c, f, 25);
        bool extraOn = (bool?)c.Call(new { cmd = "chara.edit.accessories", key = f, slot = 25, served = false })["extraSlotsOn"] == true;
        c.Check(C35SlotOfReply(x25) == 25 && (bool?)s25?["extra"] == true && (extraOn ? (bool?)s25?["loaded"] == true && !C34Warned(x25, "acc.slotNotLoaded")
                                                                               : (bool?)s25?["notLoaded"] == true && C34Warned(x25, "acc.slotNotLoaded")),
            "slot 25: reply slot 25, extra; MoreAccessories " + (extraOn ? "on: loaded, no warning" : "off: notLoaded with warning acc.slotNotLoaded"), Ctx.Short(C35J(s25)));
        c.Ok(new { cmd = "undo" }, "undo the slot-25 add");
        // moveSlot
        int n1 = W1UndoCount(c);
        var mv = c.Ok(new { cmd = "chara.edit.accessory", key = f, op = "moveSlot", accessory = id, slot = sB }, "moveSlot " + sA + " → " + sB);
        c.Check(C35SlotOfReply(mv) == sB && W1TopLabel(c) == "Move accessory" && W1UndoCount(c) == n1 + 1 && (string)C35Slot(c, f, sB)?["accessory"] == id && (int?)C35Slot(c, f, sA)?["type"] == 350,
            "moveSlot: reply slot " + sB + ", one entry \"Move accessory\", slot " + sB + " holds it, slot " + sA + " empty", Ctx.Short(C35J(mv)));
        c.Check((int?)C30Value(c, f, C35Acc(id, "importSlot")) == sB && C35Table(c, f)[sB] == id, "importSlot " + sB + "; the slot table names it");
        var same = c.Ok(new { cmd = "chara.edit.accessory", key = f, op = "moveSlot", accessory = id, slot = sB }, "moveSlot to its own slot");
        c.Check(W1UndoCount(c) == n1 + 1 && C30Strs(same["changed"]).Count == 0, "its own slot: nothing changed, nothing recorded", Ctx.Short(C35J(same)));
        int worn = table.Keys.First();
        c.Fails(new { cmd = "chara.edit.accessory", key = f, op = "moveSlot", accessory = id, slot = worn }, "bad_args", "moveSlot to the worn slot " + worn + " → bad_args");
        c.Fails(new { cmd = "chara.edit.accessory", key = f, op = "moveSlot", accessory = id }, "bad_args", "moveSlot without slot → bad_args");
        c.Fails(new { cmd = "chara.edit.accessory", key = f, op = "moveSlot", accessory = id, index = 0 }, "bad_args", "moveSlot with index → bad_args");
        c.Fails(new { cmd = "chara.edit.accessory", key = f, op = "moveSlot", accessory = "nosuchid", slot = sC }, "not_found", "moveSlot of an unknown accessory → not_found");
        // copy to a free slot (mirrored parent), to an occupied slot, «複製»
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { [C35Acc(id, "parent")] = "N_Earring_L" } }, "parent N_Earring_L");
        var cp = c.Ok(new { cmd = "chara.edit.accessory", key = f, op = "copy", from = id, toSlot = sC, mirrorParent = true }, "copy " + sB + " → free slot " + sC + " with «左右の親を変更»");
        string id2 = (string)cp["accessory"];
        c.Check(id2 != null && id2 != id && C35SlotOfReply(cp) == sC && W1TopLabel(c) == "Copy accessory" && (string)C30Value(c, f, C35Acc(id2, "parent")) == "N_Earring_R"
                && (int?)C30Value(c, f, C35Acc(id2, "importSlot")) == sC,
            "a new accessory in slot " + sC + " (reply slot, importSlot), parent N_Earring_R, one entry \"Copy accessory\"", Ctx.Short(C35J(cp)));
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { [C35Acc(id, "moves[0].rotation")] = new[] { 0.0, 30.0, 0.0 }, [C35Acc(id, "colors[0].gloss")] = 0.25 } }, "slot " + sB + ": rotation y 30, gloss 0.25");
        var ov = c.Ok(new { cmd = "chara.edit.accessory", key = f, op = "copy", from = id, toSlot = sC }, "copy " + sB + " → the occupied slot " + sC);
        c.Check(ov["accessory"] == null && C35SlotOfReply(ov) == sC && C35Table(c, f)[sC] == id2 && Math.Abs(((float?)C30Value(c, f, C35Acc(id2, "colors[0].gloss")) ?? 0) - 0.25f) < 1e-7f
                && (string)C30Value(c, f, C35Acc(id2, "parent")) == "N_Earring_L",
            "occupied: the target's id kept (" + id2 + "), its members overwritten (gloss 0.25, parent N_Earring_L), reply slot " + sC, Ctx.Short(C35J(ov)));
        var mr = c.Ok(new { cmd = "chara.edit.accessory", key = f, op = "copy", from = id, toSlot = sC, parts = new[] { "move1" }, mirror = "leftRight" }, "copy set 1 " + sB + " → " + sC + " «調整①を左右反転»");
        c.Check(C34Near(C34F(C30Value(c, f, C35Acc(id2, "moves[0].rotation"))), new[] { 0f, 210f, 0f }, 1e-4f) && C35SlotOfReply(mr) == sC, "rotation y 30 → 210 (HS2's mirror), reply slot " + sC,
            C34S(C34F(C30Value(c, f, C35Acc(id2, "moves[0].rotation")))));
        var tableNow = C35Table(c, f);
        int lowest = Enumerable.Range(0, 1000).First(s => !tableNow.ContainsKey(s));
        var dup = c.Ok(new { cmd = "chara.edit.accessory", key = f, op = "copy", from = id }, "copy without a target («複製»)");
        c.Check(dup["accessory"] != null && C35SlotOfReply(dup) == lowest, "«複製»: a new accessory in the lowest free slot " + lowest, Ctx.Short(C35J(dup)));
        c.Fails(new { cmd = "chara.edit.accessory", key = f, op = "copy", from = id, to = id2, toSlot = sC }, "bad_args", "copy with to and toSlot → bad_args");
        var tableDup = C35Table(c, f);
        int emptyS = Enumerable.Range(0, 1000).First(s => !tableDup.ContainsKey(s));
        c.Fails(new { cmd = "chara.edit.accessory", key = f, op = "copy", from = id, toSlot = emptyS, parts = new[] { "move1" } }, "bad_args",
            "a set copy to the empty slot " + emptyS + " → bad_args (an occupied target needed)");
        var mvl = c.Ok(new { cmd = "chara.edit.accessory", key = f, op = "move", accessory = id2, index = 0 }, "move (list index 0)");
        c.Check(C35SlotOfReply(mvl) == sC && W1TopLabel(c) == "Reorder accessories", "move: entry \"Reorder accessories\", the build slot kept (" + sC + ": a preferred slot)", Ctx.Short(C35J(mvl)));
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (slot block)");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
    }

    // ---------------------------------------------------------------- chara.edit.accessoryDefaults

    static void C35Defaults(Ctx c, string f)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var a = c.Ok(new { cmd = "chara.edit.accessory", key = f, op = "add", category = 359, @ref = "illusion.hs2#0" }, "add 359#0 «リュック» (three areas)");
        string id = (string)a["accessory"];
        int slot = C35SlotOfReply(a);
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object>
        {
            [C35Acc(id, "colors[0].gloss")] = 1.5, [C35Acc(id, "colors[1].color")] = new[] { 0.1, 0.2, 0.3, 1.0 }, [C35Acc(id, "colors[2].metallic")] = -0.5,
        } }, "gloss 1.5, colour 2, metallic -0.5 (outside 0..1: stored as given)");
        int n1 = W1UndoCount(c);
        var r = c.Ok(new { cmd = "chara.edit.accessoryDefaults", key = f, slot }, "chara.edit.accessoryDefaults slot " + slot);
        c.Check(W1UndoCount(c) == n1 + 1 && W1TopLabel(c) == "Edit accessory", "one entry \"Edit accessory\"", W1TopLabel(c));
        var s = C35Slot(c, f, slot);
        var cd = C34Objs(s?["colourDefaults"]);
        var ci = C34Objs(s?["colorInfo"]);
        bool ok = cd.Count == 4 && ci.Count == 4;
        for (int k = 0; ok && k < 3; k++)
            ok &= C34Near(C34F(ci[k]["color"]), C34F(cd[k]["color"]), 0) && (float)ci[k]["glossPower"] == (float)cd[k]["gloss"] && (float)ci[k]["metallicPower"] == (float)cd[k]["metallic"];
        c.Check(ok, "areas 1–3 = colourDefaults (the prefab's; XH4: 0.438 / 0.098, 0.245 / 0.085, 0.317 / 0)", Ctx.Short(C35J(ci)));
        c.Check(cd.Count == 4 && Math.Abs((float)cd[0]["gloss"] - 0.438f) < 1e-6f && Math.Abs((float)cd[1]["metallic"] - 0.085f) < 1e-6f, "359#0 colourDefaults = HS2's ↺ values (XH4)", Ctx.Short(C35J(cd)));
        c.Check(C30Strs(r["changed"]).Any(x => ((string)x).Contains("colors")), "changed names the colours", Ctx.Short(C35J(r["changed"])));
        var again = c.Ok(new { cmd = "chara.edit.accessoryDefaults", key = f, accessory = id }, "again by accessory id");
        c.Check(C30Strs(again["changed"]).Count == 0 && W1UndoCount(c) == n1 + 1, "a second «初期カラー» changes and records nothing");
        c.Fails(new { cmd = "chara.edit.accessoryDefaults", key = f }, "bad_args", "neither accessory nor slot → bad_args");
        c.Fails(new { cmd = "chara.edit.accessoryDefaults", key = f, accessory = id, slot }, "bad_args", "accessory and slot → bad_args");
        c.Fails(new { cmd = "chara.edit.accessoryDefaults", key = f, slot = 99 }, "not_found", "an empty slot → not_found");
        c.Fails(new { cmd = "chara.edit.accessoryDefaults", key = f, accessory = "nosuchid" }, "not_found", "an unknown accessory → not_found");
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (defaults block)");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
    }

    // ---------------------------------------------------------------- chara.edit.accessoryHairColour and the draw type

    static void C35HairColour(Ctx c, string f)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var a = c.Ok(new { cmd = "chara.edit.accessory", key = f, op = "add", category = 351, @ref = C35HairBun }, "add 351#40 «お団子髪» (hair-type)");
        string id = (string)a["accessory"];
        int slot = C35SlotOfReply(a);
        var s = C35Slot(c, f, slot);
        c.Check((bool?)s?["facts"]?["typeHair"] == true && (int?)s?["hairShaderType"] == (int?)s?["shaderType"], "hair-type: facts.typeHair, hairShaderType = the character's draw type", Ctx.Short(C35J(s?["facts"])));
        var hairMat = C34Objs(c.Call(new { cmd = "chara.edit.hair", key = f, part = "back" })["parts"]?[0]?["materials"]).FirstOrDefault();
        string texD = (string)s?["row"]?["TexD"], texC = (string)s?["row"]?["TexC"];
        var normal = C34Objs(s?["materials"]).Where(x => ((string)x["kind"]).StartsWith("normal", StringComparison.Ordinal)).ToList();
        c.Check(normal.Count > 0 && normal.All(x => (string)x["shader"] == (string)hairMat?["shader"] && (texD == null || (string)x["mainTex"] == texD)),
            "every normal material: the hair parts' shader (" + hairMat?["shader"] + ") and _MainTex = TexD " + texD, Ctx.Short(C35J(normal.Select(x => x["shader"] + " " + x["mainTex"]))));
        // a new hair-type accessory takes the back hair's colours (B's add rule = HS2's item pick from a non-hair row, XQ6)
        var col0 = C34Objs(C30Value(c, f, C35Acc(id, "colors")));
        c.Check(col0.Count == 4 && C34Near(C34F(col0[0]["color"]), C34F(C30Value(c, f, "chara.hair.back.baseColor")), 0) && C34Near(C34F(col0[3]["color"]), C34F(C30Value(c, f, "chara.hair.back.specular")), 0)
                && (float)col0[0]["smoothness"] == (float)C30Value(c, f, "chara.hair.back.smoothness"),
            "the added hair-type accessory has the back hair's colours (base, specular, smoothness)", Ctx.Short(C35J(col0)));
        int n1 = W1UndoCount(c);
        foreach (var (part, name) in new[] { (1, "front"), (0, "back"), (2, "side"), (3, "option") })
        {
            object arg = part == 2 ? (object)2 : name;
            // a distinct colour first, so every press changes something (BR-Chan's four parts share their colours)
            c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { [C35Acc(id, "colors[0].color")] = new[] { 0.123, 0.456, 0.789, 1.0 } } }, "colour 1 set apart before " + name);
            int nb = W1UndoCount(c);
            var r = c.Ok(new { cmd = "chara.edit.accessoryHairColour", key = f, accessory = id, part = arg }, "«髪の毛の色を取得» " + name);
            c.Check(W1UndoCount(c) == nb + 1 && W1TopLabel(c) == "Edit accessory" && C30Strs(r["changed"]).Count > 0, name + ": one entry \"Edit accessory\"", W1TopLabel(c));
            string hp = "chara.hair." + name + ".";
            var col = C34Objs(C30Value(c, f, C35Acc(id, "colors")));
            bool ok = col.Count == 4 && C34Near(C34F(col[0]["color"]), C34F(C30Value(c, f, hp + "baseColor")), 0) && C34Near(C34F(col[1]["color"]), C34F(C30Value(c, f, hp + "topColor")), 0)
                      && C34Near(C34F(col[2]["color"]), C34F(C30Value(c, f, hp + "underColor")), 0) && C34Near(C34F(col[3]["color"]), C34F(C30Value(c, f, hp + "specular")), 0)
                      && (float)col[0]["smoothness"] == (float)C30Value(c, f, hp + "smoothness") && (float)col[0]["metallic"] == (float)C30Value(c, f, hp + "metallic");
            c.Check(ok, name + ": colours 1–4 = its base / top / under / specular, colour 1 smoothness / metallic = its", Ctx.Short(C35J(col)));
        }
        c.Check(W1UndoCount(c) == n1 + 8, "four presses (and four set-asides): eight entries", "" + (W1UndoCount(c) - n1));
        var bySlot = c.Ok(new { cmd = "chara.edit.accessoryHairColour", key = f, slot, part = "front" }, "by slot");
        c.Check(C35J(C30Value(c, f, C35Acc(id, "colors[0].color"))) == C35J(C30Value(c, f, "chara.hair.front.baseColor")), "by slot: the same op (colour 1 = the front hair's base)");
        c.Fails(new { cmd = "chara.edit.accessoryHairColour", key = f, accessory = id, part = "hat" }, "bad_args", "part \"hat\" → bad_args");
        c.Fails(new { cmd = "chara.edit.accessoryHairColour", key = f, accessory = id, part = 4 }, "bad_args", "part 4 → bad_args");
        c.Fails(new { cmd = "chara.edit.accessoryHairColour", key = f, accessory = id }, "bad_args", "no part → bad_args");
        c.Fails(new { cmd = "chara.edit.accessoryDefaults", key = f, accessory = id }, "unsupported", "«初期カラー» of a hair-type accessory → unsupported (HS2 hides it)");
        var plain = C35Table(c, f).FirstOrDefault(kv => kv.Value != id && (bool?)C35Slot(c, f, kv.Key)?["facts"]?["typeHair"] == false);
        if (plain.Value != null) c.Fails(new { cmd = "chara.edit.accessoryHairColour", key = f, accessory = plain.Value, part = "back" }, "unsupported", "«髪の毛の色を取得» on a non-hair accessory → unsupported");
        // the draw type re-shades the hair-type slot
        int type0 = (int)C30Value(c, f, "chara.hair.shaderType");
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.hair.shaderType"] = 1 - type0 } }, "hair.shaderType " + (1 - type0));
        var s1 = C35Slot(c, f, slot);
        var info = c.Call(new { cmd = "chara.edit.info", key = f });
        string tex1 = 1 - type0 == 0 ? texD : texC;
        var hairMat1 = C34Objs(c.Call(new { cmd = "chara.edit.hair", key = f, part = "back" })["parts"]?[0]?["materials"]).FirstOrDefault();
        c.Check((int?)s1?["hairShaderType"] == 1 - type0 && (int?)s1?["shaderType"] == 1 - type0 && C34Objs(s1?["materials"]).Where(x => ((string)x["kind"]).StartsWith("normal", StringComparison.Ordinal))
                    .All(x => (string)x["shader"] == (string)hairMat1?["shader"] && (tex1 == null || (string)x["mainTex"] == tex1)),
            "after the switch: hairShaderType " + (1 - type0) + ", the hair parts' shader, _MainTex = " + tex1, Ctx.Short(C35J(s1?["materials"])));
        c.Check(C30Strs(info["accessories"]?["lastFlush"]?["accessoryHairShaded"]).Select(x => (int)x).Contains(slot), "chara.edit.info accessories.lastFlush.accessoryHairShaded names slot " + slot,
            Ctx.Short(C35J(info["accessories"]?["lastFlush"])));
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (hair colour block)");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
    }

    // ---------------------------------------------------------------- chara.accessories by build slot

    static void C35Visibility(Ctx c, string f)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        // an entry added by list index has no preferred slot: its build slot is the lowest free one
        var a = c.Ok(new { cmd = "chara.edit.accessory", key = f, op = "add", category = 352, @ref = C35Star, index = 0 }, "add 352#0 by list index (deprecated form, no preferred slot)");
        string id = (string)a["accessory"];
        int slot = C35SlotOfReply(a);
        c.Check(slot >= 0 && C30Value(c, f, C35Acc(id, "importSlot"))?.Type == JTokenType.Null, "no importSlot; build slot " + slot + " (the reply)", Ctx.Short(C35J(a)));
        var r = c.Ok(new { cmd = "chara.accessories", key = f, show = new Dictionary<string, object> { [slot.ToString(CultureInfo.InvariantCulture)] = false } }, "chara.accessories show {\"" + slot + "\": false}");
        c.Check((bool?)r["accessories"]?[id] == false && C30Strs(r["changed"]).Any(x => (string)x == id), "the build slot names that accessory (it has no import slot)", Ctx.Short(C35J(r)));
        c.Check((bool?)C35Slot(c, f, slot)?["show"] == false && (bool?)C35Slot(c, f, slot)?["active"] == false, "chara.edit.accessories: show false, the object inactive");
        int firstSlot = C35Table(c, f).Keys.Min();
        string firstId = C35Table(c, f)[firstSlot];
        var r0 = c.Ok(new { cmd = "chara.accessories", key = f, show = new Dictionary<string, object> { [firstSlot.ToString(CultureInfo.InvariantCulture)] = false } }, "show {\"" + firstSlot + "\": false}");
        c.Check((bool?)r0["accessories"]?[firstId] == false, "build slot " + firstSlot + " = the accessory chara.edit.accessories lists there");
        var nf = c.Fails(new { cmd = "chara.accessories", key = f, show = new Dictionary<string, object> { ["98"] = false } }, "not_found", "an empty build slot → not_found");
        c.Check(nf.ToString().Contains("build slot"), "… the message names the build slot", Ctx.Short(C35J(nf)));
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (visibility block)");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
    }

    // ---------------------------------------------------------------- chara.edit.set / reset on accessory members

    static void C35Edits(Ctx c, string f)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var t = C35Table(c, f);
        int slot = t.Keys.Min();
        string id = t[slot];
        var imp = c.Fails(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { [C35Acc(id, "importSlot")] = 7 } }, "unsupported", "chara.edit.set importSlot → unsupported (a write-blocked member)");
        c.Check(imp.ToString().Contains("preferred slot"), "… \"the preferred slot is set by add / copy / move-to-slot\"", Ctx.Short(C35J(imp)));
        c.Fails(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { [C35Acc(id, "parent")] = "N_nope" } }, "bad_args", "chara.edit.set parent \"N_nope\" → bad_args (unknown attach point)");
        // raw values: no rounding or clamping (values stored as given)
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object>
        {
            [C35Acc(id, "moves[0].position")] = new[] { 12.34, 400.0, -0.05 }, [C35Acc(id, "moves[0].rotation")] = new[] { -30.0, 359.6, 720.5 }, [C35Acc(id, "moves[0].scale")] = new[] { 0.0, 0.005, 150.0 },
        } }, "set position (12.34, 400, -0.05), rotation (-30, 359.6, 720.5), scale (0, 0.005, 150)");
        c.Check(C34Near(C34F(C30Value(c, f, C35Acc(id, "moves[0].position"))), new[] { 12.34f, 400f, -0.05f }, 0) && C34Near(C34F(C30Value(c, f, C35Acc(id, "moves[0].rotation"))), new[] { -30f, 359.6f, 720.5f }, 1e-4f)
                && C34Near(C34F(C30Value(c, f, C35Acc(id, "moves[0].scale"))), new[] { 0f, 0.005f, 150f }, 0),
            "the bridge stores raw values (no grid, no limit, no wrap)", C34S(C34F(C30Value(c, f, C35Acc(id, "moves[0].rotation")))));
        var mv = C35Slot(c, f, slot)?["moves"]?[0];
        c.Check(mv == null || mv.Type == JTokenType.Null || C34Near(C34F(mv["position"]), new[] { 1.234f, 40f, -0.005f }, 1e-4f), "the set's local position = stored × 0.1 (applied as stored)", Ctx.Short(C35J(mv)));
        var r = c.Ok(new { cmd = "chara.edit.reset", key = f, paths = new[] { C35Acc(id, "moves[0].position#1"), C35Acc(id, "moves[0].scale#2"), C35Acc(id, "moves[0].rotation") } },
            "chara.edit.reset position#1, scale#2, rotation (no reference)");
        c.Check(C34Near(C34F(C30Value(c, f, C35Acc(id, "moves[0].position"))), new[] { 12.34f, 0f, -0.05f }, 0) && C34Near(C34F(C30Value(c, f, C35Acc(id, "moves[0].scale"))), new[] { 0f, 0.005f, 1f }, 0)
                && C34Near(C34F(C30Value(c, f, C35Acc(id, "moves[0].rotation"))), new[] { 0f, 0f, 0f }, 0) && W1TopLabel(c) == "Reset value" && !C34Warned(r, "edit.resetUnavailable"),
            "the Accessories rows' defaults: position y ← 0, scale z ← 1, rotation ← (0, 0, 0); one entry \"Reset value\", no edit.resetUnavailable",
            C34S(C34F(C30Value(c, f, C35Acc(id, "moves[0].position")))) + " " + C34S(C34F(C30Value(c, f, C35Acc(id, "moves[0].scale")))) + " " + Ctx.Short(C35J(r["warnings"])));
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (edits block)");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
    }

    // ---------------------------------------------------------------- chara.edit.materials part accessories

    static void C35Materials(Ctx c, string f)
    {
        var r = c.Ok(new { cmd = "chara.edit.materials", key = f, part = "accessories" }, "chara.edit.materials part accessories");
        var rs = C34Objs(r["renderers"]);
        var ctx = C34Ctx(c, f);
        int want = 0;
        if (ctx?.Accessories != null) foreach (var a in ctx.Accessories) if (a?.Object) want += a.Object.GetComponentsInChildren<Renderer>(true).Count(x => x);
        c.Check(rs.Count == want && want > 0 && rs.All(x => x["slot"]?.Type == JTokenType.Integer), "every renderer under the slot objects (" + want + "), each with its build slot", "" + rs.Count);
        var order = rs.Select(x => (int)x["slot"]).ToList();
        c.Check(order.Zip(order.Skip(1), (a, b) => a <= b).All(x => x), "build-slot order");
        int s0 = order.FirstOrDefault();
        var one = c.Ok(new { cmd = "chara.edit.materials", key = f, part = "accessories", slot = s0 }, "slot " + s0);
        c.Check(C34Objs(one["renderers"]).Count == order.Count(x => x == s0) && C34Objs(one["renderers"]).All(x => (int)x["slot"] == s0), "slot " + s0 + ": that slot's renderers only");
        var withTex = rs.SelectMany(x => C34Objs(x["slots"])).FirstOrDefault(s => s["props"]?["_MainTex"] != null);
        c.Check(withTex == null || withTex["props"]?["_MainTex_ST"] is JArray, "texture properties followed by \"<name>_ST\"");
        c.Fails(new { cmd = "chara.edit.materials", key = f, slot = 0 }, "bad_args", "slot without part accessories → bad_args");
        c.Fails(new { cmd = "chara.edit.materials", key = f, part = "accessories", slot = -1 }, "bad_args", "slot -1 → bad_args");
        c.Fails(new { cmd = "chara.edit.materials", key = f, part = "accessory" }, "bad_args", "part \"accessory\" → bad_args");
    }

    // ---------------------------------------------------------------- chara.edit.digest parts accessories

    static void C35Digest(Ctx c, string f)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var d0 = c.Ok(new { cmd = "chara.edit.digest", key = f, parts = "accessories", text = true }, "chara.edit.digest parts accessories");
        var ctx = C34Ctx(c, f);
        c.Check((string)d0["parts"] == "accessories" && ((string)d0["materials"])?.Length == 64 && ((string)d0["objects"])?.Length == 64
                && (string)d0["bones"] == FaceDigest.Sha256("") && (string)d0["creators"] == FaceDigest.Sha256(""),
            "sections materials, objects (SHA-256); creators / bones = the empty text's hash", Ctx.Short(C35J(d0)));
        c.Check((string)d0["texts"]?["objects"] == FaceDigest.ObjectsText(ctx, "accessories") && (string)d0["texts"]?["materials"] == FaceDigest.MaterialsText(ctx, "accessories"),
            "texts = FaceDigest's accessory texts");
        var all = c.Ok(new { cmd = "chara.edit.digest", key = f, parts = "all" }, "parts all");
        c.Check(all["objects"] != null, "parts all has objects");
        var t = C35Table(c, f);
        string id = t[t.Keys.Min()];
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { [C35Acc(id, "moves[0].position")] = new[] { 1.0, 2.0, 3.0 } } }, "a transform-set edit");
        var d1 = c.Call(new { cmd = "chara.edit.digest", key = f, parts = "accessories" });
        c.Check((string)d1["objects"] != (string)d0["objects"] && (string)d1["materials"] == (string)d0["materials"], "a transform-set edit: objects change, materials do not");
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { [C35Acc(id, "colors[0].color")] = new[] { 0.2, 0.4, 0.6, 1.0 } } }, "a colour edit");
        var d2 = c.Call(new { cmd = "chara.edit.digest", key = f, parts = "accessories" });
        c.Check((string)d2["materials"] != (string)d1["materials"] && (string)d2["objects"] == (string)d1["objects"], "a colour edit: materials change, objects do not");
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (digest block)");
        var d3 = c.Call(new { cmd = "chara.edit.digest", key = f, parts = "accessories" });
        c.Check(W1DocSha(c) == sha0 && (string)d3["objects"] == (string)d0["objects"] && (string)d3["materials"] == (string)d0["materials"], "undo: scene.doc and the accessories digest as before");
        c.Fails(new { cmd = "chara.edit.digest", key = f, parts = "accessory" }, "bad_args", "parts \"accessory\" → bad_args");
    }

    // ---------------------------------------------------------------- chara.edit.info accessories

    static void C35Info(Ctx c, string f)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var i = c.Ok(new { cmd = "chara.edit.info", key = f }, "chara.edit.info BR-Chan");
        var slots = C34Objs(i["accessories"]?["slots"]);
        var acc = C35Slots(c.Call(new { cmd = "chara.edit.accessories", key = f, served = false }));
        c.Check(slots.Count == acc.Count && slots.Zip(acc, (a, b) => (int)a["slot"] == (int)b["slot"] && (string)a["accessory"] == (string)b["accessory"] && (string)a["item"] == (string)b["item"]
                    && new[] { "hasCmpAccessory", "parentColumn", "typeHair", "useColor", "alphaRenderers", "move1", "move2", "dynamicBones", "colliders" }.All(k => JToken.DeepEquals(a["facts"]?[k], b["facts"]?[k]))).All(x => x),
            "accessories.slots: slot, accessory, item and facts = chara.edit.accessories's", Ctx.Short(C35J(i["accessories"])));
        var parts = C34Objs(i["parts"]?["accessories"]);
        c.Check(parts.Count == acc.Count && parts.All(p => acc.Any(x => (int)x["slot"] == (int)p["slot"] && (string)x["accessory"] == (string)p["accessory"])),
            "parts.accessories[]: accessory and slot (the build slot)", Ctx.Short(C35J(parts)));
        var t = C35Table(c, f);
        int slot = t.Keys.Min();
        var cur = C35Slot(c, f, slot);
        string other = C34Objs(c.Call(new { cmd = "chara.edit.list", category = (int?)cur?["type"] ?? 351, sex = "female", limit = 3 })["rows"]).Select(x => (string)x["ref"]).FirstOrDefault(x => x != (string)cur?["id"]);
        c.Ok(new { cmd = "chara.edit.item", key = f, path = C35Acc(t[slot], "item"), @ref = other }, "an item change of slot " + slot + " (" + cur?["id"] + " → " + other + ")");
        var lf = c.Call(new { cmd = "chara.edit.info", key = f })["accessories"]?["lastFlush"];
        c.Check(C30Strs(lf?["accessorySlotsSwapped"]).Select(x => (int)x).Contains(slot), "lastFlush.accessorySlotsSwapped names slot " + slot, Ctx.Short(C35J(lf)));
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (info block)");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
    }

    // ---------------------------------------------------------------- HERO107: a head-weighted row

    static void C35Male(Ctx c, string m)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var r = c.Ok(new { cmd = "chara.edit.accessories", key = m }, "chara.edit.accessories HERO107");
        c.Check((bool?)r["male"] == true && C35Slots(r).Count == C35Table(c, m).Count, "male: true; the slots = its table (" + C35Slots(r).Count + ")");
        var a = c.Ok(new { cmd = "chara.edit.accessory", key = m, op = "add", category = 351, @ref = C35Bag }, "add 351#202 «袋頭巾» (Parent \"null\")");
        string id = (string)a["accessory"];
        int slot = C35SlotOfReply(a);
        var s = C35Slot(c, m, slot);
        c.Check((bool?)s?["facts"]?["headWeighted"] == true && (string)s?["facts"]?["parentColumn"] == "null" && (string)s?["anchorUsed"] == "head bones" && (bool?)s?["partsOfHead"] == false
                && (string)s?["parent"] == "" && (string)s?["parentKey"] == "",
            "head-weighted: parentColumn \"null\", anchorUsed \"head bones\", partsOfHead false, stored parent \"\", parentKey \"\" (\"null\" is no key)", Ctx.Short(C35J(s)));
        c.Ok(new { cmd = "chara.edit.set", key = m, set = new Dictionary<string, object> { [C35Acc(id, "parent")] = "N_Head_top" } }, "parent N_Head_top");
        var s2 = C35Slot(c, m, slot);
        c.Check((string)s2?["parent"] == "N_Head_top" && (string)s2?["parentKey"] == "N_Head_top" && (string)s2?["anchorUsed"] == "head bones", "the key stored, the object still on the head bones", Ctx.Short(C35J(s2)));
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (male block)");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
    }

    // ---------------------------------------------------------------- batch

    static void C35Batch(Ctx c, string f)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var t = C35Table(c, f);
        var free = Enumerable.Range(0, 20).Where(s => !t.ContainsKey(s)).ToList();
        if (free.Count < 2) { c.Warn("fewer than 2 free slots: the batch check is skipped"); return; }
        c.Ok(new
        {
            cmd = "batch",
            cmds = new object[]
            {
                new { cmd = "chara.edit.accessory", key = f, op = "add", category = 353, @ref = C35Square, slot = free[0] },
                new { cmd = "chara.edit.accessoryDefaults", key = f, slot = free[0] },
                new { cmd = "chara.edit.accessories", key = f, slot = free[0], served = false },
                new { cmd = "chara.edit.materials", key = f, part = "accessories", slot = free[0] },
            },
        }, "a batch of add (slot " + free[0] + "), accessoryDefaults, accessories, materials");
        c.Check(W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Batch", "one entry \"Batch\"", W1UndoCount(c) + " " + W1TopLabel(c));
        c.Ok(new { cmd = "undo" }, "undo the batch");
        c.Check(W1DocSha(c) == sha0, "one undo restores scene.doc");
    }
}
