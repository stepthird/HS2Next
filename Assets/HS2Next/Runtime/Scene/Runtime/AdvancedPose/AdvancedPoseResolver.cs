// Finds what an advanced-pose address names on a built character, and lists a character's addressable bones.
//   a bare name      the body bones, then the head bones (as chara.bones resolves names)
//   hair:k/name      under the hair part k; clothes:k/name under the clothes part k; acc:id/name under the slot object of the accessory
//   #n               the n-th occurrence of a repeated name in that part, depth first in child order
// A walk never enters the holder of another scene node.
using System.Collections.Generic;
using HS2Next.Chara;
using HS2Next.Motion.DynamicBones;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>One addressable bone of a character.</summary>
    public sealed class PoseBoneRow
    {
        /// <summary>The canonical address.</summary>
        public string Address;
        public Transform Bone;
        /// <summary>"body", "head", "hair:k", "clothes:k" or "acc:id".</summary>
        public string Part;
        /// <summary>The parent's address in the same part, or null at the part's top.</summary>
        public string Parent;
        public int Depth;
        public override string ToString() => Address;
    }

    public static class AdvancedPoseResolver
    {
        public const string BodyPart = "body", HeadPart = "head";

        /// <summary>The transform of a bone address on a built character, or null when this character lacks it.</summary>
        public static Transform Resolve(CharaRealization r, in PoseAddress a)
        {
            var cc = r?.Context;
            if (cc == null) return null;
            if (a.Part == HS2Next.Save.PosePart.Named)
            {
                if (cc.BodyBones != null && cc.BodyBones.TryGetValue(a.Name, out var b) && b && !UnderHolder(b, r.Root.transform)) return b;
                if (cc.HeadBones != null && cc.HeadBones.TryGetValue(a.Name, out var h) && h && !UnderHolder(h, r.Root.transform)) return h;
                return null;
            }
            var top = PartRoot(r, a.Part, a.Slot, a.AccessoryId);
            if (!top) return null;
            int n = a.Occurrence;
            return Walk(top, a.Name, ref n);
        }

        /// <summary>The transform of a bone address text, or null when it does not parse or this character lacks it.</summary>
        public static Transform Resolve(CharaRealization r, string address) =>
            PoseAddress.TryParse(HS2Next.Save.PoseAddressForm.Bone, address, out var a, out _) ? Resolve(r, a) : null;

        /// <summary>The top transform of a part (hair, clothes, accessory), or null.</summary>
        public static Transform PartRoot(CharaRealization r, HS2Next.Save.PosePart part, int slot, string accessoryId)
        {
            var cc = r?.Context;
            if (cc == null) return null;
            switch (part)
            {
                case HS2Next.Save.PosePart.Hair:
                    return slot >= 0 && slot < cc.Hair.Length && cc.Hair[slot] ? cc.Hair[slot].transform : null;
                case HS2Next.Save.PosePart.Clothes:
                    return slot >= 0 && slot < cc.Clothes.Length && cc.Clothes[slot] ? cc.Clothes[slot].transform : null;
                case HS2Next.Save.PosePart.Accessory:
                    if (string.IsNullOrEmpty(accessoryId) || cc.Accessories == null) return null;
                    foreach (var s in cc.Accessories)
                        if (s != null && s.Object && r.AccessoryIdOf(s) == accessoryId) return s.Object.transform;
                    return null;
                default:
                    return null;
            }
        }

        // the n-th transform of that name, depth first in child order, never entering a holder
        static Transform Walk(Transform t, string name, ref int n)
        {
            if (t.name == name && n-- == 0) return t;
            int c = t.childCount;
            for (int i = 0; i < c; i++)
            {
                var ch = t.GetChild(i);
                if (SceneNodeHolder.IsHolder(ch)) continue;
                var f = Walk(ch, name, ref n);
                if (f) return f;
            }
            return null;
        }

        static bool UnderHolder(Transform t, Transform root)
        {
            for (var p = t; p && p != root; p = p.parent)
                if (SceneNodeHolder.IsHolder(p)) return true;
            return false;
        }

        // ------------------------------------------------------------ listing

        /// <summary>
        /// The addressable bones of a built character, the parts in the order body, head, hair, clothes, accessories (only
        /// <paramref name="part"/> when given: "body", "head", "hair:k", "clothes:k", "acc:id"), each part depth first in child order.
        /// </summary>
        public static List<PoseBoneRow> Bones(CharaRealization r, string part = null)
        {
            var rows = new List<PoseBoneRow>();
            var cc = r?.Context;
            if (cc == null) return rows;
            if (part == null || part == BodyPart)
            {
                var body = cc.Anim ? CharaAssembler.Find(cc.Anim.transform, "cf_J_Root") : null;
                if (body) Named(r, rows, body, BodyPart, cc.HeadBone ? cc.HeadBone.transform : null);
            }
            if ((part == null || part == HeadPart) && cc.HeadBone) Named(r, rows, cc.HeadBone.transform, HeadPart, null);
            for (int k = 0; k < cc.Hair.Length; k++)
            {
                string p = HS2Next.Save.PoseAddressGrammar.PartText(HS2Next.Save.PosePart.Hair, k, null);
                if ((part == null || part == p) && cc.Hair[k]) Part(rows, cc.Hair[k].transform, HS2Next.Save.PosePart.Hair, k, null, p);
            }
            for (int k = 0; k < cc.Clothes.Length; k++)
            {
                string p = HS2Next.Save.PoseAddressGrammar.PartText(HS2Next.Save.PosePart.Clothes, k, null);
                if ((part == null || part == p) && cc.Clothes[k]) Part(rows, cc.Clothes[k].transform, HS2Next.Save.PosePart.Clothes, k, null, p);
            }
            if (cc.Accessories != null)
                foreach (var s in cc.Accessories)
                {
                    if (s == null || !s.Object) continue;
                    string id = r.AccessoryIdOf(s);
                    if (id == null) continue;
                    string p = HS2Next.Save.PoseAddressGrammar.PartText(HS2Next.Save.PosePart.Accessory, -1, id);
                    if (part == null || part == p) Part(rows, s.Object.transform, HS2Next.Save.PosePart.Accessory, -1, id, p);
                }
            return rows;
        }

        // the bones a bare name reaches, under this root: parent = the nearest ancestor row
        static void Named(CharaRealization r, List<PoseBoneRow> rows, Transform top, string part, Transform skip)
        {
            void Go(Transform t, string parent, int depth)
            {
                if (t == skip || SceneNodeHolder.IsHolder(t)) return;
                string here = parent;
                int d = depth;
                var a = PoseAddress.Bone(t.name);
                if (Resolve(r, a) == t)
                {
                    rows.Add(new PoseBoneRow { Address = a.Text, Bone = t, Part = part, Parent = parent, Depth = depth });
                    here = a.Text;
                    d = depth + 1;
                }
                for (int i = 0; i < t.childCount; i++) Go(t.GetChild(i), here, d);
            }
            Go(top, null, 0);
        }

        static void Part(List<PoseBoneRow> rows, Transform top, HS2Next.Save.PosePart kind, int slot, string id, string part)
        {
            var seen = new Dictionary<string, int>();
            void Go(Transform t, string parent, int depth)
            {
                seen.TryGetValue(t.name, out int n);
                seen[t.name] = n + 1;
                var a = PoseAddress.Bone(kind, slot, id, t.name, n);
                string text = a.Text;
                rows.Add(new PoseBoneRow { Address = text, Bone = t, Part = part, Parent = parent, Depth = depth });
                for (int i = 0; i < t.childCount; i++)
                {
                    var c = t.GetChild(i);
                    if (!SceneNodeHolder.IsHolder(c)) Go(c, text, depth + 1);
                }
            }
            Go(top, null, 0);
        }

        // ------------------------------------------------------------ physics

        /// <summary>The bones the swing chains of a character write (empty when it has none).</summary>
        public static HashSet<Transform> ChainBones(CharaRealization r)
        {
            var set = new HashSet<Transform>();
            var dyn = r?.Motion ? r.Motion.Get<DynamicBoneStage>() : null;
            if (dyn == null) return set;
            foreach (var c in dyn.Chains)
                for (int i = 0; i < c.ParticleCount; i++)
                {
                    var t = c.ParticleTransform(i);
                    if (t) set.Add(t);
                }
            return set;
        }
    }
}
