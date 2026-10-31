// Draws nothing in either pipeline (vertices are placed outside the clip volume). Used by the
// skin fit to hide material slots that are not under test (e.g. the body's liquid overlay).
Shader "Hidden/HS2Next/Invisible"
{
    SubShader
    {
        Pass
        {
            ColorMask 0 ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 pos : POSITION) : SV_POSITION { return float4(2, 2, 2, 1); }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
