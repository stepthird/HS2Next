// Text forms shared by the canonical writer and the document reader:
// float32 canonical text T(x), ids, times, `ref` text and `res` names
//, plus the double rule used when an unknown member (a raw token) is written back.
using System;
using System.Globalization;
using HS2Next.Content;

namespace HS2Next.Save
{
    public static class JsonText
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>
        /// Canonical float32 text T(x): zero of either sign → "0.0"; else the invariant "R"
        /// text with ".0" appended when it has neither '.' nor 'E'. NaN/±Infinity throw
        /// <see cref="SaveErrorCode.NonFinite"/> (location filled in by the caller).
        /// </summary>
        public static string F32(float x)
        {
            if (float.IsNaN(x) || float.IsInfinity(x))
                throw new SaveFormatException(SaveErrorCode.NonFinite, "non-finite float " + x.ToString(Inv));
            if (x == 0f) return "0.0";
            string s = x.ToString("R", Inv);
            if (s.IndexOf('.') < 0 && s.IndexOf('E') < 0) s += ".0";
            return s;
        }

        /// <summary>True for NaN and ±Infinity.</summary>
        public static bool IsNonFinite(float x) => float.IsNaN(x) || float.IsInfinity(x);

        /// <summary>
        /// Text of a double from a raw token (unknown members, ext JSON). A value whose float32 text T(f)
        /// reads back as the same double is written as T(f), so float32 text written by any canonical
        /// writer comes back byte-identical; anything else uses the invariant "R" text (".0" appended
        /// when it looks like an integer). Zero → "0.0".
        /// </summary>
        public static string F64(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d))
                throw new SaveFormatException(SaveErrorCode.NonFinite, "non-finite number " + d.ToString(Inv));
            if (d == 0d) return "0.0";
            float f = (float)d;
            if (!float.IsInfinity(f))
            {
                string t = F32(f);
                if (double.Parse(t, NumberStyles.Float, Inv) == d) return t;
            }
            string s = d.ToString("R", Inv);
            if (s.IndexOf('.') < 0 && s.IndexOf('E') < 0) s += ".0";
            return s;
        }

        // ---------- ids ----------

        /// <summary>A new id: <c>Guid.ToString("N")</c>, 32 lowercase hex digits.</summary>
        public static string NewId() => Guid.NewGuid().ToString("N");

        /// <summary>
        /// A deterministic id derived from <paramref name="name"/>: the lowercase hex of the first 16 bytes of the SHA-256
        /// of its UTF-8 bytes (32 lowercase hex digits, the form of <see cref="NewId"/>). For ids that a reader or the
        /// importer must create identically on every run.
        /// </summary>
        public static string DerivedId(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var h = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(name));
                Array.Resize(ref h, 16);
                return Hex(h);
            }
        }

        /// <summary>True for exactly 32 lowercase hex digits.</summary>
        public static bool IsId(string s) => IsHex(s, 32, false);

        /// <summary>True for 32 hex digits of either case (readers accept uppercase and lowercase it).</summary>
        public static bool IsIdAnyCase(string s) => IsHex(s, 32, true);

        /// <summary>True for <paramref name="length"/> hex digits (lowercase only unless <paramref name="anyCase"/>).</summary>
        public static bool IsHex(string s, int length, bool anyCase)
        {
            if (s == null || s.Length != length) return false;
            foreach (char c in s)
            {
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (anyCase && c >= 'A' && c <= 'F');
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>Lowercase hex of bytes.</summary>
        public static string Hex(byte[] b)
        {
            var c = new char[b.Length * 2];
            const string digits = "0123456789abcdef";
            for (int i = 0; i < b.Length; i++)
            {
                c[2 * i] = digits[b[i] >> 4];
                c[2 * i + 1] = digits[b[i] & 15];
            }
            return new string(c);
        }

        // ---------- time ----------

        public const string TimeFormat = "yyyy-MM-ddTHH:mm:ssZ";

        /// <summary>UTC "yyyy-MM-ddTHH:mm:ssZ" (sub-second part dropped; local times are converted).</summary>
        public static string Time(DateTime t)
        {
            if (t.Kind == DateTimeKind.Local) t = t.ToUniversalTime();
            return t.ToString(TimeFormat, Inv);
        }

        /// <summary>Parses the exact time form; the result has <see cref="DateTimeKind.Utc"/>.</summary>
        public static bool TryParseTime(string s, out DateTime t) =>
            DateTime.TryParseExact(s, TimeFormat, Inv, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out t);

        /// <summary>A time truncated to whole seconds in UTC (what a save stores).</summary>
        public static DateTime TruncateToSeconds(DateTime t)
        {
            if (t.Kind == DateTimeKind.Local) t = t.ToUniversalTime();
            return new DateTime(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        }

        // ---------- ref ----------

        /// <summary>True if <paramref name="s"/> is a non-empty <c>&lt;guid&gt;#&lt;id&gt;</c> (parsed at the last '#').</summary>
        public static bool IsRef(string s) => !string.IsNullOrEmpty(s) && ContentId.TryParse(s, out var c) && !c.IsEmpty;

        /// <summary>Parses a non-empty ref; false for "" and malformed text.</summary>
        public static bool TryParseRef(string s, out ContentId id) => ContentId.TryParse(s, out id) && !id.IsEmpty;

        /// <summary>The GUID part of a ref text (before the last '#'), or null when malformed.</summary>
        public static string RefGuid(string s)
        {
            if (s == null) return null;
            int i = s.LastIndexOf(ContentId.Separator);
            return i > 0 ? s.Substring(0, i) : null;
        }

        /// <summary>Text of a reference; the empty reference has no text (null).</summary>
        public static string RefText(ContentId id) => id.IsEmpty ? null : id.ToString();

        // ---------- res ----------

        /// <summary>True for <c>res/&lt;64 lowercase hex&gt;.png|jpg|bin</c>.</summary>
        public static bool IsResName(string s)
        {
            if (s == null || s.Length != 72 || !s.StartsWith("res/", StringComparison.Ordinal) || s[68] != '.') return false;
            for (int i = 4; i < 68; i++)
            {
                char c = s[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            }
            string ext = s.Substring(69);
            return ext == "png" || ext == "jpg" || ext == "bin";
        }
    }
}
