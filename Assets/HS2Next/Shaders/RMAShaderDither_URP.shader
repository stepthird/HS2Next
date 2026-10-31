// URP replacement for the mod shader "RMAShaderDither" (Phase 3.7 package F3). Same property names, types and
// defaults as the original (an Amplify surface shader of hooh's prop packs);
// blended SrcAlpha / OneMinusSrcAlpha, ZWrite Off, Transparent queue.
// Formulas in HS2NextRma.hlsl (HS2_RMA_DITHER). Inferred by black-box probing only (Tests/FitCases/f3_*.txt).
Shader "HS2Next/Compatible/RMAShaderDither"
{
    Properties
    {
        _Metallic ("Metallic", Range(0, 2)) = 1
        _Glossiness ("Glossiness", Range(0, 2)) = 1
        _EmissionColor ("EmissionColor", Color) = (0,0,0,0)
        _Color ("Color", Color) = (1,1,1,1)
        _EmissionStrength ("EmissionStrength", Float) = 0.1
        _alpha ("alpha", Range(0, 1)) = 0
        _MainTex ("MainTex", 2D) = "white" {}
        _NormalMap ("NormalMap", 2D) = "bump" {}
        _RMA ("RMA", 2D) = "white" {}
        _EmissionTex ("EmissionTex", 2D) = "black" {}
        [HideInInspector] _texcoord ("_texcoord", 2D) = "white" {}
        [HideInInspector] __dirty ("__dirty", Float) = 1
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
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
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
            #define HS2_RMA_DITHER
            #include "HS2NextRma.hlsl"
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
            #define HS2_RMA_DITHER
            #include "HS2NextRma.hlsl"
            ENDHLSL
        }
    }
}
