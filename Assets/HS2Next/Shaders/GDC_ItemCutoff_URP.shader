// URP replacement for the mod shader "GDC/Item Cutoff" (character accessories of the GDC packs) and the identical 68-property
// copy of "Hanmen/Item Cutoff" (Rose Historical swords, Ekan; ShaderMap.CopyRoutes) (Phase 3.7, package F1r2). The
// original's property names, types, defaults and flags (its main bundle copy). Surface: HS2NextHanmenItem.hlsl with
// HS2_HM_GEN2 (the main Hanmen item model without specular occlusion, emission without the main texture,
// translucency strength T * saturate(T) with the ambient part lit by the main light, no tearing, Unity's abs(N.V), an
// 8x8 ordered dither of the alpha while _Cutoff > 0). Inferred by black-box probing only (Tests/FitCases/hmi_*.txt; the
// regression is the GDC / gen-2 "# fit:" lines of hmi_fit.txt).
Shader "HS2Next/Compatible/GDC/Item Cutoff"
{
    Properties
    {
        [IntRange] _CullMode ("CullMode", Range(0, 2)) = 0
        [NoScaleOffset] [Header (RGB Color A Cutoff)] _MainTex ("MainTex", 2D) = "white" {}
        [NoScaleOffset] _ColorMask ("ColorMask", 2D) = "black" {}
        [NoScaleOffset] _BumpMap ("BumpMap", 2D) = "bump" {}
        [NoScaleOffset] [Header (R_Detail1 G_Detail2)] _DetailMask ("DetailMask", 2D) = "white" {}
        [NoScaleOffset] [Header(Packed (R_Gloss G_Emission B_MetallicMask))] _MetallicGlossMap ("MetallicGlossMap", 2D) = "white" {}
        [NoScaleOffset] [Header (Detail1)] _DetailGlossMap ("DetailGlossMap", 2D) = "white" {}
        [NoScaleOffset] [Header (Detail2)] _DetailGlossMap2 ("DetailGlossMap2", 2D) = "white" {}
        [NoScaleOffset] _OcclusionMap ("OcclusionMap", 2D) = "white" {}
        _BaseColor ("BaseColor", Color) = (1, 1, 1, 1)
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Color2 ("Color2", Color) = (1, 1, 1, 1)
        _Color3 ("Color3", Color) = (1, 1, 1, 1)
        _Color4 ("Color4", Color) = (1, 1, 1, 1)
        _EmissionColor ("EmissionColor", Color) = (0, 0, 0, 1)
        _DetailUV ("DetailUV", Vector) = (1, 1, 0, 0)
        _DetailUV2 ("DetailUV2", Vector) = (1, 1, 0, 0)
        _DetailUVRotator ("DetailUVRotator", Range(-1, 1)) = 0
        _DetailUV2Rotator ("DetailUV2Rotator", Range(-1, 1)) = 0
        _CarvatureStrength ("CarvatureStrength", Range(0, 1)) = 0
        [Header (Bump)] _Float0 ("Float0", Range(0, 3)) = 1
        [Header(Detail Settings)] _DetailNormalMapScale ("DetailNormalMapScale", Range(0, 9)) = 1
        _DetailNormalMapScale2 ("DetailNormalMapScale2", Range(0, 9)) = 1
        _DetailGlossScale ("DetailGlossScale", Range(0, 1)) = 0
        _DetailGlossScale2 ("DetailGlossScale2", Range(0, 1)) = 0
        _DetailMetallicScale ("DetailMetallicScale", Range(0, 1)) = 0
        _DetailMetallicScale2 ("DetailMetallicScale2", Range(0, 1)) = 0
        _DetailOcculusionScale ("DetailOcculusionScale", Range(0, 1)) = 0
        _DetailOcculusionScale2 ("DetailOcculusionScale2", Range(0, 1)) = 0
        [Header (Emission (Tex Required))] _EmissionStrength ("EmissionStrength", Range(0, 20)) = 1
        [Header (Color Mask Required (Optional))] [Toggle(_EMISSIONCOLOR2_ON)] _EmissionColor2 ("Color1 is Emissive", Float) = 0
        [Toggle(_EMISSIONCOLOR3_ON)] _EmissionColor3 ("Color2 is Emissive", Float) = 0
        [Toggle(_EMISSIONCOLOR4_ON)] _EmissionColor4 ("Color3 is Emissive", Float) = 0
        [Header (Ambient Occlusion)] _OcculusionStrength ("OcculusionStrength", Range(0, 1)) = 1
        [Header (Glossiness Roughness)] _Glossiness ("Glossiness", Range(0, 1)) = 0
        _Glossiness2 ("Glossiness2", Range(0, 1)) = 0
        _Glossiness3 ("Glossiness3", Range(0, 1)) = 0
        _Glossiness4 ("Glossiness4", Range(0, 1)) = 0
        _Roughness ("Roughness", Range(0, 1)) = 1
        _Roughness2 ("Roughness2", Range(0, 1)) = 1
        _Roughness3 ("Roughness3", Range(0, 1)) = 1
        _Roughness4 ("Roughness4", Range(0, 1)) = 1
        [Header (Metallic Maps)] _Metallic ("Metallic", Range(0, 1)) = 0
        _Metallic2 ("Metallic2", Range(0, 1)) = 0
        _Metallic3 ("Metallic3", Range(0, 1)) = 0
        _Metallic4 ("Metallic4", Range(0, 1)) = 0
        _MetallicMask ("MetallicMask", Range(0, 1)) = 1
        _MetallicMask2 ("MetallicMask2", Range(0, 1)) = 1
        _MetallicMask3 ("MetallicMask3", Range(0, 1)) = 1
        _MetallicMask4 ("MetallicMask4", Range(0, 1)) = 1
        [Header(Wetness)] _ExGloss ("ExGloss", Range(0, 1)) = 0
        _WetnessPower ("WetnessPower", Range(0, 2)) = 0.8
        _WetAlbedoOffset ("WetAlbedoOffset", Range(0, 1)) = 0.6
        [HideInInspector] [Header(Translucency)] _Translucency ("Translucency", Range(0, 50)) = 1
        [Header(Dithering)] [Toggle] _DitherSwitch ("DitherSwitch", Range(0, 1)) = 1
        [Toggle] _DitherBlueNoise ("DitherBlueNoise", Range(0, 1)) = 0
        [NoScaleOffset] _Noise ("Noise", 2D) = "white" {}
        [Header(Backface Settings)] [Toggle] _NormalBackDirInvert ("Backface Normal Direction Invert", Range(0, 1)) = 0
        _TransmissionStrength ("TransmissionStrength", Range(0, 5)) = 0
        [Header(Cutout Setting)] _Cutoff ("Cutoff", Range(0, 1)) = 0.5
        [Header(Translucency)] _Translucency ("Strength", Range(0, 50)) = 1
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
            #pragma dynamic_branch_local _RECEIVE_SHADOWS_OFF
            #define HS2_HM_FORWARD
            #define HS2_HM_GEN2
            #include "HS2NextHanmenItem.hlsl"
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
            #define HS2_HM_GEN2
            #include "HS2NextHanmenItem.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            Cull [_CullMode]
            HLSLPROGRAM
            #pragma vertex HS2HanmenVert
            #pragma fragment HS2HanmenDepthFrag
            #define HS2_HM_GEN2
            #include "HS2NextHanmenItem.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode"="MotionVectors" }
            ColorMask RG
            Cull [_CullMode]
            HLSLPROGRAM
            #pragma vertex HS2MotionVert
            #pragma fragment HS2MotionFrag
            #define HS2_HM_GEN2
            #include "HS2NextHanmenItem.hlsl"
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
            Cull [_CullMode]
            HLSLPROGRAM
            #pragma vertex HS2DepthNormalsVert
            #pragma fragment HS2DepthNormalsFrag
            #define HS2_HM_GEN2
            #include "HS2NextHanmenItem.hlsl"
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
