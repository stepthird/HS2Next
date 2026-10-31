// URP replacement for the older bundle copies of the mod shader "Hanmen/Item Cutoff" (52 / 54 / 58 / 68 properties: Rose
// and Asslie weapon / vehicle packs, a map, a character-parts mod; Phase 3.7, package F1r2). They are the
// "Hanmen/Clothes Cutoff v0.9" model with other colour names (Built-in against Built-in on F2's hcv9 set, renamed:
// hmi_v09eq.txt), so this shader runs F2's v0.9 surface (HS2NextHanmen.hlsl, HS2_HANMEN_V09) on their property names
// (HS2_V09_* indirection). Properties: the union of those copies (ShaderMap.CopyRoutes sends them here); the v0.9
// inputs they lack are compile-time constants (HS2_V09_ALPHAEX, HS2_V09_NO_WETMAP). Cull [_CullMode] (0 = Off for the 52-property copy, which has no _CullMode).
Shader "HS2Next/Compatible/Hanmen/Item Cutoff (v0.9 copies)"
{
    Properties
    {
        [NoScaleOffset] [Header (RGB Color A Cutoff)] _MainTex ("MainTex", 2D) = "white" {}
        [NoScaleOffset] _ColorMask ("ColorMask", 2D) = "black" {}
        [NoScaleOffset] _BumpMap ("BumpMap", 2D) = "bump" {}
        [NoScaleOffset] [Header (R_Detail1 G_Detail2)] _DetailMask ("DetailMask", 2D) = "black" {}
        [NoScaleOffset] [Header (Detail1)] _DetailGlossMap ("DetailGlossMap", 2D) = "gray" {}
        [NoScaleOffset] [Header (Detail2)] _DetailGlossMap2 ("DetailGlossMap2", 2D) = "gray" {}
        [NoScaleOffset] [Header(Packed (R_Gloss G_Emission B_MetallicMask))] _MetallicGlossMap ("MetallicGlossMap", 2D) = "white" {}
        [NoScaleOffset] _OcclusionMap ("OcclusionMap", 2D) = "white" {}
        [NoScaleOffset] [Header (Dithering Noise)] _NoiseTexture ("NoiseTexture", 2D) = "white" {}
        _BaseColor ("BaseColor", Color) = (1, 1, 1, 1)
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Color2 ("Color2", Color) = (1, 1, 1, 1)
        _Color3 ("Color3", Color) = (1, 1, 1, 1)
        _Color4 ("Color4", Color) = (1, 1, 1, 1)
        _EmissionColor ("EmissionColor", Color) = (0, 0, 0, 1)
        _DetailUV ("DetailUV", Vector) = (1, 1, 0, 0)
        _DetailUV2 ("DetailUV2", Vector) = (1, 1, 0, 0)
        _DetailUVRotator ("DetailUVRotator", Range(-1, 1)) = 0
        _DetailUVRotator2 ("DetailUVRotator2", Range(-1, 1)) = 0
        [Header (Bump)] _BumpScale ("BumpScale", Range(0, 3)) = 1
        _DetailNormalMapScale ("DetailNormalMapScale", Range(-3, 9)) = 1
        _DetailNormalMapScale2 ("DetailNormalMapScale2", Range(-3, 9)) = 1
        [Header (Emission (Tex Required))] _EmissionStrength ("EmissionStrength", Range(0, 20)) = 1
        [Header (Color Mask Required (Optional))] [Toggle(_EMISSIONCOLOR1_ON)] _EmissionColor1 ("Color1 is Emissive", Float) = 0
        [Toggle(_EMISSIONCOLOR2_ON)] _EmissionColor2 ("Color2 is Emissive", Float) = 0
        [Toggle(_EMISSIONCOLOR3_ON)] _EmissionColor3 ("Color3 is Emissive", Float) = 0
        [Header (Ambient Occlusion)] _OcclusionStrength ("OcclusionStrength", Range(0, 1)) = 1
        [Toggle(_DETAILOCCLUSION_ON)] _DetailOcclusion ("DetailOcclusion", Float) = 0
        _DetailOcclusionScale ("DetailOcclusionScale", Range(0, 1)) = 0
        _DetailOcclusionScale2 ("DetailOcclusionScale2", Range(0, 1)) = 0
        _DetailOcclusionContrast2 ("DetailOcclusionContrast2", Range(0, 2)) = 0
        _DetailOcclusionContrast ("DetailOcclusionContrast", Range(0, 2)) = 0
        [Header (Glossiness Roughness)] _Glossiness ("Glossiness", Range(0, 1)) = 1
        _Glossiness2 ("Glossiness2", Range(0, 1)) = 1
        _Glossiness3 ("Glossiness3", Range(0, 1)) = 1
        _Glossiness4 ("Glossiness4", Range(0, 1)) = 1
        _Roughness ("Roughness", Range(0, 1)) = 1
        _Roughness2 ("Roughness2", Range(0, 1)) = 1
        _Roughness3 ("Roughness3", Range(0, 1)) = 1
        _Roughness4 ("Roughness4", Range(0, 1)) = 1
        [Header (Metallic Maps)] _Metallic ("Metallic", Range(0, 1)) = 1
        _Metallic2 ("Metallic2", Range(0, 1)) = 1
        _Metallic3 ("Metallic3", Range(0, 1)) = 1
        _Metallic4 ("Metallic4", Range(0, 1)) = 1
        _MetallicMask ("MetallicMask", Range(0, 1)) = 1
        _MetallicMask2 ("MetallicMask2", Range(0, 1)) = 1
        _MetallicMask3 ("MetallicMask3", Range(0, 1)) = 1
        _MetallicMask4 ("MetallicMask4", Range(0, 1)) = 1
        [Header (Dithering Noise)] [Toggle(_BLUENOISEDITHER_ON)] _BlueNoiseDither ("BlueNoiseDither", Float) = 0
        [Header (Zero Disable Dithering)] _Cutoff ("Cutoff", Range(0, 1)) = 0
        [HideInInspector] _texcoord ("", 2D) = "white" {}
        [HideInInspector] __dirty ("", Float) = 1
        [IntRange] _CullMode ("CullMode", Range(0, 2)) = 0
        _WetnessPower ("WetnessPower", Range(0, 2)) = 0.8
        _CarvatureStrength ("CarvatureStrength", Range(0, 1)) = 0
        _ExGloss ("ExGloss", Range(0, 1)) = 0
        _WetAlbedoOffset ("WetAlbedoOffset", Range(0, 1)) = 0.6
        _DetailUV2Rotator ("DetailUV2Rotator", Range(-1, 1)) = 0
        _DetailGlossScale1 ("DetailGlossScale", Range(0, 1)) = 0
        _DetailGlossScale3 ("DetailGlossScale2", Range(0, 1)) = 0
        _DetailMetallicScale ("DetailMetallicScale", Range(0, 1)) = 0
        _DetailMetallicScale2 ("DetailMetallicScale2", Range(0, 1)) = 0
        [Header(Translucency)] _Translucency ("Strength", Range(0, 50)) = 1
        _TransNormalDistortion ("Normal Distortion", Range(0, 1)) = 0.1
        _TransScattering ("Scaterring Falloff", Range(1, 50)) = 2
        _TransDirect ("Direct", Range(0, 1)) = 1
        _TransAmbient ("Ambient", Range(0, 1)) = 0.2
        _TransShadow ("Shadow", Range(0, 1)) = 0.9
        [Enum(True,1,False,0)] _DitherSwitch ("DitherSwitch", Range(0, 1)) = 0
        _Noise ("Noise", 2D) = "white" {}
        _DitherBlueNoise ("DitherBlueNoise", Range(0, 1)) = 0
        // the v0.9 inputs these copies do not declare are not properties here (ModShaderTest M2: nothing outside the copies):
        // HS2_V09_ALPHAEX 1 (no tearing) and HS2_V09_NO_WETMAP (dry) in every pass; the weathering / pattern inputs have no
        // effect in the v0.9 surface (their cbuffer values stay 0)
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
            Cull [_CullMode]
            HLSLPROGRAM
            #pragma vertex HS2HanmenVert
            #pragma fragment HS2HanmenFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #define HS2_HANMEN_V09
            #define HS2_V09_BASE _BaseColor
            #define HS2_V09_C1 _Color
            #define HS2_V09_C2 _Color2
            #define HS2_V09_C3 _Color3
            #define HS2_V09_C4 _Color4
            #define HS2_V09_ALPHAEX 1
            #define HS2_V09_NO_WETMAP
            #include "HS2NextHanmen.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            Cull [_CullMode]
            HLSLPROGRAM
            #pragma vertex HS2HanmenShadowVert
            #pragma fragment HS2HanmenDepthFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #define HS2_HANMEN_V09
            #define HS2_V09_BASE _BaseColor
            #define HS2_V09_C1 _Color
            #define HS2_V09_C2 _Color2
            #define HS2_V09_C3 _Color3
            #define HS2_V09_C4 _Color4
            #define HS2_V09_ALPHAEX 1
            #define HS2_V09_NO_WETMAP
            #include "HS2NextHanmen.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            Cull [_CullMode]
            HLSLPROGRAM
            #pragma vertex HS2HanmenVert
            #pragma fragment HS2HanmenDepthFrag
            #define HS2_HANMEN_V09
            #define HS2_V09_BASE _BaseColor
            #define HS2_V09_C1 _Color
            #define HS2_V09_C2 _Color2
            #define HS2_V09_C3 _Color3
            #define HS2_V09_C4 _Color4
            #define HS2_V09_ALPHAEX 1
            #define HS2_V09_NO_WETMAP
            #include "HS2NextHanmen.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode"="MotionVectors" }
            ColorMask RG
            Cull [_CullMode]
            HLSLPROGRAM
            #pragma vertex HS2MotionVert
            #pragma fragment HS2MotionFrag
            #define HS2_HANMEN_V09
            #define HS2_V09_BASE _BaseColor
            #define HS2_V09_C1 _Color
            #define HS2_V09_C2 _Color2
            #define HS2_V09_C3 _Color3
            #define HS2_V09_C4 _Color4
            #define HS2_V09_ALPHAEX 1
            #define HS2_V09_NO_WETMAP
            #include "HS2NextHanmen.hlsl"
            #define HS2_PASS_A HS2HanmenAttributes
            #define HS2_PASS_V HS2HanmenVaryings
            #define HS2_PASS_VERT HS2HanmenVert
            #define HS2_PASS_CLIP HS2HanmenDepthFrag
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
            Cull [_CullMode]
            HLSLPROGRAM
            #pragma vertex HS2DepthNormalsVert
            #pragma fragment HS2DepthNormalsFrag
            #define HS2_HANMEN_V09
            #define HS2_V09_BASE _BaseColor
            #define HS2_V09_C1 _Color
            #define HS2_V09_C2 _Color2
            #define HS2_V09_C3 _Color3
            #define HS2_V09_C4 _Color4
            #define HS2_V09_ALPHAEX 1
            #define HS2_V09_NO_WETMAP
            #include "HS2NextHanmen.hlsl"
            #define HS2_PASS_A HS2HanmenAttributes
            #define HS2_PASS_V HS2HanmenVaryings
            #define HS2_PASS_VERT HS2HanmenVert
            #define HS2_PASS_CLIP HS2HanmenDepthFrag
            #define HS2_PASS_POS(a) (a).pos.xyz
            #define HS2_PASS_NORMAL(a) (a).n
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
    }
}
