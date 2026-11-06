// What the addresses of swing chains and blend-shape renderers name on a built character, for the driver, the commands and the UI.
//   a chain      <part>/<root name>[#n], part = hair:k, clothes:k, acc:id or other; #n = the n-th chain of that part with that root
//                name in the order of the character's dynamic bone stage; a chain without a root has no address
//   a renderer   <part>/<renderer name>[#n], part = head, body, hair:k, clothes:k or acc:id; #n = the n-th skinned renderer of that name
//                in that part, depth first in child order
// The walk of a part never enters the holder of another scene node, nor another part that hangs under it. These calls need no
// advanced-pose block on the character: a character without one lists the same chains and renderers.
using System;
using System.Collections.Generic;
using HS2Next.Motion.DynamicBones;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>One addressable swing chain of a character.</summary>
    public sealed class PoseChainRow
    {
        /// <summary>The canonical address.</summary>
        public string Address;
        /// <summary>"hair:k", "clothes:k", "acc:id" or "other".</summary>
        public string Part;
        /// <summary>The name of the chain's root bone.</summary>
        public string Root;
        public DynamicBoneChain Chain;
        public override string ToString() => Address;
    }

    /// <summary>One addressable skinned renderer of a character.</summary>
    public sealed class PoseRendererRow
    {
        /// <summary>The canonical address.</summary>
        public string Address;
        /// <summary>"head", "body", "hair:k", "clothes:k" or "acc:id".</summary>
        public string Part;
        public string Name;
        public SkinnedMeshRenderer Renderer;
        public override string ToString() => Address;
    }

    public static class AdvancedPoseParts
    {
        // ------------------------------------------------------------ chains

        /// <summary>The swing chains that have an address, in stage order.</summary>
        public static List<PoseChainRow> Chains(CharaRealization r)
        {
            var rows = new List<PoseChainRow>();
            var stage = r?.Motion ? r.Motion.Get<DynamicBoneStage>() : null;
            if (stage == null) return rows;
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var c in stage.Chains)
            {
                if (c == null || !c.Root || !PartOf(r, c, out var part, out int slot, out string id)) continue;
                string partText = PoseAddressGrammar.PartText(part, slot, id);
                string key = partText + "/" + c.RootName;
                seen.TryGetValue(key, out int n);
                seen[key] = n + 1;
                string text = PoseAddress.Chain(part, slot, id, c.RootName, n).Text;
                if (PoseAddress.IsCanonical(PoseAddressForm.Chain, text))   // a root whose name holds a '/' cannot be addressed
                    rows.Add(new PoseChainRow { Address = text, Part = partText, Root = c.RootName, Chain = c });
            }
            return rows;
        }

        /// <summary>The chain of a canonical address, or null when this character lacks it.</summary>
        public static DynamicBoneChain Chain(CharaRealization r, string address)
        {
            foreach (var row in Chains(r))
                if (row.Address == address) return row.Chain;
            return null;
        }

        static bool PartOf(CharaRealization r, DynamicBoneChain c, out PosePart part, out int slot, out string id)
        {
            part = PosePart.Other;
            slot = -1;
            id = null;
            switch (c.Category)
            {
                case DynamicBoneCategory.Hair:
                    part = PosePart.Hair;
                    slot = c.Kind;
                    return slot >= 0 && slot < PoseAddressGrammar.HairSlots;
                case DynamicBoneCategory.Clothes:
                    part = PosePart.Clothes;
                    slot = c.Kind;
                    return slot >= 0 && slot < PoseAddressGrammar.ClothesSlots;
                case DynamicBoneCategory.Accessory:
                    part = PosePart.Accessory;
                    var slots = r.Context?.Accessories;
                    if (slots == null) return false;
                    foreach (var s in slots)
                        if (s != null && s.Slot == c.Slot) { id = r.AccessoryIdOf(s); return id != null; }
                    return false;
                default:
                    return true;
            }
        }

        // ------------------------------------------------------------ renderers

        /// <summary>
        /// The skinned renderers with blend shapes, the parts in the order head, body, hair, clothes, accessories (only
        /// <paramref name="part"/> when given: "head", "body", "hair:k", "clothes:k", "acc:id"), each part depth first in child order.
        /// </summary>
        public static List<PoseRendererRow> Renderers(CharaRealization r, string part = null)
        {
            var rows = new List<PoseRendererRow>();
            var cc = r?.Context;
            if (cc == null) return rows;
            var tops = Tops(r);
            foreach (var (top, kind, slot, id) in Parts(r))
            {
                string text = PoseAddressGrammar.PartText(kind, slot, id);
                if (part != null && part != text) continue;
                var seen = new Dictionary<string, int>(StringComparer.Ordinal);
                Walk(top, top, tops, s =>
                {
                    seen.TryGetValue(s.name, out int n);
                    seen[s.name] = n + 1;
                    if (!s.sharedMesh || s.sharedMesh.blendShapeCount == 0) return false;
                    string address = PoseAddress.Blend(kind, slot, id, s.name, n).Text;
                    if (PoseAddress.IsCanonical(PoseAddressForm.Blend, address))
                        rows.Add(new PoseRendererRow { Address = address, Part = text, Name = s.name, Renderer = s });
                    return false;
                });
            }
            return rows;
        }

        /// <summary>The skinned renderer of a canonical address (also one without shapes), or null when this character lacks it.</summary>
        public static SkinnedMeshRenderer Renderer(CharaRealization r, string address)
        {
            if (r?.Context == null || !PoseAddress.TryParse(PoseAddressForm.Blend, address, out var a, out _)) return null;
            foreach (var (top, kind, slot, id) in Parts(r))
            {
                if (kind != a.Part || slot != a.Slot || id != a.AccessoryId) continue;
                int n = a.Occurrence;
                SkinnedMeshRenderer found = null;
                Walk(top, top, Tops(r), s =>
                {
                    if (s.name != a.Name || n-- != 0) return false;
                    found = s;
                    return true;
                });
                return found;
            }
            return null;
        }

        // the part tops of a built character, in listing order
        static IEnumerable<(Transform top, PosePart kind, int slot, string id)> Parts(CharaRealization r)
        {
            var cc = r.Context;
            if (cc.Head) yield return (cc.Head.transform, PosePart.Head, -1, null);
            if (cc.Body) yield return (cc.Body.transform, PosePart.Body, -1, null);
            for (int k = 0; k < cc.Hair.Length; k++)
                if (cc.Hair[k]) yield return (cc.Hair[k].transform, PosePart.Hair, k, null);
            for (int k = 0; k < cc.Clothes.Length; k++)
                if (cc.Clothes[k]) yield return (cc.Clothes[k].transform, PosePart.Clothes, k, null);
            if (cc.Accessories != null)
                foreach (var s in cc.Accessories)
                {
                    if (s == null || !s.Object) continue;
                    string id = r.AccessoryIdOf(s);
                    if (id != null) yield return (s.Object.transform, PosePart.Accessory, -1, id);
                }
        }

        static HashSet<Transform> Tops(CharaRealization r)
        {
            var set = new HashSet<Transform>();
            foreach (var (top, _, _, _) in Parts(r)) set.Add(top);
            return set;
        }

        // depth first in child order; visit returns true to stop. A renderer of a part is the part's own: another part under it is left out.
        static bool Walk(Transform t, Transform top, HashSet<Transform> tops, Func<SkinnedMeshRenderer, bool> visit)
        {
            var own = t.GetComponent<SkinnedMeshRenderer>();
            if (own && visit(own)) return true;
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (SceneNodeHolder.IsHolder(c) || (c != top && tops.Contains(c))) continue;
                if (Walk(c, top, tops, visit)) return true;
            }
            return false;
        }
    }
}
