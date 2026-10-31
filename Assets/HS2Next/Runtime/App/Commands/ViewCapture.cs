// The view capture of the bridge's `capture` and of the render tests: since Phase 3.6
// (package C) a thin front of CaptureService (Runtime/Rendering/Capture), which owns the
// render: a dedicated hidden camera copies the view camera's world pose, field of view and clip planes; aspect = width /
// height with the projection matrix reset (bridge prep C3); the scene's own lights (the chara light under the view camera
// included), no lighting override; the view's ViewSetup for capture (Phase 3.3 E12: helper layers masked, clear flags,
// background, HDR, post-processing, render shadows, the effect volume mask; a camera without a setup copies its mask /
// clear flags / background and renders HDR with post-processing). AA: SMAA High (`smaa`, the default) or no post AA
// (`msaa4`, `none`), the target's MSAA 4 for `msaa4`; an sRGB ARGB32 target rendered twice, RGB 8-bit PNG. Item gimmick
// icons and (3.6) light helpers are hidden during the render; bloom is mapped for the render size (3.6) when the view's
// runtime is known; the scene's background is drawn, the frame only when asked (`capture` default false). Supersampling
// 1 / 2 / 4 and the pixel cap (CaptureLimits) as the screenshot's. Main thread, graphics device needed.
using System;
using HS2Next.Scenes.Runtime;
using HS2Next.Screenshots;
using UnityEngine;

namespace HS2Next.App.Commands
{
    public static class ViewCapture
    {
        public const string Smaa = CaptureService.Smaa, Msaa4 = CaptureService.Msaa4, None = CaptureService.None;

        /// <summary>True when a graphics device exists (headless runs answer unsupported).</summary>
        public static bool Available => CaptureService.Available;

        /// <summary>
        /// Renders <paramref name="view"/>'s current pose at <paramref name="width"/> × <paramref name="height"/> and returns an
        /// 8-bit RGB PNG (tests; bounded by the hard cap of 33.2 Mpix only).
        /// </summary>
        public static byte[] Render(Camera view, int width, int height, string aa = Smaa) =>
            Render(SceneImages.RuntimeOf(view), view, new CaptureRequest { Width = width, Height = height, Aa = aa, MaxPixels = CaptureLimits.HardMaxPixels }).Png;

        /// <summary>The full request (supersampling, frame, cap) with the runtime whose effects map bloom for the render size.</summary>
        public static CaptureResult Render(SceneRuntime runtime, Camera view, CaptureRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return CaptureService.RenderSync(runtime ?? SceneImages.RuntimeOf(view), view, request);
        }
    }
}
