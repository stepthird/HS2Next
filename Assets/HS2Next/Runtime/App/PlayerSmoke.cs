// Player smoke test (preparation for Phase 2): checks that a built Windows player reads the
// settings, opens the catalog, assembles a character and renders it with the fitted URP shaders like the Editor tests.
// Runs only in a player started with -smoke <outDir>; the Smoke scene (Assets/HS2Next/Scenes/Smoke.unity, written by
// Editor/PlayerBuild.cs) holds this component. Without -smoke the player quits with exit code 2.
// Arguments: -smoke <outDir>; the content settings as in the Editor tests (-settings <file>, -gameRoot <dir>,
// -cacheDir <dir>, -modsFolder <dir>; relative paths are relative to the player folder, which is ContentSettings.ProjectRoot
// in a player); -card <path relative to the game's UserData> (default: femaleCard, copied from Tools/targets.json
// "visual.female" when the scene is built); -motionFrames N (default 60).
// Steps: settings -> shader set (Resources/HS2NextShaders, WarmUp) -> catalog (vanilla + mod lists) -> card -> the document
// path as the app builds a character (the importer's documents -> CharaDocumentInput -> CharaAssembler.Build) -> ShaderMap.ApplyUrp -> camera and light of CharaRenderTest's front full view (900x1200, MSAA 4),
// a few frames with the motion stages paused -> <outDir>/smoke_female.png (comparable with the Editor's
// <card>_urp_full.png) -> motion stages on for -motionFrames frames -> <outDir>/smoke_female_motion.png.
// Report <outDir>/smoke_report.txt: timings, materials by original shader (fitted / missing replacement / URP/Lit
// fallback), SystemInfo, log message counts (deprecated serialized files, missing scripts, warnings, errors), exceptions.
// Exit code: 0 = no material on the URP/Lit fallback or with a missing replacement, no error shader, a graphics device,
// and the front PNG has a non-black foreground; 1 = otherwise or an exception; 2 = started without -smoke.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Chara;
using HS2Next.Chara.Documents;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.Motion;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace HS2Next.App
{
    public sealed class PlayerSmoke : MonoBehaviour
    {
        /// <summary>The card to build, relative to the game's UserData folder (PlayerBuild copies Tools/targets.json visual.female).</summary>
        public string femaleCard = "";

        const string LitName = "Universal Render Pipeline/Lit";
        static readonly Color Background = new Color(0.30f, 0.32f, 0.36f);   // CharaRenderTest

        readonly StringBuilder rep = new StringBuilder();
        readonly List<string> failures = new List<string>();
        readonly object logLock = new object();
        readonly Dictionary<string, int> otherLogs = new Dictionary<string, int>();
        readonly List<string> otherOrder = new List<string>();
        int nLog, nWarn, nErr, nExc, nAssert, nDeprecated, nMissingScript, nMip;
        string[] args;
        string outDir;
        float awakeRealtime;
        double awakeSinceProcessStart;
        ContentCatalog catalog;
        GameObject chara;
        CharaMotion motion;
        Camera cam;
        Light key;
        RenderTexture rt;
        Cubemap blackCube;

        void Awake()
        {
            awakeRealtime = Time.realtimeSinceStartup;
            try { awakeSinceProcessStart = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds; }
            catch { awakeSinceProcessStart = -1; }
            Application.logMessageReceivedThreaded += OnLog;
            Application.runInBackground = true;   // an unfocused window must not stop the frame loop
        }

        void OnDestroy() => Application.logMessageReceivedThreaded -= OnLog;

        IEnumerator Start()
        {
            args = Environment.GetCommandLineArgs();
            outDir = Arg("-smoke");
            if (string.IsNullOrEmpty(outDir))
            {
                Debug.Log("PlayerSmoke: no -smoke <outDir> argument, quitting with exit code 2");
                Application.Quit(2);
                yield break;
            }
            var total = Stopwatch.StartNew();
            Directory.CreateDirectory(outDir);
            int motionFrames = int.TryParse(Arg("-motionFrames"), out var mf) ? Mathf.Max(0, mf) : 60;
            Line($"HS2Next player smoke  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Line($"startup: process start -> first Awake {awakeSinceProcessStart:F0} ms (realtimeSinceStartup {awakeRealtime * 1000:F0} ms)");
            DescribeEnvironment();
            yield return null;   // one frame after the scene load

            bool ok = Try("prepare", Prepare);
            if (ok)
            {
                ok = Try("camera", PlaceCamera);
                for (int i = 0; ok && i < 5; i++) yield return null;   // motion paused: the state of the Editor render
                if (ok) ok = Try("render front", () => Render(Path.Combine(outDir, "smoke_female.png"), "front", true));
            }
            if (ok && motion && motionFrames > 0)
            {
                motion.AutoStep = true;
                Application.targetFrameRate = 60;   // batch mode runs uncapped: give the stages about one second of time
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < motionFrames; i++) yield return null;
                Line($"motion: {motionFrames} frames in {sw.ElapsedMilliseconds} ms, steps {motion.StepCount}, stage errors {motion.Errors}, time {motion.Time:F2} s");
                if (motion.Errors > 0) Line("   (stage errors are logged once per stage; see the assembler log lines above and the player log)");
                Try("render motion", () => Render(Path.Combine(outDir, "smoke_female_motion.png"), "motion", false));
            }
            Try("cleanup", Cleanup);
            Line($"total: {total.ElapsedMilliseconds} ms after the first frame; {Memory()}");
            Finish();
        }

        // ------------------------------------------------------------------ steps

        void DescribeEnvironment()
        {
            Line($"unity {Application.unityVersion}, debug build {Debug.isDebugBuild}, batch mode {Application.isBatchMode}, platform {Application.platform}");
            Line($"scripting: {(Type.GetType("Mono.Runtime") != null ? "Mono" : "not Mono (IL2CPP?)")}, CLR {System.Environment.Version}, {IntPtr.Size * 8}-bit");
            Line($"graphics: {SystemInfo.graphicsDeviceType} '{SystemInfo.graphicsDeviceName}' {SystemInfo.graphicsDeviceVersion}, VRAM {SystemInfo.graphicsMemorySize} MB, shader level {SystemInfo.graphicsShaderLevel}");
            Line($"render pipeline: {(GraphicsSettings.currentRenderPipeline ? GraphicsSettings.currentRenderPipeline.name : "Built-in (none)")}, colour space {QualitySettings.activeColorSpace}, quality '{QualitySettings.names[QualitySettings.GetQualityLevel()]}', screen {Screen.width}x{Screen.height}");
            Line($"system: {SystemInfo.operatingSystem}, {SystemInfo.processorType} x{SystemInfo.processorCount}, RAM {SystemInfo.systemMemorySize} MB");
            Line($"player folder (ContentSettings.ProjectRoot): {ContentSettings.ProjectRoot}");
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Fail("no graphics device (started with -batchmode or -nographics?)");
        }

        void Prepare()
        {
            var sw = Stopwatch.StartNew();
            var settings = ContentSettings.LoadDefault(args);
            string root = ContentSettings.Resolve(settings.hs2Root);
            Line($"settings: {sw.ElapsedMilliseconds} ms; game root {(string.IsNullOrEmpty(root) ? "(none)" : root)}, extra mods folders {settings.modsFolders.Length}, cache {settings.ResolvedCacheDir}");
            if (string.IsNullOrEmpty(root)) throw new Exception("no game root in the settings");

            // Shader set: guarantees the fitted shaders (and URP/Lit's fallback variants) are loaded before Shader.Find.
            sw.Restart();
            var svc = Resources.Load<ShaderVariantCollection>("HS2NextShaders");
            long tLoad = sw.ElapsedMilliseconds;
            if (svc)
            {
                sw.Restart();
                svc.WarmUp();
                Line($"shader set: Resources/HS2NextShaders {svc.shaderCount} shaders, {svc.variantCount} listed variants; load {tLoad} ms, WarmUp {sw.ElapsedMilliseconds} ms");
            }
            else Line("shader set: Resources/HS2NextShaders NOT FOUND (fitted shaders rely on other references)");
            var lit = Shader.Find(LitName);
            Line($"URP/Lit (fallback): {(lit ? $"found, supported {lit.isSupported}" : "NOT FOUND")}");

            sw.Restart();
            catalog = ContentCatalog.FromSettings(settings);
            catalog.LoadVanillaLists();
            long tv = sw.ElapsedMilliseconds;
            catalog.LoadModLists();
            Line($"catalog: vanilla lists {tv} ms, mod lists {sw.ElapsedMilliseconds - tv} ms; {string.Join("; ", catalog.Log.Take(3))}");

            string rel = Arg("-card") ?? femaleCard;
            if (string.IsNullOrEmpty(rel)) throw new Exception("no card: the scene has no femaleCard and -card is missing");
            string cardPath = Path.Combine(root, "UserData", rel.Replace('/', Path.DirectorySeparatorChar));
            sw.Restart();
            byte[] cardBytes = File.ReadAllBytes(cardPath);
            var card = Card.Load(cardBytes, cardPath);
            long tCard = sw.ElapsedMilliseconds;
            sw.Restart();
            // the document path: the card imported into documents, the character built from them (as the app does)
            var findings = new HS2Next.Save.FindingList();
            var src = new HS2Next.Scenes.CardAppearanceSource { FullPath = cardPath, Sha256 = HS2Next.Save.ResStore.Sha256Hex(cardBytes), ReadBytes = () => cardBytes, Name = card.Name };
            var imported = HS2Next.Scenes.Runtime.ImportSetup.ImporterFor(catalog).Import(src, findings)
                ?? throw new Exception("the importer returned nothing for " + rel);
            long tImport = sw.ElapsedMilliseconds;
            sw.Restart();
            var input = CharaDocumentInput.From(imported.Subtree, imported.Res, catalog, findings);
            var asm = new CharaAssembler(catalog);
            chara = asm.Build(input);
            long tBuild = sw.ElapsedMilliseconds;
            Line($"card: {rel} '{card.Name}' read in {tCard} ms; imported in {tImport} ms; built from the documents in {tBuild} ms (male {asm.IsMale}); findings {findings.Count}");
            foreach (var l in asm.Log) Line("   " + l);
            if (asm.Context != null) Line("   ext: " + asm.Context.Stats.Summary());
            if (TextureGuard.Incidents > 0) Line($"   texture guard: {TextureGuard.Incidents} incidents, {TextureGuard.Replaced} textures replaced");
            motion = asm.Context?.Motion;
            if (motion) motion.AutoStep = false;   // first render: rest state as in the Editor (edit mode, no steps)

            // Materials by original shader, classified as ShaderMap.ApplyUrp will treat them.
            var byShader = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var shaderOf = new Dictionary<string, Shader>(StringComparer.Ordinal);   // the first loaded shader of each name
            var seen = new HashSet<Material>();
            int renderers = 0;
            foreach (var r in chara.GetComponentsInChildren<Renderer>(true))
            {
                renderers++;
                foreach (var m in r.sharedMaterials)
                    if (m && seen.Add(m)) { string n = m.shader ? m.shader.name : "(no shader)"; byShader[n] = byShader.TryGetValue(n, out var k) ? k + 1 : 1; if (m.shader && !shaderOf.ContainsKey(n)) shaderOf[n] = m.shader; }
            }
            int fitted = 0, kept = 0;
            var fallback = new List<string>(); var missing = new List<string>();
            foreach (var kv in byShader)
            {
                if (kv.Key.StartsWith("Universal Render Pipeline/") || kv.Key.StartsWith("HS2Next/")) { kept += kv.Value; continue; }
                if (ShaderMap.TryResolve(kv.Key, shaderOf.TryGetValue(kv.Key, out var ks) ? ks : null, out var fn))
                {
                    if (Shader.Find(fn)) fitted += kv.Value; else missing.Add($"{kv.Key} -> {fn} x{kv.Value}");
                    continue;
                }
                fallback.Add($"{kv.Key} x{kv.Value}");
            }
            Line($"materials: {seen.Count} on {renderers} renderers; fitted {fitted}, already URP {kept}");
            Line("   by original shader: " + string.Join(", ", byShader.Select(kv => $"{kv.Key} x{kv.Value}")));
            Line("   missing replacement (fitted shader not in the player): " + (missing.Count == 0 ? "none" : string.Join(", ", missing)));
            Line("   fallback (URP/Lit) shaders: " + (fallback.Count == 0 ? "none" : string.Join(", ", fallback)));
            foreach (var s in missing) Fail("fitted shader missing: " + s);
            foreach (var s in fallback) Fail("URP/Lit fallback: " + s);

            sw.Restart();
            int swapped = ShaderMap.ApplyUrp(chara);
            long tUrp = sw.ElapsedMilliseconds;
            var after = new SortedDictionary<string, int>(StringComparer.Ordinal);
            seen.Clear();
            foreach (var r in chara.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (!m || !seen.Add(m)) continue;
                    string n = !m.shader ? "(no shader)" : m.shader.isSupported ? m.shader.name : m.shader.name + " (UNSUPPORTED)";
                    after[n] = after.TryGetValue(n, out var k) ? k + 1 : 1;
                }
            Line($"URP: swapped {swapped} materials in {tUrp} ms; now " + string.Join(", ", after.Select(kv => $"{kv.Key} x{kv.Value}")));
            foreach (var kv in after)
                if (kv.Key.Contains("UNSUPPORTED") || kv.Key.Contains("InternalErrorShader") || kv.Key == "(no shader)") Fail($"bad shader after ApplyUrp: {kv.Key} x{kv.Value}");

            PluginLoadProbe();
        }

        // Phase 5.1 plugins need Assembly.Load in the player (Mono). Load a copy of a small managed assembly of this
        // player from bytes; informational, never fails the run.
        void PluginLoadProbe()
        {
            try
            {
                string f = Path.Combine(Application.dataPath, "Managed", "DebugComponents.dll");
                if (!File.Exists(f)) { Line("assembly load probe: Managed/DebugComponents.dll not present, skipped"); return; }
                var sw = Stopwatch.StartNew();
                var a = System.Reflection.Assembly.Load(File.ReadAllBytes(f));
                Line($"assembly load probe: Assembly.Load(byte[]) of Managed/DebugComponents.dll -> {a.GetName().Name}, {a.GetTypes().Length} types, {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception e) { Line($"assembly load probe: FAILED {e.GetType().Name}: {e.Message}"); }
        }

        void PlaceCamera()
        {
            var amb = new Color(0.42f, 0.42f, 0.45f);
            var sh = new SphericalHarmonicsL2(); sh.AddAmbientLight(amb.linear);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = amb;
            RenderSettings.ambientProbe = sh;
            RenderSettings.skybox = null;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = BlackCube(); RenderSettings.reflectionIntensity = 0;
            key = new GameObject("key").AddComponent<Light>();
            key.type = LightType.Directional; key.intensity = 1.0f; key.shadows = LightShadows.Soft;
            RenderingLayers.ApplyToLight(key);   // the app's asset filters by rendering layers (culling mask everything)
            key.transform.rotation = Quaternion.Euler(30, 200, 0);
            var b = new Bounds(chara.transform.position, Vector3.zero);
            foreach (var r in chara.GetComponentsInChildren<Renderer>()) if (r.enabled && r.gameObject.activeInHierarchy) b.Encapsulate(r.bounds);
            cam = new GameObject("cam").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Background;
            cam.fieldOfView = 24; cam.nearClipPlane = 0.5f; cam.farClipPlane = 1000;
            float h = Mathf.Max(b.size.y, 1);
            float d = h / (2 * Mathf.Tan(12 * Mathf.Deg2Rad)) * 1.08f;
            cam.transform.position = b.center + Vector3.forward * d;   // HS2 faces +Z; CharaRenderTest view "full"
            cam.transform.LookAt(b.center);
            rt = new RenderTexture(900, 1200, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;   // also rendered by the frame loop while we wait
            Line($"camera: bounds centre {b.center:F3} size {b.size:F3}, distance {d:F3}");
        }

        void Render(string file, string label, bool judge)
        {
            var sw = Stopwatch.StartNew();
            cam.Render(); cam.Render();   // as CharaRenderTest
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            RenderTexture.active = null;
            long tRender = sw.ElapsedMilliseconds;
            var px = tex.GetPixels32();
            var bg = px[0];
            long fg = 0, magenta = 0; double sum = 0;
            foreach (var p in px)
            {
                if (p.r > 200 && p.g < 60 && p.b > 200) magenta++;
                if (Math.Abs(p.r - bg.r) <= 2 && Math.Abs(p.g - bg.g) <= 2 && Math.Abs(p.b - bg.b) <= 2) continue;
                fg++; sum += (p.r + p.g + p.b) / 3.0;
            }
            double mean = fg > 0 ? sum / fg : 0, frac = fg / (double)px.Length;
            sw.Restart();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.Destroy(tex);
            Line($"render {label}: {tRender} ms (2 renders + readback), PNG {sw.ElapsedMilliseconds} ms -> {Path.GetFileName(file)}; background ({bg.r},{bg.g},{bg.b}), foreground {frac:P1} of {px.Length} px, mean foreground {mean:F1}/255, magenta px {magenta}");
            if (!judge) return;
            if (frac < 0.01 || mean < 5) Fail($"render {label}: no visible character (foreground {frac:P2}, mean {mean:F1})");
            if (magenta > px.Length / 1000) Fail($"render {label}: {magenta} magenta pixels (error shader?)");
        }

        void Cleanup()
        {
            if (cam) { cam.targetTexture = null; Destroy(cam.gameObject); }
            if (key) Destroy(key.gameObject);
            if (rt) { rt.Release(); Destroy(rt); }
            if (chara) Destroy(chara);
            catalog?.UnloadAll();
        }

        void Finish()
        {
            lock (logLock)
            {
                Line($"log: {nLog} messages, {nWarn} warnings, {nErr} errors, {nExc} exceptions, {nAssert} asserts");
                Line($"   'Serialized files ... before 2019.1 are deprecated': {nDeprecated}; missing scripts: {nMissingScript}; 'mipLevel' asserts: {nMip}");
                foreach (var m in otherOrder.Take(40)) Line($"   x{otherLogs[m]} {m}");
                if (otherOrder.Count > 40) Line($"   ... {otherOrder.Count - 40} more distinct warnings / errors");
            }
            int code = failures.Count == 0 ? 0 : 1;
            rep.Insert(0, $"RESULT: {(code == 0 ? "PASS" : "FAIL")} (exit {code}){(failures.Count > 0 ? "; " + string.Join("; ", failures) : "")}\n");
            try
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "smoke_report.txt"), rep.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogError("PlayerSmoke: cannot write the report: " + e); code = 1; }
            Debug.Log($"PLAYER SMOKE DONE exit {code}");
            Application.Quit(code);
        }

        // ------------------------------------------------------------------ helpers

        bool Try(string step, Action a)
        {
            try { a(); return true; }
            catch (Exception e)
            {
                Fail($"{step}: {e.GetType().Name}: {e.Message}");
                Line($"EXCEPTION in {step}: {e}");
                return false;
            }
        }

        void Fail(string msg) { failures.Add(msg); Line("FAIL " + msg); }

        void Line(string s) { rep.AppendLine(s); }

        string Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static string Memory()
        {
            long ws = -1;
            try { using (var p = Process.GetCurrentProcess()) ws = p.WorkingSet64 >> 20; } catch { }
            return $"working set {ws} MB, Unity reserved {UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() >> 20} MB " +
                   $"(allocated {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() >> 20} MB), managed heap {UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() >> 20} MB";
        }

        Cubemap BlackCube()
        {
            if (blackCube) return blackCube;
            blackCube = new Cubemap(4, TextureFormat.RGBA32, false);
            var black = Enumerable.Repeat(Color.black, 16).ToArray();
            foreach (CubemapFace f in Enum.GetValues(typeof(CubemapFace)))
                if (f != CubemapFace.Unknown) blackCube.SetPixels(black, f);
            blackCube.Apply();
            return blackCube;
        }

        void OnLog(string msg, string stack, LogType type)
        {
            if (msg == null) return;
            lock (logLock)
            {
                nLog++;
                switch (type)
                {
                    case LogType.Warning: nWarn++; break;
                    case LogType.Error: nErr++; break;
                    case LogType.Exception: nExc++; break;
                    case LogType.Assert: nAssert++; break;
                }
                if (msg.Contains("before 2019.1 are deprecated")) { nDeprecated++; return; }
                if (msg.Contains("referenced script") && msg.Contains("missing")) { nMissingScript++; return; }
                if (msg.Contains("mipLevel")) { nMip++; return; }
                if (type == LogType.Log) return;
                string first = msg.Split('\n')[0];
                if (first.Length > 220) first = first.Substring(0, 220) + "...";
                string k = $"[{type}] {first}";
                if (otherLogs.TryGetValue(k, out var n)) otherLogs[k] = n + 1;
                else { otherLogs[k] = 1; otherOrder.Add(k); }
            }
        }
    }
}
