// C30 of BridgeCoreTest: the Phase 4.0 Maker edit commands of package E (
// C26" — numbered C30 here because C26–C29 were taken by Phase 3.5 / 3.6), further methods of the partial BridgeCoreTest
// (-only C30), edit mode: registration (CoreCommands.Phase40Names); chara.edit.get (whole documents = scene.doc, paths,
// errors); shape.info (HS2's members, the documents' values); shape.set (values stored as given, −1..2 unclamped, settled
// before the reply, the bones move and come back on undo, one entry "Edit face" / "Edit body", errors); chara.edit.set
// (changed / noop / consequences, several keys in one entry, rule errors, the flush's units); chara.edit.item (hair and
// head changes with their consequences, the built part rows follow, undo); chara.edit.list (key + path, category + sex,
// face skins under the character's head, paging, search, errors; = MakerLists); chara.edit.preset (face block of the
// vanilla head-0 preset, errors); chara.edit.accessory (add / move / copy / remove, ids, built slots, undo); chara.edit.info
// (dirty, last flush, creators, owned materials, session); a batch of edits is one entry. Numbers only: bone values and
// counts, no capture. The SFW targets of Tools/targets.json only.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Content;
using HS2Next.Content.Maker;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;

public static partial class BridgeCoreTest
{
    static void C30(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        var names = HS2Next.App.Commands.CoreCommands.Phase40Names;
        c.Check(names.SequenceEqual(HS2Next.App.Commands.CharaEditCommands.Names) && names.Count == 9 && names.All(n => reg.Find(n) != null),
            "Phase40Names = the 9 commands of CharaEditCommands (chara.edit.*, shape.info, shape.set), all registered", string.Join(", ", names.Where(n => reg.Find(n) == null)));
        c.Check(HS2Next.App.Commands.CoreCommands.AllNames.Count(n => names.Contains(n)) == 9, "CoreCommands.AllNames lists them once");
        c.Ok(new { cmd = "scene.new" }, "scene.new");
        string f = C30Load(c, c.FemaleRel, "female", "chara.load BR-Chan");
        string m = C30Load(c, c.MaleRel, "male", "chara.load HERO107");
        if (f == null || m == null) return;
        string item = W1AddItem(c);
        c.Frames(2);
        C30Get(c, f, item);
        C30ShapeInfo(c, f, m);
        C30ShapeSet(c, f, m);
        C30Set(c, f, m);
        C30List(c, f, m);
        C30Item(c, f);
        C30Preset(c, f);
        C30Accessory(c, f);
        C30Info(c, f, item);
        C30Batch(c, f);
    }

    static string C30Load(Ctx c, string rel, string sex, string what)
    {
        var r = c.Ok(new { cmd = "chara.load", path = rel, sex }, what);
        var a = r["added"]?[0] as JObject;
        if (a == null || (string)a["state"] != "Built") { c.Check(false, what + ": built", Ctx.Short(c.LastText)); return null; }
        string key = (string)a["key"];
        // paused, physics off (dynamic bones / bust simulation keep a history that a body shape edit resets):
        // the bone checks then see the shape-driven pose only
        c.Ok(new { cmd = "chara.motion", key, paused = true, physics = false }, what + ": chara.motion paused, physics off");
        return key;
    }

    static JArray C30Strs(JToken t) => t as JArray ?? new JArray();
    static bool C30Has(JToken list, string s) => C30Strs(list).Any(x => (string)x == s);

    /// <summary>Every bone of bones.dump all (motion paused): path → lp, lq, ls as text.</summary>
    static Dictionary<string, string> C30ShapeBones(Ctx c, string key)
    {
        var d = c.Call(new { cmd = "bones.dump", key, root = "all" });
        var r = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var b in (d["bones"] as JArray ?? new JArray()).OfType<JObject>())
        {
            string name = (string)b["name"];
            if (name == null) continue;
            string path = (string)b["path"] ?? name;
            r[path] = b["lp"]?.ToString(Newtonsoft.Json.Formatting.None) + b["lq"]?.ToString(Newtonsoft.Json.Formatting.None) + b["ls"]?.ToString(Newtonsoft.Json.Formatting.None);
        }
        return r;
    }

    static int C30Diff(Dictionary<string, string> a, Dictionary<string, string> b) =>
        a.Count(kv => !b.TryGetValue(kv.Key, out var v) || v != kv.Value) + b.Keys.Count(k => !a.ContainsKey(k));

    /// <summary>The differing bones (path: a | b), at most <paramref name="n"/>.</summary>
    static string C30DiffText(Dictionary<string, string> a, Dictionary<string, string> b, int n = 12) =>
        string.Join("\n   ", a.Where(kv => !b.TryGetValue(kv.Key, out var v) || v != kv.Value).Take(n)
            .Select(kv => kv.Key + ": " + kv.Value + " | " + (b.TryGetValue(kv.Key, out var v2) ? v2 : "-")));

    static JToken C30Value(Ctx c, string key, string path) => c.Call(new { cmd = "chara.edit.get", key, paths = new[] { path } })["values"]?[path];

    // ---------------------------------------------------------------- chara.edit.get

    static void C30Get(Ctx c, string f, string item)
    {
        var all = c.Ok(new { cmd = "chara.edit.get", key = f }, "chara.edit.get (whole documents)");
        var doc = c.Call(new { cmd = "scene.doc", key = f, capture = false, embed = true });
        c.Check(all["values"]?["chara"] is JObject ch && JToken.DeepEquals(ch, doc["chara"]) && all["values"]?["coord"] is JObject co && JToken.DeepEquals(co, doc["coord"]),
            "chara.edit.get without paths = scene.doc's chara.json and coord.json", Ctx.Short(c.LastText));
        var chara = (JObject)doc["chara"];
        var g = c.Ok(new { cmd = "chara.edit.get", key = f, paths = new[] { "chara.face.shape[3]", "chara.face.head", "coord.clothes.top.item", "chara.body.shape", "appearance.chara.face.skin", "coord" } }, "chara.edit.get paths");
        var v = g["values"] as JObject;
        c.Check(v != null && JToken.DeepEquals(v["chara.face.shape[3]"], chara["face"]["shape"][3]) && (string)v["chara.face.head"] == (string)chara["face"]["head"]
                && JToken.DeepEquals(v["chara.body.shape"], chara["body"]["shape"]) && (string)v["appearance.chara.face.skin"] == (string)chara["face"]["skin"]
                && JToken.DeepEquals(v["coord"], doc["coord"]) && v["coord.clothes.top.item"]?.Type == JTokenType.String,
            "paths: values in the save-format JSON forms, keyed as given (relative or full; \"coord\" = the document)", Ctx.Short(g.ToString()));
        c.Fails(new { cmd = "chara.edit.get", key = f, paths = new[] { "chara.face.nosuch" } }, "bad_args", "an unknown member → bad_args");
        c.Fails(new { cmd = "chara.edit.get", key = f, paths = new[] { "coord.accessories[\"nosuchid\"].parent" } }, "not_found", "a missing accessory id → not_found");
        c.Fails(new { cmd = "chara.edit.get", key = f, paths = new[] { "chara.face.shape[" } }, "bad_args", "a malformed path → bad_args");
        c.Fails(new { cmd = "chara.edit.get", key = item }, "wrong_kind", "chara.edit.get on an item → wrong_kind");
        c.Fails(new { cmd = "chara.edit.get", key = "nosuchnode" }, "not_found", "an unknown key → not_found");
    }

    // ---------------------------------------------------------------- shape.info / shape.set

    static void C30ShapeInfo(Ctx c, string f, string m)
    {
        var si = c.Ok(new { cmd = "shape.info", key = f }, "shape.info BR-Chan");
        var hs2Order = new[] { "sex", "bodyCount", "faceCount", "bodyValues", "faceValues", "updatePending", "headId", "headName", "headMainAB", "headShapeAnime",
            "gameVersion", "unityVersion", "product", "bodyRoot", "bodyRootParent", "headRoot", "headRootParent" };
        var members = si.Properties().Select(p => p.Name).Where(n => n != "ok" && n != "cmd" && n != "id" && n != "warnings").ToList();
        var seen = members.Where(n => hs2Order.Contains(n)).ToList();
        c.Check(seen.SequenceEqual(hs2Order.Where(n => seen.Contains(n))) && seen.Count >= 13 && members.Last() == "headRef",
            "shape.info: HS2's members in HS2's order (bodyNames / faceNames not given), the HS2Next addition headRef last", string.Join(",", members));
        var doc = (JObject)c.Call(new { cmd = "scene.doc", key = f, capture = false, embed = true })["chara"];
        var bv = (si["bodyValues"] as JArray)?.Select(x => (float)(double)x).ToArray();
        var fv = (si["faceValues"] as JArray)?.Select(x => (float)(double)x).ToArray();
        c.Check((int)si["sex"] == 1 && (int)si["bodyCount"] == 33 && (int)si["faceCount"] == 59 && bv?.Length == 33 && fv?.Length == 59
                && bv.SequenceEqual(doc["body"]["shape"].Select(x => (float)x)) && fv.SequenceEqual(doc["face"]["shape"].Select(x => (float)x)),
            "shape.info: sex 1, 33 / 59 values = the chara document's (as float32)", Ctx.Short(si.ToString()));
        ContentId.TryParse((string)doc["face"]["head"], out var head);
        c.Check(si["headId"]?.Type == JTokenType.Integer && (int)si["headId"] == head.Id && (string)si["headRef"] == (string)doc["face"]["head"] && si["updatePending"]?.Type == JTokenType.Boolean
                && (string)si["product"] != null && (string)si["unityVersion"] == UnityEngine.Application.unityVersion,
            "shape.info: headId = the local id of face.head, headRef, updatePending, versions", Ctx.Short(si.ToString()));
        var sm = c.Ok(new { cmd = "shape.info", key = m }, "shape.info HERO107");
        c.Check((int)sm["sex"] == 0 && (sm["bodyValues"] as JArray)?.Count == 33 && (sm["faceValues"] as JArray)?.Count == 59, "shape.info male: sex 0, 33 / 59 values");
        c.Fails(new { cmd = "shape.info", key = W1AddItem(c, "item.add (shape.info wrong kind)") }, "wrong_kind", "shape.info on an item → wrong_kind");
        c.Ok(new { cmd = "undo" }, "undo the item add");
    }

    static void C30ShapeSet(Ctx c, string f, string m)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var bones0 = C30ShapeBones(c, f);
        c.Check(bones0.Count > 200, "bones.dump: bones found", "" + bones0.Count);
        var r = c.Ok(new { cmd = "shape.set", key = f, faceIndex = 3, faceValue = 1.5 }, "shape.set faceIndex 3 = 1.5 (typed 150)");
        var hs2 = new[] { "bodySet", "faceSet", "framesWaited", "settled", "changedTransforms", "bodyValues", "faceValues" };
        var members = r.Properties().Select(p => p.Name).Where(n => hs2.Contains(n)).ToList();
        c.Check(members.SequenceEqual(hs2), "shape.set: HS2's reply members in HS2's order", string.Join(",", members));
        c.Check((int)r["bodySet"] == 0 && (int)r["faceSet"] == 1 && (bool)r["settled"] && (int)r["changedTransforms"] > 0 && (float)(double)r["faceValues"][3] == 1.5f,
            "shape.set: faceSet 1, settled (flushed before the reply), transforms changed, faceValues[3] = 1.5 (stored as given)", Ctx.Short(r.ToString()));
        c.Check(C30Has(r["changed"], "chara.face.shape[3]"), "shape.set: changed names chara.face.shape[3]", Ctx.Short(r.ToString()));
        c.Check(W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Edit face", "one entry \"Edit face\"", W1UndoCount(c) + " " + W1TopLabel(c));
        c.Check((float)C30Value(c, f, "chara.face.shape[3]") == 1.5f, "the document holds 1.5 (no clamp)");
        var bones1 = C30ShapeBones(c, f);
        int moved = C30Diff(bones0, bones1);
        c.Check(moved > 0, "the face shape bones moved (" + moved + " shape bones)");
        var lo = c.Ok(new { cmd = "shape.set", key = f, faceIndex = 3, faceValue = -1 }, "shape.set faceIndex 3 = -1 (typed -100)");
        c.Check((float)(double)lo["faceValues"][3] == -1f && (int)lo["changedTransforms"] > 0, "−1 stored and realised (extrapolated, not clamped to 0)", Ctx.Short(lo.ToString()));
        var bonesLo = C30ShapeBones(c, f);
        c.Check(C30Diff(bones1, bonesLo) > 0, "the bones of −1 differ from those of 1.5");
        var same = c.Ok(new { cmd = "shape.set", key = f, faceIndex = 3, faceValue = -1 }, "shape.set the same value again");
        c.Check((int)same["changedTransforms"] == 0 && (same["changed"] as JArray)?.Count == 0 && W1UndoCount(c) == n0 + 2, "the same value: nothing changes, no entry", Ctx.Short(same.ToString()));
        var bw = c.Ok(new { cmd = "shape.set", key = f, body = new[] { 0.25, 2.0 } }, "shape.set body [0.25, 2]");
        c.Check((int)bw["bodySet"] == 2 && (float)(double)bw["bodyValues"][1] == 2f && (float)(double)bw["bodyValues"][0] == 0.25f && W1TopLabel(c) == "Edit body",
            "shape.set body array: the first two values set (2 stored unclamped), entry \"Edit body\"", Ctx.Short(bw.ToString()) + " " + W1TopLabel(c));
        var mm = c.Ok(new { cmd = "shape.set", key = m, bodyIndex = 0, bodyValue = 1.05 }, "shape.set male height 1.05");
        c.Check(Math.Abs((double)mm["bodyValues"][0] - 1.05) < 1e-6 && (bool)mm["settled"], "male: 1.05 stored as given (the extender skips the male range check)", Ctx.Short(mm.ToString()));
        c.Fails(new { cmd = "shape.set", key = f, bodyIndex = 33, bodyValue = 0.5 }, "bad_args", "bodyIndex 33 → bad_args");
        c.Fails(new { cmd = "shape.set", key = f, faceIndex = -1 }, "bad_args", "faceIndex −1 → bad_args");
        c.Fails(new { cmd = "shape.set", key = f, face = Enumerable.Repeat(0.5, 60).ToArray() }, "bad_args", "60 face values → bad_args");
        c.Fails(new { cmd = "shape.set", key = f, faceIndex = 3, faceValue = "x" }, "bad_args", "a text value → bad_args");
        c.Fails(new { cmd = "shape.set", key = f, face = new object[] { 0.5, "x" } }, "bad_args", "a text array element → bad_args");
        int nm = W1UndoCount(c);
        var none = c.Ok(new { cmd = "shape.set", key = f }, "shape.set without values");
        c.Check((int)none["bodySet"] == 0 && (int)none["faceSet"] == 0 && (int)none["changedTransforms"] == 0 && W1UndoCount(c) == nm, "nothing to set: no entry, nothing moves");
        for (int i = 0; i < 4; i++) c.Ok(new { cmd = "undo" }, "undo shape.set " + (4 - i));
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
        var bones2 = C30ShapeBones(c, f);
        if (!c.Check(C30Diff(bones0, bones2) == 0, "undo restores the shape bones exactly", "" + C30Diff(bones0, bones2))) c.Info(C30DiffText(bones0, bones2));
        c.Ok(new { cmd = "redo" }, "redo shape.set 1.5");
        var bones3 = C30ShapeBones(c, f);
        if (!c.Check(C30Diff(bones1, bones3) == 0, "redo realises the same bones as the set", "" + C30Diff(bones1, bones3))) c.Info(C30DiffText(bones1, bones3));
        c.Ok(new { cmd = "undo" }, "undo again");
    }

    // ---------------------------------------------------------------- chara.edit.set

    static void C30Set(Ctx c, string f, string m)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var s = c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.face.shape[5]"] = 2.0, ["chara.face.shape[6]"] = -1.0 } }, "chara.edit.set face.shape[5] = 2, [6] = −1");
        c.Check(C30Has(s["changed"], "chara.face.shape[5]") && C30Has(s["changed"], "chara.face.shape[6]") && C30Strs(s["noop"]).Count == 0 && C30Strs(s["consequences"]).Count == 0,
            "changed names both paths (relative to appearance.), no consequences", Ctx.Short(s.ToString()));
        c.Check(W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Edit face", "one entry \"Edit face\"", W1UndoCount(c) + " " + W1TopLabel(c));
        c.Check((float)C30Value(c, f, "chara.face.shape[5]") == 2f && (float)C30Value(c, f, "chara.face.shape[6]") == -1f, "2 and −1 stored as given");
        var info = c.Ok(new { cmd = "chara.edit.info", key = f }, "chara.edit.info after the set");
        c.Check(C30Has(info["lastFlush"]?["units"], "shape.face") && C30Strs(info["dirty"]).Count == 0, "the flush ran the shape.face unit; nothing dirty", Ctx.Short(info["lastFlush"]?.ToString()));
        var again = c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.face.shape[5]"] = 2.0 } }, "the same value again");
        c.Check(C30Has(again["noop"], "chara.face.shape[5]") && C30Strs(again["changed"]).Count == 0 && W1UndoCount(c) == n0 + 1, "noop, no entry", Ctx.Short(again.ToString()));
        var col = c.Ok(new { cmd = "chara.edit.set", keys = new[] { f, m }, set = new Dictionary<string, object> { ["appearance.chara.body.skin.color"] = new[] { 0.9, 0.7, 0.6, 1.0 } } }, "chara.edit.set keys [f, m] body.skin.color");
        c.Check(C30Has(col["changed"], "chara.body.skin.color") && W1UndoCount(c) == n0 + 2, "several keys: one entry", W1UndoCount(c) + " " + Ctx.Short(col.ToString()));
        info = c.Ok(new { cmd = "chara.edit.info", key = m }, "chara.edit.info male after the colour");
        var units = C30Strs(info["lastFlush"]?["units"]).Select(x => (string)x).ToList();
        c.Check(units.Contains("skin.body") && units.Contains("skin.face"), "the colour re-composited body and face (units skin.body, skin.face)", string.Join(",", units));
        c.Fails(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object>() }, "bad_args", "set {} → bad_args");
        c.Fails(new { cmd = "chara.edit.set", key = f }, "bad_args", "no set → bad_args");
        c.Fails(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.face.shape[3]"] = "x" } }, "bad_args", "a value of the wrong type → bad_args");
        c.Fails(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.face.shape"] = new[] { 0.5 } } }, "bad_args", "a fixed-length array of the wrong length → bad_args");
        c.Fails(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.profile.sex"] = 0 } }, "unsupported", "chara.profile.sex → unsupported");
        c.Fails(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.body.uncensor"] = null } }, "unsupported", "chara.body.uncensor without rebuild → unsupported");
        c.Fails(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["coord.accessories[\"nosuchid\"].parent"] = "N_Head" } }, "not_found", "a missing accessory id → not_found");
        c.Fails(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.face.skin"] = "illusion.hs2#987654" } }, "not_found", "an unlisted skin row → not_found");
        c.Check(W1UndoCount(c) == n0 + 2, "failed sets record nothing");
        c.Ok(new { cmd = "undo" }, "undo the colour");
        c.Ok(new { cmd = "undo" }, "undo the shapes");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
    }

    // ---------------------------------------------------------------- chara.edit.list

    static void C30List(Ctx c, string f, string m)
    {
        var lists = MakerCatalog.For(c.Services.Catalog);
        string head = (string)C30Value(c, f, "chara.face.head");
        ContentId.TryParse(head, out var hid);
        var sk = c.Ok(new { cmd = "chara.edit.list", key = f, path = "chara.face.skin", limit = 10000 }, "chara.edit.list key + chara.face.skin");
        var want = lists.Rows(211, 1, ListContext.ForHead(hid));
        var rows = (sk["rows"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        c.Check((int)sk["category"] == 211 && (int)sk["sex"] == 1 && (int)sk["total"] == want.Count && rows.Count == want.Count && want.Count > 0
                && rows.Select(x => (string)x["ref"]).SequenceEqual(want.Select(x => x.Key.ToString())),
            "face skins of the character's head = MakerLists.Rows(211, female, head) in order (" + want.Count + ")", Ctx.Short(sk.ToString()));
        c.Check(rows.All(x => x["headRef"] != null && x["name"] != null && x["possess"]?.Type == JTokenType.Integer && x["gated"]?.Type == JTokenType.Boolean)
                && rows.Count(x => (string)x["headRef"] == head) == rows.Count,
            "rows {ref, name, possess, gated, headRef}; every skin listed under the head", Ctx.Short(rows.FirstOrDefault()?.ToString()));
        var top = c.Ok(new { cmd = "chara.edit.list", category = 240, sex = "female" }, "chara.edit.list category 240 female");
        var wantTop = lists.Rows(240, 1);
        c.Check((int)top["total"] == wantTop.Count && (top["rows"] as JArray)?.Count == Math.Min(200, wantTop.Count) && top["rows"][0]?["headRef"] == null,
            "category + sex: total = MakerLists.Rows, default limit 200, no headRef outside skins", (int)top["total"] + " vs " + wantTop.Count);
        var page = c.Ok(new { cmd = "chara.edit.list", category = 240, sex = 1, offset = 5, limit = 3 }, "offset 5 limit 3");
        c.Check((page["rows"] as JArray)?.Select(x => (string)x["ref"]).SequenceEqual(wantTop.Skip(5).Take(3).Select(x => x.Key.ToString())) == true, "paging");
        string firstName = MakerLists.NameOf(wantTop[0], "ja");
        var srch = c.Ok(new { cmd = "chara.edit.list", category = 240, sex = 1, search = firstName, limit = 10000 }, "search by the first row's name");
        c.Check((int)srch["total"] >= 1 && (int)srch["total"] <= wantTop.Count && C30Strs(srch["rows"]).Any(x => (string)x["ref"] == wantTop[0].Key.ToString()), "search: a subset containing the row", "" + (int)srch["total"]);
        var allSk = c.Ok(new { cmd = "chara.edit.list", category = 211, sex = "female", limit = 10000 }, "category 211 without head: every skin");
        var allRows = C30Strs(allSk["rows"]).ToList();
        int distinct = allRows.Select(x => (string)x["ref"]).Distinct().Count();
        c.Check(distinct == lists.Rows(211, 1).Count && (int)allSk["total"] == allRows.Count && allRows.Count >= distinct && allRows.All(x => x["headRef"] != null),
            "211 without a head: every skin reference, one row per definition (HS2's list control), each with its head", allRows.Count + " rows, " + distinct + " refs");
        c.Info("211 unfiltered: " + allRows.Count + " rows (definitions), " + distinct + " references");
        var hd = c.Ok(new { cmd = "chara.edit.list", category = 211, sex = 1, head = "illusion.hs2#0", limit = 10000 }, "category 211 head illusion.hs2#0");
        c.Check((int)hd["total"] == lists.Rows(211, 1, ListContext.ForHead(ContentId.Vanilla(0))).Count && C30Strs(hd["rows"]).All(x => (string)x["headRef"] == "illusion.hs2#0"), "head argument filters the skins");
        var mt = c.Ok(new { cmd = "chara.edit.list", key = m, path = "coord.clothes.top.item" }, "male key + coord.clothes.top.item");
        c.Check((int)mt["category"] == 140 && (int)mt["sex"] == 0 && (int)mt["total"] == lists.Rows(140, 0).Count, "male top list = category 140", Ctx.Short(mt.ToString()));
        c.Fails(new { cmd = "chara.edit.list", key = m, path = "coord.clothes.innerTop.item" }, "bad_args", "male inner top (no list) → bad_args");
        c.Fails(new { cmd = "chara.edit.list", key = f, path = "chara.face.shape[3]" }, "bad_args", "a non-ref path → bad_args");
        c.Fails(new { cmd = "chara.edit.list", path = "chara.face.skin" }, "bad_args", "path without key → bad_args");
        c.Fails(new { cmd = "chara.edit.list", key = f, path = "chara.face.skin", category = 211 }, "bad_args", "path and category → bad_args");
        c.Fails(new { cmd = "chara.edit.list", category = 240, sex = "other" }, "bad_args", "an unknown sex → bad_args");
        c.Fails(new { cmd = "chara.edit.list", category = 240, limit = 0 }, "bad_args", "limit 0 → bad_args");
    }

    static string C30OtherRow(Ctx c, string key, string path, string current, bool vanilla = true)
    {
        var l = c.Call(new { cmd = "chara.edit.list", key, path, limit = 10000 });
        return C30Strs(l["rows"]).Select(x => (string)x["ref"]).FirstOrDefault(x => x != current && (!vanilla || x.StartsWith("illusion.hs2#", StringComparison.Ordinal)));
    }

    // ---------------------------------------------------------------- chara.edit.item

    static void C30Item(Ctx c, string f)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        string cur = (string)C30Value(c, f, "chara.hair.front.item");
        string other = C30OtherRow(c, f, "chara.hair.front.item", cur);
        if (other == null) { c.Warn("no other front hair row"); return; }
        var r = c.Ok(new { cmd = "chara.edit.item", key = f, path = "chara.hair.front.item", @ref = other }, "chara.edit.item hair front → " + other);
        c.Check(C30Has(r["changed"], "chara.hair.front.item") && W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Change item", "changed; one entry \"Change item\"", Ctx.Short(r.ToString()) + " " + W1TopLabel(c));
        c.Check(C30Strs(r["consequences"]).All(x => ((string)x).StartsWith("chara.hair.front.", StringComparison.Ordinal)), "consequences stay inside the part (bundles, decoration colours, ME items)", Ctx.Short(r["consequences"]?.ToString()));
        var info = c.Ok(new { cmd = "chara.edit.info", key = f }, "chara.edit.info after the hair change");
        c.Check((string)info["parts"]?["hair"]?[1] == other && C30Has(info["lastFlush"]?["units"], "hair.front"), "the front hair part ran (unit hair.front)", Ctx.Short(info["lastFlush"]?.ToString()));
        // head change: the skin snaps to the first skin of the new head when the current one is not listed for it
        string head = (string)C30Value(c, f, "chara.face.head"), skin = (string)C30Value(c, f, "chara.face.skin");
        string head2 = C30OtherRow(c, f, "chara.face.head", head);
        var h = c.Ok(new { cmd = "chara.edit.item", key = f, path = "chara.face.head", @ref = head2 }, "chara.edit.item face.head → " + head2);
        var skins2 = C30Strs(c.Call(new { cmd = "chara.edit.list", category = 211, sex = 1, head = head2, limit = 10000 })["rows"]).Select(x => (string)x["ref"]).ToList();
        string skinAfter = (string)C30Value(c, f, "chara.face.skin");
        bool snapped = skins2.Count > 0 && !skins2.Contains(skin);
        c.Check(snapped ? skinAfter == skins2[0] && C30Has(h["consequences"], "chara.face.skin") : skinAfter == skin,
            "head change: skin " + (snapped ? "snapped to the new head's first skin (consequence)" : "kept (listed for the new head)"), skin + " -> " + skinAfter);
        info = c.Ok(new { cmd = "chara.edit.info", key = f }, "chara.edit.info after the head change");
        c.Check((string)info["parts"]?["head"] == head2 && C30Has(info["lastFlush"]?["units"], "head") && (int)info["editRebuilds"] == 0, "the head unit swapped the head (no rebuild)", Ctx.Short(info.ToString()));
        c.Fails(new { cmd = "chara.edit.item", key = f, path = "chara.face.head", @ref = "illusion.hs2#987654" }, "not_found", "an unlisted head → not_found");
        c.Fails(new { cmd = "chara.edit.item", key = f, path = "chara.face.head", @ref = "nonsense" }, "bad_args", "a ref that does not parse → bad_args");
        c.Fails(new { cmd = "chara.edit.item", key = f, path = "chara.face.shape[3]", @ref = "illusion.hs2#0" }, "bad_args", "a non-ref path → bad_args");
        c.Fails(new { cmd = "chara.edit.item", key = f, path = "chara.face.head" }, "bad_args", "no ref → bad_args");
        c.Ok(new { cmd = "undo" }, "undo the head");
        c.Ok(new { cmd = "undo" }, "undo the hair");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
        info = c.Ok(new { cmd = "chara.edit.info", key = f }, "chara.edit.info after undo");
        c.Check((string)info["parts"]?["head"] == head && (string)info["parts"]?["hair"]?[1] == cur, "undo swaps head and hair back");
    }

    // ---------------------------------------------------------------- chara.edit.preset

    static void C30Preset(Ctx c, string f)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var r = c.Ok(new { cmd = "chara.edit.preset", key = f, head = "illusion.hs2#0" }, "chara.edit.preset head illusion.hs2#0");
        var changed = C30Strs(r["changed"]).Select(x => (string)x).ToList();
        c.Check(changed.All(x => x.StartsWith("chara.face", StringComparison.Ordinal)) && (string)C30Value(c, f, "chara.face.head") == "illusion.hs2#0",
            "the preset writes face members only; face.head = illusion.hs2#0", string.Join(",", changed.Take(8)));
        c.Check(W1UndoCount(c) == (changed.Count > 0 ? n0 + 1 : n0) && (changed.Count == 0 || W1TopLabel(c) == "Face preset"), "one entry \"Face preset\"", W1UndoCount(c) + " " + W1TopLabel(c));
        c.Fails(new { cmd = "chara.edit.preset", key = f, head = "illusion.hs2#987654" }, "not_found", "a head without a preset → not_found");
        c.Fails(new { cmd = "chara.edit.preset", key = f, head = "nonsense" }, "bad_args", "a head that does not parse → bad_args");
        if (changed.Count > 0) c.Ok(new { cmd = "undo" }, "undo the preset");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
    }

    // ---------------------------------------------------------------- chara.edit.accessory

    static void C30Accessory(Ctx c, string f)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        int accs0 = C30Strs(c.Call(new { cmd = "chara.edit.info", key = f })["parts"]?["accessories"]).Count;
        string row = (string)c.Call(new { cmd = "chara.edit.list", category = 351, sex = 1, limit = 1 })["rows"]?[0]?["ref"];
        if (row == null) { c.Warn("no row of category 351"); return; }
        var a = c.Ok(new { cmd = "chara.edit.accessory", key = f, op = "add", category = 351, @ref = row }, "accessory add 351 " + row);
        string id = (string)a["accessory"];
        c.Check(id != null && C30Has(a["changed"], "coord.accessories") && W1TopLabel(c) == "Add accessory" && W1UndoCount(c) == n0 + 1,
            "add: the new id, changed coord.accessories, one entry \"Add accessory\"", Ctx.Short(a.ToString()));
        c.Check((int)C30Value(c, f, "coord.accessories[\"" + id + "\"].category") == 351 && (string)C30Value(c, f, "coord.accessories[\"" + id + "\"].item") == row, "the document has it");
        var info = c.Ok(new { cmd = "chara.edit.info", key = f }, "chara.edit.info after the add");
        var accs = C30Strs(info["parts"]?["accessories"]).OfType<JObject>().ToList();
        c.Check(accs.Count == accs0 + 1 && accs.Any(x => (string)x["ref"] == row && (bool)x["loaded"]), "a built slot holds the new accessory", Ctx.Short(info["parts"]?["accessories"]?.ToString()));
        var mv = c.Ok(new { cmd = "chara.edit.accessory", key = f, op = "move", accessory = id, index = 0 }, "accessory move to 0");
        c.Check(W1TopLabel(c) == "Reorder accessories", "move: entry \"Reorder accessories\"", W1TopLabel(c));
        var cp = c.Ok(new { cmd = "chara.edit.accessory", key = f, op = "copy", from = id, parts = new[] { "slot" }, mirrorParent = true }, "accessory copy to a new slot (mirrored parent)");
        string id2 = (string)cp["accessory"];
        c.Check(id2 != null && id2 != id && W1TopLabel(c) == "Copy accessory", "copy: a new id, entry \"Copy accessory\"", Ctx.Short(cp.ToString()));
        var rm = c.Ok(new { cmd = "chara.edit.accessory", key = f, op = "remove", accessory = id2 }, "accessory remove the copy");
        c.Check(W1TopLabel(c) == "Remove accessory" && C30Value(c, f, "coord.accessories") is JArray al && al.All(x => (string)x["id"] != id2), "remove: entry \"Remove accessory\", the id is gone");
        c.Fails(new { cmd = "chara.edit.accessory", key = f, op = "remove", accessory = "nosuchid" }, "not_found", "remove a missing id → not_found");
        c.Fails(new { cmd = "chara.edit.accessory", key = f, op = "add", @ref = row }, "bad_args", "add without category → bad_args");
        c.Fails(new { cmd = "chara.edit.accessory", key = f, op = "add", category = 351, @ref = "illusion.hs2#987654" }, "not_found", "add an unlisted row → not_found");
        c.Fails(new { cmd = "chara.edit.accessory", key = f, op = "copy", from = id, parts = new[] { "all" } }, "bad_args", "copy an unknown part → bad_args");
        c.Fails(new { cmd = "chara.edit.accessory", key = f, op = "dance" }, "bad_args", "an unknown op → bad_args");
        c.Fails(new { cmd = "chara.edit.accessory", key = f, op = "move", accessory = id }, "bad_args", "move without index → bad_args");
        for (int i = 0; i < 4; i++) c.Ok(new { cmd = "undo" }, "undo accessory op " + (4 - i));
        c.Check(W1DocSha(c) == sha0 && W1UndoCount(c) == n0, "undo restores scene.doc");
        info = c.Ok(new { cmd = "chara.edit.info", key = f }, "chara.edit.info after undo");
        c.Check(C30Strs(info["parts"]?["accessories"]).Count == accs0, "the built slots are back", "" + C30Strs(info["parts"]?["accessories"]).Count);
    }

    // ---------------------------------------------------------------- chara.edit.info

    static void C30Info(Ctx c, string f, string item)
    {
        int n0 = W1UndoCount(c);
        var i = c.Ok(new { cmd = "chara.edit.info", key = f }, "chara.edit.info");
        var creators = (i["creators"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        var cn = creators.Select(x => (string)x["name"]).ToList();
        c.Check(cn.Contains("create_skin_body") && cn.Contains("create_skin_face") && creators.All(x => (int)x["width"] > 0 && (long)x["bytes"] > 0)
                && (long)i["creatorBytes"] == creators.Sum(x => (long)x["bytes"]) && (int)i["ownedMaterials"] > 0,
            "creators (skin body / face …, sizes, bytes), owned materials", string.Join(",", cn) + " owned " + i["ownedMaterials"]);
        var body = creators.FirstOrDefault(x => (string)x["name"] == "create_skin_body");
        if (body != null) c.Info("create_skin_body " + body["width"] + "x" + body["height"] + " " + body["bytes"] + " B, rebuilds " + body["rebuilds"]);
        c.Check(C30Strs(i["dirty"]).Count == 0 && i["inSession"]?.Type == JTokenType.Boolean && i["parts"]?["clothes"] is JArray cl && cl.Count == 8 && (i["parts"]?["hair"] as JArray)?.Count == 4,
            "dirty [], inSession, parts {head, skin, hair[4], clothes[8], accessories}", Ctx.Short(i["parts"]?.ToString()));
        var on = c.Ok(new { cmd = "chara.edit.info", key = f, session = true }, "chara.edit.info session true");
        c.Check((bool)on["inSession"], "session true: the editor is in session", Ctx.Short(on.ToString()));
        var off = c.Ok(new { cmd = "chara.edit.info", key = f, session = false }, "chara.edit.info session false");
        c.Check(!(bool)off["inSession"], "session false: ended");
        c.Check(W1UndoCount(c) == n0, "info and sessions record nothing");
        c.Fails(new { cmd = "chara.edit.info", key = item }, "wrong_kind", "chara.edit.info on an item → wrong_kind");
        c.Fails(new { cmd = "chara.edit.info", key = f, session = "yes" }, "bad_args", "session not a bool → bad_args");
    }

    // ---------------------------------------------------------------- batch

    static void C30Batch(Ctx c, string f)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        c.Ok(new
        {
            cmd = "batch",
            cmds = new object[]
            {
                new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.face.shape[10]"] = 0.9 } },
                new { cmd = "shape.set", key = f, bodyIndex = 4, bodyValue = 0.1 },
                new { cmd = "chara.edit.preset", key = f, head = "illusion.hs2#0" },
            },
        }, "a batch of chara.edit.set, shape.set, chara.edit.preset");
        c.Check(W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Batch", "one entry \"Batch\"", W1UndoCount(c) + " " + W1TopLabel(c));
        c.Ok(new { cmd = "undo" }, "undo the batch");
        c.Check(W1DocSha(c) == sha0, "one undo restores scene.doc");
    }
}
