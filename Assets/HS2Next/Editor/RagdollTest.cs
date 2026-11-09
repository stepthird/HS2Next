// RagdollTest (Phase 6.7): a character as a physics body on the SFW targets of Tools/targets.json (BR-Chan, HERO107), judged by numbers.
//   RagdollTest.Run      headless, edit mode (AppCore.ForTests hosts, frames pumped by hand, the world in a preview scene); report Spike/out/ragdoll/ragdoll_report.txt
//   RagdollTest.RunPlay  play mode, the app (port 17879); report Spike/out/ragdoll/ragdoll_play_report.txt
// The checks live in partial files of this class found by name: a static method named Headless_<part>() (no arguments) is a part of Run, Play_<part>() (an IEnumerator) a part of
// RunPlay; the parts run in ordinal order of their names and open their groups with Group(id, title, action) or POpen(id, title). -only RM2+RP1 runs those groups. Each part owns its
// file. A headless world lives in a preview scene: RagdollWorld.MakeScene is set for the run and put back at its end.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

public static partial class RagdollTest
{
    public const float Dt = 1f / 60f;
    /// <summary>The play row's port.</summary>
    public const int PlayPort = 17879;
    public const ulong DefaultSeed = 0x67a6d011UL;
    public static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "ragdoll");
    // -report <path> keeps a run's report apart when several people use the row
    static string Report => Arg("-report") ?? Path.Combine(OutDir, "ragdoll_report.txt");
    static string PlayReport => Arg("-report") ?? Path.Combine(OutDir, "ragdoll_play_report.txt");

    static StringBuilder rep;
    static int passes, fails, warns;
    static string current = "";
    static readonly Dictionary<string, (int pass, int fail)> perGroup = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
    static readonly List<string> measurements = new List<string>();
    static string[] only;

    /// <summary>Catalog and Studio lists, built once by Run (shared by every host; never disposed by a part).</summary>
    static RuntimeServices services;
    static TestTargets targets;
    /// <summary>The SFW visual targets (relative to the HS2 card folders for chara.load).</summary>
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
        var sw = Stopwatch.StartNew();
        try { a(); }
        catch (Exception e) { Check("exception", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        Info($"({sw.Elapsed.TotalSeconds:F1} s)");
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

    /// <summary>The angle between two rotations in degrees, in double: Quaternion.Angle reads 0 under about 0.16 degrees.</summary>
    internal static double Deg(Quaternion a, Quaternion b) => HS2Next.Scenes.Ragdoll.RagdollMath.AngleDeg(a.x, a.y, a.z, a.w, b.x, b.y, b.z, b.w);

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
        typeof(RagdollTest).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(m => m.Name.StartsWith(prefix, StringComparison.Ordinal) && m.GetParameters().Length == 0 && m.ReturnType == returns)
            .OrderBy(m => m.Name, StringComparer.Ordinal).ToList();

    // ------------------------------------------------------------ headless (RagdollTest.Run)

    public static void Run()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        var head = new StringBuilder();
        var makeScene = RagdollWorld.MakeScene;
        var freeScene = RagdollWorld.FreeScene;
        RagdollWorld.MakeScene = () => EditorSceneManager.NewPreviewScene();
        RagdollWorld.FreeScene = s => EditorSceneManager.ClosePreviewScene(s);
        try
        {
            services = SceneRuntimeTest.NewServices(head);
            LoadTargets();
            rep.AppendLine($"RagdollTest (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
            rep.Append(head);
            var parts = Parts("Headless_", typeof(void));
            Info("parts: " + string.Join(", ", parts.Select(p => p.Name.Substring("Headless_".Length))));
            foreach (var p in parts)
            {
                current = p.Name;
                try { p.Invoke(null, null); }
                catch (TargetInvocationException e) { Check("part " + p.Name, false, e.InnerException?.ToString() ?? e.ToString()); }
                catch (Exception e) { Check("part " + p.Name, false, e.ToString()); }
            }
        }
        catch (Exception e) { current = "run"; Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        RagdollWorld.MakeScene = makeScene;
        RagdollWorld.FreeScene = freeScene;
        End(Report, sw, "RagdollTest.Run (headless)");
        HS2Next.Motion.Face.FaceSetup.Seed = null;
        services?.Catalog?.UnloadAll();
        Debug.Log($"RAGDOLL TEST DONE: {passes} PASS, {fails} FAIL");
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

    /// <summary>The two targets: a name, the card and the sex.</summary>
    static IEnumerable<(string name, string rel, string sex)> Targets()
    {
        yield return ("BR-Chan", femaleRel, "female");
        yield return ("HERO107", maleRel, "male");
    }

    // ------------------------------------------------------------ hosts (headless)

    sealed class Host
    {
        public AppCore App;
        public SceneRuntime Rt => App.Runtime;
        public SceneSession Session => App.Session;
        public RagdollPart Part => App.Runtime.GetPart<RagdollPart>();
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
            if (r.Value<bool?>("ok") != true) Check(what, false, Short(LastText));
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

    static string LoadChara(Host h, string rel, string sex, string what, bool physics = false)
    {
        var k = Key(h.Ok(new { cmd = "chara.load", path = rel, sex }, what));
        if (!physics) h.Ok(new { cmd = "chara.motion", key = k, physics = false }, what + ": physics off");
        return k;
    }

    static void Place(Host h, string id, Vector3 pos, Vector3 rot = default)
    {
        h.Session.Set(id, "transform.pos", new Vec3(pos.x, pos.y, pos.z));
        h.Session.Set(id, "transform.rot", new Vec3(rot.x, rot.y, rot.z));
        h.Rt.ApplyNow();
    }

    /// <summary>The idle row at a fixed moment, standing still unless a speed is given.</summary>
    static void Idle(Host h, string k, float time = 0.5f, float speed = 0f) =>
        h.Ok(new { cmd = "chara.anime", key = k, group = 19, category = 0, no = 0, time, speed }, "the idle row");

    /// <summary>The floor's height in this scene, and any other change of the world's block.</summary>
    static void World(Host h, Action<SceneRagdollWorld> change)
    {
        var b = h.Rt.Model.Ragdoll?.Clone() ?? new SceneRagdollWorld();
        change(b);
        var r = h.Session.Commands.Execute(new RagdollWorldEditCommand(b), ExecuteOptions.NotRecorded);
        if (!r.Ok) Check("the world's block", false, r.Code + ": " + r.Message);
        h.Rt.ApplyNow();
    }

    static void FixedFloor(Host h, float y = 0f) => World(h, b => { b.Floor.Mode = RagdollFloorValues.ModeFixed; b.Floor.Height = y; });

    /// <summary>A change of a character's block, with no history.</summary>
    static void Block(Host h, string k, Action<CharaRagdoll> change)
    {
        var st = (CharaNodeState)h.Rt.Model.Find(k).State;
        var b = st.Ragdoll.Clone();
        change(b);
        var r = h.Session.Commands.Execute(new RagdollEditCommand(k, b), ExecuteOptions.NotRecorded);
        if (!r.Ok) Check("the character's block", false, r.Code + ": " + r.Message);
        h.Rt.ApplyNow();
    }

    static void Preset(Host h, string k, string preset) => Block(h, k, b => RagdollPreset.ApplyTo(b.Values, preset));

    /// <summary>A body on a character.</summary>
    static RagdollBody Body(Host h, string k, string what)
    {
        var r = h.Part.Build(k, ExecuteOptions.NotRecorded);
        if (!r.Ok) Check(what + ": build", false, r.Code + ": " + r.Message);
        return h.Part.BodyOf(k);
    }

    /// <summary>A hash of every part's position and rotation, by their bits.</summary>
    static ulong Hash(ulong h, RagdollBody b)
    {
        void Add(float f) { unchecked { h ^= (uint)BitConverter.SingleToInt32Bits(f); h *= 1099511628211UL; } }
        foreach (var p in b.Parts)
        {
            var a = p.Rb.position;
            var q = p.Rb.rotation;
            Add(a.x); Add(a.y); Add(a.z); Add(q.x); Add(q.y); Add(q.z); Add(q.w);
        }
        return h;
    }

    /// <summary>Every transform under a character's hips, with its local values as bits.</summary>
    static Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)> Bones(CharaRealization r)
    {
        var d = new Dictionary<Transform, (Vector3, Quaternion, Vector3)>();
        foreach (var t in r.Context.BodyBones["cf_J_Hips"].GetComponentsInChildren<Transform>(true))
        {
            t.GetLocalPositionAndRotation(out var p, out var q);
            d[t] = (p, q, t.localScale);
        }
        return d;
    }

    static bool SameBits(in Vector3 a, in Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
    static bool SameBits(in Quaternion a, in Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;

    /// <summary>How many bones of a snapshot differ now in their bits, and the first of them.</summary>
    static int Differ(Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)> before, out string first)
    {
        int n = 0;
        first = "";
        foreach (var kv in before)
        {
            var t = kv.Key;
            if (!t) { n++; continue; }
            t.GetLocalPositionAndRotation(out var p, out var q);
            if (SameBits(p, kv.Value.p) && SameBits(q, kv.Value.r) && SameBits(t.localScale, kv.Value.s)) continue;
            if (n == 0) first = t.name + " (angle " + Deg(q, kv.Value.r).ToString("G4") + ", move " + (p - kv.Value.p).magnitude.ToString("G4") + ")";
            n++;
        }
        return n;
    }

    // ------------------------------------------------------------ play mode (RagdollTest.RunPlay)

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

    static string PlayUserData => "Spike/out/ragdoll/play_userdata";
    static string PlayLaunch =>
        "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":" + (Array.IndexOf(TestArgs.Get(), "-noUi") >= 0 ? "false" : "true") + ",\"language\":\"en\"},\"userData\":\"" + PlayUserData + "\",\"bridge\":{\"port\":" + (Arg("-port") ?? PlayPort.ToString()) +
        ",\"outputDir\":\"Spike/out/ragdoll/play_out\",\"allowQuit\":false}}";

    public static void RunPlay()
    {
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(PlayReport, "SUMMARY: 0 PASS, 1 FAIL\nRagdollTest.play: started, no result yet\n", new UTF8Encoding(false));
        PlayModeHarness.Start("RagdollTest.PlayMain", PlayReport, 25, true, PlayLaunch);
    }

    static IEnumerator PlayMain()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"RagdollTest.play (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, screen {Screen.width}x{Screen.height})");
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
        End(PlayReport, sw, "RagdollTest.RunPlay (play mode)");
        Debug.Log($"RAGDOLL PLAY TEST DONE: {passes} PASS, {fails} FAIL");
        PlayModeHarness.ExitCode = fails > 0 ? 1 : 0;
    }
}
