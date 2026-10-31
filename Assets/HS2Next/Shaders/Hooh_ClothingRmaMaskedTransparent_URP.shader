// URP replacement for a mod shader of the hooh "RMA" family (Studio items and mod clothes / accessories; Phase 3.7 package
// F2). Same property names, types and defaults as the original (an Amplify surface shader): packed gloss / metal / AO map,
// two detail normal maps with a mask, culling from [_CullMode]. Formulas in HS2NextHoohRma.hlsl. Inferred by black-box
// probing only (Tests/FitCases/hooh_rma_*.txt).
Shader "HS2Next/Compatible/hooh/Clothing/RMA/MaskedTransparent"
{
    Properties
    {
        _Cutoff ("Mask Clip Value", Float) = 0.5
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
            ZWrite On
            Cull [_CullMode]
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
            #define HS2_RMA_BLEND
            #define HS2_RMA_MASKED
            #include "HS2NextHoohRma.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            Cull [_CullMode]
            HLSLPROGRAM
            #pragma vertex HS2RmaShadowVert
            #pragma fragment HS2RmaDepthFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #define HS2_RMA_CLOTHING
            #define HS2_RMA_BLEND
            #define HS2_RMA_MASKED
            #include "HS2NextHoohRma.hlsl"
            ENDHLSL
        }
    }
}
