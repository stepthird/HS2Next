// Shared helpers of the pose-file tests: an independent writer of pose
// payloads built on .NET BinaryWriter (the writer Studio's PauseCtrl uses) that records the offset of every field, a
// test-side MessagePack writer, KKEx trailer and PNG builders, and byte helpers. Synthetic data only (no game data).
// Unity-free: compiled into Assembly-CSharp-Editor and, under plain Mono, into the pose suite of Tools/mono_tests.ps1.
namespace HS2Next.Import.Pose.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using HS2Next.Import.Pose;

    /// <summary>Description of a synthetic pose; Build() writes it with BinaryWriter.</summary>
    public sealed class PoseSpec
    {
        public string Magic = PoseFile.Magic;
        public int Version = 101, Sex = 1;
        public string Name = "sample";
        public byte[] NameRaw;                 // overrides Name: raw bytes written after a 7-bit length (or RawNameWithPrefix)
        public byte[] NameRawWithPrefix;       // complete raw string bytes (prefix included)
        public int Group = 0, Category = 2, No = 11;
        public uint TimeBits = 0x3F000000u;    // 0.5
        public byte EnableIK = 0, EnableFK = 1;
        public byte[] ActiveIK = { 1, 1, 1, 1, 1 };
        public byte[] ActiveFK = { 0, 1, 0, 1, 1, 1, 0 };
        public byte[] Expression = { 1, 1, 1, 1 };
        public List<(int key, uint[] bits)> IK = new List<(int, uint[])>();
        public List<(int key, uint[] bits)> FK = new List<(int, uint[])>();
        public int? IkCountOverride, FkCountOverride;
        public byte[] Trailer;

        /// <summary>Offsets of the fields written by the last Build(): (name, start, end), the IK/FK blocks as one span each (count included).</summary>
        public readonly List<(string name, int start, int end)> Fields = new List<(string, int, int)>();
        public int PayloadEnd;

        public static uint[] Bits(float px, float py, float pz, float rx, float ry, float rz, float sx = 1, float sy = 1, float sz = 1) =>
            new[] { FloatBits.ToBits(px), FloatBits.ToBits(py), FloatBits.ToBits(pz), FloatBits.ToBits(rx), FloatBits.ToBits(ry), FloatBits.ToBits(rz), FloatBits.ToBits(sx), FloatBits.ToBits(sy), FloatBits.ToBits(sz) };

        /// <summary>A pose with 13 IK targets and the vanilla female FK key set, every value distinct.</summary>
        public static PoseSpec Standard(int seed = 1)
        {
            var s = new PoseSpec();
            var rnd = new Random(seed);
            float R(float a) => (float)((rnd.NextDouble() * 2 - 1) * a);
            for (int k = 0; k < 13; k++) s.IK.Add((k, Bits(R(10), R(10), R(10), (k % 3 == 0) ? R(180) : 0, 0, (k % 3 == 0) ? R(90) : 0)));
            foreach (int k in FemaleVanillaKeys()) s.FK.Add((k, Bits(k == 0 ? 0 : 0, k == 0 ? R(5) : 0, 0, R(360), R(360), R(360))));
            return s;
        }

        public static IEnumerable<int> FemaleVanillaKeys()
        {
            for (int k = 0; k <= 51; k++) yield return k;
            for (int k = 53; k <= 57; k++) yield return k;
            for (int k = 59; k <= 63; k++) yield return k;
            yield return 65; yield return 66;
        }

        public byte[] Build()
        {
            Fields.Clear();
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms, new UTF8Encoding(false, false));
            void Mark(string name, Action a) { int s0 = (int)ms.Position; a(); w.Flush(); Fields.Add((name, s0, (int)ms.Position)); }
            Mark("identifying string", () => w.Write(Magic));
            Mark("version", () => w.Write(Version));
            Mark("sex", () => w.Write(Sex));
            Mark("name", () =>
            {
                if (NameRawWithPrefix != null) w.Write(NameRawWithPrefix);
                else if (NameRaw != null) { Write7(w, NameRaw.Length); w.Write(NameRaw); }
                else w.Write(Name);
            });
            Mark("anime group", () => w.Write(Group));
            Mark("anime category", () => w.Write(Category));
            Mark("anime no", () => w.Write(No));
            if (Version >= 101) Mark("normalizedTime", () => w.Write(TimeBits));
            Mark("enableIK", () => w.Write(EnableIK));
            for (int i = 0; i < 5; i++) { int j = i; Mark("activeIK", () => w.Write(ActiveIK[j])); }
            Mark("IK", () => Entries(w, IK, IkCountOverride));
            Mark("enableFK", () => w.Write(EnableFK));
            for (int i = 0; i < 7; i++) { int j = i; Mark("activeFK", () => w.Write(ActiveFK[j])); }
            Mark("FK", () => Entries(w, FK, FkCountOverride));
            for (int i = 0; i < 4; i++) { int j = i; Mark("expression", () => w.Write(Expression[j])); }
            w.Flush();
            PayloadEnd = (int)ms.Position;
            if (Trailer != null) w.Write(Trailer);
            w.Flush();
            return ms.ToArray();
        }

        static void Entries(BinaryWriter w, List<(int key, uint[] bits)> list, int? count)
        {
            w.Write(count ?? list.Count);
            foreach (var (key, bits) in list)
            {
                w.Write(key);
                foreach (var b in bits) w.Write(b);
            }
        }

        public static void Write7(BinaryWriter w, int v)
        {
            uint u = (uint)v;
            while (u >= 0x80) { w.Write((byte)(u | 0x80)); u >>= 7; }
            w.Write((byte)u);
        }
    }

    /// <summary>Test-side MessagePack writer (big-endian, explicit forms so tests control the exact bytes).</summary>
    public sealed class Mp
    {
        readonly List<byte> b = new List<byte>();
        public byte[] ToArray() => b.ToArray();
        public Mp Raw(params byte[] x) { b.AddRange(x); return this; }
        public Mp Map(int n) { if (n < 16) b.Add((byte)(0x80 | n)); else { b.Add(0xde); b.Add((byte)(n >> 8)); b.Add((byte)n); } return this; }
        public Mp Arr(int n) { if (n < 16) b.Add((byte)(0x90 | n)); else { b.Add(0xdc); b.Add((byte)(n >> 8)); b.Add((byte)n); } return this; }
        public Mp Str(string s)
        {
            var u = Encoding.UTF8.GetBytes(s);
            if (u.Length < 32) b.Add((byte)(0xa0 | u.Length));
            else if (u.Length < 256) { b.Add(0xd9); b.Add((byte)u.Length); }
            else { b.Add(0xda); b.Add((byte)(u.Length >> 8)); b.Add((byte)u.Length); }
            b.AddRange(u);
            return this;
        }
        public Mp FixInt(int v) { b.Add((byte)v); return this; }
        public Mp Int32(int v) { b.Add(0xd2); BE((uint)v, 4); return this; }
        public Mp UInt16(int v) { b.Add(0xcd); BE((uint)v, 2); return this; }
        public Mp F32(float f) { b.Add(0xca); BE(FloatBits.ToBits(f), 4); return this; }
        public Mp F32Bits(uint bits) { b.Add(0xca); BE(bits, 4); return this; }
        public Mp F64(double d) { b.Add(0xcb); ulong u = (ulong)BitConverter.DoubleToInt64Bits(d); BE((uint)(u >> 32), 4); BE((uint)u, 4); return this; }
        public Mp Bool(bool v) { b.Add(v ? (byte)0xc3 : (byte)0xc2); return this; }
        public Mp Nil() { b.Add(0xc0); return this; }
        public Mp Bin(byte[] x) { if (x.Length < 256) { b.Add(0xc4); b.Add((byte)x.Length); } else { b.Add(0xc5); b.Add((byte)(x.Length >> 8)); b.Add((byte)x.Length); } b.AddRange(x); return this; }
        public Mp Ext(sbyte type, byte[] x) { b.Add(0xc7); b.Add((byte)x.Length); b.Add((byte)type); b.AddRange(x); return this; }
        void BE(uint v, int n) { for (int i = n - 1; i >= 0; i--) b.Add((byte)(v >> (8 * i))); }
    }

    public static class PoseKit
    {
        /// <summary>A KKEx trailer: marker, version, length, then the MessagePack block.</summary>
        public static byte[] KKEx(byte[] block, int version = 3, int? lengthOverride = null, string marker = "KKEx")
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms, new UTF8Encoding(false));
            w.Write(marker);
            w.Write(version);
            w.Write(lengthOverride ?? block.Length);
            w.Write(block);
            w.Flush();
            return ms.ToArray();
        }

        /// <summary>The ExtendedSave entry as the plugin writes it: "com.bepis.bepinex.extendedsave" → [0, {gameName: int32 4}].</summary>
        public static Mp ExtendedSaveEntry(Mp m, int game = 4) =>
            m.Str(KKExTrailer.ExtendedSaveGuid).Arr(2).FixInt(0).Map(1).Str("gameName").Int32(game);

        /// <summary>A PoseToolsData entry (key + value) with the plugin's field order; skirt optional.</summary>
        public static Mp PoseToolsEntry(Mp m, int brow, int eyes, int mouth, float eyeOpen, float mouthOpen, bool[] joint, IList<(int key, uint x, uint y, uint z)> skirt)
        {
            var jc = new Mp().Arr(joint.Length);
            foreach (var j in joint) jc.Bool(j);
            m.Str(PoseToolsData.Guid).Arr(2).FixInt(0).Map(skirt != null ? 7 : 6)
                .Str("EyebrowPatternData").Int32(brow).Str("EyesPatternData").Int32(eyes).Str("MouthPatternData").Int32(mouth)
                .Str("EyeOpenData").F32(eyeOpen).Str("MouthOpenData").F32(mouthOpen);
            if (skirt != null)
            {
                var sk = new Mp().Map(skirt.Count);
                foreach (var (key, x, y, z) in skirt) sk.UInt16(key).Arr(3).F32Bits(x).F32Bits(y).F32Bits(z);
                m.Str("SkirtFKData").Bin(sk.ToArray());
            }
            m.Str("JointCorrectionData").Bin(jc.ToArray());
            return m;
        }

        // ------------------------------------------------------------------ PNG

        static uint[] crcTable;
        static uint Crc(byte[] d, int off, int len)
        {
            if (crcTable == null)
            {
                crcTable = new uint[256];
                for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; crcTable[n] = c; }
            }
            uint r = 0xFFFFFFFFu;
            for (int i = 0; i < len; i++) r = crcTable[(r ^ d[off + i]) & 0xFF] ^ (r >> 8);
            return r ^ 0xFFFFFFFFu;
        }

        public static void Chunk(List<byte> o, string type, byte[] data)
        {
            int n = data.Length;
            o.Add((byte)(n >> 24)); o.Add((byte)(n >> 16)); o.Add((byte)(n >> 8)); o.Add((byte)n);
            var td = new byte[4 + n];
            Encoding.ASCII.GetBytes(type, 0, 4, td, 0);
            Buffer.BlockCopy(data, 0, td, 4, n);
            o.AddRange(td);
            uint c = Crc(td, 0, td.Length);
            o.Add((byte)(c >> 24)); o.Add((byte)(c >> 16)); o.Add((byte)(c >> 8)); o.Add((byte)c);
        }

        /// <summary>A small structurally valid PNG (IHDR, IDAT, a tEXt chunk that contains the text "IEND", IEND).</summary>
        public static byte[] Png(int idatBytes = 40)
        {
            var o = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            Chunk(o, "IHDR", new byte[] { 0, 0, 0, 4, 0, 0, 0, 4, 8, 2, 0, 0, 0 });
            Chunk(o, "tEXt", Encoding.ASCII.GetBytes("Comment\0IEND inside a text chunk"));
            var idat = new byte[idatBytes];
            for (int i = 0; i < idat.Length; i++) idat[i] = (byte)(i * 37 + 11);
            Chunk(o, "IDAT", idat);
            Chunk(o, "IEND", new byte[0]);
            return o.ToArray();
        }

        public static byte[] Concat(params byte[][] parts)
        {
            int n = 0;
            foreach (var p in parts) n += p?.Length ?? 0;
            var r = new byte[n];
            int o = 0;
            foreach (var p in parts) { if (p == null) continue; Buffer.BlockCopy(p, 0, r, o, p.Length); o += p.Length; }
            return r;
        }

        public static byte[] Prefix(byte[] d, int n) { var r = new byte[n]; Buffer.BlockCopy(d, 0, r, 0, n); return r; }

        public static bool Same(byte[] a, byte[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>First differing offset, or -1.</summary>
        public static int Diff(byte[] a, byte[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) if (a[i] != b[i]) return i;
            return a.Length == b.Length ? -1 : n;
        }

        /// <summary>Reads a payload with BinaryReader (independent of the implementation) into a readable summary.</summary>
        public static string IndependentSummary(byte[] d, int start = 0)
        {
            var r = new BinaryReader(new MemoryStream(d, start, d.Length - start), new UTF8Encoding(false, false));
            var sb = new StringBuilder();
            sb.Append(r.ReadString()).Append('|');
            int ver = r.ReadInt32();
            sb.Append(ver).Append('|').Append(r.ReadInt32()).Append('|').Append(r.ReadString()).Append('|');
            sb.Append(r.ReadInt32()).Append('/').Append(r.ReadInt32()).Append('/').Append(r.ReadInt32()).Append('|');
            if (ver >= 101) sb.Append(r.ReadUInt32().ToString("x8")).Append('|');
            sb.Append(r.ReadBoolean());
            for (int i = 0; i < 5; i++) sb.Append(r.ReadBoolean() ? '1' : '0');
            int n = r.ReadInt32();
            sb.Append("|IK").Append(n);
            for (int i = 0; i < n; i++) { sb.Append(' ').Append(r.ReadInt32()); for (int k = 0; k < 9; k++) sb.Append(':').Append(r.ReadUInt32().ToString("x")); }
            sb.Append('|').Append(r.ReadBoolean());
            for (int i = 0; i < 7; i++) sb.Append(r.ReadBoolean() ? '1' : '0');
            n = r.ReadInt32();
            sb.Append("|FK").Append(n);
            for (int i = 0; i < n; i++) { sb.Append(' ').Append(r.ReadInt32()); for (int k = 0; k < 9; k++) sb.Append(':').Append(r.ReadUInt32().ToString("x")); }
            sb.Append('|');
            for (int i = 0; i < 4; i++) sb.Append(r.ReadBoolean() ? '1' : '0');
            sb.Append("|rest").Append(r.BaseStream.Length - r.BaseStream.Position);
            return sb.ToString();
        }

        /// <summary>Runs <paramref name="a"/>; returns the PoseFormatException, or null when it did not throw; any other exception is returned as is.</summary>
        public static Exception Catch(Action a)
        {
            try { a(); return null; }
            catch (Exception e) { return e; }
        }
    }
}
