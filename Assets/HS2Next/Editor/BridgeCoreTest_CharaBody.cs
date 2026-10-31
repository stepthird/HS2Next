// C32 of BridgeCoreTest: the Phase 4.2 bridge of package E,
// further methods of the partial BridgeCoreTest (-only C32), edit mode, headless: registration (CoreCommands.Phase42Names);
// chara.edit.swatch (the seven rows of 336 by id write Unity's HSVToRGB(Base) with alpha 1, one entry "Edit body", the
// body and face colour creators rebuild, noop, several keys, errors); chara.edit.bust on BR-Chan (stored values, the breast
// chains' values = the bust stage's formula and the worked example, HB2's numbers for weights / softness outside 0..1,
// inert per particle, the breast normals: source, entry, rate, blendRate, SHA-256 = an independent hash of
// Lerp(NormalMin, NormalMax, r) of the NormalData asset and HS2's hash of the shipped normals, sizes −0.5 / 0.3 / 0.4999 /
// 1.5, undo) and on HERO107 (male: no normals, the chain values per BustOptions.MaleBustPhysics, decision A42-9);
// chara.edit.materials part body (o_body_cf / o_body_cm, the gloss lerp on the body and face draws, areola size direct);
// chara.edit.digest sections normals / bust (body / all, not face; follow edits and undo; texts; body bones = the shape
// map's destinations; no eye-look bones); chara.edit.info throttle / lastFlush.throttleWeight / bustNormals; chara.edit.list
// columns of 336 and 8; chara.edit.rtstats part body; read-only commands record nothing; a batch is one entry. Groups
// C1–C31 unchanged. Numbers only; the SFW targets only (nothing is captured).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HS2Next.Content;
using HS2Next.Content.Maker;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class BridgeCoreTest
{
    /// <summary>HS2's SHA-256 of the drawn o_body_cf normals of BR-Chan at a bust size ≥ 0.5 (= NormalMax = the shipped normals;
    /// HB3, Cache/compare/maker/body/bc3.json): little-endian float32 x, y, z per vertex.</summary>
    const string C32Hs2ShippedNormals = "d780d53c2d4e82254c1f049acb23945ee5b1165b880051350ce904b6ab7633d4";

    static void C32(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        var names = HS2Next.App.Commands.CoreCommands.Phase42Names;
        c.Check(names.SequenceEqual(HS2Next.App.Commands.CharaEditCommands.Phase42Names) && names.SequenceEqual(new[] { "chara.edit.swatch", "chara.edit.bust" })
                && names.All(n => reg.Find(n) != null), "Phase42Names = chara.edit.swatch, chara.edit.bust, both registered", string.Join(", ", names.Where(n => reg.Find(n) == null)));
        c.Check(HS2Next.App.Commands.CoreCommands.AllNames.Count(n => names.Contains(n)) == 2, "CoreCommands.AllNames lists them once");
        foreach (var n in names)
        {
            var h = c.Ok(new { cmd = "help", name = n }, "help " + n);
            c.Check(h.ToString().Contains(n), "help describes " + n, Ctx.Short(c.LastText));
        }
        var hm = c.Ok(new { cmd = "help", name = "chara.edit.materials" }, "help chara.edit.materials");
        c.Check(hm.ToString().Contains("\"part\""), "chara.edit.materials documents part", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "scene.new" }, "scene.new");
        string f = C30Load(c, c.FemaleRel, "female", "chara.load BR-Chan");
        string m = C30Load(c, c.MaleRel, "male", "chara.load HERO107");
        if (f == null || m == null) return;
        string item = W1AddItem(c);
        c.Frames(2);
        C32List(c);
        C32Swatch(c, f, m, item);
        C32Bust(c, f, item);
        C32BustMale(c, m);
        C32Materials(c, f, m);
        C32Digest(c, f, m);
        C32Info(c, f, m);
        C32RtStats(c, f);
        C32Batch(c, f);
    }

    static JToken C32Body(Ctx c, string key, string member) => C30Value(c, key, "chara.body." + member);

    static void C32Set(Ctx c, string key, string path, object value, string what = null) =>
        c.Ok(new { cmd = "chara.edit.set", key, set = new Dictionary<string, object> { [path] = value } }, what ?? path + " = " + Newtonsoft.Json.JsonConvert.SerializeObject(value));

    static float[] C32Colour(Ctx c, string key) => C31Floats(C32Body(c, key, "skin.color"));

    /// <summary>The independent hash of a normals array: SHA-256 of little-endian float32 x, y, z per vertex, lower-case hex.</summary>
    static string C32NormalsSha(Vector3[] n)
    {
        var b = new byte[n.Length * 12];
        for (int i = 0; i < n.Length; i++)
            for (int k = 0; k < 3; k++)
            {
                var x = BitConverter.GetBytes(n[i][k]);
                if (!BitConverter.IsLittleEndian) Array.Reverse(x);
                Buffer.BlockCopy(x, 0, b, i * 12 + k * 4, 4);
            }
        using (var h = SHA256.Create()) return string.Concat(h.ComputeHash(b).Select(x => x.ToString("x2", CultureInfo.InvariantCulture)));
    }

    static Vector3[] C32Lerp(Vector3[] a, Vector3[] b, float r)
    {
        var o = new Vector3[a.Length];
        for (int i = 0; i < a.Length; i++) o[i] = Vector3.Lerp(a[i], b[i], r);
        return o;
    }

    static float C32F(JToken t) => t == null || t.Type == JTokenType.Null ? float.NaN : (float)t;

    // ---------------------------------------------------------------- chara.edit.list columns of 336 and 8

    static void C32List(Ctx c)
    {
        var lists = MakerCatalog.For(c.Services.Catalog);
        int n0 = W1UndoCount(c);
        var l = c.Ok(new { cmd = "chara.edit.list", category = 336, sex = "female", columns = new[] { "BaseH", "BaseS", "BaseV", "SampleH", "SampleS", "SampleV" } }, "chara.edit.list 336 columns Base* / Sample*");
        var rows = (l["rows"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        bool ok = rows.Count == 7;
        for (int i = 0; ok && i < rows.Count; i++)
        {
            var v = rows[i]["values"] as JObject;
            ok &= (string)rows[i]["ref"] == "illusion.hs2#" + i && v != null && lists.ColourPreset(336, ContentId.Vanilla(i), out HsvColour b, out HsvColour s)
                  && P(v["BaseH"]) == b.H && P(v["BaseS"]) == b.S && P(v["BaseV"]) == b.V && P(v["SampleH"]) == s.H && P(v["SampleS"]) == s.S && P(v["SampleV"]) == s.V;
        }
        c.Check(ok, "336: 7 rows illusion.hs2#0..6, values {BaseH…SampleV} = MakerLists.ColourPreset", Ctx.Short(rows.FirstOrDefault()?.ToString()));
        var r = c.Ok(new { cmd = "chara.edit.list", category = 8, sex = "female", limit = 1000, columns = new[] { "CenterX", "MoveX", "CenterY", "MoveY", "CenterScale", "AddScale" } }, "chara.edit.list 8 columns Center* / Move* / *Scale");
        var rr = (r["rows"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        var r0 = rr.FirstOrDefault()?["values"] as JObject;
        c.Check(rr.Count == lists.Rows(8, 1).Count && rr.Count >= 20 && (string)rr[0]["ref"] == "illusion.hs2#0" && r0 != null
                && P(r0["CenterX"]) == 0.375f && P(r0["MoveX"]) == 0.125f && P(r0["CenterY"]) == -0.375f && P(r0["MoveY"]) == 0.125f && P(r0["CenterScale"]) == 13f && P(r0["AddScale"]) == 10f,
            "8: " + rr.Count + " rows (= MakerLists), region 0 «胸» = (0.375, 0.125, −0.375, 0.125, 13, 10)", Ctx.Short(rr.FirstOrDefault()?.ToString()));
        var lm = c.Ok(new { cmd = "chara.edit.list", category = 336, sex = "male" }, "chara.edit.list 336 male");
        c.Check((int?)lm["total"] == 7, "336 is shared: 7 rows for males too", "" + lm["total"]);
        c.Check(W1UndoCount(c) == n0, "lists record nothing");

        float P(JToken t) => t == null || t.Type == JTokenType.Null ? float.NaN : float.Parse((string)t, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    // ---------------------------------------------------------------- chara.edit.swatch

    static void C32Swatch(Ctx c, string f, string m, string item)
    {
        var lists = MakerCatalog.For(c.Services.Catalog);
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        // «タン» (row 3): HSVToRGB(0.058, 0.21, 0.7) = (0.7, 0.6042, 0.553); Unity's HSVToRGB of the row's Base columns
        c.Check(lists.ColourPreset(336, ContentId.Vanilla(3), out HsvColour b3, out _), "336 row 3 exists");
        var want3 = Color.HSVToRGB(b3.H, b3.S, b3.V);
        var before = C31Rebuilds(c, f);
        var r = c.Ok(new { cmd = "chara.edit.swatch", key = f, path = "chara.body.skin.color", @ref = "illusion.hs2#3" }, "chara.edit.swatch row 3 «タン»");
        var v = C32Colour(c, f);
        c.Check(v.Length == 4 && v[0] == want3.r && v[1] == want3.g && v[2] == want3.b && v[3] == 1f,
            "body.skin.color = Color.HSVToRGB(BaseH, BaseS, BaseV) of row 3 bit for bit, alpha 1", string.Join(", ", v) + " vs " + want3.r + ", " + want3.g + ", " + want3.b);
        c.Check(Math.Abs(v[0] - 0.7f) < 1e-4 && Math.Abs(v[1] - 0.6042f) < 1e-4 && Math.Abs(v[2] - 0.553f) < 1e-4, "row 3 = the census value (0.7, 0.6042, 0.553)", string.Join(", ", v));
        c.Check(C30Has(r["changed"], "chara.body.skin.color") && W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Edit body", "changed chara.body.skin.color, one entry \"Edit body\"", Ctx.Short(r.ToString()) + " " + W1TopLabel(c));
        var after = C31Rebuilds(c, f);
        bool Re(string name) => after.TryGetValue(name, out var x) && before.TryGetValue(name, out var y) && x != y;
        c.Check(Re("create_skin_body") && Re("create_skin_face") && !Re("create_skin detail_body") && !Re("create_skin detail_face"),
            "a swatch push rebuilds the body and face colour creators only (HB1's HS2Next column)", string.Join(", ", after.Where(kv => Re(kv.Key)).Select(kv => kv.Key)));
        var again = c.Ok(new { cmd = "chara.edit.swatch", key = f, path = "chara.body.skin.color", @ref = "illusion.hs2#3" }, "the same push again");
        c.Check(C30Has(again["noop"], "chara.body.skin.color") && W1UndoCount(c) == n0 + 1, "noop, no entry", Ctx.Short(again.ToString()));
        // the seven rows by id (HS2's buttons), full path accepted
        bool all = true;
        var detail = new List<string>();
        for (int i = 0; i < 7; i++)
        {
            c.Ok(new { cmd = "chara.edit.swatch", key = f, path = i % 2 == 0 ? "chara.body.skin.color" : "appearance.chara.body.skin.color", @ref = "illusion.hs2#" + i }, "chara.edit.swatch row " + i);
            lists.ColourPreset(336, ContentId.Vanilla(i), out HsvColour b, out _);
            var w = Color.HSVToRGB(b.H, b.S, b.V);
            var got = C32Colour(c, f);
            bool ok = got.Length == 4 && got[0] == w.r && got[1] == w.g && got[2] == w.b && got[3] == 1f;
            all &= ok;
            detail.Add(i + ":" + string.Join("/", got.Select(x => x.ToString("0.#####", CultureInfo.InvariantCulture))));
        }
        c.Check(all, "rows 0..6: HSVToRGB(Base) with alpha 1 (relative and full paths)", string.Join(" ", detail));
        c.Info("swatch values " + string.Join(" ", detail));
        // several keys: one entry (336 is shared by both sexes)
        int n1 = W1UndoCount(c);
        c.Ok(new { cmd = "chara.edit.swatch", keys = new[] { f, m }, path = "chara.body.skin.color", @ref = "illusion.hs2#5" }, "chara.edit.swatch keys [f, m] row 5");
        lists.ColourPreset(336, ContentId.Vanilla(5), out HsvColour b5, out _);
        var w5 = Color.HSVToRGB(b5.H, b5.S, b5.V);
        var vf = C32Colour(c, f); var vm = C32Colour(c, m);
        c.Check(vf.SequenceEqual(new[] { w5.r, w5.g, w5.b, 1f }) && vm.SequenceEqual(new[] { w5.r, w5.g, w5.b, 1f }) && W1UndoCount(c) == n1 + 1 && W1TopLabel(c) == "Edit body",
            "both characters get row 5, one entry \"Edit body\"", string.Join(", ", vf) + " | " + string.Join(", ", vm));
        int n2 = W1UndoCount(c);
        c.Fails(new { cmd = "chara.edit.swatch", key = f, path = "chara.body.sunburn.color", @ref = "illusion.hs2#0" }, "bad_args", "another colour path → bad_args");
        c.Fails(new { cmd = "chara.edit.swatch", key = f, path = "chara.body.nosuch", @ref = "illusion.hs2#0" }, "bad_args", "an unknown path → bad_args");
        c.Fails(new { cmd = "chara.edit.swatch", key = f, path = "chara.body.skin.color", @ref = "illusion.hs2#7" }, "not_found", "id 7 (outside HS2's seven buttons) → not_found");
        c.Fails(new { cmd = "chara.edit.swatch", key = f, path = "chara.body.skin.color", @ref = "illusion.hs2#99" }, "not_found", "an unknown row → not_found");
        c.Fails(new { cmd = "chara.edit.swatch", key = f, path = "chara.body.skin.color", @ref = "x" }, "bad_args", "a ref that does not parse → bad_args");
        c.Fails(new { cmd = "chara.edit.swatch", key = f, path = "chara.body.skin.color", @ref = 3 }, "bad_args", "a ref that is not a string → bad_args");
        c.Fails(new { cmd = "chara.edit.swatch", key = f, path = "chara.body.skin.color" }, "bad_args", "no ref → bad_args");
        c.Fails(new { cmd = "chara.edit.swatch", key = f, @ref = "illusion.hs2#0" }, "bad_args", "no path → bad_args");
        c.Fails(new { cmd = "chara.edit.swatch", key = "nosuchnode", path = "chara.body.skin.color", @ref = "illusion.hs2#0" }, "not_found", "an unknown key → not_found");
        c.Fails(new { cmd = "chara.edit.swatch", key = item, path = "chara.body.skin.color", @ref = "illusion.hs2#0" }, "wrong_kind", "an item → wrong_kind");
        c.Check(W1UndoCount(c) == n2, "failed pushes record nothing");
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (swatch block)");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
    }

    // ---------------------------------------------------------------- chara.edit.bust (BR-Chan)

    /// <summary>The breast chains of a chara.edit.bust reply (left, right).</summary>
    static List<JObject> C32Chains(JObject b) => (b?["physics"]?["chains"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();

    /// <summary>The chain values of <paramref name="b"/> equal the bust stage's formula for (s, w, size) within tol (both chains).</summary>
    static bool C32ChainsFollow(JObject b, float s, float w, float size, double tol, out string detail)
    {
        HS2Next.Motion.DynamicBones.BustStage.BustSoftParams(s, size, out float d, out float e, out float st);
        float gy = HS2Next.Motion.DynamicBones.BustStage.BustGravityY(s, w, size);
        var ch = C32Chains(b);
        detail = "want d " + d + " e " + e + " s " + st + " gy " + gy + "; got " + string.Join(" | ", ch.Select(x => x["damping"] + " " + x["elasticity"] + " " + x["stiffness"] + " " + x["gravity"]?.ToString(Newtonsoft.Json.Formatting.None)));
        return ch.Count == 2 && ch.All(x => Math.Abs(C32F(x["damping"]) - d) <= tol && Math.Abs(C32F(x["elasticity"]) - e) <= tol && Math.Abs(C32F(x["stiffness"]) - st) <= tol
                                            && x["gravity"] is JArray g && g.Count == 3 && C32F(g[0]) == 0f && Math.Abs(C32F(g[1]) - gy) <= tol && C32F(g[2]) == 0f);
    }

    static void C32Bust(Ctx c, string f, string item)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var b = c.Ok(new { cmd = "chara.edit.bust", key = f }, "chara.edit.bust BR-Chan");
        float size = C32F(C32Body(c, f, "shape[1]")), soft = C32F(C32Body(c, f, "bust.softness")), weight = C32F(C32Body(c, f, "bust.weight"));
        c.Check((int?)b["sex"] == 1 && C32F(b["size"]) == size && C32F(b["softness"]) == soft && C32F(b["weight"]) == weight,
            "sex 1; size / softness / weight = the stored documents (" + size + ", " + soft + ", " + weight + ")", Ctx.Short(b.ToString()));
        var ph = b["physics"] as JObject;
        var ch = C32Chains(b);
        c.Check(ph != null && ph["enabled"]?.Type == JTokenType.Boolean && !(bool)ph["enabled"] && ph["stageEnabled"]?.Type == JTokenType.Boolean && ph["maleBustPhysics"] == null
                && C32F(ph["softness"]) == soft && C32F(ph["weight"]) == weight && C32F(ph["size"]) == size,
            "physics {enabled false (C30Load turned physics off), stageEnabled, the stage's softness / weight / size = the documents}", Ctx.Short(ph?.ToString()));
        c.Check(ch.Count == 2 && (string)ch[0]["name"] == "Mune_L" && (string)ch[1]["name"] == "Mune_R" && (int?)ch[0]["kind"] == 0 && (int?)ch[1]["kind"] == 1
                && ch.All(x => (int?)x["pattern"] == 0 && x["active"]?.Type == JTokenType.Boolean && (int?)x["particles"] > 0 && (x["inert"] as JArray)?.Count == (int?)x["particles"]),
            "chains: the two breasts Mune_L / Mune_R (kind 0 / 1), pattern 0, inert per particle", Ctx.Short(ph?["chains"]?.ToString()));
        c.Check(C32ChainsFollow(b, soft, weight, size, 1e-7, out string det), "chain values = BustStage.BustSoftParams / BustGravityY of the documents", det);
        // the worked example (s 0.57, b 0.58): t 0.3406 → stiffness 0.38692, elasticity 0.16594, damping 0.13188; HB2 gravity −0.00320537
        bool br = Math.Abs(soft - 0.57f) < 1e-6 && Math.Abs(size - 0.58f) < 1e-6;
        if (!br) c.Warn("BR-Chan's stored softness / size are not 0.57 / 0.58: the worked example is not checked");
        else
            c.Check(ch.Count == 2 && ch.All(x => Math.Abs(C32F(x["stiffness"]) - 0.38692f) < 1e-6 && Math.Abs(C32F(x["elasticity"]) - 0.16594f) < 1e-6 && Math.Abs(C32F(x["damping"]) - 0.13188f) < 1e-6
                                                 && Math.Abs(C32F(x["gravity"]?[1]) - (-0.05f * weight * 0.58f * 0.57f * 0.5f)) < 1e-6),
                "the worked example (stiffness 0.38692, elasticity 0.16594, damping 0.13188, gravity y = −0.05 · w · b · s · 0.5) within 1e-6", det);
        // inert: the asset's per-particle values (HS2 HB2: 1, 0.8, 0.8, 0.8, 0.7, 1)
        var inert = ch.Count > 0 ? (ch[0]["inert"] as JArray)?.Select(x => C32F(x)).ToArray() ?? new float[0] : new float[0];
        var hs2Inert = new[] { 1f, 0.8f, 0.8f, 0.8f, 0.7f, 1f };
        c.Check(ch.Count == 2 && inert.SequenceEqual(hs2Inert) && ch[1]["inert"].Select(x => C32F(x)).SequenceEqual(hs2Inert),
            "inert per particle of both chains = HS2's pattern-0 values (1, 0.8, 0.8, 0.8, 0.7, 1; HB2)", string.Join(", ", inert));
        // breast normals: vanilla source, one entry o_body_cf, the shipped normals at size ≥ 0.5
        var nr = b["normals"] as JObject;
        var src = nr?["source"] as JObject;
        var ent = (nr?["entries"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        c.Check(src != null && (string)src["kind"] == "vanilla" && src["guid"]?.Type == JTokenType.Null && (string)src["asset"] == "p_cf_body_00_Nml" && (string)src["bundle"] == "chara/oo_base.unity3d"
                && nr["missing"]?.Type == JTokenType.Boolean && !(bool)nr["missing"],
            "normals.source {kind vanilla, asset p_cf_body_00_Nml, bundle chara/oo_base.unity3d}, missing false", Ctx.Short(nr?.ToString()));
        bool readOk = HS2Next.Chara.Edit.Units.BustNormalsUnit.ReadEntry(c.Services.Catalog, null, "chara/oo_base.unity3d", "p_cf_body_00_Nml", "o_body_cf", out var nmin, out var nmax);
        c.Check(readOk && nmin.Length == nmax.Length && nmax.Length > 0, "the NormalData asset reads (o_body_cf: " + (nmax?.Length ?? 0) + " normals)");
        if (!readOk) return;
        c.Check(ent.Count == 1 && (string)ent[0]["object"] == "o_body_cf" && (int?)ent[0]["vertices"] == nmax.Length && (int?)ent[0]["min"] == nmin.Length && (int?)ent[0]["max"] == nmax.Length
                && (bool?)ent[0]["applicable"] == true && (bool?)ent[0]["applied"] == true,
            "entries [{object o_body_cf, vertices = min = max = " + nmax.Length + ", applicable, applied}]", Ctx.Short(nr?["entries"]?.ToString()));
        string shipped = C32NormalsSha(nmax);
        c.Check(C32F(nr?["rate"]) == 1f && C32F(nr?["blendRate"]) == 1f && (bool?)nr?["applied"] == true && (bool?)nr?["meshCopied"] == true && (string)nr?["sha256"] == shipped
                && ((string)nr?["mesh"])?.EndsWith("/" + nmax.Length) == true,
            "size " + size + ": rate 1, blendRate 1, applied, meshCopied, sha256 = the independent hash of NormalMax, mesh name/" + nmax.Length, Ctx.Short(nr?.ToString()));
        c.Check(shipped == C32Hs2ShippedNormals && (string)nr?["sha256"] == C32Hs2ShippedNormals, "sha256 = HS2's hash of the shipped normals (HB3: d780d53c…; the HS2 side's bytes)", shipped);
        c.Info("BR-Chan normals: mesh " + nr?["mesh"] + ", copies " + nr?["copies"] + ", blends " + nr?["blends"] + ", lastCopyMs " + nr?["lastCopyMs"] + ", lastBlendMs " + nr?["lastBlendMs"] + ", copyBytes " + nr?["copyBytes"]);
        // sizes (HB3's rates): r = 1 for size ≥ 0.5, else InverseLerp(0, 0.5, size)
        foreach (var (sz, want) in new[] { (0.3f, 0.6f), (0.4999f, 0.9998000264f), (0f, 0f), (-0.5f, 0f), (1.5f, 1f), (0.25f, 0.5f) })
        {
            C32Set(c, f, "chara.body.shape[1]", sz, "bust size " + sz);
            var x = c.Ok(new { cmd = "chara.edit.bust", key = f }, "chara.edit.bust at size " + sz);
            float r = HS2Next.Chara.Edit.Units.BustNormalsUnit.RateOf(sz);
            var xn = x["normals"] as JObject;
            string sha = C32NormalsSha(C32Lerp(nmin, nmax, r));
            c.Check(C32F(x["size"]) == sz && C32F(xn?["rate"]) == r && Math.Abs(r - want) < 1e-7 && C32F(xn?["blendRate"]) == r && (bool?)xn?["applied"] == true && (string)xn?["sha256"] == sha,
                "size " + sz + ": rate " + r + " (HB3 " + want + "), blendRate, applied, sha256 = hash(Lerp(Min, Max, r))", Ctx.Short(xn?.ToString()));
            c.Check(C32ChainsFollow(x, soft, weight, sz, 1e-7, out string d2), "size " + sz + ": the chains follow the size", d2);
        }
        // weights / softness outside 0..1 (Y10 / HB2 on HS2: the weight clamped, the softness unclamped in the gravity product)
        C32Set(c, f, "chara.body.shape[1]", 0.58f, "bust size 0.58");
        foreach (var (s, w, gy, stiff) in new[] { (0.57f, 1.5f, -0.008265f, float.NaN), (0.57f, -0.5f, 0f, float.NaN), (1.5f, 0.388f, -0.008439f, 0.0316f), (-0.5f, 0.388f, 0.002813f, 1f) })
        {
            c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.body.bust.softness"] = s, ["chara.body.bust.weight"] = w } }, "softness " + s + ", weight " + w);
            var x = c.Ok(new { cmd = "chara.edit.bust", key = f }, "chara.edit.bust softness " + s + " weight " + w);
            var xc = C32Chains(x);
            c.Check(C32ChainsFollow(x, s, w, 0.58f, 1e-7, out string d3) && xc.All(k => Math.Abs(C32F(k["gravity"]?[1]) - gy) < 1e-6 && (float.IsNaN(stiff) || Math.Abs(C32F(k["stiffness"]) - stiff) < 1e-4)),
                "softness " + s + ", weight " + w + ": gravity y " + gy + (float.IsNaN(stiff) ? "" : ", stiffness " + stiff) + " (HS2's HB2 numbers)", d3);
        }
        c.Ok(new { cmd = "chara.motion", key = f, physics = true }, "physics on");
        var on = c.Ok(new { cmd = "chara.edit.bust", key = f }, "chara.edit.bust with physics on");
        c.Check((bool?)on["physics"]?["enabled"] == true && (bool?)on["physics"]?["stageEnabled"] == true, "physics on: enabled, stageEnabled", Ctx.Short(on["physics"]?.ToString()));
        c.Ok(new { cmd = "chara.motion", key = f, physics = false }, "physics off again");
        c.Fails(new { cmd = "chara.edit.bust", key = item }, "wrong_kind", "an item → wrong_kind");
        c.Fails(new { cmd = "chara.edit.bust", key = "nosuchnode" }, "not_found", "an unknown key → not_found");
        c.Check(W1UndoCount(c) == n0 + 11, "chara.edit.bust records nothing (the 11 sets one entry each)", "" + W1UndoCount(c));
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (bust block)");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
        var back = c.Ok(new { cmd = "chara.edit.bust", key = f }, "chara.edit.bust after undo");
        bool follows = C32ChainsFollow(back, soft, weight, size, 1e-7, out string d4);
        c.Check((string)back["normals"]?["sha256"] == shipped && C32F(back["normals"]?["rate"]) == 1f && follows,
            "after undo: the shipped normals again, chains of the stored values", d4);
    }

    // ---------------------------------------------------------------- chara.edit.bust (HERO107, male)

    static void C32BustMale(Ctx c, string m)
    {
        int n0 = W1UndoCount(c);
        var b = c.Ok(new { cmd = "chara.edit.bust", key = m }, "chara.edit.bust HERO107");
        float size = C32F(C32Body(c, m, "shape[1]")), soft = C32F(C32Body(c, m, "bust.softness")), weight = C32F(C32Body(c, m, "bust.weight"));
        var nr = b["normals"] as JObject;
        c.Check((int?)b["sex"] == 0 && C32F(b["size"]) == size && C32F(b["softness"]) == soft && C32F(b["weight"]) == weight, "sex 0; stored size / softness / weight", Ctx.Short(b.ToString()));
        c.Check(nr != null && nr["source"]?.Type == JTokenType.Null && (nr["entries"] as JArray)?.Count == 0 && nr["rate"]?.Type == JTokenType.Null && nr["blendRate"]?.Type == JTokenType.Null
                && (bool?)nr["applied"] == false && (bool?)nr["meshCopied"] == false && ((string)nr["sha256"])?.Length == 64 && ((string)nr["mesh"])?.Contains("/") == true,
            "male: no breast normals (source null, entries [], rate / blendRate null, not applied, no copy); sha256 of the drawn body mesh given", Ctx.Short(nr?.ToString()));
        var ph = b["physics"] as JObject;
        c.Check(ph != null && (bool?)ph["maleBustPhysics"] == true && (bool?)ph["enabled"] == false && C32Chains(b).Count == 2,
            "male: maleBustPhysics true (default; decision A42-9), enabled false while physics is off, two breast chains", Ctx.Short(ph?.ToString()));
        c.Check(C32ChainsFollow(b, soft, weight, size, 1e-7, out string det), "male chains = the female formula of the male card's values (HS2 writes them alike, package H)", det);
        if (Math.Abs(soft - 0.5f) < 1e-6 && Math.Abs(weight - 0.05f) < 1e-6 && Math.Abs(size - 0.5f) < 1e-6)
            c.Check(C32Chains(b).All(x => Math.Abs(C32F(x["gravity"]?[1]) - (-0.0003125f)) < 1e-6 && Math.Abs(C32F(x["stiffness"]) - 0.532f) < 1e-6
                                         && Math.Abs(C32F(x["elasticity"]) - 0.174f) < 1e-6 && Math.Abs(C32F(x["damping"]) - 0.148f) < 1e-6),
                "HERO107 = HS2's numbers (gravity y −0.0003125, stiffness 0.532, elasticity 0.174, damping 0.148)", det);
        else c.Warn("HERO107's stored bust values are not (0.5, 0.05, 0.5): HS2's numbers not compared");
        c.Ok(new { cmd = "chara.motion", key = m, physics = true }, "male physics on");
        var on = c.Ok(new { cmd = "chara.edit.bust", key = m }, "chara.edit.bust male physics on");
        c.Check((bool?)on["physics"]?["enabled"] == true, "male, physics on: enabled (BustOptions.MaleBustPhysics, not \"physics off\")", Ctx.Short(on["physics"]?.ToString()));
        c.Ok(new { cmd = "chara.motion", key = m, physics = false }, "male physics off again");
        c.Check(W1UndoCount(c) == n0, "records nothing");
    }

    // ---------------------------------------------------------------- chara.edit.materials part body

    static void C32Materials(Ctx c, string f, string m)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var bf = c.Ok(new { cmd = "chara.edit.materials", key = f, part = "body" }, "chara.edit.materials BR-Chan part body");
        var rs = (bf["renderers"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        c.Check(rs.Count >= 1 && rs.All(x => (string)x["name"] == "o_body_cf") && rs[0]["slots"]?[0]?["props"] is JObject p0 && p0["_NipScale"] != null && p0["_Gloss"] != null,
            "part body (female): o_body_cf with its props (_NipScale, _Gloss)", string.Join(",", rs.Select(x => (string)x["name"])));
        var bm = c.Ok(new { cmd = "chara.edit.materials", key = m, part = "body" }, "chara.edit.materials HERO107 part body");
        var rm = (bm["renderers"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        c.Check(rm.Count >= 1 && rm.All(x => (string)x["name"] == "o_body_cm"), "part body (male): o_body_cm", string.Join(",", rm.Select(x => (string)x["name"])));
        var face = c.Ok(new { cmd = "chara.edit.materials", key = f, part = "face" }, "chara.edit.materials part face");
        var dflt = c.Ok(new { cmd = "chara.edit.materials", key = f }, "chara.edit.materials (default)");
        c.Check(JToken.DeepEquals(face["renderers"], dflt["renderers"]) && (face["renderers"] as JArray)?.OfType<JObject>().Any(x => (string)x["name"] == "o_head") == true,
            "part face = the default (the face set, 4.1)");
        var over = c.Ok(new { cmd = "chara.edit.materials", key = f, part = "body", renderers = new[] { "o_head" }, props = new[] { "_Gloss" } }, "renderers overrides part");
        c.Check((over["renderers"] as JArray)?.OfType<JObject>().All(x => (string)x["name"] == "o_head") == true && (over["renderers"] as JArray)?.Count >= 1, "renderers [o_head] with part body → o_head");
        // body.skin.gloss → body and face _Gloss = lerp(0, 0.8, g) (+ 0.2 · gloss state 0); body.nipple.areola → _NipScale direct (unclamped)
        foreach (var g in new[] { 0.8f, 1.5f, -0.5f })
        {
            C32Set(c, f, "chara.body.skin.gloss", g, "body gloss " + g);
            float want = Mathf.Lerp(0f, 0.8f, g);
            var x = c.Ok(new { cmd = "chara.edit.materials", key = f, part = "body", props = new[] { "_Gloss" } }, "body _Gloss at gloss " + g);
            var y = c.Ok(new { cmd = "chara.edit.materials", key = f, renderers = new[] { "o_head" }, props = new[] { "_Gloss" } }, "face _Gloss at gloss " + g);
            float gb = C32F(x["renderers"]?[0]?["slots"]?[0]?["props"]?["_Gloss"]), gf = C32F(y["renderers"]?[0]?["slots"]?[0]?["props"]?["_Gloss"]);
            c.Check(Math.Abs(gb - want) < 1e-6 && Math.Abs(gf - want) < 1e-6, "gloss " + g + " → body and face _Gloss = lerp(0, 0.8, g) = " + want + " (clamped lerp)", gb + " / " + gf);
        }
        C32Set(c, f, "chara.body.nipple.areola", 1.5f, "areola size 1.5");
        var ns = c.Ok(new { cmd = "chara.edit.materials", key = f, part = "body", props = new[] { "_NipScale" } }, "body _NipScale");
        c.Check(C32F(ns["renderers"]?[0]?["slots"]?[0]?["props"]?["_NipScale"]) == 1.5f, "areola 1.5 → _NipScale 1.5 (direct, unclamped)", Ctx.Short(ns.ToString()));
        c.Fails(new { cmd = "chara.edit.materials", key = f, part = "nose" }, "bad_args", "part nose → bad_args (hair is a part since 4.3, C33)");
        c.Fails(new { cmd = "chara.edit.materials", key = f, part = 1 }, "bad_args", "part not a string → bad_args");
        c.Check(W1UndoCount(c) == n0 + 4, "materials record nothing (the 4 sets one entry each)", "" + W1UndoCount(c));
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (materials block)");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
    }

    // ---------------------------------------------------------------- chara.edit.digest sections normals / bust

    static void C32Digest(Ctx c, string f, string m)
    {
        int n0 = W1UndoCount(c);
        bool hex(JToken t) => t?.Type == JTokenType.String && ((string)t).Length == 64 && ((string)t).All(ch => "0123456789abcdef".IndexOf(ch) >= 0);
        string Sha(string s) { using (var h = SHA256.Create()) return string.Concat(h.ComputeHash(Encoding.UTF8.GetBytes(s ?? "")).Select(b => b.ToString("x2"))); }
        var face = c.Ok(new { cmd = "chara.edit.digest", key = f, parts = "face" }, "chara.edit.digest face");
        c.Check(face["normals"] == null && face["bust"] == null, "parts face: no normals / bust sections");
        var d1 = c.Ok(new { cmd = "chara.edit.digest", key = f, parts = "body", text = true }, "chara.edit.digest body text");
        c.Check(hex(d1["materials"]) && hex(d1["creators"]) && hex(d1["bones"]) && hex(d1["normals"]) && hex(d1["bust"]), "parts body: materials, creators, bones, normals, bust (SHA-256 hex)", Ctx.Short(d1.ToString()));
        var tx = d1["texts"] as JObject;
        c.Check(tx != null && Sha((string)tx["normals"]) == (string)d1["normals"] && Sha((string)tx["bust"]) == (string)d1["bust"] && Sha((string)tx["bones"]) == (string)d1["bones"],
            "texts normals / bust / bones hash to the sections");
        string bustSha = (string)c.Call(new { cmd = "chara.edit.bust", key = f })["normals"]?["sha256"];
        var nline = ((string)tx?["normals"] ?? "").Split('\n').FirstOrDefault(l => l.Contains("o_body_cf"));
        c.Check(nline != null && bustSha != null && nline.EndsWith(" " + bustSha), "the normals text's o_body_cf line carries chara.edit.bust's sha256", nline);
        var bl = ((string)tx?["bones"] ?? "").Split('\n').Where(l => l.Length > 0).ToList();
        var dst = HS2Next.Chara.Edit.FaceDigest.BodyShapeDestinations();
        c.Check(bl.Count == dst.Count && bl.All(l => l.StartsWith("body ")) && bl.Any(l => l.StartsWith("body cf_N_height ")), "body bones = the " + dst.Count + " destinations of the body shape map (cf_N_height included)", bl.Count + " lines");
        c.Check(((string)tx?["bust"])?.Contains("chain Mune_L") == true && ((string)tx?["bust"])?.StartsWith("bust female") == true, "the bust text names the stage and the chains", Ctx.Short((string)tx?["bust"]));
        var all = c.Ok(new { cmd = "chara.edit.digest", key = f, parts = "all", text = true }, "chara.edit.digest all text");
        c.Check(hex(all["normals"]) && hex(all["bust"]) && (string)all["normals"] == (string)d1["normals"] && (string)all["bust"] == (string)d1["bust"], "parts all: the same normals / bust sections as body");
        c.Check(!((string)all["texts"]?["bones"] ?? "").Contains("cf_J_look_") && !((string)tx?["bones"] ?? "").Contains("cf_J_look_"), "no eye-look bones (cf_J_look_*) in any bone section");
        var dm = c.Ok(new { cmd = "chara.edit.digest", key = m, parts = "body" }, "chara.edit.digest HERO107 body");
        c.Check(hex(dm["normals"]) && hex(dm["bust"]) && (string)dm["normals"] != (string)d1["normals"], "male body digest: its own normals / bust sections");
        // the sections follow edits
        C32Set(c, f, "chara.body.shape[1]", 0.3f, "bust size 0.3");
        var d2 = c.Call(new { cmd = "chara.edit.digest", key = f, parts = "body" });
        c.Check((string)d2["normals"] != (string)d1["normals"] && (string)d2["bust"] != (string)d1["bust"] && (string)d2["bones"] != (string)d1["bones"], "bust size 0.3: normals, bust and bones change");
        C32Set(c, f, "chara.body.bust.softness", 0.9f, "softness 0.9");
        var d3 = c.Call(new { cmd = "chara.edit.digest", key = f, parts = "body" });
        c.Check((string)d3["bust"] != (string)d2["bust"] && (string)d3["normals"] == (string)d2["normals"] && (string)d3["materials"] == (string)d2["materials"], "softness: the bust section only (normals, materials equal)");
        C32Set(c, f, "chara.body.nail.color", new[] { 0.9, 0.3, 0.3, 1.0 }, "nail colour");
        var d4 = c.Call(new { cmd = "chara.edit.digest", key = f, parts = "body" });
        c.Check((string)d4["materials"] != (string)d3["materials"] && (string)d4["normals"] == (string)d3["normals"] && (string)d4["bust"] == (string)d3["bust"], "nail colour: materials change, normals / bust equal");
        for (int k = 0; k < 3; k++) c.Ok(new { cmd = "undo" }, "undo digest edit " + (3 - k));
        var d5 = c.Call(new { cmd = "chara.edit.digest", key = f, parts = "body" });
        c.Check(new[] { "materials", "creators", "bones", "normals", "bust" }.All(s => (string)d5[s] == (string)d1[s]), "after undo every body section equals the first (normals and bust included)", Ctx.Short(d5.ToString()));
        c.Check(W1UndoCount(c) == n0, "digests record nothing (edits undone)");
    }

    // ---------------------------------------------------------------- chara.edit.info throttle / bustNormals

    static void C32Info(Ctx c, string f, string m)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var i = c.Ok(new { cmd = "chara.edit.info", key = f }, "chara.edit.info BR-Chan");
        var th = i["throttle"] as JObject;
        c.Check(th != null && (string)th["mode"] == "auto" && th["enabled"]?.Type == JTokenType.Boolean && (bool)th["enabled"] == CharaEditFlush.ThrottleBodyDuringDrags
                && th["weight"]?.Type == JTokenType.Integer && th["judged"]?.Type == JTokenType.Integer && (int)th["throttled"] == 0 && th["total"]?.Type == JTokenType.Integer,
            "throttle {mode auto (default), enabled, weight, judged, throttled 0 (edit mode), total}", Ctx.Short(th?.ToString()));
        var mode0 = CharaEditFlush.BodyThrottle;
        try
        {
            foreach (var (mode, name) in new[] { (BodyThrottleMode.Always, "always"), (BodyThrottleMode.Off, "off"), (BodyThrottleMode.Auto, "auto") })
            {
                CharaEditFlush.BodyThrottle = mode;
                var x = c.Call(new { cmd = "chara.edit.info", key = f });
                c.Check((string)x["throttle"]?["mode"] == name, "CharaEditFlush.BodyThrottle " + mode + " → mode " + name, Ctx.Short(x["throttle"]?.ToString()));
            }
        }
        finally { CharaEditFlush.BodyThrottle = mode0; }
        var bn = i["bustNormals"] as JObject;
        c.Check(bn != null && C32F(bn["rate"]) == 1f && C32F(bn["blendRate"]) == 1f && (bool?)bn["applied"] == true && (bool?)bn["meshCopied"] == true,
            "bustNormals {rate 1, blendRate 1, applied, meshCopied} (size 0.58)", Ctx.Short(bn?.ToString()));
        var im = c.Ok(new { cmd = "chara.edit.info", key = m }, "chara.edit.info HERO107");
        c.Check(im["bustNormals"]?["rate"]?.Type == JTokenType.Null && im["bustNormals"]?["blendRate"]?.Type == JTokenType.Null && (bool?)im["bustNormals"]?["applied"] == false && (bool?)im["bustNormals"]?["meshCopied"] == false,
            "male: bustNormals {rate null, blendRate null, not applied, no copy}", Ctx.Short(im["bustNormals"]?.ToString()));
        // lastFlush.throttleWeight = Σ over the body creators the flush rebuilt of 1 + their skin layers
        int Weight(JObject info)
        {
            var rebuilt = C30Strs(info["lastFlush"]?["creatorsRebuilt"]).Select(x => (string)x).ToList();
            int w = 0;
            foreach (var cr in (info["creators"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
            {
                string name = (string)cr["name"];
                if (name != null && name.Contains("body") && rebuilt.Contains(name)) w += 1 + ((int?)cr["layers"] ?? 0);
            }
            return w;
        }
        foreach (var (path, value, what, minW) in new (string, object, string, int)[]
        {
            ("chara.body.skin.color", new[] { 0.7, 0.6, 0.55, 1.0 }, "skin colour (body colour creator)", 1),
            ("chara.body.paint[1].color", new[] { 0.2, 0.4, 0.9, 1.0 }, "paint 2 colour (body colour creator)", 1),
            ("chara.body.paint[1].gloss", 0.8, "paint 2 gloss (body detail creator)", 1),
            ("chara.body.paint[1].layout", new[] { 0.3, 0.6, 0.4, 0.7 }, "paint 2 layout (both body creators)", 2),
            ("chara.body.nail.color", new[] { 0.9, 0.3, 0.3, 1.0 }, "nail colour (no composite)", 0),
        })
        {
            C32Set(c, f, path, value, what);
            var x = c.Call(new { cmd = "chara.edit.info", key = f });
            int got = (int?)x["lastFlush"]?["throttleWeight"] ?? -1, want = Weight(x);
            c.Check(got == want && got >= minW && (minW > 0 || got == 0), what + ": lastFlush.throttleWeight " + got + " = Σ(1 + layers) of the rebuilt body creators (" + want + ")",
                Ctx.Short(x["lastFlush"]?.ToString()));
        }
        C32Set(c, f, "chara.body.shape[1]", 0.3f, "bust size 0.3");
        var s3 = c.Call(new { cmd = "chara.edit.info", key = f });
        float r3 = HS2Next.Chara.Edit.Units.BustNormalsUnit.RateOf(0.3f);
        c.Check(C32F(s3["bustNormals"]?["rate"]) == r3 && C32F(s3["bustNormals"]?["blendRate"]) == r3 && (bool?)s3["bustNormals"]?["applied"] == true && (int?)s3["lastFlush"]?["throttleWeight"] == 0,
            "bust size 0.3: bustNormals rate = blendRate = " + r3 + ", applied; a shape edit has weight 0", Ctx.Short(s3["bustNormals"]?.ToString()));
        c.Check(W1UndoCount(c) == n0 + 6, "info records nothing (the 6 sets one entry each)", "" + W1UndoCount(c));
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (info block)");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
    }

    // ---------------------------------------------------------------- chara.edit.rtstats part body

    static void C32RtStats(Ctx c, string f)
    {
        if (!HS2Next.Chara.Edit.FaceDigest.HasDevice)
        {
            c.Fails(new { cmd = "chara.edit.rtstats", key = f, part = "body" }, "unavailable", "part body without a graphics device → unavailable");
            return;
        }
        var r = c.Ok(new { cmd = "chara.edit.rtstats", key = f, part = "body", grid = 2 }, "chara.edit.rtstats part body grid 2");
        var cr = (r["creators"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        c.Check(cr.Count == 2 && (string)cr[0]["name"] == "colour" && (string)cr[1]["name"] == "detail" && (string)cr[0]["creator"] == "create_skin_body" && (string)cr[1]["creator"] == "create_skin detail_body"
                && cr.All(x => (x["cells"] as JArray)?.Count == 16 && ((string)x["sha256"])?.Length == 64), "body creators colour / detail (create_skin_body / create_skin detail_body), cells 2·2·4, sha256", Ctx.Short(r.ToString()));
    }

    // ---------------------------------------------------------------- batch

    static void C32Batch(Ctx c, string f)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        c.Ok(new
        {
            cmd = "batch",
            cmds = new object[]
            {
                new { cmd = "chara.edit.swatch", key = f, path = "chara.body.skin.color", @ref = "illusion.hs2#2" },
                new { cmd = "chara.edit.bust", key = f },
                new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.body.skin.gloss"] = 0.3 } },
            },
        }, "a batch of chara.edit.swatch, bust, set");
        c.Check(W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Batch", "one entry \"Batch\"", W1UndoCount(c) + " " + W1TopLabel(c));
        c.Ok(new { cmd = "undo" }, "undo the batch");
        c.Check(W1DocSha(c) == sha0, "one undo restores scene.doc");
    }
}
