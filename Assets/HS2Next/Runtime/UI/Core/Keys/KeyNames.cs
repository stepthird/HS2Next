// Key names of the keymaps: bindings are stored as Unity `KeyCode`
// names (the list below mirrors the KeyCode enum members that can be bound; no UnityEngine reference) plus the two
// HS2Next pseudo keys WheelUp / WheelDown for wheel chords (default preset: Ctrl+wheel = FOV) and JisBackslash (the JIS ¥ /
// ろ keys, S8). Names are legacy Input KeyCode semantics; UI Toolkit keyCodes go through FromUiToolkit. Parsing accepts common
// aliases ("Del", "Esc", "Enter", "Up", "↑", "PgUp", "Num5", digits, single punctuation characters); formatting always
// writes the KeyCode name. Display labels are separate (Chord.ToDisplay).
using System;
using System.Collections.Generic;

namespace HS2Next.UI.Core
{
    /// <summary>The physical keyboard layout the key overlay labels follow (A detects it, or the user picks it).</summary>
    public enum KeyboardLayout { Us, Jis }

    public static class KeyNames
    {
        public const string WheelUp = "WheelUp", WheelDown = "WheelDown";

        /// <summary>Every bindable key name (KeyCode names without modifiers, None and mouse buttons; plus the wheel).</summary>
        public static readonly IReadOnlyList<string> All = BuildAll();

        static readonly Dictionary<string, string> canonical = BuildCanonical();

        /// <summary>The modifier keys themselves (KeyCode names): never the key of a chord.</summary>
        public static readonly HashSet<string> ModifierKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "LeftShift", "RightShift", "LeftControl", "RightControl", "LeftAlt", "RightAlt", "AltGr",
            "LeftMeta", "RightMeta", "LeftCommand", "RightCommand", "LeftApple", "RightApple", "LeftWindows", "RightWindows",
        };

        static List<string> BuildAll()
        {
            var l = new List<string>
            {
                "Backspace", "Delete", "Tab", "Clear", "Return", "Pause", "Escape", "Space",
                "KeypadPeriod", "KeypadDivide", "KeypadMultiply", "KeypadMinus", "KeypadPlus", "KeypadEnter", "KeypadEquals",
                "UpArrow", "DownArrow", "RightArrow", "LeftArrow", "Insert", "Home", "End", "PageUp", "PageDown",
                "Exclaim", "DoubleQuote", "Hash", "Dollar", "Percent", "Ampersand", "Quote", "LeftParen", "RightParen",
                "Asterisk", "Plus", "Comma", "Minus", "Period", "Slash", "Colon", "Semicolon", "Less", "Equals", "Greater",
                "Question", "At", "LeftBracket", "Backslash", "RightBracket", "Caret", "Underscore", "BackQuote",
                "LeftCurlyBracket", "Pipe", "RightCurlyBracket", "Tilde",
                "Numlock", "CapsLock", "ScrollLock", "Help", "Print", "SysReq", "Break", "Menu",
                WheelUp, WheelDown, JisBackslash,
            };
            for (int i = 0; i <= 9; i++) l.Add("Keypad" + i);
            for (int i = 0; i <= 9; i++) l.Add("Alpha" + i);
            for (int i = 1; i <= 15; i++) l.Add("F" + i);
            for (char c = 'A'; c <= 'Z'; c++) l.Add(c.ToString());
            return l;
        }

        static Dictionary<string, string> BuildCanonical()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in All) d[n] = n;
            void Alias(string a, string n) => d[a] = n;
            Alias("Del", "Delete");
            Alias("Esc", "Escape");
            Alias("Enter", "Return");
            Alias("Ins", "Insert");
            Alias("BS", "Backspace");
            Alias("Up", "UpArrow");
            Alias("Down", "DownArrow");
            Alias("Left", "LeftArrow");
            Alias("Right", "RightArrow");
            Alias("↑", "UpArrow");
            Alias("↓", "DownArrow");
            Alias("←", "LeftArrow");
            Alias("→", "RightArrow");
            Alias("PgUp", "PageUp");
            Alias("PgDn", "PageDown");
            Alias("PageDn", "PageDown");
            Alias("Wheel Up", WheelUp);
            Alias("Wheel Down", WheelDown);
            Alias("¥", JisBackslash);
            Alias("Yen", JisBackslash);
            Alias("ろ", JisBackslash);
            for (int i = 0; i <= 9; i++)
            {
                Alias(i.ToString(), "Alpha" + i);
                Alias("Num" + i, "Keypad" + i);
                Alias("Numpad" + i, "Keypad" + i);
                Alias("Num " + i, "Keypad" + i);
            }
            Alias("Num.", "KeypadPeriod");
            Alias("Num/", "KeypadDivide");
            Alias("Num*", "KeypadMultiply");
            Alias("Num-", "KeypadMinus");
            Alias("NumEnter", "KeypadEnter");
            var punct = new Dictionary<string, string>
            {
                [";"] = "Semicolon", [":"] = "Colon", ["="] = "Equals", ["/"] = "Slash", ["\\"] = "Backslash", ["."] = "Period",
                [","] = "Comma", ["-"] = "Minus", ["["] = "LeftBracket", ["]"] = "RightBracket", ["'"] = "Quote", ["`"] = "BackQuote",
                ["@"] = "At", ["^"] = "Caret", ["_"] = "Underscore", ["!"] = "Exclaim", ["#"] = "Hash", ["$"] = "Dollar",
                ["%"] = "Percent", ["&"] = "Ampersand", ["*"] = "Asterisk", ["("] = "LeftParen", [")"] = "RightParen",
                ["<"] = "Less", [">"] = "Greater", ["?"] = "Question", ["|"] = "Pipe", ["~"] = "Tilde", ["{"] = "LeftCurlyBracket",
                ["}"] = "RightCurlyBracket", ["\""] = "DoubleQuote",
            };
            foreach (var kv in punct) Alias(kv.Key, kv.Value);
            return d;
        }

        /// <summary>The KeyCode name for a name or alias (case-insensitive), or null.</summary>
        public static string Canonical(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string n = name.Trim();
            if (n.Length == 0) return null;
            return canonical.TryGetValue(n, out var c) ? c : null;
        }

        public static bool IsKnown(string name) => name != null && canonical.TryGetValue(name, out var c) && c == name;

        public static bool IsWheel(string key) => key == WheelUp || key == WheelDown;

        // ---------------- JIS OEM keys (Unity 6) ----------------
        // Chords are stored in legacy Input KeyCode semantics (what Input.GetKey polls: the US *scan position* of the key).
        // UI Toolkit's KeyDownEvent.keyCode names the US *virtual key* instead, so on the JIS layout 7 of the 12 OEM keys
        // differ; the router maps them with FromUiToolkit. The ¥ (scan 0x7D) and \ ろ (0x73) keys have no legacy KeyCode
        // (Input cannot see them); both arrive in UI Toolkit as Backslash and are the pseudo key JisBackslash here (held
        // polling must track it from KeyDown / KeyUp events).
        //
        //   key (JIS)  scan  UI Toolkit keyCode  legacy KeyCode (stored)
        //   ; れ       0x27  Equals              Semicolon
        //   : け       0x28  Semicolon           Quote
        //   ] む       0x2B  RightBracket        Backslash
        //   [ ゜       0x1B  LeftBracket         RightBracket
        //   @ ゛       0x1A  BackQuote           LeftBracket
        //   ^ へ       0x0D  Quote               Equals
        //   ¥ ー / \ ろ 0x7D / 0x73  Backslash   — (JisBackslash)
        //   / . - ,    0x35 0x34 0x0C 0x33       same in both

        /// <summary>HS2Next pseudo key: the JIS ¥ and \ ろ keys (no legacy Input KeyCode; UI Toolkit reports Backslash).</summary>
        public const string JisBackslash = "JisBackslash";

        static readonly Dictionary<string, string> jisFromUiToolkit = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Equals"] = "Semicolon",
            ["Semicolon"] = "Quote",
            ["RightBracket"] = "Backslash",
            ["LeftBracket"] = "RightBracket",
            ["BackQuote"] = "LeftBracket",
            ["Quote"] = "Equals",
            ["Backslash"] = JisBackslash,
        };

        static readonly Dictionary<string, string> jisToUiToolkit = Invert(jisFromUiToolkit);

        static Dictionary<string, string> Invert(Dictionary<string, string> d)
        {
            var r = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in d) r[kv.Value] = kv.Key;
            return r;
        }

        /// <summary>The stored (legacy Input) key name for a UI Toolkit <c>KeyDownEvent.keyCode</c> name (pass
        /// <c>evt.keyCode.ToString()</c>): on the JIS layout the OEM keys of S8 are remapped, everything else (and every key on
        /// the US layout) is returned unchanged.</summary>
        public static string FromUiToolkit(string uiToolkitKeyCode, KeyboardLayout layout)
        {
            if (uiToolkitKeyCode == null) return null;
            return layout == KeyboardLayout.Jis && jisFromUiToolkit.TryGetValue(uiToolkitKeyCode, out var k) ? k : uiToolkitKeyCode;
        }

        /// <summary>The inverse (a stored key → the UI Toolkit keyCode name the key produces), for synthesised events (ui.input).</summary>
        public static string ToUiToolkit(string storedKey, KeyboardLayout layout)
        {
            if (storedKey == null) return null;
            if (layout == KeyboardLayout.Jis && jisToUiToolkit.TryGetValue(storedKey, out var k)) return k;
            return storedKey == JisBackslash ? "Backslash" : storedKey;
        }

        /// <summary>Whether a stored key can be polled through legacy Input (false for the wheel and JisBackslash).</summary>
        public static bool IsPollable(string key) => key != null && !IsWheel(key) && key != JisBackslash;

        /// <summary>JIS keycap labels of the stored (legacy) KeyCodes of the OEM keys (S8).</summary>
        public static readonly IReadOnlyDictionary<string, string> JisLabels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Semicolon"] = ";",
            ["Quote"] = ":",
            ["Backslash"] = "]",
            ["RightBracket"] = "[",
            ["LeftBracket"] = "@",
            ["Equals"] = "^",
            [JisBackslash] = "¥ / ろ",
        };

        /// <summary>Label for the keyboard layout: JIS keycaps for the OEM keys (S8), else the US label.</summary>
        public static string Display(string key, KeyboardLayout layout) =>
            layout == KeyboardLayout.Jis && key != null && JisLabels.TryGetValue(key, out var j) ? j : Display(key);

        /// <summary>Short label for overlays and menus (US layout; see the overload for JIS).</summary>
        public static string Display(string key)
        {
            switch (key)
            {
                case null: return "";
                case "UpArrow": return "↑";
                case "DownArrow": return "↓";
                case "LeftArrow": return "←";
                case "RightArrow": return "→";
                case "Return": return "Enter";
                case "Escape": return "Esc";
                case "Delete": return "Del";
                case "PageUp": return "PgUp";
                case "PageDown": return "PgDn";
                case "Semicolon": return ";";
                case "Colon": return ":";
                case "Equals": return "=";
                case "Slash": return "/";
                case "Backslash": return "\\";
                case "Period": return ".";
                case "Comma": return ",";
                case "Minus": return "-";
                case "LeftBracket": return "[";
                case "RightBracket": return "]";
                case "Quote": return "'";
                case "BackQuote": return "`";
                case "At": return "@";
                case "Caret": return "^";
                case "Underscore": return "_";
                case WheelUp: return "Wheel↑";
                case WheelDown: return "Wheel↓";
                case JisBackslash: return "¥ / ろ";
            }
            if (key.StartsWith("Alpha", StringComparison.Ordinal) && key.Length == 6) return key.Substring(5);
            if (key.StartsWith("Keypad", StringComparison.Ordinal)) return "Num " + KeypadSuffix(key.Substring(6));
            return key;
        }

        static string KeypadSuffix(string s)
        {
            switch (s)
            {
                case "Period": return ".";
                case "Divide": return "/";
                case "Multiply": return "*";
                case "Minus": return "-";
                case "Plus": return "+";
                case "Enter": return "Enter";
                case "Equals": return "=";
                default: return s;
            }
        }
    }
}
