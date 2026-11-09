// ScriptProbe's thumbnails in the player (Phase 6.10, YT3 (a)). Every thumbnail renders under
// CaptureService.SkinScope; each command runs at its command's point, after the player's own draw of the frame. Nothing is written as a picture.
//   scriptprobe.yt1   one moment of a path: stepped (one scene step of dt of the paused clock, "stepped" its result, then the scene thumbnail, in this frame), command (the scene thumbnail),
//                     front / view (a character's card thumbnail as a save renders it, StudioOps.RenderCharaThumbnail): the app's render, the control
//                     with the test hook off (CaptureService.RecalculateSkin false) and the reference with forceMatrixRecalculationPerRender forced on
//                     every enabled skinned renderer, each against the reference in pixels over 2 levels; the right hand's position
//   scriptprobe.yt3   one moment of JR6's fixture (scriptprobe.jr6.fixture, the wide pass off) through the scene thumbnail: the thumbnail, the
//                     thumbnail camera's mosaic step read at its begin, the member's footprint with the thumbnail's camera (eroded 1 px) and the
//                     left-out control: the numbers of scriptprobe.jr6.judge's (a)
using System;
using System.Collections.Generic;
using System.Linq;
using HS2Next.Api.Bridge;
using HS2Next.App;
using HS2Next.Chara.Mosaic;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using HS2Next.Screenshots;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Stp3.Test.ScriptProbe
{
    public sealed partial class ScriptProbePlugin
    {
        const string ThumbCamera = "hs2next_thumbnail_camera";

        void RegisterThumbs(ICommandRegistry c)
        {
            c.Register(new CommandSpec("scriptprobe.yt1", "One moment of a thumbnail path: the app's render and its hook-off control against the forced render, in memory.").WithTimeout(120)
                .Arg("path", ArgTypes.Enum("stepped", "command", "front", "view"), "the path", required: true).Arg("key", ArgTypes.String, "the character (front, view; the hand)", required: true)
                .Arg("dt", ArgTypes.Float, "the step of the stepped path"),
                a => Yt1(a.Args.Require<string>("path"), a.Args.Require<string>("key"), a.Args.Get("dt", 1f / 30f)));
            c.Register(new CommandSpec("scriptprobe.yt3", "One moment of JR6's fixture through the scene thumbnail (the wide pass off): the numbers of jr6.judge's (a).").WithTimeout(120),
                _ => Yt3());
        }

        static T HookOff<T>(Func<T> f)
        {
            bool was = CaptureService.RecalculateSkin;
            CaptureService.RecalculateSkin = false;
            try { return f(); }
            finally { CaptureService.RecalculateSkin = was; }
        }

        static Color32[] SceneThumb()
        {
            var f = Rt.Ops.ThumbnailRenderer ?? throw new BridgeException(BridgeCodes.Unsupported, "no scene thumbnail renderer");
            return Decode(f());
        }

        static Color32[] CardThumb(string key, CharaThumbnailFraming framing)
        {
            var r = AppCore.Current.Ops.RenderCharaThumbnail(key, framing);
            var t = (r.Value as CharaThumbnailRender)?.Texture;
            if (!t) return null;
            try { return t.GetPixels32(); }
            finally { Object.Destroy(t); }
        }

        static Vector3 RightHand(string key)
        {
            var ctx = Rt.FindChara(key)?.Context;
            return ctx?.BodyBones != null && ctx.BodyBones.TryGetValue("cf_J_Hand_R", out var t) && t ? t.position : Vector3.zero;
        }

        Dictionary<string, object> Yt1(string path, string key, float dt)
        {
            if (Rt.FindChara(key) == null) throw new BridgeException(BridgeCodes.NotFound, "no character " + key);
            Func<Color32[]> shot = path == "front" ? () => CardThumb(key, CharaThumbnailFraming.Front)
                                 : path == "view" ? () => CardThumb(key, CharaThumbnailFraming.View)
                                 : (Func<Color32[]>)SceneThumb;
            bool stepped = false;
            Vector3 h0 = RightHand(key);
            if (path == "stepped") stepped = Rt.Step(1, dt);   // a step steps a paused clock only
            float travel = (RightHand(key) - h0).magnitude;
            var ctrl = HookOff(shot);
            var got = shot();
            Color32[] reference = null;
            Forced(() => reference = shot());
            int app = Diff(got, reference, out int appMost), c = Diff(ctrl, reference, out int ctrlMost);
            return new Dictionary<string, object>
            {
                ["path"] = path, ["frame"] = Time.frameCount, ["pixels"] = got?.Length ?? 0, ["thumbnail"] = app, ["thumbnailMost"] = appMost, ["control"] = c, ["controlMost"] = ctrlMost,
                ["hand"] = V(RightHand(key)), ["scoped"] = CaptureService.LastSkinScopeCount, ["stepped"] = stepped, ["travel"] = R6(travel),
            };
        }

        bool yt3Armed, yt3Seen;
        float yt3Floor, yt3Least;
        int yt3Drawn, yt3Under;

        void Yt3OnBegin(ScriptableRenderContext ctx, Camera cam)
        {
            if (!yt3Armed || !cam || cam.name != ThumbCamera) return;
            var f = MosaicCamera.FrameOf(cam);
            if (f == null) return;
            yt3Seen = true;
            yt3Floor = f.FloorPx;
            yt3Drawn = 0; yt3Under = 0; yt3Least = float.MaxValue;
            foreach (var g in f.Groups)
                foreach (var mm in g.Members)
                {
                    yt3Drawn++;
                    if (mm.BlockPx < f.FloorPx) yt3Under++;
                    yt3Least = Mathf.Min(yt3Least, mm.BlockPx);
                }
        }

        Dictionary<string, object> Yt3()
        {
            if (fix == null) throw new BridgeException(BridgeCodes.BadArgs, "no fixture");
            int w = ThumbnailCapture.Width, h = ThumbnailCapture.Height;
            Color32[] cap;
            RenderPipelineManager.beginCameraRendering += Yt3OnBegin;
            yt3Armed = true; yt3Seen = false;
            try { cap = SceneThumb(); }
            finally { yt3Armed = false; RenderPipelineManager.beginCameraRendering -= Yt3OnBegin; }
            var m = new Jr6Moment
            {
                Path = "thumbnail", Centre = fix.Centre, W = w, H = h, CaptureFrame = yt3Seen, Floor = yt3Floor, Drawn = yt3Drawn, Under = yt3Under, LeastBlock = yt3Least,
                View = jr6Chosen.view, ChoiceFp = jr6Chosen.fp,
            };
            var view = ViewCam;
            if (!jr6Cam) jr6Cam = NewCam();
            jr6Cam.transform.SetPositionAndRotation(view.transform.position, view.transform.rotation);
            jr6Cam.fieldOfView = view.fieldOfView;
            jr6Cam.nearClipPlane = view.nearClipPlane;
            jr6Cam.farClipPlane = view.farClipPlane;
            Forced(() => m.Fp = Footprint(jr6Cam, w, h));
            m.Fpn = m.Fp.Count(x => x);
            WithoutMosaic(fix.Marker, () => Forced(() => m.Control = SceneThumb()));
            return Judge(m, cap, false);
        }
    }
}
