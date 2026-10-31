// Character list category names (`ChaListDefine.CategoryNo` member names and numbers; data facts
// 2). The base game files a list TextAsset under the category whose name equals the asset name without its last
// "_suffix"; Sideloader's manifest <migrationInfo> entries name categories by member name or number.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace HS2Next.Content
{
    public static class ListCategories
    {
        static readonly Dictionary<string, int> ByName = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["unknown"] = -1, ["cha_sample_m"] = 0, ["cha_sample_f"] = 1, ["init_mind_param"] = 3, ["cha_sample_voice"] = 5,
            ["mole_layout"] = 6, ["facepaint_layout"] = 7, ["bodypaint_layout"] = 8,
            ["mo_head"] = 110, ["mt_skin_f"] = 111, ["mt_detail_f"] = 112, ["mt_beard"] = 121, ["mt_skin_b"] = 131, ["mt_detail_b"] = 132,
            ["mt_sunburn"] = 133, ["mo_top"] = 140, ["mo_bot"] = 141, ["mo_gloves"] = 144, ["mo_shoes"] = 147,
            ["fo_head"] = 210, ["ft_skin_f"] = 211, ["ft_detail_f"] = 212, ["ft_skin_b"] = 231, ["ft_detail_b"] = 232, ["ft_sunburn"] = 233,
            ["fo_top"] = 240, ["fo_bot"] = 241, ["fo_inner_t"] = 242, ["fo_inner_b"] = 243, ["fo_gloves"] = 244, ["fo_panst"] = 245,
            ["fo_socks"] = 246, ["fo_shoes"] = 247,
            ["so_hair_b"] = 300, ["so_hair_f"] = 301, ["so_hair_s"] = 302, ["so_hair_o"] = 303, ["preset_hair_color"] = 305, ["st_hairmeshptn"] = 306,
            ["st_paint"] = 313, ["st_eyebrow"] = 314, ["st_eyelash"] = 315, ["st_eyeshadow"] = 316, ["st_eye"] = 317, ["st_eyeblack"] = 318,
            ["st_eye_hl"] = 319, ["st_cheek"] = 320, ["st_lip"] = 322, ["st_mole"] = 323, ["st_nip"] = 334, ["st_underhair"] = 335,
            ["preset_skin_color"] = 336, ["st_pattern"] = 348,
            ["ao_none"] = 350, ["ao_head"] = 351, ["ao_ear"] = 352, ["ao_glasses"] = 353, ["ao_face"] = 354, ["ao_neck"] = 355, ["ao_shoulder"] = 356,
            ["ao_chest"] = 357, ["ao_waist"] = 358, ["ao_back"] = 359, ["ao_arm"] = 360, ["ao_hand"] = 361, ["ao_leg"] = 362, ["ao_kokan"] = 363,
            ["custom_pose_m"] = 500, ["custom_pose_f"] = 501, ["custom_eyebrow_m"] = 502, ["custom_eyebrow_f"] = 503, ["custom_eye_m"] = 504,
            ["custom_eye_f"] = 505, ["custom_mouth_m"] = 506, ["custom_mouth_f"] = 507,
        };

        static readonly Dictionary<int, string> ByNumber = Invert();

        static Dictionary<int, string> Invert()
        {
            var d = new Dictionary<int, string>();
            foreach (var kv in ByName) d[kv.Value] = kv.Key;
            return d;
        }

        /// <summary>The member name of a category number, or null.</summary>
        public static string NameOf(int category) => ByNumber.TryGetValue(category, out var n) ? n : null;

        /// <summary>
        /// A category given as a member name (exact case) or a number, as `Enum.Parse` reads it for Sideloader's
        /// <migrationInfo> (surrounding white space ignored; an undefined number is accepted).
        /// </summary>
        public static bool TryParse(string s, out int category)
        {
            category = 0;
            if (s == null) return false;
            s = s.Trim();
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out category)) return true;
            return ByName.TryGetValue(s, out category);
        }

        /// <summary>
        /// The category a base-game list TextAsset belongs to by its name (the name without its last "_suffix" is a member
        /// name), or null when it names none (the base game ignores such assets).
        /// </summary>
        public static int? OfListAsset(string assetName)
        {
            if (assetName == null) return null;
            int u = assetName.LastIndexOf('_');
            if (u <= 0) return null;
            return ByName.TryGetValue(assetName.Substring(0, u), out var c) ? c : (int?)null;
        }
    }
}
