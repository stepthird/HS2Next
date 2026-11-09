// ScriptTest (Phase 6.9): scene scripts on the SFW targets of Tools/targets.json (BR-Chan in the female roles, HERO107 in the
// male roles), judged by numbers.
//   ScriptTest.Run      headless, edit mode (AppCore.ForTests hosts, frames pumped by hand); report Spike/out/script/script_report.txt
//   ScriptTest.RunPlay  play mode; the app is off unless a part starts it (PStartApp, port 17919 or -port); report Spike/out/script/script_play_report.txt
// The checks live in partial files of this class found by name: a static method named Headless_<part>() (no arguments) is a part of Run, Play_<part>() (an
// IEnumerator) a part of RunPlay; the parts run in ordinal order of their names and open their groups with Group(id, title, action) or POpen(id, title).
// -only JP1+JX2 runs those groups; -report <path> writes the report there. Each part owns its file and builds its own hosts. A headless world lives in a
// preview scene: RagdollWorld.MakeScene is set for the run and put back at its end.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HS2Next.Api.Bridge;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using HS2Next.Scenes.Scripts;
using HS2Next.Scenes.Timeline;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static partial class ScriptTest
{
    public const float Dt = 1f / 60f;
    /// <summary>The play row's port.</summary>
    public const int PlayPort = 17919;
    public const ulong DefaultSeed = 0x69a6d011UL;
    public static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "script");
    static string Report => Arg("-report") ?? Path.Combine(OutDir, "script_report.txt");
    static string PlayReport => Arg("-report") ?? Path.Combine(OutDir, "script_play_report.txt");

    static StringBuilder rep;
    static int passes, fails, warns;
    static string current = "";
    static readonly Dictionary<string, (int pass, int fail)> perGroup = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
    static readonly List<string> measurements = new List<string>();
    static string[] only;

    /// <summary>Catalog and Studio lists, built once by Run or by the first play part that asks (shared by every host; never disposed by a part).</summary>
    static RuntimeServices services;
    static TestTargets targets;
    /// <summary>The SFW visual targets: absolute card paths, and the paths relative to the HS2 card folders that chara.load's path takes with sex.</summary>
    static string femaleCard, maleCard, femaleRel, maleRel;

    // ------------------------------------------------------------ report

    static string Arg(string name)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    static bool HasArg(string name) => Array.IndexOf(TestArgs.Get(), name) >= 0;

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

    /// <summary>True when -only names at least one of the groups (or no -only was given): a part can skip its set-up.</summary>
    static bool WantAny(params string[] ids) => only == null || ids.Any(only.Contains);

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
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, rep.ToString(), new UTF8Encoding(false));
    }

    /// <summary>The static methods of this class whose names start with <paramref name="prefix"/>, in ordinal order of their names.</summary>
    static List<MethodInfo> Parts(string prefix, Type returns) =>
        typeof(ScriptTest).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(m => m.Name.StartsWith(prefix, StringComparison.Ordinal) && m.GetParameters().Length == 0 && m.ReturnType == returns)
            .OrderBy(m => m.Name, StringComparer.Ordinal).ToList();

    // ------------------------------------------------------------ headless (ScriptTest.Run)

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
            rep.AppendLine($"ScriptTest (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})");
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
        End(Report, sw, "ScriptTest.Run (headless)");
        HS2Next.Motion.Face.FaceSetup.Seed = null;
        services?.Catalog?.UnloadAll();
        Debug.Log($"SCRIPT TEST DONE: {passes} PASS, {fails} FAIL");
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

    // ------------------------------------------------------------ hosts (headless)

    sealed class Host
    {
        public AppCore App;
        public SceneRuntime Rt => App.Runtime;
        public SceneSession Session => App.Session;
        public string LastText;

        public JObject Call(object request, int maxFrames = 600)
        {
            string json = request is string s ? s : JsonConvert.SerializeObject(request);
            LastText = App.Call(json, maxFrames);
            return JObject.Parse(LastText);
        }

        /// <summary>Calls and checks ok: true (a FAIL line with the reply otherwise); returns the reply.</summary>
        public JObject Ok(object request, string what, int maxFrames = 600)
        {
            var r = Call(request, maxFrames);
            if (r.Value<bool?>("ok") != true) Check(what, false, Short(LastText));
            return r;
        }

        public void Frames(int n, float dt = Dt)
        {
            for (int i = 0; i < n; i++) App.Frame(dt);
        }

        public void Dispose() { try { App?.Dispose(); } catch (Exception e) { Info("host dispose threw: " + e.Message); } }
    }

    /// <summary>The headless hosts' UserData (the script store lives under it) and output folder.</summary>
    static string UserDataDir => Path.Combine(OutDir, "userdata");
    static string OutputDir => Path.Combine(OutDir, "out");

    /// <summary>A fresh test host: counting ids; the parts' frames step every built character with AutoStep.</summary>
    static Host NewHost(ulong seed = DefaultSeed, Action<AppSettings> settings = null)
    {
        HS2Next.Motion.Face.FaceSetup.Seed = 0x5eed;
        var s = AppSettings.Defaults(Array.Empty<string>());
        s.Content = CardResolveTest.Settings;
        s.UserData = UserDataDir;
        s.Bridge.OutputDir = OutputDir;
        settings?.Invoke(s);
        var o = services.NewSessionOptions();
        o.Ids = new CountingIdSource(seed);
        return new Host { App = AppCore.ForTests(services, s, null, o) };
    }

    /// <summary>The node id of an add reply: its key, or its first added node's key (chara.load, item.add).</summary>
    static string Key(JObject r) => (string)r?["key"] ?? (string)r?["added"]?[0]?["key"];

    /// <summary>A target loaded with chara.load; physics off unless asked.</summary>
    static string LoadChara(Host h, string rel, string sex, string what, bool physics = false)
    {
        var k = Key(h.Ok(new { cmd = "chara.load", path = rel, sex }, what));
        if (!physics && k != null) h.Ok(new { cmd = "chara.motion", key = k, physics = false }, what + ": physics off");
        return k;
    }

    // ------------------------------------------------------------ play mode (ScriptTest.RunPlay)

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

    static string PlayUserData => "Spike/out/script/play_userdata";

    /// <summary>The app's launch settings of a play part: the row's port (or -port), the play UserData and output folder, UI per -noUi.</summary>
    static string PlayLaunch =>
        "{\"app\":{\"autoStart\":true},\"ui\":{\"enabled\":" + (HasArg("-noUi") ? "false" : "true") + ",\"language\":\"en\"},\"userData\":\"" + PlayUserData + "\",\"bridge\":{\"port\":" + (Arg("-port") ?? PlayPort.ToString()) +
        ",\"outputDir\":\"Spike/out/script/play_out\",\"allowQuit\":false}}";

    /// <summary>The app a play part started (null when none runs).</summary>
    static HS2NextApp pApp;

    /// <summary>Starts the app for a play part (as the play session's automatic start would) and waits until it is ready; a FAIL line when it is not within the time.</summary>
    static IEnumerator PStartApp(double timeoutSec = 180)
    {
        if (pApp != null) yield break;
        var args = Environment.GetCommandLineArgs();
        AppSettings s;
        try { s = AppSettings.Load(args, PlayLaunch); }
        catch (Exception e) { Check("the app's settings", false, e.Message); s = AppSettings.Defaults(args, PlayLaunch); }
        pApp = AppBootstrap.Start(s);
        float until = Time.realtimeSinceStartup + (float)timeoutSec;
        while (Time.realtimeSinceStartup < until && (AppCore.Current == null || AppCore.Current.Stage != AppStage.Ready)) yield return null;
        if (AppCore.Current == null || AppCore.Current.Stage != AppStage.Ready) Check("the app started", false, "stage " + (AppCore.Current?.StageName ?? "none") + " after " + timeoutSec + " s");
    }

    /// <summary>Stops the app a play part started (the object destroyed, its host disposed) and waits a few frames.</summary>
    static IEnumerator PStopApp()
    {
        if (pApp != null) UnityEngine.Object.Destroy(pApp.gameObject);
        pApp = null;
        yield return PFrames(3);
    }

    /// <summary>The catalog and the targets for a play part that builds its own hosts (built once per session).</summary>
    static void PlayServices()
    {
        if (services != null) return;
        services = SceneRuntimeTest.NewServices(new StringBuilder());
    }

    public static void RunPlay()
    {
        Directory.CreateDirectory(OutDir);
        var dir = Path.GetDirectoryName(PlayReport);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(PlayReport, "SUMMARY: 0 PASS, 1 FAIL\nScriptTest.play: started, no result yet\n", new UTF8Encoding(false));
        PlayModeHarness.Start("ScriptTest.PlayMain", PlayReport, 40, true, AppLaunch.Off);
    }

    static IEnumerator PlayMain()
    {
        Begin();
        var sw = Stopwatch.StartNew();
        rep.AppendLine($"ScriptTest.play (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, screen {Screen.width}x{Screen.height})");
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
            if (pApp != null) yield return PStopApp();   // a part that left the app running: stopped before the next part
        }
        End(PlayReport, sw, "ScriptTest.RunPlay (play mode)");
        services?.Catalog?.UnloadAll();
        Debug.Log($"SCRIPT PLAY TEST DONE: {passes} PASS, {fails} FAIL");
        PlayModeHarness.ExitCode = fails > 0 ? 1 : 0;
    }
}

// ScriptTest, package P's headless part (Phase 6.9): the compile (JM4), the preparation's entry (JE2), entries of replays and set-up
// (JE3), a cast node deleted during play (JB2), the order of a play (JP1), the casting order (JP2), "once" (JP3), the table (JP4),
// script.capture (JP5), timed commands through a script (JP6), the animation restart (JP7). The checks that need the script commands' slots
// (JB1, JB3, JP8) and the build form (JM5) are in this file too and run when their parts exist. Scripts are added with ScriptEditCommand and
// played with ScriptPlayer (the commands of package C call the same). Targets: BR-Chan in female roles, HERO107 in male roles.
public static partial class ScriptTest
{
    static void Headless_P_Player()
    {
        Group("JM4", "compile: role keys and focus nodes replaced, ids kept, compiled = the hash without the marker", JM4);
        Group("JM5", "build-form tracks stored equal to timeline.build's stored form of the same input", JM5);
        Group("JP1", "the order of a play and the reply's members", JP1);
        Group("JP2", "the casting order; uncast refused, partial skips and counts", JP2);
        Group("JP3", "once: the preparation's mark", JP3);
        Group("JP4", "the table of 4.3, row by row", JP4);
        Group("JP5", "script.capture: keys and requests back to roles, compiled renewed", JP5);
        Group("JP6", "timed commands of a script fire on crossings only", JP6);
        Group("JP7", "the animation restart of a play from 0", JP7);
        Group("JP7c", "the restart resets the cast's dynamic chains; a seek, a loop pass, a pause and a resume do not", JP7c);
        Group("JE2", "the preparation is one entry «Prepare script» whose undo removes what it made", JE2);
        Group("JE3", "a replay that changes nothing adds no entry; set-up adds none", JE3);
        Group("JB2", "a cast node deleted during play; undo of the delete restores the role", JB2);
        Group("JB1", "a cast character rebuilt during play keeps its Timeline slots, its move and the follow", JB1);
        Group("JB3", "scene new and load release every script slot", JB3);
        Group("JP8", "stop: the Timeline stopped, the slots released; with reset the bodies released (the stains: the play row's JP8s)", JP8);
    }

    // ------------------------------------------------------------ fixture

    /// <summary>A host with BR-Chan (a), HERO107 (b) at (5, 0, 0) and item 399 (p); rows at speed 0.</summary>
    sealed class SFix
    {
        public Host H;
        public string A, B, P;
        public TimelinePlayback Tl => H.Rt.GetPart<TimelinePlayback>();
        public ScriptPlayback Sp => H.Rt.GetPart<ScriptPlayback>();
        public SceneModel Model => H.Session.Model;
        public void Dispose() => H?.Dispose();
    }

    static SFix NewSFix(ulong seed, bool item = true)
    {
        var f = new SFix { H = NewHost(seed) };
        var h = f.H;
        f.A = LoadChara(h, femaleRel, "female", "fixture: chara.load BR-Chan");
        f.B = LoadChara(h, maleRel, "male", "fixture: chara.load HERO107");
        if (item) f.P = Key(h.Ok(new { cmd = "item.add", no = 399 }, "fixture: item.add 399"));
        if (f.A == null || f.B == null || (item && f.P == null)) throw new InvalidOperationException("the fixture's nodes were not added");
        h.Ok(new { cmd = "obj.transform", key = f.B, pos = new[] { 5, 0, 0 } }, "fixture: HERO107 at (5, 0, 0)");
        h.Ok(new { cmd = "chara.anime", key = f.A, group = 19, category = 0, no = 0, speed = 0 }, "fixture: BR-Chan's row");
        h.Ok(new { cmd = "chara.anime", key = f.B, group = 11, category = 5, no = 0, speed = 0 }, "fixture: HERO107's row");
        h.Frames(3);
        return f;
    }

    static ScriptRole CharaRole(string role, string sex, string hintName = null, string card = null) =>
        ScriptRole.Chara(role, sex, hintName != null || card != null ? new ScriptHint(hintName, card) : null);

    static SceneScript NewScript(string name, params ScriptRole[] roles) =>
        new SceneScript { Id = JsonText.NewId(), Name = name, Roles = roles.ToList(), Timeline = TimelineDocument.NewScriptTimeline() };

    static JObject J(string json) => JObject.Parse(json);

    /// <summary>A script timeline with one key per track; <paramref name="tracks"/> as (type, key or null, bone, slot, key value).</summary>
    static TimelineDocument ScriptTimeline(float duration, bool loop, params TimelineTrack[] tracks)
    {
        var d = TimelineDocument.NewScriptTimeline();
        d.Duration = duration;
        d.Loop = loop;
        d.Tracks.AddRange(tracks);
        return d;
    }

    static TimelineTrack Track(string type, string key, TimelineKey k, string bone = null, int? slot = null) =>
        new TimelineTrack(JsonText.NewId(), type, key) { Bone = bone, Slot = slot, Keys = new List<TimelineKey> { k } };

    static TimelineEvent Event(float t, string doJson) => new TimelineEvent(JsonText.NewId(), t, J(doJson));

    /// <summary>Adds scripts to the scene (one entry «Add script»).</summary>
    static void AddScripts(Host h, params SceneScript[] scripts)
    {
        var l = new SceneScriptList();
        if (h.Session.Model.Scripts != null) l.Scripts.AddRange(h.Session.Model.Scripts.Scripts);
        l.Scripts.AddRange(scripts);
        var r = h.Session.Commands.Execute(ScriptEditCommand.ReplaceScripts(l, ScriptEditCommand.LabelAdd));
        if (r.Status == CommandStatus.Error) throw new InvalidOperationException("the scripts were not added: " + r.Code + ": " + r.Message);
        h.Rt.ApplyNow();
    }

    /// <summary>Replaces one scene script by a changed clone (no history).</summary>
    static void EditScript(Host h, string id, Action<SceneScript> change)
    {
        var l = new SceneScriptList();
        foreach (var s in h.Session.Model.Scripts.Scripts)
        {
            if (s.Id != id) { l.Scripts.Add(s); continue; }
            var c = s.Clone();
            change(c);
            l.Scripts.Add(c);
        }
        var r = h.Session.Commands.Execute(ScriptEditCommand.ReplaceScripts(l, ScriptEditCommand.LabelEdit), ExecuteOptions.NotRecorded);
        if (r.Status == CommandStatus.Error) throw new InvalidOperationException("the script was not changed: " + r.Code + ": " + r.Message);
    }

    static SceneScript ScriptOf(Host h, string id) => h.Session.Model.Scripts?.Find(id);

    /// <summary>Plays a scene script with the player and pumps frames until the run is done (bounded); null when a refusal threw (in <paramref name="refusal"/>).</summary>
    static ScriptPlayResult PlayScript(Host h, string id, ScriptPlayOptions o, out BridgeError refusal, int maxFrames = 600)
    {
        refusal = null;
        ScriptPlayRun run;
        try { run = ScriptPlayer.Play(h.App, id, o ?? new ScriptPlayOptions()); }
        catch (BridgeException e) { refusal = e.Error; return null; }
        for (int i = 0; i < maxFrames && !run.IsDone; i++) h.App.Frame(Dt);
        if (!run.IsDone) { refusal = new BridgeError("timeout", "the play was not done within " + maxFrames + " frames"); return null; }
        return run.Result;
    }

    static ScriptPlayResult PlayOk(Host h, string id, ScriptPlayOptions o, string what)
    {
        var r = PlayScript(h, id, o, out var refusal);
        if (refusal != null) { Check(what + ": played", false, refusal.ToString()); return null; }
        if (!r.Ok) Check(what + ": played", false, r.Error.ToString());
        return r.Ok ? r : null;
    }

    /// <summary>The undo entries' labels, newest first (as CommandBus.UndoList lists them).</summary>
    static List<string> Undo(Host h) => h.Session.Commands.UndoList.Select(e => e.Label).ToList();

    /// <summary>The labels of the entries added since the undo list held <paramref name="before"/> entries: the newest top N, oldest of them first.</summary>
    static List<string> Added(Host h, int before)
    {
        var l = Undo(h);
        return l.Take(Math.Max(0, l.Count - before)).Reverse().ToList();
    }

    static string Labels(IEnumerable<string> l) => "[" + string.Join(", ", l) + "]";

    static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------ JM4

    static readonly string[] AllTrackTypes = { "camera", "dof", "pos", "rot", "scale", "anime", "animeSpeed", "animeTime", "eyes", "mouth", "eyesOpen", "mouthOpen", "color" };

    /// <summary>A script timeline with a track of every type keyed by roles, a dof focus on "$a", and timed commands naming roles at depth.</summary>
    static TimelineDocument EveryTrackTimeline()
    {
        var d = ScriptTimeline(12f, true,
            Track("camera", null, TimelineKey.OfCamera(0f, new TimelineCamera(new Vec3(0, 10, 0), new Vec3(10, 180, 0), 25, 23))),
            Track("dof", null, TimelineKey.OfDof(0f, new TimelineDof { On = true, Focus = TimelineFocus.OfNode("$a", "cf_J_Head", new Vec3(0, 0.5f, 0)), Size = 0.2f, Aperture = 0.5f })),
            Track("pos", "$a", TimelineKey.OfVector(0f, new Vec3(1, 0, 2))),
            Track("rot", "$a", TimelineKey.OfVector(0f, new Vec3(0, 90, 0))),
            Track("rot", "$a", TimelineKey.OfVector(0f, new Vec3(0, 20, 0)), "cf_J_Neck"),
            Track("scale", "$p", TimelineKey.OfVector(0f, new Vec3(1, 1, 1))),
            Track("anime", "$b", TimelineKey.OfAnime(0f, new AnimeRef(DocDefaults.VanillaSource, 11, 5, 0))),
            Track("animeSpeed", "$b", TimelineKey.OfNumber(0f, 1f)),
            Track("animeTime", "$a", TimelineKey.OfNumber(0f, 0.25f)),
            Track("eyes", "$a", TimelineKey.OfInteger(0f, 1)),
            Track("mouth", "$a", TimelineKey.OfInteger(0f, 2)),
            Track("eyesOpen", "$a", TimelineKey.OfNumber(0f, 0.5f)),
            Track("mouthOpen", "$a", TimelineKey.OfNumber(0f, 0.25f)),
            Track("color", "$p", TimelineKey.OfColor(0f, new Color4(1, 0, 0, 1)), null, 0));
        d.Events.Add(Event(0.5f, "{\"cmd\":\"obj.transform\",\"key\":\"$b\",\"pos\":[5,0,1]}"));
        d.Events.Add(Event(1.0f, "{\"cmd\":\"wet.attach\",\"key\":\"$a\",\"from\":{\"key\":\"$b\",\"bone\":\"cf_J_Hand_R\",\"offset\":[0,0,\"$$lit\"]},\"preset\":\"water\"}"));
        d.Events.Add(Event(2.0f, "{\"cmd\":\"obj.transform\",\"key\":\"$x\",\"pos\":[0,0,0]}"));
        return d;
    }

    static void JM4()
    {
        const string A = "0123456789abcdef0123456789abcde1", B = "0123456789abcdef0123456789abcde2", P = "0123456789abcdef0123456789abcde3";
        var s = NewScript("every track", CharaRole("a", "female"), CharaRole("b", "male"), ScriptRole.Item("p", "illusion.hs2#399"));
        s.Timeline = EveryTrackTimeline();
        var cast = new Dictionary<string, string> { ["a"] = A, ["b"] = B, ["p"] = P };
        var r = ScriptCompiler.Compile(s, cast);
        var d = r.Timeline;
        var types = d.Tracks.Select(t => t.Type).Distinct().ToList();
        var missing = AllTrackTypes.Where(t => !types.Contains(t)).ToList();
        Check("JM4 Present: a track of every type compiled (" + types.Count + " types, " + d.Tracks.Count + " tracks)", missing.Count == 0, "missing " + string.Join(", ", missing));
        Info("JM4 Present: types " + string.Join(", ", types) + "; keys replaced " + r.KeysReplaced + ", request strings replaced " + r.RequestsReplaced);
        var dollar = d.Tracks.Where(t => t.Key != null && t.Key.StartsWith("$", StringComparison.Ordinal)).Select(t => t.Describe()).ToList();
        Check("JM4: no track key names a role after the compile", dollar.Count == 0, string.Join("; ", dollar));
        Check("JM4: track keys are the cast's nodes", d.Tracks.Where(t => t.Key != null).All(t => t.Key == A || t.Key == B || t.Key == P));
        var dof = d.Tracks.First(t => t.Type == "dof").Keys[0].Dof.Focus;
        Check("JM4: the dof key's focus node is a's node, bone and offset kept", dof != null && dof.Node == A && dof.Bone == "cf_J_Head" && dof.Offset.HasValue && dof.Offset.Value.Y == 0.5f, dof?.Node);
        Check("JM4: 12 track keys and 1 focus node replaced (13)", r.KeysReplaced == 13, "" + r.KeysReplaced);
        var srcIds = s.Timeline.Tracks.Select(t => t.Id).ToList();
        Check("JM4: track ids kept, in order", d.Tracks.Select(t => t.Id).SequenceEqual(srcIds));
        var evIds = s.Timeline.Events.Take(2).Select(e => e.Id).ToList();
        Check("JM4: event ids kept (the two that name cast roles)", d.Events.Select(e => e.Id).SequenceEqual(evIds), string.Join(",", d.Events.Select(e => e.Id)));
        Check("JM4: duration, loop, autoplay copied", d.Duration == 12f && d.Loop && !d.Autoplay);
        Check("JM4: requests substituted at depth (key, from.key) and the escape kept as \"$lit\"",
            (string)d.Events[0].Do["key"] == B && (string)d.Events[1].Do["key"] == A && (string)d.Events[1].Do["from"]["key"] == B && (string)d.Events[1].Do["from"]["offset"][2] == "$lit",
            d.Events.Count > 1 ? d.Events[1].Do.ToString(Formatting.None) : "");
        Check("JM4: the event naming an undeclared role is left out and counted (1), \"x\" reported", r.EventsSkipped == 1 && r.UnknownRoles.SequenceEqual(new[] { "x" }), r.EventsSkipped + " " + string.Join(",", r.UnknownRoles));
        Check("JM4: the marker names the script", d.Script != null && d.Script.Id == s.Id && d.Script.Compiled == r.Compiled);
        var o = d.ToJObject();
        o.Remove(TimelineScriptMarker.MemberName);
        string own = ScriptText.Hash16(CanonicalJsonWriter.ToText(o));
        Check("JM4: compiled = the first 16 hex of SHA-256 of the canonical JSON without the marker (computed apart)", r.Compiled == own && d.CompiledHash() == own, r.Compiled + " / " + own);
        Check("JM4: an unedited compile reads as unedited", !d.IsEditedSinceLoad);
        int keyedA = 0, ofBP = 0;
        foreach (var t in s.Timeline.ToJObject()["tracks"])
        {
            string k = (string)t["key"];
            if (k == "$a") keyedA++;
            if (k == "$b" || k == "$p") ofBP++;
        }
        int compiledA = d.Tracks.Count(t => t.Key == A);
        Check("JM4: the compile left the script's timeline as it was: tracks keyed \"$a\" in the script " + s.Timeline.Tracks.Count(t => t.Key == "$a") + " = counted in the fixture document " + keyedA +
            "; compiled onto a's node " + compiledA, s.Timeline.Tracks.Count(t => t.Key == "$a") == keyedA && compiledA == keyedA && keyedA > 0);
        Check("JM4: the reader's findings on the compiled document: none", r.Findings.Count == 0, string.Join("; ", r.Findings.Select(x => x.Code + " " + x.Message)));
        // an uncast role: its tracks and focus keys are left out
        var partial = ScriptCompiler.Compile(s, new Dictionary<string, string> { ["a"] = A });
        Check("JM4: with b and p uncast their tracks are left out: compiled " + partial.TracksSkipped + " = counted in the fixture document " + ofBP + "; events left out 3 (" + partial.EventsSkipped + ")",
            ofBP > 0 && partial.TracksSkipped == ofBP && partial.EventsSkipped == 3 && partial.UncastRoles.Contains("b") && partial.UncastRoles.Contains("p"),
            "uncast " + string.Join(",", partial.UncastRoles));
        var noA = ScriptCompiler.Compile(s, new Dictionary<string, string> { ["b"] = B, ["p"] = P });
        Check("JM4: with a uncast the dof key focused on a is left out and its track with it", noA.KeysSkipped == 1 && !noA.Timeline.Tracks.Any(t => t.Type == "dof"), "keys " + noA.KeysSkipped);
    }


    // ------------------------------------------------------------ JM5

    /// <summary>Build-form tracks of every type in timeline.build's vocabulary, keyed by "$a" (BR-Chan), "$b" (HERO107) and "$p" (item 399).</summary>
    const string BuildTracks = @"[
      {""type"":""camera"",""keys"":[{""t"":0,""target"":[0,10,0],""rot"":[10,180,0],""dist"":25,""fov"":23},{""t"":2,""dist"":20}]},
      {""type"":""dof"",""keys"":[{""t"":0,""on"":true,""focus"":""$a"",""sharp"":0.2,""full"":4},{""t"":1.5,""focus"":{""key"":""$b"",""bone"":""cf_J_Head"",""offset"":[0,0.5,0]},""blur"":2}]},
      {""type"":""pos"",""key"":""$a"",""keys"":[{""t"":0,""v"":[0,0,0]},{""t"":2,""v"":[0,0,3],""ease"":""linear""}]},
      {""type"":""rot"",""key"":""$a"",""keys"":[{""t"":0,""v"":[0,0,0]},{""t"":1,""v"":[0,90,0]}]},
      {""type"":""rot"",""key"":""$b"",""bone"":""cf_J_Neck"",""keys"":[{""t"":0,""v"":[0,0,0]},{""t"":1,""v"":[0,20,0]}]},
      {""type"":""scale"",""key"":""$p"",""keys"":[{""t"":0,""v"":[1,1,1]},{""t"":1,""v"":[2,2,2]}]},
      {""type"":""anime"",""key"":""$b"",""keys"":[{""t"":0,""group"":11,""category"":5,""no"":0},{""t"":1.25,""group"":19,""category"":0,""no"":0}]},
      {""type"":""animeSpeed"",""key"":""$b"",""keys"":[{""t"":0,""v"":1},{""t"":1,""v"":0.5}]},
      {""type"":""animeTime"",""key"":""$a"",""keys"":[{""t"":0,""v"":0.1}]},
      {""type"":""eyes"",""key"":""$a"",""raw"":true,""keys"":[{""t"":0,""v"":1},{""t"":1,""v"":2}]},
      {""type"":""mouth"",""key"":""$a"",""keys"":[{""t"":0,""v"":3}]},
      {""type"":""eyesOpen"",""key"":""$a"",""keys"":[{""t"":0,""v"":1},{""t"":0.5,""v"":0.2}]},
      {""type"":""mouthOpen"",""key"":""$a"",""keys"":[{""t"":0,""v"":0.3}]},
      {""type"":""color"",""key"":""$p"",""slot"":0,""keys"":[{""t"":0,""v"":[1,0,0,1]}]}
    ]";

    /// <summary>A stored track as JSON with its id blanked.</summary>
    static string Blank(TimelineTrack t)
    {
        var o = JObject.Parse(t.ToJson());
        o["id"] = "-";
        return o.ToString(Formatting.None);
    }

    static void JM5()
    {
        var f = NewSFix(0x69a50005UL);
        try
        {
            var h = f.H;
            var db = h.Rt.Services.Db;
            var build = new ScriptBuildContext
            {
                Ids = new CountingIdSource(0x5c11a5UL),
                KindOfNode = id => f.Model.Find(id)?.Kind,
                ResolveAnime = (key, arg) =>
                {
                    int g = (int?)key["group"] ?? 0, c = (int?)key["category"] ?? 0, n = (int?)key["no"] ?? 0;
                    var row = db.FindAnime(g, c, n);
                    return row == null ? null : new AnimeRef(row.Guid, row.Group, row.Category, row.No);
                },
            };
            var doc = new JObject
            {
                ["format"] = SceneScript.FormatName, ["version"] = 1, ["id"] = JsonText.NewId(), ["name"] = "build form",
                ["roles"] = JArray.Parse("[{\"role\":\"a\",\"kind\":\"chara\",\"sex\":\"female\"},{\"role\":\"b\",\"kind\":\"chara\",\"sex\":\"male\"},{\"role\":\"p\",\"kind\":\"item\",\"ref\":\"" + ScriptRoles.RefOf(f.Model.Find(f.P)) + "\"}]"),
                ["timeline"] = new JObject { ["duration"] = 3, ["loop"] = true, ["tracks"] = JArray.Parse(BuildTracks), ["events"] = JArray.Parse("[{\"t\":0.5,\"do\":{\"cmd\":\"obj.transform\",\"key\":\"$b\",\"pos\":[5,0,1]}}]") },
            };
            Check("JM5: the document holds build-form tracks and events", ScriptFormat.HasBuildForm(doc) && ScriptFormat.BuildFormCount(doc) == 15, "" + ScriptFormat.BuildFormCount(doc));
            var findings = new FindingList();
            var script = ScriptFormat.FromToken(doc, findings, build);
            var types = script.Timeline.Tracks.Select(t => t.Type).Distinct().ToList();
            Check("JM5 Present: the build input has every track type (" + types.Count + " of 13)", AllTrackTypes.All(types.Contains), "missing " + string.Join(", ", AllTrackTypes.Where(x => !types.Contains(x))));
            Check("JM5: stored with role keys", script.Timeline.RoleKeys && script.Timeline.Tracks.Count(t => t.Key == "$a") == 7 && script.Timeline.Tracks.First(t => t.Type == "dof").Keys[0].Dof.Focus.Node == "$a",
                string.Join(",", script.Timeline.Tracks.Select(t => t.Key)));
            var compiled = ScriptCompiler.Compile(script, new Dictionary<string, string> { ["a"] = f.A, ["b"] = f.B, ["p"] = f.P }).Timeline;
            // timeline.build of the same input with the node ids
            string withIds = BuildTracks.Replace("\"$a\"", "\"" + f.A + "\"").Replace("\"$b\"", "\"" + f.B + "\"").Replace("\"$p\"", "\"" + f.P + "\"");
            h.Ok(new JObject { ["cmd"] = "timeline.build", ["duration"] = 3, ["loop"] = true, ["tracks"] = JArray.Parse(withIds) }.ToString(Formatting.None), "JM5: timeline.build of the same input");
            var built = h.Session.Model.Timeline;
            int same = 0;
            var diffs = new List<string>();
            for (int i = 0; i < Math.Min(compiled.Tracks.Count, built.Tracks.Count); i++)
            {
                string x = Blank(compiled.Tracks[i]), y = Blank(built.Tracks[i]);
                if (x == y) same++;
                else diffs.Add(i + ": " + Short(x) + " / " + Short(y));
            }
            Check("JM5: every stored track equals timeline.build's (" + same + " of " + built.Tracks.Count + ", ids aside)", compiled.Tracks.Count == built.Tracks.Count && same == built.Tracks.Count, string.Join(" | ", diffs.Take(3)));
            Check("JM5: duration and loop as built", compiled.Duration == built.Duration && compiled.Loop == built.Loop);
            Check("JM5: the event stored with an id and its role", script.Timeline.Events.Count == 1 && JsonText.IsId(script.Timeline.Events[0].Id) && (string)script.Timeline.Events[0].Do["key"] == "$b");
            Info("JM5: build warnings " + build.Warnings.Count + (build.Warnings.Count > 0 ? ": " + string.Join("; ", build.Warnings) : "") + "; findings " + findings.Count);
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ JP1

    static void JP1()
    {
        var f = NewSFix(0x69a10001UL, item: false);
        try
        {
            var h = f.H;
            var s = NewScript("order", CharaRole("a", "female"), CharaRole("b", "male"));
            s.Cast["a"] = f.A;
            s.Cast["b"] = f.B;
            s.Prepare.Add(J("{\"cmd\":\"obj.transform\",\"key\":\"$a\",\"pos\":[1,0,0]}"));
            s.Setup.Add(J("{\"cmd\":\"obj.transform\",\"key\":\"$a\",\"pos\":[2,0,0]}"));
            s.Timeline = ScriptTimeline(4f, true, Track("pos", "$b", TimelineKey.OfVector(0f, new Vec3(6, 0, 0))));
            AddScripts(h, s);
            var r = PlayOk(h, s.Id, null, "JP1");
            if (r == null) return;
            Check("JP1: cast, preparation, set-up, load, play in that order", r.Order.SequenceEqual(new[] { "cast", "prepare", "setup", "load", "play" }), string.Join(", ", r.Order));
            var reply = r.ToReply();
            var names = reply.Properties().Select(p => p.Name).ToList();
            var want = new[] { "script", "roles", "loaded", "prepared", "setup", "prepare", "skipped", "failures", "state", "time" };
            Check("JP1: the reply's members in order", names.SequenceEqual(want), string.Join(", ", names));
            Check("JP1: setup {sent, failed}, prepare {sent, failed}", ((JObject)reply["setup"]).Properties().Select(p => p.Name).SequenceEqual(new[] { "sent", "failed" }) &&
                ((JObject)reply["prepare"]).Properties().Select(p => p.Name).SequenceEqual(new[] { "sent", "failed" }));
            Check("JP1: values: script id, roles, loaded, prepared, 1 + 1 sent, 0 failed, playing at 0",
                (string)reply["script"] == s.Id && (string)reply["roles"]["a"] == f.A && (string)reply["roles"]["b"] == f.B && (bool)reply["loaded"] && (bool)reply["prepared"] &&
                (int)reply["setup"]["sent"] == 1 && (int)reply["prepare"]["sent"] == 1 && (int)reply["setup"]["failed"] == 0 && (int)reply["prepare"]["failed"] == 0 &&
                (string)reply["state"] == "playing" && (double)reply["time"] == 0, reply.ToString(Formatting.None));
            h.Frames(2);
            var pos = f.Model.Find(f.A).Doc.Transform.Pos;
            Check("JP1: the set-up ran after the preparation (a at x 2 in the model)", pos.X == 2f, "x " + pos.X);
            Check("JP1: the Timeline plays the loaded script", f.Tl.Player.State == TimelinePlayState.Playing && h.Session.Model.Timeline?.Script?.Id == s.Id);
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ JP2

    static void JP2()
    {
        var f = NewSFix(0x69a20001UL);
        try
        {
            var h = f.H;
            string bName = f.Model.Find(f.B).Label;
            string pRef = ScriptRoles.RefOf(f.Model.Find(f.P));
            // a fourth node: an item under a's character, the row of p
            var q = Key(h.Ok(new { cmd = "item.add", no = 399, parent = f.A, point = "N_Hand_R" }, "JP2: an item under a"));
            var s = NewScript("casting",
                CharaRole("a", "female"),                 // (1) the cast entry
                CharaRole("b", "male", bName),            // (2) the hint's name
                CharaRole("c", "female", null, femaleRel),// (3) the hint's card with load
                new ScriptRole("p", ScriptRole.KindItem) { Ref = pRef },                                            // (2) row, scene root
                new ScriptRole("q", ScriptRole.KindItem) { Ref = pRef, Parent = new ScriptRoleParent("a", "N_Hand_R") }, // (2) row, under a
                new ScriptRole("r", ScriptRole.KindItem) { Ref = pRef, Parent = new ScriptRoleParent("c", "48") });       // (3) added under c
            s.Cast["a"] = f.A;
            s.Timeline = ScriptTimeline(2f, true);
            AddScripts(h, s);
            int nodesBefore = f.Model.PreOrder().Count();
            var r = PlayOk(h, s.Id, null, "JP2");
            if (r == null) return;
            Check("JP2: a by its cast entry", r.CastBy.TryGetValue("a", out var ba) && ba == "cast" && r.Roles["a"] == f.A, ba);
            Check("JP2: b by the hint's name (" + bName + ")", r.CastBy.TryGetValue("b", out var bb) && bb == "found" && r.Roles["b"] == f.B, bb);
            Check("JP2: c loaded from the hint's card", r.CastBy.TryGetValue("c", out var bc) && bc == "loaded" && f.Model.Find(r.Roles["c"])?.Kind == NodeKind.Chara, bc);
            Check("JP2: p by its row at the scene root", r.CastBy.TryGetValue("p", out var bp) && bp == "found" && r.Roles["p"] == f.P, bp);
            Check("JP2: q by its row under a", r.CastBy.TryGetValue("q", out var bq) && bq == "found" && r.Roles["q"] == q, bq);
            string rNode = r.Roles.TryGetValue("r", out var rn) ? rn : null;
            var rN = rNode != null ? f.Model.Find(rNode) : null;
            Check("JP2: r added and parented under c", r.CastBy.TryGetValue("r", out var br) && br == "loaded" && rN != null && rN.Parent?.Id == r.Roles["c"], br + " parent " + rN?.Parent?.Id);
            Check("JP2 Present: every path resolved a role (cast 1, found 3, loaded 2)", r.CastBy.Values.Count(v => v == "cast") == 1 && r.CastBy.Values.Count(v => v == "found") == 3 && r.CastBy.Values.Count(v => v == "loaded") == 2,
                string.Join(", ", r.CastBy.Select(kv => kv.Key + ":" + kv.Value)));
            Check("JP2: the casts found and loaded are written into the scene script", ScriptOf(h, s.Id).Cast.Count == 6, string.Join(",", ScriptOf(h, s.Id).Cast.Keys));
            Check("JP2: two nodes added (c, r)", f.Model.PreOrder().Count() == nodesBefore + 2, "" + (f.Model.PreOrder().Count() - nodesBefore));
            // uncast refused, partial skips
            var u = NewScript("uncast", CharaRole("a", "female"), CharaRole("d", "male"));
            u.Cast["a"] = f.A;
            u.Setup.Add(J("{\"cmd\":\"obj.transform\",\"key\":\"$d\",\"pos\":[0,0,0]}"));
            u.Setup.Add(J("{\"cmd\":\"obj.transform\",\"key\":\"$a\",\"pos\":[0,0,1]}"));
            u.Timeline = ScriptTimeline(2f, true, Track("pos", "$d", TimelineKey.OfVector(0f, new Vec3(0, 0, 0))));
            AddScripts(h, u);
            var refused = PlayScript(h, u.Id, null, out var refusal);
            var err = refusal ?? refused?.Error;
            var detail = err?.Detail?.Extra != null && err.Detail.Extra.TryGetValue("uncast", out var uc) ? uc as string[] : null;
            Check("JP2: an uncast role refuses the play: bad_args, detail {uncast: [d]}", err != null && err.Code == BridgeCodes.BadArgs && detail != null && detail.SequenceEqual(new[] { "d" }), err?.ToString());
            var p = PlayOk(h, u.Id, new ScriptPlayOptions { Partial = true }, "JP2 partial");
            if (p != null)
                Check("JP2: partial: the step and the track that name d are skipped and counted (2), the other step sent", p.Skipped == 2 && p.SetupSent == 1 && p.Warnings.Any(w => w.Contains("role d")),
                    "skipped " + p.Skipped + ", sent " + p.SetupSent + "; " + string.Join("; ", p.Warnings));
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ JP3

    static void JP3()
    {
        var f = NewSFix(0x69a30001UL, item: false);
        try
        {
            var h = f.H;
            var s = NewScript("once", CharaRole("a", "female"));
            s.Cast["a"] = f.A;
            s.Prepare.Add(J("{\"cmd\":\"obj.transform\",\"key\":\"$a\",\"rot\":[0,30,0]}"));
            s.Timeline = ScriptTimeline(2f, true);
            AddScripts(h, s);
            var r1 = PlayOk(h, s.Id, null, "JP3 first");
            string mark1 = ScriptOf(h, s.Id).Prepared;
            Check("JP3 Present: the first play prepared and wrote the mark", r1 != null && r1.Prepared && r1.PrepareSent == 1 && ScriptText.IsHash16(mark1), mark1);
            var r2 = PlayOk(h, s.Id, null, "JP3 second");
            Check("JP3: a second play does not prepare", r2 != null && !r2.Prepared && r2.PrepareSent == 0);
            EditScript(h, s.Id, c => c.Cast["a"] = f.B);
            var r3 = PlayOk(h, s.Id, null, "JP3 recast");
            string mark3 = ScriptOf(h, s.Id).Prepared;
            Check("JP3: a recast prepares again and writes another mark", r3 != null && r3.Prepared && mark3 != mark1 && ScriptText.IsHash16(mark3), mark1 + " -> " + mark3);
            var r4 = PlayOk(h, s.Id, new ScriptPlayOptions { Prepare = ScriptPlayOptions.PrepareAlways }, "JP3 always");
            Check("JP3: prepare always prepares", r4 != null && r4.Prepared && r4.PrepareSent == 1);
            EditScript(h, s.Id, c => c.Prepared = null);
            var r5 = PlayOk(h, s.Id, new ScriptPlayOptions { Prepare = ScriptPlayOptions.PrepareNever }, "JP3 never");
            Check("JP3: prepare never does not prepare a script that was never prepared", r5 != null && !r5.Prepared && ScriptOf(h, s.Id).Prepared == null);
            // a failed step keeps prepared
            EditScript(h, s.Id, c => { c.Prepared = mark3; c.Prepare.Add(J("{\"cmd\":\"obj.transform\",\"key\":\"0000000000000000000000000000dead\",\"pos\":[0,0,0]}")); });
            var r6 = PlayOk(h, s.Id, null, "JP3 failing step");
            string mark6 = ScriptOf(h, s.Id).Prepared;
            Check("JP3: a changed preparation prepares; its failed step keeps the earlier mark", r6 != null && r6.Prepared && r6.PrepareFailed == 1 && mark6 == mark3, "failed " + r6?.PrepareFailed + ", mark " + mark6);
            var r7 = PlayOk(h, s.Id, null, "JP3 after the failure");
            Check("JP3: the next play prepares again", r7 != null && r7.Prepared);
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ JP4

    static void JP4()
    {
        var f = NewSFix(0x69a40001UL, item: false);
        try
        {
            var h = f.H;
            var s = NewScript("S", CharaRole("a", "female"), CharaRole("b", "male"));
            s.Cast["a"] = f.A;
            s.Cast["b"] = f.B;
            s.Timeline = ScriptTimeline(3f, true, Track("pos", "$a", TimelineKey.OfVector(0f, new Vec3(1, 0, 0))));
            var t = NewScript("T", CharaRole("a", "female"));
            t.Cast["a"] = f.B;
            t.Timeline = ScriptTimeline(5f, true, Track("pos", "$a", TimelineKey.OfVector(0f, new Vec3(2, 0, 0))));
            AddScripts(h, s, t);
            Func<TimelineDocument> tl = () => h.Session.Model.Timeline;
            int entries = Undo(h).Count;

            // row 1: none
            Check("JP4 row none: precondition, the scene has no Timeline", tl() == null);
            var r = PlayOk(h, s.Id, null, "JP4 none");
            var addedNone = Added(h, entries);
            Check("JP4 row none: S compiled and loaded, one entry «Load script» (read " + Labels(addedNone) + ")", r != null && r.Loaded && tl()?.Script?.Id == s.Id && addedNone.SequenceEqual(new[] { ScriptEditCommand.LabelLoad }));
            // row 2: S's, unedited, same compile: kept
            entries = Undo(h).Count;
            var before = tl();
            r = PlayOk(h, s.Id, null, "JP4 replay");
            Check("JP4 row replay: precondition, S's unedited Timeline", before?.Script?.Id == s.Id && !before.IsEditedSinceLoad);
            Check("JP4 row replay: kept, no load, no entry (read " + Labels(Added(h, entries)) + ")", r != null && !r.Loaded && ReferenceEquals(tl(), before) && Added(h, entries).Count == 0);
            // row 3: S's, unedited, the fresh compile differs (a recast)
            EditScript(h, s.Id, c => c.Cast["a"] = f.B);
            entries = Undo(h).Count;
            r = PlayOk(h, s.Id, null, "JP4 recast");
            Check("JP4 row fresh differs: precondition, the cast changed", ScriptOf(h, s.Id).Cast["a"] == f.B);
            Check("JP4 row fresh differs: the fresh compile is loaded (pos track on b), one entry «Load script» (read " + Labels(Added(h, entries)) + ")", r != null && r.Loaded && tl().Tracks[0].Key == f.B &&
                Added(h, entries).SequenceEqual(new[] { ScriptEditCommand.LabelLoad }));
            // row 4: S's, edited: kept with the edits and a warning; reload loads
            var edited = tl().Clone();
            edited.Tracks[0].Keys[0].Vector = new Vec3(9, 0, 0);
            h.Rt.GetPart<TimelinePlayback>().Service.SetDocument(edited);
            Check("JP4 row edited: precondition, the Timeline reads as edited", tl().IsEditedSinceLoad);
            r = PlayOk(h, s.Id, null, "JP4 edited");
            Check("JP4 row edited: kept with the edits, the warning given", r != null && !r.Loaded && tl().Tracks[0].Keys[0].Vector.X == 9f && r.Warnings.Any(w => w.Contains("edited after the script was loaded")),
                string.Join("; ", r?.Warnings ?? new List<string>()));
            r = PlayOk(h, s.Id, new ScriptPlayOptions { Reload = true }, "JP4 reload");
            Check("JP4 row edited, reload: the fresh compile replaces the edits", r != null && r.Loaded && tl().Tracks[0].Keys[0].Vector.X == 1f && !tl().IsEditedSinceLoad);
            // row 5: another script's, unedited
            r = PlayOk(h, t.Id, null, "JP4 another");
            Check("JP4 row another unedited: T is loaded", r != null && r.Loaded && tl().Script.Id == t.Id && tl().Duration == 5f);
            // row 6: another script's, edited: refused; discard loads
            edited = tl().Clone();
            edited.Duration = 6f;
            h.Rt.GetPart<TimelinePlayback>().Service.SetDocument(edited);
            Check("JP4 row another edited: precondition, T's Timeline edited", tl().Script.Id == t.Id && tl().IsEditedSinceLoad);
            PlayScript(h, s.Id, null, out var refusal);
            Check("JP4 row another edited: refused, bad_args naming T", refusal != null && refusal.Code == BridgeCodes.BadArgs && refusal.Message.Contains("T"), refusal?.ToString());
            Check("JP4 row another edited: nothing changed", tl().Script.Id == t.Id && tl().Duration == 6f);
            r = PlayOk(h, s.Id, new ScriptPlayOptions { Discard = true }, "JP4 discard");
            Check("JP4 row another edited, discard: S is loaded", r != null && r.Loaded && tl().Script.Id == s.Id);
            // row 7: a Timeline from no script: kept as a new scene script «Timeline», then S loaded, one entry
            h.Ok(new { cmd = "timeline.build", duration = 7, tracks = new object[] { new { type = "pos", key = f.A, keys = new object[] { new { t = 0, v = new[] { 3, 0, 0 } } } } } }, "JP4: a plain Timeline");
            Check("JP4 row plain: precondition, a Timeline from no script", tl() != null && tl().Script == null && tl().Duration == 7f);
            int scripts = f.Model.Scripts.Count;
            entries = Undo(h).Count;
            r = PlayOk(h, s.Id, null, "JP4 plain");
            var kept = f.Model.Scripts.Scripts.LastOrDefault();
            Check("JP4 row plain: kept as a new scene script «Timeline» (no roles, node ids), S loaded, in one entry",
                r != null && r.Loaded && f.Model.Scripts.Count == scripts + 1 && kept.Name == ScriptPlayer.KeptTimelineName && kept.Roles.Count == 0 && kept.Timeline.Duration == 7f &&
                kept.Timeline.Tracks[0].Key == f.A && tl().Script.Id == s.Id && Added(h, entries).SequenceEqual(new[] { ScriptEditCommand.LabelLoad }),
                "scripts " + f.Model.Scripts.Count + ", entries read " + Labels(Added(h, entries)) + ", kept " + kept?.Name);
            h.Session.Commands.Undo();
            Check("JP4 row plain: one undo brings the plain Timeline back and removes the kept script", tl() != null && tl().Script == null && tl().Duration == 7f && f.Model.Scripts.Count == scripts);
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ JP5

    static void JP5()
    {
        var f = NewSFix(0x69a50001UL, item: false);
        try
        {
            var h = f.H;
            var s = NewScript("capture", CharaRole("a", "female"), CharaRole("b", "male"));
            s.Cast["a"] = f.A;
            s.Cast["b"] = f.B;
            s.Timeline = ScriptTimeline(3f, true, Track("pos", "$a", TimelineKey.OfVector(0f, new Vec3(1, 0, 0))));
            s.Timeline.Events.Add(Event(0.5f, "{\"cmd\":\"obj.transform\",\"key\":\"$b\",\"note\":\"$$kept\",\"pos\":[0,0,0]}"));
            AddScripts(h, s);
            if (PlayOk(h, s.Id, null, "JP5") == null) return;
            var tl = h.Session.Model.Timeline.Clone();
            tl.Tracks.Add(new TimelineTrack(JsonText.NewId(), "rot", f.B) { Keys = new List<TimelineKey> { TimelineKey.OfVector(1f, new Vec3(0, 45, 0)) } });
            tl.Events.Add(new TimelineEvent(JsonText.NewId(), 1.5f, J("{\"cmd\":\"obj.transform\",\"key\":\"" + f.A + "\",\"target\":{\"key\":\"" + f.B + "\"}}")));
            h.Rt.GetPart<TimelinePlayback>().Service.SetDocument(tl);
            string oldCompiled = h.Session.Model.Timeline.Script.Compiled;
            Check("JP5 Present: the Timeline was edited", h.Session.Model.Timeline.IsEditedSinceLoad);
            int entries = Undo(h).Count;
            var reply = ScriptPlayer.Capture(h.App, s.Id);
            var back = ScriptOf(h, s.Id).Timeline;
            Check("JP5: the reply {script, tracks, keys, events}", (string)reply["script"] == s.Id && (int)reply["tracks"] == 2 && (int)reply["events"] == 2, reply.ToString(Formatting.None));
            Check("JP5: track keys back to roles", back.Tracks.Select(t => t.Key).SequenceEqual(new[] { "$a", "$b" }), string.Join(",", back.Tracks.Select(t => t.Key)));
            var e2 = back.Events.First(e => e.T == 1.5f).Do;
            Check("JP5: request strings back to roles at depth", (string)e2["key"] == "$a" && (string)e2["target"]["key"] == "$b", e2.ToString(Formatting.None));
            var e1 = back.Events.First(e => e.T == 0.5f).Do;
            Check("JP5: a literal string that starts with $ stays itself after a compile (written as $$)", (string)e1["note"] == "$$kept", (string)e1["note"]);
            var m = h.Session.Model.Timeline.Script;
            Check("JP5: compiled renewed: the Timeline reads as unedited", m.Compiled != oldCompiled && !h.Session.Model.Timeline.IsEditedSinceLoad);
            Check("JP5: one entry «Save edits into script» (read " + Labels(Added(h, entries)) + ")", Added(h, entries).SequenceEqual(new[] { ScriptEditCommand.LabelCapture }));
            var again = ScriptCompiler.Compile(ScriptOf(h, s.Id), null);
            Check("JP5: compiling the captured script gives the Timeline back (same hash)", again.Compiled == h.Session.Model.Timeline.CompiledHash(), again.Compiled + " / " + h.Session.Model.Timeline.CompiledHash());
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ JP6

    static void JP6()
    {
        var f = NewSFix(0x69a60001UL, item: false);
        try
        {
            var h = f.H;
            var s = NewScript("crossings", CharaRole("a", "female"));
            s.Cast["a"] = f.A;
            s.Timeline = ScriptTimeline(1f, true);
            foreach (var t in new[] { 0f, 0.25f, 0.5f }) s.Timeline.Events.Add(Event(t, "{\"cmd\":\"ping\"}"));
            AddScripts(h, s);
            var tlp = f.Tl;
            long fired0 = tlp.EventsFired;
            if (PlayOk(h, s.Id, null, "JP6") == null) return;
            h.Frames(1);   // the first advance runs t = 0
            long afterZero = tlp.EventsFired - fired0;
            h.Frames(40);  // 0.667 s more: 0.25 and 0.5
            long afterHalf = tlp.EventsFired - fired0;
            Check("JP6: a play from 0 fires t = 0, then 0.25 and 0.5", afterZero == 1 && afterHalf == 3, "after 1 frame " + afterZero + ", after 41 " + afterHalf);
            tlp.Service.Seek(0.1);
            h.Frames(1);
            long seekBefore = tlp.EventsFired;
            tlp.Service.Seek(0.6);
            Check("JP6: a seek past 0.25 and 0.5 fires nothing", tlp.EventsFired == seekBefore);
            tlp.Service.Pause();
            long paused = tlp.EventsFired;
            h.Frames(30);
            Check("JP6: nothing fires while paused", tlp.EventsFired == paused);
            tlp.Service.Seek(0.0);
            tlp.Service.Play();   // a resume at 0 runs (0, n]
            h.Frames(1);
            long resumed = tlp.EventsFired - paused;
            Check("JP6: a resume runs (t, n]: t = 0 does not fire again", resumed == 0, "" + resumed);
            tlp.Service.Seek(0.9);
            long beforeWrap = tlp.EventsFired;
            h.Frames(10);  // past the wrap at 1.0
            long wrap = tlp.EventsFired - beforeWrap;
            Check("JP6: a loop pass fires t = 0 again", wrap >= 1, "" + wrap);
            tlp.Service.Stop();
            long stopped = tlp.EventsFired;
            h.Frames(30);
            Check("JP6: nothing fires after stop", tlp.EventsFired == stopped);
            Check("JP6 Present: >= 3 events fired (" + (tlp.EventsFired - fired0) + "), no error", tlp.EventsFired - fired0 >= 3 && tlp.EventErrors == 0, "errors " + tlp.EventErrors);
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ JP7

    static void JP7()
    {
        var f = NewSFix(0x69a70001UL, item: false);
        try
        {
            var h = f.H;
            // HERO107 plays dance_00 at speed 1 for a while: its row plays at a time not 0
            h.Ok(new { cmd = "chara.anime", key = f.B, group = 11, category = 5, no = 0, speed = 1 }, "JP7: HERO107's row at speed 1");
            h.Frames(40);
            var anim = h.Rt.FindChara(f.B).Animation;
            float t0 = anim.NormalizedTime;
            Check("JP7 Present: the row plays at a time not 0 (" + F(t0) + ")", anim.IsLoaded && t0 > 0.01f);
            var s = NewScript("restart", CharaRole("b", "male"));
            s.Cast["b"] = f.B;
            s.Timeline = ScriptTimeline(4f, true, Track("anime", "$b", TimelineKey.OfAnime(0f, new AnimeRef(DocDefaults.VanillaSource, 11, 5, 0))));
            AddScripts(h, s);
            var tlp = f.Tl;
            long restarts0 = tlp.AnimationRestarts;
            if (PlayOk(h, s.Id, null, "JP7 play") == null) return;
            h.Frames(1);
            float t1 = anim.NormalizedTime;
            Check("JP7: the first tick after the play loads the row again at time 0 (time " + F(t0) + " -> " + F(t1) + ")", tlp.AnimationRestarts == restarts0 + 1 && t1 < t0 && t1 < 0.05f,
                "restarts " + (tlp.AnimationRestarts - restarts0));
            h.Frames(40);
            float t2 = anim.NormalizedTime;
            Check("JP7: the row goes on afterwards (no restart at later ticks)", t2 > t1 && tlp.AnimationRestarts == restarts0 + 1, F(t2));
            // a replay without a reload restarts it again
            if (PlayOk(h, s.Id, null, "JP7 replay") == null) return;
            h.Frames(1);
            float t3 = anim.NormalizedTime;
            Check("JP7: a replay without a reload restarts it again", tlp.AnimationRestarts == restarts0 + 2 && t3 < t2 && t3 < 0.05f, F(t2) + " -> " + F(t3));
            // a seek to 0 while playing restarts nothing
            h.Frames(20);
            long r4 = tlp.AnimationRestarts;
            tlp.Service.Seek(0);
            h.Frames(1);
            Check("JP7: a seek to 0 while playing restarts nothing", tlp.AnimationRestarts == r4);
            // a timeline.play from stopped restarts (any Timeline, not only a script's)
            tlp.Service.Stop();
            h.Frames(20);
            tlp.Service.Play();
            h.Frames(1);
            Check("JP7: timeline.play from stopped restarts the row", tlp.AnimationRestarts == r4 + 1);
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ JP7c

    const float Jp7cPass = 2f, Jp7cTurnFrom = 1.5f, Jp7cTurnTo = 1.6f, Jp7cPlayAt = 1.8f;
    const double Jp7cEqual = 1e-5, Jp7cSwing = 0.05;
    static readonly int[] Jp7cFrames = { 1, 10, 30 };

    /// <summary>The active skirt chains of a character's dynamic bones (none when it has no motion).</summary>
    static List<HS2Next.Motion.DynamicBones.DynamicBoneChain> SkirtChains(CharaRealization c, bool skirtOnly = true)
    {
        var st = c != null && c.Motion ? c.Motion.Get<HS2Next.Motion.DynamicBones.DynamicBoneStage>() : null;
        if (st == null) return new List<HS2Next.Motion.DynamicBones.DynamicBoneChain>();
        return st.Chains.Where(x => x.Active && x.ParticleCount > 0 && (!skirtOnly || x.IsSkirt)).ToList();
    }

    static Vector3[] Particles(List<HS2Next.Motion.DynamicBones.DynamicBoneChain> chains)
    {
        var l = new List<Vector3>();
        foreach (var ch in chains)
            for (int i = 0; i < ch.ParticleCount; i++) l.Add(ch.Position(i));
        return l.ToArray();
    }

    static Vector3[] InHolder(Transform holder, Vector3[] w) => w.Select(p => holder.InverseTransformPoint(p)).ToArray();

    /// <summary>The largest distance between two lists of points of one length (infinity when the lengths differ).</summary>
    static double MaxDist(Vector3[] a, Vector3[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return double.PositiveInfinity;
        double m = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double dx = (double)a[i].x - b[i].x, dy = (double)a[i].y - b[i].y, dz = (double)a[i].z - b[i].z;
            m = Math.Max(m, Math.Sqrt(dx * dx + dy * dy + dz * dz));
        }
        return m;
    }

    /// <summary>Frames until the Timeline's time reaches <paramref name="t"/> (bounded).</summary>
    static bool RunTo(Host h, TimelinePlayback tlp, double t, int max = 400)
    {
        for (int i = 0; i < max && tlp.Player.Time < t; i++) h.Frames(1);
        return tlp.Player.Time >= t;
    }

    /// <summary>The skirt chains' particles (and every active chain's) at frames 1, 10 and 30 after a play call.</summary>
    static (Dictionary<int, Vector3[]> skirt, Dictionary<int, Vector3[]> all) AfterRestart(Host h, string node)
    {
        var skirt = new Dictionary<int, Vector3[]>();
        var all = new Dictionary<int, Vector3[]>();
        int done = 0;
        foreach (int k in Jp7cFrames)
        {
            h.Frames(k - done);
            done = k;
            var c = h.Rt.FindChara(node);
            skirt[k] = Particles(SkirtChains(c));
            all[k] = Particles(SkirtChains(c, false));
        }
        return (skirt, all);
    }

    /// <summary>The names of the restart's chain stages ("timeline.chains" and those it prefixes) on a character's motion.</summary>
    static List<string> ChainStageNames(CharaRealization c) =>
        c != null && c.Motion ? c.Motion.Stages.Where(x => x.Name != null && x.Name.StartsWith("timeline.chains", StringComparison.Ordinal)).Select(x => x.Name).ToList() : new List<string>();

    /// <summary>
    /// The swing of a pass: a's skirt chain in its holder's frame just before the turn (rot track 0 -> 120 degrees between 1.5 and 1.6 s) and at
    /// <paramref name="at"/>; the largest particle distance between the two.
    /// </summary>
    static double Swing(Host h, TimelinePlayback tlp, string node, double at)
    {
        if (!RunTo(h, tlp, Jp7cTurnFrom - 0.05)) return -1;
        var c = h.Rt.FindChara(node);
        var before = InHolder(c.Holder, Particles(SkirtChains(c)));
        if (!RunTo(h, tlp, at)) return -1;
        return MaxDist(InHolder(c.Holder, Particles(SkirtChains(c))), before);
    }

    static void JP7c()
    {
        var h = NewHost(0x69a7c001UL);
        try
        {
            string A = LoadChara(h, femaleRel, "female", "JP7c: chara.load BR-Chan (physics on)", physics: true);
            string B = LoadChara(h, maleRel, "male", "JP7c: chara.load HERO107 (physics on)", physics: true);
            if (A == null || B == null) { Check("JP7c: the fixture's characters were added", false); return; }
            h.Ok(new { cmd = "obj.transform", key = B, pos = new[] { 8, 0, 0 } }, "JP7c: HERO107 at (8, 0, 0)");
            h.Ok(new { cmd = "chara.anime", key = A, group = 19, category = 0, no = 0, speed = 1 }, "JP7c: BR-Chan's row");
            h.Ok(new { cmd = "chara.anime", key = B, group = 11, category = 5, no = 0, speed = 1 }, "JP7c: HERO107's row");
            h.Frames(3);
            // a script with role a: an anime track (the row a plays) and a turn of a's body near the end of the 2 s pass
            var s = NewScript("chains", CharaRole("a", "female"));
            s.Cast["a"] = A;
            var zero = new Vec3(0, 0, 0);
            var turn = new TimelineTrack(JsonText.NewId(), "rot", "$a")
            {
                Keys = new List<TimelineKey> { TimelineKey.OfVector(0f, zero), TimelineKey.OfVector(Jp7cTurnFrom, zero), TimelineKey.OfVector(Jp7cTurnTo, new Vec3(0, 120, 0)) },
            };
            s.Timeline = ScriptTimeline(Jp7cPass, true, Track("anime", "$a", TimelineKey.OfAnime(0f, new AnimeRef(DocDefaults.VanillaSource, 19, 0, 0))), turn);
            AddScripts(h, s);
            h.Ok(new { cmd = "scene.save", name = "script_jp7c", overwrite = true, thumb = "placeholder", clock = "2026-11-08T00:00:00Z" }, "JP7c: scene.save");
            h.Ok(new { cmd = "scene.load", name = "script_jp7c" }, "JP7c: scene.load (the fresh load)");
            h.Frames(5);
            var tlp = h.Rt.GetPart<TimelinePlayback>();

            // the first play from the fresh load: the reference
            long cr0 = tlp.ChainResets;
            if (PlayOk(h, s.Id, null, "JP7c first play") == null) return;
            var stagesBefore = ChainStageNames(h.Rt.FindChara(A));
            h.Frames(1);
            var ca = h.Rt.FindChara(A);
            Check("JP7c: the first play's restart reset the cast's chains (a: resets " + (tlp.ChainResets - cr0) + ", last " + Labels(tlp.LastChainResets) + ")",
                tlp.ChainResets == cr0 + 1 && tlp.LastChainResets.SequenceEqual(new[] { A }));
            var stagesAfter = ChainStageNames(ca);
            Check("JP7c: the reset stages (\"timeline.chains*\") are off a's motion after the frame (before the frame " + Labels(stagesBefore) + ", after " + Labels(stagesAfter) + ")",
                ca != null && ca.Motion && stagesAfter.Count == 0);
            // AfterRestart counts the frame above as frame 1
            var refSkirt = new Dictionary<int, Vector3[]> { [1] = Particles(SkirtChains(ca)) };
            var refAll = new Dictionary<int, Vector3[]> { [1] = Particles(SkirtChains(ca, false)) };
            var refLocal1 = InHolder(ca.Holder, refSkirt[1]);
            h.Frames(9);
            refSkirt[10] = Particles(SkirtChains(ca)); refAll[10] = Particles(SkirtChains(ca, false));
            h.Frames(20);
            refSkirt[30] = Particles(SkirtChains(ca)); refAll[30] = Particles(SkirtChains(ca, false));
            int nSkirt = refSkirt[1].Length, nChains = SkirtChains(ca).Count;

            // the swing at the end of the pass, then a replay from 0
            double swing = Swing(h, tlp, A, Jp7cPlayAt);
            var endLocal = InHolder(ca.Holder, Particles(SkirtChains(ca)));
            Check("JP7c Present: a's skirt chain moved >= " + F(Jp7cSwing) + " units before the restart (" + F(swing) + " in its holder's frame, turn at " + F(Jp7cTurnFrom) + "-" + F(Jp7cTurnTo) + " s, time " +
                F(tlp.Player.Time) + ")", swing >= Jp7cSwing);
            Info("JP7c: before the replay the chain stands " + F(MaxDist(endLocal, refLocal1)) + " units (holder's frame) from where the first play's restart put it");
            long cr1 = tlp.ChainResets, ar1 = tlp.AnimationRestarts;
            if (PlayOk(h, s.Id, null, "JP7c replay") == null) return;
            var rep = AfterRestart(h, A);
            var worst = Jp7cFrames.Select(k => MaxDist(rep.skirt[k], refSkirt[k])).ToList();
            Check("JP7c: after the replay's restart a's skirt chain equals the first play's from the fresh load at frames 1, 10, 30 (<= " + F(Jp7cEqual) + "; " + string.Join(", ", worst.Select(F)) + "; " +
                nSkirt + " particles of " + nChains + " chains)", nSkirt > 0 && worst.All(d => d <= Jp7cEqual));
            Info("JP7c: every active chain of a (hair, clothes, accessories), the same frames: " + string.Join(", ", Jp7cFrames.Select(k => F(MaxDist(rep.all[k], refAll[k])))) + " (" + refAll[1].Length + " particles; printed)");
            Check("JP7c: the replay reset a's chains once (resets " + (tlp.ChainResets - cr1) + ", last " + Labels(tlp.LastChainResets) + "; animation restarts " + (tlp.AnimationRestarts - ar1) + ")",
                tlp.ChainResets == cr1 + 1 && tlp.LastChainResets.SequenceEqual(new[] { A }));

            // a seek to 0 after the swing resets nothing
            double swingSeek = Swing(h, tlp, A, Jp7cPlayAt);
            Check("JP7c Present (seek): a's skirt chain moved >= " + F(Jp7cSwing) + " units before the seek (" + F(swingSeek) + ")", swingSeek >= Jp7cSwing);
            long crS = tlp.ChainResets;
            tlp.Service.Seek(0);
            h.Frames(1);
            Check("JP7c: a seek to 0 resets no chain (resets " + (tlp.ChainResets - crS) + ", time " + F(tlp.Player.Time) + ")", tlp.ChainResets == crS && tlp.Player.State == TimelinePlayState.Playing);
            // the loop pass after the swing resets nothing
            double swingLoop = Swing(h, tlp, A, Jp7cPass - 0.05);
            Check("JP7c Present (loop pass): a's skirt chain moved >= " + F(Jp7cSwing) + " units before the loop pass (" + F(swingLoop) + ")", swingLoop >= Jp7cSwing);
            long crL = tlp.ChainResets;
            for (int i = 0; i < 30 && tlp.Player.Time >= 1.0; i++) h.Frames(1);
            Check("JP7c: the loop pass resets no chain (resets " + (tlp.ChainResets - crL) + ", time after the wrap " + F(tlp.Player.Time) + ")",
                tlp.ChainResets == crL && tlp.Player.Time < 1.0 && tlp.Player.State == TimelinePlayState.Playing);
            // a pause and a resume after the swing reset nothing
            double swingPause = Swing(h, tlp, A, Jp7cPlayAt);
            Check("JP7c Present (pause): a's skirt chain moved >= " + F(Jp7cSwing) + " units before the pause (" + F(swingPause) + ")", swingPause >= Jp7cSwing);
            long crP = tlp.ChainResets;
            tlp.Service.Pause();
            h.Frames(3);
            tlp.Service.Play();
            h.Frames(1);
            Check("JP7c: a pause and a resume reset no chain (resets " + (tlp.ChainResets - crP) + ", time " + F(tlp.Player.Time) + ")", tlp.ChainResets == crP && tlp.Player.State == TimelinePlayState.Playing);

            // a Timeline without a script marker: the characters of its anime tracks (b), not a
            tlp.Service.Stop();
            h.Frames(2);
            h.Ok(new { cmd = "timeline.build", duration = 2, loop = true, play = false, tracks = new object[] { new { type = "anime", key = B, keys = new object[] { new { t = 0, group = 11, category = 5, no = 0 } } } } },
                "JP7c: a plain Timeline with an anime track on b");
            long crB = tlp.ChainResets;
            bool plain = h.Session.Model.Timeline != null && h.Session.Model.Timeline.Script == null;
            tlp.Service.Play();
            h.Frames(1);
            Check("JP7c: without a marker a play from stopped resets the chains of its anime tracks' characters only (marker none " + plain + "; resets " + (tlp.ChainResets - crB) + ", last " +
                Labels(tlp.LastChainResets) + ")", plain && tlp.ChainResets == crB + 1 && tlp.LastChainResets.SequenceEqual(new[] { B }));
        }
        finally { h.Dispose(); }
    }

    // ------------------------------------------------------------ JE2

    static void JE2()
    {
        var f = NewSFix(0x69ae0002UL, item: false);
        try
        {
            var h = f.H;
            var s = NewScript("prepare", CharaRole("a", "female"));
            s.Cast["a"] = f.A;
            s.Prepare.Add(J("{\"wait\":0.02}"));
            s.Prepare.Add(J("{\"cmd\":\"ragdoll.set\",\"key\":\"$a\",\"preset\":\"limp\"}"));
            s.Prepare.Add(J("{\"cmd\":\"obj.transform\",\"key\":\"$a\",\"rot\":[0,15,0]}"));
            s.Timeline = ScriptTimeline(2f, true);
            AddScripts(h, s);
            var st = (CharaNodeState)f.Model.Find(f.A).State;
            string ragBefore = JsonConvert.SerializeObject(st.Ragdoll);
            int stainsBefore = st.Wet.Stains.Count;
            var undoBefore = Undo(h);
            var server = h.App.Bridge.Server;
            var gate = server.InlineGate;
            var r = PlayOk(h, s.Id, null, "JE2");
            if (r == null) return;
            Check("JE2: the app's inline gate is its own again after the preparation", gate != null && ReferenceEquals(server.InlineGate, gate));
            st = (CharaNodeState)f.Model.Find(f.A).State;
            int stainsAfter = st.Wet.Stains.Count;
            string ragAfter = JsonConvert.SerializeObject(st.Ragdoll);
            var added = Added(h, undoBefore.Count);
            int changes = (stainsAfter != stainsBefore ? 1 : 0) + (ragAfter != ragBefore ? 1 : 0) + (f.Model.Find(f.A).Doc.Transform.Rot.Y == 15f ? 1 : 0);
            Check("JE2 Present: the preparation made >= 2 model changes (" + changes + ": stains " + stainsBefore + " -> " + stainsAfter + ", ragdoll values changed " + (ragAfter != ragBefore) + ", rotation)", changes >= 2,
                string.Join("; ", r.Failures.Select(x => x.Cmd + " " + x.Code + " " + x.Message)));
            Check("JE2: every preparation step succeeded (" + r.PrepareSent + " sent)", r.PrepareFailed == 0 && r.PrepareSent == 2, string.Join("; ", r.Failures.Select(x => x.Index + " " + x.Cmd + " " + x.Code + " " + x.Message)));
            Check("JE2: every command joined: one entry «Prepare script» (then «Load script») (read " + Labels(added) + ")", added.SequenceEqual(new[] { ScriptEditCommand.LabelPrepare, ScriptEditCommand.LabelLoad }));
            h.Session.Commands.Undo();   // «Load script»
            h.Session.Commands.Undo();   // «Prepare script»
            h.Rt.ApplyNow();
            st = (CharaNodeState)f.Model.Find(f.A).State;
            Check("JE2: undo of «Prepare script» removes what it made (the ragdoll values, the rotation, the mark; the stain is the play row's JE2s)",
                st.Wet.Stains.Count == stainsBefore && JsonConvert.SerializeObject(st.Ragdoll) == ragBefore && f.Model.Find(f.A).Doc.Transform.Rot.Y != 15f && ScriptOf(h, s.Id).Prepared == null,
                "stains " + st.Wet.Stains.Count + ", prepared " + ScriptOf(h, s.Id).Prepared);
            Check("JE2: the history after the undo is the one before", Undo(h).SequenceEqual(undoBefore), string.Join(", ", Undo(h)));
            // a preparation with a failing step and a step whose command answers later: both counted, the rest joins, the gate is restored
            var g = NewScript("prepare fails", CharaRole("a", "female"));
            g.Cast["a"] = f.A;
            g.Prepare.Add(J("{\"cmd\":\"obj.transform\",\"key\":\"$a\",\"rot\":[0,25,0]}"));
            g.Prepare.Add(J("{\"cmd\":\"obj.transform\",\"key\":\"0000000000000000000000000000dead\",\"pos\":[0,0,0]}"));
            g.Prepare.Add(J("{\"cmd\":\"map.load\",\"clear\":true}"));
            g.Prepare.Add(J("{\"cmd\":\"obj.transform\",\"key\":\"$a\",\"rot\":[0,35,0]}"));
            g.Timeline = ScriptTimeline(2f, true);
            AddScripts(h, g);
            var before2 = Undo(h);
            var r2 = PlayOk(h, g.Id, null, "JE2 failing preparation");
            if (r2 == null) return;
            var defer = r2.Failures.FirstOrDefault(x => x.Cmd == "map.load");
            Check("JE2: a step whose command answers later fails unsent: unsupported, the rule named, counted", defer != null && defer.Index == 2 && defer.Code == BridgeCodes.Unsupported &&
                defer.Message.StartsWith(ScriptPlayer.DeferringStepMessage, StringComparison.Ordinal) && r2.PrepareSent == 4 && r2.PrepareFailed == 2,
                string.Join("; ", r2.Failures.Select(x => x.Index + " " + x.Cmd + " " + x.Code + " " + x.Message)));
            Check("JE2: the app's inline gate is its own again after a failing preparation step", ReferenceEquals(server.InlineGate, gate));
            var added2 = Added(h, before2.Count);
            Check("JE2: the steps that succeeded joined one entry «Prepare script»; the mark stays unwritten (read " + Labels(added2) + ")", added2.SequenceEqual(new[] { ScriptEditCommand.LabelPrepare, ScriptEditCommand.LabelLoad }) &&
                f.Model.Find(f.A).Doc.Transform.Rot.Y == 35f && ScriptOf(h, g.Id).Prepared == null);
            Info("JE2: a bake.make step in a preparation was not run (headless bake.make of a live target answers unavailable; never run). The stain made in a preparation is JE2s in the play row (wet.attach answers unsupported headless)");
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ JE3

    static void JE3()
    {
        var f = NewSFix(0x69ae0003UL, item: false);
        try
        {
            var h = f.H;
            var s = NewScript("entries", CharaRole("a", "female"));
            s.Cast["a"] = f.A;
            s.Setup.Add(J("{\"cmd\":\"obj.transform\",\"key\":\"$a\",\"pos\":[0,0,2]}"));
            s.Setup.Add(J("{\"cmd\":\"chara.face\",\"key\":\"$a\",\"eyes\":1}"));
            s.Timeline = ScriptTimeline(2f, true, Track("pos", "$a", TimelineKey.OfVector(0f, new Vec3(0, 0, 1))));
            AddScripts(h, s);
            var before = Undo(h);
            var r1 = PlayOk(h, s.Id, null, "JE3 first");
            var added1 = Added(h, before.Count);
            Check("JE3 Present: the first play added «Load script» (read " + Labels(added1) + ")", added1.SequenceEqual(new[] { ScriptEditCommand.LabelLoad }));
            Check("JE3: set-up added no entry (2 steps sent, the model changed)", r1 != null && r1.SetupSent == 2 && f.Model.Find(f.A).Doc.Transform.Pos.Z == 2f);
            var mid = Undo(h);
            var r2 = PlayOk(h, s.Id, null, "JE3 replay");
            Check("JE3: a replay that changes nothing adds no entry (read " + Labels(Added(h, mid.Count)) + ")", r2 != null && Undo(h).SequenceEqual(mid));
            for (int i = 0; i < 5; i++) PlayOk(h, s.Id, null, "JE3 replay " + i);
            Check("JE3: five more replays add no entry (playing never floods the history)", Undo(h).SequenceEqual(mid));
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ JB1, JB3, JP8 (the script commands' slots)

    static int Owned(Host h, string owner) => h.Rt.Overrides.Any ? h.Rt.Overrides.Slots(null, owner).Count : 0;

    /// <summary>A script whose timed commands at t = 0 start a move of a, a follow of a and a depth of field on a (long enough to outlast the checks).</summary>
    static SceneScript SlotScript(SFix f, string name, float moveTime = 3f)
    {
        var s = NewScript(name, CharaRole("a", "female"), CharaRole("b", "male"));
        s.Cast["a"] = f.A;
        s.Cast["b"] = f.B;
        s.Timeline = ScriptTimeline(8f, true, Track("rot", "$a", TimelineKey.OfVector(0f, new Vec3(0, 0, 0))));
        s.Timeline.Events.Add(Event(0f, "{\"cmd\":\"obj.moveTo\",\"key\":\"$a\",\"target\":[0,0,20],\"stop\":0,\"time\":" + F(moveTime) + "}"));
        s.Timeline.Events.Add(Event(0f, "{\"cmd\":\"camera.follow\",\"target\":{\"key\":\"$a\"},\"seconds\":6,\"dist\":30}"));
        s.Timeline.Events.Add(Event(0f, "{\"cmd\":\"dof\",\"on\":true,\"focus\":\"$a\"}"));
        return s;
    }

    static void JB1()
    {
        var f = NewSFix(0x69ab0001UL, item: false);
        try
        {
            var h = f.H;
            ScriptTestG.EnsureRegistered(h.App);
            var s = SlotScript(f, "rebuild", 2f);
            AddScripts(h, s);
            if (PlayOk(h, s.Id, null, "JB1") == null) return;
            h.Frames(30);   // 0.5 s into the 2 s move
            var c = h.Rt.FindChara(f.A);
            int builds = 0;
            Action<CharaRealization> onBuilt = x => { if (x.NodeId == f.A) builds++; };
            h.Rt.CharaBuilt += onBuilt;
            float zMid = c.Holder.position.z;
            Check("JB1 Present: a's move, the follow and the Timeline slot are held mid-move (z " + F(zMid) + ")", Owned(h, "move") == 1 && Owned(h, "follow") == 1 && Owned(h, "timeline") >= 1 && zMid > 1 && zMid < 19,
                "move " + Owned(h, "move") + ", follow " + Owned(h, "follow") + ", timeline " + Owned(h, "timeline"));
            int rebuildsBefore = c.PluginRebuilds;
            bool asked = c.RequestPluginRebuild("script test");
            h.Frames(3);
            h.Rt.CharaBuilt -= onBuilt;
            c = h.Rt.FindChara(f.A);
            Check("JB1 Present: the rebuild happened mid-move (a built " + builds + " time(s), z " + F(c.Holder.position.z) + ")", asked && builds >= 1 && c.PluginRebuilds == rebuildsBefore + 1 && c.Holder.position.z < 19,
                "asked " + asked + ", builds " + builds + ", rebuilds " + (c.PluginRebuilds - rebuildsBefore));
            Check("JB1: after the rebuild the Timeline slot, the move and the follow are still held", Owned(h, "move") == 1 && Owned(h, "follow") == 1 && Owned(h, "timeline") >= 1,
                "move " + Owned(h, "move") + ", follow " + Owned(h, "follow") + ", timeline " + Owned(h, "timeline"));
            float zAfter = h.Rt.FindChara(f.A).Holder.position.z;
            h.Frames(120);
            float zEnd = h.Rt.FindChara(f.A).Holder.position.z;
            Check("JB1: the move went on to its end (z " + F(zAfter) + " -> " + F(zEnd) + ", the end 20 in the model)", zEnd > zAfter && Math.Abs(zEnd - 20f) < 1e-3f && Math.Abs(f.Model.Find(f.A).Doc.Transform.Pos.Z - 20f) < 1e-3f);
            Check("JB1: the follow still holds the camera after the rebuild (6 s)", Owned(h, "follow") == 1);
        }
        finally { f.Dispose(); }
    }

    static void JB3()
    {
        var f = NewSFix(0x69ab0003UL, item: false);
        try
        {
            var h = f.H;
            ScriptTestG.EnsureRegistered(h.App);
            var s = SlotScript(f, "slots", 5f);
            AddScripts(h, s);
            if (PlayOk(h, s.Id, null, "JB3") == null) return;
            h.Frames(10);
            Check("JB3 Present: each owner holds a slot (move, follow, dof)", Owned(h, "move") == 1 && Owned(h, "follow") == 1 && Owned(h, "dof") == 1,
                Owned(h, "move") + ", " + Owned(h, "follow") + ", " + Owned(h, "dof"));
            h.Ok(new { cmd = "scene.save", name = "script_jb3", overwrite = true, thumb = "placeholder", clock = "2026-11-08T00:00:00Z" }, "JB3: scene.save");
            h.Ok(new { cmd = "scene.new" }, "JB3: scene.new");
            h.Frames(3);
            Check("JB3: scene new releases every script slot", Owned(h, "move") == 0 && Owned(h, "follow") == 0 && Owned(h, "dof") == 0 && Owned(h, "timeline") == 0,
                Owned(h, "move") + ", " + Owned(h, "follow") + ", " + Owned(h, "dof"));
            h.Ok(new { cmd = "scene.load", name = "script_jb3" }, "JB3: scene.load");
            h.Frames(3);
            var r = PlayOk(h, s.Id, null, "JB3 after load");
            h.Frames(10);
            Check("JB3 Present: after the load each owner holds a slot again", r != null && Owned(h, "move") == 1 && Owned(h, "follow") == 1 && Owned(h, "dof") == 1);
            h.Ok(new { cmd = "scene.load", name = "script_jb3" }, "JB3: scene.load over the playing scene");
            h.Frames(3);
            Check("JB3: scene load releases every script slot", Owned(h, "move") == 0 && Owned(h, "follow") == 0 && Owned(h, "dof") == 0,
                Owned(h, "move") + ", " + Owned(h, "follow") + ", " + Owned(h, "dof"));
            Check("JB3: the loaded scene keeps its script, cast and Timeline", ScriptOf(h, s.Id)?.Cast.Count == 2 && h.Session.Model.Timeline?.Script?.Id == s.Id);
        }
        finally { f.Dispose(); }
    }

    static void JP8()
    {
        var f = NewSFix(0x69a80001UL, item: false);
        try
        {
            var h = f.H;
            ScriptTestG.EnsureRegistered(h.App);
            var s = SlotScript(f, "stop", 5f);
            s.Timeline.Events.Add(Event(0.05f, "{\"cmd\":\"ragdoll.build\",\"key\":\"$b\"}"));
            AddScripts(h, s);
            var ragdolls = h.Rt.GetPart<RagdollPart>();
            foreach (bool reset in new[] { false, true })
            {
                string tag = reset ? "reset" : "reset: false";
                if (PlayOk(h, s.Id, null, "JP8 " + tag) == null) return;
                h.Frames(30);
                Check("JP8 Present (" + tag + "): a body exists, the three owners hold slots (the stain is the play row's JP8s)", ragdolls.BodyOf(f.B) != null &&
                    Owned(h, "move") == 1 && Owned(h, "follow") == 1 && Owned(h, "dof") == 1,
                    "body " + (ragdolls.BodyOf(f.B) != null) + ", slots " + Owned(h, "move") + Owned(h, "follow") + Owned(h, "dof"));
                var stop = ScriptPlayer.Stop(h.App, reset);
                h.Frames(2);
                Check("JP8 (" + tag + "): the Timeline stopped, its slots and the three owners' released", f.Tl.Player.State == TimelinePlayState.Stopped && Owned(h, "timeline") == 0 &&
                    Owned(h, "move") == 0 && Owned(h, "follow") == 0 && Owned(h, "dof") == 0 && stop.State == "stopped" && stop.Released >= 3, "released " + stop.Released);
                if (reset)
                    Check("JP8 (reset): the cast's body released (ragdolls 1)", ragdolls.BodyOf(f.B) == null && stop.Ragdolls == 1, "ragdolls " + stop.Ragdolls + "; " + string.Join("; ", stop.Warnings));
                else
                    Check("JP8 (reset: false): the body stays", ragdolls.BodyOf(f.B) != null && stop.Ragdolls == 0 && stop.Dried == 0);
                Check("JP8 (" + tag + "): the reply {state, released, ragdolls, dried}", stop.ToReply().Properties().Select(p => p.Name).SequenceEqual(new[] { "state", "released", "ragdolls", "dried" }));
                Check("JP8 (" + tag + "): the move cut short wrote its shown position into the model", f.Model.Find(f.A).Doc.Transform.Pos.Z > 0.5f, "z " + F(f.Model.Find(f.A).Doc.Transform.Pos.Z));
            }
        }
        finally { f.Dispose(); }
    }

    // ------------------------------------------------------------ JB2

    static void JB2()
    {
        var f = NewSFix(0x69ab0002UL, item: false);
        try
        {
            var h = f.H;
            var s = NewScript("deleted", CharaRole("a", "female"), CharaRole("b", "male"));
            s.Cast["a"] = f.A;
            s.Cast["b"] = f.B;
            s.Timeline = ScriptTimeline(2f, true, Track("pos", "$b", TimelineKey.OfVector(0f, new Vec3(4, 0, 0))), Track("pos", "$a", TimelineKey.OfVector(0f, new Vec3(1, 0, 0))));
            s.Timeline.Events.Add(Event(0.5f, "{\"cmd\":\"obj.transform\",\"key\":\"$b\",\"rot\":[0,10,0]}"));
            AddScripts(h, s);
            var tlp = f.Tl;
            if (PlayOk(h, s.Id, null, "JB2") == null) return;
            h.Frames(2);
            Check("JB2 Present: b was cast and its track active", ScriptOf(h, s.Id).Cast["b"] == f.B && tlp.Player.CountActiveTracks() == 2);
            h.Ok(new { cmd = "delete", key = f.B }, "JB2: delete b's node");
            long err0 = tlp.EventErrors;
            h.Frames(40);   // past 0.5
            Check("JB2: its track holds no target (1 active track left)", tlp.Player.CountActiveTracks() == 1, "" + tlp.Player.CountActiveTracks());
            Check("JB2: its timed command fails not_found and is counted", tlp.EventErrors > err0 && tlp.LastEventError?.Code == BridgeCodes.NotFound, tlp.LastEventError?.Code + " " + tlp.LastEventError?.Message);
            Check("JB2: the cast entry stays", ScriptOf(h, s.Id).Cast.TryGetValue("b", out var still) && still == f.B);
            var refused = PlayScript(h, s.Id, null, out var refusal);
            var err = refusal ?? refused?.Error;
            var uncastB = err?.Detail?.Extra != null && err.Detail.Extra.TryGetValue("uncast", out var ub) ? ub as string[] : null;
            Check("JB2: a play while b's node is gone refuses b as uncast (bad_args, detail uncast [b])", err != null && err.Code == BridgeCodes.BadArgs && uncastB != null && uncastB.SequenceEqual(new[] { "b" }), err?.ToString());
            h.Session.Commands.Undo();   // the delete
            h.Rt.ApplyNow();
            h.Frames(2);
            var r = PlayOk(h, s.Id, null, "JB2 after undo");
            Check("JB2: undo of the delete restores the role (cast to the same node, its track active again)", r != null && r.Roles["b"] == f.B && tlp.Player.CountActiveTracks() == 2);
        }
        finally { f.Dispose(); }
    }
}
