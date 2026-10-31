// View camera, capture and lights: camera.get / camera.set / camera.frame
// (HS2's `camera` object: pivot, Euler rotation, distance, fov of `scene.camera.current` or the default view),
// camera.state (HS2's full camera dump; `set` moves the view in the same frame), capture (rendered at the bridge's late
// point of frame F + waitFrames by ViewCapture into <outputDir>/shots/<name>.png; Phase 3.6: supersample, frame and the
// screenshot's pixel cap, package C) and scene.lights (HS2's lights dump).
// The view is view state: the bus never records it. Conventions are HS2's: position = pivot + Euler(rot)·(0, 0, −dist),
// positive pitch looks down, yaw 0 looks toward +Z, 1 unit = 10 cm.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using HS2Next.Api.Bridge;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace HS2Next.App.Commands
{
    public static class CameraCommands
    {
        public static void Register(ICommandRegistry r, AppCore app)
        {
            const string camDoc = "{target, rot, dist, fov, worldPos, forward, current, activeCamera}";
            CommandKit.Register(r, app, CommandKit.Spec("camera.get", "The view camera in HS2's shape (pivot, rotation, distance, fov) plus its world position and forward.", "camera")
                .Return("camera", ArgTypes.Object, camDoc)
                .WithFlags(CommandFlags.ReadOnly).WithTimeout(60), Get);

            CommandKit.Register(r, app, CommandKit.Spec("camera.set", "Sets the view (not recorded); absent members keep the current view's.", "camera")
                .Arg("target", ArgTypes.Vec3, "pivot (world)")
                .Arg("rot", ArgTypes.Euler, "rotation (Euler degrees; positive pitch looks down)")
                .Arg("dist", ArgTypes.Float, "distance from the pivot (its absolute value)")
                .Arg("fov", ArgTypes.Float, "vertical field of view (degrees)")
                .Return("camera", ArgTypes.Object, camDoc)
                .WithFlags(CommandFlags.Mutates).WithTimeout(60), Set);

            CommandKit.Register(r, app, CommandKit.Spec("camera.frame", "Points the view at a node or a point (not recorded).", "camera")
                .Arg("key", ArgTypes.Node, "target = the node's world position; one of key, point")
                .Arg("point", ArgTypes.Vec3, "target = this world point; one of key, point")
                .Arg("height", ArgTypes.Float, "added to the target's y", defaultValue: 0f)
                .Arg("dist", ArgTypes.Float, "distance", defaultValue: 25f)
                .Arg("yaw", ArgTypes.Float, "yaw (degrees)", defaultValue: 180f)
                .Arg("pitch", ArgTypes.Float, "pitch (degrees)", defaultValue: 10f)
                .Arg("fov", ArgTypes.Float, "field of view (default: keep)")
                .Return("camera", ArgTypes.Object, camDoc)
                .WithFlags(CommandFlags.Mutates).WithTimeout(60), Frame);

            CommandKit.Register(r, app, CommandKit.Spec("camera.state", "HS2's full camera dump; with set, moves the view first (in the same frame).", "camera")
                .Arg("set", ArgTypes.Object, "{pos: vec3 world, rot: [x,y,z,w] quaternion or [x,y,z] Euler, dist (default the current distance, 10 when < 1e-3), fov}")
                .Return("note", ArgTypes.String, "with set", false).Return("requested", ArgTypes.Object, "with set: {position, rotation, target, dist}", false)
                .Return("camera", ArgTypes.Object, "{name, position, rotation, euler, forward, up, fov, aspect, near, far, orthographic, pixelWidth, pixelHeight, screen, clearFlags, background, cullingMask, allowHDR, allowMSAA, renderingPath, frame, studio, current, activeCamera}")
                .WithFlags(CommandFlags.ReadOnly).WithTimeout(60), State);

            CommandKit.Register(r, app, new CommandSpec("capture", "Renders the view into <outputDir>/shots/<name>.png at the late point of frame F + waitFrames (default 0: this frame, after its motion steps).")
                {
                    Group = "camera",
                    HandlesWaitFrames = true,
                }
                .Arg("name", ArgTypes.String, "file name without extension (default the local time yyyyMMdd_HHmmss_fff; invalid characters become _)")
                .Arg("width", ArgTypes.Int, "16-8192", defaultValue: 1280)
                .Arg("height", ArgTypes.Int, "16-8192", defaultValue: 720)
                .Arg("aa", ArgTypes.Enum(ViewCapture.Smaa, ViewCapture.Msaa4, ViewCapture.None), "anti-aliasing (SMAA High; msaa4 = 4x MSAA without SMAA)", defaultValue: ViewCapture.Smaa)
                .Arg("supersample", ArgTypes.Int, "1, 2 or 4: render at k x the size, reduced by a linear-light box filter", defaultValue: 1)
                .Arg("frame", ArgTypes.Bool, "draw the scene's frame image (the background is part of the scene and always drawn)", defaultValue: false)
                .Return("path", ArgTypes.String, "absolute PNG path").Return("frame", ArgTypes.Int, "frame rendered")
                .Return("width", ArgTypes.Int, "").Return("height", ArgTypes.Int, "").Return("ms", ArgTypes.Float, "render + encode + write time")
                .Return("renderWidth", ArgTypes.Int, "width x supersample").Return("renderHeight", ArgTypes.Int, "height x supersample")
                .WithFlags(CommandFlags.WritesFiles | CommandFlags.Defers).WithTimeout(60), Capture);

            CommandKit.Register(r, app, CommandKit.Spec("scene.lights", "HS2's lights dump: every light of the loaded scenes, render settings, the Studio light settings and the view camera's components.", "camera")
                .Return("lights", ArgTypes.ArrayOf(ArgTypes.Object), "LightDesc").Return("renderSettings", ArgTypes.Object, "RenderSettings / QualitySettings members")
                .Return("studio", ArgTypes.Object, "{charaLight, mapLight, mapRef, mapLightOn, mainLight {name, reason}}").Return("cameraComponents", ArgTypes.ArrayOf(ArgTypes.Object), "{type, enabled}")
                .Return("cameraClear", ArgTypes.String, "clear flags").Return("cameraBackground", ArgTypes.Color, "background colour")
                .WithFlags(CommandFlags.ReadOnly).WithTimeout(60), Lights);
        }

        static SceneViewCamera View(CoreContext c) => c.Runtime.GetPart<SceneViewCamera>();

        /// <summary>The view brought up to date (Apply, LateApply) so that the reply shows the camera's current pose; nothing to
        /// do when no change reached the runtime since it last settled (the reply is the same).</summary>
        static void Settle(CoreContext c)
        {
            if (c.Runtime.GetPart<RuntimeUpkeep>() is RuntimeUpkeep up && up.Settled) return;
            c.Runtime.ApplyNow();
            c.Runtime.LateApply();
        }

        static object Get(CoreContext c)
        {
            Settle(c);
            return new BridgeReply().With("camera", CommandKit.CameraState(c.App));
        }

        /// <summary>The stored view or the default view.</summary>
        static (Vector3 pivot, Vector3 rot, float dist, float fov) Current(CoreContext c)
        {
            var v = c.Session.Model.Settings.Camera.Current;
            return v != null
                ? (UnityObjects.V(v.Pivot), UnityObjects.V(v.Rot), v.Distance, v.Fov)
                : (SceneViewCamera.DefaultPivot, SceneViewCamera.DefaultRot, SceneViewCamera.DefaultDistance, SceneViewCamera.DefaultFov);
        }

        static void SetView(CoreContext c, Vector3 pivot, Vector3 rot, float dist, float fov)
        {
            var view = new CameraView { Pivot = UnityObjects.V(pivot), Rot = UnityObjects.V(rot), Distance = Math.Abs(dist), Fov = fov };
            CommandKit.Check(c.Session.Set(null, "camera.current", view, CommandKit.Exec(c, "Camera")));
            Settle(c);
            var active = View(c)?.ActiveCameraId;
            if (active != null) c.Warn("camera node " + active + " is active; the view follows it");
        }

        static object Set(CoreContext c)
        {
            var (pivot, rot, dist, fov) = Current(c);
            if (c.Args.TryGet<Vector3>("target", out var t)) pivot = t;
            if (c.Args.TryGet<Vector3>("rot", out var e)) rot = e;
            if (c.Args.TryGet<float>("dist", out var d)) dist = d;
            if (c.Args.TryGet<float>("fov", out var f)) fov = f;
            SetView(c, pivot, rot, dist, fov);
            return new BridgeReply().With("camera", CommandKit.CameraState(c.App));
        }

        static object Frame(CoreContext c)
        {
            string arg = FileRoots.OneOf(c, "key", "point");
            Vector3 target;
            if (arg == "key")
            {
                var n = CommandKit.Node(c, "key");
                c.Runtime.ApplyNow();
                var h = c.Runtime.HolderOf(n.Id);
                if (!h) throw CommandKit.NotRealized("node " + n.Id + " has no holder", "key", c.Runtime.Find(n.Id)?.State.ToString() ?? "NotRealized");
                target = h.position;
            }
            else target = c.Args.Get<Vector3>("point");
            target.y += c.Args.Get("height", 0f);
            float dist = c.Args.Get("dist", 25f), yaw = c.Args.Get("yaw", 180f), pitch = c.Args.Get("pitch", 10f);
            float fov = c.Args.Get("fov", Current(c).fov);
            SetView(c, target, new Vector3(pitch, yaw, 0f), dist, fov);
            return new BridgeReply().With("camera", CommandKit.CameraState(c.App));
        }

        static object State(CoreContext c)
        {
            var r = new BridgeReply();
            var set = c.Args.Nested("set");
            if (set != null)
            {
                var cur = Current(c);
                Vector3 pos = set.Has("pos") ? set.Get<Vector3>("pos") : c.Runtime.GetPart<SceneViewCamera>()?.Camera?.transform.position ?? Vector3.zero;
                Quaternion q = Quaternion.Euler(cur.rot);
                if (set.Has("rot"))
                {
                    var a = set.Get<float[]>("rot");
                    if (a.Length == 4) q = new Quaternion(a[0], a[1], a[2], a[3]);
                    else if (a.Length == 3) q = Quaternion.Euler(a[0], a[1], a[2]);
                    else throw CommandKit.BadArgs("set.rot", "[x, y, z, w] or [x, y, z]");
                }
                float dist = set.Has("dist") ? set.Get<float>("dist") : cur.dist;
                if (Math.Abs(dist) < 1e-3f) dist = 10f;
                float fov = set.Has("fov") ? set.Get<float>("fov") : cur.fov;
                var target = pos + q * new Vector3(0f, 0f, dist);
                SetView(c, target, q.eulerAngles, dist, fov);
                r["note"] = "the camera moved in this frame";
                r["requested"] = new BridgeReply().With("position", CommandKit.D3(pos)).With("rotation", CommandKit.D4(q))
                    .With("target", CommandKit.D3(target)).With("dist", (double)dist);
            }
            else Settle(c);
            r["camera"] = CameraFull(c);
            return r;
        }

        /// <summary>HS2's camera.state `camera` object, HS2's members first.</summary>
        public static BridgeReply CameraFull(CoreContext c)
        {
            var view = View(c);
            var cam = view?.Camera;
            var r = new BridgeReply();
            var (pivot, rot, dist, fov) = Current(c);
            if (cam)
            {
                var t = cam.transform;
                r["name"] = cam.gameObject.name;
                r["position"] = CommandKit.D3(t.position);
                r["rotation"] = CommandKit.D4(t.rotation);
                r["euler"] = CommandKit.D3(t.eulerAngles);
                r["forward"] = CommandKit.D3(t.forward);
                r["up"] = CommandKit.D3(t.up);
                r["fov"] = (double)cam.fieldOfView;
                r["aspect"] = (double)cam.aspect;
                r["near"] = (double)cam.nearClipPlane;
                r["far"] = (double)cam.farClipPlane;
                r["orthographic"] = cam.orthographic;
                r["pixelWidth"] = cam.pixelWidth;
                r["pixelHeight"] = cam.pixelHeight;
                r["screen"] = new[] { Screen.width, Screen.height };
                r["clearFlags"] = cam.clearFlags.ToString();
                r["background"] = CommandKit.C(cam.backgroundColor);
                r["cullingMask"] = cam.cullingMask;
                r["allowHDR"] = cam.allowHDR;
                r["allowMSAA"] = cam.allowMSAA;
                r["renderingPath"] = cam.actualRenderingPath.ToString();
            }
            r["frame"] = Application.isPlaying ? Time.frameCount : c.Frames.Frame;
            r["studio"] = new BridgeReply().With("target", CommandKit.D3(pivot)).With("rotate", CommandKit.D3(rot))
                .With("distance", CommandKit.D3(new Vector3(0f, 0f, -dist))).With("fov", (double)fov).With("cameraControlOnMain", false)
                .With("basePosition", CommandKit.D3(Vector3.zero)).With("baseRotation", CommandKit.D4(Quaternion.identity));
            r["current"] = c.Session.Model.Settings.Camera.Current != null;
            r["activeCamera"] = view?.ActiveCameraId;
            // Phase 3.3: slots, the reset view and the ViewSetup-driven render settings
            var slots = c.Session.Model.Settings.Camera.Slots;
            int saved = 0;
            if (slots != null) foreach (var sv in slots) if (sv != null) saved++;
            r["slots"] = Math.Max(CameraState.SlotCount, slots?.Count ?? 0);
            r["slotsSaved"] = saved;
            r["reset"] = new BridgeReply().With("target", CommandKit.D3(SceneViewCamera.DefaultPivot)).With("rot", CommandKit.D3(SceneViewCamera.DefaultRot))
                .With("dist", (double)SceneViewCamera.DefaultDistance).With("fov", (double)SceneViewCamera.DefaultFov);
            if (cam)
            {
                var urp = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
                r["aa"] = urp ? urp.antialiasing.ToString() : null;
                r["aaQuality"] = urp ? urp.antialiasingQuality.ToString() : null;
                r["postProcessing"] = urp && urp.renderPostProcessing;
                r["renderShadows"] = urp && urp.renderShadows;
                r["volumeMask"] = urp ? (int)urp.volumeLayerMask : 0;
                r["hdr"] = cam.allowHDR;
            }
            r["helperMask"] = view?.Setup.HelperMask ?? 0;
            return r;
        }

        // ------------------------------------------------------------ capture

        static object Capture(CoreContext c)
        {
            string name = c.Args.Get<string>("name");
            if (string.IsNullOrEmpty(name)) name = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            int w = c.Args.Get("width", 1280), h = c.Args.Get("height", 720);
            if (w < 16 || w > 8192) throw CommandKit.BadArgs("width", "16-8192");
            if (h < 16 || h > 8192) throw CommandKit.BadArgs("height", "16-8192");
            string aa = c.Args.Get("aa", ViewCapture.Smaa);
            if (aa != ViewCapture.Smaa && aa != ViewCapture.Msaa4 && aa != ViewCapture.None)
                throw CommandKit.BadArgs("aa", ArgTypes.Enum(ViewCapture.Smaa, ViewCapture.Msaa4, ViewCapture.None));
            // Phase 3.6 (package C): supersampling, the frame (default off, so existing
            // captures do not change) and the screenshot's pixel cap (render = w × h × k²; bad_args with the limit)
            int k = c.Args.Get("supersample", 1);
            if (!HS2Next.Screenshots.CaptureLimits.ValidSupersample(k)) throw CommandKit.BadArgs("supersample", "1, 2 or 4");
            string why = HS2Next.Screenshots.CaptureLimits.Check(w, h, k, 0, out _);
            if (why != null) throw CommandKit.BadArgs(k > 1 ? "supersample" : "width", "a render of at most " + HS2Next.Screenshots.CaptureLimits.MaxPixels + " pixels", why);
            var req = new HS2Next.Screenshots.CaptureRequest { Width = w, Height = h, Supersample = k, Aa = aa, Frame = c.Args.Get("frame", false) };
            // the arguments (and the cap) are judged before the device, so a headless run answers bad_args the same way
            if (!ViewCapture.Available) throw CommandKit.Unsupported("capture needs a graphics device (this run is headless)");
            if ((long)w * h * k * k > HS2Next.Screenshots.CaptureLimits.WarnPixels) c.Warn(HS2Next.Screenshots.CaptureLimits.LargeRenderWarning);
            int wait = Math.Max(0, c.Ctx.WaitFrames);
            string full = c.Output.Resolve("shots/" + name + ".png", overwrite: true);
            var view = View(c);
            var d = c.Ctx.Defer();
            var frames = c.Frames;
            var rt = c.Runtime;
            frames.AtLate(wait, () =>
            {
                if (d.IsCompleted || d.IsAbandoned) return;
                try
                {
                    var sw = Stopwatch.StartNew();
                    SceneImages.Ensure(rt);
                    var res = ViewCapture.Render(rt, view?.Camera, req);
                    File.WriteAllBytes(full, res.Png);
                    d.Complete(new BridgeReply().With("path", full).With("frame", frames.Frame).With("width", w).With("height", h).With("ms", sw.Elapsed.TotalMilliseconds)
                        .With("renderWidth", res.RenderWidth).With("renderHeight", res.RenderHeight));
                }
                catch (Exception e) { d.Fail(new BridgeError(BridgeCodes.Internal, "capture failed: " + e.GetType().Name + ": " + e.Message)); }
            });
            return null;
        }

        // ------------------------------------------------------------ scene.lights

        static object Lights(CoreContext c)
        {
            Settle(c);
            var view = View(c);
            var cam = view?.Camera;
            var lights = new List<object>();
            foreach (var l in Resources.FindObjectsOfTypeAll<Light>())
            {
                if (!l || !l.gameObject.scene.IsValid()) continue;
                lights.Add(LightDesc(l, cam));
            }
            var r = new BridgeReply().With("lights", lights).With("renderSettings", RenderSettingsDesc());
            var s = c.Session.Model.Settings;
            var cl = s.CharaLight;
            r["studio"] = new BridgeReply()
                .With("charaLight", new BridgeReply().With("color", CommandKit.C(cl.Color)).With("intensity", (double)cl.Intensity)
                    .With("rot", new double[] { cl.Rot != null && cl.Rot.Length > 0 ? cl.Rot[0] : 0f, cl.Rot != null && cl.Rot.Length > 1 ? cl.Rot[1] : 0f })
                    .With("shadow", cl.Shadow))
                .With("mapLight", null).With("mapRef", s.Map.Ref).With("mapLightOn", s.Map.Lights)
                .With("mainLight", new BridgeReply().With("name", MainLight.Current ? MainLight.Current.name : null).With("reason", MainLight.Reason));
            // Phase 3.3: the realized chara light, the Studio lights, ambient rule and sky state
            var charaPart = c.Runtime.GetPart<SceneCharaLight>();
            var cll = charaPart?.Light;
            ((BridgeReply)r["studio"])["charaLight"] = new BridgeReply().With("color", CommandKit.C(cl.Color)).With("intensity", (double)cl.Intensity)
                .With("rot", new double[] { cl.Rot != null && cl.Rot.Length > 0 ? cl.Rot[0] : 0f, cl.Rot != null && cl.Rot.Length > 1 ? cl.Rot[1] : 0f })
                .With("shadow", cl.Shadow)
                .With("realized", cll ? new BridgeReply().With("color", CommandKit.C(cll.color)).With("intensity", (double)cll.intensity)
                    .With("shadows", cll.shadows.ToString()).With("shadowStrength", (double)cll.shadowStrength).With("localEuler", CommandKit.D3(cll.transform.localEulerAngles))
                    .With("cullingMask", cll.cullingMask).With("mainLight", MainLight.Current == cll) : null);
            var studioLights = new List<object>();
            var sl = c.Runtime.GetPart<StudioLights>();
            if (sl != null)
                foreach (var lr in sl.Realizations())
                {
                    var lst = lr.Node.State as LightNodeState;
                    studioLights.Add(new BridgeReply().With("key", lr.NodeId).With("ref", lst?.Ref)
                        .With("target", lr.Row == null ? null : lr.Row.TargetKind.ToString().ToLowerInvariant())
                        .With("state", lst == null ? null : new RawJson(FieldJson.ToText(lst))).With("realized", LightCameraCommands.Realized(lr)));
                }
            ((BridgeReply)r["studio"])["lights"] = studioLights;
            ((BridgeReply)r["studio"])["ambient"] = SkyAmbientState.StudioAmbient(c);   // package S
            ((BridgeReply)r["studio"])["sky"] = SkyAmbientState.StudioSky(c);
            if (cam)
            {
                var comps = new List<object>();
                foreach (var b in cam.GetComponents<Component>())
                {
                    if (!b) continue;
                    bool enabled = b is Behaviour be ? be.enabled : true;
                    comps.Add(new BridgeReply().With("type", b.GetType().Name).With("enabled", enabled));
                }
                r["cameraComponents"] = comps;
                r["cameraClear"] = cam.clearFlags.ToString();
                r["cameraBackground"] = CommandKit.C(cam.backgroundColor);
            }
            return r;
        }

        static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (var x = t; x; x = x.parent) parts.Add(x.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        static List<string> LayerNames(int mask)
        {
            var l = new List<string>();
            for (int i = 0; i < 32; i++)
                if ((mask & (1 << i)) != 0)
                {
                    string n = LayerMask.LayerToName(i);
                    l.Add(string.IsNullOrEmpty(n) ? i.ToString() : n);
                }
            return l;
        }

        static BridgeReply LightDesc(Light l, Camera view)
        {
            var t = l.transform;
            string layer = LayerMask.LayerToName(l.gameObject.layer);
            return new BridgeReply()
                .With("name", l.name).With("path", PathOf(t)).With("type", l.type.ToString()).With("enabled", l.enabled)
                .With("active", l.gameObject.activeInHierarchy).With("color", CommandKit.C(l.color)).With("intensity", (double)l.intensity)
                .With("bounceIntensity", (double)l.bounceIntensity).With("range", (double)l.range).With("spotAngle", (double)l.spotAngle)
                .With("position", CommandKit.D3(t.position)).With("rotation", CommandKit.D4(t.rotation)).With("euler", CommandKit.D3(t.eulerAngles))
                .With("forward", CommandKit.D3(t.forward)).With("shadows", l.shadows.ToString()).With("shadowStrength", (double)l.shadowStrength)
                .With("shadowBias", (double)l.shadowBias).With("shadowNormalBias", (double)l.shadowNormalBias).With("cullingMask", l.cullingMask)
                .With("renderMode", l.renderMode.ToString()).With("cullingLayers", LayerNames(l.cullingMask))
                .With("layer", string.IsNullOrEmpty(layer) ? l.gameObject.layer.ToString() : layer)
                .With("underMainCamera", view && t.IsChildOf(view.transform))
                // Phase 3.2: the bundle values of a folded light and the URP rendering layers
                .With("original", BundleLights.OriginalOf(l) is BundleLightOriginal o ? new BridgeReply().With("color", CommandKit.C(o.Color)).With("intensity", (double)o.Intensity) : null)
                .With("renderingLayers", l.TryGetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>(out var ld) ? (object)unchecked((uint)ld.renderingLayers) : null);
        }

        static BridgeReply RenderSettingsDesc()
        {
            var probe = RenderSettings.ambientProbe;
            var sh = new double[27];
            for (int ch = 0; ch < 3; ch++)
                for (int k = 0; k < 9; k++) sh[ch * 9 + k] = probe[ch, k];
            return new BridgeReply()
                .With("ambientMode", RenderSettings.ambientMode.ToString()).With("ambientLight", CommandKit.C(RenderSettings.ambientLight))
                .With("ambientSkyColor", CommandKit.C(RenderSettings.ambientSkyColor)).With("ambientEquatorColor", CommandKit.C(RenderSettings.ambientEquatorColor))
                .With("ambientGroundColor", CommandKit.C(RenderSettings.ambientGroundColor)).With("ambientIntensity", (double)RenderSettings.ambientIntensity)
                .With("ambientProbe", sh).With("fog", RenderSettings.fog).With("fogMode", RenderSettings.fogMode.ToString())
                .With("fogColor", CommandKit.C(RenderSettings.fogColor)).With("fogDensity", (double)RenderSettings.fogDensity)
                .With("fogStart", (double)RenderSettings.fogStartDistance).With("fogEnd", (double)RenderSettings.fogEndDistance)
                .With("skybox", RenderSettings.skybox ? RenderSettings.skybox.name + " (" + (RenderSettings.skybox.shader ? RenderSettings.skybox.shader.name : "") + ")" : null)
                .With("reflectionMode", RenderSettings.defaultReflectionMode.ToString()).With("reflectionIntensity", (double)RenderSettings.reflectionIntensity)
                .With("sun", RenderSettings.sun ? RenderSettings.sun.name : null).With("colorSpace", QualitySettings.activeColorSpace.ToString())
                .With("shadowDistance", (double)QualitySettings.shadowDistance).With("shadowCascades", QualitySettings.shadowCascades)
                .With("shadowResolution", QualitySettings.shadowResolution.ToString()).With("shadowProjection", QualitySettings.shadowProjection.ToString())
                .With("antiAliasing", QualitySettings.antiAliasing).With("pixelLightCount", QualitySettings.pixelLightCount);
        }
    }
}
