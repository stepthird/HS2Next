// URP stand-in for "Custom/Silhouette" (the train crowds of vanilla maps 35 / 36; bundle property list and pass state:
// one property _Color; pass 1 depth only (ZWrite On, colour mask 0), pass 2 SrcAlpha / OneMinusSrcAlpha, ZWrite Off,
// queue Transparent). Colour model black-box probed against the bundle copy (Shaders/README.md "Map shaders"): unlit
// _Color with alpha _Color.a. The depth pass runs first (SRPDefaultUnlit is drawn before UniversalForward per object), so
// overlapping parts of one figure blend once.
Shader "HS2Next/Custom/Silhouette"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        [HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "HS2NextFog.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float _HS2FogModes;
        CBUFFER_END
        float4 Vert(float4 pos : POSITION) : SV_POSITION { return TransformObjectToHClip(pos.xyz); }
        half4 FragDepth() : SV_Target { return 0; }
        half4 FragColor(float4 pos : SV_POSITION) : SV_Target { return half4(HS2ApplyFogCS(_Color.rgb, pos, _HS2FogModes), _Color.a); }
        ENDHLSL
        Pass
        {
            Name "DepthFirst"
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepth
            ENDHLSL
        }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragColor
            ENDHLSL
        }
    }
}
