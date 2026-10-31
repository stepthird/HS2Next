// URP replacement for HS2 "AIT/Skin Translucency simple" (tongue c_m_tang on o_tang, teeth c_m_tooth
// on o_tooth; game keywords _COLOR4SWICH_ON _DETAIL_MULX2 _NODE_2929_ON _NORMALMAP _WRAPTEXBASE_ON).
// Same property names, types and defaults as the original. Behaviour inferred by black-box probing
// only (Assets/HS2Next/Editor/Fit/SkinFit.cs, Spike/out/fit/AIT_Skin_Translucency_simple_*.txt):
//  _MainTex * _Color               base colour (no skin "tuning").
//  _OcclusionMap                   R: gloss mask, G: occlusion, B: translucency mask; sampled on plain UV0
//                                  (the material's tiling/offset is ignored: the teeth set (0.8,0.72,0.09,0.15)).
//  _DetailGlossMap                 G: detail occlusion (occlusion = lerp(1, min(Occ.G, Detail.G), _OcclusionStrength)),
//                                  A: gloss multiplier; smoothness = _Gloss * Occ.R * Detail.A.
//  _BumpMap * _NormalMapScale      tangent normal (Built-in style scaling).
//  _MatcapTexture                  sampled at 0.5 + viewNormal.xy * 0.5 * _matcaptilling; lifts the albedo in
//                                  direct light only: saturate(albedo + matcap * _matcapscale * NdotL) (also metal F0).
//  Translucency                    Amplify model with colour albedo * Occ.B (see HS2NextBRDF.hlsl).
//  Not reproduced: _DetailNormalMapScale (a weak normal perturbation from the detail map's G gradient,
//  about 2% of the lit value at scale 20 in the probes).
// The game writes nothing at load for these materials (prefab values).
Shader "HS2Next/AIT Skin Translucency simple"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MatcapTexture ("Matcap Texture", 2D) = "black" {}
        _matcaptilling ("matcap tilling", Float) = 1
        _matcapscale ("matcap scale", Range(0,1)) = 1
        _MainTex ("Main Tex", 2D) = "white" {}
        _OcclusionMap ("Occlusion Map", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _DetailGlossMap ("Detail Gloss Map (R:Col)(G:Occ)(B:Rim)(A:Gloss)", 2D) = "white" {}
        _Metallic ("Metallic", Range(0,1)) = 0.03
        _Gloss ("Gloss", Range(0,1)) = 0.55
        _OcclusionStrength ("OcclusionStrength", Range(0,1)) = 1
        _DetailNormalMapScale ("Detail Normal Map Scale", Float) = 1
        _NormalMapScale ("Normal Map Scale", Float) = 1
        [Header(Translucency)] _Translucency ("Strength", Range(0,50)) = 1
        _TransNormalDistortion ("Normal Distortion", Range(0,1)) = 0.1
        _TransScattering ("Scaterring Falloff", Range(1,50)) = 2
        _TransDirect ("Direct", Range(0,1)) = 1
        _TransAmbient ("Ambient", Range(0,1)) = 0.2
        _TransShadow ("Shadow", Range(0,1)) = 0.9
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST, _OcclusionMap_ST, _BumpMap_ST, _DetailGlossMap_ST, _MatcapTexture_ST, _Color;
            float _matcaptilling, _matcapscale, _Metallic, _Gloss, _OcclusionStrength, _DetailNormalMapScale, _NormalMapScale;
            float _Translucency, _TransNormalDistortion, _TransScattering, _TransDirect, _TransAmbient, _TransShadow;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "HS2NextBRDF.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_DetailGlossMap); SAMPLER(sampler_DetailGlossMap);
            TEXTURE2D(_MatcapTexture); SAMPLER(sampler_MatcapTexture);

            struct A { float4 pos : POSITION; float3 n : NORMAL; float4 t : TANGENT; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1; float3 n : TEXCOORD2; float4 t : TEXCOORD3; };

            V vert(A a)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
                VertexNormalInputs nn = GetVertexNormalInputs(a.n, a.t);
                o.pos = p.positionCS; o.wpos = p.positionWS;
                o.n = nn.normalWS; o.t = float4(nn.tangentWS, a.t.w * GetOddNegativeScale());
                o.uv = a.uv;
                return o;
            }

            // Built-in UnpackScaleNormal (xy scaled before z is rebuilt), RG/AG packing.
            half3 UnpackBuiltin(half4 p, half scale)
            {
                p.x *= p.w;
                half2 xy = (p.xy * 2 - 1) * scale;
                return half3(xy, sqrt(1 - saturate(dot(xy, xy))));
            }

            half4 frag(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(i.uv, _MainTex)).rgb * _Color.rgb;
                half4 oc = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, i.uv);   // no _ST (measured)
                half4 dg = SAMPLE_TEXTURE2D(_DetailGlossMap, sampler_DetailGlossMap, TRANSFORM_TEX(i.uv, _DetailGlossMap));
                half smooth = saturate(_Gloss * oc.r * dg.a);
                half occ = lerp(1, min(oc.g, dg.g), _OcclusionStrength);

                half3 nTS = UnpackBuiltin(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, TRANSFORM_TEX(i.uv, _BumpMap)), _NormalMapScale);
                float3 n = normalize(i.n) * (front ? 1 : -1);
                float3 t = normalize(i.t.xyz);
                float3 b = cross(n, t) * i.t.w;
                float3 nWS = normalize(mul(nTS, float3x3(t, b, n)));

                float3 nVS = TransformWorldToViewDir(nWS, true);
                half3 matcap = SAMPLE_TEXTURE2D(_MatcapTexture, sampler_MatcapTexture, 0.5 + nVS.xy * 0.5 * _matcaptilling).rgb;

                HS2Surface s = HS2SurfaceMetallic(albedo, _Metallic, smooth, occ, nWS);
                s.lift = matcap * _matcapscale;
                s.transStrength = _Translucency; s.transDirect = _TransDirect; s.transAmbient = _TransAmbient;
                s.transScattering = _TransScattering; s.transDistortion = _TransNormalDistortion;
                s.transColor = albedo * oc.b; s.transShadow = _TransShadow;
                return half4(HS2Shade(s, i.wpos, GetNormalizedScreenSpaceUV(i.pos)), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;
            struct A { float4 pos : POSITION; float3 n : NORMAL; };
            float4 vert(A a) : SV_POSITION
            {
                float3 w = TransformObjectToWorld(a.pos.xyz);
                float4 p = TransformWorldToHClip(ApplyShadowBias(w, TransformObjectToWorldNormal(a.n), _LightDirection));
            #if UNITY_REVERSED_Z
                p.z = min(p.z, p.w * UNITY_NEAR_CLIP_VALUE);
            #else
                p.z = max(p.z, p.w * UNITY_NEAR_CLIP_VALUE);
            #endif
                return p;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 pos : POSITION) : SV_POSITION { return TransformObjectToHClip(pos.xyz); }
            half4 frag() : SV_Target { return 0; }
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
            float4 vert(float4 pos : POSITION) : SV_POSITION { return TransformObjectToHClip(pos.xyz); }
            half4 frag() : SV_Target { return 0; }
            #define HS2_PASS_SIMPLE
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
            float4 vert(float4 pos : POSITION) : SV_POSITION { return TransformObjectToHClip(pos.xyz); }
            half4 frag() : SV_Target { return 0; }
            #define HS2_PASS_SIMPLE
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
    }
}
