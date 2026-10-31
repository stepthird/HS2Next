// Sideloader-style Studio list CSVs inside zipmods: which zip
// entries are Studio lists, how their text is read (header rule, stop rule, no quoting) and how a
// CSV maps to a "virtual bundle". Parsing happens at zipmod scan time; the result is stored in
// the per-source scan cache, so a warm start never opens the zips again.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace HS2Next.Content.Studio
{
    /// <summary>One Studio list CSV of a zipmod, already split into rows (header rows first).</summary>
    public sealed class StudioCsvFile
    {
        /// <summary>Zip entry path, e.g. "abdata/studio/info/wayne_mp5/ItemList_01_1115_31.csv".</summary>
        public readonly string EntryPath;
        /// <summary>Header rows at the top of <see cref="Rows"/> by Sideloader's rule (1 or 2; 0 for an empty file).</summary>
        public readonly int HeaderCount;
        /// <summary>Header rows, then entries; every row is the line trimmed and split on every ','.</summary>
        public readonly List<string[]> Rows;

        public StudioCsvFile(string entryPath, int headerCount, List<string[]> rows)
        {
            EntryPath = entryPath; HeaderCount = headerCount; Rows = rows;
        }

        /// <summary>List (asset) name: the file name without extension.</summary>
        public string ListName
        {
            get
            {
                int s = EntryPath.LastIndexOf('/') + 1, d = EntryPath.LastIndexOf('.');
                return d > s ? EntryPath.Substring(s, d - s) : EntryPath.Substring(s);
            }
        }

        /// <summary>
        /// The Sideloader virtual bundle path: cut from the last '/', cut up to and including the first
        /// '/', append ".unity3d" (abdata/studio/info/x/y.csv → studio/info/x.unity3d).
        /// </summary>
        public string VirtualBundle
        {
            get
            {
                var p = EntryPath.Substring(0, Math.Max(0, EntryPath.LastIndexOf('/')));
                return p.Substring(p.IndexOf('/') + 1) + ".unity3d";
            }
        }
    }

    /// <summary>Reader for Sideloader Studio CSV text.</summary>
    public static class StudioCsv
    {
        /// <summary>True for zip entries that are Studio lists: start with "abdata/studio/info", end with ".csv" (case-insensitive).</summary>
        public static bool IsStudioListEntry(string entryName) =>
            entryName.StartsWith("abdata/studio/info", StringComparison.OrdinalIgnoreCase)
            && entryName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Order of Studio bundle paths and of zipmod GUIDs: ordinal
        /// on the lower-cased strings. Reproduces the vanilla order and, unlike .NET OrdinalIgnoreCase
        /// (which upper-cases, putting '[' and '_' after letters), keeps the culture order's
        /// "punctuation before letters" for folder names such as "[author]…" or "_maps…".
        /// </summary>
        public static readonly StringComparer LoadOrder = new LowerOrdinalComparer();

        sealed class LowerOrdinalComparer : StringComparer
        {
            public override int Compare(string x, string y) =>
                string.CompareOrdinal(x?.ToLowerInvariant(), y?.ToLowerInvariant());
            public override bool Equals(string x, string y) => Compare(x, y) == 0;
            public override int GetHashCode(string s) => s == null ? 0 : s.ToLowerInvariant().GetHashCode();
        }

        /// <summary>.NET int.TryParse semantics used throughout the list reader (white space and a sign allowed).</summary>
        public static bool TryInt(string s, out int v) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

        /// <summary>
        /// Splits decoded CSV text the way Sideloader does: lines by CRLF/LF/CR, each line
        /// trimmed and split on every ','; line 1 is a header; line 2 is a header unless its first
        /// field is an integer; from line 3 on, reading stops at the first line without ','.
        /// </summary>
        public static List<string[]> Split(string text, out int headerCount)
        {
            var rows = new List<string[]>();
            headerCount = 0;
            int lineNo = 0;
            foreach (var raw in Lines(text))
            {
                var line = raw.Trim();
                lineNo++;
                if (lineNo == 1) { rows.Add(line.Split(',')); headerCount = 1; continue; }
                if (lineNo == 2)
                {
                    var f = line.Split(',');
                    if (!TryInt(f[0], out _)) headerCount = 2;
                    rows.Add(f);
                    continue;
                }
                if (line.IndexOf(',') < 0) break;
                rows.Add(line.Split(','));
            }
            return rows;
        }

        /// <summary>TextReader.ReadLine semantics: CRLF, LF or CR end a line; no empty last line after a final terminator.</summary>
        public static IEnumerable<string> Lines(string s)
        {
            int start = 0, i = 0, n = s.Length;
            while (i < n)
            {
                char c = s[i];
                if (c == '\r' || c == '\n')
                {
                    yield return s.Substring(start, i - start);
                    if (c == '\r' && i + 1 < n && s[i + 1] == '\n') i++;
                    start = ++i;
                    continue;
                }
                i++;
            }
            if (start < n) yield return s.Substring(start);
        }

        // Cache encoding of one field: tabs (the cache separator) become U+001F.
        internal static string Escape(string f) => f.IndexOf('\t') < 0 ? f : f.Replace('\t', '\u001F');
        internal static string Unescape(string f) => f.IndexOf('\u001F') < 0 ? f : f.Replace('\u001F', '\t');
    }

    /// <summary>A `&lt;hs2-scene-dependency manifest="X"&gt;` declaration of a zipmod manifest.xml.</summary>
    public sealed class SceneDependencyDecl
    {
        /// <summary>Manifest name X; the zipmod ships abdata/X.unity3d, an AssetBundleManifest bundle.</summary>
        public string Manifest;
        /// <summary>Optional `&lt;dependency bundle=… asset=…/&gt;` children.</summary>
        public readonly List<(string bundle, string asset)> Dependencies = new List<(string, string)>();

        public override string ToString() => $"{Manifest} ({Dependencies.Count} dependency entries)";
    }
}
