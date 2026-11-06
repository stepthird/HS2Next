// AdvancedPoseTest, the bone corrections (Phase 6.3 package M). Two characters of one card, one
// with the corrections and its twin without (the way of BridgeCoreTest C13), stepped by hand in lockstep with physics off.
//   AB1  every channel on the test bones of each class, the clip at 3 times, FK off and on, IK on: local = the twin's local with the
//        correction composed (1e-5, 0.01 deg); every other bone equals the twin
//   AB2  clearing returns each bone to the twin's value (1e-6, 0.001 deg, angle in doubles); no pose stage left
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Motion;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class AdvancedPoseTest
{
    static void Headless_M_Bones()
    {
        Group("AB1", "input composed with the correction", AB1);
        Group("AB2", "clearing returns the bone", AB2);
    }

    // ------------------------------------------------------------ the fixture

    sealed class Twins
    {
        public Host H;
        public string A, B;
        public CharaRealization RA => H.Rt.FindChara(A);
        public CharaRealization RB => H.Rt.FindChara(B);
        public void Dispose() => H?.Dispose();

        /// <summary>Applies pending model changes, then n steps of both characters.</summary>
        public void Step(int n = 1, float dt = Dt)
        {
            H.Rt.ApplyNow();
            for (int i = 0; i < n; i++) { RA.Motion.Step(dt); RB.Motion.Step(dt); }
        }
    }

    /// <summary>Two characters of one card at the origin, stepped by hand, physics off, the idle row at speed 0.</summary>
    static Twins NewTwins(string card, string sex, string what, bool physics = false)
    {
        var f = new Twins { H = NewHost() };
        f.A = Key(f.H.Ok(new { cmd = "chara.load", path = card, sex }, what + ": chara.load A"));
        f.B = Key(f.H.Ok(new { cmd = "chara.load", path = card, sex }, what + ": chara.load B"));
        foreach (var k in new[] { f.A, f.B })
        {
            f.H.Ok(new { cmd = "chara.anime", key = k, group = 19, category = 0, no = 0, time = 0.5f, speed = 0f }, what + ": chara.anime idle");
            f.H.Ok(new { cmd = "chara.motion", key = k, autoStep = false, physics, reset = true }, what + ": chara.motion by hand");
        }
        return f;
    }

    static BoneCorrection Corr(Vec3? pos = null, Vec3? rot = null, Vec3? scale = null) =>
        new BoneCorrection(pos ?? new Vec3(0f, 0f, 0f), rot ?? new Vec3(0f, 0f, 0f), scale ?? new Vec3(1f, 1f, 1f));

    static SortedDictionary<string, BoneCorrection> Map(params (string address, BoneCorrection value)[] entries)
    {
        var m = new SortedDictionary<string, BoneCorrection>(StringComparer.Ordinal);
        foreach (var (a, v) in entries) m[a] = v;
        return m;
    }

    /// <summary>The model's bone map replaced (no command: the commands have their own checks), applied.</summary>
    static void SetBones(Host h, string id, SortedDictionary<string, BoneCorrection> map)
    {
        var res = h.Session.Set(id, "chara.advancedPose.bones", map);
        if (!res.Ok) Check("set chara.advancedPose.bones", false, res.ToString());
        h.Rt.ApplyNow();
    }

    /// <summary>The angle between two rotations in degrees, computed in doubles.</summary>
    static double Angle(Quaternion a, Quaternion b)
    {
        double dot = (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z + (double)a.w * b.w;
        double na = (double)a.x * a.x + (double)a.y * a.y + (double)a.z * a.z + (double)a.w * a.w;
        double nb = (double)b.x * b.x + (double)b.y * b.y + (double)b.z * b.z + (double)b.w * b.w;
        return 2 * Math.Acos(Math.Min(1, Math.Abs(dot) / Math.Sqrt(na * nb))) * 180 / Math.PI;
    }

    static double Dist(Vector3 a, Vector3 b) => Math.Max(Math.Abs((double)a.x - b.x), Math.Max(Math.Abs((double)a.y - b.y), Math.Abs((double)a.z - b.z)));

    static Transform Bone(CharaRealization r, string name) => AdvancedPoseResolver.Resolve(r, name);

    /// <summary>A's bone against B's local values with the correction composed on the shown channels; returns the worst numbers.</summary>
    static void Composed(CharaRealization ra, CharaRealization rb, string bone, BoneCorrection c, bool scaleXShown, ref double worstPos, ref double worstRot, ref double worstScale, ref string worst)
    {
        var a = Bone(ra, bone); var b = Bone(rb, bone);
        if (!a || !b) return;
        var pos = Dist(a.localPosition, b.localPosition + UnityObjects.V(c.Pos));
        var q = b.localRotation * Quaternion.Euler(UnityObjects.V(c.Rot));
        var rot = Angle(a.localRotation, q);
        var s = Vector3.Scale(b.localScale, UnityObjects.V(c.Scale));
        var als = a.localScale;
        if (!scaleXShown) { s.x = 0f; als.x = 0f; }
        var sc = Dist(als, s);
        if (pos > worstPos) { worstPos = pos; worst = bone; }
        if (rot > worstRot) { worstRot = rot; worst = bone; }
        if (sc > worstScale) { worstScale = sc; worst = bone; }
    }

    // ------------------------------------------------------------ AB1

    /// <summary>The bones AB1 corrects (the later writers of a frame have AB12).</summary>
    static readonly (string bone, string cls)[] AB1Bones =
    {
        ("cf_J_Hand_Index02_L", "bound"), ("cf_J_Hand_Thumb03_R", "bound"), ("cf_J_Spine02", "bound"), ("cf_J_ArmUp00_R", "bound"), ("cf_J_Foot02_L", "bound"),
        ("cf_J_Hips", "hips"),
        ("cf_J_MouthMove", "face"), ("cf_J_Mouth_L", "face"),
        ("cf_J_NoseBase_s", "shape"), ("cf_J_EarBase_s_L", "shape"), ("cf_J_Spine01_s", "shape"),
    };

    static readonly (string name, BoneCorrection c)[] AB1Variants =
    {
        ("pos", Corr(pos: new Vec3(0.01f, -0.02f, 0.015f))),
        ("rot", Corr(rot: new Vec3(12f, -20f, 7f))),
        ("scale", Corr(scale: new Vec3(1.1f, 0.85f, 1.2f))),
        ("all", Corr(new Vec3(-0.02f, 0.01f, 0.005f), new Vec3(-8f, 15f, 30f), new Vec3(0.9f, 1.15f, 1.05f))),
    };

    static void AB1()
    {
        var f = NewTwins(femaleRel, "female", "AB1");
        try
        {
            foreach (var (bone, _) in AB1Bones)
                Check("the test bone exists on both: " + bone, Bone(f.RA, bone) && Bone(f.RB, bone));
            var modes = new (string name, Action<string> on)[]
            {
                ("FK and IK off", k => { }),
                ("FK on", k =>
                {
                    f.H.Ok(new { cmd = "fk.enable", key = k, on = true }, "fk.enable");
                    f.H.Ok(new { cmd = "fk.set", key = k, bones = new Dictionary<string, float[]> { ["cf_J_ArmUp00_R"] = new[] { 5f, 10f, 15f }, ["cf_J_Spine02"] = new[] { 3f, 0f, -4f } } }, "fk.set");
                }),
                ("IK on", k =>
                {
                    f.H.Ok(new { cmd = "fk.enable", key = k, on = false }, "fk.enable off");
                    f.H.Ok(new { cmd = "ik.enable", key = k, on = true }, "ik.enable");
                }),
            };
            foreach (var (mode, on) in modes)
            {
                on(f.A); on(f.B);
                f.Step(2);
                foreach (var time in new[] { 0.1f, 0.5f, 0.9f })
                {
                    foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "chara.anime", key = k, time, speed = 0f }, $"chara.anime time {time}");
                    f.Step(2);
                    foreach (var (vname, c) in AB1Variants)
                    {
                        SetBones(f.H, f.A, Map(AB1Bones.Select(x => (x.bone, c)).ToArray()));
                        double wp = 0, wr = 0, ws = 0; string w = "";
                        // at once (the same request's dump shows it), then after steps
                        foreach (var (bone, _) in AB1Bones) Composed(f.RA, f.RB, bone, c, bone != "cf_J_MouthMove", ref wp, ref wr, ref ws, ref w);
                        f.Step(3);
                        foreach (var (bone, _) in AB1Bones) Composed(f.RA, f.RB, bone, c, bone != "cf_J_MouthMove", ref wp, ref wr, ref ws, ref w);
                        Check($"{mode}, time {time}, {vname}: composed", wp <= 1e-5 && wr <= 0.01 && ws <= 1e-5, $"pos {wp:G3} rot {wr:G3} scale {ws:G3} ({w})");
                        SetBones(f.H, f.A, Map());
                        f.Step(1);
                    }
                }
            }
            SetBones(f.H, f.A, Map());
        }
        finally { f.Dispose(); }

        // the bones nobody corrects, on a fresh pair (the eye look keeps a state, and the corrections above moved the head it aims from):
        // finger and face corrections reach no other stage's input
        var e = NewTwins(femaleRel, "female", "AB1 others");
        try
        {
            e.Step(2);
            var few = Map(("cf_J_Hand_Index02_L", AB1Variants[3].c), ("cf_J_NoseBase_s", AB1Variants[3].c), ("cf_J_Mouth_L", AB1Variants[3].c));
            SetBones(e.H, e.A, few);
            e.Step(5);
            var da = e.H.Ok(new { cmd = "bones.dump", key = e.A, root = "all" }, "bones.dump A");
            var db = e.H.Ok(new { cmd = "bones.dump", key = e.B, root = "all" }, "bones.dump B");
            var ba = da["bones"] as JArray; var bb = db["bones"] as JArray;
            int diff = 0; var first = new List<string>();
            var corrected = new HashSet<string>(few.Keys);
            for (int i = 0; ba != null && bb != null && i < ba.Count && i < bb.Count; i++)
            {
                string name = (string)ba[i]["name"];
                if (corrected.Contains(name)) continue;
                if (JToken.DeepEquals(ba[i], bb[i])) continue;
                diff++;
                if (first.Count < 6) first.Add(name);
            }
            Check("every other bone equals the twin value for value", ba != null && bb != null && ba.Count == bb.Count && ba.Count > 700 && diff == 0,
                $"{ba?.Count} / {bb?.Count} entries, {diff} different" + (first.Count > 0 ? ": " + string.Join(", ", first) : ""));
        }
        finally { e.Dispose(); }

        // the other sex, one row
        var g = NewTwins(maleRel, "male", "AB1 male");
        try
        {
            g.Step(2);
            var c = AB1Variants[3].c;
            var bones = AB1Bones.Where(x => Bone(g.RA, x.bone)).ToList();
            Check("the test bones on the male target", bones.Count == AB1Bones.Length, bones.Count + " of " + AB1Bones.Length);
            SetBones(g.H, g.A, Map(bones.Select(x => (x.bone, c)).ToArray()));
            g.Step(3);
            double wp = 0, wr = 0, ws = 0; string w = "";
            foreach (var (bone, _) in bones) Composed(g.RA, g.RB, bone, c, bone != "cf_J_MouthMove", ref wp, ref wr, ref ws, ref w);
            Check("male: composed", wp <= 1e-5 && wr <= 0.01 && ws <= 1e-5, $"pos {wp:G3} rot {wr:G3} scale {ws:G3} ({w})");
        }
        finally { g.Dispose(); }
    }

    // ------------------------------------------------------------ AB2

    static void AB2()
    {
        var f = NewTwins(femaleRel, "female", "AB2");
        try
        {
            var before = f.RA.Motion.Stages.Select(s => s.Name).ToList();
            foreach (var time in new[] { 0.2f, 0.7f })
            {
                foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "chara.anime", key = k, time, speed = 0f }, $"chara.anime time {time}");
                f.Step(2);
                SetBones(f.H, f.A, Map(AB1Bones.Select(x => (x.bone, AB1Variants[3].c)).ToArray()));
                f.Step(10);
                SetBones(f.H, f.A, Map());
                Same(f, $"time {time}: right after clearing");
                f.Step(1);
                Same(f, $"time {time}: one step later");
                f.Step(30);
                Same(f, $"time {time}: 30 steps later");
            }
            var after = f.RA.Motion.Stages.Select(s => s.Name).ToList();
            Check("no pose stage left", !after.Any(n => n.StartsWith("pose.", StringComparison.Ordinal)) && after.SequenceEqual(before), string.Join(",", after));
        }
        finally { f.Dispose(); }
    }

    static void Same(Twins f, string what)
    {
        double wp = 0, wr = 0, ws = 0; string w = "";
        foreach (var (bone, _) in AB1Bones)
        {
            var a = Bone(f.RA, bone); var b = Bone(f.RB, bone);
            var p = Dist(a.localPosition, b.localPosition); var r = Angle(a.localRotation, b.localRotation); var s = Dist(a.localScale, b.localScale);
            if (p > wp) { wp = p; w = bone; }
            if (r > wr) { wr = r; w = bone; }
            if (s > ws) { ws = s; w = bone; }
        }
        Check(what + ": the bones equal the twin", wp <= 1e-6 && wr <= 0.001 && ws <= 1e-6, $"pos {wp:G3} rot {wr:G3} scale {ws:G3} ({w})");
    }
}
