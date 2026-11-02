// App options (U32): <UserData>/config/options.json, versioned, written on change.
//   { "version": 1, "general": { "language": "ja", … }, "camera": { … }, "gizmo": { … }, "add": { … }, "view": { … },
//     "kinematics": { … }, "rendering": { … } }
// Each option has a dotted key "<section>.<name>" (OptionDefs). Precedence (high → low): command line > options.json >
// Config/settings.local.json `ui` block (dev defaults) > built-in defaults (OptionLayers.Resolve). The `rendering`
// section and every unknown member are kept verbatim for the round trip (the rendering members are the existing
// `app.options render` ones, owned by the app). Unity-free.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.UI.Core
{
    public enum OptionKind { Bool, Int, Float, String, Choice }

    /// <summary>One option: key, kind, default, range or choices, label key (strings.tsv: "option." + key), options-panel section.</summary>
    public sealed class OptionDef
    {
        public string Key;
        public OptionKind Kind;
        public object Default;
        public double? Min, Max;
        public IReadOnlyList<string> Choices;
        /// <summary>Options panel section id (general, camera, gizmo, add, view, kinematics).</summary>
        public string Section;
        /// <summary>The vanilla option it corresponds to (for the docs and the panel's tooltip), or null.</summary>
        public string Vanilla;
        public string LabelKey => "option." + Key;

        /// <summary>The label key of a choice ("lang.ja", "keys.preset.hs2studio", "option.add.placement.pivot"); null for
        /// numeric choices shown as typed (hs2SnapStep).</summary>
        public string ChoiceLabelKey(string choice)
        {
            if (Kind != OptionKind.Choice || choice == null) return null;
            if (Key == OptionDefs.Language) return "lang." + choice;
            if (Key == OptionDefs.KeyPreset) return "keys.preset." + choice;
            if (Key == OptionDefs.GizmoHs2SnapStep) return null;
            return LabelKey + "." + choice;
        }

        /// <summary>The value coerced to the option's kind and range (null when it cannot be read as that kind).</summary>
        public object Coerce(object v)
        {
            if (v is JValue jv) v = jv.Value;
            switch (Kind)
            {
                case OptionKind.Bool:
                    if (v is bool b) return b;
                    if (v is string sb && bool.TryParse(sb, out bool pb)) return pb;
                    return null;
                case OptionKind.Int:
                {
                    double? d = AsDouble(v);
                    if (d == null) return null;
                    return (int)Math.Round(Clamp(d.Value), MidpointRounding.AwayFromZero);
                }
                case OptionKind.Float:
                {
                    double? d = AsDouble(v);
                    return d == null ? (object)null : Clamp(d.Value);
                }
                case OptionKind.Choice:
                {
                    string s = v as string ?? (v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture));
                    return s != null && Choices.Contains(s) ? s : null;
                }
                default:
                    return v as string ?? (v == null ? null : Convert.ToString(v, CultureInfo.InvariantCulture));
            }
        }

        double Clamp(double d)
        {
            if (Min.HasValue && d < Min.Value) d = Min.Value;
            if (Max.HasValue && d > Max.Value) d = Max.Value;
            return d;
        }

        static double? AsDouble(object v)
        {
            switch (v)
            {
                case double d: return double.IsNaN(d) || double.IsInfinity(d) ? (double?)null : d;
                case float f: return float.IsNaN(f) || float.IsInfinity(f) ? (double?)null : f;
                case int i: return i;
                case long l: return l;
                case string s: return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double p) ? p : (double?)null;
                default: return null;
            }
        }
    }

    /// <summary>Every option (and the view toggles).</summary>
    public static class OptionDefs
    {
        public static readonly IReadOnlyList<OptionDef> All = Build();
        static readonly Dictionary<string, OptionDef> byKey = All.ToDictionary(d => d.Key, StringComparer.Ordinal);

        public static bool TryGet(string key, out OptionDef def) => byKey.TryGetValue(key ?? "", out def);
        public static OptionDef Get(string key) => TryGet(key, out var d) ? d : throw new KeyNotFoundException("unknown option " + key);

        // keys used by code (constants so call sites are checked)
        public const string Language = "general.language", UiScale = "general.uiScale", KeyPreset = "general.keyPreset",
            ConfirmDelete = "general.confirmDelete", RecentCount = "general.recentCount";
        /// <summary>The update check at start (after 1.0.0; default on).</summary>
        public const string UpdateCheck = "general.updateCheck";
        public const string CameraRotSpeedX = "camera.rotSpeedX", CameraRotSpeedY = "camera.rotSpeedY", CameraSpeed = "camera.speed",
            CameraConstantSpeed = "camera.constantSpeed", CameraRequireAlt = "camera.requireAlt", CameraKeySpeed = "camera.keySpeed",
            CameraInvertY = "camera.invertY", CameraWheelDolly = "camera.wheelDolly";
        public const string GizmoSize = "gizmo.size", GizmoSnapMove = "gizmo.snapMove", GizmoSnapRotate = "gizmo.snapRotate",
            GizmoSnapScale = "gizmo.snapScale", GizmoHs2SnapStep = "gizmo.hs2SnapStep", GizmoFkSize = "gizmo.fkSize",
            GizmoIkSize = "gizmo.ikSize", GizmoLookSize = "gizmo.lookSize";
        public const string AddPlacement = "add.placement", AddUseCardState = "add.useCardState";
        public const string ViewGizmos = "view.gizmos", ViewPlanes = "view.planes", ViewCentre = "view.centre",
            ViewPivotMarker = "view.pivotMarker", ViewGimmickIcons = "view.gimmickIcons", ViewLightHelpers = "view.lightHelpers",
            ViewCameraIcons = "view.cameraIcons", ViewKinematicHandles = "view.kinematicHandles", ViewSpheres = "view.spheres",
            ViewFps = "view.fps";
        public const string FkColors = "kinematics.fkColors", FkLines = "kinematics.fkLines", KinExclusive = "kinematics.exclusive",
            KinRotationLimits = "kinematics.rotationLimits", KinLimitMinInterior = "kinematics.limitMinInterior",
            KinMissingTargetsFirstFrame = "kinematics.missingTargetsFirstFrame";
        // Phase 3.6: routes, the step length, screenshots, picked images. Their key
        // prefixes are their options.json sections; on the options panel they sit in existing sections (general / add / view /
        // gizmo / rendering) so the panel's section list needs no change.
        public const string RoutesTiming = "routes.timing", RoutesOrientSmoothing = "routes.orientSmoothing",
            RoutesPointLimit = "routes.pointLimit", RoutesLineWidth = "routes.lineWidth", ViewRoutes = "view.routes";
        public const string TimeStepDt = "time.stepDt";
        public const string CaptureMode = "capture.mode", CaptureScale = "capture.scale", CaptureWidth = "capture.width",
            CaptureHeight = "capture.height", CaptureAspectLock = "capture.aspectLock", CaptureSupersample = "capture.supersample",
            CaptureFrame = "capture.frame", CaptureMaxPixels = "capture.maxPixels", ImagesEmbed = "images.embed";
        /// <summary>The default render cap (3840 × 2160 × 2, R12) and the most the setting allows (7680 × 4320).</summary>
        public const int CaptureDefaultMaxPixels = 16588800, CaptureHardMaxPixels = 33177600;
        // #G36 (Phase 3.6 integrator): the view's clear colour (HS2's background colour; "" = the settings file's render
        // block, else "#RRGGBB", applied to RenderOptions.backgroundColor) and the shader-time scope of the global pause
        // (all / captures / off, SceneShaderTime.Scope); both in the panel's Rendering section.
        public const string RenderBackgroundColor = "render.backgroundColor", RenderSceneShaderTime = "render.sceneShaderTime";
        // #R5M (5.M package R): the wide screen-space mosaic over the genital-area parts and their outline (MosaicWide; on by
        // default; it only adds coverage to the mosaic that is always drawn)
        public const string RenderMosaicWide = "render.mosaicWide";
        // #W46 (Phase 4.6 package W): the Maker's file view state (the File page)
        public const string MakerThumbnailFraming = "maker.thumbnailFraming", MakerLoadFlags = "maker.loadFlags",
            MakerLoadFlagsAll = "face,body,hair,coordinate,profile";
        // #U2T2 (v0.3 trial 2 package U2): the thumbnail cell width of each grid kind (view state; px): the character browser
        // and the Maker's face, body, hair, clothes and accessory lists (every list of the kind shares it)
        public const string ThumbSizePrefix = "view.thumbSize.", ThumbSizeChara = "view.thumbSize.chara";
        /// <summary>The Maker list kinds with a thumbnail size option (view.thumbSize.&lt;kind&gt;; a new array per call: Build runs
        /// before the static fields below All are set).</summary>
        public static IReadOnlyList<string> MakerThumbKinds => new[] { "face", "body", "hair", "clothes", "accessories" };
        /// <summary>The cell widths: the character cards 126 px (63–189), the Maker cells 72 px (48–128).</summary>
        public const int CharaThumbDefault = 126, CharaThumbMin = 63, CharaThumbMax = 189, MakerThumbDefault = 72, MakerThumbMin = 48, MakerThumbMax = 128;
        /// <summary>The thumbnail size option of a grid kind ("chara", "face" …).</summary>
        public static string ThumbSizeKey(string kind) => ThumbSizePrefix + kind;

        static List<OptionDef> Build()
        {
            var l = new List<OptionDef>();
            void B(string key, bool def, string section, string vanilla = null) =>
                l.Add(new OptionDef { Key = key, Kind = OptionKind.Bool, Default = def, Section = section, Vanilla = vanilla });
            void F(string key, double def, double? min, double? max, string section, string vanilla = null) =>
                l.Add(new OptionDef { Key = key, Kind = OptionKind.Float, Default = def, Min = min, Max = max, Section = section, Vanilla = vanilla });
            void I(string key, int def, double? min, double? max, string section, string vanilla = null) =>
                l.Add(new OptionDef { Key = key, Kind = OptionKind.Int, Default = def, Min = min, Max = max, Section = section, Vanilla = vanilla });
            void C(string key, string def, string[] choices, string section, string vanilla = null) =>
                l.Add(new OptionDef { Key = key, Kind = OptionKind.Choice, Default = def, Choices = choices, Section = section, Vanilla = vanilla });

            C(Language, L10nTable.En, new[] { L10nTable.En, L10nTable.Ja, L10nTable.ZhHans }, "general");
            I(UiScale, 100, 75, 200, "general");
            C(KeyPreset, Keymap.DefaultPreset, new[] { Keymap.DefaultPreset, Keymap.Hs2StudioPreset }, "general");
            B(ConfirmDelete, false, "general");
            I(RecentCount, 8, 0, 20, "general");
            B(UpdateCheck, true, "general");   // after 1.0.0: the start check of the distributed build

            F(CameraRotSpeedX, 1, 0.1, 4, "camera", "cameraSpeedX");
            F(CameraRotSpeedY, 1, 0.1, 4, "camera", "cameraSpeedY");
            F(CameraSpeed, 1, 0.1, 5, "camera", "cameraSpeed");
            B(CameraConstantSpeed, false, "camera");
            B(CameraRequireAlt, false, "camera", "useAlt");
            F(CameraKeySpeed, 1, 0.1, 5, "camera");
            B(CameraInvertY, false, "camera");
            B(CameraWheelDolly, true, "camera");

            I(GizmoSize, 96, 48, 192, "gizmo", "manipulateSize");
            F(GizmoSnapMove, 0.1, 0.001, 100, "gizmo");
            F(GizmoSnapRotate, 15, 0.1, 180, "gizmo");
            F(GizmoSnapScale, 0.1, 0.001, 10, "gizmo");
            C(GizmoHs2SnapStep, "0.1", new[] { "0.01", "0.1", "1" }, "gizmo", "snap");
            F(GizmoFkSize, 0.5, 0.1, 1, "gizmo");
            F(GizmoIkSize, 0.5, 0.1, 1, "gizmo");
            F(GizmoLookSize, 0.5, 0.1, 1, "gizmo");
            B(ViewSpheres, false, "gizmo", "selectedState");
            B(ViewPlanes, true, "gizmo", "visibleAxisTranslation");
            C(FkColors, "palette", new[] { "palette", "hs2" }, "gizmo", "colorFK");
            B(FkLines, true, "gizmo", "lineFK");

            C(AddPlacement, "pivot", new[] { "pivot", "origin" }, "add", "initialPosition");
            B(AddUseCardState, false, "add");

            B(ViewGizmos, true, "view", "visibleAxis");
            B(ViewCentre, true, "view", "visibleAxisCenter");
            B(ViewPivotMarker, true, "view", "visibleCenter");
            B(ViewGimmickIcons, true, "view", "visibleGimmick");
            C(ViewLightHelpers, "selected", new[] { "selected", "all", "none" }, "view", "drawTarget");
            B(ViewCameraIcons, true, "view");
            B(ViewKinematicHandles, true, "view");
            B(ViewFps, false, "view");

            B(KinExclusive, false, "kinematics");
            B(KinRotationLimits, false, "kinematics");
            F(KinLimitMinInterior, 20, 0, 90, "kinematics");
            B(KinMissingTargetsFirstFrame, false, "kinematics");

            // Phase 3.6
            C(RoutesTiming, "hs2", new[] { "hs2", "length" }, "general");
            B(RoutesOrientSmoothing, true, "general");
            F(TimeStepDt, 1.0 / 60.0, 0.001, 1, "general");
            B(RoutesPointLimit, true, "add", "routePointLimit");
            B(ViewRoutes, true, "view");
            F(RoutesLineWidth, 1, 0.1, 4, "gizmo", "routeLineWidth");
            C(CaptureMode, "fit", new[] { "fit", "viewport", "custom" }, "rendering");
            F(CaptureScale, 1, 1, 4, "rendering");
            I(CaptureWidth, 1920, 16, 8192, "rendering");
            I(CaptureHeight, 1080, 16, 8192, "rendering");
            B(CaptureAspectLock, true, "rendering");
            C(CaptureSupersample, "1", new[] { "1", "2", "4" }, "rendering");
            B(CaptureFrame, true, "rendering");
            I(CaptureMaxPixels, CaptureDefaultMaxPixels, 1000000, CaptureHardMaxPixels, "rendering");
            B(ImagesEmbed, true, "rendering");
            // #G36
            l.Add(new OptionDef { Key = RenderBackgroundColor, Kind = OptionKind.String, Default = "", Section = "rendering", Vanilla = "GraphicData.BackColor" });
            C(RenderSceneShaderTime, "all", new[] { "all", "captures", "off" }, "rendering");
            B(RenderMosaicWide, true, "rendering");   // #R5M
            // #C40 (Phase 4.0 package C): Maker item changes keep clothes / accessory / hair decoration colours (C7, default off)
            B("maker.keepColours", false, "general");
            C("maker.bodyThrottle", "auto", new[] { "auto", "always", "off" }, "general");   // #W42 (Phase 4.2 package W): body composite drag throttle (Y4)
            B("maker.hairHandles", true, "gizmo");   // #W43 (Phase 4.3 package W): hair bundle handles of the Hair page (J14)
            // #W45 (Phase 4.5 package W): accessory handles (XQ15), the copy section's left / right
            // parent swap (view state), plugin data on slot copies (XQ11, default off = vanilla), the step rate of each transform set
            B("maker.accessoryHandles", true, "gizmo");
            B("maker.accessoryMirrorParent", false, "general");
            B("maker.copyAccessoryPluginData", false, "general");
            C("maker.accessoryStep.1", "normal", new[] { "fine", "normal", "coarse" }, "general");
            C("maker.accessoryStep.2", "normal", new[] { "fine", "normal", "coarse" }, "general");
            // #W46 (Phase 4.6 package W): the character thumbnail framing of the File page and the Studio inspector's save (view
            // state, default the current view) and the parts the character load dialog takes (view state: the flag names face,
            // body, hair, coordinate, profile separated by commas, default all five; "" = none ticked)
            C(MakerThumbnailFraming, "view", new[] { "front", "view" }, "general");
            l.Add(new OptionDef { Key = MakerLoadFlags, Kind = OptionKind.String, Default = MakerLoadFlagsAll, Section = "general" });
            // #U2T2
            I(ThumbSizeChara, CharaThumbDefault, CharaThumbMin, CharaThumbMax, "view");
            foreach (var k in MakerThumbKinds) I(ThumbSizeKey(k), MakerThumbDefault, MakerThumbMin, MakerThumbMax, "view");
            return l;
        }

        /// <summary>Flat keys of the settings.local.json `ui` block that map onto options (the rest of the block uses the
        /// options.json shape: { "camera": { "speed": 2 } }).</summary>
        public static readonly IReadOnlyDictionary<string, string> SettingsAliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["language"] = Language,
            ["scale"] = UiScale,
            ["uiScale"] = UiScale,
            ["preset"] = KeyPreset,
            ["keyPreset"] = KeyPreset,
        };
    }

    /// <summary>options.json: the raw object (unknown members kept) with typed access by option key.</summary>
    public sealed class OptionsDocument
    {
        public const int CurrentVersion = 1;
        public const string FileName = "options.json";

        JObject root;

        /// <summary>The version read (0 = none).</summary>
        public int Version { get; private set; } = CurrentVersion;

        public OptionsDocument() { root = new JObject(); }

        OptionsDocument(JObject o, int version) { root = o; Version = version; }

        public static OptionsDocument Parse(string json, List<string> problems = null)
        {
            JObject o;
            try { o = JObject.Parse(json ?? ""); }
            catch (JsonException e) { problems?.Add("options.json unreadable: " + e.Message); return new OptionsDocument(); }
            int v = o["version"]?.Type == JTokenType.Integer ? (int)o["version"] : 0;
            if (v > CurrentVersion) problems?.Add("options.json version " + v + " is newer than " + CurrentVersion);
            o.Remove("version");
            var d = new OptionsDocument(o, v);
            foreach (var def in OptionDefs.All)
            {
                var t = d.Token(def.Key);
                if (t != null && def.Coerce(t) == null) problems?.Add(def.Key + ": unreadable value " + t.ToString(Formatting.None) + " ignored");
            }
            return d;
        }

        public string ToJson()
        {
            var o = new JObject { ["version"] = CurrentVersion };
            foreach (var p in root.Properties()) o[p.Name] = p.Value.DeepClone();
            return o.ToString(Formatting.Indented);
        }

        JToken Token(string key)
        {
            int dot = key.IndexOf('.');
            if (dot <= 0) return root[key];
            return (root[key.Substring(0, dot)] as JObject)?[key.Substring(dot + 1)];
        }

        /// <summary>Whether the file sets the option (with a readable value).</summary>
        public bool Has(string key) => OptionDefs.TryGet(key, out var d) ? Token(key) != null && d.Coerce(Token(key)) != null : Token(key) != null;

        /// <summary>The file's value coerced to the option's kind, or null when absent / unreadable.</summary>
        public object Get(string key)
        {
            var t = Token(key);
            if (t == null) return null;
            return OptionDefs.TryGet(key, out var d) ? d.Coerce(t) : (t as JValue)?.Value;
        }

        /// <summary>Sets an option (coerced and clamped for known keys; null removes it).</summary>
        public void Set(string key, object value)
        {
            int dot = key.IndexOf('.');
            if (dot <= 0) throw new ArgumentException("option keys are <section>.<name>: " + key);
            string sec = key.Substring(0, dot), name = key.Substring(dot + 1);
            if (value == null)
            {
                (root[sec] as JObject)?.Remove(name);
                return;
            }
            object v = OptionDefs.TryGet(key, out var d) ? d.Coerce(value) ?? throw new ArgumentException(key + ": bad value " + value) : value;
            if (!(root[sec] is JObject so)) root[sec] = so = new JObject();
            so[name] = JToken.FromObject(v);
        }

        /// <summary>A section kept verbatim (rendering), or null.</summary>
        public JObject Section(string name) => root[name] as JObject;

        public OptionsDocument Clone() => new OptionsDocument((JObject)root.DeepClone(), Version);
    }

    /// <summary>The effective options from the layers (command line > options.json > settings `ui` block > defaults).</summary>
    public sealed class OptionLayers
    {
        /// <summary>Command-line values by option key (strings as typed, coerced here).</summary>
        public readonly Dictionary<string, string> CommandLine = new Dictionary<string, string>(StringComparer.Ordinal);
        public OptionsDocument File = new OptionsDocument();
        /// <summary>The `ui` block of Config/settings.local.json (flat aliases or the options.json shape), or null.</summary>
        public JObject Settings;

        /// <summary>The effective value of an option and the layer it came from ("commandLine", "file", "settings", "default").</summary>
        public object Get(string key, out string source)
        {
            var def = OptionDefs.Get(key);
            if (CommandLine.TryGetValue(key, out string cl))
            {
                var v = def.Coerce(cl);
                if (v != null) { source = "commandLine"; return v; }
            }
            var f = File?.Get(key);
            if (f != null) { source = "file"; return f; }
            var s = FromSettings(def);
            if (s != null) { source = "settings"; return s; }
            source = "default";
            return def.Default;
        }

        public object Get(string key) => Get(key, out _);
        public bool GetBool(string key) => (bool)Get(key);
        public double GetFloat(string key) => Convert.ToDouble(Get(key), CultureInfo.InvariantCulture);
        public int GetInt(string key) => Convert.ToInt32(Get(key), CultureInfo.InvariantCulture);
        public string GetString(string key) => Convert.ToString(Get(key), CultureInfo.InvariantCulture);

        object FromSettings(OptionDef def)
        {
            if (Settings == null) return null;
            foreach (var kv in OptionDefs.SettingsAliases)
                if (kv.Value == def.Key && Settings[kv.Key] != null)
                {
                    var v = def.Coerce(Settings[kv.Key]);
                    if (v != null) return v;
                }
            int dot = def.Key.IndexOf('.');
            var t = (Settings[def.Key.Substring(0, dot)] as JObject)?[def.Key.Substring(dot + 1)];
            return t == null ? null : def.Coerce(t);
        }

        /// <summary>Every option's effective value (for `ui.state` and the options panel).</summary>
        public SortedDictionary<string, object> Effective()
        {
            var d = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (var def in OptionDefs.All) d[def.Key] = Get(def.Key);
            return d;
        }
    }
}
