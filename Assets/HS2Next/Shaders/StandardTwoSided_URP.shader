// URP stand-in for "Standard(Two Sided)", a two-sided copy of Unity's Built-in Standard shipped in mod bundles (same
// property list, passes and StandardShaderGUI as Standard; Cull Off in the first subshader). Unity's published Standard
// (metallic) semantics with Cull Off in every pass (HS2NextStandard.hlsl); back faces keep the front normal.
// Also used for the other two-sided Standard copies (ShaderMap: "StandardDoublesided", "Standard Double Sided").
Shader "HS2Next/Standard(Two Sided)"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _Glossiness ("Smoothness", Range(0, 1)) = 0.5
        _GlossMapScale ("Smoothness Scale", Range(0, 1)) = 1
        _SmoothnessTextureChannel ("Smoothness texture channel", Float) = 0
        [Gamma] _Metallic ("Metallic", Range(0, 1)) = 0
        _MetallicGlossMap ("Metallic", 2D) = "white" {}
        _SpecularHighlights ("Specular Highlights", Float) = 1
        _GlossyReflections ("Glossy Reflections", Float) = 1
        _BumpScale ("Scale", Float) = 1
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _Parallax ("Height Scale", Range(0.005, 0.08)) = 0.02
        _ParallaxMap ("Height Map", 2D) = "black" {}
        _OcclusionStrength ("Strength", Range(0, 1)) = 1
        _OcclusionMap ("Occlusion", 2D) = "white" {}
        _EmissionColor ("Color", Color) = (0,0,0,1)
        _EmissionMap ("Emission", 2D) = "white" {}
        _DetailMask ("Detail Mask", 2D) = "white" {}
        _DetailAlbedoMap ("Detail Albedo x2", 2D) = "grey" {}
        _DetailNormalMapScale ("Scale", Float) = 1
        _DetailNormalMap ("Normal Map", 2D) = "bump" {}
        _UVSec ("UV Set for secondary textures", Float) = 0
        [HideInInspector] _Mode ("__mode", Float) = 0
        [HideInInspector] _SrcBlend ("__src", Float) = 1
        [HideInInspector] _DstBlend ("__dst", Float) = 0
        [HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0
        [HideInInspector] _ZWrite ("__zw", Float) = 1
        [HideInInspector] _SpecColor ("Unused (shared buffer)", Color) = (0.2,0.2,0.2,1)
        [HideInInspector] _SpecGlossMap ("Unused (shared buffer)", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "PerformanceChecks"="False" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            Cull Off
            ZWrite [_ZWrite]
            HLSLPROGRAM
            #pragma vertex StdVert
            #pragma fragment StdForwardFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma dynamic_branch_local _NORMALMAP
            #pragma dynamic_branch_local _EMISSION
            #pragma dynamic_branch_local _METALLICGLOSSMAP
            #pragma dynamic_branch_local _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #pragma dynamic_branch_local _DETAIL_MULX2
            #pragma dynamic_branch_local _PARALLAXMAP
            #pragma dynamic_branch_local _SPECULARHIGHLIGHTS_OFF
            #pragma dynamic_branch_local _GLOSSYREFLECTIONS_OFF
            #pragma dynamic_branch_local _RECEIVE_SHADOWS_OFF
            #pragma dynamic_branch_local _ALPHATEST_ON
            #pragma dynamic_branch_local _ALPHABLEND_ON
            #pragma dynamic_branch_local _ALPHAPREMULTIPLY_ON
            #define HS2_STD_FORWARD
            #include "HS2NextStandard.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma vertex StdShadowVert
            #pragma fragment StdShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma dynamic_branch_local _ALPHATEST_ON
            #pragma dynamic_branch_local _ALPHABLEND_ON
            #pragma dynamic_branch_local _ALPHAPREMULTIPLY_ON
            #pragma dynamic_branch_local _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #define HS2_STD_SHADOW
            #include "HS2NextStandard.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            Cull Off
            HLSLPROGRAM
            #pragma vertex StdVert
            #pragma fragment StdDepthFrag
            #pragma dynamic_branch_local _ALPHATEST_ON
            #pragma dynamic_branch_local _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #define HS2_STD_DEPTH
            #include "HS2NextStandard.hlsl"
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
            #pragma dynamic_branch_local _ALPHATEST_ON
            #pragma dynamic_branch_local _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #define HS2_STD_DEPTH
            #include "HS2NextStandard.hlsl"
            #define HS2_PASS_A StdAttributes
            #define HS2_PASS_V StdVaryings
            #define HS2_PASS_VERT StdVert
            #define HS2_PASS_CLIP StdDepthFrag
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
            #pragma multi_compile_fragment _ _WRITE_SMOOTHNESS
            #pragma dynamic_branch_local _METALLICGLOSSMAP
            #pragma dynamic_branch_local _SPECGLOSSMAP
            #define HS2_PASS_WANTS_SMOOTHNESS
            #pragma dynamic_branch_local _ALPHATEST_ON
            #pragma dynamic_branch_local _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
            #define HS2_STD_DEPTH
            #include "HS2NextStandard.hlsl"
            #define HS2_PASS_A StdAttributes
            #define HS2_PASS_V StdVaryings
            #define HS2_PASS_VERT StdVert
            #define HS2_PASS_CLIP StdDepthFrag
            #define HS2_PASS_POS(a) (a).pos.xyz
            #define HS2_PASS_NORMAL(a) (a).n
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
    }
}
