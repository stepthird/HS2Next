// Gizmo handles and helpers of the Studio UI: unlit colour (passed linear by the pass),
// alpha blended, no depth write, ZTest from _ZTest (Always for handles), both faces. Drawn by HS2Next.UI.Gizmos.GizmoPass
// after post-processing for the view camera only.
Shader "HS2Next/UI/Gizmo"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 8
    }
    SubShader
    {
        Tags { "RenderType" = "Overlay" "Queue" = "Overlay" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Gizmo"
            Tags { "LightMode" = "UniversalForward" }   // a ScriptableRenderPipeline pass type: the player build lists its variant
            ZTest [_ZTest]
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                return o;
            }

            float4 frag(Varyings i) : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
