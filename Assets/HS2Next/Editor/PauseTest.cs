// PauseTest (Phase 3.6 package K): the scene clock and the global pause, headless,
// edit mode, ManualStepping (SceneRuntime.Tick), on the SFW targets of Tools/targets.json (female BR-Chan, male HERO107)
// with physics, blink and look, an animated vanilla item (957, isAnime), a route with a rider item and a map-script stand-in
// (a tick part with its own time and a UVScroll-like value).
//   K1 pause → 100 Ticks → every transform under the nodes root, every blend-shape weight, the stand-in's values and the
//      clock's time unchanged (bit-equal); the stages still ran (step counts advance)
//   K2 while paused: an FK edit (female) and an IK target move (male) are visible after one Tick, everything else unchanged
//      (bit-equal)
//   K3 determinism: "30 frames, pause, step 120 × 1/60, resume, 10 frames" = "160 frames of 1/60" bit for bit; the same with
//      5 paused Ticks before the step; two runs bit-equal
//   K4 chara.motion paused composes with the global pause (paused if either)
//   K5 scene.new / scene.load reset the clock (running, time 0); the pause is not saved (the scene file has no pause)
//   K6 bridge: scene.pause read / set / shaderTime, scene.step shapes and errors (running → unavailable, n / dt ranges), steps
//      advance the time by n × dt exactly (as double sums), no history entry, the document unchanged
//   K7 measurements (R-M4): Step(600 × 1/60) with two characters (ms), pause / resume cost, DeltaSource overhead
// Run: -executeMethod PauseTest.Run [-only K1+K3]; report Spike/out/pause/pause_report.txt.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.Motion;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static partial class PauseTest
{
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "pause");
    static StringBuilder rep;
    static int passes, fails, warns;
    static string current = "";
    static readonly Dictionary<string, (int pass, int fail)> perTest = new Dictionary<string, (int, int)>();
    static readonly List<string> measurements = new List<string>();
    static string[] only;
    static RuntimeServices services;
    static string femaleCard, maleCard;
    static readonly ExecuteOptions User = new ExecuteOptions { Source = CommandSource.User };
    const ulong Seed = 0x9a05e0c10c4UL;
    const float Dt = 1f / 60f;

    static string Arg(string name)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    static void Info(string s) => rep.AppendLine("   " + s);
    static void Warn(string s) { warns++; rep.AppendLine($"WARN  [{current}] {s}"); }
    static void Measure(string s) { measurements.Add($"[{current}] {s}"); Info("MEASURE " + s); }

    static bool Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  [{current}] {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
        perTest.TryGetValue(current, out var v);
        perTest[current] = ok ? (v.pass + 1, v.fail) : (v.pass, v.fail + 1);
        return ok;
    }

    static void Guard(string id, string title, Action a)
    {
        if (only != null && !only.Contains(id)) return;
        current = id;
        rep.AppendLine();
        rep.AppendLine($"== {id} {title}");
        try { a(); }
        catch (Exception e) { Check("exception", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
    }

    public static void Run()
    {
        rep = new StringBuilder(); passes = fails = warns = 0; perTest.Clear(); measurements.Clear();
        var o = Arg("-only");
        only = o == null ? null : o.Split(',', '+').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        Directory.CreateDirectory(OutDir);
        var sw = Stopwatch.StartNew();
        var head = new StringBuilder();
        try
        {
            services = SceneRuntimeTest.NewServices(head);
            var t = TestTargets.Load();
            femaleCard = TestTargets.Abs(t.visual.female);
            maleCard = TestTargets.Abs(t.visual.male);
            rep.Insert(0, $"Pause test (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType})\n" + head);
            Guard("K1", "pause holds every scene value (100 Ticks bit-equal)", K1);
            Guard("K2", "edits while paused are visible after one Tick, the rest unchanged", K2);
            Guard("K3", "pause, step 120, resume = 120 frames; two runs bit-equal", K3);
            Guard("K4", "chara.motion paused composes with the global pause", K4);
            Guard("K5", "scene.new / scene.load reset the clock", K5);
            Guard("K6", "scene.pause / scene.step through the bridge", K6);
            Guard("K7", "costs (R-M4)", K7);
        }
        catch (Exception e) { current = "run"; Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        var sum = new StringBuilder();
        sum.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL ({warns} WARN, {sw.Elapsed.TotalSeconds:F0} s)");
        foreach (var kv in perTest.OrderBy(k => k.Key, StringComparer.Ordinal)) sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail");
        if (measurements.Count > 0) { sum.AppendLine("measurements:"); foreach (var m in measurements) sum.AppendLine("  " + m); }
        rep.Insert(0, sum.ToString());
        File.WriteAllText(Path.Combine(OutDir, "pause_report.txt"), rep.ToString(), new UTF8Encoding(false));
        HS2Next.Motion.Face.FaceSetup.Seed = null;
        services?.Catalog?.UnloadAll();
        Debug.Log($"PAUSE TEST DONE: {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }

    // ================================================================ fixture

    static string UserDataDir => Path.Combine(OutDir, "userdata");

    /// <summary>A map-script stand-in: its own time (sum of the tick deltas) and a UVScroll-like value repeat(t × 0.37, 1).</summary>
    sealed class StandIn : ISceneRuntimePart, ISceneTickPart
    {
        public double T;
        public float Offset;
        public int Ticks;
        public void Attach(SceneRuntime r) { }
        public void OnChanged(SceneChangeBatch b) { }
        public void Apply() { }
        public void LateApply() { }
        public void Clear() { }
        public void OnLoadedRealized() { }
        public void Tick(float dt)
        {
            Ticks++;
            T += dt;
            Offset = Mathf.Repeat((float)T * 0.37f, 1f);
        }
    }

    sealed class Fixture
    {
        public AppCore App;
        public StudioOps Ops => App.Ops;
        public SceneRuntime Rt => App.Runtime;
        public StandIn Script;
        public string F, M, Item, Route, Rider;
        public void Dispose() { try { App?.Dispose(); } catch (Exception e) { Info("dispose: " + e.Message); } }
    }

    static AppCore Host(ulong seed)
    {
        var s = AppSettings.Defaults(Array.Empty<string>());
        s.Content = CardResolveTest.Settings;
        s.UserData = UserDataDir;
        s.Bridge.OutputDir = Path.Combine(OutDir, "out");
        var o = services.NewSessionOptions();
        o.Ids = new CountingIdSource(seed);
        return AppCore.ForTests(services, s, null, o);
    }

    /// <summary>BR-Chan (idle, look pattern), HERO107 (idle), item 957 (rotating, isAnime), a route with item 399 on it, the stand-in.</summary>
    static Fixture Make(ulong seed = Seed)
    {
        HS2Next.Motion.Face.FaceSetup.Seed = 0x5eed;   // the blink / mouth draws of every character (reset in Run)
        var f = new Fixture { App = Host(seed) };
        var ops = f.Ops;
        f.Rt.SetManualStepping(true);
        f.Script = new StandIn();
        f.Rt.AddPart(f.Script);
        var idle = services.Db.FindAnime(19, 0, 0);
        f.F = ops.AddChara(femaleCard, null, null, -1, null, User).NewIds.FirstOrDefault();
        f.M = ops.AddChara(maleCard, null, null, -1, null, User).NewIds.FirstOrDefault();
        ops.SetTransform(new[] { f.M }, new Vector3(6, 0, 0), null, null, User);
        if (idle != null) ops.SetAnimation(new[] { f.F, f.M }, idle, 0f, 1f, null, null, User);
        ops.SetLook(new[] { f.F }, 0, 4, User);
        f.Item = ops.AddItem(ContentId.Vanilla(957), null, null, -1, User).NewIds.FirstOrDefault();
        ops.SetTransform(new[] { f.Item }, new Vector3(-4, 0, 0), null, null, User);
        var rr = f.App.Session.AddRoute(null, null, -1, User);
        f.Route = rr != null && rr.Ok ? rr.NewId : null;
        if (f.Route != null) f.Rider = ops.AddItem(ContentId.Vanilla(399), f.Route, null, -1, User).NewIds.FirstOrDefault();
        f.Rt.ApplyNow();
        return f;
    }

    static void Frames(Fixture f, int n, float dt = Dt)
    {
        for (int i = 0; i < n; i++) f.Rt.Tick(dt);
    }

    // ================================================================ snapshots

    sealed class Snap
    {
        public readonly List<string> Names = new List<string>();
        public readonly List<int> Bits = new List<int>();
        public readonly List<int> Owner = new List<int>();   // index into Owners
        public readonly List<string> Owners = new List<string>();
        public double ClockTime, ScriptT;
        public float ScriptOffset;
    }

    static void Add(Snap s, string name, float v, int owner) { s.Names.Add(name); s.Bits.Add(BitConverter.SingleToInt32Bits(v)); s.Owner.Add(owner); }

    /// <summary>Every transform under the nodes root (local pose, pre-order), every blend-shape weight, the stand-in and the clock.</summary>
    static Snap Take(Fixture f)
    {
        var s = new Snap();
        void Walk(Transform t, string path, int owner)
        {
            var h = t.GetComponent<SceneNodeHolder>();
            if (h != null && h.NodeId != null)
            {
                s.Owners.Add(h.NodeId);
                owner = s.Owners.Count - 1;
            }
            t.GetLocalPositionAndRotation(out var p, out var q);
            var sc = t.localScale;
            Add(s, path + ".px", p.x, owner); Add(s, path + ".py", p.y, owner); Add(s, path + ".pz", p.z, owner);
            Add(s, path + ".qx", q.x, owner); Add(s, path + ".qy", q.y, owner); Add(s, path + ".qz", q.z, owner); Add(s, path + ".qw", q.w, owner);
            Add(s, path + ".sx", sc.x, owner); Add(s, path + ".sy", sc.y, owner); Add(s, path + ".sz", sc.z, owner);
            var smr = t.GetComponent<SkinnedMeshRenderer>();
            if (smr && smr.sharedMesh)
                for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++) Add(s, path + ".bs" + i, smr.GetBlendShapeWeight(i), owner);
            for (int i = 0; i < t.childCount; i++) { var c = t.GetChild(i); Walk(c, path + "/" + c.name, owner); }
        }
        s.Owners.Add("(root)");
        Walk(f.Rt.NodesRoot, "Nodes", 0);
        s.ClockTime = f.Rt.Clock.Time;
        s.ScriptT = f.Script.T;
        s.ScriptOffset = f.Script.Offset;
        return s;
    }

    /// <summary>Indices whose bits differ (null when the shapes differ).</summary>
    static List<int> Diff(Snap a, Snap b)
    {
        if (a.Bits.Count != b.Bits.Count) return null;
        var l = new List<int>();
        for (int i = 0; i < a.Bits.Count; i++) if (a.Bits[i] != b.Bits[i]) l.Add(i);
        return l;
    }

    static string Describe(Snap a, Snap b, List<int> d, int max = 6)
    {
        if (d == null) return $"shape differs ({a.Bits.Count} vs {b.Bits.Count} values)";
        if (d.Count == 0) return $"{a.Bits.Count} values equal";
        var owners = d.Select(i => a.Owners[a.Owner[i]]).Distinct().ToList();
        var leaves = d.Select(i => { var n = a.Names[i]; int k = n.LastIndexOf('/'); return k >= 0 ? n.Substring(k + 1) : n; })
            .Select(n => { int k = n.LastIndexOf('.'); return k > 0 ? n.Substring(0, k) : n; }).Distinct().Take(24).ToList();
        return $"{d.Count} of {a.Bits.Count} values differ (nodes {string.Join(", ", owners)}; transforms {string.Join(", ", leaves)}), e.g. " +
               string.Join("; ", d.Take(max).Select(i => a.Names[i] + " " + BitConverter.Int32BitsToSingle(a.Bits[i]).ToString("R") + " vs " + BitConverter.Int32BitsToSingle(b.Bits[i]).ToString("R")));
    }

    static bool OnlyOwners(Snap a, List<int> d, params string[] ids) => d != null && d.All(i => ids.Contains(a.Owners[a.Owner[i]]));

    // ================================================================ K1

    static void K1()
    {
        var f = Make();
        try
        {
            Frames(f, 30);
            var running = Take(f);
            Frames(f, 2);
            var running2 = Take(f);
            var moving = Diff(running, running2);
            Check("the fixture moves while running (2 frames change bones, blend shapes, the item, the stand-in)", moving != null && moving.Count > 0 && running2.ScriptT > running.ScriptT, Describe(running, running2, moving, 3));
            Check("SetPaused(true): paused, Time.timeScale untouched", f.Rt.SetPaused(true) && f.Rt.Paused && Time.timeScale == 1f, "timeScale " + Time.timeScale);
            var c0 = f.Rt.FindChara(f.F).Motion.StepCount;
            var s0 = Take(f);
            Frames(f, 100);
            var s1 = Take(f);
            var d = Diff(s0, s1);
            Check("100 paused Ticks: every transform and blend-shape weight bit-equal", d != null && d.Count == 0, Describe(s0, s1, d));
            if (d != null && d.Count > 0) StageProbe(f, s0, d);
            Check("the clock's time, the stand-in's time and value unchanged", s1.ClockTime == s0.ClockTime && s1.ScriptT == s0.ScriptT && s1.ScriptOffset == s0.ScriptOffset && f.Rt.Clock.FrameDt == 0f,
                $"clock {s0.ClockTime:R} → {s1.ClockTime:R}, script {s0.ScriptT:R} → {s1.ScriptT:R}");
            Check("the stages still ran (step count +100) and the stand-in was ticked (dt 0)", f.Rt.FindChara(f.F).Motion.StepCount == c0 + 100 && f.Script.Ticks >= 132, "steps " + (f.Rt.FindChara(f.F).Motion.StepCount - c0) + ", ticks " + f.Script.Ticks);
            Check("CharaMotion.DeltaSource of the scene characters is the clock (0 while paused)", f.Rt.FindChara(f.F).Motion.DeltaSource != null && f.Rt.FindChara(f.F).Motion.DeltaSource() == 0f);
            f.Rt.SetPaused(false);
            Frames(f, 1);
            var s2 = Take(f);
            Check("resume: the next frame moves again", Diff(s1, s2)?.Count > 0 && s2.ScriptT > s1.ScriptT);
        }
        finally { f.Dispose(); }
    }

    /// <summary>Diagnostics: which stage of a dt-0 step changes the first differing transform (stages stepped one by one).</summary>
    static void StageProbe(Fixture f, Snap s0, List<int> d)
    {
        string name = s0.Names[d[0]];
        string leaf = name.Substring(name.LastIndexOf('/') + 1);
        leaf = leaf.Substring(0, leaf.LastIndexOf('.'));
        string owner = s0.Owners[s0.Owner[d[0]]];
        var c = f.Rt.FindChara(owner);
        if (c == null || !c.Motion) return;
        Transform t = null;
        foreach (var x in c.Root.GetComponentsInChildren<Transform>(true)) if (x.name == leaf) { t = x; break; }
        if (!t) return;
        var db = c.Motion.Get<HS2Next.Motion.DynamicBones.DynamicBoneStage>();
        var chain = db?.Chains.FirstOrDefault(ch => ch.Root == t || (ch.Root && t.IsChildOf(ch.Root)));
        Info($"probe {leaf}: chain {(chain == null ? "none" : chain + ", active " + chain.Active + ", distant " + chain.DistantDisabled)}");
        var frame = new MotionFrame(0f, 0f, c.Motion.Time, c.Motion.StepCount);
        for (int k = 0; k < 2; k++)
            foreach (var s in c.Motion.Stages)
            {
                if (!s.Enabled) continue;
                var before = t.localRotation;
                if (chain != null && s.Name == "dynbone")
                    Info($"probe pass {k} before dynbone: x {string.Join(" ", Enumerable.Range(0, chain.ParticleCount).Select(i => chain.Position(i).ToString("F6")))}, owner {(chain.Owner ? chain.Owner.position.ToString("F6") : "-")}, locals {string.Join(" ", Enumerable.Range(0, chain.ParticleCount).Select(i => chain.ParticleTransform(i) ? chain.ParticleTransform(i).localPosition.ToString("F6") : "-"))}");
                s.Step(frame);
                if (t.localRotation != before) Info($"probe {leaf} pass {k}: stage {s.Name} (phase {s.Phase}) changed it {before.ToString("F6")} → {t.localRotation.ToString("F6")}");
            }
    }

    // ================================================================ K2

    static void K2()
    {
        var f = Make();
        try
        {
            Frames(f, 20);
            f.Rt.SetPaused(true);
            Frames(f, 2);
            var s0 = Take(f);
            // FK on the female: one bone rotated
            var r1 = f.Ops.SetFkEnabled(f.F, true, User);
            Frames(f, 1);
            var s1 = Take(f);
            var d1 = Diff(s0, s1);
            Check("FK on while paused: the female changes after one Tick, only the female", r1.Ok && d1 != null && d1.Count > 0 && OnlyOwners(s0, d1, f.F), r1 + "; " + Describe(s0, s1, d1, 3));
            var bone = "cf_J_ArmUp00_L";
            var r2 = f.Ops.SetFkBones(f.F, new Dictionary<string, Vec3> { [bone] = new Vec3(0, 0, -40) }, User);
            Frames(f, 1);
            var s2 = Take(f);
            var d2 = Diff(s1, s2);
            bool shoulder = d2 != null && d2.Any(i => s1.Names[i].Contains("/" + bone + ".q"));
            Check("an FK bone edit while paused is visible after one Tick (" + bone + "), only the female changes", r2.Ok && shoulder && OnlyOwners(s1, d2, f.F), r2 + "; " + Describe(s1, s2, d2, 3));
            // IK on the male and a hand target moved
            var r3 = f.Ops.SetIkEnabled(f.M, true, User);
            Frames(f, 1);
            var s3 = Take(f);
            var r4 = f.Ops.SetIkTargets(f.M, new Dictionary<IkTargetId, IkTargetEdit> { [IkTargetId.LeftHand] = new IkTargetEdit(new Vector3(3f, 9f, 3f), null) }, IkSpace.Local, User);
            Frames(f, 1);
            var s4 = Take(f);
            var d4 = Diff(s3, s4);
            bool hand = d4 != null && d4.Any(i => s3.Names[i].Contains("cf_J_Hand_L.") || s3.Names[i].Contains("cm_J_Hand_L."));
            Check("an IK target move while paused is visible after one Tick (the left hand moved), only the male changes", r3.Ok && r4.Ok && hand && OnlyOwners(s3, d4, f.M), r3 + " / " + r4 + "; " + Describe(s3, s4, d4, 3));
            Frames(f, 10);
            var s5 = Take(f);
            var d5 = Diff(s4, s5);
            Check("after the edits: 10 more paused Ticks change nothing", d5 != null && d5.Count == 0, Describe(s4, s5, d5));
            Check("the clock did not advance", f.Rt.Clock.Time == s0.ClockTime);
        }
        finally { f.Dispose(); }
    }

    // ================================================================ K3

    static Snap RunPaused(int pausedTicks)
    {
        var f = Make();
        try
        {
            Frames(f, 30);
            f.Rt.SetPaused(true);
            Frames(f, pausedTicks);
            bool ok = f.Rt.Step(120, Dt);
            if (!ok) Check("Step(120) accepted", false);
            f.Rt.SetPaused(false);
            Frames(f, 10);
            return Take(f);
        }
        finally { f.Dispose(); }
    }

    static Snap RunFrames(int n)
    {
        var f = Make();
        try
        {
            Frames(f, n);
            return Take(f);
        }
        finally { f.Dispose(); }
    }

    static void K3()
    {
        var a = RunPaused(0);
        var b = RunFrames(160);
        var d = Diff(a, b);
        Check("30 frames, pause, Step(120 × 1/60), resume, 10 frames = 160 frames: every value bit-equal", d != null && d.Count == 0, Describe(a, b, d));
        Check("the clock time and the stand-in time equal (the same float sums)", a.ClockTime == b.ClockTime && a.ScriptT == b.ScriptT, $"clock {a.ClockTime:R} vs {b.ClockTime:R}, script {a.ScriptT:R} vs {b.ScriptT:R}");
        var a5 = RunPaused(5);
        var d5 = Diff(a5, b);
        Check("the same with 5 paused Ticks before the step (stages run with dt 0): bit-equal", d5 != null && d5.Count == 0, Describe(a5, b, d5));
        var b2 = RunFrames(160);
        var db = Diff(b, b2);
        Check("two runs of 160 frames bit-equal", db != null && db.Count == 0, Describe(b, b2, db));
        var a2 = RunPaused(0);
        var da = Diff(a, a2);
        Check("two runs of pause / step / resume bit-equal", da != null && da.Count == 0, Describe(a, a2, da));
    }

    // ================================================================ K4

    static void K4()
    {
        var f = Make();
        try
        {
            Frames(f, 10);
            var r = Call(f.App, new { cmd = "chara.motion", key = f.F, paused = true });
            Check("chara.motion paused true", r.Value<bool?>("ok") == true && r.Value<bool?>("paused") == true, r.ToString(Formatting.None));
            var s0 = Take(f);
            Frames(f, 5);
            var s1 = Take(f);
            var d = Diff(s0, s1);
            Check("running scene, the female paused per node: only the others move", d != null && d.Count > 0 && !d.Any(i => s0.Owners[s0.Owner[i]] == f.F), Describe(s0, s1, d?.Where(i => s0.Owners[s0.Owner[i]] == f.F).ToList(), 6));
            f.Rt.SetPaused(true);
            f.Rt.SetPaused(false);
            var s2 = Take(f);
            Frames(f, 5);
            var s3 = Take(f);
            var d2 = Diff(s2, s3);
            Check("after a global pause / resume the per-node pause still holds the female", d2 != null && !d2.Any(i => s2.Owners[s2.Owner[i]] == f.F), Describe(s2, s3, d2, 3));
            f.Rt.SetPaused(true);
            Call(f.App, new { cmd = "chara.motion", key = f.F, paused = false });
            var s4 = Take(f);
            Frames(f, 5);
            var s5 = Take(f);
            var d3 = Diff(s4, s5);
            Check("per-node pause off, global pause on: nothing moves (paused if either)", d3 != null && d3.Count == 0, Describe(s4, s5, d3));
            f.Rt.SetPaused(false);
            Frames(f, 2);
            var s6 = Take(f);
            var d4 = Diff(s5, s6);
            Check("both off: the female moves again", d4 != null && d4.Any(i => s5.Owners[s5.Owner[i]] == f.F), Describe(s5, s6, d4, 3));
        }
        finally { f.Dispose(); }
    }

    // ================================================================ K5

    static void K5()
    {
        var f = Make();
        try
        {
            Frames(f, 10);
            f.Rt.SetPaused(true);
            f.Rt.Step(5, Dt);
            var save = Call(f.App, new { cmd = "scene.save", name = "pause_k5", overwrite = true, thumb = "placeholder" });
            Check("scene.save while paused", save.Value<bool?>("ok") == true, save.ToString(Formatting.None));
            Check("still paused after the save (a save does not change the pause)", f.Rt.Paused);
            var path = save.Value<string>("path");
            bool noPause = path != null && File.Exists(path) && !Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("\"paused\"");
            Check("the scene file has no pause member", noPause, path);
            var load = Call(f.App, new { cmd = "scene.load", name = "pause_k5" });
            // AppCore.Frame begins clock frames in edit mode (2a2491d): the frame AppCore.Call pumps after the reset counts
            Check("scene.load: running, time 0 + the one frame AppCore.Call pumps, steps 0", load.Value<bool?>("ok") == true && !f.Rt.Paused && f.Rt.Clock.Time <= 1f / 60f + 1e-9 && f.Rt.StepsTaken == 0, $"paused {f.Rt.Paused}, time {f.Rt.Clock.Time}, steps {f.Rt.StepsTaken}");
            f.Rt.SetManualStepping(true);
            Frames(f, 3);
            f.Rt.SetPaused(true);
            var nw = Call(f.App, new { cmd = "scene.new" });
            Check("scene.new: running, time 0 + the one frame AppCore.Call pumps", nw.Value<bool?>("ok") == true && !f.Rt.Paused && f.Rt.Clock.Time <= 1f / 60f + 1e-9, $"paused {f.Rt.Paused}, time {f.Rt.Clock.Time}");
        }
        finally { f.Dispose(); }
    }

    // ================================================================ K6

    static JObject Call(AppCore app, object req)
    {
        var r = JObject.Parse(app.Call(req is string s ? s : JsonConvert.SerializeObject(req)));
        if (r.Value<bool?>("ok") != true) Info("call " + JsonConvert.SerializeObject(req) + " -> " + r.ToString(Formatting.None));
        return r;
    }

    static string Doc(AppCore app)
    {
        using (var h = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(h.ComputeHash(DocClone.Bytes(app.Session.ExportDocument()))).Replace("-", "");
    }

    static string Code(JObject r) => r.Value<bool?>("ok") == true ? "ok" : (string)r["code"] ?? (string)r["error"]?["code"];

    static void K6()
    {
        var f = Make();
        try
        {
            Frames(f, 5);
            int undo0 = f.App.Session.Commands.UndoList.Count;
            var doc0 = Doc(f.App);
            var rd = Call(f.App, new { cmd = "scene.pause" });
            var res = rd;
            Check("scene.pause (read): {paused false, time, frame, steps, shaderTime all}", rd.Value<bool?>("ok") == true && res?.Value<bool>("paused") == false && res["time"] != null && res["frame"] != null && res["steps"] != null && (string)res["shaderTime"] == "all", rd.ToString(Formatting.None));
            var st0 = Call(f.App, new { cmd = "scene.step", n = 2 });
            Check("scene.step while running → unavailable (pause first)", Code(st0) == "unavailable", st0.ToString(Formatting.None));
            var p = Call(f.App, new { cmd = "scene.pause", paused = true });
            Check("scene.pause {paused: true}", p.Value<bool?>("paused") == true && f.Rt.Paused);
            double t0 = f.Rt.Clock.Time;
            var st = Call(f.App, new { cmd = "scene.step", n = 30 });
            double expect = t0;
            for (int i = 0; i < 30; i++) expect += Dt;
            var sr = st;
            Check("scene.step {n: 30}: time + 30 × 1/60 (the clock's double sum), steps 30, ms", st.Value<bool?>("ok") == true && f.Rt.Clock.Time == expect && sr?.Value<long>("steps") == 30 && sr["ms"] != null,
                $"time {f.Rt.Clock.Time:R} expected {expect:R}; {st.ToString(Formatting.None)}");
            Check("0.5 s within float rounding", Math.Abs(f.Rt.Clock.Time - t0 - 0.5) < 1e-5);
            foreach (var (args, what) in new[] { ((object)new { cmd = "scene.step", n = 0 }, "n 0"), (new { cmd = "scene.step", n = 10001 }, "n 10001"),
                         (new { cmd = "scene.step", dt = 0f }, "dt 0"), (new { cmd = "scene.step", dt = 1.5f }, "dt 1.5"), (new { cmd = "scene.pause", shaderTime = "sometimes" }, "shaderTime sometimes") })
            {
                var e = Call(f.App, args);
                Check("bad_args: " + what, Code(e) == "bad_args", e.ToString(Formatting.None));
            }
            var sc = Call(f.App, new { cmd = "scene.pause", shaderTime = "captures" });
            Check("scene.pause {shaderTime: captures} sets the scope", (string)sc["shaderTime"] == "captures" && HS2Next.SceneShaderTime.Scope == HS2Next.SceneShaderTimeScope.Captures);
            Call(f.App, new { cmd = "scene.pause", shaderTime = "all" });
            Check("the shader-time source is this runtime's clock", HS2Next.SceneShaderTime.Source == f.Rt.Clock);
            var rs = Call(f.App, new { cmd = "scene.pause", paused = false });
            Check("scene.pause {paused: false}", rs.Value<bool?>("paused") == false && !f.Rt.Paused);
            Check("no history entry", f.App.Session.Commands.UndoList.Count == undo0, f.App.Session.Commands.UndoList.Count + " vs " + undo0);
            var doc1 = Doc(f.App);
            Check("the document bytes unchanged by pause / step", doc0 == doc1);
            Check("Step refused in code while running", !f.Rt.Step(1, Dt));
            f.Rt.SetPaused(true);
            Check("Step refused for n 0, n 10001, dt 0, dt 2", !f.Rt.Step(0, Dt) && !f.Rt.Step(10001, Dt) && !f.Rt.Step(1, 0f) && !f.Rt.Step(1, 2f));
        }
        finally { f.Dispose(); }
    }

    // ================================================================ K7

    static void K7()
    {
        var f = Make();
        try
        {
            Frames(f, 10);
            var sw = Stopwatch.StartNew();
            f.Rt.SetPaused(true);
            double pauseMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            f.Rt.Step(600, Dt);
            double stepMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            f.Rt.SetPaused(false);
            double resumeMs = sw.Elapsed.TotalMilliseconds;
            Measure($"R-M4 Step(600 × 1/60) with two characters, two items and a route, edit mode without a map: {stepMs:F0} ms ({stepMs / 600:F2} ms per step; target ≤ 3 s on map 10)");
            Check("600 steps ≤ 3 s", stepMs <= 3000, stepMs.ToString("F0") + " ms");
            Measure($"R-M4 pause {pauseMs:F3} ms, resume {resumeMs:F3} ms (edit mode: no particles to freeze)");
            var m = f.Rt.FindChara(f.F).Motion;
            Func<float> src = m.DeltaSource;
            float acc = 0;
            sw.Restart();
            for (int i = 0; i < 1000000; i++) acc += src();
            double perCall = sw.Elapsed.TotalMilliseconds * 1e3 / 1e6;
            Measure($"R-M4 CharaMotion.DeltaSource: {perCall:F4} µs per call (1e6 calls; once per character per frame){(acc < 0 ? "" : "")}");
        }
        finally { f.Dispose(); }
    }
}
