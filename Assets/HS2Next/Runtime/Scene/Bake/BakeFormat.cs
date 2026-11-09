// The bytes of a bake (Unity-free). A bake is stored as blobs, each a `res/<sha256>.bin` entry of a scene and a file of the store: one main blob
// (the canonical JSON of the bake: node tree, FK list, materials, renderers, insides) and one blob per mesh and per texture. Every blob starts with a
// 16-byte header, all little-endian: the magic (`HNBK` main, `HNBG` mesh, `HNBT` texture), the format version (u16, 1), flags (u16, 0), the length
// of the body uncompressed (u64); then the body as raw deflate, written at the fastest level. A reader refuses a wrong magic, a version above its own,
// a flag it does not know, a body whose inflated length differs or whose content does not parse (bake.blobCorrupt). A blob's name is the SHA-256 of
// its bytes, so equal blobs are stored once and a texture or a mesh is shared between bakes.
//
// The main's JSON is written by the save format's canonical writer (floats as "R", members in the order below, unknown members kept and written
// back). The mesh body: the vertex count, the flags of the optional channels, the UV dimension of channels 0-7, then positions, normals, tangents,
// the UV channels, colours, weights (four (bone, weight) pairs a vertex), the bind poses (column-major) and the sub-meshes with their topology and
// indices. The texture body: width, height, the level count, the stored format's name, the source's format name (information only), then each level
// largest first. Encoding and decoding are pure functions of their input and may run on any thread.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using HS2Next.Save;
using Newtonsoft.Json.Linq;

namespace HS2Next.Scenes.Bake
{
    /// <summary>The three kinds of blob.</summary>
    public enum BakeBlobKind { Main, Mesh, Texture }

    /// <summary>A blob that cannot be read: its code is <see cref="BakeFindings.BlobCorrupt"/>.</summary>
    public sealed class BakeFormatException : Exception
    {
        public string Code => BakeFindings.BlobCorrupt;
        public BakeFormatException(string detail, Exception inner = null) : base(detail, inner) { }
    }

    /// <summary>The blob header and the deflated body; names and hashes.</summary>
    public static class BakeBlob
    {
        /// <summary>The format version this code reads and writes.</summary>
        public const int Version = 1;
        public const int HeaderSize = 16;
        /// <summary>The largest body a reader inflates: the container's limit of one entry (1 GiB).</summary>
        public const long MaxBodyBytes = 1L << 30;

        static readonly byte[][] magics = { Ascii("HNBK"), Ascii("HNBG"), Ascii("HNBT") };

        static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

        /// <summary>The four magic bytes of <paramref name="kind"/>.</summary>
        public static byte[] Magic(BakeBlobKind kind) => (byte[])magics[(int)kind].Clone();

        /// <summary>The kind a blob's magic says, or null when it has none of the three.</summary>
        public static BakeBlobKind? KindOf(byte[] blob)
        {
            if (blob == null || blob.Length < 4) return null;
            for (int k = 0; k < magics.Length; k++)
                if (blob[0] == magics[k][0] && blob[1] == magics[k][1] && blob[2] == magics[k][2] && blob[3] == magics[k][3]) return (BakeBlobKind)k;
            return null;
        }

        /// <summary>A blob of <paramref name="kind"/> holding <paramref name="body"/>, deflated at the fastest level.</summary>
        public static byte[] Encode(BakeBlobKind kind, byte[] body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            return Write(kind, body.LongLength, s => s.Write(body, 0, body.Length));
        }

        /// <summary>A blob of <paramref name="kind"/> whose body of <paramref name="length"/> bytes <paramref name="body"/> writes into the deflate stream
        /// (no copy of the whole body is made). Throws <see cref="InvalidOperationException"/> when it writes another length.</summary>
        public static byte[] Write(BakeBlobKind kind, long length, Action<Stream> body)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            var ms = new MemoryStream();
            ms.Write(magics[(int)kind], 0, 4);
            WriteU16(ms, Version);
            WriteU16(ms, 0);
            for (int i = 0; i < 8; i++) ms.WriteByte((byte)((ulong)length >> (8 * i)));
            using (var z = new DeflateStream(ms, CompressionLevel.Fastest, true))
            {
                var counted = new CountingStream(z);
                body(counted);
                if (counted.Count != length) throw new InvalidOperationException("the body wrote " + counted.Count + " bytes, the header says " + length);
            }
            return ms.ToArray();
        }

        static void WriteU16(Stream s, int v)
        {
            s.WriteByte((byte)v);
            s.WriteByte((byte)(v >> 8));
        }

        /// <summary>Checks the header of a blob of <paramref name="expected"/> kind and returns the body's declared length; throws <see cref="BakeFormatException"/>.</summary>
        public static long ReadHeader(byte[] blob, BakeBlobKind expected)
        {
            if (blob == null || blob.Length < HeaderSize) throw new BakeFormatException("a blob of " + (blob?.Length ?? 0) + " bytes is shorter than its header");
            var kind = KindOf(blob);
            if (kind == null) throw new BakeFormatException("the blob has no bake magic");
            if (kind != expected) throw new BakeFormatException("a " + kind + " blob where a " + expected + " blob is needed");
            int version = blob[4] | blob[5] << 8, flags = blob[6] | blob[7] << 8;
            if (version > Version) throw new BakeFormatException("blob version " + version + " is newer than " + Version);
            if (version < 1) throw new BakeFormatException("blob version " + version + " is not a version");
            if (flags != 0) throw new BakeFormatException("unknown blob flags 0x" + flags.ToString("x4"));
            ulong length = 0;
            for (int i = 0; i < 8; i++) length |= (ulong)blob[8 + i] << (8 * i);
            if (length > (ulong)MaxBodyBytes) throw new BakeFormatException("a body of " + length + " bytes is over the limit of " + MaxBodyBytes);
            return (long)length;
        }

        /// <summary>The body of a blob of <paramref name="expected"/> kind; throws <see cref="BakeFormatException"/>.</summary>
        public static byte[] Decode(byte[] blob, BakeBlobKind expected)
        {
            long length = ReadHeader(blob, expected);
            var body = new byte[length];
            using (var r = Open(blob, expected))
            {
                r.ReadExactly(body, 0, body.Length);
                r.End();
            }
            return body;
        }

        /// <summary>A reader over the inflated body of a blob; call <see cref="BodyReader.End"/> after the last read.</summary>
        internal static BodyReader Open(byte[] blob, BakeBlobKind expected)
        {
            long length = ReadHeader(blob, expected);
            return new BodyReader(new DeflateStream(new MemoryStream(blob, HeaderSize, blob.Length - HeaderSize, false), CompressionMode.Decompress), length);
        }

        // ---------- names ----------

        /// <summary>The lowercase hex SHA-256 of a blob's bytes: its name.</summary>
        public static string Sha256(byte[] blob)
        {
            using (var sha = SHA256.Create()) return JsonText.Hex(sha.ComputeHash(blob));
        }

        /// <summary>True for 64 lowercase hex digits.</summary>
        public static bool IsSha(string s) => JsonText.IsHex(s, 64, false);

        /// <summary>The scene entry of blob <paramref name="sha"/>: <c>res/&lt;sha256&gt;.bin</c>.</summary>
        public static string ResName(string sha)
        {
            if (!IsSha(sha)) throw new ArgumentException("not a SHA-256 name: " + sha, nameof(sha));
            return "res/" + sha + ".bin";
        }

        /// <summary>The SHA-256 of a <c>res/&lt;sha256&gt;.bin</c> name, or null.</summary>
        public static string ShaOf(string resName)
        {
            if (resName == null || !JsonText.IsResName(resName) || !resName.EndsWith(".bin", StringComparison.Ordinal)) return null;
            return resName.Substring(4, 64);
        }

        // ---------- streams ----------

        sealed class CountingStream : Stream
        {
            readonly Stream inner;
            public long Count;
            public CountingStream(Stream inner) { this.inner = inner; }
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => Count; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) { inner.Write(buffer, offset, count); Count += count; }
            public override void WriteByte(byte value) { inner.WriteByte(value); Count++; }
        }
    }

    /// <summary>Reads an inflated body exactly: every read past the declared length, a short body and a body longer than declared are corrupt.</summary>
    internal sealed class BodyReader : IDisposable
    {
        readonly Stream s;
        readonly long length;
        long pos;
        readonly byte[] tmp = new byte[16];

        internal BodyReader(Stream s, long length)
        {
            this.s = s;
            this.length = length;
        }

        public long Remaining => length - pos;

        public void ReadExactly(byte[] buffer, int offset, int count)
        {
            if (count > length - pos) throw new BakeFormatException("the body ends before its content (" + (length - pos) + " bytes left, " + count + " needed)");
            int got = 0;
            try
            {
                while (got < count)
                {
                    int n = s.Read(buffer, offset + got, count - got);
                    if (n <= 0) throw new BakeFormatException("the deflated body is shorter than its declared " + length + " bytes");
                    got += n;
                }
            }
            catch (InvalidDataException e) { throw new BakeFormatException("the deflated body is damaged: " + e.Message, e); }
            catch (IOException e) { throw new BakeFormatException("the deflated body is damaged: " + e.Message, e); }
            pos += count;
        }

        public byte U8() { ReadExactly(tmp, 0, 1); return tmp[0]; }
        public ushort U16() { ReadExactly(tmp, 0, 2); return (ushort)(tmp[0] | tmp[1] << 8); }
        public uint U32() { ReadExactly(tmp, 0, 4); return (uint)(tmp[0] | tmp[1] << 8 | tmp[2] << 16 | tmp[3] << 24); }

        public float[] F32s(long count)
        {
            if (count < 0 || count * 4 > length - pos) throw new BakeFormatException("the body ends before " + count + " numbers");
            var bytes = new byte[count * 4];
            ReadExactly(bytes, 0, bytes.Length);
            var a = new float[count];
            Buffer.BlockCopy(bytes, 0, a, 0, bytes.Length);
            if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("big-endian");
            return a;
        }

        public byte[] Bytes(long count)
        {
            if (count < 0 || count > length - pos) throw new BakeFormatException("the body ends before " + count + " bytes");
            var b = new byte[count];
            ReadExactly(b, 0, b.Length);
            return b;
        }

        public string Str()
        {
            int n = U16();
            var b = Bytes(n);
            try { return new UTF8Encoding(false, true).GetString(b); }
            catch (ArgumentException e) { throw new BakeFormatException("a name is not UTF-8", e); }
        }

        /// <summary>The body must end here: nothing declared is left and the stream holds nothing more.</summary>
        public void End()
        {
            if (pos != length) throw new BakeFormatException((length - pos) + " bytes of the body are left over");
            int extra;
            try { extra = s.Read(tmp, 0, 1); }
            catch (InvalidDataException e) { throw new BakeFormatException("the deflated body is damaged: " + e.Message, e); }
            catch (IOException e) { throw new BakeFormatException("the deflated body is damaged: " + e.Message, e); }
            if (extra > 0) throw new BakeFormatException("the deflated body is longer than its declared " + length + " bytes");
        }

        public void Dispose() => s.Dispose();
    }

    /// <summary>Little-endian writing of the binary bodies.</summary>
    internal static class BodyWriter
    {
        public static void U8(Stream s, int v) => s.WriteByte((byte)v);
        public static void U16(Stream s, int v) { s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8)); }
        public static void U32(Stream s, long v) { s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 24)); }

        public static void F32s(Stream s, float[] a)
        {
            if (a == null || a.Length == 0) return;
            const int chunk = 1 << 16;
            var b = new byte[Math.Min(a.Length, chunk) * 4];
            for (int i = 0; i < a.Length; i += chunk)
            {
                int n = Math.Min(chunk, a.Length - i);
                Buffer.BlockCopy(a, i * 4, b, 0, n * 4);
                s.Write(b, 0, n * 4);
            }
        }

        public static void Str(Stream s, string v)
        {
            var b = Encoding.UTF8.GetBytes(v ?? "");
            if (b.Length > ushort.MaxValue) throw new ArgumentException("a name of " + b.Length + " bytes is too long");
            U16(s, b.Length);
            s.Write(b, 0, b.Length);
        }

        public static int StrLength(string v) => 2 + Encoding.UTF8.GetByteCount(v ?? "");
    }

    // ================================================================ mesh ================================================================

    /// <summary>One sub-mesh: Unity's <c>MeshTopology</c> value and its indices into the vertices.</summary>
    public sealed class BakeSubMesh
    {
        public const byte Triangles = 0, Quads = 2, Lines = 3, LineStrip = 4, Points = 5;

        public byte Topology;
        public int[] Indices = Array.Empty<int>();

        /// <summary>True for a topology this format knows.</summary>
        public static bool IsTopology(byte t) => t == Triangles || t == Quads || t == Lines || t == LineStrip || t == Points;

        /// <summary>The number of indices one primitive of <paramref name="t"/> takes (1 for strips and points).</summary>
        public static int Stride(byte t) => t == Triangles ? 3 : t == Quads ? 4 : t == Lines ? 2 : 1;
    }

    /// <summary>
    /// A mesh as data, in bind space: <see cref="VertexCount"/> vertices with positions (3 floats each), optional normals (3), tangents (4), up to 8 UV
    /// channels of 1 to 4 floats, colours (4 bytes), weights (4 bone indices and 4 weights a vertex, largest first, zero-padded), the bind poses (16 floats
    /// each, column-major) and the sub-meshes. Null arrays are absent channels. Bounds are computed by whoever builds the mesh.
    /// </summary>
    public sealed class BakeMeshData
    {
        public const byte FlagNormals = 1, FlagTangents = 2, FlagColors = 4, FlagWeights = 8;
        const byte KnownFlags = FlagNormals | FlagTangents | FlagColors | FlagWeights;
        /// <summary>UV channels.</summary>
        public const int Channels = 8;

        public int VertexCount;
        public float[] Positions = Array.Empty<float>();
        public float[] Normals;
        public float[] Tangents;
        /// <summary>Per channel the floats of that channel (<see cref="UvDims"/>[c] a vertex), or null.</summary>
        public readonly float[][] Uv = new float[Channels][];
        /// <summary>Per channel its dimension, 0 (none) to 4.</summary>
        public readonly byte[] UvDims = new byte[Channels];
        public byte[] Colors;
        /// <summary>4 bone indices a vertex (into <see cref="BindPoses"/> and the renderer's bones), or null with <see cref="BoneWeights"/>.</summary>
        public ushort[] BoneIndices;
        /// <summary>4 weights a vertex, or null with <see cref="BoneIndices"/>.</summary>
        public float[] BoneWeights;
        /// <summary>16 floats a bind pose, column-major; never null.</summary>
        public float[] BindPoses = Array.Empty<float>();
        public List<BakeSubMesh> SubMeshes = new List<BakeSubMesh>();

        public int BindPoseCount => BindPoses.Length / 16;
        public bool HasWeights => BoneIndices != null && BoneWeights != null;

        /// <summary>Sets channel <paramref name="channel"/> to <paramref name="values"/> of <paramref name="dim"/> floats a vertex (null or dim 0: none).</summary>
        public void SetUv(int channel, int dim, float[] values)
        {
            if (values == null || dim == 0) { Uv[channel] = null; UvDims[channel] = 0; return; }
            if (dim < 1 || dim > 4) throw new ArgumentOutOfRangeException(nameof(dim));
            Uv[channel] = values;
            UvDims[channel] = (byte)dim;
        }

        byte Flags => (byte)((Normals != null ? FlagNormals : 0) | (Tangents != null ? FlagTangents : 0) | (Colors != null ? FlagColors : 0) | (HasWeights ? FlagWeights : 0));

        /// <summary>Why this mesh cannot be written or used (array lengths, dimensions, indices), or null.</summary>
        public string Problem()
        {
            long n = VertexCount;
            if (n < 0) return "a negative vertex count";
            if (Positions == null || Positions.LongLength != 3 * n) return "positions hold " + (Positions?.Length ?? 0) + " floats for " + n + " vertices";
            if (Normals != null && Normals.LongLength != 3 * n) return "normals hold " + Normals.Length + " floats for " + n + " vertices";
            if (Tangents != null && Tangents.LongLength != 4 * n) return "tangents hold " + Tangents.Length + " floats for " + n + " vertices";
            for (int c = 0; c < Channels; c++)
            {
                if (UvDims[c] > 4) return "uv" + c + " has dimension " + UvDims[c];
                if ((UvDims[c] == 0) != (Uv[c] == null)) return "uv" + c + ": dimension " + UvDims[c] + " with " + (Uv[c] == null ? "no" : "some") + " values";
                if (Uv[c] != null && Uv[c].LongLength != UvDims[c] * n) return "uv" + c + " holds " + Uv[c].Length + " floats for " + n + " vertices of dimension " + UvDims[c];
            }
            if (Colors != null && Colors.LongLength != 4 * n) return "colours hold " + Colors.Length + " bytes for " + n + " vertices";
            if ((BoneIndices == null) != (BoneWeights == null)) return "bone indices and weights come together";
            if (BindPoses == null || BindPoses.Length % 16 != 0) return "bind poses hold " + (BindPoses?.Length ?? 0) + " floats, not a multiple of 16";
            if (HasWeights)
            {
                if (BoneIndices.LongLength != 4 * n || BoneWeights.LongLength != 4 * n) return "weights hold " + BoneIndices.Length + " / " + BoneWeights.Length + " entries for " + n + " vertices";
                for (int i = 0; i < BoneIndices.Length; i++)
                    if (BoneWeights[i] != 0f && BoneIndices[i] >= BindPoseCount) return "vertex " + (i / 4) + " weighs on bone " + BoneIndices[i] + " of " + BindPoseCount;
            }
            if (SubMeshes == null) return "no sub-mesh list";
            for (int k = 0; k < SubMeshes.Count; k++)
            {
                var sm = SubMeshes[k];
                if (sm == null || sm.Indices == null) return "sub-mesh " + k + " is null";
                if (!BakeSubMesh.IsTopology(sm.Topology)) return "sub-mesh " + k + " has topology " + sm.Topology;
                int stride = BakeSubMesh.Stride(sm.Topology);
                if (sm.Indices.Length % stride != 0) return "sub-mesh " + k + " holds " + sm.Indices.Length + " indices, not a multiple of " + stride;
                foreach (int i in sm.Indices)
                    if (i < 0 || i >= n) return "sub-mesh " + k + " names vertex " + i + " of " + n;
            }
            return null;
        }

        /// <summary>The body's length in bytes.</summary>
        public long BodyLength()
        {
            long n = VertexCount, len = 4 + 1 + Channels;
            len += 12 * n;
            if (Normals != null) len += 12 * n;
            if (Tangents != null) len += 16 * n;
            for (int c = 0; c < Channels; c++) len += 4L * UvDims[c] * n;
            if (Colors != null) len += 4 * n;
            if (HasWeights) len += 24 * n;
            len += 4 + 4L * BindPoses.Length;
            len += 4;
            foreach (var sm in SubMeshes) len += 1 + 4 + 4L * sm.Indices.Length;
            return len;
        }

        /// <summary>Writes the body into <paramref name="s"/>. Throws <see cref="ArgumentException"/> when <see cref="Problem"/> is not null.</summary>
        public void WriteBody(Stream s)
        {
            string p = Problem();
            if (p != null) throw new ArgumentException("mesh: " + p);
            BodyWriter.U32(s, VertexCount);
            BodyWriter.U8(s, Flags);
            for (int c = 0; c < Channels; c++) BodyWriter.U8(s, UvDims[c]);
            BodyWriter.F32s(s, Positions);
            BodyWriter.F32s(s, Normals);
            BodyWriter.F32s(s, Tangents);
            for (int c = 0; c < Channels; c++) BodyWriter.F32s(s, Uv[c]);
            if (Colors != null) s.Write(Colors, 0, Colors.Length);
            if (HasWeights)
            {
                var b = new byte[24];
                for (int v = 0; v < VertexCount; v++)
                {
                    for (int j = 0; j < 4; j++)
                    {
                        int i = 4 * v + j;
                        ushort bone = BoneIndices[i];
                        b[6 * j] = (byte)bone;
                        b[6 * j + 1] = (byte)(bone >> 8);
                        var w = BitConverter.GetBytes(BoneWeights[i]);
                        Buffer.BlockCopy(w, 0, b, 6 * j + 2, 4);
                    }
                    s.Write(b, 0, 24);
                }
            }
            BodyWriter.U32(s, BindPoseCount);
            BodyWriter.F32s(s, BindPoses);
            BodyWriter.U32(s, SubMeshes.Count);
            foreach (var sm in SubMeshes)
            {
                BodyWriter.U8(s, sm.Topology);
                BodyWriter.U32(s, sm.Indices.Length);
                var b = new byte[4];
                foreach (int i in sm.Indices)
                {
                    b[0] = (byte)i; b[1] = (byte)(i >> 8); b[2] = (byte)(i >> 16); b[3] = (byte)(i >> 24);
                    s.Write(b, 0, 4);
                }
            }
        }

        /// <summary>The uncompressed body.</summary>
        public byte[] ToBody()
        {
            var ms = new MemoryStream((int)Math.Min(BodyLength(), int.MaxValue));
            WriteBody(ms);
            return ms.ToArray();
        }

        /// <summary>The mesh blob (<c>HNBG</c>).</summary>
        public byte[] ToBlob() => BakeBlob.Write(BakeBlobKind.Mesh, BodyLength(), WriteBody);

        /// <summary>Reads a mesh blob; throws <see cref="BakeFormatException"/>.</summary>
        public static BakeMeshData FromBlob(byte[] blob)
        {
            using (var r = BakeBlob.Open(blob, BakeBlobKind.Mesh)) return Read(r);
        }

        /// <summary>Reads an uncompressed body; throws <see cref="BakeFormatException"/>.</summary>
        public static BakeMeshData FromBody(byte[] body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            using (var r = new BodyReader(new MemoryStream(body, false), body.Length)) return Read(r);
        }

        static BakeMeshData Read(BodyReader r)
        {
            var m = new BakeMeshData();
            uint n = r.U32();
            if (n > int.MaxValue / 16) throw new BakeFormatException(n + " vertices are too many");
            m.VertexCount = (int)n;
            byte flags = r.U8();
            if ((flags & ~KnownFlags) != 0) throw new BakeFormatException("unknown mesh flags 0x" + flags.ToString("x2"));
            for (int c = 0; c < Channels; c++)
            {
                m.UvDims[c] = r.U8();
                if (m.UvDims[c] > 4) throw new BakeFormatException("uv" + c + " has dimension " + m.UvDims[c]);
            }
            m.Positions = r.F32s(3L * n);
            if ((flags & FlagNormals) != 0) m.Normals = r.F32s(3L * n);
            if ((flags & FlagTangents) != 0) m.Tangents = r.F32s(4L * n);
            for (int c = 0; c < Channels; c++) if (m.UvDims[c] > 0) m.Uv[c] = r.F32s((long)m.UvDims[c] * n);
            if ((flags & FlagColors) != 0) m.Colors = r.Bytes(4L * n);
            if ((flags & FlagWeights) != 0)
            {
                var raw = r.Bytes(24L * n);
                m.BoneIndices = new ushort[4 * n];
                m.BoneWeights = new float[4 * n];
                for (long i = 0; i < 4L * n; i++)
                {
                    long o = 6 * i;
                    m.BoneIndices[i] = (ushort)(raw[o] | raw[o + 1] << 8);
                    m.BoneWeights[i] = BitConverter.ToSingle(raw, (int)(o + 2));
                }
            }
            uint poses = r.U32();
            if (poses > r.Remaining / 64) throw new BakeFormatException(poses + " bind poses do not fit in the body");
            m.BindPoses = r.F32s(16L * poses);
            uint subs = r.U32();
            if (subs > r.Remaining / 5) throw new BakeFormatException(subs + " sub-meshes do not fit in the body");
            for (uint k = 0; k < subs; k++)
            {
                var sm = new BakeSubMesh { Topology = r.U8() };
                uint count = r.U32();
                if (count > r.Remaining / 4) throw new BakeFormatException("sub-mesh " + k + ": " + count + " indices do not fit in the body");
                var raw = r.Bytes(4L * count);
                sm.Indices = new int[count];
                Buffer.BlockCopy(raw, 0, sm.Indices, 0, raw.Length);
                m.SubMeshes.Add(sm);
            }
            r.End();
            string p = m.Problem();
            if (p != null) throw new BakeFormatException("mesh: " + p);
            return m;
        }
    }

    // ================================================================ texture ================================================================

    /// <summary>
    /// A texture as texels: <see cref="Width"/> × <see cref="Height"/>, the stored format by its <c>GraphicsFormat</c> name (e.g. <c>R8G8B8A8_SRGB</c>),
    /// the source's format name (information only) and every stored level, largest first. Sampler state is in the main blob.
    /// </summary>
    public sealed class BakeTextureData
    {
        public int Width, Height;
        public string Format = "";
        public string SourceFormat = "";
        /// <summary>The levels, largest first; 1 to 255 of them.</summary>
        public List<byte[]> Levels = new List<byte[]>();

        static readonly Dictionary<string, int> pixelBytes = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["R8_UNorm"] = 1, ["R8_SRGB"] = 1, ["R8G8_UNorm"] = 2, ["R8G8_SRGB"] = 2,
            ["R8G8B8A8_UNorm"] = 4, ["R8G8B8A8_SRGB"] = 4, ["R8G8B8A8_SNorm"] = 4, ["B8G8R8A8_UNorm"] = 4, ["B8G8R8A8_SRGB"] = 4,
            ["R16_UNorm"] = 2, ["R16_SFloat"] = 2, ["R16G16_SFloat"] = 4, ["R16G16B16A16_UNorm"] = 8, ["R16G16B16A16_SFloat"] = 8,
            ["R32_SFloat"] = 4, ["R32G32_SFloat"] = 8, ["R32G32B32A32_SFloat"] = 16,
            ["A2B10G10R10_UNormPack32"] = 4, ["B10G11R11_UFloatPack32"] = 4, ["E5B9G9R9_UFloatPack32"] = 4,
        };

        /// <summary>The bytes of one texel of <paramref name="format"/> when this format checks its levels' lengths, else 0.</summary>
        public static int BytesPerPixel(string format) => format != null && pixelBytes.TryGetValue(format, out int b) ? b : 0;

        /// <summary>The size of level <paramref name="level"/> of a <paramref name="size"/> texture: max(1, size >> level).</summary>
        public static int LevelSize(int size, int level) => Math.Max(1, size >> level);

        /// <summary>The levels of a full chain of a <paramref name="w"/> × <paramref name="h"/> texture.</summary>
        public static int FullChain(int w, int h)
        {
            int n = 1, s = Math.Max(w, h);
            while (s > 1) { s >>= 1; n++; }
            return n;
        }

        /// <summary>Why this texture cannot be written or used, or null.</summary>
        public string Problem()
        {
            if (Width < 1 || Height < 1) return "size " + Width + " x " + Height;
            if (string.IsNullOrEmpty(Format)) return "no format";
            if (Levels == null || Levels.Count < 1 || Levels.Count > 255) return (Levels?.Count ?? 0) + " levels";
            if (Levels.Count > FullChain(Width, Height)) return Levels.Count + " levels for " + Width + " x " + Height;
            int bpp = BytesPerPixel(Format);
            for (int i = 0; i < Levels.Count; i++)
            {
                if (Levels[i] == null) return "level " + i + " is null";
                if (bpp > 0 && Levels[i].LongLength != (long)bpp * LevelSize(Width, i) * LevelSize(Height, i))
                    return "level " + i + " holds " + Levels[i].Length + " bytes, " + Format + " at " + LevelSize(Width, i) + " x " + LevelSize(Height, i) + " needs " + (long)bpp * LevelSize(Width, i) * LevelSize(Height, i);
            }
            return null;
        }

        /// <summary>The body's length in bytes.</summary>
        public long BodyLength()
        {
            long len = 4 + 4 + 1 + BodyWriter.StrLength(Format) + BodyWriter.StrLength(SourceFormat);
            foreach (var l in Levels) len += 4 + l.LongLength;
            return len;
        }

        /// <summary>Writes the body into <paramref name="s"/>. Throws <see cref="ArgumentException"/> when <see cref="Problem"/> is not null.</summary>
        public void WriteBody(Stream s)
        {
            string p = Problem();
            if (p != null) throw new ArgumentException("texture: " + p);
            BodyWriter.U32(s, Width);
            BodyWriter.U32(s, Height);
            BodyWriter.U8(s, Levels.Count);
            BodyWriter.Str(s, Format);
            BodyWriter.Str(s, SourceFormat);
            foreach (var l in Levels)
            {
                BodyWriter.U32(s, l.Length);
                s.Write(l, 0, l.Length);
            }
        }

        /// <summary>The uncompressed body.</summary>
        public byte[] ToBody()
        {
            var ms = new MemoryStream((int)Math.Min(BodyLength(), int.MaxValue));
            WriteBody(ms);
            return ms.ToArray();
        }

        /// <summary>The texture blob (<c>HNBT</c>), deflated straight from the levels (no copy of the whole body is made).</summary>
        public byte[] ToBlob() => BakeBlob.Write(BakeBlobKind.Texture, BodyLength(), WriteBody);

        /// <summary>The texture blob of these levels without building a <see cref="BakeTextureData"/> first.</summary>
        public static byte[] Encode(int width, int height, string format, string sourceFormat, IReadOnlyList<byte[]> levels) =>
            new BakeTextureData { Width = width, Height = height, Format = format, SourceFormat = sourceFormat ?? "", Levels = new List<byte[]>(levels) }.ToBlob();

        /// <summary>Reads a texture blob, inflating each level straight into its array; throws <see cref="BakeFormatException"/>.</summary>
        public static BakeTextureData FromBlob(byte[] blob)
        {
            using (var r = BakeBlob.Open(blob, BakeBlobKind.Texture)) return Read(r);
        }

        /// <summary>Reads an uncompressed body; throws <see cref="BakeFormatException"/>.</summary>
        public static BakeTextureData FromBody(byte[] body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            using (var r = new BodyReader(new MemoryStream(body, false), body.Length)) return Read(r);
        }

        static BakeTextureData Read(BodyReader r)
        {
            var t = new BakeTextureData();
            uint w = r.U32(), h = r.U32();
            if (w < 1 || h < 1 || w > 1 << 16 || h > 1 << 16) throw new BakeFormatException("size " + w + " x " + h);
            t.Width = (int)w;
            t.Height = (int)h;
            int levels = r.U8();
            t.Format = r.Str();
            t.SourceFormat = r.Str();
            for (int i = 0; i < levels; i++)
            {
                uint len = r.U32();
                if (len > r.Remaining) throw new BakeFormatException("level " + i + " of " + len + " bytes does not fit in the body");
                t.Levels.Add(r.Bytes(len));
            }
            r.End();
            string p = t.Problem();
            if (p != null) throw new BakeFormatException("texture: " + p);
            return t;
        }
    }

    // ================================================================ main ================================================================

    /// <summary>Value sets of the main's members.</summary>
    public static class BakeKinds
    {
        public const string Chara = "chara", Parts = "parts", Item = "item", Piece = "piece", Imported = "imported";
        public static readonly string[] Kinds = { Chara, Parts, Item, Piece, Imported };
        /// <summary><c>source.kind</c>.</summary>
        public const string SourceChara = "chara", SourceItem = "item", SourceBake = "bake";
        public static readonly string[] SourceKinds = { SourceChara, SourceItem, SourceBake };
        /// <summary><c>origin</c>.</summary>
        public const string OriginChara = "chara", OriginCentre = "centre", OriginCut = "cut", OriginItem = "item";
        public static readonly string[] Origins = { OriginChara, OriginCentre, OriginCut, OriginItem };
        /// <summary><c>renderers[].inside</c>.</summary>
        public const string InsideNone = "none", InsideFill = "fill", InsideSection = "section";
        public static readonly string[] Insides = { InsideNone, InsideFill, InsideSection };

        /// <summary>The main's <c>format</c>.</summary>
        public const string Format = "hs2next.bake";
        /// <summary>The main's <c>version</c> this code reads and writes.</summary>
        public const int Version = 1;
    }

    /// <summary>`source`: what was baked. For a character its name and sex; for an item also its row (ref, group, category).</summary>
    public sealed class BakeMainSource : DocObject
    {
        public string Kind = BakeKinds.SourceChara;
        public string Name = "";
        /// <summary>The character's sex (0 male, 1 female); null = not a character (written only when set).</summary>
        public int? Sex;
        /// <summary>An item's row; null = none (written only when set).</summary>
        public string Ref;
        public int? Group, Category;

        internal override void ReadMembers(DocReader r)
        {
            Kind = r.EnumStr("kind", Kind, BakeKinds.SourceKinds);
            Name = r.Str("name", Name);
            Sex = r.Has("sex") ? r.Int("sex") : (int?)null;
            Ref = r.Has("ref") ? r.Str("ref") : null;
            Group = r.Has("group") ? r.Int("group") : (int?)null;
            Category = r.Has("category") ? r.Int("category") : (int?)null;
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "kind", Kind);
            ReqStr(w, "name", Name);
            if (Sex.HasValue) w.Int("sex", Sex.Value);
            if (Ref != null) w.Str("ref", Ref);
            if (Group.HasValue) w.Int("group", Group.Value);
            if (Category.HasValue) w.Int("category", Category.Value);
        }
    }

    /// <summary>`nodes[]`: one transform of the baked tree, parents first; its local pose at the bake.</summary>
    public sealed class BakeMainNode : DocObject
    {
        public string Name = "";
        /// <summary>The parent's index, -1 for the root.</summary>
        public int Parent = -1;
        public Vec3 P = Vec3.Zero;
        public Quat4 R = Quat4.Identity;
        public Vec3 S = Vec3.One;
        public bool Active = true;
        public int Layer;

        internal override void ReadMembers(DocReader r)
        {
            Name = r.Str("name");
            Parent = r.Int("parent");
            P = r.V3("p", P);
            R = r.Quat("r", R);
            S = r.V3("s", S);
            Active = r.Bool("active", Active);
            Layer = r.Int("layer", Layer);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "name", Name);
            w.Int("parent", Parent);
            w.V3("p", P);
            w.Quat("r", R);
            w.V3("s", S);
            w.Bool("active", Active);
            w.Int("layer", Layer);
        }
    }

    /// <summary>`meshes[]`: a mesh blob and the mesh's name.</summary>
    public sealed class BakeMainMesh : DocObject
    {
        /// <summary>The blob's SHA-256 (64 lowercase hex digits).</summary>
        public string Blob;
        public string Name = "";

        internal override void ReadMembers(DocReader r)
        {
            Blob = BakeMain.ReadSha(r, "blob");
            Name = r.Str("name", Name);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            BakeMain.WriteSha(w, "blob", Blob);
            ReqStr(w, "name", Name);
        }
    }

    /// <summary>`textures[]`: a texture blob, the texture's name and its sampler state (Unity's wrap modes per axis, filter mode, anisotropy, mip bias).</summary>
    public sealed class BakeMainTexture : DocObject
    {
        public string Blob;
        public string Name = "";
        /// <summary>Wrap mode of U and V (0 repeat, 1 clamp, 2 mirror, 3 mirror once).</summary>
        public int[] Wrap = { 0, 0 };
        /// <summary>0 point, 1 bilinear, 2 trilinear.</summary>
        public int Filter = 1;
        public int Aniso = 1;
        public float Bias;

        internal override void ReadMembers(DocReader r)
        {
            Blob = BakeMain.ReadSha(r, "blob");
            Name = r.Str("name", Name);
            Wrap = r.IntArray("wrap", Wrap, 2);
            Filter = r.Int("filter", Filter);
            Aniso = r.Int("aniso", Aniso);
            Bias = r.F32("bias", Bias);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            BakeMain.WriteSha(w, "blob", Blob);
            ReqStr(w, "name", Name);
            Ints(w, "wrap", Wrap, 2);
            w.Int("filter", Filter);
            w.Int("aniso", Aniso);
            w.F32("bias", Bias);
        }
    }

    /// <summary>The type of a material value.</summary>
    public enum BakePropType { Float, Int, Color, Vector, Texture, Unknown }

    /// <summary>
    /// One value of a material, typed by its one member: <c>f</c> a float or range, <c>i</c> an integer, <c>c</c> a colour as stored, <c>v</c> a vector, <c>t</c> a
    /// texture index into <c>textures</c> (-1 none) with <c>st</c> (scale x, y, offset x, y). An object with none of them is a newer writer's type, kept as read.
    /// </summary>
    public sealed class BakeMainProp : DocObject
    {
        public BakePropType Type = BakePropType.Float;
        public float F;
        public int I;
        /// <summary>The colour (<c>c</c>) or the vector (<c>v</c>).</summary>
        public Vec4 V;
        /// <summary>The texture's index into <c>textures</c>, -1 none.</summary>
        public int T = -1;
        public Vec4 St = new Vec4(1f, 1f, 0f, 0f);

        public static BakeMainProp Float(float f) => new BakeMainProp { Type = BakePropType.Float, F = f };
        public static BakeMainProp Integer(int i) => new BakeMainProp { Type = BakePropType.Int, I = i };
        public static BakeMainProp Color(Vec4 c) => new BakeMainProp { Type = BakePropType.Color, V = c };
        public static BakeMainProp Vector(Vec4 v) => new BakeMainProp { Type = BakePropType.Vector, V = v };
        public static BakeMainProp Texture(int index, Vec4 st) => new BakeMainProp { Type = BakePropType.Texture, T = index, St = st };

        internal override void ReadMembers(DocReader r)
        {
            int n = (r.Has("f") ? 1 : 0) + (r.Has("i") ? 1 : 0) + (r.Has("c") ? 1 : 0) + (r.Has("v") ? 1 : 0) + (r.Has("t") ? 1 : 0);
            if (n > 1) throw new DocTypeException(r.Context, r.Object, r.Path, "a material value holds more than one of f, i, c, v, t");
            if (r.Has("f")) { Type = BakePropType.Float; F = r.F32("f"); }
            else if (r.Has("i")) { Type = BakePropType.Int; I = r.Int("i"); }
            else if (r.Has("c")) { Type = BakePropType.Color; V = r.V4("c"); }
            else if (r.Has("v")) { Type = BakePropType.Vector; V = r.V4("v"); }
            else if (r.Has("t")) { Type = BakePropType.Texture; T = r.Int("t"); St = r.V4("st", St); }
            else Type = BakePropType.Unknown;
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            switch (Type)
            {
                case BakePropType.Float: w.F32("f", F); break;
                case BakePropType.Int: w.Int("i", I); break;
                case BakePropType.Color: w.V4("c", V); break;
                case BakePropType.Vector: w.V4("v", V); break;
                case BakePropType.Texture: w.Int("t", T); w.V4("st", St); break;
            }
        }
    }

    /// <summary>`materials[]`: a material as its shader's name and every property that shader declares, typed, with its keywords, queue, disabled passes and
    /// render-type tag; <see cref="Inside"/> the flat colour of an inside copy of skin (null otherwise).</summary>
    public sealed class BakeMainMaterial : DocObject
    {
        public string Name = "";
        public string Shader = "";
        /// <summary>The render queue (-1: the shader's).</summary>
        public int Queue = -1;
        public List<string> Keywords = new List<string>();
        public List<string> PassesOff = new List<string>();
        public string RenderType = "";
        /// <summary>By property name, written in ordinal order; never null.</summary>
        public SortedDictionary<string, BakeMainProp> Props = new SortedDictionary<string, BakeMainProp>(StringComparer.Ordinal);
        /// <summary>RGB of an inside copy of skin; null = none (written only when set).</summary>
        public float[] Inside;

        internal override void ReadMembers(DocReader r)
        {
            Name = r.Str("name", Name);
            Shader = r.Str("shader");
            Queue = r.Int("queue", Queue);
            Keywords = new List<string>(r.StrArray("keywords", Keywords.ToArray()));
            PassesOff = new List<string>(r.StrArray("passesOff", PassesOff.ToArray()));
            RenderType = r.Str("renderType", RenderType);
            Props = r.Map("props", v =>
            {
                var p = new BakeMainProp();
                p.ReadFrom(v.AsObj());
                return p;
            });
            Inside = r.Has("inside") ? r.F32Array("inside", null, 3) : null;
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "name", Name);
            ReqStr(w, "shader", Shader);
            w.Int("queue", Queue);
            w.Name("keywords");
            CheckLength(w, Keywords, -1);
            w.StrArray(Keywords);
            w.Name("passesOff");
            CheckLength(w, PassesOff, -1);
            w.StrArray(PassesOff);
            ReqStr(w, "renderType", RenderType);
            w.Name("props");
            w.StartObject();
            foreach (var kv in Props)
            {
                if (kv.Value == null) throw new SaveFormatException(SaveErrorCode.BadDocument, "material value " + kv.Key + " is null", w.Entry, w.Path);
                kv.Value.WriteTo(w, kv.Key);
            }
            w.EndObject();
            if (Inside != null) F32s(w, "inside", Inside, 3);
        }
    }

    /// <summary>`renderers[]`: a renderer of the bake, by indices into <c>nodes</c>, <c>meshes</c> and <c>materials</c>.</summary>
    public sealed class BakeMainRenderer : DocObject
    {
        public int Node;
        public bool Skinned;
        /// <summary>Index into <c>meshes</c>, -1 none.</summary>
        public int Mesh = -1;
        public int[] Materials = Array.Empty<int>();
        /// <summary>The skinned renderer's bones, indices into <c>nodes</c>.</summary>
        public int[] Bones = Array.Empty<int>();
        /// <summary>The root bone, -1 none.</summary>
        public int Root = -1;
        /// <summary>Centre and size of the skinned bounds as the character had them.</summary>
        public float[] Bounds = new float[6];
        /// <summary>Unity's shadow casting mode (0 off, 1 on, 2 two-sided, 3 shadows only).</summary>
        public int Shadow = 1;
        public bool Receive = true;
        /// <summary>Unity's light-probe usage (1 blend probes) and reflection-probe usage (1 blend probes).</summary>
        public int LightProbe = 1, ReflProbe = 1;
        /// <summary>The probe anchor, an index into <c>nodes</c>, -1 none.</summary>
        public int Anchor = -1;
        public bool UpdateOffscreen;
        /// <summary>Skin weights quality (0 auto).</summary>
        public int Quality;
        /// <summary>The part key, "" none.</summary>
        public string Part = "";
        public bool Member;
        /// <summary>none, fill or section.</summary>
        public string Inside = BakeKinds.InsideNone;

        internal override void ReadMembers(DocReader r)
        {
            Node = r.Int("node");
            Skinned = r.Bool("skinned", Skinned);
            Mesh = r.Int("mesh", Mesh);
            Materials = r.IntArray("materials", Materials);
            Bones = r.IntArray("bones", Bones);
            Root = r.Int("root", Root);
            Bounds = r.F32Array("bounds", Bounds, 6);
            Shadow = r.Int("shadow", Shadow);
            Receive = r.Bool("receive", Receive);
            LightProbe = r.Int("lightProbe", LightProbe);
            ReflProbe = r.Int("reflProbe", ReflProbe);
            Anchor = r.Int("anchor", Anchor);
            UpdateOffscreen = r.Bool("updateOffscreen", UpdateOffscreen);
            Quality = r.Int("quality", Quality);
            Part = r.Str("part", Part);
            Member = r.Bool("member", Member);
            Inside = r.EnumStr("inside", Inside, BakeKinds.Insides);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Int("node", Node);
            w.Bool("skinned", Skinned);
            w.Int("mesh", Mesh);
            Ints(w, "materials", Materials, -1);
            Ints(w, "bones", Bones, -1);
            w.Int("root", Root);
            F32s(w, "bounds", Bounds, 6);
            w.Int("shadow", Shadow);
            w.Bool("receive", Receive);
            w.Int("lightProbe", LightProbe);
            w.Int("reflProbe", ReflProbe);
            w.Int("anchor", Anchor);
            w.Bool("updateOffscreen", UpdateOffscreen);
            w.Int("quality", Quality);
            ReqStr(w, "part", Part);
            w.Bool("member", Member);
            ReqStr(w, "inside", Inside);
        }
    }

    /// <summary>`insides`: whether hollow parts get inside copies, and a piece's section colour and own-material switch.</summary>
    public sealed class BakeMainInsides : DocObject
    {
        public bool Fill;
        public Vec3 Section = CutSectionColors.DarkRed;
        public bool OwnInside = true;

        internal override void ReadMembers(DocReader r)
        {
            Fill = r.Bool("fill", Fill);
            Section = r.V3("section", Section);
            OwnInside = r.Bool("ownInside", OwnInside);
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            w.Bool("fill", Fill);
            w.V3("section", Section);
            w.Bool("ownInside", OwnInside);
        }
    }

    /// <summary>
    /// The main blob's JSON: the bake's identity, kind, time and source, its parts (null = everything drawn), origin, node tree, FK bones, joint-correction
    /// categories (null = none), meshes, textures, materials, renderers and insides. Indices are zero-based; -1 is none.
    /// </summary>
    public sealed class BakeMain : DocObject
    {
        /// <summary>The entry name of errors and findings.</summary>
        public const string EntryName = "bake main";

        public string Format = BakeKinds.Format;
        public int Version = BakeKinds.Version;
        /// <summary>The bake's identity (32 lowercase hex digits), made at baking, kept through renames.</summary>
        public string Id;
        public string Kind = BakeKinds.Chara;
        public DateTime Created = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public BakeMainSource Source = new BakeMainSource();
        /// <summary>The parts baked; null = everything that was drawn (written only when set).</summary>
        public List<string> Parts;
        public string Origin = BakeKinds.OriginChara;
        public List<BakeMainNode> Nodes = new List<BakeMainNode>();
        /// <summary>The item's FK bones, by name.</summary>
        public List<string> Fk = new List<string>();
        /// <summary>The 8 joint-correction categories; null = none (written only when set).</summary>
        public bool[] Correction;
        public List<BakeMainMesh> Meshes = new List<BakeMainMesh>();
        public List<BakeMainTexture> Textures = new List<BakeMainTexture>();
        public List<BakeMainMaterial> Materials = new List<BakeMainMaterial>();
        public List<BakeMainRenderer> Renderers = new List<BakeMainRenderer>();
        public BakeMainInsides Insides = new BakeMainInsides();

        internal static string ReadSha(DocReader r, string name)
        {
            var v = r.ValueReq(name);
            string s = v.AsStr();
            if (!BakeBlob.IsSha(s)) throw new DocTypeException(r.Context, v.Token, v.Path, "expected a SHA-256 (64 lowercase hex digits), found \"" + s + "\"");
            return s;
        }

        internal static void WriteSha(CanonicalJsonWriter w, string name, string sha)
        {
            w.Name(name);
            if (!BakeBlob.IsSha(sha)) throw new SaveFormatException(SaveErrorCode.BadDocument, "not a SHA-256 name: \"" + sha + "\"", w.Entry, w.Path);
            w.Str(sha);
        }

        internal override void ReadMembers(DocReader r)
        {
            Format = r.Str("format");
            if (Format != BakeKinds.Format) throw new DocTypeException(r.Context, r.Object, r.Path, "format \"" + Format + "\" is not " + BakeKinds.Format);
            Version = r.Int("version");
            if (Version > BakeKinds.Version) throw new DocTypeException(r.Context, r.Object, r.Path, "main version " + Version + " is newer than " + BakeKinds.Version);
            if (Version < 1) throw new DocTypeException(r.Context, r.Object, r.Path, "main version " + Version + " is not a version");
            Id = r.Id("id");
            Kind = r.EnumStr("kind", Kind, BakeKinds.Kinds);
            Created = r.Time("created");
            Source = ReadNew<BakeMainSource>(r.ObjReq("source"));
            Parts = r.Has("parts") ? new List<string>(r.StrArray("parts", null)) : null;
            Origin = r.EnumStr("origin", Origin, BakeKinds.Origins);
            Nodes = ReadList<BakeMainNode>(r, "nodes");
            Fk = new List<string>(r.StrArray("fk", Fk.ToArray()));
            Correction = r.Has("correction") ? r.BoolArray("correction", null, 8) : null;
            Meshes = ReadList<BakeMainMesh>(r, "meshes");
            Textures = ReadList<BakeMainTexture>(r, "textures");
            Materials = ReadList<BakeMainMaterial>(r, "materials");
            Renderers = ReadList<BakeMainRenderer>(r, "renderers");
            Insides.ReadFrom(r.Obj("insides"));
        }

        internal override void WriteMembers(CanonicalJsonWriter w)
        {
            ReqStr(w, "format", Format);
            w.Int("version", Version);
            ReqId(w, "id", Id);
            ReqStr(w, "kind", Kind);
            w.Time("created", Created);
            w.Name("source");
            if (Source == null) throw new SaveFormatException(SaveErrorCode.BadDocument, "required object is null", w.Entry, w.Path);
            Source.WriteTo(w);
            if (Parts != null) { w.Name("parts"); w.StrArray(Parts); }
            ReqStr(w, "origin", Origin);
            WriteList(w, "nodes", Nodes);
            w.Name("fk");
            CheckLength(w, Fk, -1);
            w.StrArray(Fk);
            if (Correction != null) Bools(w, "correction", Correction, 8);
            WriteList(w, "meshes", Meshes);
            WriteList(w, "textures", Textures);
            WriteList(w, "materials", Materials);
            WriteList(w, "renderers", Renderers);
            Insides.WriteTo(w, "insides");
        }

        /// <summary>Why the indices of this main do not hold (a node's parent, a renderer's node, mesh, materials, bones, root or anchor, a texture value's
        /// index), or null.</summary>
        public string Problem()
        {
            int nodes = Nodes.Count;
            for (int i = 0; i < nodes; i++)
            {
                if (Nodes[i] == null) return "nodes[" + i + "] is null";
                int p = Nodes[i].Parent;
                if (i == 0 ? p != -1 : (p < 0 || p >= i)) return "nodes[" + i + "].parent " + p + (i == 0 ? ": the first node is the root" : ": a parent comes before its children");
            }
            for (int i = 0; i < Renderers.Count; i++)
            {
                var r = Renderers[i];
                string at = "renderers[" + i + "]";
                if (r == null) return at + " is null";
                if (r.Node < 0 || r.Node >= nodes) return at + ".node " + r.Node + " of " + nodes;
                if (r.Mesh < -1 || r.Mesh >= Meshes.Count) return at + ".mesh " + r.Mesh + " of " + Meshes.Count;
                foreach (int m in r.Materials ?? Array.Empty<int>()) if (m < 0 || m >= Materials.Count) return at + ".materials names " + m + " of " + Materials.Count;
                foreach (int b in r.Bones ?? Array.Empty<int>()) if (b < 0 || b >= nodes) return at + ".bones names " + b + " of " + nodes;
                if (r.Root < -1 || r.Root >= nodes) return at + ".root " + r.Root + " of " + nodes;
                if (r.Anchor < -1 || r.Anchor >= nodes) return at + ".anchor " + r.Anchor + " of " + nodes;
            }
            for (int i = 0; i < Materials.Count; i++)
            {
                if (Materials[i] == null) return "materials[" + i + "] is null";
                foreach (var kv in Materials[i].Props)
                    if (kv.Value != null && kv.Value.Type == BakePropType.Texture && (kv.Value.T < -1 || kv.Value.T >= Textures.Count))
                        return "materials[" + i + "].props." + kv.Key + " names texture " + kv.Value.T + " of " + Textures.Count;
            }
            return null;
        }

        /// <summary>The SHA-256 of every mesh and texture blob the main names, meshes first, in order, each once: what <c>data</c> lists.</summary>
        public List<string> DataBlobs()
        {
            var l = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in Meshes) if (m?.Blob != null && seen.Add(m.Blob)) l.Add(m.Blob);
            foreach (var t in Textures) if (t?.Blob != null && seen.Add(t.Blob)) l.Add(t.Blob);
            return l;
        }

        /// <summary>The item block naming this main (whose blob is <paramref name="mainSha"/>) and its data, with store row <paramref name="no"/> (null: none).</summary>
        public ItemBake ToItemBake(string mainSha, int? no = null)
        {
            var b = new ItemBake { Id = Id, Main = BakeBlob.ResName(mainSha), No = no };
            foreach (var sha in DataBlobs()) b.Data.Add(BakeBlob.ResName(sha));
            return b;
        }

        // ---------- bytes ----------

        /// <summary>The canonical JSON (UTF-8, trailing LF): the main blob's body.</summary>
        public byte[] ToJsonBytes() => CanonicalJsonWriter.ToDocumentBytes(EntryName, WriteTo);

        /// <summary>The main blob (<c>HNBK</c>).</summary>
        public byte[] ToBlob() => BakeBlob.Encode(BakeBlobKind.Main, ToJsonBytes());

        /// <summary>Reads a main from its JSON; a syntax, type or index error throws <see cref="BakeFormatException"/>. Reader findings (an unknown enum value)
        /// go to <paramref name="findings"/>.</summary>
        public static BakeMain Parse(byte[] json, FindingList findings = null)
        {
            BakeMain m;
            try
            {
                var o = DocReader.Parse(json, EntryName);
                m = new BakeMain();
                m.ReadFrom(new DocReader(o, new DocReadContext(EntryName, false, findings ?? new FindingList())));
            }
            catch (SaveFormatException e) { throw new BakeFormatException("the main does not read: " + e.Message, e); }
            string p = m.Problem();
            if (p != null) throw new BakeFormatException("the main's indices do not hold: " + p);
            return m;
        }

        /// <summary>Reads a main blob; throws <see cref="BakeFormatException"/>.</summary>
        public static BakeMain FromBlob(byte[] blob, FindingList findings = null) => Parse(BakeBlob.Decode(blob, BakeBlobKind.Main), findings);

        /// <summary>A deep copy (a canonical write and read).</summary>
        public BakeMain Clone() => Parse(ToJsonBytes());
    }

    // ================================================================ reading a block's blobs ================================================================

    /// <summary>What a bake block's blobs gave: the main and the bytes of each mesh and texture blob found.</summary>
    public sealed class BakeBlobSet
    {
        public enum Result { Ok, Missing, Corrupt }

        public Result Status;
        /// <summary>The main, when <see cref="Status"/> is Ok.</summary>
        public BakeMain Main;
        /// <summary>The main blob's SHA-256.</summary>
        public string MainSha;
        /// <summary>Bytes by SHA-256 of every mesh and texture blob of the main that was found.</summary>
        public readonly Dictionary<string, byte[]> Blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        /// <summary>The blobs the store supplied to the scene.</summary>
        public int FromStore;
        /// <summary>The blobs the main names that neither the block nor the scene holds.</summary>
        public readonly List<string> Absent = new List<string>();

        /// <summary>The bytes of blob <paramref name="sha"/>, or null when it is absent.</summary>
        public byte[] Get(string sha) => sha != null && Blobs.TryGetValue(sha, out var b) ? b : null;

        /// <summary>
        /// Gathers the blobs of a bake block or a cut's source: the main and every <paramref name="data"/> entry from <paramref name="res"/>; when one
        /// is missing, the store's row with the same <paramref name="id"/> supplies the blobs it holds (added to <paramref name="res"/>); a main or data
        /// entry still missing gives <see cref="Result.Missing"/> (bake.missing), a main that does not read <see cref="Result.Corrupt"/> (bake.blobCorrupt).
        /// Then every blob the main names is taken from <paramref name="res"/>, also one <paramref name="data"/> does not list; one that is not there is
        /// absent (bake.dataMissing). A data entry the main does not name is kept and unused. Findings go to <paramref name="findings"/> at <paramref name="path"/>.
        /// </summary>
        public static BakeBlobSet Gather(string id, string main, IReadOnlyList<string> data, ResStore res, BakeStore store, FindingList findings, string path)
        {
            if (res == null) throw new ArgumentNullException(nameof(res));
            var set = new BakeBlobSet { MainSha = BakeBlob.ShaOf(main) };
            var named = new List<string> { main };
            if (data != null) named.AddRange(data);
            bool missing = false;
            foreach (var n in named) if (n == null || !res.Contains(n)) missing = true;
            if (missing && store != null && id != null)
            {
                foreach (var n in named)
                {
                    if (n == null || res.Contains(n)) continue;
                    var bytes = store.Supply(id, BakeBlob.ShaOf(n), findings, path);
                    if (bytes == null) continue;
                    res.AddBlob(bytes);
                    set.FromStore++;
                }
            }
            foreach (var n in named)
                if (n == null || !res.Contains(n))
                {
                    findings?.Warning(BakeFindings.Missing, path, "the bake's blob " + (n ?? "(none)") + " is neither in the scene nor in the store; the item is not built, its blocks are kept");
                    set.Status = Result.Missing;
                    return set;
                }
            try { set.Main = BakeMain.FromBlob(res.Get(main), findings); }
            catch (BakeFormatException e)
            {
                findings?.Error(BakeFindings.BlobCorrupt, path, "the main blob " + main + " does not read: " + e.Message);
                set.Status = Result.Corrupt;
                return set;
            }
            foreach (var sha in set.Main.DataBlobs())
            {
                var b = res.Get(BakeBlob.ResName(sha));
                if (b != null) { set.Blobs[sha] = b; continue; }
                set.Absent.Add(sha);
                findings?.Warning(BakeFindings.DataMissing, path, "the blob " + BakeBlob.ResName(sha) + " the main names is not in the scene; that mesh or texture is absent");
            }
            set.Status = Result.Ok;
            return set;
        }

        /// <summary><see cref="Gather(string, string, IReadOnlyList{string}, ResStore, BakeStore, FindingList, string)"/> for an item's bake block.</summary>
        public static BakeBlobSet Gather(ItemBake bake, ResStore res, BakeStore store, FindingList findings, string path) =>
            Gather(bake?.Id, bake?.Main, bake?.AppliedData, res, store, findings, path);

        /// <summary><see cref="Gather(string, string, IReadOnlyList{string}, ResStore, BakeStore, FindingList, string)"/> for a cut's source.</summary>
        public static BakeBlobSet Gather(CutSource source, ResStore res, BakeStore store, FindingList findings, string path) =>
            Gather(source?.Id, source?.Main, source?.AppliedData, res, store, findings, path);
    }
}
