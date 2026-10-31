// The zipmod manifest infos the Maker lists need: Sideloader's `<faceSkinInfo>`
// (a skin row's head) and `<headPresetInfo>` (the GUIDs of a head's face preset ids), read from the manifest.xml of the
// zipmods that have rows of 210 / 211 / 110 / 111, kept in a cache of their own (`Cache/makerinfo-<source>.tsv`) and
// refreshed per zipmod stamp (size + mtime, as the zipmod list cache). The zipmod list cache (`modlists-*.tsv`) is not
// touched: a version bump there forces the cold scan of every zipmod.
// Elements are read as Sideloader's manifest loader reads them (behaviour only): direct children of the manifest root;
// faceSkinInfo needs integer skinID / headID attributes and a non-blank headGUID (else the line is skipped);
// headPresetInfo needs a non-blank `preset` attribute and an integer headID element; its GUID elements are blank = none.
// Unity-free (plain Mono builds it: Tools/mono_tests.ps1 -Suite maker).
//
// Cache format (UTF-8, tab separated): header line, then one block per zipmod
//   Z path stamp guid
//   F skinID headID headGUID                       (one faceSkinInfo, manifest order)
//   P preset headID headGUID g1 ... g18             (one headPresetInfo; GUIDs in HeadPresets.Elements order, "" = none)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using HS2Next.IO;

namespace HS2Next.Content.Maker
{
    /// <summary>One `&lt;faceSkinInfo skinID headID headGUID/&gt;` line of a manifest.</summary>
    public sealed class FaceSkinEntry
    {
        public int SkinId, HeadId;
        public string HeadGuid;
        public override string ToString() => $"skin {SkinId} -> {HeadGuid}#{HeadId}";
    }

    /// <summary>One `&lt;headPresetInfo preset="…"&gt;` block of a manifest.</summary>
    public sealed class HeadPresetEntry
    {
        public string Preset;
        public int HeadId;
        /// <summary>The head's GUID (null: a vanilla head).</summary>
        public string HeadGuid;
        /// <summary>The GUID elements in <see cref="HeadPresets.Elements"/> order; null = element absent or blank.</summary>
        public string[] Guids = new string[HeadPresets.Elements.Length];
        /// <summary>The declaring zipmod.</summary>
        public ZipMakerInfo Zipmod;

        /// <summary>The GUID of one element (e.g. "skinGUID"), or null.</summary>
        public string Guid(string element)
        {
            int i = Array.IndexOf(HeadPresets.Elements, element);
            return i >= 0 ? Guids[i] : null;
        }

        public override string ToString() => $"{Preset} {HeadGuid ?? "(vanilla)"}#{HeadId}";
    }

    /// <summary>The Maker infos of one zipmod.</summary>
    public sealed class ZipMakerInfo
    {
        public string Path, Stamp, Guid;
        public readonly List<FaceSkinEntry> FaceSkins = new List<FaceSkinEntry>();
        public readonly List<HeadPresetEntry> HeadPresets = new List<HeadPresetEntry>();

        Dictionary<int, FaceSkinEntry> bySkin;
        int indexed = -1;

        /// <summary>The first faceSkinInfo whose skinID is <paramref name="skinId"/> (Sideloader takes the first), or null.</summary>
        public FaceSkinEntry FaceSkinFor(int skinId)
        {
            if (FaceSkins.Count == 0) return null;
            if (bySkin == null || indexed != FaceSkins.Count)
            {
                bySkin = new Dictionary<int, FaceSkinEntry>(FaceSkins.Count);
                foreach (var e in FaceSkins) if (!bySkin.ContainsKey(e.SkinId)) bySkin[e.SkinId] = e;
                indexed = FaceSkins.Count;
            }
            return bySkin.TryGetValue(skinId, out var r) ? r : null;
        }

        public override string ToString() => $"{Guid} {Path} (faceSkinInfo {FaceSkins.Count}, headPresetInfo {HeadPresets.Count})";
    }

    /// <summary>Reads and caches the Maker infos of a set of zipmods.</summary>
    public sealed class MakerInfoCache
    {
        public const string Header = "#hs2next-makerinfo\t1";

        readonly Dictionary<string, ZipMakerInfo> byPath = new Dictionary<string, ZipMakerInfo>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Infos by zipmod path (only the zipmods passed to <see cref="Load"/>).</summary>
        public IReadOnlyDictionary<string, ZipMakerInfo> ByPath => byPath;
        /// <summary>Manifests read from disk by the last <see cref="Load"/> (not taken from the cache), and unreadable ones.</summary>
        public int Scanned { get; private set; }
        public int Unreadable { get; private set; }
        /// <summary>Skipped lines and unreadable files (Sideloader logs and skips them too).</summary>
        public readonly List<string> Errors = new List<string>();
        /// <summary>Milliseconds of the last <see cref="Load"/>.</summary>
        public double LoadMs { get; private set; }

        public ZipMakerInfo Get(string zipPath) => zipPath != null && byPath.TryGetValue(zipPath, out var z) ? z : null;

        public int FaceSkinCount => byPath.Values.Sum(z => z.FaceSkins.Count);
        public int HeadPresetCount => byPath.Values.Sum(z => z.HeadPresets.Count);

        /// <summary>
        /// Loads the infos of <paramref name="zipmods"/> (path, GUID): from <paramref name="cacheFile"/> when the stamp matches,
        /// else from the zipmod's manifest.xml. The cache is rewritten when something changed (null: no cache).
        /// </summary>
        public static MakerInfoCache Load(string cacheFile, IEnumerable<(string path, string guid)> zipmods)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var c = new MakerInfoCache();
            var cached = ReadCache(cacheFile);
            var list = zipmods.GroupBy(z => z.path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
            foreach (var (path, guid) in list)
            {
                string stamp;
                try { var fi = new FileInfo(path); stamp = fi.Exists ? fi.Length + ":" + fi.LastWriteTimeUtc.Ticks : null; }
                catch (Exception) { stamp = null; }
                if (stamp == null) { c.Errors.Add("zipmod not found: " + path); continue; }
                if (cached.TryGetValue(path, out var z) && z.Stamp == stamp) { z.Guid = guid; c.byPath[path] = z; continue; }
                c.Scanned++;
                z = new ZipMakerInfo { Path = path, Stamp = stamp, Guid = guid };
                try { ReadManifest(path, z, c.Errors); }
                catch (Exception ex) { c.Unreadable++; c.Errors.Add($"manifest unreadable {path}: {ex.Message}"); }
                c.byPath[path] = z;
            }
            if (cacheFile != null && (c.Scanned > 0 || cached.Count != c.byPath.Count))
            {
                try { c.WriteCache(cacheFile); }
                catch (Exception ex) { c.Errors.Add($"cache not written {cacheFile}: {ex.Message}"); }
            }
            c.LoadMs = sw.Elapsed.TotalMilliseconds;
            return c;
        }

        static Dictionary<string, ZipMakerInfo> ReadCache(string file)
        {
            var d = new Dictionary<string, ZipMakerInfo>(StringComparer.OrdinalIgnoreCase);
            if (file == null || !File.Exists(file)) return d;
            ZipMakerInfo cur = null;
            bool first = true;
            foreach (var line in File.ReadLines(file))
            {
                if (first) { first = false; if (line != Header) return d; continue; }
                var t = line.Split('\t');
                if (t[0] == "Z" && t.Length >= 4) { cur = new ZipMakerInfo { Path = t[1], Stamp = t[2], Guid = t[3] }; d[cur.Path] = cur; }
                else if (cur == null) continue;
                else if (t[0] == "F" && t.Length >= 4)
                    cur.FaceSkins.Add(new FaceSkinEntry { SkinId = Int(t[1]), HeadId = Int(t[2]), HeadGuid = t[3] });
                else if (t[0] == "P" && t.Length >= 4 + HeadPresets.Elements.Length)
                {
                    var p = new HeadPresetEntry { Preset = t[1], HeadId = Int(t[2]), HeadGuid = Blank(t[3]), Zipmod = cur };
                    for (int i = 0; i < HeadPresets.Elements.Length; i++) p.Guids[i] = Blank(t[4 + i]);
                    cur.HeadPresets.Add(p);
                }
            }
            return d;
        }

        void WriteCache(string file)
        {
            var lines = new List<string> { Header };
            foreach (var z in byPath.Values.OrderBy(z => z.Path, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add($"Z\t{z.Path}\t{z.Stamp}\t{z.Guid}");
                foreach (var f in z.FaceSkins) lines.Add($"F\t{N(f.SkinId)}\t{N(f.HeadId)}\t{Clean(f.HeadGuid)}");
                foreach (var p in z.HeadPresets)
                    lines.Add($"P\t{Clean(p.Preset)}\t{N(p.HeadId)}\t{Clean(p.HeadGuid)}\t" + string.Join("\t", p.Guids.Select(Clean)));
            }
            var dir = System.IO.Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = file + ".tmp";
            File.WriteAllLines(tmp, lines);
            if (File.Exists(file)) File.Delete(file);
            File.Move(tmp, file);
        }

        /// <summary>Reads the faceSkinInfo / headPresetInfo elements of one zipmod's manifest.xml into <paramref name="z"/>.</summary>
        public static void ReadManifest(string zipPath, ZipMakerInfo z, List<string> errors)
        {
            var zi = new ZipIndex(zipPath);
            var text = zi.ReadText("manifest.xml");
            if (text == null) return;
            ParseManifest(text, z, errors, zipPath);
        }

        /// <summary>Parses manifest text (Sideloader's reading rules, header comment) into <paramref name="z"/>.</summary>
        public static void ParseManifest(string manifestXml, ZipMakerInfo z, List<string> errors, string label = null)
        {
            var doc = new XmlDocument();
            doc.LoadXml(manifestXml);
            var root = doc.DocumentElement;
            if (root == null) return;
            foreach (XmlNode n in root.ChildNodes)
            {
                if (!(n is XmlElement e)) continue;
                if (e.Name == "faceSkinInfo")
                {
                    string skin = e.GetAttributeNode("skinID")?.Value, head = e.GetAttributeNode("headID")?.Value, guid = e.GetAttributeNode("headGUID")?.Value;
                    if (!TryInt(skin, out int s) || !TryInt(head, out int h) || string.IsNullOrWhiteSpace(guid))
                    {
                        errors?.Add($"faceSkinInfo line skipped {label ?? z.Path}: skinID / headID must be integers and headGUID set");
                        continue;
                    }
                    z.FaceSkins.Add(new FaceSkinEntry { SkinId = s, HeadId = h, HeadGuid = guid.Trim() });
                }
                else if (e.Name == "headPresetInfo")
                {
                    string preset = e.GetAttributeNode("preset")?.Value;
                    if (string.IsNullOrWhiteSpace(preset) || !TryInt(Child(e, "headID"), out int hid))
                    {
                        errors?.Add($"headPresetInfo skipped {label ?? z.Path}: preset and an integer headID are required");
                        continue;
                    }
                    var p = new HeadPresetEntry { Preset = preset, HeadId = hid, HeadGuid = Blank(Child(e, "headGUID")?.Trim()), Zipmod = z };
                    for (int i = 0; i < HeadPresets.Elements.Length; i++) p.Guids[i] = Blank(Child(e, HeadPresets.Elements[i])?.Trim());
                    z.HeadPresets.Add(p);
                }
            }
        }

        // The text of the first child element named `name` (XElement.Element(name).Value), or null.
        static string Child(XmlElement e, string name)
        {
            foreach (XmlNode c in e.ChildNodes)
                if (c is XmlElement ce && ce.Name == name) return ce.InnerText;
            return null;
        }

        static bool TryInt(string s, out int v) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        static int Int(string s) => TryInt(s, out int v) ? v : 0;
        static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
        static string Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
        static string Clean(string s) => s == null ? "" : s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}
