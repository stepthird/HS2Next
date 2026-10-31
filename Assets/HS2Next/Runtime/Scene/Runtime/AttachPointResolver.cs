// Attach points of a built character: point name → transform, by the
// assembler's own accessory-anchor lookup (CharaAssembler.LoadAccessory): the first transform of that name under
// p_cf_anim, else under the head bone. Cached per character; a name found on neither is remembered as missing.
using System.Collections.Generic;
using HS2Next.Chara;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed class AttachPointResolver
    {
        readonly CharaContext ctx;
        readonly Dictionary<string, Transform> cache = new Dictionary<string, Transform>(System.StringComparer.Ordinal);

        public AttachPointResolver(CharaContext ctx) { this.ctx = ctx; }

        /// <summary>The transform of point <paramref name="name"/> on this body, or null when the body has none.</summary>
        public Transform this[string name]
        {
            get
            {
                if (string.IsNullOrEmpty(name) || ctx == null) return null;
                if (cache.TryGetValue(name, out var t) && (t || t is null)) return t;
                t = (ctx.Anim ? Find(ctx.Anim.transform, name) : null)
                    ?? (ctx.HeadBone ? Find(ctx.HeadBone.transform, name) : null);
                cache[name] = t ? t : null;
                return t ? t : null;
            }
        }

        // CharaAssembler.Find (depth first, by exact name), not entering scene node holders attached to the body.
        static Transform Find(Transform t, string name)
        {
            if (t == null) return null;
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (c.GetComponent<SceneNodeHolder>() != null) continue;
                var r = Find(c, name);
                if (r) return r;
            }
            return null;
        }

        /// <summary>Forgets the cached lookups (after the hierarchy changed).</summary>
        public void Clear() => cache.Clear();
    }
}
