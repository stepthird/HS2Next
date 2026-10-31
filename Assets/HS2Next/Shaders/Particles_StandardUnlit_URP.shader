// URP stand-in for Unity's built-in "Particles/Standard Unlit" (vanilla map 15 effects, mod maps and Studio items). Unity's
// published semantics (bundle property list and pass state agree): blend, ZWrite and culling from the material's _SrcBlend /
// _DstBlend / _BlendOp / _ZWrite / _Cull (set by the material editor from _Mode); albedo _MainTex x _Color, combined with the
// vertex colour by the colour mode (multiply; _COLOROVERLAY_ON overlay; _COLORCOLOR_ON the vertex colour's hue and
// saturation with the albedo's value; _COLORADDSUBDIFF_ON albedo + vertex colour x _ColorAddSubDiff.x, the difference
// mode with _ColorAddSubDiff.y), _ALPHATEST_ON clip at _Cutoff, _ALPHAMODULATE_ON
// lerp(1, rgb, alpha), alpha output only with _ALPHABLEND_ON / _ALPHAPREMULTIPLY_ON / _ALPHAMODULATE_ON (else 1; measured), _EMISSION + _EmissionMap x _EmissionColor, _FADING_ON camera fading (_CameraFadeParams) and soft
// particles (_SoftParticleFadeParams; only when the global _HS2NextSceneDepthValid is 1: the project's renderer has no depth
// texture before the transparents). Not reproduced (approximation): distortion (EFFECT_BUMP: a grab pass), flip-book frame
// blending (_REQUIRE_UV2), fog.
Shader "HS2Next/Particles/Standard Unlit"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _BumpScale ("Scale", Float) = 1
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _EmissionColor ("Color", Color) = (0,0,0,1)
        _EmissionMap ("Emission", 2D) = "white" {}
        _DistortionStrength ("Strength", Float) = 1
        _DistortionBlend ("Blend", Range(0, 1)) = 0.5
        _SoftParticlesNearFadeDistance ("Soft Particles Near Fade", Float) = 0
        _SoftParticlesFarFadeDistance ("Soft Particles Far Fade", Float) = 1
        _CameraNearFadeDistance ("Camera Near Fade", Float) = 1
        _CameraFarFadeDistance ("Camera Far Fade", Float) = 2
        [HideInInspector] _Mode ("__mode", Float) = 0
        [HideInInspector] _ColorMode ("__colormode", Float) = 0
        [HideInInspector] _FlipbookMode ("__flipbookmode", Float) = 0
        [HideInInspector] _LightingEnabled ("__lightingenabled", Float) = 0
        [HideInInspector] _DistortionEnabled ("__distortionenabled", Float) = 0
        [HideInInspector] _EmissionEnabled ("__emissionenabled", Float) = 0
        [HideInInspector] _BlendOp ("__blendop", Float) = 0
        [HideInInspector] _SrcBlend ("__src", Float) = 1
        [HideInInspector] _DstBlend ("__dst", Float) = 0
        [HideInInspector] _ZWrite ("__zw", Float) = 1
        [HideInInspector] _Cull ("__cull", Float) = 2
        [HideInInspector] _SoftParticlesEnabled ("__softparticlesenabled", Float) = 0
        [HideInInspector] _CameraFadingEnabled ("__camerafadingenabled", Float) = 0
        [HideInInspector] _SoftParticleFadeParams ("__softparticlefadeparams", Vector) = (0,0,0,0)
        [HideInInspector] _CameraFadeParams ("__camerafadeparams", Vector) = (0,0,0,0)
        [HideInInspector] _ColorAddSubDiff ("__coloraddsubdiff", Vector) = (0,0,0,0)
        [HideInInspector] _DistortionStrengthScaled ("__distortionstrengthscaled", Float) = 0
        [HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "IgnoreProjector"="True" "PreviewType"="Plane" "PerformanceChecks"="False" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            BlendOp [_BlendOp]
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]
            ColorMask RGB
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma dynamic_branch_local _ALPHATEST_ON
            #pragma dynamic_branch_local _ALPHABLEND_ON
            #pragma dynamic_branch_local _ALPHAPREMULTIPLY_ON
            #pragma dynamic_branch_local _ALPHAMODULATE_ON
            #pragma dynamic_branch_local _COLOROVERLAY_ON
            #pragma dynamic_branch_local _COLORCOLOR_ON
            #pragma dynamic_branch_local _COLORADDSUBDIFF_ON
            #pragma dynamic_branch_local _EMISSION
            #pragma dynamic_branch_local _FADING_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "HS2NextFog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _BumpMap_ST, _EmissionMap_ST, _SoftParticleFadeParams, _CameraFadeParams, _ColorAddSubDiff;
                half4 _Color, _EmissionColor;
                float _Cutoff, _BumpScale, _DistortionStrength, _DistortionBlend, _SoftParticlesNearFadeDistance, _SoftParticlesFarFadeDistance,
                      _CameraNearFadeDistance, _CameraFarFadeDistance, _Mode, _ColorMode, _FlipbookMode, _LightingEnabled, _DistortionEnabled,
                      _EmissionEnabled, _BlendOp, _SrcBlend, _DstBlend, _ZWrite, _Cull, _SoftParticlesEnabled, _CameraFadingEnabled,
                      _DistortionStrengthScaled;
                float _HS2FogModes;
            CBUFFER_END
            float _HS2NextSceneDepthValid;
            struct A { float4 pos : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float eyeZ : TEXCOORD1; };
            V Vert(A a)
            {
                V o;
                float3 w = TransformObjectToWorld(a.pos.xyz);
                o.pos = TransformWorldToHClip(w);
                o.eyeZ = -TransformWorldToView(w).z;
                o.color = a.color;
                o.uv = TRANSFORM_TEX(a.uv, _MainTex);
                return o;
            }
            half3 RgbToHsv(half3 c)
            {
                half4 K = half4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                half4 p = lerp(half4(c.bg, K.wz), half4(c.gb, K.xy), step(c.b, c.g));
                half4 q = lerp(half4(p.xyw, c.r), half4(c.r, p.yzx), step(p.x, c.r));
                half d = q.x - min(q.w, q.y);
                half e = 1.0e-4;
                return half3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }
            half3 HsvToRgb(half3 c)
            {
                half4 K = half4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                half3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }
            half4 Frag(V i) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Color;
                half4 vc = i.color;
                if (_COLOROVERLAY_ON)
                {
                    half3 lo = 2 * albedo.rgb * vc.rgb, hi = 1 - 2 * (1 - albedo.rgb) * (1 - vc.rgb);
                    albedo.rgb = lerp(lo, hi, step(0.5, albedo.rgb));
                    albedo.a *= vc.a;
                }
                else if (_COLORCOLOR_ON)
                {
                    half3 a = RgbToHsv(albedo.rgb), b = RgbToHsv(vc.rgb);
                    albedo = half4(HsvToRgb(half3(b.x, b.y, a.z)), albedo.a * vc.a);
                }
                else if (_COLORADDSUBDIFF_ON)
                {
                    albedo.rgb = albedo.rgb + vc.rgb * _ColorAddSubDiff.x;
                    albedo.rgb = lerp(albedo.rgb, abs(albedo.rgb), _ColorAddSubDiff.y);
                    albedo.a *= vc.a;
                }
                else albedo *= vc;
                if (_FADING_ON)
                {
                    half fade = 1;
                    if (_SoftParticleFadeParams.x > 0 || _SoftParticleFadeParams.y > 0)
                        if (_HS2NextSceneDepthValid > 0)
                        {
                            float sceneZ = LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(i.pos)), _ZBufferParams);
                            fade = saturate(_SoftParticleFadeParams.y * ((sceneZ - _SoftParticleFadeParams.x) - i.eyeZ));
                        }
                    fade *= saturate((i.eyeZ - _CameraFadeParams.x) * _CameraFadeParams.y);
                    albedo.a *= fade;
                }
                if (_ALPHATEST_ON) clip(albedo.a - _Cutoff + 0.0001);
                half3 rgb = albedo.rgb;
                if (_EMISSION) rgb += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb * _EmissionColor.rgb;
                if (_ALPHAMODULATE_ON) rgb = lerp(half3(1, 1, 1), rgb, albedo.a);
                // measured: alpha only with a blend keyword (opaque, cutout and the keyword-less additive mode output 1);
                // _ALPHAPREMULTIPLY_ON leaves rgb as is (the material's One / OneMinusSrcAlpha factors do the rest)
                half a = (_ALPHABLEND_ON || _ALPHAPREMULTIPLY_ON || _ALPHAMODULATE_ON) ? albedo.a : 1;
                return half4(HS2ApplyFogEitherCS(rgb, i.pos, _HS2FogModes, _DstBlend == 1.0), a);   // additive materials: toward black
            }
            ENDHLSL
        }
    }
}
