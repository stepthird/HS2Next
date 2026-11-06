// AdvancedPoseTest, the bone corrections through the commands (Phase 6.3 package M).
//   AB3   pose.bones.set / undo / redo / clear: bones.dump's values and the block's JSON
//   AB14  a Timeline playing bones9's FK tracks: the corrections compose on them; edits while it plays and while it is paused are neither
//         refused nor suspend a track; timeline.stop {bake} leaves the block
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class AdvancedPoseTest
{
    static void Headless_O_Commands()
    {
        Group("AB3", "the commands: set, undo, redo, clear", AB3);
        Group("AB14", "under a playing Timeline", AB14);
    }

    static List<string> UndoLabels(Host h) => (h.Call(new { cmd = "history" })["undo"] as JArray)?.Select(x => (string)x).ToList() ?? new List<string>();

    static JObject DumpBone(Host h, string key, string bone)
    {
        var d = h.Ok(new { cmd = "bones.dump", key, root = "all" }, "bones.dump")["bones"] as JArray;
        return d?.OfType<JObject>().FirstOrDefault(x => (string)x["name"] == bone);
    }

    static double[] Nums(JToken t) => (t as JArray)?.Select(x => (double)x).ToArray() ?? new double[0];

    static void AB3()
    {
        var f = NewTwins(femaleRel, "female", "AB3");
        try
        {
            var h = f.H;
            const string bone = "cf_J_Hand_Index02_L";
            f.Step(2);
            var twin = Bone(f.RB, bone);
            Check("the block is empty at first", Block(h, f.A).IsEmpty);
            int n0 = UndoLabels(h).Count;

            // one member
            var r = h.Ok(new { cmd = "pose.bones.set", key = f.A, bone, rot = new[] { 10f, 20f, 30f } }, "pose.bones.set rot");
            Check("the reply names the change", (r["changed"] as JToken) != null && r["removed"] != null && r["bones"] != null, Short(h.LastText));
            Check("one entry «Bone correction»", UndoLabels(h).Count == n0 + 1 && UndoLabels(h)[0] == "Bone correction", string.Join(", ", UndoLabels(h).Take(3)));
            string j1 = Block(h, f.A).ToJson();
            var c1 = Corr(rot: new Vec3(10f, 20f, 30f));
            double d = Angle(Bone(f.RA, bone).localRotation, twin.localRotation * Quaternion.Euler(10f, 20f, 30f));
            Check("it shows at once", d <= 0.01, $"{d:G3} deg");
            var dumped = DumpBone(h, f.A, bone);
            var lq = Nums(dumped?["lq"]);
            var q = Bone(f.RA, bone).localRotation;
            Check("bones.dump in the same request shows it", lq.Length == 4 && Math.Abs(lq[0] - q.x) <= 1e-6 && Math.Abs(lq[3] - q.w) <= 1e-6);

            // another member keeps the first
            h.Ok(new { cmd = "pose.bones.set", key = f.A, bone, pos = new[] { 0.01f, 0.02f, 0.03f } }, "pose.bones.set pos");
            string j2 = Block(h, f.A).ToJson();
            var e = Block(h, f.A).Bones[bone];
            Check("omitted members keep their value", e.Rot.X == 10f && e.Rot.Y == 20f && e.Rot.Z == 30f && e.Pos.X == 0.01f, j2);
            h.Ok(new { cmd = "pose.bones.set", key = f.A, bones = new Dictionary<string, object> { ["cf_J_NoseBase_s"] = new { scale = new[] { 1.2f, 1.1f, 0.9f } }, [bone] = new { rot = new[] { 11f, 20f, 30f } } } }, "pose.bones.set bones");
            string j3 = Block(h, f.A).ToJson();
            Check("two bones in one call: one entry", UndoLabels(h).Count == n0 + 3, "" + (UndoLabels(h).Count - n0));
            f.Step(2);
            CheckComposed(f, "two bones", new[] { "cf_J_NoseBase_s" }, Corr(scale: new Vec3(1.2f, 1.1f, 0.9f)));

            // undo, redo: the block's JSON and the bone
            h.Ok(new { cmd = "undo" }, "undo");
            Check("undo: the block's JSON", Block(h, f.A).ToJson() == j2);
            h.Ok(new { cmd = "undo" }, "undo");
            Check("undo: the block's JSON", Block(h, f.A).ToJson() == j1);
            h.Rt.ApplyNow();
            d = Angle(Bone(f.RA, bone).localRotation, twin.localRotation * Quaternion.Euler(10f, 20f, 30f));
            Check("undo: the bone follows", d <= 0.01 && Dist(Bone(f.RA, bone).localPosition, twin.localPosition) <= 1e-6, $"{d:G3} deg");
            h.Ok(new { cmd = "undo" }, "undo");
            Check("undo to the start: the block is empty", Block(h, f.A).IsEmpty);
            h.Rt.ApplyNow();
            Same(f, "undo to the start");
            Check("undo to the start: no stage", !f.RA.Motion.Stages.Any(s => s.Name.StartsWith("pose.", StringComparison.Ordinal)));
            h.Ok(new { cmd = "redo" }, "redo");
            Check("redo: the block's JSON", Block(h, f.A).ToJson() == j1);
            h.Ok(new { cmd = "redo" }, "redo");
            h.Ok(new { cmd = "redo" }, "redo");
            Check("redo: the block's JSON", Block(h, f.A).ToJson() == j3);
            h.Rt.ApplyNow();
            Check("redo: the stages are back", f.RA.Motion.Stages.Any(s => s.Name == "pose.apply"));

            // identity removes the entry
            h.Ok(new { cmd = "pose.bones.set", key = f.A, bone = "cf_J_NoseBase_s", scale = new[] { 1f, 1f, 1f } }, "identity");
            Check("identity removes the entry", !Block(h, f.A).Bones.ContainsKey("cf_J_NoseBase_s") && Block(h, f.A).Bones.ContainsKey(bone), Block(h, f.A).ToJson());

            // clear
            var cl = h.Ok(new { cmd = "pose.bones.clear", key = f.A, all = true }, "pose.bones.clear all");
            Check("clear: {removed}, one entry «Clear bone corrections»", (int)cl["removed"] == 1 && UndoLabels(h)[0] == "Clear bone corrections", Short(h.LastText));
            Check("clear: the block is empty", Block(h, f.A).IsEmpty);
            h.Rt.ApplyNow();
            Same(f, "clear");
            h.Ok(new { cmd = "undo" }, "undo the clear");
            Check("undo the clear: the entry is back", Block(h, f.A).Bones.ContainsKey(bone));
            h.Rt.ApplyNow();
            f.Step(2);
            var rr = h.Ok(new { cmd = "pose.bones", key = f.A, correctedOnly = true }, "pose.bones correctedOnly");
            var row = (rr["bones"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["bone"] == bone);
            Check("pose.bones lists it, shown", row != null && (bool)row["corrected"] && (bool)row["shown"], Short(h.LastText));
        }
        finally { f.Dispose(); }
    }

    static void AB14()
    {
        var f = new Twins { H = NewHost() };
        try
        {
            var h = f.H;
            f.A = Key(h.Ok(new { cmd = "chara.load", path = femaleRel, sex = "female" }, "chara.load A"));
            f.B = Key(h.Ok(new { cmd = "chara.load", path = femaleRel, sex = "female" }, "chara.load B"));
            foreach (var k in new[] { f.A, f.B })
            {
                h.Ok(new { cmd = "chara.motion", key = k, physics = false }, "physics off");
                h.Ok(new { cmd = "chara.anime", key = k, group = 19, category = 0, no = 0, speed = 0f }, "the row at speed 0");
                h.Ok(new { cmd = "fk.enable", key = k, on = true }, "FK on");
                h.Ok(new { cmd = "fk.enable", key = k, groups = new[] { "Body", "Neck" }, on = true }, "FK groups Body and Neck");
            }
            h.Frames(3);
            // bones9's FK tracks of its second character, on both twins
            var comp = JObject.Parse(File.ReadAllText(Path.Combine(ContentSettings.ProjectRoot, "Tests", "Timeline", "compositions", "bones9.json")));
            var tracks = new JArray();
            var boneNames = new List<string>();
            foreach (var t in comp["tracks"].OfType<JObject>())
            {
                if ((string)t["key"] != "$b") continue;
                foreach (var k in new[] { f.A, f.B })
                {
                    var c = (JObject)t.DeepClone();
                    c["key"] = k;
                    tracks.Add(c);
                }
                boneNames.Add((string)t["bone"]);
            }
            var req = new JObject { ["cmd"] = "timeline.build", ["duration"] = comp["duration"], ["tracks"] = tracks, ["play"] = false };
            h.Ok(req.ToString(Formatting.None), "timeline.build with bones9's FK tracks on both");
            var corr = Corr(rot: new Vec3(6f, -9f, 12f));
            var bones = boneNames.ToArray();
            h.Ok(new { cmd = "pose.bones.set", key = f.A, bones = bones.ToDictionary(b => b, b => (object)new { rot = new[] { 6f, -9f, 12f } }) }, "corrections on the tracks' bones");
            string block = Block(h, f.A).ToJson();
            h.Ok(new { cmd = "timeline.play" }, "play");
            var wp = 0d; var wr = 0d; var ws = 0d; string w = "";
            for (int k = 0; k < 10; k++)
            {
                h.Frames(30);
                foreach (var b in bones) Composed(f.RA, f.RB, b, corr, true, ref wp, ref wr, ref ws, ref w);
            }
            Check("FK ∘ correction while it plays", wp <= 1e-5 && wr <= 0.01 && ws <= 1e-5, $"pos {wp:G3} rot {wr:G3} scale {ws:G3} ({w})");
            var st = h.Ok(new { cmd = "timeline.status" }, "status");
            Check("the tracks hold values, none suspended", (string)st["state"] == "playing" && (int)st["suspended"] == 0 && (int)st["activeTracks"] > 0, Short(h.LastText));

            // an edit while it plays
            h.Ok(new { cmd = "pose.bones.set", key = f.A, bone = bones[0], rot = new[] { 8f, 0f, 0f } }, "an edit while it plays");
            st = h.Ok(new { cmd = "timeline.status" }, "status");
            Check("the edit while it plays neither stops nor suspends", (string)st["state"] == "playing" && (int)st["suspended"] == 0, Short(h.LastText));
            h.Frames(10);
            double d = Angle(Bone(f.RA, bones[0]).localRotation, Bone(f.RB, bones[0]).localRotation * Quaternion.Euler(8f, 0f, 0f));
            Check("it shows on the track's value", d <= 0.01, $"{d:G3} deg");

            // an edit while it is paused
            h.Ok(new { cmd = "timeline.pause" }, "pause");
            h.Ok(new { cmd = "pose.bones.set", key = f.A, bone = bones[0], rot = new[] { 6f, -9f, 12f } }, "an edit while it is paused");
            st = h.Ok(new { cmd = "timeline.status" }, "status");
            Check("the edit while it is paused suspends nothing", (string)st["state"] == "paused" && (int)st["suspended"] == 0, Short(h.LastText));
            h.Frames(3);
            wp = wr = ws = 0;
            foreach (var b in bones) Composed(f.RA, f.RB, b, corr, true, ref wp, ref wr, ref ws, ref w);
            Check("composed while it is paused", wp <= 1e-5 && wr <= 0.01 && ws <= 1e-5, $"pos {wp:G3} rot {wr:G3} scale {ws:G3} ({w})");
            Check("the block holds what the commands set", Block(h, f.A).ToJson() == block);

            // the bake
            var fk0 = (h.Session.Model.Find(f.A).State as CharaNodeState).Fk.Bones.Count;
            var sp = h.Ok(new { cmd = "timeline.stop", bake = true }, "timeline.stop {bake}");
            Check("the bake wrote FK values", (int)sp["baked"] > 0, Short(h.LastText));
            Check("the bake leaves the block", Block(h, f.A).ToJson() == block);
            h.Frames(3);
            wp = wr = ws = 0;
            foreach (var b in bones) Composed(f.RA, f.RB, b, corr, true, ref wp, ref wr, ref ws, ref w);
            Check("after the bake: FK ∘ correction", wp <= 1e-5 && wr <= 0.01 && ws <= 1e-5, $"pos {wp:G3} rot {wr:G3} scale {ws:G3} ({w})");
        }
        finally { f.Dispose(); }
    }
}
