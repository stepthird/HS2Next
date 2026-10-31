// Unity measurements of Phase 3.3 package S (not a test row, numbers for the spec):
//   & Tools\unity_batch.ps1 -Method SkyAmbientTest.MeasureFalloff                -> Spike/out/effects/u10_builtin_falloff.tsv
//   & Tools\unity_batch.ps1 -Method SkyAmbientTest.MeasureAmbient -NoQuit         -> Spike/out/effects/u1_ambient.txt
// MeasureFalloff (U10, edit mode, the Built-in pipeline): reads, through Editor/SkyAmbientTest_Atten.shader, the point-light
//   falloff texture, the spot falloff texture, the default spot cookie and the light coordinates (unity_WorldToLight) of a
//   point and a spot light on planes at known positions; writes the tables (the curve is fitted from them).
// MeasureAmbient (U1 / U2 / U15 and the Trilight SH, play mode with the app's URP asset): RenderSettings.ambientProbe at a
//   map's arrival (MapHooks.Loaded, synchronously after SetActiveScene), 1 and 5 frames later and after
//   DynamicGI.UpdateEnvironment, for vanilla map 10, the Bakery mod bushitsu, the terrain mod school and the app scene; whether
//   Unity replaces an app-written probe (skybox, colours, map unload, quality level, UpdateEnvironment); the SH Unity computes
//   for Trilight / Flat colours (basis colours, HS2's default colours, intensity 1 / 1.5); the default reflection.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.Content;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static partial class SkyAmbientTest
{
    static string Fm(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ U10 Built-in falloff

    public static void MeasureFalloff()
    {
        var sb = new StringBuilder();
        var prevDef = GraphicsSettings.defaultRenderPipeline;
        var prevQ = QualitySettings.renderPipeline;
        var created = new List<Object>();
        int code = 0;
        int prevPixelLights = QualitySettings.pixelLightCount;
        try
        {
            GraphicsSettings.defaultRenderPipeline = null;
            QualitySettings.renderPipeline = null;
            QualitySettings.pixelLightCount = 8;   // restored below (the quality asset must not be saved with it)
            var sh = Shader.Find("Hidden/HS2NextTest/BuiltinAtten");
            if (!sh) throw new Exception("Hidden/HS2NextTest/BuiltinAtten not found");
            var mat = new Material(sh); created.Add(mat);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad); created.Add(quad);
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.GetComponent<Renderer>().sharedMaterial = mat;
            var camGo = new GameObject("u10cam"); created.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.clear;
            cam.renderingPath = RenderingPath.Forward;
            cam.allowHDR = true;
            cam.allowMSAA = false;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            cam.enabled = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            const int N = 1024;
            var rt = new RenderTexture(N, N, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear); created.Add(rt);
            rt.Create();
            cam.targetTexture = rt;
            var lgo = new GameObject("u10light"); created.Add(lgo);
            var light = lgo.AddComponent<Light>();
            light.color = Color.white;
            light.intensity = 1f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            var tex = new Texture2D(N, N, TextureFormat.RGBAFloat, false, true); created.Add(tex);

            Color[] Render()
            {
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, N, N), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                return tex.GetPixels();
            }

            // the unit quad at the origin facing -z, the camera at z = -1 looking +z covering it exactly
            void Frame(Vector3 centre, float size)
            {
                quad.transform.position = centre;
                quad.transform.rotation = Quaternion.identity;
                quad.transform.localScale = new Vector3(size, size, 1f);
                cam.transform.position = centre + new Vector3(0, 0, -1f);
                cam.transform.rotation = Quaternion.identity;
                cam.orthographicSize = size * 0.5f;
            }

            // mode 0: point falloff texture over u
            light.type = LightType.Point;
            light.range = 100f;
            lgo.transform.position = new Vector3(0, 0, -0.5f);
            Frame(Vector3.zero, 1f);
            mat.SetFloat("_Mode", 0);
            var p0 = Render();
            sb.AppendLine("# point falloff texture (_LightTexture0 of a point light) at u = (x + 0.5) / 1024; u = (distance / range)^2");
            sb.AppendLine("table\tpoint\tu\tvalue");
            for (int x = 0; x < N; x++) sb.AppendLine($"T\tpoint\t{Fm((x + 0.5) / N)}\t{Fm(p0[(N / 2) * N + x].r)}");

            // mode 1: spot falloff (_LightTextureB0) and cookie (_LightTexture0.w)
            light.type = LightType.Spot;
            light.spotAngle = 90f;
            light.range = 100f;
            lgo.transform.position = new Vector3(0, 0, -2f);
            lgo.transform.rotation = Quaternion.identity;
            mat.SetFloat("_Mode", 1);
            var s1 = Render();
            sb.AppendLine("# spot falloff texture (_LightTextureB0) at u");
            for (int x = 0; x < N; x++) sb.AppendLine($"T\tspotFalloff\t{Fm((x + 0.5) / N)}\t{Fm(s1[(N / 2) * N + x].r)}");
            sb.AppendLine("# spot cookie (_LightTexture0.w): the row v = 0.5 and the column u = 0.5 and the diagonal");
            for (int x = 0; x < N; x++) sb.AppendLine($"T\tcookieRow\t{Fm((x + 0.5) / N)}\t{Fm(s1[(N / 2) * N + x].g)}");
            for (int y = 0; y < N; y++) sb.AppendLine($"T\tcookieCol\t{Fm((y + 0.5) / N)}\t{Fm(s1[y * N + N / 2].g)}");
            for (int x = 0; x < N; x++) sb.AppendLine($"T\tcookieDiag\t{Fm((x + 0.5) / N)}\t{Fm(s1[x * N + x].g)}");

            // mode 2: light coordinates on planes (point range 10 at the origin; spot range 10, angle 30 / 60 / 120)
            mat.SetFloat("_Mode", 2);
            sb.AppendLine("# light coordinates: kind range angle  world x y z  ->  lc x y z w   (light at the origin, facing +z)");
            sb.AppendLine("lc\tkind\trange\tangle\twx\twy\twz\tlx\tly\tlz\tlw");
            void Sample(string kind, float range, float angle, float z, float size)
            {
                Frame(new Vector3(0, 0, z), size);
                var px = Render();
                for (int j = 0; j < 9; j++)
                    for (int i = 0; i < 9; i++)
                    {
                        int x = (int)((i + 0.5f) / 9f * N), y = (int)((j + 0.5f) / 9f * N);
                        float wx = ((x + 0.5f) / N - 0.5f) * size, wy = ((y + 0.5f) / N - 0.5f) * size;
                        var c = px[y * N + x];
                        sb.AppendLine($"lc\t{kind}\t{Fm(range)}\t{Fm(angle)}\t{Fm(wx)}\t{Fm(wy)}\t{Fm(z)}\t{Fm(c.r)}\t{Fm(c.g)}\t{Fm(c.b)}\t{Fm(c.a)}");
                    }
            }
            lgo.transform.position = Vector3.zero;
            lgo.transform.rotation = Quaternion.identity;
            light.type = LightType.Point;
            light.range = 10f;
            Sample("point", 10f, 0f, 2f, 6f);
            Sample("point", 10f, 0f, 5f, 6f);
            light.type = LightType.Spot;
            foreach (float ang in new[] { 30f, 60f, 120f })
            {
                light.spotAngle = ang;
                foreach (float z in new[] { 2f, 5f })
                {
                    float size = 2f * z * Mathf.Tan(ang * 0.5f * Mathf.Deg2Rad) * 0.9f;
                    Sample("spot", 10f, ang, z, size);
                }
            }
            light.range = 30f;
            light.spotAngle = 60f;
            Sample("spot", 30f, 60f, 5f, 2f * 5f * Mathf.Tan(30f * Mathf.Deg2Rad) * 0.9f);
            cam.targetTexture = null;
        }
        catch (Exception e)
        {
            sb.AppendLine("ERROR " + e);
            code = 1;
        }
        finally
        {
            foreach (var o in created) if (o) Object.DestroyImmediate(o);
            GraphicsSettings.defaultRenderPipeline = prevDef;
            QualitySettings.renderPipeline = prevQ;
            QualitySettings.pixelLightCount = prevPixelLights;
        }
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "u10_builtin_falloff.tsv"), sb.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        Debug.Log("U10 measured -> " + Path.Combine(OutDir, "u10_builtin_falloff.tsv"));
        if (code != 0) throw new Exception("MeasureFalloff failed (see the file)");
    }

    // ------------------------------------------------------------------ U1 / U2 / U15 / Trilight SH

    static string Sh(SphericalHarmonicsL2 s, int n = 27)
    {
        var parts = new List<string>();
        for (int c = 0; c < 3; c++)
            for (int k = 0; k < 9; k++)
                if (c * 9 + k < n) parts.Add(Fm(s[c, k]));
        return string.Join(" ", parts);
    }

    static string Sh0(SphericalHarmonicsL2 s) => $"sh0 {Fm(s[0, 0])} / {Fm(s[1, 0])} / {Fm(s[2, 0])}";

    static string Env() =>
        $"scene {SceneManager.GetActiveScene().name} mode {RenderSettings.ambientMode} intensity {Fm(RenderSettings.ambientIntensity)} " +
        $"skybox {(RenderSettings.skybox ? RenderSettings.skybox.name + " (" + (RenderSettings.skybox.shader ? RenderSettings.skybox.shader.name : "?") + ")" : "none")} " +
        $"refl {RenderSettings.defaultReflectionMode} {(RenderSettings.customReflectionTexture ? RenderSettings.customReflectionTexture.name : "none")} x{Fm(RenderSettings.reflectionIntensity)}";

    static StringBuilder mrep;
    static void M(string s) { mrep.AppendLine(s); Debug.Log("[U1] " + s); }

    public static void MeasureAmbient()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "u1_ambient.txt"), "MeasureAmbient: started, no result yet\n", new UTF8Encoding(false));
        PlayModeHarness.Start(nameof(SkyAmbientTest) + "." + nameof(MeasureAmbientMain), Path.Combine(OutDir, "u1_ambient.txt"), 30);
    }

    static IEnumerator MeasureAmbientMain()
    {
        mrep = new StringBuilder();
        M($"MeasureAmbient (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
        var pipe = new HS2Next.App.AppPipeline();
        pipe.Apply();
        IEnumerator body = null;
        try { body = MeasureAmbientBody(); }
        catch (Exception e) { M("ERROR " + e); }
        if (body != null)
            while (true)
            {
                object cur;
                try
                {
                    if (!body.MoveNext()) break;
                    cur = body.Current;
                }
                catch (Exception e) { M("ERROR " + e); break; }
                yield return cur;
            }
        try { mrt?.Dispose(); } catch (Exception e) { M("dispose " + e.Message); }
        pipe.Restore();
        File.WriteAllText(Path.Combine(OutDir, "u1_ambient.txt"), mrep.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        PlayModeHarness.ExitCode = 0;
    }

    static SceneRuntime mrt;

    static SphericalHarmonicsL2 Marker()
    {
        var s = new SphericalHarmonicsL2();
        s.AddAmbientLight(new Color(0.25f, 0.5f, 0.75f));
        s[0, 1] = 0.125f;
        return s;
    }

    static bool IsMarker(SphericalHarmonicsL2 s)
    {
        var m = Marker();
        for (int c = 0; c < 3; c++) for (int k = 0; k < 9; k++) if (Mathf.Abs(s[c, k] - m[c, k]) > 1e-6f) return false;
        return true;
    }

    /// <summary>U2 continued: AmbientMode.Custom with an app-written probe, the default reflection (U15).</summary>
    public static void MeasureCustom()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "u2_custom.txt"), "MeasureCustom: started, no result yet\n", new UTF8Encoding(false));
        PlayModeHarness.Start(nameof(SkyAmbientTest) + "." + nameof(MeasureCustomMain), Path.Combine(OutDir, "u2_custom.txt"), 30);
    }

    static IEnumerator MeasureCustomMain()
    {
        mrep = new StringBuilder();
        M($"MeasureCustom (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
        var pipe = new HS2Next.App.AppPipeline();
        pipe.Apply();
        IEnumerator body = null;
        try { body = MeasureCustomBody(); }
        catch (Exception e) { M("ERROR " + e); }
        if (body != null)
            while (true)
            {
                object cur;
                try
                {
                    if (!body.MoveNext()) break;
                    cur = body.Current;
                }
                catch (Exception e) { M("ERROR " + e); break; }
                yield return cur;
            }
        try { mrt?.Dispose(); } catch (Exception e) { M("dispose " + e.Message); }
        pipe.Restore();
        File.WriteAllText(Path.Combine(OutDir, "u2_custom.txt"), mrep.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        PlayModeHarness.ExitCode = 0;
    }

    static string Refl()
    {
        var d = ReflectionProbe.defaultTexture;
        return $"refl mode {RenderSettings.defaultReflectionMode} custom {(RenderSettings.customReflectionTexture ? RenderSettings.customReflectionTexture.name : "none")} " +
               $"x{Fm(RenderSettings.reflectionIntensity)} defaultTexture {(d ? d.name + " " + d.width + " " + d.GetType().Name : "none")} hdr {ReflectionProbe.defaultTextureHDRDecodeValues}";
    }

    static IEnumerator CustomProbeSteps(string where)
    {
        var before = RenderSettings.ambientMode;
        RenderSettings.ambientMode = AmbientMode.Custom;
        RenderSettings.ambientProbe = Marker();
        M($"{where}: mode Custom + marker, read back at once: kept {IsMarker(RenderSettings.ambientProbe)} {Sh0(RenderSettings.ambientProbe)}");
        LightProbes.GetInterpolatedProbe(new Vector3(0, 500, 0), null, out var ip0);
        M($"{where}: interpolated (far above) {Sh0(ip0)}");
        yield return PlayModeHarness.WaitFrames(5);
        M($"{where}: +5 frames: kept {IsMarker(RenderSettings.ambientProbe)} {Sh0(RenderSettings.ambientProbe)}");
        RenderSettings.ambientSkyColor = new Color(0.9f, 0.1f, 0.1f);
        yield return PlayModeHarness.WaitFrames(3);
        M($"{where}: colour change: kept {IsMarker(RenderSettings.ambientProbe)}");
        var m = new Material(Shader.Find("Skybox/Procedural"));
        var prevSky = RenderSettings.skybox;
        RenderSettings.skybox = m;
        yield return PlayModeHarness.WaitFrames(3);
        M($"{where}: skybox change: kept {IsMarker(RenderSettings.ambientProbe)} {Sh0(RenderSettings.ambientProbe)}");
        DynamicGI.UpdateEnvironment();
        yield return PlayModeHarness.WaitFrames(2);
        M($"{where}: UpdateEnvironment: kept {IsMarker(RenderSettings.ambientProbe)} {Sh0(RenderSettings.ambientProbe)}");
        RenderSettings.skybox = prevSky;
        Object.Destroy(m);
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientProbe = Marker();
        M($"{where}: mode Skybox + marker: kept {IsMarker(RenderSettings.ambientProbe)}");
        RenderSettings.ambientMode = AmbientMode.Custom;
        M($"{where}: back to Custom without a write: {Sh0(RenderSettings.ambientProbe)} marker {IsMarker(RenderSettings.ambientProbe)}");
        RenderSettings.ambientProbe = Marker();
        yield return PlayModeHarness.WaitFrames(2);
        M($"{where}: Custom + marker again +2: kept {IsMarker(RenderSettings.ambientProbe)}; " + Refl());
        RenderSettings.ambientMode = before;
    }

    static IEnumerator MeasureCustomBody()
    {
        yield return PlayModeHarness.WaitFrames(3);
        var services = SceneRuntimeTest.NewServices(new StringBuilder());
        var session = SceneRuntimeTest.NewSession(services);
        mrt = new SceneRuntime(session, services, null, true).AddStandardParts();
        yield return PlayModeHarness.WaitFrames(3);
        var map = mrt.GetPart<MapRealization>();
        M("\n== app scene: " + Env() + " | " + Refl());
        yield return CustomProbeSteps("app scene");
        // U15: the BigSight cubemap as the custom default reflection
        var cube = services.Catalog.LoadAsset<Cubemap>("studio/probe/00.unity3d", "sky_BigSight_Env");
        M("sky_BigSight_Env loaded: " + (cube ? cube.width + " " + cube.format + " mips " + cube.mipmapCount : "no"));
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = cube;
        yield return PlayModeHarness.WaitFrames(3);
        M("custom reflection BigSight +3 frames: " + Refl());
        foreach (var r in new[] { ContentId.Vanilla(10).ToString(), "hooh.minimap.bushitsu#0" })
        {
            M("\n== map " + r);
            session.Set(null, "map.ref", r);
            bool[] ok = { false };
            yield return PlayModeHarness.WaitUntil(() => map.State == MapState.Loaded && !map.IsBusy, 300, ok);
            if (!ok[0]) { M("not loaded"); continue; }
            yield return PlayModeHarness.WaitFrames(3);
            M(Env() + " | " + Refl());
            yield return CustomProbeSteps(r);
            session.Set(null, "map.ref", null);
            yield return PlayModeHarness.WaitUntil(() => !map.IsBusy && map.PendingUnloads == 0 && map.State != MapState.Loaded, 120, ok);
            yield return PlayModeHarness.WaitFrames(2);
            M("after unload: " + Env() + " | " + Refl());
        }
    }

    static IEnumerator MeasureAmbientBody()
    {
        yield return PlayModeHarness.WaitFrames(3);
        var services = SceneRuntimeTest.NewServices(new StringBuilder());
        var session = SceneRuntimeTest.NewSession(services);
        mrt = new SceneRuntime(session, services, null, true).AddStandardParts();
        yield return PlayModeHarness.WaitFrames(3);
        var map = mrt.GetPart<MapRealization>();

        M("\n== app scene (no map)");
        M("start: " + Env() + " | " + Sh0(RenderSettings.ambientProbe) + " | " + Sh(RenderSettings.ambientProbe));
        yield return PlayModeHarness.WaitFrames(5);
        M("5 frames: " + Sh0(RenderSettings.ambientProbe));
        DynamicGI.UpdateEnvironment();
        M("UpdateEnvironment (same frame): " + Sh0(RenderSettings.ambientProbe));
        yield return PlayModeHarness.WaitFrames(2);
        M("UpdateEnvironment + 2 frames: " + Env() + " | " + Sh0(RenderSettings.ambientProbe) + " | " + Sh(RenderSettings.ambientProbe));

        M("\n== Trilight SH computed by Unity 6 (app scene)");
        var cases = new (string name, AmbientMode mode, Color sky, Color eq, Color gr, float intensity)[]
        {
            ("basis sky R eq G ground B", AmbientMode.Trilight, Color.red, Color.green, Color.blue, 1f),
            ("basis sky B eq G ground R", AmbientMode.Trilight, Color.blue, Color.green, Color.red, 1f),
            ("HS2 defaults", AmbientMode.Trilight, new Color32(170, 188, 243, 255), new Color32(185, 195, 205, 255), new Color32(204, 109, 41, 255), 1f),
            ("HS2 defaults, intensity 1.5", AmbientMode.Trilight, new Color32(170, 188, 243, 255), new Color32(185, 195, 205, 255), new Color32(204, 109, 41, 255), 1.5f),
            ("grey 0.5 gamma", AmbientMode.Trilight, new Color(0.5f, 0.5f, 0.5f), new Color(0.5f, 0.5f, 0.5f), new Color(0.5f, 0.5f, 0.5f), 1f),
            ("sky only white", AmbientMode.Trilight, Color.white, Color.black, Color.black, 1f),
            ("equator only white", AmbientMode.Trilight, Color.black, Color.white, Color.black, 1f),
            ("ground only white", AmbientMode.Trilight, Color.black, Color.black, Color.white, 1f),
            ("Flat HS2 sky colour", AmbientMode.Flat, new Color32(170, 188, 243, 255), new Color32(185, 195, 205, 255), new Color32(204, 109, 41, 255), 1f),
            ("Flat HS2 sky colour, intensity 1.5", AmbientMode.Flat, new Color32(170, 188, 243, 255), new Color32(185, 195, 205, 255), new Color32(204, 109, 41, 255), 1.5f),
        };
        foreach (var c in cases)
        {
            RenderSettings.ambientMode = c.mode;
            RenderSettings.ambientSkyColor = c.sky;
            RenderSettings.ambientEquatorColor = c.eq;
            RenderSettings.ambientGroundColor = c.gr;
            RenderSettings.ambientIntensity = c.intensity;
            M($"{c.name}: right after setting: " + Sh0(RenderSettings.ambientProbe));
            yield return PlayModeHarness.WaitFrames(2);
            M($"{c.name}: 2 frames (no update call): " + Sh0(RenderSettings.ambientProbe));
            DynamicGI.UpdateEnvironment();
            M($"{c.name}: UpdateEnvironment same frame: " + Sh0(RenderSettings.ambientProbe));
            yield return PlayModeHarness.WaitFrames(2);
            M($"{c.name}: UpdateEnvironment + 2 frames: " + Sh(RenderSettings.ambientProbe));
            LightProbes.GetInterpolatedProbe(Vector3.zero, null, out var ip);
            M($"{c.name}: interpolated probe at the origin: " + Sh(ip));
        }

        M("\n== U2 app-written probe in the app scene (Trilight, HS2 defaults)");
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color32(170, 188, 243, 255);
        RenderSettings.ambientEquatorColor = new Color32(185, 195, 205, 255);
        RenderSettings.ambientGroundColor = new Color32(204, 109, 41, 255);
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.ambientProbe = Marker();
        M("set marker: kept " + IsMarker(RenderSettings.ambientProbe));
        yield return PlayModeHarness.WaitFrames(5);
        M("5 frames: kept " + IsMarker(RenderSettings.ambientProbe) + " " + Sh0(RenderSettings.ambientProbe));
        RenderSettings.ambientSkyColor = new Color32(171, 188, 243, 255);
        yield return PlayModeHarness.WaitFrames(3);
        M("after a colour change + 3 frames: kept " + IsMarker(RenderSettings.ambientProbe) + " " + Sh0(RenderSettings.ambientProbe));
        RenderSettings.ambientProbe = Marker();
        var skyMat = new Material(Shader.Find("Skybox/Procedural"));
        RenderSettings.skybox = skyMat;
        yield return PlayModeHarness.WaitFrames(3);
        M("after a skybox change + 3 frames: kept " + IsMarker(RenderSettings.ambientProbe) + " " + Sh0(RenderSettings.ambientProbe));
        RenderSettings.skybox = null;
        RenderSettings.ambientProbe = Marker();
        int q = QualitySettings.GetQualityLevel();
        QualitySettings.SetQualityLevel(q == 0 ? 1 : q - 1, true);
        yield return PlayModeHarness.WaitFrames(3);
        M("after a quality level change + 3 frames: kept " + IsMarker(RenderSettings.ambientProbe) + " " + Sh0(RenderSettings.ambientProbe));
        QualitySettings.SetQualityLevel(q, true);
        yield return PlayModeHarness.WaitFrames(2);
        RenderSettings.ambientProbe = Marker();
        DynamicGI.UpdateEnvironment();
        yield return PlayModeHarness.WaitFrames(2);
        M("after UpdateEnvironment + 2 frames: kept " + IsMarker(RenderSettings.ambientProbe) + " " + Sh0(RenderSettings.ambientProbe));

        // maps: the probe at Loaded (synchronous), frames later, after UpdateEnvironment; U2 on each map
        var refs = new[] { ContentId.Vanilla(10).ToString(), "hooh.minimap.bushitsu#0", "hooh.map.school#1" };
        SphericalHarmonicsL2? atLoaded = null;
        string envLoaded = null;
        Action<MapRealization> onLoaded = m => { atLoaded = RenderSettings.ambientProbe; envLoaded = Env(); };
        MapHooks.Loaded += onLoaded;
        try
        {
            foreach (var r in refs)
            {
                M("\n== map " + r);
                RenderSettings.ambientProbe = Marker();   // the app scene's value before the map arrives
                atLoaded = null;
                session.Set(null, "map.ref", r);
                bool[] ok = { false };
                yield return PlayModeHarness.WaitUntil(() => map.State == MapState.Loaded && !map.IsBusy, 300, ok);
                if (!ok[0] || map.State != MapState.Loaded) { M("not loaded: " + map.State); continue; }
                M("Loaded (sync): " + envLoaded + " | " + (atLoaded.HasValue ? Sh0(atLoaded.Value) + " | " + Sh(atLoaded.Value) : "no value"));
                M("Loaded (sync) is the app's marker: " + (atLoaded.HasValue && IsMarker(atLoaded.Value)));
                M("now (after the wait): " + Sh0(RenderSettings.ambientProbe));
                yield return PlayModeHarness.WaitFrames(1);
                M("+1 frame: " + Sh0(RenderSettings.ambientProbe));
                yield return PlayModeHarness.WaitFrames(5);
                M("+6 frames: " + Env() + " | " + Sh0(RenderSettings.ambientProbe));
                DynamicGI.UpdateEnvironment();
                M("UpdateEnvironment same frame: " + Sh0(RenderSettings.ambientProbe));
                yield return PlayModeHarness.WaitFrames(2);
                M("UpdateEnvironment + 2 frames: " + Sh0(RenderSettings.ambientProbe) + " | " + Sh(RenderSettings.ambientProbe));
                LightProbes.GetInterpolatedProbe(Vector3.zero, null, out var ip);
                M("interpolated probe at the origin: " + Sh0(ip) + " (light probes " + (LightmapSettings.lightProbes ? LightmapSettings.lightProbes.count : 0) + ")");
                // U2 on the map scene
                RenderSettings.ambientProbe = Marker();
                yield return PlayModeHarness.WaitFrames(5);
                M("U2 map: marker kept after 5 frames: " + IsMarker(RenderSettings.ambientProbe));
                var col = RenderSettings.ambientSkyColor;
                RenderSettings.ambientSkyColor = new Color(0.3f, 0.2f, 0.1f);
                yield return PlayModeHarness.WaitFrames(3);
                M("U2 map: marker kept after a colour change: " + IsMarker(RenderSettings.ambientProbe));
                RenderSettings.ambientSkyColor = col;
                RenderSettings.ambientProbe = Marker();
                var prevSky = RenderSettings.skybox;
                RenderSettings.skybox = skyMat;
                yield return PlayModeHarness.WaitFrames(3);
                M("U2 map: marker kept after a skybox change: " + IsMarker(RenderSettings.ambientProbe) + " " + Sh0(RenderSettings.ambientProbe));
                RenderSettings.skybox = prevSky;
                // unload: the app scene's value afterwards
                session.Set(null, "map.ref", null);
                yield return PlayModeHarness.WaitUntil(() => !map.IsBusy && map.PendingUnloads == 0 && map.State != MapState.Loaded, 120, ok);
                yield return PlayModeHarness.WaitFrames(2);
                M("after unload: " + Env() + " | " + Sh0(RenderSettings.ambientProbe) + " (marker " + IsMarker(RenderSettings.ambientProbe) + ")");
            }
        }
        finally { MapHooks.Loaded -= onLoaded; }
        Object.Destroy(skyMat);
    }

    // ------------------------------------------------------------------ U3 sky feed variants

    /// <summary>U3: which inputs AIOsky/V2 reacts to under URP (edit mode, the app's asset): the sun feed on / off,
    /// _LightColor0 black / white, the sun direction, a global set outside the camera callback.</summary>
    public static void MeasureSky()
    {
        Begin();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var urp = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>(ProductionAsset);
        var prevD = GraphicsSettings.defaultRenderPipeline; var prevQ = QualitySettings.renderPipeline;
        try
        {
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;
            UseUrp(urp);
            using (var rig = new Rig(Services()))
            {
                var cam = rig.Runtime.GetPart<SceneViewCamera>().Camera;
                var sky = rig.Sky;
                rig.Set("effects.sky.enabled", true);
                rig.Set("effects.sky.pattern", ContentId.Vanilla(0).ToString());
                rig.Step();
                cam.transform.SetPositionAndRotation(new Vector3(0f, 5f, 30f), Quaternion.Euler(-35f, 180f, 0f));
                cam.fieldOfView = 60f;
                Color[] baseline = null;
                string Run(string label, System.Action set)
                {
                    SceneSky.FeedEnabled = true; sky.SkySun.eulerAngles = new Vector3(40, 0, 0);
                    set();
                    var px = RenderTo(cam, 320, 180, false, out var t);
                    UnityEngine.Object.DestroyImmediate(t);
                    var m = Mean(px, 320, 180, 0, 180);
                    double d = 0;
                    if (baseline != null) for (int i = 0; i < px.Length; i++) d += Mathf.Abs(px[i].r - baseline[i].r) + Mathf.Abs(px[i].g - baseline[i].g) + Mathf.Abs(px[i].b - baseline[i].b);
                    if (baseline == null) baseline = px;
                    string r = $"{label}: mean ({m.r * 255:F1}, {m.g * 255:F1}, {m.b * 255:F1}), |d| vs baseline {d / (3.0 * px.Length) * 255:F2}/255";
                    Info(r);
                    return r;
                }
                Run("A feed on, _LightColor0 0, sun (40, 0, 0)", () => { });
                Run("B feed off", () => SceneSky.FeedEnabled = false);
                Run("E sun (40, 180, 0)", () => sky.SkySun.eulerAngles = new Vector3(40, 180, 0));
                Run("F sun (-40, 0, 0) below the horizon", () => sky.SkySun.eulerAngles = new Vector3(-40, 0, 0));
                Run("G sun straight up (90, 0, 0)", () => sky.SkySun.eulerAngles = new Vector3(90, 0, 0));
                Run("H feed off, global _WorldSpaceLightPos0 (0,1,0,0) set before the render", () => { SceneSky.FeedEnabled = false; Shader.SetGlobalVector("_WorldSpaceLightPos0", new Vector4(0, 1, 0, 0)); });
                Run("I feed off, global _WorldSpaceLightPos0 (0,-1,0,0)", () => { SceneSky.FeedEnabled = false; Shader.SetGlobalVector("_WorldSpaceLightPos0", new Vector4(0, -1, 0, 0)); });
                Run("J _Time-free check: material _timeScale 0", () => sky.SkyMaterial.SetFloat("_timeScale", 0f));
                // RenderSettings.sun: does the engine feed the skybox from it under URP?
                var lgo = new GameObject("u3 sun");
                var sl = lgo.AddComponent<Light>();
                sl.type = LightType.Directional; sl.intensity = 1f; sl.color = Color.white;
                var sun0 = RenderSettings.sun;
                try
                {
                    Run("K RenderSettings.sun = a white light at (40, 0, 0)", () => { RenderSettings.sun = sl; lgo.transform.rotation = Quaternion.Euler(40, 0, 0); });
                    Run("L … at (40, 180, 0)", () => { RenderSettings.sun = sl; lgo.transform.rotation = Quaternion.Euler(40, 180, 0); });
                    Run("M … intensity 0 at (40, 0, 0)", () => { RenderSettings.sun = sl; sl.intensity = 0f; lgo.transform.rotation = Quaternion.Euler(40, 0, 0); });
                    Run("N … intensity 0 at (-40, 0, 0)", () => { RenderSettings.sun = sl; sl.intensity = 0f; lgo.transform.rotation = Quaternion.Euler(-40, 0, 0); });
                    // the Editor's Built-in pipeline: the shader as HS2 renders it (light constants set by Built-in)
                    UseUrp(null);
                    Run("P Built-in, sun white (40, 0, 0)", () => { RenderSettings.sun = sl; sl.intensity = 1f; lgo.transform.rotation = Quaternion.Euler(40, 0, 0); });
                    Run("Q Built-in, sun intensity 0 (40, 0, 0)", () => { RenderSettings.sun = sl; sl.intensity = 0f; lgo.transform.rotation = Quaternion.Euler(40, 0, 0); });
                    Run("R Built-in, sun intensity 0 (40, 180, 0)", () => { RenderSettings.sun = sl; sl.intensity = 0f; lgo.transform.rotation = Quaternion.Euler(40, 180, 0); });
                    Run("S Built-in, sun intensity 0 (-40, 0, 0)", () => { RenderSettings.sun = sl; sl.intensity = 0f; lgo.transform.rotation = Quaternion.Euler(-40, 0, 0); });
                    UseUrp(urp);
                }
                finally { RenderSettings.sun = sun0; UnityEngine.Object.DestroyImmediate(lgo); UseUrp(urp); }
                var mat = sky.SkyMaterial;
                Info("material: shader " + mat.shader.name + ", keywords " + string.Join(" ", mat.shaderKeywords) + ", passes " + mat.passCount);
                for (int i = 0; i < mat.shader.GetPropertyCount(); i++)
                {
                    var n = mat.shader.GetPropertyName(i);
                    var ty = mat.shader.GetPropertyType(i);
                    string v = ty == UnityEngine.Rendering.ShaderPropertyType.Color ? mat.GetColor(n).ToString() : ty == UnityEngine.Rendering.ShaderPropertyType.Texture ? (mat.GetTexture(n) ? mat.GetTexture(n).name : "none") : ty == UnityEngine.Rendering.ShaderPropertyType.Vector ? mat.GetVector(n).ToString() : mat.GetFloat(n).ToString("G4");
                    Info($"  {n} ({ty}) = {v}");
                }
            }
        }
        catch (Exception e) { Check("ran", false, e.ToString()); }
        finally { SceneSky.FeedEnabled = true; GraphicsSettings.defaultRenderPipeline = prevD; QualitySettings.renderPipeline = prevQ; }
        End("u3_sky.txt", "U3 SKY FEED", sw);
    }

    /// <summary>U3: the sun swap (SceneSky.SunSwap, beginCameraRendering to endCameraRendering; earlier variants at recording / execution time had no effect): does turning SkySun change the sky, and does the
    /// chara light stay URP's main light (a white Lambert sphere on layer 10 lit by it, against the swap off)?</summary>
    public static void MeasureSwap()
    {
        Begin();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var urp = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset>(ProductionAsset);
        var prevD = GraphicsSettings.defaultRenderPipeline; var prevQ = QualitySettings.renderPipeline;
        try
        {
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;
            UseUrp(urp);
            using (var rig = new Rig(Services()))
            {
                var cam = rig.Runtime.GetPart<SceneViewCamera>().Camera;
                var sky = rig.Sky;
                rig.Set("effects.sky.enabled", true);
                rig.Set("effects.sky.pattern", ContentId.Vanilla(0).ToString());
                rig.Step();
                cam.transform.SetPositionAndRotation(new Vector3(0f, 5f, 30f), Quaternion.Euler(-20f, 180f, 0f));
                cam.fieldOfView = 60f;
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                UnityEngine.Object.DestroyImmediate(sphere.GetComponent<Collider>());
                var mat = new Material(Shader.Find("HS2Next/Legacy Shaders/Diffuse")) { color = Color.white };
                sphere.GetComponent<Renderer>().sharedMaterial = mat;
                sphere.layer = 10;
                HS2Next.RenderingLayers.ApplyToRenderers(sphere);
                sphere.transform.position = cam.transform.position + cam.transform.forward * 6f;
                sphere.transform.localScale = Vector3.one * 2f;
                Info("main light before: " + (HS2Next.MainLight.Current ? HS2Next.MainLight.Current.name : "none") + ", RenderSettings.sun " + (RenderSettings.sun ? RenderSettings.sun.name : "none"));
                (Color sky, Color ball, Color[] px) Shot(Vector3 sunEuler)
                {
                    sky.SkySun.eulerAngles = sunEuler;
                    var px = RenderTo(cam, 320, 180, false, out var t);
                    UnityEngine.Object.DestroyImmediate(t);
                    return (Mean(px, 320, 180, 150, 180), px[90 * 320 + 160], px);
                }
                SceneSky.FeedEnabled = false;
                var off = Shot(new Vector3(40, 0, 0));
                Info($"swap off: sky top ({off.sky.r * 255:F1}, {off.sky.g * 255:F1}, {off.sky.b * 255:F1}), sphere centre ({off.ball.r * 255:F1}, {off.ball.g * 255:F1}, {off.ball.b * 255:F1})");
                SceneSky.FeedEnabled = true;
                for (int mode = 1; mode < 2; mode++)
                {
                    var a = Shot(new Vector3(40, 0, 0));
                    var b = Shot(new Vector3(40, 180, 0));
                    var c = Shot(new Vector3(-40, 0, 0));
                    double d = 0; for (int i = 0; i < a.px.Length; i++) d += Mathf.Abs(a.px[i].b - c.px[i].b);
                    Info($"mode {mode}: sky top ({a.sky.r * 255:F1}, {a.sky.g * 255:F1}, {a.sky.b * 255:F1}), sun below the horizon changes the frame by {d / a.px.Length * 255:F1}/255 (blue); sphere centre ({a.ball.r * 255:F1}, {a.ball.g * 255:F1}, {a.ball.b * 255:F1}) vs swap off ({off.ball.r * 255:F1}); RenderSettings.sun after: {(RenderSettings.sun ? RenderSettings.sun.name : "none")}");
                }
                UnityEngine.Object.DestroyImmediate(sphere);
                UnityEngine.Object.DestroyImmediate(mat);
            }
        }
        catch (Exception e) { Check("ran", false, e.ToString()); }
        finally { SceneSky.FeedEnabled = true; GraphicsSettings.defaultRenderPipeline = prevD; QualitySettings.renderPipeline = prevQ; }
        End("u3_swap.txt", "U3 SUN SWAP", sw);
    }
}
