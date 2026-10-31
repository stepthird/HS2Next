// The sky and environment part of a scene runtime (Phase 3.3
// package S). Source of truth: `scene.effects.ambient` (block 11) and `scene.effects.sky` (block 12); the part never
// writes the model. Applied in HS2's order (11 then 12) after every change of those blocks, a whole-scene change, Clear,
// OnLoadedRealized, map Loaded / Unloaded and the render options ambient / backgroundColor; then the ambient probe rule
// (AmbientProbe) for the active scene.
//   11 environment   RenderSettings.ambientSkyColor / EquatorColor / GroundColor of the active scene = the state's colours
//   12 sky           ViewSetup.ClearFlags = Skybox when enabled, else SolidColor; ViewSetup.Background = render.backgroundColor;
//                    the pattern (also while the sky is off, HS2's Q-e): RenderSettings.skybox = an owned copy of the
//                    StudioSky row's material (the bundle's AIOsky/V2, U3) and the sun source SkySun (a directional light
//                    of intensity 0 lighting no layer, HS2's Sky Dummy Light) = the row's Euler; unknown row → nothing
//                    changes, effects.skyMissing
//   sun feed (U3)    under URP the engine feeds a skybox material's sun (_WorldSpaceLightPos0) from RenderSettings.sun's
//                    direction (its colour and intensity do not matter; global shader values do not reach the 2018 shader),
//                    while RenderSettings.sun also designates URP's main light (MainLight, maps M4). So for every camera
//                    that draws the pattern RenderSettings.sun is SkySun from beginCameraRendering to endCameraRendering,
//                    and MainLight's light is made the brightest directional one for that render (SunSwap), which keeps it
//                    URP's main light
//   ambient probe    AmbientProbe (rule hs2: HS2's SH of the Studio colours on Trilight / Flat scenes, the map file's saved
//                    probe on Skybox-mode maps, AmbientMode.Custom; guard in LateApply; rule environment: Unity's)
//   app scene        without a map the runtime's own scene gets the Studio level's environment state: ambient intensity
//                    1.5, fog mode Exp² (fog stays off, B), default reflection Custom = the vanilla probe bundle's
//                    sky_BigSight_Env (texel-identical to the Studio level's Tokyo_BigSight_Env, U15), intensity 1
//   quality          render.aa / taaQuality (TemporalAAControl.Configure on the view), render.shadowDistance (the pipeline asset's
//                    shadow distance; restored on dispose)
// Map hooks: Loaded reads the map's saved RenderSettings (MapSavedProbe, ~15 ms, 1–7 blocks of the bundle) and records
// the scene's own ambient mode before the rule switches it, then applies everything at once into the map scene;
// Unloaded applies into the runtime's own scene again.
using System;
using System.Collections.Generic;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    public sealed class SceneSky : ISceneRuntimePart, IDisposable
    {
        /// <summary>The vanilla probe bundle's cubemap that equals the Studio level's default reflection (U15).</summary>
        public const string NoMapReflectionBundle = "studio/probe/00.unity3d", NoMapReflectionAsset = "sky_BigSight_Env";
        /// <summary>The Studio level's ambient intensity (state parity; the rule hs2 ignores it, as HS2 does).</summary>
        public const float NoMapAmbientIntensity = 1.5f;


        SceneRuntime runtime;
        bool dirtyEnv = true, dirtySky = true, hooked, disposed;
        readonly AmbientProbe ambient = new AmbientProbe();
        Transform sun;
        Light sunLight;
        Material skyCopy;
        string skyCopyRef;
        static Cubemap noMapReflection;
        static bool noMapReflectionTried;
        UniversalRenderPipelineAsset shadowAsset;
        float? shadowDistanceBefore;

        public AmbientProbe Ambient => ambient;
        /// <summary>The sun-source transform (HS2's Sky Dummy Light: a directional light of intensity 0 lighting nothing).</summary>
        public Transform SkySun => sun;
        public Light SkySunLight => sunLight;
        /// <summary>The owned copy of the current pattern's material (null before a pattern was applied).</summary>
        public Material SkyMaterial => skyCopy;
        /// <summary>The StudioSky ref the copy was made from and whether that row resolved.</summary>
        public string PatternRef { get; private set; }
        public bool PatternFound { get; private set; }
        public int AppliedCount { get; private set; }
        /// <summary>Cameras the sun swap was enqueued for (tests).</summary>
        public int FeedCount { get; private set; }

        // ------------------------------------------------------------ ISceneRuntimePart

        public void Attach(SceneRuntime r)
        {
            runtime = r;
            if (!sun)
            {
                var go = new GameObject("SkySun");
                go.transform.SetParent(r.Root.transform, false);
                sun = go.transform;
                sunLight = go.AddComponent<Light>();
                sunLight.type = LightType.Directional;
                sunLight.intensity = 0f;
                sunLight.color = Color.white;
                sunLight.shadows = LightShadows.None;
                sunLight.cullingMask = 0;
                sunLight.renderingLayerMask = 0;
                sunLight.bounceIntensity = 0f;

            }
            if (!hooked)
            {
                MapHooks.Loaded += OnMapLoaded;
                MapHooks.Unloaded += OnMapUnloaded;
                RenderOptions.Changed += OnRenderOptions;
                RenderPipelineManager.beginCameraRendering += OnBeginCamera;
                RenderPipelineManager.endCameraRendering += OnEndCamera;
                hooked = true;
            }
            ApplyShadowDistance();
            MarkAll();
        }

        public void OnChanged(SceneChangeBatch b)
        {
            foreach (var c in b.Changes)
            {
                if (c.Kind != SceneChangeKind.SceneChanged) continue;
                if (c.Path == null) { MarkAll(); continue; }
                if (!FieldPath.Overlaps("effects", c.Path)) continue;
                string block = SceneEffects.SecondSegment(c.Path);
                if (block == null) MarkAll();
                else if (block == "ambient") dirtyEnv = true;
                else if (block == "sky") dirtySky = true;
            }
        }

        void MarkAll() { dirtyEnv = dirtySky = true; }

        /// <summary>The blocks marked dirty (tests).</summary>
        public IEnumerable<string> DirtyBlocks()
        {
            if (dirtyEnv) yield return "ambient";
            if (dirtySky) yield return "sky";
        }

        public void Apply()
        {
            if (runtime == null || disposed || (!dirtyEnv && !dirtySky)) return;
            var fx = runtime.Model.Settings.Effects;
            bool env = dirtyEnv, sky = dirtySky;
            dirtyEnv = dirtySky = false;
            try
            {
                if (!MapActive()) ApplyAppScene();
                if (env) ApplyEnvironment(fx.Ambient);
                if (sky) ApplySky(fx.Sky);
                ApplyAmbient(fx.Ambient);
                AppliedCount++;
            }
            catch (Exception e)
            {
                runtime.ReportOnce(FindingSeverity.Error, "effects.applyFailed", null, "ambient / sky: " + e.GetType().Name + ": " + e.Message);
                Debug.LogWarning("sky: applying threw: " + e);
            }
        }

        /// <summary>Applies blocks 11–12 and the probe now (map hooks, tests).</summary>
        public void ApplyAll()
        {
            MarkAll();
            Apply();
        }

        public void LateApply()
        {
            if (runtime == null || disposed) return;
            var view = runtime.GetPart<SceneViewCamera>();
            if (view != null) TemporalAAControl.Configure(view.Setup, view.Camera);
            if (ambient.Guard())
                runtime.ReportOnce(FindingSeverity.Info, "effects.ambientReasserted", null, "Unity replaced the app's ambient probe; it was written again (count in effects.state realized.ambient.reasserted)");
        }

        public void Clear() => MarkAll();

        public void OnLoadedRealized() => MarkAll();

        // ------------------------------------------------------------ 11 environment

        static Color C(Color4 c) => new Color(c.R, c.G, c.B, c.A);

        void ApplyEnvironment(AmbientLightEffect a)
        {
            RenderSettings.ambientSkyColor = C(a.Sky);
            RenderSettings.ambientEquatorColor = C(a.Equator);
            RenderSettings.ambientGroundColor = C(a.Ground);
        }

        void ApplyAmbient(AmbientLightEffect a)
        {
            if (!MapActive()) ambient.SetScene(null, null);
            string finding = ambient.Apply(RenderOptions.Ambient, C(a.Sky), C(a.Equator), C(a.Ground));
            if (finding != null) runtime.ReportOnce(FindingSeverity.Info, "map.ambientProbeRecomputed", null, finding);
        }

        /// <summary>The Studio level's environment state for the runtime's own scene (no map).</summary>
        void ApplyAppScene()
        {
            RenderSettings.ambientIntensity = NoMapAmbientIntensity;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            var cube = NoMapReflection(runtime.Services);
            if (cube)
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = cube;
                RenderSettings.reflectionIntensity = 1f;
            }
        }

        static Cubemap NoMapReflection(RuntimeServices s)
        {
            if (noMapReflection || noMapReflectionTried || s?.Catalog == null) return noMapReflection;
            noMapReflectionTried = true;
            try { noMapReflection = s.Catalog.LoadAsset<Cubemap>(NoMapReflectionBundle, NoMapReflectionAsset); }
            catch (Exception e) { Debug.LogWarning("sky: the no-map reflection cubemap could not be loaded: " + e.Message); }
            return noMapReflection;
        }

        // ------------------------------------------------------------ 12 sky

        void ApplySky(SkyEffect sky)
        {
            var view = runtime.GetPart<SceneViewCamera>();
            if (view != null)
            {
                view.Setup.ClearFlags = sky.Enabled ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                view.Setup.Background = RenderOptions.BackgroundColor;
            }
            string pref = string.IsNullOrEmpty(sky.Pattern) ? null : sky.Pattern;
            if (pref == null) return;
            if (pref != skyCopyRef || !skyCopy)
            {
                var src = PatternMaterial(runtime.Services, pref, out var row);
                PatternRef = pref;
                PatternFound = src;
                if (!src)
                {
                    runtime.ReportOnce(FindingSeverity.Warning, "effects.skyMissing", null, "the StudioSky row " + pref + " was not found: the sky pattern is unchanged");
                    return;
                }
                var old = skyCopy;
                skyCopy = MaterialCopies.Copy(src);
                skyCopyRef = pref;
                sunEuler = row.SunRotation;
                RenderSettings.skybox = skyCopy;
                Destroy(old);   // the copy of the previous pattern (it was the skybox of the active scene until now)
            }
            if (sun) sun.eulerAngles = sunEuler;
            RenderSettings.skybox = skyCopy;
        }

        Vector3 sunEuler;

        static Material PatternMaterial(RuntimeServices s, string pref, out StudioSky row)
        {
            row = null;
            if (s?.Db == null || s.Catalog == null || !ContentId.TryParse(pref, out var id) || !s.Db.TryGetSky(id, out row) || row == null) return null;
            return s.Catalog.LoadAsset<Material>(row);
        }

        static void Destroy(Object o)
        {
            if (!o) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }

        /// <summary>False skips the sun swap (measurements: what the sky shader sees without it).</summary>
        public static bool FeedEnabled = true;

        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            SunSwap.End();
            if (!FeedEnabled || !skyCopy || RenderSettings.skybox != skyCopy || !sunLight || !cam || cam.clearFlags != CameraClearFlags.Skybox) return;
            SunSwap.Begin(sunLight, MainLight.Current);
            FeedCount++;
        }

        void OnEndCamera(ScriptableRenderContext ctx, Camera cam) => SunSwap.End();

        /// <summary>
        /// RenderSettings.sun = the sky's sun for one camera render (U3: the engine reads the skybox's sun direction from it
        /// when the camera starts; a swap at recording or execution time comes too late). The sky's sun lights nothing and is
        /// not one of URP's visible lights, so URP then takes the brightest directional light as its main light: the light
        /// MainLight chose gets an intensity just above every other directional light's for that render (× 1.0001 of the
        /// largest; restored at endCameraRendering), so URP's main light and its shadows stay MainLight's (U3: the chara-lit
        /// test sphere is the same to the byte with and without the swap).
        /// </summary>
        static class SunSwap
        {
            static bool active;
            static Light savedSun, bumped;
            static float bumpedIntensity;
            static Light[] lights = new Light[0];
            static int lightsFrame = -1000;

            public static void Begin(Light sky, Light main)
            {
                End();
                savedSun = RenderSettings.sun;
                RenderSettings.sun = sky;
                active = true;
                if (!main || !main.isActiveAndEnabled) return;
                if (Time.frameCount - lightsFrame > 30 || Time.frameCount < lightsFrame)
                {
                    lights = Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                    lightsFrame = Time.frameCount;
                }
                float max = 0f;
                foreach (var l in lights)
                    if (l && l != main && l != sky && l.type == LightType.Directional && l.isActiveAndEnabled) max = Mathf.Max(max, l.intensity);
                if (max < main.intensity) return;
                bumped = main;
                bumpedIntensity = main.intensity;
                main.intensity = max * 1.0001f + 1e-6f;
            }

            public static void End()
            {
                if (!active) return;
                active = false;
                RenderSettings.sun = savedSun;
                savedSun = null;
                if (bumped) bumped.intensity = bumpedIntensity;
                bumped = null;
            }
        }

        // ------------------------------------------------------------ map hooks, options

        bool Mine(MapRealization m) => runtime != null && runtime.GetPart<MapRealization>() == m;

        /// <summary>True while the runtime's map is loaded and its scene is the active one.</summary>
        bool MapActive()
        {
            var m = runtime?.GetPart<MapRealization>();
            return m != null && m.State == MapState.Loaded && m.Scene.IsValid() && UnityEngine.SceneManagement.SceneManager.GetActiveScene() == m.Scene;
        }

        void OnMapLoaded(MapRealization m)
        {
            if (!Mine(m)) return;
            var loadedMode = RenderSettings.ambientMode;
            var saved = MapSavedProbe.Read(m, runtime.Services);
            if (!saved.Ok) Debug.Log("sky: map " + m.Ref + ": saved RenderSettings not read (" + saved.Error + ")");
            ambient.SetScene(loadedMode, saved);
            dirtyEnv = dirtySky = true;
            var fx = runtime.Model.Settings.Effects;
            ApplyEnvironment(fx.Ambient);
            ApplySky(fx.Sky);
            ApplyAmbient(fx.Ambient);
            AppliedCount++;
            dirtyEnv = dirtySky = false;
        }

        void OnMapUnloaded(MapRealization m)
        {
            if (!Mine(m)) return;
            ambient.SetScene(null, null);
            ApplyAll();
        }

        void OnRenderOptions(IReadOnlyCollection<string> keys)
        {
            foreach (var k in keys)
            {
                if (k == "ambient") dirtyEnv = true;
                else if (k == "backgroundColor") dirtySky = true;
                else if (k == "shadowDistance") ApplyShadowDistance();
            }
        }

        void ApplyShadowDistance()
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!asset) return;
            float want = RenderOptions.ShadowDistance;
            if (Mathf.Approximately(asset.shadowDistance, want)) return;
            if (shadowAsset != asset) { RestoreShadowDistance(); shadowAsset = asset; shadowDistanceBefore = asset.shadowDistance; }
            asset.shadowDistance = want;
        }

        void RestoreShadowDistance()
        {
            if (shadowAsset && shadowDistanceBefore.HasValue) shadowAsset.shadowDistance = shadowDistanceBefore.Value;
            shadowAsset = null;
            shadowDistanceBefore = null;
        }

        // ------------------------------------------------------------ dispose

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (hooked)
            {
                MapHooks.Loaded -= OnMapLoaded;
                MapHooks.Unloaded -= OnMapUnloaded;
                RenderOptions.Changed -= OnRenderOptions;
                RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
                RenderPipelineManager.endCameraRendering -= OnEndCamera;
                hooked = false;
            }
            RestoreShadowDistance();
            if (skyCopy && RenderSettings.skybox == skyCopy) RenderSettings.skybox = null;
            Destroy(skyCopy);
            skyCopy = null;
            SunSwap.End();
            if (sun) Destroy(sun.gameObject);
            sun = null;
            sunLight = null;
        }
    }
}
