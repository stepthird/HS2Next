// Shared surface + lighting for the URP replacements of HS2's Shader Forge hair shaders
// (SF_Hair07_URP.shader, SF_Hair08_URP.shader, SF_StudioHair07_URP.shader). Inferred by black-box probing
// only (Assets/HS2Next/Editor/Fit/SkinFit.cs, Tests/FitCases/hair07.txt, hair08.txt, studio_hair07.txt,
// hair_ring_profiles.txt); see the shader headers for the measured behaviour. All "A" quantities are in output units: the value the original writes under a flat white ambient
// (SH = 1) with metallic 0, which is also its response to a unit directional light along the normal.
//
// Variants (define before including):
//   HS2_HAIR_STUDIO   studio_hair_07: _Color2/_Color3/_Color4 for top/under/specular, ring always on (no ring
//                     texture, mesh colour or ColorMask_B), albedo x 1.25, no back-side |N.L| diffuse term
//   HS2_HAIR_CUTOUT   clip(alpha - 0.5) instead of the 2x2 screen door
#ifndef HS2NEXT_HAIR_INCLUDED
#define HS2NEXT_HAIR_INCLUDED
#include "HS2NextLights.hlsl"

TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);
TEXTURE2D(_BumpMap);    SAMPLER(sampler_BumpMap);
TEXTURE2D(_ColorMask);  SAMPLER(sampler_ColorMask);
TEXTURE2D(_DetailMask); SAMPLER(sampler_DetailMask);
TEXTURE2D(_Occlusion);  SAMPLER(sampler_Occlusion);
#if !defined(HS2_HAIR_STUDIO)
TEXTURE2D(_ColorMask_B);      SAMPLER(sampler_ColorMask_B);
TEXTURE2D(_Ring_onwoffb);     SAMPLER(sampler_Ring_onwoffb);
TEXTURE2D(_MeshColorMask);    SAMPLER(sampler_MeshColorMask);
TEXTURE2D(_MeshColorMask_B);  SAMPLER(sampler_MeshColorMask_B);
#endif

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST, _BumpMap_ST, _ColorMask_ST, _DetailMask_ST, _Occlusion_ST;
    float4 _Color;
#if defined(HS2_HAIR_STUDIO)
    float4 _Color2, _Color3, _Color4;
#else
    float4 _Specular, _TopColor, _UnderColor, _MeshColor_01;
    float4 _ColorMask_B_ST, _Ring_onwoffb_ST, _MeshColorMask_ST, _MeshColorMask_B_ST;
    float _ColorMask_A0B1, _Ringoff, _Ringcontrolr_onoff, _Mesh_onoff;
#endif
    float _TopGradation, _UnderGradation, _Metallic, _Smoothness, _DetailScale, _HighLight, _Rimpower, _ExGloss, _Cutoff;
CBUFFER_END

#if defined(HS2_HAIR_STUDIO)
    #define HS2_SPEC   _Color4.rgb
    #define HS2_TOP    _Color2.rgb
    #define HS2_UNDER  _Color3.rgb
#else
    #define HS2_SPEC   _Specular.rgb
    #define HS2_TOP    _TopColor.rgb
    #define HS2_UNDER  _UnderColor.rgb
#endif

// ---- measured constants (output units, see header)
static const float  kBase      = 0.7555;                         // base colour region: A = kBase * Color * (0.5 + 0.5 occ)
static const float  kTop       = 0.5069;                         // top / under colour regions
static const float3 kFloorG    = float3(0.0358, 0.0336, 0.0336); // MainTex.G adds this (times occ) ...
static const float3 kFloorB    = float3(0.0426, 0.0248, 0.0248); // ... MainTex.B this ...
static const float  kTexColor  = 0.1291;                         // ... and kTexColor * Color
static const float  kAlbToF0   = 0.4735;                         // metallic F0 = A * kAlbToF0 (= A / 2.112)
static const float  kRingDesat = 0.745;                          // lit ring *= 1 - kRingDesat * Color
static const float  kRingEnv   = 1.0;                            // ring band as indirect-specular environment

struct HS2HairAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    float2 uv         : TEXCOORD0;
#if !defined(HS2_HAIR_STUDIO)
    float2 uv1        : TEXCOORD1;   // the mesh masks' channel (Phase 4.3 UH1)
#endif
};

struct HS2HairVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv         : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float3 normalWS   : TEXCOORD2;
    float4 tangentWS  : TEXCOORD3;
#if !defined(HS2_HAIR_STUDIO)
    float2 uv1        : TEXCOORD4;
#endif
};

HS2HairVaryings HS2HairVert(HS2HairAttributes a)
{
    HS2HairVaryings o;
    VertexPositionInputs p = GetVertexPositionInputs(a.positionOS.xyz);
    VertexNormalInputs n = GetVertexNormalInputs(a.normalOS, a.tangentOS);
    o.positionCS = p.positionCS;
    o.positionWS = p.positionWS;
    o.normalWS = n.normalWS;
    o.tangentWS = float4(n.tangentWS, a.tangentOS.w * GetOddNegativeScale());
    o.uv = a.uv;
#if !defined(HS2_HAIR_STUDIO)
    o.uv1 = a.uv1;
#endif
    return o;
}

// Built-in UnpackNormal for DXT5nm-style maps: x = R * A, y = G.
float3 HS2HairUnpack(float4 p)
{
    float2 xy = float2(p.r * p.a, p.g) * 2 - 1;
    return float3(xy, sqrt(1 - saturate(dot(xy, xy))));
}

// Screen-door coverage: alpha >= 0.2 / 0.4 / 0.6 / 0.8 lights 1 / 2 / 3 / 4 pixels of each 2x2 block.
// _HS2NextDitherFrame (0-3; Runtime/Rendering/TemporalAA.cs, Phase 3.3 package S) shifts the 2x2 pattern by one pixel in
// x (bit 0) and y (bit 1) per frame while the camera renders with TAA, so the history integrates the coverage (U9); 0 for
// every other camera = the measured pattern.
float _HS2NextDitherFrame;
void HS2HairClip(float alpha, float4 positionCS)
{
#if defined(HS2_HAIR_CUTOUT)
    clip(alpha - 0.5);   // fixed threshold; _Cutoff is unused (measured)
#else
    uint frame = (uint)_HS2NextDitherFrame;
    uint2 p = (uint2)positionCS.xy + uint2(frame & 1u, (frame >> 1) & 1u);
    // pixel parity of the Built-in pattern (verified on render textures, where both pipelines flip the projection)
    uint x = p.x & 1, y = p.y & 1;
    #if UNITY_UV_STARTS_AT_TOP
        if (_ProjectionParams.x > 0) y = (p.y + 1) & 1;
    #endif
    float t = (x == 1 && y == 0) ? 0.2 : (x == 0 && y == 1) ? 0.4 : (x == 1 && y == 1) ? 0.6 : 0.8;
    clip(alpha - t + 1e-4);
#endif
}

// Lit-ring peak (Spec = 1, per channel before the (1 - kRingDesat C) factor): linear in the colour's max
// channel V (linear), capped.
float HS2HairRingPeak(float v) { return min(0.1948 + 0.3557 * v, 0.3548); }

struct HS2HairSurface
{
    float3 albedo;     // A (output units), lit ring and rim included
    float3 ringEnv;    // ring band + rim used as indirect-specular environment
    float  smoothness, metallic;
    float3 normalWS;
    float  alpha;
};

HS2HairSurface HS2HairSurfaceFrom(HS2HairVaryings i, bool front)
{
    HS2HairSurface s;
    float2 uv = i.uv;
    float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(uv, _MainTex));
    s.alpha = tex.a;

    // Normal: lerp(BumpMap, DetailMask (uv x 16), _DetailScale) in tangent space; two-sided.
    float3 nB = HS2HairUnpack(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, TRANSFORM_TEX(uv, _BumpMap)));
    float3 nD = HS2HairUnpack(SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, TRANSFORM_TEX(uv, _DetailMask) * 16));
    float3 nTS = normalize(lerp(nB, nD, _DetailScale));
    // Back faces flip the normal only; tangent and bitangent stay those of the front face (measured on the ring).
    float3 nv0 = normalize(i.normalWS);
    float3 t0 = normalize(i.tangentWS.xyz);
    float3 b0 = cross(nv0, t0) * i.tangentWS.w;
    float3 n0 = nv0 * (front ? 1 : -1);
    float3 n = normalize(nTS.x * t0 + nTS.y * b0 + nTS.z * n0);
    s.normalWS = n;

    // Colour regions: mask B = top (TopColor), G = under (UnderColor), weights pow(mask, gradation), summed.
#if defined(HS2_HAIR_STUDIO)
    float3 mask = SAMPLE_TEXTURE2D(_ColorMask, sampler_ColorMask, TRANSFORM_TEX(uv, _ColorMask)).rgb;
    float3 c = _Color.rgb;
#else
    // _ColorMask_A0B1 = 1: _ColorMask_B recolours the base region instead (saturate(C + g (Under - C) + b (Top - C)),
    // no region shading); in between: approximate blend of both readings.
    float3 mask = SAMPLE_TEXTURE2D(_ColorMask, sampler_ColorMask, TRANSFORM_TEX(uv, _ColorMask)).rgb * (1 - _ColorMask_A0B1);
    float3 maskB = SAMPLE_TEXTURE2D(_ColorMask_B, sampler_ColorMask_B, TRANSFORM_TEX(uv, _ColorMask_B)).rgb;
    // Mesh pattern (Phase 4.3 UH1, Spike/out/fit/Shader_Forge_hair_07_mesh.txt): weight = _Mesh_onoff x the R channels of
    // both mesh masks, each sampled on the SECOND UV channel (uv1) with its own scale / offset (the game writes
    // _MeshColorMask_B and its ST); wrap from the texture; null masks: _MeshColorMask_B black (none), _MeshColorMask white.
    // A mesh without a uv1 channel (most back / side hairs) feeds uv0 there, in both pipelines alike (uh1_nouv1*).
    float meshW = _Mesh_onoff * SAMPLE_TEXTURE2D(_MeshColorMask_B, sampler_MeshColorMask_B, TRANSFORM_TEX(i.uv1, _MeshColorMask_B)).r
                * SAMPLE_TEXTURE2D(_MeshColorMask, sampler_MeshColorMask, TRANSFORM_TEX(i.uv1, _MeshColorMask)).r;
    float3 c = lerp(_Color.rgb, _MeshColor_01.rgb, saturate(meshW));
    c = saturate(c + _ColorMask_A0B1 * (maskB.g * (HS2_UNDER - c) + maskB.b * (HS2_TOP - c)));
#endif
    float wT = pow(max(mask.b, 1e-6), _TopGradation) * step(1e-6, mask.b);
    float wU = pow(max(mask.g, 1e-6), _UnderGradation) * step(1e-6, mask.g);
    float m = wT + wU;
    float3 col = lerp(lerp(c, HS2_TOP, wT), HS2_UNDER, wU);
    float3 occ = SAMPLE_TEXTURE2D(_Occlusion, sampler_Occlusion, TRANSFORM_TEX(uv, _Occlusion)).rgb;
    float3 o = lerp(occ, 1, m);

    float3 texBoost = lerp(lerp(0, kFloorB + kTexColor * c, tex.b), kFloorG + kTexColor * c, tex.g);
    float3 base = kBase * c * (0.5 + 0.5 * o) + o * texBoost;

    // Ring ("angel ring"): band b = 1 - 2|V.y - N.y| (world up, N bumped); the lit part (added to the
    // base colour) goes as b^max(2.7, HL + 3 - 6V), the indirect-specular environment as b^max(2.7, HL + 1 - 4V),
    // V = max channel of the colour.
    float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
    float b = saturate(1 - 2 * abs(v.y - n.y));
    float vmax = max(c.r, max(c.g, c.b));
    float bandLit = pow(b, max(2.7, _HighLight + 3 - 6 * vmax));
    float bandEnv = pow(b, max(2.7, _HighLight + 1 - 4 * vmax));
#if defined(HS2_HAIR_STUDIO)
    float3 ringW = 1;
#else
    float3 ringW = (1 - _Ringoff) * SAMPLE_TEXTURE2D(_Ring_onwoffb, sampler_Ring_onwoffb, TRANSFORM_TEX(uv, _Ring_onwoffb)).rgb;
#endif
    // Rim: 2 pow(1 - N.V, _Rimpower) joins both ring terms (so it is lit like the ring and reflects like its band).
    float rimP = 2 * pow(saturate(1 - saturate(dot(n, v))), _Rimpower) * (1 - _ExGloss);
    base += saturate(ringW * bandLit + rimP) * HS2_SPEC * HS2HairRingPeak(vmax) * (1 - kRingDesat * c);
    s.ringEnv = saturate(ringW * bandEnv * kRingEnv + rimP);

    s.albedo = max(0, (1 - m) * base + m * kTop * col * o);
#if defined(HS2_HAIR_STUDIO)
    s.albedo *= 1.25;   // studio_hair_07: every albedo term x 1.25 (measured)
#endif
    // Wetness (_ExGloss): metallic >= 0.5 w, smoothness >= 0.8 w, the rest as without wetness (approximate).
    s.metallic = max(_Metallic, 0.5 * _ExGloss);
    s.smoothness = max(_Smoothness, 0.8 * _ExGloss);
    return s;
}

half HS2HairDisney(half nv, half nl, half lh, half pr)
{
    half fd90 = 0.5 + 2 * lh * lh * pr;
    return (1 + (fd90 - 1) * pow(1 - nl, 5)) * (1 + (fd90 - 1) * pow(1 - nv, 5));
}

// One light: wrapped two-sided diffuse (0.5 Disney sat(NL) + 0.5 |NL| + 0.1) / 1.1 and Built-in GGX.
float3 HS2HairDirect(HS2HairSurface s, float3 diffColor, float3 f0, float3 v, float3 l, float3 lightColor)
{
    float3 n = s.normalWS;
    float3 h = SafeNormalize(l + v);
    float nlRaw = dot(n, l);
    float nv = abs(dot(n, v));
    float nl = saturate(nlRaw);
    float nh = saturate(dot(n, h));
    float lh = saturate(dot(l, h));
    float pr = 1 - s.smoothness;
#if defined(HS2_HAIR_STUDIO)
    float f = (0.5 * HS2HairDisney(nv, nl, lh, pr) * nl + 0.1) / 1.1;   // no back-side |N.L| term (measured)
#else
    float f = (0.5 * HS2HairDisney(nv, nl, lh, pr) * nl + 0.5 * abs(nlRaw) + 0.1) / 1.1;
#endif
    pr = 1 - s.smoothness * nv * nv;   // the lobe widens at grazing view: smoothness x (N.V)^2 (measured)
    float a = pr * pr;                  // no 0.002 clamp: the lobe vanishes at smoothness 1 (measured)
    float lv = nl * (nv * (1 - a) + a);
    float ll = nv * (nl * (1 - a) + a);
    float vis = 0.5 / (lv + ll + 1e-5);
    float a2 = a * a;
    float d = (nh * a2 - nh) * nh + 1.0;
    float D = INV_PI * a2 / (d * d + 1e-7);
    float spec = max(0, vis * D * PI * nl);
    float3 F = f0 + (1 - f0) * pow(1 - lh, 5);
    return (diffColor * f + spec * F) * lightColor;
}

float3 HS2HairShade(HS2HairSurface s, float3 positionWS, float2 screenUV)
{
    float3 v = GetWorldSpaceNormalizeViewDir(positionWS);
    float3 n = s.normalWS;
    float nv = abs(dot(n, v));
    // metallic fades out at grazing view: x saturate(2 N.V - 0.1) (measured on the diffuse and F0)
    float metallic = s.metallic * saturate(2 * nv - 0.1);
    float3 diffColor = s.albedo * (1 - metallic);
    float3 f0 = lerp(0.04, s.albedo * kAlbToF0, metallic);
    float pr = 1 - s.smoothness;
    float a = max(pr * pr, 0.002);

    // Indirect: SH diffuse; specular = (ring band + rim + reflection probe) * surfaceReduction * F0.
    float3 c = diffColor * SampleSH(n);
    float3 env = s.ringEnv + GlossyEnvironmentReflection(reflect(-v, n), positionWS, pr, 1, screenUV);
    c += env * (1.0 / (a * a + 1.0)) * f0;   // no Fresnel lerp toward grazing (measured on the rim)

    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    Light main = GetMainLight(shadowCoord);
    main.color *= HS2LightLayerWeight(main.layerMask);
    c += HS2HairDirect(s, diffColor, f0, v, main.direction, main.color * main.distanceAttenuation * main.shadowAttenuation);
#if defined(_ADDITIONAL_LIGHTS)
    HS2LightIter it = HS2LightIterInit(positionWS, screenUV);
    uint lightIndex;
    [loop] while (HS2LightNext(it, lightIndex))
    {
        Light L = GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1));
        if (!HS2LightReaches(L, lightIndex)) continue;
        c += HS2HairDirect(s, diffColor, f0, v, L.direction, L.color * L.distanceAttenuation * L.shadowAttenuation);
    }
#endif
    return c;
}

// ---- passes
half4 HS2HairFrag(HS2HairVaryings i, bool front : SV_IsFrontFace) : SV_Target
{
    HS2HairSurface s = HS2HairSurfaceFrom(i, front);
    HS2HairClip(s.alpha, i.positionCS);
    float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
    return half4(HS2HairShade(s, i.positionWS, screenUV), 1);
}

float3 _LightDirection;
float3 _LightPosition;

HS2HairVaryings HS2HairShadowVert(HS2HairAttributes a)
{
    HS2HairVaryings o = HS2HairVert(a);
#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDir = normalize(_LightPosition - o.positionWS);
#else
    float3 lightDir = _LightDirection;
#endif
    float4 cs = TransformWorldToHClip(ApplyShadowBias(o.positionWS, o.normalWS, lightDir));
#if UNITY_REVERSED_Z
    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
#else
    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
#endif
    o.positionCS = cs;
    return o;
}

half4 HS2HairShadowFrag(HS2HairVaryings i) : SV_Target
{
    float a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(i.uv, _MainTex)).a;
    HS2HairClip(a, i.positionCS);
    return 0;
}

half4 HS2HairDepthFrag(HS2HairVaryings i) : SV_Target
{
    float a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(i.uv, _MainTex)).a;
    HS2HairClip(a, i.positionCS);
    return 0;
}
#endif
