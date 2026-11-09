// The map's surface as one pixel (MapSurface.cs): every renderer under a point is drawn into that pixel through a thin view along the question, the nearest fragment
// wins by depth. Red is the world position measured along _MapSurfaceAxis from _MapSurfaceFrom, green to alpha the surface's unit normal, turned towards the view. The
// normal is the triangle's, from the position's derivatives, so a mesh with no normals or with smoothed ones answers as its triangles do. Alpha and transparency of a
// map's own material do not matter here: every surface counts.
Shader "HS2Next/Hidden/MapSurface"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Surface"
            ZTest LEqual ZWrite On Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _MapSurfaceFrom;
            float4 _MapSurfaceAxis;
            float4 _MapSurfaceView;     // xyz the view's direction, w the least |normal.y| that counts
            struct A { float3 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; float3 w : TEXCOORD0; };
            V Vert(A a)
            {
                V o;
                o.w = TransformObjectToWorld(a.pos);
                o.pos = TransformWorldToHClip(o.w);
                return o;
            }
            float4 Frag(V i) : SV_Target
            {
                float3 n = cross(ddx(i.w), ddy(i.w));
                float len = length(n);
                if (len <= 0) discard;
                n /= len;
                if (abs(n.y) < _MapSurfaceView.w) discard;
                if (dot(n, _MapSurfaceView.xyz) > 0) n = -n;
                return float4(dot(i.w - _MapSurfaceFrom.xyz, _MapSurfaceAxis.xyz), n);
            }
            ENDHLSL
        }
    }
}
