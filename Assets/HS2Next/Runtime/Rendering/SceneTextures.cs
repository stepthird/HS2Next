// Scene depth / opaque texture state for the fitted shaders (Phase 3.3 package C).
// The production renderer (HS2NextURP + HS2NextURP_Renderer) keeps the depth texture on with the depth copy right after
// the opaques and the opaque texture on at full resolution, so transparents can read `_CameraDepthTexture` (soft
// particles, the `AIT/main Light` depth fade, water / ice / slime depth fade) and `_CameraOpaqueTexture` (tears
// refraction, KriptoFX distortion, water / ice / slime refraction). The shaders gate those reads on two globals that this
// class sets before every camera renders:
//   _HS2NextSceneDepthValid  1 when the camera gets the depth texture before the transparents: the active URP asset
//                            supports the depth texture (or the camera forces it on), the camera does not turn it off,
//                            and the asset's default renderer copies depth after the opaques (CopyDepthMode
//                            AfterOpaques or ForcePrepass)
//   _HS2NextSceneOpaqueValid 1 when the camera gets the opaque texture (the asset's option or the camera's override)
// The measurement asset of the fit tools has neither (both 0), so every fit and regression number measured there is unchanged.
// Nested camera renders (map mirrors render inside the view camera's beginCameraRendering) push / pop the values, so the
// outer camera keeps its own after the inner one ended. Without a scriptable pipeline (Built-in reference renders) both
// stay as they were (the Built-in originals do not read them).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HS2Next
{
    public static class SceneTextures
    {
        public static readonly int DepthValidId = Shader.PropertyToID("_HS2NextSceneDepthValid");
        public static readonly int OpaqueValidId = Shader.PropertyToID("_HS2NextSceneOpaqueValid");

        static bool hooked;
        static readonly Stack<(float depth, float opaque)> stack = new Stack<(float, float)>();

        /// <summary>The values set for the last camera that began rendering (tests).</summary>
        public static bool LastDepthValid { get; private set; }
        public static bool LastOpaqueValid { get; private set; }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void EditorHook() => Hook();
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RuntimeHook() => Hook();

        /// <summary>Subscribes to the pipeline's camera callbacks (idempotent).</summary>
        public static void Hook()
        {
            if (hooked) return;
            hooked = true;
            RenderPipelineManager.beginCameraRendering += OnBegin;
            RenderPipelineManager.endCameraRendering += OnEnd;
        }

        /// <summary>The asset's default renderer copies depth before the transparents.</summary>
        public static bool CopiesDepthBeforeTransparents(UniversalRenderPipelineAsset asset)
        {
            if (!asset) return false;
            var data = asset.rendererDataList != null && asset.rendererDataList.Length > 0 ? asset.rendererDataList[0] : null;
            return data is UniversalRendererData urd && urd.copyDepthMode != CopyDepthMode.AfterTransparents;
        }

        /// <summary>Whether <paramref name="cam"/> gets the scene depth before the transparents under <paramref name="asset"/>.</summary>
        public static bool DepthValidFor(UniversalRenderPipelineAsset asset, Camera cam)
        {
            if (!asset || !CopiesDepthBeforeTransparents(asset)) return false;
            bool on = asset.supportsCameraDepthTexture;
            if (cam && cam.TryGetComponent<UniversalAdditionalCameraData>(out var d))
            {
                if (d.requiresDepthOption == CameraOverrideOption.Off) on = false;
                else if (d.requiresDepthOption == CameraOverrideOption.On) on = true;
            }
            return on;
        }

        /// <summary>Whether <paramref name="cam"/> gets the opaque texture under <paramref name="asset"/>.</summary>
        public static bool OpaqueValidFor(UniversalRenderPipelineAsset asset, Camera cam)
        {
            if (!asset) return false;
            bool on = asset.supportsCameraOpaqueTexture;
            if (cam && cam.TryGetComponent<UniversalAdditionalCameraData>(out var d))
            {
                if (d.requiresColorOption == CameraOverrideOption.Off) on = false;
                else if (d.requiresColorOption == CameraOverrideOption.On) on = true;
            }
            return on;
        }

        static void OnBegin(ScriptableRenderContext ctx, Camera cam)
        {
            stack.Push((Shader.GetGlobalFloat(DepthValidId), Shader.GetGlobalFloat(OpaqueValidId)));
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            LastDepthValid = DepthValidFor(asset, cam);
            LastOpaqueValid = OpaqueValidFor(asset, cam);
            Shader.SetGlobalFloat(DepthValidId, LastDepthValid ? 1f : 0f);
            Shader.SetGlobalFloat(OpaqueValidId, LastOpaqueValid ? 1f : 0f);
        }

        static void OnEnd(ScriptableRenderContext ctx, Camera cam)
        {
            if (stack.Count == 0) return;
            var (d, o) = stack.Pop();
            // only nested renders restore the outer camera's values; the outermost keeps its own (read by later tests)
            if (stack.Count > 0)
            {
                Shader.SetGlobalFloat(DepthValidId, d);
                Shader.SetGlobalFloat(OpaqueValidId, o);
            }
        }
    }
}
