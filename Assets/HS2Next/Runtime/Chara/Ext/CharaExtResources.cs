// Per-character owner of the references into ExtTextures (decoded plugin images that materials of the character
// use); released when the character root is destroyed (also in edit mode). Render textures bound to materials are
// owned like the assembler's own (released by whoever destroys the character's materials).
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Chara.Ext
{
    /// <summary>Holds a character's references into <see cref="ExtTextures"/> and releases them when the character is destroyed.</summary>
    [ExecuteAlways]
    public sealed class CharaExtResources : MonoBehaviour
    {
        public readonly List<string> TextureKeys = new List<string>();

        void OnDestroy()
        {
            foreach (var k in TextureKeys) ExtTextures.Release(k);
            TextureKeys.Clear();
        }

        public static CharaExtResources Of(GameObject root)
        {
            if (!root) return null;
            var c = root.GetComponent<CharaExtResources>();
            return c ? c : root.AddComponent<CharaExtResources>();
        }
    }
}
