// Planar mirrors for HS2's MirrorReflection components (M10; package B).
// HS2 (behaviour, [code]): for every camera that renders the mirror's renderer, a hidden camera renders the scene reflected in
// the plane through the mirror's position with normal transform.up, moved m_ClipPlaneOffset along the normal, with an oblique
// near plane on that plane, culling mask m_ReflectLayers without layer 4 (Water) and inverted winding, into an
// m_TextureSize² target that is set as _ReflectionTex on every material of the renderer that declares it.
// HS2Next: one target per mirror (its slot materials become per-mirror copies, so two mirrors sharing a bundle material do
// not share a target), rendered from RenderPipelineManager.beginCameraRendering for each camera that is about to render a
// visible mirror (game and scene-view cameras, captures included), before that camera renders: URP's single-camera render
// of a hidden reflection camera inside the same context (the oblique projection derived from the viewing camera's). At most
// MapOptions.MaxMirrors mirrors render per camera render (nearest first); mirrors outside the view frustum, facing away or
// on a layer the camera does not draw are skipped. Not reproduced: m_DisablePixelLights (URP has no per-camera pixel-light
// count; Forward+ lights everything), the cameraOriginal template, pixel-light state. SSR stays off.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    public static class MapMirrors
    {
        public const string ReflectionTex = "_ReflectionTex";

        /// <summary>Statistics since start (MapRenderTest, reports).</summary>
        public static int RendersTotal { get; private set; }
        public static int SkippedOverCap { get; private set; }
        /// <summary>Handles currently attached (one per loaded map with mirrors).</summary>
        public static int ActiveHandles => handles.Count;

        static readonly List<Handle> handles = new List<Handle>();
        static bool subscribed, inside;

        /// <summary>Attaches every MirrorReflection of the map that has a renderer (active or not); null when there is none.</summary>
        public static Handle Attach(MapRealization map) => map == null ? null : Attach(map.Structure, map.Materials);

        /// <summary>As <see cref="Attach(MapRealization)"/> for a structure and its owned-copy tracker (tests build both).</summary>
        public static Handle Attach(MapStructure s, MapMaterials materials)
        {
            if (s == null || s.Mirrors.Count == 0) return null;
            var h = new Handle(materials);
            foreach (var m in s.Mirrors)
                if (m.Stub && m.Renderer) h.Add(m.Stub, m.Renderer);
            if (h.Count == 0) { h.Dispose(); return null; }
            handles.Add(h);
            if (!subscribed)
            {
                RenderPipelineManager.beginCameraRendering += OnBeginCamera;
                subscribed = true;
            }
            return h;
        }

        static void Unregister(Handle h)
        {
            handles.Remove(h);
            if (handles.Count == 0 && subscribed)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
                subscribed = false;
            }
        }

        static readonly List<(float d, Mirror m)> candidates = new List<(float, Mirror)>();
        static readonly Plane[] frustum = new Plane[6];

        static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (inside || !cam) return;
            if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
            if (IsReflectionCamera(cam)) return;
            candidates.Clear();
            GeometryUtility.CalculateFrustumPlanes(cam, frustum);
            var cp = cam.transform.position;
            foreach (var h in handles)
                foreach (var m in h.Mirrors)
                {
                    if (!m.Visible(cam, frustum)) continue;
                    candidates.Add(((m.Renderer.bounds.center - cp).sqrMagnitude, m));
                }
            if (candidates.Count == 0) return;
            candidates.Sort((a, b) => a.d.CompareTo(b.d));
            int cap = Math.Max(0, MapOptions.MaxMirrors);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (i >= cap) { SkippedOverCap++; continue; }
                inside = true;
                try { candidates[i].m.Render(ctx, cam); RendersTotal++; }
                catch (Exception e) { Debug.LogWarning("map mirror render failed: " + e.Message); }
                finally { inside = false; }
            }
        }

        static readonly HashSet<Camera> reflectionCameras = new HashSet<Camera>();
        static bool IsReflectionCamera(Camera c) => reflectionCameras.Contains(c);

        /// <summary>One mirror: its target, its per-mirror material copies and its hidden camera.</summary>
        public sealed class Mirror
        {
            public MirrorReflection Stub;
            public Renderer Renderer;
            public RenderTexture Target;
            public Camera Camera;
            public readonly List<Material> Materials = new List<Material>();
            public int Size;
            public float ClipOffset;
            public int Layers;
            public int Renders;

            public bool Visible(Camera cam, Plane[] planes)
            {
                if (!Stub || !Renderer || !Stub.isActiveAndEnabled || !Renderer.enabled || !Renderer.gameObject.activeInHierarchy) return false;
                if ((cam.cullingMask & (1 << Renderer.gameObject.layer)) == 0) return false;
                var t = Stub.transform;
                if (Vector3.Dot(cam.transform.position - t.position, t.up) <= 0) return false;   // behind the mirror plane
                return GeometryUtility.TestPlanesAABB(planes, Renderer.bounds);
            }

            public void Render(ScriptableRenderContext ctx, Camera view)
            {
                var t = Stub.transform;
                Vector3 n = t.up, p = t.position;
                var rc = Camera;
                rc.CopyFrom(view);
                rc.enabled = false;
                rc.targetTexture = Target;
                rc.cameraType = CameraType.Game;
                rc.cullingMask = Layers & view.cullingMask;
                // reflection about the plane n·x + d = 0, the plane moved by the clip offset along n
                float d = -Vector3.Dot(n, p) - ClipOffset;
                var r = Reflection(n, d);
                rc.worldToCameraMatrix = view.worldToCameraMatrix * r;
                rc.transform.position = r.MultiplyPoint(view.transform.position);
                // oblique near plane: the mirror plane (offset) in the reflection camera's space, facing the camera
                var w2c = rc.worldToCameraMatrix;
                Vector3 cpos = w2c.MultiplyPoint(p + n * ClipOffset);
                Vector3 cn = w2c.MultiplyVector(n).normalized;
                rc.projectionMatrix = view.CalculateObliqueMatrix(new Vector4(cn.x, cn.y, cn.z, -Vector3.Dot(cpos, cn)));
                foreach (var m in Materials) if (m) m.SetTexture(ReflectionTex, Target);
                bool inv = GL.invertCulling;
                GL.invertCulling = !inv;
                try
                {
#pragma warning disable CS0618 // the single-camera render inside the current context (the request API cannot nest in a camera's render)
                    UniversalRenderPipeline.RenderSingleCamera(ctx, rc);
#pragma warning restore CS0618
                }
                finally { GL.invertCulling = inv; }
                Renders++;
            }

            static Matrix4x4 Reflection(Vector3 n, float d)
            {
                var m = Matrix4x4.identity;
                m.m00 = 1 - 2 * n.x * n.x; m.m01 = -2 * n.x * n.y; m.m02 = -2 * n.x * n.z; m.m03 = -2 * d * n.x;
                m.m10 = -2 * n.y * n.x; m.m11 = 1 - 2 * n.y * n.y; m.m12 = -2 * n.y * n.z; m.m13 = -2 * d * n.y;
                m.m20 = -2 * n.z * n.x; m.m21 = -2 * n.z * n.y; m.m22 = 1 - 2 * n.z * n.z; m.m23 = -2 * d * n.z;
                return m;
            }
        }

        public sealed class Handle : IDisposable
        {
            readonly MapMaterials owner;
            public readonly List<Mirror> Mirrors = new List<Mirror>();
            public int Count => Mirrors.Count;
            bool disposed;

            public Handle(MapMaterials owner) { this.owner = owner; }

            public void Add(MirrorReflection stub, Renderer renderer)
            {
                int size = stub.m_TextureSize > 0 ? Mathf.Clamp(Mathf.NextPowerOfTwo(stub.m_TextureSize), 16, 4096) : 256;
                var m = new Mirror
                {
                    Stub = stub, Renderer = renderer, Size = size, ClipOffset = stub.m_ClipPlaneOffset,
                    Layers = stub.m_ReflectLayers.value & ~(1 << 4),
                };
                m.Target = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
                {
                    name = "HS2Next mirror " + renderer.name, hideFlags = HideFlags.DontSave,
                };
                m.Target.Create();
                // per-mirror copies of the slots that declare _ReflectionTex (HS2 writes every such material of the renderer)
                var mats = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (!src || !src.HasProperty(ReflectionTex)) continue;
                    var c = owner != null ? owner.Copy(src) : HS2Next.MaterialCopies.Copy(src);
                    c.SetTexture(ReflectionTex, m.Target);
                    mats[i] = c;
                    m.Materials.Add(c);
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = mats;
                var go = new GameObject("HS2Next mirror camera " + renderer.name) { hideFlags = HideFlags.HideAndDontSave };
                if (Application.isPlaying) Object.DontDestroyOnLoad(go);
                m.Camera = go.AddComponent<Camera>();
                m.Camera.enabled = false;
                var data = go.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.None;
                data.requiresColorOption = CameraOverrideOption.Off;
                data.requiresDepthOption = CameraOverrideOption.Off;
                reflectionCameras.Add(m.Camera);
                Mirrors.Add(m);
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                Unregister(this);
                foreach (var m in Mirrors)
                {
                    foreach (var c in m.Materials) if (c) c.SetTexture(ReflectionTex, null);
                    if (m.Camera)
                    {
                        reflectionCameras.Remove(m.Camera);
                        m.Camera.targetTexture = null;
                        Destroy(m.Camera.gameObject);
                    }
                    if (m.Target) { m.Target.Release(); Destroy(m.Target); }
                }
                Mirrors.Clear();
            }

            static void Destroy(Object o)
            {
                if (Application.isPlaying) Object.Destroy(o);
                else Object.DestroyImmediate(o);
            }
        }
    }
}
