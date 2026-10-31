// URP replacement for the 87-property bundle copy of "Hanmen/Clothes True Alpha" (Phase 3.7 package F2r2; CopyRoutes).
// Same property names, types and defaults as the original (an Amplify surface shader, "v2" generation of the Hanmen
// clothes shaders): the 71-property True Cutoff copy's surface, blended with [_BlendSCR] / [_BlendDST], alpha from
// MainTex.a, AlphaMaster and an alpha fresnel; Cull [_CullMode]. Formulas in HS2NextHanmen.hlsl (HS2_HANMEN_V2,
// HS2_HANMEN_BLEND, HS2_HANMEN_TA). Inferred by black-box probing only (Tests/FitCases/hv2_*.txt).
Shader "HS2Next/Compatible/Hanmen/Clothes True Alpha (v2 copy)"
{
    Properties
    {
        [IntRange] _CullMode ("CullMode", Range(0, 2)) = 0
        [NoScaleOffset] _MainTex ("MainTex", 2D) = "white" {}
        [NoScaleOffset] _DetailMainTex ("DetailMainTex", 2D) = "gray" {}
        [NoScaleOffset] _ColorMask ("ColorMask", 2D) = "black" {}
        [NoScaleOffset] _BumpMap ("BumpMap", 2D) = "bump" {}
        [NoScaleOffset] _DetailMask ("DetailMask", 2D) = "white" {}
        [NoScaleOffset] _DetailGlossMap ("DetailGlossMap", 2D) = "white" {}
        [NoScaleOffset] _DetailGlossMap2 ("DetailGlossMap2", 2D) = "white" {}
        [NoScaleOffset] _MetallicGlossMap ("MetallicGlossMap", 2D) = "white" {}
        [NoScaleOffset] _OcclusionMap ("OcclusionMap", 2D) = "white" {}
        [NoScaleOffset] _WeatheringMap ("WeatheringMap", 2D) = "black" {}
        [NoScaleOffset] _WeatheringMask ("WeatheringMask", 2D) = "black" {}
        [NoScaleOffset] _WetnessMap ("WetnessMap", 2D) = "white" {}
        _BaseColor ("BaseColor", Color) = (1,1,1,1)
        _Color ("Color", Color) = (1,1,1,1)
        _EmissionColor ("EmissionColor", Color) = (0,0,0,1)
        _WeatheringAlbedo ("WeatheringAlbedo", Color) = (0.6,0.65,0.65,0)
        _UVScalePattern ("UVScalePattern", Vector) = (1,1,0,0)
        _WeatheringUV ("WeatheringUV", Vector) = (1,1,0,0)
        _patternuv1 ("patternuv1", Vector) = (1,1,0,0)
        _patternuv2 ("patternuv2", Vector) = (1,1,0,0)
        _WetUV ("WetUV", Vector) = (1,1,0,0)
        _patternuv3 ("patternuv3", Vector) = (1,0,0,0)
        _patternuvbase ("patternuvbase", Vector) = (1,1,0,0)
        _AlphaEx ("AlphaEx", Range(0, 1)) = 1
        _CarvatureStrength ("CarvatureStrength", Range(0, 1)) = 0
        _BumpScale ("BumpScale", Range(0, 3)) = 1
        _DetailNormalMapScale ("DetailNormalMapScale", Range(-3, 10)) = 10
        _DetailNormalMapScale2 ("DetailNormalMapScale2", Range(-3, 10)) = 10
        _DetailGlossScale ("DetailGlossScale", Range(0, 1)) = 0
        _DetailGlossScale2 ("DetailGlossScale2", Range(0, 1)) = 0
        _DetailMetallicScale ("DetailMetallicScale", Range(0, 1)) = 0
        _DetailMetallicScale2 ("DetailMetallicScale2", Range(0, 1)) = 0
        _DetailOcclusionScale ("DetailOcclusionScale", Range(0, 1)) = 0
        _DetailOcclusionScale2 ("DetailOcclusionScale2", Range(0, 1)) = 0
        _DetailOcclusionContrast2 ("DetailOcclusionContrast2", Range(0, 2)) = 0
        _DetailOcclusionContrast ("DetailOcclusionContrast", Range(0, 2)) = 0
        _DetailUVRotator ("DetailUVRotator", Range(-1, 1)) = 0
        _DetailUV2Rotator ("DetailUV2Rotator", Range(-1, 1)) = 0
        _DetailUV ("DetailUV", Vector) = (1,1,0,0)
        _DetailUV2 ("DetailUV2", Vector) = (1,1,0,0)
        _EmissionStrength ("EmissionStrength", Range(0, 20)) = 0
        [Toggle(_EMISSIONCOLOR1_ON)] _EmissionColor1 ("Color1 is Emissive", Float) = 0
        [Toggle(_EMISSIONCOLOR2_ON)] _EmissionColor2 ("Color2 is Emissive", Float) = 0
        [Toggle(_EMISSIONCOLOR3_ON)] _EmissionColor3 ("Color3 is Emissive", Float) = 0
        _OcclusionStrength ("OcclusionStrength", Range(0, 1)) = 1
        _Roughness ("Roughness", Range(0, 1)) = 1
        _MetallicMask ("MetallicMask", Range(0, 1)) = 1
        _Glossiness4 ("Glossiness4", Range(0, 1)) = 1
        _Roughness4 ("Roughness4", Range(0, 1)) = 1
        _Metallic4 ("Metallic4", Range(0, 1)) = 1
        _MetallicMask4 ("MetallicMask4", Range(0, 1)) = 1
        _Translucency ("Strength", Range(0, 50)) = 0
        _TransNormalDistortion ("Normal Distortion", Range(0, 1)) = 0.1
        _TransScattering ("Scaterring Falloff", Range(1, 50)) = 2
        _TransDirect ("Direct", Range(0, 1)) = 1
        _TransAmbient ("Ambient", Range(0, 1)) = 0.2
        _TransShadow ("Shadow", Range(0, 1)) = 0.9
        _WeatheringAll ("WeatheringAll", Range(0, 1)) = 0
        _WeatheringRange1 ("WeatheringRange1", Range(0, 1)) = 0
        _WeatheringRange2 ("WeatheringRange2", Range(0, 1)) = 0
        _WeatheringRange3 ("WeatheringRange3", Range(0, 1)) = 0
        _WeatheringRange4 ("WeatheringRange4", Range(0, 1)) = 0
        _WeatheringRange5 ("WeatheringRange5", Range(0, 1)) = 0
        _WeatheringGloss ("WeatheringGloss", Range(0, 2)) = 0.8
        _WeatheringBump ("WeatheringBump", Range(0, 2)) = 0.4
        _WeatheringBumpPower ("WeatheringBumpPower", Range(0, 9)) = 2
        _FresnelPower ("FresnelPower", Range(0, 10)) = 5
        _FresnelBias ("FresnelBias", Range(0, 1)) = 5
        _FresnelScale ("FresnelScale", Range(0, 10)) = 5
        _ExGloss ("ExGloss", Range(0, 1)) = 0
        _BodyColor ("BodyColor", Color) = (0,0,0,0)
        _WetAlbedoOffset ("WetAlbedoOffset", Range(0, 1)) = 0.6
        _WetGlossBase ("WetGlossBase", Range(0, 2)) = 0.8
        _WetGlossSplats ("WetGlossSplats", Range(0, 2)) = 0
        _WetSplatsAlbedoOffset ("WetSplatsAlbedoOffset", Range(0, 1)) = 0.6
        _WetColor ("WetColor", Color) = (0,0,0,0)
        _WetAlpha ("WetAlpha", Range(0, 10)) = 0
        _WetOpacitySplats ("WetOpacitySplats", Range(0, 2)) = 0
        _WetBumpOffset ("WetBumpOffset", Range(0, 1)) = 0.5
        _AlphaMaster ("AlphaMaster", Range(0, 1)) = 0
        _Cutoff ("Cutoff", Range(0.01, 1)) = 0.01
        [IntRange] _BlendSCR ("BlendSCR", Range(1, 11)) = 5
        [IntRange] _BlendDST ("BlendDST", Range(1, 11)) = 11
        [Toggle] _AlphaToMask ("AlphaToMask", Range(0, 1)) = 0
        [HideInInspector] _texcoord ("", 2D) = "white" {}
        [HideInInspector] __dirty ("", Float) = 1
        [HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend [_BlendSCR] [_BlendDST]
            ZWrite Off
            Cull [_CullMode]
            HLSLPROGRAM
            #pragma vertex HS2HanmenVert
            #pragma fragment HS2HanmenFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #define HS2_HANMEN_CUTOFF
            #define HS2_HANMEN_V2
            #define HS2_HANMEN_BLEND
            #define HS2_HANMEN_TA
            #include "HS2NextHanmen.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            Cull [_CullMode]
            HLSLPROGRAM
            #pragma vertex HS2HanmenShadowVert
            #pragma fragment HS2HanmenDepthFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #define HS2_HANMEN_CUTOFF
            #define HS2_HANMEN_V2
            #define HS2_HANMEN_BLEND
            #define HS2_HANMEN_TA
            #include "HS2NextHanmen.hlsl"
            ENDHLSL
        }
    }
}
