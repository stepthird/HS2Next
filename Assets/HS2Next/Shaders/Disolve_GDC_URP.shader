// URP replacement for the mod shader "Disolve GDC" (the parts of a diving-suit mod; Phase 5.2 package S): the hooh "ClotheReplica" surface with _GlossMetalMap (HS2NextHooh.hlsl HS2_HOOH_GMM, black-box probed:
// albedo MainTex x BaseColor, metal DetailMainTex.R + GlossMetalMap.R, smoothness DetailMainTex.B + GlossMetalMap.A x
// SmoothnessMultiply, occlusion R, three normal maps, back faces with the front normal; the colour and emission properties
// have no visible effect) with the
// original's 37 properties (an Amplify surface shader, queue AlphaTest, Cull Off). Its dissolve (a procedural noise
// against a position gradient along _DissolveDirection between _LowerLimit and _UpperLimit, _Float0 the amount, an edge
// of _Color3 x _Edgethickness) is not drawn: the materials of this install keep the surface whole (Shaders/README.md).
Shader "HS2Next/Compatible/Disolve GDC"
{
    Properties
    {
        _DetailNormalMap2 ("DetailNormalMap2", 2D) = "bump" {}
        _DetailNormalMap ("DetailNormalMap", 2D) = "bump" {}
        _Color1_3 ("Color1_2", Color) = (1,1,1,1)
        _Color4 ("Color1", Color) = (1,1,1,1)
        [HDR] _Color3 ("Color 3", Color) = (0,0,0,0)
        _NoiseDirection ("Noise Direction", Vector) = (0,0,0,0)
        _Color5 ("Color2", Color) = (1,1,1,1)
        _Color6 ("Color3", Color) = (1,1,1,1)
        _BaseColor ("BaseColor", Color) = (0.7075472,0.7075472,0.7075472,1)
        _NoiseScrollSpeed ("NoiseScrollSpeed", Float) = 0
        _NoiseScale ("Noise Scale", Vector) = (0,0,0,0)
        _GlossMetalMap ("GlossMetalMap", 2D) = "black" {}
        _Color3_3 ("Color3_2", Color) = (1,1,1,1)
        _DetailMainTex ("DetailMainTex", 2D) = "gray" {}
        _ToggleSwitch0 ("Toggle Switch0", Float) = 0
        _Emission ("Emission", Color) = (1,1,1,1)
        _DissolveDirection ("Dissolve Direction", Vector) = (0,0,0,0)
        _OcclusionMap ("OcclusionMap", 2D) = "white" {}
        _Color_3 ("Color2_2", Color) = (1,1,1,1)
        _UpperLimit ("Upper Limit", Float) = 0
        _LowerLimit ("Lower Limit", Float) = 0
        _Float0 ("Float 0", Range(0, 1)) = 0
        _MainTex ("MainTex", 2D) = "white" {}
        _BumpMap ("BumpMap", 2D) = "bump" {}
        _DetailMask ("DetailMask", 2D) = "white" {}
        _MetalicGlossMap ("MetalicGlossMap", 2D) = "black" {}
        _Edgethickness ("Edge thickness", Float) = 0
        _BumpScale ("BumpScale", Range(0, 3)) = 1
        _DetailNormalMapScale ("DetailNormalMapScale", Range(0, 3)) = 1
        _AlphaMask ("AlphaMask", 2D) = "white" {}
        _OcclusionStrength ("OcclusionStrength", Range(0, 1)) = 1
        _DetailNormalMapScale2 ("DetailNormalMapScale2", Range(0, 1)) = 0.2
        _AlphaMask2 ("AlphaMask2", 2D) = "white" {}
        _DetailGlossMap2 ("DetailGlossMap2", 2D) = "bump" {}
        _SmoothnessMultiply ("SmoothnessMultiply", Range(0, 1)) = 0
        [HideInInspector] _texcoord ("_texcoord", 2D) = "white" {}
        [HideInInspector] __dirty ("__dirty", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
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
            #define HS2_HOOH_REPLICA
            #define HS2_HOOH_GMM
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
            #define HS2_HOOH_REPLICA
            #define HS2_HOOH_GMM
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
            #define HS2_HOOH_REPLICA
            #define HS2_HOOH_GMM
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
            #define HS2_HOOH_REPLICA
            #define HS2_HOOH_GMM
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
            #define HS2_HOOH_REPLICA
            #define HS2_HOOH_GMM
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
