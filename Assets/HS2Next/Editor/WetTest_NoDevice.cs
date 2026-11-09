// WetTest without a graphics device (the headless row): the part keeps the document and the clock, nothing is drawn, and the capture writes what the runtime holds by its field paths.
//   WB12  a block on a character: no render texture, no film, no mesh; a bake answers unsupported; the clock runs and the pause stops it
//   WB13  the capture: t, running and the puddle list reach the document silently, as one batch, and a second capture writes nothing
using System;
using System.Linq;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;

public static partial class WetTest
{
    static string DocStain(Host h, string key, float t, bool running)
    {
        var block = St(h, key).Wet.Clone();
        var s = new WetStain { Id = block.NextId(), Name = "doc", Seed = 5, T = t, Running = running };
        WetPreset.ApplyTo(s, WetPreset.Water);
        block.Stains.Add(s);
        block.Current = s.Id;
        var r = h.Session.Commands.Execute(new WetEditCommand(key, block, WetEditCommand.LabelAdd));
        Check("the edit went in", r.Status == CommandStatus.Ok, r.ToString());
        h.Rt.ApplyNow();
        return s.Id;
    }

    static void Headless_Rig()
    {
        Group("WB12", "no device: the document and the clock", () =>
        {
            if (WetBake.Available) { Info("this run has a device: the group is for the headless row"); return; }
            using (var h = NewHost())
            {
                string key = LoadChara(h);
                Check("no rig without a block", Part(h).Rigs == 0, "");
                string id = DocStain(h, key, 0f, true);
                var rig = RigOf(h, key);
                Check("a rig for the block", rig != null, "");
                Check("no slot without a device", rig.Slots.Count == 0, rig.Slots.Count.ToString());
                var block = St(h, key).Wet.Clone();
                Check("a bake answers unsupported", Part(h).Bake(key, block, block.Stains[0]) == CommandCodes.Unsupported, "");
                float t0 = Part(h).TimeOf(key, id);
                h.Frames(10);
                float t1 = Part(h).TimeOf(key, id);
                Check("the clock runs by the scene's delta", Math.Abs(t1 - t0 - 10 * Dt) < 1e-4, (t1 - t0).ToString());
                h.Ok(new { cmd = "scene.pause", paused = true }, "paused");
                h.Frames(5);
                Check("the pause stops it", Part(h).TimeOf(key, id) == t1, "");
                h.Ok(new { cmd = "scene.step", n = 3, dt = 0.25 }, "three steps");
                Check("a step moves it by its dt", Math.Abs(Part(h).TimeOf(key, id) - t1 - .75f) < 1e-4, (Part(h).TimeOf(key, id) - t1).ToString());
                Check("no object is left under the wet root", !Part(h).Root || Part(h).Root.childCount == 0, Part(h).Root ? Part(h).Root.childCount.ToString() : "no root");
            }
        });

        Group("WB13", "the capture's field paths", () =>
        {
            using (var h = NewHost())
            {
                string key = LoadChara(h);
                string id = DocStain(h, key, 0f, true);
                var rig = RigOf(h, key);
                h.Ok(new { cmd = "scene.pause", paused = true }, "paused");
                h.Ok(new { cmd = "scene.step", n = 30, dt = Dt }, "half a second");
                rig.Puddles.Land(id, new Vector3(1f, 0f, 2f), .07f, 1f, 1.5f, 5);
                rig.Puddles.Land(id, new Vector3(9f, 0f, 2f), .07f, 1f, 1.5f, 5);
                float t = Part(h).TimeOf(key, id);
                var before = St(h, key).Wet;
                Check("the document does not follow the clock by itself", before.Stains[0].T == 0f && before.Puddles.Count == 0, "");
                int depth = h.Session.Commands.UndoList.Count;
                Part(h).CaptureNow(key);
                var w = St(h, key).Wet;
                Check("the same block, written in place", ReferenceEquals(before, w), "");
                Check("t reaches the document", w.Stains[0].T == t, w.Stains[0].T + " / " + t);
                Check("two puddles reach it", w.Puddles.Count == 2 && w.Puddles[0].Area > 0f, w.Puddles.Count.ToString());
                Check("no history entry", h.Session.Commands.UndoList.Count == depth, "");
                Check("the capture wrote them, beside the character's own moved values", h.Rt.Capture.LastWrites >= 2, h.Rt.Capture.LastWrites.ToString());
                Part(h).CaptureNow(key);
                Check("a second capture writes nothing", h.Rt.Capture.LastWrites == 0, h.Rt.Capture.LastWrites.ToString());

                h.Ok(new { cmd = "scene.pause", paused = false }, "running");
                var run = rig.Runs[0];
                run.Running = false;
                Part(h).CaptureNow(key);
                Check("running reaches the document", !St(h, key).Wet.Stains[0].Running, "");
            }
        });
    }
}
