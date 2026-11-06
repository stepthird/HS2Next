// The notifications of a screenshot or a capture for plugins (Phase 6.1): Starting right before the render (after the
// request passed its checks), Rendered right after the render returns (also when it threw), Finished when the file is
// written or the capture failed (for a screenshot a few frames later, for the capture command in the same frame). Each
// Starting is followed by exactly one Rendered and one Finished; a request refused before anything renders raises none.
// The three events of one capture carry the same CaptureInfo; ids count from 1 per process. The screenshot path (the key,
// the toolbar, the dialog, the UI action, the screenshot command) and the capture command raise them; since Phase 6.2 the
// frame recorder raises them for every recorded frame (kind Record, the origin of its start: Command for record.start, Ui
// for the export dialog; Finished a few frames after the render, once the frame's file is written, in frame order);
// thumbnails and test renders (ViewCapture.Render) do not. CaptureService.Saved keeps its meaning (the UI's toast,
// screenshots only; never for a recorded frame). While one
// of them is being delivered, IsDelivering is true (the app's inline gate then answers busy to scene changes). Main thread.
using System;
using System.Diagnostics;
using HS2Next.Api.Plugins;
using Debug = UnityEngine.Debug;

namespace HS2Next.Screenshots
{
    public static class CaptureEvents
    {
        /// <summary>One capture's notifications (created before the render; raised at most once each).</summary>
        public sealed class Pending
        {
            internal CaptureKind Kind;
            internal CaptureOrigin Origin;
            internal int Width, Height, Supersample, FrameNumber;
            internal bool DrawsFrame;
            internal string Path;
            internal CaptureInfo Info;
            internal Stopwatch Clock;
            internal bool RenderedRaised, FinishedRaised;

            /// <summary>Starting was raised.</summary>
            public bool Started => Info != null;
            /// <summary>The capture's description (null before Starting).</summary>
            public CaptureInfo Capture => Info;
        }

        static int lastId;
        static int delivering;

        public static event Action<CaptureInfo> Starting;
        public static event Action<CaptureInfo> Rendered;
        public static event Action<CaptureResultInfo> Finished;

        /// <summary>True while one of the three events is being delivered.</summary>
        public static bool IsDelivering => delivering > 0;

        /// <summary>The last id given (0 = none yet in this process).</summary>
        public static int LastId => lastId;

        /// <summary>The notifications of a capture of <paramref name="req"/> that will write <paramref name="path"/> in frame <paramref name="frame"/>.</summary>
        public static Pending Prepare(CaptureKind kind, CaptureOrigin origin, CaptureRequest req, string path, int frame)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));
            return new Pending
            {
                Kind = kind, Origin = origin, Width = req.Width, Height = req.Height, Supersample = req.Supersample < 1 ? 1 : req.Supersample,
                DrawsFrame = req.Frame, Path = path, FrameNumber = frame,
            };
        }

        /// <summary>Raises Starting (once; the render is about to happen).</summary>
        internal static void RaiseStarting(Pending p)
        {
            if (p == null || p.Info != null) return;
            p.Info = new CaptureInfo(System.Threading.Interlocked.Increment(ref lastId), p.Kind, p.Origin, p.Width, p.Height, p.Supersample,
                p.Width * p.Supersample, p.Height * p.Supersample, p.DrawsFrame, p.FrameNumber, p.Path);
            p.Clock = Stopwatch.StartNew();
            Raise(Starting, p.Info, "Starting");
        }

        /// <summary>Raises Rendered (once, after Starting).</summary>
        internal static void RaiseRendered(Pending p)
        {
            if (p == null || p.Info == null || p.RenderedRaised) return;
            p.RenderedRaised = true;
            Raise(Rendered, p.Info, "Rendered");
        }

        /// <summary>Raises Finished (once, after Starting; Rendered first when it has not been raised).</summary>
        public static void RaiseFinished(Pending p, bool ok, string path, long bytes, string error)
        {
            if (p == null || p.Info == null || p.FinishedRaised) return;
            RaiseRendered(p);
            p.FinishedRaised = true;
            var r = new CaptureResultInfo(p.Info, ok, ok ? path : null, ok ? bytes : 0, ok ? null : (error ?? "the capture failed"), p.Clock?.Elapsed.TotalMilliseconds ?? 0);
            var h = Finished;
            if (h == null) return;
            delivering++;
            try
            {
                foreach (Action<CaptureResultInfo> d in h.GetInvocationList())
                {
                    try { d(r); }
                    catch (Exception e) { Debug.LogWarning("[capture] a Finished handler threw: " + e); }
                }
            }
            finally { delivering--; }
        }

        static void Raise(Action<CaptureInfo> h, CaptureInfo i, string what)
        {
            if (h == null) return;
            delivering++;
            try
            {
                foreach (Action<CaptureInfo> d in h.GetInvocationList())
                {
                    try { d(i); }
                    catch (Exception e) { Debug.LogWarning("[capture] a " + what + " handler threw: " + e); }
                }
            }
            finally { delivering--; }
        }
    }
}
