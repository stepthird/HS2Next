// Screenshot and scene-image commands (Phase 3.6 package C):
//   screenshot    the user-facing path (F11's): <UserData>/cap/<yyyyMMddHHmmssfff>.png (`_1`, `_2` … on a collision,
//                 R23), size by mode fit / viewport / custom × scale, supersample 1 / 2 / 4, the frame in by default; the
//                 pixel cap (bad_args with the limit), capture.largeRender above 16.6 Mpix (a warning); rendered at the late
//                 point of frame F + waitFrames after the scene images finished loading; read back, encoded and written
//                 asynchronously; the reply comes after the file is written. background "transparent" is reserved (R15:
//                 unsupported). HS2's own UserData/cap is never written.
//   scene.images  the scene's background / frame: a path as the pickers accept it (absolute, relative to <UserData>,
//                 "hs2:" + a path relative to the HS2 root) or null; FileRefs.FromPicked (DefaultData referenced, other
//                 files embedded unless embed is false); one entry per call ("Background", "Frame"; both = "Background");
//                 without arguments it reads. Reply: the stored references and the realized state of each image.
// TakeScreenshot is the one entry the UI's F11 / dialog and the bridge share.
using System;
using System.Collections.Generic;
using System.IO;
using HS2Next.Api.Bridge;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Screenshots;
using UnityEngine;

namespace HS2Next.App.Commands
{
    public static class CaptureCommands
    {
        /// <summary>The commands of this file (part of CoreCommands.Phase36Names).</summary>
        public static readonly IReadOnlyList<string> Names = new[] { "screenshot", "scene.images" };

        /// <summary>At most this many screenshots may be in flight (read back / encoded / written) at once.</summary>
        public const int MaxInFlight = 2;

        public static void Register(ICommandRegistry r, AppCore app)
        {
            // the picked-file context of FileRefs (the inspector's image pickers use it through InspectorValues)
            FileRefContext.Provider = () => new FileRefContext
            {
                Hs2Root = app.Services?.Hs2Root ?? app.Paths?.Hs2Root,
                UserData = app.Services?.UserData ?? app.Paths?.UserData,
                Res = app.Session?.Model?.File?.Res,
                Embed = CaptureSettings.Embed,
            };

            CommandKit.Register(r, app, new CommandSpec("screenshot", "Takes a screenshot as F11 does into <UserData>/cap (vanilla name yyyyMMddHHmmssfff.png); the reply comes after the file is written.")
                {
                    Group = "capture",
                    HandlesWaitFrames = true,
                }
                .Arg("name", ArgTypes.String, "file name without extension (default the local time yyyyMMddHHmmssfff; invalid characters become _; _1, _2 … on a collision)")
                .Arg("mode", ArgTypes.Enum(CaptureSettings.Modes), "fit (the viewport's aspect inside the window), viewport, custom (default the setting capture.mode)")
                .Arg("scale", ArgTypes.Float, "1-4 for fit / viewport (default the setting)")
                .Arg("width", ArgTypes.Int, "custom: 16-8192")
                .Arg("height", ArgTypes.Int, "custom: 16-8192")
                .Arg("supersample", ArgTypes.Int, "1, 2 or 4 (default the setting)")
                .Arg("frame", ArgTypes.Bool, "draw the frame image (default the setting, true)")
                .Arg("background", ArgTypes.String, "reserved: \"transparent\" answers unsupported (R15)")
                .Return("path", ArgTypes.String, "absolute PNG path under <UserData>/cap").Return("width", ArgTypes.Int, "").Return("height", ArgTypes.Int, "")
                .Return("renderWidth", ArgTypes.Int, "").Return("renderHeight", ArgTypes.Int, "").Return("megapixels", ArgTypes.Float, "of the render")
                .Return("ms", ArgTypes.Float, "from the request to the written file").Return("imagesPending", ArgTypes.Bool, "the scene images were still loading (after 600 frames)")
                .Return("timing", ArgTypes.Object, "{mainThreadMs, renderMs, readbackMs, reduceMs, encodeMs, writeMs}")
                .Return("memory", ArgTypes.Object, "{gfxBefore, gfxAfterRender} Unity's Gfx Used Memory in bytes (-1 = not available)")
                .WithFlags(CommandFlags.WritesFiles | CommandFlags.Defers).WithTimeout(180), Screenshot);

            CommandKit.Register(r, app, CommandKit.Spec("scene.images", "The scene's background and frame images: sets them from picked paths (one entry) or, without arguments, reads them.", "capture")
                .Arg("background", ArgTypes.String, "absolute, relative to <UserData>, or hs2:<path relative to the HS2 root>; null = none")
                .Arg("frame", ArgTypes.String, "as background")
                .Arg("embed", ArgTypes.Bool, "embed picked files that are not under the HS2 root's DefaultData (default the setting images.embed)")
                .Return("background", ArgTypes.Object, "the stored file reference or null").Return("frame", ArgTypes.Object, "the stored file reference or null")
                .Return("images", ArgTypes.Object, "{background, frame}: {state none|loading|loaded|missing|failed, width, height, source, error}")
                .Return("changed", ArgTypes.ArrayOf(ArgTypes.String), "").Return("noop", ArgTypes.ArrayOf(ArgTypes.String), "")
                .WithFlags(CommandFlags.Mutates).WithTimeout(120), Images);
        }

        // ------------------------------------------------------------ screenshot

        static Camera ViewOf(AppCore app) => app?.Runtime?.GetPart<SceneViewCamera>()?.Camera;

        /// <summary>
        /// Takes a screenshot of the app's view into &lt;UserData&gt;/cap (name: the stamp when null) and calls
        /// <paramref name="done"/> on the main thread once the file is written (Error set on failure). Returns the path
        /// reserved for it. Throws ArgumentException for a request the cap refuses, InvalidOperationException when there is
        /// no view, no device or too many screenshots in flight.
        /// </summary>
        public static string TakeScreenshot(AppCore app, CaptureRequest req, string name, Action<CaptureResult> done)
        {
            if (app?.Paths == null || app.Runtime == null) throw new InvalidOperationException("the app has no scene runtime");
            if (!CaptureService.Available) throw new InvalidOperationException("screenshots need a graphics device");
            var view = ViewOf(app);
            if (!view) throw new InvalidOperationException("no view camera");
            if (CaptureService.InFlight >= MaxInFlight) throw new InvalidOperationException("a screenshot is still being written");
            string why = CaptureLimits.Check(req.Width, req.Height, req.Supersample, req.MaxPixels, out _);
            if (why != null) throw new ArgumentException(why);
            string dir = app.Paths.CapDir;
            Directory.CreateDirectory(dir);
            if (string.IsNullOrEmpty(name)) name = CaptureService.StampName(DateTime.Now);
            foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            string path = CaptureService.UniquePath(dir, name);
            try
            {
                // the output-root guard of the cap folder (rooted names, "..", devices, outside the folder → io_denied)
                path = app.RootFor(dir).Resolve(Path.GetFileName(path), overwrite: false);
                SceneImages.Ensure(app.Runtime);
                string reserved = path;
                CaptureService.RenderAsync(app.Runtime, view, req, path, res =>
                {
                    CaptureService.ReleaseName(reserved);
                    done?.Invoke(res);
                });
            }
            catch
            {
                CaptureService.ReleaseName(path);
                throw;
            }
            return path;
        }

        /// <summary>The request of the bridge arguments over the current settings.</summary>
        static CaptureRequest RequestOf(CoreContext c, Camera view)
        {
            string mode = c.Args.Get("mode", CaptureSettings.Mode);
            if (Array.IndexOf(CaptureSettings.Modes, mode) < 0) throw CommandKit.BadArgs("mode", ArgTypes.Enum(CaptureSettings.Modes));
            float scale = c.Args.Get("scale", CaptureSettings.Scale);
            if (float.IsNaN(scale) || scale < 1f || scale > 4f) throw CommandKit.BadArgs("scale", "1-4");
            int w = CaptureSettings.Width, h = CaptureSettings.Height;
            if (mode == CaptureSettings.ModeCustom)
            {
                w = c.Args.Get("width", w);
                h = c.Args.Get("height", h);
                if (w < CaptureLimits.MinSide || w > CaptureLimits.MaxSide) throw CommandKit.BadArgs("width", "16-8192");
                if (h < CaptureLimits.MinSide || h > CaptureLimits.MaxSide) throw CommandKit.BadArgs("height", "16-8192");
            }
            else if (c.Args.Has("width") || c.Args.Has("height")) c.Warn("width / height are used only with mode custom");
            int k = c.Args.Get("supersample", CaptureSettings.Supersample);
            if (!CaptureLimits.ValidSupersample(k)) throw CommandKit.BadArgs("supersample", "1, 2 or 4");
            var size = CaptureSettings.OutputSize(mode, scale, w, h, view, Screen.width, Screen.height);
            return new CaptureRequest { Width = size.x, Height = size.y, Supersample = k, Frame = c.Args.Get("frame", CaptureSettings.Frame) };
        }

        static object Screenshot(CoreContext c)
        {
            string bg = c.Args.Get<string>("background");
            if (bg != null)
            {
                if (bg == "transparent") throw CommandKit.Unsupported("transparent backgrounds are not in v0.2 (R15)");
                throw CommandKit.BadArgs("background", "\"transparent\" (reserved)");
            }
            var view = ViewOf(c.App);
            if (!view) throw CommandKit.NotRealized("no view camera", null, "NoView");
            var req = RequestOf(c, view);
            string why = CaptureLimits.Check(req.Width, req.Height, req.Supersample, req.MaxPixels, out _);
            if (why != null) throw CommandKit.BadArgs(req.Supersample > 1 ? "supersample" : "width", "a render of at most " + CaptureLimits.MaxPixels + " pixels", why);
            // the arguments (and the cap) are judged before the device, so a headless run answers bad_args the same way
            if (!CaptureService.Available) throw CommandKit.Unsupported("screenshot needs a graphics device (this run is headless)");
            if (CaptureService.InFlight >= MaxInFlight) throw new BridgeException(CommandKit.Unavailable, "a screenshot is still being written", null);
            if ((long)req.Width * req.Height * req.Supersample * req.Supersample > CaptureLimits.WarnPixels) c.Warn(CaptureLimits.LargeRenderWarning);
            string name = c.Args.Get<string>("name");
            int wait = Math.Max(0, c.Ctx.WaitFrames);
            var app = c.App;
            var d = c.Ctx.Defer();
            var frames = c.Frames;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var images = SceneImages.Ensure(app.Runtime);
            int waited = 0;
            void Attempt()
            {
                if (d.IsCompleted || d.IsAbandoned) return;
                // the scene's images first (a background picked in the same request batch is still decoding)
                if (images != null && images.Pending && waited++ < 600) { frames.AtLate(1, Attempt); return; }
                try
                {
                    TakeScreenshot(app, req, name, res =>
                    {
                        if (!res.Ok) { d.Fail(new BridgeError(BridgeCodes.Internal, "capture.failed: " + res.Error)); return; }
                        d.Complete(Reply(res, sw.Elapsed.TotalMilliseconds, images != null && images.Pending));
                    });
                }
                catch (ArgumentException e) { d.Fail(BridgeError.BadArgs("width", "a render within the pixel cap", e.Message)); }
                catch (InvalidOperationException e) { d.Fail(new BridgeError(CommandKit.Unavailable, e.Message)); }
                catch (BridgeException e) { d.Fail(e); }
                catch (Exception e) { d.Fail(new BridgeError(BridgeCodes.Internal, "capture.failed: " + e.GetType().Name + ": " + e.Message)); }
            }
            frames.AtLate(wait, Attempt);
            return null;
        }

        /// <summary>The reply of a finished screenshot (the large-render warning is the envelope's, given before the defer).</summary>
        public static BridgeReply Reply(CaptureResult res, double ms, bool imagesPending)
        {
            return new BridgeReply()
                .With("path", res.Path).With("width", res.Width).With("height", res.Height)
                .With("renderWidth", res.RenderWidth).With("renderHeight", res.RenderHeight)
                .With("megapixels", Math.Round(res.Megapixels, 3)).With("ms", ms).With("imagesPending", imagesPending)
                .With("timing", new BridgeReply().With("mainThreadMs", res.MainThreadMs).With("renderMs", res.RenderMs).With("readbackMs", res.ReadbackMs)
                    .With("reduceMs", res.ReduceMs).With("encodeMs", res.EncodeMs).With("writeMs", res.WriteMs).With("totalMs", res.TotalMs))
                .With("memory", new BridgeReply().With("gfxBefore", res.GfxBefore).With("gfxAfterRender", res.GfxAfterRender));
        }

        // ------------------------------------------------------------ scene.images

        static object Images(CoreContext c)
        {
            bool setBg = c.Args.Kind("background") != ArgKind.Missing, setFrame = c.Args.Kind("frame") != ArgKind.Missing;
            var ops = CommandKit.Ops(c);
            var r = new BridgeReply();
            if (setBg || setFrame)
            {
                bool embed = c.Args.Get("embed", CaptureSettings.Embed);
                var ctx = FileRefContext.Of(c.Runtime, embed);
                FileRef bg = null, fr = null;
                if (setBg) bg = Picked(c, "background", ctx);
                if (setFrame) fr = Picked(c, "frame", ctx);
                var res = CommandKit.Check(c, ops.SetSceneImages(setBg, bg, setFrame, fr, CommandKit.Exec(c, setBg ? StudioOps.Labels.Background : StudioOps.Labels.Frame)));
                r["changed"] = res.Changed;
                r["noop"] = res.NoOp;
            }
            else SceneImages.Ensure(c.Runtime)?.Apply();
            var s = c.Session.Model.Settings;
            r["background"] = Json(s.Background);
            r["frame"] = Json(s.Frame);
            r["images"] = State(SceneImages.Ensure(c.Runtime));
            return r;
        }

        static FileRef Picked(CoreContext c, string arg, FileRefContext ctx)
        {
            if (c.Args.Kind(arg) == ArgKind.Null) return null;
            if (c.Args.Kind(arg) != ArgKind.String) throw CommandKit.BadArgs(arg, "a path string or null");
            string p = c.Args.Get<string>(arg);
            if (FileRefs.TryFromPicked(p, arg, ctx, out var f, out var why)) return f;
            if (why != null && why.StartsWith("file not found", StringComparison.Ordinal)) throw CommandKit.NotFound(why, arg);
            throw CommandKit.BadArgs(arg, "a PNG / JPEG path (absolute, relative to UserData, or hs2:<relative>)", why);
        }

        static object Json(FileRef f) => f == null ? null : new RawJson(FieldJson.ToText(f));

        /// <summary>{background, frame}: {state, width, height, source, error, loadMs} of the realized images.</summary>
        public static BridgeReply State(SceneImages images)
        {
            BridgeReply One(SceneImages.Slot s) => s == null ? null : new BridgeReply().With("state", s.State).With("width", s.Width).With("height", s.Height)
                .With("source", s.Source).With("error", s.Error).With("loadMs", s.LoadMs);
            return new BridgeReply().With("background", One(images?.Background)).With("frame", One(images?.Frame));
        }
    }
}
