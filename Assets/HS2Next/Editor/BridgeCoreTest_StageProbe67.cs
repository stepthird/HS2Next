// BridgeCoreTest group C48 (Phase 6.7, package A): the character stages on BR-Chan, headless. RY2 phase order, RY3 a rebuild, RY4 off on
// dispose and unload, RY5 a throw is a fault, RY6 skipped in a probe, RY7 inside a pass, and a phase out of range refused. First through
// a plugin's services with stages of this process, so a stage can share a list with the host's own stages at -98, 410 and 956; then the
// test plugin stp3.test.stageprobe, built with dotnet against the Api as a plugin is and loaded by the real plugin host, for the binding,
// the fault threshold, a disable and an enable. Every row that compares counts prints the count that shows the stage ran.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HS2Next.Api.Unity;
using HS2Next.App.Plugins;
using HS2Next.App.Plugins.Engine;
using HS2Next.Content;
using HS2Next.Motion;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class BridgeCoreTest
{
    const string StageProbeGuid = "stp3.test.stageprobe", StGuid = "stp3.test.stages", StOwner = "c48";
    const float StDt = 1f / 60f;

    /// <summary>A stage of this process: writes its name into a shared list, and can act or throw inside its Step.</summary>
    sealed class LogStage : IMotionStage
    {
        readonly List<string> log;
        public int Steps, Resets;
        public Action OnStep;
        public bool Throw;

        public LogStage(int phase, string name, List<string> log)
        {
            Phase = (MotionPhase)phase;
            Name = name;
            this.log = log;
        }

        public MotionPhase Phase { get; }
        public string Name { get; }
        public bool Enabled { get; set; } = true;

        public void Step(in MotionFrame frame)
        {
            Steps++;
            log?.Add(Name);
            OnStep?.Invoke();
            if (Throw) throw new InvalidOperationException("thrown by the test");
        }

        public void Reset() => Resets++;
    }

    static bool StOn(CharaMotion m, string name) => m.Stages.Any(s => s.Name == name);

    static string StFull(string name) => StGuid + "/" + name;

    static JObject StRow(Ctx c, string key, string name) =>
        (c.Call(new { cmd = "chara.motion", key })["stages"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["name"] == name);

    static void C48(Ctx c)
    {
        var plugin = new List<string>();
        var ctx = PxFemale(c, "C48", out string key);
        if (ctx == null) return;
        var rt = c.App.Runtime;
        var r = rt.FindChara(key);
        c.Check(CharaStageHost.Of(rt) == null, "C48: a scene that adds no stage has no host", rt.Parts.Count + " parts");
        var guard = new PluginGuard(StGuid, l => plugin.Add(l), 100000);
        var s = new PluginEngineServices(StGuid, guard, l => plugin.Add(l), l => plugin.Add(l), () => c.App?.Runtime, null);
        var stages = s.Stages;
        c.Check(stages != null && ReferenceEquals(stages, s.Stages), "C48: Stages is never null and is one view", "");
        C48Args(c, stages, key);

        // ---- RY2 phase order: the host's stages at -98, 410, 956 through the same host, the plugin's added in reverse order
        var log = new List<string>();
        var host = CharaStageHost.For(rt);
        int hostMakes = 0;
        var hostEntry = host.Add(key, StOwner, q =>
        {
            hostMakes++;
            return new IMotionStage[] { new LogStage(-98, "host.restore", log), new LogStage(410, "host.write", log), new LogStage(956, "host.end", log) };
        });
        c.Check(host.Listening && host.Count == 1 && hostMakes == 1 && host.StagesOf(key, StOwner).Count == 3, "RY2: the factory ran at once, three stages on the motion at phases no plugin may take",
            "makes " + hostMakes + ", entries " + host.Count + ", phases " + string.Join(" ", host.StagesOf(key, StOwner).Select(x => (int)x.Phase)));
        int lateMakes = 0, midMakes = 0;
        GameObject lastRoot = null;
        LogStage mid = null;
        stages.Add(key, root => { lateMakes++; return new LogStage(940, "late", log); });
        var midEntry = stages.Add(key, root => { midMakes++; lastRoot = root; return mid = new LogStage(420, "mid", log); });
        stages.Add(key, root => new LogStage(-90, "early", log));
        var phases = r.Motion.Stages.Select(x => (int)x.Phase).ToList();
        c.Check(phases.Zip(phases.Skip(1), (a, b) => a <= b).All(x => x), "RY2: the stage list is in phase order", string.Join(" ", phases));
        r.Motion.Step(StDt);
        const string want = "host.restore early host.write mid late host.end";
        c.Check(string.Join(" ", log) == want, "RY2: a step runs them in phase order, the plugin's between the host's", string.Join(" ", log));
        c.Check(mid != null && mid.Steps == 1 && lastRoot == r.Root, "RY2: the plugin's stage ran, its factory was given the character's root", "steps " + mid?.Steps);
        var row = StRow(c, key, StFull("mid"));
        c.Check(row != null && (int?)row["phase"] == 420 && (string)row["owner"] == StGuid && (bool?)row["enabled"] == true, "RY2: chara.motion lists it with the plugin's guid as owner", Ctx.Short(row?.ToString()));
        c.Check((string)StRow(c, key, "host.write")?["owner"] == "motion", "RY2: a host stage keeps its owner", Ctx.Short(StRow(c, key, "host.write")?.ToString()));

        // ---- refused phases
        C48Range(c, stages, guard, r.Motion, key);

        // ---- a flush keeps the stages; a stage switched off through chara.motion stays off across a rebuild
        int before = midMakes;
        var oldMid = mid;
        c.Ok(new { cmd = "chara.edit.set", key, set = new Dictionary<string, object> { ["coord.clothes.top.colors[0].gloss"] = 0.37 } }, "RY3: a flush");
        rt.ApplyNow();
        c.Check(Math.Abs(((float?)C30Value(c, key, "coord.clothes.top.colors[0].gloss") ?? 0) - 0.37f) < 1e-6f && midMakes == before && r.Motion.Stages.Any(x => x is PluginStageAdapter a && a.Name == StFull("mid")),
            "RY3: a flush runs no factory, the stage stays", "makes " + midMakes);

        c.Ok(new { cmd = "chara.motion", key, stages = new Dictionary<string, object> { [StFull("mid")] = false, ["host.write"] = false } }, "RY3: chara.motion switches a plugin's stage and a host stage off");
        int steps0 = mid.Steps;
        r.Motion.Step(StDt);
        c.Check(mid.Steps == steps0 && (bool?)StRow(c, key, StFull("mid"))?["enabled"] == false, "RY3: a stage switched off does not run", "steps " + mid.Steps);

        // ---- RY3 a rebuild
        var oldMotion = r.Motion;
        var oldRoot = r.Root;
        var oldHips = r.Context.BodyBones["cf_J_Hips"];
        int lateBefore = lateMakes, hostBefore = hostMakes;
        before = midMakes;
        c.Check(s.RequestRebuild(key), "RY3: a rebuild is requested", "");
        rt.ApplyNow();
        c.Check(!oldHips && !oldMotion && oldRoot != r.Root, "RY3: the old bones and the old motion are gone", "hips alive " + (bool)oldHips + ", motion alive " + (bool)oldMotion);
        c.Check(midMakes == before + 1 && lateMakes == lateBefore + 1 && hostMakes == hostBefore + 1 && lastRoot == r.Root && lastRoot,
            "RY3: every factory ran once more, with the new root", "makes " + midMakes + ", " + lateMakes + ", " + hostMakes);
        c.Check(!ReferenceEquals(mid, oldMid) && (bool?)StRow(c, key, StFull("mid"))?["enabled"] == false && (bool?)StRow(c, key, "host.write")?["enabled"] == false && (bool?)StRow(c, key, "host.restore")?["enabled"] == true,
            "RY3: a new stage object, what was off stays off", Ctx.Short(StRow(c, key, StFull("mid"))?.ToString()));
        c.Ok(new { cmd = "chara.motion", key, stages = new Dictionary<string, object> { [StFull("mid")] = true, ["host.write"] = true } }, "RY3: on again");
        log.Clear();
        r.Motion.Step(StDt);
        c.Check(string.Join(" ", log) == want, "RY3: the new body runs them in phase order", string.Join(" ", log));
        before = midMakes;
        c.Ok(new { cmd = "chara.change", key, path = c.FemaleRel, sex = "female" }, "RY3: chara.change");
        rt.ApplyNow();
        c.Check(midMakes == before + 1 && StOn(r.Motion, StFull("mid")) && lastRoot == r.Root, "RY3: chara.change runs the factory, the stage is on the new motion", "makes " + midMakes);

        // ---- RY4 dispose
        var m = r.Motion;
        steps0 = mid.Steps;
        midEntry.Dispose();
        midEntry.Dispose();
        m.Step(StDt);
        c.Check(!StOn(m, StFull("mid")) && mid.Steps == steps0 && StOn(m, StFull("late")), "RY4: dispose takes the stage off at once, a second dispose does nothing, the others stay", "steps " + mid.Steps);
        hostEntry.Dispose();
        c.Check(!StOn(m, "host.write") && !StOn(m, "host.restore") && !StOn(m, "host.end") && host.StagesOf(key, StOwner).Count == 0, "RY4: one dispose takes a host entry's three stages off", "");

        C48Walk(c, m, stages, key, rt);
        C48Throw(c, m, stages, guard, key, rt);
        C48Probe(c, m, stages, key, host);
        C48Pass(c, r, stages, key, rt);
        C48Delete(c, stages, host, s, key);
        C48Scene(c, s, stages, log);
        C48Plugin(c);
    }

    static void C48Args(Ctx c, ICharaStages stages, string key)
    {
        string item = (string)c.Ok(new { cmd = "item.add", no = 399 }, "RY2: an item")["added"]?[0]?["key"];
        string folder = c.App.Session.AddFolder().NewIds[0];
        c.App.Runtime.ApplyNow();
        foreach (var (id, what) in new[] { (item, "an item"), (folder, "a folder"), ("nope", "an unknown id"), (null, "null") })
        {
            bool refused = false;
            try { stages.Add(id, root => new LogStage(420, "x", null)); }
            catch (ArgumentException) { refused = true; }
            c.Check(refused, "RY2: ArgumentException for " + what, id ?? "");
        }
        bool nullMake = false;
        try { stages.Add(key, null); }
        catch (ArgumentNullException) { nullMake = true; }
        c.Check(nullMake, "RY2: ArgumentNullException for no factory", "");
    }

    static void C48Range(Ctx c, ICharaStages stages, PluginGuard guard, CharaMotion m, string key)
    {
        foreach (int phase in new[] { -91, 941, 950, 955, 956, 960, -98, -99, -100 })
        {
            int faults = guard.Total;
            var t = stages.Add(key, root => new LogStage(phase, "out" + phase, null));
            c.Check(!StOn(m, StFull("out" + phase)) && guard.Total == faults + 1, "RY7: phase " + phase + " is refused and counted as a fault", "faults " + faults + " -> " + guard.Total);
            t.Dispose();
        }
        c.Check(guard.ByHook().Keys.Any(k => k.Contains("out-91")), "RY7: the fault names the stage", string.Join(", ", guard.ByHook().Keys.Take(3)));
        foreach (int phase in new[] { -90, 940 })
        {
            var t = stages.Add(key, root => new LogStage(phase, "edge" + phase, null));
            c.Check(StOn(m, StFull("edge" + phase)), "RY7: phase " + phase + " is taken", "");
            t.Dispose();
        }
    }

    /// <summary>A stage that leaves from inside its own Step, against the walk of the stage list.</summary>
    static void C48Walk(Ctx c, CharaMotion m, ICharaStages stages, string key, SceneRuntime rt)
    {
        // what the rule guards against: the bare remove loses the stage behind it for that frame
        var a = new LogStage(430, "control.a", null);
        var b = new LogStage(431, "control.b", null);
        a.OnStep = () => m.Remove(a);
        m.Add(a);
        m.Add(b);
        m.Step(StDt);
        int lost = b.Steps;
        m.Remove(b);
        c.Check(a.Steps == 1 && lost == 0, "RY4: control: a bare remove inside a step skips the next stage", "next stage's steps " + lost);

        IDisposable token = null;
        var self = new LogStage(430, "leaves", null) { OnStep = () => token.Dispose() };
        var next = new LogStage(431, "next", null);
        token = stages.Add(key, root => self);
        var keep = stages.Add(key, root => next);
        m.Step(StDt);
        c.Check(self.Steps == 1 && next.Steps == 1, "RY4: a stage that leaves inside its Step does not cost the next stage its frame", "self " + self.Steps + ", next " + next.Steps);
        m.Step(StDt);
        c.Check(self.Steps == 1 && next.Steps == 2 && !m.Stages.Any(x => x.Name == StFull("leaves") && x.Enabled), "RY4: it does not run again", "self " + self.Steps + ", next " + next.Steps);
        rt.LateApply();
        c.Check(!StOn(m, StFull("leaves")) && StOn(m, StFull("next")), "RY4: it leaves the list at the next LateApply", "");
        keep.Dispose();
    }

    static void C48Throw(Ctx c, CharaMotion m, ICharaStages stages, PluginGuard guard, string key, SceneRuntime rt)
    {
        var bad = new LogStage(433, "throws", null) { Throw = true };
        var next = new LogStage(434, "after", null);
        var e1 = stages.Add(key, root => bad);
        var e2 = stages.Add(key, root => next);
        int faults = guard.Total, errors = m.Errors;
        for (int i = 0; i < 3; i++) m.Step(StDt);
        c.Check(guard.Total == faults + 3 && bad.Steps == 3 && next.Steps == 3 && m.Errors == errors && StOn(m, StFull("throws")),
            "RY5: a throw is a fault of the plugin, the stage stays and so does what follows it", "faults +" + (guard.Total - faults) + ", steps " + bad.Steps + ", next " + next.Steps);
        c.Check(guard.ByHook().TryGetValue("stage " + StFull("throws"), out int n) && n == 3, "RY5: the fault is counted under the stage's name", "count " + n);
        e1.Dispose();
        e2.Dispose();

        // a factory that throws: a fault, no stage, the entry waits for the next build
        faults = guard.Total;
        var t = stages.Add(key, root => throw new InvalidOperationException("thrown by the test"));
        c.Check(guard.Total == faults + 1, "RY5: a factory that throws is a fault", "faults +" + (guard.Total - faults));
        t.Dispose();

        // the threshold: the fault that reaches it unloads the plugin from inside the walk of the list
        var log = new List<string>();
        var g2 = new PluginGuard(StGuid + ".f", l => log.Add(l), 3);
        var s2 = new PluginEngineServices(StGuid + ".f", g2, l => log.Add(l), l => log.Add(l), () => c.App?.Runtime, null);
        g2.BecameFaulted += s2.Unload;
        var bad2 = new LogStage(433, "throws", null) { Throw = true };
        var next2 = new LogStage(434, "after", null);
        var own = new LogStage(435, "own", null);
        s2.Stages.Add(key, root => bad2);
        s2.Stages.Add(key, root => next2);
        var ownEntry = CharaStageHost.For(rt).Add(key, StOwner, q => own);
        for (int i = 0; i < 3; i++) m.Step(StDt);
        c.Check(g2.Faulted && s2.Unloaded && own.Steps == 3 && bad2.Steps == 3 && next2.Steps == 2, "RY5: at the threshold the plugin leaves inside the walk: its later stage rests at once, the host's stage behind them still ran in that frame",
            "plugin's next " + next2.Steps + ", host stage " + own.Steps + ", faulted " + g2.Faulted);
        rt.LateApply();
        ownEntry.Dispose();
        c.Check(!m.Stages.Any(x => x is PluginStageAdapter a && a.Owner == StGuid + ".f"), "RY5: every stage of the plugin left", string.Join(" ", m.Stages.Select(x => x.Name).Where(x => x.StartsWith(StGuid, StringComparison.Ordinal))));
        bool closed = false;
        try { var _ = s2.Stages; }
        catch (ObjectDisposedException) { closed = true; }
        c.Check(closed, "RY5: an unloaded plugin's services refuse", "");
    }

    static void C48Probe(Ctx c, CharaMotion m, ICharaStages stages, string key, CharaStageHost host)
    {
        var plugin = new LogStage(420, "probed", null);
        var own = new LogStage(421, "own", null);
        var e1 = stages.Add(key, root => plugin);
        var e2 = host.Add(key, StOwner, q => own);
        m.Probe((MotionPhase)600, m.transform, () => { });
        c.Check(plugin.Steps == 0 && own.Steps == 1, "RY6: a plugin's stage is skipped in a probe, a host stage is not", "plugin " + plugin.Steps + ", host stage " + own.Steps);
        m.Step(StDt);
        c.Check(plugin.Steps == 1 && own.Steps == 2, "RY6: it runs in the next step", "plugin " + plugin.Steps);
        e1.Dispose();
        e2.Dispose();
    }

    static void C48Pass(Ctx c, CharaRealization r, ICharaStages stages, string key, SceneRuntime rt)
    {
        var stage = new LogStage(420, "pass", null);
        var e = stages.Add(key, root => stage);
        c.Frames(3);
        int plain = stage.Steps;
        c.Check(plain == 3 && !r.Motion.Scheduled, "RY7: without a pass the stage steps once a frame", "steps " + plain);
        string a = c.App.Session.AddFolder().NewIds[0], b = c.App.Session.AddFolder().NewIds[0];
        rt.ApplyNow();
        C44Pos(c, a, b, "RY7: a constraint between two folders");
        c.Frames(1);
        c.Check(rt.Pass != null && r.Motion.Scheduled, "RY7: the pass is on", "scheduled " + r.Motion.Scheduled);
        int start = stage.Steps;
        c.Frames(5);
        c.Check(stage.Steps == start + 5, "RY7: inside a pass the stage steps once a frame", "steps +" + (stage.Steps - start));
        C44Clear(c);
        c.Frames(1);
        c.Check(rt.Pass == null, "RY7: the pass ended", "");
        e.Dispose();
    }

    static void C48Delete(Ctx c, ICharaStages stages, CharaStageHost host, PluginEngineServices s, string key)
    {
        var rt = c.App.Runtime;
        var r = rt.FindChara(key);
        int makes = 0;
        LogStage stage = null;
        var entry = stages.Add(key, root => { makes++; return stage = new LogStage(420, "buried", null); });
        var m = r.Motion;
        c.Frames(1);
        int steps = stage.Steps;
        c.App.Session.Delete(new[] { key });
        rt.ApplyNow();
        c.Frames(2);
        c.Check(r.InGraveyard && stage.Steps == steps && makes == 1 && StOn(m, StFull("buried")), "RY4: a deleted character keeps its stage, which rests", "steps " + stage.Steps + " after " + steps);
        c.Ok(new { cmd = "undo" }, "RY4: undo");
        rt.ApplyNow();
        steps = stage.Steps;
        c.Frames(2);
        c.Check(!r.InGraveyard && r.Motion == m && makes == 1 && stage.Steps == steps + 2, "RY4: an undo brings the same stage back, no factory runs", "steps +" + (stage.Steps - steps) + ", makes " + makes);
        int entries = host.Count;
        c.App.Session.Delete(new[] { key });
        rt.ApplyNow();
        c.App.Session.Commands.ClearHistory();
        rt.ApplyNow();
        c.Check(entries > 0 && host.Count == 0 && !host.Listening, "RY4: a node that can never come back ends its entries, and with the last one no event stays subscribed", "entries " + entries + " -> " + host.Count);
        bool refused = false;
        try { stages.Add(key, root => null); }
        catch (ArgumentException) { refused = true; }
        c.Check(refused, "RY4: a forgotten node is no character any more", "");
        entry.Dispose();
    }

    /// <summary>A load drops what the old scene had; a plugin adds again in SceneLoaded, before the character is built.</summary>
    static void C48Scene(Ctx c, PluginEngineServices s, ICharaStages stages, List<string> log)
    {
        var rt = c.App.Runtime;
        var host = CharaStageHost.Of(rt);
        string key = C30Load(c, c.FemaleRel, "female", "RY4: a new character");
        if (key == null) return;
        rt.ApplyNow();
        var old = stages.Add(key, root => new LogStage(420, "old", null));
        c.Check(host.Count > 0 && host.Listening, "RY4: the entry is there before the save", "entries " + host.Count);
        c.Ok(new { cmd = "scene.save", name = "c48_scene", overwrite = true }, "RY4: scene.save");
        int makes = 0;
        GameObject seen = null;
        IDisposable again = null;
        Action<HS2Next.Scenes.SceneLoadedArgs> onLoaded = a =>
        {
            if (a.IsNew) return;
            again = stages.Add(key, root => { makes++; seen = root; return new LogStage(420, "again", null); });
        };
        c.App.Session.Events.Loaded += onLoaded;
        c.Ok(new { cmd = "scene.load", name = "c48_scene" }, "RY4: scene.load");
        c.App.Session.Events.Loaded -= onLoaded;
        rt.ApplyNow();
        var r = rt.FindChara(key);
        c.Check(r != null && makes == 1 && seen == r.Root && StOn(r.Motion, StFull("again")) && !StOn(r.Motion, StFull("old")),
            "RY4: a stage added in the load waits for the build, the old scene's is gone", "makes " + makes + ", entries " + host.Count);
        c.Ok(new { cmd = "scene.new" }, "RY4: scene.new");
        rt.ApplyNow();
        c.Check(host.Count == 0 && !host.Listening, "RY4: a new scene ends every entry, no event stays subscribed", "entries " + host.Count);
        old.Dispose();
        again?.Dispose();
        c.Check(host.Count == 0, "RY4: a dispose after the scene went does nothing", "");
    }

    // ---------------------------------------------------------------- the plugin as a plugin is

    /// <summary>Builds and stages the stage probe alone into the C37 folder, as C46 does the tool probe.</summary>
    static bool C48StageProbe(Ctx c)
    {
        string tool = Path.Combine(ContentSettings.ProjectRoot, "Tools", "plugins.ps1");
        var psi = new ProcessStartInfo("pwsh", "-NoProfile -File \"" + tool + "\" -Build \"" + Path.Combine("Tests", "Plugins", "StageProbe") + "\" -Stage \"" + C37Dir + "\"")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = ContentSettings.ProjectRoot,
        };
        using (var p = Process.Start(psi))
        {
            string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            bool exited = p.WaitForExit(300000);
            return c.Check(exited && p.ExitCode == 0, "C48: the stage probe is built and staged", Ctx.Short(o));
        }
    }

    static JObject C48Entry(Ctx c) => (c.Call(new { cmd = "plugin.list" })["plugins"] as JArray)?.OfType<JObject>().FirstOrDefault(x => (string)x["guid"] == StageProbeGuid);

    static void C48Plugin(Ctx c)
    {
        if (!C37Stage(c)) return;
        if (!File.Exists(Path.Combine(C37Dir, StageProbeGuid, "manifest.json")) && !C48StageProbe(c)) return;
        var (app, _) = C37Host(c);
        const string stage = StageProbeGuid + "/probe";
        try
        {
            var entry = C48Entry(c);
            if (!c.Check((string)entry?["state"] == "loaded" && (string)entry?["apiVersion"] == "1.3", "C48: the probe of Api 1.3 is loaded", Ctx.Short(entry?.ToString()))) return;
            var st = c.Ok(new { cmd = "stageprobe.state" }, "C48: stageprobe.state");
            c.Check((bool?)st["unity"] == true && (bool?)st["stages"] == true, "C48: the plugin reaches the character stages", Ctx.Short(st.ToString()));
            c.Ok(new { cmd = "stageprobe.clear" }, "C48: stageprobe.clear");
            string key = C30Load(c, c.FemaleRel, "female", "C48: chara.load");
            if (key == null) return;
            var rt = c.App.Runtime;
            rt.ApplyNow();
            var r = rt.FindChara(key);
            string folder = c.App.Session.AddFolder().NewIds[0];
            rt.ApplyNow();
            var no = c.Ok(new { cmd = "stageprobe.add", key = folder }, "C48: a folder");
            c.Check((bool?)no["added"] == false && (string)no["why"] == "ArgumentException", "C48: the plugin's Add answers ArgumentException for a folder", Ctx.Short(no.ToString()));

            // RY2, RY3 through the Api's binding: the in-parameter of Step, the factory, Phase and Name
            c.Ok(new { cmd = "stageprobe.add", key, phase = 420 }, "C48: stageprobe.add");
            var row = StRow(c, key, stage);
            c.Check(row != null && (int?)row["phase"] == 420 && (string)row["owner"] == StageProbeGuid, "C48: chara.motion lists the plugin's stage", Ctx.Short(row?.ToString()));
            c.Frames(3);
            st = c.Ok(new { cmd = "stageprobe.state" }, "C48: state after 3 frames");
            c.Check((int?)st["steps"] >= 3 && (int?)st["makes"] == 1, "C48: the stage counted the 3 frames (a command pumps frames of its own), one factory run", Ctx.Short(st.ToString()));
            int firstRoot = (int?)st["root"] ?? 0;
            c.Check(r.RequestPluginRebuild(StageProbeGuid), "C48: a rebuild", "");
            rt.ApplyNow();
            st = c.Ok(new { cmd = "stageprobe.state" }, "C48: state after the rebuild");
            c.Check((int?)st["makes"] == 2 && (int?)st["root"] != firstRoot && (int?)st["root"] != 0, "C48: the factory ran again with a new root", Ctx.Short(st.ToString()));
            int resets = (int?)st["resets"] ?? 0;
            r.Motion.ResetAll();
            st = c.Ok(new { cmd = "stageprobe.state" }, "C48: state after a reset");
            c.Check((int?)st["resets"] == resets + 1, "C48: Reset reaches the plugin's stage", Ctx.Short(st.ToString()));

            // RY4 dispose, twice over, through the plugin
            var rem = c.Ok(new { cmd = "stageprobe.remove" }, "C48: stageprobe.remove");
            int stepsThen = (int?)c.Ok(new { cmd = "stageprobe.state" }, "C48: state")["steps"] ?? -1;
            c.Frames(2);
            c.Check((int?)rem["removed"] == 1 && StRow(c, key, stage) == null && (int?)c.Ok(new { cmd = "stageprobe.state" }, "C48: state")["steps"] == stepsThen, "C48: the stage is off and stopped counting", "steps " + stepsThen);

            // a stage that leaves in its own Step, with no fault
            c.Ok(new { cmd = "stageprobe.add", key, name = "leaves", leaveInStep = true }, "C48: a stage that leaves");
            c.Frames(2);
            c.Check(StRow(c, key, StageProbeGuid + "/leaves") == null && !(C48Entry(c)?["faults"] as JObject ?? new JObject()).Properties().Any(), "C48: it is gone after its frame, no fault", Ctx.Short(C48Entry(c)?["faults"]?.ToString()));

            // calls the motion makes outside its own guard: Enabled in the walk
            c.Ok(new { cmd = "stageprobe.add", key, name = "enabled", throwIn = "Enabled" }, "C48: Enabled throws");
            c.Frames(1);
            var f = C48Entry(c)?["faults"] as JObject;
            c.Check(((int?)f?["stage " + StageProbeGuid + "/enabled"] ?? 0) >= 1 && (string)C48Entry(c)?["state"] == "loaded", "C48: Enabled throwing inside the walk is a fault, the frame goes on", Ctx.Short(f?.ToString()));
            c.Ok(new { cmd = "stageprobe.remove" }, "C48: remove it");
            c.Ok(new { cmd = "stageprobe.add", key, name = "reset", throwIn = "Reset" }, "C48: Reset throws");
            r.Motion.ResetAll();
            f = C48Entry(c)?["faults"] as JObject;
            c.Check(((int?)f?["stage " + StageProbeGuid + "/reset"] ?? 0) == 1, "C48: Reset throwing is a fault", Ctx.Short(f?.ToString()));
            c.Ok(new { cmd = "stageprobe.remove" }, "C48: remove it");

            // the guide's example, as it stands there
            c.Ok(new { cmd = "stageprobe.watch", key }, "C48: the guide's example");
            c.Frames(2);
            float y = (float?)c.Ok(new { cmd = "stageprobe.head" }, "C48: stageprobe.head")["y"] ?? 0;
            c.Check(y > 1f, "C48: the guide's example saw the head at its height", "y " + y);
            c.Ok(new { cmd = "stageprobe.remove" }, "C48: remove it");

            // RY4 a disable and an enable take the plugin's stages with them
            c.Ok(new { cmd = "stageprobe.add", key }, "C48: add before the disable");
            c.Check(StRow(c, key, stage) != null, "C48: the stage is there before the disable", "");
            c.Ok(new { cmd = "plugin.disable", guid = StageProbeGuid }, "C48: plugin.disable");
            c.Check(StRow(c, key, stage) == null, "C48: a disable takes the stage off", "");
            c.Ok(new { cmd = "plugin.enable", guid = StageProbeGuid }, "C48: plugin.enable");
            c.Check(StRow(c, key, stage) == null, "C48: an enable adds nothing by itself", "");
            int faultsBefore = ((C48Entry(c)?["faults"] as JObject) ?? new JObject()).Properties().Sum(x => (int)x.Value);

            // RY5 the real host's threshold, reached inside the character's step
            c.Ok(new { cmd = "stageprobe.add", key, phase = 420, name = "bad", throwIn = "Step" }, "C48: a stage that throws");
            c.Ok(new { cmd = "stageprobe.add", key, phase = 421, name = "after" }, "C48: a stage behind it");
            c.Ok(new { cmd = "stageprobe.clear" }, "C48: clear");
            c.Frames(1);
            c.Check(StRow(c, key, StageProbeGuid + "/bad") != null, "C48: it stays after a fault", "");
            for (int i = 0; i < 12 && (string)C48Entry(c)?["state"] == "loaded"; i++) c.Frames(1);
            var dead = C48Entry(c);
            c.Check((string)dead?["state"] == "faulted", "C48: the plugin faulted at the threshold", Ctx.Short(dead?["lastError"]?.ToString()));
            c.Frames(1);
            c.Check(StRow(c, key, StageProbeGuid + "/bad") == null && StRow(c, key, StageProbeGuid + "/after") == null, "C48: its stages left", "");
            c.Fails(new { cmd = "stageprobe.state" }, "plugin_fault", "C48: its commands answer plugin_fault");
            c.Info("C48: faults before the bad stage " + faultsBefore + ", at the end " + Ctx.Short(dead?["faults"]?.ToString()));
        }
        finally { c.Dispose(app); }
    }
}
