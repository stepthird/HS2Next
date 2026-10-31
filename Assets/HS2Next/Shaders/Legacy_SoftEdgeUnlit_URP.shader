// URP stand-in for Unity's built-in "Legacy Shaders/Transparent/Cutout/Soft Edge Unlit" (its Unity 4 name "Transparent/Cutout/
// Soft Edge Unlit" too; mod maps, e.g. 2155X hangar). Published semantics (bundle property list and pass state agree): unlit
// _Color x _MainTex, Cull Off; first the pixels at or above _Cutoff opaque (ZWrite On), then, as Built-in's second pass with
// RequireOption SoftVegetation, the pixels below _Cutoff blended (SrcAlpha / OneMinusSrcAlpha, ZWrite Off). URP draws the
// SRPDefaultUnlit pass of an object before its UniversalForward pass, which gives that order. Fog is not applied.
Shader "HS2Next/Legacy Shaders/Transparent/Cutout/Soft Edge Unlit"
{
    Properties
    {
        _Color ("Main Color", Color) = (1,1,1,1)
        _MainTex ("Base (RGB) Alpha (A)", 2D) = "white" {}
        _Cutoff ("Base Alpha cutoff", Range(0, 0.9)) = 0.5
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "IgnoreProjector"="True" "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Color;
            float _Cutoff;
        CBUFFER_END
        struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
        struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
        V Vert(A a) { V o; o.pos = TransformObjectToHClip(a.pos.xyz); o.uv = TRANSFORM_TEX(a.uv, _MainTex); return o; }
        half4 Col(float2 uv) { return _Color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv); }
        half4 FragOpaque(V i) : SV_Target { half4 c = Col(i.uv); clip(c.a - _Cutoff); return c; }
        half4 FragSoft(V i) : SV_Target { half4 c = Col(i.uv); clip(_Cutoff - c.a); return c; }
        half4 FragDepth(V i) : SV_Target { clip(Col(i.uv).a - _Cutoff); return 0; }
        ENDHLSL
        Pass
        {
            Name "Opaque"
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragOpaque
            ENDHLSL
        }
        Pass
        {
            Name "SoftEdge"
            Tags { "LightMode"="UniversalForward" }
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragSoft
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepth
            ENDHLSL
        }
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode"="MotionVectors" }
            ColorMask RG
            ZWrite On
            HLSLPROGRAM
            #pragma vertex HS2MotionVert
            #pragma fragment HS2MotionFrag
            #define HS2_PASS_A A
            #define HS2_PASS_V V
            #define HS2_PASS_VERT Vert
            #define HS2_PASS_CLIP FragDepth
            #define HS2_PASS_POS(a) (a).pos.xyz
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ColorMask RGBA
            ZWrite On
            HLSLPROGRAM
            #pragma vertex HS2DepthNormalsVert
            #pragma fragment HS2DepthNormalsFrag
            #define HS2_PASS_A A
            #define HS2_PASS_V V
            #define HS2_PASS_VERT Vert
            #define HS2_PASS_CLIP FragDepth
            #define HS2_PASS_POS(a) (a).pos.xyz
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
    }
}
