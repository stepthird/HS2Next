// WetTest (Phase 6.6): stains, drops, puddles and rebuilds on the SFW targets of Tools/targets.json (BR-Chan, HERO107), judged by numbers;
// images stay in memory and nothing is written as a picture.
//   WetTest.Run        headless, edit mode (AppCore.ForTests hosts, frames pumped by hand); report Spike/out/wet/wet_report.txt
//   WetTest.RunRender  graphics, edit mode, the production URP asset; report Spike/out/wet/wet_render_report.txt
//   WetTest.RunPlay    graphics, play mode, the app with the UI (port 17859); report Spike/out/wet/wet_play_report.txt
// The checks live in partial files of this class found by name: a static method named Headless_<part>() (no arguments) is a part of Run, Render_<part>() a part
// of RunRender, Play_<part>() (an IEnumerator) a part of RunPlay; the parts run in ordinal order of their names and open their groups with Group(id, title,
// action) or POpen(id, title). -only WB1+WD8 runs those groups. Each part owns its file.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HS2Next;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

public static partial class WetTest
{
    public const float Dt = 1f / 60f;
    /// <summary>The play row's port.</summary>
    public const int PlayPort = 17859;
    public const ulong DefaultSeed = 0x66e7a5eedUL;
    public static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "wet");
    // -report <path> keeps a run's report apart when several people use the row
    static string Report => Arg("-report") ?? Path.Combine(OutDir, "wet_report.txt");
    static string RenderReport => Arg("-report") ?? Path.Combine(OutDir, "wet_render_report.txt");
    static string PlayReport => Arg("-report") ?? Path.Combine(OutDir, "wet_play_report.txt");

    static StringBuilder rep;
    static int passes, fails, warns;
    static string current = "";
    static readonly Dictionary<string, (int pass, int fail)> perGroup = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
    static readonly List<string> measurements = new List<string>();
    static string[] only;

    /// <summary>Catalog and Studio lists, built once by a run (shared by every host; never disposed by a part).</summary>
    static RuntimeServices services;
    static TestTargets targets;
    /// <summary>The SFW visual targets (absolute card paths; relative to the HS2 card folders for chara.load).</summary>
    static string femaleCard, maleCard, femaleRel, maleRel;

    // ------------------------------------------------------------ report

    static string Arg(string name)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    static void Info(string s) => rep.AppendLine("   " + s);
    static void Warn(string s) { warns++; rep.AppendLine($"WARN  [{current}] {s}"); }
    static void Measure(string s) { measurements.Add(s); Info(s); }

    static bool Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  [{current}] {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
        perGroup.TryGetValue(current, out var v);
        perGroup[current] = ok ? (v.pass + 1, v.fail) : (v.pass, v.fail + 1);
        return ok;
    }

    static bool Want(string id) => only == null || only.Contains(id);

    /// <summary>Runs one group (skipped unless -only names it, when given); an exception is a FAIL of the group.</summary>
    static void Group(string id, string title, Action a)
    {
        if (!Want(id)) return;
        current = id;
        rep.AppendLine();
        rep.AppendLine($"== {id} {title}");
        try { a(); }
        catch (Exception e) { Check("exception", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
    }

    /// <summary>Opens a play group; false when -only leaves it out.</summary>
    static bool POpen(string id, string title)
    {
        if (!Want(id)) return false;
        current = id;
        rep.AppendLine();
        rep.AppendLine($"== {id} {title}");
        return true;
    }

    static string Short(string s) => s == null ? "null" : s.Length > 400 ? s.Substring(0, 400) + "..." : s;

    static void Begin()
    {
        rep = new StringBuilder(); passes = fails = warns = 0; perGroup.Clear(); measurements.Clear();
        var o = Arg("-only");
        only = o == null ? null : o.Split(',', '+').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        Directory.CreateDirectory(OutDir);
    }

    static void End(string path, Stopwatch sw, string title)
    {
        var sum = new StringBuilder();
        sum.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL ({warns} WARN, {sw.Elapsed.TotalSeconds:F0} s)");
        foreach (var kv in perGroup.OrderBy(k => k.Key, StringComparer.Ordinal)) sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail");
        if (measurements.Count > 0) { sum.AppendLine("measurements:"); foreach (var m in measurements) sum.AppendLine("  " + m); }
        sum.AppendLine(title);
        rep.Insert(0, sum.ToString());
        File.WriteAllText(path, rep.ToString(), new UTF8Encoding(false));
    }

    /// <summary>The static methods of this class whose names start with <paramref name="prefix"/>, in ordinal order of their names.</summary>
    static List<MethodInfo> Parts(string prefix, Type returns) =>
        typeof(WetTest).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(m => m.Name.StartsWith(prefix, StringComparison.Ordinal) && m.GetParameters().Length == 0 && m.ReturnType == returns)
            .OrderBy(m => m.Name, StringComparer.Ordinal).ToList();

    static void RunParts(string prefix)
    {
        var parts = Parts(prefix, typeof(void));
        Info("parts: " + string.Join(", ", parts.Select(p => p.Name.Substring(prefix.Length))));
        foreach (var p in parts)
        {
            current = p.Name;
            try { p.Invoke(null, null); }
            catch (TargetInvocationException e) { Check("part " + p.Name, false, e.InnerException?.ToString() ?? e.ToString()); }
            catch (Exception e) { Check("part " + p.Name, false, e.ToString()); }
        }
    }

    // ------------------------------------------------------------ headless (WetTest.Run)

    public static void Run()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        var head = new StringBuilder();
        try
        {
            services = SceneRuntimeTest.NewServices(head);
            LoadTargets();
            rep.AppendLine($"WetTest (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
            rep.Append(head);
            RunParts("Headless_");
        }
        catch (Exception e) { current = "run"; Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        End(Report, sw, "WetTest.Run (headless)");
        HS2Next.Motion.Face.FaceSetup.Seed = null;
        services?.Catalog?.UnloadAll();
        Debug.Log($"WET TEST DONE: {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }

    // ------------------------------------------------------------ graphics (WetTest.RunRender)

    /// <summary>The production URP asset for a run, every setting back at the end.</summary>
    sealed class Pipeline : IDisposable
    {
        readonly RenderPipelineAsset d, q;
        readonly bool async;
        public readonly bool Ok;
        public Pipeline()
        {
            d = GraphicsSettings.defaultRenderPipeline; q = QualitySettings.renderPipeline; async = ShaderUtil.allowAsyncCompilation;
            var prod = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AppPipeline.EditorAssetPath);
            Ok = prod && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            if (!Ok) return;
            ShaderUtil.allowAsyncCompilation = false;
            GraphicsSettings.defaultRenderPipeline = prod; QualitySettings.renderPipeline = prod;
        }
        public void Dispose()
        {
            GraphicsSettings.defaultRenderPipeline = d; QualitySettings.renderPipeline = q; ShaderUtil.allowAsyncCompilation = async;
        }
    }

    public static void RunRender()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        var head = new StringBuilder();
        try
        {
            services = SceneRuntimeTest.NewServices(head);
            LoadTargets();
            rep.AppendLine($"WetTest.render (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
            rep.Append(head);
            using (var p = new Pipeline())
            {
                current = "render";
                if (Check("the production URP asset and a graphics device", p.Ok, SystemInfo.graphicsDeviceType.ToString())) RunParts("Render_");
            }
        }
        catch (Exception e) { current = "run"; Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        End(RenderReport, sw, "WetTest.RunRender (images in memory only)");
        HS2Next.Motion.Face.FaceSetup.Seed = null;
        services?.Catalog?.UnloadAll();
        Debug.Log($"WET RENDER TEST DONE: {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }

    static void LoadTargets()
    {
        targets = TestTargets.Load(Arg("-targets"));
        femaleCard = TestTargets.Abs(targets.visual.female);
        maleCard = TestTargets.Abs(targets.visual.male);
        femaleRel = RelToCards(targets.visual.female);
        maleRel = RelToCards(targets.visual.male);
    }

    /// <summary>A UserData-relative card path made relative to its HS2 card folder (what chara.load's path takes with sex).</summary>
    static string RelToCards(string rel)
    {
        if (string.IsNullOrEmpty(rel)) return rel;
        foreach (var pre in new[] { "chara/female/", "chara/male/" })
            if (rel.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) return rel.Substring(pre.Length);
        return rel;
    }

    // ------------------------------------------------------------ hosts (edit mode)

    sealed class Host : IDisposable
    {
        public AppCore App;
        public SceneRuntime Rt => App.Runtime;
        public SceneSession Session => App.Session;
        public string LastText;

        public JObject Call(object request)
        {
            string json = request is string s ? s : JsonConvert.SerializeObject(request);
            LastText = App.Call(json);
            return JObject.Parse(LastText);
        }

        /// <summary>Calls and checks ok: true; returns the reply.</summary>
        public JObject Ok(object request, string what)
        {
            var r = Call(request);
            Check(what, r.Value<bool?>("ok") == true, r.Value<bool?>("ok") == true ? "" : Short(LastText));
            return r;
        }

        public void Frames(int n, float dt = Dt)
        {
            for (int i = 0; i < n; i++) App.Frame(dt);
        }

        public void Dispose() { try { App?.Dispose(); } catch (Exception e) { Info("host dispose threw: " + e.Message); } }
    }

    static string UserDataDir => Path.Combine(OutDir, "userdata");
    static string OutputDir => Path.Combine(OutDir, "out");

    /// <summary>A fresh test host: counting ids; the parts' frames step every built character with AutoStep.</summary>
    static Host NewHost(ulong seed = DefaultSeed)
    {
        HS2Next.Motion.Face.FaceSetup.Seed = 0x5eed;
        var s = AppSettings.Defaults(Array.Empty<string>());
        s.Content = CardResolveTest.Settings;
        s.UserData = UserDataDir;
        s.Bridge.OutputDir = OutputDir;
        var o = services.NewSessionOptions();
        o.Ids = new CountingIdSource(seed);
        return new Host { App = AppCore.ForTests(services, s, null, o) };
    }

    /// <summary>The node id of an add reply: its key, or its first added node's key (chara.load, item.add).</summary>
    static string Key(JObject r) => (string)r?["key"] ?? (string)r?["added"]?[0]?["key"];

    /// <summary>Loads a visual target into a host; its key.</summary>
    static string LoadChara(Host h, bool male = false)
    {
        var r = h.Ok(new { cmd = "chara.load", path = male ? maleRel : femaleRel, sex = male ? "male" : "female" }, "chara.load");
        string key = Key(r);
        h.Rt.ApplyNow();
        return key;
    }

    // ------------------------------------------------------------ play mode (WetTest.RunPlay)

    static JObject pLast;
    static string pLastText;

    static bool POk => pLast?.Value<bool?>("ok") == true;

    /// <summary>One bridge request in real frames; the reply lands in pLast.</summary>
    static IEnumerator PCall(AppCore app, object req, double timeoutSec = 300)
    {
        string json = req is string s ? s : JsonConvert.SerializeObject(req);
        var t = app.Bridge.Execute(json);
        float until = Time.realtimeSinceStartup + (float)timeoutSec;
        while (!t.IsCompleted && Time.realtimeSinceStartup < until) yield return null;
        pLastText = t.IsCompleted ? (t.IsFaulted ? "{\"ok\":false,\"error\":\"faulted\"}" : t.Result) : "{\"ok\":false,\"code\":\"timeout\"}";
        try { pLast = JObject.Parse(pLastText); }
        catch (Exception) { pLast = new JObject { ["ok"] = false, ["error"] = "unparsable: " + pLastText }; }
    }

    /// <summary>n real frames (the harness pumps a coroutine from the Editor's update, so one yield is not one frame).</summary>
    static IEnumerator PFrames(int n) => PlayModeHarness.WaitFrames(n);

    static string PlayUserData => "Spike/out/wet/play_userdata";
    static string PlayLaunch =>
        "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":true,\"language\":\"en\"},\"userData\":\"" + PlayUserData + "\",\"bridge\":{\"port\":" + PlayPort +
        ",\"outputDir\":\"Spike/out/wet/play_out\",\"allowQuit\":false}}";

    public static void RunPlay()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(PlayReport, "SUMMARY: 0 PASS, 1 FAIL\nWetTest.play: started, no result yet\n", new UTF8Encoding(false));
        PlayModeHarness.Start("WetTest.PlayMain", PlayReport, 25, true, PlayLaunch);
    }

    static IEnumerator PlayMain()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"WetTest.play (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, screen {Screen.width}x{Screen.height})");
        try { LoadTargets(); }
        catch (Exception e) { current = "play"; Check("targets", false, e.ToString()); }
        var parts = Parts("Play_", typeof(IEnumerator));
        Info("parts: " + string.Join(", ", parts.Select(p => p.Name.Substring("Play_".Length))));
        foreach (var p in parts)
        {
            current = p.Name;
            IEnumerator body = null;
            try { body = (IEnumerator)p.Invoke(null, null); }
            catch (Exception e) { Check("part " + p.Name + " started", false, (e.InnerException ?? e).ToString()); }
            while (body != null)
            {
                object cur;
                try { if (!body.MoveNext()) break; cur = body.Current; }
                catch (Exception e) { Check("part " + p.Name + " ran without an exception", false, e.ToString()); break; }
                yield return cur;
            }
        }
        End(PlayReport, sw, "WetTest.RunPlay (play mode)");
        Debug.Log($"WET PLAY TEST DONE: {passes} PASS, {fails} FAIL");
        PlayModeHarness.ExitCode = fails > 0 ? 1 : 0;
    }
}
