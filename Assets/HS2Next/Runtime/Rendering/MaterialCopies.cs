// Material copies and their keywords (bundle materials are shared by every character that uses the asset;
// they are never written, a character gets copies and every change goes to a copy).
// Keywords: the Unity Editor changes material keywords on its own. The first property access of a loaded material
// (HasProperty, GetFloat, GetTexture, GetTexturePropertyNameIDs, ...), a shader assignment and every new Material(source)
// run the shader's material property drawers ([Toggle], [MaterialToggle], [ToggleOff], [KeywordEnum]:
// UnityEditor.MaterialEditor.ApplyMaterialPropertyDrawers, called from native code), which switch the drawers' keywords
// to match the float values (e.g. a mod glove with _DitherSwitch = 1 but without _DITHERSWITCH_ON gains the keyword and
// dithers). HS2 (a Unity 2018 player) and the HS2Next player never run drawers: their keywords are the serialized ones.
// So in the Editor every copy had other keywords than in the game, and a bundle material left on a renderer changed its
// keywords the first time anything read its properties (another character's build, the texture guard, a test reading
// values): the same card built with other keywords depending on what had been built before.
// Here the catalog records each loaded material's keywords right after the load, before anything reads its properties
// (Capture, from Runtime/Chara/TextureGuard.cs Load); Copy and SetShader keep those keywords (the drawers' changes are
// undone), so copies and shader swaps carry the game's keywords in the Editor as in the player, whatever touched the
// shared material before. A material that was not recorded (made at run time, or loaded outside the catalog) keeps the
// keywords it has when it is copied. KeepSerializedKeywords = false gives the Phase 1 behaviour (Editor: drawer keywords on
// copies and after shader assignments), for measurements only.
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next
{
    public static class MaterialCopies
    {
        /// <summary>Copies and shader assignments keep the loaded keywords (default). false: plain new Material / shader
        /// assignment (in the Editor the property drawers then switch keywords; Phase 1 behaviour).</summary>
        public static bool KeepSerializedKeywords = true;

        static readonly Dictionary<EntityId, string[]> loaded = new Dictionary<EntityId, string[]>();

        /// <summary>Records the keywords of the materials an asset brings (a material, or every material of a prefab's
        /// renderers), once per material. Call right after the load, before anything reads the materials' properties.</summary>
        public static void Capture(Object asset)
        {
            switch (asset)
            {
                case Material m: Record(m); break;
                case GameObject go:
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                        foreach (var m in r.sharedMaterials) Record(m);
                    break;
            }
        }

        static void Record(Material m)
        {
            if (!m) return;
            var id = m.GetEntityId();
            if (!loaded.ContainsKey(id)) loaded[id] = m.shaderKeywords;   // reading keywords does not run the drawers
        }

        /// <summary>True when the catalog recorded the material at its load (a bundle material).</summary>
        public static bool IsRecorded(Material m) => m && loaded.ContainsKey(m.GetEntityId());

        /// <summary>The keywords the material had when it was loaded, else (not recorded) its current keywords.</summary>
        public static string[] KeywordsOf(Material m)
        {
            if (!m) return null;
            return loaded.TryGetValue(m.GetEntityId(), out var k) ? (string[])k.Clone() : m.shaderKeywords;
        }

        /// <summary>A copy of <paramref name="source"/> with the source's loaded keywords; <paramref name="name"/> null keeps the source's name.</summary>
        public static Material Copy(Material source, string name = null)
        {
            if (!source) return null;
            var kw = KeepSerializedKeywords ? KeywordsOf(source) : null;
            var c = new Material(source);
            if (name != null) c.name = name;
            if (kw != null) c.shaderKeywords = kw;
            return c;
        }

        /// <summary>Assigns <paramref name="shader"/> and keeps the material's keyword names, as a player does (the Editor
        /// would apply the new shader's property drawers).</summary>
        public static void SetShader(Material m, Shader shader)
        {
            if (!m) return;
            var kw = KeepSerializedKeywords ? m.shaderKeywords : null;
            m.shader = shader;
            if (kw != null) m.shaderKeywords = kw;
        }

        /// <summary>Forgets the records (the catalog unloaded its bundles).</summary>
        public static void Forget() => loaded.Clear();
    }
}
