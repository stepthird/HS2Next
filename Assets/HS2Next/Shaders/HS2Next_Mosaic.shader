// The mosaic of the genital-area parts (method (a)): the replacement of HS2's "AIT/mnpb"
// (ShaderMap; same _WhiteOff property and default) and the material the mosaic unit (Runtime/Chara/Mosaic) gives every
// slot of every mosaic-set member. Modes and the per-camera block / cell: HS2NextMosaic.hlsl.
// Depth and order: queue 2500 (the opaque range: the member is in the depth and opaque textures, so DOF and AO see it),
// ZWrite on, ZTest LEqual, every pass draws the member where it is with a constant-only polygon offset (Offset 0, -1;
// HS2NextMosaicDepth.hlsl: it wins a tie with the skin under it, not against clothes over it), cull back, no shadow caster (as HS2's), the MotionVectors and DepthNormals passes of HS2NextPasses.hlsl (TAA, the
// depth-normals prepass).
Shader "HS2Next/Mosaic"
{
    Properties
    {
        [MaterialToggle] _WhiteOff ("White Off", Float) = 1
        [Enum(Procedural, 0, Texture, 1)] _MosaicMode ("Mode", Float) = 0
        _MainTex ("Source", 2D) = "white" {}
        _DetailMainTex ("Source detail (R metal, B gloss)", 2D) = "black" {}
        _Color ("Colour", Color) = (1,1,1,1)
        _Gloss ("Gloss", Range(0,1)) = 0.55
        _Metallic ("Metallic", Range(0,1)) = 0.03
        _MosaicBlockUv ("Block (UV, per camera)", Float) = 0.03125
        _MosaicCellPx ("Cell (pixels, per camera; 0 = the camera's)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+500" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            ZTest LEqual
            Cull Back
            Offset 0, -1
            HLSLPROGRAM
            #pragma vertex HS2MosaicVert
            #pragma fragment HS2MosaicFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #define HS2_MOSAIC_FORWARD
            #include "HS2NextMosaic.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            ZWrite On
            Cull Back
            Offset 0, -1
            HLSLPROGRAM
            #pragma vertex HS2MosaicDepthVert
            #pragma fragment HS2MosaicDepthFrag
            #include "HS2NextMosaic.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode"="MotionVectors" }
            ColorMask RG
            Cull Back
            Offset 0, -1
            HLSLPROGRAM
            #pragma vertex HS2MotionVert
            #pragma fragment HS2MotionFrag
            #include "HS2NextMosaic.hlsl"
            #define HS2_PASS_A HS2MosaicSA
            #define HS2_PASS_V HS2MosaicSV
            #define HS2_PASS_VERT HS2MosaicDepthVert
            #define HS2_PASS_CLIP HS2MosaicDepthFrag
            #define HS2_PASS_POS(a) (a).pos.xyz
            #define HS2_PASS_NORMAL(a) (a).n
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ColorMask RGBA
            ZWrite On
            Cull Back
            Offset 0, -1
            HLSLPROGRAM
            #pragma vertex HS2DepthNormalsVert
            #pragma fragment HS2DepthNormalsFrag
            #include "HS2NextMosaic.hlsl"
            #define HS2_PASS_A HS2MosaicSA
            #define HS2_PASS_V HS2MosaicSV
            #define HS2_PASS_VERT HS2MosaicDepthVert
            #define HS2_PASS_CLIP HS2MosaicDepthFrag
            #define HS2_PASS_POS(a) (a).pos.xyz
            #define HS2_PASS_NORMAL(a) (a).n
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
    }
}
