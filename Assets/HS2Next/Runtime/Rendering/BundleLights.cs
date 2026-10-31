// Lights that come out of HS2 bundles (item prefabs, map scenes).
//
//   1. URP's UniversalAdditionalLightData (its defaults) on every Light that lacks it, as the Editor's GI light
//      extraction adds it to every light and a player does not; URP reads it without a null check for an additional
//      directional light with a cookie (Phase 2.5: a NullReferenceException in LightCookieManager.GetLightUVScaleOffset
//      aborted the player's whole capture).
//   2. Folding (fold = true): HS2 renders with Built-in's non-linear light intensity
//      (lightsUseLinearIntensity = false), where the light's contribution is colour × intensity in gamma space; URP has
//      linear intensity. The original colour and intensity are recorded once per light (BundleLightOriginal on the
//      light's object, so a second Prepare is idempotent), then colour = original colour × original intensity (rgb,
//      not clamped, alpha 1) and intensity = 1.
//   3. Rendering layers (always, fold or not; Phase 3.2): light rendering layers = the light's culling mask
//      (RenderingLayers.ApplyToLight), so the Forward+ / rendering-layer pipeline filters as HS2's culling masks do.
//   Shadows, range, spot angle, cookies, culling masks and enabled state stay as the prefab says (the main-light rule is
//   Phase 3.3).
// Items fold (3.1); maps only get the light data until 3.2 switches them to folding too (T11).
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace HS2Next
{
    public static class BundleLights
    {
        /// <summary>Result of one <see cref="Prepare"/>.</summary>
        public struct Result
        {
            /// <summary>Lights under the root (inactive included).</summary>
            public int Lights;
            /// <summary>Lights that got UniversalAdditionalLightData.</summary>
            public int AddedLightData;
            /// <summary>Lights folded by this call (0 on a second call).</summary>
            public int Folded;
        }

        /// <summary>Prepares every Light under <paramref name="root"/> (inactive included); see the file comment.</summary>
        public static Result Prepare(GameObject root, bool fold = true)
        {
            var r = new Result();
            if (!root) return r;
            foreach (var l in root.GetComponentsInChildren<Light>(true))
            {
                r.Lights++;
                if (!l.TryGetComponent<UniversalAdditionalLightData>(out _))
                {
                    l.gameObject.AddComponent<UniversalAdditionalLightData>();
                    r.AddedLightData++;
                }
                RenderingLayers.ApplyToLight(l);
                if (!fold || l.TryGetComponent<BundleLightOriginal>(out _)) continue;
                var o = l.gameObject.AddComponent<BundleLightOriginal>();
                o.Light = l;
                o.Color = l.color;
                o.Intensity = l.intensity;
                l.color = Folded(o.Color, o.Intensity);
                l.intensity = 1f;
                r.Folded++;
            }
            return r;
        }

        /// <summary>The folded colour: rgb × intensity (not clamped), alpha 1.</summary>
        public static Color Folded(Color color, float intensity) => new Color(color.r * intensity, color.g * intensity, color.b * intensity, 1f);

        /// <summary>The recorded original of a folded light, or null.</summary>
        public static BundleLightOriginal OriginalOf(Light l) => l && l.TryGetComponent<BundleLightOriginal>(out var o) ? o : null;
    }
}
