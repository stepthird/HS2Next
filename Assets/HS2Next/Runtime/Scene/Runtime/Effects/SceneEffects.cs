// The screen-effect part of a scene runtime (Phase 3.3 package B).
// Source of truth: `scene.effects` (12 blocks, 45 values); the part never writes the model. It routes changes by field
// path (`effects.<block>.*` marks that block; a whole-scene change, Clear, OnLoadedRealized and the map hooks mark all)
// and applies the dirty blocks in HS2's order in Apply (before rendering, so effects.set then capture in one frame shows
// the new values). Blocks 11–12 (environment colours, sky) are package S's SceneSky; this part routes 1–10:
//    1 colour grading   ColorGradingBake (CPU strip, BaseGrade from the user's HS2) → EffectVolume ColorLookup
//    2 AO               AmbientOcclusionControl (URP SSAO feature of the renderer; colour not black → effects.aoColorIgnored)
//    3 bloom            BloomMapping → EffectVolume Bloom
//    4 DOF              stored; focus point resolved every LateApply → Dof (package C's HS2DofFeature reads it)
//    5 vignette         EffectVolume Vignette (black, centre, smoothness 0.15, not rounded; intensity)
//    6 SSR              SsrControl drives the template profile's SSR override: drawn only with render.ssr on and the
//                       block on (off by default: effects.ssrNotDrawn; no SSR in the build: effects.ssrUnavailable)
//    7 reflection probe CameraProbe (under the view camera; unknown ref → effects.probeMissing)
//    8 fog              StudioFog (colour / density written, RenderSettings.fog off, the inputs of HS2FogFeature: a post
//                       pass on opaque pixels and the fitted transparent shaders' own term; URP-native transparent
//                       slots stay unfogged → fog.urpNativeTransparent)
//    9 sun shafts       caster resolved every LateApply → the inputs of HS2SunShaftsFeature (drawn after post-processing)
//   10 self shadow      ViewSetup.RenderShadows (view and captures)
// Focus / caster binding (Q-c not reproduced, E16): any node; the point is the node holder's world position, a
// character's the midpoint of cf_J_Eye_t_L / _R (else its head bone, else its holder); an id that names no node acts as
// none (DOF: the camera pivot; shafts: the screen centre) and the finding scene.unknownNodeRef stays (FieldSchema's).
// Runtime overrides (Phase 6.2): a winning `effects.depthOfField` slot of the override layer is used instead of the block
// every LateApply, with its focus forms (the camera pivot as shown, a node's point, a transform under a node plus a world
// offset, a world point, a distance along the camera's forward axis; a second focus blended with the first) and its blur
// (the largest radius on a 1080-line frame -> HS2DofFeature.Input.MaxBlur1080; 0 = the renderer's own). A focus that does
// not resolve acts as the pivot (DofOverrideResolved false). The camera pivot is the winning `camera` slot's target while
// one wins.
// Map hooks: Loaded and Unloaded re-apply every block at once (the map scene, or the runtime's scene, is active, so the
// RenderSettings writes land in it). Render option `grading` (hs2 | hdr): hs2 = LDR grading, Tonemapping None, the baked
// strip; hdr = the pipeline asset's grading mode switched to HDR at run time (restored when the part is disposed), ACES,
// the sliders through ColorAdjustments, no base grade.
using System;
using System.Collections.Generic;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes.Overrides;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>The DOF block as package C's pass needs it (SceneEffects.Dof, refreshed every LateApply).</summary>
    public struct DofInputs
    {
        /// <summary>enabled and aperture > 0.</summary>
        public bool Active;
        public bool Enabled;
        /// <summary>World position of the focus (the node's point, or the camera pivot).</summary>
        public Vector3 FocusPoint;
        public float FocalSize, Aperture;
        /// <summary>The view's pixel height (size-dependent radii scale by frame height / ReferenceHeight).</summary>
        public int ReferenceHeight;
        /// <summary>The focus node id (null = the pivot) and how the point was found ("pivot", "node", "eyes", "head", "missing").</summary>
        public string FocusKey, FocusKind;
        /// <summary>CoC slope A = 1 / (1 − aperture) − 1.</summary>
        public float A => Aperture >= 1f ? float.PositiveInfinity : 1f / (1f - Aperture) - 1f;
        /// <summary>The largest blur radius on a 1080-line frame from a runtime override; 0 = the renderer's own.</summary>
        public float MaxBlur1080;
        /// <summary>The owner of the winning depth-of-field slot whose value is shown, or null (the model block).</summary>
        public string Overridden;
    }

    public sealed class SceneEffects : ISceneRuntimePart, IDisposable
    {
        /// <summary>HS2's block order (SystemButtonCtrl), as `scene.effects` member names.</summary>
        public static readonly string[] BlockNames =
            { "colorGrading", "ambientOcclusion", "bloom", "depthOfField", "vignette", "ssr", "reflectionProbe", "fog", "sunShafts", "shadows", "ambient", "sky" };
        /// <summary>Blocks 1–10 are routed here; 11–12 are SceneSky's (package S).</summary>
        public const int RoutedBlocks = 10;

        public static int BlockIndex(string name) => Array.IndexOf(BlockNames, name);

        SceneRuntime runtime;
        readonly bool[] dirty = new bool[BlockNames.Length];
        bool hooked, disposed;
        EffectVolume volume;
        readonly CameraProbe probe = new CameraProbe();
        readonly AmbientOcclusionControl ao = new AmbientOcclusionControl();
        Texture2D strip;
        float[] stripData;
        string bakeKey;
        UniversalRenderPipelineAsset gradingAsset;
        ColorGradingMode? gradingModeBefore;

        /// <summary>Blocks applied by the last Apply, in order (EffectsTest EF1).</summary>
        public readonly List<string> LastApplied = new List<string>();
        /// <summary>Count of block applications since the part was attached (tests).</summary>
        public int AppliedCount { get; private set; }

        public EffectVolume Volume => volume;
        public CameraProbe Probe => probe;
        public AmbientOcclusionControl AmbientOcclusion => ao;
        /// <summary>The baked strip in use (null in the hdr grading mode).</summary>
        public Texture2D Strip => strip;
        public float[] StripData => stripData;
        public string StripHash { get; private set; }
        public double LastBakeMs { get; private set; }
        public BloomParams Bloom { get; private set; }
        /// <summary>The grading mode in effect ("hs2" or "hdr").</summary>
        public string GradingMode { get; private set; } = "hs2";
        /// <summary>The LUT ref resolved for the strip and whether its texture loaded.</summary>
        public string LutRef { get; private set; }
        public bool LutFound { get; private set; }
        public string ProbeRef { get; private set; }
        public DofInputs Dof { get; private set; }
        /// <summary>The DOF inputs of the most recently updated runtime (package C's HS2DofFeature reads this).</summary>
        public static DofInputs CurrentDof { get; private set; }
        public string CasterKey { get; private set; }
        /// <summary>World position of the sun-shaft caster, null = none (the screen centre).</summary>
        public Vector3? CasterPoint { get; private set; }
        public string CasterKind { get; private set; }

        /// <summary>Tests replace the base grade (null = read from the install).</summary>
        public BaseGrade BaseGradeOverride;

        // ------------------------------------------------------------ base grade and strips (per process)

        static BaseGrade baseGrade;
        static string baseGradeRoot;
        static bool baseGradeWarned;
        static readonly Dictionary<string, float[]> strips = new Dictionary<string, float[]>(StringComparer.Ordinal);
        static readonly HashSet<string> stripsMissing = new HashSet<string>(StringComparer.Ordinal);
        static readonly Dictionary<string, Texture> cubemaps = new Dictionary<string, Texture>(StringComparer.Ordinal);

        /// <summary>The base grade of an install (read once per root; never throws).</summary>
        public static BaseGrade BaseGradeOf(string hs2Root)
        {
            if (baseGrade != null && string.Equals(baseGradeRoot, hs2Root, StringComparison.OrdinalIgnoreCase)) return baseGrade;
            baseGrade = BaseGrade.Read(hs2Root);
            baseGradeRoot = hs2Root;
            baseGrade.WriteRecord(hs2Root, HS2Next.Chara.TextureGuard.CacheRoot);
            return baseGrade;
        }

        /// <summary>Forgets the cached base grade and strips (tests).</summary>
        public static void ResetCaches()
        {
            baseGrade = null;
            baseGradeRoot = null;
            baseGradeWarned = false;
            strips.Clear();
            stripsMissing.Clear();
            cubemaps.Clear();
        }

        BaseGrade CurrentBaseGrade => BaseGradeOverride ?? BaseGradeOf(runtime?.Services?.Hs2Root);

        /// <summary>The strip of a Filter row (null = not found / not readable); cached per ref.</summary>
        public static float[] FilterStrip(RuntimeServices s, string lutRef, out bool found)
        {
            found = false;
            if (string.IsNullOrEmpty(lutRef) || s?.Db == null || s.Catalog == null) return null;
            if (strips.TryGetValue(lutRef, out var c)) { found = true; return c; }
            if (stripsMissing.Contains(lutRef)) return null;
            float[] data = null;
            if (ContentId.TryParse(lutRef, out var id) && s.Db.TryGetFilter(id, out var row) && row != null)
            {
                var tex = s.Catalog.LoadAsset<Texture2D>(row);
                data = ColorGradingBake.ReadStrip(tex);
            }
            if (data == null)
            {
                if (ColorGradingBake.CanReadStrips) stripsMissing.Add(lutRef);
                return null;
            }
            strips[lutRef] = data;
            found = true;
            return data;
        }

        static Texture ProbeCubemap(RuntimeServices s, string probeRef)
        {
            if (string.IsNullOrEmpty(probeRef) || s?.Db == null || s.Catalog == null) return null;
            if (cubemaps.TryGetValue(probeRef, out var t) && t) return t;
            Texture tex = null;
            if (ContentId.TryParse(probeRef, out var id) && s.Db.TryGetProbe(id, out var row) && row != null)
                tex = s.Catalog.LoadAsset<Texture>(row);
            if (tex) cubemaps[probeRef] = tex;
            return tex;
        }

        // ------------------------------------------------------------ ISceneRuntimePart

        public void Attach(SceneRuntime r)
        {
            runtime = r;
            if (volume == null) volume = new EffectVolume(r.ViewRoot ? r.ViewRoot : r.Root.transform);
            if (!hooked)
            {
                MapHooks.Loaded += OnMapLoaded;
                MapHooks.Unloaded += OnMapUnloaded;
                RenderOptions.Changed += OnRenderOptions;
                hooked = true;
            }
            MarkAll();
        }

        public void OnChanged(SceneChangeBatch b)
        {
            foreach (var c in b.Changes)
            {
                if (c.Kind != SceneChangeKind.SceneChanged) continue;
                if (c.Path == null) { MarkAll(); continue; }
                if (!FieldPath.Overlaps("effects", c.Path)) continue;
                string block = SecondSegment(c.Path);
                int i = block != null ? BlockIndex(block) : -1;
                if (i < 0) MarkAll(); else dirty[i] = true;
            }
        }

        /// <summary>The block name of an `effects.<block>...` path (null for `effects` itself).</summary>
        public static string SecondSegment(string path)
        {
            if (path == null || !path.StartsWith("effects.", StringComparison.Ordinal)) return null;
            int s = 8, e = s;
            while (e < path.Length && path[e] != '.' && path[e] != '[') e++;
            return e > s ? path.Substring(s, e - s) : null;
        }

        void MarkAll() { for (int i = 0; i < dirty.Length; i++) dirty[i] = true; }

        /// <summary>The blocks marked dirty (tests).</summary>
        public IEnumerable<string> DirtyBlocks()
        {
            for (int i = 0; i < dirty.Length; i++) if (dirty[i]) yield return BlockNames[i];
        }

        public void Apply()
        {
            if (runtime == null || disposed) return;
            bool any = false;
            for (int i = 0; i < RoutedBlocks; i++) any |= dirty[i];
            // blocks 11–12 belong to SceneSky (package S): their dirty flags are dropped here
            for (int i = RoutedBlocks; i < dirty.Length; i++) dirty[i] = false;
            if (LastApplied.Count > 0) LastApplied.Clear();
            if (!any) return;
            var fx = runtime.Model.Settings.Effects;
            for (int i = 0; i < RoutedBlocks; i++)
            {
                if (!dirty[i]) continue;
                dirty[i] = false;
                try { ApplyBlock(i, fx); }
                catch (Exception e)
                {
                    runtime.ReportOnce(FindingSeverity.Error, "effects.applyFailed", null, BlockNames[i] + ": " + e.GetType().Name + ": " + e.Message);
                    Debug.LogWarning("effects: applying " + BlockNames[i] + " threw: " + e);
                }
                LastApplied.Add(BlockNames[i]);
                AppliedCount++;
            }
        }

        /// <summary>Applies every routed block now, in HS2's order (map hooks, tests).</summary>
        public void ApplyAll()
        {
            MarkAll();
            Apply();
        }

        void ApplyBlock(int i, ScreenEffects fx)
        {
            switch (i)
            {
                case 0: ApplyGrading(fx.ColorGrading); break;
                case 1: ApplyAo(fx.AmbientOcclusion); break;
                case 2: ApplyBloom(fx.Bloom); break;
                case 3: ResolveDof(fx.DepthOfField); break;
                case 4:
                    volume.Vignette.active = true;
                    volume.Vignette.intensity.Override(fx.Vignette.Enabled ? Mathf.Clamp01(fx.Vignette.Intensity) : 0f);
                    break;
                case 5:
                {
                    var ssr = SsrControl.Apply(volume.Profile, fx.Ssr.Enabled);
                    if (fx.Ssr.Enabled && ssr.Reason == "option-off")
                        runtime.ReportOnce(FindingSeverity.Info, "effects.ssrNotDrawn", null, "SSR is off in HS2Next by default (render.ssr)");
                    if (ssr.Reason == "define-missing" && RenderOptions.Ssr == "on")
                        runtime.ReportOnce(FindingSeverity.Warning, "effects.ssrUnavailable", null, "render.ssr is on but this build has no screen-space reflections");
                    break;
                }
                case 6: ApplyProbe(fx.ReflectionProbe); break;
                case 7:
                    StudioFog.Apply(fx.Fog);
                    if (fx.Fog.Enabled)
                    {
                        int n = UrpNativeTransparentSlots();
                        if (n > 0) runtime.ReportOnce(FindingSeverity.Info, "fog.urpNativeTransparent", null, n + " material slots use a URP shader in the transparent queues: they are drawn without the height fog");
                    }
                    break;
                case 8:
                    ResolveCaster(fx.SunShafts);
                    PushShafts(fx.SunShafts, CasterPoint);
                    break;
                case 9:
                    var cam = runtime.GetPart<SceneViewCamera>();
                    if (cam != null) cam.Setup.RenderShadows = fx.Shadows.Enabled;
                    break;
            }
        }

        // ------------------------------------------------------------ 1 colour grading

        /// <summary>
        /// True when the active pipeline's first renderer runs Volume post-processing (a UniversalRendererData with URP's
        /// PostProcessData; without it no grading, bloom or vignette is drawn, renderer features such as SSAO still run).
        /// </summary>
        public static bool RendererPostProcessing
        {
            get
            {
                var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (!asset || asset.rendererDataList.Length == 0) return false;
                var rd = asset.rendererDataList[0];
                return !(rd is UniversalRendererData u) || u.postProcessData;
            }
        }

        void ApplyGrading(ColorGradingEffect g)
        {
            if (!RendererPostProcessing && GraphicsSettings.currentRenderPipeline)
                runtime.ReportOnce(FindingSeverity.Warning, "effects.postProcessingOff", null, "the URP renderer has no post-processing data: grading, bloom and vignette are not drawn");
            string mode = RenderOptions.Grading == "hdr" ? "hdr" : "hs2";
            SetPipelineGradingMode(mode);
            GradingMode = mode;
            if (mode == "hdr")
            {
                volume.Tonemap.mode.Override(TonemappingMode.ACES);
                volume.Adjustments.saturation.Override(Mathf.Clamp(g.Saturation, -100, 100));
                volume.Adjustments.contrast.Override(Mathf.Clamp(g.Contrast, -100, 100));
                volume.Adjustments.postExposure.Override(Mathf.Log((g.Brightness + 100f) / 100f, 2f));
                volume.Lookup.contribution.Override(0f);
                LutRef = g.Lut;
                LutFound = true;
                StripHash = null;
                return;
            }
            volume.Tonemap.mode.Override(TonemappingMode.None);
            volume.SetNeutralAdjustments();

            var bg = CurrentBaseGrade;
            if (!bg.Ok && !baseGradeWarned)
            {
                baseGradeWarned = true;
                runtime.Report(FindingSeverity.Warning, "effects.baseGradeUnavailable", null, "HS2's base grade could not be read (" + bg.Error + "): neutral grade");
            }
            var s = runtime.Services;
            string defaultRef = DocDefaults.NoneRef;
            float[] lutDefault = bg.Ok ? FilterStrip(s, defaultRef, out _) : null;
            string lut = string.IsNullOrEmpty(g.Lut) ? defaultRef : g.Lut;
            float[] filter = null;
            bool found = true;
            if (lut != defaultRef)
            {
                filter = FilterStrip(s, lut, out found);
                if (!found && ColorGradingBake.CanReadStrips)
                    runtime.ReportOnce(FindingSeverity.Warning, "effects.lutMissing", null, "the Filter row " + lut + " was not found: no filter (Lut_Default)");
            }
            LutRef = lut;
            LutFound = found;
            float blend = Mathf.Clamp01(g.Blend);
            string key = (bg.Ok ? "hs2" : "neutral") + "|" + lut + "|" + (found ? 1 : 0) + "|" + (lutDefault != null ? 1 : 0) + "|" + blend.ToString("R") + "|" + g.Saturation + "|" + g.Brightness + "|" + g.Contrast;
            if (key != bakeKey || !strip)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var user = ColorGradingBake.UserLut(lutDefault, filter ?? lutDefault, blend);
                stripData = ColorGradingBake.Bake(user, GradeInputs.Of(g.Saturation, g.Brightness, g.Contrast), bg.Resolve(blend));
                strip = ColorGradingBake.ToTexture(stripData, strip);
                LastBakeMs = sw.Elapsed.TotalMilliseconds;
                StripHash = ColorGradingBake.Hash(stripData);
                bakeKey = key;
            }
            volume.Lookup.texture.Override(strip);
            volume.Lookup.contribution.Override(1f);
        }

        void SetPipelineGradingMode(string mode)
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!asset) return;
            var want = mode == "hdr" ? ColorGradingMode.HighDynamicRange : ColorGradingMode.LowDynamicRange;
            if (asset.colorGradingMode == want) return;
            if (gradingAsset != asset) { RestorePipelineGradingMode(); gradingAsset = asset; gradingModeBefore = asset.colorGradingMode; }
            asset.colorGradingMode = want;
        }

        void RestorePipelineGradingMode()
        {
            if (gradingAsset && gradingModeBefore.HasValue) gradingAsset.colorGradingMode = gradingModeBefore.Value;
            gradingAsset = null;
            gradingModeBefore = null;
        }

        void OnRenderOptions(IReadOnlyCollection<string> keys)
        {
            foreach (var k in keys)
            {
                if (k == "grading") dirty[0] = true;
                if (k == "ssr") dirty[5] = true;
            }
        }

        // ------------------------------------------------------------ 2 AO, 3 bloom

        void ApplyAo(AmbientOcclusionEffect a)
        {
            var p = AmbientOcclusionControl.Map(a.Enabled, a.Intensity, a.Thickness);
            ao.Apply(p);
            if (a.Enabled && (a.Color.R != 0f || a.Color.G != 0f || a.Color.B != 0f))
                runtime.ReportOnce(FindingSeverity.Info, "effects.aoColorIgnored", null, "the AO colour is not black: URP's SSAO darkens towards black (E8)");
        }

        void ApplyBloom(BloomEffect b)
        {
            int w = 1920, h = 1080;
            var cam = runtime.GetPart<SceneViewCamera>()?.Camera;
            if (cam && cam.pixelHeight > 0) { w = cam.pixelWidth; h = cam.pixelHeight; }
            var p = BloomMapping.Map(b.Enabled, b.Intensity, b.Threshold, b.SoftKnee, b.Clamp, b.Diffusion, new Color(b.Color.R, b.Color.G, b.Color.B, 1f), w, h);
            Bloom = p;
            var v = volume.Bloom;
            v.active = true;
            v.intensity.Override(p.Active ? p.Intensity : 0f);
            v.threshold.Override(p.Threshold);
            v.scatter.Override(p.Scatter);
            v.clamp.Override(p.Clamp);
            v.tint.Override(p.Tint);
            v.maxIterations.Override(p.MaxIterations);
            v.highQualityFiltering.Override(p.HighQuality);
        }

        /// <summary>
        /// Phase 3.6 (package C): maps bloom for a render of
        /// <paramref name="width"/> × <paramref name="height"/> pixels (PPv2's level count at that size decides URP's
        /// iterations and the intensity) in the shared volume until the returned scope is disposed, which writes the view's
        /// values back. A capture renders synchronously inside the scope, so the view never sees the capture's mapping.
        /// </summary>
        public IDisposable BloomForSize(int width, int height)
        {
            if (BloomRemapOff || runtime == null || disposed || volume == null || width <= 0 || height <= 0) return new BloomScope(null, 0f, 0);
            var b = runtime.Model.Settings.Effects.Bloom;
            var p = BloomMapping.Map(b.Enabled, b.Intensity, b.Threshold, b.SoftKnee, b.Clamp, b.Diffusion, new Color(b.Color.R, b.Color.G, b.Color.B, 1f), width, height);
            var v = volume.Bloom;
            var scope = new BloomScope(v, v.intensity.value, v.maxIterations.value);
            v.intensity.Override(p.Active ? p.Intensity : 0f);
            v.maxIterations.Override(p.MaxIterations);
            return scope;
        }

        /// <summary>Tests (CaptureTest S5): captures reuse the view's bloom mapping, as before Phase 3.6.</summary>
        public static bool BloomRemapOff;

        sealed class BloomScope : IDisposable
        {
            UnityEngine.Rendering.Universal.Bloom bloom;
            readonly float intensity;
            readonly int iterations;
            public BloomScope(UnityEngine.Rendering.Universal.Bloom b, float i, int n) { bloom = b; intensity = i; iterations = n; }
            public void Dispose()
            {
                if (bloom == null) return;
                bloom.intensity.Override(intensity);
                bloom.maxIterations.Override(iterations);
                bloom = null;
            }
        }

        // ------------------------------------------------------------ 4 DOF, 9 sun shafts (binding)

        Vector3 Pivot()
        {
            if (runtime.OverrideApp.TryCamera(out var target, out _, out _, out _)) return target;   // the camera as shown
            var cur = runtime.Model.Settings.Camera.Current;
            return cur != null ? new Vector3(cur.Pivot.X, cur.Pivot.Y, cur.Pivot.Z) : SceneViewCamera.DefaultPivot;
        }

        /// <summary>The binding point of a node id: (point, kind); kind "missing" when the id names no realized node.</summary>
        public (Vector3? point, string kind) PointOf(string id)
        {
            if (id == null) return (null, "none");
            var n = runtime.Model.Find(id);
            var r = runtime.Find(id);
            if (n == null || r == null || !r.HasHolder || r.InGraveyard) return (null, "missing");
            if (r is CharaRealization c && c.Root)
            {
                if (!eyeCache.TryGetValue(id, out var e) || e.root != c.Root)
                {
                    Transform l = null, rr = null, head = null;
                    foreach (var t in c.Root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == "cf_J_Eye_t_L") l = t;
                        else if (t.name == "cf_J_Eye_t_R") rr = t;
                        else if (t.name == "cf_J_Head" && !head) head = t;
                        if (l && rr) break;
                    }
                    eyeCache[id] = e = (c.Root, l, rr, head);
                }
                if (e.l && e.r) return ((e.l.position + e.r.position) * 0.5f, "eyes");
                if (e.head) return (e.head.position, "head");
            }
            return (r.Holder.position, "node");
        }

        readonly Dictionary<string, (GameObject root, Transform l, Transform r, Transform head)> eyeCache =
            new Dictionary<string, (GameObject, Transform, Transform, Transform)>(StringComparer.Ordinal);

        /// <summary>False when the last late apply's winning depth-of-field slot had a focus that did not resolve (it acted as the pivot).</summary>
        public bool DofOverrideResolved { get; private set; } = true;

        readonly Dictionary<string, (GameObject root, Transform t)> boneCache = new Dictionary<string, (GameObject, Transform)>(StringComparer.Ordinal);

        /// <summary>A focus form of the override layer as a world point; false when its node or bone does not resolve (then the pivot).</summary>
        bool FocusPoint(OverrideFocus f, out Vector3 point, out string kind)
        {
            switch (f.Kind)
            {
                case OverrideFocusKind.Point:
                    point = new Vector3((float)f.X, (float)f.Y, (float)f.Z);
                    kind = "point";
                    return true;
                case OverrideFocusKind.Dist:
                {
                    var cam = runtime.GetPart<SceneViewCamera>()?.Camera;
                    if (cam)
                    {
                        point = cam.transform.position + cam.transform.forward * (float)f.Dist;
                        kind = "dist";
                        return true;
                    }
                    break;
                }
                case OverrideFocusKind.Node:
                {
                    var (p, k) = PointOf(f.Node);
                    if (p.HasValue) { point = p.Value; kind = k; return true; }
                    break;
                }
                case OverrideFocusKind.NodeBone:
                {
                    var t = BoneOf(f.Node, f.Bone);
                    if (t)
                    {
                        point = t.position + new Vector3((float)f.X, (float)f.Y, (float)f.Z);
                        kind = "bone";
                        return true;
                    }
                    break;
                }
                default:
                    point = Pivot();
                    kind = "pivot";
                    return true;
            }
            point = Pivot();
            kind = "missing";
            return false;
        }

        /// <summary>The transform named <paramref name="bone"/> under a realized node's built object (a character's root, else its holder).</summary>
        Transform BoneOf(string id, string bone)
        {
            var n = runtime.Model.Find(id);
            var r = runtime.Find(id);
            if (n == null || r == null || !r.HasHolder || r.InGraveyard || string.IsNullOrEmpty(bone)) return null;
            var root = r is CharaRealization c && c.Root ? c.Root : r.HolderObject;
            string key = id + "/" + bone;
            if (boneCache.TryGetValue(key, out var e) && e.root == root && e.t) return e.t;
            Transform found = null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == bone) { found = t; break; }
            boneCache[key] = (root, found);
            return found;
        }

        void ResolveDofOverride(OverrideSlot w)
        {
            var v = w.Dof;
            bool ok = FocusPoint(v.Focus, out var point, out string kind);
            if (v.FocusTo != null)
            {
                ok &= FocusPoint(v.FocusTo, out var to, out _);
                point = Vector3.LerpUnclamped(point, to, (float)v.FocusBlend);
                kind = "blend";
            }
            DofOverrideResolved = ok;
            int refH = runtime.GetPart<SceneViewCamera>()?.Setup.ReferenceHeight ?? 0;
            var inputs = new DofInputs
            {
                Enabled = v.Enabled,
                Active = v.Enabled && v.Aperture > 0,
                FocusPoint = point,
                FocalSize = (float)v.FocalSize,
                Aperture = (float)v.Aperture,
                ReferenceHeight = refH,
                FocusKey = v.Focus.Kind == OverrideFocusKind.Node || v.Focus.Kind == OverrideFocusKind.NodeBone ? v.Focus.Node : null,
                FocusKind = ok ? kind : "missing",
                MaxBlur1080 = v.Blur > 0 ? (float)v.Blur : 0f,
                Overridden = w.Owner,
            };
            Dof = inputs;
            PushDof(inputs);
        }

        void ResolveDof(DepthOfFieldEffect d)
        {
            var w = runtime.Overrides.WinnerOf(null, OverrideChannel.DepthOfField);
            if (w != null && w.Dof != null) { ResolveDofOverride(w); return; }
            DofOverrideResolved = true;
            var (p, kind) = PointOf(d.Focus);
            Vector3 point = p ?? Pivot();
            if (p == null) kind = d.Focus == null ? "pivot" : "missing";
            int refH = runtime.GetPart<SceneViewCamera>()?.Setup.ReferenceHeight ?? 0;
            var inputs = new DofInputs
            {
                Enabled = d.Enabled,
                Active = d.Enabled && d.Aperture > 0f,
                FocusPoint = point,
                FocalSize = d.FocalSize,
                Aperture = d.Aperture,
                ReferenceHeight = refH,
                FocusKey = d.Focus,
                FocusKind = kind,
            };
            Dof = inputs;
            PushDof(inputs);
        }

        static void PushDof(DofInputs d)
        {
            CurrentDof = d;
            // package C's pass; it draws only while Input.Active (enabled and aperture > 0)
            HS2DofFeature.Inputs = new HS2DofFeature.Input
            {
                Enabled = d.Enabled, FocusPoint = d.FocusPoint, FocalSize = d.FocalSize, Aperture = d.Aperture, ReferenceHeight = d.ReferenceHeight,
                MaxBlur1080 = d.MaxBlur1080,
            };
        }

        static void ResetDof()
        {
            CurrentDof = default;
            HS2DofFeature.Inputs = default;
        }

        /// <summary>URP-native transparent material slots of the loaded map and the scene's own objects (they get no fog).</summary>
        int UrpNativeTransparentSlots()
        {
            int n = StudioFog.UrpNativeTransparentCount(runtime.Root);
            var map = runtime.GetPart<MapRealization>();
            if (map != null && map.Roots != null)
                foreach (var go in map.Roots) n += StudioFog.UrpNativeTransparentCount(go);
            return n;
        }

        /// <summary>The sun-shaft pass's inputs from the block and the resolved caster (no point: the screen centre). The
        /// stored 0..1 colours are handed over as they are, as the original effect takes them.</summary>
        static void PushShafts(SunShaftsEffect s, Vector3? casterPoint)
        {
            if (s == null) { HS2SunShaftsFeature.Inputs = default; return; }
            HS2SunShaftsFeature.Inputs = new HS2SunShaftsFeature.Input
            {
                Enabled = s.Enabled,
                HasCaster = casterPoint.HasValue,
                CasterPoint = casterPoint ?? Vector3.zero,
                Threshold = new Color(s.ThresholdColor.R, s.ThresholdColor.G, s.ThresholdColor.B, s.ThresholdColor.A),
                Colour = new Color(s.Color.R, s.Color.G, s.Color.B, s.Color.A),
                MaxRadius = s.DistanceFalloff,
                BlurSize = s.BlurSize,
                Intensity = s.Intensity,
            };
        }

        void ResolveCaster(SunShaftsEffect s)
        {
            var (p, kind) = PointOf(s.Caster);
            CasterKey = s.Caster;
            CasterPoint = p;
            CasterKind = p == null ? (s.Caster == null ? "none" : "missing") : kind;
        }

        // ------------------------------------------------------------ 7 reflection probe

        void ApplyProbe(ReflectionProbeEffect p)
        {
            var cam = runtime.GetPart<SceneViewCamera>()?.Camera;
            string pref = string.IsNullOrEmpty(p.Cubemap) ? null : p.Cubemap;
            Texture cube = null;
            if (pref != null && p.Enabled)   // the cubemap is loaded only while the probe is on (a new scene loads nothing)
            {
                cube = ProbeCubemap(runtime.Services, pref);
                if (!cube) runtime.ReportOnce(FindingSeverity.Warning, "effects.probeMissing", null, "the Probe row " + pref + " was not found: no cubemap");
            }
            ProbeRef = pref;
            probe.Apply(cam ? cam.transform : runtime.ViewRoot, p.Enabled, cube, p.Intensity);
        }

        // ------------------------------------------------------------ per frame

        public void LateApply()
        {
            if (runtime == null || disposed) return;
            var fx = runtime.Model.Settings.Effects;
            ResolveDof(fx.DepthOfField);       // the focus moves with its node
            ResolveCaster(fx.SunShafts);        // the caster moves with its node
            PushShafts(fx.SunShafts, CasterPoint);
            var cam = runtime.GetPart<SceneViewCamera>()?.Camera;
            if (cam) probe.Follow(cam.transform);
        }

        public void Clear()
        {
            MarkAll();
            eyeCache.Clear();
            boneCache.Clear();
            DofOverrideResolved = true;
            ResetDof();
            HS2SunShaftsFeature.Inputs = default;
            SsrControl.Reset();
        }

        public void OnLoadedRealized() => MarkAll();

        // ------------------------------------------------------------ map hooks

        bool Mine(MapRealization m) => runtime != null && runtime.GetPart<MapRealization>() == m;

        void OnMapLoaded(MapRealization m)
        {
            if (!Mine(m)) return;
            ApplyAll();
        }

        void OnMapUnloaded(MapRealization m)
        {
            if (!Mine(m)) return;
            ApplyAll();
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
                hooked = false;
            }
            ao.Release();
            ResetDof();
            HS2SunShaftsFeature.Inputs = default;
            StudioFog.Reset();
            SsrControl.Reset();
            RestorePipelineGradingMode();
            probe.Destroy();
            volume?.Destroy();
            volume = null;
            if (strip) { if (Application.isPlaying) Object.Destroy(strip); else Object.DestroyImmediate(strip); }
            strip = null;
        }
    }
}
