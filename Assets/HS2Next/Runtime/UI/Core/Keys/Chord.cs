// Chords: modifiers (Ctrl, Shift, Alt) + a KeyCode name, optionally followed by a second
// stroke ("Ctrl+K Ctrl+C"). Text form: strokes separated by one space, each stroke "Ctrl+Shift+Alt+Key" in that modifier
// order with the KeyCode name (KeyNames). A stroke may be modifiers only ("Ctrl", "Shift") for Modifier-kind actions
// (drag modifiers). Parse accepts aliases and any modifier order; Format writes the canonical text, so Parse ∘ Format is
// the identity on canonical text.
using System;
using System.Collections.Generic;
using System.Text;

namespace HS2Next.UI.Core
{
    [Flags]
    public enum KeyMods { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }

    /// <summary>One key press: modifiers + key (null key = modifiers only).</summary>
    public readonly struct KeyStroke : IEquatable<KeyStroke>
    {
        public readonly KeyMods Mods;
        /// <summary>KeyCode name (KeyNames.All) or null for a modifier-only stroke.</summary>
        public readonly string Key;

        public KeyStroke(KeyMods mods, string key)
        {
            Mods = mods;
            Key = string.IsNullOrEmpty(key) ? null : key;
        }

        public bool IsModifierOnly => Key == null;
        public bool IsEmpty => Key == null && Mods == KeyMods.None;

        public bool Equals(KeyStroke o) => Mods == o.Mods && string.Equals(Key, o.Key, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is KeyStroke s && Equals(s);
        public override int GetHashCode() => ((int)Mods * 397) ^ (Key == null ? 0 : StringComparer.Ordinal.GetHashCode(Key));
        public static bool operator ==(KeyStroke a, KeyStroke b) => a.Equals(b);
        public static bool operator !=(KeyStroke a, KeyStroke b) => !a.Equals(b);

        public override string ToString() => Chord.FormatStroke(this, false);
    }

    /// <summary>A binding: one stroke or two (the second pressed after releasing the first).</summary>
    public readonly struct Chord : IEquatable<Chord>
    {
        public readonly KeyStroke First;
        public readonly KeyStroke Second;

        public Chord(KeyStroke first) { First = first; Second = default; }
        public Chord(KeyStroke first, KeyStroke second) { First = first; Second = second; }
        public Chord(KeyMods mods, string key) : this(new KeyStroke(mods, key)) { }

        public bool HasSecond => !Second.IsEmpty;
        public bool IsEmpty => First.IsEmpty;
        /// <summary>A modifier-only single stroke ("Ctrl"): usable only by Modifier-kind actions.</summary>
        public bool IsModifierOnly => !HasSecond && First.IsModifierOnly;

        public bool Equals(Chord o) => First.Equals(o.First) && Second.Equals(o.Second);
        public override bool Equals(object obj) => obj is Chord c && Equals(c);
        public override int GetHashCode() => First.GetHashCode() * 31 + Second.GetHashCode();
        public static bool operator ==(Chord a, Chord b) => a.Equals(b);
        public static bool operator !=(Chord a, Chord b) => !a.Equals(b);

        /// <summary>Canonical text ("Ctrl+Shift+Z", "Ctrl+K Ctrl+C").</summary>
        public override string ToString() => Format(this);

        // ---------------- parse / format ----------------

        public static Chord Parse(string text) =>
            TryParse(text, out var c, out string error) ? c : throw new FormatException("bad chord \"" + text + "\": " + error);

        public static bool TryParse(string text, out Chord chord, out string error)
        {
            chord = default;
            error = null;
            if (text == null) { error = "null"; return false; }
            var strokes = SplitStrokes(text.Trim());
            if (strokes.Count == 0) { error = "empty"; return false; }
            if (strokes.Count > 2) { error = "at most two strokes"; return false; }
            if (!TryParseStroke(strokes[0], out var a, out error)) return false;
            if (strokes.Count == 1) { chord = new Chord(a); return true; }
            if (!TryParseStroke(strokes[1], out var b, out error)) return false;
            if (a.IsModifierOnly || b.IsModifierOnly) { error = "a two-stroke chord needs a key in each stroke"; return false; }
            chord = new Chord(a, b);
            return true;
        }

        // Strokes are separated by whitespace; the aliases written with a space ("Wheel Up", "Num 5") are joined first.
        static readonly System.Text.RegularExpressions.Regex spacedAlias = new System.Text.RegularExpressions.Regex(
            @"\b(Wheel|Num|Numpad|Page)\s+(Up|Down|Dn|[0-9.*/+-]|Enter)(?![A-Za-z0-9])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        static List<string> SplitStrokes(string s)
        {
            s = spacedAlias.Replace(s, m => m.Groups[1].Value + m.Groups[2].Value);
            var l = new List<string>();
            foreach (var p in s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)) l.Add(p);
            return l;
        }

        public static bool TryParseStroke(string text, out KeyStroke stroke, out string error)
        {
            stroke = default;
            error = null;
            if (string.IsNullOrWhiteSpace(text)) { error = "empty stroke"; return false; }
            var parts = new List<string>();
            int start = 0;
            string s = text.Trim();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '+') continue;
                if (i == s.Length - 1 && i == start) break;        // trailing "+" alone = the Plus key
                parts.Add(s.Substring(start, i - start));
                start = i + 1;
            }
            parts.Add(s.Substring(start));
            var mods = KeyMods.None;
            string key = null;
            for (int i = 0; i < parts.Count; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) { error = "empty part in \"" + text + "\""; return false; }
                var m = ModOf(p);
                if (m != KeyMods.None)
                {
                    if ((mods & m) != 0) { error = "modifier repeated: " + p; return false; }
                    if (key != null) { error = "modifier after the key: " + p; return false; }
                    mods |= m;
                    continue;
                }
                if (key != null) { error = "two keys in one stroke: " + key + ", " + p; return false; }
                string c = p == "+" ? "Plus" : KeyNames.Canonical(p);
                if (c == null) { error = "unknown key \"" + p + "\""; return false; }
                if (KeyNames.ModifierKeys.Contains(c)) { error = "a modifier key is not a chord key: " + c; return false; }
                key = c;
            }
            if (key == null && mods == KeyMods.None) { error = "empty stroke"; return false; }
            stroke = new KeyStroke(mods, key);
            return true;
        }

        static KeyMods ModOf(string p)
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl": case "control": case "ctl": return KeyMods.Ctrl;
                case "shift": return KeyMods.Shift;
                case "alt": case "option": return KeyMods.Alt;
                default: return KeyMods.None;
            }
        }

        public static string Format(Chord c) =>
            c.IsEmpty ? "" : c.HasSecond ? FormatStroke(c.First, false) + " " + FormatStroke(c.Second, false) : FormatStroke(c.First, false);

        /// <summary>Label for menus, tooltips and the key overlay ("Ctrl+↑", "Num 5", "Ctrl+Wheel↑").</summary>
        public string ToDisplay() => ToDisplay(KeyboardLayout.Us);

        /// <summary>Label with the keycaps of a keyboard layout (JIS: ";" for Equals, ":" for Semicolon, …).</summary>
        public string ToDisplay(KeyboardLayout layout) =>
            IsEmpty ? "" : HasSecond ? FormatStroke(First, true, layout) + ", " + FormatStroke(Second, true, layout) : FormatStroke(First, true, layout);

        internal static string FormatStroke(KeyStroke s, bool display, KeyboardLayout layout = KeyboardLayout.Us)
        {
            var sb = new StringBuilder();
            void M(string n)
            {
                if (sb.Length > 0) sb.Append('+');
                sb.Append(n);
            }
            if ((s.Mods & KeyMods.Ctrl) != 0) M("Ctrl");
            if ((s.Mods & KeyMods.Shift) != 0) M("Shift");
            if ((s.Mods & KeyMods.Alt) != 0) M("Alt");
            if (s.Key != null) M(display ? KeyNames.Display(s.Key, layout) : s.Key);
            return sb.ToString();
        }

        /// <summary>Modifiers of a KeyCode name of a modifier key (LeftShift → Shift …), else None.</summary>
        public static KeyMods ModOfKey(string keyCodeName)
        {
            switch (keyCodeName)
            {
                case "LeftShift": case "RightShift": return KeyMods.Shift;
                case "LeftControl": case "RightControl": return KeyMods.Ctrl;
                case "LeftAlt": case "RightAlt": case "AltGr": return KeyMods.Alt;
                default: return KeyMods.None;
            }
        }
    }
}
