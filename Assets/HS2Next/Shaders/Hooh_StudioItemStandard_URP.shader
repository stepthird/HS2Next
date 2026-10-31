// URP replacement for a mod shader of the hooh "ClotheReplica" family (Studio items and mod clothes; Phase 3.7 package
// F2). Same property names, types and defaults as the original (an Amplify surface shader); the colour, pattern and
// emission properties have no visible effect in the original and are kept unused. Formulas in HS2NextHooh.hlsl.
// Inferred by black-box probing only (Tests/FitCases/hooh_*.txt).
Shader "HS2Next/Compatible/hooh/StudioItemStandard"
{
    Properties
    {
        _DetailNormalMap2 ("DetailNormalMap2", 2D) = "bump" {}
        _DetailNormalMap ("DetailNormalMap", 2D) = "bump" {}
        _Color1_2 ("Color1_2", Color) = (1,1,1,1)
        _Color1 ("Color1", Color) = (1,1,1,1)
        _Color2 ("Color2", Color) = (1,1,1,1)
        _Color3 ("Color3", Color) = (1,1,1,1)
        _Color3_2 ("Color3_2", Color) = (1,1,1,1)
        _Emission ("Emission", Color) = (1,1,1,1)
        _Color2_2 ("Color2_2", Color) = (1,1,1,1)
        _BaseColor ("BaseColor", Color) = (0.7075,0.7075,0.7075,1)
        _BumpMap ("BumpMap", 2D) = "bump" {}
        _MetalicGlossMap ("MetalicGlossMap", 2D) = "black" {}
        _OcclusionMap ("OcclusionMap", 2D) = "white" {}
        _WeatheringMap ("WeatheringMap", 2D) = "white" {}
        _WeatheringMask ("WeatheringMask", 2D) = "white" {}
        _MainTex ("MainTex", 2D) = "white" {}
        _BumpScale ("BumpScale", Range(0, 3)) = 1
        _Metallic2 ("Metallic2", Range(0, 1)) = 0.2
        _Metallic3 ("Metallic3", Range(0, 1)) = 0.2
        _Metallic4 ("Metallic4", Range(0, 1)) = 0.2
        _Metallic ("Metallic", Range(0, 1)) = 0.2
        _DetailNormalMapScale ("DetailNormalMapScale", Range(0, 3)) = 1
        _DetailNormalMapScale2 ("DetailNormalMapScale2", Range(0, 1)) = 0.2
        _OcclusionStrength ("OcclusionStrength", Range(0, 1)) = 1
        _Glossiness4 ("Glossiness4", Range(0, 1)) = 0.2
        _Glossiness ("Glossiness", Range(0, 1)) = 0.2
        _Glossiness3 ("Glossiness3", Range(0, 1)) = 0.2
        _Glossiness2 ("Glossiness2", Range(0, 1)) = 0.2
        [HideInInspector] _texcoord ("", 2D) = "white" {}
        [HideInInspector] __dirty ("", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex HS2HoohVert
            #pragma fragment HS2HoohFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #define HS2_HOOH_STANDARD
            #include "HS2NextHooh.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            Cull Off
            HLSLPROGRAM
            #pragma vertex HS2HoohShadowVert
            #pragma fragment HS2HoohDepthFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #define HS2_HOOH_STANDARD
            #include "HS2NextHooh.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            Cull Off
            HLSLPROGRAM
            #pragma vertex HS2HoohVert
            #pragma fragment HS2HoohDepthFrag
            #define HS2_HOOH_STANDARD
            #include "HS2NextHooh.hlsl"
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
            #define HS2_HOOH_STANDARD
            #include "HS2NextHooh.hlsl"
            #define HS2_PASS_A HS2HoohAttributes
            #define HS2_PASS_V HS2HoohVaryings
            #define HS2_PASS_VERT HS2HoohVert
            #define HS2_PASS_CLIP HS2HoohDepthFrag
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
            #define HS2_HOOH_STANDARD
            #include "HS2NextHooh.hlsl"
            #define HS2_PASS_A HS2HoohAttributes
            #define HS2_PASS_V HS2HoohVaryings
            #define HS2_PASS_VERT HS2HoohVert
            #define HS2_PASS_CLIP HS2HoohDepthFrag
            #define HS2_PASS_POS(a) (a).pos.xyz
            #define HS2_PASS_NORMAL(a) (a).n
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
    }
}
