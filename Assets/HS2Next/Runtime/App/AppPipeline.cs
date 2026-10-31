// Render pipeline and shaders of the app. In the Editor the project's default pipeline is
// Built-in, so the app switches to its production URP asset (Assets/HS2Next/Settings/HS2NextURP.asset, Phase 3.2: Forward+,
// rendering layers, 4096 shadows; GraphicsSettings.defaultRenderPipeline and QualitySettings.renderPipeline) and turns
// asynchronous shader compilation off (no cyan placeholder shaders in captures); Restore puts the three previous values
// back. The Editor render tests and the fit tools keep the measurement asset (MeasureAssetPath) as their baseline. A player built by PlayerBuild.Build has the pipeline
// already and warms the shader set of Resources/HS2NextShaders up, as PlayerSmoke does; a player without a pipeline
// logs an error and continues (captures are then meaningless, nothing else breaks).
using UnityEngine;
using UnityEngine.Rendering;

namespace HS2Next.App
{
    public sealed class AppPipeline
    {
        /// <summary>The app's URP asset (Editor play sessions, BridgeServe, the play-mode harness and the players).</summary>
        public const string EditorAssetPath = "Assets/HS2Next/Settings/HS2NextURP.asset";

        /// <summary>The measurement asset (Forward, no rendering layers): the baseline of the Editor render tests and fit tools.</summary>
        public const string MeasureAssetPath = "Assets/HS2Next/Settings/Measure/MeasureURP.asset";

        /// <summary>
        /// A test tool's `-urpAsset` value as an asset path: null, "", "measure" or its earlier name "spike" →
        /// <see cref="MeasureAssetPath"/> (the default of the render tests and fit tools), "app" or "production" →
        /// <see cref="EditorAssetPath"/>, else the value.
        /// </summary>
        public static string ResolveAssetPath(string arg) =>
            string.IsNullOrEmpty(arg) || arg == "measure" || arg == "spike" ? MeasureAssetPath : arg == "app" || arg == "production" ? EditorAssetPath : arg;

        RenderPipelineAsset prevDefault, prevQuality;
        bool changed;
        UnityEngine.Rendering.Universal.ScriptableRendererFeature[] sceneImageFeatures;   // Phase 3.6 package C
#if UNITY_EDITOR
        bool prevAsync;
#endif

        /// <summary>What Apply did, for the stage log.</summary>
        public string Summary { get; private set; } = "";

        /// <summary>Makes URP current (Editor) or warms the shader set up (player).</summary>
        public void Apply()
        {
#if UNITY_EDITOR
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>(EditorAssetPath);
                if (asset == null)
                {
                    Summary = "URP asset " + EditorAssetPath + " not found: Built-in stays (captures are meaningless)";
                    Debug.LogError("[app] pipeline: " + Summary);
                    return;
                }
                prevDefault = GraphicsSettings.defaultRenderPipeline;
                prevQuality = QualitySettings.renderPipeline;
                prevAsync = UnityEditor.ShaderUtil.allowAsyncCompilation;
                GraphicsSettings.defaultRenderPipeline = asset;
                QualitySettings.renderPipeline = asset;
                UnityEditor.ShaderUtil.allowAsyncCompilation = false;
                changed = true;
                Summary = "URP " + asset.name + " (Editor), synchronous shader compilation";
            }
            else Summary = "pipeline " + GraphicsSettings.currentRenderPipeline.name + " already set";
#else
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                Summary = "no render pipeline in this player: characters render without their shaders";
                Debug.LogError("[app] pipeline: " + Summary);
            }
            else Summary = "pipeline " + GraphicsSettings.currentRenderPipeline.name;
            var svc = Resources.Load<ShaderVariantCollection>("HS2NextShaders");
            if (svc != null)
            {
                svc.WarmUp();
                Summary += "; shader set " + svc.shaderCount + " shaders warmed up";
            }
            else Summary += "; Resources/HS2NextShaders not found";
#endif
            sceneImageFeatures = HS2BackgroundFeature.AddSceneImageFeatures();   // Phase 3.6 package C: background / frame (run time)
            Summary += "; scene shader time " + SceneShaderTime.Install();   // Phase 3.6 package K (run time)
        }

        /// <summary>Puts back what Apply changed (Editor). Idempotent.</summary>
        public void Restore()
        {
            HS2BackgroundFeature.RemoveSceneImageFeatures(sceneImageFeatures);   // Phase 3.6 package C
            sceneImageFeatures = null;
            SceneShaderTime.Uninstall();   // Phase 3.6 package K
            if (!changed) return;
            changed = false;
            GraphicsSettings.defaultRenderPipeline = prevDefault;
            QualitySettings.renderPipeline = prevQuality;
#if UNITY_EDITOR
            UnityEditor.ShaderUtil.allowAsyncCompilation = prevAsync;
#endif
        }
    }
}
