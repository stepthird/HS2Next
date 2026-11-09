// SceneRuntimeTest.RunRender: edit-mode batch run with a
// graphics device (no -nographics), ManualStepping, the runtime driven by Tick.
//   V1-V4  view camera and chara light: the default view, `scene.camera.current` (not recorded), the chara
//          light under the camera, an active camera node overriding the view and the view returning to `current`
//   S1     scene thumbnail of the fixture through the view camera: 504 × 704 RGB PNG
//   C1-C3  CharaThumbnail.Capture of BR-Chan at (10, 0, 5) rot (0, 90, 0) while Idle_00 plays: PNG produced; after the
//          scope the animation state (Save() fields), face, look, hands and physics switches equal those before; the
//          isolation layer was free; the other character's renderers kept their layers; then the same with attack_01
//          (rifle option item): option items re-created and all URP; C3: both images byte-identical (the default pose
//          does not depend on the motion)
//   C4     no free isolation layer → null and `thumbnail.noFreeLayer`
// Fixture: the nine-node fixture of SceneRuntimeTest.cs (BuildFixture; BR-Chan, HERO107, items A-C, light, folder F
// with camera K and route R) plus the parts of Package C. The project has no default render pipeline, so the run
// uses Assets/HS2Next/Settings/Measure/MeasureURP.asset like the other render tests (synchronous shader compilation).
// Images are not judged: sizes, headers and non-background pixel fractions only.
// Report: Spike/out/scene/scene_runtime_render_report.txt, first line "N PASS, M FAIL"; exit 1 on any FAIL.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Content;
using HS2Next.Motion.DynamicBones;
using HS2Next.Motion.Face;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static partial class SceneRuntimeTest
{
    static string RenderReportPath => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "scene", "scene_runtime_render_report.txt");

    /// <summary>Batch entry with graphics: the render checks of the scene runtime.</summary>
    public static void RunRender()
    {
        int failed = RenderCase.Run();
        if (failed > 0) EditorApplication.Exit(1);
    }

    static class RenderCase
    {
        static readonly StringBuilder body = new StringBuilder();
        static int pass, fail;

        static void Check(string name, bool ok, string detail = "")
        {
            if (ok) pass++; else fail++;
            body.AppendLine($"{(ok ? "PASS" : "FAIL")} {name}{(string.IsNullOrEmpty(detail) ? "" : "  (" + detail + ")")}");
        }

        static void Line(string s) => body.AppendLine(s);

        static bool Near(Vector3 a, Vector3 b, float eps = 1e-4f) => (a - b).magnitude <= eps;

        public static int Run()
        {
            var total = Stopwatch.StartNew();
            Line($"graphics {SystemInfo.graphicsDeviceType}, Unity {Application.unityVersion}, play mode {Application.isPlaying}");
            Fixture f = null;
            RuntimeServices services = null;
            // The project has no default render pipeline: use the URP asset of the other render tests for the run.
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HS2Next.App.AppPipeline.MeasureAssetPath);
            var prevDefault = GraphicsSettings.defaultRenderPipeline;
            var prevQuality = QualitySettings.renderPipeline;
            bool prevAsync = ShaderUtil.allowAsyncCompilation;
            try
            {
                ShaderUtil.allowAsyncCompilation = false;
                GraphicsSettings.defaultRenderPipeline = urp;
                QualitySettings.renderPipeline = urp;
                Check($"URP asset {HS2Next.App.AppPipeline.MeasureAssetPath} loaded and active", urp != null && GraphicsSettings.currentRenderPipeline == urp);
                var sw = Stopwatch.StartNew();
                services = NewServices(null);
                Line($"catalog + Studio lists: {sw.ElapsedMilliseconds} ms");
                sw.Restart();
                f = BuildFixture(services);   // the nine-node fixture (SceneRuntimeTest.cs), ManualStepping
                var rt = f.Runtime;
                rt.AddStandardParts();   // map, view camera, chara light (BuildFixture adds them already: no-op)
                rt.Tick(0f);
                Line($"fixture: {sw.ElapsedMilliseconds} ms; " + string.Join("; ", f.Log));
                Body(f);
            }
            catch (Exception e) { Check("render test ran without an exception", false, e.ToString()); }
            finally
            {
                try { f?.Dispose(); } catch (Exception e) { Line("dispose threw: " + e.Message); }
                try { services?.Catalog?.UnloadAll(); } catch (Exception e) { Line("catalog unload threw: " + e.Message); }
                GraphicsSettings.defaultRenderPipeline = prevDefault;
                QualitySettings.renderPipeline = prevQuality;
                ShaderUtil.allowAsyncCompilation = prevAsync;
            }
            Line($"total {total.ElapsedMilliseconds} ms");
            var sb = new StringBuilder();
            sb.AppendLine($"{pass} PASS, {fail} FAIL");
            sb.AppendLine("SceneRuntimeTest.render");
            sb.Append(body);
            Directory.CreateDirectory(Path.GetDirectoryName(RenderReportPath));
            File.WriteAllText(RenderReportPath, sb.ToString(), new UTF8Encoding(false));
            Debug.Log($"SceneRuntimeTest.render: {pass} PASS, {fail} FAIL -> {RenderReportPath}");
            return fail;
        }

        static void Body(Fixture f)
        {
            var rt = f.Runtime;
            var session = f.Session;
            var view = rt.GetPart<SceneViewCamera>();
            var light = rt.GetPart<SceneCharaLight>();
            string bra = f.BrChan, hero = f.Hero, camK = f.CameraK;
            var braR = rt.FindChara(bra);
            var heroR = rt.FindChara(hero);
            Check("fixture built (BR-Chan, HERO107, item B at N_Hand_R, camera node K under folder F)",
                  f.Failures.Count == 0 && braR?.State == RealizationState.Built && heroR?.State == RealizationState.Built &&
                  rt.Find(f.ItemB)?.State == RealizationState.Built && rt.Find(camK)?.HasHolder == true,
                  $"{braR?.State}, {heroR?.State}, item B {rt.Find(f.ItemB)?.State}, camera {rt.Find(camK)?.State}; failures {f.Failures.Count}");
            if (braR?.State != RealizationState.Built || heroR?.State != RealizationState.Built || camK == null) return;
            var sw = Stopwatch.StartNew();

            // ---------------------------------------------------------------- V1 default view
            var cam = view.Camera;
            Check("V1: view camera on the runtime's View object with URP data; LookTargets.DefaultCamera follows it",
                  cam && cam.transform == rt.ViewRoot && cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() &&
                  HS2Next.Motion.Look.LookTargets.DefaultCamera == cam.transform);
            var q0 = Quaternion.Euler(15, 180, 0);   // HS2's reset view (Phase 3.3 E4)
            Check("V1: default view = HS2's reset view (pivot (0,10,0), rot (15,180,0), distance 25, fov 23) when current is null",
                  session.Model.Settings.Camera.Current == null && Near(cam.transform.position, new Vector3(0, 10, 0) + q0 * new Vector3(0, 0, -25)) &&
                  RotationCompare.Deg(cam.transform.rotation, q0) < 0.01f && Mathf.Abs(cam.fieldOfView - 23f) < 1e-4f,
                  $"pos {cam.transform.position}, rot {cam.transform.eulerAngles}, fov {cam.fieldOfView}");

            // ---------------------------------------------------------------- V2 current (not recorded)
            int undoBefore = session.Commands.UndoList.Count;
            var cv = new CameraView { Pivot = new Vec3(0, 12, 0), Rot = new Vec3(10, 200, 0), Distance = 25, Fov = 30 };
            var r2 = session.Set(null, "camera.current", cv, ExecuteOptions.NotRecorded);
            rt.Tick(0f);
            var q = Quaternion.Euler(10, 200, 0);
            Check("V2: view from camera.current: pivot + Euler(rot)·(0,0,-distance), rotation, fov; not recorded",
                  r2.Ok && Near(cam.transform.position, new Vector3(0, 12, 0) + q * new Vector3(0, 0, -25)) && RotationCompare.Deg(cam.transform.rotation, q) < 0.01f &&
                  Mathf.Abs(cam.fieldOfView - 30f) < 1e-4f && session.Commands.UndoList.Count == undoBefore,
                  $"{r2}; pos {cam.transform.position}, fov {cam.fieldOfView}, history {undoBefore} -> {session.Commands.UndoList.Count}");

            // ---------------------------------------------------------------- V3 chara light
            var r3a = session.Set(null, "charaLight.intensity", 0.7f);
            var r3b = session.Set(null, "charaLight.rot", new[] { 20f, -30f });
            var r3c = session.Set(null, "charaLight.color", new Color4(1f, 0.9f, 0.8f, 1f));
            var r3d = session.Set(null, "charaLight.shadow", false);
            rt.Tick(0f);
            var l = light.Light;
            Check("V3: chara light under the view camera with the stored values folded (colour × intensity, intensity 1; Phase 3.3)",
                  r3a.Ok && r3b.Ok && r3c.Ok && r3d.Ok && l && l.type == LightType.Directional && l.transform.parent == cam.transform &&
                  RotationCompare.Deg(l.transform.localRotation, Quaternion.Euler(20, -30, 0)) < 0.01f && Mathf.Abs(l.intensity - 1f) < 1e-6f &&
                  Mathf.Abs(l.color.g - 0.9f * 0.7f) < 1e-6f && l.shadows == LightShadows.None,
                  l ? $"rot {l.transform.localEulerAngles}, intensity {l.intensity}, colour {l.color}, shadows {l.shadows}" : "no light");
            session.Commands.Undo(); session.Commands.Undo(); session.Commands.Undo(); session.Commands.Undo();
            rt.Tick(0f);
            Check("V3: undo restores the default chara light (white, 1, (0,0), soft)",
                  l && Mathf.Abs(l.intensity - 1f) < 1e-6f && l.color == Color.white && l.shadows == LightShadows.Soft && RotationCompare.Deg(l.transform.localRotation, Quaternion.identity) < 0.01f);

            // ---------------------------------------------------------------- V4 active camera node
            session.Set(camK, "transform.pos", new Vec3(5, 12, 20));
            session.Set(camK, "transform.rot", new Vec3(0, 200, 0));
            session.Set(camK, "camera.fov", 40f);
            var r4 = session.SetActiveCamera(camK);
            rt.Tick(0f);
            var kh = rt.HolderOf(camK);
            Check("V4: an active camera node drives the view (holder world pose, its fov); current unchanged",
                  r4.Ok && view.ActiveCameraId == camK && kh && Near(cam.transform.position, kh.position) && RotationCompare.Deg(cam.transform.rotation, kh.rotation) < 0.01f &&
                  Mathf.Abs(cam.fieldOfView - 40f) < 1e-4f && session.Model.Settings.Camera.Current?.Fov == 30f,
                  $"{r4}; pos {cam.transform.position} vs {(kh ? kh.position.ToString() : "-")}, fov {cam.fieldOfView}");
            session.Set(camK, "transform.pos", new Vec3(6, 12, 20));
            rt.Tick(0f);
            Check("V4: the view follows the node when it moves", kh && Near(cam.transform.position, kh.position), $"{cam.transform.position}");
            var r4b = session.SetActiveCamera(null);
            rt.Tick(0f);
            Check("V4: no active node → the view returns to current", r4b.Ok && view.ActiveCameraId == null &&
                  Near(cam.transform.position, new Vector3(0, 12, 0) + q * new Vector3(0, 0, -25)) && Mathf.Abs(cam.fieldOfView - 30f) < 1e-4f,
                  $"{r4b}; pos {cam.transform.position}, fov {cam.fieldOfView}");

            // ---------------------------------------------------------------- S1 scene thumbnail
            session.Set(null, "camera.current", null, ExecuteOptions.NotRecorded);
            rt.Tick(0f);
            byte[] scenePng = null;
            sw.Restart();
            try { scenePng = ThumbnailCapture.CaptureView(cam); }
            catch (Exception e) { Line("scene thumbnail threw: " + e); }
            CheckPng("S1: scene thumbnail through the view camera", scenePng);
            if (scenePng != null)
            {
                float cov = Coverage(scenePng, new Color32(128, 128, 128, 255));
                Line($"S1: {scenePng.Length} bytes, {sw.ElapsedMilliseconds} ms, non-background pixels {cov:P1}");
                Check("S1: the fixture is in the default view (non-background pixels > 1 %)", cov > 0.01f, $"{cov:P1}");
            }
            byte[] opsPng = null;
            try { opsPng = rt.Ops.ThumbnailRenderer?.Invoke(); }
            catch (Exception e) { Line("SceneOps thumbnail renderer threw: " + e); }
            CheckPng("S1: SceneOps.ThumbnailRenderer (set by the view camera part)", opsPng);

            // ---------------------------------------------------------------- C1-C3 character thumbnail
            session.Set(bra, "transform.pos", new Vec3(10, 0, 5));
            session.Set(bra, "transform.rot", new Vec3(0, 90, 0));
            var c1 = CharaCapture(rt, session, braR, heroR, 19, 0, 0, "Idle_00", "C1");
            var c2 = CharaCapture(rt, session, braR, heroR, 15, 31, 1, "attack_01", "C2");
            Check("C3: the Studio default pose does not depend on the playing motion (C1 and C2 images byte-identical)",
                  c1 != null && c2 != null && c1.SequenceEqual(c2), $"{c1?.Length ?? 0} / {c2?.Length ?? 0} bytes");

            // ---------------------------------------------------------------- C4 no free layer
            var blockers = new List<GameObject>();
            for (int layer = CharaThumbnail.FirstIsolationLayer; layer <= CharaThumbnail.LastIsolationLayer; layer++)
            {
                var g = new GameObject("layer blocker " + layer) { layer = layer };
                g.transform.SetParent(rt.Root.transform, false);
                blockers.Add(g);
            }
            byte[] none;
            try { none = CharaThumbnail.Capture(braR, null, out _); }
            finally { foreach (var g in blockers) Object.DestroyImmediate(g); }
            Check("C4: no free layer in 24..31 → no capture and thumbnail.noFreeLayer (error)", none == null && session.Findings.Has("thumbnail.noFreeLayer", bra));
            Line("findings: " + (session.Findings.Count == 0 ? "none" : string.Join(", ", session.Findings.Select(f => f.Code + (f.Count > 1 ? " x" + f.Count : "")))));
        }

        static byte[] CharaCapture(SceneRuntime rt, SceneSession session, CharaRealization braR, CharaRealization heroR, int g, int c, int n, string clip, string id)
        {
            var set = session.SetAnimation(braR.NodeId, new AnimeRef(ContentIds.Vanilla, g, c, n));
            for (int i = 0; i < 10; i++) rt.Tick(1f / 30f);
            var anim = braR.Animation;
            Check($"{id}: {clip} plays on BR-Chan", set.Ok && anim && anim.Current?.Clip == clip && anim.IsLoaded, $"{set}, clip {anim?.Current?.Clip}");
            if (!anim) return null;

            // state before
            var before = anim.Save();
            var face = braR.Expression;
            var faceBefore = face?.Status.Clone();
            float mouthBefore = face?.MouthOpen ?? 0f;
            int eyeBefore = braR.Look ? braR.Look.EyePattern : -1, neckBefore = braR.Look ? braR.Look.NeckPattern : -1;
            var stagesBefore = braR.Motion.Stages.Where(s => s is DynamicBoneStage || s is BustStage).Select(s => s.Enabled).ToArray();
            int optionsBefore = anim.Items.Objects().Count;
            var heroRenderers = VisibilityApplier.OwnRenderers(heroR.Holder);
            var heroLayers = heroRenderers.Select(r => r.gameObject.layer).ToArray();
            var braRenderers = VisibilityApplier.OwnRenderers(braR.Holder);
            var braLayers = braRenderers.Select(r => r.gameObject.layer).ToArray();
            int free = CharaThumbnail.FreeLayer(rt.Root.transform, default);
            bool viewLightOn = rt.GetPart<SceneCharaLight>().Light && rt.GetPart<SceneCharaLight>().Light.enabled;

            byte[] png = null;
            CharaThumbnailInfo info = null;
            var sw = Stopwatch.StartNew();
            try { png = CharaThumbnail.Capture(braR, null, out info); }
            catch (Exception e) { Check($"{id}: capture without an exception", false, e.ToString()); }
            long ms = sw.ElapsedMilliseconds;
            CheckPng($"{id}: character thumbnail", png);
            if (png != null)
            {
                float cov = Coverage(png, new ThumbnailSettings().Background);
                Line($"{id}: {png.Length} bytes, {ms} ms (capture {info?.Milliseconds} ms), layer {info?.Layer}, renderers {info?.Renderers}, lights off {info?.LightsOff}, non-background {cov:P1}");
                Check($"{id}: the character is in frame (non-background pixels > 2 %)", cov > 0.02f, $"{cov:P1}");
            }
            Check($"{id}: the isolation layer was free (highest free in 24..31)", info != null && info.Layer == free && free >= 24, $"layer {info?.Layer}, free {free}");

            // state after
            var after = anim.Save();
            Check($"{id}: animation state after the scope equals the one before (Save() fields; time ± 1e-4)",
                  after.Guid == before.Guid && after.Group == before.Group && after.Category == before.Category && after.No == before.No &&
                  after.Speed == before.Speed && after.Pattern == before.Pattern && after.ForceLoop == before.ForceLoop && after.OptionVisible == before.OptionVisible &&
                  after.OptionParam1 == before.OptionParam1 && after.OptionParam2 == before.OptionParam2 && after.HandLeft == before.HandLeft && after.HandRight == before.HandRight &&
                  Mathf.Abs(after.NormalizedTime - before.NormalizedTime) <= 1e-4f,
                  $"before {before}; after {after}");
            Check($"{id}: face, look and physics switches restored",
                  face != null && SameFace(faceBefore, face.Status) && Mathf.Abs(face.MouthOpen - mouthBefore) < 1e-6f &&
                  braR.Look.EyePattern == eyeBefore && braR.Look.NeckPattern == neckBefore &&
                  braR.Motion.Stages.Where(s => s is DynamicBoneStage || s is BustStage).Select(s => s.Enabled).SequenceEqual(stagesBefore),
                  $"eyes {eyeBefore}->{braR.Look.EyePattern}, neck {neckBefore}->{braR.Look.NeckPattern}");
            Check($"{id}: HERO107's renderers kept their layers", heroRenderers.Select(r => r.gameObject.layer).SequenceEqual(heroLayers));
            int changed = 0, onIso = 0;
            for (int i = 0; i < braRenderers.Count; i++) if (braRenderers[i] && braRenderers[i].gameObject.layer != braLayers[i]) changed++;   // option items of the old motion are gone
            foreach (var r in VisibilityApplier.OwnRenderers(braR.Holder)) if (info != null && r.gameObject.layer == info.Layer) onIso++;
            Check($"{id}: BR-Chan's renderers back on their layers, none left on the isolation layer", changed == 0 && onIso == 0, $"changed {changed}, on the isolation layer {onIso}");
            bool lightNow = rt.GetPart<SceneCharaLight>().Light && rt.GetPart<SceneCharaLight>().Light.enabled;
            Check($"{id}: scene lights restored", lightNow == viewLightOn);
            var objs = anim.Items.Objects();
            var notUrp = objs.SelectMany(o => o.GetComponentsInChildren<Renderer>(true)).SelectMany(r => r.sharedMaterials)
                             .Where(m => m && m.shader && !m.shader.name.StartsWith("HS2Next/") && !m.shader.name.StartsWith("Universal Render Pipeline/"))
                             .Select(m => m.shader.name).Distinct().ToList();
            Check($"{id}: option items re-created through the hook and all URP", objs.Count == optionsBefore && notUrp.Count == 0,
                  $"option objects {optionsBefore} -> {objs.Count}; non-URP {string.Join(", ", notUrp)}");
            return png;
        }

        static bool SameFace(FaceStatus a, FaceStatus b) =>
            a != null && b != null && a.EyebrowPtn == b.EyebrowPtn && a.EyesPtn == b.EyesPtn && a.MouthPtn == b.MouthPtn && a.EyesBlink == b.EyesBlink &&
            a.EyesOpenMax == b.EyesOpenMax && a.EyebrowOpenMax == b.EyebrowOpenMax && a.MouthOpenMin == b.MouthOpenMin && a.MouthOpenMax == b.MouthOpenMax &&
            a.MouthFixed == b.MouthFixed && a.HohoAkaRate == b.HohoAkaRate && a.TearsRate == b.TearsRate && a.HideEyesHighlight == b.HideEyesHighlight;

        static void CheckPng(string what, byte[] png)
        {
            var h = png != null ? PngInfo.ReadHeader(png) : null;
            Check($"{what}: 504 x 704, bit depth 8, colour type 2 (RGB), no interlace",
                  h.HasValue && h.Value.Width == ThumbnailCapture.Width && h.Value.Height == ThumbnailCapture.Height && h.Value.BitDepth == 8 && h.Value.ColorType == 2 && h.Value.Interlace == 0,
                  h.HasValue ? $"{h.Value.Width}x{h.Value.Height} depth {h.Value.BitDepth} type {h.Value.ColorType}, {png.Length} bytes" : "no valid PNG");
        }

        /// <summary>Fraction of pixels that differ from <paramref name="bg"/> by more than 8 levels (decoded in memory, never shown or saved).</summary>
        static float Coverage(byte[] png, Color32 bg)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                tex.LoadImage(png);
                var px = tex.GetPixels32();
                int k = 0;
                foreach (var p in px) if (Math.Abs(p.r - bg.r) > 8 || Math.Abs(p.g - bg.g) > 8 || Math.Abs(p.b - bg.b) > 8) k++;
                return k / (float)px.Length;
            }
            finally { Object.DestroyImmediate(tex); }
        }
    }
}
