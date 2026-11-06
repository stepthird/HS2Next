// View camera and chara light of a realized scene. Both are parts of a
// SceneRuntime (ISceneRuntimePart; register the camera before the light).
// SceneViewCamera puts a Camera with URP camera data on the runtime's `View` object (or adopts the app's camera with
// UseCamera); LookTargets.DefaultCamera follows it (look target type 0).
//   pose  from `scene.camera.current`: position = pivot + Euler(rot)·(0, 0, −distance), rotation = Euler(rot),
//         fieldOfView = fov; current null → HS2's reset view (StudioScene.Start: pivot (0, 10, 0), rot (15, 180, 0),
//         distance 25, fov 23; Phase 3.3 E4, replaces the provisional O7 values)
//   node  an active camera node overrides: every LateApply the camera copies the node holder's world position and
//         rotation, and its `fov` when non-null; the model's `current` is not changed. When no node is active the view
//         returns to `current`.
//   slot  a winning `camera` slot of the runtime override layer (Phase 6.2) is above both: every LateApply the camera is
//         placed at target + rotation·(0, 0, −distance) with that rotation and field of view; when no slot wins any more the
//         view returns to the active camera node or `current`.
// SceneCharaLight: a directional Light child of the view camera, local Euler (rot[0], rot[1], 0) (HS2's CameraLightCtrl
// rotates `Cam Light` itself), folded like the Studio lights (Phase 3.3, colour =
// rgb × intensity, intensity 1, the state values on BundleLightOriginal), shadows Soft/None with strength 1 (HS2's
// `Cam Light`), per-light biases 3 / 0.75 in URP units (HS2's 0.1 / 0.3 are Built-in units), HS2's culling mask (RenderingLayers.CharaLightCullingMask:
// Chara and MapNoShadow, not Default or Map) and the matching rendering layers (Phase 3.2).
// Render settings (Phase 3.3): the camera's culling mask, clear flags,
// background, HDR, post-processing, AA, shadows and volume mask come from `Setup` (HS2Next.ViewSetup), applied in LateApply
// whenever its Version changed (B, C and S set its members; captures apply it with forCapture = true).
// Neither writes the model. The view camera also supplies the scene thumbnail renderer of SceneOps saves.
using HS2Next.Motion.Look;
using HS2Next.Save;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace HS2Next.Scenes.Runtime
{
    public sealed class SceneViewCamera : ISceneRuntimePart
    {
        public static readonly Vector3 DefaultPivot = new Vector3(0f, 10f, 0f);
        /// <summary>HS2's reset view (StudioScene.Start → SetCamera; FOV = CameraControl's reset default; E4).</summary>
        public static readonly Vector3 DefaultRot = new Vector3(15f, 180f, 0f);
        public const float DefaultDistance = 25f, DefaultFov = 23f;

        /// <summary>The reset view as a model value (a new object each call).</summary>
        public static CameraView ResetView() => new CameraView
        {
            Pivot = new Vec3(DefaultPivot.x, DefaultPivot.y, DefaultPivot.z),
            Rot = new Vec3(DefaultRot.x, DefaultRot.y, DefaultRot.z),
            Distance = DefaultDistance,
            Fov = DefaultFov,
        };
        /// <summary>Clip planes of a created camera (scene units, 1 = 0.1 m). Far = HS2's run-time far plane 15000 (the
        /// Cinemachine lens of CameraControl, vanilla; package H's H10, decision E20 updated); near stays 0.08 (HS2 0.1).</summary>
        public const float NearClip = 0.08f, FarClip = 15000f;

        SceneRuntime runtime;
        GameObject view;
        bool created, adopted, viewDirty = true, activeDirty = true, overrideShown;
        string activeId;

        /// <summary>The object that holds the created camera (the runtime's `View`).</summary>
        public GameObject View => view;
        /// <summary>The camera that shows the scene (created or adopted).</summary>
        public Camera Camera { get; private set; }
        /// <summary>The active camera node the view follows, or null.</summary>
        public string ActiveCameraId => activeId;
        /// <summary>The view's render settings (Phase 3.3 seam); applied to <see cref="Camera"/> in LateApply when changed.</summary>
        public ViewSetup Setup { get; } = new ViewSetup();
        int appliedSetupVersion;

        /// <summary>A part: the camera is created on <see cref="SceneRuntime.ViewRoot"/> at <see cref="Attach"/>.</summary>
        public SceneViewCamera() { }

        /// <summary>Standalone: creates a `View` object with a camera under <paramref name="parent"/> (null = scene root).</summary>
        public SceneViewCamera(Transform parent)
        {
            var go = new GameObject("View");
            if (parent) go.transform.SetParent(parent, false);
            Create(go);
        }

        void Create(GameObject go)
        {
            view = go;
            var cam = go.GetComponent<Camera>();
            if (!cam)
            {
                cam = go.AddComponent<Camera>();
                cam.nearClipPlane = NearClip;
                cam.farClipPlane = FarClip;
                cam.fieldOfView = DefaultFov;
                created = true;
            }
            Use(cam);
        }

        /// <summary>
        /// Shows the scene through the app's camera instead of the created one (which is disabled). The adopted camera is
        /// not reparented; the chara light moves under it at its next application.
        /// </summary>
        public void UseCamera(Camera cam)
        {
            if (!cam || cam == Camera) return;
            if (Camera && created && !adopted) Camera.enabled = false;
            adopted = true;
            Use(cam);
            viewDirty = true;
        }

        void Use(Camera cam)
        {
            Camera = cam;
            if (!cam.GetComponent<UniversalAdditionalCameraData>()) cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            LookTargets.DefaultCamera = cam.transform;
            ViewSetup.Register(cam, Setup);
            appliedSetupVersion = 0;
            ApplySetup();
        }

        /// <summary>Applies `scene.camera.current` (null → the default view).</summary>
        public void ApplyView(CameraView current)
        {
            if (!Camera) return;
            Vector3 pivot = current != null ? new Vector3(current.Pivot.X, current.Pivot.Y, current.Pivot.Z) : DefaultPivot;
            Vector3 euler = current != null ? new Vector3(current.Rot.X, current.Rot.Y, current.Rot.Z) : DefaultRot;
            float distance = current != null ? current.Distance : DefaultDistance;
            float fov = current != null ? current.Fov : DefaultFov;
            var rot = Quaternion.Euler(euler);
            Camera.transform.SetPositionAndRotation(pivot + rot * new Vector3(0f, 0f, -distance), rot);
            Camera.fieldOfView = fov;
        }

        /// <summary>The camera slot's winner changed or went (the override layer): the next LateApply places the view again.</summary>
        public void OverrideChanged() => viewDirty = true;

        /// <summary>True while a camera slot of the override layer places the view.</summary>
        public bool Overridden => overrideShown;

        /// <summary>Follows an active camera node: its holder's world pose, and its field of view when set.</summary>
        public void FollowNode(Transform holder, float? fov)
        {
            if (!Camera || !holder) return;
            Camera.transform.SetPositionAndRotation(holder.position, holder.rotation);
            if (fov.HasValue) Camera.fieldOfView = fov.Value;
        }

        // ------------------------------------------------------------------ ISceneRuntimePart

        public void Attach(SceneRuntime r)
        {
            runtime = r;
            if (!Camera) Create(r.ViewRoot ? r.ViewRoot.gameObject : r.Root);
            viewDirty = activeDirty = true;
            if (r.Ops != null && r.Ops.ThumbnailRenderer == null) r.Ops.ThumbnailRenderer = CaptureScene;
        }

        /// <summary>The scene thumbnail: the view brought up to date (an active camera node included), then
        /// ThumbnailCapture.CaptureView of the camera (504 × 704 RGB PNG) with the item gimmick icons and the light helpers
        /// hidden and the view's ViewSetup applied for capture. Needs a graphics device.</summary>
        public byte[] CaptureScene()
        {
            Apply();
            LateApply();
            using (ItemIcons.HideForCapture())   // gimmick icons and light helpers are never captured
            using (LightHelpers.HideForCapture())
                return ThumbnailCapture.CaptureView(Camera, new ThumbnailSettings { CharaLighting = false, Configure = cam => Setup.ApplyTo(cam, true) });   // the view's effects
        }

        public void OnChanged(SceneChangeBatch b)
        {
            foreach (var c in b.Changes)
            {
                switch (c.Kind)
                {
                    case SceneChangeKind.SceneChanged:
                        if (c.Path == null || FieldPath.Overlaps(c.Path, "camera")) viewDirty = true;
                        break;
                    case SceneChangeKind.NodeChanged:
                        if (c.Path == null || FieldPath.Overlaps(c.Path, "camera")) activeDirty = true;
                        break;
                    case SceneChangeKind.NodeAdded:
                    case SceneChangeKind.NodeRemoved:
                        activeDirty = true;
                        break;
                }
            }
        }

        public void Apply()
        {
            if (!activeDirty || runtime == null) return;
            activeDirty = false;
            string id = null;
            foreach (var n in runtime.Model.PreOrder())
                if (n.Kind == NodeKind.Camera && n.InModel && n.State is CameraNodeState cs && cs.Active) { id = n.Id; break; }
            if (id != activeId)
            {
                activeId = id;
                if (id == null) viewDirty = true;   // back to `current`
            }
        }

        /// <summary>Writes <see cref="Setup"/> to the camera when its version changed; records the view's pixel height.</summary>
        public void ApplySetup()
        {
            if (!Camera) return;
            Setup.SetReferenceHeight(Camera.pixelHeight);
            if (appliedSetupVersion == Setup.Version) return;
            Setup.ApplyTo(Camera, false);
            appliedSetupVersion = Setup.Version;
        }

        public void LateApply()
        {
            if (runtime == null || !Camera) return;
            ApplySetup();
            if (runtime.OverrideApp.TryCamera(out var target, out var rot, out float dist, out float fov))
            {
                Camera.transform.SetPositionAndRotation(target + rot * new Vector3(0f, 0f, -dist), rot);
                Camera.fieldOfView = fov;
                overrideShown = true;
                return;
            }
            if (overrideShown)
            {
                overrideShown = false;
                viewDirty = true;   // back to the active camera node or `current`
            }
            if (activeId != null)
            {
                var holder = runtime.HolderOf(activeId);
                var node = runtime.Model.Find(activeId);
                if (holder && node != null && node.InModel)
                {
                    FollowNode(holder, (node.State as CameraNodeState)?.Fov);
                    return;
                }
            }
            if (!viewDirty) return;
            viewDirty = false;
            ApplyView(runtime.Model.Settings.Camera.Current);
        }

        public void Clear()
        {
            activeId = null;
            overrideShown = false;
            viewDirty = activeDirty = true;
        }

        public void OnLoadedRealized() => viewDirty = activeDirty = true;

        /// <summary>Destroy in play mode, DestroyImmediate in edit mode (batch tests).</summary>
        internal static void Kill(Object o)
        {
            if (!o) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        /// <summary>Removes what this object created: the camera (and its URP data) it added, or the standalone `View`
        /// object. An adopted camera stays. A runtime's Dispose destroys its `View` anyway.</summary>
        public void Destroy()
        {
            if (Camera && LookTargets.DefaultCamera == Camera.transform) LookTargets.DefaultCamera = null;
            if (Camera) ViewSetup.Register(Camera, null);
            if (runtime == null) Kill(view);
            else if (created && view)
            {
                var cam = view.GetComponent<Camera>();
                if (cam) { Kill(cam.GetComponent<UniversalAdditionalCameraData>()); Kill(cam); }
            }
            view = null;
            Camera = null;
            created = false;
        }
    }

    public sealed class SceneCharaLight : ISceneRuntimePart
    {
        /// <summary>
        /// The chara light's per-light shadow biases in URP units. HS2's `Cam Light` keeps 0.1 / 0.3 in Built-in units; the same
        /// numbers in URP's per-light fields give about a tenth of the depth bias and self-shadow streaks along the edge loops of
        /// curved skin and clothes. 3 / 0.75 matches the Built-in self shadow of the same characters closest without more light
        /// leaking at contacts.
        /// </summary>
        public const float CharaLightShadowBias = 3f, CharaLightShadowNormalBias = 0.75f;

        SceneRuntime runtime;
        GameObject go;
        bool dirty = true;

        public Light Light { get; private set; }

        /// <summary>Applies `scene.charaLight` under <paramref name="camera"/> (creates the light on first use).</summary>
        public void Apply(Camera camera, CharaLightSettings s)
        {
            if (!camera || s == null) return;
            if (!go)
            {
                go = new GameObject("CharaLight");
                Light = go.AddComponent<Light>();
                Light.type = LightType.Directional;
            }
            if (go.transform.parent != camera.transform) go.transform.SetParent(camera.transform, false);
            float rx = s.Rot != null && s.Rot.Length > 0 ? s.Rot[0] : 0f;
            float ry = s.Rot != null && s.Rot.Length > 1 ? s.Rot[1] : 0f;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(rx, ry, 0f);
            if (!Light.TryGetComponent<BundleLightOriginal>(out var o)) { o = go.AddComponent<BundleLightOriginal>(); o.Light = Light; }
            o.Color = new Color(s.Color.R, s.Color.G, s.Color.B, s.Color.A);
            o.Intensity = s.Intensity;
            Light.color = BundleLights.Folded(new Color(s.Color.R, s.Color.G, s.Color.B, 1f), s.Intensity);
            Light.intensity = 1f;
            Light.shadows = s.Shadow ? LightShadows.Soft : LightShadows.None;
            Light.shadowStrength = 1f;
            Light.cullingMask = RenderingLayers.CharaLightCullingMask;
            RenderingLayers.ApplyToLight(Light);
            // Per-light biases in URP units (per-light 1 / 1 renders bit-equal to the asset's 1 / 1). URP scales them by the
            // cascade's texel size and the soft-shadow kernel, Built-in by its own rule, so HS2's 0.1 / 0.3 are not copied as numbers.
            Light.GetComponent<UniversalAdditionalLightData>().usePipelineSettings = false;
            Light.shadowBias = CharaLightShadowBias;
            Light.shadowNormalBias = CharaLightShadowNormalBias;
            MainLight.SetCharaLight(Light);   // the main-light rule (Phase 3.2)
        }

        // ------------------------------------------------------------------ ISceneRuntimePart

        public void Attach(SceneRuntime r)
        {
            runtime = r;
            dirty = true;
        }

        public void OnChanged(SceneChangeBatch b)
        {
            if (b.Touches(null, "charaLight")) dirty = true;
        }

        void ISceneRuntimePart.Apply() { }

        public void LateApply()
        {
            if (runtime == null) return;
            var cam = runtime.GetPart<SceneViewCamera>()?.Camera;
            if (!cam) return;
            if (!dirty && go && go.transform.parent == cam.transform) return;
            dirty = false;
            Apply(cam, runtime.Model.Settings.CharaLight);
        }

        public void Clear() => dirty = true;

        public void OnLoadedRealized() => dirty = true;

        public void Destroy()
        {
            SceneViewCamera.Kill(go);
            go = null;
            Light = null;
        }
    }
}
