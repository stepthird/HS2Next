// StudioOps, scene images (Phase 3.6 package C): the scene's background and frame
// (`scene.background`, `scene.frame`, file refs or null = none), one entry each ("Background", "Frame"); both in one call
// = one entry "Background". Picked paths become references through FileRefs.FromPicked (DefaultData referenced, other
// files embedded unless images.embed is off). A missing file is not refused here: the reference is stored and the part
// SceneImages reports it (R19); the result carries the warning.
using System.Collections.Generic;
using HS2Next.Save;

namespace HS2Next.Scenes.Runtime
{
    public sealed partial class StudioOps
    {
        /// <summary>The scene background (null = none), one entry "Background".</summary>
        public OpResult SetBackground(FileRef f, ExecuteOptions o = default) => SetSceneImages(true, f, false, null, o);

        /// <summary>The scene frame (null = none), one entry "Frame".</summary>
        public OpResult SetFrame(FileRef f, ExecuteOptions o = default) => SetSceneImages(false, null, true, f, o);

        /// <summary>
        /// Background and / or frame in one entry (label "Background" when the background is set, else "Frame"); Changed / NoOp
        /// list "background", "frame". Warnings for references whose file is missing (the reference is stored anyway).
        /// </summary>
        public OpResult SetSceneImages(bool setBackground, FileRef background, bool setFrame, FileRef frame, ExecuteOptions o = default)
        {
            if (!setBackground && !setFrame) return OpResult.Fail(CommandCodes.BadArgs, "missing argument 'background'", "background", "one of background, frame");
            var plan = new List<(string, object)>();
            if (setBackground) plan.Add(("background", background));
            if (setFrame) plan.Add(("frame", frame));
            var r = SetMany(new string[] { null }, plan, setBackground ? Labels.Background : Labels.Frame, o, p => p);
            if (!r.Ok) return r;
            var images = SceneImages.Ensure(rt);
            images?.ApplyNow();
            if (images != null)
            {
                if (setBackground && background != null && images.Background.State == SceneImages.StateMissing) r.Warnings.Add("background: " + images.Background.Error + " (scene.backgroundMissing)");
                if (setFrame && frame != null && images.Frame.State == SceneImages.StateMissing) r.Warnings.Add("frame: " + images.Frame.Error + " (scene.frameMissing)");
                if (setBackground && background != null && images.Background.State == SceneImages.StateFailed) r.Warnings.Add("background: " + images.Background.Error + " (scene.imageDecodeFailed)");
                if (setFrame && frame != null && images.Frame.State == SceneImages.StateFailed) r.Warnings.Add("frame: " + images.Frame.Error + " (scene.imageDecodeFailed)");
            }
            return r;
        }
    }
}
