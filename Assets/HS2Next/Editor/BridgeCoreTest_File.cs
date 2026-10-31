// C36 of BridgeCoreTest: the bridge of the File node (package E of Phase 4.6), further methods of the partial BridgeCoreTest
// (-only C36), edit mode, headless: registration (CoreCommands.Phase46Names = chara.edit.load + coord.save, their help and
// groups, the extended chara.save / chara.edit.get / app.options documented; help {group: "chara edit"} lists exactly the 26);
// chara.save thumb view and clock (framing, the manifest times, byte-identical saves, not recorded, bad values);
// coord.save (the sex folder, name / path, coordName in the file only, overwrite, thumb modes, clock, the coordinate and its
// import as written, the node untouched, a reload through chara.edit.coordinate, errors); chara.edit.load (each flag and all
// five from a .hs2nchara changed in every section: one entry "Load character", undo / redo, the section's member taken and
// the others kept, the reported paths, the import provenance kept by a partial load and taken by all five, an HS2 card by a
// relative and an absolute path, the other sex refused with reason load.otherSex unless all five, bad flags, undo:false);
// app.options maker (VT12: set, read back, the effect of each switch, all-or-nothing validation). Numbers and documents only;
// the SFW targets of Tools/targets.json; files under the test hosts' UserData (Spike/out/bridge/userdata).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class BridgeCoreTest
{
    /// <summary>The fixed clock of the C36 saves.</summary>
    const string C36Clock = "2026-10-26T00:00:00Z";
    static readonly DateTime C36ClockUtc = new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>The members C36's source changes, one per load flag (relative to `appearance.`), with the new values.</summary>
    static readonly (string flag, string path, object value)[] C36Edits =
    {
        ("face", "chara.face.shape[3]", 0.83),
        ("body", "chara.body.shape[1]", 0.77),
        ("hair", "chara.hair.back.baseColor", new[] { 0.2, 0.4, 0.6, 1.0 }),
        ("coordinate", "coord.clothes.top.colors[0].base", new[] { 0.15, 0.25, 0.35, 1.0 }),
        ("profile", "chara.profile.name", "C36 Source"),
    };

    /// <summary>The prefixes of the member paths a load flag may report (relative to `appearance.`).</summary>
    static readonly Dictionary<string, string[]> C36Prefixes = new Dictionary<string, string[]>
    {
        ["face"] = new[] { "chara.face.", "chara.body.skinOverlays", "chara.body.boneMods", "chara.materialOverrides", "chara.studioOverlays" },
        ["body"] = new[] { "chara.body.", "chara.materialOverrides", "chara.studioOverlays" },
        ["hair"] = new[] { "chara.hair." },
        ["coordinate"] = new[] { "coord.", "chara.studioOverlays", "chara.state.accessories" },
        ["profile"] = new[] { "chara.profile." },
    };

    static void C36(Ctx c)
    {
        var app = c.Host();
        C36Registration(c, app);
        c.Ok(new { cmd = "scene.new" }, "scene.new");
        string f = C30Load(c, c.FemaleRel, "female", "chara.load BR-Chan");
        string m = C30Load(c, c.MaleRel, "male", "chara.load HERO107");
        if (f == null || m == null) return;
        string item = W1AddItem(c);
        c.Frames(2);
        C36CharaSave(c, f);
        C36CoordSave(c, f, m);
        C36Load(c, f, m, item);
        C36Options(c, f);
    }

    // ---------------------------------------------------------------- helpers

    static bool C36HasDevice => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

    /// <summary>Both documents of a character as canonical JSON texts (chara.edit.get without paths).</summary>
    static (string chara, string coord) C36Docs(Ctx c, string key)
    {
        var v = c.Call(new { cmd = "chara.edit.get", key })["values"];
        return (C34J(v?["chara"]), C34J(v?["coord"]));
    }

    static string C36Import(Ctx c, string key) => C36File(c.Call(new { cmd = "chara.edit.get", key, paths = new[] { "import" } })["values"]?["import"]);

    /// <summary>The fileName of an import provenance object (null for null or another token).</summary>
    static string C36File(JToken t) => (string)(t as JObject)?["fileName"];

    static bool C36Warned(JObject r, string word) => (r?["warnings"] as JArray)?.Any(w => ((string)w).Contains(word)) == true;

    /// <summary>The canonical text of a document as save-format JSON (the bytes chara.edit.get answers).</summary>
    static string C36Canon(Action<CanonicalJsonWriter> write, string entry) => JToken.Parse(System.Text.Encoding.UTF8.GetString(CanonicalJsonWriter.ToBytes(entry, write))).ToString(Newtonsoft.Json.Formatting.None);

    // ---------------------------------------------------------------- registration and help

    static void C36Registration(Ctx c, HS2Next.App.AppCore app)
    {
        var reg = app.BridgeServices.Commands;
        var names = HS2Next.App.Commands.CoreCommands.Phase46Names;
        c.Check(names.SequenceEqual(HS2Next.App.Commands.CharaEditCommands.Phase46Names.Concat(HS2Next.App.Commands.CharaCommands.Phase46Names))
                && names.SequenceEqual(new[] { "chara.edit.load", "coord.save" }) && names.All(n => reg.Find(n) != null),
            "Phase46Names = chara.edit.load, coord.save, all registered", string.Join(", ", names.Where(n => reg.Find(n) == null)));
        c.Check(HS2Next.App.Commands.CoreCommands.AllNames.Count(n => names.Contains(n)) == 2, "CoreCommands.AllNames lists them once");
        c.Check(reg.Find("chara.edit.load")?.Group == "chara edit" && reg.Find("coord.save")?.Group == "chara",
            "groups: chara.edit.load in \"chara edit\", coord.save in \"chara\"", reg.Find("chara.edit.load")?.Group + " / " + reg.Find("coord.save")?.Group);
        foreach (var (cmd, words) in new[]
        {
            ("chara.edit.load", new[] { "flags", "face", "body", "hair", "coordinate", "profile", "load.otherSex", "import", "Load character" }),
            ("coord.save", new[] { "coordName", "overwrite", "clock", "placeholder", "female|male" }),
            ("chara.save", new[] { "view", "clock", "framing" }),
            ("chara.edit.get", new[] { "import provenance" }),
            ("app.options", new[] { "maker", "keepColours", "copyAccessoryPluginData", "bodyThrottle" }),
        })
        {
            var h = c.Ok(new { cmd = "help", name = cmd }, "help " + cmd);
            var missing = words.Where(w => !h.ToString().Contains(w)).ToList();
            c.Check(missing.Count == 0, "help " + cmd + " documents " + string.Join(", ", words), string.Join(", ", missing));
        }
        var edit = HS2Next.App.Commands.CharaEditCommands.Names.Concat(HS2Next.App.Commands.CharaEditCommands.Phase41Names).Concat(HS2Next.App.Commands.CharaEditCommands.Phase42Names)
            .Concat(HS2Next.App.Commands.CharaEditCommands.Phase43Names).Concat(HS2Next.App.Commands.CharaEditCommands.Phase44Names)
            .Concat(HS2Next.App.Commands.CharaEditCommands.Phase45Names).Concat(HS2Next.App.Commands.CharaEditCommands.Phase46Names).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var he = c.Ok(new { cmd = "help", group = "chara edit" }, "help {group: \"chara edit\"}");
        var hen = (he["commands"] as JArray)?.Select(x => (string)x["name"]).OrderBy(x => x, StringComparer.Ordinal).ToList() ?? new List<string>();
        c.Check(hen.SequenceEqual(edit) && edit.Count == 26, "help {group: \"chara edit\"} lists exactly the 26 chara edit commands (4.0–4.6)", hen.Count + ": " + string.Join(", ", hen.Except(edit).Concat(edit.Except(hen))));
        var hc = c.Ok(new { cmd = "help", group = "chara" }, "help {group: \"chara\"}");
        var hcn = (hc["commands"] as JArray)?.Select(x => (string)x["name"]).ToList() ?? new List<string>();
        c.Check(hcn.Contains("coord.save") && hcn.Contains("chara.save") && hcn.Contains("chara.load"), "help {group: \"chara\"} lists coord.save beside chara.load / chara.save", string.Join(", ", hcn));
        c.Check(HS2Next.App.Commands.CoreCommands.RegistrationErrors.Count == 0, "no registration error", string.Join("; ", HS2Next.App.Commands.CoreCommands.RegistrationErrors));
    }

    // ---------------------------------------------------------------- chara.save thumb view / clock

    static void C36CharaSave(Ctx c, string f)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var a = c.Ok(new { cmd = "chara.save", key = f, path = "e46/c36_front", overwrite = true, clock = C36Clock }, "chara.save {path, clock} (thumb render = front)");
        var b = c.Ok(new { cmd = "chara.save", key = f, path = "e46/c36_view", overwrite = true, clock = C36Clock, thumb = "view" }, "chara.save thumb view");
        var p = c.Ok(new { cmd = "chara.save", key = f, path = "e46/c36_placeholder", overwrite = true, clock = C36Clock, thumb = "placeholder" }, "chara.save thumb placeholder");
        if (C36HasDevice)
        {
            c.Check((string)a["thumbnail"] == "render" && (string)a["framing"] == "front", "render: thumbnail render, framing front", Ctx.Short(c.LastText));
            // edit mode has no display camera: view falls back to front with warning thumbnail.noView
            c.Check((string)b["thumbnail"] == "render" && ((string)b["framing"] == "view" || ((string)b["framing"] == "front" && C36Warned(b, "thumbnail.noView"))),
                "view: rendered, framing view (or front with warning thumbnail.noView without a display camera)", Ctx.Short(b.ToString(Newtonsoft.Json.Formatting.None)));
        }
        else
        {
            c.Check((string)a["thumbnail"] == "placeholder" && a["framing"]?.Type == JTokenType.Null && C36Warned(a, "graphics device"),
                "render without a graphics device: the placeholder, framing null, a warning", Ctx.Short(a.ToString(Newtonsoft.Json.Formatting.None)));
            c.Check((string)b["thumbnail"] == "placeholder" && b["framing"]?.Type == JTokenType.Null, "view without a graphics device: the placeholder, framing null", Ctx.Short(b.ToString(Newtonsoft.Json.Formatting.None)));
        }
        c.Check((string)p["thumbnail"] == "placeholder" && p["framing"]?.Type == JTokenType.Null && !C36Warned(p, "thumbnail"), "placeholder: framing null, no thumbnail warning", Ctx.Short(p.ToString(Newtonsoft.Json.Formatting.None)));
        c.Check(W1UndoCount(c) == n0 && W1DocSha(c) == sha0, "chara.save is not recorded and changes no document");
        string pa = (string)a["path"], pp = (string)p["path"];
        var la = File.Exists(pa) ? CharaContainer.Load(pa) : null;
        var m = la?.Value?.LoadedManifest;
        c.Check(la != null && la.Ok && m != null && m.CreatedUtc == C36ClockUtc && m.ModifiedUtc == C36ClockUtc,
            "the file's manifest createdUtc / modifiedUtc = the clock " + C36Clock, m == null ? "no manifest" : m.CreatedUtc.ToString("o") + " / " + m.ModifiedUtc.ToString("o"));
        c.Check(m != null && m.Prerelease == null && m.AppVersion == AppSaveOptions.Version, "manifest prerelease null, app.version = the build's (" + AppSaveOptions.Version + ")", m == null ? "" : (m.Prerelease ?? "null") + " / " + m.AppVersion);
        var docs = C36Docs(c, f);
        c.Check(la?.Value != null && C36Canon(la.Value.Chara.Write, CharaDocument.EntryName) == docs.chara && C36Canon(la.Value.Coord.Write, CoordDocument.EntryName) == docs.coord,
            "the saved documents = the node's (canonical)");
        var p2 = c.Ok(new { cmd = "chara.save", key = f, path = "e46/c36_placeholder2", overwrite = true, clock = C36Clock, thumb = "placeholder" }, "chara.save placeholder again with the same clock");
        c.Check(File.Exists(pp) && File.ReadAllBytes(pp).SequenceEqual(File.ReadAllBytes((string)p2["path"])), "two saves with the same clock are byte-identical");
        c.Fails(new { cmd = "chara.save", key = f, path = "e46/c36_x", overwrite = true, thumb = "front" }, "bad_args", "chara.save thumb \"front\" → bad_args (render / view / placeholder)");
        c.Fails(new { cmd = "chara.save", key = f, path = "e46/c36_x", overwrite = true, clock = "yesterday" }, "bad_args", "chara.save clock \"yesterday\" → bad_args");
        c.Fails(new { cmd = "chara.save", key = f, path = "e46/c36_x", overwrite = true, clock = 5 }, "bad_args", "chara.save clock 5 → bad_args");
        c.Check(!File.Exists(Path.Combine(c.UserDataDir, "chara", "e46", "c36_x.hs2nchara")), "a refused save writes no file");
    }

    // ---------------------------------------------------------------- coord.save

    static void C36CoordSave(Ctx c, string f, string m)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var docs0 = C36Docs(c, f);
        string femaleDir = Path.Combine(c.UserDataDir, "coordinate", "female"), maleDir = Path.Combine(c.UserDataDir, "coordinate", "male");
        var r = c.Ok(new { cmd = "coord.save", key = f, name = "e46_c36_coord", coordName = "C36 Coord", overwrite = true, thumb = "placeholder", clock = C36Clock }, "coord.save BR-Chan {name, coordName, placeholder, clock}");
        string path = (string)r["path"];
        c.Check(path != null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(Path.Combine(femaleDir, "e46_c36_coord.hs2ncoord")), StringComparison.OrdinalIgnoreCase),
            "the file is <UserData>/coordinate/female/e46_c36_coord.hs2ncoord (absolute path in the reply)", path);
        c.Check((int?)r["sex"] == 1 && (string)r["coordName"] == "C36 Coord" && (string)r["thumbnail"] == "placeholder" && path != null && File.Exists(path) && (long?)r["bytes"] == new FileInfo(path).Length,
            "reply: sex 1, coordName, thumbnail placeholder, bytes = the file size", Ctx.Short(r.ToString(Newtonsoft.Json.Formatting.None)));
        c.Check(W1UndoCount(c) == n0 && W1DocSha(c) == sha0 && C36Docs(c, f).Equals(docs0), "coord.save is not recorded; the node's documents are untouched (the name lives in the file only)");
        var lr = path != null && File.Exists(path) ? CoordContainer.Load(path) : null;
        var cc = lr?.Value;
        c.Check(lr != null && lr.Ok && cc.Coord.Name == "C36 Coord" && cc.Import == null, "the file reloads: coord.name \"C36 Coord\", import null", lr == null ? "no file" : (lr.Ok ? cc.Coord.Name + " / " + (cc.Import == null ? "null" : cc.Import.FileName) : lr.Error.Message));
        if (cc != null)
        {
            var node = JObject.Parse(docs0.coord);
            node["name"] = "C36 Coord";
            c.Check(C36Canon(cc.Coord.Write, CoordDocument.EntryName) == node.ToString(Newtonsoft.Json.Formatting.None), "the file's coord.json = the node's with the typed name");
            var man = cc.LoadedManifest;
            c.Check(man != null && man.CreatedUtc == C36ClockUtc && man.Prerelease == null, "manifest createdUtc = the clock, prerelease null");
        }
        var d = c.Ok(new { cmd = "coord.save", key = f, name = "e46_c36_default", overwrite = true, thumb = "placeholder", clock = C36Clock }, "coord.save without coordName");
        string nodeName = (string)JObject.Parse(docs0.coord)["name"];
        c.Check((string)d["coordName"] == nodeName && File.Exists((string)d["path"]) && CoordContainer.Load((string)d["path"]).Value?.Coord.Name == nodeName,
            "without coordName the file keeps the character's coord.name (\"" + nodeName + "\")", (string)d["coordName"]);
        var d2 = c.Ok(new { cmd = "coord.save", key = f, name = "e46_c36_default2", overwrite = true, thumb = "placeholder", clock = C36Clock }, "coord.save again with the same clock");
        c.Check(File.ReadAllBytes((string)d["path"]).SequenceEqual(File.ReadAllBytes((string)d2["path"])), "two coordinate saves with the same clock are byte-identical");
        var rr = c.Call(new { cmd = "coord.save", key = f, name = "e46_c36_render", overwrite = true, clock = C36Clock });
        if (C36HasDevice) c.Check((bool?)rr["ok"] == true && (string)rr["thumbnail"] == "render", "thumb render (default) with a graphics device: rendered", Ctx.Short(c.LastText));
        else c.Check((bool?)rr["ok"] == true && (string)rr["thumbnail"] == "placeholder" && (rr["warnings"] as JArray)?.Count > 0, "thumb render without a graphics device: the placeholder with a warning", Ctx.Short(c.LastText));
        var sub = c.Ok(new { cmd = "coord.save", key = f, path = "e46/sub/c36", overwrite = true, thumb = "placeholder" }, "coord.save {path} with subfolders");
        c.Check(string.Equals(Path.GetFullPath((string)sub["path"] ?? ""), Path.GetFullPath(Path.Combine(femaleDir, "e46", "sub", "c36.hs2ncoord")), StringComparison.OrdinalIgnoreCase), "path: subfolders created under the sex folder", (string)sub["path"]);
        var mr = c.Ok(new { cmd = "coord.save", key = m, name = "e46_c36_male", overwrite = true, thumb = "placeholder" }, "coord.save HERO107");
        c.Check((int?)mr["sex"] == 0 && string.Equals(Path.GetDirectoryName(Path.GetFullPath((string)mr["path"] ?? "")), Path.GetFullPath(maleDir), StringComparison.OrdinalIgnoreCase),
            "a male character's coordinate goes to coordinate/male", (string)mr["path"]);
        c.Fails(new { cmd = "coord.save", key = f, name = "e46_c36_coord", thumb = "placeholder" }, "io_denied", "coord.save again without overwrite → io_denied");
        c.Fails(new { cmd = "coord.save", key = f, path = "../c36_up", thumb = "placeholder" }, "io_denied", "coord.save path \"../x\" → io_denied");
        c.Fails(new { cmd = "coord.save", key = f, path = Path.Combine(femaleDir, "c36_abs"), thumb = "placeholder" }, "io_denied", "coord.save with an absolute path → io_denied");
        c.Fails(new { cmd = "coord.save", key = f, thumb = "placeholder" }, "bad_args", "coord.save without name / path → bad_args");
        c.Fails(new { cmd = "coord.save", key = f, name = "a/b", thumb = "placeholder" }, "bad_args", "coord.save name with a folder → bad_args");
        c.Fails(new { cmd = "coord.save", key = f, name = "e46_c36_x", thumb = "view" }, "bad_args", "coord.save thumb \"view\" → bad_args (render / placeholder)");
        c.Fails(new { cmd = "coord.save", key = f, name = "e46_c36_x", coordName = 5 }, "bad_args", "coord.save coordName 5 → bad_args");
        c.Fails(new { cmd = "coord.save", key = f, name = "e46_c36_x", clock = "soon" }, "bad_args", "coord.save clock \"soon\" → bad_args");
        string item = W1AddItem(c, "item.add 399 (a non-character key)");
        c.Fails(new { cmd = "coord.save", key = item, name = "e46_c36_x" }, "wrong_kind", "coord.save of an item → wrong_kind");
        c.Ok(new { cmd = "undo" }, "undo the item add");
        c.Check(!File.Exists(Path.Combine(femaleDir, "e46_c36_x.hs2ncoord")), "a refused save writes no file");
        // the saved coordinate loads back (4.4's coordinate load «全て読込み»): the worn coordinate = the file's
        if (cc != null)
        {
            int n1 = W1UndoCount(c);
            var lo = c.Ok(new { cmd = "chara.edit.coordinate", key = f, file = path, mode = "all" }, "chara.edit.coordinate the saved .hs2ncoord (all)");
            c.Check(W1UndoCount(c) == n1 + 1 && C36Docs(c, f).coord == C36Canon(cc.Coord.Write, CoordDocument.EntryName), "one entry; the character wears the file's coordinate (its name included)");
            c.Ok(new { cmd = "undo" }, "undo the coordinate load");
            c.Check(C36Docs(c, f).Equals(docs0), "undo restores the documents");
        }
    }

    // ---------------------------------------------------------------- chara.edit.load

    static void C36Load(Ctx c, string f, string m, string item)
    {
        var orig = C36Docs(c, f);
        string sha0 = W1DocSha(c);
        string cardName = Path.GetFileName(c.FemaleCard);
        c.Check(C36Import(c, f) == cardName, "chara.edit.get paths [\"import\"]: the imported character's provenance names its card (" + cardName + ")", C36Import(c, f));
        // the source: BR-Chan changed in every section, saved, the edit undone
        var set = new Dictionary<string, object>();
        foreach (var e in C36Edits) set[e.path] = e.value;
        c.Ok(new { cmd = "chara.edit.set", key = f, set }, "chara.edit.set one member per section (the source)");
        var srcVals = C36Edits.ToDictionary(e => e.flag, e => C34J(C30Value(c, f, e.path)));
        var srcDocs = C36Docs(c, f);
        var s = c.Ok(new { cmd = "chara.save", key = f, path = "e46/c36_source", overwrite = true, thumb = "placeholder", clock = C36Clock }, "chara.save the source");
        c.Ok(new { cmd = "undo" }, "undo the source edit");
        c.Check(W1DocSha(c) == sha0 && C36Docs(c, f).Equals(orig), "the target is BR-Chan again");
        var origVals = C36Edits.ToDictionary(e => e.flag, e => C34J(C30Value(c, f, e.path)));
        c.Check(C36Edits.All(e => origVals[e.flag] != srcVals[e.flag]), "the source differs from the target in each section's member", string.Join("; ", C36Edits.Select(e => e.path + " " + origVals[e.flag] + " → " + srcVals[e.flag])));
        string srcPath = (string)s["path"];
        // a second source whose import provenance names another file (a partial load keeps the target's, all five take it)
        string src2 = Path.Combine(Path.GetDirectoryName(srcPath) ?? "", "c36_source_prov.hs2nchara");
        var l2 = CharaContainer.Load(srcPath);
        if (l2.Ok && l2.Value.Subtree.Import != null)
        {
            l2.Value.Subtree.Import.FileName = "C36 source.png";
            var sv = l2.Value.Save(src2, new SaveOptions { Thumbnail = ThumbnailMode.Placeholder, Clock = () => C36ClockUtc });
            c.Check(sv.Ok, "a copy of the source with import.fileName \"C36 source.png\" written", sv.Ok ? "" : sv.Error.Message);
        }
        else c.Check(false, "the source holds the card's import provenance", l2.Ok ? "import null" : l2.Error.Message);
        string rel2 = "e46/c36_source_prov.hs2nchara";

        foreach (var e in C36Edits)
        {
            var req = new { cmd = "chara.edit.load", key = f, path = rel2, flags = new[] { e.flag } };
            var r = W1History1(c, req, "Load character", "chara.edit.load flags [" + e.flag + "]",
                e.flag == "face" ? (Func<object>)(() => new { cmd = "chara.edit.load", key = f, path = rel2, flags = new[] { e.flag }, undo = false }) : null);
            if (r.Value<bool?>("ok") != true) continue;
            if (e.flag == "face")
            {
                // the undo:false call applied the load again without an entry; undo the recorded one below restores the target anyway
                c.Ok(new { cmd = "undo" }, "undo the recorded face load");
                c.Ok(new { cmd = "redo" }, "redo it");
            }
            var flags = C30Strs(r["flags"]).Select(x => (string)x).ToList();
            c.Check(flags.SequenceEqual(new[] { e.flag }), "reply flags [" + e.flag + "]", string.Join(", ", flags));
            var changed = C30Strs(r["changed"]).Select(x => (string)x).ToList();
            var outside = changed.Where(p => !C36Prefixes[e.flag].Any(pre => p.StartsWith(pre, StringComparison.Ordinal))).ToList();
            c.Check(changed.Count > 0 && outside.Count == 0 && changed.Any(p => e.path.StartsWith(p, StringComparison.Ordinal) || p.StartsWith(e.path, StringComparison.Ordinal)),
                e.flag + ": changed = second-level member paths of the section, the changed member among them", string.Join(", ", changed.Take(12)));
            c.Check((bool?)r["imported"] == false && (r["source"]?["sex"]?.Value<int>() ?? -1) == 1 && (string)r["source"]?["name"] == "C36 Source"
                    && string.Equals(Path.GetFullPath((string)r["source"]?["file"] ?? ""), Path.GetFullPath(src2), StringComparison.OrdinalIgnoreCase),
                e.flag + ": imported false (a character file), source {file, sex 1, name}", Ctx.Short(r["source"]?.ToString(Newtonsoft.Json.Formatting.None)));
            c.Check(C36File(r["import"]) == cardName && C36Import(c, f) == cardName, e.flag + ": a partial load keeps the target's import provenance (" + cardName + ")", C36File(r["import"]));
            var wrong = new List<string>();
            foreach (var o in C36Edits)
            {
                string now = C34J(C30Value(c, f, o.path));
                string want = o.flag == e.flag ? srcVals[o.flag] : origVals[o.flag];
                if (now != want) wrong.Add(o.path + " = " + now + " (want " + want + ")");
            }
            c.Check(wrong.Count == 0, e.flag + ": its member = the source's, the other sections' members = the target's", string.Join("; ", wrong));
            c.Ok(new { cmd = "undo" }, e.flag + ": undo");
            c.Check(C36Docs(c, f).Equals(orig) && W1DocSha(c) == sha0, e.flag + ": undo restores the target's documents and the scene");
        }

        // all five: the whole appearance and the provenance of the file
        {
            var r = W1History1(c, new { cmd = "chara.edit.load", key = f, path = rel2 }, "Load character", "chara.edit.load (flags default all)");
            if (r.Value<bool?>("ok") == true)
            {
                c.Check(C30Strs(r["flags"]).Select(x => (string)x).SequenceEqual(new[] { "face", "body", "hair", "coordinate", "profile" }) && C30Strs(r["changed"]).Select(x => (string)x).SequenceEqual(new[] { "appearance" }),
                    "all: flags = the five, changed [\"appearance\"]", Ctx.Short(c.LastText));
                c.Check(C36Docs(c, f).Equals(srcDocs), "all: both documents = the source's");
                c.Check(C36File(r["import"]) == "C36 source.png" && C36Import(c, f) == "C36 source.png", "all: the import provenance is the file's (import.fileName \"C36 source.png\")", C36File(r["import"]));
                c.Ok(new { cmd = "undo" }, "all: undo");
                c.Check(C36Docs(c, f).Equals(orig) && C36Import(c, f) == cardName && W1DocSha(c) == sha0, "all: undo restores documents and provenance");
            }
            var r2 = W1History1(c, new { cmd = "chara.edit.load", key = f, path = rel2, flags = new[] { "profile", "face", "hair", "coordinate", "body" } }, "Load character", "chara.edit.load flags = the five names in another order");
            if (r2.Value<bool?>("ok") == true)
            {
                c.Check(C30Strs(r2["changed"]).Select(x => (string)x).SequenceEqual(new[] { "appearance" }) && C36Docs(c, f).Equals(srcDocs), "a list of all five = all");
                c.Ok(new { cmd = "undo" }, "undo");
            }
        }

        // an HS2 card: relative (sex folder) and absolute
        {
            int n0 = W1UndoCount(c);
            var r = c.Ok(new { cmd = "chara.edit.load", key = f, path = c.FemaleRel, sex = "female", flags = new[] { "hair" } }, "chara.edit.load the BR-Chan card by its relative path, hair");
            int n1 = W1UndoCount(c);
            c.Check((bool?)r["imported"] == true && n1 <= n0 + 1 && C36Docs(c, f).Equals(orig), "a card source: imported true, at most one entry; BR-Chan's own hair = no change of the documents", n0 + " -> " + n1);
            if (n1 > n0) c.Ok(new { cmd = "undo" }, "undo");
            n0 = W1UndoCount(c);
            var ra = c.Ok(new { cmd = "chara.edit.load", key = f, path = c.FemaleCard, flags = "all" }, "chara.edit.load the card by its absolute path, flags \"all\"");
            c.Check((bool?)ra["imported"] == true && C36File(ra["import"]) == cardName && C36Docs(c, f).Equals(orig), "all five from the card: imported, the provenance names the card, the documents = the import's");
            if (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo");
            c.Check(C36Docs(c, f).Equals(orig) && W1DocSha(c) == sha0, "the target is BR-Chan again");
        }

        // the other sex: refused for a partial load (reason load.otherSex), accepted with all five
        {
            var mOrig = C36Docs(c, m);
            int n0 = W1UndoCount(c);
            var bad = c.Fails(new { cmd = "chara.edit.load", key = m, path = rel2, flags = new[] { "face" } }, "bad_args", "chara.edit.load a female file onto HERO107 with face only → bad_args");
            c.Check((string)bad["detail"]?["arg"] == "path" && (string)bad["detail"]?["reason"] == "load.otherSex" && W1UndoCount(c) == n0 && C36Docs(c, m).Equals(mOrig),
                "… arg path, detail.reason load.otherSex, nothing recorded or changed", Ctx.Short(c.LastText));
            var bad2 = c.Fails(new { cmd = "chara.edit.load", keys = new[] { f, m }, path = rel2, flags = new[] { "hair", "body" } }, "bad_args", "keys [BR-Chan, HERO107] with hair, body → bad_args (one target of the other sex)");
            c.Check((string)bad2["detail"]?["reason"] == "load.otherSex" && W1UndoCount(c) == n0 && C36Docs(c, f).Equals(orig), "… nothing changed on either", Ctx.Short(c.LastText));
            var ok = W1History1(c, new { cmd = "chara.edit.load", key = m, path = rel2, flags = "all" }, "Load character", "chara.edit.load the female file onto HERO107 with all five");
            if (ok.Value<bool?>("ok") == true)
            {
                c.Check(C36Docs(c, m).Equals(srcDocs), "all five change the sex: HERO107's documents = the female source's");
                c.Ok(new { cmd = "undo" }, "undo");
                c.Check(C36Docs(c, m).Equals(mOrig), "undo restores HERO107");
            }
        }

        // two targets in one entry
        {
            var mOrig = C36Docs(c, m);
            var r = W1History1(c, new { cmd = "chara.edit.load", keys = new[] { f, m }, path = rel2 }, "Load character", "chara.edit.load keys [BR-Chan, HERO107] all five (one entry)");
            if (r.Value<bool?>("ok") == true)
            {
                c.Check(C36Docs(c, f).Equals(srcDocs) && C36Docs(c, m).Equals(srcDocs), "both targets take the source");
                c.Ok(new { cmd = "undo" }, "undo");
                c.Check(C36Docs(c, f).Equals(orig) && C36Docs(c, m).Equals(mOrig), "one undo restores both");
            }
        }

        // errors
        int ne = W1UndoCount(c);
        var e1 = c.Fails(new { cmd = "chara.edit.load", key = f, path = rel2, flags = new string[0] }, "bad_args", "flags [] → bad_args");
        c.Check((string)e1["detail"]?["arg"] == "flags", "… naming flags");
        var e2 = c.Fails(new { cmd = "chara.edit.load", key = f, path = rel2, flags = new[] { "face", "nose" } }, "bad_args", "flags [face, nose] → bad_args");
        c.Check((string)e2["detail"]?["arg"] == "flags" && (e2["detail"]?["candidates"] as JArray)?.Select(x => (string)x).Contains("coordinate") == true, "… naming flags with the flag names as candidates", Ctx.Short(c.LastText));
        c.Fails(new { cmd = "chara.edit.load", key = f, path = rel2, flags = "nose" }, "bad_args", "flags \"nose\" → bad_args");
        c.Fails(new { cmd = "chara.edit.load", key = f, path = rel2, flags = 3 }, "bad_args", "flags 3 → bad_args");
        c.Fails(new { cmd = "chara.edit.load", key = f }, "bad_args", "no path → bad_args");
        c.Fails(new { cmd = "chara.edit.load", key = f, path = "e46/c36_missing.hs2nchara", flags = "all" }, "not_found", "a missing file → not_found");
        c.Fails(new { cmd = "chara.edit.load", key = f, path = "../c36_up.hs2nchara" }, "io_denied", "path \"../x\" → io_denied");
        c.Fails(new { cmd = "chara.edit.load", key = item, path = rel2 }, "wrong_kind", "an item key → wrong_kind");
        c.Fails(new { cmd = "chara.edit.load", path = rel2 }, "bad_args", "no key / keys → bad_args");
        c.Check(W1UndoCount(c) == ne && C36Docs(c, f).Equals(orig) && W1DocSha(c) == sha0, "refused loads record and change nothing");
    }

    // ---------------------------------------------------------------- app.options maker (VT12)

    static void C36Options(Ctx c, string f)
    {
        var r0 = c.Ok(new { cmd = "app.options" }, "app.options (read)");
        var mk = r0["maker"] as JObject;
        c.Check(mk != null && mk.Properties().Select(p => p.Name).SequenceEqual(new[] { "keepColours", "copyAccessoryPluginData", "bodyThrottle" })
                && (bool?)mk["keepColours"] == MakerServices.KeepColoursOnItemChange && (bool?)mk["copyAccessoryPluginData"] == MakerServices.CopyAccessoryPluginData
                && (string)mk["bodyThrottle"] == CharaEditFlush.BodyThrottleName(CharaEditFlush.BodyThrottle),
            "app.options maker = {keepColours, copyAccessoryPluginData, bodyThrottle} = the current switches", Ctx.Short(mk?.ToString(Newtonsoft.Json.Formatting.None)));
        // set and read back
        var r1 = c.Ok(new { cmd = "app.options", maker = new { keepColours = true, copyAccessoryPluginData = true, bodyThrottle = "off" } }, "app.options maker {keepColours: true, copyAccessoryPluginData: true, bodyThrottle: off}");
        c.Check((bool?)r1["maker"]?["keepColours"] == true && (bool?)r1["maker"]?["copyAccessoryPluginData"] == true && (string)r1["maker"]?["bodyThrottle"] == "off"
                && MakerServices.KeepColoursOnItemChange && MakerServices.CopyAccessoryPluginData && CharaEditFlush.BodyThrottle == BodyThrottleMode.Off,
            "the reply and the switches hold the new values");
        var rb = c.Ok(new { cmd = "app.options" }, "app.options read back");
        c.Check((bool?)rb["maker"]?["keepColours"] == true && (bool?)rb["maker"]?["copyAccessoryPluginData"] == true && (string)rb["maker"]?["bodyThrottle"] == "off", "read back: the same values");
        // effects: the options a Maker op takes, the throttle the editor reports, keep colours on an item change
        var o = MakerEditOptions.FromServices(c.App.Session);
        c.Check(o.KeepColours && o.CopyAccessoryPluginData, "a Maker op's options take keepColours and copyAccessoryPluginData");
        var info = c.Ok(new { cmd = "chara.edit.info", key = f }, "chara.edit.info");
        c.Check((string)info["throttle"]?["mode"] == "off", "chara.edit.info throttle.mode = off", Ctx.Short(info["throttle"]?.ToString(Newtonsoft.Json.Formatting.None)));
        c.Ok(new { cmd = "app.options", maker = new { bodyThrottle = "always" } }, "app.options maker {bodyThrottle: always} (one member)");
        c.Check((string)c.Call(new { cmd = "chara.edit.info", key = f })["throttle"]?["mode"] == "always" && MakerServices.KeepColoursOnItemChange, "throttle.mode = always; the other members kept");
        C36KeepColours(c, f, true);
        c.Ok(new { cmd = "app.options", maker = new { keepColours = false } }, "app.options maker {keepColours: false}");
        C36KeepColours(c, f, false);
        // validation: all or nothing
        c.Ok(new { cmd = "app.options", maker = new { keepColours = false, copyAccessoryPluginData = false, bodyThrottle = "auto" } }, "app.options maker back to the defaults");
        var b1 = c.Fails(new { cmd = "app.options", maker = new { keepColours = true, bodyThrottle = "fast" } }, "bad_args", "maker {keepColours: true, bodyThrottle: \"fast\"} → bad_args");
        c.Check((string)b1["detail"]?["arg"] == "maker.bodyThrottle" && !MakerServices.KeepColoursOnItemChange, "… naming maker.bodyThrottle; keepColours not applied (all or nothing)", Ctx.Short(c.LastText));
        c.Fails(new { cmd = "app.options", maker = new { keepColours = 1 } }, "bad_args", "maker {keepColours: 1} → bad_args");
        var b3 = c.Fails(new { cmd = "app.options", maker = new { colours = true } }, "bad_args", "maker {colours: true} → bad_args");
        c.Check((b3["detail"]?["candidates"] as JArray)?.Count == 3, "… with the three members as candidates", Ctx.Short(c.LastText));
        c.Fails(new { cmd = "app.options", maker = 5 }, "bad_args", "maker 5 → bad_args");
        c.Fails(new { cmd = "app.options", maker = new { keepColours = true }, routes = new { lineWidth = 9 } }, "bad_args", "maker valid + routes invalid → bad_args");
        c.Check(!MakerServices.KeepColoursOnItemChange && !MakerServices.CopyAccessoryPluginData && CharaEditFlush.BodyThrottle == BodyThrottleMode.Auto, "the switches are unchanged after the refused calls");
    }

    /// <summary>An item change of the top with keepColours on (the colour of set 0 kept) or off (the new prefab's defaults).</summary>
    static void C36KeepColours(Ctx c, string f, bool keep)
    {
        string sha0 = W1DocSha(c);
        int n0 = W1UndoCount(c);
        c.Ok(new { cmd = "chara.edit.set", key = f, set = new Dictionary<string, object> { ["coord.clothes.top.colors[0].base"] = new[] { 0.11, 0.22, 0.33, 1.0 } } }, "keepColours " + keep + ": set the top's colour 1");
        string marked = C34J(C30Value(c, f, "coord.clothes.top.colors[0].base"));
        string cur = (string)C30Value(c, f, "coord.clothes.top.item");
        // another vanilla top with a prefab (row 0 is the none row: no prefab defaults, the colours stay either way)
        string other = C30Strs(c.Call(new { cmd = "chara.edit.list", key = f, path = "coord.clothes.top.item", limit = 10000 })["rows"])
            .Select(x => (string)x["ref"]).FirstOrDefault(x => x != cur && x.StartsWith("illusion.hs2#", StringComparison.Ordinal) && x != "illusion.hs2#0");
        if (other == null) { c.Check(false, "keepColours " + keep + ": another top row", cur); return; }
        c.Ok(new { cmd = "chara.edit.item", key = f, path = "coord.clothes.top.item", @ref = other }, "keepColours " + keep + ": chara.edit.item top → " + other);
        string after = C34J(C30Value(c, f, "coord.clothes.top.colors[0].base"));
        if (keep) c.Check(after == marked, "keepColours on: an item change keeps the colour", marked + " → " + after);
        else if (after != marked) c.Check(true, "keepColours off: an item change takes the new prefab's colour", marked + " → " + after);
        else c.Warn("keepColours off: the new prefab's default colour equals the marked one (" + other + "); the switch's effect is not shown by this row");
        while (W1UndoCount(c) > n0) c.Ok(new { cmd = "undo" }, "undo");
        c.Check(W1DocSha(c) == sha0, "keepColours " + keep + ": undo restores the scene");
    }
}
