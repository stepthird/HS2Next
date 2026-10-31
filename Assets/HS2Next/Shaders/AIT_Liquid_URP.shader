// URP replacement for HS2 "AIT/Liquid" (fluid overlay, body o_body_cf slot 1 material c_m_liquid_body,
// queue 2989, game keyword _ALPHAPREMULTIPLY_ON). Same property names, types and defaults as the original.
// Behaviour inferred by black-box probing only (Assets/HS2Next/Editor/Fit/SkinFit.cs, cases in
// Tests/FitCases/liquid*.txt, data in Spike/out/fit/AIT_Liquid_*.txt):
//  Level            L = _WeatheringAll + sum_i min(region_i, _WeatheringRange_i), regions from _WeatheringMask (UV0)
//                   by colour: 1 = R, 2 = G, 3 = B, 4 = R+G, 5 = R+B, 6 = G+B; region weight = the channels of the
//                   colour minus the others (saturate(R - max(G, B)), saturate(min(R, G) - B), ...); mask alpha unused.
//  Drips            _WeatheringMap on uv' = -(UV0 * _WeatheringUV.xy + _WeatheringUV.zw) (texture turned 180 deg):
//                   B = first level, v_B = min(B, saturate(2L)); R = second level, v_R = min(R, saturate(2L - 1));
//                   v = max(v_B, v_R). G and A are unused.
//  Clip             clip(20 * max(min(B, L), min(R, 2L - 1)) - _Cutoff): with all levels 0 nothing is drawn, so the
//                   default state (game: siru levels 0) is invisible.
//  Surface          Built-in Standard premultiplied transparency: albedo 0.7 (linear), metallic 0, smoothness 0.9,
//                   alpha 0.15 * v; colour = diffuse * alpha + specular, blend One OneMinusSrcAlpha, output alpha
//                   0.04 + 0.96 * alpha (Standard's reflectivity term), ZWrite off. The highlight has the GGX lobe of
//                   smoothness 0.9 (lobe scan over 0..20 degrees) but only 1/20 of the 0.04 F0: F0 = 0.002.
//  No measurable effect: _ChromaticAberration, _UVScalePattern (the original's grab pass shows no displacement
//  of the background on flat or tilted probes, so the replacement blends instead of refracting).
// Properties the game writes: _WeatheringRange1..4 = 0 / 0.5 / 1 for siru levels 0 / 1 / 2 (front top,
// front bottom, back top, back bottom). Fit: see Assets/HS2Next/Shaders/README.md.
Shader "HS2Next/AIT Liquid"
{
    Properties
    {
        _Cutoff ("Mask Clip Value", Float) = 0.5
        _UVScalePattern ("UV Scale Pattern", Vector) = (1,1,0,0)
        [Header(Refraction)] _ChromaticAberration ("Chromatic Aberration", Range(0,0.3)) = 0.1
        _WeatheringMap ("WeatheringMap", 2D) = "black" {}
        _WeatheringMask ("WeatheringMask", 2D) = "black" {}
        _WeatheringUV ("WeatheringUV", Vector) = (3,3,0,0)
        _WeatheringAll ("WeatheringAll", Range(0,1)) = 0
        _WeatheringRange1 ("WeatheringRange1", Range(0,1)) = 0
        _WeatheringRange2 ("WeatheringRange2", Range(0,1)) = 0
        _WeatheringRange3 ("WeatheringRange3", Range(0,1)) = 0
        _WeatheringRange4 ("WeatheringRange4", Range(0,1)) = 0
        _WeatheringRange5 ("WeatheringRange5", Range(0,1)) = 0
        _WeatheringRange6 ("WeatheringRange6", Range(0,1)) = 0
        [HideInInspector] _texcoord ("", 2D) = "white" {}
        [HideInInspector] __dirty ("", Float) = 1
        [HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-1" "IgnoreProjector"="True" }
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

            #define HS2_LIQUID_ALBEDO 0.7
            #define HS2_LIQUID_ALPHA  0.15
            #define HS2_LIQUID_SMOOTH 0.9
            #define HS2_LIQUID_CLIP   20.0
            #define HS2_LIQUID_F0     0.00203

            TEXTURE2D(_WeatheringMap);  SAMPLER(sampler_WeatheringMap);
            TEXTURE2D(_WeatheringMask); SAMPLER(sampler_WeatheringMask);
            CBUFFER_START(UnityPerMaterial)
                float4 _WeatheringMap_ST, _WeatheringMask_ST, _UVScalePattern, _WeatheringUV;
                float _Cutoff, _ChromaticAberration, _WeatheringAll;
                float _WeatheringRange1, _WeatheringRange2, _WeatheringRange3, _WeatheringRange4, _WeatheringRange5, _WeatheringRange6;
                float _HS2FogModes;
            CBUFFER_END

            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1; float3 n : TEXCOORD2; };

            V vert(A a)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
                o.pos = p.positionCS; o.wpos = p.positionWS;
                o.n = TransformObjectToWorldNormal(a.n);
                o.uv = a.uv;
                return o;
            }

            // Level of fluid at this texel: global amount plus the region amounts selected by the mask colour.
            half LiquidLevel(float2 uv)
            {
                half3 m = SAMPLE_TEXTURE2D(_WeatheringMask, sampler_WeatheringMask, uv).rgb;
                half w1 = saturate(m.r - max(m.g, m.b)), w2 = saturate(m.g - max(m.r, m.b)), w3 = saturate(m.b - max(m.r, m.g));
                half w4 = saturate(min(m.r, m.g) - m.b), w5 = saturate(min(m.r, m.b) - m.g), w6 = saturate(min(m.g, m.b) - m.r);
                return _WeatheringAll + min(w1, _WeatheringRange1) + min(w2, _WeatheringRange2) + min(w3, _WeatheringRange3)
                     + min(w4, _WeatheringRange4) + min(w5, _WeatheringRange5) + min(w6, _WeatheringRange6);
            }

            half4 frag(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                half L = LiquidLevel(i.uv);
                half4 d = SAMPLE_TEXTURE2D(_WeatheringMap, sampler_WeatheringMap, -(i.uv * _WeatheringUV.xy + _WeatheringUV.zw));
                clip(HS2_LIQUID_CLIP * max(min(d.b, L), min(d.r, 2 * L - 1)) - _Cutoff + 1e-4);
                half v = max(min(d.b, saturate(2 * L)), min(d.r, saturate(2 * L - 1)));
                half alpha = HS2_LIQUID_ALPHA * v;

                float3 n = normalize(i.n) * (front ? 1 : -1);
                // Premultiplied (Standard _ALPHAPREMULTIPLY_ON): diffuse scaled by alpha, specular is not.
                HS2Surface s = HS2SurfaceMetallic(HS2_LIQUID_ALBEDO * alpha, 0, HS2_LIQUID_SMOOTH, 1, n);
                s.specColor = HS2_LIQUID_F0;   // highlights about 1/20 of the 0.04 that sets the transmission
                half3 c = HS2Shade(s, i.wpos, GetNormalizedScreenSpaceUV(i.pos));
                return half4(HS2ApplyFog(c, i.wpos, _HS2FogModes), 1 - s.oneMinusReflectivity + alpha * s.oneMinusReflectivity);
            }
            ENDHLSL
        }
    }
}
