// URP stand-in for "FX/MirrorReflection_Transparent" (mirrors of 10 vanilla maps; bundle property list _MainTex, _Color,
// _ReflectionTex; one pass SrcAlpha / OneMinusSrcAlpha, ZWrite On, Cull Back, queue Transparent). Colour model black-box
// probed against the bundle copy with constant textures (Shaders/README.md "Map shaders"): rgb = _MainTex x _ReflectionTex
// x _Color.rgb, alpha = _MainTex.a x _Color.a (the reflection's alpha unused); the reflection is sampled at the
// fragment's screen position (MapMirrors renders it with the viewing camera's projection).
Shader "HS2Next/Compatible/FX/MirrorReflection_Transparent"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        _Color ("Main Color", Color) = (1,1,1,1)
        [HideInInspector] _ReflectionTex ("", 2D) = "white" {}
        [HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "HS2NextFog.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_ReflectionTex); SAMPLER(sampler_ReflectionTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float _HS2FogModes;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 sp : TEXCOORD1; };
            V Vert(A a) { V o; o.pos = TransformObjectToHClip(a.pos.xyz); o.uv = TRANSFORM_TEX(a.uv, _MainTex); o.sp = ComputeScreenPos(o.pos); return o; }
            half4 Frag(V i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half4 refl = SAMPLE_TEXTURE2D(_ReflectionTex, sampler_ReflectionTex, i.sp.xy / i.sp.w);
                // measured: rgb = texture x reflection x colour, alpha = texture alpha x colour alpha (the reflection's alpha unused)
                return half4(HS2ApplyFogCS(tex.rgb * refl.rgb * _Color.rgb, i.pos, _HS2FogModes), tex.a * _Color.a);
            }
            ENDHLSL
        }
    }
}
