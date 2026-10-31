// App commands: app.options — the process-wide parity switches for
// measurements (extension modules of later character builds, FK options, the HS2US joint-correction default of
// characters added later, the item gimmick icons, the Phase 3.3 rendering settings `render.*`, the Phase 3.4 FK / IK options
// `kinematics.*` (IkOptions), the Phase 3.6 route settings, the Phase 4.6 Maker switches `maker.*` (keep colours on an item
// change, copy accessory plugin data with a slot copy, the body composite drag throttle: the UI options of the same names
// overwrite them when they change), a rebuild of every node), runtime only, never saved, reset by a restart — and app.quit,
// allowed only with bridge.allowQuit, which ends the player or the Editor after its reply was written (ReadyGate.None:
// it works during a failed or slow start).
using System;
using System.Collections.Generic;
using System.Reflection;
using HS2Next.Api.Bridge;
using HS2Next.Chara.Ext;
using HS2Next.Motion.Corrective;
using HS2Next.Motion.Fk;
using UnityEngine;

namespace HS2Next.App.Commands
{
    public static class AppCommands
    {
        static readonly string[] FkMembers = { "resetToIdentity", "missingBonesZero", "hipsOffset" };

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("app.options", "Runtime parity switches for measurements (never saved): extension modules of later builds, FK options, the joint-correction default of later characters, item, rendering, FK / IK and route settings, the Maker switches, a rebuild.", "app")
                .Arg("ext", ArgTypes.Object, "{<member>: bool} of the character extension modules (camelCase CharaExtOptions members: enabled, moreAccessories, abmx, invisibleBody, ...); later builds")
                .Arg("fk", ArgTypes.Object, "{resetToIdentity, missingBonesZero, hipsOffset}: FkOptions (later FK applications)")
                .Arg("correctionAllOn", ArgTypes.Bool, "joint correction of characters added later all on (HS2US \"Auto Joint Correction\") or all off")
                .Arg("items", ArgTypes.Object, "{gimmickIcons: bool}: item gimmick icons shown (Studio's system-menu switch)")
                .Arg("render", ArgTypes.Object, "{aa, taaQuality, grading, ambient, lightFalloff, shadowDistance, backgroundColor, ssr (off | on; default off)}: the rendering settings (HS2Next.RenderOptions); mosaicWide (bool): the wide screen-space mosaic over the genital-area parts (default on; the option render.mosaicWide overwrites it when changed)")
                .Arg("kinematics", ArgTypes.Object, "{exclusive, rotationLimits, limitMinInterior (0–179 degrees), missingTargetsFirstFrame}: the FK / IK options (IkOptions): exclusive = switching FK on switches IK off and the reverse; missingTargetsFirstFrame applies to later builds")
                .Arg("routes", ArgTypes.Object, "{timing (hs2|length), orientSmoothing, pointLimit, lineWidth (0.1–4)}: the route settings (RouteSettings; the UI options routes.*, which overwrite them when changed)")
                .Arg("maker", ArgTypes.Object, "{keepColours: bool, copyAccessoryPluginData: bool, bodyThrottle: \"auto\" | \"always\" | \"off\"}: the Maker switches without the UI (keep colours on an item change = MakerServices.KeepColoursOnItemChange, default off; copy MaterialEditor / DynamicBoneEditor / Additional Accessory Controls data with an accessory slot copy = MakerServices.CopyAccessoryPluginData, default off; the body composite drag throttle = CharaEditFlush.BodyThrottle, default auto); the UI options maker.keepColours / maker.copyAccessoryPluginData / maker.bodyThrottle overwrite them when changed")
                .Arg("rebuild", ArgTypes.Bool, "rebuild every node (and the map) with the new options (slow)", defaultValue: false)
                .Return("ext", ArgTypes.Object, "current extension switches").Return("fk", ArgTypes.Object, "current FK options")
                .Return("correctionAllOn", ArgTypes.Bool, "current correction default")
                .Return("items", ArgTypes.Object, "current item switches {gimmickIcons}")
                .Return("render", ArgTypes.Object, "current rendering settings (render.*; taaExperimental true until U9 passes)")
                .Return("kinematics", ArgTypes.Object, "current FK / IK options {exclusive, rotationLimits, limitMinInterior, missingTargetsFirstFrame}")
                .Return("routes", ArgTypes.Object, "current route settings {timing, orientSmoothing, pointLimit, lineWidth}")
                .Return("maker", ArgTypes.Object, "current Maker switches {keepColours, copyAccessoryPluginData, bodyThrottle}")
                .Example("{\"cmd\":\"app.options\",\"ext\":{\"abmx\":false},\"correctionAllOn\":true}")
                .WithFlags(CommandFlags.Mutates | CommandFlags.Slow).WithTimeout(300), Options);

            CommandKit.Register(r, app, CommandKit.Spec("app.quit", "Ends the app after the reply (only with bridge.allowQuit).", "app")
                .Arg("code", ArgTypes.Int, "exit code", defaultValue: 0)
                .Return("quitting", ArgTypes.Bool, "true").Return("exitCode", ArgTypes.Int, "exit code (`code` is the envelope's error code member)")
                .WithGate(ReadyGate.None).WithFlags(CommandFlags.Mutates).WithTimeout(60), Quit);
        }

        // ------------------------------------------------------------ app.options

        /// <summary>The bool members of CharaExtOptions by camelCase name.</summary>
        static Dictionary<string, FieldInfo> ExtMembers()
        {
            var d = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            foreach (var f in typeof(CharaExtOptions).GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (f.FieldType == typeof(bool)) d[char.ToLowerInvariant(f.Name[0]) + f.Name.Substring(1)] = f;
            return d;
        }

        static object Options(CoreContext c)
        {
            var members = ExtMembers();
            var ext = c.Args.Nested("ext");
            var fk = c.Args.Nested("fk");
            // validate everything first: nothing is applied when a member is wrong
            var extSet = new List<(FieldInfo f, bool v)>();
            if (ext != null)
                foreach (var name in ext.Names)
                {
                    if (!members.TryGetValue(name, out var f))
                        throw new BridgeException(BridgeCodes.BadArgs, "unknown ext member \"" + name + "\"",
                            new BridgeErrorDetail { Arg = "ext." + name, Expected = "a CharaExtOptions switch", Candidates = new List<string>(members.Keys) });
                    if (ext.Kind(name) != ArgKind.Bool) throw CommandKit.BadArgs("ext." + name, ArgTypes.Bool);
                    extSet.Add((f, ext.Get<bool>(name)));
                }
            var fkSet = new List<(string name, bool v)>();
            if (fk != null)
                foreach (var name in fk.Names)
                {
                    if (Array.IndexOf(FkMembers, name) < 0)
                        throw new BridgeException(BridgeCodes.BadArgs, "unknown fk member \"" + name + "\"",
                            new BridgeErrorDetail { Arg = "fk." + name, Expected = "an FkOptions switch", Candidates = FkMembers });
                    if (fk.Kind(name) != ArgKind.Bool) throw CommandKit.BadArgs("fk." + name, ArgTypes.Bool);
                    fkSet.Add((name, fk.Get<bool>(name)));
                }
            var items = c.Args.Nested("items");
            bool? icons = null;
            if (items != null)
                foreach (var name in items.Names)
                {
                    if (name != "gimmickIcons")
                        throw new BridgeException(BridgeCodes.BadArgs, "unknown items member \"" + name + "\"",
                            new BridgeErrorDetail { Arg = "items." + name, Expected = "an item switch", Candidates = new[] { "gimmickIcons" } });
                    if (items.Kind(name) != ArgKind.Bool) throw CommandKit.BadArgs("items." + name, ArgTypes.Bool);
                    icons = items.Get<bool>(name);
                }
            bool hasCorr = c.Args.TryGet<bool>("correctionAllOn", out bool corr);
            bool rebuild = c.Args.Get("rebuild", false);
            // kinematics: validated all-or-nothing (applied with the other switches below)
            KinematicsSettings kin = null;
            if (c.Args.Kind("kinematics") == ArgKind.Object)
            {
                kin = KinematicsSettings.FromRuntime();
                try { kin.Apply(Newtonsoft.Json.Linq.JObject.Parse(c.Args.RawJson("kinematics")), "kinematics", strict: true); }
                catch (FormatException e) { throw new BridgeException(BridgeCodes.BadArgs, e.Message, new BridgeErrorDetail { Arg = "kinematics", Expected = "kinematics options", Candidates = KinematicsSettings.Members }); }
            }
            else if (c.Args.Has("kinematics")) throw CommandKit.BadArgs("kinematics", ArgTypes.Object);
            if (c.Args.Kind("render") == ArgKind.Object)
            {
                // validated and applied all-or-nothing before any other switch is applied (mosaicWide: MosaicWide, 5.M)
                var ro = Newtonsoft.Json.Linq.JObject.Parse(c.Args.RawJson("render"));
                bool? wide = null;
                if (ro.TryGetValue(MosaicWideMember, out var wt))
                {
                    if (wt.Type != Newtonsoft.Json.Linq.JTokenType.Boolean) throw new BridgeException(BridgeCodes.BadArgs, "render." + MosaicWideMember + " must be true or false", new BridgeErrorDetail { Arg = "render", Expected = "render settings" });
                    wide = (bool)wt;
                    ro.Remove(MosaicWideMember);
                }
                try { RenderOptions.Apply(ro, "render"); }
                catch (FormatException e) { throw new BridgeException(BridgeCodes.BadArgs, e.Message, new BridgeErrorDetail { Arg = "render", Expected = "render settings", Candidates = RenderOptions.Keys }); }
                if (wide.HasValue) MosaicWide.Enabled = wide.Value;
            }
            else if (c.Args.Has("render")) throw CommandKit.BadArgs("render", ArgTypes.Object);
            // routes (Phase 3.6): validated all-or-nothing, applied with the other switches below
            Action routes = null;
            if (c.Args.Kind("routes") == ArgKind.Object) routes = ParseRoutes(Newtonsoft.Json.Linq.JObject.Parse(c.Args.RawJson("routes")));
            else if (c.Args.Has("routes")) throw CommandKit.BadArgs("routes", ArgTypes.Object);
            // maker (Phase 4.6): validated all-or-nothing, applied with the other switches below
            Action maker = null;
            if (c.Args.Kind("maker") == ArgKind.Object) maker = ParseMaker(Newtonsoft.Json.Linq.JObject.Parse(c.Args.RawJson("maker")));
            else if (c.Args.Has("maker")) throw CommandKit.BadArgs("maker", ArgTypes.Object);

            var opts = c.Services.ExtOptions;
            if (extSet.Count > 0 && opts == null) c.Services.ExtOptions = opts = new CharaExtOptions();
            foreach (var (f, v) in extSet) f.SetValue(opts, v);
            foreach (var (name, v) in fkSet)
            {
                switch (name)
                {
                    case "resetToIdentity": FkOptions.ResetToIdentity = v; break;
                    case "missingBonesZero": FkOptions.MissingBonesZero = v; break;
                    case "hipsOffset": FkOptions.HipsOffset = v; break;
                }
            }
            if (hasCorr)
            {
                CorrectSetup.InitialCategoriesOn = corr;
                var d = c.Session.Options.CorrectionDefault;
                for (int i = 0; i < d.Length; i++) d[i] = corr;
            }
            if (icons.HasValue) HS2Next.Scenes.Runtime.ItemIcons.Visible = icons.Value;
            kin?.ApplyToRuntime();
            routes?.Invoke();
            maker?.Invoke();
            if (rebuild)
            {
                c.Runtime.RealizeAll();
                c.Runtime.ApplyNow();
            }
            return Current(c);
        }

        /// <summary>The current values of every switch app.options sets.</summary>
        public static BridgeReply Current(CoreContext c)
        {
            var ext = new BridgeReply();
            var opts = c.Services.ExtOptions;
            foreach (var kv in ExtMembers()) ext[kv.Key] = opts != null && (bool)kv.Value.GetValue(opts);
            var fk = new BridgeReply().With("resetToIdentity", FkOptions.ResetToIdentity).With("missingBonesZero", FkOptions.MissingBonesZero)
                .With("hipsOffset", FkOptions.HipsOffset);
            return new BridgeReply().With("ext", ext).With("fk", fk).With("correctionAllOn", CorrectSetup.InitialCategoriesOn)
                .With("items", new BridgeReply().With("gimmickIcons", HS2Next.Scenes.Runtime.ItemIcons.Visible))
                .With("render", RenderReply())
                .With("kinematics", KinematicsReply())
                .With("routes", RoutesReply())
                .With("maker", MakerReply());
        }

        /// <summary>The members of app.options maker.</summary>
        public static readonly string[] MakerMembers = { "keepColours", "copyAccessoryPluginData", "bodyThrottle" };

        /// <summary>The Maker switches as a reply object (app.options maker).</summary>
        public static BridgeReply MakerReply() =>
            new BridgeReply().With("keepColours", HS2Next.Scenes.MakerServices.KeepColoursOnItemChange)
                .With("copyAccessoryPluginData", HS2Next.Scenes.MakerServices.CopyAccessoryPluginData)
                .With("bodyThrottle", HS2Next.Scenes.Runtime.CharaEditFlush.BodyThrottleName(HS2Next.Scenes.Runtime.CharaEditFlush.BodyThrottle));

        /// <summary>Validates an app.options maker object (bad_args on any wrong member, nothing applied) and returns the action
        /// applying it.</summary>
        static Action ParseMaker(Newtonsoft.Json.Linq.JObject o)
        {
            var apply = new List<Action>();
            foreach (var p in o.Properties())
            {
                var v = p.Value;
                string arg = "maker." + p.Name;
                switch (p.Name)
                {
                    case "keepColours":
                    case "copyAccessoryPluginData":
                    {
                        if (v.Type != Newtonsoft.Json.Linq.JTokenType.Boolean) throw CommandKit.BadArgs(arg, ArgTypes.Bool);
                        bool b = (bool)v;
                        if (p.Name == "keepColours") apply.Add(() => HS2Next.Scenes.MakerServices.KeepColoursOnItemChange = b);
                        else apply.Add(() => HS2Next.Scenes.MakerServices.CopyAccessoryPluginData = b);
                        break;
                    }
                    case "bodyThrottle":
                    {
                        string s = v.Type == Newtonsoft.Json.Linq.JTokenType.String ? (string)v : null;
                        if (!HS2Next.Scenes.Runtime.CharaEditFlush.TryParseBodyThrottle(s, out var mode))
                            throw CommandKit.BadArgs(arg, ArgTypes.Enum("auto", "always", "off"));
                        apply.Add(() => HS2Next.Scenes.Runtime.CharaEditFlush.BodyThrottle = mode);
                        break;
                    }
                    default:
                        throw new BridgeException(BridgeCodes.BadArgs, "unknown maker member \"" + p.Name + "\"",
                            new BridgeErrorDetail { Arg = arg, Expected = "a Maker switch", Candidates = MakerMembers });
                }
            }
            return () => { foreach (var a in apply) a(); };
        }

        static readonly string[] RouteMembers = { "timing", "orientSmoothing", "pointLimit", "lineWidth" };

        /// <summary>RouteSettings as a reply object (app.options routes).</summary>
        public static BridgeReply RoutesReply() =>
            new BridgeReply().With("timing", HS2Next.Scenes.Runtime.RouteSettings.Timing == HS2Next.Scenes.Routes.RouteTiming.Length ? "length" : "hs2")
                .With("orientSmoothing", HS2Next.Scenes.Runtime.RouteSettings.OrientSmoothing)
                .With("pointLimit", HS2Next.Scenes.Runtime.RouteSettings.PointLimit)
                .With("lineWidth", (double)HS2Next.Scenes.Runtime.RouteSettings.LineWidth);

        /// <summary>Validates an app.options routes object (bad_args on any wrong member) and returns the action applying it.</summary>
        static Action ParseRoutes(Newtonsoft.Json.Linq.JObject o)
        {
            var apply = new List<Action>();
            foreach (var p in o.Properties())
            {
                var v = p.Value;
                string arg = "routes." + p.Name;
                switch (p.Name)
                {
                    case "timing":
                    {
                        string s = v.Type == Newtonsoft.Json.Linq.JTokenType.String ? (string)v : null;
                        if (s != "hs2" && s != "length") throw CommandKit.BadArgs(arg, ArgTypes.Enum("hs2", "length"));
                        var t = s == "length" ? HS2Next.Scenes.Routes.RouteTiming.Length : HS2Next.Scenes.Routes.RouteTiming.Hs2;
                        apply.Add(() => HS2Next.Scenes.Runtime.RouteSettings.Timing = t);
                        break;
                    }
                    case "orientSmoothing":
                    case "pointLimit":
                    {
                        if (v.Type != Newtonsoft.Json.Linq.JTokenType.Boolean) throw CommandKit.BadArgs(arg, ArgTypes.Bool);
                        bool b = (bool)v;
                        if (p.Name == "pointLimit") apply.Add(() => HS2Next.Scenes.Runtime.RouteSettings.PointLimit = b);
                        else apply.Add(() => HS2Next.Scenes.Runtime.RouteSettings.OrientSmoothing = b);
                        break;
                    }
                    case "lineWidth":
                    {
                        double w = v.Type == Newtonsoft.Json.Linq.JTokenType.Integer || v.Type == Newtonsoft.Json.Linq.JTokenType.Float ? (double)v : double.NaN;
                        if (!(w >= 0.1 && w <= 4)) throw new BridgeException(BridgeCodes.BadArgs, arg + " must be a number in 0.1–4", new BridgeErrorDetail { Arg = arg, Expected = "0.1–4" });
                        apply.Add(() => HS2Next.Scenes.Runtime.RouteSettings.LineWidth = (float)w);
                        break;
                    }
                    default:
                        throw new BridgeException(BridgeCodes.BadArgs, "unknown routes member \"" + p.Name + "\"",
                            new BridgeErrorDetail { Arg = arg, Expected = "a route setting", Candidates = RouteMembers });
                }
            }
            return () => { foreach (var a in apply) a(); };
        }

        /// <summary>IkOptions as a reply object (app.options kinematics).</summary>
        public static BridgeReply KinematicsReply() =>
            new BridgeReply().With("exclusive", HS2Next.Motion.Ik.IkOptions.Exclusive).With("rotationLimits", HS2Next.Motion.Ik.IkOptions.RotationLimits)
                .With("limitMinInterior", (double)HS2Next.Motion.Ik.IkOptions.LimitMinInterior).With("missingTargetsFirstFrame", HS2Next.Motion.Ik.IkOptions.MissingTargetsFirstFrame);

        /// <summary>RenderOptions as a reply object.</summary>
        /// <summary>The member of app.options render that switches the wide mosaic (MosaicWide; not a RenderOptions setting:
        /// no settings key).</summary>
        public const string MosaicWideMember = "mosaicWide";

        static BridgeReply RenderReply()
        {
            var bg = RenderOptions.BackgroundColor;
            return new BridgeReply().With("aa", RenderOptions.Aa).With("taaQuality", RenderOptions.TaaQuality).With("grading", RenderOptions.Grading)
                .With("ambient", RenderOptions.Ambient).With("lightFalloff", RenderOptions.LightFalloff).With("shadowDistance", RenderOptions.ShadowDistance)
                .With("backgroundColor", new[] { bg.r, bg.g, bg.b, bg.a }).With("ssr", RenderOptions.Ssr).With("taaExperimental", true)
                .With(MosaicWideMember, MosaicWide.Enabled);
        }

        // ------------------------------------------------------------ app.quit

        /// <summary>
        /// Set when the bridge's app.quit ends the player: an unattended quit is not asked about unsaved changes (the UI's
        /// Application.wantsToQuit handler lets it through). v0.2: with a changed scene the "unsaved changes" dialog cancelled
        /// the quit and the player stayed open until it was killed. Settable for tests (reset it after use).
        /// </summary>
        public static bool QuitRequested { get; set; }

        static object Quit(CoreContext c)
        {
            if (!(c.App.Settings?.Bridge.AllowQuit ?? false)) throw CommandKit.Unsupported("app.quit is disabled (bridge.allowQuit)");
            int code = c.Args.Get("code", 0);
            c.Ctx.AfterReply(() =>
            {
                Debug.Log("[app] app.quit (exit code " + code + ")");
#if UNITY_EDITOR
                if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(code);
                else UnityEditor.EditorApplication.ExitPlaymode();
#else
                QuitRequested = true;
                Application.Quit(code);
#endif
            });
            return new BridgeReply().With("quitting", true).With("exitCode", code);
        }
    }
}
