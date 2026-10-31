// SkyAmbientTest.play (Phase 3.3 package S): play mode, the app's URP asset.
//   & Tools\unity_batch.ps1 -Method SkyAmbientTest.RunPlay -NoQuit   -> Spike/out/effects/sky_ambient_play_report.txt
//   P1 the ambient probe per rule through map loads: no map (HS2's SH of the Studio colours = H1), vanilla map 10 (the same),
//      the Bakery mod bushitsu (its saved probe from the bundle = HS2's 1.074), hooh school (an LZMA bundle: Unity's value,
//      finding map.ambientProbeRecomputed), unload back to no map; AmbientMode.Custom, the guard count stable over frames;
//      the sky pattern replaces the map's skybox on Loaded; the no-map reflection
//   P2 TAA (U9): BR-Chan turning (holder rotation every frame) under render.aa = taa, compared with an SMAA render of the end
//      pose over the band around the moving silhouette (the start and end SMAA images differ there, dilated): TAA held still,
//      after turning with the fitted shaders' MotionVectors passes and with per-object motion vectors switched off (camera
//      motion only), each 5 times; TG1 the band is visible, TG2 camera-only motion leaves a ghost above the static spread
//      (else WARN, nothing to judge), TG3 the motion vectors cut it by more than the repeats' spread, TG4 by at least a third
//      (TG3 / TG4 hard or WARN by TaaGhostHard); the characters' motion is frozen meanwhile (only the turn moves); the hair
//      dither frame; the cost of a TAA frame vs SMAA (Editor, rough).
//      The images stay in memory.
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static partial class SkyAmbientTest
{
    static string PlayReport => Path.Combine(OutDir, "sky_ambient_play_report.txt");

    public static void RunPlay()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(PlayReport, "SUMMARY: 0 PASS, 1 FAIL\nSkyAmbientTest.play: started, no result yet\n", new UTF8Encoding(false));
        PlayModeHarness.Start(nameof(SkyAmbientTest) + "." + nameof(PlayMain), PlayReport, 30);
    }

    static SceneSession prs;
    static SceneRuntime prr;

    static IEnumerator PlayMain()
    {
        only = ArgList("-only");
        Begin();
        playWarn = 0;
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"SkyAmbientTest.play (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
        var pipe = new HS2Next.App.AppPipeline();
        pipe.Apply();
        foreach (var part in new Func<IEnumerator>[] { () => Want("P1") ? PlayAmbient() : null, () => Want("P2") ? PlayTaa() : null })
        {
            IEnumerator body = null;
            try { body = part(); }
            catch (Exception e) { Check("play part started", false, e.ToString()); }
            if (body == null) continue;
            while (true)
            {
                object cur;
                try
                {
                    if (!body.MoveNext()) break;
                    cur = body.Current;
                }
                catch (Exception e) { Check("play part ran without an exception", false, e.ToString()); break; }
                yield return cur;
            }
            try { prr?.Dispose(); } catch (Exception e) { Info("dispose: " + e.Message); }
            prr = null;
        }
        RenderOptions.ResetToDefaults();
        pipe.Restore();
        rep.Insert(0, $"SUMMARY: {pass} PASS, {fail} FAIL, {playWarn} WARN\nSKY AMBIENT PLAY TEST ({sw.Elapsed.TotalSeconds:F0} s)\n");
        File.WriteAllText(PlayReport, rep.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        Debug.Log($"SKY AMBIENT PLAY TEST: {pass} PASS, {fail} FAIL, {playWarn} WARN");
        PlayModeHarness.ExitCode = fail > 0 ? 1 : 0;
    }

    static int playWarn;
    // A comparison of two single-frame measurements that vary from run to run: PASS when it holds, else WARN with the
    // numbers (never FAIL).
    static void SoftCheck(string what, bool ok, string detail)
    {
        if (ok) pass++; else playWarn++;
        rep.AppendLine($"   {(ok ? "PASS" : "WARN")} {what}{(string.IsNullOrEmpty(detail) ? "" : " - " + detail)}");
    }

    static string Sh0s(SphericalHarmonicsL2 s) => $"{s[0, 0]:F6} / {s[1, 0]:F6} / {s[2, 0]:F6}";

    static IEnumerator LoadMap(MapRealization map, string mapRef)
    {
        prs.Set(null, "map.ref", mapRef);
        bool[] ok = { false };
        if (mapRef == null)
            yield return PlayModeHarness.WaitUntil(() => !map.IsBusy && map.PendingUnloads == 0 && map.State != MapState.Loaded, 120, ok);
        else
            yield return PlayModeHarness.WaitUntil(() => map.State == MapState.Loaded && !map.IsBusy, 300, ok);
        yield return PlayModeHarness.WaitFrames(3);
    }

    static IEnumerator PlayAmbient()
    {
        Section("P1 the ambient probe through map loads");
        var s = Services();
        prs = SceneRuntimeTest.NewSession(s);
        prr = new SceneRuntime(prs, s, null, true).AddStandardParts();
        yield return PlayModeHarness.WaitFrames(3);
        var sky = prr.GetPart<SceneSky>();
        var map = prr.GetPart<MapRealization>();
        var a = sky.Ambient;
        Check("no map: rule hs2, source colours, Custom, HS2's SH (H1, 1e-5)", a.Source == "colours" && RenderSettings.ambientMode == AmbientMode.Custom && MaxDiff(RenderSettings.ambientProbe, Hs2DefaultSh) <= 1e-5f,
            a.Source + " " + RenderSettings.ambientMode + " " + Sh0s(RenderSettings.ambientProbe));
        LightProbes.GetInterpolatedProbe(new Vector3(0, 1, 0), null, out var ip0);
        Check("… and that is what renderers get (interpolated probe without light probes)", MaxDiff(ip0, Hs2DefaultSh) <= 1e-5f, Sh0s(ip0));
        Check("no map: reflection Custom sky_BigSight_Env", RenderSettings.defaultReflectionMode == DefaultReflectionMode.Custom && RenderSettings.customReflectionTexture && RenderSettings.customReflectionTexture.name == SceneSky.NoMapReflectionAsset);
        int re0 = a.Reasserted;
        yield return PlayModeHarness.WaitFrames(10);
        Check("no map: the probe holds over 10 frames (no re-assertion)", a.Holds() && a.Reasserted == re0, a.Reasserted.ToString());

        // vanilla map 10 (Trilight)
        yield return LoadMap(map, ContentId.Vanilla(10).ToString());
        Check("map 10 loaded", map.State == MapState.Loaded, map.State.ToString());
        Info($"map 10: saved {(a.Saved?.Ok == true ? "ok" : a.Saved?.Error)}, scene mode {a.SceneMode}, {a.Saved?.Ms:F1} ms");
        Check("map 10: its own mode Trilight, the Studio colours' SH = HS2's (H1: ambient_v10_default)", a.SceneMode == AmbientMode.Trilight && a.Source == "colours" && MaxDiff(RenderSettings.ambientProbe, Hs2DefaultSh) <= 1e-5f,
            a.Source + " " + Sh0s(RenderSettings.ambientProbe));
        Check("map 10: the pattern copy is the map scene's skybox (Q-e)", RenderSettings.skybox == sky.SkyMaterial && sky.SkyMaterial, RenderSettings.skybox ? RenderSettings.skybox.name : "none");
        Info($"map 10 reflection: {RenderSettings.defaultReflectionMode} {(RenderSettings.customReflectionTexture ? RenderSettings.customReflectionTexture.name : "none")} (HS2: Custom without a cubemap)");
        yield return PlayModeHarness.WaitFrames(10);
        Check("map 10: holds over 10 frames", a.Holds(), a.Reasserted.ToString());

        // the Bakery mod bushitsu (Skybox, saved probe)
        yield return LoadMap(map, "hooh.minimap.bushitsu#0");
        if (map.State != MapState.Loaded) Info("bushitsu not loaded (not installed?): " + map.State);
        else
        {
            Info($"bushitsu: saved {(a.Saved?.Ok == true ? "ok " + a.Saved.Node : a.Saved?.Error)}, {a.Saved?.Ms:F1} ms, {a.Saved?.BlocksRead} blocks");
            // a map with light probes stays in Skybox mode (package A's follow-up: Unity 6 gives renderers outside the probe hull
            // the ambient probe in Custom mode; HS2 extrapolates the probes)
            Check("bushitsu (771 light probes): Skybox mode kept, source probes", RenderSettings.ambientMode == AmbientMode.Skybox && a.Source == "probes" && a.Holds(),
                a.Source + " " + RenderSettings.ambientMode);
            // rendered (MapTest.RenderedAmbientAt: a matte white sphere lit by no light): ~0.04 from the probes; the saved ambient
            // probe in Custom mode gave 1.0 (the capture 133 vs HS2's 99)
            var atOrigin = MapTest.RenderedAmbientAt(Vector3.zero);
            Check("bushitsu: a renderer at the origin (below the probe hull) is lit by the probes' extrapolation, not the ambient probe (HS2 0.0266 there)",
                atOrigin.r < 0.1f && atOrigin.r > 0f, $"{atOrigin.r:F4} {atOrigin.g:F4} {atOrigin.b:F4}");
            prs.Set(null, "effects.ambient.sky", new Color4(1f, 0f, 0f, 1f));
            yield return PlayModeHarness.WaitFrames(3);
            Check("bushitsu: the Studio colours change nothing (H1)", RenderSettings.ambientMode == AmbientMode.Skybox && a.Source == "probes");
            prs.Set(null, "effects.ambient", new AmbientLightEffect());
            prs.Set(null, "effects.sky.pattern", ContentId.Vanilla(4).ToString());
            yield return PlayModeHarness.WaitFrames(5);
            Check("bushitsu: a sky pattern change keeps Skybox mode", RenderSettings.ambientMode == AmbientMode.Skybox && a.Source == "probes", "reasserted " + a.Reasserted);
            LightProbes.GetInterpolatedProbe(Vector3.zero, null, out var ipb);
            Info($"bushitsu: interpolated probe at the origin {Sh0s(ipb)} (light probes {(LightmapSettings.lightProbes ? LightmapSettings.lightProbes.count : 0)}; HS2: 0.0266 from its light probes, H1)");
        }

        // hooh school: LZMA bundle → fallback
        yield return LoadMap(map, "hooh.map.school#1");
        if (map.State != MapState.Loaded) Info("school not loaded: " + map.State);
        else
        {
            // school ships 584 light probes: the probe rule comes first (Skybox kept); its LZMA bundle's saved probe stays unread
            Check("school (light probes, LZMA bundle): Skybox mode kept (source probes), nothing intended",
                a.Source == "probes" && !a.HasIntended && RenderSettings.ambientMode == AmbientMode.Skybox, a.Source + " / " + a.Saved?.Error);
        }

        // unload: back to the app scene
        yield return LoadMap(map, null);
        Check("unload: no map again: colours, Custom, HS2's SH", a.Source == "colours" && RenderSettings.ambientMode == AmbientMode.Custom && MaxDiff(RenderSettings.ambientProbe, Hs2DefaultSh) <= 1e-5f, Sh0s(RenderSettings.ambientProbe));
        Info($"re-assertions over the run: {a.Reasserted}; sky feed calls {sky.FeedCount}");
    }

    // ------------------------------------------------------------------ P2 TAA

    /// <summary>
    /// TG3 / TG4 are hard checks (FAIL) when true, else WARN with the numbers. Set from the Step 0 measurement of the band
    /// metric (three runs of P2 on the same tree): hard only when the margin of TG3 held in all three.
    /// </summary>
    public static bool TaaGhostHard = false;
    /// <summary>Repeats per condition (static, moving with motion vectors, moving without per-object motion vectors).</summary>
    public const int TaaRepeats = 5;
    /// <summary>A band pixel: the SMAA images of the start and the end pose differ by more than this in a channel (0..1).</summary>
    public const float TaaBandThreshold = 4f / 255f;
    /// <summary>The band's dilation (pixels, square neighbourhood) and the frame border left out (pixels).</summary>
    public const int TaaBandDilate = 3, TaaBandBorder = 1;
    /// <summary>TG1: the band holds at least this share of the frame (the motion is visible).</summary>
    public const double TaaBandMinShare = 0.005;

    // The band around the moving silhouette: pixels where the SMAA images of the start and the end pose differ by more than
    // the threshold in a channel, dilated by TaaBandDilate pixels, without the outer TaaBandBorder pixels of the frame.
    internal static bool[] TaaBand(Color[] start, Color[] end, int w, int h)
    {
        var diff = new bool[w * h];
        for (int i = 0; i < diff.Length; i++)
            diff[i] = Mathf.Abs(start[i].r - end[i].r) > TaaBandThreshold || Mathf.Abs(start[i].g - end[i].g) > TaaBandThreshold ||
                      Mathf.Abs(start[i].b - end[i].b) > TaaBandThreshold;
        // separable square dilation: rows, then columns
        var rows = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool on = false;
                for (int dx = -TaaBandDilate; dx <= TaaBandDilate && !on; dx++)
                {
                    int xx = x + dx;
                    if (xx >= 0 && xx < w && diff[y * w + xx]) on = true;
                }
                rows[y * w + x] = on;
            }
        var band = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (x < TaaBandBorder || y < TaaBandBorder || x >= w - TaaBandBorder || y >= h - TaaBandBorder) continue;
                bool on = false;
                for (int dy = -TaaBandDilate; dy <= TaaBandDilate && !on; dy++)
                {
                    int yy = y + dy;
                    if (yy >= 0 && yy < h && rows[yy * w + x]) on = true;
                }
                band[y * w + x] = on;
            }
        return band;
    }

    // Mean |a - b| over the masked pixels (null = every pixel), the three channels averaged, in 0..255.
    internal static double MeanAbs(Color[] a, Color[] b, bool[] mask)
    {
        double d = 0;
        long n = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (mask != null && !mask[i]) continue;
            d += Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b);
            n++;
        }
        return n == 0 ? 0 : d / (3.0 * n) * 255.0;
    }

    internal static double Median(double[] v)
    {
        var s = v.OrderBy(x => x).ToArray();
        int m = s.Length / 2;
        return s.Length == 0 ? double.NaN : s.Length % 2 == 1 ? s[m] : (s[m - 1] + s[m]) / 2.0;
    }

    internal static double Spread(double[] v) => v.Length == 0 ? double.NaN : v.Max() - v.Min();

    static string List(double[] v) => string.Join(", ", v.Select(x => x.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)));

    static IEnumerator PlayTaa()
    {
        Section("P2 TAA with the MotionVectors passes (U9): the ghost in the band around the moving silhouette, " + TaaRepeats + " repeats per condition");
        var s = Services();
        var f = SceneRuntimeTest.BuildFixture(s);
        prs = f.Session;
        prr = f.Runtime;
        var br = prr.FindChara(f.BrChan);
        var view = prr.GetPart<SceneViewCamera>();
        var cam = view.Camera;
        const int W = 640, H = 360;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { name = "taa test" };
        rt.Create();
        var p2 = Stopwatch.StartNew();
        // batch play mode renders no camera by itself: every frame ticks the runtime (Apply + LateApply) and renders the view
        float frameDt = -1f;   // < 0: the frame's time; 0 while measuring (nothing advances)
        void Frame()
        {
            prr.Tick(frameDt < 0f ? Time.deltaTime : frameDt);
            prr.LateApply();
            cam.targetTexture = rt;
            cam.Render();
        }
        for (int i = 0; i < 5; i++) { Frame(); yield return PlayModeHarness.WaitFrames(1); }
        if (br == null || !br.Root) { Check("BR-Chan built", false); yield break; }
        var head = br.Root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "cf_J_Head");
        var target = head ? head.position : br.Holder.position + Vector3.up * 14f;
        var q = Quaternion.LookRotation(-br.Holder.forward, Vector3.up);
        var e = q.eulerAngles;
        prs.Set(null, "camera.current", new CameraView { Pivot = new Vec3(target.x, target.y, target.z), Rot = new Vec3(e.x, e.y, 0), Distance = 12, Fov = 23 }, ExecuteOptions.NotRecorded);
        prs.Set(null, "effects.depthOfField.enabled", false);
        for (int i = 0; i < 5; i++) { Frame(); yield return PlayModeHarness.WaitFrames(1); }
        // the characters' motion (animation, dynamic bones) is frozen for the measurement: the holder's turn is the only motion,
        // so the SMAA references and every TAA image show the same pose apart from it (an animated pose differs from run to run)
        var frozen = prr.Charas().Where(c => c.Motion && c.Motion.AutoStep).Select(c => c.Motion).ToList();
        foreach (var m in frozen) m.AutoStep = false;
        frameDt = 0f;   // the runtime steps the characters by hand (ManualStepping): steps of 0
        Info($"motion frozen: {frozen.Count} self-stepping, ManualStepping {prr.Settings.ManualStepping}, frame steps of 0");
        var holder = br.Holder;
        var rot0 = holder.rotation;
        // the images stay in memory (never written)
        Color[] Grab()
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            var px = t.GetPixels();
            Object.Destroy(t);
            return px;
        }
        IEnumerator Hold(int n) { for (int i = 0; i < n; i++) { Frame(); yield return PlayModeHarness.WaitFrames(1); } }
        // a run: frames turning by step degrees per frame under the given aa, then the last frame's image
        float stepDeg = 2f;
        int n = 20;
        var endRot = rot0 * Quaternion.Euler(0f, stepDeg * n, 0f);
        var img = new Color[1][];
        var ms = new double[1];
        IEnumerator Turn(string aa)
        {
            RenderOptions.Apply(JObject.Parse("{\"aa\":\"" + aa + "\"}"));
            holder.rotation = rot0;
            yield return Hold(12);   // history settles (the previous run's history is gone)
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; i++)
            {
                holder.rotation = rot0 * Quaternion.Euler(0f, stepDeg * (i + 1), 0f);
                Frame();
                yield return PlayModeHarness.WaitFrames(1);
            }
            ms[0] = sw.Elapsed.TotalMilliseconds / n;
            img[0] = Grab();
        }
        // TAA held at the end pose: the converged image without motion
        IEnumerator Still()
        {
            RenderOptions.Apply(JObject.Parse("{\"aa\":\"taa\"}"));
            holder.rotation = endRot;
            yield return Hold(16);
            img[0] = Grab();
        }
        // references: SMAA at the start and at the end pose, held
        RenderOptions.Apply(JObject.Parse("{\"aa\":\"smaa\"}"));
        holder.rotation = rot0;
        yield return Hold(8);
        var smaaStart = Grab();
        holder.rotation = endRot;
        yield return Hold(8);
        var smaa = Grab();
        var band = TaaBand(smaaStart, smaa, W, H);
        int bandPx = band.Count(b => b);
        double share = bandPx / (double)(W * H);
        Check($"TG1 the band around the moving silhouette holds at least {TaaBandMinShare:P1} of the frame (the motion is visible)", share >= TaaBandMinShare,
            $"{bandPx} px = {share:P2} (start / end SMAA differ by > 4/255, dilated {TaaBandDilate} px, border {TaaBandBorder} px left out)");
        RenderOptions.Apply(JObject.Parse("{\"aa\":\"taa\"}"));
        yield return Hold(16);
        var cd = cam.GetComponent<UniversalAdditionalCameraData>();
        Check("TAA renders on the view camera (TemporalAntiAliasing, dither frame = frameCount & 3)", cd.antialiasing == AntialiasingMode.TemporalAntiAliasing &&
            TemporalAAControl.DitherFrameFor(cam) == (Time.frameCount & 3), cd.antialiasing.ToString());
        var rends = br.Root.GetComponentsInChildren<Renderer>(true);
        var modes = rends.Select(r => r.motionVectorGenerationMode).ToArray();
        double[] bStatic = new double[TaaRepeats], bWith = new double[TaaRepeats], bWithout = new double[TaaRepeats];
        double[] fStatic = new double[TaaRepeats], fWith = new double[TaaRepeats], fWithout = new double[TaaRepeats];
        double msTaa = 0, msSmaa;
        // the conditions interleaved per repeat, so a slow drift of the machine reaches all three alike
        for (int k = 0; k < TaaRepeats; k++)
        {
            yield return Still();
            bStatic[k] = MeanAbs(img[0], smaa, band); fStatic[k] = MeanAbs(img[0], smaa, null);
            yield return Turn("taa");
            bWith[k] = MeanAbs(img[0], smaa, band); fWith[k] = MeanAbs(img[0], smaa, null);
            msTaa += ms[0];
            // without per-object motion vectors (camera motion only, as for a shader without the pass)
            foreach (var r in rends) r.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
            yield return Turn("taa");
            for (int i = 0; i < rends.Length; i++) rends[i].motionVectorGenerationMode = modes[i];
            bWithout[k] = MeanAbs(img[0], smaa, band); fWithout[k] = MeanAbs(img[0], smaa, null);
        }
        yield return Turn("smaa");
        msSmaa = ms[0];
        msTaa /= TaaRepeats;
        double mS = Median(bStatic), mW = Median(bWith), mN = Median(bWithout);
        double sS = Spread(bStatic), sW = Spread(bWith), sN = Spread(bWithout);
        Info($"band metric (mean |TAA - SMAA end| over the band, /255): static [{List(bStatic)}] median {mS:F3} spread {sS:F3}; " +
             $"with motion vectors [{List(bWith)}] median {mW:F3} spread {sW:F3}; without per-object motion vectors [{List(bWithout)}] median {mN:F3} spread {sN:F3}");
        Info($"frame means (the old metric, /255): static median {Median(fStatic):F3} spread {Spread(fStatic):F3}; with {Median(fWith):F3} / {Spread(fWith):F3}; " +
             $"without {Median(fWithout):F3} / {Spread(fWithout):F3}");
        Check("the image changes under TAA (the test renders)", Median(fStatic) > 0.01, $"{Median(fStatic):F3}");
        double ghost = mN - mS;
        bool informative = ghost >= 3 * sS;
        SoftCheck("TG2 camera-only motion leaves a measurable ghost (median(without) - median(static) >= 3 x spread(static); else there is no ghost to cut)",
            informative, $"without - static {ghost:F3}, 3 x spread(static) {3 * sS:F3}");
        double margin = Math.Max(sW, sN);
        bool tg3 = mW < mN - margin;
        bool tg4 = mN - mW >= ghost / 3.0;
        string d3 = $"median with {mW:F3} < median without {mN:F3} - max(spread with {sW:F3}, spread without {sN:F3}) = {mN - margin:F3}";
        string d4 = $"without - with {mN - mW:F3} >= (without - static) / 3 = {ghost / 3.0:F3}";
        if (!informative)
            Info("TG3 / TG4 not judged (no ghost to cut): " + d3 + "; " + d4);
        else if (TaaGhostHard)
        {
            Check("TG3 the motion vectors cut the ghost by more than the repeats' spread", tg3, d3);
            Check("TG4 the passes remove at least a third of the ghost camera motion alone leaves", tg4, d4);
        }
        else
        {
            SoftCheck("TG3 the motion vectors cut the ghost by more than the repeats' spread", tg3, d3);
            SoftCheck("TG4 the passes remove at least a third of the ghost camera motion alone leaves", tg4, d4);
        }
        Info($"TAAGHOST band {bandPx} share {share:F5} static {mS:F4}/{sS:F4} with {mW:F4}/{sW:F4} without {mN:F4}/{sN:F4} tg2 {informative} tg3 {tg3} tg4 {tg4} margin {mN - margin - mW:F4} p2s {p2.Elapsed.TotalSeconds:F1}");
        Info($"Editor frame time (rough, play mode, tick + render of a 640 x 360 target): TAA {msTaa:F1} ms, SMAA {msSmaa:F1} ms; P2 {p2.Elapsed.TotalSeconds:F0} s");
        cam.targetTexture = null;
        rt.Release();
        Object.Destroy(rt);
        holder.rotation = rot0;
        foreach (var m in frozen) if (m) m.AutoStep = true;
        RenderOptions.ResetToDefaults();
    }
}
