// C50: the commands of scene scripts (Phase 6.9 package C) in an edit-mode test host, headless: the clothed SFW targets
// BR-Chan and HERO107, the fallback item, scripts put in through the commands; the store is the host's own (Spike/out/bridge/userdata/scripts, its files
// removed before and after), exports go to Spike/out/bridge/out/c50 (removed after). JS1 the names, arguments, defaults, replies and their order; JS2 the
// errors, `id` stays the envelope's, a script named by id, name and index; JS3 script.borrow; JS4 the store commands; JS5 build-form tracks through
// script.set equal timeline.build's stored form; JS6 script.status while playing, paused and stopped; JS7 every command through ctx.Request of a test
// plugin with the bridge off; JS8 refused steps and timed commands; JS9 the group and the counts per group; JG3 the parity names; JB4 a scene import
// carries its scripts; JS10 a whole Timeline set back with timeline.set loses its script mark; the import warnings of JE3 (a set-up step that clears the
// history) and JC8 (plugin.call, plugin.set, motion.play stay unknown). A refusal stores nothing and makes no entry.
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
using HS2Next.Scenes.Scripts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static partial class BridgeCoreTest
{
    static readonly string[] C50Script =
    {
        "script.list", "script.get", "script.set", "script.remove", "script.cast", "script.play", "script.stop", "script.status", "script.capture",
        "script.borrow", "script.keep", "script.add", "script.import", "script.export", "script.delete", "script.purge",
    };
    static readonly string[] C50Generic = { "obj.moveTo", "camera.follow", "dof", "chara.accessoryAxis" };
    static readonly string[] C50Reads = { "script.list", "script.get", "script.status", "chara.accessoryAxis" };
    static readonly string[] C50Files = { "script.export" };
    /// <summary>Commands, experimental ones and parity names of the registry with this phase's commands in.</summary>
    const int C50Total = 265, C50Experimental = 107, C50Parity = 111;

    /// <summary>The name and the default of each argument, in order, as the spec's table gives them (script.play's strict is the envelope's).</summary>
    static readonly Dictionary<string, (string name, object value)[]> C50Args = new Dictionary<string, (string, object)[]>
    {
        ["script.list"] = new (string, object)[] { ("store", false), ("q", ""), ("deleted", false) },
        ["script.get"] = new (string, object)[] { ("script", null), ("no", null) },
        ["script.set"] = new (string, object)[] { ("script", null), ("replace", false) },
        ["script.remove"] = new (string, object)[] { ("script", null) },
        ["script.cast"] = new (string, object)[] { ("script", null), ("roles", null) },
        ["script.play"] = new (string, object)[] { ("script", null), ("from", 0f), ("prepare", "auto"), ("load", true), ("partial", false), ("reload", false), ("discard", false), ("record", null) },
        ["script.stop"] = new (string, object)[] { ("reset", true) },
        ["script.status"] = new (string, object)[0],
        ["script.capture"] = new (string, object)[] { ("script", null) },
        ["script.borrow"] = new (string, object)[] { ("script", null), ("from", null), ("no", null), ("window", null), ("shift", 0f), ("roles", null), ("events", true), ("tracks", true), ("camera", false) },
        ["script.keep"] = new (string, object)[] { ("script", null) },
        ["script.add"] = new (string, object)[] { ("no", null) },
        ["script.import"] = new (string, object)[] { ("path", null), ("to", "store"), ("replace", false) },
        ["script.export"] = new (string, object)[] { ("script", null), ("no", null), ("path", null) },
        ["script.delete"] = new (string, object)[] { ("no", null), ("restore", false) },
        ["script.purge"] = new (string, object)[0],
    };

    /// <summary>The reply members of each command, in order (script.set's and script.import's warnings travel in the envelope's warnings).</summary>
    static readonly Dictionary<string, string[]> C50Returns = new Dictionary<string, string[]>
    {
        ["script.list"] = new[] { "scripts", "loaded", "rows", "lastNumber" },
        ["script.get"] = new[] { "script", "cast", "prepared", "loaded" },
        ["script.set"] = new[] { "script", "roles", "steps", "tracks", "keys", "events" },
        ["script.remove"] = new[] { "removed" },
        ["script.cast"] = new[] { "cast", "uncast" },
        ["script.play"] = new[] { "script", "roles", "loaded", "prepared", "setup", "prepare", "skipped", "failures", "state", "time", "record" },
        ["script.stop"] = new[] { "state", "released", "ragdolls", "dried" },
        ["script.status"] = new[] { "script", "name", "state", "time", "duration", "loop", "roles", "uncast", "prepared", "lastPlay", "eventsFired", "eventErrors", "lastEventError", "slots", "recording" },
        ["script.capture"] = new[] { "script", "tracks", "keys", "events" },
        ["script.borrow"] = new[] { "events", "keys", "tracks", "replaced" },
        ["script.keep"] = new[] { "no" },
        ["script.add"] = new[] { "script" },
        ["script.import"] = new[] { "no", "script" },
        ["script.export"] = new[] { "path", "bytes" },
        ["script.delete"] = new[] { "deleted" },
        ["script.purge"] = new[] { "rows", "files", "bytes" },
    };
    static readonly string[] C50SceneRow = { "script", "name", "roles", "cast", "uncast", "prepared", "loaded", "store" };
    static readonly string[] C50StoreRow = { "no", "id", "name", "created", "modified", "deleted" };
    static readonly string[] C50Required =
    {
        "script.set:script", "script.remove:script", "script.cast:script", "script.cast:roles", "script.play:script", "script.borrow:script", "script.keep:script",
        "script.add:no", "script.import:path", "script.export:path", "script.delete:no",
    };

    /// <summary>Build-form tracks of every type, keyed by "$a" (BR-Chan), "$b" (HERO107) and "$p" (item 399); the animation rows are plain rows of targets.json.</summary>
    const string C50BuildTracks = @"[
      {""type"":""camera"",""keys"":[{""t"":0,""target"":[0,10,0],""rot"":[10,180,0],""dist"":25,""fov"":23},{""t"":2,""dist"":20}]},
      {""type"":""dof"",""keys"":[{""t"":0,""on"":true,""focus"":""$a"",""sharp"":0.2,""full"":4},{""t"":1.5,""focus"":{""key"":""$b"",""bone"":""cf_J_Head"",""offset"":[0,0.5,0]},""blur"":2}]},
      {""type"":""pos"",""key"":""$a"",""keys"":[{""t"":0,""v"":[0,0,0]},{""t"":2,""v"":[0,0,3],""ease"":""linear""}]},
      {""type"":""rot"",""key"":""$a"",""keys"":[{""t"":0,""v"":[0,0,0]},{""t"":1,""v"":[0,90,0]}]},
      {""type"":""rot"",""key"":""$b"",""bone"":""cf_J_Neck"",""keys"":[{""t"":0,""v"":[0,0,0]},{""t"":1,""v"":[0,20,0]}]},
      {""type"":""scale"",""key"":""$p"",""keys"":[{""t"":0,""v"":[1,1,1]},{""t"":1,""v"":[2,2,2]}]},
      {""type"":""anime"",""key"":""$b"",""keys"":[{""t"":0,""group"":11,""category"":5,""no"":0},{""t"":1.25,""group"":19,""category"":0,""no"":0}]},
      {""type"":""animeSpeed"",""key"":""$b"",""keys"":[{""t"":0,""v"":1},{""t"":1,""v"":0.5}]},
      {""type"":""animeTime"",""key"":""$a"",""keys"":[{""t"":0,""v"":0.1}]},
      {""type"":""eyes"",""key"":""$a"",""raw"":true,""keys"":[{""t"":0,""v"":1},{""t"":1,""v"":2}]},
      {""type"":""mouth"",""key"":""$a"",""keys"":[{""t"":0,""v"":3}]},
      {""type"":""eyesOpen"",""key"":""$a"",""keys"":[{""t"":0,""v"":1},{""t"":0.5,""v"":0.2}]},
      {""type"":""mouthOpen"",""key"":""$a"",""keys"":[{""t"":0,""v"":0.3}]},
      {""type"":""color"",""key"":""$p"",""slot"":0,""keys"":[{""t"":0,""v"":[1,0,0,1]}]}
    ]";

    sealed class C50Fix
    {
        public AppCore App;
        public string A, B, P, PRef;
    }

    static string C50Store(Ctx c) => Path.Combine(c.UserDataDir, ScriptStore.FolderName);
    static string C50Out(Ctx c) => Path.Combine(c.OutputDir, "c50");

    /// <summary>Removes the files of the host's script store and of the export folder (files only, by name; the folders when empty).</summary>
    static void C50Clear(Ctx c)
    {
        foreach (var dir in new[] { C50Store(c), C50Out(c) })
        {
            if (File.Exists(dir)) File.Delete(dir);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.GetFiles(dir)) File.Delete(f);
            if (Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir);
        }
    }

    static void C50(Ctx c)
    {
        C50Clear(c);
        try
        {
            C50Registration(c);
            var app = c.Host();
            string a = C30Load(c, c.FemaleRel, "female", "C50: BR-Chan");
            string b = C30Load(c, c.MaleRel, "male", "C50: HERO107");
            string p = W1AddItem(c, "C50: the fallback item");
            if (!c.Check(a != null && b != null && p != null, "C50: the fixture (two characters, an item)")) return;
            var x = new C50Fix { App = app, A = a, B = b, P = p, PRef = ScriptRoles.RefOf(app.Session.Model.Find(p)) };
            C50Replies(c, x);
            C50Errors(c, x);
            C50Steps(c, x);
            C50Warnings(c, x);
            C50BuildForm(c, x);
            C50Borrow(c, x);
            C50Status(c, x);
            C50TimelineSet(c, x);
            C50StoreCommands(c, x);
            C50Import(c, x);
        }
        finally { C50Clear(c); }
        C50Clear(c);
        try { C50Request(c); }
        finally { C50Clear(c); }
    }

    // ---------------------------------------------------------------- helpers

    static JObject C50Doc(string name, string id = null, string roles = null, string setup = null, string prepare = null, string tracks = null, string events = null, double duration = 6)
    {
        var o = new JObject { ["format"] = SceneScript.FormatName, ["version"] = SceneScript.FormatVersion };
        if (id != null) o["id"] = id;
        o["name"] = name;
        o["roles"] = JArray.Parse(roles ?? "[{\"role\":\"a\",\"kind\":\"chara\",\"sex\":\"female\"},{\"role\":\"b\",\"kind\":\"chara\",\"sex\":\"male\"}]");
        o["setup"] = JArray.Parse(setup ?? "[]");
        o["prepare"] = JArray.Parse(prepare ?? "[]");
        o["timeline"] = new JObject { ["duration"] = duration, ["loop"] = true, ["autoplay"] = false, ["tracks"] = JArray.Parse(tracks ?? "[]"), ["events"] = JArray.Parse(events ?? "[]") };
        return o;
    }

    static string C50Set(Ctx c, JObject doc, string what)
    {
        var r = c.Ok(new { cmd = "script.set", script = doc }, what);
        return (string)r["script"];
    }

    /// <summary>The last reply parsed without date parsing (times stay their text).</summary>
    static JObject C50Raw(Ctx c)
    {
        using (var sr = new StringReader(c.LastText))
        using (var jr = new JsonTextReader(sr) { DateParseHandling = DateParseHandling.None })
            return JObject.Load(jr);
    }

    /// <summary>A scene with the fixture again: BR-Chan, HERO107 and the item loaded as C50 loads them first (a section after scene.new).</summary>
    static bool C50Fixture(Ctx c, C50Fix x, string what)
    {
        c.Ok(new { cmd = "scene.new" }, what + ": a new scene");
        x.A = C30Load(c, c.FemaleRel, "female", what + ": BR-Chan");
        x.B = C30Load(c, c.MaleRel, "male", what + ": HERO107");
        x.P = W1AddItem(c, what + ": the fallback item");
        x.PRef = x.P != null ? ScriptRoles.RefOf(C50Model(x).Find(x.P)) : null;
        return c.Check(x.A != null && x.B != null && x.P != null, what + ": the fixture loaded again (two characters, an item)");
    }

    /// <summary>The member names of a nested row, in order, all of them (an `id` of a row is a member, not the envelope's).</summary>
    static List<string> C50Names(JObject r) => r.Properties().Select(p => p.Name).ToList();

    static List<string> C50Warnings(JObject r) => (r["warnings"] as JArray)?.Select(w => (string)w).ToList() ?? new List<string>();

    static bool C50Order(Ctx c, JObject r, string cmd, string what, string[] want = null)
    {
        var got = C47Members(r);
        want ??= C50Returns[cmd];
        return c.Check(got.SequenceEqual(want), what + ": the reply's members in the order of the spec", string.Join(", ", got));
    }

    static SceneModel C50Model(C50Fix x) => x.App.Session.Model;

    static int C50Count(C50Fix x) => C50Model(x).Scripts?.Count ?? 0;

    static SceneScript C50Script1(C50Fix x, string id) => C50Model(x).Scripts?.Find(id);

    static string C50Scripts(C50Fix x) => C50Model(x).Scripts?.ToJson() ?? "null";

    // ---------------------------------------------------------------- JS1, JS9, JG3: registration

    static void C50Registration(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        var names = CoreCommands.Phase69Names.ToList();
        c.Check(names.SequenceEqual(C50Script.Concat(C50Generic)), "JS9: Phase69Names = the 16 script commands of the spec and the 4 generic ones, in order", string.Join(", ", names));
        c.Check(ScriptCommands.Names.SequenceEqual(C50Script), "JS1: ScriptCommands.Names = the spec's 16, in its order", string.Join(", ", ScriptCommands.Names));
        c.Check(names.All(CoreCommands.AllNames.Contains), "JS9: all in CoreCommands.AllNames");
        var groups = CoreCommands.Groups.ToList();
        c.Check(groups.Last() == "script" && ScriptCommands.GroupName == "script" && ScriptedCommands.GroupName == "script" && CkGroups.Contains("script"),
            "JS9: the group script is the last of CoreCommands.Groups, a convention group, and both files declare it", string.Join(", ", groups));
        c.Check(ScriptCommands.Since == "1.3.0" && ScriptedCommands.Since69 == ScriptCommands.Since, "JS9: one Since for the group: ScriptCommands.Since 1.3.0, ScriptedCommands.Since69 the same", ScriptCommands.Since + " / " + ScriptedCommands.Since69);
        var par = CoreCommands.ParityNames.ToList();
        c.Check(C50Generic.All(par.Contains) && ScriptedCommands.ParityNames.SequenceEqual(C50Generic) && par.Count == C50Parity && C50Script.All(n => !par.Contains(n)),
            "JG3: ParityNames + 4 (obj.moveTo, camera.follow, dof, chara.accessoryAxis) = " + C50Parity + "; no script command is a parity name", par.Count + "");
        var bad = new List<string>();
        foreach (var n in names)
        {
            var s = reg.Find(n);
            if (s == null) { bad.Add(n + ": not registered"); continue; }
            bool read = C50Reads.Contains(n), files = C50Files.Contains(n);
            if (s.Owner != BridgeOwners.Core) bad.Add(n + ": owner " + s.Owner);
            if (s.Group != "script") bad.Add(n + ": group " + s.Group);
            if (s.Since != "1.3.0") bad.Add(n + ": since " + s.Since);
            if ((s.Flags & CommandFlags.Experimental) == 0) bad.Add(n + ": not experimental");
            if (((s.Flags & CommandFlags.ReadOnly) != 0) != read) bad.Add(n + ": read flag");
            if (((s.Flags & CommandFlags.Mutates) != 0) != (!read && !files)) bad.Add(n + ": mutates flag");
            if (((s.Flags & CommandFlags.WritesFiles) != 0) != files) bad.Add(n + ": writes-files flag");
            if (((s.Flags & CommandFlags.Defers) != 0) != (n == "script.play")) bad.Add(n + ": defers flag");
            if (string.IsNullOrEmpty(s.Summary)) bad.Add(n + ": no summary");
            if (s.Args.Any(x => string.IsNullOrEmpty(x.Doc))) bad.Add(n + ": an argument without a doc");
            if (s.Aliases.Count > 0) bad.Add(n + ": an alias");
            if (s.Name != n) bad.Add(n + ": found as " + s.Name);
        }
        c.Check(bad.Count == 0, "JS1: owner core, group script, Since 1.3.0, experimental, read / write / defers flags, a summary, argument docs, no alias", string.Join("; ", bad));
        c.Check(reg.Find("script.play")?.DefaultTimeoutSec == 600, "JS1: script.play answers within 600 s (loads)", reg.Find("script.play")?.DefaultTimeoutSec + "");
        foreach (var kv in C50Args)
        {
            var got = reg.Find(kv.Key)?.Args.ToList() ?? new List<FieldSpec>();
            c.Check(got.Select(x => x.Name).SequenceEqual(kv.Value.Select(x => x.name)), "JS1: " + kv.Key + ": the arguments of the spec, in order", string.Join(", ", got.Select(x => x.Name)));
            string defaults = JsonConvert.SerializeObject(got.Select(x => x.Default)), want = JsonConvert.SerializeObject(kv.Value.Select(x => x.value));
            c.Check(defaults == want, "JS1: " + kv.Key + ": the defaults of the spec", defaults);
        }
        foreach (var kv in C50Returns)
        {
            var got = reg.Find(kv.Key)?.Returns.Select(x => x.Name).ToList() ?? new List<string>();
            c.Check(got.SequenceEqual(kv.Value), "JS1: " + kv.Key + ": the reply's members of the spec, in order", string.Join(", ", got));
        }
        var required = C50Script.SelectMany(n => reg.Find(n).Args.Where(x => x.Required).Select(x => n + ":" + x.Name)).ToList();
        c.Check(required.SequenceEqual(C50Required), "JS1: the required arguments", string.Join(", ", required));
        var live = reg.Specs.Where(s => s.Owner == BridgeOwners.Core || s.Owner == BridgeOwners.Bridge).ToList();
        int experimental = live.Count(s => (s.Flags & CommandFlags.Experimental) != 0);
        c.Check(live.Count == C50Total && experimental == C50Experimental, "JS9: " + C50Total + " commands, " + C50Experimental + " experimental", live.Count + " / " + experimental);
        // the counts per group (for CK0: 245 -> 265, 20 added in the group script)
        var perGroup = live.GroupBy(s => s.Owner == BridgeOwners.Bridge ? "(bridge)" : s.Group ?? "(none)").ToDictionary(g => g.Key, g => g.Count());
        var order = groups.Concat(new[] { "(bridge)" }).Concat(perGroup.Keys.Where(k => !groups.Contains(k) && k != "(bridge)")).ToList();
        c.Info("JS9: commands per group: " + string.Join(", ", order.Where(perGroup.ContainsKey).Select(g => g + " " + perGroup[g])) + " = " + live.Count);
        c.Check(perGroup.TryGetValue("script", out int ns) && ns == 20 && perGroup.Values.Sum() == C50Total, "JS9: the group script holds 20 commands; the groups add up to " + C50Total, "script " + ns);
        foreach (var n in names)
        {
            var h = c.Call(app, new { cmd = "help", name = n });
            c.Check(h.Value<bool?>("ok") == true && (string)h["command"]?["name"] == n, "JS9: help describes " + n, Ctx.Short(c.LastText));
        }
        var hg = c.Call(app, new { cmd = "help", group = "script" });
        var listed = (hg["commands"] as JArray)?.OfType<JObject>().Select(o => (string)o["name"]).ToList() ?? new List<string>();
        c.Check(listed.Count == 20 && names.All(listed.Contains), "JS9: help {group: script} lists the 20 commands", string.Join(", ", listed));
        c.Dispose(app);
    }

    // ---------------------------------------------------------------- JS1: replies

    static void C50Replies(Ctx c, C50Fix x)
    {
        var doc = C50Doc("c50 replies", null,
            "[{\"role\":\"a\",\"kind\":\"chara\",\"sex\":\"female\"},{\"role\":\"b\",\"kind\":\"chara\",\"sex\":\"male\"},{\"role\":\"p\",\"kind\":\"item\",\"ref\":\"" + x.PRef + "\"}]",
            "[{\"cmd\":\"obj.transform\",\"key\":\"$a\",\"pos\":[0,0,1],\"note\":\"stand\"}]",
            "[{\"cmd\":\"obj.transform\",\"key\":\"$b\",\"pos\":[2,0,1]}]",
            "[{\"type\":\"pos\",\"key\":\"$a\",\"keys\":[{\"t\":0,\"v\":[0,0,1]},{\"t\":2,\"v\":[0,0,3]}]}]",
            "[{\"t\":0.5,\"do\":{\"cmd\":\"obj.transform\",\"key\":\"$b\",\"pos\":[2,0,2]}}]");
        int undo = W1UndoCount(c);
        var set = c.Ok(new { cmd = "script.set", script = doc }, "JS1: script.set");
        C50Order(c, set, "script.set", "JS1: script.set");
        string id = (string)set["script"];
        c.Check(JsonText.IsId(id) && (int)set["roles"] == 3 && (int)set["steps"] == 2 && (int)set["tracks"] == 1 && (int)set["keys"] == 2 && (int)set["events"] == 1,
            "JS1: script.set: a new id, 3 roles, 2 steps, 1 track, 2 keys, 1 timed command (the build-form track converted)", Ctx.Short(c.LastText));
        c.Check(W1UndoCount(c) == undo + 1 && W1TopLabel(c) == StudioOps.Labels.ScriptAdd, "JS1: script.set: one entry «Add script»", W1TopLabel(c));
        var list = c.Ok(new { cmd = "script.list" }, "JS1: script.list");
        C50Order(c, list, "script.list", "JS1: script.list (the scene)", new[] { "scripts", "loaded" });
        var row = (list["scripts"] as JArray)?.OfType<JObject>().FirstOrDefault();
        c.Check(row != null && C50Names(row).SequenceEqual(C50SceneRow) && (string)row["script"] == id && (row["roles"] as JArray)?.Count == 3 && (row["uncast"] as JArray)?.Count == 3 &&
            ((JObject)row["cast"]).Count == 0 && row["prepared"].Type == JTokenType.Null && row.Value<bool>("loaded") == false && row["store"].Type == JTokenType.Null && list["loaded"].Type == JTokenType.Null,
            "JS1: script.list: Present: the script, {script, name, roles, cast, uncast, prepared, loaded, store}, every role uncast", Ctx.Short(c.LastText));
        var cast = c.Ok(new { cmd = "script.cast", script = id, roles = new Dictionary<string, object> { ["a"] = x.A, ["b"] = x.B, ["p"] = x.P } }, "JS1: script.cast");
        C50Order(c, cast, "script.cast", "JS1: script.cast");
        c.Check((string)cast["cast"]?["a"] == x.A && (string)cast["cast"]?["b"] == x.B && (string)cast["cast"]?["p"] == x.P && (cast["uncast"] as JArray)?.Count == 0 && W1TopLabel(c) == StudioOps.Labels.ScriptCast,
            "JS1: script.cast: the three roles cast, one entry «Cast roles»", Ctx.Short(c.LastText));
        var get = c.Ok(new { cmd = "script.get", script = id }, "JS1: script.get");
        C50Order(c, get, "script.get", "JS1: script.get");
        var gd = get["script"] as JObject;
        c.Check(gd != null && (string)gd["format"] == SceneScript.FormatName && (string)gd["id"] == id && gd["cast"] == null && gd["prepared"] == null && (string)get["cast"]?["a"] == x.A &&
            ((JArray)gd["setup"])?[0]?["note"]?.Value<string>() == "stand", "JS1: script.get: the document (no cast, mark or store inside), its notes kept, the cast beside it", Ctx.Short(c.LastText));
        var keep = c.Ok(new { cmd = "script.keep", script = id }, "JS1: script.keep");
        C50Order(c, keep, "script.keep", "JS1: script.keep");
        int no = (int?)keep["no"] ?? -1;
        c.Ok(new { cmd = "script.list", store = true }, "JS1: script.list store");
        var store = C50Raw(c);
        C50Order(c, store, "script.list", "JS1: script.list (the store)", new[] { "rows", "lastNumber" });
        var srow = (store["rows"] as JArray)?.OfType<JObject>().FirstOrDefault();
        c.Check(no == ScriptStore.FirstNumber && srow != null && C50Names(srow).SequenceEqual(C50StoreRow) && (int)srow["no"] == no && (string)srow["id"] == id && (int)store["lastNumber"] == no &&
            ((string)srow["created"]).EndsWith("Z", StringComparison.Ordinal), "JS1: script.list store: Present: the kept row, {no, id, name, created, modified, deleted}", Ctx.Short(c.LastText));
        var add = c.Ok(new { cmd = "script.add", no }, "JS1: script.add");
        C50Order(c, add, "script.add", "JS1: script.add");
        string added = (string)add["script"];
        c.Check(added != null && added != id && C50Script1(x, added)?.Store == no && C50Warnings(add).Any(w => w.Contains(id)), "JS1: script.add: a copy with a new id (the scene holds the row's id, with a warning), its store number", Ctx.Short(c.LastText));
        var exp = c.Ok(new { cmd = "script.export", script = id, path = "c50/replies" }, "JS1: script.export");
        C50Order(c, exp, "script.export", "JS1: script.export");
        string path = (string)exp["path"];
        c.Check(path != null && path.EndsWith(SceneScript.FileExtension, StringComparison.Ordinal) && File.Exists(path) && new FileInfo(path).Length == (long)exp["bytes"],
            "JS1: script.export: the file with the extension added, its size", Ctx.Short(c.LastText));
        var imp = c.Ok(new { cmd = "script.import", path }, "JS1: script.import (to the store)");
        C50Order(c, imp, "script.import", "JS1: script.import to store", new[] { "no" });
        var imps = c.Ok(new { cmd = "script.import", path, to = "scene" }, "JS1: script.import to the scene");
        C50Order(c, imps, "script.import", "JS1: script.import to scene", new[] { "script" });
        var del = c.Ok(new { cmd = "script.delete", no = (int)imp["no"] }, "JS1: script.delete");
        C50Order(c, del, "script.delete", "JS1: script.delete");
        var purge = c.Ok(new { cmd = "script.purge" }, "JS1: script.purge");
        C50Order(c, purge, "script.purge", "JS1: script.purge");
        c.Check((int)purge["rows"] == 1 && (int)purge["files"] == 1 && (long)purge["bytes"] > 0, "JS1: script.purge: the deleted row and its file", Ctx.Short(c.LastText));
        var rem = c.Ok(new { cmd = "script.remove", script = (string)imps["script"] }, "JS1: script.remove");
        C50Order(c, rem, "script.remove", "JS1: script.remove");
        c.Check((string)rem["removed"] == (string)imps["script"] && W1TopLabel(c) == StudioOps.Labels.ScriptRemove, "JS1: script.remove: the id, one entry «Remove script»", Ctx.Short(c.LastText));
        var bor = c.Ok(new { cmd = "script.borrow", script = id, from = added, window = new[] { 0, 1 } }, "JS1: script.borrow");
        C50Order(c, bor, "script.borrow", "JS1: script.borrow");
        c.Ok(new { cmd = "scene.new" }, "(a new scene)");
    }

    // ---------------------------------------------------------------- JS2: errors, id, naming

    static void C50Errors(Ctx c, C50Fix x)
    {
        c.Ok(new { cmd = "scene.new" }, "(a new scene)");
        string id1 = C50Set(c, C50Doc("twin"), "JS2: a script named twin");
        string id2 = C50Set(c, C50Doc("twin"), "JS2: a second named twin");
        string id3 = C50Set(c, C50Doc("solo"), "JS2: a script named solo");
        // by id, by unique name, by index
        string byId = (string)c.Ok(new { cmd = "script.get", script = id3 }, "JS2: by id")["script"]?["id"];
        string byName = (string)c.Ok(new { cmd = "script.get", script = "solo" }, "JS2: by name")["script"]?["id"];
        string byIndex = (string)c.Ok(new { cmd = "script.get", script = 2 }, "JS2: by index")["script"]?["id"];
        c.Check(byId == id3 && byName == id3 && byIndex == id3, "JS2: script by id, by its unique name and by its index names the same script", byId + " " + byName + " " + byIndex);
        var amb = c.Call(new { cmd = "script.get", script = "twin" });
        c.Check((string)amb["code"] == BridgeCodes.NotFound && (amb["detail"]?["candidates"] as JArray)?.Select(t => (string)t).OrderBy(s => s).SequenceEqual(new[] { id1, id2 }.OrderBy(s => s)) == true,
            "JS2: a name two scripts hold: not_found with both ids as candidates", Ctx.Short(c.LastText));
        C45Refused(c, new { cmd = "script.get", script = "nothing like it" }, "not_found", "script", "JS2: script.get of no such script");
        C45Refused(c, new { cmd = "script.get", script = 3 }, "not_found", "script", "JS2: script.get of an index outside");
        C45Refused(c, new { cmd = "script.get", script = true }, "bad_args", "script", "JS2: script.get of a boolean");
        C45Refused(c, new { cmd = "script.get" }, "bad_args", "script", "JS2: script.get with neither script nor no");
        C45Refused(c, new { cmd = "script.get", script = 0, no = 1 }, "bad_args", "script", "JS2: script.get with script and no");
        C45Refused(c, new { cmd = "script.get", no = 999 }, "not_found", "no", "JS2: script.get of a store row that is none");
        // id stays the envelope's
        var env = c.Ok(new { cmd = "script.get", script = 2, id = "c50" }, "JS2: script.get with an envelope id");
        c.Check((string)env["id"] == "c50" && (string)env["script"]?["id"] == id3, "JS2: id is echoed; script names the script", Ctx.Short(c.LastText));
        C45Refused(c, new { cmd = "script.get", id = id3 }, "bad_args", "script", "JS2: id never names a script");
        C45Refused(c, new { cmd = "script.delete", id = 1 }, "bad_args", "no", "JS2: id never names a store row");
        // documents
        var other = C50Doc("x");
        other["format"] = "another.format";
        C45Refused(c, new { cmd = "script.set", script = other }, "bad_args", "script", "JS2: another format");
        c.Check(((string)c.Call(new { cmd = "script.set", script = other })["error"] ?? "").Contains(ScriptFindings.BadFormat), "JS2: another format names script.badFormat", Ctx.Short(c.LastText));
        var newer = C50Doc("x");
        newer["version"] = 2;
        c.Check(((string)c.Call(new { cmd = "script.set", script = newer })["error"] ?? "").Contains(ScriptFindings.NewerVersion), "JS2: a newer version names script.newerVersion", Ctx.Short(c.LastText));
        C45Refused(c, new { cmd = "script.set", script = "a text" }, "bad_args", "script", "JS2: script.set of a text");
        C45Refused(c, new { cmd = "script.set" }, "bad_args", "script", "JS2: script.set without a document");
        C45Refused(c, new { cmd = "script.set", script = C50Doc("x", null, "[{\"role\":\"a b\",\"kind\":\"chara\"}]") }, "bad_args", "script", "JS2: a role name that is none");
        var roles33 = new JArray();
        for (int i = 0; i < ScriptLimits.MaxRoles + 1; i++) roles33.Add(new JObject { ["role"] = "r" + i, ["kind"] = "chara" });
        var big = C50Doc("x");
        big["roles"] = roles33;
        var lim = c.Call(new { cmd = "script.set", script = big });
        c.Check((string)lim["code"] == BridgeCodes.BadArgs && ((string)lim["error"] ?? "").Contains(ScriptFindings.Limit), "JS2: 33 roles: bad_args naming script.limit", Ctx.Short(c.LastText));
        Directory.CreateDirectory(C50Out(c));
        string bigFile = Path.Combine(C50Out(c), "roles33" + SceneScript.FileExtension);
        File.WriteAllText(bigFile, big.ToString(Formatting.None), new System.Text.UTF8Encoding(false));
        var kept = c.Ok(new { cmd = "script.import", path = bigFile, to = "scene" }, "JS2: script.import of a file with 33 roles");
        string keptId = (string)kept["script"];
        c.Check(keptId != null && C50Script1(x, keptId)?.Roles.Count == ScriptLimits.MaxRoles && C50Warnings(kept).Any(w => w.StartsWith(ScriptFindings.Limit, StringComparison.Ordinal)),
            "JS2: a file keeps the reader's repair: the script imported with 32 roles and the warning script.limit", Ctx.Short(c.LastText));
        File.Delete(bigFile);
        c.Ok(new { cmd = "script.remove", script = keptId, undo = false }, "(the imported script removed)");
        C45Refused(c, new { cmd = "script.set", script = C50Doc("x", "0123456789abcdef0123456789abcdef"), replace = true }, "not_found", "script", "JS2: replace of an id the scene does not hold");
        // the limit of scripts per scene
        int n0 = C50Count(x);
        for (int i = n0; i < ScriptLimits.MaxScripts; i++)
        {
            var r = c.Call(new { cmd = "script.set", script = C50Doc("fill " + i), undo = false });
            if (r.Value<bool?>("ok") != true) { c.Check(false, "JS2: filling the scene to " + ScriptLimits.MaxScripts + " scripts", Ctx.Short(c.LastText)); break; }
        }
        c.Check(C50Count(x) == ScriptLimits.MaxScripts, "JS2: Present: the scene holds " + ScriptLimits.MaxScripts + " scripts", C50Count(x) + "");
        var over = c.Call(new { cmd = "script.set", script = C50Doc("one too many") });
        c.Check((string)over["code"] == BridgeCodes.BadArgs && ((string)over["error"] ?? "").Contains(ScriptFindings.Limit) && C50Count(x) == ScriptLimits.MaxScripts,
            "JS2: the 65th script: bad_args naming script.limit, nothing added", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "scene.new" }, "(a new scene)");
        // cast
        string cs = C50Set(c, C50Doc("cast", null, "[{\"role\":\"a\",\"kind\":\"chara\",\"sex\":\"male\"},{\"role\":\"p\",\"kind\":\"item\",\"ref\":\"" + x.PRef + "\"}]"), "JS2: a script to cast");
        string chara = C30Load(c, c.FemaleRel, "female", "JS2: BR-Chan");
        string item = W1AddItem(c, "JS2: an item");
        C45Refused(c, new { cmd = "script.cast", script = cs, roles = new Dictionary<string, object> { ["z"] = chara } }, "bad_args", "roles.z", "JS2: a role the script does not declare");
        C45Refused(c, new { cmd = "script.cast", script = cs, roles = new Dictionary<string, object> { ["a"] = item } }, "bad_args", "roles.a", "JS2: a character role cast to an item");
        C45Refused(c, new { cmd = "script.cast", script = cs, roles = new Dictionary<string, object> { ["p"] = chara } }, "bad_args", "roles.p", "JS2: a prop role cast to a character");
        C45Refused(c, new { cmd = "script.cast", script = cs, roles = new Dictionary<string, object> { ["a"] = "0123456789abcdef0123456789abcdef" } }, "not_found", "roles.a", "JS2: a node that is none");
        C45Refused(c, new { cmd = "script.cast", script = cs, roles = new Dictionary<string, object> { ["a"] = 5 } }, "bad_args", "roles.a", "JS2: a number as a key");
        C45Refused(c, new { cmd = "script.cast", script = cs }, "bad_args", "roles", "JS2: script.cast without roles");
        var sex = c.Ok(new { cmd = "script.cast", script = cs, roles = new Dictionary<string, object> { ["a"] = chara, ["p"] = item } }, "JS2: a male role cast to BR-Chan");
        c.Check(C50Warnings(sex).Any(w => w.Contains("cast anyway")), "JS2: the other sex is cast with a warning", Ctx.Short(c.LastText));
        var un = c.Ok(new { cmd = "script.cast", script = cs, roles = new Dictionary<string, object> { ["a"] = null } }, "JS2: null uncasts a role");
        c.Check((un["uncast"] as JArray)?.Select(t => (string)t).SequenceEqual(new[] { "a" }) == true && (string)un["cast"]?["p"] == item, "JS2: a uncast, p cast", Ctx.Short(c.LastText));
        // the store and files
        C45Refused(c, new { cmd = "script.add", no = 999 }, "not_found", "no", "JS2: script.add of a row that is none");
        C45Refused(c, new { cmd = "script.delete", no = 999 }, "not_found", "no", "JS2: script.delete of a row that is none");
        C45Refused(c, new { cmd = "script.import", path = "c50/none" + SceneScript.FileExtension }, "not_found", "path", "JS2: script.import of a file that is none");
        C45Refused(c, new { cmd = "script.import", path = Path.Combine(c.Out, "x.json") }, "io_denied", "path", "JS2: script.import of an absolute file of another kind");
        C45Refused(c, new { cmd = "script.import", path = "c50/x", to = "elsewhere" }, "bad_args", "to", "JS2: script.import to a place that is none");
        C45Refused(c, new { cmd = "script.export", script = cs, path = Path.Combine(Path.GetTempPath(), "c50_outside" + SceneScript.FileExtension) }, "io_denied", "path", "JS2: script.export outside the output root and the user data");
        C45Refused(c, new { cmd = "script.export", script = cs, path = "../c50_up" }, "io_denied", "path", "JS2: script.export above the output root");
        Directory.CreateDirectory(C50Out(c));
        string huge = Path.Combine(C50Out(c), "huge" + SceneScript.FileExtension);
        using (var fs = new FileStream(huge, FileMode.Create)) fs.SetLength(ScriptLimits.MaxFileBytes + 1L);
        var hr = c.Call(new { cmd = "script.import", path = huge, to = "scene" });
        c.Check((string)hr["code"] == BridgeCodes.BadArgs && ((string)hr["error"] ?? "").Contains(ScriptFindings.Limit), "JS2: a file over 16 MiB: bad_args naming script.limit", Ctx.Short(c.LastText));
        File.Delete(huge);
        // a store that cannot be written: a file where its folder would be
        C50Clear(c);
        File.WriteAllText(C50Store(c), "not a folder");
        var sw = c.Call(new { cmd = "script.keep", script = cs });
        c.Check((string)sw["code"] == BridgeCodes.Unavailable && ((string)sw["error"] ?? "").Contains(ScriptFindings.StoreWrite), "JS2: the store unwritable: unavailable naming script.storeWrite", Ctx.Short(c.LastText));
        File.Delete(C50Store(c));
        c.Info("JS2: busy (a recording of another caller) needs a graphics device: not run here (the player's refusal, P's rows)");
        c.Ok(new { cmd = "scene.new" }, "(a new scene)");
    }

    // ---------------------------------------------------------------- JS8: refused steps and timed commands

    static void C50Steps(Ctx c, C50Fix x)
    {
        var cases = new (string what, string setup, string prepare, string events, string path)[]
        {
            ("a set-up step of the group script", "[{\"cmd\":\"camera.get\"},{\"cmd\":\"script.play\",\"script\":0}]", null, null, "setup[1]"),
            ("a preparation step of the group script", null, "[{\"cmd\":\"script.list\"}]", null, "prepare[0]"),
            ("a set-up step that builds the Timeline", "[{\"cmd\":\"timeline.build\",\"tracks\":[]}]", null, null, "setup[0]"),
            ("a preparation step that sets the Timeline", null, "[{\"cmd\":\"timeline.set\",\"duration\":3}]", null, "prepare[0]"),
            ("a set-up step that clears the Timeline", "[{\"cmd\":\"timeline.clear\"}]", null, null, "setup[0]"),
            ("a set-up step that adds a track", "[{\"cmd\":\"timeline.track.add\",\"type\":\"camera\"}]", null, null, "setup[0]"),
            ("a set-up step that sets a key", "[{\"cmd\":\"timeline.key.set\",\"track\":0,\"t\":1}]", null, null, "setup[0]"),
            ("a set-up step that replaces the timed commands", "[{\"cmd\":\"timeline.events\",\"events\":[]}]", null, null, "setup[0]"),
            ("a step with an id", "[{\"cmd\":\"camera.get\",\"id\":3}]", null, null, "setup[0]"),
            ("a step without a command", "[{\"key\":\"x\"}]", null, null, "setup[0]"),
            ("a timed command of the group script", null, null, "[{\"t\":1,\"do\":{\"cmd\":\"script.stop\"}}]", "timeline.events[0].do"),
            ("a timed command that builds the Timeline", null, null, "[{\"t\":0.5,\"do\":{\"cmd\":\"camera.get\"}},{\"t\":1,\"do\":{\"cmd\":\"timeline.build\",\"tracks\":[]}}]", "timeline.events[1].do"),
            ("a timed command that clears the Timeline", null, null, "[{\"t\":1,\"do\":{\"cmd\":\"timeline.clear\"}}]", "timeline.events[0].do"),
        };
        foreach (var k in cases)
        {
            var req = new { cmd = "script.set", script = C50Doc("refused", null, null, k.setup, k.prepare, null, k.events) };
            C45Refused(c, req, "bad_args", "script", "JS8: " + k.what);
            var r = c.Call(req);
            c.Check((string)r["detail"]?["path"] == k.path && ((string)r["error"] ?? "").Contains(k.path), "JS8: " + k.what + ": detail.path and the text name " + k.path, Ctx.Short(c.LastText));
        }
        string ok = C50Set(c, C50Doc("allowed", null, null, "[{\"wait\":0.5},{\"cmd\":\"camera.get\"}]", null, null, "[{\"t\":1,\"do\":{\"cmd\":\"camera.get\"}}]"),
            "JS8 (control): a wait step, a plain step and a plain timed command are taken");
        c.Check(ok != null && C50Count(x) == 1, "JS8 (control): Present: the allowed script is in the scene");
        c.Ok(new { cmd = "scene.new" }, "(a new scene)");
    }

    // ---------------------------------------------------------------- JE3 (the import's warning), JC8 (the import's warning)

    static void C50Warnings(Ctx c, C50Fix x)
    {
        Directory.CreateDirectory(C50Out(c));
        var doc = C50Doc("warned", "a50a50a50a50a50a50a50a50a50a50a5", null,
            "[{\"cmd\":\"item.add\",\"no\":399},{\"cmd\":\"capture\",\"width\":64}]",
            "[{\"cmd\":\"plugin.set\",\"plugin\":\"x\",\"values\":{\"y\":1}}]", null,
            "[{\"t\":1,\"do\":{\"cmd\":\"plugin.call\",\"plugin\":\"x\",\"method\":\"y\"}},{\"t\":2,\"do\":{\"cmd\":\"motion.play\",\"key\":\"$a\"}}]");
        string file = Path.Combine(C50Out(c), "warned" + SceneScript.FileExtension);
        File.WriteAllText(file, doc.ToString(Formatting.None), new System.Text.UTF8Encoding(false));
        var reg = x.App.BridgeServices.Commands;
        c.Check(reg.Find("plugin.call") == null && reg.Find("plugin.set") == null && reg.Find("motion.play") == null, "JC8: plugin.call, plugin.set and motion.play are not commands of the app");
        int undo = W1UndoCount(c);
        var r = c.Ok(new { cmd = "script.import", path = file, to = "scene" }, "JE3/JC8: script.import of a script with such steps");
        var w = C50Warnings(r);
        int unknown = w.Count(s => s.StartsWith(ScriptFindings.UnknownCommand, StringComparison.Ordinal));
        c.Check(unknown == 3 && new[] { "plugin.call", "plugin.set", "motion.play" }.All(n => w.Any(s => s.StartsWith(ScriptFindings.UnknownCommand, StringComparison.Ordinal) && s.Contains(n))),
            "JC8: the import warns script.unknownCommand for plugin.set (preparation), plugin.call and motion.play (timed commands): " + unknown, string.Join(" | ", w));
        c.Check(w.Count(s => s.StartsWith(ScriptFindings.ClearsHistory, StringComparison.Ordinal) && s.Contains("item.add")) == 1, "JE3: the import warns script.clearsHistory for the set-up step item.add", string.Join(" | ", w));
        c.Check(w.Count(s => s.StartsWith(StudioOps.ScriptOpFindings.DeferringStep, StringComparison.Ordinal) && s.Contains("capture")) == 1, "JE3: the import warns script.deferringStep for the set-up step capture", string.Join(" | ", w));
        c.Check(C50Count(x) == 1 && W1UndoCount(c) == undo + 1 && W1TopLabel(c) == StudioOps.Labels.ScriptAdd, "JE3/JC8: Present: the script is in the scene (warned, not refused), one entry «Add script»");
        var s2 = c.Ok(new { cmd = "script.set", script = doc, replace = true }, "JE3: script.set of the same document (replace)");
        c.Check(C50Warnings(s2).Count(s => s.StartsWith(ScriptFindings.ClearsHistory, StringComparison.Ordinal)) == 1 && C50Warnings(s2).Count(s => s.StartsWith(ScriptFindings.UnknownCommand, StringComparison.Ordinal)) == 3 &&
            W1TopLabel(c) == StudioOps.Labels.ScriptEdit, "JE3: script.set warns the same, one entry «Edit script»", string.Join(" | ", C50Warnings(s2)));
        File.Delete(file);
        c.Ok(new { cmd = "scene.new" }, "(a new scene)");
    }

    // ---------------------------------------------------------------- JS5: build form through script.set

    static string C50Blank(JToken t)
    {
        var o = (JObject)t.DeepClone();
        o["id"] = "-";
        return o.ToString(Formatting.None);
    }

    static void C50BuildForm(Ctx c, C50Fix x)
    {
        if (!C50Fixture(c, x, "JS5")) return;
        var doc = C50Doc("build form", null,
            "[{\"role\":\"a\",\"kind\":\"chara\",\"sex\":\"female\"},{\"role\":\"b\",\"kind\":\"chara\",\"sex\":\"male\"},{\"role\":\"p\",\"kind\":\"item\",\"ref\":\"" + x.PRef + "\"}]",
            null, null, C50BuildTracks, "[{\"t\":0.5,\"do\":{\"cmd\":\"obj.transform\",\"key\":\"$b\",\"pos\":[5,0,1]}}]", 3);
        int buildForm = ScriptFormat.BuildFormCount(doc);
        string id = C50Set(c, doc, "JS5: script.set with build-form tracks and a timed command");
        if (id == null) return;
        var stored = (c.Ok(new { cmd = "script.get", script = id }, "JS5: script.get")["script"] as JObject)?["timeline"] as JObject;
        var tracks = (stored?["tracks"] as JArray)?.ToList() ?? new List<JToken>();
        var storedEvents = stored?["events"] as JArray;
        c.Check(buildForm == 15 && tracks.Count == 14 && tracks.All(t => JsonText.IsId((string)t["id"])) && storedEvents?.Count == 1 && JsonText.IsId((string)storedEvents[0]["id"]),
            "JS5: Present: " + buildForm + " build-form elements converted into 14 stored tracks and a timed command, each with an id", buildForm + " / " + tracks.Count);
        string withIds = C50BuildTracks.Replace("\"$a\"", "\"" + x.A + "\"").Replace("\"$b\"", "\"" + x.B + "\"").Replace("\"$p\"", "\"" + x.P + "\"");
        c.Ok(new JObject { ["cmd"] = "timeline.build", ["duration"] = 3, ["loop"] = true, ["play"] = false, ["tracks"] = JArray.Parse(withIds) }, "JS5: timeline.build of the same input with the node ids");
        var built = ((c.Ok(new { cmd = "timeline.get" }, "JS5: timeline.get")["timeline"] as JObject)?["tracks"] as JArray)?.ToList() ?? new List<JToken>();
        int same = 0;
        var diffs = new List<string>();
        for (int i = 0; i < Math.Min(tracks.Count, built.Count); i++)
        {
            string a = C50Blank(tracks[i]).Replace("\"$a\"", "\"" + x.A + "\"").Replace("\"$b\"", "\"" + x.B + "\"").Replace("\"$p\"", "\"" + x.P + "\""), b = C50Blank(built[i]);
            if (a == b) same++;
            else diffs.Add(i + ": " + Ctx.Short(a) + " / " + Ctx.Short(b));
        }
        c.Check(tracks.Count == built.Count && same == built.Count, "JS5: every stored track of the script, its roles as the cast's nodes, equals timeline.build's stored track (" + same + " of " + built.Count + ", ids aside)", string.Join(" | ", diffs.Take(3)));
        c.Ok(new { cmd = "timeline.clear", undo = false }, "(the Timeline cleared)");
        c.Ok(new { cmd = "scene.new" }, "(a new scene)");
    }

    // ---------------------------------------------------------------- JS3: borrow

    static double[] C50Times(JObject track) => (track?["keys"] as JArray)?.Select(k => (double)k["t"]).ToArray() ?? new double[0];

    static void C50Borrow(Ctx c, C50Fix x)
    {
        string target = C50Set(c, C50Doc("target", null, null, null, null,
            "[{\"type\":\"pos\",\"key\":\"$a\",\"keys\":[{\"t\":0,\"v\":[0,0,0]},{\"t\":2,\"v\":[0,0,2]},{\"t\":4,\"v\":[0,0,4]}]}]",
            "[{\"t\":1,\"do\":{\"cmd\":\"camera.get\"}}]", 10), "JS3: the target script (a, b)");
        string source = C50Set(c, C50Doc("source", null, "[{\"role\":\"x\",\"kind\":\"chara\",\"sex\":\"female\"},{\"role\":\"b\",\"kind\":\"chara\",\"sex\":\"male\"}]", null, null,
            "[{\"type\":\"pos\",\"key\":\"$x\",\"keys\":[{\"t\":1,\"v\":[1,0,1]},{\"t\":2,\"v\":[9,0,9]},{\"t\":3,\"v\":[3,0,3]}]}," +
            "{\"type\":\"camera\",\"keys\":[{\"t\":1,\"target\":[0,10,0],\"rot\":[10,180,0],\"dist\":25,\"fov\":23},{\"t\":2,\"dist\":20}]}," +
            "{\"type\":\"dof\",\"keys\":[{\"t\":1.5,\"on\":true,\"focus\":\"$x\",\"sharp\":0.2,\"full\":4}]}]",
            "[{\"t\":0.5,\"do\":{\"cmd\":\"obj.transform\",\"key\":\"$x\",\"pos\":[0,0,5]}},{\"t\":1.5,\"do\":{\"cmd\":\"obj.transform\",\"key\":\"$x\",\"pos\":[1,0,5]},\"note\":\"step\"}," +
            "{\"t\":2.5,\"do\":{\"cmd\":\"chara.anime\",\"key\":\"$b\",\"group\":19,\"category\":0,\"no\":0}},{\"t\":3.5,\"do\":{\"cmd\":\"camera.get\",\"lit\":\"$$x\"}}]", 10),
            "JS3: the source script (x, b)");
        if (target == null || source == null) return;
        // an undeclared role refuses the whole borrow
        C45Refused(c, new { cmd = "script.borrow", script = target, from = source, window = new[] { 1, 3 }, shift = 1 }, "bad_args", "roles", "JS3: a role the target does not declare (x)");
        C45Refused(c, new { cmd = "script.borrow", script = target, from = source, window = new[] { 1, 3 }, shift = 20, roles = new Dictionary<string, object> { ["x"] = "a" } }, "bad_args", "shift", "JS3: borrowed times past the target's length");
        C45Refused(c, new { cmd = "script.borrow", script = target, from = source, window = new[] { 3, 1 }, roles = new Dictionary<string, object> { ["x"] = "a" } }, "bad_args", "window", "JS3: a window that holds no time");
        C45Refused(c, new { cmd = "script.borrow", script = target, from = source, no = 1 }, "bad_args", "from", "JS3: from and no together");
        var tBefore = C50Script1(x, target).Timeline;
        var srcEvents = C50Script1(x, source).Timeline.Events.Select(e => e.Id).ToList();
        int undo = W1UndoCount(c);
        string before = C50Scripts(x);
        var r = c.Ok(new { cmd = "script.borrow", script = target, from = source, window = new[] { 1, 3 }, shift = 1, roles = new Dictionary<string, object> { ["x"] = "a" } }, "JS3: borrow [1, 3) shifted by 1, x as a");
        c.Check((int)r["events"] == 2 && (int)r["keys"] == 2 && (int)r["tracks"] == 1 && (int)r["replaced"] == 1,
            "JS3: Present: 2 timed commands, 2 keys into 1 track, 1 key replaced (the target's key at 2)", Ctx.Short(c.LastText));
        var t = C50Script1(x, target).Timeline;
        var pos = t.Tracks.FirstOrDefault(k => k.Type == "pos" && k.Key == "$a");
        c.Check(pos != null && pos.Keys.Select(k => (double)k.T).SequenceEqual(new double[] { 0, 2, 3, 4 }) && pos.Keys[1].Vector.X == 1f && pos.Keys[2].Vector.X == 9f,
            "JS3: the pos track of a: keys at 0, 2, 3, 4; at 2 the borrowed key (from 1), at 3 the one from 2", pos == null ? "no track" : string.Join(", ", pos.Keys.Select(k => k.T + ":" + k.Vector.X)));
        c.Check(t.Tracks.Count == 1, "JS3: without camera no camera or depth-of-field track is borrowed", t.Tracks.Count + "");
        var evs = t.Events.Skip(1).ToList();
        c.Check(t.Events.Count == 3 && evs.Select(e => (double)e.T).SequenceEqual(new double[] { 2.5, 3.5 }) && (string)evs[0].Do["key"] == "$a" && (string)evs[1].Do["key"] == "$b" && evs[0].Note == "step" &&
            evs.All(e => JsonText.IsId(e.Id) && !srcEvents.Contains(e.Id)), "JS3: the timed commands at 2.5 and 3.5, x renamed a, b kept, the note kept, new ids", string.Join(", ", t.Events.Select(e => e.T + " " + e.Do)));
        c.Check(W1UndoCount(c) == undo + 1 && W1TopLabel(c) == StudioOps.Labels.ScriptBorrow, "JS3: one entry «Borrow»");
        c.Ok(new { cmd = "undo" }, "JS3: undo");
        c.Check(C50Scripts(x) == before, "JS3: undo restores the scripts");
        var rc = c.Ok(new { cmd = "script.borrow", script = target, from = source, window = new[] { 1, 4 }, roles = new Dictionary<string, object> { ["x"] = "a" }, camera = true, events = false }, "JS3: borrow [1, 4) with camera, no timed commands");
        var t2 = C50Script1(x, target).Timeline;
        var dof = t2.Tracks.FirstOrDefault(k => k.Type == "dof");
        c.Check((int)rc["events"] == 0 && (int)rc["tracks"] == 3 && t2.Tracks.Any(k => k.Type == "camera") && dof?.Keys[0].Dof?.Focus?.Node == "$a" && t2.Events.Count == 1,
            "JS3: with camera the camera and depth-of-field tracks come along, the focus renamed; events false borrows no timed command", Ctx.Short(c.LastText));
        var lit = c.Ok(new { cmd = "script.borrow", script = target, from = source, window = new[] { 3.5, 4 }, roles = new Dictionary<string, object> { ["x"] = "a" }, tracks = false }, "JS3: a timed command with \"$$x\"");
        var last = C50Script1(x, target).Timeline.Events.Last();
        c.Check((int)lit["events"] == 1 && (string)last.Do["lit"] == "$$x", "JS3: \"$$x\" (a literal) is not a role and is kept", last.Do.ToString(Formatting.None));
        // from a store row
        int no = (int)c.Ok(new { cmd = "script.keep", script = source }, "JS3: the source kept in the store")["no"];
        var rs = c.Ok(new { cmd = "script.borrow", script = target, no, window = new[] { 0, 1 }, roles = new Dictionary<string, object> { ["x"] = "a" } }, "JS3: borrow from a store row");
        c.Check((int)rs["events"] == 1, "JS3: from the store row: the timed command at 0.5", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "scene.new" }, "(a new scene)");
        C50Clear(c);
    }

    // ---------------------------------------------------------------- JS6: status while playing, paused, stopped; JS1 of play, stop, status, capture

    static void C50Status(Ctx c, C50Fix x)
    {
        if (!C50Fixture(c, x, "JS6")) return;
        string id = C50Set(c, C50Doc("status", null, null, "[{\"cmd\":\"camera.get\"}]", null,
            "[{\"type\":\"pos\",\"key\":\"$a\",\"keys\":[{\"t\":0,\"v\":[0,0,0]},{\"t\":3,\"v\":[0,0,3]}]}]",
            "[{\"t\":0.1,\"do\":{\"cmd\":\"camera.get\"}}]", 4), "JS6: a script to play");
        c.Ok(new { cmd = "script.cast", script = id, roles = new Dictionary<string, object> { ["a"] = x.A, ["b"] = x.B } }, "JS6: cast");
        var idle = c.Ok(new { cmd = "script.status" }, "JS6: script.status before a play");
        C50Order(c, idle, "script.status", "JS6: script.status before a play");
        var play = c.Ok(new { cmd = "script.play", script = id, load = false }, "JS6: script.play");
        C50Order(c, play, "script.play", "JS1: script.play", C50Returns["script.play"].Take(10).ToArray());
        c.Check((string)play["script"] == id && (string)play["state"] == "playing" && play.Value<bool>("loaded") && (int)play["setup"]["sent"] == 1 && (play["failures"] as JArray)?.Count == 0,
            "JS1: script.play: loaded, playing, one set-up step sent, no failure", Ctx.Short(c.LastText));
        c.Frames(30);
        var st = c.Ok(new { cmd = "script.status" }, "JS6: script.status while playing");
        C50Order(c, st, "script.status", "JS6: script.status while playing");
        c.Check((string)st["script"] == id && (string)st["name"] == "status" && (string)st["state"] == "playing" && (double)st["duration"] == 4 && st.Value<bool>("loop") &&
            (string)st["roles"]?["a"] == x.A && (st["uncast"] as JArray)?.Count == 0 && st["lastPlay"] is JObject lp && C50Names(lp).SequenceEqual(new[] { "setup", "prepare", "skipped", "failures" }) &&
            st["slots"] is JObject sl && C50Names(sl).SequenceEqual(new[] { "moves", "follow", "dof" }) && st.Value<bool>("recording") == false,
            "JS6: playing: the script, its name, state playing, length 4, loop, the cast, lastPlay {setup, prepare, skipped, failures}, slots {moves, follow, dof}, not recording", Ctx.Short(c.LastText));
        c.Info("JS6: while playing time " + (double)st["time"] + ", timed commands fired " + (int)st["eventsFired"] + ", errors " + (int)st["eventErrors"]);
        c.Ok(new { cmd = "timeline.pause" }, "JS6: timeline.pause");
        var ps = c.Ok(new { cmd = "script.status" }, "JS6: script.status paused");
        c.Check((string)ps["state"] == "paused" && (string)ps["script"] == id, "JS6: paused: state paused, the same script", Ctx.Short(c.LastText));
        var cap = c.Ok(new { cmd = "script.capture" }, "JS1: script.capture of the loaded Timeline");
        C50Order(c, cap, "script.capture", "JS1: script.capture");
        c.Check((string)cap["script"] == id && (int)cap["tracks"] == 1 && (int)cap["events"] == 1, "JS1: script.capture: the script, its track and timed command", Ctx.Short(c.LastText));
        var stop = c.Ok(new { cmd = "script.stop" }, "JS6: script.stop");
        C50Order(c, stop, "script.stop", "JS1: script.stop");
        var ss = c.Ok(new { cmd = "script.status" }, "JS6: script.status after stop");
        c.Check((string)stop["state"] == "stopped" && (string)ss["state"] == "stopped" && (string)ss["script"] == id, "JS6: after stop: state stopped, the script still named", Ctx.Short(c.LastText));
        var again = c.Ok(new { cmd = "script.stop", reset = false }, "JS6: script.stop reset false");
        c.Check((int)again["ragdolls"] == 0 && (int)again["dried"] == 0, "JS6: stop without reset: nothing released or dried", Ctx.Short(c.LastText));
    }

    // ---------------------------------------------------------------- JS10: a whole Timeline set back loses its mark (the lead's check)

    static void C50TimelineSet(Ctx c, C50Fix x)
    {
        string id = C50Model(x).Timeline?.Script?.Id;
        if (!c.Check(id != null, "JS10: Present: the scene's Timeline carries the mark of the script it was loaded from (" + id + ")")) return;
        var got = c.Ok(new { cmd = "timeline.get" }, "JS10: timeline.get");
        var doc = got["timeline"] as JObject;
        c.Check((string)doc?["script"]?["id"] == id, "JS10: Present: timeline.get's document carries the mark", Ctx.Short(c.LastText));
        var loaded = c.Ok(new { cmd = "script.list" }, "JS10: script.list before");
        c.Check((string)loaded["loaded"] == id, "JS10: script.list names it loaded before the set", Ctx.Short(c.LastText));
        c.Ok(new JObject { ["cmd"] = "timeline.set", ["timeline"] = doc }, "JS10: timeline.set of that document");
        c.Check(C50Model(x).Timeline != null && C50Model(x).Timeline.Script == null, "JS10: the Timeline is set and has no mark", C50Model(x).Timeline?.ToJson());
        var after = c.Ok(new { cmd = "script.list" }, "JS10: script.list after");
        var row = (after["scripts"] as JArray)?.OfType<JObject>().FirstOrDefault(o => (string)o["script"] == id);
        c.Check(after["loaded"].Type == JTokenType.Null && row?.Value<bool>("loaded") == false && c.Ok(new { cmd = "script.get", script = id }, "JS10: script.get").Value<bool>("loaded") == false,
            "JS10: script.list and script.get no longer name it loaded", Ctx.Short(c.LastText));
        var st = c.Ok(new { cmd = "script.status" }, "JS10: script.status after");
        c.Info("JS10: script.status names " + (string)st["script"] + " (the script last played; the Timeline holds no mark)");
        c.Ok(new { cmd = "scene.new" }, "(a new scene)");
    }

    // ---------------------------------------------------------------- JS4: the store commands

    static void C50StoreCommands(Ctx c, C50Fix x)
    {
        C50Clear(c);
        string id = C50Set(c, C50Doc("kept"), "JS4: a script");
        int undo = W1UndoCount(c);
        int no1 = (int)c.Ok(new { cmd = "script.keep", script = id }, "JS4: script.keep")["no"];
        c.Check(no1 == 1 && W1UndoCount(c) == undo && C50Script1(x, id)?.Store == 1, "JS4: keep: row 1, no entry, the scene script's store number set", no1 + "");
        int again = (int)c.Ok(new { cmd = "script.keep", script = id }, "JS4: script.keep again")["no"];
        var rows = c.Ok(new { cmd = "script.list", store = true }, "JS4: list")["rows"] as JArray;
        c.Check(again == 1 && rows?.Count == 1, "JS4: keep again: the row of the script's id replaced (row 1, one row)", again + " / " + rows?.Count);
        string exported = (string)c.Ok(new { cmd = "script.export", no = 1, path = "c50/kept" + SceneScript.FileExtension }, "JS4: export of row 1")["path"];
        var imp1 = c.Ok(new { cmd = "script.import", path = exported }, "JS4: import without replace");
        int no2 = (int)imp1["no"];
        c.Check(no2 == 2 && C50Warnings(imp1).Any(w => w.Contains(id)), "JS4: import of the same id without replace: a copy, a new id, row 2", Ctx.Short(c.LastText));
        var imp2 = c.Ok(new { cmd = "script.import", path = exported, replace = true }, "JS4: import with replace");
        c.Check((int)imp2["no"] == 1, "JS4: import with replace: row 1 replaced", Ctx.Short(c.LastText));
        string added = (string)c.Ok(new { cmd = "script.add", no = no2 }, "JS4: script.add of row 2")["script"];
        c.Check(added != null && added != id && C50Script1(x, added)?.Store == no2 && W1TopLabel(c) == StudioOps.Labels.ScriptAdd, "JS4: add: a scene script from row 2, its store number, one entry «Add script»");
        var del = c.Ok(new { cmd = "script.delete", no = no2 }, "JS4: delete row 2");
        var live = c.Ok(new { cmd = "script.list", store = true }, "JS4: list")["rows"] as JArray;
        var all = c.Ok(new { cmd = "script.list", store = true, deleted = true }, "JS4: list deleted")["rows"] as JArray;
        c.Check(del.Value<bool>("deleted") && live?.Count == 1 && all?.Count == 2 && all.OfType<JObject>().Any(o => (int)o["no"] == no2 && o.Value<bool>("deleted")), "JS4: delete hides row 2; deleted lists it");
        var res = c.Ok(new { cmd = "script.delete", no = no2, restore = true }, "JS4: restore row 2");
        c.Check(res.Value<bool>("deleted") == false && (c.Ok(new { cmd = "script.list", store = true }, "JS4: list")["rows"] as JArray)?.Count == 2, "JS4: restore shows it again");
        c.Ok(new { cmd = "script.delete", no = no2 }, "JS4: delete row 2 again");
        string file2 = Path.Combine(C50Store(c), ScriptStore.FileNameOf(no2));
        var purge = c.Ok(new { cmd = "script.purge" }, "JS4: purge");
        c.Check((int)purge["rows"] == 1 && (int)purge["files"] == 1 && !File.Exists(file2) && File.Exists(Path.Combine(C50Store(c), ScriptStore.FileNameOf(1))),
            "JS4: Present: the purge removed 1 file (row 2's) and its row; row 1's file stays", Ctx.Short(c.LastText));
        int no3 = (int)c.Ok(new { cmd = "script.keep", script = added }, "JS4: keep the added script")["no"];
        c.Check(no3 == 3, "JS4: numbers are never given again: the next row is 3", no3 + "");
        // an unreadable index
        File.WriteAllText(Path.Combine(C50Store(c), ScriptStore.IndexName), "{ not json");
        var ul = c.Ok(new { cmd = "script.list", store = true }, "JS4: list with an unreadable index");
        c.Check((ul["rows"] as JArray)?.Count == 0 && C50Warnings(ul).Any(w => w.StartsWith(ScriptFindings.StoreUnreadable, StringComparison.Ordinal)), "JS4: an unreadable index: no row, warning script.storeUnreadable", Ctx.Short(c.LastText));
        var up = c.Ok(new { cmd = "script.purge" }, "JS4: purge with an unreadable index");
        c.Check((int)up["files"] == 0 && (int)up["rows"] == 0 && File.Exists(Path.Combine(C50Store(c), ScriptStore.FileNameOf(1))) && C50Warnings(up).Any(w => w.StartsWith(ScriptFindings.StoreUnreadable, StringComparison.Ordinal)),
            "JS4: purge with an unreadable index removes nothing, with the warning", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "script.keep", script = id }, "JS4: keep with an unreadable index");
        c.Check(File.Exists(Path.Combine(C50Store(c), ScriptStore.BadName)) && (c.Ok(new { cmd = "script.list", store = true }, "JS4: list")["rows"] as JArray)?.Count == 1,
            "JS4: the next write keeps the old index as index.json.bad and starts a new one", "");
        c.Ok(new { cmd = "scene.new" }, "(a new scene)");
        C50Clear(c);
    }

    // ---------------------------------------------------------------- JB4: a scene import carries its scripts

    static void C50Import(Ctx c, C50Fix x)
    {
        var s = x.App.Session;
        const string shared = "5c50aaaabbbbccccddddeeeeffff0001";
        string Roles(string pref) => "[{\"role\":\"p\",\"kind\":\"item\",\"ref\":\"" + pref + "\"}]";
        string Track = "[{\"type\":\"pos\",\"key\":\"$p\",\"keys\":[{\"t\":0,\"v\":[0,0,0]},{\"t\":2,\"v\":[0,1,0]}]}]";
        // the file: two items, two scripts (one with the id the importing scene also holds), one loaded
        c.Ok(new { cmd = "scene.new" }, "JB4: the file's scene");
        string i1 = W1AddItem(c, "JB4: item 1 of the file"), i2 = W1AddItem(c, "JB4: item 2 of the file");
        string f1 = C50Set(c, C50Doc("file one", shared, Roles(x.PRef), null, null, Track), "JB4: a script of the file with the shared id");
        string f2 = C50Set(c, C50Doc("file two", null, Roles(x.PRef), null, null, Track), "JB4: a second script of the file");
        c.Ok(new { cmd = "script.cast", script = f1, roles = new Dictionary<string, object> { ["p"] = i1 } }, "JB4: cast");
        c.Ok(new { cmd = "script.cast", script = f2, roles = new Dictionary<string, object> { ["p"] = i2 } }, "JB4: cast");
        c.Ok(new { cmd = "script.play", script = f2, load = false }, "JB4: the file's Timeline loaded from its second script");
        c.Ok(new { cmd = "script.stop" }, "(stopped)");
        c.Ok(new { cmd = "scene.save", name = "c50_import", overwrite = true, clock = "2026-11-08T00:00:00Z" }, "JB4: saved");
        // the importing scene: a script with the shared id, loaded
        c.Ok(new { cmd = "scene.new" }, "JB4: the importing scene");
        string mine = W1AddItem(c, "JB4: an item of its own");
        string own = C50Set(c, C50Doc("mine", shared, Roles(x.PRef), null, null, Track), "JB4: its script with the same id");
        c.Ok(new { cmd = "script.cast", script = own, roles = new Dictionary<string, object> { ["p"] = mine } }, "JB4: cast");
        c.Ok(new { cmd = "script.play", script = own, load = false }, "JB4: its Timeline loaded from it");
        c.Ok(new { cmd = "script.stop" }, "(stopped)");
        string tl = s.Model.Timeline?.ToJson();
        string marker = s.Model.Timeline?.Script?.Id;
        string scriptsBefore = C50Scripts(x);
        int before = C50Count(x), undo = W1UndoCount(c);
        c.Check(own == shared && marker == shared && before == 1, "JB4: Present: a colliding id exists (" + shared + " in the scene and in the file); the scene's Timeline carries its mark", own + " " + marker);
        var r = c.Ok(new { cmd = "scene.import", path = "c50_import.hs2nscene" }, "JB4: scene.import");
        var addedNodes = (r["added"] as JArray)?.Select(t => (string)t).ToList() ?? new List<string>();
        var list = s.Model.Scripts;
        int after = list?.Count ?? 0;
        c.Check(after == before + 2, "JB4: the scene's scripts: " + before + " + 2 imported = " + after, after + "");
        var imported = list?.Scripts.Skip(before).ToList() ?? new List<SceneScript>();
        c.Check(list != null && list.Scripts[0].Id == shared && list.Scripts[0].Name == "mine", "JB4: the scene's own script first, as it was");
        var one = imported.FirstOrDefault(z => z.Name == "file one");
        var two = imported.FirstOrDefault(z => z.Name == "file two");
        c.Check(one != null && JsonText.IsId(one.Id) && one.Id != shared && two != null && two.Id == f2, "JB4: the colliding id derived (" + one?.Id + "), the other kept", one?.Id + " " + two?.Id);
        string c1 = one?.CastOf("p"), c2 = two?.CastOf("p");
        int remapped = (c1 != null && addedNodes.Contains(c1) && c1 != i1 ? 1 : 0) + (c2 != null && addedNodes.Contains(c2) && c2 != i2 ? 1 : 0);
        c.Check(remapped == 2, "JB4: Present: " + remapped + " cast entries remapped onto the imported nodes", c1 + " " + c2 + " / " + string.Join(",", addedNodes));
        c.Check(one?.Prepared == null && two?.Prepared == null, "JB4: the imported scripts prepare again at their first play (no mark)");
        c.Check(s.Model.Timeline?.ToJson() == tl && s.Model.Timeline?.Script?.Id == marker, "JB4: the importing scene's Timeline and its mark are untouched (the file's mark is not imported)");
        c.Check(W1UndoCount(c) == undo + 1 && W1TopLabel(c) == StudioOps.Labels.ImportScene, "JB4: one entry «Import scene»");
        c.Ok(new { cmd = "undo" }, "JB4: undo");
        c.Check(C50Scripts(x) == scriptsBefore && C50Count(x) == before && addedNodes.All(n => s.Model.Find(n) == null), "JB4: undo removes the imported scripts with the nodes");
        c.Ok(new { cmd = "redo" }, "JB4: redo");
        c.Check(C50Count(x) == before + 2, "JB4: redo brings them back", C50Count(x) + "");
        var r2 = c.Ok(new { cmd = "scene.import", path = "c50_import.hs2nscene" }, "JB4: imported again");
        var ids = s.Model.Scripts.Scripts.Select(z => z.Id).ToList();
        c.Check(ids.Count == before + 4 && ids.Distinct().Count() == ids.Count, "JB4: imported twice: every id distinct", string.Join(", ", ids));
        c.Ok(new { cmd = "scene.new" }, "(a new scene)");
    }

    // ---------------------------------------------------------------- JS7: ctx.Request from a plugin, the bridge off

    static void C50Request(Ctx c)
    {
        if (!C37Stage(c)) return;
        C37ClearLog();
        var s = c.HostSettings();
        s.Bridge.Enabled = false;
        var o = c.Services.NewSessionOptions();
        o.Ids = new CountingIdSource(DefaultSeed + 50);
        var app = AppCore.ForTestsAsApp(c.Services, s, null, o);
        try
        {
            if (!c.Check(app.Bridge != null && !app.Bridge.Listening, "JS7: the host runs without a listener (the bridge off, as the app starts it)")) return;
            var host = app.LoadPlugins(C37Dir, new[] { C37Disabled, C40PrefixLater });
            var ctx = host.Find(HpGuid)?.Context;
            if (!c.Check(ctx != null && host.Find(HpGuid).IsLoaded, "JS7: the history probe is loaded", host.Find(HpGuid)?.State + "")) return;
            string item = (string)JObject.Parse(app.Call("{\"cmd\":\"item.add\",\"no\":399}"))["added"]?[0]?["key"];
            string pref = ScriptRoles.RefOf(app.Session.Model.Find(item));
            var doc = C50Doc("request", null, "[{\"role\":\"p\",\"kind\":\"item\",\"ref\":\"" + pref + "\"}]", "[{\"cmd\":\"camera.get\"}]", null,
                "[{\"type\":\"pos\",\"key\":\"$p\",\"keys\":[{\"t\":0,\"v\":[0,0,0]},{\"t\":1,\"v\":[0,1,0]}]}]", null, 2);
            Directory.CreateDirectory(C50Out(c));
            var requests = new (string cmd, object args)[]
            {
                ("script.set", new { script = doc }), ("script.list", null), ("script.get", new { script = 0 }), ("script.cast", new { script = 0, roles = new Dictionary<string, object> { ["p"] = item } }),
                ("script.status", null), ("script.borrow", new { script = 0, from = 0, window = new[] { 0, 0.5 } }), ("script.keep", new { script = 0 }), ("script.add", new { no = 1 }),
                ("script.export", new { script = 0, path = "c50/request" }), ("script.import", new { path = "c50/request" + SceneScript.FileExtension }), ("script.delete", new { no = 2 }),
                ("script.purge", null), ("script.stop", null), ("script.capture", null), ("script.remove", new { script = 1 }),
            };
            var reached = new List<string>();
            var bad = new List<string>();
            foreach (var (cmd, args) in requests)
            {
                var j = JObject.Parse(ctx.Request(cmd, args).Json);
                bool ok = j.Value<bool?>("ok") == true || ((string)j["code"] == BridgeCodes.NotFound && cmd == "script.capture");
                if ((string)j["code"] != BridgeCodes.UnknownCommand && ok) reached.Add(cmd);
                else bad.Add(cmd + ": " + Ctx.Short(j.ToString(Formatting.None)));
            }
            c.Check(reached.Count == requests.Length, "JS7: 15 commands answer through ctx.Request (script.capture without a loaded Timeline: not_found)", string.Join(" || ", bad));
            foreach (var cmd in new[] { "script.list", "script.get", "script.status" })
            {
                var args = cmd == "script.get" ? (object)new { script = 0 } : null;
                string viaRequest = ctx.Request(cmd, args).Json;
                var env = args == null ? new JObject() : JObject.FromObject(args);
                env.AddFirst(new JProperty("cmd", cmd));
                c.Check(viaRequest == ctx.Call(env.ToString(Formatting.None)), "JS7: " + cmd + ": Request = Call (the same reply text)");
            }
            var inline = JObject.Parse(ctx.Request("script.play", new { script = 0, load = false }).Json);
            c.Check((string)inline["code"] == BridgeCodes.Unsupported, "JS7: script.play answers later: the inline Request refuses it (unsupported)", Ctx.Short(inline.ToString(Formatting.None)));
            var task = ctx.RequestAsync("script.play", new { script = 0, load = false });
            for (int i = 0; i < 600 && !task.IsCompleted; i++) app.Frame(Dt);
            var played = task.IsCompleted ? JObject.Parse(task.Result.Json) : null;
            c.Check(played?.Value<bool?>("ok") == true && (string)played["state"] == "playing", "JS7: script.play through ctx.RequestAsync plays the script", played == null ? "not completed" : Ctx.Short(played.ToString(Formatting.None)));
            var cap = JObject.Parse(ctx.Request("script.capture", null).Json);
            c.Check(cap.Value<bool?>("ok") == true, "JS7: script.capture through ctx.Request after the play", Ctx.Short(cap.ToString(Formatting.None)));
            var stop = JObject.Parse(ctx.Request("script.stop", null).Json);
            c.Check(stop.Value<bool?>("ok") == true && (string)stop["state"] == "stopped", "JS7: script.stop through ctx.Request", Ctx.Short(stop.ToString(Formatting.None)));
        }
        finally { app.Dispose(); }
    }
}
