// Batch test of the app's render pipeline (Phase 3.2 package S):
// the production URP asset and the fitted shaders' Forward+ / light-layer / lightmap / reflection-probe paths.
// Run (graphics device; not -nographics): Tools/unity_batch.ps1 -Method RenderPipelineTest.Run [-MethodArgs '-card','<path>']
// -only P2,P5,...: run a subset (P1 always). Report: Spike/out/render/render_pipeline_report.txt (first and last line "SUMMARY: N PASS, M FAIL"); exit 1 on a FAIL.
//   P1 settings: Assets/HS2Next/Settings/HS2NextURP.asset (Forward+ renderer, rendering layers, main shadows 4096 / 4
//      cascades, additional-light shadows with a 4096 atlas, soft shadows, probe blending + box projection, SRP Batcher on,
//      GPU Resident Drawer off, HDR), 32 defined rendering-layer names, lightmap stripping Custom (keep all), the app's
//      start-up pipeline step (AppPipeline.Apply / Restore) and the player build use the production asset.
//   P2 shader variants: every shader under Assets/HS2Next/Shaders without errors; the forward pass of every lit fitted shader
//      compiles with Forward+, light layers, cascaded soft shadows and additional-light shadows; the Standard family also
//      with every lightmap mode (plain, directional, subtractive, shadowmask; Forward and Forward+).
//   P3 exactness: the female visual target, one directional light without shadows, flat ambient: the production asset
//      renders exactly what the measurement asset (Forward, no rendering layers) renders (max |d| linear, float target).
//      P3 and P4 compare the pipelines' lighting, so they run with the wide mosaic (render.mosaicWide, a feature of the
//      production renderer only) off - off, its pass is not enqueued; its own effect on the same view is measured first
//      (info: the pixels it changes and how many of them lie outside its groups' dilated region).
//   P4 Forward+: the same character under 12 point lights: the 12-light image equals the sum of the single-light images
//      (Forward's per-object limit would drop lights); Spike's Forward result is reported for contrast.
//   P5 light layers: a Chara-only directional light and a Chara-only point light light a layer-10 cube and not a layer-11
//      cube (HS2Next/Standard, legacy Diffuse, AIT main standard2 studio); the chara light (SceneCharaLight) carries HS2's
//      culling mask and lights layers 10 / 12, not 0 / 11; ShaderMap.ApplyUrp gives renderers 1 << layer.
//   P6 lightmaps: a lightmapped quad (runtime LightmapSettings, non-directional and directional) on HS2Next/Standard
//      equals URP/Lit on the same lightmap (diffuse 0.96 x lightmap, no realtime light, black environment); unlit without;
//      ShaderMap.SupportsLightmaps / RouteLightmapped.
//   P7 reflection probes: a chrome sphere between a red and a blue Custom probe (blend distance 2) mixes both under the
//      production asset; a probe's box projection changes the reflection (production) and not under the measurement asset.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.App;
using HS2Next.Chara;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.Save;
using HS2Next.Scenes.Runtime;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class RenderPipelineTest
{
    public const string RendererPath = "Assets/HS2Next/Settings/HS2NextURP_Renderer.asset";
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "render");

    static readonly StringBuilder sb = new StringBuilder();
    static int pass, fail;
    static readonly List<Object> trash = new List<Object>();

    static string Arg(string name, string def)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static void Check(string id, bool ok, string detail)
    {
        if (ok) pass++; else fail++;
        string line = $"{(ok ? "PASS" : "FAIL")} {id}: {detail}";
        sb.AppendLine(line);
        Debug.Log("RENDER PIPELINE " + line);
    }

    static void Info(string line) { sb.AppendLine("     " + line); Debug.Log("RENDER PIPELINE " + line); }

    // Runs over editor updates (-NoQuit): Custom reflection probes take effect only after an editor update (measured:
    // the first render after creating them shows no probe, the one after the next update blends both).
    public static void Run()
    {
        sb.Clear(); pass = fail = 0;
        var prevDefault = GraphicsSettings.defaultRenderPipeline;
        var prevQuality = QualitySettings.renderPipeline;
        bool prevAsync = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        IEnumerator<object> it = null;
        try { it = Main(); }
        catch (Exception e) { Check("run", false, "exception " + e); }
        void Finish()
        {
            EditorApplication.update -= Step;
            Cleanup();
            GraphicsSettings.defaultRenderPipeline = prevDefault;
            QualitySettings.renderPipeline = prevQuality;
            ShaderUtil.allowAsyncCompilation = prevAsync;
            string summary = $"SUMMARY: {pass} PASS, {fail} FAIL";
            File.WriteAllText(Path.Combine(OutDir, "render_pipeline_report.txt"), summary + "\n" + sb + summary + "\n");
            Debug.Log("RENDER PIPELINE TEST DONE " + summary);
            EditorApplication.Exit(fail == 0 && pass > 0 ? 0 : 1);
        }
        void Step()
        {
            bool more = false;
            try { more = it != null && it.MoveNext(); }
            catch (Exception e) { Check("run", false, "exception " + e); more = false; }
            if (!more) Finish();
        }
        EditorApplication.update += Step;
    }

    static IEnumerator<object> Main()
    {
        Directory.CreateDirectory(OutDir);
        sb.AppendLine($"render pipeline test {DateTime.Now:yyyy-MM-dd HH:mm:ss}, Unity {Application.unityVersion}, device {SystemInfo.graphicsDeviceType}");
        var prod = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AppPipeline.EditorAssetPath);
        var spike = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AppPipeline.MeasureAssetPath);
        // -only P2+P5+...: a subset (P1 always; P3 and P4 run together)
        var only = Arg("-only", null)?.Split(',', ';', '+');
        bool Want(string id) => only == null || only.Contains(id);
        if (only != null) sb.AppendLine("(only " + string.Join(",", only) + ")");
        Settings(prod);
        if (Want("P2")) Variants();
        if (!prod || !spike || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            Check("P3-P7", false, "no production / measurement asset or no graphics device (" + SystemInfo.graphicsDeviceType + ")");
            yield break;
        }
        if (Want("P5")) RunScene("P5", () => LightLayers(prod, spike));
        if (Want("P6")) RunScene("P6", () => Lightmaps(prod));
        if (Want("P3") || Want("P4")) RunScene("P3/P4", () => Character(prod, spike));
        if (Want("P7"))
        {
            var probes = Probes(prod, spike);
            while (true)
            {
                bool more;
                try { more = probes.MoveNext(); }
                catch (Exception e) { Check("P7", false, "exception " + e); more = false; }
                if (!more) break;
                yield return null;
            }
            Cleanup();
        }
    }

    static void RunScene(string id, Action a)
    {
        try { a(); }
        catch (Exception e) { Check(id, false, "exception " + e); }
        finally { Cleanup(); }
    }

    static void Cleanup()
    {
        foreach (var o in trash) if (o) Object.DestroyImmediate(o);
        trash.Clear();
        LightmapSettings.lightmaps = new LightmapData[0];
    }

    static T Keep<T>(T o) where T : Object { trash.Add(o); return o; }

    static void SetPipeline(RenderPipelineAsset rp) { GraphicsSettings.defaultRenderPipeline = rp; QualitySettings.renderPipeline = rp; }

    // ------------------------------------------------------------------ P1

    static void Settings(UniversalRenderPipelineAsset a)
    {
        Check("P1.asset", a != null, AppPipeline.EditorAssetPath + (a ? " loaded" : " NOT FOUND"));
        if (!a) return;
        var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        var so = new SerializedObject(a);
        var list = so.FindProperty("m_RendererDataList");
        bool refs = list != null && list.arraySize == 1 && list.GetArrayElementAtIndex(0).objectReferenceValue == rd;
        Check("P1.renderer", rd && refs && rd.renderingMode == RenderingMode.ForwardPlus,
            $"{RendererPath}: {(rd ? rd.renderingMode.ToString() : "missing")}, the only renderer of the asset {refs}; copy depth {(rd ? rd.copyDepthMode.ToString() : "-")}, features {(rd ? rd.rendererFeatures.Count : -1)}");
        Check("P1.layers", a.useRenderingLayers, "use rendering layers " + a.useRenderingLayers);
        Check("P1.mainShadows", a.supportsMainLightShadows && a.mainLightShadowmapResolution == 4096 && a.shadowCascadeCount == 4,
            $"main light shadows {a.supportsMainLightShadows}, {a.mainLightShadowmapResolution}, cascades {a.shadowCascadeCount}, distance {a.shadowDistance} m");
        Check("P1.additionalShadows", a.supportsAdditionalLightShadows && a.additionalLightsShadowmapResolution == 4096 && a.additionalLightsRenderingMode == LightRenderingMode.PerPixel,
            $"additional lights {a.additionalLightsRenderingMode}, shadows {a.supportsAdditionalLightShadows}, atlas {a.additionalLightsShadowmapResolution}");
        int sq = so.FindProperty("m_SoftShadowQuality")?.intValue ?? -1;
        Check("P1.softShadows", a.supportsSoftShadows && sq == (int)SoftShadowQuality.High, $"soft shadows {a.supportsSoftShadows}, quality {(SoftShadowQuality)sq}");
        bool atlas = so.FindProperty("m_ReflectionProbeAtlas")?.boolValue ?? false;
        Check("P1.probes", a.reflectionProbeBlending && a.reflectionProbeBoxProjection && atlas,
            $"probe blending {a.reflectionProbeBlending}, box projection {a.reflectionProbeBoxProjection}, atlas {atlas} (the fitted shaders do not declare _REFLECTION_PROBE_ATLAS: Forward+ blending samples the atlas)");
        Check("P1.misc", a.useSRPBatcher && a.gpuResidentDrawerMode == GPUResidentDrawerMode.Disabled && a.supportsHDR && a.msaaSampleCount == 1 && a.colorGradingMode == ColorGradingMode.LowDynamicRange,
            $"SRP Batcher {a.useSRPBatcher}, GPU Resident Drawer {a.gpuResidentDrawerMode}, HDR {a.supportsHDR}, MSAA {a.msaaSampleCount}, grading {a.colorGradingMode} (3.3)");
        int defined = RenderingLayerMask.GetDefinedRenderingLayerCount();
        uint all = RenderingLayerMask.GetDefinedRenderingLayersCombinedMaskValue();
        Check("P1.layerNames", defined == 32 && all == 0xFFFFFFFFu && RenderingLayerMask.RenderingLayerToName(10) == "Chara" && RenderingLayerMask.RenderingLayerToName(11) == "Map",
            $"defined rendering layers {defined}, combined mask 0x{all:X8}, 10 = {RenderingLayerMask.RenderingLayerToName(10)}, 11 = {RenderingLayerMask.RenderingLayerToName(11)}, 12 = {RenderingLayerMask.RenderingLayerToName(12)}");
        string gs = File.ReadAllText(Path.Combine(ContentSettings.ProjectRoot, "ProjectSettings", "GraphicsSettings.asset"));
        bool custom = gs.Contains("m_LightmapStripping: 1") && new[] { "Plain", "DirCombined", "ShadowMask", "Subtractive" }.All(k => gs.Contains($"m_LightmapKeep{k}: 1"));
        Check("P1.lightmapStripping", custom, "GraphicsSettings lightmap stripping Custom with plain / directional / shadowmask / subtractive kept " + custom +
              " (maps come from bundles: Automatic would strip every lightmap variant from the player)");
        // the app's start-up step
        SetPipeline(null);
        var ap = new AppPipeline();
        ap.Apply();
        bool applied = GraphicsSettings.currentRenderPipeline == a;
        ap.Restore();
        Check("P1.app", applied && GraphicsSettings.currentRenderPipeline == null && PlayerBuild.UrpAssetPath == AppPipeline.EditorAssetPath,
            $"AppPipeline.Apply → {(applied ? "production asset" : "other")} ({ap.Summary}); Restore → {(GraphicsSettings.currentRenderPipeline ? GraphicsSettings.currentRenderPipeline.name : "Built-in")}; PlayerBuild.UrpAssetPath {PlayerBuild.UrpAssetPath}");
    }

    // ------------------------------------------------------------------ P2

    static readonly string[] StdFamily = { "HS2Next/Standard", "HS2Next/Standard (Specular setup)", "HS2Next/Standard (Roughness setup)", "HS2Next/Standard(Two Sided)", "HS2Next/Compatible/Standard DS (flipped back faces)" };

    static void Variants()
    {
        var paths = AssetDatabase.FindAssets("t:Shader", new[] { PlayerBuild.ShaderDir }).Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".shader")).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var shaders = paths.Select(AssetDatabase.LoadAssetAtPath<Shader>).Where(s => s).ToList();
        var bad = shaders.Where(ShaderUtil.ShaderHasError).Select(s => s.name).ToList();
        Check("P2.errors", bad.Count == 0 && shaders.Count == paths.Count, $"{shaders.Count} shaders, with errors: {(bad.Count == 0 ? "none" : string.Join(", ", bad))}");
        string[] fplus = { "_CLUSTER_LIGHT_LOOP", "_LIGHT_LAYERS", "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT", "_ADDITIONAL_LIGHT_SHADOWS" };
        string[] fwd = { "_ADDITIONAL_LIGHTS", "_LIGHT_LAYERS", "_MAIN_LIGHT_SHADOWS", "_ADDITIONAL_LIGHT_SHADOWS" };
        var lmModes = new (string name, string[] kw)[]
        {
            ("plain", new[] { "LIGHTMAP_ON" }), ("directional", new[] { "LIGHTMAP_ON", "DIRLIGHTMAP_COMBINED" }),
            ("subtractive", new[] { "LIGHTMAP_ON", "LIGHTMAP_SHADOW_MIXING" }), ("shadowmask", new[] { "LIGHTMAP_ON", "LIGHTMAP_SHADOW_MIXING", "SHADOWS_SHADOWMASK" }),
            ("probes shadowmask", new[] { "LIGHTMAP_SHADOW_MIXING", "SHADOWS_SHADOWMASK" }),
        };
        int lit = 0, compiled = 0; var failed = new List<string>(); var noCluster = new List<string>();
        foreach (var s in shaders)
        {
            var space = s.keywordSpace;
            if (!space.FindKeyword("_ADDITIONAL_LIGHTS").isValid) continue;   // unlit families
            lit++;
            if (!space.FindKeyword("_CLUSTER_LIGHT_LOOP").isValid || !space.FindKeyword("_LIGHT_LAYERS").isValid) { noCluster.Add(s.name); continue; }
            var sets = new List<(string, string[])> { ("Forward+", fplus), ("Forward", fwd) };
            if (StdFamily.Contains(s.name))
                foreach (var (n, kw) in lmModes) { sets.Add(("Forward+ " + n, fplus.Concat(kw).ToArray())); sets.Add(("Forward " + n, fwd.Concat(kw).ToArray())); }
            foreach (var (label, kw) in sets)
            {
                string err = CompileForward(s, kw);
                if (err == null) compiled++; else failed.Add($"{s.name} [{label}]: {err}");
            }
        }
        Check("P2.keywords", noCluster.Count == 0 && lit > 0, $"{lit} lit shaders declare _CLUSTER_LIGHT_LOOP and _LIGHT_LAYERS; missing: {(noCluster.Count == 0 ? "none" : string.Join(", ", noCluster))}");
        Check("P2.compile", failed.Count == 0 && compiled > 0, $"{compiled} forward variants compiled (D3D, Standalone Windows 64); failed: {(failed.Count == 0 ? "none" : string.Join(" | ", failed.Take(5)))}");
        var noLm = StdFamily.Where(n => !ShaderMap.SupportsLightmaps(Shader.Find(n))).ToList();
        Check("P2.lightmapFamily", noLm.Count == 0 && !ShaderMap.SupportsLightmaps(Shader.Find("HS2Next/AIT main standard2 studio")) && ShaderMap.SupportsLightmaps(Shader.Find("Universal Render Pipeline/Lit")),
            $"SupportsLightmaps: Standard family {StdFamily.Length - noLm.Count}/{StdFamily.Length}, AIT main standard2 studio {ShaderMap.SupportsLightmaps(Shader.Find("HS2Next/AIT main standard2 studio"))}, URP/Lit {ShaderMap.SupportsLightmaps(Shader.Find("Universal Render Pipeline/Lit"))}");
    }

    // Compiles the vertex and fragment programs of the UniversalForward pass; null when both succeed.
    static string CompileForward(Shader s, string[] kw)
    {
        var data = ShaderUtil.GetShaderData(s);
        if (data == null || data.SubshaderCount == 0) return "no shader data";
        var sub = data.GetSubshader(0);
        for (int p = 0; p < sub.PassCount; p++)
        {
            var pass = sub.GetPass(p);
            if (pass.Name != "ForwardLit" && pass.Name != "FORWARD" && pass.Name != "Forward" && !pass.Name.Contains("Forward")) continue;
            foreach (var st in new[] { ShaderType.Vertex, ShaderType.Fragment })
            {
                var r = pass.CompileVariant(st, kw, ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64);
                if (!r.Success) return $"{pass.Name} {st}: " + string.Join("; ", r.Messages.Where(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).Select(m => m.message).Take(2));
            }
            return null;
        }
        return "no forward pass";
    }

    // ------------------------------------------------------------------ scene helpers

    static Camera NewCamera(Vector3 pos, Vector3 target, float fov = 30)
    {
        var cam = Keep(new GameObject("rp_cam")).AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        cam.fieldOfView = fov; cam.nearClipPlane = 0.05f; cam.farClipPlane = 100;
        cam.allowHDR = true; cam.allowMSAA = false;
        cam.transform.position = pos; cam.transform.LookAt(target);
        var d = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
        d.renderPostProcessing = false; d.antialiasing = AntialiasingMode.None;
        return cam;
    }

    // Linear float render of the camera (two frames: the first after a pipeline or scene change can be stale).
    static Color[] Capture(Camera cam, int w, int h)
    {
        foreach (var smr in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Exclude)) smr.forceMatrixRecalculationPerRender = true;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
        cam.targetTexture = rt;
        cam.Render(); cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var t = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
        t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply();
        RenderTexture.active = prev; cam.targetTexture = null;
        var px = t.GetPixels();
        Object.DestroyImmediate(t); Object.DestroyImmediate(rt);
        return px;
    }

    static void DarkEnvironment(Color ambient)
    {
        var sh = new SphericalHarmonicsL2(); sh.AddAmbientLight(ambient.linear);
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = ambient; RenderSettings.ambientProbe = sh;
        RenderSettings.skybox = null; RenderSettings.sun = null; RenderSettings.fog = false;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = ShaderProbe.BlackCube; RenderSettings.reflectionIntensity = 0;
    }

    static Light NewLight(LightType type, Vector3 pos, Vector3 euler, Color c, int cullingMask = -1, float range = 5)
    {
        var l = Keep(new GameObject("rp_light")).AddComponent<Light>();
        l.type = type; l.color = c; l.intensity = 1; l.shadows = LightShadows.None; l.range = range;
        l.transform.position = pos; l.transform.rotation = Quaternion.Euler(euler);
        l.cullingMask = cullingMask;
        RenderingLayers.ApplyToLight(l);
        return l;
    }

    static Material Mat(string shader, Action<Material> set = null)
    {
        var s = Shader.Find(shader);
        if (!s) throw new Exception("shader not found: " + shader);
        var m = Keep(new Material(s));
        set?.Invoke(m);
        return m;
    }

    static GameObject Prim(PrimitiveType t, Vector3 pos, float scale, Material m, int layer)
    {
        var g = Keep(GameObject.CreatePrimitive(t));
        Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.position = pos; g.transform.localScale = Vector3.one * scale;
        g.layer = layer;
        g.GetComponent<Renderer>().sharedMaterial = m;
        RenderingLayers.ApplyToRenderers(g);
        return g;
    }

    // mean linear rgb of the pixels inside a screen rectangle (fractions of the image)
    static Vector3 Mean(Color[] px, int w, int h, float x0, float x1, float y0, float y1)
    {
        double r = 0, g = 0, b = 0; int n = 0;
        for (int y = (int)(y0 * h); y < (int)(y1 * h); y++)
            for (int x = (int)(x0 * w); x < (int)(x1 * w); x++) { var c = px[y * w + x]; r += c.r; g += c.g; b += c.b; n++; }
        return n == 0 ? Vector3.zero : new Vector3((float)(r / n), (float)(g / n), (float)(b / n));
    }

    // mean around the screen position of a world point (half size in viewport fractions)
    static Vector3 Around(Color[] px, int w, int h, Camera cam, Vector3 world, float half)
    {
        cam.aspect = w / (float)h;
        var v = cam.WorldToViewportPoint(world);
        return Mean(px, w, h, v.x - half, v.x + half, v.y - half * w / h, v.y + half * w / h);
    }

    static string V(Vector3 v) => $"({v.x:F4}, {v.y:F4}, {v.z:F4})";

    // ------------------------------------------------------------------ P5 light layers

    static void LightLayers(UniversalRenderPipelineAsset prod, UniversalRenderPipelineAsset spike)
    {
        DarkEnvironment(Color.black);
        var shaders = new (string name, Action<Material> set)[]
        {
            ("HS2Next/Standard", m => { m.SetColor("_Color", Color.white); m.SetFloat("_Glossiness", 0); }),
            ("HS2Next/Legacy Shaders/Diffuse", m => m.SetColor("_Color", Color.white)),
            ("HS2Next/AIT main standard2 studio", null),
        };
        const int W = 192, H = 96;
        foreach (var (shader, set) in shaders)
        {
            var m = Mat(shader, set);
            var chara = Prim(PrimitiveType.Cube, new Vector3(-0.7f, 0, 0), 1f, m, RenderingLayers.Chara);
            var map = Prim(PrimitiveType.Cube, new Vector3(0.7f, 0, 0), 1f, m, RenderingLayers.Map);
            var cam = NewCamera(new Vector3(0, 0.8f, -4), Vector3.zero, 35);
            var unlit = new Dictionary<UniversalRenderPipelineAsset, (Vector3, Vector3)>();
            foreach (var rp in new[] { prod, spike })
            {
                SetPipeline(rp);
                var px0 = Capture(cam, W, H);
                unlit[rp] = (Around(px0, W, H, cam, chara.transform.position + new Vector3(0, 0, -0.5f), 0.03f),
                             Around(px0, W, H, cam, map.transform.position + new Vector3(0, 0, -0.5f), 0.03f));
            }
            foreach (var (kind, make) in new (string, Func<Light>)[]
            {
                ("directional", () => NewLight(LightType.Directional, Vector3.zero, new Vector3(30, 20, 0), Color.white, 1 << RenderingLayers.Chara)),
                ("point", () => NewLight(LightType.Point, new Vector3(0, 0.6f, -1.6f), Vector3.zero, Color.white, 1 << RenderingLayers.Chara, 6)),
            })
            {
                var l = make();
                foreach (var (rp, label) in new[] { (prod, "production"), (spike, "spike") })
                {
                    SetPipeline(rp);
                    var px = Capture(cam, W, H);
                    var a = Around(px, W, H, cam, chara.transform.position + new Vector3(0, 0, -0.5f), 0.03f);
                    var b = Around(px, W, H, cam, map.transform.position + new Vector3(0, 0, -0.5f), 0.03f);
                    a -= unlit[rp].Item1; b -= unlit[rp].Item2;   // the light's share (some shaders have a lit-independent term)
                    string d = $"{shader}, Chara-only {kind} light, added by the light: layer-10 cube {V(a)}, layer-11 cube {V(b)}";
                    if (label == "production") Check($"P5.{kind}.{shader}", a.magnitude > 0.05f && b.magnitude < 0.002f, d + $" (production; without the light {V(unlit[rp].Item1)} / {V(unlit[rp].Item2)})");
                    else Info(d + " (Spike, Forward culling masks)");
                }
                Object.DestroyImmediate(l.gameObject);
            }
            Object.DestroyImmediate(chara); Object.DestroyImmediate(map); Object.DestroyImmediate(cam.gameObject);
        }

        // the chara light: HS2's culling mask on layers 0 / 10 / 11 / 12
        {
            var m = Mat("HS2Next/Standard", x => { x.SetColor("_Color", Color.white); x.SetFloat("_Glossiness", 0); });
            int[] layers = { RenderingLayers.Default, RenderingLayers.Chara, RenderingLayers.Map, RenderingLayers.MapNoShadow };
            var cubes = new GameObject[layers.Length];
            for (int i = 0; i < layers.Length; i++) cubes[i] = Prim(PrimitiveType.Cube, new Vector3(-2.1f + 1.4f * i, 0, 0), 1f, m, layers[i]);
            var cam = NewCamera(new Vector3(0, 0.8f, -6.5f), Vector3.zero, 40);
            var part = new SceneCharaLight();
            part.Apply(cam, new CharaLightSettings { Shadow = false });
            var l = part.Light; Keep(l.gameObject);
            l.TryGetComponent<UniversalAdditionalLightData>(out var data);
            Check("P5.charaLightMask", l.cullingMask == RenderingLayers.CharaLightCullingMask && data && (uint)data.renderingLayers == unchecked((uint)RenderingLayers.CharaLightCullingMask),
                $"SceneCharaLight culling mask 0x{l.cullingMask:X8}, rendering layers 0x{(data ? (uint)data.renderingLayers : 0):X8} (HS2: 1, 2, 5, 8, 10, 12, 13-15, 19-23, 29-31)");
            SetPipeline(prod);
            const int W2 = 256, H2 = 96;
            var px = Capture(cam, W2, H2);
            var lit = new List<string>(); bool ok = true;
            for (int i = 0; i < layers.Length; i++)
            {
                var v = Around(px, W2, H2, cam, cubes[i].transform.position + new Vector3(0, 0, -0.5f), 0.02f);
                bool expect = layers[i] == RenderingLayers.Chara || layers[i] == RenderingLayers.MapNoShadow;
                ok &= expect ? v.magnitude > 0.05f : v.magnitude < 0.002f;
                lit.Add($"layer {layers[i]} {V(v)}");
            }
            Check("P5.charaLight", ok, "chara light (production): " + string.Join(", ", lit) + " (lit: 10 and 12 only)");
        }

        // ShaderMap.ApplyUrp: renderers get 1 << layer
        {
            var g = Keep(new GameObject("rp_root"));
            var c1 = Prim(PrimitiveType.Cube, Vector3.zero, 1, Mat("HS2Next/Standard"), 11); c1.transform.SetParent(g.transform);
            var c2 = Prim(PrimitiveType.Cube, Vector3.zero, 1, Mat("HS2Next/Standard"), 10); c2.transform.SetParent(g.transform);
            c1.GetComponent<Renderer>().renderingLayerMask = 1; c2.GetComponent<Renderer>().renderingLayerMask = 1;
            ShaderMap.ApplyUrp(g);
            uint m1 = c1.GetComponent<Renderer>().renderingLayerMask, m2 = c2.GetComponent<Renderer>().renderingLayerMask;
            Check("P5.applyUrp", m1 == 1u << 11 && m2 == 1u << 10, $"after ShaderMap.ApplyUrp: layer 11 → 0x{m1:X8}, layer 10 → 0x{m2:X8}");
        }
    }

    // ------------------------------------------------------------------ P6 lightmaps

    static Mesh LightmapQuad()
    {
        var m = Keep(new Mesh { name = "rp_lmquad" });
        m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
        var uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        m.uv = uv; m.uv2 = uv;
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
        return m;
    }

    static Texture2D Const(Color c, bool linear = true)
    {
        var t = Keep(new Texture2D(8, 8, TextureFormat.RGBAHalf, false, linear) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear });
        t.SetPixels(Enumerable.Repeat(c, 64).ToArray()); t.Apply(false);
        return t;
    }

    static void Lightmaps(UniversalRenderPipelineAsset prod)
    {
        SetPipeline(prod);
        DarkEnvironment(Color.black);
        var mesh = LightmapQuad();
        var std = Mat("HS2Next/Standard", m => { m.SetColor("_Color", Color.white); m.SetFloat("_Glossiness", 0); m.SetFloat("_Metallic", 0); });
        var lit = Mat("Universal Render Pipeline/Lit", m => { m.SetColor("_BaseColor", Color.white); m.SetFloat("_Smoothness", 0); m.SetFloat("_Metallic", 0); });
        var go = Keep(new GameObject("rp_lm"));
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        // the quad's normal is -Z, towards the camera at z = -2
        var cam = NewCamera(new Vector3(0, 0, -2), Vector3.zero, 20);
        var lmColor = new Color(0.6f, 0.3f, 0.15f, 1);
        const int W = 64, H = 64;
        Vector3 Center(Material m)
        {
            r.sharedMaterial = m;
            return Mean(Capture(cam, W, H), W, H, 0.4f, 0.6f, 0.4f, 0.6f);
        }
        // reference without a lightmap: black (no light, no ambient)
        r.lightmapIndex = -1;
        LightmapSettings.lightmaps = new LightmapData[0];
        var none = Center(std);
        foreach (var (mode, name, dirTex) in new (LightmapsMode, string, Texture2D)[] { (LightmapsMode.NonDirectional, "nonDirectional", null), (LightmapsMode.CombinedDirectional, "directional", Const(new Color(0.5f, 0.5f, 0.0f, 0.8f))) })
        {
            LightmapSettings.lightmapsMode = mode;
            LightmapSettings.lightmaps = new[] { new LightmapData { lightmapColor = Const(lmColor), lightmapDir = dirTex } };
            r.lightmapIndex = 0; r.lightmapScaleOffset = new Vector4(1, 1, 0, 0);
            bool lm = ShaderMap.IsLightmapped(r);
            var a = Center(std); var b = Center(lit);
            float dmax = Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y), Mathf.Abs(a.z - b.z));
            bool ratio = a.x > 0.02f && Mathf.Abs(a.y / a.x - lmColor.g / lmColor.r) < 0.02f && Mathf.Abs(a.z / a.x - lmColor.b / lmColor.r) < 0.02f;
            Check($"P6.{name}", lm && none.magnitude < 0.001f && ratio && dmax <= 0.002f + 0.01f * b.magnitude,
                $"lightmapped quad ({name}): HS2Next/Standard {V(a)}, URP/Lit {V(b)}, max |d| {dmax:F4}; colour ratios of the lightmap {ratio}; without lightmap {V(none)}; IsLightmapped {lm}");
        }
        // routing rule: a lightmapped renderer on a shader without a lightmap path goes to URP/Lit (own copy)
        var ms2 = Mat("HS2Next/AIT main standard2 studio");
        r.sharedMaterials = new[] { std, ms2 };
        int moved = ShaderMap.RouteLightmapped(r);
        var after = r.sharedMaterials;
        Check("P6.route", moved == 1 && after[0] == std && after[1] != ms2 && after[1].shader.name == "Universal Render Pipeline/Lit" && ms2.shader.name == "HS2Next/AIT main standard2 studio",
            $"RouteLightmapped: {moved} slot moved; slot 0 {after[0].shader.name} (kept), slot 1 {after[1].shader.name} (own copy: {after[1] != ms2}), the shared material stays {ms2.shader.name}");
        foreach (var mm in after) if (mm && mm != std) Keep(mm);
        r.lightmapIndex = -1;
        Check("P6.routeNone", ShaderMap.RouteLightmapped(r) == 0, "a renderer without a lightmap is not routed");
    }

    // ------------------------------------------------------------------ P7 reflection probes

    static Cubemap SolidCube(Color c) => FacesCube(Enumerable.Repeat(c, 6).ToArray());

    static Cubemap FacesCube(Color[] faces)
    {
        var cube = Keep(new Cubemap(16, TextureFormat.RGBAHalf, true));
        for (int f = 0; f < 6; f++) cube.SetPixels(Enumerable.Repeat(faces[f], 16 * 16).ToArray(), (CubemapFace)f);
        cube.Apply(true);
        return cube;
    }

    static ReflectionProbe Probe(Vector3 pos, Vector3 size, Cubemap cube, float blend, bool box)
    {
        var p = Keep(new GameObject("rp_probe")).AddComponent<ReflectionProbe>();
        p.transform.position = pos;
        p.mode = ReflectionProbeMode.Custom; p.customBakedTexture = cube;
        p.size = size; p.center = Vector3.zero; p.blendDistance = blend; p.boxProjection = box; p.importance = 1;
        return p;
    }

    static IEnumerator<object> Probes(UniversalRenderPipelineAsset prod, UniversalRenderPipelineAsset spike)
    {
        DarkEnvironment(Color.black);
        RenderSettings.reflectionIntensity = 1;
        var chrome = Mat("HS2Next/Standard", m => { m.SetColor("_Color", Color.white); m.SetFloat("_Glossiness", 1); m.SetFloat("_Metallic", 1); });
        var sphere = Prim(PrimitiveType.Sphere, Vector3.zero, 0.6f, chrome, 0);
        sphere.GetComponent<Renderer>().reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        var cam = NewCamera(new Vector3(0, 0, -3), Vector3.zero, 20);
        const int W = 64, H = 64;
        {
            // the chrome sphere against a green environment cube (no probes): checks the sphere and the cube maps
            RenderSettings.customReflectionTexture = SolidCube(new Color(0, 1, 0, 1));
            SetPipeline(prod);
            yield return null; yield return null;
            Info($"chrome sphere, green environment (no probes): {V(Mean(Capture(cam, W, H), W, H, 0.45f, 0.55f, 0.45f, 0.55f))} (production)");
            RenderSettings.customReflectionTexture = ShaderProbe.BlackCube;
        }
        var pa = Probe(new Vector3(-1, 0, 0), new Vector3(4, 4, 4), SolidCube(new Color(1, 0, 0, 1)), 2, false);
        var pb = Probe(new Vector3(1, 0, 0), new Vector3(4, 4, 4), SolidCube(new Color(0, 0, 1, 1)), 2, false);
        foreach (var (rp, label) in new[] { (prod, "production"), (spike, "spike") })
        {
            SetPipeline(rp);
            yield return null; yield return null;
            var c = Mean(Capture(cam, W, H), W, H, 0.45f, 0.55f, 0.45f, 0.55f);
            float mix = Mathf.Min(c.x, c.z) / Mathf.Max(1e-4f, Mathf.Max(c.x, c.z));
            string d = $"chrome sphere between a red and a blue probe: {V(c)}, min/max(red, blue) {mix:F3}";
            if (label == "production") Check("P7.blend", mix > 0.3f && c.x + c.z > 0.1f, d + " (production: blended)");
            else Info(d + " (Spike, no blending: one probe)");
        }
        Object.DestroyImmediate(pa.gameObject); Object.DestroyImmediate(pb.gameObject);

        // box projection: one probe with distinct faces, the sphere off the probe's centre
        sphere.transform.position = new Vector3(1.0f, 0.4f, 0);
        cam.transform.position = new Vector3(1.0f, 0.4f, -3); cam.transform.LookAt(sphere.transform.position);
        var faces = new[] { new Color(1, 0, 0, 1), new Color(0, 1, 0, 1), new Color(0, 0, 1, 1), new Color(1, 1, 0, 1), new Color(0, 1, 1, 1), new Color(1, 0, 1, 1) };
        var p = Probe(Vector3.zero, new Vector3(4, 4, 4), FacesCube(faces), 0.01f, false);
        foreach (var (rp, label) in new[] { (prod, "production"), (spike, "spike") })
        {
            SetPipeline(rp);
            p.boxProjection = false;
            yield return null; yield return null;
            var a = Capture(cam, W, H);
            p.boxProjection = true;
            yield return null; yield return null;
            var b = Capture(cam, W, H);
            double d = 0; int n = 0;
            for (int i = 0; i < a.Length; i++) { float s = Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b); if (a[i].maxColorComponent > 0 || b[i].maxColorComponent > 0) { d += s / 3; n++; } }
            d /= Math.Max(1, n);
            string txt = $"box projection on vs off: mean |d| {d:F4} over {n} px";
            if (label == "production") Check("P7.box", d > 0.01, txt + " (production: box projection applied)");
            else Info(txt + " (Spike: box projection off in the asset)");
        }
    }

    // Diagnostics: are Custom probes used only after editor updates? Logs the sphere's colour per update (then exits).
    public static void ProbeDiag()
    {
        ShaderUtil.allowAsyncCompilation = false;
        var prod = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AppPipeline.EditorAssetPath);
        SetPipeline(prod);
        DarkEnvironment(Color.black);
        RenderSettings.reflectionIntensity = 1;
        var chrome = Mat("HS2Next/Standard", m => { m.SetColor("_Color", Color.white); m.SetFloat("_Glossiness", 1); m.SetFloat("_Metallic", 1); });
        var sphere = Prim(PrimitiveType.Sphere, Vector3.zero, 0.6f, chrome, 0);
        var cam = NewCamera(new Vector3(0, 0, -3), Vector3.zero, 20);
        var pa = Probe(new Vector3(-1, 0, 0), new Vector3(4, 4, 4), SolidCube(new Color(1, 0, 0, 1)), 2, false);
        var pb = Probe(new Vector3(1, 0, 0), new Vector3(4, 4, 4), SolidCube(new Color(0, 0, 1, 1)), 2, false);
        int frame = 0;
        void Tick()
        {
            var c = Mean(Capture(cam, 64, 64), 64, 64, 0.45f, 0.55f, 0.45f, 0.55f);
            Debug.Log($"PROBEDIAG update {frame}: {V(c)}");
            if (++frame < 6) return;
            EditorApplication.update -= Tick;
            Cleanup();
            EditorApplication.Exit(0);
        }
        Debug.Log($"PROBEDIAG sync: {V(Mean(Capture(cam, 64, 64), 64, 64, 0.45f, 0.55f, 0.45f, 0.55f))}");
        EditorApplication.update += Tick;
    }

    // ------------------------------------------------------------------ P3 / P4 character

    static void Character(UniversalRenderPipelineAsset prod, UniversalRenderPipelineAsset spike)
    {
        string card = Arg("-card", null) ?? TestTargets.Load().VisualCards.FirstOrDefault();
        if (card == null || !File.Exists(card)) { Check("P3", false, "no card " + card); return; }
        var log = new StringBuilder();
        var gd = CardResolveTest.BuildDb(log);
        GameObject chara = null;
        bool wideBefore = MosaicWide.Enabled;
        try
        {
            chara = new CharaAssembler(gd).BuildFromDocuments(card);
            Keep(chara);
            ShaderMap.ApplyUrp(chara);
            var layers = chara.GetComponentsInChildren<Renderer>(true).GroupBy(r => r.gameObject.layer).OrderBy(g => g.Key).Select(g => $"{g.Key}: {g.Count()}");
            Info($"{Path.GetFileName(card)}: renderers by layer {string.Join(", ", layers)}");
            var b = new Bounds(chara.transform.position, Vector3.zero);
            foreach (var r in chara.GetComponentsInChildren<Renderer>()) if (r.enabled && r.gameObject.activeInHierarchy) b.Encapsulate(r.bounds);
            var target = b.center;
            const int W = 300, H = 600;
            float dist = b.size.y / (2 * Mathf.Tan(12 * Mathf.Deg2Rad)) * 1.08f;
            var cam = NewCamera(target + Vector3.forward * dist, target, 24);

            // P3: main light only, no shadows
            DarkEnvironment(new Color(0.42f, 0.42f, 0.45f));
            var key = NewLight(LightType.Directional, Vector3.zero, new Vector3(30, 200, 0), Color.white);
            WideMosaicEffect(prod, cam, W, H);
            MosaicWide.Enabled = false;   // restored in finally
            SetPipeline(spike); var s0 = Capture(cam, W, H);
            SetPipeline(prod); var p0 = Capture(cam, W, H);
            double maxd = 0, sum = 0; int n = 0;
            for (int i = 0; i < s0.Length; i++)
            {
                if (s0[i].maxColorComponent <= 0 && p0[i].maxColorComponent <= 0) continue;
                float d = Mathf.Max(Mathf.Abs(s0[i].r - p0[i].r), Mathf.Abs(s0[i].g - p0[i].g), Mathf.Abs(s0[i].b - p0[i].b));
                maxd = Math.Max(maxd, d); sum += d; n++;
            }
            Check("P3.exact", n > 10000 && maxd <= 1e-4, $"main light only: production vs Spike over {n} px: max |d| {maxd:F6}, mean {sum / Math.Max(1, n):F7} (linear)");
            Object.DestroyImmediate(key.gameObject);

            // P4: 12 point lights around the character
            DarkEnvironment(Color.black);
            var lights = new List<Light>();
            for (int i = 0; i < 12; i++)
            {
                float a = (-75 + i * 150f / 11) * Mathf.Deg2Rad;   // a fan on the camera's side (the character faces +Z)
                var pos = target + new Vector3(Mathf.Sin(a) * 0.9f, (i % 3 - 1) * 0.45f, Mathf.Cos(a) * 0.9f);
                var l = NewLight(LightType.Point, pos, Vector3.zero, Color.HSVToRGB(i / 12f, 0.5f, 1f), -1, 2.5f);
                lights.Add(l);
            }
            // test copies with a 64-bit HDR colour buffer (R11G11B10 rounds every image by up to ~3 %)
            var prod64 = Keep(Object.Instantiate(prod)); prod64.hdrColorBufferPrecision = HDRColorBufferPrecision._64Bits;
            var spike8 = Keep(Object.Instantiate(spike)); spike8.hdrColorBufferPrecision = HDRColorBufferPrecision._64Bits; spike8.maxAdditionalLightsCount = 8;
            {
                for (int i = 0; i < lights.Count; i++) lights[i].enabled = i % 3 != 2;   // 8 lights
                SetPipeline(spike8); var f8 = Capture(cam, W, H);
                SetPipeline(prod64); var p8 = Capture(cam, W, H);
                double md = 0, tot = 0, sumd = 0;
                for (int i = 0; i < f8.Length; i++)
                {
                    float d = Mathf.Max(Mathf.Abs(f8[i].r - p8[i].r), Mathf.Abs(f8[i].g - p8[i].g), Mathf.Abs(f8[i].b - p8[i].b));
                    md = Math.Max(md, d); sumd += d; tot += f8[i].maxColorComponent;
                }
                Check("P4.sameAsForward", tot > 1 && md <= 1e-3, $"8 point lights: Forward+ (production, 64-bit HDR) vs Forward with a per-object limit of 8 (Spike, 64-bit HDR): max |d| {md:F6}, sum |d| / sum {sumd / Math.Max(1e-6, tot):F7}");
            }
            foreach (var (rp, label) in new[] { (prod64, "production"), (spike, "spike") })
            {
                SetPipeline(rp);
                foreach (var l in lights) l.enabled = false;
                var none = Capture(cam, W, H);   // light-independent terms (emission, rims) count once
                foreach (var l in lights) l.enabled = true;
                var all = Capture(cam, W, H);
                var sumImg = new Vector3[all.Length];
                for (int i = 0; i < none.Length; i++) sumImg[i] = new Vector3(none[i].r, none[i].g, none[i].b);
                int reaching = 0;
                foreach (var l in lights)
                {
                    foreach (var o in lights) o.enabled = o == l;
                    var one = Capture(cam, W, H);
                    double e = 0;
                    for (int i = 0; i < one.Length; i++)
                    {
                        var add = new Vector3(one[i].r - none[i].r, one[i].g - none[i].g, one[i].b - none[i].b);
                        sumImg[i] += add; e += add.x + add.y + add.z;
                    }
                    if (e > 1) reaching++;
                }
                double diff = 0, tot = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    var a = new Vector3(all[i].r, all[i].g, all[i].b);
                    diff += (a - sumImg[i]).magnitude; tot += sumImg[i].magnitude;
                }
                double rel = diff / Math.Max(1e-6, tot);
                string d = $"12 point lights: |all - (none + sum of (single - none))| / |that| = {rel:F5}; lights reaching the character alone {reaching}/12";
                if (label == "production") Check("P4.forwardPlus", reaching >= 12 && rel < 0.002, d + " (production, Forward+, 64-bit HDR)");
                else Info(d + " (Spike as is: Forward, per-object limit " + spike.maxAdditionalLightsCount + ", R11G11B10)");
            }
        }
        finally
        {
            MosaicWide.Enabled = wideBefore;
            if (chara) Object.DestroyImmediate(chara);
            gd.UnloadAll();
        }
    }

    // The wide mosaic's effect on this view under the production asset (info): the render with the option on against the
    // render with it off; a changed pixel is inside a group's region when it lies within the group's projected diameter / 2
    // plus two blocks of its anchor (the mask is dilated by 1.5 blocks, the grid quantizes by one).
    static void WideMosaicEffect(UniversalRenderPipelineAsset prod, Camera cam, int w, int h)
    {
        bool before = MosaicWide.Enabled;
        try
        {
            SetPipeline(prod);
            MosaicWide.Enabled = true;
            int e0 = MosaicWideFeature.EnqueuedCount;
            var on = Capture(cam, w, h);
            int enq = MosaicWideFeature.EnqueuedCount - e0;
            var groups = MosaicWideFeature.LastGroups.ToList();
            MosaicWide.Enabled = false;
            var off = Capture(cam, w, h);
            int changed = 0, outside = 0; double maxIn = 0, maxOut = 0;
            for (int i = 0; i < on.Length; i++)
            {
                float d = Mathf.Max(Mathf.Abs(on[i].r - off[i].r), Mathf.Abs(on[i].g - off[i].g), Mathf.Abs(on[i].b - off[i].b));
                if (d <= 0) continue;
                changed++;
                var px = new Vector2(i % w + 0.5f, i / w + 0.5f);
                bool inside = enq > 0 && groups.Any(g => (px - g.Anchor).magnitude <= g.Diameter * 0.5f + 2 * g.Block);
                if (inside) maxIn = Math.Max(maxIn, d); else { outside++; maxOut = Math.Max(maxOut, d); }
            }
            string gs = string.Join(", ", groups.Select(g => "block " + g.Block.ToString("F1") + " px, diameter " + g.Diameter.ToString("F1") + " px"));
            Info($"wide mosaic on this view (production, before P3 / P4 switch it off): enqueued in {enq} renders, {groups.Count} groups " +
                 $"({gs}); {changed} of {on.Length} px changed, " +
                 $"max |d| inside the groups' region {maxIn:F6}, {outside} px outside (max |d| {maxOut:F6})");
        }
        finally { MosaicWide.Enabled = before; }
    }
}
