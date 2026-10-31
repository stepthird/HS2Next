// Approximate URP stand-in for "Effects/SlimeCutOut" (the slime of vanilla map 34; bundle property list and pass state: a
// grab pass for the distortion, then SrcAlpha / OneMinusSrcAlpha, ZWrite Off, Cull Off, depth offset -1 / -1, queue
// Transparent). Not probed: _MainTex x _Color plus _Cube reflection (reflected view on the bumped normal) x _ReflectColor,
// alpha _CutOut.a x _Color.a. Distortion (Phase 3.3, package C; approximate, not probed): the original grabs the scene
// before drawing; here, while the global _HS2NextSceneOpaqueValid is 1 (SceneTextures), URP's opaque texture is sampled at
// the screen position shifted by the tangent-space normal x _BumpAmt texels (the Built-in "bump distort" convention) and
// the slime is composited over that shifted background: rgb = c a + shifted (1 - a), drawn with alpha saturate(8 a) so
// the cut-out edges still fade. Transparents behind the slime are not in the opaque texture. Without
// the opaque texture (measurement asset of the fit tools) the shader draws as before.
Shader "HS2Next/Effects/SlimeCutOut"
{
    Properties
    {
        _Color ("Main Color", Color) = (1,1,1,1)
        _ReflectColor ("Reflection Color", Color) = (1,1,1,0.5)
        _MainTex ("Base (RGB) Emission Tex (A)", 2D) = "white" {}
        _CutOut ("CutOut (A)", 2D) = "white" {}
        _Cube ("Reflection Cubemap", Cube) = "" {}
        _BumpMap ("Normalmap", 2D) = "bump" {}
        _BumpAmt ("Distortion", Float) = 10
        [HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "HS2NextFog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_CutOut); SAMPLER(sampler_CutOut);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURECUBE(_Cube); SAMPLER(sampler_Cube);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _CutOut_ST, _BumpMap_ST;
                half4 _Color, _ReflectColor;
                float _BumpAmt;
                float _HS2FogModes;
            CBUFFER_END
            float _HS2NextSceneOpaqueValid;
            struct A { float4 pos : POSITION; float3 n : NORMAL; float4 t : TANGENT; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float4 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; float3 wpos : TEXCOORD2; float3 n : TEXCOORD3; float3 t : TEXCOORD4; float3 b : TEXCOORD5; };
            V Vert(A a)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
                VertexNormalInputs nn = GetVertexNormalInputs(a.n, a.t);
                o.pos = p.positionCS; o.wpos = p.positionWS; o.n = nn.normalWS; o.t = nn.tangentWS;
                o.b = cross(nn.normalWS, nn.tangentWS) * a.t.w * GetOddNegativeScale();
                o.uv = float4(TRANSFORM_TEX(a.uv, _MainTex), TRANSFORM_TEX(a.uv, _BumpMap));
                o.uv2 = TRANSFORM_TEX(a.uv, _CutOut);
                return o;
            }
            half4 Frag(V i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy);
                half4 packed = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv.zw);
                packed.x *= packed.w;
                half3 nt; nt.xy = packed.xy * 2 - 1; nt.z = sqrt(1 - saturate(dot(nt.xy, nt.xy)));
                float3 n = normalize(i.t * nt.x + i.b * nt.y + i.n * nt.z);
                float3 v = GetWorldSpaceNormalizeViewDir(i.wpos);
                half3 c = tex.rgb * _Color.rgb + SAMPLE_TEXTURECUBE(_Cube, sampler_Cube, reflect(-v, n)).rgb * _ReflectColor.rgb;
                half a = SAMPLE_TEXTURE2D(_CutOut, sampler_CutOut, i.uv2).a * _Color.a;
                if (_HS2NextSceneOpaqueValid > 0)
                {
                    float2 uv = GetNormalizedScreenSpaceUV(i.pos);
                    float2 texel = 1.0 / _ScaledScreenParams.xy;
                    half3 bg = SampleSceneColor(uv + nt.xy * _BumpAmt * texel);
                    return half4(HS2ApplyFog(c * a + bg * (1 - a), i.wpos, _HS2FogModes), saturate(a * 8));
                }
                return half4(HS2ApplyFog(c, i.wpos, _HS2FogModes), a);
            }
            ENDHLSL
        }
    }
}
