// HS2's camera reflection probe (U11; Phase 3.3 package B): a ReflectionProbe on a
// child "Reflection Probe" of the view camera, as HS2's rig has one under its main camera — Custom mode, the Probe row's
// cubemap as customBakedTexture, intensity = the block's value, importance 10 (map probes have 1–2, so this one wins
// where it is enabled), box 999³ centred on the camera, blend distance 1, no box projection, resolution 128, HDR; the
// GameObject is active only while the block is enabled and a cubemap resolved (an unknown ref: no cubemap, finding
// effects.probeMissing, the probe stays off). It follows the camera (adopted app cameras too).
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    public sealed class CameraProbe
    {
        public const int Importance = 10;
        public static readonly Vector3 BoxSize = new Vector3(999f, 999f, 999f);

        public GameObject Root { get; private set; }
        public ReflectionProbe Probe { get; private set; }
        public Texture Cubemap { get; private set; }
        public string CubemapName => Cubemap ? Cubemap.name : null;
        public bool Active => Root && Root.activeSelf;

        void Ensure(Transform camera)
        {
            if (!Root)
            {
                Root = new GameObject("Reflection Probe");
                Root.SetActive(false);
                Probe = Root.AddComponent<ReflectionProbe>();
                Probe.mode = ReflectionProbeMode.Custom;
                Probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                Probe.importance = Importance;
                Probe.size = BoxSize;
                Probe.center = Vector3.zero;
                Probe.blendDistance = 1f;
                Probe.boxProjection = false;
                Probe.resolution = 128;
                Probe.hdr = true;
            }
            if (camera && Root.transform.parent != camera)
            {
                Root.transform.SetParent(camera, false);
                Root.transform.localPosition = Vector3.zero;
                Root.transform.localRotation = Quaternion.identity;
            }
        }

        /// <summary>Applies the block: <paramref name="cubemap"/> null = none (the probe stays off).</summary>
        public void Apply(Transform camera, bool enabled, Texture cubemap, float intensity)
        {
            Ensure(camera);
            Cubemap = cubemap;
            Probe.customBakedTexture = cubemap;
            Probe.intensity = intensity;
            bool on = enabled && cubemap;
            if (Root.activeSelf != on) Root.SetActive(on);
        }

        /// <summary>Keeps the probe under the view camera (an adopted camera can change).</summary>
        public void Follow(Transform camera)
        {
            if (Root && camera && Root.transform.parent != camera) Ensure(camera);
        }

        public void Destroy()
        {
            if (Root)
            {
                Root.SetActive(false);
                if (Application.isPlaying) Object.Destroy(Root); else Object.DestroyImmediate(Root);
            }
            Root = null;
            Probe = null;
            Cubemap = null;
        }
    }
}
