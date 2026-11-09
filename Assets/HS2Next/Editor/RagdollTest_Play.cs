// RagdollTest in play mode, the body's rows (Phase 6.7 package P): the app, its world a local physics scene.
//   RK1  scene.step runs from one state in a fresh world, in a local scene: the same run as the headless row's, four times, with the app's frames between the commands
//   RK6  a capture reads and moves nothing of a body: the picture stays in memory and only its size is looked at
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HS2Next.App;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;

public static partial class RagdollTest
{
    static IEnumerator Play_A_Repeat()
    {
        if (!POpen("RK1", "repeats in a fresh world, in a local scene")) yield break;
        var ready = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ready);
        var app = AppCore.Current;
        if (!Check("the app runs with its bridge and runtime", ready[0] && app != null && app.Stage == AppStage.Ready && app.Bridge != null && app.Runtime != null, app?.Stage.ToString())) yield break;
        HS2Next.Motion.Face.FaceSetup.Seed = 0x5eed;
        var hashes = new List<ulong>();
        float moved = 0f, end = 0f;
        for (int run = 0; run < 5; run++)
        {
            int pushAt = run == 4 ? 61 : 60;
            yield return PCall(app, new { cmd = "scene.new" });
            yield return PCall(app, new { cmd = "scene.pause", paused = true });
            yield return PCall(app, new { cmd = "chara.load", path = femaleRel, sex = "female" });
            string a = Key(pLast);
            yield return PCall(app, new { cmd = "chara.motion", key = a, physics = false });
            yield return PCall(app, new { cmd = "chara.load", path = maleRel, sex = "male" });
            string c = Key(pLast);
            yield return PCall(app, new { cmd = "chara.motion", key = c, physics = false });
            var rt = app.Runtime;
            rt.Session.Set(c, "transform.pos", new Vec3(5f, 0f, 2f));
            rt.Session.Set(c, "transform.rot", new Vec3(0f, 200f, 0f));
            rt.ApplyNow();
            yield return PCall(app, new { cmd = "chara.anime", key = a, group = 19, category = 0, no = 0, time = 0f, speed = 1f });
            yield return PCall(app, new { cmd = "chara.anime", key = c, group = 19, category = 0, no = 0, time = 0f, speed = 1f });
            yield return PFrames(2);
            var part = rt.GetPart<RagdollPart>();
            var w = new SceneRagdollWorld();
            w.Floor.Mode = RagdollFloorValues.ModeFixed;
            rt.Session.Commands.Execute(new RagdollWorldEditCommand(w), ExecuteOptions.NotRecorded);
            rt.ApplyNow();
            part.Build(a, ExecuteOptions.NotRecorded);
            part.Build(c, ExecuteOptions.NotRecorded);
            var ba = part.BodyOf(a);
            var bc = part.BodyOf(c);
            if (!Check("run " + run + ": two bodies", ba != null && bc != null)) yield break;
            if (run == 0) Info("the world's scene: " + part.World.Scene.name + ", the physics scene valid " + part.World.Physics.IsValid());
            var start = ba.Part("chest").Tr.position;
            if (run == 0) Measure("RK1 the state it starts from: " + Hash(Hash(14695981039346656037UL, ba), bc).ToString("x16"));
            for (int i = 0; i < 180; i++)
            {
                if (i == pushAt) ba.Push(ba.Part("chest"), new Vector3(0f, 0f, -1500f));
                rt.Step(1, Dt);
                moved = Mathf.Max(moved, (ba.Part("chest").Tr.position - start).magnitude);
            }
            end = (ba.Part("chest").Tr.position - start).magnitude;
            hashes.Add(Hash(Hash(14695981039346656037UL, ba), bc));
            yield return PCall(app, new { cmd = "scene.new" });
            yield return PFrames(2);
        }
        var four = hashes.Take(4).ToList();
        Measure("RK1 hash " + hashes[0].ToString("x16") + " (local scene)");
        Check("4 of 4 runs equal", four.Distinct().Count() == 1, string.Join(" ", four.Select(x => x.ToString("x16"))));
        Check("Present: the bodies moved at least 1, the greatest distance during the run", moved >= 1f, moved.ToString("F3") + ", at the end " + end.ToString("F3"));
        Check("Present: the hash changes when the push moves one step", hashes[4] != hashes[0], hashes[4].ToString("x16"));
    }

    static IEnumerator Play_B_Capture()
    {
        if (!POpen("RK6", "a capture moves nothing of a body")) yield break;
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { Warn("RK6 no device: a capture cannot run here, this row judges nothing"); yield break; }
        var ready = new bool[1];
        yield return PlayModeHarness.WaitUntil(() => AppCore.Current != null && AppCore.Current.Stage != AppStage.Starting, 240, ready);
        var app = AppCore.Current;
        HS2Next.Motion.Face.FaceSetup.Seed = 0x5eed;
        yield return PCall(app, new { cmd = "scene.new" });
        yield return PCall(app, new { cmd = "chara.load", path = femaleRel, sex = "female" });
        string k = Key(pLast);
        yield return PCall(app, new { cmd = "chara.anime", key = k, group = 19, category = 0, no = 0, time = 0.5f, speed = 0f });
        yield return PFrames(2);
        var rt = app.Runtime;
        var w = new SceneRagdollWorld();
        w.Floor.Mode = RagdollFloorValues.ModeFixed;
        rt.Session.Commands.Execute(new RagdollWorldEditCommand(w), ExecuteOptions.NotRecorded);
        var part = rt.GetPart<RagdollPart>();
        part.Build(k, ExecuteOptions.NotRecorded);
        var b = part.BodyOf(k);
        if (!Check("a body", b != null)) yield break;
        b.Push(b.Part("chest"), new Vector3(0f, 0f, -1500f));
        yield return PFrames(30);
        yield return PCall(app, new { cmd = "scene.pause", paused = true });
        yield return PFrames(3);
        ulong before = Hash(14695981039346656037UL, b);
        long steps = part.World.Steps;
        var cam = rt.GetPart<SceneViewCamera>().Camera;
        int size = HS2Next.App.Commands.ViewCapture.Render(cam, 320, 180)?.Length ?? 0;
        yield return PFrames(3);
        Check("a capture moves nothing of the body", Hash(14695981039346656037UL, b) == before && part.World.Steps == steps, $"steps {steps}, {part.World.Steps}");
        Check("Present: a picture was made", size > 0, size + " bytes, kept in memory, not opened");
        yield return PCall(app, new { cmd = "scene.pause", paused = false });
        yield return PCall(app, new { cmd = "scene.new" });
    }
}
