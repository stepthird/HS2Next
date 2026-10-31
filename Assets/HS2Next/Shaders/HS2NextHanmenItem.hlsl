// Surface of the mod shader "Hanmen/Item Cutoff" (Phase 3.7, package F1), evaluated with the Built-in BRDF1 of
// HS2NextBRDF.hlsl and the shared light loop of HS2NextLights.hlsl. Behaviour was inferred by black-box probing only
// (Editor/Fit/SkinFit.cs; case files Tests/FitCases/hmi_*.txt) against bundle copies of the original.
//
// The name is shared by several unrelated bundle copies (52 to 91 properties); this file reproduces the main copy (76
// properties, Cull [_CullMode], about 90 % of the materials); ShaderMap.CopyAccepts routes only that copy here.
//
// Measured (flat probe values exact to the 4th decimal unless noted; regression sets hmi_fit.txt / hmi_alpha_fit.txt):
//   regions    weights w = (1 - M.r - M.g - M.b, M.r, M.g, M.b) with M = _ColorMask (a weighted sum, not saturated:
//              overlapping mask channels give negative weights) for the colours, their alphas, metallic and glossiness
//   albedo     = MainTex.rgb * BaseColor.rgb * sum w_k Color_k.rgb * lerp(1, WetAlbedoOffset, ExGloss)
//   alpha      = MainTex.a * sum w_k Color_k.a; clip(alpha - Cutoff), or the 4x4 ordered dither with _DITHERSWITCH_ON
//   metallic   = sum w_k Metallic_k lerp(1, MetallicGloss.B, MetallicMask_k), times the detail factor
//   smoothness = sum w_k Glossiness_k lerp(1, MetallicGloss.R, Roughness_k), times the detail factor, then
//                lerp(s, 1, saturate(ExGloss * WetnessPower))
//   occlusion  = lerp(1, Occlusion.G, OcculusionStrength) * detail occlusion: on the SH, the reflection and the direct
//                specular (not the direct diffuse)
//   emission   = EmissionColor.rgb * EmissionStrength * MetallicGloss.G * MainTex.rgb (not the colours)
//   BRDF       Built-in BRDF1 with N.V = saturate(N.V) instead of abs (normals turned away from the view)
//   translucency (Amplify): albedo * (Translucency * pow(saturate(V . -(L + N d)), s) * TransDirect * light
//              + saturate(Translucency * TransAmbient * SH)); on by default (1, ambient 0.1), no texture masks it
//   transmission (_TRANSMISSION_ON): diffuse colour * TransmissionStrength * saturate(-N.L) * light
//   details    R channels: factor 1 - sum_k scale_k mask_k (1 - D_k.r) for metallic and glossiness, the occlusion with
//              smoothstep(0, 0.8, D_k.r); UV frac(rotate(uv * UVScalePattern.xy * DetailUV.xy, -pi rotator) about 0.5)
//              (repeats whatever the texture's wrap; the zw parts unused); height G as forward differences (approximate)
//   tearing    AlphaEx < 1 discards where Occlusion.B < min(0.91, 1.02 - 0.6 AlphaEx) (approximate, measured to 0.02)
//   back faces Cull [_CullMode]; _NormalBackDirInvert 1 flips the normal (0 differs, not reproduced; no material)
//   no effect  _RoughnessReflections, _GlossinessReflection, _CarvatureStrength, Occlusion.R / .A, MetallicGloss.A, the
//              textures' tiling; _SPECULARSETUP_ON / _IRIDISCENT_ON / _DITHERBLUENOISE_ON are compiled by the copy but
//              no material enables them (not reproduced)
// Item Alpha (HS2_HM_ALPHA): the same surface blended SrcAlpha / OneMinusSrcAlpha (colour mask RGB, ZWrite Off), no direct
// translucency, no _UVScalePattern, no realtime shadows received.
// Gen-2 copies (HS2_HM_GEN2: GDC/Item Cutoff and the 68-property Hanmen/Item Cutoff copy with the same property set;
// regression: the gen-2 and GDC "# fit:" lines of hmi_fit.txt): no specular occlusion; emission without MainTex; N.V = abs;
// translucency strength T * saturate(T) and its ambient part SH * TransAmbient lit by the main light (colour, distance and
// shadow); no tearing, no _UVScalePattern; an 8x8 ordered (Bayer) dither while _Cutoff > 0. The detail height keeps the
// main copy's approximate rule (measured closer than a coarser gradient).
// Pattern copy (HS2_HM_P91: 91 properties, Studio patterns; regression hmi_p91_fit.txt): regions as a lerp chain (colour k over the
// previous by mask channel k); each region's colour lerp(Color_k, Color_k_2, 1 - PatternMask_k.r) with the pattern UV
// frac(rotate(uv * UVScalePattern.xy * patternuv_k.xy)); MetallicGloss R = metal mask, G = emission, B = translucency
// mask, A = glossiness mask; emission (_EMISSIONCOLORTEXBASE_ON ? region colour : EmissionColor) * strength * MG.G *
// EffectMap.rgb (UV scrolled and rotated); no specular occlusion; translucency linear in T * MG.B with the ambient part lit
// by the main light; alpha clip(MainTex.a - Cutoff), then while _Cutoff > 0 a clip of alpha * region alpha * _alpha
// against interleaved gradient noise on the pixel position (exact on 3,072 pixels).
#ifndef HS2NEXT_HANMEN_ITEM_INCLUDED
#define HS2NEXT_HANMEN_ITEM_INCLUDED
#include "HS2NextBRDF.hlsl"
#if defined(HS2_HM_ALPHA)
#include "HS2NextFog.hlsl"
#endif

TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
TEXTURE2D(_ColorMask); SAMPLER(sampler_ColorMask);
TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_DetailMask); SAMPLER(sampler_DetailMask);
TEXTURE2D(_DetailGlossMap); SAMPLER(sampler_DetailGlossMap);
TEXTURE2D(_DetailGlossMap2); SAMPLER(sampler_DetailGlossMap2);
#if defined(HS2_HM_P91)
TEXTURE2D(_PatternMask1); SAMPLER(sampler_PatternMask1);
TEXTURE2D(_PatternMask2); SAMPLER(sampler_PatternMask2);
TEXTURE2D(_PatternMask3); SAMPLER(sampler_PatternMask3);
TEXTURE2D(_EffectMap); SAMPLER(sampler_EffectMap);
#endif

CBUFFER_START(UnityPerMaterial)
    float4 _BaseColor, _Color, _Color2, _Color3, _Color4, _EmissionColor, _DetailUV, _DetailUV2, _UVScalePattern;
    float4 _SpecColor;
    float _CullMode, _Cutoff, _DitherSwitch, _DitherBluenoise, _DetailUVRotator, _DetailUV2Rotator, _AlphaEx, _CarvatureStrength;
    float _Float0, _DetailNormalMapScale, _DetailNormalMapScale2, _DetailGlossScale, _DetailGlossScale2;
    float _DetailMetallicScale, _DetailMetallicScale2, _DetailOcculusionScale, _DetailOcculusionScale2;
    float _EmissionStrength, _OcculusionStrength, _Glossiness, _Glossiness2, _Glossiness3, _Glossiness4;
    float _Roughness, _Roughness2, _Roughness3, _Roughness4, _Metallic, _Metallic2, _Metallic3, _Metallic4;
    float _RoughnessReflections, _MetallicMask, _MetallicMask2, _GlossinessReflection, _MetallicMask3, _MetallicMask4;
    float _ExGloss, _WetnessPower, _WetAlbedoOffset, _NormalBackDirInvert, _Translucency, _TransDirect, _TransAmbient;
    float _TransNormalDistortion, _TransScattering, _TransShadow, _TRANSMISSION, _TransmissionStrength;
    float _Iridiscent, _IriHue, _IriSaturation, _IriBrightness, _IriContrast, _SpecularSetup;
#if defined(HS2_HM_P91)
    float4 _Color1_2, _Color2_2, _Color3_2, _patternuv1, _patternuv2, _patternuv3, _UVScroll;
    float _patternuv1Rotator, _patternuv2Rotator, _patternuv3Rotator, _alpha, _UVScrollRotator, _EmissionColorTexBase;
    float _FresnelPower, _FresnelScale, _SHADERTYPE_ITEM;
#endif
#if defined(HS2_HM_ALPHA)
    float _HS2FogModes;
#endif
CBUFFER_END

struct HS2HanmenAttributes { float4 pos : POSITION; float3 n : NORMAL; float4 t : TANGENT; float2 uv : TEXCOORD0; };
struct HS2HanmenVaryings
{
    float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1;
    float3 n : TEXCOORD2; float4 t : TEXCOORD3; float3 b : TEXCOORD4;
};

HS2HanmenVaryings HS2HanmenVert(HS2HanmenAttributes a)
{
    HS2HanmenVaryings o;
    VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
    VertexNormalInputs nn = GetVertexNormalInputs(a.n, a.t);
    o.pos = p.positionCS; o.wpos = p.positionWS;
    o.n = nn.normalWS; o.t = float4(nn.tangentWS, a.t.w * GetOddNegativeScale());
    o.b = cross(o.n, o.t.xyz) * o.t.w;   // Built-in surface shaders build the binormal per vertex
    o.uv = a.uv;
    return o;
}

// Built-in UnpackScaleNormal: xy scaled before z is rebuilt.
half3 HS2HanmenUnpackNormal(half4 packed, half scale)
{
    packed.x *= packed.w;
    half3 n;
    n.xy = (packed.xy * 2 - 1) * scale;
    n.z = sqrt(1 - saturate(dot(n.xy, n.xy)));
    return n;
}

// 4x4 ordered dither threshold (k + 1) / 16 at this pixel; y counted from the bottom like Built-in's screen position.
float HS2HanmenBayer4(float4 positionCS)
{
    uint2 p = (uint2)positionCS.xy;
    uint y = p.y;
#if UNITY_UV_STARTS_AT_TOP
    if (_ProjectionParams.x > 0) y = (uint)(_ScreenParams.y - 1 - positionCS.y + 0.5);
#endif
    const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    return (m[(y & 3) * 4 + (p.x & 3)] + 1) / 16.0;
}

// 8x8 ordered dither threshold j / 64 at this pixel (the gen-2 copies; the matrix as measured for the Hanmen clothes
// shaders, y counted from the bottom).
float HS2HanmenItemBayer8(float4 positionCS)
{
    uint2 p = (uint2)positionCS.xy;
    uint y = p.y;
#if UNITY_UV_STARTS_AT_TOP
    if (_ProjectionParams.x > 0) y = (uint)(_ScreenParams.y - 1 - positionCS.y + 0.5);
#endif
    const float m[64] = { 1, 49, 13, 61, 4, 52, 16, 64, 33, 17, 45, 29, 36, 20, 48, 32, 9, 57, 5, 53, 12, 60, 8, 56,
                          41, 25, 37, 21, 44, 28, 40, 24, 3, 51, 15, 63, 2, 50, 14, 62, 35, 19, 47, 31, 34, 18, 46, 30,
                          11, 59, 7, 55, 10, 58, 6, 54, 43, 27, 39, 23, 42, 26, 38, 22 };
    return m[(y & 7) * 8 + (p.x & 7)] / 64.0;
}

// Interleaved gradient noise at the integer pixel (x, y from the bottom): the pattern copy's dither (measured: 0 of 3,072
// pixels differ on the probe).
float HS2HanmenIGN(float4 positionCS)
{
    float2 p = floor(positionCS.xy);
#if UNITY_UV_STARTS_AT_TOP
    if (_ProjectionParams.x > 0) p.y = floor(_ScreenParams.y - positionCS.y);
#endif
    return frac(52.9829189 * frac(0.06711056 * p.x + 0.00583715 * p.y));
}

// Rotation by -pi * rotator about (0.5, 0.5).
float2 HS2HanmenRotate(float2 t, float rotator)
{
    t -= 0.5;
    float sn, cs; sincos(-PI * rotator, sn, cs);
    return float2(cs * t.x - sn * t.y, sn * t.x + cs * t.y) + 0.5;
}

half4 HS2HanmenRegionWeights(float2 uv)
{
    half4 cm = SAMPLE_TEXTURE2D(_ColorMask, sampler_ColorMask, uv);
#if defined(HS2_HM_P91)
    // the pattern copy: a lerp chain lerp(lerp(lerp(c1, c2, R), c3, G), c4, B) written as weights (measured)
    return half4((1 - cm.r) * (1 - cm.g) * (1 - cm.b), cm.r * (1 - cm.g) * (1 - cm.b), cm.g * (1 - cm.b), cm.b);
#else
    return half4(1 - cm.r - cm.g - cm.b, cm.r, cm.g, cm.b);
#endif
}

// Alpha / coverage test shared by the colour, shadow and depth passes.
half HS2HanmenAlpha(float2 uv, float4 positionCS)
{
    // measured: the colours' alphas with the region weights (as the colours)
    half4 w = HS2HanmenRegionWeights(uv);
    half ta = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a;
    half a = ta * dot(w, half4(_Color.a, _Color2.a, _Color3.a, _Color4.a));
#if defined(HS2_HM_GEN2)
    // gen-2 copies (GDC/Item Cutoff and its Hanmen twin; measured): no tearing; _Cutoff > 0 switches an 8x8 ordered dither
    // of the alpha on (the _DITHERSWITCH_ON keyword makes no difference), _Cutoff 0 keeps every pixel
    if (_Cutoff > 0) clip(a - HS2HanmenItemBayer8(positionCS));
    return a;
#endif
    // Tearing (AlphaEx < 1): a pixel is discarded where Occlusion.B is below a threshold that rises from 0.48 at
    // AlphaEx 0.9 to 0.91 at AlphaEx <= 0.1 (measured to 0.02; approximate: 1.02 - 0.6 AlphaEx, at most 0.91). No material
    // of the samples tears (AlphaEx 1); with MainTex.a < 1 the original also lowers the alpha (not reproduced).
    if (_AlphaEx < 1)
        clip(SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv).b - min(0.91, 1.02 - 0.6 * _AlphaEx));
#if defined(HS2_HM_P91)
    // the pattern copy (measured): clip on the main texture's alpha alone; with _Cutoff > 0 the alpha x region alpha x
    // _alpha becomes an interleaved-gradient-noise dither coverage
    clip(ta - _Cutoff);
    a *= _alpha;
    if (_Cutoff > 0) clip(a - HS2HanmenIGN(positionCS));
#elif defined(_DITHERSWITCH_ON)
    clip(a - HS2HanmenBayer4(positionCS));
#else
    clip(a - _Cutoff);
#endif
    return a;
}

struct HS2HanmenSurface { half3 albedo; half metallic, smoothness, occlusion; half3 emission; float3 normalWS; half transMask; };

// Detail maps: sampled at frac(rotate(uv * UVScalePattern.xy * DetailUV.xy)) (measured: they repeat whatever the
// texture's wrap mode; DetailUV.zw and UVScalePattern.zw unused), with the derivatives of the unwrapped coordinate.
half4 HS2HanmenDetail(TEXTURE2D_PARAM(tex, smp), float2 t)
{
    return SAMPLE_TEXTURE2D_GRAD(tex, smp, frac(t), ddx(t), ddy(t));
}

// Detail occlusion transfer of the R channel: smoothstep(0, 0.8, R) (measured at R = 0, 0.25, 0.5, 0.75, 1:
// 0, 0.2323, 0.6833, 0.9885, 1).
half HS2HanmenDetailOcc(half r)
{
    return smoothstep(0, 0.8, r);
}

HS2HanmenSurface HS2HanmenSurf(HS2HanmenVaryings i, bool front)
{
    HS2HanmenSurface s;
    float2 uv = i.uv;
    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
    half4 oc = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv);
    half4 mg = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv);
    half4 dm = SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, uv);
#if defined(HS2_HM_ALPHA) || defined(HS2_HM_GEN2)
    float2 duv = uv;   // Item Alpha and the gen-2 copies have no _UVScalePattern
#else
    float2 duv = uv * _UVScalePattern.xy;
#endif
    float2 t1 = HS2HanmenRotate(duv * _DetailUV.xy, _DetailUVRotator), t2 = HS2HanmenRotate(duv * _DetailUV2.xy, _DetailUV2Rotator);
    half4 d1 = HS2HanmenDetail(TEXTURE2D_ARGS(_DetailGlossMap, sampler_DetailGlossMap), t1);
    half4 d2 = HS2HanmenDetail(TEXTURE2D_ARGS(_DetailGlossMap2, sampler_DetailGlossMap2), t2);
    half4 w = HS2HanmenRegionWeights(uv);

#if defined(HS2_HM_P91)
    // the pattern copy (measured): pattern k recolours region k by 1 - PatternMask_k.r (no mask-alpha presence, the
    // pattern colour's alpha unused); pattern UV frac(rotate(uv * UVScalePattern.xy * patternuv_k.xy)) (zw unused);
    // MetallicGloss R = metallic mask, G = emission, B = translucency mask (thickness), A = gloss (roughness) map
    float2 tp = uv * _UVScalePattern.xy;
    half pm1 = HS2HanmenDetail(TEXTURE2D_ARGS(_PatternMask1, sampler_PatternMask1), HS2HanmenRotate(tp * _patternuv1.xy, _patternuv1Rotator)).r;
    half pm2 = HS2HanmenDetail(TEXTURE2D_ARGS(_PatternMask2, sampler_PatternMask2), HS2HanmenRotate(tp * _patternuv2.xy, _patternuv2Rotator)).r;
    half pm3 = HS2HanmenDetail(TEXTURE2D_ARGS(_PatternMask3, sampler_PatternMask3), HS2HanmenRotate(tp * _patternuv3.xy, _patternuv3Rotator)).r;
    half3 col = lerp(_Color.rgb, _Color1_2.rgb, 1 - pm1) * w.x + lerp(_Color2.rgb, _Color2_2.rgb, 1 - pm2) * w.y
              + lerp(_Color3.rgb, _Color3_2.rgb, 1 - pm3) * w.z + _Color4.rgb * w.w;
    half mmask = mg.r, rmask = mg.a;
#else
    half3 col = _Color.rgb * w.x + _Color2.rgb * w.y + _Color3.rgb * w.z + _Color4.rgb * w.w;
    half mmask = mg.b, rmask = mg.r;
#endif
    half metal = dot(w, half4(_Metallic * lerp(1, mmask, _MetallicMask), _Metallic2 * lerp(1, mmask, _MetallicMask2),
                              _Metallic3 * lerp(1, mmask, _MetallicMask3), _Metallic4 * lerp(1, mmask, _MetallicMask4)));
    // measured: per region glossiness * lerp(1, MetallicGloss.R (pattern copy: .A), Roughness_k)
    half gloss = dot(w, half4(_Glossiness * lerp(1, rmask, _Roughness), _Glossiness2 * lerp(1, rmask, _Roughness2),
                              _Glossiness3 * lerp(1, rmask, _Roughness3), _Glossiness4 * lerp(1, rmask, _Roughness4)));

    // Detail factors from the R channels: 1 - sum_k scale_k mask_k (1 - D_k.r) (measured for metallic: the two details
    // add up, one alone is lerp(1, D.r, scale * mask)).
    half m1 = dm.r, m2 = dm.g;
    half metalF = saturate(1 - _DetailMetallicScale * m1 * (1 - d1.r) - _DetailMetallicScale2 * m2 * (1 - d2.r));
    half glossF = saturate(1 - _DetailGlossScale * m1 * (1 - d1.r) - _DetailGlossScale2 * m2 * (1 - d2.r));
    half occF = saturate(1 - _DetailOcculusionScale * m1 * (1 - HS2HanmenDetailOcc(d1.r)) - _DetailOcculusionScale2 * m2 * (1 - HS2HanmenDetailOcc(d2.r)));

    s.albedo = tex.rgb * _BaseColor.rgb * col * lerp(1, _WetAlbedoOffset, _ExGloss);
    s.metallic = metal * metalF;
    // measured: wet smoothness lerp(smoothness, 1, saturate(ExGloss * WetnessPower))
    s.smoothness = lerp(gloss * glossF, 1, saturate(_ExGloss * _WetnessPower));
    s.occlusion = lerp(1, oc.g, _OcculusionStrength) * occF;
#if defined(HS2_HM_P91)
    // measured: (the region colour with _EMISSIONCOLORTEXBASE_ON, else EmissionColor) x strength x MetallicGloss.G x
    // EffectMap.rgb at frac(rotate(uv * UVScroll.xy)) (zw unused); no main texture
    float2 te = HS2HanmenRotate(uv * _UVScroll.xy, _UVScrollRotator);
    half3 fx = HS2HanmenDetail(TEXTURE2D_ARGS(_EffectMap, sampler_EffectMap), te).rgb;
#if defined(_EMISSIONCOLORTEXBASE_ON)
    half3 ecol = col;
#else
    half3 ecol = _EmissionColor.rgb;
#endif
    s.emission = ecol * _EmissionStrength * mg.g * fx;
    s.transMask = mg.b;
#elif defined(HS2_HM_GEN2)
    s.emission = _EmissionColor.rgb * _EmissionStrength * mg.g;   // measured: no main texture
    s.transMask = 1;
#else
    s.emission = _EmissionColor.rgb * _EmissionStrength * mg.g * tex.rgb;   // measured: the main texture only
    s.transMask = 1;
#endif

    // Normal map (Built-in UnpackScaleNormal, _Float0) plus the detail height maps (G, forward differences at 0.0027 in
    // detail texture space, scale DetailNormalMapScale * mask), blended like BlendNormals. Approximate: on steep synthetic
    // height ramps 0.015-0.023 RMS (the original tilts a little more on smooth slopes and less on fine ones).
    const float kDelta = 0.0027;
    half h1 = d1.g, h2 = d2.g;
    half h1x = HS2HanmenDetail(TEXTURE2D_ARGS(_DetailGlossMap, sampler_DetailGlossMap), t1 + float2(kDelta, 0)).g;
    half h1y = HS2HanmenDetail(TEXTURE2D_ARGS(_DetailGlossMap, sampler_DetailGlossMap), t1 + float2(0, kDelta)).g;
    half h2x = HS2HanmenDetail(TEXTURE2D_ARGS(_DetailGlossMap2, sampler_DetailGlossMap2), t2 + float2(kDelta, 0)).g;
    half h2y = HS2HanmenDetail(TEXTURE2D_ARGS(_DetailGlossMap2, sampler_DetailGlossMap2), t2 + float2(0, kDelta)).g;
    half s1 = 2 * _DetailNormalMapScale * m1, s2 = 2 * _DetailNormalMapScale2 * m2;
    half3 nd1 = normalize(half3(-s1 * (h1x - h1), -s1 * (h1y - h1), 1));
    half3 nd2 = normalize(half3(-s2 * (h2x - h2), -s2 * (h2y - h2), 1));
    half3 nTS = HS2HanmenUnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), _Float0);
    nTS = normalize(half3(nTS.xy + nd1.xy, nTS.z * nd1.z));
    nTS = normalize(half3(nTS.xy + nd2.xy, nTS.z * nd2.z));
    float3 n = normalize(i.t.xyz * nTS.x + i.b * nTS.y + i.n * nTS.z);
    // measured: back faces flip the normal when _NormalBackDirInvert is 1 (the materials' value)
    s.normalWS = (!front && _NormalBackDirInvert > 0.5) ? -n : n;
    return s;
}

// Direct light of BRDF1 with the specular term scaled by the occlusion and N.V saturated (both measured).
half3 HS2HanmenDirect(HS2Surface s, float3 v, float3 l, half3 lightColor)
{
    float3 n = s.normalWS;
    float3 h = SafeNormalize(l + v);
#if defined(HS2_HM_GEN2)
    half nv = abs(dot(n, v));   // the gen-2 copies: Unity's abs
#else
    half nv = saturate(dot(n, v));   // measured: saturate, not Unity's abs (normals turned away from the view)
#endif
    half nl = saturate(dot(n, l));
    half nh = saturate(dot(n, h));
    half lh = saturate(dot(l, h));
    half pr = 1 - s.smoothness;
    half a = max(pr * pr, 0.002);
    half diffuseTerm = HS2DisneyDiffuse(nv, nl, lh, pr) * nl;
    half lv = nl * (nv * (1 - a) + a);
    half ll = nv * (nl * (1 - a) + a);
    half V = 0.5 / (lv + ll + 1e-5);
    half a2 = a * a;
    float d = (nh * a2 - nh) * nh + 1.0;
    half D = INV_PI * a2 / (d * d + 1e-7);
    half spec = max(0, V * D * PI * nl) * (any(s.specColor) ? 1.0 : 0.0);
    half3 F = s.specColor + (1 - s.specColor) * pow(1 - lh, 5);
#if defined(HS2_HM_GEN2) || defined(HS2_HM_P91)
    return s.albedo * s.oneMinusReflectivity * lightColor * diffuseTerm + spec * lightColor * F;   // no specular occlusion
#else
    return s.albedo * s.oneMinusReflectivity * lightColor * diffuseTerm + spec * s.occlusion * lightColor * F;
#endif
}

half3 HS2HanmenTrans(HS2Surface s, float3 v, float3 l, half3 lightColor)
{
    float3 tl = l + s.normalWS * _TransNormalDistortion;
    half vdl = pow(saturate(dot(v, -tl)), _TransScattering);
    return vdl * _TransDirect * lightColor;
}

static half hmTransMask = 1;   // the surface's translucency mask (pattern copy: MetallicGloss.B)

half3 HS2HanmenShade(HS2Surface s, float3 positionWS, float2 screenUV)
{
    float3 v = GetWorldSpaceNormalizeViewDir(positionWS);
    float3 n = s.normalWS;
    half3 diffColor = s.albedo * s.oneMinusReflectivity;
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    Light main = GetMainLight(shadowCoord);
    main.shadowAttenuation = lerp(1, main.shadowAttenuation, HS2ShadowReceive);
    main.color *= HS2LightLayerWeight(main.layerMask);

    half pr = 1 - s.smoothness;
    half a = max(pr * pr, 0.002);
    half3 sh = SampleSH(n);
    half3 giDiffuse = sh * s.occlusion;
    half3 env = GlossyEnvironmentReflection(reflect(-v, n), positionWS, pr, s.occlusion, screenUV);
#if defined(HS2_HM_GEN2)
    half nv = abs(dot(n, v));
#else
    half nv = saturate(dot(n, v));
#endif
    half surfaceReduction = 1.0 / (a * a + 1.0);
    half grazing = saturate(s.smoothness + (1 - s.oneMinusReflectivity));
    half3 fresnelLerp = lerp(s.specColor, grazing.xxx, pow(1 - nv, 5));
    half3 c = diffColor * giDiffuse + surfaceReduction * env * fresnelLerp;

    half3 mainColor = main.color * main.distanceAttenuation * main.shadowAttenuation;
    c += HS2HanmenDirect(s, v, main.direction, mainColor);
#if defined(_TRANSMISSION_ON)
    c += diffColor * _TransmissionStrength * saturate(-dot(n, main.direction)) * mainColor;
#endif
    half3 trans = HS2HanmenTrans(s, v, main.direction, main.color * main.distanceAttenuation * lerp(1, main.shadowAttenuation, _TransShadow));
#if defined(_ADDITIONAL_LIGHTS)
    HS2LightIter it = HS2LightIterInit(positionWS, screenUV);
    uint lightIndex;
    [loop] while (HS2LightNext(it, lightIndex))
    {
        Light L = GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1));
        if (!HS2LightReaches(L, lightIndex)) continue;
        half3 lc = L.color * L.distanceAttenuation;
        L.shadowAttenuation = lerp(1, L.shadowAttenuation, HS2ShadowReceive);
        c += HS2HanmenDirect(s, v, L.direction, lc * L.shadowAttenuation);
#if defined(_TRANSMISSION_ON)
        c += diffColor * _TransmissionStrength * saturate(-dot(n, L.direction)) * lc * L.shadowAttenuation;
#endif
        trans += HS2HanmenTrans(s, v, L.direction, lc * lerp(1, L.shadowAttenuation, _TransShadow));
    }
#endif
#if defined(HS2_HM_ALPHA)
    // Item Alpha (measured): the ambient part only (saturated), no direct translucency
    c += s.albedo * saturate(_Translucency * _TransAmbient * giDiffuse);
#elif defined(HS2_HM_GEN2) || defined(HS2_HM_P91)
    // gen-2 and pattern copies (measured): strength x (the direct part + the ambient part SH x TransAmbient lit by the
    // main light: nothing under ambient light alone)
    half3 mainTrans = main.color * main.distanceAttenuation * lerp(1, main.shadowAttenuation, _TransShadow);
#if defined(HS2_HM_P91)
    half tStrength = _Translucency * hmTransMask;                          // the pattern copy: linear in Translucency
#else
    half tStrength = _Translucency * saturate(_Translucency);              // gen-2: Translucency x saturate(Translucency)
#endif
    c += s.albedo * tStrength * (trans + giDiffuse * _TransAmbient * mainTrans);
#else
    // measured: the ambient part is saturated, the direct part is not
    c += s.albedo * (_Translucency * trans + saturate(_Translucency * _TransAmbient * giDiffuse));
#endif
    return c + s.emission;
}

#if defined(HS2_HM_FORWARD)   // the forward pass (declares _RECEIVE_SHADOWS_OFF)
half4 HS2HanmenFrag(HS2HanmenVaryings i, bool front : SV_IsFrontFace) : SV_Target
{
    half alpha = HS2HanmenAlpha(i.uv, i.pos);
#if defined(HS2_HM_ALPHA)
    HS2ShadowReceive = 0;   // a transparent queue: Built-in forward rendering applies no realtime shadows there
#else
    HS2ShadowReceive = _RECEIVE_SHADOWS_OFF ? 0 : 1;   // set by ShaderMap.Swap for queues above 2500
#endif
    HS2HanmenSurface hs = HS2HanmenSurf(i, front);
    hmTransMask = hs.transMask;
    HS2Surface s = HS2SurfaceMetallic(hs.albedo, hs.metallic, hs.smoothness, hs.occlusion, hs.normalWS);
    half3 c = HS2HanmenShade(s, i.wpos, GetNormalizedScreenSpaceUV(i.pos));
#if defined(HS2_HM_ALPHA)
    return half4(HS2ApplyFog(c + hs.emission, i.wpos, _HS2FogModes), alpha);
#else
    return half4(c + hs.emission, 1);
#endif
}
#endif

float3 _LightDirection;
float3 _LightPosition;

HS2HanmenVaryings HS2HanmenShadowVert(HS2HanmenAttributes a)
{
    HS2HanmenVaryings o = HS2HanmenVert(a);
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

half4 HS2HanmenDepthFrag(HS2HanmenVaryings i) : SV_Target
{
    HS2HanmenAlpha(i.uv, i.pos);
    return 0;
}
#endif
