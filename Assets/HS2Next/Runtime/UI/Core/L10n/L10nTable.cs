// The localisation table: Resources/UI/L10n/strings.tsv, UTF-8 (a BOM is skipped).
//
// Format
//   - Rows are separated by LF (CRLF accepted); cells by one TAB.
//   - Rows whose first cell starts with '#' are comments (the file's header comment, the `#<package>` block heads of the
//     shared append-only file); blank rows are skipped.
//   - The first non-comment row is the header: `key  en  ja  zh-Hans  note` (column order free; more language columns,
//     e.g. `zh-Hant`, may be added; `key` and `en` are required).
//   - Keys are dotted and stable (`toolbar.save`, `inspector.light.intensity`, `msg.save_failed`, `action.camera.reset`).
//   - Cell escapes: `\n` newline, `\t` tab, `\\` backslash. A cell wholly enclosed in double quotes is unquoted with ""
//     → " (spreadsheet exports); quotes elsewhere are literal.
//   - Placeholders `{0}`, `{1}` … (string.Format); every language of a key must use the same placeholder set.
//   - The note column flags unreviewed machine translations: `zh?` (zh-Hans), `ja?` (ja); free text after them is allowed.
//   - An empty cell = missing (lookup falls back to en, then to the key).
//   - A key defined twice: the first row wins, the duplicate is reported.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HS2Next.UI.Core
{
    public sealed class L10nTable
    {
        public const string En = "en", Ja = "ja", ZhHans = "zh-Hans";
        public static readonly IReadOnlyList<string> CoreLanguages = new[] { En, Ja, ZhHans };

        readonly Dictionary<string, string[]> rows = new Dictionary<string, string[]>(StringComparer.Ordinal);
        readonly Dictionary<string, string> notes = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, int> lineOf = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly List<string> keys = new List<string>();
        readonly List<string> languages = new List<string>();
        readonly Dictionary<string, int> column = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Parse problems (line numbers 1-based): duplicates, short rows, bad header.</summary>
        public readonly List<string> Problems = new List<string>();

        /// <summary>Language columns in file order (en first in the core file).</summary>
        public IReadOnlyList<string> Languages => languages;
        /// <summary>Keys in file order.</summary>
        public IReadOnlyList<string> Keys => keys;
        public int Count => keys.Count;

        public bool Has(string key) => key != null && rows.ContainsKey(key);
        public bool HasLanguage(string lang) => lang != null && column.ContainsKey(lang);

        /// <summary>The cell (null = missing / empty / unknown key or language).</summary>
        public string Cell(string key, string lang)
        {
            if (key == null || lang == null || !rows.TryGetValue(key, out var r) || !column.TryGetValue(lang, out int c)) return null;
            return c < r.Length && r[c].Length > 0 ? r[c] : null;
        }

        public string Note(string key) => key != null && notes.TryGetValue(key, out var n) ? n : "";
        public int LineOf(string key) => key != null && lineOf.TryGetValue(key, out int l) ? l : 0;

        /// <summary>Whether the language's cell of a key is a machine translation awaiting review (note `zh?` / `ja?`).</summary>
        public bool Unreviewed(string key, string lang)
        {
            string n = Note(key);
            string flag = lang == ZhHans ? "zh?" : lang == Ja ? "ja?" : lang + "?";
            return n.IndexOf(flag, StringComparison.Ordinal) >= 0;
        }

        public static L10nTable Parse(string text)
        {
            var t = new L10nTable();
            t.Load(text);
            return t;
        }

        void Load(string text)
        {
            if (text == null) { Problems.Add("no text"); return; }
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
            var lines = text.Split('\n');
            bool header = false;
            int noteCol = -1, keyCol = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.EndsWith("\r", StringComparison.Ordinal)) line = line.Substring(0, line.Length - 1);
                if (line.Trim().Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                var cells = line.Split('\t');
                if (!header)
                {
                    header = true;
                    for (int c = 0; c < cells.Length; c++)
                    {
                        string h = cells[c].Trim();
                        if (h == "key") keyCol = c;
                        else if (h == "note") noteCol = c;
                        else if (h.Length > 0)
                        {
                            if (column.ContainsKey(h)) { Problems.Add("line " + (i + 1) + ": column " + h + " twice"); continue; }
                            column[h] = c;
                            languages.Add(h);
                        }
                    }
                    if (keyCol < 0 || !column.ContainsKey(En)) { Problems.Add("line " + (i + 1) + ": header needs key and en columns"); return; }
                    continue;
                }
                string key = keyCol < cells.Length ? cells[keyCol].Trim() : "";
                if (key.Length == 0) { Problems.Add("line " + (i + 1) + ": empty key"); continue; }
                if (rows.ContainsKey(key)) { Problems.Add("line " + (i + 1) + ": duplicate key " + key + " (first at line " + lineOf[key] + ")"); continue; }
                var vals = new string[cells.Length];
                for (int c = 0; c < cells.Length; c++) vals[c] = c == keyCol ? key : Unescape(cells[c]);
                rows[key] = vals;
                lineOf[key] = i + 1;
                keys.Add(key);
                if (noteCol >= 0 && noteCol < cells.Length) notes[key] = cells[noteCol].Trim();
            }
            if (!header) Problems.Add("no header row");
        }

        /// <summary>Cell text → value (quotes, escapes).</summary>
        public static string Unescape(string cell)
        {
            if (cell == null) return "";
            string s = cell;
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') s = s.Substring(1, s.Length - 2).Replace("\"\"", "\"");
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\\' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    if (n == 'n') { sb.Append('\n'); i++; continue; }
                    if (n == 't') { sb.Append('\t'); i++; continue; }
                    if (n == '\\') { sb.Append('\\'); i++; continue; }
                }
                sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>Value → cell text (the inverse of <see cref="Unescape"/> for values without leading and trailing quotes).</summary>
        public static string Escape(string value) =>
            (value ?? "").Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r\n", "\\n").Replace("\n", "\\n");

        static readonly Regex placeholder = new Regex(@"\{(\d+)(?:[,:][^}]*)?\}", RegexOptions.CultureInvariant);

        /// <summary>The placeholder indices used by a text, sorted ("{0} of {1}" → 0, 1).</summary>
        public static SortedSet<int> Placeholders(string text)
        {
            var s = new SortedSet<int>();
            if (text == null) return s;
            foreach (Match m in placeholder.Matches(text)) s.Add(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            return s;
        }

        /// <summary>Keys whose cells disagree on their placeholder set (language cells that are present only).</summary>
        public List<string> PlaceholderMismatches()
        {
            var r = new List<string>();
            foreach (var k in keys)
            {
                var en = Placeholders(Cell(k, En));
                foreach (var lang in languages)
                {
                    string c = Cell(k, lang);
                    if (c == null || lang == En) continue;
                    if (!en.SetEquals(Placeholders(c))) { r.Add(k + " [" + lang + "]"); break; }
                }
            }
            return r;
        }

        /// <summary>Counts for the report: keys, missing en, and per language missing / unreviewed cells.</summary>
        public sealed class Coverage
        {
            public int Keys, MissingEn;
            public readonly SortedDictionary<string, int> Missing = new SortedDictionary<string, int>(StringComparer.Ordinal);
            public readonly SortedDictionary<string, int> Unreviewed = new SortedDictionary<string, int>(StringComparer.Ordinal);
            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.Append(Keys).Append(" keys, en missing ").Append(MissingEn);
                foreach (var kv in Missing)
                    sb.Append("; ").Append(kv.Key).Append(" missing ").Append(kv.Value).Append(", unreviewed ").Append(Unreviewed.TryGetValue(kv.Key, out int u) ? u : 0);
                return sb.ToString();
            }
        }

        public Coverage Report()
        {
            var c = new Coverage { Keys = keys.Count };
            foreach (var lang in languages)
                if (lang != En) { c.Missing[lang] = 0; c.Unreviewed[lang] = 0; }
            foreach (var k in keys)
            {
                if (Cell(k, En) == null) c.MissingEn++;
                foreach (var lang in languages)
                {
                    if (lang == En) continue;
                    if (Cell(k, lang) == null) c.Missing[lang]++;
                    else if (Unreviewed(k, lang)) c.Unreviewed[lang]++;
                }
            }
            return c;
        }
    }
}
