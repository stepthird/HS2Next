// URP replacement for HS2's "Shader Forge/main_item_studio_add" (vanilla Studio effects: wave rings, pillars, magic
// circles), fitted black-box against the Built-in original (Tests/FitCases/sf_item_add.txt): unlit, additive (One One),
// colour mask on _MainTex with priority B > G > R, combined by a per-channel max:
// c = max(saturate(R - max(G, B)^8) _Color, saturate(G - B^8) _Color2, B _Color3),
// times _alpha; texture and colour alphas have no effect; Cull Back, ZWrite Off.
Shader "HS2Next/Shader Forge/main_item_studio_add"
{
    Properties
    {
        _MainTex ("MainTex", 2D) = "white" {}
        _Color ("Color", Color) = (0.07843138,0.3921569,0.7843137,1)
        _Color2 ("Color2", Color) = (1,1,1,1)
        _Color3 ("Color3", Color) = (1,1,1,1)
        _alpha ("alpha", Range(0, 1)) = 1
        [HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "HS2NextFog.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _Color, _Color2, _Color3;
                float _alpha;
                float _HS2FogModes;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A a) { V o; o.pos = TransformObjectToHClip(a.pos.xyz); o.uv = TRANSFORM_TEX(a.uv, _MainTex); return o; }
            half4 frag(V i) : SV_Target
            {
                half3 m = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                // Mask priority B > G > R: a channel loses the eighth power of the stronger ones.
                half r = saturate(m.r - pow(max(m.g, m.b), 8));
                half g = saturate(m.g - pow(m.b, 8));
                half3 c = max(max(r * _Color.rgb, g * _Color2.rgb), m.b * _Color3.rgb);   // per channel
                return half4(HS2ApplyFogAdditiveCS(c * _alpha, i.pos, _HS2FogModes), 1);
            }
            ENDHLSL
        }
    }
}
