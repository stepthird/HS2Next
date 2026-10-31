// Point / spot light falloff of the fitted shaders (E5, U10; Phase 3.3 package S).
// render.lightFalloff = hs2 (default) sets the global _HS2NextLegacyFalloff to 1: every fitted shader that includes
// Shaders/HS2NextLights.hlsl attenuates additional point and spot lights as Built-in does (HS2's look); urp sets 0: URP's
// inverse-square falloff with its range window and angle ramp (the numbers of the fits before 3.3). URP-native shaders
// (URP/Lit fallback slots, SpeedTree7, Terrain Lit) always use URP's falloff (known rule light.urpNativeFalloff).
// Batch tools take `-lightFalloff urp|hs2` on the command line (it wins over the option), so the fit regressions can be
// run with either falloff. The C# functions mirror the HLSL (tests compare both with the U10 table).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace HS2Next
{
    public static class LightFalloff
    {
        public const string GlobalName = "_HS2NextLegacyFalloff";
        static readonly int GlobalId = Shader.PropertyToID(GlobalName);
        static bool hooked;

        /// <summary>The command line's `-lightFalloff` value (urp | hs2), null when absent or unknown.</summary>
        public static string CommandLineOverride
        {
            get
            {
                var a = Environment.GetCommandLineArgs();
                int i = Array.IndexOf(a, "-lightFalloff");
                string v = i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
                return v == "urp" || v == "hs2" ? v : null;
            }
        }

        /// <summary>The falloff in effect: the command line's, else render.lightFalloff.</summary>
        public static string Mode => CommandLineOverride ?? RenderOptions.LightFalloff;
        public static bool Legacy => Mode == "hs2";
        /// <summary>The global as last written (tests).</summary>
        public static float GlobalValue => Shader.GetGlobalFloat(GlobalId);

        /// <summary>Writes the global from <see cref="Mode"/>.</summary>
        public static void Apply() => Shader.SetGlobalFloat(GlobalId, Legacy ? 1f : 0f);

        public static void Hook()
        {
            if (hooked) return;
            hooked = true;
            RenderOptions.Changed += OnOptions;
            Apply();
        }

        static void OnOptions(IReadOnlyCollection<string> keys)
        {
            foreach (var k in keys) if (k == "lightFalloff") { Apply(); return; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InitPlayer() => Hook();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void InitEditor() => Hook();
#endif

        // ------------------------------------------------------------ the curves (mirror of HS2NextLights.hlsl)

        /// <summary>
        /// Built-in's distance falloff at u = (distance / range)^2: its 1024-texel texture holds f(i / 1024) with
        /// f(u) = 1 / (1 + 25 u) × (1 − (u − 0.64) / 0.36 above u = 0.64), sampled at texel centres → f(u − 1/2048); 0 at the range.
        /// </summary>
        public static float BuiltinDistance(float u)
        {
            if (u >= 1f) return 0f;
            u = Mathf.Max(u - 1f / 2048f, 0f);
            float a = 1f / (1f + 25f * u);
            if (u > 0.64f) a *= Mathf.Clamp01(1f - (u - 0.64f) / 0.36f);
            return a;
        }

        /// <summary>The default spot cookie at r = tan(off-axis angle) / tan(spotAngle / 2), linear in the table (r ≥ 1: 0).</summary>
        public static float BuiltinCookie(float r)
        {
            if (r >= 1f) return 0f;
            float x = Mathf.Max(r, 0f) * (CookieTable.Length - 1);
            int i = (int)x;
            if (i >= CookieTable.Length - 1) return CookieTable[CookieTable.Length - 1];
            return Mathf.Lerp(CookieTable[i], CookieTable[i + 1], x - i);
        }

        /// <summary>
        /// The default spot cookie's radial profile at r = i / 32 (U10: the mean of the axis and diagonal profiles of the
        /// cookie texture as Built-in samples it; within 0.0095 of both). Engine data measured in the Editor's Built-in.
        /// </summary>
        public static readonly float[] CookieTable =
        {
            1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f,
            0.99781f, 0.98738f, 0.96067f, 0.90720f, 0.82229f, 0.70185f, 0.55670f, 0.40511f, 0.26407f, 0.15256f, 0.07626f, 0.02858f, 0f,
        };

        /// <summary>Built-in's attenuation of a point (spotAngle ≤ 0) or spot light at a point given in the light's frame
        /// (z along the light's forward), range and spot angle in degrees.</summary>
        public static float Builtin(Vector3 local, float range, float spotAngle)
        {
            float a = BuiltinDistance(local.sqrMagnitude / (range * range));
            if (spotAngle <= 0f) return a;
            if (local.z <= 0f) return 0f;
            float r = new Vector2(local.x, local.y).magnitude / (local.z * Mathf.Tan(spotAngle * 0.5f * Mathf.Deg2Rad));
            return a * BuiltinCookie(r);
        }

        /// <summary>URP's attenuation (RealtimeLights.hlsl DistanceAttenuation × AngleAttenuation; inner angle as URP derives it
        /// when a light has none of its own: Light.innerSpotAngle is passed).</summary>
        public static float Urp(Vector3 local, float range, float spotAngle, float innerSpotAngle)
        {
            float d2 = Mathf.Max(local.sqrMagnitude, 6.103515625e-5f);
            float f = d2 / (range * range);
            float smooth = Mathf.Clamp01(1f - f * f);
            float a = smooth * smooth / d2;
            if (spotAngle <= 0f) return a;
            float cosOuter = Mathf.Cos(spotAngle * 0.5f * Mathf.Deg2Rad);
            float cosInner = Mathf.Cos(innerSpotAngle * 0.5f * Mathf.Deg2Rad);
            float inv = 1f / Mathf.Max(0.001f, cosInner - cosOuter);
            float sdl = local.z / Mathf.Sqrt(d2);
            float t = Mathf.Clamp01(sdl * inv - cosOuter * inv);
            return a * t * t;
        }
    }
}
