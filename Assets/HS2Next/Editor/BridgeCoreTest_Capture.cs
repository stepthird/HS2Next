// C28 of BridgeCoreTest: the Phase 3.6 capture commands of package C (
// a group number of its own so that package R's C27 file and this one never collide), headless: registration
// (CaptureCommands.Names in CoreCommands.Phase36Names), the screenshot / capture argument rules and the pixel cap judged
// before the device (bad_args with the limit, unsupported without a device, transparent reserved), scene.images (shapes,
// one entry "Background" / "Frame", both = one "Background", read without arguments = no entry, embed false = a user
// reference, DefaultData = an hs2 reference, null clears, missing file = not_found, non-image = bad_args, undo / redo
// restore scene.doc). Test images are written by the test (flat colours); no capture is taken or viewed.
using System;
using System.IO;
using System.Linq;
using HS2Next.Screenshots;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class BridgeCoreTest
{
    static void C28(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        var p36 = HS2Next.App.Commands.CoreCommands.Phase36Names;
        var names = HS2Next.App.Commands.CaptureCommands.Names;
        c.Check(names.SequenceEqual(new[] { "screenshot", "scene.images" }) && names.All(n => p36.Contains(n) && reg.Find(n) != null),
            "CaptureCommands.Names (screenshot, scene.images) are in Phase36Names and registered", string.Join(", ", names.Where(n => reg.Find(n) == null)));
        c.Ok(new { cmd = "scene.new" }, "scene.new");
        bool device = CaptureService.Available;

        // ---------------- capture / screenshot argument rules and the cap (before the device)
        c.Fails(new { cmd = "capture", width = 8192, height = 8192 }, "bad_args", "capture 8192 × 8192 (67 Mpix) is above the pixel cap");
        c.Check(c.LastText.Contains("16588800"), "the error names the limit", Ctx.Short(c.LastText));
        c.Fails(new { cmd = "capture", width = 640, height = 360, supersample = 3 }, "bad_args", "capture supersample 3");
        c.Fails(new { cmd = "capture", width = 3000, height = 3000, supersample = 2 }, "bad_args", "capture 3000 × 3000 × 2 (36 Mpix) is above the cap");
        c.Fails(new { cmd = "screenshot", mode = "custom", width = 4096, height = 4096, supersample = 2 }, "bad_args", "screenshot 4096 × 4096 × 2");
        c.Fails(new { cmd = "screenshot", mode = "custom", width = 9000, height = 100 }, "bad_args", "screenshot width 9000");
        c.Fails(new { cmd = "screenshot", mode = "sideways" }, "bad_args", "screenshot mode sideways");
        c.Fails(new { cmd = "screenshot", scale = 5 }, "bad_args", "screenshot scale 5");
        c.Fails(new { cmd = "screenshot", background = "transparent" }, "unsupported", "screenshot background transparent is reserved (R15)");
        if (!device)
        {
            c.Fails(new { cmd = "screenshot", mode = "custom", width = 320, height = 180 }, "unsupported", "screenshot without a graphics device (headless)");
            c.Fails(new { cmd = "capture", width = 320, height = 180 }, "unsupported", "capture without a graphics device (headless)");
        }
        else c.Info("a graphics device is present: the render paths are CaptureTest.render's");
        long prev = CaptureLimits.MaxPixels;
        try
        {
            CaptureLimits.MaxPixels = 12_441_600;
            c.Fails(new { cmd = "capture", width = 3840, height = 3600 }, "bad_args", "with capture.maxPixels 12,441,600: capture 3840 × 3600 (13.8 Mpix) is refused");
            CaptureLimits.MaxPixels = 99_000_000;
            c.Check(CaptureLimits.MaxPixels == CaptureLimits.HardMaxPixels, "capture.maxPixels is clamped to 33,177,600", "" + CaptureLimits.MaxPixels);
        }
        finally { CaptureLimits.MaxPixels = prev; }

        // ---------------- scene.images
        string bgDir = app.Paths.BgDir, frDir = app.Paths.FrameDir;
        Directory.CreateDirectory(bgDir);
        Directory.CreateDirectory(frDir);
        string bg = Path.Combine(bgDir, "c28_bg.png"), fr = Path.Combine(frDir, "c28_frame.png");
        File.WriteAllBytes(bg, FlatPng(new Color32(10, 120, 200, 255)));
        File.WriteAllBytes(fr, FlatPng(new Color32(255, 255, 255, 64)));
        int H() => (c.Call(new { cmd = "history" })["undo"] as JArray)?.Count ?? -1;
        string Doc() => (string)c.Call(new { cmd = "scene.doc" })["sha256"];
        int h0 = H();
        string d0 = Doc();
        var r = c.Ok(new { cmd = "scene.images" }, "scene.images without arguments reads");
        c.Check(r["background"]?.Type == JTokenType.Null && r["frame"]?.Type == JTokenType.Null && r["images"]?["background"]?["state"]?.ToString() == "none" && H() == h0,
            "a new scene has none, the read records nothing", Ctx.Short(r.ToString()));
        r = c.Ok(new { cmd = "scene.images", background = bg }, "scene.images background");
        string res = (string)r["background"]?["res"];
        c.Check(res != null && res.StartsWith("res/") && r["frame"]?.Type == JTokenType.Null && H() == h0 + 1 && LastLabel(c) == "Background",
            "an absolute UserData path is embedded ({res}), one entry \"Background\"", Ctx.Short(r.ToString()));
        r = c.Ok(new { cmd = "scene.images", frame = fr }, "scene.images frame");
        c.Check(((string)r["frame"]?["res"])?.StartsWith("res/") == true && (string)r["background"]?["res"] == res && H() == h0 + 2 && LastLabel(c) == "Frame",
            "the frame alone: one entry \"Frame\", the background kept", Ctx.Short(r.ToString()));
        r = c.Ok(new { cmd = "scene.images", background = "bg/c28_bg.png", frame = (string)null, embed = false }, "scene.images both, relative path, embed false");
        c.Check((string)r["background"]?["user"] == "bg/c28_bg.png" && r["frame"]?.Type == JTokenType.Null && H() == h0 + 3 && LastLabel(c) == "Background",
            "both in one call: one entry \"Background\"; embed false gives {user: \"bg/…\"}; null clears the frame", Ctx.Short(r.ToString()));
        string hs2 = app.Paths.Hs2Root;
        string defBg = hs2 != null && Directory.Exists(Path.Combine(hs2, "DefaultData", "bg"))
            ? Directory.GetFiles(Path.Combine(hs2, "DefaultData", "bg"), "*.png").OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault() : null;
        if (defBg != null)
        {
            r = c.Ok(new { cmd = "scene.images", background = "hs2:DefaultData/bg/" + Path.GetFileName(defBg) }, "scene.images hs2:DefaultData/bg/…");
            c.Check((string)r["background"]?["hs2"] == "DefaultData/bg/" + Path.GetFileName(defBg), "a DefaultData file is referenced ({hs2}), never embedded", Ctx.Short(r.ToString()));
        }
        else c.Warn("no HS2 DefaultData/bg: the hs2 case is skipped");
        c.Fails(new { cmd = "scene.images", background = Path.Combine(bgDir, "c28_none.png") }, "not_found", "a missing file → not_found");
        string txt = Path.Combine(bgDir, "c28_text.png");
        File.WriteAllText(txt, "text");
        c.Fails(new { cmd = "scene.images", background = txt }, "bad_args", "a non-image → bad_args");
        c.Fails(new { cmd = "scene.images", background = 5 }, "bad_args", "a number → bad_args");
        int hb = H();
        c.Ok(new { cmd = "undo" }, "undo");
        c.Ok(new { cmd = "undo" }, "undo");
        c.Ok(new { cmd = "undo" }, "undo");
        if (defBg != null) c.Ok(new { cmd = "undo" }, "undo");
        c.Check(Doc() == d0, "undo back to the start restores scene.doc", "");
        c.Ok(new { cmd = "redo" }, "redo");
        r = c.Ok(new { cmd = "scene.images" }, "read after redo");
        c.Check((string)r["background"]?["res"] == res, "redo restores the embedded background", Ctx.Short(r.ToString()));
        c.Info("history entries after the edits: " + hb);
    }

    static string LastLabel(Ctx c)
    {
        var u = c.Call(new { cmd = "history" })["undo"] as JArray;
        return u != null && u.Count > 0 ? (string)u[0] : null;
    }

    static byte[] FlatPng(Color32 col)
    {
        var t = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        var a = new Color32[64];
        for (int i = 0; i < a.Length; i++) a[i] = col;
        t.SetPixels32(a);
        t.Apply();
        var png = t.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(t);
        return png;
    }
}
