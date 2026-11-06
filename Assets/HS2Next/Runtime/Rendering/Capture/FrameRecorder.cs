// Frame sequence export: the view recorded at a fixed time step as numbered PNG files plus a record.json (record.start /
// record.stop, the Timeline panel's Export frames dialog). A video is made from the files with an external tool.
//   Time step: the start stores Time.captureDeltaTime and sets it to 1 / fps, so from the next frame on Time.deltaTime is
//   exactly that value whatever the real time, and every consumer of Unity's time (the scene clock and through it the
//   characters, items, routes, map scripts and the Timeline; particles, map animators, the scene shader time) advances one
//   step per frame; every stop restores the stored value.
//   Frames: the first late point after the start only arms the recording (a Timeline recorded with `timeline` is stopped
//   and played from 0 there, so its first advance is the next frame's); from the next frame on, at the late point of every
//   frame whose scene clock ran (frames of the global pause are not written: the recording holds and resumes with the next
//   running frame), the view is rendered once through CaptureService.RenderAsync with the capture command's setup (no UI,
//   gizmos, light helpers, gimmick icons, route lines or pause badge; HDR, post-processing, SMAA; the scene's frame image
//   only when asked) at width x height x supersample. The read-back and the PNG encode finish a few frames later, then a
//   worker writes <folder>/<name>NNNNNN.png (6 digits, counting written frames from 1).
//   In flight: at most 4 frames are between their render and their file. When 4 are pending the main thread waits for
//   them (the GPU read-backs, then the encodes); when no read-back completes while the frame is held, the frame is rendered,
//   read back, encoded and written at once on the main thread instead. No frame is ever dropped.
//   Capture events: each frame raises Starting and Rendered around its render and Finished after its file is written (or
//   the frame failed), kind Record, the origin of the start, in frame order; CaptureService.Saved (the screenshot toast) is
//   never raised.
//   Stops: record.stop ("stop"); the frame count ("frames"); one pass of a looping Timeline ("pass": round(duration x fps)
//   frames) or the end of one that does not loop ("ended"); a new or loaded scene ("scene", when the clear begins); the app's
//   quit ("quit": the pending frames are written first, at most 10 s); a failure ("error": a render that throws is retried
//   once at once with a synchronous render; a read-back, encode or write failure stops - the frame's moment has passed; files
//   written so far stay); less than 512 MB of free disk space before a frame ("disk"); 999,999 frames ("limit"). Every stop
//   restores the time step, waits for the pending frames (at most 60 s) and writes record.json: {format, fps, dt, width,
//   height, renderWidth, renderHeight, supersample, frame, frames, written, pattern, startUtc, stoppedBy, error, sceneTimes
//   (the scene clock's time of each frame), timelineTimes (the Timeline's play head of each frame, null for a frame without a
//   Timeline; the member is null when no frame had one)}. Current keeps the last recording's result until the next start.
// Main thread; the late point is the bridge's frame clock (the capture command's late point), or, when the app runs without the
// bridge, the recorder's own late driver at the same place in the frame (FrameRecorderDriver). The Timeline is reached through
// IRecordedTimeline, which the scene's Timeline playback part implements.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HS2Next.Api.Bridge;
using HS2Next.Api.Plugins;
using HS2Next.App;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace HS2Next.Screenshots
{
    /// <summary>
    /// The scene's Timeline as the frame recorder sees it: implemented by the Timeline playback part of the scene runtime (the
    /// recorder finds it with SceneRuntime.GetPart). Main thread.
    /// </summary>
    public interface IRecordedTimeline : ISceneRuntimePart
    {
        /// <summary>The scene has a Timeline (any state but "none").</summary>
        bool Exists { get; }
        /// <summary>Its duration in seconds (0 without a Timeline).</summary>
        double Duration { get; }
        /// <summary>Playback wraps at the end (the document's loop).</summary>
        bool Loop { get; }
        /// <summary>The play head in seconds (0 without a Timeline).</summary>
        double Time { get; }
        /// <summary>The playback ended (not looping, the play head held at the duration).</summary>
        bool Ended { get; }
        /// <summary>
        /// Stops the Timeline (its slots released) and plays it from 0 without advancing it: the first advance is the next
        /// tick of the scene clock, which fires the events at 0. False when the scene has no Timeline.
        /// </summary>
        bool RestartForRecording();
    }

    /// <summary>What to record (the arguments of record.start; <see cref="Folder"/> already resolved, absolute).</summary>
    public sealed class RecordOptions
    {
        /// <summary>The absolute output folder (<see cref="FrameRecorder.ResolveFolder"/>); created when missing.</summary>
        public string Folder;
        /// <summary>The PNG size.</summary>
        public int Width = 1280, Height = 720;
        /// <summary>Frames per second of scene time: the time step is 1 / fps (1-120).</summary>
        public int Fps = 30;
        /// <summary>1, 2 or 4: the render is k x the output, reduced in linear light.</summary>
        public int Supersample = 1;
        /// <summary>Draw the scene's frame image.</summary>
        public bool Frame;
        /// <summary>Frames to record (0 = until stopped).</summary>
        public int Frames;
        /// <summary>Play the Timeline from 0 at the first frame and stop after one pass (or at its end).</summary>
        public bool Timeline;
        /// <summary>The file name prefix.</summary>
        public string Name = FrameRecorder.DefaultName;
        /// <summary>A folder holding files is accepted (files of the same names are replaced, others kept).</summary>
        public bool Overwrite;
        /// <summary>The origin the capture events carry: Command for the bridge, Ui for the dialog.</summary>
        public CaptureOrigin Origin = CaptureOrigin.Command;
    }

    /// <summary>One recording (the active one or the last one: <see cref="Current"/>).</summary>
    public sealed class FrameRecorder
    {
        /// <summary>Frames between their render and their file at most.</summary>
        public const int MaxInFlight = 4;
        /// <summary>The longest recording (the 6-digit file numbers).</summary>
        public const int MaxFrameCount = 999_999;
        /// <summary>A frame is recorded only while the output drive has this much free space.</summary>
        public const long MinFreeBytes = 512L * 1024 * 1024;
        /// <summary>A stop waits this long for the pending frames.</summary>
        public const double StopWaitSeconds = 60;
        /// <summary>The app's quit waits this long for the pending frames.</summary>
        public const double QuitWaitSeconds = 10;
        public const int MinFps = 1, MaxFps = 120;
        public const string DefaultName = "frame_";
        /// <summary>The format member of record.json.</summary>
        public const string FormatName = "hs2next.record/1";
        /// <summary>The file written at the stop.</summary>
        public const string RecordFile = "record.json";

        /// <summary>The stop reasons (stoppedBy).</summary>
        public const string ByStop = "stop", ByFrames = "frames", ByPass = "pass", ByEnded = "ended", ByScene = "scene", ByQuit = "quit",
            ByError = "error", ByDisk = "disk", ByLimit = "limit";

        /// <summary>How long the main thread waits for the pending frames before it renders a frame synchronously.</summary>
        const double HoldBudgetMs = 2000;

        enum Phase { Recording, Stopping, Done }

        sealed class PendingFrame
        {
            public int Number;
            public string File;
            public CaptureEvents.Pending Events;
            public bool InCall, NeedsRetry, Done, Ok;
            public volatile bool Abandoned;
            public long Bytes;
            public string Error;
        }

        // ------------------------------------------------------------ the active and the last recording

        /// <summary>The active recording, or the last one (its result) until the next start; null before the first.</summary>
        public static FrameRecorder Current { get; private set; }

        /// <summary>A recording captures frames now (scene.step answers busy meanwhile).</summary>
        public static bool IsRecording => Current != null && Current.phase == Phase.Recording;

        /// <summary>A recording captures frames or still writes its pending frames (a new start answers busy).</summary>
        public static bool IsBusy => Current != null && Current.phase != Phase.Done;

        /// <summary>The recorder's frames inside CaptureService.RenderAsync (counted in CaptureService.InFlight).</summary>
        public static int CapturesInFlight => Current?.asyncOutstanding ?? 0;

        /// <summary>Raised on the main thread when a recording started.</summary>
        public static event Action<FrameRecorder> Started;

        /// <summary>Raised on the main thread when a recording finished (record.json written).</summary>
        public static event Action<FrameRecorder> Finished;

        /// <summary>The read-backs of the asynchronous renders do not complete while a frame is held (measured once per process).</summary>
        static bool holdUnsupported;

        readonly AppCore app;
        readonly SceneRuntime rt;
        readonly SceneSession session;
        readonly Camera view;
        readonly IBridgeFrames clock;
        readonly RecordOptions opt;
        readonly IRecordedTimeline timeline;
        readonly List<PendingFrame> queue = new List<PendingFrame>();
        readonly List<double> sceneTimes = new List<double>();
        readonly List<double?> timelineTimes = new List<double?>();
        List<Action<FrameRecorder>> doneWaiters;
        Phase phase;
        bool armed, timelineArmed, timelineLoop, anyTimeline, flushing;
        float savedCaptureDt;
        int count, asyncOutstanding, completions;
        Stopwatch total, stopClock;
        DriveInfo drive;
        bool driveTried;

        FrameRecorder(AppCore app, SceneRuntime rt, Camera view, IBridgeFrames clock, RecordOptions o, IRecordedTimeline timeline)
        {
            this.app = app;
            this.rt = rt;
            session = rt.Session;
            this.view = view;
            this.clock = clock;
            opt = o;
            this.timeline = timeline;
            Folder = o.Folder;
            Name = o.Name ?? DefaultName;
            Width = o.Width;
            Height = o.Height;
            Supersample = o.Supersample;
            Fps = o.Fps;
            Dt = 1f / o.Fps;
            DrawsFrame = o.Frame;
            Origin = o.Origin;
            RecordsTimeline = o.Timeline;
        }

        // ------------------------------------------------------------ what the commands and the UI read

        /// <summary>The absolute output folder.</summary>
        public string Folder { get; }
        /// <summary>The file name prefix.</summary>
        public string Name { get; }
        /// <summary>The file pattern: the prefix + "%06d.png".</summary>
        public string Pattern => Name + "%06d.png";
        public int Width { get; }
        public int Height { get; }
        public int Supersample { get; }
        public int RenderWidth => Width * Supersample;
        public int RenderHeight => Height * Supersample;
        public int Fps { get; }
        /// <summary>The time step (the float Time.captureDeltaTime takes).</summary>
        public float Dt { get; }
        public bool DrawsFrame { get; }
        public CaptureOrigin Origin { get; }
        /// <summary>The recording plays the Timeline from 0 and stops after one pass.</summary>
        public bool RecordsTimeline { get; }
        /// <summary>The frames of one Timeline pass (0 until armed, and for a Timeline that does not loop).</summary>
        public int PassFrames { get; private set; }
        /// <summary>Frames captured (rendered).</summary>
        public int Frames => count;
        /// <summary>Frames whose file is written.</summary>
        public int Written { get; private set; }
        /// <summary>Frames captured whose file is not written yet (or whose Finished is not raised yet).</summary>
        public int Pending => queue.Count;
        /// <summary>This recording captures frames now.</summary>
        public bool IsActive => phase == Phase.Recording;
        /// <summary>This recording is finished (record.json written).</summary>
        public bool IsDone => phase == Phase.Done;
        /// <summary>Why it stopped (null while recording).</summary>
        public string StoppedBy { get; private set; }
        /// <summary>The first failure (null when none).</summary>
        public string Error { get; private set; }
        public DateTime StartUtc { get; private set; }
        /// <summary>The scene clock's time of the first and of the last captured frame (null before the first).</summary>
        public double? SceneTimeStart { get; private set; }
        public double? SceneTimeEnd { get; private set; }
        /// <summary>Milliseconds from the start to the last file written (so far while it runs).</summary>
        public double Milliseconds => finishedMs ?? total?.Elapsed.TotalMilliseconds ?? 0;
        double? finishedMs;
        /// <summary>The most frames between their render and their file at one time.</summary>
        public int MaxPending { get; private set; }
        /// <summary>Frames before which the main thread waited for the pending frames, and the time it waited.</summary>
        public int Waits { get; private set; }
        public double WaitMs { get; private set; }
        /// <summary>Frames rendered and written synchronously (the pending frames did not complete while the frame was held).</summary>
        public int SyncFrames { get; private set; }
        /// <summary>Frames of the global pause (not captured).</summary>
        public int Held { get; private set; }
        /// <summary>Frames whose render threw and was retried synchronously.</summary>
        public int Retries { get; private set; }
        /// <summary>The recording captures at the recorder's own late point (the app runs without the bridge).</summary>
        public bool OwnLatePoint => clock == null;
        /// <summary>record.json's absolute path.</summary>
        public string RecordJsonPath => Path.Combine(Folder, RecordFile);

        /// <summary>The scene times of the captured frames.</summary>
        public IReadOnlyList<double> SceneTimes => sceneTimes;

        /// <summary>The absolute path of frame <paramref name="number"/> (from 1).</summary>
        public string FileOf(int number) => Path.Combine(Folder, Name + number.ToString("D6", CultureInfo.InvariantCulture) + ".png");

        /// <summary>Runs <paramref name="a"/> on the main thread when the recording is finished (at once when it is).</summary>
        public void WhenDone(Action<FrameRecorder> a)
        {
            if (a == null) return;
            if (phase == Phase.Done) { a(this); return; }
            (doneWaiters ??= new List<Action<FrameRecorder>>()).Add(a);
        }

        // ------------------------------------------------------------ start

        /// <summary>
        /// Checks <paramref name="o"/> and starts recording the view of <paramref name="app"/>'s scene into o.Folder. Throws
        /// BridgeException: busy (a recording runs or still writes), bad_args (sizes, the pixel cap, fps, frames, name),
        /// unavailable (no scene runtime, no graphics device, no view camera, no frame loop - an edit-mode host without the
        /// bridge -, timeline without a Timeline), io_denied (the folder cannot be made, or holds files without overwrite).
        /// Without the bridge (app.Bridge null) the recorder takes its own late point (FrameRecorderDriver).
        /// </summary>
        public static FrameRecorder Start(AppCore app, RecordOptions o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            ReleaseStale();
            if (IsBusy) throw BusyError();
            Validate(o);
            var rt = app?.Runtime;
            if (rt == null || rt.IsDisposed) throw new BridgeException(BridgeCodes.Unavailable, "no scene runtime");
            if (!CaptureService.Available) throw new BridgeException(BridgeCodes.Unavailable, "recording needs a graphics device (this run is headless)");
            var view = rt.GetPart<SceneViewCamera>()?.Camera;
            if (!view) throw new BridgeException(BridgeCodes.Unavailable, "no view camera");
            IBridgeFrames clock = app.Bridge?.Server?.Frames;
            if (clock == null && !FrameRecorderDriver.Ensure())
                throw new BridgeException(BridgeCodes.Unavailable, "no frame loop (an edit-mode host without the bridge)");
            var tl = rt.GetPart<IRecordedTimeline>();
            if (o.Timeline && (tl == null || !tl.Exists)) throw new BridgeException(BridgeCodes.Unavailable, "the scene has no Timeline");
            if (string.IsNullOrEmpty(o.Folder) || !Path.IsPathFullyQualified(o.Folder))
                throw new BridgeException(BridgeError.IoDenied("the folder must be an absolute path", "path"));
            PrepareFolder(o.Folder, o.Overwrite);
            var r = new FrameRecorder(app, rt, view, clock, o, tl);
            r.Begin();
            return r;
        }

        /// <summary>The busy error of a start while <see cref="IsBusy"/>.</summary>
        public static BridgeException BusyError() =>
            new BridgeException(BridgeCodes.Busy, IsRecording ? "already recording" : "the last recording is still writing its frames");

        static void Validate(RecordOptions o)
        {
            if (o.Fps < MinFps || o.Fps > MaxFps) throw new BridgeException(BridgeError.BadArgs("fps", MinFps + "-" + MaxFps));
            if (o.Width < CaptureLimits.MinSide || o.Width > CaptureLimits.MaxSide) throw new BridgeException(BridgeError.BadArgs("width", CaptureLimits.MinSide + "-" + CaptureLimits.MaxSide));
            if (o.Height < CaptureLimits.MinSide || o.Height > CaptureLimits.MaxSide) throw new BridgeException(BridgeError.BadArgs("height", CaptureLimits.MinSide + "-" + CaptureLimits.MaxSide));
            if (!CaptureLimits.ValidSupersample(o.Supersample)) throw new BridgeException(BridgeError.BadArgs("supersample", "1, 2 or 4"));
            string why = CaptureLimits.Check(o.Width, o.Height, o.Supersample, 0, out _);
            if (why != null)
                throw new BridgeException(BridgeError.BadArgs(o.Supersample > 1 ? "supersample" : "width", "a render of at most " + CaptureLimits.MaxPixels + " pixels", why));
            if (o.Frames < 0 || o.Frames > MaxFrameCount) throw new BridgeException(BridgeError.BadArgs("frames", "0-" + MaxFrameCount + " (0 = until record.stop)"));
            string n = o.Name ?? DefaultName;
            if (n.Length > 64 || n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || n.IndexOf('/') >= 0 || n.IndexOf('\\') >= 0 || n.Trim() != n)
                throw new BridgeException(BridgeError.BadArgs("name", "a file name prefix (at most 64 characters, no path)"));
        }

        static void PrepareFolder(string folder, bool overwrite)
        {
            try
            {
                if (File.Exists(folder)) throw new BridgeException(BridgeError.IoDenied("a file has the folder's name: " + folder, "path"));
                Directory.CreateDirectory(folder);
                if (!overwrite)
                    using (var e = Directory.EnumerateFileSystemEntries(folder).GetEnumerator())
                        if (e.MoveNext())
                            throw new BridgeException(BridgeError.IoDenied("the folder holds files: " + folder + " (overwrite: true replaces files of the same names)", "overwrite"));
            }
            catch (BridgeException) { throw; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is ArgumentException)
            {
                throw new BridgeException(BridgeError.IoDenied("the folder cannot be made: " + e.Message, "path"));
            }
        }

        /// <summary>
        /// The absolute folder of a <paramref name="path"/> argument: absent = &lt;output root&gt;/record/&lt;yyyyMMdd_HHmmss&gt;; a
        /// relative path lies under the output root (the bridge output root's guard: no "..", devices, ':' or rooted parts); an
        /// absolute folder must lie under the output root or the UserData folder unless <paramref name="anyFolder"/> (a folder
        /// the user picked in the dialog); HS2's folders are never accepted. Throws BridgeException io_denied. Nothing is
        /// created.
        /// </summary>
        public static string ResolveFolder(AppCore app, string outputRoot, string path, bool anyFolder = false)
        {
            if (string.IsNullOrWhiteSpace(path)) path = "record/" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string full;
            if (Path.IsPathRooted(path))
            {
                if (!Path.IsPathFullyQualified(path)) throw new BridgeException(BridgeError.IoDenied("a rooted path must start with its drive: " + path, "path"));
                try { full = Norm(path); }
                catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
                {
                    throw new BridgeException(BridgeError.IoDenied("not a folder path: " + e.Message, "path"));
                }
                if (!anyFolder)
                {
                    string ud = app?.Paths?.UserData;
                    string root = Within(full, outputRoot) ? outputRoot : Within(full, ud) ? ud : null;
                    if (root == null) throw new BridgeException(BridgeError.IoDenied("an absolute folder must lie under the output root or the UserData folder: " + full, "path"));
                    full = Guarded(app, root, Relative(full, root));
                }
            }
            else
            {
                if (string.IsNullOrEmpty(outputRoot)) throw new BridgeException(BridgeError.IoDenied("no output root for a relative folder", "path"));
                full = Guarded(app, outputRoot, path);
            }
            string hs2 = app?.Paths?.Hs2Root;
            if (!string.IsNullOrEmpty(hs2) && Within(full, hs2)) throw new BridgeException(BridgeError.IoDenied("HS2's folders are never written: " + full, "path"));
            return full;
        }

        static string Guarded(AppCore app, string root, string rel)
        {
            rel = (rel ?? "").Replace('\\', '/').Trim('/');
            if (rel.Length == 0) return Norm(root);
            if (app == null) throw new BridgeException(BridgeError.IoDenied("no app to check the folder against", "path"));
            try
            {
                string probe = app.RootFor(root).Resolve(rel + "/" + RecordFile, overwrite: true, createDirectory: false);
                return Norm(Path.GetDirectoryName(probe));
            }
            catch (BridgeException e) when (e.Code == BridgeCodes.IoDenied || e.Code == BridgeCodes.BadArgs)
            {
                throw new BridgeException(e.Code, e.Error.Message, new BridgeErrorDetail { Arg = "path" });
            }
        }

        static string Norm(string p)
        {
            string f = Path.GetFullPath(p);
            string r = Path.GetPathRoot(f);
            return f.Length > (r?.Length ?? 0) ? f.TrimEnd('\\', '/') : f;
        }

        static bool Within(string full, string root)
        {
            if (string.IsNullOrEmpty(root)) return false;
            string r;
            try { r = Norm(root); }
            catch (Exception) { return false; }
            if (string.Equals(full, r, StringComparison.OrdinalIgnoreCase)) return true;
            string withSep = r.EndsWith("\\", StringComparison.Ordinal) || r.EndsWith("/", StringComparison.Ordinal) ? r : r + Path.DirectorySeparatorChar;
            return full.StartsWith(withSep, StringComparison.OrdinalIgnoreCase);
        }

        static string Relative(string full, string root)
        {
            string r = Norm(root);
            return full.Length <= r.Length ? "" : full.Substring(r.Length).TrimStart('\\', '/');
        }

        /// <summary>A recording whose app or scene runtime ended without a stop (a test host disposed) is finished now.</summary>
        static void ReleaseStale()
        {
            var c = Current;
            if (c == null || c.phase == Phase.Done) return;
            if (!c.app.IsDisposed && !c.rt.IsDisposed) return;
            c.Stop(ByScene, "the app or its scene runtime ended");
            if (c.phase == Phase.Stopping) c.GiveUp("the app or its scene runtime ended with frames pending");
        }

        void Begin()
        {
            savedCaptureDt = UnityEngine.Time.captureDeltaTime;
            UnityEngine.Time.captureDeltaTime = Dt;
            StartUtc = DateTime.UtcNow;
            total = Stopwatch.StartNew();
            phase = Phase.Recording;
            Current = this;
            if (clock != null) clock.LateFrame += OnLate;
            else FrameRecorderDriver.Late += OnLate;
            if (session != null) session.Events.Cleared += OnCleared;
            Application.quitting += OnQuitting;
            Raise(Started, "Started");
        }

        // ------------------------------------------------------------ frames

        void OnLate(int frame)
        {
            try
            {
                if (phase == Phase.Stopping)
                {
                    if (stopClock != null && stopClock.Elapsed.TotalSeconds > StopWaitSeconds)
                        GiveUp(InFlight() + " frame(s) were not written within " + StopWaitSeconds + " s of the stop");
                    return;
                }
                if (phase != Phase.Recording) return;
                if (rt.IsDisposed || app.IsDisposed) { Stop(ByScene, null); return; }
                if (!armed) { Arm(); return; }
                if (rt.Clock.FrameDt == 0f) { Held++; return; }
                if (FreeBytes() < MinFreeBytes) { Stop(ByDisk, "less than 512 MB of free disk space for " + Folder); return; }
                Capture(frame);
                if (phase != Phase.Recording) return;
                if (opt.Frames > 0 && count >= opt.Frames) Stop(ByFrames, null);
                else if (PassFrames > 0 && count >= PassFrames) Stop(ByPass, null);
                else if (timelineArmed && !timelineLoop && timeline.Ended) Stop(ByEnded, null);
                else if (count >= MaxFrameCount) Stop(ByLimit, null);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[record] the late point threw: " + e);
                if (phase == Phase.Recording) Stop(ByError, "frame " + (count + 1) + ": " + e.GetType().Name + ": " + e.Message);
            }
        }

        /// <summary>The first late point: a recorded Timeline is restarted here, so that its first advance is the next frame's.</summary>
        void Arm()
        {
            armed = true;
            if (!opt.Timeline) return;
            if (timeline == null || !timeline.RestartForRecording()) { Stop(ByError, "the scene has no Timeline to record"); return; }
            timelineArmed = true;
            timelineLoop = timeline.Loop;
            PassFrames = timelineLoop ? Math.Max(1, (int)Math.Round(timeline.Duration * Fps, MidpointRounding.AwayFromZero)) : 0;
        }

        CaptureRequest Request() => new CaptureRequest { Width = Width, Height = Height, Supersample = Supersample, Frame = DrawsFrame, Aa = CaptureService.Smaa };

        void Capture(int frame)
        {
            if (InFlight() >= MaxInFlight) Hold();
            int number = count + 1;
            var req = Request();
            var p = new PendingFrame { Number = number, File = FileOf(number) };
            p.Events = CaptureEvents.Prepare(CaptureKind.Record, Origin, req, p.File, frame);
            double st = rt.Clock.Time;
            double? tt = timeline != null && timeline.Exists ? timeline.Time : (double?)null;
            if (tt != null) anyTimeline = true;
            sceneTimes.Add(st);
            timelineTimes.Add(tt);
            if (SceneTimeStart == null) SceneTimeStart = st;
            SceneTimeEnd = st;
            count = number;
            queue.Add(p);
            SceneImages.Ensure(rt);
            if (InFlight() > MaxInFlight)
            {
                // the pending frames did not complete while this frame was held: this one is done at once
                SyncFrames++;
                try { RenderNow(p, req); }
                catch (Exception e) { Complete(p, false, 0, "render: " + Describe(e)); }
            }
            else
            {
                p.InCall = true;
                asyncOutstanding++;
                try { CaptureService.RenderAsync(rt, view, req, null, res => OnEncoded(p, res), p.Events); }
                catch (Exception e)
                {
                    asyncOutstanding--;
                    p.NeedsRetry = true;
                    p.Error = Describe(e);
                }
                finally { p.InCall = false; }
                if (p.NeedsRetry) Retry(p, req);
            }
            MaxPending = Math.Max(MaxPending, InFlight());
            FlushHead();
        }

        /// <summary>A render that failed inside its frame: one synchronous render of the same frame.</summary>
        void Retry(PendingFrame p, CaptureRequest req)
        {
            if (p.Done) return;
            Retries++;
            string first = p.Error;
            try { RenderNow(p, req); }
            catch (Exception e) { Complete(p, false, 0, "render: " + first + "; the retry: " + Describe(e)); }
        }

        /// <summary>Renders, reads back, encodes and writes the frame now (main thread).</summary>
        void RenderNow(PendingFrame p, CaptureRequest req)
        {
            var res = CaptureService.RenderSync(rt, view, req, p.Events);
            if (!res.Ok || res.Png == null) throw new InvalidOperationException(res.Error ?? "no image");
            string err = WriteWithRetry(p, res.Png);
            Complete(p, err == null, res.Png.LongLength, err);
        }

        /// <summary>The read-back and the encode of an asynchronous render are done (main thread).</summary>
        void OnEncoded(PendingFrame p, CaptureResult res)
        {
            asyncOutstanding--;
            completions++;
            if (!res.Ok || res.Png == null)
            {
                string why = res.Error ?? "no image";
                if (p.InCall) { p.NeedsRetry = true; p.Error = why; return; }
                Complete(p, false, 0, "read-back or encode: " + why);
                return;
            }
            var png = res.Png;
            if (!Application.isPlaying)
            {
                string err = WriteWithRetry(p, png);
                Complete(p, err == null, png.LongLength, err);
                return;
            }
            Task.Run(() =>
            {
                string err = WriteWithRetry(p, png);
                CaptureDriver.Post(() => Complete(p, err == null, png.LongLength, err));
            });
        }

        /// <summary>Writes the PNG (one retry when the write fails); the error text, or null. Any thread.</summary>
        static string WriteWithRetry(PendingFrame p, byte[] png)
        {
            if (p.Abandoned) return "abandoned";
            try { File.WriteAllBytes(p.File, png); return null; }
            catch (Exception)
            {
                try { File.WriteAllBytes(p.File, png); return null; }
                catch (Exception e) { return "write: " + Describe(e); }
            }
        }

        void Complete(PendingFrame p, bool ok, long bytes, string err)
        {
            if (p.Done) return;
            p.Done = true;
            p.Ok = ok;
            p.Bytes = ok ? bytes : 0;
            p.Error = ok ? null : err ?? "the frame failed";
            if (!ok && phase == Phase.Recording) { Stop(ByError, "frame " + p.Number + ": " + p.Error); return; }
            if (!ok && Error == null) Error = "frame " + p.Number + ": " + p.Error;
            FlushHead();
        }

        /// <summary>Finished of the frames at the head of the queue whose file is done, in frame order; then the end of a stop.</summary>
        void FlushHead()
        {
            if (flushing) return;
            flushing = true;
            try
            {
                while (queue.Count > 0 && queue[0].Done)
                {
                    var p = queue[0];
                    queue.RemoveAt(0);
                    if (p.Ok) Written++;
                    CaptureEvents.RaiseFinished(p.Events, p.Ok, p.File, p.Bytes, p.Error);
                }
            }
            finally { flushing = false; }
            if (phase == Phase.Stopping && queue.Count == 0) FinishUp();
        }

        int InFlight()
        {
            int n = 0;
            foreach (var p in queue) if (!p.Done) n++;
            return n;
        }

        /// <summary>
        /// 4 frames are pending: the main thread waits for them - every GPU read-back completed, then the encodes and writes the
        /// workers post - until fewer than 4 are pending or the budget is spent. When no read-back completed in a held frame,
        /// later holds are skipped (the frame is then done synchronously).
        /// </summary>
        void Hold()
        {
            Waits++;
            var sw = Stopwatch.StartNew();
            if (Application.isPlaying && !holdUnsupported)
            {
                int before = completions;
                try { AsyncGPUReadback.WaitAllRequests(); }
                catch (Exception e) { Debug.LogWarning("[record] waiting for the read-backs: " + e.Message); }
                while (InFlight() >= MaxInFlight && sw.Elapsed.TotalMilliseconds < HoldBudgetMs)
                {
                    CaptureDriver.Drain();
                    if (InFlight() < MaxInFlight) break;
                    Thread.Sleep(1);
                }
                if (InFlight() >= MaxInFlight && completions == before)
                {
                    holdUnsupported = true;
                    Debug.Log("[record] the read-backs do not complete while a frame is held: frames over the in-flight bound are rendered synchronously");
                }
            }
            WaitMs += sw.Elapsed.TotalMilliseconds;
        }

        long FreeBytes()
        {
            try
            {
                if (!driveTried)
                {
                    driveTried = true;
                    string root = Path.GetPathRoot(Folder);
                    if (!string.IsNullOrEmpty(root) && !root.StartsWith("\\\\", StringComparison.Ordinal)) drive = new DriveInfo(root);
                }
                return drive != null ? drive.AvailableFreeSpace : long.MaxValue;
            }
            catch (Exception) { return long.MaxValue; }
        }

        static string Describe(Exception e) => e.GetType().Name + ": " + e.Message;

        // ------------------------------------------------------------ stops

        /// <summary>
        /// Stops capturing (<paramref name="reason"/> = stoppedBy, <paramref name="error"/> the first failure): the time step is
        /// restored; the recording finishes (record.json, <see cref="Finished"/>) once its pending frames are written. False when
        /// it was not recording.
        /// </summary>
        public bool Stop(string reason = ByStop, string error = null)
        {
            if (phase != Phase.Recording) return false;
            phase = Phase.Stopping;
            StoppedBy = reason ?? ByStop;
            if (error != null && Error == null) Error = error;
            UnityEngine.Time.captureDeltaTime = savedCaptureDt;
            stopClock = Stopwatch.StartNew();
            if (session != null) session.Events.Cleared -= OnCleared;
            FlushHead();
            return true;
        }

        void OnCleared() => Stop(ByScene, null);

        void OnQuitting()
        {
            Stop(ByQuit, null);
            if (phase != Phase.Stopping) return;
            var sw = Stopwatch.StartNew();
            try { AsyncGPUReadback.WaitAllRequests(); }
            catch (Exception) { }
            while (phase == Phase.Stopping && sw.Elapsed.TotalSeconds < QuitWaitSeconds)
            {
                CaptureDriver.Drain();
                if (phase != Phase.Stopping) break;
                Thread.Sleep(5);
            }
            if (phase == Phase.Stopping) GiveUp(InFlight() + " frame(s) were not written before the app quit");
        }

        /// <summary>The pending frames are given up (their Finished raised as failed); the recording finishes now.</summary>
        void GiveUp(string why)
        {
            if (phase == Phase.Done) return;
            if (Error == null) Error = why;
            foreach (var p in queue)
                if (!p.Done)
                {
                    p.Abandoned = true;
                    p.Done = true;
                    p.Ok = false;
                    p.Error = why;
                }
            if (phase == Phase.Recording) Stop(ByError, why);
            else FlushHead();
        }

        void FinishUp()
        {
            if (phase == Phase.Done) return;
            phase = Phase.Done;
            finishedMs = total.Elapsed.TotalMilliseconds;
            if (clock != null) clock.LateFrame -= OnLate;
            else FrameRecorderDriver.Late -= OnLate;
            Application.quitting -= OnQuitting;
            try { WriteJson(); }
            catch (Exception e)
            {
                if (Error == null) Error = RecordFile + ": " + Describe(e);
                Debug.LogWarning("[record] writing " + RecordFile + " failed: " + e);
            }
            var w = doneWaiters;
            doneWaiters = null;
            if (w != null)
                foreach (var a in w)
                {
                    try { a(this); }
                    catch (Exception e) { Debug.LogWarning("[record] a completion handler threw: " + e); }
                }
            Raise(Finished, "Finished");
        }

        void WriteJson()
        {
            var sb = new StringBuilder(256 + sceneTimes.Count * 40);
            using (var sw = new StringWriter(sb, CultureInfo.InvariantCulture))
            using (var w = new JsonTextWriter(sw) { Formatting = Formatting.None, Culture = CultureInfo.InvariantCulture })
            {
                w.WriteStartObject();
                w.WritePropertyName("format"); w.WriteValue(FormatName);
                w.WritePropertyName("fps"); w.WriteValue(Fps);
                w.WritePropertyName("dt"); w.WriteValue((double)Dt);
                w.WritePropertyName("width"); w.WriteValue(Width);
                w.WritePropertyName("height"); w.WriteValue(Height);
                w.WritePropertyName("renderWidth"); w.WriteValue(RenderWidth);
                w.WritePropertyName("renderHeight"); w.WriteValue(RenderHeight);
                w.WritePropertyName("supersample"); w.WriteValue(Supersample);
                w.WritePropertyName("frame"); w.WriteValue(DrawsFrame);
                w.WritePropertyName("frames"); w.WriteValue(count);
                w.WritePropertyName("written"); w.WriteValue(Written);
                w.WritePropertyName("pattern"); w.WriteValue(Pattern);
                w.WritePropertyName("startUtc"); w.WriteValue(StartUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
                w.WritePropertyName("stoppedBy"); w.WriteValue(StoppedBy);
                w.WritePropertyName("error"); w.WriteValue(Error);
                w.WritePropertyName("sceneTimes");
                w.WriteStartArray();
                foreach (var t in sceneTimes) w.WriteValue(t);
                w.WriteEndArray();
                w.WritePropertyName("timelineTimes");
                if (!anyTimeline) w.WriteNull();
                else
                {
                    w.WriteStartArray();
                    foreach (var t in timelineTimes)
                        if (t == null) w.WriteNull();
                        else w.WriteValue(t.Value);
                    w.WriteEndArray();
                }
                w.WriteEndObject();
            }
            sb.Append('\n');
            File.WriteAllText(RecordJsonPath, sb.ToString(), new UTF8Encoding(false));
        }

        void Raise(Action<FrameRecorder> h, string what)
        {
            if (h == null) return;
            foreach (Action<FrameRecorder> d in h.GetInvocationList())
            {
                try { d(this); }
                catch (Exception e) { Debug.LogWarning("[record] a " + what + " handler threw: " + e); }
            }
        }
    }
}
