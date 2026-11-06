// ModifierOrderTest, the other writers of a frame (Phase 6.3a package M):
//   FM4   the hips offset, the neck patterns, FK on and off, IK on a length-modified arm
//   FM5   the mouth width bone in edit frames, a synthetic modifier and the cards of the local list (real frames: FM5P in AdvancedPoseTest.play)
//   FM6   heels: the joint's scale with IK off and on, shoes off, a row switch; the channels and joints without a value
//   FM7   corrections over modifiers on one bone, a bind, clearing
//   FM8   a probe keeps every state and reads the modified pose
//   FM9   settle, the global pause, the character's own pause
//   FM10  a flush: a body slider, the suppressed set, a removal, the final stage again
//   FM11  character thumbnails: the scope, its end, a stop and a read in one request
//   FM12  a Timeline's FK tracks on a modified character
// Cards by hash; numbers only; no image is opened.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HS2Next.Chara.Ext;
using HS2Next.Content;
using HS2Next.Motion;
using HS2Next.Motion.Face;
using HS2Next.Motion.Heels;
using HS2Next.Motion.Modifiers;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class AdvancedPoseTest
{
    static void Modifier_N_Writers()
    {
        Group("FM4", "other writers: no drift, the sums, FK off, IK", FM4);
        Group("FM5", "the mouth width bone, edit frames", FM5);
        Group("FM6", "heels", FM6);
        Group("FM7", "corrections over modifiers on one bone", FM7);
        Group("FM8", "a probe", FM8);
        Group("FM9", "settle and pause", FM9);
        Group("FM10", "a flush", FM10);
        Group("FM11", "character thumbnails", FM11);
        Group("FM12", "a Timeline's FK tracks", FM12);
        Group("FI12", "one stale finding names every list", FI12);
        Group("FM15R", "the pass's time, a reference without a bar", FM15R);
    }

    // ------------------------------------------------------------ the local list of cards

    /// <summary>The cards of a group of the local list (Cache/compare_mods/cards.json, not in git); empty with a warning when it is absent.</summary>
    static List<(string hash, string rel, string sex)> ModCards(string group)
    {
        var res = new List<(string, string, string)>();
        string path = Path.Combine(ContentSettings.ProjectRoot, "Cache", "compare_mods", "cards.json");
        if (!File.Exists(path)) { Warn("no local list of modifier cards (Cache/compare_mods/cards.json): group " + group + " not run"); return res; }
        var d = JObject.Parse(File.ReadAllText(path));
        foreach (var c in d["cards"].OfType<JObject>())
            if (c["groups"] is JArray g && g.Any(x => (string)x == group)) res.Add(((string)c["hash"], (string)c["rel"], (string)c["sex"]));
        if (res.Count == 0) Warn("the local list has no group " + group);
        return res;
    }

    static Vector3 Scaled(Vector3 a, Vector3 b) => Vector3.Scale(a, b);

    static Twins NewCardTwin(string card, string sex, string what)
    {
        var f = new Twins { H = ReconHost() };
        f.A = Key(f.H.Ok(new { cmd = "chara.load", path = card, sex }, what + ": the card"));
        f.B = f.A;
        f.H.Ok(new { cmd = "chara.motion", key = f.A, autoStep = false, physics = false, look = false, reset = true }, what + ": chara.motion by hand");
        f.H.Rt.ApplyNow();
        return f;
    }

    // ------------------------------------------------------------ FM4

    static void FM4()
    {
        FM4Hips();
        FM4Neck();
        FM4Fk();
        FM4Ik();
    }

    static void FkOn(Twins f, float offset, params string[] groups)
    {
        foreach (var k in new[] { f.A, f.B })
        {
            f.H.Ok(new { cmd = "fk.enable", key = k, on = true }, "FK on");
            f.H.Ok(new { cmd = "fk.enable", key = k, groups, on = true }, "FK groups");
            if (offset != 0f) f.H.Ok(new { cmd = "fk.set", key = k, hipsOffset = offset }, "the hips offset");
        }
    }

    static void FM4Hips()
    {
        var f = NewBareTwins(femaleRel, "female", "FM4 hips");
        try
        {
            const float H = 0.3f, L = 1.05f;
            var P = new Vector3(0.02f, 0.05f, -0.01f);
            var ha = Bone(f.RA, "cf_J_Hips"); var hb = Bone(f.RB, "cf_J_Hips");
            Applier(f.RA).Add(new BoneModifierApplier.Modifier { Target = ha, Bone = "cf_J_Hips", Source = "test", Location = 1, Position = P, Length = L });
            BoneModifierSetup.Sync(f.RA.Context);
            RowOn(f, ReconRows[0], 0f, 1f);
            f.Step(2, 0f);
            var without = hb.localPosition;
            FkOn(f, H, "Body");
            f.Step(2, 0f);
            Check("the offset applies on the twin", Mathf.Abs(hb.localPosition.y - without.y - H) <= 1e-3f, $"{hb.localPosition.y} against {without.y}");
            Vector3 Up(Vector3 v, float h) => new Vector3(v.x, v.y + h, v.z);
            Vector3 Expect() => Up(Up(hb.localPosition, -H) * L + P, H);
            double worst = 0; int steps = 0;
            for (int i = 0; i < 600; i++)
            {
                f.Step(1, Dt);
                worst = Math.Max(worst, Dist(ha.localPosition, Expect()));
                steps++;
            }
            Check("600 steps of a clip: base x length + position + offset", worst <= 1e-4, $"worst {worst:G3} over {steps} steps");
            RowOn(f, ReconRows[0], 0.5f, 0f);
            f.Step(2, 0f);
            var at = ha.localPosition;
            double drift = 0, off = 0;
            for (int i = 0; i < 600; i++) { f.Step(1, 0f); drift = Math.Max(drift, Dist(ha.localPosition, at)); off = Math.Max(off, Dist(ha.localPosition, Expect())); }
            Check("600 steps at 0: no drift", drift <= 1e-6 && off <= 1e-4, $"drift {drift:G3}, off the sum {off:G3}");
            Measure($"FM4 hips: running {worst:G3}, still drift {drift:G3}");
            foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "fk.enable", key = k, on = false }, "FK off");
            f.Step(1, 0f);
            double d0 = Dist(ha.localPosition, hb.localPosition * L + P);
            Check("FK off: base x length + position at once", d0 <= 1e-4, $"{d0:G3}");
        }
        finally { f.Dispose(); }

        // no clip: the two sum without feeding each other
        var g = NewBareTwins(femaleRel, "female", "FM4 hips, no clip");
        try
        {
            const float H = 0.3f;
            var P = new Vector3(0f, 0.05f, 0f);
            var ha = Bone(g.RA, "cf_J_Hips"); var hb = Bone(g.RB, "cf_J_Hips");
            Applier(g.RA).Add(new BoneModifierApplier.Modifier { Target = ha, Bone = "cf_J_Hips", Source = "test", Location = 1, Position = P });
            BoneModifierSetup.Sync(g.RA.Context);
            foreach (var k in new[] { g.A, g.B }) g.H.Session.Set(k, "chara.anime.ref", null);
            g.Step(2, 0f);
            var beforeA = ha.localPosition; var beforeB = hb.localPosition;
            FkOn(g, H, "Body");
            g.Step(2, 0f);
            var at = ha.localPosition;
            double drift = 0;
            for (int i = 0; i < 600; i++) { g.Step(1, 0f); drift = Math.Max(drift, Dist(ha.localPosition, at)); }
            Check("no clip: the modifier and the offset sum, no growth over 600 steps", drift <= 1e-6 && Dist(ha.localPosition, hb.localPosition + P) <= 1e-4, $"drift {drift:G3}; off {Dist(ha.localPosition, hb.localPosition + P):G3}; hips {ha.localPosition:F4}, twin {hb.localPosition:F4}, before FK {beforeA:F4} / {beforeB:F4}");
        }
        finally { g.Dispose(); }
    }

    static void FM4Neck()
    {
        foreach (var (ptn, pname) in new[] { (0, "forward"), (1, "camera"), (3, "animation"), (4, "fix") })
        {
            var f = NewBareTwins(femaleRel, "female", "FM4 neck " + pname);
            try
            {
                var na = Bone(f.RA, "cf_J_Neck"); var nb = Bone(f.RB, "cf_J_Neck");
                var P = new Vector3(0f, 0.1f, 0f); var S = new Vector3(1.05f, 1.1f, 0.95f); const float L = 1.08f;
                var R = new Vector3(6f, -4f, 3f);
                Applier(f.RA).Add(new BoneModifierApplier.Modifier { Target = na, Bone = "cf_J_Neck", Source = "test", Location = 1, Position = P, Scale = S, Length = L, Rotation = R });
                BoneModifierSetup.Sync(f.RA.Context);
                RowOn(f, ReconRows[0], 0f, 1f);
                foreach (var k in new[] { f.A, f.B })
                {
                    f.H.Ok(new { cmd = "chara.motion", key = k, look = true }, "the look on");
                    f.H.Ok(new { cmd = "chara.face", key = k, neckLook = ptn }, "the neck pattern " + pname);
                }
                f.Step(30, Dt);
                double wp = 0, ws = 0;
                for (int i = 0; i < 300; i++)
                {
                    f.Step(1, Dt);
                    wp = Math.Max(wp, Dist(na.localPosition, nb.localPosition * L + P));
                    ws = Math.Max(ws, Dist(na.localScale, Scaled(nb.localScale, S)));
                }
                Check(pname + ": 300 steps: position and scale composed on the frame's values", wp <= 1e-4 && ws <= 1e-4, $"pos {wp:G3} scale {ws:G3}");
                RowOn(f, ReconRows[0], 0.5f, 0f);
                f.Step(30, 0f);
                var at = Local(na);
                var w = new Worst();
                for (int i = 0; i < 600; i++) { f.Step(1, 0f); if (i % 50 == 49) w.Add(Local(na), at, pname); }
                Check(pname + ": 600 steps at 0: no drift", w.Ok(1e-6, 0.01, 1e-6), w.ToString());
                Measure($"FM4 neck {pname}: running pos {wp:G3} scale {ws:G3}; still {w}");
            }
            finally { f.Dispose(); }
        }
    }

    static void FM4Fk()
    {
        string[] bones = { "cf_J_ArmUp00_R", "cf_J_Spine02" };
        foreach (var noClip in new[] { false, true })
        {
            string what = noClip ? "no clip on the channel" : "a clip's channel";
            var f = NewBareTwins(femaleRel, "female", "FM4 FK");
            try
            {
                AddAll(f.RA, bones);
                if (noClip) foreach (var k in new[] { f.A, f.B }) f.H.Session.Set(k, "chara.anime.ref", null);
                else RowOn(f, ReconRows[0]);
                f.Step(2, 0f);
                FkOn(f, 0f, "Body");
                var values = new Dictionary<string, float[]> { ["cf_J_ArmUp00_R"] = new[] { 5f, 10f, 15f }, ["cf_J_Spine02"] = new[] { 3f, 0f, -4f } };
                foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "fk.set", key = k, bones = values }, "FK values");
                f.Step(2, 0f);
                var w = new Worst();
                for (int i = 0; i < 600; i++)
                {
                    f.Step(1, 0f);
                    if (i % 50 != 49) continue;
                    foreach (var b in bones)
                    {
                        var a = Local(Bone(f.RA, b)); var t = WithMods(Local(Bone(f.RB, b)));
                        w.Add((a.p, a.r, a.s), (t.p, Bone(f.RB, b).localRotation, t.s), b);
                    }
                }
                Check(what + ": FK on, 600 steps: FK wins the rotation, the modifier keeps position and scale, no drift", w.Ok(1e-4, 0.01, 1e-4), w.ToString());
                foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "fk.enable", key = k, on = false }, "FK off");
                f.Step(1, 0f);
                var w0 = new Worst();
                AgainstTwin(f, bones, w0, "FK off");
                Check(what + ": FK off: base and modifier at once", w0.Ok(1e-4, 0.01, 1e-4), w0.ToString());
                f.Step(60, 0f);
                var w1 = new Worst();
                AgainstTwin(f, bones, w1, "60 steps after");
                Check(what + ": 60 steps after: no drift", w1.Ok(1e-4, 0.01, 1e-4), w1.ToString());
            }
            finally { f.Dispose(); }
        }
    }

    static void FM4Ik()
    {
        var f = NewBareTwins(femaleRel, "female", "FM4 IK");
        try
        {
            const float L = 1.15f;
            var la = Bone(f.RA, "cf_J_ArmLow01_R"); var lb = Bone(f.RB, "cf_J_ArmLow01_R");
            Applier(f.RA).Add(new BoneModifierApplier.Modifier { Target = la, Bone = "cf_J_ArmLow01_R", Source = "test", Location = 1, Length = L });
            BoneModifierSetup.Sync(f.RA.Context);
            RowOn(f, ReconRows[0]);
            f.Step(2, 0f);
            Check("the lengthened bone before IK", Dist(la.localPosition, lb.localPosition * L) <= 1e-4, $"{Dist(la.localPosition, lb.localPosition * L):G3}");
            foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "ik.enable", key = k, on = true }, "IK on");
            f.Step(2, 0f);
            var ca = IkDriver.Of(f.RA); var cb = IkDriver.Of(f.RB);
            if (!Check("IK runs on both", ca != null && cb != null && ca.Running && cb.Running)) return;
            var handA = ca.Bones.Sources[(int)HS2Next.Motion.Ik.IkTargetId.RightHand]; var handB = cb.Bones.Sources[(int)HS2Next.Motion.Ik.IkTargetId.RightHand];
            // the same target for both, in reach of the longer and the shorter arm, in each one's root frame
            var target = f.RB.Root.transform.InverseTransformPoint(handB.position) + new Vector3(0.1f, 0.2f, 0.1f);
            foreach (var k in new[] { f.A, f.B })
                f.H.Ok(new { cmd = "ik.set", key = k, targets = new Dictionary<string, object> { ["RightHand"] = new { pos = new[] { target.x, target.y, target.z } } } }, "the hand's target");
            double worstA = 0, worstB = 0, moved = 0;
            var first = Vector3.zero;
            for (int i = 0; i < 600; i++)
            {
                f.Step(1, 0f);
                worstA = Math.Max(worstA, (f.RA.Root.transform.InverseTransformPoint(handA.position) - target).magnitude);
                worstB = Math.Max(worstB, (f.RB.Root.transform.InverseTransformPoint(handB.position) - target).magnitude);
                if (i == 0) first = la.localPosition; else moved = Math.Max(moved, Dist(la.localPosition, first));
            }
            Check("600 steps: the length-modified arm's hand on its target as the twin's (within 2e-3)", worstA <= Math.Max(worstB, 2e-3), $"modified {worstA:G3}, twin {worstB:G3}");
            Check("600 steps: the modified bone's length does not grow", moved <= 1e-5, $"{moved:G3}");
            Measure($"FM4 IK: hand off its target: modified {worstA:G3}, twin {worstB:G3}; the bone's position moved {moved:G3}");
            foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "ik.enable", key = k, on = false }, "IK off");
            f.Step(2, 0f);
            double off = Dist(la.localPosition, lb.localPosition * L);
            Check("IK off: the twin's value with the length", off <= 1e-4, $"{off:G3}");
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ FM5

    static Vector3 BareScale(CharaRealization r, Transform t)
    {
        var v = t.localScale;
        r.Context.Bones.Bare(() => v = t.localScale);
        return v;
    }

    static Vector3 ModScale(CharaRealization r, Transform t)
    {
        var s = Vector3.one;
        foreach (var m in r.Context.Bones.Modifiers) if (m.Target == t) s = Vector3.Scale(s, m.Scale);
        return s;
    }

    /// <summary>The recon's sequence on the mouth width bone in edit frames: x, y, z hold the modifier once throughout (the modifier owns the bone's scale while it has a scale value).</summary>
    static void MouthSequence(Host h, string k, string who, BoneCorrection c)
    {
        var r = h.Rt.FindChara(k);
        var t = Bone(r, "cf_J_MouthMove");
        var face = r.Motion.Get<ExpressionStage>();
        var ms = ModScale(r, t);
        var cs = c != null ? UnityObjects.V(c.Scale) : Vector3.one;
        double worst = 0;
        string where = "";
        void Look(string what)
        {
            var want = Scaled(Scaled(BareScale(r, t), ms), cs);
            if (c != null) { h.Rt.ApplyNow(); }
            double d = Dist(t.localScale, want);
            if (d > worst) { worst = d; where = what; }
        }
        void Frames(int n) { for (int i = 0; i < n; i++) r.Motion.Step(Dt); }
        if (c != null) { SetBones(h, k, Map(("cf_J_MouthMove", c))); }
        Frames(2);
        Look("at rest");
        Frames(600);
        Look("600 frames");
        face.InvalidateMouthWidth();
        Frames(3);
        Look("the width written again");
        r.Motion.ResetAll();
        Frames(3);
        Look("a reset");
        r.Motion.ResetAll();
        Frames(3);
        Look("another reset");
        float was = face.VoiceValue;
        face.VoiceValue = 0.8f;
        for (int i = 0; i < 60; i++) { Frames(1); Look("speech frame " + i); }
        face.VoiceValue = was;
        Frames(3);
        Look("after speech");
        h.Call(new { cmd = "shape.set", key = k, faceIndex = 5, faceValue = 0.7f });
        h.Rt.ApplyNow();
        Frames(3);
        Look("a face slider");
        using (StudioDefaultPoseScope.Enter(r)) { }
        h.Call(new { cmd = "chara.motion", key = k, autoStep = false, physics = false, look = false });
        h.Rt.ApplyNow();
        Frames(3);
        Look("a thumbnail scope");
        Check($"{who}{(c != null ? ", a correction on the same bone" : "")}: x, y, z once through the sequence", worst <= 1e-5, $"worst {worst:G3} ({where}); value {t.localScale.x:F4}, {t.localScale.y:F4}, {t.localScale.z:F4}");
        if (c != null) { SetBones(h, k, Map()); Frames(2); Check(who + ": the correction cleared: the modifier once", Dist(t.localScale, Scaled(BareScale(r, t), ms)) <= 1e-5, $"{Dist(t.localScale, Scaled(BareScale(r, t), ms)):G3}"); }
    }

    static void FM5()
    {
        var cor = Corr(scale: new Vec3(1.1f, 0.9f, 1.2f));
        // synthetic, on a card without modifiers
        var h = ReconHost();
        try
        {
            var k = Key(h.Ok(new { cmd = "chara.load", path = femaleRel, sex = "female" }, "the target"));
            h.Ok(new { cmd = "chara.motion", key = k, autoStep = false, physics = false, look = false }, "by hand");
            h.Rt.ApplyNow();
            var r = h.Rt.FindChara(k);
            var t = Bone(r, "cf_J_MouthMove");
            var face = r.Motion.Get<ExpressionStage>();
            for (int i = 0; i < 2; i++) r.Motion.Step(Dt);
            var bare = t.localScale;
            face.VoiceValue = 0.8f;
            for (int i = 0; i < 20; i++) r.Motion.Step(Dt);
            Check("without a modifier the width moves x", Mathf.Abs(t.localScale.x - bare.x) > 1e-3f || face.Width.AdjustWidthScale == bare.x, $"{t.localScale.x} / {face.Width.AdjustWidthScale}");
            face.VoiceValue = 0f;
            face.InvalidateMouthWidth();
            for (int i = 0; i < 5; i++) r.Motion.Step(Dt);
            var a = Applier(r);
            a.Add(new BoneModifierApplier.Modifier { Target = t, Bone = "cf_J_MouthMove", Source = "test", Location = 1, Scale = new Vector3(1.2f, 1.3f, 1.4f) });
            BoneModifierSetup.Sync(r.Context);
            MouthSequence(h, k, "synthetic", null);
            MouthSequence(h, k, "synthetic", cor);
            a.Remove("test");
            r.Motion.Step(Dt);
            face.InvalidateMouthWidth();
            r.Motion.Step(Dt);
            Check("the modifier removed: the bone gives back y and z, the width writes x again", Mathf.Abs(t.localScale.y - bare.y) <= 1e-5f && Mathf.Abs(t.localScale.z - bare.z) <= 1e-5f && Mathf.Abs(t.localScale.x - face.Width.AdjustWidthScale) <= 1e-5f, t.localScale.ToString("F4"));
        }
        finally { h.Dispose(); }
        // the six cards
        foreach (var (hash, rel, sex) in ModCards("mouth"))
        {
            var hc = ReconHost();
            try
            {
                var k = Key(hc.Ok(new { cmd = "chara.load", path = rel, sex }, "card " + hash));
                if (k == null) continue;
                hc.Ok(new { cmd = "chara.motion", key = k, autoStep = false, physics = false, look = false }, "by hand");
                hc.Rt.ApplyNow();
                var r = hc.Rt.FindChara(k);
                if (!Check(hash + ": a modifier on the bone", r.Context.Bones && r.Context.Bones.HasScale(Bone(r, "cf_J_MouthMove")))) continue;
                MouthSequence(hc, k, hash, null);
                MouthSequence(hc, k, hash, cor);
            }
            finally { hc.Dispose(); }
        }
    }

    // ------------------------------------------------------------ FM6

    static void FM6()
    {
        FM6Cards();
        var f = NewBareTwins(femaleRel, "female", "FM6");
        try
        {
            var lists = HS2Next.Content.Maker.MakerCatalog.For(services.Catalog);
            var row = services.Catalog.EntriesOf(HeelsData.FemaleShoesCategory).Where(e => HeelsData.For(e) != null && lists.IsListed(247, 1, e.Key))
                .OrderBy(e => e.Key.ToString(), StringComparer.Ordinal).FirstOrDefault(e => HeelsData.For(e).Root.y > 0);
            if (!Check("an installed heel shoe", row != null)) return;
            var heel = HeelsData.For(row);
            string joint = "cf_J_Foot02_L", rotOnly = "cf_J_Toes01_L", free = "cf_J_Foot01_R";
            var S = new Vector3(1.2f, 0.9f, 1.1f);
            var ja = Bone(f.RA, joint); var jb = Bone(f.RB, joint);
            RowOn(f, ReconRows[0]);
            f.Step(2, 0f);
            // the shoe's edit remakes the applier from the card (none here), so the shoes first, taken off; the test modifiers, as a card's, before the heels act
            foreach (var k in new[] { f.A, f.B })
            {
                f.H.Ok(new { cmd = "chara.edit.item", key = k, path = "coord.clothes.shoes.item", @ref = row.Key.ToString() }, "the heel shoe");
                f.H.Ok(new { cmd = "chara.clothes", key = k, states = new Dictionary<string, int> { ["shoes"] = 2 } }, "shoes off");
            }
            f.H.Rt.ApplyNow();
            f.Step(2, 0f);
            var a = Applier(f.RA);
            a.Add(new BoneModifierApplier.Modifier { Target = ja, Bone = joint, Source = "test", Location = 1, Scale = S });
            a.Add(new BoneModifierApplier.Modifier { Target = Bone(f.RA, rotOnly), Bone = rotOnly, Source = "test", Location = 1, Rotation = new Vector3(0f, 0f, 7f) });
            BoneModifierSetup.Sync(f.RA.Context);
            f.Step(2, 0f);
            foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "chara.clothes", key = k, states = new Dictionary<string, int> { ["shoes"] = 0 } }, "shoes on");
            f.H.Rt.ApplyNow();
            f.Step(2, 0f);
            var foot = f.RA.Motion.Get<HeelsFoot>();
            Check("the heels are in effect", foot != null && foot.Written && foot.State.Active != null);
            Check("IK off: heel x modifier", Dist(ja.localScale, Scaled(jb.localScale, S)) <= 1e-5 && Dist(jb.localScale, heel.Foot.Scale) <= 1e-5, $"{ja.localScale:F4} / {jb.localScale:F4}");
            Vector3 v0 = ja.localScale; var rot0 = Local(Bone(f.RA, rotOnly)); var free0 = Local(Bone(f.RA, free));
            double drift = 0, driftR = 0, driftP = 0, driftS = 0, freeOff = 0;
            for (int i = 0; i < 600; i++)
            {
                f.Step(1, 0f);
                drift = Math.Max(drift, Dist(ja.localScale, v0));
                var ro = Local(Bone(f.RA, rotOnly));
                driftR = Math.Max(driftR, Angle(ro.r, rot0.r));
                driftP = Math.Max(driftP, Dist(ro.p, rot0.p));
                driftS = Math.Max(driftS, Dist(ro.s, rot0.s));
                var fa = Local(Bone(f.RA, free)); var fb = Local(Bone(f.RB, free));
                freeOff = Math.Max(freeOff, Math.Max(Dist(fa.p, fb.p), Math.Max(Angle(fa.r, fb.r) / 100, Dist(fa.s, fb.s))));
            }
            Check("600 steps: no drift of the scale", drift <= 1e-6, $"{drift:G3}");
            Check("a joint with only a rotation value: no roll accumulates, its other channels stand", driftR <= 0.01 && driftP <= 1e-6 && driftS <= 1e-6, $"rot {driftR:G3} pos {driftP:G3} scale {driftS:G3}");
            Check("a joint without a value on a card with values on others: equal to the twin", freeOff <= 1e-5, $"{freeOff:G3}");
            RowOn(f, ReconRows[1]);
            f.Step(5, 0f);
            Check("a row switch: heel x modifier", Dist(ja.localScale, Scaled(jb.localScale, S)) <= 1e-5, $"{ja.localScale:F4} / {jb.localScale:F4}");
            RowOn(f, ReconRows[0]);
            f.Step(5, 0f);
            foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "ik.enable", key = k, on = true }, "IK on");
            f.Step(3, 0f);
            Check("IK on: the heel alone", Dist(ja.localScale, jb.localScale) <= 1e-5, $"{ja.localScale:F4} / {jb.localScale:F4}");
            double dIk = 0;
            for (int i = 0; i < 120; i++) { f.Step(1, 0f); dIk = Math.Max(dIk, Dist(ja.localScale, jb.localScale)); }
            Check("IK on, 120 steps: no drift", dIk <= 1e-5, $"{dIk:G3}");
            foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "ik.enable", key = k, on = false }, "IK off");
            f.Step(2, 0f);
            Check("IK off again: heel x modifier", Dist(ja.localScale, Scaled(jb.localScale, S)) <= 1e-5, $"{ja.localScale:F4} / {jb.localScale:F4}");
            foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "chara.clothes", key = k, states = new Dictionary<string, int> { ["shoes"] = 2 } }, "shoes off");
            f.H.Rt.ApplyNow();
            f.Step(1, 0f);
            Check("shoes off: released", !foot.Written);
            Check("shoes off: bare x modifier at once", Dist(ja.localScale, Scaled(jb.localScale, S)) <= 1e-5, $"{ja.localScale:F4} / {jb.localScale:F4}");
            f.Step(60, 0f);
            Check("shoes off, 60 steps: no drift", Dist(ja.localScale, Scaled(jb.localScale, S)) <= 1e-5, $"{ja.localScale:F4} / {jb.localScale:F4}");
            var ra = Local(Bone(f.RA, rotOnly)); var rb = Local(Bone(f.RB, rotOnly));
            Check("shoes off: the rotation-only joint: base x modifier", Angle(ra.r, rb.r * Quaternion.Euler(0f, 0f, 7f)) <= 0.01, $"{Angle(ra.r, rb.r * Quaternion.Euler(0f, 0f, 7f)):G3} deg");
        }
        finally { f.Dispose(); }
    }

    // the cards of the local list that wear heel shoes: off and on, every joint: the heel's scale times the modifier's
    static void FM6Cards()
    {
        string[] joints = { "cf_J_Foot01_L", "cf_J_Foot02_L", "cf_J_Foot01_R", "cf_J_Foot02_R", "cf_J_Toes01_L", "cf_J_Toes01_R" };
        foreach (var (hash, rel, sex) in ModCards("heel"))
        {
            var h = ReconHost();
            try
            {
                List<(Vector3 p, Quaternion r, Vector3 s)> off = null;
                foreach (var on in new[] { false, true })
                {
                    h.Ok(new { cmd = "scene.new" }, "scene.new");
                    h.Ok(new { cmd = "app.options", ext = new { abmx = on, accessoryControls = false }, fk = new { hipsOffset = false }, correctionAllOn = true }, "the modifiers " + (on ? "on" : "off"));
                    var k = Key(h.Ok(new { cmd = "chara.load", path = rel, sex }, hash));
                    h.Ok(new { cmd = "chara.anime", key = k, group = 19, category = 0, no = 0, time = 0.5f, speed = 0f }, "the row");
                    h.Ok(new { cmd = "chara.motion", key = k, autoStep = false, physics = false, look = false, reset = true }, "by hand");
                    h.Rt.ApplyNow();
                    var r = h.Rt.FindChara(k);
                    for (int i = 0; i < 5; i++) r.Motion.Step(0f);
                    var now = joints.Select(j => Local(Bone(r, j))).ToList();
                    if (!on) { off = now; continue; }
                    if (!Check($"{hash}: wearing heel shoes", r.Motion.Get<HeelsLift>()?.Lifted == true)) continue;
                    var scales = joints.Select(j => r.Context.Bones ? ModScale(r, Bone(r, j)) : Vector3.one).ToList();
                    double worst = 0; string where = "";
                    for (int i = 0; i < joints.Length; i++)
                    {
                        double d = Dist(now[i].s, Vector3.Scale(off[i].s, scales[i]));
                        if (d > worst) { worst = d; where = joints[i]; }
                    }
                    Check($"{hash}: every joint's scale = heel x modifier, IK off", worst <= 1e-5, $"worst {worst:G3} {where}");
                    for (int i = 0; i < 600; i++) r.Motion.Step(0f);
                    var w = new Worst();
                    for (int i = 0; i < joints.Length; i++) w.Add(Local(Bone(r, joints[i])), now[i], joints[i]);
                    Check($"{hash}: 600 steps: no drift on the joints", w.Ok(1e-6, 0.01, 1e-6), w.ToString());
                }
            }
            finally { h.Dispose(); }
        }
    }

    // ------------------------------------------------------------ FM7

    static void FM7()
    {
        string[] bones = { "cf_J_ArmUp00_R", "cf_J_EarBase_s_L" };
        var c = OrderCorr;
        var f = NewBareTwins(femaleRel, "female", "FM7");
        try
        {
            AddAll(f.RA, bones);
            RowOn(f, ReconRows[0], 0f, 1f);
            f.Step(2, 0f);
            SetBones(f.H, f.A, Map(bones.Select(b => (b, c)).ToArray()));
            f.Step(2, 0f);
            var w = new Worst();
            void Against(Worst x, string where) { foreach (var b in bones) x.Add(Local(Bone(f.RA, b)), WithCorr(WithMods(Local(Bone(f.RB, b))), c), where + " " + b); }
            Against(w, "start");
            for (int i = 0; i < 600; i++) { f.Step(1, Dt); if (i % 25 == 24) Against(w, "running"); }
            Check("600 steps of a clip: base, modifier, correction", w.Ok(1e-4, 0.01, 1e-4), w.ToString());
            RowOn(f, ReconRows[0], 0.5f, 0f);
            f.Step(2, 0f);
            var ws = new Worst();
            for (int i = 0; i < 600; i++) { f.Step(1, 0f); if (i % 50 == 49) Against(ws, "still"); }
            Check("600 steps at 0: no drift", ws.Ok(1e-4, 0.01, 1e-4), ws.ToString());
            Measure($"FM7 running {w}; still {ws}");

            // a bind with both on one bone, a row switch and back: the bind sees the bare pose whichever handler runs first
            RowOn(f, ReconRows[1]);
            f.Step(5, 0f);
            var wb = new Worst();
            Against(wb, "the other row");
            Check("a row switch: base, modifier, correction", wb.Ok(1e-4, 0.01, 1e-4), wb.ToString());
            RowOn(f, ReconRows[0]);
            f.Step(5, 0f);
            var wa = new Worst();
            Against(wa, "back");
            Check("and back: base, modifier, correction, once", wa.Ok(1e-4, 0.01, 1e-4), wa.ToString());
            // the bind's defaults hold neither: a bone no clip writes takes the value at the bind as its default
            var wn = new Worst();
            for (int i = 0; i < 60; i++) { f.Step(1, 0f); if (i % 20 == 19) Against(wn, "after the binds"); }
            Check("60 steps after the binds: no growth", wn.Ok(1e-4, 0.01, 1e-4), wn.ToString());

            // clearing: the modifier once
            SetBones(f.H, f.A, Map());
            var wc = new Worst();
            AgainstTwin(f, bones, wc, "at once");
            f.Step(1, 0f);
            AgainstTwin(f, bones, wc, "a step later");
            f.Step(30, 0f);
            AgainstTwin(f, bones, wc, "30 steps later");
            Check("clearing: the modifier once", wc.Ok(1e-4, 0.01, 1e-4), wc.ToString());
        }
        finally { f.Dispose(); }

        // the known case: a correction, a modifier and FK on one rotation channel: recorded, no drift
        var g = NewBareTwins(femaleRel, "female", "FM7 FK");
        try
        {
            string b = "cf_J_Spine02";
            AddAll(g.RA, b);
            RowOn(g, ReconRows[0]);
            g.Step(2, 0f);
            FkOn(g, 0f, "Body");
            g.Step(2, 0f);
            SetBones(g.H, g.A, Map((b, c)));
            g.Step(2, 0f);
            var at = Local(Bone(g.RA, b));
            var wd = new Worst();
            for (int i = 0; i < 600; i++) { g.Step(1, 0f); if (i % 50 == 49) wd.Add(Local(Bone(g.RA, b)), at, b); }
            Check("a correction, a modifier and FK on one rotation channel: 600 steps, no drift", wd.Ok(1e-5, 0.01, 1e-5), wd.ToString());
        }
        finally { g.Dispose(); }
    }

    // ------------------------------------------------------------ FM8

    static List<object> ApplierState(BoneModifierApplier a)
    {
        var list = new List<object>();
        var bones = (System.Collections.IList)typeof(BoneModifierApplier).GetField("bones", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(a);
        foreach (var b in bones)
            foreach (var fi in b.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (fi.Name.StartsWith("Base", StringComparison.Ordinal) || fi.Name.StartsWith("W", StringComparison.Ordinal) || fi.Name.StartsWith("E", StringComparison.Ordinal))
                    list.Add(fi.GetValue(b));
        return list;
    }

    static void FM8()
    {
        var f = NewBareTwins(femaleRel, "female", "FM8");
        try
        {
            AddAll(f.RA, ClassBones);
            RowOn(f, ReconRows[0]);
            f.Step(5, 0f);
            var state = ApplierState(f.RA.Context.Bones);
            var values = ClassBones.Select(b => Local(Bone(f.RA, b))).ToList();
            var inside = new List<(Vector3 p, Quaternion r, Vector3 s)>();
            f.RA.Motion.Probe(MotionPhase.IkPrepare, f.RA.Root.transform, () => inside = ClassBones.Select(b => Local(Bone(f.RA, b))).ToList());
            Check("the probe's state: Base, W and E as they were", ApplierState(f.RA.Context.Bones).SequenceEqual(state));
            var wb = new Worst();
            for (int i = 0; i < ClassBones.Length; i++) wb.Add(Local(Bone(f.RA, ClassBones[i])), values[i], ClassBones[i]);
            Check("after the probe: the bones as they were", wb.Ok(1e-6, 0.001, 1e-6), wb.ToString());
            var wi = new Worst();
            for (int i = 0; i < ClassBones.Length; i++) wi.Add(inside[i], WithMods(Local(Bone(f.RB, ClassBones[i]))), ClassBones[i]);
            Check("inside the probe: the frame's input with the modifier", wi.Ok(1e-4, 0.01, 1e-4), wi.ToString());
            var wd = new Worst();
            f.Step(60, 0f);
            AgainstTwin(f, ClassBones, wd, "60 steps after");
            Check("60 steps after: no drift", wd.Ok(1e-4, 0.01, 1e-4), wd.ToString());
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ FM9

    static void FM9()
    {
        var f = new Twins { H = NewHost() };
        try
        {
            f.A = Key(f.H.Ok(new { cmd = "chara.load", path = femaleRel, sex = "female" }, "A"));
            f.B = Key(f.H.Ok(new { cmd = "chara.load", path = femaleRel, sex = "female" }, "B"));
            foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "chara.motion", key = k, look = false, physics = true, reset = true, autoStep = true }, "physics on");
            f.H.Frames(2);
            string[] bones = { "cf_J_ArmUp00_R", "cf_J_EarBase_s_L", "cf_J_Spine01_s" };
            AddAll(f.RA, bones);
            RowOn(f, ReconRows[0]);
            f.H.Frames(3);
            var at = bones.Select(b => Local(Bone(f.RA, b))).ToList();
            var c0 = new Worst();
            AgainstTwin(f, bones, c0, "running");
            Check("running: base and modifier", c0.Ok(1e-4, 0.01, 1e-4), c0.ToString());
            int settles0 = f.RA.Motion.Settles;
            f.H.Ok(new { cmd = "scene.pause", paused = true }, "the global pause");
            f.H.Frames(120);
            var wp = new Worst();
            for (int i = 0; i < bones.Length; i++) wp.Add(Local(Bone(f.RA, bones[i])), at[i], bones[i]);
            Check("the global pause, 120 frames: the value holds", wp.Ok(1e-5, 0.01, 1e-5), wp.ToString());
            Check("no settle without a change", f.RA.Motion.Settles == settles0, f.RA.Motion.Settles + " / " + settles0);
            // a body slider while paused: shows, one settle
            foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "shape.set", key = k, bodyIndex = 4, bodyValue = 0.9f }, "a body slider while paused");
            f.H.Frames(2);
            Check("one settle", f.RA.Motion.Settles == settles0 + 1, f.RA.Motion.Settles + " / " + settles0);
            var ws = new Worst();
            AgainstTwin(f, bones, ws, "after the slider");
            Check("the slider's value shows with the modifier", ws.Ok(1e-4, 0.01, 1e-4), ws.ToString());
            f.H.Ok(new { cmd = "scene.pause", paused = false }, "the pause off");
            f.H.Frames(2);
            foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "chara.motion", key = k, paused = true }, "the character's own pause");
            f.H.Frames(2);
            at = bones.Select(b => Local(Bone(f.RA, b))).ToList();
            f.H.Frames(120);
            var wo = new Worst();
            for (int i = 0; i < bones.Length; i++) wo.Add(Local(Bone(f.RA, bones[i])), at[i], bones[i]);
            Check("the character's own pause, 120 frames: the value holds", wo.Ok(1e-5, 0.01, 1e-5), wo.ToString());
            foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "chara.motion", key = k, paused = false }, "resume");
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ FM10

    static void FM10()
    {
        // a slider edit on a real card against a fresh build at the new slider
        foreach (var (hash, rel, sex) in ModCards("ah5").Take(1))
        {
            var f = new Twins { H = ReconHost() };
            try
            {
                f.A = Key(f.H.Ok(new { cmd = "chara.load", path = rel, sex }, hash + " A"));
                f.B = Key(f.H.Ok(new { cmd = "chara.load", path = rel, sex }, hash + " B"));
                foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "chara.motion", key = k, autoStep = false, physics = false, look = false, reset = true }, "by hand");
                RowOn(f, ReconRows[0]);
                f.Step(2, 0f);
                var ma = f.RA.Context.Bones;
                var mods = ma.Modifiers.Where(m => m.Target).ToList();
                var shapeOnes = mods.Where(m => m.Scale != Vector3.one && f.RA.Animation.ShapeBones.Contains(m.Target)).ToList();
                Check("the card has a scale modifier on a shape-guard bone", shapeOnes.Count > 0, shapeOnes.Count + " of " + mods.Count);
                foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "shape.set", key = k, bodyIndex = 4, bodyValue = 0.9f }, "a body slider");
                f.H.Rt.ApplyNow();
                f.RA.Motion.Step(0f);
                Check("a rebuild request of the second", f.RB.RequestPluginRebuild("modifiers.test"));
                f.H.Rt.ApplyNow();
                f.H.Ok(new { cmd = "chara.motion", key = f.B, autoStep = false, physics = false, look = false }, "by hand");
                RowOn(f, ReconRows[0]);
                f.H.Rt.ApplyNow();
                f.Step(2, 0f);
                var w = new Worst();
                foreach (var m in mods) { var t = Bone(f.RB, m.Bone); if (t) w.Add(Local(m.Target), Local(t), m.Bone); }
                Check("a body slider: equal to a fresh build at the new slider", w.Ok(1e-5, 0.01, 1e-5), w.ToString());
                // the guard holds the bare value: one sample with the applier's stage off leaves a guarded bone bare
                var one = shapeOnes[0];
                var bare = BareScale(f.RA, one.Target);
                var guard = typeof(HS2Next.Motion.Animation.CharaAnimation).GetField("shapeGuard", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(f.RA.Animation);
                var scl = (Vector3[])guard.GetType().GetField("scl", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(guard);
                var gb = f.RA.Animation.ShapeBones.ToList().IndexOf(one.Target);
                Check("the guard holds the bare value", gb >= 0 && Dist(scl[gb], bare) <= 1e-6, $"{(gb >= 0 ? scl[gb].ToString("F4") : "-")} / {bare:F4}");
            }
            finally { f.Dispose(); }
        }

        var g = NewBareTwins(femaleRel, "female", "FM10");
        try
        {
            AddAll(g.RA, ClassBones);
            RowOn(g, ReconRows[0]);
            g.Step(2, 0f);
            var m = g.RA.Context.Bones;
            var t0 = Bone(g.RA, ClassBones[2]);
            Check("SetSuppressed: changed", m.SetSuppressed(new[] { t0 }));
            g.Step(1, 0f);
            var ws = new Worst();
            ws.Add(Local(t0), Local(Bone(g.RB, ClassBones[2])), "suppressed");
            Check("suppressed: the bone at its base", ws.Ok(1e-5, 0.01, 1e-5), ws.ToString());
            m.SetSuppressed(null);
            g.Step(1, 0f);
            var wo = new Worst();
            AgainstTwin(g, ClassBones, wo, "suppressed off");
            Check("suppressed off: base and modifier", wo.Ok(1e-4, 0.01, 1e-4), wo.ToString());
            m.Remove("test");
            g.Step(1, 0f);
            var wr = new Worst();
            foreach (var b in ClassBones) wr.Add(Local(Bone(g.RA, b)), Local(Bone(g.RB, b)), b);
            Check("Remove, then a step: the bones equal the twin", wr.Ok(1e-5, 0.01, 1e-5), wr.ToString());
            g.Step(30, 0f);
            var wr2 = new Worst();
            foreach (var b in ClassBones) wr2.Add(Local(Bone(g.RA, b)), Local(Bone(g.RB, b)), b);
            Check("30 steps later: still", wr2.Ok(1e-5, 0.01, 1e-5), wr2.ToString());
            // the final stage again on a card without modifiers (B never had an applier): none and no stage
            var cx = g.RB.Context;
            cx.Edit.ReapplyFinal();
            cx.Editor.Flush();
            var names = g.RB.Motion.Stages.Select(s => s.Name).ToList();
            Check("the final stage again without modifiers: no applier, no stage", !cx.Bones && !names.Contains("bones.modifiers") && !names.Contains("bones.modifiers.end"), string.Join(",", names));
        }
        finally { g.Dispose(); }

        foreach (var (hash, rel, sex) in ModCards("ah5").Take(1))
        {
            var f = NewCardTwin(rel, sex, "FM10 " + hash);
            try
            {
                var cx = f.RA.Context;
                var before = f.RA.Motion.Stages.Select(s => s.Name).ToList();
                Check("a card with modifiers has both stages", before.Contains("bones.modifiers") && before.Contains("bones.modifiers.end"), string.Join(",", before));
                var old = cx.Bones;
                cx.Edit.ReapplyFinal();
                cx.Editor.Flush();
                var after = f.RA.Motion.Stages.Select(s => s.Name).ToList();
                Check("the final stage again: a new applier, the same stages", cx.Bones && cx.Bones != old && after.SequenceEqual(before), string.Join(",", after));
            }
            finally { f.Dispose(); }
        }
    }

    // ------------------------------------------------------------ FM11

    static void FM11()
    {
        var f = NewBareTwins(femaleRel, "female", "FM11");
        try
        {
            AddAll(f.RA, ClassBones);
            f.Step(3, 0f);
            var tpose = ClassBones.Select(b => Local(Bone(f.RA, b))).ToList();
            RowOn(f, ReconRows[0]);
            f.Step(3, 0f);
            var before = ClassBones.Select(b => Local(Bone(f.RA, b))).ToList();
            var ws = new Worst();
            using (StudioDefaultPoseScope.Enter(f.RA))
            using (StudioDefaultPoseScope.Enter(f.RB))
                for (int i = 0; i < ClassBones.Length; i++) ws.Add(Local(Bone(f.RA, ClassBones[i])), tpose[i], ClassBones[i]);
            Check("the scope's values = the T-pose row's", ws.Ok(1e-4, 0.01, 1e-4), ws.ToString());
            foreach (var k in new[] { f.A, f.B }) f.H.Ok(new { cmd = "chara.motion", key = k, autoStep = false, physics = false, look = false }, "by hand again");
            f.H.Rt.ApplyNow();
            f.Step(2, 0f);
            var wa = new Worst();
            for (int i = 0; i < ClassBones.Length; i++) wa.Add(Local(Bone(f.RA, ClassBones[i])), before[i], ClassBones[i]);
            Check("after the scope: the bones as before", wa.Ok(1e-5, 0.01, 1e-5), wa.ToString());
            // a stop, then a read in the same request
            f.RA.Animation.Stop();
            f.RB.Animation.Stop();
            var wd = new Worst();
            AgainstTwin(f, ClassBones, wd, "stop");
            Check("Stop(), read at once: the modifier shows", wd.Ok(1e-4, 0.01, 1e-4), wd.ToString());
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ FM15R

    // A stopwatch, headless, on the card of the local list with the most modifiers: the old pass (three channels read, written and read back on each bone) on the
    // same transforms, and the new pass with its end stage. A reference for spec 3.9 and no bar: the player row FM15 holds the bar.
    static void FM15R()
    {
        var cards = ModCards("mouth");
        if (cards.Count == 0) return;
        var pick = cards.OrderBy(c => c.hash, StringComparer.Ordinal).ToList();
        int best = -1; (string hash, string rel, string sex) card = default;
        foreach (var c in pick)
        {
            var h0 = ReconHost();
            try
            {
                var k0 = Key(h0.Call(new { cmd = "chara.load", path = c.rel, sex = c.sex }));
                h0.Rt.ApplyNow();
                int n0 = h0.Rt.FindChara(k0)?.Context.Bones?.BoneCount ?? 0;
                if (n0 > best) { best = n0; card = c; }
            }
            finally { h0.Dispose(); }
        }
        var h = ReconHost();
        try
        {
            var k = Key(h.Ok(new { cmd = "chara.load", path = card.rel, sex = card.sex }, card.hash));
            h.Ok(new { cmd = "chara.anime", key = k, group = 19, category = 0, no = 0, time = 0.5f, speed = 0f }, "the row");
            h.Ok(new { cmd = "chara.motion", key = k, autoStep = false, physics = false, look = false }, "by hand");
            h.Rt.ApplyNow();
            var r = h.Rt.FindChara(k);
            r.Motion.Step(0f);
            var a = r.Context.Bones;
            var ts = a.Modifiers.Where(m => m.Target).Select(m => m.Target).Distinct().ToList();
            int calls = 200;
            double Median(Action one)
            {
                var runs = new List<double>();
                for (int k2 = 0; k2 < 7; k2++)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < calls; i++) one();
                    runs.Add(sw.Elapsed.TotalMilliseconds / calls);
                }
                runs.Sort();
                return runs[runs.Count / 2];
            }
            double old = Median(() =>
            {
                foreach (var t in ts)
                {
                    var ls = t.localScale; var lp = t.localPosition; var lr = t.localRotation;
                    t.localScale = ls; t.localPosition = lp; t.localRotation = lr;
                    ls = t.localScale; lp = t.localPosition; lr = t.localRotation;
                }
            });
            double now = Median(() => { a.Pass(true); a.End(); });
            r.Motion.Step(0f);
            Measure($"FM15R card {card.hash}, {ts.Count} bones: the old pass's engine calls {old:F4} ms, the new pass and end stage {now:F4} ms per frame (headless Editor; a reference, no bar)");
            Check("the reference ran", old > 0 && now > 0);
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ FI12

    static void FI12()
    {
        var f = NewTwins(femaleRel, "female", "FI12");
        try
        {
            f.Step(2);
            const string noBone = "no_such_bone_fi12", noChain = "hair:3/no_such_chain", noRenderer = "head/no_such_renderer";
            SetBones(f.H, f.A, Map((noBone, Corr(rot: new Vec3(5f, 0f, 0f)))));
            SetChains(f.H, f.A, (noChain, new ChainValues { Damping = 0.3f }));
            SetBlend(f.H, f.A, (noRenderer, "fi12_a", 40f));
            var found = new List<string>();
            int count = 0;
            foreach (var x in f.H.Rt.Findings)
                if (x.Code == AdvancedPoseFindings.Stale && x.NodeId == f.A) { found.Add(x.Message); count = x.Count; }
            Check("one finding for the node", found.Count == 1, string.Join(" | ", found));
            string msg = found.FirstOrDefault() ?? "";
            Check("it names the bone, the chain and the renderer", msg.Contains(noBone) && msg.Contains(noChain) && msg.Contains(noRenderer), msg);
            Check("its count is the sum", count == 3, "" + count);
            f.Step(3);
            SetBones(f.H, f.A, Map((noBone, Corr(rot: new Vec3(6f, 0f, 0f)))));
            int again = 0, n = 0;
            foreach (var x in f.H.Rt.Findings) if (x.Code == AdvancedPoseFindings.Stale && x.NodeId == f.A) { n++; again = x.Count; }
            Check("more reads: still one finding, the same count", n == 1 && again == 3, n + " findings, count " + again);
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ FM12

    static void FM12()
    {
        var f = new Twins { H = NewHost() };
        try
        {
            var h = f.H;
            f.A = Key(h.Ok(new { cmd = "chara.load", path = femaleRel, sex = "female" }, "A"));
            f.B = Key(h.Ok(new { cmd = "chara.load", path = femaleRel, sex = "female" }, "B"));
            foreach (var k in new[] { f.A, f.B })
            {
                h.Ok(new { cmd = "chara.motion", key = k, physics = false, look = false }, "physics off");
                h.Ok(new { cmd = "chara.anime", key = k, group = 19, category = 0, no = 0, speed = 0f }, "the row at speed 0");
                h.Ok(new { cmd = "fk.enable", key = k, on = true }, "FK on");
                h.Ok(new { cmd = "fk.enable", key = k, groups = new[] { "Body", "Neck" }, on = true }, "FK groups Body and Neck");
            }
            h.Frames(3);
            var comp = JObject.Parse(File.ReadAllText(Path.Combine(ContentSettings.ProjectRoot, "Tests", "Timeline", "compositions", "bones9.json")));
            var tracks = new JArray();
            var names = new List<string>();
            foreach (var t in comp["tracks"].OfType<JObject>())
            {
                if ((string)t["key"] != "$b") continue;
                foreach (var k in new[] { f.A, f.B })
                {
                    var c = (JObject)t.DeepClone();
                    c["key"] = k;
                    tracks.Add(c);
                }
                names.Add((string)t["bone"]);
            }
            var bones = names.Distinct().ToArray();
            AddAll(f.RA, bones);
            h.Frames(2);
            h.Ok(new JObject { ["cmd"] = "timeline.build", ["duration"] = comp["duration"], ["tracks"] = tracks, ["play"] = false }.ToString(Formatting.None), "timeline.build with bones9's FK tracks on both");
            h.Ok(new { cmd = "timeline.play" }, "play");
            void Against(Worst x, string where)
            {
                foreach (var b in bones)
                {
                    var a = Local(Bone(f.RA, b)); var t = Local(Bone(f.RB, b));
                    var m = WithMods(t);
                    x.Add(a, (m.p, t.r, m.s), where + " " + b);
                }
            }
            var w = new Worst();
            for (int k = 0; k < 10; k++) { h.Frames(30); Against(w, "playing"); }
            Check("playing: the rotation as FK on the twin, the modifier kept on position and scale", w.Ok(1e-4, 0.01, 1e-4), w.ToString());
            h.Ok(new { cmd = "timeline.pause" }, "pause");
            h.Frames(5);
            var wp = new Worst();
            for (int k = 0; k < 4; k++) { h.Frames(30); Against(wp, "paused"); }
            Check("paused: the same", wp.Ok(1e-4, 0.01, 1e-4), wp.ToString());
            h.Ok(new { cmd = "timeline.seek", t = 1.5f }, "a seek");
            h.Frames(3);
            var ws = new Worst();
            Against(ws, "seek");
            Check("a seek: the same", ws.Ok(1e-4, 0.01, 1e-4), ws.ToString());
            h.Ok(new { cmd = "timeline.stop" }, "stop");
            h.Ok(new { cmd = "timeline.clear" }, "clear");
        }
        finally { f.Dispose(); }
    }
}
