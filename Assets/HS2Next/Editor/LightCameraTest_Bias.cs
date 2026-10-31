// Measurement U14 (Phase 3.3 package A; not a suite row): the chara light's shadow
// bias in URP. Renders BR-Chan's chest (the SFW target of Tools/targets.json, scene runtime fixture) through the view camera
// and the production URP asset with the chara light at rot (0, 0) and (30, 45), shadow off (reference) and shadow on with
// several bias settings: URP's pipeline values (asset depth 1 / normal 1) and per-light values including HS2's `Cam Light`
// 0.1 / 0.3 (Unity's Light.shadowBias / shadowNormalBias, which URP reads when usePipelineSettings is off). Per setting:
// the darkening against the reference over the image (mean, coverage > 4/255) and its high-frequency part (mean |d − blur(d)|,
// 5 × 5 box), the acne / lattice measure. PNGs and spike/out/effects/u14_bias.txt.
//   Unity -batchmode -executeMethod LightCameraTest.RunBias   (graphics)
using System;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.App.Commands;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

public static partial class LightCameraTest
{
    public static void RunBias()
    {
        var sb = new StringBuilder();
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/HS2Next/Settings/HS2NextURP.asset");
        var prevDefault = GraphicsSettings.defaultRenderPipeline;
        var prevQuality = QualitySettings.renderPipeline;
        bool prevAsync = ShaderUtil.allowAsyncCompilation;
        SceneRuntimeTest.Fixture f = null;
        RuntimeServices sv = null;
        string dir = Path.Combine(OutDir, "u14");
        Directory.CreateDirectory(dir);
        try
        {
            ShaderUtil.allowAsyncCompilation = false;
            GraphicsSettings.defaultRenderPipeline = urp;
            QualitySettings.renderPipeline = urp;
            sb.AppendLine($"U14 chara light shadow bias (Unity {Application.unityVersion}, {SystemInfo.graphicsDeviceType}); asset {urp?.name}: depth bias {urp?.shadowDepthBias}, normal bias {urp?.shadowNormalBias}, shadow res {urp?.mainLightShadowmapResolution}, distance {urp?.shadowDistance}, cascades {urp?.shadowCascadeCount}");
            sv = SceneRuntimeTest.NewServices(null);
            f = SceneRuntimeTest.BuildFixture(sv);
            var rt = f.Runtime;
            rt.Tick(0f);
            var br = rt.FindChara(f.BrChan);
            var chest = br?.Root ? br.Root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "cf_J_Mune00") : null;
            var head = br?.Root ? br.Root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "cf_J_Head") : null;
            if (!chest) { sb.AppendLine("no cf_J_Mune00 on BR-Chan"); return; }
            var c = chest.position;
            sb.AppendLine($"BR-Chan chest {c}, head {(head ? head.position.ToString() : "-")}, holder rot {br.Holder.eulerAngles}");
            var view = rt.GetPart<SceneViewCamera>();
            var light = rt.GetPart<SceneCharaLight>();
            // the character faces the holder's forward; the camera looks at the chest from 9 units in front
            var fwd = br.Holder.forward;
            var pivot = c + Vector3.up * 0.3f;
            var q = Quaternion.LookRotation(-fwd, Vector3.up) * Quaternion.Euler(8, 0, 0);
            var euler = q.eulerAngles;
            f.Session.Set(null, "camera.current", new CameraView { Pivot = new Vec3(pivot.x, pivot.y, pivot.z), Rot = new Vec3(euler.x, euler.y, 0), Distance = 9, Fov = 23 }, HS2Next.Scenes.ExecuteOptions.NotRecorded);
            rt.Tick(0f);
            var settings = new (string name, bool pipeline, float bias, float normal)[]
            {
                ("pipeline", true, 0, 0), ("hs2_0.1_0.3", false, 0.1f, 0.3f), ("0.3_0.5", false, 0.3f, 0.5f), ("0.5_0.8", false, 0.5f, 0.8f), ("1_1_light", false, 1f, 1f),
            };
            foreach (var rot in new[] { new[] { 0f, 0f }, new[] { 30f, 45f } })
            {
                f.Session.Set(null, "charaLight.rot", rot);
                f.Session.Set(null, "charaLight.shadow", false);
                rt.Tick(0f);
                string tag = $"rot{rot[0]:0}_{rot[1]:0}";
                var refImg = Shot(view.Camera, Path.Combine(dir, tag + "_off.png"));
                f.Session.Set(null, "charaLight.shadow", true);
                rt.Tick(0f);
                foreach (var s in settings)
                {
                    var l = light.Light;
                    var d = l.GetComponent<UniversalAdditionalLightData>();
                    d.usePipelineSettings = s.pipeline;
                    if (!s.pipeline) { l.shadowBias = s.bias; l.shadowNormalBias = s.normal; }
                    var img = Shot(view.Camera, Path.Combine(dir, tag + "_" + s.name + ".png"));
                    var (mean, cover, hf) = Darkening(refImg, img, 768, 768);
                    sb.AppendLine($"{tag} {s.name,-12} darkening mean {mean:F3}/255, coverage(>4) {cover:P2}, high-frequency {hf:F3}/255");
                }
                light.Light.GetComponent<UniversalAdditionalLightData>().usePipelineSettings = true;
            }
        }
        catch (Exception e) { sb.AppendLine("exception: " + e); }
        finally
        {
            try { f?.Dispose(); } catch (Exception) { }
            try { sv?.Catalog?.UnloadAll(); } catch (Exception) { }
            GraphicsSettings.defaultRenderPipeline = prevDefault;
            QualitySettings.renderPipeline = prevQuality;
            ShaderUtil.allowAsyncCompilation = prevAsync;
        }
        File.WriteAllText(Path.Combine(OutDir, "u14_bias.txt"), sb.ToString(), new UTF8Encoding(false));
        Debug.Log("U14 DONE\n" + sb);
    }

    static float[] Shot(Camera cam, string path)
    {
        var png = ViewCapture.Render(cam, 768, 768);
        File.WriteAllBytes(path, png);
        var t = new Texture2D(2, 2);
        t.LoadImage(png);
        var px = t.GetPixels32();
        UnityEngine.Object.DestroyImmediate(t);
        return px.Select(p => 0.2126f * p.r + 0.7152f * p.g + 0.0722f * p.b).ToArray();
    }

    /// <summary>Darkening d = max(0, ref − img) (0..255): its mean, the share above 4, and mean |d − box5(d)|.</summary>
    static (double mean, double cover, double hf) Darkening(float[] a, float[] b, int w, int h)
    {
        var d = new float[a.Length];
        double sum = 0; int n4 = 0;
        for (int i = 0; i < a.Length; i++) { d[i] = Math.Max(0f, a[i] - b[i]); sum += d[i]; if (d[i] > 4) n4++; }
        double hf = 0; int cnt = 0;
        for (int y = 2; y < h - 2; y++)
            for (int x = 2; x < w - 2; x++)
            {
                float s = 0;
                for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++) s += d[(y + dy) * w + x + dx];
                hf += Math.Abs(d[y * w + x] - s / 25f); cnt++;
            }
        return (sum / a.Length, n4 / (double)a.Length, hf / cnt);
    }
}
