// Draws nothing (M8): the target of shaders that cannot be drawn yet, e.g. KriptoFX's
// grab-pass distortion until Phase 3.3 provides an opaque texture (a URP/Lit fallback would draw them as opaque quads).
// Its only pass has a light mode no URP renderer draws; the material keeps the original's values for a later replacement.
Shader "HS2Next/Hidden/NoDraw"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "NoDraw"
            Tags { "LightMode"="HS2NextNoDraw" }
            ColorMask 0
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 Vert(float4 pos : POSITION) : SV_POSITION { return TransformObjectToHClip(pos.xyz); }
            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
