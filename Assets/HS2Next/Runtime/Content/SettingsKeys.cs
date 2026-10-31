// The keys of Config/settings.local.json that the runtime reads, in one place (Unity-free, no JSON library): ContentSettings
// reads Content, AppSettings reads App (a member of an object block as "block.member"), the options layer reads the `ui`
// block's flat aliases and its option sections ("ui.<section>": the options.json shape, e.g. ui.maker.keepColours). A reader
// that starts to read a new key adds it here and to Config/settings.example.json with its comment line: the startup logs the
// keys of the file that are not here once, and the plain-Mono group SettingsKeysTests checks the example against this list
// and the switches of the non-vanilla defaults.
using System;
using System.Collections.Generic;
using System.Linq;

namespace HS2Next.Content
{
    public static class SettingsKeys
    {
        /// <summary>The members ContentSettings reads.</summary>
        public static readonly IReadOnlyList<string> Content = new[] { "hs2Root", "modsFolders", "cacheDir", "aiInstall", "sideloaderZipmodSet", "sideloaderAssetRedirect" };

        /// <summary>The members of ContentSettings' block "content" ("content.member").</summary>
        public static readonly IReadOnlyList<string> ContentBlock = new[] { "content.bundleCacheMB", "content.unloadUnusedMinSec", "content.texturePrescan" };

        /// <summary>The members AppSettings reads ("block.member" inside an object).</summary>
        public static readonly IReadOnlyList<string> App = new[]
        {
            "userData", "mapAudioLink", "maps.maxMirrors", "maps.audioLinkEditor",
            "render.aa", "render.taaQuality", "render.grading", "render.ambient", "render.lightFalloff", "render.shadowDistance", "render.backgroundColor", "render.ssr",
            "kinematics.exclusive", "kinematics.rotationLimits", "kinematics.limitMinInterior", "kinematics.missingTargetsFirstFrame",
            "ui.enabled", "app.autoStart", "app.frameRate", "app.runInBackground",
            "bridge.enabled", "bridge.port", "bridge.token", "bridge.outputDir", "bridge.allowQuit",
            "plugins.enabled", "plugins.dir", "plugins.disabled",
            "studio.accessoryItems",
        };

        /// <summary>The object blocks whose members are checked one by one.</summary>
        public static readonly IReadOnlyList<string> Blocks = new[] { "maps", "render", "kinematics", "ui", "app", "bridge", "plugins", "content", "studio" };

        /// <summary>The flat option aliases of the `ui` block (the options layer) with the alias each one repeats, if any.</summary>
        public static readonly IReadOnlyDictionary<string, string> UiAliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["language"] = null, ["scale"] = null, ["uiScale"] = "scale", ["preset"] = null, ["keyPreset"] = "preset",
        };

        /// <summary>The switches of the non-vanilla defaults that this file sets, with their defaults (keep in step with the
        /// table of switches whose default is not vanilla): the zipmod set and the per-asset redirect of the loader, the
        /// Maker's colour keeping on an item change, the plugin data on an accessory slot copy and the accessory set handles
        /// (option sections of the `ui` block).</summary>
        public static readonly IReadOnlyDictionary<string, bool> Switches = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["sideloaderZipmodSet"] = true, ["sideloaderAssetRedirect"] = true, ["ui.maker.keepColours"] = false,
            ["ui.maker.copyAccessoryPluginData"] = false, ["ui.maker.accessoryHandles"] = true, ["studio.accessoryItems"] = true,
        };

        /// <summary>Every key the runtime reads (Content, App, the `ui` aliases as "ui.&lt;alias&gt;").</summary>
        public static IEnumerable<string> All => Content.Concat(ContentBlock).Concat(App).Concat(UiAliases.Keys.Select(a => "ui." + a));

        /// <summary>
        /// Whether a member of the file is read: <paramref name="path"/> is a top-level name or "block.member" of a block of
        /// <see cref="Blocks"/>; <paramref name="isObject"/> = the value is a JSON object. Members starting with "_" are
        /// comments. In the `ui` block an object member is an option section (its options are the options layer's to check).
        /// </summary>
        public static bool IsKnown(string path, bool isObject)
        {
            if (string.IsNullOrEmpty(path)) return false;
            int dot = path.IndexOf('.');
            string first = dot < 0 ? path : path.Substring(0, dot);
            string rest = dot < 0 ? null : path.Substring(dot + 1);
            if (first.StartsWith("_", StringComparison.Ordinal) || (rest != null && rest.StartsWith("_", StringComparison.Ordinal))) return true;
            if (rest == null) return Content.Contains(first) || App.Contains(first) || (Blocks.Contains(first) && isObject);
            if (App.Contains(path) || ContentBlock.Contains(path)) return true;
            return first == "ui" && (UiAliases.ContainsKey(rest) || isObject);
        }

        /// <summary>The members of a file the runtime does not read, from (path, value is an object) pairs as <see cref="IsKnown"/>
        /// takes them.</summary>
        public static List<string> Unknown(IEnumerable<KeyValuePair<string, bool>> members) =>
            members.Where(m => !IsKnown(m.Key, m.Value)).Select(m => m.Key).ToList();
    }
}
