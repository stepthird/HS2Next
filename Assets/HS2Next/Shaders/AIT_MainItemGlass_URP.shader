// URP replacement for HS2 "AIT/main Item Glass" (glasses lenses and other clear accessory parts). Same property
// names as the original so a loaded material keeps its values. Inferred by black-box probing only
// (Editor/Fit/SkinFit.cs, Tests/FitCases/glass.txt):
//   albedo = MainTex.rgb * Color.rgb, alpha = MainTex.a * Color.a, metallic = _Metallic4, smoothness = _Glossiness4,
//   normal = Built-in UnpackScaleNormal(_BumpMap, _NormalScale), Cull Off with unflipped back-face normals;
//   Built-in BRDF1 with everything (diffuse, specular) scaled by alpha: Blend SrcAlpha OneMinusSrcAlpha.
//   No occlusion or emission; no main-light shadows received (transparent queue). ZWrite Off is assumed.
Shader "HS2Next/AIT main Item Glass"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Main Tex", 2D) = "white" {}
        _Glossiness4 ("Glossiness 4", Range(0, 1)) = 0.5
        _Metallic4 ("Metallic 4", Range(0, 1)) = 0
        _BumpMap ("Bump Map", 2D) = "bump" {}
        _NormalScale ("Normal Scale", Float) = 1
        _texcoord ("texcoord", 2D) = "white" {}
        __dirty ("dirty", Float) = 1
        [HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "HS2NextFog.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _Glossiness4, _Metallic4, _NormalScale;
            float _HS2FogModes;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #include "HS2NextBRDF.hlsl"

            struct A { float4 pos : POSITION; float3 n : NORMAL; float4 t : TANGENT; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1; float3 n : TEXCOORD2; float4 t : TEXCOORD3; };

            V vert(A a)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
                VertexNormalInputs nn = GetVertexNormalInputs(a.n, a.t);
                o.pos = p.positionCS; o.wpos = p.positionWS;
                o.n = nn.normalWS; o.t = float4(nn.tangentWS, a.t.w * GetOddNegativeScale());
                o.uv = a.uv;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half4 packed = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv);
                packed.x *= packed.w;
                half3 nTS; nTS.xy = (packed.xy * 2 - 1) * _NormalScale; nTS.z = sqrt(1 - saturate(dot(nTS.xy, nTS.xy)));
                float3 n = normalize(i.n), t = normalize(i.t.xyz), b = cross(n, t) * i.t.w;
                HS2Surface s = HS2SurfaceMetallic(tex.rgb * _Color.rgb, _Metallic4, _Glossiness4, 1, normalize(mul(nTS, float3x3(t, b, n))));
                return half4(HS2ApplyFog(HS2Shade(s, i.wpos, GetNormalizedScreenSpaceUV(i.pos)), i.wpos, _HS2FogModes), tex.a * _Color.a);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;
            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A a)
            {
                V o;
                o.pos = TransformWorldToHClip(ApplyShadowBias(TransformObjectToWorld(a.pos.xyz), TransformObjectToWorldNormal(a.n), _LightDirection));
                o.uv = a.uv;
                return o;
            }
            // Approximation: shadows only from parts with alpha >= 0.5 (clear lenses cast none).
            half4 frag(V i) : SV_Target { clip(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a * _Color.a - 0.5); return 0; }
            ENDHLSL
        }
    }
}
