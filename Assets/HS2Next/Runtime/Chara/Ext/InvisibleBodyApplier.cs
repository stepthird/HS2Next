// Invisible Body (`KK_InvisibleBody`): Visible == false disables every Renderer in
// the subtrees BodyTop/p_cm_body_00, BodyTop/p_cf_body_00 and BodyTop/p_cf_anim (subtree roots included), without
// descending into accessory anchor nodes (N_*) unless their parent is ct_hairB; the anchor's own renderers are switched.
// Net effect: body, head, eyes, teeth, tongue and hair are hidden; clothes and accessories stay.
// Not reproduced: the plugin's blacklist of 35 Studio/H prop renderer names (none of them is part of a character).
using System.Collections.Generic;
using HS2Next.Data.Ext;
using UnityEngine;

namespace HS2Next.Chara.Ext
{
    public static class InvisibleBodyApplier
    {
        static readonly HashSet<string> Anchors = new HashSet<string>
        {
            "N_Waist", "N_Waist_f", "N_Waist_b", "N_Waist_L", "N_Waist_R", "N_Ana", "N_Kokan", "N_Knee_L", "N_Foot_L", "N_Ankle_L",
            "N_Leg_L", "N_Knee_R", "N_Foot_R", "N_Ankle_R", "N_Leg_R", "N_Dan", "N_Tikubi_L", "N_Tikubi_R", "N_Mouth", "N_Earring_L",
            "N_Earring_R", "N_Hitai", "N_Head", "N_Head_top", "N_Hair_pin_R", "N_Hair_pin_L", "N_Hair_twin_R", "N_Hair_twin_L",
            "N_Hair_pony", "N_Nose", "N_Megane", "N_Face", "N_Neck", "N_Elbo_L", "N_Index_L", "N_Middle_L", "N_Ring_L", "N_Hand_L",
            "N_Wrist_L", "N_Arm_L", "N_Shoulder_L", "N_Elbo_R", "N_Index_R", "N_Middle_R", "N_Ring_R", "N_Hand_R", "N_Wrist_R",
            "N_Arm_R", "N_Shoulder_R", "N_Chest", "N_Back", "N_Back_R", "N_Back_L", "N_Chest_f",
        };
        static readonly string[] Subtrees = { "p_cm_body_00", "p_cf_body_00", "p_cf_anim" };

        public static void Apply(CharaContext ctx, InvisibleBodyData data)
        {
            if (data == null || data.Visible) return;
            int n = 0;
            void Walk(Transform t)
            {
                foreach (var r in t.GetComponents<Renderer>()) { r.enabled = false; n++; }
                if (Anchors.Contains(t.name) && !(t.parent && t.parent.name == "ct_hairB")) return;
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i));
            }
            foreach (var name in Subtrees)
            {
                var t = ctx.BodyTop.transform.Find(name);
                if (t) Walk(t);
            }
            ctx.Stats.InvisibleBody = true;
            ctx.Stats.InvisibleRenderers = n;
            ctx.Log.Add($"invisibleBody: {n} renderers disabled (body, head, hair)");
        }
    }
}
