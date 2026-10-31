// The engine layer of the plugin host: for every plugin the loader is about to load it makes the plugin's engine-side
// services (PluginEngineServices: IUnityPluginServices through the context's GetService, IShaderSupply as ctx.Shaders),
// their unload and their part of the plugin.list report. Installed into PluginHost.EngineFactory when a play session or a
// player starts and when the Editor loads its scripts (edit-mode test hosts), and by Install() for callers that need it
// earlier; setting the factory registers nothing by itself (no plugin, no hook, no supply).
using System;
using HS2Next.App.Plugins;
using HS2Next.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HS2Next.App.Plugins.Engine
{
    public static class PluginEngine
    {
        /// <summary>Sets <see cref="PluginHost.EngineFactory"/> (idempotent).</summary>
        public static void Install() => PluginHost.EngineFactory = Create;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void InstallAtStart() => Install();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void InstallInEditor() => Install();
#endif

        /// <summary>The engine parts of one plugin: its services, their unload and their report members.</summary>
        public static PluginEngineParts Create(PluginHost host, PluginEntry entry)
        {
            if (entry == null) return null;
            var app = host?.App;
            var s = new PluginEngineServices(entry.Guid, entry.Guard, Debug.Log, Debug.LogWarning,
                () => app?.Runtime, () => UiHost.Current != null && UiHost.Current.Context?.App == app ? UiHost.Current.Context : null);
            return new PluginEngineParts
            {
                Services = s,
                Shaders = s,
                Unload = s.Unload,
                Report = () => Report(s),
            };
        }

        /// <summary>plugin.list members: hooks ("id@Stage"), supplies, ui {panels, sections, actions, strings}, mosaicProtected.</summary>
        public static JObject Report(PluginEngineServices s)
        {
            var ui = s.UiCounts;
            return new JObject
            {
                ["hooks"] = new JArray(s.Hooks.ToArray()),
                ["supplies"] = s.SupplyCount,
                ["ui"] = new JObject { ["panels"] = ui.Panels, ["sections"] = ui.Sections, ["actions"] = ui.Actions, ["strings"] = ui.Strings },
                ["mosaicProtected"] = s.MosaicProtected,
            };
        }
    }
}
