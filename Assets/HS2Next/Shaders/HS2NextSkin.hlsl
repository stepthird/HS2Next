// Shared surface code for the URP replacements of HS2's skin shaders "AIT/Skin True" (body,
// HS2_SKIN_BODY) and "AIT/Skin True Face" (face, HS2_SKIN_FACE). Every formula and constant
// below was inferred by black-box probing of the originals (Assets/HS2Next/Editor/Fit/SkinFit.cs,
// outputs in Spike/out/fit/AIT_Skin_True*_sweep*.txt); see the shader headers for the summary.
#ifndef HS2NEXT_SKIN_INCLUDED
#define HS2NEXT_SKIN_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_MainTex);        SAMPLER(sampler_MainTex);
TEXTURE2D(_DetailMainTex);  SAMPLER(sampler_DetailMainTex);
TEXTURE2D(_OcclusionMap);   SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_BumpMap);        SAMPLER(sampler_BumpMap);
TEXTURE2D(_BumpMap2);       SAMPLER(sampler_BumpMap2);
TEXTURE2D(_NailMask);       SAMPLER(sampler_NailMask);
TEXTURE2D(_DetailGlossMap); SAMPLER(sampler_DetailGlossMap);
TEXTURE2D(_Texture2);       SAMPLER(sampler_Texture2);
TEXTURE2D(_Texture3);       SAMPLER(sampler_Texture3);
TEXTURE2D(_Texture5);       SAMPLER(sampler_Texture5);
TEXTURE2D(_AlphaMask);      SAMPLER(sampler_AlphaMask);
TEXTURE2D(_AlphaMask2);     SAMPLER(sampler_AlphaMask2);

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST, _DetailMainTex_ST, _OcclusionMap_ST, _BumpMap_ST, _BumpMap2_ST, _NailMask_ST;
    float4 _DetailGlossMap_ST, _Texture2_ST, _Texture3_ST, _Texture5_ST, _AlphaMask_ST, _AlphaMask2_ST;
    float4 _Color, _Color2, _Color3, _Color4, _Color5, _Color13;
    float4 _FresnelSetting, _Texture3UV, _Texture5UV;
    float _Metallic, _Gloss, _OcclusionStrength, _DetailMainScale, _BumpScale, _BumpScale2, _Riality;
    float _NipGloss, _NailGloss, _ExGloss, _DetailNormalMapScale, _NipScale, _Texture3Rotator, _Texture4Scale;
    float _Translucency, _TransNormalDistortion, _TransScattering, _TransDirect, _TransAmbient, _TransShadow;
    float _Cutoff, _alpha_a, _alpha_b, _alpha_c, _alpha_d;
CBUFFER_END

// Measured constants (linear).
#define HS2_TUNE_DARK   0.03                  // albedo *= 1 - 0.03 * k            (k = OcclusionMap.B * _BumpScale2)
#define HS2_TUNE_SAT    0.124                 // albedo *= lerp(1, c / max(c), 0.124 * k)
#define HS2_TUNE_OCC    0.08                  // occlusion -= 0.08 * k * _OcclusionStrength
#define HS2_TUNE_TRANS  0.15                  // translucency colour += 0.15 * k * hue
#define HS2_BROW_GLOSS  0.15                  // eyebrow / underhair smoothness
#define HS2_NIP_COLOR   0.7                   // nipple colour = _Color2 * Texture2.R * 0.7
#define HS2_BEARD_BUMP  1.5                   // beard bump: tangent xy = (G - G(+offset)) * 1.5 * _Color5.a
#define HS2_BEARD_DUV   0.0055                // beard bump: forward-difference offset in texture UV

// Alpha masks (body): clothes cut-outs. Measured: each enabled mask channel is compared with the
// cutoff separately (min, not product); _alpha_c ("torn") re-shows the A mask but not AlphaMask2.
half HS2SkinAlpha(float2 uv)
{
#if defined(HS2_SKIN_BODY)
    half4 m1 = SAMPLE_TEXTURE2D(_AlphaMask, sampler_AlphaMask, TRANSFORM_TEX(uv, _AlphaMask));
    half4 m2 = SAMPLE_TEXTURE2D(_AlphaMask2, sampler_AlphaMask2, TRANSFORM_TEX(uv, _AlphaMask2));
    half a = min(lerp(1, m1.r, _alpha_a), lerp(1, m1.g, _alpha_b));
    a = max(a, _alpha_c);
    return min(a, lerp(1, m2.r, _alpha_d));
#else
    return 1;
#endif
}

// Eyebrow (face) / underhair (body) UV: on UV1, translate, rotate by _Texture3Rotator * pi about
// (0.3, 0.4), then scale by _Texture3UV.zw about (0.1, 0.43). Pivots measured with ramp textures.
float2 HS2BrowUV(float2 uv1)
{
    float2 p = uv1 + _Texture3UV.xy - float2(0.3, 0.4);
    float sn, cs; sincos(_Texture3Rotator * PI, sn, cs);
    p = float2(cs * p.x + sn * p.y, -sn * p.x + cs * p.y) + float2(0.3, 0.4);
    return (p - float2(0.1, 0.43)) * _Texture3UV.zw + float2(0.1, 0.43);
}

// Beard UV: on UV0, mirrored about the face centre line: (|x u + z - 0.5|, y v + w) with
// _Texture5UV = (x, y, z, w); _Texture5_ST is ignored. The game's (1.9, 2, -0.45, 0) gives
// (1.9 |u - 0.5|, 2 v): the beard texture holds one half of the face. Measured with ramp textures.
#if defined(HS2_SKIN_FACE)
float2 HS2BeardUV(float2 uv0)
{
    return float2(abs(_Texture5UV.x * uv0.x + _Texture5UV.z - 0.5), _Texture5UV.y * uv0.y + _Texture5UV.w);
}
#endif

// Nipple UV: on UV1, radial warp about (0.5, 0.5): p' = c + d * (1 + b * min(|d|, 0.25)), b = 16 - 16.8 * _NipScale,
// then saturated per component (Phase 4.2 UB1, ramp probes: the original clamps in the shader, not by a sampler state -
// a Repeat texture sampled at the edge averages its first and last texels - and does not mask the weight; without the
// clamp the Repeat nipple textures repeated over the chest). The radial factor stops growing at |d| = 0.25.
float2 HS2NipUV(float2 uv1)
{
    float2 d = uv1 - 0.5;
    return saturate(0.5 + d * (1 + (16 - 16.8 * _NipScale) * min(length(d), 0.25)));
}

// Built-in UnpackScaleNormal: xy scaled BEFORE z is reconstructed (URP's UnpackNormalScale scales
// after, which tilts scaled normals more). Handles RG/AG (DXT5nm) packing like Built-in.
half3 HS2UnpackNormalScale(half4 p, half scale)
{
    p.x *= p.w;
    half2 xy = (p.xy * 2 - 1) * scale;
    return half3(xy, sqrt(1 - saturate(dot(xy, xy))));
}

struct HS2SkinInputs
{
    half3 albedo, transColor;
    half metallic, smoothness, occlusion, liftMask;
    half3 normalTS;
};

HS2SkinInputs HS2SkinSurface(float2 uv0, float2 uv1, half4 vcol)
{
    HS2SkinInputs o;
    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(uv0, _MainTex));
    half4 dm = SAMPLE_TEXTURE2D(_DetailMainTex, sampler_DetailMainTex, TRANSFORM_TEX(uv0, _DetailMainTex));
    half4 oc = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, TRANSFORM_TEX(uv0, _OcclusionMap));
    half4 nm = SAMPLE_TEXTURE2D(_NailMask, sampler_NailMask, TRANSFORM_TEX(uv0, _NailMask));
    half4 dg = SAMPLE_TEXTURE2D(_DetailGlossMap, sampler_DetailGlossMap, TRANSFORM_TEX(uv0, _DetailGlossMap));

    // Base colour, nail/lip tint (NailMask.R, multiplied), blush / hip redness (NailMask.B, replaced).
    half3 c = tex.rgb * _Color.rgb;
    c = lerp(c, c * _Color13.rgb, nm.r * _Color13.a);
    c = lerp(c, _Color4.rgb, saturate(nm.b * _Texture4Scale * _Color4.a));

    // "Tuning" driven by OcclusionMap.B * _BumpScale2: darkens slightly, raises saturation, adds a
    // hue-coloured term to the translucency colour, lowers ambient occlusion.
    half k = oc.b * _BumpScale2;
    half mx = max(max(c.r, c.g), max(c.b, 1e-4));
    half3 hue = lerp(1, c / mx, HS2_TUNE_SAT * k);
    half3 T = c * (1 - HS2_TUNE_DARK * k) * hue;
    o.transColor = (T + HS2_TUNE_TRANS * k * (c / mx) * hue) * nm.a;   // NailMask.A = translucency mask
    o.albedo = T;

    // Smoothness: (_Gloss * DetailGlossMap.A (masked by NailMask.G), or _ExGloss) * OcclusionMap.R.
    half gloss = max(_Gloss * lerp(1, dg.a, nm.g), _ExGloss) * oc.r;

    // Eyebrow (face) / underhair (body): weight Texture3.B * _Color3.a * (1 - vertex colour B).
    half4 t3 = SAMPLE_TEXTURE2D(_Texture3, sampler_Texture3, HS2BrowUV(uv1));
    half wb = saturate(t3.b * _Color3.a * (1 - vcol.b));
    o.albedo = lerp(o.albedo, _Color3.rgb * t3.b, wb);
    // Fitted cap; exact at gloss <= 0.6, highlights at gloss 0.9 on partial brow pixels are off by up to ~15%.
    gloss = min(gloss + HS2_BROW_GLOSS * wb, lerp(0.99, HS2_BROW_GLOSS, wb));

    half s = max(gloss, dm.b);   // DetailMainTex.B: painted gloss (not masked by OcclusionMap.R)
#if defined(HS2_SKIN_FACE)
    // Beard (_Texture5.R): replaces colour, smoothness at least 0.5. Weight
    // saturate(2a) * pow(R, 4 - 3a) with a = _Color5.a (a = 1: R; a = 0.5: R^2.5).
    half4 t5 = SAMPLE_TEXTURE2D(_Texture5, sampler_Texture5, HS2BeardUV(uv0));
    half a5 = saturate(_Color5.a);
    half w5 = saturate(2 * a5) * pow(t5.r, 4 - 3 * a5);
    o.albedo = lerp(o.albedo, _Color5.rgb, w5);
    s = max(s, 0.5 * w5);
    // Beard bump: G is a height map; forward differences at a fixed texture-space offset (not
    // texel-size or screen-space dependent), strength 1.5 * _Color5.a, whiteout-blended below.
    float2 uvb = HS2BeardUV(uv0);
    half hx = SAMPLE_TEXTURE2D(_Texture5, sampler_Texture5, uvb + float2(HS2_BEARD_DUV, 0)).g;
    half hy = SAMPLE_TEXTURE2D(_Texture5, sampler_Texture5, uvb + float2(0, HS2_BEARD_DUV)).g;
    half2 nb5 = (t5.g - half2(hx, hy)) * (HS2_BEARD_BUMP * a5);
#endif
#if defined(HS2_SKIN_BODY)
    // Nipple (_Texture2 on warped UV1): weight Texture2.B * _Color2.a * (1 - vertex colour G).
    half4 t2 = SAMPLE_TEXTURE2D(_Texture2, sampler_Texture2, HS2NipUV(uv1));
    half wn = saturate(t2.b * _Color2.a * (1 - vcol.g));
    o.albedo = lerp(o.albedo, _Color2.rgb * t2.r * HS2_NIP_COLOR, wn);
    s = max(s, _NipGloss * wn);
#endif
    s += min(_NailGloss, nm.r); // nail gloss adds on top (NailMask.R; measured min, not product)
    o.smoothness = saturate(s);
    o.metallic = max(_Metallic, dm.r);   // DetailMainTex.R: painted metallic

    // Occlusion: min(OcclusionMap.G, detail occlusion), detail = max(1 - NailMask.G, DetailGlossMap.G).
    half occ = min(oc.g, max(1 - nm.g, dg.g));
    o.occlusion = lerp(1, occ, _OcclusionStrength) - HS2_TUNE_OCC * k * _OcclusionStrength;

    // Direct-only albedo lift where NailMask.G is set; the view-dependent factor uses _FresnelSetting
    // and is applied in HS2SkinFrag.
    o.liftMask = nm.g * o.smoothness * _FresnelSetting.w;

    // Normals: base and detail map (each with its own scale), combined with whiteout blending.
    half3 n1 = HS2UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, TRANSFORM_TEX(uv0, _BumpMap)), _BumpScale);
    half3 n2 = HS2UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap2, sampler_BumpMap2, TRANSFORM_TEX(uv0, _BumpMap2)), _BumpScale2);
    o.normalTS = normalize(half3(n1.xy + n2.xy, n1.z * n2.z));
#if defined(HS2_SKIN_FACE)
    half3 n5 = normalize(half3(nb5, 1));
    o.normalTS = normalize(half3(o.normalTS.xy + n5.xy, o.normalTS.z * n5.z));
#endif
    return o;
}

#if defined(HS2_SKIN_FORWARD)
#include "HS2NextBRDF.hlsl"

struct HS2SkinA { float4 pos : POSITION; float3 n : NORMAL; float4 t : TANGENT; float2 uv0 : TEXCOORD0; float2 uv1 : TEXCOORD1; half4 col : COLOR; };
struct HS2SkinV { float4 pos : SV_POSITION; float4 uv : TEXCOORD0; float3 wpos : TEXCOORD1; float3 n : TEXCOORD2; float4 t : TEXCOORD3; half4 col : TEXCOORD4; };

HS2SkinV HS2SkinVert(HS2SkinA a)
{
    HS2SkinV o;
    VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
    VertexNormalInputs nn = GetVertexNormalInputs(a.n, a.t);
    o.pos = p.positionCS; o.wpos = p.positionWS;
    o.n = nn.normalWS; o.t = float4(nn.tangentWS, a.t.w * GetOddNegativeScale());
    o.uv = float4(a.uv0, a.uv1); o.col = a.col;
    return o;
}

half4 HS2SkinFrag(HS2SkinV i, bool front : SV_IsFrontFace) : SV_Target
{
#if defined(HS2_SKIN_BODY)
    clip(HS2SkinAlpha(i.uv.xy) - _Cutoff);
#endif
    HS2SkinInputs si = HS2SkinSurface(i.uv.xy, i.uv.zw, i.col);
    float3 n = normalize(i.n) * (front ? 1 : -1);
    float3 t = normalize(i.t.xyz);
    float3 b = cross(n, t) * i.t.w;
    float3 nWS = normalize(mul(si.normalTS, float3x3(t, b, n)));

    HS2Surface s = HS2SurfaceMetallic(si.albedo, si.metallic, si.smoothness, si.occlusion, nWS);
    // Measured: direct-light albedo becomes saturate(albedo + lift * NdotL) (feeds F0 when metallic),
    // lift = _FresnelSetting.w * smoothness * NailMask.G * saturate(1 - (x + y * pow(1 - NdotV, z))).
    half nv = saturate(dot(nWS, GetWorldSpaceNormalizeViewDir(i.wpos)));
    half fres = _FresnelSetting.x + _FresnelSetting.y * pow(max(1 - nv, 1e-5), _FresnelSetting.z);
    s.lift = si.liftMask * saturate(1 - fres);
    s.transStrength = _Translucency; s.transDirect = _TransDirect; s.transAmbient = _TransAmbient;
    s.transScattering = _TransScattering; s.transDistortion = _TransNormalDistortion;
    s.transColor = si.transColor; s.transShadow = _TransShadow;
    return half4(HS2Shade(s, i.wpos, GetNormalizedScreenSpaceUV(i.pos)), 1);
}
#endif

#if defined(HS2_SKIN_SHADOW)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
float3 _LightDirection;
struct HS2SkinSA { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
struct HS2SkinSV { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
HS2SkinSV HS2SkinShadowVert(HS2SkinSA a)
{
    HS2SkinSV o;
    float3 w = TransformObjectToWorld(a.pos.xyz);
    float3 n = TransformObjectToWorldNormal(a.n);
    o.pos = TransformWorldToHClip(ApplyShadowBias(w, n, _LightDirection));
#if UNITY_REVERSED_Z
    o.pos.z = min(o.pos.z, o.pos.w * UNITY_NEAR_CLIP_VALUE);
#else
    o.pos.z = max(o.pos.z, o.pos.w * UNITY_NEAR_CLIP_VALUE);
#endif
    o.uv = a.uv;
    return o;
}
HS2SkinSV HS2SkinDepthVert(HS2SkinSA a) { HS2SkinSV o; o.pos = TransformObjectToHClip(a.pos.xyz); o.uv = a.uv; return o; }
half4 HS2SkinClipFrag(HS2SkinSV i) : SV_Target
{
#if defined(HS2_SKIN_BODY)
    clip(HS2SkinAlpha(i.uv) - _Cutoff);
#endif
    return 0;
}
#endif
#endif
