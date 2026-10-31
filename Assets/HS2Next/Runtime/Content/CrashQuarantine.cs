// The crash quarantine (Phase 5.2): a native crash inside the engine cannot be caught, so the app learns from one. Before the
// first asset load (or scene load) of a zipmod bundle in a session, the catalog writes the bundle's key and its source stamp
// to <UserData>/content/loading.marker; it clears the marker when the scene load returns, and for an asset load at the end
// of the frame, so the first use of what the load returned (an item's or a part's realization) is covered too. A marker that is not clear at the next
// start names the bundle that was loading when the process died: it goes to <UserData>/content/quarantine.tsv (key, stamp,
// time) and from then on every load from that bundle is refused with the finding content.quarantined. A changed stamp (the
// mod was updated) lifts the entry; so does a clear request. Base-game bundles are never quarantined. Unity-free; main thread.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace HS2Next.Content
{
    /// <summary>One quarantined bundle: its cache key, the stamp of its source file when it was quarantined, and when.</summary>
    public sealed class QuarantineEntry
    {
        public string Key, Stamp, Since;
        public override string ToString() => $"{Key} ({Stamp}, since {Since})";
    }

    public sealed class CrashQuarantine : IDisposable
    {
        public const string MarkerName = "loading.marker", ListName = "quarantine.tsv";

        readonly Dictionary<string, QuarantineEntry> entries = new Dictionary<string, QuarantineEntry>(StringComparer.OrdinalIgnoreCase);
        FileStream marker;
        bool armed;

        /// <summary>&lt;UserData&gt;/content.</summary>
        public string Dir { get; }
        public string MarkerFile => Path.Combine(Dir, MarkerName);
        public string ListFile => Path.Combine(Dir, ListName);

        /// <summary>The entry the marker of the previous process added at <see cref="Open"/> (null: it ended normally).</summary>
        public QuarantineEntry FromLastRun { get; private set; }

        /// <summary>Marker writes so far and their total time (the cost of the quarantine), and the median of the last 1,000.</summary>
        public int Writes { get; private set; }
        public double WriteMs { get; private set; }
        readonly List<double> recent = new List<double>();
        public double MedianWriteMs
        {
            get
            {
                if (recent.Count == 0) return 0;
                var l = new List<double>(recent);
                l.Sort();
                return l.Count % 2 == 1 ? l[l.Count / 2] : (l[l.Count / 2 - 1] + l[l.Count / 2]) / 2;
            }
        }

        public IReadOnlyCollection<QuarantineEntry> Entries => entries.Values;

        CrashQuarantine(string dir) { Dir = dir; }

        /// <summary>
        /// Opens the quarantine of a user folder: reads the list, turns a marker the previous process left into an entry
        /// (<see cref="FromLastRun"/>), and keeps the marker file open for this session. Null when the folder cannot be used.
        /// </summary>
        public static CrashQuarantine Open(string userData)
        {
            if (string.IsNullOrEmpty(userData)) return null;
            var q = new CrashQuarantine(Path.Combine(userData, "content"));
            try
            {
                Directory.CreateDirectory(q.Dir);
                q.ReadList();
                if (File.Exists(q.MarkerFile))
                {
                    // the first line; a marker emptied after its load starts with a zero byte
                    string text = File.ReadAllText(q.MarkerFile, Encoding.UTF8);
                    int nl = text.IndexOf('\n');
                    if (nl >= 0) text = text.Substring(0, nl);
                    if (text.Length > 0 && text[0] != '\0')
                    {
                        var p = text.Split('\t');
                        if (p.Length >= 2 && IsZipmodKey(p[0]))
                        {
                            var e = new QuarantineEntry { Key = p[0], Stamp = p[1], Since = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) };
                            q.entries[e.Key] = e;
                            q.FromLastRun = e;
                            q.WriteList();
                        }
                    }
                }
                q.marker = new FileStream(q.MarkerFile, FileMode.Create, FileAccess.Write, FileShare.Read);
                return q;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        /// <summary>A zipmod copy's key ("&lt;zip file&gt;!&lt;path&gt;"); base-game keys ("&lt;source&gt;:&lt;path&gt;") are never quarantined.</summary>
        public static bool IsZipmodKey(string key) => key != null && key.IndexOf('!') > 0;

        /// <summary>True when <paramref name="key"/> is quarantined for this stamp; an entry with another stamp is lifted.</summary>
        public bool IsQuarantined(string key, string stamp)
        {
            if (key == null || !entries.TryGetValue(key, out var e)) return false;
            if (string.Equals(e.Stamp, stamp, StringComparison.Ordinal)) return true;
            entries.Remove(key);
            WriteList();
            return false;
        }

        /// <summary>Marks a load of <paramref name="key"/> in flight: one write of its line at the marker's start (the file is
        /// not truncated; only the first line counts), handed to the system without a flush to disk.</summary>
        public void Enter(string key, string stamp)
        {
            if (marker == null || !IsZipmodKey(key)) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var b = Encoding.UTF8.GetBytes(key + "\t" + stamp + "\n");
                marker.Position = 0;
                marker.Write(b, 0, b.Length);
                marker.Flush();
                armed = true;
            }
            catch (IOException) { }
            double ms = sw.Elapsed.TotalMilliseconds;
            Writes++;
            WriteMs += ms;
            if (recent.Count >= 1000) recent.RemoveAt(0);
            recent.Add(ms);
        }

        static readonly byte[] Empty = { 0 };

        /// <summary>The load returned: the marker's first byte becomes zero (nothing in flight).</summary>
        public void Exit()
        {
            if (marker == null || !armed) return;
            try { marker.Position = 0; marker.Write(Empty, 0, 1); marker.Flush(); }
            catch (IOException) { }
            armed = false;
        }

        /// <summary>True when the marker names a load in flight (tests).</summary>
        public bool Armed => armed;

        /// <summary>Lifts one entry (by its key, or its key without the zipmod's folders); true when one was lifted.</summary>
        public bool Clear(string key)
        {
            if (key == null) return false;
            string hit = null;
            foreach (var k in entries.Keys)
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase) || string.Equals(ShortKey(k), key, StringComparison.OrdinalIgnoreCase)) { hit = k; break; }
            if (hit == null) return false;
            entries.Remove(hit);
            WriteList();
            return true;
        }

        /// <summary>A key without the folders of its zipmod file ("&lt;file name&gt;!&lt;path&gt;").</summary>
        public static string ShortKey(string key)
        {
            int bang = key?.IndexOf('!') ?? -1;
            if (bang < 0) return key;
            return Path.GetFileName(key.Substring(0, bang)) + key.Substring(bang);
        }

        void ReadList()
        {
            if (!File.Exists(ListFile)) return;
            foreach (var l in File.ReadAllLines(ListFile, Encoding.UTF8))
            {
                if (l.Length == 0 || l[0] == '#') continue;
                var p = l.Split('\t');
                if (p.Length < 2 || !IsZipmodKey(p[0])) continue;
                entries[p[0]] = new QuarantineEntry { Key = p[0], Stamp = p[1], Since = p.Length > 2 ? p[2] : "" };
            }
        }

        void WriteList()
        {
            try
            {
                var sb = new StringBuilder("# bundles refused because a process died loading them: key\tsource stamp\tsince\n");
                foreach (var e in entries.Values) sb.Append(e.Key).Append('\t').Append(e.Stamp).Append('\t').Append(e.Since).Append('\n');
                string tmp = ListFile + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(ListFile)) File.Replace(tmp, ListFile, null);
                else File.Move(tmp, ListFile);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>Empties the marker and closes it (a normal end: nothing was loading).</summary>
        public void Dispose()
        {
            Exit();
            try { marker?.Dispose(); } catch (IOException) { }
            marker = null;
        }
    }
}
