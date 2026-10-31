// MotionVectors and DepthNormals passes of the fitted opaque / cutout shaders (E13;
// Phase 3.3 package S). URP's TAA needs per-object motion vectors (skinned characters move without moving their
// transform; without the pass URP only has the camera motion for them), and URP features that request the depth-normals
// prepass (SSAO with the DepthNormals source, later SSR) need a DepthNormals pass, else the object is missing from the
// depth and normals textures. Both passes reuse the family's DepthOnly vertex and fragment functions, so they clip exactly
// like the depth pass (cutouts, the hair's screen door) and keep the shader's UnityPerMaterial buffer (SRP batcher).
//
// A pass copies the defines / includes of the shader's DepthOnly pass, then defines and includes this file:
//   HS2_PASS_A        the depth pass's vertex input struct          HS2_PASS_V      its varyings struct
//   HS2_PASS_VERT     the depth vertex function (A → V)              HS2_PASS_CLIP   the depth fragment function (V → half4)
//   HS2_PASS_POS(a)   the object-space position of an A (float3)     HS2_PASS_NORMAL(a)  its object-space normal (optional:
//                                                                                     without it the normal comes from the
//                                                                                     screen derivatives of the position)
//   HS2_PASS_SMOOTHNESS(v)  optional: the surface smoothness (0..1) of a V; the DepthNormals pass writes it into the normals
//                     target's alpha when the pass declares _WRITE_SMOOTHNESS and the pipeline asks for it (screen-space
//                     reflection receivers), else the alpha stays 0 (no reflection)
// or HS2_PASS_SIMPLE (no family functions: position and normal only, no clip; for shaders whose depth pass draws the bare
// mesh). Entry points: #pragma vertex HS2MotionVert / fragment HS2MotionFrag (LightMode MotionVectors, ColorMask RG) and
// #pragma vertex HS2DepthNormalsVert / fragment HS2DepthNormalsFrag (LightMode DepthNormals).
// Motion vectors as URP's ObjectMotionVectors.hlsl: the jittered position for the raster, the non-jittered current and
// previous clip positions (previous object position from TEXCOORD4 when Unity provides it, i.e. skinned meshes with
// skinnedMotionVectors) → CalcNdcMotionVectorFromCsPositions. Normals as URP's DepthNormalsPass (world normal, octahedral
// packing under _GBUFFER_NORMALS_OCT).
#ifndef HS2NEXT_PASSES_INCLUDED
#define HS2NEXT_PASSES_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MotionVectorsCommon.hlsl"
#if defined(_GBUFFER_NORMALS_OCT)
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#endif

#if defined(HS2_PASS_SIMPLE)
struct HS2PassSimpleA { float4 pos : POSITION; float3 n : NORMAL; };
struct HS2PassSimpleV { float4 pos : SV_POSITION; };
HS2PassSimpleV HS2PassSimpleVert(HS2PassSimpleA a) { HS2PassSimpleV o; o.pos = TransformObjectToHClip(a.pos.xyz); return o; }
half4 HS2PassSimpleClip(HS2PassSimpleV i) { return 0; }
    #define HS2_PASS_A HS2PassSimpleA
    #define HS2_PASS_V HS2PassSimpleV
    #define HS2_PASS_VERT HS2PassSimpleVert
    #define HS2_PASS_CLIP HS2PassSimpleClip
    #define HS2_PASS_POS(a) (a).pos.xyz
    #define HS2_PASS_NORMAL(a) (a).n
#endif

// ---------------------------------------------------------------- MotionVectors

struct HS2MotionV
{
    HS2_PASS_V base;
    float4 hs2Cur  : TEXCOORD8;
    float4 hs2Prev : TEXCOORD9;
};

HS2MotionV HS2MotionVert(HS2_PASS_A a, float3 positionOld : TEXCOORD4)
{
    HS2MotionV o;
    o.base = HS2_PASS_VERT(a);
    float4 p = float4(HS2_PASS_POS(a), 1.0);
    o.hs2Cur = mul(_NonJitteredViewProjMatrix, mul(UNITY_MATRIX_M, p));
    float4 prev = unity_MotionVectorsParams.x == 1 ? float4(positionOld, 1.0) : p;
    o.hs2Prev = mul(_PrevViewProjMatrix, mul(UNITY_PREV_MATRIX_M, prev));
    return o;
}

float4 HS2MotionFrag(HS2MotionV i) : SV_Target
{
    HS2_PASS_CLIP(i.base);
    return float4(CalcNdcMotionVectorFromCsPositions(i.hs2Cur, i.hs2Prev), 0, 0);
}

// ---------------------------------------------------------------- DepthNormals

struct HS2DepthNormalsV
{
    HS2_PASS_V base;
    float3 hs2N  : TEXCOORD8;
    float3 hs2Ws : TEXCOORD9;
};

HS2DepthNormalsV HS2DepthNormalsVert(HS2_PASS_A a)
{
    HS2DepthNormalsV o;
    o.base = HS2_PASS_VERT(a);
#if defined(HS2_PASS_NORMAL)
    o.hs2N = TransformObjectToWorldNormal(HS2_PASS_NORMAL(a));
#else
    o.hs2N = float3(0, 0, 0);
#endif
    o.hs2Ws = TransformObjectToWorld(HS2_PASS_POS(a));
    return o;
}

half4 HS2DepthNormalsFrag(HS2DepthNormalsV i, bool front : SV_IsFrontFace) : SV_Target
{
    HS2_PASS_CLIP(i.base);
#if defined(_WRITE_SMOOTHNESS) && defined(HS2_PASS_SMOOTHNESS)
    half smoothness = saturate(HS2_PASS_SMOOTHNESS(i.base));
#else
    half smoothness = 0.0;
#endif
#if defined(HS2_PASS_NORMAL)
    float3 n = normalize(i.hs2N) * (front ? 1.0 : -1.0);
#else
    float3 n = normalize(cross(ddy(i.hs2Ws), ddx(i.hs2Ws)));
    if (dot(n, GetWorldSpaceViewDir(i.hs2Ws)) < 0) n = -n;   // face the camera
#endif
#if defined(_GBUFFER_NORMALS_OCT)
    float2 oct = PackNormalOctQuadEncode(n);
    return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), smoothness);
#else
    return half4(NormalizeNormalPerPixel(n), smoothness);
#endif
}
#endif
