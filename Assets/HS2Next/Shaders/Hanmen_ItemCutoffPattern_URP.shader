// URP replacement for the 91-property bundle copy of the mod shader "Hanmen/Item Cutoff" (four road maps, rifle slings;
// ShaderMap.CopyRoutes) (Phase 3.7, package F1r2). That copy's property names, types, defaults and flags. Surface:
// HS2NextHanmenItem.hlsl with HS2_HM_P91: lerp-chain colour regions with Studio patterns (_PatternMask1..3, _Color*_2,
// _patternuv*), MetallicGloss R / G / B / A = metallic mask / emission / translucency mask / gloss, emission x EffectMap,
// no specular occlusion, translucency linear in T x the mask with the ambient part lit by the main light, an
// interleaved-gradient-noise dither of alpha x _alpha while _Cutoff > 0. Inferred by black-box probing only (regression
// Tests/FitCases/hmi_p91_fit.txt).
Shader "HS2Next/Compatible/Hanmen/Item Cutoff (pattern copy)"
{
    Properties
    {
        [Header(Backface Settings)] [IntRange] _CullMode ("CullMode", Range(0, 2)) = 0
        [Toggle] _NormalBackDirInvert ("Normal Invert", Range(0, 1)) = 1
        [Header(Alpha Clip Value)] _Cutoff ("Cutoff", Range(0, 1)) = 0.5
        _alpha ("alpha", Range(0, 1)) = 1
        [NoScaleOffset] [Header (RGB Color A Opacity)] _MainTex ("MainTex", 2D) = "white" {}
        [NoScaleOffset] [Header((R) MetallicMask)] [Header((G) Emission)] [Header((B) Thickness)] [Header((A) Roughness)] _MetallicGlossMap ("MetallicGlossMap", 2D) = "white" {}
        [NoScaleOffset] _ColorMask ("ColorMask", 2D) = "black" {}
        [NoScaleOffset] _BumpMap ("BumpMap", 2D) = "bump" {}
        [NoScaleOffset] [Header (R_Detail1 G_Detail2)] _DetailMask ("DetailMask", 2D) = "white" {}
        [NoScaleOffset] [Header (Detail1)] _DetailGlossMap ("DetailGlossMap", 2D) = "white" {}
        [NoScaleOffset] [Header (Detail2)] _DetailGlossMap2 ("DetailGlossMap2", 2D) = "white" {}
        [NoScaleOffset] [Header((R) Carvature (Edge Map))] [Header((G) OcclusionMap)] [Header((B) Tearings Map)] _OcclusionMap ("OcclusionMap", 2D) = "white" {}
        _BaseColor ("BaseColor", Color) = (1, 1, 1, 1)
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Color2 ("Color2", Color) = (1, 1, 1, 1)
        _Color3 ("Color3", Color) = (1, 1, 1, 1)
        _Color4 ("Color4", Color) = (1, 1, 1, 1)
        _DetailUV ("DetailUV", Vector) = (1, 1, 0, 0)
        _DetailUV2 ("DetailUV2", Vector) = (1, 1, 0, 0)
        _DetailUVRotator ("DetailUVRotator", Range(-1, 1)) = 0
        _DetailUV2Rotator ("DetailUV2Rotator", Range(-1, 1)) = 0
        [Header (Cloth Tearings)] _AlphaEx ("AlphaEx", Range(-1, 1)) = 1
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
        _RoughnessReflections ("RoughnessReflections", Range(0, 1)) = 1
        _MetallicMask ("MetallicMask", Range(0, 1)) = 1
        _MetallicMask2 ("MetallicMask2", Range(0, 1)) = 1
        _GlossinessReflection ("GlossinessReflection", Range(0, 1)) = 1
        _MetallicMask3 ("MetallicMask3", Range(0, 1)) = 1
        _MetallicMask4 ("MetallicMask4", Range(0, 1)) = 1
        [Header(Wetness)] _ExGloss ("ExGloss", Range(0, 1)) = 0
        _WetnessPower ("WetnessPower", Range(0, 2)) = 0.8
        _WetAlbedoOffset ("WetAlbedoOffset", Range(0, 1)) = 0.6
        [Header (UV Coordinates)] _UVScalePattern ("UVScalePattern", Vector) = (1, 1, 0, 0)
        [NoScaleOffset] [Header(Patterns)] _PatternMask1 ("PatternMask1", 2D) = "white" {}
        _Color1_2 ("Color1_2", Color) = (1, 1, 1, 1)
        [NoScaleOffset] _PatternMask2 ("PatternMask2", 2D) = "white" {}
        _Color2_2 ("Color2_2", Color) = (1, 1, 1, 1)
        [NoScaleOffset] _PatternMask3 ("PatternMask3", 2D) = "white" {}
        _Color3_2 ("Color3_2", Color) = (1, 1, 1, 1)
        _patternuv1 ("patternuv1", Vector) = (1, 1, 0, 0)
        _patternuv2 ("patternuv2", Vector) = (1, 1, 0, 0)
        _patternuv3 ("patternuv3", Vector) = (1, 1, 0, 0)
        _patternuv3Rotator ("patternuv3Rotator", Range(-1, 1)) = 0
        _patternuv1Rotator ("patternuv1Rotator", Range(-1, 1)) = 0
        _patternuv2Rotator ("patternuv2Rotator", Range(-1, 1)) = 0
        [Header (Emission (Tex Required))] _EmissionStrength ("EmissionStrength", Range(0, 20)) = 0
        _EmissionColor ("EmissionColor", Color) = (0, 0, 0, 1)
        [Toggle(_EMISSIONCOLORTEXBASE_ON)] _EmissionColorTexBase ("EmissionColorTexBase", Float) = 0
        [NoScaleOffset] [Header(Emission Effect)] _EffectMap ("EffectMap", 2D) = "white" {}
        _UVScroll ("UVScroll", Vector) = (1, 1, 0, 0)
        _UVScrollRotator ("UVScrollRotator", Range(-1, 1)) = 0
        [Header(Translucency)] _Translucency ("Translucency", Range(0, 50)) = 1
        _TransDirect ("Direct", Range(0, 1)) = 0
        _TransAmbient ("Ambient", Range(0, 1)) = 0.1
        _TransNormalDistortion ("Normal Distortion", Range(0, 1)) = 0
        _TransScattering ("Scattering Faloff", Range(1, 50)) = 1
        _TransShadow ("TransShadow", Range(0, 1)) = 1
        [Header(Fresnel Settings)] _FresnelPower ("FresnelPower", Range(0, 10)) = 0
        _FresnelScale ("FresnelScale", Range(0, 10)) = 0
        [Header(Iridiscent)] [Toggle(_IRIDISCENT_ON)] _Iridiscent ("Iridiscent", Float) = 0
        _IriHue ("Hue", Range(0, 1)) = 0
        _IriSaturation ("Saturation", Range(0, 1)) = 0.5
        _IriBrightness ("Brightness", Range(0, 1)) = 0.5
        _IriContrast ("Contrast", Range(0, 1)) = 0.5
        [Header(Specularity)] [Toggle(_SPECULARSETUP_ON)] _SpecularSetup ("SpecularSetup", Float) = 0
        _SpecColor ("SpecColor", Color) = (0.5094, 0.5094, 0.5094, 1)
        [NoScaleOffset] _SpecularMap ("SpecularMap", 2D) = "white" {}
        [HideInInspector] [Toggle(_SHADERTYPE_ITEM)] _SHADERTYPE_ITEM ("SHADERTYPE_ITEM", Float) = 1
        [HideInInspector] _texcoord ("", 2D) = "white" {}
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
            #pragma multi_compile_local _ _EMISSIONCOLORTEXBASE_ON
            #pragma dynamic_branch_local _RECEIVE_SHADOWS_OFF
            #define HS2_HM_FORWARD
            #define HS2_HM_P91
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
            #define HS2_HM_P91
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
            #define HS2_HM_P91
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
            #define HS2_HM_P91
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
            #define HS2_HM_P91
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
