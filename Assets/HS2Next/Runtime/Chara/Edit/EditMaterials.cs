// Material writes of the edit units (G2). The URP/Lit fallback of ShaderMap copies
// `_MainTex` → `_BaseMap` and `_Color` / `_BaseColor` → `_BaseColor` (α = 1) when it swaps the shader, so a later write of
// `_MainTex` or `_Color` on a fallback material would have no visible effect. These writers mirror such writes onto the
// URP names when the material came from ShaderMap's fallback (ShaderMap.IsFallback); every other property, and every
// material that is not a fallback, is written as is. Fitted replacements declare the original property names,
// so they need no mapping. Units write only materials of the character (CharaContext.OwnedMaterials).
using UnityEngine;

namespace HS2Next.Chara.Edit
{
    public static class EditMaterials
    {
        public static void SetColor(Material m, string prop, Color c)
        {
            if (!m) return;
            m.SetColor(prop, c);
            // the fallback's _BaseColor came from the original's _Color only when the original had no _BaseColor
            // (read back as the fallback did: material colours are stored linear, so the value read differs by ulps)
            if (prop != "_Color" || !ShaderMap.IsFallback(m)) return;
            // the fallback wrote _Color back from the value it read (one more linear round trip than a direct write)
            var r = m.GetColor("_Color");
            m.SetColor("_Color", r);
            if (ShaderMap.FallbackColorFromColor(m)) m.SetColor("_BaseColor", new Color(r.r, r.g, r.b, 1));
        }

        public static void SetTexture(Material m, string prop, Texture t)
        {
            if (!m) return;
            m.SetTexture(prop, t);
            if (prop == "_MainTex" && ShaderMap.IsFallback(m) && m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
        }

        public static void SetFloat(Material m, string prop, float v)
        {
            if (m) m.SetFloat(prop, v);
        }

        public static void SetVector(Material m, string prop, Vector4 v)
        {
            if (m) m.SetVector(prop, v);
        }

        public static void SetInt(Material m, string prop, int v)
        {
            if (m) m.SetInt(prop, v);
        }

        /// <summary>The scale and offset of a texture property (Phase 4.3, the hair mesh pattern's `_MeshColorMask_B_ST`,
        /// 4), written only when the shader declares the property (Unity logs an error for a texture
        /// scale / offset of an unknown property).</summary>
        public static void SetTextureScaleOffset(Material m, string prop, Vector2 scale, Vector2 offset)
        {
            if (!m || !m.HasProperty(prop)) return;
            m.SetTextureScale(prop, scale);
            m.SetTextureOffset(prop, offset);
        }
    }
}
