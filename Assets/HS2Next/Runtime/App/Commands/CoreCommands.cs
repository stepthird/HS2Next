// Registration of the built-in Core commands: the 30 of Phase 2.4 and 15 cheap 2.4+
// ones, owner "core", Since 0.2.0, and the Phase 3.1 item commands (also Since
// 0.2.0, the API is unreleased). Each command file registers its group; package W1's files (scene, map and item,
// camera and capture, app) are called directly, package W2's (characters, FK and poses, motion, dumps) through the
// partial methods below, which W2 implements in its files; Phase 3.1's ItemCommands (package A) and
// ItemMotionCommands (package B) directly, and Phase 3.2's MapCommands (package A). Names is the 2.4 contract, Phase31Names the 3.1 additions: the lists the
// tests compare the registry with. RuntimeCreated runs once the app's runtime exists (boot stage 7, AppCore.ForTests), for the
// runtime-side parts of the commands (W2: MotionOverrides.Attach; Phase 6.2: the Timeline's timed commands reach the app's
// bridge). Phase 6.2 registers the groups "timeline" (TimelineCommands, OverrideCommands) and "record" (RecordCommands), Since 1.2.0.
// Phase 6.3 registers the group "advanced pose" (AdvancedPoseCommands), Since 1.2.0. Phase 6.4 registers the group "materials"
// (MaterialCommands), Since 1.2.0.
using System.Collections.Generic;
using HS2Next.Api.Bridge;

namespace HS2Next.App.Commands
{
    public static partial class CoreCommands
    {
        /// <summary>Every Core command, in the order.</summary>
        public static readonly IReadOnlyList<string> Names = new[]
        {
            //
            "scene", "scene.doc", "scene.new", "scene.save", "scene.load", "delete", "obj.transform", "maps", "map.load",
            "chara.load", "anime.groups", "anime.search", "chara.anime", "chara.speed", "chara.face", "chara.hands",
            "look.target", "chara.motion", "bones.dump", "face.dump", "fk.list", "fk.set", "fk.enable", "fk.capture",
            "pose.load", "camera.get", "camera.set", "camera.frame", "camera.state", "capture",
            //
            "scene.list", "undo", "redo", "history", "item.search", "item.add", "item.parent", "chara.save",
            "chara.animeOption", "chara.bones", "ik.enable", "pose.list", "scene.lights", "app.options", "app.quit",
        };

        /// <summary>
        /// Commands added in Phase 3.1, owner core, Since 0.2.0 (ApiVersion 0.2.0 is still unreleased):
        /// package A's ItemCommands (item.state, item.set, item.materials, obj.visible) and package B's ItemMotionCommands
        /// (item.anime, item.motion). <see cref="Names"/> stays the 2.4 contract.
        /// </summary>
        public static readonly IReadOnlyList<string> Phase31Names = new[] { "item.state", "item.set", "item.materials", "obj.visible", "item.anime", "item.motion" };

        /// <summary>
        /// Commands added in Phase 3.2, owner core, Since 0.2.0: package A's MapCommands
        /// (map.state, map.set, map.renderers).
        /// </summary>
        public static readonly IReadOnlyList<string> Phase32Names = new[] { "map.state", "map.set", "map.renderers" };

        /// <summary>
        /// Commands added in Phase 3.3 by package A, owner core, Since 0.2.0:
        /// LightCameraCommands (light.*, charaLight.set, camera.slots / slot / add / activate, sound.*).
        /// </summary>
        public static readonly IReadOnlyList<string> Phase33LightNames = LightCameraCommands.Names;

        /// <summary>
        /// Commands added in Phase 3.3 by package B, owner core, Since 0.2.0:
        /// EffectCommands (effects.state, effects.set, effects.reset).
        /// </summary>
        public static readonly IReadOnlyList<string> Phase33EffectNames = EffectCommands.Names;

        /// <summary>
        /// Commands added in Phase 3.4 by package C, owner core, Since 0.2.0, in FkPoseCommands:
        /// ik.list, ik.set, ik.copy, fk.copy (also on HS2's bridge), ik.bake, pose.save (HS2Next only). The extended 2.4 commands
        /// (ik.enable groups, fk.set / fk.list hipsPosition, pose.load parts / ik, pose.list source, app.options kinematics) stay in Names.
        /// </summary>
        public static readonly IReadOnlyList<string> Phase34Names = new[] { "ik.list", "ik.set", "ik.copy", "fk.copy", "ik.bake", "pose.save" };

        /// <summary>Commands added in Phase 3.5 by package B, owner core, Since 0.2.0: UiSceneCommands.</summary>
        public static readonly IReadOnlyList<string> Phase35Names = UiSceneCommands.Names;

        /// <summary>Commands added in Phase 3.6 (group "routes / time / capture"), owner core,
        /// Since 0.2.0: package C's CaptureCommands (screenshot, scene.images); packages K and R append their lists here.</summary>
        public static readonly IReadOnlyList<string> Phase36Names = Lists(CaptureCommands.Names, TimeCommands.Names /* package K */, RouteCommands.Names /* package R */);

        /// <summary>Commands added in Phase 4.0 by package E (group "chara edit"), owner core, Since
        /// 0.2.0: CharaEditCommands (chara.edit.*, HS2's shape.info / shape.set).</summary>
        public static readonly IReadOnlyList<string> Phase40Names = CharaEditCommands.Names;

        /// <summary>Commands added in Phase 4.1 by package E (group "chara edit"), owner core, Since
        /// 0.2.0: CharaEditCommands.Phase41Names (chara.edit.layout, .reset, .materials, .rtstats, .digest).</summary>
        public static readonly IReadOnlyList<string> Phase41Names = CharaEditCommands.Phase41Names;

        /// <summary>Commands added in Phase 4.2 by package E (group "chara edit"), owner core, Since
        /// 0.2.0: CharaEditCommands.Phase42Names (chara.edit.swatch, .bust).</summary>
        public static readonly IReadOnlyList<string> Phase42Names = CharaEditCommands.Phase42Names;

        /// <summary>Commands added in Phase 4.3 by package E (group "chara edit"), owner core, Since
        /// 0.2.0: CharaEditCommands.Phase43Names (chara.edit.hairBundle, .hairSkin, .hair).</summary>
        public static readonly IReadOnlyList<string> Phase43Names = CharaEditCommands.Phase43Names;

        /// <summary>Commands added in Phase 4.4 by package E (the clothes pages), owner core, Since 0.2.0:
        /// CharaEditCommands.Phase44Names (chara.edit.clothes, .clothesDefaults, .coordinate; group "chara edit") and
        /// ContentCommands.Names (content.redirect, content.zipmods; group "content").</summary>
        public static readonly IReadOnlyList<string> Phase44Names = Lists(CharaEditCommands.Phase44Names, ContentCommands.Names);

        /// <summary>Commands added in Phase 4.5 by package E (group "chara edit"), owner core,
        /// Since 0.2.0: CharaEditCommands.Phase45Names (chara.edit.accessories, .accessoryDefaults, .accessoryHairColour).</summary>
        public static readonly IReadOnlyList<string> Phase45Names = CharaEditCommands.Phase45Names;

        /// <summary>Commands added in Phase 4.6 by package E (the File node), owner core, Since 0.2.0: CharaEditCommands.Phase46Names
        /// (chara.edit.load; group "chara edit") and CharaCommands.Phase46Names (coord.save; group "chara"). Extended in 4.6 without a
        /// new name: chara.save (thumb "view", clock), chara.edit.get (the pseudo path "import"), app.options (maker).</summary>
        public static readonly IReadOnlyList<string> Phase46Names = Lists(CharaEditCommands.Phase46Names, CharaCommands.Phase46Names);

        /// <summary>Commands added in Phase 5.M (group "chara"), owner core, Since 0.2.0: CharaCommands.Phase5MNames (package N:
        /// chara.animeState, chara.yure; package R appends chara.mosaic there).</summary>
        public static readonly IReadOnlyList<string> Phase5MNames = CharaCommands.Phase5MNames;

        /// <summary>Commands added in Phase 5.1 (group "plugin"), owner core, Since 1.0.0: PluginCommands.Names (package L:
        /// plugin.list, the plugin loader's report).</summary>
        public static readonly IReadOnlyList<string> Phase51Names = HS2Next.App.Plugins.PluginCommands.Names;

        /// <summary>Commands added in Phase 5.2, owner core, Since 1.0.0: ContentCommands.Phase52Names (content.stats, group
        /// "content"), PerfCommands.Names (app.perf, group "app") and QuarantineCommands.Names (content.quarantine, group
        /// "content").</summary>
        public static readonly IReadOnlyList<string> Phase52Names = Lists(ContentCommands.Phase52Names, PerfCommands.Names, QuarantineCommands.Names);

        /// <summary>Commands added in Phase 5.3 (group "app"), owner core, Since 1.0.0: SetupCommands.Names (app.paths, app.setup,
        /// app.settings: the folders, the first-run guide and the settings file; gate None).</summary>
        public static readonly IReadOnlyList<string> Phase53Names = SetupCommands.Names;

        /// <summary>Commands added in Phase 6.1 (group "plugin"), owner core, Since 1.1.0: PluginCommands.Phase61Names
        /// (package H: plugin.disable, plugin.enable).</summary>
        public static readonly IReadOnlyList<string> Phase61Names = HS2Next.App.Plugins.PluginCommands.Phase61Names;

        /// <summary>Commands added in Phase 6.2, owner core, Since 1.2.0: TimelineCommands.Names (package P: timeline.*) and
        /// OverrideCommands.Names (package O: overrides.list, experimental) in the group "timeline", RecordCommands.Names (package W:
        /// record.start, record.stop, record.status) in the group "record". 20 commands.</summary>
        public static readonly IReadOnlyList<string> Phase62Names = Lists(TimelineCommands.Names, OverrideCommands.Names, RecordCommands.Names);

        /// <summary>Commands added in Phase 6.3 (group "advanced pose"), owner core, Since 1.2.0, experimental: AdvancedPoseCommands.Names
        /// (pose.bones, pose.chains, pose.bust, pose.blendShapes, each with .set and .clear). 12 commands.</summary>
        public static readonly IReadOnlyList<string> Phase63Names = AdvancedPoseCommands.Names;

        /// <summary>Commands added in Phase 6.4 (group "materials"), owner core, Since 1.2.0, experimental: MaterialCommands.Names
        /// (materials.shaders, chara.edit.materials.*, item.materials.*, map.materials*, chara.edit.boneMods*). 14 commands.</summary>
        public static readonly IReadOnlyList<string> Phase64Names = MaterialCommands.Names;

        /// <summary>The commands of Phase 6.5 (constraints), Since 1.2.0, all experimental.</summary>
        public static readonly IReadOnlyList<string> Phase65Names = ConstraintCommands.Names;

        /// <summary>
        /// The Core commands that keep HS2 Studio's automation vocabulary (89 names HS2's own bridge also answers): never renamed
        /// or aliased away, with HS2's argument names, defaults, reply members and number forms. BridgeCoreTest checks that each
        /// is registered under its own name; the help dump lists them and Reference/COMMANDS.md marks them. A new HS2Next-only
        /// command follows the naming convention of Reference/COMMANDS.md instead.
        /// </summary>
        public static readonly IReadOnlyList<string> ParityNames = new[]
        {
            "anime.groups", "anime.search", "bones.dump", "camera.activate", "camera.add", "camera.frame", "camera.get",
            "camera.set", "camera.slot", "camera.slots", "camera.state", "capture", "chara.accessories", "chara.anime",
            "chara.animeOption", "chara.animeState", "chara.bones", "chara.change", "chara.clothes", "chara.clothesDump",
            "chara.face", "chara.hands", "chara.load", "chara.speed", "chara.yure", "charaLight.set", "coord.save",
            "delete", "effects.reset", "effects.set", "effects.state", "face.dump", "fk.capture", "fk.copy", "fk.enable",
            "fk.list", "fk.set", "ik.copy", "ik.enable", "ik.list", "ik.set", "item.add", "item.materials", "item.parent",
            "item.search", "item.set", "item.state", "light.add", "light.set", "light.state", "look.target", "map.load",
            "map.renderers", "map.set", "map.state", "maps", "obj.transform", "pose.load", "record.start", "record.stop", "route.add",
            "route.addPoint", "route.deletePoint", "route.list", "route.play", "route.set", "route.setPoint", "route.state", "route.stop",
            "scene", "scene.lights", "scene.load", "scene.new", "scene.save", "select", "shape.info", "shape.set",
            "sound.list", "sound.set", "sound.state", "timeline.build", "timeline.clear", "timeline.events", "timeline.pause",
            "timeline.play", "timeline.seek", "timeline.status", "timeline.stop", "ui.state",
        };

        static string[] Lists(params IReadOnlyList<string>[] lists)
        {
            var l = new List<string>();
            foreach (var x in lists) l.AddRange(x);
            return l.ToArray();
        }

        /// <summary>Every Core command: <see cref="Names"/>, <see cref="Phase31Names"/>, <see cref="Phase32Names"/>,
        /// <see cref="Phase33LightNames"/>, <see cref="Phase33EffectNames"/>, <see cref="Phase34Names"/>, <see cref="Phase35Names"/>.</summary>
        public static IEnumerable<string> AllNames
        {
            get
            {
                foreach (var n in Names) yield return n;
                foreach (var n in Phase31Names) yield return n;
                foreach (var n in Phase32Names) yield return n;
                foreach (var n in Phase33LightNames) yield return n;
                foreach (var n in Phase33EffectNames) yield return n;
                foreach (var n in Phase34Names) yield return n;
                foreach (var n in Phase35Names) yield return n;
                foreach (var n in Phase36Names) yield return n;   // Phase 3.6 (routes / time / capture)
                foreach (var n in Phase40Names) yield return n;   // Phase 4.0 package E (chara edit)
                foreach (var n in Phase41Names) yield return n;   // Phase 4.1 package E (chara edit: layout, reset, materials, rtstats, digest)
                foreach (var n in Phase42Names) yield return n;   // Phase 4.2 package E (chara edit: swatch, bust)
                foreach (var n in Phase43Names) yield return n;   // Phase 4.3 package E (chara edit: hairBundle, hairSkin, hair)
                foreach (var n in Phase44Names) yield return n;   // Phase 4.4 package E (chara edit: clothes, clothesDefaults, coordinate; content)
                foreach (var n in Phase45Names) yield return n;   // Phase 4.5 package E (chara edit: accessories, accessoryDefaults, accessoryHairColour)
                foreach (var n in Phase46Names) yield return n;   // Phase 4.6 package E (chara edit: load; chara: coord.save)
                foreach (var n in Phase5MNames) yield return n;   // Phase 5.M (chara: animeState, yure, mosaic)
                foreach (var n in Phase51Names) yield return n;   // Phase 5.1 (plugin: plugin.list)
                foreach (var n in Phase52Names) yield return n;   // Phase 5.2 (content: content.stats, content.quarantine; app: app.perf)
                foreach (var n in Phase53Names) yield return n;   // Phase 5.3 (app: app.paths, app.setup, app.settings)
                foreach (var n in Phase61Names) yield return n;   // Phase 6.1 (plugin: plugin.disable, plugin.enable; Since 1.1.0)
                foreach (var n in Phase62Names) yield return n;   // Phase 6.2 (timeline: timeline.*, overrides.list; record: record.*; Since 1.2.0)
                foreach (var n in Phase63Names) yield return n;   // Phase 6.3 (advanced pose: pose.bones*, pose.chains*, pose.bust*, pose.blendShapes*; Since 1.2.0)
                foreach (var n in Phase64Names) yield return n;   // Phase 6.4 (materials: the arrays of characters, items and the map, hair bone modifiers; Since 1.2.0)
                foreach (var n in Phase65Names) yield return n;   // Phase 6.5 (constraint: constraint.*; Since 1.2.0)
                foreach (var n in HS2Next.UI.Bridge.UiCommands.Names) yield return n;   // Phase 3.5 package A: ui.* (unavailable without the UI)
            }
        }

        /// <summary>The commands of package W1's files (the rest of <see cref="Names"/> is package W2's).</summary>
        public static readonly IReadOnlyList<string> W1Names = new[]
        {
            "scene", "scene.doc", "scene.new", "scene.save", "scene.load", "scene.list", "delete", "obj.transform",
            "undo", "redo", "history", "maps", "map.load", "item.search", "item.add", "item.parent",
            "camera.get", "camera.set", "camera.frame", "camera.state", "capture", "scene.lights", "app.options", "app.quit",
        };

        /// <summary>
        /// Registers every Core command into <paramref name="r"/>; the handlers work on <paramref name="app"/>. A group
        /// whose registration throws is logged and listed in <see cref="RegistrationErrors"/> (the other groups still register,
        /// so one faulty command file does not take the bridge down; BridgeCoreTest C2 fails on it).
        /// </summary>
        public static void Register(ICommandRegistry r, AppCore app)
        {
            RegistrationErrors.Clear();
            Group("scene", () => SceneCommands.Register(r, app));
            Group("map/item", () => MapItemCommands.Register(r, app));
            Group("camera", () => CameraCommands.Register(r, app));
            Group("app", () => { AppCommands.Register(r, app); PerfCommands.Register(r, app); SetupCommands.Register(r, app); });   // app.perf: Phase 5.2; app.paths / app.setup / app.settings: Phase 5.3
            Group("chara", () => RegisterChara(r, app));
            Group("fk/pose", () => RegisterFkPose(r, app));
            Group("motion", () => RegisterMotion(r, app));
            Group("dumps", () => RegisterDumps(r, app));
            Group("items", () => ItemCommands.Register(r, app));
            Group("item motion", () => ItemMotionCommands.Register(r, app));
            Group("maps", () => MapCommands.Register(r, app));
            // Phase 3.3: package A's LightCameraCommands, package B's EffectCommands (one line each)
            Group("lights/cameras/sound", () => LightCameraCommands.Register(r, app));
            Group("effects", () => EffectCommands.Register(r, app));
            Group("sky/ambient", SkyAmbientState.Hook);   // package S: effects.state realized.ambient / .sky
            Group("ui scene", () => UiSceneCommands.Register(r, app));   // Phase 3.5 package B (select, chara.clothes, …)
            Group("ui", () => HS2Next.UI.Bridge.UiCommands.Register(r, app));   // Phase 3.5 package A (ui.state, ui.action, …)
            Group("capture", () => CaptureCommands.Register(r, app));   // Phase 3.6 package C (screenshot, scene.images)
            Group("time", () => TimeCommands.Register(r, app));   // Phase 3.6 package K (scene.pause, scene.step)
            Group("routes", () => RouteCommands.Register(r, app));   // Phase 3.6 package R (route.*)
            Group("chara edit", () => CharaEditCommands.Register(r, app));   // Phase 4.0 package E (chara.edit.*, shape.info / shape.set)
            Group("content", () => { ContentCommands.Register(r, app); QuarantineCommands.Register(r, app); });   // Phase 4.4 package E (content.redirect, content.zipmods); 5.2 content.stats, content.quarantine
            Group("plugin", () => HS2Next.App.Plugins.PluginCommands.Register(r, app));   // Phase 5.1 package L (plugin.list); 6.1 package H (plugin.disable / enable)
            Group("timeline", () => { TimelineCommands.Register(r, app); OverrideCommands.Register(r, app); });   // Phase 6.2 packages P (timeline.*) and O (overrides.list)
            Group("record", () => RecordCommands.Register(r, app));   // Phase 6.2 package W (record.start, record.stop, record.status)
            Group(AdvancedPoseCommands.GroupName, () => AdvancedPoseCommands.Register(r, app));   // Phase 6.3 package C (pose.bones*, pose.chains*, pose.bust*, pose.blendShapes*)
            Group(MaterialCommands.GroupName, () => MaterialCommands.Register(r, app));   // Phase 6.4 package C (materials.shaders, chara.edit.materials.*, item.materials.*, map.materials*, chara.edit.boneMods*)
            Group(ConstraintCommands.GroupName, () => ConstraintCommands.Register(r, app));   // Phase 6.5 package C (constraint.*)
        }

        /// <summary>"&lt;group&gt;: &lt;exception&gt;" of the groups whose last registration threw, and "&lt;group&gt;: &lt;command&gt;
        /// declares the group …" of specs whose help group is not the group registering them.</summary>
        public static readonly List<string> RegistrationErrors = new List<string>();

        /// <summary>The help groups of the Core commands (CommandSpec.Group of every spec = the group of <see cref="Register"/>
        /// that registers it), in registration order.</summary>
        public static readonly IReadOnlyList<string> Groups = new[]
        {
            "scene", "map/item", "camera", "app", "chara", "fk/pose", "motion", "dumps", "items", "item motion", "maps",
            "lights/cameras/sound", "effects", "ui scene", "ui", "capture", "time", "routes", "chara edit", "content", "plugin",
            "timeline", "record", AdvancedPoseCommands.GroupName, MaterialCommands.GroupName, ConstraintCommands.GroupName,
        };

        static void Group(string name, System.Action register)
        {
            CommandKit.RegisteringGroup = name;
            try { register(); }
            catch (System.Exception e)
            {
                RegistrationErrors.Add(name + ": " + e.GetType().Name + ": " + e.Message);
                UnityEngine.Debug.LogError("[app] registering the " + name + " commands failed: " + e);
            }
            finally { CommandKit.RegisteringGroup = null; }
        }

        /// <summary>Called by AppCore once its runtime exists (and again for a new runtime).</summary>
        public static void RuntimeCreated(AppCore app)
        {
            AttachRuntime(app);
            TimelineCommands.RuntimeCreated(app);   // Phase 6.2: the Timeline's timed commands go to this app's bridge
        }

        // Package W2: one implementation each, in the named file.
        /// <summary>CharaCommands.cs: chara.load, chara.save, anime.groups, anime.search, chara.anime, chara.speed, chara.animeOption, chara.face, chara.hands, look.target.</summary>
        static partial void RegisterChara(ICommandRegistry r, AppCore app);
        /// <summary>FkPoseCommands.cs: fk.list, fk.set, fk.enable, fk.capture, ik.enable, pose.load, pose.list and Phase 3.4's Phase34Names.</summary>
        static partial void RegisterFkPose(ICommandRegistry r, AppCore app);
        /// <summary>MotionCommands.cs: chara.motion.</summary>
        static partial void RegisterMotion(ICommandRegistry r, AppCore app);
        /// <summary>DumpCommands.cs: bones.dump, face.dump, chara.bones.</summary>
        static partial void RegisterDumps(ICommandRegistry r, AppCore app);
        /// <summary>MotionCommands.cs (or MotionOverrides.cs): the runtime-side parts, e.g. MotionOverrides.Attach.</summary>
        static partial void AttachRuntime(AppCore app);
    }
}
