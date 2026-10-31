// URP replacement for HS2 "AIT/Eye Translucency" (eyeballs o_eyebase_L / o_eyebase_R, material c_m_eye_*,
// queue 2000, game keywords _COLOR4SWICH_ON _NODE_2929_ON _WRAPTEXBASE_ON). Same property names, types and
// defaults as the original. Behaviour inferred by black-box probing only (Assets/HS2Next/Editor/Fit/SkinFit.cs,
// Tests/FitCases/eye*.txt, Spike/out/fit/AIT_Eye_Translucency_*.txt). Layers, bottom to top:
//  Sclera     _Color * _MainTex (UV0).
//  Iris       _Texture2 on uv2 = (UV0 - 0.5) * _texture2uv.xy + 0.5 + _texture2uv.zw + e, where the base offset
//             e = (_eyebaseoffset.x, _eyebaseoffset.y * (2 * _texture2uv.y - 1)), plus parallax
//             -0.5 * _Parallax * V.xy / (V.z + 0.42) (V = tangent-space view direction, no height map) measured on
//             flat probes; disabled (HS2_EYE_PARK 0) because the original shows almost none on the real eyeballs.
//             Colour _Color2 * T2.r, weight T2.b * _Color2.a; emission colour * T2.r * weight * 12 * _Emission.
//  Pupil      _Texture3 on p0 = (UV0 - 0.5) * _texture2uv.xy + 0.5 + _texture2uv.zw + _eyebaseoffset.xy, scaled by
//             _texture3uv.xy about its own centre (0.5 + zw + offset), then by _texture3uv.z about 0.5; parallax 3x the
//             iris'. Colour _Color3, weight T3.a * _Color3.a, emission _Color3 * T3.r; replaces iris colour and emission.
//  Highlight  _Texture4 on rot(UV0 + _Texture4UV.zw - 0.5, _Texture4Rotator * pi) * _Texture4UV.xy + 0.5 (no parallax).
//             R: colour 3 * _Color4, weight T4.r * _Color4.a * _Smoothness (the game hides highlights with
//             _Smoothness = 0); G: specular, F0 = 0.3 * T4.g * _Color4.rgb, smoothness 0.92 * _Smoothness * T4.g *
//             (0.4 + 0.6 * _Color4.r), diffuse scaled by 1 - max(F0) (specular workflow). B unused.
//  Lighting   Built-in BRDF1 (Disney diffuse with roughness 1 - smoothness, no 0.04 dielectric F0 outside the
//             highlight), normal from _BumpMap (AG-packed) with xy scaled by 2.388 (Built-in UnpackScaleNormal form),
//             ambient occlusion saturate(_TextureSample0.r + 1 - _OcclusionStrength), Amplify translucency with colour
//             albedo * albedo (_Translucency, _TransNormalDistortion, _TransScattering, _TransDirect, _TransAmbient,
//             _TransShadow).
//  Iris lift  in direct light only, each non-zero iris colour channel moves towards 1 by T2.g * h(T2.r) * T(N.L * N.V)
//             (h, T fitted; this is the bright crescent of vanilla iris textures). The view dependence is approximate.
//  No measurable effect: _Shininess, _SpecularHighlights, _GlossyReflections.
// The game writes: _Color, _Texture2, _texture2uv (x, y, w), _Color2, _Emission, _Texture3, _Color3,
// _texture3uv (x, y), _Texture4, _Color4, _Texture4UV, _Texture4Rotator, _Smoothness.
Shader "HS2Next/AIT Eye Translucency"
{
    Properties
    {
        _Shininess ("Shininess", Range(0.01,1)) = 0.1
        [Header(Translucency)] _Translucency ("Strength", Range(0,50)) = 1
        _TransNormalDistortion ("Normal Distortion", Range(0,1)) = 0.1
        _TransScattering ("Scaterring Falloff", Range(1,50)) = 2
        _TransDirect ("Direct", Range(0,1)) = 1
        _TransAmbient ("Ambient", Range(0,1)) = 0.2
        _Color ("Color", Color) = (1,1,1,1)
        _TransShadow ("Shadow", Range(0,1)) = 0.9
        _Color2 ("Color 2", Color) = (0.706,0.706,0.706,1)
        _Color3 ("Color 3", Color) = (0.125,0.125,0.125,1)
        _Color4 ("Color 4", Color) = (0,0,0,0)
        _MainTex ("Main Tex", 2D) = "white" {}
        _Texture2 ("Texture2(eye)", 2D) = "black" {}
        _Texture3 ("Texture 3(eyeblack)", 2D) = "black" {}
        _Texture4 ("Texture 4(eyehi)", 2D) = "black" {}
        _BumpMap ("Bump Map", 2D) = "bump" {}
        _TextureSample0 ("Texture Sample 0", 2D) = "white" {}
        _Smoothness ("Smoothness", Range(0,1)) = 1
        _Emission ("Emission", Range(0,1)) = 0
        _Parallax ("Parallax", Range(-1,1)) = 0
        _OcclusionStrength ("OcclusionStrength", Range(0,1)) = 1
        _texture2uv ("texture 2 uv (xyscale/offset)", Vector) = (1,1,0,0)
        _texture3uv ("texture 3 uv (xyscale/totalscale)", Vector) = (0,0,0,0)
        _Texture4UV ("Texture 4 UV", Vector) = (1,1,0,0)
        _Texture4Rotator ("Texture 4 Rotator", Float) = 0.5
        _eyebaseoffset ("Eye base offset", Vector) = (0,0,0,0)
        [HideInInspector] _texcoord ("", 2D) = "white" {}
        [HideInInspector] __dirty ("", Float) = 1
        [Header(Forward Rendering Options)] [ToggleOff] _SpecularHighlights ("Specular Highlights", Float) = 1
        [ToggleOff] _GlossyReflections ("Reflections", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST, _Texture2_ST, _Texture3_ST, _Texture4_ST, _BumpMap_ST, _TextureSample0_ST;
            float4 _Color, _Color2, _Color3, _Color4, _texture2uv, _texture3uv, _Texture4UV, _eyebaseoffset;
            float _Shininess, _Translucency, _TransNormalDistortion, _TransScattering, _TransDirect, _TransAmbient, _TransShadow;
            float _Smoothness, _Emission, _Parallax, _OcclusionStrength, _Texture4Rotator, _SpecularHighlights, _GlossyReflections;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "HS2NextBRDF.hlsl"

            #define HS2_EYE_BUMP      2.388  // tangent normal xy scale (fitted)
            #define HS2_EYE_EMISSION  12.0   // iris emission = colour * T2.r * weight * 12 * _Emission
            #define HS2_EYE_HL_ALBEDO 3.0    // highlight R colour = 3 * _Color4
            #define HS2_EYE_HL_F0     0.3
            #define HS2_EYE_HL_SMOOTH 0.92
            #define HS2_EYE_PUPIL_PAR 3.0    // pupil parallax relative to the iris
            // Parallax weight. The tangent-space offset below reproduces the original exactly on flat probes (any tilt,
            // tangent orientation, camera distance), but on the real eyeball mesh the original shifts the iris by only
            // about 1/14 of it (cause not found); 0 matches the real eyes best (SkinFit.Real, composite eye view).
            #define HS2_EYE_PARK 0.0

            TEXTURE2D(_MainTex);        SAMPLER(sampler_MainTex);
            TEXTURE2D(_Texture2);       SAMPLER(sampler_Texture2);
            TEXTURE2D(_Texture3);       SAMPLER(sampler_Texture3);
            TEXTURE2D(_Texture4);       SAMPLER(sampler_Texture4);
            TEXTURE2D(_BumpMap);        SAMPLER(sampler_BumpMap);
            TEXTURE2D(_TextureSample0); SAMPLER(sampler_TextureSample0);

            struct A { float4 pos : POSITION; float3 n : NORMAL; float4 t : TANGENT; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1; float3 n : TEXCOORD2; float4 t : TEXCOORD3; };

            V vert(A a)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
                VertexNormalInputs nn = GetVertexNormalInputs(a.n, a.t);
                o.pos = p.positionCS; o.wpos = p.positionWS;
                o.n = nn.normalWS; o.t = float4(nn.tangentWS, a.t.w * GetOddNegativeScale());
                o.uv = a.uv;
                return o;
            }

            // Iris lift (measured): in direct light the iris colour moves towards white (per channel, only where the
            // channel is non-zero) by t = T2.g * h(T2.r) * T(N.L); h(r) = saturate(exp(10.6 (r - 0.905))),
            // T = saturate(cubic fit in N.L * N.V; frontal probes give the N.L curve, tilted ones fit N.L * N.V within
            // about 0.04 of t). Ambient, translucency and emission use the plain colour.
            struct EyeLayers { half3 sclera, iris; half w2, w3, w4, liftG, liftR; half3 pupil, hl; };

            half3 EyeDirectAlbedo(EyeLayers e, half nl)
            {
                half T = saturate(((3.358 * nl - 0.674) * nl + 0.415) * nl - 0.023);
                half t = e.liftG * saturate(exp(10.6 * (e.liftR - 0.905))) * T;
                half3 iris = lerp(e.iris, step(1e-5, e.iris), t);
                return lerp(lerp(lerp(e.sclera, iris, e.w2), e.pupil, e.w3), e.hl, e.w4);
            }

            // HS2Shade with a per-light albedo for the iris lift.
            half3 EyeShade(HS2Surface s, EyeLayers e, float3 positionWS, float2 screenUV)
            {
                float3 v = GetWorldSpaceNormalizeViewDir(positionWS);
                float3 n = s.normalWS;
                half pr = 1 - s.smoothness;
                half a = max(pr * pr, 0.002);
                half3 giDiffuse = SampleSH(n) * s.occlusion;
                half3 env = GlossyEnvironmentReflection(reflect(-v, n), positionWS, pr, s.occlusion, screenUV);
                half nv = abs(dot(n, v));
                half surfaceReduction = 1.0 / (a * a + 1.0);
                half grazing = saturate(s.smoothness + (1 - s.oneMinusReflectivity));
                half3 c = s.albedo * s.oneMinusReflectivity * giDiffuse + surfaceReduction * env * lerp(s.specColor, grazing.xxx, pow(1 - nv, 5));
                HS2Surface d = s;
                Light main = GetMainLight(TransformWorldToShadowCoord(positionWS));
                main.color *= HS2LightLayerWeight(main.layerMask);
                d.albedo = EyeDirectAlbedo(e, saturate(dot(n, main.direction)) * nv);
                c += HS2DirectLight(d, v, main.direction, main.color * main.distanceAttenuation * main.shadowAttenuation);
                if (s.transStrength > 0) c += HS2Translucency(s, v, main.direction, main.color * main.distanceAttenuation * lerp(1, main.shadowAttenuation, s.transShadow), giDiffuse);
            #if defined(_ADDITIONAL_LIGHTS)
                HS2LightIter it = HS2LightIterInit(positionWS, screenUV);
                uint lightIndex;
                [loop] while (HS2LightNext(it, lightIndex))
                {
                    Light L = GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1));
                    if (!HS2LightReaches(L, lightIndex)) continue;
                    d.albedo = EyeDirectAlbedo(e, saturate(dot(n, L.direction)) * nv);
                    c += HS2DirectLight(d, v, L.direction, L.color * L.distanceAttenuation * L.shadowAttenuation);
                    if (s.transStrength > 0) c += HS2Translucency(s, v, L.direction, L.color * L.distanceAttenuation * lerp(1, L.shadowAttenuation, s.transShadow), giDiffuse);
                }
            #endif
                return c + s.emission;
            }

            half3 UnpackEyeNormal(half4 p)
            {
                p.x *= p.w;
                half2 xy = (p.xy * 2 - 1) * HS2_EYE_BUMP;
                return half3(xy, sqrt(1 - saturate(dot(xy, xy))));
            }

            half4 frag(V i) : SV_Target
            {
                float3 n0 = normalize(i.n);
                float3 t = normalize(i.t.xyz);
                float3 b = cross(n0, t) * i.t.w;
                float3 vWS = GetWorldSpaceNormalizeViewDir(i.wpos);
                float3 vTS = float3(dot(vWS, t), dot(vWS, b), dot(vWS, n0));
                float2 par = -HS2_EYE_PARK * 0.5 * _Parallax * vTS.xy / (vTS.z + 0.42);   // Built-in ParallaxOffset with h = 0

                float2 uv = i.uv;
                float2 baseUV = (uv - 0.5) * _texture2uv.xy + 0.5 + _texture2uv.zw;
                float2 uv2 = baseUV + float2(_eyebaseoffset.x, _eyebaseoffset.y * (2 * _texture2uv.y - 1)) + par;
                float2 c3 = 0.5 + _texture2uv.zw + _eyebaseoffset.xy;
                float2 p0 = baseUV + _eyebaseoffset.xy;
                float2 uv3 = (((p0 - c3) * _texture3uv.xy + c3) - 0.5) * _texture3uv.z + 0.5 + HS2_EYE_PUPIL_PAR * par;
                float sn, cs; sincos(_Texture4Rotator * PI, sn, cs);
                float2 q = uv + _Texture4UV.zw - 0.5;
                float2 uv4 = float2(cs * q.x + sn * q.y, -sn * q.x + cs * q.y) * _Texture4UV.xy + 0.5;

                half4 t1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(uv, _MainTex));
                half4 t2 = SAMPLE_TEXTURE2D(_Texture2, sampler_Texture2, uv2);
                half4 t3 = SAMPLE_TEXTURE2D(_Texture3, sampler_Texture3, uv3);
                half4 t4 = SAMPLE_TEXTURE2D(_Texture4, sampler_Texture4, uv4);
                half4 oc = SAMPLE_TEXTURE2D(_TextureSample0, sampler_TextureSample0, uv);

                // Albedo layers and emission.
                EyeLayers e;
                e.sclera = _Color.rgb * t1.rgb;
                e.w2 = t2.b * _Color2.a;
                e.iris = _Color2.rgb * t2.r;
                e.w3 = t3.a * _Color3.a;
                e.pupil = _Color3.rgb;
                e.w4 = t4.r * _Color4.a * _Smoothness;
                e.hl = HS2_EYE_HL_ALBEDO * _Color4.rgb;
                e.liftG = t2.g; e.liftR = t2.r;
                half3 albedo = lerp(lerp(lerp(e.sclera, e.iris, e.w2), e.pupil, e.w3), e.hl, e.w4);
                half3 emission = lerp(e.iris * t2.r * e.w2 * HS2_EYE_EMISSION * _Emission, _Color3.rgb * t3.r, e.w3);

                // Highlight specular (G) in the specular workflow.
                half3 f0 = HS2_EYE_HL_F0 * t4.g * _Color4.rgb;
                half smooth = HS2_EYE_HL_SMOOTH * _Smoothness * t4.g * (0.4 + 0.6 * _Color4.r);

                float3 nWS = normalize(mul(UnpackEyeNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, TRANSFORM_TEX(uv, _BumpMap))), float3x3(t, b, n0)));
                HS2Surface s = HS2SurfaceMetallic(albedo, 0, smooth, saturate(oc.r + 1 - _OcclusionStrength), nWS);
                s.specColor = f0;
                s.oneMinusReflectivity = 1 - max(f0.r, max(f0.g, f0.b));
                s.emission = emission;
                s.transStrength = _Translucency; s.transDirect = _TransDirect; s.transAmbient = _TransAmbient;
                s.transScattering = _TransScattering; s.transDistortion = _TransNormalDistortion;
                s.transColor = albedo; s.transShadow = _TransShadow;
                return half4(EyeShade(s, e, i.wpos, GetNormalizedScreenSpaceUV(i.pos)), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            float3 _LightDirection;
            struct A { float4 pos : POSITION; float3 n : NORMAL; };
            float4 vert(A a) : SV_POSITION
            {
                float3 w = TransformObjectToWorld(a.pos.xyz);
                float4 p = TransformWorldToHClip(ApplyShadowBias(w, TransformObjectToWorldNormal(a.n), _LightDirection));
            #if UNITY_REVERSED_Z
                p.z = min(p.z, p.w * UNITY_NEAR_CLIP_VALUE);
            #else
                p.z = max(p.z, p.w * UNITY_NEAR_CLIP_VALUE);
            #endif
                return p;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 vert(float4 pos : POSITION) : SV_POSITION { return TransformObjectToHClip(pos.xyz); }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode"="MotionVectors" }
            ColorMask RG
            HLSLPROGRAM
            #pragma vertex HS2MotionVert
            #pragma fragment HS2MotionFrag
            float4 vert(float4 pos : POSITION) : SV_POSITION { return TransformObjectToHClip(pos.xyz); }
            half4 frag() : SV_Target { return 0; }
            #define HS2_PASS_SIMPLE
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ColorMask RGBA
            HLSLPROGRAM
            #pragma vertex HS2DepthNormalsVert
            #pragma fragment HS2DepthNormalsFrag
            float4 vert(float4 pos : POSITION) : SV_POSITION { return TransformObjectToHClip(pos.xyz); }
            half4 frag() : SV_Target { return 0; }
            #define HS2_PASS_SIMPLE
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
    }
}
