// Additional bones of parts, with the BonesFramework plugin's semantics (behaviour only): a
// part's bundle may hold a TextAsset "additional_bones" whose lines are "<prefab name>\t<bone>\t<bone>...". The first line
// whose first field is the loaded prefab's name (ordinal) names the bones the part's rebind moves under the skeleton.
// Lines split at "\r\n" or "\n" with empty lines dropped; fields split at tabs, empty fields kept as the plugin keeps them.
using System;
using System.Collections.Generic;

namespace HS2Next.Content
{
    public static class AdditionalBones
    {
        /// <summary>The TextAsset's name in a part's bundle.</summary>
        public const string Asset = "additional_bones";

        static readonly string[] LineBreaks = { "\r\n", "\n" };

        /// <summary>The bone names <paramref name="text"/> lists for the prefab <paramref name="prefab"/>, or null when no line
        /// names it.</summary>
        public static HashSet<string> Parse(string text, string prefab)
        {
            if (string.IsNullOrEmpty(text) || prefab == null) return null;
            foreach (var line in text.Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries))
            {
                var f = line.Split('\t');
                if (!string.Equals(f[0], prefab, StringComparison.Ordinal)) continue;
                var names = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 1; i < f.Length; i++) names.Add(f[i]);
                return names;
            }
            return null;
        }
    }
}
