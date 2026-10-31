// Shared surface for HS2's AIT clothes and accessory shaders (Clothes True / Clothes / Clothes Alpha True / Clothes
// Alpha True BackCull / Clothes Alpha / Item / Item Meka studio), evaluated with the Built-in BRDF1 of HS2NextBRDF.hlsl.
// Behaviour was inferred by black-box probing only (Editor/Fit/SkinFit.cs; case files Tests/FitCases/item_*.txt,
// ct_*.txt, cat_*.txt, bc_*.txt, ca_*.txt, meka*.txt, clothes_kw.txt, uvs_*.txt, trans_back.txt).
//
// Variant switches (define before including):
//   HS2_CLOTHES_ITEM         colour-mask regions: _Color/_Color2/_Color3/_Color4 with _Metallic*/_Glossiness*
//   HS2_CLOTHES_TRUE         colour baked into _MainTex, metal (R) / smoothness (B) from the composited _DetailMainTex
//                            (with its tiling / offset); wet smoothness max(gloss, 0.6 ExGloss) unless blended;
//                            translucency 0.4 x on back faces unless blended
//   HS2_CLOTHES_BLEND        alpha-blended output (Clothes Alpha*), no cutoff, no main-light shadows received; the scene's
//                            height-fog term of HS2NextFog.hlsl at the end, gated per material by _HS2FogModes (Phase 5.R)
//   HS2_CLOTHES_MEKA         Item Meka studio: pattern UV transforms, _EffectMap emission mask, _alpha cutout,
//                            emission ramp; no translucency / transmission / tearing (properties absent)
//   HS2_CLOTHES_STUDIO       Item studio (with HS2_CLOTHES_ITEM): Meka's pattern UV transforms and _alpha cutout,
//                            AIT/Item's translucency and transmission, no tearing (_AlphaEx absent); measured differences
//                            from AIT/Item: emission lerp(albedo, EmissionColor.rgb, EmissionColor.a) * strength * G
//                            (without _EMISSIONCOLORTEXBASE_ON), BackCull's unlit _BitangentRim rim, and with
//                            _DITHERSWITCH_ON clip(min(step(bayer4, MainTex.a), _alpha) - _Cutoff)
//   HS2_CLOTHES_FRESNEL_MULT colour times a _FresnelMultiple view term plus a _BitangentRim rim (BackCull)
//   HS2_CLOTHES_CALPHA       Clothes Alpha: _AlphaMask (min with _alpha_a / _alpha_b channels, uv0 / uv1),
//                            tiled pattern UVs (_patternuv*), emission view falloff (_EmissionRim)
// Material keywords (multi_compile_local in every variant): _DITHERSWITCH_ON (MainTex alpha becomes the 4x4
// ordered-dither coverage, _Cutoff unused) and _EMISSIONCOLORTEXBASE_ON (emission = albedo * strength * G). Bundle
// copies of the originals compile them or not; ShaderMap.Swap drops keywords the original's copy does not declare.
//
// Measured (flat probe values exact to the 4th decimal; see the case files):
//   albedo     = saturate(MainTex.rgb * BaseColor.rgb * region * (1 - 0.2 ExGloss) + CarvatureStrength * Occlusion.R)
//                (maps on uv0; texture tiling / offset (_ST) is ignored except _DetailMainTex's)
//   region     = lerp(lerp(lerp(c1, c2, M.r), c3, M.g), Color4, M.b) with M = _ColorMask;
//                c_k = lerp(Color_k, Color_k_2, p * (1 - PatternMask_k.r)), p = saturate((M.a - 0.9) * 10).
//                Item / Clothes sample the pattern masks at a degenerate constant UV (sampled here at (1, 1)).
//   metallic   = region metallic (same chain) * MetallicGloss.R * detail factors
//   smoothness = max(region gloss * MetallicGloss.A * detail factors, ExGloss)
//   occlusion  = saturate(Occlusion.G + 1 - OcculusionStrength) * detail occlusion (ambient only)
//   emission   = EmissionColor * EmissionStrength * MetallicGloss.G
//   detail k   (mask DetailMask.r / .g on uv0; t = rotate(uv0 * UVScalePattern.xy * DetailUV.xy + DetailUV.zw,
//              -pi * rotator) about 0.5):
//                metal *= saturate(D.r + 1 - DetailMetallicScale * mask)
//                gloss *= saturate(D.b + 1 - DetailGlossScale * mask)
//                occ   *= max(1 - DetailOcculusionScale * mask, saturate(D.g / 0.75))
//                normal: height D.g, forward differences of 0.0027 in t space (same level of detail as the centre
//                        sample), n = normalize(-2 s dHx, -2 s dHy, 1), s = DetailNormalMapScale * mask (texture space,
//                        not rotated back), blended into the normal map like BlendNormals (whiteout)
//   normal map: Built-in UnpackScaleNormal(_BumpMap, _Float0) in the Built-in surface-shader tangent frame
//              (interpolated vertex tangent / binormal / normal, normalized after the transform); back faces keep
//              the front normal (Cull Off, no flip)
//   translucency (Amplify): strength _Translucency * MetallicGloss.B, light L + N * distortion (not normalized),
//                pow(saturate(V . -light), scattering) * direct + ambient SH * ambient, shadowed by TransShadow
//   transmission: albedo * TransmissionStrength * 0.25 * MetallicGloss.B * saturate(-NdotL) * shadowed light
//   inner-clothes masks (Clothes True family, uv1): alpha = min(alpha, AlphaMask.r if _alpha_a, AlphaMask.g if
//                _alpha_b, AlphaMask2.r if _alpha_d); _alpha_c has no effect
//   tearing (AlphaEx = 1 - breakRate < 1): alpha *= (Occlusion.B^(12 (1 - AlphaEx)) - 0.03) / 0.97
//   wetness coverage: ExGloss > 0 makes Occlusion.A a 4x4 ordered-dither coverage,
//                     alpha = lerp(1, Occlusion.A, saturate(2 ExGloss)), pixel kept when alpha >= (bayer + 1) / 16
//   _FresnelSetting / _BitangentRim (except BackCull), _YabureDitherSwitch, _VertexColor, _VertexColorMask and the
//   _DitherSwitch / _EmissionColorTexBase floats themselves have no visible effect.
#ifndef HS2NEXT_CLOTHES_INCLUDED
#define HS2NEXT_CLOTHES_INCLUDED
#include "HS2NextBRDF.hlsl"
#if defined(HS2_CLOTHES_BLEND)
#include "HS2NextFog.hlsl"
#endif

TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_DetailMask); SAMPLER(sampler_DetailMask);
TEXTURE2D(_DetailGlossMap); SAMPLER(sampler_DetailGlossMap);
TEXTURE2D(_DetailGlossMap2); SAMPLER(sampler_DetailGlossMap2);
#if defined(HS2_CLOTHES_ITEM)
TEXTURE2D(_ColorMask); SAMPLER(sampler_ColorMask);
TEXTURE2D(_PatternMask1); SAMPLER(sampler_PatternMask1);
TEXTURE2D(_PatternMask2); SAMPLER(sampler_PatternMask2);
TEXTURE2D(_PatternMask3); SAMPLER(sampler_PatternMask3);
#endif
#if defined(HS2_CLOTHES_TRUE)
TEXTURE2D(_DetailMainTex); SAMPLER(sampler_DetailMainTex);
#endif
#if defined(HS2_CLOTHES_MEKA)
TEXTURE2D(_EffectMap); SAMPLER(sampler_EffectMap);
#endif
#if defined(HS2_CLOTHES_CALPHA) || defined(HS2_CLOTHES_TRUE)
TEXTURE2D(_AlphaMask); SAMPLER(sampler_AlphaMask);
#endif
#if defined(HS2_CLOTHES_TRUE)
TEXTURE2D(_AlphaMask2); SAMPLER(sampler_AlphaMask2);
#endif

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST, _BaseColor, _DetailUV, _DetailUV2, _EmissionColor;
    float _Float0, _OcculusionStrength, _DetailOcculusionScale, _DetailOcculusionScale2;
    float _DetailGlossScale, _DetailGlossScale2, _DetailMetallicScale, _DetailMetallicScale2;
    float _DetailNormalMapScale, _DetailNormalMapScale2, _DetailUVRotator, _DetailUV2Rotator;
    float _ExGloss, _CarvatureStrength, _EmissionStrength, _Cutoff, _AlphaEx, _TransmissionStrength;
    float _Translucency, _TransNormalDistortion, _TransScattering, _TransDirect, _TransAmbient, _TransShadow;
    float4 _Color, _Color1_2, _Color2, _Color2_2, _Color3, _Color3_2, _Color4;
    float _Metallic, _Metallic2, _Metallic3, _Metallic4, _Glossiness, _Glossiness2, _Glossiness3, _Glossiness4;
    float4 _UVScalePattern, _UVScroll, _patternuv1, _patternuv2, _patternuv3;
    float _patternuv1Rotator, _patternuv2Rotator, _patternuv3Rotator, _patternclamp1, _patternclamp2, _patternclamp3;
    float _UVScrollRotator, _alpha;
    float4 _DetailMainTex_ST, _FresnelMultiple, _FresnelSetting;
    float _BitangentRim, _EmissionRim, _AlphaMaskUVset, _alpha_a, _alpha_b, _alpha_d;
#if defined(HS2_CLOTHES_BLEND)
    float _HS2FogModes;
#endif
CBUFFER_END

struct HS2ClothesAttributes { float4 pos : POSITION; float3 n : NORMAL; float4 t : TANGENT; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; };
struct HS2ClothesVaryings
{
    float4 pos : SV_POSITION; float4 uv : TEXCOORD0; float3 wpos : TEXCOORD1;   // uv: xy = uv0, zw = uv1
    float3 n : TEXCOORD2; float4 t : TEXCOORD3; float3 b : TEXCOORD4;
};

HS2ClothesVaryings HS2ClothesVert(HS2ClothesAttributes a)
{
    HS2ClothesVaryings o;
    VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
    VertexNormalInputs nn = GetVertexNormalInputs(a.n, a.t);
    o.pos = p.positionCS; o.wpos = p.positionWS;
    o.n = nn.normalWS; o.t = float4(nn.tangentWS, a.t.w * GetOddNegativeScale());
    o.b = cross(o.n, o.t.xyz) * o.t.w;   // Built-in surface shaders build the binormal per vertex
    o.uv = float4(a.uv, a.uv1);
    return o;
}

// Built-in UnpackScaleNormal: xy scaled before z is rebuilt (URP's UnpackNormalScale rebuilds first).
half3 HS2ClothesUnpackNormal(half4 packed, half scale)
{
    packed.x *= packed.w;
    half3 n;
    n.xy = (packed.xy * 2 - 1) * scale;
    n.z = sqrt(1 - saturate(dot(n.xy, n.xy)));
    return n;
}

// Detail UV: tiling + offset, then rotation by -pi * rotator about (0.5, 0.5).
float2 HS2DetailUV(float2 uv, float4 st, float rotator)
{
    float2 t = uv * st.xy + st.zw - 0.5;
    float s, c; sincos(-PI * rotator, s, c);
    return float2(c * t.x - s * t.y, s * t.x + c * t.y) + 0.5;
}

float2 HS2Rotate(float2 t, float rotator)
{
    t -= 0.5;
    float s, c; sincos(-PI * rotator, s, c);
    return float2(c * t.x - s * t.y, s * t.x + c * t.y) + 0.5;
}

#if defined(HS2_CLOTHES_MEKA) || defined(HS2_CLOTHES_STUDIO)
// Meka pattern UV: t = uv * UVScalePattern.xy (patterns 1 and 2 only), then (t + p.zw - 0.5) * p.xy + 0.5, rotation
// by -pi * rotator about 0.5, optional clamp to [0, 1].
float2 HS2PatternUV(float2 t, float4 p, float rotator, float clampOn)
{
    t = HS2Rotate((t + p.zw - 0.5) * p.xy + 0.5, rotator);
    return clampOn > 0.5 ? saturate(t) : t;
}
#endif

// 4x4 ordered dither threshold (k + 1) / 16 at this pixel; y counted from the bottom like Built-in's screen position.
float HS2Bayer4(float4 positionCS)
{
    uint2 p = (uint2)positionCS.xy;
    uint y = p.y;
#if UNITY_UV_STARTS_AT_TOP
    if (_ProjectionParams.x > 0) y = (uint)(_ScreenParams.y - 1 - positionCS.y + 0.5);
#endif
    const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    return (m[(y & 3) * 4 + (p.x & 3)] + 1) / 16.0;
}

struct HS2ClothesSurface
{
    half3 albedo; half metallic, smoothness, occlusion, alpha; half3 emission; float3 normalWS;
    half transMask;
};

// Alpha / coverage tests shared by the colour, shadow and depth passes.
half HS2ClothesAlpha(float4 uv01, float4 positionCS)
{
    float2 uv = uv01.xy;
    half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a;
#if defined(HS2_CLOTHES_CALPHA)
    // Measured: alpha = min(MainTex.a, AlphaMask.r if _alpha_a, AlphaMask.g if _alpha_b) on uv0 or uv1
    // (_AlphaMaskUVset); _alpha_c has no effect.
    half4 am = SAMPLE_TEXTURE2D(_AlphaMask, sampler_AlphaMask, _AlphaMaskUVset > 0.5 ? uv01.zw : uv01.xy);
    a = min(a, min(_alpha_a > 0.5 ? am.r : 1, _alpha_b > 0.5 ? am.g : 1));
#elif defined(HS2_CLOTHES_TRUE)
    // Measured: inner-clothes masks on uv1: _alpha_a -> AlphaMask.r, _alpha_b -> AlphaMask.g, _alpha_d -> AlphaMask2.r
    // (combined here with min; _alpha_c has no effect).
    half4 am = SAMPLE_TEXTURE2D(_AlphaMask, sampler_AlphaMask, uv01.zw);
    half am2 = SAMPLE_TEXTURE2D(_AlphaMask2, sampler_AlphaMask2, uv01.zw).r;
    a = min(a, min(min(_alpha_a > 0.5 ? am.r : 1, _alpha_b > 0.5 ? am.g : 1), _alpha_d > 0.5 ? am2 : 1));
#endif
#if defined(HS2_CLOTHES_MEKA) || defined(HS2_CLOTHES_STUDIO)
    a = min(a, _alpha);
#else
    // Tearing (AlphaEx = 1 - breakRate): alpha *= (B^(12 (1 - AlphaEx)) - 0.03) / 0.97 with B = Occlusion.B
    // (exact on a 14 x 8 grid of the blended variant; AlphaEx = 1 disables it).
    if (_AlphaEx < 1)
    {
        float b = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv).b;
        a *= ((b > 0 ? exp2(12 * (1 - _AlphaEx) * log2(b)) : 0) - 0.03) / 0.97;
    }
#endif
#if defined(_DITHERSWITCH_ON) && defined(HS2_CLOTHES_STUDIO)
    // Measured (Item studio): MainTex.a is the dither coverage, _alpha and _Cutoff act on the dithered 0 / 1 value.
    clip(min(step(HS2Bayer4(positionCS), SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a), _alpha) - _Cutoff);
#elif defined(_DITHERSWITCH_ON)
    clip(a - HS2Bayer4(positionCS));   // measured: the alpha becomes the 4x4 dither coverage, _Cutoff unused
#elif !defined(HS2_CLOTHES_BLEND)
    clip(a - _Cutoff);
#endif
    if (_ExGloss > 0)
    {
        half wet = lerp(1, SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv).a, saturate(2 * _ExGloss));
        clip(wet - HS2Bayer4(positionCS));
    }
    return a;
}

HS2ClothesSurface HS2ClothesSurf(HS2ClothesVaryings i, bool front)
{
    HS2ClothesSurface s;
    float2 uv = i.uv.xy;
    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
    half4 oc = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv);
    half4 mg = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv);
    half4 dm = SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, uv);
    // Detail maps on uv0 * _UVScalePattern.xy (zw unused), then the detail tiling / offset and rotation.
    float2 duv = uv * _UVScalePattern.xy;
    float2 t1 = HS2DetailUV(duv, _DetailUV, _DetailUVRotator), t2 = HS2DetailUV(duv, _DetailUV2, _DetailUV2Rotator);
    half4 d1 = SAMPLE_TEXTURE2D(_DetailGlossMap, sampler_DetailGlossMap, t1);
    half4 d2 = SAMPLE_TEXTURE2D(_DetailGlossMap2, sampler_DetailGlossMap2, t2);

    half3 col = 1; half metal = 0, gloss = 0;
#if defined(HS2_CLOTHES_ITEM)
    half4 cm = SAMPLE_TEXTURE2D(_ColorMask, sampler_ColorMask, uv);
    half p = saturate((cm.a - 0.9) * 10);
#if defined(HS2_CLOTHES_MEKA) || defined(HS2_CLOTHES_STUDIO)
    float2 tp = uv * _UVScalePattern.xy;
    half pm1 = SAMPLE_TEXTURE2D(_PatternMask1, sampler_PatternMask1, HS2PatternUV(tp, _patternuv1, _patternuv1Rotator, _patternclamp1)).r;
    half pm2 = SAMPLE_TEXTURE2D(_PatternMask2, sampler_PatternMask2, HS2PatternUV(tp, _patternuv2, _patternuv2Rotator, _patternclamp2)).r;
    half pm3 = SAMPLE_TEXTURE2D(_PatternMask3, sampler_PatternMask3, HS2PatternUV(uv, _patternuv3, _patternuv3Rotator, _patternclamp3)).r;
#elif defined(HS2_CLOTHES_CALPHA)
    // Measured: pattern UV = rotate(uv * UVScalePattern.xy * p.xy + p.zw, -pi * rotator) for all three patterns.
    float2 tp = uv * _UVScalePattern.xy;
    half pm1 = SAMPLE_TEXTURE2D(_PatternMask1, sampler_PatternMask1, HS2Rotate(tp * _patternuv1.xy + _patternuv1.zw, _patternuv1Rotator)).r;
    half pm2 = SAMPLE_TEXTURE2D(_PatternMask2, sampler_PatternMask2, HS2Rotate(tp * _patternuv2.xy + _patternuv2.zw, _patternuv2Rotator)).r;
    half pm3 = SAMPLE_TEXTURE2D(_PatternMask3, sampler_PatternMask3, HS2Rotate(tp * _patternuv3.xy + _patternuv3.zw, _patternuv3Rotator)).r;
#else
    half pm1 = SAMPLE_TEXTURE2D(_PatternMask1, sampler_PatternMask1, float2(1, 1)).r;
    half pm2 = SAMPLE_TEXTURE2D(_PatternMask2, sampler_PatternMask2, float2(1, 1)).r;
    half pm3 = SAMPLE_TEXTURE2D(_PatternMask3, sampler_PatternMask3, float2(1, 1)).r;
#endif
    half3 c1 = lerp(_Color.rgb, _Color1_2.rgb, p * (1 - pm1));
    half3 c2 = lerp(_Color2.rgb, _Color2_2.rgb, p * (1 - pm2));
    half3 c3 = lerp(_Color3.rgb, _Color3_2.rgb, p * (1 - pm3));
    col = lerp(lerp(lerp(c1, c2, cm.r), c3, cm.g), _Color4.rgb, cm.b);
    metal = lerp(lerp(lerp(_Metallic, _Metallic2, cm.r), _Metallic3, cm.g), _Metallic4, cm.b);
    gloss = lerp(lerp(lerp(_Glossiness, _Glossiness2, cm.r), _Glossiness3, cm.g), _Glossiness4, cm.b);
#elif defined(HS2_CLOTHES_TRUE)
    // Colour is baked into _MainTex; the composited gloss texture holds metallic (R) and smoothness (B).
    half4 dmt = SAMPLE_TEXTURE2D(_DetailMainTex, sampler_DetailMainTex, TRANSFORM_TEX(uv, _DetailMainTex));
    metal = dmt.r; gloss = dmt.b;
#endif
    s.albedo = saturate(tex.rgb * _BaseColor.rgb * col * (1 - 0.2 * _ExGloss) + _CarvatureStrength * oc.r);
    s.alpha = tex.a;

    // The detail mask scales the strengths: scale * mask.
    half metalF = saturate(d1.r + 1 - _DetailMetallicScale * dm.r) * saturate(d2.r + 1 - _DetailMetallicScale2 * dm.g);
    half glossF = saturate(d1.b + 1 - _DetailGlossScale * dm.r) * saturate(d2.b + 1 - _DetailGlossScale2 * dm.g);
    half occF = max(1 - _DetailOcculusionScale * dm.r, saturate(d1.g / 0.75)) * max(1 - _DetailOcculusionScale2 * dm.g, saturate(d2.g / 0.75));
    s.metallic = metal * mg.r * metalF;
#if defined(HS2_CLOTHES_TRUE) && !defined(HS2_CLOTHES_BLEND)
    s.smoothness = max(gloss * mg.a * glossF, 0.6 * _ExGloss);   // measured: wet smoothness 0.6 * ExGloss here
#else
    s.smoothness = max(gloss * mg.a * glossF, _ExGloss);
#endif
    s.occlusion = saturate(oc.g + 1 - _OcculusionStrength) * occF;
#if defined(HS2_CLOTHES_MEKA)
    // Measured: x = strength * G; emission = EffectMap.R * saturate(x) * (lerp(albedo, colour, colour.a) * x
    //                                                                      + 0.1 * smoothness * occlusion);
    // the effect map uses (uv - 0.5) * UVScroll.xy + 0.5, rotated; UVScroll.zw has no effect.
    float2 te = HS2Rotate((uv - 0.5) * _UVScroll.xy + 0.5, _UVScrollRotator);
    half fx = SAMPLE_TEXTURE2D(_EffectMap, sampler_EffectMap, te).r;
    half x = _EmissionStrength * mg.g;
    s.emission = fx * saturate(x) * (lerp(s.albedo, _EmissionColor.rgb, _EmissionColor.a) * x + 0.1 * s.smoothness * s.occlusion);
#elif defined(_EMISSIONCOLORTEXBASE_ON)
    s.emission = s.albedo * _EmissionStrength * mg.g;   // measured: albedo replaces the emission colour
#elif defined(HS2_CLOTHES_STUDIO)
    // Measured (Item studio): the emission colour's alpha blends from the albedo to the colour.
    s.emission = lerp(s.albedo, _EmissionColor.rgb, _EmissionColor.a) * _EmissionStrength * mg.g;
#else
    s.emission = _EmissionColor.rgb * _EmissionStrength * mg.g;
#endif
#if defined(HS2_CLOTHES_CALPHA)
    // Measured: emission *= lerp(1, 1 - (1 - N.V)^1.5, _EmissionRim) with the geometric (vertex) normal.
    half nvg = saturate(dot(normalize(i.n), GetWorldSpaceNormalizeViewDir(i.wpos)));
    s.emission *= lerp(1, 1 - pow(1 - nvg, 1.5), _EmissionRim);
#endif
#if defined(HS2_CLOTHES_MEKA)
    s.transMask = 0;   // no translucency / transmission properties
#else
    s.transMask = mg.b;
#endif

    // Normal map plus the two detail height maps (forward differences in detail texture space).
    const float kDelta = 0.0027;
    half h1 = SAMPLE_TEXTURE2D(_DetailGlossMap, sampler_DetailGlossMap, t1).g;
    half h2 = SAMPLE_TEXTURE2D(_DetailGlossMap2, sampler_DetailGlossMap2, t2).g;
    half h1x = SAMPLE_TEXTURE2D(_DetailGlossMap, sampler_DetailGlossMap, t1 + float2(kDelta, 0)).g;
    half h1y = SAMPLE_TEXTURE2D(_DetailGlossMap, sampler_DetailGlossMap, t1 + float2(0, kDelta)).g;
    half h2x = SAMPLE_TEXTURE2D(_DetailGlossMap2, sampler_DetailGlossMap2, t2 + float2(kDelta, 0)).g;
    half h2y = SAMPLE_TEXTURE2D(_DetailGlossMap2, sampler_DetailGlossMap2, t2 + float2(0, kDelta)).g;
    half s1 = 2 * _DetailNormalMapScale * dm.r, s2 = 2 * _DetailNormalMapScale2 * dm.g;
    half3 nd1 = normalize(half3(-s1 * (h1x - h1), -s1 * (h1y - h1), 1));
    half3 nd2 = normalize(half3(-s2 * (h2x - h2), -s2 * (h2y - h2), 1));
    half3 nTS = HS2ClothesUnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), _Float0);
    nTS = normalize(half3(nTS.xy + nd1.xy, nTS.z * nd1.z));
    nTS = normalize(half3(nTS.xy + nd2.xy, nTS.z * nd2.z));
    // Built-in surface-shader tangent frame: the interpolated (not renormalized) vertex tangent, binormal and normal,
    // normalized after the transform. Back faces keep the front normal (measured: Cull Off, no flip).
    s.normalWS = normalize(i.t.xyz * nTS.x + i.b * nTS.y + i.n * nTS.z);
#if defined(HS2_CLOTHES_STUDIO)
    // Measured (Item studio): BackCull's unlit rim, BitangentRim * max(0, (S.x + S.y * (1 - N.V)^S.z) * S.w) * smoothness
    // * occlusion (negative terms clamped, unlike BackCull; back faces keep the front normal). Gap: with a bumped normal
    // the original's term changes with the main light for normals tilted towards +x (Tests/FitCases/ist_decode3.txt
    // rbumpv); not reproduced.
    {
        half nvr = dot(s.normalWS, GetWorldSpaceNormalizeViewDir(i.wpos));
        s.emission += _BitangentRim * max(0, (_FresnelSetting.x + _FresnelSetting.y * pow(max(1 - nvr, 0), _FresnelSetting.z)) * _FresnelSetting.w)
                    * s.smoothness * s.occlusion;
    }
#endif
#if defined(HS2_CLOTHES_FRESNEL_MULT)
    // Measured (BackCull): colour *= lerp(1, saturate(F.x + F.y * (1 - N.V)^F.z), F.w), F = _FresnelMultiple.
    half nv = dot(s.normalWS, GetWorldSpaceNormalizeViewDir(i.wpos));   // not saturated (measured on the rim)
    s.albedo *= lerp(1, saturate(_FresnelMultiple.x + _FresnelMultiple.y * pow(max(1 - nv, 0), _FresnelMultiple.z)), _FresnelMultiple.w);
    // Measured: an unlit rim, BitangentRim * (S.x + S.y * (1 - N.V)^S.z) * S.w * smoothness * occlusion, S = _FresnelSetting
    // (not saturated).
    s.emission += _BitangentRim * (_FresnelSetting.x + _FresnelSetting.y * pow(max(1 - nv, 0), _FresnelSetting.z)) * _FresnelSetting.w
                * s.smoothness * s.occlusion;
#endif
    return s;
}

half4 HS2ClothesFrag(HS2ClothesVaryings i, bool front : SV_IsFrontFace) : SV_Target
{
    half alpha = HS2ClothesAlpha(i.uv, i.pos);
    HS2ClothesSurface cs = HS2ClothesSurf(i, front);
    HS2Surface s = HS2SurfaceMetallic(cs.albedo, cs.metallic, cs.smoothness, cs.occlusion, cs.normalWS);
    s.emission = cs.emission;
    s.transStrength = _Translucency * cs.transMask; s.transDirect = _TransDirect; s.transAmbient = _TransAmbient;
#if defined(HS2_CLOTHES_TRUE) && !defined(HS2_CLOTHES_BLEND)
    if (!front) s.transStrength *= 0.4;   // measured: Clothes True's translucency on back faces is 0.4 x the front's
#endif
    s.transScattering = _TransScattering; s.transDistortion = _TransNormalDistortion; s.transShadow = _TransShadow;
    s.transmission = cs.albedo * (_TransmissionStrength * 0.25 * cs.transMask);
    half3 c = HS2Shade(s, i.wpos, GetNormalizedScreenSpaceUV(i.pos));
#if defined(HS2_CLOTHES_BLEND)
    return half4(HS2ApplyFog(c, i.wpos, _HS2FogModes), alpha);
#else
    return half4(c, 1);
#endif
}

float3 _LightDirection;
float3 _LightPosition;

HS2ClothesVaryings HS2ClothesShadowVert(HS2ClothesAttributes a)
{
    HS2ClothesVaryings o = HS2ClothesVert(a);
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

half4 HS2ClothesDepthFrag(HS2ClothesVaryings i) : SV_Target
{
    half a = HS2ClothesAlpha(i.uv, i.pos);
#if defined(HS2_CLOTHES_BLEND)
    clip(a - 0.5);   // blended variants: shadows from the half-opaque parts (approximation)
#endif
    return 0;
}
#endif
