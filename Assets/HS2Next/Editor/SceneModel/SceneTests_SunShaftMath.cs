// SS0: the sun-shaft arithmetic (SunShaftMath) against hand values - the sun position (centre without a caster, a caster
// point, behind the camera), the four blur steps, the prefilter (far pixels only, the threshold, the falloff edge), the
// taps, the composite and its saturation, the 8-bit sRGB storage, and a small whole-image run (border, symmetry, behind).
using System;

namespace HS2Next.Scenes.Tests
{
    using HS2Next.Save.Tests;
    using HS2Next.Scenes.Runtime;

    public static class SceneTests_SunShaftMath
    {
        public static void Run(TestRunner t)
        {
            Sun(t);
            Steps(t);
            Prefilter(t);
            TapsAndComposite(t);
            Storage(t);
            Image(t);
        }

        static bool Near(float a, float b, float eps = 1e-6f) => Math.Abs(a - b) <= eps;

        static void Sun(TestRunner t)
        {
            SunShaftMath.SunPosition(false, 0.1f, 0.2f, -3f, out float x, out float y, out bool behind);
            t.Check(x == 0.5f && y == 0.5f && !behind, "SS0 no caster: the screen centre, never behind");
            SunShaftMath.SunPosition(true, 0.3f, 0.7f, 5f, out x, out y, out behind);
            t.Check(x == 0.3f && y == 0.7f && !behind, "SS0 a caster in front: its viewport point");
            SunShaftMath.SunPosition(true, 0.3f, 0.7f, -0.01f, out _, out _, out behind);
            t.Check(behind, "SS0 a caster behind the camera (viewport z < 0)");
            SunShaftMath.SunPosition(true, 0.3f, 0.7f, 0f, out _, out _, out behind);
            t.Check(!behind, "SS0 viewport z = 0 is not behind");
        }

        static void Steps(TestRunner t)
        {
            float b = 5f;
            t.Check(Near(SunShaftMath.Step(0, b), 5f / 768f) && Near(SunShaftMath.Step(1, b), 30f / 768f) &&
                    Near(SunShaftMath.Step(2, b), 60f / 768f) && Near(SunShaftMath.Step(3, b), 90f / 768f),
                "SS0 the four blur steps at blur size 5: 5, 30, 60, 90 / 768");
            t.Check(SunShaftMath.StepFactors.Length == 4 && SunShaftMath.Taps == 6, "SS0 four passes of 6 taps");
        }

        static void Prefilter(TestRunner t)
        {
            float P(float c, float u, float v, float depth) =>
                SunShaftMath.Prefilter(c, c, c, 0.5f, 0.5f, 0.5f, u, v, 0.5f, 0.5f, 0.75f, depth);
            t.Check(Near(P(1f, 0.5f, 0.5f, 1f), 1.5f * 0.75f), "SS0 prefilter at the sun: (3 x 0.5) x 0.75");
            t.Check(P(1f, 0.5f, 0.5f, 0.5f) == 0f, "SS0 a near pixel gives 0");
            t.Check(P(1f, 0.5f, 0.5f, 0.99f) == 0f && P(1f, 0.5f, 0.5f, 0.9901f) > 0f, "SS0 far = depth above 0.99 (strict)");
            t.Check(P(0.4f, 0.5f, 0.5f, 1f) == 0f, "SS0 below the threshold gives 0");
            t.Check(Near(P(1f, 1.0f, 0.5f, 1f), 1.5f * 0.25f), "SS0 half the screen from the sun: falloff 0.75 - 0.5");
            t.Check(P(1f, 0.5f + 0.75f, 0.5f, 1f) == 0f && P(1f, 0.5f + 0.8f, 0.5f, 1f) == 0f, "SS0 at and beyond the falloff edge: 0");
            float mixed = SunShaftMath.Prefilter(0.9f, 0.2f, 0.6f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 0.5f, 2f, 1f);
            t.Check(Near(mixed, 0.4f + 0.1f), "SS0 per-channel threshold, summed (0.4 + 0 + 0.1), falloff saturated at 1");
        }

        static void TapsAndComposite(TestRunner t)
        {
            SunShaftMath.Tap(0.2f, 0.4f, 0.5f, 0.5f, 0.1f, 0, out float u0, out float v0);
            SunShaftMath.Tap(0.2f, 0.4f, 0.5f, 0.5f, 0.1f, 5, out float u5, out float v5);
            t.Check(u0 == 0.2f && v0 == 0.4f && Near(u5, 0.2f + 0.5f * 0.3f) && Near(v5, 0.4f + 0.5f * 0.1f), "SS0 taps: k = 0 at the pixel, k = 5 at half way for step 0.1");
            t.Check(Near(SunShaftMath.Composite(0.2f, 0.1f, 1f, 5f), 0.7f), "SS0 composite: 0.2 + 0.1 x 1 x 5");
            t.Check(Near(SunShaftMath.Composite(0.2f, 1f, 1f, 5f), 1.2f), "SS0 composite: the added light saturates at 1, the sum does not");
            t.Check(Near(SunShaftMath.Composite(0.2f, 0.5f, 0f, 5f), 0.2f), "SS0 composite with a black colour adds nothing");
        }

        static void Storage(TestRunner t)
        {
            t.Check(SunShaftMath.Store8(0f) == 0f && Near(SunShaftMath.Store8(1f), 1f, 1e-5f), "SS0 storage keeps 0 and 1");
            t.Check(Near(SunShaftMath.Store8(1.5f), 1f, 1e-5f) && SunShaftMath.Store8(-0.2f) == 0f, "SS0 storage clamps to 0..1");
            float half = SunShaftMath.Store8(0.5f);
            t.Check(Near(half, SunShaftMath.SrgbToLinear(188f / 255f), 1e-6f), $"SS0 storage of 0.5 = sRGB code 188 ({half:F5})");
        }

        static void Image(TestRunner t)
        {
            const int w = 16, h = 12;
            var rgb = new float[w * h * 3];
            var depth = new float[w * h];
            for (int i = 0; i < w * h; i++) { rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = 1f; depth[i] = 1f; }
            var p = new SunShaftMath.Params
            {
                ThresholdR = 0.5f, ThresholdG = 0.5f, ThresholdB = 0.5f, ColourR = 1f, ColourG = 1f, ColourB = 1f,
                MaxRadius = 0.75f, BlurSize = 5f, Intensity = 5f,
            };
            var s = SunShaftMath.Shafts(rgb, depth, w, h, p);
            t.Check(s != null && s.Length == w * h, "SS0 image: a shaft buffer of the frame's size");
            bool sym = true, inRange = true;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float a = s[y * w + x], b = s[y * w + (w - 1 - x)], c = s[(h - 1 - y) * w + x];
                    sym &= Math.Abs(a - b) <= 0.02f && Math.Abs(a - c) <= 0.02f;
                    inRange &= a >= 0f && a <= 1.00001f;
                }
            t.Check(sym, "SS0 image: a centred sun on a uniform far frame gives a symmetric buffer (within the 8-bit rounding)");
            t.Check(inRange && s[(h / 2) * w + w / 2] > s[0], "SS0 image: values in 0..1, the centre brighter than the corner");
            var near = (float[])depth.Clone();
            for (int i = 0; i < near.Length; i++) near[i] = 0.5f;
            var sNear = SunShaftMath.Shafts(rgb, near, w, h, p);
            t.Check(Array.TrueForAll(sNear, v => v == 0f), "SS0 image: a frame without far pixels has no shafts");
            p.HasCaster = true; p.CasterX = 0.5f; p.CasterY = 0.5f; p.CasterZ = -1f;
            t.Check(SunShaftMath.Shafts(rgb, depth, w, h, p) == null, "SS0 image: a caster behind the camera gives none");
            var o = SunShaftMath.Apply(rgb, null, p);
            t.Check(o.Length == rgb.Length && o[5] == rgb[5], "SS0 image: no buffer leaves the frame unchanged");
        }
    }
}
