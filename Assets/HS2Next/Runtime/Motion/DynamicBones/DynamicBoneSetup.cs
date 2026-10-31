// Attaches the dynamic-bone stages of a freshly built character.
// MotionSetup.Attach calls Attach once per character, after the shapes and the extension modules (DBE field
// writes included), so every chain is built from the final bind pose and fields, as the game builds them in Start.
//   - BustStage (MotionPhase.BustPhysics): the four DynamicBone_Ver02 of p_cf_anim, card softness / weight
//    , one ResetPosition for the shapes applied before attach, male path.
//   - DynamicBoneStage (MotionPhase.DynamicBone): every DynamicBone of the hair (per-bundle noShake), clothes
//     (skirt flag on top / bottom), accessories (slot noShake, DBE Weight) and any other object under the root.
// The chains are built from the parts as loaded: the clothes top / bottom m_Root / m_Exclusions / m_notRolls were
// re-pointed to the body bones by the part loader (CharaAssembler, copyDynamicBone;
// count in CharaContext.DynamicBonesRepointed). The rest of that step is done here: every component's m_Colliders is
// replaced by the body colliders under cf_J_Root. Both scans skip scene node holders attached to the body
//, also when DynamicBoneStage.Rebuild repeats them.
// Before the character's Animator binds another controller (CharaAnimation.BeforeBind), the chains get the game's
// Update-phase reset at once (DynamicBoneStage.ResetBeforeUpdate), so the Animator's defaults are their Start pose.
// Note: the compat data holders are the global classes `DynamicBone`, `DynamicBone_Ver02`,
// `DynamicBoneCollider`, `DynamicBonePlaneCollider`.
using System.Collections.Generic;
using HS2Next.Chara;
using HS2Next.IO;
using UnityEngine;

namespace HS2Next.Motion.DynamicBones
{
    public static class DynamicBoneSetup
    {
        /// <summary>false: no dynamic-bone stage is attached.</summary>
        public static bool Enabled = true;
        /// <summary>Options copied into each new character's DynamicBoneStage.</summary>
        public static DynamicBoneOptions Options = new DynamicBoneOptions();
        /// <summary>Options copied into each new character's BustStage.</summary>
        public static BustOptions Bust = new BustOptions();

        public static void Attach(CharaContext ctx)
        {
            if (!Enabled || ctx?.Motion == null || !ctx.Root) return;
            var bust = CreateBustStage(ctx);
            var dyn = new DynamicBoneStage(Options.Clone()) { Context = ctx };
            var stats = AddParts(dyn, ctx);
            if (bust != null) ctx.Motion.Add(bust);
            ctx.Motion.Add(dyn);
            // The Animator binds a new controller with the current pose as the defaults; HS2 has reset the chains by then.
            var anim = Animation.CharaAnimation.Of(ctx.Root);
            if (anim) anim.BeforeBind += _ => dyn.ResetBeforeUpdate();
            ctx.Log?.Add($"dynbone: {dyn.Chains.Count} chains (hair {stats.Hair}, clothes {stats.Clothes} incl. skirt {stats.Skirt}, accessories {stats.Accessory}, other {stats.Other}), " +
                         $"{dyn.ParticleCount} particles ({Tips(dyn)} tips), body colliders {stats.BodyColliders} ({stats.BodyCollidersOff} off), " +
                         $"noShake {stats.NoShake}, re-pointed {ctx.DynamicBonesRepointed}, without root {stats.Dead}, DBE weights {stats.DbeWeights}; " +
                         (bust != null ? $"{bust} ({string.Join(", ", ChainNames(bust))})" : "bust: no skeleton"));
        }

        static int Tips(DynamicBoneStage s) { int k = 0; foreach (var c in s.Chains) k += c.TipCount; return k; }

        static IEnumerable<string> ChainNames(BustStage b)
        {
            foreach (var c in b.All) yield return $"{c.Comment} {c.ParticleCount}p{(b.WantActive(c) ? "" : " off")}";
        }

        // ------------------------------------------------------------ bust / hips
        public static BustStage CreateBustStage(CharaContext ctx)
        {
            if (!ctx.Anim) return null;
            GameObject prefab = null;
            if (Bust.HipRestFromBindPose && ctx.Catalog != null)
                prefab = ctx.Catalog.LoadAsset<GameObject>("studio/base/00.unity3d", "p_cf_anim")
                         ?? ctx.Catalog.LoadAsset<GameObject>("chara/oo_base.unity3d", "p_cf_anim");
            var bust = new BustStage(ctx.Anim, Bust.Clone(), ctx.IsMale, prefab);
            // The documents (card builds: converted from the card with the card tree's values).
            var body = ctx.Chara?.Body;
            if (body != null)
            {
                var shape = body.Shape;
                float size = shape != null && shape.Length > 1 ? shape[1] : 0.5f;
                size = ShapeEngine.CardValue(size, ctx.IsMale);   // male range check (the shape.body unit)
                bust.BustSoftness = body.Bust.Softness;
                bust.BustWeight = body.Bust.Weight;
                bust.BustSize = size;
            }
            else if (ctx.Card?.Body is object cb)
            {
                var shape = cb.Get("shapeValueBody").Floats();
                float size = shape != null && shape.Length > 1 ? shape[1] : 0.5f;
                size = ShapeEngine.CardValue(size, ctx.IsMale);
                bust.BustSoftness = cb.Get("bustSoftness").Float(0.5f);
                bust.BustWeight = cb.Get("bustWeight").Float(0.5f);
                bust.BustSize = size;
            }
            // End of Load: reSetupDynamicBoneBust -> ResetPosition on the breasts, then softness and weight.
            bust.ResetPosition();
            return bust;
        }

        // ------------------------------------------------------------ ordinary chains
        public struct PartStats
        {
            public int Hair, Clothes, Skirt, Accessory, Other, NoShake, Dead, DbeWeights, BodyColliders, BodyCollidersOff;
        }

        /// <summary>Adds a chain for every DynamicBone of the character not yet in the stage.</summary>
        public static PartStats AddParts(DynamicBoneStage stage, CharaContext ctx)
        {
            var st = new PartStats();
            var known = new HashSet<global::DynamicBone>();
            foreach (var c in stage.Chains) if (c.Source) known.Add(c.Source);

            // Body colliders under cf_J_Root in hierarchy order; colliders inside accessory objects belong to
            // that accessory's slot (only parts loaded after it see them).
            var jRoot = ctx.Anim ? CharaAssembler.Find(ctx.Anim.transform, "cf_J_Root") : null;
            var accRoots = new Dictionary<Transform, int>();
            if (ctx.Accessories != null)
                foreach (var a in ctx.Accessories) if (a != null && a.Object) accRoots[a.Object.transform] = a.Slot;
            var bodyCols = new List<(MonoBehaviour c, int slot)>();
            if (jRoot)
                foreach (var c in OwnComponents<global::DynamicBoneCollider>(jRoot))
                {
                    int slot = -1;
                    for (var t = c.transform; t && t != jRoot; t = t.parent)
                        if (accRoots.TryGetValue(t, out int s)) { slot = s; break; }
                    bodyCols.Add((c, slot));
                    if (slot < 0) { st.BodyColliders++; if (!c.enabled) st.BodyCollidersOff++; }
                }
            List<MonoBehaviour> CollidersFor(int accessorySlot)
            {
                var l = new List<MonoBehaviour>(bodyCols.Count);
                foreach (var (c, slot) in bodyCols) if (slot < 0 || (accessorySlot >= 0 && slot < accessorySlot)) l.Add(c);
                return l;
            }

            DynamicBoneChain AddOne(global::DynamicBone db, DynamicBoneCategory cat, int kind, int slot)
            {
                if (!db || !known.Add(db)) return null;
                db.m_Colliders = CollidersFor(cat == DynamicBoneCategory.Accessory ? slot : -1);
                var ch = stage.Add(db, cat, kind, slot);
                if (ch.ParticleCount == 0) st.Dead++;
                return ch;
            }

            // Hair (kinds 0..3): per-bundle noShake from the card.
            for (int k = 0; k < (ctx.Hair?.Length ?? 0); k++)
            {
                var go = ctx.Hair[k];
                if (!go) continue;
                var bundles = HairBundles(go, ctx, k);
                foreach (var db in go.GetComponentsInChildren<global::DynamicBone>(true))
                {
                    var ch = AddOne(db, DynamicBoneCategory.Hair, k, -1);
                    if (ch == null) continue;
                    st.Hair++;
                    if (bundles.TryGetValue(db, out var b)) { ch.Bundle = b.bundle; ch.NoShake = b.noShake; }
                    if (ch.NoShake) st.NoShake++;
                }
            }

            // Clothes (kinds 0..7): top / bottom already re-pointed to the body bones by the loader (copyDynamicBone).
            for (int k = 0; k < (ctx.Clothes?.Length ?? 0); k++)
            {
                var go = ctx.Clothes[k];
                if (!go) continue;
                foreach (var db in go.GetComponentsInChildren<global::DynamicBone>(true))
                {
                    var ch = AddOne(db, DynamicBoneCategory.Clothes, k, -1);
                    if (ch == null) continue;
                    st.Clothes++;
                    if (k <= 1 && ch.Root && HasSkirtBone(ch.Root)) { ch.IsSkirt = true; st.Skirt++; }
                }
            }

            // Accessories (slot order; MoreAccessories slots >= 20 included): slot noShake, DBE Weight.
            var dbe = ctx.Ext?.DynamicBoneEditor;
            if (ctx.Accessories != null)
                foreach (var a in ctx.Accessories)
                {
                    if (a == null || !a.Object) continue;
                    foreach (var db in a.Object.GetComponentsInChildren<global::DynamicBone>(true))
                    {
                        var ch = AddOne(db, DynamicBoneCategory.Accessory, a.Type, a.Slot);
                        if (ch == null) continue;
                        st.Accessory++;
                        ch.NoShake = a.NoShake;
                        if (ch.NoShake) st.NoShake++;
                        if (dbe != null)
                            foreach (var e in dbe.Entries)
                                if (e.CoordinateIndex == 0 && e.Slot == a.Slot && e.Weight is float w && ch.Root && ch.Root.name == e.BoneName)
                                { ch.SetWeight(w); st.DbeWeights++; }
                    }
                }

            // Anything else under the character root, except scene nodes attached to the body.
            foreach (var db in OwnComponents<global::DynamicBone>(ctx.Root.transform))
                if (AddOne(db, DynamicBoneCategory.Other, -1, -1) != null) st.Other++;
            return st;
        }

        /// <summary>
        /// GetComponentsInChildren(true) that does not enter subtrees whose root carries a scene node holder
        ///: items, lights or characters attached under the character's N_* points belong to
        /// their own nodes, so their DynamicBones and colliders never become chains or body colliders of this character.
        /// </summary>
        static List<T> OwnComponents<T>(Transform root) where T : Component
        {
            var l = new List<T>();
            var buf = new List<T>();
            void Walk(Transform t)
            {
                if (t != root && t.GetComponent<HS2Next.Scenes.Runtime.SceneNodeHolder>() != null) return;
                t.GetComponents(buf);
                l.AddRange(buf);
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i));
            }
            if (root) Walk(root);
            return l;
        }

        static bool HasSkirtBone(Transform t)
        {
            // Studio FK group 13 (skirt) bones: the cf_J_Legsk_* body bones (approximation of the FK bone table).
            if (t.name.StartsWith("cf_J_Legsk_", System.StringComparison.Ordinal)) return true;
            for (int i = 0; i < t.childCount; i++) if (HasSkirtBone(t.GetChild(i))) return true;
            return false;
        }

        // CmpHair.boneInfo[k].dynamicBone + card parts[kind].dictBundle[k].noShake.
        static Dictionary<global::DynamicBone, (int bundle, bool noShake)> HairBundles(GameObject go, CharaContext ctx, int kind)
        {
            var r = new Dictionary<global::DynamicBone, (int, bool)>();
            var cmp = go.GetComponent<AIChara.CmpHair>();
            if (cmp == null || cmp.boneInfo == null) return r;
            var part = ctx.Chara?.Hair != null && kind >= 0 && kind < ctx.Chara.Hair.Parts.Length ? ctx.Chara.Hair.Parts[kind] : null;
            var dict = part == null ? ctx.Card?.Hair?.Get("parts").At(kind).Get("dictBundle") as Dictionary<object, object> : null;
            for (int b = 0; b < cmp.boneInfo.Count; b++)
            {
                var bi = cmp.boneInfo[b];
                if (bi?.dynamicBone == null) continue;
                bool ns = part != null ? HS2Next.Chara.Edit.Units.HairUnit.EffectiveNoShake(cmp, part, b)   // the count rule (Phase 4.3)
                        : dict != null && dict.TryGetValue((long)b, out var d) && d.Get("noShake").Bool();
                foreach (var db in bi.dynamicBone) if (db && !r.ContainsKey(db)) r[db] = (b, ns);
            }
            return r;
        }
    }
}
