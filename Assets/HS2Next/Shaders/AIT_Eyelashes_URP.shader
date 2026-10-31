// URP replacement for HS2 "AIT/eyelashes" (o_eyelashes, material c_m_eyelashes, queue 2989, game keyword
// _ALPHABLEND_ON). Same property names, types and defaults as the original. Behaviour inferred by black-box
// probing only (Assets/HS2Next/Editor/Fit/SkinFit.cs, Tests/FitCases/eyelashes*.txt, Spike/out/fit/AIT_eyelashes_*.txt):
//  Coverage         a = pow(_MainTex.r, _CutoutScale + 2 * (1 - _Color.a)); clip(a * saturate(2 * _Color.a) - _Cutoff).
//                   Only the texture's R channel is used (G, B, A have no effect).
//  Colour           premultiplied: rgb = _Color.rgb * _MainTex.r * (ambient SH + light * saturate(N.L)) (plain
//                   Lambert, no specular), blend One OneMinusSrcAlpha with alpha a. Two-sided; back faces are lit
//                   with the front normal.
//  Not reproduced:  below the cutoff the original still lets a 4x4 screen-door fraction of the pixels through
//                   (1/16 .. 1/2 of a, depending on a and _Color.a); with the game values (cutoff 0.1) that is at
//                   most about 0.005 of coverage on the faint lash tips.
// The game writes _MainTex = the eyelash texture and _Color = eyelashesColor.
Shader "HS2Next/AIT eyelashes"
{
    Properties
    {
        _Cutoff ("Mask Clip Value", Float) = 0.1
        _Color ("Color", Color) = (0,0,0,0)
        _MainTex ("Main Tex", 2D) = "white" {}
        _CutoutScale ("CutoutScale", Float) = 0
        [HideInInspector] _texcoord ("", 2D) = "white" {}
        [HideInInspector] __dirty ("", Float) = 1
        [HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "HS2NextLights.hlsl"
            #include "HS2NextFog.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _Color;
                float _Cutoff, _CutoutScale;
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
                o.uv = TRANSFORM_TEX(a.uv, _MainTex);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half r = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).r;
                half a = exp2((_CutoutScale + 2 * (1 - _Color.a)) * log2(max(r, 1e-8)));
                clip(a * saturate(2 * _Color.a) - _Cutoff);
                float3 n = normalize(i.n);   // two-sided, back faces keep the front normal (measured)
                half3 light = SampleSH(n);
                Light main = GetMainLight(TransformWorldToShadowCoord(i.wpos));
                main.color *= HS2LightLayerWeight(main.layerMask);
                light += main.color * main.distanceAttenuation * main.shadowAttenuation * saturate(dot(n, main.direction));
            #if defined(_ADDITIONAL_LIGHTS)
                HS2LightIter it = HS2LightIterInit(i.wpos, GetNormalizedScreenSpaceUV(i.pos));
                uint lightIndex;
                [loop] while (HS2LightNext(it, lightIndex))
                {
                    Light L = GetAdditionalLight(lightIndex, i.wpos, half4(1, 1, 1, 1));
                    if (!HS2LightReaches(L, lightIndex)) continue;
                    light += L.color * L.distanceAttenuation * L.shadowAttenuation * saturate(dot(n, L.direction));
                }
            #endif
                return half4(HS2ApplyFog(_Color.rgb * r * light, i.wpos, _HS2FogModes), a);
            }
            ENDHLSL
        }
    }
}
