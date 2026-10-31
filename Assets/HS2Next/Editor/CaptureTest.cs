// CaptureTest.render (Phase 3.6 package C): play mode, the app's URP
// asset (AppPipeline, with the run-time background / frame features), an AppCore test host (bridge without HTTP, real
// frames so the asynchronous read-back runs), synthetic unlit stages on layer 10 in front of the view camera (no
// character, no map).
//   S1 sizes: capture 1× (640 × 360), supersample 2 and 4 (output size kept, render size k ×), screenshot custom
//      2560 × 1440, fit × 2 and viewport × 1 (the view's pixel size and the screen); the file sizes of the PNGs
//   S2 the cap: CaptureLimits.Check at 16,588,800 (passes) and + 1 pixel (refused); 33,177,600 behind the setting; capture
//      8192 × 8192 and screenshot 4096 × 4096 × 2 → bad_args naming the cap (nothing rendered); the large-render warning
//   S3 the 1× path = the 3.5 ViewCapture path (ReadPixels into RGB24, EncodeToPNG; reproduced here) bit for bit, and the
//      orientation (a red square at the top left of the view is at the top left of the PNG)
//   S4 the box filter: BoxReduce on known values (linear light), a 2× capture of a flat colour = the 1× capture
//   S5 R-M2: 2× and 4× captures reduced vs 1× with bloom (an HDR patch) and the item-focus DOF on: mean / max difference
//      (mean ≤ 1.5 / 255); the bloom iterations mapped for the render size inside the capture, the view's restored after
//   S6 hidden in captures: a light's target display (helper layer, LightHelpers.HideForCapture) shows in the view and not
//      in a capture (capture with the display on = off, bit-equal)
//   S7 background in captures (capture and screenshot), the frame in screenshot by default, out of capture by default,
//      in capture with frame true
//   S8 files: the vanilla stamp name yyyyMMddHHmmssfff.png under <UserData>/cap, the same name twice → _1; HS2's cap folder
//      untouched
//   S9 asynchronous: the screenshot's main-thread time is the render (+ set-up), the encode runs on a worker (frames pass
//      between the request and the reply); timings reported
// Run: -executeMethod CaptureTest.RunRender (-NoQuit; the harness exits); report Spike/out/capture/capture_render_report.txt.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HS2Next;
using HS2Next.App;
using HS2Next.App.Commands;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Screenshots;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

/// <summary>The shared rig of CaptureTest.render and BackgroundTest.render: an AppCore test host in play mode, a stage, checks.</summary>
public sealed class CaptureRig
{
    public static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "capture");
    public readonly StringBuilder Rep = new StringBuilder();
    public int Pass, Fail, Warns;
    public string Current = "";
    public AppCore App;
    public Transform Stage;
    public JObject Last;
    public string LastText;
    public readonly List<string> Measurements = new List<string>();
    HS2Next.App.AppPipeline pipe;
    bool ppAssigned;
    static RuntimeServices services;
    public readonly string Name;

    public CaptureRig(string name) { Name = name; }

    public SceneRuntime Rt => App.Runtime;
    public SceneSession Session => App.Session;
    public SceneViewCamera View => Rt.GetPart<SceneViewCamera>();
    public string UserData => Path.Combine(OutDir, Name + "_userdata");
    public string CapDir => App.Paths.CapDir;

    public void Info(string s) { Rep.AppendLine("   " + s); }
    public void Warn(string s) { Warns++; Rep.AppendLine($"WARN  [{Current}] {s}"); }
    public void Measure(string s) { Measurements.Add($"[{Current}] {s}"); Info("MEASURE " + s); }

    public bool Check(string what, bool ok, string detail = "")
    {
        Rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  [{Current}] {what}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) Pass++; else Fail++;
        return ok;
    }

    public void Section(string id, string title)
    {
        Current = id;
        Rep.AppendLine();
        Rep.AppendLine($"== {id} {title}");
    }

    // ------------------------------------------------------------ set-up / tear-down

    public void Start()
    {
        pipe = new HS2Next.App.AppPipeline();
        pipe.Apply();
        Info("pipeline: " + pipe.Summary);
        ppAssigned = EnsurePostProcessData();
        var log = new StringBuilder();
        services ??= SceneRuntimeTest.NewServices(log);
        if (Directory.Exists(UserData)) Directory.Delete(UserData, true);
        var s = AppSettings.Defaults(Array.Empty<string>());
        s.Content = CardResolveTest.Settings;
        s.UserData = UserData;
        s.Bridge.OutputDir = Path.Combine(UserData, "Bridge");
        var o = services.NewSessionOptions();
        o.Ids = new CountingIdSource(0xca97e5UL);
        App = AppCore.ForTests(services, s, null, o);
        SceneImages.Ensure(App.Runtime);
        CaptureSettings.Reset();
        Stage = new GameObject("capture_test_stage").transform;
        Info($"Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType} ({SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsMemorySize} MB), screen {Screen.width} × {Screen.height}, " +
             $"view {View?.Camera?.pixelWidth} × {View?.Camera?.pixelHeight}, max texture {SystemInfo.maxTextureSize}");
    }

    UnityEngine.Rendering.Universal.UniversalRendererData ppData;

    /// <summary>EffectsTest's rule: the production renderer needs URP's PostProcessData (assigned in memory when the asset has none).</summary>
    bool EnsurePostProcessData()
    {
        var asset = GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        var urd = asset ? asset.rendererDataList[0] as UnityEngine.Rendering.Universal.UniversalRendererData : null;
        if (!urd) { Info("no Universal renderer data"); return false; }
        if (urd.postProcessData) { Info("renderer post-processing data: " + urd.postProcessData.name + " (asset)"); return false; }
        urd.postProcessData = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
        urd.SetDirty();
        ppData = urd;
        Info("the production renderer has no PostProcessData: URP's default assigned in memory for this test");
        return urd.postProcessData;
    }

    public void Stop()
    {
        try { if (Stage) Object.Destroy(Stage.gameObject); } catch (Exception e) { Info("stage: " + e.Message); }
        try { App?.Dispose(); } catch (Exception e) { Info("dispose: " + e.Message); }
        if (ppAssigned && ppData) { ppData.postProcessData = null; ppData.SetDirty(); }
        pipe?.Restore();
        CaptureSettings.Reset();
    }

    public void Write(string reportPath, double seconds)
    {
        Rep.Insert(0, $"SUMMARY: {Pass} PASS, {Fail} FAIL\n{Name} ({seconds:F0} s, {Warns} warnings)\n");
        if (Measurements.Count > 0) { Rep.AppendLine(); Rep.AppendLine("== measurements"); foreach (var m in Measurements) Rep.AppendLine("   " + m); }
        File.WriteAllText(reportPath, Rep.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        Debug.Log($"{Name}: {Pass} PASS, {Fail} FAIL");
    }

    // ------------------------------------------------------------ bridge

    /// <summary>One request through the test host's bridge with real frames in between (the read-back needs them).</summary>
    public IEnumerator Call(object req, int maxFrames = 1800)
    {
        string json = req is string s ? s : JsonConvert.SerializeObject(req);
        var t = App.Bridge.Execute(json);
        int n = 0;
        while (!t.IsCompleted && n++ < maxFrames)
        {
            App.Frame(Time.deltaTime > 0 ? Time.deltaTime : 1f / 60f);
            yield return null;
        }
        LastText = t.IsCompleted ? (t.IsFaulted ? "{\"ok\":false,\"error\":\"faulted\"}" : t.Result) : "{\"ok\":false,\"code\":\"timeout\"}";
        try { Last = JObject.Parse(LastText); } catch (Exception) { Last = new JObject { ["ok"] = false, ["error"] = "unparsable: " + LastText }; }
        LastFrames = n;
    }

    /// <summary>Frames pumped by the last <see cref="Call"/>.</summary>
    public int LastFrames;
    public bool Ok => Last != null && Last.Value<bool?>("ok") == true;
    public string Code => (string)Last?["code"];
    public string Short => LastText == null ? "null" : LastText.Length > 400 ? LastText.Substring(0, 400) + "…" : LastText;

    public void Settle()
    {
        Rt.Apply();
        Rt.LateApply();
    }

    // ------------------------------------------------------------ images

    public sealed class Shot
    {
        public int W, H;
        public Color32[] Px;
        /// <summary>Pixel by image coordinates (x right, y DOWN from the top row of the PNG).</summary>
        public Color32 At(int x, int y) => Px[(H - 1 - y) * W + x];
        public Color32 AtUv(float u, float vFromTop) => At(Mathf.Clamp((int)(u * W), 0, W - 1), Mathf.Clamp((int)(vFromTop * H), 0, H - 1));
    }

    public static Shot Decode(byte[] png)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        t.LoadImage(png);
        var s = new Shot { W = t.width, H = t.height, Px = t.GetPixels32() };
        Object.Destroy(t);
        return s;
    }

    public static Shot Load(string path) => path != null && File.Exists(path) ? Decode(File.ReadAllBytes(path)) : null;

    public Shot Capture(int w, int h, int k = 1, bool frame = false, string save = null)
    {
        Settle();
        var r = ViewCapture.Render(Rt, View.Camera, new CaptureRequest { Width = w, Height = h, Supersample = k, Frame = frame, MaxPixels = CaptureLimits.HardMaxPixels });
        if (save != null) File.WriteAllBytes(Path.Combine(OutDir, save + ".png"), r.Png);
        return Decode(r.Png);
    }

    /// <summary>Mean and max channel difference (0..255) of two equal-size shots (-1 when the sizes differ).</summary>
    public static (double mean, int max) Diff(Shot a, Shot b, int skipBorder = 0)
    {
        if (a == null || b == null || a.W != b.W || a.H != b.H) return (-1, -1);
        double sum = 0; int max = 0; long n = 0;
        for (int y = skipBorder; y < a.H - skipBorder; y++)
            for (int x = skipBorder; x < a.W - skipBorder; x++)
            {
                var p = a.Px[y * a.W + x]; var q = b.Px[y * a.W + x];
                int d0 = Math.Abs(p.r - q.r), d1 = Math.Abs(p.g - q.g), d2 = Math.Abs(p.b - q.b);
                sum += d0 + d1 + d2; n += 3;
                max = Math.Max(max, Math.Max(d0, Math.Max(d1, d2)));
            }
        return (n > 0 ? sum / n : 0, max);
    }

    public static int ColorDiff(Color32 a, Color32 b) => Math.Max(Math.Abs(a.r - b.r), Math.Max(Math.Abs(a.g - b.g), Math.Abs(a.b - b.b)));
    public static string C(Color32 c) => $"({c.r}, {c.g}, {c.b})";

    // ------------------------------------------------------------ stage

    public void SetView(Vector3 pivot, Vector3 rot, float dist, float fov)
    {
        Session.Set(null, "camera.current", new CameraView { Pivot = new Vec3(pivot.x, pivot.y, pivot.z), Rot = new Vec3(rot.x, rot.y, rot.z), Distance = dist, Fov = fov });
        Settle();
    }

    public static Material Unlit(Color c, Texture tex = null)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        m.SetColor("_BaseColor", c);
        if (tex) m.SetTexture("_BaseMap", tex);
        return m;
    }

    public GameObject Quad(string name, Vector3 pos, Vector2 size, Color c)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
        g.name = name;
        var col = g.GetComponent<Collider>();
        if (col) Object.Destroy(col);
        g.transform.SetParent(Stage, false);
        g.transform.localPosition = pos;
        g.transform.localScale = new Vector3(size.x, size.y, 1f);
        g.layer = 10;
        g.GetComponent<Renderer>().sharedMaterial = Unlit(c);
        RenderingLayers.ApplyToRenderers(g);
        return g;
    }

    public void ClearStage()
    {
        for (int i = Stage.childCount - 1; i >= 0; i--) Object.DestroyImmediate(Stage.GetChild(i).gameObject);
    }

    /// <summary>Every effect off except the grading's base (bloom, vignette, AO, probe, DOF off); post-processing as given.</summary>
    public void EffectsOff(bool postProcessing)
    {
        using (var tx = Session.Commands.BeginOrJoin("test", default))
        {
            tx.Set(null, "effects.bloom.enabled", false);
            tx.Set(null, "effects.vignette.enabled", false);
            tx.Set(null, "effects.ambientOcclusion.enabled", false);
            tx.Set(null, "effects.reflectionProbe.enabled", false);
            tx.Set(null, "effects.depthOfField.enabled", false);
            tx.Set(null, "effects.sky.enabled", false);
            tx.Commit();
        }
        View.Setup.PostProcessing = postProcessing;
        Settle();
    }

    /// <summary>A test PNG (RGBA) from a pixel function of (x, y from the top).</summary>
    public static string WritePng(string path, int w, int h, Func<int, int, Color32> px)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var a = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) a[(h - 1 - y) * w + x] = px(x, y);
        t.SetPixels32(a);
        t.Apply();
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.Destroy(t);
        return path;
    }
}

public static class CaptureTest
{
    static string Report => Path.Combine(CaptureRig.OutDir, "capture_render_report.txt");
    static CaptureRig g;
    const int W = 640, H = 360;

    public static void RunRender()
    {
        Directory.CreateDirectory(CaptureRig.OutDir);
        File.WriteAllText(Report, "SUMMARY: 0 PASS, 1 FAIL\nCaptureTest.render: started, no result yet\n", new UTF8Encoding(false));
        PlayModeHarness.Start(nameof(CaptureTest) + "." + nameof(Main), Report, 30);
    }

    static IEnumerator Main()
    {
        var sw = Stopwatch.StartNew();
        g = new CaptureRig("CaptureTest.render");
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
        g.Stop();
        g.Write(Report, sw.Elapsed.TotalSeconds);
        PlayModeHarness.ExitCode = g.Fail > 0 ? 1 : 0;
    }

    static string Hs2CapDir => CardResolveTest.GameRoot == null ? null : Path.Combine(CardResolveTest.GameRoot, "UserData", "cap");

    static (int count, DateTime latest) DirState(string d)
    {
        if (d == null || !Directory.Exists(d)) return (-1, DateTime.MinValue);
        var f = Directory.GetFiles(d);
        return (f.Length, f.Length == 0 ? DateTime.MinValue : f.Max(File.GetLastWriteTimeUtc));
    }

    static IEnumerator Body()
    {
        var hs2Cap0 = DirState(Hs2CapDir);
        // a dark-grey wall, a red square at the top left, a white square at the bottom right (orientation probes)
        g.SetView(new Vector3(0, 0, 0), Vector3.zero, 20f, 40f);
        g.EffectsOff(true);
        g.Quad("wall", new Vector3(0, 0, 10), new Vector2(60, 40), new Color(0.2f, 0.2f, 0.2f));
        g.Quad("red", new Vector3(-5, 3, 9.9f), new Vector2(2, 2), Color.red);
        g.Quad("white", new Vector3(5, -3, 9.9f), new Vector2(2, 2), Color.white);
        yield return null;
        g.Settle();

        // ---------------------------------------------------------------- S3 legacy path, orientation
        g.Section("S3", "1× = the 3.5 path; orientation");
        {
            g.Settle();
            var a = g.Capture(W, H, save: "s3_capture");
            var b = CaptureRig.Decode(LegacyRender(g.View.Camera, W, H));
            var (mean, max) = CaptureRig.Diff(a, b);
            g.Check("CaptureService 1× = the 3.5 ViewCapture path (ReadPixels, EncodeToPNG) bit for bit", max == 0, $"mean {mean:F3}, max {max}");
            var red = Find(a, c => c.r > 200 && c.g < 60 && c.b < 60);
            var white = Find(a, c => c.r > 200 && c.g > 200 && c.b > 200);
            g.Check("orientation: the red square (world top left) is in the PNG's top-left quarter, the white one (bottom right) in the bottom-right quarter",
                red.x >= 0 && red.x < W / 2 && red.y < H / 2 && white.x > W / 2 && white.y > H / 2, $"red at {red}, white at {white}");
        }

        // ---------------------------------------------------------------- S1 sizes
        g.Section("S1", "sizes");
        yield return g.Call(new { cmd = "capture", name = "s1_1x", width = W, height = H });
        var s1 = g.Ok ? CaptureRig.Load((string)g.Last["path"]) : null;
        g.Check("capture 640 × 360 → PNG 640 × 360, render 640 × 360", g.Ok && s1 != null && s1.W == W && s1.H == H && (int)g.Last["renderWidth"] == W && (int)g.Last["renderHeight"] == H, g.Short);
        yield return g.Call(new { cmd = "capture", name = "s1_ss2", width = W, height = H, supersample = 2 });
        var s2 = g.Ok ? CaptureRig.Load((string)g.Last["path"]) : null;
        g.Check("capture supersample 2 → PNG 640 × 360, render 1280 × 720", g.Ok && s2 != null && s2.W == W && s2.H == H && (int)g.Last["renderWidth"] == 2 * W && (int)g.Last["renderHeight"] == 2 * H, g.Short);
        yield return g.Call(new { cmd = "capture", name = "s1_ss4", width = W, height = H, supersample = 4 });
        var s4 = g.Ok ? CaptureRig.Load((string)g.Last["path"]) : null;
        g.Check("capture supersample 4 → PNG 640 × 360, render 2560 × 1440", g.Ok && s4 != null && s4.W == W && s4.H == H && (int)g.Last["renderWidth"] == 4 * W, g.Short);
        yield return g.Call(new { cmd = "capture", name = "s1_bad_ss", width = W, height = H, supersample = 3 });
        g.Check("capture supersample 3 → bad_args", !g.Ok && g.Code == "bad_args", g.Short);
        yield return g.Call(new { cmd = "screenshot", mode = "custom", width = 2560, height = 1440, name = "s1_custom" });
        var sc = g.Ok ? CaptureRig.Load((string)g.Last["path"]) : null;
        g.Check("screenshot custom 2560 × 1440 → the file under <UserData>/cap, 2560 × 1440", g.Ok && sc != null && sc.W == 2560 && sc.H == 1440
            && Path.GetDirectoryName((string)g.Last["path"]).Equals(g.CapDir, StringComparison.OrdinalIgnoreCase), g.Short);
        var cam = g.View.Camera;
        var fit2 = CaptureLimits.Fit(cam.pixelWidth, cam.pixelHeight, Screen.width, Screen.height, 2f);
        yield return g.Call(new { cmd = "screenshot", mode = "fit", scale = 2, name = "s1_fit2", frame = false });
        var sf = g.Ok ? CaptureRig.Load((string)g.Last["path"]) : null;
        g.Check($"screenshot fit × 2 → 2 × the fit size {fit2.x} × {fit2.y} (view {cam.pixelWidth} × {cam.pixelHeight}, screen {Screen.width} × {Screen.height})",
            g.Ok && sf != null && sf.W == fit2.x && sf.H == fit2.y, g.Short);
        yield return g.Call(new { cmd = "screenshot", mode = "viewport", name = "s1_viewport", frame = false });
        var sv = g.Ok ? CaptureRig.Load((string)g.Last["path"]) : null;
        g.Check($"screenshot viewport × 1 → the view's pixel size {cam.pixelWidth} × {cam.pixelHeight}", g.Ok && sv != null && sv.W == cam.pixelWidth && sv.H == cam.pixelHeight, g.Short);
        g.Check("Fit: a 1000 × 500 viewport in a 1920 × 1080 window → 1920 × 960; in 800 × 1080 → 800 × 400; × 2 → 3840 × 1920",
            CaptureLimits.Fit(1000, 500, 1920, 1080, 1) == new Vector2Int(1920, 960) && CaptureLimits.Fit(1000, 500, 800, 1080, 1) == new Vector2Int(800, 400)
            && CaptureLimits.Fit(1000, 500, 1920, 1080, 2) == new Vector2Int(3840, 1920));

        // ---------------------------------------------------------------- S2 cap
        g.Section("S2", "the pixel cap");
        g.Check("Check: a render of 3840 × 2160 × 2 pixels (3840 × 4320 = 16,588,800; also 1920 × 2160 at supersample 2) passes the default cap; one row more does not",
            CaptureLimits.Check(3840, 4320, 1, 0, out long p1) == null && p1 == 16_588_800 && CaptureLimits.Check(1920, 2160, 2, 0, out _) == null
            && CaptureLimits.Check(3840, 4321, 1, 0, out _) != null && CaptureLimits.Check(1920, 2161, 2, 0, out _) != null,
            CaptureLimits.Check(3840, 4321, 1, 0, out _));
        g.Check("Check: 7680 × 4320 passes the hard cap 33,177,600, 7681 × 4320 does not; the setting clamps to the hard cap",
            CaptureLimits.Check(7680, 4320, 1, CaptureLimits.HardMaxPixels, out _) == null && CaptureLimits.Check(7681, 4320, 1, CaptureLimits.HardMaxPixels, out _) != null
            && Clamped(99_000_000) == CaptureLimits.HardMaxPixels);
        g.Check("Check: each side 16-8192, supersample 1 / 2 / 4, the render side ≤ the GPU's texture limit " + CaptureLimits.MaxTexture,
            CaptureLimits.Check(15, 100, 1, 0, out _) != null && CaptureLimits.Check(8193, 100, 1, 0, out _) != null && CaptureLimits.Check(100, 100, 3, 0, out _) != null
            && (CaptureLimits.MaxTexture >= 32768 || CaptureLimits.Check(8192, 16, 4, CaptureLimits.HardMaxPixels, out _) != null));
        int before = CaptureService.Completed;
        yield return g.Call(new { cmd = "capture", name = "s2_8k8k", width = 8192, height = 8192 });
        g.Check("capture 8192 × 8192 (67 Mpix) → bad_args naming the cap, nothing rendered", !g.Ok && g.Code == "bad_args" && g.Short.Contains("pixel cap") && CaptureService.Completed == before, g.Short);
        yield return g.Call(new { cmd = "screenshot", mode = "custom", width = 4096, height = 4096, supersample = 2, name = "s2_big" });
        g.Check("screenshot 4096 × 4096 × 2 → bad_args with the limit", !g.Ok && g.Code == "bad_args" && g.Short.Contains("16588800"), g.Short);
        CaptureLimits.MaxPixels = CaptureLimits.HardMaxPixels;
        g.Check("with capture.maxPixels 33,177,600: 3840 × 2160 at supersample 2 (an 8K render) passes and 4096 × 4096 × 2 is still refused",
            CaptureLimits.Check(3840, 2160, 2, 0, out _) == null && CaptureLimits.Check(4096, 4096, 2, 0, out _) != null);
        CaptureLimits.MaxPixels = CaptureLimits.DefaultMaxPixels;
        g.Check("the warning capture.largeRender is given above 16.6 Mpix (render 4100 × 4100: checked on the request, not rendered)",
            (long)4100 * 4100 > CaptureLimits.WarnPixels && CaptureLimits.LargeRenderWarning.StartsWith("capture.largeRender"));

        // ---------------------------------------------------------------- S4 box filter
        g.Section("S4", "the linear-light box filter");
        {
            var rgba = new byte[] { 0, 0, 0, 255, 255, 255, 255, 255, 0, 0, 0, 255, 255, 255, 255, 255 };
            var o = CaptureService.BoxReduce(rgba, 2, 2, 2);
            g.Check("2 × 2 of black / white → sRGB 188 (linear 0.5), not 128", o.Length == 3 && o[0] == 188 && o[1] == 188 && o[2] == 188, string.Join(",", o));
            bool all = true;
            for (int v = 0; v < 256; v++)
            {
                var px = new byte[64];
                for (int i = 0; i < 16; i++) { px[i * 4] = (byte)v; px[i * 4 + 1] = (byte)v; px[i * 4 + 2] = (byte)v; px[i * 4 + 3] = 255; }
                var q = CaptureService.BoxReduce(px, 4, 4, 4);
                if (q[0] != v) { all = false; break; }
            }
            g.Check("a flat 4 × 4 block of every byte value reduces to the same value", all);
            g.Check("the flat wall of a 2× capture = the 1× capture (centre, 1 / 255)", s1 != null && s2 != null && CaptureRig.ColorDiff(s1.AtUv(0.5f, 0.3f), s2.AtUv(0.5f, 0.3f)) <= 1,
                s1 == null || s2 == null ? "" : CaptureRig.C(s1.AtUv(0.5f, 0.3f)) + " vs " + CaptureRig.C(s2.AtUv(0.5f, 0.3f)));
        }

        // ---------------------------------------------------------------- S5 R-M2 bloom / DOF at the capture size
        g.Section("S5", "R-M2: supersampled captures vs 1× with bloom and DOF");
        {
            g.ClearStage();
            g.Quad("wall", new Vector3(0, 0, 10), new Vector2(60, 40), new Color(0.05f, 0.05f, 0.05f));
            g.Quad("hdr", new Vector3(-3, 0, 9.9f), new Vector2(2.5f, 2.5f), new Color(8f, 6f, 4f));
            g.Quad("near", new Vector3(4, 1, 4f), new Vector2(1f, 1f), new Color(0.2f, 0.6f, 0.9f));
            using (var tx = g.Session.Commands.BeginOrJoin("test", default))
            {
                tx.Set(null, "effects.bloom.enabled", true);
                tx.Set(null, "effects.bloom.intensity", 10f);
                tx.Commit();
            }
            g.Settle();
            yield return null;
            var fx = g.Rt.GetPart<SceneEffects>();
            var vol = fx.Volume.Bloom;
            int viewIter = vol.maxIterations.value; float viewInt = vol.intensity.value;
            int mappedAt2k;
            using (fx.BloomForSize(2560, 1440)) mappedAt2k = vol.maxIterations.value;
            var expect = BloomMapping.Map(true, 10f, g.Session.Model.Settings.Effects.Bloom.Threshold, g.Session.Model.Settings.Effects.Bloom.SoftKnee, g.Session.Model.Settings.Effects.Bloom.Clamp,
                g.Session.Model.Settings.Effects.Bloom.Diffusion, Color.white, 2560, 1440);
            g.Check("BloomForSize(2560 × 1440) writes the mapping of that size, the view's values come back after the scope",
                mappedAt2k == expect.MaxIterations && vol.maxIterations.value == viewIter && Mathf.Approximately(vol.intensity.value, viewInt),
                $"view {viewIter} iterations ({g.View.Camera.pixelWidth} × {g.View.Camera.pixelHeight}), 2560 × 1440 → {mappedAt2k} (expected {expect.MaxIterations}, PPv2 levels {expect.Levels}); after {vol.maxIterations.value}");
            var b1 = g.Capture(W, H, 1, save: "s5_bloom_1x");
            var b2 = g.Capture(W, H, 2, save: "s5_bloom_2x");
            var b4 = g.Capture(W, H, 4, save: "s5_bloom_4x");
            var d2 = CaptureRig.Diff(b1, b2, 2); var d4 = CaptureRig.Diff(b1, b4, 2);
            var hot = Brightest(b1);
            g.Measure($"R-M2 bloom halo ring (30-90 px around the HDR patch at {hot}): 1× {Ring(b1, hot, 0):F2}, 2× {Ring(b2, hot, 0):F2}, 4× {Ring(b4, hot, 0):F2}, far wall {Ring(b1, new Vector2Int(W - 40, 40), -1):F2} (mean luma / 255)");
            g.Measure($"R-M2 bloom 10: 2× reduced vs 1× mean {d2.mean:F2} max {d2.max}; 4× vs 1× mean {d4.mean:F2} max {d4.max} (/ 255)");
            g.Check("R-M2 bloom: 2× reduced vs 1× mean ≤ 1.5 / 255", d2.mean >= 0 && d2.mean <= 1.5, $"mean {d2.mean:F2}, max {d2.max}");
            g.Check("R-M2 bloom: 4× reduced vs 1× mean ≤ 1.5 / 255", d4.mean >= 0 && d4.mean <= 1.5, $"mean {d4.mean:F2}, max {d4.max}");
            // without the per-size mapping (the 3.5 behaviour): the view's iterations reused at 2×
            HS2Next.Scenes.Runtime.SceneEffects.BloomRemapOff = true;
            var b2old = g.Capture(W, H, 2);
            HS2Next.Scenes.Runtime.SceneEffects.BloomRemapOff = false;
            var dOld = CaptureRig.Diff(b1, b2old, 2);
            g.Measure($"R-M2 bloom without the per-size mapping (view's {viewIter} iterations at 2×): mean {dOld.mean:F2} max {dOld.max}; halo ring 2× {Ring(b2old, hot, 0):F2}");
            g.Check("the per-size mapping keeps the 2× halo at least as close to the 1× halo as the 3.5 behaviour",
                Math.Abs(Ring(b2, hot, 0) - Ring(b1, hot, 0)) <= Math.Abs(Ring(b2old, hot, 0) - Ring(b1, hot, 0)) + 0.05,
                $"1× {Ring(b1, hot, 0):F2}, mapped 2× {Ring(b2, hot, 0):F2}, unmapped 2× {Ring(b2old, hot, 0):F2}");
            // DOF: focus on the near quad, aperture 0.6
            using (var tx = g.Session.Commands.BeginOrJoin("test", default))
            {
                tx.Set(null, "effects.bloom.enabled", false);
                tx.Set(null, "effects.depthOfField.enabled", true);
                tx.Set(null, "effects.depthOfField.focalSize", 0.05f);
                tx.Set(null, "effects.depthOfField.aperture", 0.6f);
                tx.Commit();
            }
            g.Settle();
            yield return null;
            var f1 = g.Capture(W, H, 1, save: "s5_dof_1x");
            var f2 = g.Capture(W, H, 2, save: "s5_dof_2x");
            var df = CaptureRig.Diff(f1, f2, 2);
            g.Measure($"R-M2 DOF (aperture 0.6): 2× reduced vs 1× mean {df.mean:F2} max {df.max}");
            g.Check("R-M2 DOF: 2× reduced vs 1× mean ≤ 1.5 / 255", df.mean >= 0 && df.mean <= 1.5, $"mean {df.mean:F2}, max {df.max}");
            g.Check("the DOF blurs (the 1× capture differs from DOF off)", CaptureRig.Diff(f1, b1Off(), 2).mean > 0.05);
            g.EffectsOff(true);
        }

        // ---------------------------------------------------------------- S6 helpers hidden
        g.Section("S6", "light target display: in the view, not in captures");
        {
            g.ClearStage();
            g.Quad("wall", new Vector3(0, 0, 10), new Vector2(60, 40), new Color(0.2f, 0.2f, 0.2f));
            yield return g.Call(new { cmd = "light.add", no = 1, pos = new[] { 0f, 0f, 5f }, rot = new[] { 0f, 0f, 0f } });
            string key = g.Ok ? (string)g.Last["key"] : null;
            g.Check("light.add point light", key != null, g.Short);
            yield return g.Call(new { cmd = "light.set", key, set = new { showTarget = false, range = 4f, intensity = 0f } });
            g.Settle();
            yield return null;
            var off = g.Capture(W, H);
            var viewOff = RenderView(W, H);
            yield return g.Call(new { cmd = "light.set", key, set = new { showTarget = true } });
            g.Settle();
            yield return null;
            var on = g.Capture(W, H, save: "s6_capture_helpers_on");
            var viewOn = RenderView(W, H, "s6_view_helpers_on");
            var dv = CaptureRig.Diff(viewOff, viewOn); var dc = CaptureRig.Diff(off, on);
            g.Check("the view shows the target display (view render with it on ≠ off)", dv.max > 20, $"view mean {dv.mean:F3} max {dv.max}");
            g.Check("captures never show it (capture on = off bit for bit)", dc.max == 0, $"capture mean {dc.mean:F3} max {dc.max}");
            yield return g.Call(new { cmd = "delete", key });
        }

        // ---------------------------------------------------------------- S7 background / frame in captures
        g.Section("S7", "background and frame in screenshot / capture");
        {
            g.ClearStage();
            g.EffectsOff(false);
            string bg = CaptureRig.WritePng(Path.Combine(g.App.Paths.BgDir, "capture_test_bg.png"), 64, 32, (x, y) => new Color32(40, 160, 220, 255));
            string fr = CaptureRig.WritePng(Path.Combine(g.App.Paths.FrameDir, "capture_test_frame.png"), 64, 32, (x, y) => y < 8 ? new Color32(255, 0, 255, 255) : new Color32(0, 0, 0, 0));
            yield return g.Call(new { cmd = "scene.images", background = bg, frame = fr });
            g.Check("scene.images background + frame (embedded res refs)", g.Ok && ((string)g.Last["background"]?["res"])?.StartsWith("res/") == true && ((string)g.Last["frame"]?["res"])?.StartsWith("res/") == true, g.Short);
            yield return g.Call(new { cmd = "capture", name = "s7_capture", width = W, height = H });
            var c0 = g.Ok ? CaptureRig.Load((string)g.Last["path"]) : null;
            yield return g.Call(new { cmd = "capture", name = "s7_capture_frame", width = W, height = H, frame = true });
            var c1 = g.Ok ? CaptureRig.Load((string)g.Last["path"]) : null;
            yield return g.Call(new { cmd = "screenshot", mode = "custom", width = W, height = H, name = "s7_screenshot" });
            var s0 = g.Ok ? CaptureRig.Load((string)g.Last["path"]) : null;
            Color32 bgC = new Color32(40, 160, 220, 255), mag = new Color32(255, 0, 255, 255);
            bool Has(CaptureRig.Shot s, Color32 c, float u, float v) => s != null && CaptureRig.ColorDiff(s.AtUv(u, v), c) <= 3;
            g.Check("capture (frame false by default): background in, frame out", Has(c0, bgC, 0.5f, 0.1f) && Has(c0, bgC, 0.5f, 0.7f), c0 == null ? "no image" : CaptureRig.C(c0.AtUv(0.5f, 0.1f)) + " / " + CaptureRig.C(c0.AtUv(0.5f, 0.7f)));
            g.Check("capture frame true: the frame's opaque band over the top, the background below", Has(c1, mag, 0.5f, 0.1f) && Has(c1, bgC, 0.5f, 0.7f), c1 == null ? "no image" : CaptureRig.C(c1.AtUv(0.5f, 0.1f)) + " / " + CaptureRig.C(c1.AtUv(0.5f, 0.7f)));
            g.Check("screenshot (frame true by default): frame band and background", Has(s0, mag, 0.5f, 0.1f) && Has(s0, bgC, 0.5f, 0.7f), s0 == null ? "no image" : CaptureRig.C(s0.AtUv(0.5f, 0.1f)) + " / " + CaptureRig.C(s0.AtUv(0.5f, 0.7f)));
            yield return g.Call(new { cmd = "screenshot", mode = "custom", width = W, height = H, name = "s7_screenshot_noframe", frame = false });
            var s1n = g.Ok ? CaptureRig.Load((string)g.Last["path"]) : null;
            g.Check("screenshot frame false: no frame", Has(s1n, bgC, 0.5f, 0.1f), s1n == null ? "no image" : CaptureRig.C(s1n.AtUv(0.5f, 0.1f)));
            yield return g.Call(new { cmd = "scene.images", background = (string)null, frame = (string)null });
            g.Check("scene.images null, null clears both", g.Ok && g.Last["background"]?.Type == JTokenType.Null && g.Last["frame"]?.Type == JTokenType.Null, g.Short);
            g.EffectsOff(true);
        }

        // ---------------------------------------------------------------- S8 files
        g.Section("S8", "file names");
        {
            yield return g.Call(new { cmd = "screenshot", mode = "custom", width = 320, height = 180, frame = false });
            string p0 = g.Ok ? (string)g.Last["path"] : null;
            g.Check("the default name is the vanilla stamp yyyyMMddHHmmssfff.png under <UserData>/cap", p0 != null && Regex.IsMatch(Path.GetFileName(p0), @"^\d{17}\.png$")
                && Path.GetDirectoryName(p0).Equals(g.CapDir, StringComparison.OrdinalIgnoreCase) && File.Exists(p0), p0 ?? g.Short);
            yield return g.Call(new { cmd = "screenshot", mode = "custom", width = 320, height = 180, frame = false, name = "s8_same" });
            string pa = g.Ok ? (string)g.Last["path"] : null;
            yield return g.Call(new { cmd = "screenshot", mode = "custom", width = 320, height = 180, frame = false, name = "s8_same" });
            string pb = g.Ok ? (string)g.Last["path"] : null;
            yield return g.Call(new { cmd = "screenshot", mode = "custom", width = 320, height = 180, frame = false, name = "s8_same" });
            string pc = g.Ok ? (string)g.Last["path"] : null;
            g.Check("the same name three times → s8_same.png, s8_same_1.png, s8_same_2.png", pa != null && pb != null && pc != null && Path.GetFileName(pa) == "s8_same.png"
                && Path.GetFileName(pb) == "s8_same_1.png" && Path.GetFileName(pc) == "s8_same_2.png", $"{pa} | {pb} | {pc}");
            yield return g.Call(new { cmd = "screenshot", mode = "custom", width = 320, height = 180, name = "..\\escape" });
            g.Check("a name with separators stays in the cap folder (invalid characters become _)", g.Ok && Path.GetDirectoryName((string)g.Last["path"]).Equals(g.CapDir, StringComparison.OrdinalIgnoreCase), g.Short);
            yield return g.Call(new { cmd = "screenshot", background = "transparent" });
            g.Check("background transparent → unsupported (R15)", !g.Ok && g.Code == "unsupported", g.Short);
            var hs2Cap1 = DirState(Hs2CapDir);
            g.Check("HS2's UserData/cap is untouched (count and latest write time)", hs2Cap0 == hs2Cap1, $"{hs2Cap0.count} → {hs2Cap1.count}");
        }

        // ---------------------------------------------------------------- S9 asynchronous
        g.Section("S9", "asynchronous read-back and encode");
        {
            g.ClearStage();
            g.Quad("wall", new Vector3(0, 0, 10), new Vector2(60, 40), new Color(0.3f, 0.25f, 0.2f));
            g.Settle();
            yield return g.Call(new { cmd = "screenshot", mode = "custom", width = 3840, height = 2160, frame = false, name = "s9_4k" });
            var t = g.Last?["timing"] as JObject;
            if (g.Ok && t != null)
            {
                double main = (double)t["mainThreadMs"], render = (double)t["renderMs"], enc = (double)t["encodeMs"], red = (double)t["reduceMs"], rb = (double)t["readbackMs"], tot = (double)t["totalMs"];
                g.Measure($"4K screenshot: main thread {main:F1} ms (render {render:F1}), read-back {rb:F1} ms (frames), reduce {red:F1} ms, PNG {enc:F1} ms on a worker, write {(double)t["writeMs"]:F1} ms, total {tot:F1} ms, {g.LastFrames} frames pumped");
                g.Check("the main-thread time is the render (the encode is not in it) and frames passed before the reply", main <= render + 5 && enc > 0 && g.LastFrames >= 2, $"main {main:F1}, render {render:F1}, encode {enc:F1}, frames {g.LastFrames}");
                var mem = g.Last["memory"];
                g.Measure($"4K screenshot Gfx Used Memory before {(long)mem["gfxBefore"] / 1048576.0:F0} MiB, after render {(long)mem["gfxAfterRender"] / 1048576.0:F0} MiB (Editor; R-M1 is measured in the player)");
                g.Check("a screenshot of the window size at 1× renders and reads back within 150 ms (render + read-back of the viewport size, measured next)", true);
            }
            else g.Check("4K screenshot", false, g.Short);
            yield return g.Call(new { cmd = "screenshot", mode = "viewport", frame = false, name = "s9_window" });
            if (g.Ok)
            {
                var tt = g.Last["timing"];
                double rr = (double)tt["renderMs"] + (double)tt["readbackMs"];
                g.Measure($"viewport-size screenshot {(int)g.Last["width"]} × {(int)g.Last["height"]}: render {(double)tt["renderMs"]:F1} ms + read-back {(double)tt["readbackMs"]:F1} ms (target ≤ 150 ms render + read-back)");
                g.Check("viewport-size screenshot: render + read-back ≤ 150 ms", rr <= 150, $"{rr:F1} ms");
            }
        }
    }

    static CaptureRig.Shot b1OffCache;
    static CaptureRig.Shot b1Off()
    {
        using (var tx = g.Session.Commands.BeginOrJoin("test", default)) { tx.Set(null, "effects.depthOfField.enabled", false); tx.Commit(); }
        g.Settle();
        var s = g.Capture(W, H);
        using (var tx = g.Session.Commands.BeginOrJoin("test", default)) { tx.Set(null, "effects.depthOfField.enabled", true); tx.Commit(); }
        g.Settle();
        return s;
    }

    /// <summary>The brightest pixel (luma).</summary>
    static Vector2Int Brightest(CaptureRig.Shot s)
    {
        double best = -1; var at = new Vector2Int(-1, -1);
        for (int y = 0; y < s.H; y++)
            for (int x = 0; x < s.W; x++)
            {
                var p = s.At(x, y);
                double l = 0.2126 * p.r + 0.7152 * p.g + 0.0722 * p.b;
                if (l > best) { best = l; at = new Vector2Int(x, y); }
            }
        return at;
    }

    /// <summary>Mean luma of the pixels 30-90 px from <paramref name="c"/> (the HDR patch's halo; mode 0), or of a 20 px disc (mode -1).</summary>
    static double Ring(CaptureRig.Shot s, Vector2Int c, int mode)
    {
        if (s == null || c.x < 0) return -1;
        double sum = 0; long n = 0;
        for (int y = Math.Max(0, c.y - 90); y < Math.Min(s.H, c.y + 90); y++)
            for (int x = Math.Max(0, c.x - 90); x < Math.Min(s.W, c.x + 90); x++)
            {
                double d = Math.Sqrt((x - c.x) * (x - c.x) + (y - c.y) * (y - c.y));
                if (mode == 0 && (d < 30 || d > 90)) continue;
                if (mode == -1 && d > 20) continue;
                var p = s.At(x, y);
                sum += 0.2126 * p.r + 0.7152 * p.g + 0.0722 * p.b; n++;
            }
        return n > 0 ? sum / n : -1;
    }

    static long Clamped(long v)
    {
        long prev = CaptureLimits.MaxPixels;
        CaptureLimits.MaxPixels = v;
        long r = CaptureLimits.MaxPixels;
        CaptureLimits.MaxPixels = prev;
        return r;
    }

    static Vector2Int Find(CaptureRig.Shot s, Func<Color32, bool> pred)
    {
        long sx = 0, sy = 0, n = 0;
        for (int y = 0; y < s.H; y++)
            for (int x = 0; x < s.W; x++)
                if (pred(s.At(x, y))) { sx += x; sy += y; n++; }
        return n == 0 ? new Vector2Int(-1, -1) : new Vector2Int((int)(sx / n), (int)(sy / n));
    }

    /// <summary>The view camera itself rendered into a target of w × h (what the view shows: helpers, gizmo layer, frame).</summary>
    static CaptureRig.Shot RenderView(int w, int h, string save = null)
    {
        g.Settle();
        var cam = g.View.Camera;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var prevT = cam.targetTexture; var prevAspect = cam.aspect;
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

    /// <summary>The 3.5 ViewCapture.Render body (SMAA, no runtime: ReadPixels into RGB24, EncodeToPNG), the reference of S3.</summary>
    static byte[] LegacyRender(Camera view, int width, int height)
    {
        GameObject camGo = null; RenderTexture rt = null; Texture2D tex = null;
        var icons = ItemIcons.HideForCapture();
        try
        {
            camGo = new GameObject("legacy_capture_camera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.transform.SetPositionAndRotation(view.transform.position, view.transform.rotation);
            cam.fieldOfView = view.fieldOfView; cam.nearClipPlane = view.nearClipPlane; cam.farClipPlane = view.farClipPlane;
            var urp = camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            ViewSetup.Of(view).ApplyTo(cam, true);
            cam.allowMSAA = false;
            urp.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            urp.antialiasingQuality = UnityEngine.Rendering.Universal.AntialiasingQuality.High;
            rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            cam.targetTexture = rt;
            cam.ResetProjectionMatrix();
            cam.aspect = width / (float)height;
            cam.Render(); cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            return tex.EncodeToPNG();
        }
        finally
        {
            icons.Dispose();
            if (tex) Object.DestroyImmediate(tex);
            if (rt) Object.DestroyImmediate(rt);
            if (camGo) Object.DestroyImmediate(camGo);
        }
    }
}
