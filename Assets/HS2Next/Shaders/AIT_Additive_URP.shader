// URP replacement for HS2's "AIT/Additive" (Amplify, unlit; vanilla glow parts such as the mech beam pole's beam), fitted
// black-box against the Built-in original (Tests/FitCases/ait_additive.txt): blend One / OneMinusSrcAlpha, ZWrite Off,
// Cull Off; k = _Emissive + 9 _Glossiness, colour _Color.rgb x _Color.a x k (HDR, not clamped), alpha
// saturate(_Color.a^2 x k); fragments with _MainTex alpha 0 are discarded (its colour has no effect); _TimeMask, the
// UV panner (_UVPannerTime / Speedx / Speedy), _Vector0 and _Metallic have no measurable effect; no lighting, no vertex
// colour.
Shader "HS2Next/AIT Additive"
{
    Properties
    {
        _Color ("Color", Color) = (1,0,0,0)
        [NoScaleOffset] _MainTex ("MainTex", 2D) = "white" {}
        _Emissive ("Emissive", Float) = 0
        [NoScaleOffset] _TimeMask ("TimeMask", 2D) = "white" {}
        _UVPannerTime ("UVPannerTime", Float) = 0
        _UVPannerSpeedy ("UVPannerSpeedy", Float) = 0
        _UVPannerSpeedx ("UVPannerSpeedx", Float) = 0
        _Vector0 ("Vector 0", Vector) = (1,1,0,0)
        _Glossiness ("Glossiness", Range(0, 1)) = 0
        _Metallic ("Metallic", Float) = 0
        [HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Overlay" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "HS2NextFog.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _Vector0;
                float _Emissive, _UVPannerTime, _UVPannerSpeedy, _UVPannerSpeedx, _Glossiness, _Metallic;
                float _HS2FogModes;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A a) { V o; o.pos = TransformObjectToHClip(a.pos.xyz); o.uv = a.uv; return o; }
            half4 frag(V i) : SV_Target
            {
                if (SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a <= 0) discard;
                float k = _Emissive + 9.0 * _Glossiness;
                return half4(HS2ApplyFogAdditiveCS(_Color.rgb * _Color.a * k, i.pos, _HS2FogModes), saturate(_Color.a * _Color.a * k));
            }
            ENDHLSL
        }
    }
}
