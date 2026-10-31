// Keymaps (Appendix C): data `{ actionId → [chord] }`. Presets `default` and `hs2studio`
// are JSON resources (Resources/UI/Keymaps/<name>.json); the user's overrides live in <UserData>/config/keymap.json and
// hold only the differences from the preset.
//
// Preset file:   { "version": 1, "name": "default", "bindings": { "edit.undo": ["Ctrl+Z"], … }, "notes": { … } }
// Override file: { "version": 1, "preset": "default", "bindings": { "edit.rename": ["F2", "Ctrl+R"], "tool.space": [] } }
//                (an empty list unbinds; ids absent from the override keep the preset's chords; unknown members kept)
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HS2Next.UI.Core
{
    /// <summary>Two actions of one scope (or overlapping scopes of the same precedence) bound to the same chord.</summary>
    public sealed class KeymapConflict
    {
        public ActionScope Scope;
        public Chord Chord;
        public string ActionA, ActionB;
        /// <summary>"same" (identical chord) or "prefix" (a single stroke that is the first stroke of a two-stroke chord).</summary>
        public string What;
        public override string ToString() => Scope + " " + Chord + ": " + ActionA + " / " + ActionB + " (" + What + ")";
    }

    public sealed class Keymap
    {
        public const int Version = 1;
        public const string DefaultPreset = "default", Hs2StudioPreset = "hs2studio";
        public static readonly IReadOnlyList<string> PresetNames = new[] { DefaultPreset, Hs2StudioPreset };

        readonly Dictionary<string, List<Chord>> bindings = new Dictionary<string, List<Chord>>(StringComparer.Ordinal);

        public string Name;
        /// <summary>Per-action notes of the preset file (why a binding is what it is); not used by the resolver.</summary>
        public readonly Dictionary<string, string> Notes = new Dictionary<string, string>(StringComparer.Ordinal);

        public Keymap(string name = null) { Name = name; }

        /// <summary>Action ids with at least one chord, ordinal order.</summary>
        public IEnumerable<string> BoundActions => bindings.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal);

        static readonly IReadOnlyList<Chord> None = new Chord[0];

        public IReadOnlyList<Chord> ChordsOf(string actionId) => actionId != null && bindings.TryGetValue(actionId, out var l) ? l : None;

        /// <summary>Replaces the chords of an action (empty = unbound).</summary>
        public void Set(string actionId, IEnumerable<Chord> chords)
        {
            var l = new List<Chord>();
            foreach (var c in chords ?? Enumerable.Empty<Chord>()) if (!c.IsEmpty && !l.Contains(c)) l.Add(c);
            bindings[actionId] = l;
        }

        public void Set(string actionId, params string[] chords) => Set(actionId, chords.Select(Chord.Parse));

        public void Add(string actionId, Chord chord)
        {
            if (!bindings.TryGetValue(actionId, out var l)) bindings[actionId] = l = new List<Chord>();
            if (!l.Contains(chord)) l.Add(chord);
        }

        /// <summary>Whether the map has an entry for the action (an empty entry = explicitly unbound).</summary>
        public bool Has(string actionId) => actionId != null && bindings.ContainsKey(actionId);

        public IEnumerable<string> Entries => bindings.Keys;

        public Keymap Clone(string name = null)
        {
            var k = new Keymap(name ?? Name);
            foreach (var kv in bindings) k.bindings[kv.Key] = new List<Chord>(kv.Value);
            foreach (var kv in Notes) k.Notes[kv.Key] = kv.Value;
            return k;
        }

        /// <summary>Actions bound to a single-stroke chord equal to <paramref name="stroke"/> (any scope).</summary>
        public IEnumerable<string> ActionsFor(KeyStroke stroke)
        {
            foreach (var kv in bindings)
                foreach (var c in kv.Value)
                    if (!c.HasSecond && c.First == stroke) { yield return kv.Key; break; }
        }

        // ---------------- overrides ----------------

        /// <summary>This preset with the user's overrides applied.</summary>
        public Keymap WithOverrides(KeymapOverrides o)
        {
            var k = Clone();
            if (o == null) return k;
            foreach (var kv in o.Bindings) k.Set(kv.Key, kv.Value);
            return k;
        }

        /// <summary>The differences of <paramref name="current"/> from this preset (for keymap.json).</summary>
        public KeymapOverrides DiffTo(Keymap current)
        {
            var o = new KeymapOverrides { Preset = Name };
            var ids = new SortedSet<string>(bindings.Keys.Concat(current.bindings.Keys), StringComparer.Ordinal);
            foreach (var id in ids)
            {
                var a = ChordsOf(id);
                var b = current.ChordsOf(id);
                if (a.Count == b.Count && a.All(b.Contains)) continue;
                o.Bindings[id] = new List<Chord>(b);
            }
            return o;
        }

        // ---------------- checks ----------------

        /// <summary>Conflicts: within each scope, two Trigger / Held actions with the same chord or a single stroke that is the
        /// first stroke of another action's two-stroke chord; Modifier actions are checked among themselves per scope.
        /// Bindings of different scopes do not conflict (precedence decides).</summary>
        public List<KeymapConflict> Conflicts()
        {
            var r = new List<KeymapConflict>();
            var entries = new List<(string id, ActionInfo info, Chord c)>();
            foreach (var kv in bindings)
            {
                ActionManifest.TryGet(kv.Key, out var info);
                foreach (var c in kv.Value) entries.Add((kv.Key, info, c));
            }
            entries.Sort((x, y) => string.CompareOrdinal(x.id, y.id));
            for (int i = 0; i < entries.Count; i++)
                for (int j = i + 1; j < entries.Count; j++)
                {
                    var a = entries[i];
                    var b = entries[j];
                    if (a.id == b.id || a.info == null || b.info == null) continue;
                    if (a.info.Scope != b.info.Scope) continue;
                    bool modA = a.info.Kind == ActionKind.Modifier, modB = b.info.Kind == ActionKind.Modifier;
                    if (modA != modB) continue;
                    if (a.c == b.c)
                        r.Add(new KeymapConflict { Scope = a.info.Scope, Chord = a.c, ActionA = a.id, ActionB = b.id, What = "same" });
                    else if (!a.c.HasSecond && b.c.HasSecond && b.c.First == a.c.First)
                        r.Add(new KeymapConflict { Scope = a.info.Scope, Chord = a.c, ActionA = a.id, ActionB = b.id, What = "prefix" });
                    else if (!b.c.HasSecond && a.c.HasSecond && a.c.First == b.c.First)
                        r.Add(new KeymapConflict { Scope = a.info.Scope, Chord = b.c, ActionA = b.id, ActionB = a.id, What = "prefix" });
                }
            return r;
        }

        /// <summary>Problems of the map itself: unknown action ids, modifier-only chords on non-Modifier actions, wheel chords on
        /// Held actions that need a key held, two-stroke chords on Held / Modifier actions.</summary>
        public List<string> Validate()
        {
            var p = new List<string>();
            foreach (var kv in bindings.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (!ActionManifest.TryGet(kv.Key, out var info)) { p.Add("unknown action " + kv.Key); continue; }
                foreach (var c in kv.Value)
                {
                    if (c.IsModifierOnly && info.Kind != ActionKind.Modifier) p.Add(kv.Key + ": modifier-only chord " + c + " needs a Modifier action");
                    if (c.HasSecond && info.Kind != ActionKind.Trigger) p.Add(kv.Key + ": two-stroke chord " + c + " on a " + info.Kind + " action");
                    if (info.Kind == ActionKind.Modifier && KeyNames.IsWheel(c.First.Key)) p.Add(kv.Key + ": wheel chord on a Modifier action");
                }
            }
            return p;
        }

        // ---------------- JSON ----------------

        public static Keymap FromJson(string json, List<string> problems = null)
        {
            var o = JObject.Parse(json);
            var k = new Keymap((string)o["name"]);
            int v = (int?)o["version"] ?? 0;
            if (v > Version) problems?.Add("keymap version " + v + " is newer than " + Version + "; read as " + Version);
            if (o["bindings"] is JObject b)
                foreach (var p in b.Properties())
                    k.Set(p.Name, ReadChords(p.Value, p.Name, problems));
            if (o["notes"] is JObject n)
                foreach (var p in n.Properties()) k.Notes[p.Name] = (string)p.Value;
            return k;
        }

        internal static List<Chord> ReadChords(JToken t, string id, List<string> problems)
        {
            var l = new List<Chord>();
            IEnumerable<JToken> items = t is JArray a ? a : t.Type == JTokenType.String ? new[] { t } : Enumerable.Empty<JToken>();
            if (!(t is JArray) && t.Type != JTokenType.String && t.Type != JTokenType.Null) problems?.Add(id + ": chords must be a list of strings");
            foreach (var e in items)
            {
                if (e.Type != JTokenType.String) { problems?.Add(id + ": chord is not a string: " + e); continue; }
                if (Chord.TryParse((string)e, out var c, out string err)) l.Add(c);
                else problems?.Add(id + ": " + err);
            }
            return l;
        }

        public string ToJson()
        {
            var b = new JObject();
            foreach (var kv in bindings.OrderBy(k => k.Key, StringComparer.Ordinal))
                b[kv.Key] = new JArray(kv.Value.Select(c => (object)c.ToString()).ToArray());
            var o = new JObject { ["version"] = Version, ["name"] = Name, ["bindings"] = b };
            if (Notes.Count > 0)
            {
                var n = new JObject();
                foreach (var kv in Notes.OrderBy(k => k.Key, StringComparer.Ordinal)) n[kv.Key] = kv.Value;
                o["notes"] = n;
            }
            return o.ToString(Formatting.Indented);
        }
    }

    /// <summary>The user's differences from a preset (keymap.json). Unknown members are kept for the round trip.</summary>
    public sealed class KeymapOverrides
    {
        public string Preset;
        public readonly SortedDictionary<string, List<Chord>> Bindings = new SortedDictionary<string, List<Chord>>(StringComparer.Ordinal);
        JObject extra = new JObject();

        public static KeymapOverrides FromJson(string json, List<string> problems = null)
        {
            var o = JObject.Parse(json);
            var r = new KeymapOverrides { Preset = (string)o["preset"] };
            int v = (int?)o["version"] ?? 0;
            if (v > Keymap.Version) problems?.Add("keymap.json version " + v + " is newer than " + Keymap.Version);
            if (o["bindings"] is JObject b)
                foreach (var p in b.Properties()) r.Bindings[p.Name] = Keymap.ReadChords(p.Value, p.Name, problems);
            foreach (var p in o.Properties())
                if (p.Name != "version" && p.Name != "preset" && p.Name != "bindings") r.extra[p.Name] = p.Value.DeepClone();
            return r;
        }

        public string ToJson()
        {
            var b = new JObject();
            foreach (var kv in Bindings) b[kv.Key] = new JArray(kv.Value.Select(c => (object)c.ToString()).ToArray());
            var o = new JObject { ["version"] = Keymap.Version, ["preset"] = Preset, ["bindings"] = b };
            foreach (var p in extra.Properties()) o[p.Name] = p.Value.DeepClone();
            return o.ToString(Formatting.Indented);
        }
    }
}
