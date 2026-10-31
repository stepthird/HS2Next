// URP replacement for the mod shader "Hanmen/Item Transparent" (earrings and other blended accessories; Phase 5.2 package S):
// the v0.9 surface of the older Hanmen/Item Cutoff copies (the same 49 inputs under the same names: _BaseColor, _Color,
// _Color2.._Color4) blended SrcAlpha / OneMinusSrcAlpha with ZWrite Off and Cull Off, alpha = MainTex.a x the regions'
// weighted colour alpha, not saturated (HS2NextHanmen.hlsl HS2_HANMEN_V09_BLEND); black-box probed on the bundle copy
// (Tests/FitCases/s52_itrans_fit.txt).
Shader "HS2Next/Compatible/Hanmen/Item Transparent"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("MainTex", 2D) = "white" {}
        [NoScaleOffset] _ColorMask ("ColorMask", 2D) = "black" {}
        [NoScaleOffset] _BumpMap ("BumpMap", 2D) = "bump" {}
        [NoScaleOffset] _DetailMask ("DetailMask", 2D) = "black" {}
        [NoScaleOffset] _DetailGlossMap ("DetailGlossMap", 2D) = "gray" {}
        [NoScaleOffset] _DetailGlossMap2 ("DetailGlossMap2", 2D) = "gray" {}
        [NoScaleOffset] _MetallicGlossMap ("MetallicGlossMap", 2D) = "white" {}
        [NoScaleOffset] _OcclusionMap ("OcclusionMap", 2D) = "white" {}
        _BaseColor ("BaseColor", Color) = (1,1,1,1)
        _Color ("Color", Color) = (1,1,1,1)
        _Color2 ("Color2", Color) = (1,1,1,1)
        _Color3 ("Color3", Color) = (1,1,1,1)
        _Color4 ("Color4", Color) = (1,1,1,1)
        _EmissionColor ("EmissionColor", Color) = (0,0,0,1)
        _DetailUV ("DetailUV", Vector) = (1,1,0,0)
        _DetailUV2 ("DetailUV2", Vector) = (1,1,0,0)
        _DetailUVRotator ("DetailUVRotator", Range(-1, 1)) = 0
        _DetailUVRotator2 ("DetailUVRotator2", Range(-1, 1)) = 0
        _BumpScale ("BumpScale", Range(0, 3)) = 1
        _DetailNormalMapScale ("DetailNormalMapScale", Range(-3, 9)) = 1
        _DetailNormalMapScale2 ("DetailNormalMapScale2", Range(-3, 9)) = 1
        _EmissionStrength ("EmissionStrength", Range(0, 20)) = 1
        _EmissionColor1 ("Color1 is Emissive", Float) = 0
        _EmissionColor2 ("Color2 is Emissive", Float) = 0
        _EmissionColor3 ("Color3 is Emissive", Float) = 0
        _OcclusionStrength ("OcclusionStrength", Range(0, 1)) = 1
        _DetailOcclusion ("DetailOcclusion", Float) = 0
        _DetailOcclusionScale ("DetailOcclusionScale", Range(0, 1)) = 0
        _DetailOcclusionScale2 ("DetailOcclusionScale2", Range(0, 1)) = 0
        _DetailOcclusionContrast2 ("DetailOcclusionContrast2", Range(0, 2)) = 0
        _DetailOcclusionContrast ("DetailOcclusionContrast", Range(0, 2)) = 0
        _Glossiness ("Glossiness", Range(0, 1)) = 1
        _Glossiness2 ("Glossiness2", Range(0, 1)) = 1
        _Glossiness3 ("Glossiness3", Range(0, 1)) = 1
        _Glossiness4 ("Glossiness4", Range(0, 1)) = 1
        _Roughness ("Roughness", Range(0, 1)) = 1
        _Roughness2 ("Roughness2", Range(0, 1)) = 1
        _Roughness3 ("Roughness3", Range(0, 1)) = 1
        _Roughness4 ("Roughness4", Range(0, 1)) = 1
        _Metallic ("Metallic", Range(0, 1)) = 1
        _Metallic2 ("Metallic2", Range(0, 1)) = 1
        _Metallic3 ("Metallic3", Range(0, 1)) = 1
        _Metallic4 ("Metallic4", Range(0, 1)) = 1
        _MetallicMask ("MetallicMask", Range(0, 1)) = 1
        _MetallicMask2 ("MetallicMask2", Range(0, 1)) = 1
        _MetallicMask3 ("MetallicMask3", Range(0, 1)) = 1
        _MetallicMask4 ("MetallicMask4", Range(0, 1)) = 1
        [HideInInspector] _texcoord ("_texcoord", 2D) = "white" {}
        [HideInInspector] __dirty ("__dirty", Float) = 1
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
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
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
            #define HS2_HANMEN_V09
            #define HS2_HANMEN_V09_BLEND
            #define HS2_V09_BASE _BaseColor
            #define HS2_V09_C1 _Color
            #define HS2_V09_C2 _Color2
            #define HS2_V09_C3 _Color3
            #define HS2_V09_C4 _Color4
            #define HS2_V09_ALPHAEX 1
            #define HS2_V09_NO_WETMAP
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
            #define HS2_HANMEN_V09
            #define HS2_HANMEN_V09_BLEND
            #define HS2_V09_BASE _BaseColor
            #define HS2_V09_C1 _Color
            #define HS2_V09_C2 _Color2
            #define HS2_V09_C3 _Color3
            #define HS2_V09_C4 _Color4
            #define HS2_V09_ALPHAEX 1
            #define HS2_V09_NO_WETMAP
            #include "HS2NextHanmen.hlsl"
            ENDHLSL
        }
    }
}
