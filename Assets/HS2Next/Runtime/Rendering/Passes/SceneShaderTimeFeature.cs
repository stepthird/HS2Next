// Shader time from the scene clock (R11; Phase 3.6 package K). URP 17.6
// writes the time globals (_Time, _SinTime, _CosTime, unity_DeltaTime, _TimeParameters, _LastTimeParameters) from
// Time.time (player; Editor edit mode: real time) at the start of every camera's render graph and again in each
// "Setup Camera Properties" pass (after the shadow maps, around a depth-normal prepass into the back buffer). A paused scene
// with water (Water_URP), the sky's clouds (AIOsky/V2) or SpeedTree wind therefore never stands still. This feature
// overrides those globals from the scene clock (t = SceneClock.Time, dt = FrameDt) right after each of URP's writes:
// at BeforeRenderingShadows (covers the shadow casters), AfterRenderingShadows (after the setup that follows the
// shadows) and AfterRenderingPrePasses (after the setup that follows a depth-normal prepass into the back buffer), three
// tiny unsafe passes that only set globals. A pause then freezes the shaders and steps advance them; two captures of a
// paused scene are byte-identical (R-M3).
//
// Scope (render.sceneShaderTime): All (every Game camera: the view, captures, thumbnails, mirrors), Captures (only
// cameras rendering into a target texture: captures, thumbnails, mirrors; the view keeps URP's time) or Off. Source: the
// clock of the latest SceneRuntime (set by SceneRuntime; null = URP's time). The feature is a run-time instance added to the
// active URP renderer by Install (AppPipeline.Apply; never saved, DontSave) and removed by Uninstall (AppPipeline.Restore).
using HS2Next.Scenes;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace HS2Next
{
    public enum SceneShaderTimeScope
    {
        Off = 0,
        Captures = 1,
        All = 2,
    }

    /// <summary>The scene shader time's state (what the passes read) and the install of the feature.</summary>
    public static class SceneShaderTime
    {
        /// <summary>The clock the shader time follows (the app's runtime); null = URP's time.</summary>
        public static SceneClock Source;
        /// <summary>Which cameras take the scene time (R11 default All).</summary>
        public static SceneShaderTimeScope Scope = SceneShaderTimeScope.All;

        /// <summary>Cameras whose render overrode the time (tests, diagnostics).</summary>
        public static int AppliedCount { get; internal set; }
        /// <summary>The last time written (seconds), NaN before the first.</summary>
        public static float LastTime { get; internal set; } = float.NaN;

        public static string ScopeName(SceneShaderTimeScope s) => s == SceneShaderTimeScope.All ? "all" : s == SceneShaderTimeScope.Captures ? "captures" : "off";

        public static bool TryParseScope(string s, out SceneShaderTimeScope scope)
        {
            switch (s)
            {
                case "all": scope = SceneShaderTimeScope.All; return true;
                case "captures": scope = SceneShaderTimeScope.Captures; return true;
                case "off": scope = SceneShaderTimeScope.Off; return true;
                default: scope = SceneShaderTimeScope.All; return false;
            }
        }

        /// <summary>True when the camera's render takes the scene time.</summary>
        public static bool Applies(Camera cam, CameraType type)
        {
            if (Source == null || Scope == SceneShaderTimeScope.Off || !cam || type != CameraType.Game) return false;
            return Scope == SceneShaderTimeScope.All || cam.targetTexture != null;
        }

        static SceneShaderTimeFeature feature;
        static ScriptableRendererData featureData;
        static bool runtimeFeature;

        /// <summary>"asset" (the renderer asset has the feature), "runtime" (added by Install), "none".</summary>
        public static string Installed => feature ? (runtimeFeature ? "runtime" : "asset") : "none";

        /// <summary>Adds a run-time feature to every renderer of the active URP asset that has none (idempotent).</summary>
        public static string Install()
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset asset) || asset.rendererDataList.Length == 0) return "no URP renderer";
            var rd = asset.rendererDataList[0];
            if (!rd) return "no URP renderer";
            if (feature && featureData == rd) return "installed";
            if (rd.TryGetRendererFeature<SceneShaderTimeFeature>(out var f)) { feature = f; featureData = rd; runtimeFeature = false; return "asset"; }
            f = ScriptableObject.CreateInstance<SceneShaderTimeFeature>();
            f.name = "SceneShaderTime (runtime)";
            f.hideFlags = HideFlags.DontSave;
            f.SetActive(true);
            rd.rendererFeatures.Add(f);
            rd.SetDirty();
            feature = f;
            featureData = rd;
            runtimeFeature = true;
            return "runtime";
        }

        /// <summary>Removes the run-time feature (idempotent).</summary>
        public static void Uninstall()
        {
            if (feature && runtimeFeature && featureData)
            {
                featureData.rendererFeatures.Remove(feature);
                featureData.SetDirty();
                if (Application.isPlaying) Object.Destroy(feature); else Object.DestroyImmediate(feature);
            }
            feature = null;
            featureData = null;
            runtimeFeature = false;
        }
    }

    public sealed class SceneShaderTimeFeature : ScriptableRendererFeature
    {
        SceneShaderTimePass shadows, afterShadows, afterPrepasses;

        public override void Create()
        {
            shadows = new SceneShaderTimePass(RenderPassEvent.BeforeRenderingShadows);
            afterShadows = new SceneShaderTimePass(RenderPassEvent.AfterRenderingShadows);
            afterPrepasses = new SceneShaderTimePass(RenderPassEvent.AfterRenderingPrePasses);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            ref var cd = ref renderingData.cameraData;
            var src = SceneShaderTime.Source;
            if (src == null || !SceneShaderTime.Applies(cd.camera, cd.cameraType)) return;
            float t = (float)src.Time, dt = src.FrameDt;
            shadows.Set(t, dt);
            afterShadows.Set(t, dt);
            afterPrepasses.Set(t, dt);
            renderer.EnqueuePass(shadows);
            renderer.EnqueuePass(afterShadows);
            renderer.EnqueuePass(afterPrepasses);
            SceneShaderTime.AppliedCount++;
            SceneShaderTime.LastTime = t;
        }
    }

    public sealed class SceneShaderTimePass : ScriptableRenderPass
    {
        static readonly int TimeId = Shader.PropertyToID("_Time");
        static readonly int SinTimeId = Shader.PropertyToID("_SinTime");
        static readonly int CosTimeId = Shader.PropertyToID("_CosTime");
        static readonly int DeltaTimeId = Shader.PropertyToID("unity_DeltaTime");
        static readonly int TimeParametersId = Shader.PropertyToID("_TimeParameters");
        static readonly int LastTimeParametersId = Shader.PropertyToID("_LastTimeParameters");

        float time, deltaTime;

        public SceneShaderTimePass(RenderPassEvent e)
        {
            renderPassEvent = e;
            profilingSampler = new ProfilingSampler("Scene shader time");
        }

        public void Set(float t, float dt) { time = t; deltaTime = dt; }

        sealed class Data { internal float time, deltaTime; }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddUnsafePass<Data>("Scene shader time", out var data, profilingSampler))
            {
                data.time = time;
                data.deltaTime = deltaTime;
                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (Data d, UnsafeGraphContext ctx) => Write(ctx.cmd, d.time, d.deltaTime));
            }
        }

        /// <summary>The globals URP's SetShaderTimeValues writes, from (t, dt) (the same formulas, Unity's documented layout).</summary>
        static void Write(UnsafeCommandBuffer cmd, float t, float dt)
        {
            float inv = dt > 0f ? 1f / dt : 0f;
            float last = t - dt;
            cmd.SetGlobalVector(TimeId, new Vector4(t / 20f, t, t * 2f, t * 3f));
            cmd.SetGlobalVector(SinTimeId, new Vector4(Mathf.Sin(t / 8f), Mathf.Sin(t / 4f), Mathf.Sin(t / 2f), Mathf.Sin(t)));
            cmd.SetGlobalVector(CosTimeId, new Vector4(Mathf.Cos(t / 8f), Mathf.Cos(t / 4f), Mathf.Cos(t / 2f), Mathf.Cos(t)));
            cmd.SetGlobalVector(DeltaTimeId, new Vector4(dt, inv, dt, inv));
            cmd.SetGlobalVector(TimeParametersId, new Vector4(t, Mathf.Sin(t), Mathf.Cos(t), 0f));
            cmd.SetGlobalVector(LastTimeParametersId, new Vector4(last, Mathf.Sin(last), Mathf.Cos(last), 0f));
        }
    }
}
