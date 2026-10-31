// BackgroundTest.render (Phase 3.6 package C, R16–R19): play mode, the app's URP
// asset with the run-time background / frame features, an AppCore test host (CaptureRig), test images written by the test
// (no image of the user's UserData is drawn: HS2's files are only resolved or loaded for their size).
//   B1 the stretched mapping: an 8 × 4 grid image, the centre of every cell at the expected colour (post-processing off,
//      4 / 255) under FOV 23 and 60 and the aspects 16:9, 4:3, 5:7; the view camera itself (RenderView) shows it too
//   B2 an opaque object in front hides it; a transparent-free stage behind the plane (beyond 0.98 × far) does not show
//   B3 the sky does not show through: with a background, sky on = sky off (2 / 255); without, they differ
//   B4 DOF treats it as far: focus on a near quad, the background's cell edges soften, the near quad stays sharp
//   B5 the frame: its opaque band, its half-alpha area blended in linear light, the rest the scene — in a capture with frame
//      and in the view; not in a capture without frame; not in scene thumbnails (the background is)
//   B6 missing / broken files (R19): a user reference whose file was removed keeps the reference, draws nothing and reports
//      scene.backgroundMissing; a PNG that does not decode reports scene.imageDecodeFailed
//   B7 FileRefs.FromPicked: DefaultData → hs2 (an HS2 DefaultData/bg file: resolved and loaded for its size only), a
//      UserData file → res (embed) / user (embed false), HS2's UserData/frame → res / hs2 (existence only), relative and
//      hs2: forms, a missing file and a non-image refused
//   B8 the 3.5 image pickers now resolve: InspectorValues.To(FileRef, <absolute path>) → a reference ItemTextures.File
//      resolves (FileRef.FromUser(<absolute path>), the 3.5 conversion, does not)
//   B9 save / new / load / import / undo: the references round-trip through the scene file, new clears them, import ignores
//      them, undo / redo of scene.images restore the stored value (one entry each)
//   B10 cost: a 2560 × 1440 capture with and without a background (the pass ≤ 0.1 ms at 1440p; CPU-side estimate)
// Run: -executeMethod BackgroundTest.RunRender (-NoQuit); report Spike/out/capture/background_render_report.txt.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.App.Commands;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Screenshots;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class BackgroundTest
{
    static string Report => Path.Combine(CaptureRig.OutDir, "background_render_report.txt");
    static CaptureRig g;
    const int GX = 8, GY = 4;

    public static void RunRender()
    {
        Directory.CreateDirectory(CaptureRig.OutDir);
        File.WriteAllText(Report, "SUMMARY: 0 PASS, 1 FAIL\nBackgroundTest.render: started, no result yet\n", new UTF8Encoding(false));
        PlayModeHarness.Start(nameof(BackgroundTest) + "." + nameof(Main), Report, 30);
    }

    static IEnumerator Main()
    {
        var sw = Stopwatch.StartNew();
        g = new CaptureRig("BackgroundTest.render");
        IEnumerator body = null;
        try { g.Start(); body = Body(); }
        catch (Exception e) { g.Check("set-up", false, e.ToString()); }
        if (body != null)
        {
            while (true)
            {
                object cur;
                try
                {
                    if (!body.MoveNext()) break;
                    cur = body.Current;
                }
                catch (Exception e) { g.Check("the test body ran without an exception", false, e.ToString()); break; }
                yield return cur;
            }
        }
        SceneImages.SyncLoads = false;
        g.Stop();
        g.Write(Report, sw.Elapsed.TotalSeconds);
        PlayModeHarness.ExitCode = g.Fail > 0 ? 1 : 0;
    }

    /// <summary>The colour of grid cell (cx, cy from the top) of the test image.</summary>
    static Color32 Cell(int cx, int cy) => new Color32((byte)(20 + cx * 30), (byte)(30 + cy * 60), (byte)(cx % 2 == 0 ? 200 : 60), 255);

    static SceneImages Images => g.Rt.GetPart<SceneImages>();

    static IEnumerator SetImages(object background, object frame, bool? embed = null)
    {
        var req = new Dictionary<string, object> { ["cmd"] = "scene.images" };
        if (!(background is Missing)) req["background"] = background;
        if (!(frame is Missing)) req["frame"] = frame;
        if (embed != null) req["embed"] = embed.Value;
        yield return g.Call(req);
        yield return WaitLoads();
    }

    /// <summary>Real frames until the scene images finished loading (15 s at most).</summary>
    static IEnumerator WaitLoads()
    {
        var sw = Stopwatch.StartNew();
        while (Images != null && Images.Pending && sw.Elapsed.TotalSeconds < 15) { g.App.Frame(); yield return null; }
        g.Settle();
    }

    sealed class Missing { }
    static readonly Missing Keep = new Missing();

    static IEnumerator Body()
    {
        string dir = Path.Combine(CaptureRig.OutDir, "bg_images");
        Directory.CreateDirectory(dir);
        const int IW = 256, IH = 128;
        string grid = CaptureRig.WritePng(Path.Combine(g.App.Paths.BgDir, "bgtest_grid.png"), IW, IH, (x, y) => Cell(x * GX / IW, y * GY / IH));
        string flat = CaptureRig.WritePng(Path.Combine(g.App.Paths.BgDir, "bgtest_flat.png"), 32, 32, (x, y) => new Color32(90, 140, 60, 255));
        // frame: top quarter opaque magenta, the bottom-left quarter white at alpha 128, the rest clear
        string frame = CaptureRig.WritePng(Path.Combine(g.App.Paths.FrameDir, "bgtest_frame.png"), 64, 64, (x, y) =>
            y < 16 ? new Color32(255, 0, 255, 255) : (y >= 32 && x < 32 ? new Color32(255, 255, 255, 128) : new Color32(0, 0, 0, 0)));
        g.EffectsOff(false);
        g.SetView(Vector3.zero, Vector3.zero, 20f, 23f);

        // ---------------------------------------------------------------- B1 mapping
        g.Section("B1", "stretched mapping (FOV 23 / 60, aspects 16:9, 4:3, 5:7)");
        yield return SetImages(grid, null);
        g.Check("scene.images background → embedded, loaded 256 × 128", g.Ok && Images.Background.State == SceneImages.StateLoaded && Images.Background.Width == IW && Images.Background.Height == IH, g.Short);
        foreach (float fov in new[] { 23f, 60f })
        {
            g.SetView(Vector3.zero, Vector3.zero, 20f, fov);
            foreach (var (w, h, name) in new[] { (640, 360, "16:9"), (480, 360, "4:3"), (250, 350, "5:7") })
            {
                var s = g.Capture(w, h, save: $"b1_grid_fov{fov:F0}_{name.Replace(':', 'x')}");
                int bad = 0, worst = 0; string first = null;
                for (int cy = 0; cy < GY; cy++)
                    for (int cx = 0; cx < GX; cx++)
                    {
                        var p = s.AtUv((cx + 0.5f) / GX, (cy + 0.5f) / GY);
                        int d = CaptureRig.ColorDiff(p, Cell(cx, cy));
                        worst = Math.Max(worst, d);
                        if (d > 4) { bad++; first ??= $"cell ({cx}, {cy}) {CaptureRig.C(p)} vs {CaptureRig.C(Cell(cx, cy))}"; }
                    }
                g.Check($"FOV {fov:F0}, {name} ({w} × {h}): all 32 cell centres at their colour (stretched to the view)", bad == 0, $"worst {worst} / 255{(first != null ? "; " + first : "")}");
            }
        }
        g.SetView(Vector3.zero, Vector3.zero, 20f, 40f);
        {
            var v = RenderView(400, 300, "b1_view");
            int worst = 0;
            for (int cy = 0; cy < GY; cy++) for (int cx = 0; cx < GX; cx++) worst = Math.Max(worst, CaptureRig.ColorDiff(v.AtUv((cx + 0.5f) / GX, (cy + 0.5f) / GY), Cell(cx, cy)));
            g.Check("the view camera draws the background the same way (cell centres, 4 / 255)", worst <= 4, $"worst {worst}");
        }

        // ---------------------------------------------------------------- B2 occlusion
        g.Section("B2", "geometry in front hides it");
        {
            var q = g.Quad("front", new Vector3(0, 0, 50), new Vector2(10, 10), new Color(1f, 1f, 0f));
            var s = g.Capture(640, 360, save: "b2_front");
            g.Check("an opaque quad in front shows over the background (centre yellow), the corners keep the background",
                CaptureRig.ColorDiff(s.AtUv(0.5f, 0.5f), new Color32(255, 255, 0, 255)) <= 3 && CaptureRig.ColorDiff(s.AtUv(0.5f / GX, 0.5f / GY), Cell(0, 0)) <= 4,
                CaptureRig.C(s.AtUv(0.5f, 0.5f)) + " / " + CaptureRig.C(s.AtUv(0.5f / GX, 0.5f / GY)));
            float far = g.View.Camera.farClipPlane;
            q.transform.localPosition = new Vector3(0, 0, far * 0.99f);
            q.transform.localScale = new Vector3(far, far, 1f);
            var s2 = g.Capture(640, 360);
            g.Check($"a quad beyond the plane (0.99 × far = {far * 0.99f:F0}) is hidden by the background", CaptureRig.ColorDiff(s2.AtUv(0.5f + 0.5f / GX, 0.5f + 0.5f / GY), Cell(GX / 2, GY / 2)) <= 4,
                CaptureRig.C(s2.AtUv(0.5f + 0.5f / GX, 0.5f + 0.5f / GY)));
            Object.Destroy(q);
            yield return null;
        }

        // ---------------------------------------------------------------- B3 sky
        g.Section("B3", "the sky does not show through");
        {
            CaptureRig.Shot SkyShot(bool sky, string save = null)
            {
                using (var tx = g.Session.Commands.BeginOrJoin("test", default)) { tx.Set(null, "effects.sky.enabled", sky); tx.Commit(); }
                g.Settle();
                return g.Capture(320, 180, save: save);
            }
            int enq0 = HS2BackgroundFeature.EnqueuedCount, rec0 = HS2BackgroundFeature.RecordedCount, bb0 = HS2BackgroundFeature.SkippedBackBuffer;
            var withOn = SkyShot(true, "b3_bg_sky_on");
            int enq1 = HS2BackgroundFeature.EnqueuedCount;
            g.Info($"debug: recorded {HS2BackgroundFeature.RecordedCount - rec0}, skipped (back buffer) {HS2BackgroundFeature.SkippedBackBuffer - bb0}; skybox {RenderSettings.skybox?.name} ({RenderSettings.skybox?.shader?.name}), queue {RenderSettings.skybox?.renderQueue}");
            var withOff = SkyShot(false, "b3_bg_sky_off");
            var q = g.Quad("probe", new Vector3(0, 0, 50), new Vector2(10, 10), new Color(1f, 1f, 0f));
            var quadSky = SkyShot(true, "b3_quad_sky_on");
            Object.DestroyImmediate(q);
            g.Check("with the sky on, an opaque quad in front still shows over the background", CaptureRig.ColorDiff(quadSky.AtUv(0.5f, 0.5f), new Color32(255, 255, 0, 255)) <= 3
                && CaptureRig.ColorDiff(quadSky.AtUv(0.5f / GX, 0.5f / GY), Cell(0, 0)) <= 4, CaptureRig.C(quadSky.AtUv(0.5f, 0.5f)) + " / " + CaptureRig.C(quadSky.AtUv(0.5f / GX, 0.5f / GY)));
            g.Info($"passes enqueued for the sky-on capture: {enq1 - enq0}");
            yield return SetImages(null, Keep);
            var noOn = SkyShot(true, "b3_nobg_sky_on"); var noOff = SkyShot(false);
            var dw = CaptureRig.Diff(withOn, withOff); var dn = CaptureRig.Diff(noOn, noOff);
            g.Check("without a background the sky changes the image (sanity: the sky draws)", dn.mean > 1, $"mean {dn.mean:F2}");
            g.Check("with a background, sky on = sky off (the image covers the sky)", dw.max <= 2, $"mean {dw.mean:F3} max {dw.max}");
            SkyShot(false);
            yield return SetImages(grid, Keep);
        }

        // ---------------------------------------------------------------- B4 DOF
        g.Section("B4", "DOF treats the background as far");
        {
            g.EffectsOff(true);
            var near = g.Quad("near", new Vector3(0, 0, 3f), new Vector2(0.6f, 0.6f), new Color(1f, 0.2f, 0.2f));
            g.Settle();
            var off = g.Capture(640, 360, save: "b4_dof_off");
            using (var tx = g.Session.Commands.BeginOrJoin("test", default))
            {
                tx.Set(null, "effects.depthOfField.enabled", true);
                tx.Set(null, "effects.depthOfField.focalSize", 0.05f);
                tx.Set(null, "effects.depthOfField.aperture", 0.8f);
                tx.Commit();
            }
            g.Settle();
            yield return null;
            var on = g.Capture(640, 360, save: "b4_dof_on");
            // the edge between cells (3, 0) and (4, 0): contrast across 2 px
            double Edge(CaptureRig.Shot s)
            {
                int y = s.H / 8, x = s.W / 2;
                return CaptureRig.ColorDiff(s.At(x - 2, y), s.At(x + 1, y));
            }
            g.Check("the background's cell edge softens with the near focus (far = out of focus)", Edge(on) < Edge(off) * 0.8, $"edge {Edge(off)} → {Edge(on)}");
            g.Check("the near quad in focus keeps its centre colour", CaptureRig.ColorDiff(on.AtUv(0.5f, 0.5f), off.AtUv(0.5f, 0.5f)) <= 6, CaptureRig.C(off.AtUv(0.5f, 0.5f)) + " → " + CaptureRig.C(on.AtUv(0.5f, 0.5f)));
            Object.Destroy(near);
            g.EffectsOff(false);
            yield return null;
        }

        // ---------------------------------------------------------------- B5 frame
        g.Section("B5", "the frame image");
        {
            yield return SetImages(flat, frame);
            g.Check("frame loaded 64 × 64", Images.Frame.State == SceneImages.StateLoaded && Images.Frame.Width == 64, Images.Frame.State + " " + Images.Frame.Error);
            var bgC = new Color32(90, 140, 60, 255);
            float a = 128f / 255f;
            Color32 blend = new Color32(CaptureService.LinearToSrgb(CaptureService.SrgbToLinear(90) * (1 - a) + a), CaptureService.LinearToSrgb(CaptureService.SrgbToLinear(140) * (1 - a) + a),
                CaptureService.LinearToSrgb(CaptureService.SrgbToLinear(60) * (1 - a) + a), 255);
            void Probe(CaptureRig.Shot s, string what, bool frameExpected)
            {
                var top = s.AtUv(0.5f, 0.1f); var half = s.AtUv(0.25f, 0.75f); var clear = s.AtUv(0.75f, 0.75f);
                if (frameExpected)
                    g.Check(what + ": the band opaque magenta, the half-alpha area blended in linear light, the rest the background",
                        CaptureRig.ColorDiff(top, new Color32(255, 0, 255, 255)) <= 3 && CaptureRig.ColorDiff(half, blend) <= 4 && CaptureRig.ColorDiff(clear, bgC) <= 3,
                        $"band {CaptureRig.C(top)}, half {CaptureRig.C(half)} (expected {CaptureRig.C(blend)}), clear {CaptureRig.C(clear)}");
                else
                    g.Check(what + ": no frame (the background everywhere)", CaptureRig.ColorDiff(top, bgC) <= 3 && CaptureRig.ColorDiff(half, bgC) <= 3, $"band {CaptureRig.C(top)}, half {CaptureRig.C(half)}");
            }
            Probe(g.Capture(640, 360, frame: true, save: "b5_capture_frame"), "capture with frame", true);
            Probe(g.Capture(640, 360, frame: false), "capture without frame", false);
            Probe(RenderView(640, 360, "b5_view"), "the view", true);
            var thumbPng = g.Rt.Ops.ThumbnailRenderer?.Invoke();
            var th = thumbPng != null ? CaptureRig.Decode(thumbPng) : null;
            if (th != null) File.WriteAllBytes(Path.Combine(CaptureRig.OutDir, "b5_thumbnail.png"), thumbPng);
            g.Check("scene thumbnail: the background in, the frame out", th != null && CaptureRig.ColorDiff(th.AtUv(0.5f, 0.05f), bgC) <= 3 && CaptureRig.ColorDiff(th.AtUv(0.5f, 0.5f), bgC) <= 3,
                th == null ? "no thumbnail renderer" : $"{th.W} × {th.H}: top {CaptureRig.C(th.AtUv(0.5f, 0.05f))}, centre {CaptureRig.C(th.AtUv(0.5f, 0.5f))}");
        }

        // ---------------------------------------------------------------- B6 missing / broken
        g.Section("B6", "missing and broken files (R19)");
        {
            string gone = CaptureRig.WritePng(Path.Combine(g.App.Paths.BgDir, "bgtest_gone.png"), 16, 16, (x, y) => new Color32(250, 20, 20, 255));
            yield return SetImages(gone, null, embed: false);
            string stored = (string)g.Last?["background"]?["user"];
            g.Check("embed false: a UserData file → {user: \"bg/bgtest_gone.png\"}", stored == "bg/bgtest_gone.png", g.Short);
            File.Delete(gone);
            int findings0 = g.Session.Findings.Count(f => f.Code == "scene.backgroundMissing");
            g.Rt.RealizeAll();
            g.Settle();
            yield return null;
            g.Settle();
            var s = g.Capture(320, 180);
            int findings1 = g.Session.Findings.Count(f => f.Code == "scene.backgroundMissing");
            g.Check("the file removed: state missing, the reference kept, scene.backgroundMissing reported, nothing drawn (the clear colour)",
                Images.Background.State == SceneImages.StateMissing && g.Session.Model.Settings.Background?.Value == "bg/bgtest_gone.png" && findings1 > findings0
                && CaptureRig.ColorDiff(s.AtUv(0.5f, 0.5f), new Color32(0, 0, 0, 255)) <= 2,
                $"{Images.Background.State}, findings {findings0} → {findings1}, centre {CaptureRig.C(s.AtUv(0.5f, 0.5f))}");
            // a PNG signature with garbage after it: embedded (the sniff accepts it), the decode fails
            string broken = Path.Combine(g.App.Paths.BgDir, "bgtest_broken.png");
            var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            File.WriteAllBytes(broken, bytes);
            int dec0 = g.Session.Findings.Count(f => f.Code == "scene.imageDecodeFailed");
            yield return SetImages(broken, Keep);
            int dec1 = g.Session.Findings.Count(f => f.Code == "scene.imageDecodeFailed");
            g.Check("a PNG that does not decode: state failed, scene.imageDecodeFailed, the reference kept", Images.Background.State == SceneImages.StateFailed && dec1 > dec0
                && g.Session.Model.Settings.Background?.Kind == FileRef.Kinds.Res, $"{Images.Background.State}: {Images.Background.Error}");
            g.Check("the reply carries the warning", g.LastText.Contains("imageDecodeFailed"), g.Short);
        }

        // ---------------------------------------------------------------- B7 FromPicked
        g.Section("B7", "FileRefs.FromPicked");
        {
            var paths = g.App.Paths;
            var ctx = FileRefContext.Of(g.Rt, true);
            var ctxRef = FileRefContext.Of(g.Rt, false);
            string defBg = paths.Hs2Root == null ? null : Directory.Exists(Path.Combine(paths.Hs2Root, "DefaultData", "bg"))
                ? Directory.GetFiles(Path.Combine(paths.Hs2Root, "DefaultData", "bg"), "*.png").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault() : null;
            if (defBg != null)
            {
                var f = FileRefs.FromPicked(defBg, "background", ctx);
                g.Check("an HS2 DefaultData/bg file → {hs2: \"DefaultData/bg/<f>\"} (referenced, never embedded)", f.Kind == FileRef.Kinds.Hs2 && f.Value == "DefaultData/bg/" + Path.GetFileName(defBg), FileRefs.Describe(f));
                var f2 = FileRefs.FromPicked("hs2:DefaultData/bg/" + Path.GetFileName(defBg), "background", ctx);
                g.Check("the form hs2:<relative> gives the same reference", f2.Kind == FileRef.Kinds.Hs2 && f2.Value == f.Value, FileRefs.Describe(f2));
                // resolved and loaded for its size only (an Illusion image; not drawn here)
                g.Session.Set(null, "background", f);
                Images.ApplyNow();
                yield return WaitLoads();
                g.Check("the hs2 reference resolves through the HS2 root and loads (size only)", Images.Background.State == SceneImages.StateLoaded && Images.Background.Width > 0,
                    $"{Images.Background.State} {Images.Background.Width} × {Images.Background.Height}");
                g.Session.Set(null, "background", null);
                Images.ApplyNow();
            }
            else g.Warn("no HS2 DefaultData/bg: the DefaultData case is skipped");
            var fe = FileRefs.FromPicked(flat, "background", ctx);
            g.Check("a UserData/bg file → embedded {res: res/<sha256>.png}", fe.Kind == FileRef.Kinds.Res && fe.Value.StartsWith("res/") && fe.Value.EndsWith(".png") && g.Session.Model.File.Res.Contains(fe.Value), FileRefs.Describe(fe));
            var fu = FileRefs.FromPicked(flat, "background", ctxRef);
            g.Check("embed false: → {user: \"bg/bgtest_flat.png\"}", fu.Kind == FileRef.Kinds.User && fu.Value == "bg/bgtest_flat.png", FileRefs.Describe(fu));
            var fr = FileRefs.FromPicked("bg/bgtest_flat.png", "background", ctxRef);
            g.Check("a path relative to UserData resolves (embed false → the same user reference)", fr.Kind == FileRef.Kinds.User && fr.Value == "bg/bgtest_flat.png", FileRefs.Describe(fr));
            string hs2Frame = paths.Hs2UserData != null && Directory.Exists(Path.Combine(paths.Hs2UserData, "frame"))
                ? Directory.GetFiles(Path.Combine(paths.Hs2UserData, "frame"), "*.png").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault() : null;
            if (hs2Frame != null)
            {
                var fh = FileRefs.FromPicked(hs2Frame, "frame", ctxRef);
                g.Check("embed false: an HS2 UserData/frame file → {hs2: \"UserData/frame/<f>\"} (existence only)", fh.Kind == FileRef.Kinds.Hs2 && fh.Value == "UserData/frame/" + Path.GetFileName(hs2Frame), FileRefs.Describe(fh));
            }
            else g.Info("no HS2 UserData/frame file: that case is skipped");
            g.Check("a missing file is refused", !FileRefs.TryFromPicked(Path.Combine(paths.BgDir, "nope.png"), "background", ctx, out _, out var why1) && why1.StartsWith("file not found"), why1);
            string txt = Path.Combine(paths.BgDir, "notimage.png");
            File.WriteAllText(txt, "not an image");
            g.Check("a non-image is refused (embedding sniffs the bytes)", !FileRefs.TryFromPicked(txt, "background", ctx, out _, out var why2) && why2.Contains("neither a PNG nor a JPEG"), why2);
            g.Check("\"..\" in a relative path is refused", !FileRefs.TryFromPicked("../x.png", "background", ctx, out _, out var why3), why3);
            yield return g.Call(new { cmd = "scene.images", background = Path.Combine(paths.BgDir, "nope.png") });
            g.Check("scene.images with a missing file → not_found", !g.Ok && g.Code == "not_found", g.Short);
            yield return g.Call(new { cmd = "scene.images", background = txt });
            g.Check("scene.images with a non-image → bad_args", !g.Ok && g.Code == "bad_args", g.Short);
        }

        // ---------------------------------------------------------------- B8 the 3.5 pickers
        g.Section("B8", "the item panel / pattern pickers resolve");
        {
            var v = HS2Next.UI.Panels.InspectorValues.To(typeof(FileRef), flat) as FileRef;
            var h = v != null ? ItemTextures.File(g.App.Services, g.Session.Model.File.Res, v) : default;
            g.Check("InspectorValues.To(FileRef, <absolute path>) → a reference ItemTextures.File resolves to a texture", v != null && h.Texture != null, v == null ? "null" : FileRefs.Describe(v) + " " + (h.Error ?? ""));
            ItemTextures.Release(ref h);
            var old = FileRef.FromUser(flat);
            var ho = ItemTextures.File(g.App.Services, g.Session.Model.File.Res, old);
            g.Check("the 3.5 conversion FileRef.FromUser(<absolute path>) does not resolve (the latent bug)", ho.Texture == null && ho.Error != null, ho.Error);
            ItemTextures.Release(ref ho);
        }

        // ---------------------------------------------------------------- B9 save / load / import / undo
        g.Section("B9", "save, new, load, import, undo");
        {
            yield return g.Call(new { cmd = "scene.new" });
            yield return SetImages(flat, frame);
            var bgRef = (JObject)g.Last["background"]; var frRef = (JObject)g.Last["frame"];
            yield return g.Call(new { cmd = "history" });
            var undoList = g.Last?["undo"] as JArray;
            g.Check("scene.images background + frame = one entry \"Background\"", undoList != null && undoList.Count == 1 && ((string)undoList[0]).Contains("Background"), g.Short);
            yield return g.Call(new { cmd = "scene.save", name = "bgtest_scene", overwrite = true });
            g.Check("scene.save", g.Ok, g.Short);
            yield return g.Call(new { cmd = "scene.new" });
            g.Settle();
            g.Check("scene.new clears both and draws nothing", g.Session.Model.Settings.Background == null && g.Session.Model.Settings.Frame == null && Images.Background.Texture == null && HS2BackgroundFeature.Texture == null);
            yield return g.Call(new { cmd = "scene.load", name = "bgtest_scene" });
            yield return WaitLoads();
            yield return g.Call(new { cmd = "scene.images" });
            g.Check("scene.load restores the references (res, the same names) and draws them", g.Ok && JToken.DeepEquals(g.Last["background"], bgRef) && JToken.DeepEquals(g.Last["frame"], frRef)
                && Images.Background.State == SceneImages.StateLoaded && Images.Frame.State == SceneImages.StateLoaded, g.Short);
            yield return g.Call(new { cmd = "scene.new" });
            string scenePath = Path.Combine(g.App.Paths.SceneDir, "bgtest_scene" + SaveFormats.SceneExtension);
            yield return g.Call(new { cmd = "scene.import", path = scenePath });
            g.Check("scene.import ignores the other file's background / frame", g.Session.Model.Settings.Background == null && g.Session.Model.Settings.Frame == null, g.Short);
            yield return g.Call(new { cmd = "scene.new" });
            yield return SetImages(flat, Keep);
            yield return g.Call(new { cmd = "undo" });
            g.Settle();
            bool undone = g.Session.Model.Settings.Background == null;
            yield return g.Call(new { cmd = "redo" });
            g.Settle();
            g.Check("undo clears the background, redo restores it", undone && g.Session.Model.Settings.Background?.Kind == FileRef.Kinds.Res, g.Short);
        }

        // ---------------------------------------------------------------- B10 cost
        g.Section("B10", "cost");
        {
            g.ClearStage();
            g.Quad("wall", new Vector3(0, -2, 30), new Vector2(20, 4), new Color(0.3f, 0.3f, 0.3f));
            double T(int n)
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < n; i++) g.Capture(2560, 1440);
                return sw.Elapsed.TotalMilliseconds / n;
            }
            T(1);
            double with = T(3);
            yield return SetImages(null, null);
            double without = T(3);
            g.Measure($"2560 × 1440 capture with a background {with:F1} ms, without {without:F1} ms (difference {with - without:F2} ms incl. noise; the pass is one full-screen triangle)");
            g.Check("the background adds little to a 1440p capture (≤ 10 % incl. read-back noise; the 0.1 ms target is the GPU pass, not measured here)", with - without <= 0.1 * without, $"{with - without:F2} ms of {without:F1}");
        }
    }

    /// <summary>The view camera itself rendered into a target of w × h (what the view shows).</summary>
    static CaptureRig.Shot RenderView(int w, int h, string save = null)
    {
        g.Settle();
        var cam = g.View.Camera;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prevT = cam.targetTexture;
        cam.targetTexture = rt;
        cam.aspect = w / (float)h;
        cam.Render();
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        cam.targetTexture = prevT;
        cam.ResetAspect();
        var png = tex.EncodeToPNG();
        if (save != null) File.WriteAllBytes(Path.Combine(CaptureRig.OutDir, save + ".png"), png);
        Object.Destroy(tex);
        rt.Release();
        Object.Destroy(rt);
        return CaptureRig.Decode(png);
    }
}
