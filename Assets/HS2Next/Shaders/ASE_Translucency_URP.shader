// URP replacement for the Amplify sample shader "ASESampleShaders/Translucency" (mod maps: the bushitsu curtains), black-box
// probed against the bundle copy (Phase 3.7 package L, Shaders/README.md "Lightmap paths"): Built-in's Standard Specular
// surface (HS2NextBRDF) plus Amplify's translucency term, with the Standard family's lightmap path (the lightmap replaces the
// SH ambient and feeds the translucency's ambient part; non-directional only, as the probed bundle copy draws a
// directional lightmap). Same property list as the original (14).
Shader "HS2Next/Compatible/ASESampleShaders/Translucency"
{
    Properties
    {
        _Specular ("Specular", Range(0, 1)) = 0
        _Color0 ("Color 0", Color) = (0,0,0,0)
        [Header(Translucency)] _Translucency ("Strength", Range(0, 50)) = 1
        _TransNormalDistortion ("Normal Distortion", Range(0, 1)) = 0.1
        _TransScattering ("Scaterring Falloff", Range(1, 50)) = 2
        _TransDirect ("Direct", Range(0, 1)) = 1
        _TransAmbient ("Ambient", Range(0, 1)) = 0.2
        _TransShadow ("Shadow", Range(0, 1)) = 0.9
        _Albedo ("Albedo", 2D) = "white" {}
        _Normal ("Normal", 2D) = "bump" {}
        _Depth ("Depth", 2D) = "white" {}
        _Tint ("Tint", Color) = (0,0,0,0)
        [HideInInspector] _texcoord ("", 2D) = "white" {}
        [HideInInspector] __dirty ("", Int) = 1
    }
    HLSLINCLUDE
    #include "HS2NextBRDF.hlsl"
    TEXTURE2D(_Albedo); SAMPLER(sampler_Albedo);
    TEXTURE2D(_Normal); SAMPLER(sampler_Normal);
    TEXTURE2D(_Depth); SAMPLER(sampler_Depth);
    CBUFFER_START(UnityPerMaterial)
        float4 _Color0, _Tint, _Albedo_ST, _Normal_ST, _Depth_ST, _texcoord_ST;
        float _Specular, _Translucency, _TransNormalDistortion, _TransScattering, _TransDirect, _TransAmbient, _TransShadow;
    CBUFFER_END

    struct AseA { float4 pos : POSITION; float3 n : NORMAL; float4 t : TANGENT; float2 uv0 : TEXCOORD0; float2 uv1 : TEXCOORD1; };
    struct AseV
    {
        float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1; float3 n : TEXCOORD2; float3 t : TEXCOORD3; float3 b : TEXCOORD4;
    #if defined(LIGHTMAP_ON)
        float2 lmuv : TEXCOORD5;
    #endif
    };

    AseV AseVert(AseA a)
    {
        AseV o;
        VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
        VertexNormalInputs nn = GetVertexNormalInputs(a.n, a.t);
        o.pos = p.positionCS; o.wpos = p.positionWS;
        o.n = nn.normalWS; o.t = nn.tangentWS;
        o.b = cross(nn.normalWS, nn.tangentWS) * a.t.w * GetOddNegativeScale();
        o.uv = a.uv0;
    #if defined(LIGHTMAP_ON)
        o.lmuv = a.uv1 * unity_LightmapST.xy + unity_LightmapST.zw;
    #endif
        return o;
    }

    half4 AseDepthFrag(AseV i) : SV_Target { return 0; }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex AseVert
            #pragma fragment AseForwardFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            half4 AseForwardFrag(AseV i) : SV_Target
            {
                // ASE: the surface UV is uv0 x _texcoord_ST (the hidden _texcoord property), then each texture's own _ST
                float2 uv = i.uv * _texcoord_ST.xy + _texcoord_ST.zw;
                half3 albedo = SAMPLE_TEXTURE2D(_Albedo, sampler_Albedo, uv * _Albedo_ST.xy + _Albedo_ST.zw).rgb * _Tint.rgb;
                half4 packed = SAMPLE_TEXTURE2D(_Normal, sampler_Normal, uv * _Normal_ST.xy + _Normal_ST.zw);
                packed.x *= packed.w;
                half3 nTS; nTS.xy = packed.xy * 2 - 1; nTS.z = sqrt(1 - saturate(dot(nTS.xy, nTS.xy)));
                float3 n = normalize(i.t * nTS.x + i.b * nTS.y + i.n * nTS.z);   // Cull Off: back faces keep the front normal
                // Standard Specular surface, smoothness 0 (measured: the GGX lobe of roughness 1), occlusion 1
                HS2Surface s = HS2SurfaceSpecular(albedo, _Specular.xxx, 0, 1, n);
                // Amplify translucency: albedo x (_Color0 x _Depth) x light x (vdl x direct + baked / SH ambient x ambient) x strength
                half3 depth = SAMPLE_TEXTURE2D(_Depth, sampler_Depth, uv * _Depth_ST.xy + _Depth_ST.zw).rgb;
                s.transStrength = _Translucency; s.transColor = _Color0.rgb * depth;
                s.transDirect = _TransDirect; s.transAmbient = _TransAmbient; s.transScattering = _TransScattering;
                s.transDistortion = _TransNormalDistortion; s.transShadow = _TransShadow;
            #if defined(LIGHTMAP_ON)
                HS2BakedGI = SampleLightmap(i.lmuv, n);
                HS2ShadowMask = SAMPLE_SHADOWMASK(i.lmuv);
            #elif defined(SHADOWS_SHADOWMASK)
                HS2ShadowMask = unity_ProbesOcclusion;
            #endif
                return half4(HS2Shade(s, i.wpos, GetNormalizedScreenSpaceUV(i.pos)), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            ZWrite On
            HLSLPROGRAM
            #pragma vertex AseShadowVert
            #pragma fragment AseDepthFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            AseV AseShadowVert(AseA a)
            {
                AseV o = AseVert(a);
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
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            ZWrite On
            HLSLPROGRAM
            #pragma vertex AseVert
            #pragma fragment AseDepthFrag
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
            #define HS2_PASS_A AseA
            #define HS2_PASS_V AseV
            #define HS2_PASS_VERT AseVert
            #define HS2_PASS_CLIP AseDepthFrag
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
            #define HS2_PASS_A AseA
            #define HS2_PASS_V AseV
            #define HS2_PASS_VERT AseVert
            #define HS2_PASS_CLIP AseDepthFrag
            #define HS2_PASS_POS(a) (a).pos.xyz
            #define HS2_PASS_NORMAL(a) (a).n
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
    }
}
