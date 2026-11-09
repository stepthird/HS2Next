// ScriptTest's capture part (Phase 6.9 package X): Play_X_Capture, a part of ScriptTest.RunPlay
// (play mode, graphics). Every capture renders each enabled skinned renderer with its skin matrices recalculated for the render
// (CaptureService.SkinScope). Images stay in memory: the files a path writes are read as numbers and deleted at once; nothing is shown.
// The cast are the clothed targets of Tools/targets.json; the animation is dance_00 (a plain row of its list).
//   JR1  BR-Chan dancing, the session held at 60 fps for the screen-rate moments (Application.targetFrameRate 60 with vSyncCount 0; when the
//        median frame delta of those moments is not within 14-20 ms they run again with Time.captureDeltaTime = 1/60, and the row says which
//        ran): each capture through `capture` (6), `screenshot` (6) and a frame recorded at the fixed step (8) equals a render of the same
//        moment with forceMatrixRecalculationPerRender forced on every enabled skinned renderer (made in CaptureEvents.Rendered, before the
//        frame moves on): 0 px over 2 levels at every moment. Present, per moment (as RV3): in CaptureEvents.Starting of the same capture,
//        before its render, a control render with CaptureService.RecalculateSkin false (the test hook); a moment is judged only when its
//        control differs from the forced render on at least 500 px (Step 0's S0-4: 1,386-2,928 at 640 x 360); at least 3 judged moments of a
//        judged path, else FAIL; the others are printed "not judged" with their number. The frame deltas' median and p95 are printed, and per
//        moment the frames since the skinned renderers were last drawn. The Present is judged on the recorded frames (each frame's capture
//        drew them the frame before); a batch Editor session draws nothing between the screen-rate captures (no game view), so their
//        controls show the moment's own pose: the screen-rate Present of capture and screenshot is T's JR4 in the player (printed here).
//   JR2  each renderer's own flag after a capture and a screenshot equals its flag before; the scope switched on every enabled renderer that
//        had it off (CaptureService.LastSkinScopeCount). Present: one renderer had it on before (and keeps it).
//   JX5r the app itself started with bridge.enabled false (launch settings {"bridge": {"enabled": false}}, the real bridge stage): Bridge
//        exists, Listening false, the bridge stage's line "in-process (no listener)"; a recording writes its 5 frames on the host's late
//        point, not FrameRecorderDriver's (no driver object exists before, during or after).
//   JR6  the mosaic rule itself on a moving member through the capture paths (as RV3, MosaicTest's fixture: BR-Chan, clothes hidden, a body
//        composite with marker cells inside the patches' UV; HERO107 with N_dan on and a marker checker on the members' texture): each walks
//        8 units along z = 2 by obj.moveTo (obj.transform steps while obj.moveTo is not registered) while dance_00 plays; the view 16 units
//        from the member, at each moment the one of nine (along its normal - HERO107: a fixed direction in the hips' frame -, four 40 degrees
//        off it, four 70 degrees off it) that shows the member most by the footprint alone, before any marker is read (for capture and
//        screenshot in the frame before the capture, for the recording in each frame for the next; each moment prints its view and the
//        footprint it was chosen by); 24 moments each through `capture`, `screenshot` and a frame
//        recorded at the fixed step (every third of 72), for (a) (the wide pass off) and (b) (on); at each moment, in its frame: the member's
//        footprint from a render of the members alone with the capture's camera, eroded 1 px; (a) the left-out control (the members drawn
//        unlit with the marker) and the blocks of the capture's camera step; (b) the coverage (the wide pass's debug view) and the render
//        with the wide pass off. (a): 0 marker pixels in the footprint, every drawn member's block >= F. (b): the coverage holds the
//        footprint dilated by 1.5 blocks; the changed pixels form flat blocks of the group's grid. Present: a moment under 150 px of
//        footprint is not judged; at least 20 judged per path and method; with (a) left out the marker shows in the footprint at every
//        judged moment; the member moved at least 5 units over the walk.
// The app runs on the row's port (or -port).
// Play_S_Thumbs (Phase 6.10 package S): every thumbnail renders under CaptureService.SkinScope (ThumbnailCapture).
// A batch Editor session draws nothing between renders, so where a moment needs the frame's draw before it (a step, the Front framing's default pose),
// a plain render of the scene thumbnail with the test hook off stands for it, in the same engine frame.
//   YT1  BR-Chan on dance_00 at speed 1, 24 moments each through (i) the scene thumbnail after one scene step (1/30 s) in the same engine frame (the
//        clock paused for these moments, as a step steps a paused clock only: each moment's step taken, else it is missing, and the hand's travel printed;
//        on a new scene's own view, as Step 0's thumbstep; the other paths on JR1's view of the whole figure), (ii) the
//        scene thumbnail at a command's point (scene.save {thumb: render}: SceneOps.ThumbnailRenderer read in place during the command), (iii) the card
//        thumbnail in the Front and (iv) the View framing (StudioOps.RenderCharaThumbnail, the save's texture): each equals a render of the same moment
//        with forceMatrixRecalculationPerRender forced on every enabled skinned renderer, 0 px over 2 levels. Present: with CaptureService.RecalculateSkin
//        false (the test hook) the stepped scene thumbnail differs from the forced render on at least 30,000 px and the Front framing on at least 20,000
//        px at 3 moments or more each; (ii) and (iv) print their hook-off numbers and the right hand's motion between the moments (at least 1 unit).
//   YT2  each renderer's own flag after a scene thumbnail and a Front and a View card thumbnail equals its flag before; the scope switched on every
//        enabled renderer that had it off (the scene thumbnail and the View framing; the Front framing's count is printed: its default pose may release
//        option items first). Present: one renderer had it on before (and keeps it).
//   YT3  the mosaic rule itself on a moving member through the scene thumbnail (JR6's fixture and walk; 504 x 704): at each of 24 moments, in its frame,
//        the view chosen of nine by the footprint alone, the scene thumbnail rendered, then its readings: the member's footprint with the thumbnail's
//        camera (eroded 1 px), the blocks of the thumbnail camera's step, (a) the left-out control, (b) the coverage and the render with the wide pass
//        off. (a): 0 marker pixels in the footprint, every drawn member's block >= F; (b): the coverage holds the footprint dilated by 1.5 blocks, the
//        changed pixels form flat blocks. Present: a moment under 150 px of footprint is not judged, at least 20 judged, else nothing is judged and the
//        row says so; with (a) left out the marker shows in the footprint at every judged moment; the member moved at least 5 units over the walk.
//   YT4  a paused BR-Chan: the scene thumbnail and the View card thumbnail byte-equal with the scope and with the test hook off. Present: the Front card
//        thumbnail of the same paused character differs between the two on at least 20,000 px.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HS2Next;
using HS2Next.Api.Plugins;
using HS2Next.App;
using HS2Next.Chara;
using HS2Next.Chara.Mosaic;
using HS2Next.Scenes.Runtime;
using HS2Next.Screenshots;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static partial class ScriptTest
{
    const int XW = 640, XH = 360, XLevels = 2, XLeastStale = 500;

    static IEnumerator Play_X_Capture()
    {
        if (!WantAny("JR1", "JR2", "JX5r", "JR6")) yield break;
        current = "X_Capture";
        if (!Check("X: a graphics device", SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, SystemInfo.graphicsDeviceType.ToString())) yield break;
        if (WantAny("JR1", "JR2", "JR6"))
        {
            yield return PStartApp();
            var app = AppCore.Current;
            if (!Check("X: the app runs with its bridge and runtime", app != null && app.Stage == AppStage.Ready && app.Bridge != null && app.Runtime != null, app?.StageName ?? "none")) yield break;
            CaptureEvents.Rendered += XOnRendered;
            CaptureEvents.Starting += XOnStarting;
            try
            {
                if (POpen("JR1", "a capture of a moving character equals a render with forced recalculation (capture, screenshot, recorded frame)")) yield return XJr1(app);
                if (POpen("JR2", "each renderer's own flag comes back after a capture")) yield return XJr2(app);
                if (POpen("JR6", "the mosaic rule on a moving member through capture, screenshot and a recorded frame")) yield return XJr6(app);
            }
            finally
            {
                CaptureEvents.Rendered -= XOnRendered;
                CaptureEvents.Starting -= XOnStarting;
                xArmed = null;
                xStarting = null;
                CaptureService.RecalculateSkin = true;
                if (FrameRecorder.IsRecording) FrameRecorder.Current.Stop();
            }
            yield return PStopApp();
        }
        if (POpen("JX5r", "bridge.enabled false: a recording takes the host's late point")) yield return XJx5r();
    }

    // ------------------------------------------------------------ the reference render of a capture's moment

    /// <summary>Called in CaptureEvents.Rendered (the capture's frame, right after its render) while armed; null = not armed.</summary>
    static Action<CaptureInfo> xArmed;

    static void XOnRendered(CaptureInfo info)
    {
        var a = xArmed;
        if (a == null) return;
        try { a(info); }
        catch (Exception e) { xHandlerErrors.Add(e.GetType().Name + ": " + e.Message); }
    }

    static readonly List<string> xHandlerErrors = new List<string>();

    /// <summary>Called in CaptureEvents.Starting (the capture's frame, right before its render) while armed; null = not armed.</summary>
    static Action<CaptureInfo> xStarting;

    static void XOnStarting(CaptureInfo info)
    {
        var a = xStarting;
        if (a == null) return;
        try { a(info); }
        catch (Exception e) { xHandlerErrors.Add(e.GetType().Name + ": " + e.Message); }
    }

    /// <summary>The view rendered now through CaptureService with the test hook off (each renderer as it is: the late point's skin); in memory.</summary>
    static Color32[] XHookOffRender(AppCore app, int w, int h, string aa, bool frame)
    {
        bool was = CaptureService.RecalculateSkin;
        CaptureService.RecalculateSkin = false;
        try
        {
            var view = app.Runtime.GetPart<SceneViewCamera>()?.Camera;
            return XDecode(CaptureService.RenderSync(app.Runtime, view, new CaptureRequest { Width = w, Height = h, Aa = aa, Frame = frame, MaxPixels = CaptureLimits.HardMaxPixels }).Png);
        }
        finally { CaptureService.RecalculateSkin = was; }
    }

    /// <summary>The view rendered now through CaptureService with forceMatrixRecalculationPerRender on every enabled skinned renderer (each
    /// renderer's flag put back after); the PNG decoded in memory.</summary>
    static Color32[] XForcedRender(AppCore app, int w, int h, string aa, bool frame)
    {
        var smrs = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(s => s && s.enabled).ToArray();
        var saved = smrs.Select(s => s.forceMatrixRecalculationPerRender).ToArray();
        try
        {
            foreach (var s in smrs) s.forceMatrixRecalculationPerRender = true;
            var view = app.Runtime.GetPart<SceneViewCamera>()?.Camera;
            var res = CaptureService.RenderSync(app.Runtime, view, new CaptureRequest { Width = w, Height = h, Aa = aa, Frame = frame, MaxPixels = CaptureLimits.HardMaxPixels });
            return XDecode(res.Png);
        }
        finally { for (int i = 0; i < smrs.Length; i++) if (smrs[i]) smrs[i].forceMatrixRecalculationPerRender = saved[i]; }
    }

    static Color32[] XDecode(byte[] png)
    {
        if (png == null) return null;
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try { return t.LoadImage(png) ? t.GetPixels32() : null; }
        finally { Object.Destroy(t); }
    }

    static Color32[] XReadAndDelete(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        var px = XDecode(File.ReadAllBytes(path));
        File.Delete(path);
        return px;
    }

    /// <summary>Pixels whose largest channel difference is over <see cref="XLevels"/>; -1 when either image is missing or the sizes differ.</summary>
    static int XDiff(Color32[] a, Color32[] b, out int most)
    {
        most = 0;
        if (a == null || b == null || a.Length != b.Length) return -1;
        int n = 0;
        for (int i = 0; i < a.Length; i++)
        {
            int d = Math.Max(Math.Abs(a[i].r - b[i].r), Math.Max(Math.Abs(a[i].g - b[i].g), Math.Abs(a[i].b - b[i].b)));
            if (d > XLevels) n++;
            if (d > most) most = d;
        }
        return n;
    }

    /// <summary>A recording's folder emptied and removed (frames and record.json only; under the app's output root).</summary>
    static void XRemoveRecord(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        if ((new DirectoryInfo(folder).Attributes & FileAttributes.ReparsePoint) != 0) return;
        foreach (var f in Directory.GetFiles(folder)) File.Delete(f);
        if (Directory.GetFileSystemEntries(folder).Length == 0) Directory.Delete(folder);
    }

    // ------------------------------------------------------------ JR1

    sealed class XMoment
    {
        public string Path;
        public int Frame, SinceDrawn = -1;
        public float Dt;
        public Color32[] Control, Reference;
    }

    // the frame of the last camera render outside JR1's own control render: what drew the skinned renderers last (the player's main camera
    // draws every frame; a batch Editor session draws nothing between captures)
    static int xLastDrawFrame = -1;
    static bool xInControl;

    static void XOnEndCamera(ScriptableRenderContext c, Camera cam)
    {
        if (!xInControl) xLastDrawFrame = Time.frameCount;
    }

    /// <summary>A scene of BR-Chan dancing (dance_00), the view on her whole figure; the node key, or null.</summary>
    static IEnumerator XDancer(AppCore app, string[] key)
    {
        key[0] = null;
        yield return PCall(app, new { cmd = "scene.new" });
        yield return PCall(app, new { cmd = "chara.load", path = femaleRel, sex = "female" });
        string k = Key(pLast);
        if (!Check("X: BR-Chan loads", POk && k != null, Short(pLastText))) yield break;
        yield return PCall(app, new { cmd = "chara.anime", key = k, group = 11, category = 5, no = 0, speed = 1f, loop = true });
        if (!Check("X: dance_00 plays (a plain row of Tools/targets.json)", POk, Short(pLastText))) yield break;
        yield return PCall(app, new { cmd = "camera.set", target = new[] { 0f, 8f, 0f }, rot = new[] { 5f, 180f, 0f }, dist = 42f, fov = 23f });
        yield return PFrames(23);
        key[0] = k;
    }

    static double XMedian(List<float> v) { if (v.Count == 0) return 0; var s = v.OrderBy(x => x).ToList(); return s[s.Count / 2]; }
    static double XP95(List<float> v) { if (v.Count == 0) return 0; var s = v.OrderBy(x => x).ToList(); return s[Math.Min(s.Count - 1, (int)Math.Ceiling(0.95 * s.Count) - 1)]; }

    /// <summary>The screen-rate moments of JR1: <paramref name="n"/> captures through <paramref name="path"/> (capture or screenshot), each armed
    /// with its control (Starting) and its forced render (Rendered); the moments land in <paramref name="into"/> with the capture's pixels.</summary>
    static IEnumerator XJr1Screen(AppCore app, string path, int n, string tag, List<(XMoment m, Color32[] px)> into)
    {
        var moments = new Dictionary<int, XMoment>();
        XJr1Arm(app, moments);
        try
        {
            for (int i = 0; i < n; i++)
            {
                if (path == "capture") yield return PCall(app, new { cmd = "capture", name = "x_jr1_" + tag + i, width = XW, height = XH, aa = "none", frame = false });
                else yield return PCall(app, new { cmd = "screenshot", name = "x_jr1_s" + tag + i, mode = "custom", width = XW, height = XH, supersample = 1, frame = false });
                string file = (string)pLast?["path"];
                var px = XReadAndDelete(file);
                var m = moments.Values.FirstOrDefault(x => string.Equals(x.Path, file, StringComparison.OrdinalIgnoreCase));
                into.Add((m, px));
                yield return PFrames(7);
            }
        }
        finally { xArmed = null; xStarting = null; }
    }

    /// <summary>Arms the handlers of one capture's moment: the hook-off control before its render, the forced render after it.</summary>
    static void XJr1Arm(AppCore app, Dictionary<int, XMoment> moments)
    {
        xStarting = info =>
        {
            string aa = info.Kind == CaptureKind.Capture ? CaptureService.None : CaptureService.Smaa;
            var m = new XMoment { Path = info.Path, Frame = Time.frameCount, Dt = Time.deltaTime, SinceDrawn = xLastDrawFrame < 0 ? -1 : Time.frameCount - xLastDrawFrame };
            xInControl = true;
            try { m.Control = XHookOffRender(app, info.Width, info.Height, aa, info.DrawsFrame); }
            finally { xInControl = false; }
            moments[info.Id] = m;
        };
        xArmed = info =>
        {
            if (!moments.TryGetValue(info.Id, out var m)) return;
            string aa = info.Kind == CaptureKind.Capture ? CaptureService.None : CaptureService.Smaa;
            m.Reference = XForcedRender(app, info.Width, info.Height, aa, info.DrawsFrame);
        };
    }

    static IEnumerator XJr1(AppCore app)
    {
        var key = new string[1];
        yield return XDancer(app, key);
        if (key[0] == null) yield break;
        xHandlerErrors.Clear();
        int rate0 = Application.targetFrameRate, vsync0 = QualitySettings.vSyncCount;
        float capture0 = Time.captureDeltaTime;
        var results = new Dictionary<string, List<(XMoment m, Color32[] px)>>();
        string mode = null;
        xLastDrawFrame = -1;
        RenderPipelineManager.endCameraRendering += XOnEndCamera;
        try
        {
            // the screen-rate moments at 60 fps: the target frame rate first; the fixed delta when the session does not honour it
            foreach (var attempt in new[] { "Application.targetFrameRate 60 (vSyncCount 0)", "Time.captureDeltaTime 1/60" })
            {
                if (attempt.StartsWith("Application"))
                {
                    QualitySettings.vSyncCount = 0;
                    Application.targetFrameRate = 60;
                    Time.captureDeltaTime = capture0;
                }
                else
                {
                    Application.targetFrameRate = rate0;
                    QualitySettings.vSyncCount = vsync0;
                    Time.captureDeltaTime = 1f / 60f;
                }
                yield return PFrames(11);
                var cap = new List<(XMoment, Color32[])>();
                var shot = new List<(XMoment, Color32[])>();
                yield return XJr1Screen(app, "capture", 6, attempt.StartsWith("Application") ? "t" : "c", cap);
                yield return XJr1Screen(app, "screenshot", 6, attempt.StartsWith("Application") ? "t" : "c", shot);
                var dts = cap.Concat(shot).Where(x => x.Item1 != null).Select(x => x.Item1.Dt * 1000f).ToList();
                double med = XMedian(dts), p95 = XP95(dts);
                Info($"JR1 screen-rate moments with {attempt}: frame delta median {med:F1} ms, p95 {p95:F1} ms over {dts.Count} moments");
                if (med >= 14 && med <= 20)
                {
                    mode = attempt;
                    results["capture"] = cap;
                    results["screenshot"] = shot;
                    Measure($"JR1 screen rate held by {attempt}: frame delta median {med:F1} ms, p95 {p95:F1} ms");
                    break;
                }
                Info($"JR1: {attempt} did not hold the frame delta within 14-20 ms; these moments are not judged");
            }
            Time.captureDeltaTime = capture0;
            Application.targetFrameRate = rate0;
            QualitySettings.vSyncCount = vsync0;
            if (!Check("JR1: the screen-rate moments ran at 60 fps (frame delta median within 14-20 ms)", mode != null, "")) yield break;
            yield return PFrames(3);

            // a recording at the fixed step: 8 frames
            var moments = new Dictionary<int, XMoment>();
            XJr1Arm(app, moments);
            int ids0 = CaptureEvents.LastId;
            yield return PCall(app, new { cmd = "record.start", path = "record/x_jr1", width = XW, height = XH, fps = 30, frames = 8, overwrite = true });
            string folder = (string)pLast?["path"];
            var rec = new List<(XMoment, Color32[])>();
            if (Check("JR1: record.start", POk && folder != null, Short(pLastText)))
            {
                float until = Time.realtimeSinceStartup + 60f;
                while (Time.realtimeSinceStartup < until)
                {
                    yield return PCall(app, new { cmd = "record.status" });
                    if (pLast?.Value<bool?>("recording") == false && (int?)pLast["written"] == (int?)pLast["frames"]) break;
                    yield return PFrames(2);
                }
                xArmed = null; xStarting = null;
                foreach (var kv in moments.Where(kv => kv.Key > ids0 && kv.Value.Path != null && kv.Value.Path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)).OrderBy(kv => kv.Key))
                    rec.Add((kv.Value, XDecodeFile(kv.Value.Path)));
                XRemoveRecord(folder);
            }
            results["record"] = rec;
        }
        finally
        {
            xArmed = null; xStarting = null;
            RenderPipelineManager.endCameraRendering -= XOnEndCamera;
            Time.captureDeltaTime = capture0;
            Application.targetFrameRate = rate0;
            QualitySettings.vSyncCount = vsync0;
        }
        foreach (var path in new[] { "capture", "screenshot", "record" })
        {
            var l = results[path];
            int want = path == "record" ? 8 : 6, judged = 0, bad = 0;
            var lines = new List<string>();
            for (int i = 0; i < l.Count; i++)
            {
                var (m, px) = l[i];
                int d = m == null ? -1 : XDiff(px, m.Reference, out int most);
                int c = m == null ? -1 : XDiff(m.Control, m.Reference, out int cmost);
                if (d != 0) bad++;
                bool judge = c >= XLeastStale;
                if (judge) judged++;
                lines.Add($"m{i} frame {m?.Frame} dt {(m?.Dt ?? 0) * 1000f:F1} ms, frames since the skinned renderers were last drawn {m?.SinceDrawn}: capture {d} px, control {c} px{(judge ? "" : " (not judged)")}");
            }
            Check($"JR1 {path}: each capture equals the forced render of its moment (0 px over {XLevels} levels; {l.Count} of {want} moments)", l.Count == want && bad == 0, string.Join("; ", lines));
            if (path == "record")
                Check($"JR1 {path}: Present: the hook-off control of the moment differs from it on at least {XLeastStale} px at {judged} judged moments (at least 3)", judged >= 3, $"{judged} judged of {l.Count}");
            else
                Info($"JR1 {path}: the screen-rate Present is T's JR4 in the player: this batch Editor session draws nothing between captures, so the hook-off control is the moment's own pose ({judged} of {l.Count} moments over {XLeastStale} px)");
            foreach (var s in lines) Info($"JR1 {path} {s}");
            Measure($"JR1 {path}: {judged} judged of {l.Count}; " + string.Join(", ", lines.Select(s => s.Substring(s.IndexOf(':') + 2))));
        }
        if (xHandlerErrors.Count > 0) Check("JR1: the control and reference renders ran", false, string.Join("; ", xHandlerErrors.Take(3)));
        Info("JR1: skinned renderers switched on by the last capture's scope: " + CaptureService.LastSkinScopeCount + "; the screen-rate moments ran with " + mode);
    }

    static Color32[] XDecodeFile(string path) => path != null && File.Exists(path) ? XDecode(File.ReadAllBytes(path)) : null;

    // ------------------------------------------------------------ JR2

    static IEnumerator XJr2(AppCore app)
    {
        var key = new string[1];
        yield return XDancer(app, key);
        if (key[0] == null) yield break;
        var smrs = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(s => s && s.enabled).ToArray();
        if (!Check("JR2: enabled skinned renderers in the scene", smrs.Length > 1, smrs.Length + "")) yield break;
        var chosen = smrs.FirstOrDefault(s => !s.forceMatrixRecalculationPerRender) ?? smrs[0];
        bool chosenWas = chosen.forceMatrixRecalculationPerRender;
        chosen.forceMatrixRecalculationPerRender = true;
        try
        {
            var before = smrs.Select(s => s.forceMatrixRecalculationPerRender).ToArray();
            int off = before.Count(b => !b);
            foreach (var (cmd, req) in new (string, object)[]
            {
                ("capture", new { cmd = "capture", name = "x_jr2", width = XW, height = XH, aa = "none", frame = false }),
                ("screenshot", new { cmd = "screenshot", name = "x_jr2_s", mode = "custom", width = XW, height = XH, supersample = 1, frame = false }),
            })
            {
                yield return PCall(app, req);
                XReadAndDelete((string)pLast?["path"]);
                yield return PFrames(2);
                var after = smrs.Select(s => s && s.forceMatrixRecalculationPerRender).ToArray();
                int changed = Enumerable.Range(0, smrs.Length).Count(i => after[i] != before[i]);
                Check($"JR2 {cmd}: every renderer's flag equals its flag before ({smrs.Length} renderers, {off} off, {smrs.Length - off} on)", POk && changed == 0, $"{changed} changed; {Short(pLastText)}");
                Check($"JR2 {cmd}: Present: the renderer that had it on keeps it ({chosen.name})", chosen && chosen.forceMatrixRecalculationPerRender, "");
                Check($"JR2 {cmd}: the scope switched on every enabled renderer that had it off", CaptureService.LastSkinScopeCount == off, $"{CaptureService.LastSkinScopeCount} switched, {off} off before");
            }
        }
        finally { if (chosen) chosen.forceMatrixRecalculationPerRender = chosenWas; }
    }

    // ------------------------------------------------------------ JX5r

    /// <summary>The app's launch settings with bridge.enabled false (its own UserData and output folder under Spike/out/script).</summary>
    static string XOffLaunch =>
        "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":" + (HasArg("-noUi") ? "false" : "true") + ",\"language\":\"en\"},\"userData\":\"Spike/out/script/x_off_userdata\",\"bridge\":{\"enabled\":false,\"port\":" + XPort +
        ",\"outputDir\":\"Spike/out/script/x_off_out\",\"allowQuit\":false}}";

    static IEnumerator XJx5r()
    {
        if (pApp != null) yield return PStopApp();
        XRemove(Path.Combine(XOut, "x_off_userdata"));
        XRemove(Path.Combine(XOut, "x_off_out"));
        int drivers0 = Object.FindObjectsByType<FrameRecorderDriver>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        var args = Environment.GetCommandLineArgs();
        AppSettings s;
        try { s = AppSettings.Load(args, XOffLaunch); }
        catch (Exception e) { Check("JX5r: the app's settings", false, e.Message); s = AppSettings.Defaults(args, XOffLaunch); }
        pApp = AppBootstrap.Start(s);
        float ready = Time.realtimeSinceStartup + 180f;
        while (Time.realtimeSinceStartup < ready && (AppCore.Current == null || AppCore.Current.Stage != AppStage.Ready)) yield return null;
        var app = AppCore.Current;
        try
        {
            if (!Check("JX5r: the app started with bridge.enabled false", app != null && app.Stage == AppStage.Ready && !app.Settings.Bridge.Enabled, app?.StageName ?? "none")) yield break;
            Check("JX5r: AppCore.Bridge exists, Listening false", app.Bridge != null && !app.Bridge.Listening && !app.Bridge.ManualPump, app.Bridge?.Server.State + "");
            Check("JX5r: the bridge stage's line reads \"" + AppCore.NoListenerSummary + "\"", app.BridgeStageSummary == AppCore.NoListenerSummary, app.BridgeStageSummary ?? "null");
            yield return PCall(app, new { cmd = "record.start", path = "record/x_jx5r", width = 64, height = 36, fps = 30, frames = 5, overwrite = true });
            string folder = (string)pLast?["path"];
            if (!Check("JX5r: record.start in process", POk && folder != null, Short(pLastText))) yield break;
            int drivers1 = Object.FindObjectsByType<FrameRecorderDriver>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            float until = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < until)
            {
                yield return PCall(app, new { cmd = "record.status" });
                if (pLast?.Value<bool?>("recording") == false && (int?)pLast["written"] == (int?)pLast["frames"]) break;
                yield return PFrames(2);
            }
            int files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.png").Length : 0;
            Check("JX5r: 5 frames written", files == 5 && (int?)pLast?["written"] == 5, $"{files} files, {Short(pLastText)}");
            int drivers2 = Object.FindObjectsByType<FrameRecorderDriver>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            Check("JX5r: the recorder took the host's late point (no FrameRecorderDriver before, during or after)", drivers0 == 0 && drivers1 == 0 && drivers2 == 0, $"drivers {drivers0} / {drivers1} / {drivers2}");
            XRemoveRecord(folder);
        }
        finally { }
        yield return PStopApp();
        yield return PFrames(5);
        try { XRemove(Path.Combine(XOut, "x_off_userdata")); XRemove(Path.Combine(XOut, "x_off_out")); }
        catch (Exception e) { Info("JX5r: the run's folders stay: " + e.Message); }
    }

    // ------------------------------------------------------------ JR6

    const int Jr6W = 960, Jr6H = 540, Jr6Moments = 24, Jr6LeastJudged = 20, Jr6LeastFootprint = 150, Jr6RecordEvery = 3;
    const float Jr6View = 16f, Jr6Walk = 8f, Jr6Seconds = 2.5f, Jr6LeastMoved = 5f, Jr6Line = 2f;
    static readonly string[] Jr6ViewNames = { "normal", "40 +x", "40 -x", "40 +y", "40 -y", "70 +x", "70 -x", "70 +y", "70 -y" };

    /// <summary>One character of MosaicTest's fixture in the app's scene.</summary>
    sealed class Jr6Fix
    {
        public string Label, Key;
        public bool Male;
        public CharaContext Ctx;
        public MosaicMember Focus;
        public Transform Hips;
        public Vector3 LocalView;
        public Texture Marker, Gradient;
        public readonly List<(Material m, Texture t)> Saved = new List<(Material, Texture)>();

        public Vector3 Centre => MosaicCamera.RegionMatrix(Focus).MultiplyPoint3x4(Focus.Region.Centroid);

        /// <summary>The way the view looks at the member: its normal on BR-Chan, a fixed direction in the hips' frame on HERO107 (as RV3).</summary>
        public Vector3 Normal
        {
            get
            {
                if (Male) return (Hips.rotation * LocalView).normalized;
                var n = MosaicCamera.RegionMatrix(Focus).MultiplyVector(Focus.Region.Normal).normalized;
                return n == Vector3.zero ? Ctx.Root.transform.forward : n;
            }
        }
    }

    /// <summary>What a moment's handler read in the capture's frame, right after its render.</summary>
    sealed class Jr6Moment
    {
        public string Path;
        public bool[] Fp;
        public int Fpn, Drawn, Under, W, H;
        public float Floor, LeastBlock = float.MaxValue;
        public bool CaptureFrame;
        public Color32[] Control, Cover, Off;
        public List<MosaicWideGroup> Groups;
        public Vector3 Centre;
        /// <summary>The view chosen for this moment (an index of Jr6ViewNames) and the footprint it was chosen by.</summary>
        public int View = -1, ChoiceFp;
    }

    sealed class Jr6Tally
    {
        public int Judged, NotJudged, Missing, Marks, NoCtrl, Under, NoBlock, Uncovered, Blocks, Mixed, NoGroup, LeastFp = int.MaxValue, LeastCtrl = int.MaxValue;
        public float LeastBlockRatio = float.MaxValue, LeastB = float.MaxValue, Moved;
        public Vector3? First;
        public readonly List<string> Lines = new List<string>();
    }

    static Camera xJr6Cam;

    static IEnumerator XJr6(AppCore app)
    {
        bool wide0 = MosaicWide.Enabled;
        bool mover = app.Bridge.Server.Commands.Contains("obj.moveTo");
        Info("JR6: the walk " + (mover ? "by obj.moveTo" : "by obj.transform steps (obj.moveTo is not registered in this tree)") + $", {Jr6Walk} units in {Jr6Seconds} s (the recording: {Jr6Moments * Jr6RecordEvery} frames at 30 fps), dance_00 playing; {Jr6Moments} moments a walk, {Jr6W} x {Jr6H}");
        try
        {
            foreach (var (label, rel, sex) in new[] { ("BR-Chan", femaleRel, "female"), ("HERO107", maleRel, "male") })
            {
                var box = new Jr6Fix[1];
                yield return XJr6Fixture(app, label, rel, sex, box);
                var fix = box[0];
                if (fix == null) continue;
                foreach (bool wide in new[] { false, true })
                {
                    MosaicWide.Enabled = wide;
                    if (!fix.Male) MosaicTest.Jr6UseComposite(fix.Ctx, wide ? fix.Gradient : fix.Marker);
                    foreach (var path in new[] { "capture", "screenshot", "record" })
                    {
                        var t = new Jr6Tally();
                        yield return XJr6Walk(app, fix, wide, path, mover, t);
                        XJr6Verdict(fix, wide, path, t);
                    }
                }
                foreach (var (m, tex) in fix.Saved) if (m) m.SetTexture("_MainTex", tex);
            }
        }
        finally
        {
            MosaicWide.Enabled = wide0;
            MosaicWide.DebugView = false;
            xArmed = null;
        }
        yield return PCall(app, new { cmd = "scene.new" });
        yield return PFrames(3);
        MosaicTest.Jr6Cleanup();
        xJr6Cam = null;
    }

    static IEnumerator XJr6Fixture(AppCore app, string label, string rel, string sex, Jr6Fix[] box)
    {
        box[0] = null;
        yield return PCall(app, new { cmd = "scene.new" });
        yield return PCall(app, new { cmd = "chara.load", path = rel, sex });
        string k = Key(pLast);
        if (!Check($"JR6 {label} loads", POk && k != null, Short(pLastText))) yield break;
        yield return PCall(app, new { cmd = "chara.anime", key = k, group = 11, category = 5, no = 0, speed = 1f, loop = true });
        if (!Check($"JR6 {label}: dance_00 plays", POk, Short(pLastText))) yield break;
        yield return PCall(app, new { cmd = "obj.transform", key = k, pos = new[] { -Jr6Walk / 2f, 0f, Jr6Line }, rot = new[] { 0f, 90f, 0f }, undo = false });
        yield return PFrames(10);
        var ctx = app.Runtime.Charas().FirstOrDefault(c => c.NodeId == k)?.Context;
        if (!Check($"JR6 {label}: the character is built", ctx != null && ctx.Root, "")) yield break;
        bool male = sex == "male";
        MosaicTest.Jr6HideClothes(ctx);
        if (male)
        {
            var nDan = HS2Next.Chara.Edit.EditContext.Find(ctx.Body.transform, "N_dan");
            if (nDan) nDan.gameObject.SetActive(true);
        }
        var fix = new Jr6Fix { Label = label, Key = k, Male = male, Ctx = ctx, Focus = MosaicTest.Jr6Member(ctx, male ? "cm_o_dan00" : "o_mnpb") };
        if (ctx.BodyBones != null && ctx.BodyBones.TryGetValue("cf_J_Hips", out var hips)) fix.Hips = hips;
        if (!Check($"JR6 {label}: the focus member and the hips exist", fix.Focus != null && fix.Hips, "")) yield break;
        if (male)
        {
            fix.LocalView = Quaternion.Inverse(fix.Hips.rotation) * new Vector3(0f, -0.8f, 0.6f).normalized;
            fix.Marker = MosaicTest.Jr6Checker();
            foreach (var mm in MosaicSet.StateOf(ctx).Members)
                foreach (var mat in mm.Renderer.sharedMaterials)
                    if (mat && mat.HasProperty("_MainTex") && !fix.Saved.Exists(x => x.m == mat)) { fix.Saved.Add((mat, mat.GetTexture("_MainTex"))); mat.SetTexture("_MainTex", fix.Marker); }
        }
        else
        {
            fix.Marker = MosaicTest.Jr6MarkerComposite(ctx);
            fix.Gradient = MosaicTest.Jr6GradientComposite(ctx);
        }
        box[0] = fix;
    }

    static object XJr6Step(Jr6Fix fix, float u) => new { cmd = "obj.transform", key = fix.Key, pos = new[] { -Jr6Walk / 2f + Jr6Walk * u, 0f, Jr6Line }, undo = false };

    /// <summary>The nine ways to look at the member (as RV3): its direction, four 40 degrees off it, four 70 degrees off it.</summary>
    static Vector3[] XJr6Views(Vector3 n)
    {
        var x = Vector3.Cross(n, Mathf.Abs(n.y) < .9f ? Vector3.up : Vector3.right).normalized;
        var y = Vector3.Cross(n, x);
        float c = Mathf.Cos(40f * Mathf.Deg2Rad), s = Mathf.Sin(40f * Mathf.Deg2Rad), c7 = Mathf.Cos(70f * Mathf.Deg2Rad), s7 = Mathf.Sin(70f * Mathf.Deg2Rad);
        return new[]
        {
            n, (c * n + s * x).normalized, (c * n - s * x).normalized, (c * n + s * y).normalized, (c * n - s * y).normalized,
            (c7 * n + s7 * x).normalized, (c7 * n - s7 * x).normalized, (c7 * n + s7 * y).normalized, (c7 * n - s7 * y).normalized,
        };
    }

    static Quaternion XJr6Look(Vector3 d) => Quaternion.LookRotation(-d, Mathf.Abs(d.y) > 0.95f ? Vector3.forward : Vector3.up);

    /// <summary>The view of the nine that shows the member most now, by the footprint alone (members drawn flat, no marker read): its index,
    /// its footprint and the camera.set request that places the view there.</summary>
    static (int view, int fp, object aim) XJr6Choose(AppCore app, Jr6Fix fix, int w, int h)
    {
        var cam = app.Runtime.GetPart<SceneViewCamera>().Camera;
        if (!xJr6Cam) xJr6Cam = MosaicTest.Jr6Camera();
        xJr6Cam.fieldOfView = 23f;
        xJr6Cam.nearClipPlane = cam.nearClipPlane;
        xJr6Cam.farClipPlane = cam.farClipPlane;
        var c = fix.Centre;
        var views = XJr6Views(fix.Normal);
        int best = 0, bestFp = -1;
        for (int i = 0; i < views.Length; i++)
        {
            xJr6Cam.transform.SetPositionAndRotation(c + views[i] * Jr6View, XJr6Look(views[i]));
            int n = 0;
            XForced(() => n = MosaicTest.Jr6Footprint(xJr6Cam, w, h, fix.Ctx).Count(b => b));
            if (n > bestFp) { bestFp = n; best = i; }
        }
        var e = XJr6Look(views[best]).eulerAngles;
        return (best, bestFp, new { cmd = "camera.set", target = new[] { c.x, c.y, c.z }, rot = new[] { e.x, e.y, e.z }, dist = Jr6View, fov = 23f });
    }

    static IEnumerator XJr6Walk(AppCore app, Jr6Fix fix, bool wide, string path, bool mover, Jr6Tally t)
    {
        yield return PCall(app, new { cmd = "obj.transform", key = fix.Key, pos = new[] { -Jr6Walk / 2f, 0f, Jr6Line }, rot = new[] { 0f, 90f, 0f }, undo = false });
        yield return PFrames(3);
        var first = XJr6Choose(app, fix, Jr6W, Jr6H);
        yield return PCall(app, first.aim);
        yield return PFrames(2);
        var moments = new Dictionary<string, Jr6Moment>(StringComparer.OrdinalIgnoreCase);
        int recIndex = 0;
        var chosen = (view: first.view, fp: first.fp);
        var server = app.Bridge.Server;
        xHandlerErrors.Clear();
        xArmed = info =>
        {
            if (path == "record")
            {
                int idx = recIndex++;
                if (!mover) server.SubmitAsync(JsonConvert.SerializeObject(XJr6Step(fix, Mathf.Min(1f, (idx + 1) / (float)(Jr6Moments * Jr6RecordEvery)))));
                if (idx % Jr6RecordEvery == 0)
                {
                    var rm = XJr6Read(app, fix, wide, info);
                    rm.View = chosen.view; rm.ChoiceFp = chosen.fp;
                    moments[info.Path] = rm;
                }
                var next = XJr6Choose(app, fix, info.Width, info.Height);   // the next frame's view, by the footprint alone
                chosen = (next.view, next.fp);
                server.SubmitAsync(JsonConvert.SerializeObject(next.aim));
                return;
            }
            var mm = XJr6Read(app, fix, wide, info);
            mm.View = chosen.view; mm.ChoiceFp = chosen.fp;
            moments[info.Path] = mm;
        };
        try
        {
            if (path == "record")
            {
                if (mover) yield return PCall(app, new { cmd = "obj.moveTo", key = fix.Key, target = new[] { Jr6Walk / 2f, 0f, Jr6Line }, stop = 0f, time = Jr6Moments * Jr6RecordEvery / 30f });
                yield return PCall(app, new { cmd = "record.start", path = "record/x_jr6", width = Jr6W, height = Jr6H, fps = 30, frames = Jr6Moments * Jr6RecordEvery, overwrite = true });
                string folder = (string)pLast?["path"];
                if (!Check($"JR6 {fix.Label} {(wide ? "(b)" : "(a)")} record: record.start", POk && folder != null, Short(pLastText))) yield break;
                float until = Time.realtimeSinceStartup + 180f;
                while (Time.realtimeSinceStartup < until)
                {
                    yield return PCall(app, new { cmd = "record.status" });
                    if (pLast?.Value<bool?>("recording") == false && (int?)pLast["written"] == (int?)pLast["frames"]) break;
                    yield return PFrames(3);
                }
                xArmed = null;
                int i = 0;
                foreach (var m in moments.Values.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase)) XJr6Judge(m, XDecodeFile(m.Path), wide, t, i++);
                XRemoveRecord(folder);
            }
            else
            {
                if (mover) yield return PCall(app, new { cmd = "obj.moveTo", key = fix.Key, target = new[] { Jr6Walk / 2f, 0f, Jr6Line }, stop = 0f, time = Jr6Seconds });
                for (int i = 0; i < Jr6Moments; i++)
                {
                    if (!mover) yield return PCall(app, XJr6Step(fix, (i + 1) / (float)Jr6Moments));
                    var pick = XJr6Choose(app, fix, Jr6W, Jr6H);   // in the frame before the capture, by the footprint alone
                    chosen = (pick.view, pick.fp);
                    yield return PCall(app, pick.aim);
                    if (path == "capture") yield return PCall(app, new { cmd = "capture", name = "x_jr6", width = Jr6W, height = Jr6H, aa = "none", frame = false });
                    else yield return PCall(app, new { cmd = "screenshot", name = "x_jr6_s", mode = "custom", width = Jr6W, height = Jr6H, supersample = 1, frame = false });
                    string file = (string)pLast?["path"];
                    var cap = XReadAndDelete(file);
                    moments.TryGetValue(file ?? "", out var m);
                    XJr6Judge(m, cap, wide, t, i);
                    yield return PFrames(2);
                }
            }
        }
        finally { xArmed = null; }
        if (xHandlerErrors.Count > 0) Check($"JR6 {fix.Label} {(wide ? "(b)" : "(a)")} {path}: the moments' renders ran", false, string.Join("; ", xHandlerErrors.Take(3)));
    }

    /// <summary>Forces forceMatrixRecalculationPerRender on every enabled skinned renderer around <paramref name="a"/> (each flag put back).</summary>
    static void XForced(Action a)
    {
        var smrs = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(s => s && s.enabled).ToArray();
        var saved = smrs.Select(s => s.forceMatrixRecalculationPerRender).ToArray();
        try { foreach (var s in smrs) s.forceMatrixRecalculationPerRender = true; a(); }
        finally { for (int i = 0; i < smrs.Length; i++) if (smrs[i]) smrs[i].forceMatrixRecalculationPerRender = saved[i]; }
    }

    static Jr6Moment XJr6Read(AppCore app, Jr6Fix fix, bool wide, CaptureInfo info)
    {
        var m = new Jr6Moment { Path = info.Path, Centre = fix.Centre, W = info.Width, H = info.Height };
        var f = MosaicCamera.Last;
        m.CaptureFrame = f != null && f.Camera && f.Camera.name == "hs2next_capture_camera";
        if (m.CaptureFrame)
        {
            m.Floor = f.FloorPx;
            foreach (var g in f.Groups)
                foreach (var mm in g.Members)
                {
                    m.Drawn++;
                    if (mm.BlockPx < f.FloorPx) m.Under++;
                    m.LeastBlock = Mathf.Min(m.LeastBlock, mm.BlockPx);
                }
        }
        if (wide) m.Groups = new List<MosaicWideGroup>(MosaicWideFeature.LastGroups);
        var view = app.Runtime.GetPart<SceneViewCamera>().Camera;
        if (!xJr6Cam) xJr6Cam = MosaicTest.Jr6Camera();
        xJr6Cam.transform.SetPositionAndRotation(view.transform.position, view.transform.rotation);
        xJr6Cam.fieldOfView = view.fieldOfView;
        xJr6Cam.nearClipPlane = view.nearClipPlane;
        xJr6Cam.farClipPlane = view.farClipPlane;
        XForced(() => m.Fp = MosaicTest.Jr6Footprint(xJr6Cam, info.Width, info.Height, fix.Ctx));
        m.Fpn = m.Fp.Count(x => x);
        string aa = info.Kind == CaptureKind.Capture ? CaptureService.None : CaptureService.Smaa;
        if (!wide) MosaicTest.Jr6WithoutMosaic(fix.Ctx, fix.Marker, () => m.Control = XForcedRender(app, info.Width, info.Height, aa, false));
        else
        {
            MosaicWide.DebugView = true;
            try { m.Cover = XForcedRender(app, info.Width, info.Height, aa, false); }
            finally { MosaicWide.DebugView = false; }
            MosaicWide.Enabled = false;
            try { m.Off = XForcedRender(app, info.Width, info.Height, aa, false); }
            finally { MosaicWide.Enabled = true; }
        }
        return m;
    }

    static bool XMarkerPx(Color32 c) => MosaicTest.Jr6IsMarker(new Color(CaptureService.SrgbToLinear(c.r), CaptureService.SrgbToLinear(c.g), CaptureService.SrgbToLinear(c.b), 1f));

    static void XJr6Judge(Jr6Moment m, Color32[] cap, bool wide, Jr6Tally t, int i)
    {
        if (m == null || cap == null || m.Fp == null || cap.Length != m.Fp.Length) { t.Missing++; t.Lines.Add($"m{i:00}: no picture or no reading"); return; }
        if (t.First == null) t.First = m.Centre;
        t.Moved = Mathf.Max(t.Moved, (m.Centre - t.First.Value).magnitude);
        string view = m.View >= 0 ? $"view {Jr6ViewNames[m.View]} (chosen by a footprint of {m.ChoiceFp} px)" : "view ?";
        if (m.Fpn < Jr6LeastFootprint) { t.NotJudged++; t.Lines.Add($"m{i:00}: {view}; footprint {m.Fpn} px, not judged"); return; }
        t.Judged++;
        t.LeastFp = Math.Min(t.LeastFp, m.Fpn);
        int w = m.W, h = m.H;
        if (!wide)
        {
            int mk = 0, ctrl = 0;
            for (int k = 0; k < cap.Length; k++)
                if (m.Fp[k])
                {
                    if (XMarkerPx(cap[k])) mk++;
                    if (m.Control != null && XMarkerPx(m.Control[k])) ctrl++;
                }
            t.Marks += mk;
            if (ctrl == 0) t.NoCtrl++;
            t.LeastCtrl = Math.Min(t.LeastCtrl, ctrl);
            if (!m.CaptureFrame || m.Drawn == 0) t.NoBlock++;
            t.Under += m.Under;
            if (m.Drawn > 0) t.LeastBlockRatio = Mathf.Min(t.LeastBlockRatio, m.LeastBlock / Mathf.Max(m.Floor, 1e-6f));
            t.Lines.Add($"m{i:00}: {view}; footprint {m.Fpn} px, markers {mk}, left out {ctrl}; F {m.Floor:F1}, least block {(m.Drawn > 0 ? m.LeastBlock : 0f):F1}, members {m.Drawn}, at {m.Centre.x:F2}, {m.Centre.y:F2}, {m.Centre.z:F2}");
            return;
        }
        if (m.Groups == null || m.Groups.Count == 0 || m.Cover == null || m.Off == null) { t.NoGroup++; t.Lines.Add($"m{i:00}: {view}; footprint {m.Fpn} px, the wide pass did not run in the capture"); return; }
        var g = m.Groups[0];
        float B = g.Block;
        int ir = Mathf.CeilToInt(1.5f * B), miss = 0;
        float r2 = 1.5f * B * 1.5f * B;
        var seen = m.Cover.Select(c => c.r > 127).ToArray();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!m.Fp[y * w + x]) continue;
                for (int dy = -ir; dy <= ir; dy++)
                    for (int dx = -ir; dx <= ir; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= w || yy >= h || dx * dx + dy * dy > r2) continue;
                        if (!seen[yy * w + xx]) { miss++; seen[yy * w + xx] = true; }
                    }
            }
        var sets = new Dictionary<long, HashSet<int>>();
        int changed = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int k = y * w + x;
                var a = cap[k];
                var o = m.Off[k];
                if (a.r == o.r && a.g == o.g && a.b == o.b) continue;
                changed++;
                long kx = (long)Mathf.Floor((x + 0.5f - g.Anchor.x) / B), ky = (long)Mathf.Floor((y + 0.5f - g.Anchor.y) / B);
                long key = kx * 1000000 + ky;
                if (!sets.TryGetValue(key, out var set)) sets[key] = set = new HashSet<int>();
                set.Add(a.r << 16 | a.g << 8 | a.b);
            }
        int mixed = sets.Values.Count(s => s.Count > 1);
        t.Uncovered += miss; t.Blocks += sets.Count; t.Mixed += mixed; t.LeastB = Mathf.Min(t.LeastB, B);
        t.Lines.Add($"m{i:00}: {view}; footprint {m.Fpn} px, block {B:F1} px, uncovered {miss} px, changed {changed} px, {sets.Count} blocks, {mixed} mixed, at {m.Centre.x:F2}, {m.Centre.y:F2}, {m.Centre.z:F2}");
    }

    static void XJr6Verdict(Jr6Fix fix, bool wide, string path, Jr6Tally t)
    {
        string what = $"JR6 {fix.Label} {(wide ? "(b)" : "(a)")} {path}";
        if (!wide)
        {
            Check($"{what}: 0 marker pixels in each moment's footprint, the marker shows there with the mosaic left out ({t.Judged} judged, {t.NotJudged} not judged, at least {Jr6LeastJudged})",
                t.Judged >= Jr6LeastJudged && t.Marks == 0 && t.NoCtrl == 0 && t.Missing == 0,
                $"{t.Marks} marker pixels, {t.NoCtrl} moments with no marker left out (least {(t.Judged > 0 ? t.LeastCtrl : 0)}), {t.Missing} missing, least footprint {(t.Judged > 0 ? t.LeastFp : 0)} px");
            Check($"{what}: every drawn member's block >= the capture's F at each judged moment", t.Judged > 0 && t.Under == 0 && t.NoBlock == 0,
                $"{t.Under} under, {t.NoBlock} moments without the capture's frame or a member, least block / F {(t.LeastBlockRatio < float.MaxValue ? t.LeastBlockRatio : 0):F2}");
        }
        else
            Check($"{what}: the coverage holds each moment's footprint dilated by 1.5 blocks, the changed pixels form flat blocks ({t.Judged} judged, {t.NotJudged} not judged, at least {Jr6LeastJudged})",
                t.Judged >= Jr6LeastJudged && t.Uncovered == 0 && t.Mixed == 0 && t.Blocks > 0 && t.NoGroup == 0 && t.Missing == 0,
                $"uncovered {t.Uncovered} px, {t.Blocks} blocks, {t.Mixed} mixed, {t.NoGroup} moments without the wide pass, {t.Missing} missing, least block {(t.LeastB < float.MaxValue ? t.LeastB : 0):F1} px");
        Check($"{what}: Present: the member moved at least {Jr6LeastMoved:F0} units over the walk", t.Moved >= Jr6LeastMoved, $"{t.Moved:F2} units");
        foreach (var l in t.Lines) Info(what + " " + l);
    }

    // ============================================================ YT1-YT4: thumbnails under the skin scope (Phase 6.10)

    const int YtMoments = 24, YtFrontFloor = 20000, YtStepFloor = 30000, YtLeastPresent = 3;
    const float YtDt = 1f / 30f, YtLeastMotion = 1f;
    const string YtThumbCamera = "hs2next_thumbnail_camera";

    static IEnumerator Play_S_Thumbs()
    {
        if (!WantAny("YT1", "YT2", "YT3", "YT4")) yield break;
        current = "S_Thumbs";
        if (!Check("S: a graphics device", SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, SystemInfo.graphicsDeviceType.ToString())) yield break;
        yield return PStartApp();
        var app = AppCore.Current;
        if (!Check("S: the app runs with its bridge and runtime", app != null && app.Stage == AppStage.Ready && app.Bridge != null && app.Runtime != null && app.Runtime.Ops.ThumbnailRenderer != null, app?.StageName ?? "none")) yield break;
        var inner = app.Runtime.Ops.ThumbnailRenderer;
        RenderPipelineManager.beginCameraRendering += SYtOnBegin;
        try
        {
            if (POpen("YT1", "a thumbnail of a moving character equals a render with forced recalculation (stepped scene, command's point, Front, View)")) yield return SYt1(app);
            if (POpen("YT2", "each renderer's own flag comes back after a thumbnail")) yield return SYt2(app);
            if (POpen("YT4", "a still scene's thumbnails as before; the Front framing sees the character")) yield return SYt4(app);
            if (POpen("YT3", "the mosaic rule on a moving member through the scene thumbnail")) yield return SYt3(app);
        }
        finally
        {
            RenderPipelineManager.beginCameraRendering -= SYtOnBegin;
            sYtArmed = false;
            CaptureService.RecalculateSkin = true;
            if (app.Runtime != null && app.Runtime.Ops.ThumbnailRenderer != inner) app.Runtime.Ops.ThumbnailRenderer = inner;
            MosaicWide.DebugView = false;
        }
        yield return PStopApp();
    }

    // ------------------------------------------------------------ the thumbnail paths in memory

    /// <summary><paramref name="f"/> with CaptureService.RecalculateSkin false (the test hook: the scope does nothing), the flag put back.</summary>
    static T SHookOff<T>(Func<T> f)
    {
        bool was = CaptureService.RecalculateSkin;
        CaptureService.RecalculateSkin = false;
        try { return f(); }
        finally { CaptureService.RecalculateSkin = was; }
    }

    /// <summary><paramref name="f"/> with forceMatrixRecalculationPerRender on every enabled skinned renderer (each flag put back).</summary>
    static T SForced<T>(Func<T> f)
    {
        T r = default;
        XForced(() => r = f());
        return r;
    }

    /// <summary>The scene thumbnail as the app renders it (SceneOps.ThumbnailRenderer), decoded in memory.</summary>
    static Color32[] SScene(AppCore app) => XDecode(app.Runtime.Ops.ThumbnailRenderer());

    /// <summary>A character's card thumbnail as a save renders it (StudioOps.RenderCharaThumbnail), its pixels; the texture destroyed.</summary>
    static Color32[] SCard(AppCore app, string key, CharaThumbnailFraming framing)
    {
        var r = app.Ops.RenderCharaThumbnail(key, framing);
        var t = (r.Value as CharaThumbnailRender)?.Texture;
        if (!t) return null;
        try { return t.GetPixels32(); }
        finally { Object.Destroy(t); }
    }

    static Vector3 SHand(AppCore app, string key)
    {
        var ctx = app.Runtime.Charas().FirstOrDefault(c => c.NodeId == key)?.Context;
        return ctx?.BodyBones != null && ctx.BodyBones.TryGetValue("cf_J_Hand_R", out var t) && t ? t.position : Vector3.zero;
    }

    sealed class SYtMoment
    {
        public int Frame, App = -1, AppMost, Ctrl = -1, CtrlMost;
        public Vector3 Hand;
        /// <summary>The stepped path: the step was taken (SceneRuntime.Step steps a paused clock only) and the right hand's travel over it.</summary>
        public bool Stepped;
        public float Travel;
    }

    /// <summary>One moment of a path in this engine frame: the app's render, its hook-off control and the forced reference.</summary>
    static SYtMoment SYt1Moment(AppCore app, string key, string path)
    {
        var m = new SYtMoment { Frame = Time.frameCount };
        Func<Color32[]> shot = path == "front" ? () => SCard(app, key, CharaThumbnailFraming.Front)
                             : path == "view" ? () => SCard(app, key, CharaThumbnailFraming.View)
                             : (Func<Color32[]>)(() => SScene(app));
        SHookOff(() => SScene(app));   // the frame's draw
        if (path == "stepped")
        {
            var h0 = SHand(app, key);
            m.Stepped = app.Runtime.Step(1, YtDt);
            m.Travel = (SHand(app, key) - h0).magnitude;
        }
        var ctrl = SHookOff(shot);
        var got = shot();
        var reference = SForced(shot);
        m.App = XDiff(got, reference, out m.AppMost);
        m.Ctrl = XDiff(ctrl, reference, out m.CtrlMost);
        m.Hand = SHand(app, key);
        return m;
    }

    /// <summary>BR-Chan dancing on a new scene's own view (no camera.set; Step 0's thumbstep): the stepped path's fixture.</summary>
    static IEnumerator SYtDancerOwnView(AppCore app, string[] key)
    {
        key[0] = null;
        yield return PCall(app, new { cmd = "scene.new" });
        yield return PCall(app, new { cmd = "chara.load", path = femaleRel, sex = "female" });
        string k = Key(pLast);
        if (!Check("YT1 stepped: BR-Chan loads", POk && k != null, Short(pLastText))) yield break;
        yield return PCall(app, new { cmd = "chara.anime", key = k, group = 11, category = 5, no = 0, speed = 1f, loop = true });
        if (!Check("YT1 stepped: dance_00 plays", POk, Short(pLastText))) yield break;
        yield return PFrames(23);
        key[0] = k;
    }

    static IEnumerator SYt1(AppCore app)
    {
        var key = new string[1];
        xHandlerErrors.Clear();
        string saved = null;
        foreach (var path in new[] { "stepped", "command", "front", "view" })
        {
            // the stepped path on a new scene's own view, the others on JR1's view of the whole figure (the lead's ruling after 20261109-0013)
            if (path == "stepped") yield return SYtDancerOwnView(app, key);
            else if (path == "command") yield return XDancer(app, key);
            if (key[0] == null) yield break;
            var ms = new List<SYtMoment>();
            var inner = app.Runtime.Ops.ThumbnailRenderer;
            if (path == "stepped")
            {
                // a step steps a paused clock only: the dance advances by each moment's step (the lead's ruling after 20261108-2349)
                yield return PCall(app, new { cmd = "scene.pause", paused = true });
                if (!Check("YT1 stepped: the scene clock paused for the steps", POk, Short(pLastText))) continue;
                yield return PFrames(3);
            }
            for (int i = 0; i < YtMoments; i++)
            {
                if (path == "command")
                {
                    // the scene thumbnail at a command's point: the save's own render, read in place
                    SYtMoment cur = null;
                    app.Runtime.Ops.ThumbnailRenderer = () =>
                    {
                        var png = inner();
                        try
                        {
                            var c = new SYtMoment { Frame = Time.frameCount };
                            var got = XDecode(png);
                            var ctrl = SHookOff(() => XDecode(inner()));
                            var reference = SForced(() => XDecode(inner()));
                            c.App = XDiff(got, reference, out c.AppMost);
                            c.Ctrl = XDiff(ctrl, reference, out c.CtrlMost);
                            c.Hand = SHand(app, key[0]);
                            cur = c;
                        }
                        catch (Exception e) { xHandlerErrors.Add(e.GetType().Name + ": " + e.Message); }
                        return png;
                    };
                    try { yield return PCall(app, new { cmd = "scene.save", name = "s_yt1", overwrite = true, thumb = "render" }); }
                    finally { app.Runtime.Ops.ThumbnailRenderer = inner; }
                    if (POk && (string)pLast?["thumbnail"] == "render") saved = (string)pLast["path"];
                    else Info("YT1 command: scene.save " + Short(pLastText));
                    ms.Add(cur);
                }
                else ms.Add(SYt1Moment(app, key[0], path));
                yield return PFrames(5);
            }
            if (path == "stepped") yield return PCall(app, new { cmd = "scene.pause", paused = false });
            bool stepPath = path == "stepped";
            int bad = ms.Count(m => m == null || m.App != 0 || (stepPath && !m.Stepped));
            var lines = ms.Select((m, i) => m == null ? $"m{i:00}: no reading" : $"m{i:00} frame {m.Frame}: thumbnail {m.App} px (most {m.AppMost}), hook off {m.Ctrl} px (most {m.CtrlMost})" +
                (stepPath ? (m.Stepped ? $", the step taken, the right hand moved {m.Travel:F4} units over it" : ", the step refused (missing)") : "")).ToList();
            Check($"YT1 {path}: each thumbnail equals the forced render of its moment (0 px over {XLevels} levels; {ms.Count(m => m != null && (!stepPath || m.Stepped))} of {YtMoments} moments{(stepPath ? ", each after its step" : "")})",
                ms.Count == YtMoments && bad == 0, $"{bad} differ or missing; " + string.Join("; ", lines.Take(6)));
            var read = ms.Where(m => m != null).ToList();
            if (path == "stepped" || path == "front")
            {
                int floor = path == "stepped" ? YtStepFloor : YtFrontFloor;
                int over = read.Count(m => m.Ctrl >= floor);
                Check($"YT1 {path}: Present: the hook-off control differs from the forced render on at least {floor:N0} px at {YtLeastPresent} moments or more", over >= YtLeastPresent,
                    $"{over} of {read.Count}; least {(read.Count > 0 ? read.Min(m => m.Ctrl) : 0):N0}, most {(read.Count > 0 ? read.Max(m => m.Ctrl) : 0):N0} px");
            }
            else
            {
                float moved = read.Count > 1 ? read.Max(m => (m.Hand - read[0].Hand).magnitude) : 0f;
                Check($"YT1 {path}: Present: the right hand moved at least {YtLeastMotion:F0} unit between the moments", moved >= YtLeastMotion,
                    $"{moved:F2} units; hook off {string.Join(", ", read.Select(m => m.Ctrl))} px");
            }
            foreach (var l in lines) Info($"YT1 {path} {l}");
            Measure($"YT1 {path}: thumbnail {string.Join("/", read.Select(m => m.App))} px; hook off {string.Join("/", read.Select(m => m.Ctrl))} px");
        }
        if (saved != null && File.Exists(saved)) File.Delete(saved);
        if (xHandlerErrors.Count > 0) Check("YT1: the control and reference renders ran", false, string.Join("; ", xHandlerErrors.Take(3)));
        Info("YT1: skinned renderers switched on by the last thumbnail's scope: " + CaptureService.LastSkinScopeCount);
    }

    // ------------------------------------------------------------ YT2

    static IEnumerator SYt2(AppCore app)
    {
        var key = new string[1];
        yield return XDancer(app, key);
        if (key[0] == null) yield break;
        var smrs = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(s => s && s.enabled).ToArray();
        if (!Check("YT2: enabled skinned renderers in the scene", smrs.Length > 1, smrs.Length + "")) yield break;
        var chosen = smrs.FirstOrDefault(s => !s.forceMatrixRecalculationPerRender) ?? smrs[0];
        bool chosenWas = chosen.forceMatrixRecalculationPerRender;
        chosen.forceMatrixRecalculationPerRender = true;
        try
        {
            var before = smrs.Select(s => s.forceMatrixRecalculationPerRender).ToArray();
            int off = before.Count(b => !b);
            foreach (var (what, run) in new (string, Func<Color32[]>)[]
            {
                ("scene thumbnail", () => SScene(app)),
                ("Front card thumbnail", () => SCard(app, key[0], CharaThumbnailFraming.Front)),
                ("View card thumbnail", () => SCard(app, key[0], CharaThumbnailFraming.View)),
            })
            {
                var px = run();
                var after = smrs.Select(s => s && s.forceMatrixRecalculationPerRender).ToArray();
                int changed = Enumerable.Range(0, smrs.Length).Count(i => after[i] != before[i]);
                Check($"YT2 {what}: every renderer's flag equals its flag before ({smrs.Length} renderers, {off} off, {smrs.Length - off} on)", px != null && changed == 0, $"{changed} changed");
                Check($"YT2 {what}: Present: the renderer that had it on keeps it ({chosen.name})", chosen && chosen.forceMatrixRecalculationPerRender, "");
                // the Front framing's default pose may release option items (their renderers) before the scope opens: its count is printed
                if (what.StartsWith("Front", StringComparison.Ordinal))
                    Info($"YT2 {what}: the scope switched on {CaptureService.LastSkinScopeCount} renderers ({off} enabled renderers had it off before the default pose)");
                else
                    Check($"YT2 {what}: the scope switched on every enabled renderer that had it off", CaptureService.LastSkinScopeCount == off, $"{CaptureService.LastSkinScopeCount} switched, {off} off before");
                yield return PFrames(2);
            }
        }
        finally { if (chosen) chosen.forceMatrixRecalculationPerRender = chosenWas; }
    }

    // ------------------------------------------------------------ YT4

    static IEnumerator SYt4(AppCore app)
    {
        var key = new string[1];
        yield return XDancer(app, key);
        if (key[0] == null) yield break;
        yield return PCall(app, new { cmd = "scene.pause", paused = true });
        if (!Check("YT4: the scene paused", POk, Short(pLastText))) yield break;
        yield return PFrames(10);
        var real = app.Runtime.FindChara(key[0]);
        var view = app.Runtime.GetPart<SceneViewCamera>()?.Camera;
        if (!Check("YT4: BR-Chan built and the view camera", real != null && view, "")) yield break;
        var h0 = SHand(app, key[0]);
        // one engine frame: the frame's draw, then each path with the test hook off and with the scope
        SHookOff(() => SScene(app));
        var sceneOff = SHookOff(() => app.Runtime.Ops.ThumbnailRenderer());
        var sceneOn = app.Runtime.Ops.ThumbnailRenderer();
        var viewOff = SHookOff(() => CharaThumbnail.CaptureView(real, view, app.Ops.ThumbnailRenderSettings));
        var viewOn = CharaThumbnail.CaptureView(real, view, app.Ops.ThumbnailRenderSettings);
        SHookOff(() => SScene(app));
        var frontOff = SHookOff(() => SCard(app, key[0], CharaThumbnailFraming.Front));
        var frontOn = SCard(app, key[0], CharaThumbnailFraming.Front);
        int sceneDiff = XDiff(XDecode(sceneOff), XDecode(sceneOn), out _), viewDiff = XDiff(XDecode(viewOff), XDecode(viewOn), out _);
        int front = XDiff(frontOff, frontOn, out int frontMost);
        Check("YT4: the scene thumbnail of a paused scene: the same bytes with the scope and with the test hook off", sceneOn != null && sceneOff != null && sceneOn.SequenceEqual(sceneOff),
            $"{sceneOn?.Length ?? 0} / {sceneOff?.Length ?? 0} bytes, {sceneDiff} px over {XLevels} levels");
        Check("YT4: the View card thumbnail of the paused BR-Chan: the same bytes with the scope and with the test hook off", viewOn != null && viewOff != null && viewOn.SequenceEqual(viewOff),
            $"{viewOn?.Length ?? 0} / {viewOff?.Length ?? 0} bytes, {viewDiff} px over {XLevels} levels");
        Check($"YT4: Present: the Front card thumbnail of the same paused character differs on at least {YtFrontFloor:N0} px", front >= YtFrontFloor, $"{front:N0} px (most {frontMost})");
        Check("YT4: the character stood still (the right hand within 1e-4 units)", (SHand(app, key[0]) - h0).magnitude <= 1e-4f, $"{(SHand(app, key[0]) - h0).magnitude:G3}");
        Measure($"YT4: scene {sceneDiff} px, View {viewDiff} px, Front {front:N0} px (scope against hook off, paused)");
        yield return PCall(app, new { cmd = "scene.pause", paused = false });
    }

    // ------------------------------------------------------------ YT3

    /// <summary>The thumbnail camera's mosaic step read at its begin (the last of its renders wins); armed by YT3.</summary>
    static bool sYtArmed, sYtSeen;
    static float sYtFloor, sYtLeast;
    static int sYtDrawn, sYtUnder;

    static void SYtOnBegin(ScriptableRenderContext c, Camera cam)
    {
        if (!sYtArmed || !cam || cam.name != YtThumbCamera) return;
        var f = MosaicCamera.FrameOf(cam);
        if (f == null) return;
        sYtSeen = true;
        sYtFloor = f.FloorPx;
        sYtDrawn = 0; sYtUnder = 0; sYtLeast = float.MaxValue;
        foreach (var g in f.Groups)
            foreach (var mm in g.Members)
            {
                sYtDrawn++;
                if (mm.BlockPx < f.FloorPx) sYtUnder++;
                sYtLeast = Mathf.Min(sYtLeast, mm.BlockPx);
            }
    }

    static IEnumerator SYt3(AppCore app)
    {
        bool wide0 = MosaicWide.Enabled;
        bool mover = app.Bridge.Server.Commands.Contains("obj.moveTo");
        int w = HS2Next.Save.ThumbnailCapture.Width, h = HS2Next.Save.ThumbnailCapture.Height;
        Info($"YT3: the walk {(mover ? "by obj.moveTo" : "by obj.transform steps")}, {Jr6Walk} units in {Jr6Seconds} s, dance_00 playing; {YtMoments} moments a walk through the scene thumbnail, {w} x {h}");
        try
        {
            foreach (var (label, rel, sex) in new[] { ("BR-Chan", femaleRel, "female"), ("HERO107", maleRel, "male") })
            {
                var box = new Jr6Fix[1];
                yield return XJr6Fixture(app, label, rel, sex, box);
                var fix = box[0];
                if (fix == null) continue;
                foreach (bool wide in new[] { false, true })
                {
                    MosaicWide.Enabled = wide;
                    if (!fix.Male) MosaicTest.Jr6UseComposite(fix.Ctx, wide ? fix.Gradient : fix.Marker);
                    var t = new Jr6Tally();
                    yield return SYt3Walk(app, fix, wide, mover, w, h, t);
                }
                foreach (var (m, tex) in fix.Saved) if (m) m.SetTexture("_MainTex", tex);
            }
        }
        finally
        {
            MosaicWide.Enabled = wide0;
            MosaicWide.DebugView = false;
            sYtArmed = false;
        }
        yield return PCall(app, new { cmd = "scene.new" });
        yield return PFrames(3);
        MosaicTest.Jr6Cleanup();
        xJr6Cam = null;
    }

    static IEnumerator SYt3Walk(AppCore app, Jr6Fix fix, bool wide, bool mover, int w, int h, Jr6Tally t)
    {
        string what = $"YT3 {fix.Label} {(wide ? "(b)" : "(a)")} scene thumbnail";
        yield return PCall(app, new { cmd = "obj.transform", key = fix.Key, pos = new[] { -Jr6Walk / 2f, 0f, Jr6Line }, rot = new[] { 0f, 90f, 0f }, undo = false });
        yield return PFrames(3);
        var moments = new List<(Jr6Moment m, Color32[] px)>();
        xHandlerErrors.Clear();
        if (mover) yield return PCall(app, new { cmd = "obj.moveTo", key = fix.Key, target = new[] { Jr6Walk / 2f, 0f, Jr6Line }, stop = 0f, time = Jr6Seconds });
        for (int i = 0; i < YtMoments; i++)
        {
            if (!mover) yield return PCall(app, XJr6Step(fix, (i + 1) / (float)YtMoments));
            var pick = XJr6Choose(app, fix, w, h);   // in the frame before the thumbnail, by the footprint alone
            yield return PCall(app, pick.aim);
            // the moment, in this engine frame: the scene thumbnail, then its readings
            Jr6Moment m = null;
            Color32[] px = null;
            try
            {
                sYtArmed = true; sYtSeen = false;
                var groups = new List<MosaicWideGroup>();
                try
                {
                    px = SScene(app);
                    if (wide) groups.AddRange(MosaicWideFeature.LastGroups);
                }
                finally { sYtArmed = false; }
                var view = app.Runtime.GetPart<SceneViewCamera>().Camera;
                m = new Jr6Moment { Path = "thumb" + i, Centre = fix.Centre, W = w, H = h, CaptureFrame = sYtSeen, Floor = sYtFloor, Drawn = sYtDrawn, Under = sYtUnder, LeastBlock = sYtLeast, View = pick.view, ChoiceFp = pick.fp };
                if (wide) m.Groups = groups;
                if (!xJr6Cam) xJr6Cam = MosaicTest.Jr6Camera();
                xJr6Cam.transform.SetPositionAndRotation(view.transform.position, view.transform.rotation);
                xJr6Cam.fieldOfView = view.fieldOfView;
                xJr6Cam.nearClipPlane = view.nearClipPlane;
                xJr6Cam.farClipPlane = view.farClipPlane;
                XForced(() => m.Fp = MosaicTest.Jr6Footprint(xJr6Cam, w, h, fix.Ctx));
                m.Fpn = m.Fp.Count(x => x);
                if (!wide) MosaicTest.Jr6WithoutMosaic(fix.Ctx, fix.Marker, () => m.Control = SForced(() => SScene(app)));
                else
                {
                    MosaicWide.DebugView = true;
                    try { m.Cover = SForced(() => SScene(app)); }
                    finally { MosaicWide.DebugView = false; }
                    MosaicWide.Enabled = false;
                    try { m.Off = SForced(() => SScene(app)); }
                    finally { MosaicWide.Enabled = true; }
                }
            }
            catch (Exception e) { xHandlerErrors.Add(e.GetType().Name + ": " + e.Message); }
            moments.Add((m, px));
            yield return PFrames(2);
        }
        // the footprints first: with fewer than 20 moments over 150 px nothing is judged
        int over = moments.Count(x => x.m != null && x.m.Fpn >= Jr6LeastFootprint);
        string fps = string.Join(", ", moments.Select(x => x.m?.Fpn ?? -1));
        if (!Check($"{what}: Present: at least {Jr6LeastJudged} moments with a footprint of {Jr6LeastFootprint} px or more (else nothing is judged)", over >= Jr6LeastJudged, $"{over} of {moments.Count}; footprints {fps}"))
            yield break;
        for (int i = 0; i < moments.Count; i++) XJr6Judge(moments[i].m, moments[i].px, wide, t, i);
        if (!wide)
        {
            Check($"{what}: 0 marker pixels in each moment's footprint, the marker shows there with the mosaic left out ({t.Judged} judged, {t.NotJudged} not judged, at least {Jr6LeastJudged})",
                t.Judged >= Jr6LeastJudged && t.Marks == 0 && t.NoCtrl == 0 && t.Missing == 0,
                $"{t.Marks} marker pixels, {t.NoCtrl} moments with no marker left out (least {(t.Judged > 0 ? t.LeastCtrl : 0)}), {t.Missing} missing, least footprint {(t.Judged > 0 ? t.LeastFp : 0)} px");
            Check($"{what}: every drawn member's block >= the thumbnail's F at each judged moment", t.Judged > 0 && t.Under == 0 && t.NoBlock == 0,
                $"{t.Under} under, {t.NoBlock} moments without the thumbnail camera's step or a member, least block / F {(t.LeastBlockRatio < float.MaxValue ? t.LeastBlockRatio : 0):F2}");
        }
        else
            Check($"{what}: the coverage holds each moment's footprint dilated by 1.5 blocks, the changed pixels form flat blocks ({t.Judged} judged, {t.NotJudged} not judged, at least {Jr6LeastJudged})",
                t.Judged >= Jr6LeastJudged && t.Uncovered == 0 && t.Mixed == 0 && t.Blocks > 0 && t.NoGroup == 0 && t.Missing == 0,
                $"uncovered {t.Uncovered} px, {t.Blocks} blocks, {t.Mixed} mixed, {t.NoGroup} moments without the wide pass, {t.Missing} missing, least block {(t.LeastB < float.MaxValue ? t.LeastB : 0):F1} px");
        Check($"{what}: Present: the member moved at least {Jr6LeastMoved:F0} units over the walk", t.Moved >= Jr6LeastMoved, $"{t.Moved:F2} units");
        if (xHandlerErrors.Count > 0) Check($"{what}: the moments' renders ran", false, string.Join("; ", xHandlerErrors.Take(3)));
        foreach (var l in t.Lines) Info(what + " " + l);
    }
}

/// <summary>MosaicTest's fixture of the moving-member rows (WV3m, RV3), reached by ScriptTest's JR6 (Phase 6.9); MosaicTest's own groups do not use these.</summary>
public static partial class MosaicTest
{
    internal static void Jr6HideClothes(CharaContext ctx) => HideClothes(ctx);
    internal static MosaicMember Jr6Member(CharaContext ctx, string name) => Member(ctx, name);
    internal static Texture2D Jr6MarkerComposite(CharaContext ctx) => Composite(ctx, 2048, 6, Cells(3), Skin);
    internal static Texture2D Jr6GradientComposite(CharaContext ctx) => Composite(ctx, 2048, 0, Gradient(2048), Skin);
    internal static Texture2D Jr6Checker() => Wv3mChecker(2048, 5);
    internal static void Jr6UseComposite(CharaContext ctx, Texture t) => UseComposite(ctx, t);
    internal static void Jr6WithoutMosaic(CharaContext ctx, Texture t, Action a) => WithoutMosaic(ctx, t, a);
    internal static Camera Jr6Camera() => NewCam(false, UnityEngine.Rendering.Universal.AntialiasingMode.None);
    internal static bool[] Jr6Footprint(Camera cam, int w, int h, CharaContext ctx) => Erode(Footprint(cam, w, h, ctx), w, h, 1);
    internal static bool Jr6IsMarker(Color c) => IsMarker(c);
    internal static void Jr6Cleanup() => Cleanup();
}
