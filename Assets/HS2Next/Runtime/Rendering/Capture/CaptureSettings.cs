// The screenshot settings in force (R12–R16; Phase 3.6 package C): what F11,
// the dialog and the bridge `screenshot` use when a request leaves a member out. The values live in the app options
// (<UserData>/config/options.json, keys below, P's option rows); the Studio UI's screenshot module copies them here at
// start and on every change, so the bridge without the UI uses the defaults. Also the request a setting set produces
// for a view (Plan) and the image option images.embed (FileRefs.FromPicked).
using System;
using UnityEngine;

namespace HS2Next.Screenshots
{
    public static class CaptureSettings
    {
        // option keys (OptionDefs rows; strings.tsv labels option.<key>)
        public const string KeyMode = "capture.mode", KeyScale = "capture.scale", KeyWidth = "capture.width", KeyHeight = "capture.height",
            KeySupersample = "capture.supersample", KeyFrame = "capture.frame", KeyMaxPixels = "capture.maxPixels", KeyEmbed = "images.embed";

        public const string ModeFit = "fit", ModeViewport = "viewport", ModeCustom = "custom";
        public static readonly string[] Modes = { ModeFit, ModeViewport, ModeCustom };

        /// <summary>fit (default, R13) | viewport | custom.</summary>
        public static string Mode = ModeFit;
        /// <summary>1–4 (fit / viewport).</summary>
        public static float Scale = 1f;
        /// <summary>The custom size (16–8192 each).</summary>
        public static int Width = 1920, Height = 1080;
        /// <summary>1 / 2 / 4 (R14).</summary>
        public static int Supersample = 1;
        /// <summary>The frame image in screenshots (vanilla: yes, R16).</summary>
        public static bool Frame = true;
        /// <summary>images.embed (R18): picked non-DefaultData images are embedded.</summary>
        public static bool Embed = true;

        /// <summary>Puts every member back to its default (tests).</summary>
        public static void Reset()
        {
            Mode = ModeFit; Scale = 1f; Width = 1920; Height = 1080; Supersample = 1; Frame = true; Embed = true;
            CaptureLimits.MaxPixels = CaptureLimits.DefaultMaxPixels;
        }

        /// <summary>Sets one member from an option value (unknown keys and unreadable values are ignored).</summary>
        public static void Set(string key, object v)
        {
            try
            {
                switch (key)
                {
                    case KeyMode: if (v is string m && Array.IndexOf(Modes, m) >= 0) Mode = m; break;
                    case KeyScale: Scale = Mathf.Clamp(Convert.ToSingle(v, System.Globalization.CultureInfo.InvariantCulture), 1f, 4f); break;
                    case KeyWidth: Width = Mathf.Clamp(Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture), CaptureLimits.MinSide, CaptureLimits.MaxSide); break;
                    case KeyHeight: Height = Mathf.Clamp(Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture), CaptureLimits.MinSide, CaptureLimits.MaxSide); break;
                    case KeySupersample:
                    {
                        int k = Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture);
                        if (CaptureLimits.ValidSupersample(k)) Supersample = k;
                        break;
                    }
                    case KeyFrame: if (v is bool f) Frame = f; break;
                    case KeyEmbed: if (v is bool e) Embed = e; break;
                    case KeyMaxPixels: CaptureLimits.MaxPixels = Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture); break;
                }
            }
            catch (Exception) { }
        }

        /// <summary>
        /// The output size of a mode for a view: fit (the viewport's aspect inside the window, × scale), viewport (× scale),
        /// custom (W × H).
        /// </summary>
        public static Vector2Int OutputSize(string mode, float scale, int customW, int customH, Camera view, int windowW, int windowH)
        {
            int vw = view ? view.pixelWidth : windowW, vh = view ? view.pixelHeight : windowH;
            switch (mode ?? ModeFit)
            {
                case ModeViewport: return CaptureLimits.Viewport(vw, vh, scale);
                case ModeCustom: return new Vector2Int(customW, customH);
                default: return CaptureLimits.Fit(vw, vh, windowW, windowH, scale);
            }
        }

        /// <summary>The request of the current settings for <paramref name="view"/> in a window of the screen's size.</summary>
        public static CaptureRequest Current(Camera view)
        {
            var s = OutputSize(Mode, Scale, Width, Height, view, Screen.width, Screen.height);
            return new CaptureRequest { Width = s.x, Height = s.y, Supersample = Supersample, Frame = Frame };
        }
    }
}
