// Approximate URP stand-in for "KriptoFX/ME/Distortion" (heat haze of vanilla effect items such as wall torches, candles,
// the electric stove and the oil-drum bath, and of vanilla map 0; Phase 3.3
// package C). Same property names, types and defaults as the original's property list (Tools/survey/map_shader_props.json);
// not probed. The original grabs the screen (a GrabPass) and draws one SrcAlpha / OneMinusSrcAlpha pass, ZWrite and cull
// from _ZWriteMode / _CullMode; the vanilla materials use the toggles USE_SOFT_PARTICLES and USE_ALPHA_CLIPING with a
// normal map, _Distortion -60 / -30 and _InvFade 10 on particle systems.
// Here: the scene behind is URP's opaque texture sampled at the screen position shifted by the normal map's xy x
// _Distortion texels x the particle's vertex alpha (x the soft-particle fade), tinted by _MainColor; _UseMainTex adds
// _MainTex x _TintColor x the vertex colour; _UseFresnel adds _FresnelColor x the Schlick term (_FresnelR0, _FresnelPow);
// _UseAlphaCliping hides the flat parts of the normal map (alpha 1 - N.z^_AlphaClip, steepened); _UseCutout masks by
// _CutoutTex.r against _Cutout with the soft width _CutoutThreshold; _UseSoft fades by _InvFade x (scene depth - particle
// depth) while the scene depth exists. The toggles are read as floats (no keywords: no variants). Height displacement and
// refractive / fresnel distortion terms are not reproduced. Without an opaque texture (the global _HS2NextSceneOpaqueValid
// 0, e.g. the measurement asset) it draws nothing, as HS2Next/Hidden/NoDraw did before Phase 3.3.
Shader "HS2Next/Compatible/KriptoFX/ME/Distortion"
{
    Properties
    {
        [Header(Main Settings)] [Toggle(USE_MAINTEX)] _UseMainTex ("Use Main Texture", Float) = 0
        [HDR] _TintColor ("Tint Color", Color) = (1,1,1,1)
        _MainTex ("Main Texture", 2D) = "black" {}
        [Header(Main Settings)] [Normal] _NormalTex ("Normal(RG) Alpha(A)", 2D) = "bump" {}
        [HDR] _MainColor ("Main Color", Color) = (1,1,1,1)
        _Distortion ("Distortion", Float) = 100
        [Toggle(USE_REFRACTIVE)] _UseRefractive ("Use Refractive Distort", Float) = 0
        _RefractiveStrength ("Refractive Strength", Range(-1, 1)) = 0
        [Toggle(USE_SOFT_PARTICLES)] _UseSoft ("Use Soft Particles", Float) = 0
        _InvFade ("Soft Particle Factor", Float) = 3
        [Space] [Header(Height Settings)] [Toggle(USE_HEIGHT)] _UseHeight ("Use Height Map", Float) = 0
        _HeightTex ("Height Tex", 2D) = "white" {}
        _Height ("_Height", Float) = 0.1
        _HeightUVScrollDistort ("Height UV Scroll(XY)", Vector) = (8, 12, 0, 0)
        [Space] [Header(Fresnel)] [Toggle(USE_FRESNEL)] _UseFresnel ("Use Fresnel", Float) = 0
        [HDR] _FresnelColor ("Fresnel Color", Color) = (0.5,0.5,0.5,1)
        _FresnelPow ("Fresnel Pow", Float) = 5
        _FresnelR0 ("Fresnel R0", Float) = 0.04
        _FresnelDistort ("Fresnel Distort", Float) = 1500
        [Space] [Header(Cutout)] [Toggle(USE_CUTOUT)] _UseCutout ("Use Cutout", Float) = 0
        _CutoutTex ("Cutout Tex", 2D) = "white" {}
        _Cutout ("Cutout", Range(0, 1.2)) = 1
        [HDR] _CutoutColor ("Cutout Color", Color) = (1,1,1,1)
        _CutoutThreshold ("Cutout Threshold", Range(0, 1)) = 0.015
        [Space] [Header(Rendering)] [Toggle] _ZWriteMode ("ZWrite On?", Float) = 0
        [Enum(Off,0,Front,1,Back,2)] _CullMode ("Culling", Float) = 2
        [Toggle(USE_ALPHA_CLIPING)] _UseAlphaCliping ("Use Alpha Cliping", Float) = 0
        _AlphaClip ("Alpha Clip Threshold", Float) = 10
        [Toggle(USE_BLENDING)] _UseBlending ("Use Blending", Float) = 0
        [HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Distortion"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ZWriteMode]
            Cull [_CullMode]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "HS2NextFog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);
            TEXTURE2D(_CutoutTex); SAMPLER(sampler_CutoutTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _NormalTex_ST, _CutoutTex_ST, _HeightTex_ST, _HeightUVScrollDistort;
                half4 _TintColor, _MainColor, _FresnelColor, _CutoutColor;
                float _UseMainTex, _Distortion, _UseRefractive, _RefractiveStrength, _UseSoft, _InvFade, _UseHeight, _Height;
                float _UseFresnel, _FresnelPow, _FresnelR0, _FresnelDistort, _UseCutout, _Cutout, _CutoutThreshold;
                float _ZWriteMode, _CullMode, _UseAlphaCliping, _AlphaClip, _UseBlending;
                float _HS2FogModes;
            CBUFFER_END
            // Globals (SceneTextures): 1 when this camera has the opaque colour / the scene depth before the transparents.
            float _HS2NextSceneOpaqueValid, _HS2NextSceneDepthValid;
            struct A { float4 pos : POSITION; float3 n : NORMAL; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; float3 wpos : TEXCOORD2; float3 n : TEXCOORD3; };
            V Vert(A a)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
                o.pos = p.positionCS; o.wpos = p.positionWS;
                o.n = TransformObjectToWorldNormal(a.n);
                o.color = a.color;
                o.uv = float4(TRANSFORM_TEX(a.uv, _NormalTex), TRANSFORM_TEX(a.uv, _MainTex));
                o.uv2 = TRANSFORM_TEX(a.uv, _CutoutTex);
                return o;
            }
            half4 Frag(V i) : SV_Target
            {
                if (_HS2NextSceneOpaqueValid <= 0) { clip(-1); return 0; }
                half3 nrm = UnpackNormal(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, i.uv.xy));
                float2 uv = GetNormalizedScreenSpaceUV(i.pos);
                half fade = 1;
                if (_UseSoft > 0.5 && _HS2NextSceneDepthValid > 0)
                {
                    float sceneZ = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                    fade = saturate(_InvFade * (sceneZ - LinearEyeDepth(i.pos.z, _ZBufferParams)));
                }
                float2 texel = 1.0 / _ScaledScreenParams.xy;
                float2 off = nrm.xy * _Distortion * texel * i.color.a * fade;
                half3 col = SampleSceneColor(uv + off) * _MainColor.rgb;
                if (_UseMainTex > 0.5)
                    col += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.zw).rgb * _TintColor.rgb * i.color.rgb * _TintColor.a;
                if (_UseFresnel > 0.5)
                {
                    float nv = saturate(abs(dot(normalize(i.n), GetWorldSpaceNormalizeViewDir(i.wpos))));
                    col += _FresnelColor.rgb * (_FresnelR0 + (1 - _FresnelR0) * pow(1 - nv, _FresnelPow));
                }
                half mask = 1;
                if (_UseAlphaCliping > 0.5)
                    mask = saturate((1 - pow(saturate(nrm.z), _AlphaClip)) * 8);
                if (_UseCutout > 0.5)
                {
                    half cut = SAMPLE_TEXTURE2D(_CutoutTex, sampler_CutoutTex, i.uv2).r;
                    mask *= saturate((cut - (1 - _Cutout)) / max(_CutoutThreshold, 1e-3));
                }
                return half4(HS2ApplyFog(col, i.wpos, _HS2FogModes), saturate(mask * fade * i.color.a));
            }
            ENDHLSL
        }
    }
}
