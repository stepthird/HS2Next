// Phase 3.6 schema rows: the route inspector (Route; Route point on a
// sub-selected point or aid), the scene panel's Background / frame and Routes sections, and the screenshot dialog's
// option rows. Ranges: point speed 0.1–30 with one decimal (HS2's slider, scene model A.1), typed values clamped to the
// same range. Ease choices (R10): HS2's six panel entries first, then the other 26 members in enum order behind
// "More…" (RouteEaseUiCount). Writes go through InspectorWrites → StudioOps.Routes / StudioOps.SceneImages (packages R
// and C); play / stop are not recorded (R7). Unity-free.
using System.Collections.Generic;

namespace HS2Next.UI.Core
{
    public static partial class InspectorSchemas
    {
        /// <summary>The 32 StudioTween ease names in enum order (= HS2Next.Save.RouteEase; `linear` is index 21).</summary>
        public static readonly IReadOnlyList<string> RouteEaseNames = new[]
        {
            "easeInQuad", "easeOutQuad", "easeInOutQuad", "easeInCubic", "easeOutCubic", "easeInOutCubic", "easeInQuart", "easeOutQuart",
            "easeInOutQuart", "easeInQuint", "easeOutQuint", "easeInOutQuint", "easeInSine", "easeOutSine", "easeInOutSine", "easeInExpo",
            "easeOutExpo", "easeInOutExpo", "easeInCirc", "easeOutCirc", "easeInOutCirc", "linear", "spring", "easeInBounce", "easeOutBounce",
            "easeInOutBounce", "easeInBack", "easeOutBack", "easeInOutBack", "easeInElastic", "easeOutElastic", "easeInOutElastic",
        };

        /// <summary>HS2's six panel entries in panel order (直線的, 徐々に早く, 徐々に遅く, 急に早く, 急に遅く, バウンド).</summary>
        public static readonly IReadOnlyList<string> RouteEaseUi = new[] { "linear", "easeInQuad", "easeOutQuad", "easeInQuart", "easeOutQuart", "easeOutBounce" };

        /// <summary>How many leading entries of the ease row's Choices the drop-down shows before "More…" (R10).</summary>
        public const int RouteEaseUiCount = 6;

        /// <summary>The ease row's choices: the six UI entries, then the remaining 26 in enum order.</summary>
        public static IReadOnlyList<string> RouteEaseChoices()
        {
            var l = new List<string>(RouteEaseUi);
            foreach (var n in RouteEaseNames) if (!l.Contains(n)) l.Add(n);
            return l;
        }

        /// <summary>Label key of an ease name ("route.ease.easeOutBounce").</summary>
        public static string RouteEaseLabelKey(string name) => "route.ease." + name;

        /// <summary>The point-row template placeholder: the sub-selected point's index in `route.points`.</summary>
        public const string RoutePointPlaceholder = "point";

        /// <summary>HS2's point speed range (slider and typed clamp).</summary>
        public const double RouteSpeedMin = 0.1, RouteSpeedMax = 30;

        static List<SchemaSection> BuildRoute()
        {
            var l = new List<SchemaSection>();

            var r = new SchemaSection("route", "inspector.route.section.route");
            var b = new B(r, "inspector.route.", OpLabels.Route);
            b.Button(ActionIds.RoutePlay, "play", "").Gate = SchemaGates.RouteCanPlay;
            b.Button(ActionIds.RouteStop, "stop", "");
            b.Button(ActionIds.RouteAddPoint, "addPoint", OpLabels.AddRoutePoint).Gate = SchemaGates.RouteCanAddPoint;
            b.Bool("route.loop", "loop");
            b.Bool("route.showLine", "showLine");
            b.Color("route.color", "color", false, new[] { 0.0, 0.0, 1.0, 1.0 });
            b.Enum("route.orient", "orient", new[] { "none", "xy", "y" }, "enum.routeOrient.");
            l.Add(r);

            var p = new SchemaSection("routePoint", "inspector.route.section.point");
            var pb = new B(p, "inspector.route.point.", OpLabels.RoutePoint);
            string P(string member) => "route.points[{" + RoutePointPlaceholder + "}]." + member;
            var speed = pb.FloatClamped(P("speed"), "speed", RouteSpeedMin, RouteSpeedMax);
            speed.Format = "0.0";
            speed.Gate = SchemaGates.RoutePointUnlinked;
            var ease = pb.Enum(P("ease"), "ease", null, "route.ease.");
            ease.Choices = RouteEaseChoices();
            ease.Gate = SchemaGates.RoutePointUnlinked;
            pb.Enum(P("connection"), "connection", new[] { "line", "curve" }, "enum.routeConnection.").Gate = SchemaGates.RoutePoint;
            pb.Bool(P("link"), "link").Gate = SchemaGates.RoutePointLinkable;
            pb.Button(ActionIds.RouteResetAid, "resetAid", OpLabels.RoutePoint).Gate = SchemaGates.RoutePointCurve;
            var pos = pb.Vec3(P("pos"), "pos");
            pos.Format = "0.#####";
            pos.Gate = SchemaGates.RoutePointOther;
            var rot = pb.Vec3(P("rot"), "rot");
            rot.Format = "0.#####";
            rot.Gate = SchemaGates.RoutePointStart;
            var aid = pb.Vec3(P("aid.pos"), "aid");
            aid.Format = "0.#####";
            aid.Gate = SchemaGates.RoutePointCurve;
            pb.Button(ActionIds.RouteDeletePoint, "delete", OpLabels.DeleteRoutePoint).Gate = SchemaGates.RoutePointOther;
            foreach (var f in p.Fields) f.Applies = Applies.Primary;   // one sub-selection: the primary route's point (R25)
            l.Add(p);
            return l;
        }

        static List<SchemaSection> BuildSceneImages()
        {
            const FieldTarget S = FieldTarget.Scene;
            var s = new SchemaSection("images", "inspector.scene.section.images");
            var b = new B(s, "inspector.scene.images.", OpLabels.Background, S);
            b.File("background", "background");
            b.File("frame", "frame").OpLabel = OpLabels.Frame;
            b.Button(ActionIds.FileOptions, "clearColour", "");   // the clear colour is the app option render.backgroundColor
            return new List<SchemaSection> { s };
        }

        static List<SchemaSection> BuildSceneRoutes()
        {
            var s = new SchemaSection("routes", "inspector.scene.section.routes");
            var b = new B(s, "inspector.scene.routes.", "", FieldTarget.Scene);
            b.Button(ActionIds.RoutePlayAll, "playAll", "");
            b.Button(ActionIds.RouteRestartAll, "restartAll", "");
            b.Button(ActionIds.RouteStopAll, "stopAll", "");
            return new List<SchemaSection> { s };
        }

        static List<SchemaSection> BuildCapture()
        {
            var s = new SchemaSection("capture", "inspector.capture.section");
            var b = new B(s, "option.", null, FieldTarget.Option);
            b.Enum(OptionDefs.CaptureMode, OptionDefs.CaptureMode, new[] { "fit", "viewport", "custom" }, "option." + OptionDefs.CaptureMode + ".");
            b.FloatClamped(OptionDefs.CaptureScale, OptionDefs.CaptureScale, 1, 4, 1.0).Format = "0.##";
            b.Int(OptionDefs.CaptureWidth, OptionDefs.CaptureWidth, 16, 8192, 1920);
            b.Int(OptionDefs.CaptureHeight, OptionDefs.CaptureHeight, 16, 8192, 1080);
            b.Bool(OptionDefs.CaptureAspectLock, OptionDefs.CaptureAspectLock);
            b.Enum(OptionDefs.CaptureSupersample, OptionDefs.CaptureSupersample, new[] { "1", "2", "4" }, "option." + OptionDefs.CaptureSupersample + ".");
            b.Bool(OptionDefs.CaptureFrame, OptionDefs.CaptureFrame);
            b.Button(ActionIds.CaptureScreenshot, "take").LabelKey = "inspector.capture.take";   // option table: records nothing (null label)
            return new List<SchemaSection> { s };
        }
    }
}
