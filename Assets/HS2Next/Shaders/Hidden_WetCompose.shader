// A stain over a texture at one moment (WetCompose.cs). The amount at a texel is a function of the stain's distance picture and the
// radius: 0 where the picture holds 255 or R <= 0, else saturate((R - d) / feather), d = v / 254 * reach.
//   Colour   l = .3 r + .59 g + .11 b; w = (l + (c - l) saturate) tint; with an amount, lerp(w, stain (.55 + .45 l), amount);
//            out = lerp(c, w, a skin), on sRGB-encoded values: an sRGB input is converted to them and back per texel. Alpha is the
//            input's. A texel the stain does not reach returns the input as sampled.
//   Mask     the amount, with BlendOp Max: the film's mask of a target is the largest amount of its stains.
Shader "HS2Next/Hidden/WetCompose"
{
    Properties { _MainTex ("", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_WetMap); SAMPLER(sampler_WetMap);
        float4 _WetSpread;   // x radius, y feather, z reach, w the kind's factor
        float4 _WetTint;     // rgb tint, a saturate
        float4 _WetStain;    // rgb stain colour, a amount
        float _WetEncoded;   // 1: the input is sRGB data, sampled as linear values
        struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
        struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
        V Vert(A a) { V o; o.pos = TransformObjectToHClip(a.pos.xyz); o.uv = a.uv; return o; }
        float Amount(float2 uv)
        {
            float s = SAMPLE_TEXTURE2D_LOD(_WetMap, sampler_WetMap, uv, 0).r;
            if (s >= 254.5 / 255.0 || _WetSpread.x <= 0) return 0;
            float d = s * (255.0 / 254.0) * _WetSpread.z;
            return saturate((_WetSpread.x - d) / _WetSpread.y);
        }
        ENDHLSL

        Pass
        {
            Name "Colour"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Frag(V i) : SV_Target
            {
                float4 src = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, i.uv, 0);
                float a = Amount(i.uv) * _WetSpread.w;
                if (a <= 0) return src;
                float3 c = _WetEncoded > 0.5 ? LinearToSRGB(saturate(src.rgb)) : src.rgb;
                float l = dot(c, float3(0.3, 0.59, 0.11));
                float3 w = (l + (c - l) * _WetTint.a) * _WetTint.rgb;
                if (_WetStain.a > 0) w = lerp(w, _WetStain.rgb * (0.55 + 0.45 * l), _WetStain.a);
                float3 o = saturate(lerp(c, w, a));
                return float4(_WetEncoded > 0.5 ? SRGBToLinear(o) : o, src.a);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Mask"
            ZTest Always ZWrite Off Cull Off
            BlendOp Max
            Blend One One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            float4 Frag(V i) : SV_Target { return Amount(i.uv).xxxx; }
            ENDHLSL
        }
    }
}
