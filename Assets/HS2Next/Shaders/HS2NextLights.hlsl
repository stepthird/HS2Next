// Light access shared by the fitted shaders (URP 17.6): the additional-light loop
// for Forward and Forward+ (cluster) rendering, light layers, and the baked GI inputs of lightmapped renderers.
//
//   Forward (_CLUSTER_LIGHT_LOOP off): the per-object light list (GetAdditionalLightsCount, culling masks applied by
//     Unity's per-object lists; at most the asset's per-object limit).
//   Forward+ (_CLUSTER_LIGHT_LOOP on): first the additional directional lights (not in the clusters), then the clustered
//     punctual lights of this pixel; no per-object limit, culling masks have no effect (only rendering layers filter).
//   _LIGHT_LAYERS (asset "Use Rendering Layers"): a light reaches a renderer when IsMatchingLightLayer(light layers,
//     renderer.renderingLayerMask); the main light too. Without the keyword every light matches.
//
// Use:
//   Light main = GetMainLight(shadowCoord);  main.color *= HS2LightLayerWeight(main.layerMask);
//   HS2LightIter it = HS2LightIterInit(positionWS, screenUV);
//   uint lightIndex;
//   [loop] while (HS2LightNext(it, lightIndex))
//   {
//       Light L = GetAdditionalLight(lightIndex, positionWS, shadowMask);
//       if (!HS2LightReaches(L, lightIndex)) continue;
//       ...
//   }
// With only a main light and the keywords off, the result is bit for bit the earlier per-object loop.
//
// Point / spot falloff (Phase 3.3 package S, E5, U10): HS2 (Built-in) attenuates point
// and spot lights by its falloff texture at u = (distance / range)^2 and shapes spot cones with the default spot cookie;
// URP uses 1 / distance^2 with a smooth range window and a linear angle ramp. While the global _HS2NextLegacyFalloff is 1
// (Runtime/Rendering/LightFalloff.cs: render.lightFalloff = hs2, the default; 0 = urp, or `-lightFalloff urp` for the batch
// tools) every GetAdditionalLight(...) after this file returns Built-in's attenuation in distanceAttenuation: the macro at
// the end redirects the name to HS2GetAdditionalLight (URP's own functions above it keep URP's). Measured in the Editor's
// Built-in pipeline (SkyAmbientTest.MeasureFalloff, Spike/out/effects/u10_builtin_falloff.tsv):
//   distance  f(u) = 1 / (1 + 25 u), times 1 - (u - 0.64) / 0.36 above u = 0.64 (0 at the range); the 1024-texel texture
//             holds f(i / 1024) and is sampled at texel centres: f(u - 1/2048)
//   spot      the same distance term times the default cookie at r = tan(angle off the axis) / tan(spotAngle / 2) (its light
//             coordinates are xy / w + 0.5 with w = z 2 tan(spotAngle / 2)); the cookie is radial within 0.0095 (the
//             33-entry table); zero behind the light
// URP's light data give the range (attenuation.x = 1 / range^2) and the spot's outer angle (cos = -attenuation.w /
// attenuation.z; attenuation.z is 0 for point lights). Directional additional lights are unchanged.
#ifndef HS2NEXT_LIGHTS_INCLUDED
#define HS2NEXT_LIGHTS_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// 1 when a light with these rendering layers reaches this renderer (_LIGHT_LAYERS), else 0; always 1 without the keyword.
half HS2LightLayerWeight(uint lightLayerMask)
{
#if defined(_LIGHT_LAYERS)
    return IsMatchingLightLayer(lightLayerMask, GetMeshRenderingLayer()) ? half(1) : half(0);
#else
    return half(1);
#endif
}

struct HS2LightIter
{
    uint next;      // next index of the plain loop (Forward: per-object lights; Forward+: additional directional lights)
    uint count;     // its length
#if USE_CLUSTER_LIGHT_LOOP
    ClusterIterator cluster;
#endif
};

HS2LightIter HS2LightIterInit(float3 positionWS, float2 normalizedScreenUV)
{
    HS2LightIter it;
    it.next = 0;
#if USE_CLUSTER_LIGHT_LOOP
    it.count = min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS);
    it.cluster = ClusterInit(normalizedScreenUV, positionWS, 0);
#elif defined(_ADDITIONAL_LIGHTS)
    it.count = GetAdditionalLightsCount();
#else
    it.count = 0;
#endif
    return it;
}

// The next additional light (an index for GetAdditionalLight); false after the last one.
bool HS2LightNext(inout HS2LightIter it, out uint lightIndex)
{
    lightIndex = 0;
    if (it.next < it.count) { lightIndex = it.next; it.next++; return true; }
#if USE_CLUSTER_LIGHT_LOOP
    if (ClusterNext(it.cluster, lightIndex)) { lightIndex += URP_FP_DIRECTIONAL_LIGHTS_COUNT; return true; }
#endif
    return false;
}

// False when the light must be skipped: its rendering layers miss this renderer (_LIGHT_LAYERS), or (Forward+ on a
// subtractive-lightmapped renderer) the light is already in the lightmap (URP's CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK).
bool HS2LightReaches(Light light, uint lightIndex)
{
#if USE_CLUSTER_LIGHT_LOOP && defined(LIGHTMAP_ON) && defined(LIGHTMAP_SHADOW_MIXING)
    if (_AdditionalLightsColor[lightIndex].a > 0.0h) return false;
#endif
#if defined(_LIGHT_LAYERS)
    return IsMatchingLightLayer(light.layerMask, GetMeshRenderingLayer());
#else
    return true;
#endif
}
// ---------------------------------------------------------------- Built-in-compatible point / spot falloff (Phase 3.3)

// 1 = Built-in's falloff for additional point / spot lights (render.lightFalloff = hs2, the default; LightFalloff.cs);
// 0 = URP's (GetAdditionalLight unchanged).
float _HS2NextLegacyFalloff;

// The default spot cookie's radial profile at r = i / 32 (U10; LightFalloff.CookieTable holds the same numbers).
static const float HS2SpotCookie[33] =
{
    1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
    0.99781, 0.98738, 0.96067, 0.90720, 0.82229, 0.70185, 0.55670, 0.40511, 0.26407, 0.15256, 0.07626, 0.02858, 0
};

// Built-in's distance falloff at u = (distance / range)^2 (texel i of its 1024-texel texture holds f(i / 1024)).
float HS2BuiltinDistanceAtten(float u)
{
    if (u >= 1.0) return 0.0;
    u = max(u - 1.0 / 2048.0, 0.0);
    return saturate(1.0 - max(u - 0.64, 0.0) / 0.36) / (1.0 + 25.0 * u);
}

// The default spot cookie at r = tan(off-axis angle) / tan(spotAngle / 2).
float HS2BuiltinCookie(float r)
{
    if (r >= 1.0) return 0.0;
    float x = max(r, 0.0) * 32.0;
    uint i = min((uint)x, 31u);
    return lerp(HS2SpotCookie[i], HS2SpotCookie[i + 1], x - (float)i);
}

// Built-in's attenuation of the additional light perObjectLightIndex at positionWS (URP's own value for directional lights).
float HS2LegacyAttenuation(int perObjectLightIndex, float3 positionWS, float urpAttenuation)
{
#if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
    float4 lightPositionWS = _AdditionalLightsBuffer[perObjectLightIndex].position;
    half4 att = _AdditionalLightsBuffer[perObjectLightIndex].attenuation;
    half4 spotDirection = _AdditionalLightsBuffer[perObjectLightIndex].spotDirection;
#else
    float4 lightPositionWS = _AdditionalLightsPosition[perObjectLightIndex];
    half4 att = _AdditionalLightsAttenuation[perObjectLightIndex];
    half4 spotDirection = _AdditionalLightsSpotDir[perObjectLightIndex];
#endif
    if (lightPositionWS.w < 0.5 || att.x <= 0.0) return urpAttenuation;   // directional
    float3 toPoint = positionWS - lightPositionWS.xyz;
    float d2 = dot(toPoint, toPoint);
    float a = HS2BuiltinDistanceAtten(d2 * (float)att.x);                  // attenuation.x = 1 / range^2
    if (att.z != 0.0)                                                      // spot (points: z = 0, w = 1)
    {
        float cosOuter = saturate(-(float)att.w / (float)att.z);
        float z = dot(toPoint, -(float3)spotDirection.xyz);                // along the light's forward (spotDirection = -forward)
        if (z <= 0.0) return 0.0;
        float tanHalf = sqrt(max(1.0 - cosOuter * cosOuter, 0.0)) / max(cosOuter, 1e-4);
        a *= HS2BuiltinCookie(sqrt(max(d2 - z * z, 0.0)) / (z * tanHalf));
    }
    return a;
}

int HS2PerObjectLightIndex(uint i)
{
#if USE_CLUSTER_LIGHT_LOOP
    return (int)i;
#else
    return GetPerObjectLightIndex(i);
#endif
}

Light HS2GetAdditionalLight(uint i, float3 positionWS)
{
    Light light = GetAdditionalLight(i, positionWS);
    if (_HS2NextLegacyFalloff > 0.5) light.distanceAttenuation = HS2LegacyAttenuation(HS2PerObjectLightIndex(i), positionWS, light.distanceAttenuation);
    return light;
}

Light HS2GetAdditionalLight(uint i, float3 positionWS, half4 shadowMask)
{
    Light light = GetAdditionalLight(i, positionWS, shadowMask);
    if (_HS2NextLegacyFalloff > 0.5) light.distanceAttenuation = HS2LegacyAttenuation(HS2PerObjectLightIndex(i), positionWS, light.distanceAttenuation);
    return light;
}

Light HS2GetAdditionalLight(uint i, InputData inputData, half4 shadowMask, AmbientOcclusionFactor aoFactor)
{
    Light light = GetAdditionalLight(i, inputData, shadowMask, aoFactor);
    if (_HS2NextLegacyFalloff > 0.5) light.distanceAttenuation = HS2LegacyAttenuation(HS2PerObjectLightIndex(i), inputData.positionWS, light.distanceAttenuation);
    return light;
}

// From here on, GetAdditionalLight in the files that include this one is the Built-in-compatible variant.
#define GetAdditionalLight HS2GetAdditionalLight
#endif
