// Culling masks → URP rendering layers. HS2 filters lights per object by the
// light's culling mask against the object's layer (Built-in). URP's Forward+ path ignores culling masks; with "Use
// Rendering Layers" on (the app's asset, Assets/HS2Next/Settings/HS2NextURP.asset) a light reaches a renderer when its
// rendering layers and the renderer's renderingLayerMask share a bit. The identity mapping keeps HS2's rule exactly:
//   renderer.renderingLayerMask = 1 << gameObject.layer
//   light rendering layers      = light.cullingMask (UniversalAdditionalLightData.renderingLayers; shadows the same mask,
//                                 customShadowLayers off, so Light.renderingLayerMask filters the shadow casters alike)
// Rules: every renderer that comes from a bundle goes through ShaderMap.ApplyUrp (which calls ApplyToRenderers), every
// Light in the app goes through ApplyToLight (BundleLights.Prepare for item and map lights, the Studio light and the chara
// light realizations, thumbnails) — a Light that skips it keeps URP's default rendering layers (Default only) and lights
// nothing on other layers. Call ApplyToLight again after changing a culling mask, ApplyToRenderers after moving
// renderers to another layer for good (temporary moves, e.g. CharaThumbnail's isolation layer, need nothing). Under an
// asset without rendering layers (the measurement asset of the render tests) the values are unused.
// The 32 rendering-layer names (ProjectSettings/TagManager.asset) are HS2 Studio's GameObject layer names; URP masks a
// light's layers with the defined names, so all 32 must stay defined.
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HS2Next
{
    public static class RenderingLayers
    {
        /// <summary>HS2 Studio's GameObject layers used by the light rules.</summary>
        public const int Default = 0, Chara = 10, Map = 11, MapNoShadow = 12;

        /// <summary>
        /// The culling mask of HS2 Studio's chara light (`Cam Light`): layers 1, 2, 5, 8, 10 Chara, 12 MapNoShadow, 13–15,
        /// 19–23, 29–31; not 0 Default and not 11 Map (read from the game's Studio scene).
        /// </summary>
        public const int CharaLightCullingMask =
            1 << 1 | 1 << 2 | 1 << 5 | 1 << 8 | 1 << Chara | 1 << MapNoShadow | 1 << 13 | 1 << 14 | 1 << 15 |
            1 << 19 | 1 << 20 | 1 << 21 | 1 << 22 | 1 << 23 | 1 << 29 | 1 << 30 | 1 << 31;

        /// <summary>The rendering-layer mask of a GameObject layer (identity: bit <paramref name="layer"/>).</summary>
        public static uint ForLayer(int layer) => 1u << (layer & 31);

        /// <summary>The rendering layers of a light with this culling mask (identity).</summary>
        public static uint ForCullingMask(int cullingMask) => unchecked((uint)cullingMask);

        /// <summary>
        /// Sets renderingLayerMask = 1 &lt;&lt; layer on every renderer and terrain under <paramref name="root"/> (inactive
        /// included; idempotent); returns the count.
        /// </summary>
        public static int ApplyToRenderers(GameObject root)
        {
            if (!root) return 0;
            int n = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!r) continue;
                uint m = ForLayer(r.gameObject.layer);
                if (r.renderingLayerMask != m) r.renderingLayerMask = m;
                n++;
            }
            foreach (var t in root.GetComponentsInChildren<Terrain>(true))
            {
                if (!t) continue;
                uint m = ForLayer(t.gameObject.layer);
                if (t.renderingLayerMask != m) t.renderingLayerMask = m;
                n++;
            }
            return n;
        }

        /// <summary>
        /// Gives the light rendering layers = its culling mask (light and shadows; adds URP's light data when missing).
        /// </summary>
        public static void ApplyToLight(Light light)
        {
            if (!light) return;
            if (!light.TryGetComponent<UniversalAdditionalLightData>(out var data)) data = light.gameObject.AddComponent<UniversalAdditionalLightData>();
            uint m = ForCullingMask(light.cullingMask);
            if (data.customShadowLayers) data.customShadowLayers = false;
            data.renderingLayers = unchecked((int)m);
            data.shadowRenderingLayers = unchecked((int)m);
            // The engine-side mask filters shadow casters; URP syncs it only when the value above changes.
            if (light.renderingLayerMask != unchecked((int)m)) light.renderingLayerMask = unchecked((int)m);
        }

        /// <summary><see cref="ApplyToLight"/> on every Light under <paramref name="root"/> (inactive included); returns the count.</summary>
        public static int ApplyToLights(GameObject root)
        {
            if (!root) return 0;
            int n = 0;
            foreach (var l in root.GetComponentsInChildren<Light>(true)) { ApplyToLight(l); n++; }
            return n;
        }

        /// <summary>True when the current pipeline asset filters lights by rendering layers.</summary>
        public static bool Active => GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset u && u.useRenderingLayers;
    }
}
