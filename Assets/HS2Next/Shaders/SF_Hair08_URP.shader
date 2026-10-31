// URP replacement for HS2 "Shader Forge/hair_08" (hair shader type 1 "cutout", material hair_cutout in
// chara/hair_shader_mat.unity3d). Same property names, types and defaults as the original (only the
// _MeshColor_01 default differs from hair_07). Behaviour inferred by black-box probing only (SkinFit.Sweep on the
// hair_07 case set, Spike/out/fit/Shader_Forge_hair_08_*.txt): identical to hair_07 (see SF_Hair07_URP.shader and
// HS2NextHair.hlsl) except the coverage, a fixed alpha test clip(_MainTex.a - 0.5) (no screen door; _Cutoff has no
// effect), also in the shadow caster.
Shader "HS2Next/Shader Forge/hair_08"
{
    Properties
    {
        _Color ("Color", Color) = (0.585,0.317,0.317,1)
        _Specular ("Specular", Color) = (0.155,0.641,0.764,1)
        _TopColor ("Top Color", Color) = (1,0,0,1)
        _UnderColor ("Under Color", Color) = (0,0.104,1,1)
        _TopGradation ("Top Gradation", Range(1,5)) = 1
        _UnderGradation ("Under Gradation", Range(1,5)) = 1
        _Metallic ("Metallic", Range(0,1)) = 0
        _Smoothness ("Smoothness", Range(0,1)) = 0
        _DetailScale ("Detail Scale", Range(0,1)) = 0
        _HighLight ("HighLight", Range(0,20)) = 8.9
        _MainTex ("MainTex", 2D) = "white" {}
        _BumpMap ("BumpMap", 2D) = "bump" {}
        _ColorMask_A0B1 ("ColorMask_A(0)/B(1)", Range(0,1)) = 0
        _ColorMask ("ColorMask", 2D) = "white" {}
        _ColorMask_B ("ColorMask_B", 2D) = "white" {}
        _DetailMask ("Detail Mask", 2D) = "bump" {}
        _Occlusion ("Occlusion", 2D) = "white" {}
        _Rimpower ("Rimpower", Range(2.7,20)) = 2.7
        _ExGloss ("Ex Gloss", Range(0,1)) = 0
        _Ringoff ("Ringoff", Range(0,1)) = 0
        [MaterialToggle] _Ringcontrolr_onoff ("Ringcontrolr_on/off", Float) = 1
        _Ring_onwoffb ("Ring_on(w)/off(b)", 2D) = "white" {}
        [MaterialToggle] _Mesh_onoff ("Mesh_on/off", Float) = 0.585
        _MeshColorMask ("MeshColorMask", 2D) = "white" {}
        _MeshColorMask_B ("MeshColorMask_B", 2D) = "black" {}
        _MeshColor_01 ("MeshColor_01", Color) = (1,0,0,1)
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        Cull Off
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HS2HairVert
            #pragma fragment HS2HairFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #define HS2_HAIR_CUTOUT
            #include "HS2NextHair.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HS2HairShadowVert
            #pragma fragment HS2HairShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #define HS2_HAIR_CUTOUT
            #include "HS2NextHair.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HS2HairVert
            #pragma fragment HS2HairDepthFrag
            #define HS2_HAIR_CUTOUT
            #include "HS2NextHair.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode"="MotionVectors" }
            ZWrite On ColorMask RG
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HS2MotionVert
            #pragma fragment HS2MotionFrag
            #define HS2_HAIR_CUTOUT
            #include "HS2NextHair.hlsl"
            #define HS2_PASS_A HS2HairAttributes
            #define HS2_PASS_V HS2HairVaryings
            #define HS2_PASS_VERT HS2HairVert
            #define HS2_PASS_CLIP HS2HairDepthFrag
            #define HS2_PASS_POS(a) (a).positionOS.xyz
            #define HS2_PASS_NORMAL(a) (a).normalOS
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On ColorMask RGBA
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HS2DepthNormalsVert
            #pragma fragment HS2DepthNormalsFrag
            #define HS2_HAIR_CUTOUT
            #include "HS2NextHair.hlsl"
            #define HS2_PASS_A HS2HairAttributes
            #define HS2_PASS_V HS2HairVaryings
            #define HS2_PASS_VERT HS2HairVert
            #define HS2_PASS_CLIP HS2HairDepthFrag
            #define HS2_PASS_POS(a) (a).positionOS.xyz
            #define HS2_PASS_NORMAL(a) (a).normalOS
            #include "HS2NextPasses.hlsl"
            ENDHLSL
        }
    }
}
