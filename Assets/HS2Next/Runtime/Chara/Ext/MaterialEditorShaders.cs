// MaterialEditor's shader registry on the character side: which copy of a shader a MaterialEditor shader name stands for.
// MaterialEditor fills its registry at start-up, first with its own declarations of the game's shaders, then from the
// <MaterialEditor> element (or, when that is absent, the <HS2_MaterialEditor> element) of every loaded zipmod manifest. Each
// <Shader Name AssetBundle Asset RenderQueue ShaderOptimization> entry names a game-relative bundle and in it either a prefab
// (the shader is taken from a renderer material whose shader has that name) or, without Asset, a Shader asset of that name.
// A later declaration of a name replaces an earlier one (zipmods in GUID load order, entries in manifest order).
// HS2Next asks this registry for the copy a name stands for and never the shaders that happen to be loaded in the process,
// so a character resolves its MaterialEditor shaders the same way whatever was built before it:
// - a name a manifest declares loads its declared copy through the catalog (the redirect and file rules of any named load),
//   once per catalog and zipmod set; a copy that does not load counts as no copy (logged once);
// - the plugin's own declarations are the game's AIT / Standard / Shader Forge shaders, whose replacement does not depend on
//   the copy (no copy rule of ShaderMap names one of them), so they, like names declared nowhere, have no original here and
//   take their fitted replacement directly.
// The declarations are read from the manifests once and kept in <CacheDir>/meshaders-<hash of the sources' folders>.tsv,
// keyed by each zipmod's path, size and write time (a header line, then per zipmod "Z path stamp" followed by its
// "D element name bundle asset renderQueue shaderOptimization" lines, and an end line "#end <zipmod count>").
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using HS2Next.Content;
using HS2Next.IO;
using UnityEngine;

namespace HS2Next.Chara.Ext
{
    public sealed class MaterialEditorShaders
    {
        /// <summary>One manifest &lt;Shader&gt; entry as declared (attribute values as written, null when absent).</summary>
        public sealed class Declaration
        {
            public string Name, Bundle, Asset, RenderQueue, ShaderOptimization;
            /// <summary>"MaterialEditor" or "HS2_MaterialEditor": the manifest element that declared it.</summary>
            public string Element;
            /// <summary>The zipmod whose manifest declares it.</summary>
            public ZipmodInfo Zipmod;

            public override string ToString() => $"{Name} <- {Zipmod?.Guid}: {Bundle} {(string.IsNullOrEmpty(Asset) ? "(shader asset)" : Asset)}";
        }

        public const string CacheHeader = "#hs2next-meshaders\t1";
        const string CacheEnd = "#end";

        static readonly ConditionalWeakTable<ContentCatalog, MaterialEditorShaders> perCatalog = new ConditionalWeakTable<ContentCatalog, MaterialEditorShaders>();

        /// <summary>The registry of <paramref name="catalog"/>'s loaded zipmods (one per catalog), or null without a catalog.</summary>
        public static MaterialEditorShaders Of(ContentCatalog catalog) => catalog == null ? null : perCatalog.GetValue(catalog, c => new MaterialEditorShaders(c));

        /// <summary>A registry over <paramref name="catalog"/> that is not the catalog's own (tests: a second build from the cache).</summary>
        public static MaterialEditorShaders Unshared(ContentCatalog catalog) => new MaterialEditorShaders(catalog ?? throw new ArgumentNullException(nameof(catalog)));

        readonly ContentCatalog catalog;
        IReadOnlyList<ZipmodInfo> builtFrom;
        Dictionary<string, Declaration> declarations = new Dictionary<string, Declaration>(StringComparer.Ordinal);
        readonly Dictionary<string, Shader> originals = new Dictionary<string, Shader>(StringComparer.Ordinal);
        readonly HashSet<string> failed = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Numbers of the last build: zipmods visited, manifests read from disk, zipmods taken from the cache,
        /// zipmods whose manifest declares at least one shader, declarations read (before later ones replace earlier ones).</summary>
        public int Zipmods { get; private set; }
        public int ManifestsRead { get; private set; }
        public int FromCache { get; private set; }
        public int DeclaringZipmods { get; private set; }
        public int DeclarationLines { get; private set; }
        public double BuildMs { get; private set; }
        public string CacheFile { get; private set; }
        /// <summary>Why the cache file was not used or not written (null when it was fine).</summary>
        public string CacheError { get; private set; }
        /// <summary>Number of declared copies loaded so far (each name once per catalog and zipmod set while its copy stays loaded).</summary>
        public int Loads { get; private set; }

        MaterialEditorShaders(ContentCatalog c) { catalog = c; }

        /// <summary>The declarations in effect by name (built on first use, again when the catalog's zipmod set changes).</summary>
        public IReadOnlyDictionary<string, Declaration> Declarations { get { Ensure(); return declarations; } }

        /// <summary>The declaration in effect for <paramref name="name"/>, or null when no loaded manifest declares it.</summary>
        public Declaration Find(string name)
        {
            Ensure();
            return name != null && declarations.TryGetValue(name, out var d) ? d : null;
        }

        /// <summary>
        /// The copy <paramref name="name"/> stands for: the declared shader loaded through the catalog (kept while it stays
        /// loaded; loaded again from the same declaration after its bundle was unloaded), or null when no manifest declares
        /// the name or its declared bundle / asset does not give a shader of that name.
        /// </summary>
        public Shader Original(string name)
        {
            var d = Find(name);
            if (d == null) return null;
            if (originals.TryGetValue(name, out var sh) && sh) return sh;
            if (failed.Contains(name)) return null;
            sh = Load(d);
            Loads++;
            if (sh) originals[name] = sh;
            else
            {
                failed.Add(name);
                catalog.Log.Add($"materialEditor shaders: the declared copy of {name} did not load ({d})");
            }
            return sh;
        }

        Shader Load(Declaration d)
        {
            if (string.IsNullOrEmpty(d.Bundle)) return null;
            if (string.IsNullOrEmpty(d.Asset)) return catalog.LoadAssetAt<Shader>(d.Zipmod, d.Bundle, null, d.Name);
            var prefab = catalog.LoadAssetAt<GameObject>(d.Zipmod, d.Bundle, null, d.Asset);
            if (!prefab) return null;
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m && m.shader && m.shader.name == d.Name) return m.shader;
            return null;
        }

        // ---- the declarations of the loaded zipmods

        void Ensure()
        {
            var list = catalog.ZipmodsInLoadOrder;
            if (ReferenceEquals(list, builtFrom)) return;
            builtFrom = list;
            originals.Clear();
            failed.Clear();
            var sw = Stopwatch.StartNew();
            var d = new Dictionary<string, Declaration>(StringComparer.Ordinal);
            CacheFile = CachePath();
            CacheError = null;
            var cached = ReadCache(CacheFile, out string error);
            if (error != null) CacheError = error;
            var outText = new StringBuilder(CacheHeader).Append('\n');
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int zipmods = 0, read = 0, hit = 0, declaring = 0, lines = 0, blocks = 0;
            void Block(string path, string stamp, List<string[]> rows)
            {
                blocks++;
                outText.Append("Z\t").Append(path).Append('\t').Append(stamp ?? "").Append('\n');
                foreach (var r in rows) outText.Append(string.Join("\t", r)).Append('\n');
            }
            foreach (var z in list)
            {
                if (z?.Guid == null || !visited.Add(z.Path)) continue;
                zipmods++;
                List<string[]> rows;
                if (z.Stamp != null && cached != null && cached.TryGetValue(z.Path, out var c) && c.Stamp == z.Stamp) { rows = c.Rows; hit++; }
                else { rows = ReadManifest(z.Path); read++; }
                Block(z.Path, z.Stamp, rows);
                if (rows.Count > 0) declaring++;
                foreach (var r in rows)
                {
                    lines++;
                    d[r[2]] = new Declaration
                    {
                        Element = r[1], Name = r[2], Bundle = Blank(r[3]), Asset = Blank(r[4]), RenderQueue = Blank(r[5]), ShaderOptimization = Blank(r[6]), Zipmod = z,
                    };
                }
            }
            // zipmods of another set (e.g. the files the zipmod loader skips) keep their lines for the next build that has them
            if (cached != null)
                foreach (var kv in cached)
                    if (!visited.Contains(kv.Key)) Block(kv.Key, kv.Value.Stamp, kv.Value.Rows);
            outText.Append(CacheEnd).Append('\t').Append(blocks.ToString(CultureInfo.InvariantCulture)).Append('\n');
            if (CacheFile != null && (read > 0 || cached == null))
            {
                try
                {
                    var dir = Path.GetDirectoryName(CacheFile);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    BundleNamesCache.ReplaceFile(CacheFile, outText.ToString());
                }
                catch (Exception e) { CacheError = (CacheError != null ? CacheError + "; " : "") + "not written: " + e.Message; }
            }
            declarations = d;
            Zipmods = zipmods; ManifestsRead = read; FromCache = hit; DeclaringZipmods = declaring; DeclarationLines = lines;
            BuildMs = sw.Elapsed.TotalMilliseconds;
            catalog.Log.Add($"materialEditor shaders: {d.Count} names declared by {declaring} zipmods ({lines} declarations; {zipmods} zipmods: " +
                            $"{hit} from the cache, {read} manifests read; {BuildMs:F0} ms){(CacheError != null ? "; cache " + CacheError : "")}");
        }

        string CachePath()
        {
            var names = catalog.NamesCacheFile();
            if (names == null) return null;
            string file = Path.GetFileName(names);
            const string prefix = "bundlenames-";
            return Path.Combine(Path.GetDirectoryName(names) ?? "", "meshaders-" + (file.StartsWith(prefix, StringComparison.Ordinal) ? file.Substring(prefix.Length) : file));
        }

        sealed class Cached { public string Stamp; public readonly List<string[]> Rows = new List<string[]>(); }

        // null (and no error) when there is no file; null with an error when it cannot be used.
        static Dictionary<string, Cached> ReadCache(string file, out string error)
        {
            error = null;
            if (file == null || !File.Exists(file)) return null;
            var cache = new Dictionary<string, Cached>(StringComparer.OrdinalIgnoreCase);
            try
            {
                Cached cur = null;
                bool first = true, ended = false;
                foreach (var line in File.ReadLines(file))
                {
                    if (ended) throw new FormatException("lines after the end line");
                    if (first)
                    {
                        first = false;
                        if (line != CacheHeader) throw new FormatException("another version");
                        continue;
                    }
                    var t = line.Split('\t');
                    if (t[0] == CacheEnd)
                    {
                        if (t.Length != 2 || !int.TryParse(t[1], NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n != cache.Count)
                            throw new FormatException("bad end line");
                        ended = true;
                        continue;
                    }
                    if (t[0] == "Z" && t.Length == 3) { cache[t[1]] = cur = new Cached { Stamp = t[2] }; continue; }
                    if (t[0] == "D" && t.Length == 7 && cur != null && t[2].Length > 0) { cur.Rows.Add(t); continue; }
                    throw new FormatException("unexpected line");
                }
                if (!ended) throw new FormatException("no end line");
            }
            catch (Exception e)
            {
                error = "ignored (" + e.Message + ")";
                return null;
            }
            return cache;
        }

        /// <summary>The &lt;Shader&gt; entries of a zipmod's manifest as cache rows (element, name, bundle, asset, render queue,
        /// shader optimization); empty when it has none or cannot be read.</summary>
        public static List<string[]> ReadManifest(string zipPath)
        {
            try { return Parse(ReadManifestText(zipPath)); }
            catch (Exception) { return new List<string[]>(); }
        }

        /// <summary>The zip's manifest.xml (any case, the last such entry, as the zip index keeps it) decoded like the zip
        /// index decodes text, or null. The central directory is read in one piece: a cold registry build reads every
        /// loaded zipmod's manifest, and reading the directory entry by entry would take minutes for the whole mod folder.</summary>
        public static string ReadManifestText(string zipPath)
        {
            using (var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            {
                int max = (int)Math.Min(fs.Length, 65557);
                if (max < 22) return null;
                var tail = new byte[max];
                fs.Position = fs.Length - max;
                Fill(fs, tail, max);
                int e = -1;
                for (int i = max - 22; i >= 0; i--)
                    if (tail[i] == 0x50 && tail[i + 1] == 0x4b && tail[i + 2] == 5 && tail[i + 3] == 6) { e = i; break; }
                if (e < 0) return null;
                long count = U16(tail, e + 10), cdSize = U32(tail, e + 12), cdOffset = U32(tail, e + 16);
                if (count == 0xFFFF || cdSize == 0xFFFFFFFF || cdOffset == 0xFFFFFFFF)
                {
                    // ZIP64: the locator sits 20 bytes before the end record and points at the ZIP64 end record
                    long eocd = fs.Length - max + e;
                    if (eocd < 20) return null;
                    var loc = new byte[20];
                    fs.Position = eocd - 20;
                    Fill(fs, loc, 20);
                    if (U32(loc, 0) != 0x07064b50) return null;
                    var rec = new byte[56];
                    fs.Position = (long)U64(loc, 8);
                    Fill(fs, rec, 56);
                    count = (long)U64(rec, 32); cdSize = (long)U64(rec, 40); cdOffset = (long)U64(rec, 48);
                }
                if (cdSize <= 0 || cdSize > int.MaxValue || cdOffset < 0 || cdOffset + cdSize > fs.Length) return null;
                var cd = new byte[cdSize];
                fs.Position = cdOffset;
                Fill(fs, cd, (int)cdSize);
                int p = 0, method = -1;
                long csize = 0, usize = 0, lho = -1;
                for (long k = 0; k < count && p + 46 <= cd.Length; k++)
                {
                    if (U32(cd, p) != 0x02014b50) break;
                    int nlen = U16(cd, p + 28), xlen = U16(cd, p + 30), clen = U16(cd, p + 32);
                    if (p + 46 + nlen > cd.Length) break;
                    string name = Encoding.UTF8.GetString(cd, p + 46, nlen).Replace('\\', '/');
                    if (string.Equals(name, "manifest.xml", StringComparison.OrdinalIgnoreCase))
                    {
                        method = U16(cd, p + 10);
                        csize = U32(cd, p + 20); usize = U32(cd, p + 24); lho = U32(cd, p + 42);
                        // ZIP64 extra field (id 1): the 64-bit sizes and offset of the fields that are 0xFFFFFFFF, in that order
                        int x = p + 46 + nlen, xend = Math.Min(x + xlen, cd.Length);
                        while (x + 4 <= xend)
                        {
                            int id = U16(cd, x), sz = U16(cd, x + 2), q = x + 4;
                            if (id == 1)
                            {
                                if (usize == 0xFFFFFFFF && q + 8 <= xend) { usize = (long)U64(cd, q); q += 8; }
                                if (csize == 0xFFFFFFFF && q + 8 <= xend) { csize = (long)U64(cd, q); q += 8; }
                                if (lho == 0xFFFFFFFF && q + 8 <= xend) lho = (long)U64(cd, q);
                            }
                            x += 4 + sz;
                        }
                    }
                    p += 46 + nlen + xlen + clen;
                }
                if (lho < 0 || csize < 0 || csize > int.MaxValue || lho + 30 > fs.Length) return null;
                var lh = new byte[30];
                fs.Position = lho;
                Fill(fs, lh, 30);
                if (U32(lh, 0) != 0x04034b50) return null;
                fs.Position = lho + 30 + U16(lh, 26) + U16(lh, 28);
                var data = new byte[csize];
                Fill(fs, data, (int)csize);
                if (method == 0) return ZipIndex.DecodeText(data);
                if (method != 8) return null;
                using (var ds = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress))
                using (var ms = new MemoryStream((int)Math.Min(Math.Max(usize, 0), 1 << 24)))
                {
                    ds.CopyTo(ms);
                    return ZipIndex.DecodeText(ms.ToArray());
                }
            }
        }

        static void Fill(Stream s, byte[] b, int n)
        {
            int got = 0;
            while (got < n) { int r = s.Read(b, got, n - got); if (r <= 0) throw new EndOfStreamException(); got += r; }
        }

        static int U16(byte[] b, int i) => b[i] | b[i + 1] << 8;
        static long U32(byte[] b, int i) => (uint)(b[i] | b[i + 1] << 8 | b[i + 2] << 16 | b[i + 3] << 24);
        static ulong U64(byte[] b, int i) => (ulong)U32(b, i) | (ulong)U32(b, i + 4) << 32;

        /// <summary>The &lt;Shader&gt; children of the manifest root's first &lt;MaterialEditor&gt; element, else of its first
        /// &lt;HS2_MaterialEditor&gt; element, in manifest order; entries without a name are left out.</summary>
        public static List<string[]> Parse(string manifestXml)
        {
            var rows = new List<string[]>();
            if (string.IsNullOrEmpty(manifestXml) || manifestXml.IndexOf("MaterialEditor", StringComparison.Ordinal) < 0) return rows;
            var doc = new XmlDocument();
            try { doc.LoadXml(manifestXml); }
            catch (XmlException) { return rows; }
            var root = doc.DocumentElement;
            if (root == null) return rows;
            var el = Child(root, "MaterialEditor") ?? Child(root, "HS2_MaterialEditor");
            if (el == null) return rows;
            foreach (XmlNode n in el.ChildNodes)
            {
                if (!(n is XmlElement s) || s.Name != "Shader") continue;
                string name = Attr(s, "Name");
                if (string.IsNullOrEmpty(name)) continue;
                rows.Add(new[] { "D", el.Name, Clean(name), Clean(Attr(s, "AssetBundle") ?? ""), Clean(Attr(s, "Asset") ?? ""),
                    Clean(Attr(s, "RenderQueue") ?? ""), Clean(Attr(s, "ShaderOptimization") ?? "") });
            }
            return rows;
        }

        static XmlElement Child(XmlElement parent, string name)
        {
            foreach (XmlNode n in parent.ChildNodes)
                if (n is XmlElement e && e.Name == name) return e;
            return null;
        }

        static string Attr(XmlElement e, string name) => e.GetAttributeNode(name)?.Value;
        static string Clean(string s) => s.IndexOfAny(Bad) < 0 ? s : s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        static readonly char[] Bad = { '\t', '\r', '\n' };
        static string Blank(string s) => string.IsNullOrEmpty(s) ? null : s;
    }
}
