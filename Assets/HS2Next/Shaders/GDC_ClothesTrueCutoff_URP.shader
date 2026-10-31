// URP replacement for the mod shader "GDC/Clothes True Cutoff" (mod clothes; Phase 3.7 package F2r2). Same property
// names, types and defaults as the original (an Amplify surface shader of the Hanmen clothes family, "v3" generation):
// composited colour / metal / gloss, region 4 by ColorMask.B, detail maps, translucency by MetallicGlossMap.B, transmission,
// 4x4 dither (_DITHERSWITCH_ON), tearing; Cull Off. Formulas in HS2NextHanmenV3.hlsl. Inferred by black-box probing only
// (Tests/FitCases/hv3_*.txt).
Shader "HS2Next/Compatible/GDC/Clothes True Cutoff"
{
    Properties
    {
        _Cutoff ("Cutoff", Range(0, 1)) = 0.5
        [NoScaleOffset] _DetailMainTex ("DetailMainTex", 2D) = "black" {}
        [NoScaleOffset] _MainTex ("MainTex", 2D) = "white" {}
        [NoScaleOffset] _ColorMask ("ColorMask", 2D) = "black" {}
        [NoScaleOffset] _BumpMap ("BumpMap", 2D) = "bump" {}
        [NoScaleOffset] _OcclusionMap ("", 2D) = "white" {}
        [NoScaleOffset] _WeatheringMap ("WeatheringMap", 2D) = "black" {}
        [NoScaleOffset] _MetallicGlossMap ("", 2D) = "white" {}
        [NoScaleOffset] _WeatheringMask ("WeatheringMask", 2D) = "black" {}
        [NoScaleOffset] _WetnessMap ("WetnessMap", 2D) = "black" {}
        _WetStreaksUV ("WetStreaksUV", Vector) = (1,1,0,0)
        _WetUV ("WetUV", Vector) = (1,1,0,0)
        _BaseColor ("BaseColor", Color) = (1,1,1,1)
        _Color ("Color", Color) = (1,1,1,1)
        _EmissionColor ("EmissionColor", Color) = (0,0,0,1)
        _WeatheringAlbedo ("WeatheringAlbedo", Color) = (0.6,0.65,0.65,0)
        _UVScalePattern ("UVScalePattern", Vector) = (1,1,0,0)
        _WeatheringUV ("WeatheringUV", Vector) = (1,1,0,0)
        _patternuvbase ("patternuvbase", Vector) = (1,1,0,0)
        _AlphaEx ("AlphaEx", Range(-1, 1)) = -1
        _CarvatureStrength ("CarvatureStrength", Range(0, 1)) = 0
        _Float0 ("Float0", Range(0, 3)) = 1
        [NoScaleOffset] _DetailMask ("", 2D) = "white" {}
        [NoScaleOffset] _DetailGlossMap ("DetailGlossMap", 2D) = "white" {}
        _DetailUV ("DetailUV", Vector) = (1,1,0,0)
        [NoScaleOffset] _DetailGlossMap2 ("DetailGlossMap2", 2D) = "white" {}
        _DetailUV2 ("DetailUV2", Vector) = (1,1,0,0)
        _DetailNormalMapScale ("DetailNormalMapScale", Range(0, 9)) = 9
        _DetailNormalMapScale2 ("DetailNormalMapScale2", Range(0, 9)) = 1
        _DetailGlossScale ("DetailGlossScale", Range(0, 1)) = 0
        _DetailGlossScale2 ("DetailGlossScale2", Range(0, 1)) = 0
        _DetailMetallicScale ("DetailMetallicScale", Range(0, 1)) = 0
        _DetailMetallicScale2 ("DetailMetallicScale2", Range(0, 1)) = 0
        _DetailOcculusionScale ("DetailOcculusionScale", Range(0, 1)) = 0
        _DetailOcculusionScale2 ("DetailOcculusionScale2", Range(0, 1)) = 0
        _DetailUVRotator ("DetailUVRotator", Range(-1, 1)) = 1
        _DetailUV2Rotator ("DetailUV2Rotator", Range(-1, 1)) = 0
        _EmissionStrength ("EmissionStrength", Range(0, 20)) = 0
        [Toggle(_EMISSIONCOLOR2_ON)] _EmissionColor2 ("Color1 is Emissive", Float) = 0
        [Toggle(_EMISSIONCOLOR3_ON)] _EmissionColor3 ("Color2 is Emissive", Float) = 0
        [Toggle(_EMISSIONCOLOR4_ON)] _EmissionColor4 ("Color3 is Emissive", Float) = 0
        _OcculusionStrength ("OcculusionStrength", Range(0, 1)) = 1
        _AlphaMaster ("AlphaMaster", Range(0, 1)) = 1
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
        _WeatheringGloss ("WeatheringGloss", Range(0, 2)) = 0.95
        _WeatheringEmission ("WeatheringEmission", Range(0, 1)) = 0.2
        _WeatheringUVSeed ("WeatheringUVSeed", Range(0, 1)) = 0
        _ExGloss ("ExGloss", Range(0, 1)) = 0
        _WetGlossBase ("WetGlossBase", Range(0, 2)) = 0.8
        _BodyColor1 ("BodyColor", Color) = (0.71,0.56,0.51,0)
        _WetAlbedoOffset ("WetAlbedoOffset", Range(0, 1)) = 0.6
        _WetSplatsAlbedoOffset ("WetSplatsAlbedoOffset", Range(0, 1)) = 0.6
        _WetAlpha ("WetAlpha", Range(0, 10)) = 0
        _WetOpacitySplats ("WetOpacitySplats", Range(0, 2)) = 0
        _WetOpacityStreaks ("WetOpacityStreaks", Range(0, 2)) = 0
        _WetGlossSplats ("WetGlossSplats", Range(0, 2)) = 0
        _WetBumpOffset ("WetBumpOffset", Range(0, 1)) = 0.5
        _WetColor ("WetColor", Color) = (0,0,0,0)
        [Toggle] _DitherSwitch ("DitherSwitch", Range(0, 1)) = 1
        [Toggle] _DitherBlueNoise ("DitherBlueNoise", Range(0, 1)) = 0
        [NoScaleOffset] _Noise ("Noise", 2D) = "white" {}
        [Toggle] _NormalBackDirInvert ("Backface Normal Direction Invert", Range(0, 1)) = 0
        _Translucency ("Strength", Range(0, 50)) = 1
        _TransmissionStrength ("TransmissionStrength", Range(0, 5)) = 1
        _TransNormalDistortion ("Normal Distortion", Range(0, 1)) = 0.1
        _TransScattering ("Scaterring Falloff", Range(1, 50)) = 2
        _TransDirect ("Direct", Range(0, 1)) = 1
        _TransAmbient ("Ambient", Range(0, 1)) = 0.2
        _TransShadow ("Shadow", Range(0, 1)) = 0.9
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
            #pragma vertex HS2HmV3Vert
            #pragma fragment HS2HmV3Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_local _ _DITHERSWITCH_ON
            #pragma multi_compile_local _ _DITHERBLUENOISE_ON
            #pragma dynamic_branch_local _RECEIVE_SHADOWS_OFF
            #define HS2_HV3_FORWARD
            #include "HS2NextHanmenV3.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            Cull Off
            HLSLPROGRAM
            #pragma vertex HS2HmV3ShadowVert
            #pragma fragment HS2HmV3DepthFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_local _ _DITHERSWITCH_ON
            #pragma multi_compile_local _ _DITHERBLUENOISE_ON
            #include "HS2NextHanmenV3.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            Cull Off
            HLSLPROGRAM
            #pragma vertex HS2HmV3Vert
            #pragma fragment HS2HmV3DepthFrag
            #pragma multi_compile_local _ _DITHERSWITCH_ON
            #pragma multi_compile_local _ _DITHERBLUENOISE_ON
            #include "HS2NextHanmenV3.hlsl"
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
            #pragma multi_compile_local _ _DITHERSWITCH_ON
            #pragma multi_compile_local _ _DITHERBLUENOISE_ON
            #include "HS2NextHanmenV3.hlsl"
            #define HS2_PASS_A HS2HmV3Attributes
            #define HS2_PASS_V HS2HmV3Varyings
            #define HS2_PASS_VERT HS2HmV3Vert
            #define HS2_PASS_CLIP HS2HmV3DepthFrag
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
            #pragma multi_compile_local _ _DITHERSWITCH_ON
            #pragma multi_compile_local _ _DITHERBLUENOISE_ON
            #include "HS2NextHanmenV3.hlsl"
            #define HS2_PASS_A HS2HmV3Attributes
            #define HS2_PASS_V HS2HmV3Varyings
            #define HS2_PASS_VERT HS2HmV3Vert
            #define HS2_PASS_CLIP HS2HmV3DepthFrag
            #define HS2_PASS_POS(a) (a).pos.xyz
            #define HS2_PASS_NORMAL(a) (a).n
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
    }
}
