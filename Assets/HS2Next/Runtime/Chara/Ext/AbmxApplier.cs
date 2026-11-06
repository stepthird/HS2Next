// ABMX bone modifiers (`KKABMPlugin.ABMData`): resolves each modifier's bone
// and hands it to the character's BoneModifierApplier, after the shape sliders (the shape result is the base).
// Lookup: BodyTop = a name dictionary over cf_J_Root, depth-first, first occurrence wins, not descending into
// accessory slot objects (ca_slotNN) or children named chaF_* / chaM_*; Accessory0..19 = the slot object's subtree;
// Unknown (and any other value) = body first, then accessories 0..19. Alias c_J_hairXYZ <-> c_J_hair_XYZ.
// Bones that are not found are skipped and listed once in the log. Extra rule effects (AAC body-part rules) are added
// to the same component. The one-shot Apply here gives the static result; the motion stage bones.modifiers applies it every step.
using System.Collections.Generic;
using HS2Next.Data.Ext;
using UnityEngine;

namespace HS2Next.Chara.Ext
{
    public static class AbmxApplier
    {
        public static void Apply(CharaContext ctx, AbmxData abmx, List<BoneModifierApplier.Modifier> effects, CharaExtOptions options)
        {
            int nMods = abmx?.Modifiers.Count ?? 0;
            if (nMods == 0 && effects.Count == 0) return;
            var st = ctx.Stats;
            var body = BodyDictionary(ctx);
            var acc = new Dictionary<string, Transform>[20];
            Dictionary<string, Transform> Acc(int slot)
            {
                if (slot < 0 || slot >= 20) return null;
                if (acc[slot] == null)
                {
                    var o = ctx.AccessoryObject(slot);
                    acc[slot] = new Dictionary<string, Transform>();
                    if (o) Collect(o.transform, acc[slot], null);
                }
                return acc[slot];
            }

            var list = new List<BoneModifierApplier.Modifier>();
            var unknown = new List<string>();
            if (abmx != null)
                foreach (var m in abmx.Modifiers)
                {
                    st.AbmxModifiers++;
                    Transform t = null;
                    int loc = m.Location;
                    if (loc == (int)AbmxData.BoneLocation.BodyTop) t = Find(body, m.BoneName);
                    else if (m.AccessorySlot >= 0) t = Find(Acc(m.AccessorySlot), m.BoneName);
                    else
                    {
                        t = Find(body, m.BoneName);
                        if (t) loc = (int)AbmxData.BoneLocation.BodyTop;
                        else
                            for (int s = 0; s < 20 && !t; s++)
                                if ((t = Find(Acc(s), m.BoneName))) loc = 10 + s;
                    }
                    if (!t)
                    {
                        if (!unknown.Contains(m.BoneName)) unknown.Add(m.BoneName);
                        continue;
                    }
                    var d = m.Data;
                    if (d.IsIdentity) { st.AbmxIdentity++; continue; }
                    if (loc >= 10) st.AbmxOnAccessories++;
                    list.Add(new BoneModifierApplier.Modifier
                    {
                        Target = t, Bone = m.BoneName, Source = "abmx", Location = loc,
                        Scale = d.Scale, Length = d.Length, Position = d.Position, Rotation = d.Rotation,
                    });
                }
            st.AbmxApplied = list.Count;
            st.AbmxUnknownBones.AddRange(unknown);
            foreach (var e in effects)
            {
                if (!e.Target) e.Target = Find(body, e.Bone);
                if (e.Target) list.Add(e);
                else ctx.Log.Add($"bone effect {e.Source}: bone {e.Bone} not found");
            }
            if (list.Count == 0)
            {
                if (unknown.Count > 0) ctx.Log.Add($"abmx: {nMods} modifiers, none applied; unknown bones ({unknown.Count}): {string.Join(", ", unknown)}");
                return;
            }

            var applier = ctx.Root.GetComponent<BoneModifierApplier>();
            if (!applier) applier = ctx.Root.AddComponent<BoneModifierApplier>();
            applier.AddRange(list);
            applier.Apply();
            if (!options.BoneModifierComponent) Object.DestroyImmediate(applier);
            else ctx.Bones = applier;
            ctx.Log.Add($"abmx: {st.AbmxApplied} of {nMods} modifiers applied ({st.AbmxIdentity} identity, {st.AbmxOnAccessories} on accessories), " +
                        $"{list.Count - st.AbmxApplied} rule effects" +
                        (unknown.Count > 0 ? $"; unknown bones ({unknown.Count}): {string.Join(", ", unknown)}" : ""));
        }

        /// <summary>The BodyTop dictionary over cf_J_Root of the current hierarchy (call after the uncensor step and accessories).</summary>
        public static Dictionary<string, Transform> BodyDictionary(CharaContext ctx)
        {
            var d = new Dictionary<string, Transform>();
            var root = CharaAssembler.Find(ctx.Anim ? ctx.Anim.transform : ctx.Root.transform, "cf_J_Root");
            if (!root) return d;
            var skip = new HashSet<Transform>();
            foreach (var a in ctx.Accessories) if (a.Object) skip.Add(a.Object.transform);
            Collect(root, d, skip);
            return d;
        }

        static void Collect(Transform t, Dictionary<string, Transform> d, HashSet<Transform> skip)
        {
            if (!d.ContainsKey(t.name)) d[t.name] = t;
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (skip != null && (skip.Contains(c) || c.name.StartsWith("chaF_") || c.name.StartsWith("chaM_"))) continue;
                Collect(c, d, skip);
            }
        }

        /// <summary>Name lookup with the hair alias (c_J_hairXYZ ↔ c_J_hair_XYZ).</summary>
        public static Transform Find(Dictionary<string, Transform> d, string name)
        {
            if (d == null || name == null) return null;
            if (d.TryGetValue(name, out var t)) return t;
            const string a = "c_J_hair_", b = "c_J_hair";
            if (name.StartsWith(a)) return d.TryGetValue(b + name.Substring(a.Length), out t) ? t : null;
            if (name.StartsWith(b)) return d.TryGetValue(a + name.Substring(b.Length), out t) ? t : null;
            return null;
        }
    }
}
