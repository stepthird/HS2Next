// Recent scene files (U32): `<UserData>/config/recent.json` =
//   { "version": 1, "files": ["<absolute path>", …] }   newest first, at most the option general.recentCount (8).
// Unknown members are kept; an unreadable file is replaced by an empty list on the next write. Unity-free.
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;

namespace HS2Next.UI.Panels
{
    public sealed class RecentFiles
    {
        public const int Version = 1;
        readonly List<string> files = new List<string>();
        JObject unknown = new JObject();

        public string Path { get; }
        public int Max { get; set; } = 8;
        public IReadOnlyList<string> Files => files;
        /// <summary>Why the last load failed (null = fine or no file).</summary>
        public string Problem { get; private set; }

        public RecentFiles(string path) { Path = path; }

        public static RecentFiles Load(string path, int max = 8)
        {
            var r = new RecentFiles(path) { Max = max };
            try
            {
                if (path != null && File.Exists(path)) r.Parse(File.ReadAllText(path));
            }
            catch (Exception e) { r.Problem = e.Message; }
            return r;
        }

        public void Parse(string json)
        {
            files.Clear();
            var o = JObject.Parse(json);
            if (o["files"] is JArray a)
                foreach (var t in a)
                    if (t.Type == JTokenType.String && !string.IsNullOrEmpty((string)t) && !Contains((string)t)) files.Add((string)t);
            o.Remove("files");
            o.Remove("version");
            unknown = o;
            Trim();
        }

        bool Contains(string p) => files.Exists(f => string.Equals(f, p, StringComparison.OrdinalIgnoreCase));

        void Trim()
        {
            int max = Math.Max(0, Max);
            if (files.Count > max) files.RemoveRange(max, files.Count - max);
        }

        /// <summary>Moves <paramref name="fullPath"/> to the top and writes the file.</summary>
        public void Touch(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return;
            files.RemoveAll(f => string.Equals(f, fullPath, StringComparison.OrdinalIgnoreCase));
            files.Insert(0, fullPath);
            Trim();
            Save();
        }

        public void Remove(string fullPath)
        {
            if (files.RemoveAll(f => string.Equals(f, fullPath, StringComparison.OrdinalIgnoreCase)) > 0) Save();
        }

        public string ToJson()
        {
            var o = new JObject { ["version"] = Version, ["files"] = new JArray(files) };
            foreach (var p in unknown.Properties()) if (o[p.Name] == null) o[p.Name] = p.Value.DeepClone();
            return o.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        public void Save()
        {
            if (Path == null) return;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                string tmp = Path + ".tmp";
                File.WriteAllText(tmp, ToJson());
                if (File.Exists(Path)) File.Replace(tmp, Path, null); else File.Move(tmp, Path);
            }
            catch (Exception e) { Problem = e.Message; }
        }
    }
}
