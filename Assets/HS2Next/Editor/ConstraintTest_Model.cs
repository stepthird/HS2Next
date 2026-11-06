// ConstraintTest, constraints beside the Timeline, the model and the other paths of a frame (Phase 6.5 package O).
// Edit mode:
//   CO16  a character thumbnail's scope: no driven value in it, bones and stages back after
//   CD1   a save under the pause with constraints on, then every constraint off with undo false, then a save: equal apart from the switches
//   CD2   600 frames of an active list: version, dirty mark and history stand
//   CD3   chara.look is not captured with a driven head, and is again after off
//   CD5   the Timeline's Key stores what shows; timeline.stop with bake writes the Timeline's values only
//   CD6   fk.capture and ik.copy on a driven character give the bare pose
// Real frames:
//   CO10  every row of the table of constraints and the Timeline
//   CO11  the chain at a time t by play, by a pause and scene.step, by a Timeline seek and in a recording: equal values
//   CO12  timeline.seek and capture in one list
//   CO14  under the pause: a drag of a source moves the target each frame, one settle after the commit; at rest no settle
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.App;
using HS2Next.Motion;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Overrides;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class ConstraintTest
{
    static void Headless_E_Model()
    {
        Group("CO16", "a thumbnail's scope", CO16);
        Group("CD1", "two saves around a switch", CD1);
        Group("CD2", "frames leave the model alone", CD2);
        Group("CD3", "the look of a driven head", CD3);
        Group("CD5", "the Timeline's Key and bake", CD5);
        Group("CD6", "captures read the bare pose", CD6);
    }

    /// <summary>Twins of the female target on the idle row, physics off, in one host.</summary>
    static (Host h, string a, string b) Twins(string tag, float speed = 0f)
    {
        var h = NewHost();
        var a = LoadChara(h, femaleRel, "female", tag + ": A");
        var b = LoadChara(h, femaleRel, "female", tag + ": B");
        foreach (var k in new[] { a, b }) h.Ok(new { cmd = "chara.anime", key = k, group = 19, category = 0, no = 0, time = 0f, speed }, tag + ": the row");
        h.Frames(3);
        return (h, a, b);
    }

    static void CO16()
    {
        var (h, a, b) = Twins("CO16");
        try
        {
            h.Ok(new { cmd = "ik.copy", key = a, from = "current", enable = true }, "IK on A");
            h.Ok(new { cmd = "ik.copy", key = b, from = "current", enable = true }, "IK on B");
            h.Frames(2);
            var ra = h.Rt.FindChara(a); var rb = h.Rt.FindChara(b);
            string s = Node(h, "the source");
            Place(h, s, new Vector3(1f, 8f, 2f), new Vector3(10f, 20f, 30f));
            var e1 = CEntry(new ConstraintEnd(s), new ConstraintEnd(a, FreeBone), ConstraintComponents.Rotation);
            var e2 = CEntry(new ConstraintEnd(s), new ConstraintEnd(a, ik: "RightHand"), ConstraintComponents.Position);
            Put(h, "a bone and an IK target on A", e1, e2);
            h.Frames(3);
            var t = B(ra, FreeBone);
            var hand = B(ra, "cf_J_Hand_R");
            var driven = t.rotation; var drivenHand = hand.position;
            Check("the bone and the hand are driven", Deg(t.rotation, h.Rt.HolderOf(s).rotation) <= 0.02f && (hand.position - B(rb, "cf_J_Hand_R").position).magnitude > 0.01f);
            using (var sa = StudioDefaultPoseScope.Enter(ra))
            using (var sb = StudioDefaultPoseScope.Enter(rb))
            {
                Check("in the scope: the bone is the twin's", Deg(t.localRotation, B(rb, FreeBone).localRotation) <= 1e-3f, Deg(t.localRotation, B(rb, FreeBone).localRotation).ToString("G3"));
                Check("in the scope: the hand is the twin's", (hand.localPosition - B(rb, "cf_J_Hand_R").localPosition).magnitude <= 1e-5 && Deg(hand.localRotation, B(rb, "cf_J_Hand_R").localRotation) <= 1e-3f);
                var stages = ra.Motion.Stages.Where(x => x.Name.StartsWith("bones.driven", StringComparison.Ordinal)).ToList();
                Check("in the scope: the three stages are off", stages.Count == 3 && stages.All(x => !x.Enabled), string.Join(",", stages.Select(x => x.Name + "=" + x.Enabled)));
            }
            var back = ra.Motion.Stages.Where(x => x.Name.StartsWith("bones.driven", StringComparison.Ordinal)).ToList();
            Check("after the scope: the stages are on", back.Count == 3 && back.All(x => x.Enabled));
            h.Frames(3);
            Check("after the scope: the driven values are back", Deg(t.rotation, driven) <= 0.02f && (hand.position - drivenHand).magnitude <= 1e-4, (hand.position - drivenHand).magnitude.ToString("G3"));
        }
        finally { h.Dispose(); }
    }

    static void CD1()
    {
        var (h, a, b) = Twins("CD1");
        try
        {
            h.Ok(new { cmd = "ik.copy", key = b, from = "current", enable = true }, "IK on B");
            string s = Node(h, "the source"), t = Node(h, "the node target");
            Place(h, s, new Vector3(1f, 8f, 2f), new Vector3(10f, 20f, 30f));
            var list = new[]
            {
                CEntry(new ConstraintEnd(s), new ConstraintEnd(t), ConstraintComponents.Position, ConstraintComponents.Rotation),
                CEntry(new ConstraintEnd(s), new ConstraintEnd(a, FreeBone), ConstraintComponents.Rotation),
                CEntry(new ConstraintEnd(s), new ConstraintEnd(b, ik: "RightHand"), ConstraintComponents.Position),
                CEntry(new ConstraintEnd(a, "cf_J_Head"), new ConstraintEnd(b), ConstraintComponents.Position),
            };
            Put(h, "four constraints", list);
            h.Ok(new { cmd = "scene.pause", paused = true }, "the global pause");
            h.Frames(5);
            string p1 = (string)h.Ok(new { cmd = "scene.save", name = "cd1a", overwrite = true, thumb = "placeholder", clock = SaveClockE }, "save A")["path"];
            foreach (var e in list)
            {
                var off = e.Clone();
                off.On = false;
                var r = h.Session.Commands.Execute(ConstraintEditCommand.ReplaceEntry(off), ExecuteOptions.NotRecorded);
                Check("off with undo false", r.Ok, r.Message);
            }
            h.Frames(3);
            string p2 = (string)h.Ok(new { cmd = "scene.save", name = "cd1b", overwrite = true, thumb = "placeholder", clock = SaveClockE }, "save B")["path"];
            var za = Zip(p1); var zb = Zip(p2);
            Check("the same entries", za.Keys.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(zb.Keys.OrderBy(x => x, StringComparer.Ordinal)), string.Join(",", za.Keys));
            var diff = new List<string>();
            int compared = 0;
            foreach (var kv in za)
            {
                if (kv.Key == "scene.json" || kv.Key.StartsWith("thumb", StringComparison.Ordinal) || !zb.TryGetValue(kv.Key, out var other)) continue;
                compared++;
                if (!kv.Value.SequenceEqual(other)) diff.Add(kv.Key);
            }
            Check("every entry but the scene and the thumbnail is equal", diff.Count == 0 && compared > 0, compared + " compared; " + string.Join(",", diff));
            JObject Strip(byte[] x)
            {
                var o = JObject.Parse(Encoding.UTF8.GetString(x));
                foreach (var c in o["constraints"] as JArray ?? new JArray()) ((JObject)c).Remove("on");
                return o;
            }
            var ja = Strip(za["scene.json"]); var jb = Strip(zb["scene.json"]);
            Check("scene.json is equal apart from the switches", JToken.DeepEquals(ja, jb));
            Check("the file holds the list, in both", (ja["constraints"] as JArray)?.Count == 4 && (jb["constraints"] as JArray)?.Count == 4);
            h.Ok(new { cmd = "scene.pause", paused = false }, "running");
        }
        finally { h.Dispose(); }
    }

    static void CD2()
    {
        var (h, a, b) = Twins("CD2", 1f);
        try
        {
            string s = Node(h, "the source"), t = Node(h, "the target");
            Put(h, "an active list", CEntry(new ConstraintEnd(a, "cf_J_Hand_R"), new ConstraintEnd(t), ConstraintComponents.Position, ConstraintComponents.Rotation),
                CEntry(new ConstraintEnd(t), new ConstraintEnd(b, FreeBone), ConstraintComponents.Rotation));
            h.Frames(3);
            var bus = h.Session.Commands;
            long v = bus.Version;
            bool dirty = bus.IsDirty;
            int undo = bus.UndoList.Count, redo = bus.RedoList.Count;
            string model = h.Session.Model.Find(t).Doc.Transform.Pos.ToString();
            h.Frames(600);
            Check("600 frames: the version stands", bus.Version == v, (bus.Version - v).ToString());
            Check("the dirty mark stands", bus.IsDirty == dirty);
            Check("the history stands", bus.UndoList.Count == undo && bus.RedoList.Count == redo);
            Check("the target's model value stands", h.Session.Model.Find(t).Doc.Transform.Pos.ToString() == model);
        }
        finally { h.Dispose(); }
    }

    static void CD3()
    {
        var (h, a, b) = Twins("CD3", 1f);
        try
        {
            var ra = h.Rt.FindChara(a);
            var state = ra.Node.State as CharaNodeState;
            h.Frames(5);
            h.Rt.CaptureNow();
            string neck0 = Fix(state), eyes0 = state.Look.EyeRotations?.ToString();
            Check("a character without a slot is captured", neck0.Length > 2, neck0.Length.ToString());
            string s = Node(h, "the source");
            Place(h, s, Vector3.zero, new Vector3(0f, 30f, 0f));
            var e = CEntry(new ConstraintEnd(s), new ConstraintEnd(a, "cf_J_Head"), ConstraintComponents.Rotation);
            Put(h, "a driven head", e);
            h.Ok(new { cmd = "chara.face", key = a, neckLook = 1 }, "the neck on the camera");
            h.Frames(20);
            string time0 = state.Anime.Time.ToString("R");
            h.Rt.CaptureNow();
            Check("the time is still captured", state.Anime.Time.ToString("R") != time0);
            Check("the look stands: a driven head is not captured", Fix(state) == neck0, Fix(state).Length.ToString());
            Check("the eyes stand", state.Look.EyeRotations?.ToString() == eyes0);
            var off = e.Clone();
            off.On = false;
            h.Session.Commands.Execute(ConstraintEditCommand.ReplaceEntry(off), ExecuteOptions.NotRecorded);
            h.Frames(20);
            h.Rt.CaptureNow();
            Check("switched off: captured again", Fix(state) != neck0);
        }
        finally { h.Dispose(); }
    }

    static string Fix(CharaNodeState s) => string.Join(";", s.Look.NeckFix.Select(k => k.Key + "=" + k.Value));

    static void CD5()
    {
        var (h, a, b) = Twins("CD5");
        try
        {
            string s = Node(h, "the source"), t = Node(h, "the target");
            Place(h, s, new Vector3(3f, 4f, 5f));
            Place(h, t, new Vector3(1f, 1f, 1f));
            var built = h.Ok(new { cmd = "timeline.build", duration = 2f, play = false, tracks = new object[] { new { type = "pos", key = t, keys = new object[] { new { t = 0f, v = new[] { 7f, 7f, 7f } } } } } }, "a track on the target");
            Put(h, "the constraint on it", CEntry(new ConstraintEnd(s), new ConstraintEnd(t), ConstraintComponents.Position));
            h.Ok(new { cmd = "timeline.seek", t = 0.5f }, "seek");
            h.Frames(3);
            var holder = h.Rt.HolderOf(t);
            Check("the constraint shows", (holder.position - h.Rt.HolderOf(s).position).magnitude <= 1e-5, holder.position.ToString("F4"));
            var key = h.Ok(new { cmd = "timeline.key.set", track = 0, t = 1f }, "Key with no value");
            var v = key["v"] as JArray;
            Check("the key stores the value shown, the winning slot's", v != null && Math.Abs((double)v[0] - 3) <= 1e-5 && Math.Abs((double)v[1] - 4) <= 1e-5 && Math.Abs((double)v[2] - 5) <= 1e-5, key["v"]?.ToString(Newtonsoft.Json.Formatting.None));
            h.Ok(new { cmd = "timeline.seek", t = 0f }, "seek to the first key");
            h.Frames(3);
            var stop = h.Ok(new { cmd = "timeline.stop", bake = true }, "stop with bake");
            var doc = h.Session.Model.Find(t).Doc.Transform.Pos;
            Check("bake wrote the Timeline's value, not the constraint's", Math.Abs(doc.X - 7f) <= 1e-5 && Math.Abs(doc.Y - 7f) <= 1e-5, doc.ToString());
            h.Frames(2);
            Check("the constraint still shows", (holder.position - h.Rt.HolderOf(s).position).magnitude <= 1e-5);
        }
        finally { h.Dispose(); }
    }

    static void CD6()
    {
        var (h, a, b) = Twins("CD6");
        try
        {
            var ra = h.Rt.FindChara(a); var rb = h.Rt.FindChara(b);
            const string bone = "cf_J_ArmUp00_R";
            string s = Node(h, "the source");
            Place(h, s, Vector3.zero, new Vector3(30f, 40f, 50f));
            Put(h, "a driven arm on A", CEntry(new ConstraintEnd(s), new ConstraintEnd(a, bone), ConstraintComponents.Rotation));
            h.Frames(3);
            Check("the arm stands apart", Deg(B(ra, bone).localRotation, B(rb, bone).localRotation) > 1f);
            foreach (var k in new[] { a, b })
            {
                h.Ok(new { cmd = "fk.enable", key = k, on = false }, "FK off");
                h.Ok(new { cmd = "fk.capture", key = k }, "fk.capture");
            }
            var fa = (h.Session.Model.Find(a).State as CharaNodeState).Fk; var fb = (h.Session.Model.Find(b).State as CharaNodeState).Fk;
            string Text(CharaFkState f) => string.Join(";", f.Bones.Select(k => k.Key + "=" + k.Value));
            Check("fk.capture stored the same values on both", fa.Bones.Count > 0 && Text(fa) == Text(fb), fa.Bones.Count + " bones");
            foreach (var k in new[] { a, b })
                h.Ok(new { cmd = "ik.copy", key = k, from = "current" }, "ik.copy from the pose");
            var ia = (h.Session.Model.Find(a).State as CharaNodeState).Ik; var ib = (h.Session.Model.Find(b).State as CharaNodeState).Ik;
            string Targets(CharaIkState k) => string.Join(";", k.Targets.Targets.Select(x => x == null ? "-" : x.Pos + " " + x.Rot));
            Check("ik.copy stored the same targets on both", Targets(ia).Length > 13 && Targets(ia) == Targets(ib));
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ play mode

    static IEnumerator Play_E_Model()
    {
        current = "Model";
        if (!new[] { "CO10", "CO11", "CO12", "CO14" }.Any(Want)) yield break;
        var ready = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ready);
        var app = AppCore.Current;
        if (!Check("the app runs with its bridge and runtime", ready[0] && app != null && app.Stage == AppStage.Ready && app.Bridge != null && app.Runtime != null, app?.Stage.ToString())) yield break;
        float old = Time.captureDeltaTime;
        Time.captureDeltaTime = Dt;
        HS2Next.Motion.Face.FaceSetup.Seed = 0x5eed;
        try
        {
            if (POpen("CO10", "beside the Timeline")) yield return CO10(app);
            if (POpen("CO14", "a drag of a source under the pause")) yield return CO14(app);
            if (POpen("CO11", "one result on every path")) yield return CO11(app);
            yield return PCall(app, new { cmd = "timeline.stop" });
            yield return PCall(app, new { cmd = "scene.pause", paused = false });
            yield return PCall(app, new { cmd = "scene.new" });
        }
        finally { Time.captureDeltaTime = old; HS2Next.Motion.Face.FaceSetup.Seed = null; }
    }

    static object[] Keys(params (float t, float[] v)[] k) => k.Select(x => (object)new { t = x.t, v = x.v, ease = "linear" }).ToArray();

    static IEnumerator CO10(AppCore app)
    {
        yield return PScene(app, 1, 0, "CO10");
        if (pA == null) yield break;
        var ra = PR(app, pA);
        string s = null, t1 = null, t2 = null, t3 = null, t4 = null, s2 = null;
        yield return AddNode(app, "the source", k => s = k);
        yield return AddNode(app, "a plain target", k => t1 = k);
        yield return AddNode(app, "a target on a bone", k => t2 = k);
        yield return AddNode(app, "a target with a track, weight 1", k => t3 = k);
        yield return AddNode(app, "a target with a track, weight 0.5", k => t4 = k);
        yield return AddNode(app, "the bones' source", k => s2 = k);
        app.Session.Set(s2, "transform.rot", new Vec3(20f, 30f, 40f));
        yield return PCall(app, new { cmd = "fk.enable", key = pA, on = true });
        yield return PCall(app, new { cmd = "fk.enable", key = pA, groups = new[] { "Body" }, on = true });
        Check("FK on the source character", POk, Short(pLastText));
        // the track turns the source's parent: a bone that only turns keeps its position
        const string fkSource = "cf_J_ArmUp00_L", moved1 = "cf_J_Hand_L", driven1 = "cf_J_ArmLow01_R", driven2 = "cf_J_ArmLow01_L";
        yield return PCall(app, new
        {
            cmd = "timeline.build", duration = 4f, loop = false, play = false,
            tracks = new object[]
            {
                new { type = "pos", key = s, keys = Keys((0f, new[] { 0f, 0f, 0f }), (4f, new[] { 8f, 4f, 2f })) },
                new { type = "pos", key = t3, keys = Keys((0f, new[] { 1f, 1f, 1f }), (4f, new[] { 1f, 5f, 1f })) },
                new { type = "pos", key = t4, keys = Keys((0f, new[] { 2f, 2f, 2f }), (4f, new[] { 2f, 6f, 2f })) },
                new { type = "rot", key = pA, bone = fkSource, keys = Keys((0f, new[] { 0f, 0f, 0f }), (4f, new[] { 0f, 0f, 60f })) },
                new { type = "rot", key = pA, bone = driven1, keys = Keys((0f, new[] { 0f, 0f, 0f }), (4f, new[] { 0f, 50f, 0f })) },
                new { type = "rot", key = pA, bone = driven2, keys = Keys((0f, new[] { 0f, 0f, 0f }), (4f, new[] { 0f, -50f, 0f })) },
            },
        });
        Check("the Timeline", POk, Short(pLastText));
        var e1 = CEntry(new ConstraintEnd(s), new ConstraintEnd(t1), ConstraintComponents.Position);
        var e2 = CEntry(new ConstraintEnd(pA, moved1), new ConstraintEnd(t2), ConstraintComponents.Position);
        var e3 = CEntry(new ConstraintEnd(s), new ConstraintEnd(t3), ConstraintComponents.Position);
        var e4 = CEntry(new ConstraintEnd(s), new ConstraintEnd(t4), ConstraintComponents.Position);
        e4.Weight = 0.5f;
        var e5 = CEntry(new ConstraintEnd(s2), new ConstraintEnd(pA, driven1), ConstraintComponents.Rotation);
        var e6 = CEntry(new ConstraintEnd(s2), new ConstraintEnd(pA, driven2), ConstraintComponents.Rotation);
        e6.Weight = 0.5f;
        PutList(app.Session, app.Runtime, "the constraints", e1, e2, e3, e4, e5, e6);
        Check("all active", Part(app).Active == 6, Part(app).Active.ToString());
        yield return PCall(app, new { cmd = "timeline.play", from = 0 });
        Check("timeline.play", POk, Short(pLastText));
        yield return PFrames(2);
        var ov = app.Runtime.Overrides;
        var hs = app.Runtime.HolderOf(s); var h1 = app.Runtime.HolderOf(t1); var h2 = app.Runtime.HolderOf(t2); var h3 = app.Runtime.HolderOf(t3); var h4 = app.Runtime.HolderOf(t4); var hs2 = app.Runtime.HolderOf(s2);
        var fk = B(ra, moved1); var b1 = B(ra, driven1); var b2 = B(ra, driven2);
        var track4 = ov.Slots(t4, OverrideLayer.TimelineOwner).FirstOrDefault(x => x.Path == "transform.pos");
        var fkSlot2 = ov.Slots(pA, OverrideLayer.TimelineOwner).FirstOrDefault(x => x.ParsedPath.Channel == OverrideChannel.FkBone && x.ParsedPath.Bone == driven2);
        if (!Check("the tracks' slots", track4 != null && fkSlot2 != null)) yield break;
        double w1 = 0, w2 = 0, w3 = 0, w4 = 0, w5 = 0, w6 = 0, moved = 0;
        var last = hs.position; var lastBone = fk.position;
        double movedBone = 0;
        var worldOfLocal = new Func<Vector3, Vector3>(v => v);
        yield return Sampled(app, 120, () =>
        {
            w1 = Math.Max(w1, (h1.position - hs.position).magnitude);
            w2 = Math.Max(w2, (h2.position - fk.position).magnitude);
            w3 = Math.Max(w3, (h3.position - hs.position).magnitude);
            var tv = new Vector3((float)track4[0], (float)track4[1], (float)track4[2]);
            w4 = Math.Max(w4, (h4.position - Vector3.Lerp(tv, hs.position, 0.5f)).magnitude);
            w5 = Math.Max(w5, Deg(b1.rotation, hs2.rotation));
            var fv = new Quaternion((float)fkSlot2[0], (float)fkSlot2[1], (float)fkSlot2[2], (float)fkSlot2[3]);
            var want = Quaternion.Slerp(fv, Quaternion.Inverse(b2.parent.rotation) * hs2.rotation, 0.5f);
            w6 = Math.Max(w6, Deg(b2.localRotation, want));
            moved = Math.Max(moved, (hs.position - last).magnitude);
            movedBone = Math.Max(movedBone, (fk.position - lastBone).magnitude);
            last = hs.position; lastBone = fk.position;
        });
        Check("a Timeline moves a source node: the target follows in the frame", w1 <= 1e-5 && moved > 1e-3, $"{w1:G3}, moved {moved:G3} a frame");
        Check("a Timeline turns an FK bone of a source character: the target follows in the frame", w2 <= 1e-5 && movedBone > 1e-3, $"{w2:G3}, moved {movedBone:G3} a frame");
        Check("a Timeline moves a target, weight 1: the constraint shows", w3 <= 1e-5, w3.ToString("G3"));
        Check("the same at weight 0.5: lerp of the track's value and the driven one", w4 <= 1e-5, w4.ToString("G3"));
        Check("an FK bone track on a driven bone, weight 1: stage 440 replaces it", w5 <= 0.01, w5.ToString("G3"));
        Check("the same at weight 0.5: the track's value is the base", w6 <= 0.02, w6.ToString("G3"));
        Measure($"CO10: source {w1:G3}, bone {w2:G3}, weight 1 {w3:G3}, weight 0.5 {w4:G3}, FK replaced {w5:G3} deg, FK blended {w6:G3} deg");
        yield return PCall(app, new { cmd = "overrides.list", key = t3 });
        var rows = pLast["overrides"] as JArray;
        var tlRow = rows?.OfType<JObject>().FirstOrDefault(o => (string)o["owner"] == OverrideLayer.TimelineOwner && (string)o["path"] == "transform.pos");
        var coRow = rows?.OfType<JObject>().FirstOrDefault(o => (string)o["owner"] == OverrideLayer.ConstraintsOwner && (string)o["path"] == "transform.pos");
        Check("overrides.list says so: the track is not winning, the constraint is", tlRow != null && coRow != null && !(bool)tlRow["winning"] && (bool)coRow["winning"] && (int)coRow["priority"] == 10, Short(pLastText));

        // paused, a model edit of a channel the Timeline drives: its track suspends, the constraint does not
        yield return PCall(app, new { cmd = "timeline.pause" });
        Check("timeline.pause", POk, Short(pLastText));
        yield return PFrames(2);
        var p0 = h3.position;
        app.Session.Set(t3, "transform.pos", new Vec3(9f, 9f, 9f));
        app.Runtime.ApplyNow();
        yield return PFrames(3);
        Check("the Timeline paused, a model edit: the constraint stays on the target", (h3.position - hs.position).magnitude <= 1e-5, (h3.position - hs.position).magnitude.ToString("G3"));
        // stop: the Timeline's slots go, and the base changes in that frame
        var model4 = h4.parent ? app.Session.Model.Find(t4).Doc.Transform.Pos : default;
        yield return PCall(app, new { cmd = "timeline.stop" });
        Check("timeline.stop", POk, Short(pLastText));
        yield return PFrames(2);
        var m4 = new Vector3(model4.X, model4.Y, model4.Z);
        Check("the Timeline's slots gone: the weight blends with the model", (h4.position - Vector3.Lerp(m4, hs.position, 0.5f)).magnitude <= 1e-5, h4.position.ToString("F4"));
        Check("the source is back on its model value, the targets with it", (h1.position - hs.position).magnitude <= 1e-5);
    }

    static IEnumerator CO14(AppCore app)
    {
        yield return PScene(app, 2, 0, "CO14", physics: true);
        if (pB == null) yield break;
        string s = null, t = null;
        yield return AddNode(app, "the source", k => s = k);
        yield return AddNode(app, "a node target", k => t = k);
        yield return PFrames(3);
        var rb = PR(app, pB);
        PutList(app.Session, app.Runtime, "the character's holder and a node follow the source",
            CEntry(new ConstraintEnd(s), new ConstraintEnd(pB), ConstraintComponents.Position),
            CEntry(new ConstraintEnd(s), new ConstraintEnd(t), ConstraintComponents.Position));
        yield return PCall(app, new { cmd = "scene.pause", paused = true });
        Check("the global pause", POk, Short(pLastText));
        yield return PFrames(5);
        int s0 = rb.Motion.Settles;
        var hs = app.Runtime.HolderOf(s); var ht = app.Runtime.HolderOf(t); var hb = rb.Holder;
        var tx = app.Session.Commands.Begin("drag");
        double worst = 0;
        int n = 0;
        yield return Sampled(app, 30, () =>
        {
            worst = Math.Max(worst, Math.Max((ht.position - hs.position).magnitude, (hb.position - hs.position).magnitude));
            n++;
            app.Session.Set(s, "transform.pos", new Vec3(0.02f * n, 0f, 0f));
        });
        yield return PFrames(1);
        Check("each frame of the drag: the targets stand on the source", worst <= 0.021, worst.ToString("G3"));
        Check("no settle during the drag", rb.Motion.Settles == s0, (rb.Motion.Settles - s0).ToString());
        tx.Commit();
        yield return PFrames(5);
        Check("one settle after the commit", rb.Motion.Settles == s0 + 1, (rb.Motion.Settles - s0).ToString());
        Check("the targets stand on the source", (ht.position - hs.position).magnitude <= 1e-5 && (hb.position - hs.position).magnitude <= 1e-5);
        yield return Sampled(app, 600, () => { });
        Check("a resting scene asks no settle over 600 frames", rb.Motion.Settles == s0 + 1, (rb.Motion.Settles - s0).ToString());
        yield return PCall(app, new { cmd = "scene.pause", paused = false });
    }

    // ------------------------------------------------------------ CO11, CO12

    sealed class Co11Ids { public string A, B, N1, N2; public Transform H1, H2, HandA, HandB; }

    /// <summary>The chain with a Timeline that sets A's clip by time: a pose that is a function of the Timeline's time alone.</summary>
    static IEnumerator Co11Scene(AppCore app, Co11Ids c, bool play)
    {
        yield return PCall(app, new { cmd = "timeline.stop" });
        yield return PScene(app, 2, -1, "CO11");
        if (pB == null) yield break;
        c.A = pA; c.B = pB;
        yield return PCall(app, new { cmd = "batch", cmds = new object[]
        {
            new { cmd = "chara.anime", key = pA, group = 11, category = 5, no = 0, time = 0f, speed = 0f },
            new { cmd = "chara.anime", key = pB, group = 19, category = 0, no = 0, time = 0f, speed = 0f },
        } });
        yield return PCall(app, new { cmd = "ik.copy", key = pB, from = "current", enable = true });
        Check("IK on B", POk, Short(pLastText));
        yield return PFrames(3);
        var ra = PR(app, pA); var rb = PR(app, pB);
        c.HandA = B(ra, "cf_J_Hand_R"); c.HandB = B(rb, "cf_J_Hand_L");
        var at = c.HandA.position - c.HandB.position;
        app.Session.Set(pB, "transform.pos", new Vec3(at.x, at.y, at.z));
        yield return AddNode(app, "N1", k => c.N1 = k);
        yield return AddNode(app, "N2", k => c.N2 = k);
        PutList(app.Session, app.Runtime, "the chain",
            CEntry(new ConstraintEnd(pA, "cf_J_Hand_R"), new ConstraintEnd(c.N1), ConstraintComponents.Parent),
            CEntry(new ConstraintEnd(c.N1), new ConstraintEnd(pB, ik: "LeftHand"), ConstraintComponents.Position),
            CEntry(new ConstraintEnd(pB, "cf_J_Hand_L"), new ConstraintEnd(c.N2), ConstraintComponents.Parent));
        c.H1 = app.Runtime.HolderOf(c.N1); c.H2 = app.Runtime.HolderOf(c.N2);
        yield return PCall(app, new
        {
            cmd = "timeline.build", duration = 2f, loop = false, play,
            tracks = new object[] { new { type = "animeTime", key = pA, keys = new object[] { new { t = 0f, v = 0f, ease = "linear" }, new { t = 2f, v = 1f, ease = "linear" } } } },
        });
        Check("the Timeline", POk, Short(pLastText));
    }

    static double[] Co11Read(Co11Ids c) => new[] { c.H1.position, c.HandB.position, c.H2.position, c.HandA.position }.SelectMany(v => new double[] { v.x, v.y, v.z }).ToArray();

    static double Co11Diff(double[] a, double[] b)
    {
        double w = 0;
        for (int i = 0; i < a.Length; i++) w = Math.Max(w, Math.Abs(a[i] - b[i]));
        return w;
    }

    static double TimelineTime(AppCore app) => app.Runtime.GetPart<TimelinePlayback>().Service.Time;

    static IEnumerator CO11(AppCore app)
    {
        // a play: every frame's time and values
        var c = new Co11Ids();
        yield return Co11Scene(app, c, true);
        if (c.H2 == null) yield break;
        yield return PFrames(1);
        var played = new List<(double t, double[] v)>();
        yield return Sampled(app, 90, () => played.Add((TimelineTime(app), Co11Read(c))));
        var mid = played[40];
        Check("the Timeline plays and the chain moves", mid.t > 0.1 && Co11Diff(played[10].v, played[70].v) > 1e-3, $"t {mid.t}, moved {Co11Diff(played[10].v, played[70].v):G3}");

        // a seek, in one list with a capture
        c = new Co11Ids();
        yield return Co11Scene(app, c, false);
        yield return PCall(app, new { cmd = "scene.pause", paused = true });
        int frameAtLate = -1;
        double[] atLate = null;
        var hook = new LateHook { OnLate = () => { frameAtLate = Time.frameCount; atLate = Co11Read(c); } };
        app.Runtime.AddPart(hook);
        yield return PCall(app, new { cmd = "batch", cmds = new object[] { new { cmd = "timeline.seek", t = (float)mid.t }, new { cmd = "capture", width = 64, height = 36, name = "co12" } } });
        app.Runtime.RemovePart(hook);
        if (POpen("CO12", "timeline.seek and capture in one list"))
        {
            Check("the list ran", POk, Short(pLastText));
            int shot = (int?)pLast["results"]?[1]?["frame"] ?? -1;
            Check("the numbers at the capture's frame are the seek's", atLate != null && Co11Diff(atLate, mid.v) <= 1e-4, atLate == null ? "none" : $"{Co11Diff(atLate, mid.v):G3}, capture frame {shot}, last frame {frameAtLate}");
            current = "CO11";
        }
        yield return PFrames(2);
        double seek = Co11Diff(Co11Read(c), mid.v);
        Check("a Timeline seek gives the play's values at that time", seek <= 1e-4, seek.ToString("G3"));

        // a pause and scene.step: the scene's time runs through the steps
        c = new Co11Ids();
        yield return Co11Scene(app, c, true);
        yield return PCall(app, new { cmd = "scene.pause", paused = true });
        yield return PCall(app, new { cmd = "timeline.seek", t = 0f });
        yield return PFrames(2);
        double step0 = TimelineTime(app);
        int steps = (int)Math.Round((mid.t - step0) / Dt);
        yield return PCall(app, new { cmd = "scene.step", n = steps, dt = Dt });
        Check("scene.step", POk, Short(pLastText));
        yield return PFrames(1);
        double stepT = TimelineTime(app);
        var nearest = played.OrderBy(x => Math.Abs(x.t - stepT)).First();
        double stepped = Co11Diff(Co11Read(c), nearest.v);
        Check("a pause and scene.step gives the play's values at that time", Math.Abs(nearest.t - stepT) <= 1e-4 && stepped <= 1e-3, $"t {stepT} against {nearest.t}, {stepped:G3}");
        Measure($"CO11: seek {seek:G3}, steps {stepped:G3}");
        yield return PCall(app, new { cmd = "scene.pause", paused = false });

        // a recording: its frames are ordinary frames
        c = new Co11Ids();
        yield return Co11Scene(app, c, false);
        var recorded = new List<(double t, double[] v)>();
        var rec = new LateHook { OnLate = () => recorded.Add((TimelineTime(app), Co11Read(c))) };
        app.Runtime.AddPart(rec);
        yield return PCall(app, new { cmd = "record.start", width = 64, height = 36, fps = 60, frames = 60, timeline = true, overwrite = true, path = "co11_record" });
        Check("record.start", POk, Short(pLastText));
        float until = Time.realtimeSinceStartup + 120f;
        while (Time.realtimeSinceStartup < until)
        {
            yield return PCall(app, new { cmd = "record.status" });
            if (!POk || pLast.Value<bool?>("recording") != true) break;
            yield return PFrames(5);
        }
        app.Runtime.RemovePart(rec);
        Time.captureDeltaTime = Dt;
        double worst = 0;
        int matched = 0;
        foreach (var (t, v) in recorded)
        {
            if (t <= 0.02) continue;
            var near = played.OrderBy(x => Math.Abs(x.t - t)).First();
            if (Math.Abs(near.t - t) > 1e-4) continue;
            matched++;
            worst = Math.Max(worst, Co11Diff(v, near.v));
        }
        Check("a recording's frames equal the play's at the same time", matched >= 30 && worst <= 1e-3, $"{matched} frames matched of {recorded.Count}, worst {worst:G3}");
        Measure($"CO11 recording: {matched} frames, worst {worst:G3}");
    }
}
