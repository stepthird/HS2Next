// A puddle: an unlit disc with the mirror image of its plane laid over it (PlanarMirrors.cs), no light, no glint. Per renderer, through
// a property block: the colour, the rim, the shine, the mirror's strength and the plane's height. The mesh's uv0.x runs from 0 at the
// centre to 1 at the outline; the rim fades over _WetPuddleEdge of the radius.
// The image: at the screen position, alpha strength * shine * lerp(.3, 1, (1 - cos view)^2), tinted halfway to the puddle's hue
// (max-normalised). A puddle whose plane has no image for this camera shows its disc.
Shader "HS2Next/Hidden/WetPuddle"
{
    Properties
    {
        [HideInInspector] _WetPuddleColor ("Colour", Color) = (0.1, 0.097, 0.097, 0.8)
        [HideInInspector] _WetPuddleEdge ("Rim", Float) = 0.04
        [HideInInspector] _WetPuddleShine ("Shine", Float) = 1
        [HideInInspector] _WetMirrorStrength ("Mirror strength", Float) = 0.85
        [HideInInspector] _WetPlaneY ("Plane height", Float) = 0
    }
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
            TEXTURE2D(_HS2WetMirror0); SAMPLER(sampler_HS2WetMirror0);
            TEXTURE2D(_HS2WetMirror1); SAMPLER(sampler_HS2WetMirror1);
            float4 _HS2WetMirrorPlanes;   // x, y: the planes' heights; z, w: 1 when the plane has an image for this camera
            CBUFFER_START(UnityPerMaterial)
                float4 _WetPuddleColor;
                float _WetPuddleEdge, _WetPuddleShine, _WetMirrorStrength, _WetPlaneY;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float4 sp : TEXCOORD0; float3 wpos : TEXCOORD1; float t : TEXCOORD2; };
            V Vert(A a)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
                o.pos = p.positionCS;
                o.sp = ComputeScreenPos(p.positionCS);
                o.wpos = p.positionWS;
                o.t = a.uv.x;
                return o;
            }
            half4 Frag(V i) : SV_Target
            {
                float rim = saturate((1 - i.t) / max(_WetPuddleEdge, 1e-4));
                float core = _WetPuddleColor.a * rim;
                float2 uv = i.sp.xy / i.sp.w;
                float3 image = 0;
                float have = 0;
                if (_HS2WetMirrorPlanes.z > 0.5 && abs(_WetPlaneY - _HS2WetMirrorPlanes.x) < 0.0005)
                {
                    image = SAMPLE_TEXTURE2D(_HS2WetMirror0, sampler_HS2WetMirror0, uv).rgb;
                    have = 1;
                }
                else if (_HS2WetMirrorPlanes.w > 0.5 && abs(_WetPlaneY - _HS2WetMirrorPlanes.y) < 0.0005)
                {
                    image = SAMPLE_TEXTURE2D(_HS2WetMirror1, sampler_HS2WetMirror1, uv).rgb;
                    have = 1;
                }
                float cosView = saturate(normalize(_WorldSpaceCameraPos - i.wpos).y);
                float k = 1 - cosView;
                float m = saturate(have * _WetMirrorStrength * _WetPuddleShine * lerp(0.3, 1, k * k)) * rim;
                float3 hue = _WetPuddleColor.rgb / max(max(_WetPuddleColor.r, max(_WetPuddleColor.g, _WetPuddleColor.b)), 1e-4);
                image *= lerp(1, hue, 0.5);
                // the image over the disc
                float outA = m + core * (1 - m);
                float3 rgb = (image * m + _WetPuddleColor.rgb * core * (1 - m)) / max(outA, 1e-4);
                return half4(rgb, outA);
            }
            ENDHLSL
        }
    }
}
