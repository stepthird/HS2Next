// 2D overlay compositing for skin and clothes textures (written from the
// formulas there): _MainTex = a copy of the target, _Overlay = the overlay image; the pass is blended onto the
// target itself with [_SrcBlend] [_DstBlend] (Default mode: SrcAlpha / OneMinusSrcAlpha, LinearAlpha: One / Zero).
// Loaded with Resources.Load by Runtime/Chara/Ext/OverlayCompositor.cs; works under Built-in and URP (Graphics.Blit).
Shader "Hidden/HS2Next/OverlayComposite"
{
    Properties
    {
        _MainTex ("Target copy", 2D) = "black" {}
        _Overlay ("Overlay", 2D) = "black" {}
        _Override ("Override", Float) = 0
        _LinearAlpha ("Linear alpha", Float) = 0
        _SrcBlend ("Src blend", Float) = 5
        _DstBlend ("Dst blend", Float) = 10
    }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            Blend [_SrcBlend] [_DstBlend]
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _Overlay;
            float _Override, _LinearAlpha;

            float4 frag (v2f_img i) : SV_Target
            {
                float4 o = tex2D(_Overlay, i.uv);
                float4 m = tex2D(_MainTex, i.uv);
                // Porter-Duff "o over m", straight alpha.
                float a = o.a + m.a * (1 - o.a);
                float3 pd = a > 0 ? (o.rgb * o.a + m.rgb * m.a * (1 - o.a)) / a : float3(0, 0, 0);
                // Linear result.
                float3 lin = lerp(m.rgb, o.rgb, max(o.a, _Override));
                float la = _Override > 0.5 ? o.a : a;
                return _LinearAlpha > 0.5 ? float4(lin, la) : float4(pd, a);
            }
            ENDCG
        }
    }
}
