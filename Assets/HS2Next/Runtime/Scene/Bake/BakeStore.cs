// The store of bakes in the user's data (Unity-free): `<UserData>/bakes/` with `index.json` (the rows) and `blobs/<sha256>.bin` (the same bytes as a
// scene's `res/` entries). Nothing exists until the first bake. Writing puts the blobs first (a temporary name, then a rename; a blob already there is
// not written again) and the index after them (a temporary file, then a replace), so a stop between the two leaves only blobs no row lists. Row numbers
// only grow and are never given again, also after a delete or a purge. A delete marks a row and touches no file; a purge drops the marked rows and then
// every blob that no remaining row lists, the set to keep being read from the index alone, never from the files present; it waits while a bake is being
// written or imported. A missing or unreadable index is an empty store: the file is left as it is until the next write, which first copies it to
// `index.json.bad`, and a purge is refused until then. A scene never depends on the store: a bake added to a scene has its blobs copied into the scene.
// One app writes the store; every call is serialized on the store object.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using HS2Next.Save;

namespace HS2Next.Scenes.Bake
{
    /// <summary>A store operation that did not happen: its <see cref="Code"/> is <see cref="BakeFindings.StoreWrite"/>, <see cref="BakeFindings.Busy"/> or
    /// <see cref="BakeFindings.StoreUnreadable"/>.</summary>
    public sealed class BakeStoreException : Exception
    {
        public readonly string Code;
        public BakeStoreException(string code, string detail, Exception inner = null) : base(detail, inner) { Code = code; }
    }

    /// <summary>`rows[]` of index.json: one bake of the store.</summary>
    public sealed class BakeStoreRow : DocObject
    {
        /// <summary>The row's number, the generated row's id in the item list.</summary>
        public int Number;
        /// <summary>The bake's identity.</summary>
        public string Id;
        public string Name = "";
        public DateTime Created = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public string Kind = "";
        /// <summary>The main blob's SHA-256.</summary>
        public string Main;
        /// <summary>The SHA-256 of every mesh and texture blob, in the main's order; never null.</summary>
        public List<string> Data = new List<string>();
        /// <summary>The parts label ("" for a whole bake).</summary>
        public string Parts = "";
        /// <summary>The bake's FK bones by name (the generated row's FK list); never null.</summary>
        public List<string> Fk = new List<string>();
        /// <summary>The bytes of its blobs together.</summary>
        public long Bytes;
        public bool Deleted;

        internal override void ReadMembers(DocReader r)
        {
            Number = r.Int("number");
            Id = r.Id("id");
            Name = r.Str("name", Name);
            Created = r.Time("created", Created);
            Kind = r.Str("kind", Kind);
            Main = BakeMain.ReadSha(r, "main");
            Data = r.Array("data", v =>
            {
                string s = v.AsStr();
                if (!BakeBlob.IsSha(s)) throw new DocTypeException(v.Context, v.Token, v.Path, "expected a SHA-256 (64 lowercase hex digits), found \"" + s + "\"");
                return s;
            });
            Parts = r.Str("parts", Parts);
            Fk = new List<string>(r.StrArray("fk", Fk.ToArray()));
            Bytes = r.Int64("bytes", Bytes);
            Deleted = r.Bool("deleted", Deleted);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Int("number", Number);
            ReqId(w, "id", Id);
            ReqStr(w, "name", Name);
            w.Time("created", Created);
            ReqStr(w, "kind", Kind);
            BakeMain.WriteSha(w, "main", Main);
            w.Name("data");
            CheckLength(w, Data, -1);
            w.StartArray();
            foreach (var d in Data)
            {
                if (!BakeBlob.IsSha(d)) throw new SaveFormatException(SaveErrorCode.BadDocument, "not a SHA-256 name: \"" + d + "\"", w.Entry, w.Path);
                w.Str(d);
            }
            w.EndArray();
            ReqStr(w, "parts", Parts);
            w.Name("fk");
            CheckLength(w, Fk, -1);
            w.StrArray(Fk);
            w.Int64("bytes", Bytes);
            w.Bool("deleted", Deleted);
        }

        /// <summary>The main and every data blob, each once.</summary>
        public List<string> Blobs()
        {
            var l = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (Main != null && seen.Add(Main)) l.Add(Main);
            foreach (var d in Data) if (d != null && seen.Add(d)) l.Add(d);
            return l;
        }

        /// <summary>The scene block naming this row's bake (store row <see cref="Number"/>).</summary>
        public ItemBake ToItemBake()
        {
            var b = new ItemBake { Id = Id, Main = BakeBlob.ResName(Main), No = Number };
            foreach (var d in Data) b.Data.Add(BakeBlob.ResName(d));
            return b;
        }

        /// <summary>A deep copy.</summary>
        public BakeStoreRow Clone()
        {
            var bytes = CanonicalJsonWriter.ToDocumentBytes(BakeStore.IndexName, WriteTo);
            var c = new BakeStoreRow();
            c.ReadFrom(new DocReader(DocReader.Parse(bytes, BakeStore.IndexName), new DocReadContext(BakeStore.IndexName)));
            return c;
        }
    }

    /// <summary>index.json.</summary>
    public sealed class BakeStoreIndex : DocObject
    {
        public const string FormatName = "hs2next.bakeStore";
        public const int FormatVersion = 1;

        public string Format = FormatName;
        public int Version = FormatVersion;
        public int LastNumber;
        public List<BakeStoreRow> Rows = new List<BakeStoreRow>();

        internal override void ReadMembers(DocReader r)
        {
            Format = r.Str("format");
            if (Format != FormatName) throw new DocTypeException(r.Context, r.Object, r.Path, "format \"" + Format + "\" is not " + FormatName);
            Version = r.Int("version");
            if (Version > FormatVersion || Version < 1) throw new DocTypeException(r.Context, r.Object, r.Path, "store version " + Version + " is not " + FormatVersion);
            LastNumber = r.Int("lastNumber");
            Rows = ReadList<BakeStoreRow>(r, "rows");
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "format", Format);
            w.Int("version", Version);
            w.Int("lastNumber", LastNumber);
            WriteList(w, "rows", Rows);
        }

        internal byte[] ToBytes() => CanonicalJsonWriter.ToDocumentBytes(BakeStore.IndexName, WriteTo);

        internal static BakeStoreIndex Parse(byte[] bytes)
        {
            var x = new BakeStoreIndex();
            x.ReadFrom(new DocReader(DocReader.Parse(bytes, BakeStore.IndexName), new DocReadContext(BakeStore.IndexName)));
            return x;
        }
    }

    /// <summary>What a purge removed.</summary>
    public sealed class BakePurgeResult
    {
        /// <summary>Rows removed (they were marked deleted).</summary>
        public int Rows;
        /// <summary>Blob files removed, and their bytes.</summary>
        public int Blobs;
        public long Bytes;
        /// <summary>Temporary files of an interrupted write removed.</summary>
        public int Leftovers;
    }

    /// <summary>The store of one UserData folder.</summary>
    public sealed class BakeStore
    {
        public const string FolderName = "bakes";
        public const string IndexName = "index.json";
        public const string BlobsName = "blobs";
        public const string BadName = "index.json.bad";
        /// <summary>The first row number of a new store.</summary>
        public const int FirstNumber = 73100001;

        const string TempMark = ".tmp-";

        readonly object gate = new object();
        BakeStoreIndex index = new BakeStoreIndex { LastNumber = FirstNumber - 1 };
        int busy;
        bool unreadable;

        /// <summary>The store folder.</summary>
        public string Folder { get; }
        public string IndexPath => Path.Combine(Folder, IndexName);
        public string BlobsPath => Path.Combine(Folder, BlobsName);

        /// <summary>A test seam: runs after the blobs of an add are written and before its index is (a throw there leaves the blobs and the old index).</summary>
        public Action BeforeIndexWrite;

        BakeStore(string folder)
        {
            Folder = Path.GetFullPath(folder ?? throw new ArgumentNullException(nameof(folder)));
        }

        /// <summary>Opens the store of folder <paramref name="folder"/> (nothing is created): reads its index; findings (bake.storeUnreadable) go to <paramref name="findings"/>.</summary>
        public static BakeStore Open(string folder, FindingList findings = null)
        {
            var s = new BakeStore(folder);
            s.Reload(findings);
            return s;
        }

        /// <summary>The store of a UserData folder: <c>&lt;userData&gt;/bakes</c>.</summary>
        public static BakeStore ForUserData(string userData, FindingList findings = null) => Open(Path.Combine(userData, FolderName), findings);

        /// <summary>Reads index.json again. A missing folder is an empty store with no finding; a folder without a readable index is an empty store,
        /// <see cref="Unreadable"/>, with finding bake.storeUnreadable.</summary>
        public void Reload(FindingList findings = null)
        {
            lock (gate)
            {
                index = new BakeStoreIndex { LastNumber = FirstNumber - 1 };
                unreadable = false;
                if (!Directory.Exists(Folder)) return;
                string why;
                if (!File.Exists(IndexPath)) why = "the store has no " + IndexName;
                else
                {
                    try
                    {
                        var read = BakeStoreIndex.Parse(File.ReadAllBytes(IndexPath));
                        foreach (var r in read.Rows) read.LastNumber = Math.Max(read.LastNumber, r.Number);
                        read.LastNumber = Math.Max(read.LastNumber, FirstNumber - 1);
                        index = read;
                        return;
                    }
                    catch (SaveFormatException e) { why = IndexName + " does not read: " + e.Message; }
                    catch (IOException e) { why = IndexName + " cannot be read: " + e.Message; }
                    catch (UnauthorizedAccessException e) { why = IndexName + " cannot be read: " + e.Message; }
                }
                unreadable = true;
                findings?.Warning(BakeFindings.StoreUnreadable, IndexPath, why + "; the store reads as empty, the file is kept and copied to " + BadName + " before the next write");
            }
        }

        /// <summary>True while the store's index could not be read and nothing has been written since.</summary>
        public bool Unreadable { get { lock (gate) return unreadable; } }

        /// <summary>The highest number given so far (<see cref="FirstNumber"/> - 1 for a new store).</summary>
        public int LastNumber { get { lock (gate) return index.LastNumber; } }

        /// <summary>Copies of every row, deleted ones included, in index order.</summary>
        public List<BakeStoreRow> AllRows()
        {
            lock (gate) return index.Rows.ConvertAll(r => r.Clone());
        }

        /// <summary>Copies of the rows not deleted, in index order.</summary>
        public List<BakeStoreRow> LiveRows()
        {
            lock (gate) return index.Rows.FindAll(r => !r.Deleted).ConvertAll(r => r.Clone());
        }

        /// <summary>A copy of row <paramref name="number"/>, or null.</summary>
        public BakeStoreRow Find(int number)
        {
            lock (gate) return index.Rows.Find(r => r.Number == number)?.Clone();
        }

        /// <summary>A copy of the first row of bake <paramref name="id"/> (one not deleted first), or null.</summary>
        public BakeStoreRow FindById(string id)
        {
            lock (gate) return (index.Rows.Find(r => r.Id == id && !r.Deleted) ?? index.Rows.Find(r => r.Id == id))?.Clone();
        }

        // ---------- blobs ----------

        string BlobPath(string sha) => Path.Combine(BlobsPath, sha + ".bin");

        /// <summary>True when blob <paramref name="sha"/> is in the store (its content is not checked).</summary>
        public bool HasBlob(string sha) => BakeBlob.IsSha(sha) && File.Exists(BlobPath(sha));

        /// <summary>The bytes of blob <paramref name="sha"/>, or null when the store does not hold it; a blob whose content does not equal its name is not used
        /// (bake.storeCorrupt).</summary>
        public byte[] ReadBlob(string sha, FindingList findings = null, string path = null)
        {
            if (!BakeBlob.IsSha(sha)) return null;
            string p = BlobPath(sha);
            byte[] b;
            try
            {
                if (!File.Exists(p)) return null;
                b = File.ReadAllBytes(p);
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            if (BakeBlob.Sha256(b) != sha)
            {
                findings?.Warning(BakeFindings.StoreCorrupt, path ?? p, "the store's blob " + sha + " does not hold what its name says; not used");
                return null;
            }
            return b;
        }

        /// <summary>Blob <paramref name="sha"/> for a scene's bake <paramref name="id"/>: given only when a row of the store (deleted ones included) holds that
        /// bake; else null.</summary>
        public byte[] Supply(string id, string sha, FindingList findings = null, string path = null)
        {
            lock (gate) if (!index.Rows.Exists(r => r.Id == id)) return null;
            return ReadBlob(sha, findings, path);
        }

        /// <summary>True when a blob row <paramref name="row"/> lists is missing or does not hold what its name says (the row is damaged). Reads every blob.</summary>
        public bool IsDamaged(BakeStoreRow row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            foreach (var sha in row.Blobs()) if (ReadBlob(sha) == null) return true;
            return false;
        }

        // ---------- writing ----------

        /// <summary>Marks the store busy while the returned object is not disposed (a bake being written, an import): a purge then waits.</summary>
        public IDisposable Hold()
        {
            lock (gate) busy++;
            return new Release(this);
        }

        sealed class Release : IDisposable
        {
            BakeStore s;
            public Release(BakeStore s) { this.s = s; }
            public void Dispose()
            {
                var x = s;
                s = null;
                if (x != null) lock (x.gate) x.busy--;
            }
        }

        /// <summary>True while a <see cref="Hold"/> is open.</summary>
        public bool Busy { get { lock (gate) return busy > 0; } }

        static BakeStoreException WriteError(string what, Exception e) => new BakeStoreException(BakeFindings.StoreWrite, what + ": " + e.Message, e);

        void WriteBlobFile(string sha, byte[] bytes)
        {
            string final = BlobPath(sha);
            if (File.Exists(final)) return;
            Directory.CreateDirectory(BlobsPath);
            string temp = final + TempMark + Guid.NewGuid().ToString("N");
            File.WriteAllBytes(temp, bytes);
            try { File.Move(temp, final); }
            catch (IOException) when (File.Exists(final)) { File.Delete(temp); }
        }

        void WriteIndex()
        {
            Directory.CreateDirectory(Folder);
            var bytes = index.ToBytes();
            if (unreadable && File.Exists(IndexPath)) File.Copy(IndexPath, Path.Combine(Folder, BadName), true);
            string temp = IndexPath + TempMark + Guid.NewGuid().ToString("N");
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(IndexPath))
            {
                try { File.Replace(temp, IndexPath, null); }
                catch (PlatformNotSupportedException)
                {
                    File.Delete(IndexPath);
                    File.Move(temp, IndexPath);
                }
            }
            else File.Move(temp, IndexPath);
            unreadable = false;
        }

        /// <summary>Runs a write under the lock and the store's own error rule: a file error is <see cref="BakeFindings.StoreWrite"/>, and the index in memory is
        /// what it was before.</summary>
        T Write<T>(string what, Func<T> body)
        {
            lock (gate)
            {
                var before = index.ToBytes();
                bool wasUnreadable = unreadable;
                try { return body(); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException)
                {
                    index = BakeStoreIndex.Parse(before);
                    unreadable = wasUnreadable;
                    throw WriteError(what, e);
                }
                catch
                {
                    index = BakeStoreIndex.Parse(before);
                    unreadable = wasUnreadable;
                    throw;
                }
            }
        }

        /// <summary>
        /// Adds a row for a bake: <paramref name="row"/>'s id, name, created, kind, main, data, parts and FK list (its number and deleted mark are set here),
        /// and <paramref name="blobs"/> by SHA-256 (each checked against its name; a blob the store holds already is not written again). Every blob the row
        /// lists must be given or held. The row takes <paramref name="number"/> when given (it must be free) or the next number. Returns a copy of the stored row.
        /// Throws <see cref="BakeStoreException"/> (bake.storeWrite) when a file cannot be written: nothing of the row is in the index then.
        /// </summary>
        public BakeStoreRow Add(BakeStoreRow row, IEnumerable<KeyValuePair<string, byte[]>> blobs, int? number = null)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            if (!JsonText.IsId(row.Id)) throw new ArgumentException("the row's id is not an id: " + row.Id);
            if (!BakeBlob.IsSha(row.Main)) throw new ArgumentException("the row's main is not a SHA-256: " + row.Main);
            foreach (var d in row.Data) if (!BakeBlob.IsSha(d)) throw new ArgumentException("a data entry is not a SHA-256: " + d);
            var given = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            if (blobs != null)
                foreach (var kv in blobs)
                {
                    if (kv.Value == null || BakeBlob.Sha256(kv.Value) != kv.Key) throw new ArgumentException("blob " + kv.Key + " does not hold what its name says");
                    given[kv.Key] = kv.Value;
                }
            foreach (var sha in row.Blobs())
                if (!given.ContainsKey(sha) && !HasBlob(sha)) throw new ArgumentException("blob " + sha + " is neither given nor in the store");
            return Write("adding a bake to the store", () =>
            {
                int n = number ?? index.LastNumber + 1;
                if (index.Rows.Exists(r => r.Number == n)) throw new ArgumentException("row " + n + " exists");
                long bytes = 0;
                foreach (var sha in row.Blobs())
                {
                    if (given.TryGetValue(sha, out var b))
                    {
                        WriteBlobFile(sha, b);
                        bytes += b.LongLength;
                    }
                    else bytes += new FileInfo(BlobPath(sha)).Length;
                }
                BeforeIndexWrite?.Invoke();
                var stored = row.Clone();
                stored.Number = n;
                stored.Deleted = false;
                stored.Bytes = bytes;
                index.Rows.Add(stored);
                index.LastNumber = Math.Max(index.LastNumber, n);
                WriteIndex();
                return stored.Clone();
            });
        }

        /// <summary>Raises the last number to <paramref name="number"/> when it is higher (the importer keeps the old numbers); writes the index when it changes.</summary>
        public void RaiseLastNumber(int number)
        {
            Write("raising the store's number", () =>
            {
                if (number <= index.LastNumber) return false;
                index.LastNumber = number;
                WriteIndex();
                return true;
            });
        }

        /// <summary>Renames row <paramref name="number"/> (the index only); returns its old name, or null when there is no such row.</summary>
        public string Rename(int number, string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            return Write("renaming a bake", () =>
            {
                var r = index.Rows.Find(x => x.Number == number);
                if (r == null) return null;
                string old = r.Name;
                if (old == name) return old;
                r.Name = name;
                WriteIndex();
                return old;
            });
        }

        /// <summary>Marks row <paramref name="number"/> deleted, or clears the mark with <paramref name="restore"/>; no file is touched. False when there is no such row.</summary>
        public bool Delete(int number, bool restore = false)
        {
            return Write("deleting a bake", () =>
            {
                var r = index.Rows.Find(x => x.Number == number);
                if (r == null) return false;
                if (r.Deleted == !restore) return true;
                r.Deleted = !restore;
                WriteIndex();
                return true;
            });
        }

        /// <summary>The rows and blobs a purge would remove now (nothing is removed).</summary>
        public BakePurgeResult PurgePreview()
        {
            lock (gate)
            {
                var keep = KeepSet();
                var res = new BakePurgeResult { Rows = index.Rows.FindAll(r => r.Deleted).Count };
                foreach (var f in BlobFiles())
                {
                    if (f.Value == null) res.Leftovers++;
                    else if (!keep.Contains(f.Value)) { res.Blobs++; res.Bytes += new FileInfo(f.Key).Length; }
                }
                return res;
            }
        }

        HashSet<string> KeepSet()
        {
            var keep = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in index.Rows) if (!r.Deleted) foreach (var sha in r.Blobs()) keep.Add(sha);
            return keep;
        }

        /// <summary>The files of blobs/ this store writes: blob files (path, SHA-256) and temporary files of an interrupted write (path, null). Other files are not listed.</summary>
        List<KeyValuePair<string, string>> BlobFiles()
        {
            var l = new List<KeyValuePair<string, string>>();
            if (!Directory.Exists(BlobsPath)) return l;
            foreach (var f in Directory.GetFiles(BlobsPath))
            {
                string name = Path.GetFileName(f);
                if (name.Length == 68 && name.EndsWith(".bin", StringComparison.Ordinal) && BakeBlob.IsSha(name.Substring(0, 64))) l.Add(new KeyValuePair<string, string>(f, name.Substring(0, 64)));
                else if (name.Length > 68 + TempMark.Length && BakeBlob.IsSha(name.Substring(0, 64)) && string.CompareOrdinal(name, 64, ".bin" + TempMark, 0, 4 + TempMark.Length) == 0)
                    l.Add(new KeyValuePair<string, string>(f, null));
            }
            l.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return l;
        }

        /// <summary>
        /// Removes the deleted rows, then every blob that no remaining row lists in <c>main</c> or <c>data</c> (the set to keep is read from the index alone,
        /// never from the files present), and the temporary files of an interrupted write. Refused while the store is busy (<see cref="BakeFindings.Busy"/>)
        /// and while its index could not be read (<see cref="BakeFindings.StoreUnreadable"/>).
        /// </summary>
        public BakePurgeResult Purge()
        {
            return Write("purging the store", () =>
            {
                if (busy > 0) throw new BakeStoreException(BakeFindings.Busy, "a bake is being written or imported; purge later");
                if (unreadable) throw new BakeStoreException(BakeFindings.StoreUnreadable, "the store's index could not be read; nothing is purged before it is written again");
                var res = new BakePurgeResult { Rows = index.Rows.RemoveAll(r => r.Deleted) };
                if (res.Rows > 0) WriteIndex();
                var keep = KeepSet();
                foreach (var f in BlobFiles())
                {
                    if (f.Value != null && keep.Contains(f.Value)) continue;
                    long len = new FileInfo(f.Key).Length;
                    File.Delete(f.Key);
                    if (f.Value == null) res.Leftovers++;
                    else { res.Blobs++; res.Bytes += len; }
                }
                return res;
            });
        }

        // ---------- renames in a scene ----------

        /// <summary>
        /// The items a rename of bake <paramref name="bakeId"/> from <paramref name="oldName"/> renames: those whose node name equals the old name and whose
        /// <c>bake.id</c> equals the bake's, in the order given. <paramref name="items"/> holds (node id, node name, bake id) of the scene's items.
        /// </summary>
        public static List<string> RenameTargets(IEnumerable<(string nodeId, string name, string bakeId)> items, string bakeId, string oldName)
        {
            var l = new List<string>();
            if (items == null) return l;
            foreach (var (nodeId, name, id) in items)
                if (nodeId != null && id != null && id == bakeId && name == oldName) l.Add(nodeId);
            return l;
        }
    }
}
