// C42: the advanced-pose commands (Phase 6.3 package C) in an edit-mode test host on the clothed
// SFW female target (BR-Chan), a vanilla item and a character without a card. AC1 every command's arguments, replies and errors, one
// history entry with its label and undo, redo and `undo: false`, `merge`, a batch, `strict`, wrong_kind, a character that is not built,
// keepMissing and stale lists, the limits; AC2 the registration: Phase63Names, group, Since, flags, and bones.dump and chara.bones
// official with their contract unchanged; AC3 the Api listings Tools/api/HS2Next.Api*.surface.txt equal to the code. What the values do
// to the bones, the chains and the weights is AdvancedPoseTest's; the mosaic refusal on its fixtures is MosaicTest's (one check here
// on what the character itself has).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next.Api.Bridge;
using HS2Next.App;
using HS2Next.App.Commands;
using HS2Next.Save;
using HS2Next.Scenes;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class BridgeCoreTest
{
    const string C42Finger = "cf_J_Hand_Index02_L", C42Face = "cf_J_MouthMove", C42Spine = "cf_J_Spine02", C42Gone = "no_such_bone";
    const string C42Nope = "0123456789abcdef0123456789abcdef";

    static readonly string[] C42Reads = { "pose.bones", "pose.chains", "pose.bust", "pose.blendShapes" };

    static void C42(Ctx c)
    {
        C42Registration(c);
        C42ApiListings(c);
        var app = c.Host();
        string f = W1LoadFemale(c);
        string item = W1AddItem(c);
        if (!c.Check(f != null && item != null, "AC1: the character and the item")) return;
        var nb = app.Session.AddChara(null);
        if (!c.Check(nb != null && nb.Ok, "AC1: a character without a card (not built)", nb?.ToString())) return;
        app.Runtime.ApplyNow();
        string none = nb.NewId;
        C42Common(c, f, item, none);
        C42Bones(c, app, f, none);
        C42Chains(c, app, f, none);
        C42Bust(c, f, none);
        C42Blend(c, app, f, none);
        c.Ok(new { cmd = "delete", keys = new[] { none } }, "delete the character without a card");
    }

    // ---------------------------------------------------------------- AC2

    static void C42Registration(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        var names = CoreCommands.Phase63Names.ToList();
        var want = new[]
        {
            "pose.bones", "pose.bones.set", "pose.bones.clear", "pose.chains", "pose.chains.set", "pose.chains.clear",
            "pose.bust", "pose.bust.set", "pose.bust.clear", "pose.blendShapes", "pose.blendShapes.set", "pose.blendShapes.clear",
        };
        c.Check(names.SequenceEqual(want), "AC2: Phase63Names = the 12 commands", string.Join(", ", names));
        c.Check(names.All(CoreCommands.AllNames.Contains), "AC2: all in CoreCommands.AllNames");
        c.Check(CoreCommands.Groups.Contains("advanced pose"), "AC2: the group \"advanced pose\" is in CoreCommands.Groups", string.Join(", ", CoreCommands.Groups));
        var bad = new List<string>();
        foreach (var n in names)
        {
            var s = reg.Find(n);
            if (s == null) { bad.Add(n + ": not registered"); continue; }
            bool read = C42Reads.Contains(n);
            if (s.Owner != BridgeOwners.Core) bad.Add(n + ": owner " + s.Owner);
            if (s.Group != "advanced pose") bad.Add(n + ": group " + s.Group);
            if (s.Since != "1.2.0") bad.Add(n + ": since " + s.Since);
            if (s.Gate != ReadyGate.App) bad.Add(n + ": gate " + s.Gate);
            if ((s.Flags & CommandFlags.Experimental) == 0) bad.Add(n + ": not experimental");
            if (read != ((s.Flags & CommandFlags.ReadOnly) != 0)) bad.Add(n + ": readOnly flag");
            if (read == ((s.Flags & CommandFlags.Mutates) != 0)) bad.Add(n + ": mutates flag");
            if (s.Examples.Count == 0) bad.Add(n + ": no example");
            if (s.Returns.Count == 0) bad.Add(n + ": no returned field");
            if (s.Args.All(a => a.Name != "key" || !a.Required)) bad.Add(n + ": key is not required");
            if (s.Returns.Any(r => r.Name == "error")) bad.Add(n + ": a returned field named error");
            string help = s.Summary + string.Concat(s.Args.Select(a => a.Doc)) + string.Concat(s.Returns.Select(r => r.Doc));
            if (help.Contains("\u00a7")) bad.Add(n + ": a section mark in the help");
            var h = c.Call(new { cmd = "help", name = n });
            if (h.Value<bool?>("ok") != true || (h["command"]?["flags"] as JArray)?.Any(x => (string)x == "experimental") != true) bad.Add(n + ": help");
        }
        c.Check(bad.Count == 0, "AC2: each has owner core, the group, Since 1.2.0, gate app, experimental, readOnly or mutates, an example, returned fields, a required key, no member error, help without a section mark", string.Join("; ", bad));
        foreach (var (n, args) in new[] { ("bones.dump", new[] { "key", "root" }), ("chara.bones", new[] { "key", "names" }) })
        {
            var s = reg.Find(n);
            c.Check(s != null && (s.Flags & CommandFlags.Experimental) == 0 && (s.Flags & CommandFlags.ReadOnly) != 0 && s.Since == "1.0.0" && s.Group == "dumps",
                "AC2: " + n + " is official: not experimental, readOnly, Since 1.0.0, group dumps", s?.Flags + " " + s?.Since + " " + s?.Group);
            c.Check(s != null && s.Args.Select(a => a.Name).SequenceEqual(args), "AC2: " + n + " keeps its arguments", string.Join(", ", s?.Args.Select(a => a.Name) ?? new string[0]));
        }
        c.Check((reg.Find("bones.dump")?.Returns.Select(r => r.Name) ?? new string[0]).SequenceEqual(new[] { "bones" }), "AC2: bones.dump keeps its returned fields");
        c.Check((reg.Find("chara.bones")?.Returns.Select(r => r.Name) ?? new string[0]).SequenceEqual(new[] { "bones", "time" }), "AC2: chara.bones keeps its returned fields");
        c.Check((reg.Find("face.dump")?.Flags & CommandFlags.Experimental) != 0, "AC2: face.dump stays experimental");
        c.Dispose(app);
    }

    // ---------------------------------------------------------------- AC3

    static void C42ApiListings(Ctx c)
    {
        var v = ApiSurfaceTest.Ver.Current;
        foreach (var (file, asm, name) in new[]
        {
            (ApiSurfaceTest.ApiFile, typeof(HS2Next.ApiVersion).Assembly, "HS2Next.Api"),
            (ApiSurfaceTest.ApiUnityFile, typeof(HS2Next.Api.Unity.IUnityPluginServices).Assembly, "HS2Next.Api.Unity"),
        })
        {
            string listing = File.ReadAllText(Path.Combine(ApiSurfaceTest.ListingDir, file), System.Text.Encoding.UTF8).Replace("\r\n", "\n");
            string code = ApiSurfaceTest.AssemblySurface(asm, name, v).Replace("\r\n", "\n");
            c.Check(listing == code, "AC3: Tools/api/" + file + " equals the code's surface (" + listing.Length + " characters)", listing.Length + " / " + code.Length);
        }
        c.Check(HS2Next.ApiVersion.Semver == "1.2.0", "AC3: ApiVersion stays 1.2.0", HS2Next.ApiVersion.Semver);
    }

    // ---------------------------------------------------------------- every command

    static void C42Common(Ctx c, string f, string item, string none)
    {
        foreach (var n in CoreCommands.Phase63Names)
        {
            string text(string key, string more = "") => "{\"cmd\":\"" + n + "\",\"key\":\"" + key + "\"" + more + "}";
            bool read = C42Reads.Contains(n);
            string req = C42Required(n);
            c.Fails("{\"cmd\":\"" + n + "\"}", "bad_args", n + ": no key -> bad_args");
            c.Fails(text(item, req), "wrong_kind", n + ": the key of an item -> wrong_kind");
            c.Fails(text(C42Nope, req), "not_found", n + ": an unknown key -> not_found");
            c.Fails("{\"cmd\":\"" + n + "\",\"key\":5}", "bad_args", n + ": a number as the key -> bad_args");
            var strict = c.Call(text(f, req + ",\"nope\":1,\"strict\":true"));
            c.Check(strict.Value<string>("code") == "bad_args" && (string)strict["detail"]?["arg"] == "nope", n + ": strict: an unknown argument -> bad_args naming it", Ctx.Short(c.LastText));
            if (read) c.Fails(text(none), "unavailable", n + ": a character that is not built -> unavailable");
        }
        // a warning without strict
        var w = c.Ok("{\"cmd\":\"pose.bones\",\"key\":\"" + f + "\",\"limit\":1,\"nope\":1}", "an unknown argument without strict");
        c.Check((w["warnings"] as JArray)?.Any(x => ((string)x).Contains("nope")) == true, "... a warning names it", Ctx.Short(c.LastText));
    }

    // the required arguments besides key, so that the key is what a request fails on
    static string C42Required(string n)
    {
        switch (n)
        {
            case "pose.chains.set": return ",\"chain\":\"other/x\",\"damping\":0.5";
            case "pose.bust.set": return ",\"chain\":\"breastL\",\"damping\":0.5";
            case "pose.blendShapes.set": return ",\"renderer\":\"head/x\",\"shape\":\"s\",\"weight\":1";
            case "pose.bones.set": return ",\"bone\":\"" + C42Finger + "\",\"pos\":[0,0,0.001]";
            case "pose.bones.clear": case "pose.chains.clear": case "pose.bust.clear": case "pose.blendShapes.clear": return ",\"all\":true";
            default: return "";
        }
    }

    static JObject C42Row(JObject reply, string list, string member, string name) =>
        (reply[list] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x[member] == name);

    static bool C42V3(JToken t, float x, float y, float z) => t is JArray a && a.Count == 3 && (float)a[0] == x && (float)a[1] == y && (float)a[2] == z;

    static CharaAdvancedPose C42Block(AppCore app, string id) => (app.Session.Model.Find(id).State as CharaNodeState).AdvancedPose;

    static void C42Err(Ctx c, object req, string code, string arg, string what)
    {
        var r = c.Call(req);
        c.Check(r.Value<bool?>("ok") == false && r.Value<string>("code") == code && (arg == null || (string)r["detail"]?["arg"] == arg),
            what + " -> " + code + (arg != null ? " naming " + arg : ""), Ctx.Short(c.LastText));
    }

    // ---------------------------------------------------------------- pose.bones

    static void C42Bones(Ctx c, AppCore app, string f, string none)
    {
        string sha0 = W1DocSha(c);
        var all = c.Ok(new { cmd = "pose.bones", key = f }, "pose.bones");
        var rows = all["bones"] as JArray;
        c.Check(rows != null && rows.Count > 300 && (int?)all["count"] == rows.Count && (all["stale"] as JArray)?.Count == 0, "pose.bones: the bones, count, no stale", Ctx.Short(c.LastText));
        var finger = C42Row(all, "bones", "bone", C42Finger);
        var names = new[] { "bone", "name", "parent", "depth", "part", "fkGroup", "corrected", "pos", "rot", "scale", "shown", "physics", "protected" };
        c.Check(finger != null && finger.Properties().Select(p => p.Name).SequenceEqual(names), "pose.bones: a row's members", finger?.ToString(Newtonsoft.Json.Formatting.None));
        c.Check(finger != null && (string)finger["name"] == C42Finger && (string)finger["part"] == "body" && (int)finger["depth"] > 0 && (string)finger["parent"] != null && (string)finger["fkGroup"] == "LeftHand" &&
                (bool)finger["corrected"] == false && C42V3(finger["pos"], 0, 0, 0) && C42V3(finger["rot"], 0, 0, 0) && C42V3(finger["scale"], 1, 1, 1) && (bool)finger["shown"] && !(bool)finger["physics"] && !(bool)finger["protected"],
            "pose.bones: the finger's row: part body, an FK group, no correction", finger?.ToString(Newtonsoft.Json.Formatting.None));
        var head = c.Ok(new { cmd = "pose.bones", key = f, part = "head" }, "pose.bones part head");
        var mouth = C42Row(head, "bones", "bone", C42Face);
        c.Check((head["bones"] as JArray).OfType<JObject>().All(x => (string)x["part"] == "head") && mouth != null && mouth["fkGroup"].Type == JTokenType.Null, "pose.bones part head: only that part; a bone without an FK row has a null fkGroup", mouth?.ToString(Newtonsoft.Json.Formatting.None));
        var fil = c.Ok(new { cmd = "pose.bones", key = f, filter = "hand_index02" }, "pose.bones filter");
        c.Check((fil["bones"] as JArray).Count >= 2 && (fil["bones"] as JArray).OfType<JObject>().All(x => ((string)x["name"]).IndexOf("hand_index02", StringComparison.OrdinalIgnoreCase) >= 0), "pose.bones filter: by name, ignoring case", Ctx.Short(c.LastText));
        var lim = c.Ok(new { cmd = "pose.bones", key = f, limit = 3 }, "pose.bones limit");
        c.Check((lim["bones"] as JArray).Count == 3 && (int)lim["count"] == rows.Count, "pose.bones limit: three rows, count before the limit", (int?)lim["count"] + "");
        c.Check((c.Ok(new { cmd = "pose.bones", key = f, correctedOnly = true }, "pose.bones correctedOnly")["bones"] as JArray).Count == 0, "pose.bones correctedOnly: none yet");
        C42Err(c, new { cmd = "pose.bones", key = f, part = "nope" }, "bad_args", "part", "pose.bones part nope");
        C42Err(c, new { cmd = "pose.bones", key = f, part = "other" }, "bad_args", "part", "pose.bones part other");
        C42Err(c, new { cmd = "pose.bones", key = f, part = "hair:9" }, "bad_args", "part", "pose.bones part hair:9");
        C42Err(c, new { cmd = "pose.bones", key = f, part = 1 }, "bad_args", "part", "pose.bones a number as the part");
        C42Err(c, new { cmd = "pose.bones", key = f, filter = 1 }, "bad_args", "filter", "pose.bones a number as the filter");
        C42Err(c, new { cmd = "pose.bones", key = f, correctedOnly = 1 }, "bad_args", "correctedOnly", "pose.bones a number as correctedOnly");
        C42Err(c, new { cmd = "pose.bones", key = f, limit = "x" }, "bad_args", "limit", "pose.bones a text as the limit");
        C42Err(c, new { cmd = "pose.bones", key = f, limit = -1 }, "bad_args", "limit", "pose.bones limit -1");

        // set: one entry, undo, redo, undo: false
        Quaternion fingerNow()
        {
            var e = (c.Ok(new { cmd = "bones.dump", key = f }, "bones.dump")["bones"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["name"] == C42Finger);
            var a = e?["lq"] as JArray;
            return a != null ? new Quaternion((float)a[0], (float)a[1], (float)a[2], (float)a[3]) : new Quaternion(0, 0, 0, 0);
        }
        var bare = fingerNow();
        var one = W1History1(c, new { cmd = "pose.bones.set", key = f, bone = C42Finger, rot = new[] { 0, 0, 15 } }, "Bone correction", "pose.bones.set rot",
            () => new { cmd = "pose.bones.set", key = f, bone = C42Spine, pos = new[] { 0, 0.01, 0 }, undo = false });
        c.Check((one["changed"] as JArray)?.Single().ToString() == C42Finger && (one["removed"] as JArray)?.Count == 0 && C42V3(one["bones"]?[C42Finger]?["rot"], 0, 0, 15) && C42V3(one["bones"]?[C42Finger]?["scale"], 1, 1, 1),
            "pose.bones.set: {changed, removed, bones}", Ctx.Short(one.ToString(Newtonsoft.Json.Formatting.None)));
        c.Check(Quaternion.Angle(fingerNow(), bare * Quaternion.Euler(0, 0, 15)) < 0.01f, "the bone shows the correction at once (bones.dump, in the same frame): the pose times the rotation", fingerNow().eulerAngles.ToString("F3"));
        c.Check(C42Block(app, f).Bones.ContainsKey(C42Spine), "undo: false stored its value");
        c.Ok(new { cmd = "pose.bones.clear", key = f, bone = C42Spine, undo = false }, "undo: false clear");
        var row = C42Row(c.Ok(new { cmd = "pose.bones", key = f, correctedOnly = true }, "pose.bones correctedOnly"), "bones", "bone", C42Finger);
        c.Check(row != null && (bool)row["corrected"] && C42V3(row["rot"], 0, 0, 15) && (bool)row["shown"], "pose.bones: the row shows the correction", row?.ToString(Newtonsoft.Json.Formatting.None));

        // members left out keep their value; identity removes the entry
        var keep = c.Ok(new { cmd = "pose.bones.set", key = f, bone = C42Finger, scale = new[] { 1.1, 1, 1 } }, "pose.bones.set scale");
        c.Check(C42V3(keep["bones"]?[C42Finger]?["rot"], 0, 0, 15), "members left out keep their value", Ctx.Short(c.LastText));
        var gone = c.Ok(new { cmd = "pose.bones.set", key = f, bone = C42Finger, rot = new[] { 0, 0, 0 }, scale = new[] { 1, 1, 1 } }, "pose.bones.set identity");
        c.Check((gone["removed"] as JArray)?.Single().ToString() == C42Finger && (gone["changed"] as JArray)?.Count == 0 && !C42Block(app, f).Bones.ContainsKey(C42Finger) && gone["bones"]?[C42Finger] == null, "an identity value removes the entry", Ctx.Short(c.LastText));

        // a value without a change
        c.Ok(new { cmd = "pose.bones.set", key = f, bone = C42Finger, rot = new[] { 1, 2, 3 } }, "pose.bones.set");
        int n0 = W1UndoCount(c);
        var same = c.Ok(new { cmd = "pose.bones.set", key = f, bone = C42Finger, rot = new[] { 1, 2, 3 } }, "pose.bones.set the same value");
        c.Check((same["changed"] as JArray)?.Count == 0 && W1UndoCount(c) == n0, "the same value: nothing changed, no entry", Ctx.Short(c.LastText));

        // several bones: one entry; a refusal stores none
        n0 = W1UndoCount(c);
        var map = c.Ok(new { cmd = "pose.bones.set", key = f, bones = new JObject { [C42Face] = new JObject { ["scale"] = new JArray(1.0, 1.2, 1.0) }, [C42Spine] = new JObject { ["pos"] = new JArray(0, 0.02, 0), ["rot"] = new JArray(5, 0, 0) } } }, "pose.bones.set bones");
        c.Check(W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Bone correction" && (map["changed"] as JArray)?.Count == 2 && C42V3(map["bones"]?[C42Spine]?["rot"], 5, 0, 0), "pose.bones.set bones: one entry for both", Ctx.Short(c.LastText));
        int stored = C42Block(app, f).Bones.Count;
        C42Err(c, new { cmd = "pose.bones.set", key = f, bones = new JObject { ["cf_J_Neck"] = new JObject { ["pos"] = new JArray(0, 0.1, 0) }, [C42Gone] = new JObject { ["pos"] = new JArray(0, 0.1, 0) } } }, "not_found", "bones." + C42Gone, "a bone this character lacks");
        c.Check(C42Block(app, f).Bones.Count == stored, "... stores neither");

        // keepMissing and stale
        var kept = c.Ok(new { cmd = "pose.bones.set", key = f, bone = C42Gone, pos = new[] { 0, 0.1, 0 }, keepMissing = true }, "keepMissing stores a bone this character lacks");
        c.Check((kept["changed"] as JArray)?.Single().ToString() == C42Gone, "... changed names it", Ctx.Short(c.LastText));
        var st = c.Ok(new { cmd = "pose.bones", key = f, limit = 0 }, "pose.bones with a missing bone");
        c.Check((st["stale"] as JArray)?.Select(x => (string)x).Contains(C42Gone) == true && (st["bones"] as JArray).Count == 0, "stale lists it", Ctx.Short(c.LastText));
        var identityOfGone = c.Ok(new { cmd = "pose.bones.set", key = f, bone = C42Gone, pos = new[] { 0, 0, 0 } }, "identity on a stale entry needs no bone");
        c.Check((identityOfGone["removed"] as JArray)?.Single().ToString() == C42Gone, "... and removes it", Ctx.Short(c.LastText));

        // a key that is no address, kept from a file, is named as it is
        var raw = "chara.advancedPose.bones[\"a/b\"]";
        var put = app.Session.Commands.Set(f, raw, new BoneCorrection(new Vec3(0, 1, 0), Vec3.Zero, Vec3.One));
        c.Check(put.Ok, "a key that is no address through the bus", put.ToString());
        app.Runtime.ApplyNow();
        c.Check((c.Ok(new { cmd = "pose.bones", key = f, limit = 0 }, "pose.bones")["stale"] as JArray)?.Select(x => (string)x).Contains("a/b") == true, "stale lists a key that is no address");
        c.Check((int?)c.Ok(new { cmd = "pose.bones.clear", key = f, bone = "a/b" }, "pose.bones.clear of it")["removed"] == 1, "pose.bones.clear takes that key as it is", Ctx.Short(c.LastText));

        // refusals
        C42Err(c, new { cmd = "pose.bones.set", key = f, pos = new[] { 0, 0, 1 } }, "bad_args", "bone", "neither bone nor bones");
        C42Err(c, new { cmd = "pose.bones.set", key = f, bone = C42Finger, bones = new JObject() }, "bad_args", "bone", "both bone and bones");
        C42Err(c, new { cmd = "pose.bones.set", key = f, bone = C42Finger }, "bad_args", "pos", "bone without a value");
        C42Err(c, new { cmd = "pose.bones.set", key = f, bones = new JObject(), }, "bad_args", "bones", "an empty bones map");
        C42Err(c, new { cmd = "pose.bones.set", key = f, bones = new JObject { [C42Finger] = 1 } }, "bad_args", "bones." + C42Finger, "a number for a bone");
        C42Err(c, new { cmd = "pose.bones.set", key = f, bones = new JObject { [C42Finger] = new JObject() } }, "bad_args", "bones." + C42Finger, "an empty object for a bone");
        C42Err(c, new { cmd = "pose.bones.set", key = f, bones = 1 }, "bad_args", "bones", "a number as bones");
        C42Err(c, new { cmd = "pose.bones.set", key = f, bones = new JObject { [C42Finger] = new JObject { ["pos"] = new JArray(1) } } }, "bad_args", "bones." + C42Finger + ".pos", "a short vector in bones");
        C42Err(c, new { cmd = "pose.bones.set", key = f, bones = new JObject { [C42Finger] = new JObject { ["scale"] = new JArray(0, 1, 1) } } }, "bad_args", "bones." + C42Finger + ".scale", "scale 0 in bones");
        C42Err(c, new { cmd = "pose.bones.set", key = f, bones = new JObject { ["a/b"] = new JObject { ["pos"] = new JArray(0, 1, 0) } } }, "bad_args", "bones.a/b", "an address that does not read in bones");
        C42Err(c, new { cmd = "pose.bones.set", key = f, bones = new JObject { [C42Finger] = new JObject { ["pos"] = new JArray(0, 1, 0) } }, pos = new[] { 0, 1, 0 } }, "bad_args", "pos", "pos beside bones");
        C42Err(c, new { cmd = "pose.bones.set", key = f, bone = 5, pos = new[] { 0, 0, 1 } }, "bad_args", "bone", "a number as the bone");
        C42Err(c, new { cmd = "pose.bones.set", key = f, bone = "a/b", pos = new[] { 0, 0, 1 } }, "bad_args", "bone", "an address that does not read");
        C42Err(c, new { cmd = "pose.bones.set", key = f, bone = "hair:9/x", pos = new[] { 0, 0, 1 } }, "bad_args", "bone", "a hair slot outside 0..3");
        C42Err(c, new { cmd = "pose.bones.set", key = f, bone = C42Gone, pos = new[] { 0, 0, 1 } }, "not_found", "bone", "a bone this character lacks");
        C42Err(c, new { cmd = "pose.bones.set", key = f, bone = C42Finger, pos = new[] { 1001, 0, 0 } }, "bad_args", "pos", "pos out of range");
        C42Err(c, new { cmd = "pose.bones.set", key = f, bone = C42Finger, pos = new[] { 1, 2 } }, "bad_args", "pos", "a short pos");
        C42Err(c, new { cmd = "pose.bones.set", key = f, bone = C42Finger, pos = "x" }, "bad_args", "pos", "a text as pos");
        C42Err(c, new { cmd = "pose.bones.set", key = f, bone = C42Finger, rot = new[] { 1e39, 0, 0 } }, "bad_args", "rot[0]", "rot not finite as a float");
        C42Err(c, new { cmd = "pose.bones.set", key = f, bone = C42Finger, scale = new[] { 0.0, 1, 1 } }, "bad_args", "scale", "scale 0");
        C42Err(c, new { cmd = "pose.bones.set", key = f, bone = C42Finger, scale = new[] { 1001.0, 1, 1 } }, "bad_args", "scale", "scale over the range");
        C42Err(c, new { cmd = "pose.bones.set", key = f, bone = C42Finger, rot = new[] { 1, 0, 0 }, keepMissing = 1 }, "bad_args", "keepMissing", "a number as keepMissing");
        C42Err(c, new { cmd = "pose.bones.set", key = f, bone = C42Finger, rot = new[] { 1, 0, 0 }, merge = 1 }, "bad_args", "merge", "a number as merge");
        C42Err(c, new { cmd = "pose.bones.set", key = f, bones = new JObject { [C42Face] = new JObject { ["pos"] = new JArray(0, 0.1, 0) }, [C42Spine] = new JObject { ["pos"] = new JArray(0, 0.1, 0) } }, merge = true }, "bad_args", "merge", "merge with several bones");
        var rotBig = c.Ok(new { cmd = "pose.bones.set", key = f, bone = C42Finger, rot = new[] { 720, -720, 1000 }, undo = false }, "any finite rotation");
        c.Check(C42V3(rotBig["bones"]?[C42Finger]?["rot"], 720, -720, 1000), "... is stored as given", Ctx.Short(c.LastText));
        // nested unknown members: a warning, bad_args under strict
        var nw = c.Ok(new { cmd = "pose.bones.set", key = f, bones = new JObject { [C42Finger] = new JObject { ["rot"] = new JArray(1, 0, 0), ["rott"] = 1 } }, undo = false }, "an unknown member in bones");
        c.Check((nw["warnings"] as JArray)?.Any(x => ((string)x).Contains("rott")) == true, "... a warning names it", Ctx.Short(c.LastText));
        C42Err(c, new { cmd = "pose.bones.set", key = f, bones = new JObject { [C42Finger] = new JObject { ["rot"] = new JArray(1, 0, 0), ["rott"] = 1 } }, strict = true }, "bad_args", "bones." + C42Finger + ".rott", "an unknown member in bones under strict");

        // merge: one entry per address
        c.Ok(new { cmd = "pose.bones.clear", key = f, all = true, undo = false }, "clear");
        n0 = W1UndoCount(c);
        string before = C42Block(app, f).ToJson();
        for (int i = 1; i <= 3; i++) c.Ok(new { cmd = "pose.bones.set", key = f, bone = C42Finger, rot = new[] { 0, 0, i * 5 }, merge = true }, "merge " + i);
        c.Check(W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Bone correction" && C42Block(app, f).Bones[C42Finger].Rot.Z == 15f, "merge: three edits of one bone are one entry", W1UndoCount(c) - n0 + " entries");
        string merged = C42Block(app, f).ToJson();
        c.Ok(new { cmd = "pose.bones.set", key = f, bone = C42Spine, rot = new[] { 0, 0, 5 }, merge = true }, "merge: another bone");
        c.Check(W1UndoCount(c) == n0 + 2, "merge: another bone is its own entry");
        c.Ok(new { cmd = "pose.bones.set", key = f, bone = C42Spine, rot = new[] { 0, 0, 0 }, merge = true }, "merge: back to identity");
        c.Check(W1UndoCount(c) == n0 + 3 && !C42Block(app, f).Bones.ContainsKey(C42Spine), "merge: a removal stands alone", W1UndoCount(c) - n0 + " entries");
        c.Ok(new { cmd = "undo" }, "undo the removal");
        c.Ok(new { cmd = "undo" }, "undo the other bone");
        c.Check(C42Block(app, f).ToJson() == merged, "the block after the merged edits");
        c.Ok(new { cmd = "undo" }, "undo the merged edits");
        c.Check(C42Block(app, f).ToJson() == before, "one undo takes the merged edits away");
        c.Ok(new { cmd = "redo" }, "redo the merged edits");
        c.Check(C42Block(app, f).ToJson() == merged, "one redo brings the last value");
        c.Ok(new { cmd = "undo" }, "undo the merged edits again");

        // a batch joins: one entry for both commands
        n0 = W1UndoCount(c);
        var batch = c.Call(new { cmd = "batch", cmds = new object[]
        {
            new { cmd = "pose.bones.set", key = f, bone = C42Finger, rot = new[] { 0, 0, 7 } },
            new { cmd = "pose.bones.set", key = f, bone = C42Spine, pos = new[] { 0, 0.03, 0 } },
        } });
        c.Check(batch.Value<bool?>("ok") == true && W1UndoCount(c) == n0 + 1 && C42Block(app, f).Bones.Count == 2, "a batch: one entry for its commands", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "undo" }, "undo the batch");
        c.Check(C42Block(app, f).Bones.Count == 0, "undo of the batch takes both away");

        // clear
        c.Ok(new { cmd = "pose.bones.set", key = f, bones = new JObject { [C42Finger] = new JObject { ["rot"] = new JArray(0, 0, 1) }, [C42Face] = new JObject { ["rot"] = new JArray(0, 0, 1) }, [C42Spine] = new JObject { ["rot"] = new JArray(0, 0, 1) } } }, "three corrections");
        var cl = W1History1(c, new { cmd = "pose.bones.clear", key = f, bone = C42Finger }, "Clear bone corrections", "pose.bones.clear bone");
        c.Check((int?)cl["removed"] == 1, "pose.bones.clear bone: removed 1", Ctx.Short(c.LastText));
        c.Check((int?)c.Ok(new { cmd = "pose.bones.clear", key = f, bones = new[] { C42Face, C42Finger } }, "pose.bones.clear bones")["removed"] == 1, "pose.bones.clear bones: counts what it took", Ctx.Short(c.LastText));
        n0 = W1UndoCount(c);
        c.Check((int?)c.Ok(new { cmd = "pose.bones.clear", key = f, bone = C42Finger }, "pose.bones.clear of no entry")["removed"] == 0 && W1UndoCount(c) == n0, "pose.bones.clear of no entry: removed 0, no history entry", Ctx.Short(c.LastText));
        var ca = W1History1(c, new { cmd = "pose.bones.clear", key = f, all = true }, "Clear bone corrections", "pose.bones.clear all");
        c.Check((int?)ca["removed"] == 1 && C42Block(app, f).IsEmpty, "pose.bones.clear all: the block is empty", Ctx.Short(c.LastText));
        n0 = W1UndoCount(c);
        c.Check((int?)c.Ok(new { cmd = "pose.bones.clear", key = f, all = true }, "pose.bones.clear all again")["removed"] == 0 && W1UndoCount(c) == n0, "pose.bones.clear all on nothing: removed 0, no entry", Ctx.Short(c.LastText));
        C42Err(c, new { cmd = "pose.bones.clear", key = f }, "bad_args", "all", "pose.bones.clear of nothing named");
        C42Err(c, new { cmd = "pose.bones.clear", key = f, bone = C42Finger, all = true }, "bad_args", "all", "pose.bones.clear bone and all");
        C42Err(c, new { cmd = "pose.bones.clear", key = f, all = false }, "bad_args", "all", "pose.bones.clear all false");
        C42Err(c, new { cmd = "pose.bones.clear", key = f, bone = "a/b" }, "bad_args", "bone", "pose.bones.clear an address that does not read and names no entry");
        C42Err(c, new { cmd = "pose.bones.clear", key = f, bones = "x" }, "bad_args", "bones", "pose.bones.clear a text as bones");

        // the limit
        var over = new JObject();
        for (int i = 0; i <= AdvancedPoseLimits.MaxBones; i++) over["gone_" + i] = new JObject { ["pos"] = new JArray(0, 0.1, 0) };
        C42Err(c, new { cmd = "pose.bones.set", key = f, bones = over, keepMissing = true }, "bad_args", "bones", "more bones than the limit");
        c.Check(C42Block(app, f).IsEmpty, "... stores none");

        // not built: stored without resolving
        var nb = c.Ok(new { cmd = "pose.bones.set", key = none, bone = C42Gone, rot = new[] { 0, 1, 0 } }, "pose.bones.set on a character that is not built");
        c.Check(C42Block(app, none).Bones.ContainsKey(C42Gone) && (nb["changed"] as JArray)?.Count == 1, "... stored without resolving");
        c.Check((int?)c.Ok(new { cmd = "pose.bones.clear", key = none, all = true }, "pose.bones.clear on it")["removed"] == 1, "... and cleared");
        c.Check(C42Block(app, f).IsEmpty && W1DocSha(c) == sha0, "every correction cleared: the scene document is the one before the first", W1DocSha(c) + " / " + sha0);
        c.Check(Quaternion.Angle(fingerNow(), bare) < 0.01f, "every correction cleared: the bone is back", fingerNow().eulerAngles.ToString("F3"));
    }

    // ---------------------------------------------------------------- pose.chains

    static void C42Chains(Ctx c, AppCore app, string f, string none)
    {
        var all = c.Ok(new { cmd = "pose.chains", key = f }, "pose.chains");
        var rows = (all["chains"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        c.Check((all["stale"] as JArray)?.Count == 0, "pose.chains: no stale");
        if (!c.Check(rows.Count > 0, "pose.chains: the target has swing chains", rows.Count + "")) return;
        var row = rows[0];
        var names = new[] { "chain", "part", "root", "category", "particles", "active", "noShake", "values", "base", "override" };
        c.Check(row.Properties().Select(p => p.Name).SequenceEqual(names), "pose.chains: a row's members", row.ToString(Newtonsoft.Json.Formatting.None));
        var vnames = new[] { "enabled", "weight", "damping", "elasticity", "stiffness", "inert", "radius", "gravity", "force", "freezeAxis" };
        c.Check(((JObject)row["values"]).Properties().Select(p => p.Name).SequenceEqual(vnames) && ((JObject)row["base"]).Properties().Select(p => p.Name).SequenceEqual(vnames) && row["override"].Type == JTokenType.Null,
            "pose.chains: values and base with every member, no override", row.ToString(Newtonsoft.Json.Formatting.None));
        c.Check(JToken.DeepEquals(row["values"], row["base"]), "pose.chains: values = base without an override");
        string ad = (string)row["chain"];
        c.Check(ad.StartsWith((string)row["part"] + "/", StringComparison.Ordinal) && ad.Contains((string)row["root"]) && rows.Select(x => (string)x["chain"]).Distinct().Count() == rows.Count, "pose.chains: addresses are <part>/<root>[#n], each once", ad);
        foreach (var x in rows)
            c.Check(PoseAddress.IsCanonical(PoseAddressForm.Chain, (string)x["chain"]), "pose.chains: " + (string)x["chain"] + " is canonical");

        double baseDamping = (double)row["base"]["damping"];
        double setTo = baseDamping == 0.25 ? 0.75 : 0.25;
        string sha = W1DocSha(c);
        var one = W1History1(c, new { cmd = "pose.chains.set", key = f, chain = ad, damping = setTo }, "Jiggle values", "pose.chains.set damping",
            () => new { cmd = "pose.chains.set", key = f, chain = ad, stiffness = 0.5, undo = false });
        c.Check((string)one["chain"] == ad && Math.Abs((double)one["override"]["damping"] - setTo) < 1e-6 && Math.Abs((double)one["values"]["damping"] - setTo) < 1e-6, "pose.chains.set: {chain, override, values}", Ctx.Short(c.LastText));
        var now = (c.Ok(new { cmd = "pose.chains", key = f }, "pose.chains")["chains"] as JArray).OfType<JObject>().First(x => (string)x["chain"] == ad);
        c.Check((double)now["base"]["damping"] == baseDamping && Math.Abs((double)now["values"]["damping"] - setTo) < 1e-6 && Math.Abs((double)now["override"]["stiffness"] - 0.5) < 1e-6,
            "pose.chains: base keeps the card's value, values the override, override what is stored", now.ToString(Newtonsoft.Json.Formatting.None));

        // every member; null removes one member
        var full = c.Ok(new JObject { ["cmd"] = "pose.chains.set", ["key"] = f, ["chain"] = ad, ["enabled"] = false, ["weight"] = 0.5, ["elasticity"] = 0.5, ["inert"] = 0.5, ["radius"] = 0.25, ["gravity"] = new JArray(0, -1, 0), ["force"] = new JArray(1, 0, 0), ["freezeAxis"] = 2 }, "pose.chains.set every member");
        var ov = (JObject)full["override"];
        c.Check(ov.Properties().Select(p => p.Name).SequenceEqual(vnames) && !(bool)ov["enabled"] && (int)ov["freezeAxis"] == 2 && C42V3(ov["gravity"], 0, -1, 0) && !(bool)full["values"]["enabled"] && C42V3(full["values"]["force"], 1, 0, 0), "pose.chains.set: every member stored in order", ov.ToString(Newtonsoft.Json.Formatting.None));
        var nul = c.Ok(new JObject { ["cmd"] = "pose.chains.set", ["key"] = f, ["chain"] = ad, ["gravity"] = JValue.CreateNull(), ["enabled"] = JValue.CreateNull() }, "pose.chains.set null");
        c.Check(nul["override"]["gravity"] == null && nul["override"]["enabled"] == null && nul["override"]["force"] != null && JToken.DeepEquals(nul["values"]["gravity"], row["base"]["gravity"]) && (bool)nul["values"]["enabled"] == (bool)row["base"]["enabled"], "pose.chains.set: an explicit null removes that member only", Ctx.Short(c.LastText));
        var emptied = c.Ok(new JObject { ["cmd"] = "pose.chains.set", ["key"] = f, ["chain"] = ad, ["weight"] = JValue.CreateNull(), ["damping"] = JValue.CreateNull(), ["elasticity"] = JValue.CreateNull(), ["stiffness"] = JValue.CreateNull(), ["inert"] = JValue.CreateNull(), ["radius"] = JValue.CreateNull(), ["force"] = JValue.CreateNull(), ["freezeAxis"] = JValue.CreateNull() }, "pose.chains.set null of every member");
        c.Check(emptied["override"].Type == JTokenType.Null && !C42Block(app, f).Chains.ContainsKey(ad) && JToken.DeepEquals(emptied["values"], row["base"]), "pose.chains.set: the last member removed: the entry leaves, values = base", Ctx.Short(c.LastText));
        var noneAgain = c.Ok(new JObject { ["cmd"] = "pose.chains.set", ["key"] = f, ["chain"] = ad, ["damping"] = JValue.CreateNull() }, "pose.chains.set null on no entry");
        c.Check(noneAgain["override"].Type == JTokenType.Null, "pose.chains.set: null on no entry changes nothing", Ctx.Short(c.LastText));

        // refusals
        C42Err(c, new { cmd = "pose.chains.set", key = f, chain = ad }, "bad_args", "chain", "pose.chains.set without a member");
        C42Err(c, new { cmd = "pose.chains.set", key = f, damping = 0.5 }, "bad_args", "chain", "pose.chains.set without a chain");
        C42Err(c, new { cmd = "pose.chains.set", key = f, chain = 5, damping = 0.5 }, "bad_args", "chain", "pose.chains.set a number as the chain");
        C42Err(c, new { cmd = "pose.chains.set", key = f, chain = "nope", damping = 0.5 }, "bad_args", "chain", "pose.chains.set an address that does not read");
        C42Err(c, new { cmd = "pose.chains.set", key = f, chain = "other/" + C42Gone, damping = 0.5 }, "not_found", "chain", "pose.chains.set a chain this character lacks");
        foreach (var m in new[] { "weight", "damping", "elasticity", "stiffness", "inert" })
        {
            C42Err(c, new JObject { ["cmd"] = "pose.chains.set", ["key"] = f, ["chain"] = ad, [m] = 1.5 }, "bad_args", m, "pose.chains.set " + m + " over 1");
            C42Err(c, new JObject { ["cmd"] = "pose.chains.set", ["key"] = f, ["chain"] = ad, [m] = -0.5 }, "bad_args", m, "pose.chains.set " + m + " under 0");
            C42Err(c, new JObject { ["cmd"] = "pose.chains.set", ["key"] = f, ["chain"] = ad, [m] = "x" }, "bad_args", m, "pose.chains.set a text as " + m);
        }
        C42Err(c, new { cmd = "pose.chains.set", key = f, chain = ad, radius = -1 }, "bad_args", "radius", "pose.chains.set radius under 0");
        C42Err(c, new { cmd = "pose.chains.set", key = f, chain = ad, gravity = new[] { 1, 2 } }, "bad_args", "gravity", "pose.chains.set a short gravity");
        C42Err(c, new { cmd = "pose.chains.set", key = f, chain = ad, force = "x" }, "bad_args", "force", "pose.chains.set a text as force");
        C42Err(c, new { cmd = "pose.chains.set", key = f, chain = ad, freezeAxis = 4 }, "bad_args", "freezeAxis", "pose.chains.set freezeAxis 4");
        C42Err(c, new { cmd = "pose.chains.set", key = f, chain = ad, freezeAxis = 1.5 }, "bad_args", "freezeAxis", "pose.chains.set a fraction as freezeAxis");
        C42Err(c, new { cmd = "pose.chains.set", key = f, chain = ad, enabled = 1 }, "bad_args", "enabled", "pose.chains.set a number as enabled");
        c.Check(!C42Block(app, f).Chains.ContainsKey(ad), "... none stored");

        // merge
        int n0 = W1UndoCount(c);
        for (int i = 1; i <= 3; i++) c.Ok(new { cmd = "pose.chains.set", key = f, chain = ad, damping = i * 0.1, merge = true }, "pose.chains.set merge " + i);
        c.Check(W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Jiggle values", "pose.chains.set merge: one entry", W1UndoCount(c) - n0 + " entries");
        c.Ok(new { cmd = "undo" }, "undo the merged edits");
        c.Check(!C42Block(app, f).Chains.ContainsKey(ad), "... the chain's entry is gone");

        // stale, clear
        var put = app.Session.Commands.Set(f, "chara.advancedPose.chains[\"other/" + C42Gone + "\"]", new ChainValues { Damping = 0.5f });
        c.Check(put.Ok, "a chain this character lacks through the bus", put.ToString());
        app.Runtime.ApplyNow();
        c.Check((c.Ok(new { cmd = "pose.chains", key = f }, "pose.chains")["stale"] as JArray)?.Select(x => (string)x).Contains("other/" + C42Gone) == true, "pose.chains: stale lists it");
        var ce = c.Ok(new { cmd = "pose.chains.clear", key = f, chain = "other/" + C42Gone }, "pose.chains.clear chain");
        c.Check((int?)ce["removed"] == 1 && W1TopLabel(c) == "Clear jiggle values", "pose.chains.clear chain: removed 1, its label", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "pose.chains.set", key = f, chain = ad, damping = 0.5 }, "pose.chains.set");
        var cl = W1History1(c, new { cmd = "pose.chains.clear", key = f, all = true }, "Clear jiggle values", "pose.chains.clear all");
        c.Check((int?)cl["removed"] == 1 && C42Block(app, f).Chains.Count == 0, "pose.chains.clear all: removed 1", Ctx.Short(c.LastText));
        C42Err(c, new { cmd = "pose.chains.clear", key = f }, "bad_args", "all", "pose.chains.clear of nothing named");
        C42Err(c, new { cmd = "pose.chains.clear", key = f, chain = ad, all = true }, "bad_args", "all", "pose.chains.clear chain and all");
        C42Err(c, new { cmd = "pose.chains.clear", key = f, chain = "nope" }, "bad_args", "chain", "pose.chains.clear an address that does not read");

        // not built
        var nb = c.Ok(new { cmd = "pose.chains.set", key = none, chain = "other/" + C42Gone, damping = 0.5 }, "pose.chains.set on a character that is not built");
        c.Check(C42Block(app, none).Chains.Count == 1 && nb["values"].Type == JTokenType.Null && Math.Abs((double)nb["override"]["damping"] - 0.5) < 1e-6, "... stored without resolving, values null", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "pose.chains.clear", key = none, all = true }, "pose.chains.clear on it");
        c.Check(C42Block(app, f).IsEmpty && W1DocSha(c) == sha, "every chain cleared: the scene document is the one before the first", W1DocSha(c) + " / " + sha);

        // the limit
        var block = C42Block(app, none);
        for (int i = 0; i < AdvancedPoseLimits.MaxChains; i++) block.Chains["other/x" + i] = new ChainValues { Damping = 0.5f };
        C42Err(c, new { cmd = "pose.chains.set", key = none, chain = "other/y", damping = 0.5 }, "bad_args", "chain", "more chains than the limit");
        var again = c.Ok(new { cmd = "pose.chains.set", key = none, chain = "other/x0", damping = 0.25, undo = false }, "an edit of a chain already there");
        c.Check(Math.Abs((double)again["override"]["damping"] - 0.25) < 1e-6, "... is allowed at the limit");
        block.Chains.Clear();
    }

    // ---------------------------------------------------------------- pose.bust

    static void C42Bust(Ctx c, string f, string none)
    {
        var all = c.Ok(new { cmd = "pose.bust", key = f }, "pose.bust");
        var rows = (all["chains"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        c.Check(rows.Select(x => (string)x["chain"]).SequenceEqual(new[] { "breastL", "breastR", "hipL", "hipR" }), "pose.bust: the four chains in order", Ctx.Short(c.LastText));
        var row = rows.First();
        c.Check(row.Properties().Select(p => p.Name).SequenceEqual(new[] { "chain", "active", "values", "base", "override" }), "pose.bust: a row's members", row.ToString(Newtonsoft.Json.Formatting.None));
        var vnames = new[] { "enabled", "gravity", "force", "damping", "elasticity", "stiffness", "inert" };
        c.Check(((JObject)row["values"]).Properties().Select(p => p.Name).SequenceEqual(vnames) && JToken.DeepEquals(row["values"], row["base"]) && row["override"].Type == JTokenType.Null, "pose.bust: values = base with every member, no override", row.ToString(Newtonsoft.Json.Formatting.None));

        double baseDamping = (double)row["base"]["damping"];
        double setTo = baseDamping == 0.25 ? 0.75 : 0.25;
        var one = W1History1(c, new { cmd = "pose.bust.set", key = f, chain = "breastL", damping = setTo }, "Jiggle values", "pose.bust.set damping",
            () => new { cmd = "pose.bust.set", key = f, chain = "hipR", inert = 0.5, undo = false });
        c.Check((string)one["chain"] == "breastL" && Math.Abs((double)one["override"]["damping"] - setTo) < 1e-6 && Math.Abs((double)one["values"]["damping"] - setTo) < 1e-6, "pose.bust.set: {chain, override, values}", Ctx.Short(c.LastText));
        var now = c.Ok(new { cmd = "pose.bust", key = f }, "pose.bust");
        var bl = C42Row(now, "chains", "chain", "breastL");
        c.Check(Math.Abs((double)bl["values"]["damping"] - setTo) < 1e-6 && (double)bl["base"]["damping"] == baseDamping, "pose.bust: base keeps the card's value, values the override", bl.ToString(Newtonsoft.Json.Formatting.None));
        c.Check(C42Row(now, "chains", "chain", "hipR")?["override"]?["inert"] != null, "undo: false stored its value");
        var full = c.Ok(new JObject { ["cmd"] = "pose.bust.set", ["key"] = f, ["chain"] = "breastL", ["enabled"] = false, ["gravity"] = new JArray(0, -1, 0), ["force"] = new JArray(1, 0, 0), ["elasticity"] = 0.5, ["stiffness"] = 0.5, ["inert"] = 0.5 }, "pose.bust.set every member");
        c.Check(((JObject)full["override"]).Properties().Select(p => p.Name).SequenceEqual(vnames), "pose.bust.set: every member stored in order", full["override"].ToString(Newtonsoft.Json.Formatting.None));
        var nul = c.Ok(new JObject { ["cmd"] = "pose.bust.set", ["key"] = f, ["chain"] = "breastL", ["gravity"] = JValue.CreateNull() }, "pose.bust.set null");
        c.Check(nul["override"]["gravity"] == null && nul["override"]["force"] != null && JToken.DeepEquals(nul["values"]["gravity"], row["base"]["gravity"]), "pose.bust.set: an explicit null removes that member only", Ctx.Short(c.LastText));
        C42Err(c, new { cmd = "pose.bust.set", key = f, chain = "nope", damping = 0.5 }, "bad_args", "chain", "pose.bust.set an unknown chain");
        C42Err(c, new { cmd = "pose.bust.set", key = f, chain = "breastL" }, "bad_args", "chain", "pose.bust.set without a member");
        C42Err(c, new { cmd = "pose.bust.set", key = f, damping = 0.5 }, "bad_args", "chain", "pose.bust.set without a chain");
        C42Err(c, new { cmd = "pose.bust.set", key = f, chain = "breastL", damping = 1.5 }, "bad_args", "damping", "pose.bust.set damping over 1");
        C42Err(c, new { cmd = "pose.bust.set", key = f, chain = "breastL", gravity = new[] { 1 } }, "bad_args", "gravity", "pose.bust.set a short gravity");
        C42Err(c, new { cmd = "pose.bust.set", key = f, chain = "breastL", enabled = "x" }, "bad_args", "enabled", "pose.bust.set a text as enabled");
        int n0 = W1UndoCount(c);
        for (int i = 1; i <= 3; i++) c.Ok(new { cmd = "pose.bust.set", key = f, chain = "breastR", stiffness = i * 0.1, merge = true }, "pose.bust.set merge " + i);
        c.Check(W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Jiggle values", "pose.bust.set merge: one entry", W1UndoCount(c) - n0 + " entries");
        var cl = c.Ok(new { cmd = "pose.bust.clear", key = f, chain = "breastR" }, "pose.bust.clear chain");
        c.Check((int?)cl["removed"] == 1 && W1TopLabel(c) == "Clear jiggle values", "pose.bust.clear chain: removed 1, its label", Ctx.Short(c.LastText));
        var ca = W1History1(c, new { cmd = "pose.bust.clear", key = f, all = true }, "Clear jiggle values", "pose.bust.clear all");
        c.Check((int?)ca["removed"] == 2, "pose.bust.clear all: counts the chains that held values", Ctx.Short(c.LastText));
        n0 = W1UndoCount(c);
        c.Check((int?)c.Ok(new { cmd = "pose.bust.clear", key = f, all = true }, "pose.bust.clear all again")["removed"] == 0 && W1UndoCount(c) == n0, "pose.bust.clear of nothing: removed 0, no entry", Ctx.Short(c.LastText));
        C42Err(c, new { cmd = "pose.bust.clear", key = f }, "bad_args", "all", "pose.bust.clear of nothing named");
        C42Err(c, new { cmd = "pose.bust.clear", key = f, chain = "nope" }, "bad_args", "chain", "pose.bust.clear an unknown chain");
        var nb = c.Ok(new { cmd = "pose.bust.set", key = none, chain = "hipL", damping = 0.5 }, "pose.bust.set on a character that is not built");
        c.Check(nb["values"].Type == JTokenType.Null && Math.Abs((double)nb["override"]["damping"] - 0.5) < 1e-6, "... stored, values null", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "pose.bust.clear", key = none, all = true }, "pose.bust.clear on it");
    }

    // ---------------------------------------------------------------- pose.blendShapes

    static void C42Blend(Ctx c, AppCore app, string f, string none)
    {
        var all = c.Ok(new { cmd = "pose.blendShapes", key = f }, "pose.blendShapes");
        c.Check((all["stale"] as JArray)?.Count == 0, "pose.blendShapes: no stale");
        var head = C42Row(all, "renderers", "renderer", "head/o_head");
        var tooth = C42Row(all, "renderers", "renderer", "head/o_tooth");
        if (!c.Check(head != null && tooth != null, "pose.blendShapes: head/o_head and head/o_tooth", Ctx.Short(c.LastText))) return;
        c.Check(head.Properties().Select(p => p.Name).SequenceEqual(new[] { "renderer", "part", "name", "protected", "shapes" }) && (string)head["part"] == "head" && (string)head["name"] == "o_head" && !(bool)head["protected"], "pose.blendShapes: a row's members", Ctx.Short(head.ToString(Newtonsoft.Json.Formatting.None)));
        var shapes = (tooth["shapes"] as JArray).OfType<JObject>().ToList();
        c.Check((head["shapes"] as JArray).Count == 58 && shapes.Count == 7, "pose.blendShapes: the head's 58 shapes, the teeth's 7", (head["shapes"] as JArray).Count + " / " + shapes.Count);
        c.Check(shapes[0].Properties().Select(p => p.Name).SequenceEqual(new[] { "name", "weight", "override" }) && shapes[0]["override"].Type == JTokenType.Null, "pose.blendShapes: a shape's members, no override", shapes[0].ToString(Newtonsoft.Json.Formatting.None));
        var byPart = c.Ok(new { cmd = "pose.blendShapes", key = f, part = "head" }, "pose.blendShapes part head");
        c.Check((byPart["renderers"] as JArray).OfType<JObject>().All(x => (string)x["part"] == "head") && (byPart["renderers"] as JArray).Count >= 2, "pose.blendShapes part: only that part");
        var byName = c.Ok(new { cmd = "pose.blendShapes", key = f, renderer = "head/o_tooth" }, "pose.blendShapes renderer");
        c.Check((byName["renderers"] as JArray).Count == 1, "pose.blendShapes renderer: only that one");
        string shape = (string)shapes[0]["name"], shape2 = (string)shapes[1]["name"];
        var byFilter = c.Ok(new { cmd = "pose.blendShapes", key = f, renderer = "head/o_tooth", filter = shape.ToUpperInvariant() }, "pose.blendShapes filter");
        c.Check(((byFilter["renderers"] as JArray)?[0]?["shapes"] as JArray)?.Any(x => (string)x["name"] == shape) == true && ((byFilter["renderers"] as JArray)[0]["shapes"] as JArray).Count < 7 + 1, "pose.blendShapes filter: on shape names, ignoring case", Ctx.Short(c.LastText));
        c.Check((c.Ok(new { cmd = "pose.blendShapes", key = f, filter = "no such shape" }, "pose.blendShapes filter of nothing")["renderers"] as JArray).Count == 0, "pose.blendShapes filter: a renderer without a match is left out");
        C42Err(c, new { cmd = "pose.blendShapes", key = f, renderer = "nope" }, "bad_args", "renderer", "pose.blendShapes an address that does not read");
        C42Err(c, new { cmd = "pose.blendShapes", key = f, renderer = "head/" + C42Gone }, "not_found", "renderer", "pose.blendShapes a renderer this character lacks");
        C42Err(c, new { cmd = "pose.blendShapes", key = f, part = "other" }, "bad_args", "part", "pose.blendShapes part other");
        C42Err(c, new { cmd = "pose.blendShapes", key = f, filter = 1 }, "bad_args", "filter", "pose.blendShapes a number as the filter");

        const string r0 = "head/o_tooth";
        var one = W1History1(c, new { cmd = "pose.blendShapes.set", key = f, renderer = r0, shape, weight = 50 }, "Blend shapes", "pose.blendShapes.set",
            () => new { cmd = "pose.blendShapes.set", key = f, renderer = r0, shape = shape2, weight = 25, undo = false });
        c.Check((string)one["renderer"] == r0 && (one["changed"] as JArray)?.Single().ToString() == shape && (one["removed"] as JArray)?.Count == 0, "pose.blendShapes.set: {renderer, changed, removed}", Ctx.Short(c.LastText));
        var now = C42Row(c.Ok(new { cmd = "pose.blendShapes", key = f, renderer = r0 }, "pose.blendShapes"), "renderers", "renderer", r0);
        var s0 = (now["shapes"] as JArray).OfType<JObject>().First(x => (string)x["name"] == shape);
        c.Check(s0["override"].Type != JTokenType.Null && (double)s0["override"] == 50, "pose.blendShapes: override shows what is stored", s0.ToString(Newtonsoft.Json.Formatting.None));
        var map = c.Ok(new JObject { ["cmd"] = "pose.blendShapes.set", ["key"] = f, ["renderer"] = r0, ["weights"] = new JObject { [shape] = 60, [shape2] = JValue.CreateNull() } }, "pose.blendShapes.set weights");
        c.Check((map["changed"] as JArray)?.Single().ToString() == shape && (map["removed"] as JArray)?.Single().ToString() == shape2, "pose.blendShapes.set weights: a null removes that weight", Ctx.Short(c.LastText));
        var nulw = c.Ok(new JObject { ["cmd"] = "pose.blendShapes.set", ["key"] = f, ["renderer"] = r0, ["shape"] = shape, ["weight"] = JValue.CreateNull() }, "pose.blendShapes.set weight null");
        c.Check((nulw["removed"] as JArray)?.Single().ToString() == shape && !C42Block(app, f).BlendShapes.ContainsKey(r0), "pose.blendShapes.set: the last weight removed: the renderer leaves", Ctx.Short(c.LastText));

        // refusals
        C42Err(c, new { cmd = "pose.blendShapes.set", key = f, shape, weight = 1 }, "bad_args", "renderer", "pose.blendShapes.set without a renderer");
        C42Err(c, new { cmd = "pose.blendShapes.set", key = f, renderer = r0 }, "bad_args", "shape", "pose.blendShapes.set neither shape nor weights");
        C42Err(c, new { cmd = "pose.blendShapes.set", key = f, renderer = r0, shape, weights = new JObject { [shape] = 1 } }, "bad_args", "shape", "pose.blendShapes.set both shape and weights");
        C42Err(c, new { cmd = "pose.blendShapes.set", key = f, renderer = r0, shape }, "bad_args", "weight", "pose.blendShapes.set shape without weight");
        C42Err(c, new { cmd = "pose.blendShapes.set", key = f, renderer = r0, shape, weight = "x" }, "bad_args", "weight", "pose.blendShapes.set a text as weight");
        C42Err(c, new { cmd = "pose.blendShapes.set", key = f, renderer = r0, shape, weight = 301 }, "bad_args", "weight", "pose.blendShapes.set weight over 300");
        C42Err(c, new { cmd = "pose.blendShapes.set", key = f, renderer = r0, shape, weight = -201 }, "bad_args", "weight", "pose.blendShapes.set weight under -200");
        C42Err(c, new { cmd = "pose.blendShapes.set", key = f, renderer = r0, weights = new JObject() }, "bad_args", "weights", "pose.blendShapes.set an empty weights map");
        C42Err(c, new { cmd = "pose.blendShapes.set", key = f, renderer = r0, weights = 1 }, "bad_args", "weights", "pose.blendShapes.set a number as weights");
        C42Err(c, new { cmd = "pose.blendShapes.set", key = f, renderer = r0, weights = new JObject { [shape] = "x" } }, "bad_args", "weights." + shape, "pose.blendShapes.set a text in weights");
        C42Err(c, new { cmd = "pose.blendShapes.set", key = f, renderer = r0, weights = new JObject { [shape] = 301 } }, "bad_args", "weights." + shape, "pose.blendShapes.set a weight out of range in weights");
        C42Err(c, new { cmd = "pose.blendShapes.set", key = f, renderer = "nope", shape, weight = 1 }, "bad_args", "renderer", "pose.blendShapes.set an address that does not read");
        C42Err(c, new { cmd = "pose.blendShapes.set", key = f, renderer = "head/" + C42Gone, shape, weight = 1 }, "not_found", "renderer", "pose.blendShapes.set a renderer this character lacks");
        C42Err(c, new { cmd = "pose.blendShapes.set", key = f, renderer = r0, shape = C42Gone, weight = 1 }, "not_found", "shape", "pose.blendShapes.set a shape it lacks");
        C42Err(c, new { cmd = "pose.blendShapes.set", key = f, renderer = r0, weights = new JObject { [C42Gone] = 1 } }, "not_found", "weights." + C42Gone, "pose.blendShapes.set a shape it lacks in weights");
        C42Err(c, new { cmd = "pose.blendShapes.set", key = f, renderer = r0, shape, weight = 1, merge = 1 }, "bad_args", "merge", "pose.blendShapes.set a number as merge");
        c.Check(C42Block(app, f).BlendShapes.Count == 0, "... none stored");

        // a protected renderer: what the character has
        var prot = (all["renderers"] as JArray).OfType<JObject>().FirstOrDefault(x => (bool)x["protected"]);
        if (prot != null)
        {
            string ps = (string)(prot["shapes"] as JArray)?.FirstOrDefault()?["name"];
            c.Fails(new { cmd = "pose.blendShapes.set", key = f, renderer = (string)prot["renderer"], shape = ps, weight = 1 }, "unsupported", "pose.blendShapes.set on a protected renderer -> unsupported");
            c.Fails(new { cmd = "pose.blendShapes.set", key = f, renderer = (string)prot["renderer"], shape = ps, weight = 1, keepMissing = true }, "unsupported", "... also with keepMissing");
        }
        else c.Info("no protected renderer with shapes on this target");
        // the renderers the rule keeps out, whether they hold shapes or not: nothing is stored
        var cc = app.Runtime.FindChara(f).Context;
        var kept = new List<string>();
        foreach (var s in cc.Body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (HS2Next.Chara.Mosaic.MosaicSet.GuardsBlend(cc, s)) kept.Add("body/" + s.name);
        c.Check(kept.Count > 0, "the target has a body renderer the rule keeps out", string.Join(", ", kept));
        foreach (var k in kept)
        {
            var refused = c.Call(new { cmd = "pose.blendShapes.set", key = f, renderer = k, shape = "x", weight = 1, keepMissing = true });
            c.Check((string)refused["code"] == "unsupported" && ((string)refused["error"])?.Contains("mosaic") == true, "pose.blendShapes.set " + k + " -> unsupported, also with keepMissing", Ctx.Short(c.LastText));
        }
        c.Check(C42Block(app, f).BlendShapes.Count == 0, "... none stored");

        // keepMissing and stale
        c.Ok(new { cmd = "pose.blendShapes.set", key = f, renderer = r0, shape = C42Gone, weight = 1, keepMissing = true }, "keepMissing stores a shape it lacks");
        c.Ok(new { cmd = "pose.blendShapes.set", key = f, renderer = "head/" + C42Gone, shape, weight = 1, keepMissing = true }, "keepMissing stores a renderer it lacks");
        var st = (c.Ok(new { cmd = "pose.blendShapes", key = f }, "pose.blendShapes")["stale"] as JArray)?.Select(x => (string)x).ToList();
        c.Check(st != null && st.Contains(r0 + ":" + C42Gone) && st.Contains("head/" + C42Gone), "pose.blendShapes: stale lists both", string.Join(", ", st ?? new List<string>()));
        var cs = W1History1(c, new { cmd = "pose.blendShapes.clear", key = f, renderer = r0, shape = C42Gone }, "Clear blend shapes", "pose.blendShapes.clear shape");
        c.Check((int?)cs["removed"] == 1, "pose.blendShapes.clear shape: removed 1", Ctx.Short(c.LastText));
        var cr = c.Ok(new { cmd = "pose.blendShapes.clear", key = f, renderer = "head/" + C42Gone }, "pose.blendShapes.clear renderer");
        c.Check((int?)cr["removed"] == 1 && C42Block(app, f).BlendShapes.Count == 0, "pose.blendShapes.clear renderer: removed 1", Ctx.Short(c.LastText));

        // merge
        int n0 = W1UndoCount(c);
        for (int i = 1; i <= 3; i++) c.Ok(new { cmd = "pose.blendShapes.set", key = f, renderer = r0, shape, weight = i * 10, merge = true }, "pose.blendShapes.set merge " + i);
        c.Check(W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Blend shapes", "pose.blendShapes.set merge: one entry", W1UndoCount(c) - n0 + " entries");
        c.Ok(new { cmd = "undo" }, "undo the merged edits");
        c.Check(C42Block(app, f).BlendShapes.Count == 0, "... the renderer's entry is gone");

        // clear all, errors
        c.Ok(new { cmd = "pose.blendShapes.set", key = f, renderer = r0, weights = new JObject { [shape] = 1, [shape2] = 2 } }, "two weights");
        var ca = W1History1(c, new { cmd = "pose.blendShapes.clear", key = f, all = true }, "Clear blend shapes", "pose.blendShapes.clear all");
        c.Check((int?)ca["removed"] == 2, "pose.blendShapes.clear all: counts the weights", Ctx.Short(c.LastText));
        n0 = W1UndoCount(c);
        c.Check((int?)c.Ok(new { cmd = "pose.blendShapes.clear", key = f, all = true }, "pose.blendShapes.clear all again")["removed"] == 0 && W1UndoCount(c) == n0, "pose.blendShapes.clear of nothing: removed 0, no entry", Ctx.Short(c.LastText));
        C42Err(c, new { cmd = "pose.blendShapes.clear", key = f }, "bad_args", "all", "pose.blendShapes.clear of nothing named");
        C42Err(c, new { cmd = "pose.blendShapes.clear", key = f, shape }, "bad_args", "all", "pose.blendShapes.clear shape without a renderer");
        C42Err(c, new { cmd = "pose.blendShapes.clear", key = f, renderer = r0, all = true }, "bad_args", "all", "pose.blendShapes.clear renderer and all");
        C42Err(c, new { cmd = "pose.blendShapes.clear", key = f, renderer = "nope" }, "bad_args", "renderer", "pose.blendShapes.clear an address that does not read");

        // the limits
        var many = new JObject();
        for (int i = 0; i <= AdvancedPoseLimits.MaxWeights; i++) many["s" + i] = 1;
        C42Err(c, new { cmd = "pose.blendShapes.set", key = f, renderer = r0, weights = many, keepMissing = true }, "bad_args", "weights", "more weights than the limit");
        var block = C42Block(app, none);
        for (int i = 0; i < AdvancedPoseLimits.MaxRenderers; i++) block.BlendShapes["head/r" + i] = new BlendValues();
        C42Err(c, new { cmd = "pose.blendShapes.set", key = none, renderer = "head/other", shape, weight = 1 }, "bad_args", "renderer", "more renderers than the limit");
        block.BlendShapes.Clear();

        // not built
        var nb = c.Ok(new { cmd = "pose.blendShapes.set", key = none, renderer = "head/" + C42Gone, shape = C42Gone, weight = 1 }, "pose.blendShapes.set on a character that is not built");
        c.Check(C42Block(app, none).BlendShapes.Count == 1 && (nb["changed"] as JArray)?.Count == 1, "... stored without resolving", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "pose.blendShapes.clear", key = none, all = true }, "pose.blendShapes.clear on it");
        c.Check(C42Block(app, f).IsEmpty && C42Block(app, none).IsEmpty, "both blocks are empty at the end");
    }
}
