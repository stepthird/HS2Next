// Surface of the hooh "RMA" mod shaders (Phase 3.7 package F2): hooh/Accessory/RMA/{Opqaue, Cutout} and
// hooh/Clothing/RMA/{Opqaue, Transparent, MaskedTransparent}, evaluated with the Built-in BRDF1 of HS2NextBRDF.hlsl. The
// originals are Amplify surface shaders around a packed map "PackedMap (Gloss, Metal, AO)" (_MetallicGlossMap R / G / B,
// default gray) and two detail normal maps. Inferred by black-box probing only (Tests/FitCases/hooh_rma_*.txt).
//
// Variant switches (define before including):
//   HS2_RMA_ACCESSORY  colour-mask regions: albedo MainTex x lerp(1, Color, M.r) x lerp(1, Color2, M.g) x lerp(1, Color3, M.b);
//                      metal / gloss sliders weighted by the mask (M.r Metallic + M.g Metallic2 + M.b Metallic3; no base
//                      region) plus the packed map
//   HS2_RMA_CUTOUT     (Accessory) clip(MainTex.a x _alpha - _Cutoff)
//   HS2_RMA_CLOTHING   albedo MainTex x BaseColor; metal / smoothness = packed map + the composited _DetailMainTex R / B
//                      (+ _ExGloss on the smoothness)
//   HS2_RMA_BLEND      (Clothing) SrcAlpha blending, alpha MainTex.a x BaseColor.a
//   HS2_RMA_MASKED     (Clothing, with HS2_RMA_BLEND) clip(alpha - _Cutoff) as well
//
// Measured (flat probe values exact to the 4th decimal):
//   metallic   = saturate(sliders + MGM.G (+ DetailMainTex.R)), smoothness = saturate(sliders + MGM.R (+ DetailMainTex.B
//                + ExGloss)), occlusion = MGM.B x OcclusionStrength
//   normal     = whiteout blends of _BumpMap (x _BumpScale), _DetailNormalMap (x _DetailNormalIntensity) and
//                _DetailNormalMap2 (x _DetailNormalIntensity2), each scaled by its _BumpMapMask channel (R / G / B)
//   cull       [_CullMode] as Unity's raw value (0 Off, 1 Front, 2 Back; the inspector's Enum labels are misleading);
//              back faces keep the front normal; Clothing/RMA/Opqaue declares _CullMode but culls Back
//   UVs        uv0 with each texture's own tiling / offset; _IsRoughness1 (_ISROUGHNESS1_ON) is compiled by no copy found
#ifndef HS2NEXT_HOOH_RMA_INCLUDED
#define HS2NEXT_HOOH_RMA_INCLUDED
#include "HS2NextBRDF.hlsl"
#if defined(HS2_RMA_BLEND)
#include "HS2NextFog.hlsl"
#endif

TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
TEXTURE2D(_DetailNormalMap); SAMPLER(sampler_DetailNormalMap);
TEXTURE2D(_DetailNormalMap2); SAMPLER(sampler_DetailNormalMap2);
TEXTURE2D(_BumpMapMask); SAMPLER(sampler_BumpMapMask);
TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_ColorMask); SAMPLER(sampler_ColorMask);
TEXTURE2D(_DetailMainTex); SAMPLER(sampler_DetailMainTex);

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST, _BumpMap_ST, _DetailNormalMap_ST, _DetailNormalMap2_ST, _BumpMapMask_ST, _MetallicGlossMap_ST;
    float4 _ColorMask_ST, _DetailMainTex_ST;
    float4 _Color, _Color2, _Color3, _BaseColor;
    float _Metallic, _Metallic2, _Metallic3, _Glossiness, _Glossiness2, _Glossiness3;
    float _DetailNormalIntensity, _DetailNormalIntensity2, _OcclusionStrength, _BumpScale, _IsRoughness1, _CullMode;
    float _alpha, _Cutoff, _ExGloss;
#if defined(HS2_RMA_BLEND)
    float _HS2FogModes;
#endif
CBUFFER_END

struct HS2RmaAttributes { float4 pos : POSITION; float3 n : NORMAL; float4 t : TANGENT; float2 uv : TEXCOORD0; };
struct HS2RmaVaryings
{
    float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1;
    float3 n : TEXCOORD2; float4 t : TEXCOORD3; float3 b : TEXCOORD4;
};

HS2RmaVaryings HS2RmaVert(HS2RmaAttributes a)
{
    HS2RmaVaryings o;
    VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
    VertexNormalInputs nn = GetVertexNormalInputs(a.n, a.t);
    o.pos = p.positionCS; o.wpos = p.positionWS;
    o.n = nn.normalWS; o.t = float4(nn.tangentWS, a.t.w * GetOddNegativeScale());
    o.b = cross(o.n, o.t.xyz) * o.t.w;   // Built-in surface shaders build the binormal per vertex
    o.uv = a.uv;
    return o;
}

half3 HS2RmaUnpackNormal(half4 packed, half scale)
{
    packed.x *= packed.w;
    half3 n;
    n.xy = (packed.xy * 2 - 1) * scale;
    n.z = sqrt(1 - saturate(dot(n.xy, n.xy)));
    return n;
}

half HS2RmaAlpha(float2 uv)
{
    half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(uv, _MainTex)).a;
#if defined(HS2_RMA_CUTOUT)
    a *= _alpha;
    clip(a - _Cutoff);
#elif defined(HS2_RMA_BLEND)
    a *= _BaseColor.a;
#if defined(HS2_RMA_MASKED)
    clip(a - _Cutoff);
#endif
#endif
    return a;
}

half4 HS2RmaFrag(HS2RmaVaryings i) : SV_Target
{
    float2 uv = i.uv;
    half alpha = HS2RmaAlpha(uv);
    half3 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(uv, _MainTex)).rgb;
    half4 mg = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, TRANSFORM_TEX(uv, _MetallicGlossMap));
#if defined(HS2_RMA_ACCESSORY)
    half3 m = SAMPLE_TEXTURE2D(_ColorMask, sampler_ColorMask, TRANSFORM_TEX(uv, _ColorMask)).rgb;
    half3 albedo = tex * lerp(1, _Color.rgb, m.r) * lerp(1, _Color2.rgb, m.g) * lerp(1, _Color3.rgb, m.b);
    half metallic = saturate(m.r * _Metallic + m.g * _Metallic2 + m.b * _Metallic3 + mg.g);
    half smoothness = saturate(m.r * _Glossiness + m.g * _Glossiness2 + m.b * _Glossiness3 + mg.r);
#else
    half3 albedo = tex * _BaseColor.rgb;
    half4 dmt = SAMPLE_TEXTURE2D(_DetailMainTex, sampler_DetailMainTex, TRANSFORM_TEX(uv, _DetailMainTex));
    half metallic = saturate(mg.g + dmt.r);
    half smoothness = saturate(mg.r + dmt.b + _ExGloss);
#endif
    half occlusion = mg.b * _OcclusionStrength;   // measured: a product, not a lerp (strength 0 = no ambient)
    half3 bm = SAMPLE_TEXTURE2D(_BumpMapMask, sampler_BumpMapMask, TRANSFORM_TEX(uv, _BumpMapMask)).rgb;
    half3 n0 = HS2RmaUnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, TRANSFORM_TEX(uv, _BumpMap)), _BumpScale * bm.r);
    half3 n1 = HS2RmaUnpackNormal(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, TRANSFORM_TEX(uv, _DetailNormalMap)), _DetailNormalIntensity * bm.g);
    half3 n2 = HS2RmaUnpackNormal(SAMPLE_TEXTURE2D(_DetailNormalMap2, sampler_DetailNormalMap2, TRANSFORM_TEX(uv, _DetailNormalMap2)), _DetailNormalIntensity2 * bm.b);
    half3 nTS = normalize(half3(n0.xy + n1.xy, n0.z * n1.z));
    nTS = normalize(half3(nTS.xy + n2.xy, nTS.z * n2.z));
    float3 nWS = normalize(i.t.xyz * nTS.x + i.b * nTS.y + i.n * nTS.z);
    HS2Surface s = HS2SurfaceMetallic(albedo, metallic, smoothness, occlusion, nWS);
#if defined(HS2_RMA_BLEND)
    HS2ShadowReceive = 0;   // Built-in forward rendering: no realtime shadows in the transparent queues
    return half4(HS2ApplyFog(HS2Shade(s, i.wpos, GetNormalizedScreenSpaceUV(i.pos)), i.wpos, _HS2FogModes), alpha);
#else
    return half4(HS2Shade(s, i.wpos, GetNormalizedScreenSpaceUV(i.pos)), 1);
#endif
}

float3 _LightDirection;
float3 _LightPosition;

HS2RmaVaryings HS2RmaShadowVert(HS2RmaAttributes a)
{
    HS2RmaVaryings o = HS2RmaVert(a);
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

half4 HS2RmaDepthFrag(HS2RmaVaryings i) : SV_Target
{
    HS2RmaAlpha(i.uv);
    return 0;
}
#endif
