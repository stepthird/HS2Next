// HS2 Studio's ambient obscurance (PPv2's multi-scale volumetric obscurance, "MSVO", ambientOnly off) re-made for URP
// (E8's alternative; Phase 3.3 package B, fix round). Drawn by HS2AoFeature after the
// opaques. Written from the behaviour HS2 shows (package H's ao_* captures), not from PPv2's code:
//   - screen-space, from depth only (no normals): obscurance comes from depth differences against the neighbourhood at
//     several screen scales, so it reaches far (a third of H's stage darkens) and leaves convex shapes and planes alone;
//   - per scale s (radius R·2^s half-res texels) and per direction pair (+d, −d): the two samples' heights over the pixel
//     h = z − z_sample (eye depth, positive when the sample is nearer), each clamped to ±thickness × the scale's world
//     size (a thicker occluder darkens more and further: HS2's thickness modifier); the pair occludes by
//     saturate((h1 + h2) / 2 / scale size): a plane cancels (h1 = −h2), a crease or a nearby object occludes;
//   - the scales are averaged with weights, obscurance o = mean, visibility v = (1 − o)^intensity;
//   - a depth-aware blur, then the camera colour is multiplied by v (the whole opaque image, as HS2's ambientOnly false).
// Passes: 0 obscurance (half res, R16F), 1 blur (half res, depth-aware 9-tap cross), 2 composite (Blend Zero SrcColor onto
// the camera colour). Parameters: _HS2AoParams (radius in half-res texels at the reference height, intensity, thickness,
// world size per pixel at eye depth 1 for the half-res frame), _HS2AoTexel (1 / half width, 1 / half height, half w, half h).
Shader "HS2Next/Hidden/HS2Ao"
{
    Properties { }
    HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        TEXTURE2D(_HS2AoTex);
        float4 _HS2AoParams;
        float4 _HS2AoTexel;
        float4 _HS2AoScales;   // weights of the four scales

        #define AO_DIRS 6
        static const float2 kDir[AO_DIRS] =
        {
            float2(1.0000, 0.0000), float2(0.5000, 0.8660), float2(-0.5000, 0.8660),
            float2(0.8660, 0.5000), float2(0.0000, 1.0000), float2(-0.8660, 0.5000)
        };

        float EyeDepth(float2 uv)
        {
            float raw = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                if (raw <= 0.0) return 1e6;
            #else
                if (raw >= 1.0) return 1e6;
            #endif
            return LinearEyeDepth(raw, _ZBufferParams);
        }

        float FragAo(Varyings i) : SV_Target
        {
            float2 uv = i.texcoord;
            float z = EyeDepth(uv);
            if (z > 1e5) return 0;
            float R = _HS2AoParams.x, thick = _HS2AoParams.z, px = _HS2AoParams.w;
            // a per-pixel rotation of the direction set (interleaved gradient noise of the pixel position: deterministic)
            float2 pos = uv * _HS2AoTexel.zw;
            float ign = frac(52.9829189 * frac(dot(pos, float2(0.06711056, 0.00583715))));
            float a = ign * 3.14159265 / AO_DIRS;
            float2x2 rot = float2x2(cos(a), -sin(a), sin(a), cos(a));
            float occ = 0, wsum = 0;
            [unroll] for (int s = 0; s < 4; s++)
            {
                float rs = R * exp2((float)s);
                float size = rs * px * z;                 // world size of the scale's radius at this depth
                float lim = thick * size;
                float o = 0;
                [unroll] for (int k = 0; k < AO_DIRS; k++)
                {
                    float2 d = mul(rot, kDir[k]) * rs * _HS2AoTexel.xy;
                    float h1 = clamp(z - EyeDepth(uv + d), -lim, lim);
                    float h2 = clamp(z - EyeDepth(uv - d), -lim, lim);
                    o += saturate(0.5 * (h1 + h2) / size);
                }
                float w = _HS2AoScales[s];
                occ += w * o / AO_DIRS;
                wsum += w;
            }
            return occ / max(wsum, 1e-4);
        }

        float FragBlur(Varyings i) : SV_Target
        {
            float2 uv = i.texcoord;
            float z = EyeDepth(uv);
            float sum = SAMPLE_TEXTURE2D_LOD(_HS2AoTex, sampler_LinearClamp, uv, 0).r, wsum = 1;
            [unroll] for (int k = 1; k <= 4; k++)
            {
                [unroll] for (int a = 0; a < 4; a++)
                {
                    float2 dir = a == 0 ? float2(1, 0) : a == 1 ? float2(-1, 0) : a == 2 ? float2(0, 1) : float2(0, -1);
                    float2 suv = uv + dir * k * _HS2AoTexel.xy;
                    float zs = EyeDepth(suv);
                    float w = exp(-abs(zs - z) / max(0.05 * z, 1e-3)) * (1.0 - k / 5.0);
                    sum += w * SAMPLE_TEXTURE2D_LOD(_HS2AoTex, sampler_LinearClamp, suv, 0).r;
                    wsum += w;
                }
            }
            return sum / wsum;
        }

        float4 FragComposite(Varyings i) : SV_Target
        {
            float o = SAMPLE_TEXTURE2D_LOD(_HS2AoTex, sampler_LinearClamp, i.texcoord, 0).r;
            float v = pow(saturate(1.0 - o), _HS2AoParams.y);
            return float4(v, v, v, 1);
        }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off
        Pass { Name "HS2AoObscurance" Tags { "LightMode"="HS2NextFullscreen" } HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragAo
            ENDHLSL }
        Pass { Name "HS2AoBlur" Tags { "LightMode"="HS2NextFullscreen" } HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlur
            ENDHLSL }
        Pass { Name "HS2AoComposite" Tags { "LightMode"="HS2NextFullscreen" } Blend Zero SrcColor HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            ENDHLSL }
    }
}
