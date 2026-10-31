// Marker on every scene node holder GameObject: the GameObject that carries a
// node's transform and under which its content (character, item prefab, light prefab) sits at local identity.
// It carries no logic. Phase 1 code that scans a character's hierarchy stops at it, so scene children
// attached under the character's bones are never treated as parts of the character.
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    [DisallowMultipleComponent]
    public sealed class SceneNodeHolder : MonoBehaviour
    {
        /// <summary>Id of the scene node (32 lowercase hex).</summary>
        public string NodeId;

        /// <summary>True when <paramref name="t"/> carries a holder (it is the root of another node's realization).</summary>
        public static bool IsHolder(Transform t) => t != null && t.GetComponent<SceneNodeHolder>() != null;
    }
}
