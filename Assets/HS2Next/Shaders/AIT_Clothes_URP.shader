// URP replacement for HS2 "AIT/Clothes" (despite the name used by accessories such as collars, bracelets and
// necklaces). Same model as AIT/Item (HS2NextClothes.hlsl); the original additionally compiles the
// _DITHERSWITCH_ON keyword (MainTex alpha becomes 4x4 ordered-dither coverage) and _EMISSIONCOLORTEXBASE_ON
// (emission = albedo * strength * MetallicGloss.G). Inferred by black-box probing (Tests/FitCases/item_*.txt,
// clothes_kw.txt).
Shader "HS2Next/AIT Clothes"
{
    Properties
    {
        _Cutoff ("Mask Clip Value", Float) = 0.5
        _BitangentRim ("Bitangent Rim", Range(0, 1)) = 0
        _FresnelSetting ("Fresnel Setting", Vector) = (0.000,1.000,3.000,0.000)
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
        _ColorMask ("Color Mask (Alpha)", 2D) = "black" {}
        _PatternMask1 ("Pattern Mask 1", 2D) = "white" {}
        _PatternMask2 ("Pattern Mask 2", 2D) = "white" {}
        _PatternMask3 ("Pattern Mask 3", 2D) = "white" {}
        _Color ("Color", Color) = (1.000,0.000,0.000,1.000)
        _Color1_2 ("Color 1_2", Color) = (0.500,0.500,0.500,1.000)
        _Color2 ("Color 2", Color) = (0.900,0.900,0.900,1.000)
        _Color2_2 ("Color 2_2", Color) = (0.500,0.500,0.500,1.000)
        _Color3 ("Color 3", Color) = (0.900,0.900,0.900,1.000)
        _Color3_2 ("Color 3_2", Color) = (0.500,0.500,0.500,1.000)
        _Color4 ("Color 4", Color) = (0.900,0.900,0.900,1.000)
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Metallic2 ("Metallic 2", Range(0, 1)) = 0
        _Metallic3 ("Metallic 3", Range(0, 1)) = 1
        _Metallic4 ("Metallic 4", Range(0, 1)) = 0
        _Glossiness ("Glossiness", Range(0, 1)) = 0.5
        _Glossiness2 ("Glossiness 2", Range(0, 1)) = 0.5
        _Glossiness3 ("Glossiness 3", Range(0, 1)) = 0.5
        _Glossiness4 ("Glossiness 4", Range(0, 1)) = 0.5
        _VertexColorMask ("VertexColorMask", Float) = 0
        _Translucency ("Strength", Range(0, 50)) = 1
        _TransNormalDistortion ("Normal Distortion", Range(0, 1)) = 0.1
        _TransScattering ("Scaterring Falloff", Range(1, 50)) = 2
        _TransDirect ("Direct", Range(0, 1)) = 1
        _TransAmbient ("Ambient", Range(0, 1)) = 0.2
        _TransShadow ("Shadow", Range(0, 1)) = 0.9
        _texcoord ("texcoord", 2D) = "white" {}
        __dirty ("dirty", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
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
            #define HS2_CLOTHES_ITEM
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
            #define HS2_CLOTHES_ITEM
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
            #define HS2_CLOTHES_ITEM
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
            #define HS2_CLOTHES_ITEM
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
            #define HS2_CLOTHES_ITEM
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
