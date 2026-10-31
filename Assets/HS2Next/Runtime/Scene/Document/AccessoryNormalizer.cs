// `state.accessories` fill and prune (save format open point 15.23): when a
// character's worn coordinate is known (a stored subtree, or a card source whose accessory ids are known),
// every accessory id missing from the map is added as visible and every key that is not an accessory of the
// coordinate is removed; values of present ids are kept. An unknown coordinate leaves the map exactly as
// stored. Runs at bind, when an appearance is set or changes, and as a guard before save.
using System;
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes
{
    public static class AccessoryNormalizer
    {
        /// <summary>The accessory ids the map of <paramref name="chara"/> follows, or null when the coordinate is not known.</summary>
        public static IReadOnlyList<string> KnownIds(TreeNode chara) => chara?.Kind == NodeKind.Chara ? chara.Appearance?.WornAccessoryIds() : null;

        /// <summary>The normalised map, or null when <paramref name="ids"/> is null or nothing would change.</summary>
        public static SortedDictionary<string, bool> Normalized(SortedDictionary<string, bool> current, IReadOnlyList<string> ids)
        {
            if (ids == null) return null;
            var want = new HashSet<string>(ids, StringComparer.Ordinal);
            bool changed = false;
            if (current != null)
                foreach (var k in current.Keys) if (!want.Contains(k)) { changed = true; break; }
            if (!changed)
                foreach (var id in want) if (current == null || !current.ContainsKey(id)) { changed = true; break; }
            if (!changed) return null;
            var r = new SortedDictionary<string, bool>(StringComparer.Ordinal);
            foreach (var id in want) r[id] = current != null && current.TryGetValue(id, out bool v) ? v : true;
            return r;
        }

        /// <summary>Normalises the map of a character node in place (no events, no history); true when it changed.</summary>
        internal static bool Normalize(TreeNode chara)
        {
            if (!(chara?.Doc?.State is CharaNodeState cs)) return false;
            var map = Normalized(cs.State.Accessories, KnownIds(chara));
            if (map == null) return false;
            cs.State.Accessories = map;
            return true;
        }
    }
}
