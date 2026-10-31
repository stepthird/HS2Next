// Container reader, byte level: detection (EOCD search,
// absolute or relative offsets, PNG prefix walk, HS2 card/coordinate/autosave codes), validation
// (Zip64, multi-disk, encryption, methods, unsafe and duplicate names, size limits) and
// materialisation of every entry: inflated with the declared size as a hard cap and CRC-32 checked
// here (not by the platform ZipArchive). The source is read once and closed; nothing keeps a
// handle (Windows File.Replace on the same path must work right after loading). Interpreting the
// manifest and documents is the container layer's job.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace HS2Next.Save
{
    /// <summary>One entry as stored: name, inflated bytes (null when corrupt or not requested) and the reason it failed.</summary>
    public sealed class RawEntry
    {
        public string Name;
        public byte[] Data;
        /// <summary>Null when <see cref="Data"/> is good; else why the entry is unusable (CRC, size, inflate error).</summary>
        public string Error;
        /// <summary>True when the entry was not inflated because the caller did not ask for it.</summary>
        public bool Skipped;
        public long Size, CompressedSize;
        public uint Crc;
        public int Method;
        /// <summary>True when the name satisfies the grammar.</summary>
        public bool Conforming;
    }

    public sealed class ContainerReader
    {
        const int MaxTail = 65557;                           // 22-byte EOCD + 65,535-byte comment

        /// <summary>PNG thumbnail bytes (signature to the end of IEND), or null (plain ZIP, unknown or corrupt prefix).</summary>
        public byte[] Thumbnail;
        /// <summary>IHDR of <see cref="Thumbnail"/>.</summary>
        public PngHeader? ThumbnailHeader;
        /// <summary>Length of the bytes before the first local header (0 for a plain ZIP).</summary>
        public long PrefixLength;
        /// <summary>Base of the archive offsets: 0 for absolute offsets (every HS2Next writer), else the relative-offset form.</summary>
        public long Base;
        /// <summary>Entries in central-directory order, directory entries left out.</summary>
        public readonly List<RawEntry> Entries = new List<RawEntry>();
        public readonly FindingList Findings;

        readonly Dictionary<string, RawEntry> byName = new Dictionary<string, RawEntry>(StringComparer.Ordinal);
        readonly ContainerLimits limits;

        ContainerReader(FindingList findings, ContainerLimits limits)
        {
            Findings = findings ?? new FindingList();
            this.limits = limits ?? ContainerLimits.Default;
        }

        public RawEntry Get(string name) => byName.TryGetValue(name, out var e) ? e : null;

        public bool Contains(string name) => byName.ContainsKey(name);

        /// <summary>Entry names in central-directory order.</summary>
        public IEnumerable<string> Names
        {
            get { foreach (var e in Entries) yield return e.Name; }
        }

        /// <summary>The inflated bytes of an entry; null when absent; EntryCorrupt when unreadable or not inflated.</summary>
        public byte[] ReadEntry(string name)
        {
            var e = Get(name);
            if (e == null) return null;
            if (e.Data == null) throw new SaveFormatException(SaveErrorCode.EntryCorrupt, e.Skipped ? "entry not inflated" : e.Error ?? "unreadable", name);
            return e.Data;
        }

        /// <summary>The manifest without loading the documents (library lists): NotHs2Next when absent or not hs2next.*, BadManifest when malformed.</summary>
        public Manifest ReadManifest(FindingList findings = null)
        {
            var b = ReadEntry(EntryNames.Manifest) ?? throw new SaveFormatException(SaveErrorCode.NotHs2Next, "no manifest.json");
            var m = Manifest.Read(DocReader.ForDocument(b, new DocReadContext(EntryNames.Manifest, false, findings, SaveErrorCode.BadManifest)));
            if (m.Format == null || !m.Format.StartsWith(SaveFormats.Prefix, StringComparison.Ordinal))
                throw new SaveFormatException(SaveErrorCode.NotHs2Next, "manifest format is not hs2next.*", EntryNames.Manifest, "format");
            return m;
        }

        /// <summary>Reads a container file. The file is opened read-only, fully materialised and closed before returning.</summary>
        /// <param name="inflate">Which entries to inflate (null = all); the others are listed with <see cref="RawEntry.Skipped"/>.</param>
        public static ContainerReader Read(string path, FindingList findings = null, Func<string, bool> inflate = null, ContainerLimits limits = null)
        {
            FileStream fs;
            try { fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 1 << 16); }
            catch (FileNotFoundException e) { throw new SaveFormatException(SaveErrorCode.IoFailure, "file not found", null, null, 0, e); }
            catch (DirectoryNotFoundException e) { throw new SaveFormatException(SaveErrorCode.IoFailure, "folder not found", null, null, 0, e); }
            catch (IOException e) { throw new SaveFormatException(SaveErrorCode.IoFailure, e.Message, null, null, 0, e); }
            catch (UnauthorizedAccessException e) { throw new SaveFormatException(SaveErrorCode.IoFailure, e.Message, null, null, 0, e); }
            using (fs) return Read(fs, findings, inflate, limits);
        }

        public static ContainerReader Read(byte[] bytes, FindingList findings = null, Func<string, bool> inflate = null, ContainerLimits limits = null)
        {
            using (var ms = new MemoryStream(bytes, false)) return Read(ms, findings, inflate, limits);
        }

        /// <summary>Reads from a seekable stream (left open; its position is changed).</summary>
        public static ContainerReader Read(Stream s, FindingList findings = null, Func<string, bool> inflate = null, ContainerLimits limits = null)
        {
            if (!s.CanSeek) throw new ArgumentException("the container reader needs a seekable stream", nameof(s));
            var r = new ContainerReader(findings, limits);
            try { r.Load(s, inflate); }
            catch (EndOfStreamException e) { throw new SaveFormatException(SaveErrorCode.CorruptContainer, "unexpected end of file", null, null, 0, e); }
            catch (IOException e) when (!(e is EndOfStreamException)) { throw new SaveFormatException(SaveErrorCode.IoFailure, e.Message, null, null, 0, e); }
            return r;
        }

        static void ReadExact(Stream s, long pos, byte[] buf, int count)
        {
            s.Position = pos;
            int got = 0;
            while (got < count)
            {
                int n = s.Read(buf, got, count - got);
                if (n <= 0) throw new EndOfStreamException();
                got += n;
            }
        }

        static ushort U16(byte[] b, int o) => (ushort)(b[o] | b[o + 1] << 8);
        static uint U32(byte[] b, int o) => (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24);

        internal static bool IsDocumentName(string n) =>
            n == EntryNames.Manifest || n == EntryNames.CharaDoc || n == EntryNames.CoordDoc || n == EntryNames.SceneDoc || n == PoseDocument.EntryName ||
            n == "coord/coord.json" || (n.StartsWith("charas/", StringComparison.Ordinal) && (n.EndsWith("/chara.json", StringComparison.Ordinal) || n.EndsWith("/coord/coord.json", StringComparison.Ordinal)));

        void Load(Stream s, Func<string, bool> inflate)
        {
            long fileLen = s.Length;
            if (fileLen < 22) { Fail(s, fileLen, "file too short"); return; }

            // 1. EOCD: signature at q with q + 22 + commentLength = file length (search backwards).
            int tailLen = (int)Math.Min(fileLen, MaxTail);
            var tail = new byte[tailLen];
            ReadExact(s, fileLen - tailLen, tail, tailLen);
            int qi = -1;
            for (int i = tailLen - 22; i >= 0; i--)
            {
                if (tail[i] == 0x50 && tail[i + 1] == 0x4B && tail[i + 2] == 5 && tail[i + 3] == 6 && i + 22 + U16(tail, i + 20) == tailLen)
                {
                    qi = i;
                    break;
                }
            }
            if (qi < 0) { Fail(s, fileLen, "no end-of-central-directory record"); return; }
            long q = fileLen - tailLen + qi;

            // 2. Disk numbers, counts, Zip64 markers.
            ushort disk = U16(tail, qi + 4), cdDisk = U16(tail, qi + 6), nDisk = U16(tail, qi + 8), nTotal = U16(tail, qi + 10);
            uint cdSize = U32(tail, qi + 12), cdOffset = U32(tail, qi + 16);
            if (nDisk == 0xFFFF || nTotal == 0xFFFF || cdSize == 0xFFFFFFFF || cdOffset == 0xFFFFFFFF)
                throw new SaveFormatException(SaveErrorCode.UnsupportedZip, "Zip64 archive");
            if (disk != 0 || cdDisk != 0 || nDisk != nTotal)
                throw new SaveFormatException(SaveErrorCode.UnsupportedZip, "multi-disk archive");
            if (q >= 4)
            {
                var loc = new byte[4];
                if (q >= 20) { ReadExact(s, q - 20, loc, 4); if (U32(loc, 0) == 0x07064B50) throw new SaveFormatException(SaveErrorCode.UnsupportedZip, "Zip64 locator"); }
            }

            // 3. Absolute or relative offsets.
            if ((long)cdOffset + cdSize == q) Base = 0;
            else
            {
                long b = q - ((long)cdOffset + cdSize);
                var sig = new byte[4];
                if (b > 0 && b + cdOffset + 4 <= q + 4 && b + cdOffset + 4 <= fileLen)
                {
                    ReadExact(s, b + cdOffset, sig, 4);
                    if (U32(sig, 0) != 0x02014B50 && nTotal > 0) b = -1;
                }
                else b = -1;
                if (b <= 0) { CorruptOrCard(s, fileLen, "central directory offsets inconsistent"); return; }
                Base = b;
                Findings.Info("container.relativeOffsets", "", "archive offsets are relative to a prefix of " + b + " bytes");
            }

            // Central directory.
            long cdStart = Base + cdOffset;
            if (cdSize > int.MaxValue || cdStart < 0 || cdStart + cdSize > q) { CorruptOrCard(s, fileLen, "central directory out of range"); return; }
            var cd = new byte[cdSize];
            ReadExact(s, cdStart, cd, (int)cdSize);
            var lhos = new List<KeyValuePair<RawEntry, long>>();
            int p = 0;
            long total = 0;
            for (int i = 0; i < nTotal; i++)
            {
                if (p + 46 > cd.Length || U32(cd, p) != 0x02014B50) { CorruptOrCard(s, fileLen, "central directory entry " + i + " broken"); return; }
                ushort flags = U16(cd, p + 8), method = U16(cd, p + 10);
                uint crc = U32(cd, p + 16), csize = U32(cd, p + 20), usize = U32(cd, p + 24);
                int nlen = U16(cd, p + 28), xlen = U16(cd, p + 30), clen = U16(cd, p + 32);
                ushort startDisk = U16(cd, p + 34);
                uint lho = U32(cd, p + 42);
                if (p + 46 + nlen + xlen + clen > cd.Length) { CorruptOrCard(s, fileLen, "central directory entry " + i + " truncated"); return; }
                string name = DecodeName(cd, p + 46, nlen, (flags & 0x800) != 0);
                p += 46 + nlen + xlen + clen;

                string unsafeWhy = EntryNames.Unsafe(name);
                if (unsafeWhy != null) throw new SaveFormatException(SaveErrorCode.UnsafeEntryName, unsafeWhy + ": \"" + Printable(name) + "\"");
                if (csize == 0xFFFFFFFF || usize == 0xFFFFFFFF || lho == 0xFFFFFFFF || startDisk == 0xFFFF)
                    throw new SaveFormatException(SaveErrorCode.UnsupportedZip, "Zip64 entry", name);
                if (startDisk != 0) throw new SaveFormatException(SaveErrorCode.UnsupportedZip, "multi-disk entry", name);
                if ((flags & 0x41) != 0) throw new SaveFormatException(SaveErrorCode.UnsupportedZip, "encrypted entry", name);
                if (method != 0 && method != 8) throw new SaveFormatException(SaveErrorCode.UnsupportedZip, "compression method " + method, name);
                if (EntryNames.IsDirectory(name)) continue;
                if (byName.ContainsKey(name)) throw new SaveFormatException(SaveErrorCode.DuplicateEntry, "duplicate entry", name);
                if (usize > limits.MaxEntrySize) throw new SaveFormatException(SaveErrorCode.TooLarge, "entry over the size limit", name);
                if (usize > limits.MaxDocumentSize && IsDocumentName(name)) throw new SaveFormatException(SaveErrorCode.TooLarge, "JSON document over the size limit", name);
                total += usize;
                if (total > limits.MaxTotalSize) throw new SaveFormatException(SaveErrorCode.TooLarge, "uncompressed total over the limit");
                if (method == 0 && csize != usize) throw new SaveFormatException(SaveErrorCode.CorruptContainer, "stored entry with different sizes", name);
                var e = new RawEntry
                {
                    Name = name, Size = usize, CompressedSize = csize, Crc = crc, Method = method,
                    Conforming = EntryNames.IsConforming(name),
                };
                Entries.Add(e);
                byName[name] = e;
                lhos.Add(new KeyValuePair<RawEntry, long>(e, Base + lho));
            }

            // 4. Prefix and thumbnail.
            long firstLocal = cdStart;
            foreach (var kv in lhos) if (kv.Value < firstLocal) firstLocal = kv.Value;
            PrefixLength = firstLocal;
            if (firstLocal > 0)
            {
                if (firstLocal > int.MaxValue) throw new SaveFormatException(SaveErrorCode.TooLarge, "prefix over 2 GiB");
                var prefix = new byte[firstLocal];
                ReadExact(s, 0, prefix, (int)firstLocal);
                if (PngInfo.HasSignature(prefix))
                {
                    long end = PngInfo.FindEnd(prefix, prefix.Length, out var h);
                    if (end == prefix.Length)
                    {
                        Thumbnail = prefix;
                        ThumbnailHeader = h;
                    }
                    else Findings.Warning("ThumbnailCorrupt", "", "the PNG prefix is broken or does not end where the archive starts");
                }
                else Findings.Warning("UnknownPrefix", "", firstLocal + " bytes before the archive are not a PNG");
            }

            // Entries: local headers, inflate with the declared size as cap, CRC-32.
            var lh = new byte[30];
            foreach (var kv in lhos)
            {
                var e = kv.Key;
                long at = kv.Value;
                if (at < 0 || at + 30 > cdStart) throw new SaveFormatException(SaveErrorCode.CorruptContainer, "local header out of range", e.Name);
                ReadExact(s, at, lh, 30);
                if (U32(lh, 0) != 0x04034B50) throw new SaveFormatException(SaveErrorCode.CorruptContainer, "local header signature", e.Name);
                int nlen = U16(lh, 26), xlen = U16(lh, 28);
                long dataStart = at + 30 + nlen + xlen;
                if (dataStart + e.CompressedSize > cdStart) throw new SaveFormatException(SaveErrorCode.CorruptContainer, "entry data out of range", e.Name);
                var nameBytes = new byte[nlen];
                ReadExact(s, at + 30, nameBytes, nlen);
                if (DecodeName(nameBytes, 0, nlen, (U16(lh, 6) & 0x800) != 0) != e.Name)
                    throw new SaveFormatException(SaveErrorCode.CorruptContainer, "local and central names differ", e.Name);
                if (inflate != null && !inflate(e.Name)) { e.Skipped = true; continue; }
                e.Data = Inflate(s, dataStart, e, out e.Error);
            }
        }

        static byte[] Inflate(Stream s, long dataStart, RawEntry e, out string error)
        {
            error = null;
            var buf = new byte[e.Size];
            try
            {
                var sub = new SubReadStream(s, dataStart, e.CompressedSize);
                if (e.Method == 0)
                {
                    ReadAll(sub, buf);
                }
                else
                {
                    using (var ds = new DeflateStream(sub, CompressionMode.Decompress, true))
                    {
                        int got = ReadAll(ds, buf);
                        if (got < buf.Length) { error = "inflated " + got + " bytes, declared " + e.Size; return null; }
                        var one = new byte[1];
                        if (ds.Read(one, 0, 1) > 0) { error = "inflates to more than the declared " + e.Size + " bytes"; return null; }
                    }
                }
            }
            catch (InvalidDataException x) { error = "inflate error: " + x.Message; return null; }
            catch (EndOfStreamException) { error = "data truncated"; return null; }
            uint crc = Crc32.Compute(buf);
            if (crc != e.Crc) { error = "CRC-32 mismatch"; return null; }
            return buf;
        }

        static int ReadAll(Stream src, byte[] buf)
        {
            int got = 0;
            while (got < buf.Length)
            {
                int n = src.Read(buf, got, Math.Min(buf.Length - got, 1 << 20));
                if (n <= 0) break;
                got += n;
            }
            return got;
        }

        static string DecodeName(byte[] b, int offset, int count, bool utf8Flag)
        {
            // HS2Next names are ASCII; foreign names are taken as UTF-8 (with or without the flag).
            return Encoding.UTF8.GetString(b, offset, count);
        }

        static string Printable(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s) sb.Append(c < 0x20 || c == 0x7F ? "\\x" + ((int)c).ToString("X2") : c.ToString());
            return sb.ToString();
        }

        // ---------- step 6: not a ZIP (or a broken one) ----------

        void Fail(Stream s, long fileLen, string why)
        {
            var head = new byte[(int)Math.Min(fileLen, 1 << 16)];
            ReadExact(s, 0, head, head.Length);
            var code = DetectHs2(s, fileLen, head);
            if (code.HasValue) throw new SaveFormatException(code.Value, code.Value == SaveErrorCode.Hs2Card ? "an HS2 character card (use the importer)" : "an HS2 coordinate card (use the importer)");
            // A ZIP that starts but has no end record is a truncated container.
            long zipStart = 0;
            if (PngInfo.HasSignature(head))
            {
                var pngBuf = ReadPrefixForPng(s, fileLen);
                zipStart = PngInfo.FindEnd(pngBuf, pngBuf.Length, out _);
            }
            if (zipStart >= 0 && zipStart + 4 <= fileLen)
            {
                var sig = new byte[4];
                ReadExact(s, zipStart, sig, 4);
                if (U32(sig, 0) == 0x04034B50) throw new SaveFormatException(SaveErrorCode.CorruptContainer, why + " (truncated archive)");
            }
            throw new SaveFormatException(SaveErrorCode.NotAContainer, why);
        }

        void CorruptOrCard(Stream s, long fileLen, string why)
        {
            var head = new byte[(int)Math.Min(fileLen, 1 << 16)];
            ReadExact(s, 0, head, head.Length);
            var code = DetectHs2(s, fileLen, head);
            if (code.HasValue) throw new SaveFormatException(code.Value, "an HS2 card (use the importer)");
            throw new SaveFormatException(SaveErrorCode.CorruptContainer, why);
        }

        static byte[] ReadPrefixForPng(Stream s, long fileLen)
        {
            // HS2 card thumbnails are a few hundred KB; read up to 64 MiB to find IEND.
            var b = new byte[(int)Math.Min(fileLen, 64L << 20)];
            ReadExact(s, 0, b, b.Length);
            return b;
        }

        static readonly string CharaMarker = "【AIS_Chara】";
        static readonly string ClothesMarker = "【AIS_Clothes】";

        /// <summary>PNG + int32 + .NET string marker (or no PNG: a Maker autosave).</summary>
        static SaveErrorCode? DetectHs2(Stream s, long fileLen, byte[] head)
        {
            long at = 0;
            if (PngInfo.HasSignature(head))
            {
                var b = ReadPrefixForPng(s, fileLen);
                at = PngInfo.FindEnd(b, b.Length, out _);
                if (at < 0) return null;
            }
            var buf = new byte[64];
            if (at + 5 > fileLen) return null;
            int n = (int)Math.Min(64, fileLen - at);
            ReadExact(s, at, buf, n);
            // int32 productNo (any value), then 7-bit length + UTF-8.
            int len = 0, shift = 0, i = 4;
            while (i < n)
            {
                byte c = buf[i++];
                len |= (c & 0x7F) << shift;
                if ((c & 0x80) == 0) break;
                shift += 7;
                if (shift > 28) return null;
            }
            if (len <= 0 || i + len > n) return null;
            string marker = Encoding.UTF8.GetString(buf, i, len);
            if (marker == CharaMarker) return SaveErrorCode.Hs2Card;
            if (marker == ClothesMarker) return SaveErrorCode.Hs2Coordinate;
            return null;
        }

        /// <summary>
        /// Classifies a file without loading it: null for a readable HS2Next-looking archive, else the code the
        /// reader would fail with first (NotAContainer, Hs2Card, Hs2Coordinate, CorruptContainer, ...).
        /// </summary>
        public static SaveErrorCode? Probe(string path)
        {
            try { Read(path, null, n => false); return null; }
            catch (SaveFormatException e) { return e.Code; }
        }
    }
}
