// URP approximation of the mod shader "Mochie/Standard" (Phase 3.7 package F3, W8). Same property names, types,
// defaults and flags as the original (84 properties); the main maps evaluated in Unity Standard's metallic model
// (HS2NextStdLike.hlsl, HS2_SL_MOCHIE); the rest of the original's features is not reproduced (see the HLSL header).
Shader "HS2Next/Compatible/Mochie/Standard"
{
    Properties
    {
        _BlendMode ("Blending Mode", Float) = 0
        _Workflow ("Workflow", Float) = 0
        _SamplingMode ("Sampling Mode", Float) = 0
        _TriplanarFalloff ("Triplanar Falloff", Float) = 1
        _EdgeFadeMin ("Edge Fade Min", Float) = 0.25
        _EdgeFadeMax ("Edge Fade Max", Float) = 0.5
        _Color ("Color", Color) = (1,1,1,1)
        _Saturation ("Saturation", Float) = 1
        _MainTex ("Albedo", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _PackedMap ("Packed Texture", 2D) = "white" {}
        _RoughnessMult ("Roughness Multiplier", Float) = 0
        _MetallicMult ("Metallic Multiplier", Float) = 0
        _OcclusionMult ("Occlusion Multiplier", Float) = 0
        _HeightMult ("Height Multiplier", Float) = 0
        _RoughnessChannel ("Roughness Channel", Float) = 1
        _MetallicChannel ("Metallic Channel", Float) = 2
        _OcclusionChannel ("Occlusion Channel", Float) = 0
        _HeightChannel ("Height Channel", Float) = 3
        _Glossiness ("Roughness", Range(0, 1)) = 0.5
        _SpecGlossMap ("Roughness Map", 2D) = "white" {}
        _Metallic ("Metallic", Range(0, 1)) = 0
        _MetallicGlossMap ("Metallic", 2D) = "white" {}
        _BumpScale ("Scale", Float) = 1
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _Parallax ("Height Scale", Range(0, 0.2)) = 0.02
        _ParallaxMap ("Height Map", 2D) = "black" {}
        _ParallaxMask ("Height Mask", 2D) = "white" {}
        _ParallaxSteps ("Parallax Steps", Range(1, 50)) = 25
        _ParallaxOffset ("Parallax Offset", Range(-1, 1)) = 0
        _OcclusionStrength ("Strength", Range(0, 1)) = 1
        _OcclusionMap ("Occlusion", 2D) = "white" {}
        [HDR] _EmissionColor ("Color", Color) = (0,0,0,1)
        _EmissionMap ("Emission", 2D) = "white" {}
        _EmissionMask ("Mask", 2D) = "white" {}
        _EmissionIntensity ("Intensity", Float) = 1
        _UV0Rotate ("UV0 Rotation", Float) = 0
        _UV0Scroll ("UV0 Scrolling", Vector) = (0,0,0,0)
        _UV1Rotate ("UV1 Rotation", Float) = 0
        _UV1Scroll ("UV1 Scrolling", Vector) = (0,0,0,0)
        _UV2Scroll ("Mask Scrolling", Vector) = (0,0,0,0)
        _UV3Scroll ("Mask Scrolling", Vector) = (0,0,0,0)
        _DetailMask ("Detail Mask", 2D) = "white" {}
        _DetailAlbedoMap ("Detail Base Color", 2D) = "gray" {}
        _DetailAlbedoBlend ("Detail Base Color Blend", Float) = 2
        _DetailNormalMap ("Detail Normal Map", 2D) = "bump" {}
        _DetailNormalMapScale ("Scale", Float) = 1
        _DetailRoughnessMap ("Detail Roughness Map", 2D) = "gray" {}
        _DetailRoughBlend ("Detail Roughness Blend", Float) = 2
        _DetailAOMap ("Detail AO Map", 2D) = "white" {}
        _DetailAOBlend ("Detail AO Blend", Float) = 2
        _UVSec ("UV Set for secondary textures", Float) = 0
        _ReflCube ("Reflection Fallback", Cube) = "" {}
        _ReflCubeOverride ("Reflection Override", Cube) = "" {}
        _CubeThreshold ("Threshold", Range(0.0001, 1)) = 0.45
        _EdgeFade ("SSR Edge Fade", Range(0, 1)) = 0.1
        _Subsurface ("Subsurface Scattering", Float) = 0
        _ScatterAlbedoTint ("Scatter Albedo Tint", Float) = 0
        _ThicknessMap ("Thickness Map", 2D) = "black" {}
        _ThicknessMapPower ("Thickness Map Power", Float) = 1
        _ScatterCol ("Subsurface Color", Color) = (1,1,1,1)
        _ScatterIntensity ("Intensity", Range(0, 10)) = 1
        _ScatterPow ("Power", Range(0.01, 10)) = 1
        _ScatterDist ("Distance", Range(0, 10)) = 1
        _ScatterAmbient ("Ambient Intensity", Range(0, 0.5)) = 0
        _ScatterShadow ("Shadow Power", Range(0, 1)) = 1
        _WrappingFactor ("Wrapping Factor", Range(0.001, 1)) = 0.01
        _Cull ("_Cull", Float) = 2
        _MetaCull ("_MetaCull", Float) = 0
        _CullingMode ("_CullingMode", Float) = 2
        _SpecularHighlights ("Specular Highlights", Float) = 1
        _GlossyReflections ("Glossy Reflections", Float) = 1
        _SSR ("Screenspace Reflections", Float) = 0
        _UseHeight ("Use Heightmap", Float) = 0
        _GSAA ("GSAA", Float) = 0
        _SSRStrength ("SSR Strength", Float) = 1
        _ReflectionStrength ("Relfection Strength", Float) = 1
        _SpecularStrength ("Specular Strength", Float) = 1
        _QueueOffset ("Queue Offset", Float) = 0
        [HideInInspector] _SrcBlend ("__src", Float) = 1
        [HideInInspector] _DstBlend ("__dst", Float) = 0
        [HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0
        [HideInInspector] _ZWrite ("__zw", Float) = 1
        [HideInInspector] _ZTest ("__zt", Float) = 4
        [HideInInspector] _NoiseTexSSR ("SSR Noise Texture", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "PerformanceChecks"="False" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _Color, _MainTex_ST, _PackedMap_ST, _SpecGlossMap_ST, _MetallicGlossMap_ST, _BumpMap_ST;
            float4 _ParallaxMap_ST, _ParallaxMask_ST, _OcclusionMap_ST, _EmissionColor, _EmissionMap_ST, _EmissionMask_ST;
            float4 _UV0Scroll, _UV1Scroll, _UV2Scroll, _UV3Scroll, _DetailMask_ST, _DetailAlbedoMap_ST;
            float4 _DetailNormalMap_ST, _DetailRoughnessMap_ST, _DetailAOMap_ST, _ThicknessMap_ST, _ScatterCol, _NoiseTexSSR_ST;
            float _BlendMode, _Workflow, _SamplingMode, _TriplanarFalloff, _EdgeFadeMin, _EdgeFadeMax, _Saturation, _Cutoff;
            float _RoughnessMult, _MetallicMult, _OcclusionMult, _HeightMult, _RoughnessChannel, _MetallicChannel, _OcclusionChannel, _HeightChannel;
            float _Glossiness, _Metallic, _BumpScale, _Parallax, _ParallaxSteps, _ParallaxOffset, _OcclusionStrength, _EmissionIntensity;
            float _UV0Rotate, _UV1Rotate, _DetailAlbedoBlend, _DetailNormalMapScale, _DetailRoughBlend, _DetailAOBlend, _UVSec, _CubeThreshold;
            float _EdgeFade, _Subsurface, _ScatterAlbedoTint, _ThicknessMapPower, _ScatterIntensity, _ScatterPow, _ScatterDist, _ScatterAmbient;
            float _ScatterShadow, _WrappingFactor, _Cull, _MetaCull, _CullingMode, _SpecularHighlights, _GlossyReflections, _SSR;
            float _UseHeight, _GSAA, _SSRStrength, _ReflectionStrength, _SpecularStrength, _QueueOffset, _SrcBlend, _DstBlend;
            float _ZWrite, _ZTest;
            float _HS2FogModes;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex HS2SLVert
            #pragma fragment HS2SLFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma dynamic_branch_local _METALLICGLOSSMAP
            #pragma dynamic_branch_local _SPECGLOSSMAP
            #pragma dynamic_branch_local _NORMALMAP
            #pragma dynamic_branch_local _RECEIVE_SHADOWS_OFF
            #define HS2_SL_FORWARD
            #define HS2_SL_MOCHIE
            #include "HS2NextStdLike.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex HS2SLShadowVert
            #pragma fragment HS2SLDepthFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #define HS2_SL_MOCHIE
            #include "HS2NextStdLike.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex HS2SLVert
            #pragma fragment HS2SLDepthFrag
            #define HS2_SL_MOCHIE
            #include "HS2NextStdLike.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode"="MotionVectors" }
            ColorMask RG
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex HS2MotionVert
            #pragma fragment HS2MotionFrag
            #define HS2_SL_MOCHIE
            #include "HS2NextStdLike.hlsl"
            #define HS2_PASS_A HS2SLAttributes
            #define HS2_PASS_V HS2SLVaryings
            #define HS2_PASS_VERT HS2SLVert
            #define HS2_PASS_CLIP HS2SLDepthFrag
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
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex HS2DepthNormalsVert
            #pragma fragment HS2DepthNormalsFrag
            #define HS2_SL_MOCHIE
            #include "HS2NextStdLike.hlsl"
            #define HS2_PASS_A HS2SLAttributes
            #define HS2_PASS_V HS2SLVaryings
            #define HS2_PASS_VERT HS2SLVert
            #define HS2_PASS_CLIP HS2SLDepthFrag
            #define HS2_PASS_POS(a) (a).pos.xyz
            #define HS2_PASS_NORMAL(a) (a).n
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
    }
}
