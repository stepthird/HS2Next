// Container writer, byte level: PNG prefix, then a ZIP
// written by ZipArchive (Create mode) on the same stream, so central-directory offsets are absolute
// file offsets. Entry order: manifest.json, the main document, then every other entry in ordinal
// byte order. Fixed fields: DOS time 0x0000 / date 0x0021 (1980-01-01 00:00), method 8, no extra
// fields, no comments, no Zip64. Limits are checked before anything touches the disk. Saving is
// atomic: build in memory, write <target>.tmp, flush to disk, optionally verify, then File.Replace
// (or File.Move for a new target); any failure deletes the tmp and leaves the target unchanged.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace HS2Next.Save
{
    public sealed class ContainerWriter
    {
        static readonly DateTimeOffset DosZero = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

        readonly List<KeyValuePair<string, byte[]>> entries = new List<KeyValuePair<string, byte[]>>();
        readonly HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>PNG prefix (written first; null = no prefix, for tests only: writers always write one).</summary>
        public byte[] Thumbnail;
        /// <summary>Name of the main document (written right after manifest.json).</summary>
        public string MainDocument;
        /// <summary>The limits (tests lower them).</summary>
        public ContainerLimits Limits = ContainerLimits.Default;

        /// <summary>Adds an entry. Names must satisfy the entry-name grammar and be unique.</summary>
        public void Add(string name, byte[] data)
        {
            if (!EntryNames.IsConforming(name)) throw new SaveFormatException(SaveErrorCode.UnsafeEntryName, "entry name outside the entry-name grammar: \"" + name + "\"", name);
            if (!names.Add(name)) throw new SaveFormatException(SaveErrorCode.DuplicateEntry, "entry added twice", name);
            entries.Add(new KeyValuePair<string, byte[]>(name, data ?? throw new ArgumentNullException(nameof(data))));
        }

        public bool Contains(string name) => names.Contains(name);

        public int Count => entries.Count;

        /// <summary>Entries order.</summary>
        public List<KeyValuePair<string, byte[]>> Ordered()
        {
            var list = new List<KeyValuePair<string, byte[]>>(entries);
            list.Sort((a, b) =>
            {
                int ra = Rank(a.Key), rb = Rank(b.Key);
                return ra != rb ? ra.CompareTo(rb) : string.CompareOrdinal(a.Key, b.Key);
            });
            return list;
        }

        int Rank(string n) => n == EntryNames.Manifest ? 0 : n == MainDocument ? 1 : 2;

        /// <summary>The checks that can be made before writing (count, entry sizes, total); throws TooLarge.</summary>
        public void CheckLimits()
        {
            if (entries.Count > Limits.MaxEntries) throw new SaveFormatException(SaveErrorCode.TooLarge, "more than " + Limits.MaxEntries + " entries");
            long total = 0;
            foreach (var kv in entries)
            {
                long n = kv.Value.LongLength;
                if (n > Limits.MaxEntrySize) throw new SaveFormatException(SaveErrorCode.TooLarge, "entry over the size limit", kv.Key);
                if (n > Limits.MaxDocumentSize && ContainerReader.IsDocumentName(kv.Key))
                    throw new SaveFormatException(SaveErrorCode.TooLarge, "JSON document over the size limit", kv.Key);
                total += n;
            }
            if (total > Limits.MaxTotalSize) throw new SaveFormatException(SaveErrorCode.TooLarge, "uncompressed total over the limit");
        }

        /// <summary>Writes the whole container to <paramref name="s"/> (positioned at its start; left open).</summary>
        public void WriteTo(Stream s)
        {
            CheckLimits();
            long start = s.Position;
            if (Thumbnail != null) s.Write(Thumbnail, 0, Thumbnail.Length);
            using (var zip = new ZipArchive(s, ZipArchiveMode.Create, true, new UTF8Encoding(false)))
            {
                foreach (var kv in Ordered())
                {
                    var e = zip.CreateEntry(kv.Key, CompressionLevel.Optimal);
                    e.LastWriteTime = DosZero;
                    using (var es = e.Open()) es.Write(kv.Value, 0, kv.Value.Length);
                }
            }
            if (s.Position - start > Limits.MaxFileSize) throw new SaveFormatException(SaveErrorCode.TooLarge, "file over the size limit");
        }

        /// <summary>The container as bytes (in memory).</summary>
        public byte[] ToBytes()
        {
            var ms = new MemoryStream();
            WriteTo(ms);
            return ms.ToArray();
        }

        /// <summary>
        /// Atomic save to <paramref name="target"/>. <paramref name="verify"/> re-opens the tmp file
        /// with the reader (end record, central directory, the named documents' CRC). <paramref name="wrap"/> lets
        /// tests inject faults into the file stream.
        /// </summary>
        public void Save(string target, bool verify = true, IEnumerable<string> verifyEntries = null, Func<Stream, Stream> wrap = null)
        {
            CheckLimits();
            string full = Path.GetFullPath(target);
            string tmp = full + ".tmp";
            try
            {
                if (File.Exists(tmp)) File.Delete(tmp);
                using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16))
                {
                    var stream = wrap != null ? wrap(fs) : fs;
                    WriteTo(stream);
                    stream.Flush();
                    fs.Flush(true);
                }
                if (verify)
                {
                    var want = new HashSet<string>(verifyEntries ?? new[] { EntryNames.Manifest }, StringComparer.Ordinal);
                    var check = ContainerReader.Read(tmp, null, n => want.Contains(n));
                    foreach (var n in want)
                    {
                        var e = check.Get(n);
                        if (e == null || e.Data == null)
                            throw new SaveFormatException(SaveErrorCode.IoFailure, "verification of the written file failed: " + (e?.Error ?? "entry missing"), n);
                    }
                }
                if (File.Exists(full)) File.Replace(tmp, full, null);
                else File.Move(tmp, full);
            }
            catch (Exception e)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                if (e is SaveFormatException) throw;
                throw new SaveFormatException(SaveErrorCode.IoFailure, e.GetType().Name + ": " + e.Message, null, null, 0, e);
            }
        }
    }
}
