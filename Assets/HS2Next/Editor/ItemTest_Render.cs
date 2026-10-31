// ItemTest.RunRender (package A): edit-mode batch run WITH a graphics device (no
// -nographics), URP through Assets/HS2Next/Settings/Measure/MeasureURP.asset like the other render tests. Each vanilla target is built
// alone and captured through ViewCapture (the bridge's capture path, SMAA, 640 × 360) from a camera framing its
// bounds: the capture succeeds (a PNG of that size), no exception is logged during it (URP's light-cookie path: the
// cookie lights of 821 and 1017), the number of fallback (URP/Lit) slots is reported. The gimmick icon of 957 is never
// captured: the captures with the icon option on and off are byte-identical. Maps load only in play mode; the map
// variant is covered by the player's item case (BridgeAcceptance) — not here.
// Images are not decoded or judged: PNG headers, sizes and byte equality only.
// Report: Spike/out/items/item_render_report.txt ("SUMMARY: N PASS, M FAIL").
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.App.Commands;
using HS2Next.Content;
using HS2Next.Scenes.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static partial class ItemTest
{
    public static void RunRender()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HS2Next.App.AppPipeline.MeasureAssetPath);
        var prevDefault = GraphicsSettings.defaultRenderPipeline;
        var prevQuality = QualitySettings.renderPipeline;
        bool prevAsync = ShaderUtil.allowAsyncCompilation;
        var errors = new List<string>();
        Application.LogCallback onErr = (cond, stack, type) =>
        {
            if (type == LogType.Exception || (type == LogType.Error && !cond.Contains("FMOD") && !cond.Contains("Licensing"))) errors.Add(cond.Length > 200 ? cond.Substring(0, 200) : cond);
        };
        Application.logMessageReceived += onErr;
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            GraphicsSettings.defaultRenderPipeline = urp;
            QualitySettings.renderPipeline = urp;
            Section("R", "captures of the vanilla targets (URP, SMAA)");
            Check("URP asset active and a graphics device", urp != null && GraphicsSettings.currentRenderPipeline == urp && ViewCapture.Available, SystemInfo.graphicsDeviceType.ToString());
            var sv = Services(new StringBuilder());
            var targets = Vanilla.Concat(new[] { 1017 }).Distinct().ToList();
            var camGo = new GameObject("item_render_test_camera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = 30f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            var lightGo = new GameObject("item_render_test_light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGo.AddComponent<UniversalAdditionalLightData>();
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            try
            {
                using (var s = new Scene(sv, standardParts: true))
                {
                    foreach (var no in targets)
                    {
                        var row = sv.Db.Items.All.FirstOrDefault(r => r.IsVanilla && r.No == no);
                        if (row == null) { Warn(no + ": no such vanilla row"); continue; }
                        var id = s.Add(no);
                        var it = s.Item(id);
                        if (it?.Instance == null) { Check(no + " builds", false); continue; }
                        s.Runtime.Tick(0f);
                        Frame(cam, it.Instance);
                        int e0 = errors.Count;
                        byte[] png = null;
                        try { png = ViewCapture.Render(cam, 640, 360, ViewCapture.Smaa); }
                        catch (Exception x) { errors.Add(x.GetType().Name + ": " + x.Message); }
                        var (w, h) = png != null ? PngSize(png) : (0, 0);
                        int fb = it.Writer.FallbackShaders.Values.Sum();
                        Check($"{no} {row.Name}: capture succeeds (640 × 360 PNG), no error logged", png != null && w == 640 && h == 360 && errors.Count == e0,
                            $"{png?.Length ?? 0} bytes; fallback slots {fb}" + (fb > 0 ? " (" + string.Join(", ", it.Writer.FallbackShaders.Keys) + ")" : "") +
                            (errors.Count > e0 ? "; " + string.Join(" | ", errors.Skip(e0).Take(2)) : ""));
                        if (no == 821 || no == 1017)
                            Info($"{no}: lights {string.Join(", ", it.Instance.GetComponentsInChildren<Light>(true).Select(l => l.type + (l.cookie ? "+cookie" : "")))}");
                        if (it.Icon)
                        {
                            ItemIcons.Visible = true;
                            s.Runtime.Tick(0f);
                            var on = ViewCapture.Render(cam, 640, 360, ViewCapture.Smaa);
                            bool activeOn = it.Icon.gameObject.activeInHierarchy;
                            ItemIcons.Visible = false;
                            s.Runtime.Tick(0f);
                            var off = ViewCapture.Render(cam, 640, 360, ViewCapture.Smaa);
                            ItemIcons.Visible = true;
                            Check($"{no}: the icon is active but never captured (captures with the option on / off byte-identical)", activeOn && on.SequenceEqual(off), on.Length + " / " + off.Length + " bytes");
                        }
                        s.Runtime.Ops.Delete(new[] { id });
                        s.Runtime.Apply();
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(lightGo);
            }
        }
        catch (Exception e) { current = "run"; Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        finally
        {
            Application.logMessageReceived -= onErr;
            GraphicsSettings.defaultRenderPipeline = prevDefault;
            QualitySettings.renderPipeline = prevQuality;
            ShaderUtil.allowAsyncCompilation = prevAsync;
        }
        End("item_render_report.txt", "ITEM RENDER TEST", sw);
        services?.Catalog?.UnloadAll();
        services = null;
        if (fails > 0) EditorApplication.Exit(1);
    }

    /// <summary>Points the camera at the renderers' bounds from the front-right, far enough to see them whole.</summary>
    static void Frame(Camera cam, GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.one);
        foreach (var r in rs) b.Encapsulate(r.bounds);
        float radius = Mathf.Max(0.5f, b.extents.magnitude);
        float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.1f;
        var dir = Quaternion.Euler(20f, -35f, 0f) * Vector3.forward;
        cam.transform.position = b.center - dir * dist;
        cam.transform.rotation = Quaternion.LookRotation(dir);
        cam.nearClipPlane = Mathf.Max(0.01f, dist - radius * 2f);
        cam.farClipPlane = dist + radius * 4f + 10f;
    }
}
