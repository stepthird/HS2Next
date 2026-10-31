// The mosaic of the genital-area parts (HS2Next/Mosaic, method (a)). Two modes, chosen per member by
// the mosaic unit (Runtime/Chara/Mosaic) through _MosaicMode:
//   0 procedural  HS2's look of its own mosaic: opaque, unlit, screen-aligned square cells of _MosaicCellPx pixels (the
//                 grid fixed to the screen, anchored at its centre), each cell one skin shade h in [0, 1) from a hash of
//                 the cell (HS2Next's own: the PCG hash), colour (1 - 0.36 W, 1 + W (-0.776 + 0.136 h),
//                 1 + W (-0.808 + 0.068 h)), W = _WhiteOff. The cell size is set per camera through a property block
//                 (at least the camera's HS2 cell and the floor); without one (parts the unit does not track) it is the
//                 HS2 cell of the camera (the view height / (2 d), d = the distance from the camera to the object's
//                 origin) or the camera's floor, whichever is larger. The cell is never below the floor of the render
//                 target itself, max(4, ceil(L / 100)) pixels (L its long side), whatever a property block or the global
//                 _HS2MosaicFloorPx holds (the global can only raise it: MosaicCamera writes it per camera, k times the
//                 output's floor for a capture rendered k times larger).
//   1 texture     the member draws _MainTex at block-quantized texture coordinates: every block of _MosaicBlockUv (a power
//                 of two) on a UV-aligned grid shows the mip level whose texel is the block, sampled at the block's centre
//                 (the block's mean colour), times _Color; gloss and metal from _DetailMainTex sampled the same way
//                 (B, R) at least _Gloss / _Metallic; lit by the skin's BRDF (HS2NextBRDF.hlsl) with the member's own
//                 smooth vertex normal, no normal or detail map (no fine detail returns through the lighting). The
//                 block is set per camera through a property block (the default keeps a material without one coarse).
// The global _HS2MosaicUnlit > 0.5 (set only by the Editor tests' block measurements) drops the lighting of the texture
// mode: the same block-quantized sample, unlit. It changes neither the block nor the coverage.
#ifndef HS2NEXT_MOSAIC_INCLUDED
#define HS2NEXT_MOSAIC_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "HS2NextMosaicDepth.hlsl"

TEXTURE2D(_MainTex);        SAMPLER(sampler_MainTex);
TEXTURE2D(_DetailMainTex);  SAMPLER(sampler_DetailMainTex);

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST, _MainTex_TexelSize, _DetailMainTex_ST, _DetailMainTex_TexelSize;
    float4 _Color;
    float _WhiteOff, _MosaicMode, _MosaicBlockUv, _MosaicCellPx, _Gloss, _Metallic;
CBUFFER_END

// Per camera (Runtime/Chara/Mosaic/MosaicCamera.cs): the floor max(4 px, ceil(L / 100)) in render pixels.
float _HS2MosaicFloorPx;
// Editor tests (see the header): unlit, still block-quantized.
float _HS2MosaicUnlit;

// PCG hash (Jarzynski and Olano, "Hash Functions for GPU Rendering", 2020).
uint HS2MosaicPcg(uint v)
{
    uint s = v * 747796405u + 2891336453u;
    uint w = ((s >> ((s >> 28u) + 4u)) ^ s) * 277803737u;
    return (w >> 22u) ^ w;
}

// A shade in [0, 1) per cell.
float HS2MosaicShade(int2 cell)
{
    uint2 c = (uint2)(cell + int2(1 << 20, 1 << 20));
    return (HS2MosaicPcg(c.x + HS2MosaicPcg(c.y)) >> 8) * (1.0 / 16777216.0);
}

// The floor of the render target: max(4, ceil(L / 100)) pixels, or the camera step's (larger) value.
float HS2MosaicFloorPx()
{
    float own = max(4.0, ceil(max(_ScreenParams.x, _ScreenParams.y) / 100.0));
    return max(own, _HS2MosaicFloorPx);
}

// The procedural cell size in render pixels: the property block's, else the camera's HS2 cell; never below the floor.
float HS2MosaicCellPx()
{
    float floorPx = HS2MosaicFloorPx();
    if (_MosaicCellPx > 0) return max(_MosaicCellPx, floorPx);
    float4x4 m = UNITY_MATRIX_M;
    float3 origin = float3(m._m03, m._m13, m._m23);
    float d = max(distance(_WorldSpaceCameraPos.xyz, origin), 1e-3);
    return max(_ScreenParams.y / (2.0 * d), floorPx);
}

half3 HS2MosaicProcedural(float2 pixel)
{
    float cellPx = max(HS2MosaicCellPx(), 1.0);
    int2 cell = (int2)floor((pixel - 0.5 * _ScreenParams.xy) / cellPx);
    float h = HS2MosaicShade(cell);
    float w = _WhiteOff;
    return half3(1.0 - 0.36 * w, 1.0 + w * (-0.776 + 0.136 * h), 1.0 + w * (-0.808 + 0.068 * h));
}

// The block-quantized sample of a texture: the block's centre at the mip level whose texel is the block.
half4 HS2MosaicSample(TEXTURE2D_PARAM(tex, smp), float4 st, float4 texel, float2 uv, float block)
{
    float2 q = (floor(uv / block) + 0.5) * block;
    float2 suv = q * st.xy + st.zw;
    float size = max(texel.z, texel.w) * max(abs(st.x), abs(st.y));
    float lod = log2(max(block * size, 1.0));
    return SAMPLE_TEXTURE2D_LOD(tex, smp, suv, lod);
}

// The depth-only family of the member (DepthOnly; MotionVectors and DepthNormals through HS2NextPasses.hlsl): the member's
// depth placement (HS2NextMosaicDepth.hlsl), no clip.
struct HS2MosaicSA { float4 pos : POSITION; float3 n : NORMAL; };
struct HS2MosaicSV { float4 pos : SV_POSITION; };
HS2MosaicSV HS2MosaicDepthVert(HS2MosaicSA a)
{
    HS2MosaicSV o;
    o.pos = TransformWorldToHClip(HS2MosaicPositionWS(a.pos.xyz, a.n));
    return o;
}
half4 HS2MosaicDepthFrag(HS2MosaicSV i) : SV_Target { return 0; }

#if defined(HS2_MOSAIC_FORWARD)
#include "HS2NextBRDF.hlsl"

struct HS2MosaicA { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
struct HS2MosaicV { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1; float3 n : TEXCOORD2; };

HS2MosaicV HS2MosaicVert(HS2MosaicA a)
{
    HS2MosaicV o;
    o.wpos = HS2MosaicPositionWS(a.pos.xyz, a.n);
    o.pos = TransformWorldToHClip(o.wpos);
    o.n = TransformObjectToWorldNormal(a.n);
    o.uv = a.uv;
    return o;
}

half4 HS2MosaicFrag(HS2MosaicV i, bool front : SV_IsFrontFace) : SV_Target
{
    if (_MosaicMode < 0.5) return half4(HS2MosaicProcedural(i.pos.xy), 1);
    float block = clamp(_MosaicBlockUv, 1.0 / 4096.0, 1.0);
    half4 tex = HS2MosaicSample(TEXTURE2D_ARGS(_MainTex, sampler_MainTex), _MainTex_ST, _MainTex_TexelSize, i.uv, block);
    half3 albedo = tex.rgb * _Color.rgb;
    if (_HS2MosaicUnlit > 0.5) return half4(albedo, 1);
    half4 det = HS2MosaicSample(TEXTURE2D_ARGS(_DetailMainTex, sampler_DetailMainTex), _DetailMainTex_ST, _DetailMainTex_TexelSize, i.uv, block);
    half smoothness = saturate(max(_Gloss, det.b));
    half metallic = saturate(max(_Metallic, det.r));
    float3 n = normalize(i.n) * (front ? 1.0 : -1.0);
    HS2Surface s = HS2SurfaceMetallic(albedo, metallic, smoothness, 1, n);
    return half4(HS2Shade(s, i.wpos, GetNormalizedScreenSpaceUV(i.pos)), 1);
}
#endif
#endif
