// Approximate URP stand-in for Unity's Tree Creator Leaves shaders in mod maps ("Hidden/Nature/Tree Creator Leaves Optimized",
// "Nature/Tree Creator Leaves", "... Leaves Fast Optimized", "... Leaves Fast"). Not a fit (no Built-in reference of the
// optimized shaders in batch; terrain-tree wind and squash not reproduced): cutout bumped diffuse (_MainTex x _Color, clip at _Cutoff); translucency, the shadow texture and the leaves' specular are ignored.
// The normal map is _BumpSpecMap's G (y) and A (x) channels (Tree Creator's packing); the vertex alpha is its baked
// ambient occlusion. Lit with SH ambient and every light (HS2NextLights.hlsl), shadows cast and received.
// The two Tree Creator files share their code: change both together.
Shader "HS2Next/Nature/Tree Creator Leaves"
{
    Properties
    {
        _Color ("Main Color", Color) = (1,1,1,1)
        _TranslucencyColor ("Translucency Color", Color) = (0.73,0.85,0.41,1)
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.3
        _TranslucencyViewDependency ("View dependency", Range(0, 1)) = 0.7
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.8
        _ShadowOffsetScale ("Shadow Offset Scale", Float) = 1
        _MainTex ("Base (RGB) Alpha (A)", 2D) = "white" {}
        _ShadowTex ("Shadow (RGB)", 2D) = "white" {}
        _BumpSpecMap ("Normalmap (GA) Spec (R) Shadow Offset (B)", 2D) = "bump" {}
        _TranslucencyMap ("Trans (B) Gloss(A)", 2D) = "white" {}
        [HideInInspector] _TreeInstanceColor ("TreeInstanceColor", Vector) = (1,1,1,1)
        [HideInInspector] _TreeInstanceScale ("TreeInstanceScale", Vector) = (1,1,1,1)
        [HideInInspector] _SquashAmount ("Squash", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #define HS2_TREE_LEAVES
        ENDHLSL
        HLSLINCLUDE
        #include "HS2NextLights.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_BumpSpecMap); SAMPLER(sampler_BumpSpecMap);
        TEXTURE2D(_TranslucencyMap); SAMPLER(sampler_TranslucencyMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST, _BumpSpecMap_ST, _TranslucencyMap_ST, _TreeInstanceColor, _TreeInstanceScale;
            half4 _Color, _TranslucencyColor, _SpecColor;
            float _Cutoff, _TranslucencyViewDependency, _ShadowStrength, _ShadowOffsetScale, _SquashAmount;
        CBUFFER_END
        struct A { float4 pos : POSITION; float3 n : NORMAL; float4 t : TANGENT; float2 uv : TEXCOORD0; half4 color : COLOR; };
        struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1; float3 n : TEXCOORD2; float3 t : TEXCOORD3; float3 b : TEXCOORD4; half occ : TEXCOORD5; };
        V Vert(A a)
        {
            V o;
            VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
            VertexNormalInputs nn = GetVertexNormalInputs(a.n, a.t);
            o.pos = p.positionCS; o.wpos = p.positionWS; o.n = nn.normalWS; o.t = nn.tangentWS;
            o.b = cross(nn.normalWS, nn.tangentWS) * a.t.w * GetOddNegativeScale();
            o.uv = TRANSFORM_TEX(a.uv, _MainTex);
            o.occ = a.color.a;   // the Tree Creator's baked ambient occlusion
            return o;
        }
        half4 Albedo(float2 uv)
        {
            half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
            c.rgb *= _Color.rgb;
            return c;
        }
        float3 Normal(V i, float2 uv, out half4 bs)
        {
            bs = SAMPLE_TEXTURE2D(_BumpSpecMap, sampler_BumpSpecMap, uv);
            half3 t; t.xy = bs.ag * 2 - 1; t.z = sqrt(1 - saturate(dot(t.xy, t.xy)));   // normal x in A, y in G
            return normalize(i.t * t.x + i.b * t.y + i.n * t.z);
        }
        half3 Shade(half3 albedo, float3 n, float3 v, float3 wpos, float4 posCS, half spec, half gloss)
        {
            half3 c = albedo * SampleSH(n);
            Light main = GetMainLight(TransformWorldToShadowCoord(wpos));
            main.color *= HS2LightLayerWeight(main.layerMask);
            half3 lc = main.color * main.distanceAttenuation * main.shadowAttenuation;
            float3 h = normalize(main.direction + v);
            c += lc * (albedo * saturate(dot(n, main.direction)) + _SpecColor.rgb * pow(saturate(dot(n, h)), max(spec, 0.01) * 128.0) * gloss);
#if defined(_ADDITIONAL_LIGHTS)
            HS2LightIter it = HS2LightIterInit(wpos, GetNormalizedScreenSpaceUV(posCS));
            uint lightIndex;
            [loop] while (HS2LightNext(it, lightIndex))
            {
                Light L = GetAdditionalLight(lightIndex, wpos, half4(1, 1, 1, 1));
                if (!HS2LightReaches(L, lightIndex)) continue;
                half3 l2 = L.color * L.distanceAttenuation * L.shadowAttenuation;
                float3 h2 = normalize(L.direction + v);
                c += l2 * (albedo * saturate(dot(n, L.direction)) + _SpecColor.rgb * pow(saturate(dot(n, h2)), max(spec, 0.01) * 128.0) * gloss);
            }
#endif
            return c;
        }
        half4 Forward(V i) : SV_Target
        {
            half4 col = Albedo(i.uv);
#if defined(HS2_TREE_LEAVES)
            clip(col.a - _Cutoff);
#endif
            half4 bs;
            float3 n = Normal(i, i.uv, bs);
            half gloss = SAMPLE_TEXTURE2D(_TranslucencyMap, sampler_TranslucencyMap, i.uv).a;
#if defined(HS2_TREE_LEAVES)
            half spec = 0; gloss = 0;
#else
            half spec = bs.r; gloss *= _Color.r;
#endif
            half3 c = Shade(col.rgb * i.occ, n, GetWorldSpaceNormalizeViewDir(i.wpos), i.wpos, i.pos, spec, gloss);
            return half4(c, 1);
        }
        float3 _LightDirection;
        float3 _LightPosition;
        V ShadowVert(A a)
        {
            V o = Vert(a);
#if _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 l = normalize(_LightPosition - o.wpos);
#else
            float3 l = _LightDirection;
#endif
            float4 cs = TransformWorldToHClip(ApplyShadowBias(o.wpos, o.n, l));
#if UNITY_REVERSED_Z
            cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
#else
            cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
#endif
            o.pos = cs;
            return o;
        }
        half4 DepthFrag(V i) : SV_Target
        {
#if defined(HS2_TREE_LEAVES)
            clip(Albedo(i.uv).a - _Cutoff);
#endif
            return 0;
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Forward
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            ZWrite On
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment DepthFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
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
            #pragma fragment DepthFrag
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
            #define HS2_PASS_CLIP DepthFrag
            #define HS2_PASS_POS(a) (a).pos.xyz
            #define HS2_PASS_NORMAL(a) (a).n
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
            #define HS2_PASS_CLIP DepthFrag
            #define HS2_PASS_POS(a) (a).pos.xyz
            #define HS2_PASS_NORMAL(a) (a).n
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
    }
}
