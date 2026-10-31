// URP replacement for a mod shader of the hooh "RMA" family (Studio items and mod clothes / accessories; Phase 3.7 package
// F2). Same property names, types and defaults as the original (an Amplify surface shader): packed gloss / metal / AO map,
// two detail normal maps with a mask, culling from [_CullMode]. Formulas in HS2NextHoohRma.hlsl. Inferred by black-box
// probing only (Tests/FitCases/hooh_rma_*.txt).
Shader "HS2Next/Compatible/hooh/Clothing/RMA/Opqaue"
{
    Properties
    {
        _BaseColor ("BaseColor (Not Adjustable in Game)", Color) = (0.7075,0.7075,0.7075,1)
        _MainTex ("MainTex", 2D) = "white" {}
        _ExGloss ("ExGloss - Controlled By Game", Range(0, 1)) = 0
        [HideInInspector] _DetailMainTex ("Gloss Render Texture", 2D) = "black" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _DetailNormalMap ("First Detail Normal Map", 2D) = "bump" {}
        _DetailNormalMap2 ("Second Detail Normal Map", 2D) = "bump" {}
        _BumpMapMask ("Normal Map Mask (R:Main,G:Detail1,B:Detail2) - def:white", 2D) = "white" {}
        _MetallicGlossMap ("PackedMap (Gloss,Metal,AO)", 2D) = "gray" {}
        _DetailNormalIntensity ("Detail 1 Intensity ", Range(0, 3)) = 1
        _DetailNormalIntensity2 ("Detail 2 Intensity ", Range(0, 1)) = 0.2
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 1
        _BumpScale ("Normal Map Scale", Range(0, 2)) = 1
        [Enum(Back,0,Front,1,All,2)] _CullMode ("CullMode", Float) = 0
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
            #pragma vertex HS2RmaVert
            #pragma fragment HS2RmaFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #define HS2_RMA_CLOTHING
            #include "HS2NextHoohRma.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex HS2RmaShadowVert
            #pragma fragment HS2RmaDepthFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #define HS2_RMA_CLOTHING
            #include "HS2NextHoohRma.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            Cull Back
            HLSLPROGRAM
            #pragma vertex HS2RmaVert
            #pragma fragment HS2RmaDepthFrag
            #define HS2_RMA_CLOTHING
            #include "HS2NextHoohRma.hlsl"
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
            #define HS2_RMA_CLOTHING
            #include "HS2NextHoohRma.hlsl"
            #define HS2_PASS_A HS2RmaAttributes
            #define HS2_PASS_V HS2RmaVaryings
            #define HS2_PASS_VERT HS2RmaVert
            #define HS2_PASS_CLIP HS2RmaDepthFrag
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
            #define HS2_RMA_CLOTHING
            #include "HS2NextHoohRma.hlsl"
            #define HS2_PASS_A HS2RmaAttributes
            #define HS2_PASS_V HS2RmaVaryings
            #define HS2_PASS_VERT HS2RmaVert
            #define HS2_PASS_CLIP HS2RmaDepthFrag
            #define HS2_PASS_POS(a) (a).pos.xyz
            #define HS2_PASS_NORMAL(a) (a).n
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
    }
}
