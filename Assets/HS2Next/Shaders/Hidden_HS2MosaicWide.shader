// The wide screen-space mosaic (method (b), option render.mosaicWide). Drawn by
// MosaicWideFeature / MosaicWidePass (Runtime/Rendering/Passes) after post-processing and the depth of field. Every
// coordinate below is a render-target pixel as SV_Position gives it (the mask is drawn with the camera's render-into-texture
// projection, so the mask, the camera colour and the CPU's anchors share one pixel grid). Up to four groups (characters),
// group g in channel g of the mask, each with its block B (pixels) and its grid anchor (pixels):
//   9-12 Mask     DrawRenderer of a member with the camera's own view-projection _HS2MWViewProj (jitter included) and the
//                 member's depth placement (HS2NextMosaicDepth.hlsl: the same position and constant offset), depth-tested
//                 LEqual against the camera's depth buffer (ZWrite off): writes 1 into channel g (pass 9 + g) exactly
//                 where the member won the depth test in the frame - nothing under clothes, an arm or the body
//   0-3 Mask      the same for a multisampled camera (its depth buffer cannot be attached here): the member's eye depth
//                 against the camera depth texture with a 0.002 % + 0.0005 tolerance
//   4 Quarter     the full-resolution mask reduced 4 x 4 -> 1 by max (RGBA8)
//   5 Occupancy   per group at block resolution (R8): 1 when a quarter texel overlapping the block's pixel rectangle holds
//                 the group
//   6 Dilate      per group at block resolution: the occupancy grown by two blocks (every block within 1.5 blocks of a
//                 covered pixel is covered; conservative)
//   7 Mean        per group at block resolution (RGBA16F): the mean of 4 x 4 bilinear samples of the camera colour over the
//                 block (the part inside the frame)
//   8 Composite   full resolution: the first group (sorted by B, largest first: the larger block wins) whose dilated block
//                 covers the pixel gives the block's mean; other pixels keep the camera colour. _HS2MWDebug = 1 (Editor tests; the pass sets it per draw)
//                 writes the coverage instead: (1, (g + 1) / 8, B / 256) on covered pixels, black elsewhere.
Shader "HS2Next/Hidden/HS2MosaicWide"
{
    Properties { }
    HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "HS2NextMosaicDepth.hlsl"

        // mask (globals set by the pass for DrawRenderer)
        float4x4 _HS2MWViewProj;
        float4x4 _HS2MWView;
        TEXTURE2D(_HS2MWDepth);
        float4 _HS2MWSize;          // render width, height, 1 / width, 1 / height
        float4 _HS2MWDepthParams;   // 1 = orthographic, near, far, unused
        // reductions and composite (property blocks)
        TEXTURE2D(_HS2MWMask);
        TEXTURE2D(_HS2MWQuarter);
        TEXTURE2D(_HS2MWOcc);
        float4 _HS2MWGroup;         // B, anchor x, anchor y, channel
        float4 _HS2MWGrid;          // first block index x, y, blocks x, y
        float _HS2MWCount, _HS2MWDebug;
        TEXTURE2D(_HS2MWDil0); TEXTURE2D(_HS2MWDil1); TEXTURE2D(_HS2MWDil2); TEXTURE2D(_HS2MWDil3);
        TEXTURE2D(_HS2MWMean0); TEXTURE2D(_HS2MWMean1); TEXTURE2D(_HS2MWMean2); TEXTURE2D(_HS2MWMean3);
        float4 _HS2MWGroup0, _HS2MWGroup1, _HS2MWGroup2, _HS2MWGroup3;
        float4 _HS2MWGrid0, _HS2MWGrid1, _HS2MWGrid2, _HS2MWGrid3;

        // ---------------------------------------------------------------- mask
        struct MaskA { float4 pos : POSITION; float3 n : NORMAL; };
        struct MaskV { float4 pos : SV_POSITION; float eye : TEXCOORD0; };

        MaskV MaskVert(MaskA a)
        {
            MaskV o;
            float4 w = float4(HS2MosaicPositionWS(a.pos.xyz, a.n), 1.0);
            o.pos = mul(_HS2MWViewProj, w);
            o.eye = -mul(_HS2MWView, w).z;
            return o;
        }

        float SceneEye(float raw)
        {
            if (_HS2MWDepthParams.x > 0.5)
            {
            #if UNITY_REVERSED_Z
                raw = 1.0 - raw;
            #endif
                return lerp(_HS2MWDepthParams.y, _HS2MWDepthParams.z, raw);
            }
            return LinearEyeDepth(raw, _ZBufferParams);
        }

        float MaskVisible(MaskV i)
        {
            int2 p = clamp((int2)i.pos.xy, int2(0, 0), (int2)_HS2MWSize.xy - 1);
            float scene = SceneEye(LOAD_TEXTURE2D(_HS2MWDepth, p).r);
            return i.eye <= scene * 1.00002 + 0.0005 ? 1.0 : 0.0;
        }

        half4 MaskFragR(MaskV i) : SV_Target { clip(MaskVisible(i) - 0.5); return half4(1, 0, 0, 0); }
        half4 MaskFragG(MaskV i) : SV_Target { clip(MaskVisible(i) - 0.5); return half4(0, 1, 0, 0); }
        half4 MaskFragB(MaskV i) : SV_Target { clip(MaskVisible(i) - 0.5); return half4(0, 0, 1, 0); }
        half4 MaskFragA(MaskV i) : SV_Target { clip(MaskVisible(i) - 0.5); return half4(0, 0, 0, 1); }
        half4 MaskDepthFragR(MaskV i) : SV_Target { return half4(1, 0, 0, 0); }
        half4 MaskDepthFragG(MaskV i) : SV_Target { return half4(0, 1, 0, 0); }
        half4 MaskDepthFragB(MaskV i) : SV_Target { return half4(0, 0, 1, 0); }
        half4 MaskDepthFragA(MaskV i) : SV_Target { return half4(0, 0, 0, 1); }

        // ---------------------------------------------------------------- reductions
        half4 FragQuarter(Varyings i) : SV_Target
        {
            int2 q = (int2)i.positionCS.xy;
            int2 maxP = (int2)_HS2MWSize.xy - 1;
            half4 m = 0;
            [unroll] for (int y = 0; y < 4; y++)
                [unroll] for (int x = 0; x < 4; x++)
                    m = max(m, LOAD_TEXTURE2D(_HS2MWMask, min(q * 4 + int2(x, y), maxP)));
            return m;
        }

        // The pixel rectangle [lo, hi) of block texel t of a group (clamped to the frame).
        void BlockRect(int2 t, float4 group, float4 grid, out float2 lo, out float2 hi)
        {
            float2 k = float2(t) + grid.xy;
            lo = group.yz + k * group.x;
            hi = lo + group.x;
            lo = clamp(lo, 0, _HS2MWSize.xy);
            hi = clamp(hi, 0, _HS2MWSize.xy);
        }

        half4 FragOccupancy(Varyings i) : SV_Target
        {
            float2 lo, hi;
            BlockRect((int2)i.positionCS.xy, _HS2MWGroup, _HS2MWGrid, lo, hi);
            if (any(hi <= lo)) return 0;
            int2 q0 = (int2)floor(lo / 4.0), q1 = (int2)floor((hi - 1.0) / 4.0);
            q1 = min(q1, q0 + 127);
            int c = (int)_HS2MWGroup.w;
            [loop] for (int y = q0.y; y <= q1.y; y++)
                [loop] for (int x = q0.x; x <= q1.x; x++)
                {
                    half4 m = LOAD_TEXTURE2D(_HS2MWQuarter, int2(x, y));
                    half v = c == 0 ? m.r : c == 1 ? m.g : c == 2 ? m.b : m.a;
                    if (v > 0.5) return 1;
                }
            return 0;
        }

        half4 FragDilate(Varyings i) : SV_Target
        {
            int2 t = (int2)i.positionCS.xy;
            int2 n = (int2)_HS2MWGrid.zw - 1;
            half o = 0;
            [unroll] for (int y = -2; y <= 2; y++)
                [unroll] for (int x = -2; x <= 2; x++)
                    o = max(o, LOAD_TEXTURE2D(_HS2MWOcc, clamp(t + int2(x, y), int2(0, 0), n)).r);
            return o;
        }

        half4 FragMean(Varyings i) : SV_Target
        {
            float2 lo, hi;
            BlockRect((int2)i.positionCS.xy, _HS2MWGroup, _HS2MWGrid, lo, hi);
            if (any(hi <= lo)) return 0;
            float2 step = (hi - lo) / 4.0;
            half3 sum = 0;
            [unroll] for (int y = 0; y < 4; y++)
                [unroll] for (int x = 0; x < 4; x++)
                {
                    float2 uv = (lo + (float2(x, y) + 0.5) * step) * _HS2MWSize.zw;
                    sum += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
                }
            return half4(sum / 16.0, 1);
        }

        // ---------------------------------------------------------------- composite
        bool Covered(TEXTURE2D_PARAM(dil, s0), TEXTURE2D_PARAM(mean, s1), float4 group, float4 grid, float2 px, out half3 c)
        {
            c = 0;
            float2 k = floor((px - group.yz) / group.x);
            int2 t = (int2)(k - grid.xy);
            if (any(t < 0) || any(t >= (int2)grid.zw)) return false;
            if (LOAD_TEXTURE2D(dil, t).r < 0.5) return false;
            c = LOAD_TEXTURE2D(mean, t).rgb;
            return true;
        }

        half4 Covering(int g, float4 group, half3 c)
        {
            return _HS2MWDebug > 0.5 ? half4(1, (g + 1) / 8.0, group.x / 256.0, 1) : half4(c, 1);
        }

        half4 FragComposite(Varyings i) : SV_Target
        {
            float2 px = floor(i.positionCS.xy) + 0.5;
            half3 c;
            if (_HS2MWCount > 0.5 && Covered(TEXTURE2D_ARGS(_HS2MWDil0, sampler_PointClamp), TEXTURE2D_ARGS(_HS2MWMean0, sampler_PointClamp), _HS2MWGroup0, _HS2MWGrid0, px, c)) return Covering(0, _HS2MWGroup0, c);
            if (_HS2MWCount > 1.5 && Covered(TEXTURE2D_ARGS(_HS2MWDil1, sampler_PointClamp), TEXTURE2D_ARGS(_HS2MWMean1, sampler_PointClamp), _HS2MWGroup1, _HS2MWGrid1, px, c)) return Covering(1, _HS2MWGroup1, c);
            if (_HS2MWCount > 2.5 && Covered(TEXTURE2D_ARGS(_HS2MWDil2, sampler_PointClamp), TEXTURE2D_ARGS(_HS2MWMean2, sampler_PointClamp), _HS2MWGroup2, _HS2MWGrid2, px, c)) return Covering(2, _HS2MWGroup2, c);
            if (_HS2MWCount > 3.5 && Covered(TEXTURE2D_ARGS(_HS2MWDil3, sampler_PointClamp), TEXTURE2D_ARGS(_HS2MWMean3, sampler_PointClamp), _HS2MWGroup3, _HS2MWGrid3, px, c)) return Covering(3, _HS2MWGroup3, c);
            if (_HS2MWDebug > 0.5) return half4(0, 0, 0, 1);
            return LOAD_TEXTURE2D_X(_BlitTexture, (int2)i.positionCS.xy);
        }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        Pass { Name "MaskR" Tags { "LightMode"="HS2NextFullscreen" } ColorMask R Cull Off HLSLPROGRAM
            #pragma vertex MaskVert
            #pragma fragment MaskFragR
            ENDHLSL }
        Pass { Name "MaskG" Tags { "LightMode"="HS2NextFullscreen" } ColorMask G Cull Off HLSLPROGRAM
            #pragma vertex MaskVert
            #pragma fragment MaskFragG
            ENDHLSL }
        Pass { Name "MaskB" Tags { "LightMode"="HS2NextFullscreen" } ColorMask B Cull Off HLSLPROGRAM
            #pragma vertex MaskVert
            #pragma fragment MaskFragB
            ENDHLSL }
        Pass { Name "MaskA" Tags { "LightMode"="HS2NextFullscreen" } ColorMask A Cull Off HLSLPROGRAM
            #pragma vertex MaskVert
            #pragma fragment MaskFragA
            ENDHLSL }
        Pass { Name "Quarter" Tags { "LightMode"="HS2NextFullscreen" } HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragQuarter
            ENDHLSL }
        Pass { Name "Occupancy" Tags { "LightMode"="HS2NextFullscreen" } HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragOccupancy
            ENDHLSL }
        Pass { Name "Dilate" Tags { "LightMode"="HS2NextFullscreen" } HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDilate
            ENDHLSL }
        Pass { Name "Mean" Tags { "LightMode"="HS2NextFullscreen" } HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragMean
            ENDHLSL }
        Pass { Name "Composite" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            ENDHLSL }
        Pass { Name "MaskDepthR" Tags { "LightMode"="HS2NextFullscreen" } ColorMask R Cull Off ZTest LEqual ZWrite Off Offset 0, -1 HLSLPROGRAM
            #pragma vertex MaskVert
            #pragma fragment MaskDepthFragR
            ENDHLSL }
        Pass { Name "MaskDepthG" Tags { "LightMode"="HS2NextFullscreen" } ColorMask G Cull Off ZTest LEqual ZWrite Off Offset 0, -1 HLSLPROGRAM
            #pragma vertex MaskVert
            #pragma fragment MaskDepthFragG
            ENDHLSL }
        Pass { Name "MaskDepthB" Tags { "LightMode"="HS2NextFullscreen" } ColorMask B Cull Off ZTest LEqual ZWrite Off Offset 0, -1 HLSLPROGRAM
            #pragma vertex MaskVert
            #pragma fragment MaskDepthFragB
            ENDHLSL }
        Pass { Name "MaskDepthA" Tags { "LightMode"="HS2NextFullscreen" } ColorMask A Cull Off ZTest LEqual ZWrite Off Offset 0, -1 HLSLPROGRAM
            #pragma vertex MaskVert
            #pragma fragment MaskDepthFragA
            ENDHLSL }
    }
}
