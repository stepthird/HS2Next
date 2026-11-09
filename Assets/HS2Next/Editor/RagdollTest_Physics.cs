// RagdollTest, how a body behaves (Phase 6.7 package P): RP2 to RP14, by numbers in a preview world. Each row keeps the bars of the spec; a bar the spec leaves open is named where it is set.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Ragdoll;
using HS2Next.Scenes.Runtime;
using UnityEngine;

public static partial class RagdollTest
{
    static void Headless_C_Physics()
    {
        Group("RP2", "knees and elbows bend the right way", RP2);
        Group("RP3", "feet flat from the first frame", RP3);
        Group("RP4", "a head that stops nodding", RP4);
        Group("RP5", "a body follows its animation", RP5);
        Group("RP6", "gravity compensation", RP6);
        Group("RP7", "knockdown and getting up", RP7);
        Group("RP8", "a hold on a limp body", RP8);
        Group("RP9", "the accepted fall", RP9);
        Group("RP10", "a tie", RP10);
        Group("RP11", "a grab sags, a follow pin tracks", RP11);
        Group("RP12", "a pusher", RP12);
        Group("RP13", "two bodies", RP13);
        Group("RP14", "turn and settle", RP14);
    }

    /// <summary>The turn of a joint about its flexion axis from the rest pose, in degrees: positive towards flexion.</summary>
    static double Flex(RagdollBodyPart p)
    {
        var d = p.LocalRotation * Quaternion.Inverse(p.StartLocal);
        var v = new Vector3(d.x, d.y, d.z);
        double along = Vector3.Dot(v, p.FlexAxis.normalized);
        double a = 2.0 * Math.Atan2(along, d.w) * 180.0 / Math.PI;
        return a > 180.0 ? a - 360.0 : a < -180.0 ? a + 360.0 : a;
    }

    static void RP2()
    {
        foreach (var (name, rel, sex) in Targets())
        {
            var h = Standing(rel, sex, name, out string k);
            try
            {
                Place(h, k, new Vector3(0f, 6f, 0f));
                h.Frames(1);
                Preset(h, k, RagdollPreset.Limp);
                var b = Body(h, k, name);
                b.Push(b.Part("chest"), new Vector3(0f, 0f, -600f));
                var keys = new[] { "shin_L", "shin_R", "forearm_L", "forearm_R" };
                var least = keys.ToDictionary(x => x, x => double.MaxValue);
                var most = keys.ToDictionary(x => x, x => double.MinValue);
                for (int f = 0; f < 600; f++)
                {
                    h.Frames(1);
                    foreach (var key in keys)
                    {
                        double a = Flex(b.Part(key));
                        least[key] = Math.Min(least[key], a);
                        most[key] = Math.Max(most[key], a);
                    }
                }
                string line = string.Join(", ", keys.Select(x => $"{x} {least[x]:F1} to {most[x]:F1}"));
                // the limits of the table, with the joint's projection angle of 5 degrees: the spec gives the side, not a margin
                bool side = least["shin_L"] >= -7 && least["shin_R"] >= -7 && least["forearm_L"] >= -10 && least["forearm_R"] >= -10;
                Check(name + ": knee and elbow stay on the flexion side", side, line);
                Check(name + ": Present: the angles reach at least 30 degrees", keys.All(x => most[x] >= 30), line);
            }
            finally { h.Dispose(); }
        }
    }

    static float Pitch(Vector3 up) => Mathf.Asin(Mathf.Clamp(up.y, -1f, 1f)) * Mathf.Rad2Deg;

    static readonly RB12Pose Kneel = new RB12Pose("rifle", 15, 31, 1, 0.5f);
    static readonly RB12Pose Seated = new RB12Pose("chair", 19, 1, 11, 0.5f);

    // a sole does not follow a toe-down target into the floor: how far a pitch stands outside the span from the target's pitch to level
    static double OffSpan(float pitch, float target)
    {
        float low = Mathf.Min(target, 0f), high = Mathf.Max(target, 0f);
        return pitch < low ? low - pitch : pitch > high ? pitch - high : 0.0;
    }

    // the floor at the character's root height, the map's case: what the feet do to the body in 2 s
    static void RP3Run(string rel, string sex, string what, out float lowest, out float pelvis, out float lift, out double span, out double plain, out string where, RB12Pose? pose = null)
    {
        Host h;
        string k;
        if (pose == null) h = Standing(rel, sex, what, out k);
        else
        {
            h = NewHost();
            k = LoadChara(h, rel, sex, what);
            RB12Row(h, k, pose.Value);
            h.Frames(3);
            FixedFloor(h);
        }
        try
        {
            var b = Body(h, k, what);
            var feet = new[] { b.Part("foot_L"), b.Part("foot_R") };
            lowest = feet.Min(p => RagdollBody.LowestOf(p));
            pelvis = 0f;
            lift = 0f;
            span = 0;
            plain = 0;
            where = "";
            for (int f = 0; f < 120; f++)
            {
                h.Frames(1);
                var c = b.Parts[0];
                var d = c.Rb.worldCenterOfMass - c.TargetCom;
                pelvis = Mathf.Max(pelvis, d.magnitude);
                lift = Mathf.Max(lift, d.y);
                foreach (var p in feet)
                {
                    float now = Pitch(p.Tr.up), target = Pitch(p.TargetRot * Vector3.up);
                    plain = Math.Max(plain, Math.Abs(now - target));
                    double a = OffSpan(now, target);
                    if (a > span) { span = a; where = p.Key + " at frame " + (f + 1); }
                }
            }
        }
        finally { h.Dispose(); }
    }

    static void RP3()
    {
        foreach (var (name, rel, sex) in Targets())
        {
            RP3Run(rel, sex, name, out float lowest, out float pelvis, out float lift, out double span, out double plain, out string where);
            Info($"{name}: the feet's lowest point at the first frame {lowest:F4}, for information: a foot the pose tilts digs by its corner");
            Check(name + ": over 2 s on a floor at the root's height the pelvis stays within .1 of its target", pelvis <= 0.1f, pelvis.ToString("F4"));
            Check(name + ": each foot's pitch stays within 2 degrees of the span from its target's pitch to level", span <= 2.0, $"{span:F3} degrees {where}, off the target itself at most {plain:F3}");
            float kept = RagdollBody.AnkleRatio;
            RagdollBody.AnkleRatio = 0.65f;
            try
            {
                RP3Run(rel, sex, name + ": the old constant", out _, out _, out float old, out _, out _, out _);
                Check(name + ": Present: the old constant .65 on the same floor lifts the pelvis by at least .2", old >= 0.2f, old.ToString("F4"));
            }
            finally { RagdollBody.AnkleRatio = kept; }
            // the first pose of the test rows, in their order, that stands by the row's rule: the bars set on idle, on data they were not set on
            RP3Run(rel, sex, name + ": the chair row", out float low2, out float pelvis2, out _, out double span2, out double plain2, out string where2, Seated);
            Info($"{name}, the chair row: the first frame's lowest {low2:F4}");
            Check(name + ": the chair row: the pelvis stays within .1 of its target", pelvis2 <= 0.1f, pelvis2.ToString("F4"));
            Check(name + ": the chair row: each foot's pitch stays within 2 degrees of the span", span2 <= 2.0, $"{span2:F3} degrees {where2}, off the target itself at most {plain2:F3}");
            // a kneel is out of the row: its target passes the ankle's limit, so the foot stops short
            RP3Run(rel, sex, name + ": the kneel", out _, out float seat, out _, out double short_, out _, out string foot, Kneel);
            Warn($"RP3W {name}, the rifle kneel: the target passes the ankle's limit: foot pitch {short_:F2} degrees off the span, {foot}, pelvis {seat:F4} from its target");
        }
    }

    // friction itself: gravity 0, the trunk held, muscle 0, a push small enough to stay inside the joint's limits.
    // The head's speed against the chest 1 s on, as a part of its first value
    static float RP4Run(float friction, string what, out float first, out float late, out double angle, out string limits, out string curve)
    {
        var h = Standing(femaleRel, "female", what, out string k);
        try
        {
            World(h, w => { w.Values.Friction = friction; w.Values.Gravity = 0f; });
            Block(h, k, b => { b.Values.Muscle = 0f; b.Values.Pin = 1f; b.Values.PinHipsOnly = true; b.Values.PinFalloff = 0f; b.Values.KnockDistance = 0f; });
            var body = Body(h, k, what);
            var head = body.Part(RagdollParts.Head);
            var chest = body.Part(RagdollParts.Chest);
            body.Parts[0].Rb.isKinematic = true;
            chest.Rb.isKinematic = true;
            h.Frames(5);
            var j = head.Joint;
            limits = $"on {head.Parent.Key}, x {j.lowAngularXLimit.limit:F0} to {j.highAngularXLimit.limit:F0}, y {j.angularYLimit.limit:F0}, z {j.angularZLimit.limit:F0}";
            Quaternion Bend() => Quaternion.Inverse(chest.Tr.rotation) * head.Tr.rotation;
            float Speed() => (head.Rb.angularVelocity - chest.Rb.angularVelocity).magnitude;
            var start = Bend();
            body.Push(head, new Vector3(0.5f, 0f, 0.5f));
            first = 0f;
            angle = 0;
            var line = new List<string>();
            for (int f = 1; f <= 61; f++)
            {
                h.Frames(1);
                if (f == 1) first = Speed();
                angle = Math.Max(angle, Deg(start, Bend()));
                if (f == 2 || f == 4 || f == 8 || f == 16 || f == 31 || f == 46) line.Add($"frame {f} {Speed():F4}{(head.Rb.IsSleeping() ? " asleep" : "")}");
            }
            late = Speed();
            curve = string.Join(", ", line) + (head.Rb.IsSleeping() ? ", asleep at the reading" : ", awake at the reading");
            return late / first;
        }
        finally { h.Dispose(); }
    }

    static void RP4()
    {
        float on = RP4Run(3f, "friction 3", out float first3, out float late3, out double angle3, out string limits, out string curve3);
        float off = RP4Run(0f, "friction 0", out float first0, out float late0, out double angle0, out _, out string curve0);
        Info($"friction 3: the first value {first3:F4} rad/s, after 1 s {late3:F4}, the greatest angle {angle3:F2} degrees; friction 0: {first0:F4}, {late0:F4}, {angle0:F2}; the head's limits {limits}");
        Info("friction 3: " + curve3);
        Info("friction 0: " + curve0);
        Check("friction 3: after 1 s the head's speed against the chest is at most .1 of its first value", on <= 0.1f, on.ToString("F4"));
        Check("Present: with friction 0 the same ratio is at least .5", off >= 0.5f, off.ToString("F4"));
    }

    static void RP5()
    {
        foreach (var (name, rel, sex) in Targets())
        {
            var h = Standing(rel, sex, name, out string k);
            try
            {
                Idle(h, k, 0f, 1f);
                h.Frames(2);
                var b = Body(h, k, name);
                var speeds = b.Parts.Skip(1).ToDictionary(p => p.Key, p => new List<float>(600));
                float worst = 0f;
                for (int f = 0; f < 600; f++)
                {
                    h.Frames(1);
                    var pelvis = b.Parts[0];
                    worst = Mathf.Max(worst, (pelvis.Rb.worldCenterOfMass - pelvis.TargetCom).magnitude);
                    foreach (var p in b.Parts.Skip(1)) speeds[p.Key].Add((p.Rb.angularVelocity - p.Parent.Rb.angularVelocity).magnitude);
                }
                float Median(List<float> l) { var s = l.OrderBy(x => x).ToList(); return s[s.Count / 2]; }
                var top = speeds.OrderByDescending(kv => Median(kv.Value)).First();
                Check(name + ": the pelvis stays within .3 of its target over 600 frames", worst <= 0.3f, worst.ToString("F4"));
                Check(name + ": no joint oscillates above 2 rad/s median", Median(top.Value) <= 2f, $"{Median(top.Value):F3} on {top.Key}");
                Check(name + ": it stays up", b.State == RagdollState.Up, b.State.ToString());
            }
            finally { h.Dispose(); }
        }
        var l2 = Standing(femaleRel, "female", "limp", out string k2);
        try
        {
            Preset(l2, k2, RagdollPreset.Limp);
            var b = Body(l2, k2, "limp");
            float start = b.Parts[0].Tr.position.y;
            l2.Frames(120);
            float drop = start - b.Parts[0].Tr.position.y;
            Check("Present: with pin 0 and muscle 0 it falls", drop >= 3f, drop.ToString("F3"));
        }
        finally { l2.Dispose(); }
    }

    static float RP6Run(bool comp, string what)
    {
        var h = Standing(femaleRel, "female", what, out string k);
        try
        {
            World(h, w => { w.Floor.Height = -200f; });
            Block(h, k, b => { b.Values.PinFalloff = 0f; b.Values.KnockDistance = 0f; b.Values.Muscle = 0f; b.Values.GravityComp = comp; });
            var body = Body(h, k, what);
            h.Frames(240);
            var p = body.Parts[0];
            return p.TargetCom.y - p.Rb.worldCenterOfMass.y;
        }
        finally { h.Dispose(); }
    }

    static void RP6()
    {
        float on = RP6Run(true, "compensated"), off = RP6Run(false, "not compensated");
        float want = 98.1f / (12f * 12f);
        Check("with gravity compensation a pin sags by at most .1", Mathf.Abs(on) <= 0.1f, on.ToString("F4"));
        Check($"Present: without it within 20 % of g / w^2 = {want:F3}", Mathf.Abs(off - want) <= 0.2f * want, off.ToString("F4"));
    }

    static void RP7()
    {
        foreach (var (name, rel, sex) in Targets())
        {
            var h = Standing(rel, sex, name, out string k);
            try
            {
                var b = Body(h, k, name);
                h.Frames(30);
                b.Knock();
                var order = new List<RagdollState> { b.State };
                int upAt = -1;
                int limit = (int)((1.5f + 2.5f + 2f) * 60f);
                for (int f = 0; f < limit && upAt < 0; f++)
                {
                    h.Frames(1);
                    if (b.State != order[order.Count - 1]) order.Add(b.State);
                    if (b.State == RagdollState.Up) upAt = f + 1;
                }
                string seq = string.Join(" ", order);
                Check(name + ": the states in order", seq == "Down GettingUp Up", seq);
                Check(name + ": up within minDown + getUp + 2 s", upAt > 0, upAt + " frames of " + limit);
                h.Frames(2);
                double worst = 0;
                string where = "";
                foreach (var p in b.Parts.Skip(1))
                {
                    double d = Deg(p.LocalRotation, Quaternion.Inverse(p.Parent.TargetRot) * p.TargetRot);
                    if (d > worst) { worst = d; where = p.Key; }
                }
                Check(name + ": joints within 45 degrees after", worst <= 45.0, $"{worst:F2} on {where}");
                Measure($"RP7 {name}: up at frame {upAt}, moved the node {b.Relocations} times, worst joint {worst:F2} degrees");
            }
            finally { h.Dispose(); }
        }
        var l = Standing(femaleRel, "female", "pin 0", out string k2);
        try
        {
            Block(l, k2, b => b.Values.Pin = 0f);
            var body = Body(l, k2, "pin 0");
            body.Knock();
            l.Frames(360);
            Check("Present: a body with pin 0 stays down", body.State == RagdollState.Down, body.State.ToString());
        }
        finally { l.Dispose(); }
    }

    static double RP8Run(bool hold, string what, out double peak, out float wobble)
    {
        var h = Standing(femaleRel, "female", what, out string k);
        try
        {
            Place(h, k, new Vector3(0f, 6f, 0f));
            h.Frames(1);
            Preset(h, k, RagdollPreset.Limp);
            var b = Body(h, k, what);
            h.Frames(1);
            if (hold)
            {
                var res = h.Part.Hold(k, "upperarm,forearm,hand", out int held, ExecuteOptions.NotRecorded);
                Check(what + ": held", res.Ok && held == 6, held + " joints");
            }
            var p = b.Part("forearm_L");
            var start = p.LocalRotation;
            b.Push(b.Part("chest"), new Vector3(0f, 0f, -600f));
            var keys = new[] { "upperarm_L", "upperarm_R", "forearm_L", "forearm_R", "hand_L", "hand_R" };
            var speeds = keys.ToDictionary(x => x, x => new List<float>());
            peak = 0;
            int at = 0;
            for (int f = 0; f < 240; f++)
            {
                h.Frames(1);
                double d = Deg(start, p.LocalRotation);
                if (d > peak) { peak = d; at = f + 1; }
                if (f >= 180)
                    foreach (var key in keys)
                    {
                        var q = b.Part(key);
                        speeds[key].Add((q.Rb.angularVelocity - q.Parent.Rb.angularVelocity).magnitude);
                    }
            }
            wobble = speeds.Values.Max(l => l.OrderBy(x => x).ElementAt(l.Count / 2));
            Info($"{what}: the most {peak:F2} degrees at frame {at}, at frame 240 {Deg(start, p.LocalRotation):F2}");
            return Deg(start, p.LocalRotation);
        }
        finally { h.Dispose(); }
    }

    static void RP8()
    {
        double on = RP8Run(true, "a hold", out double peak, out float wobble);
        RP8Run(false, "no hold", out double off, out _);
        Check("a hold on a limp body stands at most 3 degrees off at rest, frame 240", on <= 3.0, $"{on:F3}, the impact's peak {peak:F3}");
        Check("no held joint oscillates above 2 rad/s median at rest", wobble <= 2f, wobble.ToString("F4"));
        Check("Present: without a hold at least 20", off >= 20.0, off.ToString("F3"));
    }

    // the fall of V12, by the owner's script, in seconds from the build: a tie, a shot that goes limp, pushes in the root's frame, a settle
    static bool RP9Run(float start, bool settle, out float front, out float height)
    {
        var h = NewHost();
        try
        {
            string k = LoadChara(h, femaleRel, "female", "RP9");
            h.Ok(new { cmd = "chara.anime", key = k, group = 19, category = 0, no = 0, time = start, speed = 1f }, "the idle row");
            h.Frames(3);
            FixedFloor(h);
            var b = Body(h, k, "RP9");
            var rootRot = h.Rt.FindChara(k).Holder.rotation;
            var chest = b.Part(RagdollParts.Chest);
            var head = b.Part(RagdollParts.Head);
            var fwd = rootRot * Vector3.forward;
            var script = new (float at, Action act)[]
            {
                (0.01f, () => h.Part.Tie(k, "hand_L", "hand_R", ExecuteOptions.NotRecorded)),
                (0.03f, () =>
                {
                    var at = head.Rb.worldCenterOfMass;
                    RagdollShots.For(h.Rt).Fire(at + fwd * 20f, -fwd, at + fwd * 20f, 40f, true);
                }),
                (0.04f, () => b.Push(head, rootRot * new Vector3(0f, 0f, -100f))),
                (0.09f, () => b.Push(chest, rootRot * new Vector3(0f, 0f, -250f))),
                (0.29f, () => b.Push(chest, rootRot * new Vector3(0f, 0f, -150f))),
                (0.63f, () => { if (settle) b.Settle(Vector3.up, 5.5f, 16f, 3f); }),
            };
            int next = 0;
            for (int f = 1; f <= 390; f++)
            {
                float t = f * Dt;
                while (next < script.Length && script[next].at <= t) script[next++].act();
                h.Frames(1);
            }
            front = (chest.BoneRotation * Vector3.forward).y;
            height = b.Parts[0].Rb.worldCenterOfMass.y;
            return front >= 0.7f && height <= 2f;
        }
        finally { h.Dispose(); }
    }

    static void RP9()
    {
        int with = 0, without = 0;
        var line = new List<string>();
        for (int i = 0; i < 10; i++)
        {
            float t = i * 0.1f;
            if (RP9Run(t, true, out float front, out float height)) with++;
            if (RP9Run(t, false, out float f2, out float h2)) without++;
            line.Add($"{t:F1}: {front:F2}/{height:F2}, without {f2:F2}/{h2:F2}");
        }
        Info(string.Join("; ", line));
        Check("chest forward y at least .7 and the pelvis at most 2 over the floor in at least 8 of 10 runs", with >= 8, with + " of 10");
        Info("the same with no settle: " + without + " of 10, reported" + (without >= with ? "; at this foot the fall shows no gain from settle, RP14 carries settle's proof" : ""));
    }

    static float RP10Run(bool tie, string what, out float moved, out float rest)
    {
        var h = Standing(femaleRel, "female", what, out string k);
        try
        {
            Place(h, k, new Vector3(0f, 6f, 0f));
            h.Frames(1);
            Preset(h, k, RagdollPreset.Limp);
            var b = Body(h, k, what);
            if (tie)
            {
                var r = h.Part.Tie(k, "hand_L", "hand_R", ExecuteOptions.NotRecorded);
                Check(what + ": tied", r.Ok && b.TieCount == 1, b.TieCount + " ties");
            }
            var l = b.Part("hand_L");
            var rr = b.Part("hand_R");
            float d0 = (l.Rb.worldCenterOfMass - rr.Rb.worldCenterOfMass).magnitude, worst = 0f, late = 0f;
            int at = 0;
            b.Push(b.Part("chest"), new Vector3(0f, 0f, -600f));
            for (int f = 0; f < 240; f++)
            {
                h.Frames(1);
                float d = Mathf.Abs((l.Rb.worldCenterOfMass - rr.Rb.worldCenterOfMass).magnitude - d0);
                if (d > worst) { worst = d; at = f + 1; }
                if (f >= 180) late = Mathf.Max(late, d);
            }
            Info($"{what}: span {d0:F2}, the most {worst:F4} at frame {at}, the most over the last 60 frames {late:F4}");
            moved = worst;
            rest = late;
            return worst;
        }
        finally { h.Dispose(); }
    }

    static void RP10()
    {
        float on = RP10Run(true, "tied", out float peak, out float late), off = RP10Run(false, "untied", out _, out _);
        Check("tied, at rest (the last 60 frames) the hands' distance stays within .05", late <= 0.05f, $"{late:F4}, the peak {peak:F4}");
        if (peak > 0.3f) Warn($"RP10W the peak of a tie {peak:F3} at impact");
        Check("Present: untied it moves by at least 1", off >= 1f, off.ToString("F3"));
    }

    static void RP11()
    {
        var h = Standing(femaleRel, "female", "grab", out string k);
        try
        {
            World(h, w => w.Floor.Height = -200f);
            Preset(h, k, RagdollPreset.Limp);
            var b = Body(h, k, "grab");
            var part = b.Part(RagdollParts.Chest);
            var g = h.Part.World.Grab(part.Rb, part.Rb.worldCenterOfMass);
            h.Frames(240);
            float sag = g.Goal.y - g.Point.y;
            Check("a grab by one point sags by at least 1.5", sag >= 1.5f, sag.ToString("F3"));
            g.Drop();
        }
        finally { h.Dispose(); }
        var h2 = Standing(femaleRel, "female", "follow", out string k2);
        try
        {
            World(h2, w => w.Floor.Height = -200f);
            Preset(h2, k2, RagdollPreset.Limp);
            string node = Key(h2.Ok(new { cmd = "item.add", no = 399 }, "a node"));
            h2.Frames(1);
            var b = Body(h2, k2, "follow");
            var part = b.Part("hand_R");
            Place(h2, node, part.Rb.worldCenterOfMass);
            var r = h2.Part.PinPart(k2, "hand_R", null, node, null, false, ExecuteOptions.NotRecorded);
            Check("the pin", r.Ok && b.Pins.Count == 1, r.Ok ? "" : r.Code + ": " + r.Message);
            var at = part.Rb.worldCenterOfMass;
            var pin = b.Pins[0];
            h2.Frames(120);
            float worst = 0f;
            var node0 = h2.Rt.HolderOf(node).position;
            float moved = 0f;
            for (int f = 0; f < 240; f++)
            {
                var p = node0 + new Vector3(f * 0.05f, 0f, 0f);
                Place(h2, node, p);
                h2.Frames(1);
                worst = Mathf.Max(worst, (pin.Grab.Point - pin.Grab.Goal).magnitude);
                moved = (p - node0).magnitude;
            }
            Check("a pin that follows tracks a moving node within .5", worst <= 0.5f, worst.ToString("F3"));
            Check("Present: the node moved at least 10", moved >= 10f, moved.ToString("F2"));
            Check("while the node stands the pin reads ok", pin.State == RagdollPinStates.Ok, pin.State);
            var goal = pin.Grab.Goal;
            h2.Session.Delete(new[] { node });
            h2.Frames(3);
            Check("a pin on a deleted node reads missing and stays where it is", pin.State == RagdollPinStates.Missing && pin.Grab.Goal == goal, pin.State);
            h2.Ok(new { cmd = "undo" }, "undo");
            h2.Frames(3);
            Check("an undo brings it back to following", pin.State == RagdollPinStates.Ok, pin.State);
        }
        finally { h2.Dispose(); }
    }

    // a capsule along the upper body of a second character, walked through the first by its node
    static float RP12Run(bool pusher, string what, out float off, out int count)
    {
        var h = Standing(femaleRel, "female", what, out string k);
        off = 0f;
        count = 0;
        try
        {
            string other = LoadChara(h, femaleRel, "female", what + ": the second");
            Idle(h, other);
            Place(h, other, new Vector3(8f, 0f, 0f));
            h.Frames(2);
            var b = Body(h, k, what);
            if (pusher)
            {
                var r = h.Part.AddPusher(other, "cf_J_Spine01", "cf_J_Neck", 1.5f, out count, ExecuteOptions.NotRecorded);
                Check(what + ": the pusher", r.Ok, r.Ok ? "" : r.Code + ": " + r.Message);
            }
            var chest = b.Part(RagdollParts.Chest);
            var start = chest.Rb.worldCenterOfMass;
            float most = 0f;
            var ro = h.Rt.FindChara(other);
            for (int f = 0; f < 90; f++)
            {
                Place(h, other, new Vector3(8f - f * 0.16f, 0f, 0f));
                h.Frames(1);
                most = Mathf.Max(most, (chest.Rb.worldCenterOfMass - start).magnitude);
                if (pusher)
                {
                    var p = h.Part.World.Pushers[0];
                    var mid = (B(ro, "cf_J_Spine01").position + B(ro, "cf_J_Neck").position) * 0.5f;
                    off = Mathf.Max(off, (p.Rb.position - mid).magnitude);
                }
            }
            return most;
        }
        finally { h.Dispose(); }
    }

    static void RP12()
    {
        float on = RP12Run(true, "with a pusher", out float off, out int count), none = RP12Run(false, "without", out _, out _);
        Check("a pusher moves a body", on >= 1f, on.ToString("F3") + ", pushers " + count);
        // a frame's lag, declared: the capsule follows its bones one frame behind, .16 units here
        Check("a body does not move a pusher: it stays on its bones within a frame's move", off <= 0.2f, off.ToString("F4"));
        Check("Present: without it the same path leaves the body where it is", none < 0.3f, none.ToString("F3"));
    }

    static void RP13()
    {
        var h = Standing(femaleRel, "female", "below", out string k);
        try
        {
            string top = LoadChara(h, maleRel, "male", "above");
            Idle(h, top);
            Place(h, top, new Vector3(0.5f, 20f, 0f), new Vector3(0f, 90f, 0f));
            World(h, w => w.Room.Size = new Vec3(40f, 60f, 40f));
            h.Frames(2);
            Preset(h, k, RagdollPreset.Limp);
            Preset(h, top, RagdollPreset.Limp);
            var a = Body(h, k, "below");
            var c = Body(h, top, "above");
            float depth = 0f, late = 0f;
            int touched = 0, deepest = 0;
            for (int f = 0; f < 300; f++)
            {
                h.Frames(1);
                foreach (var p in a.Parts)
                    foreach (var q in c.Parts)
                        if (Physics.ComputePenetration(p.Col, p.Tr.position, p.Tr.rotation, q.Col, q.Tr.position, q.Tr.rotation, out _, out float d))
                        {
                            if (d > depth) { depth = d; deepest = f + 1; }
                            if (f >= 240) late = Mathf.Max(late, d);
                            touched++;
                        }
            }
            Check("two bodies do not interpenetrate: at rest, the last 60 frames, the deepest is at most .1", late <= 0.1f, $"{late:F4}, the contact's peak {depth:F4} at frame {deepest}");
            if (depth > 0.3f) Warn($"RP13W the contact's peak {depth:F3} at frame {deepest}");
            Check("Present: they touched", touched > 0, touched + " part pairs in contact over 300 frames");
        }
        finally { h.Dispose(); }
    }

    // gravity 0 and a limp body touching nothing, its front 90 degrees off up
    static RagdollBody InTheAir(Host h, string k, string what)
    {
        World(h, w => { w.Values.Gravity = 0f; w.Room.Size = new Vec3(40f, 60f, 40f); });
        Place(h, k, new Vector3(8f, 30f, 0f), new Vector3(0f, 0f, 90f));
        h.Frames(1);
        Preset(h, k, RagdollPreset.Limp);
        var b = Body(h, k, what);
        h.Frames(30);
        return b;
    }

    static float FrontToUp(RagdollBody b) => Vector3.Angle(b.Part(RagdollParts.Chest).BoneRotation * Vector3.forward, Vector3.up);

    static float RP14Turn(bool turn, string what)
    {
        var h = Standing(femaleRel, "female", what, out string k);
        try
        {
            var b = InTheAir(h, k, what);
            if (turn) b.Turn(Vector3.up, 2f);
            h.Frames(30);
            return FrontToUp(b);
        }
        finally { h.Dispose(); }
    }

    static void RP14()
    {
        var h = Standing(femaleRel, "female", "settle", out string k);
        try
        {
            var b = InTheAir(h, k, "settle");
            float start = FrontToUp(b);
            Info($"in the air: the lowest point {b.Parts.Min(p => RagdollBody.LowestOf(p)):F2}");
            b.Settle(Vector3.up, 5.5f, 16f, 3f);
            int first = 0;
            float least = 180f;
            for (int f = 1; f <= 120; f++)
            {
                h.Frames(1);
                float a = FrontToUp(b);
                least = Mathf.Min(least, a);
                if (first == 0 && a <= 30f) first = f;
            }
            Check("settle, the script's values: the chest's front comes within 30 degrees of up within 2 s", first > 0, $"{(first > 0 ? "first at frame " + first : "not reached")}, the least {least:F2}, at 2 s {FrontToUp(b):F2}");
            Check("Present: it started at least 60 degrees off", start >= 60f, start.ToString("F2"));
        }
        finally { h.Dispose(); }
        float with = RP14Turn(true, "turn"), without = RP14Turn(false, "no turn");
        Check("turn at its defaults: 30 frames later at least 5 degrees nearer than a twin without it", without - with >= 5f, $"{with:F2} against {without:F2}");
    }
}
