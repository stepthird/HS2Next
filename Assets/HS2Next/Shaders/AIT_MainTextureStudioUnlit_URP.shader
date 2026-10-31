// URP replacement for HS2 "AIT/main texture studio unlit" (the Studio image panel, vanilla item 975). Same property
// names as the original so a loaded material keeps its values and the panel's writes (_MainTex, _Color, _patternuv1,
// _patternuv1Rotator, _patternclamp1) land as in HS2. Inferred by black-box probing only (Editor/Fit/SkinFit.cs,
// Tests/FitCases/panel_decode.txt, panel_fit.txt). Measured:
//   unlit; colour = MainTex.rgb * _Color.rgb (gamma-converted colour, values above 1 kept), alpha = MainTex.a (the colour's
//   alpha has no effect); blended SrcAlpha / OneMinusSrcAlpha; clip(MainTex.a - _Cutoff); both faces drawn;
//   main texture UV = rotate((uv0 + q.zw - 0.5) * q.xy + 0.5, -pi * _patternuv1Rotator) about 0.5 with q = _patternuv1,
//   saturated when _patternclamp1 = 1 (the texture's own tiling / offset is ignored); _UVScroll1 showed no effect in the
//   Editor's batch renders. Depth write not measurable on the single panel quad: ZWrite On like the measured
//   "AIT/main standard2 studio alpha".
Shader "HS2Next/AIT main texture studio unlit"
{
    Properties
    {
        _patternuv1 ("pattern uv 1", Vector) = (1.000,1.000,0.000,0.000)
        _patternuv1Rotator ("pattern uv 1 Rotator", Range(-1, 1)) = 0
        _patternclamp1 ("patternclamp1", Range(0, 1)) = 0
        _UVScroll1 ("UVScroll1", Range(0, 1)) = 0
        _Color ("Color", Color) = (1.000,1.000,1.000,1.000)
        _Cutoff ("Mask Clip Value", Float) = 0.01
        _MainTex ("Main Tex", 2D) = "white" {}
        _texcoord ("texcoord", 2D) = "white" {}
        __dirty ("dirty", Float) = 1
        [HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-50" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "HS2NextFog.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _patternuv1, _Color;
            float _patternuv1Rotator, _patternclamp1, _UVScroll1, _Cutoff;
            float _HS2FogModes;
        CBUFFER_END
        struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
        struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
        float2 PanelUV(float2 uv)
        {
            float2 t = (uv + _patternuv1.zw - 0.5) * _patternuv1.xy;
            float s, c; sincos(-PI * _patternuv1Rotator, s, c);
            t = float2(c * t.x - s * t.y, s * t.x + c * t.y) + 0.5;
            return _patternclamp1 > 0.5 ? saturate(t) : t;
        }
        V Vert(A a) { V o; o.pos = TransformObjectToHClip(a.pos.xyz); o.uv = a.uv; return o; }
        half4 Frag(V i) : SV_Target
        {
            half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, PanelUV(i.uv));
            clip(t.a - _Cutoff);
            return half4(HS2ApplyFogCS(t.rgb * _Color.rgb, i.pos, _HS2FogModes), t.a);
        }
        float3 _LightDirection;
        float3 _LightPosition;
        V ShadowVert(A a)
        {
            V o;
            float3 wpos = TransformObjectToWorld(a.pos.xyz);
            float3 n = TransformObjectToWorldNormal(a.n);
        #if _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 lightDir = normalize(_LightPosition - wpos);
        #else
            float3 lightDir = _LightDirection;
        #endif
            float4 cs = TransformWorldToHClip(ApplyShadowBias(wpos, n, lightDir));
        #if UNITY_REVERSED_Z
            cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
        #else
            cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
        #endif
            o.pos = cs; o.uv = a.uv;
            return o;
        }
        half4 DepthFrag(V i) : SV_Target
        {
            clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, PanelUV(i.uv)).a - _Cutoff);
            return 0;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            Cull Off
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment DepthFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
    }
}
