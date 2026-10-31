// C25 of BridgeCoreTest: the Phase 3.4 FK / IK commands of package C, further
// methods of the partial BridgeCoreTest (-only C25), on BR-Chan in edit mode (frames pumped by hand, the character paused):
//   shapes   ik.list (HS2's members first, 13 targets in IkTargets order with key / group / kind / usesRot, exact vs
//            rounded, world), fk.list hipsPosition, the solve info once IK runs, `effective` for a raw-cleared target
//   errors   unknown target / group / from / space / part, fk.copy from anime with Hair, fk.copy on an item, a character
//            that is not built (copies and bake unavailable, ik.enable flag only), app.options kinematics members
//   history  one entry per mutating call with its label; undo / redo restore scene.doc (W1History1)
//   values   ik.set world round trip (1e-5 on a moved and turned character), ik.copy anime / current = the source bones
//            (1e-5; IK off), the exclusive preset through app.options, ik.bake → fk.hipsPosition, the HS2 IK import and
//            ik.copy from:"import" of a 2.3-style raw block, pose.save → pose.load → scene.doc part equality, pose.list source
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class BridgeCoreTest
{
    /// <summary>P2 (both modes, one scale, all IK groups), relative to the HS2 pose folder.</summary>
    public const string KinPoseP2 = "STANDING/Floor/Peace.dat";

    static readonly string[] KinParts = { "anime", "hands", "fk", "ik", "correction", "face", "look" };

    static void C25(Ctx c)
    {
        var app = c.Host();
        foreach (var f in Directory.Exists(app.Paths.PoseDir) ? Directory.GetFiles(app.Paths.PoseDir, "*", SearchOption.AllDirectories) : new string[0])
            if (f.Replace('\\', '/').Contains("/c25")) File.Delete(f);
        c.Ok(new { cmd = "scene.new" }, "scene.new");
        string id = W1LoadFemale(c);
        if (id == null) return;
        c.Frames(2);
        var r = app.Runtime.FindChara(id);
        if (!c.Check(r != null && r.State == RealizationState.Built && IkDriver.Of(r) != null, "BR-Chan built with an IK character (IkDriver)")) return;

        // ---------------- ik.list shape on a fresh character (IK off, no stored target)
        var l0 = c.Ok(new { cmd = "ik.list", key = id }, "ik.list (fresh)");
        var order = l0.Properties().Select(p => p.Name).Where(n => !W2Shape.Envelope.Contains(n)).ToList();
        var hs2Order = new[] { "ikEnabled", "enableIK", "enableFK", "activeIK", "activeFK", "targets" };
        c.Check(order.Take(6).SequenceEqual(hs2Order) && order.Skip(6).Take(3).SequenceEqual(new[] { "bodySemantics", "effective", "built" }),
            "ik.list: HS2's members first (ikEnabled, enableIK, enableFK, activeIK, activeFK, targets), then bodySemantics, effective, built", string.Join(", ", order));
        var t0 = l0["targets"] as JArray;
        bool names = t0 != null && t0.Count == 13 && t0.Select(t => (string)t["name"]).SequenceEqual(IkTargets.Names) && t0.Select(t => (int)t["key"]).SequenceEqual(Enumerable.Range(0, 13));
        c.Check(names, "13 targets in IkTargets.Names order, key 0–12", Ctx.Short(t0?.ToString(Formatting.None)));
        if (names)
        {
            var kinds = t0.Select(t => (string)t["kind"]).ToList();
            var rots = t0.Select(t => (bool)t["usesRot"]).ToList();
            var groups = t0.Select(t => (string)t["group"]).ToList();
            c.Check(Enumerable.Range(0, 13).All(i => (kinds[i] == "bendGoal") == (i == 2 || i == 5 || i == 8 || i == 11)) && Enumerable.Range(0, 13).All(i => rots[i] == (i == 3 || i == 6 || i == 9 || i == 12)),
                "kind bendGoal for the four chains, usesRot for the hands and feet");
            c.Check(groups[0] == "Body" && groups[1] == "LeftArm" && groups[4] == "RightArm" && groups[7] == "LeftLeg" && groups[12] == "RightLeg",
                "group = HS2's group names (Body, LeftArm, RightArm, LeftLeg, RightLeg)", string.Join(",", groups));
            var tk = (t0[0] as JObject)?.Properties().Select(p => p.Name).ToList();
            c.Check(tk != null && tk.Take(7).SequenceEqual(new[] { "name", "key", "group", "kind", "usesRot", "pos", "rot" }), "target members in HS2's order (name, key, group, kind, usesRot, pos, rot)", tk == null ? "" : string.Join(",", tk));
        }
        c.Check(t0 != null && t0.All(t => t["pos"].Type == JTokenType.Null && !(bool)t["stored"]) && (l0["effective"] as JArray)?.Count == 0 && (string)l0["bodySemantics"] == "hips"
            && (bool)l0["built"] && l0["solve"]?["ran"]?.Value<bool>() == false,
            "fresh: no value (pos null, stored false), effective [], bodySemantics hips, solve.ran false", Ctx.Short(c.LastText));

        // ---------------- argument errors
        var eg = c.Fails(new { cmd = "ik.enable", key = id, groups = new[] { "Tail" } }, "bad_args", "ik.enable {groups: [Tail]} → bad_args");
        c.Check((eg["detail"]?["candidates"] as JArray)?.Count == 5, "… with the 5 IK group names as candidates", Ctx.Short(c.LastText));
        c.Fails(new { cmd = "ik.set", key = id, targets = new JObject { ["Nope"] = new JObject { ["pos"] = new JArray(0, 1, 0) } } }, "bad_args", "ik.set with only an unknown target → bad_args");
        c.Fails(new { cmd = "ik.set", key = id, targets = new JObject() }, "bad_args", "ik.set {targets: {}} → bad_args");
        c.Fails(new { cmd = "ik.set", key = id, targets = new JObject { ["RightHand"] = new JObject { ["pos"] = new JArray(1, 2) } } }, "bad_args", "ik.set pos with 2 numbers → bad_args");
        c.Fails(new { cmd = "ik.set", key = id, space = "up", targets = new JObject { ["RightHand"] = new JObject { ["pos"] = new JArray(0, 1, 0) } } }, "bad_args", "ik.set space up → bad_args");
        c.Fails(new { cmd = "ik.copy", key = id, from = "air" }, "bad_args", "ik.copy {from: air} → bad_args");
        c.Fails(new { cmd = "ik.copy", key = id, groups = new[] { "Tail" } }, "bad_args", "ik.copy {groups: [Tail]} → bad_args");
        c.Fails(new { cmd = "ik.copy", key = id, from = "import", groups = new[] { "Body" } }, "bad_args", "ik.copy {from: import, groups} → bad_args");
        c.Fails(new { cmd = "ik.copy", key = id, from = "import" }, "bad_args", "ik.copy {from: import} without an imported IK block → bad_args");
        c.Fails(new { cmd = "fk.copy", key = id, from = "anime", groups = new[] { "Hair" } }, "bad_args", "fk.copy {from: anime, groups: [Hair]} → bad_args");
        c.Fails(new { cmd = "fk.copy", key = id, groups = new[] { "Tail" } }, "bad_args", "fk.copy {groups: [Tail]} → bad_args");
        c.Fails(new { cmd = "fk.copy", key = id, from = "import" }, "bad_args", "fk.copy {from: import} → bad_args");
        c.Fails(new { cmd = "pose.save", key = id, path = "c25/bad", parts = new[] { "nope" } }, "bad_args", "pose.save {parts: [nope]} → bad_args");
        c.Fails(new { cmd = "pose.save", key = id, path = "../c25_out" }, "io_denied", "pose.save {path: ../x} → io_denied");
        c.Fails(new { cmd = "pose.list", source = "both" }, "bad_args", "pose.list {source: both} → bad_args");
        c.Fails(new { cmd = "fk.set", key = id, hipsPosition = new[] { 1, 2 } }, "bad_args", "fk.set {hipsPosition: [1, 2]} → bad_args");
        string item = W1AddItem(c, "item.add 399 (fk.copy target)");
        if (item != null) c.Fails(new { cmd = "fk.copy", key = item }, "wrong_kind", "fk.copy on an item → wrong_kind");
        bool exclusive0 = IkOptions.Exclusive;
        c.Fails(new { cmd = "app.options", kinematics = new { nope = true } }, "bad_args", "app.options {kinematics: {nope}} → bad_args");
        c.Fails(new { cmd = "app.options", kinematics = new { exclusive = true, limitMinInterior = 500 } }, "bad_args", "app.options {kinematics: {limitMinInterior: 500}} → bad_args …");
        c.Check(IkOptions.Exclusive == exclusive0, "… and nothing applied (exclusive unchanged)");
        c.Fails(new { cmd = "app.options", kinematics = new { exclusive = "yes" } }, "bad_args", "app.options {kinematics: {exclusive: \"yes\"}} → bad_args");

        // a character that is not built
        var nb = app.Session.AddChara(null);
        if (c.Check(nb != null && nb.Ok, "a character without a card (not built)", nb?.ToString()))
        {
            app.Runtime.ApplyNow();
            string nid = nb.NewId;
            c.Fails(new { cmd = "ik.copy", key = nid }, "unavailable", "ik.copy on a character that is not built → unavailable");
            c.Fails(new { cmd = "fk.copy", key = nid }, "unavailable", "fk.copy on a character that is not built → unavailable");
            c.Fails(new { cmd = "ik.bake", key = nid }, "unavailable", "ik.bake on a character that is not built → unavailable");
            c.Fails(new { cmd = "ik.set", key = nid, space = "world", targets = new JObject { ["Body"] = new JObject { ["pos"] = new JArray(0, 1, 0) } } }, "unavailable", "ik.set space world on it → unavailable");
            var en = c.Ok(new { cmd = "ik.enable", key = nid }, "ik.enable on it: the flag only");
            c.Check((bool?)en["ikEnabled"] == true && (en["captured"] as JArray)?.Count == 0, "… ikEnabled, nothing captured", Ctx.Short(c.LastText));
            var ln = c.Ok(new { cmd = "ik.list", key = nid, world = true }, "ik.list on it");
            c.Check(!(bool)ln["built"] && ln["solve"]?.Type == JTokenType.Null && (ln["targets"] as JArray)?.All(t => t["world"] == null) == true, "… built false, solve null, no world poses", Ctx.Short(c.LastText));
            c.Ok(new { cmd = "delete", key = nid }, "delete it");
        }
        if (item != null) c.Ok(new { cmd = "delete", key = item }, "delete the item");

        // ---------------- history: one entry each, undo / redo restore scene.doc
        var en1 = W1History1(c, new { cmd = "ik.enable", key = id, on = true }, "IK", "ik.enable {on}");
        c.Check((bool?)en1["ikEnabled"] == true && (en1["captured"] as JArray)?.Count == 13 && (en1["activeIK"] as JArray)?.All(x => (bool)x) == true,
            "ik.enable: IK on, all five groups, the 13 missing targets captured", Ctx.Short(en1.ToString(Formatting.None)));
        var stAfter = (CharaNodeState)app.Session.Model.Find(id).State;
        c.Check(stAfter.Ik.Targets.Targets.All(x => x != null), "… all 13 targets stored");
        var eg2 = W1History1(c, new { cmd = "ik.enable", key = id, on = false, groups = new[] { "rightArm" } }, "IK", "ik.enable {groups: [rightArm], on: false}");
        c.Check((eg2["activeIK"] as JArray)?.Select(x => (bool)x).SequenceEqual(new[] { true, true, true, false, true }) == true && (bool?)eg2["ikEnabled"] == true,
            "… rightArm off, IK still on", Ctx.Short(eg2.ToString(Formatting.None)));
        c.Ok(new { cmd = "ik.enable", key = id, groups = new[] { "RightArm" } }, "ik.enable {groups: [RightArm]} (HS2's name) back on");
        var rh = stAfter.Ik.Targets[6].Pos;
        var setPos = new[] { rh.X + 0.05f, rh.Y + 0.02f, rh.Z + 0.03f };
        var s1 = W1History1(c, new { cmd = "ik.set", key = id, targets = new JObject { ["righthand"] = new JObject { ["pos"] = new JArray(setPos), ["rot"] = new JArray(10, 20, 30) }, ["Body"] = new JObject { ["rot"] = new JArray(5, 0, 0) }, ["Tail"] = new JObject() } },
            "IK targets", "ik.set {righthand (any case), Body rot, Tail}");
        c.Check((int?)s1["set"] == 2 && (s1["missing"] as JArray)?.Select(x => (string)x).SequenceEqual(new[] { "Tail" }) == true && (s1["rotIgnored"] as JArray)?.Select(x => (string)x).SequenceEqual(new[] { "Body" }) == true,
            "ik.set: set 2, missing [Tail], rotIgnored [Body] (HS2's shape)", Ctx.Short(s1.ToString(Formatting.None)));
        c.Frames(3);
        var le = c.Ok(new { cmd = "ik.list", key = id, exact = true }, "ik.list exact (IK on, after 3 frames)");
        var leR = (le["targets"] as JArray)?[6];
        c.Check(leR != null && (double)leR["pos"][0] == (double)setPos[0] && (double)leR["pos"][1] == (double)setPos[1] && (double)leR["pos"][2] == (double)setPos[2]
            && (double)leR["rot"][1] == 20.0, "exact: RightHand pos = the float values set (bit for bit), rot (10, 20, 30)", Ctx.Short(leR?.ToString(Formatting.None)));
        var lr4 = c.Ok(new { cmd = "ik.list", key = id }, "ik.list rounded");
        var r4 = (lr4["targets"] as JArray)?[6]?["pos"];
        c.Check(r4 != null && Math.Abs((double)r4[0] - Math.Round((double)setPos[0], 4)) < 1e-12, "rounded: 4 decimals", Ctx.Short(r4?.ToString(Formatting.None)));
        var solve = le["solve"];
        bool ran = solve?["ran"]?.Value<bool>() == true;
        c.Check(ran && solve["residual"] is JObject resObj && resObj.Count > 0 && solve["clamped"] is JArray && solve["poleFallback"] is JObject && solve["ms"] != null
            && (le["owned"] as JArray)?.Count > 0 && (le["applied"] as JArray)?.All(x => (bool)x) == true,
            "solve: ran, residual per target, clamped, poleFallback, ms; owned bones listed; all groups applied", Ctx.Short(solve?.ToString(Formatting.None)));
        if (ran)
        {
            var res = (JObject)solve["residual"];
            var clamped = new HashSet<string>((solve["clamped"] as JArray).Select(x => (string)x));
            var ends = new[] { "LeftHand", "RightHand", "LeftFoot", "RightFoot" }.Where(n => res[n] != null && !clamped.Contains(n)).ToList();
            double maxRes = ends.Count == 0 ? double.NaN : ends.Max(n => (double)res[n]);
            c.Check(ends.Count > 0 && maxRes <= 1e-4, "reachable hand / foot residuals ≤ 1e-4", $"max {maxRes:G3} over {string.Join(",", ends)}");
            c.Check((le["owned"] as JArray).Any(x => (string)x == "cf_J_Hand_R") && (le["owned"] as JArray).Any(x => (string)x == "cf_J_Hips"), "owned includes cf_J_Hand_R and cf_J_Hips");
        }
        // a target cleared by a raw set (as a loaded file can leave it) uses the runtime snapshot while IK runs
        app.Session.Set(id, "chara.ik.targets.LeftFoot", null);
        app.Runtime.ApplyNow();
        c.Frames(2);
        var lsn = c.Ok(new { cmd = "ik.list", key = id }, "ik.list after a raw clear of LeftFoot");
        var lf = (lsn["targets"] as JArray)?[9];
        c.Check((lsn["effective"] as JArray)?.Select(x => (string)x).SequenceEqual(new[] { "LeftFoot" }) == true && lf?["pos"]?.Type == JTokenType.Array && (bool?)lf?["stored"] == false,
            "effective [LeftFoot]: the snapshot is listed as its value (stored false)", Ctx.Short(lsn["effective"]?.ToString(Formatting.None)));
        var s2 = c.Ok(new { cmd = "ik.set", key = id, targets = new JObject { ["RightFoot"] = new JObject { ["pos"] = new JArray(0.2, 0.1, 0.05) } } }, "ik.set RightFoot → captures LeftFoot");
        c.Check((s2["captured"] as JArray)?.Select(x => (string)x).SequenceEqual(new[] { "LeftFoot" }) == true, "ik.set: the other missing target captured (LeftFoot)", Ctx.Short(s2.ToString(Formatting.None)));

        // ---------------- world space round trip (moved and turned character)
        c.Ok(new { cmd = "obj.transform", key = id, pos = new[] { 1.5, 0.25, -2.0 }, rot = new[] { 0, 70, 0 } }, "obj.transform (move and turn)");
        var root = r.Root.transform;
        var wpos = new Vector3(1.3f, 1.1f, -1.6f);
        W1History1(c, new { cmd = "ik.set", key = id, space = "world", targets = new JObject { ["LeftHand"] = new JObject { ["pos"] = new JArray(wpos.x, wpos.y, wpos.z), ["rot"] = new JArray(0, 90, 0) } } },
            "IK targets", "ik.set {space: world}");
        var lw = c.Ok(new { cmd = "ik.list", key = id, exact = true, world = true }, "ik.list {exact, world}");
        var lh = (lw["targets"] as JArray)?[3];
        var wp = W1V3(lh?["world"]?["pos"]);
        var lp = W1V3(lh?["pos"]);
        var wr = W1V3(lh?["world"]?["rot"]);
        c.Check(W1Dist(wp, wpos) <= 1e-5 && W1Dist(lp, root.InverseTransformPoint(wpos)) <= 1e-5, "world round trip: world pos = the value set, pos = root space (1e-5)", $"world {wp:F6}, local {lp:F6}");
        c.Check(Quaternion.Angle(Quaternion.Euler(wr), Quaternion.Euler(0, 90, 0)) <= 1e-3f, "world rot = the value set (1e-3°)", wr.ToString("F4"));
        c.Ok(new { cmd = "obj.transform", key = id, pos = new[] { 0, 0, 0 }, rot = new[] { 0, 0, 0 } }, "obj.transform back");

        // ---------------- copies (IK off: the final pose is the paused clip pose = the probe's)
        c.Ok(new { cmd = "ik.enable", key = id, on = false }, "ik.enable {on: false}");
        c.Frames(2);
        var ca = W1History1(c, new { cmd = "ik.copy", key = id }, "IK from animation", "ik.copy (anime, all groups)");
        c.Check((ca["copied"] as JArray)?.Count == 13 && (string)ca["from"] == "anime" && (bool?)ca["ikEnabled"] == false, "ik.copy anime: 13 copied, IK stays off", Ctx.Short(ca.ToString(Formatting.None)));
        KinSourceCheck(c, id, r, "ik.copy anime");
        c.Ok(new { cmd = "ik.set", key = id, targets = new JObject { ["LeftHand"] = new JObject { ["pos"] = new JArray(0.4, 1.0, 0.2) }, ["LeftArmChain"] = new JObject { ["pos"] = new JArray(0.3, 1.2, -0.3) } } }, "ik.set (moves two targets away)");
        var cc = W1History1(c, new { cmd = "ik.copy", key = id, from = "current", groups = new[] { "leftArm" } }, "IK from pose", "ik.copy {from: current, groups: [leftArm]}");
        c.Check((cc["copied"] as JArray)?.Select(x => (string)x).SequenceEqual(new[] { "LeftShoulder", "LeftArmChain", "LeftHand" }) == true, "ik.copy current leftArm: the arm's 3 targets", Ctx.Short(cc.ToString(Formatting.None)));
        KinSourceCheck(c, id, r, "ik.copy current");
        var fa = W1History1(c, new { cmd = "fk.copy", key = id }, "FK from animation", "fk.copy (anime)");
        c.Check((int?)fa["copied"] > 0 && (string)fa["from"] == "anime" && (bool?)fa["fkEnabled"] == false, "fk.copy anime: values written, FK stays off", Ctx.Short(fa.ToString(Formatting.None)));
        var spine = (CharaNodeState)app.Session.Model.Find(id).State;
        c.Check(spine.Fk.Bones.ContainsKey("cf_J_Spine01") && spine.Fk.Bones.ContainsKey("cf_J_Neck"), "… Body and Neck bones in fk.bones");
        c.Ok(new { cmd = "ik.enable", key = id, on = true }, "ik.enable on (the targets moved by the copies make the arm move)");
        c.Ok(new { cmd = "ik.set", key = id, targets = new JObject { ["LeftHand"] = new JObject { ["pos"] = new JArray(0.45, 0.95, 0.25) } } }, "ik.set LeftHand");
        c.Frames(2);
        var fc = W1History1(c, new { cmd = "fk.copy", key = id, from = "current", groups = new[] { "body" } }, "FK from pose", "fk.copy {from: current, groups: [body]}");
        c.Check((int?)fc["copied"] > 0 && (string)fc["from"] == "current", "fk.copy current: Body values written", Ctx.Short(fc.ToString(Formatting.None)));

        // ---------------- exclusive preset through app.options
        var ko = c.Ok(new { cmd = "app.options", kinematics = new { exclusive = true } }, "app.options {kinematics: {exclusive: true}}");
        c.Check(ko["kinematics"]?["exclusive"]?.Value<bool>() == true && IkOptions.Exclusive && ko["kinematics"]?["limitMinInterior"] != null, "reply kinematics.exclusive true, IkOptions.Exclusive", Ctx.Short(ko["kinematics"]?.ToString(Formatting.None)));
        var fe = W1History1(c, new { cmd = "fk.enable", key = id, on = true }, "FK", "fk.enable {on} (exclusive)");
        c.Check((bool?)fe["fkEnabled"] == true && (bool?)fe["ikEnabled"] == false, "exclusive: FK on switches IK off in the same entry", Ctx.Short(fe.ToString(Formatting.None)));
        var ie = W1History1(c, new { cmd = "ik.enable", key = id, on = true }, "IK", "ik.enable {on} (exclusive)");
        c.Check((bool?)ie["ikEnabled"] == true && (bool?)ie["fkEnabled"] == false, "exclusive: IK on switches FK off in the same entry", Ctx.Short(ie.ToString(Formatting.None)));
        var fce = c.Ok(new { cmd = "fk.copy", key = id, from = "current", enable = true }, "fk.copy {enable} (exclusive)");
        c.Check((bool?)fce["fkEnabled"] == true && (bool?)fce["ikEnabled"] == false, "exclusive: fk.copy enable switches FK on and IK off", Ctx.Short(fce.ToString(Formatting.None)));
        c.Ok(new { cmd = "app.options", kinematics = new { exclusive = false } }, "app.options {kinematics: {exclusive: false}}");
        var ib = c.Ok(new { cmd = "ik.enable", key = id, on = true }, "ik.enable {on} (not exclusive)");
        c.Check((bool?)ib["ikEnabled"] == true && (bool?)ib["fkEnabled"] == true && !IkOptions.Exclusive, "not exclusive: both modes on", Ctx.Short(ib.ToString(Formatting.None)));
        c.Frames(3);

        // ---------------- bake and the hips position
        var bk = W1History1(c, new { cmd = "ik.bake", key = id }, "Bake IK", "ik.bake");
        var hp = W1V3(bk["hipsPosition"]);
        c.Check((int?)bk["bones"] > 0 && !float.IsNaN(hp.x) && (bool?)bk["fkEnabled"] == true && (bool?)bk["ikEnabled"] == false, "ik.bake: Body bones, hipsPosition, FK on, IK off", Ctx.Short(bk.ToString(Formatting.None)));
        var fl = c.Ok(new { cmd = "fk.list", key = id, exact = true }, "fk.list exact after the bake");
        c.Check(W1Dist(W1V3(fl["hipsPosition"]), hp) == 0, "fk.list hipsPosition = the baked one", Ctx.Short(fl["hipsPosition"]?.ToString(Formatting.None)));
        var fs = W1History1(c, new { cmd = "fk.set", key = id, hipsPosition = (object)null, hipsOffset = 0.1 }, "FK bones", "fk.set {hipsPosition: null, hipsOffset: 0.1}");
        c.Check(fs["hipsPosition"]?.Type == JTokenType.Null && (int?)fs["set"] == 0, "fk.set: hipsPosition null in the reply", Ctx.Short(fs.ToString(Formatting.None)));
        var fl2 = c.Ok(new { cmd = "fk.list", key = id }, "fk.list");
        var hipsEntry = (fl2["bones"] as JArray)?.FirstOrDefault(b => (int)b["key"] == 0);
        c.Check(fl2["hipsPosition"]?.Type == JTokenType.Null && Math.Abs((double)fl2["hipsOffset"] - 0.1) < 1e-6 && hipsEntry != null && Math.Abs((double)hipsEntry["pos"][1] - 0.1) < 1e-6,
            "fk.list: hipsPosition null, hipsOffset 0.1, cf_J_Hips pos (0, 0.1, 0) (HS2 shape)", Ctx.Short(fl2["hipsPosition"]?.ToString(Formatting.None)));
        c.Ok(new { cmd = "fk.set", key = id, hipsPosition = new[] { 0f, 1.1f, 0.05f } }, "fk.set {hipsPosition: [0, 1.1, 0.05]}");
        c.Check(W1Dist(W1V3(c.Call(new { cmd = "fk.list", key = id, exact = true })["hipsPosition"]), new Vector3(0f, 1.1f, 0.05f)) == 0, "fk.list hipsPosition = the value set");

        // ---------------- pose.save → pose.load → scene.doc part equality; pose.list source
        c.Ok(new { cmd = "fk.enable", key = id, on = false }, "fk.enable off (no FK fill at save)");
        c.Ok(new { cmd = "ik.enable", key = id, on = true }, "ik.enable on (all 13 targets stored: no IK fill)");
        var partsBefore = KinParts0(c, id);
        var ps = c.Ok(new { cmd = "pose.save", key = id, path = "c25/p1", name = "C25 pose" }, "pose.save {path: c25/p1, name}");
        string pfile = (string)ps["path"];
        c.Check(pfile != null && File.Exists(pfile) && pfile.Replace('\\', '/').EndsWith("/pose/c25/p1.hs2npose") && (long?)ps["bytes"] > 0
            && (ps["parts"] as JArray)?.Select(x => (string)x).SequenceEqual(KinParts) == true && (string)ps["relative"] == "c25/p1.hs2npose"
            && (ps["filledFk"] as JArray)?.Count == 0 && (ps["filledIk"] as JArray)?.Count == 0,
            "pose.save: <UserData>/pose/c25/p1.hs2npose, all 7 parts, nothing filled", Ctx.Short(c.LastText));
        c.Check(W1UndoCount(c) >= 0 && W1TopLabel(c) == "IK" , "pose.save records nothing (top entry still \"IK\")", W1TopLabel(c));
        c.Fails(new { cmd = "pose.save", key = id, path = "c25/p1" }, "io_denied", "pose.save again without overwrite → io_denied");
        c.Ok(new { cmd = "pose.save", key = id, path = "c25/p1.hs2npose", name = "C25 pose", overwrite = true }, "pose.save again with overwrite");
        c.Ok(new { cmd = "pose.save", key = id, path = "c25/p2_hands", parts = new[] { "HANDS", "fk" } }, "pose.save {parts: [HANDS, fk]}");
        // change every part, then load the file back
        c.Ok(new { cmd = "ik.set", key = id, targets = new JObject { ["Body"] = new JObject { ["pos"] = new JArray(0.1, 1.0, 0.1) } } }, "ik.set Body (changes ik)");
        c.Ok(new { cmd = "ik.enable", key = id, on = false }, "ik.enable off");
        c.Ok(new { cmd = "fk.set", key = id, bones = new Dictionary<string, object> { ["cf_J_Spine02"] = new[] { 3f, 4f, 5f } } }, "fk.set Spine02 (changes fk)");
        c.Ok(new { cmd = "chara.hands", key = id, left = 3, right = 5 }, "chara.hands (changes hands)");
        int u0 = W1UndoCount(c);
        var pl = c.Ok(new { cmd = "pose.load", key = id, path = "c25/p1.hs2npose" }, "pose.load {path: c25/p1.hs2npose}");
        c.Check((string)pl["source"] == "hs2next" && (bool?)pl["loaded"] == true && (string)pl["name"] == "C25 pose" && (bool?)pl["sexMatches"] == true
            && (pl["parts"] as JArray)?.Count == 7 && W1UndoCount(c) == u0 + 1 && W1TopLabel(c) == "Load pose",
            "pose.load: source hs2next, loaded, name, 7 parts, one entry \"Load pose\"", Ctx.Short(c.LastText));
        var partsAfter = KinParts0(c, id);
        var diff = KinParts.Where(p => !JToken.DeepEquals(partsBefore?[p], partsAfter?[p])).ToList();
        c.Check(partsBefore != null && diff.Count == 0, "scene.doc: every part equals the state saved", "differ: " + string.Join(", ", diff));
        c.Ok(new { cmd = "undo" }, "undo the load");
        c.Check(!JToken.DeepEquals(KinParts0(c, id)?["ik"], partsBefore?["ik"]), "undo restores the changed ik");
        c.Fails(new { cmd = "pose.load", key = id, path = "c25/p1.hs2npose", parts = new[] { "nope" } }, "bad_args", "pose.load {parts: [nope]} → bad_args");
        var plp = c.Ok(new { cmd = "pose.load", key = id, path = Path.GetFullPath(pfile), parts = new[] { "hands" } }, "pose.load {absolute path, parts: [hands]}");
        c.Check((plp["parts"] as JArray)?.Select(x => (string)x).SequenceEqual(new[] { "hands" }) == true && JToken.DeepEquals(KinParts0(c, id)?["hands"], partsBefore?["hands"]),
            "… only hands set (= the file's)", Ctx.Short(c.LastText));
        var pls = c.Ok(new { cmd = "pose.list", source = "hs2next" }, "pose.list {source: hs2next}");
        var own = (pls["poses"] as JArray)?.Where(p => ((string)p["path"]).StartsWith("c25/")).ToList();
        c.Check(own != null && own.Count == 2 && own.All(p => (string)p["source"] == "hs2next" && (string)p["kind"] == "hs2npose") && own.Any(p => (string)p["path"] == "c25/p1.hs2npose" && (string)p["name"] == "C25 pose" && (int)p["sex"] == 1)
            && pls["roots"]?["hs2"]?.Type == JTokenType.Null,
            "pose.list hs2next: the two files (path, name, sex 1, kind hs2npose, source)", Ctx.Short(c.LastText));
        if (app.Paths.Hs2PoseDir != null && Directory.Exists(Path.Combine(app.Paths.Hs2PoseDir, "STANDING", "Floor")))
        {
            var pla = c.Ok(new { cmd = "pose.list", source = "all", dir = "STANDING/Floor" }, "pose.list {source: all, dir: STANDING/Floor}");
            var src = (pla["poses"] as JArray)?.Select(p => (string)p["source"]).Distinct().ToList();
            c.Check(src != null && src.Contains("hs2") && (pla["poses"] as JArray).Any(p => (string)p["path"] == KinPoseP2), "pose.list all: HS2's entries with source hs2 (P2 among them)", Ctx.Short(c.LastText));
            var plh = c.Ok(new { cmd = "pose.list", dir = "STANDING/Floor", limit = 1 }, "pose.list (default source hs2)");
            c.Check((string)(plh["poses"] as JArray)?[0]?["source"] == "hs2", "default source hs2");
        }
        else c.Warn("HS2 pose folder STANDING/Floor not found: pose.list all and the import checks skipped");

        // ---------------- HS2 pose IK import and ik.copy from:"import"
        string p2 = app.Paths.Hs2PoseDir != null ? Path.Combine(app.Paths.Hs2PoseDir, KinPoseP2.Replace('/', Path.DirectorySeparatorChar)) : null;
        if (p2 != null && File.Exists(p2))
        {
            var pi = W1History1(c, new { cmd = "pose.load", key = id, path = KinPoseP2 }, "Load pose", "pose.load P2 (IK import)");
            c.Check((string)pi["source"] == "hs2" && (bool?)pi["ikApproximated"] == true && (int?)pi["ikScale"] == 1 && pi["ikBodyShift"] is JArray && (bool?)pi["ikPending"] == false
                && pi["ikUnreachable"] is JArray, "P2: ikApproximated, ikScale 1, ikBodyShift, ikPending false", Ctx.Short(c.LastText));
            var lp2 = c.Call(new { cmd = "ik.list", key = id });
            c.Check((bool?)lp2["ikEnabled"] == true && (lp2["targets"] as JArray)?.All(t => (bool)t["stored"]) == true, "P2: IK on, 13 targets stored");
            var praw = c.Ok(new { cmd = "pose.load", key = id, path = KinPoseP2, ik = false }, "pose.load P2 {ik: false} (2.3's raw import)");
            c.Check((bool?)praw["ikPending"] == true && (bool?)praw["ikApproximated"] == false, "ik false: ikPending true, ikApproximated false", Ctx.Short(c.LastText));
            c.Fails(new { cmd = "pose.load", key = id, path = KinPoseP2, parts = new[] { "fk" } }, "bad_args", "pose.load of an HS2 file with parts → bad_args");
            var ci = W1History1(c, new { cmd = "ik.copy", key = id, from = "import" }, "Convert IK", "ik.copy {from: import}");
            c.Check((ci["copied"] as JArray)?.Count == 13 && (int?)ci["ikScale"] == 1 && ci["ikBodyShift"] is JArray && (bool?)ci["ikEnabled"] == true,
                "ik.copy import: 13 targets converted, ikScale 1, ikBodyShift, IK on from the block", Ctx.Short(ci.ToString(Formatting.None)));
            var lci = c.Call(new { cmd = "ik.list", key = id, exact = true })["targets"] as JArray;
            var lpi = lp2["targets"] as JArray;
            bool same = lci != null && lpi != null && Enumerable.Range(0, 13).All(i => W1Dist(W1V3(lci[i]["pos"]), W1V3(lpi[i]["pos"])) <= 1e-4);
            c.Check(same, "ik.copy import = the targets of the converting load (1e-4; same pose, same body)");
        }
        else c.Warn("pose " + KinPoseP2 + " not found: the IK import checks skipped");
    }

    /// <summary>The 7 pose parts of a character node in scene.doc {key, capture:false, embed:true} (node.chara.*), or null.</summary>
    static JObject KinParts0(Ctx c, string id)
    {
        var d = c.Call(new { cmd = "scene.doc", key = id, capture = false, embed = true });
        var node = d["node"] as JObject ?? (d["node"]?.Type == JTokenType.String ? JObject.Parse((string)d["node"]) : null);
        var ch = node?["chara"] as JObject;
        if (ch == null) return null;
        var o = new JObject();
        foreach (var p in KinParts) o[p] = ch[p]?.DeepClone();
        return o;
    }

    /// <summary>ik.list {exact} = the source bones of the targets the copy wrote (root-space positions 1e-5; hand / foot rotations 1e-3°).</summary>
    static void KinSourceCheck(Ctx c, string id, CharaRealization r, string what)
    {
        var l = c.Call(new { cmd = "ik.list", key = id, exact = true })["targets"] as JArray;
        var root = r.Root.transform;
        double maxP = 0, maxR = 0;
        var bad = new List<string>();
        for (int i = 0; i < 13 && l != null; i++)
        {
            if (!(bool)l[i]["stored"]) continue;
            var bone = r.Context.BodyBones.TryGetValue(IkGroupInfo.SourceBones[i], out var t) ? t : null;
            if (!bone) { bad.Add(IkGroupInfo.SourceBones[i] + " missing"); continue; }
            double dp = W1Dist(W1V3(l[i]["pos"]), root.InverseTransformPoint(bone.position));
            maxP = Math.Max(maxP, dp);
            if (dp > 1e-5) bad.Add(IkTargets.Names[i] + " " + dp.ToString("G3"));
            if (IkGroupInfo.UsesRotation((IkTargetId)i))
            {
                double dr = Quaternion.Angle(Quaternion.Euler(W1V3(l[i]["rot"])), Quaternion.Inverse(root.rotation) * bone.rotation);
                maxR = Math.Max(maxR, dr);
                if (dr > 1e-3) bad.Add(IkTargets.Names[i] + " rot " + dr.ToString("G3"));
            }
        }
        c.Check(l != null && bad.Count == 0, what + ": ik.list = the source bones (root space 1e-5, hand / foot rotation 1e-3°)", $"max {maxP:G3} / {maxR:G3}°; " + string.Join(", ", bad));
    }
}
