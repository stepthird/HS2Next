// Approximate URP stand-in for "NatureManufacture Shaders/Water/Water Particles Foam" (the river foam particles of vanilla
// map 14; bundle property list and pass state: SrcAlpha / OneMinusSrcAlpha, colour mask RGB, ZWrite Off, Cull Off, queue
// Transparent; a lit surface shader). Albedo _ParticleTexture x _Color x vertex colour, alpha texture x _Opacity x vertex
// alpha, lit by SH and every light on the particle's normal; normal map, metallic / smoothness and AO not reproduced
// (approximation).
Shader "HS2Next/Compatible/Water Particles Foam"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,0)
        _ColorPower ("Color Power", Vector) = (5,5,5,0)
        _AOPower ("AO Power", Range(0, 1)) = 1
        _Opacity ("Opacity", Range(0, 1)) = 0.23
        _ParticleTexture ("Particle Texture", 2D) = "white" {}
        _ParticleNormalmap ("Particle Normalmap", 2D) = "bump" {}
        _NormalScale ("Normal Scale", Range(0, 2)) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.8
        _Metallic ("Metallic", Range(0, 1)) = 0.1
        [HideInInspector] _texcoord ("", 2D) = "white" {}
        [HideInInspector] __dirty ("", Float) = 1
        [HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent+0" "IgnoreProjector"="True" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #include "HS2NextLights.hlsl"
            #include "HS2NextFog.hlsl"
            TEXTURE2D(_ParticleTexture); SAMPLER(sampler_ParticleTexture);
            CBUFFER_START(UnityPerMaterial)
                float4 _ParticleTexture_ST, _ColorPower;
                half4 _Color;
                float _AOPower, _Opacity, _NormalScale, _Smoothness, _Metallic;
                float _HS2FogModes;
            CBUFFER_END
            struct A { float4 pos : POSITION; float3 n : NORMAL; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float3 n : TEXCOORD1; float3 wpos : TEXCOORD2; };
            V Vert(A a)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
                o.pos = p.positionCS; o.wpos = p.positionWS; o.n = TransformObjectToWorldNormal(a.n);
                o.color = a.color; o.uv = TRANSFORM_TEX(a.uv, _ParticleTexture);
                return o;
            }
            half4 Frag(V i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_ParticleTexture, sampler_ParticleTexture, i.uv);
                half3 albedo = tex.rgb * _Color.rgb * i.color.rgb;
                float3 n = normalize(i.n) * (IS_FRONT_VFACE(face, true, false) ? 1 : -1);
                Light main = GetMainLight(TransformWorldToShadowCoord(i.wpos));
                main.color *= HS2LightLayerWeight(main.layerMask);
                half3 c = albedo * (SampleSH(n) + main.color * main.shadowAttenuation * saturate(dot(n, main.direction)));
#if defined(_ADDITIONAL_LIGHTS)
                HS2LightIter it = HS2LightIterInit(i.wpos, GetNormalizedScreenSpaceUV(i.pos));
                uint lightIndex;
                [loop] while (HS2LightNext(it, lightIndex))
                {
                    Light L = GetAdditionalLight(lightIndex, i.wpos, half4(1, 1, 1, 1));
                    if (!HS2LightReaches(L, lightIndex)) continue;
                    c += albedo * L.color * L.distanceAttenuation * L.shadowAttenuation * saturate(dot(n, L.direction));
                }
#endif
                return half4(HS2ApplyFog(c, i.wpos, _HS2FogModes), saturate(tex.a * _Opacity * i.color.a));
            }
            ENDHLSL
        }
    }
}
