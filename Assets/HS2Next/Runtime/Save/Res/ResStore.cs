// Embedded resources of one container: `res/<sha256>.<ext>`, the
// name derived from the bytes (SHA-256, lowercase hex) and the extension from the magic bytes, never
// from a file name. Bytes are stored exactly as received. Identical bytes are stored once. Loading
// re-hashes every entry and re-keys mismatches (the caller rewrites references through the rename
// map); saving keeps only resources reachable from a core document (garbage collection).
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace HS2Next.Save
{
    public enum ResMediaType { Png, Jpeg, Other }

    public sealed class ResStore
    {
        sealed class Item
        {
            public byte[] Data;
            public ResMediaType Type;
        }

        readonly Dictionary<string, Item> items = new Dictionary<string, Item>(StringComparer.Ordinal);

        /// <summary>Names in ordinal order.</summary>
        public IEnumerable<string> Names
        {
            get
            {
                var l = new List<string>(items.Keys);
                l.Sort(string.CompareOrdinal);
                return l;
            }
        }

        public int Count => items.Count;
        public bool Contains(string name) => name != null && items.ContainsKey(name);

        /// <summary>The bytes of a resource, or null when it is missing.</summary>
        public byte[] Get(string name) => name != null && items.TryGetValue(name, out var i) ? i.Data : null;

        public ResMediaType? TypeOf(string name) => name != null && items.TryGetValue(name, out var i) ? i.Type : (ResMediaType?)null;

        /// <summary>Total size of all resources in bytes.</summary>
        public long TotalBytes
        {
            get { long n = 0; foreach (var i in items.Values) n += i.Data.LongLength; return n; }
        }

        // ---------- naming ----------

        public static ResMediaType Sniff(byte[] b)
        {
            if (PngInfo.HasSignature(b)) return ResMediaType.Png;
            if (b != null && b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ResMediaType.Jpeg;
            return ResMediaType.Other;
        }

        public static string ExtensionOf(ResMediaType t) => t == ResMediaType.Png ? "png" : t == ResMediaType.Jpeg ? "jpg" : "bin";

        public static string MediaTypeOf(ResMediaType t) => t == ResMediaType.Png ? "image/png" : t == ResMediaType.Jpeg ? "image/jpeg" : "application/octet-stream";

        public static string Sha256Hex(byte[] data)
        {
            using (var sha = SHA256.Create()) return JsonText.Hex(sha.ComputeHash(data));
        }

        /// <summary>The true name of <paramref name="data"/>: <c>res/&lt;sha256&gt;.&lt;ext&gt;</c>.</summary>
        public static string NameOf(byte[] data) => "res/" + Sha256Hex(data) + "." + ExtensionOf(Sniff(data));

        // ---------- adding ----------

        /// <summary>
        /// Adds a PNG or JPEG (editor and runtime use) and returns its `res` name; identical bytes are stored once.
        /// Other content throws <see cref="ArgumentException"/>: `bin` resources come only from imports (<see cref="AddImported"/>)
        /// and bakes (<see cref="AddBlob"/>).
        /// </summary>
        public string Add(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var t = Sniff(data);
            if (t == ResMediaType.Other) throw new ArgumentException("resources must be PNG or JPEG (other content only through the import API)", nameof(data));
            return Put(data, t);
        }

        /// <summary>Adds an imported plugin texture as received; content that is neither PNG nor JPEG is stored as `bin` and reported (`res.unknownMediaType`).</summary>
        public string AddImported(byte[] data, FindingList findings = null, string path = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var t = Sniff(data);
            if (t == ResMediaType.Other) findings?.Warning("res.unknownMediaType", path ?? "", "embedded texture is neither PNG nor JPEG; stored as bin, ignored at run time");
            return Put(data, t);
        }

        /// <summary>True when <paramref name="data"/> starts with a bake blob's magic: <c>HNBK</c> (main), <c>HNBG</c> (mesh) or <c>HNBT</c> (texture).</summary>
        public static bool IsBakeBlob(byte[] data) =>
            data != null && data.Length >= 4 && data[0] == (byte)'H' && data[1] == (byte)'N' && data[2] == (byte)'B' && (data[3] == (byte)'K' || data[3] == (byte)'G' || data[3] == (byte)'T');

        /// <summary>
        /// Adds a bake blob (content that starts with a bake magic, <see cref="IsBakeBlob"/>) as `bin`, with no finding, and returns its `res`
        /// name; identical bytes are stored once. Other content throws <see cref="ArgumentException"/>. The run time reads such entries
        /// through the bake reader only.
        /// </summary>
        public string AddBlob(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (!IsBakeBlob(data)) throw new ArgumentException("not a bake blob (magic HNBK, HNBG or HNBT)", nameof(data));
            return Put(data, ResMediaType.Other);
        }

        string Put(byte[] data, ResMediaType t)
        {
            string name = "res/" + Sha256Hex(data) + "." + ExtensionOf(t);
            if (!items.ContainsKey(name)) items[name] = new Item { Data = data, Type = t };
            return name;
        }

        public bool Remove(string name) => name != null && items.Remove(name);

        /// <summary>Copies the named resources from <paramref name="other"/> (placing content in another container). Missing ones are skipped and returned.</summary>
        public List<string> CopyFrom(ResStore other, IEnumerable<string> names)
        {
            var missing = new List<string>();
            foreach (var n in names)
            {
                if (items.ContainsKey(n)) continue;
                if (other.items.TryGetValue(n, out var i)) items[n] = i;
                else missing.Add(n);
            }
            return missing;
        }

        // ---------- load ----------

        /// <summary>
        /// Adds a `res/` entry read from a container. The bytes are hashed; when the true name differs from
        /// <paramref name="entryName"/> the resource is re-keyed and the pair is added to <paramref name="renames"/>
        /// (finding `res.hashMismatch`). Returns the true name.
        /// </summary>
        public string LoadEntry(string entryName, byte[] data, IDictionary<string, string> renames, FindingList findings)
        {
            var t = Sniff(data);
            string name = "res/" + Sha256Hex(data) + "." + ExtensionOf(t);
            if (name != entryName)
            {
                renames[entryName] = name;
                findings?.Warning("res.hashMismatch", entryName, "content hash differs from the name; re-keyed to " + name);
            }
            if (!items.ContainsKey(name)) items[name] = new Item { Data = data, Type = t };
            return name;
        }

        // ---------- garbage collection ----------

        static readonly Regex ResString = new Regex("\"(res/[0-9a-f]{64}\\.(?:png|jpg|bin))\"", RegexOptions.CultureInvariant);

        /// <summary>
        /// Adds every `res` name that occurs as a JSON string in canonical document text to <paramref name="into"/>
        /// (unknown members included). A member name that happens to
        /// equal a res name also counts; keeping one extra resource is harmless.
        /// </summary>
        public static void ScanReferences(byte[] utf8Json, ISet<string> into)
        {
            // Res names are ASCII, so a byte-to-char view is enough and avoids decoding.
            string s = System.Text.Encoding.ASCII.GetString(utf8Json);
            foreach (Match m in ResString.Matches(s)) into.Add(m.Groups[1].Value);
        }

        /// <summary>Resources to write: the reachable ones that exist, in ordinal order. Reachable names without bytes are reported as dangling.</summary>
        public List<KeyValuePair<string, byte[]>> Collect(ISet<string> reachable, FindingList findings)
        {
            var list = new List<KeyValuePair<string, byte[]>>();
            var names = new List<string>(reachable);
            names.Sort(string.CompareOrdinal);
            foreach (var n in names)
            {
                if (items.TryGetValue(n, out var i)) list.Add(new KeyValuePair<string, byte[]>(n, i.Data));
                else findings?.Warning("res.dangling", n, "a document references a resource that is not in the file");
            }
            return list;
        }

        /// <summary>Drops every resource not in <paramref name="reachable"/>; returns how many were dropped.</summary>
        public int Retain(ISet<string> reachable)
        {
            var drop = new List<string>();
            foreach (var n in items.Keys) if (!reachable.Contains(n)) drop.Add(n);
            foreach (var n in drop) items.Remove(n);
            return drop.Count;
        }
    }
}
