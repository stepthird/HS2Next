// Screen-space reflections, opt-in. HS2 Studio draws screen-space reflections whenever a scene's SSR flag is on (its
// default, and on in every scene seen so far); HS2Next draws them only while the app option render.ssr is on (default off,
// a preview feature of the render pipeline) and the scene's flag is on. The build carries the pipeline's reflection
// feature in the production renderer (the scripting define URP_SCREEN_SPACE_REFLECTION compiles it in), placed after the
// opaques: it traces the current frame's opaque image and blends the result in by the surfaces' smoothness (HS2's
// reflections are a post effect of the opaque stage too; placed before the opaques it would trace the previous frame,
// which a one-off capture does not have); a Volume override
// of the scene's effect profile turns it on: mode "opaques only", full resolution, the hierarchical tracer, HS2's ray count
// and reach, fade towards the screen edge. Otherwise the override is inactive and the project's default profile keeps the
// mode Disabled, so the feature does no work for any camera. Receivers: the pipeline's own lit materials (map fallbacks,
// map shaders) and the fitted Standard families; characters are not receivers.
using UnityEngine;
using UnityEngine.Rendering;
#if URP_SCREEN_SPACE_REFLECTION
using UnityEngine.Rendering.Universal;
#endif

namespace HS2Next
{
    public static class SsrControl
    {
        /// <summary>What block 6 did (effects.state realized.ssr).</summary>
        public struct State
        {
            public bool Drawn, Option, SceneFlag;
            /// <summary>"urp" or "urp+standard".</summary>
            public string Receivers;
            /// <summary>Why not drawn: null, "option-off", "scene-off" or "define-missing".</summary>
            public string Reason;
        }

        /// <summary>The materials that receive the reflections in this build.</summary>
        public const string ReceiversInUse = "urp+standard";

        /// <summary>HS2's tracer settings in the pipeline's terms: ray steps, reach (scene units), screen-edge fade.</summary>
        public const int Hs2RaySteps = 48;
        public const float Hs2MaxDistance = 590f, Hs2EdgeFade = 0.5f, Hs2DistanceFade = 0.5f;
        /// <summary>
        /// The smoothness below which a surface receives nothing, and where the fade-in ends: only surfaces glossier than the
        /// common material default (0.5) reflect, as HS2's reflections show only on its glossy floors (with the pipeline's own
        /// defaults, 0.05 / 0.1, nearly every map surface took the traced reflection in place of its probe reflection).
        /// </summary>
        public const float MinSmoothness = 0.55f, SmoothnessFadeEnd = 0.75f;
        /// <summary>
        /// The reflection's strength: HS2's reflections change a glossy floor by about a third of what the pipeline's tracer
        /// adds at full strength (HS2 compare, hooh library #3: HS2 3.4-3.5 against 11.3-12.6 per changed pixel).
        /// </summary>
        public const float Strength = 0.35f;

        /// <summary>True when this build compiled the reflection feature in (the scripting define).</summary>
        public static bool Available =>
#if URP_SCREEN_SPACE_REFLECTION
            true;
#else
            false;
#endif

        /// <summary>The state of the last <see cref="Apply"/>.</summary>
        public static State Last { get; private set; }

        /// <summary>Sets the profile's reflection override from render.ssr and the scene's flag; returns the state.</summary>
        public static State Apply(VolumeProfile profile, bool sceneFlag)
        {
            bool option = RenderOptions.Ssr == "on";
            var st = new State { Option = option, SceneFlag = sceneFlag, Receivers = ReceiversInUse };
            if (!Available) st.Reason = "define-missing";
            else if (!option) st.Reason = "option-off";
            else if (!sceneFlag) st.Reason = "scene-off";
            st.Drawn = st.Reason == null;
#if URP_SCREEN_SPACE_REFLECTION
            if (profile)
            {
                if (!profile.TryGet(out ScreenSpaceReflectionVolumeSettings s)) s = profile.Add<ScreenSpaceReflectionVolumeSettings>(false);
                Configure(s, st.Drawn);
            }
#endif
            Last = st;
            return st;
        }

        /// <summary>Resets the state (a disposed scene).</summary>
        public static void Reset() => Last = default;

#if URP_SCREEN_SPACE_REFLECTION
        /// <summary>The override for drawn / not drawn (not drawn: inactive, the default profile's Disabled applies).</summary>
        public static void Configure(ScreenSpaceReflectionVolumeSettings s, bool drawn)
        {
            s.active = drawn;
            s.mode.Override(drawn ? ScreenSpaceReflectionVolumeSettings.ReflectionMode.OpaquesOnly : ScreenSpaceReflectionVolumeSettings.ReflectionMode.Disabled);
            if (!drawn) return;
            s.reflectionStrength.Override(Strength);
            s.resolution.Override(ScreenSpaceReflectionVolumeSettings.Resolution.Full);
            s.upscalingMethod.Override(ScreenSpaceReflectionVolumeSettings.UpscalingMethod.Bilateral);
            s.marchingMethod.Override(ScreenSpaceReflectionVolumeSettings.MarchingMethod.Hierarchical);
            s.maxRaySteps.Override(Hs2RaySteps);
            s.maxRayLength.Override(Hs2MaxDistance);
            s.rayLengthFade.Override(Hs2MaxDistance * Hs2DistanceFade);
            s.screenEdgeFadeDistance.Override(Hs2EdgeFade);
            s.reflectSky.Override(false);
            s.minimumSmoothness.Override(MinSmoothness);
            s.smoothnessFadeStart.Override(SmoothnessFadeEnd);
        }
#endif
    }
}
