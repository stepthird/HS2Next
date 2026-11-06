// Core bridge commands in edit mode: AppCore.ForTests hosts (no HTTP, package B's
// dispatcher through AppCore.Call, frames pumped by hand), checks C1–C17 (later phases add C18–C25 in further files; C25 the
// Phase 3.4 FK / IK commands, Editor/BridgeCoreTest_Kinematics.cs; C40 the Phase 6.1 plugin host points with package B's KB
// checks, Editor/BridgeCoreTest_Plugins61.cs; C41 the Phase 6.2 runtime overrides of plugins, Editor/BridgeCoreTest_Overrides62.cs;
// C42 the Phase 6.3 advanced-pose commands, Editor/BridgeCoreTest_AdvancedPose63.cs; C43 the Phase 6.4 material commands,
// Editor/BridgeCoreTest_Materials64.cs; C44 the Phase 6.5 constraint commands, Editor/BridgeCoreTest_Constraints65.cs).
// Package W1 owns this file, the fixture and
// C1–C9; package W2 adds Editor/BridgeCoreTest_Chara.cs with C10–C17 as further methods of this partial class.
// A check is a static method named C<number> taking one Ctx; Run finds them by reflection and runs them in number
// order (each in a guard: an exception is a FAIL; the hosts a check created are disposed after it).
// Run: -executeMethod BridgeCoreTest.Run [-only C3,C10] (headless, -nographics, edit mode).
// Cards: the SFW visual targets of Tools/targets.json; animations: the plain rows of targets.json (C11 also plays one H
// row on the clothed target, judged by numbers); map: vanilla 10.
// Output: Spike/out/bridge/ (userdata/ = the hosts' UserData, out/ = their output root); report
// Spike/out/bridge/bridge_core_report.txt, first line "SUMMARY: N PASS, M FAIL"; EditorApplication.Exit(1) on a FAIL.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HS2Next.App;
using HS2Next.Content;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static partial class BridgeCoreTest
{
    public const float Dt = 1f / 60f;
    /// <summary>Id seed of <see cref="Ctx.Host"/> (counting ids: two hosts with the same seed make the same ids).</summary>
    public const ulong DefaultSeed = 0xb41d6ec04e5eedUL;
    public static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "bridge");
    public const string ReportFile = "bridge_core_report.txt";

    /// <summary>What a check works with (one object for the whole run; <see cref="Id"/> names the running check).</summary>
    public sealed class Ctx
    {
        internal readonly StringBuilder Rep = new StringBuilder();
        internal readonly Dictionary<string, (int pass, int fail, int warn)> PerCheck = new Dictionary<string, (int, int, int)>(StringComparer.Ordinal);
        readonly List<AppCore> hosts = new List<AppCore>();

        /// <summary>Catalog and Studio lists, built once (shared by every host; do not dispose).</summary>
        public RuntimeServices Services { get; internal set; }
        public TestTargets Targets { get; internal set; }
        /// <summary>Absolute paths of the SFW visual targets.</summary>
        public string FemaleCard { get; internal set; }
        public string MaleCard { get; internal set; }
        /// <summary>The targets relative to the HS2 female / male card folder (what chara.load's path takes with sex).</summary>
        public string FemaleRel { get; internal set; }
        public string MaleRel { get; internal set; }
        /// <summary>Spike/out/bridge.</summary>
        public string Out => OutDir;
        /// <summary>The hosts' UserData (Spike/out/bridge/userdata) and output root (Spike/out/bridge/out).</summary>
        public string UserDataDir => Path.Combine(OutDir, "userdata");
        public string OutputDir => Path.Combine(OutDir, "out");

        /// <summary>The running check ("C10").</summary>
        public string Id { get; internal set; } = "";
        /// <summary>The host of the last <see cref="Host"/> call of this check.</summary>
        public AppCore App { get; private set; }
        /// <summary>Raw text of the last reply of <see cref="Call(string)"/>.</summary>
        public string LastText { get; private set; }
        public int Passed, Failed, Warned;

        /// <summary>The settings of a test host: default app settings, the tests' UserData and output root, the content settings.</summary>
        public AppSettings HostSettings()
        {
            var s = AppSettings.Defaults(Array.Empty<string>());
            s.Content = CardResolveTest.Settings;
            s.UserData = UserDataDir;
            s.Bridge.OutputDir = OutputDir;
            return s;
        }

        /// <summary>A fresh host with counting ids (<paramref name="seed"/>); becomes <see cref="App"/>; disposed after the check.</summary>
        public AppCore Host(ulong seed = DefaultSeed)
        {
            var o = Services.NewSessionOptions();
            o.Ids = new CountingIdSource(seed);
            var app = AppCore.ForTests(Services, HostSettings(), null, o);
            hosts.Add(app);
            App = app;
            return app;
        }

        /// <summary>Disposes a host before the end of the check (e.g. the first of two determinism hosts).</summary>
        public void Dispose(AppCore app)
        {
            if (app == null) return;
            hosts.Remove(app);
            app.Dispose();
            if (App == app) App = null;
        }

        // Process-wide switches app.options may change (restored after every check).
        bool fkZero, fkIdentity, fkHips, correctionOn, gimmickIcons = true;
        HS2Next.App.KinematicsSettings kinematics;   // Phase 3.4: app.options kinematics (IkOptions)
        HS2Next.Chara.Ext.CharaExtOptions extOptions;
        // Phase 4.6: app.options maker (the Maker switches)
        bool makerKeepColours, makerCopyPlugins;
        HS2Next.Scenes.Runtime.BodyThrottleMode makerThrottle;

        internal void BeginCheck()
        {
            fkZero = HS2Next.Motion.Fk.FkOptions.MissingBonesZero;
            fkIdentity = HS2Next.Motion.Fk.FkOptions.ResetToIdentity;
            fkHips = HS2Next.Motion.Fk.FkOptions.HipsOffset;
            correctionOn = HS2Next.Motion.Corrective.CorrectSetup.InitialCategoriesOn;
            gimmickIcons = HS2Next.Scenes.Runtime.ItemIcons.Visible;
            kinematics = HS2Next.App.KinematicsSettings.FromRuntime();
            extOptions = Services.ExtOptions;
            makerKeepColours = HS2Next.Scenes.MakerServices.KeepColoursOnItemChange;
            makerCopyPlugins = HS2Next.Scenes.MakerServices.CopyAccessoryPluginData;
            makerThrottle = HS2Next.Scenes.Runtime.CharaEditFlush.BodyThrottle;
            if (extOptions != null) Services.ExtOptions = (HS2Next.Chara.Ext.CharaExtOptions)Clone(extOptions);
        }

        static object Clone(object o)
        {
            var m = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
            return m.Invoke(o, null);
        }

        internal void EndCheck()
        {
            foreach (var h in hosts) { try { h.Dispose(); } catch (Exception e) { Info("host dispose threw: " + e.Message); } }
            hosts.Clear();
            App = null;
            if (Services == null) return;
            HS2Next.Motion.Fk.FkOptions.MissingBonesZero = fkZero;
            HS2Next.Motion.Fk.FkOptions.ResetToIdentity = fkIdentity;
            HS2Next.Motion.Fk.FkOptions.HipsOffset = fkHips;
            HS2Next.Motion.Corrective.CorrectSetup.InitialCategoriesOn = correctionOn;
            HS2Next.Scenes.Runtime.ItemIcons.Visible = gimmickIcons;
            kinematics?.ApplyToRuntime();
            Services.ExtOptions = extOptions;
            HS2Next.Scenes.MakerServices.KeepColoursOnItemChange = makerKeepColours;
            HS2Next.Scenes.MakerServices.CopyAccessoryPluginData = makerCopyPlugins;
            HS2Next.Scenes.Runtime.CharaEditFlush.BodyThrottle = makerThrottle;
        }

        /// <summary>One request through <see cref="App"/>'s in-process bridge (frames pumped until it completes); the reply parsed.</summary>
        public JObject Call(string json) => Call(App, json);

        public JObject Call(AppCore app, string json)
        {
            LastText = app.Call(json);
            JObject r;
            try { r = JObject.Parse(LastText); }
            catch (Exception e) { throw new Exception("reply is not a JSON object (" + e.Message + "): " + LastText); }
            // a result member named like an envelope field is dropped by the bridge: always a Core bug
            if ((r["warnings"] as JArray)?.Any(w => ((string)w).Contains("clashes with the envelope")) == true)
                Check(false, "reply without envelope clashes (" + (string)r["cmd"] + ")", Short(LastText));
            return r;
        }

        /// <summary>A request object (anonymous type, dictionary, JObject) serialized and called.</summary>
        public JObject Call(object request) => Call(App, request);
        public JObject Call(AppCore app, object request) => Call(app, request is string s ? s : JsonConvert.SerializeObject(request));

        /// <summary>Calls and checks <c>ok: true</c> (a FAIL line with the reply otherwise); returns the reply.</summary>
        public JObject Ok(object request, string what)
        {
            var r = Call(App, request);
            Check(r.Value<bool?>("ok") == true, what, r.Value<bool?>("ok") == true ? "" : Short(LastText));
            return r;
        }

        /// <summary>Calls and checks an error with <paramref name="code"/>; returns the reply.</summary>
        public JObject Fails(object request, string code, string what)
        {
            var r = Call(App, request);
            Check(r.Value<bool?>("ok") == false && r.Value<string>("code") == code, what, "code " + r.Value<string>("code") + " (" + Short(LastText) + ")");
            return r;
        }

        /// <summary>Pumps <paramref name="n"/> frames of <see cref="App"/>.</summary>
        public void Frames(int n, float dt = Dt)
        {
            for (int i = 0; i < n; i++) App.Frame(dt);
        }

        /// <summary>A PASS / FAIL line of the running check.</summary>
        public bool Check(bool ok, string what, string detail = "")
        {
            Rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  [{Id}] {what}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
            if (ok) Passed++; else Failed++;
            PerCheck.TryGetValue(Id, out var v);
            PerCheck[Id] = ok ? (v.pass + 1, v.fail, v.warn) : (v.pass, v.fail + 1, v.warn);
            return ok;
        }

        /// <summary>A WARN line (not counted as a failure).</summary>
        public void Warn(string what)
        {
            Rep.AppendLine($"WARN  [{Id}] {what}");
            Warned++;
            PerCheck.TryGetValue(Id, out var v);
            PerCheck[Id] = (v.pass, v.fail, v.warn + 1);
        }

        public void Info(string line) => Rep.AppendLine("   " + line);

        /// <summary>The first 300 characters of a text (for FAIL details).</summary>
        public static string Short(string s) => s == null ? "null" : s.Length > 300 ? s.Substring(0, 300) + "…" : s;
    }

    static string Arg(string name)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    /// <summary>
    /// The ids after -only ("C3,C10", "C3+C10" or "C3;C10"; Tools/unity_batch.ps1 hands every array element over as it is and
    /// splits only a single comma-joined string from `pwsh -File` on commas, so the ids may also arrive as separate arguments up
    /// to the next "-" flag), or null.
    /// </summary>
    static string[] OnlyArgs()
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, "-only");
        if (i < 0) return null;
        var l = new List<string>();
        for (int k = i + 1; k < a.Length && !a[k].StartsWith("-", StringComparison.Ordinal); k++)
            l.AddRange(a[k].Split(',', '+', ';').Select(x => x.Trim()).Where(x => x.Length > 0));
        return l.ToArray();
    }

    /// <summary>The checks of this partial class: static methods C&lt;n&gt;(Ctx), by number.</summary>
    static List<(string id, int no, MethodInfo m)> FindChecks()
    {
        var rx = new Regex(@"^C(\d+)$");
        var l = new List<(string, int, MethodInfo)>();
        foreach (var m in typeof(BridgeCoreTest).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var mm = rx.Match(m.Name);
            var ps = m.GetParameters();
            if (mm.Success && ps.Length == 1 && ps[0].ParameterType == typeof(Ctx)) l.Add((m.Name, int.Parse(mm.Groups[1].Value), m));
        }
        l.Sort((a, b) => a.Item2.CompareTo(b.Item2));
        return l;
    }

    public static void Run()
    {
        var c = new Ctx();
        var only = OnlyArgs();
        Directory.CreateDirectory(OutDir);
        var sw = Stopwatch.StartNew();
        var head = new StringBuilder();
        var checks = FindChecks();
        try
        {
            c.Services = SceneRuntimeTest.NewServices(head);
            c.Targets = TestTargets.Load();
            c.FemaleCard = TestTargets.Abs(c.Targets.visual.female);
            c.MaleCard = TestTargets.Abs(c.Targets.visual.male);
            c.FemaleRel = RelTo(c.FemaleCard, Path.Combine(TestTargets.UserData, "chara", "female"));
            c.MaleRel = RelTo(c.MaleCard, Path.Combine(TestTargets.UserData, "chara", "male"));
            c.Rep.AppendLine($"Bridge core test (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}); checks {string.Join(", ", checks.Select(x => x.id))}");
            c.Rep.Append(head);
            foreach (var (id, _, m) in checks)
            {
                if (only != null && !only.Contains(id)) continue;
                c.Id = id;
                c.Rep.AppendLine();
                c.Rep.AppendLine("== " + id);
                var t0 = sw.Elapsed.TotalSeconds;
                c.BeginCheck();
                try { m.Invoke(null, new object[] { c }); }
                catch (TargetInvocationException e) { c.Check(false, "exception", (e.InnerException ?? e).GetType().Name + ": " + (e.InnerException ?? e).Message + "\n" + (e.InnerException ?? e).StackTrace); }
                finally { c.EndCheck(); }
                c.Info($"{id}: {sw.Elapsed.TotalSeconds - t0:F1} s");
            }
        }
        catch (Exception e) { c.Id = "run"; c.Check(false, "run (exception)", e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        finally { c.EndCheck(); }
        var sum = new StringBuilder();
        sum.AppendLine($"SUMMARY: {c.Passed} PASS, {c.Failed} FAIL ({c.Warned} WARN, {sw.Elapsed.TotalSeconds:F0} s)");
        foreach (var kv in c.PerCheck.OrderBy(k => k.Key.Length).ThenBy(k => k.Key, StringComparer.Ordinal))
            sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail{(kv.Value.warn > 0 ? ", " + kv.Value.warn + " warn" : "")}");
        c.Rep.Insert(0, sum.ToString());
        File.WriteAllText(Path.Combine(OutDir, ReportFile), c.Rep.ToString(), new UTF8Encoding(false));
        c.Services?.Catalog?.UnloadAll();
        Debug.Log($"BRIDGE CORE TEST DONE: {c.Passed} PASS, {c.Failed} FAIL, {c.Warned} WARN");
        if (c.Failed > 0) TestExit.Exit(1);
    }

    // ================================================================ W1 checks (C1–C9)

    /// <summary>
    /// C2: registration invariants — every command group registers without an error (also a spec registered under another
    /// group's call), the Core command set = CoreCommands.AllNames (distinct, nothing missing, nothing extra), every core spec
    /// documented (owner core, Since, summary, argument docs), help {name} answers for each, help lists the core commands and
    /// the bridge's five. The name lists of the phases are checked by their own groups (C20–C34); the lists of the closed
    /// phases 2.4–3.6 are pinned here by their counts, so a later phase does not edit this check.
    /// </summary>
    static void C2(Ctx c)
    {
        var app = c.Host();
        var reg = app.BridgeServices.Commands;
        c.Check(HS2Next.App.Commands.CoreCommands.RegistrationErrors.Count == 0, "every command group registers without an error",
            string.Join("; ", HS2Next.App.Commands.CoreCommands.RegistrationErrors));
        var allList = HS2Next.App.Commands.CoreCommands.AllNames.ToList();
        var all = new HashSet<string>(allList);
        var dupes = allList.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        c.Check(dupes.Count == 0, $"CoreCommands.AllNames are distinct ({allList.Count})", string.Join(", ", dupes));
        var core = reg.Specs.Where(x => x.Owner == HS2Next.Api.Bridge.BridgeOwners.Core).ToList();
        var missing = all.Where(n => reg.Find(n) == null).OrderBy(n => n, StringComparer.Ordinal).ToList();
        c.Check(missing.Count == 0, $"every name of CoreCommands.AllNames is registered ({all.Count})", string.Join(", ", missing));
        var extra = core.Where(x => !all.Contains(x.Name)).Select(x => x.Name).ToList();
        c.Check(extra.Count == 0, "no core command outside CoreCommands.AllNames", string.Join(", ", extra));
        c.Check(core.Count == all.Count, $"{all.Count} core commands registered = CoreCommands.AllNames", "" + core.Count);
        var cc = new (string phase, int want, int got)[]
        {
            ("2.4", 45, HS2Next.App.Commands.CoreCommands.Names.Count), ("3.1", 6, HS2Next.App.Commands.CoreCommands.Phase31Names.Count),
            ("3.2", 3, HS2Next.App.Commands.CoreCommands.Phase32Names.Count),
            ("3.3", 14, HS2Next.App.Commands.CoreCommands.Phase33LightNames.Count + HS2Next.App.Commands.CoreCommands.Phase33EffectNames.Count),
            ("3.4", 6, HS2Next.App.Commands.CoreCommands.Phase34Names.Count), ("3.5", 10, HS2Next.App.Commands.CoreCommands.Phase35Names.Count),
            ("3.5 ui.*", 6, HS2Next.UI.Bridge.UiCommands.Names.Count), ("3.6", 13, HS2Next.App.Commands.CoreCommands.Phase36Names.Count),
        };
        var off = cc.Where(x => x.want != x.got).Select(x => $"{x.phase}: {x.got} (pinned {x.want})").ToList();
        c.Check(off.Count == 0, "the closed phases' name lists keep their counts (2.4 45, 3.1 6, 3.2 3, 3.3 14, 3.4 6, 3.5 10 + ui.* 6, 3.6 13)", string.Join("; ", off));
        c.Info("core commands per list: " + string.Join(" + ", cc.Select(x => x.got + " of " + x.phase)) + " + " + HS2Next.App.Commands.CoreCommands.Phase40Names.Count + " of 4.0 + "
            + HS2Next.App.Commands.CoreCommands.Phase41Names.Count + " of 4.1 + " + HS2Next.App.Commands.CoreCommands.Phase42Names.Count + " of 4.2 + "
            + HS2Next.App.Commands.CoreCommands.Phase43Names.Count + " of 4.3 + " + HS2Next.App.Commands.CoreCommands.Phase44Names.Count + " of 4.4 + " + HS2Next.App.Commands.CoreCommands.Phase45Names.Count + " of 4.5 + "
            + HS2Next.App.Commands.CoreCommands.Phase46Names.Count + " of 4.6 = " + allList.Count);
        var bad = new List<string>();
        foreach (var x in core)
        {
            if (x.Owner != "core") bad.Add(x.Name + ": owner " + x.Owner);
            if (string.IsNullOrEmpty(x.Summary)) bad.Add(x.Name + ": no summary");
            // every Core command of the first public version carries CommandKit.Since (1.0.0, the Api freeze); a later one the Api
            // version that added it (Phase 6.1: 1.1.0; Phase 6.2 to 6.5: 1.2.0)
            string since = HS2Next.App.Commands.CoreCommands.Phase61Names.Contains(x.Name) ? HS2Next.App.Plugins.PluginCommands.Since61
                : HS2Next.App.Commands.CoreCommands.Phase62Names.Contains(x.Name) ? HS2Next.App.Commands.TimelineCommands.Since62
                : HS2Next.App.Commands.CoreCommands.Phase63Names.Contains(x.Name) ? HS2Next.App.Commands.AdvancedPoseCommands.Since63
                : HS2Next.App.Commands.CoreCommands.Phase64Names.Contains(x.Name) ? HS2Next.App.Commands.MaterialCommands.Since64
                : HS2Next.App.Commands.CoreCommands.Phase65Names.Contains(x.Name) ? HS2Next.App.Commands.ConstraintCommands.Since65 : HS2Next.App.Commands.CommandKit.Since;
            if (x.Since != since) bad.Add(x.Name + ": since " + x.Since);
            foreach (var a in x.Args) if (string.IsNullOrEmpty(a.Doc)) bad.Add(x.Name + "." + a.Name + ": no doc");
        }
        c.Check(bad.Count == 0, "every core spec has owner core, Since " + HS2Next.App.Commands.CommandKit.Since + ", a summary and argument docs", string.Join("; ", bad.Take(10)));
        int helpOk = 0;
        var helpBad = new List<string>();
        foreach (var x in core)
        {
            var r = c.Call(new { cmd = "help", name = x.Name });
            if (r.Value<bool?>("ok") == true && r["command"]?.Value<string>("name") == x.Name) helpOk++;
            else helpBad.Add(x.Name);
        }
        c.Check(helpBad.Count == 0, $"help {{name}} answers ok for every core command ({helpOk})", string.Join(", ", helpBad.Take(10)));
        var allHelp = c.Call(new { cmd = "help" });
        int total = (allHelp["commands"] as JArray)?.Count ?? -1;
        c.Info("help lists " + total + " commands (" + core.Count + " core)");
        c.Check(total >= core.Count + 5, "help lists the core commands and the bridge's five", "" + total);
    }

    // ---------------------------------------------------------------- helpers of the W1 checks

    /// <summary>True when every named command is registered on the current host (W2's commands at integration).</summary>
    static bool W1Has(Ctx c, params string[] cmds)
    {
        foreach (var n in cmds) if (c.App.BridgeServices.Commands.Find(n) == null) return false;
        return true;
    }

    /// <summary>Loads BR-Chan through chara.load (package W2) and pauses it; null (with a WARN) when the commands are missing.</summary>
    static string W1LoadFemale(Ctx c, bool pause = true)
    {
        if (!W1Has(c, "chara.load", "chara.motion")) { c.Warn("chara.load / chara.motion not registered (package W2): character steps skipped"); return null; }
        var r = c.Ok(new { cmd = "chara.load", path = c.FemaleRel, sex = "female" }, "chara.load BR-Chan");
        string key = (string)r["added"]?[0]?["key"];
        if (key != null && pause) c.Ok(new { cmd = "chara.motion", key, paused = true }, "chara.motion paused");
        return key;
    }

    /// <summary>Adds the fallback item (illusion.hs2#399) at the root; its node id.</summary>
    static string W1AddItem(Ctx c, string what = "item.add 399")
    {
        var r = c.Ok(new { cmd = "item.add", no = 399 }, what);
        return (string)r["added"]?[0]?["key"];
    }

    static int W1UndoCount(Ctx c) => (c.Call(new { cmd = "history" })["undo"] as JArray)?.Count ?? -1;
    static string W1TopLabel(Ctx c) => (string)(c.Call(new { cmd = "history" })["undo"] as JArray)?.FirstOrDefault();
    static string W1DocSha(Ctx c) => (string)c.Call(new { cmd = "scene.doc", capture = false })["sha256"];

    static Vector3 W1V3(JToken t) => t is JArray a && a.Count >= 3 ? new Vector3((float)a[0], (float)a[1], (float)a[2]) : new Vector3(float.NaN, float.NaN, float.NaN);
    static double W1Dist(Vector3 a, Vector3 b) => (a - b).magnitude;

    /// <summary>
    /// One mutating request: exactly one undo entry with <paramref name="label"/>; undo restores the scene.doc {capture:false}
    /// text from before, redo the text after; the same request with undo:false (when <paramref name="again"/> gives one)
    /// adds no entry.
    /// </summary>
    static JObject W1History1(Ctx c, object request, string label, string what, Func<object> again = null)
    {
        string before = W1DocSha(c);
        int n0 = W1UndoCount(c);
        var r = c.Call(request);
        if (!c.Check(r.Value<bool?>("ok") == true, what + ": ok", r.Value<bool?>("ok") == true ? "" : Ctx.Short(c.LastText))) return r;
        int n1 = W1UndoCount(c);
        string top = W1TopLabel(c);
        c.Check(n1 == n0 + 1 && top == label, what + ": one undo entry \"" + label + "\"", n0 + " -> " + n1 + ", top \"" + top + "\"");
        string after = W1DocSha(c);
        c.Check(after != before, what + ": changes the scene document");
        c.Ok(new { cmd = "undo" }, what + ": undo");
        c.Check(W1DocSha(c) == before, what + ": undo restores the document");
        c.Ok(new { cmd = "redo" }, what + ": redo");
        c.Check(W1DocSha(c) == after, what + ": redo restores the document after");
        if (again != null)
        {
            int n2 = W1UndoCount(c);
            var r2 = c.Call(again());
            c.Check(r2.Value<bool?>("ok") == true && W1UndoCount(c) == n2, what + " with undo:false: no entry", Ctx.Short(c.LastText));
        }
        return r;
    }

    // ---------------------------------------------------------------- C1 worker scan

    static void C1(Ctx c)
    {
        var settings = CardResolveTest.Settings;
        var a = ContentCatalog.FromSettings(settings);
        var b = ContentCatalog.FromSettings(settings);
        try
        {
            // Mod lists only: the vanilla list bundles are already open in the shared services' catalog.
            int visited = 0, mainThread = System.Threading.Thread.CurrentThread.ManagedThreadId, workerThread = -1;
            var sw = Stopwatch.StartNew();
            var task = System.Threading.Tasks.Task.Run(() =>
            {
                workerThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
                return a.ScanModSources(n => System.Threading.Interlocked.Exchange(ref visited, n));
            });
            var results = task.Result;
            long scanMs = sw.ElapsedMilliseconds;
            a.LoadModLists(results);
            sw.Restart();
            b.LoadModLists();
            long mainMs = sw.ElapsedMilliseconds;
            int zips = results.Sum(x => x.Zipmods.Count);
            c.Info($"worker scan {scanMs} ms ({zips} zipmods, {results.Sum(x => x.Scanned)} read from disk), main-thread LoadModLists {mainMs} ms");
            c.Check(workerThread != -1 && workerThread != mainThread, "ScanModSources ran on a worker thread", "worker " + workerThread + ", main " + mainThread);
            c.Check(visited == zips && zips > 0, "the progress counter reached every zipmod", visited + " / " + zips);
            c.Check(a.ModRowsLoaded == b.ModRowsLoaded, "ModRowsLoaded equal", a.ModRowsLoaded + " vs " + b.ModRowsLoaded);
            c.Check(a.Count == b.Count, "Count equal", a.Count + " vs " + b.Count);
            c.Check(a.ZipmodsByGuid.Count == b.ZipmodsByGuid.Count, "ZipmodsByGuid.Count equal", a.ZipmodsByGuid.Count + " vs " + b.ZipmodsByGuid.Count);
            List<string> Keys(ContentCatalog cat)
            {
                var l = new List<string>();
                foreach (var ct in cat.Categories) foreach (var e in cat.EntriesOf(ct)) l.Add(ct + " " + e.Key + " " + e.ZipPath);
                l.Sort(StringComparer.Ordinal);
                return l;
            }
            var ka = Keys(a);
            var kb = Keys(b);
            int diff = 0;
            for (int i = 0; i < Math.Min(ka.Count, kb.Count); i++) if (ka[i] != kb[i]) diff++;
            c.Check(ka.Count == kb.Count && diff == 0, "the sorted (category, key) lists are equal", ka.Count + " vs " + kb.Count + " entries, " + diff + " differ");
        }
        finally
        {
            a.UnloadAll();
            b.UnloadAll();
        }
    }

    // ---------------------------------------------------------------- C3 envelope rules

    static void C3(Ctx c)
    {
        c.Host();
        var r = c.Ok(new { cmd = "scene", foo = 1 }, "unknown argument → ok");
        c.Check((r["warnings"] as JArray)?.Any(w => ((string)w).Contains("foo")) == true, "unknown argument → a warning naming it", Ctx.Short(c.LastText));
        c.Fails(new { cmd = "scene", foo = 1, strict = true }, "bad_args", "strict: unknown argument → bad_args");
        string item = W1AddItem(c);
        c.Fails(new { cmd = "obj.transform", key = 5, pos = new[] { 0, 0, 0 } }, "bad_args", "integer key → bad_args");
        var nf = c.Fails(new { cmd = "obj.transform", key = "0123456789abcdef0123456789abcdef", pos = new[] { 0, 0, 0 } }, "not_found", "unknown key → not_found");
        c.Check((nf["detail"]?["candidates"] as JArray)?.Any(x => (string)x == item) == true, "not_found lists candidates", Ctx.Short(c.LastText));
        var p = c.Ok(new { cmd = "obj.transform", key = item.Substring(0, 8), pos = new[] { 1, 0, 0 } }, "unique 8-character prefix accepted");
        c.Check((p["warnings"] as JArray)?.Any(w => ((string)w).Contains("unique prefix of " + item)) == true, "prefix → a warning naming the id", Ctx.Short(c.LastText));
        c.Fails(new { cmd = "obj.transform", key = item.Substring(0, 7), pos = new[] { 1, 0, 0 } }, "not_found", "a 7-character prefix is not accepted");
        string item2 = W1AddItem(c, "a second item");
        c.Fails(new { cmd = "obj.transform", key = item.Substring(0, 8), pos = new[] { 1, 0, 0 } }, "not_found", "an ambiguous prefix → not_found");
        if (W1Has(c, "chara.anime")) c.Fails(new { cmd = "chara.anime", key = item, group = 19, category = 0, no = 0 }, "wrong_kind", "item key to chara.anime → wrong_kind");
        else c.Warn("chara.anime not registered (package W2)");
        c.Fails(new { cmd = "item.parent", key = item2, chara = item, point = "48" }, "wrong_kind", "item key as item.parent's chara → wrong_kind");
        c.Fails(new { cmd = "obj.transform" }, "bad_args", "missing key → bad_args");
    }

    // ---------------------------------------------------------------- C4 history

    static void C4(Ctx c)
    {
        c.Host();
        string item = W1AddItem(c);
        string chara = W1LoadFemale(c);
        // W1's mutating commands
        W1History1(c, new { cmd = "obj.transform", key = item, pos = new[] { 1, 2, 3 } }, "Transform", "obj.transform",
            () => new { cmd = "obj.transform", key = item, pos = new[] { 2, 2, 3 }, undo = false });
        W1History1(c, new { cmd = "map.load", no = 10 }, "Map", "map.load", () => new { cmd = "map.load", clear = true, undo = false });
        W1History1(c, new { cmd = "item.add", no = 399 }, "Add item", "item.add");
        string folderItem = W1AddItem(c, "item.add (parent)");
        W1History1(c, new { cmd = "item.parent", key = folderItem, parent = item }, "Parent", "item.parent (under a node)");
        if (chara != null) W1History1(c, new { cmd = "item.parent", key = folderItem, chara, point = "N_Hand_R" }, "Parent", "item.parent (attach point)");
        W1History1(c, new { cmd = "delete", key = folderItem }, "Delete", "delete");
        // view and runtime switches record nothing
        int n = W1UndoCount(c);
        c.Ok(new { cmd = "camera.set", target = new[] { 0, 9, 0 }, dist = 20 }, "camera.set");
        c.Ok(new { cmd = "camera.frame", key = item }, "camera.frame");
        c.Ok(new { cmd = "camera.state", set = new { pos = new[] { 0, 10, -30 }, rot = new[] { 0, 0, 0 } } }, "camera.state set");
        c.Ok(new { cmd = "app.options", fk = new { hipsOffset = true } }, "app.options");
        c.Check(W1UndoCount(c) == n, "camera.*, app.options record nothing", n + " -> " + W1UndoCount(c));
        // undo:false on a structural command clears the history, with a warning
        var cl = c.Call(new { cmd = "item.add", no = 399, undo = false });
        c.Check(cl.Value<bool?>("ok") == true && W1UndoCount(c) == 0, "item.add with undo:false clears the history", Ctx.Short(c.LastText));
        c.Check((cl["warnings"] as JArray)?.Any(w => ((string)w).StartsWith("history cleared")) == true, "… with the \"history cleared\" warning", Ctx.Short(c.LastText));
        // package W2's mutating commands (integration)
        if (chara == null) return;
        var w2 = new (string cmd, object req, string label)[]
        {
            ("chara.anime", new { cmd = "chara.anime", key = chara, group = 19, category = 0, no = 0, time = 0.25 }, "Animation"),
            ("chara.speed", new { cmd = "chara.speed", key = chara, speed = 0.5 }, "Animation speed"),
            ("chara.animeOption", new { cmd = "chara.animeOption", key = chara, visible = false }, "Animation options"),
            ("chara.face", new { cmd = "chara.face", key = chara, mouth = 3, tears = 0.5 }, "Face"),
            ("chara.hands", new { cmd = "chara.hands", key = chara, left = 2 }, "Hands"),
            ("look.target", new { cmd = "look.target", key = chara, pos = new[] { 1, 15, 10 } }, "Look target"),
            ("fk.set", new { cmd = "fk.set", key = chara, bones = new Dictionary<string, float[]> { ["cf_J_Spine01"] = new[] { 10f, 0f, 0f } } }, "FK bones"),
            ("fk.enable", new { cmd = "fk.enable", key = chara }, "FK"),
            ("fk.capture", new { cmd = "fk.capture", key = chara }, "FK from pose"),
            ("ik.enable", new { cmd = "ik.enable", key = chara }, "IK"),
            ("chara.motion", new { cmd = "chara.motion", key = chara, correction = new { flags = new[] { true, true, true, true, true, true, true, true } } }, "Joint correction"),
        };
        foreach (var (cmd, req, label) in w2)
        {
            if (!W1Has(c, cmd)) { c.Warn(cmd + " not registered (package W2)"); continue; }
            W1History1(c, req, label, cmd);
        }
        if (W1Has(c, "chara.load")) W1History1(c, new { cmd = "chara.load", path = c.MaleRel, sex = "male" }, "Add character", "chara.load");
    }

    // ---------------------------------------------------------------- C5 batch

    static void C5(Ctx c)
    {
        c.Host();
        string item = W1AddItem(c);
        int n0 = W1UndoCount(c);
        var b = c.Ok(new
        {
            cmd = "batch",
            cmds = new object[]
            {
                new { cmd = "obj.transform", key = item, pos = new[] { 1, 0, 0 } },
                new { cmd = "obj.transform", key = item, rot = new[] { 0, 45, 0 } },
                new { cmd = "item.add", no = 399, @as = "second" },
                new { cmd = "obj.transform", key = "$second.added.0.key", pos = new[] { 0, 0, 3 } },
                new { cmd = "map.load", no = 10 },
            },
        }, "a batch of 5 mutating members");
        c.Check(W1UndoCount(c) == n0 + 1 && W1TopLabel(c) == "Batch", "… records one entry \"Batch\"", n0 + " -> " + W1UndoCount(c) + " " + W1TopLabel(c));
        string second = (string)b["results"]?[2]?["added"]?[0]?["key"];
        c.Ok(new { cmd = "undo" }, "undo the batch");
        var sc = c.Call(new { cmd = "scene" });
        c.Check((sc["objects"] as JArray)?.Count == 1 && (int)sc["map"]["no"] == -1, "one undo reverts every member", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "redo" }, "redo the batch");
        sc = c.Call(new { cmd = "scene" });
        c.Check((sc["objects"] as JArray)?.Count == 2 && second != null, "redo replays every member");
        // a failing third member: members 1-2 stay applied, one entry, failedAt 2
        int n1 = W1UndoCount(c);
        var f = c.Call(new
        {
            cmd = "batch",
            cmds = new object[]
            {
                new { cmd = "obj.transform", key = item, pos = new[] { 5, 0, 0 } },
                new { cmd = "obj.transform", key = item, rot = new[] { 0, 90, 0 } },
                new { cmd = "obj.transform", key = "0123456789abcdef0123456789abcdef", pos = new[] { 0, 0, 0 } },
                new { cmd = "obj.transform", key = item, pos = new[] { 9, 9, 9 } },
            },
        });
        c.Check(f.Value<bool?>("ok") == false && f.Value<int?>("failedAt") == 2, "a failing third member → ok false, failedAt 2", Ctx.Short(c.LastText));
        sc = c.Call(new { cmd = "scene" });
        var obj = (sc["objects"] as JArray)?.FirstOrDefault(o => (string)o["key"] == item);
        c.Check(obj != null && W1Dist(W1V3(obj["pos"]), new Vector3(5, 0, 0)) < 1e-4 && Math.Abs((double)obj["rot"][1] - 90) < 1e-3, "members 1-2 stay applied, member 4 did not run", Ctx.Short(obj?.ToString()));
        c.Check(W1UndoCount(c) == n1 + 1 && W1TopLabel(c) == "Batch", "… as one entry", n1 + " -> " + W1UndoCount(c));
        // undo / redo inside a batch answer busy
        var u = c.Call(new { cmd = "batch", cmds = new object[] { new { cmd = "obj.transform", key = item, pos = new[] { 6, 0, 0 } }, new { cmd = "undo" } } });
        c.Check(u.Value<bool?>("ok") == false && (string)u["results"]?[1]?["code"] == "busy", "undo inside a batch → busy", Ctx.Short(c.LastText));
        // FK and pose loads join the batch's transaction (package W2)
        string chara = W1LoadFemale(c);
        if (chara == null || !W1Has(c, "fk.enable", "pose.load")) return;
        int n2 = W1UndoCount(c);
        string pose = W1PoseCase1(c);
        var cmds = new List<object>
        {
            new { cmd = "obj.transform", key = chara, pos = new[] { 0, 0, 1 } },
            new { cmd = "fk.enable", key = chara },
        };
        if (pose != null) cmds.Add(new { cmd = "pose.load", key = chara, path = pose });
        var j = c.Call(new { cmd = "batch", cmds });
        c.Check(j.Value<bool?>("ok") == true, "fk.enable" + (pose != null ? " and pose.load" : "") + " inside a batch succeed", Ctx.Short(c.LastText));
        c.Check(W1UndoCount(c) == n2 + 1 && W1TopLabel(c) == "Batch", "… one entry", n2 + " -> " + W1UndoCount(c) + " " + W1TopLabel(c));
    }

    /// <summary>The pose file of compare case 1 (Tools/compare_poses.json), relative to the pose folder, or null.</summary>
    static string W1PoseCase1(Ctx c)
    {
        var f = Path.Combine(ContentSettings.ProjectRoot, "Tools", "compare_poses.json");
        if (!File.Exists(f)) { c.Warn("Tools/compare_poses.json missing"); return null; }
        var j = JToken.Parse(File.ReadAllText(f));
        var cases = j is JObject o ? o["cases"] ?? o["poses"] : j;
        var first = (cases as JArray)?.FirstOrDefault();
        string p = (string)(first?["file"] ?? first?["path"]);
        if (p == null) c.Warn("compare case 1 has no file member");
        return p;
    }

    // ---------------------------------------------------------------- C6 BeginOrJoin (the model group)

    static void C6(Ctx c)
    {
        var t = new HS2Next.Save.Tests.TestRunner { FixturesDir = Path.Combine(ContentSettings.ProjectRoot, "Tests"), Group = "SceneTests_Join" };
        t.TempDir = Path.Combine(OutDir, "join_tmp");
        Directory.CreateDirectory(t.TempDir);
        HS2Next.Scenes.Tests.SceneTests_Join.Run(t);
        foreach (var f in t.Failures) c.Info("join: " + f);
        c.Check(t.Failed == 0 && t.Passed > 0, $"SceneTests_Join: {t.Passed} passed, {t.Failed} failed");
    }

    // ---------------------------------------------------------------- C7 scene files

    static void C7(Ctx c)
    {
        var app = c.Host();
        foreach (var f in Directory.GetFiles(app.Paths.SceneDir, "c7_*" + HS2Next.App.Commands.SceneCommands.SceneExt, SearchOption.AllDirectories)) File.Delete(f);
        c.Ok(new { cmd = "scene.new" }, "scene.new");
        W1AddItem(c);
        var s = c.Ok(new { cmd = "scene.save", name = "c7_scene", clock = "2026-10-16T00:00:00Z" }, "scene.save {name}");
        string path = (string)s["path"];
        c.Check(path != null && File.Exists(path) && Path.GetDirectoryName(path) == Path.GetFullPath(app.Paths.SceneDir) && path.EndsWith("c7_scene.hs2nscene"),
            "the file is in userdata/scene with .hs2nscene appended", path);
        c.Check((string)s["name"] == "c7_scene.hs2nscene" && s.Value<bool?>("dirty") == false && (string)s["thumbnail"] == "keep", "reply name, dirty false, thumbnail keep (headless)", Ctx.Short(c.LastText));
        c.Fails(new { cmd = "scene.save", name = "c7_scene" }, "io_denied", "again without overwrite → io_denied");
        c.Ok(new { cmd = "scene.save", name = "c7_scene", overwrite = true }, "again with overwrite");
        c.Fails(new { cmd = "scene.save", path = "../c7_x" }, "io_denied", "path ../x → io_denied");
        c.Fails(new { cmd = "scene.save", path = "C:/c7_x" }, "io_denied", "path C:/x → io_denied");
        c.Fails(new { cmd = "scene.save", name = "sub/c7_x" }, "bad_args", "name with a folder → bad_args");
        c.Fails(new { cmd = "scene.save" }, "bad_args", "neither name nor path → bad_args");
        c.Ok(new { cmd = "scene.save", path = "c7_sub/c7_nested" }, "path with a subfolder");
        var l = c.Ok(new { cmd = "scene.list", q = "c7_" }, "scene.list");
        var names = (l["scenes"] as JArray)?.Select(x => (string)x["name"]).ToList() ?? new List<string>();
        c.Check(names.Contains("c7_scene.hs2nscene") && names.Contains("c7_sub/c7_nested.hs2nscene"), "scene.list lists both", string.Join(", ", names));
        string before = W1DocSha(c);
        c.Fails(new { cmd = "scene.load", name = "c7_missing" }, "not_found", "scene.load of a missing name → not_found");
        c.Check(W1DocSha(c) == before, "… the scene is untouched");
        c.Ok(new { cmd = "scene.new" }, "scene.new");
        var ld = c.Ok(new { cmd = "scene.load", name = "c7_scene" }, "scene.load {name}");
        c.Check(ld.Value<int?>("nodes") == 1 && W1UndoCount(c) == 0, "the loaded scene has its node; history cleared", Ctx.Short(c.LastText));
        c.Ok(new { cmd = "scene.load", path = path }, "scene.load {path absolute}");
        c.Fails(new { cmd = "scene.load", path = "C:/Windows/win.ini" }, "io_denied", "an absolute path of another type → io_denied");
    }

    // ---------------------------------------------------------------- C8 scene.doc determinism

    static string W1C8Sequence(Ctx c, out string withCapture)
    {
        c.Ok(new { cmd = "scene.new" }, "scene.new");
        string item = W1AddItem(c);
        c.Ok(new { cmd = "obj.transform", key = item, pos = new[] { 1.25, 0, -2 }, rot = new[] { 0, 33.3, 0 } }, "obj.transform");
        c.Ok(new { cmd = "map.load", no = 10 }, "map.load");
        c.Ok(new { cmd = "camera.set", target = new[] { 0, 11, 0 }, rot = new[] { 5, 170, 0 }, dist = 22, fov = 30 }, "camera.set");
        string chara = W1LoadFemale(c);
        if (chara != null && W1Has(c, "chara.anime"))
        {
            c.Ok(new { cmd = "chara.anime", key = chara, group = 19, category = 0, no = 0, time = 0.3 }, "chara.anime");
            c.Ok(new { cmd = "chara.motion", key = chara, paused = true }, "chara.motion paused");
        }
        c.Frames(5);
        var a = c.Call(new { cmd = "scene.doc", capture = false });
        var b = c.Call(new { cmd = "scene.doc", capture = true });
        withCapture = (string)b["sha256"];
        return (string)a["sha256"];
    }

    static void C8(Ctx c)
    {
        var h1 = c.Host(DefaultSeed);
        string a1 = W1C8Sequence(c, out string a2);
        c.Check(a1 != null && a1 == a2, "capture:false and capture:true give the same text while paused", a1 + " / " + a2);
        c.Dispose(h1);
        c.Host(DefaultSeed);
        string b1 = W1C8Sequence(c, out string b2);
        c.Check(b1 == b2, "host 2: capture:false = capture:true", b1 + " / " + b2);
        c.Check(a1 == b1, "two fresh hosts with the same seed give the same scene.doc (sha256)", a1 + " / " + b1);
    }

    // ---------------------------------------------------------------- C9 maps, items, camera (edit mode)

    static void C9(Ctx c)
    {
        var app = c.Host();
        var maps = c.Ok(new { cmd = "maps", limit = 1000 }, "maps");
        var m10 = (maps["maps"] as JArray)?.FirstOrDefault(m => (int)m["no"] == 10 && (string)m["guid"] == "illusion.hs2");
        c.Check(m10 != null && (string)m10["id"] == "illusion.hs2#10", "maps lists vanilla 10 with id illusion.hs2#10", Ctx.Short(m10?.ToString()));
        int n0 = W1UndoCount(c);
        var ml = c.Ok(new { cmd = "map.load", no = 10 }, "map.load {no:10}");
        c.Check((string)ml["state"] == "Unsupported" && (ml["warnings"] as JArray)?.Any(w => ((string)w).Contains("play mode")) == true && W1UndoCount(c) == n0 + 1,
            "edit mode: state Unsupported, a warning, one entry", Ctx.Short(c.LastText));
        c.Check((string)ml["ref"] == "illusion.hs2#10", "map.load reply ref = illusion.hs2#10", (string)ml["ref"]);
        var byRef = c.Ok(new { cmd = "map.load", @ref = "illusion.hs2#10", wait = false }, "map.load {ref} wait:false");
        c.Check((string)byRef["note"] != null, "wait:false replies with HS2's note");
        c.Fails(new { cmd = "map.load", no = 100000946 }, "not_found", "a Sideloader runtime number → not_found");
        // items
        app.Db.TryGetItem(ContentId.Vanilla(399), out var row399);
        string q = row399?.Name ?? "";
        var s = c.Ok(new { cmd = "item.search", q, limit = 1000 }, "item.search {q: the name of item 399}");
        c.Check((s["items"] as JArray)?.Any(x => (string)x["id"] == "illusion.hs2#399") == true, "item.search finds illusion.hs2#399", q);
        string item = W1AddItem(c);
        c.Ok(new { cmd = "obj.transform", key = item, pos = new[] { 3, 4, 5 }, rot = new[] { 0, 20, 0 } }, "obj.transform item");
        c.Fails(new { cmd = "item.parent", key = item, chara = item, point = "48" }, "wrong_kind", "item.parent onto an item → wrong_kind");
        string chara = W1LoadFemale(c);
        if (chara != null)
        {
            c.Ok(new { cmd = "obj.transform", key = chara, pos = new[] { -1, 0, 2 }, rot = new[] { 0, 150, 0 } }, "obj.transform chara");
            var before = W1V3(W1Obj(c, item)["worldPos"]);
            var p = c.Ok(new { cmd = "item.parent", key = item, chara, point = "48" }, "item.parent {chara, point:\"48\"}");
            c.Check((string)p["parent"] == "N_Hand_R" && (string)p["bone"] == "N_Hand_R", "point 48 = N_Hand_R", Ctx.Short(c.LastText));
            var after = W1V3(W1Obj(c, item)["worldPos"]);
            c.Check(W1Dist(before, after) <= 1e-4, "item.parent keeps the world position (± 1e-4)", before + " → " + after);
            c.Ok(new { cmd = "obj.transform", key = item, pos = new[] { 0, 0, 0 }, rot = new[] { 0, 0, 0 } }, "obj.transform {0,0,0}");
            var hand = app.Runtime.AttachTransformFor(chara, "N_Hand_R");
            var at = W1V3(W1Obj(c, item)["worldPos"]);
            c.Check(hand && hand.name == "N_Hand_R" && W1Dist(at, hand.position) <= 1e-4, "… puts it at N_Hand_R (± 1e-4)", at + " vs " + (hand ? hand.position.ToString() : "none"));
            var bad = c.Fails(new { cmd = "item.parent", key = item, chara, point = "右手" }, "bad_args", "an unknown point name → bad_args");
            c.Check((bad["detail"]?["candidates"] as JArray)?.All(x => ((string)x).StartsWith("N_")) == true, "… with N_* candidates", Ctx.Short(c.LastText));
            var un = c.Ok(new { cmd = "item.parent", key = item }, "item.parent without chara/parent → unparent");
            c.Check((string)un["parent"] == "" && W1Obj(c, item)["parent"] == null, "the item is at the root again", Ctx.Short(c.LastText));
        }
        // camera
        int n1 = W1UndoCount(c);
        var set = c.Ok(new { cmd = "camera.set", target = new[] { 1, 12, -3 }, rot = new[] { 15, 160, 0 }, dist = -18, fov = 28 }, "camera.set");
        var get = c.Ok(new { cmd = "camera.get" }, "camera.get");
        c.Check(JToken.DeepEquals(set["camera"], get["camera"]), "camera.get equals camera.set's reply", Ctx.Short(get.ToString()));
        c.Check(W1Dist(W1V3(get["camera"]["target"]), new Vector3(1, 12, -3)) < 1e-4 && Math.Abs((double)get["camera"]["dist"] - 18) < 1e-4 && Math.Abs((double)get["camera"]["fov"] - 28) < 1e-4,
            "camera.get values: target, |dist|, fov", Ctx.Short(get["camera"]?.ToString()));
        var cur = get["camera"];
        var expected = new Vector3(1, 12, -3) + Quaternion.Euler(15, 160, 0) * new Vector3(0, 0, -18);
        c.Check(W1Dist(W1V3(cur["worldPos"]), expected) < 1e-3, "worldPos = pivot + Euler(rot)·(0, 0, −dist)", W1V3(cur["worldPos"]) + " vs " + expected);
        c.Check(W1UndoCount(c) == n1, "camera.set records nothing");
        var st = c.Ok(new { cmd = "camera.state", set = new { pos = new[] { 2, 11, -25 }, rot = new[] { 0, 0.258819, 0, 0.9659258 }, dist = 15 } }, "camera.state set (quaternion)");
        c.Check(W1Dist(W1V3(st["camera"]["position"]), new Vector3(2, 11, -25)) <= 1e-4, "camera.state set: position = requested ± 1e-4 in the same call", Ctx.Short(st["camera"]?.ToString()));
        c.Check(st["note"] != null && st["requested"] != null && st.Properties().First(x => x.Name == "note").Name == "note", "note and requested come with set");
        var keys = (st["camera"] as JObject)?.Properties().Select(x => x.Name).ToList();
        var hs2 = new[] { "name", "position", "rotation", "euler", "forward", "up", "fov", "aspect", "near", "far", "orthographic", "pixelWidth", "pixelHeight", "screen", "clearFlags", "background", "cullingMask", "allowHDR", "allowMSAA", "renderingPath", "frame", "studio" };
        c.Check(keys != null && keys.Take(hs2.Length).SequenceEqual(hs2), "camera.state members in HS2's order", keys == null ? "" : string.Join(",", keys));
        var fr = c.Ok(new { cmd = "camera.frame", key = item, dist = 12 }, "camera.frame {key}");
        var holder = app.Runtime.HolderOf(item);
        c.Check(holder && W1Dist(W1V3(fr["camera"]["target"]), holder.position) <= 1e-4, "camera.frame target = the holder's position", W1V3(fr["camera"]["target"]) + " vs " + (holder ? holder.position.ToString() : "none"));
        c.Fails(new { cmd = "capture" }, "unsupported", "capture headless → unsupported");
        var lights = c.Ok(new { cmd = "scene.lights" }, "scene.lights");
        c.Check((lights["lights"] as JArray)?.Count >= 1 && lights["renderSettings"]?["ambientProbe"] is JArray pr && pr.Count == 27, "scene.lights: lights and a 27-value ambient probe", Ctx.Short(c.LastText));
    }

    static JToken W1Obj(Ctx c, string key) =>
        (c.Call(new { cmd = "scene" })["objects"] as JArray)?.FirstOrDefault(o => (string)o["key"] == key) ?? new JObject();

    /// <summary><paramref name="path"/> relative to <paramref name="dir"/> with '/' separators.</summary>
    public static string RelTo(string path, string dir)
    {
        var p = Path.GetFullPath(path);
        var d = Path.GetFullPath(dir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return (p.StartsWith(d, StringComparison.OrdinalIgnoreCase) ? p.Substring(d.Length) : p).Replace('\\', '/');
    }
}
