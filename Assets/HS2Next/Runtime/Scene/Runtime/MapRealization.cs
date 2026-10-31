// Map realization: `scene.map.ref` → the Studio map row's
// streamed-scene bundle loaded additively, then a fixed post-load pipeline (Phase 3.2), each step guarded (an
// exception becomes the finding map.stepFailed {step} and the next step runs) and timed (StepMs):
//    2 structure  MapStructure.Build (roots in GetRootGameObjects order, renderers, lights, scripts, the MapComponent by
//                 rule M2), the roots' poses recorded, static-batched renderers counted
//    3 audio      MapAudio.Silence (every AudioSource stopped and disabled until 3.6), map cameras / listeners disabled
//    4 materials  MapMaterials.Apply: one owned copy per distinct bundle material, ShaderMap swap, rendering layers
//    5 lighting   MapLighting.Apply: BundleLights.Prepare(fold: true) per root (URP data, colour × intensity, rendering layers)
//    6 lightmaps  MapLightmaps.Apply: LightmapPrefab entries, ShaderMap.RouteLightmapped per lightmapped renderer, Bakery
//                 detection; LightProbes.TetrahedralizeAsync
//    7 active     SetActiveScene(map) (its RenderSettings and LightmapSettings apply); the skybox copy (MapMaterials)
//    8 pose       root 0 = (T, R) as HS2 (its serialized pose discarded), roots 1.. R·p0 + T, R·r0 (M1: they follow the map;
//                 HS2 leaves them in place)
//    9 blocks     MapBehaviour.Apply(options, lights); the sea one frame later (HS2's LateUpdate)
//   10 scripts    MapScripts.Start (ME_LightCurves, UVScroll, AnimatedFountainUVs)
//   11 mirrors    MapHooks.Mirrors (package B); the handle is disposed when the map is dropped
//   12 main light MainLight.Refresh (the map's lights are a source while the map is loaded)
//   13 Loaded     state Loaded, StateChanged, MapHooks.Loaded (Phase 3.3 re-applies the Studio-owned RenderSettings)
// Unload: scripts stopped, mirrors disposed, the scene unloaded asynchronously (the runtime's own scene active
// again); when the unload has completed the owned copies are destroyed, probes re-tetrahedralized, the main light
// refreshed, MapHooks.Unloaded raised, the bundle unloaded (unless another load still uses it) and, once no other change
// is pending, Resources.UnloadUnusedAssets (M15: as HS2; it frees the lightmaps).
//
// Play mode or a player only: in Editor edit mode the state becomes Unsupported (`map.editMode`). The load is a job
// advanced by Apply() once per frame. Scene loads cannot be cancelled: a request that supersedes a load in flight
// makes that load stale, and a stale scene is unloaded as soon as it arrives (its completion callback). Roots stay
// in the map scene (never reparented), so unloading it destroys them.
//
// As a part of a SceneRuntime (ISceneRuntimePart) it follows `scene.map` (`ref`, `pos`, `rot`, `options`, `lights`) of the
// session's model: a change batch touching `map` marks it, the next Apply reads the model. Standalone (tools, tests) the
// caller uses SetTarget / SetFlags / Apply. The map scripts step once per frame in LateApply (Time.deltaTime) unless the
// runtime steps manually (ManualStepping: the caller uses StepScripts).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace HS2Next.Scenes.Runtime
{
    public enum MapState
    {
        /// <summary>No map requested (`map.ref` null).</summary>
        None,
        Loading,
        Loaded,
        /// <summary>The row does not exist (`map.missing`).</summary>
        Missing,
        /// <summary>Editor edit mode (`map.editMode`).</summary>
        Unsupported,
        /// <summary>Bundle or scene could not be loaded (`map.loadFailed`, `map.noScene`).</summary>
        Failed,
    }

    public sealed class MapRealization : ISceneRuntimePart
    {
        SceneRuntime runtime;
        bool modelDirty;
        ContentCatalog catalog;
        StudioDb db;
        SceneFindings findings;
        string hs2Root;

        // target (what the model asks for)
        string targetRef;
        Vector3 targetPos, targetRot;
        bool targetOption = true, targetLight = true;
        bool refDirty, poseDirty, flagsDirty;

        // current map
        Vector3[] rootPos0;
        Quaternion[] rootRot0;
        GameObject[] roots = Array.Empty<GameObject>();
        Scene ownScene;
        IDisposable mirrors;
        bool seaPending, audioPending;
        int seaFrame;
        bool probesPending;
        float probesStart;
        int scriptFrame = -1;
        bool staticBatchReported;
        int probeFrame = -1;

        sealed class LoadJob
        {
            public AsyncOperation Op;
            public Scene Scene;
            public string Path;
            public AssetBundle Bundle;
            public StudioMap Row;
            public int LightmapsBefore;
        }

        sealed class PendingUnload { public AsyncOperation Op; public AssetBundle Bundle; public MapMaterials Materials; }

        // The bundle owner of each map load (the scene bundle and the dependencies opened for it), released when that scene
        // bundle is released.
        readonly Dictionary<AssetBundle, BundleOwner> loadOwners = new Dictionary<AssetBundle, BundleOwner>();

        // A zipmod map bundle opening asynchronously (its LZMA or deflated data decompress off the main thread); the scene load
        // starts when it is open. Opens of an earlier request finish in staleOpens and are released then.
        sealed class Opening
        {
            public ContentCatalog.BundleOpen Open;
            public StudioMap Row;
            public string MapRef;
            public BundleOwner Owner;
        }
        Opening opening;
        readonly List<Opening> staleOpens = new List<Opening>();

        LoadJob job;                                                    // the load of the current request
        readonly List<LoadJob> stale = new List<LoadJob>();             // superseded loads still in flight
        readonly List<PendingUnload> unloading = new List<PendingUnload>();
        readonly Stopwatch loadWatch = new Stopwatch();
        readonly List<KeyValuePair<string, long>> stepMs = new List<KeyValuePair<string, long>>();

        /// <summary>Seconds the post-load step waits for the probes' tetrahedralization (finding map.probesTimeout).</summary>
        public const float ProbesTimeoutSec = 2f;

        /// <summary>A part for <see cref="SceneRuntime.AddPart"/> (catalog, lists and findings come from the runtime).</summary>
        public MapRealization() { }

        /// <summary>A standalone map loader (tools, tests): the caller drives it with <see cref="SetTarget"/> and <see cref="Apply"/>.</summary>
        public MapRealization(ContentCatalog catalog, StudioDb db, SceneFindings findings, string hs2Root = null)
        {
            this.catalog = catalog;
            this.db = db;
            this.findings = findings;
            this.hs2Root = hs2Root;
        }

        // ------------------------------------------------------------------ ISceneRuntimePart

        public void Attach(SceneRuntime r)
        {
            runtime = r;
            catalog = r.Services.Catalog;
            db = r.Services.Db;
            hs2Root = r.Services.Hs2Root;
            findings = r.Findings;
            if (r.Root) ownScene = r.Root.scene;
            modelDirty = true;
        }

        public void OnChanged(SceneChangeBatch b)
        {
            if (b.Touches(null, "map")) modelDirty = true;
        }

        void ISceneRuntimePart.Apply()
        {
            if (modelDirty && runtime != null)
            {
                modelDirty = false;
                var m = runtime.Model.Settings.Map;
                SetTarget(m.Ref, m.Pos, m.Rot);
                SetFlags(m.Options, m.Lights);
            }
            Apply();
        }

        public void LateApply()
        {
            if (State != MapState.Loaded || !Scripts.Running) return;
            if (runtime != null && runtime.Settings.ManualStepping) return;
            if (!Application.isPlaying || Time.frameCount == scriptFrame) return;
            scriptFrame = Time.frameCount;
            StepScripts(runtime != null ? runtime.LateScriptDt : Time.deltaTime);   // the scene clock (Phase 3.6 K)
        }

        void ISceneRuntimePart.Clear() => Clear();

        public void OnLoadedRealized() => modelDirty = true;

        // ------------------------------------------------------------------ state

        public MapState State { get; private set; } = MapState.None;
        /// <summary>The `map.ref` the current state belongs to.</summary>
        public string Ref { get; private set; }
        /// <summary>The map row of the current request (null for None and Missing).</summary>
        public StudioMap Row { get; private set; }
        public AssetBundle Bundle { get; private set; }
        /// <summary>The loaded map scene (default when none).</summary>
        public Scene Scene { get; private set; }
        public string ScenePath { get; private set; }
        /// <summary>The map scene's root objects (in the map scene; never reparented).</summary>
        public IReadOnlyList<GameObject> Roots => roots;
        /// <summary>The structure of the loaded map (null when none is loaded).</summary>
        public MapStructure Structure { get; private set; }
        /// <summary>The owned material copies of the loaded map (released after its unload; null when none is loaded).</summary>
        public MapMaterials Materials { get; private set; }
        /// <summary>Option / light blocks and the sea.</summary>
        public MapBehaviour Behaviour { get; } = new MapBehaviour();
        /// <summary>The hull probe for renderers outside the map's light probes (Unity 6 gives them the ambient probe).</summary>
        public MapProbeFallback ProbeFallback { get; } = new MapProbeFallback();
        /// <summary>The natively reproduced map scripts.</summary>
        public MapScripts Scripts { get; } = new MapScripts();
        public MapLighting.Result Lighting { get; private set; }
        public MapLightmaps.Result Lightmaps { get; private set; }
        public MapAudio.Result Audio { get; private set; }
        /// <summary>Package B's mirror handle is attached.</summary>
        public bool MirrorsAttached => mirrors != null;
        /// <summary>The probes' tetrahedralization after the last load / unload has completed (or timed out).</summary>
        public bool ProbesTetrahedralized { get; private set; } = true;
        /// <summary>Milliseconds per post-load step of the last load, in pipeline order.</summary>
        public IReadOnlyList<KeyValuePair<string, long>> StepMs => stepMs;
        /// <summary>The flags last applied (scene.map.options / lights).</summary>
        public bool OptionFlag => targetOption;
        public bool LightFlag => targetLight;
        /// <summary>The pose last asked for.</summary>
        public Vector3 Pos => targetPos;
        public Vector3 Rot => targetRot;
        /// <summary>Cameras and audio listeners of the map scene that were enabled and got disabled at load.</summary>
        public int DisabledCameras { get; private set; }
        public int DisabledListeners { get; private set; }
        /// <summary>Map lights that got URP's UniversalAdditionalLightData (defaults) at the load.</summary>
        public int AddedLightData => Lighting.AddedLightData;
        /// <summary>Materials changed by ShaderMap at load (fitted or URP/Lit fallback).</summary>
        public int MappedMaterials { get; private set; }
        /// <summary>Renderers of the map that are static-batched (they cannot follow the map pose).</summary>
        public int StaticBatchedRenderers { get; private set; }
        /// <summary>Milliseconds from starting the load to Loaded (bundle open, scene load, mapping); and the bundle open part.</summary>
        public long LoadMs { get; private set; }
        public long BundleMs { get; private set; }
        /// <summary>The findings count when the current change started.</summary>
        public int FindingsMark { get; private set; }
        /// <summary>The map findings of the current change (the session's SceneFindings merge repeated codes into one entry with a
        /// count, so a second map's findings would not show there as new entries).</summary>
        public IReadOnlyList<SceneFinding> MapFindingsOfLoad => loadFindings;
        /// <summary>The PPv2 volumes of the loaded map's scene as read from its bundle (null before the first load's findings).</summary>
        public MapPpv2Info Ppv2 { get; private set; }
        readonly List<SceneFinding> loadFindings = new List<SceneFinding>();

        void AddFinding(string code, string message)
        {
            MapFindings.Add(findings, code, message);
            loadFindings.Add(new SceneFinding { Severity = MapFindings.Level(code), Code = code, Message = message ?? "" });
        }
        /// <summary>The findings the part writes to (the runtime's or the standalone caller's).</summary>
        public SceneFindings Findings => findings;
        /// <summary>Increments with every change of the requested map.</summary>
        public int Generation { get; private set; }
        /// <summary>Superseded loads still in flight plus scene unloads still running.</summary>
        public int PendingUnloads => unloading.Count + stale.Count;
        /// <summary>True while a requested map is not settled yet (a change not started, Loading, or the probes of the
        /// loaded map not tetrahedralized yet).</summary>
        public bool IsBusy => refDirty || modelDirty && runtime != null || State == MapState.Loading || probesPending;

        /// <summary>Map bundle opens still running for requests that were replaced (their bundles are released when done).</summary>
        public int PendingStaleOpens => staleOpens.Count;

        /// <summary>Raised when <see cref="State"/> changes (for Loaded after the whole post-load pipeline).</summary>
        public event Action<MapRealization> StateChanged;

        /// <summary>Asks for a map (null = none) and its pose; the work happens in <see cref="Apply"/>.</summary>
        public void SetTarget(string mapRef, Vec3 pos, Vec3 rot)
        {
            if (!string.Equals(mapRef, targetRef, StringComparison.Ordinal) || State == MapState.None && mapRef != null)
            {
                targetRef = mapRef;
                refDirty = true;
            }
            var p = ToVector(pos);
            var r = ToVector(rot);
            if (!p.Equals(targetPos) || !r.Equals(targetRot)) { targetPos = p; targetRot = r; poseDirty = true; }
        }

        /// <summary>Asks for the option and light flags (scene.map.options / lights); applied in <see cref="Apply"/>.</summary>
        public void SetFlags(bool option, bool light)
        {
            if (option == targetOption && light == targetLight) return;
            targetOption = option;
            targetLight = light;
            flagsDirty = true;
        }

        /// <summary>Waits (one frame at a time) until the requested map is loaded or has failed. The scene runtime's
        /// driver (or a caller of <see cref="Apply"/>) must keep advancing the job.</summary>
        public async Awaitable WaitLoaded()
        {
            while (IsBusy) await Awaitable.NextFrameAsync();
        }

        /// <summary>Advances the job: starts a requested change, finishes a load, re-applies the pose and the flags, the sea
        /// one frame after the load, the probes' wait.</summary>
        public void Apply()
        {
            if (refDirty) StartChange();
            if (opening != null || staleOpens.Count > 0) PumpOpening();
            if (job != null && job.Op.isDone) FinishLoad();
            if (State != MapState.Loaded) return;
            if (poseDirty) ApplyPose();
            if (flagsDirty) ApplyFlags();
            if (seaPending && (!Application.isPlaying || Time.frameCount > seaFrame)) ApplySea();
            if (ProbeFallback.Active && Time.frameCount != probeFrame)
            {
                probeFrame = Time.frameCount;
                try { ProbeFallback.Update(); }
                catch (Exception e) { Debug.LogWarning("map: the probe fallback threw: " + e.Message); }
            }
            if (audioPending && (!Application.isPlaying || Time.frameCount > seaFrame + 1)) CheckAudio();
            if (probesPending && Time.realtimeSinceStartup - probesStart > ProbesTimeoutSec)
            {
                probesPending = false;
                ProbesTetrahedralized = false;
                AddFinding("map.probesTimeout", $"map {Ref}: light probe tetrahedralization not finished within {ProbesTimeoutSec} s");
            }
        }

        /// <summary>Steps the map scripts by <paramref name="dt"/> (the runtime's frame; tests with ManualStepping).</summary>
        public void StepScripts(float dt)
        {
            if (State != MapState.Loaded) return;
            try { Scripts.Step(dt); }
            catch (Exception e)
            {
                Scripts.Stop();
                AddFinding("map.stepFailed", $"map {Ref}: step scripts threw {e.GetType().Name}: {e.Message}");
                Debug.LogWarning("map: stepping the map scripts threw: " + e);
            }
        }

        /// <summary>Unloads the map (asynchronously) and forgets the request; findings are kept.</summary>
        public void Clear()
        {
            Generation++;
            targetRef = null;
            refDirty = poseDirty = flagsDirty = false;
            targetPos = targetRot = Vector3.zero;
            DropCurrent();
            SetState(MapState.None, null, null);
        }

        // ------------------------------------------------------------------ change

        void StartChange()
        {
            refDirty = false;
            Generation++;
            DropCurrent();
            FindingsMark = findings?.Count ?? 0;
            loadFindings.Clear();
            string mapRef = targetRef;
            if (mapRef == null) { SetState(MapState.None, null, null); return; }

            if (!ContentId.TryParse(mapRef, out var id) || db == null || !db.TryGetMap(id, out var row))
            {
                AddFinding("map.missing", $"map {mapRef}: no Studio map row");
                SetState(MapState.Missing, mapRef, null);
                return;
            }
            if (!Application.isPlaying)
            {
                AddFinding("map.editMode", $"map {mapRef}: maps load only in play mode or a player");
                SetState(MapState.Unsupported, mapRef, row);
                return;
            }
            MapAudio.EnsureLink(hs2Root);
            // without a graphics device (-nographics batch runs: the corpus) Unity crashes natively rendering a realtime
            // reflection probe's cubemap in the frame the scene arrives (Camera::StandaloneRenderToCubemap); nothing
            // renders there anyway
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null && QualitySettings.realtimeReflectionProbes)
                QualitySettings.realtimeReflectionProbes = false;

            loadWatch.Restart();
            AssetBundle bundle = null;
            var owner = catalog?.NewOwner("map");
            if (catalog != null && !string.IsNullOrEmpty(row.BundlePath) && OpenAsynchronously)
            {
                // a zipmod map bundle opens off the main thread; the scene load goes on when it is open (PumpOpening)
                ContentCatalog.BundleOpen open = null;
                try { using (catalog.Own(owner)) open = catalog.LoadBundleAsync(row.Zipmod, row.BundlePath, row.Manifest, owner); }
                catch (Exception e) { Debug.LogWarning($"map {mapRef}: bundle {row.BundlePath} threw {e.GetType().Name}: {e.Message}"); }
                if (open != null && !open.IsDone)
                {
                    opening = new Opening { Open = open, Row = row, MapRef = mapRef, Owner = owner };
                    SetState(MapState.Loading, mapRef, row);
                    return;
                }
                bundle = open?.Bundle;
            }
            else
            {
                try { using (catalog != null ? catalog.Own(owner) : default) bundle = string.IsNullOrEmpty(row.BundlePath) ? null : catalog?.LoadBundle(row.Zipmod, row.BundlePath, row.Manifest); }
                catch (Exception e) { Debug.LogWarning($"map {mapRef}: bundle {row.BundlePath} threw {e.GetType().Name}: {e.Message}"); }
            }
            ContinueLoad(bundle, row, mapRef, owner);
        }

        /// <summary>A zipmod map bundle opens asynchronously (default true; false = the earlier synchronous open, for comparisons).</summary>
        public static bool OpenAsynchronously = true;

        // the asynchronous open of the current request done: the load goes on; done opens of replaced requests are released
        void PumpOpening()
        {
            catalog?.PumpOpens();
            for (int i = staleOpens.Count - 1; i >= 0; i--)
            {
                var s = staleOpens[i];
                if (!s.Open.IsDone) continue;
                staleOpens.RemoveAt(i);
                if (s.Open.Bundle && catalog != null && s.Open.Bundle != Bundle && (job == null || job.Bundle != s.Open.Bundle)) catalog.UnloadBundle(s.Open.Bundle);
                if (s.Owner != null) catalog?.Release(s.Owner);
            }
            if (opening == null || !opening.Open.IsDone) return;
            var o = opening;
            opening = null;
            ContinueLoad(o.Open.Bundle, o.Row, o.MapRef, o.Owner);
        }

        void ContinueLoad(AssetBundle bundle, StudioMap row, string mapRef, BundleOwner owner)
        {
            BundleMs = loadWatch.ElapsedMilliseconds;
            if (bundle && owner != null) loadOwners[bundle] = owner;
            else catalog?.Release(owner);
            if (!bundle)
            {
                AddFinding("map.loadFailed", $"map {mapRef}: bundle {(string.IsNullOrEmpty(row.BundlePath) ? "(empty bundle column)" : row.BundlePath)} could not be opened");
                SetState(MapState.Failed, mapRef, row);
                return;
            }
            string path = PickScene(bundle.GetAllScenePaths(), row.FileName);
            if (path == null)
            {
                AddFinding("map.noScene", $"map {mapRef}: bundle {row.BundlePath} holds no scene");
                ReleaseBundle(bundle);
                SetState(MapState.Failed, mapRef, row);
                return;
            }

            if (!ownScene.IsValid()) ownScene = SceneManager.GetActiveScene();
            int lmBefore = LightmapSettings.lightmaps.Length;
            AsyncOperation op = null;
            string error = null;
            catalog?.BeginLoadMark(bundle);   // the crash quarantine: a process that ends during the scene load names this bundle
            try { op = SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive); }
            catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
            bool byName = false;
            if (op == null)
            {
                byName = true;
                // some mod bundles list a scene path that Unity's lookup by path does not find; by name it does
                try { op = SceneManager.LoadSceneAsync(Path.GetFileNameWithoutExtension(path), LoadSceneMode.Additive); }
                catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
                if (op != null) Debug.Log($"map {mapRef}: scene {path} loaded by its name");
            }
            if (op == null)
            {
                catalog?.EndLoadMark();
                AddFinding("map.loadFailed", $"map {mapRef}: scene {path} could not be loaded{(error != null ? " (" + error + ")" : "")}");
                ReleaseBundle(bundle);
                SetState(MapState.Failed, mapRef, row);
                return;
            }
            var added = SceneManager.GetSceneAt(SceneManager.sceneCount - 1);   // the scene just added (also for repeats of one path)
            job = new LoadJob
            {
                Op = op,
                Scene = byName || string.Equals(added.path, path, StringComparison.OrdinalIgnoreCase) ? added : default,
                Path = path,
                Bundle = bundle,
                Row = row,
                LightmapsBefore = lmBefore,
            };
            SetState(MapState.Loading, mapRef, row);
        }

        /// <summary>The entry whose file name equals the row's scene name (ordinal, ignoring case), else the first; null when empty.</summary>
        public static string PickScene(string[] paths, string sceneName)
        {
            if (paths == null || paths.Length == 0) return null;
            foreach (var p in paths)
                if (string.Equals(Path.GetFileNameWithoutExtension(p), sceneName, StringComparison.OrdinalIgnoreCase)) return p;
            return paths[0];
        }

        static Scene SceneOf(LoadJob j) => j.Scene.IsValid() ? j.Scene : SceneManager.GetSceneByPath(j.Path);

        void Step(string name, Action a)
        {
            var sw = Stopwatch.StartNew();
            try { a(); }
            catch (Exception e)
            {
                AddFinding("map.stepFailed", $"map {Ref}: step {name} threw {e.GetType().Name}: {e.Message}");
                Debug.LogWarning($"map {Ref}: post-load step {name} threw: {e}");
            }
            stepMs.Add(new KeyValuePair<string, long>(name, sw.ElapsedMilliseconds));
        }

        void FinishLoad()
        {
            var j = job;
            job = null;
            catalog?.EndLoadMark();
            var scene = SceneOf(j);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                AddFinding("map.loadFailed", $"map {Ref}: scene {j.Path} did not load");
                ReleaseBundle(j.Bundle);
                SetState(MapState.Failed, Ref, j.Row);
                return;
            }

            stepMs.Clear();
            stepMs.Add(new KeyValuePair<string, long>("load", loadWatch.ElapsedMilliseconds));
            staticBatchReported = false;
            Scene = scene;
            ScenePath = j.Path;
            Bundle = j.Bundle;
            Materials = new MapMaterials();
            roots = scene.GetRootGameObjects();
            rootPos0 = new Vector3[roots.Length];
            rootRot0 = new Quaternion[roots.Length];
            for (int i = 0; i < roots.Length; i++)
            {
                rootPos0[i] = roots[i].transform.localPosition;
                rootRot0[i] = roots[i].transform.localRotation;
            }

            // 2 structure
            Step("structure", () =>
            {
                Structure = MapStructure.Build(roots);
                StaticBatchedRenderers = Structure.StaticBatched;
                Behaviour.Bind(Structure.Component);
            });
            var s = Structure ?? MapStructure.Build(Array.Empty<GameObject>());
            // 3 audio, cameras, listeners
            Step("audio", () =>
            {
                Audio = MapAudio.Silence(s.AudioSources);
                DisabledCameras = DisabledListeners = 0;
                foreach (var c in s.Cameras) if (c && c.enabled) { c.enabled = false; DisabledCameras++; }
                foreach (var l in s.Listeners) if (l && l.enabled) { l.enabled = false; DisabledListeners++; }
                // (no graphics device: see StartChange)
                if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                    foreach (var p in s.ReflectionProbes)
                        if (p && p.enabled && p.mode == UnityEngine.Rendering.ReflectionProbeMode.Realtime) p.enabled = false;
            });
            // 4 materials
            MappedMaterials = 0;
            Step("materials", () =>
            {
                MappedMaterials = Materials.Apply(roots);
                if (MapHooks.Terrains != null && s.Terrains.Count > 0) MapHooks.Terrains(this);
            });
            // 5 lighting
            Step("lighting", () => Lighting = MapLighting.Apply(roots, s.Lights));
            // 6 lightmaps and probes
            Step("lightmaps", () =>
            {
                Lightmaps = MapLightmaps.Apply(s, Materials, j.LightmapsBefore);
            });
            // 7 the map scene active, its skybox mapped
            Step("active", () =>
            {
                SceneManager.SetActiveScene(scene);
                Materials.ApplySkybox();
                Tetrahedralize(true);   // after SetActiveScene: LightmapSettings shows the map's probes only then
                ProbeFallback.Begin(scene);
            });
            // 8 pose (root 0 as HS2, the other roots follow: M1)
            Step("pose", ApplyPose);
            // 9 blocks; the sea one frame later
            Step("blocks", () =>
            {
                flagsDirty = false;
                Behaviour.Apply(targetOption, targetLight);
                seaPending = s.SeaRenderers.Count > 0;
                audioPending = s.AudioSources.Count > 0;
                seaFrame = Time.frameCount;
            });
            // 10 map scripts
            Step("scripts", () => Scripts.Start(s, Materials));
            // 11 mirrors (package B)
            Step("mirrors", () => mirrors = MapHooks.Mirrors?.Invoke(this));
            // 12 main light
            Step("mainLight", () =>
            {
                var lights = s.Lights;
                MainLight.SetSource(this, "map", () => lights);
                MainLight.Refresh();
            });
            Step("findings", () => LoadFindings(s));
            LoadMs = loadWatch.ElapsedMilliseconds;
            loadWatch.Stop();
            // 13 Loaded
            SetState(MapState.Loaded, Ref, j.Row);
            MapHooks.Raise(MapHooks.Loaded, this, "Loaded");
        }

        /// <summary>The per-load findings (info unless noted).</summary>
        void LoadFindings(MapStructure s)
        {
            if (findings == null) return;
            string m = "map " + Ref + ": ";
            if (s.ComponentRoot > 0)
                AddFinding("map.componentOutsideFirstRoot", m + $"the MapComponent is under root {s.ComponentRoot} ({roots[s.ComponentRoot].name}); HS2 finds none (it looks under root 0 only)");
            if (Behaviour.NullRefs > 0) AddFinding("map.blockNullRefs", m + $"{Behaviour.NullRefs} option / light block entries without an object");
            if (Lighting.ShadowedPointSpot > MapLighting.ManyShadowedLights)
                AddFinding("map.manyShadowedLights", m + $"{Lighting.ShadowedPointSpot} active shadowed point / spot lights (URP's shadow atlas drops what does not fit)");
            int unshadowed = 0;
            foreach (var l in s.Lights)
                if (MainLight.IsCandidate(l) && l != MainLight.Current) unshadowed++;
            if (unshadowed > 0)
                AddFinding("map.unshadowedDirectional", m + $"{unshadowed} shadowed directional map lights are not the main light and draw no shadows");
            var lm = Lightmaps;
            if (lm.PrefabDead > 0) AddFinding("map.lightmapPrefabDead", m + $"{lm.PrefabDead} of {lm.PrefabEntries} LightmapPrefab entries skipped (no renderer or index out of range)");
            if (lm.BakeryOnlyMaps > 0) AddFinding("map.bakeryOnlyLightmaps", m + $"{lm.BakeryOnlyMaps} Bakery storage lightmaps are not in LightmapSettings; the map renders without them (fallback deferred, M5)");
            if (lm.BakeryModeStorages > 0) AddFinding("map.bakeryMode", m + $"{lm.BakeryModeStorages} Bakery storages with RNM / SH modes or volumes (the L0 lightmap is used)");
            if (s.InertScripts.Count > 0)
            {
                var parts = new List<string>();
                foreach (var kv in s.InertScripts) parts.Add(kv.Key + " " + kv.Value);
                AddFinding("map.scriptsInert", m + "map scripts that do not run: " + string.Join(", ", parts));
            }
            if (s.MissingScripts > 0) AddFinding("map.missingScripts", m + $"{s.MissingScripts} components with a missing script (no stub; PPv2 volumes among them, not run: map.ppv2Ignored)");
            // PPv2 volumes are not drawn: the ones that would reach HS2's Studio camera are read from the map's bundle and reported
            Ppv2 = MapPpv2Volumes.Read(Row, ScenePath, catalog, hs2Root);
            if (Ppv2.Ok && Ppv2.Reaching > 0)
                AddFinding("map.ppv2Ignored", m + $"{Ppv2.Reaching} PPv2 post-processing volumes would reach HS2's Studio camera ({Ppv2.ReachingWeighted} with a weight above 0; " +
                    $"{Ppv2.Volumes} in the scene); HS2Next does not draw PPv2 volumes");
            int realtime = 0;
            foreach (var p in s.ReflectionProbes) if (p && p.mode == UnityEngine.Rendering.ReflectionProbeMode.Realtime) realtime++;
            if (realtime > 0) AddFinding("map.realtimeProbes", m + $"{realtime} realtime reflection probes");
            long mb = VramMB(s);
            if (mb > 1024) AddFinding("map.largeLightmaps", m + $"lightmaps and probe cubemaps take about {mb} MB");
            AddFinding("map.vram", m + $"about {mb} MB of lightmaps, reflection-probe cubemaps and mirror targets");
        }

        /// <summary>Lightmaps of this scene + its reflection probes' cubemaps + 4 MB per mirror (texture sizes at load).</summary>
        long VramMB(MapStructure s)
        {
            long bytes = 0;
            var lms = LightmapSettings.lightmaps;
            for (int i = Lightmaps.FirstIndex; i < lms.Length && i < Lightmaps.FirstIndex + Lightmaps.SceneLightmaps; i++)
            {
                var d = lms[i];
                if (d == null) continue;
                bytes += TexBytes(d.lightmapColor) + TexBytes(d.lightmapDir) + TexBytes(d.shadowMask);
            }
            var seen = new HashSet<Texture>();
            foreach (var p in s.ReflectionProbes)
                if (p && p.texture && seen.Add(p.texture)) bytes += TexBytes(p.texture);
            bytes += 4L * 1024 * 1024 * s.Mirrors.Count;
            return bytes / (1024 * 1024);
        }

        static long TexBytes(Texture t)
        {
            if (!t) return 0;
            long b = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);   // 0 in a non-development player
            return b > 0 ? b : (long)t.width * t.height * (t.dimension == UnityEngine.Rendering.TextureDimension.Cube ? 6 : 1);
        }

        void ApplyPose()
        {
            poseDirty = false;
            if (rootPos0 == null) return;
            PoseRoots(roots, rootPos0, rootRot0, targetPos, targetRot);
            bool identity = targetPos.Equals(Vector3.zero) && targetRot.Equals(Vector3.zero);
            if (StaticBatchedRenderers > 0 && !identity && !staticBatchReported)
            {
                staticBatchReported = true;   // once per load (pose drags would repeat it)
                AddFinding("map.staticBatchMoved", $"map {Ref}: {StaticBatchedRenderers} static-batched renderers cannot follow the map pose (as in HS2)");
            }
        }

        /// <summary>
        /// The map pose (M1, with package C's measurement): root 0 gets the pose itself, localPosition = T and localRotation =
        /// R (R = Euler(rot), T = pos), as HS2's MapCtrl.Reflect, which discards root 0's serialized pose on every load (159
        /// mod maps have a root 0 with a non-identity pose); every other root follows the map as one rigid body, root i at
        /// R·p0[i] + T with rotation R·r0[i] (p0 / r0 its pose in the scene file; HS2 leaves those roots where they are). At
        /// the default pose this equals HS2 for every map.
        /// </summary>
        public static void PoseRoots(IReadOnlyList<GameObject> roots, Vector3[] p0, Quaternion[] r0, Vector3 pos, Vector3 rot)
        {
            if (roots == null || p0 == null || r0 == null) return;
            var q = Quaternion.Euler(rot);
            for (int i = 0; i < roots.Count && i < p0.Length && i < r0.Length; i++)
            {
                if (!roots[i]) continue;
                if (i == 0) roots[i].transform.SetPositionAndRotation(pos, q);
                else roots[i].transform.SetPositionAndRotation(q * p0[i] + pos, q * r0[i]);
            }
        }

        void ApplyFlags()
        {
            flagsDirty = false;
            try
            {
                Behaviour.Apply(targetOption, targetLight);
                var l = Lighting;
                MapLighting.Count(Structure?.Lights, ref l);
                Lighting = l;
                MainLight.Refresh();
            }
            catch (Exception e)
            {
                AddFinding("map.stepFailed", $"map {Ref}: step blocks threw {e.GetType().Name}: {e.Message}");
                Debug.LogWarning("map: applying the option / light flags threw: " + e);
            }
        }

        /// <summary>Two frames after the load: the clips that preload their data and have not loaded it (map.envAudioUnavailable).</summary>
        void CheckAudio()
        {
            audioPending = false;
            try
            {
                var a = Audio;
                a.ClipsUnavailable = MapAudio.UnavailableClips(Structure?.AudioSources);
                Audio = a;
                if (a.ClipsUnavailable > 0)
                    AddFinding("map.envAudioUnavailable", $"map {Ref}: {a.ClipsUnavailable} audio clips could not load their data (relative resource path; link {MapAudio.LinkState})");
            }
            catch (Exception e) { Debug.LogWarning("map: the audio check threw: " + e.Message); }
        }

        void ApplySea()
        {
            seaPending = false;
            try { Behaviour.ApplySea(Structure?.SeaRenderers, Materials); }
            catch (Exception e)
            {
                AddFinding("map.stepFailed", $"map {Ref}: step sea threw {e.GetType().Name}: {e.Message}");
                Debug.LogWarning("map: the sea step threw: " + e);
            }
        }

        // ------------------------------------------------------------------ probes

        static bool probeHooked;
        static int probeCompletions;
        int probeMark;

        void Tetrahedralize(bool wait)
        {
            if (!probeHooked)
            {
                probeHooked = true;
                LightProbes.tetrahedralizationCompleted += () => probeCompletions++;
            }
            var lp = LightmapSettings.lightProbes;
            bool any = lp != null && lp.count > 0 || wait && Lightmaps.Probes > 0;   // before SetActiveScene the set shows no probes
            probeMark = probeCompletions;
            LightProbes.TetrahedralizeAsync();
            ProbesTetrahedralized = !any;
            probesPending = wait && any;
            probesStart = Time.realtimeSinceStartup;
            if (probesPending) WaitProbes();
        }

        async void WaitProbes()
        {
            int gen = Generation;
            while (probesPending && gen == Generation)
            {
                if (probeCompletions > probeMark) { probesPending = false; ProbesTetrahedralized = true; return; }
                await Awaitable.NextFrameAsync();
                if (!Application.isPlaying) return;
                if (probesPending && gen == Generation && Time.realtimeSinceStartup - probesStart > ProbesTimeoutSec)
                {
                    probesPending = false;
                    ProbesTetrahedralized = false;
                    AddFinding("map.probesTimeout", $"map {Ref}: light probe tetrahedralization not finished within {ProbesTimeoutSec} s");
                }
            }
        }

        // ------------------------------------------------------------------ unload

        /// <summary>Unloads the current map scene (async; the runtime's own scene becomes active) and makes a load in
        /// flight stale.</summary>
        void DropCurrent()
        {
            if (opening != null)
            {
                // an open cannot be cancelled: its bundle is released when it is done
                staleOpens.Add(opening);
                opening = null;
            }
            if (job != null)
            {
                // scene loads cannot be cancelled: unload the stale scene when it arrives (also after the runtime is gone)
                var j = job;
                job = null;
                catalog?.EndLoadMark();   // the next load marks its own bundle
                stale.Add(j);
                j.Op.completed += _ =>
                {
                    if (stale.Remove(j)) BeginUnload(SceneOf(j), j.Bundle, null);
                };
            }
            if (Scene.IsValid())
            {
                try { Scripts.Stop(); } catch (Exception e) { Debug.LogWarning("map: stopping the map scripts threw: " + e.Message); }
                if (mirrors != null)
                {
                    try { mirrors.Dispose(); } catch (Exception e) { Debug.LogWarning("map: disposing the mirrors threw: " + e); }
                    mirrors = null;
                }
                MainLight.RemoveSource(this);
                ProbeFallback.End();
                // URP's Forward+ reflection-probe atlas (ReflectionProbeManager) caches visible probes by component; a probe
                // destroyed by the scene unload while still in its culling results throws in UpdateGpuData (player, T5
                // after a map). Probes are disabled first so that they leave culling before the unload destroys them.
                if (Structure != null)
                    foreach (var p in Structure.ReflectionProbes)
                        if (p && p.enabled) p.enabled = false;
                var s = Scene;
                var b = Bundle;
                var mats = Materials;
                Scene = default;
                Bundle = null;
                Materials = null;
                if (SceneManager.GetActiveScene() == s) RestoreActive(s);
                MainLight.Refresh();
                BeginUnload(s, b, mats);
            }
            ScenePath = null;
            roots = Array.Empty<GameObject>();
            rootPos0 = null;
            rootRot0 = null;
            Structure = null;
            Behaviour.Unbind();
            StaticBatchedRenderers = 0;
            seaPending = audioPending = probesPending = false;
            stepMs.Clear();
        }

        void RestoreActive(Scene leaving)
        {
            Scene target = ownScene.IsValid() && ownScene.isLoaded && ownScene != leaving ? ownScene : default;
            if (!target.IsValid())
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    if (s.isLoaded && s != leaving) { target = s; break; }
                }
            if (target.IsValid()) SceneManager.SetActiveScene(target);
        }

        void BeginUnload(Scene s, AssetBundle bundle, MapMaterials mats)
        {
            AsyncOperation op = null;
            if (s.IsValid() && s.isLoaded)
            {
                if (SceneManager.GetActiveScene() == s) RestoreActive(s);
                try { op = SceneManager.UnloadSceneAsync(s); }
                catch (Exception e) { Debug.LogWarning($"map: unloading {s.path} threw {e.GetType().Name}: {e.Message}"); }
            }
            if (op == null)
            {
                mats?.Release();
                ReleaseBundle(bundle);
                return;
            }
            var u = new PendingUnload { Op = op, Bundle = bundle, Materials = mats };
            unloading.Add(u);
            op.completed += _ =>
            {
                unloading.Remove(u);
                UnloadCompleted(u);
            };
        }

        void UnloadCompleted(PendingUnload u)
        {
            try { u.Materials?.Release(); } catch (Exception e) { Debug.LogWarning("map: releasing the map materials threw: " + e); }
            ReleaseBundle(u.Bundle);
            if (u.Materials == null) return;   // a stale load: nothing of it was realized
            try
            {
                LightProbes.TetrahedralizeAsync();
                MainLight.Refresh();
            }
            catch (Exception e) { Debug.LogWarning("map: probes / main light after the unload threw: " + e.Message); }
            MapHooks.Raise(MapHooks.Unloaded, this, "Unloaded");
            // M15: once per settled change (no other unload or load in flight)
            if (unloading.Count == 0 && stale.Count == 0 && job == null)
            {
                if (catalog != null) catalog.ReleaseUnused();   // counted with the catalog's own calls
                else Resources.UnloadUnusedAssets();
            }
        }

        /// <summary>Unloads <paramref name="bundle"/> through the catalog unless the current map, a load in flight or a
        /// running unload still uses it (that one releases it later).</summary>
        void ReleaseBundle(AssetBundle bundle)
        {
            if (!bundle) return;
            if (bundle == Bundle || job != null && job.Bundle == bundle) return;
            foreach (var j in stale) if (j.Bundle == bundle) return;
            foreach (var u in unloading) if (u.Bundle == bundle) return;
            loadOwners.TryGetValue(bundle, out var owner);
            loadOwners.Remove(bundle);
            if (catalog != null) catalog.UnloadBundle(bundle);
            else bundle.Unload(true);
            if (owner != null && catalog != null) { catalog.Release(owner); catalog.NoteRealizationDestroyed(); }
        }

        void SetState(MapState s, string mapRef, StudioMap row)
        {
            Ref = mapRef;
            Row = row;
            State = s;
            StateChanged?.Invoke(this);
        }

        static Vector3 ToVector(Vec3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}
