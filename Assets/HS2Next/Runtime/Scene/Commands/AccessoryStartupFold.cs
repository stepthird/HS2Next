// Additional Accessory Controls' Studio startup pass as data (Phase 4.5 visibility
// fold): which accessories of a coordinate the plugin hides when the character is loaded in Studio. The importer and the
// coordinate load fold the result once into the scene's `chara.state.accessories` (false = hidden); the build no longer
// toggles slot objects for these hides, so the scene's state is the one truth of a slot's visibility. Later clothes-state
// changes do not re-run it (live clothes rules come with the plugin editing).
//
// The pass on the typed rules of the coordinate (`accessories[*].rules.visibility`, `ruleOverrides`):
//   1. every slot whose rules include STUDIO_LOAD (Studio processes only those): its clothes rules TOP … SHOE hide it when
//      any of them matches the character's clothes state (with the coordinate's suppressed / override lists), else they
//      show it; then STARTUP hides it;
//   2. the links ACCESSORY_LINK / ACCESSORY_INVERSE_LINK (to an accessory id) copy / invert the linked accessory's state,
//      repeated until nothing changes (at most 25 rounds; a loop stops there).
// A clothes kind counts as present when the coordinate's item of that kind is not the none row; its state is the
// character's clothes state (0 on, 1 half, 2 off). The hair-part, hair-accessory and body-part rules stay with the build
// (they need the realized objects). Unity-free.
using System;
using System.Collections.Generic;
using System.Globalization;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public static class AccessoryStartupFold
    {
        /// <summary>The rule names the pass reads (AdditionalAccessoryVisibilityRules member names as the importer stores them).</summary>
        public const string Startup = "STARTUP", StudioLoad = "STUDIO_LOAD", Link = "ACCESSORY_LINK", InverseLink = "ACCESSORY_INVERSE_LINK";
        /// <summary>The clothes rules in clothes kind order (TOP 0, BOT 1, INNER_TOP 2, INNER_BOT 3, GLOVE 4, PANTYHOSE 5, SOCK 6, SHOE 7).</summary>
        public static readonly IReadOnlyList<string> ClothesRules = new[] { "TOP", "BOT", "INNER_TOP", "INNER_BOT", "GLOVE", "PANTYHOSE", "SOCK", "SHOE" };
        /// <summary>The longest link chain the pass resolves (the plugin's bound).</summary>
        public const int LinkRounds = 25;

        const string On = "ON", Half = "HALF", Off = "OFF", All = "ALL";

        /// <summary>The ids the startup pass hides, every accessory shown before it (the scene's map then holds false for them).</summary>
        public static HashSet<string> Hidden(CoordDocument coord, IReadOnlyList<int> clothesState) => Hidden(coord, clothesState, null, out _);

        /// <summary>As <see cref="Hidden(CoordDocument, IReadOnlyList{int})"/> with the card's clothes state as bytes.</summary>
        public static HashSet<string> Hidden(CoordDocument coord, byte[] clothesState)
        {
            var s = new int[8];
            if (clothesState != null) for (int k = 0; k < s.Length && k < clothesState.Length; k++) s[k] = clothesState[k];
            return Hidden(coord, s, null, out _);
        }

        /// <summary>
        /// The ids the pass leaves hidden when the accessories start with <paramref name="shownBefore"/> (accessory id → shown; a
        /// missing id or null = shown): the pass may also show a slot (a clothes rule that does not match) and links copy the
        /// states before them. <paramref name="loopStopped"/> is true when the links did not settle in 25 rounds.
        /// </summary>
        public static HashSet<string> Hidden(CoordDocument coord, IReadOnlyList<int> clothesState, IReadOnlyDictionary<string, bool> shownBefore, out bool loopStopped)
        {
            loopStopped = false;
            var hidden = new HashSet<string>(StringComparer.Ordinal);
            var list = coord?.Accessories;
            if (list == null || list.Count == 0) return hidden;
            var shown = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var a in list)
                if (a?.Id != null && !shown.ContainsKey(a.Id)) shown[a.Id] = shownBefore == null || !shownBefore.TryGetValue(a.Id, out bool v) || v;

            // 1. clothes rules and STARTUP, for slots with STUDIO_LOAD
            foreach (var a in list)
            {
                var rules = a?.Rules?.Visibility;
                if (a?.Id == null || rules == null || rules.Count == 0 || !Has(rules, StudioLoad)) continue;
                bool anyClothes = false, match = false;
                foreach (var r in rules)
                {
                    if (r == null || KindOf(r.Rule) < 0) continue;
                    anyClothes = true;
                    if (Match(coord, clothesState, r.Rule, Mod(r.Modifier))) match = true;
                }
                if (anyClothes) shown[a.Id] = !match;
                if (Has(rules, Startup)) shown[a.Id] = false;
            }

            // 2. links (by accessory id)
            var links = new List<(string id, bool inverse, string target)>();
            foreach (var a in list)
            {
                var rules = a?.Rules?.Visibility;
                if (a?.Id == null || rules == null) continue;
                foreach (var r in rules)
                    if (r != null && (r.Rule == Link || r.Rule == InverseLink) && r.Link != null) links.Add((a.Id, r.Rule == InverseLink, r.Link));
            }
            if (links.Count > 0)
            {
                bool changed = true;
                int round = 0;
                while (changed && round++ < LinkRounds)
                {
                    changed = false;
                    foreach (var l in links)
                    {
                        if (!shown.TryGetValue(l.target, out bool t)) continue;
                        bool v = l.inverse ? !t : t;
                        if (shown[l.id] != v) { shown[l.id] = v; changed = true; }
                    }
                }
                loopStopped = changed;
            }

            foreach (var kv in shown) if (!kv.Value) hidden.Add(kv.Key);
            return hidden;
        }

        static bool Has(List<AacRule> rules, string name)
        {
            foreach (var r in rules) if (r != null && r.Rule == name) return true;
            return false;
        }

        /// <summary>The clothes kind (0..7) of a clothes rule name, or -1.</summary>
        public static int KindOf(string rule)
        {
            if (rule == null) return -1;
            for (int k = 0; k < ClothesRules.Count; k++) if (ClothesRules[k] == rule) return k;
            return -1;
        }

        // A modifier as its member name (the importer stores names; a decimal text from an older writer is mapped).
        static string Mod(string m)
        {
            if (m == null) return null;
            if (!int.TryParse(m, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) return m;
            switch (v)
            {
                case 10: return On;
                case 11: return Half;
                case 12: return Off;
                case 50: return All;
                case 0: return "NONE";
            }
            return m;
        }

        // The clothes test of one (rule, modifier); `original` decides the HALF case (gloves, socks and shoes also match off).
        static bool Test(CoordDocument coord, IReadOnlyList<int> state, string rule, string m, string original)
        {
            int k = KindOf(rule);
            if (k < 0) return false;
            string item = coord?.Clothes?.Parts != null && k < coord.Clothes.Parts.Length ? coord.Clothes.Parts[k]?.Item : null;
            bool present = item != null && !string.Equals(item, DocDefaults.NoneRef, StringComparison.Ordinal);
            if (!present) return false;
            int s = state != null && k < state.Count ? state[k] : 0;
            switch (m)
            {
                case On: return s == 0;
                case Half: return s == 1 || (s == 2 && (original == "GLOVE" || original == "SOCK" || original == "SHOE"));
                case Off: return s == 2;
                case All: return s >= 0 && s <= 2;
            }
            return false;
        }

        static bool Suppressed(AacRuleOverrides o, string rule, string m)
        {
            if (o?.Suppressed == null) return false;
            foreach (var x in o.Suppressed) if (x != null && x.Rule == rule && Mod(x.Modifier) == m) return true;
            return false;
        }

        // The coordinate's override lists modify the test: an override whose source is (rule, m) or (rule, ALL) stops the
        // direct test; every override whose target is (rule, m) or (rule, ALL) tests its source instead (both ALL: the
        // slot's own modifier).
        static bool Match(CoordDocument coord, IReadOnlyList<int> state, string rule, string m)
        {
            var o = coord?.RuleOverrides;
            bool overridden = false;
            if (o?.Overrides != null)
                foreach (var x in o.Overrides)
                    if (x != null && x.Rule == rule && (Mod(x.Modifier) == m || Mod(x.Modifier) == All)) { overridden = true; break; }
            bool hit = !overridden && !Suppressed(o, rule, m) && Test(coord, state, rule, m, rule);
            if (o?.Overrides != null)
                foreach (var x in o.Overrides)
                {
                    if (x == null || x.OverrideRule != rule) continue;
                    string om = Mod(x.OverrideModifier), sm = Mod(x.Modifier);
                    if (om != m && om != All) continue;
                    string m2 = sm == All && om == All ? m : sm;
                    if (!Suppressed(o, x.Rule, m2) && Test(coord, state, x.Rule, m2, rule)) hit = true;
                }
            return hit;
        }
    }
}
