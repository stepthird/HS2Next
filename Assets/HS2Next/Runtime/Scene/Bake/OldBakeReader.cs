// Chara to Item: the reader of the old bakes. Unity-free. An old bake is a file of its own
// layout (versions 4 to 6): a header (magic, version, number, name, character name, sex, creation ticks, FK names, texture hashes,
// from 5 the parts label), then a raw deflate body (nodes, sources, textures, materials, meshes, renderers, the twist-correction rows,
// from 5 the inside flag and colours, from 6 each renderer's part and inside kind, the section colour, the own-inside switch, and the
// magic again). Its texture files hold one texture each (magic, size, format, mips, linear, length, raw deflate of every level) and are
// named by the MD5 of "<w>x<h>f<format>[m][l]" and the texels. Files are opened for reading with a share mode that lets others read;
// nothing is ever written, moved or deleted where they are. A file that is not what it says is refused with bake.importCorrupt.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using HS2Next.Save;

namespace HS2Next.Scenes.Bake
{
    /// <summary>One node of an old bake (depth-first, parents first; the first is the root).</summary>
    public sealed class OldNode
    {
        public string Name = "";
        public int Parent = -1;
        public float[] P = new float[3], R = { 0f, 0f, 0f, 1f }, S = { 1f, 1f, 1f };
        public bool Active = true;
        public int Layer;
    }

    /// <summary>A bundle asset an old bake refers to: a prefab (<see cref="KindPrefab"/>) or a material (<see cref="KindMaterial"/>).</summary>
    public sealed class OldSource
    {
        public const int KindPrefab = 0, KindMaterial = 1;
        public string Bundle = "", Asset = "", Manifest = "";
        public int Kind;
    }

    /// <summary>A texture of an old bake: stored in a texture file (<see cref="Hash"/>), taken by name from a source's materials
    /// (<see cref="Source"/>), or loaded by bundle and name from a list bundle (<see cref="ListBundle"/>).</summary>
    public sealed class OldTextureRef
    {
        public string Name = "", Hash = "", ListBundle = "", ListManifest = "";
        public int Source = -1;
        public int Width, Height, Format;
        public bool Mips, Linear;
        public int WrapU, WrapV, Filter = 1, Aniso = 1;
        public float MipBias;

        public bool Stored => !string.IsNullOrEmpty(Hash);
        public bool FromSource => !Stored && Source >= 0;
        public bool FromList => !Stored && !FromSource && !string.IsNullOrEmpty(ListBundle);
    }

    /// <summary>A texture slot of an old material.</summary>
    public sealed class OldSlot
    {
        /// <summary>Keep the base material's texture; none; the texture <see cref="Texture"/> of the bake's table.</summary>
        public const int ModeKeep = 0, ModeNone = 1, ModeStored = 2;
        public string Property = "";
        public float[] Scale = { 1f, 1f }, Offset = { 0f, 0f };
        public int Mode, Texture = -1;
    }

    /// <summary>A material of an old bake: a base material from a source (prefab renderer slot or material asset) or none, the shader by
    /// name (its loaded original from <see cref="ShaderSource"/>: a source index, −1 none, −2 a shared copy), the values.</summary>
    public sealed class OldMaterial
    {
        public const int ShaderNone = -1, ShaderSharedCopy = -2;
        public string Name = "", Shader = "", RenderType = "";
        public int Queue = -1;
        public readonly List<string> Keywords = new List<string>(), PassesOff = new List<string>();
        public int Base = -1, BaseSlot;
        public string BasePath = "", BaseName = "";
        public int ShaderSource = ShaderNone;
        public readonly List<KeyValuePair<string, float>> Floats = new List<KeyValuePair<string, float>>();
        public readonly List<KeyValuePair<string, float[]>> Vectors = new List<KeyValuePair<string, float[]>>();
        public readonly List<OldSlot> Slots = new List<OldSlot>();
    }

    /// <summary>A sub-mesh of a stored old mesh (topology as Unity's MeshTopology number).</summary>
    public sealed class OldSubMesh
    {
        public int Topology;
        public int[] Indices = Array.Empty<int>();
    }

    /// <summary>A mesh of an old bake: referenced (a source prefab's mesh at <see cref="SourcePath"/>, else by name) or stored as data.</summary>
    public sealed class OldMesh
    {
        public string Name = "", SourcePath = "";
        public int Source = -1;
        public bool Stored => Source < 0;
        public int VertexCount;
        /// <summary>3 floats a vertex; normals 3 (or null); tangents 4 (or null); four UV channels of 4 floats a vertex (null: none);
        /// colours 4 bytes; bone indices and weights 4 each (or null); bind poses 16 floats each, column-major.</summary>
        public float[] Positions = Array.Empty<float>(), Normals, Tangents;
        public readonly float[][] Uv = new float[4][];
        public byte[] Colors;
        public int[] BoneIndices;
        public float[] BoneWeights;
        public float[] BindPoses = Array.Empty<float>();
        public readonly List<OldSubMesh> SubMeshes = new List<OldSubMesh>();
        /// <summary>Two vectors: the bounds' centre, then its size (as the old writer gave them).</summary>
        public float[] Bounds = new float[6];
        public int BindPoseCount => BindPoses.Length / 16;
    }

    /// <summary>A renderer of an old bake.</summary>
    public sealed class OldRenderer
    {
        /// <summary>Inside kinds (version 6): none, a hollow part's inside, a cut renderer's section.</summary>
        public const int InsideNone = 0, InsideFill = 1, InsideSection = 2;
        public int Node, Mesh = -1, Root = -1, Anchor = -1;
        public bool Skinned, UpdateOffscreen, Receive = true;
        public int[] Materials = Array.Empty<int>(), Bones = Array.Empty<int>();
        public float[] Bounds = new float[6];
        public int Quality, Shadow = 1, SortingOrder, LightProbe = 1, ReflProbe = 1;
        public uint LayerMask;
        public float[] BlendWeights = Array.Empty<float>();
        /// <summary>The part key (version 6; "" before).</summary>
        public string Part = "";
        public int Inside;
    }

    /// <summary>One twist-correction row of an old bake: the row's name (its bone), whether it was on, its index and category.</summary>
    public sealed class OldCorrectionRow
    {
        public string Name = "";
        public bool Enabled, HasLookAt, HasCorrect;
        public int Index, Category;
    }

    /// <summary>An old bake as read (every member of the layout; nothing loaded).</summary>
    public sealed class OldBake
    {
        public const int Magic = 0x31493243, TextureMagic = 0x55324943, MinVersion = 4, MaxVersion = 6;

        public string File = "";
        public long FileBytes, BodyBytes;
        public int Version, Number, Sex = -1;
        public string Name = "", CharaName = "", PartsLabel = "";
        /// <summary>The creation time as the old writer stored it: ticks of the local time of the machine that made it.</summary>
        public long CreatedTicks;
        public readonly List<string> Fk = new List<string>(), TextureHashes = new List<string>();
        public readonly List<OldNode> Nodes = new List<OldNode>();
        public readonly List<OldSource> Sources = new List<OldSource>();
        public readonly List<OldTextureRef> Textures = new List<OldTextureRef>();
        public readonly List<OldMaterial> Materials = new List<OldMaterial>();
        public readonly List<OldMesh> Meshes = new List<OldMesh>();
        public readonly List<OldRenderer> Renderers = new List<OldRenderer>();
        /// <summary>The twist correction's master switch and its rows.</summary>
        public bool CorrectionOn;
        public readonly List<OldCorrectionRow> CorrectionRows = new List<OldCorrectionRow>();
        /// <summary>Version 5: hollow parts filled; the inside colour per material.</summary>
        public bool FillInside;
        public readonly List<float[]> InsideColours = new List<float[]>();
        /// <summary>Version 6: the section colour (rgba) and the own-inside switch of a piece.</summary>
        public float[] SectionColour = { 0.35f, 0.03f, 0.03f, 1f };
        public bool OwnInside = true;

        public int StoredMeshes { get { int n = 0; foreach (var m in Meshes) if (m.Stored) n++; return n; } }
        public int ReferencedMeshes => Meshes.Count - StoredMeshes;
        public int StoredVertices { get { int n = 0; foreach (var m in Meshes) if (m.Stored) n += m.VertexCount; return n; } }
        public int TexturesStored { get { int n = 0; foreach (var t in Textures) if (t.Stored) n++; return n; } }
        public int TexturesFromSource { get { int n = 0; foreach (var t in Textures) if (t.FromSource) n++; return n; } }
        public int TexturesFromList { get { int n = 0; foreach (var t in Textures) if (t.FromList) n++; return n; } }
        public int SkinnedRenderers { get { int n = 0; foreach (var r in Renderers) if (r.Skinned) n++; return n; } }
        public int SectionRenderers { get { int n = 0; foreach (var r in Renderers) if (r.Inside == OldRenderer.InsideSection) n++; return n; } }

        /// <summary>The creation time in UTC: the stored ticks read as the local time of <paramref name="zone"/> (this machine's when null).</summary>
        public DateTime CreatedUtc(TimeZoneInfo zone = null)
        {
            if (CreatedTicks <= 0 || CreatedTicks > DateTime.MaxValue.Ticks) return new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var local = new DateTime(CreatedTicks, DateTimeKind.Unspecified);
            try { return TimeZoneInfo.ConvertTimeToUtc(local, zone ?? TimeZoneInfo.Local); }
            catch (ArgumentException) { return DateTime.SpecifyKind(local, DateTimeKind.Utc); }
        }

        /// <summary>The joint-correction categories the old rows switch on (a category is on when one of its rows was, and the master
        /// switch was on); null when none is.</summary>
        public bool[] Correction(int categories = 8)
        {
            if (!CorrectionOn) return null;
            var on = new bool[categories];
            bool any = false;
            foreach (var r in CorrectionRows)
                if (r.Enabled && r.Category >= 0 && r.Category < categories) { on[r.Category] = true; any = true; }
            return any ? on : null;
        }
    }

    /// <summary>The header of an old texture file and the lengths of its levels.</summary>
    public sealed class OldTexture
    {
        public const int FormatRgb24 = 3, FormatRgba32 = 4;
        public string File = "", Name = "";
        public long FileBytes;
        public int Width, Height, Format;
        public bool Mips, Linear;
        public long RawLength;
        /// <summary>Bytes of each level as stored, largest first.</summary>
        public readonly List<long> Levels = new List<long>();
        public int Channels => Format == FormatRgb24 ? 3 : 4;
        /// <summary>The string the name's hash starts with.</summary>
        public string HashPrefix => Width.ToString(CultureInfo.InvariantCulture) + "x" + Height.ToString(CultureInfo.InvariantCulture) + "f" +
                                    Format.ToString(CultureInfo.InvariantCulture) + (Mips ? "m" : "") + (Linear ? "l" : "");
    }

    /// <summary>A folder of old bakes: its bake files (by name), its texture folder, its last number.</summary>
    public sealed class OldBakeFolder
    {
        public string Folder = "", TextureFolder = "";
        public readonly List<string> Files = new List<string>();
        /// <summary>The number in last_no.txt, or null.</summary>
        public int? LastNumber;
    }

    public static class OldBakeReader
    {
        /// <summary>The finding of a file that is not what it says (a wrong magic or version, a short or damaged body, a texture whose
        /// content does not equal its name): the file is refused.</summary>
        public const string Corrupt = "bake.importCorrupt";
        public const string Extension = ".c2i", TextureExtension = ".c2t", TextureFolderName = "textures", LastNumberName = "last_no.txt";
        /// <summary>Counts beyond these are refused as damage (the largest of the owner's files holds 857 nodes and 83,754 vertices).</summary>
        public const int MaxCount = 1 << 20, MaxVertices = 1 << 24, MaxIndices = 1 << 27;

        /// <summary>A file opened for reading; others may read it too; nothing is written.</summary>
        public static FileStream OpenRead(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);

        public static byte[] ReadAll(string path)
        {
            using (var f = OpenRead(path))
            {
                long n = f.Length;
                if (n > int.MaxValue) throw new IOException("the file is too large");
                var b = new byte[n];
                int o = 0;
                while (o < b.Length)
                {
                    int k = f.Read(b, o, b.Length - o);
                    if (k <= 0) throw new EndOfStreamException();
                    o += k;
                }
                return b;
            }
        }

        /// <summary>The bake files of a folder in ordinal order of their names, its texture folder, its last number.</summary>
        public static OldBakeFolder Scan(string folder)
        {
            var f = new OldBakeFolder { Folder = folder ?? "", TextureFolder = Path.Combine(folder ?? "", TextureFolderName) };
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return f;
            var files = new List<string>(Directory.GetFiles(folder, "*" + Extension));
            files.RemoveAll(p => !string.Equals(Path.GetExtension(p), Extension, StringComparison.OrdinalIgnoreCase));
            files.Sort((a, b) => string.CompareOrdinal(Path.GetFileName(a), Path.GetFileName(b)));
            f.Files.AddRange(files);
            var last = Path.Combine(folder, LastNumberName);
            if (System.IO.File.Exists(last))
            {
                try
                {
                    var s = Encoding.UTF8.GetString(ReadAll(last)).Trim();
                    if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) f.LastNumber = n;
                }
                catch (IOException) { }
            }
            return f;
        }

        /// <summary>The path of a texture file by its hash.</summary>
        public static string TexturePath(string textureFolder, string hash) => Path.Combine(textureFolder ?? "", hash + TextureExtension);

        // ------------------------------------------------------------ bakes

        /// <summary>Reads an old bake from a file; null (with <see cref="Corrupt"/>) when it is not one.</summary>
        public static OldBake ReadFile(string path, FindingList findings)
        {
            byte[] raw;
            try { raw = ReadAll(path); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                findings?.Error(Corrupt, Path.GetFileName(path), "the file cannot be read: " + e.Message);
                return null;
            }
            return Read(raw, Path.GetFileName(path), findings);
        }

        /// <summary>Reads an old bake from its bytes; null (with <see cref="Corrupt"/>) when they are not one.</summary>
        public static OldBake Read(byte[] raw, string label, FindingList findings)
        {
            try { return Parse(raw, label); }
            catch (Exception e) when (e is OldBakeException || e is IOException || e is InvalidDataException || e is ArgumentException || e is OverflowException || e is DecoderFallbackException)
            {
                findings?.Error(Corrupt, label ?? "", "not an old bake (" + (e is EndOfStreamException ? "the file ends early" : e.Message) + "): refused");
                return null;
            }
        }

        sealed class OldBakeException : Exception { public OldBakeException(string m) : base(m) { } }

        static OldBake Parse(byte[] raw, string label)
        {
            if (raw == null || raw.Length < 8) throw new EndOfStreamException();
            var h = new Bin(raw, 0, raw.Length);
            if (h.I32() != OldBake.Magic) throw new OldBakeException("a wrong magic");
            var b = new OldBake { File = label ?? "", FileBytes = raw.LongLength };
            b.Version = h.I32();
            if (b.Version < OldBake.MinVersion || b.Version > OldBake.MaxVersion) throw new OldBakeException("version " + b.Version + " is not 4 to 6");
            b.Number = h.I32();
            b.Name = h.Str();
            b.CharaName = h.Str();
            b.Sex = h.I32();
            b.CreatedTicks = h.I64();
            h.Strings(b.Fk);
            h.Strings(b.TextureHashes);
            if (b.Version >= 5) b.PartsLabel = h.Str();
            byte[] body;
            using (var src = new MemoryStream(raw, h.Pos, raw.Length - h.Pos, false))
            using (var z = new DeflateStream(src, CompressionMode.Decompress))
            using (var o = new MemoryStream())
            {
                z.CopyTo(o);
                body = o.ToArray();
            }
            b.BodyBytes = body.LongLength;
            var r = new Bin(body, 0, body.Length);
            int n = r.Count(41);
            for (int i = 0; i < n; i++)
            {
                var d = new OldNode { Name = r.Str(), Parent = r.I32() };
                r.Floats(d.P); r.Floats(d.R); r.Floats(d.S);
                d.Active = r.Bool(); d.Layer = r.I32();
                if (d.Parent >= i || d.Parent < -1 || (i == 0) != (d.Parent == -1)) throw new OldBakeException("node " + i + " has the parent " + d.Parent);
                b.Nodes.Add(d);
            }
            n = r.Count(7);
            for (int i = 0; i < n; i++) b.Sources.Add(new OldSource { Bundle = r.Str(), Asset = r.Str(), Manifest = r.Str(), Kind = r.I32() });
            n = r.Count(42);
            for (int i = 0; i < n; i++)
            {
                var t = new OldTextureRef { Name = r.Str(), Hash = r.Str(), Source = r.I32(), ListBundle = r.Str(), ListManifest = r.Str(), Width = r.I32(), Height = r.I32(), Format = r.I32() };
                t.Mips = r.Bool(); t.Linear = r.Bool(); t.WrapU = r.I32(); t.WrapV = r.I32(); t.Filter = r.I32(); t.Aniso = r.I32(); t.MipBias = r.F32();
                if (t.Source >= b.Sources.Count) throw new OldBakeException("texture " + i + " names source " + t.Source);
                b.Textures.Add(t);
            }
            n = r.Count(30);
            for (int i = 0; i < n; i++)
            {
                var m = new OldMaterial { Name = r.Str(), Shader = r.Str(), RenderType = r.Str(), Queue = r.I32() };
                r.Strings(m.Keywords); r.Strings(m.PassesOff);
                m.Base = r.I32(); m.BasePath = r.Str(); m.BaseSlot = r.I32(); m.BaseName = r.Str(); m.ShaderSource = r.I32();
                if (m.Base >= b.Sources.Count || m.ShaderSource >= b.Sources.Count || m.ShaderSource < OldMaterial.ShaderSharedCopy)
                    throw new OldBakeException("material " + i + " names source " + m.Base + " / " + m.ShaderSource);
                int k = r.Count(5);
                for (int j = 0; j < k; j++) { var key = r.Str(); m.Floats.Add(new KeyValuePair<string, float>(key, r.F32())); }
                k = r.Count(17);
                for (int j = 0; j < k; j++) { var key = r.Str(); var v = new float[4]; r.Floats(v); m.Vectors.Add(new KeyValuePair<string, float[]>(key, v)); }
                k = r.Count(25);
                for (int j = 0; j < k; j++)
                {
                    var s = new OldSlot { Property = r.Str() };
                    r.Floats(s.Scale); r.Floats(s.Offset);
                    s.Mode = r.I32(); s.Texture = r.I32();
                    if (s.Mode == OldSlot.ModeStored && (s.Texture < 0 || s.Texture >= b.Textures.Count)) throw new OldBakeException("material " + i + " slot " + s.Property + " names texture " + s.Texture);
                    m.Slots.Add(s);
                }
                b.Materials.Add(m);
            }
            n = r.Count(9);
            for (int i = 0; i < n; i++) b.Meshes.Add(ReadMesh(r, b, i));
            n = r.Count(62);
            for (int i = 0; i < n; i++)
            {
                var x = new OldRenderer { Node = r.I32(), Skinned = r.Bool(), Mesh = r.I32() };
                x.Materials = r.Ints(); x.Bones = r.Ints(); x.Root = r.I32();
                r.Floats(x.Bounds);
                x.Quality = r.I32(); x.Shadow = r.I32(); x.SortingOrder = r.I32(); x.UpdateOffscreen = r.Bool(); x.Receive = r.Bool();
                x.Anchor = r.I32(); x.LightProbe = r.I32(); x.ReflProbe = r.I32(); x.LayerMask = r.U32();
                int bw = r.Count(4);
                x.BlendWeights = new float[bw];
                r.Floats(x.BlendWeights);
                if (x.Node < 0 || x.Node >= b.Nodes.Count || x.Mesh >= b.Meshes.Count || x.Root >= b.Nodes.Count || x.Anchor >= b.Nodes.Count)
                    throw new OldBakeException("renderer " + i + " names node " + x.Node + ", mesh " + x.Mesh);
                foreach (int mi in x.Materials) if (mi >= b.Materials.Count) throw new OldBakeException("renderer " + i + " names material " + mi);
                foreach (int bi in x.Bones) if (bi >= b.Nodes.Count) throw new OldBakeException("renderer " + i + " names bone " + bi);
                b.Renderers.Add(x);
            }
            b.CorrectionOn = r.Bool();
            n = r.Count(70);
            for (int i = 0; i < n; i++)
            {
                var c = new OldCorrectionRow { Name = r.Str(), Enabled = r.Bool(), HasLookAt = r.Bool(), HasCorrect = r.Bool(), Index = r.I32(), Category = r.I32() };
                // the rest of the row as the old writer kept it: the look-at part (bones, axes and order, limits), the correct part (bones,
                // calc, order, charm rate, three used axes with their ranges); the import takes the row's category and switch only
                r.Str(); r.Str(); r.Str();
                for (int k = 0; k < 5; k++) r.I32();
                r.F32(); r.F32();
                r.Str(); r.Str(); r.I32(); r.I32(); r.F32();
                for (int k = 0; k < 3; k++) { r.Bool(); r.F32(); r.F32(); }
                b.CorrectionRows.Add(c);
            }
            if (b.Version >= 5)
            {
                b.FillInside = r.Bool();
                n = r.Count(16);
                for (int i = 0; i < n; i++) { var v = new float[4]; r.Floats(v); b.InsideColours.Add(v); }
            }
            if (b.Version >= 6)
            {
                n = r.Count(5);
                if (n != b.Renderers.Count) throw new OldBakeException(n + " renderer parts for " + b.Renderers.Count + " renderers");
                for (int i = 0; i < n; i++) { b.Renderers[i].Part = r.Str(); b.Renderers[i].Inside = r.I32(); }
                r.Floats(b.SectionColour);
                b.OwnInside = r.Bool();
            }
            if (r.I32() != OldBake.Magic) throw new OldBakeException("the closing magic is missing");
            if (r.Pos != body.Length) throw new OldBakeException((body.Length - r.Pos) + " bytes follow the closing magic");
            return b;
        }

        static OldMesh ReadMesh(Bin r, OldBake b, int i)
        {
            var m = new OldMesh { Name = r.Str(), Source = r.I32(), SourcePath = r.Str() };
            if (m.Source >= b.Sources.Count) throw new OldBakeException("mesh " + i + " names source " + m.Source);
            if (!m.Stored) return m;
            int n = r.I32();
            if (n < 0 || n > MaxVertices || (long)n * 12 > r.Left) throw new OldBakeException("mesh " + i + " has " + n + " vertices");
            m.VertexCount = n;
            m.Positions = new float[3 * n]; r.Floats(m.Positions);
            if (r.Bool()) { m.Normals = new float[3 * n]; r.Floats(m.Normals); }
            if (r.Bool()) { m.Tangents = new float[4 * n]; r.Floats(m.Tangents); }
            for (int ch = 0; ch < 4; ch++) if (r.Bool()) { m.Uv[ch] = new float[4 * n]; r.Floats(m.Uv[ch]); }
            if (r.Bool()) m.Colors = r.Bytes(4 * n);
            if (r.Bool())
            {
                m.BoneIndices = new int[4 * n];
                m.BoneWeights = new float[4 * n];
                for (int k = 0; k < n; k++)
                {
                    for (int j = 0; j < 4; j++) m.BoneIndices[4 * k + j] = r.I32();
                    for (int j = 0; j < 4; j++) m.BoneWeights[4 * k + j] = r.F32();
                }
            }
            int bp = r.Count(64);
            m.BindPoses = new float[16 * bp];
            r.Floats(m.BindPoses);
            int sc = r.Count(8);
            for (int s = 0; s < sc; s++)
            {
                var sm = new OldSubMesh { Topology = r.I32() };
                int k = r.Count(4);
                if (k > MaxIndices) throw new OldBakeException("mesh " + i + " has " + k + " indices");
                sm.Indices = new int[k];
                for (int j = 0; j < k; j++)
                {
                    int x = r.I32();
                    if (x < 0 || x >= n) throw new OldBakeException("mesh " + i + " has the index " + x + " of " + n + " vertices");
                    sm.Indices[j] = x;
                }
                m.SubMeshes.Add(sm);
            }
            r.Floats(m.Bounds);
            if (m.BoneIndices != null)
                foreach (int x in m.BoneIndices) if (x < 0 || (bp > 0 && x >= bp)) throw new OldBakeException("mesh " + i + " weighs on bone " + x + " of " + bp);
            return m;
        }

        // ------------------------------------------------------------ texture files

        /// <summary>The header of a texture file and its level lengths; null (with <see cref="Corrupt"/>) when it is not one.</summary>
        public static OldTexture ReadTextureHeader(string path, FindingList findings)
        {
            try
            {
                using (var f = OpenRead(path)) return Header(f, path);
            }
            catch (Exception e) when (e is OldBakeException || e is IOException || e is UnauthorizedAccessException)
            {
                findings?.Error(Corrupt, Path.GetFileName(path), "not an old texture (" + (e is EndOfStreamException ? "the file ends early" : e.Message) + "): refused");
                return null;
            }
        }

        static OldTexture Header(Stream f, string path)
        {
            var head = new byte[22];
            int got = 0;
            while (got < head.Length) { int k = f.Read(head, got, head.Length - got); if (k <= 0) throw new EndOfStreamException(); got += k; }
            var r = new Bin(head, 0, head.Length);
            if (r.I32() != OldBake.TextureMagic) throw new OldBakeException("a wrong magic");
            var t = new OldTexture { File = Path.GetFileName(path), Name = Path.GetFileNameWithoutExtension(path), FileBytes = f.Length };
            t.Width = r.I32(); t.Height = r.I32(); t.Format = r.I32(); t.Mips = r.Bool(); t.Linear = r.Bool(); t.RawLength = (uint)r.I32();
            if (t.Format != OldTexture.FormatRgb24 && t.Format != OldTexture.FormatRgba32) throw new OldBakeException("format " + t.Format + " is not RGB24 or RGBA32");
            if (t.Width <= 0 || t.Height <= 0 || t.Width > 16384 || t.Height > 16384) throw new OldBakeException("size " + t.Width + " x " + t.Height);
            int w = t.Width, h = t.Height;
            long sum = 0;
            while (true)
            {
                long n = (long)w * h * t.Channels;
                t.Levels.Add(n);
                sum += n;
                if (!t.Mips || (w == 1 && h == 1)) break;
                w = Math.Max(1, w >> 1); h = Math.Max(1, h >> 1);
            }
            if (sum != t.RawLength) throw new OldBakeException("its length " + t.RawLength + " is not the " + sum + " bytes of its levels");
            return t;
        }

        /// <summary>
        /// Reads every level of a texture file, largest first, and hands each to <paramref name="level"/> (index, width, height, texels as
        /// RGBA32: a three-channel level gets alpha 255) as soon as it is inflated; the content is checked against the file's name (the
        /// MD5 of the size string and the texels as stored). False (with <see cref="Corrupt"/>) when the file is not what its name says:
        /// the caller then drops what it was given. <paramref name="level"/> may be null (a check only).
        /// </summary>
        public static bool ReadTexture(string path, Action<int, int, int, byte[]> level, FindingList findings, out OldTexture info)
        {
            info = null;
            string label = Path.GetFileName(path);
            try
            {
                using (var f = OpenRead(path))
                {
                    var t = Header(f, path);
                    info = t;
                    using (var md5 = MD5.Create())
                    {
                        var pre = Encoding.ASCII.GetBytes(t.HashPrefix);
                        md5.TransformBlock(pre, 0, pre.Length, null, 0);
                        using (var z = new DeflateStream(f, CompressionMode.Decompress, true))
                        {
                            int w = t.Width, h = t.Height;
                            for (int l = 0; l < t.Levels.Count; l++)
                            {
                                var raw = new byte[t.Levels[l]];
                                Fill(z, raw);
                                md5.TransformBlock(raw, 0, raw.Length, null, 0);
                                level?.Invoke(l, w, h, t.Channels == 4 ? raw : Expand(raw, w * h));
                                w = Math.Max(1, w >> 1); h = Math.Max(1, h >> 1);
                            }
                            if (z.ReadByte() >= 0) throw new OldBakeException("bytes follow its levels");
                        }
                        md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        string hash = Hex(md5.Hash);
                        if (!string.Equals(hash, t.Name, StringComparison.OrdinalIgnoreCase))
                            throw new OldBakeException("its content hashes to " + hash + ", not to its name");
                    }
                    return true;
                }
            }
            catch (Exception e) when (e is OldBakeException || e is IOException || e is InvalidDataException || e is UnauthorizedAccessException)
            {
                findings?.Error(Corrupt, label, "not the texture its name says (" + (e is EndOfStreamException ? "the file ends early" : e.Message) + "): refused");
                return false;
            }
        }

        static void Fill(Stream z, byte[] b)
        {
            int o = 0;
            while (o < b.Length)
            {
                int k = z.Read(b, o, b.Length - o);
                if (k <= 0) throw new EndOfStreamException();
                o += k;
            }
        }

        static byte[] Expand(byte[] rgb, int pixels)
        {
            var o = new byte[pixels * 4];
            for (int i = 0; i < pixels; i++) { o[4 * i] = rgb[3 * i]; o[4 * i + 1] = rgb[3 * i + 1]; o[4 * i + 2] = rgb[3 * i + 2]; o[4 * i + 3] = 255; }
            return o;
        }

        public static string Hex(byte[] h)
        {
            var sb = new StringBuilder(h.Length * 2);
            foreach (var x in h) sb.Append(x.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        // ------------------------------------------------------------ the binary reader (little-endian, strings with a 7-bit length)

        sealed class Bin
        {
            readonly byte[] b;
            int o;
            readonly int end;
            public Bin(byte[] b, int o, int end) { this.b = b; this.o = o; this.end = end; }
            public int Pos => o;
            public long Left => end - o;

            void Need(int n) { if (n < 0 || o + n > end) throw new EndOfStreamException(); }
            public int I32() { Need(4); int v = b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24; o += 4; return v; }
            public uint U32() => (uint)I32();
            public long I64() { long lo = (uint)I32(); long hi = (uint)I32(); return lo | hi << 32; }
            public float F32() { Need(4); float v = BitConverter.ToSingle(b, o); if (!BitConverter.IsLittleEndian) throw new NotSupportedException(); o += 4; return v; }
            public bool Bool() { Need(1); return b[o++] != 0; }
            public byte[] Bytes(int n) { Need(n); var x = new byte[n]; Buffer.BlockCopy(b, o, x, 0, n); o += n; return x; }

            /// <summary>A count, refused when it is negative or when its elements (at least <paramref name="minBytes"/> each) cannot fit.</summary>
            public int Count(int minBytes)
            {
                int n = I32();
                if (n < 0 || n > MaxCount || (long)n * Math.Max(1, minBytes) > Left) throw new OldBakeException("a count of " + n);
                return n;
            }

            public string Str()
            {
                int n = 0, shift = 0;
                while (true)
                {
                    Need(1);
                    byte c = b[o++];
                    n |= (c & 0x7F) << shift;
                    if (c < 0x80) break;
                    shift += 7;
                    if (shift > 28) throw new OldBakeException("a string length that does not end");
                }
                Need(n);
                var s = new UTF8Encoding(false, true).GetString(b, o, n);
                o += n;
                return s;
            }

            public void Strings(List<string> into)
            {
                int n = Count(1);
                for (int i = 0; i < n; i++) into.Add(Str());
            }

            public int[] Ints()
            {
                int n = Count(4);
                var a = new int[n];
                for (int i = 0; i < n; i++) a[i] = I32();
                return a;
            }

            public void Floats(float[] into)
            {
                Need(4 * into.Length);
                Buffer.BlockCopy(b, o, into, 0, 4 * into.Length);
                o += 4 * into.Length;
            }
        }
    }
}
