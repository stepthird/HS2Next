// Field paths: the scene.json member path of a value inside a node
// ("chara.face.eyesOpen", "item.colors[2].pattern.uv", "chara.fk.bones[\"cf_J_Head\"]") or inside the scene
// object ("effects.bloom.intensity"). Grammar:
//   path = segment *( "." segment ); segment = name *( index / key );
//   name = ALPHA *( ALPHA / DIGIT ); index = "[" 1*DIGIT "]"; key = "[" json-string "]"
// A parsed path is immutable and has one canonical text (indices in decimal, keys as JSON string literals);
// events carry that text, and prefix tests on canonical texts are exact at step boundaries.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HS2Next.Scenes
{
    public enum FieldStepKind { Name, Index, Key }

    /// <summary>One step of a path: a member name, an array/list index or a map key.</summary>
    public readonly struct FieldStep
    {
        public readonly FieldStepKind Kind;
        /// <summary>The member name (as written in the JSON) or the map key.</summary>
        public readonly string Name;
        public readonly int Index;

        FieldStep(FieldStepKind kind, string name, int index) { Kind = kind; Name = name; Index = index; }

        public static FieldStep Member(string name) => new FieldStep(FieldStepKind.Name, name, -1);
        public static FieldStep At(int index) => new FieldStep(FieldStepKind.Index, null, index);
        public static FieldStep Key(string key) => new FieldStep(FieldStepKind.Key, key, -1);

        public override string ToString()
        {
            switch (Kind)
            {
                case FieldStepKind.Name: return Name;
                case FieldStepKind.Index: return "[" + Index.ToString(CultureInfo.InvariantCulture) + "]";
                default: return "[" + FieldPath.Quote(Name) + "]";
            }
        }
    }

    public sealed class FieldPath : IEquatable<FieldPath>
    {
        const int CacheLimit = 4096;
        static readonly Dictionary<string, FieldPath> cache = new Dictionary<string, FieldPath>(StringComparer.Ordinal);
        static readonly object cacheLock = new object();

        readonly FieldStep[] steps;

        FieldPath(FieldStep[] steps)
        {
            this.steps = steps;
            Text = Format(steps);
        }

        /// <summary>The canonical text.</summary>
        public string Text { get; }
        public IReadOnlyList<FieldStep> Steps => steps;
        public int Count => steps.Length;
        public FieldStep this[int i] => steps[i];
        /// <summary>The first member name ("chara", "transform", "effects", "title", …).</summary>
        public string Root => steps[0].Name;

        /// <summary>Parses a path; <see cref="FormatException"/> with the reason on bad text.</summary>
        public static FieldPath Parse(string text) =>
            TryParse(text, out var p, out string error) ? p : throw new FormatException("bad field path \"" + text + "\": " + error);

        public static bool TryParse(string text, out FieldPath path, out string error)
        {
            path = null;
            error = null;
            if (text == null) { error = "null"; return false; }
            lock (cacheLock)
                if (cache.TryGetValue(text, out path)) return true;
            var steps = new List<FieldStep>();
            int i = 0, n = text.Length;
            if (n == 0) { error = "empty"; return false; }
            while (true)
            {
                // name
                int start = i;
                if (i >= n || !IsAlpha(text[i])) { error = "a member name must start with a letter at " + i; return false; }
                i++;
                while (i < n && (IsAlpha(text[i]) || (text[i] >= '0' && text[i] <= '9'))) i++;
                steps.Add(FieldStep.Member(text.Substring(start, i - start)));
                // selectors
                while (i < n && text[i] == '[')
                {
                    i++;
                    if (i < n && text[i] == '"')
                    {
                        if (!ReadString(text, ref i, out string key, out error)) return false;
                        steps.Add(FieldStep.Key(key));
                    }
                    else
                    {
                        int ds = i;
                        while (i < n && text[i] >= '0' && text[i] <= '9') i++;
                        if (i == ds) { error = "expected an index or a quoted key at " + ds; return false; }
                        if (!int.TryParse(text.Substring(ds, i - ds), NumberStyles.None, CultureInfo.InvariantCulture, out int idx))
                        { error = "index out of range at " + ds; return false; }
                        steps.Add(FieldStep.At(idx));
                    }
                    if (i >= n || text[i] != ']') { error = "expected ']' at " + i; return false; }
                    i++;
                }
                if (i == n) break;
                if (text[i] != '.') { error = "unexpected '" + text[i] + "' at " + i; return false; }
                i++;
            }
            path = new FieldPath(steps.ToArray());
            lock (cacheLock)
            {
                if (cache.Count >= CacheLimit) cache.Clear();
                cache[text] = path;
                if (path.Text != text) cache[path.Text] = path;
            }
            return true;
        }

        /// <summary>A path from steps (the first must be a name).</summary>
        public static FieldPath FromSteps(IEnumerable<FieldStep> steps)
        {
            var a = new List<FieldStep>(steps).ToArray();
            if (a.Length == 0 || a[0].Kind != FieldStepKind.Name) throw new ArgumentException("a path starts with a member name");
            return Parse(Format(a));
        }

        /// <summary>This path plus a member name.</summary>
        public FieldPath Member(string name) => Parse(Text + "." + name);
        /// <summary>This path plus an index.</summary>
        public FieldPath At(int index) => Parse(Text + "[" + index.ToString(CultureInfo.InvariantCulture) + "]");
        /// <summary>This path plus a map key.</summary>
        public FieldPath Key(string key) => Parse(Text + "[" + Quote(key) + "]");

        /// <summary>The path without its last step, or null for a one-step path.</summary>
        public FieldPath Parent
        {
            get
            {
                if (steps.Length <= 1) return null;
                var a = new FieldStep[steps.Length - 1];
                Array.Copy(steps, a, a.Length);
                return Parse(Format(a));
            }
        }

        /// <summary>True when this path equals <paramref name="other"/> or names an object that contains it.</summary>
        public bool IsPrefixOf(FieldPath other) => other != null && IsPrefixOf(Text, other.Text);

        /// <summary>True when one path is a prefix of the other (a write to one may change the other).</summary>
        public bool Overlaps(FieldPath other) => other != null && Overlaps(Text, other.Text);

        /// <summary>Prefix test on canonical texts at step boundaries ("chara.face" is a prefix of "chara.face.eyes" and of
        /// "chara.face", not of "chara.faces"). A null or empty prefix is a prefix of everything.</summary>
        public static bool IsPrefixOf(string prefix, string path)
        {
            if (string.IsNullOrEmpty(prefix)) return true;
            if (path == null || path.Length < prefix.Length || !path.StartsWith(prefix, StringComparison.Ordinal)) return false;
            if (path.Length == prefix.Length) return true;
            char c = path[prefix.Length];
            return c == '.' || c == '[';
        }

        public static bool Overlaps(string a, string b) => IsPrefixOf(a, b) || IsPrefixOf(b, a);

        /// <summary>The canonical text of a path text, or null when it does not parse.</summary>
        public static string Canonical(string text) => TryParse(text, out var p, out _) ? p.Text : null;

        /// <summary>A key as a JSON string literal (the canonical key form).</summary>
        public static string Quote(string key)
        {
            var sb = new StringBuilder(key.Length + 2);
            sb.Append('"');
            foreach (char c in key)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        static bool IsAlpha(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        // A JSON string literal starting at text[i] == '"'; on success i is after the closing quote.
        static bool ReadString(string text, ref int i, out string value, out string error)
        {
            value = null;
            error = null;
            var sb = new StringBuilder();
            int n = text.Length;
            i++;
            while (i < n)
            {
                char c = text[i++];
                if (c == '"') { value = sb.ToString(); return true; }
                if (c < 0x20) { error = "control character in a key at " + (i - 1); return false; }
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= n) break;
                char e = text[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > n || !int.TryParse(text.Substring(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int u))
                        { error = "bad \\u escape at " + (i - 2); return false; }
                        sb.Append((char)u);
                        i += 4;
                        break;
                    default: error = "bad escape at " + (i - 2); return false;
                }
            }
            error = "unterminated key";
            return false;
        }

        static string Format(FieldStep[] steps)
        {
            var sb = new StringBuilder();
            for (int k = 0; k < steps.Length; k++)
            {
                if (steps[k].Kind == FieldStepKind.Name && k > 0) sb.Append('.');
                sb.Append(steps[k].ToString());
            }
            return sb.ToString();
        }

        public bool Equals(FieldPath other) => other != null && other.Text == Text;
        public override bool Equals(object obj) => obj is FieldPath p && Equals(p);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Text);
        public override string ToString() => Text;
    }
}
