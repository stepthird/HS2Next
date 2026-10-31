// Built-in pipeline "BRDF1" (Disney diffuse + GGX, Smith-joint visibility) evaluated with URP
// lights, so replacements for HS2's Built-in shaders shade like the originals. Written from
// the published Unity BRDF description; verified against renders (Editor/Fit/ShaderFit.cs).
#ifndef HS2NEXT_BRDF_INCLUDED
#define HS2NEXT_BRDF_INCLUDED
#include "HS2NextLights.hlsl"

struct HS2Surface
{
    half3 albedo;       // before energy split
    half3 specColor;    // F0
    half oneMinusReflectivity;
    half smoothness;
    half occlusion;
    half3 emission;
    float3 normalWS;
    // Translucency (Amplify-style, used by HS2's AIT shaders); strength 0 disables it.
    half transStrength, transDirect, transAmbient, transScattering, transDistortion;
    half3 transColor;   // multiplies albedo in the translucency term (default 1; skin: tuned colour)
    half transShadow;   // 0: translucency ignores shadows, 1: fully shadowed (Amplify _TransShadow)
    half3 lift;         // direct-light-only albedo lift: diffuse uses saturate(albedo + lift * NdotL)
    half3 transmission; // Amplify-style transmission colour: adds transmission * saturate(-NdotL) * shadowed light
    half specularHighlights;   // 0: no direct specular (Standard _SPECULARHIGHLIGHTS_OFF)
    half glossyReflections;    // 0: indirect specular from the flat environment colour (Standard _GLOSSYREFLECTIONS_OFF)
};

// Shadow receiving for HS2Shade: 1 (default) applies the lights' shadows, 0 ignores them. Built-in forward rendering
// applies no realtime shadows to materials in the transparent queues (> 2500); replacements that follow this declare
// _RECEIVE_SHADOWS_OFF (ShaderMap.Swap sets it for those queues) and set this to 0 before HS2Shade.
static half HS2ShadowReceive = 1;

// Baked GI of a lightmapped renderer (HS2NextStandard.hlsl sets them before HS2Shade; only shaders that declare the
// lightmap keywords compile these paths): HS2BakedGI replaces the SH ambient under LIGHTMAP_ON (Built-in Standard: the
// lightmap times occlusion, no SH), HS2ShadowMask is the shadow mask (lightmap or light probes) under SHADOWS_SHADOWMASK.
static half3 HS2BakedGI = 0;
static half4 HS2ShadowMask = 1;

HS2Surface HS2SurfaceMetallic(half3 albedo, half metallic, half smoothness, half occlusion, float3 n)
{
    HS2Surface s;
    s.albedo = albedo;
    s.specColor = lerp(half3(0.04, 0.04, 0.04), albedo, metallic);
    s.oneMinusReflectivity = 0.96 - metallic * 0.96;
    s.smoothness = smoothness; s.occlusion = occlusion; s.emission = 0; s.normalWS = n;
    s.transStrength = 0; s.transDirect = 1; s.transAmbient = 0.2; s.transScattering = 2; s.transDistortion = 0.1;
    s.transColor = 1; s.transShadow = 0; s.lift = 0; s.transmission = 0; s.specularHighlights = 1; s.glossyReflections = 1;
    return s;
}

// Specular workflow (Unity's Standard (Specular setup)): F0 = specColor, diffuse = albedo * (1 - max(specColor)).
HS2Surface HS2SurfaceSpecular(half3 albedo, half3 specColor, half smoothness, half occlusion, float3 n)
{
    HS2Surface s = HS2SurfaceMetallic(albedo, 0, smoothness, occlusion, n);
    s.specColor = specColor;
    s.oneMinusReflectivity = 1 - max(max(specColor.r, specColor.g), specColor.b);
    return s;
}

half HS2DisneyDiffuse(half nv, half nl, half lh, half pr)
{
    half fd90 = 0.5 + 2 * lh * lh * pr;
    half l = 1 + (fd90 - 1) * pow(1 - nl, 5);
    half v = 1 + (fd90 - 1) * pow(1 - nv, 5);
    return l * v;
}

half3 HS2DirectLight(HS2Surface s, float3 v, float3 l, half3 lightColor)
{
    float3 n = s.normalWS;
    float3 h = SafeNormalize(l + v);
    half nv = abs(dot(n, v));
    half nl = saturate(dot(n, l));
    half nh = saturate(dot(n, h));
    half lh = saturate(dot(l, h));
    half pr = 1 - s.smoothness;
    half a = max(pr * pr, 0.002);
    half diffuseTerm = HS2DisneyDiffuse(nv, nl, lh, pr) * nl;
    half lv = nl * (nv * (1 - a) + a);
    half ll = nv * (nl * (1 - a) + a);
    half V = 0.5 / (lv + ll + 1e-5);
    half a2 = a * a;
    float d = (nh * a2 - nh) * nh + 1.0;
    half D = INV_PI * a2 / (d * d + 1e-7);
    half spec = max(0, V * D * PI * nl) * s.specularHighlights;
    // Skin lift (HS2NextSkin.hlsl): direct-light albedo saturate(albedo + lift * NdotL), also as metal F0.
    bool lifted = any(s.lift);
    half3 albedoD = lifted ? saturate(s.albedo + s.lift * nl) : s.albedo;
    half3 specColor = lifted ? lerp(half3(0.04, 0.04, 0.04), albedoD, 1 - s.oneMinusReflectivity / 0.96) : s.specColor;
    spec *= any(specColor) ? 1.0 : 0.0;
    half3 F = specColor + (1 - specColor) * pow(1 - lh, 5);
    half3 diffColor = albedoD * s.oneMinusReflectivity;
    return diffColor * lightColor * diffuseTerm + spec * lightColor * F;
}

half3 HS2Translucency(HS2Surface s, float3 v, float3 l, half3 lightColor, half3 giDiffuse)
{
    float3 tl = l + s.normalWS * s.transDistortion;
    half vdl = pow(saturate(dot(v, -tl)), s.transScattering);
    return s.albedo * s.transColor * lightColor * (vdl * s.transDirect + giDiffuse * s.transAmbient) * s.transStrength;
}

half3 HS2Shade(HS2Surface s, float3 positionWS, float2 screenUV)
{
    float3 v = GetWorldSpaceNormalizeViewDir(positionWS);
    float3 n = s.normalWS;
    half3 diffColor = s.albedo * s.oneMinusReflectivity;

    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
#if defined(LIGHTMAP_ON) || defined(SHADOWS_SHADOWMASK)
    Light main = GetMainLight(shadowCoord, positionWS, HS2ShadowMask);   // realtime and baked shadows mixed (URP)
#else
    Light main = GetMainLight(shadowCoord);
#endif
    main.shadowAttenuation = lerp(1, main.shadowAttenuation, HS2ShadowReceive);
#if defined(LIGHTMAP_ON) || defined(SHADOWS_SHADOWMASK)
    // Built-in's transparent queues drop the realtime shadow but keep the baked shadow mask (probed on Standard,
    // Phase 3.7 package L, l_std_lm_fit.txt lm_pm_mask): the mask alone, mixed as URP's MainLightShadow with no realtime term.
    if (HS2ShadowReceive == 0)
    {
    #if defined(CALCULATE_BAKED_SHADOWS)
        half bakedMain = BakedShadow(HS2ShadowMask, _MainLightOcclusionProbes, GetMainLightShadowParams());
    #else
        half bakedMain = 1;
    #endif
    #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
        half fadeMain = GetMainLightShadowFade(positionWS);
    #else
        half fadeMain = 1;
    #endif
        main.shadowAttenuation = MixRealtimeAndBakedShadows(1, bakedMain, fadeMain);
    }
#endif
    main.color *= HS2LightLayerWeight(main.layerMask);

    // Indirect: SH diffuse (or the lightmap) + reflection probe specular (Built-in's surfaceReduction/FresnelLerp).
    half pr = 1 - s.smoothness;
    half a = max(pr * pr, 0.002);
#if defined(LIGHTMAP_ON)
    half3 baked = HS2BakedGI;
    MixRealtimeAndBakedGI(main, n, baked);   // subtractive mode: the main light's shadow taken out of the lightmap
    half3 giDiffuse = baked * s.occlusion;
#else
    half3 giDiffuse = SampleSH(n) * s.occlusion;
#endif
    half3 env = s.glossyReflections > 0 ? GlossyEnvironmentReflection(reflect(-v, n), positionWS, pr, s.occlusion, screenUV)
                                        : _GlossyEnvironmentColor.rgb * s.occlusion;
    half nv = abs(dot(n, v));
    half surfaceReduction = 1.0 / (a * a + 1.0);
    half grazing = saturate(s.smoothness + (1 - s.oneMinusReflectivity));
    half3 fresnelLerp = lerp(s.specColor, grazing.xxx, pow(1 - nv, 5));
    half3 c = diffColor * giDiffuse + surfaceReduction * env * fresnelLerp;

    half3 mainColor = main.color * main.distanceAttenuation * main.shadowAttenuation;
    c += HS2DirectLight(s, v, main.direction, mainColor);
    c += s.transmission * saturate(-dot(n, main.direction)) * mainColor;
    // Amplify: translucency light = lerp(unshadowed, shadowed, _TransShadow).
    if (s.transStrength > 0) c += HS2Translucency(s, v, main.direction, main.color * main.distanceAttenuation * lerp(1, main.shadowAttenuation, s.transShadow), giDiffuse);
#if defined(_ADDITIONAL_LIGHTS)
    HS2LightIter it = HS2LightIterInit(positionWS, screenUV);
    uint lightIndex;
    [loop] while (HS2LightNext(it, lightIndex))
    {
        Light L = GetAdditionalLight(lightIndex, positionWS, HS2ShadowMask);
        if (!HS2LightReaches(L, lightIndex)) continue;
        L.shadowAttenuation = lerp(1, L.shadowAttenuation, HS2ShadowReceive);
        c += HS2DirectLight(s, v, L.direction, L.color * L.distanceAttenuation * L.shadowAttenuation);
        c += s.transmission * saturate(-dot(n, L.direction)) * L.color * L.distanceAttenuation * L.shadowAttenuation;
        if (s.transStrength > 0) c += HS2Translucency(s, v, L.direction, L.color * L.distanceAttenuation * lerp(1, L.shadowAttenuation, s.transShadow), giDiffuse);
    }
#endif
    return c + s.emission;
}
#endif
