// The Editor-only part of BridgeTest.play (package W2's, called by BridgeTest.PlayMain after the
// acceptance A1–A14 and the AutoStep checks S1–S6, which moved to the runtime in Phase 2.5 so that the player runs them too:
// Runtime/App/BridgeAcceptance_Parts.cs). Over real HTTP, on the map, with c.FemaleKey = the acceptance character:
//   Shape (H1–H5): the HTTP replies vs the HS2 recordings of Cache/compare (BridgeCoreTest's shape helpers):
//     H1 bones.dump all, H2 face.dump, H3 camera.state (also with set), H4 fk.list exact of a fresh BR-Chan after
//     compare case 1 (the entry key set = HS2's pose_1.json keys except the penis chain 11000–11003), H5 scene objects;
//     WARN when the recordings are absent.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next.App;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class BridgeTest
{
    static double W2Dbl(JToken t) => t == null || t.Type == JTokenType.Null ? double.NaN : t.Value<double>();

    /// <summary>H1–H5 (the HS2 shape of the dumps over HTTP).</summary>
    static IEnumerator Shape(AcceptanceCtx c)
    {
        c.Id = "H1";
        string rest = Path.Combine(BridgeCoreTest.W2Shape.FemaleRecordings, "rest.json"), pose1 = Path.Combine(BridgeCoreTest.W2Shape.FemaleRecordings, "pose_1.json");
        if (!File.Exists(rest) || !File.Exists(pose1)) { c.Warn("Cache/compare recordings of BR-Chan not found: H1–H5 skipped"); yield break; }
        JObject recRest, recPose;
        using (var r = new JsonTextReader(new StringReader(File.ReadAllText(rest))) { DateParseHandling = DateParseHandling.None }) recRest = JObject.Load(r);
        using (var r = new JsonTextReader(new StringReader(File.ReadAllText(pose1))) { DateParseHandling = DateParseHandling.None }) recPose = JObject.Load(r);
        string key = c.FemaleKey;
        if (key == null) { c.Warn("no acceptance character: H1–H5 skipped"); yield break; }

        // ---- H1 bones.dump all
        yield return c.Call(new { cmd = "bones.dump", key, root = "all" });
        if (c.CheckOk("bones.dump all"))
        {
            var diffs = new List<string>();
            var info = new List<string>();
            BridgeCoreTest.W2Shape.ShapeDump(diffs, info, recRest["dump"] as JObject, BridgeCoreTest.W2Shape.Result(c.J));
            foreach (var l in info) c.Info(l);
            W2Report(c, "bones.dump all: HS2's members, order, kinds, ≥ 99 % of the body and head paths", diffs);
        }

        // ---- H2 face.dump
        c.Id = "H2";
        yield return c.Call(new { cmd = "face.dump", key });
        if (c.CheckOk("face.dump"))
        {
            var diffs = new List<string>();
            BridgeCoreTest.W2Shape.ShapeFace(diffs, recRest["face"] as JObject, BridgeCoreTest.W2Shape.Result(c.J));
            W2Report(c, "face.dump: HS2's members, order and kinds (fbs absent by design)", diffs);
        }

        // ---- H3 camera.state (also with set)
        c.Id = "H3";
        var recCam = recRest["visual"]?["face"]?["camera"] as JObject;
        yield return c.Call(new { cmd = "camera.state" });
        if (c.CheckOk("camera.state"))
        {
            var diffs = new List<string>();
            BridgeCoreTest.W2Shape.ShapeCamera(diffs, recCam, c.J["camera"] as JObject);
            W2Report(c, "camera.state: HS2's members, order and kinds", diffs);
        }
        yield return c.Call(new { cmd = "camera.state", set = new { pos = new[] { 0f, 15f, 30f }, rot = new[] { 0f, 180f, 0f }, dist = 30f, fov = 23f } });
        if (c.CheckOk("camera.state {set}"))
        {
            var diffs = new List<string>();
            var res = BridgeCoreTest.W2Shape.Result(c.J);
            BridgeCoreTest.W2Shape.ShapeObject(diffs, "camera.state(set)", new[] { "note", "requested", "camera" }, null, res);
            BridgeCoreTest.W2Shape.ShapeObject(diffs, "camera.state(set).requested", new[] { "position", "rotation", "target", "dist" }, null, res["requested"] as JObject);
            BridgeCoreTest.W2Shape.ShapeCamera(diffs, recCam, res["camera"] as JObject);
            var p = res["camera"]?["position"];
            bool same = p != null && Math.Abs(W2Dbl(p[0]) - 0) <= 1e-4 && Math.Abs(W2Dbl(p[1]) - 15) <= 1e-4 && Math.Abs(W2Dbl(p[2]) - 30) <= 1e-4;
            if (!same) diffs.Add("camera.position " + p?.ToString(Formatting.None) + " is not the requested [0, 15, 30] in the same call");
            W2Report(c, "camera.state {set}: note, requested, camera in HS2's order; the camera moved in the same call", diffs);
        }

        // ---- H4 fk.list exact after compare case 1 (a fresh BR-Chan)
        c.Id = "H4";
        yield return c.Call(new { cmd = "chara.load", path = c.FemaleRel, sex = "female" }, 600);
        string kf = c.Last != null && c.Last.Ok ? (string)c.J["added"]?[0]?["key"] : null;
        c.Check(kf != null, "chara.load a fresh BR-Chan", AcceptanceCtx.Short(c.Last?.ToString()));
        if (kf != null)
        {
            yield return c.Call(new { cmd = "pose.load", key = kf, path = BridgeCoreTest.W2Shape.PoseCase1 });
            c.CheckOk("pose.load compare case 1");
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "fk.list", key = kf, exact = true });
            if (c.CheckOk("fk.list exact"))
            {
                var diffs = new List<string>();
                var recFk = recPose["fk"] as JObject;
                BridgeCoreTest.W2Shape.ShapeFk(diffs, recFk, BridgeCoreTest.W2Shape.Result(c.J));
                W2Report(c, "fk.list exact: HS2's members, order and kinds", diffs);
                var hs2 = new SortedSet<int>((recFk?["bones"] as JArray)?.OfType<JObject>().Select(b => (int)b["key"]) ?? new int[0]);
                var next = new SortedSet<int>((c.J["bones"] as JArray)?.OfType<JObject>().Select(b => (int)b["key"]) ?? new int[0]);
                var expected = new SortedSet<int>(hs2.Where(k => k < 11000 || k > 11003));
                var onlyHs2 = expected.Except(next).ToList();
                var onlyNext = next.Except(hs2).ToList();
                c.Check(hs2.Count > 0 && onlyHs2.Count == 0 && onlyNext.Count == 0, "the entry key set = HS2's pose_1 keys except 11000–11003",
                    $"HS2 {hs2.Count}, HS2Next {next.Count}; only HS2: {string.Join(",", onlyHs2.Take(12))}; only HS2Next: {string.Join(",", onlyNext.Take(12))}");
            }
            yield return c.Call(new { cmd = "delete", key = kf });
            c.CheckOk("delete the H4 character");
        }

        // ---- H5 scene objects
        c.Id = "H5";
        yield return c.Call(new { cmd = "scene" });
        if (c.CheckOk("scene"))
        {
            var o = (c.J["objects"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["key"] == key);
            var diffs = new List<string>();
            BridgeCoreTest.W2Shape.ShapeSceneObject(diffs, recRest["state"] as JObject, o);
            BridgeCoreTest.W2Shape.ShapeObject(diffs, "scene", new[] { "map", "camera", "objects" }, null, BridgeCoreTest.W2Shape.Result(c.J));
            BridgeCoreTest.W2Shape.ShapeObject(diffs, "scene.map", new[] { "no", "name" }, null, c.J["map"] as JObject);
            W2Report(c, "scene: map, camera, objects; the character in HS2's order and kinds", diffs);
        }
    }

    static void W2Report(AcceptanceCtx c, string what, List<string> diffs) =>
        c.Check(diffs.Count == 0, what, diffs.Count == 0 ? "" : diffs.Count + " differences: " + string.Join("; ", diffs.Take(12)));
}
