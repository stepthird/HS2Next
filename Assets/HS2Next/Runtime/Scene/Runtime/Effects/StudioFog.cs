// HS2's fog block: the six values drive HS2Next's fog pass (HS2FogFeature). The Studio-owned RenderSettings values HS2
// writes — fogColor (the block's colour) and fogDensity — are written into the active scene (the map scene after the map's
// Loaded hook, the runtime's own scene otherwise; the fog mode and the linear distances stay the scene's, as in HS2), and
// the pass's inputs are published from the block and those RenderSettings (mode, density, start, end, the colour in linear
// space). RenderSettings.fog is kept false, also when a map scene brought fog on: the fog is drawn by HS2Next's own pass and
// the fitted transparent shaders' own term, so URP's keyword fog never runs (it would fog URP-native opaque surfaces a second
// time, and the player strips its variants).
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public static class StudioFog
    {
        /// <summary>Writes the fog colour and density of <paramref name="f"/>, switches RenderSettings.fog off and publishes
        /// the fog pass's inputs.</summary>
        public static void Apply(FogEffect f)
        {
            if (f == null) return;
            RenderSettings.fogColor = new Color(f.Color.R, f.Color.G, f.Color.B, f.Color.A);
            RenderSettings.fogDensity = f.Density;
            RenderSettings.fog = false;
            HS2FogFeature.Inputs = InputOf(f);
        }

        /// <summary>The fog pass's inputs for <paramref name="f"/> under the active scene's RenderSettings.</summary>
        public static HS2FogFeature.Input InputOf(FogEffect f)
        {
            if (f == null) return default;
            var c = new Color(f.Color.R, f.Color.G, f.Color.B, f.Color.A);
            return new HS2FogFeature.Input
            {
                Enabled = f.Enabled,
                Mode = (int)RenderSettings.fogMode,
                Density = f.Density,
                Start = RenderSettings.fogStartDistance,
                End = RenderSettings.fogEndDistance,
                Height = f.Height,
                HeightDensity = f.HeightDensity,
                ExcludeFarPixels = f.ExcludeFarPixels,
                ColorLinear = QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c,
            };
        }

        /// <summary>
        /// The material slots under <paramref name="root"/> (active, enabled renderers) that use a URP-native shader in the
        /// transparent queues: they draw without the forward fog (only the fitted shaders have it; finding fog.urpNativeTransparent).
        /// </summary>
        public static int UrpNativeTransparentCount(GameObject root)
        {
            if (!root) return 0;
            int n = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled) continue;
                foreach (var m in r.sharedMaterials)
                    if (m && m.shader && m.renderQueue > 2500 && m.shader.name.StartsWith("Universal Render Pipeline/", System.StringComparison.Ordinal)) n++;
            }
            return n;
        }

        /// <summary>No fog: the pass's inputs back to their default (off), as when no scene is shown.</summary>
        public static void Reset() => HS2FogFeature.Inputs = default;
    }
}
