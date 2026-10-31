// Surface of the mod shader "hooh/ColorableObjectReplica" (Phase 3.7 package F2), evaluated with the Built-in BRDF1 of
// HS2NextBRDF.hlsl. The original is an Amplify surface shader of its own (not related to the ClotheReplica family).
// Inferred by black-box probing only (Tests/FitCases/hooh_cor_*.txt).
//
// Measured (flat probe values exact to the 4th decimal):
//   M = _ColorMask.rgb; colour = saturate(1 - M.r - M.g - M.b) Color + M.r Color2 + M.g Color3 + M.b Color4 (alphas unused)
//   albedo     = MainTex.rgb * colour
//   metallic   = saturate(lerp(Metallic, 1, M.r (Metallic2 + Metallic3 + Metallic4)) + MetallicSmoothness.R)
//   smoothness = saturate(lerp(Glossiness, 1, M.r (Glossiness2 + Glossiness3 + Glossiness4)) + MetallicSmoothness.A)
//                (only the mask's R channel mixes metal / gloss: G and B leave them at region 1)
//   occlusion  = lerp(1, AmbientOcclusion.R, AmbientOcclusionstrength)
//   emission   = _Emission.rgb * EmissionBoost (texture)
//   normal     = whiteout blend of _NormalMap, _BlendNormalMap and _DetailNormalMap (Unity's UnpackScaleNormal with
//                their strengths); _NormalizedNormalLerp has no visible effect
//   UVs        uv0 with each texture's own tiling / offset; Cull Back
#ifndef HS2NEXT_HOOH_COLORABLE_INCLUDED
#define HS2NEXT_HOOH_COLORABLE_INCLUDED
#include "HS2NextBRDF.hlsl"

TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
TEXTURE2D(_BlendNormalMap); SAMPLER(sampler_BlendNormalMap);
TEXTURE2D(_DetailNormalMap); SAMPLER(sampler_DetailNormalMap);
#if defined(HS2_COR_MS1)
// the bundle copy that names its packed map _MetallicSmoothness1 (otherwise the same shader)
#define _MetallicSmoothness _MetallicSmoothness1
#define sampler_MetallicSmoothness sampler_MetallicSmoothness1
#define _MetallicSmoothness_ST _MetallicSmoothness1_ST
#endif
TEXTURE2D(_MetallicSmoothness); SAMPLER(sampler_MetallicSmoothness);
TEXTURE2D(_ColorMask); SAMPLER(sampler_ColorMask);
TEXTURE2D(_AmbientOcclusion); SAMPLER(sampler_AmbientOcclusion);
TEXTURE2D(_Emission); SAMPLER(sampler_Emission);

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST, _NormalMap_ST, _BlendNormalMap_ST, _DetailNormalMap_ST, _MetallicSmoothness_ST, _ColorMask_ST;
    float4 _AmbientOcclusion_ST, _Emission_ST;
    float4 _Color, _Color2, _Color3, _Color4;
    float _BlendNormalMapStrength, _DetailNormalMapStrength, _NormalMapStrength, _NormalizedNormalLerp, _EmissionBoost;
    float _AmbientOcclusionstrength;
    float _Metallic, _Metallic2, _Metallic3, _Metallic4, _Glossiness, _Glossiness2, _Glossiness3, _Glossiness4;
CBUFFER_END


struct HS2HoohCorAttributes { float4 pos : POSITION; float3 n : NORMAL; float4 t : TANGENT; float2 uv : TEXCOORD0; };
struct HS2HoohCorVaryings
{
    float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1;
    float3 n : TEXCOORD2; float4 t : TEXCOORD3; float3 b : TEXCOORD4;
};

HS2HoohCorVaryings HS2HoohCorVert(HS2HoohCorAttributes a)
{
    HS2HoohCorVaryings o;
    VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
    VertexNormalInputs nn = GetVertexNormalInputs(a.n, a.t);
    o.pos = p.positionCS; o.wpos = p.positionWS;
    o.n = nn.normalWS; o.t = float4(nn.tangentWS, a.t.w * GetOddNegativeScale());
    o.b = cross(o.n, o.t.xyz) * o.t.w;   // Built-in surface shaders build the binormal per vertex
    o.uv = a.uv;
    return o;
}

half3 HS2HoohCorUnpackNormal(half4 packed, half scale)
{
    packed.x *= packed.w;
    half3 n;
    n.xy = (packed.xy * 2 - 1) * scale;
    n.z = sqrt(1 - saturate(dot(n.xy, n.xy)));
    return n;
}

half4 HS2HoohCorFrag(HS2HoohCorVaryings i) : SV_Target
{
    float2 uv = i.uv;
    half3 m = SAMPLE_TEXTURE2D(_ColorMask, sampler_ColorMask, TRANSFORM_TEX(uv, _ColorMask)).rgb;
    half3 col = saturate(1 - m.r - m.g - m.b) * _Color.rgb + m.r * _Color2.rgb + m.g * _Color3.rgb + m.b * _Color4.rgb;
    half3 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(uv, _MainTex)).rgb * col;
    half4 ms = SAMPLE_TEXTURE2D(_MetallicSmoothness, sampler_MetallicSmoothness, TRANSFORM_TEX(uv, _MetallicSmoothness));
    half metallic = saturate(lerp(_Metallic, 1, m.r * (_Metallic2 + _Metallic3 + _Metallic4)) + ms.r);
    half smoothness = saturate(lerp(_Glossiness, 1, m.r * (_Glossiness2 + _Glossiness3 + _Glossiness4)) + ms.a);
    half occlusion = lerp(1, SAMPLE_TEXTURE2D(_AmbientOcclusion, sampler_AmbientOcclusion, TRANSFORM_TEX(uv, _AmbientOcclusion)).r, _AmbientOcclusionstrength);
    half3 n0 = HS2HoohCorUnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, TRANSFORM_TEX(uv, _NormalMap)), _NormalMapStrength);
    half3 n1 = HS2HoohCorUnpackNormal(SAMPLE_TEXTURE2D(_BlendNormalMap, sampler_BlendNormalMap, TRANSFORM_TEX(uv, _BlendNormalMap)), _BlendNormalMapStrength);
    half3 n2 = HS2HoohCorUnpackNormal(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, TRANSFORM_TEX(uv, _DetailNormalMap)), _DetailNormalMapStrength);
    half3 nTS = normalize(half3(n0.xy + n1.xy, n0.z * n1.z));   // measured: two sequential whiteout blends
    nTS = normalize(half3(nTS.xy + n2.xy, nTS.z * n2.z));
    float3 nWS = normalize(i.t.xyz * nTS.x + i.b * nTS.y + i.n * nTS.z);
    HS2Surface s = HS2SurfaceMetallic(albedo, metallic, smoothness, occlusion, nWS);
    s.emission = SAMPLE_TEXTURE2D(_Emission, sampler_Emission, TRANSFORM_TEX(uv, _Emission)).rgb * _EmissionBoost;
    return half4(HS2Shade(s, i.wpos, GetNormalizedScreenSpaceUV(i.pos)), 1);
}

float3 _LightDirection;
float3 _LightPosition;

HS2HoohCorVaryings HS2HoohCorShadowVert(HS2HoohCorAttributes a)
{
    HS2HoohCorVaryings o = HS2HoohCorVert(a);
#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDir = normalize(_LightPosition - o.wpos);
#else
    float3 lightDir = _LightDirection;
#endif
    float4 cs = TransformWorldToHClip(ApplyShadowBias(o.wpos, o.n, lightDir));
#if UNITY_REVERSED_Z
    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
#else
    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
#endif
    o.pos = cs;
    return o;
}

half4 HS2HoohCorDepthFrag(HS2HoohCorVaryings i) : SV_Target { return 0; }
#endif
