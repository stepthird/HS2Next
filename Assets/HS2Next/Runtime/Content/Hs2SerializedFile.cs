// A small reader of Unity's serialized-file format for the HS2 player's own uncompressed files (sharedassets*.assets,
// level*, globalgamemanagers.assets; Phase 3.3 package B). It reads the
// header, the type table (skipping type trees when the file has them), the object table, the script-type table and the
// external-file table of formats 9–23 (Unity 5.x–6; HS2 is Unity 2018.4, format 17), and hands out an object's bytes
// with a little-endian field cursor that follows Unity's 4-byte alignment rules. Anything outside the supported layout
// throws Hs2SerializedFileException: callers fail closed. Read-only; the file is opened, read and closed per call.
// Package S added (Phase 3.3): FromBytes (a serialized file held in memory, e.g. a bundle's node) and
// Hs2BundleFile (UnityFS bundles with uncompressed or LZ4 / LZ4HC blocks: the node table and a node's bytes, decompressing
// only the blocks that cover the requested range; LZMA blocks throw), used to read a map scene's saved ambient probe.
// Phase 4.4 added the AssetBundle object's container keys (the asset names a load by name matches) and the data blocks'
// compression, read by the per-asset redirect (AssetRedirect.cs) without opening the bundle in Unity.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HS2Next.Content
{
    public sealed class Hs2SerializedFileException : Exception
    {
        public Hs2SerializedFileException(string message) : base(message) { }
    }

    /// <summary>One entry of the object table.</summary>
    public struct Hs2SerializedObject
    {
        public long PathId;
        /// <summary>Absolute file offset of the object's data.</summary>
        public long Start;
        public uint Size;
        public int TypeIndex;
        /// <summary>Unity class id (114 = MonoBehaviour, 115 = MonoScript, 28 = Texture2D, ...).</summary>
        public int ClassId;
        /// <summary>Absolute file offset of the object's table entry fields: the data-relative start (32-bit before format 22,
        /// 64-bit from 22 on), followed by the 32-bit size (the patch writer rewrites them).</summary>
        public long EntryOffset;
    }

    /// <summary>One entry of the external-file table (PPtr file id = index + 1).</summary>
    public sealed class Hs2SerializedExternal
    {
        public string Path;
        public Guid Guid;
        public int Type;
        /// <summary>The last path component in lower case ("sharedassets2.assets", "globalgamemanagers.assets").</summary>
        public string FileName => System.IO.Path.GetFileName((Path ?? "").Replace('\\', '/')).ToLowerInvariant();
    }

    /// <summary>A PPtr: file id (0 = this file, n = externals[n − 1]) and path id.</summary>
    public struct Hs2PPtr
    {
        public int FileId;
        public long PathId;
        public bool IsNull => PathId == 0;
        public override string ToString() => FileId + ":" + PathId;
    }

    public sealed class Hs2SerializedFile
    {
        public const int ClassMonoBehaviour = 114, ClassMonoScript = 115;
        public const int MinVersion = 9, MaxVersion = 23;

        public string FilePath { get; private set; }
        public int Version { get; private set; }
        public string UnityVersion { get; private set; }
        public int TargetPlatform { get; private set; }
        public bool HasTypeTrees { get; private set; }
        public long FileSize { get; private set; }
        public long DataOffset { get; private set; }
        public IReadOnlyList<int> TypeClassIds => typeClassIds;
        public IReadOnlyList<Hs2SerializedObject> Objects => objects;
        public IReadOnlyList<Hs2SerializedExternal> Externals => externals;

        readonly List<int> typeClassIds = new List<int>();
        readonly List<Hs2SerializedObject> objects = new List<Hs2SerializedObject>();
        readonly Dictionary<long, int> byPathId = new Dictionary<long, int>();
        readonly List<Hs2SerializedExternal> externals = new List<Hs2SerializedExternal>();
        // the metadata block (absolute file offset metaOrigin) and, per type, where its type tree is in it (-1: none)
        byte[] meta;
        long metaOrigin;
        readonly List<(int pos, int nodes, int strings)> treeAt = new List<(int, int, int)>();
        readonly Dictionary<int, Hs2TypeNode> trees = new Dictionary<int, Hs2TypeNode>();

        /// <summary>Absolute file offset of the header's file size field (big-endian; 32-bit before format 22, 64-bit from 22 on).</summary>
        public long FileSizeField => Version >= 22 ? 24 : 4;

        Hs2SerializedFile() { }

        /// <summary>Reads the metadata of <paramref name="path"/> (header, types, objects, externals); throws on any layout it does not know.</summary>
        public static Hs2SerializedFile Open(string path)
        {
            var f = new Hs2SerializedFile { FilePath = path };
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                f.ReadHeader(fs);
            return f;
        }

        /// <summary>A serialized file held in memory (<paramref name="label"/> names it in messages); see <see cref="Open"/>.</summary>
        public static Hs2SerializedFile FromBytes(byte[] data, string label)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return FromStream(() => new MemoryStream(data, false), label);
        }

        /// <summary>A serialized file held in memory at <paramref name="data"/>[<paramref name="offset"/>, +<paramref name="count"/>)
        /// (a node of a bundle's decompressed data); positions are relative to its start.</summary>
        public static Hs2SerializedFile FromBytes(byte[] data, int offset, int count, string label)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return FromStream(() => new MemoryStream(data, offset, count, false), label);
        }

        /// <summary>
        /// A serialized file read through <paramref name="open"/> (a seekable stream of exactly the file's bytes, opened for
        /// each read; e.g. <see cref="Hs2BundleFile.OpenNode"/>); see <see cref="Open"/>.
        /// </summary>
        public static Hs2SerializedFile FromStream(Func<Stream> open, string label)
        {
            var f = new Hs2SerializedFile { FilePath = label, opener = open ?? throw new ArgumentNullException(nameof(open)) };
            using (var s = open())
                f.ReadHeader(s);
            return f;
        }

        Func<Stream> opener;

        Stream OpenData() => opener != null ? opener() : new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        void ReadHeader(Stream fs)
        {
            var f = this;
            {
                f.FileSize = fs.Length;
                var head = new byte[20];
                if (fs.Read(head, 0, 20) != 20) throw new Hs2SerializedFileException("file shorter than a header");
                uint metadataSize = BigU32(head, 0), fileSize32 = BigU32(head, 4), version = BigU32(head, 8), dataOffset32 = BigU32(head, 12);
                if (version < MinVersion || version > MaxVersion) throw new Hs2SerializedFileException("serialized-file format " + version + " is not supported (" + MinVersion + "-" + MaxVersion + ")");
                if (head[16] != 0) throw new Hs2SerializedFileException("big-endian metadata is not supported");
                long fileSize = fileSize32, dataOffset = dataOffset32, metaStart = 20;
                if (version >= 22)
                {
                    // format 22 on (Unity 2020.1+): metadata size, 64-bit file size and data offset, 8 reserved bytes
                    var more = new byte[28];
                    if (fs.Read(more, 0, 28) != 28) throw new Hs2SerializedFileException("file shorter than a header");
                    metadataSize = BigU32(more, 0);
                    fileSize = (long)((ulong)BigU32(more, 4) << 32 | BigU32(more, 8));
                    dataOffset = (long)((ulong)BigU32(more, 12) << 32 | BigU32(more, 16));
                    metaStart = 48;
                }
                if (fileSize != fs.Length) throw new Hs2SerializedFileException("header file size " + fileSize + " != " + fs.Length);
                if (metadataSize == 0 || metaStart + (long)metadataSize > fs.Length || dataOffset > fs.Length) throw new Hs2SerializedFileException("metadata size / data offset out of range");
                f.Version = (int)version;
                f.DataOffset = dataOffset;
                var meta = new byte[metadataSize];
                fs.Position = metaStart;
                if (fs.Read(meta, 0, meta.Length) != meta.Length) throw new Hs2SerializedFileException("metadata truncated");
                f.meta = meta;
                f.metaOrigin = metaStart;
                f.ReadMetadata(new Hs2FieldReader(meta, metaStart));
            }
        }

        static uint BigU32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);

        void ReadMetadata(Hs2FieldReader r)
        {
            int v = Version;
            UnityVersion = r.CString();
            TargetPlatform = r.I32();
            HasTypeTrees = v >= 13 && r.U8() != 0;
            int typeCount = r.I32();
            if (typeCount < 0 || typeCount > 100000) throw new Hs2SerializedFileException("type count " + typeCount);
            for (int i = 0; i < typeCount; i++)
            {
                int classId = r.I32();
                if (v >= 16) r.U8();          // stripped
                if (v >= 17) r.I16();         // script type index
                if (v >= 13)
                {
                    if ((v < 16 && classId < 0) || (v >= 16 && classId == ClassMonoBehaviour)) r.Skip(16);   // script id
                    r.Skip(16);                                                                                 // old type hash
                }
                if (HasTypeTrees)
                {
                    if (v < 12 && v != 10) throw new Hs2SerializedFileException("old-style type trees are not supported");
                    bool tree = true;
                    if (v >= 23)
                    {
                        // format 23 (Unity 6): a content hash and the tree's size, the tree only when the size is not 0
                        r.Skip(16);
                        tree = r.I32() != 0;
                    }
                    if (tree)
                    {
                        if (v >= 23 && (r.I32() != 0x7474686D || r.I32() != v)) throw new Hs2SerializedFileException("bad type tree header");   // "mhtt", format
                        int nodes = r.I32(), strings = r.I32();
                        if (nodes < 0 || strings < 0) throw new Hs2SerializedFileException("bad type tree");
                        treeAt.Add((r.Position, nodes, strings));
                        r.Skip((long)nodes * (v >= 19 ? 32 : 24) + strings);
                    }
                    else treeAt.Add((-1, 0, 0));
                    if (v >= 21) { int deps = r.I32(); r.Skip(4L * deps); }
                }
                else treeAt.Add((-1, 0, 0));
                typeClassIds.Add(classId);
            }
            bool bigIds = false;
            if (v >= 7 && v < 14) bigIds = r.I32() != 0;
            int objectCount = r.I32();
            if (objectCount < 0 || objectCount > 10000000) throw new Hs2SerializedFileException("object count " + objectCount);
            for (int i = 0; i < objectCount; i++)
            {
                var o = new Hs2SerializedObject();
                if (v >= 14) { r.Align(); o.PathId = r.I64(); }
                else o.PathId = bigIds ? r.I64() : r.I32();
                o.EntryOffset = metaOrigin + r.Position;
                o.Start = DataOffset + (v >= 22 ? r.I64() : r.U32());
                o.Size = r.U32();
                o.TypeIndex = r.I32();
                if (v < 16) o.ClassId = r.U16();
                else
                {
                    if (o.TypeIndex < 0 || o.TypeIndex >= typeClassIds.Count) throw new Hs2SerializedFileException("type index " + o.TypeIndex + " out of range");
                    o.ClassId = typeClassIds[o.TypeIndex];
                }
                if (v < 11) r.U16();
                if (v >= 11 && v < 17) r.I16();
                if (v == 15 || v == 16) r.U8();
                if (o.Start < 0 || o.Start + o.Size > FileSize) throw new Hs2SerializedFileException("object " + o.PathId + " outside the file");
                byPathId[o.PathId] = objects.Count;
                objects.Add(o);
            }
            if (v >= 11)
            {
                int scripts = r.I32();
                for (int i = 0; i < scripts; i++)
                {
                    r.I32();
                    if (v >= 14) { r.Align(); r.I64(); } else r.I32();
                }
            }
            int extCount = r.I32();
            if (extCount < 0 || extCount > 100000) throw new Hs2SerializedFileException("external count " + extCount);
            for (int i = 0; i < extCount; i++)
            {
                var e = new Hs2SerializedExternal();
                if (v >= 6) r.CString();
                if (v >= 5) { e.Guid = new Guid(r.Bytes(16)); e.Type = r.I32(); }
                e.Path = r.CString();
                externals.Add(e);
            }
        }

        /// <summary>
        /// The type tree of an object's type (parsed on first use), or null when the file stores no tree for it. Field names
        /// come from the file's own string table; Unity's shared names (offsets with the high bit set) resolve for the few
        /// names this reader uses and read "@offset" otherwise.
        /// </summary>
        public Hs2TypeNode TypeTreeOf(Hs2SerializedObject o)
        {
            int ti = o.TypeIndex;
            if (Version < 16) ti = typeClassIds.IndexOf(o.ClassId);
            if (ti < 0 || ti >= treeAt.Count || treeAt[ti].pos < 0) return null;
            lock (trees)
            {
                if (trees.TryGetValue(ti, out var t)) return t;
                t = ParseTree(treeAt[ti]);
                trees[ti] = t;
                return t;
            }
        }

        Hs2TypeNode ParseTree((int pos, int nodes, int strings) at)
        {
            int size = Version >= 19 ? 32 : 24;
            long sbase = at.pos + (long)at.nodes * size;
            if (at.nodes < 1 || sbase + at.strings > meta.Length) throw new Hs2SerializedFileException("type tree outside the metadata");
            var list = new List<Hs2TypeNode>(at.nodes);
            for (int i = 0; i < at.nodes; i++)
            {
                int p = at.pos + i * size;
                list.Add(new Hs2TypeNode
                {
                    Level = meta[p + 2], TypeFlags = meta[p + 3],
                    Type = TreeString((int)sbase, at.strings, LeU32(meta, p + 4)), Name = TreeString((int)sbase, at.strings, LeU32(meta, p + 8)),
                    ByteSize = (int)LeU32(meta, p + 12), MetaFlag = (int)LeU32(meta, p + 20),
                });
            }
            var stack = new List<Hs2TypeNode> { list[0] };
            for (int i = 1; i < list.Count; i++)
            {
                var n = list[i];
                while (stack.Count > 0 && stack[stack.Count - 1].Level >= n.Level) stack.RemoveAt(stack.Count - 1);
                if (stack.Count == 0 || n.Level != stack[stack.Count - 1].Level + 1) throw new Hs2SerializedFileException("type tree levels out of order");
                stack[stack.Count - 1].Children.Add(n);
                stack.Add(n);
            }
            return list[0];
        }

        static uint LeU32(byte[] b, int o) => (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24);

        // Unity's shared type-tree strings this reader needs by name (offsets into the engine's common string buffer)
        static readonly Dictionary<uint, string> CommonNames = new Dictionary<uint, string>
        {
            [49] = "Array", [76] = "bool", [81] = "char", [106] = "data", [161] = "float", [222] = "int", [427] = "m_Name",
            [795] = "size", [840] = "string", [894] = "TypelessData", [907] = "UInt16", [914] = "UInt32", [928] = "UInt8",
        };

        string TreeString(int sbase, int ssize, uint off)
        {
            if ((off & 0x80000000u) != 0) { off &= 0x7FFFFFFFu; return CommonNames.TryGetValue(off, out var c) ? c : "@" + off; }
            if (off >= ssize) throw new Hs2SerializedFileException("type tree string outside its table");
            int s0 = sbase + (int)off, e = s0;
            while (e < sbase + ssize && meta[e] != 0) e++;
            return Encoding.UTF8.GetString(meta, s0, e - s0);
        }

        /// <summary>
        /// This serialized file (its bytes at <paramref name="buf"/>[<paramref name="offset"/>..]) with the objects of
        /// <paramref name="replace"/> (path id → new bytes) replaced. When every new object has its old size, the bytes are
        /// patched in place and null is returned; otherwise a new array holds the file: objects keep their table order, an
        /// object whose size changes moves every later object by a multiple of 16 bytes (their alignment is kept), the table's
        /// starts and sizes and the header's file size follow, and bytes between objects become zeros. The metadata is
        /// unchanged otherwise.
        /// </summary>
        public byte[] WithObjects(byte[] buf, int offset, IDictionary<long, byte[]> replace)
        {
            if (buf == null || offset < 0 || offset + FileSize > buf.LongLength) throw new Hs2SerializedFileException("serialized file bytes do not match the header");
            bool same = true;
            foreach (var kv in replace)
            {
                if (!TryFind(kv.Key, out var o)) throw new Hs2SerializedFileException("object " + kv.Key + " not in the file");
                if (kv.Value.Length != o.Size) same = false;
            }
            if (same)
            {
                foreach (var o in objects)
                    if (replace.TryGetValue(o.PathId, out var nb)) Buffer.BlockCopy(nb, 0, buf, offset + (int)o.Start, nb.Length);
                return null;
            }
            var starts = new long[objects.Count];
            var sizes = new uint[objects.Count];
            var order = new List<int>(objects.Count);
            for (int i = 0; i < objects.Count; i++) order.Add(i);
            order.Sort((a, b) => objects[a].Start.CompareTo(objects[b].Start));
            long shift = 0, prevEnd = DataOffset;
            foreach (int i in order)
            {
                var o = objects[i];
                if (o.Start < prevEnd) throw new Hs2SerializedFileException("objects overlap");
                starts[i] = o.Start + shift;
                long size = replace.TryGetValue(o.PathId, out var nb) ? (long)nb.Length : o.Size;
                sizes[i] = (uint)size;
                shift += ((size - o.Size + 15) >> 4) << 4;
                prevEnd = o.Start + o.Size;
            }
            long total = FileSize + shift;
            if (total > int.MaxValue || (Version < 22 && total > uint.MaxValue)) throw new Hs2SerializedFileException("serialized file too large to rewrite");
            var r = new byte[total];
            Buffer.BlockCopy(buf, offset, r, 0, (int)DataOffset);
            long lastEnd = DataOffset;
            for (int i = 0; i < objects.Count; i++)
            {
                var o = objects[i];
                if (replace.TryGetValue(o.PathId, out var nb)) Buffer.BlockCopy(nb, 0, r, (int)starts[i], nb.Length);
                else Buffer.BlockCopy(buf, offset + (int)o.Start, r, (int)starts[i], (int)o.Size);
                lastEnd = Math.Max(lastEnd, o.Start + o.Size);
            }
            if (lastEnd < FileSize) Buffer.BlockCopy(buf, offset + (int)lastEnd, r, (int)(lastEnd + shift), (int)(FileSize - lastEnd));
            if (Version >= 22) for (int k = 0; k < 8; k++) r[FileSizeField + k] = (byte)(total >> (56 - 8 * k));
            else for (int k = 0; k < 4; k++) r[FileSizeField + k] = (byte)(total >> (24 - 8 * k));
            int w = Version >= 22 ? 8 : 4;
            for (int i = 0; i < objects.Count; i++)
            {
                long e = objects[i].EntryOffset, rel = starts[i] - DataOffset;
                for (int k = 0; k < w; k++) r[e + k] = (byte)(rel >> (8 * k));
                for (int k = 0; k < 4; k++) r[e + w + k] = (byte)(sizes[i] >> (8 * k));
            }
            return r;
        }

        public bool TryFind(long pathId, out Hs2SerializedObject o)
        {
            if (byPathId.TryGetValue(pathId, out int i)) { o = objects[i]; return true; }
            o = default;
            return false;
        }

        /// <summary>The external file a PPtr names (file id ≥ 1), or null (0 = this file, out of range).</summary>
        public Hs2SerializedExternal ExternalOf(Hs2PPtr p) => p.FileId >= 1 && p.FileId <= externals.Count ? externals[p.FileId - 1] : null;

        /// <summary>The bytes of an object.</summary>
        public byte[] ReadBytes(Hs2SerializedObject o)
        {
            var b = new byte[o.Size];
            using (var fs = OpenData())
            {
                fs.Position = o.Start;
                int got = 0;
                while (got < b.Length)
                {
                    int n = fs.Read(b, got, b.Length - got);
                    if (n <= 0) throw new Hs2SerializedFileException("object " + o.PathId + " truncated");
                    got += n;
                }
            }
            return b;
        }

        /// <summary>The bytes of several objects in one pass (keyed by path id).</summary>
        public Dictionary<long, byte[]> ReadBytes(IEnumerable<Hs2SerializedObject> list)
        {
            var res = new Dictionary<long, byte[]>();
            using (var fs = OpenData())
                foreach (var o in list)
                {
                    var b = new byte[o.Size];
                    fs.Position = o.Start;
                    int got = 0;
                    while (got < b.Length)
                    {
                        int n = fs.Read(b, got, b.Length - got);
                        if (n <= 0) throw new Hs2SerializedFileException("object " + o.PathId + " truncated");
                        got += n;
                    }
                    res[o.PathId] = b;
                }
            return res;
        }

        /// <summary>A field cursor over an object's bytes (position 0 = the object's start; Unity aligns relative to it).</summary>
        public Hs2FieldReader Reader(Hs2SerializedObject o) => new Hs2FieldReader(ReadBytes(o), 0);

        /// <summary>Unity class id of the AssetBundle object of a bundle's serialized file.</summary>
        public const int ClassAssetBundle = 142;

        /// <summary>
        /// The m_Container keys of an AssetBundle object (fields m_Name, m_PreloadTable, m_Container: each key followed by
        /// preloadIndex, preloadSize and the asset PPtr), in stored order. PPtrs hold a 64-bit path id from format 14 on.
        /// </summary>
        public List<string> AssetBundleContainerKeys(Hs2SerializedObject o)
        {
            if (o.ClassId != ClassAssetBundle) throw new Hs2SerializedFileException("object " + o.PathId + " is not an AssetBundle (class " + o.ClassId + ")");
            var r = Reader(o);
            int pptr = Version >= 14 ? 12 : 8;
            r.AlignedString();
            int preload = r.I32();
            if (preload < 0 || preload > 10000000) throw new Hs2SerializedFileException("preload table size " + preload);
            r.Skip((long)preload * pptr);
            int n = r.I32();
            if (n < 0 || n > 10000000) throw new Hs2SerializedFileException("container size " + n);
            var keys = new List<string>(n);
            for (int i = 0; i < n; i++)
            {
                keys.Add(r.AlignedString());
                r.Skip(8 + pptr);
            }
            return keys;
        }

        /// <summary>
        /// The MonoBehaviour base fields every script object starts with: m_GameObject, m_Enabled, m_Script, m_Name.
        /// </summary>
        public static void ReadMonoBehaviourHeader(Hs2FieldReader r, out Hs2PPtr gameObject, out bool enabled, out Hs2PPtr script, out string name)
        {
            gameObject = r.PPtr();
            enabled = r.Bool();
            script = r.PPtr();
            name = r.AlignedString();
        }

        /// <summary>
        /// A MonoScript's class and namespace (m_Name, m_ExecutionOrder, m_PropertiesHash, m_ClassName, m_Namespace, ...).
        /// </summary>
        public static bool TryReadMonoScript(byte[] b, out string className, out string ns)
        {
            className = ns = null;
            try
            {
                var r = new Hs2FieldReader(b, 0);
                r.AlignedString();
                r.I32();
                r.Skip(16);
                className = r.AlignedString();
                ns = r.AlignedString();
                return true;
            }
            catch (Hs2SerializedFileException) { return false; }
        }
    }

    /// <summary>
    /// Little-endian cursor over serialized bytes with Unity's rules: bools and bytes followed by alignment to 4 where Unity
    /// aligns (<see cref="Bool"/>), length-prefixed UTF-8 strings aligned to 4, PPtrs as (int32 file id, int64 path id).
    /// Every read past the end throws <see cref="Hs2SerializedFileException"/>.
    /// </summary>
    public sealed class Hs2FieldReader
    {
        readonly byte[] b;
        readonly long origin;
        int p;

        /// <param name="origin">Absolute file offset of b[0] (alignment of the metadata block is absolute; objects use 0).</param>
        public Hs2FieldReader(byte[] bytes, long origin)
        {
            b = bytes ?? throw new ArgumentNullException(nameof(bytes));
            this.origin = origin;
        }

        public int Position => p;
        public int Length => b.Length;
        public bool AtEnd => p == b.Length;

        void Need(long n)
        {
            if (n < 0 || p + n > b.Length) throw new Hs2SerializedFileException("read of " + n + " bytes at " + p + " past the end (" + b.Length + ")");
        }

        public void Skip(long n) { Need(n); p += (int)n; }
        public void Align() { long a = origin + p; int pad = (int)((4 - (a & 3)) & 3); Need(pad); p += pad; }
        public byte U8() { Need(1); return b[p++]; }
        public short I16() { Need(2); short v = (short)(b[p] | b[p + 1] << 8); p += 2; return v; }
        public ushort U16() => unchecked((ushort)I16());
        public int I32() { Need(4); int v = b[p] | b[p + 1] << 8 | b[p + 2] << 16 | b[p + 3] << 24; p += 4; return v; }
        public uint U32() => unchecked((uint)I32());
        public long I64() { uint lo = U32(); uint hi = U32(); return (long)((ulong)hi << 32 | lo); }
        public float F32() { Need(4); float v = BitConverter.ToSingle(b, p); p += 4; return v; }
        public byte[] Bytes(int n) { Need(n); var r = new byte[n]; Buffer.BlockCopy(b, p, r, 0, n); p += n; return r; }
        /// <summary>A bool field (1 byte) followed by Unity's alignment to 4.</summary>
        public bool Bool() { bool v = U8() != 0; Align(); return v; }
        public Hs2PPtr PPtr() => new Hs2PPtr { FileId = I32(), PathId = I64() };
        public float[] F32s(int n) { var r = new float[n]; for (int i = 0; i < n; i++) r[i] = F32(); return r; }

        /// <summary>An int32 length, UTF-8 bytes, alignment to 4.</summary>
        public string AlignedString()
        {
            int n = I32();
            if (n < 0 || n > 1 << 20) throw new Hs2SerializedFileException("string length " + n);
            Need(n);
            string s = Encoding.UTF8.GetString(b, p, n);
            p += n;
            Align();
            return s;
        }

        /// <summary>A zero-terminated UTF-8 string (metadata tables).</summary>
        public string CString()
        {
            int s = p;
            while (true)
            {
                Need(1);
                if (b[p++] == 0) break;
            }
            return Encoding.UTF8.GetString(b, s, p - s - 1);
        }

        /// <summary>Throws unless the cursor is exactly at the end (fail-closed layout checks).</summary>
        public void ExpectEnd(string what)
        {
            if (p != b.Length) throw new Hs2SerializedFileException(what + ": parse ended at " + p + " of " + b.Length + " bytes");
        }
    }

    /// <summary>One node of a serialized type tree: a field (name, type, byte size of a fixed-size leaf, flags) and its children.</summary>
    public sealed class Hs2TypeNode
    {
        public string Type, Name;
        public int Level, ByteSize, MetaFlag;
        public byte TypeFlags;
        public readonly List<Hs2TypeNode> Children = new List<Hs2TypeNode>();
        /// <summary>An array: children "size" (int) and "data" (the element); serialized as an int count and the elements.</summary>
        public bool IsArray => (TypeFlags & 1) != 0;
        /// <summary>The stream is aligned to 4 bytes after this field.</summary>
        public bool AlignAfter => (MetaFlag & 0x4000) != 0;
        public Hs2TypeNode Child(string name)
        {
            foreach (var c in Children) if (c.Name == name) return c;
            return null;
        }

        int fixedSize = int.MinValue;

        /// <summary>The serialized size of a field that holds no array and no alignment (its declared byte size, checked
        /// against its leaves), else -1: an array of such elements is skipped as count x size.</summary>
        public int FixedSize
        {
            get
            {
                if (fixedSize != int.MinValue) return fixedSize;
                int r = -1;
                if (!IsArray && !AlignAfter && ByteSize > 0)
                {
                    if (Children.Count == 0) r = ByteSize;
                    else
                    {
                        long sum = 0;
                        foreach (var c in Children) { int k = c.FixedSize; if (k < 0) { sum = -1; break; } sum += k; }
                        if (sum == ByteSize) r = ByteSize;
                    }
                }
                fixedSize = r;
                return r;
            }
        }
        public override string ToString() => Type + " " + Name;
    }

    /// <summary>
    /// Random access to one object's bytes (object-relative positions): an array slice, or a reader that fetches windows on
    /// demand (a bundle node through <see cref="Hs2BundleFile.ReadRange"/>: only the blocks a read touches are decompressed).
    /// </summary>
    public sealed class Hs2ObjectBytes
    {
        readonly byte[] data;
        readonly long origin;
        readonly Func<long, int, byte[]> readAt;
        byte[] win;
        long winPos = -1;
        public long Length { get; }
        /// <summary>The cursor (object-relative).</summary>
        public long Pos;

        public Hs2ObjectBytes(byte[] data, long offset, long length) { this.data = data; origin = offset; Length = length; }
        public Hs2ObjectBytes(Func<long, int, byte[]> readAt, long length) { this.readAt = readAt; Length = length; }

        int At(long pos, int n)
        {
            if (pos < 0 || n < 0 || pos + n > Length) throw new Hs2SerializedFileException("object read of " + n + " bytes at " + pos + " past its end (" + Length + ")");
            if (data != null) return (int)(origin + pos);
            if (win == null || pos < winPos || pos + n > winPos + win.Length)
            {
                int want = (int)Math.Min(Length - pos, Math.Max(n, 4096));
                win = readAt(pos, want);
                if (win.Length < n) throw new Hs2SerializedFileException("object truncated at " + pos);
                winPos = pos;
            }
            return (int)(pos - winPos);
        }

        byte[] Buf => data ?? win;

        /// <summary>A little-endian integer of 1, 2, 4 or 8 bytes at the cursor (zero-extended).</summary>
        public long Int(int size)
        {
            int i = At(Pos, size);
            var b = Buf;
            long v = 0;
            for (int k = size - 1; k >= 0; k--) v = v << 8 | b[i + k];
            Pos += size;
            return v;
        }

        public int I32() => (int)Int(4);
        public void Skip(long n) { if (n < 0 || Pos + n > Length) throw new Hs2SerializedFileException("object skip of " + n + " bytes at " + Pos + " past its end (" + Length + ")"); Pos += n; }
        /// <summary>Unity's alignment to 4 (relative to the object's start).</summary>
        public void Align() { long p = (Pos + 3) & ~3L; if (p > Length) throw new Hs2SerializedFileException("object alignment past its end"); Pos = p; }

        /// <summary>Copies <paramref name="n"/> bytes at <paramref name="pos"/> to <paramref name="to"/>.</summary>
        public void CopyTo(long pos, long n, Stream to)
        {
            while (n > 0)
            {
                int k = (int)Math.Min(n, 1 << 20);
                int i = At(pos, k);
                to.Write(Buf, i, k);
                pos += k;
                n -= k;
            }
        }

        public byte[] Bytes(long pos, int n)
        {
            int i = At(pos, n);
            var r = new byte[n];
            Buffer.BlockCopy(Buf, i, r, 0, n);
            return r;
        }
    }

    /// <summary>One field read through a type tree: where it is in the object and what it holds.</summary>
    public sealed class Hs2Field
    {
        public Hs2TypeNode Node;
        /// <summary>Object-relative start; end after the alignment that follows the field.</summary>
        public long Start, End;
        /// <summary>A leaf's value (zero-extended).</summary>
        public long Value;
        /// <summary>An array's element count (-1 for other fields) and the position of its first element.</summary>
        public int Count = -1;
        public long DataStart;
        /// <summary>A compound field's children by name (the first of a name), for the first levels of the tree.</summary>
        public Dictionary<string, Hs2Field> Fields;

        public Hs2Field this[string name] => Fields != null && Fields.TryGetValue(name, out var f) ? f : null;
    }

    /// <summary>
    /// Reads and rewrites objects through their type trees, by structure: a leaf is its byte size, an array an int count and
    /// its elements, a compound field its children in order, and every field with the align flag is followed by alignment
    /// to 4 relative to the object's start (Unity's rule). Strings are arrays of bytes under a "string" field.
    /// </summary>
    public static class Hs2TypeTree
    {
        /// <summary>
        /// The top-level fields of an object (and the fields under compound ones, three levels deep) by name. Arrays of
        /// fixed-size elements are skipped without reading them. <paramref name="stop"/>, when given, ends the walk after the
        /// top-level field it returns true for.
        /// </summary>
        public static Dictionary<string, Hs2Field> Read(Hs2TypeNode root, Hs2ObjectBytes r, Func<Hs2Field, bool> stop = null)
        {
            var top = new Dictionary<string, Hs2Field>(StringComparer.Ordinal);
            r.Pos = 0;
            foreach (var c in root.Children)
            {
                var f = Walk(c, r, top, 1);
                if (stop != null && stop(f)) break;
            }
            return top;
        }

        static Hs2Field Walk(Hs2TypeNode n, Hs2ObjectBytes r, Dictionary<string, Hs2Field> into, int depth)
        {
            var f = new Hs2Field { Node = n, Start = r.Pos };
            if (n.IsArray)
            {
                if (n.Children.Count < 2) throw new Hs2SerializedFileException("array field without an element type: " + n);
                int count = r.I32();
                var e = n.Children[1];
                if (count < 0 || count > r.Length - r.Pos) throw new Hs2SerializedFileException("array " + n.Name + " count " + count + " past the object's end");
                f.Count = count;
                f.DataStart = r.Pos;
                if (e.Children.Count == 0 && e.ByteSize > 0 && !e.AlignAfter) r.Skip((long)count * e.ByteSize);
                else if (e.FixedSize > 0) r.Skip((long)count * e.FixedSize);
                else for (int i = 0; i < count; i++) Walk(e, r, null, depth + 1);
            }
            else if (n.Children.Count == 0)
            {
                if (n.ByteSize != 1 && n.ByteSize != 2 && n.ByteSize != 4 && n.ByteSize != 8) throw new Hs2SerializedFileException("leaf field " + n + " of size " + n.ByteSize);
                f.Value = r.Int(n.ByteSize);
            }
            else
            {
                Dictionary<string, Hs2Field> sub = depth <= 3 ? new Dictionary<string, Hs2Field>(StringComparer.Ordinal) : null;
                f.Fields = sub;
                foreach (var c in n.Children) Walk(c, r, sub, depth + 1);
            }
            if (n.AlignAfter) r.Align();
            f.End = r.Pos;
            if (into != null && n.Name != null && !into.ContainsKey(n.Name)) into[n.Name] = f;
            return f;
        }

        /// <summary>
        /// True when an object's bytes end before its type tree does (a read past the object's end); false when the whole tree
        /// reads. Other layouts the walker does not know count as complete.
        /// </summary>
        public static bool IsTruncated(Hs2TypeNode root, Hs2ObjectBytes r) => ZeroTail(root, r) != 0;

        /// <summary>
        /// How an object's bytes end against its type tree: 0 when the whole tree reads (or the tree has a layout the walker
        /// does not know); n &gt; 0 when the bytes end exactly where a top-level field begins and n zero bytes complete the
        /// object (the missing fields then read as zeros, empty arrays and empty strings; at most <paramref name="max"/>);
        /// -1 when the bytes end inside a field or the completion would need more.
        /// </summary>
        public static int ZeroTail(Hs2TypeNode root, Hs2ObjectBytes r, int max = 64)
        {
            long fieldStart = 0;
            int i = 0;
            try
            {
                r.Pos = 0;
                for (; i < root.Children.Count; i++) { fieldStart = r.Pos; Walk(root.Children[i], r, null, 4); }
                return 0;
            }
            catch (Hs2SerializedFileException e)
            {
                if (e.Message.IndexOf("past its end", StringComparison.Ordinal) < 0 && e.Message.IndexOf("past the object's end", StringComparison.Ordinal) < 0) return 0;
            }
            if (fieldStart != r.Length) return -1;
            // the missing fields over zeros, at the same alignment phase as the object's end
            int phase = (int)(r.Length & 3);
            var z = new Hs2ObjectBytes(new byte[phase + max], 0, phase + max) { Pos = phase };
            try { for (; i < root.Children.Count; i++) Walk(root.Children[i], z, null, 4); }
            catch (Hs2SerializedFileException) { return -1; }
            return z.Pos > phase ? (int)(z.Pos - phase) : -1;
        }

        /// <summary>The text of a "string" field read by <see cref="Read"/> (null when it is not one).</summary>
        public static string StringOf(Hs2Field f, Hs2ObjectBytes r)
        {
            var a = f?["Array"];
            if (a == null || a.Count < 0) return null;
            return Encoding.UTF8.GetString(r.Bytes(a.DataStart, a.Count));
        }

        /// <summary>
        /// The object re-serialized with some fields replaced: <paramref name="set"/> maps a field path ("m_Width",
        /// "m_StreamData/size") to its new value — a long for a leaf, a byte[] for an array of bytes, a string for a "string"
        /// field. Every other field is copied as it is; alignment follows the output's positions, so a replaced array of
        /// another length moves the fields after it correctly. Paths the tree does not have are ignored.
        /// </summary>
        public static byte[] Rewrite(Hs2TypeNode root, Hs2ObjectBytes r, IDictionary<string, object> set)
        {
            var w = new MemoryStream((int)Math.Min(r.Length + 256, int.MaxValue));
            r.Pos = 0;
            foreach (var c in root.Children) Write(c, c.Name, r, w, set);
            return w.ToArray();
        }

        static void Write(Hs2TypeNode n, string path, Hs2ObjectBytes r, MemoryStream w, IDictionary<string, object> set)
        {
            if (set.TryGetValue(path, out var v))
            {
                Walk(n, r, null, 4);   // consume the old value
                switch (v)
                {
                    case long l when n.Children.Count == 0:
                        PutInt(w, l, n.ByteSize);
                        break;
                    case byte[] b when n.IsArray:
                        PutInt(w, b.Length, 4);
                        w.Write(b, 0, b.Length);
                        if (n.Children.Count > 1 && n.Children[1].AlignAfter) Align(w);
                        break;
                    case string s when !n.IsArray && n.Children.Count == 1 && n.Children[0].IsArray:
                        var sb = Encoding.UTF8.GetBytes(s);
                        PutInt(w, sb.Length, 4);
                        w.Write(sb, 0, sb.Length);
                        if (n.Children[0].AlignAfter) Align(w);
                        break;
                    default:
                        throw new Hs2SerializedFileException("cannot set " + path + " (" + n + ") to a " + v.GetType().Name);
                }
                if (n.AlignAfter) Align(w);
                return;
            }
            if (n.IsArray)
            {
                int count = r.I32();
                PutInt(w, count, 4);
                var e = n.Children[1];
                if (count < 0 || count > r.Length - r.Pos) throw new Hs2SerializedFileException("array " + n.Name + " count " + count + " past the object's end");
                if (e.Children.Count == 0 && e.ByteSize > 0 && !e.AlignAfter)
                {
                    long bytes = (long)count * e.ByteSize;
                    r.CopyTo(r.Pos, bytes, w);
                    r.Skip(bytes);
                }
                else for (int i = 0; i < count; i++) Write(e, path + "/" + e.Name, r, w, set);
            }
            else if (n.Children.Count == 0)
            {
                long p = r.Pos;
                r.Int(n.ByteSize);
                r.CopyTo(p, n.ByteSize, w);
            }
            else foreach (var c in n.Children) Write(c, path + "/" + c.Name, r, w, set);
            if (n.AlignAfter) { r.Align(); Align(w); }
        }

        static void Align(MemoryStream w) { while ((w.Length & 3) != 0) w.WriteByte(0); }

        static void PutInt(MemoryStream w, long v, int size) { for (int k = 0; k < size; k++) w.WriteByte((byte)(v >> (8 * k))); }
    }

    /// <summary>One node (file) of a UnityFS bundle: offset and size in the bundle's uncompressed data stream.</summary>
    public struct Hs2BundleNode
    {
        public string Path;
        public long Offset, Size;
        public uint Flags;
    }

    /// <summary>
    /// UnityFS asset bundles (Unity 5.3+; format versions 6–8; package S, Phase 3.3): the header, the blocks info
    /// (uncompressed, LZ4 or LZ4HC) and the node table; <see cref="ReadRange"/> decompresses only the data blocks that cover
    /// the requested range (blocks uncompressed or LZ4 / LZ4HC; LZMA throws: every HS2 map bundle of this install is LZ4HC),
    /// unless the caller asks for LZMA (v0.3 trial 2: <see cref="Hs2Lzma"/>; the thumbnail header check).
    /// The bytes come from a random-access reader (a file, a zip entry stored without compression at its offset, or an
    /// array). Anything unexpected throws <see cref="Hs2SerializedFileException"/>.
    /// </summary>
    public sealed class Hs2BundleFile
    {
        struct Block { public uint USize, CSize; public ushort Flags; public long COffset, UOffset; }

        readonly Func<long, int, byte[]> readAt;
        readonly List<Block> blocks = new List<Block>();
        readonly List<Hs2BundleNode> nodes = new List<Hs2BundleNode>();
        readonly List<int> recordAt = new List<int>();   // each node's record in BlocksInfo
        readonly Dictionary<int, byte[]> cache = new Dictionary<int, byte[]>();

        public string Label { get; }
        public int FormatVersion { get; private set; }
        /// <summary>The header's engine version string (e.g. "5.x.x") and revision (e.g. "2018.4.11f1").</summary>
        public string UnityVersion { get; private set; }
        public string UnityRevision { get; private set; }
        uint archiveFlags;
        public IReadOnlyList<Hs2BundleNode> Nodes => nodes;
        /// <summary>Compression of the data blocks (0 none, 1 LZMA, 2 LZ4, 3 LZ4HC): the first block's, or the blocks info's
        /// when the bundle has no data block.</summary>
        public int DataCompression { get; private set; }
        /// <summary>Count of data blocks decompressed so far (tests, timing).</summary>
        public int BlocksRead { get; private set; }
        /// <summary>The decompressed blocks info (hash, block table, node table) as the file holds it, and where its node table
        /// starts (diagnostics).</summary>
        public byte[] BlocksInfo { get; private set; }
        public int NodeTableAt { get; private set; }
        /// <summary>
        /// Node records found after a name that runs past its first zero byte (a writer that left bytes of a longer name
        /// behind it): the record is the one whose offset continues the previous node. Such bundles load in Unity.
        /// </summary>
        public int NodesRecovered { get; private set; }
        /// <summary>The size of the uncompressed data the blocks hold.</summary>
        public long DataSize { get; private set; }

        /// <param name="readAt">Returns <c>count</c> bytes at bundle offset <c>offset</c> (fewer only at the end).</param>
        /// <param name="length">The bundle's size in bytes.</param>
        /// <param name="lzma">Decode LZMA blocks too (off: they throw, and callers that fall back to Unity keep doing so).</param>
        public Hs2BundleFile(Func<long, int, byte[]> readAt, long length, string label, bool lzma = false)
        {
            this.readAt = readAt ?? throw new ArgumentNullException(nameof(readAt));
            Label = label;
            Lzma = lzma;
            Parse(length);
        }

        /// <summary>LZMA blocks are decoded (the caller asked for it).</summary>
        public bool Lzma { get; }

        /// <summary>A bundle stored in a file from <paramref name="offset"/> on (a whole file, or a stored zip entry).</summary>
        public static Hs2BundleFile OpenFile(string path, long offset, long length, string label = null, bool lzma = false)
        {
            Func<long, int, byte[]> read = (o, n) =>
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    fs.Position = offset + o;
                    var b = new byte[(int)Math.Max(0, Math.Min(n, length - o))];
                    int got = 0;
                    while (got < b.Length) { int k = fs.Read(b, got, b.Length - got); if (k <= 0) break; got += k; }
                    if (got != b.Length) Array.Resize(ref b, got);
                    return b;
                }
            };
            return new Hs2BundleFile(read, length, label ?? path, lzma);
        }

        /// <summary>A bundle held in memory.</summary>
        public static Hs2BundleFile FromBytes(byte[] data, string label, bool lzma = false)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            Func<long, int, byte[]> read = (o, n) =>
            {
                int k = (int)Math.Max(0, Math.Min(n, data.LongLength - o));
                var b = new byte[k];
                if (k > 0) Buffer.BlockCopy(data, (int)o, b, 0, k);
                return b;
            };
            return new Hs2BundleFile(read, data.LongLength, label, lzma);
        }

        static uint BeU32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
        static long BeI64(byte[] b, int o) => (long)((ulong)BeU32(b, o) << 32 | BeU32(b, o + 4));

        static string CStr(byte[] b, ref int o)
        {
            int s = o;
            while (o < b.Length && b[o] != 0) o++;
            if (o >= b.Length) throw new Hs2SerializedFileException("bundle: unterminated string");
            string r = Encoding.UTF8.GetString(b, s, o - s);
            o++;
            return r;
        }

        void Parse(long length)
        {
            var h = readAt(0, (int)Math.Min(length, 4096));
            int o = 0;
            string sig = CStr(h, ref o);
            if (sig != "UnityFS") throw new Hs2SerializedFileException("bundle: signature \"" + sig + "\" is not UnityFS");
            if (o + 4 > h.Length) throw new Hs2SerializedFileException("bundle: header truncated");
            FormatVersion = (int)BeU32(h, o); o += 4;
            if (FormatVersion < 6 || FormatVersion > 8) throw new Hs2SerializedFileException("bundle: format version " + FormatVersion + " is not supported (6-8)");
            UnityVersion = CStr(h, ref o);
            UnityRevision = CStr(h, ref o);
            if (o + 20 > h.Length) throw new Hs2SerializedFileException("bundle: header truncated");
            long size = BeI64(h, o); o += 8;
            uint cInfo = BeU32(h, o), uInfo = BeU32(h, o + 4), flags = BeU32(h, o + 8); o += 12;
            archiveFlags = flags;
            if (size != length) throw new Hs2SerializedFileException("bundle: header size " + size + " != " + length);
            if (FormatVersion >= 7) o = (o + 15) & ~15;
            bool infoAtEnd = (flags & 0x80) != 0;
            long infoPos = infoAtEnd ? length - cInfo : o;
            var ci = readAt(infoPos, (int)cInfo);
            if (ci.Length != cInfo) throw new Hs2SerializedFileException("bundle: blocks info truncated");
            var info = Decompress(ci, (int)uInfo, (int)(flags & 0x3F), "blocks info", Lzma);
            long dataStart = infoAtEnd ? o : o + cInfo;
            if ((flags & 0x200) != 0) dataStart = (dataStart + 15) & ~15L;
            int p = 16;   // the uncompressed data hash
            if (info.Length < p + 4) throw new Hs2SerializedFileException("bundle: blocks info truncated");
            int nb = (int)BeU32(info, p); p += 4;
            if (nb < 0 || nb > 1 << 22 || p + 10L * nb + 4 > info.Length) throw new Hs2SerializedFileException("bundle: block count " + nb);
            long cOff = dataStart, uOff = 0;
            for (int i = 0; i < nb; i++)
            {
                var bk = new Block { USize = BeU32(info, p), CSize = BeU32(info, p + 4), Flags = (ushort)(info[p + 8] << 8 | info[p + 9]), COffset = cOff, UOffset = uOff };
                p += 10;
                cOff += bk.CSize;
                uOff += bk.USize;
                blocks.Add(bk);
            }
            if (cOff > length) throw new Hs2SerializedFileException("bundle: blocks extend past the file");
            DataCompression = blocks.Count > 0 ? blocks[0].Flags & 0x3F : (int)(flags & 0x3F);
            BlocksInfo = info;
            NodeTableAt = p;
            DataSize = uOff;
            int nn = (int)BeU32(info, p); p += 4;
            if (nn < 0 || nn > 1 << 20) throw new Hs2SerializedFileException("bundle: node count " + nn);
            for (int i = 0; i < nn; i++)
            {
                if (p + 20 > info.Length) throw new Hs2SerializedFileException("bundle: node table truncated");
                int at = p, rec = p;
                var n = NodeAt(info, ref p);
                if (Outside(n, uOff) && i > 0 && nodes[i - 1].Offset + nodes[i - 1].Size > 0)
                {
                    // the previous name ran past its first zero byte: the record starts where an offset continues the
                    // previous node (nodes lie back to back), at most 64 bytes on
                    long next = nodes[i - 1].Offset + nodes[i - 1].Size;
                    for (int q = at + 1; q <= at + 64 && q + 20 <= info.Length; q++)
                    {
                        if (BeI64(info, q) != next) continue;
                        int p2 = q;
                        Hs2BundleNode m;
                        try { m = NodeAt(info, ref p2); }
                        catch (Hs2SerializedFileException) { continue; }
                        if (Outside(m, uOff)) continue;
                        n = m;
                        p = p2;
                        rec = q;
                        NodesRecovered++;
                        break;
                    }
                }
                if (Outside(n, uOff)) throw new Hs2SerializedFileException("bundle: node " + n.Path + " outside the data");
                nodes.Add(n);
                recordAt.Add(rec);
            }
        }

        static Hs2BundleNode NodeAt(byte[] info, ref int p)
        {
            var n = new Hs2BundleNode { Offset = BeI64(info, p), Size = BeI64(info, p + 8), Flags = BeU32(info, p + 16) };
            p += 20;
            n.Path = CStr(info, ref p);
            return n;
        }

        // no overflow of a garbage table
        static bool Outside(Hs2BundleNode n, long dataSize) => n.Offset < 0 || n.Size < 0 || n.Offset > dataSize || n.Size > dataSize - n.Offset;

        static byte[] Decompress(byte[] src, int usize, int kind, string what, bool lzma)
        {
            switch (kind)
            {
                case 1 when lzma:
                    return Hs2Lzma.Decode(src, usize);
                case 0:
                    if (src.Length != usize) throw new Hs2SerializedFileException("bundle: " + what + " size " + src.Length + " != " + usize);
                    return src;
                case 2:
                case 3:
                    return Hs2Lz4.Decode(src, usize);
                default:
                    throw new Hs2SerializedFileException("bundle: " + what + " compression " + kind + (kind == 1 ? " (LZMA)" : "") + " is not supported");
            }
        }

        byte[] BlockData(int i)
        {
            if (cache.TryGetValue(i, out var d)) return d;
            var bk = blocks[i];
            if (bk.USize > int.MaxValue - 64 || bk.CSize > int.MaxValue - 64) throw new Hs2SerializedFileException("bundle: block " + i + " of " + bk.USize + " bytes is too large to read in memory");
            var c = readAt(bk.COffset, (int)bk.CSize);
            if (c.Length != bk.CSize) throw new Hs2SerializedFileException("bundle: block " + i + " truncated");
            d = Decompress(c, (int)bk.USize, bk.Flags & 0x3F, "block " + i, Lzma);
            if (cache.Count > 64) cache.Clear();
            cache[i] = d;
            BlocksRead++;
            return d;
        }

        /// <summary>
        /// The whole bundle uncompressed, with its own file names renamed (Phase 4.4 R2, the CAB rewrite): every node name
        /// whose stem (the name up to its first '.') starts with "CAB-" is offered to <paramref name="rename"/>, which returns a
        /// stem of the same length (null or the same stem: kept); the renamed stems are replaced in the node names and wherever
        /// they occur in the data (a serialized file's "archive:/CAB-x/CAB-x.resS" references to its own resource files). The
        /// result keeps the format version, the block sizes and the node offsets; the blocks info sits uncompressed after the
        /// header and every data block is stored uncompressed. <paramref name="renamed"/> maps old stem to new stem and
        /// <paramref name="replaced"/> counts the occurrences replaced in the data. Throws for LZMA blocks or a bundle over 2 GB.
        /// </summary>
        public byte[] Renamed(Func<string, string> rename, out Dictionary<string, string> renamed, out int replaced)
        {
            if (rename == null) throw new ArgumentNullException(nameof(rename));
            renamed = new Dictionary<string, string>(StringComparer.Ordinal);
            replaced = 0;
            foreach (var n in nodes)
            {
                int dot = n.Path.IndexOf('.');
                string stem = dot >= 0 ? n.Path.Substring(0, dot) : n.Path;
                if (!stem.StartsWith("CAB-", StringComparison.Ordinal) || renamed.ContainsKey(stem)) continue;
                string to = rename(stem);
                if (to == null || to == stem) continue;
                if (to.Length != stem.Length || Encoding.UTF8.GetByteCount(to) != Encoding.UTF8.GetByteCount(stem))
                    throw new Hs2SerializedFileException("bundle: a renamed file name must keep its length: " + stem + " -> " + to);
                renamed[stem] = to;
            }

            long total = 0;
            foreach (var bk in blocks) total += bk.USize;
            if (total > int.MaxValue - 4096) throw new Hs2SerializedFileException("bundle: " + total + " bytes are too large to rewrite in memory");
            var data = new byte[total];
            for (int i = 0; i < blocks.Count; i++)
            {
                var bk = blocks[i];
                byte[] d;
                if (!cache.TryGetValue(i, out d))
                {
                    var c = readAt(bk.COffset, (int)bk.CSize);
                    if (c.Length != bk.CSize) throw new Hs2SerializedFileException("bundle: block " + i + " truncated");
                    d = Decompress(c, (int)bk.USize, bk.Flags & 0x3F, "block " + i, Lzma);
                }
                Buffer.BlockCopy(d, 0, data, (int)bk.UOffset, d.Length);
            }
            foreach (var kv in renamed) replaced += ReplaceAll(data, Encoding.UTF8.GetBytes(kv.Key), Encoding.UTF8.GetBytes(kv.Value));

            // blocks info: the data hash (zeros), the blocks (each stored), the nodes with their new names
            var info = new MemoryStream();
            info.Write(new byte[16], 0, 16);
            PutU32(info, (uint)blocks.Count);
            foreach (var bk in blocks)
            {
                PutU32(info, bk.USize);
                PutU32(info, bk.USize);
                ushort f = (ushort)(bk.Flags & ~0x3F);
                info.WriteByte((byte)(f >> 8));
                info.WriteByte((byte)f);
            }
            PutU32(info, (uint)nodes.Count);
            foreach (var n in nodes)
            {
                PutU64(info, n.Offset);
                PutU64(info, n.Size);
                PutU32(info, n.Flags);
                int dot = n.Path.IndexOf('.');
                string stem = dot >= 0 ? n.Path.Substring(0, dot) : n.Path;
                string path = renamed.TryGetValue(stem, out var to) ? to + n.Path.Substring(stem.Length) : n.Path;
                var pb = Encoding.UTF8.GetBytes(path);
                info.Write(pb, 0, pb.Length);
                info.WriteByte(0);
            }
            var infoBytes = info.ToArray();

            // header: signature, format, versions, size, the blocks info's sizes, the flags (no compression, info in front)
            var head = new MemoryStream();
            PutStr(head, "UnityFS");
            PutU32(head, (uint)FormatVersion);
            PutStr(head, UnityVersion ?? "5.x.x");
            PutStr(head, UnityRevision ?? "");
            long sizePos = head.Length;
            PutU64(head, 0);
            PutU32(head, (uint)infoBytes.Length);
            PutU32(head, (uint)infoBytes.Length);
            PutU32(head, archiveFlags & ~0x3Fu & ~0x80u & ~0x200u);
            if (FormatVersion >= 7) while (head.Length % 16 != 0) head.WriteByte(0);
            long size = head.Length + infoBytes.Length + data.LongLength;
            var r = new byte[size];
            var hb = head.ToArray();
            for (int i = 0; i < 8; i++) hb[sizePos + i] = (byte)(size >> (56 - 8 * i));
            Buffer.BlockCopy(hb, 0, r, 0, hb.Length);
            Buffer.BlockCopy(infoBytes, 0, r, hb.Length, infoBytes.Length);
            Buffer.BlockCopy(data, 0, r, hb.Length + infoBytes.Length, data.Length);
            return r;
        }

        // Replaces every occurrence of <paramref name="from"/> by <paramref name="to"/> (same length) in place; returns the count.
        static int ReplaceAll(byte[] data, byte[] from, byte[] to)
        {
            int n = 0;
            if (from.Length == 0 || from.Length != to.Length) return 0;
            byte first = from[0];
            int i = 0, last = data.Length - from.Length;
            while (i <= last)
            {
                i = Array.IndexOf(data, first, i, last - i + 1);
                if (i < 0) break;
                int k = 1;
                while (k < from.Length && data[i + k] == from[k]) k++;
                if (k == from.Length) { Buffer.BlockCopy(to, 0, data, i, to.Length); n++; i += from.Length; }
                else i++;
            }
            return n;
        }

        static void PutU32(Stream s, uint v) { s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
        static void PutU64(Stream s, long v) { PutU32(s, (uint)((ulong)v >> 32)); PutU32(s, (uint)v); }
        static void PutStr(Stream s, string v) { var b = Encoding.UTF8.GetBytes(v); s.Write(b, 0, b.Length); s.WriteByte(0); }

        /// <summary>The bundle's uncompressed data stream (every block decompressed). Throws over 2 GB.</summary>
        public byte[] AllData()
        {
            long total = 0;
            foreach (var bk in blocks) total += bk.USize;
            if (total > int.MaxValue - 4096) throw new Hs2SerializedFileException("bundle: " + total + " bytes are too large to read in memory");
            var data = new byte[total];
            for (int i = 0; i < blocks.Count; i++)
            {
                var bk = blocks[i];
                if (!cache.TryGetValue(i, out var d))
                {
                    var c = readAt(bk.COffset, (int)bk.CSize);
                    if (c.Length != bk.CSize) throw new Hs2SerializedFileException("bundle: block " + i + " truncated");
                    d = Decompress(c, (int)bk.USize, bk.Flags & 0x3F, "block " + i, Lzma);
                }
                Buffer.BlockCopy(d, 0, data, (int)bk.UOffset, d.Length);
            }
            return data;
        }

        /// <summary>
        /// The bundle with some nodes' bytes replaced (<paramref name="replace"/>: node path → new bytes; <paramref name="data"/>:
        /// <see cref="AllData"/>, possibly patched in place), written uncompressed: the header keeps the format version and the
        /// engine versions; the blocks info sits uncompressed after the header and holds stored blocks of 128 KiB; a node whose
        /// size changes moves the later nodes by a multiple of 16 bytes. Node names, flags and order are kept (a patched bundle
        /// keeps its file names); a node table read past long names is written as the file holds it, with the new offsets and
        /// sizes, and a replacement applies to the first node of its path. <paramref name="names"/> (index = node; null entries
        /// keep the name) writes the table anew with those names; <paramref name="moved"/> (index = node; -1 = the table's)
        /// gives a node's bytes another place in <paramref name="data"/> than the table says (a table whose offset is wrong).
        /// </summary>
        public byte[] WithNodes(byte[] data, IDictionary<string, byte[]> replace, string[] names = null, long[] moved = null)
        {
            long Src(int i) => moved != null && moved[i] >= 0 ? moved[i] : nodes[i].Offset;
            var rep = new byte[nodes.Count][];
            var taken = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < nodes.Count; i++)
                if (replace != null && replace.TryGetValue(nodes[i].Path, out var rb) && taken.Add(nodes[i].Path)) rep[i] = rb;
            var order = new List<int>(nodes.Count);
            for (int i = 0; i < nodes.Count; i++) order.Add(i);
            order.Sort((a, b) => Src(a).CompareTo(Src(b)));
            var offs = new long[nodes.Count];
            var sizes = new long[nodes.Count];
            long shift = 0, prevEnd = 0;
            foreach (int i in order)
            {
                var n = nodes[i];
                if (Src(i) < prevEnd) throw new Hs2SerializedFileException("bundle: nodes overlap");
                offs[i] = Src(i) + shift;
                sizes[i] = rep[i]?.LongLength ?? n.Size;
                shift += ((sizes[i] - n.Size + 15) >> 4) << 4;
                prevEnd = Src(i) + n.Size;
            }
            long total = data.LongLength + shift;

            const int BlockSize = 1 << 17;
            ushort blockFlags = (ushort)((blocks.Count > 0 ? blocks[0].Flags : 0) & ~0x3F);
            var info = new MemoryStream();
            info.Write(new byte[16], 0, 16);
            long nb2 = (total + BlockSize - 1) / BlockSize;
            PutU32(info, (uint)nb2);
            for (long i = 0; i < nb2; i++)
            {
                uint us = (uint)Math.Min(BlockSize, total - i * BlockSize);
                PutU32(info, us);
                PutU32(info, us);
                info.WriteByte((byte)(blockFlags >> 8));
                info.WriteByte((byte)blockFlags);
            }
            if (NodesRecovered > 0 && names == null)
            {
                var table = new byte[BlocksInfo.Length - NodeTableAt];
                Buffer.BlockCopy(BlocksInfo, NodeTableAt, table, 0, table.Length);
                for (int i = 0; i < nodes.Count; i++)
                    for (int k = 0; k < 8; k++)
                    {
                        table[recordAt[i] - NodeTableAt + k] = (byte)(offs[i] >> (56 - 8 * k));
                        table[recordAt[i] - NodeTableAt + 8 + k] = (byte)(sizes[i] >> (56 - 8 * k));
                    }
                info.Write(table, 0, table.Length);
            }
            else
            {
                PutU32(info, (uint)nodes.Count);
                for (int i = 0; i < nodes.Count; i++)
                {
                    PutU64(info, offs[i]);
                    PutU64(info, sizes[i]);
                    PutU32(info, nodes[i].Flags);
                    var pb = Encoding.UTF8.GetBytes(names?[i] ?? nodes[i].Path);
                    info.Write(pb, 0, pb.Length);
                    info.WriteByte(0);
                }
            }
            var infoBytes = info.ToArray();
            var head = new MemoryStream();
            PutStr(head, "UnityFS");
            PutU32(head, (uint)FormatVersion);
            PutStr(head, UnityVersion ?? "5.x.x");
            PutStr(head, UnityRevision ?? "");
            long sizePos = head.Length;
            PutU64(head, 0);
            PutU32(head, (uint)infoBytes.Length);
            PutU32(head, (uint)infoBytes.Length);
            PutU32(head, archiveFlags & ~0x3Fu & ~0x80u & ~0x200u);
            if (FormatVersion >= 7) while (head.Length % 16 != 0) head.WriteByte(0);
            long size = head.Length + infoBytes.Length + total;
            if (size > int.MaxValue - 64) throw new Hs2SerializedFileException("bundle: " + size + " bytes are too large to write in memory");
            var r = new byte[size];
            var hb = head.ToArray();
            for (int i = 0; i < 8; i++) hb[sizePos + i] = (byte)(size >> (56 - 8 * i));
            Buffer.BlockCopy(hb, 0, r, 0, hb.Length);
            Buffer.BlockCopy(infoBytes, 0, r, hb.Length, infoBytes.Length);
            int d0 = hb.Length + infoBytes.Length;
            long lastEnd = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                if (rep[i] != null) Buffer.BlockCopy(rep[i], 0, r, d0 + (int)offs[i], rep[i].Length);
                else Buffer.BlockCopy(data, (int)Src(i), r, d0 + (int)offs[i], (int)n.Size);
                lastEnd = Math.Max(lastEnd, Src(i) + n.Size);
            }
            if (lastEnd < data.LongLength) Buffer.BlockCopy(data, (int)lastEnd, r, d0 + (int)(lastEnd + shift), (int)(data.LongLength - lastEnd));
            return r;
        }

        /// <summary>The node named <paramref name="path"/> (ordinal), else null.</summary>
        public Hs2BundleNode? Find(string path)
        {
            foreach (var n in nodes) if (n.Path == path) return n;
            return null;
        }

        /// <summary><paramref name="count"/> bytes at <paramref name="offset"/> of a node (clamped to the node's end).</summary>
        public byte[] ReadRange(Hs2BundleNode node, long offset, int count)
        {
            if (offset < 0 || offset > node.Size) throw new Hs2SerializedFileException("bundle: read outside node " + node.Path);
            count = (int)Math.Min(count, node.Size - offset);
            var r = new byte[count];
            if (count == 0) return r;
            long u = node.Offset + offset;
            int done = 0;
            int bi = BlockAt(u);
            while (done < count)
            {
                if (bi >= blocks.Count) throw new Hs2SerializedFileException("bundle: read past the last block");
                var bk = blocks[bi];
                int s = (int)(u + done - bk.UOffset);
                if ((bk.Flags & 0x3F) == 0 && bk.CSize == bk.USize)
                {
                    // a stored block: read only the requested range (an uncompressed bundle may be one block of the whole data)
                    int k = (int)Math.Min(count - done, bk.USize - s);
                    var part = readAt(bk.COffset + s, k);
                    if (part.Length != k) throw new Hs2SerializedFileException("bundle: block " + bi + " truncated");
                    Buffer.BlockCopy(part, 0, r, done, k);
                    done += k;
                    bi++;
                    continue;
                }
                var d = BlockData(bi);
                int n = Math.Min(count - done, d.Length - s);
                Buffer.BlockCopy(d, s, r, done, n);
                done += n;
                bi++;
            }
            return r;
        }

        int BlockAt(long u)
        {
            int lo = 0, hi = blocks.Count - 1;
            while (lo <= hi)
            {
                int m = (lo + hi) / 2;
                if (u < blocks[m].UOffset) hi = m - 1;
                else if (u >= blocks[m].UOffset + blocks[m].USize) lo = m + 1;
                else return m;
            }
            throw new Hs2SerializedFileException("bundle: offset " + u + " in no block");
        }

        /// <summary>A seekable read-only stream over a node (for <see cref="Hs2SerializedFile.FromStream"/>).</summary>
        public Stream OpenNode(Hs2BundleNode node) => new NodeStream(this, node);

        sealed class NodeStream : Stream
        {
            readonly Hs2BundleFile b;
            readonly Hs2BundleNode n;
            long pos;
            public NodeStream(Hs2BundleFile b, Hs2BundleNode n) { this.b = b; this.n = n; }
            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => n.Size;
            public override long Position { get => pos; set => pos = value; }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (pos >= n.Size || count <= 0) return 0;
                var d = b.ReadRange(n, pos, count);
                Buffer.BlockCopy(d, 0, buffer, offset, d.Length);
                pos += d.Length;
                return d.Length;
            }
            public override long Seek(long offset, SeekOrigin origin)
            {
                pos = origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? pos + offset : n.Size + offset;
                return pos;
            }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }

    /// <summary>LZ4 block-format decoder (the LZ4 and LZ4HC blocks of UnityFS bundles).</summary>
    public static class Hs2Lz4
    {
        /// <summary>Decodes one LZ4 block into exactly <paramref name="outSize"/> bytes; throws on malformed input.</summary>
        public static byte[] Decode(byte[] src, int outSize)
        {
            var dst = new byte[outSize];
            int s = 0, d = 0;
            while (s < src.Length)
            {
                int token = src[s++];
                int lit = token >> 4;
                if (lit == 15)
                {
                    int b;
                    do { if (s >= src.Length) throw Bad(); b = src[s++]; lit += b; } while (b == 255);
                }
                if (s + lit > src.Length || d + lit > outSize) throw Bad();
                Buffer.BlockCopy(src, s, dst, d, lit);
                s += lit;
                d += lit;
                if (s >= src.Length) break;   // the last sequence has literals only
                if (s + 2 > src.Length) throw Bad();
                int off = src[s] | src[s + 1] << 8;
                s += 2;
                if (off == 0 || off > d) throw Bad();
                int len = token & 15;
                if (len == 15)
                {
                    int b;
                    do { if (s >= src.Length) throw Bad(); b = src[s++]; len += b; } while (b == 255);
                }
                len += 4;
                if (d + len > outSize) throw Bad();
                int m = d - off;
                if (off >= len) { Buffer.BlockCopy(dst, m, dst, d, len); d += len; }
                else for (int i = 0; i < len; i++) dst[d++] = dst[m++];
            }
            if (d != outSize) throw new Hs2SerializedFileException("lz4: decoded " + d + " of " + outSize + " bytes");
            return dst;
        }

        static Hs2SerializedFileException Bad() => new Hs2SerializedFileException("lz4: malformed block");
    }
}
