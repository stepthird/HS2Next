// Sideloader's per-asset redirect, the Unity-free part: which zipmods are loaded (the <game> filter and one file per GUID by
// the manifest version), the index of bundle paths shipped by several sources, the asset names of their copies (read from
// the bundles' AssetBundle containers without Unity), the rule that picks the copy a named load is served from, and a read
// model of that choice.
//
// The rule, as the zipmod loader applies it to every named asset load of any bundle: a texture request is first answered by
// a loose PNG (LooseTextures, before this rule); then the copies registered under the bundle's exact path are asked in
// zipmod load order (GUIDs in lower-case ordinal order) and the first copy whose container holds an asset of the requested
// name serves it, whatever the requested type (a type mismatch then loads nothing). No copy holds the name: the abdata file
// answers when it exists, else the first copy (the asset is missing). Paths with a single source never ask for names.
// A path is redirectable when it has at least two sources: an abdata file and a zipmod copy, or two zipmod copies.
//
// The asset names of a copy are the short names of its container keys (last path component, extension removed, lower case),
// which is what a load by name matches. They are read with Hs2BundleFile (uncompressed, LZ4 and LZ4HC blocks); an LZMA
// copy is marked and read later through Unity by the catalog. The names cache (BundleNamesCache) keeps them per
// (zip path, zip stamp, bundle path).
// Unity-free: plain Mono builds this file (Tools/mono_tests.ps1 -Suite content). Bundle opens stay with ContentCatalog.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HS2Next.IO;

namespace HS2Next.Content
{
    /// <summary>A zipmod as the redirect sees it (<see cref="ZipmodInfo"/> implements it; tests use plain objects).</summary>
    public interface IRedirectZip
    {
        /// <summary>The zipmod file.</summary>
        string Path { get; }
        /// <summary>The manifest GUID.</summary>
        string Guid { get; }
        /// <summary>abdata-relative paths of the files it ships under abdata/, in their own case.</summary>
        IReadOnlyCollection<string> BundlePaths { get; }
        /// <summary>The asset names of its copy of <paramref name="bundlePath"/> (exact path), or null when they were not read.</summary>
        BundleNames NamesOf(string bundlePath);
    }

    // ==================================================================== the loaded zipmod set

    /// <summary>The zipmod loader's choice of zipmods: the game filter, the manifest version rule and one file per GUID.</summary>
    public static class SideloaderZipmods
    {
        /// <summary>Game names (lower case, trimmed) of a manifest's &lt;game&gt; elements that load the zipmod in HS2.</summary>
        public static readonly string[] Hs2Games = { "hs2", "honeyselect2", "honey select 2" };

        /// <summary>True when a zipmod with these &lt;game&gt; values loads in HS2: no (non-blank) value, or one of <see cref="Hs2Games"/>.</summary>
        public static bool LoadsOnHs2(IEnumerable<string> games)
        {
            if (games == null) return true;
            bool any = false;
            foreach (var g in games)
            {
                if (string.IsNullOrWhiteSpace(g)) continue;
                any = true;
                if (Array.IndexOf(Hs2Games, g.Trim().ToLowerInvariant()) >= 0) return true;
            }
            return !any;
        }

        static readonly Regex PartSplit = new Regex("[. \\-,_]", RegexOptions.CultureInvariant);
        static readonly Regex Runs = new Regex("\\d+|\\D+", RegexOptions.CultureInvariant);

        // One run of a version: a number (digits without leading zeros) or text.
        struct Run { public bool Number; public string Text; }

        static string Normal(string v)
        {
            v = (v ?? "").Trim().TrimStart('v', 'V', 'r', 'R', ' ');
            return v.Length == 0 ? "0" : v;
        }

        static List<Run> Tokens(string normal)
        {
            var l = new List<Run>();
            foreach (var part in PartSplit.Split(normal.Trim()))
            {
                if (part.Length == 0) { l.Add(new Run { Number = true, Text = "0" }); continue; }
                foreach (Match m in Runs.Matches(part))
                {
                    string s = m.Value;
                    if (char.IsDigit(s[0]) && s.All(c => c >= '0' && c <= '9'))
                    {
                        s = s.TrimStart('0');
                        l.Add(new Run { Number = true, Text = s.Length == 0 ? "0" : s });
                    }
                    else l.Add(new Run { Number = false, Text = s });
                }
            }
            return l;
        }

        static int CompareNumbers(string a, string b) => a.Length != b.Length ? a.Length.CompareTo(b.Length) : Math.Sign(string.CompareOrdinal(a, b));

        /// <summary>
        /// The manifest version order: leading v / V / r / R and blanks trimmed, an empty version is "0"; parts split on '.',
        /// blank, '-', ',' and '_', each part into runs of digits and non-digits; runs compared in turn (numbers as numbers,
        /// text ordinally, a missing run counts as 0; a number against text: "0" is lower, else the ordinal order of their text).
        /// Equal normalized strings are equal. Returns -1, 0 or 1.
        /// </summary>
        public static int CompareVersions(string a, string b)
        {
            string na = Normal(a), nb = Normal(b);
            if (na == nb) return 0;
            var ta = Tokens(na); var tb = Tokens(nb);
            var zero = new Run { Number = true, Text = "0" };
            for (int i = 0; i < Math.Max(ta.Count, tb.Count); i++)
            {
                var x = i < ta.Count ? ta[i] : zero;
                var y = i < tb.Count ? tb[i] : zero;
                if (x.Number == y.Number)
                {
                    int c = x.Number ? CompareNumbers(x.Text, y.Text) : Math.Sign(string.CompareOrdinal(x.Text, y.Text));
                    if (c != 0) return c;
                    continue;
                }
                if (x.Text == "0" && y.Text != "0") return -1;
                if (y.Text == "0" && x.Text != "0") return 1;
                if (x.Text != y.Text) return Math.Sign(string.CompareOrdinal(x.Text, y.Text));
            }
            return 0;
        }

        /// <summary>
        /// The file the loader keeps of several files with one GUID (given in scan order): when every file has a non-empty
        /// version, the highest version, ties by the longer file name; otherwise the newest last-write time. A complete tie
        /// keeps the earliest file of the scan. Returns its index (-1 for an empty list).
        /// </summary>
        public static int Choose(IReadOnlyList<(string version, string fileName, long lastWriteTicks)> files)
        {
            if (files == null || files.Count == 0) return -1;
            int best = 0;
            bool versions = files.All(f => !string.IsNullOrEmpty(f.version));
            for (int i = 1; i < files.Count; i++)
            {
                int c;
                if (versions)
                {
                    c = CompareVersions(files[i].version, files[best].version);
                    if (c == 0) c = (files[i].fileName ?? "").Length.CompareTo((files[best].fileName ?? "").Length);
                }
                else c = files[i].lastWriteTicks.CompareTo(files[best].lastWriteTicks);
                if (c > 0) best = i;
            }
            return best;
        }

        /// <summary>
        /// Marks, within each source's scan, the files of a GUID the loader does not keep (<see cref="ZipmodInfo.SkippedBy"/>
        /// "version"; GUIDs grouped without regard to case; files the game filter skipped take no part). <paramref name="on"/>
        /// false only clears earlier marks (every file loads, the last one of a GUID wins as before). Repeatable.
        /// </summary>
        public static void MarkOlderFiles(IEnumerable<IReadOnlyList<ZipmodInfo>> sources, bool on)
        {
            var all = sources.Select(s => s.ToList()).ToList();
            foreach (var s in all) foreach (var z in s) if (z.SkippedBy == "version") z.SkippedBy = null;
            if (!on) return;
            foreach (var s in all)
                foreach (var g in s.Where(z => z.Guid != null && z.SkippedBy == null).GroupBy(z => z.Guid, StringComparer.OrdinalIgnoreCase))
                {
                    var l = g.ToList();
                    if (l.Count < 2) continue;
                    int keep = Choose(l.Select(z => (z.Version, System.IO.Path.GetFileName(z.Path), z.LastWriteTicks)).ToList());
                    for (int k = 0; k < l.Count; k++) if (k != keep) l[k].SkippedBy = "version";
                }
        }
    }

    // ==================================================================== asset names of one bundle

    /// <summary>The short asset names of a bundle's container and a few header facts (compression, file names inside).</summary>
    public sealed class BundleNames
    {
        public enum Kind { Ok, Lzma, Unreadable }

        public Kind Status;
        /// <summary>Short names (lower case); null unless <see cref="Status"/> is Ok.</summary>
        public HashSet<string> Names;
        /// <summary>Block compression of the data: none, LZMA, LZ4, LZ4HC (or a number); null when unknown.</summary>
        public string Compression;
        /// <summary>The names of the serialized files inside (CAB-…), comma separated; null when unknown.</summary>
        public string SerializedFiles;
        /// <summary>The names of the resource files inside (CAB-….resS, ….resource), comma separated; null when unknown.</summary>
        public string ResourceFiles;
        /// <summary>The external files the serialized files reference (lower case, Unity's built-in files left out), comma
        /// separated; empty for none; null when unknown.</summary>
        public string Externals;
        /// <summary>Why the names are missing.</summary>
        public string Error;

        public int Count => Names?.Count ?? 0;

        /// <summary>True when the container holds an asset named <paramref name="asset"/> (short name or full path, any case).</summary>
        public bool Contains(string asset) => Names != null && asset != null && Names.Contains(ShortName(asset));

        /// <summary>The short name a load by name matches: last path component, the extension removed, lower case.</summary>
        public static string ShortName(string key)
        {
            if (key == null) return null;
            int s = key.LastIndexOf('/');
            string n = s >= 0 ? key.Substring(s + 1) : key;
            int d = n.LastIndexOf('.');
            if (d >= 0) n = n.Substring(0, d);
            return n.ToLowerInvariant();
        }

        static readonly string[] CompressionNames = { "none", "LZMA", "LZ4", "LZ4HC" };

        /// <summary>Reads the container of a UnityFS bundle. Never throws: failures come back as <see cref="Kind.Unreadable"/>
        /// (or <see cref="Kind.Lzma"/> when the blocks are LZMA).</summary>
        public static BundleNames Read(Func<Hs2BundleFile> open, string label)
        {
            var r = new BundleNames();
            Hs2BundleFile b;
            try { b = open(); }
            catch (Exception e)
            {
                r.Status = e.Message.IndexOf("LZMA", StringComparison.Ordinal) >= 0 ? Kind.Lzma : Kind.Unreadable;
                if (r.Status == Kind.Lzma) r.Compression = "LZMA";
                r.Error = e.Message;
                return r;
            }
            try
            {
                int ck = b.DataCompression;
                r.Compression = ck >= 0 && ck < CompressionNames.Length ? CompressionNames[ck] : ck.ToString(CultureInfo.InvariantCulture);
                var ser = new List<string>(); var res = new List<string>();
                foreach (var n in b.Nodes) (IsSerialized(n) ? ser : res).Add(n.Path);
                r.SerializedFiles = string.Join(",", ser);
                r.ResourceFiles = string.Join(",", res);
                if (ck == 1) { r.Status = Kind.Lzma; r.Error = "LZMA blocks"; return r; }
                var names = new HashSet<string>(StringComparer.Ordinal);
                var ext = new List<string>();
                bool found = false;
                foreach (var n in b.Nodes)
                {
                    if (!IsSerialized(n)) continue;
                    var node = n;
                    var sf = Hs2SerializedFile.FromStream(() => b.OpenNode(node), label + ":" + n.Path);
                    foreach (var x in sf.Externals)
                    {
                        string p = (x.Path ?? "").Replace('\\', '/').ToLowerInvariant();
                        if (p.Length == 0 || p.StartsWith("library/", StringComparison.Ordinal) || p.StartsWith("resources/", StringComparison.Ordinal)) continue;
                        int s = p.LastIndexOf('/');
                        p = s >= 0 ? p.Substring(s + 1) : p;
                        if (!ext.Contains(p)) ext.Add(p);
                    }
                    r.Externals = string.Join(",", ext);
                    foreach (var o in sf.Objects)
                    {
                        if (o.ClassId != Hs2SerializedFile.ClassAssetBundle) continue;
                        found = true;
                        foreach (var k in sf.AssetBundleContainerKeys(o)) names.Add(ShortName(k));
                    }
                }
                if (!found) { r.Status = Kind.Unreadable; r.Error = "no AssetBundle object"; return r; }
                r.Names = names;
                r.Status = Kind.Ok;
            }
            catch (Exception e)
            {
                r.Status = e.Message.IndexOf("LZMA", StringComparison.Ordinal) >= 0 ? Kind.Lzma : Kind.Unreadable;
                r.Error = e.Message;
                r.Names = null;
            }
            return r;
        }

        /// <summary>A bundle file on disk (or a stored zip entry at its offset).</summary>
        public static BundleNames ReadFile(string file, long offset, long length, string label = null) =>
            Read(() => Hs2BundleFile.OpenFile(file, offset, length, label ?? file), label ?? file);

        /// <summary>A zip entry: stored entries are read in place, deflated ones in memory.</summary>
        public static BundleNames ReadZipEntry(ZipIndex zip, string entryName)
        {
            string label = zip.Path + "!" + entryName;
            if (!zip.Entries.TryGetValue(entryName, out var e))
                return new BundleNames { Status = Kind.Unreadable, Error = "no zip entry " + entryName };
            return e.Method == 0
                ? ReadFile(zip.Path, e.DataOffset, e.Size, label)
                : Read(() => Hs2BundleFile.FromBytes(zip.ReadAll(e), label), label);
        }

        // Serialized files carry node flag 4; older writers leave the flags 0, so a name without an extension counts too.
        static bool IsSerialized(Hs2BundleNode n) => (n.Flags & 4) != 0 || (n.Flags == 0 && n.Path.IndexOf('.') < 0);

        public override string ToString() => Status == Kind.Ok ? Count + " names" : Status + (Error != null ? " (" + Error + ")" : "");
    }

    // ==================================================================== the index and the rule

    public sealed class AssetRedirect
    {
        /// <summary>The source a named load is served from: a zipmod's copy, the abdata file, or none.</summary>
        public struct Served
        {
            /// <summary>The zipmod whose copy serves (null: the abdata file or nothing).</summary>
            public IRedirectZip Zip;
            /// <summary>The abdata file serves.</summary>
            public bool Abdata;
            /// <summary>The serving source holds the name (false: the fallback of a name no copy holds).</summary>
            public bool HasName;
            public bool None => Zip == null && !Abdata;
            public string Describe() => Zip != null ? Zip.Guid + " (" + System.IO.Path.GetFileName(Zip.Path) + ")" : Abdata ? "abdata" : "none";
            public bool Same(Served o) => Abdata == o.Abdata && ReferenceEquals(Zip, o.Zip);
        }

        readonly Dictionary<string, List<IRedirectZip>> exact = new Dictionary<string, List<IRedirectZip>>(StringComparer.Ordinal);
        readonly Dictionary<string, string> folded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> withAbdata = new HashSet<string>(StringComparer.Ordinal);
        readonly List<string> redirectable = new List<string>();
        readonly Dictionary<(string zip, string path), BundleNames> late = new Dictionary<(string, string), BundleNames>();

        /// <summary>Reads the names of a copy whose names are not known (an LZMA copy, or names not read by the scan); set by the
        /// catalog (main thread, through Unity). Its results are kept for the session.</summary>
        public Func<IRedirectZip, string, BundleNames> ReadMissingNames;

        /// <summary>The redirectable paths (exact case, ordinal order).</summary>
        public IReadOnlyList<string> RedirectablePaths => redirectable;

        /// <summary>Redirected loads (served copy ≠ today's copy) per path, and the loads per (path, asset) of this session.</summary>
        public readonly Dictionary<string, int> Stats = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly List<(string path, string asset, string served, string today)> recent = new List<(string, string, string, string)>();

        /// <summary>
        /// Builds the index from the loaded zipmods in load order and the abdata predicate (true when a source's own store has the
        /// abdata-relative path). Only *.unity3d paths are indexed.
        /// </summary>
        public static AssetRedirect Build(IEnumerable<IRedirectZip> loadOrder, Func<string, bool> abdataHas)
        {
            var a = new AssetRedirect();
            foreach (var z in loadOrder)
            {
                if (z?.BundlePaths == null) continue;
                foreach (var p in z.BundlePaths)
                {
                    if (p == null || !p.EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!a.exact.TryGetValue(p, out var l)) { a.exact[p] = l = new List<IRedirectZip>(1); if (!a.folded.ContainsKey(p)) a.folded[p] = p; }
                    if (!l.Contains(z)) l.Add(z);
                }
            }
            foreach (var kv in a.exact)
            {
                bool ab = abdataHas != null && abdataHas(kv.Key);
                if (ab) a.withAbdata.Add(kv.Key);
                if (kv.Value.Count >= 2 || (ab && kv.Value.Count >= 1)) a.redirectable.Add(kv.Key);
            }
            a.redirectable.Sort(StringComparer.Ordinal);
            return a;
        }

        /// <summary>The exact indexed path of <paramref name="path"/> (the exact case first, then any case), or null.</summary>
        public string Exact(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            path = path.Replace('\\', '/');
            if (exact.ContainsKey(path)) return path;
            return folded.TryGetValue(path, out var e) ? e : null;
        }

        /// <summary>True when the path has two or more sources (asked case-exact first, then case-insensitively).</summary>
        public bool IsRedirectable(string path)
        {
            var e = Exact(path);
            return e != null && (withAbdata.Contains(e) ? exact[e].Count >= 1 : exact[e].Count >= 2);
        }

        /// <summary>The zipmods shipping the path, in load order (empty when none).</summary>
        public IReadOnlyList<IRedirectZip> CopiesOf(string path)
        {
            var e = Exact(path);
            return e != null ? exact[e] : (IReadOnlyList<IRedirectZip>)Array.Empty<IRedirectZip>();
        }

        /// <summary>True when an abdata file of the path exists (for indexed paths).</summary>
        public bool HasAbdata(string path) { var e = Exact(path); return e != null && withAbdata.Contains(e); }

        /// <summary>The names of a copy: read by the scan, else read on demand through <see cref="ReadMissingNames"/> (kept).</summary>
        public BundleNames NamesOf(IRedirectZip z, string path)
        {
            var e = Exact(path) ?? path;
            var n = z.NamesOf(e);
            if (n != null && n.Status == BundleNames.Kind.Ok) return n;
            if (late.TryGetValue((z.Path, e), out var l)) return l;
            if (ReadMissingNames == null) return n;
            BundleNames r;
            try { r = ReadMissingNames(z, e); }
            catch (Exception ex) { r = new BundleNames { Status = BundleNames.Kind.Unreadable, Error = ex.Message }; }
            if (r == null) return n;
            late[(z.Path, e)] = r;
            return r;
        }

        /// <summary>Names read on demand this session (an LZMA copy read through Unity), for the names cache.</summary>
        public IEnumerable<(string zip, string path, BundleNames names)> LateNames => late.Select(kv => (kv.Key.zip, kv.Key.path, kv.Value));

        /// <summary>
        /// The source of a named load of <paramref name="asset"/> from <paramref name="path"/>: the first copy in load order whose
        /// names hold the asset; else the abdata file when it exists; else the first copy (the asset is then missing). Only for
        /// redirectable paths (<see cref="IsRedirectable"/>); other paths keep the catalog's own source.
        /// </summary>
        public Served Serve(string path, string asset)
        {
            var e = Exact(path);
            if (e == null) return default;
            var copies = exact[e];
            foreach (var z in copies)
            {
                var n = NamesOf(z, e);
                if (n != null && n.Contains(asset)) return new Served { Zip = z, HasName = true };
            }
            if (withAbdata.Contains(e)) return new Served { Abdata = true };
            return copies.Count > 0 ? new Served { Zip = copies[0] } : default;
        }

        /// <summary>Counts a load whose served source differs from today's (the catalog calls it); true at the path's first one.</summary>
        public bool NoteRedirect(string path, string asset, Served served, Served today)
        {
            var e = Exact(path) ?? path;
            bool first = !Stats.ContainsKey(e);
            Stats[e] = (first ? 0 : Stats[e]) + 1;
            lock (recent) recent.Add((e, asset, served.Describe(), today.Describe()));
            return first;
        }

        /// <summary>The redirected loads since the last call (path, asset, served, today), oldest first.</summary>
        public List<(string path, string asset, string served, string today)> TakeRecent()
        {
            lock (recent)
            {
                var r = recent.ToList();
                recent.Clear();
                return r;
            }
        }

        /// <summary>Every asset name of the path's copies (lower case, ordinal order).</summary>
        public IReadOnlyList<string> AllNames(string path)
        {
            var e = Exact(path);
            if (e == null) return Array.Empty<string>();
            var s = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var z in exact[e]) { var n = NamesOf(z, e); if (n?.Names != null) s.UnionWith(n.Names); }
            return s.ToList();
        }

        // ---------------------------------------------------------------- read model

        public sealed class CopyFacts
        {
            /// <summary>The zipmod GUID, or null for the abdata file.</summary>
            public string Guid;
            /// <summary>The zipmod file, or the abdata file.</summary>
            public string File;
            /// <summary>The copy holds the asset (null: names unknown or no asset asked).</summary>
            public bool? Has;
            public string Names, Compression, SerializedFiles, ResourceFiles, Externals;
        }

        public sealed class Explanation
        {
            public string Path, Asset;
            public bool Indexed, Redirectable, HasAbdata;
            /// <summary>The abdata file first (when it exists), then the zipmod copies in load order.</summary>
            public List<CopyFacts> Copies = new List<CopyFacts>();
            /// <summary>"zipmod", "abdata" or "none"; for paths that are not redirectable: the catalog's own source.</summary>
            public string ServedKind, ServedGuid, ServedFile;
            public string TodayKind, TodayGuid, TodayFile;
            public bool RedirectOn, ZipmodSetOn;
            public override string ToString() =>
                $"{Path} {Asset ?? "(all)"}: redirectable {Redirectable}, served {ServedKind} {ServedGuid}, today {TodayKind} {TodayGuid}, copies {Copies.Count}";
        }

        /// <summary>
        /// The read model of one named load: the path's copies (with, for <paramref name="asset"/>, whether each holds it), the
        /// source the rule serves and today's source. <paramref name="abdataFile"/> is the abdata file of the path (null when
        /// none); <paramref name="today"/> is the catalog's own source.
        /// </summary>
        public Explanation Explain(string path, string asset, string abdataFile, Served today, Func<string, BundleNames> abdataNames = null)
        {
            var x = new Explanation { Path = path, Asset = asset };
            var e = Exact(path);
            x.Indexed = e != null;
            x.Redirectable = IsRedirectable(path);
            x.HasAbdata = e != null ? withAbdata.Contains(e) : abdataFile != null;
            if (e != null) x.Path = e;
            if (x.HasAbdata && abdataFile != null)
            {
                var an = abdataNames?.Invoke(abdataFile);
                x.Copies.Add(Facts(null, abdataFile, an, asset));
            }
            if (e != null) foreach (var z in exact[e]) x.Copies.Add(Facts(z.Guid, z.Path, NamesOf(z, e), asset));
            var s = x.Redirectable ? Serve(e, asset) : today;
            Fill(s, abdataFile, out x.ServedKind, out x.ServedGuid, out x.ServedFile);
            Fill(today, abdataFile, out x.TodayKind, out x.TodayGuid, out x.TodayFile);
            return x;
        }

        static CopyFacts Facts(string guid, string file, BundleNames n, string asset) => new CopyFacts
        {
            Guid = guid, File = file,
            Has = asset == null || n?.Names == null ? (bool?)null : n.Contains(asset),
            Names = n?.ToString(), Compression = n?.Compression, SerializedFiles = n?.SerializedFiles, ResourceFiles = n?.ResourceFiles,
            Externals = n?.Externals,
        };

        static void Fill(Served s, string abdataFile, out string kind, out string guid, out string file)
        {
            kind = s.Zip != null ? "zipmod" : s.Abdata ? "abdata" : "none";
            guid = s.Zip?.Guid;
            file = s.Zip != null ? s.Zip.Path : s.Abdata ? abdataFile : null;
        }
    }

    // ==================================================================== the names cache

    /// <summary>
    /// The names of the copies of redirectable paths, cached per (zip path, zip stamp, bundle path) in
    /// Cache/bundlenames-&lt;hash&gt;.tsv. Format (UTF-8, tab separated): a header line, then one line per copy
    ///   P zip-path stamp bundle-path status compression serialized-files resource-files externals name name ...
    /// (status Ok: the names follow; otherwise the error follows)
    /// and a last line "#end &lt;count&gt;" (a file without it, or with another count, is discarded: a cold read). Written to
    /// &lt;file&gt;.tmp and moved over the old file.
    /// </summary>
    public static class BundleNamesCache
    {
        public const string Header = "#hs2next-bundlenames\t1";
        const string End = "#end";

        /// <summary>Reads the cache; an unreadable or incomplete file gives an empty result and a message in <paramref name="error"/>.</summary>
        public static Dictionary<(string zip, string path), (string stamp, BundleNames names)> Read(string file, out string error)
        {
            error = null;
            var d = new Dictionary<(string, string), (string, BundleNames)>();
            if (file == null || !File.Exists(file)) return d;
            try
            {
                var lines = File.ReadAllLines(file);
                if (lines.Length == 0 || lines[0] != Header) { error = "names cache " + file + ": other version, read cold"; return d; }
                int n = 0;
                bool ended = false;
                for (int i = 1; i < lines.Length; i++)
                {
                    var t = lines[i].Split('\t');
                    if (t[0] == End)
                    {
                        if (t.Length != 2 || !int.TryParse(t[1], NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count != n || i != lines.Length - 1)
                            throw new FormatException("bad end line");
                        ended = true;
                        break;
                    }
                    if (t[0] != "P" || t.Length < 9) throw new FormatException("bad line " + i);
                    if (!Enum.TryParse(t[4], false, out BundleNames.Kind status) || !Enum.IsDefined(typeof(BundleNames.Kind), status)) throw new FormatException("bad status on line " + i);
                    var b = new BundleNames
                    {
                        Status = status,
                        Compression = Blank(t[5]), SerializedFiles = Blank(t[6]), ResourceFiles = Blank(t[7]), Externals = t[8],
                    };
                    if (b.Status == BundleNames.Kind.Ok)
                    {
                        b.Names = new HashSet<string>(StringComparer.Ordinal);
                        for (int k = 9; k < t.Length; k++) b.Names.Add(t[k]);
                    }
                    else if (t.Length > 9) b.Error = t[9];
                    d[(t[1], t[3])] = (t[2], b);
                    n++;
                }
                if (!ended) throw new FormatException("no end line");
            }
            catch (Exception e)
            {
                error = "names cache " + file + " discarded (" + e.Message + "), read cold";
                d.Clear();
            }
            return d;
        }

        /// <summary>Writes the cache (to a temporary file moved over the old one); returns null or the error.</summary>
        public static string Write(string file, IEnumerable<(string zip, string stamp, string path, BundleNames names)> entries)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append(Header).Append('\n');
                int n = 0;
                foreach (var (zip, stamp, path, b) in entries.OrderBy(x => x.zip, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.path, StringComparer.Ordinal))
                {
                    if (b == null || Bad(zip) || Bad(path) || Bad(stamp)) continue;
                    sb.Append("P\t").Append(zip).Append('\t').Append(stamp).Append('\t').Append(path).Append('\t').Append(b.Status)
                      .Append('\t').Append(Clean(b.Compression)).Append('\t').Append(Clean(b.SerializedFiles)).Append('\t').Append(Clean(b.ResourceFiles))
                      .Append('\t').Append(Clean(b.Externals));
                    if (b.Status == BundleNames.Kind.Ok) foreach (var nm in b.Names.OrderBy(x => x, StringComparer.Ordinal)) { if (!Bad(nm)) sb.Append('\t').Append(nm); }
                    else if (b.Error != null) sb.Append('\t').Append(Clean(b.Error));
                    sb.Append('\n');
                    n++;
                }
                sb.Append(End).Append('\t').Append(n.ToString(CultureInfo.InvariantCulture)).Append('\n');
                var dir = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                ReplaceFile(file, sb.ToString());
                return null;
            }
            catch (Exception e) { return "names cache " + file + " not written: " + e.Message; }
        }

        /// <summary>Writes <paramref name="text"/> to &lt;file&gt;.tmp and moves it over <paramref name="file"/> (UTF-8, no BOM).</summary>
        public static void ReplaceFile(string file, string text)
        {
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(file)) File.Replace(tmp, file, null);
            else File.Move(tmp, file);
        }

        static bool Bad(string s) => s == null || s.IndexOf('\t') >= 0 || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0;
        static string Clean(string s) => s == null ? "" : s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        static string Blank(string s) => s.Length == 0 ? null : s;
    }
}
