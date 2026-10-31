// Built-in measurement shader of SkyAmbientTest.MeasureFalloff (U10; Phase 3.3
// package S). Editor only (an Editor folder: never in a player). Under the Built-in pipeline it reads the textures and the
// light matrix Built-in uses for one point or spot light in its forward-add pass and writes them out unchanged (float
// target, no lighting model):
//   _Mode 0: rgb = the point light's falloff texture (_LightTexture0) at u = uv.x  (the index is (distance / range)^2)
//   _Mode 1: r = the spot falloff texture (_LightTextureB0) at u = uv.x, g = the spot cookie (_LightTexture0).w at uv
//   _Mode 2: rgba = unity_WorldToLight x (world position, 1) (the light coordinates of this pixel)
// The forward-base pass writes 0, the add pass adds (One One), so the target holds exactly the add pass's value.
Shader "Hidden/HS2NextTest/BuiltinAtten"
{
    Properties { _Mode ("Mode", Float) = 0 }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag() : SV_Target { return 0; }
            ENDCG
        }
        Pass
        {
            Tags { "LightMode"="ForwardAdd" }
            Blend One One
            ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdadd
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"
            float _Mode;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1; };
            v2f vert(float4 v : POSITION, float2 uv : TEXCOORD0)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v);
                o.uv = uv;
                o.wpos = mul(unity_ObjectToWorld, v).xyz;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float4 lc = mul(unity_WorldToLight, float4(i.wpos, 1));
                if (_Mode > 1.5) return lc;
            #if defined(POINT)
                return float4(tex2D(_LightTexture0, i.uv.xx).rrr, 1);
            #elif defined(SPOT)
                return float4(tex2D(_LightTextureB0, i.uv.xx).r, tex2D(_LightTexture0, i.uv).w, 0, 1);
            #else
                return float4(0, 0, 0, 1);
            #endif
            }
            ENDCG
        }
    }
}
