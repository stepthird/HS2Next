// The sun-shaft effect's arithmetic without UnityEngine (the CPU reference of HS2SunShaftsFeature and its shader; the
// mono scene suite and the render check use it). HS2 Studio's sun shafts, as the effect behaves:
//   sun position  the caster's viewport point; no caster = the screen centre (0.5, 0.5); a caster behind the camera
//                 (viewport z < 0) adds nothing
//   prefilter     full resolution; only far pixels (linear 0..1 depth > 0.99: the sky, the background, the clear colour)
//                 contribute, with the value (sum over r, g, b of max(c − threshold, 0)) × saturate(maxRadius − |sun − uv|)
//                 (UV units, no aspect correction; maxRadius = the block's distance falloff); the one-pixel frame border
//                 is cleared
//   radial blur   four passes, each the average of 6 taps from the pixel towards the sun, tap k at
//                 uv + k (sun − uv) step; the steps are blurSize × {1, 6, 12, 18} / 768 (the step of a pass is the one set
//                 after the previous pass: 1 / 768 first, then (2i + 1) × 6 / 768 and (2i + 2) × 6 / 768 for i = 0, 1)
//   intermediates 8-bit sRGB-encoded textures (the effect's temporaries in a linear-space project): every pass result is
//                 clamped to 0..1 and stored with 8 bits after the sRGB encoding
//   composite     add: out = c + saturate(shaft × colour × intensity) per channel
// The threshold and the colour are the block's stored values used as they are (no gamma conversion).
using System;

namespace HS2Next.Scenes.Runtime
{
    public static class SunShaftMath
    {
        /// <summary>Linear 0..1 depth above which a pixel counts as far (sky, background).</summary>
        public const float FarDepth01 = 0.99f;
        /// <summary>Taps per radial blur pass.</summary>
        public const int Taps = 6;
        /// <summary>The step factors of the four blur passes (× blurSize / 768).</summary>
        public static readonly float[] StepFactors = { 1f, 6f, 12f, 18f };
        public const float StepDivisor = 768f;

        public static float Saturate(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        /// <summary>The sun's viewport point: none → the centre; behind = the caster's viewport z is below 0.</summary>
        public static void SunPosition(bool hasCaster, float vx, float vy, float vz, out float sx, out float sy, out bool behind)
        {
            if (!hasCaster) { sx = 0.5f; sy = 0.5f; behind = false; return; }
            sx = vx; sy = vy; behind = vz < 0f;
        }

        /// <summary>The step (UV units) of blur pass <paramref name="pass"/> (0..3).</summary>
        public static float Step(int pass, float blurSize) => blurSize * StepFactors[pass] / StepDivisor;

        /// <summary>The prefilter value of one pixel (before the 8-bit storage).</summary>
        public static float Prefilter(float r, float g, float b, float tr, float tg, float tb, float u, float v,
            float sunX, float sunY, float maxRadius, float depth01)
        {
            if (!(depth01 > FarDepth01)) return 0f;
            float dx = sunX - u, dy = sunY - v;
            float dist = Saturate(maxRadius - (float)Math.Sqrt(dx * dx + dy * dy));
            float sum = Math.Max(r - tr, 0f) + Math.Max(g - tg, 0f) + Math.Max(b - tb, 0f);
            return sum * dist;
        }

        /// <summary>Tap <paramref name="k"/> of a blur pass at (u, v).</summary>
        public static void Tap(float u, float v, float sunX, float sunY, float step, int k, out float tu, out float tv)
        {
            tu = u + k * (sunX - u) * step;
            tv = v + k * (sunY - v) * step;
        }

        /// <summary>One channel of the composite.</summary>
        public static float Composite(float c, float shaft, float colour, float intensity) => c + Saturate(shaft * colour * intensity);

        // ---------------------------------------------------------------- the 8-bit sRGB storage

        public static float LinearToSrgb(float l)
        {
            l = Saturate(l);
            return l <= 0.0031308f ? l * 12.92f : 1.055f * (float)Math.Pow(l, 1.0 / 2.4) - 0.055f;
        }

        public static float SrgbToLinear(float s) => s <= 0.04045f ? s / 12.92f : (float)Math.Pow((s + 0.055f) / 1.055f, 2.4);

        /// <summary>A value as an 8-bit sRGB texture stores and returns it.</summary>
        public static float Store8(float l) => SrgbToLinear((float)Math.Round(LinearToSrgb(l) * 255f) / 255f);

        // ---------------------------------------------------------------- the whole image (reference)

        /// <summary>The effect's parameters.</summary>
        public struct Params
        {
            public bool HasCaster;
            /// <summary>The caster's viewport point (x, y, z); ignored without a caster.</summary>
            public float CasterX, CasterY, CasterZ;
            public float ThresholdR, ThresholdG, ThresholdB;
            public float ColourR, ColourG, ColourB;
            public float MaxRadius, BlurSize, Intensity;
        }

        /// <summary>
        /// The shaft buffer of a w × h frame (row 0 = v near 0) from the frame's colour (rgb, linear, 3 floats per pixel) and its
        /// linear 0..1 depth, with bilinear clamped sampling at texel centres and the 8-bit sRGB storage of every pass.
        /// Returns null when the sun is behind the camera.
        /// </summary>
        public static float[] Shafts(float[] rgb, float[] depth01, int w, int h, Params p)
        {
            SunPosition(p.HasCaster, p.CasterX, p.CasterY, p.CasterZ, out float sx, out float sy, out bool behind);
            if (behind) return null;
            var a = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (x == 0 || y == 0 || x == w - 1 || y == h - 1) { a[i] = 0f; continue; }
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                    a[i] = Store8(Prefilter(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2], p.ThresholdR, p.ThresholdG, p.ThresholdB,
                        u, v, sx, sy, p.MaxRadius, depth01[i]));
                }
            var b = new float[w * h];
            for (int pass = 0; pass < StepFactors.Length; pass++)
            {
                float step = Step(pass, p.BlurSize);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float u = (x + 0.5f) / w, v = (y + 0.5f) / h, sum = 0f;
                        for (int k = 0; k < Taps; k++)
                        {
                            Tap(u, v, sx, sy, step, k, out float tu, out float tv);
                            sum += Bilinear(a, w, h, tu, tv);
                        }
                        b[y * w + x] = Store8(sum / Taps);
                    }
                var t = a; a = b; b = t;
            }
            return a;
        }

        /// <summary>The composite of a frame with its shaft buffer (null = unchanged).</summary>
        public static float[] Apply(float[] rgb, float[] shafts, Params p)
        {
            var o = (float[])rgb.Clone();
            if (shafts == null) return o;
            for (int i = 0; i < shafts.Length; i++)
            {
                o[i * 3] = Composite(rgb[i * 3], shafts[i], p.ColourR, p.Intensity);
                o[i * 3 + 1] = Composite(rgb[i * 3 + 1], shafts[i], p.ColourG, p.Intensity);
                o[i * 3 + 2] = Composite(rgb[i * 3 + 2], shafts[i], p.ColourB, p.Intensity);
            }
            return o;
        }

        /// <summary>Bilinear sample of a w × h scalar image at (u, v), texel centres at (i + 0.5) / w, clamped to the edge.</summary>
        public static float Bilinear(float[] img, int w, int h, float u, float v)
        {
            float fx = u * w - 0.5f, fy = v * h - 0.5f;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
            float ax = fx - x0, ay = fy - y0;
            float S(int x, int y) => img[Clamp(y, 0, h - 1) * w + Clamp(x, 0, w - 1)];
            float top = S(x0, y0) * (1 - ax) + S(x0 + 1, y0) * ax;
            float bot = S(x0, y0 + 1) * (1 - ax) + S(x0 + 1, y0 + 1) * ax;
            return top * (1 - ay) + bot * ay;
        }

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
