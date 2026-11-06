// The bones of a character's hair: the transform N_hair_Root under the head and every transform under it, without what scene nodes hold
// there. A hair bone adjustment edits an entry of the character's bone modifiers on one of these. Beside them, the bones of the
// accessories: an entry without a location goes on to those when the body lacks its bone, so such an entry acts there and is not lost.
using System.Collections.Generic;
using HS2Next.Chara;
using HS2Next.Chara.Edit;
using HS2Next.Chara.Ext;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public static class HairBones
    {
        /// <summary>The transform the class starts from.</summary>
        public const string RootName = "N_hair_Root";

        /// <summary>The class in hierarchy order, the root first; empty without a head.</summary>
        public static List<Transform> Of(CharaContext ctx)
        {
            var list = new List<Transform>();
            var root = ctx?.HeadBone ? EditContext.Find(ctx.HeadBone.transform, RootName) : null;
            if (root) Collect(root, list);
            return list;
        }

        /// <summary>The names of the class.</summary>
        public static HashSet<string> Names(CharaContext ctx)
        {
            var names = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var t in Of(ctx)) names.Add(t.name);
            return names;
        }

        /// <summary>The first bone of the class with this name, or null.</summary>
        public static Transform Find(CharaContext ctx, string name)
        {
            foreach (var t in Of(ctx)) if (t.name == name) return t;
            return null;
        }

        /// <summary>The transforms of the accessories by name, where an entry without a location looks after the body: the slots of HS2's
        /// own accessories. Ask with <see cref="AbmxApplier.Find"/>, which knows the names a hair bone goes by.</summary>
        public static Dictionary<string, Transform> AccessoryBones(CharaContext ctx)
        {
            var bones = new Dictionary<string, Transform>(System.StringComparer.Ordinal);
            for (int slot = 0; ctx != null && slot < AccessorySlotRule.BaseSlots; slot++)
            {
                var go = ctx.AccessoryObject(slot);
                if (go) Collect(go.transform, bones);
            }
            return bones;
        }

        static void Collect(Transform t, Dictionary<string, Transform> bones)
        {
            if (!bones.ContainsKey(t.name)) bones[t.name] = t;
            for (int i = 0; i < t.childCount; i++) Collect(t.GetChild(i), bones);
        }

        static void Collect(Transform t, List<Transform> list)
        {
            list.Add(t);
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (!SceneNodeHolder.IsHolder(c)) Collect(c, list);
            }
        }
    }
}
