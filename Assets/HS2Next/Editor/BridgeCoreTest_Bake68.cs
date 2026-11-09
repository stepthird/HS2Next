// C49: the commands of Chara to Item and the Cutter (Phase 6.8 package C) in an edit-mode test host, headless: the clothed
// SFW target BR-Chan, the fallback item, bakes made in code put into the host's own store (Spike/out/bridge/userdata/bakes, emptied before and after) and
// added through item.add, cuts on the CPU. BG1 the names, arguments, defaults, replies and their order; BG3 the errors (a capture without a device answers
// unavailable); BG4 `id` stays the envelope's, a bake is named by `bake` and a store row by `no`; BG5 every command through ctx.Request from a test plugin
// with the bridge off; BG6 the group. A refusal stores nothing and makes no entry.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Api.Bridge;
using HS2Next.App;
using HS2Next.App.Commands;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Bake;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class BridgeCoreTest
{
    const string C49Nope = "0123456789abcdef0123456789abcdef";
    static readonly string[] C49Bake = { "bake.parts", "bake.make", "bake.list", "bake.rename", "bake.delete", "bake.purge", "bake.keep", "bake.status", "bake.import" };
    static readonly string[] C49Cut = { "cut.marker", "cut.make", "cut.set", "cut.status" };
    static readonly string[] C49Reads = { "bake.parts", "bake.list", "bake.status", "cut.status" };
    /// <summary>Commands and experimental ones of the registry with this phase's commands in.</summary>
    const int C49Total = 265, C49Experimental = 107;
    static readonly string[] C49FkGroups = { "Hair", "Neck", "Breast", "Body", "RightHand", "LeftHand", "Skirt" };

    /// <summary>The name and the default of each argument, in order, as the spec's table gives them.</summary>
    static readonly Dictionary<string, (string name, object value)[]> C49Args = new Dictionary<string, (string, object)[]>
    {
        ["bake.parts"] = new (string, object)[] { ("key", null) },
        ["bake.make"] = new (string, object)[] { ("key", null), ("keys", null), ("parts", new string[0]), ("split", false), ("name", ""), ("fk", C49FkGroups), ("insides", true), ("centre", true), ("hide", true), ("store", true) },
        ["bake.list"] = new (string, object)[] { ("q", ""), ("deleted", false) },
        ["bake.rename"] = new (string, object)[] { ("no", null), ("bake", null), ("name", null) },
        ["bake.delete"] = new (string, object)[] { ("no", null), ("bake", null), ("restore", false) },
        ["bake.purge"] = new (string, object)[0],
        ["bake.keep"] = new (string, object)[] { ("key", null) },
        ["bake.status"] = new (string, object)[] { ("key", null) },
        ["bake.import"] = new (string, object)[] { ("path", null) },
        ["cut.marker"] = new (string, object)[] { ("kind", null), ("target", null) },
        ["cut.make"] = new (string, object)[] { ("key", null), ("marker", null), ("shape", null), ("mode", null), ("ragged", null), ("clothesOnly", false), ("section", null), ("hide", true), ("name", "") },
        ["cut.set"] = new (string, object)[] { ("key", null), ("marker", null), ("shape", null), ("mode", null), ("ragged", null), ("clothesOnly", null), ("section", null), ("recut", false) },
        ["cut.status"] = new (string, object)[] { ("key", null) },
    };

    /// <summary>The reply members of each command, in order.</summary>
    static readonly Dictionary<string, string[]> C49Returns = new Dictionary<string, string[]>
    {
        ["bake.parts"] = new[] { "parts" },
        ["bake.make"] = new[] { "items", "bakes", "ms" },
        ["bake.list"] = new[] { "rows", "lastNumber" },
        ["bake.rename"] = new[] { "name", "items" },
        ["bake.delete"] = new[] { "deleted" },
        ["bake.purge"] = new[] { "rows", "blobs", "bytes" },
        ["bake.keep"] = new[] { "kept", "no" },
        ["bake.status"] = new[] { "bake", "main", "data", "bytes", "nodes", "renderers", "meshes", "textures", "materials", "fk", "correction", "members", "parts", "kind", "source", "no", "piece" },
        ["bake.import"] = new[] { "imported", "skipped" },
        ["cut.marker"] = new[] { "key" },
        ["cut.make"] = new[] { "pieces", "ms" },
        ["cut.set"] = new[] { "changed", "ms" },
        ["cut.status"] = new[] { "source", "pose", "steps", "section", "recomputed" },
    };
    static readonly string[] C49PartRow = { "part", "label", "item", "renderers" };
    static readonly string[] C49ListRow = { "no", "bake", "name", "created", "kind", "parts", "fk", "bytes", "deleted" };
    static readonly string[] C49Required = { "bake.parts:key", "bake.rename:name", "bake.keep:key", "bake.status:key", "cut.marker:kind", "cut.make:key", "cut.set:key", "cut.status:key" };

    sealed class C49Fix
    {
        public AppCore App;
        public string Chara, Plain, Item, Scene;
        public int No;
    }

    static void C49(Ctx c)
    {
        BakeCmdKit.ClearStore(c.UserDataDir);
        try
        {
            C49Registration(c);
            var app = c.Host();
            bool registered = BakeCmdKit.RegisterRows(app, c.UserDataDir);
            c.Check(registered, "BG: the app's group on the test host");
            try
            {
                string f = C30Load(c, c.FemaleRel, "female", "BG: BR-Chan");
                string plain = W1AddItem(c, "BG: the fallback item");
                int no = BakeCmdKit.StoreSynthetic(app, "c49 bake", new Color32(200, 60, 60, 255));
                var add = c.Ok(new { cmd = "item.add", group = AppItemRows.Group, category = AppItemRows.BakesCategory, no }, "BG: item.add of the store row");
                string item = (string)add["added"]?[0]?["key"];
                string scene = BakeCmdKit.SceneSynthetic(app, "c49 scene", new Color32(60, 60, 200, 255));
                app.Runtime.ApplyNow();
                var built = BakeCmdKit.Built(app, item);
                if (!c.Check(f != null && plain != null && item != null && scene != null && built?.Source == "bake" && !built.IsFallback, "BG: the fixture (a character, the fallback item, a stored bake's item, a scene's bake item)",
                    built?.Source + " " + built?.IsFallback)) return;
                var x = new C49Fix { App = app, Chara = f, Plain = plain, Item = item, Scene = scene, No = no };
                C49Replies(c, x);
                C49Errors(c, x);
                C49Id(c, x);
            }
            finally { AppItemRows.Remove(app.Db); }
            C49Request(c);
        }
        finally { BakeCmdKit.ClearStore(c.UserDataDir); }
    }

    static List<string> C49Members(JObject r) => C47Members(r);

    /// <summary>A marker put <paramref name="up"/> units above the bake item's origin (the strip of the bake made in code runs from 0 to 6 up).</summary>
    static void C49Place(Ctx c, C49Fix x, string marker, float up)
    {
        var at = x.App.Runtime.HolderOf(x.Item).position + Vector3.up * up;
        c.Ok(new { cmd = "obj.transform", key = marker, pos = new[] { at.x, at.y, at.z }, undo = false }, "(the marker " + up + " above the item)");
    }

    static bool C49Order(Ctx c, JObject r, string cmd, string what)
    {
        var got = C49Members(r);
        return c.Check(got.SequenceEqual(C49Returns[cmd]), what + ": the reply's members in the order of the spec", string.Join(", ", got));
    }

    // ---------------------------------------------------------------- BG1, BG6: registration

    static void C49Registration(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        var names = CoreCommands.Phase68Names.ToList();
        c.Check(names.SequenceEqual(C49Bake.Concat(C49Cut)), "BG1: Phase68Names = the bake and cut commands of the spec, in order", string.Join(", ", names));
        c.Check(names.All(CoreCommands.AllNames.Contains), "BG1: all in CoreCommands.AllNames");
        var groups = CoreCommands.Groups.ToList();
        c.Check(groups.Contains("bake") && groups.Contains("cut") && groups.IndexOf("cut") == groups.IndexOf("bake") + 1 && CkGroups.Contains("bake") && CkGroups.Contains("cut"), "BG6: the groups bake and cut follow each other in CoreCommands.Groups and are convention groups", string.Join(", ", groups));
        c.Check(names.All(n => !CoreCommands.ParityNames.Contains(n)), "BG1: none is a parity name");
        var bad = new List<string>();
        foreach (var n in names)
        {
            var s = reg.Find(n);
            if (s == null) { bad.Add(n + ": not registered"); continue; }
            bool read = C49Reads.Contains(n);
            string group = n.Substring(0, n.IndexOf('.'));
            if (s.Owner != BridgeOwners.Core) bad.Add(n + ": owner " + s.Owner);
            if (s.Group != group) bad.Add(n + ": group " + s.Group);
            if (s.Since != "1.3.0") bad.Add(n + ": since " + s.Since);
            if ((s.Flags & CommandFlags.Experimental) == 0) bad.Add(n + ": not experimental");
            if (((s.Flags & CommandFlags.ReadOnly) != 0) != read) bad.Add(n + ": read flag");
            if (((s.Flags & CommandFlags.Mutates) != 0) == read) bad.Add(n + ": mutates flag");
            if ((s.Flags & CommandFlags.Defers) != 0) bad.Add(n + ": defers");
            if (string.IsNullOrEmpty(s.Summary)) bad.Add(n + ": no summary");
            if (s.Args.Any(a => string.IsNullOrEmpty(a.Doc))) bad.Add(n + ": an argument without a doc");
            if (s.Aliases.Count > 0) bad.Add(n + ": an alias");
            if (s.Name != n) bad.Add(n + ": found as " + s.Name);
        }
        c.Check(bad.Count == 0, "BG1: owner core, groups bake and cut, Since 1.3.0, experimental, read and write flags, a summary, argument docs, no alias", string.Join("; ", bad));
        foreach (var kv in C49Args)
        {
            var got = reg.Find(kv.Key)?.Args.ToList() ?? new List<FieldSpec>();
            c.Check(got.Select(a => a.Name).SequenceEqual(kv.Value.Select(a => a.name)), "BG1: " + kv.Key + ": the arguments of the spec, in order", string.Join(", ", got.Select(a => a.Name)));
            string defaults = JsonConvert.SerializeObject(got.Select(a => a.Default)), want = JsonConvert.SerializeObject(kv.Value.Select(a => a.value));
            c.Check(defaults == want, "BG1: " + kv.Key + ": the defaults of the spec", defaults);
        }
        foreach (var kv in C49Returns)
        {
            var got = reg.Find(kv.Key)?.Returns.Select(a => a.Name).ToList() ?? new List<string>();
            c.Check(got.SequenceEqual(kv.Value), "BG1: " + kv.Key + ": the reply's members of the spec, in order", string.Join(", ", got));
        }
        var required = names.SelectMany(n => reg.Find(n).Args.Where(a => a.Required).Select(a => n + ":" + a.Name)).ToList();
        c.Check(required.SequenceEqual(C49Required), "BG1: the required arguments", string.Join(", ", required));
        var live = reg.Specs.Where(s => s.Owner == BridgeOwners.Core || s.Owner == BridgeOwners.Bridge).ToList();
        c.Check(live.Count == C49Total && live.Count(s => (s.Flags & CommandFlags.Experimental) != 0) == C49Experimental && CoreCommands.ParityNames.Count == 111,
            "BG6: " + C49Total + " commands, " + C49Experimental + " experimental, 111 parity", live.Count + " / " + live.Count(s => (s.Flags & CommandFlags.Experimental) != 0));
        foreach (var n in names)
        {
            var h = c.Call(app, new { cmd = "help", name = n });
            c.Check(h.Value<bool?>("ok") == true && (string)h["command"]?["name"] == n, "BG1: help describes " + n, Ctx.Short(c.LastText));
        }
        var hg = c.Call(app, new { cmd = "help", group = "bake" });
        var listed = (hg["commands"] as JArray)?.OfType<JObject>().Select(o => (string)o["name"]).ToList() ?? new List<string>();
        c.Check(C49Bake.All(listed.Contains), "BG6: help {group: bake} lists the bake commands", string.Join(", ", listed));
        c.Dispose(app);
    }

    // ---------------------------------------------------------------- BG1: replies

    static void C49Replies(Ctx c, C49Fix x)
    {
        var parts = c.Ok(new { cmd = "bake.parts", key = x.Chara }, "BG1: bake.parts of BR-Chan");
        C49Order(c, parts, "bake.parts", "BG1: bake.parts");
        var rows = (parts["parts"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        c.Check(rows.Count >= 1 && rows.All(p => C49Members(p).SequenceEqual(C49PartRow)), "BG1: bake.parts: Present: " + rows.Count + " parts, each {part, label, item, renderers}", Ctx.Short(c.LastText));
        c.Check(rows.Any(p => (string)p["part"] == "body") && rows.Any(p => (string)p["part"] == "head"), "BG1: bake.parts: body and head are there");

        var list = c.Ok(new { cmd = "bake.list" }, "BG1: bake.list");
        C49Order(c, list, "bake.list", "BG1: bake.list");
        var lr = (list["rows"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        c.Check(lr.Count == 1 && C49Members(lr[0]).SequenceEqual(C49ListRow) && (int)lr[0]["no"] == x.No && (int)list["lastNumber"] == x.No && (int)lr[0]["fk"] == 3,
            "BG1: bake.list: Present: the one stored row, {no, bake, name, created, kind, parts, fk, bytes, deleted}", Ctx.Short(c.LastText));
        var q = c.Ok(new { cmd = "bake.list", q = "nothing like it" }, "BG1: bake.list q");
        c.Check((q["rows"] as JArray)?.Count == 0, "BG1: bake.list: q filters the rows");

        var st = c.Ok(new { cmd = "bake.status", key = x.Item }, "BG1: bake.status of the stored bake's item");
        C49Order(c, st, "bake.status", "BG1: bake.status");
        c.Check((int?)st["no"] == x.No && (int?)st["renderers"] == 2 && (int?)st["meshes"] == 2 && (st["fk"] as JArray)?.Count == 3 && (string)st["kind"] == "item" && st["piece"]?.Type == JTokenType.Null &&
            (string)st["main"] == BakeCmdKit.Item(x.App, x.Item).Bake.Main, "BG1: bake.status: the store row, the counts of the bake made in code, no piece", Ctx.Short(c.LastText));

        var keep = c.Ok(new { cmd = "bake.keep", key = x.Scene }, "BG1: bake.keep of the scene's bake");
        C49Order(c, keep, "bake.keep", "BG1: bake.keep");
        int kept = (int?)keep["no"] ?? -1;
        c.Check(keep.Value<bool?>("kept") == true && kept == x.No + 1, "BG1: bake.keep: a new row with the next number", Ctx.Short(c.LastText));
        var again = c.Ok(new { cmd = "bake.keep", key = x.Scene }, "BG1: bake.keep again");
        c.Check(again.Value<bool?>("kept") == false && (int?)again["no"] == kept, "BG1: bake.keep: a bake the store has: nothing new, its row", Ctx.Short(c.LastText));

        var ren = c.Ok(new { cmd = "bake.rename", no = kept, name = "c49 kept" }, "BG1: bake.rename");
        C49Order(c, ren, "bake.rename", "BG1: bake.rename");
        c.Check((string)ren["name"] == "c49 kept" && (ren["items"] as JArray)?.Select(i => (string)i).SequenceEqual(new[] { x.Scene }) == true, "BG1: bake.rename: the item with the old name renamed", Ctx.Short(c.LastText));

        var del = c.Ok(new { cmd = "bake.delete", no = kept }, "BG1: bake.delete");
        C49Order(c, del, "bake.delete", "BG1: bake.delete");
        c.Check(del.Value<bool?>("deleted") == true && (c.Ok(new { cmd = "bake.list" }, "bake.list")["rows"] as JArray)?.Count == 1 &&
            (c.Ok(new { cmd = "bake.list", deleted = true }, "bake.list deleted")["rows"] as JArray)?.Count == 2, "BG1: bake.delete: hidden from the list, listed with deleted");
        var res = c.Ok(new { cmd = "bake.delete", no = kept, restore = true }, "BG1: bake.delete restore");
        c.Check(res.Value<bool?>("deleted") == false && (c.Ok(new { cmd = "bake.list" }, "bake.list")["rows"] as JArray)?.Count == 2, "BG1: bake.delete restore: listed again");
        c.Ok(new { cmd = "bake.delete", no = kept }, "BG1: bake.delete again");
        var store = BakeProvider.StoreOf(x.App.Runtime);
        int only = store.Find(kept).Blobs().Except(store.Find(x.No).Blobs()).Count(), shared = store.Find(kept).Blobs().Intersect(store.Find(x.No).Blobs()).Count();
        var purge = c.Ok(new { cmd = "bake.purge" }, "BG1: bake.purge");
        C49Order(c, purge, "bake.purge", "BG1: bake.purge");
        c.Check((int)purge["rows"] == 1 && (int)purge["blobs"] == only && only >= 1 && shared >= 1,
            "BG1: bake.purge: the deleted row goes with the " + only + " blobs only it listed (Present >= 1); the " + shared + " it shared with the live row stay", Ctx.Short(c.LastText));
        c.Check(store.Find(x.No) != null && store.Find(x.No).Blobs().All(store.HasBlob), "BG1: bake.purge: every blob of the live row is kept");

        var mk = c.Ok(new { cmd = "cut.marker", kind = "plane", target = x.Item }, "BG1: cut.marker");
        C49Order(c, mk, "cut.marker", "BG1: cut.marker");
        string marker = (string)mk["key"];
        C49Place(c, x, marker, 3f);
        var ms = marker != null ? x.App.Session.Model.Find(marker)?.State as ItemNodeState : null;
        c.Check(ms != null && HS2Next.Scenes.Cut.CutMarkers.KindOf(ms) == HS2Next.Scenes.Cut.CutKind.Plane, "BG1: cut.marker: a plane marker", Ctx.Short(c.LastText));
        var cut = c.Ok(new { cmd = "cut.make", key = x.Item, marker, hide = false }, "BG1: cut.make of the bake item");
        C49Order(c, cut, "cut.make", "BG1: cut.make");
        var pieces = (cut["pieces"] as JArray)?.Select(p => (string)p).ToList() ?? new List<string>();
        c.Check(pieces.Count == 2, "BG1: cut.make: both sides: two pieces", Ctx.Short(c.LastText));
        string piece = pieces.FirstOrDefault();
        var cs = c.Ok(new { cmd = "cut.status", key = piece }, "BG1: cut.status");
        C49Order(c, cs, "cut.status", "BG1: cut.status");
        var steps = (cs["steps"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
        c.Check(steps.Count == 1 && (string)steps[0]["shape"] == "plane" && (string)steps[0]["keep"] == "front" && (string)cs["source"]?["bake"] == BakeCmdKit.Item(x.App, x.Item).Bake.Id &&
            cs.Value<bool?>("recomputed") == false, "BG1: cut.status: one plane step, the front, the bake item's bake as source", Ctx.Short(c.LastText));
        var ps = c.Ok(new { cmd = "bake.status", key = piece }, "BG1: bake.status of a piece");
        c.Check((int?)ps["piece"]?["steps"] == 1 && (string)ps["piece"]?["source"] == BakeCmdKit.Item(x.App, x.Item).Bake.Id && (string)ps["kind"] == "piece", "BG1: bake.status: a piece's steps and source", Ctx.Short(c.LastText));
        var set = c.Ok(new { cmd = "cut.set", key = piece, section = new { color = new[] { 0.1f, 0.2f, 0.3f }, ownInside = false } }, "BG1: cut.set");
        C49Order(c, set, "cut.set", "BG1: cut.set");
        c.Check(set.Value<bool?>("changed") == true && BakeCmdKit.Item(x.App, piece).Cut.Section.OwnInside == false, "BG1: cut.set: the section changed", Ctx.Short(c.LastText));
        var shaped = c.Ok(new { cmd = "cut.make", key = piece, shape = new { kind = "box", pos = new[] { 0f, 5f, 0f }, size = new[] { 1f, 1f, 1f } }, mode = "outside", hide = false }, "BG1: cut.make of a piece by a shape");
        var p2 = (shaped["pieces"] as JArray)?.Select(p => (string)p).FirstOrDefault();
        var chain = p2 != null ? BakeCmdKit.Item(x.App, p2)?.Cut : null;
        c.Check(chain != null && chain.Steps.Count == 2 && chain.Source.Id == BakeCmdKit.Item(x.App, x.Item).Bake.Id && chain.Steps[1].Shape == "box" && chain.Steps[1].Keep == "outside",
            "BG1: a cut of a piece stays a procedure on the first source: two steps", chain?.ToJson());
        c.Check(chain != null && Math.Abs(chain.Steps[1].Size.X - 1f) < 1e-4f, "BG1: a shape's size in world units is the step's size", chain?.ToJson());
        var rg = c.Call(new { cmd = "cut.set", key = piece, ragged = new { amp = 0.9f, tooth = 0.3f } });
        c.Check(rg.Value<bool?>("ok") == true && (rg["warnings"] as JArray)?.Any(w => ((string)w).StartsWith(BakeFindings.CutRange, StringComparison.Ordinal)) == true,
            "BG1: a ragged value out of its range is taken with the warning cut.range", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "cut.set", key = piece, ragged = (object)null }, "BG1: ragged null: a clean cut again");
        c.Check(BakeCmdKit.Item(x.App, piece).Cut.Steps[0].Ragged == null, "BG1: ragged null: the step has no ragged edge");
    }

    // ---------------------------------------------------------------- BG3: errors

    static void C49Errors(Ctx c, C49Fix x)
    {
        string marker = (string)c.Ok(new { cmd = "cut.marker", kind = "box", target = x.Item, undo = false }, "BG3: a box marker")["key"];
        C49Place(c, x, marker, 3f);
        string piece = (string)c.Ok(new { cmd = "cut.make", key = x.Item, marker, mode = "inside", hide = false, undo = false }, "BG3: a piece")["pieces"]?[0];
        c.Check(!StudioOps.BakeHasDevice, "BG3: this row runs without a graphics device", SystemInfo.graphicsDeviceType.ToString());
        // bake.make
        C45Refused(c, new { cmd = "bake.make", key = x.Plain }, "bad_args", "key", "BG3: bake.make of an item");
        C45Refused(c, new { cmd = "bake.make", key = C49Nope }, "not_found", "key", "BG3: bake.make of no node");
        C45Refused(c, new { cmd = "bake.make", key = x.Chara, parts = new[] { "wings" } }, "bad_args", "parts", "BG3: bake.make of a part that is none");
        C45Refused(c, new { cmd = "bake.make", key = x.Chara, fk = new[] { "Tail" } }, "bad_args", "fk", "BG3: bake.make with an FK group that is none");
        C45Refused(c, new { cmd = "bake.make", key = x.Chara, keys = new[] { x.Chara } }, "bad_args", null, "BG3: bake.make with key and keys");
        C45Refused(c, new { cmd = "bake.make", key = x.Chara }, "unavailable", "key", "BG3: bake.make without a graphics device");
        c.Check((string)c.Call(new { cmd = "bake.make", key = x.Chara })["detail"]?["state"] == "NoDevice", "BG3: bake.make without a device: state NoDevice", Ctx.Short(c.LastText));
        // bake.parts, keep, status
        C45Refused(c, new { cmd = "bake.parts", key = x.Plain }, "bad_args", "key", "BG3: bake.parts of an item");
        C45Refused(c, new { cmd = "bake.parts", key = C49Nope }, "not_found", "key", "BG3: bake.parts of no node");
        C45Refused(c, new { cmd = "bake.keep", key = x.Plain }, "bad_args", "key", "BG3: bake.keep of a plain item");
        C45Refused(c, new { cmd = "bake.keep", key = x.Chara }, "bad_args", "key", "BG3: bake.keep of a character");
        C45Refused(c, new { cmd = "bake.status", key = x.Plain }, "bad_args", "key", "BG3: bake.status of a plain item");
        C45Refused(c, new { cmd = "bake.status", key = marker }, "bad_args", "key", "BG3: bake.status of a marker");
        // the store
        C45Refused(c, new { cmd = "bake.rename", name = "x" }, "bad_args", "no", "BG3: bake.rename with neither no nor bake");
        C45Refused(c, new { cmd = "bake.rename", no = x.No, bake = C49Nope, name = "x" }, "bad_args", "no", "BG3: bake.rename with no and bake");
        C45Refused(c, new { cmd = "bake.rename", no = 12345, name = "x" }, "not_found", "no", "BG3: bake.rename of a row that is none");
        C45Refused(c, new { cmd = "bake.rename", bake = "zz", name = "x" }, "bad_args", "bake", "BG3: bake.rename of a bake that is no identity");
        C45Refused(c, new { cmd = "bake.rename", no = x.No, name = " " }, "bad_args", "name", "BG3: bake.rename to a blank name");
        C45Refused(c, new { cmd = "bake.rename", no = x.No }, "bad_args", "name", "BG3: bake.rename without a name");
        C45Refused(c, new { cmd = "bake.delete", no = 12345 }, "not_found", "no", "BG3: bake.delete of a row that is none");
        C45Refused(c, new { cmd = "bake.import", path = System.IO.Path.Combine(c.Out, "no such folder") }, "not_found", "path", "BG3: bake.import of a folder that is none");
        C45Refused(c, new { cmd = "bake.import", path = c.Out }, "unavailable", "path", "BG3: bake.import without a graphics device");
        var store = BakeProvider.StoreOf(x.App.Runtime);
        using (store.Hold())
        {
            C45Refused(c, new { cmd = "bake.purge" }, "unavailable", null, "BG3: bake.purge while the store is busy");
            c.Check(((string)c.Call(new { cmd = "bake.purge" })["error"] ?? "").Contains(BakeFindings.Busy), "BG3: bake.purge while busy names bake.busy", Ctx.Short(c.LastText));
        }
        // markers and cuts
        C45Refused(c, new { cmd = "cut.marker", kind = "circle" }, "bad_args", "kind", "BG3: cut.marker of a kind that is none");
        C45Refused(c, new { cmd = "cut.marker", kind = "plane", target = C49Nope }, "not_found", "target", "BG3: cut.marker on no node");
        C45Refused(c, new { cmd = "cut.make", key = marker, marker }, "bad_args", "key", "BG3: cut.make of a marker");
        C45Refused(c, new { cmd = "cut.make", key = x.Item }, "bad_args", "marker", "BG3: cut.make with neither marker nor shape");
        C45Refused(c, new { cmd = "cut.make", key = x.Item, marker, shape = new { kind = "plane" } }, "bad_args", "marker", "BG3: cut.make with marker and shape");
        C45Refused(c, new { cmd = "cut.make", key = x.Item, marker = x.Plain }, "bad_args", "marker", "BG3: cut.make by an item that is no marker");
        C45Refused(c, new { cmd = "cut.make", key = x.Item, marker, mode = "front" }, "bad_args", "mode", "BG3: cut.make with a mode of another shape");
        C45Refused(c, new { cmd = "cut.make", key = x.Item, shape = new { kind = "circle" } }, "bad_args", "shape.kind", "BG3: cut.make by a shape that is none");
        C45Refused(c, new { cmd = "cut.make", key = x.Item, shape = new { pos = new[] { 0, 0, 0 } } }, "bad_args", "shape.kind", "BG3: cut.make by a shape with no kind");
        C45Refused(c, new { cmd = "cut.make", key = x.Item, marker, ragged = new { amp = "much" } }, "bad_args", "ragged.amp", "BG3: cut.make with a ragged value that is no number");
        C45Refused(c, new { cmd = "cut.make", key = x.Item, marker, section = new { color = "pink" } }, "bad_args", "section.color", "BG3: cut.make with a colour that is none");
        C45Refused(c, new { cmd = "cut.make", key = x.Chara, marker }, "unavailable", "key", "BG3: cut.make of a character without a graphics device");
        C45Refused(c, new { cmd = "cut.make", key = x.Plain, marker }, "unavailable", "key", "BG3: cut.make of a plain item without a graphics device");
        C45Refused(c, new { cmd = "cut.set", key = x.Item, recut = true }, "bad_args", "key", "BG3: cut.set of a bake item that is no piece");
        C45Refused(c, new { cmd = "cut.set", key = piece }, "bad_args", "recut", "BG3: cut.set with nothing to set");
        C45Refused(c, new { cmd = "cut.set", key = piece, mode = "front" }, "bad_args", "mode", "BG3: cut.set with a mode of another shape");
        C45Refused(c, new { cmd = "cut.status", key = x.Item }, "bad_args", "key", "BG3: cut.status of a bake item that is no piece");
        // a limit: a piece of 64 steps cut again
        var st = BakeCmdKit.Item(x.App, piece);
        if (c.Check(st?.Cut != null, "BG3: the piece for the limit"))
        {
            var full = st.Cut.Clone();
            while (full.Steps.Count < BakeLimits.MaxSteps) full.Steps.Add(full.Steps[full.Steps.Count - 1]);
            var put = x.App.Session.Commands.Execute(new BakeEditCommand(piece, st.Bake, full), ExecuteOptions.NotRecorded);
            c.Check(put.Ok && BakeCmdKit.Item(x.App, piece).Cut.Steps.Count == BakeLimits.MaxSteps, "BG3: Present: the piece holds " + BakeLimits.MaxSteps + " steps", put.ToString());
            C45Refused(c, new { cmd = "cut.make", key = piece, marker }, "bad_args", "key", "BG3: a cut past the limit of steps");
            c.Check(((string)c.Call(new { cmd = "cut.make", key = piece, marker })["error"] ?? "").Contains(BakeFindings.CutLimit), "BG3: the limit's name in the text", Ctx.Short(c.LastText));
        }
    }

    // ---------------------------------------------------------------- BG4: id

    static void C49Id(Ctx c, C49Fix x)
    {
        string bake = BakeCmdKit.Item(x.App, x.Item).Bake.Id;
        var r = c.Ok(new { cmd = "bake.status", key = x.Item, id = "c49" }, "BG4: bake.status with an envelope id");
        c.Check((string)r["id"] == "c49" && (string)r["bake"] == bake, "BG4: id is echoed; the bake's identity is the member bake", Ctx.Short(c.LastText));
        C45Refused(c, new { cmd = "bake.rename", id = bake, name = "x" }, "bad_args", "no", "BG4: id never names a bake");
        var byBake = c.Ok(new { cmd = "bake.rename", bake, name = "c49 by bake" }, "BG4: bake.rename by bake");
        var store = BakeProvider.StoreOf(x.App.Runtime);
        c.Check(store.Find(x.No)?.Name == "c49 by bake", "BG4: bake names the store row of that identity", store.Find(x.No)?.Name);
        var d1 = c.Ok(new { cmd = "bake.delete", bake }, "BG4: bake.delete by bake");
        var d2 = c.Ok(new { cmd = "bake.delete", no = x.No, restore = true }, "BG4: bake.delete restore by no");
        c.Check(d1.Value<bool?>("deleted") == true && d2.Value<bool?>("deleted") == false && store.Find(x.No)?.Deleted == false, "BG4: no and bake name the same row");
        var list = c.Ok(new { cmd = "bake.list", id = 7 }, "BG4: bake.list with an envelope id");
        var row = (list["rows"] as JArray)?.OfType<JObject>().FirstOrDefault(o => (int)o["no"] == x.No);
        c.Check((int?)list["id"] == 7 && row != null && (string)row["bake"] == bake, "BG4: a row's identity is bake, its number no", Ctx.Short(c.LastText));
    }

    // ---------------------------------------------------------------- BG5: ctx.Request from a plugin, the bridge off

    static void C49Request(Ctx c)
    {
        if (!C37Stage(c)) return;
        C37ClearLog();
        var s = c.HostSettings();
        s.Bridge.Enabled = false;
        var o = c.Services.NewSessionOptions();
        o.Ids = new CountingIdSource(DefaultSeed + 49);
        var app = AppCore.ForTests(c.Services, s, null, o);
        try
        {
            var host = app.LoadPlugins(C37Dir, new[] { C37Disabled, C40PrefixLater });
            var ctx = host.Find(HpGuid)?.Context;
            if (!c.Check(ctx != null && host.Find(HpGuid).IsLoaded && !s.Bridge.Enabled, "BG5: the history probe is loaded, the bridge off", host.Find(HpGuid)?.State + "")) return;
            string plain = (string)JObject.Parse(app.Call("{\"cmd\":\"item.add\",\"no\":399}"))["added"]?[0]?["key"];
            var requests = new (string cmd, object args)[]
            {
                ("bake.parts", new { key = plain }), ("bake.make", new { key = plain }), ("bake.list", null), ("bake.rename", new { no = 1, name = "x" }),
                ("bake.delete", new { no = 1 }), ("bake.purge", null), ("bake.keep", new { key = plain }), ("bake.status", new { key = plain }),
                ("bake.import", new { path = System.IO.Path.Combine(c.Out, "no such folder") }), ("cut.marker", new { kind = "circle" }), ("cut.make", new { key = plain }), ("cut.set", new { key = plain, recut = true }), ("cut.status", new { key = plain }),
            };
            var diff = new List<string>();
            var reached = new List<string>();
            foreach (var (cmd, args) in requests)
            {
                string viaRequest = ctx.Request(cmd, args).Json;
                var env = args == null ? new JObject() : JObject.FromObject(args);
                env.AddFirst(new JProperty("cmd", cmd));
                string viaCall = ctx.Call(env.ToString(Formatting.None));
                if (viaRequest != viaCall) diff.Add(cmd + ": " + Ctx.Short(viaRequest) + " | " + Ctx.Short(viaCall));
                var j = JObject.Parse(viaRequest);
                if ((string)j["code"] != BridgeCodes.UnknownCommand && (j.Value<bool?>("ok") == true || (string)j["code"] == BridgeCodes.BadArgs || (string)j["code"] == BridgeCodes.NotFound)) reached.Add(cmd);
            }
            c.Check(diff.Count == 0, "BG5: Request = Call (the same reply text) for every command", string.Join(" || ", diff.Take(3)));
            c.Check(reached.Count == requests.Length, "BG5: every command reached its handler through ctx.Request", string.Join(", ", requests.Select(r => r.cmd).Except(reached)));
            var mk = JObject.Parse(ctx.Request("cut.marker", new { kind = "hole" }).Json);
            string marker = (string)mk["key"];
            c.Check(mk.Value<bool?>("ok") == true && marker != null && app.Session.Model.Find(marker) != null, "BG5: cut.marker through ctx.Request adds the marker", Ctx.Short(mk.ToString(Formatting.None)));
            var hist = JObject.Parse(ctx.Request("history", null).Json);
            c.Check((string)(hist["undo"] as JArray)?.FirstOrDefault() == StudioOps.Labels.AddItem, "BG5: one entry «Add item»", Ctx.Short(hist.ToString(Formatting.None)));
        }
        finally { app.Dispose(); }
    }
}
