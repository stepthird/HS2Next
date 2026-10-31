// Minimal zip central-directory reader (incl. ZIP64) that also reports each entry's data
// offset, so stored (uncompressed) AssetBundles can be opened in place with
// AssetBundle.LoadFromFile(path, 0, offset) — the same trick Sideloader relies on.
// The central directory is read in one piece and parsed in memory (a cold scan of the whole mod folder reads every zip's
// directory; reading it field by field through the file took minutes); an entry's local header is read with its data.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace HS2Next.IO
{
    public class ZipIndex
    {
        public class Entry
        {
            public string Name;
            public int Method;           // 0 = stored, 8 = deflate
            public long CompressedSize, Size, LocalHeaderOffset;
            internal long dataOffset = -1;
            internal ZipIndex Owner;

            public long DataOffset
            {
                get
                {
                    if (dataOffset >= 0) return dataOffset;
                    using (var fs = new FileStream(Owner.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var br = new BinaryReader(fs))
                    {
                        fs.Position = LocalHeaderOffset + 26;
                        int nameLen = br.ReadUInt16(), extraLen = br.ReadUInt16();
                        dataOffset = LocalHeaderOffset + 30 + nameLen + extraLen;
                    }
                    return dataOffset;
                }
            }
        }

        public readonly string Path;
        public readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public ZipIndex(string path)
        {
            Path = path;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            {
                long eocd = FindEocd(fs);
                var end = ReadAt(fs, eocd, 22);
                long count = U16(end, 10);
                long cdSize = U32(end, 12);
                long cdOffset = U32(end, 16);
                if (count == 0xFFFF || cdOffset == 0xFFFFFFFF)
                {
                    // ZIP64 end of central directory locator sits 20 bytes before EOCD.
                    if (eocd >= 20)
                    {
                        var loc = ReadAt(fs, eocd - 20, 20);
                        if (loc.Length == 20 && U32(loc, 0) == 0x07064b50)
                        {
                            long z64 = I64(loc, 8);
                            var rec = ReadAt(fs, z64, 56);
                            if (rec.Length == 56) { count = I64(rec, 32); cdSize = I64(rec, 40); cdOffset = I64(rec, 48); }
                        }
                    }
                }
                // the whole central directory in one read (a truncated directory parses as far as it goes)
                var cd = ReadAt(fs, cdOffset, (int)Math.Max(0, Math.Min(cdSize, Math.Min(int.MaxValue, fs.Length - cdOffset))));
                int p = 0;
                for (long i = 0; i < count; i++)
                {
                    if (p + 46 > cd.Length || U32(cd, p) != 0x02014b50) break;
                    int method = U16(cd, p + 10);
                    long csize = U32(cd, p + 20), usize = U32(cd, p + 24);
                    int nlen = U16(cd, p + 28), xlen = U16(cd, p + 30), clen = U16(cd, p + 32);
                    long lho = U32(cd, p + 42);
                    if (p + 46 + nlen > cd.Length) break;
                    var name = Encoding.UTF8.GetString(cd, p + 46, nlen);
                    int x = p + 46 + nlen, xend = x + xlen;
                    while (x + 4 <= xend && x + 4 <= cd.Length)
                    {
                        int id = U16(cd, x), sz = U16(cd, x + 2);
                        int q = x + 4;
                        if (id == 1)
                        {
                            if (usize == 0xFFFFFFFF && q + 8 <= cd.Length) { usize = I64(cd, q); q += 8; }
                            if (csize == 0xFFFFFFFF && q + 8 <= cd.Length) { csize = I64(cd, q); q += 8; }
                            if (lho == 0xFFFFFFFF && q + 8 <= cd.Length) lho = I64(cd, q);
                        }
                        x += 4 + sz;
                    }
                    p = xend + clen;
                    Entries[name.Replace('\\', '/')] = new Entry
                        { Name = name, Method = method, CompressedSize = csize, Size = usize, LocalHeaderOffset = lho, Owner = this };
                }
            }
        }

        static byte[] ReadAt(FileStream fs, long offset, int count)
        {
            if (offset < 0 || count <= 0) return new byte[0];
            fs.Position = offset;
            var b = new byte[count];
            int got = 0;
            while (got < count) { int n = fs.Read(b, got, count - got); if (n <= 0) break; got += n; }
            if (got != count) Array.Resize(ref b, got);
            return b;
        }

        static int U16(byte[] b, int o) => b[o] | b[o + 1] << 8;
        static uint U32(byte[] b, int o) => (uint)(b[o] | b[o + 1] << 8 | b[o + 2] << 16 | b[o + 3] << 24);
        static long I64(byte[] b, int o) => (long)((ulong)U32(b, o) | (ulong)U32(b, o + 4) << 32);

        static long FindEocd(FileStream fs)
        {
            int max = (int)Math.Min(fs.Length, 65557);
            var buf = new byte[max];
            fs.Position = fs.Length - max;
            fs.Read(buf, 0, max);
            for (int i = max - 22; i >= 0; i--)
                if (buf[i] == 0x50 && buf[i + 1] == 0x4b && buf[i + 2] == 5 && buf[i + 3] == 6)
                    return fs.Length - max + i;
            throw new InvalidDataException("zip: end of central directory not found: " + fs.Name);
        }

        public byte[] ReadAll(Entry e)
        {
            using (var fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (e.dataOffset < 0)
                {
                    // the local header with the same handle (the entry remembers the data offset)
                    var lh = ReadAt(fs, e.LocalHeaderOffset + 26, 4);
                    if (lh.Length == 4) e.dataOffset = e.LocalHeaderOffset + 30 + U16(lh, 0) + U16(lh, 2);
                }
                fs.Position = e.DataOffset;
                if (e.Method == 0)
                {
                    var buf = new byte[e.Size];
                    int got = 0;
                    while (got < buf.Length) { int n = fs.Read(buf, got, buf.Length - got); if (n <= 0) break; got += n; }
                    return buf;
                }
                using (var ds = new DeflateStream(fs, CompressionMode.Decompress))
                using (var ms = new MemoryStream((int)Math.Min(e.Size, int.MaxValue)))
                {
                    ds.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        /// <summary>The entry decoded with <see cref="DecodeText"/>, or null if the zip has no such entry.</summary>
        public string ReadText(string name)
        {
            if (!Entries.TryGetValue(name, out var e)) return null;
            return DecodeText(ReadAll(e));
        }

        /// <summary>
        /// Decodes text like .NET's StreamReader(stream, Encoding.UTF8), which Sideloader reads list files with: a
        /// UTF-8, UTF-16 (LE/BE) or UTF-32 (LE/BE) byte-order mark selects the encoding and is removed; without one
        /// the bytes are UTF-8. Invalid sequences become U+FFFD.
        /// </summary>
        public static string DecodeText(byte[] b)
        {
            int n = b.Length;
            if (n >= 4 && b[0] == 0xFF && b[1] == 0xFE && b[2] == 0 && b[3] == 0) return new UTF32Encoding(false, false).GetString(b, 4, n - 4);
            if (n >= 4 && b[0] == 0 && b[1] == 0 && b[2] == 0xFE && b[3] == 0xFF) return new UTF32Encoding(true, false).GetString(b, 4, n - 4);
            if (n >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) return Encoding.UTF8.GetString(b, 3, n - 3);
            if (n >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, n - 2);
            if (n >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, n - 2);
            return Encoding.UTF8.GetString(b);
        }
    }
}
