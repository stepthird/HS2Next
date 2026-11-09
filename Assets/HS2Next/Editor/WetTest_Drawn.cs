// WetTest: what the rig itself draws, seen by a camera on BR-Chan. WD10: droplets in the air and puddles on the floor change the image, by renderer, on the production pipeline; a puddle
// in view asks for its mirror, a scene with no puddle asks for none. Images stay in memory, as counts of pixels.
using System;
using System.Linq;
using HS2Next;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using UnityEngine;

public static partial class WetTest
{
    static void Render_Drawn()
    {
        Group("WD10", "the rig's drops and puddles in a camera", () =>
        {
            using (var s = new Stage())
            {
                var h = s.H;
                string key = s.Real.NodeId;
                h.Ok(new { cmd = "scene.pause", paused = true }, "paused");
                h.Ok(new { cmd = "chara.motion", key, paused = true }, "the character stands");
                var fwd = s.Ctx.Root.transform.forward;
                s.Aim(s.Centre - Vector3.up * 6f, (fwd + Vector3.up * .5f).normalized, 22f);
                int rendersBefore = PlanarMirrors.Renders;
                s.Shot();
                Check("no puddle: no mirror render", PlanarMirrors.Renders == rendersBefore && !PlanarMirrors.Subscribed, "");

                Stain(h, key, WetPreset.Blood, 0f, true, st => { st.Shape.Spread = .5f; st.Drips.Tail = 3f; st.Drips.Rate = 80f; });
                var rig = RigOf(h, key);
                int steps = 0;
                while (steps < 300 && !(rig.Drops.Drops >= 3 && rig.Puddles.Count > 0))
                {
                    h.Ok(new { cmd = "scene.step", n = 5, dt = Dt }, "steps");
                    steps += 5;
                }
                h.Frames(1);
                Check("droplets in the air and a puddle on the floor", rig.Drops.Drops > 0 && rig.Puddles.Count > 0, $"{rig.Drops.Drops} droplets, {rig.Puddles.Count} puddles after {steps} steps");
                Check("the floor without a map is the feet", Part(h).StatusOf(key).FloorSource == WetFloorSources.Feet, Part(h).StatusOf(key).FloorSource);
                Check("a claim on a mirror", rig.Puddles.Mirrors > 0 && PlanarMirrors.Subscribed, rig.Puddles.Mirrors.ToString());

                var all = s.Shot();
                Check("a puddle in view renders its mirror", PlanarMirrors.Renders > rendersBefore, (PlanarMirrors.Renders - rendersBefore).ToString());
                Check("no mirror render failed", PlanarMirrors.Failures == 0, PlanarMirrors.Failures.ToString());

                var drops = rig.Drops.Renderer;
                Check("the drops have a renderer", drops && drops.enabled, "");
                drops.enabled = false;
                var noDrops = s.Shot();
                drops.enabled = true;
                int dropPixels = Diff(all, noDrops);
                Check("the droplets change the image", dropPixels > 0, dropPixels + " px");
                Info($"droplets {rig.Drops.Drops}, splashes {rig.Drops.Splashes}, indices {rig.Drops.DrawnIndices}: {dropPixels} px");

                var discs = rig.Puddles.List.Select(p => p.Renderer).ToList();
                Check("every puddle has a renderer on the chara layer", discs.All(r => r && r.enabled && r.gameObject.layer == RenderingLayers.Chara), "");
                foreach (var r in discs) r.enabled = false;
                var noPuddles = s.Shot();
                foreach (var r in discs) r.enabled = true;
                int puddlePixels = Diff(all, noPuddles);
                Check("the puddles change the image", puddlePixels > 0, puddlePixels + " px");
                Info($"puddles {rig.Puddles.Count}: {puddlePixels} px");

                var again = s.Shot();
                Check("two renders of the same moment are equal", Diff(all, again, 0) == 0, Diff(all, again, 0) + " px");
                Check("a puddle comes back after the mirror", discs.All(r => !r.forceRenderingOff), "");

                h.Ok(new { cmd = "obj.visible", key, visible = false }, "the node hidden");
                h.Frames(1);
                s.Shot();
                Check("hidden: no drop draws", drops == null || !drops.enabled, "");
                Check("hidden: no puddle renderer draws", rig.Puddles.List.All(p => !p.Renderer.enabled), "");
                h.Ok(new { cmd = "obj.visible", key, visible = true }, "the node shown");
                h.Frames(1);

                h.Ok(new { cmd = "scene.new" }, "scene.new");
                h.Rt.ApplyNow();
                Check("the end: no plane, not subscribed", PlanarMirrors.Planes == 0 && !PlanarMirrors.Subscribed, $"{PlanarMirrors.Planes} planes");
            }
        });
    }
}
