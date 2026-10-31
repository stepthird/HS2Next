// Standard-like approximations of two large mod shaders (Phase 3.7 package F3, W8), evaluated with the Built-in BRDF1 of
// HS2NextBRDF.hlsl in Unity Standard's metallic model: "Mochie/Standard" (HS2_SL_MOCHIE) and "UBER - Metallic Setup/ Core" /
// "UBER - Metallic Setup/2 Sided/ Core" (HS2_SL_UBER). The shader files declare the originals' full property lists and the
// UnityPerMaterial buffer (generated from Tools/survey/mod_shader_props.json); this file reads the main maps only.
// Reproduced: (filled in from the probes). Not reproduced: triplanar / stochastic sampling, parallax occlusion, detail
// blend modes, subsurface, SSR, wetness / snow / glitter / translucency, decals, tessellation.
// Fog (Phase 5.R): the forward pass ends with the scene's height-fog term of HS2NextFog.hlsl, gated per material by
// _HS2FogModes (declared in each shader file's buffer; 0 for opaque and cutout materials).
#ifndef HS2NEXT_STDLIKE_INCLUDED
#define HS2NEXT_STDLIKE_INCLUDED
#include "HS2NextBRDF.hlsl"
#include "HS2NextFog.hlsl"

TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
#if defined(HS2_SL_MOCHIE)
TEXTURE2D(_SpecGlossMap); SAMPLER(sampler_SpecGlossMap);
TEXTURE2D(_EmissionMask); SAMPLER(sampler_EmissionMask);
#endif

struct HS2SLAttributes { float4 pos : POSITION; float3 n : NORMAL; float4 t : TANGENT; float2 uv0 : TEXCOORD0; float2 uv1 : TEXCOORD1; };
struct HS2SLVaryings
{
    float4 pos : SV_POSITION; float4 uv : TEXCOORD0; float3 wpos : TEXCOORD1;
    float3 n : TEXCOORD2; float4 t : TEXCOORD3; float3 b : TEXCOORD4;
};

HS2SLVaryings HS2SLVert(HS2SLAttributes a)
{
    HS2SLVaryings o;
    VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
    VertexNormalInputs nn = GetVertexNormalInputs(a.n, a.t);
    o.pos = p.positionCS; o.wpos = p.positionWS;
    o.n = nn.normalWS; o.t = float4(nn.tangentWS, a.t.w * GetOddNegativeScale());
    o.b = cross(o.n, o.t.xyz) * o.t.w;
    o.uv = float4(TRANSFORM_TEX(a.uv0, _MainTex), a.uv1);
    return o;
}

half3 HS2SLUnpackNormal(half4 packed, half scale)
{
    packed.x *= packed.w;
    half3 n;
    n.xy = (packed.xy * 2 - 1) * scale;
    n.z = sqrt(1 - saturate(dot(n.xy, n.xy)));
    return n;
}

#if defined(HS2_SL_FORWARD)

struct HS2SLSurface { half3 albedo; half metallic, smoothness, occlusion, alpha; half3 emission; float3 normalWS; };

HS2SLSurface HS2SLSurf(HS2SLVaryings i, bool front)
{
    HS2SLSurface s;
    float2 uv = i.uv.xy;
    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
    s.albedo = tex.rgb * _Color.rgb;
    s.alpha = tex.a * _Color.a;
    half3 nTS = half3(0, 0, 1);
#if defined(HS2_SL_MOCHIE)
    half lum = dot(s.albedo, half3(0.3, 0.59, 0.11));   // measured weights
    s.albedo = lerp(lum.xxx, s.albedo, _Saturation);
    // measured on the bundle copy: the Standard workflow whatever _Workflow says (the packed map is not read); the maps
    // are always multiplied by the sliders (_MetallicMult / _RoughnessMult have no effect)
    half metallic = _Metallic, rough = _Glossiness;
    if (_METALLICGLOSSMAP) metallic = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv).r * _Metallic;
    if (_SPECGLOSSMAP) rough = SAMPLE_TEXTURE2D(_SpecGlossMap, sampler_SpecGlossMap, uv).r * _Glossiness;
    half occ = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv).g;
    s.metallic = metallic;
    s.smoothness = 1 - rough;
    s.occlusion = lerp(1, occ, _OcclusionStrength);
    if (_NORMALMAP) nTS = HS2SLUnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), _BumpScale);
    s.emission = 0;
#else   // UBER
    half4 mg = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv);
    s.metallic = mg.r * _Metallic;
    s.smoothness = _Smoothness_from_albedo_alpha > 0.5 ? tex.a * _Glossiness : mg.a * _Glossiness;
    half occ = _Occlusion_from_albedo_alpha > 0.5 ? tex.a : SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv).g;
    s.occlusion = lerp(1, occ, _OcclusionStrength);
    nTS = HS2SLUnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), _BumpScale);
    s.emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uv).rgb * _EmissionColor.rgb;
#endif
    s.normalWS = normalize(i.t.xyz * nTS.x + i.b * nTS.y + i.n * nTS.z);
    return s;
}

half4 HS2SLFrag(HS2SLVaryings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
{
    HS2SLSurface hs = HS2SLSurf(i, IS_FRONT_VFACE(face, true, false));
    HS2Surface s = HS2SurfaceMetallic(hs.albedo, hs.metallic, hs.smoothness, hs.occlusion, hs.normalWS);
    s.emission = hs.emission;
#if defined(HS2_SL_MOCHIE)
    s.specularHighlights = _SpecularStrength;   // measured: scales the direct specular
#endif
    HS2ShadowReceive = _RECEIVE_SHADOWS_OFF ? 0 : 1;   // set by ShaderMap.Swap for transparent queues
    return half4(HS2ApplyFog(HS2Shade(s, i.wpos, GetNormalizedScreenSpaceUV(i.pos)), i.wpos, _HS2FogModes), 1);
}
#endif

float3 _LightDirection;
float3 _LightPosition;

HS2SLVaryings HS2SLShadowVert(HS2SLAttributes a)
{
    HS2SLVaryings o = HS2SLVert(a);
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

half4 HS2SLDepthFrag(HS2SLVaryings i) : SV_Target
{
    return 0;
}
#endif
