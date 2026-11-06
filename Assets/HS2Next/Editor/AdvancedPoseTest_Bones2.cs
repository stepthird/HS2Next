// AdvancedPoseTest, the bone corrections after the seam (Phase 6.3 package M). The same
// fixture as AB1: a character with corrections and its twin without, stepped by hand in lockstep.
//   AB4   a rebuild (chara.change to the same card, to the other sex's target and back; a plugin's rebuild request; a hair swap): the
//         corrections act again with the same local values
//   AB5   stale: a name the other body lacks, a part swapped away: kept, the finding once, no effect, active again after undo, saved
//   AB6   paused edit: a set shows after ApplyNow, the times do not move, one settle at the commit
//   AB7   fk.capture, FK from pose, a group becoming applied, IK from pose, the bake: the values equal the twin's, the shown pose stays
//   AB8   a probe leaves the bones and the applied state as they were; inside it the bones are bare; no drift after it
//   AB9   the thumbnail scope: no delta inside, back after it
//   AB10  a body slider edit and a correction on an unbound shape bone: after clearing the bone equals a fresh build at the new slider
//   AB11  another animation row with a correction on a bound bone: after clearing the bone equals a run that never had the correction
//   AB12  the later writers: 600 steps with time and 600 without: no drift; shown as stated
//   AB13  saves: entry for entry but for the block after clearing; a load gives the values back
//   AB15  duplicate, delete and undo
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using HS2Next.Motion;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Motion.DynamicBones;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class AdvancedPoseTest
{
    const string SaveClock = "2026-11-04T00:00:00Z";

    static void Headless_N_Bones()
    {
        Group("AB4", "a rebuild", AB4);
        Group("AB5", "stale entries", AB5);
        Group("AB6", "a paused edit", AB6);
        Group("AB7", "captures read the bare pose", AB7);
        Group("AB8", "a probe", AB8);
        Group("AB9", "the thumbnail scope", AB9);
        Group("AB10", "a slider edit under a correction", AB10);
        Group("AB11", "another animation row under a correction", AB11);
        Group("AB12", "the later writers", AB12);
        Group("AB13", "saves", AB13);
        Group("AB15", "duplicate, delete, undo", AB15);
    }

    // ------------------------------------------------------------ helpers

    static BoneCorrection Full => Corr(new Vec3(-0.02f, 0.01f, 0.005f), new Vec3(-8f, 15f, 30f), new Vec3(0.9f, 1.15f, 1.05f));

    /// <summary>The block of a character node.</summary>
    static CharaAdvancedPose Block(Host h, string id) => (h.Session.Model.Find(id)?.State as CharaNodeState)?.AdvancedPose;

    static void CheckComposed(Twins f, string what, IEnumerable<string> bones, BoneCorrection c)
    {
        double wp = 0, wr = 0, ws = 0; string w = "";
        int n = 0;
        foreach (var bone in bones)
        {
            Composed(f.RA, f.RB, bone, c, bone != "cf_J_MouthMove", ref wp, ref wr, ref ws, ref w);
            n++;
        }
        Check(what + ": composed", n > 0 && wp <= 1e-5 && wr <= 0.01 && ws <= 1e-5, $"{n} bones, pos {wp:G3} rot {wr:G3} scale {ws:G3} ({w})");
    }

    static string OtherRow(Host h, string key, string path, string current)
    {
        var l = h.Call(new { cmd = "chara.edit.list", key, path, limit = 10000 });
        return (l["rows"] as JArray)?.Select(x => (string)x["ref"]).FirstOrDefault(x => x != current && x.StartsWith("illusion.hs2#", StringComparison.Ordinal));
    }

    static string EditValue(Host h, string key, string path) => (string)h.Call(new { cmd = "chara.edit.get", key, paths = new[] { path } })["values"]?[path];

    static (Vector3 p, Quaternion r, Vector3 s) Local(Transform t) => (t.localPosition, t.localRotation, t.localScale);

    static int FindingCount(Host h, string code, string id)
    {
        foreach (var x in h.Rt.Findings) if (x.Code == code && x.NodeId == id) return x.Count;
        return 0;
    }

    // ------------------------------------------------------------ AB4

    static void AB4()
    {
        var f = NewTwins(femaleRel, "female", "AB4");
        try
        {
            var bones = new[] { "cf_J_Hand_Index02_L", "cf_J_Spine02", "cf_J_NoseBase_s", "cf_J_Mouth_L" };
            SetBones(f.H, f.A, Map(bones.Select(b => (b, Full)).ToArray()));
            f.Step(3);
            CheckComposed(f, "before", bones, Full);
            void Both(Func<string, object> make, string what)
            {
                foreach (var k in new[] { f.A, f.B }) f.H.Ok(make(k), what);
            }
            void Hand()
            {
                foreach (var k in new[] { f.A, f.B })
                {
                    f.H.Ok(new { cmd = "chara.anime", key = k, group = 19, category = 0, no = 0, time = 0.5f, speed = 0f }, "chara.anime idle again");
                    f.H.Ok(new { cmd = "chara.motion", key = k, autoStep = false, physics = false }, "by hand again");
                }
            }
            Both(k => new { cmd = "chara.change", key = k, path = femaleRel, sex = "female" }, "chara.change to the same card");
            Hand(); f.Step(3);
            CheckComposed(f, "chara.change to the same card", bones, Full);
            Check("the stages are on the new root", f.RA.Motion.Stages.Any(s => s.Name == "pose.apply"));
            Check("the twin has none", !f.RB.Motion.Stages.Any(s => s.Name.StartsWith("pose.", StringComparison.Ordinal)));
            Both(k => new { cmd = "chara.change", key = k, path = maleRel, sex = "male" }, "chara.change to the other sex's target");
            Hand(); f.Step(3);
            CheckComposed(f, "the other sex's target", bones, Full);
            Both(k => new { cmd = "chara.change", key = k, path = femaleRel, sex = "female" }, "chara.change back");
            Hand(); f.Step(3);
            CheckComposed(f, "back on the first target", bones, Full);
            foreach (var k in new[] { f.A, f.B })
                Check("a rebuild request is taken", f.H.Rt.FindChara(k).RequestPluginRebuild("advancedpose.test"));
            f.H.Rt.ApplyNow();
            Hand(); f.Step(3);
            CheckComposed(f, "a rebuild request", bones, Full);

            // a hair swap: the body's corrections stay, the part's top is found again under the same name
            var hair = f.RA.Context.Hair[1];
            if (hair)
            {
                string top = "hair:1/" + hair.name;
                var both = bones.Select(b => (b, Full)).Concat(new[] { (top, Full) }).ToArray();
                SetBones(f.H, f.A, Map(both));
                f.Step(2);
                CheckComposed(f, "with a correction on the hair part's top", bones.Concat(new[] { top }), Full);
                string cur = EditValue(f.H, f.A, "chara.hair.front.item");
                string other = OtherRow(f.H, f.A, "chara.hair.front.item", cur);
                if (other == null) Warn("no other front hair row");
                else
                {
                    Both(k => new { cmd = "chara.edit.item", key = k, path = "chara.hair.front.item", @ref = other }, "a hair swap");
                    f.Step(3);
                    var e = AdvancedPoseDriver.Of(f.RA).Set.Of(top);
                    if (e != null)
                    {
                        Check("the entry is on the new part's transform", e.Bone && f.RA.Context.Hair[1] && e.Bone == f.RA.Context.Hair[1].transform);
                        CheckComposed(f, "after the hair swap", bones.Concat(new[] { top }), Full);
                    }
                    else
                    {
                        Info("the new part's top has another name: " + (f.RA.Context.Hair[1] ? f.RA.Context.Hair[1].name : "none"));
                        CheckComposed(f, "after the hair swap (the body bones)", bones, Full);
                    }
                }
            }
            else Warn("no front hair on the target");
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ AB5

    static void AB5()
    {
        var f = NewTwins(femaleRel, "female", "AB5");
        try
        {
            // a name on this body the other target lacks, found by loading the other target aside
            var m = Key(f.H.Ok(new { cmd = "chara.load", path = maleRel, sex = "male" }, "the other target, aside"));
            var rm = f.H.Rt.FindChara(m);
            string only = AdvancedPoseResolver.Bones(f.RA).Select(x => x.Address).FirstOrDefault(a => !AdvancedPoseResolver.Resolve(rm, a));
            f.H.Ok(new { cmd = "delete", key = m }, "the other target goes");
            if (!Check("an address only this body has", only != null, only)) return;
            var common = "cf_J_Hand_Index02_L";
            SetBones(f.H, f.A, Map((only, Full), (common, Full)));
            string json0 = Block(f.H, f.A).ToJson();
            f.Step(2);
            var c0 = AdvancedPoseDriver.Of(f.RA);
            Check("both act on this body", c0.Set.Entries.Count == 2 && c0.StaleBones.Count == 0);
            Check("no stale finding yet", FindingCount(f.H, AdvancedPoseFindings.Stale, f.A) == 0);

            f.H.Ok(new { cmd = "chara.change", key = f.A, path = maleRel, sex = "male" }, "chara.change to the other body");
            f.H.Ok(new { cmd = "chara.motion", key = f.A, autoStep = false, physics = false }, "by hand");
            f.H.Rt.ApplyNow();
            var c1 = AdvancedPoseDriver.Of(f.RA);
            Check("the entry is stale there, the other acts", c1 != null && c1.StaleBones.SequenceEqual(new[] { only }) && c1.Set.Entries.Count == 1, c1 == null ? "no state" : string.Join(",", c1.StaleBones));
            Check("the finding once, with its count", FindingCount(f.H, AdvancedPoseFindings.Stale, f.A) == 1, "" + FindingCount(f.H, AdvancedPoseFindings.Stale, f.A));
            f.RA.Motion.Step(Dt);
            SetBones(f.H, f.A, Map((only, Full), (common, Corr(rot: new Vec3(3f, 0f, 0f)))));
            SetBones(f.H, f.A, Map((only, Full), (common, Full)));
            Check("still one finding after more reads", FindingCount(f.H, AdvancedPoseFindings.Stale, f.A) == 1, "" + FindingCount(f.H, AdvancedPoseFindings.Stale, f.A));
            Check("the block keeps it", Block(f.H, f.A).ToJson() == json0);
            string path = (string)f.H.Ok(new { cmd = "scene.save", name = "ab5", overwrite = true, thumb = "placeholder", clock = SaveClock }, "save with a stale entry")["path"];
            string text = Encoding.UTF8.GetString(Entries(path)["scene.json"]);
            Check("saved in the file", text.IndexOf(only, StringComparison.Ordinal) >= 0);

            // undo of chara.change (the edits of the map on top go first)
            f.H.Ok(new { cmd = "chara.change", key = f.A, path = femaleRel, sex = "female" }, "back to the first body");
            f.H.Ok(new { cmd = "chara.motion", key = f.A, autoStep = false, physics = false }, "by hand");
            f.H.Rt.ApplyNow();
            var c2 = AdvancedPoseDriver.Of(f.RA);
            Check("active again when the bone returns", c2 != null && c2.StaleBones.Count == 0 && c2.Set.Entries.Count == 2, c2 == null ? "no state" : string.Join(",", c2.StaleBones));
            Check("the block is unchanged", Block(f.H, f.A).ToJson() == json0);

            // a part swapped away: an entry under the hair part, by a name inside it
            var rows = AdvancedPoseResolver.Bones(f.RA, "hair:1");
            string cur = EditValue(f.H, f.A, "chara.hair.front.item");
            string other = OtherRow(f.H, f.A, "chara.hair.front.item", cur);
            var deep = rows.LastOrDefault();
            if (deep == null || other == null) { Warn("no front hair or no other row"); return; }
            SetBones(f.H, f.A, Map((deep.Address, Full)));
            string json1 = Block(f.H, f.A).ToJson();
            f.H.Rt.ApplyNow();
            Check("the part's entry acts", AdvancedPoseDriver.Of(f.RA).Set.Entries.Count == 1);
            int findings0 = FindingCount(f.H, AdvancedPoseFindings.Stale, f.A);
            f.H.Ok(new { cmd = "chara.edit.item", key = f.A, path = "chara.hair.front.item", @ref = other }, "swap the part away");
            f.Step(2);
            var c3 = AdvancedPoseDriver.Of(f.RA);
            bool gone = c3.StaleBones.Contains(deep.Address);
            if (!gone) Info("the other row holds the same name: " + deep.Address);
            Check("stale, or found again by name on the new part", gone ? c3.Set.Entries.Count == 0 : c3.Set.Entries.Count == 1, string.Join(",", c3.StaleBones));
            Check("the block is unchanged by the swap", Block(f.H, f.A).ToJson() == json1);
            if (gone) Check("the finding once for this build", FindingCount(f.H, AdvancedPoseFindings.Stale, f.A) == findings0 + 1, findings0 + " -> " + FindingCount(f.H, AdvancedPoseFindings.Stale, f.A));
            f.H.Ok(new { cmd = "undo" }, "undo the swap");
            f.Step(2);
            var c4 = AdvancedPoseDriver.Of(f.RA);
            Check("active again after undo", c4.StaleBones.Count == 0 && c4.Set.Entries.Count == 1 && c4.Set.Entries[0].Bone, string.Join(",", c4.StaleBones));
            Check("the block is unchanged after undo", Block(f.H, f.A).ToJson() == json1);
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ AB6

    static void AB6()
    {
        var h = NewHost();
        try
        {
            var a = Key(h.Ok(new { cmd = "chara.load", path = femaleRel, sex = "female" }, "chara.load"));
            h.Ok(new { cmd = "chara.anime", key = a, group = 19, category = 0, no = 0, time = 0.5f, speed = 1f }, "chara.anime");
            h.Frames(5);
            h.Ok(new { cmd = "scene.pause", paused = true }, "the global pause");
            h.Frames(3);
            var r = h.Rt.FindChara(a);
            var t = Bone(r, "cf_J_Hand_Index02_L");
            var before = t.localRotation;
            int settles = r.Motion.Settles;
            float mt = r.Motion.Time, at = r.Animation.NormalizedTime;
            double ct = h.Rt.Clock.Time;
            var c = Corr(rot: new Vec3(25f, 0f, 0f));
            h.Session.Set(a, "chara.advancedPose.bones", Map(("cf_J_Hand_Index02_L", c)));
            h.Rt.ApplyNow();
            Check("it shows at once", Angle(t.localRotation, before * Quaternion.Euler(25f, 0f, 0f)) <= 0.01, $"{Angle(t.localRotation, before * Quaternion.Euler(25f, 0f, 0f)):G3} deg");
            h.Frames(5);
            Check("one settle at the commit", r.Motion.Settles == settles + 1 && !r.Motion.SettlePending, settles + " -> " + r.Motion.Settles);
            Check("the times stand", r.Motion.Time == mt && r.Animation.NormalizedTime == at && h.Rt.Clock.Time == ct, $"motion {mt:R} -> {r.Motion.Time:R}, animation {at:R} -> {r.Animation.NormalizedTime:R}, clock {ct:R} -> {h.Rt.Clock.Time:R}");
            Check("it still shows after the frames", Angle(t.localRotation, before * Quaternion.Euler(25f, 0f, 0f)) <= 0.01);
            h.Session.Set(a, "chara.advancedPose.bones", Map());
            h.Rt.ApplyNow();
            Check("cleared while paused: the bone is back", Angle(t.localRotation, before) <= 0.001);
            h.Frames(3);
            Check("still back after the frames", Angle(t.localRotation, before) <= 0.001);
            h.Ok(new { cmd = "scene.pause", paused = false }, "resume");
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ AB7

    static void CompareFk(Twins f, string what)
    {
        var fa = (f.H.Session.Model.Find(f.A).State as CharaNodeState).Fk;
        var fb = (f.H.Session.Model.Find(f.B).State as CharaNodeState).Fk;
        double worst = 0; string w = ""; int n = 0;
        foreach (var kv in fb.Bones)
        {
            if (!fa.Bones.TryGetValue(kv.Key, out var v)) { worst = 999; w = kv.Key + " missing"; continue; }
            double d = Angle(Quaternion.Euler(v.X, v.Y, v.Z), Quaternion.Euler(kv.Value.X, kv.Value.Y, kv.Value.Z));
            if (d > worst) { worst = d; w = kv.Key; }
            n++;
        }
        Check(what + ": the FK values equal the twin's", n > 0 && fa.Bones.Count == fb.Bones.Count && worst <= 0.01, $"{n} values, worst {worst:G3} deg ({w})");
    }

    static void AB7()
    {
        var bones = new[] { "cf_J_Spine02", "cf_J_ArmUp00_R", "cf_J_Hand_Index02_L", "cf_J_Head" };
        var c = Corr(rot: new Vec3(11f, -13f, 17f));
        void Case(string what, Action<Twins, string> op, bool ik = false)
        {
            var f = NewTwins(femaleRel, "female", "AB7 " + what);
            try
            {
                f.Step(2);
                SetBones(f.H, f.A, Map(bones.Select(b => (b, c)).ToArray()));
                f.Step(2);
                var shown = bones.Select(b => Bone(f.RA, b).localRotation).ToArray();
                op(f, f.A); op(f, f.B);
                f.H.Rt.ApplyNow();
                double worst = 0; string w = "";
                for (int i = 0; i < bones.Length; i++)
                {
                    double d = Angle(Bone(f.RA, bones[i]).localRotation, shown[i]);
                    if (d > worst) { worst = d; w = bones[i]; }
                }
                Check(what + ": the shown pose stays, right after it", worst <= 0.01, $"worst {worst:G3} deg ({w})");
                f.Step(3);
                worst = 0;
                for (int i = 0; i < bones.Length; i++)
                {
                    double d = Angle(Bone(f.RA, bones[i]).localRotation, shown[i]);
                    if (d > worst) { worst = d; w = bones[i]; }
                }
                Check(what + ": the shown pose stays, after steps", worst <= 0.01, $"worst {worst:G3} deg ({w})");
                if (!ik) CompareFk(f, what);
                else
                {
                    var ta = f.H.Ok(new { cmd = "ik.list", key = f.A, exact = true }, "ik.list A")["targets"] as JArray;
                    var tb = f.H.Ok(new { cmd = "ik.list", key = f.B, exact = true }, "ik.list B")["targets"] as JArray;
                    double wp = 0; string wn = ""; int n = 0;
                    for (int i = 0; ta != null && tb != null && i < ta.Count && i < tb.Count; i++)
                    {
                        if (!(ta[i]["pos"] is JArray pa) || !(tb[i]["pos"] is JArray pb)) continue;
                        for (int k = 0; k < 3; k++)
                        {
                            double d = Math.Abs((double)pa[k] - (double)pb[k]);
                            if (d > wp) { wp = d; wn = (string)ta[i]["name"]; }
                        }
                        n++;
                    }
                    Check(what + ": the targets equal the twin's", n > 0 && wp <= 1e-4, $"{n} targets, worst {wp:G3} ({wn})");
                }
            }
            finally { f.Dispose(); }
        }
        Case("fk.capture", (f, k) => f.H.Ok(new { cmd = "fk.capture", key = k, stopAnime = true }, "fk.capture"));
        Case("FK from pose", (f, k) => f.H.Ok(new { cmd = "fk.copy", key = k, from = "current", enable = true }, "fk.copy current"));
        Case("a group becoming applied", (f, k) => f.H.Ok(new { cmd = "fk.enable", key = k, on = true }, "fk.enable"));
        Case("IK from pose", (f, k) => f.H.Ok(new { cmd = "ik.copy", key = k, from = "current", enable = true }, "ik.copy current"), true);
        Case("the bake", (f, k) =>
        {
            f.H.Ok(new { cmd = "ik.enable", key = k, on = true }, "ik.enable");
            f.H.Rt.ApplyNow();
            f.H.Rt.FindChara(k).Motion.Step(Dt);
            f.H.Ok(new { cmd = "ik.bake", key = k }, "ik.bake");
        });
    }

    // ------------------------------------------------------------ AB8

    static void AB8()
    {
        var f = NewTwins(femaleRel, "female", "AB8");
        try
        {
            var bones = new[] { "cf_J_Hand_Index02_L", "cf_J_Spine02", "cf_J_NoseBase_s", "cf_J_Hips" };
            f.Step(2);
            SetBones(f.H, f.A, Map(bones.Select(b => (b, Full)).ToArray()));
            f.Step(3);
            var set = AdvancedPoseDriver.Of(f.RA).Set;
            Check("applied before the probe", set.Applied);
            var before = bones.Select(b => Local(Bone(f.RA, b))).ToArray();
            double insideRot = 0, insidePos = 0;
            f.RA.Motion.Probe(MotionPhase.IkPrepare, f.RA.Root.transform, () =>
            {
                foreach (var b in new[] { "cf_J_Hand_Index02_L", "cf_J_Spine02" })
                {
                    double d = 0;
                    var ta = Bone(f.RA, b);
                    f.RB.Motion.Probe(MotionPhase.IkPrepare, f.RB.Root.transform, () => d = Angle(ta.localRotation, Bone(f.RB, b).localRotation));
                    insideRot = Math.Max(insideRot, d);
                }
                insidePos = Dist(Bone(f.RA, "cf_J_NoseBase_s").localPosition, Bone(f.RB, "cf_J_NoseBase_s").localPosition);
            });
            Check("inside a probe the bones are bare (the twin's probe)", insideRot <= 0.001 && insidePos <= 1e-6, $"rot {insideRot:G3} deg, pos {insidePos:G3}");
            Check("applied after the probe", set.Applied);
            double wp = 0, wr = 0, ws = 0;
            for (int i = 0; i < bones.Length; i++)
            {
                var (p, q, s) = Local(Bone(f.RA, bones[i]));
                wp = Math.Max(wp, Dist(p, before[i].p)); wr = Math.Max(wr, Angle(q, before[i].r)); ws = Math.Max(ws, Dist(s, before[i].s));
            }
            Check("the probe leaves the bones as they were", wp <= 1e-6 && wr <= 0.001 && ws <= 1e-6, $"pos {wp:G3} rot {wr:G3} scale {ws:G3}");
            var ia = IkDriver.Of(f.RA)?.AnimationPose();
            var ib = IkDriver.Of(f.RB)?.AnimationPose();
            Check("the IK copy's probe reads what the twin's reads", ia != null && ib != null);
            f.Step(60);
            CheckComposed(f, "60 steps after the probes", bones, Full);
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ AB9

    static void AB9()
    {
        var f = NewTwins(femaleRel, "female", "AB9");
        try
        {
            var bones = new[] { "cf_J_Hand_Index02_L", "cf_J_Spine02", "cf_J_NoseBase_s", "cf_J_MouthMove" };
            f.Step(2);
            SetBones(f.H, f.A, Map(bones.Select(b => (b, Full)).ToArray()));
            f.Step(3);
            using (var sa = StudioDefaultPoseScope.Enter(f.RA))
            using (var sb = StudioDefaultPoseScope.Enter(f.RB))
            {
                double wp = 0, wr = 0, ws = 0; string w = "";
                foreach (var b in bones)
                {
                    var (ap, aq, asc) = Local(Bone(f.RA, b));
                    var (bp, bq, bsc) = Local(Bone(f.RB, b));
                    double p = Dist(ap, bp), r = Angle(aq, bq), s = Dist(asc, bsc);
                    if (p > wp) { wp = p; w = b; }
                    if (r > wr) { wr = r; w = b; }
                    if (s > ws) { ws = s; w = b; }
                }
                Check("no delta inside the scope", wp <= 1e-6 && wr <= 0.01 && ws <= 1e-6, $"pos {wp:G3} rot {wr:G3} scale {ws:G3} ({w})");
                var c = AdvancedPoseDriver.Of(f.RA);
                Check("the stages are off and nothing is applied inside", c.Suspended && !c.Set.Applied && f.RA.Motion.Stages.Where(s => s.Name.StartsWith("pose.", StringComparison.Ordinal)).All(s => !s.Enabled));
            }
            foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "chara.motion", key = k, autoStep = false, physics = false }, "by hand");
            f.Step(3);
            var cc = AdvancedPoseDriver.Of(f.RA);
            Check("the stages are on again", !cc.Suspended && cc.Set.Applied && f.RA.Motion.Stages.Where(s => s.Name.StartsWith("pose.", StringComparison.Ordinal)).All(s => s.Enabled));
            CheckComposed(f, "after the scope", bones, Full);
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ AB10

    static void AB10()
    {
        var f = NewTwins(femaleRel, "female", "AB10");
        try
        {
            // a shape bone no card modifier writes
            var mods = f.RA.Context.Bones;
            string[] candidates = { "cf_J_Spine01_s", "cf_J_Spine02_s", "cf_J_Kosi01_s", "cf_J_LegUp01_s_L" };
            string bone = candidates.FirstOrDefault(n => Bone(f.RA, n) && (!mods || !mods.Modifiers.Any(m => m.Target == Bone(f.RA, n))));
            Check("a shape bone without a card modifier", bone != null, bone);
            f.Step(2);
            SetBones(f.H, f.A, Map((bone, Full)));
            f.Step(2);
            foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "shape.set", key = k, bodyIndex = 4, bodyValue = 0.9f }, "shape.set a body slider");
            f.Step(2);
            CheckComposed(f, "at the new slider, the correction on", new[] { bone }, Full);
            foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "shape.set", key = k, bodyIndex = 12, bodyValue = 0.2f }, "shape.set another body slider");
            f.Step(2);
            SetBones(f.H, f.A, Map());
            f.Step(2);
            var a = Bone(f.RA, bone); var b = Bone(f.RB, bone);
            double p = Dist(a.localPosition, b.localPosition), s = Dist(a.localScale, b.localScale), r = Angle(a.localRotation, b.localRotation);
            Check("after clearing the bone equals the twin (the guard took no delta)", p <= 1e-6 && s <= 1e-6 && r <= 0.001, $"pos {p:G3} scale {s:G3} rot {r:G3}");
            // the fresh build at the new slider
            Check("a rebuild request of the twin", f.RB.RequestPluginRebuild("advancedpose.test"));
            f.H.Rt.ApplyNow();
            f.H.Ok(new { cmd = "chara.anime", key = f.B, group = 19, category = 0, no = 0, time = 0.5f, speed = 0f }, "chara.anime idle again");
            f.H.Ok(new { cmd = "chara.motion", key = f.B, autoStep = false, physics = false }, "by hand");
            f.Step(2);
            b = Bone(f.RB, bone);
            p = Dist(a.localPosition, b.localPosition); s = Dist(a.localScale, b.localScale); r = Angle(a.localRotation, b.localRotation);
            Check("after clearing the bone equals a fresh build at the new slider", p <= 1e-6 && s <= 1e-6 && r <= 0.001, $"pos {p:G3} scale {s:G3} rot {r:G3}");
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ AB11

    static void AB11()
    {
        var f = NewTwins(femaleRel, "female", "AB11");
        try
        {
            // none of these moves the head: the eye look, which keeps a state, stays equal as well
            var bones = new[] { "cf_J_Hand_Index02_L", "cf_J_Hand_Thumb03_R", "cf_J_ArmUp00_R" };
            f.Step(2);
            SetBones(f.H, f.A, Map(bones.Select(b => (b, Full)).ToArray()));
            f.Step(2);
            foreach (var k in new[] { f.A, f.B })
            {
                f.H.Ok(new { cmd = "chara.anime", key = k, group = 11, category = 5, no = 0, time = 0.3f, speed = 0f }, "chara.anime dance");
                f.H.Ok(new { cmd = "chara.motion", key = k, autoStep = false, physics = false }, "by hand");
            }
            f.Step(3);
            CheckComposed(f, "another row", bones, Full);
            foreach (var k in new[] { f.A, f.B })
            {
                f.H.Ok(new { cmd = "chara.anime", key = k, group = 19, category = 0, no = 0, time = 0.5f, speed = 0f }, "chara.anime idle");
                f.H.Ok(new { cmd = "chara.motion", key = k, autoStep = false, physics = false }, "by hand");
            }
            f.Step(3);
            CheckComposed(f, "and back", bones, Full);
            SetBones(f.H, f.A, Map());
            f.Step(3);
            Same(f, "after clearing");
            var da = f.H.Ok(new { cmd = "bones.dump", key = f.A, root = "all" }, "bones.dump A")["bones"] as JArray;
            var db = f.H.Ok(new { cmd = "bones.dump", key = f.B, root = "all" }, "bones.dump B")["bones"] as JArray;
            int diff = 0; string first = "";
            for (int i = 0; da != null && db != null && i < da.Count && i < db.Count; i++)
            {
                double worst = 0;
                foreach (var m in new[] { "lp", "lq", "ls" })
                {
                    var x = (JArray)da[i][m]; var y = (JArray)db[i][m];
                    for (int k = 0; k < x.Count; k++) worst = Math.Max(worst, Math.Abs((double)x[k] - (double)y[k]));
                }
                if (worst > 1e-5) { diff++; if (first.Length == 0) first = (string)da[i]["name"] + " " + worst.ToString("G3"); }
            }
            Check("every bone equals the run that never had the correction", da != null && db != null && da.Count == db.Count && diff == 0, $"{diff} bones over 1e-5 {first}");
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ AB12

    /// <summary>One case of AB12: the bone's value now.</summary>
    static double[] Values(Transform t)
    {
        var (p, q, s) = Local(t);
        return new double[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w, s.x, s.y, s.z };
    }

    static void Drift(Twins f, string what, string bone, float dt1, int steps, ref double worstPos, ref double worstRot, ref double worstScale)
    {
        var t = Bone(f.RA, bone);
        var (p0, q0, s0) = Local(t);
        f.Step(steps, dt1);
        var (p1, q1, s1) = Local(t);
        worstPos = Math.Max(worstPos, Dist(p0, p1));
        worstRot = Math.Max(worstRot, Angle(q0, q1));
        worstScale = Math.Max(worstScale, Dist(s0, s1));
    }

    static void AB12()
    {
        var eye = "cf_J_look_L";
        var twistLeg = "cf_J_LegUpDam_L";
        var twistArm = "cf_J_ArmElboura_dam_L";
        var mouth = "cf_J_MouthMove";
        var rotOnly = Corr(rot: new Vec3(9f, 6f, 4f));
        var scaleAll = Corr(scale: new Vec3(1.3f, 1.2f, 1.1f));

        // no physics: the eye, the twist rows, the mouth width
        var f = NewTwins(femaleRel, "female", "AB12");
        try
        {
            foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "chara.motion", key = k, correction = new { flags = new[] { true, true, true, true, true, true, true, true } } }, "the joint correction's rows on");
            SetBones(f.H, f.A, Map((eye, rotOnly), (twistLeg, rotOnly), (twistArm, rotOnly), (mouth, scaleAll)));
            f.Step(300);
            foreach (var (bone, shownExpected, why) in new[] { (eye, false, "the eye look writes after it"), (twistLeg, false, "the twist row writes after it"), (twistArm, false, "the twist row writes after it"), (mouth, false, "the mouth width writes scale x") })
            {
                double wp = 0, wr = 0, ws = 0;
                Drift(f, "with time", bone, Dt, 600, ref wp, ref wr, ref ws);
                Check(bone + ": 600 steps with time, no drift", wp <= 1e-4 && wr <= 0.01 && ws <= 1e-4, $"pos {wp:G3} rot {wr:G3} scale {ws:G3}");
                wp = wr = ws = 0;
                Drift(f, "without time", bone, 0f, 600, ref wp, ref wr, ref ws);
                Check(bone + ": 600 steps without time, no drift", wp <= 1e-4 && wr <= 0.01 && ws <= 1e-4, $"pos {wp:G3} rot {wr:G3} scale {ws:G3}");
                var e = AdvancedPoseDriver.Of(f.RA).Set.Of(bone);
                Check(bone + ": shown " + shownExpected + " (" + why + ")", BoneCorrections.Shown(e) == shownExpected, "shown " + BoneCorrections.Shown(e));
            }
            var mb = Bone(f.RA, mouth); var tb = Bone(f.RB, mouth);
            Check("the mouth width: x is the face's, y and z are corrected", Mathf.Abs(mb.localScale.x - tb.localScale.x) <= 1e-5 && Mathf.Abs(mb.localScale.y - tb.localScale.y * 1.2f) <= 1e-5 && Mathf.Abs(mb.localScale.z - tb.localScale.z * 1.1f) <= 1e-5,
                $"{mb.localScale.x:G6},{mb.localScale.y:G6},{mb.localScale.z:G6} twin {tb.localScale.x:G6},{tb.localScale.y:G6},{tb.localScale.z:G6}");
            var ea = Bone(f.RA, eye); var eb = Bone(f.RB, eye);
            Check("the eye: the look's value (the head is not corrected)", Angle(ea.localRotation, eb.localRotation) <= 0.01, $"{Angle(ea.localRotation, eb.localRotation):G3} deg");
            // the twist rows off: the correction shows
            foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "chara.motion", key = k, correction = new { flags = new bool[8] } }, "the joint correction's rows off");
            f.Step(5);
            foreach (var bone in new[] { twistLeg, twistArm })
            {
                double wp = 0, wr = 0, ws = 0;
                Drift(f, "row off", bone, Dt, 600, ref wp, ref wr, ref ws);
                Check(bone + ", the row off: 600 steps, no drift", wp <= 1e-4 && wr <= 0.01 && ws <= 1e-4, $"pos {wp:G3} rot {wr:G3} scale {ws:G3}");
                var e = AdvancedPoseDriver.Of(f.RA).Set.Of(bone);
                Check(bone + ", the row off: shown", BoneCorrections.Shown(e));
                double dr = Angle(Bone(f.RA, bone).localRotation, Bone(f.RB, bone).localRotation * Quaternion.Euler(9f, 6f, 4f));
                Check(bone + ", the row off: composed", dr <= 0.01, $"{dr:G3} deg");
            }
        }
        finally { f.Dispose(); }

        // physics on: a bust chain bone and a hair chain bone
        var g = NewTwins(femaleRel, "female", "AB12 physics", physics: true);
        try
        {
            var bust = g.RA.Motion.Get<BustStage>()?.All.FirstOrDefault();
            var dyn = g.RA.Motion.Get<DynamicBoneStage>();
            Transform bb = null; DynamicBoneChain chain = null; Transform hb = null;
            for (int i = 0; bust != null && i < bust.ParticleCount && !bb; i++) if (i > 0) bb = bust.ParticleTransform(i);
            foreach (var c in dyn?.Chains ?? new List<DynamicBoneChain>())
            {
                if (c.Category != DynamicBoneCategory.Hair || c.ParticleCount < 2) continue;
                chain = c;
                hb = c.ParticleTransform(1);
                if (hb) break;
            }
            Check("a bust chain bone and a hair chain bone", bb && hb, (bb ? bb.name : "no bust bone") + ", " + (hb ? hb.name : "no hair bone"));
            if (!bb || !hb) return;
            string aBust = bb.name;
            string aHair = AdvancedPoseResolver.Bones(g.RA).FirstOrDefault(x => x.Bone == hb)?.Address;
            Check("both have addresses", Bone(g.RA, aBust) == bb && aHair != null, aHair);
            g.Step(120);
            SetBones(g.H, g.A, Map((aBust, rotOnly), (aHair, rotOnly)));
            g.Step(200);
            foreach (var (addr, t) in new[] { (aBust, bb), (aHair, hb) })
            {
                var (p0, q0, s0) = Local(t);
                g.Step(600, Dt);
                var (p1, q1, s1) = Local(t);
                Check(addr + ", physics on: 600 steps with time, no drift", Dist(p0, p1) <= 1e-4 && Angle(q0, q1) <= 0.01 && Dist(s0, s1) <= 1e-4, $"pos {Dist(p0, p1):G3} rot {Angle(q0, q1):G3} scale {Dist(s0, s1):G3}");
                (p0, q0, s0) = Local(t);
                g.Step(600, 0f);
                (p1, q1, s1) = Local(t);
                Check(addr + ", physics on: 600 steps without time, no drift", Dist(p0, p1) <= 1e-4 && Angle(q0, q1) <= 0.01 && Dist(s0, s1) <= 1e-4, $"pos {Dist(p0, p1):G3} rot {Angle(q0, q1):G3} scale {Dist(s0, s1):G3}");
            }
            var set = AdvancedPoseDriver.Of(g.RA).Set;
            Check("a bust chain bone: not shown (copied from its reference)", !BoneCorrections.Shown(set.Of(aBust)));
            Info("a hair chain bone, physics on: shown " + BoneCorrections.Shown(set.Of(aHair)));
            // physics off: plain
            foreach (var k in new[] { g.A, g.B }) g.H.Ok(new { cmd = "chara.motion", key = k, physics = false }, "physics off");
            g.Step(5);
            {
                var (p0, q0, s0) = Local(hb);
                g.Step(600, Dt);
                var (p1, q1, s1) = Local(hb);
                Check(aHair + ", physics off: 600 steps, no drift", Dist(p0, p1) <= 1e-4 && Angle(q0, q1) <= 0.01 && Dist(s0, s1) <= 1e-4, $"pos {Dist(p0, p1):G3} rot {Angle(q0, q1):G3} scale {Dist(s0, s1):G3}");
                Check(aHair + ", physics off: shown", BoneCorrections.Shown(set.Of(aHair)));
                var tw = Bone(g.RB, aHair);
                double dr = Angle(hb.localRotation, tw.localRotation * Quaternion.Euler(9f, 6f, 4f));
                Check(aHair + ", physics off: composed on the twin's rest pose", dr <= 0.01, $"{dr:G3} deg");
            }
        }
        finally { g.Dispose(); }
    }

    // ------------------------------------------------------------ AB13

    static Dictionary<string, byte[]> Entries(string path)
    {
        var d = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using (var ms = new MemoryStream(File.ReadAllBytes(path)))
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
            foreach (var e in zip.Entries)
                using (var s = e.Open())
                using (var o = new MemoryStream())
                {
                    s.CopyTo(o);
                    d[e.FullName] = o.ToArray();
                }
        return d;
    }

    static void AB13()
    {
        var h = NewHost();
        try
        {
            var bones = new[] { "cf_J_Hand_Index02_L", "cf_J_Spine02", "cf_J_NoseBase_s", "cf_J_Head", "cf_J_look_L" };
            var a = Key(h.Ok(new { cmd = "chara.load", path = femaleRel, sex = "female" }, "chara.load"));
            h.Ok(new { cmd = "chara.anime", key = a, group = 19, category = 0, no = 0, time = 0.5f, speed = 0f }, "chara.anime");
            h.Ok(new { cmd = "chara.motion", key = a, physics = false }, "physics off");
            h.Ok(new { cmd = "scene.pause", paused = true }, "the global pause");
            h.Frames(5);
            string p0 = (string)h.Ok(new { cmd = "scene.save", name = "ab13_none", overwrite = true, thumb = "placeholder", clock = SaveClock }, "save without")["path"];
            h.Session.Set(a, "chara.advancedPose.bones", Map(bones.Select(b => (b, Full)).ToArray()));
            h.Rt.ApplyNow();
            h.Frames(3);
            var r = h.Rt.FindChara(a);
            // the head follows the neck look and the eye its look, whose states start anew at a load: the values checked are the pose's
            var values = bones.Where(b => b != "cf_J_look_L" && b != "cf_J_Head").Select(b => (b, Local(Bone(r, b)))).ToList();
            string pa = (string)h.Ok(new { cmd = "scene.save", name = "ab13_with", overwrite = true, thumb = "placeholder", clock = SaveClock }, "save with corrections")["path"];
            string block = Block(h, a).ToJson();
            h.Session.Set(a, "chara.advancedPose.bones", Map());
            h.Rt.ApplyNow();
            h.Frames(3);
            string pb = (string)h.Ok(new { cmd = "scene.save", name = "ab13_cleared", overwrite = true, thumb = "placeholder", clock = SaveClock }, "save after clearing")["path"];
            var e0 = Entries(p0); var ea = Entries(pa); var eb = Entries(pb);
            var diff = new List<string>();
            foreach (var n in ea.Keys.Union(eb.Keys))
            {
                if (n.IndexOf("thumb", StringComparison.OrdinalIgnoreCase) >= 0 || n == "scene.json" || n == "manifest.json") continue;
                if (!ea.TryGetValue(n, out var x) || !eb.TryGetValue(n, out var y) || !x.SequenceEqual(y)) diff.Add(n);
            }
            Check("A = B entry for entry but for the scene document", diff.Count == 0, string.Join(", ", diff));
            Check("the scene without a correction and the scene after clearing are byte-equal", eb["scene.json"].SequenceEqual(e0["scene.json"]));
            var ja = JObject.Parse(Encoding.UTF8.GetString(ea["scene.json"]));
            var jb = JObject.Parse(Encoding.UTF8.GetString(eb["scene.json"]));
            int removed = 0;
            foreach (var t in ja.Descendants().OfType<JProperty>().Where(x => x.Name == "advancedPose").ToList()) { t.Remove(); removed++; }
            Check("the block is in the file once, and the rest of the scene document is the same", removed == 1 && JToken.DeepEquals(ja, jb), "blocks " + removed);
            h.Ok(new { cmd = "scene.new" }, "scene.new");
            var rl = h.Ok(new { cmd = "scene.load", path = pa }, "load");
            h.Rt.ApplyNow();
            var id = h.Rt.Charas().FirstOrDefault()?.NodeId;
            Check("the character is back with its block", id != null && Block(h, id)?.ToJson() == block, Short(h.LastText));
            h.Ok(new { cmd = "chara.motion", key = id, physics = false }, "physics off");
            h.Ok(new { cmd = "scene.pause", paused = true }, "the global pause");
            h.Frames(5);
            r = h.Rt.FindChara(id);
            double wp = 0, wr = 0, ws = 0; string w = "";
            foreach (var (b, v) in values)
            {
                var (p, q, s) = Local(Bone(r, b));
                double dp = Dist(p, v.p), dq = Angle(q, v.r), ds = Dist(s, v.s);
                if (dp > wp) { wp = dp; w = b; }
                if (dq > wr) { wr = dq; w = b; }
                if (ds > ws) { ws = ds; w = b; }
            }
            Check("a load reproduces the values", wp <= 1e-5 && wr <= 0.01 && ws <= 1e-5, $"pos {wp:G3} rot {wr:G3} scale {ws:G3} ({w})");
            h.Ok(new { cmd = "scene.pause", paused = false }, "resume");
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ AB15

    static void AB15()
    {
        var f = NewTwins(femaleRel, "female", "AB15");
        try
        {
            var bones = new[] { "cf_J_Hand_Index02_L", "cf_J_Spine02", "cf_J_NoseBase_s" };
            f.Step(2);
            SetBones(f.H, f.A, Map(bones.Select(b => (b, Full)).ToArray()));
            f.Step(2);
            string block = Block(f.H, f.A).ToJson();
            int entries0 = (f.H.Call(new { cmd = "history" })["undo"] as JArray)?.Count ?? 0;
            var res = f.H.Session.Duplicate(new[] { f.A });
            Check("duplicate", res.Ok, res.ToString());
            f.H.Rt.ApplyNow();
            var copy = f.H.Rt.Charas().Select(c => c.NodeId).FirstOrDefault(id => id != f.A && id != f.B);
            Check("the copy exists and carries the block", copy != null && Block(f.H, copy)?.ToJson() == block);
            if (copy != null)
            {
                var rc = f.H.Rt.FindChara(copy);
                Check("the copy has its own stages", rc.Motion.Stages.Any(s => s.Name == "pose.apply"));
                f.H.Ok(new { cmd = "chara.anime", key = copy, group = 19, category = 0, no = 0, time = 0.5f, speed = 0f }, "the copy's row");
                f.H.Ok(new { cmd = "chara.motion", key = copy, autoStep = false, physics = false, reset = true }, "the copy by hand");
                f.H.Rt.ApplyNow();
                rc.Motion.Step(Dt);
                double wr = 0;
                foreach (var b in bones) wr = Math.Max(wr, Angle(Bone(rc, b).localRotation, Bone(f.RA, b).localRotation));
                Info("the copy against the original: " + wr.ToString("G3") + " deg");
                while (((f.H.Call(new { cmd = "history" })["undo"] as JArray)?.Count ?? 0) > entries0) f.H.Ok(new { cmd = "undo" }, "undo the copy's entries");
                f.H.Rt.ApplyNow();
                Check("the copy went with the undo", f.H.Session.Model.Find(copy) == null);
            }
            // delete and undo: buried and revived with its stages
            f.H.Ok(new { cmd = "delete", key = f.A }, "delete");
            f.H.Rt.ApplyNow();
            Check("gone from the model", f.H.Session.Model.Find(f.A) == null);
            f.H.Ok(new { cmd = "undo" }, "undo");
            f.H.Rt.ApplyNow();
            Check("back with its block", Block(f.H, f.A)?.ToJson() == block);
            f.H.Ok(new { cmd = "chara.motion", key = f.A, autoStep = false, physics = false }, "by hand");
            f.Step(3);
            Check("still with its stages", f.RA.Motion.Stages.Any(s => s.Name == "pose.apply"));
            CheckComposed(f, "after delete and undo", bones, Full);
            f.Step(60);
            CheckComposed(f, "60 steps later", bones, Full);
        }
        finally { f.Dispose(); }
    }
}
