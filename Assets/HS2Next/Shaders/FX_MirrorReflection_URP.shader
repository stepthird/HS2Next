// URP stand-ins for the planar mirror shaders of HS2 maps (M10): MapMirrors renders the
// reflection into _ReflectionTex; both shaders sample it at the fragment's screen position (the reflection camera shares
// the viewing camera's projection). "FX/MirrorReflection" (the published Unify-wiki mirror, 1 mod map): opaque, texture x
// reflection, not probed. "FX/MirrorReflection_Transparent" (10 vanilla maps): see FX_MirrorReflectionTransparent_URP.shader.
Shader "HS2Next/Compatible/FX/MirrorReflection"
{
    Properties
    {
        _MainTex ("Base (RGB)", 2D) = "white" {}
        [HideInInspector] _ReflectionTex ("", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_ReflectionTex); SAMPLER(sampler_ReflectionTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 sp : TEXCOORD1; };
            V Vert(A a) { V o; o.pos = TransformObjectToHClip(a.pos.xyz); o.uv = TRANSFORM_TEX(a.uv, _MainTex); o.sp = ComputeScreenPos(o.pos); return o; }
            half4 Frag(V i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half4 refl = SAMPLE_TEXTURE2D(_ReflectionTex, sampler_ReflectionTex, i.sp.xy / i.sp.w);
                return tex * refl;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 Vert(float4 pos : POSITION) : SV_POSITION { return TransformObjectToHClip(pos.xyz); }
            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode"="MotionVectors" }
            ColorMask RG
            HLSLPROGRAM
            #pragma vertex HS2MotionVert
            #pragma fragment HS2MotionFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 Vert(float4 pos : POSITION) : SV_POSITION { return TransformObjectToHClip(pos.xyz); }
            half4 Frag() : SV_Target { return 0; }
            #define HS2_PASS_SIMPLE
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ColorMask RGBA
            HLSLPROGRAM
            #pragma vertex HS2DepthNormalsVert
            #pragma fragment HS2DepthNormalsFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 Vert(float4 pos : POSITION) : SV_POSITION { return TransformObjectToHClip(pos.xyz); }
            half4 Frag() : SV_Target { return 0; }
            #define HS2_PASS_SIMPLE
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
    }
}
