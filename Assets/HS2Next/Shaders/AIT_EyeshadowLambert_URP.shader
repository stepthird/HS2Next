// URP replacement for HS2 "AIT/main eyeshadow lambert" (eye-socket shadow o_eyeshadow, material c_m_eyekage*,
// queue 2450, game keywords _ALPHABLEND_ON _COLOR4SWICH_ON _NODE_2929_ON _WRAPTEXBASE_ON). Same property
// names, types and defaults as the original. Behaviour inferred by black-box probing only
// (Assets/HS2Next/Editor/Fit/SkinFit.cs, Tests/FitCases/eyeshadow.txt, Spike/out/fit/AIT_main_eyeshadow_lambert_*.txt):
//  Multiplies what is behind it (blend DstColor Zero), unlit despite the name (no change with any light):
//  dst *= lerp(1, _Color.rgb, _Color.a * pow(_MainTex.a, _ShadowScale)). _MainTex RGB is unused.
// The game writes _ShadowScale = lerp(0.1, 0.9, whiteShadowScale): a smaller value widens the shadow.
Shader "HS2Next/AIT main eyeshadow lambert"
{
    Properties
    {
        _Color ("Color", Color) = (0,0,0,0)
        _MainTex ("MainTex", 2D) = "white" {}
        _ShadowScale ("Shadow Scale", Range(0.1,0.9)) = 0.5
        [HideInInspector] _texcoord ("", 2D) = "white" {}
        [HideInInspector] __dirty ("", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        Pass
        {
            Name "Multiply"
            Tags { "LightMode"="UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _Color;
                float _ShadowScale;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A a)
            {
                V o;
                o.pos = TransformObjectToHClip(a.pos.xyz);
                o.uv = TRANSFORM_TEX(a.uv, _MainTex);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                half w = _Color.a * exp2(_ShadowScale * log2(max(a, 1e-6)));
                return half4(lerp(half3(1, 1, 1), _Color.rgb, w), 1);
            }
            ENDHLSL
        }
    }
}
