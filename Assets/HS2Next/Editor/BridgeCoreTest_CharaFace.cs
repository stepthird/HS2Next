// C31 of BridgeCoreTest: the Phase 4.1 bridge of package E,
// further methods of the partial BridgeCoreTest (-only C31), edit mode, headless: registration (CoreCommands.Phase41Names);
// chara.edit.layout (the mole and face-paint pushes write (Scale, Scale, PosX, PosY) of the row by id, rotation and
// layoutItem kept, one entry "Edit face", the creators a push rebuilds, errors); chara.edit.reset (schema default without a
// reference, the reference after the edit session opened, component rows, linked eyes, `defaults`, `reference` per node,
// edit.resetUnavailable, one entry "Reset value", errors); chara.edit.list columns / current / preset; chara.edit.preset
// (the vanilla preset, the missing-asset fallback edit.presetMissing as a head change with the skin snap); chara.edit.info
// (deferred units of a preview flush inside a batch with CharaEditFlush.ForcePreview, lastFlush.mode, per-creator
// rebuilds of a colour and a gloss edit, creators lastRebuildMs / size, reference); chara.edit.materials (the face set,
// the reply shape of the HS2 side, a lerp mapping through the shader property, filters, errors); chara.edit.digest
// (deterministic, sections follow edits and undo, texts hash to the sections, errors); chara.edit.rtstats (argument
// errors; `unavailable` without a graphics device — the read-back is checked in MakerFaceTest.render and H's FC1 / FC4);
// read-only commands record nothing; a batch of the new edits is one entry. Numbers only; the SFW targets only.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HS2Next.Content;
using HS2Next.Content.Maker;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;

public static partial class BridgeCoreTest
{
    static void C31(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        var names = HS2Next.App.Commands.CoreCommands.Phase41Names;
        c.Check(names.SequenceEqual(HS2Next.App.Commands.CharaEditCommands.Phase41Names) && names.Count == 5 && names.All(n => reg.Find(n) != null)
                && names.SequenceEqual(new[] { "chara.edit.layout", "chara.edit.reset", "chara.edit.materials", "chara.edit.rtstats", "chara.edit.digest" }),
            "Phase41Names = chara.edit.layout, .reset, .materials, .rtstats, .digest, all registered", string.Join(", ", names.Where(n => reg.Find(n) == null)));
        c.Check(HS2Next.App.Commands.CoreCommands.AllNames.Count(n => names.Contains(n)) == 5, "CoreCommands.AllNames lists them once");
        foreach (var n in names)
        {
            var h = c.Ok(new { cmd = "help", name = n }, "help " + n);
            c.Check(h.ToString().Contains(n), "help describes " + n, Ctx.Short(c.LastText));
        }
        // MakerServices.References is static and bound to one scene session; an earlier check's host may have left references
        // of nodes whose ids this scene reuses (test ids are deterministic): bind it to this host's session (clears them), as
        // the single-session app is
        HS2Next.Scenes.MakerServices.References.Attach(app.Session);
        c.Ok(new { cmd = "scene.new" }, "scene.new");
        string f = C30Load(c, c.FemaleRel, "female", "chara.load BR-Chan");
        string m = C30Load(c, c.MaleRel, "male", "chara.load HERO107");
        if (f == null || m == null) return;
        string item = W1AddItem(c);
        c.Frames(2);
        C31Layout(c, f, m);
        C31Reset(c, f, m);
        C31List(c, f, m);
        C31Preset(c, f);
        C31Info(c, f);
        C31Materials(c, f, m, item);
        C31Digest(c, f, item);
        C31RtStats(c, f);
        C31Batch(c, f);
    }

    static float[] C31Floats(JToken t) => (t as JArray)?.Select(x => (float)x).ToArray() ?? new float[0];

    static Dictionary<string, int> C31Rebuilds(Ctx c, string key) =>
        ((c.Call(new { cmd = "chara.edit.info", key })["creators"] as JArray) ?? new JArray()).OfType<JObject>()
            .ToDictionary(x => (string)x["name"], x => (int)x["rebuilds"]);

    /// <summary>Which face creators rebuilt between two creator counts: "colour", "detail", "both" or "none".</summary>
    static string C31Rebuilt(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        bool col = b.TryGetValue("create_skin_face", out var x) && a.TryGetValue("create_skin_face", out var y) && x != y;
        bool det = b.TryGetValue("create_skin detail_face", out var u) && a.TryGetValue("create_skin detail_face", out var v) && u != v;
        return col && det ? "both" : col ? "colour" : det ? "detail" : "none";
    }

    static bool C31Warned(JObject r, string what) => (r["warnings"] as JArray)?.Any(w => ((string)w).Contains(what)) == true;

    // ---------------------------------------------------------------- chara.edit.layout

    static void C31Layout(Ctx c, string f, string m)
    {
        var lists = MakerCatalog.For(c.Services.Catalog);
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        // mole row 4 «左の目元»: (0.33, 0.33, 0.75, 0.52) on this install; the values come from the list by id
        c.Check(lists.LayoutValues(6, ContentId.Vanilla(4), out float s6, out float x6, out float y6), "list 6 row 4 exists");
        var before = C31Rebuilds(c, f);
        var r = c.Ok(new { cmd = "chara.edit.layout", key = f, path = "chara.face.mole.layout", @ref = "illusion.hs2#4" }, "chara.edit.layout mole row 4");
        var v = C31Floats(C30Value(c, f, "chara.face.mole.layout"));
        c.Check(v.Length == 4 && v[0] == s6 && v[1] == s6 && v[2] == x6 && v[3] == y6, "face.mole.layout = (Scale, Scale, PosX, PosY) of row 4 = (" + s6 + ", " + s6 + ", " + x6 + ", " + y6 + ")", string.Join(", ", v));
        c.Check(Math.Abs(s6 - 0.33f) < 1e-6 && Math.Abs(x6 - 0.75f) < 1e-6 && Math.Abs(y6 - 0.52f) < 1e-6, "row 4 = the census values (0.33, 0.75, 0.52)", s6 + " " + x6 + " " + y6);
        c.Check(C30Has(r["changed"], "chara.face.mole.layout") && W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Edit face", "changed chara.face.mole.layout, one entry \"Edit face\"", Ctx.Short(r.ToString()) + " " + W1TopLabel(c));
        string mr = C31Rebuilt(before, C31Rebuilds(c, f));
        c.Check(mr == "colour", "a mole layout push rebuilds the colour creator only (HF3, F6)", mr);
        var again = c.Ok(new { cmd = "chara.edit.layout", key = f, path = "chara.face.mole.layout", @ref = "illusion.hs2#4" }, "the same push again");
        c.Check(C30Has(again["noop"], "chara.face.mole.layout") && W1UndoCount(c) == n0 + 1, "noop, no entry", Ctx.Short(again.ToString()));
        // paint 2 row 5 «顎» on both characters: one entry; rotation and layoutItem untouched (F18)
        c.Check(lists.LayoutValues(7, ContentId.Vanilla(5), out float s7, out float x7, out float y7), "list 7 row 5 exists");
        var rot = C30Value(c, f, "chara.face.makeup.paint[1].rotation")?.ToString();
        var li = C30Value(c, f, "chara.face.makeup.paint[1].layoutItem")?.ToString();
        before = C31Rebuilds(c, f);
        var p = c.Ok(new { cmd = "chara.edit.layout", keys = new[] { f, m }, path = "chara.face.makeup.paint[1].layout", @ref = "illusion.hs2#5" }, "chara.edit.layout keys [f, m] paint[1] row 5");
        var vf = C31Floats(C30Value(c, f, "chara.face.makeup.paint[1].layout"));
        var vm = C31Floats(C30Value(c, m, "appearance.chara.face.makeup.paint[1].layout"));
        c.Check(vf.SequenceEqual(new[] { s7, s7, x7, y7 }) && vm.SequenceEqual(new[] { s7, s7, x7, y7 }), "both characters: paint[1].layout = (Scale, Scale, PosX, PosY) of row 5", string.Join(", ", vf) + " | " + string.Join(", ", vm));
        c.Check(C30Value(c, f, "chara.face.makeup.paint[1].rotation")?.ToString() == rot && C30Value(c, f, "chara.face.makeup.paint[1].layoutItem")?.ToString() == li,
            "rotation and layoutItem untouched (F18)");
        c.Check(W1UndoCount(c) == n0 + 2 && W1TopLabel(c) == "Edit face", "several keys: one entry", W1UndoCount(c) + " " + W1TopLabel(c) + " " + Ctx.Short(p.ToString()));
        string pr = C31Rebuilt(before, C31Rebuilds(c, f));
        c.Check(pr == "both", "a paint layout push rebuilds both creators (HF3)", pr);
        c.Fails(new { cmd = "chara.edit.layout", key = f, path = "chara.face.eyebrow.layout", @ref = "illusion.hs2#0" }, "bad_args", "a path that is not a layout push target → bad_args");
        c.Fails(new { cmd = "chara.edit.layout", key = f, path = "chara.face.mole.layout", @ref = "illusion.hs2#99" }, "not_found", "an unknown row → not_found");
        c.Fails(new { cmd = "chara.edit.layout", key = f, path = "chara.face.mole.layout", @ref = "x" }, "bad_args", "a ref that does not parse → bad_args");
        c.Fails(new { cmd = "chara.edit.layout", key = f, path = "chara.face.mole.layout" }, "bad_args", "no ref → bad_args");
        c.Fails(new { cmd = "chara.edit.layout", key = f, @ref = "illusion.hs2#4" }, "bad_args", "no path → bad_args");
        c.Fails(new { cmd = "chara.edit.layout", key = "nosuchnode", path = "chara.face.mole.layout", @ref = "illusion.hs2#4" }, "not_found", "an unknown key → not_found");
        c.Check(W1UndoCount(c) == n0 + 2, "failed pushes record nothing");
        c.Ok(new { cmd = "undo" }, "undo the paint push");
        c.Ok(new { cmd = "undo" }, "undo the mole push");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
    }

    // ---------------------------------------------------------------- chara.edit.reset

    static void C31Reset(Ctx c, string f, string m)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        const string tilt = "chara.face.eyebrow.tilt";
        var info = c.Ok(new { cmd = "chara.edit.info", key = f }, "chara.edit.info before any session");
        c.Check(info["reference"]?.Type == JTokenType.Boolean && !(bool)info["reference"], "no reference before the edit session", Ctx.Short(info.ToString()));
        // without a reference: the Edit tab row's default (0.5)
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { [tilt] = 0.9 } }, "eyebrow tilt = 0.9");
        var r = c.Ok(new { cmd = "chara.edit.reset", key = f, paths = new[] { tilt } }, "chara.edit.reset tilt (no reference)");
        c.Check((float)C30Value(c, f, tilt) == 0.5f && r["reference"]?[f]?.Type == JTokenType.Boolean && !(bool)r["reference"][f] && C30Has(r["changed"], tilt),
            "no reference: the row default 0.5; reference {f: false}; changed names the path", Ctx.Short(r.ToString()));
        c.Check(W1UndoCount(c) == n0 + 2 && W1TopLabel(c) == "Reset value", "one entry \"Reset value\"", W1UndoCount(c) + " " + W1TopLabel(c));
        // the edit session captures the reference (the documents as they are now)
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { [tilt] = 0.7, ["chara.face.eyebrow.layout"] = new[] { 0.4, 0.3, 0.6, 0.6 } } }, "tilt 0.7, eyebrow layout (0.4, 0.3, 0.6, 0.6)");
        var on = c.Ok(new { cmd = "chara.edit.info", key = f, session = true }, "chara.edit.info session true (captures the reference)");
        c.Check((bool)on["reference"], "reference true after the session opened", Ctx.Short(on.ToString()));
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { [tilt] = 1.5, ["chara.face.eyebrow.layout"] = new[] { 0.1, 0.2, 0.9, 0.4 } } }, "tilt 1.5, eyebrow layout (0.1, 0.2, 0.9, 0.4)");
        int n1 = W1UndoCount(c);
        r = c.Ok(new { cmd = "chara.edit.reset", key = f, paths = new[] { tilt, "chara.face.eyebrow.layout#2" } }, "chara.edit.reset tilt + eyebrow layout component 2");
        var lay = C31Floats(C30Value(c, f, "chara.face.eyebrow.layout"));
        c.Check((float)C30Value(c, f, tilt) == 0.7f && lay.SequenceEqual(new[] { 0.1f, 0.2f, 0.6f, 0.4f }) && (bool)r["reference"][f],
            "reference values: tilt 0.7, only component 2 of the layout back to 0.6 (others kept); reference {f: true}", string.Join(", ", lay) + " " + Ctx.Short(r.ToString()));
        c.Check(W1UndoCount(c) == n1 + 1 && W1TopLabel(c) == "Reset value", "two paths: one entry", W1UndoCount(c) + " " + W1TopLabel(c));
        // linked eyes: a reset of the left eye resets both (the planner's consequence)
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.face.eyes.linked"] = true } }, "eyes linked");
        var refEm = (float)C30Value(c, f, "chara.face.eyes.left.iris.emission");
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.face.eyes.left.iris.emission"] = 1.5 } }, "left iris emission 1.5 (linked: both)");
        c.Check((float)C30Value(c, f, "chara.face.eyes.right.iris.emission") == 1.5f, "linked: the right eye followed");
        var le = c.Ok(new { cmd = "chara.edit.reset", key = f, paths = new[] { "chara.face.eyes.left.iris.emission" } }, "chara.edit.reset left iris emission");
        float lEm = (float)C30Value(c, f, "chara.face.eyes.left.iris.emission"), rEm = (float)C30Value(c, f, "chara.face.eyes.right.iris.emission");
        c.Check(lEm == refEm && rEm == refEm, "linked eyes: the left eye resets to its reference " + refEm + ", the right eye follows (HS2's linked copy)", lEm + " / " + rEm + " " + Ctx.Short(le.ToString()));
        // several keys: per node (m has no reference → the default)
        c.Ok(new { cmd = "chara.edit.set", keys = new[] { f, m }, set = new Dictionary<string, object> { [tilt] = 0.1 } }, "tilt 0.1 on both");
        r = c.Ok(new { cmd = "chara.edit.reset", keys = new[] { f, m }, paths = new[] { tilt } }, "chara.edit.reset keys [f, m] tilt");
        c.Check((float)C30Value(c, f, tilt) == 0.7f && (float)C30Value(c, m, tilt) == 0.5f && (bool)r["reference"][f] && !(bool)r["reference"][m],
            "per node: f → its reference 0.7, m → the default 0.5; reference {f: true, m: false}", Ctx.Short(r.ToString()));
        // `defaults` for a member without a reference; null = none → edit.resetUnavailable, nothing recorded
        c.Ok(new { cmd = "chara.edit.set", key = m, set = new Dictionary<string, object> { ["chara.body.shape[3]"] = 0.9 } }, "m body.shape[3] = 0.9");
        c.Ok(new { cmd = "chara.edit.reset", key = m, paths = new[] { "chara.body.shape[3]" }, defaults = new object[] { 0.25 } }, "chara.edit.reset m body.shape[3] defaults [0.25]");
        c.Check((float)C30Value(c, m, "chara.body.shape[3]") == 0.25f, "defaults: 0.25 stored");
        int n2 = W1UndoCount(c);
        var un = c.Ok(new { cmd = "chara.edit.reset", key = m, paths = new[] { "chara.body.shape[3]" }, defaults = new object[] { null } }, "chara.edit.reset with defaults [null]");
        c.Check(C31Warned(un, "edit.resetUnavailable") && W1UndoCount(c) == n2 && (float)C30Value(c, m, "chara.body.shape[3]") == 0.25f,
            "neither reference nor default: skipped with edit.resetUnavailable, no entry", Ctx.Short(un.ToString()));
        c.Fails(new { cmd = "chara.edit.reset", key = f }, "bad_args", "no paths → bad_args");
        c.Fails(new { cmd = "chara.edit.reset", key = f, paths = new string[0] }, "bad_args", "paths [] → bad_args");
        c.Fails(new { cmd = "chara.edit.reset", key = f, paths = new[] { "chara.face.nosuch" } }, "bad_args", "an unknown member → bad_args");
        c.Fails(new { cmd = "chara.edit.reset", key = f, paths = new[] { tilt + "#1" } }, "bad_args", "a component of a float member → bad_args");
        c.Fails(new { cmd = "chara.edit.reset", key = f, paths = new[] { "chara.face.eyebrow.layout#x" } }, "bad_args", "a component that is not a number → bad_args");
        c.Fails(new { cmd = "chara.edit.reset", key = f, paths = new[] { tilt }, defaults = new object[] { 0.5, 0.5 } }, "bad_args", "defaults of another length → bad_args");
        c.Fails(new { cmd = "chara.edit.reset", key = f, paths = new[] { tilt }, defaults = new object[] { new { a = 1 } } }, "bad_args", "a default of an unusable form → bad_args");
        c.Ok(new { cmd = "chara.edit.info", key = f, session = false }, "chara.edit.info session false");
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (reset block)");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
    }

    // ---------------------------------------------------------------- chara.edit.list (columns, current, preset)

    static void C31List(Ctx c, string f, string m)
    {
        var lists = MakerCatalog.For(c.Services.Catalog);
        int n0 = W1UndoCount(c);
        var l6 = c.Ok(new { cmd = "chara.edit.list", category = 6, sex = "female", columns = new[] { "Scale", "PosX", "PosY", "NoSuchColumn" } }, "chara.edit.list 6 columns Scale / PosX / PosY");
        var rows6 = (l6["rows"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        bool ok6 = rows6.Count == 8;
        foreach (var row in rows6)
        {
            ContentId.TryParse((string)row["ref"], out var id);
            ok6 &= lists.LayoutValues(6, id, out float s, out float x, out float y) && row["values"] is JObject vals
                   && float.Parse((string)vals["Scale"], System.Globalization.CultureInfo.InvariantCulture) == s
                   && float.Parse((string)vals["PosX"], System.Globalization.CultureInfo.InvariantCulture) == x
                   && float.Parse((string)vals["PosY"], System.Globalization.CultureInfo.InvariantCulture) == y
                   && vals["NoSuchColumn"]?.Type == JTokenType.Null;
        }
        c.Check(ok6, "8 rows of list 6, values {column: text} = LayoutValues; a missing column → null", Ctx.Short(rows6.FirstOrDefault()?.ToString()));
        var sk = c.Ok(new { cmd = "chara.edit.list", key = f, path = "chara.face.skin", current = true, limit = 10000 }, "chara.edit.list key + chara.face.skin current");
        string skin = (string)C30Value(c, f, "chara.face.skin");
        var cur = (sk["rows"] as JArray)?.OfType<JObject>().Where(x => (bool?)x["current"] == true).ToList() ?? new List<JObject>();
        int ci = (int?)sk["currentIndex"] ?? -2;
        c.Check((string)sk["currentRef"] == skin && (ci >= 0 ? cur.Count == 1 && (string)cur[0]["ref"] == skin && (string)sk["rows"][ci]["ref"] == skin : cur.Count == 0),
            "current: currentRef = the stored skin, its row (index " + ci + ") marked, no other", Ctx.Short(sk.ToString()));
        var hd = c.Ok(new { cmd = "chara.edit.list", key = f, path = "chara.face.head", current = true, limit = 10000 }, "chara.edit.list key + chara.face.head current");
        var hrows = (hd["rows"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        int presets = hrows.Count(x => (bool?)x["preset"] == true);
        c.Check((int)hd["category"] == 210 && hrows.All(x => x["preset"]?.Type == JTokenType.Boolean) && presets == lists.PresetHeads(1).Count && (string)hd["currentRef"] == (string)C30Value(c, f, "chara.face.head"),
            "female heads: preset bool on every row, " + presets + " with a preset = MakerLists.PresetHeads(female); currentRef = the head", hrows.Count + " rows");
        var mh = c.Ok(new { cmd = "chara.edit.list", category = 110, sex = "male", limit = 10000 }, "chara.edit.list 110 male");
        var mrows = (mh["rows"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        c.Check(mrows.Count(x => (bool?)x["preset"] == true) == lists.PresetHeads(0).Count, "male heads: preset count = MakerLists.PresetHeads(male) (" + lists.PresetHeads(0).Count + " of " + mrows.Count + ")");
        c.Info("heads: female " + hrows.Count + " rows, " + presets + " with a preset; male " + mrows.Count + " rows, " + lists.PresetHeads(0).Count + " with a preset");
        var top = c.Ok(new { cmd = "chara.edit.list", category = 240, sex = "female", limit = 3 }, "chara.edit.list 240 (no extras asked)");
        c.Check(top["rows"]?[0]?["preset"] == null && top["rows"]?[0]?["values"] == null && top["rows"]?[0]?["current"] == null && top["currentRef"] == null, "no preset / values / current unless asked or a head list");
        c.Fails(new { cmd = "chara.edit.list", category = 211, sex = "female", current = true }, "bad_args", "current without key + path → bad_args");
        c.Fails(new { cmd = "chara.edit.list", category = 6, sex = "female", columns = "Scale" }, "bad_args", "columns not an array → bad_args");
        c.Fails(new { cmd = "chara.edit.list", key = f, path = "chara.face.skin", current = "yes" }, "bad_args", "current not a bool → bad_args");
        c.Check(W1UndoCount(c) == n0, "lists record nothing");
    }

    // ---------------------------------------------------------------- chara.edit.preset (fallback)

    static void C31Preset(Ctx c, string f)
    {
        var lists = MakerCatalog.For(c.Services.Catalog);
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var v = c.Ok(new { cmd = "chara.edit.preset", key = f, head = "illusion.hs2#3" }, "chara.edit.preset head illusion.hs2#3");
        c.Check((string)C30Value(c, f, "chara.face.head") == "illusion.hs2#3" && !C31Warned(v, "edit.presetMissing") && W1TopLabel(c) == "Face preset",
            "vanilla preset: head 3, no edit.presetMissing, entry \"Face preset\"", Ctx.Short(v.ToString()));
        c.Ok(new { cmd = "undo" }, "undo the preset");
        // the heads of 4.0's H4 / HF4 that name a preset their MainAB lacks
        var heads = lists.Rows(210, 1);
        string missing = null;
        foreach (var want in new[] { "cunihinx.ada4#424", "Darkruler.ElenaFisher#166" })
        {
            var row = heads.FirstOrDefault(x => string.Equals(x.Key.ToString(), want, StringComparison.OrdinalIgnoreCase));
            if (row != null) { missing = row.Key.ToString(); break; }
        }
        if (missing == null) { c.Warn("no installed head without a preset asset (cunihinx.ada4#424, Darkruler.ElenaFisher#166): fallback not checked"); }
        else
        {
            string skin0 = (string)C30Value(c, f, "chara.face.skin");
            var r = c.Ok(new { cmd = "chara.edit.preset", key = f, head = missing }, "chara.edit.preset " + missing + " (no preset asset)");
            ContentId.TryParse(missing, out var hid);
            var skins = lists.Rows(211, 1, ListContext.ForHead(hid)).Select(x => x.Key.ToString()).ToList();
            string skin = (string)C30Value(c, f, "chara.face.skin");
            c.Check(C31Warned(r, "edit.presetMissing") && (string)C30Value(c, f, "chara.face.head") == missing && W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Face preset",
                "missing asset: the head change with edit.presetMissing, one entry \"Face preset\" (F5)", Ctx.Short(r.ToString()));
            c.Check(skins.Count == 0 ? skin == skin0 : skins.Contains(skin), "the skin is listed for the new head (snapped if needed)", skin0 + " -> " + skin);
            var info = c.Ok(new { cmd = "chara.edit.info", key = f }, "chara.edit.info after the fallback");
            c.Check((string)info["parts"]?["head"] == missing && C30Has(info["lastFlush"]?["units"], "head"), "the head unit swapped the head", Ctx.Short(info["lastFlush"]?.ToString()));
            c.Ok(new { cmd = "undo" }, "undo the fallback");
        }
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
    }

    // ---------------------------------------------------------------- chara.edit.info (deferred, creators, mode)

    static void C31Info(Ctx c, string f)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var i = c.Ok(new { cmd = "chara.edit.info", key = f }, "chara.edit.info");
        var creators = (i["creators"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        c.Check(creators.Any(x => (string)x["name"] == "create_skin_face") && creators.Any(x => (string)x["name"] == "create_skin detail_face")
                && creators.All(x => x["lastRebuildMs"] != null && x["size"] is JArray sz && sz.Count == 2 && (int)sz[0] == (int)x["width"] && (int)sz[1] == (int)x["height"]),
            "creators: the face pair named create_skin_face / create_skin detail_face (\"face\" / \"detail\"), lastRebuildMs, size [w, h]", Ctx.Short(i["creators"]?.ToString()));
        c.Check(i["deferred"] is JArray d0 && d0.Count == 0 && (string)i["lastFlush"]?["mode"] != null && i["lastFlush"]?["deferred"] is JArray && i["lastFlush"]?["creatorsRebuilt"] is JArray
                && i["lastFlush"]?["faceDrawChanged"]?.Type == JTokenType.Boolean,
            "deferred [], lastFlush {mode, deferred, creatorsRebuilt, faceDrawChanged}", Ctx.Short(i.ToString()));
        // per-creator rebuilds (GF2): a makeup colour → the colour creator only, a makeup gloss → the detail creator only
        var b0 = C31Rebuilds(c, f);
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.face.makeup.lip.color"] = new[] { 0.8, 0.2, 0.3, 0.9 } } }, "lip colour");
        var b1 = C31Rebuilds(c, f);
        var li = c.Call(new { cmd = "chara.edit.info", key = f });
        c.Check(C31Rebuilt(b0, b1) == "colour" && C30Has(li["lastFlush"]?["creatorsRebuilt"], "create_skin_face") && !C30Has(li["lastFlush"]?["creatorsRebuilt"], "create_skin detail_face"),
            "a lip colour rebuilds the colour creator only; lastFlush.creatorsRebuilt names it", C31Rebuilt(b0, b1) + " " + Ctx.Short(li["lastFlush"]?.ToString()));
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.face.makeup.lip.gloss"] = 0.77 } }, "lip gloss");
        var b2 = C31Rebuilds(c, f);
        c.Check(C31Rebuilt(b1, b2) == "detail", "a lip gloss rebuilds the detail creator only", C31Rebuilt(b1, b2));
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.face.eyebrow.tilt"] = 0.6 } }, "eyebrow tilt (draw.face)");
        var dr = c.Call(new { cmd = "chara.edit.info", key = f });
        c.Check((bool?)dr["lastFlush"]?["faceDrawChanged"] == true && C31Rebuilt(b2, C31Rebuilds(c, f)) == "none", "a draw.face edit: faceDrawChanged, no creator rebuilt", Ctx.Short(dr["lastFlush"]?.ToString()));
        // the preview seam (F7): inside a batch (an open transaction) with ForcePreview the flush defers its deferrable work
        bool fp = CharaEditFlush.ForcePreview;
        JObject bt;
        try
        {
            CharaEditFlush.ForcePreview = true;
            bt = c.Ok(new
            {
                cmd = "batch",
                cmds = new object[]
                {
                    new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.face.shape[3]"] = 0.9 } },
                    new { cmd = "chara.edit.info", key = f },
                },
            }, "batch [face.shape[3] = 0.9, chara.edit.info] with ForcePreview");
        }
        finally { CharaEditFlush.ForcePreview = fp; }
        var inner = bt["results"]?[1] as JObject;
        var deferred = C30Strs(inner?["deferred"]).Select(x => (string)x).ToList();
        c.Check(deferred.Count > 0 && (string)inner?["lastFlush"]?["mode"] == "preview" && C30Strs(inner?["lastFlush"]?["deferred"]).Count == deferred.Count,
            "inside the transaction: a preview flush, deferred " + string.Join(",", deferred), Ctx.Short(inner?.ToString()));
        var after = c.Ok(new { cmd = "chara.edit.info", key = f }, "chara.edit.info after the batch");
        var units = C30Strs(after["lastFlush"]?["units"]).Select(x => (string)x).ToList();
        c.Check(C30Strs(after["deferred"]).Count == 0 && (string)after["lastFlush"]?["mode"] == "normal" && deferred.All(units.Contains),
            "after the commit: deferred [], a normal flush ran the deferred units", string.Join(",", units));
        c.Check(W1UndoCount(c) == n0 + 4, "the edits recorded one entry each (batch one), info nothing", "" + W1UndoCount(c));
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo (info block)");
        c.Check(W1DocSha(c) == sha0, "undo restores scene.doc");
    }

    // ---------------------------------------------------------------- chara.edit.materials

    static void C31Materials(Ctx c, string f, string m, string item)
    {
        int n0 = W1UndoCount(c);
        var all = c.Ok(new { cmd = "chara.edit.materials", key = f }, "chara.edit.materials (the face set)");
        var rs = (all["renderers"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        var set = new[] { "o_head", "o_eyebase_L", "o_eyebase_R", "o_eyeshadow", "o_eyelashes" };
        c.Check(set.All(n => rs.Any(x => (string)x["name"] == n)) && rs.All(x => set.Contains((string)x["name"])),
            "the default renderers: o_head, o_eyebase_L / R, o_eyeshadow, o_eyelashes", string.Join(",", rs.Select(x => (string)x["name"])));
        bool shape = rs.All(x => x["enabled"]?.Type == JTokenType.Boolean && x["active"]?.Type == JTokenType.Boolean && x["parent"] != null
                                  && x["slots"] is JArray sl && sl.Count > 0 && sl.OfType<JObject>().All(s => s["material"] != null && s["shader"]?.Type == JTokenType.String
                                      && s["queue"]?.Type == JTokenType.Integer && s["keywords"] is JArray && s["props"] is JObject po && po.Count > 0));
        c.Check(shape, "renderer {name, enabled, active, parent, slots [{material, shader, queue, keywords, props}]} (the HS2 side's shape)", Ctx.Short(rs.FirstOrDefault()?.ToString()));
        var head = rs.FirstOrDefault(x => (string)x["name"] == "o_head")?["slots"]?[0]?["props"] as JObject;
        var eye = rs.FirstOrDefault(x => (string)x["name"] == "o_eyebase_L")?["slots"]?[0]?["props"] as JObject;
        c.Check(head?["_Texture3UV"] is JArray t3 && t3.Count == 4 && t3.All(x => x.Type == JTokenType.Float || x.Type == JTokenType.Integer) && head?["_Texture3Rotator"] != null,
            "o_head props carry HS2's names (_Texture3UV as [4] numbers, _Texture3Rotator)", Ctx.Short(head?.ToString()));
        c.Check(eye?["_texture2uv"] is JArray t2 && t2.Count == 4, "o_eyebase_L props carry _texture2uv", Ctx.Short(eye?.ToString()));
        var tex = rs.SelectMany(x => x["slots"].OfType<JObject>()).SelectMany(s => ((JObject)s["props"]).Properties()).Select(p => p.Value).OfType<JObject>().FirstOrDefault();
        c.Check(tex != null && tex["name"] != null && tex["w"]?.Type == JTokenType.Integer && tex["h"]?.Type == JTokenType.Integer && tex["format"]?.Type == JTokenType.String,
            "textures as {name, w, h, format}", Ctx.Short(tex?.ToString()));
        // a lerp mapping read through the shader property (_Texture3Rotator = lerp(−0.15, 0.15, tilt), clamped)
        foreach (var (t, want) in new[] { (0.75, 0.075), (1.5, 0.15), (-0.5, -0.15) })
        {
            c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.face.eyebrow.tilt"] = t } }, "eyebrow tilt " + t);
            var one = c.Ok(new { cmd = "chara.edit.materials", key = f, renderers = new[] { "o_head" }, props = new[] { "_Texture3Rotator", "_Gloss", "nosuchprop" } }, "chara.edit.materials o_head, props filter");
            var ors = (one["renderers"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
            var pp = ors.FirstOrDefault()?["slots"]?[0]?["props"] as JObject;
            double got = pp?["_Texture3Rotator"] != null ? (double)pp["_Texture3Rotator"] : double.NaN;
            c.Check(ors.Count >= 1 && ors.All(x => (string)x["name"] == "o_head") && pp != null && pp.Properties().All(p => p.Name == "_Texture3Rotator" || p.Name == "_Gloss")
                    && Math.Abs(got - want) < 1e-6,
                "tilt " + t + " → _Texture3Rotator " + want + " (only o_head, only the filtered props)", got + " " + Ctx.Short(pp?.ToString()));
        }
        var none = c.Ok(new { cmd = "chara.edit.materials", key = f, renderers = new[] { "nosuchrenderer" } }, "chara.edit.materials unknown renderer");
        c.Check((none["renderers"] as JArray)?.Count == 0, "an unknown renderer name → an empty list");
        var mb = c.Ok(new { cmd = "chara.edit.materials", key = m, renderers = new[] { "o_head" }, props = new[] { "_Texture5", "_Color5" } }, "chara.edit.materials HERO107 o_head beard props");
        c.Info("HERO107 o_head beard: " + Ctx.Short(mb["renderers"]?[0]?["slots"]?[0]?["props"]?.ToString()));
        c.Fails(new { cmd = "chara.edit.materials", key = item }, "wrong_kind", "an item → wrong_kind");
        c.Fails(new { cmd = "chara.edit.materials", key = f, renderers = "o_head" }, "bad_args", "renderers not an array → bad_args");
        c.Fails(new { cmd = "chara.edit.materials", key = f, props = new object[] { 3 } }, "bad_args", "props not strings → bad_args");
        c.Check(W1UndoCount(c) == n0 + 3, "materials record nothing (the three tilt sets one entry each)", "" + W1UndoCount(c));
        for (int k = 0; k < 3; k++) c.Ok(new { cmd = "undo" }, "undo tilt " + (3 - k));
    }

    // ---------------------------------------------------------------- chara.edit.digest

    static void C31Digest(Ctx c, string f, string item)
    {
        int n0 = W1UndoCount(c);
        var d1 = c.Ok(new { cmd = "chara.edit.digest", key = f }, "chara.edit.digest face");
        bool hex(JToken t) => t?.Type == JTokenType.String && ((string)t).Length == 64 && ((string)t).All(ch => "0123456789abcdef".IndexOf(ch) >= 0);
        c.Check((string)d1["parts"] == "face" && hex(d1["materials"]) && hex(d1["creators"]) && hex(d1["bones"]), "{parts, materials, creators, bones}: SHA-256 hex", Ctx.Short(d1.ToString()));
        c.Check(HS2Next.Chara.Edit.FaceDigest.HasDevice ? hex(d1["rts"]) : d1["rts"] == null, "rts only with a graphics device (" + (HS2Next.Chara.Edit.FaceDigest.HasDevice ? "device" : "headless") + ")");
        var d2 = c.Call(new { cmd = "chara.edit.digest", key = f });
        bool same(JObject a, JObject b) => (string)a["materials"] == (string)b["materials"] && (string)a["creators"] == (string)b["creators"] && (string)a["bones"] == (string)b["bones"];
        c.Check(same(d1, d2), "deterministic: a second digest is equal");
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.face.eyebrow.tilt"] = 0.8 } }, "eyebrow tilt 0.8");
        var d3 = c.Call(new { cmd = "chara.edit.digest", key = f });
        c.Check((string)d3["materials"] != (string)d1["materials"] && (string)d3["bones"] == (string)d1["bones"], "a material edit changes the materials section only (bones equal)");
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.face.makeup.cheek.color"] = new[] { 0.9, 0.1, 0.1, 0.5 } } }, "cheek colour");
        var d4 = c.Call(new { cmd = "chara.edit.digest", key = f });
        c.Check((string)d4["creators"] != (string)d3["creators"], "a composite edit changes the creators section");
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.face.shape[3]"] = 0.95 } }, "face.shape[3] 0.95");
        var d5 = c.Call(new { cmd = "chara.edit.digest", key = f });
        c.Check((string)d5["bones"] != (string)d4["bones"], "a face shape edit changes the bones section");
        for (int k = 0; k < 3; k++) c.Ok(new { cmd = "undo" }, "undo digest edit " + (3 - k));
        var d6 = c.Call(new { cmd = "chara.edit.digest", key = f });
        c.Check(same(d1, d6), "after undo the digest equals the first (the realization follows the documents)", Ctx.Short(d6.ToString()));
        var tx = c.Ok(new { cmd = "chara.edit.digest", key = f, text = true }, "chara.edit.digest text");
        string Sha(string s) { using (var h = SHA256.Create()) return string.Concat(h.ComputeHash(Encoding.UTF8.GetBytes(s ?? "")).Select(b => b.ToString("x2"))); }
        c.Check(tx["texts"] is JObject tt && ((string)tt["materials"]).Length > 0 && Sha((string)tt["materials"]) == (string)tx["materials"] && Sha((string)tt["creators"]) == (string)tx["creators"]
                && Sha((string)tt["bones"]) == (string)tx["bones"], "texts hash to the sections");
        var body = c.Ok(new { cmd = "chara.edit.digest", key = f, parts = "body" }, "chara.edit.digest body");
        var all = c.Ok(new { cmd = "chara.edit.digest", key = f, parts = "all" }, "chara.edit.digest all");
        c.Check(hex(body["materials"]) && hex(all["bones"]) && (string)all["materials"] != (string)d1["materials"] && (string)body["materials"] != (string)d1["materials"], "body / all digests differ from face");
        // 4.3: "hair" is a part — its own sections, chains among them, none in the face digest
        var hair = c.Ok(new { cmd = "chara.edit.digest", key = f, parts = "hair" }, "chara.edit.digest hair (4.3)");
        c.Check((string)hair["parts"] == "hair" && hex(hair["materials"]) && hex(hair["bones"]) && hex(hair["chains"]) && (string)hair["materials"] != (string)d1["materials"]
                && (string)hair["bones"] != (string)d1["bones"] && d1["chains"] == null, "parts hair: its own materials / bones and a chains section; the face digest has no chains (details in C33)", Ctx.Short(hair.ToString()));
        c.Fails(new { cmd = "chara.edit.digest", key = f, parts = "nose" }, "bad_args", "parts nose → bad_args");
        c.Fails(new { cmd = "chara.edit.digest", key = item }, "wrong_kind", "an item → wrong_kind");
        c.Check(W1UndoCount(c) == n0, "digests record nothing (edits undone)");
    }

    // ---------------------------------------------------------------- chara.edit.rtstats

    static void C31RtStats(Ctx c, string f)
    {
        c.Fails(new { cmd = "chara.edit.rtstats", key = f, part = "hair" }, "bad_args", "part hair → bad_args");
        c.Fails(new { cmd = "chara.edit.rtstats", key = f, grid = 0 }, "bad_args", "grid 0 → bad_args");
        c.Fails(new { cmd = "chara.edit.rtstats", key = f, grid = 65 }, "bad_args", "grid 65 → bad_args");
        if (!HS2Next.Chara.Edit.FaceDigest.HasDevice)
        {
            c.Fails(new { cmd = "chara.edit.rtstats", key = f }, "unavailable", "no graphics device → unavailable");
            return;
        }
        var r = c.Ok(new { cmd = "chara.edit.rtstats", key = f, part = "face", grid = 4 }, "chara.edit.rtstats face grid 4");
        var cr = (r["creators"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        c.Check(cr.Count == 2 && (string)cr[0]["name"] == "colour" && (string)cr[1]["name"] == "detail" && cr.All(x => (x["cells"] as JArray)?.Count == 64 && (x["mean"] as JArray)?.Count == 4
                && ((string)x["sha256"])?.Length == 64 && x["props"] is JObject), "creators colour / detail {mean[4], std[4], cells[4·4·4], sha256, props}", Ctx.Short(r.ToString()));
    }

    // ---------------------------------------------------------------- batch

    static void C31Batch(Ctx c, string f)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        c.Ok(new
        {
            cmd = "batch",
            cmds = new object[]
            {
                new { cmd = "chara.edit.layout", key = f, path = "chara.face.mole.layout", @ref = "illusion.hs2#7" },
                new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["chara.face.eyebrow.tilt"] = 0.3 } },
                new { cmd = "chara.edit.reset", key = f, paths = new[] { "chara.face.eyebrow.tilt" } },
            },
        }, "a batch of chara.edit.layout, set, reset");
        c.Check(W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Batch", "one entry \"Batch\"", W1UndoCount(c) + " " + W1TopLabel(c));
        c.Ok(new { cmd = "undo" }, "undo the batch");
        c.Check(W1DocSha(c) == sha0, "one undo restores scene.doc");
    }
}
