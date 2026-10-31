// URP replacement for HS2 "AIT/Skin True Face" (face skin, o_head slot 0). Same property names,
// types and defaults as the original so a loaded material keeps its values. Behaviour inferred by
// black-box probing only (Assets/HS2Next/Editor/Fit/SkinFit.cs; data in Spike/out/fit/); the surface code
// is shared with the body in HS2NextSkin.hlsl, lighting is HS2NextBRDF.hlsl (Built-in BRDF1).
//
// Inferred behaviour (game keyword set: _EMISSION _NODE_686_ON _TUNINGCOLOR21OR3_ON _TUNINGSPEC21OR3_ON):
//  _MainTex * _Color               base colour (the game composites skin colour/makeup into _MainTex).
//  _NailMask (cf_head_00_mask)     R: tint by _Color13 (multiply, weight R * _Color13.a);
//                                  G: enables the detail map (DetailGlossMap G = occlusion via max(1 - G, detail.G),
//                                     A = _Gloss multiplier) and a direct-light-only albedo lift (see _FresnelSetting);
//                                  B: blush, replace by _Color4 with weight B * _Texture4Scale * _Color4.a;
//                                  A: translucency mask.
//  _OcclusionMap                   R: gloss mask, G: occlusion (via _OcclusionStrength), B * _BumpScale2 = k:
//                                  albedo *= (1 - 0.03k) * lerp(1, c / max(c), 0.124k); occlusion -= 0.08k * strength;
//                                  translucency colour += 0.15k * hue.
//  _Gloss, _ExGloss                smoothness = max(_Gloss * detail A, _ExGloss) * OcclusionMap.R, then max with
//                                  _DetailMainTex.B (painted gloss); _DetailMainTex.R = painted metallic (max with _Metallic).
//  _BumpMap * _BumpScale, _BumpMap2 * _BumpScale2   whiteout-blended tangent normals.
//  _Texture3 (eyebrow)             on UV1 via _Texture3UV (translate, scale) and _Texture3Rotator (* pi);
//                                  colour _Color3 * Texture3.B, weight Texture3.B * _Color3.a * (1 - vertex colour B),
//                                  smoothness min(s + 0.15w, lerp(0.99, 0.15, w)).
//  _FresnelSetting (x,y,z,w)       lift = w * smoothness * NailMask.G * saturate(1 - (x + y * pow(1 - NdotV, z)));
//                                  direct-light albedo = saturate(albedo + lift * NdotL), also used as metal F0.
//  _Texture5 (beard)               on UV0 mirrored about the face centre: uv = (|x u + z - 0.5|, y v + w) with
//                                  _Texture5UV = (x, y, z, w) (game (1.9, 2, -0.45, 0) = (1.9 |u - 0.5|, 2 v)); _Texture5_ST,
//                                  uv1/uv2 and vertex colour unused. R: replace by _Color5, weight
//                                  saturate(2a) * pow(R, 4 - 3a) (a = _Color5.a), after the eyebrow; smoothness >= 0.5 * w.
//                                  G: height map, tangent normal xy = (G - G(uv + 0.0055 along u / v)) * 1.5 * a
//                                  (fixed texture-space offset), whiteout-blended over the bump maps. B, A unused.
//  Translucency                    Amplify model: albedo * transColor * light * (pow(V.-(L + N*dist), scattering)
//                                  * direct + GI * ambient) * strength, shadow via _TransShadow.
//  No measurable effect: DetailGlossMap.R/B, _Riality, _DetailMainScale,
//  _DetailNormalMapScale, _Texture2, _NipScale, _Weathering* (liquid overlay, not implemented).
// Properties the game writes at load: _MainTex/_DetailMainTex (composited RTs),
// _OcclusionMap, _BumpMap, _BumpMap2, _BumpScale2, _Gloss, _Metallic, _Texture3 + _Color3 + _Texture3UV +
// _Texture3Rotator (eyebrow), _Texture4Scale (blush), _ExGloss (wet), _WeatheringRange6 (fluids).
// Fit: see Assets/HS2Next/Shaders/README.md.
Shader "HS2Next/AIT Skin True Face"
{
    Properties
    {
        _FresnelSetting ("Fresnel Setting", Vector) = (0,1.985,50,1)
        _WeatheringMap ("WeatheringMap", 2D) = "black" {}
        _WeatheringMask ("WeatheringMask", 2D) = "black" {}
        _WeatheringUV ("WeatheringUV", Vector) = (3,3,0,0)
        _WeatheringAll ("WeatheringAll", Range(0,1)) = 0
        _WeatheringRange1 ("WeatheringRange1", Range(0,1)) = 0
        _WeatheringRange2 ("WeatheringRange2", Range(0,1)) = 0
        _WeatheringRange3 ("WeatheringRange3", Range(0,1)) = 0
        _WeatheringRange4 ("WeatheringRange4", Range(0,1)) = 0
        _WeatheringRange5 ("WeatheringRange5", Range(0,1)) = 0
        _WeatheringRange6 ("WeatheringRange6", Range(0,1)) = 0
        _Color2 ("Color 2 nip", Color) = (1,0.934,0.934,1)
        _Color3 ("Color 3 mnpk", Color) = (0.434,0.434,0.434,0)
        _Color4 ("Color 4", Color) = (0.899,0.454,0.454,1)
        _Color5 ("Color 5", Color) = (0,0,0,1)
        _Color13 ("Color 13", Color) = (0.797,0.702,0.635,1)
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Main Tex", 2D) = "white" {}
        _DetailMainTex ("DetailMainTex", 2D) = "black" {}
        _Texture2 ("Texture2(UV2nip)", 2D) = "black" {}
        _Texture3 ("Texture3(UV3mnpk)", 2D) = "black" {}
        _Texture5 ("Texture5(Hige)", 2D) = "black" {}
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
        _Texture5UV ("Texture5 UV", Vector) = (0,0,0,0)
        [Header(Translucency)] _Translucency ("Strength", Range(0,50)) = 1
        _TransNormalDistortion ("Normal Distortion", Range(0,1)) = 0.1
        _TransScattering ("Scaterring Falloff", Range(1,50)) = 2
        _TransDirect ("Direct", Range(0,1)) = 1
        _TransAmbient ("Ambient", Range(0,1)) = 0.2
        _TransShadow ("Shadow", Range(0,1)) = 0.9
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest-40" }
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
            #define HS2_SKIN_FACE
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
            #define HS2_SKIN_FACE
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
            #define HS2_SKIN_FACE
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
            #define HS2_SKIN_FACE
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
            #define HS2_SKIN_FACE
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
