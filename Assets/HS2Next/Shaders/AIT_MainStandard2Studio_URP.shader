// URP replacement for HS2 "AIT/main standard2 studio" (Studio items: furniture, structures, primitives, weapons). Same property names as the
// original so a loaded material keeps its values. Colour-mask regions with a lighten blend, three patterns with their
// own UV transforms, metal / gloss per region, detail albedo / normal, parallax from the occlusion alpha, cutout or 4x4 dither
// (HS2NextStudioStandard.hlsl, see its header for the measured formulas). Inferred by black-box probing only
// (Editor/Fit/SkinFit.cs, Tests/FitCases/ms2_*.txt).
Shader "HS2Next/AIT main standard2 studio"
{
    Properties
    {
        _Cutoff ("Mask Clip Value", Float) = 0.5
        _DitherSwitch ("Dither Switch", Float) = 0
        _MainTex ("Main Tex", 2D) = "white" {}
        _BumpMap ("Bump Map", 2D) = "bump" {}
        _MetallicGlossMap ("Metallic Gloss Map", 2D) = "white" {}
        _OcclusionMap ("Occlusion Map", 2D) = "white" {}
        _EmissonMap ("Emisson Map (RGB)or(R)", 2D) = "white" {}
        _DetailMask ("Detail Mask", 2D) = "white" {}
        _ColorMask ("Color Mask", 2D) = "black" {}
        _Color ("Color", Color) = (0.800,0.800,0.800,1.000)
        _Color1_2 ("Color 1_2", Color) = (0.800,0.800,0.800,1.000)
        _Color2 ("Color 2", Color) = (0.800,0.800,0.800,1.000)
        _Color2_2 ("Color 2_2", Color) = (0.800,0.800,0.800,1.000)
        _Color3 ("Color 3", Color) = (0.800,0.800,0.800,1.000)
        _Color3_2 ("Color 3_2", Color) = (0.800,0.800,0.800,1.000)
        _Color4 ("Color 4", Color) = (0.800,0.800,0.800,1.000)
        _UV ("UV", Vector) = (1.000,1.000,0.000,0.000)
        _alpha ("alpha", Range(0, 1)) = 1
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Metallic2 ("Metallic 2", Range(0, 1)) = 0
        _Metallic3 ("Metallic 3", Range(0, 1)) = 0
        _Metallic4 ("Metallic 4", Range(0, 1)) = 0
        _Glossiness ("Glossiness", Range(0, 1)) = 0.5
        _Glossiness2 ("Glossiness 2", Range(0, 1)) = 0.5
        _Glossiness3 ("Glossiness 3", Range(0, 1)) = 0.5
        _Glossiness4 ("Glossiness 4", Range(0, 1)) = 0.5
        _Float1 ("Bump Scale", Range(0, 9)) = 1
        _Parallax ("Parallax", Range(-0.1, 0.1)) = 0
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 1
        _EmissionStrength ("Emission Strength", Range(0, 9)) = 1
        [HDR] _EmissionColor ("Emission Color (HDR)", Color) = (1.000,1.000,1.000,1.000)
        _UVSec ("UVSec", Vector) = (1.000,1.000,0.000,0.000)
        _DetailNormalMap ("Detail Normal Map", 2D) = "bump" {}
        _DetailAlbedoMap ("Detail Albedo Map", 2D) = "gray" {}
        _DetailAlbedoMapScale ("Detail Albedo Map Scale", Range(0, 1)) = 0
        _DetailAlbedoNormalScale ("Detail Albedo Normal Scale", Range(0, 9)) = 1
        _DetailNormalMapScale ("Detail Normal Map Scale", Range(0, 9)) = 1
        _PatternMask1 ("Pattern Mask 1", 2D) = "white" {}
        _PatternMask3 ("Pattern Mask 3", 2D) = "white" {}
        _PatternMask2 ("Pattern Mask 2", 2D) = "white" {}
        _patternuv1 ("pattern uv 1", Vector) = (1.000,1.000,0.000,0.000)
        _patternuv2 ("pattern uv 2", Vector) = (1.000,1.000,0.000,0.000)
        _patternuv3 ("pattern uv 3", Vector) = (1.000,1.000,0.000,0.000)
        _patternuv1Rotator ("pattern uv 1 Rotator", Range(-1, 1)) = 0
        _patternuv2Rotator ("pattern uv 2 Rotator", Range(-1, 1)) = 0
        _patternuv3Rotator ("pattern uv 3 Rotator", Range(-1, 1)) = 0
        _patternclamp1 ("patternclamp1", Range(0, 1)) = 0
        _patternclamp2 ("patternclamp2", Range(0, 1)) = 0
        _patternclamp3 ("patternclamp3", Range(0, 1)) = 0
        _EmissionMainColorBase ("Emission Main Color Base", Range(0, 1)) = 1
        _texcoord ("texcoord", 2D) = "white" {}
        __dirty ("dirty", Float) = 1
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
            Cull Back
            HLSLPROGRAM
            #pragma vertex HS2MS2Vert
            #pragma fragment HS2MS2Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_local _ _DITHERSWITCH_ON
            #include "HS2NextStudioStandard.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex HS2MS2ShadowVert
            #pragma fragment HS2MS2DepthFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_local _ _DITHERSWITCH_ON
            #include "HS2NextStudioStandard.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            Cull Back
            HLSLPROGRAM
            #pragma vertex HS2MS2Vert
            #pragma fragment HS2MS2DepthFrag
            #pragma multi_compile_local _ _DITHERSWITCH_ON
            #include "HS2NextStudioStandard.hlsl"
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
            #pragma multi_compile_local _ _DITHERSWITCH_ON
            #include "HS2NextStudioStandard.hlsl"
            #define HS2_PASS_A HS2MS2Attributes
            #define HS2_PASS_V HS2MS2Varyings
            #define HS2_PASS_VERT HS2MS2Vert
            #define HS2_PASS_CLIP HS2MS2DepthFrag
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
            #pragma multi_compile_fragment _ _WRITE_SMOOTHNESS
            #define HS2_PASS_WANTS_SMOOTHNESS
            #pragma multi_compile_local _ _DITHERSWITCH_ON
            #include "HS2NextStudioStandard.hlsl"
            #define HS2_PASS_A HS2MS2Attributes
            #define HS2_PASS_V HS2MS2Varyings
            #define HS2_PASS_VERT HS2MS2Vert
            #define HS2_PASS_CLIP HS2MS2DepthFrag
            #define HS2_PASS_POS(a) (a).pos.xyz
            #define HS2_PASS_NORMAL(a) (a).n
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
    }
}
