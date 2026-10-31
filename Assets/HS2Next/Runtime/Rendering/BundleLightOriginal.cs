// The colour and intensity a bundle light had before BundleLights folded it: a data
// component on the light's object, so Prepare is idempotent and tools can report the prefab values.
using UnityEngine;

namespace HS2Next
{
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class BundleLightOriginal : MonoBehaviour
    {
        public Light Light;
        public Color Color = Color.white;
        public float Intensity = 1f;
    }
}
