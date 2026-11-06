// ModifierOrderTest, real frames (Phase 6.3a package M, FM5's real frames): parts of AdvancedPoseTest.play (-only FM13+FM5P).
//   FM13  auto step and hand-stepped: between two steps nothing writes the bones (values bit-equal over 30 frames with no step); at speed 0 the auto step holds them
//   FM5P  the mouth width bone in real frames, a synthetic modifier and the cards of the local list: x, y, z hold the modifier once
//   FM3P  a channel one state of a controller writes and another does not, in real frames, where Unity reads the defaults a second time after the bind: twin x modifier
//         over every order of the rows; the same with the character stepped by hand, left 40 frames before its first step after each row; the same when the bind happens
//         under the global pause, and when the pause ends only after further frames
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2Next.App;
using HS2Next.Motion.Face;
using HS2Next.Scenes.Runtime;
using UnityEngine;

public static partial class AdvancedPoseTest
{
    static IEnumerator Play_N_ModifierOrder()
    {
        current = "N_ModifierOrder";
        if (!Want("FM13") && !Want("FM5P") && !Want("FM3P")) yield break;
        var ready = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ready);
        var app = AppCore.Current;
        if (!Check("the app runs with its bridge and runtime", ready[0] && app != null && app.Stage == AppStage.Ready && app.Bridge != null && app.Runtime != null, app?.Stage.ToString())) yield break;
        float old = Time.captureDeltaTime;
        Time.captureDeltaTime = Dt;
        try
        {
            if (POpen("FM13", "real frames: between steps nothing writes")) yield return FM13(app);
            if (POpen("FM5P", "the mouth width bone, real frames")) yield return FM5P(app);
            if (POpen("FM3P", "a channel one state writes and another does not, real frames")) yield return FM3P(app);
            yield return PCall(app, new { cmd = "scene.new" });
        }
        finally { Time.captureDeltaTime = old; }
    }

    static IEnumerator FM3P(AppCore app)
    {
        string[] bones = { FirstClipOnly };
        var Idle = ReconRows[0]; var Dance = ReconRows[1]; var Chair = ReconRows[2]; var Attack = ReconRows[3];
        var orders = new[]
        {
            new[] { Idle, Chair, Idle, Chair, Idle },
            new[] { Chair, Idle },
            new[] { Idle, Dance, Idle },
            new[] { Idle, Dance, Chair, Idle },
            new[] { Attack, Idle, Chair, Idle },
        };
        foreach (var byHand in new[] { false, true })
            foreach (var order in orders)
            {
                yield return PScene(app, 2, -1, "FM3P");
                if (pB == null) yield break;
                var ra = PR(app, pA); var rb = PR(app, pB);
                yield return PCall(app, new { cmd = "batch", cmds = new object[] { new { cmd = "chara.motion", key = pA, autoStep = !byHand, look = false, physics = false }, new { cmd = "chara.motion", key = pB, autoStep = !byHand, look = false, physics = false } } });
                AddMods(ra, bones);
                yield return PFrames(3);
                foreach (var row in order)
                {
                    yield return PCall(app, new { cmd = "batch", cmds = new object[] { new { cmd = "chara.anime", key = pA, group = row.g, category = row.c, no = row.n, time = 0.5f, speed = 0f }, new { cmd = "chara.anime", key = pB, group = row.g, category = row.c, no = row.n, time = 0.5f, speed = 0f } } });
                    if (byHand)
                    {
                        yield return PFrames(40);   // many frames before the first step: the second read waits for it
                        for (int i = 0; i < 2; i++)
                        {
                            yield return PCall(app, new { cmd = "chara.motion", key = pA, step = new { dt = Dt, n = 1 } });
                            yield return PCall(app, new { cmd = "chara.motion", key = pB, step = new { dt = Dt, n = 1 } });
                            yield return PFrames(2);
                        }
                    }
                    else yield return PFrames(6);
                    var w = new Worst();
                    foreach (var b in bones) w.Add(Local(Bone(ra, b)), WithMods(Local(Bone(rb, b))), row.name);
                    Check($"{(byHand ? "by hand, " : "")}{string.Join(", ", order.Select(x => x.name))}: at {row.name}, twin with the modifier once", w.Ok(1e-4, 0.01, 1e-4), w.ToString());
                }
                ra.Context.Bones.Remove("test");
            }

        // the bind under the global pause: the step runs at no time, and a later frame's first step still ends the wait for the read
        foreach (var order in new[] { new[] { Idle, Chair, Idle }, new[] { Chair, Idle }, new[] { Idle, Dance, Chair, Idle } })
        {
            yield return PScene(app, 2, -1, "FM3P paused");
            if (pB == null) yield break;
            var ra = PR(app, pA); var rb = PR(app, pB);
            yield return PCall(app, new { cmd = "batch", cmds = new object[] { new { cmd = "chara.motion", key = pA, look = false, physics = false }, new { cmd = "chara.motion", key = pB, look = false, physics = false } } });
            AddMods(ra, bones);
            yield return PFrames(3);
            yield return PCall(app, new { cmd = "scene.pause", paused = true });
            Check("the global pause", POk, Short(pLastText));
            string names = string.Join(", ", order.Select(x => x.name));
            foreach (var row in order)
            {
                yield return PCall(app, new { cmd = "batch", cmds = new object[] { new { cmd = "chara.anime", key = pA, group = row.g, category = row.c, no = row.n, time = 0.5f, speed = 0f }, new { cmd = "chara.anime", key = pB, group = row.g, category = row.c, no = row.n, time = 0.5f, speed = 0f } } });
                yield return PFrames(6);
                var w = new Worst();
                foreach (var b in bones) w.Add(Local(Bone(ra, b)), WithMods(Local(Bone(rb, b))), row.name);
                Check($"under the pause, {names}: at {row.name}, twin with the modifier once", w.Ok(1e-4, 0.01, 1e-4), w.ToString());
            }
            yield return PFrames(20);
            yield return PCall(app, new { cmd = "scene.pause", paused = false });
            yield return PFrames(6);
            var after = new Worst();
            foreach (var b in bones) after.Add(Local(Bone(ra, b)), WithMods(Local(Bone(rb, b))), "resumed");
            Check($"resumed, {names}: twin with the modifier once", after.Ok(1e-4, 0.01, 1e-4), after.ToString());
            // the shape that went wrong, once more after the pause
            foreach (var row in new[] { Chair, Idle })
            {
                yield return PCall(app, new { cmd = "batch", cmds = new object[] { new { cmd = "chara.anime", key = pA, group = row.g, category = row.c, no = row.n, time = 0.5f, speed = 0f }, new { cmd = "chara.anime", key = pB, group = row.g, category = row.c, no = row.n, time = 0.5f, speed = 0f } } });
                yield return PFrames(6);
                var w = new Worst();
                foreach (var b in bones) w.Add(Local(Bone(ra, b)), WithMods(Local(Bone(rb, b))), row.name);
                Check($"after the pause, {names}, then {row.name}: twin with the modifier once", w.Ok(1e-4, 0.01, 1e-4), w.ToString());
            }
            ra.Context.Bones.Remove("test");
        }
    }

    static bool BitEqual((Vector3 p, Quaternion r, Vector3 s) a, (Vector3 p, Quaternion r, Vector3 s) b) =>
        a.p.x == b.p.x && a.p.y == b.p.y && a.p.z == b.p.z && a.s.x == b.s.x && a.s.y == b.s.y && a.s.z == b.s.z
        && a.r.x == b.r.x && a.r.y == b.r.y && a.r.z == b.r.z && a.r.w == b.r.w;

    static IEnumerator FM13(AppCore app)
    {
        yield return PScene(app, 2, 0, "FM13");
        if (pB == null) yield break;
        yield return PFrames(5);
        var ra = PR(app, pA); var rb = PR(app, pB);
        AddMods(ra, ClassBones);
        yield return PFrames(5);
        var w = new Worst();
        for (int k = 0; k < 6; k++)
        {
            yield return PFrames(10);
            foreach (var b in ClassBones) w.Add(Local(Bone(ra, b)), WithMods(Local(Bone(rb, b))), b);
        }
        Check("auto step, 60 frames: base and modifier", w.Ok(1e-4, 0.01, 1e-4), w.ToString());
        var at = ClassBones.Select(b => Local(Bone(ra, b))).ToList();
        var wa = new Worst();
        for (int k = 0; k < 30; k++)
        {
            yield return PFrames(1);
            for (int i = 0; i < ClassBones.Length; i++) wa.Add(Local(Bone(ra, ClassBones[i])), at[i], ClassBones[i]);
        }
        Check("auto step at speed 0, 30 frames: the values hold", wa.Ok(1e-6, 0.001, 1e-6), wa.ToString());
        // by hand: a step, then frames without one
        yield return PCall(app, new { cmd = "chara.motion", key = pA, autoStep = false });
        Check("the character by hand", POk, Short(pLastText));
        yield return PFrames(2);
        at = ClassBones.Select(b => Local(Bone(ra, b))).ToList();
        int bad = 0;
        string first = "";
        for (int k = 0; k < 30; k++)
        {
            yield return PFrames(1);
            for (int i = 0; i < ClassBones.Length; i++)
                if (!BitEqual(Local(Bone(ra, ClassBones[i])), at[i])) { bad++; if (first.Length == 0) first = ClassBones[i] + " at frame " + k; }
        }
        Check("by hand: over 30 frames with no step the values are bit-equal", bad == 0, bad + " reads differ " + first);
        yield return PCall(app, new { cmd = "chara.motion", key = pA, step = new { dt = Dt, n = 1 } });
        Check("a hand step", POk, Short(pLastText));
        at = ClassBones.Select(b => Local(Bone(ra, b))).ToList();
        bad = 0;
        for (int k = 0; k < 30; k++)
        {
            yield return PFrames(1);
            for (int i = 0; i < ClassBones.Length; i++) if (!BitEqual(Local(Bone(ra, ClassBones[i])), at[i])) bad++;
        }
        Check("after a hand step: 30 frames bit-equal", bad == 0, bad + " reads differ");
        yield return PCall(app, new { cmd = "chara.motion", key = pA, autoStep = true });
        ra.Context.Bones.Remove("test");
    }

    static IEnumerator FM5PSequence(AppCore app, string key, string who)
    {
        var r = PR(app, key);
        var t = Bone(r, "cf_J_MouthMove");
        var face = r.Motion.Get<ExpressionStage>();
        var ms = ModScale(r, t);
        double worst = 0; string where = "";
        void Look(string what)
        {
            double d = Dist(t.localScale, Scaled(BareScale(r, t), ms));
            if (d > worst) { worst = d; where = what; }
        }
        yield return PFrames(5);
        Look("at rest");
        yield return PFrames(60);
        Look("60 frames");
        face.InvalidateMouthWidth();
        yield return PFrames(3);
        Look("the width written again");
        r.Motion.ResetAll();
        yield return PFrames(3);
        Look("a reset");
        r.Motion.ResetAll();
        yield return PFrames(3);
        Look("another reset");
        float was = face.VoiceValue;
        face.VoiceValue = 0.8f;
        for (int i = 0; i < 60; i++) { yield return PFrames(1); Look("speech frame " + i); }
        face.VoiceValue = was;
        yield return PFrames(3);
        Look("after speech");
        yield return PCall(app, new { cmd = "shape.set", key, faceIndex = 5, faceValue = 0.7f });
        yield return PFrames(3);
        Look("a face slider");
        using (StudioDefaultPoseScope.Enter(r)) { }
        yield return PFrames(3);
        Look("a thumbnail scope");
        Check(who + ": x, y, z hold the modifier once through the sequence", worst <= 1e-5, $"worst {worst:G3} ({where}); value {t.localScale.x:F4}, {t.localScale.y:F4}, {t.localScale.z:F4}");
    }

    static IEnumerator FM5P(AppCore app)
    {
        yield return PCall(app, new { cmd = "app.options", ext = new { abmx = true, accessoryControls = false }, fk = new { hipsOffset = false }, correctionAllOn = true });
        yield return PScene(app, 1, -1, "FM5P");
        if (pA != null)
        {
            var r = PR(app, pA);
            var t = Bone(r, "cf_J_MouthMove");
            Applier(r).Add(new HS2Next.Chara.Ext.BoneModifierApplier.Modifier { Target = t, Bone = "cf_J_MouthMove", Source = "test", Location = 1, Scale = new Vector3(1.2f, 1.3f, 1.4f) });
            HS2Next.Motion.Modifiers.BoneModifierSetup.Sync(r.Context);
            yield return FM5PSequence(app, pA, "synthetic");
        }
        foreach (var (hash, rel, sex) in ModCards("mouth"))
        {
            yield return PCall(app, new { cmd = "scene.new" });
            yield return PCall(app, new { cmd = "chara.load", path = rel, sex });
            pA = Key(pLast);
            if (!Check(hash + ": loaded", pA != null, Short(pLastText))) continue;
            yield return PCall(app, new { cmd = "chara.motion", key = pA, physics = false, look = false });
            yield return FM5PSequence(app, pA, hash);
        }
    }
}
