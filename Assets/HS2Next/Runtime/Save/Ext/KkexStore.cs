// `hs2.kkex`: imported HS2 plugin data of one chara or coord scope instance (
// 10). Core-owned, never dispatched to handlers: `index.json` (canonical JSON) plus the raw
// MessagePack copies, written back byte-identical. `role` source = provenance of a typed section
// (never applied), `opaque` = the only copy. StripImportSources removes exactly the source entries.
using System;
using System.Collections.Generic;

namespace HS2Next.Save
{
    public sealed class KkexEntry
    {
        public static readonly string[] Origins = { "kkex", "object", "block" };
        public static readonly string[] Roles = { "source", "opaque" };
        public static readonly string[] Reasons = { "noDecoder", "unsupportedVersion", "decodeError", "malformed", "blockVersion" };

        /// <summary>Plugin data id, exact.</summary>
        public string Id;
        /// <summary>Path relative to <c>ext/hs2.kkex/</c>.</summary>
        public string File;
        public string Origin;
        public string Object;
        public int? Version;
        public string Form;
        public string Role;
        public string Reason;
        public long Size;
        public string Sha256;
        public JsonLeftovers Unknown;

        public bool IsSource => Role == "source";

        internal static KkexEntry Read(DocReader r) => new KkexEntry
        {
            Id = r.Str("id"),
            File = r.Str("file"),
            Origin = r.EnumStr("origin", "kkex", Origins),
            Object = r.StrN("object"),
            Version = r.IntN("version"),
            Form = r.Str("form", ""),
            Role = r.EnumStr("role", "opaque", Roles),
            Reason = r.EnumStrN("reason", null, Reasons),
            Size = r.Int64("size", 0),
            Sha256 = r.Str("sha256", ""),
            Unknown = r.Leftovers(),
        };

        internal void Write(CanonicalJsonWriter w)
        {
            w.StartObject();
            w.Str("id", Id);
            w.Str("file", File);
            w.Str("origin", Origin);
            w.Str("object", Object);
            w.IntN("version", Version);
            w.Str("form", Form);
            w.Str("role", Role);
            w.Str("reason", Reason);
            w.Int64("size", Size);
            w.Str("sha256", Sha256);
            w.Leftovers(Unknown);
            w.EndObject();
        }

        public KkexEntry Clone()
        {
            var c = (KkexEntry)MemberwiseClone();
            c.Unknown = Unknown?.Clone();
            return c;
        }

        /// <summary>Sort key: (origin, object, id) ordinal; a null object sorts first.</summary>
        public static int Compare(KkexEntry a, KkexEntry b)
        {
            int c = string.CompareOrdinal(a.Origin, b.Origin);
            if (c == 0) c = string.CompareOrdinal(a.Object ?? "", b.Object ?? "");
            if (c == 0) c = string.CompareOrdinal(a.Id, b.Id);
            return c;
        }
    }

    public sealed class KkexStore
    {
        public const string IndexFile = "index.json";
        public const int ManifestVersion = 1;

        public int IndexVersion = 1;
        /// <summary>"hs2.chara" or "hs2.coord".</summary>
        public string Source = "";
        /// <summary>KKEx block version string ("3") or the coordinate trailer version as text.</summary>
        public string BlockVersion = "";
        public JsonLeftovers Unknown;
        public readonly List<KkexEntry> Entries = new List<KkexEntry>();
        /// <summary>Files relative to <c>ext/hs2.kkex/</c> (index.json excluded).</summary>
        public readonly SortedDictionary<string, byte[]> Files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        /// <summary>When index.json could not be read, its bytes are kept here and written back unchanged (the store is then opaque).</summary>
        public byte[] RawIndex;

        public bool IsEmpty => Entries.Count == 0 && Files.Count == 0 && RawIndex == null;

        /// <summary>Path of an entry's file relative to <c>ext/hs2.kkex/</c>.</summary>
        public static string FileFor(string origin, string objectName, string id)
        {
            switch (origin)
            {
                case "object": return "object/" + EntryNames.Escape(objectName) + "/" + EntryNames.Escape(id) + ".msgpack";
                case "block": return "block.KKEx.bin";
                default: return EntryNames.Escape(id) + ".msgpack";
            }
        }

        /// <summary>
        /// Adds a raw copy (importer API, Phase 2.3). <paramref name="data"/> = the exact PluginData MessagePack bytes
        /// (or the whole block for origin "block"). Replaces an entry with the same file.
        /// </summary>
        public KkexEntry Add(string id, string origin, string objectName, int? version, string form, string role, string reason, byte[] data)
        {
            string file = FileFor(origin, objectName, id);
            Entries.RemoveAll(e => e.File == file);
            var entry = new KkexEntry
            {
                Id = id, File = file, Origin = origin, Object = origin == "object" ? objectName : null, Version = version,
                Form = form, Role = role, Reason = role == "source" ? null : reason, Size = data.LongLength, Sha256 = ResStore.Sha256Hex(data),
            };
            Entries.Add(entry);
            Files[file] = data;
            return entry;
        }

        /// <summary>Removes every `source` entry and its file; returns how many were removed.</summary>
        public int StripImportSources()
        {
            int n = 0;
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                if (!Entries[i].IsSource) continue;
                Files.Remove(Entries[i].File);
                Entries.RemoveAt(i);
                n++;
            }
            return n;
        }

        public KkexStore Clone()
        {
            var c = new KkexStore { IndexVersion = IndexVersion, Source = Source, BlockVersion = BlockVersion, Unknown = Unknown?.Clone(), RawIndex = RawIndex };
            foreach (var e in Entries) c.Entries.Add(e.Clone());
            foreach (var kv in Files) c.Files[kv.Key] = kv.Value;
            return c;
        }

        /// <summary>
        /// Builds a store from the files under <c>&lt;root&gt;ext/hs2.kkex/</c> (paths relative to that folder,
        /// index.json included). Problems are findings; nothing is lost.
        /// </summary>
        public static KkexStore Load(IDictionary<string, byte[]> files, string location, FindingList findings, bool lenient)
        {
            var s = new KkexStore();
            foreach (var kv in files) if (kv.Key != IndexFile) s.Files[kv.Key] = kv.Value;
            if (!files.TryGetValue(IndexFile, out var index))
            {
                findings.Warning("kkex.noIndex", location, "hs2.kkex without index.json; files kept as they are");
                return s;
            }
            try
            {
                var r = DocReader.ForDocument(index, new DocReadContext(location + IndexFile, lenient, findings));
                s.IndexVersion = r.Int("indexVersion", 1);
                s.Source = r.Str("source", "");
                s.BlockVersion = r.Str("blockVersion", "");
                foreach (var e in r.Array("entries", v => KkexEntry.Read(v.AsObj())))
                {
                    if (!s.Files.TryGetValue(e.File ?? "", out var data))
                    {
                        findings.Error("kkex.missingFile", location + e.File, "index entry " + e.Id + " has no file; entry dropped");
                        continue;
                    }
                    // Size and SHA-256 are verified against the file (cheap next to inflating it); a mismatch is reported
                    // and corrected in the index the next save writes.
                    string sha = ResStore.Sha256Hex(data);
                    if (data.LongLength != e.Size || e.Sha256 != sha)
                    {
                        findings.Warning("kkex.indexMismatch", location + e.File, "size or SHA-256 in index.json does not fit the file; corrected on save");
                        e.Size = data.LongLength;
                        e.Sha256 = sha;
                    }
                    s.Entries.Add(e);
                }
                s.Unknown = r.Leftovers();
            }
            catch (SaveFormatException ex)
            {
                findings.Error("kkex.badIndex", location + IndexFile, "index.json unreadable (" + ex.Detail + "); hs2.kkex kept unchanged");
                s.Entries.Clear();
                s.RawIndex = index;
                return s;
            }
            var indexed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in s.Entries) indexed.Add(e.File);
            foreach (var f in s.Files.Keys)
                if (!indexed.Contains(f)) findings.Warning("kkex.unindexedFile", location + f, "file not listed in index.json; kept");
            return s;
        }

        /// <summary>Files to write (relative to <c>ext/hs2.kkex/</c>), index.json included; empty when the store is empty.</summary>
        public SortedDictionary<string, byte[]> Save()
        {
            var o = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
            if (IsEmpty) return o;
            foreach (var kv in Files) o[kv.Key] = kv.Value;
            if (RawIndex != null) { o[IndexFile] = RawIndex; return o; }
            var sorted = new List<KkexEntry>(Entries);
            sorted.Sort(KkexEntry.Compare);
            foreach (var e in sorted)
            {
                if (Files.TryGetValue(e.File, out var data) && (e.Size != data.LongLength || (e.Sha256 ?? "").Length != 64))
                {
                    e.Size = data.LongLength;
                    e.Sha256 = ResStore.Sha256Hex(data);
                }
            }
            o[IndexFile] = CanonicalJsonWriter.ToBytes(IndexFile, w =>
            {
                w.StartObject();
                w.Int("indexVersion", IndexVersion);
                w.Str("source", Source);
                w.Str("blockVersion", BlockVersion);
                w.StartArray("entries");
                foreach (var e in sorted) e.Write(w);
                w.EndArray();
                w.Leftovers(Unknown);
                w.EndObject();
            });
            return o;
        }
    }
}
