// Planar mirrors of horizontal planes: the image a puddle shows. The mechanism of MapMirrors in a class of its own: a hidden camera
// rendered inside the viewing camera's beginCameraRendering with URP's single-camera render, the view reflected in the plane, an oblique
// near plane on it, inverted winding, no post effects, no anti-aliasing. With no plane requested the class is not subscribed: no
// camera, no target, no cost.
//   Planes   Request(owner, floor, scale): one plane per distinct height (1 mm), 0.02 above the floor; a handle holds its puddle
//            renderers. Several owners of a height share the plane, which takes their largest scale.
//   Cameras  Game cameras whose culling mask holds a puddle's layer, above the plane, with a puddle of the plane in the frustum: so a
//            capture takes this path with no code of its own, and a thumbnail on an isolation layer or the UI's clear camera asks for
//            nothing. At most MaxPlanes planes per camera render, nearest first; a further plane is skipped and counted.
//   Target   a temporary render texture per camera render, the camera's size times the scale, at least 64 pixels, released at
//            endCameraRendering. It is bound as the global _HS2WetMirror0 / 1 for that camera, _HS2WetMirrorPlanes says which height each
//            holds, and they go back to black at endCameraRendering, so a camera with no mirror of its own samples no released target.
//   Render   the transform takes the reflected position and rotation; the explicit worldToCameraMatrix decides the image. Around the
//            render: every puddle off, the mosaic's frame of this camera (MosaicCamera.Enter, Leave) and a volume stack of its own with the
//            pipeline's reflections disabled, as that feature looks at the stack and not at post-processing. The wide pass's member mask draws
//            with a depth bias of a few units here: under an oblique projection the frame's draw and the mask's differ in the last bits of depth.
//            All of it is undone in a finally.
//   Failure  logged once; the plane is off for that camera render, a puddle shows its disc.
using System;
using System.Collections.Generic;
using HS2Next.Chara.Mosaic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace HS2Next
{
    public static class PlanarMirrors
    {
        /// <summary>Planes one camera render may show.</summary>
        public const int MaxPlanes = 2;
        /// <summary>The reflecting plane above the floor.</summary>
        public const float PlaneOffset = 0.02f;
        /// <summary>The per-renderer value of a puddle (see <see cref="Handle.PlaneY"/>).</summary>
        public static readonly int PlaneYId = Shader.PropertyToID("_WetPlaneY");

        static readonly int[] MirrorIds = { Shader.PropertyToID("_HS2WetMirror0"), Shader.PropertyToID("_HS2WetMirror1") };
        static readonly int PlanesId = Shader.PropertyToID("_HS2WetMirrorPlanes");
        const int UiLayer = 5;

        /// <summary>One owner's claim on a height.</summary>
        public sealed class Handle : IDisposable
        {
            internal Plane plane;
            /// <summary>Who asked: a runtime part, for two runtimes in one Editor.</summary>
            public object Owner { get; internal set; }
            /// <summary>The mirror target's size against the camera's, 0.1 to 1.</summary>
            public float Scale { get; set; }
            /// <summary>The puddles of this owner on this height; a puddle in the frustum is what asks for the mirror.</summary>
            public readonly List<Renderer> Puddles = new List<Renderer>();
            /// <summary>The value a puddle of this height carries as _WetPlaneY.</summary>
            public float PlaneY => plane != null ? plane.Y : float.NaN;

            public void Dispose()
            {
                if (plane == null) return;
                var p = plane;
                plane = null;
                p.Handles.Remove(this);
                if (p.Handles.Count == 0) planes.Remove(p);
                if (planes.Count == 0) Shut();
            }
        }

        internal sealed class Plane
        {
            public int Key;
            public float Y;
            public readonly List<Handle> Handles = new List<Handle>();
            public float Scale()
            {
                float s = 0f;
                foreach (var h in Handles) s = Mathf.Max(s, h.Scale);
                return Mathf.Clamp(s, 0.05f, 1f);
            }
        }

        /// <summary>Sets the global depth bias for what follows in the hidden camera's frame.</summary>
        sealed class DepthBias : ScriptableRenderPass
        {
            sealed class Data { public float Bias; }
            readonly float bias;

            public DepthBias(RenderPassEvent e, float bias)
            {
                renderPassEvent = e;
                this.bias = bias;
                profilingSampler = new ProfilingSampler("HS2 Planar Mirror Depth Bias");
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                using (var builder = graph.AddRasterRenderPass<Data>("HS2 Planar Mirror Depth Bias", out var data, profilingSampler))
                {
                    data.Bias = bias;
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
                    builder.AllowGlobalStateModification(true);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (Data d, RasterGraphContext ctx) => ctx.cmd.SetGlobalDepthBias(d.Bias, 0f));
                }
            }
        }

        static readonly DepthBias BiasOn = new DepthBias(RenderPassEvent.AfterRenderingPostProcessing - 1, -4f);
        static readonly DepthBias BiasOff = new DepthBias(RenderPassEvent.AfterRenderingPostProcessing + 1, 0f);

        sealed class Binding
        {
            public Camera Camera;
            public readonly RenderTexture[] Targets = new RenderTexture[MaxPlanes];
        }

        static readonly List<Plane> planes = new List<Plane>();
        static readonly List<Binding> bindings = new List<Binding>();
        static readonly List<(float d, Plane p)> picks = new List<(float, Plane)>();
        static readonly Comparison<(float d, Plane p)> Nearest = (a, b) => a.d.CompareTo(b.d);
        static readonly List<Renderer> hidden = new List<Renderer>();
        static readonly UnityEngine.Plane[] frustum = new UnityEngine.Plane[6];
        static Camera mirror;
        static UniversalAdditionalCameraData mirrorData;
        static VolumeStack mirrorStack;
        static bool subscribed, inside, logged;

        /// <summary>Hooked to the pipeline's camera callbacks now.</summary>
        public static bool Subscribed => subscribed;
        /// <summary>Hidden cameras alive, targets held, planes requested.</summary>
        public static int Cameras => mirror ? 1 : 0;
        public static int Targets { get { int n = 0; foreach (var b in bindings) foreach (var t in b.Targets) if (t) n++; return n; } }
        public static int Planes => planes.Count;
        /// <summary>Statistics since start: mirror renders, planes over the cap, failed renders.</summary>
        public static int Renders { get; private set; }
        public static int SkippedOverCap { get; private set; }
        public static int Failures { get; private set; }
#if UNITY_EDITOR
        /// <summary>Editor tests only: called with the hidden camera between the mosaic's frame and the render.</summary>
        public static Action<Camera> BeforeRender;
        /// <summary>Editor tests only: called with the hidden camera and its target right after the render.</summary>
        public static Action<Camera, RenderTexture> AfterRender;
#endif

        /// <summary>A claim on the plane over <paramref name="floor"/>: no render until a puddle of it is in view. Dispose gives it back.</summary>
        public static Handle Request(object owner, float floor, float scale = 0.5f)
        {
            int key = Mathf.RoundToInt(floor * 1000f);
            Plane p = null;
            foreach (var x in planes) if (x.Key == key) { p = x; break; }
            if (p == null) planes.Add(p = new Plane { Key = key, Y = floor });
            var h = new Handle { plane = p, Owner = owner, Scale = scale };
            p.Handles.Add(h);
            if (!subscribed)
            {
                subscribed = true;
                Clear();
                RenderPipelineManager.beginCameraRendering += OnBegin;
                RenderPipelineManager.endCameraRendering += OnEnd;
            }
            return h;
        }

        static void Shut()
        {
            if (!subscribed) return;
            subscribed = false;
            RenderPipelineManager.beginCameraRendering -= OnBegin;
            RenderPipelineManager.endCameraRendering -= OnEnd;
            foreach (var b in bindings) Release(b);
            bindings.Clear();
            Clear();
            if (mirror)
            {
                var go = mirror.gameObject;
                mirror = null; mirrorData = null;
                if (mirrorStack != null) { VolumeManager.instance.DestroyStack(mirrorStack); mirrorStack = null; }
                if (Application.isPlaying) Object.Destroy(go); else Object.DestroyImmediate(go);
            }
        }

        static void Clear()
        {
            foreach (var id in MirrorIds) Shader.SetGlobalTexture(id, Texture2D.blackTexture);
            Shader.SetGlobalVector(PlanesId, Vector4.zero);
        }

        static void Release(Binding b)
        {
            for (int i = 0; i < b.Targets.Length; i++)
                if (b.Targets[i]) { RenderTexture.ReleaseTemporary(b.Targets[i]); b.Targets[i] = null; }
        }

        static bool Shows(Renderer r, Camera cam) =>
            r && r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy && (cam.cullingMask & (1 << r.gameObject.layer)) != 0
            && GeometryUtility.TestPlanesAABB(frustum, r.bounds);

        static void OnBegin(ScriptableRenderContext ctx, Camera cam)
        {
            if (inside || !cam || cam == mirror || cam.cameraType != CameraType.Game) return;
            picks.Clear();
            GeometryUtility.CalculateFrustumPlanes(cam, frustum);
            var cp = cam.transform.position;
            foreach (var p in planes)
            {
                if (cp.y <= p.Y + PlaneOffset) continue;
                float best = float.PositiveInfinity;
                foreach (var h in p.Handles)
                    foreach (var r in h.Puddles)
                        if (Shows(r, cam)) best = Mathf.Min(best, (r.bounds.center - cp).sqrMagnitude);
                if (best < float.PositiveInfinity) picks.Add((best, p));
            }
            if (picks.Count == 0) return;
            picks.Sort(Nearest);
            var binding = new Binding { Camera = cam };
            var heights = Vector4.zero;
            for (int i = 0; i < picks.Count; i++)
            {
                if (i >= MaxPlanes) { SkippedOverCap++; continue; }
                var p = picks[i].p;
                var t = Render(ctx, cam, p);
                if (!t) continue;
                binding.Targets[i] = t;
                Shader.SetGlobalTexture(MirrorIds[i], t);
                heights[i] = p.Y;
                heights[2 + i] = 1f;
            }
            Shader.SetGlobalVector(PlanesId, heights);
            bindings.Add(binding);
        }

        static void OnEnd(ScriptableRenderContext ctx, Camera cam)
        {
            for (int i = bindings.Count - 1; i >= 0; i--)
            {
                if (bindings[i].Camera != cam) continue;
                Release(bindings[i]);
                bindings.RemoveAt(i);
                Clear();
                return;
            }
        }

        static Camera Hidden()
        {
            if (mirror) return mirror;
            var go = new GameObject("HS2Next planar mirror camera") { hideFlags = HideFlags.HideAndDontSave };
            if (Application.isPlaying) Object.DontDestroyOnLoad(go);
            mirror = go.AddComponent<Camera>();
            mirror.enabled = false;
            mirrorData = go.AddComponent<UniversalAdditionalCameraData>();
            mirrorData.renderPostProcessing = false;
            mirrorData.antialiasing = AntialiasingMode.None;
            mirrorData.requiresColorOption = CameraOverrideOption.Off;
            mirrorData.requiresDepthOption = CameraOverrideOption.Off;
            mirrorStack = VolumeManager.instance.CreateStack();
#if URP_SCREEN_SPACE_REFLECTION
            var ssr = mirrorStack.GetComponent<ScreenSpaceReflectionVolumeSettings>();
            if (ssr != null) ssr.mode.value = ScreenSpaceReflectionVolumeSettings.ReflectionMode.Disabled;
#endif
            return mirror;
        }

        static RenderTexture Render(ScriptableRenderContext ctx, Camera view, Plane plane)
        {
            float scale = plane.Scale();
            int w = Mathf.Max(64, Mathf.RoundToInt(view.pixelWidth * scale)), h = Mathf.Max(64, Mathf.RoundToInt(view.pixelHeight * scale));
            var target = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            try
            {
                Draw(ctx, view, plane, target, scale);
                Renders++;
                return target;
            }
            catch (Exception e)
            {
                Failures++;
                if (!logged) { logged = true; Debug.LogWarning("planar mirror render failed: " + e.Message); }
                RenderTexture.ReleaseTemporary(target);
                return null;
            }
        }

        static void Draw(ScriptableRenderContext ctx, Camera view, Plane plane, RenderTexture target, float scale)
        {
            var rc = Hidden();
            rc.CopyFrom(view);
            rc.enabled = false;
            rc.targetTexture = target;
            rc.rect = new Rect(0f, 0f, 1f, 1f);
            rc.cameraType = CameraType.Game;
            rc.cullingMask = view.cullingMask & ~(1 << ViewSetup.HelperLayer) & ~(1 << UiLayer);
            rc.allowMSAA = false;
            mirrorData.renderShadows = !view.TryGetComponent<UniversalAdditionalCameraData>(out var vd) || vd.renderShadows;
            float py = plane.Y + PlaneOffset;
            var r = Matrix4x4.identity;
            r.m11 = -1f; r.m13 = 2f * py;
            rc.worldToCameraMatrix = view.worldToCameraMatrix * r;
            var vt = view.transform;
            rc.transform.SetPositionAndRotation(r.MultiplyPoint(vt.position), Quaternion.LookRotation(r.MultiplyVector(vt.forward), r.MultiplyVector(vt.up)));
            var w2c = rc.worldToCameraMatrix;
            Vector3 cpos = w2c.MultiplyPoint(new Vector3(0f, py, 0f));
            Vector3 cn = w2c.MultiplyVector(Vector3.up).normalized;
            rc.projectionMatrix = view.CalculateObliqueMatrix(new Vector4(cn.x, cn.y, cn.z, -Vector3.Dot(cpos, cn)));

            hidden.Clear();
            foreach (var p in planes)
                foreach (var hd in p.Handles)
                    foreach (var pr in hd.Puddles)
                        if (pr && !pr.forceRenderingOff) { pr.forceRenderingOff = true; hidden.Add(pr); }
            var outer = MosaicCamera.FrameOf(view);
            int k = Mathf.Max(1, Mathf.RoundToInt((outer != null ? outer.OutputScale : 1) * scale));
            bool inv = GL.invertCulling, entered = false;
            var stack = VolumeManager.instance.stack;
            VolumeManager.instance.stack = mirrorStack;
            GL.invertCulling = !inv;
            inside = true;
            try
            {
                MosaicCamera.Enter(rc, k);
                entered = true;
#if UNITY_EDITOR
                BeforeRender?.Invoke(rc);
#endif
                if (MosaicWide.Enabled && MosaicCamera.FrameOf(rc)?.Groups.Count > 0 && mirrorData.scriptableRenderer is ScriptableRenderer sr)
                {
                    sr.EnqueuePass(BiasOn);
                    sr.EnqueuePass(BiasOff);
                }
#pragma warning disable CS0618 // the single-camera render inside the current context (the request API cannot nest in a camera's render)
                UniversalRenderPipeline.RenderSingleCamera(ctx, rc);
#pragma warning restore CS0618
#if UNITY_EDITOR
                AfterRender?.Invoke(rc, target);
#endif
            }
            finally
            {
                inside = false;
                if (entered) MosaicCamera.Leave(rc);
                foreach (var pr in hidden) if (pr) pr.forceRenderingOff = false;
                hidden.Clear();
                GL.invertCulling = inv;
                VolumeManager.instance.stack = stack;
                rc.targetTexture = null;
            }
        }
    }
}
