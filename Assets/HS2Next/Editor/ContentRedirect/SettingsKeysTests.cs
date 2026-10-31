// Unity-free check of the settings keys (Runtime/Content/SettingsKeys.cs) against Config/settings.example.json, plain Mono:
// Tools/mono_tests.ps1 -Suite content (fixtures = the project root).
//   SettingsKeysTests  the example holds every key the runtime reads (an alias that repeats another may stand in for it),
//                      only such keys (a "_" member is a comment), a comment line naming each key, the switches of the
//                      non-vanilla defaults with their defaults; mapAudioLink false and app.runInBackground true; the rule
//                      of IsKnown on members made up for it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace HS2Next.Content.Tests
{
    using HS2Next.Save.Tests;

    public static class SettingsKeysTests
    {
        public static void Run(TestRunner t)
        {
            string file = Path.Combine(t.FixturesDir ?? "", "Config", "settings.example.json");
            t.Check(File.Exists(file), "Config/settings.example.json exists (" + file + ")");
            if (!File.Exists(file)) return;
            JObject o;
            try { o = JObject.Parse(File.ReadAllText(file)); }
            catch (Exception e) { t.Check(false, "settings.example.json parses: " + e.Message); return; }

            // the members as the startup's unknown-key log sees them
            var members = new List<KeyValuePair<string, bool>>();
            foreach (var p in o.Properties())
            {
                members.Add(new KeyValuePair<string, bool>(p.Name, p.Value is JObject));
                if (p.Value is JObject b && SettingsKeys.Blocks.Contains(p.Name))
                    foreach (var q in b.Properties()) members.Add(new KeyValuePair<string, bool>(p.Name + "." + q.Name, q.Value is JObject));
            }
            var paths = new HashSet<string>(members.Select(m => m.Key), StringComparer.Ordinal);
            var unknown = SettingsKeys.Unknown(members);
            t.Check(unknown.Count == 0, "the example holds only keys the runtime reads" + (unknown.Count > 0 ? ": " + string.Join(", ", unknown) : ""));

            var missing = new List<string>();
            foreach (var k in SettingsKeys.All)
            {
                if (paths.Contains(k)) continue;
                // an alias that repeats another stands in for it (and the reverse)
                if (k.StartsWith("ui.", StringComparison.Ordinal))
                {
                    string a = k.Substring(3);
                    if (SettingsKeys.UiAliases.TryGetValue(a, out var same) && same != null && paths.Contains("ui." + same)) continue;
                    if (SettingsKeys.UiAliases.Any(x => x.Value == a && paths.Contains("ui." + x.Key))) continue;
                }
                missing.Add(k);
            }
            t.Check(missing.Count == 0, $"the example holds every key the runtime reads ({SettingsKeys.All.Count()} keys)" + (missing.Count > 0 ? "; missing: " + string.Join(", ", missing) : ""));

            // a comment line ("_comment…" member at the top level) names each key (its last segment)
            var comments = string.Join("\n", o.Properties().Where(p => p.Name.StartsWith("_", StringComparison.Ordinal)).Select(p => (string)p.Value ?? ""));
            var uncommented = SettingsKeys.All.Where(k => !NamedIn(comments, k.Substring(k.LastIndexOf('.') + 1))).ToList();
            t.Check(uncommented.Count == 0, "a comment line names every key" + (uncommented.Count > 0 ? "; not named: " + string.Join(", ", uncommented) : ""));

            // the switches of the non-vanilla defaults, at their defaults
            foreach (var kv in SettingsKeys.Switches)
            {
                var v = o.SelectToken(kv.Key);
                t.Check(v != null && v.Type == JTokenType.Boolean && (bool)v == kv.Value, $"switch {kv.Key} = {kv.Value.ToString().ToLowerInvariant()} in the example ({(v == null ? "absent" : v.ToString())})");
                if (kv.Key.StartsWith("ui.", StringComparison.Ordinal)) continue;
                t.Check(SettingsKeys.Content.Contains(kv.Key) || SettingsKeys.App.Contains(kv.Key), "switch " + kv.Key + " is a key the runtime reads");
            }

            // the documented defaults of Phase 5.3: no map audio junction by default, the app runs while unfocused
            var mal = o["mapAudioLink"];
            t.Check(mal != null && mal.Type == JTokenType.Boolean && !(bool)mal, "mapAudioLink = false in the example (" + (mal?.ToString() ?? "absent") + ")");
            var rib = o.SelectToken("app.runInBackground");
            t.Check(rib != null && rib.Type == JTokenType.Boolean && (bool)rib, "app.runInBackground = true in the example (" + (rib?.ToString() ?? "absent") + ")");

            // the rule itself
            t.Check(SettingsKeys.IsKnown("_comment_x", false) && SettingsKeys.IsKnown("bridge._note", false), "a member starting with _ is a comment");
            t.Check(!SettingsKeys.IsKnown("hs2root", false), "keys are case-sensitive (hs2root is not hs2Root)");
            t.Check(!SettingsKeys.IsKnown("maps", false) && SettingsKeys.IsKnown("maps", true), "a block must be an object");
            t.Check(!SettingsKeys.IsKnown("bridge.tokens", false) && SettingsKeys.IsKnown("bridge.token", false), "a block's members are checked");
            t.Check(SettingsKeys.IsKnown("ui.camera", true) && !SettingsKeys.IsKnown("ui.camera", false) && SettingsKeys.IsKnown("ui.uiScale", false),
                "the ui block: option sections (objects) and the flat aliases");
            t.Check(!SettingsKeys.IsKnown("sideloaderRedirect", false) && !SettingsKeys.IsKnown("maker", true), "an unknown top-level key or block");
        }

        static bool NamedIn(string text, string word)
        {
            int i = 0;
            while ((i = text.IndexOf(word, i, StringComparison.Ordinal)) >= 0)
            {
                bool before = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
                bool after = i + word.Length >= text.Length || !char.IsLetterOrDigit(text[i + word.Length]);
                if (before && after) return true;
                i += word.Length;
            }
            return false;
        }
    }
}
