// Local settings: where the HS2 install and extra mods folders are, and where caches go.
// Read from <data root>/Config/settings.local.json (in the Editor the project's Config/settings.local.json, gitignored; copy
// Config/settings.example.json). The data root (HS2Next.App.Setup.AppRoots) is the project folder in the Editor, the exe folder
// in the portable mode (portable.txt next to the exe), the folder HS2NEXT_DATA_ROOT names, else the per-user folder of the
// player. Paths may be absolute or relative to the data root. No settings file is not an error (defaults: no HS2 install).
// Command-line overrides:
//   -settings <file>  -gameRoot <dir>  -modsFolder <dir> (repeatable)  -cacheDir <dir>  -sideloader on|off
//   -bundleCacheMB <n> (the byte budget of the unowned bundles; tests use 0 to evict every unowned bundle)
//   -texturePrescan (the background verdict scan of every bundle, also in the Editor), -noTexturePrescan (none for one run)
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HS2Next.Content
{
    [Serializable]
    public class ContentSettings
    {
        /// <summary>HS2 install root (contains abdata and mods). Empty = no HS2 install.</summary>
        public string hs2Root = "";

        /// <summary>Extra stand-alone zipmod folders, lowest priority last.</summary>
        public string[] modsFolders = Array.Empty<string>();

        /// <summary>Cache directory; empty = &lt;data root&gt;/Cache.</summary>
        public string cacheDir = "";

        /// <summary>
        /// The HS2 install shows the AI-Shoujo content of the Studio lists (the game's `IsInstallAI`
        /// 5): true (default) = no AI gate, as HS2 Studio measured on the reference install (its item lists
        /// include groups 4 and 12, items 89 / 197 / 185 / 121 / 1022, anime group 0 categories 1-12, maps 0 and 1);
        /// false = emulate a machine without it (StudioLoadOptions.EmulateAIInstallGate). Tests keep their own choice.
        /// </summary>
        public bool aiInstall = true;

        /// <summary>The zipmod loader's set of zipmods (ContentCatalog.SideloaderZipmodSet, default true): the &lt;game&gt; filter
        /// and one file per GUID by version; false = every file, the last one per GUID. Takes effect at the next start.</summary>
        public bool sideloaderZipmodSet = true;

        /// <summary>The per-asset redirect of named loads (ContentCatalog.SideloaderAssetRedirect, default true); false = each
        /// load reads its row's own source.</summary>
        public bool sideloaderAssetRedirect = true;

        /// <summary>The block "content": the catalog's memory and the texture check (see <see cref="ContentBlock"/>).</summary>
        public ContentBlock content = new ContentBlock();

        /// <summary>The members of the block "content".</summary>
        [Serializable]
        public class ContentBlock
        {
            /// <summary>The byte budget of the bundles no owner holds any more, in MB (0: every one is evicted at the next pass).</summary>
            public int bundleCacheMB = 768;
            /// <summary>The minimum seconds between two Resources.UnloadUnusedAssets calls the catalog makes by itself.</summary>
            public float unloadUnusedMinSec = 30;
            /// <summary>The background verdict scan of every bundle's textures after the boot: the player runs it when true (the
            /// default); the Editor and test hosts only with -texturePrescan.</summary>
            public bool texturePrescan = true;
        }

        /// <summary>The members of the settings file this class reads (SettingsKeys.Content; Config/settings.example.json names
        /// each with its comment line).</summary>
        public static IReadOnlyList<string> KnownKeys => SettingsKeys.Content;

        /// <summary>The Unity project root (parent of Assets) in the Editor, the folder of the exe in a player (plugin code, the
        /// build description, the portable marker). Main thread only.</summary>
        public static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        static HS2Next.App.Setup.AppRoots roots;

        /// <summary>
        /// The data root of this process (resolved once, on the main thread at the first use): the project folder in the Editor;
        /// in a player the exe folder with portable.txt, else HS2NEXT_DATA_ROOT, else Application.persistentDataPath.
        /// </summary>
        public static HS2Next.App.Setup.AppRoots Roots =>
            roots ??= HS2Next.App.Setup.AppRoots.Resolve(Application.isEditor, ProjectRoot, Application.persistentDataPath);

        /// <summary>
        /// Replaces the data root of this process (null: resolve it again at the next use). For an Editor play session that must not
        /// touch the project's settings (the launch override's `dataRoot`) and for tests; a player never calls it.
        /// </summary>
        public static void SetRoots(HS2Next.App.Setup.AppRoots value) => roots = value;

        /// <summary>&lt;data root&gt;/Config/settings.local.json.</summary>
        public static string DefaultPath => Roots.DefaultSettingsFile;

        /// <summary>Absolute cache directory (empty cacheDir = &lt;data root&gt;/Cache).</summary>
        public string ResolvedCacheDir => Roots.CacheDirOf(cacheDir);

        /// <summary>Makes a settings path absolute (relative paths are relative to the data root).</summary>
        public static string Resolve(string path) => Roots.Resolve(path);

        /// <summary>Reads a settings file.</summary>
        public static ContentSettings Load(string file)
        {
            var s = JsonUtility.FromJson<ContentSettings>(File.ReadAllText(file)) ?? new ContentSettings();
            s.modsFolders ??= Array.Empty<string>();
            s.content ??= new ContentBlock();
            return s;
        }

        /// <summary>
        /// Reads the settings file (default <see cref="DefaultPath"/>, or -settings) and applies command-line overrides. Without the
        /// default settings file the defaults apply (no HS2 install: the first-run guide asks for one); a file named by -settings
        /// must exist.
        /// </summary>
        public static ContentSettings LoadDefault(string[] args = null)
        {
            args ??= Environment.GetCommandLineArgs();
            string root = Arg(args, "-gameRoot");
            string file = Roots.SettingsFileToRead(Arg(args, "-settings"), root != null);
            var s = file != null ? Load(file) : new ContentSettings();
            if (root != null) s.hs2Root = root;
            if (Arg(args, "-cacheDir") is string cd) s.cacheDir = cd;
            // -sideloader off: both switches off (the earlier zipmod set and sources, for comparisons); on: both on
            if (Arg(args, "-sideloader") is string sl) s.sideloaderZipmodSet = s.sideloaderAssetRedirect = !string.Equals(sl, "off", StringComparison.OrdinalIgnoreCase);
            s.content ??= new ContentBlock();
            if (Arg(args, "-bundleCacheMB") is string mb && int.TryParse(mb, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int mbv) && mbv >= 0)
                s.content.bundleCacheMB = mbv;
            if (Array.IndexOf(args, "-texturePrescan") >= 0) s.content.texturePrescan = true;
            var extra = new List<string>(s.modsFolders);
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-modsFolder") extra.Add(args[i + 1]);
            s.modsFolders = extra.ToArray();
            return s;
        }

        static string Arg(string[] a, string name)
        {
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }
    }
}
