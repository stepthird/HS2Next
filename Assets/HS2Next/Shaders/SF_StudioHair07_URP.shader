// URP replacement for HS2 "Shader Forge/studio_hair_07" (hair-type studio items, e.g. studio/00.unity3d
// p_acs_hair_odangostu; queue 2450). Same property names, types and defaults as the original. Behaviour inferred by
// black-box probing only (SkinFit.Sweep -fitItem, Tests/FitCases/studio_*.txt, Spike/out/fit/Shader_Forge_studio_hair_07_*.txt):
// the hair_07 model (SF_Hair07_URP.shader, HS2NextHair.hlsl with HS2_HAIR_STUDIO) with
//  - _Color4 in place of _Specular (ring and rim colour), _Color2 = top colour (mask B), _Color3 = under colour (mask G);
//  - no _ColorMask_B / ring texture / _Ringoff / mesh colour (the ring is always on);
//  - every albedo term x 1.25;
//  - direct diffuse (0.5 Disney sat(N.L) + 0.1) / 1.1: the 0.5 |N.L| back-side term of hair_07 is missing.
// Same 2x2 screen door as hair_07 (_Cutoff unused).
Shader "HS2Next/Shader Forge/studio_hair_07"
{
    Properties
    {
        _Color ("Color", Color) = (0.2,0.2,0.2,1)
        _Color4 ("Color 4", Color) = (0,0,0,1)
        _Color2 ("Color 2", Color) = (0.412,0.412,0.412,1)
        _Color3 ("Color 3", Color) = (1,1,1,1)
        _TopGradation ("Top Gradation", Range(1,5)) = 1
        _UnderGradation ("Under Gradation", Range(1,5)) = 1
        _Metallic ("Metallic", Range(0,1)) = 0
        _Smoothness ("Smoothness", Range(0,1)) = 0
        _DetailScale ("Detail Scale", Range(0,1)) = 0
        _HighLight ("HighLight", Range(0,20)) = 8.9
        _MainTex ("MainTex", 2D) = "white" {}
        _BumpMap ("BumpMap", 2D) = "bump" {}
        _ColorMask ("ColorMask", 2D) = "white" {}
        _DetailMask ("Detail Mask", 2D) = "bump" {}
        _Occlusion ("Occlusion", 2D) = "white" {}
        _Rimpower ("Rimpower", Range(2.7,20)) = 2.7
        _ExGloss ("Ex Gloss", Range(0,1)) = 0
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        Cull Off
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HS2HairVert
            #pragma fragment HS2HairFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #define HS2_HAIR_STUDIO
            #include "HS2NextHair.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HS2HairShadowVert
            #pragma fragment HS2HairShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #define HS2_HAIR_STUDIO
            #include "HS2NextHair.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HS2HairVert
            #pragma fragment HS2HairDepthFrag
            #define HS2_HAIR_STUDIO
            #include "HS2NextHair.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode"="MotionVectors" }
            ZWrite On ColorMask RG
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HS2MotionVert
            #pragma fragment HS2MotionFrag
            #define HS2_HAIR_STUDIO
            #include "HS2NextHair.hlsl"
            #define HS2_PASS_A HS2HairAttributes
            #define HS2_PASS_V HS2HairVaryings
            #define HS2_PASS_VERT HS2HairVert
            #define HS2_PASS_CLIP HS2HairDepthFrag
            #define HS2_PASS_POS(a) (a).positionOS.xyz
            #define HS2_PASS_NORMAL(a) (a).normalOS
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On ColorMask RGBA
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HS2DepthNormalsVert
            #pragma fragment HS2DepthNormalsFrag
            #define HS2_HAIR_STUDIO
            #include "HS2NextHair.hlsl"
            #define HS2_PASS_A HS2HairAttributes
            #define HS2_PASS_V HS2HairVaryings
            #define HS2_PASS_VERT HS2HairVert
            #define HS2_PASS_CLIP HS2HairDepthFrag
            #define HS2_PASS_POS(a) (a).positionOS.xyz
            #define HS2_PASS_NORMAL(a) (a).normalOS
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
    }
}
