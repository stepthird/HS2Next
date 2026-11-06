// ConstraintTest, the order of a frame with real constraints (Phase 6.5 package O), on the SFW targets, by numbers.
//   CO3   the chain of CO2 with the list reversed, with B before A in the tree, with its second constraint first: equal, bit for bit (edit mode)
//   CO15  autoStep false, paused, time scale 0, a step by hand (edit mode)
//   CW3   the walk and the evaluation allocate nothing: 600 walks with the characters held out of it, 600 runs of stage 440 (edit mode; the stages' own closures
//         in CharaMotion.Step lie outside what is counted)
//   CO4   the chain in real frames, 600 frames of dance_00: no lag on any frame
//   CO5   a left hand on a rod in the right hand: lag 1, exact one frame behind, exact at rest; two characters' cross case: the lowest index lags
//   CO13  an item in A's hand and an item's bone as sources of B's IK target: the item steps once per frame, before B
//   CO17  parent and child both driven, bones and nodes, the list in both orders: the child's world value on its source on every frame of 600
//   CO18  a character under its own pause, then at time scale 0, whose holder, IK target, bone follows a moving source: no settle while it moves, one after
//   CO19  a bone target with a source bone of its own character reads the pose without a correction above the source; another character's source has it
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.Motion;
using HS2Next.Motion.AdvancedPose;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Overrides;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class ConstraintTest
{
    static void Headless_D_Order()
    {
        Group("CO3", "the chain does not depend on list or tree order", CO3);
        Group("CO15", "characters that do not step by themselves", CO15);
        Group("CW3", "no allocation", CW3);
    }

    static ConstraintPart Part(AppCore app) => app.Runtime.GetPart<ConstraintPart>();

    static void PutList(SceneSession s, SceneRuntime rt, string what, params ConstraintEntry[] entries)
    {
        var r = s.Commands.Execute(ConstraintEditCommand.ReplaceList(new ConstraintList(entries), ConstraintEditCommand.LabelAdd));
        Check(what, r.Ok, r.Ok ? "" : r.Code + ": " + r.Message);
        rt.ApplyNow();
    }

    // ------------------------------------------------------------ CO3

    /// <summary>The chain on the given tree and list order: the bits of N1, B's hand and N2 at every tick.</summary>
    static List<int[]> ChainTrace(string what, bool bFirst, int[] order, int ticks)
    {
        var h = NewHost();
        var trace = new List<int[]>();
        try
        {
            string a, b;
            if (bFirst) { b = LoadChara(h, femaleRel, "female", what + ": B"); a = LoadChara(h, femaleRel, "female", what + ": A"); }
            else { a = LoadChara(h, femaleRel, "female", what + ": A"); b = LoadChara(h, femaleRel, "female", what + ": B"); }
            h.Ok(new { cmd = "chara.anime", key = a, group = 11, category = 5, no = 0, time = 0f, speed = 1f }, what + ": dance_00 on A");
            h.Ok(new { cmd = "chara.anime", key = b, group = 19, category = 0, no = 0, time = 0f, speed = 1f }, what + ": the idle row on B");
            h.Ok(new { cmd = "ik.copy", key = b, from = "current", enable = true }, what + ": IK on B");
            h.Frames(2);
            var ra = h.Rt.FindChara(a); var rb = h.Rt.FindChara(b);
            var handA = B(ra, "cf_J_Hand_R"); var handB = B(rb, "cf_J_Hand_L");
            Place(h, b, handA.position - handB.position);
            h.Frames(1);
            string n1 = Node(h, what + ": N1"), n2 = Node(h, what + ": N2");
            var e = new[]
            {
                CEntry(new ConstraintEnd(a, "cf_J_Hand_R"), new ConstraintEnd(n1), ConstraintComponents.Parent),
                CEntry(new ConstraintEnd(n1), new ConstraintEnd(b, ik: "LeftHand"), ConstraintComponents.Position),
                CEntry(new ConstraintEnd(b, "cf_J_Hand_L"), new ConstraintEnd(n2), ConstraintComponents.Parent),
            };
            Put(h, what + ": the list", order.Select(i => e[i]).ToArray());
            Check(what + ": no lag", Part(h).Lagged.Count == 0 && Part(h).Active == 3, Part(h).Active + " active");
            var h1 = h.Rt.HolderOf(n1); var h2 = h.Rt.HolderOf(n2);
            for (int i = 0; i < ticks; i++)
            {
                h.Frames(1);
                var v = new[] { h1.position, handB.position, h2.position };
                trace.Add(v.SelectMany(x => new[] { BitConverter.SingleToInt32Bits(x.x), BitConverter.SingleToInt32Bits(x.y), BitConverter.SingleToInt32Bits(x.z) }).ToArray());
            }
        }
        finally { h.Dispose(); }
        return trace;
    }

    static void CO3()
    {
        const int ticks = 90;
        var a = ChainTrace("in order", false, new[] { 0, 1, 2 }, ticks);
        var variants = new (string name, List<int[]> t)[]
        {
            ("the list reversed", ChainTrace("the list reversed", false, new[] { 2, 1, 0 }, ticks)),
            ("B before A in the tree", ChainTrace("B first", true, new[] { 0, 1, 2 }, ticks)),
            ("the second constraint first", ChainTrace("the second first", false, new[] { 1, 0, 2 }, ticks)),
            ("B first and the list reversed", ChainTrace("B first, reversed", true, new[] { 2, 1, 0 }, ticks)),
        };
        int moved = 0;
        for (int i = 1; i < a.Count; i++) if (!a[i].SequenceEqual(a[i - 1])) moved++;
        Check("the chain moves", moved > ticks / 2, moved + " of " + ticks + " ticks change");
        foreach (var (name, t) in variants)
        {
            int differ = 0;
            for (int i = 0; i < ticks && i < t.Count; i++) if (!a[i].SequenceEqual(t[i])) differ++;
            Check(name + ": equal, bit for bit, on every tick", differ == 0 && t.Count == ticks, differ + " of " + ticks + " differ");
        }
    }

    // ------------------------------------------------------------ CO15

    static void CO15()
    {
        var h = NewHost();
        try
        {
            var a = LoadChara(h, femaleRel, "female", "A");
            var b = LoadChara(h, femaleRel, "female", "B");
            h.Ok(new { cmd = "chara.anime", key = a, group = 11, category = 5, no = 0, time = 0f, speed = 1f }, "dance_00 on A");
            h.Ok(new { cmd = "chara.anime", key = b, group = 19, category = 0, no = 0, time = 0f, speed = 0f }, "the idle row on B");
            h.Frames(2);
            var ra = h.Rt.FindChara(a); var rb = h.Rt.FindChara(b);
            string n = Node(h, "N");
            var e1 = CEntry(new ConstraintEnd(a, "cf_J_Hand_R"), new ConstraintEnd(n), ConstraintComponents.Position);
            var e2 = CEntry(new ConstraintEnd(a, "cf_J_Hand_R"), new ConstraintEnd(b, FreeBone), ConstraintComponents.Rotation);
            Put(h, "a node and a bone follow A's hand", e1, e2);
            var hn = h.Rt.HolderOf(n); var handA = B(ra, "cf_J_Hand_R"); var bone = B(rb, FreeBone);
            h.Frames(3);

            // autoStep false on the source: not stepped by the pass, its K units still run
            h.Ok(new { cmd = "chara.motion", key = a, autoStep = false }, "autoStep off on A");
            int s0 = ra.Motion.StepCount, sb = rb.Motion.StepCount;
            var p0 = handA.position;
            h.Frames(5);
            Check("autoStep off: the pass does not step it", ra.Motion.StepCount == s0, (ra.Motion.StepCount - s0).ToString());
            Check("the others step", rb.Motion.StepCount == sb + 5);
            Check("its pose stands, the node on it", (handA.position - p0).magnitude == 0 && (hn.position - handA.position).magnitude <= 1e-5);

            // a step by hand of the source: the node follows in the next frame's K
            h.Ok(new { cmd = "chara.motion", key = a, step = new { dt = 0.1f, n = 1 } }, "a step by hand");
            Check("the hand step moved A", ra.Motion.StepCount == s0 + 1 && (handA.position - p0).magnitude > 1e-4);
            h.Frames(1);
            Check("the next frame's unit reads it", (hn.position - handA.position).magnitude <= 1e-5);

            // autoStep false on the bone's character: the target waits for a step
            h.Ok(new { cmd = "chara.motion", key = a, autoStep = true }, "autoStep on on A");
            h.Ok(new { cmd = "chara.motion", key = b, autoStep = false }, "autoStep off on B");
            var q0 = bone.rotation;
            sb = rb.Motion.StepCount;
            h.Frames(5);
            Check("a bone target on a character that does not step waits", rb.Motion.StepCount == sb && Deg(bone.rotation, q0) == 0f);
            // the source holds still for this one: a request's reply pumps a frame, and A would step after B read it
            h.Ok(new { cmd = "chara.motion", key = a, autoStep = false }, "autoStep off on A for the read");
            h.Ok(new { cmd = "chara.motion", key = b, step = new { dt = 0.016f, n = 1 } }, "a step by hand of B");
            Check("a step by hand runs stage 440 and reads its sources", Deg(bone.rotation, handA.rotation) <= 0.02f, Deg(bone.rotation, handA.rotation).ToString("G3"));
            h.Ok(new { cmd = "chara.motion", key = a, autoStep = true }, "autoStep on on A");
            h.Ok(new { cmd = "chara.motion", key = b, autoStep = true }, "autoStep on on B");

            // paused, time scale 0: it steps at dt 0 in the pass and the constraints act
            foreach (var (name, args) in new (string, object)[] { ("paused", new { cmd = "chara.motion", key = b, paused = true }), ("time scale 0", new { cmd = "chara.motion", key = b, paused = false, timeScale = 0f }) })
            {
                h.Ok(args, name + " on B");
                sb = rb.Motion.StepCount;
                float tb = rb.Motion.Time;
                h.Frames(5);
                Check(name + ": it steps in the pass, no time passes", rb.Motion.StepCount == sb + 5 && rb.Motion.Time == tb, $"{rb.Motion.StepCount - sb} steps, time {rb.Motion.Time - tb}");
                Check(name + ": the constraint on its bone acts", Deg(bone.rotation, handA.rotation) <= 0.02f, Deg(bone.rotation, handA.rotation).ToString("G3"));
            }
            h.Ok(new { cmd = "chara.motion", key = b, paused = false, timeScale = 1f }, "B back");

            // hidden characters step
            h.Session.Set(a, "visible", false);
            h.Rt.ApplyNow();
            s0 = ra.Motion.StepCount;
            h.Frames(3);
            Check("a hidden source steps and is read", ra.Motion.StepCount == s0 + 3 && (hn.position - handA.position).magnitude <= 1e-5);
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ CW3

    static void CW3()
    {
        var h = NewHost();
        try
        {
            var a = LoadChara(h, femaleRel, "female", "A");
            var b = LoadChara(h, femaleRel, "female", "B");
            foreach (var k in new[] { a, b }) h.Ok(new { cmd = "chara.anime", key = k, group = 11, category = 5, no = 0, time = 0f, speed = 1f }, "dance_00");
            h.Ok(new { cmd = "ik.copy", key = b, from = "current", enable = true }, "IK on B");
            h.Frames(2);
            string n1 = Node(h, "a node"), n2 = Node(h, "a node");
            var list = new List<ConstraintEntry>
            {
                CEntry(new ConstraintEnd(a, "cf_J_Hand_R"), new ConstraintEnd(n1), ConstraintComponents.Parent),
                CEntry(new ConstraintEnd(n1), new ConstraintEnd(n2), ConstraintComponents.Position, ConstraintComponents.Rotation),
                CEntry(new ConstraintEnd(n1), new ConstraintEnd(b, ik: "LeftHand"), ConstraintComponents.Position, ConstraintComponents.Rotation),
                CEntry(new ConstraintEnd(a, "cf_J_Hand_L"), new ConstraintEnd(b, FreeBone), ConstraintComponents.Position, ConstraintComponents.Rotation),
                CEntry(new ConstraintEnd(a, "cf_J_Head"), new ConstraintEnd(b, "cf_J_Head"), ConstraintComponents.LookAt),
            };
            list[1].Weight = 0.5f;
            Put(h, "five constraints of the three target kinds", list.ToArray());
            Check("all active", Part(h).Active == 5, Part(h).Active.ToString());
            h.Frames(5);
            var ra = h.Rt.FindChara(a); var rb = h.Rt.FindChara(b);
            var set = DrivenPoseDriver.Of(rb)?.Set;
            if (!Check("the driven set", set != null && set.Evaluator != null)) return;
            var frame = new MotionFrame(0f, 0f, 0f, 0);
            // the characters held out of the walk: what remains is the walk, its guards and the units
            h.Ok(new { cmd = "chara.motion", key = a, autoStep = false }, "A held");
            h.Ok(new { cmd = "chara.motion", key = b, autoStep = false }, "B held");
            var pass = h.Rt.Pass;
            // a source that moves, so the values change and the slots are really written
            var moving = B(ra, "cf_J_Hand_R");
            var rest = moving.localRotation;
            void Turn(int i) => moving.localRotation = rest * Quaternion.Euler(0f, 0f, 0.01f * (i % 100));
            for (int i = 0; i < 30; i++) { Turn(i); pass.Run(Dt, false); set.Restore(); set.Apply(frame); set.End(); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 600; i++) { Turn(i); pass.Run(Dt, false); }
            long walk = GC.GetAllocatedBytesForCurrentThread() - before;
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 600; i++) { Turn(i); set.Restore(); set.Apply(frame); set.End(); }
            long stage = GC.GetAllocatedBytesForCurrentThread() - before;
            Check("600 walks: 0 B", walk == 0, walk + " B");
            Check("600 runs of the stages: 0 B", stage == 0, stage + " B");
            Measure($"CW3: walk {walk} B, stages {stage} B over 600");
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ play mode

    static IEnumerator Play_D_Order()
    {
        current = "Order";
        if (!new[] { "CO4", "CO5", "CO13", "CO17", "CO18", "CO19" }.Any(Want)) yield break;
        var ready = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ready);
        var app = AppCore.Current;
        if (!Check("the app runs with its bridge and runtime", ready[0] && app != null && app.Stage == AppStage.Ready && app.Bridge != null && app.Runtime != null, app?.Stage.ToString())) yield break;
        float old = Time.captureDeltaTime;
        Time.captureDeltaTime = Dt;
        HS2Next.Motion.Face.FaceSetup.Seed = 0x5eed;
        try
        {
            if (POpen("CO4", "the chain in real frames: no lag")) yield return CO4(app);
            if (POpen("CO5", "the declared lag")) yield return CO5(app);
            if (POpen("CO13", "items in the chain step once, before their reader")) yield return CO13(app);
            if (POpen("CO17", "parents and children both driven")) yield return CO17(app);
            if (POpen("CO18", "a moving source costs a still character no settle per frame")) yield return CO18(app);
            if (POpen("CO19", "a source bone of the target's own character")) yield return CO19(app);
            yield return PCall(app, new { cmd = "scene.pause", paused = false });
            yield return PCall(app, new { cmd = "scene.new" });
        }
        finally { Time.captureDeltaTime = old; HS2Next.Motion.Face.FaceSetup.Seed = null; }
    }

    static IEnumerator AddNode(AppCore app, string what, Action<string> got)
    {
        yield return PCall(app, new { cmd = "item.add", no = 399 });
        Check(what, POk, POk ? "" : Short(pLastText));
        got(Key(pLast));
    }

    static IEnumerator CO4(AppCore app)
    {
        yield return PScene(app, 2, -1, "CO4");
        if (pB == null) yield break;
        yield return PCall(app, new { cmd = "batch", cmds = new object[]
        {
            new { cmd = "chara.anime", key = pA, group = 11, category = 5, no = 0, time = 0f, speed = 0f },
            new { cmd = "chara.anime", key = pB, group = 11, category = 5, no = 0, time = 0f, speed = 0f },
        } });
        yield return PCall(app, new { cmd = "ik.copy", key = pB, from = "current", enable = true });
        Check("IK on B", POk, Short(pLastText));
        yield return PFrames(3);
        var ra = PR(app, pA); var rb = PR(app, pB);
        // the chest of A: it moves through the dance and stays within reach of an arm of B, who dances beside it
        const string sourceBone = "cf_J_Spine03";
        var handA = B(ra, sourceBone); var handB = B(rb, "cf_J_Hand_L");
        var upB = B(rb, "cf_J_ArmUp00_L");
        float arm = (upB.position - B(rb, "cf_J_ArmLow01_L").position).magnitude + (B(rb, "cf_J_ArmLow01_L").position - handB.position).magnitude;
        // B dances too, its left shoulder half an arm off A's chest, and only the left arm solves
        var at = rb.Holder.position + handA.position - upB.position + new Vector3(0f, 0f, arm * 0.5f);
        app.Session.Set(pB, "transform.pos", new Vec3(at.x, at.y, at.z));
        app.Runtime.ApplyNow();
        yield return PCall(app, new { cmd = "ik.enable", key = pB, on = false, groups = new[] { "body", "rightLeg", "leftLeg", "rightArm" } });
        Check("only the left arm solves", POk, Short(pLastText));
        yield return PCall(app, new { cmd = "batch", cmds = new object[]
        {
            new { cmd = "chara.anime", key = pA, group = 11, category = 5, no = 0, time = 0f, speed = 1f },
            new { cmd = "chara.anime", key = pB, group = 11, category = 5, no = 0, time = 0f, speed = 1f },
        } });
        yield return PFrames(2);
        string n1 = null, n2 = null;
        yield return AddNode(app, "N1", k => n1 = k);
        yield return AddNode(app, "N2", k => n2 = k);
        var e1 = CEntry(new ConstraintEnd(pA, sourceBone), new ConstraintEnd(n1), ConstraintComponents.Parent);
        var e2 = CEntry(new ConstraintEnd(n1), new ConstraintEnd(pB, ik: "LeftHand"), ConstraintComponents.Position);
        var e3 = CEntry(new ConstraintEnd(pB, "cf_J_Hand_L"), new ConstraintEnd(n2), ConstraintComponents.Parent);
        PutList(app.Session, app.Runtime, "the chain", e1, e2, e3);
        Check("no lag declared", Part(app).Lagged.Count == 0 && Part(app).Active == 3, Part(app).Active.ToString());
        yield return PFrames(2);
        var h1 = app.Runtime.HolderOf(n1); var h2 = app.Runtime.HolderOf(n2);
        var ik = IkDriver.Of(rb).Stage;
        int hand = Array.IndexOf(IkTargets.Names, "LeftHand");
        double w1 = 0, w2 = 0, w3 = 0, moved = 0;
        int clamped = 0;
        var last = handA.position;
        double far = 0, near = double.MaxValue, res = 0;
        yield return Sampled(app, 600, () =>
        {
            double reach = (h1.position - upB.position).magnitude;
            far = Math.Max(far, reach); near = Math.Min(near, reach);
            res = Math.Max(res, Math.Abs((handB.position - h1.position).magnitude - ik.LastSolve.Residual[hand]));
            w1 = Math.Max(w1, (h1.position - handA.position).magnitude);
            w2 = Math.Max(w2, (h2.position - handB.position).magnitude);
            if (ik.LastSolve.Clamped[hand]) clamped++;
            else w3 = Math.Max(w3, (handB.position - h1.position).magnitude);
            moved = Math.Max(moved, (handA.position - last).magnitude);
            last = handA.position;
        });
        Check("N1 on A's final bone on every frame", w1 <= 1e-5, w1.ToString("G3"));
        Measure("CO4 reach: " + (600 - clamped) + " of 600 frames");
        Check("B's hand on N1 on every frame it reaches", w3 <= 1e-5, w3.ToString("G3"));
        Check("N2 on B's final bone on every frame", w2 <= 1e-5, w2.ToString("G3"));
        Check("on a frame it does not reach, the hand stands as far off as the solve says", res <= 1e-4, res.ToString("G3"));
        Check("the arm reaches on most frames", clamped < 300, clamped + " of 600 frames clamped");
        Check("the source moves from frame to frame, so a frame behind would show", moved > 1e-3, moved.ToString("G3"));
        Measure($"CO4: N1 {w1:G3}, hand {w3:G3}, N2 {w2:G3}, clamped {clamped} of 600, A's hand moves up to {moved:G3} a frame; shoulder to N1 {near:G3} to {far:G3}, the arm {arm:G3}");
    }

    static IEnumerator CO5(AppCore app)
    {
        yield return PScene(app, 1, -1, "CO5");
        if (pA == null) yield break;
        yield return PCall(app, new { cmd = "chara.anime", key = pA, group = 11, category = 5, no = 0, time = 0f, speed = 1f });
        yield return PCall(app, new { cmd = "ik.copy", key = pA, from = "current", enable = true });
        Check("IK on A", POk, Short(pLastText));
        // only the left arm solves: the rod rides on the clip's right hand
        yield return PCall(app, new { cmd = "ik.enable", key = pA, on = false, groups = new[] { "body", "rightLeg", "leftLeg", "rightArm" } });
        Check("the other groups off", POk, Short(pLastText));
        yield return PCall(app, new { cmd = "item.add", no = 399, parent = pA, point = "N_Hand_R" });
        Check("a rod in the right hand", POk, Short(pLastText));
        string rod = Key(pLast);
        yield return PFrames(3);
        var ra = PR(app, pA);
        var e = CEntry(new ConstraintEnd(rod), new ConstraintEnd(pA, ik: "LeftHand"), ConstraintComponents.Position);
        PutList(app.Session, app.Runtime, "the left hand on the rod", e);
        var row = Part(app).RowOf(e.Id);
        Check("no true loop: active, marked lag 1", row?.State == ConstraintStates.Active && row.Lag && Part(app).Lagged.Contains(e.Id), row?.State + " lag " + row?.Lag);
        yield return PFrames(2);
        var holder = app.Runtime.HolderOf(rod);
        var hand = B(ra, "cf_J_Hand_L");
        var ik = IkDriver.Of(ra).Stage;
        int ti = Array.IndexOf(IkTargets.Names, "LeftHand");
        Vector3 prev = holder.position;
        double behind = 0, same = 0, moved = 0, res = 0;
        int reached = 0;
        var root = ra.Root.transform;
        yield return Sampled(app, 300, () =>
        {
            // the target as the solve took it, against the rod of the frame before and of this frame
            var target = root.TransformPoint(ik.TargetOverrides[ti].Pos);
            behind = Math.Max(behind, (target - prev).magnitude);
            same = Math.Max(same, (target - holder.position).magnitude);
            moved = Math.Max(moved, (holder.position - prev).magnitude);
            res = Math.Max(res, Math.Abs((hand.position - target).magnitude - ik.LastSolve.Residual[ti]));
            if (!ik.LastSolve.Clamped[ti]) reached++;
            prev = holder.position;
        });
        // the root does not move here, so a root-local value compares in the world
        Check("frame n: the target equals the rod at n-1", behind <= 1e-5, behind.ToString("G3"));
        Check("and not the rod at n: the case is real", same > 1e-3 && moved > 1e-3, $"{same:G3}, the rod moves up to {moved:G3} a frame");
        Check("the left hand stands on that target within the solve's residual", res <= 1e-4, res.ToString("G3"));
        Measure($"CO5 grip: to the rod at n-1 {behind:G3}, to the rod at n {same:G3}, reached {reached} of 300");
        yield return PCall(app, new { cmd = "scene.pause", paused = true });
        Check("the global pause", POk, Short(pLastText));
        yield return PFrames(2);
        var target2 = root.TransformPoint(ik.TargetOverrides[ti].Pos);
        Check("paused: exact in the next frame", (target2 - holder.position).magnitude <= 1e-5, (target2 - holder.position).magnitude.ToString("G3"));
        yield return PCall(app, new { cmd = "scene.pause", paused = false });

        // two characters, each steering a limb of the other
        yield return PScene(app, 2, 1, "CO5 cross");
        if (pB == null) yield break;
        foreach (var k in new[] { pA, pB })
        {
            yield return PCall(app, new { cmd = "ik.copy", key = k, from = "current", enable = true });
            Check("IK on", POk, Short(pLastText));
        }
        yield return PFrames(3);
        var e1 = CEntry(new ConstraintEnd(pB, "cf_J_Hand_R"), new ConstraintEnd(pA, ik: "LeftHand"), ConstraintComponents.Position);
        var e2 = CEntry(new ConstraintEnd(pA, "cf_J_Hand_R"), new ConstraintEnd(pB, ik: "LeftHand"), ConstraintComponents.Position);
        PutList(app.Session, app.Runtime, "the cross", e1, e2);
        var r1 = Part(app).RowOf(e1.Id); var r2 = Part(app).RowOf(e2.Id);
        Check("both active, the lowest list index takes the lag", r1?.State == ConstraintStates.Active && r2?.State == ConstraintStates.Active && r1.Lag && !r2.Lag, $"{r1?.State} {r1?.Lag}, {r2?.State} {r2?.Lag}");
        PutList(app.Session, app.Runtime, "the cross, the list the other way", e2, e1);
        r1 = Part(app).RowOf(e1.Id); r2 = Part(app).RowOf(e2.Id);
        Check("the list the other way: the other one lags", !r1.Lag && r2.Lag, $"{r1.Lag}, {r2.Lag}");
    }

    static IEnumerator CO13(AppCore app)
    {
        yield return PScene(app, 2, 1, "CO13");
        if (pB == null) yield break;
        // 498 has dynamic bones: an item with motion in A's right hand
        yield return PCall(app, new { cmd = "item.add", no = 498, parent = pA, point = "N_Hand_R" });
        Check("an item with motion in A's hand", POk, Short(pLastText));
        string item = Key(pLast);
        yield return PCall(app, new { cmd = "ik.copy", key = pB, from = "current", enable = true });
        Check("IK on B", POk, Short(pLastText));
        yield return PFrames(3);
        var ra = PR(app, pA); var rb = PR(app, pB);
        var im = HS2Next.Scenes.Runtime.ItemMotion.Of(app.Runtime.FindItem(item));
        if (!Check("the item has motion", im?.Dynamic != null)) yield break;
        var chain = im.Dynamic.Chains.First(c => c.ParticleCount > 1);
        string boneName = chain.ParticleTransform(1).name;
        var e1 = CEntry(new ConstraintEnd(item), new ConstraintEnd(pB, ik: "LeftHand"), ConstraintComponents.Position);
        var e2 = CEntry(new ConstraintEnd(item, boneName), new ConstraintEnd(pB, ik: "RightHand"), ConstraintComponents.Position);
        PutList(app.Session, app.Runtime, "B's hands on the item and on its bone", e1, e2);
        var order = Part(app).Order.Select(o => o.Unit == pA ? "A" : o.Unit == pB ? "B" : o.Unit == item ? "I" : o.Unit == e1.Id ? "K1" : o.Unit == e2.Id ? "K2" : "?").ToList();
        // the item's holder is final once A has stepped; its bone once the item has
        Check("the item is a unit, after A, before the reader of its bone and before B", order.IndexOf("A") < order.IndexOf("I") && order.IndexOf("A") < order.IndexOf("K1") && order.IndexOf("I") < order.IndexOf("K2") && order.IndexOf("K1") < order.IndexOf("B") && order.IndexOf("K2") < order.IndexOf("B"), string.Join(" ", order));
        Check("no lag", Part(app).Lagged.Count == 0);
        yield return PFrames(2);
        var ik = IkDriver.Of(rb).Stage;
        int t1 = Array.IndexOf(IkTargets.Names, "LeftHand"), t2 = Array.IndexOf(IkTargets.Names, "RightHand");
        var holder = app.Runtime.HolderOf(item); var bone = chain.ParticleTransform(1);
        var root = rb.Root.transform;
        int once = 0; long seq = im.LastStepSequence; double w1 = 0, w2 = 0; int frame = -1, sameFrame = 0;
        yield return Sampled(app, 120, () =>
        {
            if (im.LastStepSequence == seq + 1) once++;
            seq = im.LastStepSequence;
            if (im.LastStepFrame == Time.frameCount) sameFrame++;
            frame = Time.frameCount;
            w1 = Math.Max(w1, (root.TransformPoint(ik.TargetOverrides[t1].Pos) - holder.position).magnitude);
            w2 = Math.Max(w2, (root.TransformPoint(ik.TargetOverrides[t2].Pos) - bone.position).magnitude);
        });
        Check("the item steps exactly once per frame", once == 120 && sameFrame == 120, $"{once} of 120, in its frame {sameFrame}");
        Check("B's target is the item of this frame", w1 <= 1e-5, w1.ToString("G3"));
        Check("B's target is the item's bone of this frame", w2 <= 1e-5, w2.ToString("G3"));
        Measure($"CO13: item {w1:G3}, its bone {w2:G3}");
    }

    static IEnumerator CO17(AppCore app)
    {
        foreach (var flip in new[] { false, true })
        {
            string tag = flip ? "the child's entry first" : "the parent's entry first";
            yield return PScene(app, 2, -1, "CO17");
            if (pB == null) yield break;
            yield return PCall(app, new { cmd = "batch", cmds = new object[]
            {
                new { cmd = "chara.anime", key = pA, group = 11, category = 5, no = 0, time = 0f, speed = 1f },
                new { cmd = "chara.anime", key = pB, group = 19, category = 0, no = 0, time = 0f, speed = 1f },
            } });
            yield return PFrames(3);
            var ra = PR(app, pA); var rb = PR(app, pB);
            // two bones of one character: B's forearm and hand, each on a bone of A
            var pb = CEntry(new ConstraintEnd(pA, "cf_J_ArmLow01_R"), new ConstraintEnd(pB, "cf_J_ArmLow01_R"), ConstraintComponents.Position, ConstraintComponents.Rotation);
            var cb = CEntry(new ConstraintEnd(pA, "cf_J_Hand_L"), new ConstraintEnd(pB, "cf_J_Hand_R"), ConstraintComponents.Position, ConstraintComponents.Rotation);
            // two nodes, one under the other, each on its own source
            string parent = null, child = null, s1 = null, s2 = null;
            yield return AddNode(app, "the parent node", k => parent = k);
            yield return PCall(app, new { cmd = "item.add", no = 399, parent });
            Check("the child node", POk, Short(pLastText));
            child = Key(pLast);
            yield return AddNode(app, "source 1", k => s1 = k);
            yield return AddNode(app, "source 2", k => s2 = k);
            app.Session.Set(s1, "transform.pos", new Vec3(2f, 3f, 1f));
            app.Session.Set(s1, "transform.rot", new Vec3(0f, 40f, 0f));
            app.Session.Set(s2, "transform.pos", new Vec3(-1f, 4f, 2f));
            var pn = CEntry(new ConstraintEnd(s1), new ConstraintEnd(parent), ConstraintComponents.Position, ConstraintComponents.Rotation);
            var cn = CEntry(new ConstraintEnd(s2), new ConstraintEnd(child), ConstraintComponents.Position);
            var pa = CEntry(new ConstraintEnd(pA, "cf_J_Head"), new ConstraintEnd(s1), ConstraintComponents.Position);
            PutList(app.Session, app.Runtime, "the list: " + tag, flip ? new[] { cb, pb, cn, pn, pa } : new[] { pb, cb, pn, cn, pa });
            Check(tag + ": all active, no lag", Part(app).Active == 5 && Part(app).Lagged.Count == 0, Part(app).Active + ", lag " + Part(app).Lagged.Count);
            yield return PFrames(2);
            var srcP = B(ra, "cf_J_ArmLow01_R"); var srcC = B(ra, "cf_J_Hand_L");
            var dstP = B(rb, "cf_J_ArmLow01_R"); var dstC = B(rb, "cf_J_Hand_R");
            var hc = app.Runtime.HolderOf(child); var h2 = app.Runtime.HolderOf(s2);
            double wp = 0, wc = 0, wr = 0, wn = 0;
            yield return Sampled(app, 600, () =>
            {
                wp = Math.Max(wp, (dstP.position - srcP.position).magnitude);
                wc = Math.Max(wc, (dstC.position - srcC.position).magnitude);
                wr = Math.Max(wr, Deg(dstC.rotation, srcC.rotation));
                wn = Math.Max(wn, (hc.position - h2.position).magnitude);
            });
            Check(tag + ": the parent bone on its source on every frame", wp <= 1e-5, wp.ToString("G3"));
            Check(tag + ": the child bone on its source on every frame", wc <= 1e-5 && wr <= 0.01, $"{wc:G3}, {wr:G3} deg");
            Check(tag + ": the child node on its source on every frame", wn <= 1e-5, wn.ToString("G3"));
            Measure($"CO17 {tag}: bones {wp:G3}, {wc:G3}, {wr:G3} deg; node {wn:G3}");
        }
    }

    static IEnumerator CO18(AppCore app)
    {
        foreach (var how in new[] { "own pause", "time scale 0" })
            foreach (var kind in new[] { "holder", "IK target", "bone" })
            {
                string tag = how + ", " + kind;
                yield return PScene(app, 1, 0, "CO18 " + tag, physics: true);
                if (pA == null) yield break;
                string source = null;
                yield return AddNode(app, "the moving source", k => source = k);
                var ra = PR(app, pA);
                if (kind == "IK target")
                {
                    yield return PCall(app, new { cmd = "ik.copy", key = pA, from = "current", enable = true });
                    Check("IK on", POk, Short(pLastText));
                }
                yield return PFrames(5);
                var src = app.Runtime.HolderOf(source);
                var e = kind == "holder" ? CEntry(new ConstraintEnd(source), new ConstraintEnd(pA), ConstraintComponents.Position)
                      : kind == "IK target" ? CEntry(new ConstraintEnd(source), new ConstraintEnd(pA, ik: "RightHand"), ConstraintComponents.Position)
                      : CEntry(new ConstraintEnd(source), new ConstraintEnd(pA, FreeBone), ConstraintComponents.Rotation);
                if (how == "own pause") yield return PCall(app, new { cmd = "chara.motion", key = pA, paused = true });
                else yield return PCall(app, new { cmd = "chara.motion", key = pA, timeScale = 0f });
                Check(tag + ": no time for the character", POk && ra.StandsStill, Short(pLastText));
                PutList(app.Session, app.Runtime, tag + ": the constraint", e);
                yield return PFrames(3);
                int settles0 = ra.Motion.Settles;
                int n = 0;
                double ms0 = ra.Motion.LastSettleMs;
                yield return Sampled(app, 60, () =>
                {
                    n++;
                    app.Session.Set(source, "transform.pos", new Vec3(0.01f * n, 0.2f, 0f), new ExecuteOptions { Record = HistoryMode.None });
                    app.Session.Set(source, "transform.rot", new Vec3(0f, n * 2f, 0f), new ExecuteOptions { Record = HistoryMode.None });
                });
                Check(tag + ": no settle while the source moves", ra.Motion.Settles == settles0, (ra.Motion.Settles - settles0).ToString());
                yield return PFrames(10);
                Check(tag + ": one settle after it rests", ra.Motion.Settles == settles0 + 1, (ra.Motion.Settles - settles0).ToString());
                yield return PFrames(60);
                Check(tag + ": at rest, no more", ra.Motion.Settles == settles0 + 1, (ra.Motion.Settles - settles0).ToString());
            }
    }

    static IEnumerator CO19(AppCore app)
    {
        yield return PScene(app, 2, 0, "CO19");
        if (pB == null) yield break;
        yield return PFrames(3);
        var ra = PR(app, pA); var rb = PR(app, pB);
        const string source = "cf_J_Hand_R", above = "cf_J_ArmLow01_R", target = "cf_J_Hand_L", other = FreeBone;
        var corr = new BoneCorrection(new Vec3(0f, 0f, 0f), new Vec3(0f, 40f, 0f), new Vec3(1f, 1f, 1f));
        var bare = B(ra, source).position;
        // the same character: read at 440, under no correction
        var e1 = CEntry(new ConstraintEnd(pA, source), new ConstraintEnd(pA, other), ConstraintComponents.Rotation);
        // another character's bone as the source: read final, the correction is in the value
        var e2 = CEntry(new ConstraintEnd(pA, source), new ConstraintEnd(pB, target), ConstraintComponents.Rotation);
        PutList(app.Session, app.Runtime, "a source on its own character and on another", e1, e2);
        Check("the first row names the read", Part(app).RowOf(e1.Id)?.SameCharacter == true && Part(app).RowOf(e2.Id)?.SameCharacter != true);
        Check("the tooltip key", Part(app).IsEffective(Part(app).RowOf(e1.Id), out var tip) && tip == "constraints.sameCharaTip", tip);
        yield return PFrames(3);
        var before = B(ra, source).rotation;
        app.Session.Set(pA, "chara.advancedPose.bones", Map((above, corr)));
        app.Runtime.ApplyNow();
        yield return PFrames(3);
        var after = B(ra, source).rotation;
        Check("the correction above the source turns it", Deg(before, after) > 10f, Deg(before, after).ToString("G3"));
        var w = B(ra, other).rotation;
        double own = Deg(Target(ra, other, w), before);
        Check("the same character: the value is the pose without the correction", Deg(B(ra, other).rotation, before) <= 0.05, Deg(B(ra, other).rotation, before).ToString("G3"));
        Check("another character: the correction is in the value", Deg(B(rb, target).rotation, after) <= 0.05, Deg(B(rb, target).rotation, after).ToString("G3"));
        yield return PCall(app, new { cmd = "scene.pause", paused = true });
        yield return PFrames(5);
        Check("at rest too", Deg(B(ra, other).rotation, before) <= 0.05 && Deg(B(rb, target).rotation, after) <= 0.05);
        yield return PCall(app, new { cmd = "scene.pause", paused = false });
    }

    static Quaternion Target(CharaRealization r, string bone, Quaternion q) => q;
}
