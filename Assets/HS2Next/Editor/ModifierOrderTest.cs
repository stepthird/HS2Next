// ModifierOrderTest (Phase 6.3a package M): a card's bone modifiers as motion stages. Headless, edit mode, a
// HeadlessBatch member; report Spike/out/advancedpose/modifier_order_report.txt. -only FM2+FM3 picks groups. "Twin" = the same card without the
// test modifiers, stepped by hand in lockstep. The checks live in partial files of AdvancedPoseTest, which holds the hosts and the helpers; a
// static method named Modifier_<part>() is a part of this row, in ordinal order of the names.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HS2Next.Chara.Ext;
using HS2Next.Motion;
using HS2Next.Motion.Modifiers;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class ModifierOrderTest
{
    public static void Run() => AdvancedPoseTest.RunModifierOrder();
}

public static partial class AdvancedPoseTest
{
    static string ModifierReport => Path.Combine(OutDir, "modifier_order_report.txt");
    static Newtonsoft.Json.Linq.JObject savedOptions;

    public static void RunModifierOrder()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        var head = new StringBuilder();
        try
        {
            services = SceneRuntimeTest.NewServices(head);
            LoadTargets();
            // the switches this row's hosts change are global: back as they were at its end
            var first = NewHost();
            savedOptions = first.Call(new { cmd = "app.options" });
            first.Dispose();
            rep.AppendLine($"ModifierOrderTest (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
            rep.Append(head);
            var parts = Parts("Modifier_", typeof(void));
            Info("parts: " + string.Join(", ", parts.Select(p => p.Name.Substring("Modifier_".Length))));
            foreach (var p in parts)
            {
                current = p.Name;
                try { p.Invoke(null, null); }
                catch (TargetInvocationException e) { Check("part " + p.Name, false, e.InnerException?.ToString() ?? e.ToString()); }
                catch (Exception e) { Check("part " + p.Name, false, e.ToString()); }
            }
        }
        catch (Exception e) { current = "run"; Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        if (savedOptions != null)
        {
            var last = NewHost();
            last.Call(new Newtonsoft.Json.Linq.JObject { ["cmd"] = "app.options", ["ext"] = savedOptions["ext"], ["fk"] = savedOptions["fk"], ["correctionAllOn"] = savedOptions["correctionAllOn"] }.ToString());
            last.Dispose();
            savedOptions = null;
        }
        End(ModifierReport, sw, "ModifierOrderTest.Run (headless)");
        HS2Next.Motion.Face.FaceSetup.Seed = null;
        services?.Catalog?.UnloadAll();
        Debug.Log($"MODIFIER ORDER TEST DONE: {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }

    // ------------------------------------------------------------ the fixture

    /// <summary>Two characters of one card, stepped by hand, physics and look off, on the card's default row: nothing bound but the T-pose row.</summary>
    static Twins NewBareTwins(string card, string sex, string what)
    {
        var f = new Twins { H = NewHost() };
        f.A = Key(f.H.Ok(new { cmd = "chara.load", path = card, sex }, what + ": chara.load A"));
        f.B = Key(f.H.Ok(new { cmd = "chara.load", path = card, sex }, what + ": chara.load B"));
        foreach (var k in new[] { f.A, f.B })
            f.H.Ok(new { cmd = "chara.motion", key = k, autoStep = false, physics = false, look = false, reset = true }, what + ": chara.motion by hand");
        f.Step(2, 0f);
        return f;
    }

    /// <summary>The test modifiers on these bones (rotation, position, length, scale), with their stages.</summary>
    static void AddAll(CharaRealization r, params string[] bones)
    {
        AddMods(r, bones);
        BoneModifierSetup.Sync(r.Context);
    }

    static void RowOn(Twins f, (int g, int c, int n, string name) row, float time = 0.5f, float speed = 0f)
    {
        foreach (var k in new[] { f.A, f.B })
            f.H.Ok(new { cmd = "chara.anime", key = k, group = row.g, category = row.c, no = row.n, time, speed }, "the row " + row.name);
    }

    // the worst of A against the twin with the modifiers composed, on these bones
    static void AgainstTwin(Twins f, string[] bones, Worst w, string where)
    {
        foreach (var b in bones) w.Add(Local(Bone(f.RA, b)), WithMods(Local(Bone(f.RB, b))), where + " " + b);
    }

    // ------------------------------------------------------------ the parts

    static void Modifier_M_Stages()
    {
        Group("FM1", "the stages, static", FM1);
        Group("FM2", "every class of channel shows the modifier once, on every row", FM2);
        Group("FM3", "a modifier does not depend on when the bind happened", FM3);
    }

    /// <summary>The target's stage names as they were before modifiers became stages (the last run of AO1 before the change prints this list).</summary>
    static readonly string[] CardWithoutModifiersStages = { "anim", "hands", "heels.lift", "fkhips", "fkneck", "ik", "heels.foot", "neck", "fk", "eyes", "face", "correct", "bust", "bustKinematic", "dynbone" };

    static void FM1()
    {
        var f = NewTwins(femaleRel, "female", "FM1");
        try
        {
            var r = f.RA;
            var names = r.Motion.Stages.Select(s => s.Name).ToList();
            Info("the target's stages: " + string.Join(",", names));
            Check("a card without modifiers has no applier", !r.Context.Bones);
            Check("a card without modifiers has no modifier stage", !names.Contains("bones.modifiers") && !names.Contains("bones.modifiers.end"), string.Join(",", names));
            Check("its stage list is the list before the change", names.SequenceEqual(CardWithoutModifiersStages), string.Join(",", names));
            Check("the applier has no LateUpdate", typeof(BoneModifierApplier).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) == null);
            var asm = typeof(BoneModifierApplier).Assembly;
            Check("no order -1 component", asm.GetTypes().All(t => t.Name != "BoneCorrectionLate"));
            Check("no order 1 component", asm.GetTypes().All(t => t.Name != "BoneCorrectionAfterModifiers"));
            AddAll(r, "cf_J_ArmUp00_R");
            var list = r.Motion.Stages.ToList();
            var pass = list.FirstOrDefault(s => s.Name == "bones.modifiers");
            var end = list.FirstOrDefault(s => s.Name == "bones.modifiers.end");
            if (!Check("Sync adds both stages", pass != null && end != null, string.Join(",", list.Select(s => s.Name)))) return;
            Check("phases 120 and 960", (int)pass.Phase == 120 && (int)end.Phase == 960, $"{(int)pass.Phase}, {(int)end.Phase}");
            bool sorted = true;
            for (int i = 1; i < list.Count; i++) sorted &= list[i - 1].Phase <= list[i].Phase;
            Check("the stage list stays sorted", sorted);
            int I(string n) => list.FindIndex(s => s.Name == n);
            Check("after the heel lift, before the hips offset", I("heels.lift") < I("bones.modifiers") && I("bones.modifiers") < I("fkhips"), string.Join(",", list.Select(s => s.Name)));
            Check("the end stage is last", list[list.Count - 1] == end, list[list.Count - 1].Name);
            UnityEngine.Object.DestroyImmediate(r.Context.Bones);
            r.Context.Bones = null;
            BoneModifierSetup.Sync(r.Context);
            names = r.Motion.Stages.Select(s => s.Name).ToList();
            Check("without the applier Sync takes both away", !names.Contains("bones.modifiers") && !names.Contains("bones.modifiers.end"), string.Join(",", names));
        }
        finally { f.Dispose(); }
    }

    /// <summary>A bone whose scale Chair_A's clip writes and Idle_00's, in the same controller, does not.</summary>
    const string FirstClipOnly = "cf_J_Kokan";

    // C, D, N and a shape-guard bone of the census
    static readonly string[] ClassBones = { "cf_J_ArmUp00_R", "cf_J_Foot02_L", "cf_J_EarBase_s_L", "cf_J_Spine01_s" };

    static void FM2()
    {
        foreach (var row in ReconRows)
        {
            var f = NewBareTwins(femaleRel, "female", "FM2");
            try
            {
                var guard = new HashSet<Transform>(f.RA.Animation.ShapeBones);
                if (row.name == ReconRows[0].name)
                    Check("the guard bone is a shape-guard bone and the others are not", guard.Contains(Bone(f.RA, "cf_J_Spine01_s")) && !guard.Contains(Bone(f.RA, "cf_J_ArmUp00_R")));
                AddAll(f.RA, ClassBones);
                f.Step(2, 0f);
                RowOn(f, row, 0f, 1f);
                f.Step(1, 0f);
                var w = new Worst();
                AgainstTwin(f, ClassBones, w, "the bind");
                for (int i = 1; i <= 600; i++)
                {
                    f.Step(1, Dt);
                    if (i % 25 == 0) AgainstTwin(f, ClassBones, w, row.name + " at step " + i);
                }
                Check($"{row.name}: 600 steps at 1/60: final = twin with the modifier composed", w.Ok(1e-4, 0.01, 1e-4), w.ToString());
                RowOn(f, row, 0.5f, 0f);
                f.Step(1, 0f);
                var at = ClassBones.Select(b => Local(Bone(f.RA, b))).ToList();
                var w0 = new Worst(); var drift = new Worst();
                for (int i = 1; i <= 600; i++)
                {
                    f.Step(1, 0f);
                    if (i % 50 == 0)
                    {
                        AgainstTwin(f, ClassBones, w0, row.name + " still, step " + i);
                        for (int b = 0; b < ClassBones.Length; b++) drift.Add(Local(Bone(f.RA, ClassBones[b])), at[b], ClassBones[b]);
                    }
                }
                Check($"{row.name}: 600 steps at 0: final = twin with the modifier composed", w0.Ok(1e-4, 0.01, 1e-4), w0.ToString());
                Check($"{row.name}: 600 steps at 0: no drift", drift.Ok(1e-6, 0.001, 1e-6), drift.ToString());
                Measure($"FM2 {row.name}: moving {w}; still {w0}; drift {drift}");
            }
            finally { f.Dispose(); }
        }
    }

    static void FM3()
    {
        var f = NewBareTwins(femaleRel, "female", "FM3");
        try
        {
            AddAll(f.RA, ClassBones);   // before the bind
            f.Step(2, 0f);
            RowOn(f, ReconRows[0]);
            f.Step(2, 0f);
            AddAll(f.RB, ClassBones);   // after the bind
            f.Step(2, 0f);
            var w = new Worst();
            foreach (var b in ClassBones) w.Add(Local(Bone(f.RA, b)), Local(Bone(f.RB, b)), b);
            Check("added before and after the bind: equal", w.Ok(1e-5, 0.01, 1e-5), w.ToString());
            var before = ClassBones.Select(b => Local(Bone(f.RA, b))).ToList();
            RowOn(f, ReconRows[1]);
            f.Step(5, 0f);
            var wo = new Worst();
            foreach (var b in ClassBones) wo.Add(Local(Bone(f.RA, b)), Local(Bone(f.RB, b)), b);
            Check("on another controller: equal", wo.Ok(1e-5, 0.01, 1e-5), wo.ToString());
            RowOn(f, ReconRows[0]);
            f.Step(5, 0f);
            var wb = new Worst();
            for (int i = 0; i < ClassBones.Length; i++) wb.Add(Local(Bone(f.RA, ClassBones[i])), before[i], ClassBones[i]);
            Check("a row switch and back: the values before", wb.Ok(1e-5, 0.01, 1e-5), wb.ToString());
            var wa = new Worst();
            foreach (var b in ClassBones) wa.Add(Local(Bone(f.RA, b)), Local(Bone(f.RB, b)), b);
            Check("after it: still equal", wa.Ok(1e-5, 0.01, 1e-5), wa.ToString());
        }
        finally { f.Dispose(); }

        // a channel one state of a controller writes and another does not (edit frames; real frames: FM3P)
        var g = NewBareTwins(femaleRel, "female", "FM3 a state's channel");
        try
        {
            string[] bones = { FirstClipOnly };
            AddAll(g.RA, bones);
            g.Step(2, 0f);
            foreach (var row in new[] { ReconRows[0], ReconRows[2], ReconRows[0], ReconRows[2], ReconRows[0] })
            {
                RowOn(g, row);
                g.Step(3, 0f);
                var w = new Worst();
                AgainstTwin(g, bones, w, row.name);
                Check(row.name + ": twin with the modifier once on a channel only Chair_A's clip writes", w.Ok(1e-4, 0.01, 1e-4), w.ToString());
            }
        }
        finally { g.Dispose(); }
    }
}
