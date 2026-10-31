// Additional Accessory Controls for the static state after a Studio load
// (clothes states from CharaContext.ClothesState, all 0 = fully dressed). One startup pass:
//   1. per slot (Studio: only slots with STUDIO_LOAD, CharaExtOptions.AccessoryControlsStudio): clothes rules
//      TOP..SHOE → hidden if any matches the clothes state (with the coordinate's suppress/override lists), forced
//      shown otherwise; then STARTUP → hidden;
//   2. hair-accessory rule HAIR:HAIR_ACC → while the slot is shown, every other hair-type accessory is hidden;
//   3. links ACCESSORY_LINK / ACCESSORY_INVERSE_LINK (1-based slot) → copied / inverted, ≤ 25 iterations;
//   4. hair-part rule HAIR:HAIR_FRONT/BACK/SIDE/EXT → while shown, the child objects of the hair part are deactivated;
//   5. body-part rules NOSE / EAR / HAND / FOOT / EYELASH → while shown, bone effects (scale or length × 0.01),
//      handed to the BoneModifierApplier together with ABMX.
// The order of STARTUP vs. clothes rules within a slot is not given by the spec; STARTUP wins here.
// Phase 4.5: the shown / hidden result of steps 1-3 no longer toggles the slot
// objects: the import folds the startup pass's hides once into the scene's state.accessories, which decides the slots'
// visibility (a build-time toggle was undone by the scene's apply). The result still decides the hair-part and body-part
// effects of steps 4 and 5. AdvancedParent, CharacterAccessory and AutoMatchBackHairColor are coordinate-load features and
// are not applied here (the raw blobs stay on the decoded data).
using System.Collections.Generic;
using System.Linq;
using AIChara;
using HS2Next.Data.Ext;
using UnityEngine;
using Rule = HS2Next.Data.Ext.AdditionalAccessoryControlsData.Rule;

namespace HS2Next.Chara.Ext
{
    public static class AccessoryControlsApplier
    {
        const string ON = "10", HALF = "11", OFF = "12", ALL = "50";

        public static void Apply(CharaContext ctx, CardExtData ext, CharaExtOptions options, List<BoneModifierApplier.Modifier> effects)
        {
            var aac = ext.AdditionalAccessoryControls;
            if (aac == null) return;
            var st = ctx.Stats;
            st.AacRules = aac.RuleCount;
            if (st.AacRules == 0) return;

            int n = ctx.Accessories.Count;
            var shown = new bool[n];
            for (int s = 0; s < n; s++) shown[s] = ctx.Accessories[s].Object && ctx.Accessories[s].Show;
            var before = (bool[])shown.Clone();
            var slots = new Dictionary<int, AdditionalAccessoryControlsData.SlotData>();
            foreach (var sd in aac.Slots) if (!sd.IsEmpty && sd.VisibilityRules.Count > 0 && sd.SlotNumber >= 0 && sd.SlotNumber < n) slots[sd.SlotNumber] = sd;

            // 1. startup + clothes rules
            foreach (var kv in slots)
            {
                int s = kv.Key; var rules = kv.Value.VisibilityRules;
                bool studioLoad = rules.Any(r => r.Rule == Rule.STUDIO_LOAD);
                if (options.AccessoryControlsStudio && !studioLoad) continue;
                if (!ctx.Accessories[s].Object) continue;
                var clothes = rules.Where(r => Kind(r.Rule) >= 0).ToList();
                if (clothes.Count > 0)
                {
                    st.AacRulesEvaluated += clothes.Count;
                    shown[s] = !clothes.Any(r => Match(ctx, aac, r.Rule, r.Modifier));
                }
                if (rules.Any(r => r.Rule == Rule.STARTUP)) { st.AacRulesEvaluated++; shown[s] = false; }
                if (studioLoad) st.AacRulesEvaluated++;
            }
            // 2. hair-accessory rule
            foreach (var kv in slots)
            {
                if (!kv.Value.VisibilityRules.Any(r => r.Rule == Rule.HAIR && r.Modifier == "34")) continue;
                st.AacRulesEvaluated++;
                if (!shown[kv.Key]) continue;
                for (int s = 0; s < n; s++)
                    if (s != kv.Key && ctx.Accessories[s].Object && ctx.Accessories[s].Object.GetComponent<CmpAccessory>() is CmpAccessory c && c != null && c.typeHair != 0)
                        shown[s] = false;
            }
            // 3. links
            var links = slots.SelectMany(kv => kv.Value.VisibilityRules.Where(r => r.Rule == Rule.ACCESSORY_LINK || r.Rule == Rule.ACCESSORY_INVERSE_LINK)
                .Select(r => (slot: kv.Key, inverse: r.Rule == Rule.ACCESSORY_INVERSE_LINK, target: int.TryParse(r.Modifier, out int t) ? t - 1 : -1))).ToList();
            if (links.Count > 0)
            {
                st.AacRulesEvaluated += links.Count;
                bool changed = true; int it = 0;
                while (changed && it++ < 25)
                {
                    changed = false;
                    foreach (var l in links)
                    {
                        if (l.target < 0 || l.target >= n || !ctx.Accessories[l.slot].Object) continue;
                        bool v = l.inverse ? !shown[l.target] : shown[l.target];
                        if (shown[l.slot] != v) { shown[l.slot] = v; changed = true; }
                    }
                }
                if (changed) ctx.Log.Add("aac: accessory links did not settle in 25 iterations (loop)");
            }
            // 4. hair-part rules
            var hideHair = new HashSet<int>();
            foreach (var kv in slots)
                foreach (var r in kv.Value.VisibilityRules)
                {
                    if (r.Rule != Rule.HAIR) continue;
                    int h = r.Modifier == "30" ? 1 : r.Modifier == "31" ? 0 : r.Modifier == "32" ? 2 : r.Modifier == "33" ? 3 : -1;
                    if (h < 0) continue;
                    st.AacRulesEvaluated++;
                    if (shown[kv.Key]) hideHair.Add(h);
                }
            foreach (int h in hideHair)
            {
                var hair = ctx.Hair[h];
                if (!hair) continue;
                for (int i = 0; i < hair.transform.childCount; i++)
                {
                    var c = hair.transform.GetChild(i).gameObject;
                    if (c.activeSelf) { c.SetActive(false); st.AacHairObjectsHidden++; }
                }
            }
            // 5. body-part rules -> bone effects
            foreach (var kv in slots)
                foreach (var r in kv.Value.VisibilityRules)
                {
                    var bones = BodyPartBones(r.Rule, r.Modifier, out bool length);
                    if (bones == null) continue;
                    st.AacRulesEvaluated++;
                    if (!shown[kv.Key]) continue;
                    foreach (var b in bones)
                    {
                        effects.Add(new BoneModifierApplier.Modifier
                        {
                            Bone = b, Source = "aac", Location = 1,
                            Scale = length ? Vector3.one : new Vector3(0.01f, 0.01f, 0.01f), Length = length ? 0.01f : 1,
                        });
                        st.AacBoneEffects++;
                    }
                }
            // the slots the pass shows or hides differently from the read state: counted, not toggled (the scene's
            // state.accessories holds the folded result; the slot objects and their read show flags stay as loaded)
            for (int s = 0; s < n; s++)
                if (shown[s] != before[s] && ctx.Accessories[s].Object) st.AacSlotsChanged++;
            ctx.Log.Add($"aac: {st.AacRulesEvaluated} of {st.AacRules} rules evaluated ({(options.AccessoryControlsStudio ? "Studio" : "game")} startup pass), " +
                        $"{st.AacSlotsChanged} slots changed (scene state, not toggled), {st.AacHairObjectsHidden} hair objects hidden, {st.AacBoneEffects} bone effects");
        }

        /// <summary>Clothes rule → clothes kind (TOP 0, BOT 1, INNER_TOP 2, INNER_BOT 3, GLOVE 4, PANTYHOSE 5, SOCK 6, SHOE 7), else -1.</summary>
        static int Kind(Rule r)
        {
            switch (r)
            {
                case Rule.TOP: return 0;
                case Rule.BOT: return 1;
                case Rule.INNER_TOP: return 2;
                case Rule.INNER_BOT: return 3;
                case Rule.GLOVE: return 4;
                case Rule.PANTYHOSE: return 5;
                case Rule.SOCK: return 6;
                case Rule.SHOE: return 7;
                default: return -1;
            }
        }

        // Clothes test of one (rule, modifier); `original` decides the HALF special case (GLOVE/SOCK/SHOE also match state 2).
        static bool Test(CharaContext ctx, Rule r, string m, Rule original)
        {
            int k = Kind(r);
            if (k < 0) return false;
            bool present = ctx.Clothes[k];
            int s = ctx.ClothesState[k];
            if (!present) return false;
            switch (m)
            {
                case ON: return s == 0;
                case HALF: return s == 1 || (s == 2 && (original == Rule.GLOVE || original == Rule.SOCK || original == Rule.SHOE));
                case OFF: return s == 2;
                case ALL: return s >= 0 && s <= 2;
                default: return false;
            }
        }

        static bool Suppressed(AdditionalAccessoryControlsData aac, Rule r, string m) =>
            aac.SuppressedRules.Any(x => x.Rule == r && x.RuleModifier == m);

        // "Coordinate override data modifies the clothes test".
        static bool Match(CharaContext ctx, AdditionalAccessoryControlsData aac, Rule r, string m)
        {
            bool overridden = aac.OverrideRules.Any(o => o.Rule == r && (o.RuleModifier == m || o.RuleModifier == ALL));
            bool hit = !overridden && !Suppressed(aac, r, m) && Test(ctx, r, m, r);
            foreach (var o in aac.OverrideRules)
            {
                if (o.OverrideRule != r || !(o.OverrideRuleModifier == m || o.OverrideRuleModifier == ALL)) continue;
                var r2 = o.Rule;
                var m2 = o.RuleModifier == ALL && o.OverrideRuleModifier == ALL ? m : o.RuleModifier;
                if (!Suppressed(aac, r2, m2) && Test(ctx, r2, m2, r)) hit = true;
            }
            return hit;
        }

        static string[] BodyPartBones(Rule r, string m, out bool length)
        {
            length = false;
            string side = m == "40" ? "L" : m == "41" ? "R" : null;
            switch (r)
            {
                case Rule.NOSE: return new[] { "cf_J_Nose_r" };
                case Rule.EAR: length = true; return side == null ? null : new[] { "cf_J_EarBase_s_" + side };
                case Rule.HAND: return side == null ? null : new[] { "cf_J_Hand_" + side };
                case Rule.FOOT: return side == null ? null : new[] { "cf_J_Foot01_" + side };
                case Rule.EYELASH: return side == null ? null : new[] { "cf_J_Eye01_s_" + side, "cf_J_Eye02_s_" + side, "cf_J_Eye03_s_" + side, "cf_J_Eye04_s_" + side };
                default: return null;
            }
        }
    }
}
