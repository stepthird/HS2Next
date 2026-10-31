// The CPU bake of HS2's LDR colour-grading strip (Phase 3.3 package B).
// HS2 Studio grades with PPv2 in LDR mode: a 32³ strip (1024 × 32, x = r + 32 b, y = g; Unity row order) baked from the
// user LUT (Lut_Default lerped towards the Filter row's strip by the blend), the scene's brightness / contrast /
// saturation and the hidden base grade (BaseGrade), looked up in sRGB-encoded space after a saturate. HS2Next bakes the
// same strip on the CPU and hands it to URP's ColorLookup (contribution 1, LDR grading, Tonemapping None): URP looks the
// user LUT up in sRGB after its own saturate, so the image matches to the LUT's precision.
// Steps per grid node n (HS2's float32 grid constants), each skipped when it is the identity (so neutral inputs give the
// grid bit-exactly):
//   1 user LUT        u = n + contribution (lut(n) − n)                       (the strip texel at the node)
//   2 brightness      u *= (brightness + 100) / 100
//   3 contrast        u = (u − 0.217638) (contrast / 100 + 1) + 0.217638        (no clamp)
//   4 white balance (LMS round trip), colour filter, channel mixer           (identity on HS2's profile)
//   5 lift/gamma/gain u = u gain + lift; u = sign(u) |u|^invGamma; u = max(u, 0)
//   6 HSV             satMult = 2 hueVsSat(h) · 2 satVsSat(s) · lumVsSat(luma(u)) · 2 (saturation / 100 + 1);
//                     hue += shift + hueVsHue − 0.5 (wrapped); u = saturate(luma' + satMult (c − luma'))
//                     (curves looked up at 128 x − 0.5, clamped; Rec.709 luma 0.212673 / 0.715152 / 0.072175)
//   7 YRGB curves     master, then r / g / b, each looked up at 128 x (x + 1/256), clamped to sample 127; saturate
// The neutral profile (no base grade) skips the curves entirely (linear), so a neutral grade is the identity.
// The strip is a linear (non-sRGB) RGBAHalf Texture2D, bilinear, clamped (HS2's internal strip is ARGBHalf too).
// Strips of the Filter rows are read back from the GPU once per texture (bundle textures are not CPU-readable):
// Blit into a linear ARGB32 render texture, ReadPixels; raw texel / 255, no flip (Unity row order on both sides).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>The user inputs of block 1 (HS2's sliders) and the LUT blend.</summary>
    public struct GradeInputs
    {
        public float Saturation, Brightness, Contrast;
        public static GradeInputs Of(float saturation, float brightness, float contrast) => new GradeInputs { Saturation = saturation, Brightness = brightness, Contrast = contrast };
    }

    public static class ColorGradingBake
    {
        public const int Size = 32, Width = Size * Size, Texels = Width * Size;
        /// <summary>Floats of a strip: 32 rows × 1024 columns × RGB.</summary>
        public const int Floats = Texels * 3;

        /// <summary>The neutral strip at the texel centres (HS2's float32 constants 1/2048, 1/64, 1.032258).</summary>
        public static float[] Grid()
        {
            var g = new float[Floats];
            const float k = 1.032258f;
            for (int y = 0; y < Size; y++)
            {
                float fy = (y + 0.5f) / Size;
                float G = (fy - 1f / 64f) * k;
                for (int x = 0; x < Width; x++)
                {
                    float fx = (x + 0.5f) / Width;
                    float px = fx - 0.00048828125f;
                    float r = (px * 32f) % 1f;
                    float b = px - r / 32f;
                    int i = (y * Width + x) * 3;
                    g[i] = r * k;
                    g[i + 1] = G;
                    g[i + 2] = b * k;
                }
            }
            return g;
        }

        static float[] grid;
        static float[] GridCached() => grid ??= Grid();

        /// <summary>
        /// The user LUT of step 1: Lut_Default lerped towards the filter strip by <paramref name="blend"/> (≤ 0 → Lut_Default,
        /// ≥ 1 → the filter; null strips = the grid). The texel lerp is unquantized (the spec's model).
        /// </summary>
        public static float[] UserLut(float[] lutDefault, float[] filter, float blend)
        {
            var d = lutDefault ?? GridCached();
            var f = filter ?? d;
            if (blend <= 0f) return lutDefault;
            if (blend >= 1f) return filter ?? lutDefault;
            var u = new float[Floats];
            for (int i = 0; i < Floats; i++) u[i] = d[i] + blend * (f[i] - d[i]);
            return u;
        }

        /// <summary>Bakes one strip (float32, 32 × 1024 × RGB in 0..1). <paramref name="userLut"/> null = the identity.</summary>
        public static float[] Bake(float[] userLut, GradeInputs inputs, GradeProfile prof)
        {
            var n = GridCached();
            var o = new float[Floats];
            float contribution = prof.Contribution;
            float bright = (inputs.Brightness + 100f) / 100f;
            float con = inputs.Contrast / 100f + 1f;
            bool doBright = inputs.Brightness != 0f, doContrast = inputs.Contrast != 0f;
            bool doBalance = !prof.BalanceIdentity, doFilter = !prof.FilterIdentity, doMixer = !prof.MixerIdentity, doLgg = !prof.LggIdentity;
            var cv = prof.Curves;
            bool neutralVs = prof.Neutral || (IsConst(cv[4], 0.5f) && IsConst(cv[5], 0.5f) && IsConst(cv[6], 0.5f) && IsConst(cv[7], 0.5f));
            bool doHsv = !(neutralVs && inputs.Saturation == 0f && prof.HueShift == 0f);
            bool doCurves = !prof.Neutral && cv[0] != null;
            float satScale = inputs.Saturation / 100f + 1f;
            float[] cb = prof.ColorBalance, cf = prof.ColorFilter, mx = prof.Mixer, lift = prof.Lift, ig = prof.InvGamma, gain = prof.Gain;

            // rows in parallel (pure per texel; each row writes its own range)
            System.Threading.Tasks.Parallel.For(0, Size, row =>
            {
            for (int t = row * Width, tEnd = t + Width; t < tEnd; t++)
            {
                int i = t * 3;
                float r = n[i], g = n[i + 1], b = n[i + 2];
                if (userLut != null)
                {
                    r = r + contribution * (userLut[i] - r);
                    g = g + contribution * (userLut[i + 1] - g);
                    b = b + contribution * (userLut[i + 2] - b);
                }
                if (doBright) { r *= bright; g *= bright; b *= bright; }
                if (doContrast)
                {
                    r = (r - 0.217638f) * con + 0.217638f;
                    g = (g - 0.217638f) * con + 0.217638f;
                    b = (b - 0.217638f) * con + 0.217638f;
                }
                if (doBalance)
                {
                    float l = (0.390405f * r + 0.549941f * g + 0.008926f * b) * cb[0];
                    float m = (0.070842f * r + 0.963172f * g + 0.001358f * b) * cb[1];
                    float s = (0.023108f * r + 0.128021f * g + 0.936245f * b) * cb[2];
                    r = 2.85847f * l - 1.62879f * m - 0.024891f * s;
                    g = -0.210182f * l + 1.1582f * m + 0.000324f * s;
                    b = -0.041812f * l - 0.118169f * m + 1.06867f * s;
                }
                if (doFilter) { r *= cf[0]; g *= cf[1]; b *= cf[2]; }
                if (doMixer)
                {
                    float r2 = r * mx[0] + g * mx[1] + b * mx[2];
                    float g2 = r * mx[3] + g * mx[4] + b * mx[5];
                    float b2 = r * mx[6] + g * mx[7] + b * mx[8];
                    r = r2; g = g2; b = b2;
                }
                if (doLgg)
                {
                    r = SignedPow(r * gain[0] + lift[0], ig[0]);
                    g = SignedPow(g * gain[1] + lift[1], ig[1]);
                    b = SignedPow(b * gain[2] + lift[2], ig[2]);
                    if (r < 0f) r = 0f; if (g < 0f) g = 0f; if (b < 0f) b = 0f;
                }
                else
                {
                    if (r < 0f) r = 0f; if (g < 0f) g = 0f; if (b < 0f) b = 0f;
                }
                if (doHsv)
                {
                    RgbToHsv(r, g, b, out float h, out float sv, out float v);
                    float luma = r * 0.212673f + g * 0.715152f + b * 0.072175f;
                    float hvs = neutralVs ? 0.5f : Sat01(Sample(cv[5], h, 0f));
                    float svs = neutralVs ? 0.5f : Sat01(Sample(cv[6], sv, 0f));
                    float lvs = neutralVs ? 0.5f : Sat01(Sample(cv[7], luma, 0f));
                    float satMult = 2f * hvs * (2f * svs) * lvs;
                    satMult = satScale * 2f * satMult;
                    float hh = h + prof.HueShift;
                    hh = hh + (prof.Neutral || cv[4] == null ? 0.5f : Sat01(Sample(cv[4], hh, 0f))) - 0.5f;
                    if (hh > 1f) hh -= 1f; else if (hh < 0f) hh += 1f;
                    HsvToRgb(hh, sv, v, out float cr, out float cg, out float cb2);
                    float lc = cr * 0.212673f + cg * 0.715152f + cb2 * 0.072175f;
                    r = Sat01(lc + satMult * (cr - lc));
                    g = Sat01(lc + satMult * (cg - lc));
                    b = Sat01(lc + satMult * (cb2 - lc));
                }
                else { r = Sat01(r); g = Sat01(g); b = Sat01(b); }
                if (doCurves)
                {
                    const float half = 1f / 256f;
                    r = Sat01(Sample(cv[0], r, half)); g = Sat01(Sample(cv[0], g, half)); b = Sat01(Sample(cv[0], b, half));
                    r = Sat01(Sample(cv[1], r, half)); g = Sat01(Sample(cv[2], g, half)); b = Sat01(Sample(cv[3], b, half));
                }
                o[i] = r; o[i + 1] = g; o[i + 2] = b;
            }
            });
            return o;
        }

        static bool IsConst(float[] t, float v)
        {
            if (t == null) return true;
            for (int i = 0; i < t.Length; i++) if (t[i] != v) return false;
            return true;
        }

        static float Sat01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;

        static float SignedPow(float x, float p)
        {
            if (p == 1f) return x;
            float a = Math.Abs(x);
            if (a < 1e-30f) a = 1e-30f;
            return Math.Sign(x) * (float)Math.Pow(a, p);
        }

        /// <summary>Bilinear clamped lookup of a 128-sample table at x (+ offset), as the GPU samples the curve texture.</summary>
        static float Sample(float[] table, float x, float offset)
        {
            float idx = (x + offset) * 128f - 0.5f;
            if (idx < 0f) idx = 0f; else if (idx > 127f) idx = 127f;
            int i0 = (int)Math.Floor(idx);
            int i1 = i0 + 1 > 127 ? 127 : i0 + 1;
            float f = idx - i0;
            return table[i0] * (1f - f) + table[i1] * f;
        }

        static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
        {
            float px, py, pz, pw;
            if (g >= b) { px = g; py = b; pz = 0f; pw = -1f / 3f; }
            else { px = b; py = g; pz = -1f; pw = 2f / 3f; }
            float qx, qy, qz, qw;
            if (r >= px) { qx = r; qy = py; qz = pz; qw = px; }
            else { qx = px; qy = py; qz = pw; qw = r; }
            float d = qx - Math.Min(qw, qy);
            h = Math.Abs(qz + (qw - qy) / (6f * d + 1e-4f));
            s = d / (qx + 1e-4f);
            v = qx;
        }

        static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
        {
            float C(float k)
            {
                float p = Math.Abs(((h + k) % 1f) * 6f - 3f);
                float q = p - 1f; q = q < 0f ? 0f : q > 1f ? 1f : q;
                return v * (1f + s * (q - 1f));
            }
            r = C(1f); g = C(2f / 3f); b = C(1f / 3f);
        }

        // ------------------------------------------------------------ texture

        /// <summary>A strip as a linear RGBAHalf texture (bilinear, clamped) for URP's ColorLookup; <paramref name="reuse"/> is refilled when it fits.</summary>
        public static Texture2D ToTexture(float[] strip, Texture2D reuse = null)
        {
            var tex = reuse && reuse.width == Width && reuse.height == Size && reuse.format == TextureFormat.RGBAHalf ? reuse
                : new Texture2D(Width, Size, TextureFormat.RGBAHalf, false, true) { name = "HS2Next grading strip", hideFlags = HideFlags.DontSave };
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.anisoLevel = 0;
            var data = new ushort[Texels * 4];
            ushort one = Mathf.FloatToHalf(1f);
            for (int t = 0; t < Texels; t++)
            {
                data[t * 4] = Mathf.FloatToHalf(strip[t * 3]);
                data[t * 4 + 1] = Mathf.FloatToHalf(strip[t * 3 + 1]);
                data[t * 4 + 2] = Mathf.FloatToHalf(strip[t * 3 + 2]);
                data[t * 4 + 3] = one;
            }
            tex.SetPixelData(data, 0);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>True when strips can be read back (a graphics device exists).</summary>
        public static bool CanReadStrips => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        /// <summary>
        /// The texels of a 1024 × 32 strip texture as float32 RGB in 0..1 (raw / 255 for 8-bit strips), read back from the GPU;
        /// null when the texture is not a strip or no device exists.
        /// </summary>
        public static float[] ReadStrip(Texture tex)
        {
            if (!tex || tex.width != Width || tex.height != Size || !CanReadStrips) return null;
            RenderTexture rt = null;
            Texture2D cpu = null;
            var prev = RenderTexture.active;
            try
            {
                rt = RenderTexture.GetTemporary(Width, Size, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                rt.filterMode = FilterMode.Point;
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                cpu = new Texture2D(Width, Size, TextureFormat.RGBAFloat, false, true);
                cpu.ReadPixels(new Rect(0, 0, Width, Size), 0, 0, false);
                cpu.Apply(false, false);
                var px = cpu.GetPixelData<Color>(0);
                var o = new float[Floats];
                bool eightBit = tex is Texture2D t2 && (t2.format == TextureFormat.RGB24 || t2.format == TextureFormat.RGBA32 || t2.format == TextureFormat.ARGB32);
                for (int i = 0; i < Texels; i++)
                {
                    var c = px[i];
                    if (eightBit)
                    {
                        o[i * 3] = Mathf.Round(c.r * 255f) / 255f;
                        o[i * 3 + 1] = Mathf.Round(c.g * 255f) / 255f;
                        o[i * 3 + 2] = Mathf.Round(c.b * 255f) / 255f;
                    }
                    else { o[i * 3] = c.r; o[i * 3 + 1] = c.g; o[i * 3 + 2] = c.b; }
                }
                return o;
            }
            finally
            {
                RenderTexture.active = prev;
                if (rt) RenderTexture.ReleaseTemporary(rt);
                if (cpu) Object.DestroyImmediate(cpu);
            }
        }

        /// <summary>Largest and mean absolute difference of two strips (in 0..1).</summary>
        public static (double max, double mean) Diff(float[] a, float[] b)
        {
            double max = 0, sum = 0;
            for (int i = 0; i < Floats; i++)
            {
                double d = Math.Abs(a[i] - b[i]);
                if (d > max) max = d;
                sum += d;
            }
            return (max, sum / Floats);
        }

        /// <summary>A short hash of a strip's float bits (effects.state lutHash).</summary>
        public static string Hash(float[] s)
        {
            if (s == null) return null;
            unchecked
            {
                uint h = 2166136261;
                for (int i = 0; i < s.Length; i++) { h ^= (uint)BitConverter.SingleToInt32Bits(s[i]); h *= 16777619; }
                return h.ToString("x8");
            }
        }
    }
}
