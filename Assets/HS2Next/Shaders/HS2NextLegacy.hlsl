// Unity's published legacy surface shaders (Legacy Shaders/*, Mobile/*: Lambert and BlinnPhong lighting models) in URP,
// for mod items and maps. Each pass includes this file after defining the pass and the features of its shader:
//   pass:     HS2_LEG_FORWARD (LegVert / LegForwardFrag), HS2_LEG_SHADOW (LegShadowVert / LegShadowFrag), HS2_LEG_DEPTH
//   features: HS2_LEG_SPEC (BlinnPhong: _SpecColor x pow(N.H, _Shininess x 128) x gloss = albedo alpha), HS2_LEG_BUMP
//             (_BumpMap on its own tiling; HS2_LEG_BUMP_MAINUV: on _MainTex's UVs, the Mobile shaders),
//             HS2_LEG_CUTOUT (clip at _Cutoff), HS2_LEG_FADE (alpha:fade, the blend is in the shader file),
//             HS2_LEG_ILLUM (emission albedo x _Illum.a, own tiling), HS2_LEG_REFL (emission _Cube(reflected view) x albedo
//             alpha x _ReflectColor), HS2_LEG_MOBILE (no _Color, specular colour = the light colour), HS2_LEG_HALFASVIEW (the
//             main light's half vector per vertex, interpolated: Mobile/Bumped Specular's halfasview), HS2_LEG_FLIPBACK
//             (back faces use the flipped vertex normal; the shader file sets Cull Off), HS2_LEG_NOCOLOR (no _Color, the
//             specular colour kept), HS2_LEG_RIM (emission _RimColor x (1 - saturate(dot(normalize(view), N)))^_RimPower with
//             the view vector in the surface-shader tangent frame: Unity's documented rim example on BlinnPhong),
//             HS2_LEG_SPECGAIN <k> (the specular term times k: Bunker/Rim's highlight is 5 x BlinnPhong's, measured),
//             HS2_LEG_FOG (the transparent rows: the scene's height-fog term of HS2NextFog.hlsl, gated per material by
//             _HS2FogModes).
// Lighting as Built-in's surface shaders: ambient albedo x SH(N), per light albedo x light x max(0, N.L) (+ specular),
// light = colour x attenuation x shadow; the tangent frame is the surface-shader one (interpolated vertex tangent,
// binormal built per vertex, normalized after the transform). _RECEIVE_SHADOWS_OFF (ShaderMap.Swap: queue > 2500) drops
// the shadows as Built-in forward rendering does for transparent queues.
// Lightmaps (Phase 3.7 package L; the forward pass declares LIGHTMAP_ON, DIRLIGHTMAP_COMBINED, LIGHTMAP_SHADOW_MIXING and
// SHADOWS_SHADOWMASK like the Standard family): the baked term albedo x lightmap (uv1 x unity_LightmapST, URP's
// SampleLightmap: plain or directional) replaces albedo x SH as in Built-in's surface-shader lightmap path; subtractive
// mode takes the main light's shadow out of it (MixRealtimeAndBakedGI); the shadow mask (lightmap, or the light probes'
// occlusion when not lightmapped) mixes the baked shadows into the main and additional lights. Without these keywords the
// code is the earlier one (every fit regression unchanged).
#ifndef HS2NEXT_LEGACY_INCLUDED
#define HS2NEXT_LEGACY_INCLUDED
#include "HS2NextLights.hlsl"
#if defined(HS2_LEG_FOG)
#include "HS2NextFog.hlsl"
#endif

TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
TEXTURE2D(_Illum); SAMPLER(sampler_Illum);
TEXTURECUBE(_Cube); SAMPLER(sampler_Cube);
CBUFFER_START(UnityPerMaterial)
    float4 _Color, _SpecColor, _ReflectColor, _MainTex_ST, _BumpMap_ST, _Illum_ST, _RimColor;
    float _Shininess, _Cutoff, _Emission, _RimPower;
#if defined(HS2_LEG_FOG)
    float _HS2FogModes;
#endif
CBUFFER_END

struct LegAttributes
{
    float4 pos : POSITION; float3 n : NORMAL; float4 t : TANGENT; float2 uv0 : TEXCOORD0;
#if defined(LIGHTMAP_ON)
    float2 uv1 : TEXCOORD1;
#endif
};
struct LegVaryings
{
    float4 pos : SV_POSITION; float4 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; float3 wpos : TEXCOORD2; float3 n : TEXCOORD3; float3 t : TEXCOORD4; float3 b : TEXCOORD5; float3 h : TEXCOORD6;
#if defined(LIGHTMAP_ON)
    float2 lmuv : TEXCOORD7;   // lightmap UV: uv1 x unity_LightmapST (renderer.lightmapScaleOffset)
#endif
};

LegVaryings LegVert(LegAttributes a)
{
    LegVaryings o;
    VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
    VertexNormalInputs nn = GetVertexNormalInputs(a.n, a.t);
    o.pos = p.positionCS; o.wpos = p.positionWS;
    o.n = nn.normalWS; o.t = nn.tangentWS;
    o.b = cross(nn.normalWS, nn.tangentWS) * a.t.w * GetOddNegativeScale();
#if defined(HS2_LEG_BUMP_MAINUV)
    float2 bumpUv = TRANSFORM_TEX(a.uv0, _MainTex);
#else
    float2 bumpUv = TRANSFORM_TEX(a.uv0, _BumpMap);
#endif
    o.uv = float4(TRANSFORM_TEX(a.uv0, _MainTex), bumpUv);
    o.uv2 = TRANSFORM_TEX(a.uv0, _Illum);
    // halfasview (Mobile/Bumped Specular): the main light's half vector per vertex, interpolated, renormalized per pixel.
    o.h = normalize(normalize(_MainLightPosition.xyz) + normalize(GetCameraPositionWS() - p.positionWS));
#if defined(LIGHTMAP_ON)
    o.lmuv = a.uv1 * unity_LightmapST.xy + unity_LightmapST.zw;
#endif
    return o;
}

half4 LegBase(float2 uv)
{
    half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
#if !defined(HS2_LEG_MOBILE) && !defined(HS2_LEG_NOCOLOR)
    c *= _Color;
#endif
    return c;
}

#if defined(HS2_LEG_FORWARD)
half3 LegLight(half3 albedo, half gloss, float3 n, float3 h, float3 l, half3 lc)
{
    half3 c = albedo * lc * max(0, dot(n, l));
#if defined(HS2_LEG_SPEC)
    float nh = max(0, dot(n, h));
    float spec = pow(nh, _Shininess * 128.0) * gloss;
#if defined(HS2_LEG_SPECGAIN)
    spec *= HS2_LEG_SPECGAIN;
#endif
#if defined(HS2_LEG_MOBILE)
    c += lc * spec;
#else
    c += lc * _SpecColor.rgb * spec;
#endif
#endif
    return c;
}

half4 LegForwardFrag(LegVaryings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
{
#if defined(HS2_LEG_FLIPBACK)
    if (!IS_FRONT_VFACE(face, true, false)) i.n = -i.n;   // back faces: the vertex normal flipped
#endif
    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy);
#if defined(HS2_LEG_MOBILE) || defined(HS2_LEG_NOCOLOR)
    half4 col = tex;
#else
    half4 col = tex * _Color;
#endif
#if defined(HS2_LEG_CUTOUT)
    clip(col.a - _Cutoff);
#endif
#if defined(HS2_LEG_BUMP)
    half4 packed = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv.zw);
    packed.x *= packed.w;
    half3 nTS; nTS.xy = packed.xy * 2 - 1; nTS.z = sqrt(1 - saturate(dot(nTS.xy, nTS.xy)));
    float3 n = normalize(i.t * nTS.x + i.b * nTS.y + i.n * nTS.z);
#else
    half3 nTS = half3(0, 0, 1);
    float3 n = normalize(i.n);
#endif
    float3 v = GetWorldSpaceNormalizeViewDir(i.wpos);
    half receive = _RECEIVE_SHADOWS_OFF ? 0 : 1;
#if defined(LIGHTMAP_ON) || defined(SHADOWS_SHADOWMASK)
#if defined(LIGHTMAP_ON)
    half4 shadowMask = SAMPLE_SHADOWMASK(i.lmuv);   // the shadow mask under SHADOWS_SHADOWMASK, else 1
#else
    half4 shadowMask = unity_ProbesOcclusion;       // non-lightmapped renderers: the probes' occlusion
#endif
    Light main = GetMainLight(TransformWorldToShadowCoord(i.wpos), i.wpos, shadowMask);   // realtime and baked shadows mixed
#else
    half4 shadowMask = half4(1, 1, 1, 1);
    Light main = GetMainLight(TransformWorldToShadowCoord(i.wpos));
#endif
    main.color *= HS2LightLayerWeight(main.layerMask);
#if defined(HS2_LEG_FADE) && defined(LIGHTMAP_ON) && defined(SHADOWS_SHADOWMASK)
    // Built-in's alpha:fade legacy shaders leave a mixed Shadowmask main light out on lightmapped renderers (probed:
    // l_legacy_lm_fit.txt lm_mask on Transparent/Bumped Specular); URP marks such a light by its occlusion probe channel.
    if (any(_MainLightOcclusionProbes != 0)) main.color = 0;
#endif
#if defined(LIGHTMAP_ON)
    half3 baked = SampleLightmap(i.lmuv, n);   // URP's decoding (plain or DIRLIGHTMAP_COMBINED)
    MixRealtimeAndBakedGI(main, n, baked);     // subtractive mode: the main light's shadow taken out of the lightmap
    half3 c = col.rgb * baked;
#else
    half3 c = col.rgb * SampleSH(n);
#endif
#if defined(HS2_LEG_HALFASVIEW)
    float3 hMain = normalize(i.h);
#else
    float3 hMain = normalize(main.direction + v);
#endif
    c += LegLight(col.rgb, tex.a, n, hMain, main.direction, main.color * main.distanceAttenuation * lerp(1, main.shadowAttenuation, receive));
#if defined(_ADDITIONAL_LIGHTS)
    HS2LightIter it = HS2LightIterInit(i.wpos, GetNormalizedScreenSpaceUV(i.pos));
    uint lightIndex;
    [loop] while (HS2LightNext(it, lightIndex))
    {
        Light L = GetAdditionalLight(lightIndex, i.wpos, shadowMask);
        if (!HS2LightReaches(L, lightIndex)) continue;
        c += LegLight(col.rgb, tex.a, n, normalize(L.direction + v), L.direction, L.color * L.distanceAttenuation * lerp(1, L.shadowAttenuation, receive));
    }
#endif
#if defined(HS2_LEG_ILLUM)
    c += col.rgb * SAMPLE_TEXTURE2D(_Illum, sampler_Illum, i.uv2).a;
#endif
#if defined(HS2_LEG_REFL)
    half4 refl = SAMPLE_TEXTURECUBE(_Cube, sampler_Cube, reflect(-v, n)) * tex.a;
    c += refl.rgb * _ReflectColor.rgb;
#endif
#if defined(HS2_LEG_RIM)
    // the surface shader's IN.viewDir: the per-pixel world view vector in the interpolated tangent frame (rows t, b, n)
    float3 vTS = float3(dot(i.t, v), dot(i.b, v), dot(i.n, v));
    half rim = 1 - saturate(dot(normalize(vTS), nTS));
    c += _RimColor.rgb * pow(rim, _RimPower);
#endif
#if defined(HS2_LEG_FOG)
    c = HS2ApplyFog(c, i.wpos, _HS2FogModes);
#endif
#if defined(HS2_LEG_FADE)
    return half4(c, col.a);
#else
    return half4(c, 1);
#endif
}
#endif

#if defined(HS2_LEG_SHADOW)
float3 _LightDirection;
float3 _LightPosition;
LegVaryings LegShadowVert(LegAttributes a)
{
    LegVaryings o = LegVert(a);
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
half4 LegShadowFrag(LegVaryings i) : SV_Target
{
#if defined(HS2_LEG_CUTOUT)
    clip(LegBase(i.uv.xy).a - _Cutoff);
#endif
    return 0;
}
#endif

#if defined(HS2_LEG_DEPTH)
half4 LegDepthFrag(LegVaryings i) : SV_Target
{
#if defined(HS2_LEG_CUTOUT)
    clip(LegBase(i.uv.xy).a - _Cutoff);
#endif
    return 0;
}
#endif
#endif
