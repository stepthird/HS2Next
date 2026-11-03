// The pose browser's files (4,399 HS2 files in 155 folders, depth 4,
// mostly .dat without an image): the HS2 install's `UserData/studio/pose` (read only; .dat and PoseTools .png, each file's
// header: name, sex) and HS2Next's `<UserData>/pose/**/*.hs2npose` (manifest title). v1.2 (owner's answer): every file of
// the list may be deleted, to the recycle bin only; a file of the HS2 install leaves HS2 too.
// The folder walk also keeps each file's creation time (the «最近追加» view). Unity-free.
//
// The scan runs in two steps off the main thread (v0.3 trial 2): List walks the folders (names, sizes and times only, no file
// opened) and takes each file's header from the pose index when the index has the same path, size and time; Read then opens
// the files the index does not know, several at once, and hands each result to the main thread through a queue
// (TakeResults), so the browser shows the list at once and fills names and sexes as they arrive. The index
// (`<cacheDir>/poses/pose_index.tsv`) is written when Read ends and something changed; it is never written into the HS2
// install. A first start opens every file (about 40 s for 4,399 files read one by one on a cold disk cache here, a few seconds
// with eight readers); later starts open none.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HS2Next.Import.Pose;
using HS2Next.Save;

namespace HS2Next.UI.Panels
{
    /// <summary>What reading a pose file found.</summary>
    public enum PoseEntryStatus
    {
        /// <summary>Not read yet (listed from the folder walk; the name is the file name until read).</summary>
        Pending,
        /// <summary>A readable pose.</summary>
        Pose,
        /// <summary>A PNG without a pose payload (a plain image); never listed.</summary>
        NotAPose,
        /// <summary>A file that failed to read; never listed.</summary>
        Error,
    }

    public sealed class PoseEntry
    {
        public string FullPath;
        public string FileName;
        /// <summary>"hs2" or "hs2next".</summary>
        public string Root;
        /// <summary>Folder relative to the root ('/'; "" = the root).</summary>
        public string Folder;
        public string Name;
        /// <summary>0 male, 1 female, -1 unknown.</summary>
        public int Sex = -1;
        public bool IsHs2n;
        /// <summary>The file starts with a PNG image (PoseTools .png, .hs2npose thumbnails).</summary>
        public bool HasImage;
        public long Size;
        public DateTime ModifiedUtc;
        /// <summary>The file's creation time: the date it was placed in the folder («最近追加», newest first).</summary>
        public DateTime CreatedUtc;
        public PoseEntryStatus Status = PoseEntryStatus.Pending;

        public string DisplayName => string.IsNullOrEmpty(Name) ? FileName : Name;
        public string SearchText => FileName + " " + (Name ?? "");
        public string FolderKey => Folder.Length == 0 ? Root : Root + "/" + Folder;
        /// <summary>Every listed file may go to the recycle bin (v1.2; before, HS2 files were read only).</summary>
        public bool CanDelete => true;
        /// <summary>The file went to the recycle bin (no longer listed).</summary>
        public bool Removed;
        /// <summary>Shown in the browser: a pose, or a file not read yet that is not a PNG (a PNG may be a plain image).</summary>
        public bool Listed => !Removed && (Status == PoseEntryStatus.Pose || Status == PoseEntryStatus.Pending && (IsHs2n || !HasImage));
    }

    /// <summary>The header of one pose file as read (or as the index remembers it).</summary>
    public struct PoseHeader
    {
        public PoseEntryStatus Status;
        public string Name;
        public int Sex;
        public bool HasImage;
        public string Problem;
    }

    public sealed class PoseFiles
    {
        public const string RootHs2 = "hs2", RootHs2Next = "hs2next";
        /// <summary>Files read at once by <see cref="Read"/> (opening a file the disk cache does not hold costs about 7 ms here,
        /// mostly waiting; eight readers took 1.1 ms per file).</summary>
        public const int Readers = 8;

        /// <summary>Every candidate file in folder order (hs2 then hs2next, ordinal by relative path), listed or not.</summary>
        public readonly List<PoseEntry> Entries = new List<PoseEntry>();
        public readonly List<string> Folders = new List<string>();
        public readonly List<(string id, string dir)> Roots = new List<(string, string)>();
        public readonly List<string> Problems = new List<string>();
        public int NotAPose, Errors;
        /// <summary>The folder walk with the index lookup (List).</summary>
        public double ListMs;
        /// <summary>List + Read (the time until every header was known).</summary>
        public double ScanMs;
        /// <summary>Headers taken from the index / read from the files.</summary>
        public int IndexHits, FilesRead;
        /// <summary>The index file used (null = none).</summary>
        public string IndexFile;
        /// <summary>True when Read wrote the index.</summary>
        public bool IndexWritten;

        readonly List<int> pending = new List<int>();
        /// <summary>Index rows of folders this scan did not walk (another install, a test's folder): written back unchanged.</summary>
        readonly List<PoseIndex.Row> otherFolders = new List<PoseIndex.Row>();
        readonly ConcurrentQueue<(int index, PoseHeader head)> results = new ConcurrentQueue<(int, PoseHeader)>();
        int unread;
        volatile bool readDone;
        Stopwatch clock;

        /// <summary>Entries whose header is not known yet.</summary>
        public int Unread => Volatile.Read(ref unread);
        /// <summary>True when <see cref="Read"/> has finished (every result queued, the index written when needed).</summary>
        public bool ReadDone => readDone;

        /// <summary>The index file under a cache folder (null without one).</summary>
        public static string IndexPath(string cacheDir) => string.IsNullOrEmpty(cacheDir) ? null : Path.Combine(cacheDir, "poses", "pose_index.tsv");

        /// <summary>List and Read in one call (tests, tools): every header known on return.</summary>
        public static PoseFiles Scan(string hs2PoseDir, string hs2nPoseDir, string indexFile = null)
        {
            var pf = List(hs2PoseDir, hs2nPoseDir, indexFile);
            pf.Read();
            while (pf.results.TryDequeue(out var r)) pf.Apply(r.index, r.head);
            pf.Recount();
            return pf;
        }

        /// <summary>Walks the folders (no file is opened) and takes each file's header from the index when it has the same
        /// path, size and modification time.</summary>
        public static PoseFiles List(string hs2PoseDir, string hs2nPoseDir, string indexFile)
        {
            var pf = new PoseFiles { clock = Stopwatch.StartNew(), IndexFile = indexFile };
            var index = PoseIndex.Load(indexFile, pf.Problems);
            if (!string.IsNullOrEmpty(hs2PoseDir) && Directory.Exists(hs2PoseDir))
            {
                string root = Path.GetFullPath(hs2PoseDir).TrimEnd('\\', '/');
                pf.Roots.Add((RootHs2, root));
                var files = new List<FileInfo>();
                Walk(root, root, files, pf.Problems, f =>
                    string.Equals(f.Extension, ".dat", StringComparison.OrdinalIgnoreCase) || string.Equals(f.Extension, ".png", StringComparison.OrdinalIgnoreCase));
                pf.AddFiles(files, root, RootHs2, false, index);
            }
            if (!string.IsNullOrEmpty(hs2nPoseDir) && Directory.Exists(hs2nPoseDir))
            {
                string root = Path.GetFullPath(hs2nPoseDir).TrimEnd('\\', '/');
                pf.Roots.Add((RootHs2Next, root));
                var files = new List<FileInfo>();
                Walk(root, root, files, pf.Problems, f => string.Equals(f.Extension, SaveFormats.PoseExtension, StringComparison.OrdinalIgnoreCase));
                pf.AddFiles(files, root, RootHs2Next, true, index);
            }
            // rows of the walked folders that are gone are dropped; rows of other folders are kept for the scans that walk them
            int stale = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in pf.Entries) seen.Add(e.FullPath);
            foreach (var row in index.Values)
            {
                if (seen.Contains(row.Path)) continue;
                if (pf.UnderRoot(row.Path)) stale++;
                else pf.otherFolders.Add(row);
            }
            pf.unread = pf.pending.Count;
            pf.Recount();
            pf.ListMs = pf.clock.Elapsed.TotalMilliseconds;
            if (pf.pending.Count == 0)
            {
                pf.ScanMs = pf.ListMs;
                pf.readDone = true;
                // the index lists a file of these folders that is no longer there: write it again without it
                if (stale > 0) pf.WriteIndex();
            }
            return pf;
        }

        bool UnderRoot(string path)
        {
            foreach (var r in Roots)
                if (path.Length > r.dir.Length && path.StartsWith(r.dir, StringComparison.OrdinalIgnoreCase) && (path[r.dir.Length] == '\\' || path[r.dir.Length] == '/')) return true;
            return false;
        }

        void AddFiles(List<FileInfo> files, string root, string rootId, bool hs2n, Dictionary<string, PoseIndex.Row> index)
        {
            var rel = new List<KeyValuePair<string, FileInfo>>(files.Count);
            foreach (var f in files) rel.Add(new KeyValuePair<string, FileInfo>(f.FullName.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/'), f));
            rel.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            foreach (var kv in rel)
            {
                var f = kv.Value;
                int slash = kv.Key.LastIndexOf('/');
                var e = new PoseEntry
                {
                    FullPath = f.FullName, FileName = Path.GetFileNameWithoutExtension(f.Name), Root = rootId, IsHs2n = hs2n,
                    Folder = slash < 0 ? "" : kv.Key.Substring(0, slash),
                    HasImage = !hs2n && string.Equals(f.Extension, ".png", StringComparison.OrdinalIgnoreCase),
                };
                try { e.Size = f.Length; e.ModifiedUtc = f.LastWriteTimeUtc; e.CreatedUtc = f.CreationTimeUtc; }
                catch (Exception x) { Problems.Add(e.FileName + ": " + x.Message); }
                Entries.Add(e);
                if (index.TryGetValue(e.FullPath, out var row) && row.Size == e.Size && row.Ticks == e.ModifiedUtc.Ticks)
                {
                    Apply(Entries.Count - 1, row.Head);
                    IndexHits++;
                }
                else pending.Add(Entries.Count - 1);
            }
        }

        static void Walk(string root, string dir, List<FileInfo> files, List<string> problems, Func<FileInfo, bool> want)
        {
            try
            {
                var di = new DirectoryInfo(dir);
                foreach (var f in di.EnumerateFiles()) if (want(f)) files.Add(f);
                foreach (var d in di.EnumerateDirectories())
                {
                    if ((d.Attributes & FileAttributes.ReparsePoint) != 0) { problems.Add("skipped a linked directory: " + Rel(root, d.FullName)); continue; }
                    Walk(root, d.FullName, files, problems, want);
                }
            }
            catch (Exception x) when (x is IOException || x is UnauthorizedAccessException || x is System.Security.SecurityException)
            {
                problems.Add("cannot list " + (dir.Length > root.Length ? Rel(root, dir) : "the root") + ": " + x.GetType().Name);
            }
        }

        static string Rel(string root, string path) => path.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');

        /// <summary>
        /// Opens the files the index did not know (<see cref="Readers"/> at once) and queues each header for
        /// <see cref="TakeResults"/>; writes the index at the end when anything was read. Runs on the calling thread (a worker).
        /// </summary>
        public void Read(CancellationToken cancel = default)
        {
            try
            {
                if (pending.Count > 0)
                {
                    var heads = new PoseHeader[pending.Count];
                    Parallel.For(0, pending.Count, new ParallelOptions { MaxDegreeOfParallelism = Readers, CancellationToken = cancel }, i =>
                    {
                        var e = Entries[pending[i]];
                        heads[i] = ReadHeader(e.FullPath, e.IsHs2n);
                        results.Enqueue((pending[i], heads[i]));
                        Interlocked.Decrement(ref unread);
                    });
                    FilesRead = pending.Count;
                    ScanMs = clock.Elapsed.TotalMilliseconds;
                    // the index: every header known now (hits as listed, the rest as read)
                    var rows = new List<PoseIndex.Row>(Entries.Count);
                    var readAt = new Dictionary<int, PoseHeader>(pending.Count);
                    for (int i = 0; i < pending.Count; i++) readAt[pending[i]] = heads[i];
                    for (int i = 0; i < Entries.Count; i++)
                    {
                        var e = Entries[i];
                        var h = readAt.TryGetValue(i, out var rh) ? rh : new PoseHeader { Status = e.Status, Name = e.Name, Sex = e.Sex, HasImage = e.HasImage };
                        if (h.Status == PoseEntryStatus.Pending || h.Status == PoseEntryStatus.Error && h.Problem != null && h.Problem.StartsWith("io:", StringComparison.Ordinal)) continue;   // read again next time
                        rows.Add(new PoseIndex.Row { Path = e.FullPath, Size = e.Size, Ticks = e.ModifiedUtc.Ticks, Head = h });
                    }
                    WriteIndex(rows);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception x) { lock (Problems) Problems.Add("pose scan: " + x.GetType().Name + ": " + x.Message); }
            finally { readDone = true; }
        }

        void WriteIndex(List<PoseIndex.Row> rows = null)
        {
            if (IndexFile == null) return;
            if (rows == null)
            {
                rows = new List<PoseIndex.Row>(Entries.Count);
                foreach (var e in Entries)
                    if (e.Status != PoseEntryStatus.Pending)
                        rows.Add(new PoseIndex.Row { Path = e.FullPath, Size = e.Size, Ticks = e.ModifiedUtc.Ticks, Head = new PoseHeader { Status = e.Status, Name = e.Name, Sex = e.Sex, HasImage = e.HasImage } });
            }
            rows.AddRange(otherFolders);
            string err = PoseIndex.Save(IndexFile, rows);
            if (err == null) IndexWritten = true;
            else lock (Problems) Problems.Add("pose index: " + err);
        }

        /// <summary>Reads one file's header (never throws).</summary>
        public static PoseHeader ReadHeader(string path, bool hs2n)
        {
            if (hs2n)
            {
                try
                {
                    var r = ContainerReader.Read(path, null, n => n == EntryNames.Manifest);
                    var m = r.ReadManifest();
                    return new PoseHeader { Status = PoseEntryStatus.Pose, Name = m.Title, Sex = m.Sex ?? -1, HasImage = r.Thumbnail != null };
                }
                catch (Exception x) { return new PoseHeader { Status = PoseEntryStatus.Error, Sex = -1, Problem = Path.GetFileNameWithoutExtension(path) + ": " + x.Message }; }
            }
            bool png = string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase);
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception x) { return new PoseHeader { Status = PoseEntryStatus.Error, Sex = -1, HasImage = png, Problem = "io: " + x.GetType().Name }; }
            try
            {
                var pose = PoseFile.Read(bytes);
                return new PoseHeader { Status = PoseEntryStatus.Pose, Name = pose.Name, Sex = pose.Sex, HasImage = pose.Kind == PoseContainerKind.Png };
            }
            catch (PoseFormatException x)
            {
                if (png && x.Code == PoseErrorCode.NoPayload) return new PoseHeader { Status = PoseEntryStatus.NotAPose, Sex = -1, HasImage = true };
                return new PoseHeader { Status = PoseEntryStatus.Error, Sex = -1, HasImage = png, Problem = x.Message };
            }
            catch (Exception x) { return new PoseHeader { Status = PoseEntryStatus.Error, Sex = -1, HasImage = png, Problem = x.GetType().Name + ": " + x.Message }; }
        }

        void Apply(int i, PoseHeader h)
        {
            var e = Entries[i];
            e.Status = h.Status;
            e.Name = h.Name;
            e.Sex = h.Sex;
            e.HasImage = h.HasImage;
            if (h.Status == PoseEntryStatus.NotAPose) NotAPose++;
            else if (h.Status == PoseEntryStatus.Error) { Errors++; if (h.Problem != null) lock (Problems) Problems.Add(h.Problem); }
        }

        /// <summary>
        /// Main thread: applies the headers read since the last call to their entries (at most <paramref name="max"/>) and
        /// returns their positions in <see cref="Entries"/> (for ListIndex.RowsChanged).
        /// </summary>
        public List<int> TakeResults(int max = int.MaxValue)
        {
            var changed = new List<int>();
            while (changed.Count < max && results.TryDequeue(out var r))
            {
                Apply(r.index, r.head);
                changed.Add(r.index);
            }
            return changed;
        }

        /// <summary>True when results wait for <see cref="TakeResults"/>.</summary>
        public bool HasResults => !results.IsEmpty;

        /// <summary>Main thread: an entry whose file went away (a delete) is no longer listed (it stays in <see cref="Entries"/>, so
        /// the positions the reader hands out keep their meaning) and the folders are recounted; false when it was not listed.
        /// The pose index forgets the file at the next scan.</summary>
        public bool Remove(PoseEntry e)
        {
            if (e == null || e.Removed || !Entries.Contains(e)) return false;
            e.Removed = true;
            Recount();
            return true;
        }

        /// <summary>The folder list from the listed entries (roots first, then "root/a/b" keys).</summary>
        public void Recount()
        {
            Folders.Clear();
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var e in Entries)
            {
                if (!e.Listed) continue;
                string k = e.FolderKey;
                while (true)
                {
                    set.Add(k);
                    int i = k.LastIndexOf('/');
                    if (i < 0) break;
                    k = k.Substring(0, i);
                }
            }
            foreach (var r in Roots) Folders.Add(r.id);
            foreach (var f in set) if (f.IndexOf('/') >= 0) Folders.Add(f);
        }

        /// <summary>The folder keys of the listed entries (FolderTree counts).</summary>
        public IEnumerable<string> ListedFolderKeys()
        {
            foreach (var e in Entries) if (e.Listed) yield return e.FolderKey;
        }

        /// <summary>The number of listed entries.</summary>
        public int ListedCount
        {
            get { int n = 0; foreach (var e in Entries) if (e.Listed) n++; return n; }
        }

        /// <summary>True when the entry lies in the folder or below (null = all).</summary>
        public static bool InFolder(PoseEntry e, string folderKey)
        {
            if (string.IsNullOrEmpty(folderKey)) return true;
            string k = e.FolderKey;
            return k.Length == folderKey.Length ? string.Equals(k, folderKey, StringComparison.Ordinal)
                : k.Length > folderKey.Length && k[folderKey.Length] == '/' && k.StartsWith(folderKey, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The pose index file: one line per file, `path \t size \t modified ticks (UTC) \t status \t sex \t image \t name`, under a
    /// first line `#hs2next-pose-index 1`; tabs, line breaks and backslashes in names are escaped. Unreadable lines are
    /// skipped; a file of another version is ignored (read again).
    /// </summary>
    public static class PoseIndex
    {
        public const string Header = "#hs2next-pose-index\t1";

        public struct Row
        {
            public string Path;
            public long Size, Ticks;
            public PoseHeader Head;
        }

        public static Dictionary<string, Row> Load(string file, List<string> problems = null)
        {
            var d = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return d;
            try
            {
                using (var rd = new StreamReader(file, new UTF8Encoding(false)))
                {
                    if (rd.ReadLine() != Header) return d;
                    string line;
                    while ((line = rd.ReadLine()) != null)
                    {
                        var f = line.Split('\t');
                        if (f.Length != 7 || !long.TryParse(f[1], out long size) || !long.TryParse(f[2], out long ticks) || !int.TryParse(f[4], out int sex)) continue;
                        PoseEntryStatus st;
                        switch (f[3]) { case "P": st = PoseEntryStatus.Pose; break; case "N": st = PoseEntryStatus.NotAPose; break; case "E": st = PoseEntryStatus.Error; break; default: continue; }
                        d[f[0]] = new Row { Path = f[0], Size = size, Ticks = ticks, Head = new PoseHeader { Status = st, Sex = sex, HasImage = f[5] == "1", Name = f[6].Length == 0 ? null : Unescape(f[6]) } };
                    }
                }
            }
            catch (Exception x) { problems?.Add("pose index unreadable: " + x.Message); d.Clear(); }
            return d;
        }

        /// <summary>Writes the index through a temporary file and a replace (another process may read the old one meanwhile);
        /// returns null or the error.</summary>
        public static string Save(string file, IEnumerable<Row> rows)
        {
            string tmp = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                var sb = new StringBuilder(1 << 16);
                sb.Append(Header).Append('\n');
                foreach (var r in rows)
                {
                    char st = r.Head.Status == PoseEntryStatus.Pose ? 'P' : r.Head.Status == PoseEntryStatus.NotAPose ? 'N' : 'E';
                    sb.Append(r.Path).Append('\t').Append(r.Size).Append('\t').Append(r.Ticks).Append('\t').Append(st).Append('\t')
                      .Append(r.Head.Sex).Append('\t').Append(r.Head.HasImage ? '1' : '0').Append('\t').Append(Escape(r.Head.Name)).Append('\n');
                }
                tmp = file + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(file)) File.Replace(tmp, file, null, true);
                else File.Move(tmp, file);
                tmp = null;
                return null;
            }
            catch (Exception x) { return x.GetType().Name + ": " + x.Message; }
            finally { if (tmp != null) try { File.Delete(tmp); } catch (Exception) { } }
        }

        static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOfAny(new[] { '\\', '\t', '\n', '\r' }) < 0) return s;
            return s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char n = s[++i];
                sb.Append(n == 't' ? '\t' : n == 'n' ? '\n' : n == 'r' ? '\r' : n);
            }
            return sb.ToString();
        }
    }
}
