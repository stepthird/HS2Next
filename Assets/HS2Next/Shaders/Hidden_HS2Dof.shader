// HS2 Studio's item-focus depth of field under URP (Phase 3.3 package C). Drawn by
// HS2DofFeature / HS2DofPass (Runtime/Rendering/Passes) as full-screen triangles; the passes in order:
//   0 CoC        full res, RG16F: R = coc, G = coc of pixels in front of the focus (near) else 0, with HS2's legacy
//                DepthOfField formula coc = saturate(A |d - f| / d - focalSize), A = 1 / (1 - aperture) - 1, d and f the
//                linear eye depths of the pixel and of the focus point (scale-free, so equal to the legacy's
//                Linear01Depth form)
//   1 Downsample half res, RGBA16F: the 2x2 full-resolution pixels, colour weighted by their coc, and the average coc
//   2 NearDilate half res, R16F: the near coc spread outwards over the distance it blurs (foreground overlap: a blurred
//                foreground covers the sharp background behind its edge, as the legacy near blur does)
//   3 Blur       half res, RGBA16F: disc gather (22 taps on a disc of radius coc x 1.1 R half-res texels), every tap
//                weighted by its own coc (max of coc and dilated near coc; the centre max(coc / 4, 0.1)), alpha = the coc
//   4 Composite  full res, the legacy DOF's two-scale merge: a small disc blur at full resolution with radius coc x 2R
//                full-res texels in which every tap counts with its own coc (at least 0.1; the centre counts
//                max(coc / 4, 0.1)), then towards the half-resolution blur by smoothstep(0.65, 0.85, coc); coc = max(full-res
//                coc, dilated near coc); coc < 0.01 keeps the scene pixel. (Fix round: the first version blended
//                saturate(coc R) towards the half-resolution blur, which blurred nearly-in-focus pixels ~1.8x wider than
//                HS2, package H's dof480 cases.)
//   5 Debug      full res (tests, HS2DofFeature.DebugView): rgb = (full-res coc, dilated near coc, big-blur weight)
// R = maxBlurSize (HS2: 2 half-res texels) x frame height / reference height (captures at other sizes blur the same
// fraction of the frame as the view). Parameters: _HS2DofParams (A, focalSize, focus eye depth, R),
// _HS2DofHalfTexel (1 / half width, 1 / half height, half width, half height).
Shader "HS2Next/Hidden/HS2Dof"
{
    Properties { }
    HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        TEXTURE2D(_HS2DofCoc);
        TEXTURE2D(_HS2DofHalf);
        TEXTURE2D(_HS2DofNear);
        TEXTURE2D(_HS2DofBlur);
        float4 _HS2DofParams;
        float4 _HS2DofHalfTexel;
        // Fitted on package H's dof480 captures (HS2 vs HS2Next at the view's height, 6 cases; fix round of Phase 3.3):
        // a sharp tap still counts 0.1 in the small blur (HS2 lets nearly-sharp edges bleed a little; 0 or 1 were worse),
        // and the half-resolution disc reaches 1.1 R (the Gaussian-equivalent widths then match HS2's: 3.0 / 3.0 / 3.25 /
        // 2.75 px vs 3.0 / 3.0 / 3.0 / 2.75 px at coc 1, 1.25 vs 1.25 px at aperture 0.3)
        static const float _HS2DofSmallFloor = 0.1;
        static const float _HS2DofBigScale = 1.1;

        #define DOF_TAPS 22
        // a fixed disc kernel: centre excluded, unit radius, two rings and an inner set (rotated so no two taps align)
        static const float2 kDisc[DOF_TAPS] =
        {
            float2( 0.3333, 0.0000), float2( 0.1667, 0.2887), float2(-0.1667, 0.2887), float2(-0.3333, 0.0000),
            float2(-0.1667,-0.2887), float2( 0.1667,-0.2887),
            float2( 0.6158, 0.2551), float2( 0.2551, 0.6158), float2(-0.2551, 0.6158), float2(-0.6158, 0.2551),
            float2(-0.6158,-0.2551), float2(-0.2551,-0.6158), float2( 0.2551,-0.6158), float2( 0.6158,-0.2551),
            float2( 1.0000, 0.0000), float2( 0.7071, 0.7071), float2( 0.0000, 1.0000), float2(-0.7071, 0.7071),
            float2(-1.0000, 0.0000), float2(-0.7071,-0.7071), float2( 0.0000,-1.0000), float2( 0.7071,-0.7071)
        };

        float2 CocAt(float2 uv)
        {
            float raw = SampleSceneDepth(uv);
            float d = max(LinearEyeDepth(raw, _ZBufferParams), 1e-5);
            float f = _HS2DofParams.z;
            float coc = saturate(_HS2DofParams.x * abs(d - f) / d - _HS2DofParams.y);
            return float2(coc, d < f ? coc : 0);
        }

        float4 FragCoc(Varyings i) : SV_Target
        {
            return float4(CocAt(i.texcoord), 0, 1);
        }

        float4 FragDownsample(Varyings i) : SV_Target
        {
            // the four full-resolution pixels, colour weighted by their coc (a sharp object's pixels do not spread into
            // the blurred half-resolution image), coc averaged
            float2 h = _HS2DofHalfTexel.xy * 0.25;
            float3 sum = 0; float wsum = 0, csum = 0;
            [unroll] for (int k = 0; k < 4; k++)
            {
                float2 uv = i.texcoord + float2(k & 1 ? h.x : -h.x, k & 2 ? h.y : -h.y);
                float cc = SAMPLE_TEXTURE2D_LOD(_HS2DofCoc, sampler_PointClamp, uv, 0).r;
                float w = cc + 0.01;
                sum += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).rgb * w;
                wsum += w; csum += cc;
            }
            return float4(sum / wsum, csum * 0.25);
        }

        float4 FragNearDilate(Varyings i) : SV_Target
        {
            float R = _HS2DofParams.w;
            float m = SAMPLE_TEXTURE2D_LOD(_HS2DofCoc, sampler_LinearClamp, i.texcoord, 0).g;
            [unroll] for (int k = 0; k < DOF_TAPS; k++)
            {
                float2 o = kDisc[k] * R;
                float n = SAMPLE_TEXTURE2D_LOD(_HS2DofCoc, sampler_LinearClamp, i.texcoord + o * _HS2DofHalfTexel.xy, 0).g;
                // the tap's blur reaches this pixel
                float reach = saturate(n * R - length(o) + 1.0);
                m = max(m, n * reach);
            }
            return float4(m, 0, 0, 1);
        }

        float4 FragBlur(Varyings i) : SV_Target
        {
            float R = _HS2DofParams.w;
            float4 centre = SAMPLE_TEXTURE2D_LOD(_HS2DofHalf, sampler_LinearClamp, i.texcoord, 0);
            float nearD = SAMPLE_TEXTURE2D_LOD(_HS2DofNear, sampler_LinearClamp, i.texcoord, 0).r;
            float c = max(centre.a, nearD);
            float r = c * R * _HS2DofBigScale;
            if (r < 0.25) return float4(centre.rgb, c);
            float w0 = max(c * 0.25, 0.1);
            float3 sum = centre.rgb * w0;
            float wsum = w0;
            [unroll] for (int k = 0; k < DOF_TAPS; k++)
            {
                float2 o = kDisc[k] * r;
                float4 t = SAMPLE_TEXTURE2D_LOD(_HS2DofHalf, sampler_LinearClamp, i.texcoord + o * _HS2DofHalfTexel.xy, 0);
                float tn = SAMPLE_TEXTURE2D_LOD(_HS2DofNear, sampler_LinearClamp, i.texcoord + o * _HS2DofHalfTexel.xy, 0).r;
                // each tap counts with its own coc (the legacy weighting): a sharp tap (coc 0) adds nothing, a nearly
                // sharp object bleeds little into a blurred background, a blurred foreground tap fully (its near coc)
                float w = max(t.a, tn);
                sum += t.rgb * w;
                wsum += w;
            }
            return float4(sum / wsum, c);
        }

        float4 FragComposite(Varyings i) : SV_Target
        {
            float4 scene = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, i.texcoord, 0);
            float coc = SAMPLE_TEXTURE2D_LOD(_HS2DofCoc, sampler_PointClamp, i.texcoord, 0).r;
            float nearD = SAMPLE_TEXTURE2D_LOD(_HS2DofNear, sampler_LinearClamp, i.texcoord, 0).r;
            float c = max(coc, nearD);
            if (c < 0.01) return scene;
            // small blur at full resolution, taps weighted by their own coc
            float2 fullTexel = _HS2DofHalfTexel.xy * 0.5;
            float r = c * _HS2DofParams.w * 2.0;
            float w0 = max(c * 0.25, 0.1);
            float3 sum = scene.rgb * w0;
            float wsum = w0;
            [unroll] for (int k = 0; k < DOF_TAPS; k++)
            {
                float2 uv = i.texcoord + kDisc[k] * r * fullTexel;
                float tc = SAMPLE_TEXTURE2D_LOD(_HS2DofCoc, sampler_PointClamp, uv, 0).r;
                float3 t = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
                float tw = max(tc, _HS2DofSmallFloor);
                sum += t * tw;
                wsum += tw;
            }
            float3 smallBlur = sum / wsum;
            float3 bigBlur = SAMPLE_TEXTURE2D_LOD(_HS2DofBlur, sampler_LinearClamp, i.texcoord, 0).rgb;
            return float4(lerp(smallBlur, bigBlur, smoothstep(0.65, 0.85, c)), scene.a);
        }

        float4 FragDebug(Varyings i) : SV_Target
        {
            float coc = SAMPLE_TEXTURE2D_LOD(_HS2DofCoc, sampler_PointClamp, i.texcoord, 0).r;
            float nearD = SAMPLE_TEXTURE2D_LOD(_HS2DofNear, sampler_LinearClamp, i.texcoord, 0).r;
            return float4(coc, nearD, smoothstep(0.65, 0.85, max(coc, nearD)), 1);
        }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        // The light mode only lets the player's shader variant collection list the passes (PlayerBuild.WriteShaderSet);
        // the feature draws them by index.
        Pass { Name "HS2DofCoC" Tags { "LightMode"="HS2NextFullscreen" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCoc
            ENDHLSL }
        Pass { Name "HS2DofDownsample" Tags { "LightMode"="HS2NextFullscreen" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDownsample
            ENDHLSL }
        Pass { Name "HS2DofNearDilate" Tags { "LightMode"="HS2NextFullscreen" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragNearDilate
            ENDHLSL }
        Pass { Name "HS2DofBlur" Tags { "LightMode"="HS2NextFullscreen" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlur
            ENDHLSL }
        Pass { Name "HS2DofComposite" Tags { "LightMode"="HS2NextFullscreen" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            ENDHLSL }
        Pass { Name "HS2DofDebug" Tags { "LightMode"="HS2NextFullscreen" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDebug
            ENDHLSL }
    }
}
