// Stand-ins for mod particle and effect shaders in URP: the particle router of ShaderMap (Fitted block "5.2 Sc").
// Tools/gen_legacy_shaders.py (table ROUTED) writes each shader file with the original's property list and pass state (blend,
// cull, depth write, colour mask from the bundle's shader object); one unlit pass, the colour model by define. Black-box probed
// against the bundle copies (Tests/FitCases/s52_*.txt; colours linear, "tex" = the texture sample with its tiling, "vc" = the
// vertex colour):
//   HS2_FX_HOVL           Hovl's centre-glow particles: rgb = tex x noise x _Color x vc x _Emission x lerp(1, glow,
//                         _Usecenterglow), alpha = tex.a x noise.a x _Color.a x vc.a (HS2_FX_OPACITY: x _Opacity, not
//                         saturated); HS2_FX_PREMUL (the additive one, blend One [_Blend2]): rgb x alpha. The textures scroll by
//                         _SpeedMainTexUVNoiseZW x time; glow = _Mask.r x the particle's custom data (uv0.z), 0 on a mesh
//                         without custom data
//   HS2_FX_HOVL_TWOSIDES  Hovl's two-sided particle: rgb = tex x (front or back face colour) x vc x vc.a x _Emission, with
//                         _UseFresnel on front faces x lerp(1, _FresnelColor x _FresnelEmission, (1 - N.V)^_Fresnel); alpha 1;
//                         clipped at _Cutoff on lerp(1, _Mask.r x uv0.z, _UseCustomData) (the custom data dissolve: a mesh without custom
//                         data is clipped as in the original)
//   HS2_FX_FIREWING_MAIN  2 x _TintColor x _SubTex x _Mask.a x _Transparency x vc (additive)
//   HS2_FX_FIREWING_SUB   2 x tex x _TintColor x _Mask x _Transparency x vc (additive)
//   HS2_FX_GAPH           rgb = 0.75 x tex x vc x _TintColor x _AllColorFactor x (_ColorRedFactor, _ColorGreenFactor,
//                         _ColorBlueFactor) (the tint is [HDR]: used as stored),
//                         alpha = tex.a x vc.a x _TintColor.a x _AllColorFactor (blend from _BlendSrc / _BlendDst)
// Fog: the scene's height-fog term of HS2NextFog.hlsl at the end, gated per material by _HS2FogModes (the fog modes the
// original was compiled for): toward black for the additive and premultiplied models (FireWing, HS2_FX_PREMUL) and for GAPH
// when its destination blend is One, toward the fog colour otherwise.
// Not drawn (approximations): the flow-map and noise UV distortions, the depth fades (soft particles), Hovl's separate back-face
// fresnel.
#ifndef HS2NEXT_FX_INCLUDED
#define HS2NEXT_FX_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "HS2NextFog.hlsl"

TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
#if defined(HS2_FX_HOVL) || defined(HS2_FX_HOVL_TWOSIDES)
TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
#endif
#if defined(HS2_FX_HOVL) || defined(HS2_FX_HOVL_TWOSIDES) || defined(HS2_FX_FIREWING_MAIN) || defined(HS2_FX_FIREWING_SUB)
TEXTURE2D(_Mask); SAMPLER(sampler_Mask);
#endif
#if defined(HS2_FX_FIREWING_MAIN)
TEXTURE2D(_SubTex); SAMPLER(sampler_SubTex);
#endif

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
#if defined(HS2_FX_HOVL)
    float4 _Noise_ST, _Flow_ST, _Mask_ST, _SpeedMainTexUVNoiseZW, _DistortionSpeedXYPowerZ;
    half4 _Color;
    float _Emission, _Opacity, _Usecenterglow, _Usedepth, _Depthpower, _CullMode, _Blend2, _Usecustomrandom;
#elif defined(HS2_FX_HOVL_TWOSIDES)
    float4 _Mask_ST, _Noise_ST, _SpeedMainTexUVNoiseZW;
    half4 _FrontFacesColor, _BackFacesColor, _FresnelColor;
    float _Cutoff, _Emission, _UseFresnel, _SeparateFresnel, _SeparateEmission, _Fresnel, _FresnelEmission, _UseCustomData;
#elif defined(HS2_FX_FIREWING_MAIN) || defined(HS2_FX_FIREWING_SUB)
    float4 _SubTex_ST, _Mask_ST, _Noise_ST, _MaskNoice_ST, _Noice_ST, _Reflective_ST;
    half4 _TintColor;
    float _Transparency;
#else
    half4 _TintColor;
    float _AllColorFactor, _ColorRedFactor, _ColorGreenFactor, _ColorBlueFactor, _BlendSrc, _BlendDst, _InvFade;
#endif
    float _HS2FogModes;
CBUFFER_END

struct FxAttributes { float4 pos : POSITION; float3 n : NORMAL; half4 color : COLOR; float4 uv0 : TEXCOORD0; };
struct FxVaryings { float4 pos : SV_POSITION; half4 color : COLOR; float4 uv0 : TEXCOORD0; float3 nWS : TEXCOORD1; float3 vWS : TEXCOORD2; };

FxVaryings FxVert(FxAttributes a)
{
    FxVaryings o;
    float3 wpos = TransformObjectToWorld(a.pos.xyz);
    o.pos = TransformWorldToHClip(wpos);
    o.color = a.color;
    o.uv0 = a.uv0;
    o.nWS = TransformObjectToWorldNormal(a.n);
    o.vWS = GetWorldSpaceViewDir(wpos);
    return o;
}

half4 FxShade(FxVaryings i, bool front)
{
    float2 uv = i.uv0.xy;
    half4 vc = i.color;
#if defined(HS2_FX_HOVL) || defined(HS2_FX_HOVL_TWOSIDES)
    float t = _Time.y;
    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv * _MainTex_ST.xy + _MainTex_ST.zw + _SpeedMainTexUVNoiseZW.xy * t);
    half mask = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, uv * _Mask_ST.xy + _Mask_ST.zw).r;
#endif
#if defined(HS2_FX_HOVL)
    half4 noise = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, uv * _Noise_ST.xy + _Noise_ST.zw + _SpeedMainTexUVNoiseZW.zw * t);
    half4 col = tex * noise * _Color * vc;
    half glow = saturate(mask * (half)i.uv0.z);
    col.rgb *= _Emission * lerp(1.0h, glow, (half)_Usecenterglow);
#if defined(HS2_FX_OPACITY)
    col.a *= _Opacity;
#endif
#if defined(HS2_FX_PREMUL)
    col.rgb *= col.a;
#endif
    return col;
#elif defined(HS2_FX_HOVL_TWOSIDES)
    half4 face = front ? _FrontFacesColor : _BackFacesColor;
    half3 rgb = tex.rgb * face.rgb * vc.rgb * vc.a * _Emission;
    if (_UseFresnel > 0.5 && front)
    {
        float3 n = normalize(i.nWS);
        half f = pow(saturate(1.0 - saturate(dot(n, normalize(i.vWS)))), max(_Fresnel, 1e-4));
        rgb *= lerp((half3)1.0h, _FresnelColor.rgb * _FresnelEmission, f);
    }
    clip(lerp(1.0h, saturate(mask * (half)i.uv0.z), (half)_UseCustomData) - _Cutoff);
    return half4(rgb, 1.0h);
#elif defined(HS2_FX_FIREWING_MAIN)
    half4 sub = SAMPLE_TEXTURE2D(_SubTex, sampler_SubTex, uv * _SubTex_ST.xy + _SubTex_ST.zw);
    half ma = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, uv * _Mask_ST.xy + _Mask_ST.zw).a;
    return half4(2.0h * _TintColor.rgb * sub.rgb * ma * _Transparency * vc.rgb, 1.0h);
#elif defined(HS2_FX_FIREWING_SUB)
    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv * _MainTex_ST.xy + _MainTex_ST.zw);
    half3 m = SAMPLE_TEXTURE2D(_Mask, sampler_Mask, uv * _Mask_ST.xy + _Mask_ST.zw).rgb;
    return half4(2.0h * tex.rgb * _TintColor.rgb * m * _Transparency * vc.rgb, 1.0h);
#else
    half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv * _MainTex_ST.xy + _MainTex_ST.zw);
    half4 col = tex * vc * _TintColor * _AllColorFactor;
    col.rgb *= 0.75h * half3(_ColorRedFactor, _ColorGreenFactor, _ColorBlueFactor);
    return col;
#endif
}
half4 FxFrag(FxVaryings i, bool front : SV_IsFrontFace) : SV_Target
{
    half4 col = FxShade(i, front);
#if defined(HS2_FX_FIREWING_MAIN) || defined(HS2_FX_FIREWING_SUB) || defined(HS2_FX_PREMUL)
    col.rgb = HS2ApplyFogAdditiveCS(col.rgb, i.pos, _HS2FogModes);
#elif defined(HS2_FX_GAPH)
    col.rgb = HS2ApplyFogEitherCS(col.rgb, i.pos, _HS2FogModes, _BlendDst == 1.0);
#else
    col.rgb = HS2ApplyFogCS(col.rgb, i.pos, _HS2FogModes);
#endif
    return col;
}
#endif
