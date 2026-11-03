// The browsers' favourites: one file per user, <UserData>/config/favorites.json =
//   { "version": 1, "rows": { "<list kind>": ["guid#id", …] }, "files": { "<root id>": ["<relative path>", …] } }
// Catalog rows (items, animations, maps, lights) are keyed by their list kind and content id (`guid#id`, ordinal); files
// (character cards and files, coordinates, poses) by the id of the folder they were found under and their path relative to it
// ('/' separators, compared without case, as Windows does). Every change is written at once through a temporary file and a
// replace; unknown members are kept. A file that cannot be read is kept as favorites.json.bak, the store starts empty and
// says so once (Problem). Changed tells the open browsers. Unity-free.
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.UI.Core
{
    public sealed class FavoriteStore
    {
        public const string FileName = "favorites.json";
        public const int Version = 1;
        /// <summary>The list kinds of the catalog browsers.</summary>
        public const string Items = "items", Anime = "anime", Maps = "maps", Lights = "lights";

        readonly Dictionary<string, HashSet<string>> rows = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        readonly Dictionary<string, HashSet<string>> files = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        JObject unknown = new JObject();

        /// <summary>The file (null = kept in memory only).</summary>
        public string Path { get; private set; }
        /// <summary>Why the file could not be read at the load, or the last write failed (null = fine).</summary>
        public string Problem { get; private set; }
        /// <summary>The unreadable file was kept under this name (null = nothing was moved).</summary>
        public string BackupPath { get; private set; }
        /// <summary>The UI has shown <see cref="Problem"/> (it is shown once).</summary>
        public bool ProblemShown;
        /// <summary>Changes since the load (a view built for one revision is rebuilt for the next).</summary>
        public int Revision { get; private set; }
        /// <summary>Writes done (tests).</summary>
        public int Writes { get; private set; }
        /// <summary>A favourite was added or removed: the list kind or the root id.</summary>
        public event Action<string> Changed;

        public FavoriteStore(string path) { Path = path; }

        /// <summary>Reads the store (no file = empty). An unreadable file is moved to "&lt;name&gt;.bak" (an older backup is
        /// replaced) and the store starts empty with <see cref="Problem"/> set.</summary>
        public static FavoriteStore Load(string path)
        {
            var s = new FavoriteStore(path);
            s.Read();
            return s;
        }

        /// <summary>Reads the file again into this store (the same instance: whoever holds it sees the new content), optionally
        /// from another file; the same rules as <see cref="Load"/>. Changed is raised with null.</summary>
        public void Reload(string path = null)
        {
            if (path != null) Path = path;
            Read();
            Revision++;
            try { Changed?.Invoke(null); }
            catch (Exception) { }
        }

        void Read()
        {
            rows.Clear();
            files.Clear();
            unknown = new JObject();
            Problem = null;
            BackupPath = null;
            ProblemShown = false;
            string path = Path;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            string text;
            try { text = File.ReadAllText(path); }
            catch (Exception e) { Problem = "unreadable: " + e.Message; return; }
            try { Parse(text); }
            catch (Exception e) when (e is JsonException || e is FormatException || e is InvalidCastException || e is ArgumentException)
            {
                rows.Clear();
                files.Clear();
                unknown = new JObject();
                Problem = "unreadable: " + e.Message;
                string bak = path + ".bak";
                try
                {
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(path, bak);
                    BackupPath = bak;
                }
                catch (Exception m) { Problem += " (not moved: " + m.Message + ")"; }
            }
        }

        void Parse(string json)
        {
            var o = JObject.Parse(json);
            if (o["rows"] != null && o["rows"].Type != JTokenType.Object || o["files"] != null && o["files"].Type != JTokenType.Object)
                throw new FormatException("\"rows\" and \"files\" must be objects");
            if (o["rows"] is JObject r)
                foreach (var p in r.Properties())
                    if (p.Value is JArray a)
                        foreach (var t in a)
                            if (t.Type == JTokenType.String && ((string)t).Length > 0) Set(rows, p.Name, StringComparer.Ordinal).Add((string)t);
            if (o["files"] is JObject f)
                foreach (var p in f.Properties())
                    if (p.Value is JArray a)
                        foreach (var t in a)
                            if (t.Type == JTokenType.String && Rel((string)t).Length > 0) Set(files, p.Name, StringComparer.OrdinalIgnoreCase).Add(Rel((string)t));
            o.Remove("rows");
            o.Remove("files");
            o.Remove("version");
            unknown = o;
        }

        static HashSet<string> Set(Dictionary<string, HashSet<string>> d, string key, StringComparer cmp)
        {
            if (!d.TryGetValue(key, out var s)) d[key] = s = new HashSet<string>(cmp);
            return s;
        }

        /// <summary>A relative path as stored ('/' separators, no leading or trailing separator).</summary>
        public static string Rel(string relPath) => (relPath ?? "").Replace('\\', '/').Trim('/');

        public bool IsRow(string kind, string contentRef) =>
            kind != null && contentRef != null && rows.TryGetValue(kind, out var s) && s.Contains(contentRef);

        public bool IsFile(string rootId, string relPath) =>
            rootId != null && relPath != null && files.TryGetValue(rootId, out var s) && s.Contains(Rel(relPath));

        /// <summary>The favourite rows of a list kind (empty when none).</summary>
        public IReadOnlyCollection<string> RowsOf(string kind) => kind != null && rows.TryGetValue(kind, out var s) ? (IReadOnlyCollection<string>)s : Array.Empty<string>();

        /// <summary>The favourite files under a root (relative paths; empty when none).</summary>
        public IReadOnlyCollection<string> FilesOf(string rootId) => rootId != null && files.TryGetValue(rootId, out var s) ? (IReadOnlyCollection<string>)s : Array.Empty<string>();

        /// <summary>Adds or removes a catalog row; writes the file when it changed. Returns true when it changed.</summary>
        public bool SetRow(string kind, string contentRef, bool on)
        {
            if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(contentRef)) return false;
            bool changed = on ? Set(rows, kind, StringComparer.Ordinal).Add(contentRef)
                : rows.TryGetValue(kind, out var s) && s.Remove(contentRef);
            if (changed) Commit(kind);
            return changed;
        }

        /// <summary>Adds or removes a file; writes the file when it changed. Returns true when it changed.</summary>
        public bool SetFile(string rootId, string relPath, bool on)
        {
            string rel = Rel(relPath);
            if (string.IsNullOrEmpty(rootId) || rel.Length == 0) return false;
            bool changed = on ? Set(files, rootId, StringComparer.OrdinalIgnoreCase).Add(rel)
                : files.TryGetValue(rootId, out var s) && s.Remove(rel);
            if (changed) Commit(rootId);
            return changed;
        }

        void Commit(string what)
        {
            Revision++;
            Save();
            try { Changed?.Invoke(what); }
            catch (Exception) { }
        }

        public string ToJson()
        {
            var o = new JObject { ["version"] = Version };
            var r = new JObject();
            foreach (var kv in Sorted(rows)) r[kv.Key] = new JArray(kv.Value);
            var f = new JObject();
            foreach (var kv in Sorted(files)) f[kv.Key] = new JArray(kv.Value);
            o["rows"] = r;
            o["files"] = f;
            foreach (var p in unknown.Properties()) if (o[p.Name] == null) o[p.Name] = p.Value.DeepClone();
            return o.ToString(Formatting.Indented);
        }

        static IEnumerable<KeyValuePair<string, List<string>>> Sorted(Dictionary<string, HashSet<string>> d)
        {
            var keys = new List<string>(d.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys)
            {
                if (d[k].Count == 0) continue;
                var l = new List<string>(d[k]);
                l.Sort(StringComparer.Ordinal);
                yield return new KeyValuePair<string, List<string>>(k, l);
            }
        }

        /// <summary>Writes the file through a temporary file and a replace (null = written, else the problem, also kept in
        /// <see cref="Problem"/>).</summary>
        public string Save()
        {
            if (string.IsNullOrEmpty(Path)) return null;
            string tmp = null;
            try
            {
                string dir = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                tmp = Path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
                File.WriteAllText(tmp, ToJson(), new System.Text.UTF8Encoding(false));
                if (File.Exists(Path)) File.Replace(tmp, Path, null, true);
                else File.Move(tmp, Path);
                tmp = null;
                Writes++;
                return null;
            }
            catch (Exception e) { Problem = "not written: " + e.Message; ProblemShown = false; return Problem; }
            finally { if (tmp != null) try { File.Delete(tmp); } catch (Exception) { } }
        }
    }
}
