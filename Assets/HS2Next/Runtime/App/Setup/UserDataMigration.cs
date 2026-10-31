// The one migration of a user's files (Unity-free): earlier players kept UserData next to the exe; a player in the per-user mode
// (or on a data root named by HS2NEXT_DATA_ROOT) offers to copy that folder into its own UserData, and the same copy runs on
// demand from any folder the user picks (an older
// portable folder, a trial folder). The copy never moves, deletes or changes the source, never overwrites a file (identical
// bytes = "same", different bytes = a conflict left alone), never follows a link (a junction or symbolic link is refused and not
// entered), and copies only the user's work: scenes, characters, coordinates, poses, screenshots, backgrounds, frames, the UI's
// config, plugin data and the crash quarantine; never the bridge's endpoint and token, the bridge's output, the crash marker or
// temporary files. The settings file of the old folder is copied only when the new root has none, its relative paths made
// absolute against the old folder. Each run is recorded in <UserData>/migration.json; a second run copies only what is missing.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.App.Setup
{
    /// <summary>One file of a migration: its path relative to the source UserData and its kind.</summary>
    public sealed class MigrationItem
    {
        public string Rel;
        public string Kind;
        public long Bytes;
    }

    /// <summary>What a copy from one source would do (no file is written to make it).</summary>
    public sealed class MigrationPlan
    {
        /// <summary>The source UserData (absolute).</summary>
        public string Source;
        /// <summary>The files to copy, in walk order.</summary>
        public List<MigrationItem> Files = new List<MigrationItem>();
        /// <summary>Files left out by rule, counted by kind ("bridge", "shots", "crashMarker", "temporary", "state", "other").</summary>
        public Dictionary<string, int> Skipped = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Links (junctions, symbolic links) found and not entered, relative to the source.</summary>
        public List<string> Refused = new List<string>();
        /// <summary>The old settings file next to the source (&lt;source&gt;/../Config/settings.local.json) when it exists.</summary>
        public string SettingsFile;
        /// <summary>The bytes of <see cref="Files"/>.</summary>
        public long Bytes => Files.Sum(f => f.Bytes);
        /// <summary>The files to copy, counted by kind (the offer's numbers).</summary>
        public Dictionary<string, int> CountByKind() => Files.GroupBy(f => f.Kind).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
    }

    /// <summary>The record of one migration run (one entry of migration.json's "runs").</summary>
    public sealed class MigrationRun
    {
        [JsonProperty("source")] public string Source;
        [JsonProperty("at")] public string At;
        [JsonProperty("copied")] public int Copied;
        [JsonProperty("same")] public int Same;
        [JsonProperty("conflicts")] public List<string> Conflicts = new List<string>();
        [JsonProperty("refused")] public List<string> Refused = new List<string>();
        [JsonProperty("skipped")] public Dictionary<string, int> Skipped = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>The bytes copied by this run.</summary>
        [JsonProperty("bytes")] public long Bytes;
        /// <summary>Files that could not be read or written ("rel: reason"); the run goes on without them.</summary>
        [JsonProperty("failed")] public List<string> Failed = new List<string>();
        /// <summary>The old settings file: "copied", "kept" (the new root has its own), or "none".</summary>
        [JsonProperty("settings")] public string Settings = "none";
    }

    /// <summary>A migration that cannot start (no source, not enough free space); nothing was copied.</summary>
    public sealed class MigrationException : Exception
    {
        public MigrationException(string message) : base(message) { }
    }

    public static class UserDataMigration
    {
        /// <summary>The report file inside the destination UserData.</summary>
        public const string ReportFile = "migration.json";

        // the user's work, copied whole (first path segment, any case)
        static readonly string[] WorkFolders = { "scene", "chara", "coordinate", "pose", "cap", "bg", "frame", "config", "Plugins" };

        /// <summary>
        /// The folder to copy from when <paramref name="picked"/> is not a UserData itself: a folder holding a UserData subfolder and
        /// none of the user's folders becomes that subfolder (an old player or portable folder). Absolute.
        /// </summary>
        public static string NormaliseSource(string picked)
        {
            string p = Path.GetFullPath(picked);
            // Config/ and Plugins/ also sit next to an exe (the settings, the plugin code): only the work folders decide
            bool direct = WorkFolders.Take(7).Any(w => Directory.Exists(Path.Combine(p, w)));
            string sub = Path.Combine(p, "UserData");
            return !direct && Directory.Exists(sub) ? sub : p;
        }

        /// <summary>
        /// The source a start offers to copy, or null: only when the data root is apart from the exe (the per-user mode, or a
        /// root named by HS2NEXT_DATA_ROOT) and UserData is at its default under that root, from the exe folder's UserData when it
        /// is a plain folder (not a link) holding at least one user file, neither inside nor around the destination, and not
        /// recorded in the destination's migration.json yet. Never in the portable mode (the exe folder is the root) or the Editor.
        /// </summary>
        public static string Offered(AppRoots roots, string userDataSource, string userData)
        {
            if (roots == null || (roots.Mode != DataRootMode.PerUser && roots.Mode != DataRootMode.Override) || userDataSource != "default" ||
                string.IsNullOrEmpty(roots.ExeDir)) return null;
            string legacy = Path.Combine(roots.ExeDir, "UserData");
            try
            {
                if (!Directory.Exists(legacy) || IsLink(new DirectoryInfo(legacy))) return null;
                if (AppRoots.IsUnder(legacy, userData) || AppRoots.IsUnder(userData, legacy)) return null;
                if (!HasUserFiles(legacy) || Recorded(userData, legacy)) return null;
            }
            catch (Exception e) when (AppFolders.IsFolderError(e)) { return null; }
            return Path.GetFullPath(legacy);
        }

        /// <summary>
        /// The folder holds at least one user file: scene/*.hs2nscene, chara/*.hs2nchara, coordinate/**/*.hs2ncoord,
        /// pose/**/*.hs2npose, a file under cap/, bg/, frame/ or Plugins/, or config/*.json (links are not followed).
        /// </summary>
        public static bool HasUserFiles(string userData)
        {
            foreach (var it in Walk(userData, null, null))
            {
                string first = First(it.Rel), name = Path.GetFileName(it.Rel), ext = Path.GetExtension(it.Rel);
                bool top = it.Rel.IndexOf(Path.DirectorySeparatorChar) == it.Rel.LastIndexOf(Path.DirectorySeparatorChar);
                if (Is(first, "scene") && top && Is(ext, ".hs2nscene")) return true;
                if (Is(first, "chara") && top && Is(ext, ".hs2nchara")) return true;
                if (Is(first, "coordinate") && Is(ext, ".hs2ncoord")) return true;
                if (Is(first, "pose") && Is(ext, ".hs2npose")) return true;
                if ((Is(first, "cap") || Is(first, "bg") || Is(first, "frame") || Is(first, "Plugins")) && !IsTemporary(name)) return true;
                if (Is(first, "config") && top && Is(ext, ".json")) return true;
            }
            return false;
        }

        /// <summary>migration.json of <paramref name="userData"/> holds a run from <paramref name="source"/>.</summary>
        public static bool Recorded(string userData, string source)
        {
            var report = ReadReport(userData);
            string s = Path.GetFullPath(source).TrimEnd('\\', '/');
            return report["runs"] is JArray runs && runs.OfType<JObject>().Any(r =>
                string.Equals(((string)r["source"] ?? "").TrimEnd('\\', '/'), s, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The report of <paramref name="userData"/> ({"version": 1, "runs": []} when absent or unreadable).</summary>
        public static JObject ReadReport(string userData)
        {
            string f = Path.Combine(userData, ReportFile);
            try
            {
                if (File.Exists(f) && Parse(File.ReadAllText(f)) is JObject o && o["runs"] is JArray) return o;
            }
            catch (Exception e) when (e is JsonException || AppFolders.IsFolderError(e)) { }
            return new JObject { ["version"] = 1, ["runs"] = new JArray() };
        }

        /// <summary>What a copy from <paramref name="source"/> (a UserData folder) would take; nothing is written.</summary>
        public static MigrationPlan Plan(string source)
        {
            source = Path.GetFullPath(source).TrimEnd('\\', '/');
            if (!Directory.Exists(source)) throw new MigrationException("the folder " + source + " does not exist");
            var plan = new MigrationPlan { Source = source };
            if (IsLink(new DirectoryInfo(source))) throw new MigrationException("the folder " + source + " is a link; pick the folder it points to");
            foreach (var it in Walk(source, plan.Refused, null))
            {
                string kind = KindOf(it.Rel);
                if (kind.StartsWith("-", StringComparison.Ordinal))
                {
                    string k = kind.Substring(1);
                    plan.Skipped[k] = plan.Skipped.TryGetValue(k, out int n) ? n + 1 : 1;
                }
                else plan.Files.Add(new MigrationItem { Rel = it.Rel, Kind = kind, Bytes = it.Bytes });
            }
            string settings = Path.Combine(Path.GetDirectoryName(source) ?? source, "Config", "settings.local.json");
            if (File.Exists(settings)) plan.SettingsFile = settings;
            return plan;
        }

        /// <summary>
        /// Copies <paramref name="source"/> (a UserData folder, see <see cref="NormaliseSource"/>) into <paramref name="destUserData"/>
        /// by the rules above, and the old settings file into <paramref name="destSettingsFile"/> when that does not exist (null:
        /// never). Checks the free space first (the bytes still to copy + 10 %; <paramref name="freeBytes"/> overrides the drive's
        /// free space, for tests). <paramref name="progress"/> gets (files done, files planned). Appends the run to
        /// &lt;destUserData&gt;/migration.json and returns it. Throws <see cref="MigrationException"/> before copying anything when
        /// the source is missing or a link, the destination lies inside the source (or the reverse), or the space is short.
        /// </summary>
        public static MigrationRun Run(string source, string destUserData, string destSettingsFile = null, Action<int, int> progress = null,
            Func<string, long> freeBytes = null, DateTime? now = null)
        {
            var plan = Plan(source);
            string dest = Path.GetFullPath(destUserData).TrimEnd('\\', '/');
            if (AppRoots.IsUnder(dest, plan.Source) || AppRoots.IsUnder(plan.Source, dest))
                throw new MigrationException("the folders " + plan.Source + " and " + dest + " overlap; pick another folder");
            // the space the copy still needs (files already at the destination are compared, not copied)
            long need = 0;
            foreach (var f in plan.Files) if (!File.Exists(Path.Combine(dest, f.Rel))) need += f.Bytes;
            need += need / 10;
            long free = (freeBytes ?? FreeBytes)(dest);
            if (free >= 0 && free < need)
                throw new MigrationException("not enough free space for the copy: " + Mb(need) + " needed, " + Mb(free) + " free on the drive of " + dest);
            Directory.CreateDirectory(dest);

            var run = new MigrationRun
            {
                Source = plan.Source,
                At = (now ?? DateTime.UtcNow).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                Refused = plan.Refused,
                Skipped = plan.Skipped,
            };
            int done = 0;
            foreach (var f in plan.Files)
            {
                string from = Path.Combine(plan.Source, f.Rel), to = Path.Combine(dest, f.Rel);
                try
                {
                    if (File.Exists(to))
                    {
                        if (SameBytes(from, to)) run.Same++;
                        else run.Conflicts.Add(f.Rel);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(to));
                        File.Copy(from, to, false);
                        run.Copied++;
                        run.Bytes += f.Bytes;
                    }
                }
                catch (Exception e) when (AppFolders.IsFolderError(e))
                {
                    run.Failed.Add(f.Rel + ": " + e.GetType().Name + ": " + e.Message);
                }
                progress?.Invoke(++done, plan.Files.Count);
            }
            if (destSettingsFile != null && plan.SettingsFile != null)
            {
                if (File.Exists(destSettingsFile)) run.Settings = "kept";
                else
                {
                    try
                    {
                        WriteAtomic(destSettingsFile, RewriteSettings(File.ReadAllText(plan.SettingsFile), Path.GetDirectoryName(Path.GetDirectoryName(plan.SettingsFile)), plan.Source));
                        run.Settings = "copied";
                    }
                    catch (Exception e) when (e is JsonException || AppFolders.IsFolderError(e))
                    {
                        run.Failed.Add("Config/settings.local.json: " + e.GetType().Name + ": " + e.Message);
                    }
                }
            }
            var report = ReadReport(dest);
            ((JArray)report["runs"]).Add(JObject.FromObject(run));
            WriteAtomic(Path.Combine(dest, ReportFile), report.ToString(Formatting.Indented) + "\n");
            return run;
        }

        /// <summary>
        /// An old settings file for a new root: hs2Root, cacheDir, userData, bridge.outputDir and each modsFolders entry made absolute
        /// against <paramref name="oldRoot"/> when relative; a userData (or bridge.outputDir) inside the migrated
        /// <paramref name="migratedUserData"/> is dropped (the new root has its own); every other member, comment and the order kept.
        /// </summary>
        public static string RewriteSettings(string json, string oldRoot, string migratedUserData)
        {
            var o = Parse(json) as JObject ?? throw new JsonReaderException("the settings file is not a JSON object");
            string Abs(string v) => string.IsNullOrEmpty(v) || Path.IsPathRooted(v) && !IsDriveRelative(v) ? v : Path.GetFullPath(Path.Combine(oldRoot, v));
            foreach (var key in new[] { "hs2Root", "cacheDir" })
                if (o[key] is JValue v && v.Type == JTokenType.String) o[key] = Abs((string)v);
            if (o["modsFolders"] is JArray mods)
                for (int i = 0; i < mods.Count; i++)
                    if (mods[i].Type == JTokenType.String) mods[i] = Abs((string)mods[i]);
            if (o["userData"] is JValue ud && ud.Type == JTokenType.String && !string.IsNullOrEmpty((string)ud))
            {
                string a = Abs((string)ud);
                if (AppRoots.IsUnder(a, migratedUserData)) o.Remove("userData");
                else o["userData"] = a;
            }
            if (o["bridge"] is JObject b && b["outputDir"] is JValue od && od.Type == JTokenType.String && !string.IsNullOrEmpty((string)od))
            {
                string a = Abs((string)od);
                if (AppRoots.IsUnder(a, migratedUserData)) b.Remove("outputDir");
                else b["outputDir"] = a;
            }
            return o.ToString(Formatting.Indented) + "\n";
        }

        /// <summary>JSON text as tokens, strings kept as written (no date conversion).</summary>
        public static JToken Parse(string json)
        {
            using (var r = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None })
                return JToken.ReadFrom(r);
        }

        // ------------------------------------------------------------ rules

        /// <summary>The kind of a source file (its first path segment), or "-&lt;reason&gt;" when it is left out.</summary>
        static string KindOf(string rel)
        {
            string name = Path.GetFileName(rel), first = First(rel);
            bool top = rel.IndexOf(Path.DirectorySeparatorChar) < 0;
            if (IsTemporary(name)) return "-temporary";
            if (top) return Is(name, ReportFile) || Is(name, InstanceLock.FileName) ? "-state" : "-other";
            if (Is(first, "Bridge"))
            {
                string second = rel.Split(Path.DirectorySeparatorChar)[1];
                return Is(second, "shots") ? "-shots" : "-bridge";
            }
            if (Is(first, "content")) return Is(rel, Path.Combine("content", "quarantine.tsv")) ? "content" : Is(name, "loading.marker") ? "-crashMarker" : "-other";
            foreach (var w in WorkFolders) if (Is(first, w)) return w;
            return "-other";
        }

        static bool IsTemporary(string name) => name.IndexOf(".tmp", StringComparison.OrdinalIgnoreCase) >= 0 || name.StartsWith(".write-probe-", StringComparison.Ordinal);

        static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        static string First(string rel)
        {
            int i = rel.IndexOf(Path.DirectorySeparatorChar);
            return i < 0 ? rel : rel.Substring(0, i);
        }

        struct Entry
        {
            public string Rel;
            public long Bytes;
        }

        // files under root by hand: a link (reparse point) is listed in refused and never entered or read
        static IEnumerable<Entry> Walk(string root, List<string> refused, List<string> unreadable)
        {
            root = Path.GetFullPath(root).TrimEnd('\\', '/');
            var stack = new Stack<DirectoryInfo>();
            stack.Push(new DirectoryInfo(root));
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                FileSystemInfo[] entries;
                try { entries = d.GetFileSystemInfos(); }
                catch (Exception e) when (AppFolders.IsFolderError(e)) { unreadable?.Add(d.FullName); continue; }
                foreach (var e in entries.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    string rel = e.FullName.Substring(root.Length + 1);
                    if (IsLink(e)) { refused?.Add(rel); continue; }
                    if (e is DirectoryInfo sub) stack.Push(sub);
                    else if (e is FileInfo fi) yield return new Entry { Rel = rel, Bytes = fi.Length };
                }
            }
        }

        static bool IsLink(FileSystemInfo e) => (e.Attributes & FileAttributes.ReparsePoint) != 0;

        static bool IsDriveRelative(string p) => p.Length >= 2 && p[1] == ':' && (p.Length == 2 || (p[2] != '\\' && p[2] != '/'));

        static bool SameBytes(string a, string b)
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return false;
            using (var sa = fa.OpenRead())
            using (var sb = fb.OpenRead())
            {
                var ba = new byte[65536];
                var bb = new byte[65536];
                while (true)
                {
                    int na = sa.Read(ba, 0, ba.Length);
                    int nb = ReadFull(sb, bb, na);
                    if (na != nb) return false;
                    if (na == 0) return true;
                    for (int i = 0; i < na; i++) if (ba[i] != bb[i]) return false;
                }
            }
        }

        static int ReadFull(Stream s, byte[] buf, int count)
        {
            int n = 0;
            while (n < count)
            {
                int r = s.Read(buf, n, count - n);
                if (r == 0) break;
                n += r;
            }
            return n;
        }

        static long FreeBytes(string dir)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(dir));
                return string.IsNullOrEmpty(root) ? -1 : new DriveInfo(root).AvailableFreeSpace;
            }
            catch (Exception e) when (AppFolders.IsFolderError(e)) { return -1; }   // unknown: the copy reports per-file failures
        }

        static string Mb(long bytes) => (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";

        static void WriteAtomic(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp" + System.Diagnostics.Process.GetCurrentProcess().Id;
            File.WriteAllText(tmp, text, new System.Text.UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
    }
}
