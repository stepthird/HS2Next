// Unity's published unlit and legacy particle shaders in URP (Unlit/*, Legacy Shaders/Particles/*, their Unity 5 names
// Particles/*, Mobile/Particles/*) and the Effects/* particle shaders of Studio effects, for items and maps. Each shader file sets the blend state and defines:
//   HS2_UL_COLOR (output _Color), HS2_UL_TEX (_MainTex with its tiling), HS2_UL_CUTOUT (clip at _Cutoff),
//   HS2_UL_VCOL (x vertex colour), HS2_UL_TINT2X (x 2 _TintColor, alpha saturated: Additive, Alpha Blended),
//   HS2_UL_PREMUL (x vertex alpha again: Alpha Blended Premultiply), HS2_UL_ADDSMOOTH (rgb x alpha: Additive (Soft)),
//   HS2_UL_SOFT (soft particles: vertex alpha x saturate(_InvFade (scene depth - fragment depth)); applied only when the
//   global _HS2NextSceneDepthValid is 1, because the project's URP renderer has no depth texture before the transparents),
//   HS2_UL_STRENGTH (with HS2_UL_TINT2X: 2 x vertex colour x tint x texture x _ColorStrength: the Effects/* particle
//   shaders; HS2_UL_STRENGTH_RGB: the strength on rgb only; HS2_UL_ALPHA_SAT: alpha saturated afterwards),
//   HS2_UL_TINT_COLOR (that tint is _Color instead of _TintColor),
//   HS2_UL_FOG (the transparent rows: the scene's height-fog term of HS2NextFog.hlsl at the end, gated per material by
//   _HS2FogModes), HS2_UL_FOG_BLACK (with HS2_UL_FOG: toward black, for the additive and premultiplied rows).
#ifndef HS2NEXT_UNLIT_INCLUDED
#define HS2NEXT_UNLIT_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#if defined(HS2_UL_FOG)
#include "HS2NextFog.hlsl"
#endif
#if defined(HS2_UL_SOFT)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#endif

TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
CBUFFER_START(UnityPerMaterial)
    float4 _Color, _TintColor, _MainTex_ST;
    float _Cutoff, _InvFade, _ColorStrength;
#if defined(HS2_UL_FOG)
    float _HS2FogModes;
#endif
CBUFFER_END
// Global, 1 when _CameraDepthTexture holds this camera's opaque depth before the transparents (see AIT_mainLight_URP).
float _HS2NextSceneDepthValid;

struct UlAttributes { float4 pos : POSITION; float4 color : COLOR; float2 uv0 : TEXCOORD0; };
struct UlVaryings { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

UlVaryings UlVert(UlAttributes a)
{
    UlVaryings o;
    o.pos = TransformObjectToHClip(a.pos.xyz);
    o.color = a.color;
    o.uv = TRANSFORM_TEX(a.uv0, _MainTex);
    return o;
}

half4 UlFrag(UlVaryings i) : SV_Target
{
#if defined(HS2_UL_COLOR)
    half4 col = _Color;
#else
    half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
#endif
#if defined(HS2_UL_CUTOUT)
    clip(col.a - _Cutoff);
#endif
#if defined(HS2_UL_VCOL)
    half4 vc = i.color;
#if defined(HS2_UL_SOFT)
    if (_HS2NextSceneDepthValid > 0)
    {
        float sceneZ = LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(i.pos)), _ZBufferParams);
        vc.a *= saturate(_InvFade * (sceneZ - LinearEyeDepth(i.pos.z, _ZBufferParams)));
    }
#endif
#if defined(HS2_UL_TINT2X) && defined(HS2_UL_STRENGTH)
#if defined(HS2_UL_TINT_COLOR)
    col = 2.0 * vc * _Color * col;
#else
    col = 2.0 * vc * _TintColor * col;
#endif
#if defined(HS2_UL_STRENGTH_RGB)
    col.rgb *= _ColorStrength;
#else
    col *= _ColorStrength;
#endif
#if defined(HS2_UL_ALPHA_SAT)
    col.a = saturate(col.a);
#endif
#elif defined(HS2_UL_TINT2X)
    col = 2.0 * vc * _TintColor * col;
    col.a = saturate(col.a);
#elif defined(HS2_UL_PREMUL)
    col = vc * col * vc.a;
#else
    col = vc * col;
#endif
#if defined(HS2_UL_ADDSMOOTH)
    col.rgb *= col.a;
#endif
#endif
#if defined(HS2_UL_FOG) && defined(HS2_UL_FOG_BLACK)
    col.rgb = HS2ApplyFogAdditiveCS(col.rgb, i.pos, _HS2FogModes);
#elif defined(HS2_UL_FOG)
    col.rgb = HS2ApplyFogCS(col.rgb, i.pos, _HS2FogModes);
#endif
    return col;
}

half4 UlDepthFrag(UlVaryings i) : SV_Target
{
#if defined(HS2_UL_CUTOUT)
    clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a - _Cutoff);
#endif
    return 0;
}
#endif
