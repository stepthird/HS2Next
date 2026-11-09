// C47: the commands of ragdolls (Phase 6.7 package C) in an edit-mode test host on the clothed SFW targets (BR-Chan, HERO107), an item and a folder, the world in a preview
// scene. RC1 the names and the group; RC2 arguments and defaults; RC3 replies and their order; RC4 the six added; RC6 `id` stays the envelope's; RC7 every form scene scripts use, on a live
// body, {key, accessory} starting at the muzzle chara.accessoryAxis reads (Phase 6.9); RD5 the forms of from and target; RE1 one history entry per operation with its label, undo and redo to the byte; RE2 none for a running
// clock; RE3 a merge; RE4 `undo: false`; RE5 undo of a build and of a release; RB13 the bake. Every check that compares a difference prints what shows the thing compared is there.
// A call pumps frames, so a reading that compares two moments of a body is taken with the scene clock paused.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Api.Bridge;
using HS2Next.Api.Unity;
using HS2Next.App;
using HS2Next.App.Commands;
using HS2Next.Motion.Fk;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Ragdoll;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

public static partial class BridgeCoreTest
{
    const string C47Nope = "0123456789abcdef0123456789abcdef";
    static readonly string[] C47Parity =
    {
        "ragdoll.build", "ragdoll.lock", "ragdoll.tie", "ragdoll.pin", "ragdoll.unpin", "ragdoll.follower", "ragdoll.turn", "ragdoll.settle", "ragdoll.pusher", "ragdoll.parts", "ragdoll.push",
        "ragdoll.release", "fx.shoot",
    };
    static readonly string[] C47Added = { "ragdoll.status", "ragdoll.set", "ragdoll.knock", "ragdoll.throw", "ragdoll.bake", "ragdoll.obstacle" };
    static readonly string[] C47Reads = { "ragdoll.parts", "ragdoll.status" };
    static readonly string[] C47WorldRow = { "bodies", "props", "pushers", "hz", "steps", "floor", "floorSource", "room", "lastHit" };
    static readonly string[] C47CharaRow = { "key", "state", "awake", "parts", "pins", "ties", "holds", "overridden", "protected", "settled", "weakest" };
    static readonly string[] C47PinRow = { "pin", "part", "follow", "lag", "state" };
    static readonly float[] C47Up = { 0f, 1f, 0f };

    /// <summary>The name and the default of each argument, in order, as the spec's tables give them.</summary>
    static readonly Dictionary<string, (string name, object value)[]> C47Args = new Dictionary<string, (string, object)[]>
    {
        ["ragdoll.build"] = new (string, object)[] { ("key", null), ("mapFloor", true), ("room", null) },
        ["ragdoll.lock"] = new (string, object)[] { ("key", null), ("parts", "upperarm,forearm,hand") },
        ["ragdoll.tie"] = new (string, object)[] { ("key", null), ("a", "hand_L"), ("b", "hand_R") },
        ["ragdoll.pin"] = new (string, object)[] { ("key", null), ("part", "hand_R"), ("follow", null), ("snap", false) },
        ["ragdoll.unpin"] = new (string, object)[] { ("key", null) },
        ["ragdoll.follower"] = new (string, object)[] { ("key", null), ("part", "hand_R") },
        ["ragdoll.turn"] = new (string, object)[] { ("key", null), ("dir", C47Up), ("strength", 2f) },
        ["ragdoll.settle"] = new (string, object)[] { ("key", null), ("dir", C47Up), ("seconds", 2f), ("gain", 6f), ("damp", 1.5f) },
        ["ragdoll.pusher"] = new (string, object)[] { ("key", null), ("a", "cf_J_LegLow01_R"), ("b", "cf_J_Toes01_R"), ("radius", 0.45f) },
        ["ragdoll.parts"] = new (string, object)[] { ("key", null) },
        ["ragdoll.push"] = new (string, object)[] { ("key", null), ("part", "chest"), ("impulse", null), ("local", null), ("away", null), ("bone", "cf_J_Spine02"), ("strength", 100f), ("lift", 0f) },
        ["ragdoll.release"] = new (string, object)[] { ("key", null), ("blend", false) },
        ["fx.shoot"] = new (string, object)[] { ("from", null), ("target", null), ("spread", 0f), ("kill", false), ("momentum", 181f), ("stain", false) },
        ["ragdoll.status"] = new (string, object)[] { ("key", null) },
        ["ragdoll.set"] = new (string, object)[] { ("key", null), ("values", null), ("snap", false), ("preset", null) },
        ["ragdoll.knock"] = new (string, object)[] { ("key", null) },
        ["ragdoll.throw"] = new (string, object)[] { ("from", null), ("target", null), ("ray", null), ("kind", null), ("mass", null), ("speed", null), ("size", null) },
        ["ragdoll.bake"] = new (string, object)[] { ("key", null) },
        ["ragdoll.obstacle"] = new (string, object)[] { ("key", null), ("on", true) },
    };

    /// <summary>The reply members of each command, in order.</summary>
    static readonly Dictionary<string, string[]> C47Returns = new Dictionary<string, string[]>
    {
        ["ragdoll.build"] = new[] { "built", "floor" },
        ["ragdoll.lock"] = new[] { "locked" },
        ["ragdoll.tie"] = new[] { "tied" },
        ["ragdoll.pin"] = new[] { "pinned" },
        ["ragdoll.unpin"] = new[] { "unpinned" },
        ["ragdoll.follower"] = new[] { "folder", "constraint" },
        ["ragdoll.turn"] = new[] { "ok2" },
        ["ragdoll.settle"] = new[] { "ok2" },
        ["ragdoll.pusher"] = new[] { "count" },
        ["ragdoll.parts"] = new[] { "parts" },
        ["ragdoll.push"] = new[] { "pushed" },
        ["ragdoll.release"] = new string[0],
        ["fx.shoot"] = new[] { "hits", "muzzle", "target" },
        ["ragdoll.status"] = new[] { "world", "characters" },
        ["ragdoll.set"] = new[] { "changed" },
        ["ragdoll.knock"] = new[] { "state" },
        ["ragdoll.throw"] = new[] { "props" },
        ["ragdoll.bake"] = new[] { "bones" },
        ["ragdoll.obstacle"] = new[] { "obstacles" },
    };

    /// <summary>What the checks work on.</summary>
    sealed class C47Fix
    {
        public AppCore App;
        public string Chara, Other, Item, Folder;
        public RagdollPart Part => App.Runtime.GetPart<RagdollPart>();
        public StudioOps Ops => App.Ops;
    }

    /// <summary>Where the two characters stand.</summary>
    static readonly Vector3 C47Home = Vector3.zero, C47OtherHome = new Vector3(8f, 0f, 0f);

    sealed class C47Map : IMapQuery
    {
        public float Height;
        public bool Loaded => true;
        public IReadOnlyList<GameObject> Roots => Array.Empty<GameObject>();

        public bool Surface(Vector3 point, out MapSurfaceHit hit, float above = 0f, float below = 60f)
        {
            hit = new MapSurfaceHit { Height = Height, Normal = Vector3.up, Source = MapSurface.SourceMesh };
            return true;
        }

        public int Surfaces(IReadOnlyList<Vector3> points, MapSurfaceHit[] hits, float above = 0f, float below = 60f)
        {
            for (int i = 0; i < points.Count; i++) hits[i] = new MapSurfaceHit { Height = Height, Normal = Vector3.up, Source = MapSurface.SourceMesh };
            return points.Count;
        }
    }

    static void C47(Ctx c)
    {
        var make = RagdollWorld.MakeScene;
        var free = RagdollWorld.FreeScene;
        RagdollWorld.MakeScene = () => EditorSceneManager.NewPreviewScene();
        RagdollWorld.FreeScene = s => EditorSceneManager.ClosePreviewScene(s);
        try
        {
            C47Registration(c);
            var app = c.Host();
            string f = C30Load(c, c.FemaleRel, "female", "RC: BR-Chan");
            string m = C30Load(c, c.MaleRel, "male", "RC: HERO107");
            string item = W1AddItem(c);
            var folder = app.Session.AddFolder();
            app.Runtime.ApplyNow();
            if (!c.Check(f != null && m != null && item != null && folder.Ok, "RC: the fixture")) return;
            var x = new C47Fix { App = app, Chara = f, Other = m, Item = item, Folder = folder.NewId };
            foreach (var k in new[] { f, m })
            {
                c.Ok(new { cmd = "chara.motion", key = k, paused = false, undo = false }, "RC: the motion runs");
                c.Ok(new { cmd = "chara.anime", key = k, group = 19, category = 0, no = 0, time = 0.5f, speed = 0f, undo = false }, "RC: the idle row");
            }
            c.Ok(new { cmd = "obj.transform", key = m, pos = new[] { 8f, 0f, 0f }, undo = false }, "RC: the second character stands apart");
            c.Ok(new { cmd = "obj.transform", key = item, pos = new[] { 0f, 10f, 12f }, undo = false }, "RC: the item stands in front");
            c.Frames(3);
            C47Key(c, x);
            C47NoBody(c, x);
            C47Replies(c, x);
            C47Refusals(c, x);
            C47Limits(c, x);
            C47Set(c, x);
            C47Status(c, x);
            C47Id(c, x);
            C47Entries(c, x);
            C47Clock(c, x);
            C47Merge(c, x);
            C47NoHistory(c, x);
            C47Undo(c, x);
            C47Batch(c, x);
            C47Forms(c, x);
            C47Shots(c, x);
            C47Timed(c, x);
            C47Throw(c, x);
            C47Bake(c, x);
        }
        finally
        {
            c.Dispose(c.App);
            RagdollWorld.MakeScene = make;
            RagdollWorld.FreeScene = free;
        }
    }

    // ---------------------------------------------------------------- helpers

    static RagdollBody C47Body(C47Fix x, string key) => x.Part.BodyOf(key);
    static CharaRagdoll C47Block(C47Fix x, string key) => ((CharaNodeState)x.App.Session.Model.Find(key).State).Ragdoll;
    static string C47BlockJson(C47Fix x, string key) => C47Block(x, key).ToJson();
    static string C47WorldJson(C47Fix x) => x.App.Session.Model.Ragdoll?.ToJson();

    /// <summary>The members of a reply beyond the envelope.</summary>
    static List<string> C47Members(JObject r) => r.Properties().Select(p => p.Name).Where(n => n != "ok" && n != "cmd" && n != "id" && n != "warnings").ToList();

    static void C47Pause(Ctx c, bool on) => c.Ok(new { cmd = "scene.pause", paused = on }, on ? "(the clock stops)" : "(the clock runs)");

    // a getting up moves its character's node: back to where it stood, with a line in the report
    static void C47Home2(Ctx c, C47Fix x, string key, Vector3 home, string who)
    {
        var at = x.App.Runtime.HolderOf(key).position;
        if ((at - home).magnitude > 1e-4f) c.Info("C47Reset: " + who + "'s node stood at " + at.ToString("F3") + ", back to " + home.ToString("F3"));
        x.App.Session.Set(key, "transform.pos", new Vec3(home.x, home.y, home.z), ExecuteOptions.NotRecorded);
        x.App.Session.Set(key, "transform.rot", new Vec3(0f, 0f, 0f), ExecuteOptions.NotRecorded);
    }

    // no body, no world, no block, both characters where they stood, what is on the history stays
    static void C47Reset(Ctx c, C47Fix x)
    {
        c.Call(new { cmd = "scene.pause", paused = false });
        c.Call(new { cmd = "ragdoll.release", undo = false });
        var bus = x.App.Session.Commands;
        foreach (var k in new[] { x.Chara, x.Other }) bus.Execute(new RagdollEditCommand(k, null), ExecuteOptions.NotRecorded);
        bus.Execute(new RagdollWorldEditCommand(null), ExecuteOptions.NotRecorded);
        C47Home2(c, x, x.Chara, C47Home, "BR-Chan");
        C47Home2(c, x, x.Other, C47OtherHome, "HERO107");
        x.App.Runtime.ApplyNow();
        c.Frames(1);
    }

    static double C47Moved(RagdollBody b, Vector3 from) => (b.Parts[0].Tr.position - from).magnitude;

    static string C47Doc(Ctx c)
    {
        c.Call(new { cmd = "scene.doc", capture = false });
        return c.LastText;
    }

    // where two texts part, for a failure's detail; empty when they are equal
    static string C47FirstDiff(string a, string b)
    {
        if (a == b) return "";
        if (a == null || b == null) return (a == null ? "none" : "a block") + " against " + (b == null ? "none" : "a block");
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        int from = Math.Max(0, i - 80);
        return "at " + i + ": " + a.Substring(from, Math.Min(200, a.Length - from)).Replace("\n", " ") + "  AGAINST  " + b.Substring(from, Math.Min(200, b.Length - from)).Replace("\n", " ");
    }

    // one history entry with its label; undo and redo give both blocks back to the byte
    static JObject C47Entry(Ctx c, C47Fix x, object req, string label, string what, string key = null)
    {
        key = key ?? x.Chara;
        int n = W1UndoCount(c);
        x.Part.CaptureNow(key);
        string block = C47BlockJson(x, key), world = C47WorldJson(x);
        var r = c.Ok(req, what);
        c.Check(W1UndoCount(c) == n + 1 && W1TopLabel(c) == label, what + ": one entry «" + label + "»", W1UndoCount(c) - n + " " + W1TopLabel(c));
        string doneBlock = C47BlockJson(x, key), doneWorld = C47WorldJson(x);
        c.Check(doneBlock != block || doneWorld != world || r.Value<bool?>("built") == false, what + ": a block changed", Ctx.Short(doneBlock));
        c.Ok(new { cmd = "undo" }, what + ": undo");
        c.Check(C47BlockJson(x, key) == block && C47WorldJson(x) == world, what + ": undo gives the blocks back to the byte", C47FirstDiff(block, C47BlockJson(x, key)) + " / " + C47FirstDiff(world, C47WorldJson(x)));
        c.Ok(new { cmd = "redo" }, what + ": redo");
        c.Check(C47BlockJson(x, key) == doneBlock && C47WorldJson(x) == doneWorld, what + ": redo gives the blocks again to the byte");
        return r;
    }

    // what changes no model value: no entry, the document as it was
    static JObject C47NoEntry(Ctx c, object req, string what)
    {
        string sha = W1DocSha(c);
        int n = W1UndoCount(c);
        var r = c.Ok(req, what);
        c.Check(W1UndoCount(c) == n && W1DocSha(c) == sha, what + ": no entry, the document as it was", W1UndoCount(c) - n + "");
        return r;
    }

    // ---------------------------------------------------------------- RC1-RC4: registration

    static void C47Registration(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        var names = CoreCommands.Phase67Names.ToList();
        c.Check(names.SequenceEqual(C47Parity.Concat(C47Added)), "RC1: Phase67Names = the 13 and the 6", string.Join(", ", names));
        c.Check(names.All(CoreCommands.AllNames.Contains), "RC1: all in CoreCommands.AllNames");
        c.Check(CoreCommands.Groups.Contains("ragdoll") && CkGroups.Contains("ragdoll"), "RC1: the group is in CoreCommands.Groups and a convention group", string.Join(", ", CoreCommands.Groups));
        c.Check(C47Parity.All(CoreCommands.ParityNames.Contains) && C47Parity.SequenceEqual(RagdollCommands.ParityNames), "RC1: the 13 parity names");
        c.Check(C47Added.All(n => !CoreCommands.ParityNames.Contains(n)), "RC4: the six added are not parity names");
        var bad = new List<string>();
        foreach (var n in names)
        {
            var s = reg.Find(n);
            if (s == null) { bad.Add(n + ": not registered"); continue; }
            bool read = C47Reads.Contains(n);
            if (s.Owner != BridgeOwners.Core) bad.Add(n + ": owner " + s.Owner);
            if (s.Group != "ragdoll") bad.Add(n + ": group " + s.Group);
            if (s.Since != "1.3.0") bad.Add(n + ": since " + s.Since);
            if ((s.Flags & CommandFlags.Experimental) == 0) bad.Add(n + ": not experimental");
            if (((s.Flags & CommandFlags.ReadOnly) != 0) != read) bad.Add(n + ": read flag");
            if (((s.Flags & CommandFlags.Mutates) != 0) == read) bad.Add(n + ": mutates flag");
            if ((s.Flags & CommandFlags.Defers) != 0) bad.Add(n + ": defers");
            if (string.IsNullOrEmpty(s.Summary)) bad.Add(n + ": no summary");
            if (s.Args.Any(a => string.IsNullOrEmpty(a.Doc))) bad.Add(n + ": an argument without a doc");
            if (s.Aliases.Count > 0) bad.Add(n + ": an alias");
        }
        c.Check(bad.Count == 0, "RC1: owner, group ragdoll, Since 1.3.0, experimental, read and write flags, a summary, no alias", string.Join("; ", bad));
        foreach (var kv in C47Args)
        {
            var got = reg.Find(kv.Key)?.Args.ToList() ?? new List<FieldSpec>();
            string rc = C47Added.Contains(kv.Key) ? "RC4" : "RC2";
            c.Check(got.Select(a => a.Name).SequenceEqual(kv.Value.Select(a => a.name)), rc + ": " + kv.Key + ": the arguments of the spec, in order", string.Join(", ", got.Select(a => a.Name)));
            string defaults = JsonConvert.SerializeObject(got.Select(a => a.Default)), want = JsonConvert.SerializeObject(kv.Value.Select(a => a.value));
            c.Check(defaults == want, rc + ": " + kv.Key + ": the defaults of the spec", defaults);
        }
        foreach (var kv in C47Returns)
        {
            var got = reg.Find(kv.Key)?.Returns.Select(a => a.Name).ToList() ?? new List<string>();
            c.Check(got.SequenceEqual(kv.Value), (C47Added.Contains(kv.Key) ? "RC4: " : "RC3: ") + kv.Key + ": the reply's members, in order", string.Join(", ", got));
        }
        c.Check(!reg.Find("ragdoll.build").Returns.First(a => a.Name == "floor").Required, "RC3: ragdoll.build's floor is not always there");
        c.Check(reg.Find("fx.shoot").Args.Where(a => a.Required).Select(a => a.Name).SequenceEqual(new[] { "from", "target" }), "RC2: fx.shoot needs from and target");
        c.Check(reg.Find("ragdoll.obstacle").Args.First(a => a.Name == "key").Required, "RC4: ragdoll.obstacle needs an item");
        var live = reg.Specs.Where(s => s.Owner == BridgeOwners.Core || s.Owner == BridgeOwners.Bridge).ToList();
        c.Check(live.Count == 265 && live.Count(s => (s.Flags & CommandFlags.Experimental) != 0) == 107 && CoreCommands.ParityNames.Count == 111, "RC1: 265 commands, 107 experimental, 111 parity", live.Count + "");
        foreach (var n in names)
        {
            var h = c.Call(app, new { cmd = "help", name = n });
            c.Check(h.Value<bool?>("ok") == true && (string)h["command"]?["name"] == n, "RC1: help describes " + n, Ctx.Short(c.LastText));
        }
        c.Dispose(app);
        // the host above is gone: the next c.Host() is the one the checks use
    }

    // ---------------------------------------------------------------- the key

    static void C47Key(Ctx c, C47Fix x)
    {
        C47Reset(c, x);
        x.App.Session.Selection.Set(new string[0]);
        var ops = x.Ops;
        c.Check(ops.RagdollDefaultCharacter() == null, "RC2: no build and no selection: no character");
        foreach (var cmd in new[] { "ragdoll.build", "ragdoll.lock", "ragdoll.tie", "ragdoll.pin", "ragdoll.follower", "ragdoll.turn", "ragdoll.settle", "ragdoll.pusher", "ragdoll.parts", "ragdoll.push", "ragdoll.knock", "ragdoll.bake" })
            C45Refused(c, new { cmd }, "not_found", "key", "RC2: " + cmd + " with no key and no character");
        C45Refused(c, new { cmd = "ragdoll.set", preset = "limp" }, "not_found", "key", "RC4: a preset with no key and no character");
        C45Refused(c, new { cmd = "ragdoll.set", values = new Dictionary<string, object> { ["muscle"] = 3 } }, "not_found", "key", "RC4: a character's value with no key and no character");

        x.App.Session.Selection.Set(new[] { x.Folder, x.Item, x.Chara });
        c.Check(ops.RagdollDefaultCharacter() == x.Chara, "RC2: the selection's first character, past what is not one");
        c.Ok(new { cmd = "ragdoll.build", undo = false }, "RC2: a command with no key takes it");
        c.Check(C47Block(x, x.Chara).On && C47Body(x, x.Chara) != null, "RC2: on that character");
        x.App.Session.Selection.Set(new[] { x.Other });
        c.Check(ops.RagdollDefaultCharacter() == x.Chara, "RC2: the last build goes before the selection");
        c.Ok(new { cmd = "ragdoll.build", key = x.Other, undo = false }, "RC2: another build");
        c.Check(ops.RagdollDefaultCharacter() == x.Other, "RC2: the last build counts");
        x.App.Session.Selection.Set(new string[0]);
        foreach (var cmd in new[] { "ragdoll.build", "ragdoll.lock", "ragdoll.tie", "ragdoll.pin", "ragdoll.unpin", "ragdoll.follower", "ragdoll.turn", "ragdoll.settle", "ragdoll.pusher", "ragdoll.parts", "ragdoll.push", "ragdoll.release", "ragdoll.status", "ragdoll.set", "ragdoll.knock", "ragdoll.bake" })
        {
            C45Refused(c, new { cmd, key = x.Item }, "wrong_kind", "key", "RC2: " + cmd + " on an item");
            C45Refused(c, new { cmd, key = C47Nope }, "not_found", "key", "RC2: " + cmd + " on no node");
        }
        C45Refused(c, new { cmd = "ragdoll.status", key = 3 }, "bad_args", "key", "RC2: a number is no key");
        C45Refused(c, new { cmd = "ragdoll.obstacle", key = x.Chara }, "wrong_kind", "key", "RC4: a character is no obstacle");
        C45Refused(c, new { cmd = "ragdoll.obstacle" }, "bad_args", "key", "RC4: an obstacle needs a key");
        C47Reset(c, x);
    }

    // ---------------------------------------------------------------- no body

    static void C47NoBody(Ctx c, C47Fix x)
    {
        C47Reset(c, x);
        foreach (var r in new object[]
        {
            new { cmd = "ragdoll.lock", key = x.Chara }, new { cmd = "ragdoll.tie", key = x.Chara }, new { cmd = "ragdoll.pin", key = x.Chara }, new { cmd = "ragdoll.follower", key = x.Chara },
            new { cmd = "ragdoll.parts", key = x.Chara }, new { cmd = "ragdoll.push", key = x.Chara, local = new[] { 0, 0, 1 } }, new { cmd = "ragdoll.knock", key = x.Chara },
            new { cmd = "ragdoll.bake", key = x.Chara }, new { cmd = "ragdoll.set", key = x.Chara, snap = true },
        })
            C45Refused(c, r, "unavailable", "key", "RC2: " + JObject.FromObject(r)["cmd"] + " on a character with no body");
        c.Check(c.Ok(new { cmd = "ragdoll.turn", key = x.Chara }, "RC2: ragdoll.turn").Value<bool?>("ok2") == false, "RC2: ragdoll.turn answers ok2 false with no body", Ctx.Short(c.LastText));
        c.Check(c.Ok(new { cmd = "ragdoll.settle", key = x.Chara }, "RC2: ragdoll.settle").Value<bool?>("ok2") == false, "RC2: ragdoll.settle answers ok2 false with no body", Ctx.Short(c.LastText));
        c.Check((int?)C47NoEntry(c, new { cmd = "ragdoll.unpin", key = x.Chara }, "RC2: ragdoll.unpin with no pin")["unpinned"] == 0, "RC2: no pin went");
        c.Check(C47Members(C47NoEntry(c, new { cmd = "ragdoll.release", key = x.Chara }, "RC2: a release with no body")).Count == 0, "RC2: a release answers nothing", Ctx.Short(c.LastText));
        C47NoEntry(c, new { cmd = "ragdoll.release" }, "RC2: a release of a scene with no world");
        c.Check(x.Part.World == null, "RC2: no world came of it");
        var status = c.Ok(new { cmd = "ragdoll.status" }, "RC4: ragdoll.status with no world");
        c.Check(C47Members((JObject)status["world"] == null ? new JObject() : status).SequenceEqual(new[] { "world", "characters" }) && (status["characters"] as JArray)?.Count == 0, "RC4: world and characters are there, no row", Ctx.Short(c.LastText));
        var w = (JObject)status["world"];
        c.Check(w.Properties().Select(p => p.Name).SequenceEqual(C47WorldRow) && (int)w["bodies"] == 0 && (double)w["hz"] == 240 && w["floor"].Type == JTokenType.Null && w["room"].Type == JTokenType.Null, "RC4: the world's row with no world", w.ToString(Formatting.None));
        var row = c.Ok(new { cmd = "ragdoll.status", key = x.Chara }, "RC4: a character with no body")["characters"]?[0] as JObject;
        c.Check(row != null && row["state"].Type == JTokenType.Null && (int)row["parts"] == 0, "RC4: its row has no state and no parts", row?.ToString(Formatting.None));
    }

    // ---------------------------------------------------------------- RC2, RC3, RC4: a call of each on a live body

    static void C47Replies(Ctx c, C47Fix x)
    {
        C47Reset(c, x);
        // no map: the floor is the feet's, no floor member
        var built = c.Ok(new { cmd = "ragdoll.build", key = x.Chara }, "RC3: ragdoll.build");
        c.Check(C47Members(built).SequenceEqual(new[] { "built" }) && (bool)built["built"], "RC3: built true, no floor with no map", Ctx.Short(c.LastText));
        var body = C47Body(x, x.Chara);
        c.Check(body != null && body.Parts.Count == 16 && x.Part.World.FloorSource == RagdollFloorSources.Feet, "RC2: a body of 16 parts on the feet's floor", x.Part.World?.FloorSource);
        int undo = W1UndoCount(c);
        var again = c.Ok(new { cmd = "ragdoll.build", key = x.Chara }, "RC3: ragdoll.build again");
        c.Check((bool?)again["built"] == false && C47Body(x, x.Chara) == body && W1UndoCount(c) == undo, "RC3: built false, the same body, no entry", Ctx.Short(c.LastText));

        c.Check((int)c.Ok(new { cmd = "ragdoll.lock", key = x.Chara }, "RC3: ragdoll.lock")["locked"] == 6, "RC3: locked counts the joints of both arms", Ctx.Short(c.LastText));
        c.Check(C47Members(c.Ok(new { cmd = "ragdoll.lock", key = x.Chara, parts = "forearm" }, "RC3: a part of a name")).SequenceEqual(new[] { "locked" }), "RC3: locked alone");
        c.Check((bool?)c.Ok(new { cmd = "ragdoll.tie", key = x.Chara }, "RC3: ragdoll.tie")["tied"] == true, "RC3: tied", Ctx.Short(c.LastText));
        c.Check(C47Block(x, x.Chara).Ties.Count == 1 && C47Block(x, x.Chara).Ties[0].A == "hand_L" && C47Block(x, x.Chara).Ties[0].B == "hand_R" && body.TieCount == 1, "RC2: a and b default to the hands");
        c.Check((bool?)c.Ok(new { cmd = "ragdoll.pin", key = x.Chara }, "RC3: ragdoll.pin")["pinned"] == true, "RC3: pinned", Ctx.Short(c.LastText));
        c.Check(C47Block(x, x.Chara).Pins.Count == 1 && C47Block(x, x.Chara).Pins[0].Part == "hand_R" && !C47Block(x, x.Chara).Pins[0].IsFollowing && body.Pins.Count == 1, "RC2: part defaults to hand_R, no follow");
        c.Ok(new { cmd = "ragdoll.pin", key = x.Chara, part = "hand_L", follow = x.Item }, "RC2: follow a node");
        c.Ok(new { cmd = "ragdoll.pin", key = x.Chara, part = "foot_L", follow = new { key = x.Other, bone = "cf_J_Hand_L" }, snap = true }, "RC2: follow {key, bone}, with snap");
        var pins = C47Block(x, x.Chara).Pins;
        c.Check(pins.Count == 3 && pins[1].Follow == x.Item && pins[1].Bone == "" && pins[2].Follow == x.Other && pins[2].Bone == "cf_J_Hand_L" && pins[2].Offset?.Equals(Vec3.Zero) == true && pins[1].Offset?.Equals(Vec3.Zero) == false, "RC2: the two follow forms and snap's offset", C47BlockJson(x, x.Chara));
        C45Refused(c, new { cmd = "ragdoll.pin", key = x.Chara, follow = C47Nope }, "not_found", "follow", "RC2: a follow to no node");
        C45Refused(c, new { cmd = "ragdoll.pin", key = x.Chara, follow = new { key = x.Other, bone = "nope" } }, "not_found", "follow", "RC2: a follow to no bone");
        C45Refused(c, new { cmd = "ragdoll.pin", key = x.Chara, follow = 3 }, "bad_args", "follow", "RC2: a number is no follow");
        C45Refused(c, new { cmd = "ragdoll.pin", key = x.Chara, follow = new { bone = "x" } }, "bad_args", "follow.key", "RC2: {bone} alone is no follow");
        c.Check((int)c.Ok(new { cmd = "ragdoll.unpin", key = x.Chara }, "RC3: ragdoll.unpin")["unpinned"] == 3, "RC3: unpinned says how many", Ctx.Short(c.LastText));
        c.Check(body.Pins.Count == 0, "RC2: the pins are gone from the body");
        c.Ok(new { cmd = "ragdoll.build", key = x.Other }, "RC2: a second body");
        c.Ok(new { cmd = "ragdoll.pin", key = x.Chara }, "RC2: a pin on the first");
        c.Ok(new { cmd = "ragdoll.pin", key = x.Other, part = "head" }, "RC2: a pin on the second");
        int n = W1UndoCount(c);
        c.Check((int)c.Ok(new { cmd = "ragdoll.unpin" }, "RC2: ragdoll.unpin with no key")["unpinned"] == 2 && W1UndoCount(c) == n + 1 && W1TopLabel(c) == "Unpin", "RC2: every character's, one entry", Ctx.Short(c.LastText));

        var follower = c.Ok(new { cmd = "ragdoll.follower", key = x.Chara }, "RC3: ragdoll.follower");
        string folder = (string)follower["folder"], constraint = (string)follower["constraint"];
        var node = x.App.Session.Model.Find(folder);
        var entry = x.App.Session.Model.Constraints?.Find(constraint);
        c.Check(C47Members(follower).SequenceEqual(new[] { "folder", "constraint" }) && node?.Kind == NodeKind.Folder && node.Label == x.App.Session.Model.Find(x.Chara).Label + " hand_R", "RC3: a folder named after the character and the part", node?.Label);
        c.Check(entry != null && entry.Source.Node == x.Chara && entry.Source.Bone == "cf_J_Hand_R" && entry.Target.Node == folder && entry.Components.SequenceEqual(new[] { ConstraintComponents.Parent }) && entry.Offset.Pos.Equals(Vec3.Zero) && entry.Offset.Rot.Equals(Vec3.Zero) && entry.Offset.Scale.Equals(Vec3.One), "RC2: a constraint from the bone to the folder, parent, no offset", entry?.ToJson());
        c.Ok(new { cmd = "undo" }, "(the follower goes)");

        c.Check((bool?)c.Ok(new { cmd = "ragdoll.turn", key = x.Chara }, "RC3: ragdoll.turn")["ok2"] == true, "RC3: ok2 true with a body", Ctx.Short(c.LastText));
        C47Pause(c, true);
        c.Check((bool?)c.Ok(new { cmd = "ragdoll.settle", key = x.Chara }, "RC3: ragdoll.settle")["ok2"] == true && body.SettleLeft == 2f, "RC2: settle runs 2 seconds by default", body.SettleLeft + "");
        C47Pause(c, false);
        c.Ok(new { cmd = "ragdoll.settle", key = x.Chara, dir = new[] { 0, 0, 1 }, seconds = 0.5, gain = 3, damp = 1 }, "RC2: settle with its arguments");
        C45Refused(c, new { cmd = "ragdoll.turn", key = x.Chara, dir = new[] { 0, 0, 0 } }, "bad_args", "dir", "RC2: a direction with no length");
        C45Refused(c, new { cmd = "ragdoll.settle", key = x.Chara, seconds = -1 }, "bad_args", "seconds", "RC2: a time below 0");

        c.Check((int)c.Ok(new { cmd = "ragdoll.pusher", key = x.Chara }, "RC3: ragdoll.pusher")["count"] == 1, "RC3: count", Ctx.Short(c.LastText));
        var pusher = x.App.Session.Model.Ragdoll?.Pushers.FirstOrDefault();
        c.Check(pusher != null && pusher.Key == x.Chara && pusher.A == "cf_J_LegLow01_R" && pusher.B == "cf_J_Toes01_R" && pusher.Radius == 0.45f && x.Part.World.Pushers.Count == 1, "RC2: the defaults of a pusher, at work");
        c.Check((int)c.Ok(new { cmd = "ragdoll.pusher", key = x.Other, a = "cf_J_ArmUp00_L", b = "cf_J_Hand_L", radius = 0.3 }, "RC2: a second pusher")["count"] == 2, "RC3: count says how many");
        C45Refused(c, new { cmd = "ragdoll.pusher", key = x.Chara, a = "nope" }, "not_found", "a", "RC2: a pusher on no bone");
        C45Refused(c, new { cmd = "ragdoll.pusher", key = x.Chara, radius = -1 }, "bad_args", "radius", "RC2: a radius below 0");

        C47Pause(c, true);
        var parts = c.Ok(new { cmd = "ragdoll.parts", key = x.Chara }, "RC3: ragdoll.parts")["parts"] as JObject;
        c.Check(parts != null && parts.Properties().Select(p => p.Name).SequenceEqual(RagdollParts.Keys), "RC3: every part, in part order", string.Join(", ", parts?.Properties().Select(p => p.Name) ?? new string[0]));
        var hand = (JObject)parts["hand_R"];
        var com = body.Part("hand_R").Rb.worldCenterOfMass;
        var fwd = body.Part("hand_R").BoneRotation * Vector3.forward;
        c.Check(hand.Properties().Select(p => p.Name).SequenceEqual(new[] { "pos", "forward", "up" }), "RC3: pos, forward, up", hand.ToString(Formatting.None));
        c.Check(hand["pos"].Select((v, i) => Math.Abs((double)v - com[i])).Max() <= 0.0005001 && hand["forward"].Select((v, i) => Math.Abs((double)v - fwd[i])).Max() <= 0.0005001, "RC3: the centre of mass and the bone's axes to three decimals", hand.ToString(Formatting.None) + " " + com);
        c.Check(hand["pos"].All(v => Math.Round((double)v, 3) == (double)v), "RC3: three decimals", hand["pos"].ToString(Formatting.None));
        C47Pause(c, false);

        var at = body.Part("chest").Rb.linearVelocity;
        c.Check((bool?)c.Ok(new { cmd = "ragdoll.push", key = x.Chara, impulse = new[] { 0f, 0f, 50f } }, "RC3: ragdoll.push")["pushed"] == true, "RC3: pushed", Ctx.Short(c.LastText));
        C45Refused(c, new { cmd = "ragdoll.push", key = x.Chara }, "bad_args", "impulse", "RC2: a push needs one of impulse, local, away");
        C45Refused(c, new { cmd = "ragdoll.push", key = x.Chara, impulse = new[] { 1, 0, 0 }, local = new[] { 1, 0, 0 } }, "bad_args", "impulse", "RC2: two of them are refused");
        C45Refused(c, new { cmd = "ragdoll.push", key = x.Chara, away = x.Item, bone = "nope" }, "not_found", "bone", "RC2: away on no bone");
        C45Refused(c, new { cmd = "ragdoll.push", key = x.Chara, away = C47Nope }, "not_found", "away", "RC2: away from no node");

        var release = c.Ok(new { cmd = "ragdoll.release", key = x.Other }, "RC3: ragdoll.release with a key");
        c.Check(C47Members(release).Count == 0 && C47Body(x, x.Other) == null && C47Body(x, x.Chara) == body, "RC3: nothing in the reply; that body goes, the other stays", Ctx.Short(c.LastText));
        c.Check(x.App.Session.Model.Ragdoll.Pushers.Count == 2, "RC2: a keyed release leaves the pushers");
        var scene = x.Part.World.Scene;
        c.Ok(new { cmd = "ragdoll.release" }, "RC2: ragdoll.release with no key");
        c.Check(x.Part.World == null && C47Body(x, x.Chara) == null && !C47Block(x, x.Chara).On && x.App.Session.Model.Ragdoll == null, "RC2: every body, the pushers and the world go", C47WorldJson(x));
        c.Check(!scene.IsValid() || !scene.isLoaded, "RC2: the world's scene goes with it");

        // a map that answers
        var map = new C47Map { Height = -2.5f };
        var query = RagdollFloor.QueryOf;
        RagdollFloor.QueryOf = rt => map;
        try
        {
            var r = c.Ok(new { cmd = "ragdoll.build", key = x.Chara }, "RC3: ragdoll.build on a map");
            c.Check(C47Members(r).SequenceEqual(new[] { "built", "floor" }) && Math.Abs((double)r["floor"] + 2.5) < 1e-5, "RC3: built, then the floor's height", Ctx.Short(c.LastText));
            var second = c.Ok(new { cmd = "ragdoll.build", key = x.Chara }, "RC3: again");
            c.Check((bool?)second["built"] == false && Math.Abs((double)second["floor"] + 2.5) < 1e-5, "RC3: a body already: built false, the floor all the same", Ctx.Short(c.LastText));
            c.Ok(new { cmd = "ragdoll.release" }, "(a release)");
            var feet = c.Ok(new { cmd = "ragdoll.build", key = x.Chara, mapFloor = false }, "RC2: mapFloor false");
            c.Check(C47Members(feet).SequenceEqual(new[] { "built" }) && x.App.Session.Model.Ragdoll?.Floor.Mode == RagdollFloorValues.ModeFeet && x.Part.World.FloorSource == RagdollFloorSources.Feet, "RC2: the floor's mode is feet", C47WorldJson(x));
            c.Check(W1TopLabel(c) == "Ragdoll", "RC2: one entry for the block and the floor", W1TopLabel(c));
        }
        finally { RagdollFloor.QueryOf = query; }
        C47Reset(c, x);
        c.Ok(new { cmd = "ragdoll.build", key = x.Chara, room = new[] { -30f, 10f, -5f, 15f } }, "RC2: room");
        var room = x.App.Session.Model.Ragdoll?.Room;
        c.Check(room != null && room.Center?.Equals(new Vec2(-10f, 5f)) == true && room.Size.Equals(new Vec3(40f, 40f, 20f)), "RC2: the middle and the two lengths, 40 high, in the world block", C47WorldJson(x));
        c.Check(x.Part.World.RoomCenter == new Vector2(-10f, 5f) && x.Part.World.RoomSize == new Vector3(40f, 40f, 20f), "RC2: the room stands there");
        C45Refused(c, new { cmd = "ragdoll.build", key = x.Other, room = new[] { 1f, 0f, 0f, 1f } }, "bad_args", "room", "RC2: a room with x1 under x0");
        C45Refused(c, new { cmd = "ragdoll.build", key = x.Other, room = new[] { 1f, 2f } }, "bad_args", "room", "RC2: a room of two numbers");
        C47Reset(c, x);
    }

    // ---------------------------------------------------------------- refusals: what is no part

    static void C47Refusals(Ctx c, C47Fix x)
    {
        C47Reset(c, x);
        c.Ok(new { cmd = "ragdoll.build", key = x.Chara, undo = false }, "RC2: a body");
        foreach (var (req, arg) in new (object, string)[]
        {
            (new { cmd = "ragdoll.lock", key = x.Chara, parts = "nope" }, "parts"),
            (new { cmd = "ragdoll.lock", key = x.Chara, parts = "hand,nope" }, "parts"),
            (new { cmd = "ragdoll.tie", key = x.Chara, a = "nope" }, "a"),
            (new { cmd = "ragdoll.tie", key = x.Chara, b = "nope" }, "b"),
            (new { cmd = "ragdoll.pin", key = x.Chara, part = "nope" }, "part"),
            (new { cmd = "ragdoll.push", key = x.Chara, part = "nope", impulse = new[] { 1, 0, 0 } }, "part"),
        })
        {
            var r = c.Call(req);
            c.Check((string)r["code"] == "bad_args" && (string)r["detail"]?["arg"] == arg && ((string)r["error"]).Contains("upperarm_L") && ((string)r["error"]).Contains("foot_R"), "RC2: no part: bad_args naming " + arg + ", the text lists the keys", Ctx.Short(c.LastText));
        }
        C45Refused(c, new { cmd = "ragdoll.tie", key = x.Chara, a = "chest", b = "chest" }, "bad_args", "b", "RC2: a part tied to itself");
        var follower = c.Call(new { cmd = "ragdoll.follower", key = x.Chara, part = "nope" });
        c.Check((string)follower["code"] == "not_found" && ((string)follower["error"]).Contains("nope") && (string)follower["detail"]?["arg"] == "part", "RC2: ragdoll.follower answers not_found for no part", Ctx.Short(c.LastText));
        c.Check(x.App.Session.Model.PreOrder().Count(n => n.Kind == NodeKind.Folder) == 1 && (x.App.Session.Model.Constraints?.Count ?? 0) == 0, "RC2: and makes no folder and no constraint", (x.App.Session.Model.Constraints?.Count ?? 0) + "");
        C47Reset(c, x);
    }

    // ---------------------------------------------------------------- limits

    static void C47Limits(Ctx c, C47Fix x)
    {
        C47Reset(c, x);
        c.Ok(new { cmd = "ragdoll.build", key = x.Chara, undo = false }, "RC2: a body");
        c.Ok(new { cmd = "ragdoll.build", key = x.Other, undo = false }, "RC2: a second body");
        void Limit(object req, string what)
        {
            string doc = C47Doc(c);
            string sha = W1DocSha(c);
            var r = c.Call(req);
            c.Check((string)r["code"] == "bad_args" && ((string)r["error"]).Contains("ragdoll.limit"), what + ": bad_args with ragdoll.limit in the text", Ctx.Short(c.LastText));
            c.Check(W1DocSha(c) == sha, what + ": nothing stored", C47FirstDiff(doc, C47Doc(c)));
        }
        var keys = RagdollParts.Keys;
        for (int i = 0; i < RagdollLimits.MaxPins; i++)
            c.Check(c.Call(new { cmd = "ragdoll.pin", key = i < 8 ? x.Chara : x.Other, part = keys[i], undo = false }).Value<bool?>("ok") == true, "RC2: pin " + (i + 1) + " of 16 in the scene", Ctx.Short(c.LastText));
        Limit(new { cmd = "ragdoll.pin", key = x.Chara, part = "head", undo = false }, "RC2: a 17th pin on the first character");
        Limit(new { cmd = "ragdoll.pin", key = x.Other, part = "head", undo = false }, "RC2: a 17th pin on the second");
        c.Ok(new { cmd = "ragdoll.unpin", undo = false }, "(the pins go)");
        int ties = 0;
        for (int i = 1; i < keys.Count && ties < RagdollLimits.MaxTies; i++)
            for (int k = 0; k < i && ties < RagdollLimits.MaxTies; k++, ties++)
                c.Check(c.Call(new { cmd = "ragdoll.tie", key = ties < 8 ? x.Chara : x.Other, a = keys[k], b = keys[i], undo = false }).Value<bool?>("ok") == true, "RC2: tie " + (ties + 1) + " of 16 in the scene", Ctx.Short(c.LastText));
        Limit(new { cmd = "ragdoll.tie", key = x.Other, a = "hand_L", b = "foot_R", undo = false }, "RC2: a 17th tie");
        C47Reset(c, x);
        c.Ok(new { cmd = "ragdoll.build", key = x.Chara, undo = false }, "RC2: a body again");
        for (int i = 0; i < RagdollLimits.MaxPushers; i++)
            c.Check((int?)c.Call(new { cmd = "ragdoll.pusher", key = x.Chara, undo = false })["count"] == i + 1, "RC2: pusher " + (i + 1) + " of 32", Ctx.Short(c.LastText));
        Limit(new { cmd = "ragdoll.pusher", key = x.Chara, undo = false }, "RC2: a 33rd pusher");
        C47Reset(c, x);
        var many = new SceneRagdollWorld();
        for (int i = 0; i < RagdollLimits.MaxObstacles; i++) many.Obstacles.Add(i.ToString("x32"));
        x.App.Session.Commands.Execute(new RagdollWorldEditCommand(many), ExecuteOptions.NotRecorded);
        x.App.Runtime.ApplyNow();
        Limit(new { cmd = "ragdoll.obstacle", key = x.Item, undo = false }, "RC4: a 65th obstacle");
        C47Reset(c, x);
    }

    // ---------------------------------------------------------------- ragdoll.set

    static Dictionary<string, object> C47V(params object[] kv)
    {
        var d = new Dictionary<string, object>();
        for (int i = 0; i < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
        return d;
    }

    static string C47Changed(JObject r) => string.Join(",", (r["changed"] as JArray)?.Select(t => (string)t) ?? new string[0]);

    static void C47Set(Ctx c, C47Fix x)
    {
        C47Reset(c, x);
        var r = c.Ok(new { cmd = "ragdoll.set", key = x.Chara, values = C47V("muscle", 30) }, "RC4: a body's value");
        c.Check(C47Members(r).SequenceEqual(new[] { "changed" }) && C47Changed(r) == "muscle" && C47Block(x, x.Chara).Values.Muscle == 30f, "RC4: stored, also with no body, changed names it", Ctx.Short(c.LastText));
        c.Check(W1TopLabel(c) == "Ragdoll settings", "RC4: the label", W1TopLabel(c));
        r = c.Ok(new { cmd = "ragdoll.set", values = C47V("timeScale", 0.5) }, "RC4: a world's value needs no key");
        c.Check(C47Changed(r) == "timeScale" && x.App.Session.Model.Ragdoll?.Values.TimeScale == 0.5f, "RC4: stored in the scene's block", C47WorldJson(x));
        int n = W1UndoCount(c);
        string block = C47BlockJson(x, x.Chara), world = C47WorldJson(x);
        r = c.Ok(new { cmd = "ragdoll.set", key = x.Chara, values = C47V("mass", 60, "gravity", 50) }, "RC4: both blocks in one call");
        c.Check(C47Changed(r) == "mass,gravity" && W1UndoCount(c) == n + 1 && W1TopLabel(c) == "Ragdoll settings", "RC4: one entry", C47Changed(r) + " " + (W1UndoCount(c) - n));
        c.Ok(new { cmd = "undo" }, "RC4: undo");
        c.Check(C47BlockJson(x, x.Chara) == block && C47WorldJson(x) == world, "RC4: one undo takes both back to the byte");
        n = W1UndoCount(c);
        r = c.Ok(new { cmd = "ragdoll.set", key = x.Chara, values = C47V("muscle", 30) }, "RC4: the same value again");
        c.Check(C47Changed(r) == "" && W1UndoCount(c) == n, "RC4: nothing changed, no entry", C47Changed(r));

        // every value by its name
        var bad = new List<string>();
        foreach (string name in RagdollBodyValues.Names)
        {
            object d = RagdollBodyValues.DefaultOf(name);
            object v = d is bool b ? (object)!b : (float)d + 0.25f;
            r = c.Call(new { cmd = "ragdoll.set", key = x.Chara, values = C47V(name, v), undo = false });
            if (r.Value<bool?>("ok") != true || !Equals(C47Block(x, x.Chara).Values.Get(name), v)) bad.Add(name);
        }
        foreach (string name in RagdollWorldValues.Names)
        {
            object d = RagdollWorldValues.DefaultOf(name);
            object v = d is float f ? (object)(f + 0.25f) : "box";
            r = c.Call(new { cmd = "ragdoll.set", values = C47V(name, v), undo = false });
            if (r.Value<bool?>("ok") != true || !Equals(x.App.Session.Model.Ragdoll?.Values.Get(name), v)) bad.Add(name);
        }
        c.Check(bad.Count == 0, "RC4: each of the " + (RagdollBodyValues.Names.Count + RagdollWorldValues.Names.Count) + " values by its name", string.Join(", ", bad));
        C47Reset(c, x);

        // room and floor
        void Room(object value, Vec2? center, Vec3 size, string what)
        {
            c.Ok(new { cmd = "ragdoll.set", values = C47V("room", value) }, what);
            var room = x.App.Session.Model.Ragdoll?.Room;
            c.Check(room != null && Nullable.Equals(room.Center, center) && room.Size.Equals(size), what + ": stored", C47WorldJson(x));
        }
        Room(new[] { -10, 10, -5, 5 }, new Vec2(0f, 0f), new Vec3(20f, 40f, 10f), "RC4: room as a strip");
        Room(new { center = new[] { 1, 2 }, size = new[] { 30, 20, 50 } }, new Vec2(1f, 2f), new Vec3(30f, 20f, 50f), "RC4: room as {center, size}");
        c.Ok(new { cmd = "ragdoll.set", values = C47V("room.center", new[] { 3, 4 }, "room.size", new[] { 60, 30, 70 }) }, "RC4: room.center, room.size");
        c.Check(x.App.Session.Model.Ragdoll.Room.Center?.Equals(new Vec2(3f, 4f)) == true && x.App.Session.Model.Ragdoll.Room.Size.Equals(new Vec3(60f, 30f, 70f)), "RC4: stored", C47WorldJson(x));
        c.Ok(JObject.Parse("{\"cmd\":\"ragdoll.set\",\"values\":{\"room.center\":null}}"), "RC4: room.center null");
        c.Check(x.App.Session.Model.Ragdoll.Room.Center == null, "RC4: nobody placed it any more", C47WorldJson(x));
        c.Ok(new { cmd = "ragdoll.set", values = C47V("floor.mode", "fixed", "floor.height", -1.5, "floor.offset", 0.25) }, "RC4: floor.*");
        var fl = x.App.Session.Model.Ragdoll.Floor;
        c.Check(fl.Mode == "fixed" && fl.Height == -1.5f && fl.Offset == 0.25f, "RC4: stored", C47WorldJson(x));
        c.Ok(new { cmd = "ragdoll.build", key = x.Chara, undo = false }, "RC4: a body on that floor");
        c.Check(Math.Abs(x.Part.World.FloorY + 1.25f) < 1e-6f && x.Part.World.FloorSource == RagdollFloorSources.Fixed, "RC4: the room's floor is height and offset", x.Part.World.FloorY + " " + x.Part.World.FloorSource);
        c.Ok(new { cmd = "ragdoll.set", values = C47V("floor", new { mode = "feet", offset = 0, height = 0 }) }, "RC4: floor as an object");
        c.Check(x.Part.World.FloorSource == RagdollFloorSources.Feet, "RC4: the floor follows at once", x.Part.World.FloorSource);
        c.Ok(new { cmd = "ragdoll.set", values = C47V("room", new { size = new[] { 50, 30, 50 } }) }, "RC4: a size alone");
        c.Check(x.Part.World.RoomSize == new Vector3(50f, 30f, 50f), "RC4: the room follows at once", x.Part.World.RoomSize + "");
        C47Reset(c, x);

        // presets
        foreach (var preset in RagdollPreset.Names)
        {
            var want = new RagdollBodyValues();
            RagdollPreset.ApplyTo(want, preset);
            c.Ok(new { cmd = "ragdoll.set", key = x.Chara, preset, undo = false }, "RC4: the preset " + preset);
            var got = C47Block(x, x.Chara).Values;
            c.Check(RagdollBodyValues.Names.All(v => Equals(want.Get(v), got.Get(v))), "RC4: its values", C47BlockJson(x, x.Chara));
        }
        r = c.Ok(new { cmd = "ragdoll.set", key = x.Chara, preset = "limp", values = C47V("muscle", 12) }, "RC4: a preset and a value");
        c.Check(C47Block(x, x.Chara).Values.Muscle == 12f && C47Block(x, x.Chara).Values.Pin == 0f && C47Changed(r).Contains("preset") && C47Changed(r).Contains("muscle"), "RC4: the value wins over the preset", C47Changed(r));
        C47Reset(c, x);

        // snap
        c.Ok(new { cmd = "ragdoll.build", key = x.Chara, undo = false }, "RC4: a body");
        var body = C47Body(x, x.Chara);
        c.Frames(2);
        var start = body.Parts[0].Tr.position;
        body.GoLimp();
        c.Ok(new { cmd = "ragdoll.push", key = x.Chara, local = new[] { 0, 0, -400 } }, "RC4: a push");
        c.Frames(30);
        double moved = C47Moved(body, start);
        C47Pause(c, true);
        r = c.Ok(new { cmd = "ragdoll.set", key = x.Chara, snap = true }, "RC4: snap");
        c.Check(C47Changed(r) == "snap" && body.State == RagdollState.Up, "RC4: state up", C47Changed(r) + " " + body.State);
        c.Check(moved >= 1.0 && C47Moved(body, body.Parts[0].TargetPos) < 1e-4 && body.Parts[0].Rb.linearVelocity.sqrMagnitude < 1e-6f, "RC4: every part at its target, at rest", "moved " + moved.ToString("F3") + " before, " + C47Moved(body, body.Parts[0].TargetPos).ToString("G4") + " after");
        C47Pause(c, false);

        // refusals, and a value out of range stored as given
        C45Refused(c, new { cmd = "ragdoll.set", key = x.Chara, values = C47V("nope", 1) }, "bad_args", "values.nope", "RC4: no such value");
        C45Refused(c, new { cmd = "ragdoll.set", key = x.Chara, values = C47V("muscle", "x") }, "bad_args", "values.muscle", "RC4: a number given as text");
        C45Refused(c, new { cmd = "ragdoll.set", key = x.Chara, values = C47V("relocate", 1) }, "bad_args", "values.relocate", "RC4: a switch given as a number");
        C45Refused(c, new { cmd = "ragdoll.set", values = C47V("propKind", "nope") }, "bad_args", "values.propKind", "RC4: a kind that is none");
        C45Refused(c, new { cmd = "ragdoll.set", values = C47V("floor.mode", "nope") }, "bad_args", "values.floor.mode", "RC4: a floor mode that is none");
        C45Refused(c, new { cmd = "ragdoll.set", values = C47V("room", new[] { 2, 1, 0, 1 }) }, "bad_args", "values.room", "RC4: a room that runs backwards");
        C45Refused(c, new { cmd = "ragdoll.set", values = C47V("room", new { nope = 1 }) }, "bad_args", "values.room", "RC4: a room's member that is none");
        C45Refused(c, new { cmd = "ragdoll.set", key = x.Chara, preset = "nope" }, "bad_args", "preset", "RC4: a preset that is none");
        C45Refused(c, new { cmd = "ragdoll.set", key = x.Chara }, "bad_args", "values", "RC4: nothing to set");
        c.Ok(new { cmd = "ragdoll.set", key = x.Chara, values = C47V("muscle", 1000), undo = false }, "RC4: a value beyond its range");
        c.Check(C47Block(x, x.Chara).Values.Muscle == 1000f && C47Body(x, x.Chara).Values.Muscle == 60f, "RC4: stored as given, clamped when it acts", C47Body(x, x.Chara).Values.Muscle + "");
        C47Reset(c, x);
    }

    // ---------------------------------------------------------------- ragdoll.status

    static void C47Status(Ctx c, C47Fix x)
    {
        C47Reset(c, x);
        c.Ok(new { cmd = "ragdoll.build", key = x.Chara, undo = false }, "RC4: a body");
        c.Ok(new { cmd = "ragdoll.lock", key = x.Chara, parts = "hand", undo = false }, "RC4: two holds");
        c.Ok(new { cmd = "ragdoll.tie", key = x.Chara, undo = false }, "RC4: a tie");
        c.Ok(new { cmd = "ragdoll.pin", key = x.Chara, part = "head", undo = false }, "RC4: a plain pin");
        c.Ok(new { cmd = "ragdoll.pin", key = x.Chara, part = "chest", follow = x.Item, undo = false }, "RC4: a pin that follows an item");
        c.Ok(new { cmd = "ragdoll.pusher", key = x.Chara, undo = false }, "RC4: a pusher");
        c.Frames(2);
        C47Pause(c, true);
        var s = c.Ok(new { cmd = "ragdoll.status" }, "RC4: ragdoll.status");
        var w = (JObject)s["world"];
        c.Check(C47Members(s).SequenceEqual(new[] { "world", "characters" }) && w.Properties().Select(p => p.Name).SequenceEqual(C47WorldRow), "RC4: the members of the world's row, in order", w.ToString(Formatting.None));
        c.Check((int)w["bodies"] == 1 && (int)w["pushers"] == 1 && (int)w["props"] == 0 && (double)w["hz"] == 240 && (long)w["steps"] == x.Part.World.Steps && (long)w["steps"] > 0, "RC4: its numbers", w.ToString(Formatting.None));
        c.Check((string)w["floorSource"] == "feet" && (double)w["floor"] == x.Part.World.FloorY && ((JObject)w["room"]).Properties().Select(p => p.Name).SequenceEqual(new[] { "center", "size" }) && w["lastHit"].Type == JTokenType.Null, "RC4: floor and room", w.ToString(Formatting.None));
        var rows = (JArray)s["characters"];
        var row = (JObject)rows[0];
        c.Check(rows.Count == 1 && row.Properties().Select(p => p.Name).SequenceEqual(C47CharaRow), "RC4: a row per body, with the spec's members in order", row.ToString(Formatting.None));
        c.Check((string)row["key"] == x.Chara && (string)row["state"] == "up" && (bool)row["awake"] && (int)row["parts"] == 16 && (int)row["ties"] == 1 && (int)row["holds"] == 2 && (double)row["weakest"] == 0 && ((JArray)row["protected"]).Count == 0 && ((JArray)row["overridden"]).Count == 0 && !(bool)row["settled"], "RC4: its values", row.ToString(Formatting.None));
        var pins = (JArray)row["pins"];
        var plain = (JObject)pins[0];
        var following = (JObject)pins[1];
        c.Check(pins.Count == 2 && plain.Properties().Select(p => p.Name).SequenceEqual(C47PinRow), "RC4: the members of a pin's row, in order", pins.ToString(Formatting.None));
        c.Check((string)plain["pin"] == "a" && (string)plain["part"] == "head" && plain["follow"].Type == JTokenType.Null && (int)plain["lag"] == 0 && (string)plain["state"] == "ok", "RC4: a plain pin", plain.ToString(Formatting.None));
        c.Check((string)following["part"] == "chest" && (string)following["follow"] == x.Item && (int)following["lag"] == 0, "RC4: a pin that follows: no lag with no constraint on the item", following.ToString(Formatting.None));
        C47Pause(c, false);

        // lag and overridden
        var con = x.Ops.AddConstraint(new ConstraintAddRequest { Source = new ConstraintEnd(x.Other), Target = new ConstraintEnd(x.Item), Components = new[] { ConstraintComponents.Position } });
        var over = x.Ops.AddConstraint(new ConstraintAddRequest { Source = new ConstraintEnd(x.Item), Target = new ConstraintEnd(x.Chara, "cf_J_ArmLow01_L"), Components = new[] { ConstraintComponents.Rotation } });
        c.Check(con.Ok && over.Ok, "RC4: two constraints", con.Message + " " + over.Message);
        c.Frames(2);
        row = (JObject)c.Ok(new { cmd = "ragdoll.status", key = x.Chara }, "RC4: with a constraint on the item and on a bone")["characters"][0];
        c.Check((int)row["pins"][1]["lag"] == 1 && (int)row["pins"][0]["lag"] == 0, "RC4: lag 1 on the pin whose node a constraint moves", row["pins"].ToString(Formatting.None));
        c.Check(((JArray)row["overridden"]).Select(t => (string)t).SequenceEqual(new[] { "forearm_L" }), "RC4: overridden names the part whose bone a driven bone writes", row["overridden"].ToString(Formatting.None));
        c.Ok(new { cmd = "undo" }, "(one constraint goes)");
        c.Ok(new { cmd = "undo" }, "(the other goes)");
        c.Check(x.App.Session.Model.Constraints == null || x.App.Session.Model.Constraints.Count == 0, "RC4: no constraint left", "");

        // a pin on a node that went
        c.Ok(new { cmd = "ragdoll.unpin", key = x.Chara, undo = false }, "(the pins go)");
        var folder = x.App.Session.AddFolder();
        x.App.Runtime.ApplyNow();
        c.Ok(new { cmd = "ragdoll.pin", key = x.Chara, part = "chest", follow = folder.NewId, undo = false }, "RC4: a pin on a folder");
        c.Ok(new { cmd = "delete", keys = new[] { folder.NewId } }, "RC4: the folder goes");
        c.Frames(2);
        row = (JObject)c.Ok(new { cmd = "ragdoll.status", key = x.Chara }, "RC4: the status")["characters"][0];
        c.Check((string)row["pins"][0]["state"] == "missing", "RC4: state missing, the pin stays where it is", row["pins"].ToString(Formatting.None));
        c.Ok(new { cmd = "undo" }, "(the folder comes back)");

        // a body with weak parts, a second character
        c.Ok(new { cmd = "ragdoll.build", key = x.Other, undo = false }, "RC4: a second body");
        var shot = c.Ok(new { cmd = "fx.shoot", from = new[] { 0f, 9f, 12f }, target = new { key = x.Chara } }, "RC4: a shot");
        var rows2 = (JArray)c.Ok(new { cmd = "ragdoll.status" }, "RC4: the status")["characters"];
        c.Check(rows2.Count == 2 && (int)shot["hits"] >= 1 && (double)rows2[0]["weakest"] > 0, "RC4: two rows, a hit leaves a weak part", rows2[0]["weakest"] + " after " + shot["hits"] + " hits");
        C47Reset(c, x);
    }

    // ---------------------------------------------------------------- RC6

    static void C47Id(Ctx c, C47Fix x)
    {
        c.Check(C47Args.Values.All(a => a.All(y => y.name != "id")), "RC6: no argument of these commands is named id");
        C47Reset(c, x);
        c.Ok(new { cmd = "ragdoll.build", key = x.Chara, undo = false }, "RC6: a body");
        c.Ok(new { cmd = "ragdoll.pin", key = x.Chara, undo = false }, "RC6: a pin");
        var r = c.Ok(new { cmd = "ragdoll.status", key = x.Chara, id = 7 }, "RC6: a request with an id");
        c.Check((int?)r["id"] == 7, "RC6: id comes back as the envelope's", Ctx.Short(c.LastText));
        var row = (JObject)r["characters"][0]["pins"][0];
        c.Check(row.Property("id") == null && (string)row["pin"] == "a", "RC6: a pin's row calls it pin", row.ToString(Formatting.None));
        c.Check((int?)c.Ok(new { cmd = "ragdoll.unpin", key = x.Chara, id = "b", undo = false }, "RC6: unpin with an id")["unpinned"] == 1, "RC6: the id names no pin: every pin of the character goes", Ctx.Short(c.LastText));
        C47Reset(c, x);
    }

    // ---------------------------------------------------------------- RE1

    static void C47Entries(Ctx c, C47Fix x)
    {
        C47Reset(c, x);
        c.Ok(new { cmd = "obj.transform", key = x.Item, pos = new[] { 0f, 10f, 12f } }, "RE1: something on the history");
        string k = x.Chara;
        C47Entry(c, x, new { cmd = "ragdoll.build", key = k }, "Ragdoll", "RE1: ragdoll.build");
        c.Check(C47Body(x, k) != null, "RE1: redo of a build makes a body");
        C47Entry(c, x, new { cmd = "ragdoll.lock", key = k }, "Hold", "RE1: ragdoll.lock");
        C47Entry(c, x, new { cmd = "ragdoll.tie", key = k }, "Tie", "RE1: ragdoll.tie");
        C47Entry(c, x, new { cmd = "ragdoll.pin", key = k }, "Pin", "RE1: ragdoll.pin");
        C47Entry(c, x, new { cmd = "ragdoll.unpin", key = k }, "Unpin", "RE1: ragdoll.unpin");
        C47Entry(c, x, new { cmd = "ragdoll.set", key = k, values = C47V("muscle", 25) }, "Ragdoll settings", "RE1: ragdoll.set, a body's value");
        C47Entry(c, x, new { cmd = "ragdoll.set", values = C47V("gravity", 80) }, "Ragdoll settings", "RE1: ragdoll.set, a world's value");
        C47Entry(c, x, new { cmd = "ragdoll.set", key = k, preset = "stiff" }, "Ragdoll settings", "RE1: ragdoll.set, a preset");
        C47Entry(c, x, new { cmd = "ragdoll.pusher", key = k }, "Pusher", "RE1: ragdoll.pusher");
        C47Entry(c, x, new { cmd = "ragdoll.obstacle", key = x.Item }, "Obstacle", "RE1: ragdoll.obstacle");
        int n = W1UndoCount(c);
        c.Check((int)c.Ok(new { cmd = "ragdoll.obstacle", key = x.Item }, "RE1: an obstacle already")["obstacles"] == 1 && W1UndoCount(c) == n, "RE1: no entry", Ctx.Short(c.LastText));
        c.Check((int)c.Ok(new { cmd = "ragdoll.obstacle", key = x.Item, on = false }, "RE1: on false")["obstacles"] == 0 && W1TopLabel(c) == "Obstacle", "RE1: it stops, one entry", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "undo" }, "(back)");

        // the follower: a folder and a constraint in one entry
        n = W1UndoCount(c);
        int nodes = x.App.Session.Model.PreOrder().Count();
        var f = c.Ok(new { cmd = "ragdoll.follower", key = k, part = "hand_L" }, "RE1: ragdoll.follower");
        c.Check(W1UndoCount(c) == n + 1 && W1TopLabel(c) == "Follower" && x.App.Session.Model.PreOrder().Count() == nodes + 1 && x.App.Session.Model.Constraints?.Count == 1, "RE1: one entry «Follower»: the folder and the constraint", W1TopLabel(c));
        c.Ok(new { cmd = "undo" }, "RE1: undo");
        c.Check(x.App.Session.Model.Find((string)f["folder"]) == null && (x.App.Session.Model.Constraints?.Count ?? 0) == 0, "RE1: one undo takes both");
        c.Ok(new { cmd = "redo" }, "RE1: redo");
        c.Check(x.App.Session.Model.Find((string)f["folder"]) != null && x.App.Session.Model.Constraints?.Count == 1, "RE1: redo brings both");
        c.Ok(new { cmd = "undo" }, "(back)");

        // a grab that ends as a pin: the operation the tool calls
        var body = C47Body(x, k);
        var part = body.Part("hand_R");
        var grab = x.Part.World.Grab(part.Rb, part.Rb.worldCenterOfMass);
        c.Check(grab != null && x.Part.World.Grabs.Count >= 1, "RE1: a plain grab");
        c.Check(W1UndoCount(c) == n, "RE1: a plain grab has no entry");
        var pin = x.Ops.RagdollPin(k, "hand_R", grab.Point, null, null, false, new ExecuteOptions { Source = CommandSource.User });
        grab.Drop();
        c.Check(pin.Ok && W1UndoCount(c) == n + 1 && W1TopLabel(c) == "Pin" && C47Block(x, k).Pins.Count == 1 && body.Pins.Count == 1, "RE1: a grab that ends as a pin: one entry «Pin»", pin.Message);
        var follow = x.Ops.RagdollPin(k, "hand_L", null, x.Item);
        c.Check(follow.Ok && x.Ops.RagdollStay(k, C47Block(x, k).Pins[1].Id).Ok && !C47Block(x, k).Pins[1].IsFollowing && W1TopLabel(c) == "Pin" && W1UndoCount(c) == n + 3, "RE1: a pin that follows, then stays: one entry each", W1UndoCount(c) - n + "");
        c.Ok(new { cmd = "undo" }, "(back)");
        c.Ok(new { cmd = "undo" }, "(back)");
        c.Ok(new { cmd = "undo" }, "(back)");

        // what acts once: no history
        C47NoEntry(c, new { cmd = "ragdoll.push", key = k, part = "chest", impulse = new[] { 0, 0, 10 } }, "RE1: a push");
        C47NoEntry(c, new { cmd = "ragdoll.turn", key = k }, "RE1: a turn");
        C47NoEntry(c, new { cmd = "ragdoll.settle", key = k, seconds = 0.1 }, "RE1: a settle");
        C47NoEntry(c, new { cmd = "ragdoll.knock", key = k }, "RE1: a knock");
        C47NoEntry(c, new { cmd = "fx.shoot", from = new[] { 0f, 9f, 12f }, target = new { key = k } }, "RE1: a shot");
        C47NoEntry(c, new { cmd = "ragdoll.throw", from = new[] { 0f, 9f, 12f }, target = new[] { 0f, 8f, 0f } }, "RE1: a throw");
        C47NoEntry(c, new { cmd = "ragdoll.parts", key = k }, "RE1: a read");
        C47NoEntry(c, new { cmd = "ragdoll.status" }, "RE1: a read");

        // the release, and what it clears
        c.Ok(new { cmd = "ragdoll.pin", key = k }, "RE1: a pin");
        c.Ok(new { cmd = "ragdoll.tie", key = k }, "RE1: a tie");
        c.Ok(new { cmd = "ragdoll.lock", key = k }, "RE1: holds");
        c.Ok(new { cmd = "ragdoll.set", key = k, values = C47V("muscle", 25) }, "RE1: a value");
        C47Entry(c, x, new { cmd = "ragdoll.release", key = k }, "Release ragdoll", "RE1: ragdoll.release");
        var b = C47Block(x, k);
        c.Check(!b.On && b.Pins.Count == 0 && b.Ties.Count == 0 && b.Holds.Count == 0 && b.Pose.IsEmpty && b.Values.Muscle == 25f, "RE1: a release clears pins, ties, holds and pose, the values stay", b.ToJson());
        C47Reset(c, x);
        c.Ok(new { cmd = "ragdoll.build", key = x.Chara, undo = false }, "RE1: a body");
        c.Ok(new { cmd = "ragdoll.build", key = x.Other, undo = false }, "RE1: a second body");
        c.Ok(new { cmd = "ragdoll.pusher", key = x.Other, undo = false }, "RE1: a pusher");
        n = W1UndoCount(c);
        c.Ok(new { cmd = "ragdoll.release" }, "RE1: a release with no key");
        c.Check(W1UndoCount(c) == n + 1 && W1TopLabel(c) == "Release ragdoll" && x.Part.World == null, "RE1: one entry for every body and the pushers", W1UndoCount(c) - n + "");
        c.Ok(new { cmd = "undo" }, "RE1: its undo");
        c.Check(C47Body(x, x.Chara) != null && C47Body(x, x.Other) != null && x.App.Session.Model.Ragdoll?.Pushers.Count == 1 && x.Part.World?.Pushers.Count == 1, "RE1: every body and the pusher come back", C47WorldJson(x));
        c.Ok(new { cmd = "redo" }, "RE1: its redo");
        c.Check(C47Body(x, x.Chara) == null && C47Body(x, x.Other) == null && x.App.Session.Model.Ragdoll == null, "RE1: gone again", C47WorldJson(x));
        C47Reset(c, x);
    }

    // ---------------------------------------------------------------- RE2

    static void C47Clock(Ctx c, C47Fix x)
    {
        C47Reset(c, x);
        c.Ok(new { cmd = "ragdoll.build", key = x.Chara }, "RE2: a body");
        var body = C47Body(x, x.Chara);
        c.Frames(2);
        var start = body.Parts[0].Tr.position;
        c.Ok(new { cmd = "ragdoll.knock", key = x.Chara }, "RE2: a knock");
        c.Ok(new { cmd = "ragdoll.push", key = x.Chara, local = new[] { 0, 0, -300 } }, "RE2: a push");
        int n = W1UndoCount(c);
        long version = x.App.Session.Commands.Version;
        double most = 0;
        var seen = new HashSet<RagdollState>();
        for (int i = 0; i < 600; i++)
        {
            c.Frames(1);
            seen.Add(body.State);
            most = Math.Max(most, C47Moved(body, start));
        }
        c.Check(W1UndoCount(c) == n, "RE2: 600 frames of a running clock add no entry", "states " + string.Join(", ", seen) + "; moved up to " + most.ToString("F3") + "; relocations " + body.Relocations + "; version +" + (x.App.Session.Commands.Version - version));
        c.Check(most >= 1.0, "RE2: the body moved in them", most.ToString("F3"));
        c.Ok(new { cmd = "ragdoll.pin", key = x.Chara }, "RE2: a pin in the same run");
        c.Check(W1UndoCount(c) == n + 1, "RE2: a pin adds one", W1UndoCount(c) - n + "");
        C47Reset(c, x);
    }

    // ---------------------------------------------------------------- RE3

    static void C47Merge(Ctx c, C47Fix x)
    {
        C47Reset(c, x);
        c.Ok(new { cmd = "ragdoll.build", key = x.Chara }, "RE3: a body");
        int n = W1UndoCount(c);
        string block = C47BlockJson(x, x.Chara);
        var o = new ExecuteOptions { MergeKey = "ragdoll.muscle", Source = CommandSource.User };
        foreach (float v in new[] { 21f, 22f, 23f, 24f })
            c.Check(x.Ops.RagdollValue(x.Chara, "muscle", new JValue(v), o).Ok, "RE3: a drag's step " + v);
        c.Check(W1UndoCount(c) == n + 1 && W1TopLabel(c) == "Ragdoll settings" && C47Block(x, x.Chara).Values.Muscle == 24f && C47Body(x, x.Chara).Values.Muscle == 24f, "RE3: four steps of a merge key are one entry, the body follows each", W1UndoCount(c) - n + "");
        c.Ok(new { cmd = "undo" }, "RE3: undo");
        c.Check(C47BlockJson(x, x.Chara) == block, "RE3: one undo takes the whole drag back");
        c.Ok(new { cmd = "redo" }, "RE3: redo");
        c.Check(C47Block(x, x.Chara).Values.Muscle == 24f, "RE3: redo gives the last value");
        var w = new ExecuteOptions { MergeKey = "ragdoll.gravity", Source = CommandSource.User };
        foreach (float v in new[] { 90f, 80f, 70f })
            c.Check(x.Ops.RagdollValue(null, "gravity", new JValue(v), w).Ok, "RE3: a drag of the scene's value " + v);
        c.Check(W1UndoCount(c) == n + 2 && x.App.Session.Model.Ragdoll?.Values.Gravity == 70f && x.Part.World.Values.Gravity == 70f, "RE3: one entry for the scene's block too, the world follows", W1UndoCount(c) - n + "");
        c.Check(x.Ops.RagdollValue(x.Chara, "muscle", new JValue(30f), new ExecuteOptions { MergeKey = "other", Source = CommandSource.User }).Ok, "RE3: another key");
        c.Check(W1UndoCount(c) == n + 3, "RE3: another key is another entry", W1UndoCount(c) - n + "");
        C47Reset(c, x);
    }

    // ---------------------------------------------------------------- RE4

    static void C47NoHistory(Ctx c, C47Fix x)
    {
        C47Reset(c, x);
        c.Ok(new { cmd = "obj.transform", key = x.Item, pos = new[] { 0f, 10f, 12f } }, "RE4: something on the history");
        int n = W1UndoCount(c);
        string top = W1TopLabel(c);
        void Quiet(object req, Func<bool> done, string what)
        {
            c.Ok(req, what);
            c.Check(done(), what + ": it did its work", Ctx.Short(c.LastText));
            c.Check(W1UndoCount(c) == n && W1TopLabel(c) == top, what + ": no entry, the history kept", W1UndoCount(c) - n + "");
        }
        string k = x.Chara;
        Quiet(new { cmd = "ragdoll.build", key = k, undo = false, room = new[] { -20f, 20f, -20f, 20f } }, () => C47Body(x, k) != null, "RE4: ragdoll.build with a room");
        Quiet(new { cmd = "ragdoll.lock", key = k, undo = false }, () => C47Block(x, k).Holds.Count == 6, "RE4: ragdoll.lock");
        Quiet(new { cmd = "ragdoll.tie", key = k, undo = false }, () => C47Block(x, k).Ties.Count == 1, "RE4: ragdoll.tie");
        Quiet(new { cmd = "ragdoll.pin", key = k, undo = false }, () => C47Block(x, k).Pins.Count == 1, "RE4: ragdoll.pin");
        Quiet(new { cmd = "ragdoll.unpin", undo = false }, () => C47Block(x, k).Pins.Count == 0, "RE4: ragdoll.unpin");
        Quiet(new { cmd = "ragdoll.set", key = k, values = C47V("muscle", 25, "gravity", 80), undo = false }, () => C47Block(x, k).Values.Muscle == 25f && x.App.Session.Model.Ragdoll?.Values.Gravity == 80f, "RE4: ragdoll.set in both blocks");
        Quiet(new { cmd = "ragdoll.pusher", key = k, undo = false }, () => x.Part.World.Pushers.Count == 1, "RE4: ragdoll.pusher");
        Quiet(new { cmd = "ragdoll.obstacle", key = x.Item, undo = false }, () => x.App.Session.Model.Ragdoll?.Obstacles.Count == 1, "RE4: ragdoll.obstacle");
        Quiet(new { cmd = "ragdoll.release", key = k, undo = false }, () => C47Body(x, k) == null && !C47Block(x, k).On, "RE4: ragdoll.release with a key");
        Quiet(new { cmd = "ragdoll.build", key = k, undo = false }, () => C47Body(x, k) != null, "RE4: ragdoll.build");
        Quiet(new { cmd = "ragdoll.bake", key = k, undo = false }, () => C47Body(x, k) == null && ((CharaNodeState)x.App.Session.Model.Find(k).State).Fk.Enabled, "RE4: ragdoll.bake");
        c.Ok(new { cmd = "fk.enable", key = k, on = false, undo = false }, "(FK off again)");
        Quiet(new { cmd = "ragdoll.build", key = k, undo = false }, () => C47Body(x, k) != null, "RE4: a body again");
        Quiet(new { cmd = "ragdoll.release", undo = false }, () => x.Part.World == null, "RE4: ragdoll.release with no key");
        // a structural change without history empties it, as for every command that adds a node
        c.Ok(new { cmd = "ragdoll.build", key = k, undo = false }, "(a body)");
        var f = c.Ok(new { cmd = "ragdoll.follower", key = k, undo = false }, "RE4: ragdoll.follower");
        c.Check(W1UndoCount(c) == 0 && x.App.Session.Model.Find((string)f["folder"]) != null, "RE4: a folder added with no entry empties the history, as any node added so", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "delete", keys = new[] { (string)f["folder"] }, undo = false }, "(the folder goes)");
        foreach (var e in (x.App.Session.Model.Constraints?.Entries ?? new List<ConstraintEntry>()).ToList()) x.Ops.RemoveConstraint(e.Id, ExecuteOptions.NotRecorded);
        C47Reset(c, x);
    }

    // ---------------------------------------------------------------- RE5

    static void C47Undo(Ctx c, C47Fix x)
    {
        C47Reset(c, x);
        string k = x.Chara;
        var r = x.App.Runtime.FindChara(k);
        var hand = r.Context.BodyBones["cf_J_Hand_R"];
        c.Frames(2);
        var before = hand.position;

        // undo of a build: at once, no blend, with a blend time set
        c.Ok(new { cmd = "ragdoll.set", values = C47V("blendOut", 1), undo = false }, "RE5: a blend time");
        c.Ok(new { cmd = "ragdoll.build", key = k }, "RE5: a build");
        c.Ok(new { cmd = "ragdoll.set", key = k, preset = "limp", undo = false }, "RE5: limp");
        c.Ok(new { cmd = "ragdoll.push", key = k, part = "chest", local = new[] { 0, 0, -300 } }, "RE5: a push");
        c.Frames(30);
        double moved = (hand.position - before).magnitude;
        c.Ok(new { cmd = "undo" }, "RE5: undo of the build");
        c.Check(C47Body(x, k) == null && !C47Block(x, k).On, "RE5: the body goes at once, with a blend time set", "");
        c.Check(x.Part.World == null, "RE5: the world with the last body");
        c.Frames(1);
        c.Check(moved >= 1.0 && (hand.position - before).magnitude < 1e-4, "RE5: the hand is back where it was", "moved " + moved.ToString("F3") + ", then " + (hand.position - before).magnitude.ToString("G4"));
        c.Ok(new { cmd = "redo" }, "RE5: redo of the build");
        c.Check(C47Body(x, k) != null && C47Block(x, k).On, "RE5: a body again");
        C47Reset(c, x);

        // undo of a release: a new body at the pose at rest
        c.Ok(new { cmd = "ragdoll.build", key = k }, "RE5: a build");
        var body = C47Body(x, k);
        c.Ok(new { cmd = "ragdoll.set", key = k, preset = "limp" }, "RE5: limp");
        c.Frames(420);
        C47Pause(c, true);
        var awake = body.Parts.Where(p => !p.Rb.IsSleeping()).Count();
        var speed = body.Parts.Max(p => p.Rb.linearVelocity.magnitude);
        var target = body.Parts.Select(p => p.TargetBoneRot).ToArray();
        var rest = body.Parts.Select(p => p.Tr.rotation).ToArray();
        double far = Enumerable.Range(0, 16).Max(i => RagdollTestDeg(body.Parts[i].BoneRotation, target[i]));
        c.Check(body.SettledParts != null, "RE5: the body came to rest", "awake " + awake + ", speed " + speed.ToString("G4"));
        c.Check(far > 10.0, "RE5: its pose is not the pose sources'", "greatest angle " + far.ToString("F2"));
        int n = W1UndoCount(c);
        c.Ok(new { cmd = "ragdoll.release", key = k }, "RE5: a release");
        c.Check(C47Body(x, k) == null && C47Block(x, k).Pose.IsEmpty && W1UndoCount(c) == n + 1, "RE5: the body goes, the block's pose with it");
        c.Ok(new { cmd = "undo" }, "RE5: undo of the release");
        var again = C47Body(x, k);
        c.Check(again != null && again != body && C47Block(x, k).On && !C47Block(x, k).Pose.IsEmpty, "RE5: a new body, the block holds the pose", C47Block(x, k).Pose.IsEmpty + "");
        if (again != null)
        {
            double worst = Enumerable.Range(0, 16).Max(i => RagdollTestDeg(again.Parts[i].Tr.rotation, rest[i]));
            c.Check(worst <= 1e-3, "RE5: at the pose at rest", "greatest angle " + worst.ToString("G4") + " against " + far.ToString("F2") + " from the pose sources");
        }
        c.Ok(new { cmd = "redo" }, "RE5: redo of the release");
        c.Check(C47Body(x, k) == null && !C47Block(x, k).On, "RE5: gone again");
        C47Reset(c, x);

        // an undo inside a fall does not rewind it
        c.Ok(new { cmd = "ragdoll.build", key = k }, "RE5: a build");
        body = C47Body(x, k);
        c.Ok(new { cmd = "ragdoll.set", key = k, preset = "limp" }, "RE5: limp");
        c.Frames(2);
        c.Ok(new { cmd = "ragdoll.push", key = k, part = "chest", local = new[] { 0, 0, -300 } }, "RE5: a push");
        c.Ok(new { cmd = "ragdoll.set", key = k, values = C47V("mass", 60) }, "RE5: a value");
        c.Frames(10);
        C47Pause(c, true);
        var at = body.Parts[0].Tr.position;
        c.Ok(new { cmd = "undo" }, "RE5: undo inside the fall");
        c.Check(C47Body(x, k) == body && (body.Parts[0].Tr.position - at).magnitude < 1e-5, "RE5: the same body where it was: physics is not rewound", (body.Parts[0].Tr.position - at).magnitude.ToString("G4"));
        C47Reset(c, x);
    }

    static double RagdollTestDeg(Quaternion a, Quaternion b) => RagdollMath.AngleDeg(a.x, a.y, a.z, a.w, b.x, b.y, b.z, b.w);

    // ---------------------------------------------------------------- a batch

    static void C47Batch(Ctx c, C47Fix x)
    {
        C47Reset(c, x);
        string k = x.Chara;
        string block = C47BlockJson(x, k);
        int n = W1UndoCount(c);
        c.Ok(new
        {
            cmd = "batch",
            cmds = new object[]
            {
                new { cmd = "ragdoll.build", key = k, room = new[] { -20f, 20f, -20f, 20f } },
                new { cmd = "ragdoll.tie", key = k },
                new { cmd = "ragdoll.pin", key = k },
                new { cmd = "ragdoll.lock", key = k },
                new { cmd = "ragdoll.set", key = k, preset = "limp", values = C47V("gravity", 80) },
                new { cmd = "ragdoll.pusher", key = k },
            },
        }, "RE1: a batch of six");
        c.Check(W1UndoCount(c) == n + 1 && W1TopLabel(c) == "Batch", "RE1: a batch is one entry", Ctx.Short(c.LastText));
        c.Check(C47Body(x, k)?.TieCount == 1 && C47Body(x, k)?.Pins.Count == 1 && C47Block(x, k).Holds.Count == 6, "RE1: each member sees the one before");
        c.Ok(new { cmd = "undo" }, "RE1: undo of the batch");
        c.Check(C47BlockJson(x, k) == block && x.App.Session.Model.Ragdoll == null && C47Body(x, k) == null && x.Part.World == null, "RE1: one undo takes it all", C47WorldJson(x));
        C47Reset(c, x);
    }

    // ---------------------------------------------------------------- RC7

    static void C47Forms(Ctx c, C47Fix x)
    {
        C47Reset(c, x);
        string k = x.Chara;
        var res = new List<string>();
        // the first scene: a body in a strip of a room, a pusher, a tie, a shot, pushes, a settle
        c.Ok(new { cmd = "ragdoll.build", key = k, room = new[] { -30f, 30f, -15f, 15f } }, "RC7: {cmd: ragdoll.build, key, room}");
        var body = C47Body(x, k);
        c.Check(body != null && x.Part.World.RoomCenter == Vector2.zero && x.Part.World.RoomSize.x == 60f, "RC7: a body in that room");
        c.Frames(2);
        var start = body.Parts[0].Tr.position;
        c.Ok(new { cmd = "ragdoll.pusher", key = x.Other, a = "cf_J_LegLow01_R", b = "cf_J_Toes01_R", radius = 0.5f }, "RC7: {cmd: ragdoll.pusher, key, a, b, radius}");
        c.Ok(new { cmd = "ragdoll.tie", key = k, a = "hand_L", b = "hand_R" }, "RC7: {cmd: ragdoll.tie, key, a, b}");
        var shot = c.Ok(new { cmd = "fx.shoot", from = x.Item, target = new { key = k, bone = "cf_J_Spine02" }, spread = 0.5, kill = true, momentum = 40 }, "RC7: {cmd: fx.shoot, from: <item>, target: {key, bone}, spread, kill, momentum}");
        c.Check((int)shot["hits"] == 1, "RC7: the shot hit the body", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "ragdoll.push", key = k, part = "head", local = new[] { 0f, 0f, -100f } }, "RC7: {cmd: ragdoll.push, key, part, local}");
        c.Ok(new { cmd = "ragdoll.push", key = k, part = "chest", local = new[] { 0f, 0f, -250f } }, "RC7: a second push");
        c.Frames(20);
        c.Ok(new { cmd = "ragdoll.push", key = k, part = "chest", local = new[] { 0f, 0f, -150f } }, "RC7: a third push");
        C47Pause(c, true);
        var settle = c.Ok(new { cmd = "ragdoll.settle", key = k, seconds = 5.5, gain = 16, damp = 3 }, "RC7: {cmd: ragdoll.settle, key, seconds, gain, damp}");
        c.Check((bool?)settle["ok2"] == true && Math.Abs(body.SettleLeft - 5.5f) < 1e-5f, "RC7: the settle runs", body.SettleLeft + "");
        C47Pause(c, false);
        c.Frames(60);
        c.Check(C47Moved(body, start) >= 1.0, "RC7: the body moved", "moved " + C47Moved(body, start).ToString("F3"));
        // what the fall scripts add
        c.Ok(new { cmd = "ragdoll.lock", key = k, parts = "upperarm,forearm" }, "RC7: ragdoll.lock");
        c.Ok(new { cmd = "ragdoll.turn", key = k, dir = new[] { 0, 1, 0 }, strength = 2 }, "RC7: ragdoll.turn");
        c.Check(c.Ok(new { cmd = "ragdoll.parts", key = k }, "RC7: ragdoll.parts")["parts"]?["chest"]?["forward"] is JArray, "RC7: what the fall scripts read");
        // a shot that kills, the other forms of a target
        c.Ok(new { cmd = "ragdoll.build", key = x.Other }, "RC7: a second body");
        c.Frames(1);
        var other = C47Body(x, x.Other);
        var hip = other.Parts[0].Tr.position;
        var eye = new Vector3(hip.x, hip.y + 2f, hip.z + 12f);
        var line = C47Line(x, eye, Vector3.back);
        string places = "BR-Chan's node at " + x.App.Runtime.HolderOf(k).position.ToString("F3") + ", pelvis at " + body.Parts[0].Tr.position.ToString("F3") + "; HERO107's node at " + x.App.Runtime.HolderOf(x.Other).position.ToString("F3") + ", pelvis at " + hip.ToString("F3") + "; on the line: " + line;
        var kill = c.Ok(new { cmd = "fx.shoot", from = new[] { hip.x, hip.y + 2f, hip.z + 12f }, target = new[] { hip.x, hip.y + 2f, hip.z }, kill = true, momentum = 181 }, "RC7: {cmd: fx.shoot, from: [x, y, z], target: [x, y, z], kill}");
        c.Check((int)kill["hits"] == 1 && other.State == RagdollState.Limp, "RC7: the first body hit goes limp", kill["hits"] + " " + other.State + "; " + places);
        C47Accessory(c, k, new { key = k, bone = "cf_J_Spine02" }, "RC7");
        int undo = W1UndoCount(c);
        c.Ok(new { cmd = "ragdoll.release" }, "RC7: ragdoll.release");
        c.Check(x.Part.World == null && W1UndoCount(c) == undo + 1, "RC7: every body and the world go");
        C47Reset(c, x);
    }

    // {key, accessory} (Phase 6.9): with the clock stopped, the ray starts at the muzzle chara.accessoryAxis reads for slot 3 (within 1e-4); an empty slot
    // of the character answers not_found
    static void C47Accessory(Ctx c, string k, object target, string id)
    {
        C47Pause(c, true);
        var axis = c.Ok(new { cmd = "chara.accessoryAxis", key = k, slot = 3 }, id + ": chara.accessoryAxis of slot 3");
        var shot = c.Ok(new { cmd = "fx.shoot", from = new { key = k, accessory = 3 }, target }, id + ": {key, accessory} of slot 3");
        double d = double.NaN;
        if (axis["muzzle"] is JArray am && shot["muzzle"] is JArray sm && am.Count == 3 && sm.Count == 3)
            d = Math.Sqrt(Enumerable.Range(0, 3).Sum(i => ((double)sm[i] - (double)am[i]) * ((double)sm[i] - (double)am[i])));
        c.Check(d <= 1e-4, id + ": {key, accessory}: the ray starts at chara.accessoryAxis's muzzle (distance " + d.ToString("G3") + ", bar 1e-4)", Ctx.Short(c.LastText));
        int empty = -1;
        for (int s = 0; s < 20 && empty < 0; s++)
        {
            var r = c.Call(new { cmd = "chara.accessoryAxis", key = k, slot = s });
            if ((string)r["code"] == "not_found" && ((string)r["error"] ?? "").Contains("no accessory in slot " + s)) empty = s;
        }
        if (c.Check(empty >= 0, id + ": an empty accessory slot of the character: " + empty))
        {
            var e = c.Call(new { cmd = "fx.shoot", from = new { key = k, accessory = empty }, target });
            c.Check((string)e["code"] == "not_found" && ((string)e["error"] ?? "").Contains("no accessory in slot " + empty), id + ": {key, accessory} of the empty slot " + empty + ": not_found", Ctx.Short(c.LastText));
        }
        C47Pause(c, false);
    }

    // the characters on a ray, nearest first, by the world's own query
    static string C47Line(C47Fix x, Vector3 origin, Vector3 dir)
    {
        var hits = new RaycastHit[64];
        int n = x.Part.World.Physics.Raycast(origin, dir, hits, 3000f, ~0, QueryTriggerInteraction.Ignore);
        var seen = new List<string>();
        foreach (var h in hits.Take(n).OrderBy(h => h.distance))
        {
            var p = h.rigidbody ? x.Part.World.PartOf(h.rigidbody) : null;
            if (p == null) continue;
            string who = p.Body.NodeId == x.Chara ? "BR-Chan" : "HERO107";
            if (!seen.Contains(who)) seen.Add(who);
        }
        return seen.Count == 0 ? "no character" : string.Join(" then ", seen);
    }

    // ---------------------------------------------------------------- RD5

    static void C47Shots(Ctx c, C47Fix x)
    {
        C47Reset(c, x);
        string k = x.Chara;

        // no world: a tracer, no hit, no world made
        var free = c.Ok(new { cmd = "fx.shoot", from = new[] { 0f, 5f, 10f }, target = new[] { 0f, 5f, 0f } }, "RD5: a shot with no world");
        c.Check(C47Members(free).SequenceEqual(new[] { "hits", "muzzle", "target" }) && (int)free["hits"] == 0 && x.Part.World == null, "RD5: hits 0, no world made", Ctx.Short(c.LastText));
        c.Check(W1V3(free["muzzle"]) == new Vector3(0f, 5f, 10f) && Math.Abs(W1V3(free["target"]).z + 590f) < 1e-3, "RD5: from the muzzle, 600 units along", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "ragdoll.build", key = k }, "RD5: a body");
        c.Frames(1);
        var body = C47Body(x, k);
        var spine = x.App.Runtime.FindChara(k).Context.BodyBones["cf_J_Spine02"];

        // the forms of a target
        var aim = c.Ok(new { cmd = "fx.shoot", from = new[] { spine.position.x, spine.position.y, spine.position.z + 12f }, target = new { key = k } }, "RD5: target {key}");
        c.Check((int)aim["hits"] == 1, "RD5: a character with no bone means " + "cf_J_Spine02", Ctx.Short(c.LastText));
        var offset = c.Ok(new { cmd = "fx.shoot", from = new[] { 0f, spine.position.y + 50f, 12f }, target = new { key = k, bone = "cf_J_Head", offset = new[] { 0f, 40f, 0f } } }, "RD5: target {key, bone, offset}");
        c.Check((int)offset["hits"] == 0, "RD5: the offset moves the aim off the body", Ctx.Short(c.LastText));
        var onItem = c.Ok(new { cmd = "fx.shoot", from = new[] { 0f, 10f, 30f }, target = new { key = x.Item } }, "RD5: target {key} on an item");
        c.Check(W1V3(onItem["muzzle"]) == new Vector3(0f, 10f, 30f), "RD5: an item's place, no bone", Ctx.Short(c.LastText));
        C45Err(c, new { cmd = "fx.shoot", from = new[] { 0f, 10f, 30f }, target = new { key = k, bone = "nope" } }, "not_found", "target.bone", "RD5: a bone that is none");
        C45Err(c, new { cmd = "fx.shoot", from = new[] { 0f, 10f, 30f }, target = new { key = C47Nope } }, "not_found", "target.key", "RD5: a node that is none");
        C45Err(c, new { cmd = "fx.shoot", from = new[] { 0f, 10f, 30f }, target = new { bone = "x" } }, "bad_args", "target.key", "RD5: {bone} alone");
        C45Err(c, new { cmd = "fx.shoot", from = new[] { 0f, 10f, 30f }, target = 3 }, "bad_args", "target", "RD5: a number is no target");
        C45Err(c, new { cmd = "fx.shoot", from = new[] { 0f, 10f, 30f } }, "bad_args", "target", "RD5: no target");
        C45Err(c, new { cmd = "fx.shoot", target = new[] { 0f, 10f, 30f } }, "bad_args", "from", "RD5: no from");
        C45Err(c, new { cmd = "fx.shoot", from = new[] { 1f, 2f, 3f }, target = new[] { 1f, 2f, 3f } }, "bad_args", "target", "RD5: from and target the same point");
        C45Err(c, new { cmd = "fx.shoot", from = new[] { 0f, 10f, 30f }, target = new[] { 0f, 0f, 0f }, spread = -1 }, "bad_args", "spread", "RD5: a spread below 0");

        // the forms of from
        var item = c.Ok(new { cmd = "fx.shoot", from = x.Item, target = new[] { 0f, 10f, -5f } }, "RD5: from an item");
        var holder = x.App.Runtime.HolderOf(x.Item).position;
        var far = W1V3(item["muzzle"]);
        c.Check((far - holder).z <= 0f && (far - holder).magnitude < 20f, "RD5: its far end towards the target", far + " from " + holder);
        var bone = c.Ok(new { cmd = "fx.shoot", from = new { key = x.Other, bone = "cf_J_Hand_R", offset = new[] { 0f, 0f, 1f } }, target = new[] { 0f, 0f, 30f } }, "RD5: from {key, bone, offset}");
        var hand = x.App.Runtime.FindChara(x.Other).Context.BodyBones["cf_J_Hand_R"];
        c.Check((W1V3(bone["muzzle"]) - hand.TransformPoint(new Vector3(0f, 0f, 1f))).magnitude < 1e-3, "RD5: the offset in the bone's space", bone["muzzle"].ToString(Formatting.None));
        var node = c.Ok(new { cmd = "fx.shoot", from = new { key = x.Other }, target = new[] { 0f, 0f, 30f } }, "RD5: from {key}");
        c.Check((W1V3(node["muzzle"]) - x.App.Runtime.HolderOf(x.Other).position).magnitude < 1e-4, "RD5: from a key with no bone the node's place: the default bone is the target's", node["muzzle"].ToString(Formatting.None));
        C47Accessory(c, k, new[] { 0f, 0f, 0f }, "RD5");
        C45Err(c, new { cmd = "fx.shoot", from = x.Chara, target = new[] { 0f, 0f, 0f } }, "wrong_kind", "from", "RD5: a character's key is no item");

        // arguments
        var soft = c.Ok(new { cmd = "fx.shoot", from = new[] { spine.position.x, spine.position.y, spine.position.z + 12f }, target = new[] { spine.position.x, spine.position.y, spine.position.z }, momentum = 40 }, "RD5: momentum");
        c.Check((int)soft["hits"] == 1, "RD5: a weak shot hits", Ctx.Short(c.LastText));
        int sha = W1UndoCount(c);
        var stain = c.Ok(new { cmd = "fx.shoot", from = new[] { spine.position.x, spine.position.y, spine.position.z + 12f }, target = new[] { spine.position.x, spine.position.y, spine.position.z }, momentum = 40, stain = true }, "RD5: stain true, with no graphics device");
        c.Check((int)stain["hits"] == 1 && W1UndoCount(c) == sha, "RD5: the shot hits, no entry", Ctx.Short(c.LastText));
        var a = new Vector3[4];
        for (int i = 0; i < 4; i++)
            a[i] = W1V3(c.Ok(new { cmd = "fx.shoot", from = new[] { 0f, 50f, 0f }, target = new[] { 0f, 50f, 100f }, spread = 5 }, "RD5: spread")["target"]);
        c.Check(a.Distinct().Count() == 4 && a.All(p => Math.Abs(p.x) < 60f && Math.Abs(p.y - 50f) < 60f), "RD5: spread turns each shot differently, in the disc", a[0] + " " + a[1]);

        // a release with no key takes the effects along, one with a key leaves them
        var shots = RagdollShots.Of(x.App.Runtime);
        c.Check(shots?.Fx != null, "RD5: the shots left effects", "tracers " + shots?.Fx?.Tracers + ", dust " + shots?.Fx?.Dust + ", droplets " + shots?.Fx?.Droplets);
        c.Ok(new { cmd = "ragdoll.release", key = k, undo = false }, "RD5: a release with a key");
        c.Check(shots?.Fx != null, "RD5: it leaves the effects");
        c.Ok(new { cmd = "ragdoll.release", undo = false }, "RD5: a release with no key");
        c.Check(shots != null && shots.Fx == null && x.Part.World == null, "RD5: the effects go with the world");
        C47Reset(c, x);
    }

    // ---------------------------------------------------------------- timed commands

    static void C47Timed(Ctx c, C47Fix x)
    {
        C47Reset(c, x);
        var pb = x.App.Runtime.GetPart<HS2Next.Scenes.Runtime.TimelinePlayback>();
        if (!c.Check(pb != null, "RC7: the playback")) return;
        string k = x.Chara;
        c.Ok(new { cmd = "obj.transform", key = x.Item, pos = new[] { 0f, 10f, 12f } }, "RC7: something on the history");
        c.Ok(new
        {
            cmd = "timeline.build", duration = 1, tracks = new object[0], play = false,
            events = new object[]
            {
                new { t = 0, @do = (object)new { cmd = "ragdoll.release" } },
                new { t = 0.1, @do = (object)new { cmd = "ragdoll.build", key = k, room = new[] { -20f, 20f, -15f, 15f } } },
                new { t = 0.2, @do = (object)new { cmd = "ragdoll.pusher", key = k, a = "cf_J_LegLow01_R", b = "cf_J_Toes01_R", radius = 0.4 } },
                new { t = 0.3, @do = (object)new { cmd = "ragdoll.tie", key = k, a = "hand_L", b = "hand_R" } },
                new { t = 0.4, @do = (object)new { cmd = "fx.shoot", from = x.Item, target = new { key = k, bone = "cf_J_Spine02" }, momentum = 40 } },
                new { t = 0.5, @do = (object)new { cmd = "ragdoll.push", key = k, part = "chest", local = new[] { 0, 0, -250 } } },
                new { t = 0.6, @do = (object)new { cmd = "ragdoll.settle", key = k, seconds = 5.5, gain = 16, damp = 3 } },
            },
        }, "RC7: a Timeline with seven timed commands");
        int built = W1UndoCount(c);
        var scenes = new List<string>();
        for (int run = 0; run < 2; run++)
        {
            long fired = pb.EventsFired;
            c.Ok(new { cmd = "timeline.play" }, "RC7: play, run " + (run + 1));
            int n = 0;
            string scene = null;
            while (pb.Player.Time < 0.95 && n < 400)
            {
                x.App.Frame(Dt);
                n++;
                if (scene == null && x.Part.World != null) scene = x.Part.World.Scene.handle.ToString();
            }
            x.App.Frame(Dt);
            x.App.Frame(Dt);
            pb.CollectReplies();
            c.Check(pb.EventErrors == 0, "RC7: no error, run " + (run + 1), pb.LastEventError?.Message);
            c.Check(pb.EventsFired - fired >= 6, "RC7: the events fired, run " + (run + 1), (pb.EventsFired - fired) + "");
            c.Check(C47Body(x, k) != null && C47Body(x, k).TieCount == 1 && x.Part.World.Pushers.Count == 1, "RC7: the body, the tie and the pusher are there, run " + (run + 1), C47BlockJson(x, k));
            c.Check(W1UndoCount(c) == built, "RC7: a timed command leaves the history alone, run " + (run + 1), W1UndoCount(c) - built + "");
            scenes.Add(scene);
            c.Ok(new { cmd = "timeline.stop" }, "RC7: stop");
        }
        c.Check(scenes.Distinct().Count() == 2, "RC7: a release at 0 starts the next run in a fresh world", string.Join(", ", scenes));
        c.Ok(new { cmd = "timeline.clear", undo = false }, "(the Timeline goes)");
        C47Reset(c, x);
    }

    // ---------------------------------------------------------------- ragdoll.throw, ragdoll.obstacle

    static void C47Throw(Ctx c, C47Fix x)
    {
        C47Reset(c, x);
        var r = c.Ok(new { cmd = "ragdoll.throw", from = new[] { 0f, 8f, 30f }, target = new[] { 0f, 8f, 0f } }, "RC4: ragdoll.throw with no world");
        c.Check(C47Members(r).SequenceEqual(new[] { "props" }) && (int)r["props"] == 1 && x.Part.World != null, "RC4: a world came with the prop", Ctx.Short(c.LastText));
        var p = x.Part.World.Props.All[0];
        c.Check(p.Kind == "ball" && p.Mass == 2f && Math.Abs(p.Rb.linearVelocity.z + 80f) < 1e-3, "RC4: the scene's defaults, along the ray", p.Kind + " " + p.Mass + " " + p.Rb.linearVelocity);
        r = c.Ok(new { cmd = "ragdoll.throw", ray = new { origin = new[] { 0f, 8f, 30f }, dir = new[] { 0f, 0f, -2f } }, kind = "heavy", mass = 3, speed = 40, size = 1 }, "RC4: ragdoll.throw with a ray and every argument");
        var h = x.Part.World.Props.All[1];
        c.Check((int)r["props"] == 2 && h.Kind == "heavy" && h.Mass == 30f && Math.Abs(h.Rb.linearVelocity.z + 40f) < 1e-3 && h.Diameter == 2.5f, "RC4: kind, mass, speed and size", h.Kind + " " + h.Mass + " " + h.Rb.linearVelocity + " " + h.Diameter);
        c.Ok(new { cmd = "ragdoll.throw", from = x.Item, target = new { key = x.Other, bone = "cf_J_Spine02" }, kind = "box" }, "RC4: from an item, at a bone");
        c.Check(x.Part.World.Props.All[2].Kind == "box", "RC4: a box");
        C45Refused(c, new { cmd = "ragdoll.throw" }, "bad_args", "from", "RC4: nothing to throw along");
        C45Refused(c, new { cmd = "ragdoll.throw", from = new[] { 0, 0, 0 } }, "bad_args", "target", "RC4: no target");
        C45Refused(c, new { cmd = "ragdoll.throw", ray = new { origin = new[] { 0, 0, 0 } } }, "bad_args", "ray.dir", "RC4: a ray with no dir");
        C45Refused(c, new { cmd = "ragdoll.throw", ray = new { origin = new[] { 0, 0, 0 }, dir = new[] { 0, 0, 0 } } }, "bad_args", "ray.dir", "RC4: a ray with no length");
        C45Refused(c, new { cmd = "ragdoll.throw", ray = new { origin = new[] { 0, 0, 0 }, dir = new[] { 0, 0, 1 } }, from = new[] { 0, 0, 0 } }, "bad_args", "ray", "RC4: a ray and a from");
        C45Refused(c, new { cmd = "ragdoll.throw", from = new[] { 0, 0, 0 }, target = new[] { 0, 0, 1 }, kind = "nope" }, "bad_args", "kind", "RC4: a kind that is none");
        c.Ok(new { cmd = "ragdoll.release" }, "RC4: a release with no key");
        c.Check(x.Part.World == null, "RC4: the props go with the world");

        // an obstacle needs no world, and takes part when one comes
        var o = c.Ok(new { cmd = "ragdoll.obstacle", key = x.Item }, "RC4: ragdoll.obstacle");
        c.Check(C47Members(o).SequenceEqual(new[] { "obstacles" }) && (int)o["obstacles"] == 1 && x.App.Session.Model.Ragdoll?.Obstacles.Contains(x.Item) == true && x.Part.World == null, "RC4: in the scene's block, no world made", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "ragdoll.build", key = x.Chara }, "RC4: a body");
        c.Frames(2);
        c.Check(x.Part.World.Obstacles.Of(x.Item) != null, "RC4: the first world takes it up");
        c.Check((int)c.Ok(new { cmd = "ragdoll.obstacle", key = x.Item, on = false }, "RC4: on false")["obstacles"] == 0, "RC4: off again");
        c.Frames(2);
        c.Check(x.Part.World.Obstacles.Of(x.Item) == null, "RC4: it left the world");
        C47Reset(c, x);
    }

    // ---------------------------------------------------------------- RB13

    /// <summary>What a bake is judged on: every bone the FK groups Body and Neck write, in world space.</summary>
    static Dictionary<Transform, (Vector3 p, Quaternion q)> C47Pose(CharaRealization r)
    {
        var d = new Dictionary<Transform, (Vector3, Quaternion)>();
        var table = FkDriver.TableFor(r);
        foreach (var g in new[] { FkGroup.Body, FkGroup.Neck })
            foreach (var b in table.Of(g))
                if (b.Bone) d[b.Bone] = (b.Bone.position, b.Bone.rotation);
        foreach (var row in RagdollParts.All)
        {
            var t = r.Context.BodyBones[row.Bone];
            d[t] = (t.position, t.rotation);
        }
        return d;
    }

    // the worst angle and the worst move against a snapshot, and where
    static (double angle, double move, string where) C47Against(Dictionary<Transform, (Vector3 p, Quaternion q)> before)
    {
        double angle = 0, move = 0;
        string where = "";
        foreach (var kv in before)
        {
            double a = RagdollTestDeg(kv.Key.rotation, kv.Value.q), m = (kv.Key.position - kv.Value.p).magnitude;
            if (a > angle) { angle = a; where = kv.Key.name; }
            if (m > move) move = m;
        }
        return (angle, move, where);
    }

    static void C47BakeOne(Ctx c, C47Fix x, string key, string who, string variant, Action before, Action after)
    {
        const double Bar = 1e-4;
        C47Reset(c, x);
        string what = "RB13 (" + who + ", " + variant + ")";
        before?.Invoke();
        c.Ok(new { cmd = "ragdoll.build", key, mapFloor = false }, what + ": a body");
        c.Ok(new { cmd = "ragdoll.set", key, preset = "limp" }, what + ": limp");
        var body = C47Body(x, key);
        var r = x.App.Runtime.FindChara(key);
        c.Frames(2);
        var start = C47Pose(r);
        c.Ok(new { cmd = "ragdoll.push", key, part = "chest", local = new[] { 40f, 30f, -300f } }, what + ": a push");
        c.Ok(new { cmd = "ragdoll.push", key, part = "forearm_L", local = new[] { 0f, 60f, 60f } }, what + ": another");
        c.Frames(40);
        C47Pause(c, true);
        c.Frames(2);
        var pose = C47Pose(r);
        var moved = C47Against(start);
        var fkBefore = ((CharaNodeState)x.App.Session.Model.Find(key).State).Fk.Bones.ToDictionary(kv => kv.Key, kv => kv.Value);
        int n = W1UndoCount(c);
        var reply = c.Ok(new { cmd = "ragdoll.bake", key }, what + ": ragdoll.bake");
        c.Check(C47Members(reply).SequenceEqual(new[] { "bones" }), what + ": the reply", Ctx.Short(c.LastText));
        c.Check(W1UndoCount(c) == n + 1 && W1TopLabel(c) == "Bake pose to FK" && C47Body(x, key) == null && !C47Block(x, key).On, what + ": one entry, the body is gone", W1TopLabel(c));
        var st = (CharaNodeState)x.App.Session.Model.Find(key).State;
        int changed = st.Fk.Bones.Count(kv => !fkBefore.TryGetValue(kv.Key, out var v) || !v.Equals(kv.Value));
        c.Check(st.Fk.Enabled && st.Fk.Groups.Body && st.Fk.Groups.Neck && !st.Ik.Enabled && st.Anime.Speed == 0f && st.Fk.HipsPosition != null && st.Fk.HipsOffset == 0f, "RB13: " + what + ": FK and the two groups on, IK off, speed 0, the hips' place kept", st.Fk.Enabled + " " + st.Fk.Groups.Body + " " + st.Fk.Groups.Neck);
        c.Check(changed >= 16, what + ": the bake changed FK values", changed + " values of " + reply["bones"] + " written");
        c.Frames(3);
        var (angle, move, where) = C47Against(pose);
        c.Check(angle <= Bar && move <= Bar, what + ": bones after equal bones before, in world space, by angle in double", "greatest angle " + angle.ToString("G4") + " degrees at " + where + ", greatest move " + move.ToString("G4") + "; " + pose.Count + " bones; the push moved them up to " + moved.angle.ToString("F2") + " degrees, " + moved.move.ToString("F3") + " units");
        c.Check(moved.angle > 10.0 && moved.move >= 1.0, what + ": the compared pose is not the start", moved.angle.ToString("F2") + " degrees, " + moved.move.ToString("F3") + " units");
        C47Pause(c, false);
        after?.Invoke();
        c.Ok(new { cmd = "undo" }, what + ": undo");
        var fk = ((CharaNodeState)x.App.Session.Model.Find(key).State).Fk;
        c.Check(!fk.Enabled && C47Block(x, key).On && C47Body(x, key) != null, what + ": one undo gives FK off and the body back", fk.Enabled + "");
        c.Ok(new { cmd = "ragdoll.release", undo = false }, "(a release)");
        c.Ok(new { cmd = "fk.enable", key, on = false, undo = false }, "(FK off)");
        c.Frames(1);
    }

    static void C47Bake(Ctx c, C47Fix x)
    {
        C47Reset(c, x);
        string k = x.Chara;
        C47BakeOne(c, x, k, "BR-Chan", "plain", null, null);
        C47BakeOne(c, x, x.Other, "HERO107", "plain", null, null);

        // a correction stays on top: the view before equals the view after
        const string bone = "cf_J_ArmLow01_L";
        C47BakeOne(c, x, k, "BR-Chan", "a correction on top",
            () => c.Ok(new { cmd = "pose.bones.set", key = k, bone, rot = new[] { 0f, 25f, 0f }, undo = false }, "RB13: a correction"),
            () =>
            {
                var cb = ((CharaNodeState)x.App.Session.Model.Find(k).State).AdvancedPose;
                c.Check(cb.Bones.Count == 1, "RB13: the correction stays", cb.Bones.Count + "");
            });
        c.Ok(new { cmd = "pose.bones.clear", key = k, all = true, undo = false }, "(the correction goes)");

        // IK on: a bake switches it off, so FK shows what the body wrote
        C47BakeOne(c, x, k, "BR-Chan", "IK on",
            () => c.Ok(new { cmd = "ik.enable", key = k, on = true, undo = false }, "RB13: IK on"), null);
        c.Ok(new { cmd = "ik.enable", key = k, on = false, undo = false }, "(IK off)");
        C47Reset(c, x);
    }
}
