// Batch test of the scene realization (tests R1–R9; Package B).
// Run: -executeMethod SceneRuntimeTest.Run [-only R1,R3] (headless, -nographics, edit mode, ManualStepping).
// Fixture (built through the session API and SceneOps, deterministic ids): BR-Chan (root, origin), HERO107 (root,
// pos (10, 0, 0), rot (0, 180, 0)); item A = the first vanilla item row whose prefab has ItemComponent.childRoot set
// (row order, ≤ 50 prefab loads); item B = illusion.hs2#399 at BR-Chan's N_Hand_R; item C = #399 under A; light
// illusion.hs2#2 under A; folder F with camera K and route R: 9 nodes. Cards and animation rows come from
// Tools/targets.json (SFW visual targets; Idle_00 19/0/0 and attack_01 15/31/1 with its rifle option item).
//   R1 build         R2 URP incl. option items      R3 attach + keep-world   R4 inheritScale
//   R5 visibility    R6 delete + undo (graveyard and rebuild)    R7 save → load → save
//   R8 timings (S10/200)   R9 findings (crafted document from the golden scene; 2.3: the golden subtree builds from
//   documents, Unbuildable only with SubtreeCharaBuilder unregistered)
// Phase 2.3 (SceneRuntimeTest_Import.cs): R7b import at add + save/load, R10 accessory
// ids / visibility / undo / duplicate / findings, R11 a card-backed node (model API) saved with its import, R12
// AddCharaFromFile, R13 a failing importer. Since Phase 4.6 an importer is always available in a Unity runtime
// (ImportSetup.ImporterOf): R1–R9 run with the importer unregistered and their characters are imported at the add.
// Report: Spike/out/scene/scene_runtime_report.txt, first line "SUMMARY: N PASS, M FAIL"; EditorApplication.Exit(1) on
// any FAIL. Images are never rendered or judged here (Package C's RunRender / RunPlay live in partial-class files).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Motion.Look;
using HS2Next.Save;
using HS2Next.Save.Tests;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static partial class SceneRuntimeTest
{
    // ================================================================ shared fixture (also used by RunRender / RunPlay)

    public sealed class Fixture : IDisposable
    {
        public RuntimeServices Services;
        public SceneSession Session;
        public SceneRuntime Runtime;
        public string FemaleCard, MaleCard;
        /// <summary>Node ids of the nine fixture nodes.</summary>
        public string BrChan, Hero, ItemA, ItemB, ItemC, Light, FolderF, CameraK, RouteR;
        /// <summary>The row of item A (first vanilla row with ItemComponent.childRoot) and how many prefabs were loaded to find it.</summary>
        public StudioItem RowA;
        public int RowASearchLoads;
        public static readonly AnimeRef Idle = new AnimeRef(ContentIds.Vanilla, 19, 0, 0);
        public static readonly AnimeRef Attack = new AnimeRef(ContentIds.Vanilla, 15, 31, 1);
        public readonly List<string> Log = new List<string>();
        public readonly List<CommandResult> Failures = new List<CommandResult>();

        public string[] AllIds => new[] { BrChan, Hero, ItemA, ItemB, ItemC, Light, FolderF, CameraK, RouteR };

        public void Dispose()
        {
            Runtime?.Dispose();
            Runtime = null;
        }
    }

    /// <summary>Catalog (vanilla + mod lists), Studio lists, HS2 root from Config/settings.local.json; ManualStepping on.</summary>
    public static RuntimeServices NewServices(StringBuilder log)
    {
        var gd = CardResolveTest.BuildDb(log);
        var db = gd.LoadStudioLists();
        return new RuntimeServices { Catalog = gd, Db = db, Hs2Root = CardResolveTest.GameRoot, Settings = new SceneRuntimeSettings { ManualStepping = true } };
    }

    /// <summary>A session with the Unity-layer options and deterministic ids.</summary>
    public static SceneSession NewSession(RuntimeServices services, ulong idSeed = 0x5ce4e5ce4e5ce4e5UL)
    {
        var o = services.NewSessionOptions();
        o.Ids = new CountingIdSource(idSeed);
        return new SceneSession(o);
    }

    /// <summary>
    /// Builds the nine-node fixture through the session API (one Apply at the end). <paramref name="standardParts"/>:
    /// the runtime gets <see cref="SceneRuntime.AddStandardParts"/> (map, view camera, chara light) before the nodes are added.
    /// </summary>
    public static Fixture BuildFixture(RuntimeServices services, Transform parent = null, bool? drivers = null, bool standardParts = true)
    {
        var t = TestTargets.Load();
        var f = new Fixture { Services = services, FemaleCard = TestTargets.Abs(t.visual.female), MaleCard = TestTargets.Abs(t.visual.male) };
        f.Session = NewSession(services);
        f.Runtime = new SceneRuntime(f.Session, services, parent, drivers ?? false);
        if (standardParts) f.Runtime.AddStandardParts();
        var s = f.Session;
        var ops = f.Runtime.Ops;
        string Ok(CommandResult r, string what)
        {
            if (r == null || r.Status == CommandStatus.Error) { f.Failures.Add(r); f.Log.Add("FAILED " + what + ": " + r); return null; }
            return r.NewId;
        }
        void Must(CommandResult r, string what) { if (r == null || r.Status == CommandStatus.Error) { f.Failures.Add(r); f.Log.Add("FAILED " + what + ": " + r); } }

        f.BrChan = Ok(ops.AddCharaFromCard(f.FemaleCard), "add BR-Chan");
        f.Hero = Ok(ops.AddCharaFromCard(f.MaleCard), "add HERO107");
        if (f.Hero != null)
        {
            Must(s.Set(f.Hero, "transform.pos", new Vec3(10f, 0f, 0f)), "HERO107 pos");
            Must(s.Set(f.Hero, "transform.rot", new Vec3(0f, 180f, 0f)), "HERO107 rot");
        }
        f.RowA = FindItemWithChildRoot(services, out f.RowASearchLoads);
        f.Log.Add("item A: " + (f.RowA != null ? f.RowA.Key + " " + f.RowA.Name + " (" + f.RowA.BundlePath + "/" + f.RowA.FileName + ")" : "none") + " after " + f.RowASearchLoads + " prefab loads");
        if (f.RowA != null) f.ItemA = Ok(ops.AddItem(f.RowA.Key), "add item A");
        if (f.BrChan != null) f.ItemB = Ok(ops.AddItem(ContentId.Vanilla(399), f.BrChan, "N_Hand_R"), "add item B");
        if (f.ItemA != null)
        {
            f.ItemC = Ok(ops.AddItem(ContentId.Vanilla(399), f.ItemA), "add item C");
            f.Light = Ok(ops.AddLight(ContentId.Vanilla(2), f.ItemA), "add light");
        }
        f.FolderF = Ok(s.AddFolder(), "add folder F");
        if (f.FolderF != null)
        {
            f.CameraK = Ok(s.AddCamera(f.FolderF), "add camera K");
            f.RouteR = Ok(s.AddRoute(f.FolderF), "add route R");
        }
        if (f.BrChan != null) Must(s.SetAnimation(f.BrChan, Fixture.Idle), "BR-Chan Idle_00");
        f.Runtime.Apply();
        return f;
    }

    /// <summary>
    /// The first vanilla item row (row order) whose prefab has ItemComponent.childRoot set; at most 50 prefab loads. Rows
    /// whose list column 7 (`子の接続先`, unused by the game) names a child attach object are tried first.
    /// </summary>
    public static StudioItem FindItemWithChildRoot(RuntimeServices services, out int loads)
    {
        loads = 0;
        var rows = services.Db.Items.All.Where(r => r.IsVanilla && r.Visible).ToList();
        var ordered = rows.Where(r => !string.IsNullOrEmpty(r.ChildAttach) && r.ChildAttach != "0").Concat(rows.Where(r => string.IsNullOrEmpty(r.ChildAttach) || r.ChildAttach == "0"));
        foreach (var row in ordered)
        {
            if (loads >= 50) break;
            GameObject prefab = null;
            try { prefab = services.Catalog.LoadAsset<GameObject>(row); } catch (Exception) { }
            loads++;
            var ic = prefab ? prefab.GetComponent<Studio.ItemComponent>() : null;
            if (ic != null && ic.childRoot) return row;
        }
        return null;
    }

    // ================================================================ report

    static StringBuilder rep;
    static int passes, fails;
    static readonly Dictionary<string, (int pass, int fail)> perTest = new Dictionary<string, (int, int)>();
    static string current = "setup";
    static readonly List<string> exceptions = new List<string>();
    static string[] only;

    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "scene");

    static void Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  {current} {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
        perTest.TryGetValue(current, out var c);
        perTest[current] = ok ? (c.pass + 1, c.fail) : (c.pass, c.fail + 1);
    }
    static void Info(string s) => rep.AppendLine("   " + s);
    static void Section(string id, string title) { current = id; rep.AppendLine(); rep.AppendLine("== " + id + " " + title); }

    static bool Want(string id) => only == null || only.Contains(id);

    static void Guard(string id, Action a)
    {
        if (!Want(id)) return;
        int ex0 = exceptions.Count;
        try { a(); }
        catch (Exception e) { current = id; Check("(exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        if (exceptions.Count > ex0)
        {
            current = id;
            Info($"{exceptions.Count - ex0} exception log(s) during {id}: " + string.Join(" | ", exceptions.Skip(ex0).Take(3)));
        }
    }

    static void OnLog(string condition, string stack, LogType type)
    {
        if (type == LogType.Exception) exceptions.Add(condition.Length > 300 ? condition.Substring(0, 300) : condition);
    }

    static string Arg(string name)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    // ================================================================ entry

    public static void Run()
    {
        rep = new StringBuilder(); passes = fails = 0; perTest.Clear(); exceptions.Clear();
        var o = Arg("-only");
        only = o == null ? null : o.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        Directory.CreateDirectory(OutDir);
        Application.logMessageReceived += OnLog;
        var head = new StringBuilder();
        RuntimeServices services = null;
        Fixture f = null;
        var sw = Stopwatch.StartNew();
        try
        {
            services = NewServices(head);
            rep.AppendLine($"scene runtime test (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, play mode {Application.isPlaying})");
            rep.Append(head);
            current = "setup";
            var rootsBefore = SceneManager.GetActiveScene().GetRootGameObjects().ToList();
            var sb = Stopwatch.StartNew();
            int ex0 = exceptions.Count;
            try { f = BuildFixture(services); }
            catch (Exception e) { Check("fixture builds", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
            Section("setup", "fixture");
            if (f != null)
            {
                Info($"fixture built in {sb.ElapsedMilliseconds} ms; " + string.Join("; ", f.Log));
                foreach (var c in f.Runtime.Charas()) Info($"{c.Node.Label}: build {c.BuildMilliseconds:F0} ms, sex {c.Sex}, state {c.State}");
                Check("fixture operations succeed", f.Failures.Count == 0, string.Join("; ", f.Failures.Select(x => x?.ToString() ?? "null")));
                Guard("R1", () => R1(f, rootsBefore, exceptions.Count - ex0));
                Guard("R2", () => R2(f));
                Guard("R3", () => R3(f));
                Guard("R4", () => R4(f));
                Guard("R5", () => R5(f));
                Guard("R6", () => R6(f));
                Guard("R7", () => R7(f));
                f.Dispose();
            }
            Guard("R8", () => R8(services));
            Guard("R9", () => R9(services));
            Guard("R7b", () => R7b(services));
            Guard("R10", () => R10(services));
            Guard("R11", () => R11(services));
            Guard("R12", () => R12(services));
            Guard("R13", () => R13(services));
            Guard("parts", () => Parts(services));
        }
        catch (Exception e) { Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        finally
        {
            Application.logMessageReceived -= OnLog;
            f?.Dispose();
            LookTargets.DefaultCamera = null;
        }
        var sum = new StringBuilder();
        sum.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL ({sw.Elapsed.TotalSeconds:F0} s)");
        foreach (var kv in perTest.OrderBy(k => k.Key, StringComparer.Ordinal)) sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail");
        rep.Insert(0, sum.ToString());
        File.WriteAllText(Path.Combine(OutDir, "scene_runtime_report.txt"), rep.ToString());
        services?.Catalog?.UnloadAll();
        Debug.Log($"SCENE RUNTIME TEST DONE: {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }

    // ================================================================ helpers

    static float VErr(Vector3 a, Vector3 b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Max(Mathf.Abs(a.y - b.y), Mathf.Abs(a.z - b.z)));
    static string F(Vector3 v) => $"({v.x:F5}, {v.y:F5}, {v.z:F5})";
    static byte[] SceneBytes(SceneSession s) => DocIo.Bytes(s.ExportDocument());
    static bool Same(byte[] a, byte[] b) => a != null && b != null && a.SequenceEqual(b);
    static string Diff(byte[] a, byte[] b) => a == null || b == null ? "null" : DocIo.FirstDiff(a, b);

    static bool IsUrp(Material m) => m && m.shader && (m.shader.name.StartsWith("HS2Next/", StringComparison.Ordinal) || m.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal));

    /// <summary>Materials of the own renderers of a holder that are not URP / HS2Next shaders, by shader name.</summary>
    static Dictionary<string, int> NonUrp(Transform holder)
    {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in VisibilityApplier.OwnRenderers(holder))
            foreach (var m in r.sharedMaterials)
            {
                if (!m || IsUrp(m)) continue;
                string n = m.shader ? m.shader.name : "<no shader>";
                d[n] = d.TryGetValue(n, out var k) ? k + 1 : 1;
            }
        return d;
    }

    static string Join(Dictionary<string, int> d) => d.Count == 0 ? "none" : string.Join(", ", d.Select(kv => kv.Key + " x" + kv.Value));

    static Transform Bone(CharaRealization c, string name) => c?.Context?.BodyBones != null && c.Context.BodyBones.TryGetValue(name, out var t) ? t : null;

    static SaveOptions FixedSave() => new SaveOptions { Clock = () => new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc), Thumbnail = ThumbnailMode.Placeholder, AppVersion = "0.1.0" };

    // ================================================================ R1 build

    static void R1(Fixture f, List<GameObject> rootsBefore, int buildExceptions)
    {
        Section("R1", "build");
        var rt = f.Runtime;
        int built = 0;
        var states = new List<string>();
        foreach (var id in f.AllIds)
        {
            var r = id != null ? rt.Find(id) : null;
            if (r != null && r.State == RealizationState.Built) built++;
            states.Add((r?.ToString() ?? "missing " + id));
        }
        Check("9 realizations Built", built == 9 && f.Session.Model.Count == 9, $"{built} built, model {f.Session.Model.Count}: " + string.Join("; ", states.Where(s => !s.Contains(" Built"))));
        Check("0 exceptions during the build", buildExceptions == 0, buildExceptions + " exception logs");
        var errs = f.Session.Findings.Where(x => x.Severity == FindingSeverity.Error).ToList();
        Check("0 error findings", errs.Count == 0, string.Join("; ", errs));
        foreach (var x in f.Session.Findings) Info("finding: " + x);

        // content under holders, holders under the documented attach transforms
        var br = rt.FindChara(f.BrChan); var he = rt.FindChara(f.Hero);
        var a = rt.FindItem(f.ItemA); var b = rt.FindItem(f.ItemB); var c = rt.FindItem(f.ItemC);
        var li = rt.Find(f.Light) as LightRealization; var fo = rt.Find(f.FolderF); var k = rt.Find(f.CameraK); var ro = rt.Find(f.RouteR) as RouteRealization;
        bool content = br?.Root && br.Root.transform.parent == br.Holder && he?.Root && he.Root.transform.parent == he.Holder
                       && a?.Instance && a.Instance.transform.parent == a.Holder && b?.Instance && b.Instance.transform.parent == b.Holder
                       && c?.Instance && c.Instance.transform.parent == c.Holder && li?.Instance && li.Instance.transform.parent == li.Holder
                       && ro?.ChildRoot && ro.ChildRoot.parent == ro.Holder;
        Check("every content under its holder at local identity", content && br.Root.transform.localPosition == Vector3.zero && RotationCompare.Deg(a.Instance.transform.localRotation, Quaternion.identity) == 0);
        var hand = br?.AttachPoints?["N_Hand_R"];
        Check("holders under the attach transforms (B at N_Hand_R, C and light at A's childRoot, K and R in F, roots in Nodes)",
              hand && b?.Holder.parent == hand && c?.Holder.parent == a?.Attach && li?.Holder.parent == a?.Attach && a?.Attach != a?.Instance.transform
              && k?.Holder.parent == fo?.Holder && ro?.Holder.parent == fo?.Holder && br.Holder.parent == rt.NodesRoot && a.Holder.parent == rt.NodesRoot,
              $"B under {b?.Holder.parent?.name}, C under {c?.Holder.parent?.name}, light under {li?.Holder.parent?.name}, A.childRoot {a?.Attach?.name}");
        Check("characters stepped by hand (AutoStep off)", br?.Motion && !br.Motion.AutoStep && he?.Motion && !he.Motion.AutoStep);
        var rootsAfter = SceneManager.GetActiveScene().GetRootGameObjects().ToList();
        var added = rootsAfter.Where(g => !rootsBefore.Contains(g)).ToList();
        Check("active scene roots after the build = before + the runtime root", added.Count == 1 && added[0] == rt.Root && rootsBefore.All(g => !g || rootsAfter.Contains(g)),
              "added: " + string.Join(", ", added.Select(g => g.name)));
        Info($"HERO107 holder {F(he?.Holder.position ?? Vector3.zero)} rot {F(he?.Holder.eulerAngles ?? Vector3.zero)}; graveyard active {rt.GraveyardRoot.gameObject.activeSelf}");
    }

    // ================================================================ R2 URP

    static void R2(Fixture f)
    {
        Section("R2", "URP");
        var rt = f.Runtime;
        var br = rt.FindChara(f.BrChan);
        foreach (var id in new[] { f.BrChan, f.Hero, f.ItemA, f.ItemB, f.ItemC })
        {
            var r = rt.Find(id);
            var bad = NonUrp(r?.Holder);
            Check($"{r?.Node.Label}: every own material URP / HS2Next", r != null && bad.Count == 0, Join(bad));
        }
        var res = f.Session.SetAnimation(f.BrChan, Fixture.Attack);
        rt.Apply();
        var items = br.Animation.Items;
        var objs = items.Objects();
        int nonUrp = 0, parentless = 0, notInContainer = 0;
        var mats1 = new HashSet<Material>();
        foreach (var o in objs)
            foreach (var r in o.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials) { if (!m) continue; mats1.Add(m); if (!IsUrp(m)) nonUrp++; }
        foreach (var it in items.Items)
        {
            if (!it.Root) continue;
            if (it.Root.transform.parent == null) parentless++;
            bool moved = it.Moved.Any(mv => mv.moved == it.Root.transform);
            if (!moved && it.Root.transform.parent != br.OptionItems) notInContainer++;
        }
        Check("attack_01 loaded with ≥ 1 option item", res.Ok && br.Animation.IsLoaded && items.Count >= 1 && objs.Count >= 1, $"{res}; items {items.Count}, objects {objs.Count}, hook calls {br.OptionItemLoads}");
        Check("option item materials all URP", nonUrp == 0 && mats1.Count > 0, $"{mats1.Count} materials, {nonUrp} not URP");
        Check("parentless option item roots now under OptionItems", parentless == 0 && notInContainer == 0, $"parentless {parentless}, roots outside the container {notInContainer}");
        f.Session.SetAnimation(f.BrChan, Fixture.Idle);
        rt.Apply();
        f.Session.SetAnimation(f.BrChan, Fixture.Attack);
        rt.Apply();
        var mats2 = new HashSet<Material>();
        int nonUrp2 = 0;
        foreach (var o in br.Animation.Items.Objects())
            foreach (var r in o.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials) { if (!m) continue; mats2.Add(m); if (!IsUrp(m)) nonUrp2++; }
        Check("after Idle_00 and back: still URP, same number of distinct material instances (no double copies)", nonUrp2 == 0 && mats2.Count == mats1.Count && !mats2.Overlaps(mats1),
              $"first {mats1.Count}, again {mats2.Count}, not URP {nonUrp2}");
        var bad2 = NonUrp(br.Holder);
        Check("BR-Chan own materials still URP (no re-mapping of the character root)", bad2.Count == 0, Join(bad2));
        f.Session.SetAnimation(f.BrChan, Fixture.Idle);
        rt.Apply();
        Check("back on Idle_00: option items released", br.Animation.Items.Count == 0 && br.OptionItems.childCount == 0, $"items {br.Animation.Items.Count}, container children {br.OptionItems.childCount}");
    }

    // ================================================================ R3 attach and keep-world

    static void R3(Fixture f)
    {
        Section("R3", "attach and keep-world");
        var rt = f.Runtime;
        var br = rt.FindChara(f.BrChan);
        var b = rt.FindItem(f.ItemB);
        var hand = br.AttachPoints["N_Hand_R"];
        rt.Apply();
        Check("B world position = N_Hand_R after Apply", VErr(b.Holder.position, hand.position) <= 1e-4f, $"{F(b.Holder.position)} vs {F(hand.position)}");
        var p0 = b.Holder.position; var q0 = b.Holder.rotation;
        var r1 = rt.Ops.MoveKeepWorld(f.ItemB, null, null);
        Check("MoveKeepWorld B → root: ok, holder under Nodes", r1.Ok && b.Holder.parent == rt.NodesRoot && f.Session.Model.Find(f.ItemB).Parent == null, r1.ToString());
        Check("world position kept ±1e-4, rotation ±0.01°", VErr(b.Holder.position, p0) <= 1e-4f && RotationCompare.Deg(b.Holder.rotation, q0) <= 0.01f,
              $"{F(b.Holder.position)} vs {F(p0)}, {Quaternion.Angle(b.Holder.rotation, q0):F4}°");
        var r2 = rt.Ops.MoveKeepWorld(f.ItemB, f.BrChan, "N_Hand_R");
        Check("MoveKeepWorld B → BR-Chan N_Hand_R: ok, holder under N_Hand_R", r2.Ok && b.Holder.parent == hand, r2.ToString());
        Check("world position kept ±1e-4, rotation ±0.01° (back)", VErr(b.Holder.position, p0) <= 1e-4f && RotationCompare.Deg(b.Holder.rotation, q0) <= 0.01f,
              $"{F(b.Holder.position)} vs {F(p0)}, {Quaternion.Angle(b.Holder.rotation, q0):F4}°");
        var hp0 = hand.position;
        for (int i = 0; i < 30; i++) rt.Tick(1f / 30f);
        Check("after 30 Tick(1/30) with Idle_00: B still at N_Hand_R ±1e-4 (and the hand moved)", VErr(b.Holder.position, hand.position) <= 1e-4f,
              $"{F(b.Holder.position)} vs {F(hand.position)}; hand moved {VErr(hp0, hand.position):F5}");

        // DynamicBoneSetup.AddParts (repeated by DynamicBoneStage.Rebuild) skips scene node holders under the body.
        var dyn = br.Motion.Get<HS2Next.Motion.DynamicBones.DynamicBoneStage>();
        if (dyn == null) { Check("character has a DynamicBoneStage", false); return; }
        int n0 = dyn.Chains.Count;
        var probe = new GameObject("probe holder");
        probe.AddComponent<SceneNodeHolder>().NodeId = "probe";
        var bone = new GameObject("probe bone");
        bone.transform.SetParent(probe.transform, false);
        bone.AddComponent<DynamicBone>();
        probe.transform.SetParent(hand, false);
        try
        {
            dyn.Rebuild();
            int n1 = dyn.Chains.Count;
            Object.DestroyImmediate(probe.GetComponent<SceneNodeHolder>());
            dyn.Rebuild();
            int n2 = dyn.Chains.Count;
            Check("DynamicBoneStage.Rebuild skips a DynamicBone inside a scene node holder at N_Hand_R (control without the marker: one more chain)",
                  n1 == n0 && n2 == n0 + 1, $"chains {n0} → {n1} with holder, {n2} without");
        }
        finally
        {
            Object.DestroyImmediate(probe);
            dyn.Rebuild();
        }
        Check("chains back to the character's own after the probe is gone", dyn.Chains.Count == n0, $"{dyn.Chains.Count} vs {n0}");
    }

    // ================================================================ R4 inheritScale

    static void R4(Fixture f)
    {
        Section("R4", "inheritScale");
        var rt = f.Runtime; var s = f.Session;
        var a = rt.FindItem(f.ItemA); var c = rt.FindItem(f.ItemC); var b = rt.FindItem(f.ItemB); var br = rt.FindChara(f.BrChan);
        s.Set(f.ItemA, "transform.scale", new Vec3(2f, 2f, 2f));
        rt.Tick(1f / 30f);
        Info($"A holder lossy {F(a.Holder.lossyScale)}, A childRoot lossy {F(a.Attach.lossyScale)} (childRoot local {F(a.Attach.localScale)})");
        Check("C (inheritScale false, scale 1) lossy (1,1,1) after Tick", VErr(c.Holder.lossyScale, Vector3.one) <= 1e-4f, F(c.Holder.lossyScale));
        s.Set(f.ItemC, "transform.inheritScale", true);
        rt.Tick(1f / 30f);
        var expect = a.Attach.lossyScale;
        Check("C inheritScale true → lossy = A's attach lossy scale (2,2,2 for a unit childRoot)", VErr(c.Holder.lossyScale, expect) <= 1e-4f && VErr(expect, new Vector3(2, 2, 2)) <= 1e-4f,
              $"{F(c.Holder.lossyScale)} vs attach {F(expect)}");
        s.Set(f.ItemC, "transform.inheritScale", false);
        s.Set(f.BrChan, "transform.scale", new Vec3(1.5f, 1.5f, 1.5f));
        for (int i = 0; i < 10; i++) rt.Tick(1f / 30f);
        Check("BR-Chan scale 1.5: holder lossy 1.5", VErr(br.Holder.lossyScale, new Vector3(1.5f, 1.5f, 1.5f)) <= 1e-4f, F(br.Holder.lossyScale));
        Check("B (inheritScale false) lossy (1,1,1) after 10 ticks", VErr(b.Holder.lossyScale, Vector3.one) <= 1e-4f, $"{F(b.Holder.lossyScale)}; N_Hand_R lossy {F(b.Holder.parent.lossyScale)}");
        var u = s.Commands.Undo();
        rt.Tick(1f / 30f);
        var st = s.Model.Find(f.BrChan).Doc.Transform.Scale;
        Check("undo restores BR-Chan scale 1.5 → 1", u.Ok && st.X == 1f && st.Y == 1f && st.Z == 1f && VErr(br.Holder.lossyScale, Vector3.one) <= 1e-5f, $"{u}; model ({st.X}, {st.Y}, {st.Z}), lossy {F(br.Holder.lossyScale)}");
        Check("B lossy (1,1,1) after the undo", VErr(b.Holder.lossyScale, Vector3.one) <= 1e-4f, F(b.Holder.lossyScale));
        Info($"attached nodes enforced per LateApply: {rt.Scale.Count}");
    }

    // ================================================================ R5 visibility

    static void R5(Fixture f)
    {
        Section("R5", "visibility");
        var rt = f.Runtime; var s = f.Session;
        var br = rt.FindChara(f.BrChan); var he = rt.FindChara(f.Hero); var b = rt.FindItem(f.ItemB);
        var optionRenderers = new HashSet<Renderer>(br.Animation.Items.Items.SelectMany(i => i.Renderers));
        var enabled0 = rt.Root.GetComponentsInChildren<Renderer>(true).Where(r => !optionRenderers.Contains(r)).ToDictionary(r => r, r => r.enabled);
        List<Renderer> Own(NodeRealization r) => VisibilityApplier.OwnRenderers(r.Holder);
        int Off(List<Renderer> l) => l.Count(r => r.forceRenderingOff);
        var brR = Own(br); var heR = Own(he); var bR = Own(b);
        Info($"own renderers: BR-Chan {brR.Count}, HERO107 {heR.Count}, B {bR.Count}");

        s.Set(f.BrChan, "visible", false); rt.Apply();
        Check("hide BR-Chan: all its own renderers off, B's too, HERO107's none", brR.Count > 0 && Off(brR) == brR.Count && Off(bR) == bR.Count && Off(heR) == 0,
              $"BR {Off(brR)}/{brR.Count}, B {Off(bR)}/{bR.Count}, HERO {Off(heR)}/{heR.Count}; B effective {s.Model.Find(f.ItemB).EffectiveVisible}");
        Check("CharaAnimation.CharacterVisible follows", !br.Animation.CharacterVisible);
        s.Set(f.BrChan, "visible", true); rt.Apply();
        Check("show: all cleared", Off(brR) == 0 && Off(bR) == 0 && Off(heR) == 0 && br.Animation.CharacterVisible);
        s.Set(f.ItemB, "visible", false); rt.Apply();
        Check("hide B only: BR-Chan unaffected", Off(brR) == 0 && Off(bR) == bR.Count, $"BR {Off(brR)}, B {Off(bR)}/{bR.Count}");
        s.Set(f.BrChan, "visible", false); rt.Apply();
        s.Set(f.BrChan, "visible", true); rt.Apply();
        Check("hidden child under a re-shown parent stays hidden", Off(brR) == 0 && Off(bR) == bR.Count);
        s.Set(f.ItemB, "visible", true); rt.Apply();
        s.Set(f.FolderF, "visible", false); rt.Apply();
        Check("folder F hidden: K and R effective false", !s.Model.Find(f.CameraK).EffectiveVisible && !s.Model.Find(f.RouteR).EffectiveVisible);
        s.Set(f.FolderF, "visible", true); rt.Apply();
        Check("folder F shown: K and R effective true", s.Model.Find(f.CameraK).EffectiveVisible && s.Model.Find(f.RouteR).EffectiveVisible);
        int changed = enabled0.Count(kv => kv.Key && kv.Key.enabled != kv.Value);
        Check("renderer.enabled never changed by visibility (option item renderers excluded: CharacterVisible drives them)", changed == 0, $"{changed} of {enabled0.Count} changed");
    }

    // ================================================================ R6 delete + undo

    static void R6(Fixture f)
    {
        Section("R6", "delete + undo");
        var rt = f.Runtime; var s = f.Session;
        var br = rt.FindChara(f.BrChan); var b = rt.FindItem(f.ItemB);
        rt.CaptureNow();
        var bytes0 = SceneBytes(s);
        var idBr = br.HolderObject.GetEntityId(); var idB = b.HolderObject.GetEntityId(); var idRoot = br.Root.GetEntityId();
        var pBr = br.Holder.position; var pB = b.Holder.position; var qB = b.Holder.rotation;
        var sw = Stopwatch.StartNew();
        var d = rt.Ops.Delete(new[] { f.BrChan });
        double msDel = sw.Elapsed.TotalMilliseconds;
        Check("delete BR-Chan (with B): holders inactive under Graveyard, model 7 nodes",
              d.Ok && s.Model.Count == 7 && br.Holder.IsChildOf(rt.GraveyardRoot) && !br.HolderObject.activeInHierarchy && b.Holder.IsChildOf(br.Holder) && !b.HolderObject.activeInHierarchy,
              $"{d}; model {s.Model.Count}; graveyard entries {rt.Graveyard.EntryCount} ({msDel:F1} ms)");
        sw.Restart();
        var u = s.Commands.Undo();
        rt.Apply();
        double msUndo = sw.Elapsed.TotalMilliseconds;
        var br2 = rt.FindChara(f.BrChan); var b2 = rt.FindItem(f.ItemB);
        Check("undo: same GameObject instances, active again", u.Ok && br2 == br && br2.HolderObject.GetEntityId() == idBr && b2.HolderObject.GetEntityId() == idB && br2.Root.GetEntityId() == idRoot && br2.HolderObject.activeInHierarchy && rt.Graveyard.EntryCount == 0,
              $"{u} ({msUndo:F1} ms)");
        Check("world poses ±1e-4", VErr(br2.Holder.position, pBr) <= 1e-4f && VErr(b2.Holder.position, pB) <= 1e-4f && RotationCompare.Deg(b2.Holder.rotation, qB) <= 0.01f,
              $"BR {F(br2.Holder.position)} vs {F(pBr)}, B {F(b2.Holder.position)} vs {F(pB)}");
        var bytes1 = SceneBytes(s);
        Check("scene bytes equal pre-delete", Same(bytes0, bytes1), Diff(bytes0, bytes1));

        // graveyard limit 0: undo rebuilds
        var set = rt.Settings;
        int mc = set.GraveyardMaxCharas, mn = set.GraveyardMaxNodes;
        set.GraveyardMaxCharas = 0; set.GraveyardMaxNodes = 0;
        try
        {
            rt.Tick(0f);
            var hand0 = Bone(br2, "cf_J_Hand_R").position;
            int rcount0 = VisibilityApplier.OwnRenderers(br2.Holder).Count;
            rt.CaptureNow();
            var bytesA = SceneBytes(s);
            d = rt.Ops.Delete(new[] { f.BrChan });
            Check("delete with limit 0: graveyard emptied at once", d.Ok && rt.Graveyard.EntryCount == 0 && rt.Find(f.BrChan) == null && !br2.HasHolder, $"{d}; evicted {rt.LastEvicted}");
            sw.Restart();
            u = s.Commands.Undo();
            rt.Apply();
            double msRebuild = sw.Elapsed.TotalMilliseconds;
            var br3 = rt.FindChara(f.BrChan); var b3 = rt.FindItem(f.ItemB);
            Check("undo rebuilds: new instances, Built, B at N_Hand_R", u.Ok && br3 != null && br3 != br2 && br3.State == RealizationState.Built && br3.HolderObject.GetEntityId() != idBr
                  && b3 != null && b3.State == RealizationState.Built && b3.Holder.parent == br3.AttachPoints["N_Hand_R"], $"{u}; rebuild {msRebuild:F0} ms");
            Check("same renderer count", br3 != null && VisibilityApplier.OwnRenderers(br3.Holder).Count == rcount0, $"{rcount0} vs {(br3 != null ? VisibilityApplier.OwnRenderers(br3.Holder).Count : -1)}");
            rt.Tick(0f);
            var hand1 = Bone(br3, "cf_J_Hand_R")?.position ?? Vector3.positiveInfinity;
            Check("cf_J_Hand_R world position ±1e-3 after one Tick(0)", VErr(hand0, hand1) <= 1e-3f, $"{F(hand0)} vs {F(hand1)} (Δ {VErr(hand0, hand1):F5})");
            var bytesB = SceneBytes(s);
            Check("scene bytes after the rebuild equal those before the delete", Same(bytesA, bytesB), Diff(bytesA, bytesB));
        }
        finally { set.GraveyardMaxCharas = mc; set.GraveyardMaxNodes = mn; }
        R6Rebuilds(f);
    }

    // Rebuild protocol, duplicate through SceneOps, NodeForgotten.
    static void R6Rebuilds(Fixture f)
    {
        var rt = f.Runtime; var s = f.Session;
        rt.CaptureNow();
        var bytes0 = SceneBytes(s);
        var br = rt.FindChara(f.BrChan);
        var femaleStored = br.Stored;
        var male = f.Services.CardSources.FromFile(f.MaleCard);
        var r = s.SetAppearance(f.BrChan, new CharaAppearance { Card = male });
        rt.Apply();
        var b = rt.FindItem(f.ItemB);
        Check("appearance change to a card source (model API) rebuilds the character in place (male body, the card imported and built from documents), child B re-attached at the new N_Hand_R",
              r.Ok && br.State == RealizationState.Built && br.Sex == 0 && br.Card == male && br.Builder == CardCharaBuilder.Instance && b.Holder.parent == br.AttachPoints["N_Hand_R"]
              && VErr(b.Holder.position, br.AttachPoints["N_Hand_R"].position) <= 1e-4f,
              $"{r}; sex {br.Sex}; builder {br.Builder?.GetType().Name}; B under {b.Holder.parent?.name}");
        var u = s.Commands.Undo();
        rt.Apply();
        Check("undo: female body again (her stored documents), B at its N_Hand_R, scene bytes unchanged", u.Ok && br.Sex == 1 && br.Card == null && br.Stored == femaleStored && b.Holder.parent == br.AttachPoints["N_Hand_R"] && Same(bytes0, SceneBytes(s)),
              $"{u}; sex {br.Sex}; {Diff(bytes0, SceneBytes(s))}");

        var a = rt.FindItem(f.ItemA); var c = rt.FindItem(f.ItemC); var li = rt.Find(f.Light);
        var oldRef = ((ItemNodeState)s.Model.Find(f.ItemA).State).Ref;
        r = s.Set(f.ItemA, "item.ref", ContentId.Vanilla(399).ToString());
        rt.Apply();
        Check("item ref change rebuilds the instance; C and the light re-attached at the new attach transform (instance root of 399)",
              r.Ok && a.State == RealizationState.Built && a.Row?.Key == ContentId.Vanilla(399) && c.Holder.parent == a.Attach && li.Holder.parent == a.Attach && a.Attach == a.Instance.transform,
              $"{r}; attach {a.Attach?.name}");
        u = s.Commands.Undo();
        rt.Apply();
        Check("undo: original row and childRoot again", u.Ok && a.Row?.Key.ToString() == oldRef && c.Holder.parent == a.Attach && a.Attach != a.Instance.transform && Same(bytes0, SceneBytes(s)), u.ToString());

        int cards0 = f.Services.Cards.Count;
        var d = rt.Ops.Duplicate(new[] { f.Hero });
        var copy = d.Ok ? rt.FindChara(d.NewId) : null;
        var he = rt.FindChara(f.Hero);
        Check("duplicate HERO107 through SceneOps: a second Built character with a clone of the stored documents, same world pose, right after the original",
              copy != null && copy.State == RealizationState.Built && copy.Stored != null && he.Stored != null && !ReferenceEquals(copy.Stored, he.Stored)
              && VErr(copy.Holder.position, he.Holder.position) <= 1e-4f && s.Model.Find(d.NewId).Index == s.Model.Find(f.Hero).Index + 1,
              d.ToString());
        u = s.Commands.Undo();
        rt.Apply();
        Check("undo of the duplicate: the copy goes to the graveyard", u.Ok && copy != null && copy.InGraveyard && s.Model.Count == 9, u.ToString());
        s.Commands.ClearHistory();
        rt.Apply();
        Check("history cleared → NodeForgotten destroys the copy's graveyard objects; the card cache unchanged",
              copy != null && !copy.HasHolder && rt.Find(copy.NodeId) == null && rt.Graveyard.EntryCount == 0 && f.Services.Cards.Count == cards0,
              $"graveyard entries {rt.Graveyard.EntryCount}, cards {f.Services.Cards.Count} (before {cards0})");
    }

    // ================================================================ R7 save → load

    static void R7(Fixture f)
    {
        Section("R7", "save → load");
        var s = f.Session;
        int n0 = s.Findings.CountOf("chara.notImported");
        byte[] s1 = null;
        try { s1 = f.Runtime.Ops.ToBytes(FixedSave()); } catch (Exception e) { Check("save S1", false, e.Message); return; }
        int notImported = s.Findings.CountOf("chara.notImported") - n0;
        int subtrees = ContainerCharas(s1, out int blocks, out bool provenance);
        Check("S1: two character subtrees with provenance (imported at the add), no hs2next.cardref block, no chara.notImported",
              subtrees == 2 && blocks == 0 && provenance && notImported == 0, $"{s1.Length} bytes, subtrees {subtrees}, cardref blocks {blocks}, notImported {notImported}");

        using (var x = new LoadedScene(f.Services, s1))
        {
            Check("load S1 in a new session", x.Load.Ok, x.Load.ToString());
            if (!x.Load.Ok) return;
            var charas = x.Runtime.Charas().ToList();
            Check("both characters Built from their documents", charas.Count == 2 && charas.All(c => c.State == RealizationState.Built && c.Builder == SubtreeCharaBuilder.Instance && c.Node.Appearance.Stored != null),
                  string.Join("; ", charas.Select(c => c.ToString())));
            Check("all 9 nodes realized Built", x.Session.Model.PreOrder().All(n => x.Runtime.Find(n.Id)?.State == RealizationState.Built) && x.Session.Model.Count == 9);
            var again = x.Runtime.Ops.ToBytes(FixedSave());
            Check("ToBytes after load = S1", Same(s1, again), Diff(s1, again));
            for (int i = 0; i < 30; i++) x.Runtime.Tick(1f / 30f);
            var s2 = x.Runtime.Ops.ToBytes(FixedSave());
            Check("after 30 ticks the capture changed the scene (anime time / look)", !Same(s1, s2), $"captured {x.Runtime.Capture.TotalWrites} values");
            using (var y = new LoadedScene(f.Services, s2))
            {
                Check("reload S2", y.Load.Ok, y.Load.ToString());
                var s3 = y.Load.Ok ? y.Runtime.Ops.ToBytes(FixedSave()) : null;
                Check("S3 = S2", Same(s2, s3), Diff(s2, s3));
            }
        }
    }

    /// <summary>A new session + runtime that loaded a scene from bytes and applied it.</summary>
    sealed class LoadedScene : IDisposable
    {
        public readonly SceneSession Session;
        public readonly SceneRuntime Runtime;
        public readonly LoadResult<SceneContainer> Load;

        public LoadedScene(RuntimeServices services, byte[] bytes, ulong seed = 0x10adUL)
        {
            Session = NewSession(services, seed);
            Runtime = new SceneRuntime(Session, services, null, false).AddStandardParts();
            Load = Runtime.Ops.Load(bytes);
        }

        public void Dispose() => Runtime.Dispose();
    }

    // ================================================================ R8 timings

    static void R8(RuntimeServices services)
    {
        Section("R8", "timings (S10/200)");
        var t = TestTargets.Load();
        string female = TestTargets.Abs(t.visual.female);
        var s = NewSession(services, 0x8888UL);
        var rt = new SceneRuntime(s, services, null, false).AddStandardParts();
        try
        {
            var buildMs = new List<double>();
            var charas = new List<string>();
            var swAll = Stopwatch.StartNew();
            for (int i = 0; i < 10; i++)
            {
                var r = rt.Ops.AddCharaFromCard(female);
                if (!r.Ok) { Check("add character " + i, false, r.ToString()); return; }
                charas.Add(r.NewId);
                s.Set(r.NewId, "transform.pos", new Vec3(i * 8f, 0f, 0f));
                s.SetAnimation(r.NewId, Fixture.Idle);
                buildMs.Add(rt.FindChara(r.NewId).BuildMilliseconds);
            }
            rt.Apply();
            Info($"10 character builds: {string.Join(", ", buildMs.Select(m => m.ToString("F0")))} ms (median {Median(buildMs):F0} ms; report only, 1–3 s expected); total {swAll.ElapsedMilliseconds} ms");
            var folder = s.AddFolder().NewId;
            var itemMs = new List<double>();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 200; i++)
            {
                string parent = i < 100 ? null : charas[i % 10];
                string point = i < 100 ? null : (i % 2 == 0 ? "N_Hand_R" : "N_Hand_L");
                var r = rt.Ops.AddItem(ContentId.Vanilla(399), parent, point);
                if (!r.Ok) { Check("add item " + i, false, r.ToString()); return; }
                if (i < 100) s.Set(r.NewId, "transform.pos", new Vec3(i % 10, 0f, i / 10));
                itemMs.Add(rt.FindItem(r.NewId).BuildMilliseconds);
            }
            double items200 = sw.Elapsed.TotalMilliseconds;
            rt.Apply();
            Check($"item build, warm bundle: median ≤ 20 ms (2× = 40)", Median(itemMs) <= 40, $"median {Median(itemMs):F2} ms, max {itemMs.Max():F1} ms");
            Check($"200 items ≤ 4 s (2× = 8 s), incl. the add commands", items200 <= 8000, $"{items200:F0} ms");

            // Apply with nothing dirty
            for (int i = 0; i < 5; i++) rt.Tick(1f / 30f);
            var ap = new List<double>();
            for (int i = 0; i < 200; i++) { sw.Restart(); rt.Apply(); ap.Add(sw.Elapsed.TotalMilliseconds); }
            Check("Apply() with nothing dirty ≤ 0.2 ms (2× = 0.4)", Median(ap) <= 0.4, $"median {Median(ap) * 1000:F1} µs, max {ap.Max() * 1000:F1} µs, {rt.RealizationCount} realizations");
            // LateApply scale enforcement
            var la = new List<double>();
            for (int i = 0; i < 200; i++) { sw.Restart(); rt.Scale.Enforce(); la.Add(sw.Elapsed.TotalMilliseconds); }
            double per200 = Median(la) * 200.0 / Math.Max(1, rt.Scale.Count);
            Check("LateApply scale enforcement for 200 attached nodes ≤ 0.5 ms (2× = 1.0)", per200 <= 1.0, $"{rt.Scale.Count} attached: median {Median(la) * 1000:F1} µs → {per200 * 1000:F1} µs per 200");

            // delete + undo of a 200-item folder
            sw.Restart();
            for (int i = 0; i < 200; i++) s.AddItem(ItemDefaultsFor(services), folder);
            rt.Apply();
            Info($"folder with 200 items built in {sw.ElapsedMilliseconds} ms");
            sw.Restart();
            var d = rt.Ops.Delete(new[] { folder });
            double fd = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var u = s.Commands.Undo(); rt.Apply();
            double fu = sw.Elapsed.TotalMilliseconds;
            Check("delete + undo of a 200-item folder (graveyard) ≤ 50 ms each way (2× = 100)", d.Ok && u.Ok && fd <= 100 && fu <= 100 && rt.LastRevived >= 1 && rt.LastBuilt == 0,
                  $"delete {fd:F1} ms, undo {fu:F1} ms; revived {rt.LastRevived}, built {rt.LastBuilt}");
            // delete + undo of a character with its 20 attached items
            sw.Restart();
            d = rt.Ops.Delete(new[] { charas[3] });
            double cd = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            u = s.Commands.Undo(); rt.Apply();
            double cu = sw.Elapsed.TotalMilliseconds;
            Check("delete + undo of a character (graveyard) ≤ 20 ms each way (2× = 40)", d.Ok && u.Ok && cd <= 40 && cu <= 40 && rt.LastBuilt == 0,
                  $"delete {cd:F1} ms, undo {cu:F1} ms (with {s.Model.Find(charas[3]).Children.Count} attached items)");
            // save of the realized S10/200
            sw.Restart();
            var bytes = rt.Ops.ToBytes(FixedSave());
            Info($"ToBytes of the realized scene ({s.Model.Count} nodes): {sw.ElapsedMilliseconds} ms, {bytes.Length} bytes");
        }
        finally { rt.Dispose(); }
    }

    static ItemNodeState ItemDefaultsFor(RuntimeServices services)
    {
        services.Db.TryGetItem(ContentId.Vanilla(399), out var row);
        var prefab = row != null ? services.Catalog.LoadAsset<GameObject>(row) : null;
        return ItemDefaults.FromPrefab(prefab, row);
    }

    static double Median(List<double> l)
    {
        if (l.Count == 0) return double.NaN;
        var s = l.OrderBy(x => x).ToList();
        return s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
    }

    // ================================================================ standard parts and dispose

    static void Parts(RuntimeServices services)
    {
        Section("parts", "standard parts and dispose");
        var rootsBefore = SceneManager.GetActiveScene().GetRootGameObjects().ToList();
        var app = new GameObject("App Camera");
        var appCam = app.AddComponent<Camera>();
        var s = NewSession(services, 0x7777UL);
        var rt = new SceneRuntime(s, services, null, false).AddStandardParts();
        try
        {
            var parts = rt.Parts.Select(p => p.GetType().Name).ToList();
            // 2.2's three parts first, then the Phase 3.3 parts in their documented order (
            // packages B and S insert SceneEffects / SceneSky between StudioLights and SceneSound)
            var order33 = new[] { "StudioLights", "SceneEffects", "SceneSky", "SceneSound" };
            var got33 = parts.Skip(3).Where(n => order33.Contains(n)).ToList();
            Check("AddStandardParts: map, view camera, chara light, then the 3.3 parts (StudioLights … SceneSound) in that order; idempotent",
                  parts.Take(3).SequenceEqual(new[] { nameof(MapRealization), nameof(SceneViewCamera), nameof(SceneCharaLight) }) &&
                  got33.SequenceEqual(order33.Where(got33.Contains)) && got33.Contains(nameof(StudioLights)) && got33.Contains(nameof(SceneSound)) &&
                  rt.AddStandardParts().Parts.Count == parts.Count, string.Join(", ", parts));
            var view = rt.GetPart<SceneViewCamera>();
            var light = rt.GetPart<SceneCharaLight>();
            rt.Tick(0f);
            Check("view camera under View, chara light under it, LookTargets.DefaultCamera = view camera",
                  view.Camera && view.Camera.transform.IsChildOf(rt.ViewRoot) && light.Light && light.Light.transform.parent == view.Camera.transform && LookTargets.DefaultCamera == view.Camera.transform);
            view.UseCamera(appCam);
            rt.Tick(0f);
            Check("adopted app camera: the chara light moves under it", light.Light && light.Light.transform.parent == app.transform && LookTargets.DefaultCamera == app.transform);
        }
        finally { rt.Dispose(); }
        bool lightLeft = app.GetComponentsInChildren<Light>(true).Length > 0;
        var rootsAfter = SceneManager.GetActiveScene().GetRootGameObjects().ToList();
        var added = rootsAfter.Where(g => !rootsBefore.Contains(g)).ToList();
        Check("Dispose: the chara light under the adopted camera is destroyed, the app camera stays, DefaultCamera released, no runtime objects left",
              !lightLeft && appCam && LookTargets.DefaultCamera != app.transform && added.Count == 1 && added[0] == app,
              $"light left {lightLeft}; roots added: {string.Join(", ", added.Select(g => g.name))}");
        Object.DestroyImmediate(app);
    }

    // ================================================================ R9 findings

    const string GoldenChara = "6f1c0e4a9b2d4c3e8f7a6b5c4d3e2f10", GoldenCamera = "7a7a7a7a7a7a7a7a7a7a7a7a7a7a7a7a", GoldenItem = "0a1b2c3d4e5f60718293a4b5c6d7e8f9",
        GoldenOpaque = "8b8b8b8b8b8b8b8b8b8b8b8b8b8b8b8b", GoldenOpaqueChild = "9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c", GoldenLight = "1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b";

    static void R9(RuntimeServices services)
    {
        Section("R9", "findings (crafted document)");
        string dir = Path.Combine(ContentSettings.ProjectRoot, "Tests", "SaveFormat", "golden_src");
        var doc = DocIo.Scene(File.ReadAllBytes(Path.Combine(dir, "scene.json")));
        var cam = doc.Find(GoldenCamera);
        cam.Parent = new NodeParent(GoldenChara, "N_NotAPoint");
        var item = doc.Find(GoldenItem);
        ((ItemNodeState)item.State).Ref = "illusion.hs2#987654321";
        var chara = new CharaContainer();
        chara.Subtree.Chara = DocIo.Chara(File.ReadAllBytes(Path.Combine(dir, "chara.json")));
        chara.Subtree.Coord = DocIo.Coord(File.ReadAllBytes(Path.Combine(dir, "coord.json")));
        var c = new SceneContainer { Scene = doc, Title = "R9" };
        c.PlaceCharacter(GoldenChara, chara);
        // 2.3: the golden subtree is built from its documents; its missing mod parts and resources are reported.
        {
            var s0 = NewSession(services, 0x9998UL);
            var rt0 = new SceneRuntime(s0, services, null, false).AddStandardParts();
            try
            {
                s0.Bind(c);
                rt0.Apply();
                var ch0 = rt0.FindChara(GoldenChara);
                Check("golden subtree → Built by SubtreeCharaBuilder", ch0 != null && ch0.State == RealizationState.Built && ch0.Builder == SubtreeCharaBuilder.Instance, ch0?.ToString());
                var parts = s0.Findings.Where(x => x.NodeId == GoldenChara && (x.Code.StartsWith("part.", StringComparison.Ordinal) || x.Code.StartsWith("build.", StringComparison.Ordinal))).ToList();
                Check("missing parts reported as part.* (the golden's test.mod references), missing images as build.resMissing",
                      s0.Findings.Has("part.missingMod", GoldenChara) && s0.Findings.Has("build.resMissing", GoldenChara) && !s0.Findings.Has("chara.unbuildable", GoldenChara),
                      string.Join("; ", parts.Select(x => x.Code + " x" + x.Count)));
                var errs0 = s0.Findings.Where(x => x.Severity == FindingSeverity.Error).ToList();
                Check("no error findings with the golden subtree built", errs0.Count == 0, string.Join("; ", errs0));
            }
            finally { rt0.Dispose(); }
        }
        // The 2.2 expectations with the document builder unregistered.
        var s = NewSession(services, 0x9999UL);
        var rt = new SceneRuntime(s, services, null, false).AddStandardParts();
        bool removed = CharaBuilders.Unregister(SubtreeCharaBuilder.Instance);
        try
        {
            s.Bind(c);
            rt.Apply();
            var ch = rt.FindChara(GoldenChara);
            Check("subtree-only character with SubtreeCharaBuilder unregistered → Unbuildable + chara.unbuildable", removed && ch != null && ch.State == RealizationState.Unbuildable && ch.HasHolder && s.Findings.Has("chara.unbuildable", GoldenChara), ch?.ToString());
            var k = rt.Find(GoldenCamera);
            Check("unknown point name → node at the character holder + scene.unknownPoint", k != null && k.HasHolder && k.Holder.parent == ch?.Holder && s.Findings.Has("scene.unknownPoint", GoldenCamera), k?.ToString());
            var it = rt.FindItem(GoldenItem);
            Check("missing item row → fallback 399 + item.missing", it != null && it.State == RealizationState.Built && it.IsFallback && it.Row?.Key == ContentId.Vanilla(399) && s.Findings.Has("item.missing", GoldenItem)
                  && ((ItemNodeState)s.Model.Find(GoldenItem).State).Ref == "illusion.hs2#987654321", it?.ToString());
            Check("children of the unbuilt character at its holder (+ scene.attachPointUnavailable)", it != null && it.Holder.parent == ch?.Holder && s.Findings.Has("scene.attachPointUnavailable", GoldenItem));
            var op = rt.Find(GoldenOpaque); var opc = rt.Find(GoldenOpaqueChild);
            Check("unknown-kind subtree → not built + scene.unknownKindSubtree", op != null && op.State == RealizationState.NotBuilt && !op.HasHolder && opc != null && opc.State == RealizationState.NotBuilt && !opc.HasHolder
                  && s.Findings.Has("scene.unknownKindSubtree", GoldenOpaque) && s.Findings.CountOf("scene.unknownKindSubtree") == 1);
            var li = rt.Find(GoldenLight);
            Check("light under the fallback item builds at the item's attach transform", li != null && li.State == RealizationState.Built && li.Holder.parent == it?.Attach, li?.ToString());
            foreach (var x in s.Findings) Info("finding: " + x);
            var errs = s.Findings.Where(x => x.Severity == FindingSeverity.Error).ToList();
            Check("no error findings", errs.Count == 0, string.Join("; ", errs));
        }
        finally
        {
            rt.Dispose();
            if (removed) CharaBuilders.Register(SubtreeCharaBuilder.Instance, true);
        }
    }
}
