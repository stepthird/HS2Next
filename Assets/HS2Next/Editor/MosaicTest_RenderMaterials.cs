// MosaicTest.render, material edits (Phase 6.4 package R). The body skin is editable, so its shader and queue can be anything a candidate
// offers; the members must draw over it all the same. Images stay in memory.
//   MO6  BR-Chan, clothes hidden, a code-made composite with the marker checker under the patches, as MR1. The skin on a transparent candidate at queues 3000 and 5000 and on
//        a candidate without _MainTex, each from a document with the item (a fresh build): the members draw, 0 marker pixels inside their footprint (eroded 4 px) at
//        0.3, 1 and 3 m from three angles. With the patches off the same view is counted as well (information: what the skin shows there).
//   MO6all  opt-in (-only MO6all, not in the row's default run): the skin on every candidate, at its default queue and at 5000, at 0.3 m from three angles. The mask is the members'
//        footprint with the skin as the card has it, taken once, so a skin drawn over a patch cannot shrink what is judged: 0 marker pixels inside it, and no pixel of it where
//        the members no longer show. With the patches off the view is counted too: a case that shows no marker there can only prove that the members draw. -budgetMin N (22): when
//        the projected time passes it the angles drop, never the candidates, and the report says so.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next;
using HS2Next.Chara;
using HS2Next.Chara.Mosaic;
using HS2Next.Save;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using static MakerTestKit;

public static partial class MosaicTest
{
    static void Mo6(MakerCoreTest.Harness h)
    {
        MaterialShaders.Reset();   // the list of this device
        var withMain = MaterialShaders.All.Where(c => c.Queue >= 3000 && c.Shader.FindPropertyIndex("_MainTex") >= 0).ToList();
        var noMain = MaterialShaders.All.Where(c => c.Shader.FindPropertyIndex("_MainTex") < 0).ToList();
        Info($"MO6 {MaterialShaders.All.Count} candidates: {withMain.Count} transparent with _MainTex ({string.Join(", ", withMain.Take(6).Select(c => c.Label))}), {noMain.Count} without _MainTex ({string.Join(", ", noMain.Take(6).Select(c => c.Label))})");
        if (!Check("MO6 a transparent candidate exists", withMain.Count > 0, "")) return;
        var skinClass = withMain.FirstOrDefault(c => c.Name.Contains("Clothes Alpha")) ?? withMain[0];
        var cases = new List<(string shader, int? queue)> { (skinClass.Name, 3000), (skinClass.Name, 5000) };
        if (noMain.Count > 0) cases.Add((noMain[0].Name, null));
        else Warn("MO6", "no candidate without _MainTex");
        foreach (var (shader, queue) in cases) Mo6Case(h, shader, queue);
    }

    /// <summary>True when -only names <paramref name="id"/> itself: an opt-in group runs for nothing less.</summary>
    static bool OnlyNamed(string id) => Arg("-only")?.Split(',', '+').Select(x => x.Trim()).Contains(id) == true;

    static void Mo6All(MakerCoreTest.Harness h)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double budget = double.Parse(Arg("-budgetMin", "22"), System.Globalization.CultureInfo.InvariantCulture);
        MaterialShaders.Reset();   // the list of this device
        var all = MaterialShaders.All;
        const int W = 1280, H = 720;
        const float Distance = 3f;
        var cam = NewCam(true, AntialiasingMode.SubpixelMorphologicalAntiAliasing);
        var views = new List<(string name, Vector3 dir)>();
        var reference = new List<bool[]>();
        Vector3 centre;
        Texture composite;
        string skin;
        using (var f = new Disposer(h.Build(FemaleCard)))
        {
            if (!Check("MO6all the card builds", f.B.Ok, f.B.Error ?? "")) return;
            var ctx = f.B.Context;
            skin = HS2Next.Chara.Ext.MaterialEditorApplier.Fmt(ctx.BodyDraw.name);
            HideClothes(ctx);
            composite = Composite(ctx, 2048, 6, MarkerTexel, Skin);
            UseComposite(ctx, composite);
            MosaicWide.Enabled = false;
            Vector3 n;
            (centre, n) = PatchPose(ctx);
            var right = Vector3.Cross(Vector3.up, n).normalized;
            views.Add(("front", n));
            views.Add(("30 deg up", Quaternion.AngleAxis(-30f, right) * n));
            views.Add(("45 deg side", Quaternion.AngleAxis(45f, Vector3.up) * n));
            foreach (var (_, dir) in views)
            {
                Aim(cam, centre, dir, Distance);
                reference.Add(Erode(Footprint(cam, W, H, ctx), W, H, 4));
            }
            Info($"MO6all {all.Count} candidates; the mask with the card's skin: " + string.Join(", ", reference.Select(x => x.Count(y => y) + " px")));
        }
        int total = all.Count * 2, done = 0, angles = views.Count, withMarkers = 0, passed = 0, viewsRun = 0, cut = 0;
        var failed = new List<string>();
        foreach (var cand in all)
            foreach (var queue in new int?[] { null, 5000 })
            {
                string what = cand.Label + (queue.HasValue ? " queue " + queue : " default queue " + cand.Queue);
                string why = Mo6AllCase(h, cand.Name, queue, skin, composite, cam, centre, views, reference, angles, Distance, W, H, out int shown, ref viewsRun);
                done++;
                if (why == null) passed++; else failed.Add(what + ": " + why);
                if (shown > 0) withMarkers++;
                Info($"MO6all {what}: {(why ?? "ok")}; the patches off: {shown}");
                if (done % 20 == 0) h.ReleaseBundles();
                double projected = clock.Elapsed.TotalMinutes / done * total;
                if (projected > budget && angles > 1 && done >= 8)
                {
                    angles--; cut = done;
                    Info($"MO6all projected {projected:F1} min after {done} cases: {angles} angles from here");
                }
            }
        Info($"MO6all {done} cases, {viewsRun} views, {clock.Elapsed.TotalMinutes:F1} min; the skin shows markers with the patches off in {withMarkers} cases, in the others only the members' drawing is proved" + (cut > 0 ? $"; angles cut, last at case {cut}" : ""));
        foreach (var l in failed) Info("MO6all FAILED " + l);
        Check("MO6all every candidate at both queues: the members draw, 0 marker pixels in the footprint", failed.Count == 0 && done == total, $"{passed} of {total} cases");
    }

    // null when the case holds, else what it shows
    static string Mo6AllCase(MakerCoreTest.Harness h, string shader, int? queue, string skin, Texture composite, Camera cam, Vector3 centre, List<(string name, Vector3 dir)> views,
                             List<bool[]> reference, int angles, float distance, int w, int ht, out int shown, ref int viewsRun)
    {
        shown = 0;
        var c = MakerEditTest.Import(h, FemaleCard, out string error);
        if (c == null) return "not imported: " + error;
        c.Subtree.Chara.MaterialOverrides.Add(new MeShader { Material = skin, Shader = shader, RenderQueue = queue });
        using (var f = new Disposer(h.Build(c)))
        {
            if (!f.B.Ok) return "does not build: " + (f.B.Error ?? "");
            var ctx = f.B.Context;
            if (ctx.BodyDraw.shader.name != shader) return "the skin has " + ctx.BodyDraw.shader.name;
            if (queue.HasValue && ctx.BodyDraw.renderQueue != queue.Value) return "the skin's queue is " + ctx.BodyDraw.renderQueue;
            var st = MosaicSet.StateOf(ctx);
            if (!st.Members.All(m => m.Renderer.sharedMaterials.All(MosaicUnit.IsUnitMaterial))) return "a member lost its mosaic material";
            HideClothes(ctx);
            UseComposite(ctx, composite);
            MosaicWide.Enabled = false;
            var bad = new List<string>();
            for (int v = 0; v < angles && v < views.Count; v++)
            {
                Aim(cam, centre, views[v].dir, distance);
                var mask = reference[v];
                var drawn = Footprint(cam, w, ht, ctx);
                int lost = 0, inMask = 0;
                for (int i = 0; i < mask.Length; i++) if (mask[i]) { inMask++; if (!drawn[i]) lost++; }
                int markers = Markers(Render(cam, w, ht), mask);
                foreach (var m in st.Members) m.Renderer.enabled = false;
                shown = Math.Max(shown, Markers(Render(cam, w, ht), mask));
                foreach (var m in st.Members) m.Renderer.enabled = true;
                viewsRun++;
                if (markers > 0 || lost > 0) bad.Add($"{views[v].name}: {markers} marker px, {lost} of {inMask} mask px not the members'");
            }
            return bad.Count == 0 ? null : string.Join("; ", bad);
        }
    }

    static void Mo6Case(MakerCoreTest.Harness h, string shader, int? queue)
    {
        string what = $"MO6 {shader.Substring(MaterialShaders.Prefix.Length)} queue {(queue.HasValue ? queue.ToString() : "default")}";
        CharaContainer c;
        using (var first = new Disposer(h.Build(FemaleCard)))
        {
            if (!Check(what + ": the card builds", first.B.Ok, first.B.Error ?? "")) return;
            string skin = HS2Next.Chara.Ext.MaterialEditorApplier.Fmt(first.B.Context.BodyDraw.name);
            c = first.B.Container;
            c.Subtree.Chara.MaterialOverrides.Add(new MeShader { Material = skin, Shader = shader, RenderQueue = queue });
        }
        using (var f = new Disposer(h.Build(c)))
        {
            if (!Check(what + ": the document builds", f.B.Ok, f.B.Error ?? "")) return;
            var ctx = f.B.Context;
            Check(what + ": the skin took the shader", ctx.BodyDraw.shader.name == shader, ctx.BodyDraw.shader.name);
            if (queue.HasValue) Check(what + ": the skin took the queue", ctx.BodyDraw.renderQueue == queue.Value, ctx.BodyDraw.renderQueue.ToString());
            else Info($"{what}: the skin's queue {ctx.BodyDraw.renderQueue}");
            var st = MosaicSet.StateOf(ctx);
            Check(what + ": the members hold their mosaic materials", st.Members.All(m => m.Renderer.sharedMaterials.All(MosaicUnit.IsUnitMaterial)), "");
            Info(what + ": members " + string.Join(", ", st.Members.Select(m => $"{m.Renderer.name} {m.Mode}{(m.SourceKind != null ? "/" + m.SourceKind : "")}")));
            HideClothes(ctx);
            UseComposite(ctx, Composite(ctx, 2048, 6, MarkerTexel, Skin));
            MosaicWide.Enabled = false;
            var (centre, n) = PatchPose(ctx);
            var right = Vector3.Cross(Vector3.up, n).normalized;
            var dirs = new[] { ("front", n), ("30 deg up", Quaternion.AngleAxis(-30f, right) * n), ("45 deg side", Quaternion.AngleAxis(45f, Vector3.up) * n) };
            float[] dists = { 3f, 10f, 30f };
            const int W = 1280, H = 720;
            var cam = NewCam(true, AntialiasingMode.SubpixelMorphologicalAntiAliasing);
            int worst = 0, fpTotal = 0, drawnViews = 0, off = 0;
            foreach (var (dn, dir) in dirs)
                foreach (var d in dists)
                {
                    Aim(cam, centre, dir, d);
                    var fp = Erode(Footprint(cam, W, H, ctx), W, H, 4);
                    var px = Render(cam, W, H);
                    int fpn = fp.Count(x => x), mk = Markers(px, fp);
                    worst = Math.Max(worst, mk);
                    fpTotal += fpn;
                    if (fpn > 0) drawnViews++;
                    foreach (var m in st.Members) m.Renderer.enabled = false;
                    int shown = Markers(Render(cam, W, H), fp);
                    foreach (var m in st.Members) m.Renderer.enabled = true;
                    off = Math.Max(off, shown);
                    Info($"{what} {dn} {d / 10f:0.#} m: footprint {fpn} px, markers {mk}; the patches off: {shown}");
                }
            Check(what + ": the members draw in every view", drawnViews == dirs.Length * dists.Length, $"{drawnViews} of {dirs.Length * dists.Length} views, footprint total {fpTotal} px");
            Check(what + ": 0 marker pixels of the skin inside the footprint", worst == 0, $"worst {worst}; with the patches off the skin shows up to {off} there");
        }
    }
}
