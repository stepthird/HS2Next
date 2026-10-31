// Enum strings of the save documents: the model keeps the string as read; the
// typed views of the document classes map it through these helpers and return null for an unknown string.
// Also the attach-point names (AccessoryParentKey 1..54), which order the children of a character node.
using System;
using System.Collections.Generic;

namespace HS2Next.Save
{
    public static class DocEnum
    {
        static class Names<T> where T : struct
        {
            public static readonly Dictionary<string, T> ByName = Build(false);
            public static readonly Dictionary<string, T> ByCamel = Build(true);

            static Dictionary<string, T> Build(bool camel)
            {
                var d = new Dictionary<string, T>(StringComparer.Ordinal);
                foreach (T v in Enum.GetValues(typeof(T))) d[camel ? Camel(v.ToString()) : v.ToString()] = v;
                return d;
            }
        }

        /// <summary>The member whose name is exactly <paramref name="s"/> (no numbers, no case folding), or null.</summary>
        public static T? Parse<T>(string s) where T : struct =>
            s != null && Names<T>.ByName.TryGetValue(s, out var v) ? v : (T?)null;

        /// <summary>As <see cref="Parse{T}"/> for enums whose JSON strings are the camel-cased member names (`none`, `xy`).</summary>
        public static T? ParseCamel<T>(string s) where T : struct =>
            s != null && Names<T>.ByCamel.TryGetValue(s, out var v) ? v : (T?)null;

        /// <summary>Exact member names of <typeparamref name="T"/> (for the reader's unknown-string finding).</summary>
        public static ICollection<string> NamesOf<T>() where T : struct => Names<T>.ByName.Keys;

        /// <summary>Camel-cased member names of <typeparamref name="T"/>.</summary>
        public static ICollection<string> CamelNamesOf<T>() where T : struct => Names<T>.ByCamel.Keys;

        /// <summary>The camel-cased JSON string of a member (null for null).</summary>
        public static string Camel<T>(T? v) where T : struct => v.HasValue ? Camel(v.Value.ToString()) : null;

        static string Camel(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        /// <summary>AccessoryParentKey names: index i holds the point with value i + 1 (`N_Hair_pony` = 1 … `N_Ana` = 54).</summary>
        public static readonly string[] AttachPoints =
        {
            "N_Hair_pony", "N_Hair_twin_L", "N_Hair_twin_R", "N_Hair_pin_L", "N_Hair_pin_R",
            "N_Head_top", "N_Hitai", "N_Head", "N_Face",
            "N_Earring_L", "N_Earring_R", "N_Megane", "N_Nose", "N_Mouth",
            "N_Neck", "N_Chest_f", "N_Chest",
            "N_Tikubi_L", "N_Tikubi_R", "N_Back", "N_Back_L", "N_Back_R",
            "N_Waist", "N_Waist_f", "N_Waist_b", "N_Waist_L", "N_Waist_R",
            "N_Leg_L", "N_Knee_L", "N_Ankle_L", "N_Foot_L", "N_Leg_R", "N_Knee_R", "N_Ankle_R", "N_Foot_R",
            "N_Shoulder_L", "N_Elbo_L", "N_Arm_L", "N_Wrist_L", "N_Shoulder_R", "N_Elbo_R", "N_Arm_R", "N_Wrist_R",
            "N_Hand_L", "N_Index_L", "N_Middle_L", "N_Ring_L", "N_Hand_R", "N_Index_R", "N_Middle_R", "N_Ring_R",
            "N_Dan", "N_Kokan", "N_Ana",
        };

        static readonly Dictionary<string, int> attachPointValue = BuildPointValues();

        static Dictionary<string, int> BuildPointValues()
        {
            var d = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < AttachPoints.Length; i++) d[AttachPoints[i]] = i + 1;
            return d;
        }

        /// <summary>AccessoryParentKey value (1..54) of a point name, or 0 when the name is not a known point.</summary>
        public static int AttachPointValue(string name) => name != null && attachPointValue.TryGetValue(name, out int v) ? v : 0;
    }
}
