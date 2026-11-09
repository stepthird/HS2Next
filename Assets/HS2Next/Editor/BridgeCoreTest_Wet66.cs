// C45: the commands of stains (Phase 6.6 package C) in an edit-mode test host on the clothed SFW female target (BR-Chan), an item and a folder. This row
// has no graphics device, so a bake answers unsupported here and a stain is put on the node by hand, as a block with no maps: what a bake adds is WetTest's (Render_Commands).
// WR1 every command's name, arguments, defaults, replies, Since and flags, the parity names, the key rule, wet.status; WR2 wet.set by the names scripts use and by paths, its refusals,
// values that wait for the first stain; WR3 the forms of from and target, a miss; WR4 ground.probe's shape; WE each edit one history entry with its label, undo and redo, `undo: false`,
// a batch, a timed command, the clock against an undo, limits, duplicate and import.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Api.Bridge;
using HS2Next.App;
using HS2Next.App.Commands;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Wet;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class BridgeCoreTest
{
    const string C45Nope = "0123456789abcdef0123456789abcdef";
    static readonly string[] C45Reads = { "wet.status", "ground.probe" };
    static readonly string[] C45Parity = { "wet.attach", "wet.stain", "wet.status", "wet.use", "ground.probe" };
    static readonly string[] C45RowMembers = { "key", "current", "t", "running", "radius", "targets", "unreadable", "tiled", "noFilm", "puddles", "floor", "floorSource", "maps" };

    /// <summary>What the checks work on.</summary>
    sealed class C45Fix
    {
        public AppCore App;
        public string Chara, Item, Folder;
    }

    static void C45(Ctx c)
    {
        C45Registration(c);
        var app = c.Host();
        string f = W1LoadFemale(c);
        string item = W1AddItem(c);
        var folder = app.Session.AddFolder();
        app.Runtime.ApplyNow();
        if (!c.Check(f != null && item != null && folder.Ok, "WR1: the fixture")) return;
        var x = new C45Fix { App = app, Chara = f, Item = item, Folder = folder.NewId };
        C45Key(c, x);
        C45Status(c, x);
        C45NoBake(c, x);
        C45Use(c, x);
        C45Edits(c, x);
        C45Kept(c, x);
        C45Set(c, x);
        C45Waiting(c, x);
        C45Floor(c, x);
        C45Rays(c, x);
        C45Probe(c, x);
        C45Limits(c, x);
        C45Clock(c, x);
        C45Batch(c, x);
        C45Timed(c, x);
        C45Duplicate(c, x);
        C45Import(c, x);
    }

    // ---------------------------------------------------------------- helpers

    static CharaNodeState C45St(C45Fix x) => (CharaNodeState)x.App.Session.Model.Find(x.Chara).State;

    // Replaces the block with no history entry; the runtime takes it at once.
    static void C45Block(C45Fix x, CharaWet b)
    {
        x.App.Session.Commands.Execute(new WetEditCommand(x.Chara, b), new ExecuteOptions { Record = HistoryMode.None });
        x.App.Runtime.ApplyNow();
    }

    // Two stains, the second current, with a puddle each.
    static void C45Two(C45Fix x)
    {
        var b = new CharaWet();
        var w = WetEdits.NewStain(b, "water", WetPreset.Water);
        WetEdits.Add(b, w);
        var blood = WetEdits.NewStain(b, "blood", WetPreset.Blood);
        WetEdits.Add(b, blood, true);
        b.Puddles.Add(new WetPuddleEntry { Stain = w.Id, Center = new Vec3(1f, 0f, 0f), Area = 1f, Seed = 1f });
        b.Puddles.Add(new WetPuddleEntry { Stain = blood.Id, Center = new Vec3(2f, 0f, 0f), Area = 2f, Seed = 2f });
        C45Block(x, b);
    }

    static void C45None(C45Fix x) => C45Block(x, new CharaWet());

    static bool C45Err(Ctx c, object req, string code, string arg, string what)
    {
        var r = c.Call(req);
        return c.Check(r.Value<bool?>("ok") == false && r.Value<string>("code") == code && (arg == null || (string)r["detail"]?["arg"] == arg),
            what + " -> " + code + (arg != null ? " naming " + arg : ""), Ctx.Short(c.LastText));
    }

    // a refusal: its code, nothing stored, no history entry
    static void C45Refused(Ctx c, object req, string code, string arg, string what)
    {
        string sha = W1DocSha(c);
        int undo = W1UndoCount(c);
        C45Err(c, req, code, arg, what);
        c.Check(W1DocSha(c) == sha && W1UndoCount(c) == undo, what + ": nothing stored, no entry");
    }

    static JObject C45Row(Ctx c, string key) => c.Ok(new { cmd = "wet.status", key }, "wet.status")["characters"]?[0] as JObject;

    // ---------------------------------------------------------------- WR1: registration

    static void C45Registration(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        var names = CoreCommands.Phase66Names.ToList();
        var want = new[] { "wet.attach", "wet.stain", "wet.status", "wet.use", "wet.start", "wet.dry", "wet.set", "wet.keep", "wet.remove", "wet.floor", "ground.probe" };
        c.Check(names.SequenceEqual(want), "WR1: Phase66Names = the 11 commands", string.Join(", ", names));
        c.Check(names.All(CoreCommands.AllNames.Contains), "WR1: all in CoreCommands.AllNames");
        c.Check(CoreCommands.Groups.Contains("wet") && CkGroups.Contains("wet"), "WR1: the group is in CoreCommands.Groups and a convention group", string.Join(", ", CoreCommands.Groups));
        c.Check(C45Parity.All(CoreCommands.ParityNames.Contains) && C45Parity.SequenceEqual(WetCommands.ParityNames), "WR1: the five parity names");
        c.Check(names.Except(C45Parity).All(n => !CoreCommands.ParityNames.Contains(n)), "WR1: the other six are not");
        var bad = new List<string>();
        foreach (var n in names)
        {
            var s = reg.Find(n);
            if (s == null) { bad.Add(n + ": not registered"); continue; }
            bool read = C45Reads.Contains(n);
            if (s.Owner != BridgeOwners.Core) bad.Add(n + ": owner " + s.Owner);
            if (s.Group != "wet") bad.Add(n + ": group " + s.Group);
            if (s.Since != "1.3.0") bad.Add(n + ": since " + s.Since);
            if ((s.Flags & CommandFlags.Experimental) == 0) bad.Add(n + ": not experimental");
            if (((s.Flags & CommandFlags.ReadOnly) != 0) != read) bad.Add(n + ": read flag");
            if (((s.Flags & CommandFlags.Mutates) != 0) == read) bad.Add(n + ": mutates flag");
            if ((s.Flags & CommandFlags.Defers) != 0) bad.Add(n + ": defers");
            if (string.IsNullOrEmpty(s.Summary)) bad.Add(n + ": no summary");
        }
        c.Check(bad.Count == 0, "WR1: owner, group, Since 1.3.0, experimental, read and write flags, a summary", string.Join("; ", bad));
        var args = new Dictionary<string, string[]>
        {
            ["wet.attach"] = new[] { "key", "from", "target", "preset" },
            ["wet.stain"] = new[] { "key", "name", "preset", "from", "target", "radius", "spread", "tail", "skinDrips" },
            ["wet.status"] = new[] { "key" },
            ["wet.use"] = new[] { "key", "name", "start" },
            ["wet.start"] = new[] { "key", "name", "loop" },
            ["wet.dry"] = new[] { "key", "name", "puddles" },
            ["wet.set"] = new[] { "key", "name", "values" },
            ["wet.keep"] = new[] { "key" },
            ["wet.remove"] = new[] { "key", "name", "all" },
            ["wet.floor"] = new[] { "key", "mode", "offset", "height" },
            ["ground.probe"] = new[] { "points", "y", "above", "below" },
        };
        foreach (var kv in args)
        {
            var got = reg.Find(kv.Key)?.Args.Select(a => a.Name).ToList() ?? new List<string>();
            c.Check(got.SequenceEqual(kv.Value), "WR1: " + kv.Key + ": the arguments of the spec", string.Join(", ", got));
        }
        var returns = new Dictionary<string, string[]>
        {
            ["wet.attach"] = new[] { "status", "floor", "stain" },
            ["wet.stain"] = new[] { "status" },
            ["wet.status"] = new[] { "stains", "characters" },
            ["wet.use"] = new[] { "status" },
            ["wet.start"] = new[] { "t" },
            ["wet.dry"] = new string[0],
            ["wet.set"] = new[] { "changed" },
            ["wet.keep"] = new[] { "stain" },
            ["wet.remove"] = new[] { "removed" },
            ["wet.floor"] = new[] { "y", "source" },
            ["ground.probe"] = new[] { "hits", "source" },
        };
        foreach (var kv in returns)
        {
            var got = reg.Find(kv.Key)?.Returns.Select(a => a.Name).ToList() ?? new List<string>();
            c.Check(got.SequenceEqual(kv.Value), "WR1: " + kv.Key + ": the replies of the spec", string.Join(", ", got));
        }
        var stain = reg.Find("wet.stain");
        object Default(string a) => stain.Args.First(x => x.Name == a).Default;
        c.Check(Equals(Default("name"), "stain") && Equals(Default("preset"), "water") && Equals(Default("radius"), 1.8f) && Equals(Default("spread"), 3f) && Equals(Default("tail"), 5f) && Equals(Default("skinDrips"), false),
            "WR1: wet.stain keeps the defaults of the scripts", string.Join(", ", stain.Args.Select(a => a.Name + "=" + a.Default)));
        var use = reg.Find("wet.use");
        c.Check(use.Args.First(a => a.Name == "name").Required && Equals(use.Args.First(a => a.Name == "start").Default, true), "WR1: wet.use: name is required, start true");
        var probe = reg.Find("ground.probe");
        c.Check(probe.Args.First(a => a.Name == "points").Required && Equals(probe.Args.First(a => a.Name == "y").Default, 0f) && Equals(probe.Args.First(a => a.Name == "above").Default, 6f) &&
            Equals(probe.Args.First(a => a.Name == "below").Default, 30f), "WR1: ground.probe: points required, y 0, above 6, below 30");
        var live = reg.Specs.Where(s => s.Owner == BridgeOwners.Core || s.Owner == BridgeOwners.Bridge).ToList();
        c.Check(live.Count == 265 && live.Count(s => (s.Flags & CommandFlags.Experimental) != 0) == 107, "WR1: 265 commands, 107 experimental", live.Count + "");
        c.Check(names.All(n => reg.Find(n).Name == n && reg.Find(n).Aliases.Count == 0), "WR1: no alias");
        foreach (var n in names)
        {
            var h = c.Call(app, new { cmd = "help", name = n });
            c.Check(h.Value<bool?>("ok") == true && (string)h["command"]?["name"] == n, "WR1: help describes " + n, Ctx.Short(c.LastText));
        }
        c.Dispose(app);
        // the host above is gone: the next c.Host() is the one the checks use
    }

    // ---------------------------------------------------------------- WR1: the key

    static void C45Key(Ctx c, C45Fix x)
    {
        C45Two(x);
        x.App.Session.Selection.Set(new string[0]);
        var ops = x.App.Ops;
        c.Check(ops.WetDefaultCharacter() == null, "WR1: no attach and no selection: no character");
        foreach (var cmd in new[] { "wet.attach", "wet.stain", "wet.start", "wet.dry", "wet.keep", "wet.remove", "wet.floor" })
            C45Refused(c, new { cmd }, "not_found", "key", "WR1: " + cmd + " with no key and no character");
        C45Refused(c, new { cmd = "wet.use", name = "water" }, "not_found", "key", "WR1: wet.use with no key and no character");
        C45Refused(c, new { cmd = "wet.set", values = new { maxRadius = 3 } }, "not_found", "key", "WR1: wet.set with no key and no character");

        x.App.Session.Selection.Set(new[] { x.Folder, x.Item, x.Chara });
        c.Check(ops.WetDefaultCharacter() == x.Chara, "WR1: the selection's first character, past what is not one");
        c.Ok(new { cmd = "wet.use", name = "water", start = false, undo = false }, "WR1: a command with no key takes it");
        c.Check(C45St(x).Wet.CurrentStain.Name == "water", "WR1: on that character");
        x.App.Session.Selection.Set(new string[0]);

        foreach (var cmd in new[] { "wet.attach", "wet.stain", "wet.status", "wet.start", "wet.dry", "wet.keep", "wet.remove", "wet.floor" })
        {
            C45Refused(c, new { cmd, key = x.Item }, "wrong_kind", "key", "WR1: " + cmd + " on an item");
            C45Refused(c, new { cmd, key = C45Nope }, "not_found", "key", "WR1: " + cmd + " on no node");
        }
        C45Refused(c, new { cmd = "wet.status", key = 3 }, "bad_args", "key", "WR1: a number is no key");
        C45None(x);
    }

    // ---------------------------------------------------------------- WR1: wet.status

    static void C45Status(Ctx c, C45Fix x)
    {
        var none = c.Ok(new { cmd = "wet.status" }, "WR1: wet.status of a scene with no stain, with no key and no character");
        c.Check(none["stains"] is JArray a && a.Count == 0 && none["characters"] is JArray b && b.Count == 0, "WR1: stains and characters are there, empty", Ctx.Short(c.LastText));
        var dry = C45Row(c, x.Chara);
        c.Check(dry != null && C45RowMembers.All(m => dry.Property(m) != null) && dry.Properties().Count() == C45RowMembers.Length, "WR1: a row has the spec's members", dry?.ToString(Formatting.None));
        c.Check((string)dry["key"] == x.Chara && dry["current"].Type == JTokenType.Null && (double)dry["t"] == 0 && !(bool)dry["running"] && (int)dry["puddles"] == 0 && (int)dry["maps"]["live"] == 0 && (int)dry["maps"]["dormant"] == 0,
            "WR1: a character with no stain", dry.ToString(Formatting.None));
        c.Check(((JObject)dry["maps"]).Properties().Select(p => p.Name).SequenceEqual(new[] { "live", "dormant" }), "WR1: maps says live and dormant, and nothing else");

        C45Two(x);
        var r = c.Ok(new { cmd = "wet.status" }, "WR1: wet.status with no key");
        c.Check((r["stains"] as JArray).Select(s => (string)s).SequenceEqual(new[] { "water", "blood" }), "WR1: stains holds the names, in order", Ctx.Short(c.LastText));
        var rows = r["characters"] as JArray;
        c.Check(rows.Count == 1 && (string)rows[0]["key"] == x.Chara, "WR1: a row per character that has a stain", Ctx.Short(c.LastText));
        var row = (JObject)rows[0];
        c.Check((string)row["current"] == "blood" && (int)row["puddles"] == 2 && (double)row["radius"] == 0, "WR1: current by name, the puddles, a dry stain's radius", row.ToString(Formatting.None));
        c.Check(new[] { "map", "feet", "fixed", "none" }.Contains((string)row["floorSource"]), "WR1: floorSource is a source", row.ToString(Formatting.None));
        var byKey = c.Ok(new { cmd = "wet.status", key = x.Chara }, "WR1: wet.status with a key");
        c.Check((byKey["characters"] as JArray).Count == 1 && (byKey["stains"] as JArray).Count == 2, "WR1: that character", Ctx.Short(c.LastText));
        var another = x.App.Session.AddChara(null);
        x.App.Runtime.ApplyNow();
        var empty = c.Ok(new { cmd = "wet.status", key = another.NewId }, "WR1: a character that is not built");
        c.Check((empty["stains"] as JArray).Count == 0 && (empty["characters"] as JArray).Count == 1, "WR1: it has a row and no stains", Ctx.Short(c.LastText));
        c.Check((c.Ok(new { cmd = "wet.status" }, "WR1: again with no key")["characters"] as JArray).Count == 1, "WR1: a character with no stain is not listed");
        c.Ok(new { cmd = "delete", keys = new[] { another.NewId }, undo = false }, "(the second character goes)");
        C45None(x);
    }

    // ---------------------------------------------------------------- with no device a bake is refused

    static void C45NoBake(Ctx c, C45Fix x)
    {
        c.Check(!x.App.Ops.Wet.CanBake, "no device in this row");
        C45Refused(c, new { cmd = "wet.attach", key = x.Chara }, "unsupported", null, "WR1: wet.attach needs a bake");
        C45Refused(c, new { cmd = "wet.stain", key = x.Chara, name = "n" }, "unsupported", null, "WR1: wet.stain needs a bake");
        C45Refused(c, new { cmd = "wet.attach", key = x.Chara, preset = "wine" }, "bad_args", "preset", "WR1: wet.attach: an unknown preset");
        C45Refused(c, new { cmd = "wet.stain", key = x.Chara, preset = "wine" }, "bad_args", "preset", "WR1: wet.stain: an unknown preset");
        C45Refused(c, new { cmd = "wet.stain", key = x.Chara, name = "" }, "bad_args", "name", "WR1: wet.stain: an empty name");
        C45Refused(c, new { cmd = "wet.stain", key = x.Chara, preset = 3 }, "bad_args", "preset", "WR1: wet.stain: a preset is a text");
        C45Refused(c, new { cmd = "wet.stain", key = x.Chara, radius = "far" }, "bad_args", "radius", "WR1: wet.stain: a radius is a number");
        C45Refused(c, new { cmd = "wet.stain", key = x.Chara, skinDrips = 1 }, "bad_args", "skinDrips", "WR1: wet.stain: skinDrips is true or false");
        var none = x.App.Session.AddChara(null);
        x.App.Runtime.ApplyNow();
        C45Refused(c, new { cmd = "wet.attach", key = none.NewId }, "unavailable", "key", "WR1: a character that is not built");
        c.Ok(new { cmd = "delete", keys = new[] { none.NewId }, undo = false }, "(that character goes)");
    }

    // ---------------------------------------------------------------- WR1: wet.use

    static void C45Use(Ctx c, C45Fix x)
    {
        C45Two(x);
        c.Ok(new { cmd = "scene.pause", paused = true }, "(the pause: a running stain's clock stands, so a document can be compared)");
        C45Refused(c, new { cmd = "wet.use", key = x.Chara }, "bad_args", "name", "WR1: wet.use needs a name");
        C45Refused(c, new { cmd = "wet.use", key = x.Chara, name = "nothing" }, "not_found", "name", "WR1: wet.use: a name no stain has");
        C45Refused(c, new { cmd = "wet.use", key = x.Chara, name = 3 }, "bad_args", "name", "WR1: wet.use: a number is no name");
        var r = W1History1(c, new { cmd = "wet.use", key = x.Chara, name = "water" }, "Start stain", "WR1: wet.use");
        c.Check((string)r["status"] == "ok" && r.Properties().Any(p => p.Name == "status"), "WR1: the reply has status", Ctx.Short(c.LastText));
        var st = C45St(x).Wet;
        c.Check(st.CurrentStain.Name == "water" && st.CurrentStain.Running && st.CurrentStain.T == 0f, "WR1: start defaults to true: it runs from 0");
        c.Check(st.Stains.Count == 2 && st.Puddles.Count == 2, "WR1: what is there stays");
        W1History1(c, new { cmd = "wet.use", key = x.Chara, name = "blood", start = false }, "Stain", "WR1: wet.use start false");
        st = C45St(x).Wet;
        c.Check(st.CurrentStain.Name == "blood" && !st.CurrentStain.Running, "WR1: it becomes current and does not run");
        c.Check(st.Find(st.Stains[0].Id).Running, "WR1: the other stain goes on");
        c.Ok(new { cmd = "scene.pause", paused = false }, "(the pause ends)");
        C45None(x);
    }

    // ---------------------------------------------------------------- WE: each edit an entry

    static void C45Edits(Ctx c, C45Fix x)
    {
        C45Two(x);
        c.Ok(new { cmd = "scene.pause", paused = true }, "(the pause: a running stain's clock stands, so a document can be compared)");
        var t = W1History1(c, new { cmd = "wet.start", key = x.Chara }, "Start stain", "WE5: wet.start", () => new { cmd = "wet.start", key = x.Chara, undo = false });
        c.Check((double)t["t"] == 0, "WE5: t is 0", Ctx.Short(c.LastText));
        c.Check(C45St(x).Wet.CurrentStain.Running, "WE5: the current stain runs");
        C45Refused(c, new { cmd = "wet.start", key = x.Chara, name = "nothing" }, "not_found", "name", "WE5: a name no stain has");
        C45Refused(c, new { cmd = "wet.start", key = x.Chara, loop = 1 }, "bad_args", "loop", "WE5: loop is true or false");
        W1History1(c, new { cmd = "wet.start", key = x.Chara, name = "water", loop = true }, "Start stain", "WE5: wet.start by name with a loop");
        var st = C45St(x).Wet;
        c.Check(st.Stains[0].Running && st.Stains[0].Loop && st.CurrentStain.Name == "blood", "WE5: the named stain runs and loops; it does not become current");

        var d = W1History1(c, new { cmd = "wet.dry", key = x.Chara, name = "water", puddles = false }, "Dry stain", "WE6: wet.dry of one stain");
        st = C45St(x).Wet;
        c.Check(!st.Stains[0].Running && st.Stains[1].Running && st.Puddles.Count == 2, "WE6: that stain alone, its puddles stay", Ctx.Short(c.LastText));
        c.Check(!d.Properties().Any(p => p.Name != "ok" && p.Name != "cmd" && p.Name != "id" && p.Name != "warnings" && p.Name != "ms"), "WE6: the reply has no member of its own", Ctx.Short(c.LastText));
        W1History1(c, new { cmd = "wet.dry", key = x.Chara }, "Dry stain", "WE6: wet.dry of all");
        st = C45St(x).Wet;
        c.Check(st.Stains.All(s => !s.Running && s.T == 0f) && st.Puddles.Count == 0, "WE6: every stain, and puddles default to true");
        C45Refused(c, new { cmd = "wet.dry", key = x.Chara, name = "nothing" }, "not_found", "name", "WE6: a name no stain has");
        C45Refused(c, new { cmd = "wet.dry", key = x.Chara, puddles = "yes" }, "bad_args", "puddles", "WE6: puddles is true or false");
        int undo = W1UndoCount(c);
        c.Ok(new { cmd = "wet.dry", key = x.Chara }, "WE6: dry again");
        c.Check(W1UndoCount(c) == undo, "WE6: a dry stain dried is no entry");

        C45Two(x);
        var k = W1History1(c, new { cmd = "wet.keep", key = x.Chara }, "Keep layer", "WE4: wet.keep");
        st = C45St(x).Wet;
        c.Check((string)k["stain"] == "blood 2" && st.CurrentStain.Name == "blood 2" && st.Stains.Count == 3 && st.Stains[1].Kept, "WE4: a new current stain", Ctx.Short(c.LastText));

        var rm = W1History1(c, new { cmd = "wet.remove", key = x.Chara, name = "water" }, "Remove stain", "WE2: wet.remove by name");
        c.Check((int)rm["removed"] == 1 && C45St(x).Wet.Stains.Count == 2, "WE2: one removed", Ctx.Short(c.LastText));
        c.Check(C45St(x).Wet.Puddles.All(p => p.Stain != "s1"), "WE2: its puddles with it");
        rm = W1History1(c, new { cmd = "wet.remove", key = x.Chara }, "Remove stain", "WE2: wet.remove of the current stain");
        c.Check((int)rm["removed"] == 1 && C45St(x).Wet.CurrentStain == null, "WE2: the current stain goes; only a kept stain is left", Ctx.Short(c.LastText));
        C45Refused(c, new { cmd = "wet.remove", key = x.Chara }, "not_found", "name", "WE2: no current stain");
        C45Refused(c, new { cmd = "wet.remove", key = x.Chara, name = "nothing" }, "not_found", "name", "WE2: a name no stain has");
        C45Refused(c, new { cmd = "wet.remove", key = x.Chara, name = "blood", all = true }, "bad_args", "all", "WE2: a name and all");
        rm = W1History1(c, new { cmd = "wet.remove", key = x.Chara, all = true }, "Remove stain", "WE2: wet.remove all");
        c.Check((int)rm["removed"] == 1 && C45St(x).Wet.IsEmpty, "WE2: the block is empty", Ctx.Short(c.LastText));
        undo = W1UndoCount(c);
        var nothing = c.Ok(new { cmd = "wet.remove", key = x.Chara, all = true }, "WE2: all of nothing");
        c.Check((int)nothing["removed"] == 0 && W1UndoCount(c) == undo, "WE2: no stain removed, no entry");
        c.Ok(new { cmd = "scene.pause", paused = false }, "(the pause ends)");
    }

    // ---------------------------------------------------------------- a kept stain

    static void C45Kept(Ctx c, C45Fix x)
    {
        C45Two(x);
        c.Ok(new { cmd = "wet.keep", key = x.Chara, undo = false }, "WE4: keep");
        string kept = "blood";
        C45Refused(c, new { cmd = "wet.start", key = x.Chara, name = kept }, "bad_args", "name", "WE4: a kept stain does not start");
        C45Refused(c, new { cmd = "wet.dry", key = x.Chara, name = kept }, "bad_args", "name", "WE4: nor dry");
        C45Refused(c, new { cmd = "wet.use", key = x.Chara, name = kept }, "bad_args", "name", "WE4: nor run through wet.use");
        C45Refused(c, new { cmd = "wet.set", key = x.Chara, name = kept, values = new Dictionary<string, object> { ["look.amount"] = 0.1 } }, "bad_args", "name", "WE4: nor take a value");
        C45Refused(c, new { cmd = "wet.stain", key = x.Chara, name = kept, preset = "water" }, "bad_args", "name", "WE4: nor be replaced");
        c.Ok(new { cmd = "wet.use", key = x.Chara, name = kept, start = false, undo = false }, "WE4: it can be made current");
        C45Refused(c, new { cmd = "wet.keep", key = x.Chara }, "bad_args", "name", "WE4: a kept stain is not kept twice");
        C45Refused(c, new { cmd = "wet.start", key = x.Chara }, "bad_args", "name", "WE4: as the current stain it does not start");
        c.Ok(new { cmd = "wet.dry", key = x.Chara, undo = false }, "WE4: dry of all");
        var st = C45St(x).Wet;
        c.Check(st.Stains[1].Kept && st.Puddles.Count == 1 && st.Puddles[0].Color != null, "WE4: its frozen puddle stays");
        c.Ok(new { cmd = "wet.remove", key = x.Chara, name = kept, undo = false }, "WE4: a kept stain can go");
        c.Check(C45St(x).Wet.Puddles.Count == 0, "WE4: with its puddles");
        C45None(x);
    }

    // ---------------------------------------------------------------- WR2

    static void C45Set(Ctx c, C45Fix x)
    {
        C45Two(x);
        object Set(params (string name, object value)[] v) => new { cmd = "wet.set", key = x.Chara, values = v.ToDictionary(p => p.name, p => p.value) };
        var r = W1History1(c, Set(("maxRadius", 3), ("spreadTime", 4.5), ("dripTail", 6), ("dripRate", 7), ("puddleGrowth", 2), ("puddleScale", 1.25)), "Stain", "WR2: the names scripts use, one entry");
        var s = C45St(x).Wet.CurrentStain;
        c.Check(s.Shape.Radius == 3f && s.Shape.Spread == 4.5f && s.Drips.Tail == 6f && s.Drips.Rate == 7f && s.Drips.Growth == 2f && s.Puddle.Scale == 1.25f, "WR2: each reaches its member");
        c.Check((r["changed"] as JArray).Select(n => (string)n).SequenceEqual(new[] { "maxRadius", "spreadTime", "dripTail", "dripRate", "puddleGrowth", "puddleScale" }), "WR2: changed lists them as given", Ctx.Short(c.LastText));
        int undo = W1UndoCount(c);
        r = c.Ok(Set(("maxRadius", 3)), "WR2: the same value");
        c.Check((r["changed"] as JArray).Count == 0 && W1UndoCount(c) == undo, "WR2: nothing changed, no entry", Ctx.Short(c.LastText));

        r = W1History1(c, Set(("look.amount", 0.5), ("look.tint", new[] { 1, 0.5, 0.25 }), ("drips.color", new[] { 0.1, 0.2, 0.3, 0.4 }), ("puddle.edge", 0.1), ("hold", 3), ("loop", true)), "Stain", "WR2: paths of a stain");
        s = C45St(x).Wet.CurrentStain;
        c.Check(s.Look.Amount == 0.5f && s.Look.Tint.Equals(new Vec3(1f, 0.5f, 0.25f)) && s.Drips.Color.Equals(new Color4(0.1f, 0.2f, 0.3f, 0.4f)) && s.Puddle.Edge == 0.1f && s.Hold == 3f && s.Loop, "WR2: each reaches its member");
        W1History1(c, Set(("film.gloss", 0.5), ("mirror.scale", 0.25), ("targets.hair", false)), "Stain", "WR2: paths of the block");
        var b = C45St(x).Wet;
        c.Check(b.Film.Gloss == 0.5f && b.Mirror.Scale == 0.25f && !b.Targets.Hair, "WR2: each reaches its member");
        W1History1(c, new { cmd = "wet.set", key = x.Chara, name = "water", values = new Dictionary<string, object> { ["look.amount"] = 0.125 } }, "Stain", "WR2: another stain by name");
        c.Check(C45St(x).Wet.FindByName("water").Look.Amount == 0.125f && C45St(x).Wet.CurrentStain.Look.Amount == 0.5f, "WR2: that stain alone");
        W1History1(c, Set(("preset", "water")), "Stain", "WR2: a preset");
        c.Check(C45St(x).Wet.CurrentStain.Preset == "water" && C45St(x).Wet.CurrentStain.Look.Amount == 0.3f, "WR2: writes its values");
        W1History1(c, Set(("name", "renamed")), "Stain", "WR2: a name");
        c.Check(C45St(x).Wet.CurrentStain.Name == "renamed", "WR2: stands");
        W1History1(c, Set(("origin.down", 0.25)), "Move origin", "WR2: a path of the origin is «Move origin»");
        W1History1(c, Set(("shape.radius", 500)), "Stain", "WR2: a value outside its range");
        c.Check(C45St(x).Wet.CurrentStain.Shape.Radius == 500f, "WR2: is stored as given");

        bool marker = x.App.Ops.WetShowMarker;
        undo = W1UndoCount(c);
        string sha = W1DocSha(c);
        r = c.Ok(Set(("showMarker", !marker)), "WR2: showMarker");
        c.Check(x.App.Ops.WetShowMarker == !marker && W1UndoCount(c) == undo && W1DocSha(c) == sha, "WR2: a view switch: no entry, no document");
        c.Check((r["changed"] as JArray).Select(n => (string)n).SequenceEqual(new[] { "showMarker" }), "WR2: it is listed", Ctx.Short(c.LastText));
        c.Ok(Set(("showMarker", marker)), "WR2: back");

        C45Refused(c, Set(("look.nothing", 1)), "bad_args", "values.look.nothing", "WR2: an unknown path");
        foreach (string own in new[] { "t", "running", "kept", "id", "maps", "current", "puddles", "stains" })
            C45Refused(c, Set((own, 1)), "bad_args", "values." + own, "WR2: " + own + " has its own command");
        C45Refused(c, Set(("maxRadius", "far")), "bad_args", "values.maxRadius", "WR2: a text for a number");
        C45Refused(c, Set(("loop", 1)), "bad_args", "values.loop", "WR2: a number for a switch");
        C45Refused(c, Set(("look.tint", new[] { 1, 2 })), "bad_args", "values.look.tint", "WR2: a vector of two");
        C45Refused(c, Set(("drips.color", new[] { 1, 2, 3 })), "bad_args", "values.drips.color", "WR2: a colour of three");
        C45Refused(c, Set(("preset", "wine")), "bad_args", "values.preset", "WR2: an unknown preset");
        C45Refused(c, Set(("name", "water")), "bad_args", "values.name", "WR2: another stain's name");
        C45Refused(c, Set(("floor.mode", "ceiling")), "bad_args", "values.floor.mode", "WR2: an unknown floor mode");
        C45Refused(c, Set(("showMarker", 1)), "bad_args", "values.showMarker", "WR2: showMarker is true or false");
        C45Refused(c, Set(("look.amount", 0.2), ("look.nothing", 1)), "bad_args", "values.look.nothing", "WR2: one wrong name takes all back");
        C45Refused(c, Set(("look.amount", 0.2), ("maxRadius", "far")), "bad_args", "values.maxRadius", "WR2: one wrong value takes all back");
        C45Refused(c, new { cmd = "wet.set", key = x.Chara }, "bad_args", "values", "WR2: values is required");
        C45Refused(c, new { cmd = "wet.set", key = x.Chara, values = 3 }, "bad_args", "values", "WR2: values is an object");
        C45Refused(c, new { cmd = "wet.set", key = x.Chara, values = new { } }, "bad_args", "values", "WR2: with something in it");
        C45Refused(c, new { cmd = "wet.set", key = x.Chara, name = "nothing", values = new { maxRadius = 1 } }, "not_found", "name", "WR2: a name no stain has");
        // with no current stain a stain's value has nowhere to go, a block's has
        var left = C45St(x).Wet.Clone();
        left.Current = "";
        C45Block(x, left);
        C45Refused(c, Set(("look.amount", 0.2)), "not_found", "name", "WR2: no current stain");
        c.Ok(Set(("film.opacity", 0.5)), "WR2: a block's member needs no stain");
        C45None(x);
    }

    static void C45Waiting(Ctx c, C45Fix x)
    {
        var ops = x.App.Ops;
        string sha = W1DocSha(c);
        int undo = W1UndoCount(c);
        var r = c.Ok(new { cmd = "wet.set", key = x.Chara, values = new Dictionary<string, object> { ["maxRadius"] = 3, ["dripRate"] = 9, ["film.gloss"] = 0.5, ["showMarker"] = true } }, "WR2: values with no stain yet");
        c.Check(ops.WetWaiting(x.Chara) == 3, "WR2: they wait", ops.WetWaiting(x.Chara) + "");
        c.Check(W1DocSha(c) == sha && W1UndoCount(c) == undo && C45St(x).Wet.IsEmpty, "WR2: not saved, no entry");
        c.Check((r["changed"] as JArray).Select(n => (string)n).SequenceEqual(new[] { "maxRadius", "dripRate", "film.gloss" }), "WR2: changed lists what waits", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "wet.set", key = x.Chara, values = new { maxRadius = 4 } }, "WR2: the same member again");
        c.Check(ops.WetWaiting(x.Chara) == 3, "WR2: the later value replaces the earlier", ops.WetWaiting(x.Chara) + "");
        C45Refused(c, new { cmd = "wet.set", key = x.Chara, values = new { maxRadius = "far" } }, "bad_args", "values.maxRadius", "WR2: a wrong value is refused now");
        C45Refused(c, new { cmd = "wet.set", key = x.Chara, values = new Dictionary<string, object> { ["look.nothing"] = 1 } }, "bad_args", "values.look.nothing", "WR2: a wrong name too");
        c.Check(ops.WetWaiting(x.Chara) == 3, "WR2: nothing of it waits");
        C45Err(c, new { cmd = "wet.attach", key = x.Chara }, "unsupported", null, "WR2: a refused attach");
        c.Check(ops.WetWaiting(x.Chara) == 3, "WR2: leaves them waiting");
        var other = x.App.Session.AddChara(null);
        x.App.Runtime.ApplyNow();
        c.Check(ops.WetWaiting(other.NewId) == 0, "WR2: they are this character's");
        c.Ok(new { cmd = "delete", keys = new[] { other.NewId }, undo = false }, "(that character goes)");
    }

    // ---------------------------------------------------------------- wet.floor

    static void C45Floor(Ctx c, C45Fix x)
    {
        C45Two(x);
        int undo = W1UndoCount(c);
        var r = c.Ok(new { cmd = "wet.floor", key = x.Chara }, "WR1: wet.floor reads");
        c.Check((string)r["source"] == "feet" && r["y"].Type != JTokenType.Null, "WR1: with no map the feet", Ctx.Short(c.LastText));
        double feet = (double)r["y"];
        c.Check(W1UndoCount(c) == undo, "WR1: a read is no entry");
        r = W1History1(c, new { cmd = "wet.floor", key = x.Chara, offset = 0.5 }, "Floor", "WR1: an offset");
        c.Check(Math.Abs((double)r["y"] - (feet + 0.5)) < 1e-4, "WR1: y takes it", Ctx.Short(c.LastText));
        var st = C45St(x).Wet;
        c.Check(Math.Abs(st.Puddles[0].Center.Y - 0.5f) < 1e-4f && Math.Abs(st.Puddles[1].Center.Y - 0.5f) < 1e-4f, "WR1: the puddles move by the difference", st.Puddles[0].Center + "");
        r = W1History1(c, new { cmd = "wet.floor", key = x.Chara, mode = "fixed" }, "Floor", "WR1: fixed with no height");
        c.Check((string)r["source"] == "fixed" && Math.Abs((double)r["y"] - (feet + 0.5)) < 1e-4, "WR1: takes the floor of now", Ctx.Short(c.LastText));
        c.Check(Math.Abs(C45St(x).Wet.Floor.Height - feet) < 1e-4, "WR1: the height is that floor without the offset", C45St(x).Wet.Floor.Height + "");
        r = W1History1(c, new { cmd = "wet.floor", key = x.Chara, height = 2 }, "Floor", "WR1: a height");
        c.Check(Math.Abs((double)r["y"] - 2.5) < 1e-4, "WR1: a world height plus the offset", Ctx.Short(c.LastText));
        c.Check(Math.Abs(C45St(x).Wet.Puddles[0].Center.Y - (2.5f - feet)) < 1e-3f, "WR1: the puddles again", C45St(x).Wet.Puddles[0].Center + "");
        r = W1History1(c, new { cmd = "wet.floor", key = x.Chara, mode = "feet", offset = 0 }, "Floor", "WR1: feet");
        c.Check((string)r["source"] == "feet" && Math.Abs((double)r["y"] - feet) < 1e-4, "WR1: back", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "wet.floor", key = x.Chara, mode = "map", undo = false }, "WR1: map");
        c.Check((string)c.Call(new { cmd = "wet.floor", key = x.Chara })["source"] == "feet", "WR1: a map that does not answer falls back to the feet, and says so");
        C45Refused(c, new { cmd = "wet.floor", key = x.Chara, mode = "ceiling" }, "bad_args", "mode", "WR1: an unknown mode");
        C45Refused(c, new { cmd = "wet.floor", key = x.Chara, mode = 3 }, "bad_args", "mode", "WR1: a mode is a text");
        C45Refused(c, new { cmd = "wet.floor", key = x.Chara, offset = "up" }, "bad_args", "offset", "WR1: an offset is a number");
        C45None(x);

        // with no stain the values wait, and the reply says what they would give
        var w = c.Ok(new { cmd = "wet.floor", key = x.Chara, mode = "fixed", height = 3 }, "WR1: a floor with no stain");
        c.Check((double)w["y"] == 3 && (string)w["source"] == "fixed", "WR1: answers what it would be", Ctx.Short(c.LastText));
        c.Check(C45St(x).Wet.IsEmpty && x.App.Ops.WetWaiting(x.Chara) >= 2, "WR1: and waits");
    }

    // ---------------------------------------------------------------- WR3

    static void C45Rays(Ctx c, C45Fix x)
    {
        C45None(x);
        var away = new[] { 500, 500, 500 };
        var further = new[] { 510, 500, 500 };
        // a miss, by each form of the two ends
        var a = c.Call(new { cmd = "wet.attach", key = x.Chara, from = away, target = further });
        c.Check((bool?)a["ok"] == false && (string)a["code"] == "not_found" && c.LastText.Contains("the ray meets nothing"), "WR3: wet.attach: [x, y, z] to [x, y, z], a miss", Ctx.Short(c.LastText));
        var s = c.Call(new { cmd = "wet.stain", key = x.Chara, name = "n", from = away, target = further });
        c.Check((bool?)s["ok"] == false && (string)s["code"] == "not_found", "WR3: wet.stain: a miss", Ctx.Short(c.LastText));
        c.Check(C45St(x).Wet.IsEmpty, "WR3: nothing changes");

        // the hips, from in front: a hit goes on to the bake, which this row refuses
        var r = x.App.Runtime.FindChara(x.Chara);
        WetTargets.Frame(r.Context, out var f);
        var mid = WetTargets.V(f.Mid);
        var front = mid + WetTargets.V(f.Forward) * 10f;
        var hip = new { key = x.Chara, bone = "cf_J_Hips" };
        double[] V(Vector3 v) => new double[] { v.x, v.y, v.z };
        C45Err(c, new { cmd = "wet.attach", key = x.Chara, from = V(front), target = V(mid) }, "unsupported", null, "WR3: points: a hit");
        C45Err(c, new { cmd = "wet.attach", key = x.Chara, from = V(front), target = hip }, "unsupported", null, "WR3: {key, bone}: a hit");
        C45Err(c, new { cmd = "wet.attach", key = x.Chara, from = V(front), target = new { key = x.Chara, bone = "cf_J_Hips", offset = new[] { 0, 0, 0 } } }, "unsupported", null, "WR3: with an offset: a hit");
        C45Err(c, new { cmd = "wet.attach", key = x.Chara, from = V(front) }, "unsupported", null, "WR3: from alone goes to the hips: a hit");
        C45Err(c, new { cmd = "wet.attach", key = x.Chara, target = hip }, "unsupported", null, "WR3: target alone comes from in front: a hit");
        C45Err(c, new { cmd = "wet.stain", key = x.Chara, from = V(front), target = V(mid) }, "unsupported", null, "WR3: wet.stain: a hit");
        // an offset moves the aim
        C45Err(c, new { cmd = "wet.attach", key = x.Chara, from = V(front), target = new { key = x.Chara, bone = "cf_J_Hips", offset = new[] { 900, 0, 0 } } }, "not_found", null, "WR3: an offset moves the aim off the character");
        // the ray ends half past the target
        var near = mid + WetTargets.V(f.Forward) * 40f;
        var short1 = near - WetTargets.V(f.Forward) * 2f;
        C45Err(c, new { cmd = "wet.attach", key = x.Chara, from = V(near), target = V(short1) }, "not_found", null, "WR3: the ray ends half past the target, short of the character");
        // an item: its far end towards the target starts the ray
        c.Ok(new { cmd = "obj.transform", key = x.Item, pos = V(front) }, "WR3: the item in front of the character");
        C45Err(c, new { cmd = "wet.attach", key = x.Chara, from = x.Item, target = V(mid) }, "unsupported", null, "WR3: an item's key: a hit");
        C45Err(c, new { cmd = "wet.attach", key = x.Chara, from = new { key = x.Item }, target = V(mid) }, "unsupported", null, "WR3: {key} of an item: a hit");
        C45Refused(c, new { cmd = "wet.attach", key = x.Chara, from = x.Folder, target = V(mid) }, "wrong_kind", "from", "WR3: a key that is no item");
        C45Refused(c, new { cmd = "wet.attach", key = x.Chara, from = C45Nope, target = V(mid) }, "not_found", "from", "WR3: a key of nothing");

        // what is refused
        // {key, accessory} (Phase 6.9: the muzzle chara.accessoryAxis reads) passes the argument checks; headless the stain's own refusal answers
        var accessory = new { cmd = "wet.attach", key = x.Chara, from = new { key = x.Chara, accessory = 0 }, target = V(mid) };
        C45Refused(c, accessory, "unsupported", null, "WR3: {key, accessory} passes the argument checks (no bad_args naming from.accessory)");
        var acc = c.Call(accessory);
        c.Check(((string)acc["error"] ?? "").Contains("the stain cannot be baked"), "WR3: {key, accessory}: headless the stain's own unsupported «" + (string)acc["error"] + "»", Ctx.Short(c.LastText));
        C45Refused(c, new { cmd = "wet.attach", key = x.Chara, from = new[] { 1, 2 }, target = V(mid) }, "bad_args", "from", "WR3: a point of two numbers");
        C45Refused(c, new { cmd = "wet.attach", key = x.Chara, from = new object[] { 1, "a", 3 }, target = V(mid) }, "bad_args", "from[1]", "WR3: a text in a point");
        C45Refused(c, new { cmd = "wet.attach", key = x.Chara, from = 3, target = V(mid) }, "bad_args", "from", "WR3: a number is no point");
        C45Refused(c, new { cmd = "wet.attach", key = x.Chara, target = "x" }, "bad_args", "target", "WR3: a text is no target");
        C45Refused(c, new { cmd = "wet.attach", key = x.Chara, target = new { bone = "cf_J_Hips" } }, "bad_args", "target.key", "WR3: an object with no key");
        C45Refused(c, new { cmd = "wet.attach", key = x.Chara, target = new { key = C45Nope } }, "not_found", "target.key", "WR3: a key of nothing");
        C45Refused(c, new { cmd = "wet.attach", key = x.Chara, target = new { key = x.Chara, bone = "no_such_bone" } }, "not_found", "target.bone", "WR3: a bone the character lacks");
        C45Refused(c, new { cmd = "wet.attach", key = x.Chara, target = new { key = x.Chara, bone = 3 } }, "bad_args", "target.bone", "WR3: a bone is a text");
        C45Refused(c, new { cmd = "wet.attach", key = x.Chara, target = new { key = x.Chara, offset = new[] { 1 } } }, "bad_args", "target.offset", "WR3: an offset of one number");
        C45Refused(c, new { cmd = "wet.attach", key = x.Chara, from = V(mid), target = V(mid) }, "bad_args", "target", "WR3: from and target the same point");
        var strict = c.Call(new { cmd = "wet.attach", key = x.Chara, from = V(front), target = new { key = x.Chara, bone = "cf_J_Hips", extra = 1 }, strict = true });
        c.Check((string)strict["code"] == "bad_args" && (string)strict["detail"]?["arg"] == "target.extra", "WR3: an unknown member under strict", Ctx.Short(c.LastText));
        var loose = c.Call(new { cmd = "wet.attach", key = x.Chara, from = V(front), target = new { key = x.Chara, bone = "cf_J_Hips", extra = 1 } });
        c.Check((string)loose["code"] == "unsupported", "WR3: else ignored", Ctx.Short(c.LastText));
        c.Check(C45St(x).Wet.IsEmpty, "WR3: nothing changed through all of it");
    }

    // ---------------------------------------------------------------- WR4

    static void C45Probe(Ctx c, C45Fix x)
    {
        int undo = W1UndoCount(c);
        string sha = W1DocSha(c);
        var r = c.Ok(new { cmd = "ground.probe", points = new object[] { new[] { 1, 2 }, new[] { 3, 4, 5 } } }, "WR4: ground.probe with no map");
        var hits = r["hits"] as JArray;
        c.Check(r.Property("source") != null && r["source"].Type == JTokenType.Null && r.Property("triangles") == null, "WR4: source null, triangles dropped", Ctx.Short(c.LastText));
        c.Check(hits?.Count == 2, "WR4: a hit per point", Ctx.Short(c.LastText));
        var h = (JObject)hits[0];
        c.Check(h.Properties().Select(p => p.Name).SequenceEqual(new[] { "x", "z", "y", "normal" }), "WR4: {x, z, y, normal}", h.ToString(Formatting.None));
        c.Check((double)h["x"] == 1 && (double)h["z"] == 2 && h["y"].Type == JTokenType.Null && h["normal"].Type == JTokenType.Null, "WR4: [x, z] read, no answer", h.ToString(Formatting.None));
        c.Check((double)hits[1]["x"] == 3 && (double)hits[1]["z"] == 5, "WR4: [x, y, z] read", hits[1].ToString(Formatting.None));
        c.Check(W1UndoCount(c) == undo && W1DocSha(c) == sha, "WR4: a read: no entry, no document");
        c.Check((c.Ok(new { cmd = "ground.probe", points = new object[0] }, "WR4: no point")["hits"] as JArray).Count == 0, "WR4: no hit");
        c.Ok(new { cmd = "ground.probe", points = new object[] { new[] { 1, 2 } }, undo = false }, "WR4: undo: false");
        c.Check(W1UndoCount(c) == undo, "WR4: leaves the history alone");
        C45Err(c, new { cmd = "ground.probe" }, "bad_args", "points", "WR4: points is required");
        C45Err(c, new { cmd = "ground.probe", points = 3 }, "bad_args", "points", "WR4: points is an array");
        C45Err(c, new { cmd = "ground.probe", points = new object[] { 3 } }, "bad_args", "points[0]", "WR4: a point is an array");
        C45Err(c, new { cmd = "ground.probe", points = new object[] { new[] { 1 } } }, "bad_args", "points[0]", "WR4: of two or three numbers");
        C45Err(c, new { cmd = "ground.probe", points = new object[] { new[] { 1, 2, 3, 4 } } }, "bad_args", "points[0]", "WR4: not four");
        C45Err(c, new { cmd = "ground.probe", points = new object[] { new[] { 1, 2 }, new object[] { 1, "a" } } }, "bad_args", "points[1][1]", "WR4: numbers");
        C45Err(c, new { cmd = "ground.probe", points = new object[0], y = "low" }, "bad_args", "y", "WR4: y is a number");
        C45Err(c, new { cmd = "ground.probe", points = new object[0], above = "up" }, "bad_args", "above", "WR4: above is a number");
        C45Err(c, new { cmd = "ground.probe", points = new object[0], below = "down" }, "bad_args", "below", "WR4: below is a number");
    }

    // ---------------------------------------------------------------- limits

    static void C45Limits(Ctx c, C45Fix x)
    {
        var b = new CharaWet();
        for (int i = 0; i < WetLimits.MaxStains; i++) WetEdits.Add(b, WetEdits.NewStain(b, "n" + i, WetPreset.Water));
        C45Block(x, b);
        void Limit(object req, string what)
        {
            string sha = W1DocSha(c);
            var r = c.Call(req);
            c.Check((string)r["code"] == "bad_args" && c.LastText.Contains("wet.limit"), what + " is bad_args with wet.limit in the message", Ctx.Short(c.LastText));
            c.Check(W1DocSha(c) == sha, what + ": nothing stored");
        }
        Limit(new { cmd = "wet.stain", key = x.Chara, name = "ninth" }, "WR1: a ninth stain");
        Limit(new { cmd = "wet.keep", key = x.Chara }, "WE4: keep at eight stains");
        C45None(x);
        Limit(new { cmd = "wet.stain", key = x.Chara, name = new string('n', WetLimits.MaxName + 1) }, "WR1: a name of 65 characters");
        C45Two(x);
        Limit(new { cmd = "wet.set", key = x.Chara, values = new { name = new string('n', WetLimits.MaxName + 1) } }, "WR2: a name of 65 characters by value");
        C45None(x);
    }

    // ---------------------------------------------------------------- the clock against an edit

    static void C45Clock(Ctx c, C45Fix x)
    {
        C45Two(x);
        c.Ok(new { cmd = "scene.pause", paused = true }, "WE5: the pause");
        c.Ok(new { cmd = "wet.start", key = x.Chara, undo = false }, "WE5: a start");
        c.Ok(new { cmd = "scene.step", n = 10, dt = 0.1 }, "WE5: ten steps of 0.1");
        var row = C45Row(c, x.Chara);
        c.Check(Math.Abs((double)row["t"] - 1.0) < 1e-4 && (bool)row["running"], "WE5: the scene clock moved it", row.ToString(Formatting.None));
        c.Check(C45St(x).Wet.CurrentStain.T == 0f, "WE5: the clock is the runtime's: the document has not moved");
        c.Ok(new { cmd = "wet.dry", key = x.Chara }, "WE6: dry");
        row = C45Row(c, x.Chara);
        c.Check((double)row["t"] == 0 && !(bool)row["running"], "WE6: stands at 0", row.ToString(Formatting.None));
        c.Ok(new { cmd = "undo" }, "WE6: undo");
        row = C45Row(c, x.Chara);
        c.Check(Math.Abs((double)row["t"] - 1.0) < 1e-4 && (bool)row["running"], "WE6: undo brings back what was shown", row.ToString(Formatting.None));
        c.Ok(new { cmd = "scene.step", n = 5, dt = 0.1 }, "WE6: it goes on");
        c.Check(Math.Abs((double)C45Row(c, x.Chara)["t"] - 1.5) < 1e-4, "WE6: from there");
        c.Ok(new { cmd = "scene.pause", paused = false }, "(the pause ends)");
        C45None(x);
    }

    // ---------------------------------------------------------------- a batch

    static void C45Batch(Ctx c, C45Fix x)
    {
        C45Two(x);
        string sha = W1DocSha(c);
        int undo = W1UndoCount(c);
        var ok = c.Ok(new
        {
            cmd = "batch",
            cmds = new object[]
            {
                new { cmd = "wet.start", key = x.Chara },
                new { cmd = "wet.set", key = x.Chara, values = new Dictionary<string, object> { ["look.amount"] = 0.5 } },
                new { cmd = "wet.keep", key = x.Chara },
                new { cmd = "wet.use", key = x.Chara, name = "water" },
            },
        }, "WE: a batch of four");
        c.Check(W1UndoCount(c) == undo + 1 && W1TopLabel(c) == "Batch", "WE: a batch is one entry", Ctx.Short(c.LastText));
        c.Check(C45St(x).Wet.Stains.Count == 3 && C45St(x).Wet.CurrentStain.Name == "water" && C45St(x).Wet.Stains[1].Look.Amount == 0.5f, "WE: each member sees the one before");
        c.Ok(new { cmd = "undo" }, "WE: undo of the batch");
        c.Check(W1DocSha(c) == sha, "WE: one undo takes it all");
        C45None(x);
    }

    // ---------------------------------------------------------------- timed commands

    static void C45Timed(Ctx c, C45Fix x)
    {
        var pb = x.App.Runtime.GetPart<TimelinePlayback>();
        if (!c.Check(pb != null, "WE: the playback")) return;
        C45Two(x);
        c.Ok(new { cmd = "obj.transform", key = x.Item, pos = new[] { 0, 0, 0 }, undo = true }, "WE: something on the history");
        int undo = W1UndoCount(c);
        string top = W1TopLabel(c);
        c.Ok(new
        {
            cmd = "timeline.build", duration = 1, tracks = new object[0], play = false,
            events = new object[]
            {
                new { t = 0.2, @do = new { cmd = "wet.use", key = x.Chara, name = "water" } },
                new { t = 0.4, @do = (object)new { cmd = "wet.set", key = x.Chara, values = new Dictionary<string, object> { ["maxRadius"] = 3 } } },
                new { t = 0.6, @do = (object)new { cmd = "wet.dry", key = x.Chara, puddles = false } },
                new { t = 0.8, @do = (object)new { cmd = "wet.start", key = x.Chara } },
            },
        }, "WE: a Timeline with four timed commands");
        int built = W1UndoCount(c);
        c.Ok(new { cmd = "timeline.play" }, "WE: play");
        long fired = pb.EventsFired;
        int k = 0;
        while (pb.Player.Time < 0.95 && k < 400) { x.App.Frame(Dt); k++; }
        x.App.Frame(Dt);
        x.App.Frame(Dt);
        pb.CollectReplies();
        c.Check(pb.EventsFired == fired + 4, "WE: all four fire", (pb.EventsFired - fired) + "");
        c.Check(pb.EventErrors == 0, "WE: no error", pb.LastEventError?.Message);
        var st = C45St(x).Wet;
        c.Check(st.CurrentStain.Name == "water" && st.CurrentStain.Shape.Radius == 3f, "WE: each did its work");
        c.Check(W1UndoCount(c) == built, "WE: a timed command leaves the history alone", W1UndoCount(c) + " / " + built);
        c.Ok(new { cmd = "timeline.stop" }, "WE: stop");
        c.Ok(new { cmd = "timeline.clear", undo = false }, "WE: the Timeline goes");
        C45None(x);
    }

    // ---------------------------------------------------------------- duplicate and import

    static void C45Duplicate(Ctx c, C45Fix x)
    {
        C45Two(x);
        c.Ok(new { cmd = "scene.pause", paused = true }, "(the pause)");
        c.Ok(new { cmd = "wet.start", key = x.Chara, undo = false }, "WE: a running stain");
        c.Ok(new { cmd = "scene.step", n = 5, dt = 0.1 }, "WE: half a second");
        var ops = x.App.Ops;
        int undo = W1UndoCount(c), nodes = x.App.Session.Model.Count;
        var d = ops.Duplicate(new[] { x.Chara });
        c.Check(d.Ok && d.NewIds.Count == 1, "WE: duplicate", d.ToString());
        c.Check(W1UndoCount(c) == undo + 1 && W1TopLabel(c) == "Duplicate", "WE: one entry «Duplicate»");
        var copy = ((CharaNodeState)x.App.Session.Model.Find(d.NewIds[0]).State).Wet;
        c.Check(copy.Stains.Count == 2 && copy.Stains.Select(s => s.Name).SequenceEqual(new[] { "water", "blood" }), "WE: the stains come along");
        c.Check(Math.Abs(copy.CurrentStain.T - 0.5f) < 1e-4f, "WE: at the moment they show", copy.CurrentStain.T + "");
        c.Check(copy.Stains.All(s => !s.Running), "WE: not running");
        c.Check(copy.Puddles.Count == 0, "WE: puddles stay behind");
        c.Check(C45St(x).Wet.Puddles.Count == 2, "WE: on the source too they stay");
        c.Ok(new { cmd = "undo" }, "WE: undo");
        c.Check(x.App.Session.Model.Count == nodes && x.App.Session.Model.Find(d.NewIds[0]) == null, "WE: one undo takes the copy");
        c.Ok(new { cmd = "redo" }, "WE: redo");
        c.Check(x.App.Session.Model.Find(d.NewIds[0]) != null, "WE: redo brings it back");
        c.Ok(new { cmd = "undo" }, "WE: undo again");
        c.Ok(new { cmd = "scene.pause", paused = false }, "(the pause ends)");
        C45None(x);
    }

    static void C45Import(Ctx c, C45Fix x)
    {
        C45Two(x);
        c.Ok(new { cmd = "scene.pause", paused = true }, "(the pause)");
        c.Ok(new { cmd = "wet.start", key = x.Chara, undo = false }, "WE: a running stain");
        c.Ok(new { cmd = "scene.step", n = 5, dt = 0.1 }, "WE: half a second");
        c.Ok(new { cmd = "scene.save", name = "c45_import", overwrite = true, clock = "2026-11-06T00:00:00Z" }, "WE: saved");
        c.Ok(new { cmd = "scene.pause", paused = false }, "(the pause ends)");
        c.Ok(new { cmd = "scene.new" }, "WE: the target scene");
        var s = x.App.Session;
        int undo = W1UndoCount(c), nodes = s.Model.Count;
        var r = c.Ok(new { cmd = "scene.import", path = "c45_import.hs2nscene" }, "WE: scene.import");
        c.Check(W1UndoCount(c) == undo + 1 && W1TopLabel(c) == "Import scene", "WE: one entry «Import scene»");
        var chara = s.Model.PreOrder().First(n => n.Kind == NodeKind.Chara);
        var block = ((CharaNodeState)chara.State).Wet;
        c.Check(block.Stains.Count == 2 && block.Stains.Select(t => t.Name).SequenceEqual(new[] { "water", "blood" }), "WE: the stains come along");
        c.Check(Math.Abs(block.CurrentStain.T - 0.5f) < 1e-4f, "WE: at the moment they showed", block.CurrentStain.T + "");
        c.Check(block.Stains.All(t => !t.Running), "WE: not running");
        c.Check(block.Puddles.Count == 0, "WE: puddles stay behind");
        c.Ok(new { cmd = "undo" }, "WE: undo");
        c.Check(s.Model.Count == nodes, "WE: one undo takes it");
    }
}
