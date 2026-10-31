// URP stand-in for "KriptoFX/ME/Particle" (vanilla map effects: fires, sparks, smoke; bundle property list and pass state:
// Blend [SrcMode] [DstMode], Cull [CullMode], ZWrite Off, queue Transparent). The material's blend factors carry its blend
// keyword (BlendAdd: SrcAlpha One, BlendAlpha: SrcAlpha OneMinusSrcAlpha), so one shader serves both (
// M8). Colour black-box probed against the bundle copy (Shaders/README.md "Map shaders"); Clip_ON clips at
// _Cutout; FrameBlend_ON (flip-book blending), SoftParticles_ON (needs the scene depth), FresnelFade and VertLight are not
// reproduced (approximation).
Shader "HS2Next/Compatible/KriptoFX/ME/Particle"
{
    Properties
    {
        [HDR] _TintColor ("Tint Color", Color) = (0.5,0.5,0.5,1)
        _MainTex ("Particle Texture", 2D) = "white" {}
        [HideInInspector] _Cutout ("_Cutout", Float) = 0.2
        [HideInInspector] _InvFade ("Soft Particles Factor", Float) = 1
        [HideInInspector] _FresnelStr ("Fresnel Strength", Float) = 1
        [HideInInspector] SrcMode ("SrcMode", Float) = 5
        [HideInInspector] DstMode ("DstMode", Float) = 10
        [HideInInspector] CullMode ("Cull Mode", Float) = 2
        [HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            Blend [SrcMode] [DstMode]
            Cull [CullMode]
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma dynamic_branch_local Clip_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "HS2NextFog.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _TintColor;
                float _Cutout, _InvFade, _FresnelStr, SrcMode, DstMode, CullMode;
                float _HS2FogModes;
            CBUFFER_END
            struct A { float4 pos : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            V Vert(A a) { V o; o.pos = TransformObjectToHClip(a.pos.xyz); o.color = a.color; o.uv = TRANSFORM_TEX(a.uv, _MainTex); return o; }
            half4 Frag(V i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half4 col = 2.0h * i.color * _TintColor * tex;
                if (Clip_ON) clip(col.a - _Cutout);
                col.a = saturate(col.a);
                col.rgb = HS2ApplyFogEitherCS(col.rgb, i.pos, _HS2FogModes, DstMode == 1.0);   // additive materials: toward black
                return col;
            }
            ENDHLSL
        }
    }
}
