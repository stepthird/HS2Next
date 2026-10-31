// URP replacement for the mod shader "Hanmen/Item Alpha" (car windows and other blended mod item parts; Phase 3.7, package
// F1): the alpha-blended sibling of "Hanmen/Item Cutoff" (its main bundle copy's 75 properties, names, types, defaults and
// flags). SrcAlpha / OneMinusSrcAlpha, ZWrite Off, colour mask RGB, Cull [_CullMode] (default Back), no realtime shadows
// received (transparent queue). Surface: HS2NextHanmenItem.hlsl with HS2_HM_ALPHA. Inferred by black-box probing only
// (Editor/Fit/SkinFit.cs, Tests/FitCases/hmi_*.txt).
Shader "HS2Next/Compatible/Hanmen/Item Alpha"
{
    Properties
    {
        [IntRange] _CullMode ("CullMode", Range(0, 2)) = 2
        [Header(Translucency)] _Translucency ("Translucency", Range(0, 50)) = 1
        _TransDirect ("Direct", Range(0, 1)) = 0
        _TransAmbient ("Ambient", Range(0, 1)) = 0.1
        [NoScaleOffset] [Header(Packed (R_Gloss G_Emission B_MetallicMask))] _MetallicGlossMap ("MetallicGlossMap", 2D) = "white" {}
        _TransNormalDistortion ("Normal Distortion", Range(0, 1)) = 0
        _TransScattering ("Scattering Faloff", Range(1, 50)) = 1
        _TransShadow ("TransShadow", Range(0, 1)) = 1
        [Header(Backface Settings)] [Toggle] _NormalBackDirInvert ("Backface Normal Direction Invert", Range(0, 1)) = 1
        [Toggle(_DITHERSWITCH_ON)] _DitherSwitch ("DitherSwitch", Float) = 0
        [Toggle(_DITHERBLUENOISE_ON)] _DitherBluenoise ("DitherBluenoise", Float) = 0
        [NoScaleOffset] _Noise ("Noise", 2D) = "white" {}
        [NoScaleOffset] [Header (RGB Color A Cutoff)] _MainTex ("MainTex", 2D) = "white" {}
        [NoScaleOffset] _ColorMask ("ColorMask", 2D) = "black" {}
        [NoScaleOffset] _BumpMap ("BumpMap", 2D) = "bump" {}
        [NoScaleOffset] [Header (R_Detail1 G_Detail2)] _DetailMask ("DetailMask", 2D) = "white" {}
        [NoScaleOffset] [Header (Detail1)] _DetailGlossMap ("DetailGlossMap", 2D) = "white" {}
        [NoScaleOffset] [Header (Detail2)] _DetailGlossMap2 ("DetailGlossMap2", 2D) = "white" {}
        [Toggle(_METALLICGLOSSMAP)] _METALLICGLOSSMAP ("METALLICGLOSSMAP", Float) = 1
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
        [Header (Emission (Tex Required))] _EmissionStrength ("EmissionStrength", Range(0, 20)) = 1
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
        [Header(Cutout Setting)] _Cutoff ("Cutoff", Range(0, 1)) = 0.5
        [Toggle(_IRIDISCENT_ON)] _Iridiscent ("Iridiscent", Float) = 0
        _IriHue ("Hue", Range(0, 1)) = 0
        _IriSaturation ("Saturation", Range(0, 1)) = 0.5
        _IriBrightness ("Brightness", Range(0, 1)) = 0.5
        _IriContrast ("Contrast", Range(0, 1)) = 0.5
        [Toggle(_SPECULARSETUP_ON)] _SpecularSetup ("SpecularSetup", Float) = 0
        _SpecColor ("SpecColor", Color) = (0.5, 0.5, 0.5, 1)
        [NoScaleOffset] _SpecularMap ("SpecularMap", 2D) = "white" {}
        [HideInInspector] _texcoord ("", 2D) = "white" {}
        [HideInInspector] __dirty ("", Float) = 1
        [HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_CullMode]
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
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
            #pragma multi_compile_local _ _DITHERSWITCH_ON
            #define HS2_HM_FORWARD
            #define HS2_HM_ALPHA
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
            #pragma multi_compile_local _ _DITHERSWITCH_ON
            #define HS2_HM_ALPHA
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
            #pragma multi_compile_local _ _DITHERSWITCH_ON
            #define HS2_HM_ALPHA
            #include "HS2NextHanmenItem.hlsl"
            ENDHLSL
        }
    }
}
