// The view camera's render settings as one object (Phase 3.3
// seam of package A). SceneViewCamera owns one instance (SceneViewCamera.Setup) and applies it to the view camera in its
// LateApply whenever Version changed; the capture paths (ViewCapture, the scene thumbnail) apply the same instance to
// their own camera with forCapture = true, so a capture renders what the view shows without the helper layers.
// Members are set by their owners: CullingMask (A; HS2's main-camera mask, E10), HelperMask (A: the light helpers'
// layer, view only), ClearFlags / Background (S: sky on → Skybox, else SolidColor with render.backgroundColor),
// PostProcessing / VolumeMask / RenderShadows (B: effects, self shadow), Antialiasing / AntialiasingQuality (S: render.aa;
// captures always SMAA High, E12), Hdr (on, as HS2's camera). ReferenceHeight is the view's pixel height, written by the
// view only (size-dependent passes scale by frame height / ReferenceHeight). Every setter that changes a value
// bumps Version. Of(camera) finds the setup of a view camera (null for any other camera). Main thread.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace HS2Next
{
    public sealed class ViewSetup
    {
        /// <summary>
        /// HS2 Studio's main-camera culling mask: layers 0, 1, 2, 4, 8, 10–15, 19–22, 29 (not 5 UI, 27 Studio Col, 28
        /// Studio Select, 30 Studio Route).
        /// </summary>
        public const int Hs2MainCullingMask =
            1 << 0 | 1 << 1 | 1 << 2 | 1 << 4 | 1 << 8 | 1 << 10 | 1 << 11 | 1 << 12 | 1 << 13 | 1 << 14 | 1 << 15 |
            1 << 19 | 1 << 20 | 1 << 21 | 1 << 22 | 1 << 29;

        /// <summary>HS2's "Studio Select" layer (28): the directional light's arrow and HS2Next's light helpers (view only).</summary>
        public const int HelperLayer = 28;

        /// <summary>HS2's PostProcessLayer volume layer (8); the effect Volume's GameObject layer.</summary>
        public const int EffectVolumeLayer = 8;

        int cullingMask = Hs2MainCullingMask, helperMask = 1 << HelperLayer, volumeMask = 1 << EffectVolumeLayer, referenceHeight;
        CameraClearFlags clearFlags = CameraClearFlags.SolidColor;
        Color background = new Color(0f, 0f, 0f, 1f);
        bool postProcessing = true, renderShadows = true, hdr = true;
        AntialiasingMode aa = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        AntialiasingQuality aaQuality = AntialiasingQuality.High;

        /// <summary>Incremented by every change of a member (the view re-applies when it differs from the applied one).</summary>
        public int Version { get; private set; } = 1;

        /// <summary>The scene layers the view and the captures render (default HS2's main mask).</summary>
        public int CullingMask { get => cullingMask; set => Set(ref cullingMask, value); }
        /// <summary>Layers added for the view only and removed from captures (default the helper layer 28).</summary>
        public int HelperMask { get => helperMask; set => Set(ref helperMask, value); }
        public CameraClearFlags ClearFlags { get => clearFlags; set => Set(ref clearFlags, value); }
        /// <summary>Background colour for SolidColor (render.backgroundColor; HS2's config default black).</summary>
        public Color Background { get => background; set { if (background != value) { background = value; Version++; } } }
        public bool PostProcessing { get => postProcessing; set => Set(ref postProcessing, value); }
        /// <summary>The view's anti-aliasing (render.aa); captures use SMAA High whatever this says.</summary>
        public AntialiasingMode Antialiasing { get => aa; set => Set(ref aa, value); }
        public AntialiasingQuality AntialiasingQuality { get => aaQuality; set => Set(ref aaQuality, value); }
        /// <summary>Self shadow (effects.shadows.enabled): off = no realtime shadows at all (view and captures).</summary>
        public bool RenderShadows { get => renderShadows; set => Set(ref renderShadows, value); }
        public bool Hdr { get => hdr; set => Set(ref hdr, value); }
        /// <summary>The URP volume layer mask of the view and capture cameras (default layer 8).</summary>
        public int VolumeMask { get => volumeMask; set => Set(ref volumeMask, value); }
        /// <summary>The view's pixel height (0 until the view rendered); written by the view only.</summary>
        public int ReferenceHeight => referenceHeight;

        /// <summary>The view sets its pixel height (not a Version change: it is read, not applied).</summary>
        internal void SetReferenceHeight(int h) { if (h > 0) referenceHeight = h; }

        void Set<T>(ref T field, T value)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            Version++;
        }

        /// <summary>The culling mask a camera gets: view = CullingMask | HelperMask, capture = CullingMask without HelperMask.</summary>
        public int MaskFor(bool forCapture) => forCapture ? cullingMask & ~helperMask : cullingMask | helperMask;

        /// <summary>
        /// Writes the settings to <paramref name="cam"/> (adds URP camera data when missing): culling mask, clear flags,
        /// background, HDR, URP post-processing, anti-aliasing (captures: SMAA High), render shadows, volume mask.
        /// Pose, field of view and clip planes are the caller's.
        /// </summary>
        public void ApplyTo(Camera cam, bool forCapture)
        {
            if (!cam) return;
            cam.cullingMask = MaskFor(forCapture);
            cam.clearFlags = clearFlags;
            cam.backgroundColor = background;
            cam.allowHDR = hdr;
            if (!cam.TryGetComponent<UniversalAdditionalCameraData>(out var urp)) urp = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            urp.renderPostProcessing = postProcessing;
            urp.antialiasing = forCapture ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : aa;
            urp.antialiasingQuality = forCapture ? AntialiasingQuality.High : aaQuality;
            urp.renderShadows = renderShadows;
            urp.volumeLayerMask = volumeMask;
        }

        // ------------------------------------------------------------ lookup by camera

        static readonly Dictionary<Camera, ViewSetup> byCamera = new Dictionary<Camera, ViewSetup>();

        /// <summary>The setup of a view camera, or null (any other camera, or a destroyed one).</summary>
        public static ViewSetup Of(Camera cam)
        {
            if (!cam) return null;
            return byCamera.TryGetValue(cam, out var s) ? s : null;
        }

        /// <summary>The view registers its camera (null setup removes it).</summary>
        internal static void Register(Camera cam, ViewSetup s)
        {
            Prune();
            if (!cam) return;
            if (s == null) byCamera.Remove(cam);
            else byCamera[cam] = s;
        }

        static void Prune()
        {
            List<Camera> dead = null;
            foreach (var kv in byCamera) if (!kv.Key) (dead ??= new List<Camera>()).Add(kv.Key);
            if (dead != null) foreach (var c in dead) byCamera.Remove(c);
        }
    }
}
