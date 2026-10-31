// Lighting conversion of a loaded map (post-load step 5): BundleLights.Prepare(root,
// fold: true) on every root, inactive objects included: URP light data, the original colour / intensity recorded once
// (BundleLightOriginal), colour = original × intensity (rgb, unclamped), intensity = 1, and the rendering layers = the
// culling mask (RenderingLayers.ApplyToLight, light and shadow side). Shadows, cookies, range, bake state stay as serialized;
// baked-only and area lights are left to Unity (they light through the lightmaps). The renderers' and terrains' rendering
// layers come from ShaderMap.ApplyUrp (MapMaterials.Apply).
// Counts for the bridge and findings: map.manyShadowedLights (> 32 active shadowed point / spot lights that light at run
// time; URP's additional-light shadow atlas drops what does not fit).
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public static class MapLighting
    {
        public const int ManyShadowedLights = 32;

        public struct Result
        {
            public int Lights, Active, Folded, AddedLightData;
            /// <summary>Active lights with shadows; of them point / spot and directional (realtime only).</summary>
            public int Shadowed, ShadowedPointSpot, ShadowedDirectional;
            public int BakedOnly, Area, Cookies;
        }

        public static Result Apply(IReadOnlyList<GameObject> roots, IReadOnlyList<Light> lights)
        {
            var r = new Result();
            if (roots != null)
                foreach (var g in roots)
                {
                    if (!g) continue;
                    var p = BundleLights.Prepare(g, fold: true);
                    r.Lights += p.Lights;
                    r.Folded += p.Folded;
                    r.AddedLightData += p.AddedLightData;
                }
            Count(lights, ref r);
            return r;
        }

        /// <summary>Recounts the activity-dependent numbers (after a block change).</summary>
        public static void Count(IReadOnlyList<Light> lights, ref Result r)
        {
            r.Active = r.Shadowed = r.ShadowedPointSpot = r.ShadowedDirectional = r.BakedOnly = r.Area = r.Cookies = 0;
            if (lights == null) return;
            foreach (var l in lights)
            {
                if (!l) continue;
                bool area = l.type == LightType.Rectangle || l.type == LightType.Disc;
                if (area) r.Area++;
                bool baked = MainLight.IsBakedOnly(l);
                if (baked) r.BakedOnly++;
                if (!l.enabled || !l.gameObject.activeInHierarchy) continue;
                r.Active++;
                if (l.cookie) r.Cookies++;
                if (l.shadows == LightShadows.None || baked || area) continue;
                r.Shadowed++;
                if (l.type == LightType.Directional) r.ShadowedDirectional++;
                else r.ShadowedPointSpot++;
            }
        }
    }
}
