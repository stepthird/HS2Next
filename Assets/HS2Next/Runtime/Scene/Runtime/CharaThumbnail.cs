// Character thumbnail of a character node in a scene, in two framings:
//   Front  the character in the Studio default pose (StudioDefaultPoseScope), isolated on a free layer and shot with HS2's
//          coordinate camera relative to its holder (the bridge's and every headless save's default):
//          1. StudioDefaultPoseScope.Enter (no animation, default expression with eyes open, eyes and neck forward, hands 0,
//             physics off, settled).
//          2. Isolation (below).
//          3. Camera: holder.position + holder.rotation·(0, 10.2, 45.6) (female) / (0, 10.5, 46.9) (male), rotation
//             holder.rotation·Euler(2.62, −180, 0), vertical FOV CropFov(23), chara lighting on; holder scale ignored. For a
//             holder at the origin facing +Z this equals ThumbnailCapture.CaptureCoordinateShot.
//   View   the character as shown (no pose scope: its animation frame, expression, look, clothes and accessory state,
//          physics), isolated the same way, shot from the given camera's position and rotation with its vertical field of
//          view and its near / far planes (the 504 × 704 image is the centre crop at that field of view), chara light and
//          the new-scene ambient colours, grey 128, SMAA. The UI's display camera is the view; without one the caller falls
//          back to Front.
// Isolation (both framings, and the off-scene builds of MakerThumbnails / CharaFileImport through Isolate): the highest
// layer in 24..31 that no GameObject under the runtime root or in the map scene uses (scanned per capture; none →
// `thumbnail.noFreeLayer`, error, no capture). The node's own renderers (the walk from its holder that stops at every other
// node holder, as visibility uses it) move to that layer and the capture camera renders only it; every other enabled Light
// and ReflectionProbe in the loaded scenes and RenderSettings.fog are off for the capture, the isolated renderers use
// neither light nor reflection probes (a map's probes would light them), and the capture is ThumbnailCapture's isolated
// one (no volumes, its own ambient probe and default reflection). Everything is restored in `finally`.
// Needs a graphics device (ThumbnailCapture throws without one). Main thread only.
using System;
using System.Collections.Generic;
using HS2Next.Content.Studio;
using HS2Next.Motion.Animation;
using HS2Next.Save;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    /// <summary>The two framings of a character thumbnail.</summary>
    public enum CharaThumbnailFraming
    {
        /// <summary>HS2's coordinate camera relative to the node, Studio default pose (the bridge's and headless saves' default).</summary>
        Front,
        /// <summary>The display camera's pose and field of view, the character as shown.</summary>
        View,
    }

    /// <summary>What a character capture did (tests, diagnostics).</summary>
    public sealed class CharaThumbnailInfo
    {
        /// <summary>The isolation layer used (-1 when none was free).</summary>
        public int Layer = -1;
        /// <summary>Renderers moved to the isolation layer.</summary>
        public int Renderers;
        /// <summary>Other lights switched off for the capture.</summary>
        public int LightsOff;
        public bool FogWasOn;
        public long Milliseconds;
        /// <summary>The framing used.</summary>
        public CharaThumbnailFraming Framing;
    }

    public static class CharaThumbnail
    {
        public const int FirstIsolationLayer = 24, LastIsolationLayer = 31;

        /// <summary>Thumbnail of a built character node of a scene runtime: 504 × 704 RGB PNG, or null when no isolation
        /// layer is free (finding `thumbnail.noFreeLayer`).</summary>
        public static byte[] Capture(CharaRealization r, ThumbnailSettings settings = null) => Capture(r, settings, out _);

        public static byte[] Capture(CharaRealization r, ThumbnailSettings settings, out CharaThumbnailInfo info)
        {
            Prepare(r, out var map);
            var rt = r.Runtime;
            return Capture(r.Holder, r.Root, r.Sex == 0, rt.Services.Db, rt.Root ? rt.Root.transform : null,
                           map, rt.Findings, r.NodeId, settings, out info);
        }

        /// <summary>
        /// Renders the character built under <paramref name="charaRoot"/> (content of the node holder <paramref name="holder"/>)
        /// in the Studio default pose and returns a 504 × 704 RGB PNG, or null when no isolation layer is free
        /// (finding `thumbnail.noFreeLayer` on <paramref name="findings"/>).
        /// </summary>
        /// <param name="runtimeRoot">The scene runtime's root (scanned for used layers); null = the holder's root.</param>
        /// <param name="mapScene">The loaded map scene (scanned for used layers), or default.</param>
        /// <param name="male">Sex of the character (picks the male or female coordinate camera).</param>
        public static byte[] Capture(Transform holder, GameObject charaRoot, bool male, StudioDb db, Transform runtimeRoot, Scene mapScene,
                                     SceneFindings findings, string nodeId, ThumbnailSettings settings, out CharaThumbnailInfo info) =>
            Front(holder, charaRoot, male, db, runtimeRoot, mapScene, findings, nodeId, settings, out info, Png);

        /// <summary>
        /// The view framing of a built character node: the character as shown, isolated, from <paramref name="view"/>'s pose,
        /// vertical field of view and clip planes. 504 × 704 RGB PNG, or null when no isolation layer is free.
        /// </summary>
        public static byte[] CaptureView(CharaRealization r, Camera view, ThumbnailSettings settings = null) => CaptureView(r, view, settings, out _);

        public static byte[] CaptureView(CharaRealization r, Camera view, ThumbnailSettings settings, out CharaThumbnailInfo info)
        {
            Prepare(r, out var map);
            var rt = r.Runtime;
            return View(r.Holder, view, rt.Root ? rt.Root.transform : null, map, rt.Findings, r.NodeId, settings, out info, Png);
        }

        /// <summary>
        /// The texture a save of <paramref name="r"/> in <paramref name="framing"/> would encode (504 × 704 RGB24, no mipmaps;
        /// the caller destroys it), or null when no isolation layer is free. View needs <paramref name="view"/>.
        /// </summary>
        public static Texture2D CaptureTexture(CharaRealization r, CharaThumbnailFraming framing, Camera view, ThumbnailSettings settings, out CharaThumbnailInfo info)
        {
            Prepare(r, out var map);
            var rt = r.Runtime;
            var runtimeRoot = rt.Root ? rt.Root.transform : null;
            if (framing == CharaThumbnailFraming.View)
            {
                if (!view) throw new ArgumentNullException(nameof(view));
                return View(r.Holder, view, runtimeRoot, map, rt.Findings, r.NodeId, settings, out info, ThumbnailCapture.CaptureTexture);
            }
            return Front(r.Holder, r.Root, r.Sex == 0, rt.Services.Db, runtimeRoot, map, rt.Findings, r.NodeId, settings, out info, ThumbnailCapture.CaptureTexture);
        }

        delegate T Shot<T>(Vector3 position, Quaternion rotation, float verticalFov, ThumbnailSettings settings, float? near, float? far);

        static byte[] Png(Vector3 position, Quaternion rotation, float verticalFov, ThumbnailSettings settings, float? near, float? far) =>
            ThumbnailCapture.Capture(position, rotation, verticalFov, settings, near, far);

        static void Prepare(CharaRealization r, out Scene map)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (r.State != RealizationState.Built || !r.Root || !r.Holder)
                throw new InvalidOperationException($"character {r.NodeId} is not built ({r.State})");
            var m = r.Runtime.GetPart<MapRealization>();
            map = m != null ? m.Scene : default;
        }

        static T Front<T>(Transform holder, GameObject charaRoot, bool male, StudioDb db, Transform runtimeRoot, Scene mapScene,
                          SceneFindings findings, string nodeId, ThumbnailSettings settings, out CharaThumbnailInfo info, Shot<T> shot) where T : class
        {
            if (!holder) throw new ArgumentNullException(nameof(holder));
            if (!charaRoot) throw new ArgumentNullException(nameof(charaRoot));
            info = new CharaThumbnailInfo { Framing = CharaThumbnailFraming.Front };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // the pose scope creates no objects (it releases option items), so the layer scan can come first
            int layer = FreeLayer(runtimeRoot ? runtimeRoot : holder.root, mapScene);
            if (layer < 0)
            {
                NoLayer(findings, nodeId);
                return null;
            }
            info.Layer = layer;
            using (StudioDefaultPoseScope.Enter(charaRoot, db))
            using (var iso = Isolate(VisibilityApplier.OwnRenderers(holder), layer, info))
            {
                var rot = holder.rotation;
                var offset = male ? ThumbnailCapture.CoordCameraMale : ThumbnailCapture.CoordCameraFemale;
                var result = shot(holder.position + rot * offset, rot * Quaternion.Euler(ThumbnailCapture.CoordCameraEuler),
                                  ThumbnailCapture.CropFov(ThumbnailCapture.Hs2Fov), ShotSettings(settings, layer), null, null);
                info.Milliseconds = sw.ElapsedMilliseconds;
                return result;
            }
        }

        static T View<T>(Transform holder, Camera view, Transform runtimeRoot, Scene mapScene, SceneFindings findings, string nodeId,
                         ThumbnailSettings settings, out CharaThumbnailInfo info, Shot<T> shot) where T : class
        {
            if (!holder) throw new ArgumentNullException(nameof(holder));
            if (!view) throw new ArgumentNullException(nameof(view));
            info = new CharaThumbnailInfo { Framing = CharaThumbnailFraming.View };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int layer = FreeLayer(runtimeRoot ? runtimeRoot : holder.root, mapScene);
            if (layer < 0)
            {
                NoLayer(findings, nodeId);
                return null;
            }
            info.Layer = layer;
            using (var iso = Isolate(VisibilityApplier.OwnRenderers(holder), layer, info))
            {
                var t = view.transform;
                var result = shot(t.position, t.rotation, view.fieldOfView, ShotSettings(settings, layer), view.nearClipPlane, view.farClipPlane);
                info.Milliseconds = sw.ElapsedMilliseconds;
                return result;
            }
        }

        static void NoLayer(SceneFindings findings, string nodeId) =>
            findings?.Error("thumbnail.noFreeLayer", nodeId, $"no layer in {FirstIsolationLayer}..{LastIsolationLayer} is free for the character thumbnail");

        /// <summary>The capture settings of an isolated character shot: the caller's background and pipeline, chara lighting on,
        /// the camera culling only <paramref name="layer"/>.</summary>
        public static ThumbnailSettings ShotSettings(ThumbnailSettings settings, int layer)
        {
            var s = settings ?? new ThumbnailSettings();
            return new ThumbnailSettings
            {
                Background = s.Background,
                Pipeline = s.Pipeline,
                CharaLighting = true,
                NearClip = s.NearClip,
                FarClip = s.FarClip,
                CullingMask = 1 << layer,
                Isolated = true,
            };
        }

        /// <summary>
        /// Puts <paramref name="renderers"/> on <paramref name="layer"/> without light and reflection probes, switches every other
        /// enabled light and reflection probe of the loaded scenes and the fog off; the handle restores layers, probe use,
        /// probes, lights and fog (in that reverse order). <paramref name="info"/> receives the counts.
        /// </summary>
        public static IDisposable Isolate(IReadOnlyList<Renderer> renderers, int layer, CharaThumbnailInfo info = null)
        {
            var iso = new Isolation(renderers ?? Array.Empty<Renderer>());
            iso.Apply(layer);
            if (info != null)
            {
                info.Renderers = iso.Renderers.Count;
                info.LightsOff = iso.LightsOff.Count;
                info.FogWasOn = iso.Fog;
            }
            return iso;
        }

        sealed class Isolation : IDisposable
        {
            public readonly IReadOnlyList<Renderer> Renderers;
            public readonly List<Light> LightsOff = new List<Light>();
            public readonly List<ReflectionProbe> ProbesOff = new List<ReflectionProbe>();
            readonly int[] oldLayers;
            readonly LightProbeUsage[] oldLightProbes;
            readonly UnityEngine.Rendering.ReflectionProbeUsage[] oldReflectionProbes;
            public bool Fog;
            bool done;

            public Isolation(IReadOnlyList<Renderer> renderers)
            {
                Renderers = renderers;
                oldLayers = new int[renderers.Count];
                oldLightProbes = new LightProbeUsage[renderers.Count];
                oldReflectionProbes = new UnityEngine.Rendering.ReflectionProbeUsage[renderers.Count];
            }

            public void Apply(int layer)
            {
                Fog = RenderSettings.fog;
                try
                {
                    for (int i = 0; i < Renderers.Count; i++)
                    {
                        if (!Renderers[i]) continue;
                        oldLayers[i] = Renderers[i].gameObject.layer;
                        Renderers[i].gameObject.layer = layer;
                        oldLightProbes[i] = Renderers[i].lightProbeUsage;
                        oldReflectionProbes[i] = Renderers[i].reflectionProbeUsage;
                        Renderers[i].lightProbeUsage = LightProbeUsage.Off;
                        Renderers[i].reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    }
                    foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                        if (l.enabled) { l.enabled = false; LightsOff.Add(l); }
                    // Forward+ blends every visible probe whatever a renderer's setting: the probes themselves are off
                    foreach (var p in Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                        if (p.enabled) { p.enabled = false; ProbesOff.Add(p); }
                    RenderSettings.fog = false;
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                if (done) return;
                done = true;
                RenderSettings.fog = Fog;
                foreach (var l in LightsOff) if (l) l.enabled = true;
                foreach (var p in ProbesOff) if (p) p.enabled = true;
                for (int i = 0; i < Renderers.Count; i++)
                {
                    if (!Renderers[i]) continue;
                    Renderers[i].reflectionProbeUsage = oldReflectionProbes[i];
                    Renderers[i].lightProbeUsage = oldLightProbes[i];
                    Renderers[i].gameObject.layer = oldLayers[i];
                }
            }
        }

        /// <summary>Sex of a character built by CharaAssembler (true = male; false when unknown).</summary>
        public static bool IsMale(GameObject charaRoot) => CharaAnimation.Of(charaRoot)?.Context?.IsMale ?? false;

        /// <summary>The highest layer in 24..31 used by no GameObject under <paramref name="runtimeRoot"/> or in <paramref name="mapScene"/>; -1 if none.</summary>
        public static int FreeLayer(Transform runtimeRoot, Scene mapScene)
        {
            int used = 0;
            if (runtimeRoot)
                foreach (var t in runtimeRoot.GetComponentsInChildren<Transform>(true)) used |= 1 << t.gameObject.layer;
            if (mapScene.IsValid() && mapScene.isLoaded)
                foreach (var g in mapScene.GetRootGameObjects())
                    foreach (var t in g.GetComponentsInChildren<Transform>(true)) used |= 1 << t.gameObject.layer;
            return HighestFree(used);
        }

        /// <summary>The highest layer in 24..31 used by no GameObject of any loaded scene (off-scene builds: the scene runtime
        /// and the map live in loaded scenes); -1 if none. <paramref name="except"/>'s objects are not counted.</summary>
        public static int FreeLayerInLoadedScenes(Transform except = null)
        {
            int used = 0;
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                foreach (var g in scene.GetRootGameObjects())
                {
                    if (except && g.transform == except) continue;
                    foreach (var t in g.GetComponentsInChildren<Transform>(true)) used |= 1 << t.gameObject.layer;
                }
            }
            return HighestFree(used);
        }

        static int HighestFree(int used)
        {
            for (int l = LastIsolationLayer; l >= FirstIsolationLayer; l--)
                if ((used & (1 << l)) == 0) return l;
            return -1;
        }
    }
}
