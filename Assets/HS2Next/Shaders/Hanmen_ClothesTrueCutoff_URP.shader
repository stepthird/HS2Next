// URP replacement for the mod shader "Hanmen/Clothes True Cutoff" (mod clothes and Studio items; Phase 3.7 package F2).
// Same property names, types and defaults as the original (an Amplify surface shader, not a copy of AIT/Clothes True):
// colour from _MainTex x BaseColor x Color, metal / smoothness from the composited _DetailMainTex masked by the packed
// MetallicGlossMap, region 4 by ColorMask.B, wetness from _ExGloss and the wetness map, 8x8 dithered alpha switched on by
// _Cutoff > 0, tearing; Cull Off. Formulas in HS2NextHanmen.hlsl (HS2_HANMEN_CUTOFF). Inferred by black-box probing only
// (Tests/FitCases/hct_*.txt).
Shader "HS2Next/Compatible/Hanmen/Clothes True Cutoff"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("MainTex", 2D) = "white" {}
        [NoScaleOffset] _DetailMainTex ("DetailMainTex", 2D) = "gray" {}
        [NoScaleOffset] _ColorMask ("ColorMask", 2D) = "black" {}
        [NoScaleOffset] _BumpMap ("BumpMap", 2D) = "bump" {}
        [NoScaleOffset] _DetailMask ("DetailMask", 2D) = "black" {}
        [NoScaleOffset] _DetailGlossMap ("DetailGlossMap", 2D) = "gray" {}
        [NoScaleOffset] _DetailGlossMap2 ("DetailGlossMap2", 2D) = "gray" {}
        [NoScaleOffset] _MetallicGlossMap ("MetallicGlossMap", 2D) = "white" {}
        [NoScaleOffset] _OcclusionMap ("OcclusionMap", 2D) = "white" {}
        [NoScaleOffset] _WeatheringMap ("WeatheringMap", 2D) = "black" {}
        [NoScaleOffset] _WeatheringMask ("WeatheringMask", 2D) = "black" {}
        [NoScaleOffset] _WetnessMap ("WetnessMap", 2D) = "black" {}
        [NoScaleOffset] _NoiseTexture ("NoiseTexture", 2D) = "white" {}
        _BaseColor ("BaseColor", Color) = (1,1,1,1)
        _Color ("Color", Color) = (1,1,1,1)
        _EmissionColor ("EmissionColor", Color) = (0,0,0,1)
        _WeatheringAlbedo1 ("WeatheringAlbedo", Color) = (0.6,0.65,0.65,0)
        _UVScalePattern ("UVScalePattern", Vector) = (1,1,0,0)
        _DetailUV ("DetailUV", Vector) = (1,1,0,0)
        _DetailUV2 ("DetailUV2", Vector) = (1,1,0,0)
        _DetailUVRotator ("DetailUVRotator", Range(1, 360)) = 1
        _DetailUVRotator2 ("DetailUVRotator2", Range(1, 360)) = 1
        _WeatheringUV ("WeatheringUV", Vector) = (1,1,0,0)
        _patternuv1 ("patternuv1", Vector) = (1,1,0,0)
        _patternuv2 ("patternuv2", Vector) = (1,1,0,0)
        _patternuv3 ("patternuv3", Vector) = (1,0,0,0)
        _patternuvbase ("patternuvbase", Vector) = (1,1,0,0)
        _AlphaEx ("AlphaEx", Range(0, 1)) = 1
        _ExGloss ("ExGloss", Range(0, 1)) = 0
        _WetnessPower ("WetnessPower", Range(0, 2)) = 0
        _WetnessPower2 ("WetnessPower2", Range(0, 2)) = 0
        _BumpScale ("BumpScale", Range(0, 3)) = 1
        _DetailNormalMapScale ("DetailNormalMapScale", Range(-3, 9)) = 1
        _DetailNormalMapScale2 ("DetailNormalMapScale2", Range(-3, 9)) = 1
        _EmissionStrength ("EmissionStrength", Range(0, 20)) = 0
        [Toggle(_EMISSIONCOLOR1_ON)] _EmissionColor1 ("Color1 is Emissive", Float) = 0
        [Toggle(_EMISSIONCOLOR2_ON)] _EmissionColor2 ("Color2 is Emissive", Float) = 0
        [Toggle(_EMISSIONCOLOR3_ON)] _EmissionColor3 ("Color3 is Emissive", Float) = 0
        _OcclusionStrength ("OcclusionStrength", Range(0, 1)) = 1
        [Toggle(_DETAILOCCLUSION_ON)] _DetailOcclusion ("DetailOcclusion", Float) = 0
        _DetailOcclusionScale ("DetailOcclusionScale", Range(0, 1)) = 0
        _DetailOcclusionScale2 ("DetailOcclusionScale2", Range(0, 1)) = 0
        _DetailOcclusionContrast2 ("DetailOcclusionContrast2", Range(0, 2)) = 0
        _DetailOcclusionContrast ("DetailOcclusionContrast", Range(0, 2)) = 0
        _Roughness ("Roughness", Range(0, 1)) = 1
        _MetallicMask ("MetallicMask", Range(0, 1)) = 1
        _Glossiness4 ("Glossiness4", Range(0, 1)) = 1
        _Roughness4 ("Roughness4", Range(0, 1)) = 1
        _Metallic4 ("Metallic4", Range(0, 1)) = 1
        _MetallicMask4 ("MetallicMask4", Range(0, 1)) = 1
        _WeatheringAll ("WeatheringAll", Range(0, 1)) = 0
        _WeatheringRange1 ("WeatheringRange1", Range(0, 1)) = 0
        _WeatheringRange2 ("WeatheringRange2", Range(0, 1)) = 0
        _WeatheringRange3 ("WeatheringRange3", Range(0, 1)) = 0
        _WeatheringRange4 ("WeatheringRange4", Range(0, 1)) = 0
        _WeatheringRange5 ("WeatheringRange5", Range(0, 1)) = 0
        _WeatheringGloss ("WeatheringGloss", Range(0, 2)) = 0.8
        _WeatheringBump ("WeatheringBump", Range(0, 2)) = 0.4
        _WeatheringBumpPower ("WeatheringBumpPower", Range(0, 9)) = 2
        [Toggle(_BLUENOISEDITHER_ON)] _BlueNoiseDither ("BlueNoiseDither", Float) = 0
        [Toggle(_ENABLEALPHAMASK1_ON)] _EnableAlphaMask1 ("EnableAlphaMask", Float) = 0
        _Cutoff ("Cutoff", Range(0, 1)) = 0
        [HideInInspector] _texcoord ("", 2D) = "white" {}
        [HideInInspector] __dirty ("", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
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
            #include "HS2NextHanmen.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            Cull Off
            HLSLPROGRAM
            #pragma vertex HS2HanmenShadowVert
            #pragma fragment HS2HanmenDepthFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #define HS2_HANMEN_CUTOFF
            #include "HS2NextHanmen.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            Cull Off
            HLSLPROGRAM
            #pragma vertex HS2HanmenVert
            #pragma fragment HS2HanmenDepthFrag
            #define HS2_HANMEN_CUTOFF
            #include "HS2NextHanmen.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode"="MotionVectors" }
            ColorMask RG
            Cull Off
            HLSLPROGRAM
            #pragma vertex HS2MotionVert
            #pragma fragment HS2MotionFrag
            #define HS2_HANMEN_CUTOFF
            #include "HS2NextHanmen.hlsl"
            #define HS2_PASS_A HS2HanmenAttributes
            #define HS2_PASS_V HS2HanmenVaryings
            #define HS2_PASS_VERT HS2HanmenVert
            #define HS2_PASS_CLIP HS2HanmenDepthFrag
            #define HS2_PASS_POS(a) (a).pos.xyz
            #define HS2_PASS_NORMAL(a) (a).n
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ColorMask RGBA
            Cull Off
            HLSLPROGRAM
            #pragma vertex HS2DepthNormalsVert
            #pragma fragment HS2DepthNormalsFrag
            #define HS2_HANMEN_CUTOFF
            #include "HS2NextHanmen.hlsl"
            #define HS2_PASS_A HS2HanmenAttributes
            #define HS2_PASS_V HS2HanmenVaryings
            #define HS2_PASS_VERT HS2HanmenVert
            #define HS2_PASS_CLIP HS2HanmenDepthFrag
            #define HS2_PASS_POS(a) (a).pos.xyz
            #define HS2_PASS_NORMAL(a) (a).n
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
    }
}
