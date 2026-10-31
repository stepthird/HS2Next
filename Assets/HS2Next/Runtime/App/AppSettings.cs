// Application settings of the HS2Next app: the content settings of ContentSettings
// (hs2Root, modsFolders, cacheDir, read as before with JsonUtility) plus the members `userData`, `app {autoStart,
// frameRate, runInBackground}`, `bridge {enabled, port, token, outputDir, allowQuit}`, `mapAudioLink`, `maps {maxMirrors}` (Phase 3.2) and `render {aa, taaQuality, grading, ambient, lightFalloff,
// shadowDistance, backgroundColor}` (Phase 3.3, HS2Next.RenderOptions; applied process-wide at load), `plugins {enabled, dir,
// disabled}` (Phase 5.1, the plugin loader's folder and switch) and `kinematics {exclusive,
// rotationLimits, limitMinInterior, missingTargetsFirstFrame}` (Phase 3.4; mirrored into
// HS2Next.Motion.Ik.IkOptions at load and by app.options) of the same settings file, read with
// Newtonsoft so that an absent member keeps its default and a member of the wrong JSON type is an error.
// Precedence: Editor launch override (play-mode harness) > command line > file > defaults. The launch override may also
// name the plugin folder of the session as a top-level `pluginsDir` (the same as -pluginsDir: a test's explicit choice) and the data
// root of the session as a top-level `dataRoot` (Editor play sessions only: a test's own Config / UserData / Cache instead of the
// project's). Command line:
//   -bridge / -noBridge  -bridgePort N  -bridgeToken [value]  -bridgeOutputDir <dir>  -bridgeAllowQuit  -userData <dir>
//   -noApp (no automatic start)  -ui / -noUi (Phase 3.5 Studio UI, block `ui`)  -noPlugins / -pluginsDir <dir> (Phase 5.1)
//   and ContentSettings' -settings, -gameRoot, -cacheDir, -modsFolder.
// Relative userData / outputDir paths resolve against the data root (ContentSettings.Roots: the project folder in the Editor; the
// exe folder in the portable mode; HS2NEXT_DATA_ROOT; else the per-user folder of the player). The bridge token is required by
// default in the distributed build only (BuildInfo); bridge.token and -bridgeToken override that both ways.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using HS2Next.Api.Bridge;
using HS2Next.Content;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HS2Next.App
{
    public sealed class BridgeSettings
    {
        /// <summary>on in every build.</summary>
        public bool Enabled = true;
        public int Port = BridgeProtocol.DefaultPort;
        /// <summary>A token is required: by default only in the distributed build (build.json kind "distribution"), off in
        /// development builds and the Editor; `bridge.token` and -bridgeToken override the default both ways.</summary>
        public bool Token = BuildInfo.Current.TokenRequiredByDefault;
        /// <summary>The token itself when <see cref="Token"/> is set: <c>-bridgeToken &lt;value&gt;</c> or a string <c>bridge.token</c>;
        /// null = a random 128-bit token (package B writes either to &lt;BridgeDir&gt;/token).</summary>
        public string TokenValue;
        /// <summary>Absolute; default &lt;UserData&gt;/Bridge.</summary>
        public string OutputDir;
        /// <summary><c>app.quit</c> is allowed.</summary>
        public bool AllowQuit;

        public BridgeSettings Clone() => (BridgeSettings)MemberwiseClone();
    }

    /// <summary>
    /// The `kinematics` block (K3, K4, K9): FK / IK options read by the scene runtime through
    /// <see cref="HS2Next.Motion.Ik.IkOptions"/>. Applied process-wide when the settings load; <c>app.options kinematics</c> changes
    /// the statics at run time (never saved).
    /// </summary>
    public sealed class KinematicsSettings
    {
        /// <summary>Member names in reply order.</summary>
        public static readonly string[] Members = { "exclusive", "rotationLimits", "limitMinInterior", "missingTargetsFirstFrame" };

        /// <summary>Vanilla exclusivity preset: switching FK on switches IK off in the same entry and the reverse (K3).</summary>
        public bool Exclusive;
        /// <summary>Elbow / knee interior angle limit of the two-bone solve (K9).</summary>
        public bool RotationLimits;
        /// <summary>The smallest interior angle in degrees with <see cref="RotationLimits"/> (0–179).</summary>
        public float LimitMinInterior = 20f;
        /// <summary>HS2's first-frame copy of missing targets (vanilla parity, K4).</summary>
        public bool MissingTargetsFirstFrame;

        public KinematicsSettings Clone() => (KinematicsSettings)MemberwiseClone();

        /// <summary>The current values of the IkOptions statics.</summary>
        public static KinematicsSettings FromRuntime() => new KinematicsSettings
        {
            Exclusive = HS2Next.Motion.Ik.IkOptions.Exclusive,
            RotationLimits = HS2Next.Motion.Ik.IkOptions.RotationLimits,
            LimitMinInterior = HS2Next.Motion.Ik.IkOptions.LimitMinInterior,
            MissingTargetsFirstFrame = HS2Next.Motion.Ik.IkOptions.MissingTargetsFirstFrame,
        };

        /// <summary>Writes the values into the IkOptions statics (later operations, solves and builds read them).</summary>
        public void ApplyToRuntime()
        {
            HS2Next.Motion.Ik.IkOptions.Exclusive = Exclusive;
            HS2Next.Motion.Ik.IkOptions.RotationLimits = RotationLimits;
            HS2Next.Motion.Ik.IkOptions.LimitMinInterior = LimitMinInterior;
            HS2Next.Motion.Ik.IkOptions.MissingTargetsFirstFrame = MissingTargetsFirstFrame;
        }

        /// <summary>
        /// Applies the members of a `kinematics` object to this instance after validating all of them (nothing changes when one
        /// is wrong): bools, <c>limitMinInterior</c> a number in 0–179. <paramref name="strict"/>: an unknown member is an error
        /// (app.options); otherwise it is ignored (a settings file of a later version). Throws FormatException naming the member.
        /// </summary>
        public void Apply(JObject o, string what, bool strict)
        {
            if (o == null) return;
            var n = Clone();
            foreach (var p in o.Properties())
            {
                string w = what + "." + p.Name;
                switch (p.Name)
                {
                    case "exclusive": n.Exclusive = Bool(p.Value, w); break;
                    case "rotationLimits": n.RotationLimits = Bool(p.Value, w); break;
                    case "missingTargetsFirstFrame": n.MissingTargetsFirstFrame = Bool(p.Value, w); break;
                    case "limitMinInterior":
                        if (p.Value.Type != JTokenType.Integer && p.Value.Type != JTokenType.Float) throw new FormatException(w + " must be a number");
                        double d = (double)p.Value;
                        if (double.IsNaN(d) || d < 0 || d > 179) throw new FormatException(w + " must be in 0–179 (degrees)");
                        n.LimitMinInterior = (float)d;
                        break;
                    default:
                        if (strict) throw new FormatException(what + ": unknown member \"" + p.Name + "\" (" + string.Join(", ", Members) + ")");
                        break;
                }
            }
            Exclusive = n.Exclusive;
            RotationLimits = n.RotationLimits;
            LimitMinInterior = n.LimitMinInterior;
            MissingTargetsFirstFrame = n.MissingTargetsFirstFrame;
        }

        static bool Bool(JToken t, string what)
        {
            if (t.Type == JTokenType.Boolean) return (bool)t;
            throw new FormatException(what + " must be true or false");
        }
    }

    /// <summary>
    /// The `ui` block (Phase 3.5): `enabled` (default true) starts the Studio UI; the
    /// other members (language, scale, preset, and options.json-shaped sections) are the options layer "settings" (dev
    /// defaults). `-noUi` always wins; `-ui` (or `ui.enabled: true` in the Editor launch override) turns the UI on where it
    /// is off by default: Editor batch play sessions, `-smoke` and `-bridgeAccept` players.
    /// </summary>
    public sealed class UiSettings
    {
        /// <summary>`ui.enabled` of the settings file / launch override (default true).</summary>
        public bool Enabled = true;
        /// <summary>-ui on the command line, or `ui.enabled: true` in the launch override.</summary>
        public bool Forced;
        /// <summary>-noUi on the command line.</summary>
        public bool Off;
        /// <summary>The merged `ui` objects of the settings file and the launch override (null when absent), for the options layer.</summary>
        public JObject Block;
        /// <summary>Effective: the app starts the UI (computed when the settings finish loading).</summary>
        public bool Run;

        public UiSettings Clone()
        {
            var c = (UiSettings)MemberwiseClone();
            c.Block = (JObject)Block?.DeepClone();
            return c;
        }
    }

    public sealed class AppSettings
    {
        /// <summary>ContentSettings.LoadDefault(args): hs2Root, modsFolders, cacheDir. Null when they could not be read.</summary>
        public ContentSettings Content;
        /// <summary>The file read (-settings or ContentSettings.DefaultPath), null when absent.</summary>
        public string SettingsFile;
        /// <summary>The settings file of this run whether it exists or not (absolute): -settings, else &lt;data root&gt;/Config/
        /// settings.local.json; the first-run guide and the settings page write here.</summary>
        public string SettingsPath;
        /// <summary>The data root of this run (ContentSettings.Roots when the settings were read).</summary>
        public HS2Next.App.Setup.AppRoots Roots;
        /// <summary>Absolute.</summary>
        public string UserData;
        /// <summary>Where the folders came from: "launch" (Editor launch override), "arg" (command line), "file" (the settings file)
        /// or "default" (under the data root); "fallback" after the settings stage switched an unwritable folder.</summary>
        public string SettingsSource = "default", UserDataSource = "default", OutputDirSource = "default", CacheDirSource = "default";
        public bool AutoStart = true;
        /// <summary>Application.targetFrameRate to set; 0 = leave Unity's (batch mode: 60).</summary>
        public int FrameRate;
        /// <summary>`app.runInBackground` (default true): the app keeps running, and answering the bridge, while another window
        /// has the focus; false pauses an unfocused windowed app (batch-mode players run regardless).</summary>
        public bool RunInBackground = true;
        public BridgeSettings Bridge = new BridgeSettings();
        /// <summary>-noApp was given (no automatic start).</summary>
        public bool NoApp;
        /// <summary>`mapAudioLink` (content member, default false): the abdata junction for the map clips' relative resource
        /// paths (M9); off by default because the map clips stay silent and a user's app folder
        /// gets no link into the game data.</summary>
        public bool MapAudioLink;
        /// <summary>`maps.maxMirrors` (default 4, 0–16): mirrors rendered per frame.</summary>
        public int MaxMirrors = 4;
        /// <summary>`maps.audioLinkEditor` (default false): the abdata junction also in the Editor, where Unity looks next to the
        /// project folder (outside it).</summary>
        public bool MapAudioLinkEditor;
        /// <summary>`kinematics` (Phase 3.4): the FK / IK options, written into IkOptions when the settings finish loading.</summary>
        public KinematicsSettings Kinematics = new KinematicsSettings();
        /// <summary>`ui` (Phase 3.5): whether the Studio UI starts, and the dev defaults of its options.</summary>
        public UiSettings Ui = new UiSettings();
        /// <summary>`studio.accessoryItems` (default true): the character accessories are offered as Studio items (item group
        /// 647158); false = the group is absent.</summary>
        public bool StudioAccessoryItems = true;
        /// <summary>`plugins.enabled` (default true): plugins load from the plugin folder.</summary>
        public bool PluginsEnabled = true;
        /// <summary>`plugins.dir` (default "" = the Plugins folder next to the app; relative to the app folder).</summary>
        public string PluginsDir = "";
        /// <summary>`plugins.disabled`: GUIDs that do not load (state disabled).</summary>
        public List<string> PluginsDisabled = new List<string>();
        /// <summary>-pluginsDir &lt;dir&gt; on the command line (wins over everything), or null.</summary>
        public string PluginsDirArg;
        /// <summary>-noPlugins on the command line.</summary>
        public bool NoPlugins;

        /// <summary>The plugin folder and switch of this run (-pluginsDir, -noPlugins, HS2NEXT_NO_PLUGINS, the settings).</summary>
        public HS2Next.App.Plugins.PluginLaunch PluginLaunch() =>
            HS2Next.App.Plugins.PluginLaunch.Resolve(PluginsDirArg, NoPlugins, Environment.GetEnvironmentVariable(HS2Next.App.Plugins.PluginLaunch.EnvNoPlugins),
                PluginsEnabled, PluginsDir, PluginsDisabled, ContentSettings.ProjectRoot);

        /// <summary>The default UserData folder: &lt;data root&gt;/UserData.</summary>
        public static string DefaultUserData => ContentSettings.Roots.DefaultUserData;

        /// <summary>
        /// Reads the settings. No settings file at the default place is not an error (the defaults apply). Throws when
        /// a file named by -settings is absent, when a member has the wrong JSON type, or for a bad command-line value.
        /// </summary>
        public static AppSettings Load(string[] args = null, string launchJson = null)
        {
            args ??= Environment.GetCommandLineArgs();
            ApplyLaunchDataRoot(launchJson);
            var s = new AppSettings { Roots = ContentSettings.Roots };
            string given = Arg(args, "-settings");
            string file = given ?? ContentSettings.DefaultPath;
            s.SettingsPath = Path.GetFullPath(file);
            s.SettingsSource = given != null ? "arg" : "default";
            s.SettingsFile = File.Exists(file) ? s.SettingsPath : null;
            s.Content = ContentSettings.LoadDefault(args);
            if (Arg(args, "-cacheDir") != null) s.CacheDirSource = "arg";
            else if (!string.IsNullOrEmpty(s.Content.cacheDir)) s.CacheDirSource = "file";
            if (s.SettingsFile != null)
            {
                string text = File.ReadAllText(s.SettingsFile);
                s.ApplyJson(text, "settings file " + s.SettingsFile);
                LogUnknownKeys(s.SettingsFile, text);
            }
            s.ApplyArgs(args);
            if (!string.IsNullOrEmpty(launchJson)) s.ApplyJson(launchJson, "launch override");
            s.Finish();
            return s;
        }

        /// <summary>
        /// The settings without the content settings, for a host whose settings could not be read (the bridge still
        /// comes up and reports the failed stage): defaults, then the command line and the launch override where they
        /// parse. Never throws.
        /// </summary>
        public static AppSettings Defaults(string[] args = null, string launchJson = null)
        {
            try { ApplyLaunchDataRoot(launchJson); } catch (Exception) { }
            var s = new AppSettings();
            try
            {
                s.Roots = ContentSettings.Roots;
                string file = Arg(args ?? Environment.GetCommandLineArgs(), "-settings");
                s.SettingsSource = file != null ? "arg" : "default";
                s.SettingsPath = Path.GetFullPath(file ?? ContentSettings.DefaultPath);
            }
            catch (Exception) { }
            try { s.ApplyArgs(args ?? Environment.GetCommandLineArgs()); } catch (Exception) { }
            try { if (!string.IsNullOrEmpty(launchJson)) s.ApplyJson(launchJson, "launch override"); } catch (Exception) { }
            s.Finish();
            return s;
        }

        /// <summary>
        /// The Editor launch override's top-level `dataRoot` (a play session's own Config / UserData / Cache, created when absent):
        /// becomes the data root of the process (mode override). Players have no launch override.
        /// </summary>
        static void ApplyLaunchDataRoot(string launchJson)
        {
            if (string.IsNullOrEmpty(launchJson) || !Application.isEditor) return;
            JObject o;
            try { o = JObject.Parse(launchJson); }
            catch (Exception) { return; }   // ApplyJson reports a launch override that does not parse
            if (!o.TryGetValue("dataRoot", out var v) || v.Type == JTokenType.Null) return;
            string root = Str(v, "launch override: dataRoot");
            if (string.IsNullOrEmpty(root)) return;
            var cur = ContentSettings.Roots;
            ContentSettings.SetRoots(new HS2Next.App.Setup.AppRoots(HS2Next.App.Setup.DataRootMode.Override, Path.GetFullPath(root), cur.ExeDir, cur.PersistentDataPath));
        }

        /// <summary>A copy (the content settings are shared).</summary>
        public AppSettings Clone()
        {
            var c = (AppSettings)MemberwiseClone();
            c.Bridge = Bridge.Clone();
            c.Kinematics = Kinematics.Clone();
            c.Ui = Ui.Clone();
            c.PluginsDisabled = new List<string>(PluginsDisabled);
            return c;
        }

        // ------------------------------------------------------------ members

        /// <summary>The members of the settings file this class reads ("block.member" inside an object; SettingsKeys.App;
        /// Config/settings.example.json names each with its comment line).</summary>
        public static IReadOnlyList<string> KnownKeys => SettingsKeys.App;

        static readonly HashSet<string> unknownLogged = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The members of the settings file the runtime does not read (SettingsKeys: the content members, the app
        /// members and blocks, the `ui` block's aliases and option sections), logged once per file and process; a member
        /// starting with "_" is a comment.</summary>
        static void LogUnknownKeys(string file, string json)
        {
            List<string> unknown;
            try
            {
                var members = new List<KeyValuePair<string, bool>>();
                foreach (var p in JObject.Parse(json).Properties())
                {
                    members.Add(new KeyValuePair<string, bool>(p.Name, p.Value is JObject));
                    if (p.Value is JObject b && SettingsKeys.Blocks.Contains(p.Name))
                        foreach (var q in b.Properties()) members.Add(new KeyValuePair<string, bool>(p.Name + "." + q.Name, q.Value is JObject));
                }
                unknown = SettingsKeys.Unknown(members);
            }
            catch (Exception) { return; }   // ApplyJson has reported a file that does not parse
            if (unknown.Count == 0 || !unknownLogged.Add(file + "|" + string.Join(",", unknown))) return;
            Debug.Log("[settings] " + file + ": members HS2Next does not read (ignored): " + string.Join(", ", unknown));
        }

        /// <summary>Applies the members userData, app and bridge of a settings object (absent members keep their values).</summary>
        public void ApplyJson(string json, string what)
        {
            JObject o;
            try { o = JObject.Parse(json); }
            catch (Exception e) { throw new FormatException(what + ": not a JSON object (" + e.Message + ")"); }
            string source = what == "launch override" ? "launch" : "file";
            if (o.TryGetValue("userData", out var ud))
            {
                UserData = Str(ud, what + ": userData");
                UserDataSource = string.IsNullOrEmpty(UserData) ? "default" : source;
            }
            if (o.TryGetValue("mapAudioLink", out var mal)) MapAudioLink = Bool(mal, what + ": mapAudioLink");
            var maps = Obj(o, "maps", what);
            if (maps != null && maps.TryGetValue("maxMirrors", out var mm)) MaxMirrors = Int(mm, what + ": maps.maxMirrors", 0, 16);
            if (maps != null && maps.TryGetValue("audioLinkEditor", out var ale)) MapAudioLinkEditor = Bool(ale, what + ": maps.audioLinkEditor");
            var studio = Obj(o, "studio", what);
            if (studio != null && studio.TryGetValue("accessoryItems", out var sai)) StudioAccessoryItems = Bool(sai, what + ": studio.accessoryItems");
            var render = Obj(o, "render", what);
            if (render != null) RenderOptions.Apply(render, what + ": render");   // validates all first (Phase 3.3)
            var kin = Obj(o, "kinematics", what);
            if (kin != null) Kinematics.Apply(kin, what + ": kinematics", strict: false);   // Phase 3.4
            var ui = Obj(o, "ui", what);
            if (ui != null)
            {
                // Phase 3.5: enabled here; the other members are read by the UI's options layer
                if (ui.TryGetValue("enabled", out var ue))
                {
                    Ui.Enabled = Bool(ue, what + ": ui.enabled");
                    if (Ui.Enabled && what == "launch override") Ui.Forced = true;
                }
                if (Ui.Block == null) Ui.Block = new JObject();
                Ui.Block.Merge(ui, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
            }
            if (what == "launch override" && o.TryGetValue("pluginsDir", out var lpd)) PluginsDirArg = Str(lpd, what + ": pluginsDir");
            var plugins = Obj(o, "plugins", what);
            if (plugins != null)
            {
                if (plugins.TryGetValue("enabled", out var pe)) PluginsEnabled = Bool(pe, what + ": plugins.enabled");
                if (plugins.TryGetValue("dir", out var pd)) PluginsDir = Str(pd, what + ": plugins.dir") ?? "";
                if (plugins.TryGetValue("disabled", out var px))
                {
                    if (px.Type == JTokenType.Null) PluginsDisabled = new List<string>();
                    else if (px is JArray pa && pa.All(t => t.Type == JTokenType.String)) PluginsDisabled = pa.Select(t => (string)t).ToList();
                    else throw new FormatException(what + ": plugins.disabled must be an array of plugin GUIDs");
                }
            }
            var app = Obj(o, "app", what);
            if (app != null)
            {
                if (app.TryGetValue("autoStart", out var a)) AutoStart = Bool(a, what + ": app.autoStart");
                if (app.TryGetValue("frameRate", out var f)) FrameRate = Int(f, what + ": app.frameRate", 0, 1000);
                if (app.TryGetValue("runInBackground", out var rib)) RunInBackground = Bool(rib, what + ": app.runInBackground");
            }
            var b = Obj(o, "bridge", what);
            if (b != null)
            {
                if (b.TryGetValue("enabled", out var e)) Bridge.Enabled = Bool(e, what + ": bridge.enabled");
                if (b.TryGetValue("port", out var p)) Bridge.Port = Int(p, what + ": bridge.port", 1024, 65535);
                if (b.TryGetValue("token", out var t))
                {
                    // true / false: required with a random token / off; a string: required with that token ("" = off)
                    if (t.Type == JTokenType.String)
                    {
                        string v = (string)t;
                        Bridge.Token = v.Length > 0;
                        Bridge.TokenValue = v.Length > 0 ? v : null;
                    }
                    else if (t.Type == JTokenType.Boolean)
                    {
                        Bridge.Token = (bool)t;
                        Bridge.TokenValue = null;
                    }
                    else throw new FormatException(what + ": bridge.token must be true, false or a string");
                }
                if (b.TryGetValue("outputDir", out var d))
                {
                    Bridge.OutputDir = Str(d, what + ": bridge.outputDir");
                    OutputDirSource = string.IsNullOrEmpty(Bridge.OutputDir) ? "default" : source;
                }
                if (b.TryGetValue("allowQuit", out var q)) Bridge.AllowQuit = Bool(q, what + ": bridge.allowQuit");
            }
        }

        static JObject Obj(JObject o, string name, string what)
        {
            if (!o.TryGetValue(name, out var v) || v.Type == JTokenType.Null) return null;
            if (v is JObject jo) return jo;
            throw new FormatException(what + ": " + name + " must be an object");
        }

        static bool Bool(JToken t, string what)
        {
            if (t.Type == JTokenType.Boolean) return (bool)t;
            throw new FormatException(what + " must be true or false");
        }

        static int Int(JToken t, string what, int min, int max)
        {
            if (t.Type != JTokenType.Integer) throw new FormatException(what + " must be an integer");
            long v = (long)t;
            if (v < min || v > max) throw new FormatException(what + " must be in " + min + "–" + max);
            return (int)v;
        }

        static string Str(JToken t, string what)
        {
            if (t.Type == JTokenType.Null) return null;
            if (t.Type == JTokenType.String) return (string)t;
            throw new FormatException(what + " must be a string");
        }

        // ------------------------------------------------------------ command line

        /// <summary>Applies the app and bridge arguments of the command line (flag order irrelevant).</summary>
        public void ApplyArgs(string[] args)
        {
            if (args == null) return;
            bool on = false, off = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-bridge": on = true; break;
                    case "-noBridge": off = true; break;
                    case "-bridgeToken":
                        // an optional value (B's rule: the next argument when it does not start with '-'); alone = random
                        Bridge.Token = true;
                        Bridge.TokenValue = null;
                        if (i + 1 < args.Length && args[i + 1].Length > 0 && args[i + 1][0] != '-') Bridge.TokenValue = args[++i];
                        break;
                    case "-bridgeAllowQuit": Bridge.AllowQuit = true; break;
                    case "-noApp": NoApp = true; break;
                    case "-noPlugins": NoPlugins = true; break;
                    case "-pluginsDir": PluginsDirArg = Value(args, i); i++; break;
                    case "-ui": Ui.Forced = true; break;
                    case "-noUi": Ui.Off = true; break;
                    case "-smoke": case "-bridgeAccept": headlessDefault = true; break;
                    case "-bridgePort":
                        if (!int.TryParse(Value(args, i), NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) || port < 1024 || port > 65535)
                            throw new FormatException("-bridgePort needs a port number in 1024–65535");
                        Bridge.Port = port;
                        i++;
                        break;
                    case "-bridgeOutputDir": Bridge.OutputDir = Value(args, i); OutputDirSource = "arg"; i++; break;
                    case "-userData": UserData = Value(args, i); UserDataSource = "arg"; i++; break;
                }
            }
            if (on) Bridge.Enabled = true;
            if (off) Bridge.Enabled = false;
        }

        static string Value(string[] args, int i)
        {
            if (i + 1 >= args.Length || args[i + 1].StartsWith("-", StringComparison.Ordinal)) throw new FormatException(args[i] + " needs a value");
            return args[i + 1];
        }

        static string Arg(string[] a, string name)
        {
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        /// <summary>Resolves the folders and the batch-mode frame rate.</summary>
        void Finish()
        {
            if (string.IsNullOrEmpty(UserData)) UserDataSource = "default";
            if (string.IsNullOrEmpty(Bridge.OutputDir)) OutputDirSource = "default";
            UserData = (Roots ?? ContentSettings.Roots).UserDataOf(UserData);
            Bridge.OutputDir = string.IsNullOrEmpty(Bridge.OutputDir) ? Path.Combine(UserData, "Bridge") : (Roots ?? ContentSettings.Roots).Resolve(Bridge.OutputDir);
            // the batch loop runs uncapped (0 = Unity's, which is uncapped in batch mode).
            if (FrameRate == 0 && Application.isBatchMode) FrameRate = 60;
            HS2Next.Scenes.Runtime.MapOptions.AudioLink = MapAudioLink;
            HS2Next.Scenes.Runtime.MapOptions.MaxMirrors = MaxMirrors;
            HS2Next.Scenes.Runtime.MapOptions.AudioLinkEditor = MapAudioLinkEditor;
            Kinematics.ApplyToRuntime();
            // Phase 3.5: tests and headless runs keep working without the UI unless they ask for it
            bool headless = headlessDefault || Application.isBatchMode;
            Ui.Run = !Ui.Off && (Ui.Forced || (Ui.Enabled && !headless));
        }

        /// <summary>
        /// The settings stage's fallback: UserData becomes <paramref name="dir"/> (absolute), and the bridge output root follows when
        /// it was the default &lt;UserData&gt;/Bridge.
        /// </summary>
        public void MoveUserData(string dir)
        {
            bool derived = OutputDirSource == "default";
            UserData = Path.GetFullPath(dir);
            UserDataSource = "fallback";
            if (derived) Bridge.OutputDir = Path.Combine(UserData, "Bridge");
        }

        /// <summary>The settings stage's fallback: the cache becomes <paramref name="dir"/> (absolute).</summary>
        public void MoveCache(string dir)
        {
            if (Content == null) return;
            Content.cacheDir = Path.GetFullPath(dir);
            CacheDirSource = "fallback";
        }

        /// <summary>-smoke / -bridgeAccept seen on the command line (the UI is off by default there).</summary>
        bool headlessDefault;
    }
}
