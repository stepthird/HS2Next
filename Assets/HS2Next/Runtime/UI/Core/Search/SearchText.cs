// Normalised search: search keys are precomputed once per row — NFKC (full-width
// ASCII → half-width, half-width katakana → full-width, compatibility forms), lower case (invariant), katakana →
// hiragana — and a query is split at white space into tokens that must all occur (AND of substrings). A token holding '#'
// is a content ref "guid#id": its guid part must occur in the row's ref guid (prefix typing works) and a non-empty id part
// must equal the row's id exactly ("abc#12" does not match "#120"); rows without a ref fall back to the substring rule.
using System;
using System.Collections.Generic;
using System.Text;

namespace HS2Next.UI.Core
{
    public static class SearchText
    {
        /// <summary>The search key of a text (null → "").</summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string s;
            try { s = text.Normalize(NormalizationForm.FormKC); }
            catch (ArgumentException) { s = text; }   // ill-formed UTF-16 (lone surrogates): keep as is
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s)
            {
                char c = char.ToLowerInvariant(ch);
                if (c >= 'ァ' && c <= 'ヶ') c = (char)(c - 0x60);       // katakana ァ..ヶ → hiragana ぁ..ゖ
                else if (c == 'ヽ' || c == 'ヾ') c = (char)(c - 0x60);  // ヽヾ → ゝゞ
                else if (char.IsWhiteSpace(c)) c = ' ';
                sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>A parsed query.</summary>
    public sealed class SearchQuery
    {
        public static readonly SearchQuery Empty = new SearchQuery("", new string[0], new RefToken[0]);

        public struct RefToken
        {
            /// <summary>Normalised guid part (may be empty: "#12" matches id 12 in any guid).</summary>
            public string Guid;
            /// <summary>Id part (null when the token ends with '#').</summary>
            public string Id;
            /// <summary>The whole token for rows without a ref.</summary>
            public string Text;
        }

        public readonly string Text;
        /// <summary>Normalised plain tokens.</summary>
        public readonly IReadOnlyList<string> Tokens;
        public readonly IReadOnlyList<RefToken> Refs;

        public bool IsEmpty => Tokens.Count == 0 && Refs.Count == 0;

        SearchQuery(string text, string[] tokens, RefToken[] refs)
        {
            Text = text;
            Tokens = tokens;
            Refs = refs;
        }

        public static SearchQuery Parse(string query)
        {
            string n = SearchText.Normalize(query);
            if (n.Trim().Length == 0) return Empty;
            var plain = new List<string>();
            var refs = new List<RefToken>();
            foreach (var t in n.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int h = t.IndexOf('#');
                if (h < 0) { if (!plain.Contains(t)) plain.Add(t); continue; }
                string id = t.Substring(h + 1);
                refs.Add(new RefToken { Guid = t.Substring(0, h), Id = id.Length == 0 ? null : id, Text = t });
            }
            return new SearchQuery(query ?? "", plain.ToArray(), refs.ToArray());
        }

        /// <summary>Whether a row matches: <paramref name="key"/> = the row's normalised search key, <paramref name="refGuid"/> /
        /// <paramref name="refId"/> = its content ref parts, normalised (null when the row has no ref).</summary>
        public bool Matches(string key, string refGuid = null, string refId = null)
        {
            key = key ?? "";
            var tokens = Tokens;
            for (int i = 0; i < tokens.Count; i++)
                if (key.IndexOf(tokens[i], StringComparison.Ordinal) < 0) return false;
            var refs = Refs;
            for (int i = 0; i < refs.Count; i++)
            {
                var r = refs[i];
                if (refGuid == null)
                {
                    if (key.IndexOf(r.Text, StringComparison.Ordinal) < 0) return false;
                    continue;
                }
                if (r.Guid.Length > 0 && refGuid.IndexOf(r.Guid, StringComparison.Ordinal) < 0) return false;
                if (r.Id != null && !string.Equals(r.Id, refId, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        /// <summary>Splits a ref "guid#id" into normalised parts (false when it has no '#').</summary>
        public static bool SplitRef(string contentRef, out string guid, out string id)
        {
            guid = id = null;
            if (string.IsNullOrEmpty(contentRef)) return false;
            int h = contentRef.LastIndexOf('#');
            if (h < 0) return false;
            guid = SearchText.Normalize(contentRef.Substring(0, h));
            id = SearchText.Normalize(contentRef.Substring(h + 1));
            return true;
        }
    }
}
