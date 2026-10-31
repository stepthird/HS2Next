// Shared zipmod handling for sources that have a folder of Sideloader zipmods: enumeration,
// manifest/list scanning and the per-source scan cache.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using HS2Next.Content.Studio;
using HS2Next.IO;
using UnityEngine;

namespace HS2Next.Content
{
    public abstract class ZipmodSourceBase : IContentSource
    {
        // Version 5: character list CSVs read like Sideloader (CharaListCsv). Version 6: manifest name/author/website/version
        // and <migrationInfo> entries. Version 7: the manifest's <game> values (W) and an end line; a version 6 cache is
        // upgraded by reading the <game> values of each manifest alone (no cold scan). Version 8: the names of the manifest's
        // top-level elements (T; any element is read from the zip on demand, ZipmodInfo.ReadManifestElement) instead of the
        // rows of one plugin's declarations; a version 6 or 7 cache is upgraded by reading each manifest alone (its old
        // declaration rows are dropped).
        public const string CacheHeader = "#hs2next-modlists\t8";
        const string CacheHeader7 = "#hs2next-modlists\t7", CacheHeader6 = "#hs2next-modlists\t6";
        const string CacheEnd = "#end";

        /// <summary>
        /// Applies the zipmod loader's game filter when the zipmods are built from the scan (ContentCatalog sets it from
        /// ContentCatalog.SideloaderZipmodSet before each scan): a zipmod whose &lt;game&gt; values name no HS2 game gets
        /// <see cref="ZipmodInfo.SkippedBy"/> "game" and none of its rows. The cache keeps every zipmod's lines either way.
        /// </summary>
        public bool GameFilter { get; set; } = true;

        public string Id { get; }
        public string DisplayName { get; }

        /// <summary>Folder searched (recursively) for *.zipmod / *.zip files.</summary>
        public string ModsDir { get; }

        public IReadOnlyList<ZipmodInfo> Zipmods { get; private set; } = Array.Empty<ZipmodInfo>();

        protected ZipmodSourceBase(string id, string displayName, string modsDir)
        {
            Id = id; DisplayName = displayName; ModsDir = modsDir;
        }

        public abstract IEnumerable<ListEntry> ReadLists(IBundleCache cache);
        public abstract bool HasBundle(string path);
        public virtual IEnumerable<string> EnumerateStudioInfoBundles() => Enumerable.Empty<string>();
        public abstract AssetBundle OpenBundle(string path, IBundleCache cache);

        public IEnumerable<string> EnumerateZipmodFiles() =>
            ModsDir == null || !Directory.Exists(ModsDir) ? Enumerable.Empty<string>() :
            Directory.EnumerateFiles(ModsDir, "*.zip*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".zipmod", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        // Cache file format (tab separated; a header line, then one block per zipmod):
        //   Z path stamp guid | B abdata-relative-path
        //   E category id key value key value ... (a character list row; columns with an empty key are left out)
        //   S entry-path header-count (a Studio list CSV; its rows follow as C lines)
        //   C field field ... (one CSV row; tabs inside fields are stored as U+001F)
        //   M manifest bundle asset bundle asset ... (an hs2-scene-dependency declaration)
        //   T name name ... (the manifest root's child element names, distinct, in manifest order; a line without names: none)
        //   N name author website version (manifest details; U+0001 marks an absent element)
        //   G kind category guidOld guidNew idOld idNew (a <migrationInfo> entry Sideloader accepts)
        //   W game game ... (the manifest's non-blank <game> values, trimmed; a line without values: none)
        // and a last line "#end <zipmod count>". The cache is rewritten (to <file>.tmp, then moved over the old file) only when
        // something changed. A cache that cannot be parsed, or lacks its end line, is ignored with one message (a cold scan).
        public ZipmodScanResult ScanZipmods(string cacheFile) => ScanZipmods(cacheFile, null);

        // A cached zipmod block (rows checked when the cache was read).
        sealed class Cached { public string Stamp, Guid; public List<string[]> Rows = new List<string[]>(); }

        /// <summary>
        /// Reads a scan cache. Returns null (with <paramref name="error"/>) when the file cannot be parsed: a line that lacks its
        /// fields or numbers, or a version 7 / 8 file without its end line (e.g. cut short). <paramref name="version"/>: 6 (no
        /// &lt;game&gt; lines, no end line), 7 (no element lines; old declaration lines U, dropped by the scan) or 8.
        /// </summary>
        static Dictionary<string, Cached> ReadCache(string cacheFile, out int version, out string error)
        {
            version = 8; error = null;
            var cache = new Dictionary<string, Cached>(StringComparer.OrdinalIgnoreCase);
            if (cacheFile == null || !File.Exists(cacheFile)) return cache;
            try
            {
                Cached cur = null; bool first = true, ended = false; int blocks = 0;
                foreach (var line in File.ReadLines(cacheFile))
                {
                    if (ended) throw new FormatException("lines after the end line");
                    if (first)
                    {
                        first = false;
                        if (line == CacheHeader6) version = 6;
                        else if (line == CacheHeader7) version = 7;
                        else if (line != CacheHeader) return cache;   // another version: cold scan, no message
                        continue;
                    }
                    var t = line.Split('\t');
                    if (t[0] == CacheEnd)
                    {
                        if (t.Length != 2 || !int.TryParse(t[1], NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n != blocks)
                            throw new FormatException("bad end line");
                        ended = true;
                        continue;
                    }
                    if (t[0] == "Z")
                    {
                        if (t.Length < 4) throw new FormatException("Z line with " + t.Length + " fields");
                        cur = new Cached { Stamp = t[2], Guid = t[3].Length == 0 ? null : t[3] };
                        cache[t[1]] = cur;
                        blocks++;
                        continue;
                    }
                    if (cur == null || t[0].Length != 1 || "EBSCMUNGWT".IndexOf(t[0][0]) < 0) throw new FormatException("unexpected line \"" + Short(line) + "\"");
                    if (!RowValid(t)) throw new FormatException("incomplete " + t[0] + " line \"" + Short(line) + "\"");
                    cur.Rows.Add(t);
                }
                if (version >= 7 && !ended) throw new FormatException("no end line (cut short)");
            }
            catch (Exception e)
            {
                error = $"zipmod scan cache {cacheFile} ignored ({e.Message}): cold scan";
                return null;
            }
            return cache;
        }

        static string Short(string s) => s.Length > 80 ? s.Substring(0, 80) + "..." : s;
        static bool Int(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

        // The fields the block builder below reads exist and parse.
        static bool RowValid(string[] r)
        {
            switch (r[0])
            {
                case "E": return r.Length >= 3 && Int(r[1]) && Int(r[2]);
                case "B": return r.Length >= 2 && r[1].Length > 0;
                case "S": return r.Length >= 3 && Int(r[2]);
                case "C": return true;
                case "M": return r.Length >= 2;
                case "U": return r.Length >= 2;
                case "N": return r.Length >= 5;
                case "G": return r.Length >= 7 && Int(r[2]) && Int(r[5]) && Int(r[6]) && Enum.TryParse(r[1], out MigrationKind _);
                case "W": return true;
                case "T": return true;
                default: return false;
            }
        }

        /// <summary>
        /// As <see cref="ScanZipmods(string)"/>; <paramref name="progress"/> gets the number of zipmods visited so far after
        /// each one (on the calling thread; the scan touches no Unity API, so it may run on a worker thread).
        /// </summary>
        public ZipmodScanResult ScanZipmods(string cacheFile, Action<int> progress)
        {
            var res = new ZipmodScanResult();
            var cache = ReadCache(cacheFile, out int version, out string cacheError);
            bool dropped = false;
            if (cache == null)
            {
                res.CacheDiscarded = true;
                res.Errors.Add(cacheError);
                cache = new Dictionary<string, Cached>(StringComparer.OrdinalIgnoreCase);
            }
            var outLines = new List<string> { CacheHeader };
            int files = 0;
            foreach (var path in EnumerateZipmodFiles())
            {
                files++;
                var fi = new FileInfo(path);
                string stamp = fi.Length + ":" + fi.LastWriteTimeUtc.Ticks;
                if (!cache.TryGetValue(path, out var c) || c.Stamp != stamp)
                {
                    res.Scanned++;
                    c = new Cached { Stamp = stamp };
                    try { var s = ScanZip(path, stamp, res.Errors); c.Guid = s.Item2; c.Rows = s.Item3; }
                    catch (Exception ex) { res.Unreadable++; res.Errors.Add($"zip error {path}: {ex.Message}"); }
                }
                else
                {
                    // an older block: the declaration rows of version 7 and before go; the <game> values (version 6) and the
                    // element names (versions 6 and 7) are read from the manifest alone
                    if (c.Rows.RemoveAll(r => r[0] == "U") > 0) dropped = true;
                    bool needW = c.Guid != null && !c.Rows.Any(r => r[0] == "W"), needT = c.Guid != null && !c.Rows.Any(r => r[0] == "T");
                    if (needW || needT)
                    {
                        string mx = null, err = null;
                        try { mx = new ZipIndex(path).ReadText("manifest.xml"); }
                        catch (Exception ex) { err = ex.Message; }
                        if (needW)
                        {
                            res.GamesRead++;
                            c.Rows.Insert(0, GamesRow(err == null ? ReadGames(mx) : null));
                        }
                        if (needT)
                        {
                            res.ElementsRead++;
                            c.Rows.Insert(c.Rows.Count > 0 && c.Rows[0][0] == "W" ? 1 : 0, ElementsRow(err == null ? ParseManifest(mx) : null));
                        }
                        if (err != null) res.Errors.Add($"manifest unreadable {path}: {err}");
                    }
                }
                outLines.Add($"Z\t{path}\t{c.Stamp}\t{c.Guid}");
                var info = new ZipmodInfo(this, path, c.Guid) { Stamp = c.Stamp };
                res.Zipmods.Add(info);
                progress?.Invoke(files);
                foreach (var r in c.Rows)
                    if (r[0] == "W") info.Games = r.Skip(1).Where(g => g.Trim().Length > 0).Select(g => g.Trim()).ToArray();
                if (info.Guid != null && GameFilter && !SideloaderZipmods.LoadsOnHs2(info.Games))
                {
                    info.SkippedBy = "game";
                    res.SkippedByGame++;
                }
                bool load = info.Guid != null && info.SkippedBy == null;
                StudioCsvFile csv = null;
                foreach (var r in c.Rows)
                {
                    outLines.Add(string.Join("\t", r));
                    if (!load) continue;
                    switch (r[0])
                    {
                        case "B": info.AddBundlePath(r[1]); continue;
                        case "S":
                            csv = new StudioCsvFile(r[1], int.Parse(r[2]), new List<string[]>());
                            info.AddStudioCsv(csv);
                            continue;
                        case "C":
                            if (csv == null) continue;
                            var f = new string[r.Length - 1];
                            for (int k = 1; k < r.Length; k++) f[k - 1] = StudioCsv.Unescape(r[k]);
                            csv.Rows.Add(f);
                            continue;
                        case "M":
                            var d = new SceneDependencyDecl { Manifest = r[1] };
                            for (int k = 2; k + 1 < r.Length; k += 2) d.Dependencies.Add((r[k], r[k + 1]));
                            info.AddSceneDependency(d);
                            continue;
                        case "T":
                            info.SetManifestElements(r.Skip(1).Where(n => n.Length > 0));
                            continue;
                        case "N":
                            if (r.Length >= 5) { info.Name = Absent(r[1]); info.Author = Absent(r[2]); info.Website = Absent(r[3]); info.Version = Absent(r[4]); }
                            continue;
                        case "G":
                            if (r.Length >= 7 && Enum.TryParse(r[1], out MigrationKind kind))
                                info.AddMigration(new ZipmodMigration
                                {
                                    Kind = kind, Category = int.Parse(r[2], CultureInfo.InvariantCulture), GuidOld = r[3], GuidNew = r[4],
                                    IdOld = int.Parse(r[5], CultureInfo.InvariantCulture), IdNew = int.Parse(r[6], CultureInfo.InvariantCulture),
                                });
                            continue;
                        case "W":
                            continue;
                    }
                    var e = new ListEntry { Category = int.Parse(r[1], CultureInfo.InvariantCulture), Key = new ContentId(info.Guid, int.Parse(r[2], CultureInfo.InvariantCulture)) };
                    for (int k = 3; k + 1 < r.Length; k += 2) e.Values[r[k]] = r[k + 1];
                    info.AddEntry(e);
                }
            }
            if (cacheFile != null && (res.Scanned > 0 || files != cache.Count || version != 8 || dropped || res.GamesRead > 0 || res.ElementsRead > 0 || res.CacheDiscarded))
            {
                outLines.Add(CacheEnd + "\t" + files.ToString(CultureInfo.InvariantCulture));
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cacheFile));
                    BundleNamesCache.ReplaceFile(cacheFile, string.Join("\n", outLines) + "\n");
                }
                catch (Exception ex) { res.Errors.Add($"zipmod scan cache {cacheFile} not written: {ex.Message}"); }
            }
            Zipmods = res.Zipmods;
            return res;
        }

        /// <summary>The non-blank &lt;game&gt; values of a manifest (trimmed), as the zipmod loader reads them; empty for none.</summary>
        public static List<string> ReadGames(string manifestXml)
        {
            var l = new List<string>();
            if (string.IsNullOrEmpty(manifestXml) || manifestXml.IndexOf("<game", StringComparison.OrdinalIgnoreCase) < 0) return l;
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(manifestXml);
                foreach (XmlNode n in doc.GetElementsByTagName("game"))
                {
                    var v = n.InnerText?.Trim();
                    if (!string.IsNullOrEmpty(v)) l.Add(v);
                }
            }
            catch (XmlException)
            {
                foreach (Match m in Regex.Matches(manifestXml, "<game>(.*?)</game>", RegexOptions.Singleline))
                {
                    var v = m.Groups[1].Value.Trim();
                    if (v.Length > 0) l.Add(v);
                }
            }
            return l;
        }

        /// <summary>The manifest as an XML document, or null (absent or not XML).</summary>
        static XmlDocument ParseManifest(string manifestXml)
        {
            if (string.IsNullOrEmpty(manifestXml)) return null;
            try { var d = new XmlDocument(); d.LoadXml(manifestXml); return d; }
            catch (XmlException) { return null; }
        }

        /// <summary>Row T: the distinct names of the manifest root's child elements in manifest order (none for null).</summary>
        static string[] ElementsRow(XmlDocument doc)
        {
            var r = new List<string> { "T" };
            if (doc?.DocumentElement != null)
                foreach (XmlNode n in doc.DocumentElement.ChildNodes)
                    if (n is XmlElement e && r.IndexOf(Clean(e.Name), 1) < 0) r.Add(Clean(e.Name));
            return r.ToArray();
        }

        static string[] GamesRow(List<string> games)
        {
            var r = new List<string> { "W" };
            if (games != null) r.AddRange(games.Select(Clean));
            return r.ToArray();
        }

        static (string, string, List<string[]>) ScanZip(string path, string stamp, List<string> errors)
        {
            var z = new ZipIndex(path);
            string guid = null;
            var mx = z.ReadText("manifest.xml");
            XmlDocument doc = null;
            if (mx != null)
            {
                try { doc = new XmlDocument(); doc.LoadXml(mx); guid = doc.SelectSingleNode("//guid")?.InnerText?.Trim(); }
                catch { doc = null; var m = Regex.Match(mx, "<guid>(.*?)</guid>"); if (m.Success) guid = m.Groups[1].Value.Trim(); }
            }
            var rows = new List<string[]>();
            if (string.IsNullOrEmpty(guid)) return (stamp, null, rows);
            rows.Add(GamesRow(ReadGames(mx)));
            rows.Add(ElementsRow(doc));
            if (doc?.DocumentElement != null)
            {
                rows.Add(new[] { "N", Present(RootText(doc, "name")), Present(RootText(doc, "author")), Present(RootText(doc, "website")), Present(RootText(doc, "version")) });
                foreach (var m in ReadMigrations(doc, guid, errors, path)) rows.Add(m);
            }
            // Character list CSVs, in zip-entry order, read like Sideloader
            // (CharaListCsv: header lines may carry trailing commas, rows end at the first line without ',',
            // fields are kept verbatim, no distributionNo gate).
            foreach (var name in z.Entries.Keys)
            {
                if (!CharaListCsv.IsCharaListEntry(name)) continue;
                var text = z.ReadText(name);
                if (text == null) continue;
                var csv = CharaListCsv.Parse(text, out var error);
                if (csv == null) { errors?.Add($"list skipped {path} :: {name}: {error}"); continue; }
                if (csv.BadIdRows > 0) errors?.Add($"list rows without an integer id skipped {path} :: {name}: {csv.BadIdRows}");
                var cat = csv.Category.ToString(CultureInfo.InvariantCulture);
                foreach (var (id, fields) in csv.Rows)
                {
                    var row = new List<string> { "E", cat, id.ToString(CultureInfo.InvariantCulture) };
                    for (int k = 0; k < csv.Keys.Length && k < fields.Length; k++)
                    {
                        if (csv.Keys[k].Length == 0) continue;
                        row.Add(Clean(csv.Keys[k])); row.Add(Clean(fields[k]));
                    }
                    rows.Add(row.ToArray());
                }
            }
            // Studio list CSVs, in zip-entry order. ReadText decodes like
            // Sideloader's StreamReader (byte-order mark detection, else UTF-8; U+FFFD for invalid bytes).
            foreach (var name in z.Entries.Keys)
            {
                if (!StudioCsv.IsStudioListEntry(name) || name.IndexOfAny(BadNameChars) >= 0) continue;
                var text = z.ReadText(name);
                if (text == null) continue;
                var csvRows = StudioCsv.Split(text, out int headers);
                rows.Add(new[] { "S", name, headers.ToString() });
                foreach (var f in csvRows)
                {
                    var r = new string[f.Length + 1];
                    r[0] = "C";
                    for (int k = 0; k < f.Length; k++) r[k + 1] = StudioCsv.Escape(f[k]);
                    rows.Add(r);
                }
            }
            // <hs2-scene-dependency manifest="X"> declarations (dependency manifests).
            if (mx != null && mx.IndexOf("hs2-scene-dependency", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var decls = new List<SceneDependencyDecl>();
                if (doc != null)
                {
                    foreach (XmlNode n in doc.GetElementsByTagName("hs2-scene-dependency"))
                    {
                        var d = new SceneDependencyDecl { Manifest = n.Attributes?["manifest"]?.Value?.Trim() };
                        foreach (XmlNode c in n.ChildNodes)
                            if (c.Name == "dependency")
                                d.Dependencies.Add((c.Attributes?["bundle"]?.Value ?? "", c.Attributes?["asset"]?.Value ?? ""));
                        decls.Add(d);
                    }
                }
                else
                    foreach (Match m in Regex.Matches(mx, "<hs2-scene-dependency[^>]*manifest\\s*=\\s*\"([^\"]*)\""))
                        decls.Add(new SceneDependencyDecl { Manifest = m.Groups[1].Value.Trim() });
                foreach (var d in decls)
                {
                    if (string.IsNullOrEmpty(d.Manifest)) continue;
                    var r = new List<string> { "M", Clean(d.Manifest) };
                    foreach (var (b, a) in d.Dependencies) { r.Add(Clean(b)); r.Add(Clean(a)); }
                    rows.Add(r.ToArray());
                }
            }
            // Everything shipped under abdata/, for bundle lookups (also across zipmods).
            foreach (var name in z.Entries.Keys)
                if (name.Length > 7 && name.StartsWith("abdata/", StringComparison.OrdinalIgnoreCase) && !name.EndsWith("/")
                    && name.IndexOfAny(BadNameChars) < 0)
                    rows.Add(new[] { "B", name.Substring(7) });
            return (stamp, guid, rows);
        }

        static readonly char[] BadNameChars = { '\t', '\r', '\n' };

        const string AbsentMark = "\u0001";
        static string Present(string s) => s == null ? AbsentMark : Clean(s);
        static string Absent(string s) => s == AbsentMark ? null : s;

        /// <summary>Text of a direct child element of the manifest root, trimmed (Sideloader reads name/author/website/version so).</summary>
        static string RootText(XmlDocument doc, string name)
        {
            foreach (XmlNode n in doc.DocumentElement.ChildNodes)
                if (n is XmlElement e && e.Name == name) return e.InnerText.Trim();
            return null;
        }

        // <migrationInfo> (the first such child of the root) with its <info> lines, read like Sideloader's manifest loader:
        // migrationType defaults to Migrate (an unknown name rejects the line), guidOld is required, guidNew defaults to this
        // manifest's GUID; MigrateAll / StripAll need nothing else; Migrate needs a category (member name or number) and
        // integer idOld / idNew. Rejected lines are logged and left out, as Sideloader logs and skips them.
        static IEnumerable<string[]> ReadMigrations(XmlDocument doc, string guid, List<string> errors, string path)
        {
            XmlElement mi = null;
            foreach (XmlNode n in doc.DocumentElement.ChildNodes)
                if (n is XmlElement e && e.Name == "migrationInfo") { mi = e; break; }
            if (mi == null) yield break;
            foreach (XmlNode n in mi.ChildNodes)
            {
                if (!(n is XmlElement info) || info.Name != "info") continue;
                string typeText = info.GetAttributeNode("migrationType")?.Value;
                var kind = MigrationKind.Migrate;
                if (!string.IsNullOrWhiteSpace(typeText) && (!Enum.TryParse(typeText.Trim(), false, out kind) || !Enum.IsDefined(typeof(MigrationKind), kind)))
                {
                    errors?.Add($"migration line skipped {path}: unknown migrationType \"{typeText}\"");
                    continue;
                }
                string oldGuid = info.GetAttributeNode("guidOld")?.Value;
                string newGuid = info.GetAttributeNode("guidNew")?.Value;
                if (string.IsNullOrWhiteSpace(newGuid)) newGuid = guid;
                if (string.IsNullOrEmpty(oldGuid)) { errors?.Add($"migration line skipped {path}: no guidOld"); continue; }
                if (kind != MigrationKind.Migrate)
                {
                    yield return new[] { "G", kind.ToString(), "0", Clean(oldGuid.Trim()), Clean(newGuid.Trim()), "0", "0" };
                    continue;
                }
                if (!ListCategories.TryParse(info.GetAttributeNode("category")?.Value, out int category)
                    || !int.TryParse(info.GetAttributeNode("idOld")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idOld)
                    || !int.TryParse(info.GetAttributeNode("idNew")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idNew))
                {
                    errors?.Add($"migration line skipped {path}: Migrate needs a category, idOld and idNew");
                    continue;
                }
                yield return new[] { "G", kind.ToString(), category.ToString(CultureInfo.InvariantCulture), Clean(oldGuid.Trim()), Clean(newGuid.Trim()),
                    idOld.ToString(CultureInfo.InvariantCulture), idNew.ToString(CultureInfo.InvariantCulture) };
            }
        }

        static string Clean(string s) => s.IndexOfAny(BadNameChars) < 0 ? s : s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

        /// <summary>Short stable hash of a path, for cache file names (case-insensitive, FNV-1a).</summary>
        public static string PathHash(string path)
        {
            uint h = 2166136261;
            foreach (char ch in Path.GetFullPath(path).TrimEnd('\\', '/').ToLowerInvariant()) { h ^= ch; h *= 16777619; }
            return h.ToString("x8");
        }

        public override string ToString() => $"{Id} ({DisplayName})";
    }
}
