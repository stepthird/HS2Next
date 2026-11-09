// The gloss of a wet garment: a film drawn over it by a hidden renderer with the garment's mesh and bones. It runs the garment's own
// alpha code (the cutoff, the alpha masks, tearing, dither, wet coverage), so a pixel the garment does not draw, the film does not
// draw; its own alpha is the stain's mask, never _MainTex's. The family's surface and shading come from HS2NextClothes.hlsl, chosen
// by a local keyword:
//   (none)              HS2_CLOTHES_TRUE                                       AIT Clothes True
//   _WETFILM_ITEM       HS2_CLOTHES_ITEM                                       AIT Clothes
//   _WETFILM_CALPHA     HS2_CLOTHES_ITEM, HS2_CLOTHES_CALPHA, HS2_CLOTHES_BLEND   AIT Clothes Alpha
//   _WETFILM_TRUEALPHA  HS2_CLOTHES_TRUE, HS2_CLOTHES_BLEND                    AIT Clothes Alpha True and its BackCull (no Fresnel term)
// Properties: the union of the families', so a garment's values copy over by name, then the film's own. _ExGloss is the film's gloss,
// so the family's wet terms act. A constant polygon offset keeps a tie with the garment's own depth on the film's side.
// _WetFilmFlat (tests) draws the film's pixels flat (1, 0, 1).
Shader "HS2Next/Hidden/WetFilm"
{
    Properties
    {
        [HideInInspector] _WetMask ("Mask", 2D) = "black" {}
        [HideInInspector] _WetFilmSmooth ("Film Smoothness", Float) = 0.8
        [HideInInspector] _WetFilmOpacity ("Film Opacity", Float) = 0.85
        [HideInInspector] _WetFilmFog ("Film Fog Modes", Float) = 0
        [HideInInspector] _WetFilmFlat ("Film Flat", Float) = 0
        [HideInInspector] _WetFilmOffsetFactor ("Film Offset Factor", Float) = 0
        [HideInInspector] _WetFilmOffsetUnits ("Film Offset Units", Float) = -256
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
        _EmissionRim ("EmissionRim", Range(0, 3)) = 0
        _AlphaMaskUVset ("AlphaMaskUVset", Float) = 0
        _patternuv1 ("pattern uv 1", Vector) = (1.000,1.000,0.000,0.000)
        _patternuv2 ("pattern uv 2", Vector) = (1.000,1.000,0.000,0.000)
        _patternuv3 ("pattern uv 3", Vector) = (1.000,1.000,0.000,0.000)
        _patternuv1Rotator ("pattern uv 1 Rotator", Range(-1, 1)) = 0
        _patternuv2Rotator ("pattern uv 2 Rotator", Range(-1, 1)) = 0
        _patternuv3Rotator ("pattern uv 3 Rotator", Range(-1, 1)) = 0
        [HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0
        _FresnelMultiple ("Fresnel Multiple", Vector) = (-0.500,1.000,-0.500,1.000)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Offset [_WetFilmOffsetFactor], [_WetFilmOffsetUnits]
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex HS2ClothesVert
            #pragma fragment WetFilmFrag
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
            #pragma multi_compile_local _ _WETFILM_ITEM _WETFILM_CALPHA _WETFILM_TRUEALPHA
            #if defined(_WETFILM_ITEM)
                #define HS2_CLOTHES_ITEM
            #elif defined(_WETFILM_CALPHA)
                #define HS2_CLOTHES_ITEM
                #define HS2_CLOTHES_CALPHA
                #define HS2_CLOTHES_BLEND
            #elif defined(_WETFILM_TRUEALPHA)
                #define HS2_CLOTHES_TRUE
                #define HS2_CLOTHES_BLEND
            #else
                #define HS2_CLOTHES_TRUE
            #endif
            // the blended families receive no main-light shadow
            #if defined(HS2_CLOTHES_BLEND)
                #undef _MAIN_LIGHT_SHADOWS
                #undef _MAIN_LIGHT_SHADOWS_CASCADE
                #undef _MAIN_LIGHT_SHADOWS_SCREEN
                #undef _SHADOWS_SOFT
            #endif
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "HS2NextClothes.hlsl"
            #include "HS2NextFog.hlsl"

            TEXTURE2D(_WetMask); SAMPLER(sampler_WetMask);
            float _WetFilmSmooth, _WetFilmOpacity, _WetFilmFog, _WetFilmFlat;

            half4 WetFilmFrag(HS2ClothesVaryings i, bool front : SV_IsFrontFace) : SV_Target
            {
                half alpha = HS2ClothesAlpha(i.uv, i.pos);
                half m = SAMPLE_TEXTURE2D(_WetMask, sampler_WetMask, i.uv.xy).r;
                clip(m - 0.004);
                if (_WetFilmFlat > 0.5) return half4(1, 0, 1, 1);
                HS2ClothesSurface cs = HS2ClothesSurf(i, front);
                cs.smoothness = max(cs.smoothness, _WetFilmSmooth);
                HS2Surface s = HS2SurfaceMetallic(cs.albedo, cs.metallic, cs.smoothness, cs.occlusion, cs.normalWS);
                s.emission = cs.emission;
                s.transStrength = _Translucency * cs.transMask; s.transDirect = _TransDirect; s.transAmbient = _TransAmbient;
            #if defined(HS2_CLOTHES_TRUE) && !defined(HS2_CLOTHES_BLEND)
                if (!front) s.transStrength *= 0.4;
            #endif
                s.transScattering = _TransScattering; s.transDistortion = _TransNormalDistortion; s.transShadow = _TransShadow;
                s.transmission = cs.albedo * (_TransmissionStrength * 0.25 * cs.transMask);
                half3 c = HS2Shade(s, i.wpos, GetNormalizedScreenSpaceUV(i.pos));
                half a = m * _WetFilmOpacity;
            #if defined(HS2_CLOTHES_BLEND)
                a *= alpha;
            #endif
                return half4(HS2ApplyFog(c, i.wpos, _WetFilmFog), a);
            }
            ENDHLSL
        }
    }
}
