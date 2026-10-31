// Shared surface for HS2's Studio item shaders "AIT/main standard2 studio" (opaque, cutout / dither) and
// "AIT/main standard2 studio alpha" (alpha blended), evaluated with the Built-in BRDF1 of HS2NextBRDF.hlsl.
// Behaviour was inferred by black-box probing only (Editor/Fit/SkinFit.cs; case files Tests/FitCases/ms2_*.txt,
// ms2a_*.txt; the full regression sets ms2_fit.txt / ms2a_fit.txt).
//
// HS2_MS2_OLD: the older "AIT/main standard2" (room parts): lerp-chain regions, one metallic / glossiness, pattern UV
// uv0 * q.xy + q.zw without rotation / clamp, _Color3_3, alpha min(MainTex.a, _AlphaEx, _Color.a * weighted colour alphas),
// emission by
// the real keywords _EMISSIONMAINCOLORBASE_ON (albedo * colour * map.r) / _EMISSION_ON (colour * map.rgb).
// Variant switch (define before including): HS2_MS2_ALPHA  alpha-blended output (SrcAlpha / OneMinusSrcAlpha, ZWrite On:
// measured on overlapping real geometry, e.g. the snowflake and veil items), the region colour's alpha joins the alpha,
// no dither, no main-light shadows received, and the scene's height-fog term of HS2NextFog.hlsl at the end of the forward
// pass, gated per material by _HS2FogModes (Phase 5.R).
//
// Measured (flat probe values exact to the 4th decimal; see the case files):
//   UVs        main maps (_MainTex, _ColorMask, _BumpMap, _MetallicGlossMap, _OcclusionMap, _EmissonMap, _DetailMask) on
//              uvM = uv0 * _UV.xy + _UV.zw; detail maps (_DetailAlbedoMap, _DetailNormalMap) on uvD = uv0 * _UVSec.xy +
//              _UVSec.zw; the textures' own tiling / offset (_ST) is ignored; uv1 and vertex colours are unused
//   parallax   height _OcclusionMap.a at uvM, Unity's ParallaxOffset(h, _Parallax, tangent-space view) added to uvM and
//              uvD (not to the pattern UVs)
//   patterns   p_k = _PatternMask_k.r at rotate((uv0 + q.zw - 0.5) * q.xy + 0.5, -pi * rotator) about 0.5, q =
//              _patternuv_k, saturated when _patternclamp_k (uv0 only); c_k.rgb = lerp(Color_k, Color_k_2,
//              saturate(Color_k_2.a - p_k)) (the pattern colour's alpha is its opacity; patterns leave the alpha alone)
//   regions    M = _ColorMask, S = M.r + M.g + M.b, w1 = saturate(1 - S); colour = max(c1 w1, c2 M.r, c3 M.g, Color4 M.b)
//              per channel (a "lighten" blend, not a lerp chain); metallic / glossiness are the weighted sums
//              m1 w1 + m2 M.r + m3 M.g + m4 M.b; region alpha (alpha variant) = saturate(saturate(Color.a - S)
//              + Color2.a M.r + Color3.a M.g + Color4.a M.b)
//   albedo     lerp(MainTex.rgb * colour, DetailAlbedo.rgb, _DetailAlbedoMapScale * DetailMask.r) (a lerp towards the
//              detail colour, not a multiply)
//   metallic   region metallic * MetallicGloss.r; smoothness = region glossiness * MetallicGloss.a
//   occlusion  saturate(Occlusion.r + 1 - _OcclusionStrength) (indirect light only)
//   emission   lerp(_EmissionColor.rgb, albedo, _EmissionMainColorBase) * _EmissionStrength * Emisson.r
//   normal     Built-in UnpackScaleNormal(_BumpMap, _Float1), detail UnpackScaleNormal(_DetailNormalMap,
//              _DetailNormalMapScale * DetailMask.r), whiteout blend; _DetailAlbedoNormalScale has no effect
//   alpha      a = min(MainTex.a, _alpha) (alpha variant: min(MainTex.a, colour.a, _alpha)); opaque: _DITHERSWITCH_ON
//              keeps a pixel when step(bayer4, a) >= _Cutoff, else clip(a - _Cutoff); alpha variant: clip(a - _Cutoff)
//   faces      back faces culled (both variants)
//   _EmissionColor is [HDR] in the originals (not gamma-converted, values above 1 kept); the replacements declare it so.
#ifndef HS2NEXT_STUDIO_STANDARD_INCLUDED
#define HS2NEXT_STUDIO_STANDARD_INCLUDED
#include "HS2NextBRDF.hlsl"
#if defined(HS2_MS2_ALPHA)
#include "HS2NextFog.hlsl"
#endif

TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_EmissonMap); SAMPLER(sampler_EmissonMap);
TEXTURE2D(_DetailMask); SAMPLER(sampler_DetailMask);
TEXTURE2D(_ColorMask); SAMPLER(sampler_ColorMask);
TEXTURE2D(_DetailNormalMap); SAMPLER(sampler_DetailNormalMap);
TEXTURE2D(_DetailAlbedoMap); SAMPLER(sampler_DetailAlbedoMap);
TEXTURE2D(_PatternMask1); SAMPLER(sampler_PatternMask1);
TEXTURE2D(_PatternMask2); SAMPLER(sampler_PatternMask2);
TEXTURE2D(_PatternMask3); SAMPLER(sampler_PatternMask3);

CBUFFER_START(UnityPerMaterial)
    float4 _Color, _Color1_2, _Color2, _Color2_2, _Color3, _Color3_2, _Color4, _UV, _UVSec, _EmissionColor;
    float4 _patternuv1, _patternuv2, _patternuv3;
    float _Cutoff, _alpha, _Metallic, _Metallic2, _Metallic3, _Metallic4, _Glossiness, _Glossiness2, _Glossiness3, _Glossiness4;
    float _Float1, _Parallax, _OcclusionStrength, _EmissionStrength, _DetailAlbedoMapScale, _DetailNormalMapScale;
    float _patternuv1Rotator, _patternuv2Rotator, _patternuv3Rotator, _patternclamp1, _patternclamp2, _patternclamp3;
    float _EmissionMainColorBase;
    float4 _Color3_3;   // HS2_MS2_OLD: the old shader's third pattern colour
    float _AlphaEx;     // HS2_MS2_OLD: alpha factor
#if defined(HS2_MS2_ALPHA)
    float _HS2FogModes; // the fog modes of the original (set when the material is switched)
#endif
CBUFFER_END

struct HS2MS2Attributes { float4 pos : POSITION; float3 n : NORMAL; float4 t : TANGENT; float2 uv : TEXCOORD0; };
struct HS2MS2Varyings
{
    float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1;
    float3 n : TEXCOORD2; float4 t : TEXCOORD3; float3 b : TEXCOORD4;
};

HS2MS2Varyings HS2MS2Vert(HS2MS2Attributes a)
{
    HS2MS2Varyings o;
    VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
    VertexNormalInputs nn = GetVertexNormalInputs(a.n, a.t);
    o.pos = p.positionCS; o.wpos = p.positionWS;
    o.n = nn.normalWS; o.t = float4(nn.tangentWS, a.t.w * GetOddNegativeScale());
    o.b = cross(o.n, o.t.xyz) * o.t.w;   // Built-in surface shaders build the binormal per vertex
    o.uv = a.uv;
    return o;
}

// Built-in UnpackScaleNormal: xy scaled before z is rebuilt (URP's UnpackNormalScale rebuilds first).
half3 HS2MS2UnpackNormal(half4 packed, half scale)
{
    packed.x *= packed.w;
    half3 n;
    n.xy = (packed.xy * 2 - 1) * scale;
    n.z = sqrt(1 - saturate(dot(n.xy, n.xy)));
    return n;
}

// Pattern UV: (uv + q.zw - 0.5) * q.xy + 0.5, rotated by -pi * rotator about 0.5, optionally saturated.
float2 HS2MS2PatternUV(float2 uv, float4 q, float rotator, float clampOn)
{
    float2 t = (uv + q.zw - 0.5) * q.xy;
    float s, c; sincos(-PI * rotator, s, c);
    t = float2(c * t.x - s * t.y, s * t.x + c * t.y) + 0.5;
    return clampOn > 0.5 ? saturate(t) : t;
}

// 4x4 ordered dither threshold (k + 1) / 16 at this pixel; y counted from the bottom like Built-in's screen position.
float HS2MS2Bayer4(float4 positionCS)
{
    uint2 p = (uint2)positionCS.xy;
    uint y = p.y;
#if UNITY_UV_STARTS_AT_TOP
    if (_ProjectionParams.x > 0) y = (uint)(_ScreenParams.y - 1 - positionCS.y + 0.5);
#endif
    const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    return (m[(y & 3) * 4 + (p.x & 3)] + 1) / 16.0;
}

// Unity's ParallaxOffset with the tangent-space view direction.
float2 HS2MS2Parallax(HS2MS2Varyings i, float2 uvM)
{
    if (_Parallax == 0) return 0;
    float3 v = GetWorldSpaceNormalizeViewDir(i.wpos);
    float3 vt = normalize(float3(dot(v, normalize(i.t.xyz)), dot(v, normalize(i.b)), dot(v, normalize(i.n))));
    half h = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uvM).a * _Parallax - _Parallax / 2;
    vt.z += 0.42;
    return h * (vt.xy / vt.z);
}

void HS2MS2Uvs(HS2MS2Varyings i, out float2 uvM, out float2 uvD)
{
    uvM = i.uv * _UV.xy + _UV.zw;
    uvD = i.uv * _UVSec.xy + _UVSec.zw;
    float2 off = HS2MS2Parallax(i, uvM);
    uvM += off; uvD += off;
}

// Region colour (rgba) from the colour mask and the three patterns.
half4 HS2MS2Region(float2 uv0, half4 m, out half metal, out half gloss)
{
#if defined(HS2_MS2_OLD)
    // Measured (AIT/main standard2): pattern UV uv0 * q.xy + q.zw (no rotation / clamp), a plain lerp chain R / G / B,
    // one metallic / glossiness for the whole surface, the third pattern colour named _Color3_3.
    half p1 = SAMPLE_TEXTURE2D(_PatternMask1, sampler_PatternMask1, uv0 * _patternuv1.xy + _patternuv1.zw).r;
    half p2 = SAMPLE_TEXTURE2D(_PatternMask2, sampler_PatternMask2, uv0 * _patternuv2.xy + _patternuv2.zw).r;
    half p3 = SAMPLE_TEXTURE2D(_PatternMask3, sampler_PatternMask3, uv0 * _patternuv3.xy + _patternuv3.zw).r;
    half4 c1 = half4(lerp(_Color.rgb, _Color1_2.rgb, saturate(_Color1_2.a - p1)), _Color.a);
    half4 c2 = half4(lerp(_Color2.rgb, _Color2_2.rgb, saturate(_Color2_2.a - p2)), _Color2.a);
    half4 c3 = half4(lerp(_Color3.rgb, _Color3_3.rgb, saturate(_Color3_3.a - p3)), _Color3.a);
    metal = _Metallic; gloss = _Glossiness;
    return lerp(lerp(lerp(c1, c2, m.r), c3, m.g), _Color4, m.b);
#else
    half p1 = SAMPLE_TEXTURE2D(_PatternMask1, sampler_PatternMask1, HS2MS2PatternUV(uv0, _patternuv1, _patternuv1Rotator, _patternclamp1)).r;
    half p2 = SAMPLE_TEXTURE2D(_PatternMask2, sampler_PatternMask2, HS2MS2PatternUV(uv0, _patternuv2, _patternuv2Rotator, _patternclamp2)).r;
    half p3 = SAMPLE_TEXTURE2D(_PatternMask3, sampler_PatternMask3, HS2MS2PatternUV(uv0, _patternuv3, _patternuv3Rotator, _patternclamp3)).r;
    half3 c1 = lerp(_Color.rgb, _Color1_2.rgb, saturate(_Color1_2.a - p1));
    half3 c2 = lerp(_Color2.rgb, _Color2_2.rgb, saturate(_Color2_2.a - p2));
    half3 c3 = lerp(_Color3.rgb, _Color3_2.rgb, saturate(_Color3_2.a - p3));
    half sum = m.r + m.g + m.b;
    half w1 = saturate(1 - sum);
    metal = _Metallic * w1 + _Metallic2 * m.r + _Metallic3 * m.g + _Metallic4 * m.b;
    gloss = _Glossiness * w1 + _Glossiness2 * m.r + _Glossiness3 * m.g + _Glossiness4 * m.b;
    half a = saturate(saturate(_Color.a - sum) + _Color2.a * m.r + _Color3.a * m.g + _Color4.a * m.b);
    return half4(max(max(c1 * w1, c2 * m.r), max(c3 * m.g, _Color4.rgb * m.b)), a);
#endif
}

half HS2MS2Alpha(HS2MS2Varyings i, float2 uvM)
{
#if defined(HS2_MS2_OLD)
    // Measured (AIT/main standard2): _AlphaEx instead of _alpha, and the colours' alphas join as
    // _Color.a * saturate(w1 + Color2.a R + Color3.a G + Color4.a B) (weighted sum, region 1 weight w1 = 1 - R - G - B).
    half4 m0 = SAMPLE_TEXTURE2D(_ColorMask, sampler_ColorMask, uvM);
    half ca = _Color.a * saturate(saturate(1 - m0.r - m0.g - m0.b) + _Color2.a * m0.r + _Color3.a * m0.g + _Color4.a * m0.b);
    half a = min(min(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvM).a, _AlphaEx), ca);
#else
    half a = min(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvM).a, _alpha);
#endif
#if defined(HS2_MS2_ALPHA)
    half metal, gloss;
    half4 m = SAMPLE_TEXTURE2D(_ColorMask, sampler_ColorMask, uvM);
    a = min(a, HS2MS2Region(i.uv, m, metal, gloss).a);
    clip(a - _Cutoff);
#elif defined(_DITHERSWITCH_ON)
    clip(step(HS2MS2Bayer4(i.pos), a) - _Cutoff);
#else
    clip(a - _Cutoff);
#endif
    return a;
}

half4 HS2MS2Frag(HS2MS2Varyings i) : SV_Target
{
    float2 uvM, uvD;
    HS2MS2Uvs(i, uvM, uvD);
    half alpha = HS2MS2Alpha(i, uvM);

    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvM);
    half4 m = SAMPLE_TEXTURE2D(_ColorMask, sampler_ColorMask, uvM);
    half metal, gloss;
    half4 region = HS2MS2Region(i.uv, m, metal, gloss);
    half dmask = SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, uvM).r;
    half3 detail = SAMPLE_TEXTURE2D(_DetailAlbedoMap, sampler_DetailAlbedoMap, uvD).rgb;
    half3 albedo = lerp(tex.rgb * region.rgb, detail, _DetailAlbedoMapScale * dmask);
    half4 mg = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uvM);
    half occ = saturate(SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uvM).r + 1 - _OcclusionStrength);

    half3 nTS = HS2MS2UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvM), _Float1);
    half3 nD = HS2MS2UnpackNormal(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, uvD), _DetailNormalMapScale * dmask);
    nTS = normalize(half3(nTS.xy + nD.xy, nTS.z * nD.z));
    // Built-in surface-shader tangent frame: the interpolated vertex tangent, binormal and normal, normalized after the
    // transform.
    float3 n = normalize(i.t.xyz * nTS.x + i.b * nTS.y + i.n * nTS.z);

    HS2Surface s = HS2SurfaceMetallic(albedo, metal * mg.r, gloss * mg.a, occ, n);
#if defined(HS2_MS2_OLD)
    // Measured (AIT/main standard2, real keywords): _EMISSIONMAINCOLORBASE_ON albedo * colour * map.r, else _EMISSION_ON
    // colour * map.rgb, else none; _EmissionPower has no effect.
    half4 em = SAMPLE_TEXTURE2D(_EmissonMap, sampler_EmissonMap, uvM);
    #if defined(_EMISSIONMAINCOLORBASE_ON)
    s.emission = albedo * _EmissionColor.rgb * em.r;
    #elif defined(_EMISSION_ON)
    s.emission = _EmissionColor.rgb * em.rgb;
    #endif
#else
    s.emission = lerp(_EmissionColor.rgb, albedo, _EmissionMainColorBase) * _EmissionStrength
               * SAMPLE_TEXTURE2D(_EmissonMap, sampler_EmissonMap, uvM).r;
#endif
#if defined(HS2_MS2_ALPHA)
    HS2ShadowReceive = 0;   // Built-in forward: no realtime shadows on transparent-queue materials
    return half4(HS2ApplyFog(HS2Shade(s, i.wpos, GetNormalizedScreenSpaceUV(i.pos)), i.wpos, _HS2FogModes), alpha);
#else
    return half4(HS2Shade(s, i.wpos, GetNormalizedScreenSpaceUV(i.pos)), 1);
#endif
}

float3 _LightDirection;
float3 _LightPosition;

HS2MS2Varyings HS2MS2ShadowVert(HS2MS2Attributes a)
{
    HS2MS2Varyings o = HS2MS2Vert(a);
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

#if defined(HS2_PASS_WANTS_SMOOTHNESS)
// The surface smoothness the forward pass uses (region glossiness x the metallic-gloss map's alpha; no parallax), for the
// DepthNormals pass's smoothness output (screen-space reflection receivers).
half HS2MS2Smoothness(HS2MS2Varyings i)
{
    float2 uvM = i.uv * _UV.xy + _UV.zw;
    half metal, gloss;
    HS2MS2Region(i.uv, SAMPLE_TEXTURE2D(_ColorMask, sampler_ColorMask, uvM), metal, gloss);
    return gloss * SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uvM).a;
}
#define HS2_PASS_SMOOTHNESS(v) HS2MS2Smoothness(v)
#endif

half4 HS2MS2DepthFrag(HS2MS2Varyings i) : SV_Target
{
    float2 uvM = i.uv * _UV.xy + _UV.zw;
    HS2MS2Alpha(i, uvM);
    return 0;
}
#endif
