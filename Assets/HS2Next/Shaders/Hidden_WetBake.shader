// The distance picture of a stain (WetBake.cs): per texel of a part's UV space, how far the stain must spread to reach it.
//   Raster   a mesh with the UVs as positions and the distance in z, into an R16 target with BlendOp Min (overlapping triangles keep
//            the least), cleared to 1, the distance scaled by 1 / 16;
//   Dilate   an empty texel takes the least of its four neighbours;
//   Finish   the R8 map: v = round(254 d / reach), 255 where nothing arrives (an empty texel, or d >= reach);
//   Red      the red channel of a decoded picture into an R8 target.
Shader "HS2Next/Hidden/WetBake"
{
    Properties { _MainTex ("", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        float4 _WetTexel;
        float _WetReach;
        struct BlitA { float4 pos : POSITION; float2 uv : TEXCOORD0; };
        struct BlitV { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
        BlitV BlitVert(BlitA a) { BlitV o; o.pos = TransformObjectToHClip(a.pos.xyz); o.uv = a.uv; return o; }
        ENDHLSL

        Pass
        {
            Name "Raster"
            ZTest Always ZWrite Off Cull Off
            BlendOp Min
            Blend One One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            struct A { float3 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; float d : TEXCOORD0; };
            V Vert(A a)
            {
                V o;
                float2 p = a.pos.xy * 2 - 1;
                #if UNITY_UV_STARTS_AT_TOP
                p.y = -p.y;
                #endif
                o.pos = float4(p, 0.5, 1);
                o.d = a.pos.z;
                return o;
            }
            float4 Frag(V i) : SV_Target { return saturate(max(i.d, 0) * (1.0 / 16.0)).xxxx; }
            ENDHLSL
        }

        Pass
        {
            Name "Dilate"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex BlitVert
            #pragma fragment Frag
            float4 Frag(BlitV i) : SV_Target
            {
                float c = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, i.uv, 0).r;
                if (c >= 0.99999)
                {
                    c = min(c, SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, i.uv + float2(_WetTexel.x, 0), 0).r);
                    c = min(c, SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, i.uv - float2(_WetTexel.x, 0), 0).r);
                    c = min(c, SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, i.uv + float2(0, _WetTexel.y), 0).r);
                    c = min(c, SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, i.uv - float2(0, _WetTexel.y), 0).r);
                }
                return c.xxxx;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Finish"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex BlitVert
            #pragma fragment Frag
            float4 Frag(BlitV i) : SV_Target
            {
                float s = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, i.uv, 0).r;
                float d = s * 16.0;
                if (s >= 0.99999 || d >= _WetReach) return 1;
                return (round(254.0 * saturate(d / _WetReach)) / 255.0).xxxx;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Red"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex BlitVert
            #pragma fragment Frag
            float4 Frag(BlitV i) : SV_Target { return SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, i.uv, 0).rrrr; }
            ENDHLSL
        }
    }
}
