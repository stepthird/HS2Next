// Thumbnail rendering: a dedicated camera renders 504 × 704
// into an sRGB ARGB32 RenderTexture (in this Linear project URP's final blit encodes linear → sRGB,
// so the bytes are display-encoded; Editor/CompareTest.cs uses the same path), twice because the
// first frame after setup can be empty, with SMAA; ReadPixels into an RGB24 texture and
// EncodeToPNG give an opaque 8-bit RGB PNG. Character and coordinate thumbnails use HS2's coordinate
// shot (camera at (0, 10.2, 45.6) female / (0, 10.5, 46.9) male, rotation (2.62, −180, 0), the
// character at the origin facing +Z) with HS2's 23° field of view narrowed to the 504 × 704 centre
// crop of its 1280 × 720 render (22.502°), a solid sRGB 128 grey background, a new scene's chara light
// (white directional light under the camera, intensity 1, shadows) and ambient colours, no map.
// Screen effects are not applied yet (Phase 3.1 brings the effect stack). Scene thumbnails take the
// current view's pose and vertical field of view (the image is the centre crop of that view). Posing
// the character (Studio defaults: no animation, default expression, eyes and neck forward) is the
// caller's business. CaptureTexture returns the RGB24 texture the PNG is encoded from (previews). Main thread only.
// Isolated captures (character and outfit thumbnails) take nothing from the scene: no volume reaches the
// camera (its volume layer mask is empty, the post-processing stack keeps the project defaults), and the capture's own
// environment replaces the scene's for the render: the ambient probe and the default reflection both come from the
// new-scene ambient colours (sky above, equator at the horizon, ground below), reflection intensity 1; everything is put
// back afterwards (the scene's ambient probe included).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace HS2Next.Save
{
    public sealed class ThumbnailSettings
    {
        /// <summary>Opaque background, sRGB (default 128 grey; a setting).</summary>
        public Color32 Background = new Color32(128, 128, 128, 255);
        /// <summary>Render pipeline to use for the capture (null = the active one).</summary>
        public RenderPipelineAsset Pipeline;
        /// <summary>Adds the chara light and the default ambient colours for the capture (chara/coord thumbnails; off for scenes).</summary>
        public bool CharaLighting = true;
        public float NearClip = 0.08f;
        public float FarClip = 1000f;
        /// <summary>Layers the camera renders (default: everything except UI).</summary>
        public int CullingMask = ~(1 << 5);
        /// <summary>Called on the capture camera after its setup, before rendering (Phase 3.3: scene thumbnails apply the
        /// view's ViewSetup, so they show the scene's effects).</summary>
        public Action<Camera> Configure;
        /// <summary>The capture takes nothing from the scene (character and outfit thumbnails): no volumes, the capture's own
        /// ambient probe and default reflection (Phase 4.6). Off for scene thumbnails, which show the scene.</summary>
        public bool Isolated;
    }

    public static class ThumbnailCapture
    {
        public const int Width = 504, Height = 704;
        /// <summary>HS2's Maker card camera field of view (degrees, over its 720-pixel render height).</summary>
        public const float Hs2Fov = 23f;

        public static readonly Vector3 CoordCameraFemale = new Vector3(0f, 10.2f, 45.6f);
        public static readonly Vector3 CoordCameraMale = new Vector3(0f, 10.5f, 46.9f);
        public static readonly Vector3 CoordCameraEuler = new Vector3(2.62f, -180f, 0f);

        // A new scene's ambient colours (sRGB bytes /255).
        static readonly Color AmbientSky = new Color(170 / 255f, 188 / 255f, 243 / 255f);
        static readonly Color AmbientEquator = new Color(185 / 255f, 195 / 255f, 205 / 255f);
        static readonly Color AmbientGround = new Color(204 / 255f, 109 / 255f, 41 / 255f);

        static SphericalHarmonicsL2? ownProbe;
        static Cubemap ownReflection;

        /// <summary>The environment of an isolated capture as radiance: the sky colour above, the equator colour at the horizon, the
        /// ground colour below, blended linearly with the height of the direction (linear colour values).</summary>
        static Color Environment(float y)
        {
            Color sky = AmbientSky.linear, eq = AmbientEquator.linear, ground = AmbientGround.linear;
            return y >= 0f ? Color.Lerp(eq, sky, y) : Color.Lerp(eq, ground, -y);
        }

        /// <summary>The ambient probe of an isolated capture: the environment projected onto L2 spherical harmonics as diffuse
        /// lighting (a uniform environment of colour c gives c), from a fixed 64 × 128 grid of directions.</summary>
        public static SphericalHarmonicsL2 OwnAmbientProbe()
        {
            if (ownProbe.HasValue) return ownProbe.Value;
            var sh = new SphericalHarmonicsL2();
            const int rows = 64, cols = 128;
            double total = 0;
            var dirs = new List<(Vector3 d, float w)>(rows * cols);
            for (int i = 0; i < rows; i++)
            {
                double theta = Math.PI * (i + 0.5) / rows;               // from +Y down
                double sinT = Math.Sin(theta), cosT = Math.Cos(theta);
                for (int j = 0; j < cols; j++)
                {
                    double phi = 2 * Math.PI * (j + 0.5) / cols;
                    dirs.Add((new Vector3((float)(sinT * Math.Cos(phi)), (float)cosT, (float)(sinT * Math.Sin(phi))), (float)sinT));
                    total += sinT;
                }
            }
            // the irradiance of the environment over π: Σ L(ω) max(0, n·ω) dω / π with dω = 4π · w / Σw
            foreach (var (d, w) in dirs)
                sh.AddDirectionalLight(d, Environment(d.y), (float)(4.0 * w / total));
            ownProbe = sh;
            return sh;
        }

        /// <summary>The default reflection of an isolated capture: a cubemap of the environment (the sky colour on the face
        /// above, the ground colour below, the equator colour on the four sides), 64 px with mipmaps.</summary>
        public static Cubemap OwnReflection()
        {
            if (ownReflection) return ownReflection;
            const int size = 64;
            var c = new Cubemap(size, TextureFormat.RGBA32, true) { name = "hs2next_thumbnail_environment", hideFlags = HideFlags.HideAndDontSave };
            void Face(CubemapFace f, Color col)
            {
                var px = new Color[size * size];
                for (int i = 0; i < px.Length; i++) px[i] = col;
                c.SetPixels(px, f);
            }
            Face(CubemapFace.PositiveY, AmbientSky);
            Face(CubemapFace.NegativeY, AmbientGround);
            Face(CubemapFace.PositiveX, AmbientEquator);
            Face(CubemapFace.NegativeX, AmbientEquator);
            Face(CubemapFace.PositiveZ, AmbientEquator);
            Face(CubemapFace.NegativeZ, AmbientEquator);
            c.Apply(true, false);
            ownReflection = c;
            return c;
        }

        /// <summary>Vertical field of view of the 504 × 704 centre crop of a 1280 × 720 render: 2·atan(tan(fov/2)·704/720).</summary>
        public static float CropFov(float fov) =>
            2f * Mathf.Atan(Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * Height / 720f) * Mathf.Rad2Deg;

        /// <summary>HS2's coordinate shot for a character of <paramref name="sex"/> (0 male, 1 female) standing at the origin facing +Z.</summary>
        public static byte[] CaptureCoordinateShot(int sex, ThumbnailSettings settings = null) =>
            Capture(sex == 0 ? CoordCameraMale : CoordCameraFemale, Quaternion.Euler(CoordCameraEuler), CropFov(Hs2Fov), settings);

        /// <summary>The current view of <paramref name="view"/> (pose and vertical field of view); scene thumbnails and editor saves.</summary>
        public static byte[] CaptureView(Camera view, ThumbnailSettings settings = null)
        {
            var s = settings ?? new ThumbnailSettings { CharaLighting = false };
            return Capture(view.transform.position, view.transform.rotation, view.fieldOfView, s, view.nearClipPlane, view.farClipPlane);
        }

        /// <summary>Renders 504 × 704 from the given pose and vertical field of view and returns an 8-bit RGB PNG.</summary>
        public static byte[] Capture(Vector3 position, Quaternion rotation, float verticalFov, ThumbnailSettings settings = null, float? near = null, float? far = null)
        {
            var tex = CaptureTexture(position, rotation, verticalFov, settings, near, far);
            try { return tex.EncodeToPNG(); }
            finally { Object.DestroyImmediate(tex); }
        }

        /// <summary>
        /// As <see cref="Capture(Vector3, Quaternion, float, ThumbnailSettings, float?, float?)"/>, but returns the 504 × 704 RGB24
        /// texture the PNG is encoded from (no mipmaps, applied); the caller destroys it (previews).
        /// </summary>
        public static Texture2D CaptureTexture(Vector3 position, Quaternion rotation, float verticalFov, ThumbnailSettings settings = null, float? near = null, float? far = null)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("thumbnail capture needs a graphics device (use ThumbnailPlaceholder headless)");
            var s = settings ?? new ThumbnailSettings();
            var prevDefault = GraphicsSettings.defaultRenderPipeline;
            var prevQuality = QualitySettings.renderPipeline;
            var prevAmbientMode = RenderSettings.ambientMode;
            Color prevSky = RenderSettings.ambientSkyColor, prevEq = RenderSettings.ambientEquatorColor, prevGround = RenderSettings.ambientGroundColor;
            float prevIntensity = RenderSettings.ambientIntensity;
            var prevProbe = RenderSettings.ambientProbe;
            var prevReflMode = RenderSettings.defaultReflectionMode;
            var prevRefl = RenderSettings.customReflectionTexture;
            float prevReflIntensity = RenderSettings.reflectionIntensity;
            GameObject camGo = null;
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                if (s.Pipeline != null)
                {
                    GraphicsSettings.defaultRenderPipeline = s.Pipeline;
                    QualitySettings.renderPipeline = s.Pipeline;
                }
                camGo = new GameObject("hs2next_thumbnail_camera") { hideFlags = HideFlags.HideAndDontSave };
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.transform.SetPositionAndRotation(position, rotation);
                cam.fieldOfView = verticalFov;
                cam.nearClipPlane = near ?? s.NearClip;
                cam.farClipPlane = far ?? s.FarClip;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = (Color)s.Background;
                cam.cullingMask = s.CullingMask;
                cam.allowHDR = false;
                var urp = camGo.AddComponent<UniversalAdditionalCameraData>();
                urp.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                urp.antialiasingQuality = AntialiasingQuality.High;
                urp.renderPostProcessing = true;
                if (s.Isolated)
                {
                    urp.volumeLayerMask = 0;   // no scene volume (a map's or the app's effects) reaches the capture
                    urp.volumeTrigger = camGo.transform;
                }
                if (s.CharaLighting)
                {
                    var lightGo = new GameObject("hs2next_thumbnail_chara_light") { hideFlags = HideFlags.HideAndDontSave };
                    lightGo.transform.SetParent(camGo.transform, false);   // the chara light sits under the camera, local Euler (0, 0, 0)
                    var l = lightGo.AddComponent<Light>();
                    l.type = LightType.Directional;
                    l.color = Color.white;
                    l.intensity = 1f;
                    l.shadows = LightShadows.Soft;
                    RenderingLayers.ApplyToLight(l);   // all layers (culling mask everything), also under rendering layers
                    RenderSettings.ambientMode = AmbientMode.Trilight;
                    RenderSettings.ambientSkyColor = AmbientSky;
                    RenderSettings.ambientEquatorColor = AmbientEquator;
                    RenderSettings.ambientGroundColor = AmbientGround;
                    RenderSettings.ambientIntensity = 1f;
                }
                if (s.Isolated)
                {
                    // the capture's own environment instead of the scene's (a map scene is the active scene: its probe and
                    // reflection would apply otherwise); the renderers use the ambient probe (no light probes, CharaThumbnail)
                    RenderSettings.ambientMode = AmbientMode.Custom;
                    RenderSettings.ambientProbe = OwnAmbientProbe();
                    RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                    RenderSettings.customReflectionTexture = OwnReflection();
                    RenderSettings.reflectionIntensity = 1f;
                }
                s.Configure?.Invoke(cam);
                rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
                cam.targetTexture = rt;
                cam.aspect = Width / (float)Height;
                cam.Render();
                cam.Render();
                var prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                RenderTexture.active = prevActive;
                cam.targetTexture = null;
                var done = tex;
                tex = null;   // handed to the caller
                return done;
            }
            finally
            {
                if (tex != null) Object.DestroyImmediate(tex);
                if (rt != null) Object.DestroyImmediate(rt);
                if (camGo != null) Object.DestroyImmediate(camGo);
                RenderSettings.ambientMode = prevAmbientMode;
                RenderSettings.ambientSkyColor = prevSky;
                RenderSettings.ambientEquatorColor = prevEq;
                RenderSettings.ambientGroundColor = prevGround;
                RenderSettings.ambientIntensity = prevIntensity;
                RenderSettings.ambientProbe = prevProbe;   // a mode change recomputes the probe; the scene's own comes back
                if (s.Isolated)
                {
                    RenderSettings.defaultReflectionMode = prevReflMode;
                    RenderSettings.customReflectionTexture = prevRefl;
                    RenderSettings.reflectionIntensity = prevReflIntensity;
                }
                if (s.Pipeline != null)
                {
                    GraphicsSettings.defaultRenderPipeline = prevDefault;
                    QualitySettings.renderPipeline = prevQuality;
                }
            }
        }
    }
}
