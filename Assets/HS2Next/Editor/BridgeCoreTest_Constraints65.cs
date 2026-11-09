// C44: the constraint commands (Phase 6.5 package C) in an edit-mode test host on the clothed SFW
// female target (BR-Chan), a vanilla item, a light, folders and a character without a card. CR1 every command's arguments and replies,
// Since and flags, one history entry with its label, undo and redo; CR2 each error of the commands; CR3 inside a batch, `undo: false`,
// `merge`; CR4 a timed constraint.set; CB6 a duplicate carries the entry with both ends copied, one undo; CB7 a scene import carries the
// file's entries with both ends imported, one entry «Import scene»; CB8 save, load, undo and redo of each edit leave the plan equal.
// What a constraint does to the bones is ConstraintTest's; the mosaic refusal on its fixtures is MosaicTest's.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next.Api.Bridge;
using HS2Next.App;
using HS2Next.App.Commands;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class BridgeCoreTest
{
    const string C44Nope = "0123456789abcdef0123456789abcdef";
    const string C44Hand = "cf_J_Hand_R", C44Finger = "cf_J_Hand_Index02_L", C44Gone = "no_such_bone";
    static readonly string[] C44Reads = { "constraint.list", "constraint.status" };
    static readonly string[] C44RowMembers = { "id", "label", "on", "weight", "source", "target", "components", "offset", "state", "lag", "effective", "reason" };
    static readonly string[] C44StandMembers = { "constraint", "state", "lag", "offset" };

    /// <summary>What the checks work on.</summary>
    sealed class C44Fix
    {
        public AppCore App;
        public string Chara, Item, Light, None, A, B, C, Child;
    }

    static void C44(Ctx c)
    {
        C44Registration(c);
        var app = c.Host();
        var x = C44Make(c, app);
        if (!c.Check(x != null, "CR1: the fixture")) return;
        C44List(c, x);
        C44Add(c, x);
        C44Ends(c, x);
        C44Set(c, x);
        C44TakeOffset(c, x);
        C44RemoveClearMove(c, x);
        C44Errors(c, x);
        C44Limits(c, x);
        C44Status(c, x);
        C44Batch(c, x);
        C44Timed(c, x);
        C44Duplicate(c, x);
        C44Import(c, x);
        C44Plan(c, x);
        C44File(c, x);
    }

    static C44Fix C44Make(Ctx c, AppCore app)
    {
        string f = W1LoadFemale(c);
        string item = W1AddItem(c);
        var light = c.Ok(new { cmd = "light.add", no = 0 }, "CR1: a light");
        var none = app.Session.AddChara(null);
        var a = app.Session.AddFolder();
        var b = app.Session.AddFolder();
        var d = app.Session.AddFolder();
        var child = app.Session.AddFolder(a.NewId);
        app.Runtime.ApplyNow();
        if (f == null || item == null || !none.Ok || !a.Ok || !b.Ok || !d.Ok || !child.Ok) return null;
        c.Ok(new { cmd = "obj.transform", key = a.NewId, pos = new[] { 1, 0, 0 } }, "CR1: folder A at x 1");
        c.Ok(new { cmd = "obj.transform", key = b.NewId, pos = new[] { 0, 2, 0 } }, "CR1: folder B at y 2");
        return new C44Fix
        {
            App = app, Chara = f, Item = item, Light = (string)light["added"]?[0]?["key"] ?? (string)light["key"], None = none.NewId, A = a.NewId, B = b.NewId, C = d.NewId, Child = child.NewId,
        };
    }

    // ---------------------------------------------------------------- helpers

    static object C44End(string key, string bone = null, string ik = null)
    {
        var o = new Dictionary<string, object> { ["key"] = key };
        if (bone != null) o["bone"] = bone;
        if (ik != null) o["ik"] = ik;
        return o;
    }

    static string C44AddId(Ctx c, object source, object target, string[] components, string what, bool keepOffset = true) =>
        (string)c.Ok(new { cmd = "constraint.add", source, target, components, keepOffset }, what)["constraint"]; 

    static string C44Pos(Ctx c, string s, string t, string what) => C44AddId(c, C44End(s), C44End(t), new[] { "position" }, what);

    static JObject C44Row(Ctx c, string id) =>
        (c.Call(new { cmd = "constraint.list" })["constraints"] as JArray)?.OfType<JObject>().FirstOrDefault(r => (string)r["id"] == id);

    static int C44Count(Ctx c) => (c.Call(new { cmd = "constraint.list" })["constraints"] as JArray)?.Count ?? -1;

    static void C44Clear(Ctx c) => c.Ok(new { cmd = "constraint.clear", undo = false }, "(clear the list)");

    static void C44Err(Ctx c, object req, string code, string arg, string what)
    {
        var r = c.Call(req);
        c.Check(r.Value<bool?>("ok") == false && r.Value<string>("code") == code && (arg == null || (string)r["detail"]?["arg"] == arg),
            what + " -> " + code + (arg != null ? " naming " + arg : ""), Ctx.Short(c.LastText));
    }

    // a refusal: its code, nothing stored, no history entry
    static void C44Refused(Ctx c, object req, string code, string arg, string what)
    {
        string sha = W1DocSha(c);
        int undo = W1UndoCount(c);
        C44Err(c, req, code, arg, what);
        c.Check(W1DocSha(c) == sha && W1UndoCount(c) == undo, what + ": nothing stored, no entry");
    }

    static bool C44V3(JToken t, double x, double y, double z, double within = 1e-5) =>
        t is JArray a && a.Count == 3 && Math.Abs((double)a[0] - x) <= within && Math.Abs((double)a[1] - y) <= within && Math.Abs((double)a[2] - z) <= within;

    // ---------------------------------------------------------------- CR1: registration

    static void C44Registration(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        var names = CoreCommands.Phase65Names.ToList();
        var want = new[] { "constraint.list", "constraint.add", "constraint.set", "constraint.remove", "constraint.clear", "constraint.move", "constraint.status" };
        c.Check(names.SequenceEqual(want), "CR1: Phase65Names = the 7 commands", string.Join(", ", names));
        c.Check(names.All(CoreCommands.AllNames.Contains), "CR1: all in CoreCommands.AllNames");
        c.Check(CoreCommands.Groups.Contains("constraint") && CkGroups.Contains("constraint"), "CR1: the group is in CoreCommands.Groups and a convention group", string.Join(", ", CoreCommands.Groups));
        c.Check(names.All(n => !CoreCommands.ParityNames.Contains(n)), "CR1: none is a parity name");
        var bad = new List<string>();
        foreach (var n in names)
        {
            var s = reg.Find(n);
            if (s == null) { bad.Add(n + ": not registered"); continue; }
            bool read = C44Reads.Contains(n);
            if (s.Owner != BridgeOwners.Core) bad.Add(n + ": owner " + s.Owner);
            if (s.Group != "constraint") bad.Add(n + ": group " + s.Group);
            if (s.Since != "1.2.0") bad.Add(n + ": since " + s.Since);
            if (s.Gate != ReadyGate.App) bad.Add(n + ": gate " + s.Gate);
            if ((s.Flags & CommandFlags.Experimental) == 0) bad.Add(n + ": not experimental");
            if (read != ((s.Flags & CommandFlags.ReadOnly) != 0)) bad.Add(n + ": readOnly flag");
            if (read == ((s.Flags & CommandFlags.Mutates) != 0)) bad.Add(n + ": mutates flag");
            if (s.Examples.Count == 0) bad.Add(n + ": no example");
            if (s.Returns.Count == 0) bad.Add(n + ": no returned field");
            if (s.Returns.Any(r => r.Name == "error")) bad.Add(n + ": a returned field named error");
            string help = s.Summary + string.Concat(s.Args.Select(a => a.Doc)) + string.Concat(s.Returns.Select(r => r.Doc));
            if (help.Contains("\u00a7")) bad.Add(n + ": a section mark in the help");
            var h = c.Call(new { cmd = "help", name = n });
            if (h.Value<bool?>("ok") != true || (h["command"]?["flags"] as JArray)?.Any(f => (string)f == "experimental") != true) bad.Add(n + ": help");
        }
        c.Check(bad.Count == 0, "CR1: each has owner core, the group, Since 1.2.0, gate app, experimental, readOnly or mutates, an example, returned fields, no member error, help without a section mark", string.Join("; ", bad));
        var args = new Dictionary<string, string[]>
        {
            ["constraint.list"] = new[] { "key" },
            ["constraint.add"] = new[] { "source", "target", "components", "keepOffset", "weight", "on", "label", "index" },
            ["constraint.set"] = new[] { "constraint", "on", "weight", "label", "source", "target", "components", "offset", "takeOffset", "merge" },
            ["constraint.remove"] = new[] { "constraint" },
            ["constraint.clear"] = new string[0],
            ["constraint.move"] = new[] { "constraint", "index" },
            ["constraint.status"] = new[] { "perf", "reset" },
        };
        foreach (var kv in args)
        {
            var got = reg.Find(kv.Key)?.Args.Select(a => a.Name).ToList() ?? new List<string>();
            c.Check(got.SequenceEqual(kv.Value), "CR1: " + kv.Key + ": the arguments of the spec", string.Join(", ", got));
        }
        var live = reg.Specs.Where(s => s.Owner == BridgeOwners.Core || s.Owner == BridgeOwners.Bridge).ToList();
        c.Check(live.Count == 265 && live.Count(s => (s.Flags & CommandFlags.Experimental) != 0) == 107, "CR1: 265 commands, 107 experimental", live.Count + "");
        c.Dispose(app);
        // the host above is gone: the next c.Host() is the one the checks use
    }

    // ---------------------------------------------------------------- constraint.list

    static void C44List(Ctx c, C44Fix x)
    {
        var empty = c.Ok(new { cmd = "constraint.list" }, "CR1: list of a scene with none");
        c.Check((empty["constraints"] as JArray)?.Count == 0, "CR1: an empty array");
        string id = C44Pos(c, x.A, x.B, "CR1: position from A to B");
        string id2 = C44Pos(c, x.C, x.Child, "CR1: position from C to the child");
        var all = c.Ok(new { cmd = "constraint.list" }, "CR1: list");
        var row = all["constraints"]?[0] as JObject;
        c.Check(row != null && row.Properties().Select(p => p.Name).SequenceEqual(C44RowMembers), "CR1: a row's members", row?.ToString(Formatting.None));
        c.Check(row != null && (string)row["id"] == id && (string)row["label"] == "" && (bool)row["on"] && (double)row["weight"] == 1 && (string)row["source"]["key"] == x.A && (string)row["target"]["key"] == x.B &&
                (string)row["target"]["ik"] == null && (row["components"] as JArray)?.Count == 1 && (string)row["state"] == "active" && (int)row["lag"] == 0 && (bool)row["effective"] && row["reason"].Type == JTokenType.Null,
            "CR1: a row's values", row?.ToString(Formatting.None));
        var by = c.Ok(new { cmd = "constraint.list", key = x.C }, "CR1: list by a source");
        c.Check((by["constraints"] as JArray)?.Count == 1 && (string)by["constraints"][0]["id"] == id2, "CR1: key: the entry with this node as its source", Ctx.Short(c.LastText));
        var by2 = c.Ok(new { cmd = "constraint.list", key = x.Child }, "CR1: list by a target");
        c.Check((by2["constraints"] as JArray)?.Count == 1 && (string)by2["constraints"][0]["id"] == id2, "CR1: key: the entry with this node as its target", Ctx.Short(c.LastText));
        var by3 = c.Ok(new { cmd = "constraint.list", key = x.Item }, "CR1: list by a node with none");
        c.Check((by3["constraints"] as JArray)?.Count == 0, "CR1: key: no entry");
        c.Fails(new { cmd = "constraint.list", key = C44Nope }, "not_found", "CR1: list: an unknown key -> not_found");
        c.Fails("{\"cmd\":\"constraint.list\",\"key\":5}", "bad_args", "CR1: list: a number as the key -> bad_args");
        var w = c.Ok("{\"cmd\":\"constraint.list\",\"nope\":1}", "CR1: an unknown argument without strict");
        c.Check((w["warnings"] as JArray)?.Any(s => ((string)s).Contains("nope")) == true, "CR1: ... a warning names it", Ctx.Short(c.LastText));
        var strict = c.Call("{\"cmd\":\"constraint.list\",\"nope\":1,\"strict\":true}");
        c.Check(strict.Value<string>("code") == "bad_args" && (string)strict["detail"]?["arg"] == "nope", "CR1: strict: an unknown argument -> bad_args naming it", Ctx.Short(c.LastText));
        C44Clear(c);
    }

    // ---------------------------------------------------------------- constraint.add

    static void C44Add(Ctx c, C44Fix x)
    {
        var req = new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.B), components = new[] { "position" } };
        var r = W1History1(c, req, "Add constraint", "CR1: constraint.add");
        c.Check(r.Properties().Where(p => p.Name != "ok" && p.Name != "cmd").Select(p => p.Name).Where(C44StandMembers.Contains).SequenceEqual(C44StandMembers), "CR1: add: id, state, lag, offset", Ctx.Short(c.LastText));
        c.Check(JsonText.IsId((string)r["constraint"]) && (string)r["state"] == "active" && (int)r["lag"] == 0, "CR1: add: a new id, active, no lag", Ctx.Short(c.LastText));
        c.Check(C44V3(r["offset"]["pos"], -1, 2, 0) && C44V3(r["offset"]["rot"], 0, 0, 0) && C44V3(r["offset"]["scale"], 1, 1, 1), "CR1: add: keepOffset by default: the offset that keeps the target where it is", Ctx.Short(c.LastText));
        C44Clear(c);

        var plain = c.Ok(new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.B), components = new[] { "position" }, keepOffset = false }, "CR1: add keepOffset false");
        c.Check(C44V3(plain["offset"]["pos"], 0, 0, 0), "CR1: add: keepOffset false: the identity", Ctx.Short(c.LastText));
        var full = c.Ok(new { cmd = "constraint.add", source = C44End(x.C), target = C44End(x.Child), components = new[] { "rotation", "position" }, keepOffset = false, weight = 0.5, on = false, label = "lean", index = 0 }, "CR1: add with every argument");
        var row = C44Row(c, (string)full["constraint"]);
        c.Check(row != null && (double)row["weight"] == 0.5 && !(bool)row["on"] && (string)row["label"] == "lean" && (string)row["state"] == "off" && !(bool)row["effective"], "CR1: add: weight, on, label stored; state off", row?.ToString(Formatting.None));
        c.Check((row["components"] as JArray).Select(t => (string)t).SequenceEqual(new[] { "position", "rotation" }), "CR1: add: the components in writing order", row?.ToString(Formatting.None));
        c.Check((string)c.Call(new { cmd = "constraint.list" })["constraints"][0]["id"] == (string)full["constraint"], "CR1: add: index 0 puts it first");
        var at1 = c.Ok(new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.B), components = new[] { "rotation" }, keepOffset = false, index = 1 }, "CR1: add in the middle");
        c.Check((string)c.Call(new { cmd = "constraint.list" })["constraints"][1]["id"] == (string)at1["constraint"], "CR1: add: index 1 puts it second");
        C44Clear(c);

        // an unknown member of an end, and an IK target as a source
        var w = c.Ok(new { cmd = "constraint.add", source = new { key = x.A, nope = 1 }, target = C44End(x.B), components = new[] { "position" } }, "CR1: an unknown member of source");
        c.Check((w["warnings"] as JArray)?.Any(s => ((string)s).Contains("nope")) == true, "CR1: ... a warning names it", Ctx.Short(c.LastText));
        C44Err(c, new { cmd = "constraint.add", source = new { key = x.A, nope = 1 }, target = C44End(x.B), components = new[] { "position" }, strict = true }, "bad_args", "source.nope", "CR1: strict: an unknown member of source");
        C44Clear(c);
    }

    // every end: a node, a bone, an IK target, an item's bone, each node kind
    static void C44Ends(Ctx c, C44Fix x)
    {
        // a bone source with parent: a node follows a hand
        var hand = c.Ok(new { cmd = "constraint.add", source = C44End(x.Chara, C44Hand), target = C44End(x.B), components = new[] { "parent" } }, "CR1: a bone as source");
        var row = C44Row(c, (string)hand["constraint"]);
        c.Check(row != null && (string)row["source"]["bone"] == C44Hand && (string)row["state"] == "active", "CR1: a bone source stored, active", row?.ToString(Formatting.None));
        // a bone target
        var finger = c.Ok(new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.Chara, C44Finger), components = new[] { "rotation" } }, "CR1: a bone as target");
        row = C44Row(c, (string)finger["constraint"]);
        c.Check(row != null && (string)row["target"]["bone"] == C44Finger && (string)row["state"] == "active", "CR1: a bone target stored, active", row?.ToString(Formatting.None));
        // an IK target with a rotation and without
        var ik = c.Ok(new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.Chara, null, "LeftHand"), components = new[] { "position", "rotation" } }, "CR1: an IK target");
        row = C44Row(c, (string)ik["constraint"]);
        c.Check(row != null && (string)row["target"]["ik"] == "LeftHand" && (string)row["state"] == "active", "CR1: an IK target stored, active", row?.ToString(Formatting.None));
        c.Check((bool)row["effective"] == false && (string)row["reason"] == "constraints.ikOff", "CR1: IK off for that limb: effective false, the reason's key", row?.ToString(Formatting.None));
        c.Ok(new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.Chara, null, "Body"), components = new[] { "parent" } }, "CR1: parent on an IK target without a rotation");
        C44Clear(c);
        // a transform of an item
        var inst = x.App.Runtime.FindItem(x.Item).Instance.transform;
        var item = c.Ok(new { cmd = "constraint.add", source = C44End(x.Item, inst.name), target = C44End(x.B), components = new[] { "position" } }, "CR1: a transform of an item as source");
        c.Check((string)item["state"] == "active", "CR1: an item's transform resolves", Ctx.Short(c.LastText));
        C44Clear(c);
        // each node kind as a target: position; scale on the two that have it
        foreach (var (key, name) in new[] { (x.Chara, "a character"), (x.Item, "an item"), (x.Light, "a light"), (x.C, "a folder") })
        {
            var r = c.Ok(new { cmd = "constraint.add", source = C44End(x.B), target = C44End(key), components = new[] { "position" } }, "CR1: " + name + " as target");
            c.Check((string)r["state"] == "active", "CR1: " + name + " as target: active", Ctx.Short(c.LastText));
            C44Clear(c);
        }
        foreach (var (key, name) in new[] { (x.Chara, "a character"), (x.Item, "an item") })
        {
            c.Ok(new { cmd = "constraint.add", source = C44End(x.B), target = C44End(key), components = new[] { "scale" } }, "CR1: scale on " + name);
            C44Clear(c);
        }
        // an end that is not built yet: stored, judged by the compile, the offset stays the identity
        var later = c.Ok(new { cmd = "constraint.add", source = C44End(x.None, C44Hand), target = C44End(x.B), components = new[] { "position" } }, "CR1: a bone of a character that is not built");
        c.Check((later["warnings"] as JArray)?.Any(s => ((string)s).Contains("not built")) == true && C44V3(later["offset"]["pos"], 0, 0, 0), "CR1: not built: a warning, the identity", Ctx.Short(c.LastText));
        c.Check((string)later["state"] != "active", "CR1: not built: not active", Ctx.Short(c.LastText));
        C44Clear(c);
    }

    // ---------------------------------------------------------------- constraint.set

    static void C44Set(Ctx c, C44Fix x)
    {
        string id = C44Pos(c, x.A, x.B, "CR1: an entry to edit");
        var r = W1History1(c, new { cmd = "constraint.set", constraint = id, weight = 0.5 }, "Constraint", "CR1: set weight", () => new { cmd = "constraint.set", constraint = id, weight = 0.75, undo = false });
        c.Check(r.Properties().Select(p => p.Name).Where(C44StandMembers.Contains).SequenceEqual(C44StandMembers) && (string)r["constraint"] == id, "CR1: set: id, state, lag, offset", Ctx.Short(c.LastText));
        c.Check(Math.Abs((double)C44Row(c, id)["weight"] - 0.75) < 1e-6, "CR1: set: undo false applied");
        W1History1(c, new { cmd = "constraint.set", constraint = id, on = false }, "Constraint", "CR1: set on");
        c.Check((string)C44Row(c, id)["state"] == "off", "CR1: set on false: state off");
        c.Ok(new { cmd = "constraint.set", constraint = id, on = true }, "CR1: set on true");
        W1History1(c, new { cmd = "constraint.set", constraint = id, label = "follow" }, "Constraint", "CR1: set label");
        c.Check((string)C44Row(c, id)["label"] == "follow", "CR1: set label stored");
        c.Ok("{\"cmd\":\"constraint.set\",\"constraint\":\"" + id + "\",\"label\":null}", "CR1: set label null");
        c.Check((string)C44Row(c, id)["label"] == "", "CR1: a label given as null is cleared");
        W1History1(c, new { cmd = "constraint.set", constraint = id, source = C44End(x.C) }, "Constraint", "CR1: set source");
        c.Check((string)C44Row(c, id)["source"]["key"] == x.C, "CR1: set source stored");
        W1History1(c, new { cmd = "constraint.set", constraint = id, target = C44End(x.Chara, null, "RightHand") }, "Constraint", "CR1: set target");
        var row = C44Row(c, id);
        c.Check((string)row["target"]["ik"] == "RightHand" && (string)row["target"]["key"] == x.Chara, "CR1: set target stored", row.ToString(Formatting.None));
        c.Ok(new { cmd = "constraint.set", constraint = id, target = C44End(x.B), source = C44End(x.A) }, "CR1: back to folders");

        // offsets: members left out stay; null is the identity; the offset no component uses is at its identity
        c.Ok(new { cmd = "constraint.set", constraint = id, offset = new { pos = new[] { 1, 2, 3 } } }, "CR1: set offset pos");
        c.Check(C44V3(C44Row(c, id)["offset"]["pos"], 1, 2, 3), "CR1: set offset pos stored");
        W1History1(c, new { cmd = "constraint.set", constraint = id, components = new[] { "position", "rotation" } }, "Constraint", "CR1: set components");
        c.Ok(new { cmd = "constraint.set", constraint = id, offset = new { rot = new[] { 0, 90, 0 } } }, "CR1: set offset rot");
        row = C44Row(c, id);
        c.Check(C44V3(row["offset"]["pos"], 1, 2, 3) && C44V3(row["offset"]["rot"], 0, 90, 0), "CR1: a member left out keeps its value", row.ToString(Formatting.None));
        c.Ok("{\"cmd\":\"constraint.set\",\"constraint\":\"" + id + "\",\"offset\":{\"pos\":null}}", "CR1: an offset member given as null");
        row = C44Row(c, id);
        c.Check(C44V3(row["offset"]["pos"], 0, 0, 0) && C44V3(row["offset"]["rot"], 0, 90, 0), "CR1: ... goes back to its identity", row.ToString(Formatting.None));
        c.Ok(new { cmd = "constraint.set", constraint = id, components = new[] { "position" } }, "CR1: a set without rotation");
        c.Check(C44V3(C44Row(c, id)["offset"]["rot"], 0, 0, 0), "CR1: the rotation no component uses is at its identity");
        c.Ok(new { cmd = "constraint.set", constraint = id, offset = new { pos = new[] { 1, 1, 1 } } }, "CR1: an offset to clear");
        c.Ok("{\"cmd\":\"constraint.set\",\"constraint\":\"" + id + "\",\"offset\":null}", "CR1: offset given as null");
        c.Check(C44V3(C44Row(c, id)["offset"]["pos"], 0, 0, 0), "CR1: ... clears all three");
        C44Err(c, new { cmd = "constraint.set", constraint = id, offset = new { } }, "bad_args", "offset", "CR1: an empty offset");
        var w = c.Ok(new { cmd = "constraint.set", constraint = id, offset = new { pos = new[] { 0, 0, 1 }, nope = 1 } }, "CR1: an unknown member of offset");
        c.Check((w["warnings"] as JArray)?.Any(s => ((string)s).Contains("nope")) == true, "CR1: ... a warning names it", Ctx.Short(c.LastText));

        // a set that changes nothing
        string sha = W1DocSha(c);
        int undo = W1UndoCount(c);
        var same = c.Ok(new { cmd = "constraint.set", constraint = id, weight = (double)C44Row(c, id)["weight"], on = true, source = C44End(x.A) }, "CR1: a set that changes nothing");
        c.Check(W1UndoCount(c) == undo && W1DocSha(c) == sha, "CR1: ... no entry, no change", Ctx.Short(c.LastText));
        c.Check((string)same["constraint"] == id && (string)same["state"] == "active", "CR1: ... the reply as ever", Ctx.Short(c.LastText));
        C44Err(c, new { cmd = "constraint.set", constraint = id }, "bad_args", null, "CR1: a set with nothing to change");
        C44Err(c, new { cmd = "constraint.set" }, "bad_args", "constraint", "CR1: a set without an id");
        C44Err(c, new { cmd = "constraint.set", id, on = false }, "bad_args", "constraint", "CR1: the envelope's id names no entry");
        C44Clear(c);
    }

    static void C44TakeOffset(Ctx c, C44Fix x)
    {
        string id = (string)c.Ok(new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.B), components = new[] { "position" }, keepOffset = false, on = false }, "CR1: an entry that is off")["constraint"];
        var r = W1History1(c, new { cmd = "constraint.set", constraint = id, takeOffset = true }, "Take offset", "CR1: takeOffset");
        c.Check(C44V3(r["offset"]["pos"], -1, 2, 0), "CR1: takeOffset: target minus source", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "constraint.set", constraint = id, on = true }, "CR1: switched on");
        c.Frames(2);
        // on an active constraint the values as shown give the offset it has
        int undo = W1UndoCount(c);
        string sha = W1DocSha(c);
        var again = c.Ok(new { cmd = "constraint.set", constraint = id, takeOffset = true }, "CR1: takeOffset on an active constraint");
        c.Check(W1UndoCount(c) == undo && W1DocSha(c) == sha, "CR1: ... changes nothing", Ctx.Short(c.LastText));
        // the way to re-seat a target: off, pose, take, on
        c.Ok(new { cmd = "constraint.set", constraint = id, on = false }, "CR1: off");
        c.Ok(new { cmd = "obj.transform", key = x.B, pos = new[] { 0, 3, 0 } }, "CR1: the target posed");
        var re = c.Ok(new { cmd = "constraint.set", constraint = id, takeOffset = true, on = true }, "CR1: take and on in one request");
        c.Check(C44V3(re["offset"]["pos"], -1, 3, 0) && (string)re["state"] == "active", "CR1: ... the new offset, active", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "obj.transform", key = x.B, pos = new[] { 0, 2, 0 } }, "CR1: the target back");
        // an end given in the same request counts; the target, active still, shows y 3
        var both = c.Ok(new { cmd = "constraint.set", constraint = id, source = C44End(x.C), takeOffset = true, on = false }, "CR1: take with a new source");
        c.Check(C44V3(both["offset"]["pos"], 0, 3, 0), "CR1: takeOffset reads the target as shown and follows the source of the same request", Ctx.Short(c.LastText));
        C44Err(c, new { cmd = "constraint.set", constraint = id, takeOffset = true, offset = new { pos = new[] { 0, 0, 0 } } }, "bad_args", "takeOffset", "CR1: takeOffset with offset");
        C44Clear(c);
    }

    // ---------------------------------------------------------------- remove, clear, move

    static void C44RemoveClearMove(Ctx c, C44Fix x)
    {
        string a = C44Pos(c, x.A, x.B, "CR1: entry a");
        string b = C44Pos(c, x.A, x.C, "CR1: entry b");
        string d = C44Pos(c, x.B, x.C, "CR1: entry d");
        var m = W1History1(c, new { cmd = "constraint.move", constraint = d, index = 0 }, "Reorder constraints", "CR1: constraint.move");
        c.Check((int)m["index"] == 0, "CR1: move: index", Ctx.Short(c.LastText));
        var ids = (c.Call(new { cmd = "constraint.list" })["constraints"] as JArray).Select(r => (string)r["id"]).ToList();
        c.Check(ids.SequenceEqual(new[] { d, a, b }), "CR1: move: the order", string.Join(", ", ids));
        int undo = W1UndoCount(c);
        c.Ok(new { cmd = "constraint.move", constraint = d, index = 0 }, "CR1: a move to where it is");
        c.Check(W1UndoCount(c) == undo, "CR1: ... no entry");
        var rm = W1History1(c, new { cmd = "constraint.remove", constraint = a }, "Remove constraint", "CR1: constraint.remove");
        c.Check((int)rm["removed"] == 1 && C44Count(c) == 2, "CR1: remove: removed 1", Ctx.Short(c.LastText));
        var cl = W1History1(c, new { cmd = "constraint.clear" }, "Clear constraints", "CR1: constraint.clear");
        c.Check((int)cl["removed"] == 2 && C44Count(c) == 0, "CR1: clear: removed 2", Ctx.Short(c.LastText));
        undo = W1UndoCount(c);
        var none = c.Ok(new { cmd = "constraint.clear" }, "CR1: clear of no list");
        c.Check((int)none["removed"] == 0 && W1UndoCount(c) == undo, "CR1: ... removed 0, no entry", Ctx.Short(c.LastText));
        // the last entry removed: the member goes
        string only = C44Pos(c, x.A, x.B, "CR1: one entry");
        c.Ok(new { cmd = "constraint.remove", constraint = only }, "CR1: the last one removed");
        c.Check(x.App.Session.Model.Constraints == null, "CR1: the scene has no list");
    }

    // ---------------------------------------------------------------- CR2

    static void C44Errors(Ctx c, C44Fix x)
    {
        string id = C44Pos(c, x.A, x.B, "CR2: an entry");
        var pos = new[] { "position" };
        // not_found
        foreach (var (req, arg, what) in new (object, string, string)[]
        {
            (new { cmd = "constraint.set", constraint = C44Nope, on = false }, "constraint", "set: an unknown id"),
            (new { cmd = "constraint.remove", constraint = C44Nope }, "constraint", "remove: an unknown id"),
            (new { cmd = "constraint.move", constraint = C44Nope, index = 0 }, "constraint", "move: an unknown id"),
            (new { cmd = "constraint.add", source = C44End(C44Nope), target = C44End(x.B), components = pos }, "source.key", "add: an unknown source"),
            (new { cmd = "constraint.add", source = C44End(x.A), target = C44End(C44Nope), components = pos }, "target.key", "add: an unknown target"),
            (new { cmd = "constraint.add", source = C44End(x.Chara, C44Gone), target = C44End(x.C), components = pos }, "source.bone", "add: a bone the character lacks, as source"),
            (new { cmd = "constraint.add", source = C44End(x.C), target = C44End(x.Chara, C44Gone), components = new[] { "rotation" } }, "target.bone", "add: a bone the character lacks, as target"),
            (new { cmd = "constraint.add", source = C44End(x.Item, C44Gone), target = C44End(x.C), components = pos }, "source.bone", "add: a transform the item lacks"),
            (new { cmd = "constraint.set", constraint = id, source = C44End(x.Chara, C44Gone) }, "source.bone", "set: a bone the character lacks, as source"),
            (new { cmd = "constraint.set", constraint = id, target = C44End(x.Chara, C44Gone) }, "target.bone", "set: a bone the character lacks, as target"),
        })
            C44Refused(c, req, "not_found", arg, "CR2: " + what);
        // wrong_kind
        foreach (var (req, arg, what) in new (object, string, string)[]
        {
            (new { cmd = "constraint.add", source = C44End(x.Light, C44Hand), target = C44End(x.C), components = pos }, "source.bone", "a bone of a light, as source"),
            (new { cmd = "constraint.add", source = C44End(x.C), target = C44End(x.Light, C44Hand), components = new[] { "rotation" } }, "target.bone", "a bone of a light, as target"),
            (new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.Item, null, "LeftHand"), components = pos }, "target.ik", "an IK target of an item"),
            (new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.Light, null, "LeftHand"), components = pos }, "target.ik", "an IK target of a light"),
            (new { cmd = "constraint.set", constraint = id, target = C44End(x.C, null, "LeftHand") }, "target.ik", "set: an IK target of a folder"),
        })
            C44Refused(c, req, "wrong_kind", arg, "CR2: " + what);
        // bad_args: components
        foreach (var (components, what) in new[]
        {
            (new string[0], "no component"), (new[] { "wobble" }, "an unknown component"), (new[] { "position", "position" }, "a component named twice"),
            (new[] { "parent", "position" }, "parent with position"), (new[] { "parent", "rotation" }, "parent with rotation"), (new[] { "parent", "lookAt" }, "parent with lookAt"), (new[] { "lookAt", "rotation" }, "lookAt with rotation"),
        })
        {
            C44Refused(c, new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.C), components }, "bad_args", "components", "CR2: add: " + what);
            C44Refused(c, new { cmd = "constraint.set", constraint = id, components }, "bad_args", "components", "CR2: set: " + what);
        }
        C44Refused(c, new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.C), components = new[] { "scale" } }, "bad_args", "components", "CR2: scale on a folder");
        C44Refused(c, new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.Light), components = new[] { "scale" } }, "bad_args", "components", "CR2: scale on a light");
        C44Refused(c, new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.Chara, null, "LeftHand"), components = new[] { "scale" } }, "bad_args", "components", "CR2: scale on an IK target");
        C44Refused(c, new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.Chara, null, "Body"), components = new[] { "rotation" } }, "bad_args", "components", "CR2: rotation on an IK target that takes none");
        C44Refused(c, new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.Chara, null, "Body"), components = new[] { "lookAt" } }, "bad_args", "components", "CR2: lookAt on an IK target that takes none");
        C44Refused(c, new { cmd = "constraint.set", constraint = id, components = new[] { "scale" } }, "bad_args", "components", "CR2: set: scale on the folder it has");
        // bad_args: values
        foreach (var w in new[] { -0.1, 1.1 })
        {
            C44Refused(c, new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.C), components = pos, weight = w }, "bad_args", "weight", "CR2: add: weight " + w);
            C44Refused(c, new { cmd = "constraint.set", constraint = id, weight = w }, "bad_args", "weight", "CR2: set: weight " + w);
        }
        string longLabel = new string('x', ConstraintLimits.MaxLabel + 1);
        C44Refused(c, new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.C), components = pos, label = longLabel }, "bad_args", "label", "CR2: add: a label over the limit");
        C44Refused(c, new { cmd = "constraint.set", constraint = id, label = longLabel }, "bad_args", "label", "CR2: set: a label over the limit");
        foreach (int i in new[] { -1, 2 })
            C44Refused(c, new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.C), components = pos, index = i }, "bad_args", "index", "CR2: add: index " + i);
        foreach (int i in new[] { -1, 1 })
            C44Refused(c, new { cmd = "constraint.move", constraint = id, index = i }, "bad_args", "index", "CR2: move: index " + i);
        C44Refused(c, new { cmd = "constraint.set", constraint = id, offset = new { pos = new[] { 2e6, 0, 0 } } }, "bad_args", "offset.pos", "CR2: an offset over the limit");
        C44Refused(c, new { cmd = "constraint.add", source = C44End(x.Chara, "a/b"), target = C44End(x.C), components = pos }, "bad_args", "source.bone", "CR2: an address that does not read");
        C44Refused(c, new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.Chara, C44Hand, "LeftHand"), components = pos }, "bad_args", "target", "CR2: bone and ik together");
        C44Refused(c, new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.Chara, null, "Tail"), components = pos }, "bad_args", "target.ik", "CR2: an unknown IK target");
        C44Refused(c, new { cmd = "constraint.add", source = new { key = x.Chara, ik = "LeftHand" }, target = C44End(x.C), components = pos }, "bad_args", "source.ik", "CR2: an IK target as source");
        C44Refused(c, new { cmd = "constraint.add", source = 3, target = C44End(x.C), components = pos }, "bad_args", "source", "CR2: a number as source");
        C44Refused(c, new { cmd = "constraint.add", source = new { bone = C44Hand }, target = C44End(x.C), components = pos }, "bad_args", "source.key", "CR2: a source without a key");
        c.Fails(new { cmd = "constraint.add", target = C44End(x.C), components = pos }, "bad_args", "CR2: add without source");
        c.Fails(new { cmd = "constraint.add", source = C44End(x.C), components = pos }, "bad_args", "CR2: add without target");
        c.Fails(new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.C) }, "bad_args", "CR2: add without components");
        // unsupported
        C44Refused(c, new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.Item, x.App.Runtime.FindItem(x.Item).Instance.name), components = new[] { "rotation" } }, "unsupported", null, "CR2: a bone of an item as target");
        C44Clear(c);
        C44Cycles(c, x);
        C44TakeRefused(c, x);
        C44Busy(c, x);
    }

    static void C44Cycles(Ctx c, C44Fix x)
    {
        string ab = C44Pos(c, x.A, x.B, "CR2: A to B");
        var bad = c.Call(new { cmd = "constraint.add", source = C44End(x.B), target = C44End(x.A), components = new[] { "position" }, label = "back" });
        string msg = (string)bad["error"];
        c.Check(bad.Value<bool?>("ok") == false && (string)bad["code"] == "bad_args" && (string)bad["detail"]?["arg"] == "source", "CR2: B to A closes a loop -> bad_args naming source", Ctx.Short(c.LastText));
        c.Check(msg != null && msg.StartsWith("constraint cycle: back -> ", StringComparison.Ordinal) && msg.EndsWith(" -> back", StringComparison.Ordinal), "CR2: the message names the rows, by label", msg);
        c.Check(C44Count(c) == 1, "CR2: nothing added");
        // a set that closes one
        string bc = C44Pos(c, x.B, x.C, "CR2: B to C");
        C44Refused(c, new { cmd = "constraint.set", constraint = bc, target = C44End(x.A) }, "bad_args", "source", "CR2: set of a target that closes a loop");
        C44Refused(c, new { cmd = "constraint.set", constraint = ab, source = C44End(x.C) }, "bad_args", "source", "CR2: set of a source that closes a loop");
        // what a switch cannot do: an entry that is off stands in the graph, so switching it on needs no check
        C44Clear(c);
        // self: equal, and under
        C44Refused(c, new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.A), components = new[] { "position" } }, "bad_args", "source", "CR2: source equal to target");
        C44Refused(c, new { cmd = "constraint.add", source = C44End(x.Child), target = C44End(x.A), components = new[] { "position" } }, "bad_args", "source", "CR2: source under its target");
        c.Ok(new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.Child), components = new[] { "position" } }, "CR2: target under its source is allowed");
        C44Clear(c);
        // an IK target: a hand follows nothing under its own arm; the other hand's arm is free
        C44Refused(c, new { cmd = "constraint.add", source = C44End(x.Chara, "cf_J_Hand_L"), target = C44End(x.Chara, null, "LeftHand"), components = new[] { "position" } }, "bad_args", "source", "CR2: a hand's IK target on that hand");
        c.Ok(new { cmd = "constraint.add", source = C44End(x.Chara, "cf_J_Hand_R"), target = C44End(x.Chara, null, "LeftHand"), components = new[] { "position" } }, "CR2: the other hand is allowed");
        C44Clear(c);
        // a long loop: at most 8 rows in the message
        var folders = new List<string> { x.A, x.B, x.C };
        for (int i = 0; i < 8; i++) { var n = x.App.Session.AddFolder(); folders.Add(n.NewId); }
        x.App.Runtime.ApplyNow();
        for (int i = 0; i + 1 < folders.Count; i++)
            c.Ok(new { cmd = "constraint.add", source = C44End(folders[i]), target = C44End(folders[i + 1]), components = new[] { "position" }, label = "row" + i }, "CR2: a chain, row " + i);
        var far = c.Call(new { cmd = "constraint.add", source = C44End(folders[folders.Count - 1]), target = C44End(folders[0]), components = new[] { "position" }, label = "last" });
        var rows = ((string)far["error"] ?? "").Replace("constraint cycle: ", "").Split(new[] { " -> " }, StringSplitOptions.None);
        c.Check((string)far["code"] == "bad_args" && rows.Length == 8 && rows[0] == "last", "CR2: a loop of 11: 8 rows named, from the entry", Ctx.Short(c.LastText));
        C44Clear(c);
        c.Ok(new { cmd = "delete", keys = folders.Skip(3).ToArray() }, "CR2: the extra folders go");
    }

    static void C44TakeRefused(Ctx c, C44Fix x)
    {
        string id = (string)c.Ok(new { cmd = "constraint.add", source = C44End(x.None, C44Hand), target = C44End(x.B), components = new[] { "position" } }, "CR2: a source that is not built")["constraint"];
        C44Refused(c, new { cmd = "constraint.set", constraint = id, takeOffset = true }, "bad_args", "takeOffset", "CR2: takeOffset with an end that does not resolve");
        C44Clear(c);

        // a broken end: the entry stays, says so, keeps remove and retarget
        var gone = x.App.Session.AddFolder();
        x.App.Runtime.ApplyNow();
        id = C44Pos(c, x.A, gone.NewId, "CR2: an entry on a folder");
        c.Ok(new { cmd = "delete", keys = new[] { gone.NewId } }, "CR2: the folder deleted");
        var row = C44Row(c, id);
        c.Check((string)row["state"] == "targetMissing" && !(bool)row["effective"] && (string)row["reason"] == gone.NewId, "CR2: targetMissing, the reason names it", row.ToString(Formatting.None));
        C44Refused(c, new { cmd = "constraint.set", constraint = id, takeOffset = true }, "bad_args", "takeOffset", "CR2: takeOffset on a missing target");
        var back = c.Ok(new { cmd = "constraint.set", constraint = id, target = C44End(x.B) }, "CR2: retarget");
        c.Check((string)back["state"] == "active", "CR2: retargeted: active", Ctx.Short(c.LastText));
        C44Clear(c);
    }

    static void C44Busy(Ctx c, C44Fix x)
    {
        var ops = x.App.Ops;
        OpResult inside = null;
        void Handler(SceneChangeBatch b)
        {
            if (inside != null) return;
            inside = ops.ClearConstraints();
        }
        x.App.Session.Events.Changed += Handler;
        try { C44Pos(c, x.A, x.B, "CR2: an edit whose event runs another edit"); }
        finally { x.App.Session.Events.Changed -= Handler; }
        c.Check(inside != null && inside.Code == "busy", "CR2: an edit while the bus delivers -> busy", inside?.ToString());
        C44Clear(c);

        // an open transaction (a drag, a batch) takes the edit as a step, as every mutation; undo waits for it
        string sha = W1DocSha(c);
        var tx = x.App.Session.Commands.Begin("Drag");
        var step = ops.AddConstraint(new ConstraintAddRequest { Source = new ConstraintEnd(x.A), Target = new ConstraintEnd(x.B), Components = new[] { "position" } });
        c.Check(step.Ok && x.App.Session.Model.Constraints?.Count == 1, "CR2: an open transaction takes the edit as a step", step.ToString());
        c.Fails(new { cmd = "undo" }, "busy", "CR2: undo while it is open -> busy");
        tx.Cancel();
        x.App.Runtime.ApplyNow();
        c.Check(W1DocSha(c) == sha && C44Count(c) == 0, "CR2: its cancel takes the edit back");
    }

    static void C44Limits(Ctx c, C44Fix x)
    {
        var s = x.App.Session;
        var list = new ConstraintList();
        for (int i = 0; i < ConstraintLimits.MaxEntries; i++)
            list.Entries.Add(new ConstraintEntry { Id = s.NewId(), Source = new ConstraintEnd(x.A), Target = new ConstraintEnd(x.B), Components = new List<string> { "position" } });
        c.Check(s.Commands.Execute(ConstraintEditCommand.ReplaceList(list)).Ok, "CR2: a list at the limit");
        x.App.Runtime.ApplyNow();
        C44Refused(c, new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.C), components = new[] { "position" } }, "bad_args", null, "CR2: an entry over the limit of 256");
        C44Clear(c);
    }

    // ---------------------------------------------------------------- constraint.status

    static void C44Status(Ctx c, C44Fix x)
    {
        var empty = c.Ok(new { cmd = "constraint.status" }, "CR1: status of a scene with none");
        c.Check((int)empty["count"] == 0 && (int)empty["active"] == 0 && !(bool)empty["passActive"] && (empty["order"] as JArray)?.Count == 0 && (empty["lagged"] as JArray)?.Count == 0 && empty["perf"] == null,
            "CR1: status: nothing, no pass, no perf", Ctx.Short(c.LastText));
        string a = C44Pos(c, x.A, x.B, "CR1: an active entry");
        string off = (string)c.Ok(new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.C), components = new[] { "position" }, on = false }, "CR1: an entry that is off")["constraint"];
        var st = c.Ok(new { cmd = "constraint.status" }, "CR1: status");
        var units = (st["order"] as JArray).OfType<JObject>().ToList();
        c.Check((int)st["count"] == 2 && (int)st["active"] == 1 && (bool)st["passActive"], "CR1: status: the counts, the pass", Ctx.Short(c.LastText));
        c.Check(units.All(u => u.Properties().Select(p => p.Name).SequenceEqual(new[] { "unit", "kind" })) && units.Any(u => (string)u["unit"] == a && (string)u["kind"] == "constraint") &&
                units.Any(u => (string)u["unit"] == x.Chara && (string)u["kind"] == "chara") && units.All(u => (string)u["unit"] != off), "CR1: status: order holds the constraint and the character", Ctx.Short(c.LastText));
        c.Frames(3);
        var p = c.Ok(new { cmd = "constraint.status", perf = true }, "CR1: status perf");
        c.Check(p["perf"] != null && p["perf"].OfType<JProperty>().Select(q => q.Name).SequenceEqual(new[] { "frames", "medianUs", "p95Us", "maxUs", "walkUs" }) && (int)p["perf"]["frames"] >= 3 && (double)p["perf"]["medianUs"] >= 0, "CR1: status: perf after three frames", Ctx.Short(c.LastText));
        var r = c.Ok(new { cmd = "constraint.status", perf = true, reset = true }, "CR1: status perf reset");
        c.Check((int)r["perf"]["frames"] >= 3, "CR1: reset: the reply before the reset", Ctx.Short(c.LastText));
        var n = c.Ok(new { cmd = "constraint.status", perf = true }, "CR1: status perf after a reset");
        c.Check((int)n["perf"]["frames"] < (int)r["perf"]["frames"], "CR1: reset: a new window (a call pumps one frame)", Ctx.Short(c.LastText));
        C44Clear(c);

        // the one lag: a left hand on an item in the same character's right hand
        c.Ok(new { cmd = "item.parent", key = x.Item, chara = x.Chara, point = "N_Hand_R" }, "CR1: the item into the right hand");
        string grip = (string)c.Ok(new { cmd = "constraint.add", source = C44End(x.Item), target = C44End(x.Chara, null, "LeftHand"), components = new[] { "position" } }, "CR1: the left hand on it")["constraint"];
        var row = C44Row(c, grip);
        c.Check((int)row["lag"] == 1, "CR1: list: lag 1", row.ToString(Formatting.None));
        var ls = c.Ok(new { cmd = "constraint.status" }, "CR1: status with a lag");
        c.Check((ls["lagged"] as JArray)?.Select(t => (string)t).SequenceEqual(new[] { grip }) == true, "CR1: status: lagged names it", Ctx.Short(c.LastText));
        C44Refused(c, new { cmd = "constraint.set", constraint = grip, target = C44End(x.Chara, null, "RightHand") }, "bad_args", "source", "CR2: the hand that holds it closes a loop");
        C44Clear(c);
        c.Ok(new { cmd = "item.parent", key = x.Item, chara = x.Chara }, "CR1: the item back at the root");

        // a bone target on the source's own character: the note
        string same = (string)c.Ok(new { cmd = "constraint.add", source = C44End(x.Chara, C44Hand), target = C44End(x.Chara, C44Finger), components = new[] { "rotation" } }, "CR1: a bone on a bone of its character")["constraint"];
        c.Check((string)C44Row(c, same)["reason"] == "constraints.sameCharaTip", "CR1: list: the reason's key of a same-character source", C44Row(c, same).ToString(Formatting.None));
        C44Clear(c);
    }

    // ---------------------------------------------------------------- CR3

    static void C44Batch(Ctx c, C44Fix x)
    {
        // a client's batch: one entry for all, and a member that fails takes all back
        int undo = W1UndoCount(c);
        string sha = W1DocSha(c);
        var ok = c.Ok(new
        {
            cmd = "batch",
            cmds = new object[]
            {
                new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.B), components = new[] { "position" } },
                new { cmd = "constraint.add", source = C44End(x.B), target = C44End(x.C), components = new[] { "position" } },
                new { cmd = "constraint.clear" },
                new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.C), components = new[] { "position" }, label = "kept" },
            },
        }, "CR3: a batch of four");
        c.Check(W1UndoCount(c) == undo + 1 && W1TopLabel(c) == "Batch" && C44Count(c) == 1, "CR3: a batch is one entry", Ctx.Short(c.LastText));
        var results = ok["results"] as JArray;
        c.Check(results?.Count == 4 && (string)results[3]["state"] == "active", "CR3: inside a batch the reply says how the entry stands", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "undo" }, "CR3: undo of the batch");
        c.Check(W1DocSha(c) == sha && C44Count(c) == 0, "CR3: one undo takes it all");

        // a loop made inside the batch is seen by the next member
        var loop = c.Call(new
        {
            cmd = "batch",
            cmds = new object[]
            {
                new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.B), components = new[] { "position" } },
                new { cmd = "constraint.add", source = C44End(x.B), target = C44End(x.A), components = new[] { "position" } },
            },
        });
        c.Check(loop.Value<bool?>("ok") == false && (int?)loop["failedAt"] == 1 && (string)loop["results"]?[1]?["code"] == "bad_args", "CR3: a batch's second member closes the loop of the first", Ctx.Short(c.LastText));
        c.Check(C44Count(c) == 1 && W1UndoCount(c) == undo + 1, "CR3: a failed member: the first stays, as one entry");
        c.Ok(new { cmd = "undo" }, "CR3: undo of that batch");
        c.Check(W1DocSha(c) == sha, "CR3: one undo takes it");

        // undo: false keeps the history, on every mutating command
        c.Ok(new { cmd = "obj.transform", key = x.C, pos = new[] { 0, 0, 0 } }, "CR3: something on the history");
        int n = W1UndoCount(c);
        string a = (string)c.Ok(new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.B), components = new[] { "position" }, undo = false }, "CR3: add undo false")["constraint"];
        string b = (string)c.Ok(new { cmd = "constraint.add", source = C44End(x.A), target = C44End(x.C), components = new[] { "position" }, undo = false }, "CR3: a second")["constraint"];
        c.Ok(new { cmd = "constraint.set", constraint = a, on = false, undo = false }, "CR3: set undo false");
        c.Ok(new { cmd = "constraint.move", constraint = b, index = 0, undo = false }, "CR3: move undo false");
        c.Ok(new { cmd = "constraint.remove", constraint = a, undo = false }, "CR3: remove undo false");
        c.Check(W1UndoCount(c) == n && C44Count(c) == 1, "CR3: undo false: no entry, the history kept");
        var cl = c.Ok(new { cmd = "constraint.clear", undo = false }, "CR3: clear undo false");
        c.Check(W1UndoCount(c) == n && !((cl["warnings"] as JArray)?.Any(w => ((string)w).StartsWith("history cleared")) ?? false), "CR3: clear undo false: the history is not cleared", Ctx.Short(c.LastText));

        // merge: one entry with the first old and the last new
        string id = C44Pos(c, x.A, x.B, "CR3: an entry for merge");
        n = W1UndoCount(c);
        string before = x.App.Session.Model.Constraints.ToJson();
        foreach (var w in new[] { 0.9, 0.8, 0.7 }) c.Ok(new { cmd = "constraint.set", constraint = id, weight = w, merge = true }, "CR3: merge " + w);
        c.Check(W1UndoCount(c) == n + 1 && W1TopLabel(c) == "Constraint", "CR3: merge: three edits, one entry");
        c.Ok(new { cmd = "constraint.set", constraint = id, weight = 0.6, merge = true }, "CR3: merge once more");
        c.Check(W1UndoCount(c) == n + 1, "CR3: merge: still one");
        c.Ok(new { cmd = "constraint.set", constraint = id, weight = 0.5 }, "CR3: an edit without merge");
        c.Check(W1UndoCount(c) == n + 2, "CR3: an edit without merge stands alone");
        c.Ok(new { cmd = "undo" }, "CR3: undo of that");
        c.Check(Math.Abs((double)C44Row(c, id)["weight"] - 0.6) < 1e-6, "CR3: undo: the merged value");
        c.Ok(new { cmd = "undo" }, "CR3: undo of the merged edits");
        c.Check(x.App.Session.Model.Constraints.ToJson() == before, "CR3: undo: the first old value");
        string other = C44Pos(c, x.A, x.C, "CR3: another entry");
        n = W1UndoCount(c);
        c.Ok(new { cmd = "constraint.set", constraint = id, weight = 0.4, merge = true }, "CR3: merge on one entry");
        c.Ok(new { cmd = "constraint.set", constraint = other, weight = 0.4, merge = true }, "CR3: merge on another");
        c.Check(W1UndoCount(c) == n + 2, "CR3: merge chains one entry's edits only");
        C44Clear(c);
    }

    // ---------------------------------------------------------------- CR4

    static void C44Timed(Ctx c, C44Fix x)
    {
        var pb = x.App.Runtime.GetPart<TimelinePlayback>();
        if (!c.Check(pb != null, "CR4: the playback")) return;
        string id = C44Pos(c, x.A, x.B, "CR4: an entry");
        int undo = W1UndoCount(c);
        c.Ok(new
        {
            cmd = "timeline.build", duration = 1, tracks = new object[0], play = false,
            events = new object[] { new { t = 0.5, @do = new { cmd = "constraint.set", constraint = id, on = false } } },
        }, "CR4: a Timeline with a timed constraint.set");
        c.Ok(new { cmd = "timeline.play" }, "CR4: play");
        long fired = pb.EventsFired;
        int k = 0;
        while (pb.Player.Time < 0.5 && k < 200) { x.App.Frame(Dt); k++; }
        c.Check(pb.EventsFired == fired + 1, "CR4: it fires at its time", "frame " + k + ", time " + pb.Player.Time);
        c.Check(x.App.Session.Model.Constraints.Find(id).On, "CR4: the crossing frame: not yet");
        x.App.Frame(Dt);
        x.App.Frame(Dt);
        var row = C44Row(c, id);
        c.Check(!(bool)row["on"] && (string)row["state"] == "off", "CR4: the frame after: off", row.ToString(Formatting.None));
        pb.CollectReplies();
        c.Check(pb.EventErrors == 0, "CR4: no error", pb.LastEventError?.Message);
        while (pb.Player.Time < 0.95 && k < 400) { x.App.Frame(Dt); k++; }
        c.Check(pb.EventsFired == fired + 1, "CR4: it fires once", (pb.EventsFired - fired) + "");
        c.Ok(new { cmd = "timeline.stop" }, "CR4: stop");
        c.Ok(new { cmd = "timeline.clear", undo = false }, "CR4: the Timeline goes");
        C44Clear(c);
    }

    // ---------------------------------------------------------------- CB6

    static void C44Duplicate(Ctx c, C44Fix x)
    {
        var ops = x.App.Ops;
        string inner = C44Pos(c, x.A, x.Child, "CB6: inner: both ends in the subtree");
        string outer = C44Pos(c, x.A, x.B, "CB6: outer: one end outside");
        string sha = W1DocSha(c);
        int undo = W1UndoCount(c), nodes = x.App.Session.Model.Count;
        var d = ops.Duplicate(new[] { x.A });
        c.Check(d.Ok && d.NewIds.Count == 1, "CB6: duplicate of the subtree", d.ToString());
        c.Check(W1UndoCount(c) == undo + 1 && W1TopLabel(c) == "Duplicate", "CB6: one entry «Duplicate»");
        c.Check(x.App.Session.Model.Count == nodes + 2, "CB6: the nodes copied");
        var rows = (c.Call(new { cmd = "constraint.list" })["constraints"] as JArray).OfType<JObject>().ToList();
        var top = x.App.Session.Model.Find(d.NewIds[0]);
        c.Check(rows.Count == 3 && (string)rows[0]["id"] == inner && (string)rows[1]["id"] == outer, "CB6: the inner entry copied, the outer not, after the originals", rows.Count + "");
        var copy = rows.Last();
        c.Check((string)copy["id"] != inner && (string)copy["source"]["key"] == top.Id && (string)copy["target"]["key"] == top.Children[0].Id && (string)copy["state"] == "active", "CB6: new id, ends on the copies, active", copy.ToString(Formatting.None));
        c.Check(x.App.Session.Selection.Primary == top.Id, "CB6: the copy is selected");
        var st = c.Ok(new { cmd = "constraint.status" }, "CB6: status");
        c.Check((int)st["active"] == 3 && (st["order"] as JArray).OfType<JObject>().Any(u => (string)u["unit"] == (string)copy["id"]), "CB6: the plan holds the copy", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "undo" }, "CB6: undo");
        c.Check(W1DocSha(c) == sha && C44Count(c) == 2 && x.App.Session.Model.Count == nodes, "CB6: one undo takes nodes and entry");
        c.Check((int)c.Ok(new { cmd = "constraint.status" }, "CB6: status after undo")["active"] == 2, "CB6: undo: the plan follows");
        c.Ok(new { cmd = "redo" }, "CB6: redo");
        c.Check(C44Count(c) == 3 && x.App.Session.Model.Find(top.Id) != null, "CB6: redo: both back, same ids");
        c.Ok(new { cmd = "undo" }, "CB6: undo again");
        C44Clear(c);

        // with no entry to carry a duplicate runs the plain add: nothing to do with the list
        var plain = ops.Duplicate(new[] { x.C });
        c.Check(plain.Ok && plain.Command.Command is AddNodesCommand && x.App.Session.Model.Constraints == null, "CB6: no list: the plain add", plain.ToString());
        c.Ok(new { cmd = "undo" }, "CB6: undo of the plain duplicate");

        // a character with a bone and an IK target: the addresses hold on the copy
        string bone = (string)c.Ok(new { cmd = "constraint.add", source = C44End(x.Chara, C44Hand), target = C44End(x.Chara, C44Finger), components = new[] { "rotation" } }, "CB6: a bone on a bone")["constraint"];
        var dc = ops.Duplicate(new[] { x.Chara });
        c.Check(dc.Ok, "CB6: duplicate of the character", dc.ToString());
        x.App.Runtime.ApplyNow();
        var cr = (c.Call(new { cmd = "constraint.list", key = dc.NewIds[0] })["constraints"] as JArray)?.OfType<JObject>().FirstOrDefault();
        c.Check(cr != null && (string)cr["source"]["bone"] == C44Hand && (string)cr["target"]["bone"] == C44Finger && (string)cr["id"] != bone && (string)cr["state"] == "active", "CB6: the copy's entry acts on the copy's bones", cr?.ToString(Formatting.None));
        c.Ok(new { cmd = "undo" }, "CB6: undo of the character's duplicate");
        C44Clear(c);
    }

    // ---------------------------------------------------------------- CB7

    static void C44Import(Ctx c, C44Fix x)
    {
        // a file: two folders with an inner entry, and an entry that names a node the file lacks
        var s = x.App.Session;
        c.Ok(new { cmd = "scene.new" }, "CB7: a new scene");
        var a = s.AddFolder();
        var b = s.AddFolder();
        var gone = s.AddFolder();
        x.App.Runtime.ApplyNow();
        c.Ok(new { cmd = "obj.transform", key = a.NewId, pos = new[] { 1, 0, 0 } }, "CB7: a folder at x 1");
        string inner = C44Pos(c, a.NewId, b.NewId, "CB7: the inner entry");
        string lost = C44Pos(c, a.NewId, gone.NewId, "CB7: an entry whose target goes");
        c.Ok(new { cmd = "delete", keys = new[] { gone.NewId } }, "CB7: its target deleted");
        c.Ok(new { cmd = "scene.save", name = "c44_import", overwrite = true, clock = "2026-11-06T00:00:00Z" }, "CB7: saved");
        var plain = s.AddFolder();
        c.Ok(new { cmd = "constraint.clear", undo = false }, "CB7: no list");
        x.App.Runtime.ApplyNow();
        c.Ok(new { cmd = "scene.save", name = "c44_plain", overwrite = true, clock = "2026-11-06T00:00:00Z" }, "CB7: a file without a list");

        c.Ok(new { cmd = "scene.new" }, "CB7: the target scene");
        var mine = s.AddFolder();
        var mine2 = s.AddFolder();
        x.App.Runtime.ApplyNow();
        string own = C44Pos(c, mine.NewId, mine2.NewId, "CB7: an entry of its own");
        string sha = W1DocSha(c);
        int undo = W1UndoCount(c), nodes = s.Model.Count;
        var r = c.Ok(new { cmd = "scene.import", path = "c44_import.hs2nscene" }, "CB7: scene.import");
        c.Check(W1UndoCount(c) == undo + 1 && W1TopLabel(c) == "Import scene", "CB7: one entry «Import scene»");
        c.Check(s.Model.Count == nodes + 2, "CB7: the file's two nodes");
        var rows = (c.Call(new { cmd = "constraint.list" })["constraints"] as JArray).OfType<JObject>().ToList();
        c.Check(rows.Count == 2 && (string)rows[0]["id"] == own, "CB7: the file's inner entry after the scene's own; the one with an end outside left out", rows.Count + "");
        var imported = rows.Last();
        var added = (r["added"] as JArray).Select(t => (string)t).ToList();
        c.Check((string)imported["id"] != inner && added.Contains((string)imported["source"]["key"]) && added.Contains((string)imported["target"]["key"]) && (string)imported["state"] == "active", "CB7: new id, ends remapped onto the imported nodes, active", imported.ToString(Formatting.None));
        c.Check(C44V3(imported["offset"]["pos"], -1, 0, 0), "CB7: the offset kept", imported.ToString(Formatting.None));
        c.Check((r["warnings"] as JArray)?.Any(w => ((string)w).StartsWith("1 constraints of the file left out", StringComparison.Ordinal)) == true, "CB7: a warning counts the entry left out", Ctx.Short(c.LastText));
        string after = W1DocSha(c);
        c.Ok(new { cmd = "undo" }, "CB7: undo");
        c.Check(W1DocSha(c) == sha && C44Count(c) == 1 && s.Model.Count == nodes, "CB7: undo: one step, the scene as before");
        c.Check((int)c.Ok(new { cmd = "constraint.status" }, "CB7: status")["active"] == 1, "CB7: undo: the plan follows");
        c.Ok(new { cmd = "redo" }, "CB7: redo");
        c.Check(W1DocSha(c) == after, "CB7: redo: the same result");
        // twice: ids never clash
        var r2 = c.Ok(new { cmd = "scene.import", path = "c44_import.hs2nscene" }, "CB7: imported again");
        var ids = (c.Call(new { cmd = "constraint.list" })["constraints"] as JArray).Select(t => (string)t["id"]).ToList();
        c.Check(ids.Count == 3 && ids.Distinct().Count() == 3, "CB7: imported twice: three distinct ids", string.Join(", ", ids));

        // a file without a list: the list as it was, no warning
        var list = s.Model.Constraints;
        var r3 = c.Ok(new { cmd = "scene.import", path = "c44_plain.hs2nscene" }, "CB7: a file without a list");
        c.Check(ReferenceEquals(list, s.Model.Constraints) && r3["warnings"] == null, "CB7: a file without a list: the same list object, no warning", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "scene.new" }, "CB7: done");
    }

    // ---------------------------------------------------------------- CB8

    // The plan as it can be read: the list, each row's state and lag, the order, the lagged ids.
    static string C44PlanText(Ctx c)
    {
        var rows = c.Call(new { cmd = "constraint.list" })["constraints"] as JArray;
        var st = c.Call(new { cmd = "constraint.status" });
        var sb = new System.Text.StringBuilder();
        foreach (var r in rows.OfType<JObject>())
            sb.Append(r.ToString(Formatting.None)).Append('\n');
        sb.Append(st["active"]).Append(' ').Append(st["passActive"]).Append(' ').Append(st["order"].ToString(Formatting.None)).Append(' ').Append(st["lagged"].ToString(Formatting.None));
        return sb.ToString();
    }

    static void C44Plan(Ctx c, C44Fix x)
    {
        c.Ok(new { cmd = "scene.new" }, "CB8: a new scene");
        var s = x.App.Session;
        string f = W1LoadFemale(c);
        var a = s.AddFolder();
        var b = s.AddFolder();
        var d = s.AddFolder();
        x.App.Runtime.ApplyNow();
        if (!c.Check(f != null && a.Ok && b.Ok && d.Ok, "CB8: the scene")) return;
        c.Ok(new { cmd = "obj.transform", key = a.NewId, pos = new[] { 1, 0, 0 } }, "CB8: a folder moved");
        string node = null, bone = null, ik = null;
        var edits = new (string what, Func<object> request)[]
        {
            ("add a node", () => new { cmd = "constraint.add", source = C44End(a.NewId), target = C44End(b.NewId), components = new[] { "position", "rotation" }, label = "node" }),
            ("add a bone", () => new { cmd = "constraint.add", source = C44End(a.NewId), target = C44End(f, C44Finger), components = new[] { "rotation" } }),
            ("add an IK target", () => new { cmd = "constraint.add", source = C44End(f, C44Hand), target = C44End(f, null, "LeftHand"), components = new[] { "position", "rotation" } }),
            ("add a chain step", () => new { cmd = "constraint.add", source = C44End(f, C44Hand), target = C44End(d.NewId), components = new[] { "parent" }, index = 0 }),
            ("set a weight", () => new { cmd = "constraint.set", constraint = node, weight = 0.5 }),
            ("set a switch", () => new { cmd = "constraint.set", constraint = bone, on = false }),
            ("set a label", () => new { cmd = "constraint.set", constraint = ik, label = "hand" }),
            ("set an offset", () => new { cmd = "constraint.set", constraint = bone, offset = new { rot = new[] { 0, 10, 0 } } }),
            ("set components", () => new { cmd = "constraint.set", constraint = node, components = new[] { "position" } }),
            ("set a source", () => new { cmd = "constraint.set", constraint = node, source = C44End(d.NewId) }),
            ("take an offset", () => new { cmd = "constraint.set", constraint = bone, takeOffset = true }),
            ("move", () => new { cmd = "constraint.move", constraint = ik, index = 0 }),
            ("remove", () => new { cmd = "constraint.remove", constraint = bone }),
            ("clear", () => new { cmd = "constraint.clear" }),
        };
        foreach (var (what, request) in edits)
        {
            string before = C44PlanText(c);
            var r = c.Ok(request(), "CB8: " + what);
            string id = (string)r["constraint"];
            if (what == "add a node") node = id;
            if (what == "add a bone") bone = id;
            if (what == "add an IK target") ik = id;
            string after = C44PlanText(c);
            c.Check(after != before, "CB8: " + what + ": the plan changes");
            c.Ok(new { cmd = "undo" }, "CB8: " + what + ": undo");
            c.Check(C44PlanText(c) == before, "CB8: " + what + ": undo: the plan as before", C44PlanText(c));
            c.Ok(new { cmd = "redo" }, "CB8: " + what + ": redo");
            c.Check(C44PlanText(c) == after, "CB8: " + what + ": redo: the plan as after", C44PlanText(c));
            if (what == "clear") continue;
            // a save and a load give the same list and the same plan; a load clears the history, so each round works on its own file
            c.Ok(new { cmd = "scene.save", name = "c44_plan", overwrite = true, clock = "2026-11-06T00:00:00Z" }, "CB8: " + what + ": saved");
            string json = s.Model.Constraints?.ToJson();
            c.Ok(new { cmd = "scene.load", name = "c44_plan" }, "CB8: " + what + ": loaded");
            x.App.Runtime.ApplyNow();
            c.Check(s.Model.Constraints?.ToJson() == json, "CB8: " + what + ": the list byte-equal");
            c.Check(C44PlanText(c) == after, "CB8: " + what + ": the plan equal after a load", C44PlanText(c));
        }
        c.Ok(new { cmd = "scene.new" }, "CB8: done");
    }

    // ---------------------------------------------------------------- what a file brings

    // A file may hold what no edit may make: such an entry is listed with its state, and an edit of it is refused only for what the edit brings.
    static void C44File(Ctx c, C44Fix x)
    {
        var s = x.App.Session;
        var a = s.AddFolder();
        var b = s.AddFolder();
        var d = s.AddFolder();
        x.App.Runtime.ApplyNow();
        var file = SceneContainer.Load(x.App.Ops.Scene.ToBytes()).Value;
        ConstraintEntry E(string component, float weight = 1f) => new ConstraintEntry
        {
            Id = s.NewId(), Weight = weight, Source = new ConstraintEnd(a.NewId), Target = new ConstraintEnd(b.NewId), Components = new List<string> { component },
        };
        var bad = E("wobble");
        var loud = E("position", 1.5f);
        file.Scene.Constraints = ConstraintList.Parse(new ConstraintList(new[] { bad, loud }).ToJson());
        s.Bind(file);
        x.App.Runtime.ApplyNow();
        var row = C44Row(c, bad.Id);
        c.Check(row != null && (string)row["state"] == "bad" && ((string)row["reason"]).Contains("wobble"), "CR1: a file's bad entry: state bad, the reason", row?.ToString(Formatting.None));
        c.Ok(new { cmd = "constraint.set", constraint = bad.Id, on = false }, "CR1: a bad entry can be switched");
        c.Ok(new { cmd = "constraint.set", constraint = bad.Id, target = C44End(d.NewId) }, "CR1: ... and retargeted");
        c.Check((string)C44Row(c, bad.Id)["state"] == "bad", "CR1: ... it stays bad");
        c.Fails(new { cmd = "constraint.set", constraint = bad.Id, components = new[] { "wobble" } }, "bad_args", "CR1: an edit cannot name an unknown component");
        var fixedRow = c.Ok(new { cmd = "constraint.set", constraint = bad.Id, components = new[] { "position" }, on = true }, "CR1: a set of components repairs it");
        c.Check((string)fixedRow["state"] == "active", "CR1: ... active", Ctx.Short(c.LastText));
        row = C44Row(c, loud.Id);
        c.Check(row != null && (double)row["weight"] == 1.5 && (string)row["state"] == "active", "CR1: a file's weight over 1: stored as read, active", row?.ToString(Formatting.None));
        c.Ok(new { cmd = "constraint.set", constraint = loud.Id, label = "loud" }, "CR1: an edit of another member leaves it");
        c.Check((double)C44Row(c, loud.Id)["weight"] == 1.5, "CR1: ... the weight as read");
        c.Fails(new { cmd = "constraint.set", constraint = loud.Id, weight = 1.25 }, "bad_args", "CR1: an edit cannot set one");

        // over the limit: kept, inactive; the list can shrink and cannot grow
        var many = new List<ConstraintEntry>();
        for (int i = 0; i < ConstraintLimits.MaxEntries + 4; i++) many.Add(E("position"));
        file = SceneContainer.Load(x.App.Ops.Scene.ToBytes()).Value;
        file.Scene.Constraints = new ConstraintList(many);
        s.Bind(file);
        x.App.Runtime.ApplyNow();
        c.Check((string)C44Row(c, many[ConstraintLimits.MaxEntries].Id)["state"] == "limit" && (string)C44Row(c, many[0].Id)["state"] == "active", "CR1: a file's entry beyond 256: state limit");
        C44Refused(c, new { cmd = "constraint.add", source = C44End(a.NewId), target = C44End(d.NewId), components = new[] { "position" } }, "bad_args", null, "CR2: a list over the limit takes no more");
        c.Ok(new { cmd = "constraint.move", constraint = many[259].Id, index = 0 }, "CR1: a move within it");
        c.Check((string)C44Row(c, many[259].Id)["state"] == "active" && (string)C44Row(c, many[255].Id)["state"] == "limit", "CR1: ... the place decides");
        c.Ok(new { cmd = "constraint.remove", constraint = many[0].Id }, "CR1: it can shrink");
        c.Check(C44Count(c) == ConstraintLimits.MaxEntries + 3, "CR1: ... by one");
        c.Ok(new { cmd = "scene.new" }, "CR1: done");
    }
}
