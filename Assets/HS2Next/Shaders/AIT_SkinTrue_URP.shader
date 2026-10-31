// URP replacement for HS2 "AIT/Skin True" (body skin, o_body_cf slot 0). Same property names,
// types and defaults as the original so a loaded material keeps its values. Behaviour inferred by
// black-box probing only (Assets/HS2Next/Editor/Fit/SkinFit.cs; data in Spike/out/fit/); the surface code
// is shared with the face in HS2NextSkin.hlsl, lighting is HS2NextBRDF.hlsl (Built-in BRDF1).
//
// Same model as "AIT/Skin True Face" (see that header) for colour, tuning, gloss, occlusion,
// normals and translucency, plus (game keyword set: _EMISSION _NODE_686_ON _NORMALMAP
// _TUNINGCOLOR21OR3_ON _TUNINGSPEC21OR3_ON):
//  _NailMask (cf_body_00_mask)     R: nails, tint by _Color13 (weight R * _Color13.a) and smoothness += min(_NailGloss, R);
//                                  B: hip redness (_Color4, _Texture4Scale); G/A as on the face.
//  _Texture2 (nipple)              on UV1 with a radial warp about (0.5,0.5), saturated (Phase 4.2 UB1):
//                                  uv' = saturate(0.5 + d * (1 + (16 - 16.8 * _NipScale) * min(|d|, 0.25)));
//                                  colour _Color2 * Texture2.R * 0.7, weight Texture2.B * _Color2.a * (1 - vertex colour G),
//                                  smoothness max(s, _NipGloss * w).
//  _Texture3 (underhair)           as the face eyebrow (UV1, _Texture3UV, _Texture3Rotator, vertex colour B).
//  _AlphaMask / _AlphaMask2        clip(min(lerp(1,A.r,_alpha_a), lerp(1,A.g,_alpha_b)) max _alpha_c, min lerp(1,A2.r,_alpha_d)) - _Cutoff);
//                                  _MainTex alpha is not used.
//  _FresnelSetting                 direct-light albedo lift where NailMask.G is set, as on the face.
//  No measurable effect: DetailGlossMap.R/B, _Riality, _DetailMainScale, _DetailNormalMapScale.
// Properties the game writes at load: _MainTex/_DetailMainTex (composited RTs), _OcclusionMap,
// _BumpMap2, _BumpScale2, _Gloss, _Metallic, _Texture2/_Color2/_NipGloss/_NipScale, _Texture3/_Color3,
// _Color13/_NailGloss, _AlphaMask/_AlphaMask2/_alpha_a.._alpha_d (clothes), _Texture4Scale, _ExGloss.
// Fit: see Assets/HS2Next/Shaders/README.md.
Shader "HS2Next/AIT Skin True"
{
    Properties
    {
        _FresnelSetting ("Fresnel Setting", Vector) = (0,1.985,50,1)
        _Cutoff ("Mask Clip Value", Float) = 0.5
        [Toggle] _alpha_a ("alpha_a", Range(0,1)) = 0
        [Toggle] _alpha_d ("alpha_d", Range(0,1)) = 0
        [Toggle] _alpha_b ("alpha_b", Range(0,1)) = 0
        [Toggle] _alpha_c ("alpha_c", Range(0,1)) = 0
        _AlphaMask ("AlphaMask", 2D) = "white" {}
        _AlphaMask2 ("AlphaMask 2", 2D) = "white" {}
        _Color2 ("Color 2 nip", Color) = (1,0.934,0.934,1)
        _Color3 ("Color 3 mnpk", Color) = (0.434,0.434,0.434,0)
        _Color4 ("Color 4", Color) = (0.899,0.454,0.454,1)
        _Color13 ("Color 13", Color) = (0.797,0.702,0.635,1)
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Main Tex", 2D) = "white" {}
        _DetailMainTex ("DetailMainTex", 2D) = "black" {}
        _Texture2 ("Texture2(UV2nip)", 2D) = "black" {}
        _Texture3 ("Texture3(UV3mnpk)", 2D) = "black" {}
        _OcclusionMap ("Occlusion Map", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpMap2 ("BumpMap2", 2D) = "bump" {}
        _NailMask ("Nail Mask", 2D) = "black" {}
        _DetailGlossMap ("Detail Gloss Map (R:Col)(G:Occ)(B:Rim)(A:Gloss)", 2D) = "white" {}
        _Metallic ("Metallic", Range(0,1)) = 0.03
        _Gloss ("Gloss", Range(0,1)) = 0.55
        _OcclusionStrength ("OcclusionStrength", Range(0,1)) = 1
        _DetailMainScale ("DetailMainScale", Range(0,1)) = 1
        _BumpScale ("BumpScale", Range(0,1)) = 1
        _BumpScale2 ("BumpScale2", Range(0,1)) = 0
        _Riality ("Riality", Range(0,1)) = 1
        _NipGloss ("Nip Gloss", Range(0,1)) = 0
        _NailGloss ("NailGloss", Range(0,1)) = 0
        _ExGloss ("Ex Gloss", Range(0,1)) = 0
        _DetailNormalMapScale ("Detail Normal Map Scale", Range(0,1)) = 1
        _NipScale ("Nip Scale", Range(0,1)) = 0.5
        _Texture3Rotator ("Texture3 Rotator", Range(-1,1)) = 0
        _Texture3UV ("Texture3 UV (tx,ty,sx,sy)", Vector) = (0,0.08,1,1)
        _Texture4Scale ("Texture4 Scale", Range(0,1)) = 0
        [Header(Translucency)] _Translucency ("Strength", Range(0,50)) = 1
        _TransNormalDistortion ("Normal Distortion", Range(0,1)) = 0.1
        _TransScattering ("Scaterring Falloff", Range(1,50)) = 2
        _TransDirect ("Direct", Range(0,1)) = 1
        _TransAmbient ("Ambient", Range(0,1)) = 0.2
        _TransShadow ("Shadow", Range(0,1)) = 0.9
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest-40" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex HS2SkinVert
            #pragma fragment HS2SkinFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #define HS2_SKIN_BODY
            #define HS2_SKIN_FORWARD
            #include "HS2NextSkin.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex HS2SkinShadowVert
            #pragma fragment HS2SkinClipFrag
            #define HS2_SKIN_BODY
            #define HS2_SKIN_SHADOW
            #include "HS2NextSkin.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex HS2SkinDepthVert
            #pragma fragment HS2SkinClipFrag
            #define HS2_SKIN_BODY
            #define HS2_SKIN_SHADOW
            #include "HS2NextSkin.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode"="MotionVectors" }
            ColorMask RG
            HLSLPROGRAM
            #pragma vertex HS2MotionVert
            #pragma fragment HS2MotionFrag
            #define HS2_SKIN_BODY
            #define HS2_SKIN_SHADOW
            #include "HS2NextSkin.hlsl"
            #define HS2_PASS_A HS2SkinSA
            #define HS2_PASS_V HS2SkinSV
            #define HS2_PASS_VERT HS2SkinDepthVert
            #define HS2_PASS_CLIP HS2SkinClipFrag
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
            HLSLPROGRAM
            #pragma vertex HS2DepthNormalsVert
            #pragma fragment HS2DepthNormalsFrag
            #define HS2_SKIN_BODY
            #define HS2_SKIN_SHADOW
            #include "HS2NextSkin.hlsl"
            #define HS2_PASS_A HS2SkinSA
            #define HS2_PASS_V HS2SkinSV
            #define HS2_PASS_VERT HS2SkinDepthVert
            #define HS2_PASS_CLIP HS2SkinClipFrag
            #define HS2_PASS_POS(a) (a).pos.xyz
            #define HS2_PASS_NORMAL(a) (a).n
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
    }
}
