// The frame recording commands (group "record", owner core, Since 1.2.0; the frame recorder does the work):
//   record.start   {path?, width 1280, height 720, fps 30, fixed true, supersample 1, frame false, frames 0, timeline false,
//                  name "frame_", overwrite false}: starts recording the view at a fixed time step into a folder of numbered
//                  PNG files (default <output root>/record/<yyyyMMdd_HHmmss>; a relative path lies under the output root, an
//                  absolute one under the output root or the UserData folder); reply {path, pattern, width, height,
//                  renderWidth, renderHeight, fps, dt}. fixed false is unsupported (only fixed-step recording); a second start
//                  answers busy; sizes over the pixel cap or a side over 8192 bad_args; no graphics device, view camera or
//                  frame clock unavailable; timeline without a Timeline unavailable; a folder holding files io_denied unless
//                  overwrite.
//   record.stop    stops it and answers when every pending frame is written (at most 60 s): {frames, width, height, path,
//                  pattern, written, stoppedBy, sceneTimeStart, sceneTimeEnd, ms, failure, maxPending, waits, syncFrames,
//                  held}; not recording answers unavailable.
//   record.status  the recording now, or the last one's result until the next start: {recording, frames, written, pending,
//                  path, pattern, fps, dt, sceneTime, stoppedBy, failure, maxPending, waits, syncFrames, held}.
//                  The first failure is the member failure (a member named error would clash with the reply envelope's).
// record.start and record.stop keep HS2's names, arguments and reply members (ParityNames); CoreCommands registers the three
// in the group "record" through Register.
using System.Collections.Generic;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using HS2Next.Screenshots;

namespace HS2Next.App.Commands
{
    public static class RecordCommands
    {
        /// <summary>The commands of this file (Phase 6.2, Since 1.2.0), registered by CoreCommands in the group "record".</summary>
        public static readonly IReadOnlyList<string> Names = new[] { "record.start", "record.stop", "record.status" };

        /// <summary>The commands of this file that keep HS2's automation vocabulary (CoreCommands.ParityNames).</summary>
        public static readonly IReadOnlyList<string> ParityNames = new[] { "record.start", "record.stop" };

        /// <summary>CommandSpec.Since of these commands (the Api version that adds them).</summary>
        public const string Since62 = "1.2.0";

        /// <summary>The help group of these commands.</summary>
        public const string GroupName = "record";

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("record.start", "Starts recording the view as numbered PNG files at a fixed time step: from the next frame on " +
                    "every frame of the scene advances by exactly 1 / fps whatever the real time and is captured as the capture command captures; " +
                    "frames of the global pause are not written. A video is made from the files with an external tool.", GroupName)
                    .WithSince(Since62).WithFlags(CommandFlags.WritesFiles)
                    .Arg("path", ArgTypes.Path, "the folder: relative to the output root, or absolute under the output root or the UserData folder " +
                        "(default <output root>/record/<yyyyMMdd_HHmmss>); created when missing")
                    .Arg("width", ArgTypes.Int, "the image width (16-8192)", false, 1280)
                    .Arg("height", ArgTypes.Int, "the image height (16-8192)", false, 720)
                    .Arg("fps", ArgTypes.Int, "frames per second of scene time (1-120): the time step is 1 / fps", false, 30)
                    .Arg("fixed", ArgTypes.Bool, "fixed-step recording (false is not supported)", false, true)
                    .Arg("supersample", ArgTypes.Int, "1, 2 or 4: render at k times the size and reduce", false, 1)
                    .Arg("frame", ArgTypes.Bool, "draw the scene's frame image", false, false)
                    .Arg("frames", ArgTypes.Int, "frames to record (0 = until record.stop; at most 999999)", false, 0)
                    .Arg("timeline", ArgTypes.Bool, "play the scene's Timeline from 0 at the first frame and stop after one pass (or at its end when it does not loop)", false, false)
                    .Arg("name", ArgTypes.String, "the file name prefix", false, FrameRecorder.DefaultName)
                    .Arg("overwrite", ArgTypes.Bool, "accept a folder that holds files (files of the same names are replaced, others kept)", false, false)
                    .Return("path", ArgTypes.Path, "the absolute folder")
                    .Return("pattern", ArgTypes.String, "the file names: the prefix + %06d.png, counting from 1")
                    .Return("width", ArgTypes.Int, "").Return("height", ArgTypes.Int, "")
                    .Return("renderWidth", ArgTypes.Int, "the render size (with supersampling)").Return("renderHeight", ArgTypes.Int, "")
                    .Return("fps", ArgTypes.Int, "").Return("dt", ArgTypes.Float, "the time step in seconds (as single precision)")
                    .Example("{\"cmd\":\"record.start\",\"width\":1280,\"height\":720,\"fps\":30,\"timeline\":true}"),
                Start);

            CommandKit.Register(r, app, CommandKit.Spec("record.stop", "Stops the recording; the reply comes when every pending frame is written (at most 60 s), " +
                    "after record.json (the time step and the scene time of every frame) is written into the folder.", GroupName)
                    .WithSince(Since62).WithFlags(CommandFlags.WritesFiles | CommandFlags.Defers).WithTimeout(90)
                    .Return("frames", ArgTypes.Int, "frames recorded").Return("width", ArgTypes.Int, "").Return("height", ArgTypes.Int, "")
                    .Return("path", ArgTypes.Path, "the folder")
                    .Return("pattern", ArgTypes.String, "the file names").Return("written", ArgTypes.Int, "files written")
                    .Return("stoppedBy", ArgTypes.String, "stop, frames, pass, ended, scene, quit, error, disk or limit")
                    .Return("sceneTimeStart", ArgTypes.Float, "the scene time of the first frame (null without frames)")
                    .Return("sceneTimeEnd", ArgTypes.Float, "the scene time of the last frame (null without frames)")
                    .Return("ms", ArgTypes.Float, "milliseconds from the start to the last file written")
                    .Return("failure", ArgTypes.String, "the first failure, or null (record.json writes it as error)")
                    .Return("maxPending", ArgTypes.Int, "the most frames between their render and their file at one time (at most 4)")
                    .Return("waits", ArgTypes.Int, "frames before which the recording waited for the pending frames")
                    .Return("syncFrames", ArgTypes.Int, "frames rendered and written at once because the pending frames did not complete in time")
                    .Return("held", ArgTypes.Int, "frames of the global pause (not written)")
                    .Example("{\"cmd\":\"record.stop\"}"),
                Stop);

            CommandKit.Register(r, app, CommandKit.Spec("record.status", "The recording now, or the result of the last one until the next start.", GroupName)
                    .WithSince(Since62).WithFlags(CommandFlags.ReadOnly)
                    .Return("recording", ArgTypes.Bool, "frames are being recorded")
                    .Return("frames", ArgTypes.Int, "frames recorded").Return("written", ArgTypes.Int, "files written")
                    .Return("pending", ArgTypes.Int, "frames whose file is not written yet")
                    .Return("path", ArgTypes.Path, "the folder (null before the first recording)")
                    .Return("pattern", ArgTypes.String, "the file names").Return("fps", ArgTypes.Int, "").Return("dt", ArgTypes.Float, "")
                    .Return("sceneTime", ArgTypes.Float, "the scene time of the last recorded frame (null without frames)")
                    .Return("stoppedBy", ArgTypes.String, "why the last recording stopped (null while recording)")
                    .Return("failure", ArgTypes.String, "the first failure, or null (record.json writes it as error)")
                    .Return("maxPending", ArgTypes.Int, "").Return("waits", ArgTypes.Int, "").Return("syncFrames", ArgTypes.Int, "").Return("held", ArgTypes.Int, "")
                    .Example("{\"cmd\":\"record.status\"}"),
                Status);
        }

        static object Start(CoreContext c)
        {
            if (FrameRecorder.IsBusy) throw FrameRecorder.BusyError();
            if (!c.Args.Get("fixed", true)) throw CommandKit.Unsupported("only fixed-step recording");
            var o = new RecordOptions
            {
                Width = c.Args.Get("width", 1280),
                Height = c.Args.Get("height", 720),
                Fps = c.Args.Get("fps", 30),
                Supersample = c.Args.Get("supersample", 1),
                Frame = c.Args.Get("frame", false),
                Frames = c.Args.Get("frames", 0),
                Timeline = c.Args.Get("timeline", false),
                Name = c.Args.Get("name", FrameRecorder.DefaultName),
                Overwrite = c.Args.Get("overwrite", false),
                Origin = CaptureOrigin.Command,
            };
            o.Folder = FrameRecorder.ResolveFolder(c.App, c.Output?.Root, c.Args.Get<string>("path"));
            var rec = FrameRecorder.Start(c.App, o);
            if ((long)rec.RenderWidth * rec.RenderHeight > CaptureLimits.WarnPixels) c.Warn(CaptureLimits.LargeRenderWarning);
            return new BridgeReply()
                .With("path", rec.Folder).With("pattern", rec.Pattern).With("width", rec.Width).With("height", rec.Height)
                .With("renderWidth", rec.RenderWidth).With("renderHeight", rec.RenderHeight).With("fps", rec.Fps).With("dt", (double)rec.Dt);
        }

        static object Stop(CoreContext c)
        {
            var rec = FrameRecorder.Current;
            if (rec == null || !rec.IsActive) throw new BridgeException(CommandKit.Unavailable, "not recording");
            var d = c.Ctx.Defer();
            rec.Stop(FrameRecorder.ByStop);
            rec.WhenDone(x => d.Complete(StopReply(x)));
            return null;
        }

        /// <summary>The reply of a finished recording.</summary>
        public static BridgeReply StopReply(FrameRecorder rec) => new BridgeReply()
            .With("frames", rec.Frames).With("width", rec.Width).With("height", rec.Height).With("path", rec.Folder)
            .With("pattern", rec.Pattern).With("written", rec.Written).With("stoppedBy", rec.StoppedBy)
            .With("sceneTimeStart", rec.SceneTimeStart).With("sceneTimeEnd", rec.SceneTimeEnd).With("ms", rec.Milliseconds)
            .With("failure", rec.Error)
            .With("maxPending", rec.MaxPending).With("waits", rec.Waits).With("syncFrames", rec.SyncFrames).With("held", rec.Held);

        static object Status(CoreContext c)
        {
            var rec = FrameRecorder.Current;
            if (rec == null)
                return new BridgeReply().With("recording", false).With("frames", 0).With("written", 0).With("pending", 0).With("path", null)
                    .With("pattern", null).With("fps", null).With("dt", null).With("sceneTime", null).With("stoppedBy", null).With("failure", null)
                    .With("maxPending", 0).With("waits", 0).With("syncFrames", 0).With("held", 0);
            return new BridgeReply()
                .With("recording", rec.IsActive).With("frames", rec.Frames).With("written", rec.Written).With("pending", rec.Pending)
                .With("path", rec.Folder).With("pattern", rec.Pattern).With("fps", rec.Fps).With("dt", (double)rec.Dt)
                .With("sceneTime", rec.SceneTimeEnd).With("stoppedBy", rec.StoppedBy).With("failure", rec.Error)
                .With("maxPending", rec.MaxPending).With("waits", rec.Waits).With("syncFrames", rec.SyncFrames).With("held", rec.Held);
        }
    }
}
