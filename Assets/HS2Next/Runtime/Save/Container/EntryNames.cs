// Entry-name rules of the container: the conforming
// grammar, the safety check readers apply before touching any entry, the escaping function E for
// imported plugin data ids, and the core-area test that separates core entries from foreign ones.
using System;
using System.Text;

namespace HS2Next.Save
{
    public static class EntryNames
    {
        public const int MaxBytes = 1024;
        public const string Manifest = "manifest.json";
        public const string CharaDoc = "chara.json";
        public const string CoordDoc = "coord.json";
        public const string SceneDoc = "scene.json";
        public const string ResPrefix = "res/";
        public const string ExtDir = "ext/";

        static bool IsGrammarChar(char c) =>
            (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-' || c == '%';

        /// <summary>The grammar: ASCII segments of <c>[A-Za-z0-9._%-]</c> joined by '/', no "." or ".." segment, at most 1,024 bytes.</summary>
        public static bool IsConforming(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > MaxBytes) return false;
            int segStart = 0;
            for (int i = 0; i <= name.Length; i++)
            {
                if (i == name.Length || name[i] == '/')
                {
                    int len = i - segStart;
                    if (len == 0) return false;
                    if (len == 1 && name[segStart] == '.') return false;
                    if (len == 2 && name[segStart] == '.' && name[segStart + 1] == '.') return false;
                    segStart = i + 1;
                }
                else if (!IsGrammarChar(name[i])) return false;
            }
            return true;
        }

        /// <summary>
        /// The safety check (<c>UnsafeEntryName</c>): null if safe, else the reason. Unsafe: empty, absolute
        /// (leading '/'), backslash, ':' (drive letters), "." or ".." segment, empty segment, NUL or control
        /// characters. A trailing '/' (directory entry) is checked without it.
        /// </summary>
        public static string Unsafe(string name)
        {
            if (string.IsNullOrEmpty(name)) return "empty name";
            if (name[0] == '/') return "absolute name";
            string n = name.EndsWith("/", StringComparison.Ordinal) ? name.Substring(0, name.Length - 1) : name;
            if (n.Length == 0) return "empty name";
            foreach (char c in n)
            {
                if (c == '\\') return "backslash";
                if (c == ':') return "':' (drive letter or stream)";
                if (c < 0x20 || c == 0x7F) return "control character";
            }
            foreach (var seg in n.Split('/'))
            {
                if (seg.Length == 0) return "empty segment";
                if (seg == "." || seg == "..") return "'" + seg + "' segment";
            }
            return null;
        }

        /// <summary>True for directory entries (name ends with '/'); readers ignore them.</summary>
        public static bool IsDirectory(string name) => name.EndsWith("/", StringComparison.Ordinal);

        /// <summary>
        /// Escaping function E: the UTF-8 bytes of <paramref name="s"/>, keeping <c>[A-Za-z0-9._-]</c> and
        /// writing every other byte as '%' plus two uppercase hex digits. "." and ".." (which the grammar forbids
        /// as segments) are escaped completely.
        /// </summary>
        public static string Escape(string s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            var bytes = Encoding.UTF8.GetBytes(s);
            var sb = new StringBuilder(bytes.Length + 8);
            bool forceAll = s == "." || s == ".." || s.Length == 0;
            foreach (byte b in bytes)
            {
                char c = (char)b;
                bool keep = !forceAll && ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-');
                if (keep) sb.Append(c);
                else sb.Append('%').Append("0123456789ABCDEF"[b >> 4]).Append("0123456789ABCDEF"[b & 15]);
            }
            return sb.ToString();
        }

        /// <summary>Inverse of <see cref="Escape"/>; null when the text is not a valid escape.</summary>
        public static string Unescape(string e)
        {
            if (e == null) return null;
            var bytes = new System.Collections.Generic.List<byte>(e.Length);
            for (int i = 0; i < e.Length; i++)
            {
                char c = e[i];
                if (c == '%')
                {
                    if (i + 2 >= e.Length) return null;
                    int hi = HexVal(e[i + 1]), lo = HexVal(e[i + 2]);
                    if (hi < 0 || lo < 0) return null;
                    bytes.Add((byte)(hi * 16 + lo));
                    i += 2;
                }
                else if (c < 0x80) bytes.Add((byte)c);
                else return null;
            }
            try { return new UTF8Encoding(false, true).GetString(bytes.ToArray()); }
            catch (ArgumentException) { return null; }
        }

        static int HexVal(char c) =>
            c >= '0' && c <= '9' ? c - '0' : c >= 'A' && c <= 'F' ? c - 'A' + 10 : c >= 'a' && c <= 'f' ? c - 'a' + 10 : -1;

        /// <summary>Ordinal byte comparison of entry names. Names are ASCII after validation, so char order equals byte order.</summary>
        public static int CompareOrdinal(string a, string b) => string.CompareOrdinal(a, b);
    }
}
