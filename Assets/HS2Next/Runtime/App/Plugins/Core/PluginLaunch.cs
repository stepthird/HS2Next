// Where the plugins come from in this run and whether any load (Unity-free). Precedence: the command line's
// -pluginsDir <dir> (plugins load from that folder; an explicit test call that names a folder counts the same), then
// -noPlugins, then the environment switch HS2NEXT_NO_PLUGINS=1 (test runners set it for every process), then the
// settings plugins.enabled / plugins.dir (default: the Plugins folder next to the app). plugins.disabled refuses its
// GUIDs with state disabled whenever plugins load. Relative folders resolve against the app folder.
using System;
using System.Collections.Generic;
using System.IO;

namespace HS2Next.App.Plugins
{
    public sealed class PluginLaunch
    {
        public const string EnvNoPlugins = "HS2NEXT_NO_PLUGINS";
        public const string SourceSettings = "settings", SourceArg = "-pluginsDir", SourceNoPlugins = "-noPlugins", SourceEnv = "env";

        /// <summary>The plugin folder (absolute; reported even when no plugin loads).</summary>
        public string Root;
        /// <summary>Plugins load in this run.</summary>
        public bool Enabled;
        /// <summary>What decided: "settings", "-pluginsDir", "-noPlugins" or "env".</summary>
        public string Source;
        /// <summary>The disabled list.</summary>
        public List<string> Disabled = new List<string>();

        /// <summary>True for "1" or "true" (any case) after trimming.</summary>
        public static bool EnvOn(string value)
        {
            if (value == null) return false;
            value = value.Trim();
            return value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Decides the run's plugin folder and switch. <paramref name="argDir"/> = the -pluginsDir value (null when absent),
        /// <paramref name="argNoPlugins"/> = -noPlugins given, <paramref name="env"/> = the value of HS2NEXT_NO_PLUGINS (null
        /// when unset), <paramref name="appFolder"/> = the folder relative paths resolve against (the project folder in the
        /// Editor, the player folder in a player).
        /// </summary>
        public static PluginLaunch Resolve(string argDir, bool argNoPlugins, string env, bool settingsEnabled, string settingsDir,
                                          IEnumerable<string> settingsDisabled, string appFolder)
        {
            var l = new PluginLaunch();
            if (settingsDisabled != null) l.Disabled.AddRange(settingsDisabled);
            string defaultRoot = string.IsNullOrEmpty(settingsDir) ? Path.Combine(appFolder ?? "", "Plugins") : settingsDir;
            if (!string.IsNullOrEmpty(argDir))
            {
                l.Root = Full(argDir, appFolder);
                l.Enabled = true;
                l.Source = SourceArg;
                return l;
            }
            l.Root = Full(defaultRoot, appFolder);
            if (argNoPlugins)
            {
                l.Enabled = false;
                l.Source = SourceNoPlugins;
            }
            else if (EnvOn(env))
            {
                l.Enabled = false;
                l.Source = SourceEnv;
            }
            else
            {
                l.Enabled = settingsEnabled;
                l.Source = SourceSettings;
            }
            return l;
        }

        static string Full(string dir, string appFolder)
        {
            if (string.IsNullOrEmpty(dir)) return null;
            return Path.GetFullPath(Path.IsPathRooted(dir) ? dir : Path.Combine(appFolder ?? Directory.GetCurrentDirectory(), dir));
        }

        public override string ToString() => (Enabled ? "on" : "off") + " (" + Source + ") " + Root;
    }
}
