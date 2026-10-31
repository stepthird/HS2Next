// Approximate URP stand-in for "Effects/Ice/IceFront" (the ice wall of vanilla map 34 and a mod effect item; bundle property
// list and pass state: lit forward base / add passes, SrcAlpha / OneMinusSrcAlpha, colour mask RGB, ZWrite Off, Cull Back,
// queue Transparent+1). Not probed: _MainTex x _Color lit by SH and every light (x _LightStr) with a BlinnPhong highlight
// (_SpecColor, _Shininess, gloss = _MainTex.a), plus _Cube reflection x _ReflectColor x _ReflectionStrength weighted by the
// Fresnel term _R0 + (1 - _R0)(1 - N.V)^_FPOW; alpha _Opacity raised by the same Fresnel term. Refraction of the scene
// behind (the original's look through the ice) is not reproduced (no opaque texture before Phase 3.3).
Shader "HS2Next/Effects/Ice/IceFront"
{
    Properties
    {
        _Color ("Main Color", Color) = (1,1,1,1)
        _SpecColor ("Specular Color", Color) = (0.5,0.5,0.5,1)
        _Shininess ("Shininess", Range(0.01, 1)) = 0.078125
        _ReflectColor ("Reflection Color", Color) = (1,1,1,0.5)
        _ReflectionStrength ("ReflectionStrength", Range(1, 20)) = 1
        _MainTex ("Base (RGB) Emission Tex (A)", 2D) = "white" {}
        _Opacity ("Material opacity", Range(-1, 1)) = 0.5
        _Cube ("Reflection Cubemap", Cube) = "" {}
        _BumpMap ("Normalmap", 2D) = "bump" {}
        _FPOW ("FPOW Fresnel", Float) = 5
        _R0 ("R0 Fresnel", Float) = 0.05
        _Cutoff ("Cutoff", Range(0, 1)) = 0.5
        _LightStr ("Light strength", Range(0, 5)) = 1
        [HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent+1" "IgnoreProjector"="True" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 200
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            ZWrite Off
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
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURECUBE(_Cube); SAMPLER(sampler_Cube);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _BumpMap_ST;
                half4 _Color, _SpecColor, _ReflectColor;
                float _Shininess, _ReflectionStrength, _Opacity, _FPOW, _R0, _Cutoff, _LightStr;
                float _HS2FogModes;
            CBUFFER_END
            struct A { float4 pos : POSITION; float3 n : NORMAL; float4 t : TANGENT; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float4 uv : TEXCOORD0; float3 wpos : TEXCOORD1; float3 n : TEXCOORD2; float3 t : TEXCOORD3; float3 b : TEXCOORD4; };
            V Vert(A a)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
                VertexNormalInputs nn = GetVertexNormalInputs(a.n, a.t);
                o.pos = p.positionCS; o.wpos = p.positionWS; o.n = nn.normalWS; o.t = nn.tangentWS;
                o.b = cross(nn.normalWS, nn.tangentWS) * a.t.w * GetOddNegativeScale();
                o.uv = float4(TRANSFORM_TEX(a.uv, _MainTex), TRANSFORM_TEX(a.uv, _BumpMap));
                return o;
            }
            half3 One(half3 albedo, half gloss, float3 n, float3 v, float3 l, half3 lc)
            {
                float3 h = normalize(l + v);
                return lc * (albedo * saturate(dot(n, l)) * _LightStr + _SpecColor.rgb * pow(saturate(dot(n, h)), _Shininess * 128.0) * gloss);
            }
            half4 Frag(V i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy);
                half3 albedo = tex.rgb * _Color.rgb;
                half4 packed = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv.zw);
                packed.x *= packed.w;
                half3 nt; nt.xy = packed.xy * 2 - 1; nt.z = sqrt(1 - saturate(dot(nt.xy, nt.xy)));
                float3 n = normalize(i.t * nt.x + i.b * nt.y + i.n * nt.z);
                float3 v = GetWorldSpaceNormalizeViewDir(i.wpos);
                half3 c = albedo * SampleSH(n);
                Light main = GetMainLight(TransformWorldToShadowCoord(i.wpos));
                main.color *= HS2LightLayerWeight(main.layerMask);
                c += One(albedo, tex.a, n, v, main.direction, main.color * main.distanceAttenuation * main.shadowAttenuation);
#if defined(_ADDITIONAL_LIGHTS)
                HS2LightIter it = HS2LightIterInit(i.wpos, GetNormalizedScreenSpaceUV(i.pos));
                uint lightIndex;
                [loop] while (HS2LightNext(it, lightIndex))
                {
                    Light L = GetAdditionalLight(lightIndex, i.wpos, half4(1, 1, 1, 1));
                    if (!HS2LightReaches(L, lightIndex)) continue;
                    c += One(albedo, tex.a, n, v, L.direction, L.color * L.distanceAttenuation * L.shadowAttenuation);
                }
#endif
                half fres = saturate(_R0 + (1 - _R0) * pow(1 - saturate(dot(n, v)), _FPOW));
                c += SAMPLE_TEXTURECUBE(_Cube, sampler_Cube, reflect(-v, n)).rgb * _ReflectColor.rgb * _ReflectionStrength * fres;
                return half4(HS2ApplyFog(c, i.wpos, _HS2FogModes), saturate(_Opacity + fres));
            }
            ENDHLSL
        }
    }
}
