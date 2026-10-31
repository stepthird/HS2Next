// URP approximation of the mod shader "UBER - Metallic Setup/2 Sided/ Core" (Phase 3.7 package F3, W8). Same property names, types,
// defaults and flags as the original (204 properties); the main maps evaluated in Unity Standard's metallic model
// (HS2NextStdLike.hlsl, HS2_SL_UBER); the rest of the original's features is not reproduced (see the HLSL header).
Shader "HS2Next/Compatible/UBER - Metallic Setup/2 Sided/ Core"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1,1,1,1)
        [HDR] _Color2 ("Color2", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _MainTex2 ("Albedo2", 2D) = "white" {}
        _DecalMaskGUI ("Decal mask (GUI)", Float) = 3
        _DecalMask ("Decal Mask", Float) = 1
        _Pierceable ("Pierceable", Float) = 0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _CutoffEdgeGlow ("Edge glow", Color) = (0,0,0,0.02)
        _Glossiness ("Smoothness", Range(0, 1)) = 1
        _Glossiness2 ("Smoothness2", Range(0, 1)) = 1
        [Gamma] _Metallic ("Metallic", Range(0, 1)) = 1
        [Gamma] _Metallic2 ("Metallic2", Range(0, 1)) = 1
        _MetallicGlossMap ("Metallic", 2D) = "white" {}
        _MetallicGlossMap2 ("Metallic2", 2D) = "white" {}
        _BumpScale ("Scale", Float) = 1
        _BumpScale2 ("Scale2", Float) = 1
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpMap2 ("Normal Map2", 2D) = "bump" {}
        _Parallax ("Height Scale", Range(0, 0.2)) = 0.04
        _Parallax2 ("Height Scale2", Range(0, 0.2)) = 0.04
        _ParallaxMap ("Height Map", 2D) = "black" {}
        _ParallaxMap2 ("Height Map2", 2D) = "black" {}
        _OcclusionStrength ("Strength", Range(0, 1)) = 0
        _OcclusionStrength2 ("Strength2", Range(0, 1)) = 0
        _SecOcclusionStrength ("Strength (secondary)", Range(0, 1)) = 0
        _OcclusionMap ("Occlusion", 2D) = "white" {}
        _EmissionColor ("Color", Color) = (0,0,0,1)
        _EmissionMap ("Emission", 2D) = "white" {}
        _DetailMask ("Detail Mask", 2D) = "white" {}
        _DetailAlbedoMap ("Detail Albedo x2", 2D) = "grey" {}
        _DetailNormalMapScale ("Scale", Float) = 1
        _DetailNormalMap ("Normal Map", 2D) = "bump" {}
        _UVSec ("UV Set for secondary textures", Float) = 0
        _UVSecOcclusion ("UV Set for occlusion", Float) = 0
        _UVSecOcclusionLightmapPacked ("2ndary occlusion from UV1 - apply lightmap transform", Float) = 0
        [HideInInspector] _Mode ("__mode", Float) = 0
        [HideInInspector] _SrcBlend ("__src", Float) = 1
        [HideInInspector] _DstBlend ("__dst", Float) = 0
        [HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0
        [HideInInspector] _ZWrite ("__zw", Float) = 1
        _DiffuseScatter ("Diffuse Scatter", Float) = 1
        _DiffuseScatteringColor ("Diffuse scattering", Color) = (0,0,0,0)
        _DiffuseScatteringColor2 ("Diffuse scattering2", Color) = (0,0,0,0)
        _DiffuseScatteringExponent ("Diffuse scattering exponent", Range(2, 20)) = 8
        _DiffuseScatteringOffset ("Diffuse scattering offset", Range(-0.5, 0.5)) = 0
        _GlossMin ("Gloss Min", Range(0, 1)) = 0
        _GlossMax ("Gloss Max", Range(0, 1)) = 1
        _BendNormalsFreq ("Bend normals frequency", Float) = 4
        _BendNormalsStrength ("Bend normals strangth", Range(0, 0.2)) = 0.05
        _DetailUVMult ("Detail mask tiling mult", Float) = 1
        _DetailNormalLerp ("Detail normalmap override", Range(0, 1)) = 0
        _DetailColor ("Tint (RGB+A - opacity)", Color) = (1,1,1,1)
        _DetailEmissiveness ("Detail emissiveness", Range(0, 1)) = 0
        _SpecularRGBGlossADetail ("Specular(RGB) Gloss(A)", 2D) = "white" {}
        _DetailSpecLerp ("Spec/Gloss override", Range(0, 1)) = 1
        _DetailSpecGloss ("Spec/gloss Tint (RGB,A)", Color) = (1,1,1,1)
        _MetallicGlossMapDetail ("Metallic(R) Gloss(A)", 2D) = "white" {}
        _DetailMetalness ("Detail metallic", Range(0, 1)) = 1
        _DetailGloss ("Detail smoothness", Range(0, 1)) = 1
        _PanEmissionMask ("Pan Emission Mask", Float) = 0
        _PanUSpeed ("   Pan U Speed", Float) = 0
        _PanVSpeed ("   Pan V Speed", Float) = 0
        _PulsateEmission ("Pulsate Emission", Float) = 0
        _EmissionPulsateSpeed ("   Emission Pulsate Frequency", Float) = 0
        _MinPulseBrightness ("   Min Pulse Brightness", Range(0, 1)) = 0
        _Translucency ("Translucency", Float) = 0
        _TranslucencyShown ("_TranslucencyShown", Float) = 1
        _TranslucencyColor ("Translucency color", Color) = (1,1,1,1)
        _TranslucencyColor2 ("Translucency color2", Color) = (1,1,1,1)
        _TranslucencyStrength ("Translucency strength", Float) = 4
        _TranslucencyPointLightDirectionality ("Point lights directionality", Range(0, 1)) = 0.7
        _TranslucencyConstant ("Translucency constant", Range(0, 0.5)) = 0.1
        _TranslucencyNormalOffset ("Translucency normal offset", Range(0, 0.3)) = 0.05
        _TranslucencyExponent ("Translucency exponent", Range(2, 100)) = 30
        _TranslucencyOcclusion ("Translucency occlusion", Range(0, 1)) = 0
        _TranslucencySuppressRealtimeShadows ("Translucency shadows suppression", Range(0, 20)) = 0.5
        _TranslucencyNDotL ("Translucency NdotL", Range(0, 1)) = 0
        _TranslucencyDeferredLightIndex ("Translucency deferred light index", Float) = 0
        _POM ("POM", Float) = 0
        _POMShown ("_POMShown", Float) = 1
        _DepthWrite ("Zwrite", Float) = 0
        _Depth ("Depth", Range(0.001, 2)) = 0.1
        _DistSteps ("Max relief steps", Float) = 64
        _ReliefMIPbias ("Relief MIP offset", Range(0, 2)) = 0
        _ObjectNormalsTex ("Object normals tex", 2D) = "grey" {}
        _ObjectTangentsTex ("Object tangents tex", 2D) = "grey" {}
        _CurvatureMultOffset ("Curvature", Vector) = (1,1,0,0)
        _Tan2ObjectMultOffset ("Tex2Object", Vector) = (1,1,0,0)
        _UV_Clip_Borders ("UV clip borders", Vector) = (0,0,1,1)
        _POM_BottomCut ("Bottom cut value", Range(0, 1)) = 0
        _POM_MeshIsVolume ("Depth per vertex", Float) = 0
        _POM_ExtrudeVolume ("Extruded mesh", Float) = 0
        _POMPrecomputedFlag ("POM precomputed", Float) = 0
        _POMCurvatureType ("Type of curvature mapping", Float) = 0
        _DepthReductionDistance ("Depth reduction distance", Range(1, 100)) = 20
        _CurvatureCustomU ("Curvature custom U", Float) = 0
        _CurvatureCustomV ("Curvature custom V", Float) = 0
        _CurvatureMultU ("Curvature mult U", Float) = 1
        _CurvatureMultV ("Curvature mult V", Float) = 1
        _Tan2ObjCustomU ("Tex U to Object space", Float) = 1
        _Tan2ObjCustomV ("Tex V to Object space", Float) = 1
        _Tan2ObjMultU ("Tex U to Object space mult", Float) = 1
        _Tan2ObjMultV ("Tex V to Object space mult", Float) = 1
        _UV_Clip ("UV Clip", Float) = 0
        _POMShadows ("Self-shadowing", Float) = 0
        _ShadowStrength ("   Self-shadow strength", Range(0, 1)) = 1
        _DistStepsShadows ("   Max shadow steps", Float) = 64
        _ShadowMIPbias ("   Shadow MIP offset", Range(0, 2)) = 0
        _Softness ("   Softness", Range(6, 0)) = 2
        _SoftnessFade ("   Softness fade", Range(0.2, 1.5)) = 0.4
        _Refraction ("Refration", Range(0, 0.5)) = 0.02
        _RefractionBumpScale ("Refraction bump scale", Range(0, 2)) = 0.5
        _RefractionChromaticAberration ("Chromatic Aberration", Range(0, 0.1)) = 0
        _Wetness ("Wetness", Float) = 0
        _WetnessShown ("_WetnessShown", Float) = 1
        _WetnessLevel ("Level (Height Map dependent)", Range(0, 1.25)) = 1
        _WetnessConst ("Const (Height Map independent)", Range(0, 1)) = 0
        _WetnessColor ("Wetness Color (RGB Tint, A Opacity)", Color) = (0,0,0,0)
        _WetnessDarkening ("Wetness darkening", Range(0, 8)) = 2
        _WetnessSpecGloss ("Wetness Specular (RGB) Gloss (A)", Color) = (0.05,0.05,0.05,0.7)
        _WetnessEmissiveness ("Wetness emissiveness", Range(0, 20)) = 0
        _WetnessNormalInfluence ("Wetness normal override", Range(0, 1)) = 0.5
        _WetnessUVMult ("Wetness - Detail mask tiling", Float) = 1
        _RippleMap ("Ripple Map", 2D) = "bump" {}
        _WetRipples ("Ripples (vertex color B)", Float) = 0
        _RippleMapWet ("Ripple Map wet (UI)", 2D) = "bump" {}
        _RippleStrength ("Strength", Range(0.01, 2)) = 0.5
        _RippleTiling ("Tiling", Float) = 1
        _RippleSpecFilter ("Spec filtering", Float) = 0.02
        _RippleAnimSpeed ("Anim speed", Float) = 0.5
        _FlowCycleScale ("Cycle scale", Float) = 2
        _RippleRefraction ("Refraction", Range(0, 2)) = 0.3
        _WetnessNormMIP ("Flow normal MIP level", Range(1, 8)) = 5
        _WetnessNormStrength ("Flow normal strength", Range(0, 4)) = 2
        _WetnessEmissivenessWrap ("Emissiveness normal wrap", Float) = 0
        _WetnessLevelFromGlobal ("Water level controlled globally", Float) = 0
        _WetnessConstFromGlobal ("Water const controlled globally", Float) = 0
        _WetnessFlowGlobalTime ("Water flow global time", Float) = 1
        _WetnessMergeWithSnowPerMaterial ("Water level from snow level (per material)", Float) = 0
        _RippleStrengthFromGlobal ("Flow ripple normal strength controlled globally", Float) = 0
        _RainIntensityFromGlobal ("Rain Intensity controlled globally (multiplied)", Float) = 0
        _WetDroplets ("Droplets (vertex color B)", Float) = 0
        _DropletsMap ("Droplets Map", 2D) = "bump" {}
        _RainIntensity ("Rain Intensity", Range(0, 1)) = 1
        _DropletsTiling ("Tiling", Float) = 1
        _DropletsAnimSpeed ("Anim speed", Float) = 10
        _TessDepth ("Depth", Float) = 0.05
        _TessOffset ("Offset", Range(0, 1)) = 0.1
        _Tess ("Tessellation", Range(1, 60)) = 9
        _TessEdgeLengthLimit ("Tessellation edge limit", Range(2, 50)) = 5
        minDist ("Min camera disance", Float) = 1
        maxDist ("Max camera disance", Float) = 10
        _Phong ("Phong", Range(0, 1)) = 0
        _Snow ("Snow", Float) = 0
        _SnowShown ("_SnowShown", Float) = 1
        _RippleMapSnow ("Ripple Map snow (UI)", 2D) = "bump" {}
        _SnowColorAndCoverage ("Color (RGB Tint, A Level)", Color) = (1,1,1,1)
        _Frost ("Frost", Range(0, 1)) = 0
        _SnowSpecGloss ("Specular (RGB) Gloss (A)", Color) = (0.1,0.1,0.1,0.15)
        _SnowSlopeDamp ("Slope damp", Range(0, 6)) = 2
        _SnowDiffuseScatteringColor ("Diffuse scattering", Color) = (1,1,1,0)
        _SnowDiffuseScatteringExponent ("Diffuse scattering exponent", Range(2, 20)) = 6
        _SnowDiffuseScatteringOffset ("Diffuse scattering offset", Range(-0.5, 0.5)) = 0.4
        _SnowDeepSmoothen ("Deep smoothen", Range(0, 8)) = 4
        _SparkleMapSnow ("Snow detail (UI)", 2D) = "black" {}
        _SnowEmissionTransparency ("Snow emission transparency", Color) = (0.1,0.1,0.1,1)
        _SnowMicroTiling ("Micro tiling", Float) = 4
        _SnowBumpMicro ("  Bumps micro", Range(0.001, 0.2)) = 0.08
        _SnowMacroTiling ("Macro tiling", Float) = 1
        _SnowBumpMacro ("  Bumps macro", Range(0.001, 0.5)) = 0.1
        _SnowWorldMapping ("Snow World mapping", Float) = 0
        _SnowDissolve ("Dissolve", Range(0, 4)) = 2
        _SnowDissolveMaskOcclusion ("Dissolve mask occlusion", Range(0, 1)) = 0
        _SnowTranslucencyColor ("Translucency color", Color) = (0.75,1,1,1)
        _SnowGlitterColor ("Glitter Color", Color) = (0.8,0.8,0.8,0.2)
        _SnowHeightThreshold ("Height threshold", Float) = -10000
        _SnowHeightThresholdTransition ("Height threshold transition", Range(10, 4000)) = 1000
        _SnowLevelFromGlobal ("Snow level controlled globally", Float) = 0
        _FrostFromGlobal ("Frost controlled globally", Float) = 0
        _SnowBumpMicroFromGlobal ("Snow bumps micro controlled globally", Float) = 0
        _SnowDissolveFromGlobal ("Snow dissolve controlled globally", Float) = 0
        _SnowSpecGlossFromGlobal ("Snow spec/gloss controlled globally", Float) = 0
        _SnowGlitterColorFromGlobal ("Snow glitter color controlled globally", Float) = 0
        _Glitter ("Glitter", Float) = 0
        _GlitterShown ("_GlitterShown", Float) = 1
        _SparkleMap ("Sparkle & snow detail", 2D) = "black" {}
        _SparkleMapGlitter ("Glitter map (UI)", 2D) = "black" {}
        _GlitterColor ("Glitter Color", Color) = (0.8,0.8,0.8,0.2)
        _GlitterColor2 ("Glitter Color2", Color) = (0.8,0.8,0.8,0.2)
        _GlitterColorization ("Random colorization", Range(0, 1)) = 0.2
        _GlitterDensity ("Density", Range(0, 1)) = 0.2
        _GlitterTiling ("Tiling", Float) = 1
        _GlitterAnimationFrequency ("Animation frequency", Float) = 0.02
        _GlitterFilter ("Filtering", Range(-4, 4)) = -1
        _GlitterMask ("Masking", Range(0, 1)) = 0
        [HideInInspector] _ShadowCull ("__shadowcull", Float) = 2
        _MainShown ("_MainShown", Float) = 1
        _SecondaryShown ("_SecondaryShown", Float) = 1
        _PresetsShown ("_PresetsShown", Float) = 0
        _Occlusion_from_albedo_alpha ("_Occlusion_from_albedo_alpha", Float) = 0
        _Smoothness_from_albedo_alpha ("_Smoothness_from_albedo_alpha", Float) = 0
        heightMapTexelSize ("heightMap TexelSize fix for substance", Vector) = (1,1,1,1)
        bumpMapTexelSize ("bumpMap TexelSize fix for substance", Vector) = (1,1,1,1)
        bumpMap2TexelSize ("bumpMap2 TexelSize fix for substance", Vector) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "PerformanceChecks"="False" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _Color, _Color2, _MainTex_ST, _MainTex2_ST, _CutoffEdgeGlow, _MetallicGlossMap_ST;
            float4 _MetallicGlossMap2_ST, _BumpMap_ST, _BumpMap2_ST, _ParallaxMap_ST, _ParallaxMap2_ST, _OcclusionMap_ST;
            float4 _EmissionColor, _EmissionMap_ST, _DetailMask_ST, _DetailAlbedoMap_ST, _DetailNormalMap_ST, _DiffuseScatteringColor;
            float4 _DiffuseScatteringColor2, _DetailColor, _SpecularRGBGlossADetail_ST, _DetailSpecGloss, _MetallicGlossMapDetail_ST, _TranslucencyColor;
            float4 _TranslucencyColor2, _ObjectNormalsTex_ST, _ObjectTangentsTex_ST, _CurvatureMultOffset, _Tan2ObjectMultOffset, _UV_Clip_Borders;
            float4 _WetnessColor, _WetnessSpecGloss, _RippleMap_ST, _RippleMapWet_ST, _DropletsMap_ST, _RippleMapSnow_ST;
            float4 _SnowColorAndCoverage, _SnowSpecGloss, _SnowDiffuseScatteringColor, _SparkleMapSnow_ST, _SnowEmissionTransparency, _SnowTranslucencyColor;
            float4 _SnowGlitterColor, _SparkleMap_ST, _SparkleMapGlitter_ST, _GlitterColor, _GlitterColor2, heightMapTexelSize;
            float4 bumpMapTexelSize, bumpMap2TexelSize;
            float _DecalMaskGUI, _DecalMask, _Pierceable, _Cutoff, _Glossiness, _Glossiness2, _Metallic, _Metallic2;
            float _BumpScale, _BumpScale2, _Parallax, _Parallax2, _OcclusionStrength, _OcclusionStrength2, _SecOcclusionStrength, _DetailNormalMapScale;
            float _UVSec, _UVSecOcclusion, _UVSecOcclusionLightmapPacked, _Mode, _SrcBlend, _DstBlend, _ZWrite, _DiffuseScatter;
            float _DiffuseScatteringExponent, _DiffuseScatteringOffset, _GlossMin, _GlossMax, _BendNormalsFreq, _BendNormalsStrength, _DetailUVMult, _DetailNormalLerp;
            float _DetailEmissiveness, _DetailSpecLerp, _DetailMetalness, _DetailGloss, _PanEmissionMask, _PanUSpeed, _PanVSpeed, _PulsateEmission;
            float _EmissionPulsateSpeed, _MinPulseBrightness, _Translucency, _TranslucencyShown, _TranslucencyStrength, _TranslucencyPointLightDirectionality, _TranslucencyConstant, _TranslucencyNormalOffset;
            float _TranslucencyExponent, _TranslucencyOcclusion, _TranslucencySuppressRealtimeShadows, _TranslucencyNDotL, _TranslucencyDeferredLightIndex, _POM, _POMShown, _DepthWrite;
            float _Depth, _DistSteps, _ReliefMIPbias, _POM_BottomCut, _POM_MeshIsVolume, _POM_ExtrudeVolume, _POMPrecomputedFlag, _POMCurvatureType;
            float _DepthReductionDistance, _CurvatureCustomU, _CurvatureCustomV, _CurvatureMultU, _CurvatureMultV, _Tan2ObjCustomU, _Tan2ObjCustomV, _Tan2ObjMultU;
            float _Tan2ObjMultV, _UV_Clip, _POMShadows, _ShadowStrength, _DistStepsShadows, _ShadowMIPbias, _Softness, _SoftnessFade;
            float _Refraction, _RefractionBumpScale, _RefractionChromaticAberration, _Wetness, _WetnessShown, _WetnessLevel, _WetnessConst, _WetnessDarkening;
            float _WetnessEmissiveness, _WetnessNormalInfluence, _WetnessUVMult, _WetRipples, _RippleStrength, _RippleTiling, _RippleSpecFilter, _RippleAnimSpeed;
            float _FlowCycleScale, _RippleRefraction, _WetnessNormMIP, _WetnessNormStrength, _WetnessEmissivenessWrap, _WetnessLevelFromGlobal, _WetnessConstFromGlobal, _WetnessFlowGlobalTime;
            float _WetnessMergeWithSnowPerMaterial, _RippleStrengthFromGlobal, _RainIntensityFromGlobal, _WetDroplets, _RainIntensity, _DropletsTiling, _DropletsAnimSpeed, _TessDepth;
            float _TessOffset, _Tess, _TessEdgeLengthLimit, minDist, maxDist, _Phong, _Snow, _SnowShown;
            float _Frost, _SnowSlopeDamp, _SnowDiffuseScatteringExponent, _SnowDiffuseScatteringOffset, _SnowDeepSmoothen, _SnowMicroTiling, _SnowBumpMicro, _SnowMacroTiling;
            float _SnowBumpMacro, _SnowWorldMapping, _SnowDissolve, _SnowDissolveMaskOcclusion, _SnowHeightThreshold, _SnowHeightThresholdTransition, _SnowLevelFromGlobal, _FrostFromGlobal;
            float _SnowBumpMicroFromGlobal, _SnowDissolveFromGlobal, _SnowSpecGlossFromGlobal, _SnowGlitterColorFromGlobal, _Glitter, _GlitterShown, _GlitterColorization, _GlitterDensity;
            float _GlitterTiling, _GlitterAnimationFrequency, _GlitterFilter, _GlitterMask, _ShadowCull, _MainShown, _SecondaryShown, _PresetsShown;
            float _Occlusion_from_albedo_alpha, _Smoothness_from_albedo_alpha;
            float _HS2FogModes;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Off
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

            #pragma dynamic_branch_local _RECEIVE_SHADOWS_OFF
            #define HS2_SL_FORWARD
            #define HS2_SL_UBER
            #include "HS2NextStdLike.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            Cull Off
            HLSLPROGRAM
            #pragma vertex HS2SLShadowVert
            #pragma fragment HS2SLDepthFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #define HS2_SL_UBER
            #include "HS2NextStdLike.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            Cull Off
            HLSLPROGRAM
            #pragma vertex HS2SLVert
            #pragma fragment HS2SLDepthFrag
            #define HS2_SL_UBER
            #include "HS2NextStdLike.hlsl"
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
            #define HS2_SL_UBER
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
            Cull Off
            HLSLPROGRAM
            #pragma vertex HS2DepthNormalsVert
            #pragma fragment HS2DepthNormalsFrag
            #define HS2_SL_UBER
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
