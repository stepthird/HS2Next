// Droplets and splashes: one dynamic mesh in world space, oriented in the vertex shader from the camera in use, so a mirror and every
// capture draw them right. Per vertex: POSITION the droplet's point in the world, NORMAL the axis it hangs along (world space; zero: the
// camera's up), TEXCOORD0.xy the offset in world units across the view and along the axis, COLOR its colour. Unlit.
Shader "HS2Next/Hidden/WetDrops"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float3 pos : POSITION; float3 axis : NORMAL; float2 off : TEXCOORD0; half4 color : COLOR; };
            struct V { float4 pos : SV_POSITION; half4 color : COLOR; };
            V Vert(A a)
            {
                V o;
                float3 w = TransformObjectToWorld(a.pos);
                float3 toCam = normalize(_WorldSpaceCameraPos - w);
                float3 axis = dot(a.axis, a.axis) > 1e-10 ? normalize(a.axis) : UNITY_MATRIX_V[1].xyz;
                float3 across = cross(axis, toCam);
                float l = dot(across, across);
                across = l > 1e-8 ? across * rsqrt(l) : UNITY_MATRIX_V[0].xyz;
                o.pos = TransformWorldToHClip(w + across * a.off.x + axis * a.off.y);
                o.color = a.color;
                return o;
            }
            half4 Frag(V i) : SV_Target { return i.color; }
            ENDHLSL
        }
    }
}
