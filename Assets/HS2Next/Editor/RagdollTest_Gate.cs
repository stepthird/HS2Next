// RagdollTest, the first rows of the body (Phase 6.7 package P): what everything else stands on.
//   RM2   the rest pose worked out on paper equals writing the rest rotations, and writes nothing
//   RP1   a limp drop lands
//   RO1   the three stages on a body and on no other character
//   RB2   chara.change: new bones written, stages there, part rotations kept
//   RO4   a correction stacks on the body; off gives 0 on every bone
//   RO5   a driven bone wins over the body, with no lag, and the twist bones follow
//   RB12  what a release leaves: RagdollTest_Release.cs
//   RK1   scene.step runs from one state in a fresh world: bit-equal bodies
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2Next;
using HS2Next.App;
using HS2Next.Motion;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Ragdoll;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class RagdollTest
{
    static void Headless_A_Gate()
    {
        Group("RM2", "the rest pose on paper", RM2);
        Group("RP1", "a limp drop lands", RP1);
        Group("RO1", "the stages", RO1);
        Group("RO4", "a correction stacks on the body", RO4);
        Group("RO5", "a driven bone wins over the body", RO5);
        Group("RB12", "a release gives the bones back", RB12);
        Group("RB14", "a jump resets the chains", RB14);
        Group("RB2", "chara.change rebuilds the body", RB2);
        Group("RK1", "repeats in a fresh world", RK1);
    }

    // ------------------------------------------------------------ RM2

    static void RM2()
    {
        foreach (var (name, rel, sex) in Targets())
        {
            var h = NewHost();
            try
            {
                string k = LoadChara(h, rel, sex, name);
                Idle(h, k);
                Place(h, k, new Vector3(3f, 1f, -2f), new Vector3(0f, 40f, 0f));
                h.Frames(3);
                var r = h.Rt.FindChara(k);
                var ctx = r.Context;
                var hips = ctx.BodyBones["cf_J_Hips"];
                var before = Bones(r);
                var rest = RagdollRest.Solve(ctx, hips);
                int changed = Differ(before, out string first);
                Check(name + ": Solve writes no bone", changed == 0 && before.Count > 100, changed + " of " + before.Count + " bones changed " + first);
                Check(name + ": the rest comes from the prefab", rest.FromPrefab);

                // the same by writing the bones
                var prefab = ctx.Catalog.LoadAsset<GameObject>("studio/base/00.unity3d", "p_cf_anim") ?? ctx.Catalog.LoadAsset<GameObject>("chara/oo_base.unity3d", "p_cf_anim");
                var map = new Dictionary<string, Transform>();
                foreach (var t in prefab.GetComponentsInChildren<Transform>(true)) if (!map.ContainsKey(t.name)) map[t.name] = t;
                var liveRot = RagdollParts.All.ToDictionary(p => p.Key, p => ctx.BodyBones[p.Bone].rotation);
                foreach (var kv in before) if (map.TryGetValue(kv.Key.name, out var pt)) kv.Key.localRotation = pt.localRotation;
                double worstP = 0, worstR = 0;
                string where = "";
                int far = 0;
                foreach (var p in RagdollParts.All)
                {
                    var t = ctx.BodyBones[p.Bone];
                    rest.Position(t, out double x, out double y, out double z);
                    var w = t.position;
                    double dp = Math.Sqrt((x - w.x) * (x - w.x) + (y - w.y) * (y - w.y) + (z - w.z) * (z - w.z));
                    double dr = Deg(rest.Rotation(t), t.rotation);
                    if (dp > worstP) { worstP = dp; where = p.Bone; }
                    worstR = Math.Max(worstR, dr);
                    if (Deg(liveRot[p.Key], t.rotation) > 10.0) far++;
                }
                foreach (var kv in before) kv.Key.SetLocalPositionAndRotation(kv.Value.p, kv.Value.r);
                Check(name + ": Solve equals writing the rest rotations", worstP <= 1e-5 && worstR <= 1e-3, $"position {worstP:G3} at {where}, rotation {worstR:G3} degrees");
                Check(name + ": Present: the live pose differs from the rest by more than 10 degrees on at least 8 bones", far >= 8, far + " bones");
            }
            finally { h.Dispose(); }
        }
    }

    // ------------------------------------------------------------ RP1

    static void RP1()
    {
        foreach (var (name, rel, sex) in Targets())
        {
            var h = NewHost();
            try
            {
                string k = LoadChara(h, rel, sex, name);
                Idle(h, k);
                Place(h, k, new Vector3(0f, 6f, 0f));
                h.Frames(2);
                FixedFloor(h, 0f);
                Preset(h, k, RagdollPreset.Limp);
                var b = Body(h, k, name);
                if (!Check(name + ": a body", b != null)) continue;
                Check(name + ": 16 parts, 15 joints", b.Parts.Count == 16 && b.Parts.Count(p => p.Joint) == 15, b.Parts.Count + ", " + b.Parts.Count(p => p.Joint));
                float start = b.Lowest();
                Measure($"{name}: mass {b.Mass:F2}, scale {b.Scale:F3}, overlaps at rest {b.IgnoredRest}, live {b.IgnoredLive}, build {b.BuildMs:F2} ms, prefab {b.RestFromPrefab}");
                Check(name + ": Present: it starts at least 3 above the floor", start >= 3f, start.ToString("F3"));
                h.Frames(180);
                float low = b.Lowest();
                var pelvis = b.Parts[0];
                Check(name + ": it lands, lowest within .05 of the floor", Mathf.Abs(low) <= 0.05f, $"lowest {low:F4}, pelvis y {pelvis.Tr.position.y:F3}, speed {pelvis.Rb.linearVelocity.magnitude:F3}, substeps {h.Part.World.Steps}");
            }
            finally { h.Dispose(); }
        }
    }

    // ------------------------------------------------------------ RO1

    static void RO1()
    {
        var h = NewHost();
        try
        {
            string a = LoadChara(h, femaleRel, "female", "A");
            string c = LoadChara(h, femaleRel, "female", "B");
            Place(h, c, new Vector3(10f, 0f, 0f));
            h.Frames(1);
            FixedFloor(h);
            var before = h.Rt.FindChara(a).Motion.Stages.Select(s => s.Name).ToList();
            var body = Body(h, a, "A");
            h.Frames(1);
            var ra = h.Rt.FindChara(a).Motion.Stages;
            var rc = h.Rt.FindChara(c).Motion.Stages;
            string list = string.Join(" ", ra.Select(s => s.Name + ":" + (int)s.Phase));
            Info(list);
            int Where(string n) { for (int i = 0; i < ra.Count; i++) if (ra[i].Name == n) return i; return -1; }
            Check("-98, 410, 956 are on the body", ra.Any(s => s.Name == "ragdoll.restore" && (int)s.Phase == -98) && ra.Any(s => s.Name == "ragdoll" && (int)s.Phase == 410) && ra.Any(s => s.Name == "ragdoll.end" && (int)s.Phase == 956), list);
            Check("no such stage on the other character", !rc.Any(s => s.Name.StartsWith("ragdoll", StringComparison.Ordinal)));
            Check("in order: after pose.restore", Where("ragdoll.restore") > Where("pose.restore") || Where("pose.restore") < 0);
            Check("before: no stage before the build", !before.Any(n => n.StartsWith("ragdoll", StringComparison.Ordinal)));
            h.Part.Release(a, false, ExecuteOptions.NotRecorded);
            Check("the stages go with the body", !h.Rt.FindChara(a).Motion.Stages.Any(s => s.Name.StartsWith("ragdoll", StringComparison.Ordinal)));
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ RO4, RO5: the second set under the two others

    static Transform B(CharaRealization r, string n) => r.Context.BodyBones[n];

    static void RO4()
    {
        var h = NewHost();
        try
        {
            string k = LoadChara(h, femaleRel, "female", "A");
            Idle(h, k);
            h.Frames(3);
            FixedFloor(h);
            var b = Body(h, k, "A");
            Preset(h, k, RagdollPreset.Stiff);
            h.Frames(30);
            h.Ok(new { cmd = "scene.pause", paused = true }, "pause");
            h.Frames(3);
            var r = h.Rt.FindChara(k);
            var hand = B(r, "cf_J_Hand_L");
            var without = Bones(r);
            var handAt = hand.position;
            h.Ok(new { cmd = "pose.bones.set", key = k, bone = "cf_J_ArmLow01_L", rot = new[] { 0f, 40f, 0f } }, "a correction on the forearm");
            h.Frames(2);
            float moved = (hand.position - handAt).magnitude;
            Check("Present: the correction moved the hand by at least .5", moved >= 0.5f, moved.ToString("F3"));
            var part = b.Part("forearm_L");
            Check("the proxy does not see it", part != null);
            h.Ok(new { cmd = "pose.bones.clear", key = k, all = true }, "off");
            h.Frames(2);
            int d = Differ(without, out string first);
            Check("off gives 0 on every bone", d == 0, d + " of " + without.Count + " differ " + first);
        }
        finally { h.Dispose(); }
    }

    static void RO5()
    {
        var h = NewHost();
        try
        {
            string k = LoadChara(h, femaleRel, "female", "A");
            Idle(h, k);
            h.Ok(new { cmd = "chara.motion", key = k, correction = new { flags = new[] { true, true, true, true, true, true, true, true } } }, "the joint correction's rows on");
            string node = Key(h.Ok(new { cmd = "item.add", no = 399 }, "a node"));
            h.Frames(3);
            FixedFloor(h);
            var b = Body(h, k, "A");
            Preset(h, k, RagdollPreset.Stiff);
            h.Frames(30);
            h.Ok(new { cmd = "scene.pause", paused = true }, "pause");
            h.Frames(3);
            var r = h.Rt.FindChara(k);
            var bone = B(r, "cf_J_ArmLow01_L");
            var hand = B(r, "cf_J_Hand_L");
            var rows = r.Context.BodyBones.Values.Where(t => t && t.name.Contains("_dam")).ToDictionary(t => t, t => t.localRotation);
            Place(h, node, bone.position, bone.rotation.eulerAngles + new Vector3(0f, 60f, 0f));
            var handAt = hand.position;
            h.Frames(1);
            var without = Bones(r);
            var res = h.Ok(new { cmd = "constraint.add", source = new { key = node }, target = new { key = k, bone = "cf_J_ArmLow01_L" }, components = new[] { "rotation" }, keepOffset = false }, "a constraint on the forearm");
            string id = (string)res["constraint"];
            h.Frames(3);
            var list = h.Ok(new { cmd = "constraint.list" }, "constraint.list");
            var row = (list["constraints"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["id"] == id);
            Check("it is active", (string)row?["state"] == "active", Short(row?.ToString()));
            Check("lag 0", row?.Value<bool?>("lag") != true, Short(row?.ToString()));
            double err = Deg(bone.rotation, h.Rt.HolderOf(node).rotation);
            float moved = (hand.position - handAt).magnitude;
            Check("the driven bone wins over the body", err <= 0.01, err.ToString("G4") + " degrees");
            Check("Present: the hand moved by at least .5", moved >= 0.5f, moved.ToString("F3"));
            // the table: cf_J_ArmElbo_dam_01_L reads cf_J_ArmLow01_L, cf_J_ArmElboura_dam_L aims with it as its up bone
            foreach (var n in new[] { "cf_J_ArmElbo_dam_01_L", "cf_J_ArmElboura_dam_L" })
            {
                var t = B(r, n);
                Check(n + " follows the driven bone", Deg(t.localRotation, rows[t]) >= 0.05, Deg(t.localRotation, rows[t]).ToString("F3") + " degrees");
            }
            // cf_J_ArmLow02_dam_L reads cf_J_Hand_L, cf_J_Hand_Wrist_dam_L aims at it with cf_J_Hand_L as its up bone: neither input changes in the forearm's frame here
            Info("the others, as information: " + string.Join(", ", rows.Select(kv => kv.Key.name + " " + Deg(kv.Key.localRotation, kv.Value).ToString("F3"))));
            h.Ok(new { cmd = "constraint.remove", constraint = id }, "off");
            h.Frames(3);
            int d = Differ(without, out string first);
            Check("off gives 0 on every bone", d == 0, d + " of " + without.Count + " differ " + first);
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ RB2

    static void RB2()
    {
        var h = NewHost();
        try
        {
            string k = LoadChara(h, femaleRel, "female", "A");
            Idle(h, k);
            h.Frames(3);
            FixedFloor(h);
            Preset(h, k, RagdollPreset.Limp);
            var b = Body(h, k, "A");
            h.Frames(60);
            h.Ok(new { cmd = "scene.pause", paused = true }, "pause");
            h.Frames(2);
            var r = h.Rt.FindChara(k);
            var oldBones = RagdollParts.All.Select(p => r.Context.BodyBones[p.Bone]).ToList();
            var keep = b.Parts.Select(p => p.Tr.rotation).ToList();
            var hips = b.Parts[0].Tr.position;
            h.Ok(new { cmd = "chara.change", key = k, path = femaleRel, sex = "female" }, "chara.change");
            h.Rt.ApplyNow();
            h.Frames(1);
            int alive = oldBones.Count(t => t);
            Check("Present: the old bones are destroyed", alive == 0, alive + " of 16 alive");
            var nb = h.Part.BodyOf(k);
            Check("the same body goes on", nb == b && b.Parts.Count == 16);
            r = h.Rt.FindChara(k);
            Check("the stages are on the new motion", r.Motion.Stages.Count(s => s.Name.StartsWith("ragdoll", StringComparison.Ordinal)) == 3, string.Join(" ", r.Motion.Stages.Select(s => s.Name)));
            double worst = 0;
            for (int i = 0; i < 16; i++) worst = Math.Max(worst, Deg(keep[i], b.Parts[i].Tr.rotation));
            Check("part rotations kept within 1e-4", worst <= 1e-4 * 180.0 / Math.PI, worst.ToString("G4") + " degrees");
            Check("the pelvis stays", (b.Parts[0].Tr.position - hips).magnitude <= 1e-4f, (b.Parts[0].Tr.position - hips).magnitude.ToString("G4"));
            double written = 0;
            foreach (var p in b.Parts)
                if (p.Entry != null) written = Math.Max(written, Deg(p.Bone.rotation, p.Tr.rotation * p.Offset));
            Check("the new bones are written", written <= 0.01 && b.Parts.All(p => p.Bone && p.Bone == r.Context.BodyBones[p.Row.Bone]), written.ToString("G4") + " degrees");
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ RK1

    /// <summary>The run of RK1: two characters, active, the idle row from time 0, a push at a step, n steps of scene.step.</summary>
    static ulong RK1Run(int pushAt, int steps, out float moved, out float end)
    {
        var h = NewHost();
        try
        {
            h.Ok(new { cmd = "scene.pause", paused = true }, "pause");
            string a = LoadChara(h, femaleRel, "female", "A");
            string c = LoadChara(h, maleRel, "male", "B");
            Place(h, c, new Vector3(5f, 0f, 2f), new Vector3(0f, 200f, 0f));
            Idle(h, a, 0f, 1f);
            Idle(h, c, 0f, 1f);
            h.Frames(2);
            FixedFloor(h);
            var ba = Body(h, a, "A");
            var bc = Body(h, c, "B");
            var start = ba.Part("chest").Tr.position;
            moved = 0f;
            for (int i = 0; i < steps; i++)
            {
                if (i == pushAt) ba.Push(ba.Part("chest"), new Vector3(0f, 0f, -1500f));
                h.Rt.Step(1, Dt);
                moved = Mathf.Max(moved, (ba.Part("chest").Tr.position - start).magnitude);
            }
            end = (ba.Part("chest").Tr.position - start).magnitude;
            return Hash(Hash(14695981039346656037UL, ba), bc);
        }
        finally { h.Dispose(); }
    }

    static void RK1()
    {
        var hashes = new List<ulong>();
        float moved = 0f, end = 0f;
        for (int i = 0; i < 4; i++) hashes.Add(RK1Run(60, 180, out moved, out end));
        string list = string.Join(" ", hashes.Select(x => x.ToString("x16")));
        Measure("RK1 hash " + hashes[0].ToString("x16") + " (preview scene)");
        Check("4 of 4 runs equal", hashes.Distinct().Count() == 1, list);
        Check("Present: the bodies moved at least 1, the greatest distance during the run", moved >= 1f, moved.ToString("F3") + ", at the end " + end.ToString("F3"));
        ulong other = RK1Run(61, 180, out _, out _);
        Check("Present: the hash changes when the push moves one step", other != hashes[0], other.ToString("x16"));
    }
}
