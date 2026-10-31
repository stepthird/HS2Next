// Surface of the "v3" Hanmen clothes copies (Phase 3.7 package F2r2): GDC/Clothes True Cutoff (82 properties), evaluated
// with the Built-in BRDF1 of HS2NextBRDF.hlsl and the shared light loop. Inferred by black-box probing only
// (Tests/FitCases/hv3_*.txt). The lighting follows the measurements of package F1 on Hanmen/Item Cutoff
// (HS2NextHanmenItem.hlsl: N.V saturated, Amplify translucency; here the occlusion stays off the direct specular); the
// surface inputs are the composited clothes inputs. The 81-property copy of Hanmen/Clothes True Cutoff (ROC clothes) has
// the same property set without _TransmissionStrength but shades differently (probed; not mapped).
//
// Measured (flat probe values exact to the 4th decimal unless noted):
//   albedo     = MainTex.rgb * BaseColor.rgb (no _Color; _MainTex tiling unused)
//   metallic   = lerp(DetailMainTex.R lerp(1, MGM.R, MetallicMask), Metallic4 lerp(1, MGM.R, MetallicMask4), ColorMask.B)
//                x detail factor; smoothness the same with DetailMainTex.B, Glossiness4, Roughness / Roughness4
//   details    factor 1 - sum_k scale_k mask_k (1 - D_k.r) for metallic / smoothness, occlusion with smoothstep(0, 0.8,
//              D_k.r); masks DetailMask R / G; UV frac(rotate(uv * UVScalePattern.xy * DetailUV.xy, -pi rotator))
//   occlusion  = lerp(1, Occlusion.G, OcculusionStrength) x detail occlusion
//   emission   = EmissionColor.rgb * EmissionStrength * MGM.G
//   translucency (direct only) albedo * Translucency * MGM.B * TransDirect * pow(saturate(V . -(L + N d)), s) * light;
//              transmission (no keyword) albedo * TransmissionStrength * saturate(-N.L) * light
//   alpha      _DITHERSWITCH_ON: _Cutoff > 0 switches the 8x8 ordered dither of MainTex.a on; otherwise clip(a - Cutoff);
//              tearing (only while _Cutoff > 0) keeps Occlusion.B >= a threshold of AlphaEx (0.925 at AlphaEx <= 0;
//              approximate, see below)
//   back faces Cull Off; _NormalBackDirInvert 1 flips the normal on back faces
#ifndef HS2NEXT_HANMEN_V3_INCLUDED
#define HS2NEXT_HANMEN_V3_INCLUDED
#include "HS2NextBRDF.hlsl"

TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
TEXTURE2D(_DetailMainTex); SAMPLER(sampler_DetailMainTex);
TEXTURE2D(_ColorMask); SAMPLER(sampler_ColorMask);
TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_DetailMask); SAMPLER(sampler_DetailMask);
TEXTURE2D(_DetailGlossMap); SAMPLER(sampler_DetailGlossMap);
TEXTURE2D(_DetailGlossMap2); SAMPLER(sampler_DetailGlossMap2);
TEXTURE2D(_Noise); SAMPLER(sampler_Noise);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseColor, _Color, _EmissionColor, _UVScalePattern, _DetailUV, _DetailUV2, _BodyColor1, _WetColor;
    float4 _WeatheringAlbedo, _WeatheringUV, _patternuvbase, _WetUV, _WetStreaksUV;
    float _Cutoff, _AlphaEx, _CarvatureStrength, _Float0, _DetailNormalMapScale, _DetailNormalMapScale2;
    float _DetailGlossScale, _DetailGlossScale2, _DetailMetallicScale, _DetailMetallicScale2;
    float _DetailOcculusionScale, _DetailOcculusionScale2, _DetailUVRotator, _DetailUV2Rotator;
    float _EmissionStrength, _EmissionColor2, _EmissionColor3, _EmissionColor4, _OcculusionStrength, _AlphaMaster;
    float _Roughness, _MetallicMask, _Glossiness4, _Roughness4, _Metallic4, _MetallicMask4;
    float _WeatheringAll, _WeatheringRange1, _WeatheringRange2, _WeatheringRange3, _WeatheringRange4, _WeatheringRange5;
    float _WeatheringGloss, _WeatheringEmission, _WeatheringUVSeed, _ExGloss, _WetGlossBase, _WetAlbedoOffset;
    float _WetSplatsAlbedoOffset, _WetAlpha, _WetOpacitySplats, _WetOpacityStreaks, _WetGlossSplats, _WetBumpOffset;
    float _DitherSwitch, _DitherBlueNoise, _NormalBackDirInvert, _Translucency, _TransmissionStrength;
    float _TransNormalDistortion, _TransScattering, _TransDirect, _TransAmbient, _TransShadow;
CBUFFER_END

struct HS2HmV3Attributes { float4 pos : POSITION; float3 n : NORMAL; float4 t : TANGENT; float2 uv : TEXCOORD0; };
struct HS2HmV3Varyings
{
    float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1;
    float3 n : TEXCOORD2; float4 t : TEXCOORD3; float3 b : TEXCOORD4;
};

HS2HmV3Varyings HS2HmV3Vert(HS2HmV3Attributes a)
{
    HS2HmV3Varyings o;
    VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
    VertexNormalInputs nn = GetVertexNormalInputs(a.n, a.t);
    o.pos = p.positionCS; o.wpos = p.positionWS;
    o.n = nn.normalWS; o.t = float4(nn.tangentWS, a.t.w * GetOddNegativeScale());
    o.b = cross(o.n, o.t.xyz) * o.t.w;   // Built-in surface shaders build the binormal per vertex
    o.uv = a.uv;
    return o;
}

// Built-in UnpackScaleNormal: xy scaled before z is rebuilt.
half3 HS2HmV3UnpackNormal(half4 packed, half scale)
{
    packed.x *= packed.w;
    half3 n;
    n.xy = (packed.xy * 2 - 1) * scale;
    n.z = sqrt(1 - saturate(dot(n.xy, n.xy)));
    return n;
}

// 8x8 ordered dither threshold j / 64 (j = 1..64; Amplify's matrix as in HS2NextHanmen.hlsl); y from the bottom.
float HS2HmV3Bayer8(float4 positionCS)
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

float2 HS2HmV3Rotate(float2 t, float rotator)
{
    t -= 0.5;
    float sn, cs; sincos(-PI * rotator, sn, cs);
    return float2(cs * t.x - sn * t.y, sn * t.x + cs * t.y) + 0.5;
}

// Tearing: a pixel stays where Occlusion.B >= threshold(AlphaEx). Measured brackets (hv3_decode3.txt): AlphaEx <= 0 ->
// (0.90, 0.95], 0.25 -> (0.85, 0.90], 0.5 -> (0.75, 0.76], 0.75 -> (0.5, 0.6], 0.9 -> (0.3, 0.4], 1 -> 0; piecewise linear
// through points inside the brackets (approximate between them).
float HS2HmV3TearThreshold(float ae)
{
    ae = saturate(ae);
    if (ae < 0.25) return lerp(0.925, 0.875, ae / 0.25);
    if (ae < 0.5) return lerp(0.875, 0.755, (ae - 0.25) / 0.25);
    if (ae < 0.75) return lerp(0.755, 0.55, (ae - 0.5) / 0.25);
    if (ae < 0.9) return lerp(0.55, 0.35, (ae - 0.75) / 0.15);
    return lerp(0.35, 0, (ae - 0.9) / 0.1);
}

// Alpha / coverage test shared by the colour, shadow and depth passes.
half HS2HmV3Alpha(float2 uv, float4 positionCS)
{
    half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a;
    // measured: the tearing test runs only while _Cutoff > 0 (as the dither switch)
    if (_Cutoff > 0) clip(SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv).b - HS2HmV3TearThreshold(_AlphaEx));
#if defined(_DITHERSWITCH_ON) && defined(_DITHERBLUENOISE_ON)
    // blue-noise variant (approximate: the original's screen mapping of _Noise was not decoded; with the default white
    // texture only alpha 1 stays, as in the original)
    if (_Cutoff > 0) clip(a - SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv).a - 0.0001);
#elif defined(_DITHERSWITCH_ON)
    if (_Cutoff > 0) clip(a - HS2HmV3Bayer8(positionCS));
#else
    clip(a - _Cutoff);
#endif
    return a;
}

half4 HS2HmV3Detail(TEXTURE2D_PARAM(tex, smp), float2 t)
{
    return SAMPLE_TEXTURE2D_GRAD(tex, smp, frac(t), ddx(t), ddy(t));
}

struct HS2HmV3Surface { half3 albedo; half metallic, smoothness, occlusion, thickness; half3 emission; float3 normalWS; };

HS2HmV3Surface HS2HmV3Surf(HS2HmV3Varyings i, bool front)
{
    HS2HmV3Surface s;
    float2 uv = i.uv;
    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
    half4 oc = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv);
    half4 mg = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv);
    half4 dm = SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, uv);
    half4 dmt = SAMPLE_TEXTURE2D(_DetailMainTex, sampler_DetailMainTex, uv);
    half m4 = SAMPLE_TEXTURE2D(_ColorMask, sampler_ColorMask, uv).b;
    float2 duv = uv * _UVScalePattern.xy;
    float2 t1 = HS2HmV3Rotate(duv * _DetailUV.xy, _DetailUVRotator), t2 = HS2HmV3Rotate(duv * _DetailUV2.xy, _DetailUV2Rotator);
    half4 d1 = HS2HmV3Detail(TEXTURE2D_ARGS(_DetailGlossMap, sampler_DetailGlossMap), t1);
    half4 d2 = HS2HmV3Detail(TEXTURE2D_ARGS(_DetailGlossMap2, sampler_DetailGlossMap2), t2);

    half m1 = dm.r, m2 = dm.g;
    half metalF = saturate(1 - _DetailMetallicScale * m1 * (1 - d1.r) - _DetailMetallicScale2 * m2 * (1 - d2.r));
    half glossF = saturate(1 - _DetailGlossScale * m1 * (1 - d1.r) - _DetailGlossScale2 * m2 * (1 - d2.r));
    half occF = saturate(1 - _DetailOcculusionScale * m1 * (1 - smoothstep(0, 0.8, d1.r))
                           - _DetailOcculusionScale2 * m2 * (1 - smoothstep(0, 0.8, d2.r)));

    s.albedo = tex.rgb * _BaseColor.rgb;
    s.metallic = lerp(dmt.r * lerp(1, mg.r, _MetallicMask), _Metallic4 * lerp(1, mg.r, _MetallicMask4), m4) * metalF;
    s.smoothness = lerp(dmt.b * lerp(1, mg.r, _Roughness), _Glossiness4 * lerp(1, mg.r, _Roughness4), m4) * glossF;
    // wetness (game-driven _ExGloss; measured with the splat / streak inputs at their defaults 0): albedo towards
    // WetAlbedoOffset, smoothness towards WetGlossBase
    s.albedo *= lerp(1, _WetAlbedoOffset, _ExGloss);
    s.smoothness = lerp(s.smoothness, _WetGlossBase, _ExGloss);
    s.occlusion = lerp(1, oc.g, _OcculusionStrength) * occF;
    s.thickness = mg.b;
    s.emission = _EmissionColor.rgb * _EmissionStrength * mg.g;

    // Normal map (UnpackScaleNormal, _Float0) plus the detail height maps (G, forward differences in detail texture
    // space), blended like BlendNormals.
    const float kDelta = 0.0027;
    half h1 = d1.g, h2 = d2.g;
    half h1x = HS2HmV3Detail(TEXTURE2D_ARGS(_DetailGlossMap, sampler_DetailGlossMap), t1 + float2(kDelta, 0)).g;
    half h1y = HS2HmV3Detail(TEXTURE2D_ARGS(_DetailGlossMap, sampler_DetailGlossMap), t1 + float2(0, kDelta)).g;
    half h2x = HS2HmV3Detail(TEXTURE2D_ARGS(_DetailGlossMap2, sampler_DetailGlossMap2), t2 + float2(kDelta, 0)).g;
    half h2y = HS2HmV3Detail(TEXTURE2D_ARGS(_DetailGlossMap2, sampler_DetailGlossMap2), t2 + float2(0, kDelta)).g;
    half s1 = 2 * _DetailNormalMapScale * m1, s2 = 2 * _DetailNormalMapScale2 * m2;
    half3 nd1 = normalize(half3(-s1 * (h1x - h1), -s1 * (h1y - h1), 1));
    half3 nd2 = normalize(half3(-s2 * (h2x - h2), -s2 * (h2y - h2), 1));
    half3 nTS = HS2HmV3UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), _Float0);
    nTS = normalize(half3(nTS.xy + nd1.xy, nTS.z * nd1.z));
    nTS = normalize(half3(nTS.xy + nd2.xy, nTS.z * nd2.z));
    float3 n = normalize(i.t.xyz * nTS.x + i.b * nTS.y + i.n * nTS.z);
    s.normalWS = (!front && _NormalBackDirInvert > 0.5) ? -n : n;
    return s;
}

// Direct light of BRDF1 with N.V saturated (the occlusion does not reach the direct specular here, unlike Item Cutoff).
half3 HS2HmV3Direct(HS2Surface s, float3 v, float3 l, half3 lightColor)
{
    float3 n = s.normalWS;
    float3 h = SafeNormalize(l + v);
    half nv = saturate(dot(n, v));
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
    return s.albedo * s.oneMinusReflectivity * lightColor * diffuseTerm + spec * lightColor * F;
}

half3 HS2HmV3Trans(HS2Surface s, float3 v, float3 l, half3 lightColor)
{
    float3 tl = l + s.normalWS * _TransNormalDistortion;
    half vdl = pow(saturate(dot(v, -tl)), _TransScattering);
    return vdl * _TransDirect * lightColor;
}

half3 HS2HmV3Shade(HS2Surface s, half thickness, float3 positionWS, float2 screenUV)
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
    half3 giDiffuse = SampleSH(n) * s.occlusion;
    half3 env = GlossyEnvironmentReflection(reflect(-v, n), positionWS, pr, s.occlusion, screenUV);
    half nv = saturate(dot(n, v));
    half surfaceReduction = 1.0 / (a * a + 1.0);
    half grazing = saturate(s.smoothness + (1 - s.oneMinusReflectivity));
    half3 fresnelLerp = lerp(s.specColor, grazing.xxx, pow(1 - nv, 5));
    half3 c = diffColor * giDiffuse + surfaceReduction * env * fresnelLerp;

    half3 mainColor = main.color * main.distanceAttenuation * main.shadowAttenuation;
    c += HS2HmV3Direct(s, v, main.direction, mainColor);
    c += s.albedo * _TransmissionStrength * saturate(-dot(n, main.direction)) * mainColor;
    half3 trans = HS2HmV3Trans(s, v, main.direction, main.color * main.distanceAttenuation * lerp(1, main.shadowAttenuation, _TransShadow));
#if defined(_ADDITIONAL_LIGHTS)
    HS2LightIter it = HS2LightIterInit(positionWS, screenUV);
    uint lightIndex;
    [loop] while (HS2LightNext(it, lightIndex))
    {
        Light L = GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1));
        if (!HS2LightReaches(L, lightIndex)) continue;
        half3 lc = L.color * L.distanceAttenuation;
        L.shadowAttenuation = lerp(1, L.shadowAttenuation, HS2ShadowReceive);
        c += HS2HmV3Direct(s, v, L.direction, lc * L.shadowAttenuation);
        c += s.albedo * _TransmissionStrength * saturate(-dot(n, L.direction)) * lc * L.shadowAttenuation;
        trans += HS2HmV3Trans(s, v, L.direction, lc * lerp(1, L.shadowAttenuation, _TransShadow));
    }
#endif
    c += s.albedo * _Translucency * thickness * trans;
    return c;
}

#if defined(HS2_HV3_FORWARD)
half4 HS2HmV3Frag(HS2HmV3Varyings i, bool front : SV_IsFrontFace) : SV_Target
{
    HS2HmV3Alpha(i.uv, i.pos);
    HS2ShadowReceive = _RECEIVE_SHADOWS_OFF ? 0 : 1;   // set by ShaderMap.Swap for queues above 2500
    HS2HmV3Surface hs = HS2HmV3Surf(i, front);
    HS2Surface s = HS2SurfaceMetallic(hs.albedo, hs.metallic, hs.smoothness, hs.occlusion, hs.normalWS);
    half3 c = HS2HmV3Shade(s, hs.thickness, i.wpos, GetNormalizedScreenSpaceUV(i.pos));
    return half4(c + hs.emission, 1);
}
#endif

float3 _LightDirection;
float3 _LightPosition;

HS2HmV3Varyings HS2HmV3ShadowVert(HS2HmV3Attributes a)
{
    HS2HmV3Varyings o = HS2HmV3Vert(a);
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

half4 HS2HmV3DepthFrag(HS2HmV3Varyings i) : SV_Target
{
    HS2HmV3Alpha(i.uv, i.pos);
    return 0;
}
#endif
