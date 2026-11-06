// The Core commands of the plugin group (group "plugin", owner core):
//   plugin.list     (read-only, no readiness gate so it answers while the app starts): the run's plugin folder and switch,
//                   and every candidate plugin with its state, refusal reason, assemblies and load times, entry type,
//                   commands, ext namespaces, event handlers, undo entries, engine registrations, faults per hook, the last
//                   error, the last failed enable, the Update time (median / max over the last 600 frames) and its
//                   manifest's unknown members. With diagnostics the reply adds the managed bytes after a full collection and
//                   the counts of engine objects (the leak rows).
//   plugin.disable  (Phase 6.1) disables a loaded or faulted plugin while the app runs (PluginHost.Disable), optionally with
//                   the plugins that depend on it and optionally recorded in the settings file's plugins.disabled. Its
//                   runtime override slots are released (Phase 6.2: the reply's overrides counts them; plugin.list lists a
//                   plugin's slots as overrides).
//   plugin.enable   (Phase 6.1) enables a disabled, faulted or failed plugin while the app runs (PluginHost.Enable),
//                   optionally removing it from plugins.disabled. A DLL that changed on disk loads after a restart.
// The settings file is written atomically through the settings file code; nothing else is written.
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Api.Bridge;
using HS2Next.App.Commands;
using HS2Next.App.Setup;
using Newtonsoft.Json.Linq;

namespace HS2Next.App.Plugins
{
    public static class PluginCommands
    {
        /// <summary>The commands of this file of Phase 5.1 (registered by CoreCommands in the group "plugin").</summary>
        public static readonly string[] Names = { "plugin.list" };

        /// <summary>The commands of this file added in Phase 6.1 (Since 1.1.0).</summary>
        public static readonly string[] Phase61Names = { "plugin.disable", "plugin.enable" };

        /// <summary>CommandSpec.Since of the Phase 6.1 commands (the Api version that adds them).</summary>
        public const string Since61 = "1.1.0";

        public static void Register(ICommandRegistry r, AppCore app)
        {
            CommandKit.Register(r, app, CommandKit.Spec("plugin.list", "The plugins of this run: the plugin folder and switch, and every candidate with its state, reason, " +
                    "assemblies, commands, ext namespaces, event handlers, engine registrations, faults and Update times.", "plugin")
                .WithGate(ReadyGate.None).WithFlags(CommandFlags.ReadOnly)
                .Arg("diagnostics", ArgTypes.Bool, "add the managed bytes after a full collection and the engine object counts (leak checks)", defaultValue: false)
                .Return("root", ArgTypes.Path, "the plugin folder of this run (absolute), null before the plugins stage")
                .Return("enabled", ArgTypes.Bool, "plugins load in this run")
                .Return("source", ArgTypes.String, "what decided the folder and the switch: settings, -pluginsDir, -noPlugins or env")
                .Return("plugins", ArgTypes.ArrayOf(ArgTypes.Object), "every candidate: guid, name, version, apiVersion, dir, state, reason, assemblies, entryType, " +
                    "commands, extNamespaces, events, history, overrides, hooks, supplies, ui, faults, lastError, enableError, updateMs, loadMs, unknown")
                .Return("diagnostics", ArgTypes.Object, "with diagnostics: managedBytes, gameObjects, materials, meshes, textures, renderTextures", always: false),
                c =>
                {
                    bool diag = c.Args.Get("diagnostics", false);
                    var host = c.App.Plugins;
                    if (host == null)
                    {
                        var o = new JObject { ["root"] = null, ["enabled"] = false, ["source"] = null, ["plugins"] = new JArray(), ["stage"] = "not run yet" };
                        if (diag) o["diagnostics"] = PluginHost.Diagnostics();
                        return o;
                    }
                    return host.ListJson(diag);
                });

            CommandKit.Register(r, app, CommandKit.Spec("plugin.disable", "Disables a loaded or faulted plugin now: its Unload, then everything it registered is removed " +
                    "(commands, ext handlers, events, frame actions, undo entries, hooks, supplies, UI); characters its build hooks touched are rebuilt. Its assembly, " +
                    "statics and objects stay in the process.", "plugin")
                .WithSince(Since61).WithFlags(CommandFlags.WritesFiles)
                .Arg("guid", ArgTypes.String, "the plugin's GUID", required: true)
                .Arg("persist", ArgTypes.Bool, "also add the GUID to plugins.disabled in the settings file (it then stays disabled at the next start)", defaultValue: false)
                .Arg("withDependents", ArgTypes.Bool, "also disable the loaded plugins that depend on it (first, in reverse load order); without it they refuse the disable", defaultValue: false)
                .Return("guid", ArgTypes.String, "the plugin's GUID as its manifest writes it")
                .Return("state", ArgTypes.String, "its state now (disabled)")
                .Return("disabled", ArgTypes.ArrayOf(ArgTypes.String), "the GUIDs disabled, in that order (dependents first)")
                .Return("persisted", ArgTypes.Bool, "the settings file's plugins.disabled holds the GUID")
                .Return("rebuilt", ArgTypes.Int, "characters queued for a rebuild (their last build ran one of its build hooks)")
                .Return("overrides", ArgTypes.Int, "runtime override slots released (the scene shows the model values again)")
                .Return("ms", ArgTypes.Float, "the time of the disable")
                .Example("{\"cmd\":\"plugin.disable\",\"guid\":\"stp3.sample.sequencer\"}"),
                c => Toggle(c, false));

            CommandKit.Register(r, app, CommandKit.Spec("plugin.enable", "Enables a disabled, faulted or failed plugin now: the load steps on the assemblies already loaded, a " +
                    "new instance and Load, the open scene's ext blocks of its namespaces read again, then SceneLoaded (Initial) and AppReady. A DLL that changed on disk " +
                    "loads after a restart (plugin.staleAssembly).", "plugin")
                .WithSince(Since61).WithFlags(CommandFlags.WritesFiles)
                .Arg("guid", ArgTypes.String, "the plugin's GUID", required: true)
                .Arg("persist", ArgTypes.Bool, "also remove the GUID from plugins.disabled in the settings file", defaultValue: false)
                .Return("guid", ArgTypes.String, "the plugin's GUID as its manifest writes it")
                .Return("state", ArgTypes.String, "its state now (loaded)")
                .Return("persisted", ArgTypes.Bool, "the settings file's plugins.disabled no longer holds the GUID")
                .Return("ms", ArgTypes.Float, "the time of the enable")
                .Return("extBlocks", ArgTypes.Int, "the open scene's ext blocks its handlers read again")
                .Return("rebuilt", ArgTypes.Int, "characters queued for a rebuild (it registered build hooks)")
                .Example("{\"cmd\":\"plugin.enable\",\"guid\":\"stp3.sample.sequencer\"}"),
                c => Toggle(c, true));
        }

        static object Toggle(CoreContext c, bool enable)
        {
            string guid = c.Args.Get<string>("guid");
            if (string.IsNullOrWhiteSpace(guid)) throw CommandKit.BadArgs("guid", ArgTypes.String, "a plugin GUID");
            bool persist = c.Args.Get("persist", false);
            var host = c.App.Plugins;
            if (host == null) throw CommandKit.NotFound("no plugin " + guid + ": the plugins stage has not run", "guid");
            var res = enable ? host.Enable(guid) : host.Disable(guid, c.Args.Get("withDependents", false));
            // persist records the choice even when an enable fails (the plugin then loads at the next start)
            bool persisted = false;
            if (persist && (res.Ok || (enable && res.Error.Code == BridgeCodes.Unavailable)))
                persisted = Persist(c, res.Guid ?? guid, disabled: !enable);
            if (!res.Ok)
            {
                if (persisted) c.Warn("plugins.disabled updated in the settings file: " + (enable ? "it loads at the next start" : "it stays disabled at the next start"));
                throw new BridgeException(res.Error);
            }
            foreach (var w in res.Warnings) c.Warn(w);
            var reply = new BridgeReply().With("guid", res.Guid).With("state", res.State);
            if (!enable) reply.With("disabled", res.Changed);
            reply.With("persisted", persisted).With("ms", Math.Round(res.Ms, 3));
            if (enable) reply.With("extBlocks", res.ExtBlocks);
            reply.With("rebuilt", res.Rebuilt);
            if (!enable) reply.With("overrides", res.Overrides);
            return reply;
        }

        /// <summary>
        /// Adds (<paramref name="disabled"/>) or removes the GUID in the settings file's plugins.disabled (an atomic write, the
        /// other members kept); true when the file then holds the choice. A file that cannot be read or written answers
        /// io_denied.
        /// </summary>
        public static bool Persist(CoreContext c, string guid, bool disabled) => Persist(c.App, guid, disabled);

        public static bool Persist(AppCore app, string guid, bool disabled)
        {
            string file = app?.Paths?.SettingsFile ?? app?.Settings?.SettingsPath;
            if (string.IsNullOrEmpty(file)) throw new BridgeException(BridgeCodes.IoDenied, "this run has no settings file");
            try
            {
                var o = SettingsFile.Read(file);
                var list = (SettingsFile.Get(o, "plugins.disabled") as JArray)?.Select(t => (string)t).Where(x => !string.IsNullOrEmpty(x)).ToList() ?? new List<string>();
                bool has = list.Any(x => string.Equals(x, guid, StringComparison.OrdinalIgnoreCase));
                if (disabled && !has) list.Add(guid);
                if (!disabled && has) list.RemoveAll(x => string.Equals(x, guid, StringComparison.OrdinalIgnoreCase));
                if (disabled != has) SettingsFile.Write(file, new[] { new KeyValuePair<string, JToken>("plugins.disabled", new JArray(list)) });
                return true;
            }
            catch (Exception e) when (!(e is BridgeException))
            {
                throw new BridgeException(BridgeCodes.IoDenied, "the settings file " + file + " could not be updated: " + e.Message);
            }
        }
    }
}
