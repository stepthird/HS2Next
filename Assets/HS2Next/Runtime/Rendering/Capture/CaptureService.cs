// Screenshots and bridge captures (Phase 3.6 package C). One render path for the
// user's screenshot (F11, the dialog, the bridge `screenshot`) and the test capture (`capture`, ViewCapture):
//   1. size: the output W × H and the supersampling factor k (1 / 2 / 4); the render is kW × kH and must pass the pixel
//      cap (CaptureLimits: default 16,588,800 = 3840 × 2160 × 2, at most 33,177,600 = 7680 × 4320 behind the option
//      capture.maxPixels; each side ≤ 8192 and ≤ SystemInfo.maxTextureSize; above 16.6 Mpix the warning
//      capture.largeRender);
//   2. a hidden camera copies the view camera's pose, vertical FOV and clip planes, aspect = W / H, the view's ViewSetup
//      for capture (helper layer masked, HDR, post-processing, SMAA High; `aa` msaa4 / none for the bridge's capture);
//      inside the render: item gimmick icons and light helpers hidden (ItemIcons / LightHelpers.HideForCapture), bloom
//      mapped for the render size (SceneEffects.BloomForSize, restored afterwards), the background drawn
//      (HS2BackgroundFeature.CaptureScope) and the frame when asked (HS2FrameFeature.CaptureScope); gizmos, UI, route
//      lines and the pause badge never reach a capture camera (they draw on the view camera or the overlay);
//   3. an sRGB ARGB32 target (24-bit depth; MSAA 4 for msaa4) rendered twice (the first render after the setup can be
//      empty), read back with AsyncGPUReadback (RGBA8, rows bottom-up as ReadPixels gives them);
//   4. k > 1: a box filter over k × k pixels in linear light (the sRGB bytes decoded, averaged, encoded back);
//   5. RGB 8-bit PNG by ImageConversion.EncodeArrayToPNG (thread-safe), the file written.
// RenderSync does 3–5 on the calling thread (edit-mode tests, `capture`). RenderAsync renders on the main thread and
// returns at once; the read-back completes a few frames later (main thread), 4–5 and the write run on a worker, and
// the callback is posted back to the main thread (CaptureDriver; play mode and the player only). Main thread, graphics
// device needed. Built-in is never used. A caller that passes a CaptureEvents.Pending gets its Starting raised right
// before the render (after the request's checks) and its Rendered right after it (also when it threw); the caller raises
// Finished.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using HS2Next.Scenes.Runtime;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace HS2Next.Screenshots
{
    /// <summary>The pixel cap and the size rules (pure).</summary>
    public static class CaptureLimits
    {
        /// <summary>The default cap: 3840 × 2160 × 2 (R12).</summary>
        public const long DefaultMaxPixels = 16_588_800;
        /// <summary>The cap R-M1 falls back to when 16.6 Mpix exceeds 7 GB on an 8 GB card: 4K × 1.5.</summary>
        public const long ReducedMaxPixels = 12_441_600;
        /// <summary>The largest setting: 7680 × 4320 (8K).</summary>
        public const long HardMaxPixels = 33_177_600;
        /// <summary>Renders above this warn (capture.largeRender).</summary>
        public const long WarnPixels = 16_588_800;
        public const int MinSide = 16, MaxSide = 8192;
        public const string LargeRenderWarning = "capture.largeRender: a render above 16.6 Mpix may exceed the GPU memory of an 8 GB card";

        static long maxPixels = DefaultMaxPixels;

        /// <summary>The cap in force (option capture.maxPixels; clamped to [1 Mpix … HardMaxPixels]).</summary>
        public static long MaxPixels
        {
            get => maxPixels;
            set => maxPixels = Math.Max(1_000_000, Math.Min(HardMaxPixels, value <= 0 ? DefaultMaxPixels : value));
        }

        /// <summary>Supersampling factors offered (R14).</summary>
        public static bool ValidSupersample(int k) => k == 1 || k == 2 || k == 4;

        /// <summary>
        /// Null when an output of <paramref name="w"/> × <paramref name="h"/> at supersampling <paramref name="k"/> may be
        /// rendered under <paramref name="cap"/> (0 = <see cref="MaxPixels"/>), else the reason (the bad_args message).
        /// </summary>
        public static string Check(int w, int h, int k, long cap, out long renderPixels)
        {
            if (cap <= 0) cap = MaxPixels;
            renderPixels = (long)w * h * k * k;
            if (w < MinSide || h < MinSide || w > MaxSide || h > MaxSide) return $"each side must be {MinSide}-{MaxSide} (got {w} × {h})";
            if (!ValidSupersample(k)) return "supersample must be 1, 2 or 4 (got " + k + ")";
            int maxTex = MaxTexture;
            if ((long)w * k > maxTex || (long)h * k > maxTex) return $"the render {w * k} × {h * k} exceeds the GPU's texture limit {maxTex}";
            if (renderPixels > cap)
                return $"the render {w * k} × {h * k} ({renderPixels / 1e6:F1} Mpix) exceeds the pixel cap {cap} ({cap / 1e6:F1} Mpix; capture.maxPixels, at most {HardMaxPixels})";
            return null;
        }

        /// <summary>SystemInfo.maxTextureSize (16384 without a device).</summary>
        public static int MaxTexture
        {
            get
            {
                try { int m = SystemInfo.maxTextureSize; return m > 0 ? m : 16384; }
                catch (Exception) { return 16384; }
            }
        }

        /// <summary>"fit" (R13): the largest size with the viewport's aspect inside the window, × scale.</summary>
        public static Vector2Int Fit(int viewportW, int viewportH, int windowW, int windowH, float scale)
        {
            if (viewportW <= 0 || viewportH <= 0) { viewportW = Math.Max(1, windowW); viewportH = Math.Max(1, windowH); }
            if (windowW <= 0 || windowH <= 0) { windowW = viewportW; windowH = viewportH; }
            double a = (double)viewportW / viewportH;
            double w, h;
            if ((double)windowW / windowH > a) { h = windowH; w = h * a; }
            else { w = windowW; h = w / a; }
            float s = Mathf.Clamp(scale, 1f, 4f);
            return new Vector2Int(Math.Max(1, (int)Math.Round(w * s)), Math.Max(1, (int)Math.Round(h * s)));
        }

        /// <summary>"viewport": the viewport's pixel size × scale.</summary>
        public static Vector2Int Viewport(int viewportW, int viewportH, float scale)
        {
            float s = Mathf.Clamp(scale, 1f, 4f);
            return new Vector2Int(Math.Max(1, (int)Math.Round(viewportW * s)), Math.Max(1, (int)Math.Round(viewportH * s)));
        }
    }

    /// <summary>What to render.</summary>
    public sealed class CaptureRequest
    {
        /// <summary>The output size (the PNG's).</summary>
        public int Width = 1280, Height = 720;
        /// <summary>1, 2 or 4: the render is k × the output, reduced by a linear-light box filter.</summary>
        public int Supersample = 1;
        /// <summary>Draw the scene's frame image over the render (screenshots: yes by default; `capture`: no).</summary>
        public bool Frame;
        /// <summary>"smaa" (SMAA High), "msaa4" or "none" (the bridge capture's choices).</summary>
        public string Aa = CaptureService.Smaa;
        /// <summary>The cap for this request (0 = CaptureLimits.MaxPixels).</summary>
        public long MaxPixels;
    }

    /// <summary>What a capture produced.</summary>
    public sealed class CaptureResult
    {
        /// <summary>The PNG (RenderSync, or RenderAsync without a path).</summary>
        public byte[] Png;
        /// <summary>The file written (RenderAsync with a path).</summary>
        public string Path;
        public int Width, Height, RenderWidth, RenderHeight;
        public double RenderMs, ReadbackMs, ReduceMs, EncodeMs, WriteMs, TotalMs;
        /// <summary>Main-thread time of the call (render + set-up; RenderAsync returns after it).</summary>
        public double MainThreadMs;
        /// <summary>Unity's "Gfx Used Memory" before and right after the render (bytes; -1 = not available).</summary>
        public long GfxBefore = -1, GfxAfterRender = -1;
        public string Error;
        public readonly List<string> Warnings = new List<string>();
        public bool Ok => Error == null;
        public double Megapixels => RenderWidth * (double)RenderHeight / 1e6;
    }

    public static class CaptureService
    {
        public const string Smaa = "smaa", Msaa4 = "msaa4", None = "none";

        /// <summary>True when a graphics device exists (headless runs answer unsupported).</summary>
        public static bool Available => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        /// <summary>Asynchronous captures whose file is not written yet.</summary>
        public static int InFlight => System.Threading.Volatile.Read(ref inFlight);
        static int inFlight;

        /// <summary>Completed captures (diagnostics).</summary>
        public static int Completed { get; private set; }

        /// <summary>Raised on the main thread after a screenshot file was written (the UI's toast).</summary>
        public static event Action<CaptureResult> Saved;

        // ------------------------------------------------------------ render

        sealed class Frame
        {
            public GameObject CamGo;
            public RenderTexture Rt;
            public int W, H, K;
            public CaptureResult Result;
            public Stopwatch Total;

            public void Release()
            {
                if (Rt != null) { Rt.Release(); Object.DestroyImmediate(Rt); Rt = null; }
                if (CamGo != null) { Object.DestroyImmediate(CamGo); CamGo = null; }
            }
        }

        static void Validate(Camera view, CaptureRequest req)
        {
            if (!Available) throw new InvalidOperationException("capture needs a graphics device");
            if (!view) throw new InvalidOperationException("no view camera");
            if (GraphicsSettings.currentRenderPipeline == null) throw new InvalidOperationException("no render pipeline (URP) is active");
            if (req == null) throw new ArgumentNullException(nameof(req));
            string why = CaptureLimits.Check(req.Width, req.Height, req.Supersample, req.MaxPixels, out _);
            if (why != null) throw new ArgumentException(why);
        }

        /// <summary>Renders into a new target (main thread); the caller reads it back and releases the frame.</summary>
        static Frame RenderFrame(SceneRuntime rt, Camera view, CaptureRequest req, CaptureEvents.Pending events)
        {
            Validate(view, req);
            CaptureEvents.RaiseStarting(events);
            try { return RenderFrameCore(rt, view, req); }
            finally { CaptureEvents.RaiseRendered(events); }
        }

        static Frame RenderFrameCore(SceneRuntime rt, Camera view, CaptureRequest req)
        {
            int k = req.Supersample, w = req.Width * k, h = req.Height * k;
            var f = new Frame { W = req.Width, H = req.Height, K = k, Total = Stopwatch.StartNew() };
            var res = f.Result = new CaptureResult { Width = req.Width, Height = req.Height, RenderWidth = w, RenderHeight = h };
            if ((long)w * h > CaptureLimits.WarnPixels) res.Warnings.Add(CaptureLimits.LargeRenderWarning);
            res.GfxBefore = GfxUsed();
            var sw = Stopwatch.StartNew();
            var icons = ItemIcons.HideForCapture();
            var helpers = LightHelpers.HideForCapture();
            IDisposable bloom = null, bg = null, frame = null;
            try
            {
                bloom = rt?.GetPart<SceneEffects>()?.BloomForSize(w, h);
                bg = HS2BackgroundFeature.CaptureScope();
                frame = HS2FrameFeature.CaptureScope(req.Frame);
                f.CamGo = new GameObject("hs2next_capture_camera") { hideFlags = HideFlags.HideAndDontSave };
                var cam = f.CamGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.transform.SetPositionAndRotation(view.transform.position, view.transform.rotation);
                cam.fieldOfView = view.fieldOfView;
                cam.nearClipPlane = view.nearClipPlane;
                cam.farClipPlane = view.farClipPlane;
                var urp = f.CamGo.AddComponent<UniversalAdditionalCameraData>();
                var setup = ViewSetup.Of(view);
                if (setup != null) setup.ApplyTo(cam, true);
                else
                {
                    cam.cullingMask = view.cullingMask;
                    cam.clearFlags = view.clearFlags;
                    cam.backgroundColor = view.backgroundColor;
                    cam.allowHDR = true;
                    urp.renderPostProcessing = true;
                    if (view.TryGetComponent<UniversalAdditionalCameraData>(out var vd)) { urp.volumeLayerMask = vd.volumeLayerMask; urp.renderShadows = vd.renderShadows; }
                }
                string aa = req.Aa ?? Smaa;
                cam.allowMSAA = aa == Msaa4;
                urp.antialiasing = aa == Smaa ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
                urp.antialiasingQuality = AntialiasingQuality.High;
                f.Rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = aa == Msaa4 ? 4 : 1, name = "hs2next_capture" };
                f.Rt.Create();
                cam.targetTexture = f.Rt;
                cam.ResetProjectionMatrix();
                cam.aspect = w / (float)h;
                // the mosaic's block floor follows the output size, not the supersampled render (5.M)
                using (HS2Next.Chara.Mosaic.MosaicCamera.OutputScope(cam, k))
                {
                    cam.Render();
                    cam.Render();
                }
                cam.targetTexture = null;
                res.GfxAfterRender = GfxUsed();
            }
            catch
            {
                f.Release();
                throw;
            }
            finally
            {
                frame?.Dispose();
                bg?.Dispose();
                bloom?.Dispose();
                helpers.Dispose();
                icons.Dispose();
            }
            res.RenderMs = sw.Elapsed.TotalMilliseconds;
            return f;
        }

        /// <summary>Renders, reads back, reduces and encodes on the calling thread; returns the PNG in the result.</summary>
        public static CaptureResult RenderSync(SceneRuntime rt, Camera view, CaptureRequest req, CaptureEvents.Pending events = null)
        {
            var f = RenderFrame(rt, view, req, events);
            var res = f.Result;
            try
            {
                var sw = Stopwatch.StartNew();
                var rq = AsyncGPUReadback.Request(f.Rt, 0, TextureFormat.RGBA32);
                rq.WaitForCompletion();
                if (rq.hasError) throw new InvalidOperationException("the GPU read-back failed");
                var rgba = rq.GetData<byte>().ToArray();
                res.ReadbackMs = sw.Elapsed.TotalMilliseconds;
                res.MainThreadMs = res.RenderMs + res.ReadbackMs;
                f.Release();
                res.Png = Encode(rgba, f, res);
            }
            finally { f.Release(); }
            res.TotalMs = f.Total.Elapsed.TotalMilliseconds;
            Completed++;
            return res;
        }

        /// <summary>
        /// Renders now (main thread) and finishes asynchronously: read-back (main thread, a later frame), reduction, PNG and
        /// the write to <paramref name="path"/> (null = keep the PNG in the result) on a worker; <paramref name="done"/> runs on
        /// the main thread with the result (Error set on failure). Falls back to <see cref="RenderSync"/> outside play mode.
        /// Throws for bad requests (before anything is rendered).
        /// </summary>
        public static void RenderAsync(SceneRuntime rt, Camera view, CaptureRequest req, string path, Action<CaptureResult> done, CaptureEvents.Pending events = null)
        {
            if (!Application.isPlaying)
            {
                var r = RenderSync(rt, view, req, events);
                if (path != null) WriteFile(path, r);
                done?.Invoke(r);
                if (path != null && r.Ok) RaiseSaved(r);
                return;
            }
            var f = RenderFrame(rt, view, req, events);
            var res = f.Result;
            res.MainThreadMs = res.RenderMs;
            System.Threading.Interlocked.Increment(ref inFlight);
            var sw = Stopwatch.StartNew();
            CaptureDriver.Ensure();
            try
            {
                AsyncGPUReadback.Request(f.Rt, 0, TextureFormat.RGBA32, rq =>
                {
                    byte[] rgba = null;
                    try
                    {
                        if (rq.hasError) res.Error = "the GPU read-back failed";
                        else rgba = rq.GetData<byte>().ToArray();
                    }
                    catch (Exception e) { res.Error = "read-back: " + e.Message; }
                    res.ReadbackMs = sw.Elapsed.TotalMilliseconds;
                    f.Release();
                    if (res.Error != null) { Finish(res, f, done, path); return; }
                    Task.Run(() =>
                    {
                        try
                        {
                            res.Png = Encode(rgba, f, res);
                            if (path != null) WriteFile(path, res);
                        }
                        catch (Exception e) { res.Error = e.GetType().Name + ": " + e.Message; }
                        CaptureDriver.Post(() => Finish(res, f, done, path));
                    });
                });
            }
            catch (Exception e)
            {
                f.Release();
                res.Error = "read-back request: " + e.Message;
                Finish(res, f, done, path);
            }
        }

        static void Finish(CaptureResult res, Frame f, Action<CaptureResult> done, string path)
        {
            System.Threading.Interlocked.Decrement(ref inFlight);
            res.TotalMs = f.Total.Elapsed.TotalMilliseconds;
            Completed++;
            try { done?.Invoke(res); }
            catch (Exception e) { Debug.LogWarning("[capture] completion handler threw: " + e); }
            if (path != null && res.Ok) RaiseSaved(res);
        }

        static void RaiseSaved(CaptureResult r)
        {
            try { Saved?.Invoke(r); }
            catch (Exception e) { Debug.LogWarning("[capture] Saved handler threw: " + e); }
        }

        static void WriteFile(string path, CaptureResult res)
        {
            if (res.Png == null) return;
            var sw = Stopwatch.StartNew();
            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, res.Png);
            res.Path = path;
            res.WriteMs = sw.Elapsed.TotalMilliseconds;
        }

        // ------------------------------------------------------------ reduce and encode (any thread)

        static byte[] Encode(byte[] rgba, Frame f, CaptureResult res)
        {
            var sw = Stopwatch.StartNew();
            byte[] rgb = f.K == 1 ? DropAlpha(rgba, f.W, f.H) : BoxReduce(rgba, f.W * f.K, f.H * f.K, f.K);
            res.ReduceMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var png = ImageConversion.EncodeArrayToPNG(rgb, GraphicsFormat.R8G8B8_SRGB, (uint)f.W, (uint)f.H, (uint)(f.W * 3));
            res.EncodeMs = sw.Elapsed.TotalMilliseconds;
            return png;
        }

        /// <summary>RGBA8 rows → RGB8 rows (same order).</summary>
        public static byte[] DropAlpha(byte[] rgba, int w, int h)
        {
            var o = new byte[w * h * 3];
            for (int i = 0, j = 0, n = w * h; i < n; i++, j += 3)
            {
                int s = i * 4;
                o[j] = rgba[s]; o[j + 1] = rgba[s + 1]; o[j + 2] = rgba[s + 2];
            }
            return o;
        }

        static float[] s2l;
        static byte[] l2s;
        const int L2SSize = 16384;

        static void Tables()
        {
            if (s2l != null) return;
            var a = new float[256];
            for (int i = 0; i < 256; i++) { double s = i / 255.0; a[i] = (float)(s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4)); }
            var b = new byte[L2SSize + 1];
            for (int i = 0; i <= L2SSize; i++)
            {
                double l = i / (double)L2SSize;
                double s = l <= 0.0031308 ? l * 12.92 : 1.055 * Math.Pow(l, 1 / 2.4) - 0.055;
                b[i] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(s * 255.0)));
            }
            l2s = b;
            s2l = a;
        }

        /// <summary>The sRGB byte of a linear value (table, 1 / 16384 steps).</summary>
        public static byte LinearToSrgb(float l) { Tables(); return l2s[(int)(Mathf.Clamp01(l) * L2SSize + 0.5f)]; }

        /// <summary>The linear value of an sRGB byte.</summary>
        public static float SrgbToLinear(byte s) { Tables(); return s2l[s]; }

        /// <summary>
        /// A k × k box filter in linear light: RGBA8 sRGB rows of <paramref name="w"/> × <paramref name="h"/> (multiples of
        /// k) → RGB8 sRGB rows of (w / k) × (h / k). Alpha is ignored.
        /// </summary>
        public static byte[] BoxReduce(byte[] rgba, int w, int h, int k)
        {
            Tables();
            int ow = w / k, oh = h / k;
            var o = new byte[ow * oh * 3];
            float inv = 1f / (k * k);
            var lut = s2l;
            Parallel.For(0, oh, oy =>
            {
                for (int ox = 0; ox < ow; ox++)
                {
                    float r = 0, g = 0, b = 0;
                    for (int dy = 0; dy < k; dy++)
                    {
                        int row = ((oy * k + dy) * w + ox * k) * 4;
                        for (int dx = 0; dx < k; dx++)
                        {
                            int s = row + dx * 4;
                            r += lut[rgba[s]]; g += lut[rgba[s + 1]]; b += lut[rgba[s + 2]];
                        }
                    }
                    int j = (oy * ow + ox) * 3;
                    o[j] = l2s[(int)(Math.Min(1f, r * inv) * L2SSize + 0.5f)];
                    o[j + 1] = l2s[(int)(Math.Min(1f, g * inv) * L2SSize + 0.5f)];
                    o[j + 2] = l2s[(int)(Math.Min(1f, b * inv) * L2SSize + 0.5f)];
                }
            });
            return o;
        }

        // ------------------------------------------------------------ memory

        static Unity.Profiling.ProfilerRecorder gfxRecorder;
        static bool gfxTried;

        /// <summary>Unity's "Gfx Used Memory" counter in bytes, or -1 when the player does not provide it.</summary>
        public static long GfxUsed()
        {
            try
            {
                if (!gfxTried)
                {
                    gfxTried = true;
                    gfxRecorder = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "Gfx Used Memory");
                }
                if (gfxRecorder.Valid) return gfxRecorder.CurrentValue;
            }
            catch (Exception) { }
            return -1;
        }

        // ------------------------------------------------------------ files

        /// <summary>The vanilla file name for a moment: yyyyMMddHHmmssfff (local time).</summary>
        public static string StampName(DateTime local) => local.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);

        static readonly HashSet<string> reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// &lt;dir&gt;/&lt;name&gt;.png, or &lt;name&gt;_1.png, _2 … when that file exists or another capture in flight has it (R23).
        /// The name is reserved until <see cref="ReleaseName"/>.
        /// </summary>
        public static string UniquePath(string dir, string name)
        {
            lock (reserved)
            {
                string p = System.IO.Path.Combine(dir, name + ".png");
                for (int n = 1; File.Exists(p) || reserved.Contains(p); n++) p = System.IO.Path.Combine(dir, name + "_" + n + ".png");
                reserved.Add(p);
                return p;
            }
        }

        public static void ReleaseName(string path)
        {
            if (path == null) return;
            lock (reserved) reserved.Remove(path);
        }
    }
}
