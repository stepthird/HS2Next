// MosaicTest, a card's bone modifiers (Phase 6.3a package M): more modifiers show on the bones under the patches now, and the patches
// must stay on the skin. Official behaviour keeps the mosaic in place: nothing here adds a rule or a clamp.
//   FM20  CPU-skinned patch vertices against the body vertices on the cards of the local list (groups ah5 and mosaic, Cache/compare_mods/cards.json, not in git), on row 19/0/0
//         and Chair_A at 0.5, speed 0: the distances within the same card's with its modifiers off + the bars of AM3 (median + 0.005, max + 0.02 units)
//   FM21  chara.mosaic's reply and every member's material on those cards: equal with the modifiers on and off
// Without the local list the groups warn. Cards by hash; numbers only; no image is opened.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next.Chara.Mosaic;
using HS2Next.Content;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static MakerTestKit;

public static partial class MosaicTest
{
    static void ModifierGroups()
    {
        var faceSeed = HS2Next.Motion.Face.FaceSetup.Seed;
        try
        {
            if (Want("FM20") || Want("FM21")) Guard("FM20, FM21 the patches on modifier cards", Fm20);
        }
        finally { HS2Next.Motion.Face.FaceSetup.Seed = faceSeed; }
    }

    static List<(string hash, string rel, string sex)> FmCards()
    {
        var res = new List<(string, string, string)>();
        string path = Path.Combine(ContentSettings.ProjectRoot, "Cache", "compare_mods", "cards.json");
        if (!File.Exists(path)) { Warn("FM20", "no local list of modifier cards (Cache/compare_mods/cards.json): not run"); return res; }
        foreach (var c in JObject.Parse(File.ReadAllText(path))["cards"].OfType<JObject>())
            if (c["groups"] is JArray g && g.Any(x => (string)x == "ah5" || (string)x == "mosaic")) res.Add(((string)c["hash"], (string)c["rel"], (string)c["sex"]));
        if (res.Count == 0) Warn("FM20", "the local list has no group ah5 or mosaic: not run");
        return res;
    }

    // the character of a card at a row, its modifiers on or off, stepped by hand
    static string FmLoad(AmHost h, string rel, string sex, bool on, int group, int category, int no)
    {
        h.Ok(new { cmd = "scene.new" }, "scene.new");
        h.Ok(new { cmd = "app.options", ext = new { abmx = on, accessoryControls = false }, fk = new { hipsOffset = false }, correctionAllOn = true }, "app.options");
        var r = h.Ok(new { cmd = "chara.load", path = rel, sex }, "chara.load");
        string key = (string)r["key"] ?? (string)r["added"]?[0]?["key"];
        if (key == null) return null;
        h.Ok(new { cmd = "chara.anime", key, group, category, no, time = 0.5f, speed = 0f }, "the row");
        h.Ok(new { cmd = "chara.motion", key, autoStep = false, physics = false, look = false, reset = true }, "by hand");
        h.Rt.ApplyNow();
        for (int i = 0; i < 3; i++) h.Rt.FindChara(key).Motion.Step(1f / 60f);
        return key;
    }

    static string FmMosaicReply(AmHost h, string key)
    {
        var o = h.Call(new { cmd = "chara.mosaic", key });
        o.Remove("key");
        return o.ToString();
    }

    static string FmMaterials(HS2Next.Chara.CharaContext ctx)
    {
        var lines = new List<string>();
        var st = MosaicSet.StateOf(ctx);
        if (st != null)
            foreach (var x in st.Members)
                if (x.Renderer) foreach (var m in x.Renderer.sharedMaterials) lines.Add(x.Path + ": " + (m ? (m.shader ? m.shader.name : "no shader") + " rq " + m.renderQueue + " kw " + string.Join(",", m.shaderKeywords.OrderBy(k => k, StringComparer.Ordinal)) : "none"));
        lines.Sort(StringComparer.Ordinal);
        return string.Join("\n", lines);
    }

    static void Fm20()
    {
        var cards = FmCards();
        if (cards.Count == 0) return;
        var rows = new[] { ("row 19/0/0", 19, 0, 0), ("Chair_A", 19, 1, 11) };
        double worstM = 0, worstX = 0, worstMoved = 0;
        string whereM = "", whereX = "";
        using (var h = NewAmHost())
        {
            var saved = h.Call(new { cmd = "app.options" });
            foreach (var (hash, rel, sex) in cards)
                foreach (var (name, g, c, n) in rows)
                {
                    string off = FmLoad(h, rel, sex, false, g, c, n);
                    if (!Check($"FM20 {hash} {name}: builds with the modifiers off", off != null)) continue;
                    var ctx0 = h.Rt.FindChara(off).Context;
                    var patches0 = MosaicSet.SurfaceMembers(ctx0);
                    if (patches0.Count == 0) { Info($"FM20 {hash}: no body-surface member"); continue; }
                    var body0 = AmSkinnedVertices(ctx0.BodyRenderer);
                    if (body0 == null) { Warn("FM20 " + hash, "the body mesh is not readable"); continue; }
                    var rest = new Dictionary<string, (double median, double max)>();
                    var restV = new Dictionary<string, Vector3[]>();
                    foreach (var p in patches0)
                    {
                        var v = AmSkinnedVertices(p);
                        if (v == null) continue;
                        restV[p.name] = v;
                        rest[p.name] = AmDistances(v, body0);
                    }
                    string reply0 = FmMosaicReply(h, off), mats0 = FmMaterials(ctx0);

                    string on = FmLoad(h, rel, sex, true, g, c, n);
                    if (!Check($"FM20 {hash} {name}: builds with the modifiers on", on != null)) continue;
                    var ctx1 = h.Rt.FindChara(on).Context;
                    var body1 = AmSkinnedVertices(ctx1.BodyRenderer);
                    foreach (var p in MosaicSet.SurfaceMembers(ctx1))
                    {
                        if (!rest.TryGetValue(p.name, out var r0)) continue;
                        var v = AmSkinnedVertices(p);
                        var d = AmDistances(v, body1);
                        double dm = d.median - r0.median, dx = d.max - r0.max;
                        double moved = v.Length == restV[p.name].Length ? Enumerable.Range(0, v.Length).Max(i => (double)(v[i] - restV[p.name][i]).magnitude) : -1;
                        if (dm > worstM) { worstM = dm; whereM = hash + " " + name + " " + p.name; }
                        if (dx > worstX) { worstX = dx; whereX = hash + " " + name + " " + p.name; }
                        worstMoved = Math.Max(worstMoved, moved);
                        Info($"FM20 {hash} {name} {p.name}: modifiers off median {r0.median:F4}, max {r0.max:F4}; on median {d.median:F4} ({dm:+0.0000;-0.0000}), max {d.max:F4} ({dx:+0.0000;-0.0000}); the patch moved up to {moved:F4} units");
                        Check($"FM20 {hash} {name} {p.name}: within the bars (median + 0.005, max + 0.02)", dm <= 0.005 && dx <= 0.02, $"median {dm:+0.0000;-0.0000}, max {dx:+0.0000;-0.0000}");
                    }
                    Check($"FM21 {hash} {name}: chara.mosaic equal with the modifiers on and off", FmMosaicReply(h, on) == reply0);
                    Check($"FM21 {hash} {name}: every member's material equal", FmMaterials(ctx1) == mats0, mats0.Length + " characters of text");
                }
            h.Ok(new { cmd = "scene.new" }, "scene.new");
            h.Ok(new JObject { ["cmd"] = "app.options", ["ext"] = saved["ext"], ["fk"] = saved["fk"], ["correctionAllOn"] = saved["correctionAllOn"] }.ToString(), "app.options back");
        }
        Info($"FM20 RESULT: {cards.Count} cards, worst median change {worstM:+0.0000;-0.0000} ({whereM}), worst max change {worstX:+0.0000;-0.0000} ({whereX}), the patch moved up to {worstMoved:F4} units");
    }
}
