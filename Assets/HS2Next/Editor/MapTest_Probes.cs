// MapTest.RunProbeProbe (Phase 3.3 follow-up of package A): why characters on a map that
// ships light probes get the ambient probe instead of the map's probes (hooh.minimap.bushitsu#0: 771 probes; HS2's
// LightProbes.GetInterpolatedProbe at the origin 0.0266, HS2Next's = the ambient probe). Play mode, a standalone
// MapRealization; prints the probe set, the interpolated probe at the origin and at the first probe, before and after an
// explicit tetrahedralization and an AmbientMode.Custom write, and what a fresh renderer standing there gets.
// Run: & Tools\unity_batch.ps1 -Method MapTest.RunProbeProbe -NoQuit [-MethodArgs '-maps','hooh.minimap.bushitsu#0']
// Report: Spike/out/maps/map_probe_probe.txt.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

public static partial class MapTest
{
    static string ProbeReport => Path.Combine(OutDir, "map_probe_probe.txt");

    public static void RunProbeProbe()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(ProbeReport, "SUMMARY: 0 PASS, 1 FAIL\nstarted\n", new UTF8Encoding(false));
        PlayModeHarness.Start(nameof(MapTest) + "." + nameof(ProbeMain), ProbeReport, 15);
    }

    static string Sh0(SphericalHarmonicsL2 sh) => $"{sh[0, 0]:F6} / {sh[1, 0]:F6} / {sh[2, 0]:F6}";

    static string ProbeState(string what)
    {
        var lp = LightmapSettings.lightProbes;
        LightProbes.GetInterpolatedProbe(Vector3.zero, null, out var o);
        string s = $"{what}: lightProbes {(lp ? lp.count : -1)} (cells {(lp ? lp.cellCount : -1)}), ambientMode {RenderSettings.ambientMode}, ambientProbe {Sh0(RenderSettings.ambientProbe)}, interpolated at origin {Sh0(o)}";
        if (lp && lp.count > 0)
        {
            var pos = lp.positions;
            var b = new Bounds(pos[0], Vector3.zero);
            foreach (var p in pos) b.Encapsulate(p);
            var co = lp.bakedProbes;
            LightProbes.GetInterpolatedProbe(pos[0], null, out var at0);
            double mean = co.Average(x => (double)x[0, 0]);
            s += $"\n      bounds {b.min} .. {b.max}; probe 0 at {pos[0]} sh0 {Sh0(co[0])}, interpolated there {Sh0(at0)}; mean sh0 R {mean:F6}";
            LightProbes.GetInterpolatedProbe(b.center, null, out var atc);
            s += $"\n      interpolated at the probe centre {b.center}: {Sh0(atc)}";
        }
        return s;
    }

    /// <summary>Renders matte white spheres (every light switched off for the render) at the origin (below the probe hull)
    /// and at (0, 10, 0) (inside it) and returns their centre pixels: the SH the renderer really gets.</summary>
    /// <summary>The linear centre pixel of a matte white sphere at <paramref name="p"/> lit by no light (URP must be current):
    /// the ambient / probe SH a renderer there really gets.</summary>
    public static Color RenderedAmbientAt(Vector3 p, bool fallback = true)
    {
        RenderedSh("x", new[] { p }, out var cs, fallback);
        return cs[0];
    }

    static string RenderedSh(string what, bool fallback = false) => RenderedSh(what, new[] { Vector3.zero, new Vector3(0, 10, 0) }, out _, fallback);

    /// <param name="fallback">apply MapProbeFallback to the sphere first (what the map realization does each frame)</param>
    static string RenderedSh(string what, Vector3[] points, out Color[] colors, bool fallback)
    {
        colors = new Color[points.Length];
        int ci = 0;
        // no realtime light may reach the spheres: every enabled light is switched off for these renders
        var off = new List<Light>();
        foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            if (l.enabled) { l.enabled = false; off.Add(l); }
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var mat = new Material(lit);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Smoothness", 0f);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_EnvironmentReflections", 0f);
        mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        mat.SetFloat("_SpecularHighlights", 0f);
        mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        var res = new StringBuilder(what + ":");
        foreach (var p in points)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.position = p;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.renderingLayerMask = 1u << 31;
            r.shadowCastingMode = ShadowCastingMode.Off;
            if (fallback) MapProbeFallback.Apply(r);
            var camGo = new GameObject("probe cam");
            var cam = camGo.AddComponent<Camera>();
            camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            cam.transform.position = p + new Vector3(0, 0, 3);
            cam.transform.LookAt(p);
            cam.fieldOfView = 10;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 10f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            go.layer = 31;                 // the camera sees only the sphere (characters or map objects at the point
            cam.cullingMask = 1 << 31;     // would hide it)
            var rt = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBHalf);
            cam.targetTexture = rt;
            cam.Render();
            var tex = new Texture2D(64, 64, TextureFormat.RGBAHalf, false);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
            RenderTexture.active = prev;
            var c = tex.GetPixel(32, 32);
            colors[ci++] = c;
            LightProbes.GetInterpolatedProbe(p, r, out var api);
            res.Append($" at {p}: rendered {c.r:F4} {c.g:F4} {c.b:F4} (API {api[0, 0]:F4})");
            UnityEngine.Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(camGo);
            UnityEngine.Object.DestroyImmediate(go);
        }
        UnityEngine.Object.DestroyImmediate(mat);
        foreach (var l in off) if (l) l.enabled = true;
        return res.ToString();
    }

    static IEnumerator ProbeMain()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        Section("probes", "map light probes");
        var sv = PlayServices();
        var pipe = new HS2Next.App.AppPipeline();   // the app's URP asset (the harness alone runs Built-in)
        pipe.Apply();
        Info("pipeline: " + pipe.Summary);
        string mref = Arg("-maps") ?? "hooh.minimap.bushitsu#0";
        Info(ProbeState("before the map"));
        var findings = new SceneFindings();
        var map = new MapRealization(sv.Catalog, sv.Db, findings, sv.Hs2Root);
        bool[] ok = { false };
        // a renderer that exists before the map loads (as a character does)
        var early = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        early.name = "early probe user";
        var er = early.GetComponent<MeshRenderer>();
        Info($"early renderer: lightProbeUsage {er.lightProbeUsage}, anchor {(er.probeAnchor ? er.probeAnchor.name : "none")}");
        string atLoaded = null;
        Action<MapRealization> onLoaded = m => atLoaded = $"at MapHooks.Loaded: LightmapSettings.lightProbes {(LightmapSettings.lightProbes ? LightmapSettings.lightProbes.count : -1)}, " +
            $"shared for the scene {(LightProbes.GetSharedLightProbesForScene(m.Scene) is LightProbes sp0 && sp0 ? sp0.count : -1)}, frame {Time.frameCount}";
        MapHooks.Loaded += onLoaded;
        map.SetTarget(mref, Vec3.Zero, Vec3.Zero);
        yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return !map.IsBusy; }, 300, ok);
        MapHooks.Loaded -= onLoaded;
        Info(atLoaded ?? "Loaded not raised");
        Info($"{mref}: {map.State}; tetrahedralized {map.ProbesTetrahedralized}; lightmaps {map.Lightmaps.SceneLightmaps}, probes {map.Lightmaps.Probes}");
        Info(ProbeState("after the load"));
        {
            var hullR = GameObject.CreatePrimitive(PrimitiveType.Sphere).GetComponent<Renderer>();
            var sbp = new StringBuilder("outside-hull API values right after the load:");
            foreach (var q in new[] { Vector3.zero, new Vector3(0, -100, 0), new Vector3(200, 10, 0), new Vector3(0, 10, 100), new Vector3(-24, 2, -60) })
            {
                hullR.transform.position = q;
                LightProbes.GetInterpolatedProbe(q, hullR, out var v);
                sbp.Append($" {q}: {v[0, 0]:F4} L1 {v[0, 1]:F4} {v[0, 2]:F4} {v[0, 3]:F4};");
            }
            Info(sbp.ToString());
            var sbh = new StringBuilder("null-renderer API up the y axis from the origin (HS2 at the origin: 0.0266 / L1 -0.0021 -0.0043 -0.0092):");
            foreach (var y in new[] { 0f, 1f, 1.4f, 1.5f, 1.6f, 2f, 3f, 5f })
            {
                LightProbes.GetInterpolatedProbe(new Vector3(0, y, 0), null, out var v);
                sbh.Append($" y {y}: {v[0, 0]:F4} L1 {v[0, 1]:F4} {v[0, 2]:F4} {v[0, 3]:F4};");
            }
            Info(sbh.ToString());
            UnityEngine.Object.DestroyImmediate(hullR.gameObject);
        }
        LightProbes.GetInterpolatedProbe(new Vector3(0, 10, 0), null, out var inHull);
        Info($"Skybox mode: at (0,10,0) null renderer (inside the hull) {Sh0(inHull)}");
        foreach (var d in LightmapSettings.lightmaps)
            if (d != null && d.lightmapColor) Info($"lightmap {d.lightmapColor.name}: {d.lightmapColor.format} / {d.lightmapColor.graphicsFormat}, {d.lightmapColor.width}");
        foreach (var m in typeof(UnityEditor.PlayerSettings).GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            if (m.Name.Contains("LightmapEncoding") && m.Name.StartsWith("Get") && m.GetParameters().Length == 1)
            {
                try
                {
                    var pt = m.GetParameters()[0].ParameterType;
                    object arg = pt == typeof(UnityEditor.BuildTargetGroup) ? (object)UnityEditor.BuildTargetGroup.Standalone :
                        pt == typeof(UnityEditor.BuildTarget) ? (object)UnityEditor.BuildTarget.StandaloneWindows64 :
                        pt.IsEnum ? Enum.Parse(pt, "Standalone") : (object)UnityEditor.Build.NamedBuildTarget.Standalone;
                    Info($"{m.Name}({arg}) = {m.Invoke(null, new[] { arg })}");
                }
                catch (Exception e) { Info(m.Name + "(" + m.GetParameters()[0].ParameterType.FullName + ") threw " + e.GetType().Name + ": " + (e.InnerException ?? e).Message); }
            }
        foreach (var l in map.Structure.Lights)
            Info($"light {l.name}: type {l.type}, bake {l.bakingOutput.lightmapBakeType}, isBaked {l.bakingOutput.isBaked}, mixed {l.bakingOutput.mixedLightingMode}, color {l.color}, intensity {l.intensity}, shadows {l.shadows}");
        int lmR = 0, probeR = 0, offR = 0;
        foreach (var r in map.Structure.Renderers)
        {
            if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (ShaderMap.IsLightmapped(r)) lmR++; else if (r.lightProbeUsage != LightProbeUsage.Off) probeR++; else offR++;
        }
        Info($"map renderers: lightmapped {lmR}, probe-lit {probeR}, probes off (ambient) {offR}; lightmap mode {LightmapSettings.lightmapsMode}");
        yield return PlayModeHarness.WaitFrames(5);
        Info(ProbeState("5 frames later"));
        LightProbes.Tetrahedralize();
        Info(ProbeState("after LightProbes.Tetrahedralize()"));
        var keepMode = RenderSettings.ambientMode;
        var keepProbe = RenderSettings.ambientProbe;
        var custom = new SphericalHarmonicsL2();
        custom.AddAmbientLight(new Color(5, 5, 5));
        RenderSettings.ambientMode = AmbientMode.Custom;
        RenderSettings.ambientProbe = custom;
        yield return null;
        Info(ProbeState("after AmbientMode.Custom with a bright ambient probe"));
        LightProbes.GetInterpolatedProbe(Vector3.zero, er, out var underCustom);
        LightProbes.GetInterpolatedProbe(new Vector3(0, 10, 0), null, out var underCustomIn);
        Info($"Custom: GetInterpolatedProbe(origin, early renderer) {Sh0(underCustom)}; at (0,10,0) null renderer (inside the hull) {Sh0(underCustomIn)}");
        LightProbes.Tetrahedralize();
        Info(ProbeState("Custom + Tetrahedralize()"));
        string rCustom = RenderedSh("Custom 5");
        RenderSettings.ambientMode = keepMode;
        RenderSettings.ambientProbe = keepProbe;
        yield return null;
        string rSky = RenderedSh("scene mode");
        Info("rendered (a white matte sphere lit by no light, centre pixel, linear): " + rSky + " | " + rCustom);
        Info("with MapProbeFallback: " + RenderedSh("scene mode + fallback", true));
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.white; DynamicGI.UpdateEnvironment();
        yield return null;
        string rFlat = RenderedSh("Flat white");
        RenderSettings.ambientMode = AmbientMode.Trilight; RenderSettings.ambientSkyColor = Color.white; RenderSettings.ambientEquatorColor = Color.white; RenderSettings.ambientGroundColor = Color.white; DynamicGI.UpdateEnvironment();
        yield return null;
        string rTri = RenderedSh("Trilight white");
        var one = new SphericalHarmonicsL2(); one.AddAmbientLight(Color.white);
        RenderSettings.ambientMode = AmbientMode.Custom; RenderSettings.ambientProbe = one;
        yield return null;
        string rC1 = RenderedSh("Custom 1");
        Info("more modes: " + rFlat + " | " + rTri + " | " + rC1);
        RenderSettings.ambientMode = keepMode;
        RenderSettings.ambientProbe = keepProbe;
        DynamicGI.UpdateEnvironment();
        yield return null;
        Info("after Skybox + DynamicGI.UpdateEnvironment: " + RenderedSh("Skybox updated") + "; ambientProbe " + Sh0(RenderSettings.ambientProbe));
        Info("   … with MapProbeFallback: " + RenderedSh("Skybox updated + fallback", true));
        var skyMat = RenderSettings.skybox;
        RenderSettings.skybox = skyMat ? new Material(skyMat) : null;
        yield return null;
        Info("after a skybox change: " + RenderedSh("Skybox swapped") + "; ambientProbe " + Sh0(RenderSettings.ambientProbe));
        RenderSettings.ambientMode = AmbientMode.Custom; RenderSettings.ambientProbe = new SphericalHarmonicsL2();
        yield return null;
        Info("Custom zero: " + RenderedSh("Custom 0") + "; ambientProbe " + Sh0(RenderSettings.ambientProbe));
        RenderSettings.ambientMode = keepMode; RenderSettings.ambientIntensity = 0f; DynamicGI.UpdateEnvironment();
        yield return null;
        Info("Skybox, intensity 0: " + RenderedSh("Skybox i0") + "; ambientProbe " + Sh0(RenderSettings.ambientProbe));
        var lp = LightmapSettings.lightProbes;
        Info($"LightProbes.GetSharedLightProbesForScene(map scene): {(LightProbes.GetSharedLightProbesForScene(map.Scene) is LightProbes sp && sp ? sp.count.ToString() : "null")}; " +
             $"instantiated? lightProbes == shared {(lp && lp == LightProbes.GetSharedLightProbesForScene(map.Scene))}");
        // a renderer of the map with probes on
        var mr = map.Structure.Renderers.FirstOrDefault(r => r && r.lightProbeUsage != LightProbeUsage.Off && !ShaderMap.IsLightmapped(r));
        if (mr) Info($"a map renderer with probes: {mr.name} usage {mr.lightProbeUsage} at {mr.bounds.center}");
        var late = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var lr = late.GetComponent<MeshRenderer>();
        LightProbes.GetInterpolatedProbe(Vector3.zero, lr, out var forR);
        LightProbes.GetInterpolatedProbe(Vector3.zero, er, out var forE);
        Info($"GetInterpolatedProbe(origin, late renderer) {Sh0(forR)}; early renderer {Sh0(forE)}");
        var arr = new Vector3[] { Vector3.zero, new Vector3(0, 1, 0), new Vector3(0, 10, 0) };
        var shs = new SphericalHarmonicsL2[3];
        var occ = new Vector4[3];
        LightProbes.CalculateInterpolatedLightAndOcclusionProbes(arr, shs, occ);
        Info($"CalculateInterpolatedLightAndOcclusionProbes: {string.Join(" | ", shs.Select(Sh0))}");
        Check("ran", true);
        map.Clear();
        yield return PlayModeHarness.WaitUntil(() => { map.Apply(); return map.PendingUnloads == 0; }, 60, ok);
        Info(ProbeState("after the unload"));

        // a character of the scene runtime on the map: its renderers' probe usage and the SH they get
        var f = SceneRuntimeTest.BuildFixture(sv, null, true);
        f.Session.SetMap(mref);
        var part = f.Runtime.GetPart<MapRealization>();
        yield return PlayModeHarness.WaitUntil(() => { f.Runtime.Apply(); return part.State == MapState.Loaded && !part.IsBusy; }, 300, ok);
        yield return PlayModeHarness.WaitFrames(3);
        var br = f.Runtime.FindChara(f.BrChan);
        if (br != null && br.Root)
        {
            var rs = br.Root.GetComponentsInChildren<Renderer>(true);
            yield return PlayModeHarness.WaitFrames(3);
            var usage = rs.GroupBy(r => r.lightProbeUsage + (r.probeAnchor ? " anchor " + r.probeAnchor.name : "")).Select(g => g.Key + " x" + g.Count());
            Info($"probe fallback: active {part.ProbeFallback.Active}, overridden renderers {part.ProbeFallback.Overridden}");
            Info($"BR-Chan at {br.Root.transform.position}: renderers {rs.Length}: {string.Join(", ", usage)}");
            var body = rs.OfType<SkinnedMeshRenderer>().FirstOrDefault(r => r.sharedMesh && r.sharedMesh.vertexCount > 5000) ?? rs.FirstOrDefault();
            if (body)
            {
                LightProbes.GetInterpolatedProbe(body.bounds.center, body, out var bsh);
                Info($"a body renderer {body.name} (usage {body.lightProbeUsage}) bounds centre {body.bounds.center}: the probe it would get {Sh0(bsh)}");
            }
        }
        f.Dispose();
        pipe.Restore();
        End("map_probe_probe.txt", "MAP PROBE PROBE", sw);
        PlayModeHarness.ExitCode = fails > 0 ? 1 : 0;
    }
}
