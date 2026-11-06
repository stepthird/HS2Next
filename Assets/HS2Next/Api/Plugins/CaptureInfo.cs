// What the capture events of IAppEvents carry. A screenshot (the screenshot key, the toolbar button, the screenshot dialog,
// the capture.screenshot UI action, the screenshot command) and the capture command raise, on the main thread,
// CaptureStarting right before the render, CaptureRendered right after it (also when the render failed) and CaptureFinished
// when the file is written or the capture failed: for a screenshot a few frames after the render, for the capture command in
// the same frame. A frame recording (the record.start command, the Export frames dialog) raises the three for every recorded
// frame, with the kind Record; its CaptureFinished comes a few frames after the render. A request refused before anything
// renders raises none; scene and character thumbnails and test renders raise none. Part of the stable API (assembly
// HS2Next.Api).
namespace HS2Next.Api.Plugins
{
    /// <summary>What kind of capture it is.</summary>
    public enum CaptureKind
    {
        /// <summary>A screenshot: a file under &lt;UserData&gt;/cap/.</summary>
        Screenshot = 0,
        /// <summary>The <c>capture</c> command: a file under the bridge output root's <c>shots/</c>.</summary>
        Capture = 1,
        /// <summary>
        /// One frame of a frame recording (the <c>record.start</c> command or the Export frames dialog): a numbered file in the
        /// recording's folder. Every recorded frame raises the three capture events.
        /// </summary>
        Record = 2,
    }

    /// <summary>Where a capture was asked for.</summary>
    public enum CaptureOrigin
    {
        /// <summary>The Studio UI: the screenshot key, the toolbar button, the screenshot dialog, the <c>capture.screenshot</c> UI action.</summary>
        Ui = 0,
        /// <summary>A bridge command (<c>screenshot</c>, <c>capture</c>), over HTTP or in-process.</summary>
        Command = 1,
    }

    /// <summary>One capture as it renders. Created by the host; the three events of one capture carry the same instance.</summary>
    public sealed class CaptureInfo
    {
        /// <summary>Creates the description (the host does; plugins read it).</summary>
        public CaptureInfo(int id, CaptureKind kind, CaptureOrigin origin, int width, int height, int supersample, int renderWidth,
            int renderHeight, bool drawsFrame, int frameNumber, string path)
        {
            Id = id;
            Kind = kind;
            Origin = origin;
            Width = width;
            Height = height;
            Supersample = supersample;
            RenderWidth = renderWidth;
            RenderHeight = renderHeight;
            DrawsFrame = drawsFrame;
            FrameNumber = frameNumber;
            Path = path;
        }

        /// <summary>The capture's number, counted from 1 in each process.</summary>
        public int Id { get; }

        /// <summary>A screenshot, the capture command or a recorded frame.</summary>
        public CaptureKind Kind { get; }

        /// <summary>The UI or a command.</summary>
        public CaptureOrigin Origin { get; }

        /// <summary>The width of the written image, pixels.</summary>
        public int Width { get; }

        /// <summary>The height of the written image, pixels.</summary>
        public int Height { get; }

        /// <summary>The supersampling factor (1, 2 or 4): the render is this many times the image size on each axis.</summary>
        public int Supersample { get; }

        /// <summary>The width of the render, pixels.</summary>
        public int RenderWidth { get; }

        /// <summary>The height of the render, pixels.</summary>
        public int RenderHeight { get; }

        /// <summary>True when the scene's frame image is drawn into the capture.</summary>
        public bool DrawsFrame { get; }

        /// <summary>The frame in which the capture renders (for the capture command the <c>frame</c> of its reply).</summary>
        public int FrameNumber { get; }

        /// <summary>The file the capture will write (absolute).</summary>
        public string Path { get; }

        /// <summary>"capture 3 Screenshot Ui 1920x1080 x2" for logs.</summary>
        public override string ToString() => "capture " + Id + " " + Kind + " " + Origin + " " + Width + "x" + Height + " x" + Supersample;
    }

    /// <summary>The end of one capture: the file written, or the failure. Created by the host.</summary>
    public sealed class CaptureResultInfo
    {
        /// <summary>Creates the result (the host does; plugins read it).</summary>
        public CaptureResultInfo(CaptureInfo capture, bool ok, string path, long bytes, string error, double milliseconds)
        {
            Capture = capture;
            Ok = ok;
            Path = path;
            Bytes = bytes;
            Error = error;
            Milliseconds = milliseconds;
        }

        /// <summary>The capture, as <see cref="IAppEvents.CaptureStarting"/> carried it.</summary>
        public CaptureInfo Capture { get; }

        /// <summary>True when the file was written.</summary>
        public bool Ok { get; }

        /// <summary>The written file (absolute), or null on failure.</summary>
        public string Path { get; }

        /// <summary>The size of the written file in bytes; 0 on failure.</summary>
        public long Bytes { get; }

        /// <summary>The error message on failure, else null.</summary>
        public string Error { get; }

        /// <summary>Milliseconds from the start of the capture to its end.</summary>
        public double Milliseconds { get; }

        /// <summary>"capture 3 ok 812345 B" for logs.</summary>
        public override string ToString() => "capture " + (Capture?.Id ?? 0) + (Ok ? " ok " + Bytes + " B" : " failed: " + Error);
    }
}
