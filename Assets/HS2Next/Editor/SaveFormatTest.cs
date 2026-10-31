// Batch test of the save format. The checks live in
// Editor/SaveFormat/ as test groups (static classes in namespace HS2Next.Save.Tests with
// `public static void Run(TestRunner t)`; the same files run under plain Mono while the format code
// is developed): InfraTests_* (container, JSON layer, ext, versions, resources, sizes and speed:
// T3, T4, T6-T14, the ext/foreign part of T5, the placeholder part of T16) and DocTests_* (documents:
// T1, T2, the core part of T5) when present. No game data and no Illusion files are used; fixtures
// are synthetic (refs like test.mod#1) or come from Tests/SaveFormat/ in the repository.
// Run: -executeMethod SaveFormatTest.Run [-groups A,B]   headless group (substring filter on group names)
//      -executeMethod SaveFormatTest.RunGraphics         T16 with a graphics device: ThumbnailCapture of a
//      primitive scene and of the female visual target (Tools/targets.json) through the HS2 coordinate shot, then a
//      character file saved with that thumbnail; PNG headers and sizes only (the images are not judged).
// Reports: Spike/out/save/saveformat_report.txt, Spike/out/save/saveformat_graphics_report.txt; first line
// "N passed, M failed"; EditorApplication.Exit(1) when a check fails.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HS2Next.Chara;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.Save;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class SaveFormatTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "save");

    static string Arg(string name, string def)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static HS2Next.Save.Tests.TestRunner NewRunner()
    {
        var t = new HS2Next.Save.Tests.TestRunner
        {
            TempDir = Path.Combine(Path.GetTempPath(), "hs2n_saveformat_" + Process.GetCurrentProcess().Id),
            FixturesDir = Path.Combine(ContentSettings.ProjectRoot, "Tests", "SaveFormat"),
        };
        Directory.CreateDirectory(t.TempDir);
        return t;
    }

    static void Finish(HS2Next.Save.Tests.TestRunner t, string report, StringBuilder body, string title)
    {
        try { Directory.Delete(t.TempDir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        Directory.CreateDirectory(OutDir);
        var sb = new StringBuilder();
        sb.AppendLine($"{t.Passed} passed, {t.Failed} failed");
        sb.AppendLine(title);
        sb.AppendLine($"runtime: Unity {Application.unityVersion}, {Environment.Version}");
        sb.Append(body);
        if (t.Failures.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("FAILURES:");
            foreach (var f in t.Failures) sb.AppendLine("  " + f);
        }
        string path = Path.Combine(OutDir, report);
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        Debug.Log($"SaveFormatTest: {t.Passed} passed, {t.Failed} failed -> {path}");
        if (t.Failed > 0) TestExit.Exit(1);
    }

    /// <summary>Headless group: every test group of Editor/SaveFormat (or those matching -groups).</summary>
    public static void Run()
    {
        var t = NewRunner();
        var filter = Arg("-groups", null)?.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        var groups = typeof(HS2Next.Save.Tests.TestRunner).Assembly.GetTypes()
            .Where(ty => ty.Namespace == "HS2Next.Save.Tests" && ty.IsAbstract && ty.IsSealed)
            .Select(ty => new { ty.Name, M = ty.GetMethod("Run", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(HS2Next.Save.Tests.TestRunner) }, null) })
            .Where(g => g.M != null)
            .OrderBy(g => g.Name, StringComparer.Ordinal)
            .ToList();
        var body = new StringBuilder();
        foreach (var g in groups)
        {
            if (filter != null && !filter.Any(f => g.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
            t.Group = g.Name;
            int before = t.Failed, lines = t.Lines.Count;
            var sw = Stopwatch.StartNew();
            try { g.M.Invoke(null, new object[] { t }); }
            catch (TargetInvocationException e) { t.Check(false, "group threw " + e.InnerException); }
            body.AppendLine($"{(t.Failed == before ? "PASS" : "FAIL")} {g.Name} ({sw.ElapsedMilliseconds} ms{(t.Failed == before ? "" : ", " + (t.Failed - before) + " failed")})");
            for (int i = lines; i < t.Lines.Count; i++) body.AppendLine("   " + t.Lines[i]);
        }
        Finish(t, "saveformat_report.txt", body, "SaveFormatTest headless group");
    }

    // ================================================================ T16 graphics

    static void CheckPng(HS2Next.Save.Tests.TestRunner t, byte[] png, string what)
    {
        var h = PngInfo.ReadHeader(png);
        t.Check(h.HasValue && h.Value.Width == ThumbnailCapture.Width && h.Value.Height == ThumbnailCapture.Height && h.Value.BitDepth == 8 &&
                h.Value.ColorType == 2 && h.Value.Interlace == 0,
            $"T16: {what}: 504x704, bit depth 8, colour type 2 (RGB), no interlace (got {(h.HasValue ? $"{h.Value.Width}x{h.Value.Height} depth {h.Value.BitDepth} type {h.Value.ColorType} interlace {h.Value.Interlace}" : "no valid PNG")}, {png?.Length ?? 0} bytes)");
    }

    /// <summary>Fraction of pixels that differ from the background by more than 8 levels (decoded in memory; never shown or saved).</summary>
    static float Coverage(byte[] png, Color32 bg)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
        try
        {
            tex.LoadImage(png);
            var px = tex.GetPixels32();
            int n = 0;
            foreach (var p in px) if (Math.Abs(p.r - bg.r) > 8 || Math.Abs(p.g - bg.g) > 8 || Math.Abs(p.b - bg.b) > 8) n++;
            return n / (float)px.Length;
        }
        finally { Object.DestroyImmediate(tex); }
    }

    public static void RunGraphics()
    {
        var t = NewRunner();
        t.Group = "T16";
        var body = new StringBuilder();
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            t.Check(false, "T16: needs a graphics device (no -nographics)");
            Finish(t, "saveformat_graphics_report.txt", body, "SaveFormatTest graphics group (T16)");
            return;
        }
        ShaderUtil.allowAsyncCompilation = false;
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HS2Next.App.AppPipeline.MeasureAssetPath);
        var settings = new ThumbnailSettings { Pipeline = urp };
        t.Check(Math.Abs(ThumbnailCapture.CropFov(23f) - 22.502f) < 0.001f, $"T16: crop field of view for 23 deg = {ThumbnailCapture.CropFov(23f):F3} (spec 22.502)");

        // 1. a primitive at the origin, HS2 coordinate shot
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.position = new Vector3(0, 10, 0);
        cube.transform.localScale = new Vector3(4, 8, 4);
        byte[] png;
        try { png = ThumbnailCapture.CaptureCoordinateShot(1, settings); }
        finally { Object.DestroyImmediate(cube); }
        CheckPng(t, png, "primitive scene");
        float cov = Coverage(png, settings.Background);
        body.AppendLine($"primitive: {png.Length} bytes, non-background pixels {cov:P1}");
        t.Check(cov > 0.01f && cov < 0.9f, $"T16: the primitive is in frame and the background is the 128 grey ({cov:P1} non-background)");

        // 2. the female visual target, built as in CompareTest (Studio rest state), placed at the origin facing +Z
        try
        {
            var targets = TestTargets.Load();
            string card = targets.VisualCards.FirstOrDefault();
            if (card == null || !File.Exists(card)) body.AppendLine("character: skipped (no visual target card in this install)");
            else
            {
                var gd = CardResolveTest.BuildDb(new StringBuilder());
                var asm = new CharaAssembler(gd);
                asm.ExtOptions.Abmx = false;
                var root = asm.BuildFromDocuments(card);
                try
                {
                    HS2Next.ShaderMap.ApplyUrp(root);
                    root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    var sw = Stopwatch.StartNew();
                    var chara = ThumbnailCapture.CaptureCoordinateShot(1, settings);
                    long ms = sw.ElapsedMilliseconds;
                    CheckPng(t, chara, "character (coordinate shot)");
                    float cc = Coverage(chara, settings.Background);
                    body.AppendLine($"character: {chara.Length} bytes, capture {ms} ms, non-background pixels {cc:P1}");
                    t.Check(cc > 0.02f, $"T16: the character is in frame ({cc:P1} non-background)");

                    // a character file saved with the rendered thumbnail
                    var c = new CharaContainer();
                    c.Chara.Profile.Name = "thumbnail test";
                    c.Chara.Profile.Sex = 1;
                    string path = Path.Combine(t.TempDir, "thumb.hs2nchara");
                    var r = c.Save(path, new SaveOptions { Thumbnail = ThumbnailMode.Render, Renderer = () => ThumbnailCapture.CaptureCoordinateShot(1, settings), Registry = new ExtRegistry() });
                    t.Check(r.Ok, "T16: save with a rendered thumbnail (" + r + ")");
                    var raw = ContainerReader.Read(path);
                    CheckPng(t, raw.Thumbnail, "thumbnail prefix of the saved file");
                    var m = CharaContainer.Load(path).Value?.LoadedManifest;
                    t.Check(m != null && m.ThumbnailWidth == 504 && m.ThumbnailHeight == 704, "T16: manifest.thumbnail 504 x 704");
                }
                finally { Object.DestroyImmediate(root); }
            }
        }
        catch (Exception e) { t.Check(false, "T16: character capture threw " + e); }

        // 3. the placeholder path never needs graphics
        CheckPng(t, ThumbnailPlaceholder.Png(), "placeholder");
        Finish(t, "saveformat_graphics_report.txt", body, "SaveFormatTest graphics group (T16)");
    }
}
