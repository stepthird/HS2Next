// URP replacement for the mod shader "hooh/ColorableObjectReplica" (Studio items and mod accessories; Phase 3.7 package F2).
// Same property names, types and defaults as the original's main copy (an Amplify surface shader): colour-mask regions,
// metal / gloss mixed by the mask's R channel, packed metallic / smoothness map, emission texture, three blended normal
// maps; Cull Back. Formulas in HS2NextHoohColorable.hlsl. Inferred by black-box probing only (Tests/FitCases/hooh_cor_*.txt).
Shader "HS2Next/Compatible/hooh/ColorableObjectReplica"
{
    Properties
    {
        _NormalMap ("NormalMap", 2D) = "bump" {}
        _BlendNormalMap ("BlendNormalMap", 2D) = "bump" {}
        _BlendNormalMapStrength ("BlendNormalMapStrength", Range(0, 10)) = 0
        _DetailNormalMap ("DetailNormalMap", 2D) = "bump" {}
        _DetailNormalMapStrength ("DetailNormalMapStrength", Range(0, 10)) = 0
        _NormalMapStrength ("NormalMapStrength", Range(0, 10)) = 1
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Metallic2 ("Metallic 2", Range(0, 1)) = 0
        _Metallic3 ("Metallic 3", Range(0, 1)) = 0
        _Metallic4 ("Metallic 4", Range(0, 1)) = 0
        _Glossiness ("Glossiness 1", Range(0, 1)) = 0
        _Glossiness2 ("Glossiness 2", Range(0, 1)) = 0
        _Glossiness3 ("Glossiness 3", Range(0, 1)) = 0
        _Glossiness4 ("Glossiness 4", Range(0, 1)) = 0
        _Color ("Color 1", Color) = (0,0,0,0)
        _Color2 ("Color 2", Color) = (0,0,0,0)
        _Color3 ("Color 3", Color) = (0,0,0,0)
        _Color4 ("Color 4", Color) = (0,0,0,0)
        _MainTex ("MainTex", 2D) = "white" {}
        _MetallicSmoothness ("MetallicSmoothness", 2D) = "black" {}
        _ColorMask ("ColorMask", 2D) = "black" {}
        _AmbientOcclusion ("AmbientOcclusion", 2D) = "white" {}
        _Emission ("Emission", 2D) = "black" {}
        _NormalizedNormalLerp ("NormalizedNormalLerp", Range(0, 1)) = 0
        _EmissionBoost ("Emission Boost", Range(0, 10)) = 1
        _AmbientOcclusionstrength ("AmbientOcclusionstrength", Range(0, 1)) = 0
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
            Cull Back
            HLSLPROGRAM
            #pragma vertex HS2HoohCorVert
            #pragma fragment HS2HoohCorFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #include "HS2NextHoohColorable.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex HS2HoohCorShadowVert
            #pragma fragment HS2HoohCorDepthFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "HS2NextHoohColorable.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            Cull Back
            HLSLPROGRAM
            #pragma vertex HS2HoohCorVert
            #pragma fragment HS2HoohCorDepthFrag
            #include "HS2NextHoohColorable.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode"="MotionVectors" }
            ColorMask RG
            Cull Back
            HLSLPROGRAM
            #pragma vertex HS2MotionVert
            #pragma fragment HS2MotionFrag
            #include "HS2NextHoohColorable.hlsl"
            #define HS2_PASS_A HS2HoohCorAttributes
            #define HS2_PASS_V HS2HoohCorVaryings
            #define HS2_PASS_VERT HS2HoohCorVert
            #define HS2_PASS_CLIP HS2HoohCorDepthFrag
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
            Cull Back
            HLSLPROGRAM
            #pragma vertex HS2DepthNormalsVert
            #pragma fragment HS2DepthNormalsFrag
            #include "HS2NextHoohColorable.hlsl"
            #define HS2_PASS_A HS2HoohCorAttributes
            #define HS2_PASS_V HS2HoohCorVaryings
            #define HS2_PASS_VERT HS2HoohCorVert
            #define HS2_PASS_CLIP HS2HoohCorDepthFrag
            #define HS2_PASS_POS(a) (a).pos.xyz
            #define HS2_PASS_NORMAL(a) (a).n
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
    }
}
