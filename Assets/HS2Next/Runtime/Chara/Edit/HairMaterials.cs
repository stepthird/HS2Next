// The hair shader pass and the hair colours, shared by the hair parts and the hair-type accessories (Docs/
// item 2): the shader of the draw type (hair.shaderType 0 = the material hair_dithering, 1 =
// hair_cutout of chara/hair_shader_mat), the main texture of the row's TexD (0) / TexC (1) column and the colour
// properties the hair shaders read (_Color, _TopColor, _UnderColor, _Specular, _Metallic, _Smoothness). These helpers only
// write; which value a hair part or an accessory writes (the parity switches of the hair parts, the colour areas of an
// accessory) stays with the units. Unity-dependent.
using System.Collections.Generic;
using HS2Next.Content;
using UnityEngine;
using EM = HS2Next.Chara.Edit.EditMaterials;

namespace HS2Next.Chara.Edit
{
    public static class HairMaterials
    {
        /// <summary>The bundle holding the two hair materials whose shaders the draw types use.</summary>
        public const string ShaderBundle = "chara/hair_shader_mat.unity3d";

        /// <summary>The material whose shader a draw type uses: 0 hair_dithering, otherwise hair_cutout.</summary>
        public static string ShaderMaterial(int shaderType) => shaderType == 0 ? "hair_dithering" : "hair_cutout";

        /// <summary>The list column naming a row's main texture for a draw type: 0 TexD, otherwise TexC (bundle column TexAB).</summary>
        public static string TextureColumn(int shaderType) => shaderType == 0 ? "TexD" : "TexC";

        /// <summary>The hair shader of a draw type (null when the catalog or the material is missing).</summary>
        public static Shader ShaderOf(ContentCatalog catalog, int shaderType)
        {
            if (catalog == null) return null;
            var m = catalog.LoadAsset<Material>(ShaderBundle, ShaderMaterial(shaderType));
            return m != null ? m.shader : null;
        }

        /// <summary>
        /// The shader pass of one material: the shader (when given; the material keeps its keywords, as the game), the main
        /// texture (only when given: without one the material keeps its own), _Ringoff (only when <paramref name="ringOff"/>
        /// has a value and the shader declares it: the hair parts' RingOff column; accessories write none); the render queue
        /// stays the material's.
        /// </summary>
        public static void ApplyShader(Material m, Shader shader, Texture texture, bool? ringOff = null)
        {
            if (!m) return;
            int q = m.renderQueue;
            if (shader) MaterialCopies.SetShader(m, shader);
            if (texture) EM.SetTexture(m, "_MainTex", texture);
            if (ringOff.HasValue && m.HasProperty("_Ringoff")) m.SetInt("_Ringoff", ringOff.Value ? 1 : 0);
            m.renderQueue = q;
        }

        /// <summary>The shader pass of every material of <paramref name="materials"/> (null entries skipped).</summary>
        public static void ApplyShader(IEnumerable<Material> materials, Shader shader, Texture texture, bool? ringOff = null)
        {
            if (materials == null) return;
            foreach (var m in materials) ApplyShader(m, shader, texture, ringOff);
        }

        /// <summary>
        /// The hair colours of one material, in this order: _Color (when <paramref name="baseColor"/> has a value), _TopColor,
        /// _UnderColor (each when given), _Specular, _Metallic, _Smoothness. Values are written as given (the callers decide
        /// the alpha).
        /// </summary>
        public static void ApplyColours(Material m, Color? baseColor, Color? top, Color? under, Color specular, float metallic, float smoothness)
        {
            if (!m) return;
            if (baseColor.HasValue) EM.SetColor(m, "_Color", baseColor.Value);
            if (top.HasValue) EM.SetColor(m, "_TopColor", top.Value);
            if (under.HasValue) EM.SetColor(m, "_UnderColor", under.Value);
            EM.SetColor(m, "_Specular", specular);
            EM.SetFloat(m, "_Metallic", metallic);
            EM.SetFloat(m, "_Smoothness", smoothness);
        }
    }
}
