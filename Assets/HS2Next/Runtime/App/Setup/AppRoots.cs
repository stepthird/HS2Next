// Where a process keeps its files (Unity-free, so plain-Mono tests drive it): one data root per process with the layout
// <root>/Config/settings.local.json, <root>/UserData/, <root>/Cache/. The root is the project folder in the Editor; the exe folder
// when a file portable.txt lies next to the exe (the portable mode); the folder named by the environment variable
// HS2NEXT_DATA_ROOT (players only: tests and advanced use); else the per-user folder Unity gives the player
// (Application.persistentDataPath). The exe folder stays a separate value: plugin code, the build description, the portable
// marker and the map audio junction belong to the exe, everything the user makes belongs to the data root.
// AppFolders creates the folders a process writes and proves each is writable; a folder that cannot be written falls back to the
// per-user folder once (the user is told), and a per-user folder that cannot be written is a clear failure. CacheNames keeps cache
// file paths short.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace HS2Next.App.Setup
{
    /// <summary>How the data root was chosen.</summary>
    public enum DataRootMode { Editor, Portable, Override, PerUser }

    public sealed class AppRoots
    {
        /// <summary>The marker file next to the exe that selects the portable mode (any case; its content is ignored).</summary>
        public const string PortableMarker = "portable.txt";
        /// <summary>The environment variable that names the data root of a player.</summary>
        public const string EnvDataRoot = "HS2NEXT_DATA_ROOT";
        /// <summary>A data root longer than this many characters is warned about (its longest cache path would pass 240).</summary>
        public const int LongRootChars = 100;

        public DataRootMode Mode { get; }
        /// <summary>The folder that holds Config/, UserData/ and Cache/ (absolute).</summary>
        public string DataRoot { get; }
        /// <summary>The project folder (Editor) or the folder of the exe (player), absolute.</summary>
        public string ExeDir { get; }
        /// <summary>The per-user folder of the player (the fallback of an unwritable folder), absolute; may be null in tests.</summary>
        public string PersistentDataPath { get; }

        public AppRoots(DataRootMode mode, string dataRoot, string exeDir, string persistentDataPath)
        {
            Mode = mode;
            DataRoot = Full(dataRoot);
            ExeDir = Full(exeDir);
            PersistentDataPath = Full(persistentDataPath);
        }

        /// <summary>"editor", "portable", "override" or "perUser".</summary>
        public string ModeName => ModeNameOf(Mode);

        public static string ModeNameOf(DataRootMode m) =>
            m == DataRootMode.Editor ? "editor" : m == DataRootMode.Portable ? "portable" : m == DataRootMode.Override ? "override" : "perUser";

        public string ConfigDir => Path.Combine(DataRoot, "Config");
        /// <summary>&lt;root&gt;/Config/settings.local.json (the settings file when -settings is not given).</summary>
        public string DefaultSettingsFile => Path.Combine(ConfigDir, "settings.local.json");
        /// <summary>&lt;root&gt;/UserData.</summary>
        public string DefaultUserData => Path.Combine(DataRoot, "UserData");
        /// <summary>&lt;root&gt;/Cache.</summary>
        public string DefaultCacheDir => Path.Combine(DataRoot, "Cache");
        /// <summary>&lt;persistent data path&gt;/UserData: where an unwritable UserData falls back to (null without one).</summary>
        public string FallbackUserData => PersistentDataPath == null ? null : Path.Combine(PersistentDataPath, "UserData");
        /// <summary>&lt;persistent data path&gt;/Cache: where an unwritable cache falls back to (null without one).</summary>
        public string FallbackCacheDir => PersistentDataPath == null ? null : Path.Combine(PersistentDataPath, "Cache");

        /// <summary>A settings path made absolute: a relative path is relative to the data root.</summary>
        public string Resolve(string path) =>
            string.IsNullOrEmpty(path) ? path : Path.GetFullPath(Path.Combine(DataRoot, path));

        /// <summary>The UserData folder of a run: the value given (command line over settings file; relative to the data root),
        /// else &lt;root&gt;/UserData.</summary>
        public string UserDataOf(string value) => string.IsNullOrEmpty(value) ? DefaultUserData : Resolve(value);

        /// <summary>The cache folder of a run: the value given (relative to the data root), else &lt;root&gt;/Cache.</summary>
        public string CacheDirOf(string value) => string.IsNullOrEmpty(value) ? DefaultCacheDir : Resolve(value);

        /// <summary>
        /// The settings file to read, or null for the defaults: <paramref name="given"/> (-settings) when it exists, else the
        /// default file when it exists, else null (no file is not an error). A file named by -settings that does not exist throws
        /// FileNotFoundException, unless <paramref name="gameRootGiven"/> (-gameRoot alone is enough).
        /// </summary>
        public string SettingsFileToRead(string given, bool gameRootGiven, Func<string, bool> exists = null)
        {
            exists = exists ?? File.Exists;
            string file = string.IsNullOrEmpty(given) ? DefaultSettingsFile : given;
            if (exists(file)) return file;
            if (!string.IsNullOrEmpty(given) && !gameRootGiven)
                throw new FileNotFoundException("Settings file not found: " + given + " (given by -settings). Pass an existing file, or leave -settings out to use " +
                                                DefaultSettingsFile + ".", given);
            return null;
        }

        /// <summary>The data root is longer than <see cref="LongRootChars"/> characters (its longest paths come near MAX_PATH).</summary>
        public bool IsLong => DataRoot.Length > LongRootChars;

        /// <summary>"mode perUser, data root …, exe folder …" (the settings stage line, app.paths).</summary>
        public override string ToString() => "mode " + ModeName + ", data root " + DataRoot + ", exe folder " + ExeDir;

        /// <summary>
        /// The data root of a process. <paramref name="isEditor"/>: the project folder (<paramref name="exeDir"/>), whatever the
        /// environment says. A player: the exe folder when <see cref="PortableMarker"/> lies next to it (any case); else the folder
        /// <see cref="EnvDataRoot"/> names when it is set and not empty (made absolute); else <paramref name="persistentDataPath"/>.
        /// <paramref name="env"/> reads an environment variable (default: the process environment).
        /// </summary>
        public static AppRoots Resolve(bool isEditor, string exeDir, string persistentDataPath, Func<string, string> env = null)
        {
            if (string.IsNullOrEmpty(exeDir)) throw new ArgumentException("the exe folder is required", nameof(exeDir));
            if (isEditor) return new AppRoots(DataRootMode.Editor, exeDir, exeDir, persistentDataPath);
            if (HasPortableMarker(exeDir)) return new AppRoots(DataRootMode.Portable, exeDir, exeDir, persistentDataPath);
            string v = (env ?? Environment.GetEnvironmentVariable)(EnvDataRoot);
            if (!string.IsNullOrWhiteSpace(v)) return new AppRoots(DataRootMode.Override, v.Trim(), exeDir, persistentDataPath);
            if (string.IsNullOrEmpty(persistentDataPath)) throw new ArgumentException("the per-user folder is required", nameof(persistentDataPath));
            return new AppRoots(DataRootMode.PerUser, persistentDataPath, exeDir, persistentDataPath);
        }

        /// <summary>A file named portable.txt (any case) lies in <paramref name="exeDir"/>.</summary>
        public static bool HasPortableMarker(string exeDir)
        {
            try
            {
                if (!Directory.Exists(exeDir)) return false;
                foreach (var f in Directory.EnumerateFiles(exeDir))
                    if (string.Equals(Path.GetFileName(f), PortableMarker, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception e) when (AppFolders.IsFolderError(e)) { }
            return false;
        }

        /// <summary><paramref name="path"/> is <paramref name="root"/> or lies under it (case-insensitive, after normalising).</summary>
        public static bool IsUnder(string path, string root)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root)) return false;
            string p, r;
            try
            {
                p = Path.GetFullPath(path).TrimEnd('\\', '/');
                r = Path.GetFullPath(root).TrimEnd('\\', '/');
            }
            catch (Exception e) when (AppFolders.IsFolderError(e)) { return false; }
            return string.Equals(p, r, StringComparison.OrdinalIgnoreCase) ||
                   p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   p.StartsWith(r + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        // absolute, without a trailing separator (the root of a drive keeps its own)
        static string Full(string p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            string f = Path.GetFullPath(p);
            string t = f.TrimEnd('\\', '/');
            if (t.Length == 0) return f;
            return t[t.Length - 1] == ':' ? t + Path.DirectorySeparatorChar : t;
        }
    }

    /// <summary>A folder that could not be written and the one used instead.</summary>
    public sealed class FolderFallback
    {
        /// <summary>"userData" or "cache".</summary>
        public string Kind;
        public string From;
        public string To;
        /// <summary>"&lt;exception type&gt;: &lt;message&gt;" of the failure.</summary>
        public string Reason;

        public override string ToString() => Kind + ": " + From + " is not writable (" + Reason + "); using " + To;
    }

    /// <summary>A folder HS2Next must write cannot be created or written (the settings stage fails with it).</summary>
    public sealed class FolderException : IOException
    {
        public string Folder { get; }
        public string Reason { get; }

        public FolderException(string folder, string reason) : base("HS2Next cannot write its folder " + folder + ": " + reason)
        {
            Folder = folder;
            Reason = reason;
        }
    }

    public static class AppFolders
    {
        /// <summary>The exceptions a folder that cannot be created or written throws (a missing drive, no permission, a path
        /// too long, a file in the way, an invalid name).</summary>
        public static bool IsFolderError(Exception e) =>
            e is UnauthorizedAccessException || e is IOException || e is NotSupportedException || e is ArgumentException ||
            e is System.Security.SecurityException;

        /// <summary>Creates <paramref name="dir"/> and proves it is writable: creates and deletes &lt;dir&gt;/.write-probe-&lt;pid&gt;.</summary>
        public static void Probe(string dir)
        {
            Directory.CreateDirectory(dir);
            string probe = Path.Combine(dir, ".write-probe-" + Process.GetCurrentProcess().Id);
            using (var f = new FileStream(probe, FileMode.Create, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
                f.WriteByte(0);
            if (File.Exists(probe)) File.Delete(probe);
        }

        /// <summary>
        /// Prepares one folder: <paramref name="create"/>(dir) (it creates the folder and its subfolders), then <see cref="Probe"/>.
        /// On a folder error (<see cref="IsFolderError"/>) for a folder outside <paramref name="persistentDataPath"/>, the same is
        /// done once with <paramref name="fallbackDir"/>, the switch is added to <paramref name="fallbacks"/> and the fallback is
        /// returned. Throws <see cref="FolderException"/> when the folder lies under the per-user folder (nothing to fall back to),
        /// when there is no fallback, or when the fallback fails too. Returns the folder in use.
        /// </summary>
        public static string Prepare(string kind, string dir, string fallbackDir, string persistentDataPath, Action<string> create,
            List<FolderFallback> fallbacks)
        {
            create = create ?? (d => Directory.CreateDirectory(d));
            try
            {
                create(dir);
                Probe(dir);
                return dir;
            }
            catch (Exception e) when (IsFolderError(e) && !(e is FolderException))
            {
                string reason = e.GetType().Name + ": " + e.Message;
                if (string.IsNullOrEmpty(fallbackDir) || AppRoots.IsUnder(dir, persistentDataPath) || AppRoots.IsUnder(dir, fallbackDir))
                    throw new FolderException(dir, reason);
                try
                {
                    create(fallbackDir);
                    Probe(fallbackDir);
                }
                catch (Exception e2) when (IsFolderError(e2) && !(e2 is FolderException))
                {
                    throw new FolderException(fallbackDir, e2.GetType().Name + ": " + e2.Message + " (after " + dir + ": " + reason + ")");
                }
                fallbacks?.Add(new FolderFallback { Kind = kind, From = dir, To = Path.GetFullPath(fallbackDir), Reason = reason });
                return Path.GetFullPath(fallbackDir);
            }
        }
    }

    /// <summary>Short, stable folder names for the caches (long names push cache paths past MAX_PATH; the player is not
    /// long-path aware).</summary>
    public static class CacheNames
    {
        /// <summary>The longest folder name <see cref="ThumbFolder"/> returns unchanged.</summary>
        public const int MaxFolderChars = 40;

        /// <summary>
        /// The thumbnail cache folder named after a zipmod GUID: characters that are invalid in a file name become '_'; a name of
        /// more than <see cref="MaxFolderChars"/> characters becomes its first 32 characters, '~' and 8 hex digits of a hash of
        /// the whole GUID (so distinct long GUIDs stay distinct). Deterministic across runs and machines.
        /// </summary>
        public static string ThumbFolder(string guid)
        {
            guid = guid ?? "";
            var bad = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(guid.Length);
            foreach (char c in guid) sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
            string safe = sb.ToString();
            if (safe.Length <= MaxFolderChars) return safe;
            uint h = 2166136261u;   // FNV-1a over the UTF-16 code units of the original GUID
            foreach (char c in guid)
            {
                h ^= (byte)c;
                h *= 16777619u;
                h ^= (byte)(c >> 8);
                h *= 16777619u;
            }
            return safe.Substring(0, 32) + "~" + h.ToString("x8");
        }
    }
}
