// The store of scene scripts in the user's data (Unity-free): `<UserData>/scripts/` with `index.json` (the rows) and one `<no>.hs2nscript` per row
// (the script's document, without a scene's cast, preparation mark and store number). Nothing exists until the first script is kept. Writing puts
// the script's file first (a temporary name, then a replace) and the index after it (a temporary file, then a replace), so a stop between the two
// leaves only a file no row lists. Row numbers only grow and are never given again, also after a delete or a purge. A delete hides a row and
// touches no file; a purge removes exactly the files the hidden rows of a readable index name, then drops those rows: a file no row names, a
// leftover temporary file and every other file stay. A missing or unreadable index (script files present) is an empty store: the file is left as
// it is until the next write, which first copies it to `index.json.bad`, and a purge removes nothing until then (finding script.storeUnreadable). A scene never depends on the store: a script added to a scene is copied into the scene. One app writes the
// store; every call is serialized on the store object.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HS2Next.Save;

namespace HS2Next.Scenes.Scripts
{
    /// <summary>A store operation that did not happen: its <see cref="Code"/> is <see cref="ScriptFindings.StoreWrite"/>,
    /// <see cref="ScriptFindings.StoreUnreadable"/> or <see cref="ScriptFindings.Limit"/>.</summary>
    public sealed class ScriptStoreException : Exception
    {
        public readonly string Code;
        public ScriptStoreException(string code, string detail, Exception inner = null) : base(detail, inner) { Code = code; }
    }

    /// <summary>`rows[]` of index.json: one script of the store.</summary>
    public sealed class ScriptStoreRow : DocObject
    {
        /// <summary>The row's number (never given again).</summary>
        public int No;
        /// <summary>The script's id.</summary>
        public string Id;
        public string Name = "";
        /// <summary>The file name in the store folder, "&lt;no&gt;.hs2nscript".</summary>
        public string File;
        public DateTime Created = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public DateTime Modified = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public bool Deleted;

        internal override void ReadMembers(DocReader r)
        {
            No = r.Int("no");
            Id = r.Id("id");
            Name = r.Str("name", Name);
            var fv = r.ValueReq("file");
            File = fv.AsStr();
            if (!ScriptStore.IsFileName(File)) throw fv.Error("a file name \"<no>" + SceneScript.FileExtension + "\"");
            Created = r.Time("created", Created);
            Modified = r.Time("modified", Modified);
            Deleted = r.Bool("deleted", Deleted);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Int("no", No);
            ReqId(w, "id", Id);
            ReqStr(w, "name", Name);
            if (!ScriptStore.IsFileName(File)) throw new SaveFormatException(SaveErrorCode.BadDocument, "not a script file name: \"" + File + "\"", w.Entry, DocReader.Join(w.Path, "file"));
            w.Str("file", File);
            w.Time("created", Created);
            w.Time("modified", Modified);
            w.Bool("deleted", Deleted);
        }

        /// <summary>A deep copy.</summary>
        public ScriptStoreRow Clone()
        {
            var bytes = CanonicalJsonWriter.ToDocumentBytes(ScriptStore.IndexName, WriteTo);
            var c = new ScriptStoreRow();
            c.ReadFrom(new DocReader(DocReader.Parse(bytes, ScriptStore.IndexName), new DocReadContext(ScriptStore.IndexName)));
            return c;
        }
    }

    /// <summary>index.json.</summary>
    public sealed class ScriptStoreIndex : DocObject
    {
        public const string FormatName = "hs2next.scriptStore";
        public const int FormatVersion = 1;

        public string Format = FormatName;
        public int Version = FormatVersion;
        public int LastNumber;
        public List<ScriptStoreRow> Rows = new List<ScriptStoreRow>();

        internal override void ReadMembers(DocReader r)
        {
            Format = r.Str("format");
            if (Format != FormatName) throw new DocTypeException(r.Context, r.Object, r.Path, "format \"" + Format + "\" is not " + FormatName);
            Version = r.Int("version");
            if (Version > FormatVersion || Version < 1) throw new DocTypeException(r.Context, r.Object, r.Path, "store version " + Version + " is not " + FormatVersion);
            LastNumber = r.Int("lastNumber");
            Rows = ReadList<ScriptStoreRow>(r, "rows");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "format", Format);
            w.Int("version", Version);
            w.Int("lastNumber", LastNumber);
            WriteList(w, "rows", Rows);
        }

        internal byte[] ToBytes() => CanonicalJsonWriter.ToDocumentBytes(ScriptStore.IndexName, WriteTo);

        internal static ScriptStoreIndex Parse(byte[] bytes)
        {
            var x = new ScriptStoreIndex();
            x.ReadFrom(new DocReader(DocReader.Parse(bytes, ScriptStore.IndexName), new DocReadContext(ScriptStore.IndexName)));
            return x;
        }
    }

    /// <summary>What a purge removed.</summary>
    public sealed class ScriptPurgeResult
    {
        /// <summary>Rows removed from the index (they were hidden).</summary>
        public int Rows;
        /// <summary>Script files removed (the files the hidden rows name), and their bytes.</summary>
        public int Files;
        public long Bytes;
        /// <summary>True when the store's index could not be read: nothing was removed (finding script.storeUnreadable).</summary>
        public bool Unreadable;
    }

    /// <summary>The script store of one UserData folder.</summary>
    public sealed class ScriptStore
    {
        public const string FolderName = "scripts";
        public const string IndexName = "index.json";
        public const string BadName = "index.json.bad";
        /// <summary>The first row number of a new store.</summary>
        public const int FirstNumber = 1;

        const string TempMark = ".tmp-";

        readonly object gate = new object();
        ScriptStoreIndex index = new ScriptStoreIndex { LastNumber = FirstNumber - 1 };
        bool unreadable;

        /// <summary>The store folder.</summary>
        public string Folder { get; }
        public string IndexPath => Path.Combine(Folder, IndexName);

        /// <summary>The clock rows are stamped with (UTC; a test seam).</summary>
        public Func<DateTime> Clock = () => DateTime.UtcNow;
        /// <summary>The id a copy gets when an import meets a row of its id without replace (a test seam).</summary>
        public Func<string> NewId = JsonText.NewId;
        /// <summary>A test seam: runs after a script's file is written and before the index is (a throw there leaves the file and the old index).</summary>
        public Action BeforeIndexWrite;

        ScriptStore(string folder)
        {
            Folder = Path.GetFullPath(folder ?? throw new ArgumentNullException(nameof(folder)));
        }

        /// <summary>Opens the store of folder <paramref name="folder"/> (nothing is created): reads its index; findings (script.storeUnreadable) go to
        /// <paramref name="findings"/>.</summary>
        public static ScriptStore Open(string folder, FindingList findings = null)
        {
            var s = new ScriptStore(folder);
            s.Reload(findings);
            return s;
        }

        /// <summary>The store of a UserData folder: <c>&lt;userData&gt;/scripts</c>.</summary>
        public static ScriptStore ForUserData(string userData, FindingList findings = null) => Open(Path.Combine(userData, FolderName), findings);

        /// <summary>"&lt;no&gt;.hs2nscript" of row <paramref name="no"/>.</summary>
        public static string FileNameOf(int no) => no.ToString(CultureInfo.InvariantCulture) + SceneScript.FileExtension;

        /// <summary>True for "&lt;positive integer&gt;.hs2nscript" written as <see cref="FileNameOf"/> writes it.</summary>
        public static bool IsFileName(string s) => NumberOf(s) > 0;

        /// <summary>The number of a script file name, or 0.</summary>
        public static int NumberOf(string fileName)
        {
            if (fileName == null || !fileName.EndsWith(SceneScript.FileExtension, StringComparison.Ordinal)) return 0;
            string n = fileName.Substring(0, fileName.Length - SceneScript.FileExtension.Length);
            if (n.Length == 0 || n.Length > 10 || n[0] == '0') return 0;
            foreach (char c in n) if (c < '0' || c > '9') return 0;
            return int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out int v) && FileNameOf(v) == fileName ? v : 0;
        }

        /// <summary>Reads index.json again. A missing folder is an empty store with no finding; a folder without a readable index is an empty store,
        /// <see cref="Unreadable"/>, with finding script.storeUnreadable.</summary>
        public void Reload(FindingList findings = null)
        {
            lock (gate)
            {
                index = new ScriptStoreIndex { LastNumber = FirstNumber - 1 };
                unreadable = false;
                if (!Directory.Exists(Folder)) return;
                string why;
                if (!System.IO.File.Exists(IndexPath))
                {
                    // A folder that holds no script file and no index is a store that was never written.
                    if (ScriptFiles().Count == 0) return;
                    why = "the store has no " + IndexName;
                }
                else
                {
                    try
                    {
                        var read = ScriptStoreIndex.Parse(System.IO.File.ReadAllBytes(IndexPath));
                        foreach (var r in read.Rows) read.LastNumber = Math.Max(read.LastNumber, r.No);
                        read.LastNumber = Math.Max(read.LastNumber, FirstNumber - 1);
                        index = read;
                        return;
                    }
                    catch (SaveFormatException e) { why = IndexName + " does not read: " + e.Message; }
                    catch (IOException e) { why = IndexName + " cannot be read: " + e.Message; }
                    catch (UnauthorizedAccessException e) { why = IndexName + " cannot be read: " + e.Message; }
                }
                unreadable = true;
                findings?.Warning(ScriptFindings.StoreUnreadable, IndexPath, why + "; the store reads as empty, the file is kept and copied to " + BadName + " before the next write");
            }
        }

        /// <summary>True while the store's index could not be read and nothing has been written since.</summary>
        public bool Unreadable { get { lock (gate) return unreadable; } }

        /// <summary>The highest number given so far (<see cref="FirstNumber"/> - 1 for a new store).</summary>
        public int LastNumber { get { lock (gate) return index.LastNumber; } }

        /// <summary>Copies of every row, hidden ones included, in index order.</summary>
        public List<ScriptStoreRow> AllRows()
        {
            lock (gate) return index.Rows.ConvertAll(r => r.Clone());
        }

        /// <summary>Copies of the rows not hidden, in index order.</summary>
        public List<ScriptStoreRow> LiveRows()
        {
            lock (gate) return index.Rows.FindAll(r => !r.Deleted).ConvertAll(r => r.Clone());
        }

        /// <summary>The number of rows, hidden ones included.</summary>
        public int Count { get { lock (gate) return index.Rows.Count; } }

        /// <summary>A copy of row <paramref name="no"/>, or null.</summary>
        public ScriptStoreRow Find(int no)
        {
            lock (gate) return index.Rows.Find(r => r.No == no)?.Clone();
        }

        /// <summary>A copy of the row of script <paramref name="id"/> (one not hidden first), or null.</summary>
        public ScriptStoreRow FindById(string id)
        {
            lock (gate) return (index.Rows.Find(r => r.Id == id && !r.Deleted) ?? index.Rows.Find(r => r.Id == id))?.Clone();
        }

        /// <summary>The path of row <paramref name="no"/>'s file.</summary>
        public string PathOf(int no) => Path.Combine(Folder, FileNameOf(no));

        /// <summary>
        /// The script of row <paramref name="no"/> (hidden rows included), read with the reader's rules (findings go to <paramref name="findings"/>); null
        /// when there is no such row. Throws <see cref="SaveFormatException"/> when the file does not read, <see cref="ScriptRefusedException"/> when it is
        /// of another format or a newer version, <see cref="ScriptStoreException"/> (script.storeWrite) when it cannot be opened.
        /// </summary>
        public SceneScript Read(int no, FindingList findings = null)
        {
            ScriptStoreRow row;
            lock (gate) row = index.Rows.Find(r => r.No == no);
            if (row == null) return null;
            byte[] b;
            string p = Path.Combine(Folder, row.File);
            try { b = System.IO.File.ReadAllBytes(p); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                throw new ScriptStoreException(ScriptFindings.StoreWrite, "the store's file " + row.File + " cannot be read: " + e.Message, e);
            }
            return SceneScript.ParseDocument(b, findings, row.File);
        }

        // ---------- writing ----------

        static ScriptStoreException WriteError(string what, Exception e) => new ScriptStoreException(ScriptFindings.StoreWrite, what + ": " + e.Message, e);

        static void ReplaceFile(string final, byte[] bytes)
        {
            string temp = final + TempMark + Guid.NewGuid().ToString("N");
            System.IO.File.WriteAllBytes(temp, bytes);
            if (System.IO.File.Exists(final))
            {
                try { System.IO.File.Replace(temp, final, null); }
                catch (PlatformNotSupportedException)
                {
                    System.IO.File.Delete(final);
                    System.IO.File.Move(temp, final);
                }
            }
            else System.IO.File.Move(temp, final);
        }

        /// <summary>Writes <paramref name="bytes"/> to <paramref name="path"/> through a temporary file and a replace (an export).</summary>
        public static void WriteFileAtomic(string path, byte[] bytes)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            ReplaceFile(Path.GetFullPath(path), bytes ?? Array.Empty<byte>());
        }

        void WriteIndex()
        {
            Directory.CreateDirectory(Folder);
            var bytes = index.ToBytes();
            if (unreadable && System.IO.File.Exists(IndexPath)) System.IO.File.Copy(IndexPath, Path.Combine(Folder, BadName), true);
            ReplaceFile(IndexPath, bytes);
            unreadable = false;
        }

        // Runs a write under the lock and the store's own error rule: a file error is script.storeWrite, and the index in memory is what it was before.
        T Write<T>(string what, Func<T> body)
        {
            lock (gate)
            {
                var before = index.ToBytes();
                bool wasUnreadable = unreadable;
                try { return body(); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException)
                {
                    index = ScriptStoreIndex.Parse(before);
                    unreadable = wasUnreadable;
                    throw WriteError(what, e);
                }
                catch
                {
                    index = ScriptStoreIndex.Parse(before);
                    unreadable = wasUnreadable;
                    throw;
                }
            }
        }

        static byte[] DocumentBytes(SceneScript script)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));
            if (script.IsOpaque) throw new ArgumentException("a script of another format or a newer version is not kept in the store");
            string p = script.Problem();
            if (p != null) throw new ArgumentException("the script cannot be kept: " + p);
            return script.ToDocument().ToDocumentBytes();
        }

        /// <summary>
        /// Adds a row for <paramref name="script"/> (its document: the scene members are not kept) under the next number. Returns a copy of the stored row.
        /// Throws <see cref="ScriptStoreException"/>: script.limit when the store holds <see cref="ScriptLimits.MaxStoreRows"/> rows, script.storeWrite
        /// when a file cannot be written (nothing of the row is in the index then); <see cref="ArgumentException"/> when the script breaks a rule.
        /// </summary>
        public ScriptStoreRow Add(SceneScript script)
        {
            var bytes = DocumentBytes(script);
            return Write("keeping a script in the store", () =>
            {
                if (index.Rows.Count >= ScriptLimits.MaxStoreRows)
                    throw new ScriptStoreException(ScriptFindings.Limit, ScriptFindings.Limit + ": the store holds at most " + ScriptLimits.MaxStoreRows + " scripts");
                int n = index.LastNumber + 1;
                // A file a stopped write left behind keeps its number out of use.
                while (System.IO.File.Exists(PathOf(n))) n++;
                Directory.CreateDirectory(Folder);
                ReplaceFile(PathOf(n), bytes);
                BeforeIndexWrite?.Invoke();
                var now = JsonText.TruncateToSeconds(Clock());
                var row = new ScriptStoreRow { No = n, Id = script.Id, Name = script.Name ?? "", File = FileNameOf(n), Created = now, Modified = now };
                index.Rows.Add(row);
                index.LastNumber = Math.Max(index.LastNumber, n);
                WriteIndex();
                return row.Clone();
            });
        }

        /// <summary>
        /// Replaces the script of row <paramref name="no"/> with <paramref name="script"/> (same number; the row's id and name follow the script, its
        /// modified time is now, a hidden row is shown again). Returns a copy of the row, or null when there is no such row. Throws as <see cref="Add"/>.
        /// </summary>
        public ScriptStoreRow Replace(int no, SceneScript script)
        {
            var bytes = DocumentBytes(script);
            return Write("replacing a script of the store", () =>
            {
                var row = index.Rows.Find(r => r.No == no);
                if (row == null) return null;
                ReplaceFile(Path.Combine(Folder, row.File), bytes);
                BeforeIndexWrite?.Invoke();
                row.Id = script.Id;
                row.Name = script.Name ?? "";
                row.Modified = JsonText.TruncateToSeconds(Clock());
                row.Deleted = false;
                WriteIndex();
                return row.Clone();
            });
        }

        /// <summary>
        /// Imports <paramref name="script"/> (a document read from a file) into a row: when a row holds its id, that row is replaced with
        /// <paramref name="replace"/> (same number), else the copy gets a new id and the next number; without such a row it gets the next number.
        /// Returns a copy of the stored row; <paramref name="stored"/> is the script as stored (its id may be new). Throws as <see cref="Add"/>.
        /// </summary>
        public ScriptStoreRow Import(SceneScript script, bool replace, out SceneScript stored)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));
            var existing = FindById(script.Id);
            if (existing != null && replace)
            {
                stored = script.ToDocument();
                return Replace(existing.No, stored);
            }
            stored = script.ToDocument();
            if (existing != null) stored.Id = NewId();
            return Add(stored);
        }

        /// <summary>Hides row <paramref name="no"/>, or shows it again with <paramref name="restore"/>; no file is touched. False when there is no such row.</summary>
        public bool Delete(int no, bool restore = false)
        {
            return Write("hiding a script of the store", () =>
            {
                var r = index.Rows.Find(x => x.No == no);
                if (r == null) return false;
                if (r.Deleted == !restore) return true;
                r.Deleted = !restore;
                WriteIndex();
                return true;
            });
        }

        /// <summary>What a purge would remove now (nothing is removed); with an unreadable index nothing, <see cref="ScriptPurgeResult.Unreadable"/> set.</summary>
        public ScriptPurgeResult PurgePreview()
        {
            lock (gate)
            {
                if (unreadable) return new ScriptPurgeResult { Unreadable = true };
                var res = new ScriptPurgeResult { Rows = index.Rows.FindAll(r => r.Deleted).Count };
                foreach (var f in PurgeFiles())
                {
                    res.Files++;
                    res.Bytes += new FileInfo(f).Length;
                }
                return res;
            }
        }

        // The files a purge removes: exactly the files the hidden rows of the index name (those on disk that no live row names too). Decided from the
        // index alone; a file the index does not name is never removed.
        List<string> PurgeFiles()
        {
            var live = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in index.Rows) if (!r.Deleted) live.Add(r.File);
            var l = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in index.Rows)
            {
                if (!r.Deleted || !IsFileName(r.File) || live.Contains(r.File) || !seen.Add(r.File)) continue;
                string f = Path.Combine(Folder, r.File);
                if (System.IO.File.Exists(f)) l.Add(f);
            }
            l.Sort(string.CompareOrdinal);
            return l;
        }

        // The files of the folder this store writes: script files and temporary files of a stopped write (of script files and of the index). Other
        // files are not listed.
        List<string> ScriptFiles()
        {
            var l = new List<string>();
            if (!Directory.Exists(Folder)) return l;
            foreach (var f in Directory.GetFiles(Folder))
            {
                string name = Path.GetFileName(f);
                if (IsFileName(name)) l.Add(f);
                else
                {
                    int t = name.IndexOf(TempMark, StringComparison.Ordinal);
                    if (t > 0 && (IsFileName(name.Substring(0, t)) || name.Substring(0, t) == IndexName)) l.Add(f);
                }
            }
            l.Sort(string.CompareOrdinal);
            return l;
        }

        /// <summary>
        /// Removes the files the hidden rows of the index name, then those rows from the index. Nothing else is removed: a script file no row names, a
        /// leftover temporary file and any other file stay. Numbers stay used. While the store's index could not be read (also a folder of script files
        /// without one), nothing is removed: the result has <see cref="ScriptPurgeResult.Unreadable"/> and <paramref name="findings"/> gets
        /// script.storeUnreadable. A file that cannot be removed answers script.storeWrite; the index then still lists the hidden rows.
        /// </summary>
        public ScriptPurgeResult Purge(FindingList findings = null)
        {
            return Write("purging the store", () =>
            {
                if (unreadable)
                {
                    findings?.Warning(ScriptFindings.StoreUnreadable, IndexPath, "the store's index could not be read; nothing is purged before it is written again");
                    return new ScriptPurgeResult { Unreadable = true };
                }
                var res = new ScriptPurgeResult();
                foreach (var f in PurgeFiles())
                {
                    long len = new FileInfo(f).Length;
                    System.IO.File.Delete(f);
                    res.Files++;
                    res.Bytes += len;
                }
                res.Rows = index.Rows.RemoveAll(r => r.Deleted);
                if (res.Rows > 0) WriteIndex();
                return res;
            });
        }
    }
}
