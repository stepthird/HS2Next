// C33 of BridgeCoreTest: the Phase 4.3 bridge of package E,
// further methods of the partial BridgeCoreTest (-only C33), edit mode, headless: registration (CoreCommands.Phase43Names);
// chara.edit.list 305 / 306 columns; chara.edit.hair on BR-Chan and HERO107 (the vanilla facts, limits, default rates and
// correction transforms = the HS2 side's records / the census, the facts = HairInfo's, the part argument, the male
// prefab variants of 300#22 / #59, read-only); chara.edit.hairBundle (set move / rotate / noShake with the transforms and
// the chain step's kind, rates outside 0..1 stored as given, «髪の調整をまとめて行う» per component, resets to the prefab
// default (one component, noShake, both), «全て初期化» keeping noShake, noop, the materialization after an item change and
// after a wrong stored count, several keys and a skipped node, errors, undo); chara.edit.hairSkin (the shown part
// only with same setting on, Unity's HSV arithmetic, stored alpha kept); chara.edit.swatch 305 (the six members of a row by
// id, same setting on / off, no auto derivation, stored alpha kept, errors); chara.edit.materials part hair (+ hairPart,
// "<name>_ST", the mesh pattern on the front hair, metallic direct); chara.edit.digest parts hair (sections, chains, texts,
// which sections follow which edits); chara.edit.info hair (facts, lastFlush.dynBone none / light / full); a batch is one
// entry. Groups C1–C32 unchanged. Numbers only; the SFW targets only (nothing is captured).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HS2Next.Content;
using HS2Next.Content.Maker;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class BridgeCoreTest
{
    static readonly string[] C33Parts = { "back", "front", "side", "option" };

    static void C33(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        var names = HS2Next.App.Commands.CoreCommands.Phase43Names;
        c.Check(names.SequenceEqual(HS2Next.App.Commands.CharaEditCommands.Phase43Names) && names.SequenceEqual(new[] { "chara.edit.hairBundle", "chara.edit.hairSkin", "chara.edit.hair" })
                && names.All(n => reg.Find(n) != null), "Phase43Names = chara.edit.hairBundle, chara.edit.hairSkin, chara.edit.hair, all registered", string.Join(", ", names.Where(n => reg.Find(n) == null)));
        c.Check(HS2Next.App.Commands.CoreCommands.AllNames.Count(n => names.Contains(n)) == 3, "CoreCommands.AllNames lists them once");
        foreach (var n in names)
        {
            var h = c.Ok(new { cmd = "help", name = n }, "help " + n);
            c.Check(h.ToString().Contains(n), "help describes " + n, Ctx.Short(c.LastText));
        }
        foreach (var (cmd, word) in new[] { ("chara.edit.materials", "hairPart"), ("chara.edit.digest", "chains"), ("chara.edit.swatch", "305"), ("chara.edit.info", "dynBone") })
        {
            var h = c.Ok(new { cmd = "help", name = cmd }, "help " + cmd);
            c.Check(h.ToString().Contains(word), cmd + " documents " + word, Ctx.Short(c.LastText));
        }
        c.Ok(new { cmd = "scene.new" }, "scene.new");
        string f = C30Load(c, c.FemaleRel, "female", "chara.load BR-Chan");
        string m = C30Load(c, c.MaleRel, "male", "chara.load HERO107");
        if (f == null || m == null) return;
        string item = W1AddItem(c);
        c.Frames(2);
        C33List(c);
        C33Hair(c, f, m, item);
        C33Bundle(c, f, m, item);
        C33Materialize(c, f, m);
        C33Skin(c, f, m, item);
        C33Swatch(c, f, m, item);
        C33Materials(c, f);
        C33Digest(c, f);
        C33Info(c, f, m);
        C33Batch(c, f);
    }

    // ---------------------------------------------------------------- helpers

    static float[] C33F(JToken t) => (t as JArray)?.Select(x => x.Type == JTokenType.Null ? float.NaN : (float)x).ToArray() ?? new float[0];
    static Vector3 C33V(JToken t) { var a = C33F(t); return a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : new Vector3(float.NaN, float.NaN, float.NaN); }
    static Quaternion C33Q(JToken t) { var a = C33F(t); return a.Length >= 4 ? new Quaternion(a[0], a[1], a[2], a[3]) : new Quaternion(float.NaN, float.NaN, float.NaN, float.NaN); }
    static float C33Max(float[] a, float[] b) => a.Length != b.Length ? float.PositiveInfinity : a.Zip(b, (x, y) => Math.Abs(x - y)).DefaultIfEmpty(0f).Max();
    static bool C33Eq(float[] a, float[] b) => a.Length == b.Length && a.Zip(b, (x, y) => x == y).All(x => x);
    static string C33S(float[] a) => "(" + string.Join(", ", a.Select(x => x.ToString("R", CultureInfo.InvariantCulture))) + ")";
    static string C33S(Vector3 v) => C33S(new[] { v.x, v.y, v.z });

    static JObject C33Part(Ctx c, string key, int k) => (c.Call(new { cmd = "chara.edit.hair", key, part = k })["parts"] as JArray)?.OfType<JObject>().FirstOrDefault();
    static List<JObject> C33Bundles(JObject part) => (part?["bundles"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
    static JToken C33Doc(Ctx c, string key, string member) => C30Value(c, key, "chara.hair." + member);
    static string C33DynBone(Ctx c, string key) => (string)c.Call(new { cmd = "chara.edit.info", key })["hair"]?["lastFlush"]?["dynBone"];

    /// <summary>A chara.edit.hairBundle request: key, part and name / value pairs.</summary>
    static Dictionary<string, object> C33Req(string key, object part, params object[] kv)
    {
        var d = new Dictionary<string, object> { ["cmd"] = "chara.edit.hairBundle", ["key"] = key, ["part"] = part };
        for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
        return d;
    }

    /// <summary>Two transform names equal but for underscores (the vanilla abdata copy of a hair prefab vs the zipmod copy HS2
    /// draws: "c_J_hairFLa_s_01" / "c_J_hair_FLa_s_01"; package A, H's shadowed-bundle facts).</summary>
    static bool C33SameName(string a, string b) => a != null && b != null && a.Replace("_", "") == b.Replace("_", "");

    /// <summary>LerpUnclamped per axis (the unit's ShapeEngine.RangeLerp, v0.2 / HH5).</summary>
    static Vector3 C33Lerp(Vector3 a, Vector3 b, Vector3 r) => new Vector3(a.x + (b.x - a.x) * r.x, a.y + (b.y - a.y) * r.y, a.z + (b.z - a.z) * r.z);

    /// <summary>The correction transform of a bundle reply equals the per-axis unclamped lerp of its limits at its effective
    /// rates (position within 1e-5, rotation within 1e-3°).</summary>
    static bool C33Pose(JObject b, out string detail)
    {
        var lim = b?["limits"];
        var eff = b?["effective"];
        var pos = C33Lerp(C33V(lim?["posMin"]), C33V(lim?["posMax"]), C33V(eff?["move"]));
        var rot = Quaternion.Euler(C33Lerp(C33V(lim?["rotMin"]), C33V(lim?["rotMax"]), C33V(eff?["rotate"])));
        var lp = C33V(b?["local"]?["position"]);
        var lq = C33Q(b?["local"]?["rotation"]);
        float dp = (lp - pos).magnitude, da = Quaternion.Angle(lq, rot);
        detail = "position " + C33S(lp) + " vs " + C33S(pos) + " (Δ " + dp + "), angle Δ " + da + "°";
        return dp <= 1e-5f && da <= 1e-3f;
    }

    // ---------------------------------------------------------------- chara.edit.list 305 / 306

    static void C33List(Ctx c)
    {
        var lists = MakerCatalog.For(c.Services.Catalog);
        int n0 = W1UndoCount(c);
        var cols = new[] { "BaseH", "BaseS", "BaseV", "TopH", "TopS", "TopV", "UnderH", "UnderS", "UnderV", "SpecularH", "SpecularS", "SpecularV", "Metallic", "Smoothness" };
        var l = c.Ok(new { cmd = "chara.edit.list", category = 305, sex = "female", columns = cols }, "chara.edit.list 305 columns Base* … Smoothness");
        var rows = (l["rows"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        bool ok = rows.Count == 7;
        for (int i = 0; ok && i < rows.Count; i++)
        {
            var v = rows[i]["values"] as JObject;
            ok &= (string)rows[i]["ref"] == "illusion.hs2#" + i && v != null && lists.HairColourPreset(ContentId.Vanilla(i), out HairPresetColumns hc)
                  && P(v["BaseH"]) == hc.Base.H && P(v["TopS"]) == hc.Top.S && P(v["UnderV"]) == hc.Under.V && P(v["SpecularH"]) == hc.Specular.H
                  && P(v["Metallic"]) == hc.Metallic && P(v["Smoothness"]) == hc.Smoothness;
        }
        c.Check(ok, "305: 7 rows illusion.hs2#0..6, values {BaseH … Smoothness} = MakerLists.HairColourPreset", Ctx.Short(rows.FirstOrDefault()?.ToString()));
        c.Check(rows.Count == 7 && (string)rows[5]["name"] == "金髪" && (string)rows[0]["name"] == "黒髪", "305 names (row 0 «黒髪», row 5 «金髪»)", rows.Count == 7 ? rows[0]["name"] + " / " + rows[5]["name"] : "");
        var mr = c.Ok(new { cmd = "chara.edit.list", category = 306, sex = "female", columns = new[] { "MainAB", "MainTex" } }, "chara.edit.list 306 columns MainAB / MainTex");
        var mrows = (mr["rows"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        bool mok = mrows.Count == 12 && (string)mrows[0]["ref"] == "illusion.hs2#0" && (string)mrows[0]["values"]?["MainAB"] == "0";
        for (int i = 1; mok && i < 12; i++) mok &= (string)mrows[i]["values"]?["MainTex"] == "mesh_t_" + i.ToString("00", CultureInfo.InvariantCulture);
        c.Check(mok, "306: 12 rows, row 0 MainAB \"0\" (none), rows 1..11 MainTex mesh_t_01 … mesh_t_11", Ctx.Short(mrows.FirstOrDefault()?.ToString()));
        c.Check(W1UndoCount(c) == n0, "lists record nothing");

        float P(JToken t) => t == null || t.Type == JTokenType.Null ? float.NaN : float.Parse((string)t, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    // ---------------------------------------------------------------- chara.edit.hair

    /// <summary>One vanilla bundle of the HS2 side's records (Cache/compare/maker/hair/hc1.json; = the census's prefab read).</summary>
    struct C33B
    {
        public int Index; public string Trf; public Vector3 PosMin, PosMax, RotMin, RotMax, Move, Rotate;
        public C33B(int i, string trf, Vector3 pmin, Vector3 pmax, Vector3 rmin, Vector3 rmax, Vector3 move, Vector3 rot)
        { Index = i; Trf = trf; PosMin = pmin; PosMax = pmax; RotMin = rmin; RotMax = rmax; Move = move; Rotate = rot; }
    }

    static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

    /// <summary>Checks a part of chara.edit.hair against HS2's record of the same vanilla row: prefab, flags, bundles (count,
    /// shown, titles, correction transforms, limits exactly, default rates within 1e-6), the pose, the materials' main texture.</summary>
    static void C33Vanilla(Ctx c, JObject p, string who, string item, string prefab, bool top, bool under, int rendHair, C33B[] want)
    {
        var fx = p?["facts"] as JObject;
        c.Check(p != null && (string)p["item"] == item && (string)p["mainData"] == prefab && (string)p["prefab"] == prefab && (bool?)p["realized"] == true,
            who + ": item " + item + ", mainData = prefab " + prefab + " (HS2 / census)", Ctx.Short(p?.ToString()));
        c.Check(fx != null && (bool?)fx["hasCmpHair"] == true && (bool?)fx["useTopColor"] == top && (bool?)fx["useUnderColor"] == under && (bool?)fx["useSameSkinColorButton"] == false
                && (bool?)fx["useAcsColor01"] == false && (bool?)fx["useAcsColor02"] == false && (bool?)fx["useAcsColor03"] == false && (fx["acsDefColor"] as JArray)?.Count == 0
                && (int?)fx["rendHair"] == rendHair && (int?)fx["bundles"] == want.Length,
            who + ": facts = HS2's (top " + top + ", under " + under + ", no skin match, no decoration, rendHair " + rendHair + ", bundles " + want.Length + ")", Ctx.Short(fx?.ToString()));
        // useMesh: HS2's instance flag (prefab or HairMeshforAll); the prefab copy HS2Next reads may differ from the census (package A)
        bool live = (int?)fx?["rendHair"] > 0;
        bool expMesh = (bool?)fx?["prefabUseMesh"] == true || (HS2Next.Chara.Edit.Units.HairUnit.MeshOnEveryHair && live);
        c.Check(fx != null && (bool?)fx["useMesh"] == expMesh && (bool?)fx["meshWritten"] == (live && expMesh),
            who + ": useMesh = prefabUseMesh || MeshOnEveryHair (" + HS2Next.Chara.Edit.Units.HairUnit.MeshOnEveryHair + ") with a live rendHair; meshWritten", Ctx.Short(fx?.ToString()));
        if ((bool?)fx?["useMesh"] != true) c.Warn(who + ": useMesh false where HS2 reads true (prefab copy / HairMeshforAll switch)");
        var bs = C33Bundles(p);
        bool ok = bs.Count == want.Length;
        var det = new List<string>();
        var renamed = new List<string>();
        for (int i = 0; ok && i < want.Length; i++)
        {
            var b = bs[i]; var w = want[i];
            string trf = (string)b["trfCorrect"];
            if (trf != w.Trf && C33SameName(trf, w.Trf)) renamed.Add(trf + " / HS2 " + w.Trf);
            bool bi = (int?)b["index"] == w.Index && (bool?)b["shown"] == true && (int?)b["title"] == i + 1 && C33SameName(trf, w.Trf)
                      && C33V(b["limits"]?["posMin"]) == w.PosMin && C33V(b["limits"]?["posMax"]) == w.PosMax && C33V(b["limits"]?["rotMin"]) == w.RotMin && C33V(b["limits"]?["rotMax"]) == w.RotMax
                      && C33Max(C33F(b["default"]?["move"]), new[] { w.Move.x, w.Move.y, w.Move.z }) <= 1e-6f
                      && C33Max(C33F(b["default"]?["rotate"]), new[] { w.Rotate.x, w.Rotate.y, w.Rotate.z }) <= 1e-6f;
            if (!bi) det.Add(i + ": " + Ctx.Short(b.ToString()));
            ok &= bi;
        }
        c.Check(ok, who + ": bundles = HS2's records (index, shown, title 1.., trfCorrect, limits exactly, default rates within 1e-6)", string.Join(" | ", det));
        if (renamed.Count > 0) c.Warn(who + ": correction transform names differ from the copy HS2 draws by underscores only (a zipmod-shadowed vanilla bundle; packages A, H): " + string.Join(", ", renamed));
        bool poses = bs.All(b => C33Pose(b, out _));
        string pd = string.Join(" | ", bs.Where(b => !C33Pose(b, out _)).Select(b => { C33Pose(b, out string d); return b["index"] + ": " + d; }));
        c.Check(bs.Count > 0 && poses, who + ": each correction transform = lerp(limits, effective rates) (HH5's unclamped lerp)", pd);
        var mats = (p?["materials"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        string texD = (string)p?["texD"];
        c.Check(mats.Count >= rendHair && mats.All(x => (int?)x["renderQueue"] == 2450 && (string)x["mainTex"] == texD && x["shader"]?.Type == JTokenType.String && x["props"] is JObject),
            who + ": materials (every rendHair slot): queue 2450, _MainTex = the row's TexD " + texD + " (draw type 0), shader, props", Ctx.Short(p?["materials"]?.ToString()));
    }

    static void C33Hair(Ctx c, string f, string m, string item)
    {
        int n0 = W1UndoCount(c);
        string sha0 = W1DocSha(c);
        var all = c.Ok(new { cmd = "chara.edit.hair", key = f }, "chara.edit.hair BR-Chan");
        var parts = (all["parts"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        int shaderType = (int?)C33Doc(c, f, "shaderType") ?? -1;
        c.Check(parts.Count == 4 && Enumerable.Range(0, 4).All(k => (int?)parts[k]["part"] == k && (string)parts[k]["partName"] == C33Parts[k]) && (int?)all["shaderType"] == shaderType
                && all["meshOnEveryHair"]?.Type == JTokenType.Boolean,
            "four parts (part 0..3, partName back / front / side / option), shaderType = hair.shaderType (" + shaderType + "), meshOnEveryHair", Ctx.Short(all.ToString()));
        // the order of the HS2 side's chara.hairInfo members, then HS2Next's additions
        var hs2Order = new[] { "part", "item", "object", "name", "mainAB", "mainData", "mainData02", "ringOff", "texAB", "texD", "texC", "facts", "stored", "bundles", "otherChains", "materials", "decoration", "otherRenderers" };
        var keys = parts.Count > 0 ? parts[0].Properties().Select(x => x.Name).ToList() : new List<string>();
        c.Check(keys.Take(hs2Order.Length).SequenceEqual(hs2Order), "part members in the HS2 side's order (" + string.Join(", ", hs2Order) + "), additions after", string.Join(", ", keys));
        var b0 = parts.Count > 0 ? C33Bundles(parts[0]).FirstOrDefault() : null;
        var bOrder = new[] { "index", "shown", "title", "trfCorrect", "parent", "limits", "base", "default", "stored", "effective", "local", "world", "chains" };
        c.Check(b0 != null && b0.Properties().Select(x => x.Name).SequenceEqual(bOrder), "bundle members in the HS2 side's order", b0 == null ? "" : string.Join(", ", b0.Properties().Select(x => x.Name)));
        // BR-Chan back 300#11 «ウェーブロング», front 301#50 «斜めぱっつん» (HS2: hc1.json)
        C33Vanilla(c, parts.ElementAtOrDefault(0), "BR-Chan back", "illusion.hs2#11", "p_c_hair_b_12_02", true, true, 3, new[]
        {
            new C33B(0, "c_J_hair_BCa_s_01", V(-0.4f, 0.25f, 0.4f), V(0.4f, -0.6f, -0.8f), V(-31, 45, -30), V(31, -45, 30), V(0.5f, 0.29411763f, 0.33333334f), V(0.5f, 0.5f, 0.5f)),
            new C33B(1, "c_J_hair_BLa_s_01", V(-0.4f, 0.25f, 0.4f), V(0.4f, -0.6f, -0.8f), V(-50, -60, 50), V(50, 60, -50), V(0.5f, 0.29411763f, 0.33333334f), V(0.5f, 0.5f, 0.5f)),
            new C33B(2, "c_J_hair_BRa_s_01", V(-0.4f, 0.25f, 0.4f), V(0.4f, -0.6f, -0.8f), V(-50, 60, -50), V(50, -60, 50), V(0.5f, 0.29421794f, 0.33333334f), V(0.5f, 0.5f, 0.5f)),
        });
        C33Vanilla(c, parts.ElementAtOrDefault(1), "BR-Chan front", "illusion.hs2#50", "p_c_hair_f_37_00", true, true, 1, new[]
        {
            new C33B(0, "c_J_hair_FCa_s_01", V(-0.2f, 0.2f, -0.1f), V(0.2f, -0.5f, 0.1f), V(-30, -30, -30), V(30, 30, 30), V(0.5f, 0.2856839f, 0.50034744f), V(0.5f, 0.5f, 0.5f)),
            new C33B(1, "c_J_hair_FLa_s_01", V(0.1f, 0.2f, -0.2f), V(-0.1f, -0.5f, 0.2f), V(-30, 30, 30), V(30, -30, -30), V(0.5f, 0.2857143f, 0.5f), V(0.5f, 0.5f, 0.5f)),
            new C33B(2, "c_J_hair_FRa_s_01", V(-0.2f, 0.2f, -0.2f), V(0.2f, -0.5f, 0.2f), V(-30, -30, -30), V(30, 30, 30), V(0.5001806f, 0.2858087f, 0.49983397f), V(0.5f, 0.5f, 0.5f)),
        });
        var back = parts.ElementAtOrDefault(0);
        var front = parts.ElementAtOrDefault(1);
        // stored / effective: BR-Chan's lists are valid (count = the prefab's), so effective = stored
        foreach (var (p, who) in new[] { (back, "back"), (front, "front") })
        {
            var docB = C33Doc(c, f, who + ".bundles") as JArray;
            var bs = C33Bundles(p);
            bool eq = docB != null && (int?)p?["stored"] == docB.Count && (bool?)p?["storedValid"] == true && bs.Count == docB.Count
                      && bs.All(b => b["stored"] is JObject s && C33Eq(C33F(s["move"]), C33F(docB[(int)b["index"]]["move"])) && C33Eq(C33F(s["rotate"]), C33F(docB[(int)b["index"]]["rotate"]))
                                     && C33Eq(C33F(b["effective"]?["move"]), C33F(s["move"])) && C33Eq(C33F(b["effective"]?["rotate"]), C33F(s["rotate"])));
            c.Check(eq, "BR-Chan " + who + ": stored = the document's bundles, storedValid, effective = stored", Ctx.Short(docB?.ToString()));
        }
        // chains: every vanilla bundle's chain roots lie below its correction transform
        var chains = C33Bundles(back).Concat(C33Bundles(front)).SelectMany(b => (b["chains"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>()).ToList();
        c.Check(C33Bundles(back).Concat(C33Bundles(front)).All(b => (b["chains"] as JArray)?.Count >= 1) && chains.All(x => (string)x["relation"] == "trfCorrect above the root"
                && (int?)x["particles"] > 0 && x["active"]?.Type == JTokenType.Boolean && !(bool)x["active"] && (bool?)x["noShake"] == false && (bool?)x["enabled"] == true && x["root"]?.Type == JTokenType.String && x["rest"]?["rotation"] is JArray),
            "chains: each bundle has its chains, relation \"trfCorrect above the root\", enabled (own state) but inactive (physics off), noShake false, rest pose", Ctx.Short(string.Join(" | ", chains.Take(3))));
        var fr = C33Bundles(front).FirstOrDefault();
        var roots = (fr?["chains"] as JArray)?.OfType<JObject>().Select(x => (string)x["root"]).OrderBy(x => x, StringComparer.Ordinal).ToList() ?? new List<string>();
        var hs2Roots = new[] { "c_J_hair_FCa_01", "c_J_hair_FCb_01" };
        c.Check(roots.Count == 2 && roots.Zip(hs2Roots, C33SameName).All(x => x), "front bundle 0's chain roots = HS2's (c_J_hair_FCa_01, c_J_hair_FCb_01; underscores aside)", string.Join(", ", roots));
        if (!roots.SequenceEqual(hs2Roots)) c.Warn("front bundle 0's chain roots differ from the copy HS2 draws by underscores only: " + string.Join(", ", roots));
        // the mesh pattern on the front hair (pattern 10, layout (0, 0, 0, 1), white)
        var mesh = front?["mesh"] as JObject;
        bool written = (bool?)front?["facts"]?["meshWritten"] == true;
        c.Check(mesh != null && (string)mesh["item"] == "illusion.hs2#10" && C33Eq(C33F(mesh["layout"]), new[] { 0f, 0f, 0f, 1f }) && (bool?)mesh["written"] == written,
            "front mesh: item illusion.hs2#10, layout (0, 0, 0, 1) as stored, written = facts.meshWritten", Ctx.Short(mesh?.ToString()));
        c.Check(written && (string)mesh["texture"] == "mesh_t_10" && C33Eq(C33F(mesh["scale"]), new[] { 0.5f, 0.5f }) && C33Eq(C33F(mesh["offset"]), new[] { 0.5f, 1.5f })
                && C33Eq(C33F(mesh["color"]), new[] { 1f, 1f, 1f, 1f }) && (int?)mesh["w"] == 1024 && (bool?)mesh["uniform"] == true,
            "front mesh as written: _MeshColorMask_B mesh_t_10 (1024²), scale (0.5, 0.5), offset (0.5, 1.5), _MeshColor_01 (1, 1, 1, 1) (HH1)", Ctx.Short(mesh?.ToString()));
        // side / extension: none rows (no CmpHair, no bundles, no materials)
        foreach (int k in new[] { 2, 3 })
        {
            var p = parts.ElementAtOrDefault(k);
            c.Check(p != null && (bool?)p["facts"]?["hasCmpHair"] == false && C33Bundles(p).Count == 0 && (p["materials"] as JArray)?.Count == 0,
                "BR-Chan " + C33Parts[k] + ": a none row (hasCmpHair false, no bundles, no materials)", Ctx.Short(p?.ToString()));
        }
        // the facts = HairInfo's (what the Maker gates read)
        bool same = true;
        var fd = new List<string>();
        for (int k = 0; k < 4 && k < parts.Count; k++)
        {
            bool got = HS2Next.Scenes.Runtime.HairInfo.TryPart(c.App.Runtime, f, k, out var hf);
            var fx = parts[k]["facts"];
            var bs = C33Bundles(parts[k]);
            bool ok = got && hf.HasPrefab == (bool)fx["hasCmpHair"] && (!hf.HasPrefab ? hf.Bundles.Length == 0 && !hf.UseMesh && ((JObject)fx).Count == 1 : hf.UseMesh == (bool)fx["meshWritten"] && hf.UseTopColor == (bool)fx["useTopColor"] && hf.UseUnderColor == (bool)fx["useUnderColor"]
                      && hf.UseSameSkinColorButton == (bool)fx["useSameSkinColorButton"] && hf.AcsDefColorCount == (int)fx["acsDefColorCount"] && hf.Bundles.Length == (hf.HasPrefab ? (int)fx["bundles"] : 0)
                      && hf.Bundles.All(b => bs.Any(x => (int)x["index"] == b.Index && (int)x["title"] == b.Title && C33Eq(C33F(x["default"]?["move"]), new[] { b.DefaultMove.X, b.DefaultMove.Y, b.DefaultMove.Z })
                                                         && C33Eq(C33F(x["default"]?["rotate"]), new[] { b.DefaultRotate.X, b.DefaultRotate.Y, b.DefaultRotate.Z }))));
            if (!ok) fd.Add(C33Parts[k]);
            same &= ok;
        }
        c.Check(same, "facts and default rates = HairInfo.TryPart's (the gates' service) bit for bit; a part without CmpHair: facts {hasCmpHair: false} only (the HS2 side)", string.Join(", ", fd));
        // the part argument
        var pf = c.Ok(new { cmd = "chara.edit.hair", key = f, part = "front" }, "chara.edit.hair part front");
        var p1 = c.Ok(new { cmd = "chara.edit.hair", key = f, part = 1 }, "chara.edit.hair part 1");
        c.Check((pf["parts"] as JArray)?.Count == 1 && (int?)pf["parts"]?[0]?["part"] == 1 && JToken.DeepEquals(pf["parts"], p1["parts"]) && JToken.DeepEquals(pf["parts"]?[0], front),
            "part \"front\" = part 1 = the front of the whole reply");
        c.Fails(new { cmd = "chara.edit.hair", key = f, part = "nose" }, "bad_args", "part \"nose\" → bad_args");
        c.Fails(new { cmd = "chara.edit.hair", key = f, part = 4 }, "bad_args", "part 4 → bad_args");
        c.Fails(new { cmd = "chara.edit.hair", key = f, part = true }, "bad_args", "part true → bad_args");
        c.Fails(new { cmd = "chara.edit.hair", key = item }, "wrong_kind", "an item → wrong_kind");
        c.Fails(new { cmd = "chara.edit.hair", key = "nosuchnode" }, "not_found", "an unknown key → not_found");
        // HERO107: back 300#1, front 301#2 (no top colour); the back's three chains outside any bundle (HS2: otherChains)
        var hm = c.Ok(new { cmd = "chara.edit.hair", key = m }, "chara.edit.hair HERO107");
        var mparts = (hm["parts"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        C33Vanilla(c, mparts.ElementAtOrDefault(0), "HERO107 back", "illusion.hs2#1", "p_c_hair_b_02_00", true, true, 2, new[]
        {
            new C33B(0, "c_J_hair_BCa_s_01", V(0, 0, 0), V(0.2f, -0.6f, -0.4f), V(-40, 20, -35), V(120, -20, 35), V(0, 0, 0), V(0.25f, 0.5f, 0.5f)),
            new C33B(1, "c_J_hair_BLa_s_01", V(0.2f, 0, 0), V(-0.2f, -0.6f, -0.4f), V(-20, 60, 75), V(60, -60, -75), V(0.5f, 0, 0), V(0.25f, 0.5f, 0.5f)),
            new C33B(2, "c_J_hair_BRa_s_01", V(-0.2f, 0, 0), V(0.2f, -0.6f, -0.4f), V(-20, -60, -75), V(60, 60, 75), V(0.5f, 0, 0), V(0.25f, 0.5f, 0.5f)),
        });
        C33Vanilla(c, mparts.ElementAtOrDefault(1), "HERO107 front", "illusion.hs2#2", "p_c_hair_f_02_00", false, true, 2, new[]
        {
            new C33B(0, "c_J_hair_FC_s_01", V(-0.4f, 0.15f, -0.3f), V(0.4f, -0.9f, 0.3f), V(70, -35, -30), V(-70, 35, 30), V(0.5f, 0.14285715f, 0.5f), V(0.5f, 0.5f, 0.5f)),
            new C33B(1, "c_J_hair_FL_s_01", V(0.6f, 0.15f, -0.5f), V(-0.6f, -0.9f, 0.5f), V(70, 60, 70), V(-70, -60, -70), V(0.5f, 0.14285715f, 0.5f), V(0.5f, 0.5f, 0.5f)),
            new C33B(2, "c_J_hair_FR_s_01", V(-0.4f, 0.15f, -0.5f), V(0.4f, -0.9f, 0.5f), V(70, -60, -70), V(-70, 60, 70), V(0.5f, 0.14285715f, 0.5f), V(0.5f, 0.5f, 0.5f)),
        });
        var oc = (mparts.ElementAtOrDefault(0)?["otherChains"] as JArray)?.OfType<JObject>().Select(x => (string)x["root"]).OrderBy(x => x, StringComparer.Ordinal).ToList() ?? new List<string>();
        c.Check(oc.SequenceEqual(new[] { "c_J_hair_BCb_01", "c_J_hair_BLb_01", "c_J_hair_BRb_01" }), "HERO107 back: otherChains = HS2's (c_J_hair_BCb_01, BLb, BRb)", string.Join(", ", oc));
        // the male prefab variants (HS2 HC9: 300#22 / #59 load p_cm_* with 0 / 2 bundles)
        foreach (var (row, prefab, nb) in new[] { ("illusion.hs2#22", "p_cm_hair_b_19_00", 0), ("illusion.hs2#59", "p_cm_hair_b_39_00", 2) })
        {
            c.Ok(new { cmd = "chara.edit.item", key = m, path = "chara.hair.back.item", @ref = row }, "HERO107 back → " + row);
            var mb = C33Part(c, m, 0);
            c.Check(mb != null && (string)mb["prefab"] == prefab && (string)mb["mainData02"] == prefab && (int?)mb["facts"]?["bundles"] == nb && (bool?)mb["facts"]?["hasCmpHair"] == true,
                "HERO107 back " + row + ": prefab = MainData02 " + prefab + ", " + nb + " bundles (HS2 HC9)", Ctx.Short(mb?.ToString()));
            c.Ok(new { cmd = "undo" }, "undo the male back");
        }
        c.Check(W1UndoCount(c) == n0 && W1DocSha(c) == sha0, "chara.edit.hair records nothing (the item changes undone)");
    }

    // ---------------------------------------------------------------- chara.edit.hairBundle

    static void C33Bundle(Ctx c, string f, string m, string item)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var p0 = C33Part(c, f, 0);
        var b = C33Bundles(p0);
        if (b.Count != 3) { c.Check(false, "BR-Chan back has 3 bundles", "" + b.Count); return; }
        var dMove = b.Select(x => C33F(x["default"]?["move"])).ToList();
        var dRot = b.Select(x => C33F(x["default"]?["rotate"])).ToList();
        // a move edit: stored as given, the transform follows, the light chain re-derivation
        var r = c.Ok(C33Req(f, "back", "index", 0, "move", new[] { 0.7, 0.2, 0.4 }), "hairBundle back 0 move (0.7, 0.2, 0.4)");
        string db = C33DynBone(c, f);
        c.Check(C30Has(r["changed"], "chara.hair.back.bundles[0].move") && !C30Has(r["changed"], "chara.hair.back.bundles") && W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Edit hair",
            "changed chara.hair.back.bundles[0].move (no materialization: the stored list is valid), one entry \"Edit hair\"", Ctx.Short(r.ToString()) + " " + W1TopLabel(c));
        c.Check(db == "light", "a rate edit runs the light chain re-derivation (info.hair.lastFlush.dynBone light)", db);
        var p = C33Part(c, f, 0);
        var bb = C33Bundles(p);
        c.Check(C33Eq(C33F(C33Doc(c, f, "back.bundles[0].move")), new[] { 0.7f, 0.2f, 0.4f }) && C33Eq(C33F(bb[0]["effective"]?["move"]), new[] { 0.7f, 0.2f, 0.4f })
                && C33Pose(bb[0], out string pd0), "document and effective move (0.7, 0.2, 0.4); the correction transform = lerp(posMin, posMax, r)", Ctx.Short(bb[0].ToString()));
        // rates outside 0..1 are stored and extrapolate (v0.2, HH5)
        c.Ok(C33Req(f, 0, "index", 1, "move", new[] { -0.5, 1.5, 0.5 }), "hairBundle back 1 move (−0.5, 1.5, 0.5) (part as 0)");
        bb = C33Bundles(C33Part(c, f, 0));
        bool pose1 = C33Pose(bb[1], out string pd1);
        c.Check(C33Eq(C33F(C33Doc(c, f, "back.bundles[1].move")), new[] { -0.5f, 1.5f, 0.5f }) && pose1, "rates outside 0..1 stored as given; the transform extrapolates (unclamped lerp)", pd1);
        c.Ok(C33Req(f, "back", "index", 0, "rotate", new[] { 0.25, 0.5, 0.75 }), "hairBundle back 0 rotate (0.25, 0.5, 0.75)");
        bb = C33Bundles(C33Part(c, f, 0));
        bool pose2 = C33Pose(bb[0], out string pd2);
        c.Check(C33Eq(C33F(C33Doc(c, f, "back.bundles[0].rotate")), new[] { 0.25f, 0.5f, 0.75f }) && pose2, "rotate stored; the local rotation = Euler(lerp(rotMin, rotMax, r))", pd2);
        // noShake: the full rebuild; the bundle's chains carry it
        c.Ok(C33Req(f, "back", "index", 1, "noShake", true), "hairBundle back 1 noShake true");
        db = C33DynBone(c, f);
        bb = C33Bundles(C33Part(c, f, 0));
        bool chainsNs = bb[1]["chains"] is JArray c1 && c1.Count > 0 && c1.All(x => (bool?)x["noShake"] == true && (bool?)x["enabled"] == false && (bool?)x["active"] == false)
                        && bb[0]["chains"] is JArray c0 && c0.All(x => (bool?)x["noShake"] == false && (bool?)x["enabled"] == true);
        c.Check(db == "full" && (bool?)C33Doc(c, f, "back.bundles[1].noShake") == true && (bool?)bb[1]["effective"]?["noShake"] == true && chainsNs,
            "noShake true: the full rebuild (dynBone full), stored, effective, bundle 1's chains noShake and not enabled (others enabled)", db + " " + Ctx.Short(bb[1]["chains"]?.ToString()));
        // «髪の調整をまとめて行う»: each changed component on every bundle (C10), move and rotate apart
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.hair.editor.ctrlTogether"] = true } }, "ctrlTogether on");
        int nt = W1UndoCount(c);
        c.Ok(C33Req(f, "back", "index", 2, "rotate", new double[] { 0.1, dRot[2][1], dRot[2][2] }), "together: bundle 2 rotate x 0.1 (y / z = its effective values)");
        var r0 = C33F(C33Doc(c, f, "back.bundles[0].rotate")); var r1 = C33F(C33Doc(c, f, "back.bundles[1].rotate")); var r2 = C33F(C33Doc(c, f, "back.bundles[2].rotate"));
        c.Check(C33Eq(r2, new[] { 0.1f, dRot[2][1], dRot[2][2] }) && C33Eq(r0, new[] { 0.1f, 0.5f, 0.75f }) && C33Eq(r1, new[] { 0.1f, dRot[1][1], dRot[1][2] }) && W1UndoCount(c) == nt + 1,
            "together: rotate x 0.1 on all three bundles, y / z of the others unchanged, one entry", C33S(r0) + " " + C33S(r1) + " " + C33S(r2));
        var m2 = C33F(C33Bundles(C33Part(c, f, 0))[2]["effective"]?["move"]);
        c.Ok(C33Req(f, "back", "index", 2, "move", new double[] { m2[0], 0.9, m2[2] }), "together: bundle 2 move y 0.9");
        var mm0 = C33F(C33Doc(c, f, "back.bundles[0].move")); var mm1 = C33F(C33Doc(c, f, "back.bundles[1].move"));
        c.Check(C33Eq(mm0, new[] { 0.7f, 0.9f, 0.4f }) && C33Eq(mm1, new[] { -0.5f, 0.9f, 0.5f }) && C33Eq(C33F(C33Doc(c, f, "back.bundles[0].rotate")), new[] { 0.1f, 0.5f, 0.75f }),
            "together: move y 0.9 on every bundle, x / z and rotate unchanged", C33S(mm0) + " " + C33S(mm1));
        // resets never propagate: one component (axis), rotate, noShake, both
        c.Ok(C33Req(f, "back", "index", 0, "reset", true, "rotate", new[] { 0, 0, 0 }), "reset bundle 0 rotate (together on)");
        c.Check(C33Eq(C33F(C33Doc(c, f, "back.bundles[0].rotate")), dRot[0]) && C33Eq(C33F(C33Doc(c, f, "back.bundles[1].rotate")), new[] { 0.1f, dRot[1][1], dRot[1][2] }),
            "reset rotate → the prefab default on bundle 0 only (no propagation)", C33S(C33F(C33Doc(c, f, "back.bundles[0].rotate"))));
        c.Ok(C33Req(f, "back", "index", 0, "reset", true, "move", new[] { 0, 0, 0 }, "axis", 1), "reset bundle 0 move axis 1");
        c.Check(C33Eq(C33F(C33Doc(c, f, "back.bundles[0].move")), new[] { 0.7f, dMove[0][1], 0.4f }) && C33Eq(C33F(C33Doc(c, f, "back.bundles[1].move")), new[] { -0.5f, 0.9f, 0.5f }),
            "reset move axis 1 → only y = the default " + dMove[0][1] + " (a slider row's ↺)", C33S(C33F(C33Doc(c, f, "back.bundles[0].move"))));
        c.Ok(C33Req(f, "back", "index", 1, "reset", true, "noShake", true), "reset bundle 1 noShake");
        db = C33DynBone(c, f);
        c.Check((bool?)C33Doc(c, f, "back.bundles[1].noShake") == false && db == "full" && C33Eq(C33F(C33Doc(c, f, "back.bundles[1].move")), new[] { -0.5f, 0.9f, 0.5f }),
            "reset noShake → false (the value only selects), the rates kept, a full rebuild", db);
        c.Ok(C33Req(f, "back", "index", 2, "reset", true), "reset bundle 2 (move and rotate)");
        c.Check(C33Eq(C33F(C33Doc(c, f, "back.bundles[2].move")), dMove[2]) && C33Eq(C33F(C33Doc(c, f, "back.bundles[2].rotate")), dRot[2]), "reset without members → move and rotate = the defaults");
        // «全て初期化»: every bundle's rates = the defaults, noShake kept
        c.Ok(C33Req(f, "back", "index", 0, "noShake", true), "bundle 0 noShake true");
        int na = W1UndoCount(c);
        var ra = c.Ok(C33Req(f, "back", "all", true, "reset", true), "hairBundle all + reset («全て初期化»)");
        bool allDef = Enumerable.Range(0, 3).All(i => C33Eq(C33F(C33Doc(c, f, "back.bundles[" + i + "].move")), dMove[i]) && C33Eq(C33F(C33Doc(c, f, "back.bundles[" + i + "].rotate")), dRot[i]));
        c.Check(allDef && (bool?)C33Doc(c, f, "back.bundles[0].noShake") == true && (bool?)C33Doc(c, f, "back.bundles[1].noShake") == false && W1UndoCount(c) == na + 1 && W1TopLabel(c) == "Edit hair"
                && C30Has(ra["changed"], "chara.hair.back.bundles"), "all + reset: every bundle at the prefab defaults, noShake of bundle 0 kept, one entry \"Edit hair\"", Ctx.Short(ra.ToString()));
        bb = C33Bundles(C33Part(c, f, 0));
        c.Check(bb.All(x => C33Pose(x, out _)), "after «全て初期化» the correction transforms = the default poses");
        var nop = c.Ok(C33Req(f, "back", "index", 0, "move", dMove[0].Select(x => (double)x).ToArray()), "the default move again");
        c.Check(C30Has(nop["noop"], "chara.hair.back.bundles[0].move") && W1UndoCount(c) == na + 1, "noop, no entry", Ctx.Short(nop.ToString()));
        // errors (nothing recorded)
        int ne = W1UndoCount(c);
        c.Fails(C33Req(f, "back", "index", 3, "move", new[] { 0.5, 0.5, 0.5 }), "not_found", "index 3 (the prefab has 3 bundles) → not_found");
        c.Fails(C33Req(f, "back", "index", -1, "move", new[] { 0.5, 0.5, 0.5 }), "not_found", "index −1 → not_found");
        c.Fails(C33Req(f, "nose", "index", 0, "move", new[] { 0.5, 0.5, 0.5 }), "bad_args", "part \"nose\" → bad_args");
        c.Fails(C33Req(f, 4, "index", 0, "move", new[] { 0.5, 0.5, 0.5 }), "bad_args", "part 4 → bad_args");
        c.Fails(new { cmd = "chara.edit.hairBundle", key = f, index = 0, move = new[] { 0.5, 0.5, 0.5 } }, "bad_args", "no part → bad_args");
        c.Fails(C33Req(f, "back", "move", new[] { 0.5, 0.5, 0.5 }), "bad_args", "no index (and no all) → bad_args");
        c.Fails(C33Req(f, "back", "index", 0), "bad_args", "nothing to set → bad_args");
        c.Fails(C33Req(f, "back", "index", 0, "move", new[] { 0.5, 0.5 }), "bad_args", "move of two values → bad_args");
        c.Fails(C33Req(f, "back", "index", 0, "move", "x"), "bad_args", "move a string → bad_args");
        c.Fails(C33Req(f, "back", "index", 0, "move", new object[] { 0.5, "a", 0.5 }), "bad_args", "a move component a string → bad_args");
        c.Fails(C33Req(f, "back", "index", 0, "noShake", "yes"), "bad_args", "noShake a string → bad_args");
        c.Fails(C33Req(f, "back", "index", 0, "reset", true, "axis", 3), "bad_args", "axis 3 → bad_args");
        c.Fails(C33Req(f, "back", "index", 0, "move", new[] { 0.5, 0.5, 0.5 }, "axis", 1), "bad_args", "axis without reset → bad_args");
        c.Fails(C33Req(f, "back", "all", true), "bad_args", "all without reset → bad_args");
        c.Fails(C33Req(f, "back", "all", true, "reset", true, "index", 0), "bad_args", "all with index → bad_args");
        c.Fails(C33Req(f, "back", "all", true, "reset", true, "move", new[] { 0.5, 0.5, 0.5 }), "bad_args", "all with move → bad_args");
        c.Fails(C33Req(item, "back", "index", 0, "move", new[] { 0.5, 0.5, 0.5 }), "wrong_kind", "an item → wrong_kind");
        c.Fails(C33Req("nosuchnode", "back", "index", 0, "move", new[] { 0.5, 0.5, 0.5 }), "not_found", "an unknown key → not_found");
        var side = c.Call(C33Req(f, "side", "index", 0, "move", new[] { 0.5, 0.5, 0.5 }));
        c.Check((bool?)side["ok"] == false && ((string)side["code"] == "not_found" || (string)side["code"] == "unsupported"), "the side none row (no bundles) → not_found / unsupported", Ctx.Short(c.LastText));
        c.Check(W1UndoCount(c) == ne, "failed edits record nothing");
        // several keys: one entry
        int nk = W1UndoCount(c);
        c.Ok(new { cmd = "chara.edit.hairBundle", keys = new[] { f, m }, part = "back", index = 0, move = new[] { 0.55, 0.45, 0.35 } }, "hairBundle keys [BR-Chan, HERO107] back 0 move");
        c.Check(C33Eq(C33F(C33Doc(c, f, "back.bundles[0].move")), new[] { 0.55f, 0.45f, 0.35f }) && C33Eq(C33F(C33Doc(c, m, "back.bundles[0].move")), new[] { 0.55f, 0.45f, 0.35f }) && W1UndoCount(c) == nk + 1,
            "both characters' back bundle 0 move, one entry");
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (bundle block)");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
        var back = C33Bundles(C33Part(c, f, 0));
        c.Check(back.Count == 3 && back.All(x => C33Pose(x, out _)) && C33DynBone(c, f) != null, "after undo the correction transforms follow the stored rates again");
    }

    /// <summary>The first bundle edit of a part whose stored list is not the effective one stores the whole list:
    /// after an item change (bundles cleared) and after a stored list of the wrong count.</summary>
    static void C33Materialize(Ctx c, string f, string m)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        // an item change clears the bundles; «ボリュームポニー» 300#2 (HH2: one bundle)
        c.Ok(new { cmd = "chara.edit.item", key = f, path = "chara.hair.back.item", @ref = "illusion.hs2#2" }, "back → illusion.hs2#2");
        var p = C33Part(c, f, 0);
        int n = (int?)p?["facts"]?["bundles"] ?? 0;
        var docB = C33Doc(c, f, "back.bundles") as JArray;
        c.Check(docB != null && docB.Count == 0 && (int?)p?["stored"] == 0 && (bool?)p?["storedValid"] == false
                && C33Bundles(p).All(x => x["stored"]?.Type == JTokenType.Null && C33Eq(C33F(x["effective"]?["move"]), C33F(x["default"]?["move"])) && C33Eq(C33F(x["effective"]?["rotate"]), C33F(x["default"]?["rotate"]))),
            "after the item change: bundles [] stored, storedValid false, every bundle at its prefab default (effective = default)", Ctx.Short(p?.ToString()));
        if (n != 1) c.Warn("300#2 has " + n + " bundles here (HS2 HH2: 1)");
        if (n < 1) { while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo"); return; }
        int n1 = W1UndoCount(c);
        var r = c.Ok(C33Req(f, "back", "index", 0, "move", new[] { 0.6, 0.6, 0.6 }), "the first bundle edit after the item change");
        docB = C33Doc(c, f, "back.bundles") as JArray;
        var defs = C33Bundles(p);
        bool listOk = docB != null && docB.Count == n && Enumerable.Range(0, n).All(i => (int?)docB[i]["index"] == i)
                      && C33Eq(C33F(docB[0]["move"]), new[] { 0.6f, 0.6f, 0.6f }) && C33Eq(C33F(docB[0]["rotate"]), C33F(defs[0]["default"]?["rotate"]))
                      && Enumerable.Range(1, n - 1).All(i => C33Eq(C33F(docB[i]["move"]), C33F(defs[i]["default"]?["move"])));
        c.Check(listOk && C30Has(r["changed"], "chara.hair.back.bundles") && C30Has(r["changed"], "chara.hair.back.bundles[0].move") && W1UndoCount(c) == n1 + 1,
            "materialized: changed chara.hair.back.bundles + bundles[0].move, " + n + " entries (indices 0..n−1, the defaults, bundle 0's move set), one entry", Ctx.Short(r.ToString()) + " " + Ctx.Short(docB?.ToString()));
        c.Fails(C33Req(f, "back", "index", n, "move", new[] { 0.5, 0.5, 0.5 }), "not_found", "index " + n + " on the new prefab → not_found");
        c.Ok(new { cmd = "undo" }, "undo the materializing edit");
        c.Check(((JArray)C33Doc(c, f, "back.bundles"))?.Count == 0, "one undo returns to the empty list");
        // a node that lacks the bundle is skipped on a multi-selection (edit.bundleSkipped)
        var sk = c.Ok(new { cmd = "chara.edit.hairBundle", keys = new[] { f, m }, part = "back", index = 2, move = new[] { 0.3, 0.3, 0.3 } }, "keys [BR-Chan (1 bundle), HERO107 (3)] back 2");
        c.Check(C31Warned(sk, "edit.bundleSkipped") && C33Eq(C33F(C33Doc(c, m, "back.bundles[2].move")), new[] { 0.3f, 0.3f, 0.3f }) && ((JArray)C33Doc(c, f, "back.bundles"))?.Count == 0,
            "BR-Chan skipped (warning edit.bundleSkipped), HERO107 edited", Ctx.Short(sk.ToString()));
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (materialize block)");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
        // a stored list of the wrong count (two entries for the three bundles of 300#11): defaults apply; the first edit stores three
        var two = new[] { new { index = 0, move = new[] { 0.9, 0.9, 0.9 }, rotate = new[] { 0.9, 0.9, 0.9 }, noShake = true }, new { index = 1, move = new[] { 0.1, 0.1, 0.1 }, rotate = new[] { 0.1, 0.1, 0.1 }, noShake = false } };
        var set = c.Call(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.hair.back.bundles"] = two } });
        if ((bool?)set["ok"] != true) { c.Warn("chara.edit.set of a whole bundles list is refused (" + set["code"] + "): the count rule is not checked through the bridge"); return; }
        var w = C33Part(c, f, 0);
        var wb = C33Bundles(w);
        c.Check((int?)w?["stored"] == 2 && (bool?)w?["storedValid"] == false && wb.Count == 3 && wb.All(x => C33Eq(C33F(x["effective"]?["move"]), C33F(x["default"]?["move"])) && (bool?)x["effective"]?["noShake"] == false)
                && wb.All(x => C33Pose(x, out _)), "a stored count of 2 for 3 bundles: storedValid false, every bundle at the defaults with noShake off (HS2's load rule, J6)", Ctx.Short(w?.ToString()));
        c.Ok(C33Req(f, "back", "index", 2, "rotate", new[] { 0.2, 0.2, 0.2 }), "the first edit of the wrong-count list");
        var db = C33Doc(c, f, "back.bundles") as JArray;
        c.Check(db != null && db.Count == 3 && C33Eq(C33F(db[0]["move"]), C33F(wb[0]["default"]?["move"])) && (bool?)db[0]["noShake"] == false && C33Eq(C33F(db[2]["rotate"]), new[] { 0.2f, 0.2f, 0.2f }),
            "materialized from the defaults (not the stored two): 3 entries, bundle 2 rotate set", Ctx.Short(db?.ToString()));
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (wrong count)");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
    }

    // ---------------------------------------------------------------- chara.edit.hairSkin

    static void C33Skin(Ctx c, string f, string m, string item)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        c.Check((bool?)C33Doc(c, f, "editor.sameSetting") == true, "BR-Chan has «前髪と後ろ髪の色を統一» on");
        var skin = C33F(C30Value(c, f, "chara.body.skin.color"));
        float gloss = (float)C30Value(c, f, "chara.body.skin.gloss");
        var under0 = C33F(C33Doc(c, f, "back.underColor"));
        var front0 = C33Doc(c, f, "front")?.ToString();
        Color.RGBToHSV(new Color(skin[0], skin[1], skin[2]), out float h, out float s, out float v);
        var want = Color.HSVToRGB(h, Math.Max(0f, s - 0.06f), Math.Max(0f, v - 0.06f));
        var r = c.Ok(new { cmd = "chara.edit.hairSkin", key = f, part = "back" }, "chara.edit.hairSkin back");
        var under = C33F(C33Doc(c, f, "back.underColor"));
        float sm = (float)C33Doc(c, f, "back.smoothness");
        float du = C33Max(under.Take(3).ToArray(), new[] { want.r, want.g, want.b });
        c.Check(du <= 1e-6f && under.Length == 4 && under[3] == under0[3] && sm == Math.Max(0f, gloss - 0.3f),
            "back underColor = HSVToRGB(H, S − 0.06, V − 0.06) of the skin colour (Unity's, Δ " + du + "), stored alpha kept, smoothness = max(0, gloss − 0.3) = " + sm, C33S(under) + " vs " + want);
        c.Check(C33Doc(c, f, "front")?.ToString() == front0 && C30Has(r["changed"], "chara.hair.back.underColor") && !r["changed"].Any(x => ((string)x).StartsWith("chara.hair.front", StringComparison.Ordinal))
                && W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Edit hair", "the shown part only although same setting is on (J12, HH7); one entry \"Edit hair\"", Ctx.Short(r.ToString()));
        var again = c.Ok(new { cmd = "chara.edit.hairSkin", key = f, part = 0 }, "the same match again (part 0)");
        c.Check(C30Has(again["noop"], "chara.hair.back.underColor") && W1UndoCount(c) == n0 + 1, "noop, no entry", Ctx.Short(again.ToString()));
        c.Ok(new { cmd = "chara.edit.hairSkin", keys = new[] { f, m }, part = 1 }, "hairSkin keys [BR-Chan, HERO107] part 1");
        c.Check(W1UndoCount(c) == n0 + 2 && C33Max(C33F(C33Doc(c, f, "front.underColor")).Take(3).ToArray(), new[] { want.r, want.g, want.b }) <= 1e-6f, "several keys: one entry; BR-Chan's front matched");
        int ne = W1UndoCount(c);
        c.Fails(new { cmd = "chara.edit.hairSkin", key = f }, "bad_args", "no part → bad_args");
        c.Fails(new { cmd = "chara.edit.hairSkin", key = f, part = "x" }, "bad_args", "part \"x\" → bad_args");
        c.Fails(new { cmd = "chara.edit.hairSkin", key = f, part = -1 }, "bad_args", "part −1 → bad_args");
        c.Fails(new { cmd = "chara.edit.hairSkin", key = item, part = "back" }, "wrong_kind", "an item → wrong_kind");
        c.Fails(new { cmd = "chara.edit.hairSkin", key = "nosuchnode", part = "back" }, "not_found", "an unknown key → not_found");
        c.Check(W1UndoCount(c) == ne, "failed matches record nothing");
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (skin block)");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
    }

    // ---------------------------------------------------------------- chara.edit.swatch 305

    static void C33Swatch(Ctx c, string f, string m, string item)
    {
        var lists = MakerCatalog.For(c.Services.Catalog);
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        float[] Want(HsvColour x, float alpha) { var w = Color.HSVToRGB(x.H, x.S, x.V); return new[] { w.r, w.g, w.b, alpha }; }
        // auto colour on (the push must not derive) and a stored alpha 0.5 on the back base colour (kept, J11)
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.hair.editor.autoSetting"] = true } }, "autoSetting on");
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.hair.back.baseColor"] = new[] { 0.1, 0.2, 0.3, 0.5 } } }, "back baseColor alpha 0.5");
        int n1 = W1UndoCount(c);
        c.Check(lists.HairColourPreset(ContentId.Vanilla(5), out HairPresetColumns p5), "305 row 5 exists");
        var r = c.Ok(new { cmd = "chara.edit.swatch", key = f, path = "chara.hair.back.baseColor", @ref = "illusion.hs2#5" }, "chara.edit.swatch hair back row 5 «金髪»");
        float worst = 0;
        bool ok = true;
        var det = new List<string>();
        for (int k = 0; k < 4; k++)
        {
            string pn = C33Parts[k];
            foreach (var (member, hsv) in new[] { ("baseColor", p5.Base), ("topColor", p5.Top), ("underColor", p5.Under), ("specular", p5.Specular) })
            {
                var got = C33F(C33Doc(c, f, pn + "." + member));
                float alpha = k == 0 && member == "baseColor" ? 0.5f : got.Length == 4 ? got[3] : float.NaN;
                var w = Want(hsv, alpha);
                float d = C33Max(got, w);
                worst = Math.Max(worst, d);
                if (!(d <= 1e-6f)) { ok = false; det.Add(pn + "." + member + " " + C33S(got) + " vs " + C33S(w)); }
            }
            ok &= (float)C33Doc(c, f, pn + ".metallic") == p5.Metallic && (float)C33Doc(c, f, pn + ".smoothness") == p5.Smoothness;
        }
        c.Check(ok && W1UndoCount(c) == n1 + 1 && W1TopLabel(c) == "Edit hair",
            "row 5 on all four parts (same setting on): base / top / under / specular = Unity's HSVToRGB of the row (Δ ≤ " + worst + "), metallic / smoothness = its columns, no auto derivation, one entry \"Edit hair\"", string.Join(" | ", det));
        var b0 = C33F(C33Doc(c, f, "back.baseColor"));
        c.Check(b0.Length == 4 && b0[3] == 0.5f && Math.Abs(b0[0] - 0.75f) < 1e-3 && Math.Abs(b0[1] - 0.684f) < 1e-3 && Math.Abs(b0[2] - 0.585f) < 1e-3,
            "back base = the census value (0.75, 0.684, 0.585) with the stored alpha 0.5 kept (J11)", C33S(b0));
        c.Check(C30Has(r["changed"], "chara.hair.front.topColor") && (C30Has(r["changed"], "chara.hair.option.smoothness") || C30Has(r["noop"], "chara.hair.option.smoothness")),
            "changed / noop name the members of all four parts", Ctx.Short(r.ToString()));
        // same setting off: the shown part only
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.hair.editor.sameSetting"] = false } }, "sameSetting off");
        string back1 = C33Doc(c, f, "back")?.ToString();
        lists.HairColourPreset(ContentId.Vanilla(2), out HairPresetColumns p2);
        c.Ok(new { cmd = "chara.edit.swatch", key = f, path = "appearance.chara.hair.front.baseColor", @ref = "illusion.hs2#2" }, "swatch front row 2 (full path)");
        c.Check(C33Doc(c, f, "back")?.ToString() == back1 && C33Max(C33F(C33Doc(c, f, "front.underColor")).Take(3).ToArray(), Want(p2.Under, 1f).Take(3).ToArray()) <= 1e-6f,
            "same setting off: row 2 on the front only, the back unchanged");
        // the seven rows by id
        bool seven = true;
        for (int i = 0; i < 7; i++)
        {
            c.Ok(new { cmd = "chara.edit.swatch", key = f, path = "chara.hair.side.baseColor", @ref = "illusion.hs2#" + i }, "swatch side row " + i);
            lists.HairColourPreset(ContentId.Vanilla(i), out HairPresetColumns pi);
            seven &= C33Max(C33F(C33Doc(c, f, "side.specular")).Take(3).ToArray(), Want(pi.Specular, 1f).Take(3).ToArray()) <= 1e-6f && (float)C33Doc(c, f, "side.metallic") == pi.Metallic;
        }
        c.Check(seven, "rows 0..6 by id: the side's specular / metallic follow each row");
        // several keys (305 is shared by both sexes)
        int nk = W1UndoCount(c);
        c.Ok(new { cmd = "chara.edit.swatch", keys = new[] { f, m }, path = "chara.hair.back.baseColor", @ref = "illusion.hs2#0" }, "swatch keys [BR-Chan, HERO107] back row 0");
        lists.HairColourPreset(ContentId.Vanilla(0), out HairPresetColumns p0);
        c.Check(W1UndoCount(c) == nk + 1 && C33Max(C33F(C33Doc(c, m, "back.baseColor")).Take(3).ToArray(), Want(p0.Base, 1f).Take(3).ToArray()) <= 1e-6f, "both characters, one entry");
        int ne = W1UndoCount(c);
        c.Fails(new { cmd = "chara.edit.swatch", key = f, path = "chara.hair.back.topColor", @ref = "illusion.hs2#0" }, "bad_args", "another hair colour path → bad_args");
        c.Fails(new { cmd = "chara.edit.swatch", key = f, path = "chara.hair.nose.baseColor", @ref = "illusion.hs2#0" }, "bad_args", "an unknown hair part → bad_args");
        c.Fails(new { cmd = "chara.edit.swatch", key = f, path = "chara.hair.back.baseColor", @ref = "illusion.hs2#7" }, "not_found", "id 7 (outside HS2's seven buttons) → not_found");
        c.Fails(new { cmd = "chara.edit.swatch", key = f, path = "chara.hair.back.baseColor", @ref = "illusion.hs2#99" }, "not_found", "an unknown row → not_found");
        c.Fails(new { cmd = "chara.edit.swatch", key = item, path = "chara.hair.back.baseColor", @ref = "illusion.hs2#0" }, "wrong_kind", "an item → wrong_kind");
        c.Check(W1UndoCount(c) == ne, "failed pushes record nothing");
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (swatch block)");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
    }

    // ---------------------------------------------------------------- chara.edit.materials part hair

    static void C33Materials(Ctx c, string f)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var all = c.Ok(new { cmd = "chara.edit.materials", key = f, part = "hair" }, "chara.edit.materials part hair");
        var rs = (all["renderers"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        var hair = c.Call(new { cmd = "chara.edit.hair", key = f });
        var parts = (hair["parts"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        bool cover = parts.All(p => ((p["materials"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            .All(x => rs.Any(y => (string)y["name"] == (string)x["renderer"] && (string)y["hairPart"] == C33Parts[(int)p["part"]])));
        var order = rs.Select(x => Array.IndexOf(C33Parts, (string)x["hairPart"])).ToList();
        c.Check(rs.Count > 0 && cover && order.All(k => k >= 0) && order.SequenceEqual(order.OrderBy(k => k)),
            "part hair: every renderer under the hair parts (part order), hairPart on each, covering chara.edit.hair's rendHair renderers", string.Join(", ", rs.Select(x => x["hairPart"] + ":" + x["name"])));
        var fr = rs.Where(x => (string)x["hairPart"] == "front").SelectMany(x => (x["slots"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>()).ToList();
        bool written = (bool?)parts.ElementAtOrDefault(1)?["facts"]?["meshWritten"] == true;
        c.Check(written && fr.Count > 0 && fr.All(s => (string)s["props"]?["_MeshColorMask_B"]?["name"] == "mesh_t_10" && C33Eq(C33F(s["props"]?["_MeshColorMask_B_ST"]), new[] { 0.5f, 0.5f, 0.5f, 1.5f })
                && C33Eq(C33F(s["props"]?["_MeshColor_01"]), new[] { 1f, 1f, 1f, 1f }) && (string)s["props"]?["_MeshColorMask_B"]?["wrapU"] == "Repeat" && s["props"]?["_MainTex_ST"] is JArray),
            "front slots: _MeshColorMask_B mesh_t_10 (wrapU Repeat), _MeshColorMask_B_ST (0.5, 0.5, 0.5, 1.5), _MeshColor_01 (1, 1, 1, 1), _MainTex_ST (the HS2 side's props)", Ctx.Short(fr.FirstOrDefault()?.ToString()));
        // chara.edit.hair's materials props = these props (the HS2 hair set)
        var hm = parts.ElementAtOrDefault(1)?["materials"]?[0]?["props"] as JObject;
        var mp = fr.FirstOrDefault()?["props"] as JObject;
        c.Check(hm != null && mp != null && hm.Properties().All(x => JToken.DeepEquals(x.Value, mp[x.Name])) && hm["_MeshColorMask_B_ST"] != null,
            "chara.edit.hair front materials[0].props = the same values in chara.edit.materials (the HS2 side's set: " + (hm == null ? "" : string.Join(", ", hm.Properties().Select(x => x.Name))) + ")");
        var one = c.Ok(new { cmd = "chara.edit.materials", key = f, part = "hair", hairPart = "front" }, "part hair hairPart front");
        var one1 = c.Ok(new { cmd = "chara.edit.materials", key = f, part = "hair", hairPart = 1 }, "part hair hairPart 1");
        c.Check((one["renderers"] as JArray)?.OfType<JObject>().All(x => (string)x["hairPart"] == "front") == true && JToken.DeepEquals(one["renderers"], one1["renderers"])
                && (one["renderers"] as JArray)?.Count == rs.Count(x => (string)x["hairPart"] == "front"), "hairPart front = hairPart 1 = the front renderers only");
        var st = c.Ok(new { cmd = "chara.edit.materials", key = f, part = "hair", hairPart = "front", props = new[] { "_MeshColorMask_B_ST" } }, "props [_MeshColorMask_B_ST]");
        c.Check(st["renderers"]?[0]?["slots"]?[0]?["props"] is JObject sp && sp.Count == 1 && sp["_MeshColorMask_B_ST"] is JArray, "props filter selects a texture's \"_ST\" alone", Ctx.Short(st.ToString()));
        c.Fails(new { cmd = "chara.edit.materials", key = f, part = "hair", hairPart = "nose" }, "bad_args", "hairPart \"nose\" → bad_args");
        c.Fails(new { cmd = "chara.edit.materials", key = f, part = "face", hairPart = "front" }, "bad_args", "hairPart without part hair → bad_args");
        var face = c.Ok(new { cmd = "chara.edit.materials", key = f }, "part face (default) unchanged");
        c.Check((face["renderers"] as JArray)?.OfType<JObject>().All(x => x["hairPart"] == null && !((x["slots"]?[0]?["props"] as JObject)?.Properties().Any(pp => pp.Name.EndsWith("_ST", StringComparison.Ordinal)) ?? false)) == true,
            "the face set has no hairPart and no \"_ST\" entries (4.1's reply)");
        // metallic → _Metallic direct (also outside 0..1), on every part with same setting
        foreach (var x in new[] { 0.3f, 1.5f })
        {
            c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.hair.back.metallic"] = x } }, "hair back metallic " + x);
            var q = c.Ok(new { cmd = "chara.edit.materials", key = f, part = "hair", props = new[] { "_Metallic" } }, "hair _Metallic");
            var vals = (q["renderers"] as JArray)?.OfType<JObject>().Where(rr => parts.Any(pp => ((pp["materials"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>()).Any(mm => (string)mm["renderer"] == (string)rr["name"])))
                .SelectMany(rr => (rr["slots"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>()).Select(s => (float?)s["props"]?["_Metallic"]).ToList() ?? new List<float?>();
            c.Check(vals.Count > 0 && vals.All(v => v == x) && W1TopLabel(c) == "Edit hair", "metallic " + x + " → _Metallic " + x + " on every rendHair slot of back and front (same setting; direct)", string.Join(", ", vals));
        }
        c.Check(W1UndoCount(c) == n0 + 2, "materials record nothing (the 2 sets one entry each)");
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (materials block)");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
    }

    // ---------------------------------------------------------------- chara.edit.digest parts hair

    static void C33Digest(Ctx c, string f)
    {
        int n0 = W1UndoCount(c);
        bool hex(JToken t) => t?.Type == JTokenType.String && ((string)t).Length == 64 && ((string)t).All(ch => "0123456789abcdef".IndexOf(ch) >= 0);
        string Sha(string s) { using (var h = SHA256.Create()) return string.Concat(h.ComputeHash(Encoding.UTF8.GetBytes(s ?? "")).Select(b => b.ToString("x2"))); }
        var d1 = c.Ok(new { cmd = "chara.edit.digest", key = f, parts = "hair", text = true }, "chara.edit.digest hair text");
        c.Check(hex(d1["materials"]) && hex(d1["creators"]) && hex(d1["bones"]) && hex(d1["chains"]) && (string)d1["creators"] == Sha("") && d1["normals"] == null && d1["bust"] == null,
            "parts hair: materials, creators (the empty text: the hair has no creators), bones, chains; no normals / bust", Ctx.Short(d1.ToString()));
        var tx = d1["texts"] as JObject;
        c.Check(tx != null && Sha((string)tx["chains"]) == (string)d1["chains"] && Sha((string)tx["bones"]) == (string)d1["bones"] && Sha((string)tx["materials"]) == (string)d1["materials"],
            "texts chains / bones / materials hash to the sections");
        c.Check(((string)tx?["chains"])?.Contains("chain hair0 bundle 0") == true && ((string)tx?["bones"])?.Contains("hair0 0 euler") == true && ((string)tx?["materials"])?.Contains("hair1") == true,
            "the texts name the hair parts' chains, bundles and renderers", Ctx.Short((string)tx?["chains"]));
        var face = c.Ok(new { cmd = "chara.edit.digest", key = f, parts = "face" }, "chara.edit.digest face");
        c.Check(face["chains"] == null, "parts face: no chains section");
        var all = c.Ok(new { cmd = "chara.edit.digest", key = f, parts = "all" }, "chara.edit.digest all");
        c.Check(hex(all["chains"]) && (string)all["chains"] == (string)d1["chains"] && hex(all["normals"]) && (string)all["materials"] != (string)d1["materials"], "parts all: the hair chains section, plus the body's");
        // which sections follow which edits
        c.Ok(C33Req(f, "back", "index", 0, "move", new[] { 0.8, 0.3, 0.6 }), "bundle move");
        var d2 = c.Call(new { cmd = "chara.edit.digest", key = f, parts = "hair" });
        c.Check((string)d2["bones"] != (string)d1["bones"] && (string)d2["materials"] == (string)d1["materials"], "a bundle move: bones change, materials equal");
        c.Ok(C33Req(f, "back", "index", 1, "noShake", true), "noShake");
        var d3 = c.Call(new { cmd = "chara.edit.digest", key = f, parts = "hair" });
        c.Check((string)d3["chains"] != (string)d2["chains"] && (string)d3["materials"] == (string)d2["materials"] && (string)d3["bones"] == (string)d2["bones"], "noShake: chains change, materials / bones equal");
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.hair.front.mesh.layout"] = new[] { 1.5, 1, 0, 0 } } }, "front mesh layout (1.5, 1, 0, 0)");
        var d4 = c.Call(new { cmd = "chara.edit.digest", key = f, parts = "hair" });
        c.Check((string)d4["materials"] != (string)d3["materials"] && (string)d4["bones"] == (string)d3["bones"] && (string)d4["chains"] == (string)d3["chains"], "a mesh layout: materials change, bones / chains equal");
        for (int k = 0; k < 3; k++) c.Ok(new { cmd = "undo" }, "undo digest edit " + (3 - k));
        var d5 = c.Call(new { cmd = "chara.edit.digest", key = f, parts = "hair" });
        c.Check(new[] { "materials", "creators", "bones", "chains" }.All(s => (string)d5[s] == (string)d1[s]), "after undo every hair section equals the first", Ctx.Short(d5.ToString()));
        c.Check(W1UndoCount(c) == n0, "digests record nothing (edits undone)");
    }

    // ---------------------------------------------------------------- chara.edit.info hair

    static void C33Info(Ctx c, string f, string m)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var i = c.Ok(new { cmd = "chara.edit.info", key = f }, "chara.edit.info BR-Chan");
        var h = i["hair"] as JObject;
        var hp = (h?["parts"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        var hair = (c.Call(new { cmd = "chara.edit.hair", key = f })["parts"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        c.Check(hp.Count == 4 && Enumerable.Range(0, 4).All(k => (int?)hp[k]["part"] == k && (string)hp[k]["name"] == C33Parts[k] && hair.Count == 4 && JToken.DeepEquals(hp[k]["facts"], hair[k]["facts"])
                && (string)hp[k]["item"] == (string)hair[k]["item"] && (string)hp[k]["prefab"] == (string)hair[k]["prefab"]),
            "hair.parts: part, name, item, prefab and facts per part = chara.edit.hair's", Ctx.Short(h?.ToString()));
        c.Check(h?["deferred"]?.Type == JTokenType.Boolean && !(bool)h["deferred"] && (h["lastFlush"] == null || h["lastFlush"].Type == JTokenType.Null || h["lastFlush"]?["dynBone"]?.Type == JTokenType.String),
            "hair.deferred false (edit mode: no preview flush), lastFlush {dynBone, deferred}", Ctx.Short(h?.ToString()));
        foreach (var (req, what, want) in new (object, string, string)[]
        {
            (C33Req(f, "front", "index", 1, "rotate", new[] { 0.6, 0.4, 0.5 }), "a bundle rotate", "light"),
            (C33Req(f, "front", "index", 1, "noShake", true), "a noShake toggle", "full"),
            (new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.hair.back.baseColor"] = new[] { 0.3, 0.2, 0.1, 1.0 } } }, "a hair colour", "none"),
            (new { cmd = "chara.edit.item", key = f, path = "chara.hair.back.item", @ref = "illusion.hs2#0" }, "a hair item change", "full"),
        })
        {
            c.Ok(req, what);
            var x = c.Call(new { cmd = "chara.edit.info", key = f });
            string a = (string)x["hair"]?["lastFlush"]?["dynBone"], b = (string)x["lastFlush"]?["dynBone"];
            c.Check(a == want && b == want && (bool?)x["hair"]?["lastFlush"]?["deferred"] == false, what + ": lastFlush.dynBone " + want + " (hair.lastFlush and lastFlush)", a + " / " + b);
        }
        var im = c.Ok(new { cmd = "chara.edit.info", key = m }, "chara.edit.info HERO107");
        c.Check((im["hair"]?["parts"] as JArray)?.Count == 4 && (bool?)im["hair"]?["parts"]?[1]?["facts"]?["useTopColor"] == false, "male: hair.parts, front without a top colour");
        c.Check(W1UndoCount(c) == n0 + 4, "info records nothing (the 4 edits one entry each)", "" + W1UndoCount(c));
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (info block)");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
    }

    // ---------------------------------------------------------------- batch

    static void C33Batch(Ctx c, string f)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        c.Ok(new
        {
            cmd = "batch",
            cmds = new object[]
            {
                C33Req(f, "back", "index", 0, "move", new[] { 0.2, 0.8, 0.5 }),
                new { cmd = "chara.edit.hairSkin", key = f, part = "front" },
                new { cmd = "chara.edit.swatch", key = f, path = "chara.hair.back.baseColor", @ref = "illusion.hs2#3" },
                new { cmd = "chara.edit.hair", key = f, part = "back" },
            },
        }, "a batch of chara.edit.hairBundle, hairSkin, swatch, hair");
        c.Check(W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Batch", "one entry \"Batch\"", W1UndoCount(c) + " " + W1TopLabel(c));
        c.Ok(new { cmd = "undo" }, "undo the batch");
        c.Check(W1DocSha(c) == sha0, "one undo restores scene.doc");
    }
}
