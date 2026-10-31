// C34 of BridgeCoreTest: the bridge of the clothes pages and of the content catalog (package E of Phase 4.4), further
// methods of the partial BridgeCoreTest (-only C34), edit mode, headless: registration (CoreCommands.Phase44Names, every Core
// spec's help group = the group that registers it, help {group}); chara.edit.list 348 / 240 columns; chara.edit.clothes on
// BR-Chan and HERO107 (the kinds, the row, the facts / defaults / creators / materials / objects = ClothesReport's and the
// HS2 records of BR-Chan's top, the served copy, the kind argument, the masks block, read-only); chara.edit.clothesDefaults
// («初期カラー»: the three sets = the reply's defaults, one entry, errors, several keys); chara.edit.reset of the clothes
// members to the prefab defaults; chara.edit.coordinate (HS2 coordinate cards in the three modes: what each mode replaces
// and keeps, the name, the warnings, no rebuild, one entry and undo, the other sex refused or skipped, the sex of a card: its
// evidence, else its coordinate/<sex> folder, else the character's with coord.sexAssumed, errors);
// chara.edit.materials / .rtstats part clothes (+ kind); chara.edit.digest parts clothes (section objects, which sections an
// edit changes); chara.edit.info clothes (facts, lastFlush: the creators a gloss / a colour edit rebuilds, the swap of an item
// change, the object reset of an option toggle) and {session: true} on a character that is not built (unavailable, nothing
// opened); content.redirect (the census's paths: the kimono top, the body, a HairMod hair path, a mod row on a path two
// zipmods ship; the row form; every name of a path; the summary; errors) and content.zipmods (the census's counts, the
// filters, one GUID, errors); a batch is one entry. C1–C33 unchanged. Numbers only; the SFW targets only (nothing captured).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HS2Next.Chara.Edit;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class BridgeCoreTest
{
    static readonly string[] C34Kinds = { "top", "bottom", "innerTop", "innerBottom", "gloves", "pantyhose", "socks", "shoes" };

    /// <summary>The SFW coordinate cards of the clothes comparisons (vanilla items; the female one has one accessory).</summary>
    const string C34FemaleCoord = "ガーリーブラウス.png", C34MaleCoord = "作業着.png";

    static void C34(Ctx c)
    {
        var app = c.Host();
        C34Registration(c, app);
        c.Ok(new { cmd = "scene.new" }, "scene.new");
        string f = C30Load(c, c.FemaleRel, "female", "chara.load BR-Chan");
        string m = C30Load(c, c.MaleRel, "male", "chara.load HERO107");
        if (f == null || m == null) return;
        string item = W1AddItem(c);
        c.Frames(2);
        C34List(c);
        C34Clothes(c, f, m, item);
        C34Defaults(c, f, m);
        C34Reset(c, f);
        C34Coordinate(c, f, m);
        C34Materials(c, f);
        C34RtStats(c, f);
        C34Digest(c, f);
        C34Info(c, f, m, app);
        C34Redirect(c);
        C34Zipmods(c);
        C34Batch(c, f);
    }

    // ---------------------------------------------------------------- helpers

    static string C34J(JToken t) => t?.ToString(Newtonsoft.Json.Formatting.None) ?? "null";
    static List<JObject> C34Objs(JToken t) => (t as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
    static JObject C34Kind(Ctx c, string key, object kind) => C34Objs(c.Call(new { cmd = "chara.edit.clothes", key, kind })["kinds"]).FirstOrDefault();
    static bool C34Warned(JObject r, string code) => (r?["warnings"] as JArray)?.Any(w => ((string)w)?.Contains(code) == true) == true;
    static float[] C34F(JToken t) => (t as JArray)?.Select(x => x.Type == JTokenType.Null ? float.NaN : (float)x).ToArray() ?? new float[0];
    static bool C34Near(float[] a, float[] b, float tol = 1e-6f) => a.Length == b.Length && a.Zip(b, (x, y) => Math.Abs(x - y) <= tol).All(x => x);
    static string C34S(float[] a) => "(" + string.Join(", ", a.Select(x => x.ToString("R", CultureInfo.InvariantCulture))) + ")";
    static HS2Next.Chara.CharaContext C34Ctx(Ctx c, string key) { c.App.Runtime.ApplyNow(); return c.App.Runtime.FindChara(key)?.Context; }
    static int C34Rebuilds(Ctx c, string key) => (int?)c.Call(new { cmd = "chara.edit.info", key })["editRebuilds"] ?? -1;
    static string C34CoordPath(Ctx c, string sex, string name) => Path.Combine(c.App.Paths.Hs2UserData ?? "", "coordinate", sex, name);

    // ---------------------------------------------------------------- registration and groups

    static void C34Registration(Ctx c, HS2Next.App.AppCore app)
    {
        var reg = app.BridgeServices.Commands;
        var names = HS2Next.App.Commands.CoreCommands.Phase44Names;
        c.Check(names.SequenceEqual(HS2Next.App.Commands.CharaEditCommands.Phase44Names.Concat(HS2Next.App.Commands.ContentCommands.Names))
                && names.SequenceEqual(new[] { "chara.edit.clothes", "chara.edit.clothesDefaults", "chara.edit.coordinate", "content.redirect", "content.zipmods" })
                && names.All(n => reg.Find(n) != null), "Phase44Names = chara.edit.clothes, .clothesDefaults, .coordinate, content.redirect, content.zipmods, all registered",
            string.Join(", ", names.Where(n => reg.Find(n) == null)));
        c.Check(HS2Next.App.Commands.CoreCommands.AllNames.Count(n => names.Contains(n)) == 5, "CoreCommands.AllNames lists them once");
        foreach (var n in names)
        {
            var h = c.Ok(new { cmd = "help", name = n }, "help " + n);
            c.Check(h.ToString().Contains(n), "help describes " + n, Ctx.Short(c.LastText));
        }
        foreach (var (cmd, word) in new[] { ("chara.edit.materials", "clothes"), ("chara.edit.rtstats", "clothes"), ("chara.edit.digest", "objects"), ("chara.edit.info", "creatorsRebuilt"), ("chara.edit.reset", "prefab"), ("ui.action", "edit.show") })
        {
            var h = c.Ok(new { cmd = "help", name = cmd }, "help " + cmd);
            c.Check(h.ToString().Contains(word), cmd + " documents " + word, Ctx.Short(c.LastText));
        }
        // the help groups: every Core spec names the group that registers it
        c.Check(HS2Next.App.Commands.CoreCommands.RegistrationErrors.Count == 0, "no registration error (no group mismatch)", string.Join("; ", HS2Next.App.Commands.CoreCommands.RegistrationErrors));
        var groups = HS2Next.App.Commands.CoreCommands.Groups;
        var core = reg.Specs.Where(x => x.Owner == HS2Next.Api.Bridge.BridgeOwners.Core).ToList();
        var outside = core.Where(x => !groups.Contains(x.Group)).Select(x => x.Name + " (" + x.Group + ")").ToList();
        c.Check(outside.Count == 0, "every Core spec's group is one of CoreCommands.Groups (" + groups.Count + ")", string.Join(", ", outside.Take(10)));
        var empty = groups.Where(g => core.All(x => x.Group != g)).ToList();
        c.Check(empty.Count == 0, "every group of CoreCommands.Groups has commands", string.Join(", ", empty));
        var expect = new (string cmd, string group)[]
        {
            ("scene", "scene"), ("maps", "map/item"), ("capture", "camera"), ("screenshot", "capture"), ("app.options", "app"), ("chara.load", "chara"),
            ("fk.list", "fk/pose"), ("chara.motion", "motion"), ("bones.dump", "dumps"), ("item.state", "items"), ("item.anime", "item motion"),
            ("map.state", "maps"), ("light.add", "lights/cameras/sound"), ("effects.set", "effects"), ("chara.clothes", "ui scene"), ("ui.state", "ui"),
            ("scene.pause", "time"), ("route.add", "routes"), ("shape.info", "chara edit"), ("chara.edit.clothes", "chara edit"), ("content.redirect", "content"),
        };
        var wrong = expect.Where(e => reg.Find(e.cmd)?.Group != e.group).Select(e => e.cmd + ": " + reg.Find(e.cmd)?.Group).ToList();
        c.Check(wrong.Count == 0, "the groups of one command per group (scene, map/item, camera, capture, …, chara edit, content)", string.Join(", ", wrong));
        var edit = HS2Next.App.Commands.CharaEditCommands.Names.Concat(HS2Next.App.Commands.CharaEditCommands.Phase41Names).Concat(HS2Next.App.Commands.CharaEditCommands.Phase42Names)
            .Concat(HS2Next.App.Commands.CharaEditCommands.Phase43Names).Concat(HS2Next.App.Commands.CharaEditCommands.Phase44Names).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var he = c.Ok(new { cmd = "help", group = "chara edit" }, "help {group: \"chara edit\"}");
        var hen = (he["commands"] as JArray)?.Select(x => (string)x["name"]).OrderBy(x => x, StringComparer.Ordinal).ToList() ?? new List<string>();
        // later phases add to the group (4.5: C35 pins the whole list): here the 22 commands of 4.0–4.4, all listed, and nothing
        // else besides the later phases' lists
        var later = new HashSet<string>(HS2Next.App.Commands.CharaEditCommands.Phase45Names.Concat(HS2Next.App.Commands.CharaEditCommands.Phase46Names));
        var hen44 = hen.Where(x => !later.Contains(x)).ToList();
        c.Check(hen44.SequenceEqual(edit) && edit.Count == 22, "help {group: \"chara edit\"} lists the 22 chara edit commands of 4.0–4.4 (and only later phases' others)", hen.Count + ": " + string.Join(", ", hen44.Except(edit).Concat(edit.Except(hen44))));
        var hc = c.Ok(new { cmd = "help", group = "content" }, "help {group: \"content\"}");
        var hcn = (hc["commands"] as JArray)?.Select(x => (string)x["name"]).OrderBy(x => x, StringComparer.Ordinal).ToList() ?? new List<string>();
        c.Check(hcn.Contains("content.redirect") && hcn.Contains("content.zipmods") && hcn.All(n => n.StartsWith("content.", StringComparison.Ordinal)),
            "help {group: \"content\"} lists content.redirect, content.zipmods (and the content commands of later phases)", string.Join(", ", hcn));
        c.Fails(new { cmd = "help", group = "chara" + ".edit" }, "not_found", "help {group: \"chara.edit\"} (no such group) → not_found");
    }

    // ---------------------------------------------------------------- chara.edit.list 348 / 240

    static void C34List(Ctx c)
    {
        int n0 = W1UndoCount(c);
        var l = c.Ok(new { cmd = "chara.edit.list", category = 348, sex = "female", columns = new[] { "MainTexAB", "MainTex" }, limit = 100 }, "chara.edit.list 348 columns MainTexAB / MainTex");
        var rows = C34Objs(l["rows"]);
        var r1 = rows.FirstOrDefault(r => (string)r["ref"] == "illusion.hs2#1");
        c.Check(r1 != null && (string)r1["values"]?["MainTex"] == "pattern01" && !string.IsNullOrEmpty((string)r1["values"]?["MainTexAB"]),
            "348 row illusion.hs2#1: MainTex pattern01 with its bundle (HS2's pattern row 1)", Ctx.Short(C34J(r1)));
        var r0 = rows.FirstOrDefault(r => (string)r["ref"] == "illusion.hs2#0");
        c.Check(r0 != null, "348 lists the none row illusion.hs2#0", "" + rows.Count);
        int vanilla = (int?)c.Call(new { cmd = "chara.edit.list", category = 348, sex = "female", search = "illusion.hs2#", limit = 1 })["total"] ?? -1;
        c.Info("348: " + l["total"] + " rows, " + vanilla + " matching \"illusion.hs2#\"");
        var t = c.Ok(new { cmd = "chara.edit.list", category = 240, sex = "female", columns = new[] { "Coordinate", "MainAB", "MainData" }, limit = 200 }, "chara.edit.list 240 columns Coordinate / MainAB / MainData");
        var t1 = C34Objs(t["rows"]).FirstOrDefault(r => (string)r["ref"] == "illusion.hs2#1");
        var t223 = C34Objs(c.Call(new { cmd = "chara.edit.list", category = 240, sex = "female", columns = new[] { "MainAB", "MainData" }, search = "illusion.hs2#223", limit = 5 })["rows"])
            .FirstOrDefault(r => (string)r["ref"] == "illusion.hs2#223");
        c.Check(t1 != null && (string)t1["values"]?["Coordinate"] == "1", "240 row #1 «ワンピース»: Coordinate 1 (one-piece; census)", Ctx.Short(C34J(t1)));
        c.Check(t223 != null && (string)t223["values"]?["MainAB"] == "chara/30/fo_top_30.unity3d" && (string)t223["values"]?["MainData"] == "p_cf_top_dougi",
            "240 row #223 «道着»: chara/30/fo_top_30.unity3d p_cf_top_dougi (the kimono top of the redirect)", Ctx.Short(C34J(t223)));
        var male = c.Ok(new { cmd = "chara.edit.list", category = 140, sex = "male", limit = 5 }, "chara.edit.list 140 (male tops)");
        c.Check((int?)male["total"] > 0 && (int?)male["category"] == 140, "140 lists rows", "" + male["total"]);
        c.Check(W1UndoCount(c) == n0, "lists record nothing");
    }

    // ---------------------------------------------------------------- chara.edit.clothes

    static void C34Clothes(Ctx c, string f, string m, string item)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var r = c.Ok(new { cmd = "chara.edit.clothes", key = f }, "chara.edit.clothes BR-Chan");
        var kinds = C34Objs(r["kinds"]);
        c.Check(kinds.Count == 8 && Enumerable.Range(0, 8).All(k => (string)kinds[k]["kind"] == C34Kinds[k] && (int?)kinds[k]["index"] == k && (int?)kinds[k]["category"] == 240 + k),
            "8 kinds top … shoes, index 0..7, female categories 240..247", Ctx.Short(C34J(r)));
        // HS2's member order first (the HS2 side's chara.clothesInfo), then HS2Next's
        var order = new[] { "kind", "index", "category", "item", "realized", "object", "active", "activeInHierarchy", "skipped", "state", "stateKinds", "row", "facts", "creators", "defaults", "objects", "materials", "rowRef", "prefab", "onePiece", "served" };
        var props = kinds.FirstOrDefault()?.Properties().Select(p => p.Name).ToList() ?? new List<string>();
        c.Check(props.SequenceEqual(order), "kind members in the HS2 side's order, then rowRef, prefab, onePiece, served", string.Join(", ", props));
        c.Check(r["flags"] is JObject fl && fl["notBot"]?.Type == JTokenType.Boolean && fl["underMaskReflectionType"]?.Type == JTokenType.Integer
                && r["masks"] is JObject mk && mk.Properties().Select(p => p.Name).SequenceEqual(new[] { "body", "bra", "innerTB", "innerB", "panst" })
                && r["maskRenderers"] is JObject mr && mr.Properties().Select(p => p.Name).SequenceEqual(new[] { "bra", "innerTB", "innerB", "panst" })
                && (bool?)r["subMenu"]?["bottom"] == true && (bool?)r["male"] == false,
            "flags, masks {body, bra, innerTB, innerB, panst}, maskRenderers, subMenu.bottom (two-piece top), male false", Ctx.Short(C34J(new JObject { ["flags"] = r["flags"], ["masks"] = r["masks"], ["subMenu"] = r["subMenu"] })));
        c.Check(r["masks"]?["body"] is JObject body && body["_alpha_a"]?.Type == JTokenType.Float || r["masks"]?["body"]?["_alpha_a"]?.Type == JTokenType.Integer,
            "masks.body has _alpha_a (the body draw material)", Ctx.Short(C34J(r["masks"]?["body"])));
        // BR-Chan's top = the HS2 record's (240#205 «コンシェルジュスーツF», WC2): row, facts, defaults
        var top = kinds.Count == 8 ? kinds[0] : null;
        var row = top?["row"] as JObject;
        c.Check((string)top?["item"] == "illusion.hs2#205" && (bool?)top?["realized"] == true && (bool?)top?["skipped"] == false && (string)top?["object"] != null,
            "top: item illusion.hs2#205, realized, not skipped, an object", Ctx.Short(C34J(top)));
        c.Check(row != null && (int?)row["id"] == 205 && (string)row["MainAB"] == "chara/30/fo_top_30.unity3d" && (string)row["MainData"] == "p_cf_top_h2mcon"
                && (string)row["Coordinate"] == "0" && (string)row["MainTex"] == "cf_top_h2main2_t" && (string)row["ColorMaskTex"] == "cf_top_h2main2_mc",
            "top row: id 205, MainAB chara/30/fo_top_30.unity3d, MainData p_cf_top_h2mcon, Coordinate 0, MainTex / ColorMaskTex (= HS2's record)", Ctx.Short(C34J(row)));
        var facts = top?["facts"] as JObject;
        bool fOk = facts != null && (bool?)facts["hasCmpClothes"] == true && (facts["useColorN"] as JArray)?.Count == 3 && (facts["useColorA"] as JArray)?.Count == 3
                   && facts["useColorN"].Select(x => (bool)x).SequenceEqual(new[] { true, true, true }) && facts["useColorA"].Select(x => (bool)x).SequenceEqual(new[] { false, false, false })
                   && (bool?)facts["useBreak"] == false && (int?)facts["objOpt01"] == 0 && (int?)facts["objOpt02"] == 0
                   && (bool?)facts["objTopDef"] == true && (bool?)facts["objTopHalf"] == true && (bool?)facts["objBotDef"] == true && (bool?)facts["objBotHalf"] == true
                   && C34Near(C34F(facts["uvScalePattern"]), new[] { 1f, 1f, 0f, 0f });
        c.Check(fOk, "top facts = HS2's record (useColorN 1 1 1, useColorA 0 0 0, no break, no options, the four def / half objects, uvScalePattern (1, 1, 0, 0))", Ctx.Short(C34J(facts)));
        var g0 = C34Objs(facts?["groups"]);
        c.Check(g0.Count == 3 && (int?)g0[0]["renderers"] == 4 && (int?)g0[0]["slots"] == 4 && C34Near(C34F(g0[0]["mainTex"]), new[] { 2048f, 2048f }) && (int?)g0[1]["renderers"] == 0,
            "top groups: 4 renderers / 4 slots / 2048² in group 1, none in 2 / 3 (= HS2's record)", Ctx.Short(C34J(facts?["groups"])));
        var d = C34Objs(top?["defaults"]);
        c.Check(d.Count == 3 && Math.Abs((float)d[0]["gloss"] - 0.206f) <= 1e-6f && Math.Abs((float)d[0]["metallic"] - 0.35f) <= 1e-6f
                && C34Near(C34F(d[0]["layout"]), new[] { 0.5f, 0.5f, 0.5f, 0.5f }) && Math.Abs((float)d[0]["rotation"] - 0.5f) <= 1e-6f && (string)d[0]["pattern"] == "illusion.hs2#0",
            "top defaults set 1: gloss 0.206, metallic 0.35, layout 0.5 ×4, rotation 0.5, no pattern (HS2's ↺ of WH3)", Ctx.Short(C34J(d.FirstOrDefault())));
        // equal to the Unity-side report
        var ctx = C34Ctx(c, f);
        bool same = ctx != null;
        string why = "";
        for (int k = 0; same && k < 8; k++)
        {
            var p = ClothesReport.KindOf(ctx, k);
            var x = kinds[k];
            var fx = x["facts"] as JObject;
            same &= (string)x["item"] == p.Item && (bool?)x["realized"] == p.Realized && (bool?)x["skipped"] == p.Skipped && (string)x["prefab"] == p.Prefab
                    && (string)x["rowRef"] == p.Row && (bool?)fx?["hasCmpClothes"] == p.HasCmpClothes;
            if (same && p.HasCmpClothes)
            {
                same &= fx["useColorN"].Select(v => (bool)v).SequenceEqual(p.UseColorN) && fx["useColorA"].Select(v => (bool)v).SequenceEqual(p.UseColorA) && (bool?)fx["useBreak"] == p.UseBreak
                        && (int?)fx["objOpt01"] == p.Opt01 && (int?)fx["objOpt02"] == p.Opt02;
                int creators = p.Groups.Sum(g => (g.Colour != null ? 1 : 0) + (g.Detail != null ? 1 : 0));
                same &= C34Objs(x["creators"]).Count == creators;
                var dd = C34Objs(x["defaults"]);
                for (int s = 0; same && s < 3; s++)
                    same &= Math.Abs((float)dd[s]["gloss"] - p.Defaults[s].Gloss) == 0 && Math.Abs((float)dd[s]["metallic"] - p.Defaults[s].Metallic) == 0 && (string)dd[s]["pattern"] == p.Defaults[s].Pattern;
            }
            int slots = 0;
            var go = ctx.Clothes != null && k < ctx.Clothes.Length ? ctx.Clothes[k] : null;
            if (go) foreach (var rd in go.GetComponentsInChildren<Renderer>(true)) if (rd) slots += rd.sharedMaterials.Length;
            same &= C34Objs(x["materials"]).Count == slots;
            if (!same) why = C34Kinds[k] + ": " + Ctx.Short(C34J(x));
        }
        c.Check(same, "every kind = ClothesReport.KindOf (item, realized, skipped, prefab, row, facts, creators, defaults) and materials = every slot under the part", why);
        // the creators and the materials as written
        var cr = C34Objs(top?["creators"]);
        c.Check(cr.Count == 2 && (string)cr[0]["creator"] == "colour" && (int?)cr[0]["group"] == 0 && (string)cr[1]["creator"] == "detail"
                && C34Near(C34F(cr[0]["rt"]), new[] { 2048f, 2048f }) && cr[0]["props"]?["_Color"] != null && cr[1]["props"]?["_Glossiness"] != null && ((string)cr[0]["name"])?.StartsWith("create_clothes") == true,
            "top creators: colour then detail of group 0, RT 2048², the create materials' _Color / _Glossiness, the HS2Next names", Ctx.Short(C34J(top?["creators"])));
        var mats = C34Objs(top?["materials"]);
        var g1 = mats.Where(x => (int?)x["group"] == 1).ToList();
        c.Check(g1.Count == 4 && g1.All(x => (int?)x["slot"] == 0 && (string)x["props"]?["_MainTex"]?["creator"] == "colour 0" && (string)x["props"]?["_DetailMainTex"]?["creator"] == "detail 0"
                && x["props"]?["_AlphaEx"] != null && x.Properties().Select(p => p.Name).Take(10).SequenceEqual(new[] { "renderer", "group", "slot", "slots", "enabled", "active", "material", "shader", "queue", "keywords" })),
            "top materials: the 4 group-1 slots draw _MainTex / _DetailMainTex = colour 0 / detail 0 (HS2's record), _AlphaEx listed, HS2's member order", Ctx.Short(C34J(g1.FirstOrDefault())));
        var ob = top?["objects"] as JObject;
        c.Check(ob != null && ob.Properties().Select(p => p.Name).SequenceEqual(new[] { "topDef", "topHalf", "botDef", "botHalf", "opt1", "opt2" }) && (bool?)ob["topDef"] == true && (bool?)ob["topHalf"] == false,
            "top objects {topDef true, topHalf false, botDef, botHalf, opt1, opt2} (dressed)", Ctx.Short(C34J(ob)));
        c.Check((int?)top?["state"] == 0 && top?["stateKinds"]?.Select(x => (int)x).SequenceEqual(new[] { 0, 1, 2 }) == true, "top state 0, stateKinds [0, 1, 2]", Ctx.Short(C34J(top?["stateKinds"])));
        bool redirect = ContentCatalog.SideloaderAssetRedirect;
        c.Check((string)top?["served"]?["MainData"]?["kind"] == "abdata" && top?["served"]?["MainData"]?["guid"]?.Type == JTokenType.Null,
            "top served.MainData = abdata (p_cf_top_h2mcon is no zipmod copy's name; redirect " + (redirect ? "on" : "off") + ")", Ctx.Short(C34J(top?["served"])));
        // a kind without CmpClothes: facts {hasCmpClothes: false} only
        var none = kinds.FirstOrDefault(x => (bool?)x["facts"]?["hasCmpClothes"] == false);
        if (none != null)
            c.Check(((JObject)none["facts"]).Properties().Count() == 1 && none["defaults"]?.Type == JTokenType.Null && C34Objs(none["creators"]).Count == 0,
                "a kind without CmpClothes (" + none["kind"] + "): facts {hasCmpClothes: false} only, defaults null, no creators", Ctx.Short(C34J(none)));
        else c.Info("BR-Chan wears a CmpClothes on every kind");
        // the kind argument
        var one = c.Ok(new { cmd = "chara.edit.clothes", key = f, kind = "innerTop" }, "chara.edit.clothes kind innerTop");
        c.Check(C34Objs(one["kinds"]).Count == 1 && (string)one["kinds"][0]["kind"] == "innerTop" && JToken.DeepEquals(one["kinds"][0], kinds[2]), "kind innerTop: that kind only, equal to the all-kinds reply's");
        var byIndex = c.Ok(new { cmd = "chara.edit.clothes", key = f, kind = 7 }, "chara.edit.clothes kind 7");
        c.Check((string)byIndex["kinds"]?[0]?["kind"] == "shoes", "kind 7 = shoes");
        var noServed = c.Ok(new { cmd = "chara.edit.clothes", key = f, kind = 0, served = false }, "chara.edit.clothes served false");
        c.Check(noServed["kinds"]?[0]?["served"] == null, "served false: no served member");
        c.Fails(new { cmd = "chara.edit.clothes", key = f, kind = "hat" }, "bad_args", "kind \"hat\" → bad_args");
        c.Fails(new { cmd = "chara.edit.clothes", key = f, kind = 8 }, "bad_args", "kind 8 → bad_args");
        c.Fails(new { cmd = "chara.edit.clothes", key = f, kind = 1.5 }, "bad_args", "kind 1.5 → bad_args");
        c.Fails(new { cmd = "chara.edit.clothes", key = item }, "wrong_kind", "an item key → wrong_kind");
        c.Fails(new { cmd = "chara.edit.clothes" }, "bad_args", "no key → bad_args");
        // the male character: four Maker kinds, the stored female-list ids of the others still realized as stored
        var mr2 = c.Ok(new { cmd = "chara.edit.clothes", key = m }, "chara.edit.clothes HERO107");
        var mk2 = C34Objs(mr2["kinds"]);
        c.Check((bool?)mr2["male"] == true && mk2.Count == 8 && Enumerable.Range(0, 8).All(k => (int?)mk2[k]["category"] == ClothesSet.MaleCategories[k]),
            "male: categories 140, 141, 242, 243, 144, 245, 246, 147", string.Join(", ", mk2.Select(x => (string)x["category"])));
        c.Check((bool?)mk2.ElementAtOrDefault(0)?["facts"]?["hasCmpClothes"] == true && ((string)mk2.ElementAtOrDefault(0)?["item"])?.StartsWith("illusion.hs2#") == true,
            "HERO107's top: a vanilla row with a CmpClothes", Ctx.Short(C34J(mk2.ElementAtOrDefault(0)?["item"])));
        c.Check(W1UndoCount(c) == n0 && W1DocSha(c) == sha0, "chara.edit.clothes records and changes nothing");
    }

    // ---------------------------------------------------------------- chara.edit.clothesDefaults («初期カラー»)

    static void C34Defaults(Ctx c, string f, string m)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var defaults = C34Objs(C34Kind(c, f, "top")?["defaults"]);
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["coord.clothes.top.colors[0].gloss"] = 0.9, ["coord.clothes.top.colors[1].layout"] = new[] { 0.1, 1.7, -0.3, 0.2 } } }, "set top gloss 0.9 and a layout outside 0..1");
        int n1 = W1UndoCount(c);
        var r = c.Ok(new { cmd = "chara.edit.clothesDefaults", key = f, kind = "top" }, "chara.edit.clothesDefaults top");
        c.Check(W1UndoCount(c) == n1 + 1 && W1TopLabel(c) == "Edit clothes", "one entry \"Edit clothes\"", W1UndoCount(c) - n1 + " " + W1TopLabel(c));
        var colors = C30Value(c, f, "coord.clothes.top.colors") as JArray;
        bool ok = colors != null && colors.Count == 3 && defaults.Count == 3;
        for (int s = 0; ok && s < 3; s++)
        {
            var cs = colors[s] as JObject; var ds = defaults[s];
            ok &= C34Near(C34F(cs?["base"]), C34F(ds["base"]), 0) && (string)cs?["pattern"] == (string)ds["pattern"] && C34Near(C34F(cs?["patternColor"]), C34F(ds["patternColor"]), 0)
                  && C34Near(C34F(cs?["layout"]), C34F(ds["layout"]), 0) && (float?)cs?["rotation"] == (float)ds["rotation"] && (float?)cs?["gloss"] == (float)ds["gloss"] && (float?)cs?["metallic"] == (float)ds["metallic"];
        }
        c.Check(ok, "the three sets = chara.edit.clothes defaults bit for bit (gloss 0.9 and the layout back to the prefab's)", Ctx.Short(C34J(colors)));
        c.Check(C30Strs(r["changed"]).Any(x => ((string)x).StartsWith("coord.clothes.top.colors")), "changed names coord.clothes.top.colors…", Ctx.Short(C34J(r["changed"])));
        var again = c.Ok(new { cmd = "chara.edit.clothesDefaults", key = f, kind = 0 }, "again (kind 0): nothing to change");
        c.Check(C30Strs(again["changed"]).Count == 0 && W1UndoCount(c) == n1 + 1, "a second «初期カラー» changes nothing and records nothing", Ctx.Short(C34J(again)));
        c.Ok(new { cmd = "undo" }, "undo «初期カラー»");
        c.Check(Math.Abs(((float?)C30Value(c, f, "coord.clothes.top.colors[0].gloss") ?? 0) - 0.9f) < 1e-7f, "undo restores gloss 0.9");
        c.Ok(new { cmd = "undo" }, "undo the set");
        c.Check(W1DocSha(c) == sha0 && W1UndoCount(c) == n0, "undo restores scene.doc");
        c.Fails(new { cmd = "chara.edit.clothesDefaults", key = f }, "bad_args", "no kind → bad_args");
        c.Fails(new { cmd = "chara.edit.clothesDefaults", key = f, kind = "hat" }, "bad_args", "kind \"hat\" → bad_args");
        c.Fails(new { cmd = "chara.edit.clothesDefaults", key = f, kind = -1 }, "bad_args", "kind -1 → bad_args");
        // a kind without the prefab's defaults (a none row): unsupported alone, skipped with a warning among several keys
        var kinds = C34Objs(c.Call(new { cmd = "chara.edit.clothes", key = f, served = false })["kinds"]);
        var none = kinds.FirstOrDefault(x => x["defaults"]?.Type == JTokenType.Null);
        if (none != null)
        {
            c.Fails(new { cmd = "chara.edit.clothesDefaults", key = f, kind = (string)none["kind"] }, "unsupported", "«初期カラー» of a kind without a prefab (" + none["kind"] + ") → unsupported");
            c.Check(W1UndoCount(c) == n0, "… and nothing recorded");
        }
        else c.Warn("BR-Chan has no kind without a prefab: the unsupported case is not checked");
        var both = c.Ok(new { cmd = "chara.edit.clothesDefaults", keys = new[] { f, m }, kind = "top" }, "«初期カラー» on BR-Chan and HERO107 (top)");
        c.Check(W1UndoCount(c) <= n0 + 1, "several keys: at most one entry", "" + (W1UndoCount(c) - n0));
        if (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo");
        c.Check(W1DocSha(c) == sha0, "scene.doc back to before");
    }

    // ---------------------------------------------------------------- chara.edit.reset: the clothes prefab defaults

    static void C34Reset(Ctx c, string f)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var d = C34Objs(C34Kind(c, f, "top")?["defaults"]);
        if (d.Count != 3) { c.Check(false, "top defaults for the reset checks"); return; }
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object>
        {
            ["coord.clothes.top.colors[0].gloss"] = 0.9, ["coord.clothes.top.colors[0].metallic"] = 0.8, ["coord.clothes.top.colors[0].rotation"] = 0.1,
            ["coord.clothes.top.colors[0].layout"] = new[] { 0.2, 0.3, 0.4, 0.6 }, ["coord.clothes.top.breakRate"] = 0.4,
        } }, "set top gloss, metallic, rotation, layout, breakRate");
        var r = c.Ok(new { cmd = "chara.edit.reset", key = f, paths = new[] { "coord.clothes.top.colors[0].gloss", "coord.clothes.top.colors[0].metallic", "coord.clothes.top.colors[0].rotation", "coord.clothes.top.colors[0].layout#1", "coord.clothes.top.breakRate" } },
            "chara.edit.reset gloss, metallic, rotation, layout#1, breakRate");
        c.Check(W1TopLabel(c) == "Reset value", "one entry \"Reset value\"", W1TopLabel(c));
        float G(string p) => (float?)C30Value(c, f, p) ?? float.NaN;
        var lay = C34F(C30Value(c, f, "coord.clothes.top.colors[0].layout"));
        c.Check(G("coord.clothes.top.colors[0].gloss") == (float)d[0]["gloss"] && G("coord.clothes.top.colors[0].metallic") == (float)d[0]["metallic"]
                && G("coord.clothes.top.colors[0].rotation") == (float)d[0]["rotation"],
            "gloss, metallic, rotation ← the worn prefab's defaults (" + d[0]["gloss"] + ", " + d[0]["metallic"] + ", " + d[0]["rotation"] + ")",
            G("coord.clothes.top.colors[0].gloss") + ", " + G("coord.clothes.top.colors[0].metallic") + ", " + G("coord.clothes.top.colors[0].rotation"));
        c.Check(lay.Length == 4 && lay[0] == 0.2f && lay[1] == (float)d[0]["layout"][1] && lay[2] == 0.4f && lay[3] == 0.6f, "layout#1 ← the prefab's height only, the other components kept", C34S(lay));
        c.Check(G("coord.clothes.top.breakRate") == 0f, "breakRate ← 0");
        c.Check(!C34Warned(r, "edit.defaultsUnavailable"), "no edit.defaultsUnavailable warning (the prefab is known)", Ctx.Short(C34J(r["warnings"])));
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (reset block)");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
    }

    // ---------------------------------------------------------------- chara.edit.coordinate

    static void C34Coordinate(Ctx c, string f, string m)
    {
        string full = C34CoordPath(c, "female", C34FemaleCoord), fullM = C34CoordPath(c, "male", C34MaleCoord);
        if (!File.Exists(full) || !File.Exists(fullM)) { c.Warn("the SFW coordinate cards are missing (" + full + "): coordinate checks skipped"); return; }
        var found = new HS2Next.Save.FindingList();
        var src = c.App.Ops.LoadCoordinate(full, found, out bool imported, out var fail);
        if (!c.Check(src != null && imported, "the female coordinate card reads through the importer", fail?.Message)) return;
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var acc0 = C30Value(c, f, "coord.accessories");
        var clothes0 = C30Value(c, f, "coord.clothes");
        int rb0 = C34Rebuilds(c, f);
        // clothes: the eight parts and the name, the accessories kept
        var r = c.Ok(new { cmd = "chara.edit.coordinate", key = f, file = C34FemaleCoord, mode = "clothes" }, "chara.edit.coordinate clothes «" + Path.GetFileNameWithoutExtension(C34FemaleCoord) + "»");
        c.Check(W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Load coordinate", "one entry \"Load coordinate\"", (W1UndoCount(c) - n0) + " " + W1TopLabel(c));
        c.Check((string)r["source"]?["file"] == Path.GetFullPath(full) && (int?)r["source"]?["sex"] == 1 && (string)r["source"]?["name"] == src.Coord.Name && (string)r["source"]?["mode"] == "clothes" && (bool?)r["imported"] == true,
            "source {file (absolute), sex 1, name, mode clothes}, imported", Ctx.Short(C34J(r)));
        c.Check(C34Warned(r, "coord.extKept"), "warning coord.extKept (the coordinate's plugin data not loaded in this mode)", Ctx.Short(C34J(r["warnings"])));
        var items = Enumerable.Range(0, 8).Select(k => (string)C30Value(c, f, "coord.clothes." + C34Kinds[k] + ".item")).ToList();
        c.Check(items.SequenceEqual(Enumerable.Range(0, 8).Select(k => src.Coord.Clothes.Parts[k].Item)), "the eight items = the coordinate's", string.Join(", ", items));
        c.Check(JToken.DeepEquals(C30Value(c, f, "coord.accessories"), acc0), "the accessories kept");
        c.Check((string)C30Value(c, f, "coord.name") == src.Coord.Name, "the name follows the coordinate (HS2's three buttons)", (string)C30Value(c, f, "coord.name"));
        var info = c.Call(new { cmd = "chara.edit.info", key = f });
        c.Check((int?)info["editRebuilds"] == rb0 && (bool?)info["rebuildRequested"] == false, "no rebuild: editRebuilds unchanged, rebuildRequested false (per-part swaps)", info["editRebuilds"] + " vs " + rb0);
        c.Ok(new { cmd = "undo" }, "undo the clothes load");
        c.Check(W1DocSha(c) == sha0 && JToken.DeepEquals(C30Value(c, f, "coord.clothes"), clothes0), "undo restores scene.doc and the clothes");
        // accessories: the list replaced, the clothes kept
        var ra = c.Ok(new { cmd = "chara.edit.coordinate", key = f, file = full, mode = "accessories" }, "chara.edit.coordinate accessories (absolute path)");
        var accItems = (C30Value(c, f, "coord.accessories") as JArray)?.Select(x => (string)x["item"]).ToList() ?? new List<string>();
        c.Check(accItems.SequenceEqual(src.Coord.Accessories.Select(a => a.Item)) && JToken.DeepEquals(C30Value(c, f, "coord.clothes"), clothes0),
            "accessories = the coordinate's (" + src.Coord.Accessories.Count + "), the clothes kept", string.Join(", ", accItems));
        c.Check(C34Warned(ra, "coord.extKept") && W1TopLabel(c) == "Load coordinate", "warning coord.extKept, one entry");
        c.Ok(new { cmd = "undo" }, "undo the accessories load");
        // all: both
        var rall = c.Ok(new { cmd = "chara.edit.coordinate", key = f, file = C34FemaleCoord }, "chara.edit.coordinate (mode all by default)");
        var all = Enumerable.Range(0, 8).Select(k => (string)C30Value(c, f, "coord.clothes." + C34Kinds[k] + ".item")).ToList();
        var allAcc = (C30Value(c, f, "coord.accessories") as JArray)?.Select(x => (string)x["item"]).ToList() ?? new List<string>();
        c.Check((string)rall["source"]?["mode"] == "all" && all.SequenceEqual(Enumerable.Range(0, 8).Select(k => src.Coord.Clothes.Parts[k].Item)) && allAcc.SequenceEqual(src.Coord.Accessories.Select(a => a.Item))
                && !C34Warned(rall, "coord.extKept"), "all: clothes and accessories = the coordinate's, no coord.extKept", Ctx.Short(C34J(rall)));
        c.Check(C34Rebuilds(c, f) == rb0, "all: no rebuild either");
        c.Ok(new { cmd = "undo" }, "undo the all load");
        c.Check(W1DocSha(c) == sha0 && W1UndoCount(c) == n0, "undo restores scene.doc");
        // the other sex, several keys, the male coordinate
        c.Fails(new { cmd = "chara.edit.coordinate", key = m, file = full }, "bad_args", "a female coordinate on HERO107 → bad_args (same sex only)");
        C34CoordinateSex(c, f, m, full, fullM, sha0, n0);
        var both = c.Ok(new { cmd = "chara.edit.coordinate", keys = new[] { f, m }, file = C34FemaleCoord, mode = "clothes" }, "the female coordinate on BR-Chan and HERO107");
        c.Check(C34Warned(both, "edit.coordinateSkipped") && W1UndoCount(c) == n0 + 1, "HERO107 skipped with edit.coordinateSkipped, one entry", Ctx.Short(C34J(both["warnings"])));
        c.Ok(new { cmd = "undo" }, "undo");
        c.Check(W1DocSha(c) == sha0, "scene.doc back to before");
        // errors
        c.Fails(new { cmd = "chara.edit.coordinate", key = f, file = "no such coordinate.png" }, "not_found", "a missing card → not_found");
        c.Fails(new { cmd = "chara.edit.coordinate", key = f, file = "none.hs2ncoord" }, "not_found", "a missing .hs2ncoord → not_found");
        c.Fails(new { cmd = "chara.edit.coordinate", key = f, file = C34FemaleCoord, mode = "hair" }, "bad_args", "mode \"hair\" → bad_args");
        c.Fails(new { cmd = "chara.edit.coordinate", key = f }, "bad_args", "no file → bad_args");
        c.Fails(new { cmd = "chara.edit.coordinate", key = f, file = "../x.png" }, "io_denied", "a relative path leaving the folder → io_denied");
        c.Check(W1UndoCount(c) == n0, "the errors record nothing");
    }

    /// <summary>The sex of an HS2 coordinate card to be worn: the card's evidence (a CoordinateSex block, resolve entries) wins;
    /// without it the folder coordinate/male or coordinate/female it lies in decides, else the character's sex with the warning
    /// coord.sexAssumed.</summary>
    static void C34CoordinateSex(Ctx c, string f, string m, string full, string fullM, string sha0, int n0)
    {
        // the vanilla male coordinate (no evidence) in coordinate/male: male
        var srcM = c.App.Ops.LoadCoordinate(fullM, new HS2Next.Save.FindingList(), out bool impM, out var failM, 0);
        if (!c.Check(srcM != null && impM && srcM.Coord.Sex == 0, "the vanilla male coordinate «" + Path.GetFileNameWithoutExtension(C34MaleCoord) + "» (no sex evidence) reads as male from its folder coordinate/male",
                failM?.Message ?? "sex " + srcM?.Coord.Sex)) return;
        c.Fails(new { cmd = "chara.edit.coordinate", key = f, file = fullM }, "bad_args", "the male coordinate on BR-Chan → bad_args (same sex only)");
        var clothes0 = C30Value(c, m, "coord.clothes");
        var rm = c.Ok(new { cmd = "chara.edit.coordinate", key = m, file = C34MaleCoord, mode = "clothes" }, "the male coordinate «" + Path.GetFileNameWithoutExtension(C34MaleCoord) + "» on HERO107 (relative to the male folder)");
        var items = Enumerable.Range(0, 8).Select(k => (string)C30Value(c, m, "coord.clothes." + C34Kinds[k] + ".item")).ToList();
        c.Check((int?)rm["source"]?["sex"] == 0 && !C34Warned(rm, "coord.sexAssumed") && W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Load coordinate",
            "male source (from the folder, no coord.sexAssumed), one entry \"Load coordinate\"", Ctx.Short(C34J(rm)));
        c.Check(items.SequenceEqual(Enumerable.Range(0, 8).Select(k => srcM.Coord.Clothes.Parts[k].Item)) && !JToken.DeepEquals(C30Value(c, m, "coord.clothes"), clothes0),
            "HERO107's eight clothes items = the coordinate's", string.Join(", ", items));
        c.Ok(new { cmd = "undo" }, "undo the male load");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
        // no coordinate/<sex> folder: the character's sex, with the warning coord.sexAssumed
        string plainDir = Path.Combine(c.Out, "e44coord", "plain");
        Directory.CreateDirectory(plainDir);
        string plain = Path.Combine(plainDir, "plain.png");
        File.Copy(full, plain, true);
        foreach (var (key, sex, who) in new[] { (m, 0, "HERO107"), (f, 1, "BR-Chan") })
        {
            var rp = c.Ok(new { cmd = "chara.edit.coordinate", key, file = plain, mode = "clothes" }, "the female card «" + Path.GetFileNameWithoutExtension(C34FemaleCoord) + "» outside a coordinate/<sex> folder on " + who);
            c.Check((int?)rp["source"]?["sex"] == sex && C34Warned(rp, "coord.sexAssumed"), who + ": the character's sex (" + sex + ") with the warning coord.sexAssumed", Ctx.Short(C34J(rp)));
            c.Ok(new { cmd = "undo" }, "undo");
        }
        c.Check(W1DocSha(c) == sha0, "scene.doc back to before");
        // evidence wins over the folder: a female card whose import finds sex evidence, put into a coordinate/male folder
        var importer = ImportSetup.ImporterFor(c.Services.Catalog) as HS2Next.Import.Hs2Importer;
        string femaleDir = Path.GetDirectoryName(full), evidence = null;
        int tried = 0;
        if (importer != null && Directory.Exists(femaleDir))
            foreach (var p in Directory.GetFiles(femaleDir, "*.png").OrderBy(x => x, StringComparer.Ordinal))
            {
                if (++tried > 300) break;
                var res = importer.ImportCoord(p);
                if (res == null || !res.Ok || res.Container?.Coord.Sex != 1) continue;
                var counts = res.Report.Counts;
                if (counts.ContainsKey("coord.sex.coordinateSex") || counts.ContainsKey("coord.sex.resolveInfo")) { evidence = p; break; }
            }
        if (evidence == null) { c.Warn("no female coordinate card with sex evidence among " + tried + ": the evidence case is not checked"); return; }
        c.Info("evidence card: the " + tried + "th female coordinate card in name order (its import finds sex evidence)");
        string maleDir = Path.Combine(c.Out, "e44coord", "coordinate", "male");
        Directory.CreateDirectory(maleDir);
        string inMale = Path.Combine(maleDir, "evidence.png");
        File.Copy(evidence, inMale, true);
        var ev = c.App.Ops.LoadCoordinate(inMale, new HS2Next.Save.FindingList(), out _, out _, 0);
        c.Check(ev != null && ev.Coord.Sex == 1, "a female card with evidence inside coordinate/male still reads as female (the evidence wins)", "sex " + ev?.Coord.Sex);
        c.Fails(new { cmd = "chara.edit.coordinate", key = m, file = inMale, mode = "clothes" }, "bad_args", "… so HERO107 refuses it (bad_args)");
        var rf = c.Ok(new { cmd = "chara.edit.coordinate", key = f, file = inMale, mode = "clothes" }, "… and BR-Chan wears it");
        c.Check((int?)rf["source"]?["sex"] == 1 && !C34Warned(rf, "coord.sexAssumed"), "female source, no coord.sexAssumed", Ctx.Short(C34J(rf["source"])));
        c.Ok(new { cmd = "undo" }, "undo");
        c.Check(W1DocSha(c) == sha0 && W1UndoCount(c) == n0, "scene.doc and the history back to before");
        try { File.Delete(plain); File.Delete(inMale); } catch (Exception) { }
    }

    // ---------------------------------------------------------------- chara.edit.materials / rtstats part clothes

    static void C34Materials(Ctx c, string f)
    {
        var r = c.Ok(new { cmd = "chara.edit.materials", key = f, part = "clothes" }, "chara.edit.materials part clothes");
        var rs = C34Objs(r["renderers"]);
        var ctx = C34Ctx(c, f);
        int want = 0;
        if (ctx?.Clothes != null) foreach (var go in ctx.Clothes) if (go) want += go.GetComponentsInChildren<Renderer>(true).Count(x => x);
        c.Check(rs.Count == want && want > 0 && rs.All(x => C34Kinds.Contains((string)x["kind"])), "every renderer under the clothes kind objects (" + want + "), each with its kind", rs.Count + "");
        var kindOrder = rs.Select(x => Array.IndexOf(C34Kinds, (string)x["kind"])).ToList();
        c.Check(kindOrder.Zip(kindOrder.Skip(1), (a, b) => a <= b).All(x => x), "kind order top … shoes");
        var slot = rs.SelectMany(x => C34Objs(x["slots"])).FirstOrDefault(s => s["props"]?["_MainTex"] != null);
        c.Check(slot != null && slot["props"]?["_MainTex_ST"] is JArray st && st.Count == 4, "texture properties followed by \"<name>_ST\" [4]", Ctx.Short(C34J(slot?["props"])));
        var top = c.Ok(new { cmd = "chara.edit.materials", key = f, part = "clothes", kind = "top" }, "kind top");
        var tr = C34Objs(top["renderers"]);
        c.Check(tr.Count > 0 && tr.All(x => (string)x["kind"] == "top") && tr.Count == rs.Count(x => (string)x["kind"] == "top"), "kind top: the top's renderers only", "" + tr.Count);
        c.Fails(new { cmd = "chara.edit.materials", key = f, kind = "top" }, "bad_args", "kind without part clothes → bad_args");
        c.Fails(new { cmd = "chara.edit.materials", key = f, part = "clothes", kind = "hat" }, "bad_args", "kind \"hat\" → bad_args");
        c.Fails(new { cmd = "chara.edit.materials", key = f, part = "shoes" }, "bad_args", "part \"shoes\" → bad_args");
        var body = c.Ok(new { cmd = "chara.edit.materials", key = f, part = "body" }, "part body unchanged");
        c.Check(C34Objs(body["renderers"]).All(x => x["kind"] == null), "part body: no kind member");
    }

    static void C34RtStats(Ctx c, string f)
    {
        c.Fails(new { cmd = "chara.edit.rtstats", key = f, kind = "top" }, "bad_args", "rtstats kind without part clothes → bad_args");
        c.Fails(new { cmd = "chara.edit.rtstats", key = f, part = "clothes", kind = "hat" }, "bad_args", "rtstats kind \"hat\" → bad_args");
        if (!HS2Next.Chara.Edit.FaceDigest.HasDevice)
        {
            c.Fails(new { cmd = "chara.edit.rtstats", key = f, part = "clothes", kind = "top" }, "unavailable", "part clothes without a graphics device → unavailable");
            return;
        }
        var r = c.Ok(new { cmd = "chara.edit.rtstats", key = f, part = "clothes", kind = "top", grid = 4 }, "rtstats part clothes kind top");
        var cr = C34Objs(r["creators"]);
        c.Check(cr.Count == 2 && (string)cr[0]["name"] == "colour" && (string)cr[1]["name"] == "detail" && cr.All(x => (string)x["kind"] == "top" && (int?)x["group"] == 0 && x["sha256"] != null),
            "top: colour then detail of group 0 with kind, group, sha256", Ctx.Short(C34J(r)));
    }

    // ---------------------------------------------------------------- chara.edit.digest parts clothes

    static void C34Digest(Ctx c, string f)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var d0 = c.Ok(new { cmd = "chara.edit.digest", key = f, parts = "clothes", text = true }, "chara.edit.digest parts clothes");
        var ctx = C34Ctx(c, f);
        c.Check((string)d0["parts"] == "clothes" && ((string)d0["objects"])?.Length == 64 && ((string)d0["materials"])?.Length == 64 && ((string)d0["creators"])?.Length == 64
                && (string)d0["bones"] == HS2Next.Chara.Edit.FaceDigest.Sha256(""),
            "sections materials, creators, objects (SHA-256); bones = the empty text's hash (the clothes have no bones)", Ctx.Short(C34J(d0)));
        c.Check((string)d0["texts"]?["objects"] == HS2Next.Chara.Edit.FaceDigest.ObjectsText(ctx, "clothes") && ((string)d0["texts"]?["objects"])?.Contains("clothes0") == true,
            "texts.objects = FaceDigest.ObjectsText (per kind)", Ctx.Short((string)d0["texts"]?["objects"]));
        c.Check(d0["chains"] == null && d0["normals"] == null, "no chains / normals sections for clothes");
        var all = c.Ok(new { cmd = "chara.edit.digest", key = f, parts = "all" }, "parts all");
        c.Check(all["objects"] != null && all["chains"] != null && all["normals"] != null, "parts all includes objects (and chains, normals)");
        // a gloss edit changes creators (not objects); a clothes state change changes objects
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["coord.clothes.top.colors[0].gloss"] = 0.77 } }, "top gloss 0.77");
        var d1 = c.Call(new { cmd = "chara.edit.digest", key = f, parts = "clothes" });
        c.Check((string)d1["creators"] != (string)d0["creators"] && (string)d1["objects"] == (string)d0["objects"], "gloss: creators change, objects do not");
        c.Ok(new { cmd = "chara.clothes", key = f, states = new { top = 1 } }, "top half (chara.clothes)");
        var d2 = c.Call(new { cmd = "chara.edit.digest", key = f, parts = "clothes" });
        c.Check((string)d2["objects"] != (string)d1["objects"], "the top's state: objects change", (string)d2["objects"]);
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (digest block)");
        var d3 = c.Call(new { cmd = "chara.edit.digest", key = f, parts = "clothes" });
        c.Check(W1DocSha(c) == sha0 && (string)d3["objects"] == (string)d0["objects"] && (string)d3["creators"] == (string)d0["creators"], "undo: scene.doc and the clothes digest as before");
        c.Fails(new { cmd = "chara.edit.digest", key = f, parts = "shoes" }, "bad_args", "parts \"shoes\" → bad_args");
    }

    // ---------------------------------------------------------------- chara.edit.info clothes and the session check

    static void C34Info(Ctx c, string f, string m, HS2Next.App.AppCore app)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var i = c.Ok(new { cmd = "chara.edit.info", key = f }, "chara.edit.info BR-Chan");
        var ck = C34Objs(i["clothes"]?["kinds"]);
        var cl = C34Objs(c.Call(new { cmd = "chara.edit.clothes", key = f, served = false })["kinds"]);
        c.Check(ck.Count == 8 && cl.Count == 8 && Enumerable.Range(0, 8).All(k => (string)ck[k]["kind"] == C34Kinds[k] && (int?)ck[k]["index"] == k && JToken.DeepEquals(ck[k]["facts"], cl[k]["facts"])
                && (string)ck[k]["item"] == (string)cl[k]["item"] && (string)ck[k]["prefab"] == (string)cl[k]["prefab"]),
            "clothes.kinds: kind, index, item, prefab and facts = chara.edit.clothes's", Ctx.Short(C34J(i["clothes"])));
        // a gloss edit rebuilds the detail creators only, a colour edit the colour creators only
        foreach (var (path, value, colour, detail, what) in new (string, object, bool, bool, string)[]
        {
            ("coord.clothes.top.colors[0].gloss", 0.3, false, true, "a gloss edit"),
            ("coord.clothes.top.colors[0].base", new[] { 0.5, 0.4, 0.3, 1.0 }, true, false, "a main colour edit"),
        })
        {
            c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { [path] = value } }, what);
            var x = c.Call(new { cmd = "chara.edit.info", key = f });
            var runs = C34Objs(x["clothes"]?["lastFlush"]?["creatorsRebuilt"]);
            c.Check(runs.Count > 0 && runs.All(rr => (string)rr["kind"] == "top" && (bool?)rr["colour"] == colour && (bool?)rr["detail"] == detail),
                what + ": lastFlush.creatorsRebuilt = the top's " + (colour ? "colour" : "detail") + " creators only", Ctx.Short(C34J(x["clothes"]?["lastFlush"])));
        }
        // an item change swaps; an option toggle resets the objects without a swap
        c.Ok(new { cmd = "chara.edit.item", key = f, path = "coord.clothes.top.item", @ref = "illusion.hs2#51" }, "top → 240#51 «クリスマスポンチョ» (option parts)");
        var sw = c.Call(new { cmd = "chara.edit.info", key = f })["clothes"]?["lastFlush"];
        c.Check((bool?)sw?["clothesSwapped"]?[0] == true && (bool?)sw?["objectsReset"]?[0] == false, "item change: clothesSwapped[0] true, objectsReset[0] false", Ctx.Short(C34J(sw)));
        var opt = C34Kind(c, f, "top")?["facts"];
        if ((int?)opt?["objOpt01"] > 0)
        {
            c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["coord.clothes.top.hideOptions[0]"] = true } }, "hide option part 1");
            var ot = c.Call(new { cmd = "chara.edit.info", key = f })["clothes"]?["lastFlush"];
            c.Check((bool?)ot?["objectsReset"]?[0] == true && (bool?)ot?["clothesSwapped"]?[0] == false, "option toggle: objectsReset[0] true without a swap", Ctx.Short(C34J(ot)));
            var o1 = C34Kind(c, f, "top")?["objects"]?["opt1"] as JArray;
            c.Check(o1 != null && o1.Count > 0 && o1.All(v => v.Type == JTokenType.Boolean && !(bool)v), "objects.opt1 inactive", Ctx.Short(C34J(o1)));
        }
        else c.Warn("240#51 shows no option part: the option toggle case is not checked");
        var im = c.Ok(new { cmd = "chara.edit.info", key = m }, "chara.edit.info HERO107");
        c.Check(C34Objs(im["clothes"]?["kinds"]).Count == 8, "male: clothes.kinds 8");
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (info block)");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
        // {session: true} on a character that is not built: unavailable, and no session left open
        var nb = app.Session.AddChara(null);
        if (c.Check(nb != null && nb.Ok, "a character without a card (not built)", nb?.ToString()))
        {
            app.Runtime.ApplyNow();
            string nid = nb.NewId;
            c.Fails(new { cmd = "chara.edit.info", key = nid, session = true }, "unavailable", "chara.edit.info {session: true} on it → unavailable");
            c.Check(!CharaEditSessions.Instance.IsActive(nid), "… and no edit session was opened");
            c.Fails(new { cmd = "chara.edit.info", key = nid }, "unavailable", "chara.edit.info on it → unavailable");
            c.Ok(new { cmd = "delete", keys = new[] { nid } }, "delete it");
        }
        var s = c.Ok(new { cmd = "chara.edit.info", key = f, session = true }, "{session: true} on BR-Chan (built)");
        c.Check((bool?)s["inSession"] == true && CharaEditSessions.Instance.IsActive(f), "the session opens on a built character");
        c.Ok(new { cmd = "chara.edit.info", key = f, session = false }, "{session: false}");
        c.Check(!CharaEditSessions.Instance.IsActive(f), "the session closes");
    }

    // ---------------------------------------------------------------- content.redirect

    static void C34Redirect(Ctx c)
    {
        var cat = c.Services.Catalog;
        bool on = ContentCatalog.SideloaderAssetRedirect;
        c.Info("switches: redirect " + on + ", zipmod set " + ContentCatalog.SideloaderZipmodSet);
        var sum = c.Ok(new { cmd = "content.redirect" }, "content.redirect (summary)");
        var paths = C30Strs(sum["paths"]).Select(x => (string)x).ToList();
        c.Check((int?)sum["redirectable"] == paths.Count && paths.Count == (cat.Redirect?.RedirectablePaths.Count ?? -1) && (bool?)sum["switch"]?["redirect"] == on,
            "summary: the redirectable paths = the catalog's index, switch states", paths.Count + "");
        if (paths.Count == 96) c.Check(true, "96 redirectable paths (the census: 18 abdata paths a zipmod ships + 78 shipped by several zipmods)");
        else c.Warn("redirectable paths " + paths.Count + " (the census counted 96 with the zipmod loader's set; zipmod set " + ContentCatalog.SideloaderZipmodSet + ")");
        c.Check((int?)sum["withAbdata"] == 18 || !ContentCatalog.SideloaderZipmodSet, "18 of them with an abdata file (with the zipmod loader's set)", "" + sum["withAbdata"]);
        // the kimono top: Terable Kimonos' copy holds p_cf_top_dougi
        var k = c.Ok(new { cmd = "content.redirect", path = "chara/30/fo_top_30.unity3d", asset = "p_cf_top_dougi" }, "content.redirect kimono top 240#223");
        var kc = C34Objs(k["copies"]);
        c.Check((bool?)k["redirectable"] == true && (bool?)k["hasAbdata"] == true && kc.Count >= 2 && kc[0]["guid"]?.Type == JTokenType.Null && kc.Skip(1).Any(x => (bool?)x["has"] == true),
            "fo_top_30: redirectable, the abdata file first, a zipmod copy holding p_cf_top_dougi", Ctx.Short(C34J(k)));
        string kimonoGuid = (string)kc.Skip(1).FirstOrDefault(x => (bool?)x["has"] == true)?["guid"];
        c.Check(on ? (string)k["served"]?["kind"] == "zipmod" && (string)k["served"]?["guid"] == kimonoGuid : (string)k["served"]?["kind"] == "abdata",
            "served: " + (on ? "the zipmod copy (" + kimonoGuid + ")" : "abdata (redirect off)"), Ctx.Short(C34J(k["served"])));
        c.Check((string)k["today"]?["kind"] == "abdata" && kc.All(x => x["cab"] != null && x["compression"] != null), "today abdata (a fixed path); copies with cab and compression", Ctx.Short(C34J(k["today"])));
        // the same through the list row's column
        var kr = c.Ok(new { cmd = "content.redirect", row = new { category = 240, @ref = "illusion.hs2#223", column = "MainData" } }, "content.redirect row 240#223 MainData");
        c.Check((string)kr["path"] == "chara/30/fo_top_30.unity3d" && (string)kr["asset"] == "p_cf_top_dougi" && (string)kr["row"]?["ab"] == "MainAB"
                && JToken.DeepEquals(kr["served"], k["served"]) && (string)kr["today"]?["kind"] == "abdata",
            "row form: path and asset from MainAB / MainData, the same served copy, today = the vanilla row's abdata", Ctx.Short(C34J(kr)));
        // the body: HS2_UncensorSelector Base holds p_cf_body_00, not p_cf_anim
        var b = c.Ok(new { cmd = "content.redirect", path = "chara/oo_base.unity3d", asset = "p_cf_body_00" }, "content.redirect chara/oo_base p_cf_body_00");
        var anim = c.Ok(new { cmd = "content.redirect", path = "chara/oo_base.unity3d", asset = "p_cf_anim" }, "content.redirect chara/oo_base p_cf_anim");
        c.Check((bool?)b["redirectable"] == true && C34Objs(b["copies"]).Skip(1).Any(x => (bool?)x["has"] == true) && C34Objs(anim["copies"]).Skip(1).All(x => (bool?)x["has"] != true)
                && (string)anim["served"]?["kind"] == "abdata" && (string)b["served"]?["kind"] == (on ? "zipmod" : "abdata"),
            "the body: a zipmod copy holds p_cf_body_00 (served " + (on ? "zipmod" : "abdata") + "), none holds p_cf_anim (abdata)", Ctx.Short(C34J(b["served"]) + " / " + C34J(anim["served"])));
        // a HairMod hair path: every name of the path (no asset) served by the zipmod copy when the redirect is on
        string hair = paths.FirstOrDefault(p => p.StartsWith("chara/38/", StringComparison.Ordinal) && p.Contains("hair")) ?? "chara/38/so_hair_b_38.unity3d";
        var h = c.Ok(new { cmd = "content.redirect", path = hair }, "content.redirect " + hair + " (every name)");
        var assets = C34Objs(h["assets"]);
        c.Check((bool?)h["redirectable"] == true && assets.Count > 0 && h["asset"]?.Type == JTokenType.Null && C34Objs(h["copies"]).All(x => x["has"]?.Type == JTokenType.Null)
                && assets.All(a => (string)a["served"]?["kind"] == (on ? "zipmod" : "abdata")),
            hair + ": " + assets.Count + " names, each served by " + (on ? "the zipmod copy" : "abdata") + "; has null without an asset", Ctx.Short(C34J(h)));
        // a mod row on a path two zipmods ship (H's RH2: the earliest loaded copy serves)
        var pr = c.Call(new { cmd = "content.redirect", row = new { category = 241, @ref = "fffq.pants2#100", column = "MainData" } });
        if ((bool?)pr["ok"] == true)
        {
            c.Check((bool?)pr["redirectable"] == true && C34Objs(pr["copies"]).Count >= 2 && string.Equals((string)pr["today"]?["guid"], "fffq.pants2", StringComparison.OrdinalIgnoreCase)
                    && (on ? !string.Equals((string)pr["served"]?["guid"], "fffq.pants2", StringComparison.OrdinalIgnoreCase) : JToken.DeepEquals(pr["served"], pr["today"])),
                "241 fffq.pants2#100: two zipmod copies, today its own zipmod, served " + (on ? "the earlier loaded copy (RH2)" : "= today (redirect off)"), Ctx.Short(C34J(pr)));
        }
        else c.Warn("241 fffq.pants2#100 not installed: the multi-zipmod row case is skipped (" + Ctx.Short(c.LastText) + ")");
        // a path no zipmod ships
        var plain = c.Ok(new { cmd = "content.redirect", path = "chara/00/fo_top_00.unity3d", asset = "p_cf_top_t_00" }, "content.redirect of a path no zipmod ships");
        c.Check((bool?)plain["redirectable"] == false && (string)plain["served"]?["kind"] == (string)plain["today"]?["kind"], "not redirectable: served = today", Ctx.Short(C34J(plain)));
        c.Check(sum["cabClashes"] != null && k["cabClashes"] is JArray && k["loads"]?.Type == JTokenType.Integer, "cabClashes and loads reported");
        // errors
        c.Fails(new { cmd = "content.redirect", asset = "p_cf_anim" }, "bad_args", "asset without path → bad_args");
        c.Fails(new { cmd = "content.redirect", path = "chara/oo_base.unity3d", row = new { category = 240, @ref = "illusion.hs2#1", column = "MainData" } }, "bad_args", "path and row → bad_args");
        c.Fails(new { cmd = "content.redirect", row = new { category = 240, @ref = "illusion.hs2#99999", column = "MainData" } }, "not_found", "an unknown row → not_found");
        c.Fails(new { cmd = "content.redirect", row = new { category = 240, @ref = "nope", column = "MainData" } }, "bad_args", "a bad ref → bad_args");
        c.Fails(new { cmd = "content.redirect", row = new { category = 240, @ref = "illusion.hs2#1", column = "NoSuchColumn" } }, "bad_args", "an empty column → bad_args");
    }

    // ---------------------------------------------------------------- content.zipmods

    static void C34Zipmods(Ctx c)
    {
        var cat = c.Services.Catalog;
        bool set = ContentCatalog.SideloaderZipmodSet;
        var r = c.Ok(new { cmd = "content.zipmods" }, "content.zipmods");
        var zs = C34Objs(r["zipmods"]);
        int withGuid = cat.ScannedZipmods.Count(z => z.Guid != null);
        c.Check((int?)r["scanned"] == withGuid && (int?)r["loaded"] == cat.ZipmodsByGuid.Count && (int?)r["total"] == zs.Count && zs.Count == (int?)r["guids"]
                && zs.Count(z => (bool)z["loaded"]) == cat.ZipmodsByGuid.Count,
            "scanned files, GUIDs, loaded GUIDs = the catalog's (" + withGuid + " files, " + r["guids"] + " GUIDs, " + r["loaded"] + " loaded)", Ctx.Short(C34J(new JObject { ["scanned"] = r["scanned"], ["guids"] = r["guids"], ["loaded"] = r["loaded"], ["skipped"] = r["skipped"] })));
        c.Check(withGuid == 5938, "5,938 zipmod files with a GUID (the census)", "" + withGuid);
        c.Check((int?)r["notForHs2"] == 9, "9 files whose <game> values name no HS2 game (the census)", "" + r["notForHs2"]);
        c.Check((int?)r["several"] == 5, "5 GUIDs with several files the game filter keeps (the census)", "" + r["several"]);
        if (set)
            c.Check((int?)r["skipped"]?["game"] == 9 && (int?)r["differs"] == 4 && (int?)r["loaded"] == 5924, "zipmod set on: 9 skipped by the game filter, 4 version choices differ from the last scanned file, 5,924 GUIDs load (the census)",
                Ctx.Short(C34J(r["skipped"]) + " differs " + r["differs"] + " loaded " + r["loaded"]));
        else
            c.Check((int?)r["skipped"]?["game"] == 0 && (int?)r["skipped"]?["version"] == 0 && (int?)r["differs"] == 0, "zipmod set off: nothing skipped, the last file of a GUID kept", Ctx.Short(C34J(r["skipped"])));
        var order = zs.Where(z => (bool)z["loaded"]).Select(z => (string)z["guid"]).ToList();
        c.Check(order.SequenceEqual(cat.ZipmodsInLoadOrder.Select(z => z.Guid)), "the loaded GUIDs in load order");
        c.Check(zs.All(z => z["files"] is JArray fa && fa.Count >= 1 && (string)z["today"] == (string)fa.Last["file"]), "every entry lists its files; today = the last scanned file");
        var sev = c.Ok(new { cmd = "content.zipmods", filter = "several" }, "filter several");
        c.Check(C34Objs(sev["zipmods"]).Count == 5 && C34Objs(sev["zipmods"]).All(z => ((JArray)z["files"]).Count >= 2), "several: 5 GUIDs with ≥ 2 files the game filter keeps", "" + sev["total"]);
        var sk = c.Ok(new { cmd = "content.zipmods", filter = "skipped" }, "filter skipped");
        c.Check(C34Objs(sk["zipmods"]).All(z => ((JArray)z["files"]).Any(x => x["skipped"]?.Type == JTokenType.String)) && (set ? (int?)sk["total"] > 0 : (int?)sk["total"] == 0),
            "skipped: GUIDs with a skipped file (" + sk["total"] + ")", Ctx.Short(C34J(sk["zipmods"])));
        var not = zs.Where(z => (bool?)z["forHs2"] == false).ToList();
        c.Info("GUIDs whose kept file names no HS2 game: " + string.Join(", ", not.Select(z => (string)z["guid"])));
        string g = (string)zs.FirstOrDefault()?["guid"];
        var one = c.Ok(new { cmd = "content.zipmods", guid = g?.ToUpperInvariant() }, "one GUID (case ignored)");
        c.Check((int?)one["total"] == 1 && string.Equals((string)one["zipmods"]?[0]?["guid"], g, StringComparison.OrdinalIgnoreCase), "guid: that GUID only");
        var page = c.Ok(new { cmd = "content.zipmods", offset = 10, limit = 3 }, "offset 10, limit 3");
        c.Check(C34Objs(page["zipmods"]).Select(z => (string)z["guid"]).SequenceEqual(zs.Skip(10).Take(3).Select(z => (string)z["guid"])), "paging");
        c.Fails(new { cmd = "content.zipmods", guid = "no.such.guid.e44" }, "not_found", "an unknown GUID → not_found");
        c.Fails(new { cmd = "content.zipmods", filter = "old" }, "bad_args", "filter \"old\" → bad_args");
        c.Fails(new { cmd = "content.zipmods", limit = 0 }, "bad_args", "limit 0 → bad_args");
    }

    // ---------------------------------------------------------------- batch

    static void C34Batch(Ctx c, string f)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        c.Ok(new
        {
            cmd = "batch",
            cmds = new object[]
            {
                new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["coord.clothes.top.colors[0].gloss"] = 0.1 } },
                new { cmd = "chara.edit.clothesDefaults", key = f, kind = "top" },
                new { cmd = "chara.edit.coordinate", key = f, file = C34FemaleCoord, mode = "clothes" },
                new { cmd = "chara.edit.clothes", key = f, kind = "top" },
                new { cmd = "content.redirect", path = "chara/oo_base.unity3d", asset = "p_cf_body_00" },
            },
        }, "a batch of chara.edit.set, clothesDefaults, coordinate, clothes, content.redirect");
        c.Check(W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Batch", "one entry \"Batch\"", W1UndoCount(c) + " " + W1TopLabel(c));
        c.Ok(new { cmd = "undo" }, "undo the batch");
        c.Check(W1DocSha(c) == sha0, "one undo restores scene.doc");
    }
}
