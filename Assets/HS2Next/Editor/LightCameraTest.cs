// Phase 3.3 package A edit-mode test: Studio lights (target XOR, folding,
// the `studio` MainLight source, helpers), the chara light, HS2's reset view and the slots, camera nodes, the sound frame
// and ViewSetup. Builds the vanilla Light rows through a SceneRuntime with the standard parts (catalog of
// Config/settings.local.json; no graphics needed). Report Spike/out/effects/light_camera_report.txt, "SUMMARY: N PASS, M FAIL".
//   Unity -batchmode -nographics -executeMethod LightCameraTest.Run [-only LC1,LC3]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static partial class LightCameraTest
{
    public static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "effects");

    static StringBuilder rep;
    static int passes, fails, warns;
    static readonly Dictionary<string, (int pass, int fail, int warn)> perTest = new Dictionary<string, (int, int, int)>();
    static string current = "setup";
    static readonly List<string> exceptions = new List<string>();
    static string[] only;
    static readonly List<Object> made = new List<Object>();
    static RuntimeServices services;

    static void Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  {current} {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
        perTest.TryGetValue(current, out var c);
        perTest[current] = ok ? (c.pass + 1, c.fail, c.warn) : (c.pass, c.fail + 1, c.warn);
    }

    static void Warn(string what)
    {
        rep.AppendLine($"WARN  {current} {what}");
        warns++;
        perTest.TryGetValue(current, out var c);
        perTest[current] = (c.pass, c.fail, c.warn + 1);
    }

    static void Info(string s) => rep.AppendLine("   " + s);

    static void Guard(string id, string title, Action a)
    {
        if (only != null && !only.Contains(id)) return;
        current = id;
        rep.AppendLine();
        rep.AppendLine("== " + id + " " + title);
        int ex0 = exceptions.Count;
        var sw = Stopwatch.StartNew();
        try { a(); }
        catch (Exception e) { current = id; Check("(exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        finally
        {
            foreach (var o in made) if (o) Object.DestroyImmediate(o);
            made.Clear();
            MainLight.Reset();
        }
        if (exceptions.Count > ex0) { current = id; Check("no exception logged", false, $"{exceptions.Count - ex0}: " + string.Join(" | ", exceptions.Skip(ex0).Take(3))); }
        Info($"{id}: {sw.Elapsed.TotalSeconds:F1} s");
    }

    static void OnLog(string condition, string stack, LogType type)
    {
        if (type == LogType.Exception) exceptions.Add(condition.Length > 300 ? condition.Substring(0, 300) : condition);
    }

    static string[] OnlyArgs()
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, "-only");
        if (i < 0) return null;
        var l = new List<string>();
        for (int k = i + 1; k < a.Length && !a[k].StartsWith("-", StringComparison.Ordinal); k++)
            l.AddRange(a[k].Split(',', '+', ';').Select(x => x.Trim()).Where(x => x.Length > 0));
        return l.ToArray();
    }

    public static void Run()
    {
        rep = new StringBuilder(); passes = fails = warns = 0; perTest.Clear(); exceptions.Clear();
        only = OnlyArgs();
        Directory.CreateDirectory(OutDir);
        Application.logMessageReceived += OnLog;
        var sw = Stopwatch.StartNew();
        var head = new StringBuilder();
        rep.AppendLine($"light / camera test (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}); edit mode");
        try
        {
            services = SceneRuntimeTest.NewServices(head);
            Guard("LC1", "target XOR (Q-b)", LC1);
            Guard("LC2", "folding of Studio and chara lights, rendering layers", LC2);
            Guard("LC3", "studio MainLight source", LC3);
            Guard("LC4", "light helpers and the capture scope", LC4);
            Guard("LC5", "reset view and slots", LC5);
            Guard("LC6", "camera nodes", LC6);
            Guard("LC7", "sound frame", LC7);
            Guard("LC8", "ViewSetup and RenderOptions", LC8);
        }
        catch (Exception e) { current = "setup"; Check("(setup exception)", false, e.ToString()); }
        Application.logMessageReceived -= OnLog;
        var sum = new StringBuilder();
        sum.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL ({warns} WARN, {sw.Elapsed.TotalSeconds:F0} s)");
        foreach (var kv in perTest.OrderBy(k => k.Key, StringComparer.Ordinal))
            sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail{(kv.Value.warn > 0 ? ", " + kv.Value.warn + " warn" : "")}");
        rep.Insert(0, sum.ToString());
        File.WriteAllText(Path.Combine(OutDir, "light_camera_report.txt"), rep.ToString(), new UTF8Encoding(false));
        Debug.Log($"LIGHT CAMERA TEST DONE: {passes} PASS, {fails} FAIL, {warns} WARN");
        if (fails > 0) TestExit.Exit(1);
    }

    // ================================================================ helpers

    sealed class Rig : IDisposable
    {
        public SceneSession Session;
        public SceneRuntime Runtime;
        public void Dispose() { Runtime?.Dispose(); Runtime = null; }
    }

    static Rig NewRig(ulong seed = 0x33aa33aaUL)
    {
        var s = SceneRuntimeTest.NewSession(services, seed);
        var rt = new SceneRuntime(s, services, null, false).AddStandardParts();
        rt.Tick(0f);
        return new Rig { Session = s, Runtime = rt };
    }

    static string AddLight(Rig r, int no, Vec3? rot = null)
    {
        var res = r.Session.AddLight(ContentId.Vanilla(no).ToString());
        string id = res.NewIds?.FirstOrDefault();
        if (id != null && rot.HasValue) r.Session.Set(id, "transform.rot", rot.Value);
        r.Runtime.Tick(0f);
        return id;
    }

    static LightRealization Real(Rig r, string id) => r.Runtime.Find(id) as LightRealization;

    static int Mask(params int[] layers) => layers.Aggregate(0, (m, l) => m | 1 << l);

    static bool Near(float a, float b, float eps = 1e-5f) => Math.Abs(a - b) <= eps;
    static bool NearC(Color c, float r, float g, float b, float eps = 1e-5f) => Near(c.r, r, eps) && Near(c.g, g, eps) && Near(c.b, b, eps);

    // ================================================================ LC1

    static void LC1()
    {
        int prefab = Mask(4, 10, 11, 12);
        Check("pure rule on the prefab mask: All 4,10,11,12 / Chara 4,10 / Map 4,11,12",
              LightRealization.TargetMask(prefab, StudioLightTarget.All) == prefab && LightRealization.TargetMask(prefab, StudioLightTarget.Chara) == Mask(4, 10)
              && LightRealization.TargetMask(prefab, StudioLightTarget.Map) == Mask(4, 11, 12));
        int bare = Mask(4);
        Check("Q-b: a mask without the bits gains them (Chara → 4,11,12; Map → 4,10)",
              LightRealization.TargetMask(bare, StudioLightTarget.Chara) == Mask(4, 11, 12) && LightRealization.TargetMask(bare, StudioLightTarget.Map) == Mask(4, 10));
        using (var r = NewRig())
        {
            var bad = new List<string>();
            int built = 0;
            for (int no = 0; no < 9; no++)
            {
                var l = Real(r, AddLight(r, no));
                if (l?.Light == null) { bad.Add(no + ": not built"); continue; }
                built++;
                int want = LightRealization.TargetMask(l.PrefabCullingMask, l.Row.TargetKind);
                var data = l.Light.GetComponent<UniversalAdditionalLightData>();
                if (l.PrefabCullingMask != prefab || l.Light.cullingMask != want || !data || unchecked((uint)data.renderingLayers) != unchecked((uint)want)
                    || unchecked((uint)data.shadowRenderingLayers) != unchecked((uint)want) || l.Light.renderingLayerMask != want)
                    bad.Add($"{no} {l.Row.TargetKind}/{l.Light.type}: prefab {l.PrefabCullingMask} mask {l.Light.cullingMask} layers {(data ? (uint)data.renderingLayers : 0)}");
                Info($"row {no}: {l.Row.Name} {l.Row.TargetKind} {l.Light.type} mask {l.Light.cullingMask}");
            }
            Check("the nine vanilla rows build; masks = XOR rule on the prefab mask 4,10,11,12; rendering and shadow layers = mask", built == 9 && bad.Count == 0, string.Join("; ", bad));
        }
    }

    // ================================================================ LC2

    static void LC2()
    {
        using (var r = NewRig())
        {
            string id = AddLight(r, 1);   // All point
            var l = Real(r, id);
            if (l?.Light == null) { Check("light built", false); return; }
            var o = BundleLights.OriginalOf(l.Light);
            Check("a new light: white × 1 folded (colour 1, intensity 1), original white × 1, Soft, strength 0.8 (prefab), range 10, spot 30",
                  NearC(l.Light.color, 1, 1, 1) && Near(l.Light.intensity, 1) && o && NearC(o.Color, 1, 1, 1) && Near(o.Intensity, 1) && l.Light.shadows == LightShadows.Soft
                  && Near(l.Light.shadowStrength, 0.8f, 1e-4f) && Near(l.Light.range, 10) && Near(l.Light.spotAngle, 30),
                  $"colour {l.Light.color} i {l.Light.intensity} strength {l.Light.shadowStrength}");
            r.Session.Set(id, "light.color", new Color4(1f, 0.5f, 0.25f, 0.7f));
            r.Session.Set(id, "light.intensity", 1.5f);
            r.Session.Set(id, "light.range", 120f);
            r.Session.Set(id, "light.spotAngle", 200f);
            r.Runtime.Tick(0f);
            Check("folding: colour = rgb × 1.5 (alpha 1), intensity 1; original = the state's (alpha kept); range unclamped, spot 200 kept in the state (Unity clamps Light.spotAngle to 179)",
                  NearC(l.Light.color, 1.5f, 0.75f, 0.375f) && Near(l.Light.color.a, 1) && Near(l.Light.intensity, 1) && NearC(o.Color, 1, 0.5f, 0.25f) && Near(o.Color.a, 0.7f) && Near(o.Intensity, 1.5f)
                  && Near(l.Light.range, 120) && Near(l.Light.spotAngle, 179) && (r.Session.Model.Find(id).State as LightNodeState).SpotAngle == 200f,
                  $"colour {l.Light.color} original {o.Color} × {o.Intensity}, range {l.Light.range}, spot {l.Light.spotAngle}");
            Check("MainLight.Brightness reads the original: max(rgb) × intensity = 1.5", Near(MainLight.Brightness(l.Light), 1.5f), "" + MainLight.Brightness(l.Light));
            var data = l.Light.GetComponent<UniversalAdditionalLightData>();
            Check("URP light data: pipeline bias (shadow tier High in play mode only: URP refuses the setter in edit mode)", data && data.usePipelineSettings && (!Application.isPlaying || data.additionalLightsShadowResolutionTier == UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierHigh));
            r.Session.Set(id, "light.shadow", false);
            r.Session.Set(id, "light.enabled", false);
            r.Runtime.Tick(0f);
            Check("shadow off → None, enabled off → Light.enabled false", l.Light.shadows == LightShadows.None && !l.Light.enabled);
            r.Session.Commands.Undo(); r.Session.Commands.Undo();
            r.Runtime.Tick(0f);
            Check("undo restores Soft / enabled", l.Light.shadows == LightShadows.Soft && l.Light.enabled);

            var cl = r.Runtime.GetPart<SceneCharaLight>();
            r.Session.Set(null, "charaLight.color", new Color4(0.8f, 0.6f, 0.4f, 1f));
            r.Session.Set(null, "charaLight.intensity", 2f);
            r.Runtime.Tick(0f);
            var co = BundleLights.OriginalOf(cl.Light);
            var cd = cl.Light.GetComponent<UniversalAdditionalLightData>();
            Check("chara light folded: colour (1.6, 1.2, 0.8), intensity 1, original (0.8, 0.6, 0.4) × 2, strength 1, Soft, HS2's mask and layers",
                  NearC(cl.Light.color, 1.6f, 1.2f, 0.8f) && Near(cl.Light.intensity, 1) && co && NearC(co.Color, 0.8f, 0.6f, 0.4f) && Near(co.Intensity, 2) && Near(cl.Light.shadowStrength, 1)
                  && cl.Light.shadows == LightShadows.Soft && cl.Light.cullingMask == RenderingLayers.CharaLightCullingMask && cd && unchecked((uint)cd.renderingLayers) == unchecked((uint)RenderingLayers.CharaLightCullingMask),
                  $"{cl.Light.color} × {cl.Light.intensity}");
            Check("chara light biases: 3 / 0.75 per light in URP units (HS2's Cam Light 0.1 / 0.3 in Built-in units)", cd && !cd.usePipelineSettings
                  && Near(cl.Light.shadowBias, SceneCharaLight.CharaLightShadowBias) && Near(cl.Light.shadowNormalBias, SceneCharaLight.CharaLightShadowNormalBias)
                  && Near(SceneCharaLight.CharaLightShadowBias, 3f) && Near(SceneCharaLight.CharaLightShadowNormalBias, 0.75f),
                  $"pipeline {cd?.usePipelineSettings}, {cl.Light.shadowBias} / {cl.Light.shadowNormalBias}");
        }
    }

    // ================================================================ LC3

    static void LC3()
    {
        using (var r = NewRig())
        {
            var sl = r.Runtime.GetPart<StudioLights>();
            var chara = r.Runtime.GetPart<SceneCharaLight>().Light;
            Check("StudioLights registered after SceneCharaLight; the chara light is the main light", sl != null && MainLight.Current == chara && MainLight.Reason == MainLight.ReasonChara,
                  MainLight.Current ? MainLight.Current.name : "none");
            string d0 = AddLight(r, 0, new Vec3(50, 30, 0));   // All directional
            var l0 = Real(r, d0);
            Check("a shadowed Studio directional with the chara light on: chara wins; finding light.unshadowedDirectional (info)",
                  MainLight.Current == chara && r.Runtime.Findings.Any(f => f.Code == "light.unshadowedDirectional" && f.NodeId == d0 && f.Severity == FindingSeverity.Info));
            int before = sl.Refreshes;
            r.Session.Set(null, "charaLight.shadow", false);
            r.Runtime.Tick(0f);
            Check("chara shadow off → the shadowed Studio directional is the main light (brightest)", MainLight.Current == l0.Light && MainLight.Reason == MainLight.ReasonBrightest,
                  MainLight.Current ? MainLight.Current.name : "none");
            string d1 = AddLight(r, 0);
            var l1 = Real(r, d1);
            r.Session.Set(d1, "light.intensity", 2f);
            r.Runtime.Tick(0f);
            Check("refresh on add / change: the brighter one (intensity 2) wins", MainLight.Current == l1.Light && sl.Refreshes > before, $"refreshes {before} -> {sl.Refreshes}");
            r.Session.Set(d1, "light.shadow", false);
            r.Runtime.Tick(0f);
            Check("refresh on shadow off: back to the first", MainLight.Current == l0.Light);
            r.Session.Set(d0, "light.enabled", false);
            r.Runtime.Tick(0f);
            Check("refresh on enabled off: no candidate → none", MainLight.Current == null && MainLight.Reason == MainLight.ReasonNone);
            r.Session.Set(d0, "light.enabled", true);
            r.Session.Set(d1, "light.shadow", true);
            r.Runtime.Tick(0f);
            r.Session.Delete(new[] { d1 });
            r.Runtime.Tick(0f);
            Check("refresh on delete: the remaining one", MainLight.Current == l0.Light, MainLight.Current ? MainLight.Current.name : "none");
            // tie: a map light of the same apparent brightness wins (map source before studio)
            var mapGo = new GameObject("map dir"); made.Add(mapGo);
            var ml = mapGo.AddComponent<Light>();
            ml.type = LightType.Directional; ml.shadows = LightShadows.Soft; ml.color = Color.white; ml.intensity = 1f;
            var owner = new object();
            MainLight.SetSource(owner, "map", () => new[] { ml });
            MainLight.Refresh();
            Check("tie (both max(rgb) × i = 1): the map light wins over the Studio light", MainLight.Current == ml);
            MainLight.RemoveSource(owner);
            MainLight.Refresh();
            r.Session.Set(null, "charaLight.shadow", true);
            r.Runtime.Tick(0f);
            Check("chara shadow on again: chara wins", MainLight.Current == chara);
        }
        Check("disposing the runtime leaves no studio source behind (no main light)", MainLight.Refresh() == null);
    }

    // ================================================================ LC4

    static void LC4()
    {
        using (var r = NewRig())
        {
            var dir = Real(r, AddLight(r, 0));
            var pt = Real(r, AddLight(r, 1));
            var sp = Real(r, AddLight(r, 2));
            if (dir?.Helpers == null || pt?.Helpers == null || sp?.Helpers == null) { Check("lights with helpers", false); return; }
            Check("helpers: directional arrow renderer(s) on layer 28; point 3 circles; spot 4 edges + disc",
                  dir.Helpers.Renderers.Any() && dir.Helpers.Renderers.All(x => x.gameObject.layer == ViewSetup.HelperLayer) && pt.Helpers.Lines.Count == 3 && sp.Helpers.Lines.Count == 5
                  && pt.Helpers.Renderers.Concat(sp.Helpers.Renderers).All(x => x.gameObject.layer == ViewSetup.HelperLayer),
                  $"arrow {dir.Helpers.Renderers.Count()}, point {pt.Helpers.Lines.Count}, spot {sp.Helpers.Lines.Count}");
            var c0 = pt.Helpers.Lines[0];
            float rad = c0.positionCount > 0 ? c0.GetPosition(0).magnitude : -1;
            Check("point circles: radius = range (10), 64 segments, looped", Near(rad, 10, 1e-4f) && c0.positionCount == LightHelpers.CircleSegments && c0.loop, $"r {rad}");
            var e0 = sp.Helpers.Lines[0];
            var end = e0.GetPosition(1);
            Check("spot edge: length = range, half angle 15°", Near(end.magnitude, 10, 1e-4f) && Near(Vector3.Angle(Vector3.forward, end), 15, 1e-3f), end.ToString());
            Check("shown by default (showTarget true) and visible", dir.Helpers.Shown && pt.Helpers.Visible && sp.Helpers.Visible);
            using (LightHelpers.HideForCapture())
                Check("inside the capture scope: every helper renderer forced off", !dir.Helpers.Visible && !pt.Helpers.Visible && !sp.Helpers.Visible);
            Check("after the scope: visible again", pt.Helpers.Visible && dir.Helpers.Visible);
            r.Session.Set(pt.NodeId, "light.showTarget", false);
            r.Session.Set(pt.NodeId, "light.range", 30f);
            r.Session.Set(pt.NodeId, "light.color", new Color4(1, 0, 0, 1));
            r.Runtime.Tick(0f);
            Check("showTarget false hides the point helpers; range 30 → radius 30", !pt.Helpers.Visible && !pt.Helpers.Shown && Near(pt.Helpers.Lines[1].GetPosition(0).magnitude, 30, 1e-4f));
            var mat = pt.Helpers.Lines[0].sharedMaterial;
            Check("helper material HS2Next/Unlit/Color in the light's colour", mat && mat.shader && mat.shader.name == "HS2Next/Unlit/Color" && mat.GetColor("_Color") == Color.red, mat ? mat.shader.name : "none");
            var arrowMat = dir.Helpers.Renderers.First().sharedMaterial;
            Check("the arrow's material is a fitted copy with _Color = the light colour (LightColor)", arrowMat && arrowMat.HasProperty("_Color") && arrowMat.GetColor("_Color") == Color.white,
                  arrowMat ? arrowMat.shader.name : "none");
            var vs = new ViewSetup();
            Check("ViewSetup: the view mask includes layer 28, the capture mask does not",
                  (vs.MaskFor(false) & 1 << ViewSetup.HelperLayer) != 0 && (vs.MaskFor(true) & 1 << ViewSetup.HelperLayer) == 0);
            int n = LightHelpers.Count;
            r.Session.Delete(new[] { sp.NodeId });
            r.Runtime.Tick(0f);
            Info($"helpers alive {n} -> {LightHelpers.Count} after a delete (graveyard keeps the realization)");
        }
        Check("no helpers left after the runtime is disposed", LightHelpers.Count == 0, "" + LightHelpers.Count);
    }

    // ================================================================ LC5

    static void LC5()
    {
        Check("reset view constants (0,10,0)/(15,180,0)/25/23 (E4, H2)",
              SceneViewCamera.DefaultPivot == new Vector3(0, 10, 0) && SceneViewCamera.DefaultRot == new Vector3(15, 180, 0) && SceneViewCamera.DefaultDistance == 25 && SceneViewCamera.DefaultFov == 23
              && SceneViewCamera.NearClip == 0.08f && SceneViewCamera.FarClip == 15000f);
        using (var r = NewRig())
        {
            var view = r.Runtime.GetPart<SceneViewCamera>();
            var cam = view.Camera;
            var q = Quaternion.Euler(15, 180, 0);
            Check("current null → the view at the reset view", r.Session.Model.Settings.Camera.Current == null && (cam.transform.position - (new Vector3(0, 10, 0) + q * new Vector3(0, 0, -25))).magnitude < 1e-4f
                  && RotationCompare.Deg(cam.transform.rotation, q) < 0.01f && Near(cam.fieldOfView, 23) && Near(cam.farClipPlane, 15000), cam.transform.position.ToString());
            var rv = SceneViewCamera.ResetView();
            Check("ResetView() as a model value", rv.Pivot.Y == 10 && rv.Rot.X == 15 && rv.Rot.Y == 180 && rv.Distance == 25 && rv.Fov == 23);
            var slots = r.Session.Model.Settings.Camera.Slots;
            Check("a new scene has 10 empty slots (empty = the reset view, Q-i)", slots.Count == CameraState.SlotCount && slots.All(x => x == null));
            int u0 = r.Session.Commands.UndoList.Count;
            var list = new List<CameraView>(new CameraView[10]);
            list[2] = SceneViewCamera.ResetView();
            var res = r.Session.Set(null, "camera.slots", list);
            Check("a slot save (camera.slots) is one history entry", res.Ok && r.Session.Commands.UndoList.Count == u0 + 1 && r.Session.Model.Settings.Camera.Slots[2]?.Distance == 25, res.ToString());
            var cur = new CameraView { Pivot = new Vec3(1, 2, 3), Rot = new Vec3(10, 20, 0), Distance = 7, Fov = 40 };
            r.Session.Set(null, "camera.current", cur);
            r.Runtime.Tick(0f);
            Check("a slot load (camera.current) records nothing and moves the view", r.Session.Commands.UndoList.Count == u0 + 1 && Near(cam.fieldOfView, 40));
        }
    }

    // ================================================================ LC6

    static void LC6()
    {
        using (var r = NewRig())
        {
            var view = r.Runtime.GetPart<SceneViewCamera>();
            var cam = view.Camera;
            string a = r.Session.AddCamera().NewIds[0], b = r.Session.AddCamera().NewIds[0];
            r.Session.Set(a, "transform.pos", new Vec3(5, 12, 20));
            r.Session.Set(a, "transform.rot", new Vec3(0, 200, 0));
            r.Session.Set(b, "transform.pos", new Vec3(-5, 8, 10));
            r.Session.SetActiveCamera(a);
            r.Runtime.Tick(0f);
            Check("active camera node: the view takes its world pose, FOV unchanged without fov (H14)", view.ActiveCameraId == a && (cam.transform.position - new Vector3(5, 12, 20)).magnitude < 1e-4f && Near(cam.fieldOfView, 23));
            r.Session.Set(b, "camera.fov", 50f);
            r.Session.SetActiveCamera(b);
            r.Runtime.Tick(0f);
            var na = r.Session.Model.Find(a).State as CameraNodeState;
            Check("single active: activating b deactivates a; b's fov 50 applies (HS2Next's optional fov)", view.ActiveCameraId == b && na != null && !na.Active && Near(cam.fieldOfView, 50));
            r.Session.Delete(new[] { b });
            r.Runtime.Tick(0f);
            var q = Quaternion.Euler(15, 180, 0);
            Check("deleting the active node returns the view to current (the reset view)", view.ActiveCameraId == null && (cam.transform.position - (new Vector3(0, 10, 0) + q * new Vector3(0, 0, -25))).magnitude < 1e-4f && Near(cam.fieldOfView, 23),
                  cam.transform.position.ToString());
            r.Session.SetActiveCamera(a);
            r.Runtime.Tick(0f);
            r.Session.Set(a, "transform.pos", new Vec3(6, 12, 20));
            r.Runtime.Tick(0f);
            Check("the view follows the active node when it moves", (cam.transform.position - new Vector3(6, 12, 20)).magnitude < 1e-4f);
        }
    }

    // ================================================================ LC7

    static void LC7()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "hs2next_lc7_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        string user = Path.Combine(tmp, "user"), hs2 = Path.Combine(tmp, "hs2");
        try
        {
            Directory.CreateDirectory(Path.Combine(user, "audio"));
            Directory.CreateDirectory(Path.Combine(hs2, "UserData", "audio"));
            File.WriteAllBytes(Path.Combine(user, "audio", "b.wav"), new byte[4]);
            File.WriteAllBytes(Path.Combine(user, "audio", "a.txt"), new byte[4]);
            File.WriteAllBytes(Path.Combine(hs2, "UserData", "audio", "B.WAV"), new byte[4]);
            File.WriteAllBytes(Path.Combine(hs2, "UserData", "audio", "c.wav"), new byte[4]);
            File.WriteAllBytes(Path.Combine(hs2, "UserData", "audio", "d.wave"), new byte[4]);
            var files = SceneSound.AudioFiles(user, hs2);
            Check("audio list: *.wav only, HS2Next first, a name clash keeps HS2Next's (b.wav user, c.wav hs2)",
                  files.Count == 2 && files[0].Name == "b.wav" && files[0].Source == "user" && files[0].Ref.Kind == FileRef.Kinds.User && files[0].Ref.Value == "audio/b.wav"
                  && files[1].Name == "c.wav" && files[1].Source == "hs2" && files[1].Ref.Value == "UserData/audio/c.wav",
                  string.Join(", ", files.Select(f => f.Source + ":" + f.Name)));
            Check("FileExists: user / hs2 refs, missing file, unsafe path, res entry",
                  SceneSound.FileExists(FileRef.FromUser("audio/b.wav"), user, hs2) && SceneSound.FileExists(FileRef.FromHs2("UserData/audio/c.wav"), user, hs2)
                  && !SceneSound.FileExists(FileRef.FromUser("audio/x.wav"), user, hs2) && !SceneSound.FileExists(FileRef.FromUser("../audio/b.wav"), user, hs2)
                  && SceneSound.FileExists(FileRef.FromRes("res/abc.wav"), user, hs2));
            var db = services.Db;
            Check("BGM rows: row 0 resolves, an unknown ref does not", SceneSound.Bgm(db, "illusion.hs2#0") != null && SceneSound.Bgm(db, "illusion.hs2#9999") == null && SceneSound.Bgm(db, null) == null);
            Info($"BGM rows visible {db.Bgm.VisibleCount} of {db.Bgm.Count}");
        }
        finally { try { Directory.Delete(tmp, true); } catch (Exception) { } }

        using (var r = NewRig())
        {
            var part = r.Runtime.GetPart<SceneSound>();
            Check("SceneSound registered", part != null);
            r.Session.Set(null, "sound.bgm.ref", "illusion.hs2#9999");
            r.Session.Set(null, "sound.userAudio.file", FileRef.FromUser("audio/nothing_here.wav"));
            r.Runtime.Tick(0f);
            Check("findings sound.bgmMissing and sound.fileMissing (warnings)",
                  r.Runtime.Findings.Any(f => f.Code == "sound.bgmMissing" && f.Severity == FindingSeverity.Warning) && r.Runtime.Findings.Any(f => f.Code == "sound.fileMissing" && f.Severity == FindingSeverity.Warning));
            r.Session.Set(null, "sound.bgm.play", true);
            r.Session.Set(null, "sound.userAudio.play", true);
            r.Runtime.Tick(0f);
            Check("both flags set by edits: no conflict finding (only a load reports it)", !r.Runtime.Findings.Any(f => f.Code == "sound.channelConflict"));
            part.OnLoadedRealized();
            r.Runtime.Tick(0f);
            var cf = r.Runtime.Findings.FirstOrDefault(f => f.Code == "sound.channelConflict");
            Check("a loaded scene with both flags: sound.channelConflict (info), both kept, HS2's outcome named (bgm: the file is missing)",
                  cf != null && cf.Severity == FindingSeverity.Info && cf.Message.Contains("bgm") && r.Session.Model.Settings.Sound.Bgm.Play && r.Session.Model.Settings.Sound.UserAudio.Play, cf?.Message);
            Check("LoadWinner: user audio when its file exists", SceneSound.LoadWinner(r.Session.Model.Settings.Sound, null, null) == "bgm");
        }
    }

    // ================================================================ LC8

    static void LC8()
    {
        var go = new GameObject("lc8 cam"); made.Add(go);
        var cam = go.AddComponent<Camera>();
        var vs = new ViewSetup();
        Check("defaults: HS2's main mask, helper 28, SolidColor black, post on, SMAA High, shadows, HDR, volume layer 8",
              vs.CullingMask == ViewSetup.Hs2MainCullingMask && vs.HelperMask == 1 << 28 && vs.ClearFlags == CameraClearFlags.SolidColor && vs.Background == Color.black
              && vs.PostProcessing && vs.Antialiasing == AntialiasingMode.SubpixelMorphologicalAntiAliasing && vs.AntialiasingQuality == AntialiasingQuality.High && vs.RenderShadows && vs.Hdr && vs.VolumeMask == 1 << 8);
        int[] layers = { 0, 1, 2, 4, 8, 10, 11, 12, 13, 14, 15, 19, 20, 21, 22, 29 };
        Check("Hs2MainCullingMask = layers 0,1,2,4,8,10-15,19-22,29 (E10)", ViewSetup.Hs2MainCullingMask == Mask(layers));
        int v0 = vs.Version;
        vs.RenderShadows = false;
        vs.Antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        vs.Background = new Color(0.1f, 0.2f, 0.3f, 1f);
        vs.ClearFlags = CameraClearFlags.Skybox;
        vs.Hdr = false;
        int v1 = vs.Version;
        vs.Hdr = false;
        Check("every changed member bumps Version; an equal value does not", v1 == v0 + 5 && vs.Version == v1, $"{v0} -> {v1} -> {vs.Version}");
        vs.ApplyTo(cam, false);
        var urp = cam.GetComponent<UniversalAdditionalCameraData>();
        Check("ApplyTo(view): mask | helpers, clear flags, background, HDR, AA FXAA, shadows off, post-processing, volume mask",
              cam.cullingMask == (ViewSetup.Hs2MainCullingMask | 1 << 28) && cam.clearFlags == CameraClearFlags.Skybox && cam.backgroundColor == new Color(0.1f, 0.2f, 0.3f, 1f) && !cam.allowHDR
              && urp && urp.antialiasing == AntialiasingMode.FastApproximateAntialiasing && !urp.renderShadows && urp.renderPostProcessing && urp.volumeLayerMask == 1 << 8);
        vs.ApplyTo(cam, true);
        Check("ApplyTo(capture): no helper layer, SMAA High whatever the view's AA, shadows as the view",
              cam.cullingMask == ViewSetup.Hs2MainCullingMask && urp.antialiasing == AntialiasingMode.SubpixelMorphologicalAntiAliasing && urp.antialiasingQuality == AntialiasingQuality.High && !urp.renderShadows);
        using (var r = NewRig())
        {
            var view = r.Runtime.GetPart<SceneViewCamera>();
            Check("the view applies its setup: ViewSetup.Of(view camera) = Setup, HS2's mask + helpers, SolidColor, far 15000",
                  ViewSetup.Of(view.Camera) == view.Setup && view.Camera.cullingMask == view.Setup.MaskFor(false) && view.Camera.clearFlags == CameraClearFlags.SolidColor && ViewSetup.Of(cam) == null);
            view.Setup.RenderShadows = false;
            view.Setup.ClearFlags = CameraClearFlags.Skybox;
            r.Runtime.LateApply();
            var vu = view.Camera.GetComponent<UniversalAdditionalCameraData>();
            Check("a member change reaches the view camera at the next LateApply", !vu.renderShadows && view.Camera.clearFlags == CameraClearFlags.Skybox);
            Info($"reference height {view.Setup.ReferenceHeight} (pixel height {view.Camera.pixelHeight})");
            Check("ReferenceHeight = the view's pixel height", view.Setup.ReferenceHeight == view.Camera.pixelHeight || view.Camera.pixelHeight <= 0);
        }

        // RenderOptions
        RenderOptions.ResetToDefaults();
        IReadOnlyCollection<string> got = null;
        Action<IReadOnlyCollection<string>> h = k => got = k;
        RenderOptions.Changed += h;
        try
        {
            bool threw = false;
            try { RenderOptions.Apply(Newtonsoft.Json.Linq.JObject.Parse("{\"aa\":\"taa\",\"shadowDistance\":5}")); }
            catch (FormatException) { threw = true; }
            Check("RenderOptions: a bad member throws and nothing is applied", threw && RenderOptions.Aa == "smaa" && got == null);
            var ch = RenderOptions.Apply(Newtonsoft.Json.Linq.JObject.Parse("{\"aa\":\"taa\",\"grading\":\"hdr\",\"backgroundColor\":[0.5,0.5,0.5]}"));
            Check("RenderOptions: Apply sets the values, returns and raises the changed keys",
                  RenderOptions.Aa == "taa" && RenderOptions.Grading == "hdr" && RenderOptions.BackgroundColor == new Color(0.5f, 0.5f, 0.5f, 1f) && ch.Count == 3 && got != null && got.Count == 3);
            var s = HS2Next.App.AppSettings.Defaults(new string[0], "{\"render\":{\"lightFalloff\":\"urp\"}}");
            Check("AppSettings reads the render block", RenderOptions.LightFalloff == "urp" && s != null);
        }
        finally
        {
            RenderOptions.Changed -= h;
            RenderOptions.ResetToDefaults();
        }
        Check("ResetToDefaults: smaa, high, hs2, hs2, hs2, 100, black",
              RenderOptions.Aa == "smaa" && RenderOptions.TaaQuality == "high" && RenderOptions.Grading == "hs2" && RenderOptions.Ambient == "hs2" && RenderOptions.LightFalloff == "hs2"
              && RenderOptions.ShadowDistance == 100 && RenderOptions.BackgroundColor == Color.black);
    }
}
