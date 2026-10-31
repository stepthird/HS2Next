// Surface of the hooh "ClotheReplica" mod shaders (Phase 3.7 package F2), evaluated with the Built-in BRDF1 of
// HS2NextBRDF.hlsl. The originals are Amplify surface shaders sharing one property set (_Color1.._Color3, _Color*_2,
// _Emission, _MetalicGlossMap, ...); the colour, pattern and emission properties have no visible effect in any of them,
// so HS2's Studio colour writes do nothing on these shaders. Inferred by black-box probing only (Tests/FitCases/hooh_*.txt).
//
// Variant switches (define before including):
//   HS2_HOOH_STANDARD   "hooh/StudioItemStandard": metallic _Metallic x _MetalicGlossMap.R, smoothness _Glossiness
//   HS2_HOOH_REPLICA    "hooh/ClotheReplicaOpaque" / "hooh/ClotheReplicaAlpha": metal R / smoothness B of the composited
//                       _DetailMainTex (default gray)
//   HS2_HOOH_CULLOFF    "hooh/ClotheReplicaAlphaCullOff": metal DetailMainTex1.R x DetailMainTex.R, smoothness
//                       DetailMainTex1.A x DetailMainTex.B, cutout at _Cutoff on MainTex.a x BaseColor.a
//   HS2_HOOH_GMM        (with HS2_HOOH_REPLICA) ClotheReplicaOpaque: metal / smoothness plus _GlossMetalMap R / A x
//                       _SmoothnessMultiply, saturated (bundle copies with and without these properties share the rule)
//   HS2_HOOH_BLEND      blended output (ClotheReplicaAlpha): alpha MainTex.a x BaseColor.a, clip(_CutoutMap.R - _Cutoff)
//   HS2_HOOH_NODMT1     (with HS2_HOOH_CULLOFF) the ClotheReplicaAlphaCullOff copy without _DetailMainTex1 (Phase 5.2): metal /
//                       smoothness of the plain replica rule, the cutout of AlphaCullOff
//
// Measured (flat probe values exact to the 4th decimal):
//   albedo    = MainTex.rgb * BaseColor.rgb
//   occlusion = lerp(1, Occlusion.R, OcclusionStrength)
//   normal    = BlendNormals(BlendNormals(N(_BumpMap, _BumpScale), N(_DetailNormalMap, scale)), N(_DetailNormalMap2, scale2))
//               with Unity's UnpackScaleNormal; surface-shader tangent frame
//   UVs       uv0 with each texture's own tiling / offset
//   no emission; _Color*, _Emission, _MetalicGlossMap G/B/A, _Metallic2..4, _Glossiness2..4, _DetailGlossMap*, _DetailMask,
//   _AlphaMask*, _Weathering* have no visible effect.
#ifndef HS2NEXT_HOOH_INCLUDED
#define HS2NEXT_HOOH_INCLUDED
#include "HS2NextBRDF.hlsl"
#if defined(HS2_HOOH_BLEND)
#include "HS2NextFog.hlsl"
#endif

TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
TEXTURE2D(_DetailNormalMap); SAMPLER(sampler_DetailNormalMap);
TEXTURE2D(_DetailNormalMap2); SAMPLER(sampler_DetailNormalMap2);
TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_MetalicGlossMap); SAMPLER(sampler_MetalicGlossMap);
TEXTURE2D(_DetailMainTex); SAMPLER(sampler_DetailMainTex);
TEXTURE2D(_DetailMainTex1); SAMPLER(sampler_DetailMainTex1);
TEXTURE2D(_CutoutMap); SAMPLER(sampler_CutoutMap);
TEXTURE2D(_GlossMetalMap); SAMPLER(sampler_GlossMetalMap);

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST, _BumpMap_ST, _DetailNormalMap_ST, _DetailNormalMap2_ST, _OcclusionMap_ST, _MetalicGlossMap_ST;
    float4 _DetailMainTex_ST, _DetailMainTex1_ST, _CutoutMap_ST, _GlossMetalMap_ST;
    float4 _BaseColor, _Color1, _Color2, _Color3, _Color1_2, _Color2_2, _Color3_2, _Emission;
    float _BumpScale, _DetailNormalMapScale, _DetailNormalMapScale2, _OcclusionStrength, _Cutoff, _SmoothnessMultiply;
    float _Metallic, _Metallic2, _Metallic3, _Metallic4, _Glossiness, _Glossiness2, _Glossiness3, _Glossiness4;
#if defined(HS2_HOOH_BLEND)
    float _HS2FogModes;
#endif
CBUFFER_END

struct HS2HoohAttributes { float4 pos : POSITION; float3 n : NORMAL; float4 t : TANGENT; float2 uv : TEXCOORD0; };
struct HS2HoohVaryings
{
    float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1;
    float3 n : TEXCOORD2; float4 t : TEXCOORD3; float3 b : TEXCOORD4;
};

HS2HoohVaryings HS2HoohVert(HS2HoohAttributes a)
{
    HS2HoohVaryings o;
    VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
    VertexNormalInputs nn = GetVertexNormalInputs(a.n, a.t);
    o.pos = p.positionCS; o.wpos = p.positionWS;
    o.n = nn.normalWS; o.t = float4(nn.tangentWS, a.t.w * GetOddNegativeScale());
    o.b = cross(o.n, o.t.xyz) * o.t.w;   // Built-in surface shaders build the binormal per vertex
    o.uv = a.uv;
    return o;
}

// Built-in UnpackScaleNormal: x = R * A, xy scaled before z is rebuilt.
half3 HS2HoohUnpackNormal(half4 packed, half scale)
{
    packed.x *= packed.w;
    half3 n;
    n.xy = (packed.xy * 2 - 1) * scale;
    n.z = sqrt(1 - saturate(dot(n.xy, n.xy)));
    return n;
}

// Alpha of the blended variant and the cutout tests shared by the colour, shadow and depth passes.
half HS2HoohAlpha(float2 uv)
{
    half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(uv, _MainTex)).a * _BaseColor.a;
#if defined(HS2_HOOH_BLEND)
    clip(SAMPLE_TEXTURE2D(_CutoutMap, sampler_CutoutMap, TRANSFORM_TEX(uv, _CutoutMap)).r - _Cutoff);
#elif defined(HS2_HOOH_CULLOFF)
    clip(a - _Cutoff);
#endif
    return a;
}

struct HS2HoohSurface { half3 albedo; half metallic, smoothness, occlusion; float3 normalWS; };

HS2HoohSurface HS2HoohSurf(HS2HoohVaryings i)
{
    HS2HoohSurface s;
    float2 uv = i.uv;
    s.albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(uv, _MainTex)).rgb * _BaseColor.rgb;
#if defined(HS2_HOOH_STANDARD)
    s.metallic = _Metallic * SAMPLE_TEXTURE2D(_MetalicGlossMap, sampler_MetalicGlossMap, TRANSFORM_TEX(uv, _MetalicGlossMap)).r;
    s.smoothness = _Glossiness;
#else
    half4 dmt = SAMPLE_TEXTURE2D(_DetailMainTex, sampler_DetailMainTex, TRANSFORM_TEX(uv, _DetailMainTex));
#if defined(HS2_HOOH_CULLOFF) && !defined(HS2_HOOH_NODMT1)
    half4 dmt1 = SAMPLE_TEXTURE2D(_DetailMainTex1, sampler_DetailMainTex1, TRANSFORM_TEX(uv, _DetailMainTex1));
    s.metallic = dmt1.r * dmt.r;
    s.smoothness = dmt1.a * dmt.b;
#elif defined(HS2_HOOH_GMM)
    // Measured (the ClotheReplicaOpaque copies with _GlossMetalMap; its default black and _SmoothnessMultiply 0 give the
    // plain rule): metal saturate(DMT.R + GMM.R), smoothness saturate(DMT.B + GMM.A x SmoothnessMultiply).
    half4 gmm = SAMPLE_TEXTURE2D(_GlossMetalMap, sampler_GlossMetalMap, TRANSFORM_TEX(uv, _GlossMetalMap));
    s.metallic = saturate(dmt.r + gmm.r);
    s.smoothness = saturate(dmt.b + gmm.a * _SmoothnessMultiply);
#else
    s.metallic = dmt.r;
    s.smoothness = dmt.b;
#endif
#endif
    s.occlusion = lerp(1, SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, TRANSFORM_TEX(uv, _OcclusionMap)).r, _OcclusionStrength);
    half3 n0 = HS2HoohUnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, TRANSFORM_TEX(uv, _BumpMap)), _BumpScale);
    half3 n1 = HS2HoohUnpackNormal(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, TRANSFORM_TEX(uv, _DetailNormalMap)), _DetailNormalMapScale);
    half3 n2 = HS2HoohUnpackNormal(SAMPLE_TEXTURE2D(_DetailNormalMap2, sampler_DetailNormalMap2, TRANSFORM_TEX(uv, _DetailNormalMap2)), _DetailNormalMapScale2);
    half3 nTS = normalize(half3(n0.xy + n1.xy, n0.z * n1.z));
    nTS = normalize(half3(nTS.xy + n2.xy, nTS.z * n2.z));
    // Built-in surface-shader tangent frame; back faces keep the front normal (measured on the Cull Off variants).
    s.normalWS = normalize(i.t.xyz * nTS.x + i.b * nTS.y + i.n * nTS.z);
    return s;
}

half4 HS2HoohFrag(HS2HoohVaryings i) : SV_Target
{
    half alpha = HS2HoohAlpha(i.uv);
    HS2HoohSurface hs = HS2HoohSurf(i);
#if defined(HS2_HOOH_BLEND)
    HS2ShadowReceive = 0;   // Built-in forward rendering: no realtime shadows in the transparent queues
#endif
    HS2Surface s = HS2SurfaceMetallic(hs.albedo, hs.metallic, hs.smoothness, hs.occlusion, hs.normalWS);
    half3 c = HS2Shade(s, i.wpos, GetNormalizedScreenSpaceUV(i.pos));
#if defined(HS2_HOOH_BLEND)
    return half4(HS2ApplyFog(c, i.wpos, _HS2FogModes), alpha);
#else
    return half4(c, 1);
#endif
}

float3 _LightDirection;
float3 _LightPosition;

HS2HoohVaryings HS2HoohShadowVert(HS2HoohAttributes a)
{
    HS2HoohVaryings o = HS2HoohVert(a);
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

half4 HS2HoohDepthFrag(HS2HoohVaryings i) : SV_Target
{
    HS2HoohAlpha(i.uv);
    return 0;
}
#endif
