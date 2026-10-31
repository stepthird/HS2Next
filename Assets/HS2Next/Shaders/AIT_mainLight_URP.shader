// URP replacement for HS2 "AIT/main Light" (an Amplify surface shader in the vanilla accessory bundles, e.g. material
// "Light" on the waist lamp's glow sphere pSphere_light, mask waist_lamp_light_mc; queue 4501 = Overlay+501,
// RenderType Overlay, no keywords). Same property names, types and defaults as the original. Behaviour inferred by
// black-box probing only (Assets/HS2Next/Editor/Fit/SkinFit.cs, Tests/FitCases/mainlight_*.txt, Spike/out/fit/AIT_main_Light_*.txt):
//  Colour           c = _Color (1 - max(M.r, M.g, M.b)) + M.r _Color2 + M.g _Color3 + M.b _Color4 with M = _ColorMask on
//                   uv0 (both _texcoord_ST and _ColorMask_ST applied, in that order); colour and mask alphas are unused.
//  View falloff     f = saturate(1 - 1.05 pow(1 - N.V, _Float0)) (a fresnel with bias 0, scale 1.05, power _Float0, inverted):
//                   1 at the centre of the sphere, 0 at the silhouette and on back faces; _Float0 0 draws nothing.
//                   N is the interpolated per-vertex world normal, not renormalized (on the real 219-vertex sphere this
//                   gives mean |d| 0.0000; a renormalized normal 0.0038), V the normalized per-pixel view direction.
//  Depth fade       d = saturate(|sceneEyeDepth - eyeDepth| / 1): fades within 1 unit of the opaque surface behind
//                   the glow (a soft-particle fade read from _CameraDepthTexture; measured with a depth-writing backdrop
//                   0.05-1.5 units behind the probe). Applied here only when the global _HS2NextSceneDepthValid is 1,
//                   because the project's URP renderer has no depth texture during the transparent queues (depth
//                   texture off, copy depth after transparents) and an unset _CameraDepthTexture is undefined.
//                   Without it d = 1, which is also what the Built-in reference renders show: they have no depth
//                   texture either and read Unity's grey default (raw 0.216, i.e. eye depth about 4.6 x the near
//                   plane), so the original only fades there within about 1 unit of that depth, next to the camera.
//  Output           rgb = c f d _Float1 (unlit: no dependence on lights, ambient, vertex colour), not clamped;
//                   blend One OneMinusSrcColor (screen: src + dst - src dst), ZTest LEqual.
//  Not measured     ZWrite (no effect on a single convex glow mesh; Off here), fog (no fog in the probes), the
//                   ForwardAdd pass (no light dependence in ForwardBase; extra lights add nothing here).
Shader "HS2Next/AIT main Light"
{
    Properties
    {
        _Float0 ("Float 0", Range(0,1)) = 1
        _Float1 ("Float 1", Float) = 2.71
        _Color ("Color", Color) = (1,0.588,0,0)
        _Color2 ("Color 2", Color) = (1,0.588,0,0)
        _Color3 ("Color 3", Color) = (1,0.588,0,0)
        _Color4 ("Color 4", Color) = (1,0.588,0,0)
        _ColorMask ("Color Mask", 2D) = "black" {}
        [HideInInspector] _texcoord ("", 2D) = "white" {}
        [HideInInspector] __dirty ("", Float) = 1
        [HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Overlay" "RenderPipeline"="UniversalPipeline" "Queue"="Overlay+501" "IgnoreProjector"="True" }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcColor
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "HS2NextFog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_ColorMask); SAMPLER(sampler_ColorMask);
            CBUFFER_START(UnityPerMaterial)
                float4 _ColorMask_ST, _texcoord_ST, _Color, _Color2, _Color3, _Color4;
                float _Float0, _Float1, __dirty;
                float _HS2FogModes;
            CBUFFER_END
            // Global, set to 1 by the host when _CameraDepthTexture holds this camera's opaque depth before the
            // transparent queues (URP: depth texture on, copy depth after opaques). Unset (0): no depth fade.
            float _HS2NextSceneDepthValid;
            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1; float3 n : TEXCOORD2; };
            V vert(A a)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
                o.pos = p.positionCS; o.wpos = p.positionWS;
                o.n = TransformObjectToWorldNormal(a.n);   // unit per vertex, interpolated
                o.uv = TRANSFORM_TEX(TRANSFORM_TEX(a.uv, _texcoord), _ColorMask);
                return o;
            }
            float4 frag(V i) : SV_Target
            {
                float3 m = SAMPLE_TEXTURE2D(_ColorMask, sampler_ColorMask, i.uv).rgb;
                float3 c = _Color.rgb * (1 - max(m.r, max(m.g, m.b))) + m.r * _Color2.rgb + m.g * _Color3.rgb + m.b * _Color4.rgb;
                float3 v = normalize(GetWorldSpaceViewDir(i.wpos));
                float nv = dot(i.n, v);
                float u = 1 - nv;
                float f = saturate(1 - 1.05 * (u > 0 ? pow(u, _Float0) : (_Float0 > 0 ? 0 : 1)));
                float d = 1;
                if (_HS2NextSceneDepthValid > 0)
                {
                    float sceneZ = LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(i.pos)), _ZBufferParams);
                    d = saturate(abs(sceneZ - LinearEyeDepth(i.pos.z, _ZBufferParams)));
                }
                return float4(HS2ApplyFogAdditive(c * (f * d * _Float1), i.wpos, _HS2FogModes), 1);
            }
            ENDHLSL
        }
    }
}
