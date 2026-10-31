// The Core command plugin.list (group "plugin", owner core, read-only, no readiness gate so it answers while the app
// starts): the run's plugin folder and switch, and every candidate plugin with its state, refusal reason, assemblies and
// load times, entry type, commands, ext namespaces, event handlers, engine registrations, faults per hook, the last error,
// the Update time (median / max over the last 600 frames) and its manifest's unknown members. With diagnostics the reply
// adds the managed bytes after a full collection and the counts of engine objects (the leak rows).
using HS2Next.Api.Bridge;
using HS2Next.App.Commands;
using Newtonsoft.Json.Linq;

namespace HS2Next.App.Plugins
{
    public static class PluginCommands
    {
        /// <summary>The commands of this file (registered by CoreCommands in the group "plugin").</summary>
        public static readonly string[] Names = { "plugin.list" };

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
                    "commands, extNamespaces, events, hooks, supplies, ui, faults, lastError, updateMs, loadMs, unknown")
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
        }
    }
}
