// HS2 Studio pose file (`PauseCtrl`) reader/writer, Unity-free (System only): the binary layout,
// how Studio applies it, the ExtendedSave trailer, the PNG container and the reader rule.
//
// API
//   PoseFile.Read(byte[])            content decides, not the extension: the payload at offset 0;
//                                    else, for a PNG, after the chunk walk to IEND (+ CRC); else at the first offset
//                                    where `0A` + UTF-8 `【pose】` starts a payload that parses (flag PayloadFoundByScan;
//                                    covers PNG poses with extra images and other prefixes). Throws only
//                                    PoseFormatException (code + absolute byte offset).
//   PoseFile.TryRead(byte[], out pose, out error)   never throws.
//   PoseFile.ReadFile(path)          File I/O errors become PoseFormatException(Io).
//   pose.Write()                     ImagePrefix (when not null) + payload + trailer. An unmodified file is reproduced
//                                    byte for byte: strings and non-0/1 bool bytes keep their raw bytes while their value
//                                    is unchanged, floats are stored as raw IEEE bits (−0 and NaN payloads survive).
//   pose.WritePayload(trailer)       the `.dat` form (identifying string onward), with or without the trailer.
//
// Fields, in file order: Identifier (`【pose】`), Version (101), Sex (0 male, 1 female), Name, AnimeGroup /
// AnimeCategory / AnimeNo, NormalizedTime (present when Version >= 101), EnableIK, ActiveIK[5] (Body, RightLeg,
// LeftLeg, RightArm, LeftArm), IK (count × entry; key = IKTargetEN 0..12), EnableFK, ActiveFK[7] (Hair, Neck, Breast,
// Body, RightHand, LeftHand, Skirt), FK (count × entry; key = Bone-list no), Expression[4] (joint-correction
// categories 0..3), Trailer (raw bytes to EOF; null when absent). Entries keep file order and duplicates.
// Derived views: Ext (KKExTrailer decoded from Trailer, or null), PoseTools (typed `PoseToolsData`, or null),
// GameName (ExtendedSave `gameName`, or null). Flags list the anomalies met while reading (none blocks reading).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace HS2Next.Import.Pose
{
    /// <summary>Container a pose came from.</summary>
    public enum PoseContainerKind
    {
        /// <summary>Plain `.dat` (Studio's own format).</summary>
        Dat,
        /// <summary>PNG image followed by the payload (HS2_PoseTools).</summary>
        Png,
    }

    /// <summary>Anomalies seen while reading; none of them stops reading, all are preserved on write.</summary>
    [Flags]
    public enum PoseFileFlags
    {
        None = 0,
        /// <summary>Version is not 101; read as the version implies (normalizedTime only from 101).</summary>
        UnexpectedVersion = 1 << 0,
        /// <summary>An IK key occurs twice (Studio keeps the first).</summary>
        DuplicateIkKey = 1 << 1,
        /// <summary>An FK key occurs twice (Studio's reader throws; HS2Next maps the first, PoseKeyMap).</summary>
        DuplicateFkKey = 1 << 2,
        /// <summary>IK count is not 13 or the keys are not 0..12.</summary>
        NonStandardIk = 1 << 3,
        /// <summary>A bool byte other than 0 / 1 (read as true, kept on write).</summary>
        NonCanonicalBool = 1 << 4,
        /// <summary>A string whose bytes are not what BinaryWriter writes (non-minimal length, invalid UTF-8; kept on write).</summary>
        NonCanonicalString = 1 << 5,
        /// <summary>Bytes after the payload that are not an ExtendedSave `KKEx` block (kept raw).</summary>
        TrailerNotKKEx = 1 << 6,
        /// <summary>A `KKEx` trailer whose length or MessagePack is broken (kept raw).</summary>
        TrailerMalformed = 1 << 7,
        /// <summary>The payload was neither at offset 0 nor right after the PNG's IEND; found by scanning for the identifier.</summary>
        PayloadFoundByScan = 1 << 8,
    }

    /// <summary>Three floats (Unity-free stand-in for Vector3).</summary>
    public struct PoseVector3 : IEquatable<PoseVector3>
    {
        public float X, Y, Z;

        public PoseVector3(float x, float y, float z) { X = x; Y = y; Z = z; }

        public static readonly PoseVector3 Zero = new PoseVector3(0, 0, 0);
        public static readonly PoseVector3 One = new PoseVector3(1, 1, 1);

        public bool Equals(PoseVector3 o) => X.Equals(o.X) && Y.Equals(o.Y) && Z.Equals(o.Z);
        public override bool Equals(object obj) => obj is PoseVector3 v && Equals(v);
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() * 397) ^ (Z.GetHashCode() * 7919);
        public override string ToString() => "(" + X.ToString("R", CultureInfo.InvariantCulture) + ", " + Y.ToString("R", CultureInfo.InvariantCulture) + ", " + Z.ToString("R", CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// One IK or FK record: int key + Studio `ChangeAmount` (pos, rot, scale; 9 float32). The floats are stored as raw
    /// IEEE bits, so −0 and NaN payloads are written back exactly; the Vector views convert on access.
    /// </summary>
    public sealed class PoseEntry
    {
        /// <summary>IK: IKTargetEN (0..12). FK: Bone-list no.</summary>
        public int Key;
        readonly uint[] bits = new uint[9];

        /// <summary>Absolute offset of the record in the source file, or -1 for a record made in code.</summary>
        public int SourceOffset { get; internal set; } = -1;

        /// <summary>Pos 0, rot 0, scale 1 (Studio's reset of a pose entry).</summary>
        public PoseEntry(int key)
        {
            Key = key;
            bits[6] = bits[7] = bits[8] = 0x3F800000u;
        }

        public PoseEntry(int key, PoseVector3 pos, PoseVector3 rot, PoseVector3 scale) : this(key)
        {
            Pos = pos; Rot = rot; Scale = scale;
        }

        /// <summary>Position (FK: unused by vanilla, plugin hips height on `cf_J_Hips`; IK: target position in character-root space).</summary>
        public PoseVector3 Pos { get => V(0); set => S(0, value); }
        /// <summary>Unity Euler degrees (FK: local rotation; IK: hands/feet target rotation).</summary>
        public PoseVector3 Rot { get => V(3); set => S(3, value); }
        /// <summary>Scale (unused; 1 in every corpus file).</summary>
        public PoseVector3 Scale { get => V(6); set => S(6, value); }

        /// <summary>Component i of pos.xyz, rot.xyz, scale.xyz (0..8).</summary>
        public float this[int i] { get => FloatBits.ToFloat(bits[i]); set => bits[i] = FloatBits.ToBits(value); }

        /// <summary>Raw IEEE-754 bits of component i (0..8).</summary>
        public uint GetBits(int i) => bits[i];
        public void SetBits(int i, uint value) => bits[i] = value;

        PoseVector3 V(int o) => new PoseVector3(FloatBits.ToFloat(bits[o]), FloatBits.ToFloat(bits[o + 1]), FloatBits.ToFloat(bits[o + 2]));
        void S(int o, PoseVector3 v) { bits[o] = FloatBits.ToBits(v.X); bits[o + 1] = FloatBits.ToBits(v.Y); bits[o + 2] = FloatBits.ToBits(v.Z); }

        public PoseEntry Clone()
        {
            var c = new PoseEntry(Key) { SourceOffset = SourceOffset };
            Array.Copy(bits, c.bits, 9);
            return c;
        }

        /// <summary>Same key and the same 9 bit patterns.</summary>
        public bool BitwiseEquals(PoseEntry o)
        {
            if (o == null || o.Key != Key) return false;
            for (int i = 0; i < 9; i++) if (bits[i] != o.bits[i]) return false;
            return true;
        }

        public override string ToString() => $"{Key}: pos {Pos} rot {Rot} scale {Scale}";
    }

    /// <summary>An HS2 Studio pose file (see the header of this file for the API).</summary>
    public sealed class PoseFile
    {
        /// <summary>The identifying string.</summary>
        public const string Magic = "【pose】";
        /// <summary>`PauseCtrl.saveVersion`.</summary>
        public const int CurrentVersion = 101;
        /// <summary>Bytes of one IK/FK record (int32 key + 9 float32).</summary>
        public const int EntrySize = 40;
        /// <summary>Number of bool fields (enableIK, activeIK[5], enableFK, activeFK[7], expression[4]).</summary>
        const int BoolCount = 18;

        /// <summary>`OICharInfo.IKTargetEN` names by key.</summary>
        public static readonly string[] IkTargetNames =
        {
            "Body", "LeftShoulder", "LeftArmChain", "LeftHand", "RightShoulder", "RightArmChain", "RightHand",
            "LeftThigh", "LeftLegChain", "LeftFoot", "RightThigh", "RightLegChain", "RightFoot",
        };
        /// <summary>IK groups by ActiveIK index (`BoneGroup` 1 << i).</summary>
        public static readonly string[] IkGroupNames = { "Body", "RightLeg", "LeftLeg", "RightArm", "LeftArm" };
        /// <summary>FK groups by ActiveFK index (`FKCtrl.parts`).</summary>
        public static readonly string[] FkGroupNames = { "Hair", "Neck", "Breast", "Body", "RightHand", "LeftHand", "Skirt" };

        /// <summary>Png when the file starts with a PNG image (ImagePrefix), else Dat.</summary>
        public PoseContainerKind Kind { get; set; } = PoseContainerKind.Dat;
        /// <summary>The bytes before the payload (PNG: the image through the IEND CRC, or more when the payload was found
        /// by scanning), or null. Write() emits it whenever it is not null; a Png pose needs one.</summary>
        public byte[] ImagePrefix { get; set; }

        public int Version { get; set; } = CurrentVersion;
        /// <summary>0 male, 1 female (Studio lists by it; the payload does not depend on it).</summary>
        public int Sex { get; set; } = 1;
        public string Name { get; set; } = "";
        /// <summary>Studio anime record.</summary>
        public int AnimeGroup { get; set; } = -1;
        public int AnimeCategory { get; set; } = -1;
        public int AnimeNo { get; set; } = -1;
        /// <summary>Raw layer-0 normalized time (may exceed 1). Written only when Version >= 101.</summary>
        public float NormalizedTime { get => FloatBits.ToFloat(NormalizedTimeBits); set => NormalizedTimeBits = FloatBits.ToBits(value); }
        public uint NormalizedTimeBits { get; set; }
        public bool HasNormalizedTime => Version >= 101;

        public bool EnableIK { get; set; }
        /// <summary>Body, RightLeg, LeftLeg, RightArm, LeftArm (Studio default all true).</summary>
        public bool[] ActiveIK { get; } = { true, true, true, true, true };
        /// <summary>IK targets in file order (13 in every corpus file).</summary>
        public List<PoseEntry> IK { get; } = new List<PoseEntry>();
        public bool EnableFK { get; set; }
        /// <summary>Hair, Neck, Breast, Body, RightHand, LeftHand, Skirt (Studio default Neck + Body).</summary>
        public bool[] ActiveFK { get; } = { false, true, false, true, false, false, false };
        /// <summary>FK bones in file order (Bone-list no; duplicates kept).</summary>
        public List<PoseEntry> FK { get; } = new List<PoseEntry>();
        /// <summary>Joint-correction categories 0..3 (default all true).</summary>
        public bool[] Expression { get; } = { true, true, true, true };

        byte[] trailer;
        /// <summary>Bytes after the payload (ExtendedSave block written by plugins), or null. Written as is.</summary>
        public byte[] Trailer
        {
            get => trailer;
            set { trailer = value != null && value.Length == 0 ? null : value; extFor = null; ext = null; }
        }

        KKExTrailer ext;
        byte[] extFor;
        /// <summary>The trailer decoded as an ExtendedSave `KKEx` block (null without a trailer). Recomputed when
        /// <see cref="Trailer"/> is assigned; check <see cref="KKExTrailer.Status"/>.</summary>
        public KKExTrailer Ext
        {
            get
            {
                if (trailer == null) return null;
                if (!ReferenceEquals(extFor, trailer)) { ext = KKExTrailer.Parse(trailer); extFor = trailer; }
                return ext;
            }
        }

        /// <summary>`PoseToolsData` (HS2_PoseTools) from the trailer, or null.</summary>
        public PoseToolsData PoseTools
        {
            get
            {
                var e = Ext?.Find(PoseToolsData.Guid);
                return e == null ? null : PoseToolsData.From(e);
            }
        }

        /// <summary>ExtendedSave `gameName` (4 = HS2), or null.</summary>
        public int? GameName
        {
            get
            {
                var e = Ext?.Find(KKExTrailer.ExtendedSaveGuid);
                return e?.Data is Dictionary<object, object> m && m.TryGetValue("gameName", out var v) && PoseMsgPack.TryInt(v, out long g) ? (int?)g : null;
            }
        }

        /// <summary>Anomalies seen while reading.</summary>
        public PoseFileFlags Flags { get; private set; }
        /// <summary>Absolute offset of the identifying string in the source (PNG: the image length), or -1.</summary>
        public int PayloadOffset { get; private set; } = -1;
        /// <summary>Absolute offset where the trailer starts in the source (= end of the payload), or -1.</summary>
        public int PayloadEnd { get; private set; } = -1;

        // raw bytes kept for byte-identical writes
        byte[] identRaw;
        RawString nameRaw;
        byte[] boolRaw;

        // ------------------------------------------------------------------ reading

        /// <summary>Reads a `.dat` or PNG pose (reader rule in the file header). Throws only <see cref="PoseFormatException"/>.</summary>
        public static PoseFile Read(byte[] data)
        {
            if (data == null) throw new PoseFormatException(PoseErrorCode.Io, -1, "no data");
            try
            {
                if (StartsWithMagic(data, 0)) return ReadPayload(data, 0);
                bool png = PosePng.HasSignature(data);
                int img = -1;
                PoseFormatException walk = null;
                if (png)
                {
                    try { img = PosePng.ImageLength(data); }
                    catch (PoseFormatException e) { walk = e; }
                    if (img >= 0 && StartsWithMagic(data, img)) return WithPrefix(ReadPayload(data, img), data, img, false);
                }
                // fallback: the first offset where the identifier starts a payload that parses
                PoseFormatException firstBroken = null;
                for (int p = Math.Max(1, img); (p = NextMagic(data, p)) >= 0; p++)
                {
                    try { return WithPrefix(ReadPayload(data, p), data, p, true); }
                    catch (PoseFormatException e) { firstBroken = firstBroken ?? e; }
                }
                if (!png) return ReadPayload(data, 0); // throws BadMagic / Truncated at the start
                if (walk != null) throw walk;
                if (firstBroken != null) throw firstBroken;
                throw new PoseFormatException(PoseErrorCode.NoPayload, img, img == data.Length
                    ? "PNG ends at IEND (a plain image, not a pose)"
                    : (data.Length - img) + " bytes after IEND without a pose payload");
            }
            catch (PoseFormatException) { throw; }
            catch (Exception e)
            {
                // Defensive: the reader is bounds-checked; anything else is a reader bug, still reported as a typed error.
                throw new PoseFormatException(PoseErrorCode.Internal, -1, "unexpected " + e.GetType().Name + ": " + e.Message, e);
            }
        }

        static readonly byte[] MagicBytes = RawString.Encode(Magic); // 0A + 10 UTF-8 bytes

        static int NextMagic(byte[] d, int from)
        {
            int last = d.Length - MagicBytes.Length;
            for (int p = Math.Max(0, from); p <= last; p++)
            {
                if (d[p] != MagicBytes[0]) continue;
                int k = 1;
                while (k < MagicBytes.Length && d[p + k] == MagicBytes[k]) k++;
                if (k == MagicBytes.Length) return p;
            }
            return -1;
        }

        static PoseFile WithPrefix(PoseFile f, byte[] data, int start, bool scanned)
        {
            var prefix = new byte[start];
            Buffer.BlockCopy(data, 0, prefix, 0, start);
            f.ImagePrefix = prefix;
            f.Kind = PosePng.HasSignature(data) ? PoseContainerKind.Png : PoseContainerKind.Dat;
            if (scanned) f.Flags |= PoseFileFlags.PayloadFoundByScan;
            return f;
        }

        /// <summary><see cref="Read"/> without exceptions.</summary>
        public static bool TryRead(byte[] data, out PoseFile pose, out PoseFormatException error)
        {
            try { pose = Read(data); error = null; return true; }
            catch (PoseFormatException e) { pose = null; error = e; return false; }
        }

        /// <summary>Reads a file; I/O failures become <see cref="PoseErrorCode.Io"/>.</summary>
        public static PoseFile ReadFile(string path)
        {
            byte[] data;
            try { data = File.ReadAllBytes(path); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is NotSupportedException || e is System.Security.SecurityException)
            {
                throw new PoseFormatException(PoseErrorCode.Io, -1, e.GetType().Name + ": " + e.Message, e);
            }
            return Read(data);
        }

        /// <summary>True when a complete identifying string `【pose】` starts at <paramref name="offset"/>.</summary>
        public static bool StartsWithMagic(byte[] data, int offset)
        {
            if (data == null || offset < 0 || offset >= data.Length) return false;
            try { return new PoseReader(data, offset, data.Length).String("identifying string").Value == Magic; }
            catch (PoseFormatException) { return false; }
        }

        static PoseFile ReadPayload(byte[] d, int start)
        {
            var r = new PoseReader(d, start, d.Length);
            var f = new PoseFile { PayloadOffset = start };
            var id = r.String("identifying string");
            if (id.Value != Magic) throw new PoseFormatException(PoseErrorCode.BadMagic, start, "identifying string is not " + Magic);
            f.identRaw = id.Raw;
            if (!id.Canonical) f.Flags |= PoseFileFlags.NonCanonicalString;
            f.Version = r.Int32("version");
            if (f.Version != CurrentVersion) f.Flags |= PoseFileFlags.UnexpectedVersion;
            f.Sex = r.Int32("sex");
            f.nameRaw = r.String("name");
            f.Name = f.nameRaw.Value;
            if (!f.nameRaw.Canonical) f.Flags |= PoseFileFlags.NonCanonicalString;
            f.AnimeGroup = r.Int32("anime group");
            f.AnimeCategory = r.Int32("anime category");
            f.AnimeNo = r.Int32("anime no");
            if (f.Version >= 101) f.NormalizedTimeBits = r.UInt32("normalizedTime");

            var raw = new byte[BoolCount];
            int bi = 0;
            bool B(string what) { byte b = r.Byte(what); raw[bi++] = b; return b != 0; }
            f.EnableIK = B("enableIK");
            for (int i = 0; i < 5; i++) f.ActiveIK[i] = B("activeIK");
            ReadEntries(r, f.IK, "IK");
            f.EnableFK = B("enableFK");
            for (int i = 0; i < 7; i++) f.ActiveFK[i] = B("activeFK");
            ReadEntries(r, f.FK, "FK");
            for (int i = 0; i < 4; i++) f.Expression[i] = B("expression");
            foreach (var b in raw) if (b > 1) { f.boolRaw = raw; f.Flags |= PoseFileFlags.NonCanonicalBool; break; }

            f.PayloadEnd = r.Pos;
            if (r.Remaining > 0) f.Trailer = r.Slice(r.Pos, r.Remaining);

            var ik = new HashSet<int>();
            bool dupIk = false, standard = f.IK.Count == 13;
            foreach (var e in f.IK) { if (!ik.Add(e.Key)) dupIk = true; if (e.Key < 0 || e.Key > 12) standard = false; }
            if (dupIk) f.Flags |= PoseFileFlags.DuplicateIkKey;
            if (!standard || dupIk) f.Flags |= PoseFileFlags.NonStandardIk;
            var fk = new HashSet<int>();
            foreach (var e in f.FK) if (!fk.Add(e.Key)) { f.Flags |= PoseFileFlags.DuplicateFkKey; break; }
            var x = f.Ext;
            if (x != null)
            {
                if (x.Status == KKExStatus.NotKKEx) f.Flags |= PoseFileFlags.TrailerNotKKEx;
                else if (x.Status == KKExStatus.Malformed) f.Flags |= PoseFileFlags.TrailerMalformed;
            }
            return f;
        }

        static void ReadEntries(PoseReader r, List<PoseEntry> list, string what)
        {
            int at = r.Pos;
            int n = r.Int32(what + " count");
            if (n < 0 || (long)n * EntrySize > r.Remaining)
                throw new PoseFormatException(PoseErrorCode.CountOutOfRange, at, $"{what} count {n} needs {(long)n * EntrySize} bytes, {r.Remaining} left");
            list.Capacity = Math.Max(list.Capacity, n);
            for (int i = 0; i < n; i++)
            {
                var e = new PoseEntry(r.Int32(what + " key")) { SourceOffset = r.Pos - 4 };
                for (int k = 0; k < 9; k++) e.SetBits(k, r.UInt32(what + " value"));
                list.Add(e);
            }
        }

        // ------------------------------------------------------------------ writing

        /// <summary>ImagePrefix (when not null) + payload + trailer.</summary>
        public byte[] Write()
        {
            if (Kind == PoseContainerKind.Png && (ImagePrefix == null || ImagePrefix.Length == 0))
                throw new InvalidOperationException("a PNG pose needs ImagePrefix");
            var w = new PoseWriter(256 + (IK.Count + FK.Count) * EntrySize + (ImagePrefix?.Length ?? 0) + (trailer?.Length ?? 0));
            w.Bytes(ImagePrefix);
            WritePayloadTo(w, true);
            return w.ToArray();
        }

        /// <summary>The `.dat` form: identifying string onward, with or without the trailer.</summary>
        public byte[] WritePayload(bool includeTrailer = true)
        {
            var w = new PoseWriter(256 + (IK.Count + FK.Count) * EntrySize + (trailer?.Length ?? 0));
            WritePayloadTo(w, includeTrailer);
            return w.ToArray();
        }

        void WritePayloadTo(PoseWriter w, bool includeTrailer)
        {
            w.Bytes(identRaw ?? RawString.Encode(Magic));
            w.Int32(Version);
            w.Int32(Sex);
            w.Bytes(nameRaw.Raw != null && string.Equals(Name, nameRaw.Value, StringComparison.Ordinal) ? nameRaw.Raw : RawString.Encode(Name));
            w.Int32(AnimeGroup);
            w.Int32(AnimeCategory);
            w.Int32(AnimeNo);
            if (Version >= 101) w.UInt32(NormalizedTimeBits);
            int bi = 0;
            void B(bool v) { byte r = boolRaw?[bi] ?? 0; bi++; w.Byte(v ? (r > 1 ? r : (byte)1) : (byte)0); }
            B(EnableIK);
            for (int i = 0; i < 5; i++) B(ActiveIK[i]);
            WriteEntries(w, IK, "IK");
            B(EnableFK);
            for (int i = 0; i < 7; i++) B(ActiveFK[i]);
            WriteEntries(w, FK, "FK");
            for (int i = 0; i < 4; i++) B(Expression[i]);
            if (includeTrailer) w.Bytes(trailer);
        }

        static void WriteEntries(PoseWriter w, List<PoseEntry> list, string what)
        {
            w.Int32(list.Count);
            foreach (var e in list)
            {
                if (e == null) throw new InvalidOperationException(what + " list contains null");
                w.Int32(e.Key);
                for (int k = 0; k < 9; k++) w.UInt32(e.GetBits(k));
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>First FK record with this key, or null.</summary>
        public PoseEntry FindFk(int key) { foreach (var e in FK) if (e.Key == key) return e; return null; }
        /// <summary>First IK record with this key, or null (Studio keeps the first of duplicate IK keys).</summary>
        public PoseEntry FindIk(int key) { foreach (var e in IK) if (e.Key == key) return e; return null; }

        public override string ToString() =>
            $"{Kind} v{Version} sex {Sex} \"{Name}\" anime {AnimeGroup}/{AnimeCategory}/{AnimeNo} t={NormalizedTime.ToString("R", CultureInfo.InvariantCulture)} IK {(EnableIK ? "on" : "off")} {IK.Count} FK {(EnableFK ? "on" : "off")} {FK.Count} trailer {trailer?.Length ?? 0}";
    }
}
