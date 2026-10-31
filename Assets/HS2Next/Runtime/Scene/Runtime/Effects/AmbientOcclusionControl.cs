// HS2's AO block (E8; Phase 3.3 package B). Fix round: HS2's MSVO is a wide,
// multi-scale, depth-only obscurance (package H's ao_* captures: a third of the stage darkens, around objects on the floor,
// convex shapes untouched). URP's SSAO renderer feature reached the amount and the darkened area after a refit (radius 8
// units, intensity 1.6·i^1.15) but not the pattern (it darkened the spheres and the character: |dA − dB| ≈ HS2's own delta),
// so the block now drives HS2Next's own pass, HS2AoFeature (Runtime/Rendering/Passes/HS2AoFeature.cs, shader
// HS2Next/Hidden/HS2Ao; E8's alternative). URP's SSAO feature, when the renderer asset has one, is switched off at run time
// (its asset entry stays active: URP strips SSAO's resources from a player when no SSAO feature is active at build time and
// the player then cannot create its renderer) and restored on release.
// The control finds HS2AoFeature in the active renderer or adds a run-time instance (never saved, removed on release), and
// writes HS2AoFeature.Inputs from the mapping:
//   active     enabled and intensity > 0
//   radius     RadiusFit (fraction of the frame height; scales ×1, ×2, ×4, ×8)
//   intensity  IntensityFit · i^IntensityPower (the exponent of the visibility)
//   thickness  ThicknessFit (the pair clamp); HS2's thickness modifier enters as radius × (1 + 0.1 (t − 2)) and
//              intensity × (1 + 0.05 (t − 2))
//   fitted on H's four ao_* cases (linear, darkened area / mean ratio; HS2 → HS2Next): 0.2/2 22 % 0.917 → 18 % 0.916,
//   0.2/8 25 % 0.893 → 22 % 0.895, 1/2 32 % 0.745 → 26 % 0.744, 1/8 35 % 0.691 → 31 % 0.694. H's comparison (|dA − dB| over
//   the changed pixels vs the approximate gate): 0.2/2 and 0.2/8 PASS, 1/2 7.84 / 6, 1/8 9.72 / 7.13 FAIL (URP SSAO after its
//   refit: 19.8 and 26.2; the first version: 6.6–26 with almost no darkening) — the rest is HS2's broad far-floor darkening
//   and a slight darkening of the character that this pass does not reproduce
//   colour: black is exact (the pass darkens towards black); other colours are ignored (finding effects.aoColorIgnored)
// Fit constants in the code with their measurement; EffectFit overrides them in the Editor while fitting.
using System;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    public struct AoParams
    {
        public bool Active;
        /// <summary>Exponent of the visibility.</summary>
        public float Intensity;
        /// <summary>Base radius as a fraction of the frame height.</summary>
        public float Radius;
        /// <summary>Occluder thickness in units of a scale's world size.</summary>
        public float Thickness;
        public Vector4 ScaleWeights;
    }

    public sealed class AmbientOcclusionControl
    {
        public const float RadiusFit = 0.04f;
        public const float IntensityFit = 1.6f;
        public const float IntensityPower = 1f;
        public const float ThicknessFit = 1.2f;
        public const float ThicknessSlope = 0f;
        /// <summary>Radius × (1 + ThicknessRadius (t − 2)), intensity × (1 + ThicknessIntensity (t − 2)): HS2's thickness modifier darkens more and further.</summary>
        public const float ThicknessRadius = 0.1f, ThicknessIntensity = 0.05f;
        public static readonly Vector4 ScaleWeightsFit = new Vector4(1f, 1f, 1f, 1f);

        /// <summary>HS2's AO values → the pass's inputs (pure; EffectsTest EF4).</summary>
        public static AoParams Map(bool enabled, float intensity, float thickness)
        {
            float rf = EffectFit.Get("ao.radius", RadiusFit);
            float iFit = EffectFit.Get("ao.intensity", IntensityFit), iPow = EffectFit.Get("ao.intensityPower", IntensityPower);
            float tFit = EffectFit.Get("ao.thickness", ThicknessFit), slope = EffectFit.Get("ao.thicknessSlope", ThicknessSlope);
            float tr = EffectFit.Get("ao.thicknessRadius", ThicknessRadius), ti = EffectFit.Get("ao.thicknessIntensity", ThicknessIntensity);
            float dt = thickness - 2f;
            var w = new Vector4(EffectFit.Get("ao.w0", ScaleWeightsFit.x), EffectFit.Get("ao.w1", ScaleWeightsFit.y), EffectFit.Get("ao.w2", ScaleWeightsFit.z), EffectFit.Get("ao.w3", ScaleWeightsFit.w));
            return new AoParams
            {
                Active = enabled && intensity > 0f,
                Intensity = Mathf.Max(0f, iFit * Mathf.Pow(Mathf.Max(0f, intensity), iPow) * Mathf.Max(0.1f, 1f + ti * dt)),
                Radius = rf * Mathf.Max(0.1f, 1f + tr * dt),
                Thickness = Mathf.Max(0.05f, tFit * (1f + slope * dt)),
                ScaleWeights = w,
            };
        }

        HS2AoFeature feature;
        bool runtimeFeature;
        ScriptableRendererData featureData;
        ScreenSpaceAmbientOcclusion ssao;
        bool ssaoWasActive, ssaoTouched;

        /// <summary>"asset" (the renderer asset's HS2AoFeature), "runtime" (added by this control), "none" (no URP renderer).</summary>
        public string Source => feature ? (runtimeFeature ? "runtime" : "asset") : "none";
        public HS2AoFeature Feature => feature;
        public AoParams Applied { get; private set; }

        static UniversalRendererData ActiveRendererData()
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset asset) || asset.rendererDataList.Length == 0) return null;
            return asset.rendererDataList[0] as UniversalRendererData;
        }

        /// <summary>The SSAO feature of the active pipeline's renderers (the first one); null when there is none.</summary>
        public static ScreenSpaceAmbientOcclusion FindFeature()
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset asset)) return null;
            foreach (var rd in asset.rendererDataList)
                if (rd && rd.TryGetRendererFeature<ScreenSpaceAmbientOcclusion>(out var f)) return f;
            return null;
        }

        void Ensure()
        {
            if (feature) return;
            var rd = ActiveRendererData();
            if (!rd) return;
            if (rd.TryGetRendererFeature<HS2AoFeature>(out var f)) { feature = f; runtimeFeature = false; featureData = rd; }
            else
            {
                f = ScriptableObject.CreateInstance<HS2AoFeature>();
                f.name = "HS2AoFeature (runtime)";
                f.hideFlags = HideFlags.DontSave;
                f.SetActive(true);
                rd.rendererFeatures.Add(f);
                rd.SetDirty();
                feature = f;
                runtimeFeature = true;
                featureData = rd;
            }
            // URP's SSAO stays in the asset (resources) but does not draw: HS2's AO is HS2AoFeature's
            ssao = FindFeature();
            if (ssao && !ssaoTouched)
            {
                ssaoWasActive = ssao.isActive;
                ssaoTouched = true;
                if (ssao.isActive) { ssao.SetActive(false); rd.SetDirty(); }
            }
        }

        /// <summary>Writes the pass inputs (adds the run-time feature on first use); false when there is no URP renderer.</summary>
        public bool Apply(AoParams p)
        {
            Ensure();
            Applied = p;
            HS2AoFeature.Inputs = new HS2AoFeature.Input { Active = p.Active, Radius = p.Radius, Intensity = p.Intensity, Thickness = p.Thickness, ScaleWeights = p.ScaleWeights };
            return feature;
        }

        /// <summary>Reads an SSAO feature's intensity, radius, after-opaque and source (diagnostics, tests of the JSON route).</summary>
        public static bool TryRead(ScreenSpaceAmbientOcclusion f, out float intensity, out float radius, out bool afterOpaque, out int source)
        {
            intensity = radius = 0f; afterOpaque = false; source = -1;
            if (!f) return false;
            string json = JsonUtility.ToJson(f);
            bool ok = TryGet(json, "Intensity", out string a) & TryGet(json, "Radius", out string b) & TryGet(json, "AfterOpaque", out string c) & TryGet(json, "Source", out string d);
            if (!ok) return false;
            intensity = float.Parse(a, System.Globalization.CultureInfo.InvariantCulture);
            radius = float.Parse(b, System.Globalization.CultureInfo.InvariantCulture);
            afterOpaque = c == "true";
            source = int.Parse(d, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        static bool TryGet(string json, string name, out string value)
        {
            var m = Regex.Match(json, "\"" + name + "\"\\s*:\\s*(-?[0-9.eE+\\-]+|true|false)");
            value = m.Success ? m.Groups[1].Value : null;
            return m.Success;
        }

        /// <summary>Removes the run-time feature, restores URP's SSAO feature and clears the inputs.</summary>
        public void Release()
        {
            HS2AoFeature.Inputs = default;
            if (ssao && ssaoTouched) { ssao.SetActive(ssaoWasActive); if (featureData) featureData.SetDirty(); }
            ssaoTouched = false;
            ssao = null;
            if (feature && runtimeFeature && featureData)
            {
                featureData.rendererFeatures.Remove(feature);
                featureData.SetDirty();
                if (Application.isPlaying) Object.Destroy(feature); else Object.DestroyImmediate(feature);
            }
            feature = null;
            runtimeFeature = false;
            featureData = null;
        }
    }
}
