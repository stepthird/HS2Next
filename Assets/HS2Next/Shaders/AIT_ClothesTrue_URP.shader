// URP replacement for HS2 "AIT/Clothes True" (most clothes). Same property names as the original so a loaded
// material can switch shaders and keep its values. Colour and pattern are baked into _MainTex by the clothes
// compositing (Create/Clothes); metallic (R) and smoothness (B) come from the composited _DetailMainTex (gloss RT,
// with its tiling / offset), times MetallicGloss.R / .A and the detail factors; wet smoothness max(gloss, 0.6 ExGloss).
// Everything else (detail maps, occlusion, carvature, emission, translucency, transmission, tearing, wet coverage)
// is the shared model of HS2NextClothes.hlsl, plus the inner-clothes masks (_AlphaMask / _AlphaMask2 on uv1 with
// _alpha_a / _alpha_b / _alpha_d) and 0.4 x translucency on back faces. Cull Off, back faces keep the front normal.
// Not reproduced: the liquid overlay (_Weathering*, invisible at the Studio default of 0).
// Inferred by black-box probing only (Editor/Fit/SkinFit.cs, Tests/FitCases/ct_*.txt).
Shader "HS2Next/AIT Clothes True"
{
    Properties
    {
        _Cutoff ("Mask Clip Value", Float) = 0.5
        _BitangentRim ("Bitangent Rim", Range(0, 1)) = 0
        _FresnelSetting ("Fresnel Setting", Vector) = (0.000,1.000,3.000,0.000)
        _alpha_a ("alpha_a ()", Range(0, 1)) = 0
        _alpha_d ("alpha_d ()", Range(0, 1)) = 0
        _alpha_b ("alpha_b ()", Range(0, 1)) = 0
        _alpha_c ("alpha_c ()", Range(0, 1)) = 0
        _AlphaMask ("AlphaMask", 2D) = "white" {}
        _AlphaMask2 ("AlphaMask 2", 2D) = "white" {}
        _UVScalePattern ("UV Scale Pattern", Vector) = (1.000,1.000,0.000,0.000)
        _DitherSwitch ("Dither Switch", Float) = 0
        _YabureDitherSwitch ("Yabure Dither Switch", Float) = 0
        _VertexColor ("VertexColor", Float) = 0
        _BaseColor ("Base Color", Color) = (0.900,0.900,0.900,0.000)
        _MainTex ("Main Tex", 2D) = "white" {}
        _EmissionColor ("EmissionColor", Color) = (1.000,1.000,1.000,1.000)
        _EmissionColorTexBase ("Emission Color Tex Base", Float) = 0
        _BumpMap ("Bump Map", 2D) = "bump" {}
        _Float0 ("Normal Scale", Range(0, 9)) = 1
        _MetallicGlossMap ("Metallic(R)Emi(G)Trans(B)Gloss(A)", 2D) = "white" {}
        _OcclusionMap ("Occlusion(G)Carva(R)Alpha(B)WetAll(A)", 2D) = "white" {}
        _DetailMask ("Detail Mask", 2D) = "white" {}
        _DetailGlossMap ("Detail Map R(R:Metal G:Bump B:Gloss)", 2D) = "white" {}
        _DetailGlossMap2 ("Detail Map G(R:Metal G:Bump B:Gloss)", 2D) = "white" {}
        _ExGloss ("Ex Gloss", Range(0, 1)) = 0
        _OcculusionStrength ("Occulusion Strength", Range(0, 1)) = 1
        _EmissionStrength ("EmissionStrength", Range(0, 9)) = 0
        _AlphaEx ("AlphaEx", Range(0, 1)) = 1
        _CarvatureStrength ("Carvature Strength", Range(0, 1)) = 0
        _DetailNormalMapScale ("DetailNormalMapScale", Range(0, 9)) = 3
        _DetailNormalMapScale2 ("DetailNormalMapScale2", Range(0, 9)) = 3
        _DetailMetallicScale ("Detail Metallic Scale", Range(0, 1)) = 0.419
        _DetailMetallicScale2 ("Detail Metallic Scale2", Range(0, 1)) = 1
        _DetailGlossScale ("Detail Gloss Scale", Range(0, 1)) = 1
        _DetailGlossScale2 ("Detail Gloss Scale2", Range(0, 1)) = 1
        _DetailOcculusionScale ("Detail Occulusion Scale", Range(0, 1)) = 0.5
        _DetailOcculusionScale2 ("Detail Occulusion Scale2", Range(0, 1)) = 0.5
        _DetailUV ("Detail UV", Vector) = (1.000,1.000,0.000,0.000)
        _DetailUV2 ("Detail UV2", Vector) = (1.000,1.000,0.000,0.000)
        _DetailUVRotator ("Detail UV Rotator", Range(-1, 1)) = 0
        _DetailUV2Rotator ("Detail UV2 Rotator", Range(-1, 1)) = 0
        _TransmissionStrength ("TransmissionStrength", Range(0, 5)) = 0
        _DetailMainTex ("DetailMainTex", 2D) = "black" {}
        _WeatheringMap ("WeatheringMap", 2D) = "black" {}
        _WeatheringUV ("WeatheringUV", Vector) = (3.000,3.000,0.000,0.000)
        _WeatheringAll ("WeatheringAll", Range(0, 1)) = 0
        _WeatheringRange1 ("WeatheringRange1", Range(0, 1)) = 0
        _WeatheringRange2 ("WeatheringRange2", Range(0, 1)) = 0
        _WeatheringRange3 ("WeatheringRange3", Range(0, 1)) = 0
        _WeatheringRange4 ("WeatheringRange4", Range(0, 1)) = 0
        _WeatheringRange5 ("WeatheringRange5", Range(0, 1)) = 0
        _WeatheringMask ("WeatheringMask", 2D) = "black" {}
        _WeatheringRange6 ("WeatheringRange6", Range(0, 1)) = 0
        _Translucency ("Strength", Range(0, 50)) = 1
        _TransNormalDistortion ("Normal Distortion", Range(0, 1)) = 0.1
        _TransScattering ("Scaterring Falloff", Range(1, 50)) = 2
        _TransDirect ("Direct", Range(0, 1)) = 1
        _TransAmbient ("Ambient", Range(0, 1)) = 0.2
        _TransShadow ("Shadow", Range(0, 1)) = 0.9
        _texcoord2 ("texcoord2", 2D) = "white" {}
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
            Cull Off
            HLSLPROGRAM
            #pragma vertex HS2ClothesVert
            #pragma fragment HS2ClothesFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_local _ _DITHERSWITCH_ON
            #pragma multi_compile_local _ _EMISSIONCOLORTEXBASE_ON
            #define HS2_CLOTHES_TRUE
            #include "HS2NextClothes.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            Cull Off
            HLSLPROGRAM
            #pragma vertex HS2ClothesShadowVert
            #pragma fragment HS2ClothesDepthFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_local _ _DITHERSWITCH_ON
            #pragma multi_compile_local _ _EMISSIONCOLORTEXBASE_ON
            #define HS2_CLOTHES_TRUE
            #include "HS2NextClothes.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            Cull Off
            HLSLPROGRAM
            #pragma vertex HS2ClothesVert
            #pragma fragment HS2ClothesDepthFrag
            #pragma multi_compile_local _ _DITHERSWITCH_ON
            #pragma multi_compile_local _ _EMISSIONCOLORTEXBASE_ON
            #define HS2_CLOTHES_TRUE
            #include "HS2NextClothes.hlsl"
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
            #pragma multi_compile_local _ _DITHERSWITCH_ON
            #pragma multi_compile_local _ _EMISSIONCOLORTEXBASE_ON
            #define HS2_CLOTHES_TRUE
            #include "HS2NextClothes.hlsl"
            #define HS2_PASS_A HS2ClothesAttributes
            #define HS2_PASS_V HS2ClothesVaryings
            #define HS2_PASS_VERT HS2ClothesVert
            #define HS2_PASS_CLIP HS2ClothesDepthFrag
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
            #pragma multi_compile_local _ _DITHERSWITCH_ON
            #pragma multi_compile_local _ _EMISSIONCOLORTEXBASE_ON
            #define HS2_CLOTHES_TRUE
            #include "HS2NextClothes.hlsl"
            #define HS2_PASS_A HS2ClothesAttributes
            #define HS2_PASS_V HS2ClothesVaryings
            #define HS2_PASS_VERT HS2ClothesVert
            #define HS2_PASS_CLIP HS2ClothesDepthFrag
            #define HS2_PASS_POS(a) (a).pos.xyz
            #define HS2_PASS_NORMAL(a) (a).n
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
    }
}
