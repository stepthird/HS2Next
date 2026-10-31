// HS2's bloom block (PPv2 Bloom: intensity, threshold, soft knee, clamp flag, diffusion, colour) mapped onto URP's Bloom
// override (Phase 3.3 package B). A pure function, unit-tested
// (EffectsTest EF4). PPv2's behaviour [code, the installed runtime read for behaviour]: threshold and clamp converted from
// gamma to linear; knee kn = t·k, prefilter max(soft, b − t) with soft = clamp(b − t + kn, 0, 2kn)² / 4kn; the pyramid has
// floor(log2(max(w/2, h/2)) + min(diffusion, 10) − 10) levels and each up-sample adds the level below; the result is scaled by
// 2^(intensity/10) − 1 and the linear colour. URP [package source]: threshold from gamma with the knee fixed at t/2, a lerp
// chain by scatter (energy kept), the tint normalised by its luminance, intensity linear.
// The mapping (fix round, fitted on package H's bloom_* captures, Cache/compare/effects vs Cache/compare_hs2next/effects):
//   clamp      the flag sets PPv2's clamp override (value 1, linear 1); off → 65472 — exact
//   threshold  t_u = t · (1 − KneeSlope d − KneeCurve d²), d = k − 0.5, in linear terms: at k = 0.5 PPv2's knee equals URP's
//              and the curves are identical; KneeSlope 1.95 / KneeCurve −1.58 fitted on H's captures (factor 0.60 at the
//              default knee 0.76, 0.42 at knee 1) (the onset matching of the first version overshot: 1.9× at
//              the defaults, 6.1× at knee 1; an equal-energy match for an exponential brightness distribution undershot
//              knee 1 at 0.21×)
//   iterations PPv2's level count at the frame size (the view's), clamped to URP's 2–8
//   scatter    fixed ScatterFit (URP remaps it to 0.05–0.95)
//   intensity  (2^(I/10) − 1) · levels · Scale · max(colour r, g, b); the tint is the colour with URP's luminance normalisation
//              kept: HS2's coloured bloom (1, .5, .25) adds 1.36× its grey-191 default; the first version multiplied the
//              luminance back and gave 0.73×; normalised tint × the brightest gamma channel gives ≈ 1.37× on H's stage
// Fit constants: Scale, KneeSlope, KneeCurve, ScatterFit (EffectFit overrides them in the Editor while fitting).
using System;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public struct BloomParams
    {
        public bool Active;
        public float Threshold, Intensity, Scatter, Clamp;
        public Color Tint;
        public int MaxIterations;
        /// <summary>PPv2's level count at the frame size (diagnostics).</summary>
        public int Levels;
        /// <summary>URP's linear threshold (diagnostics).</summary>
        public float ThresholdLinear;
        public bool HighQuality;
    }

    public static class BloomMapping
    {
        /// <summary>URP intensity per PPv2 level and unit of 2^(I/10) − 1 (fit on H's bloom_* captures).</summary>
        public const float Scale = 0.27f;
        /// <summary>Slope of URP's threshold factor over PPv2's knee (see <see cref="MatchThreshold"/>).</summary>
        public const float KneeSlope = 1.95f;
        /// <summary>Curvature of the threshold factor over the knee (negative: the factor flattens towards k = 1).</summary>
        public const float KneeCurve = -1.58f;
        /// <summary>URP scatter (before its 0.05–0.95 remap) standing in for PPv2's summed levels.</summary>
        public const float ScatterFit = 0.78f;
        /// <summary>URP's clamp default and PPv2's (65472, half's largest finite value).</summary>
        public const float NoClamp = 65472f;

        /// <summary>Rec.709 luminance of a linear colour.</summary>
        public static float Luminance(Color linear) => linear.r * 0.2126729f + linear.g * 0.7151522f + linear.b * 0.0721750f;

        /// <summary>PPv2's pyramid level count for a frame of <paramref name="width"/> × <paramref name="height"/>.</summary>
        public static int Levels(int width, int height, float diffusion)
        {
            int hw = Math.Max(1, width / 2), hh = Math.Max(1, height / 2);
            float n = Mathf.Log(Mathf.Max(hw, hh), 2f) + Mathf.Min(diffusion, 10f) - 10f;
            return Mathf.Clamp(Mathf.FloorToInt(n), 1, 16);
        }

        /// <summary>
        /// URP's linear threshold for PPv2's (linear threshold <paramref name="t"/>, knee <paramref name="k"/>): t · (1 − KneeSlope d − KneeCurve d²), d = k − 0.5,
        /// floored at 5 % of t. At k = 0.5 PPv2's knee t·k equals URP's fixed t/2 and the curves are identical; a softer PPv2
        /// knee passes more light below t (URP's threshold goes down), a harder one less (up). KneeSlope is the fit on H's
        /// captures (default k 0.76 and k 1 at t 1, k 0.76 at t 0.5; k 0 extrapolated).
        /// </summary>
        public static float MatchThreshold(float t, float k, float slope, float curve = KneeCurve)
        {
            if (t <= 0f) return 0f;
            float d = Mathf.Clamp01(k) - 0.5f;
            return t * Mathf.Max(0.05f, 1f - slope * d - curve * d * d);
        }

        /// <param name="color">HS2's bloom colour as stored (gamma bytes / 255).</param>
        /// <param name="width">The frame width the levels are counted for (the view's; 1920 when unknown).</param>
        public static BloomParams Map(bool enabled, float intensity, float threshold, float softKnee, bool clamp, float diffusion, Color color, int width = 1920, int height = 1080)
        {
            float scale = EffectFit.Get("bloom.scale", Scale), slope = EffectFit.Get("bloom.kneeSlope", KneeSlope);
            float tLin = Mathf.GammaToLinearSpace(Mathf.Max(0f, threshold));
            float tu = MatchThreshold(tLin, softKnee, slope, EffectFit.Get("bloom.kneeCurve", KneeCurve));
            int levels = Levels(width, height, diffusion);
            float gain = Mathf.Pow(2f, intensity / 10f) - 1f;
            return new BloomParams
            {
                Active = enabled && gain > 0f,
                Clamp = clamp ? 1f : NoClamp,
                Tint = new Color(color.r, color.g, color.b, 1f),
                Intensity = Mathf.Max(0f, gain * levels * scale * Mathf.Max(color.r, Mathf.Max(color.g, color.b))),
                Threshold = Mathf.LinearToGammaSpace(tu),
                ThresholdLinear = tu,
                Scatter = EffectFit.Get("bloom.scatter", ScatterFit),
                MaxIterations = Mathf.Clamp(levels, 2, 8),
                Levels = levels,
                HighQuality = true,
            };
        }
    }
}
