// URP replacement for HS2 "AIT/main namida" (tears o_namida, material c_m_eye_namida, queue 2989, game keyword
// _ALPHABLEND_ON). Same property names, types and defaults as the original. Behaviour inferred by black-box
// probing only (Assets/HS2Next/Editor/Fit/SkinFit.cs, Tests/FitCases/namida*.txt, Spike/out/fit/AIT_main_namida_*.txt):
//  Amount           f = pow(_MainTex.g, 8 - 7 * _NamidaScale) * saturate(_NamidaScale / 0.3): the tear mask (texture G)
//                   grows from its brightest texels as _NamidaScale rises; clip(f - _Cutoff), so the default
//                   _NamidaScale = 0 (game: tearsRate 0) draws nothing. R, B and A of the texture are unused.
//  Surface          Built-in Standard premultiplied transparency: albedo _Color.rgb, alpha _Color.a * f, metallic 0
//                   (F0 0.04, specular not scaled by alpha), smoothness max(0.6 * G, _Glossiness * f * pow(G, 8 - 7 *
//                   _NamidaScale)) (lobes solved per case with an offline BRDF1 model of the probe); blend One
//                   OneMinusSrcAlpha, output alpha 0.04 + 0.96 * alpha, ZWrite off.
//  Refraction       (Phase 3.3, package C) the original's grab-pass refraction from URP's opaque texture: the background
//                   behind the tear is shifted on screen by (_RefractionScale - 1) x the view-space normal x (1 - N.V)
//                   / (clip z + 1) (screen-uv units; 1.3% of the view for a 30 degree tilt at scale 0), red at that
//                   offset, green at x (1 - _ChromaticAberration), blue at x (1 + _ChromaticAberration), and seen
//                   through the tear's remaining transmission (1 - output alpha). The blend stays premultiplied over
//                   the frame buffer and only the shift is added (refracted - unshifted opaque background), so
//                   transparents drawn before the tears (eyelashes, eye overlays) stay visible; HS2's grab pass also
//                   shifts those, URP's opaque texture holds the opaques only. Applied only while
//                   the global _HS2NextSceneOpaqueValid is 1 (SceneTextures: the app's asset; the measurement asset of the
//                   fit tools has no opaque texture, so the fits there are unchanged). At the game value 0.98 the shift
//                   is below 0.1% of the view. _NormalMapScale has no measurable effect.
// The game writes _NamidaScale = tearsRate.
Shader "HS2Next/AIT main namida"
{
    Properties
    {
        _Color ("Color", Color) = (0.25,0.25,0.25,0.329)
        _MainTex ("Main Tex", 2D) = "white" {}
        _Cutoff ("Mask Clip Value", Float) = 0.5
        _NamidaScale ("Namida Scale", Range(0,1)) = 0
        _Glossiness ("Glossiness", Range(0,1)) = 0.85
        _RefractionScale ("Refraction Scale", Float) = 0.95
        _NormalMapScale ("Normal Map Scale", Float) = 3
        [Header(Refraction)] _ChromaticAberration ("Chromatic Aberration", Range(0,0.3)) = 0.1
        [HideInInspector] _texcoord ("", 2D) = "white" {}
        [HideInInspector] __dirty ("", Float) = 1
        [HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="Overlay+1" "IgnoreProjector"="True" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "HS2NextBRDF.hlsl"
            #include "HS2NextFog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _Color;
                float _Cutoff, _NamidaScale, _Glossiness, _RefractionScale, _NormalMapScale, _ChromaticAberration;
                float _HS2FogModes;
            CBUFFER_END
            // Global, 1 when _CameraOpaqueTexture holds this camera's opaques (SceneTextures).
            float _HS2NextSceneOpaqueValid;
            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1; float3 n : TEXCOORD2; float4 scr : TEXCOORD3; };
            V vert(A a)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
                o.pos = p.positionCS; o.wpos = p.positionWS;
                o.n = TransformObjectToWorldNormal(a.n);
                o.uv = TRANSFORM_TEX(a.uv, _MainTex);
                o.scr = ComputeScreenPos(p.positionCS);   // xy / w: screen uv (y up), z: clip z
                return o;
            }
            // the opaque background shifted by the refraction offset minus the unshifted one (rgb per channel)
            half3 RefractionShift(float3 n, float3 wpos, float4 scr)
            {
                float3 nv = mul((float3x3)GetWorldToViewMatrix(), n);
                float3 v = GetWorldSpaceNormalizeViewDir(wpos);
                float k = (_RefractionScale - 1.0) * (1.0 - dot(n, v)) / (scr.z + 1.0);
                float2 off = nv.xy * k;
                float2 uv = scr.xy / scr.w;
                half3 bg = SampleSceneColor(uv);
                half r = SampleSceneColor(uv + off).r;
                half g = SampleSceneColor(uv + off * (1.0 - _ChromaticAberration)).g;
                half b = SampleSceneColor(uv + off * (1.0 + _ChromaticAberration)).b;
                return half3(r, g, b) - bg;
            }
            half4 frag(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                half g = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).g;
                half gp = exp2((8 - 7 * _NamidaScale) * log2(max(g, 1e-8)));   // pow(G, 8 - 7 * scale)
                half f = gp * saturate(_NamidaScale / 0.3);
                clip(f - _Cutoff);
                half alpha = _Color.a * f;
                float3 n = normalize(i.n) * (front ? 1 : -1);
                half smooth = max(0.6 * g, _Glossiness * f * gp);
                HS2Surface s = HS2SurfaceMetallic(_Color.rgb * alpha, 0, smooth, 1, n);
                half3 c = HS2Shade(s, i.wpos, GetNormalizedScreenSpaceUV(i.pos));
                half aOut = 1 - s.oneMinusReflectivity + alpha * s.oneMinusReflectivity;
                if (_HS2NextSceneOpaqueValid > 0)
                    c += RefractionShift(n, i.wpos, i.scr) * (1 - aOut);
                return half4(HS2ApplyFog(c, i.wpos, _HS2FogModes), aOut);
            }
            ENDHLSL
        }
    }
}
