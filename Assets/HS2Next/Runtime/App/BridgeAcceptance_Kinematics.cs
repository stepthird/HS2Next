// IK1 of the bridge acceptance (Phase 3.4 package C): an imported HS2 IK pose
// solved through the bridge and kept across save and load, in Editor play mode (BridgeTest.RunPlay) and in the app player
// (-bridgeAccept) after FX1.
//   scene.new; chara.load the acceptance card (paused, physics off, as A8); pose.load P2 (STANDING/Floor/Peace.dat: both
//   modes, one scale, all IK groups) → ikApproximated, ikScale 1; after 3 frames ik.list {exact}: the solve ran, every
//   reachable hand / foot residual ≤ 1e-4; bones.dump body after 10 settle frames (the 3 → 13 frame change is reported) and
//   unchanged 5 frames later; a capture; scene.save → scene.new → [scene.load, chara.motion paused] → after 13 frames the same
//   ik.list targets (1e-6) and bones.dump within 1e-4 units / 0.01°. The body skeleton is compared (IK's bones and everything
//   under them); the head's eye-look bones are not: with paused (dt-0) steps the eye look does not settle again after the
//   reload (cf_J_look_R 0.012°, not an IK matter).
//   U10 (Editor vs player): the line "IK1 numbers:" lists the residual maximum and digests (FNV-1a of the exact doubles) of the
//   targets and of the dumped bones; the two hosts' lines are compared by hand (the solve is pure script math).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HS2Next.App
{
    public static partial class BridgeAcceptance
    {
        /// <summary>P2, relative to the HS2 pose folder.</summary>
        public const string Ik1Pose = "STANDING/Floor/Peace.dat";
        static readonly string[] Ik1Ends = { "LeftHand", "RightHand", "LeftFoot", "RightFoot" };

        public static IEnumerator Kinematics(AcceptanceCtx c)
        {
            c.Id = "IK1";
            yield return c.Call(new { cmd = "scene.new" });
            c.CheckOk("scene.new");
            yield return c.Call(new { cmd = "chara.load", path = c.FemaleRel, sex = "female" }, 180);
            string key = (string)c.J["added"]?[0]?["key"];
            if (!c.Check(c.Last != null && c.Last.Ok && key != null, "chara.load the acceptance card", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            yield return c.Call(new { cmd = "chara.motion", key, paused = true, physics = false, waitFrames = 2 });
            c.CheckOk("chara.motion {paused, physics: false}");
            yield return c.Call(new { cmd = "pose.load", key, path = Ik1Pose });
            if (!c.Check(c.Last != null && c.Last.Ok && (bool?)c.J["ikApproximated"] == true && (int?)c.J["ikScale"] == 1 && (bool?)c.J["ikPending"] == false,
                "pose.load P2: IK converted (ikApproximated, ikScale 1, ikPending false)", AcceptanceCtx.Short(c.Last?.ToString()))) yield break;
            c.Info("IK1: P2 body shift " + c.J["ikBodyShift"]?.ToString(Newtonsoft.Json.Formatting.None) + ", unreachable " + c.J["ikUnreachable"]?.ToString(Newtonsoft.Json.Formatting.None));
            yield return c.Frames(3);
            yield return c.Call(new { cmd = "ik.list", key, exact = true });
            var l1 = c.J;
            var solve = l1["solve"];
            double maxRes = double.NaN;
            if (c.Check(c.Last.Ok && (bool?)l1["ikEnabled"] == true && solve?["ran"]?.Value<bool>() == true, "ik.list after 3 frames: IK on, the solve ran", AcceptanceCtx.Short(c.Last?.ToString())))
            {
                var res = solve["residual"] as JObject;
                var clamped = new HashSet<string>((solve["clamped"] as JArray ?? new JArray()).Select(x => (string)x));
                var ends = Ik1Ends.Where(n => res?[n] != null && !clamped.Contains(n)).ToList();
                maxRes = ends.Count == 0 ? double.NaN : ends.Max(n => (double)res[n]);
                c.Check(ends.Count > 0 && maxRes <= 1e-4, "every reachable hand / foot residual ≤ 1e-4",
                    $"max {maxRes.ToString("G4", CultureInfo.InvariantCulture)} over {string.Join(", ", ends)}; clamped {string.Join(", ", clamped)}; torso {solve["torsoResidualDeg"]} deg; {solve["ms"]} ms");
                c.Check((l1["owned"] as JArray)?.Count > 0 && (l1["targets"] as JArray)?.All(t => t["stored"]?.Value<bool>() == true) == true, "owned bones listed, the 13 targets stored");
            }
            yield return c.Call(new { cmd = "bones.dump", key, root = "body" });
            var b0 = c.J;
            // B1 after 10 more frames (the 3 → 13 frame change is reported), then a paused character under IK holds its pose
            yield return c.Frames(10);
            yield return c.Call(new { cmd = "bones.dump", key, root = "body" });
            var b1 = c.J;
            c.Check(c.Last.Ok && (b1["bones"] as JArray)?.Count > 100, "bones.dump body after 10 more frames → B1", (b1["bones"] as JArray)?.Count + " bones");
            Ik1CompareBones(c, b0, b1, "frames 3 → 13 (settling after the load)", info: true);
            yield return c.Frames(5);
            yield return c.Call(new { cmd = "bones.dump", key, root = "body" });
            Ik1CompareBones(c, b1, c.J, "paused: bones.dump body after 5 more frames within 1e-4 / 0.01° of B1");
            yield return c.Call(new { cmd = "camera.frame", key });
            yield return c.Frames(2);
            yield return c.Call(new { cmd = "capture", name = "ik1_pose" });
            string shot = (string)c.J["path"];
            if (c.CheckOk("capture ik1_pose"))
            {
                var (w, h, fg, _) = Png(shot, null);
                c.Check(w > 0 && h > 0 && fg >= 0.01, "ik1_pose: PNG with a foreground ≥ 1 %", $"{w} × {h}, foreground {fg:P1}");
            }
            c.Info("IK1 numbers: maxResidual " + maxRes.ToString("R", CultureInfo.InvariantCulture) + ", targets " + Ik1Digest(l1["targets"], "pos", "rot")
                + ", owned bones " + Ik1Digest(Ik1Owned(b1, l1), "lp", "lq") + " (" + Ik1Owned(b1, l1).Count + "), body bones " + Ik1Digest(b1["bones"], "lp", "lq")
                + ", residuals " + solve?["residual"]?.ToString(Newtonsoft.Json.Formatting.None));

            // save → load: the same targets and pose
            yield return c.Call(new { cmd = "scene.save", name = "ik1_scene", overwrite = true });
            c.CheckOk("scene.save ik1_scene");
            yield return c.Call(new { cmd = "scene.new" });
            yield return c.Call(new
            {
                cmd = "batch",
                cmds = new object[]
                {
                    new { cmd = "scene.load", name = "ik1_scene", wait = false },
                    new { cmd = "chara.motion", key, paused = true, physics = false },
                },
            });
            c.CheckOk("batch [scene.load ik1_scene, chara.motion paused]");
            yield return c.Frames(13);
            yield return c.Call(new { cmd = "ik.list", key, exact = true });
            var l2 = c.J;
            double maxT = 0;
            var t1 = l1["targets"] as JArray;
            var t2 = l2["targets"] as JArray;
            bool tOk = c.Last.Ok && t1 != null && t2 != null && t1.Count == 13 && t2.Count == 13;
            for (int i = 0; tOk && i < 13; i++)
                foreach (var m in new[] { "pos", "rot" })
                    for (int k = 0; k < 3; k++)
                    {
                        var a = t1[i][m]; var b = t2[i][m];
                        if (!(a is JArray) || !(b is JArray)) { tOk = false; break; }
                        maxT = Math.Max(maxT, Math.Abs((double)a[k] - (double)b[k]));
                    }
            c.Check(tOk && maxT <= 1e-6 && l2["solve"]?["ran"]?.Value<bool>() == true, "after scene.load: the same ik.list targets (1e-6), the solve runs", $"max {maxT:G3}");
            yield return c.Call(new { cmd = "bones.dump", key, root = "body" });
            var b2 = c.J;
            Ik1CompareBones(c, b1, b2, "after scene.load: bones.dump body within 1e-4 / 0.01°");
            yield return c.Call(new { cmd = "scene.new" });
        }

        /// <summary>The bones.dump entries of the bones ik.list names as owned (the solved set), in dump order.</summary>
        static JArray Ik1Owned(JObject dump, JObject list)
        {
            var owned = new HashSet<string>((list?["owned"] as JArray ?? new JArray()).Select(x => (string)x));
            return new JArray((dump?["bones"] as JArray ?? new JArray()).Where(b => owned.Contains((string)b["name"])).Select(b => b.DeepClone()));
        }

        /// <summary>FNV-1a 64 over the exact doubles of members <paramref name="m1"/> / <paramref name="m2"/> of every entry (the U10 digest).</summary>
        static string Ik1Digest(JToken list, string m1, string m2)
        {
            ulong h = 14695981039346656037UL;
            void Add(double d)
            {
                long bits = BitConverter.DoubleToInt64Bits(d);
                for (int i = 0; i < 8; i++) { h ^= (byte)(bits >> (8 * i)); h *= 1099511628211UL; }
            }
            foreach (var e in list as JArray ?? new JArray())
                foreach (var m in new[] { m1, m2 })
                    if (e[m] is JArray a) foreach (var x in a) Add((double)x);
                    else Add(double.NaN);
            return h.ToString("x16");
        }

        /// <summary>bones.dump B2 against B1 by path: local position within 1e-4, local rotation within 0.01°.</summary>
        static void Ik1CompareBones(AcceptanceCtx c, JObject b1, JObject b2, string what, bool info = false)
        {
            var m1 = new Dictionary<string, JToken>();
            foreach (var b in b1?["bones"] as JArray ?? new JArray()) m1[(string)b["path"] ?? (string)b["name"]] = b;
            int n = 0, missing = 0;
            double maxP = 0, maxA = 0;
            string worstP = null, worstA = null;
            foreach (var b in b2?["bones"] as JArray ?? new JArray())
            {
                string p = (string)b["path"] ?? (string)b["name"];
                if (!m1.TryGetValue(p, out var a)) { missing++; continue; }
                n++;
                var pa = a["lp"] as JArray; var pb = b["lp"] as JArray;
                var qa = a["lq"] as JArray; var qb = b["lq"] as JArray;
                if (pa == null || pb == null || qa == null || qb == null) { missing++; continue; }
                double dp = Math.Sqrt(Enumerable.Range(0, 3).Sum(k => Math.Pow((double)pa[k] - (double)pb[k], 2)));
                // the angle between the two rotations, robust for equal non-unit float quaternions (2·acos(|q·q|) reads
                // 0.04° for a bit-equal pair whose norm is 1 − 6e-8): 2·atan2(|a − s b|, |a + s b|), s = sign(a·b)
                double sgn = Enumerable.Range(0, 4).Sum(k => (double)qa[k] * (double)qb[k]) < 0 ? -1 : 1;
                double dm = Math.Sqrt(Enumerable.Range(0, 4).Sum(k => Math.Pow((double)qa[k] - sgn * (double)qb[k], 2)));
                double dpl = Math.Sqrt(Enumerable.Range(0, 4).Sum(k => Math.Pow((double)qa[k] + sgn * (double)qb[k], 2)));
                double ang = 2 * Math.Atan2(dm, dpl) * 180 / Math.PI;
                if (dp > maxP) { maxP = dp; worstP = p; }
                if (ang > maxA) { maxA = ang; worstA = p; }
            }
            string detail = $"{n} bones, {missing} unmatched, max {maxP:G3} ({worstP}) / {maxA:G3}° ({worstA})";
            if (info) c.Info("IK1: " + what + ": " + detail);
            else c.Check(n > 100 && missing == 0 && maxP <= 1e-4 && maxA <= 0.01, what, detail);
        }
    }
}
