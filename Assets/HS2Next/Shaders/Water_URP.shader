// Approximate URP replacement for every water shader of HS2 maps (M11): Lux Water,
// NatureManufacture river / swamp, AQUAS, Unity's FX/Water, ASE / Ciconia / misc single-map water. Not a fit: the originals
// refract the scene behind the surface, fade by depth, tessellate and displace. This shader draws a transparent surface:
// tint x (SH ambient + diffuse lights) under a
// GGX highlight per light, two normal-map layers on world XZ scrolled in opposite directions, the reflection probe
// (box-projected, blended) weighted by Fresnel, alpha lerp(_HS2WaterAlpha, 1, Fresnel). Its inputs (tint, normal map,
// tiling, speed, smoothness, normal strength) are filled at map load by MapShaderHooks.WaterInputs from the original
// material's own properties (a name table per family); a swapped copy whose inputs were not filled draws the defaults.
// The original's render queue is kept (ShaderMap.Swap); ZWrite stays off. USINGWATERVOLUME (HS2's sea step) is ignored.
// Depth and refraction (Phase 3.3, package C; approximate): while the globals _HS2NextSceneDepthValid and
// _HS2NextSceneOpaqueValid are 1 (SceneTextures: the app's asset has the depth copy after the opaques and the opaque
// texture), the water body is composited from URP's opaque texture instead of blending with the frame buffer: the bottom
// is sampled at the screen position shifted by the surface normal x _HS2WaterRefraction (scaled down in the first metre of
// water; a shifted sample in front of the surface falls back to the unshifted one) and seen through the surface with the
// same opacity as the blended path (lerp(_HS2WaterAlpha, 1, Fresnel): a depth-dependent absorption made map 32's clear pool
// opaque at grazing views, where HS2 shows the tiles); the surface fades out where the water is shallower than
// _HS2WaterEdgeFade metres along the view ray (shorelines). Transparents behind the water are not in the opaque
// texture. Without the textures (the measurement asset of the fit tools) the shader draws as before.
Shader "HS2Next/Compatible/Water"
{
    Properties
    {
        _HS2WaterTint ("Tint", Color) = (0.25,0.42,0.5,1)
        _HS2WaterAlpha ("Opacity at normal incidence", Range(0, 1)) = 0.55
        _HS2WaterNormal ("Normal map", 2D) = "bump" {}
        _HS2WaterNormalScale ("Normal strength", Range(0, 2)) = 1
        _HS2WaterTiling ("Tiling per metre", Float) = 0.1
        _HS2WaterSpeed ("Speed (xy layer 1, zw layer 2; per second)", Vector) = (0.02,0.01,-0.015,0.02)
        _HS2WaterSmoothness ("Smoothness", Range(0, 1)) = 0.9
        _HS2WaterF0 ("Fresnel F0", Range(0, 1)) = 0.02
        _HS2WaterRefraction ("Refraction (screen uv per unit of normal tilt)", Range(0, 0.2)) = 0.03
        _HS2WaterEdgeFade ("Edge fade depth (m)", Range(0.001, 5)) = 0.2
        [HideInInspector] _HS2FogModes ("Fog Modes", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent-1" "RenderType"="Transparent" "IgnoreProjector"="True" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #include "HS2NextLights.hlsl"
            #include "HS2NextFog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            TEXTURE2D(_HS2WaterNormal); SAMPLER(sampler_HS2WaterNormal);
            CBUFFER_START(UnityPerMaterial)
                float4 _HS2WaterNormal_ST, _HS2WaterSpeed;
                half4 _HS2WaterTint;
                float _HS2WaterAlpha, _HS2WaterNormalScale, _HS2WaterTiling, _HS2WaterSmoothness, _HS2WaterF0;
                float _HS2WaterRefraction, _HS2WaterEdgeFade;
                float _HS2FogModes;
            CBUFFER_END
            // Globals (SceneTextures): 1 when the scene depth / opaque colour before the transparents exist for this camera.
            float _HS2NextSceneDepthValid, _HS2NextSceneOpaqueValid;
            struct A { float4 pos : POSITION; float3 n : NORMAL; };
            struct V { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; float3 n : TEXCOORD1; };
            V Vert(A a)
            {
                V o;
                VertexPositionInputs p = GetVertexPositionInputs(a.pos.xyz);
                o.pos = p.positionCS; o.wpos = p.positionWS; o.n = TransformObjectToWorldNormal(a.n);
                return o;
            }
            half3 Layer(float2 uv)
            {
                half4 packed = SAMPLE_TEXTURE2D(_HS2WaterNormal, sampler_HS2WaterNormal, uv);
                packed.x *= packed.w;   // DXT5nm (AG) or RG / BC5
                half3 t; t.xy = (packed.xy * 2 - 1) * _HS2WaterNormalScale; t.z = sqrt(1 - saturate(dot(t.xy, t.xy)));
                return t;
            }
            // GGX specular term (D x Vis x F0-weighted Fresnel folded in by the caller), Built-in BRDF1 shape
            half Specular(float3 n, float3 v, float3 l, half roughness)
            {
                float3 h = SafeNormalize(l + v);
                float nh = saturate(dot(n, h)), nl = saturate(dot(n, l)), nv = saturate(dot(n, v));
                float a2 = roughness * roughness;
                float d = nh * nh * (a2 - 1) + 1;
                float D = a2 / (PI * d * d + 1e-7);
                float k = roughness * 0.5;
                float vis = 0.25 / ((nl * (1 - k) + k) * (nv * (1 - k) + k) + 1e-5);
                return D * vis * nl * PI;
            }
            half4 Frag(V i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float t = _Time.y;
                float2 wuv = i.wpos.xz * _HS2WaterTiling;
                half3 a = Layer(wuv + _HS2WaterSpeed.xy * t), b = Layer(wuv * 1.37 + _HS2WaterSpeed.zw * t);
                half3 nt = normalize(half3(a.xy + b.xy, a.z * b.z));
                float3 ng = normalize(i.n) * (IS_FRONT_VFACE(face, true, false) ? 1 : -1);
                // tangent frame of the world XZ mapping on the (mostly horizontal) surface
                float3 tx = normalize(float3(1, 0, 0) - ng * ng.x), tz = cross(tx, ng);
                float3 n = normalize(tx * nt.x - tz * nt.y + ng * nt.z);
                float3 v = GetWorldSpaceNormalizeViewDir(i.wpos);
                half nv = saturate(dot(n, v));
                half fres = _HS2WaterF0 + (1 - _HS2WaterF0) * pow(1 - nv, 5);
                half perceptual = 1 - _HS2WaterSmoothness, rough = max(perceptual * perceptual, 0.002);
                half3 diffuse = _HS2WaterTint.rgb * SampleSH(n);
                half3 spec = 0;
                Light main = GetMainLight(TransformWorldToShadowCoord(i.wpos));
                main.color *= HS2LightLayerWeight(main.layerMask);
                half3 lc = main.color * main.distanceAttenuation * main.shadowAttenuation;
                diffuse += _HS2WaterTint.rgb * lc * saturate(dot(n, main.direction));
                spec += lc * Specular(n, v, main.direction, rough);
#if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = i.wpos;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.pos);
                HS2LightIter it = HS2LightIterInit(i.wpos, inputData.normalizedScreenSpaceUV);
                uint lightIndex;
                [loop] while (HS2LightNext(it, lightIndex))
                {
                    Light L = GetAdditionalLight(lightIndex, i.wpos, half4(1, 1, 1, 1));
                    if (!HS2LightReaches(L, lightIndex)) continue;
                    half3 c = L.color * L.distanceAttenuation * L.shadowAttenuation;
                    diffuse += _HS2WaterTint.rgb * c * saturate(dot(n, L.direction));
                    spec += c * Specular(n, v, L.direction, rough);
                }
#endif
                half3 env = GlossyEnvironmentReflection(reflect(-v, n), i.wpos, perceptual, 1, GetNormalizedScreenSpaceUV(i.pos));
                if (_HS2NextSceneDepthValid > 0 && _HS2NextSceneOpaqueValid > 0)
                {
                    float2 uv = GetNormalizedScreenSpaceUV(i.pos);
                    float surf = LinearEyeDepth(i.pos.z, _ZBufferParams);
                    float thick = max(LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams) - surf, 0);
                    float2 ruv = uv + nt.xy * _HS2WaterRefraction * saturate(thick);
                    float thickR = LinearEyeDepth(SampleSceneDepth(ruv), _ZBufferParams) - surf;
                    if (thickR < 0) ruv = uv;
                    half a = saturate(lerp(_HS2WaterAlpha, 1, fres) + dot(spec, 0.333) * fres);
                    half3 surface = diffuse * (1 - fres) + (env + spec) * fres;
                    return half4(HS2ApplyFog(surface + SampleSceneColor(ruv) * (1 - a), i.wpos, _HS2FogModes), saturate(thick / _HS2WaterEdgeFade));
                }
                half3 col = diffuse * (1 - fres) + (env + spec) * fres;
                half alpha = saturate(lerp(_HS2WaterAlpha, 1, fres) + dot(spec, 0.333) * fres);
                return half4(HS2ApplyFog(col / max(alpha, 0.01), i.wpos, _HS2FogModes), alpha);
            }
            ENDHLSL
        }
    }
}
