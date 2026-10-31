// Surface of the "RMA" mod shaders (Phase 3.7 package F3), evaluated with the Built-in BRDF1 of HS2NextBRDF.hlsl:
// "Custom/SurfaceShader" and "RMAShaderOpaque" / "RMAShaderCutout" / "RMAShaderCutoutAccessory" / "RMAShaderDither" /
// "RMAShaderOpaqueCullOff" (Amplify surface shaders of hooh's prop packs with a packed roughness / metallic / occlusion
// texture _RMA). Behaviour inferred by black-box probing only (Editor/Fit/SkinFit.cs; Tests/FitCases/f3_rma_*.txt).
//
// Variant switches (define before including):
//   HS2_RMA_CUSTOM     Custom/SurfaceShader: world-space triplanar maps, detail albedo / normal
//   HS2_RMA_CUTOUT     clip(MainTex.a - _Cutoff) (RMAShaderCutout, RMAShaderCutoutAccessory)
//   HS2_RMA_DITHER     RMAShaderDither: blended SrcAlpha / OneMinusSrcAlpha with alpha MainTex.a x _alpha (no dither despite
//                      the name; Transparent queue: no realtime shadows received)
//   HS2_RMA_CULLOFF    RMAShaderOpaqueCullOff (emission texture _Emission, no _EmissionStrength, other channel roles)
//
// Measured (flat probe values exact to the 4th decimal; Tests/FitCases/f3_rma*_decode*.txt, f3_cus_decode*.txt):
//   RMAShader*   albedo MainTex.rgb x Color.rgb; smoothness saturate((1 - RMA.R) x _Glossiness); metallic
//                saturate(RMA.G x _Metallic); occlusion RMA.B (ambient only); RMA.A unused; emission EmissionTex.rgb x
//                EmissionColor.rgb x EmissionStrength x 10; normal Built-in UnpackNormal (R x A, G) of _NormalMap, no scale;
//                UVs uv0 x _texcoord_ST then each texture's own tiling / offset; _Color.a has no effect (cutout and
//                blending read MainTex.a alone); the defaults "white" of _NormalMap / _EmissionTex (Cutout, CullOff) are
//                the originals' (a white normal map tilts the normal away from every light: reproduced as is)
//   CullOff      smoothness saturate(RMA.G x _Metallic), metallic saturate(1 - RMA.R x _Glossiness), occlusion RMA.B,
//                emission _Emission.rgb x EmissionColor.rgb; Cull Off with the front normal on back faces
//   Custom       triplanar in world space (weights |N| / sum of the interpolated normal, planes zy / xz / xy signed by N,
//                x _TextureScale.xy or _DetailScale.xy; zw, the mesh UVs and the textures' tiling unused); albedo
//                lerp(MainTex x Color, 1, DetailTex) (screen); smoothness / metallic / occlusion as RMAShader*; normals:
//                per plane the unpacked xy x scale added to the surface normal's in-plane components (the map's z unused),
//                main and detail combined with Unity's BlendNormals in the surface tangent frame
#ifndef HS2NEXT_RMA_INCLUDED
#define HS2NEXT_RMA_INCLUDED
#include "HS2NextBRDF.hlsl"
#if defined(HS2_RMA_DITHER)
#include "HS2NextFog.hlsl"
#endif

TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
TEXTURE2D(_RMA); SAMPLER(sampler_RMA);
TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
TEXTURE2D(_EmissionTex); SAMPLER(sampler_EmissionTex);
TEXTURE2D(_Emission); SAMPLER(sampler_Emission);
TEXTURE2D(_DetailTex); SAMPLER(sampler_DetailTex);
TEXTURE2D(_DetailNormalMap); SAMPLER(sampler_DetailNormalMap);

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST, _RMA_ST, _NormalMap_ST, _EmissionTex_ST, _Emission_ST, _DetailTex_ST, _DetailNormalMap_ST, _texcoord_ST;
    float4 _Color, _EmissionColor, _TextureScale, _DetailScale;
    float _Metallic, _Glossiness, _EmissionStrength, _Cutoff, _alpha, _NormalScale, _DetailNormalScale, __dirty;
#if defined(HS2_RMA_DITHER)
    float _HS2FogModes;
#endif
CBUFFER_END

// Fit switches (globals, debug only; 0 = the fitted values).
float _HS2FitK, _HS2FitK2;

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

// Built-in UnpackScaleNormal: x = R * A (DXT5nm and plain RGB alike), xy scaled before z is rebuilt.
half3 HS2RmaUnpackNormal(half4 packed, half scale)
{
    packed.x *= packed.w;
    half3 n;
    n.xy = (packed.xy * 2 - 1) * scale;
    n.z = sqrt(1 - saturate(dot(n.xy, n.xy)));
    return n;
}

// Amplify's uv_texcoord (_texcoord_ST), then each texture's own tiling / offset (measured).
float2 HS2RmaUV(float2 uv) { return uv * _texcoord_ST.xy + _texcoord_ST.zw; }

// Coverage: MainTex.a alone (_Color.a has no effect, measured).
half HS2RmaAlpha(float2 uv)
{
    return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(HS2RmaUV(uv), _MainTex)).a;
}

#if defined(HS2_RMA_CUSTOM)
// Amplify's world-space triplanar sampling (measured: world position x _TextureScale.xy (zw unused), weights |N| / sum
// (falloff 1) of the interpolated vertex normal, planes zy x (sign N.x, 1), xz x (sign N.y, 1), xy x (-sign N.z, 1); the
// mesh UVs and the textures' tiling / offset are not used).
struct HS2Tri { float3 w, sg; float2 uvX, uvY, uvZ; };
HS2Tri HS2RmaTri(float3 wpos, float3 n, float2 scale)
{
    HS2Tri t;
    float3 an = abs(n);
    t.w = an / (an.x + an.y + an.z);
    t.sg = sign(n);
    t.uvX = wpos.zy * float2(t.sg.x, 1) * scale;
    t.uvY = wpos.xz * float2(t.sg.y, 1) * scale;
    t.uvZ = wpos.xy * float2(-t.sg.z, 1) * scale;
    return t;
}
#define HS2_TRI_SAMPLE(tex, t) (SAMPLE_TEXTURE2D(tex, sampler##tex, (t).uvX) * (t).w.x + SAMPLE_TEXTURE2D(tex, sampler##tex, (t).uvY) * (t).w.y + SAMPLE_TEXTURE2D(tex, sampler##tex, (t).uvZ) * (t).w.z)

// Triplanar normal map (world space): per plane the unpacked xy (signed like the UVs) added to the surface normal's two
// in-plane components, the surface normal's third component kept (the map's z is not used), blended by the weights.
float3 HS2RmaTriNormal(TEXTURE2D_PARAM(tex, smp), HS2Tri t, float3 n, half scale)
{
    half2 x = HS2RmaUnpackNormal(SAMPLE_TEXTURE2D(tex, smp, t.uvX), scale).xy * float2(t.sg.x, 1);
    half2 y = HS2RmaUnpackNormal(SAMPLE_TEXTURE2D(tex, smp, t.uvY), scale).xy * float2(t.sg.y, 1);
    half2 z = HS2RmaUnpackNormal(SAMPLE_TEXTURE2D(tex, smp, t.uvZ), scale).xy * float2(-t.sg.z, 1);
    float3 nx = float3(x + n.zy, n.x).zyx, ny = float3(y + n.xz, n.y).xzy, nz = float3(z + n.xy, n.z);
    return normalize(nx * t.w.x + ny * t.w.y + nz * t.w.z);
}
#endif

struct HS2RmaSurface { half3 albedo; half metallic, smoothness, occlusion; half3 emission; float3 normalWS; half alpha; };

HS2RmaSurface HS2RmaSurf(HS2RmaVaryings i)
{
    HS2RmaSurface s;
#if defined(HS2_RMA_CUSTOM)
    float3 n = normalize(i.n);
    HS2Tri tm = HS2RmaTri(i.wpos, n, _TextureScale.xy), td = HS2RmaTri(i.wpos, n, _DetailScale.xy);
    half4 tex = HS2_TRI_SAMPLE(_MainTex, tm);
    half4 rma = HS2_TRI_SAMPLE(_RMA, tm);
    half3 det = HS2_TRI_SAMPLE(_DetailTex, td).rgb;
    // detail albedo: screen blend lerp(albedo, 1, detail) (measured)
    s.albedo = lerp(tex.rgb * _Color.rgb, 1, det);
    s.alpha = 1;
    s.smoothness = saturate((1 - rma.r) * _Glossiness);
    s.metallic = saturate(rma.g * _Metallic);
    s.occlusion = rma.b;
    s.emission = 0;
    float3 nm = HS2RmaTriNormal(TEXTURE2D_ARGS(_NormalMap, sampler_NormalMap), tm, n, _NormalScale);
    float3 nd = HS2RmaTriNormal(TEXTURE2D_ARGS(_DetailNormalMap, sampler_DetailNormalMap), td, n, _DetailNormalScale);
    // both world normals to the surface-shader tangent frame, Unity's BlendNormals there, and back
    float3 t = i.t.xyz, b = i.b;
    float3 am = float3(dot(nm, t), dot(nm, b), dot(nm, i.n)), ad = float3(dot(nd, t), dot(nd, b), dot(nd, i.n));
    float3 nTS = _HS2FitK == 1 ? normalize(am + ad - float3(0, 0, 1)) : normalize(float3(am.xy + ad.xy, am.z * ad.z));
    s.normalWS = normalize(t * nTS.x + b * nTS.y + i.n * nTS.z);
#else
    float2 uvm = HS2RmaUV(i.uv);
    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(uvm, _MainTex));
    half4 rma = SAMPLE_TEXTURE2D(_RMA, sampler_RMA, TRANSFORM_TEX(uvm, _RMA));
    s.albedo = tex.rgb * _Color.rgb;
    s.alpha = tex.a;
#if defined(HS2_RMA_CULLOFF)
    // RMAShaderOpaqueCullOff (measured): smoothness from G x _Metallic, metallic 1 - R x _Glossiness
    s.smoothness = saturate(rma.g * _Metallic);
    s.metallic = saturate(1 - rma.r * _Glossiness);
    s.emission = SAMPLE_TEXTURE2D(_Emission, sampler_Emission, TRANSFORM_TEX(uvm, _Emission)).rgb * _EmissionColor.rgb;
#else
    s.smoothness = saturate((1 - rma.r) * _Glossiness);
    s.metallic = saturate(rma.g * _Metallic);
    // measured: EmissionTex x EmissionColor x EmissionStrength x 10
    s.emission = SAMPLE_TEXTURE2D(_EmissionTex, sampler_EmissionTex, TRANSFORM_TEX(uvm, _EmissionTex)).rgb * _EmissionColor.rgb * _EmissionStrength * 10;
#endif
    s.occlusion = rma.b;
    half3 nTS = HS2RmaUnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, TRANSFORM_TEX(uvm, _NormalMap)), 1);
    // Built-in surface-shader tangent frame: interpolated vertex tangent / binormal / normal, normalized after the transform.
    s.normalWS = normalize(i.t.xyz * nTS.x + i.b * nTS.y + i.n * nTS.z);
#endif
    return s;
}

half4 HS2RmaFrag(HS2RmaVaryings i) : SV_Target
{
    HS2RmaSurface hs = HS2RmaSurf(i);
#if defined(HS2_RMA_CUTOUT)
    clip(hs.alpha - _Cutoff);
#endif
    HS2Surface s = HS2SurfaceMetallic(hs.albedo, hs.metallic, hs.smoothness, hs.occlusion, hs.normalWS);
    s.emission = hs.emission;
#if defined(HS2_RMA_DITHER)
    HS2ShadowReceive = 0;   // Transparent queue: Built-in forward applies no realtime shadows
    return half4(HS2ApplyFog(HS2Shade(s, i.wpos, GetNormalizedScreenSpaceUV(i.pos)), i.wpos, _HS2FogModes), hs.alpha * _alpha);
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
#if defined(HS2_RMA_CUTOUT)
    clip(HS2RmaAlpha(i.uv) - _Cutoff);
#endif
    return 0;
}
#endif
