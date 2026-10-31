// StudioOps, light / camera / sound area (extracted from
// light.set, charaLight.set, camera.slot and sound.set): section sets validated against the section's settable members
// (all-or-nothing, the same messages as the handlers), camera slots (save recorded, load = a view change), the active
// camera, a camera node's FOV override, and the sound frame with HS2's one-channel rule.
using System;
using System.Collections.Generic;
using HS2Next.Save;
using UnityEngine;

namespace HS2Next.Scenes.Runtime
{
    public sealed partial class StudioOps
    {
        public static readonly string[] LightMembers = { "color", "intensity", "range", "spotAngle", "shadow", "enabled", "showTarget" };
        public static readonly string[] CharaLightMembers = { "color", "intensity", "rot", "shadow" };
        public static readonly string[] SoundMembers = { "bgm.ref", "bgm.play", "bgm.repeat", "userAudio.file", "userAudio.play", "userAudio.repeat" };

        /// <summary>
        /// The canonical path of <paramref name="name"/> (with or without the section prefix) when it lies under one of the
        /// section's <paramref name="members"/>; else the bad_args the handlers answer ("&lt;arg&gt;.&lt;name&gt;: not a settable … field").
        /// </summary>
        public static OpResult SectionPath(string section, string[] members, string name, string arg, out string full)
        {
            string rel = name.StartsWith(section + ".", StringComparison.Ordinal) ? name.Substring(section.Length + 1) : name;
            full = FieldPath.Canonical(section + "." + rel);
            string relCanon = full?.Substring(section.Length + 1);
            if (relCanon != null)
                foreach (var m in members)
                    if (relCanon == m || relCanon.StartsWith(m + ".", StringComparison.Ordinal) || relCanon.StartsWith(m + "[", StringComparison.Ordinal)) return null;
            full = null;
            var f = OpResult.Fail(CommandCodes.BadArgs, arg + "." + name + ": not a settable " + section + " field (" + string.Join(", ", members) + ")",
                arg + "." + name, "a " + section + " field path", members);
            return f;
        }

        /// <summary>Checks a plan of canonical section paths (members, finite floats).</summary>
        static OpResult CheckPlan(string section, string[] members, IList<(string path, object value)> plan)
        {
            if (plan == null || plan.Count == 0) return OpResult.BadArgs("set", "an object", "set names no field");
            foreach (var (path, value) in plan)
            {
                var bad = SectionPath(section, members, path, "set", out _);
                if (bad != null) return bad;
                if (value is float f && !Finite(f)) return OpResult.BadArgs("set." + path, "a finite number");
            }
            return null;
        }

        /// <summary>
        /// Applies a plan on every target as one entry: Changed = paths that changed on any target, NoOp = the plan's paths that
        /// changed on none (light.set / charaLight.set / sound.set).
        /// </summary>
        OpResult ApplyPlan(IList<string> targets, IList<(string path, object value)> plan, string label, ExecuteOptions o)
        {
            var r = SetMany(targets, plan, label, o, p => "set." + p);
            if (!r.Ok) return r;
            r.NoOp.Clear();
            foreach (var (path, _) in plan) if (!r.Changed.Contains(path) && !r.NoOp.Contains(path)) r.NoOp.Add(path);
            return r;
        }

        // ------------------------------------------------------------ lights

        /// <summary>Light-section values (canonical `light.` paths) of every Studio light, one entry "Light".</summary>
        public OpResult SetLight(IEnumerable<string> ids, IList<(string path, object value)> plan, ExecuteOptions o = default)
        {
            var nodes = OfKind(ids, NodeKind.Light, "light", "keys", out var fail);
            if (nodes == null) return fail;
            var bad = CheckPlan("light", LightMembers, plan);
            if (bad != null) return bad;
            return ApplyPlan(Ids(nodes), plan, Labels.Light, o);
        }

        /// <summary>The chara light (scene.charaLight: color, intensity, rot [x, y], shadow), one entry "Chara light".</summary>
        public OpResult SetCharaLight(IList<(string path, object value)> plan, ExecuteOptions o = default)
        {
            var bad = CheckPlan("charaLight", CharaLightMembers, plan);
            if (bad != null) return bad;
            foreach (var (path, value) in plan)
                if (path == "charaLight.rot" && !(value is float[] a && a.Length == 2)) return OpResult.BadArgs("set.rot", "[x, y]");
            return ApplyPlan(new string[] { null }, plan, Labels.CharaLight, o);
        }

        // ------------------------------------------------------------ camera

        static CameraView Copy(CameraView v) => v == null ? null : new CameraView { Pivot = v.Pivot, Rot = v.Rot, Distance = v.Distance, Fov = v.Fov };

        /// <summary>Saves the current view (or the reset view) into slot <paramref name="index"/> (0–9), one entry "Camera slot". Value = the view.</summary>
        public OpResult SaveCameraSlot(int index, ExecuteOptions o = default)
        {
            if (index < 0 || index >= CameraState.SlotCount) return OpResult.BadArgs("index", "0-9");
            var cam = Model.Settings.Camera;
            var view = Copy(cam.Current) ?? SceneViewCamera.ResetView();
            var list = new List<CameraView>();
            var old = cam.Slots ?? new List<CameraView>();
            for (int i = 0; i < Math.Max(CameraState.SlotCount, old.Count); i++) list.Add(i == index ? view : Copy(i < old.Count ? old[i] : null));
            var r = OpResult.From(Session.Set(null, "camera.slots", list, Labelled(o, Labels.CameraSlot)));
            rt.ApplyNow();
            r.Value = view;
            return r;
        }

        /// <summary>
        /// Loads slot <paramref name="index"/> into the view (an empty slot = the reset view; a view change, `camera.current`).
        /// Value = the view; a warning when a camera node is active (the view follows it).
        /// </summary>
        public OpResult LoadCameraSlot(int index, ExecuteOptions o = default)
        {
            if (index < 0 || index >= CameraState.SlotCount) return OpResult.BadArgs("index", "0-9");
            var cam = Model.Settings.Camera;
            var slot = cam.Slots != null && index < cam.Slots.Count ? cam.Slots[index] : null;
            var view = Copy(slot) ?? SceneViewCamera.ResetView();
            var r = OpResult.From(Session.Set(null, "camera.current", view, Labelled(o, Labels.Camera)));
            rt.ApplyNow();
            rt.LateApply();
            var active = rt.GetPart<SceneViewCamera>()?.ActiveCameraId;
            if (active != null) r.Warnings.Add("camera node " + active + " is active; the view follows it");
            r.Value = view;
            return r;
        }

        /// <summary>Makes a camera node the active camera (the others inactive) or, with null, releases the view ("Active camera" / "Release camera").</summary>
        public OpResult SetActiveCamera(string id, ExecuteOptions o = default)
        {
            if (id != null)
            {
                var n = OfKind(new[] { id }, NodeKind.Camera, "camera", "key", out var fail);
                if (n == null) return fail;
            }
            var r = OpResult.From(Session.SetActiveCamera(id, Labelled(o, id == null ? Labels.ReleaseCamera : Labels.ActiveCamera)));
            rt.ApplyNow();
            rt.LateApply();
            return r;
        }

        /// <summary>The FOV override of camera nodes (null = use the view's FOV), one entry "Camera".</summary>
        public OpResult SetCameraFov(IEnumerable<string> ids, float? fov, ExecuteOptions o = default)
        {
            var nodes = OfKind(ids, NodeKind.Camera, "camera", "keys", out var fail);
            if (nodes == null) return fail;
            if (fov != null && !Finite(fov.Value)) return OpResult.BadArgs("fov", "a finite number or null");
            return SetMany(Ids(nodes), new List<(string, object)> { ("camera.fov", fov) }, Labels.Camera, o, _ => "fov");
        }

        // ------------------------------------------------------------ sound

        /// <summary>
        /// scene.sound values (canonical `sound.` paths) with HS2's one channel: bgm.play true clears userAudio.play and the reverse
        /// (in the same entry "Sound"); both true is bad_args. Warnings for a BGM row not in the list and a missing user audio file
        /// (looked up under <paramref name="userDir"/> / <paramref name="hs2Root"/>, default the services' folders).
        /// </summary>
        public OpResult SetSound(IList<(string path, object value)> plan, ExecuteOptions o = default, string userDir = null, string hs2Root = null)
        {
            var bad = CheckPlan("sound", SoundMembers, plan);
            if (bad != null) return bad;
            var p = new List<(string path, object value)>(plan);
            var warnings = new List<string>();
            bool? bgmPlay = null, audioPlay = null;
            string user = userDir ?? Services?.UserData, hs2 = hs2Root ?? Services?.Hs2Root;
            foreach (var (path, value) in p)
            {
                if (path == "sound.bgm.play") bgmPlay = (bool)value;
                if (path == "sound.userAudio.play") audioPlay = (bool)value;
                if (path == "sound.bgm.ref" && value is string r0 && SceneSound.Bgm(Services?.Db, r0) == null) warnings.Add("BGM " + r0 + " is not in the BGM list (sound.bgmMissing)");
                if (path == "sound.userAudio.file" && value is FileRef f && !SceneSound.FileExists(f, user, hs2)) warnings.Add("user audio file " + f.Value + " not found (sound.fileMissing)");
            }
            if (bgmPlay == true && audioPlay == true)
            {
                var b = OpResult.BadArgs("set", "one channel", "BGM and user audio share one channel: set only one of bgm.play, userAudio.play to true");
                b.Warnings.AddRange(warnings);
                return b;
            }
            if (bgmPlay == true && audioPlay == null) p.Add(("sound.userAudio.play", false));
            if (audioPlay == true && bgmPlay == null) p.Add(("sound.bgm.play", false));
            var r = ApplyPlan(new string[] { null }, p, Labels.Sound, o);
            r.Warnings.InsertRange(0, warnings);
            return r;
        }
    }
}
