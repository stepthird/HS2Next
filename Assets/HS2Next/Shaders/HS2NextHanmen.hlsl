// Surface of the Hanmen mod clothes shaders (Phase 3.7 package F2), evaluated with the Built-in BRDF1 of HS2NextBRDF.hlsl.
// The originals are Amplify surface shaders with their own property sets (not copies of AIT/Clothes True). Behaviour was
// inferred by black-box probing only (Editor/Fit/SkinFit.cs; case files Tests/FitCases/hcr_*.txt).
//
// Variant switches (define before including):
//   HS2_HANMEN_REPLICA   "Hanmen/Clothes True Replica Opaque": colour-mask regions with per-region metal / gloss
//   HS2_HANMEN_CUTOFF    "Hanmen/Clothes True Cutoff": composited colour / metal / gloss (AIT Clothes True-like inputs)
//   HS2_HANMEN_V09       "Hanmen/Clothes Cutoff v0.9": colour-mask regions (_Color1, _Color3, _Color4, _Color5) with
//                        Cutoff's alpha, wetness and normals
//   HS2_HANMEN_V09_BLEND (with HS2_HANMEN_V09) "Hanmen/Item Transparent" (Phase 5.2): the v0.9 surface blended with alpha
//                        MainTex.a x the regions' weighted colour alpha (not saturated; no dither, no cutoff; probed)
//
// Measured (Replica; flat probe values exact to the 4th decimal):
//   M = _ColorMask.rgb, w = (1 - M.r - M.g - M.b, M.r, M.g, M.b) (weighted sums, not clamped: a white mask gives w1 = -2)
//   albedo     = MainTex.rgb * BaseColor.rgb * sum(w_k Color_k)              (not saturated; the alphas unused)
//   metallic   = sum(w_k Metallic_k lerp(1, MetallicGlossMap.B, MetallicMask_k))
//   smoothness = sum(w_k Glossiness_k) * MetallicGlossMap.R * DetailMainTex.B (not clamped: 1.2 shades like 0.8)
//   occlusion  = lerp(1, Occlusion.R, OcclusionStrength) + M_d.r c1 lerp(1, D1.r, s1) + M_d.g c2 lerp(1, D2.r, s2)
//                (M_d = DetailMask, c = DetailOcclusionContrast, s = DetailOcclusionScale; added, not multiplied)
//   emission   = EmissionColor.rgb * EmissionStrength * MetallicGlossMap.G; with _EMISSIONCOLOR1_ON 5 * Color.rgb * G
//   alpha      = MainTex.a * saturate(Occlusion.B + AlphaEx), clip(alpha - Cutoff) (AlphaToMask in the original: no
//                effect on a single-sample target)
//   UVs        all maps on uv0 without their tiling / offset; detail maps D1 / D2 on rotate(uv0 * DetailUV.xy,
//                -DetailUVRotator degrees) about (0.5, 0.5) (DetailUV.zw unused)
//   normal     n = normalize(N.xy + g1 + g2, 0.5) with N.xy of Built-in UnpackScaleNormal(_BumpMap, _BumpScale) (the
//                map's z is not used: normal maps tilt about twice as much as in Standard), g_k = (h - h(t + 0.0125)) per
//                axis of D_k.g in detail texture space, x DetailNormalMapScale_k x DetailMask (R / G) x 0.5 (map 1) or 1
//                (map 2); surface-shader tangent frame; back faces keep the front normal
//   _Roughness*, _UVScalePattern, _patternuv*, _AlphaMask*, _Weathering* have no visible effect.
//
// Measured (Cutoff; hct_*.txt):
//   albedo     = MainTex.rgb * BaseColor.rgb * Color.rgb
//   metallic   = lerp(DetailMainTex.R lerp(1, MGM.B, MetallicMask), Metallic4 lerp(1, MGM.B, MetallicMask4), ColorMask.B)
//   smoothness = lerp(DetailMainTex.B lerp(1, MGM.R, Roughness), Glossiness4 lerp(1, MGM.R, Roughness4), ColorMask.B)
//   wetness    w = saturate(WetnessMap.R + WetnessMap.G): metallic -> lerp(m, 1, saturate(ExGloss WetnessPower2 w)),
//                smoothness -> lerp(s, 1, saturate(ExGloss WetnessPower w))
//   occlusion  = lerp(1, Occlusion.R, OcclusionStrength); emission as Replica (no keyword compiled in any item copy)
//   alpha      _Cutoff > 0 switches an 8x8 ordered dither of MainTex.a on (threshold j / 64, j = 1..64, the matrix in
//                HS2HanmenBayer8; y from the bottom); tearing keeps Occlusion.B + AlphaEx >= 1
//   normal     Unity's UnpackScaleNormal (z rebuilt) plus the detail height gradients as Replica with gain 2 for both maps
//
// Measured (v0.9; hcv9_*.txt): albedo MainTex.rgb x BaseColor1.rgb x weighted-sum colour (Color1 / Color3 / Color4 /
//   Color5); per region metal_k = Metallic_k lerp(1, MGM.B, MetallicMask_k), gloss_k = Glossiness_k lerp(1, MGM.R,
//   Roughness_k), combined as lerp(w1 x1 + R x2 + G x3, x4, B); dither alpha MainTex.a x weighted-sum colour alpha;
//   wetness, tearing, occlusion, emission as Cutoff; detail height normals on plain uv0 (DetailUV / rotators unused)
//   not reproduced: _WeatheringAll (default 0; brightens and glosses), the keyword variants _DETAILOCCLUSION_ON,
//                _BLUENOISEDITHER_ON, _ENABLEALPHAMASK1_ON, _EMISSIONCOLOR*_ON (compiled by no item copy found)
#ifndef HS2NEXT_HANMEN_INCLUDED
#define HS2NEXT_HANMEN_INCLUDED
#include "HS2NextBRDF.hlsl"
#if defined(HS2_HANMEN_BLEND) || defined(HS2_HANMEN_V09_BLEND)
#define HS2_HANMEN_FOG
#include "HS2NextFog.hlsl"
#endif

TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
TEXTURE2D(_ColorMask); SAMPLER(sampler_ColorMask);
TEXTURE2D(_DetailMainTex); SAMPLER(sampler_DetailMainTex);
TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
TEXTURE2D(_DetailMask); SAMPLER(sampler_DetailMask);
TEXTURE2D(_DetailGlossMap); SAMPLER(sampler_DetailGlossMap);
TEXTURE2D(_DetailGlossMap2); SAMPLER(sampler_DetailGlossMap2);
TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_WetnessMap); SAMPLER(sampler_WetnessMap);

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST, _BaseColor, _Color, _Color2, _Color3, _Color4, _EmissionColor;
    float4 _UVScalePattern, _DetailUV, _DetailUV2, _WeatheringUV, _patternuv1, _patternuv2, _patternuv3, _patternuvbase;
    float _Cutoff, _AlphaEx, _BumpScale, _DetailNormalMapScale, _DetailNormalMapScale2, _DetailUVRotator, _DetailUVRotator2;
    float _EmissionStrength, _EmissionColor1, _EmissionColor2, _EmissionColor3, _OcclusionStrength;
    float _DetailOcclusionScale, _DetailOcclusionScale2, _DetailOcclusionContrast, _DetailOcclusionContrast2;
    float _Glossiness, _Glossiness2, _Glossiness3, _Glossiness4, _Roughness, _Roughness2, _Roughness3, _Roughness4;
    float _Metallic, _Metallic2, _Metallic3, _Metallic4, _MetallicMask, _MetallicMask2, _MetallicMask3, _MetallicMask4;
    float _WeatheringAll, _WeatheringRange1, _WeatheringRange2, _WeatheringRange3, _WeatheringRange4, _WeatheringRange5, _WeatheringRange6;
    float4 _WeatheringAlbedo1;
    float _ExGloss, _WetnessPower, _WetnessPower2, _DetailOcclusion, _BlueNoiseDither, _EnableAlphaMask1;
    float _WeatheringGloss, _WeatheringBump, _WeatheringBumpPower;
    float4 _BaseColor1, _Color1, _Color5, _WeatheringAlbedo;
    float _EnableAlphaMask;
    // v2 copies (HS2_HANMEN_V2) and the blended siblings (HS2_HANMEN_BLEND)
    float4 _BodyColor, _WetColor, _WetUV;
    float _DetailGlossScale, _DetailGlossScale2, _DetailMetallicScale, _DetailMetallicScale2, _DetailUV2Rotator;
    float _CarvatureStrength, _WetAlpha, _WetGlossBase, _WetAlbedoOffset, _WetSplatsAlbedoOffset, _WetOpacitySplats;
    float _WetGlossSplats, _WetBumpOffset, _CullMode, _AlphaMaster, _FresnelBias, _FresnelPower, _FresnelScale;
    float _Translucency, _TransNormalDistortion, _TransScattering, _TransDirect, _TransAmbient, _TransShadow;
    float _BlendSCR, _BlendDST, _AlphaToMask, _DetailAlpha1, _DetailAlpha2, _DetailGloss;
#if defined(HS2_HANMEN_FOG)
    float _HS2FogModes;
#endif
CBUFFER_END

// Colour names of the v0.9 model (HS2_HANMEN_V09): base, region 1, R, G, B. A wrapper for a bundle copy that declares the
// same set under other names (the older Hanmen/Item Cutoff copies: _BaseColor, _Color, _Color2, _Color3, _Color4) defines
// all five before including this file.
#ifndef HS2_V09_C1
#define HS2_V09_BASE _BaseColor1
#define HS2_V09_C1 _Color1
#define HS2_V09_C2 _Color3
#define HS2_V09_C3 _Color4
#define HS2_V09_C4 _Color5
#endif
// Inputs of the v0.9 surface that some bundle copies do not declare (the older Hanmen/Item Cutoff copies have no _AlphaEx
// and no _WetnessMap): such a wrapper defines HS2_V09_ALPHAEX 1 (no tearing) and HS2_V09_NO_WETMAP (dry) instead of
// declaring properties its originals lack (ModShaderTest M2). Default: the properties (every other Hanmen shader).
#ifndef HS2_V09_ALPHAEX
#define HS2_V09_ALPHAEX _AlphaEx
#endif



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

// Built-in UnpackScaleNormal: x = R * A (DXT5nm and plain RGB alike), xy scaled before z is rebuilt.
half3 HS2HanmenUnpackNormal(half4 packed, half scale)
{
    packed.x *= packed.w;
    half3 n;
    n.xy = (packed.xy * 2 - 1) * scale;
    n.z = sqrt(1 - saturate(dot(n.xy, n.xy)));
    return n;
}

// Detail UV: uv * DetailUV.xy, rotated by -degrees about (0.5, 0.5); DetailUV.zw has no effect (measured).
float2 HS2HanmenDetailUV(float2 uv, float4 st, float degrees)
{
    float2 t = uv * st.xy - 0.5;
    float s, c; sincos(radians(degrees), s, c);
    return float2(c * t.x + s * t.y, -s * t.x + c * t.y) + 0.5;
}

// Rotation by -pi * rotator about (0.5, 0.5) (rotators with the range [-1, 1]).
float2 HS2HanmenRotPi(float2 t, float rotator)
{
    t -= 0.5;
    float sn, cs; sincos(-PI * rotator, sn, cs);
    return float2(cs * t.x - sn * t.y, sn * t.x + cs * t.y) + 0.5;
}

// 8x8 ordered dither threshold j / 64 (j = 1..64) at this pixel; y counted from the bottom like Built-in's screen position.
float HS2HanmenBayer8(float4 positionCS)
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

// Alpha tests shared by the colour, shadow and depth passes.
void HS2HanmenClip(float2 uv, float4 positionCS)
{
    half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a;
    half tear = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv).b;
#if defined(HS2_HANMEN_V09)
    half3 cm = SAMPLE_TEXTURE2D(_ColorMask, sampler_ColorMask, uv).rgb;
    // Measured (v0.9): the dithered alpha is MainTex.a times the regions' colour alphas (weighted sum, as the colours).
    a *= (1 - cm.r - cm.g - cm.b) * HS2_V09_C1.a + cm.r * HS2_V09_C2.a + cm.g * HS2_V09_C3.a + cm.b * HS2_V09_C4.a;
#endif
#if defined(HS2_HANMEN_V09_BLEND)
    // blended v0.9: the alpha goes to the blend; tearing as v0.9
    clip(tear + HS2_V09_ALPHAEX - 1);
#elif defined(HS2_HANMEN_BLEND)
#if defined(HS2_HANMEN_TA)
    // True Alpha: MainTex.a against _Cutoff, tearing as the v2 copies
    clip(a - _Cutoff);
    clip(tear + saturate(_AlphaEx) - 1);
#else
    // blended siblings: tearing only (the alpha goes to the blend)
    clip(tear + _AlphaEx - 1);
#endif
#elif defined(HS2_HANMEN_V2)
    // v2 copies: as Cutoff, the tearing with AlphaEx saturated (AlphaEx -1 keeps B = 1 only)
    if (_Cutoff > 0) clip(a - HS2HanmenBayer8(positionCS));
    clip(tear + saturate(_AlphaEx) - 1);
#elif defined(HS2_HANMEN_CUTOFF) || defined(HS2_HANMEN_V09)
    // Measured: _Cutoff only switches the 8x8 dither of MainTex.a on (any value > 0); tearing keeps B + AlphaEx >= 1.
    if (_Cutoff > 0) clip(a - HS2HanmenBayer8(positionCS));
    clip(tear + HS2_V09_ALPHAEX - 1);
#else
    clip(a * saturate(tear + _AlphaEx) - _Cutoff);
#endif
}

// Height gradient of a grayscale detail map (G): forward differences at a fixed offset d in detail texture space,
// (h - h(t + d)) * strength * gain.
half2 HS2HanmenHeightGrad(TEXTURE2D_PARAM(tex, smp), float2 t, half strength, float d, half gain)
{
    half h = SAMPLE_TEXTURE2D(tex, smp, t).g;
    half hx = SAMPLE_TEXTURE2D(tex, smp, t + float2(d, 0)).g;
    half hy = SAMPLE_TEXTURE2D(tex, smp, t + float2(0, d)).g;
    return half2(h - hx, h - hy) * strength * gain;
}

struct HS2HanmenSurface { half3 albedo; half metallic, smoothness, occlusion; half3 emission; float3 normalWS; };

HS2HanmenSurface HS2HanmenSurf(HS2HanmenVaryings i)
{
    HS2HanmenSurface s;
    float2 uv = i.uv;
    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
    half4 mg = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv);
    half4 oc = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv);
    half4 dm = SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, uv);
    half4 dmt = SAMPLE_TEXTURE2D(_DetailMainTex, sampler_DetailMainTex, uv);
#if defined(HS2_HANMEN_V2) || defined(HS2_HANMEN_PH)
    // v2 copies and Pantyhose: rotators in [-1, 1] (x pi), UVScalePattern on the detail UVs, repeating detail maps (as Item Cutoff)
    float2 t1 = HS2HanmenRotPi(uv * _UVScalePattern.xy * _DetailUV.xy, _DetailUVRotator);
    float2 t2 = HS2HanmenRotPi(uv * _UVScalePattern.xy * _DetailUV2.xy, _DetailUV2Rotator);
    half4 d1 = SAMPLE_TEXTURE2D_GRAD(_DetailGlossMap, sampler_DetailGlossMap, frac(t1), ddx(t1), ddy(t1));
    half4 d2 = SAMPLE_TEXTURE2D_GRAD(_DetailGlossMap2, sampler_DetailGlossMap2, frac(t2), ddx(t2), ddy(t2));
#else
    float2 t1 = HS2HanmenDetailUV(uv, _DetailUV, _DetailUVRotator), t2 = HS2HanmenDetailUV(uv, _DetailUV2, _DetailUVRotator2);
    half4 d1 = SAMPLE_TEXTURE2D(_DetailGlossMap, sampler_DetailGlossMap, t1);
    half4 d2 = SAMPLE_TEXTURE2D(_DetailGlossMap2, sampler_DetailGlossMap2, t2);
#endif

#if defined(HS2_HANMEN_CUTOFF)
    // Colour composited into _MainTex (characters) or the item texture, times BaseColor and Color; metal (R) / smoothness
    // (B) from the composited _DetailMainTex, masked by MetallicGlossMap B / R; region 4 (ColorMask.B) lerps them towards
    // Metallic4 / Glossiness4.
    s.albedo = tex.rgb * _BaseColor.rgb * _Color.rgb;
    half m4 = SAMPLE_TEXTURE2D(_ColorMask, sampler_ColorMask, uv).b;
#if defined(HS2_HANMEN_V2)
    // v2 copies (measured): the metallic masks read MetallicGlossMap.R (not B); detail factors from the R channels of the detail maps
    // (1 - scale x mask x (1 - D.r), masks DetailMask R / G); occlusion from Occlusion.G; wet albedo lerp(1, offset, ExGloss)
    half metalF = saturate(1 - _DetailMetallicScale * dm.r * (1 - d1.r) - _DetailMetallicScale2 * dm.g * (1 - d2.r));
    half glossF = saturate(1 - _DetailGlossScale * dm.r * (1 - d1.r) - _DetailGlossScale2 * dm.g * (1 - d2.r));
    s.metallic = lerp(dmt.r * lerp(1, mg.r, _MetallicMask), _Metallic4 * lerp(1, mg.r, _MetallicMask4), m4) * metalF;
    s.smoothness = lerp(dmt.b * lerp(1, mg.r, _Roughness), _Glossiness4 * lerp(1, mg.r, _Roughness4), m4) * glossF;
    s.albedo *= lerp(1, _WetAlbedoOffset, _ExGloss);
    s.smoothness = lerp(s.smoothness, _WetGlossBase, _ExGloss);   // wetness with the splat inputs at their defaults
    s.occlusion = lerp(1, oc.g, _OcclusionStrength);
    s.emission = _EmissionColor.rgb * _EmissionStrength * mg.g;
#else
    s.metallic = lerp(dmt.r * lerp(1, mg.b, _MetallicMask), _Metallic4 * lerp(1, mg.b, _MetallicMask4), m4);
    s.smoothness = lerp(dmt.b * lerp(1, mg.r, _Roughness), _Glossiness4 * lerp(1, mg.r, _Roughness4), m4);
    // Wetness (game-driven _ExGloss): metal and smoothness towards 1 by ExGloss x WetnessPower2 / WetnessPower x map.
    half4 wm = SAMPLE_TEXTURE2D(_WetnessMap, sampler_WetnessMap, uv);
    half wet = saturate(wm.r + wm.g);   // measured: R and G add (max and R alone do not fit)
    s.metallic = lerp(s.metallic, 1, saturate(_ExGloss * _WetnessPower2 * wet));
    s.smoothness = lerp(s.smoothness, 1, saturate(_ExGloss * _WetnessPower * wet));
    s.occlusion = lerp(1, oc.r, _OcclusionStrength);
    s.emission = _EmissionColor.rgb * _EmissionStrength * mg.g;
#endif
#elif defined(HS2_HANMEN_V09)
    half3 m = SAMPLE_TEXTURE2D(_ColorMask, sampler_ColorMask, uv).rgb;
    half4 w = half4(1 - m.r - m.g - m.b, m.r, m.g, m.b);
    s.albedo = tex.rgb * HS2_V09_BASE.rgb * (w.x * HS2_V09_C1.rgb + w.y * HS2_V09_C2.rgb + w.z * HS2_V09_C3.rgb + w.w * HS2_V09_C4.rgb);
    half4 mk = half4(_Metallic * lerp(1, mg.b, _MetallicMask), _Metallic2 * lerp(1, mg.b, _MetallicMask2),
                     _Metallic3 * lerp(1, mg.b, _MetallicMask3), _Metallic4 * lerp(1, mg.b, _MetallicMask4));
    half4 gk = half4(_Glossiness * lerp(1, mg.r, _Roughness), _Glossiness2 * lerp(1, mg.r, _Roughness2),
                     _Glossiness3 * lerp(1, mg.r, _Roughness3), _Glossiness4 * lerp(1, mg.r, _Roughness4));
    // Measured: regions 1-3 as a weighted sum (w1 = 1 - R - G - B), then a lerp towards region 4 by B (exact over
    // hcv9_fit.txt; a plain weighted sum misses by 0.2).
    s.metallic = lerp(dot(w.xyz, mk.xyz), mk.w, m.b);
    s.smoothness = lerp(dot(w.xyz, gk.xyz), gk.w, m.b);
#if defined(HS2_V09_NO_WETMAP)
    half wet = 0;
#else
    half4 wm = SAMPLE_TEXTURE2D(_WetnessMap, sampler_WetnessMap, uv);
    half wet = saturate(wm.r + wm.g);
#endif
    s.metallic = lerp(s.metallic, 1, saturate(_ExGloss * _WetnessPower2 * wet));
    s.smoothness = lerp(s.smoothness, 1, saturate(_ExGloss * _WetnessPower * wet));
    s.occlusion = lerp(1, oc.r, _OcclusionStrength);
    s.emission = _EmissionColor.rgb * _EmissionStrength * mg.g;
#else
    half3 m = SAMPLE_TEXTURE2D(_ColorMask, sampler_ColorMask, uv).rgb;
    half4 w = half4(1 - m.r - m.g - m.b, m.r, m.g, m.b);
    half3 col = w.x * _Color.rgb + w.y * _Color2.rgb + w.z * _Color3.rgb + w.w * _Color4.rgb;
    s.albedo = tex.rgb * _BaseColor.rgb * col;
    s.metallic = w.x * _Metallic * lerp(1, mg.b, _MetallicMask) + w.y * _Metallic2 * lerp(1, mg.b, _MetallicMask2)
               + w.z * _Metallic3 * lerp(1, mg.b, _MetallicMask3) + w.w * _Metallic4 * lerp(1, mg.b, _MetallicMask4);
    s.smoothness = dot(w, half4(_Glossiness, _Glossiness2, _Glossiness3, _Glossiness4)) * mg.r * dmt.b;
    s.occlusion = lerp(1, oc.r, _OcclusionStrength)
                + dm.r * _DetailOcclusionContrast * lerp(1, d1.r, _DetailOcclusionScale)
                + dm.g * _DetailOcclusionContrast2 * lerp(1, d2.r, _DetailOcclusionScale2);
#if defined(_EMISSIONCOLOR1_ON)
    // Measured on a bundle copy that compiles the keyword: the emission becomes 5 x _Color.rgb x MetallicGlossMap.G (the
    // emission colour and strength, the region weight and BaseColor unused). _EMISSIONCOLOR2_ON / 3 were compiled by no
    // copy found (not reproduced).
    s.emission = 5 * _Color.rgb * mg.g;
#else
    s.emission = _EmissionColor.rgb * _EmissionStrength * mg.g;
#endif

#endif
    half3 nTS = HS2HanmenUnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), _BumpScale);
#if defined(HS2_HANMEN_V09)
    // Measured: as Cutoff, but the detail maps on plain uv0 (DetailUV and the rotators have no effect).
    half2 g1 = HS2HanmenHeightGrad(TEXTURE2D_ARGS(_DetailGlossMap, sampler_DetailGlossMap), uv, _DetailNormalMapScale * dm.r, 0.0125, 2);
    half2 g2 = HS2HanmenHeightGrad(TEXTURE2D_ARGS(_DetailGlossMap2, sampler_DetailGlossMap2), uv, _DetailNormalMapScale2 * dm.g, 0.0125, 2);
    nTS = normalize(half3(nTS.xy + g1 + g2, nTS.z));
#elif defined(HS2_HANMEN_V2) || defined(HS2_HANMEN_PH)
    // v2 copies and Pantyhose: detail height normals as Item Cutoff (forward differences at 0.0027 in detail texture space, x 2 scale x
    // mask, whiteout blends)
    const float kD = 0.0027;
    half h1x = SAMPLE_TEXTURE2D_GRAD(_DetailGlossMap, sampler_DetailGlossMap, frac(t1 + float2(kD, 0)), ddx(t1), ddy(t1)).g;
    half h1y = SAMPLE_TEXTURE2D_GRAD(_DetailGlossMap, sampler_DetailGlossMap, frac(t1 + float2(0, kD)), ddx(t1), ddy(t1)).g;
    half h2x = SAMPLE_TEXTURE2D_GRAD(_DetailGlossMap2, sampler_DetailGlossMap2, frac(t2 + float2(kD, 0)), ddx(t2), ddy(t2)).g;
    half h2y = SAMPLE_TEXTURE2D_GRAD(_DetailGlossMap2, sampler_DetailGlossMap2, frac(t2 + float2(0, kD)), ddx(t2), ddy(t2)).g;
    half q1 = 2 * _DetailNormalMapScale * dm.r, q2 = 2 * _DetailNormalMapScale2 * dm.g;
    half3 nd1 = normalize(half3(-q1 * (h1x - d1.g), -q1 * (h1y - d1.g), 1));
    half3 nd2 = normalize(half3(-q2 * (h2x - d2.g), -q2 * (h2y - d2.g), 1));
    nTS = normalize(half3(nTS.xy + nd1.xy, nTS.z * nd1.z));
    nTS = normalize(half3(nTS.xy + nd2.xy, nTS.z * nd2.z));
#elif defined(HS2_HANMEN_CUTOFF)
    // Measured: Unity's normal map (z rebuilt) plus both height gradients with gain 2 (hct_decode3.txt: 0.0000).
    half2 g1 = HS2HanmenHeightGrad(TEXTURE2D_ARGS(_DetailGlossMap, sampler_DetailGlossMap), t1, _DetailNormalMapScale * dm.r, 0.0125, 2);
    half2 g2 = HS2HanmenHeightGrad(TEXTURE2D_ARGS(_DetailGlossMap2, sampler_DetailGlossMap2), t2, _DetailNormalMapScale2 * dm.g, 0.0125, 2);
    nTS = normalize(half3(nTS.xy + g1 + g2, nTS.z));
#else
    // Measured: offset 0.0125, gain 0.5 for the first map and 1 for the second (hcr_dnfit.txt: 0.0000).
    half2 g1 = HS2HanmenHeightGrad(TEXTURE2D_ARGS(_DetailGlossMap, sampler_DetailGlossMap), t1, _DetailNormalMapScale * dm.r, 0.0125, 0.5);
    half2 g2 = HS2HanmenHeightGrad(TEXTURE2D_ARGS(_DetailGlossMap2, sampler_DetailGlossMap2), t2, _DetailNormalMapScale2 * dm.g, 0.0125, 1);
    // Measured: the normal map's z is replaced by 0.5 (normalize(xy + gradients, 0.5)), so a normal map tilts twice
    // as much as in Unity's Standard.
    nTS = normalize(half3(nTS.xy + g1 + g2, 0.5));
#endif
    // Built-in surface-shader tangent frame: interpolated vertex tangent / binormal / normal, normalized after the
    // transform; back faces keep the front normal (measured, Cull Off).
    s.normalWS = normalize(i.t.xyz * nTS.x + i.b * nTS.y + i.n * nTS.z);
    return s;
}

#if defined(HS2_HANMEN_BLEND)
// Blended alpha (Pantyhose / Transparent / True Alpha): MainTex.a with the shader's alpha terms.
half HS2HanmenBlendAlpha(HS2HanmenVaryings i)
{
    float2 uv = i.uv;
    half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a;
#if defined(HS2_HANMEN_PH) || defined(HS2_HANMEN_TA)
    float3 v = GetWorldSpaceNormalizeViewDir(i.wpos);
    half nv = saturate(dot(normalize(i.n), v));
    half fres = _FresnelBias + _FresnelScale * pow(1 - nv, _FresnelPower);
#endif
#if defined(HS2_HANMEN_PH)
    // Measured (Pantyhose): each detail map pulls the alpha towards its _DetailAlpha by mask x Luminance(detail) (Unity's
    // linear-space luminance weights; unclamped, applied in turn), then the fresnel lerps it towards 1; alpha 0 stays 0.
    half4 dm = SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, uv);
    float2 t1 = HS2HanmenRotPi(uv * _DetailUV.xy, _DetailUVRotator), t2 = HS2HanmenRotPi(uv * _DetailUV2.xy, _DetailUVRotator2);
    half3 lw = half3(0.0396819152, 0.458021790, 0.00609653955);
    half l1 = dot(SAMPLE_TEXTURE2D_GRAD(_DetailGlossMap, sampler_DetailGlossMap, frac(t1), ddx(t1), ddy(t1)).rgb, lw);
    half l2 = dot(SAMPLE_TEXTURE2D_GRAD(_DetailGlossMap2, sampler_DetailGlossMap2, frac(t2), ddx(t2), ddy(t2)).rgb, lw);
    half b = lerp(a, _DetailAlpha1, dm.r * l1);
    b = lerp(b, _DetailAlpha2, dm.g * l2);
    b = lerp(b, 1, saturate(fres));
    return a > 0 ? b : 0;
#elif defined(HS2_HANMEN_TA)
    // Measured (True Alpha): smoothstep(0, 0.75, a) x lerp(a x AlphaMaster, 1, saturate(fresnel))
    return smoothstep(0, 0.75, a) * lerp(a * _AlphaMaster, 1, saturate(fres));
#else
    return a;
#endif
}
#endif

#if defined(HS2_HANMEN_V09_BLEND)
// Blended v0.9 alpha: MainTex.a x the regions' colour alphas (weighted sum, as the colours), not saturated.
half HS2HanmenV09Alpha(float2 uv)
{
    half3 cm = SAMPLE_TEXTURE2D(_ColorMask, sampler_ColorMask, uv).rgb;
    return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a
        * ((1 - cm.r - cm.g - cm.b) * HS2_V09_C1.a + cm.r * HS2_V09_C2.a + cm.g * HS2_V09_C3.a + cm.b * HS2_V09_C4.a);
}
#endif

half4 HS2HanmenFrag(HS2HanmenVaryings i) : SV_Target
{
    HS2HanmenClip(i.uv, i.pos);
    HS2HanmenSurface hs = HS2HanmenSurf(i);
    HS2Surface s = HS2SurfaceMetallic(hs.albedo, hs.metallic, hs.smoothness, hs.occlusion, hs.normalWS);
    s.emission = hs.emission;
#if defined(HS2_HANMEN_V09_BLEND)
    HS2ShadowReceive = 0;   // Built-in forward rendering: no realtime shadows in the transparent queues
    return half4(HS2ApplyFog(HS2Shade(s, i.wpos, GetNormalizedScreenSpaceUV(i.pos)), i.wpos, _HS2FogModes), HS2HanmenV09Alpha(i.uv));
#elif defined(HS2_HANMEN_BLEND)
    HS2ShadowReceive = 0;   // Built-in forward rendering: no realtime shadows in the transparent queues
    return half4(HS2ApplyFog(HS2Shade(s, i.wpos, GetNormalizedScreenSpaceUV(i.pos)), i.wpos, _HS2FogModes), HS2HanmenBlendAlpha(i));
#else
    return half4(HS2Shade(s, i.wpos, GetNormalizedScreenSpaceUV(i.pos)), 1);
#endif
}

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
    HS2HanmenClip(i.uv, i.pos);
    return 0;
}
#endif
