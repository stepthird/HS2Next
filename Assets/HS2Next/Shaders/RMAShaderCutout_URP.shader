// URP replacement for the mod shader "RMAShaderCutout" (Phase 3.7 package F3). Same property names, types and
// defaults as the original (an Amplify surface shader of hooh's prop packs);
// cutout at _Cutoff, Cull Back.
// Formulas in HS2NextRma.hlsl (HS2_RMA_CUTOUT). Inferred by black-box probing only (Tests/FitCases/f3_*.txt).
Shader "HS2Next/Compatible/RMAShaderCutout"
{
    Properties
    {
        _Metallic ("Metallic", Range(0, 2)) = 1
        _Glossiness ("Glossiness", Range(0, 2)) = 1
        _EmissionColor ("EmissionColor", Color) = (0,0,0,0)
        _Color ("Color", Color) = (1,1,1,1)
        _EmissionStrength ("EmissionStrength", Float) = 0.1
        _MainTex ("MainTex", 2D) = "white" {}
        _NormalMap ("NormalMap", 2D) = "white" {}
        _RMA ("RMA", 2D) = "white" {}
        _EmissionTex ("EmissionTex", 2D) = "white" {}
        _Cutoff ("Mask Clip Value", Float) = 0.5
        [HideInInspector] _texcoord ("_texcoord", 2D) = "white" {}
        [HideInInspector] __dirty ("__dirty", Float) = 1
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
            #define HS2_RMA_CUTOUT
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
            #define HS2_RMA_CUTOUT
            #include "HS2NextRma.hlsl"
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
            #define HS2_RMA_CUTOUT
            #include "HS2NextRma.hlsl"
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
            #define HS2_RMA_CUTOUT
            #include "HS2NextRma.hlsl"
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
            #define HS2_RMA_CUTOUT
            #include "HS2NextRma.hlsl"
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
