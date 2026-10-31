// SkyAmbientTest (Phase 3.3 package S): sky, ambient, quality and light falloff.
//   & Tools\unity_batch.ps1 -Method SkyAmbientTest.Run [-MethodArgs @('-only','SA1+SA2')]  -> Spike/out/effects/sky_ambient_report.txt
//   & Tools\unity_batch.ps1 -Method SkyAmbientTest.RunRender -NoQuit                      -> Spike/out/effects/sky_ambient_render_report.txt
//   & Tools\unity_batch.ps1 -Method SkyAmbientTest.RunPlay -NoQuit                        -> Spike/out/effects/sky_ambient_play_report.txt
// The measurements (U1 / U2 / U10 / U15) are in SkyAmbientTest_Measure.cs.
// Run (edit mode; SA0 / SA4 need a graphics device):
//   SA0 the MotionVectors and DepthNormals passes of the 44 fitted opaque / cutout shaders compile (vertex and fragment, D3D,
//       no keywords); every fitted shader's forward pass compiles with _ADDITIONAL_LIGHTS + _CLUSTER_LIGHT_LOOP (the falloff
//       code of HS2NextLights.hlsl); the transparent fitted shaders have no MotionVectors pass
//   SA1 ambient SH: AmbientSh.Trilight without quantization = Unity 6's own Trilight probe (1e-6, six colour sets incl.
//       the basis colours); with HS2's 8-bit linear colours = HS2 Studio's interpolated probe of H1 for the default colours
//       and pure red / green / blue (27 coefficients, 1e-6); Flat = the sky colour
//   SA2 MapSavedProbe: the Bakery mod bushitsu's saved probe = HS2's RenderSettings.ambientProbe of H1 (1e-5), mode Skybox,
//       intensity 0.46; vanilla map 10 Trilight with a zero probe; an LZMA bundle (hooh school) and a truncated / non-bundle
//       input fail closed with a reason; LZ4 decoding of every block of map 10's bundle
//   SA3 the ambient rule: Trilight / Flat / Skybox with and without a saved probe, rule environment; AmbientMode.Custom with
//       the intended probe; the guard re-asserts after an outside write (count)
//   SA4 sky blocks in a runtime: pattern → owned skybox copy of the StudioSky row's material and SkySun = the row's Euler
//       (also while the sky is off, Q-e); clear flags / background through ViewSetup; an unknown row keeps the pattern
//       (effects.skyMissing); ambient colours written; routing (effects.sky / effects.ambient mark only S's blocks)
//   SA5 falloff: LightFalloff.BuiltinDistance / BuiltinCookie vs the U10 tables; the global follows render.lightFalloff
//   SA6 anti-aliasing: render.aa → ViewSetup / camera data, TAA quality, the dither frame per camera
//   SA7 the no-map environment (U15): ambient intensity 1.5, fog mode Exp², default reflection Custom sky_BigSight_Env
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static partial class SkyAmbientTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "effects");
    static StringBuilder rep;
    static int pass, fail;
    static string[] only;

    static void Begin() { rep = new StringBuilder(); pass = fail = 0; }
    static void Section(string s) => rep.AppendLine("\n== " + s);
    static void Info(string s) => rep.AppendLine("   " + s);
    static void Check(string what, bool ok, string detail = "")
    {
        if (ok) pass++; else fail++;
        rep.AppendLine($"   {(ok ? "PASS" : "FAIL")} {what}{(string.IsNullOrEmpty(detail) ? "" : " - " + detail)}");
    }
    static void End(string file, string title, Stopwatch sw)
    {
        Directory.CreateDirectory(OutDir);
        rep.Insert(0, $"SUMMARY: {pass} PASS, {fail} FAIL\n{title} ({sw.Elapsed.TotalSeconds:F0} s)\n");
        File.WriteAllText(Path.Combine(OutDir, file), rep.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
        Debug.Log($"{title}: {pass} PASS, {fail} FAIL -> {Path.Combine(OutDir, file)}");
    }
    static bool Want(string id) => only == null || only.Contains(id);
    static string F(double v) => v.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);

    static string[] ArgList(string name)
    {
        var a = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1].Split(',', '+', ';').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray() : null;
    }

    static RuntimeServices services;
    static RuntimeServices Services()
    {
        if (services != null) return services;
        services = SceneRuntimeTest.NewServices(new StringBuilder());
        return services;
    }

    /// <summary>A session and a runtime with the standard parts (no drivers).</summary>
    sealed class Rig : IDisposable
    {
        public SceneSession Session;
        public SceneRuntime Runtime;
        public SceneSky Sky => Runtime.GetPart<SceneSky>();
        public Rig(RuntimeServices s)
        {
            Session = SceneRuntimeTest.NewSession(s);
            Runtime = new SceneRuntime(Session, s, null, false).AddStandardParts();
            Runtime.Apply();
            Runtime.LateApply();
        }
        public void Set(string path, object v)
        {
            var r = Session.Set(null, path, v);
            if (r == null || r.Status == CommandStatus.Error) Check("set " + path, false, r?.ToString() ?? "null");
        }
        public void Step() { Runtime.Apply(); Runtime.LateApply(); }
        public void Dispose() { Runtime?.Dispose(); Runtime = null; }
    }

    // H1 (HS2 Studio, LightProbes.GetInterpolatedProbe at the origin; Cache/compare/effects/ambient_nomap_*.json): the SH
    // HS2 renders for Studio's default ambient colours (c × 9 + k)
    static readonly float[] Hs2DefaultSh =
    {
        0.4921569f, -0.05443312f, 0f, 0f, 0f, 0f, -0.0009191372f, 0f, -0.002757356f,
        0.472549f, 0.0949911f, 0f, 0f, 0f, 0f, 0.01133578f, 0f, 0.03400734f,
        0.5614379f, 0.2380114f, 0f, 0f, 0f, 0f, 0.00786354f, 0f, 0.02359069f,
    };
    // H1: bushitsu's saved probe as HS2 reports it (RenderSettings.ambientProbe, Skybox mode)
    static readonly float[] Hs2BushitsuSh =
    {
        1.07426f, -0.567535f, 0.241207f, 0.047265f, 0.055415f, 0.246339f, 0.018685f, 0.007407f, -0.101126f,
        0.95638f, -0.294258f, 0.265591f, 0.039824f, 0.046551f, 0.257392f, 0.029531f, 0.005617f, -0.07868f,
        0.803784f, 0.058259f, 0.299942f, 0.036933f, 0.041275f, 0.278168f, 0.037356f, 0.004197f, -0.064386f,
    };

    static float MaxDiff(SphericalHarmonicsL2 a, IReadOnlyList<float> b)
    {
        float m = 0f;
        for (int c = 0; c < 3; c++) for (int k = 0; k < 9; k++) m = Mathf.Max(m, Mathf.Abs(a[c, k] - b[c * 9 + k]));
        return m;
    }

    /// <summary>H1's recorded interpolated probe of one ambient case (null when the recording is absent).</summary>
    static float[] RecordedSh(string ambientCase)
    {
        string f = Path.Combine(ContentSettings.ProjectRoot, "Cache", "compare", "effects", ambientCase + ".json");
        if (!File.Exists(f)) return null;
        var j = JObject.Parse(File.ReadAllText(f));
        var a = j.SelectToken("effects.realized.renderSettings.interpolatedProbeAtOrigin") as JArray;
        return a?.Select(x => (float)x).ToArray();
    }

    public static void Run()
    {
        only = ArgList("-only");
        Begin();
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"SkyAmbientTest (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
        int code = 0;
        var mode0 = RenderSettings.ambientMode;
        var probe0 = RenderSettings.ambientProbe;
        try
        {
            if (Want("SA0")) TestPasses();
            if (Want("SA1")) TestSh();
            if (Want("SA2")) TestSavedProbe();
            if (Want("SA3")) TestRule();
            if (Want("SA4")) TestSky();
            if (Want("SA5")) TestFalloff();
            if (Want("SA6")) TestAa();
            if (Want("SA7")) TestNoMap();
        }
        catch (Exception e) { Check("ran without an exception", false, e.ToString()); }
        finally
        {
            RenderSettings.ambientMode = mode0;
            RenderSettings.ambientProbe = probe0;
            RenderOptions.ResetToDefaults();
            LightFalloff.Apply();
        }
        End("sky_ambient_report.txt", "SKY AMBIENT TEST", sw);
        if (fail > 0) code = 1;
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    // ------------------------------------------------------------------ SA0 passes

    static readonly string[] PassFamilies = { "MotionVectors", "DepthNormals" };

    static IEnumerable<Shader> FittedShaders()
    {
        foreach (var g in AssetDatabase.FindAssets("t:Shader", new[] { "Assets/HS2Next/Shaders" }))
        {
            var s = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(g));
            if (s && s.name.StartsWith("HS2Next/", StringComparison.Ordinal)) yield return s;
        }
    }

    static string CompilePass(ShaderData.Pass p, string[] kw)
    {
        foreach (var st in new[] { ShaderType.Vertex, ShaderType.Fragment })
        {
            var r = p.CompileVariant(st, kw, ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64);
            if (!r.Success)
                return st + ": " + string.Join(" | ", r.Messages.Select(m => m.message + " (" + m.file + ":" + m.line + ")").Take(3));
        }
        return null;
    }

    static void TestPasses()
    {
        Section("SA0 MotionVectors / DepthNormals passes and the falloff code compile");
        int withPasses = 0, forward = 0, mvTransparent = 0;
        var errors = new List<string>();
        foreach (var s in FittedShaders())
        {
            var data = ShaderUtil.GetShaderData(s);
            if (data == null || data.SubshaderCount == 0) continue;
            var sub = data.GetSubshader(0);
            bool hasMv = false, transparent = s.renderQueue >= 2501 && s.renderQueue < 5000 && s.renderQueue != 2000;
            for (int i = 0; i < sub.PassCount; i++)
            {
                var p = sub.GetPass(i);
                string lm = p.FindTagValue(new ShaderTagId("LightMode")).name;
                if (PassFamilies.Contains(lm, StringComparer.OrdinalIgnoreCase))   // Unity reports MOTIONVECTORS (a built-in pass type)
                {
                    hasMv |= string.Equals(lm, "MotionVectors", StringComparison.OrdinalIgnoreCase);
                    string e = CompilePass(p, new string[0]);
                    if (e != null) errors.Add(s.name + " " + lm + ": " + e);
                }
                else if (lm == "UniversalForward")
                {
                    forward++;
                    string e = CompilePass(p, new[] { "_ADDITIONAL_LIGHTS", "_CLUSTER_LIGHT_LOOP" });
                    if (e != null && !e.Contains("undeclared")) errors.Add(s.name + " forward: " + e);
                }
            }
            if (hasMv) withPasses++;
            if (hasMv && transparent) mvTransparent++;
        }
        Check($"the 44 fitted opaque / cutout shaders have the MotionVectors + DepthNormals passes ({withPasses} shader objects)", withPasses >= 44, withPasses.ToString());
        Check("no transparent-queue fitted shader has a MotionVectors pass", mvTransparent == 0, mvTransparent.ToString());
        Check($"every new pass and every forward pass ({forward}) compiles", errors.Count == 0, string.Join("\n      ", errors.Take(12)));
    }

    // ------------------------------------------------------------------ SA1 SH

    static void TestSh()
    {
        Section("SA1 the ambient SH (H1 / U1)");
        var sets = new (string, Color, Color, Color)[]
        {
            ("basis", Color.red, Color.green, Color.blue),
            ("HS2 defaults", new Color32(170, 188, 243, 255), new Color32(185, 195, 205, 255), new Color32(204, 109, 41, 255)),
            ("greys", new Color(0.9f, 0.9f, 0.9f), new Color(0.2f, 0.2f, 0.2f), new Color(0.5f, 0.5f, 0.5f)),
            ("random 1", new Color(0.13f, 0.77f, 0.41f), new Color(0.62f, 0.05f, 0.93f), new Color(0.31f, 0.48f, 0.02f)),
            ("random 2", new Color(1f, 0.5f, 0f), new Color(0f, 0.25f, 0.75f), new Color(0.66f, 0.66f, 0.1f)),
            ("map 10 own", new Color(0.231373f, 0.596909f, 0.772549f), new Color(0.646479f, 0.655244f, 0.659f), new Color(0.584314f, 0.477632f, 0.443137f)),
        };
        var m0 = RenderSettings.ambientMode;
        float worst = 0f;
        bool computed = true;
        foreach (var (name, s, e, g) in sets)
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = s;
            RenderSettings.ambientEquatorColor = e;
            RenderSettings.ambientGroundColor = g;
            RenderSettings.ambientIntensity = 1f;
            var unity = RenderSettings.ambientProbe;
            var mine = AmbientSh.Trilight(s, e, g, quantize: false);
            float d = AmbientSh.MaxDiff(unity, mine);
            if (unity[0, 0] == 0f && unity[1, 0] == 0f) computed = false;
            worst = Mathf.Max(worst, d);
            Info($"{name}: max |Unity - AmbientSh| {F(d)} (sh0 {F(unity[0, 0])} / {F(mine[0, 0])})");
        }
        RenderSettings.ambientMode = m0;
        if (computed) Check("AmbientSh.Trilight (unquantized) = Unity 6's Trilight probe within 1e-6", worst <= 1e-6f, F(worst));
        else Info("Unity did not compute a Trilight probe in edit mode (the comparison needs play mode; U1's play-mode numbers agree)");
        var hs2 = AmbientSh.Trilight(new Color32(170, 188, 243, 255), new Color32(185, 195, 205, 255), new Color32(204, 109, 41, 255));
        float dh = MaxDiff(hs2, Hs2DefaultSh);
        Check("HS2's SH for Studio's default colours (8-bit linear colours) = H1 within 1e-6", dh <= 1.5e-6f, F(dh));
        var rec = RecordedSh("ambient_nomap_default");
        if (rec != null) Check("… and = the recorded 27 coefficients of ambient_nomap_default within 1e-6", MaxDiff(hs2, rec) <= 1.5e-6f, F(MaxDiff(hs2, rec)));
        foreach (var (cn, c) in new[] { ("red", Color.red), ("green", Color.green), ("blue", Color.blue) })
        {
            var r2 = RecordedSh("ambient_nomap_" + cn);
            if (r2 == null) continue;
            var sh = AmbientSh.Trilight(c, c, c);
            Check($"pure {cn} colours = H1's recording within 1e-6", MaxDiff(sh, r2) <= 1.5e-6f, F(MaxDiff(sh, r2)));
        }
        var unq = AmbientSh.Trilight(new Color32(170, 188, 243, 255), new Color32(185, 195, 205, 255), new Color32(204, 109, 41, 255), false);
        Info($"Unity's unquantized SH differs from HS2's by {F(AmbientSh.MaxDiff(unq, hs2))} (the reason for the 8-bit step)");
        var flat = AmbientSh.Flat(new Color32(170, 188, 243, 255), false);
        Check("Flat = the linear sky colour in c0 (U1: 0.401978 / 0.502887 / 0.896269)",
            Mathf.Abs(flat[0, 0] - 0.401978f) < 1e-5f && Mathf.Abs(flat[1, 0] - 0.502887f) < 1e-5f && Mathf.Abs(flat[2, 0] - 0.896269f) < 1e-5f && flat[0, 1] == 0f);
    }

    // ------------------------------------------------------------------ SA2 saved probe

    static Hs2BundleFile OpenZipBundle(string zipRel, string entry)
    {
        string zip = Path.Combine(CardResolveTest.GameRoot, "mods", zipRel);
        if (!File.Exists(zip)) return null;
        var z = new HS2Next.IO.ZipIndex(zip);
        if (!z.Entries.TryGetValue("abdata/" + entry, out var e)) return null;
        return e.Method == 0 ? Hs2BundleFile.OpenFile(zip, e.DataOffset, e.Size, zip + "!" + entry) : Hs2BundleFile.FromBytes(z.ReadAll(e), zip + "!" + entry);
    }

    static void TestSavedProbe()
    {
        Section("SA2 a map's saved RenderSettings from its bundle");
        var b = OpenZipBundle("Sideloader Modpack - Maps/Hooh/[hooh] minimap_bushitsu.zipmod", "hooh_minimap_bushitsu_bundles/hooh_minimap_bushitsu/data_scene_000.unity3d");
        if (b == null) { Info("bushitsu not installed: skipped"); }
        else
        {
            var sw = Stopwatch.StartNew();
            var r = MapSavedProbe.ReadBundle(b, "map01");
            Info($"bushitsu: {r.Node} ({r.UnityVersion}), {b.BlocksRead} blocks decompressed of {b.Nodes.Count} nodes, {sw.Elapsed.TotalMilliseconds:F1} ms");
            Check("bushitsu: read, mode Skybox, intensity 0.46", r.Ok && r.Mode == AmbientMode.Skybox && Mathf.Abs(r.Intensity - 0.46f) < 1e-6f, r.Error);
            if (r.Ok) Check("bushitsu's saved probe = HS2's RenderSettings.ambientProbe (H1) within 1e-5", MaxDiff(r.Probe, Hs2BushitsuSh) <= 1e-5f, F(MaxDiff(r.Probe, Hs2BushitsuSh)));
        }
        string m10 = Path.Combine(CardResolveTest.GameRoot, "abdata", "studio", "map", "studio_map10.unity3d");
        if (File.Exists(m10))
        {
            var b10 = Hs2BundleFile.OpenFile(m10, 0, new FileInfo(m10).Length);
            var r = MapSavedProbe.ReadBundle(b10, b10.Nodes.First(n => !n.Path.EndsWith(".sharedAssets", StringComparison.Ordinal)).Path.Substring("BuildPlayer-".Length));
            bool zero = r.Ok && AmbientSh.MaxDiff(r.Probe, new SphericalHarmonicsL2()) == 0f;
            Check("a vanilla map bundle: Trilight with a zero saved probe (the census)", r.Ok && r.Mode == AmbientMode.Trilight && zero, r.Error ?? r.Mode.ToString());
            // LZ4: every block of the bundle decodes to its size
            int nodes = 0;
            foreach (var n in b10.Nodes) { b10.ReadRange(n, 0, (int)Math.Min(n.Size, int.MaxValue)); nodes++; }
            Check($"LZ4 / LZ4HC: every block of map 10's bundle decodes ({b10.BlocksRead} blocks, {nodes} nodes)", b10.BlocksRead > 0);
            var raw = File.ReadAllBytes(m10);
            try { Hs2BundleFile.FromBytes(raw.Take(raw.Length / 2).ToArray(), "half"); Check("a truncated bundle fails closed", false, "no exception"); }
            catch (Hs2SerializedFileException e) { Check("a truncated bundle fails closed", true, e.Message); }
        }
        var sch = OpenZipBundle("Sideloader Modpack - Maps/Hooh/[Hooh] school.zipmod", "hooh/studiomap_hooh_school.unity3d");
        if (sch != null)
        {
            SavedRenderSettings r;
            try { r = MapSavedProbe.ReadBundle(sch, "school"); }
            catch (Hs2SerializedFileException e) { r = new SavedRenderSettings { Error = e.Message }; }
            Check("an LZMA bundle (hooh school) fails closed with the reason", !r.Ok && r.Error != null && r.Error.Contains("LZMA"), r.Error);
        }
        try { Hs2BundleFile.FromBytes(Encoding.ASCII.GetBytes("not a bundle\0\0\0\0\0\0\0\0\0\0\0\0"), "text"); Check("a non-bundle fails closed", false); }
        catch (Hs2SerializedFileException e) { Check("a non-bundle fails closed", true, e.Message); }
        // the LZ4 decoder on a hand-made block: literals + an overlapping match
        var block = new byte[] { 0x3F, (byte)'a', (byte)'b', (byte)'c', 3, 0, 0, 0x10, (byte)'z' };   // "abc", 19 bytes from 3 back, "z"
        var dec = Hs2Lz4.Decode(block, 3 + 19 + 1);
        Check("LZ4 overlapping match (literals, extended match length, final literals)", Encoding.ASCII.GetString(dec) == "abcabcabcabcabcabcabcaz", Encoding.ASCII.GetString(dec));
    }

    // ------------------------------------------------------------------ SA3 rule

    static void TestRule()
    {
        Section("SA3 the ambient rule (AmbientProbe)");
        var sky = (Color)new Color32(170, 188, 243, 255); var eq = (Color)new Color32(185, 195, 205, 255); var gr = (Color)new Color32(204, 109, 41, 255);
        var a = new AmbientProbe();
        a.SetScene(null, null);
        var f = a.Apply("hs2", sky, eq, gr);
        Check("no map: Trilight → colours, AmbientMode.Custom, the probe = HS2's SH", f == null && a.Source == "colours" && RenderSettings.ambientMode == AmbientMode.Custom && MaxDiff(RenderSettings.ambientProbe, Hs2DefaultSh) <= 1.5e-6f,
            a.Source + " " + RenderSettings.ambientMode + " " + F(MaxDiff(RenderSettings.ambientProbe, Hs2DefaultSh)));
        Check("the probe holds", a.Holds());
        var other = new SphericalHarmonicsL2(); other.AddAmbientLight(Color.gray);
        RenderSettings.ambientProbe = other;
        bool re = a.Guard();
        Check("the guard re-asserts an outside write (count 1)", re && a.Reasserted == 1 && a.Holds(), a.Reasserted.ToString());
        Check("the guard leaves a holding probe alone", !a.Guard() && a.Reasserted == 1);
        a.SetScene(AmbientMode.Flat, new SavedRenderSettings { Ok = true, Mode = AmbientMode.Flat });
        a.Apply("hs2", sky, eq, gr);
        Check("Flat scene → the sky colour's SH", a.Source == "flat" && AmbientSh.MaxDiff(RenderSettings.ambientProbe, AmbientSh.Flat(sky)) == 0f);
        var saved = new SavedRenderSettings { Ok = true, Mode = AmbientMode.Skybox, Probe = AmbientSh.FromArray(Hs2BushitsuSh) };
        a.SetScene(AmbientMode.Skybox, saved);
        a.Apply("hs2", sky, eq, gr);
        Check("Skybox scene with a saved probe → the saved probe (colours ignored)", a.Source == "saved" && MaxDiff(RenderSettings.ambientProbe, Hs2BushitsuSh) <= 1e-6f && RenderSettings.ambientMode == AmbientMode.Custom);
        a.SetScene(AmbientMode.Skybox, new SavedRenderSettings { Error = "test" });
        f = a.Apply("hs2", sky, eq, gr);
        Check("Skybox scene without a saved probe → Unity's value, the scene's mode, a finding", a.Source == "unity" && f != null && RenderSettings.ambientMode == AmbientMode.Skybox && !a.HasIntended, f);
        a.SetScene(null, null);
        a.Apply("environment", sky, eq, gr);
        Check("rule environment: the scene's own mode (Trilight), no intended probe", a.Rule == "environment" && a.Source == "unity" && RenderSettings.ambientMode == AmbientMode.Trilight && !a.HasIntended && !a.Guard());
    }

    // ------------------------------------------------------------------ SA4 sky in a runtime

    static void TestSky()
    {
        Section("SA4 sky and environment blocks in a runtime");
        var s = Services();
        using (var rig = new Rig(s))
        {
            var sky = rig.Sky;
            Check("the runtime has SceneSky (after SceneEffects, before SceneSound)", sky != null);
            if (sky == null) return;
            var parts = rig.Runtime.Parts.Select(p => p.GetType().Name).ToList();
            Check("part order StudioLights, SceneEffects, SceneSky, SceneSound", parts.IndexOf("SceneEffects") < parts.IndexOf("SceneSky") && parts.IndexOf("SceneSky") < parts.IndexOf("SceneSound"), string.Join(",", parts));
            var view = rig.Runtime.GetPart<SceneViewCamera>();
            // defaults: sky off, pattern 0 (applied while off)
            Check("default: pattern 0 applied while the sky is off (Q-e)", RenderSettings.skybox == sky.SkyMaterial && sky.SkyMaterial && sky.SkyMaterial.name == "m_h2_SkySuny00_00",
                RenderSettings.skybox ? RenderSettings.skybox.name : "none");
            Check("default: clear flags SolidColor, background black", view.Setup.ClearFlags == CameraClearFlags.SolidColor && view.Setup.Background == Color.black);
            Check("SkySun Euler (40, 0, 0)", Vector3.Distance(sky.SkySun.eulerAngles, new Vector3(40, 0, 0)) < 1e-3f, sky.SkySun.eulerAngles.ToString("F3"));
            var first = sky.SkyMaterial;
            rig.Set("effects.sky.pattern", ContentId.Vanilla(3).ToString());
            rig.Set("effects.sky.enabled", true);
            rig.Step();
            Check("pattern 3: m_h2_SkyEvrning00_00, the previous copy destroyed", sky.SkyMaterial && sky.SkyMaterial.name == "m_h2_SkyEvrning00_00" && !first && RenderSettings.skybox == sky.SkyMaterial);
            Check("pattern 3: SkySun eulerAngles = HS2's (6, 180, 180) for x 174 (H4)", Vector3.Distance(sky.SkySun.eulerAngles, new Vector3(6, 180, 180)) < 1e-2f, sky.SkySun.eulerAngles.ToString("F3"));
            Check("sky on: clear flags Skybox on the view setup and the camera", view.Setup.ClearFlags == CameraClearFlags.Skybox && view.Camera && view.Camera.clearFlags == CameraClearFlags.Skybox);
            Check("the copy keeps the bundle shader AIOsky/V2", sky.SkyMaterial.shader && sky.SkyMaterial.shader.name == "AIOsky/V2", sky.SkyMaterial.shader ? sky.SkyMaterial.shader.name : "none");
            var keep = sky.SkyMaterial;
            int warn0 = rig.Session.Findings.Count(f => f.Code == "effects.skyMissing");
            rig.Set("effects.sky.pattern", ContentId.Vanilla(99).ToString());
            rig.Step();
            Check("an unknown row keeps the pattern and reports effects.skyMissing (warning)", RenderSettings.skybox == keep && sky.SkyMaterial == keep &&
                rig.Session.Findings.Count(f => f.Code == "effects.skyMissing" && f.Severity == FindingSeverity.Warning) == warn0 + 1);
            RenderOptions.Apply(JObject.Parse("{\"backgroundColor\":[0.2,0.3,0.4,1]}"));
            rig.Set("effects.sky.enabled", false);
            rig.Step();
            Check("sky off: SolidColor with render.backgroundColor", view.Setup.ClearFlags == CameraClearFlags.SolidColor && view.Setup.Background == new Color(0.2f, 0.3f, 0.4f, 1f) &&
                view.Camera.backgroundColor == new Color(0.2f, 0.3f, 0.4f, 1f));
            RenderOptions.ResetToDefaults();
            rig.Set("effects.ambient.sky", new Color4(1f, 0f, 0f, 1f));
            rig.Step();
            Check("block 11: the active scene's ambient sky colour = the state's", RenderSettings.ambientSkyColor == new Color(1f, 0f, 0f, 1f));
            Check("… and the probe follows (rule hs2: colours)", sky.Ambient.Source == "colours" && sky.Ambient.Holds() &&
                AmbientSh.MaxDiff(RenderSettings.ambientProbe, AmbientSh.Trilight(Color.red, RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor)) <= 1e-6f);
            // routing: an effects.bloom change marks nothing of S
            rig.Step();
            rig.Set("effects.bloom.intensity", 3f);
            Check("effects.bloom marks no S block", !sky.DirtyBlocks().Any(), string.Join(",", sky.DirtyBlocks()));
            rig.Set("effects.sky.enabled", true);
            Check("effects.sky marks only sky", sky.DirtyBlocks().SequenceEqual(new[] { "sky" }), string.Join(",", sky.DirtyBlocks()));
            rig.Step();
            RenderOptions.Apply(JObject.Parse("{\"ambient\":\"environment\"}"));
            rig.Step();
            Check("render.ambient = environment: Unity's own (Trilight mode, source unity)", sky.Ambient.Rule == "environment" && sky.Ambient.Source == "unity" && RenderSettings.ambientMode == AmbientMode.Trilight);
            RenderOptions.ResetToDefaults();
            rig.Step();
            Check("back to hs2: Custom again", sky.Ambient.Rule == "hs2" && RenderSettings.ambientMode == AmbientMode.Custom);
            var copy = sky.SkyMaterial;
            rig.Dispose();
            Check("dispose destroys the copy and the sun source", !copy && !RenderSettings.skybox);
        }
    }

    // ------------------------------------------------------------------ SA5 falloff

    static void TestFalloff()
    {
        Section("SA5 Built-in-compatible falloff vs the U10 tables");
        string tsv = Path.Combine(OutDir, "u10_builtin_falloff.tsv");
        if (!File.Exists(tsv)) { Check("U10 table present (SkyAmbientTest.MeasureFalloff)", false, tsv); return; }
        var rows = File.ReadAllLines(tsv).Select(l => l.Split('\t')).Where(r => r.Length >= 4 && r[0] == "T").ToList();
        float P(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        float dmax = 0f, cmax = 0f;
        foreach (var r in rows.Where(r => r[1] == "point" || r[1] == "spotFalloff"))
            dmax = Mathf.Max(dmax, Mathf.Abs(LightFalloff.BuiltinDistance(P(r[2])) - P(r[3])));
        foreach (var r in rows.Where(r => r[1] == "cookieRow" || r[1] == "cookieCol" || r[1] == "cookieDiag"))
        {
            float u = P(r[2]);
            float rad = Mathf.Abs(u - 0.5f) * 2f * (r[1] == "cookieDiag" ? Mathf.Sqrt(2f) : 1f);
            if (rad > 1f) continue;
            cmax = Mathf.Max(cmax, Mathf.Abs(LightFalloff.BuiltinCookie(rad) - P(r[3])));
        }
        Check("distance falloff = Built-in's texture within 0.0005 (1024 + 1024 samples)", dmax <= 5e-4f, F(dmax));
        Check("spot cookie = Built-in's within 0.01 (axes and diagonal)", cmax <= 0.01f, F(cmax));
        // URP vs Built-in at range 10 (the reason for E5)
        foreach (float d in new[] { 1f, 2f, 3f, 5f, 8f })
            Info($"range 10, distance {d}: Built-in {F(LightFalloff.Builtin(new Vector3(0, 0, d), 10f, 0f))}, URP {F(LightFalloff.Urp(new Vector3(0, 0, d), 10f, 0f, 0f))}");
        RenderOptions.Apply(JObject.Parse("{\"lightFalloff\":\"urp\"}"));
        bool urp = LightFalloff.CommandLineOverride != null || LightFalloff.GlobalValue == 0f;
        RenderOptions.ResetToDefaults();
        bool hs2 = LightFalloff.CommandLineOverride != null || LightFalloff.GlobalValue == 1f;
        Check("_HS2NextLegacyFalloff follows render.lightFalloff (urp 0, hs2 1)", urp && hs2, "command line " + (LightFalloff.CommandLineOverride ?? "none"));
    }

    // ------------------------------------------------------------------ SA6 AA

    static void TestAa()
    {
        Section("SA6 anti-aliasing option");
        var setup = new ViewSetup();
        var go = new GameObject("aa test cam");
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            foreach (var (opt, mode) in new[] { ("smaa", AntialiasingMode.SubpixelMorphologicalAntiAliasing), ("taa", AntialiasingMode.TemporalAntiAliasing), ("fxaa", AntialiasingMode.FastApproximateAntialiasing), ("none", AntialiasingMode.None) })
            {
                RenderOptions.Apply(JObject.Parse("{\"aa\":\"" + opt + "\",\"taaQuality\":\"veryHigh\"}"));
                TemporalAAControl.Configure(setup, cam);
                setup.ApplyTo(cam, false);
                TemporalAAControl.Configure(setup, cam);
                var d = cam.GetComponent<UniversalAdditionalCameraData>();
                bool ok = setup.Antialiasing == mode && d.antialiasing == mode && (mode != AntialiasingMode.TemporalAntiAliasing || d.taaSettings.quality == TemporalAAQuality.VeryHigh);
                int frame = TemporalAAControl.DitherFrameFor(cam);
                Check($"render.aa {opt} → {mode}" + (opt == "taa" ? " (quality VeryHigh, dither frame frameCount & 3)" : " (dither frame 0)"), ok && (opt == "taa" ? frame == (Time.frameCount & 3) : frame == 0));
                setup.ApplyTo(cam, true);
                Check($"… a capture renders SMAA High whatever the option ({opt})", d.antialiasing == AntialiasingMode.SubpixelMorphologicalAntiAliasing && d.antialiasingQuality == AntialiasingQuality.High);
            }
        }
        finally
        {
            RenderOptions.ResetToDefaults();
            Object.DestroyImmediate(go);
        }
    }

    // ------------------------------------------------------------------ SA7 no map

    static void TestNoMap()
    {
        Section("SA7 the no-map environment (U15)");
        using (var rig = new Rig(Services()))
        {
            rig.Step();
            Check("ambient intensity 1.5 (the Studio level), fog mode Exp² (fog off)", Mathf.Approximately(RenderSettings.ambientIntensity, 1.5f) && RenderSettings.fogMode == FogMode.ExponentialSquared && !RenderSettings.fog);
            var tex = RenderSettings.customReflectionTexture;
            Check("default reflection Custom = sky_BigSight_Env (= level2's Tokyo_BigSight_Env), intensity 1",
                RenderSettings.defaultReflectionMode == DefaultReflectionMode.Custom && tex && tex.name == SceneSky.NoMapReflectionAsset && Mathf.Approximately(RenderSettings.reflectionIntensity, 1f),
                RenderSettings.defaultReflectionMode + " " + (tex ? tex.name : "none"));
            Check("the probe: HS2's SH of the default colours (rule hs2)", rig.Sky.Ambient.Source == "colours" && MaxDiff(RenderSettings.ambientProbe, Hs2DefaultSh) <= 1.5e-6f, F(MaxDiff(RenderSettings.ambientProbe, Hs2DefaultSh)));
        }
    }
}
