// Localised lookup: L10n.Get(key) → the current language's text → en → the key itself
// (each missing key reported once through Missing, which package A turns into the finding `ui.l10nMissing`). Language
// switch at run time (LanguageChanged; A dirties every LocBinding and switches the font chain). Default language en; the
// dev machine sets ja through settings / options (OptionsDocument `general.language`). Unity-free: package A loads the
// TextAsset text and calls L10n.Load.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace HS2Next.UI.Core
{
    /// <summary>A lookup over one table and one current language (L10n is the app-wide instance).</summary>
    public sealed class Localizer
    {
        L10nTable table = L10nTable.Parse("key\ten\n");
        string language = L10nTable.En;
        readonly HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>A key without a cell in the current language or en, or an unknown key (key, language); once per key and language.</summary>
        public event Action<string, string> Missing;
        public event Action<string> LanguageChanged;
        /// <summary>The handlers of <see cref="LanguageChanged"/> (tests: a list dropped from the panel leaves none behind).</summary>
        public int LanguageListeners => LanguageChanged?.GetInvocationList().Length ?? 0;

        public L10nTable Table => table;
        public string Language => language;

        public void Load(L10nTable t)
        {
            table = t ?? throw new ArgumentNullException(nameof(t));
            reported.Clear();
            LanguageChanged?.Invoke(language);
        }

        /// <summary>Switches the language (unknown codes fall back to en; returns the language set).</summary>
        public string SetLanguage(string lang)
        {
            string l = Normalize(lang);
            if (!table.HasLanguage(l) && table.Count > 0) l = L10nTable.En;
            if (l == language) return l;
            language = l;
            LanguageChanged?.Invoke(l);
            return l;
        }

        /// <summary>"ja-JP" → "ja", "zh-CN" / "zh-SG" / "zh" → "zh-Hans", "en-US" → "en"; else as given.</summary>
        public static string Normalize(string lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return L10nTable.En;
            string l = lang.Trim().Replace('_', '-');
            string low = l.ToLowerInvariant();
            if (low == "ja" || low.StartsWith("ja-", StringComparison.Ordinal)) return L10nTable.Ja;
            if (low == "zh" || low == "zh-hans" || low == "zh-cn" || low == "zh-sg" || low.StartsWith("zh-hans-", StringComparison.Ordinal)) return L10nTable.ZhHans;
            if (low == "en" || low.StartsWith("en-", StringComparison.Ordinal)) return L10nTable.En;
            return l;
        }

        public string Get(string key)
        {
            if (key == null) return "";
            string v = table.Cell(key, language);
            if (v != null) return v;
            Report(key);
            return table.Cell(key, L10nTable.En) ?? key;
        }

        /// <summary>The text for a specific language (no reporting); en, then the key, as fallbacks.</summary>
        public string GetIn(string key, string lang) => table.Cell(key, Normalize(lang)) ?? table.Cell(key, L10nTable.En) ?? key;

        public bool TryGet(string key, out string text)
        {
            text = key == null ? null : table.Cell(key, language) ?? table.Cell(key, L10nTable.En);
            return text != null;
        }

        /// <summary>Get + string.Format with the invariant culture (a bad format string returns the unformatted text).</summary>
        public string Format(string key, params object[] args)
        {
            string f = Get(key);
            if (args == null || args.Length == 0) return f;
            try { return string.Format(CultureInfo.InvariantCulture, f, args); }
            catch (FormatException) { return f; }
        }

        void Report(string key)
        {
            if (Missing == null) return;
            if (reported.Add(language + "\u0001" + key)) Missing(key, language);
        }
    }

    /// <summary>The app-wide localiser.</summary>
    public static class L10n
    {
        public static readonly Localizer Default = new Localizer();

        public static string Language => Default.Language;
        public static L10nTable Table => Default.Table;

        public static void Load(L10nTable t) => Default.Load(t);
        public static void LoadText(string tsv) => Default.Load(L10nTable.Parse(tsv));
        public static string SetLanguage(string lang) => Default.SetLanguage(lang);
        public static string Get(string key) => Default.Get(key);
        public static string Format(string key, params object[] args) => Default.Format(key, args);
        public static bool TryGet(string key, out string text) => Default.TryGet(key, out text);
    }
}
