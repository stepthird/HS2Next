// ConstraintTest, the scene post stage and BoneWritten on the real runtime (Phase 6.5 package O):
//   CX2   edit mode: the stage's place in AppCore.Frame, Tick and StepOnce, once per frame and per step, after the characters, before LateApply
//   CX2P  play mode, with an item that has motion: a real frame through ScenePostDriver, then StepOnce and Tick: after the items, before LateApply
//   CX4   no plugin with the interface: no ScenePostDriver, PostStage null; the driver lives exactly as long as the stage
//   CX5   a write after the step reported through BoneWritten: with a correction and a card modifier no drift over 300 frames; on a driven bone with no
//         correction the write does not outlive the driven value. Without the report it does: the check sees the case
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HS2Next.App;
using HS2Next.App.Plugins;
using HS2Next.App.Plugins.Engine;
using HS2Next.Content;
using HS2Next.Motion;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Overrides;
using HS2Next.Scenes.Runtime;
using UnityEngine;

public static partial class ConstraintTest
{
    static void Headless_C_Post()
    {
        Group("CX2", "the scene post stage's place, edit mode", CX2);
        Group("CX4", "no plugin, no driver", CX4);
        Group("CX5", "BoneWritten", CX5);
    }

    /// <summary>Marks what happens in a frame: the post stage and the late apply, with the item steps done by then.</summary>
    sealed class PostMarks : ISceneRuntimePart
    {
        public readonly List<string> Marks = new List<string>();
        public readonly List<float> Dts = new List<float>();
        public long ItemSeqAtPost = -1, ItemSeqAtLate = -1;
        public int CharaStepsAtPost = -1;
        public CharaRealization Chara;
        public void Post(float dt)
        {
            Marks.Add("post");
            Dts.Add(dt);
            ItemSeqAtPost = HS2Next.Scenes.Runtime.ItemMotion.Sequence;
            if (Chara != null) CharaStepsAtPost = Chara.Motion.StepCount;
        }
        public void Attach(SceneRuntime r) { }
        public void OnChanged(SceneChangeBatch b) { }
        public void Apply() { }
        public void LateApply() { Marks.Add("late"); ItemSeqAtLate = HS2Next.Scenes.Runtime.ItemMotion.Sequence; }
        public void Clear() { }
        public void OnLoadedRealized() { }
        public void Reset() { Marks.Clear(); Dts.Clear(); }
    }

    static void CX2()
    {
        var (h, key) = LayerHost("CX2");
        try
        {
            var rt = h.Rt;
            var c = rt.FindChara(key);
            var marks = new PostMarks { Chara = c };
            rt.AddPart(marks);
            rt.PostStage = marks.Post;
            Check("a test host has no driver of the stage", rt.Root.GetComponent<ScenePostDriver>() == null);

            // AppCore.Frame
            int s0 = c.Motion.StepCount;
            h.Frames(5);
            Check("AppCore.Frame: once per frame, before LateApply", string.Join(",", marks.Marks) == "post,late,post,late,post,late,post,late,post,late", string.Join(",", marks.Marks));
            Check("AppCore.Frame: the delta of the frame", marks.Dts.All(d => d == Dt), string.Join(",", marks.Dts));
            Check("AppCore.Frame: after the character's step", marks.CharaStepsAtPost == s0 + 5, (marks.CharaStepsAtPost - s0).ToString());

            // a paused frame: no time, the stage still runs
            marks.Reset();
            rt.SetPaused(true);
            h.Frames(2);
            Check("a paused frame: once, with no time", marks.Marks.Count(m => m == "post") == 2 && marks.Dts.All(d => d == 0f), string.Join(",", marks.Dts));

            // StepOnce
            marks.Reset();
            s0 = c.Motion.StepCount;
            bool stepped = rt.Step(3, 0.02f);
            Check("StepOnce: once per step, before LateApply", stepped && string.Join(",", marks.Marks) == "post,late,post,late,post,late", string.Join(",", marks.Marks));
            Check("StepOnce: the step's delta", marks.Dts.All(d => d == 0.02f), string.Join(",", marks.Dts));
            Check("StepOnce: after the characters", marks.CharaStepsAtPost == s0 + 3);
            rt.SetPaused(false);

            // Tick with manual stepping
            rt.SetManualStepping(true);
            marks.Reset();
            s0 = c.Motion.StepCount;
            rt.Tick(0.03f);
            rt.Tick(0.03f);
            Check("Tick: once per tick, before LateApply", string.Join(",", marks.Marks) == "post,late,post,late", string.Join(",", marks.Marks));
            Check("Tick: after the characters, with the frame's delta", marks.CharaStepsAtPost == s0 + 2 && marks.Dts.All(d => d == 0.03f));

            // Tick without manual stepping steps nothing, and runs no stage
            rt.SetManualStepping(false);
            marks.Reset();
            rt.Tick(0.03f);
            Check("Tick without manual stepping: no motion, no stage", !marks.Marks.Contains("post") && marks.Marks.Contains("late"), string.Join(",", marks.Marks));

            rt.PostStage = null;
            marks.Reset();
            h.Frames(1);
            Check("the stage taken away: not called", !marks.Marks.Contains("post"));
            rt.RemovePart(marks);
        }
        finally { h.Dispose(); }
    }

    static void CX4()
    {
        var (h, key) = LayerHost("CX4");
        try
        {
            var rt = h.Rt;
            Check("no plugin with the interface: PostStage null, no driver", rt.PostStage == null && rt.Root.GetComponent<ScenePostDriver>() == null);
            Check("and no motion pass, no pass driver", rt.Pass == null && rt.Root.GetComponent<MotionPassDriver>() == null);
            long calls = 0;
            rt.RunPost(0.1f);
            Check("RunPost is a null check", calls == 0);
            // the place of the driver in the frame
            int order = typeof(ScenePostDriver).GetCustomAttribute<DefaultExecutionOrder>()?.order ?? 0;
            Check("ScenePostDriver at 1075, MotionPassDriver at 1010", order == 1075 && typeof(MotionPassDriver).GetCustomAttribute<DefaultExecutionOrder>()?.order == 1010, order.ToString());
            Check("between the items and the late driver", typeof(ItemMotionDriver).GetCustomAttribute<DefaultExecutionOrder>().order < order && order < typeof(SceneRuntimeLateDriver).GetCustomAttribute<DefaultExecutionOrder>().order);
        }
        finally { h.Dispose(); }
    }

    static void CX5()
    {
        var log = new List<string>();
        var h = NewHost();
        PluginEngineServices svc = null;
        try
        {
            var rt = h.Rt;
            svc = new PluginEngineServices("test.post", new PluginGuard("test.post", l => log.Add(l)), l => log.Add(l), l => log.Add(l), () => rt, null);
            string Load(string tag) => Key(h.Ok(new { cmd = "chara.load", path = femaleRel, sex = "female" }, tag));
            string a = Load("twin A"), b = Load("twin B"), n = Load("twin without the report");
            foreach (var k in new[] { a, b, n })
                h.Ok(new { cmd = "chara.anime", key = k, group = 19, category = 0, no = 0, time = 0f, speed = 0f }, "the idle row");
            h.Frames(3);
            var ra = rt.FindChara(a); var rb = rt.FindChara(b); var rn = rt.FindChara(n);
            Check("a name that is no node: false", !svc.BoneWritten("nobody", B(ra, FreeBone)));
            Check("no bone: false", !svc.BoneWritten(a, null));

            // a correction and a card modifier: what the plugin wrote is off again in the next step, each frame
            AddMods(ra, FreeBone); AddMods(rb, FreeBone);
            h.Session.Set(a, "chara.advancedPose.bones", Map((FreeBone, OrderCorr)));
            h.Session.Set(b, "chara.advancedPose.bones", Map((FreeBone, OrderCorr)));
            h.Frames(3);
            var ta = B(ra, FreeBone); var tb = B(rb, FreeBone);
            var w = new Worst();
            int frames = 0;
            rt.PostStage = dt =>
            {
                frames++;
                w.Add(Local(ta), Local(tb), "at the post stage");
                ta.localRotation *= Quaternion.Euler(3f, 0f, 0f);
                ta.localPosition += new Vector3(0.001f, 0f, 0f);
                svc.BoneWritten(a, ta);
            };
            h.Frames(300);
            rt.PostStage = null;
            Check("a correction and a modifier: 300 frames, each starts from the twin's values, no drift", w.Ok() && frames == 300, w + ", " + frames + " frames");
            Measure("CX5 correction and modifier: " + w);
            h.Frames(2);
            var wo = new Worst();
            wo.Add(Local(ta), Local(tb), "after the writes stopped");
            Check("the writes stopped: the twin's values", wo.Ok(), wo.ToString());
            h.Session.Set(a, "chara.advancedPose.bones", Map());
            h.Session.Set(b, "chara.advancedPose.bones", Map());
            ra.Context.Bones.Remove("test"); rb.Context.Bones.Remove("test");
            HS2Next.Motion.Modifiers.BoneModifierSetup.Sync(ra.Context); HS2Next.Motion.Modifiers.BoneModifierSetup.Sync(rb.Context);
            h.Frames(3);

            // a driven bone with no correction: the write does not outlive the driven value
            const string bone = "cf_J_EarBase_s_R";
            var xa = B(ra, bone); var xb = B(rb, bone); var xn = B(rn, bone);
            var r0 = xb.localRotation;
            var v = r0 * Quaternion.Euler(20f, 10f, -15f);
            var sa = rt.Overrides.Acquire(Owner, a, OverridePath.BoneText(bone, "rot"), Prio);
            var sn = rt.Overrides.Acquire(Owner, n, OverridePath.BoneText(bone, "rot"), Prio);
            sa.Set(v.x, v.y, v.z, v.w); sn.Set(v.x, v.y, v.z, v.w);
            h.Frames(2);
            var wd = new Worst();
            frames = 0;
            rt.PostStage = dt =>
            {
                frames++;
                wd.Add(Local(xa), (xa.localPosition, v, xa.localScale), "at the post stage");
                xa.localRotation = Quaternion.Euler(40f, 0f, 0f);
                svc.BoneWritten(a, xa);
                xn.localRotation = Quaternion.Euler(40f, 0f, 0f);   // the same write, not reported
            };
            h.Frames(120);
            rt.PostStage = null;
            Check("a driven bone: the driven value stands at the post stage of each of 120 frames", wd.Ok() && frames == 120, wd.ToString());
            sa.Release(); sn.Release();
            h.Frames(2);
            Check("off: the reported write is gone, the pose stands", Angle(xa.localRotation, r0) <= 1e-3, Angle(xa.localRotation, r0).ToString("G3"));
            Check("off: the write nobody reported stays as the next input (the case BoneWritten is for)", Angle(xn.localRotation, r0) > 1f, Angle(xn.localRotation, r0).ToString("G3"));
            Measure($"CX5 driven bone, no correction: {wd}; after off {Angle(xa.localRotation, r0):G3} deg against {Angle(xn.localRotation, r0):G3} deg without the report");
        }
        finally { svc?.Unload(); h.Dispose(); }
    }

    // ------------------------------------------------------------ play mode

    static IEnumerator Play_C_Post()
    {
        current = "Post";
        if (!POpen("CX2P", "the scene post stage in a real frame")) yield break;
        var ready = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ready);
        var app = AppCore.Current;
        if (!Check("the app runs with its bridge and runtime", ready[0] && app != null && app.Stage == AppStage.Ready && app.Bridge != null && app.Runtime != null, app?.Stage.ToString())) yield break;
        var rt = app.Runtime;
        Check("no plugin: no post driver, no stage", rt.PostStage == null && rt.Root.GetComponent<ScenePostDriver>() == null);
        yield return PScene(app, 1, 1, "CX2P");
        if (pA == null) yield break;
        yield return PCall(app, new { cmd = "item.add", no = 498, parent = pA, point = "N_Hand_R" });
        Check("an item with motion in the hand", POk, Short(pLastText));
        string item = Key(pLast);
        yield return PFrames(3);
        var im = HS2Next.Scenes.Runtime.ItemMotion.Of(rt.FindItem(item));
        if (!Check("the item has motion", im?.Dynamic != null)) yield break;
        var marks = new PostMarks { Chara = PR(app, pA) };
        rt.AddPart(marks);
        rt.PostStage = marks.Post;
        Check("the driver comes with the stage", rt.Root.GetComponent<ScenePostDriver>() != null);
        int s0 = marks.Chara.Motion.StepCount;
        yield return PFrames(30);
        var post = marks.Marks.Count(m => m == "post"); var late = marks.Marks.Count(m => m == "late");
        bool alternates = true;
        for (int i = 1; i < marks.Marks.Count; i++) alternates &= marks.Marks[i] != marks.Marks[i - 1];
        Check("once per real frame, before LateApply", post >= 30 && Math.Abs(post - late) <= 1 && alternates, $"{post} post, {late} late, alternating {alternates}");
        Check("after the character's step of the frame", marks.CharaStepsAtPost > s0);
        Check("after the item's step of the frame, and nothing steps before LateApply", marks.ItemSeqAtPost == im.LastStepSequence && marks.ItemSeqAtLate == marks.ItemSeqAtPost, $"{marks.ItemSeqAtPost}, {im.LastStepSequence}, {marks.ItemSeqAtLate}");

        // a step
        yield return PCall(app, new { cmd = "scene.pause", paused = true });
        yield return PFrames(1);
        marks.Reset();
        long seq = im.LastStepSequence;
        rt.Step(3, 0.02f);
        Check("StepOnce: once per step, before LateApply", marks.Dts.Count == 3 && marks.Dts.All(d => d == 0.02f), string.Join(",", marks.Marks));
        Check("StepOnce: after the items", im.LastStepSequence == seq + 3 && marks.ItemSeqAtPost == im.LastStepSequence, $"{im.LastStepSequence - seq}");
        yield return PCall(app, new { cmd = "scene.pause", paused = false });

        // manual stepping
        rt.SetManualStepping(true);
        marks.Reset();
        seq = im.LastStepSequence;
        rt.Tick(0.03f);
        Check("Tick: once, after the items, before LateApply", string.Join(",", marks.Marks) == "post,late" && im.LastStepSequence == seq + 1 && marks.ItemSeqAtPost == im.LastStepSequence, string.Join(",", marks.Marks));
        rt.SetManualStepping(false);
        rt.PostStage = null;
        Check("the driver goes with the stage", rt.Root.GetComponent<ScenePostDriver>() == null);
        marks.Reset();
        yield return PFrames(3);
        Check("no stage: not called", !marks.Marks.Contains("post"));
        rt.RemovePart(marks);
        yield return PCall(app, new { cmd = "scene.new" });
    }
}
