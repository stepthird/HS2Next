// RagdollTest, the order against the other writers and the clock (Phase 6.7 package P): RO2, RO3, RO6 to RO11, RK2 to RK4, RK6 to RK8, by numbers. A rotation is judged by its angle in double with
// the sign aligned: q and -q are one rotation. Bones of a twist check are the ones Runtime/Motion/Corrective/JointCorrectionTable.cs names as reading the written bone.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Motion;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class RagdollTest
{
    static void Headless_D_Order()
    {
        Group("RO2", "a write at 410 reaches the twist bones", RO2);
        Group("RO3", "the eye look aims from the physical head", RO3);
        Group("RO6", "the target at 410 sees no correction and no driven value", RO6);
        Group("RO7", "IK, FK and a card's modifiers after a release", RO7);
        Group("RO8", "a pass gives the same body", RO8);
        Group("RO9", "a probe and a settle with a body", RO9);
        Group("RO10", "a capture under a body", RO10);
        Group("RO11", "chains stay finite in a hard fall", RO11);
        Group("RK2", "a pause stops the world", RK2);
        Group("RK3", "scene.step runs its substeps", RK3);
        Group("RK4", "a recording drops none, a hitch is cut", RK4);
        Group("RK6", "a seek moves nothing of a body", RK6);
        Group("RK7", "the world goes with its last body", RK7);
        Group("RK8", "the world's time scale", RK8);
    }

    /// <summary>A fixed turn added to some bones at a phase.</summary>
    sealed class FixedWrite : IMotionStage
    {
        readonly (Transform bone, Quaternion turn)[] writes;
        public FixedWrite(int phase, params (Transform, Quaternion)[] w) { Phase = (MotionPhase)phase; writes = w; }
        public MotionPhase Phase { get; }
        public string Name => "test.write";
        public bool Enabled { get; set; } = true;
        public void Step(in MotionFrame frame) { foreach (var (b, q) in writes) b.localRotation = b.localRotation * q; }
        public void Reset() { }
    }

    // the same run with no write, a write at 410, a write at 705: local rotations of the named bones at the end
    static Dictionary<string, Quaternion> WriteRun(int phase, string[] names, Func<CharaRealization, (Transform, Quaternion)[]> writes, Action<Host, string> setup, int frames, string what)
    {
        var h = NewHost();
        try
        {
            string k = LoadChara(h, femaleRel, "female", what);
            Idle(h, k);
            setup(h, k);
            h.Frames(3);
            var r = h.Rt.FindChara(k);
            if (phase != 0) r.Motion.Add(new FixedWrite(phase, writes(r)));
            h.Frames(frames);
            var all = RootBones(r);
            return names.ToDictionary(n => n, n => all.First(t => t.name == n).localRotation);
        }
        finally { h.Dispose(); }
    }

    static void RO2()
    {
        var followers = new[] { "cf_J_ArmElbo_dam_01_L", "cf_J_ArmElboura_dam_L", "cf_J_ArmUp01_dam_L", "cf_J_ArmUp02_dam_L", "cf_J_LegUpDam_R" };
        Func<CharaRealization, (Transform, Quaternion)[]> writes = r => new[]
        {
            (B(r, "cf_J_ArmLow01_L"), Quaternion.Euler(30f, 40f, 0f)), (B(r, "cf_J_ArmUp00_L"), Quaternion.Euler(0f, 30f, 25f)), (B(r, "cf_J_LegUp00_R"), Quaternion.Euler(40f, 0f, 0f)),
        };
        Action<Host, string> rows = (h, k) => h.Ok(new { cmd = "chara.motion", key = k, correction = new { flags = new[] { true, true, true, true, true, true, true, true } } }, "the joint correction's rows on");
        var none = WriteRun(0, followers, writes, rows, 30, "no write");
        var at410 = WriteRun(410, followers, writes, rows, 30, "a write at 410");
        var at705 = WriteRun(705, followers, writes, rows, 30, "a write at 705");
        foreach (var n in followers)
        {
            double a = Deg(none[n], at410[n]), b = Deg(none[n], at705[n]);
            Check(n + ": follows a write at 410, by more than .1 degrees", a > 0.1, a.ToString("F3"));
            Check(n + ": does not follow a write at 705", b <= 1e-4, b.ToString("G3"));
        }
    }

    static void RO3()
    {
        var eyes = new[] { "cf_J_look_L", "cf_J_look_R" };
        Func<CharaRealization, (Transform, Quaternion)[]> writes = r => new[] { (B(r, "cf_J_Neck"), Quaternion.Euler(15f, 25f, 0f)) };
        Action<Host, string> look = (h, k) =>
        {
            h.Ok(new { cmd = "chara.face", key = k, eyesLook = 1 }, "the eye look's pattern 1");
            h.Ok(new { cmd = "look.target", key = k, pos = new[] { 0f, 14f, 12f } }, "the look's target");
        };
        var none = WriteRun(0, eyes, writes, look, 120, "no write");
        var at410 = WriteRun(410, eyes, writes, look, 120, "a write at 410");
        var at705 = WriteRun(705, eyes, writes, look, 120, "a write at 705");
        foreach (var n in eyes)
        {
            double a = Deg(none[n], at410[n]), b = Deg(none[n], at705[n]);
            // S0-7's q of .05 is 5.7 degrees
            Check(n + ": aims from a write at 410, by more than 5.7 degrees", a > 5.7, a.ToString("F3"));
            Check(n + ": does not see a write at 705", b <= 1e-4, b.ToString("G3"));
        }
    }

    static void RO6()
    {
        var h = Standing(femaleRel, "female", "RO6", out string k);
        try
        {
            string node = Key(h.Ok(new { cmd = "item.add", no = 399 }, "a node"));
            h.Frames(1);
            var b = Body(h, k, "RO6");
            Preset(h, k, RagdollPreset.Stiff);
            h.Frames(20);
            h.Ok(new { cmd = "scene.pause", paused = true }, "pause");
            h.Frames(2);
            var arm = b.Part("forearm_L");
            var hand = b.Part("upperarm_R");
            var armTarget = arm.TargetBoneRot;
            var handTarget = hand.TargetBoneRot;
            h.Ok(new { cmd = "pose.bones.set", key = k, bone = "cf_J_ArmLow01_L", rot = new[] { 0f, 40f, 0f } }, "a correction");
            Place(h, node, hand.Bone.position, (hand.Bone.rotation * Quaternion.Euler(0f, 50f, 0f)).eulerAngles);
            h.Ok(new { cmd = "constraint.add", source = new { key = node }, target = new { key = k, bone = "cf_J_ArmUp00_R" }, components = new[] { "rotation" }, keepOffset = false }, "a constraint");
            h.Frames(3);
            double seen1 = Deg(arm.TargetBoneRot, armTarget), seen2 = Deg(hand.TargetBoneRot, handTarget);
            double shown1 = Deg(arm.Bone.rotation, arm.TargetBoneRot), shown2 = Deg(hand.Bone.rotation, hand.TargetBoneRot);
            Check("the target sees no correction", seen1 <= 1e-4, seen1.ToString("G3"));
            Check("the target sees no driven value", seen2 <= 1e-4, seen2.ToString("G3"));
            Check("Present: the correction and the driven value differ from the target", shown1 > 5.0 && shown2 > 5.0, $"{shown1:F2}, {shown2:F2} degrees");
        }
        finally { h.Dispose(); }
    }

    // a run with or without a body, then what the bones hold at the end, chains and look bones left out
    static RootSnap RO7Run(bool body, string mode, string what, out bool ikOn, out HashSet<int> leftOut)
    {
        var h = NewHost();
        try
        {
            string k = LoadChara(h, maleRel, "male", what, physics: true);
            Idle(h, k);
            h.Frames(3);
            if (mode == "ik") h.Ok(new { cmd = "ik.copy", key = k, from = "current", enable = true }, "IK on");
            else h.Ok(new { cmd = "fk.copy", key = k, from = "current", enable = true }, "FK on");
            h.Frames(2);
            FixedFloor(h);
            Preset(h, k, RagdollPreset.Limp);
            var r = h.Rt.FindChara(k);
            ikOn = r.Motion.Get<IkStage>()?.AnyApplied == true;
            if (body)
            {
                var b = Body(h, k, what);
                b.Push(b.Part("chest"), new Vector3(0f, 0f, -800f));
                h.Frames(30);
                h.Part.Release(k, false, ExecuteOptions.NotRecorded);
            }
            else h.Frames(30);
            h.Frames(310);
            var all = RootBones(r);
            leftOut = new HashSet<int>(ChainsOf(r, all).SelectMany(c => c.Bones));
            for (int i = 0; i < all.Length; i++) if (all[i].name.StartsWith("cf_J_look", StringComparison.Ordinal)) leftOut.Add(i);
            return new RootSnap(all);
        }
        finally { h.Dispose(); }
    }

    static void RO7()
    {
        foreach (var mode in new[] { "ik", "fk" })
        {
            var twin = RO7Run(false, mode, mode + ": the twin", out _, out _);
            var body = RO7Run(true, mode, mode + ": the body", out bool ikOn, out var leftOut);
            double angle = 0, place = 0;
            string where = "";
            for (int i = 0; i < twin.Names.Length; i++)
            {
                if (leftOut.Contains(i)) continue;
                double a = Deg(twin.R[i], body.R[i]), p = (twin.P[i] - body.P[i]).magnitude;
                if (a > angle) { angle = a; where = twin.Names[i]; }
                place = Math.Max(place, p);
            }
            Check(mode + " on, a card with modifiers: after a release equal to a twin that never had a body within 1e-6", angle <= 1e-6 && place <= 1e-6, $"{angle:G3} degrees on {where}, {place:G3} units; {leftOut.Count} chain and look bones left out");
            if (mode == "ik") Check("Present: the IK stage writes bones", ikOn);
        }
    }

    static ulong RO8Run(bool pass, out bool scheduled, out ulong start)
    {
        var h = Standing(femaleRel, "female", "RO8", out string k);
        try
        {
            string other = LoadChara(h, maleRel, "male", "the other");
            Idle(h, other);
            Place(h, other, new Vector3(30f, 0f, 0f));
            string node = Key(h.Ok(new { cmd = "item.add", no = 399 }, "a node"));
            // the same frames in both runs: a command pumps one
            if (pass) h.Ok(new { cmd = "constraint.add", source = new { key = node }, target = new { key = other, bone = "cf_J_ArmLow01_L" }, components = new[] { "rotation" }, keepOffset = true }, "a constraint on the other");
            else h.Ok(new { cmd = "constraint.list" }, "a command of the same length");
            h.Frames(2);
            ulong s = 14695981039346656037UL;
            void Add(float f) { unchecked { s ^= (uint)BitConverter.SingleToInt32Bits(f); s *= 1099511628211UL; } }
            foreach (var kv in Bones(h.Rt.FindChara(k)).OrderBy(x => x.Key.name, StringComparer.Ordinal))
            {
                Add(kv.Value.p.x); Add(kv.Value.p.y); Add(kv.Value.p.z); Add(kv.Value.r.x); Add(kv.Value.r.y); Add(kv.Value.r.z); Add(kv.Value.r.w);
            }
            start = s;
            var b = Body(h, k, "RO8");
            b.Push(b.Part("chest"), new Vector3(0f, 0f, -1500f));
            h.Frames(120);
            scheduled = h.Rt.FindChara(k).Motion.Scheduled;
            return Hash(14695981039346656037UL, b);
        }
        finally { h.Dispose(); }
    }

    static void RO8()
    {
        ulong a = RO8Run(false, out bool s0, out ulong p0), b = RO8Run(true, out bool s1, out ulong p1);
        Check("the two runs start from the same pose, bits", p0 == p1, p0.ToString("x16") + " " + p1.ToString("x16"));
        Check("a pass gives the same body as no pass, bits", a == b, a.ToString("x16") + " " + b.ToString("x16"));
        Check("Present: the pass ran", s1 && !s0, $"Scheduled {s0}, {s1}");
    }

    static void RO9()
    {
        var h = Standing(femaleRel, "female", "RO9", out string k);
        try
        {
            Preset(h, k, RagdollPreset.Limp);
            var b = Body(h, k, "RO9");
            h.Frames(60);
            h.Ok(new { cmd = "scene.pause", paused = true }, "pause");
            h.Frames(2);
            var r = h.Rt.FindChara(k);
            ulong before = Hash(14695981039346656037UL, b);
            long steps = h.Part.World.Steps;
            var bones = Bones(r);
            int ran = r.Motion.Stages.Count(s => s.Enabled && s.Phase < MotionPhase.IkPrepare);
            bool sawBase = false;
            r.Motion.Probe(MotionPhase.IkPrepare, r.Root.transform, () => sawBase = Deg(B(r, "cf_J_Spine02").localRotation, b.Part(RagdollParts.Chest).Entry.BaseRot) <= 1e-4);
            h.Frames(1);
            int d = Differ(bones, out string first);
            Check("a probe leaves the bones, after the next step's stages wrote them, bits", d == 0, d + " differ " + first);
            Check("a probe reads what lies under the body", sawBase);
            Check("Present: the probe stepped at least 3 stages", ran >= 3, ran.ToString());
            r.Motion.RequestSettle();
            h.Frames(2);
            Check("a settle and a probe change nothing of the world", Hash(14695981039346656037UL, b) == before && h.Part.World.Steps == steps, $"steps {steps}, {h.Part.World.Steps}");
            d = Differ(bones, out first);
            Info("after the settle " + d + " of " + bones.Count + " bones differ, chains off: " + first);
        }
        finally { h.Dispose(); }
    }

    static void RO10()
    {
        var h = Standing(femaleRel, "female", "RO10", out string k);
        try
        {
            Preset(h, k, RagdollPreset.Limp);
            var b = Body(h, k, "RO10");
            b.Push(b.Part("chest"), new Vector3(0f, 0f, -600f));
            h.Frames(60);
            h.Ok(new { cmd = "scene.pause", paused = true }, "pause");
            h.Frames(2);
            var arm = b.Part("upperarm_L");
            var under = arm.Entry.BaseRot;
            var shown = arm.Bone.localRotation;
            h.Ok(new { cmd = "fk.capture", key = k, stopAnime = false }, "fk.capture");
            var fk = ((CharaNodeState)h.Rt.Model.Find(k).State).Fk;
            bool has = fk.Bones.TryGetValue(arm.Row.Bone, out var euler);
            Check("fk.capture stored the bone", has, fk.Bones.Count + " bones");
            if (!has) return;
            var stored = Quaternion.Euler(euler.X, euler.Y, euler.Z);
            Check("fk.capture stores what lies under the body", Deg(stored, under) <= 0.01, Deg(stored, under).ToString("G3") + " degrees");
            Check("Present: that differs from the body by more than 10 degrees", Deg(under, shown) > 10.0, Deg(under, shown).ToString("F2"));
            double read = 0;
            b.ReadBodyPose(() => read = Deg(arm.Bone.localRotation, shown));
            Check("the body's own pose can be read for a bake", read <= 1e-4, read.ToString("G3") + " degrees");
        }
        finally { h.Dispose(); }
    }

    static void RO11()
    {
        foreach (var (name, rel, sex) in Targets())
        {
            var h = NewHost();
            try
            {
                string k = LoadChara(h, rel, sex, name, physics: true);
                Idle(h, k);
                Place(h, k, new Vector3(0f, 28f, 0f));
                h.Frames(3);
                World(h, w => { w.Floor.Mode = RagdollFloorValues.ModeFixed; w.Floor.Height = 0f; w.Room.Size = new Vec3(80f, 80f, 80f); });
                Preset(h, k, RagdollPreset.Limp);
                var b = Body(h, k, name);
                b.Push(b.Part("chest"), new Vector3(0f, -600f, -1500f));
                var r = h.Rt.FindChara(k);
                var all = RootBones(r);
                var hips = B(r, "cf_J_Hips");
                var last = hips.position;
                float speed = 0f;
                int bad = 0;
                string first = "";
                for (int f = 0; f < 300; f++)
                {
                    h.Frames(1);
                    speed = Mathf.Max(speed, (hips.position - last).magnitude / Dt);
                    last = hips.position;
                    foreach (var t in all)
                    {
                        var p = t.position;
                        var q = t.rotation;
                        if (float.IsNaN(p.x + p.y + p.z + q.x + q.y + q.z + q.w) || float.IsInfinity(p.x + p.y + p.z))
                        {
                            if (bad++ == 0) first = t.name + " at frame " + (f + 1);
                        }
                    }
                }
                Check(name + ": every bone finite over a hard fall", bad == 0, bad + " " + first);
                Check(name + ": Present: the hips' world speed reached at least 50", speed >= 50f, speed.ToString("F1"));
            }
            finally { h.Dispose(); }
        }
    }

    // ------------------------------------------------------------ the clock

    static void RK2()
    {
        var h = Standing(femaleRel, "female", "RK2", out string k);
        try
        {
            Preset(h, k, RagdollPreset.Limp);
            var b = Body(h, k, "RK2");
            var chest = b.Part(RagdollParts.Chest);
            var start = chest.Tr.position;
            h.Frames(20);
            float moved = (chest.Tr.position - start).magnitude;
            h.Ok(new { cmd = "scene.pause", paused = true }, "pause");
            ulong before = Hash(14695981039346656037UL, b);
            long steps = h.Part.World.Steps;
            h.Frames(120);
            Check("no part moves in 120 paused frames", Hash(14695981039346656037UL, b) == before && h.Part.World.Steps == steps);
            Check("Present: they moved before", moved >= 0.5f, moved.ToString("F3"));
        }
        finally { h.Dispose(); }
    }

    static void RK3()
    {
        var h = Standing(femaleRel, "female", "RK3", out string k);
        try
        {
            var b = Body(h, k, "RK3");
            h.Ok(new { cmd = "scene.pause", paused = true }, "pause");
            foreach (var (n, dt) in new[] { (100, 1f / 60f), (7, 0.1f), (33, 1f / 30f) })
            {
                long s0 = h.Part.World.Steps;
                h.Ok(new { cmd = "scene.step", n, dt }, "scene.step");
                long ran = h.Part.World.Steps - s0;
                double want = Math.Floor(n * (double)dt * 240.0);
                Check($"scene.step {n} of {dt:F4} runs floor(n dt hz) substeps, give or take one", Math.Abs(ran - want) <= 1, ran + " against " + want);
            }
        }
        finally { h.Dispose(); }
    }

    static void RK4()
    {
        var h = Standing(femaleRel, "female", "RK4", out string k);
        try
        {
            var b = Body(h, k, "RK4");
            h.Frames(1);
            var w = h.Part.World;
            long s0 = w.Steps;
            h.Frames(1, 0.2f);
            Check("a live frame of 200 ms runs 12 substeps", w.LastSubsteps == 12, w.LastSubsteps + ", " + (w.Steps - s0));
            float old = Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime = 0.1f;
                s0 = w.Steps;
                h.Frames(10, 0.1f);
                long ran = w.Steps - s0;
                Check("a recording at 10 fps drops none", Math.Abs(ran - 240) <= 1, ran + " of 240, " + w.LastSubsteps + " in the last frame");
            }
            finally { Time.captureDeltaTime = old; }
        }
        finally { h.Dispose(); }
    }

    static void RK6()
    {
        var h = Standing(femaleRel, "female", "RK6", out string k);
        try
        {
            string other = LoadChara(h, maleRel, "male", "the other");
            Idle(h, other);
            Place(h, other, new Vector3(20f, 0f, 0f));
            h.Frames(1);
            Preset(h, k, RagdollPreset.Limp);
            var b = Body(h, k, "RK6");
            h.Frames(60);
            h.Ok(new { cmd = "scene.pause", paused = true }, "pause");
            h.Frames(2);
            h.Ok(new { cmd = "timeline.set", duration = 4, undo = false }, "a Timeline");
            var track = h.Ok(new { cmd = "timeline.track.add", type = "pos", key = other }, "a track on the other character");
            var id = track["track"];
            h.Ok(new JObject { ["cmd"] = "timeline.key.set", ["track"] = id, ["t"] = 0, ["v"] = new JArray(20f, 0f, 0f) }.ToString(Newtonsoft.Json.Formatting.None), "a key");
            h.Ok(new JObject { ["cmd"] = "timeline.key.set", ["track"] = id, ["t"] = 2, ["v"] = new JArray(30f, 0f, 0f) }.ToString(Newtonsoft.Json.Formatting.None), "a key");
            ulong before = Hash(14695981039346656037UL, b);
            long steps = h.Part.World.Steps;
            h.Ok(new { cmd = "timeline.seek", t = 0 }, "seek");
            h.Frames(2);
            var holder = h.Rt.HolderOf(other);
            var at = holder.position;
            h.Ok(new { cmd = "timeline.seek", t = 2 }, "seek");
            h.Frames(2);
            float moved = (holder.position - at).magnitude;
            Check("a seek moves nothing of a body", Hash(14695981039346656037UL, b) == before && h.Part.World.Steps == steps);
            Check("Present: the seek moved a character with no body by at least 1", moved >= 1f, moved.ToString("F3"));
            Info("a capture reads only: this row has no device; RagdollTest.play judges it");
        }
        finally { h.Dispose(); }
    }

    static void RK7()
    {
        var h = Standing(femaleRel, "female", "RK7", out string k);
        try
        {
            var b = Body(h, k, "RK7");
            var w = h.Part.World;
            var scene = w.Scene;
            var handle = scene.handle;
            h.Frames(5);
            h.Part.Release(k, false, ExecuteOptions.NotRecorded);
            Check("the release of the last body destroys the world", h.Part.World == null);
            Check("its scene is gone", !scene.IsValid(), scene.IsValid().ToString());
            Body(h, k, "again");
            var w2 = h.Part.World;
            Check("a new build makes a new world", w2 != null && w2 != w);
            Check("Present: on another scene", w2 != null && w2.Scene.handle != handle, $"handles {handle}, {w2?.Scene.handle}");
        }
        finally { h.Dispose(); }
    }

    static void RK8()
    {
        var h = Standing(femaleRel, "female", "RK8", out string k);
        try
        {
            var b = Body(h, k, "RK8");
            var w = h.Part.World;
            long s0 = w.Steps;
            h.Frames(60);
            long full = w.Steps - s0;
            World(h, x => x.Values.TimeScale = 0.5f);
            s0 = w.Steps;
            h.Frames(60);
            long half = w.Steps - s0;
            Check("a time scale of .5 halves the substeps", Math.Abs(half * 2 - full) <= 2, $"{full}, {half}");
        }
        finally { h.Dispose(); }
    }
}
