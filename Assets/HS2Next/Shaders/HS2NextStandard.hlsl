// Unity's published Standard shader family evaluated with HS2NextBRDF's Built-in BRDF1 (URP stand-ins for the Built-in
// "Standard", "Standard (Specular setup)", "Standard (Roughness setup)" and two-sided copies of Standard found in mod
// bundles). Each pass includes this file after defining the pass and, optionally, the workflow:
//   pass:     HS2_STD_FORWARD (StdVert / StdForwardFrag), HS2_STD_SHADOW (StdShadowVert / StdShadowFrag),
//             HS2_STD_DEPTH (StdVert / StdDepthFrag)
//   workflow: (none) metallic: _Metallic / _MetallicGlossMap (metallic R, smoothness A * _GlossMapScale),
//             HS2_STD_SPECULAR: _SpecColor / _SpecGlossMap (specular RGB, smoothness A * _GlossMapScale); diffuse =
//               albedo * (1 - max(specular)),
//             HS2_STD_ROUGHNESS: metallic R of _MetallicGlossMap or _Metallic, smoothness 1 - (R of _SpecGlossMap or
//               _Glossiness) (_Glossiness is the roughness there).
//   HS2_STD_FLIPBACK: back faces use the flipped vertex normal (two-sided copies that light back faces from their side).
// Material keywords select the features (dynamic branches, no variants): _NORMALMAP (Built-in UnpackScaleNormal with
// _BumpScale), _EMISSION (EmissionMap * EmissionColor), _METALLICGLOSSMAP, _SPECGLOSSMAP,
// _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A, _DETAIL_MULX2 (detail albedo x 4.59 in linear, detail normal; UV set _UVSec),
// _PARALLAXMAP (one-step offset), _SPECULARHIGHLIGHTS_OFF, _RECEIVE_SHADOWS_OFF (ShaderMap.Swap: queue > 2500), _GLOSSYREFLECTIONS_OFF, rendering mode _ALPHATEST_ON (cutout),
// _ALPHABLEND_ON (Fade: everything times alpha), _ALPHAPREMULTIPLY_ON (Transparent: diffuse premultiplied, alpha
// 1 - oneMinusReflectivity + alpha * it). The shader files declare the blend factors, depth writes (from _SrcBlend /
// _DstBlend / _ZWrite like Standard) and the culling.
// Lightmaps (Phase 3.2; the forward passes declare LIGHTMAP_ON, DIRLIGHTMAP_COMBINED, LIGHTMAP_SHADOW_MIXING and
// SHADOWS_SHADOWMASK like URP/Lit): the lightmap (uv1 x unity_LightmapST, URP's SampleLightmap) replaces the SH ambient,
// times occlusion, as Built-in Standard does; subtractive mode takes the main light's shadow out of it
// (MixRealtimeAndBakedGI); the shadow mask (lightmap, or the light probes' occlusion when not lightmapped) mixes the baked
// shadows into the main and additional lights (URP's MainLightShadow / AdditionalLightShadow). Non-lightmapped renderers
// take the SH (light probes or the ambient probe) unchanged. ShaderMap.SupportsLightmaps tells shaders with this path.
// Fog (Phase 5.R): the forward pass ends with the scene's height-fog term of HS2NextFog.hlsl, gated per material by
// _HS2FogModes (the fog modes the original was compiled for; set only for materials in the transparent queues, so opaque
// and cutout materials, fogged by the fog pass, keep 0).
#ifndef HS2NEXT_STANDARD_INCLUDED
#define HS2NEXT_STANDARD_INCLUDED
#include "HS2NextBRDF.hlsl"
#include "HS2NextFog.hlsl"

TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_SpecGlossMap); SAMPLER(sampler_SpecGlossMap);
TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
TEXTURE2D(_ParallaxMap); SAMPLER(sampler_ParallaxMap);
TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
TEXTURE2D(_DetailMask); SAMPLER(sampler_DetailMask);
TEXTURE2D(_DetailAlbedoMap); SAMPLER(sampler_DetailAlbedoMap);
TEXTURE2D(_DetailNormalMap); SAMPLER(sampler_DetailNormalMap);
CBUFFER_START(UnityPerMaterial)
    float4 _Color, _MainTex_ST, _DetailAlbedoMap_ST, _EmissionColor, _SpecColor;
    float _Cutoff, _Glossiness, _GlossMapScale, _Metallic, _BumpScale, _Parallax, _OcclusionStrength;
    float _DetailNormalMapScale, _UVSec;
    float _HS2FogModes;
CBUFFER_END

struct StdAttributes { float4 pos : POSITION; float3 n : NORMAL; float4 t : TANGENT; float2 uv0 : TEXCOORD0; float2 uv1 : TEXCOORD1; };
struct StdVaryings
{
    float4 pos : SV_POSITION; float4 uv : TEXCOORD0; float3 wpos : TEXCOORD1; float3 n : TEXCOORD2; float4 t : TEXCOORD3; float3 vpar : TEXCOORD4;
#if defined(LIGHTMAP_ON)
    float2 lmuv : TEXCOORD5;   // lightmap UV: uv1 x unity_LightmapST (renderer.lightmapScaleOffset)
#endif
};

StdVaryings StdVert(StdAttributes a)
{
    StdVaryings o;
    VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
    VertexNormalInputs nn = GetVertexNormalInputs(a.n, a.t);
    o.pos = p.positionCS; o.wpos = p.positionWS;
    o.n = nn.normalWS; o.t = float4(nn.tangentWS, a.t.w * GetOddNegativeScale());
    // Standard: xy = main maps (_MainTex tiling), zw = detail maps (_DetailAlbedoMap tiling on uv0 or uv1).
    o.uv = float4(TRANSFORM_TEX(a.uv0, _MainTex), TRANSFORM_TEX(_UVSec == 0 ? a.uv0 : a.uv1, _DetailAlbedoMap));
    // Parallax view vector as Built-in Standard builds it: the object-space view vector in the object-space tangent frame
    // (rows tangent, cross(n, t) * w, normal), per vertex, normalized per pixel (differs from a world-space frame on
    // non-uniformly scaled objects).
    float3 vOS = TransformWorldToObject(GetCameraPositionWS()) - a.pos.xyz;
    float3 bOS = cross(normalize(a.n), normalize(a.t.xyz)) * a.t.w;
    o.vpar = float3(dot(a.t.xyz, vOS), dot(bOS, vOS), dot(a.n, vOS));
#if defined(LIGHTMAP_ON)
    o.lmuv = a.uv1 * unity_LightmapST.xy + unity_LightmapST.zw;
#endif
    return o;
}

// Coverage alpha: albedo alpha x colour alpha, or the colour alpha alone when the albedo alpha is the smoothness.
half StdAlpha(float2 uv)
{
    return _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A ? _Color.a : SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a * _Color.a;
}

#if defined(HS2_STD_FORWARD)
half3 StdUnpackNormal(half4 packed, half scale)
{
    packed.x *= packed.w;
    half3 n; n.xy = (packed.xy * 2 - 1) * scale; n.z = sqrt(1 - saturate(dot(n.xy, n.xy)));
    return n;
}

half4 StdForwardFrag(StdVaryings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
{
    float3 n = normalize(i.n), t = normalize(i.t.xyz), b = cross(n, t) * i.t.w;
#if defined(HS2_STD_FLIPBACK)
    if (!IS_FRONT_VFACE(face, true, false)) n = -n;   // back faces: the vertex normal flipped (tangent and binormal kept)
#endif
    float4 uv = i.uv;
    if (_PARALLAXMAP)
    {
        float3 vTS = normalize(i.vpar);
        half h = SAMPLE_TEXTURE2D(_ParallaxMap, sampler_ParallaxMap, uv.xy).g;
        vTS.z += 0.42;
        float2 off = (h * _Parallax - _Parallax / 2) * (vTS.xy / vTS.z);
        uv += float4(off, off);
    }
    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv.xy);
    half alpha = _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A ? _Color.a : tex.a * _Color.a;
    if (_ALPHATEST_ON) clip(alpha - _Cutoff);
    half3 albedo = tex.rgb * _Color.rgb;
    half detailMask = SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, uv.xy).a;
    if (_DETAIL_MULX2)
    {
        half3 d = SAMPLE_TEXTURE2D(_DetailAlbedoMap, sampler_DetailAlbedoMap, uv.zw).rgb;
        albedo *= lerp(1, d * 4.59479380, detailMask);   // unity_ColorSpaceDouble in linear space
    }
    half3 nTS = half3(0, 0, 1);
    if (_NORMALMAP)
    {
        nTS = StdUnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv.xy), _BumpScale);
        if (_DETAIL_MULX2)
        {
            half3 dn = StdUnpackNormal(SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, uv.zw), _DetailNormalMapScale);
            nTS = lerp(nTS, normalize(half3(nTS.xy + dn.xy, nTS.z * dn.z)), detailMask);
        }
    }
    half occ = lerp(1, SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, uv.xy).g, _OcclusionStrength);
    float3 nWS = normalize(mul(nTS, float3x3(t, b, n)));
#if defined(HS2_STD_SPECULAR)
    half4 sg;
    if (_SPECGLOSSMAP)
    {
        half4 m = SAMPLE_TEXTURE2D(_SpecGlossMap, sampler_SpecGlossMap, uv.xy);
        sg = half4(m.rgb, (_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A ? tex.a : m.a) * _GlossMapScale);
    }
    else sg = half4(_SpecColor.rgb, _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A ? tex.a * _GlossMapScale : _Glossiness);
    HS2Surface s = HS2SurfaceSpecular(albedo, sg.rgb, sg.a, occ, nWS);
#elif defined(HS2_STD_ROUGHNESS)
    half metallic = _METALLICGLOSSMAP ? SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv.xy).r : _Metallic;
    half rough = _SPECGLOSSMAP ? SAMPLE_TEXTURE2D(_SpecGlossMap, sampler_SpecGlossMap, uv.xy).r : _Glossiness;
    HS2Surface s = HS2SurfaceMetallic(albedo, metallic, 1 - rough, occ, nWS);
#else
    half2 mg;
    if (_METALLICGLOSSMAP)
    {
        half4 m = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv.xy);
        mg = half2(m.r, (_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A ? tex.a : m.a) * _GlossMapScale);
    }
    else mg = half2(_Metallic, _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A ? tex.a * _GlossMapScale : _Glossiness);
    HS2Surface s = HS2SurfaceMetallic(albedo, mg.x, mg.y, occ, nWS);
#endif
    if (_EMISSION) s.emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uv.xy).rgb * _EmissionColor.rgb;
#if defined(LIGHTMAP_ON)
    HS2BakedGI = SampleLightmap(i.lmuv, nWS);   // URP's decoding (plain or DIRLIGHTMAP_COMBINED); HS2Shade multiplies by occlusion
    HS2ShadowMask = SAMPLE_SHADOWMASK(i.lmuv);   // the shadow mask under SHADOWS_SHADOWMASK, else 1
#elif defined(SHADOWS_SHADOWMASK)
    HS2ShadowMask = unity_ProbesOcclusion;   // non-lightmapped renderers: the probes' occlusion
#endif
    s.specularHighlights = _SPECULARHIGHLIGHTS_OFF ? 0 : 1;
    s.glossyReflections = _GLOSSYREFLECTIONS_OFF ? 0 : 1;
    HS2ShadowReceive = _RECEIVE_SHADOWS_OFF ? 0 : 1;   // set by ShaderMap.Swap for transparent queues (Built-in: no shadows there)
    half outAlpha = 1;
    if (_ALPHABLEND_ON) outAlpha = alpha;
    if (_ALPHAPREMULTIPLY_ON)
    {
        s.albedo *= alpha;   // diffuse premultiplied; specular colour was taken before
        outAlpha = 1 - s.oneMinusReflectivity + alpha * s.oneMinusReflectivity;
    }
    half3 c = HS2Shade(s, i.wpos, GetNormalizedScreenSpaceUV(i.pos));
    c = HS2ApplyFog(c, i.wpos, _HS2FogModes);
    return half4(c, outAlpha);
}
#endif

#if defined(HS2_STD_SHADOW)
float3 _LightDirection;
float3 _LightPosition;

// 4x4 ordered dither threshold (k + 1) / 16 (Fade / Transparent shadows; Built-in uses a dither mask texture).
float StdDither(float4 positionCS)
{
    uint2 p = (uint2)positionCS.xy;
    const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    return (m[(p.y & 3) * 4 + (p.x & 3)] + 1) / 16.0;
}

StdVaryings StdShadowVert(StdAttributes a)
{
    StdVaryings o = StdVert(a);
#if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 l = normalize(_LightPosition - o.wpos);
#else
    float3 l = _LightDirection;
#endif
    float4 cs = TransformWorldToHClip(ApplyShadowBias(o.wpos, o.n, l));
#if UNITY_REVERSED_Z
    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
#else
    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
#endif
    o.pos = cs;
    return o;
}

half4 StdShadowFrag(StdVaryings i) : SV_Target
{
    half alpha = StdAlpha(i.uv.xy);
    if (_ALPHATEST_ON) clip(alpha - _Cutoff);
    if (_ALPHABLEND_ON || _ALPHAPREMULTIPLY_ON) clip(alpha - StdDither(i.pos));
    return 0;
}
#endif

#if defined(HS2_STD_DEPTH) && defined(HS2_PASS_WANTS_SMOOTHNESS)
// The surface smoothness the forward pass uses (the workflow's map or value; no parallax), for the DepthNormals pass's
// smoothness output (screen-space reflection receivers).
half StdSmoothness(float2 uv)
{
    half albedoA = _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A ? SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).a : 1;
#if defined(HS2_STD_SPECULAR)
    if (_SPECGLOSSMAP) return (_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A ? albedoA : SAMPLE_TEXTURE2D(_SpecGlossMap, sampler_SpecGlossMap, uv).a) * _GlossMapScale;
    return _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A ? albedoA * _GlossMapScale : _Glossiness;
#elif defined(HS2_STD_ROUGHNESS)
    return 1 - (_SPECGLOSSMAP ? SAMPLE_TEXTURE2D(_SpecGlossMap, sampler_SpecGlossMap, uv).r : _Glossiness);
#else
    if (_METALLICGLOSSMAP) return (_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A ? albedoA : SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv).a) * _GlossMapScale;
    return _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A ? albedoA * _GlossMapScale : _Glossiness;
#endif
}
#define HS2_PASS_SMOOTHNESS(v) StdSmoothness((v).uv.xy)
#endif

#if defined(HS2_STD_DEPTH)
half4 StdDepthFrag(StdVaryings i) : SV_Target
{
    if (_ALPHATEST_ON) clip(StdAlpha(i.uv.xy) - _Cutoff);
    return 0;
}
#endif
#endif
