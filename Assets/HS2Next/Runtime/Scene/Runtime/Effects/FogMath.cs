// The CPU reference of HS2's height fog (UnityEngine-free, plain floats): the per-pixel factors the fog pass and the
// transparent shaders compute, used by the render checks and the headless tests.
// Opaque pixels in HS2 are fogged twice toward the same colour, so one factor fA * fB is exact:
//   A  the post-processing stack's deferred distance fog: distance = view depth - near (z, not radial), exp2 of the
//      density times the distance squared (also on linear scenes; exponential scenes: exp2 of it, see PostMode), skipped on
//      sky / far pixels (linear 01 depth at or above 0.9999);
//   B  the opaque image-effect fog: g = (radial distance - near) - start (start 0) + a half-space height term below the
//      fog top (height, height density), clamped at 0, the factor of the scene's fog mode; excludeFarPixels keeps far pixels.
// Transparent forward passes are fogged once by the engine's forward fog: distance = (view depth - near) * far / (far - near)
// (the clip-space form), the factor of the mode, colour toward the fog colour (toward black for additive blending).
// Fog modes as the engine numbers them: 1 linear, 2 exponential, 3 exponential squared.
using System;

namespace HS2Next.Scenes.Runtime
{
    public static class FogMath
    {
        public const int Linear = 1, Exp = 2, Exp2 = 3;
        /// <summary>1 / sqrt(ln 2) and 1 / ln 2: the engine's exponential fog through exp2.</summary>
        public const float Exp2Scale = 1.2011224087f, ExpScale = 1.4426950408f;
        /// <summary>Linear 01 depth from which the post-processing fog treats a pixel as sky.</summary>
        public const float SkyDepth01 = 0.9999f;

        /// <summary>
        /// The engine's fog factor for distance <paramref name="x"/> (1 = no fog, 0 = fog colour), saturated. Linear with
        /// end == start gives 0 slope (the factor end / (end - start) term is dropped as the engine does).
        /// </summary>
        public static float Factor(int mode, float density, float start, float end, float x)
        {
            float f;
            switch (mode)
            {
                case Linear:
                {
                    float diff = end - start;
                    float inv = Math.Abs(diff) > 0.0001f ? 1f / diff : 0f;
                    f = x * -inv + end * inv;
                    break;
                }
                case Exp: f = Exp2f(-(density * ExpScale) * x); break;
                default:
                {
                    float t = density * Exp2Scale * x;
                    f = Exp2f(-t * t);
                    break;
                }
            }
            return Sat(f);
        }

        /// <summary>
        /// The post-processing stack's form of the fog factor: exp2 of the density times the distance without the 1 / ln 2
        /// scale (exponential) and the square of it (exponential squared); linear as <see cref="Factor"/>.
        /// </summary>
        public static float PostFactor(int mode, float density, float start, float end, float x)
        {
            float f;
            switch (mode)
            {
                case Linear:
                    f = Math.Abs(end - start) > 1e-6f ? (end - x) / (end - start) : 0f;
                    break;
                case Exp: f = Exp2f(-density * x); break;
                default:
                {
                    float t = density * x;
                    f = Exp2f(-t * t);
                    break;
                }
            }
            return Sat(f);
        }

        /// <summary>
        /// The half-space height term added to the distance (non-positive above the fog top, see the header):
        /// camera C, point P, fog top <paramref name="height"/>, <paramref name="heightDensity"/>.
        /// </summary>
        public static float HeightTerm(float cx, float cy, float cz, float px, float py, float pz, float height, float heightDensity)
        {
            float vx = px - cx, vy = py - cy, vz = pz - cz;
            float fdotC = cy - height;
            float k = fdotC <= 0f ? 1f : 0f;
            float fdotP = py - height;
            float fdotV = vy;
            float c1 = k * (fdotP + fdotC);
            float c2 = (1f - 2f * k) * fdotP;
            float m = Math.Min(c2, 0f);
            float lenAV = Math.Abs(heightDensity * 0.5f) * Len(vx, vy, vz);
            return -lenAV * (c1 - m * m / Math.Abs(fdotV + 1.0e-5f));
        }

        /// <summary>Term B: the opaque image-effect fog factor of a pixel at P (radial distance + height term).</summary>
        public static float ImageFogFactor(int mode, float density, float start, float end, float near,
            float cx, float cy, float cz, float px, float py, float pz, float height, float heightDensity, bool excludeFar, bool far)
        {
            if (excludeFar && far) return 1f;
            float g = Len(px - cx, py - cy, pz - cz) - near;          // radial distance, the engine's fog starts at the near plane
            g += HeightTerm(cx, cy, cz, px, py, pz, height, heightDensity);
            return Factor(mode, density, start, end, Math.Max(0f, g));
        }

        /// <summary>
        /// The mode term A is evaluated in: on linear scenes HS2's post-processing fog follows the density in the exponential
        /// squared form (measured on HS2's linear map 32; the shader's keyword-less variant), and exponential squared on
        /// exponential-squared scenes; exponential scenes are taken as exponential (no HS2 measurement).
        /// </summary>
        public static int PostMode(int mode) => mode == Exp ? Exp : Exp2;

        /// <summary>Term A: the post-processing distance fog factor (1 on sky / far pixels).</summary>
        public static float PostFogFactor(int mode, float density, float start, float end, float depth01, float near, float farPlane)
        {
            if (!(depth01 < SkyDepth01)) return 1f;
            return PostFactor(PostMode(mode), density, start, end, depth01 * farPlane - near);
        }

        /// <summary>The opaque factor (A · B) of a pixel at linear 01 depth <paramref name="depth01"/> and world point P.</summary>
        public static float OpaqueFactor(int mode, float density, float start, float end, float near, float farPlane, float depth01,
            float cx, float cy, float cz, float px, float py, float pz, float height, float heightDensity, bool excludeFar)
        {
            bool far = depth01 >= 1f;
            return PostFogFactor(mode, density, start, end, depth01, near, farPlane)
                 * ImageFogFactor(mode, density, start, end, near, cx, cy, cz, px, py, pz, height, heightDensity, excludeFar, far);
        }

        /// <summary>The forward (in-shader) fog distance of a fragment at view depth <paramref name="viewDepth"/>.</summary>
        public static float ForwardDistance(float viewDepth, float near, float farPlane) =>
            Math.Max((viewDepth - near) * farPlane / Math.Max(farPlane - near, 1e-6f), 0f);

        /// <summary>The transparent (forward) fog factor at view depth <paramref name="viewDepth"/>.</summary>
        public static float ForwardFactor(int mode, float density, float start, float end, float viewDepth, float near, float farPlane) =>
            Factor(mode, density, start, end, ForwardDistance(viewDepth, near, farPlane));

        /// <summary>lerp(fog, c, f): one channel of the fogged colour (additive passes use fog = 0).</summary>
        public static float Apply(float fog, float c, float f) => fog + (c - fog) * f;

        /// <summary>The fog-mode bit of a mode (1 linear, 2 exponential, 4 exponential squared; 0 for other values).</summary>
        public static int ModeBit(int mode) => mode >= Linear && mode <= Exp2 ? 1 << (mode - 1) : 0;

        static float Exp2f(float x) => (float)Math.Pow(2.0, x);
        static float Sat(float x) => x < 0f ? 0f : (x > 1f ? 1f : (float.IsNaN(x) ? 0f : x));
        static float Len(float x, float y, float z) => (float)Math.Sqrt(x * x + y * y + z * z);
    }
}
