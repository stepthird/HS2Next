// Batch test of Studio character animation playback (Runtime/Motion/Animation).
// Run: -executeMethod AnimationTest.Run [-render] [-card <png>]
//   Rows: 19/0/0 Idle_00 (standing), 11/5/0 dance_00 (loop), 19/1/11 Chair_A (sitting, height blend),
//   15/31/1 attack_01 + rifle option item, 15/31/0 attack_00 (force-loop timing), mod 69/662/6625 Slow Walk
//   (GUID .0.k1t0k1tn.StudioAnims). One H row is played on the female target (numbers only; Editor/HAnimeTest.cs has the
//   H checks).
//   Checks: attach / stage order, controller data, worked examples A-C, normalized time set/get,
//   speed 0.5 / 1 / 2, pause + set time (deviation b), force loop at speed 0.7 in both follow-up modes,
//   option items (count, parent, scale, follow, visibility, release), option params / pattern, hand
//   patterns (mirror rule; female and the first male card), shape-bone guard (deviation a), root
//   motion in place, mouth untouched, interplay with look / correction / dynamic bones, scene round
//   trip, Stop, engine agreement (Playable vs Animator fallback), an H row played, zero allocation per step, timing.
//   -render: BR-Chan full body, standing / walking (mod) / sitting at t = 0 and 0.5 (+ the rifle row) to
//   Spike/out/motion/anim_<row>_<t>.png.
// Report: Spike/out/motion/anim_report.txt (PASS/FAIL); EditorApplication.Exit(1) on any FAIL.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HS2Next.Chara;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Data;
using HS2Next.Motion;
using HS2Next.Motion.Animation;
using HS2Next.Motion.Corrective;
using HS2Next.Motion.DynamicBones;
using HS2Next.Motion.Look;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class AnimationTest
{
    const float Dt = 1f / 60f;
    const float Tol = 1e-4f;
    const string ModGuid = ".0.k1t0k1tn.StudioAnims";
    static StringBuilder rep;
    static int passes, fails;
    static StudioDb db;

    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "motion");

    static string Arg(string name, string def)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
    }

    static void Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  " + detail : "")}");
        if (ok) passes++; else fails++;
    }
    static void Info(string s) => rep.AppendLine("   " + s);
    static void Section(string s) { rep.AppendLine(); rep.AppendLine("== " + s); }

    static void Guard(string what, Action a)
    {
        try { a(); }
        catch (Exception e) { Check(what + " (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
    }

    // ---------------------------------------------------------------- rows

    static StudioAnime Stand, Dance, Chair, Rifle, RifleLoop, Mod;

    static StudioAnime Row(int g, int c, int n, string guid = null)
    {
        var r = CharaAnimation.Resolve(db, guid ?? ContentIds.Vanilla, g, c, n);
        return r != null && !r.IsH ? r : null;
    }

    public static void Run()
    {
        rep = new StringBuilder(); passes = fails = 0;
        Directory.CreateDirectory(OutDir);
        bool render = TestArgs.Get().Contains("-render");
        var head = new StringBuilder();
        var gd = CardResolveTest.BuildDb(head);
        db = gd.LoadStudioLists();
        rep.AppendLine($"animation test (Unity {Application.unityVersion}, graphics {SystemInfo.graphicsDeviceType}, render {(render ? "on" : "off")}, engine default {AnimationSetup.Defaults.Engine})");
        rep.Append(head);
        Stand = Row(19, 0, 0); Dance = Row(11, 5, 0); Chair = Row(19, 1, 11); Rifle = Row(15, 31, 1); RifleLoop = Row(15, 31, 0); Mod = Row(69, 662, 6625, ModGuid);
        Section("rows");
        foreach (var (n, r) in new[] { ("stand", Stand), ("dance", Dance), ("chair", Chair), ("rifle", Rifle), ("rifle loop", RifleLoop), ("mod walk", Mod) })
            Info($"{n}: {(r != null ? r.ToString() + $" options {r.Options.Count}" : "MISSING")}");
        Check("all six test rows found, none H", Stand != null && Dance != null && Chair != null && Rifle != null && RifleLoop != null && Mod != null);
        Check("mod row comes from its GUID", Mod != null && string.Equals(Mod.Guid, ModGuid, StringComparison.OrdinalIgnoreCase), Mod?.Guid ?? "-");
        string female = Arg("-card", Path.Combine(CardResolveTest.GameRoot, @"UserData\chara\female\BR-Chan HS2 Rev2.png"));
        try
        {
            if (Stand != null) Guard("female", () => FemaleTests(gd, female));
            string maleDir = Path.Combine(CardResolveTest.GameRoot, @"UserData\chara\male");
            var male = Directory.Exists(maleDir)
                ? Directory.GetFiles(maleDir, "*.png", SearchOption.AllDirectories).OrderBy(f => f.Substring(maleDir.Length).Replace('\\', '/'), StringComparer.Ordinal).FirstOrDefault()
                : null;
            if (male != null && Stand != null) Guard("male", () => MaleTests(gd, male));
            else Check("male card present", false, maleDir);
            if (render && Stand != null) Guard("render", () => Render(gd, female));
        }
        finally { LookTargets.DefaultCamera = null; }
        rep.Insert(0, $"SUMMARY: {passes} PASS, {fails} FAIL\n");
        File.WriteAllText(Path.Combine(OutDir, "anim_report.txt"), rep.ToString());
        gd.UnloadAll();
        Debug.Log($"ANIMATION TEST DONE: {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }

    // ---------------------------------------------------------------- helpers

    sealed class Ch
    {
        public GameObject Root; public CharaContext Ctx; public CharaAnimation A; public CharaMotion M; public CharaAssembler Asm;
        public Transform B(string n) => Ctx.BodyBones.TryGetValue(n, out var t) ? t : null;
        public void Step(int n = 1, float dt = Dt) { for (int i = 0; i < n; i++) M.Step(dt); }
    }

    static Ch Build(ContentCatalog gd, string path)
    {
        var asm = new CharaAssembler(gd);
        var root = asm.BuildFromDocuments(path);
        var ctx = asm.Context;
        ctx.Motion.AutoStep = false;
        return new Ch { Root = root, Ctx = ctx, A = CharaAnimation.Of(root), M = ctx.Motion, Asm = asm };
    }

    static float QErr(Quaternion a, Quaternion b) =>
        Mathf.Min(Mathf.Max(Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y)), Mathf.Max(Mathf.Abs(a.z - b.z), Mathf.Abs(a.w - b.w))),
                  Mathf.Max(Mathf.Max(Mathf.Abs(a.x + b.x), Mathf.Abs(a.y + b.y)), Mathf.Max(Mathf.Abs(a.z + b.z), Mathf.Abs(a.w + b.w))));
    static float VErr(Vector3 a, Vector3 b) => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Max(Mathf.Abs(a.y - b.y), Mathf.Abs(a.z - b.z)));
    static string F(Quaternion q) => $"({q.x:F5}, {q.y:F5}, {q.z:F5}, {q.w:F5})";
    static string F(Vector3 v) => $"({v.x:F5}, {v.y:F5}, {v.z:F5})";

    static Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)> Snap(IEnumerable<Transform> ts) =>
        ts.Where(t => t).Distinct().ToDictionary(t => t, t => (t.localPosition, t.localRotation, t.localScale));

    static double MaxPoseDiff(Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)> a) => MaxPoseDiff(a, out _);
    static double MaxPoseDiff(Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)> a, out string worst)
    {
        double m = 0; worst = "-";
        foreach (var kv in a)
        {
            double d = Math.Max(RotationCompare.Deg(kv.Key.localRotation, kv.Value.r), VErr(kv.Key.localPosition, kv.Value.p));
            if (d > m) { m = d; worst = kv.Key.name; }
        }
        return m;
    }

    // Bones written by stage A / hands only: the body skeleton without the head-bone subtree and without the chains
    // that the bust / dynamic-bone stages simulate (their state depends on the physics history, not on the motion).
    static IEnumerable<Transform> AnimBones(Ch c)
    {
        var sim = new HashSet<Transform>();
        var dyn = c.M.Get<DynamicBoneStage>();
        if (dyn != null) foreach (var ch in dyn.Chains) for (int i = 0; i < ch.ParticleCount; i++) { var t = ch.ParticleTransform(i); if (t) sim.Add(t); }
        return Skeleton(c).Where(t => !sim.Contains(t) && !t.name.StartsWith("cf_J_Mune") && !t.name.StartsWith("cf_J_Siri") && !t.name.StartsWith("cf_J_sk_") && !t.name.StartsWith("cf_J_Legsk"));
    }

    static IEnumerable<Transform> Skeleton(Ch c) => c.Ctx.BodyBones.Values.Where(t => t && !t.IsChildOf(c.Ctx.HeadBone.transform));

    // ================================================================ female

    static void FemaleTests(ContentCatalog gd, string path)
    {
        Section("female card: " + Path.GetFileNameWithoutExtension(path));
        if (!File.Exists(path)) { Check("card exists", false, path); return; }
        var c = Build(gd, path);
        try
        {
            foreach (var l in c.Asm.Log.Where(l => l.StartsWith("anim:") || l.StartsWith("motion:"))) Info(l);
            Guard("attach", () => Attach(c));
            Guard("controller data", () => ControllerData(c));
            Guard("example A", () => ExampleA(c));
            Guard("example B", () => ExampleB(c));
            Guard("example C + option items", () => ExampleC(c));
            Guard("time set/get", () => TimeSetGet(c));
            Guard("speed", () => SpeedTests(c));
            Guard("pause", () => PauseTests(c));
            Guard("force loop", () => ForceLoopTests(c));
            Guard("params", () => ParamTests(c));
            Guard("hands", () => HandTests(c, "female"));
            Guard("shape bones", () => ShapeBoneTests(c));
            Guard("root motion", () => RootMotionTests(c));
            Guard("mouth", () => MouthTests(c));
            Guard("interplay", () => Interplay(c));
            Guard("scene", () => SceneRoundTrip(c));
            Guard("engines", () => EngineAgreement(c));
            Guard("H row", () => HRowPlays(c));
            Guard("alloc", () => AllocAndTiming(c));
            Guard("stop", () => StopTests(c));
        }
        finally { Object.DestroyImmediate(c.Root); }
    }

    static void Attach(Ch c)
    {
        Section("attach");
        var a = c.A;
        Check("CharaAnimation on the root, stages registered", a != null && a.Stage != null && a.Hands != null && c.M.Stages.Contains(a.Stage) && c.M.Stages.Contains(a.Hands),
              "order " + string.Join(" < ", c.M.Stages.Select(s => s.Name + ":" + (int)s.Phase)));
        Check("stage A first, hands at Shape", c.M.Stages[0] == a.Stage && a.Hands.Phase == MotionPhase.Shape && c.M.Stages[1] == a.Hands);
        Check("nothing loaded at attach: Animator disabled, no controller", !a.IsLoaded && a.Animator && !a.Animator.enabled && a.Animator.runtimeAnimatorController == null);
        Check("hand tables: 21 patterns, 15 + 15 finger bones bound", a.Hands.PatternCount == 21 && a.Hands.BoundCount(0) == 15 && a.Hands.BoundCount(1) == 15,
              $"patterns {a.Hands.PatternCount}, L {a.Hands.BoundCount(0)}, R {a.Hands.BoundCount(1)}; L bones {string.Join(",", a.Hands.Bones(0).Select(t => t ? t.name.Replace("cf_J_Hand_", "") : "null"))}");
        Check("shape-bone guard bound (shape_body_map targets)", a.ShapeBoneCount >= 80, $"{a.ShapeBoneCount} bones");
        Info($"body height {a.BodyHeight:F4}, bust {a.BodyBust:F4}; default speed {a.Speed}, pattern {a.Pattern}, loop {a.ForceLoop}, option visible {a.OptionVisible}");
        Check("defaults: speed 1, pattern 0, loop off, options visible", a.Speed == 1f && a.Pattern == 0f && !a.ForceLoop && a.OptionVisible);
    }

    static void ControllerData(Ch c)
    {
        Section("controller data");
        var a = c.A;
        var sw = Stopwatch.StartNew();
        bool ok = a.Load(Stand);
        double ms = sw.Elapsed.TotalMilliseconds;
        var d = a.Driver;
        var st = a.StateInfo;
        Info($"Load(19/0/0) {ms:F1} ms (first, bundle 34 cold); engine {a.Options.Engine}; controller {d?.Controller.name}, clips {d?.Controller.animationClips.Length}");
        Check("19/0/0 loads f_studio_34 with 1 layer, parameter height (float 0)", ok && d != null && d.Controller.name == "f_studio_34" && d.LayerCount == 1 &&
              d.Parameters.Length == 1 && d.Parameters[0].name == "height" && d.Parameters[0].type == AnimatorControllerParameterType.Float && d.Parameters[0].defaultFloat == 0f,
              $"layers {d?.LayerCount}, params {string.Join(",", d?.Parameters.Select(p => p.name + ":" + p.type + "=" + p.defaultFloat) ?? new string[0])}");
        Check("Idle_00: loop, state speed 0.5, length 2.6667 (1.3333 / 0.5)", st.loop && Mathf.Abs(st.speed - 0.5f) < 1e-6f && Mathf.Abs(st.length - 2.6667f) < 1e-3f && st.shortNameHash == Animator.StringToHash("Idle_00"),
              $"loop {st.loop}, speed {st.speed}, length {st.length:F4}");
        Check("Animator: enabled (playable output), no own controller, AlwaysAnimate, root motion applied as the game's (kept in place by HS2Next)",
              a.Animator.enabled == (a.Options.Engine == AnimationEngine.Playable) && (a.Options.Engine != AnimationEngine.Playable || a.Animator.runtimeAnimatorController == null) &&
              a.Animator.cullingMode == AnimatorCullingMode.AlwaysAnimate && a.Animator.applyRootMotion);
        Check("height parameter = body height", a.HeightParameter == "height" && Mathf.Abs(d.GetFloat(Animator.StringToHash("height")) - a.BodyHeight) < 1e-6f, $"{a.HeightParameter} {d.GetFloat(Animator.StringToHash("height")):F4}");
        sw.Restart(); a.Load(Stand); double again = sw.Elapsed.TotalMilliseconds;
        Check("reloading the same controller keeps the binding (no new driver)", a.Driver == d, $"{again:F2} ms");
    }

    // Example A: 19/0/0 Idle_00.
    static readonly (string bone, bool pos, Vector4 t0, Vector4 t05)[] ExA =
    {
        ("cf_J_ArmUp00_L", false, new Vector4(0.36787f, 0.57102f, 0.42623f, 0.59744f), new Vector4(0.40005f, 0.56880f, 0.41084f, 0.58960f)),
        ("cf_J_Spine01", false, new Vector4(-0.00394f, 0.04746f, -0.00828f, 0.99883f), new Vector4(0.00975f, 0.04734f, -0.00893f, 0.99879f)),
        ("cf_J_Hips", false, new Vector4(-0.01909f, -0.05101f, 0.02047f, 0.99831f), new Vector4(-0.02581f, -0.05114f, 0.02013f, 0.99815f)),
        ("cf_J_Hips", true, new Vector4(0.10179f, 11.24667f, 0.27903f, 0), new Vector4(0.10179f, 11.24667f, 0.33377f, 0)),
    };

    static float ExampleErr(Ch c, (string bone, bool pos, Vector4 t0, Vector4 t05) e, bool half, out string got)
    {
        var t = c.B(e.bone); var x = half ? e.t05 : e.t0;
        if (e.pos) { got = F(t.localPosition); return VErr(t.localPosition, new Vector3(x.x, x.y, x.z)); }
        got = F(t.localRotation); return QErr(t.localRotation, new Quaternion(x.x, x.y, x.z, x.w));
    }

    static void ExampleA(Ch c)
    {
        Section("worked example A (19/0/0 Idle_00)");
        var a = c.A;
        a.Speed = 1f;
        foreach (bool half in new[] { false, true })
        {
            a.Load(Stand, half ? 0.5f : 0f);
            float worst = 0; var parts = new List<string>();
            foreach (var e in ExA) { float err = ExampleErr(c, e, half, out var got); worst = Mathf.Max(worst, err); parts.Add($"{e.bone}{(e.pos ? ".pos" : "")} {got} err {err:F6}"); }
            Check($"example A at t {(half ? 0.5 : 0)}: right after Load(t) (re-sampled), tolerance {Tol}", worst <= Tol, string.Join("; ", parts));
        }
        a.Load(Stand, 0f);
        c.Step(80);
        float nt = a.NormalizedTime; float w = 0; var p2 = new List<string>();
        foreach (var e in ExA) { float err = ExampleErr(c, e, true, out var got); w = Mathf.Max(w, err); p2.Add($"{e.bone}{(e.pos ? ".pos" : "")} err {err:F6}"); }
        Check("example A: 80 steps of 1/60 s at speed 1 reach normalized 0.5 (rate 0.375/s)", Mathf.Abs(nt - 0.5f) < 1e-4f, $"normalized {nt:F6}; pose vs t 0.5 table: {string.Join(", ", p2)}");
        a.Load(Stand, 0f);
        c.Step(600);
        Check("10 s at speed 1: raw normalized 3.75 (not wrapped)", Mathf.Abs(a.NormalizedTime - 3.75f) < 1e-3f, $"{a.NormalizedTime:F5}");
    }

    static void ExampleB(Ch c)
    {
        Section("worked example B (19/1/11 Chair_A, height 0.5)");
        var a = c.A;
        float h0 = a.BodyHeight;
        a.BodyHeight = 0.5f;
        try
        {
            a.Load(Chair, 0.5f);
            var leg = c.B("cf_J_LegUp00_L"); var hips = c.B("cf_J_Hips");
            float e1 = QErr(leg.localRotation, new Quaternion(-0.59504f, 0.10054f, -0.00903f, 0.79734f));
            float e2 = QErr(hips.localRotation, new Quaternion(-0.07230f, 0f, 0f, 0.99738f));
            float e3 = VErr(hips.localPosition, new Vector3(0f, 6.88897f, 0.46077f));
            Check($"example B at t 0.5 (M_Chair_A), tolerance {Tol}", Mathf.Max(e1, Mathf.Max(e2, e3)) <= Tol,
                  $"LegUp00_L {F(leg.localRotation)} err {e1:F6}; Hips {F(hips.localRotation)} err {e2:F6}; Hips.pos {F(hips.localPosition)} err {e3:F6}; state {a.StateInfo.shortNameHash == Animator.StringToHash("Chair_A")}, loop {a.StateInfo.loop}");
        }
        finally { a.BodyHeight = h0; }
        a.Load(Chair, 0.5f);
        Info($"BR-Chan height {h0:F3}: LegUp00_L {F(c.B("cf_J_LegUp00_L").localRotation)}, Hips.pos {F(c.B("cf_J_Hips").localPosition)}");
    }

    static void ExampleC(Ch c)
    {
        Section("worked example C and option items, 15/31/1 attack_01 + rifle");
        var a = c.A;
        a.ForceLoop = false;
        a.Load(Rifle, 0.39f);
        var arm = c.B("cf_J_ArmUp00_R"); var hand = c.B("cf_J_Hand_R");
        var exp = new[] { (0.39f, new Quaternion(-0.35786f, -0.59733f, -0.29160f, 0.65582f), new Quaternion(-0.17729f, 0.30542f, -0.00620f, 0.93555f)),
                          (0.48f, new Quaternion(-0.25593f, -0.60336f, -0.35085f, 0.66885f), new Quaternion(-0.29619f, 0.33312f, 0.00972f, 0.89510f)) };
        foreach (var (t, qa, qh) in exp)
        {
            a.NormalizedTime = t;
            float e1 = QErr(arm.localRotation, qa), e2 = QErr(hand.localRotation, qh);
            Check($"example C at {t}: ArmUp00_R and Hand_R, tolerance {Tol}", Mathf.Max(e1, e2) <= Tol, $"arm {F(arm.localRotation)} err {e1:F6}; hand {F(hand.localRotation)} err {e2:F6}");
        }
        a.NormalizedTime = 1f; c.A.Speed = 0f; c.Step(1);
        var end = Snap(new[] { arm, hand });
        a.NormalizedTime = 1.7f;
        double d17 = MaxPoseDiff(end);
        Check("no force loop: normalized 1.7 shows the last frame and reads 1.7", d17 < 1e-3f && Mathf.Abs(a.NormalizedTime - 1.7f) < 1e-5f, $"pose diff {d17:F5}, reads {a.NormalizedTime:F4}");
        a.Speed = 1f;

        // option items
        a.Load(Rifle, 0.39f);
        var items = a.Items;
        var it = items.Count > 0 ? items.Items[0] : null;
        Info("items: " + string.Join("; ", items.Items.Select(x => x.ToString())) + (items.Log.Count > 0 ? " | log: " + string.Join(" / ", items.Log) : ""));
        Check("one option item: rifle prefab under k_f_handR_00, no Animator, not synced", items.Count == 1 && it.Root && it.Root.name.StartsWith("p_ai_hi_assult00_stu") &&
              it.Root.transform.parent && it.Root.transform.parent.name == "k_f_handR_00" && !it.Animator && !it.Synced,
              it != null ? $"{it.Root?.name} parent {it.Root?.transform.parent?.name}" : "none");
        if (it == null) return;
        Check("counterScale false: moved object keeps the prefab root scale; local zero / identity", it.Root.transform.localScale == it.RootScale && it.Root.transform.localPosition == Vector3.zero && RotationCompare.Deg(it.Root.transform.localRotation, Quaternion.identity) == 0,
              $"scale {F(it.Root.transform.localScale)} prefab {F(it.RootScale)}");
        Check("renderers collected (enabled in the prefab) and visible", it.Renderers.Count > 0 && it.Renderers.All(r => r.enabled), $"{it.Renderers.Count} renderers");
        var kHand = c.B("k_f_handR_00");
        float follow = 0; var p0 = it.Root.transform.position;
        for (int i = 0; i < 60; i++) { c.Step(); follow = Mathf.Max(follow, (it.Root.transform.position - kHand.position).magnitude); }
        Check("item follows k_f_handR_00 while the clip plays (60 steps)", follow < 1e-4f && (it.Root.transform.position - p0).magnitude > 0.01f, $"max offset {follow:E2}, moved {(it.Root.transform.position - p0).magnitude:F3}");
        a.OptionVisible = false;
        bool hidden = it.Renderers.All(r => !r.enabled);
        c.Step(30);
        bool stillFollows = (it.Root.transform.position - kHand.position).magnitude < 1e-4f;
        a.OptionVisible = true;
        Check("visibility toggle hides / shows the renderers; hidden item keeps following", hidden && stillFollows && it.Renderers.All(r => r.enabled));
        a.CharacterVisible = false; bool hid2 = it.Renderers.All(r => !r.enabled); a.CharacterVisible = true;
        Check("items hidden while the character is hidden", hid2);
        var root = it.Root;
        a.Load(Stand);
        Check("release on change: the next load destroys the item", items.Count == 0 && !root, $"items {items.Count}, object alive {(bool)root}");
        a.Load(Rifle); var r1 = items.Items[0].Root; a.Load(Rifle);
        Check("reloading the same row re-creates the item", items.Count == 1 && !r1 && items.Items[0].Root);
        a.Load(Stand);
    }

    static void TimeSetGet(Ch c)
    {
        Section("normalized time set / get (stand, dance, chair, mod walk)");
        var a = c.A; a.Speed = 1f;
        var bones = new[] { "cf_J_ArmUp00_L", "cf_J_Spine01", "cf_J_Head", "cf_J_LegUp00_R" }.Select(c.B).ToArray();
        foreach (var (name, row) in new[] { ("stand", Stand), ("dance", Dance), ("chair", Chair), ("mod walk", Mod) })
        {
            if (!a.Load(row)) { Check($"{name}: load", false, string.Join(" / ", a.Log.Skip(Math.Max(0, a.Log.Count - 2)))); continue; }
            var poses = new List<Quaternion[]>(); var reads = new List<float>();
            foreach (float t in new[] { 0f, 0.25f, 0.5f, 0.75f })
            {
                a.NormalizedTime = t;
                reads.Add(a.NormalizedTime);
                poses.Add(bones.Select(b => b.localRotation).ToArray());
            }
            double minDiff = double.MaxValue;
            for (int i = 0; i < 4; i++) for (int j = i + 1; j < 4; j++)
                minDiff = Math.Min(minDiff, Enumerable.Range(0, bones.Length).Max(k => RotationCompare.Deg(poses[i][k], poses[j][k])));
            bool readOk = reads.Zip(new[] { 0f, 0.25f, 0.5f, 0.75f }, (r, t) => Mathf.Abs(r - t) < 1e-5f).All(x => x);
            Check($"{name}: get returns the set time; poses at 0 / 0.25 / 0.5 / 0.75 all differ", readOk && minDiff > 0.05f,
                  $"reads [{string.Join(", ", reads.Select(r => r.ToString("F4")))}], smallest pairwise max bone difference {minDiff:F2} deg, state length {a.StateInfo.length:F4}, loop {a.StateInfo.loop}");
        }
        a.Load(Stand);
    }

    static int FramesTo(Ch c, float target, int max = 2000)
    {
        for (int f = 1; f <= max; f++) { c.Step(); if (c.A.NormalizedTime >= target - 1e-5f) return f; }
        return -1;
    }

    static void SpeedTests(Ch c)
    {
        Section("speed: frames from 0 to normalized 0.5 on Idle_00 (rate 0.375/s x speed)");
        var a = c.A;
        var res = new List<string>(); bool ok = true;
        foreach (var (s, expect) in new[] { (0.5f, 160), (1f, 80), (2f, 40) })
        {
            a.Speed = s; a.Load(Stand, 0f);
            int n = FramesTo(c, 0.5f);
            res.Add($"speed {s}: {n} frames (expected {expect})");
            ok &= Math.Abs(n - expect) <= 1;
        }
        Check("speed 0.5 / 1 / 2", ok, string.Join("; ", res));
        a.Speed = 2f; a.Load(Dance);
        Check("speed survives a motion change (LoadAnime never resets it)", a.Speed == 2f);
        a.Speed = -1f;
        Check("negative speed clamps to 0", a.Speed == 0f);
        a.Speed = 1f;
        c.M.TimeScale = 0.5f; a.Load(Stand, 0f);
        int nm = FramesTo(c, 0.5f);
        c.M.TimeScale = 1f;
        Check("CharaMotion.TimeScale also scales the animation (frame.Dt)", Math.Abs(nm - 160) <= 1, $"{nm} frames");
    }

    static void PauseTests(Ch c)
    {
        Section("pause and set time (deviation b)");
        var a = c.A; a.Speed = 1f;
        var arm = c.B("cf_J_ArmUp00_L");
        a.Load(Stand, 0.25f); var q25 = arm.localRotation;
        a.Load(Stand, 0.6f);
        a.Speed = 0f;
        var q60 = arm.localRotation; float t60 = a.NormalizedTime;
        c.Step(30);
        Check("speed 0: time and pose hold over 30 steps", Mathf.Abs(a.NormalizedTime - t60) < 1e-6f && RotationCompare.Deg(arm.localRotation, q60) < 1e-4f, $"t {a.NormalizedTime:F5}");
        arm.localRotation = Quaternion.identity; c.Step(1);
        Check("speed 0: the clip pose is still written every step", RotationCompare.Deg(arm.localRotation, q60) < 1e-3f);
        a.NormalizedTime = 0.25f;
        Check("speed 0 + set time: pose changes at once and the time reads back", RotationCompare.Deg(arm.localRotation, q25) < 1e-3f && Mathf.Abs(a.NormalizedTime - 0.25f) < 1e-6f,
              $"diff to the t 0.25 pose {Quaternion.Angle(arm.localRotation, q25):F4} deg, reads {a.NormalizedTime:F4}");
        a.Options.ResampleOnSetTime = false;
        a.NormalizedTime = 0.6f;
        bool notYet = RotationCompare.Deg(arm.localRotation, q25) < 1e-3f;
        c.Step(1);
        bool after = RotationCompare.Deg(arm.localRotation, q60) < 1e-3f;
        a.Options.ResampleOnSetTime = true;
        Check("ResampleOnSetTime off: the pose changes at the next step only", notYet && after);
        c.M.Paused = true; a.Speed = 1f; float tp = a.NormalizedTime; c.Step(20); c.M.Paused = false;
        Check("CharaMotion.Paused freezes the animation", Mathf.Abs(a.NormalizedTime - tp) < 1e-6f);
        a.Restart();
        Check("Restart: time 0 at once", Mathf.Abs(a.NormalizedTime) < 1e-6f, $"{a.NormalizedTime:F5}");
    }

    static void ForceLoopTests(Ch c)
    {
        Section("force loop: 15/31/0 attack_00, L 4.3333 s, speed 0.7, 60 fps");
        var a = c.A;
        foreach (bool delay in new[] { false, true })
        {
            a.Options.GameFrameDelay = delay;
            a.Speed = 0.7f; a.ForceLoop = true;
            a.Load(RifleLoop, 0f);
            var ts = new List<float> { a.NormalizedTime };
            for (int f = 1; f <= 760; f++) { c.Step(); ts.Add(a.NormalizedTime); }
            var drops = Enumerable.Range(1, ts.Count - 1).Where(f => ts[f] < ts[f - 1] - 0.5f).ToList();
            int restart = drops.Count > 0 ? drops[0] : -1, second = drops.Count > 1 ? drops[1] : -1;
            float atRestart = restart > 0 ? ts[restart] : 0, before = restart > 0 ? ts[restart - 1] : 0, next = restart > 0 && restart + 1 < ts.Count ? ts[restart + 1] : 0;
            if (!delay)
            {
                // Default: the frame that crosses 1 (372) restarts in the same step and shows time 0.
                Check("default (same step): restart in step 372, that step shows time 0, step 373 shows dt x speed / L",
                      restart == 372 && Mathf.Abs(atRestart) < 1e-6f && Mathf.Abs(next - 0.0026923f) < 2e-5f && second - restart == 372,
                      $"restart step {restart}, time there {atRestart:F6} (previous {before:F6}), next {next:F6}, cycle {second - restart} steps");
            }
            else
            {
                // Game parity: step 372 shows the last pose at 1.00154, the restart is consumed in step 373.
                Check("GameFrameDelay: step 372 reads 1.00154, the restart shows in step 373; cycle 372 if the consuming step advances, else 373",
                      restart == 373 && Mathf.Abs(before - 1.00154f) < 5e-5f && second - restart == (atRestart > 1e-6f ? 372 : 373),
                      $"first drop seen in step {restart}, time there {atRestart:F6} (engine: {(atRestart > 1e-6f ? "advances in the consuming step" : "no advance in the consuming step")}), previous {before:F6}, cycle {second - restart} steps");
                Info($"answered for this engine: the step that consumes Play(state, 0, 0) reports {atRestart:F7} (dt x 0.7 / L = 0.0026923)");
            }
        }
        a.Options.GameFrameDelay = false;
        a.ForceLoop = false; a.Speed = 1f;
        a.Load(RifleLoop, 0.98f);
        c.Step(120);
        Check("without force loop the non-looping state keeps growing past 1", a.NormalizedTime > 1.3f, $"{a.NormalizedTime:F4}");
        a.ForceLoop = true; a.Load(Stand, 0.98f); c.Step(30);
        Check("looping states are never restarted by force loop", a.NormalizedTime > 1f, $"{a.NormalizedTime:F4}");
        a.ForceLoop = false;
    }

    static void ParamTests(Ch c)
    {
        Section("option parameters and pattern");
        var a = c.A;
        a.Load(Chair, 0f);
        int hId = Animator.StringToHash("height");
        a.OptionParam1 = 0.9f; a.OptionParam2 = 0.1f;
        Check("plain row: option params are stored but do not touch the Animator (height stays the body height)",
              a.OptionParam1 == 0.9f && a.OptionParam2 == 0.1f && Mathf.Abs(a.Driver.GetFloat(hId) - a.BodyHeight) < 1e-6f && a.BreastParameter == "", $"height {a.Driver.GetFloat(hId):F4}");
        a.Pattern = 1.7f;
        Check("pattern clamps to [0, 1] and is stored; plain body has no `motion`", a.Pattern == 1f && !a.Driver.HasParameter("motion"));
        a.Pattern = 0f; a.OptionParam1 = 0f; a.OptionParam2 = 0f;
        // deviation (c): card change re-applies height
        a.Driver.SetFloat(hId, 0.123f);
        a.OnCardChanged();
        Check("OnCardChanged re-applies `height` (deviation c)", Mathf.Abs(a.Driver.GetFloat(hId) - a.BodyHeight) < 1e-6f);
        Check("PV flags 0-3 on for a plain row", a.PvFlags.Take(4).All(x => x));
    }

    static void HandTests(Ch c, string sex)
    {
        Section($"hand patterns, {sex}");
        var a = c.A; var h = a.Hands;
        if (h == null) { Check($"{sex}: hand stage", false); return; }
        a.Speed = 0f; a.Load(Stand, 0.3f); c.Step(1);
        var clipL = h.Bones(0).Select(t => t.localRotation).ToArray();
        var clipR = h.Bones(1).Select(t => t.localRotation).ToArray();
        float worst = 0, mirror = 0;
        for (int p = 1; p <= h.PatternCount; p++)
        {
            h[0] = p; h[1] = p; c.Step(1);
            for (int i = 0; i < 15; i++)
            {
                worst = Mathf.Max(worst, QErr(h.Bones(0)[i].localRotation, h.PatternRotation(0, i, p)));
                worst = Mathf.Max(worst, QErr(h.Bones(1)[i].localRotation, h.PatternRotation(1, i, p)));
                var eL = h.Data.Euler[0][i][p - 1]; var eR = h.Data.Euler[1][i][p - 1];
                mirror = Mathf.Max(mirror, QErr(Quaternion.Euler(eR), Quaternion.Euler(eL.x, -eL.y, -eL.z)));
            }
        }
        Check($"{sex}: patterns 1-21 write the table rotations on all 30 finger bones", worst < 1e-6f, $"max err {worst:E2}");
        Check($"{sex}: right hand = mirror (x, -y, -z) of the left", mirror < 2e-5f, $"max quaternion err {mirror:E2}");
        var ex = new (string bone, int hand, int p, Quaternion q)[]
        {
            ("cf_J_Hand_Index01_L", 0, 1, new Quaternion(-0.02555f, -0.00713f, 0.53961f, 0.84150f)),
            ("cf_J_Hand_Index01_R", 1, 1, new Quaternion(-0.02555f, 0.00713f, -0.53961f, 0.84150f)),
            ("cf_J_Hand_Thumb03_L", 0, 1, new Quaternion(0f, -0.40042f, 0f, 0.91633f)),
            ("cf_J_Hand_Index02_L", 0, 4, new Quaternion(0f, 0f, 0.58701f, 0.80958f)),
        };
        float ew = 0; var parts = new List<string>();
        foreach (var e in ex)
        {
            h[e.hand] = e.p; c.Step(1);
            var t = c.B(e.bone); float err = QErr(t.localRotation, e.q); ew = Mathf.Max(ew, err);
            parts.Add($"{e.bone} p{e.p} {F(t.localRotation)} err {err:F6}");
        }
        Check($"{sex}: examples, tolerance {Tol}", ew <= Tol, string.Join("; ", parts));
        // table spot check: pattern 1, left I1 z = 65, L2 z = 106, T1 x = 18.9
        h[0] = 1; c.Step(1);
        float z(string n) => Mathf.DeltaAngle(0, c.B(n).localEulerAngles.z);
        Check($"{sex}: table pattern 1 left: I1 z 65, L2 z 106, M3 z 101 (deg, rounded)", Mathf.Abs(z("cf_J_Hand_Index01_L") - 65) < 0.6f && Mathf.Abs(z("cf_J_Hand_Little02_L") - 106) < 0.6f && Mathf.Abs(z("cf_J_Hand_Middle03_L") - 101) < 0.6f,
              $"{z("cf_J_Hand_Index01_L"):F2}, {z("cf_J_Hand_Little02_L"):F2}, {z("cf_J_Hand_Middle03_L"):F2}");
        h[0] = 0; h[1] = 0; c.Step(1);
        double back = 0;
        for (int i = 0; i < 15; i++) { back = Math.Max(back, RotationCompare.Deg(h.Bones(0)[i].localRotation, clipL[i])); back = Math.Max(back, RotationCompare.Deg(h.Bones(1)[i].localRotation, clipR[i])); }
        Check($"{sex}: pattern 0: next step the fingers show the clip again", back < 1e-3f, $"max {back:F5} deg");
        h[0] = 5; h[0] = 99;
        Check($"{sex}: out-of-range pattern ignored", h[0] == 5);
        h[0] = 0;
        a.Speed = 1f;
    }

    static void ShapeBoneTests(Ch c)
    {
        Section("shape bones (deviation a)");
        var a = c.A;
        var guarded = c.Ctx.BodyBones.Values.Where(t => t && (t.name.EndsWith("_s") || t.name.Contains("_s_"))).ToList();
        var shapeSnap = Snap(guarded);
        var emotion = new[] { (19, 2, 30), (19, 2, 0), (19, 2, 10), (19, 2, 20) }.Select(k => Row(k.Item1, k.Item2, k.Item3)).Where(r => r != null).ToList();
        var rows = new[] { Stand, Dance, Chair, Rifle, Mod }.Concat(emotion).ToList();
        foreach (bool guard in new[] { false, true })
        {
            a.Options.RestoreShapeBones = guard;
            var lines = new List<string>(); int worstCount = 0;
            foreach (var r in rows)
            {
                a.Load(r, 0f); c.Step(30);
                var moved = shapeSnap.Where(kv => VErr(kv.Key.localScale, kv.Value.s) > 1e-6f || VErr(kv.Key.localPosition, kv.Value.p) > 1e-6f || RotationCompare.Deg(kv.Key.localRotation, kv.Value.r) > 1e-3f).Select(kv => kv.Key.name).ToList();
                worstCount = Math.Max(worstCount, moved.Count);
                if (moved.Count > 0) lines.Add($"{r.Group}/{r.Category}/{r.No} {r.Clip}: {moved.Count} [{string.Join(",", moved.Take(8))}]");
                foreach (var kv in shapeSnap) { kv.Key.localPosition = kv.Value.p; kv.Key.localRotation = kv.Value.r; kv.Key.localScale = kv.Value.s; }
            }
            if (!guard)
            {
                Info($"guard OFF, *_s bones changed after 30 steps: {(lines.Count == 0 ? "none" : string.Join("; ", lines))}");
                // Which *_s bones are in the bound set at all: perturb them, evaluate with dt 0, see which get rewritten.
                var bound = new List<string>();
                foreach (var r in rows)
                {
                    a.Load(r, 0.5f);
                    var off = Quaternion.Euler(7f, 11f, 13f);
                    foreach (var kv in shapeSnap) { kv.Key.localScale = kv.Value.s * 1.37f; kv.Key.localPosition = kv.Value.p + new Vector3(0.37f, 0.37f, 0.37f); kv.Key.localRotation = kv.Value.r * off; }
                    a.NormalizedTime = 0.5f;
                    var parts = new List<string>(); int nb = 0, nd = 0;
                    foreach (var kv in shapeSnap)
                    {
                        var t = kv.Key;
                        bool rp = VErr(t.localPosition, kv.Value.p + new Vector3(0.37f, 0.37f, 0.37f)) > 1e-5f, rr = Quaternion.Angle(t.localRotation, kv.Value.r * off) > 1e-3f, rs = VErr(t.localScale, kv.Value.s * 1.37f) > 1e-5f;
                        if (!rp && !rr && !rs) continue;
                        nb++;
                        bool dp = rp && VErr(t.localPosition, kv.Value.p) > 1e-5f, dr = rr && Quaternion.Angle(t.localRotation, kv.Value.r) > 1e-3f, ds = rs && VErr(t.localScale, kv.Value.s) > 1e-5f;
                        if (dp || dr || ds) nd++;
                        parts.Add($"{t.name}[{(rp ? "p" : "")}{(rr ? "r" : "")}{(rs ? "s" : "")}{(dp || dr || ds ? " differs: " + (dp ? "p" + F(t.localPosition) + " vs " + F(kv.Value.p) : "") + (dr ? "r" + F(t.localRotation) : "") + (ds ? "s" + F(t.localScale) + " vs " + F(kv.Value.s) : "") : "")}]");
                    }
                    if (nb > 0) bound.Add($"{r.Group}/{r.Category}/{r.No} {r.Clip}: rewritten {nb}, clip value differs from the shape value {nd}: {string.Join(" ", parts)}");
                    foreach (var kv in shapeSnap) { kv.Key.localPosition = kv.Value.p; kv.Key.localRotation = kv.Value.r; kv.Key.localScale = kv.Value.s; }
                }
                Info($"guard OFF, *_s bones in the bound set (rewritten after a perturbation): {(bound.Count == 0 ? "none" : string.Join("; ", bound))}");
                lines.AddRange(bound.Where(b => b.StartsWith("19/2/") && !b.Contains("differs from the shape value 0")));
            }
            else Check("guard ON: no *_s bone changes on any test row (incl. emotion rows that bind them)", worstCount == 0, lines.Count == 0 ? $"{rows.Count} rows" : string.Join("; ", lines));
            if (!guard) Info($"guard OFF: emotion rows writing values that differ from the shape value: {(lines.Any(l => l.StartsWith("19/2/")) ? "yes" : "none observed at t 0 / 0.5 with this card")} (rows {string.Join(",", emotion.Select(e => e.Clip))}); the guard mechanism is checked below");
        }
        a.Options.RestoreShapeBones = true;
        a.Load(Stand);
        var guardSnap = Snap(a.ShapeBones);
        foreach (var kv in guardSnap) { kv.Key.localScale = kv.Value.s * 1.37f; kv.Key.localPosition = kv.Value.p + Vector3.one * 0.37f; kv.Key.localRotation = kv.Value.r * Quaternion.Euler(7, 11, 13); }
        c.Step(1);
        int notBack = guardSnap.Count(kv => VErr(kv.Key.localScale, kv.Value.s) > 0 || VErr(kv.Key.localPosition, kv.Value.p) > 0 || QErr(kv.Key.localRotation, kv.Value.r) > 0);
        foreach (var kv in guardSnap) { kv.Key.localPosition = kv.Value.p; kv.Key.localRotation = kv.Value.r; kv.Key.localScale = kv.Value.s; }
        Check("guard ON: every guarded shape bone is back to its exact post-build value after one step (mechanism, all channels perturbed)", notBack == 0,
              $"{notBack} of {guardSnap.Count} not restored; *_s bones by name {shapeSnap.Count}, of them guarded {shapeSnap.Keys.Count(t => guardSnap.ContainsKey(t))}; unguarded *_s: {string.Join(",", shapeSnap.Keys.Where(t => !guardSnap.ContainsKey(t)).Select(t => t.name))}");
    }

    static void RootMotionTests(Ch c)
    {
        Section("root motion in place: mod 69/662/6625 Slow Walk, 10 cycles");
        var a = c.A; a.Speed = 1f;
        var anim = c.Ctx.Anim.transform; var p0 = anim.localPosition; var r0 = anim.localRotation;
        a.Load(Mod, 0f);
        var hips = c.B("cf_J_Hips"); var h0 = hips.localPosition;
        float maxXZ = 0;
        float len = a.StateInfo.length;
        int frames = Mathf.CeilToInt(10 * len * 60);
        for (int i = 0; i < frames; i++) { c.Step(); var d = hips.localPosition - h0; maxXZ = Mathf.Max(maxXZ, new Vector2(d.x, d.z).magnitude); }
        Check("p_cf_anim unchanged after 10 cycles; hips stay near the start (walk in place)", VErr(anim.localPosition, p0) < 1e-6f && RotationCompare.Deg(anim.localRotation, r0) < 1e-4f && maxXZ < 3f && !a.UnbakedRootMotion,
              $"cycle {len:F3} s, {frames} steps, normalized {a.NormalizedTime:F3}, max hips XZ offset {maxXZ:F3}, unbaked flag {a.UnbakedRootMotion}, loop {a.StateInfo.loop}");
        a.Load(Stand);
    }

    static void MouthTests(Ch c)
    {
        Section("mouth during animation");
        var e = HS2Next.Motion.Face.CharaExpression.Of(c.Root);
        if (e == null) { Check("expression present", false); return; }
        e.SetMouthOpen(0.6f); e.SetStudioMouth(3); e.SetLipSync(false);
        var a = c.A;
        a.Load(Dance, 0.3f); c.Step(10); a.Restart(); a.NormalizedTime = 0.7f; a.Speed = 0f; c.Step(5); a.Speed = 1f; a.Load(Stand);
        Check("load / play / restart / pause keep mouth open, mouth pattern and lip sync", Mathf.Abs(e.MouthOpen - 0.6f) < 1e-6f && e.MouthPtn == 3 && !e.LipSync,
              $"open {e.MouthOpen}, ptn {e.MouthPtn}, lipsync {e.LipSync}");
        e.SetMouthOpen(0f); e.SetStudioMouth(0); e.SetLipSync(true);
    }

    static void Interplay(Ch c)
    {
        Section("interplay with the motion stages (dance 11/5/0, 120 steps)");
        var a = c.A; a.Speed = 1f;
        var look = CharaLook.Of(c.Root); var corr = CharaJointCorrection.Of(c.Root); var dyn = c.M.Get<DynamicBoneStage>();
        var headJ = c.B("cf_J_Head"); var dam = c.B("cf_J_ArmUp01_dam_L");
        var target = new GameObject("anim_test_target").transform; target.SetParent(c.Root.transform, false);
        LookTargets.DefaultCamera = target;
        if (corr != null) corr[0] = true;
        Quaternion Pass(int neck, out string info)
        {
            a.Load(Dance, 0f); c.M.ResetAll(); look?.SetNeckPattern(neck);
            target.position = headJ.position + new Vector3(-25, 0, 15);
            int e0 = c.M.Errors; bool nan = false; float hairRatio = 0; double damRef = 0;
            for (int f = 0; f < 120; f++)
            {
                c.Step();
                foreach (var t in c.Root.GetComponentsInChildren<Transform>()) { var p = t.localPosition; var q = t.localRotation; if (float.IsNaN(p.x + p.y + p.z + q.x + q.y + q.z + q.w)) { nan = true; break; } }
                if (dyn != null) foreach (var ch in dyn.Chains)
                    {
                        if (ch.Category != DynamicBoneCategory.Hair || !ch.Active || ch.ParticleCount < 2 || !ch.Root || ch.TotalLength < 1e-4f) continue;
                        for (int i = 0; i < ch.ParticleCount; i++) hairRatio = Mathf.Max(hairRatio, (ch.Position(i) - ch.Root.position).magnitude / ch.TotalLength);
                    }
                if (corr != null && dam != null && f % 30 == 29) { var q = dam.localRotation; corr.Refresh(); damRef = Math.Max(damRef, RotationCompare.Deg(q, dam.localRotation)); }
            }
            info = $"neck {neck}: motion errors {c.M.Errors - e0}, NaN {nan}, hair max distance / chain length {hairRatio:F3}, dam vs re-evaluation {damRef:F5} deg";
            if (c.M.Errors - e0 > 0 || nan || hairRatio > 1.05f || damRef > 1e-3f) info = "BAD " + info;
            return headJ.rotation;
        }
        var qA = Pass(3, out var iA); var qL = Pass(1, out var iL);
        var to = (target.position - headJ.position).normalized;
        float angA = Vector3.Angle(qA * Vector3.forward, to), angL = Vector3.Angle(qL * Vector3.forward, to);
        Check("120 steps with look, correction (category 0) and dynamic bones: no errors, no NaN, hair bounded, dam bones from this step's arm", !iA.StartsWith("BAD") && !iL.StartsWith("BAD"), iA + " | " + iL);
        Check("neck look (pattern 1, target left) still turns the animated head", angL < angA - 10f, $"head-to-target angle: animation {angA:F1} deg, look {angL:F1} deg");
        if (corr != null) corr[0] = false;
        look?.SetNeckPattern(3);
        LookTargets.DefaultCamera = null;
        Object.DestroyImmediate(target.gameObject);
    }

    static void SceneRoundTrip(Ch c)
    {
        Section("scene round trip");
        var a = c.A;
        a.Load(Rifle, 0.2f);
        a.Speed = 0.8f; a.Pattern = 0.3f; a.ForceLoop = true; a.OptionVisible = false; a.OptionParam1 = 0.2f; a.OptionParam2 = 0.4f; a.Hands[0] = 3; a.Hands[1] = 5;
        c.Step(45);
        var bones = AnimBones(c).ToList();
        // Compare the stage-A + hand-pattern pose (later stages such as the neck look depend on their own history).
        a.Stage.Step(new MotionFrame(0f, 0f, 0f, 0)); a.Hands.Apply();
        var pose = Snap(bones);
        var s = a.Save();
        Info("saved: " + s);
        var json = JsonUtility.ToJson(s);
        a.Stop();
        a.Speed = 1f; a.Pattern = 0; a.ForceLoop = false; a.OptionVisible = true; a.OptionParam1 = a.OptionParam2 = 0; a.Hands[0] = 0; a.Hands[1] = 0;
        var s2 = JsonUtility.FromJson<AnimationSceneState>(json);
        bool ok = a.Restore(db, s2);
        c.A.Hands.Apply();
        double poseDiff = MaxPoseDiff(pose, out var worstBone);
        var t = a.Save();
        Check("Restore reproduces every field (JSON round trip) and the raw time", ok && t.Guid == s.Guid && t.Group == s.Group && t.Category == s.Category && t.No == s.No && t.Speed == s.Speed &&
              t.Pattern == s.Pattern && t.ForceLoop == s.ForceLoop && t.OptionVisible == s.OptionVisible && t.OptionParam1 == s.OptionParam1 && t.OptionParam2 == s.OptionParam2 &&
              t.HandLeft == s.HandLeft && t.HandRight == s.HandRight && Mathf.Abs(t.NormalizedTime - s.NormalizedTime) < 1e-5f, "restored: " + t);
        Check("restored pose equals the saved pose (skeleton, incl. hand patterns)", poseDiff < 1e-3f, $"max diff {poseDiff:F5} ({worstBone}) over {bones.Count} animation-driven bones");
        Check("restored option item hidden", a.Items.Count == 1 && a.Items.Items[0].Renderers.All(r => !r.enabled));
        var m = CharaAnimation.Resolve(db, ModGuid, 69, 662, 6625);
        Check("identity: (GUID, group, category, no) resolves the mod row", m == Mod);
        a.Speed = 1f; a.Pattern = 0; a.ForceLoop = false; a.OptionVisible = true; a.OptionParam1 = a.OptionParam2 = 0; a.Hands[0] = 0; a.Hands[1] = 0;
        a.Load(Stand);
    }

    static void EngineAgreement(Ch c)
    {
        Section("engines: Playable (default) vs Animator fallback");
        var a = c.A;
        var bones = AnimBones(c).ToList();
        var res = new Dictionary<AnimationEngine, Dictionary<Transform, (Vector3 p, Quaternion r, Vector3 s)>>();
        var times = new Dictionary<AnimationEngine, float>();
        foreach (var e in new[] { AnimationEngine.Playable, AnimationEngine.Animator })
        {
            a.Options.Engine = e;
            a.Speed = 1f; a.ForceLoop = true;
            a.Load(Rifle, 0f);
            c.Step(300);
            res[e] = Snap(bones); times[e] = a.NormalizedTime;
            Info($"{e}: Animator enabled {a.Animator.enabled}, own controller {(a.Animator.runtimeAnimatorController ? a.Animator.runtimeAnimatorController.name : "none")}, normalized after 300 steps {a.NormalizedTime:F5}, items {a.Items.Count}");
        }
        double maxDiff = 0; string wb = "-";
        foreach (var kv in res[AnimationEngine.Playable])
        {
            var o = res[AnimationEngine.Animator][kv.Key];
            double d = Math.Max(RotationCompare.Deg(kv.Value.r, o.r), VErr(kv.Value.p, o.p));
            if (d > maxDiff) { maxDiff = d; wb = kv.Key.name; }
        }
        Check("both engines give the same pose and time (rifle row with force loop, 300 steps)", maxDiff < 1e-3f && Mathf.Abs(times[AnimationEngine.Playable] - times[AnimationEngine.Animator]) < 1e-5f,
              $"max bone diff {maxDiff:E2} ({wb}) over {bones.Count} animation-driven bones");
        a.Options.Engine = AnimationEngine.Playable; a.ForceLoop = false;
        a.Load(Stand);
        Check("switching back to the playable engine", a.Animator.enabled && a.Animator.runtimeAnimatorController == null && a.IsLoaded);
    }

    // An H row plays like HS2 plays it (the clothed female target; judged by numbers only, nothing rendered).
    static void HRowPlays(Ch c)
    {
        Section("H row: played (numbers only)");
        var a = c.A;
        var h = db.Anime.All.FirstOrDefault(r => r.IsH && r.Visible && r.IsVanilla && !string.IsNullOrEmpty(r.OverrideFile));
        if (h == null) { Check("an H row with an override exists", false); return; }
        a.Load(Stand, 0.4f);
        bool played = a.Load(h);
        int clip = Animator.StringToHash(h.Clip ?? "");
        Check("Load(H row) plays: IsHPlaying, Current = Playing = the row, layer-0 state = its clip, an override controller bound",
              played && a.IsHPlaying && a.Current == h && a.Playing == h && a.StateInfo.shortNameHash == clip && a.Driver.Controller is AnimatorOverrideController,
              $"row {h.Group}/{h.Category}/{h.No}, controller {(a.Driver != null ? a.Driver.Controller.name : "-")}");
        bool pv = true;
        for (int i = 0; i < 8; i++) pv &= a.PvFlags[i] == h.PvFlags[i];
        Check("the 8 PV flags are the row's", pv);
        a.Load(Stand);
        Check("a plain row after it: not H, PV 0-3 on, no sway line applies (everything enabled)",
              !a.IsHPlaying && a.PvFlags[0] && a.PvFlags[3] && a.Sway.Matched == null && a.Sway.BustL && a.Sway.BustR && a.Sway.HipL && a.Sway.HipR && a.Sway.MaskL == 0 && a.Sway.MaskR == 0,
              a.Sway.ToString());
    }

    static void AllocAndTiming(Ch c)
    {
        Section("allocation and timing");
        var a = c.A;
        a.Speed = 1f; a.ForceLoop = true; a.Load(Rifle, 0.9f); a.Hands[0] = 2;
        var frame = new MotionFrame(Dt, Dt, 0, 0);
        for (int i = 0; i < 50; i++) { a.Stage.Step(frame); a.Hands.Step(frame); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 300; i++) { a.Stage.Step(frame); a.Hands.Step(frame); }
        long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        Check("no allocation per step (anim + hands, force loop on, option item, 300 steps)", alloc == 0, $"{alloc} bytes");
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 600; i++) a.Stage.Step(frame);
        double us = sw.Elapsed.TotalMilliseconds * 1000 / 600;
        sw.Restart();
        for (int i = 0; i < 600; i++) a.Hands.Step(frame);
        double hu = sw.Elapsed.TotalMilliseconds * 1000 / 600;
        sw.Restart(); c.Step(120); double full = sw.Elapsed.TotalMilliseconds * 1000 / 120;
        Info($"stage A {us:F1} us/step, hands {hu:F2} us/step, whole CharaMotion step {full:F0} us");
        a.Hands[0] = 0; a.ForceLoop = false;
        var sw2 = Stopwatch.StartNew(); a.Load(Dance); double lm = sw2.Elapsed.TotalMilliseconds;
        sw2.Restart(); a.Load(Mod); double lmod = sw2.Elapsed.TotalMilliseconds;
        sw2.Restart(); a.Load(Mod); double lmod2 = sw2.Elapsed.TotalMilliseconds;
        Info($"Load: dance (other vanilla bundle) {lm:F1} ms, mod first {lmod:F1} ms, mod again {lmod2:F2} ms");
        a.Load(Stand);
    }

    static void StopTests(Ch c)
    {
        Section("stop");
        var a = c.A;
        a.Load(Rifle, 0.4f);
        var arm = c.B("cf_J_ArmUp00_R");
        a.Stop();
        var animPrefab = c.Ctx.Catalog.LoadAsset<GameObject>("studio/base/00.unity3d", "p_cf_anim");
        var prefArm = CharaAssembler.Find(animPrefab.transform, "cf_J_ArmUp00_R");
        Check("Stop: driver and items released, Animator disabled without controller, skeleton back to the built pose",
              !a.IsLoaded && a.Items.Count == 0 && !a.Animator.enabled && a.Animator.runtimeAnimatorController == null && RotationCompare.Deg(arm.localRotation, prefArm.localRotation) < 1e-3f,
              $"arm vs prefab {Quaternion.Angle(arm.localRotation, prefArm.localRotation):F4} deg");
        c.Step(5);
        Check("steps after Stop do nothing to the arm", RotationCompare.Deg(arm.localRotation, prefArm.localRotation) < 1e-3f);
    }

    // ================================================================ male

    static void MaleTests(ContentCatalog gd, string path)
    {
        Section("male card: " + Path.GetFileNameWithoutExtension(path));
        var c = Build(gd, path);
        try
        {
            foreach (var l in c.Asm.Log.Where(l => l.StartsWith("anim:"))) Info(l);
            var a = c.A;
            Check("male: attached, body height 0.75", a != null && a.Hands != null && Mathf.Abs(a.BodyHeight - 0.75f) < 1e-6f && a.Hands.BoundCount(0) == 15 && a.Hands.BoundCount(1) == 15);
            Check("male: 19/0/0 loads and plays", a.Load(Stand, 0.5f) && Mathf.Abs(a.NormalizedTime - 0.5f) < 1e-6f);
            HandTests(c, "male");
            int e0 = c.M.Errors; c.Step(60);
            Check("male: 60 steps without motion errors", c.M.Errors == e0);
        }
        finally { Object.DestroyImmediate(c.Root); }
    }

    // ================================================================ render

    static void Render(ContentCatalog gd, string path)
    {
        Section("render (BR-Chan, URP, full body)");
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Check("render needs a graphics device", false, "run without -nographics"); return; }
        ShaderUtil.allowAsyncCompilation = false;
        var c = Build(gd, path);
        try
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HS2Next.App.AppPipeline.MeasureAssetPath);
            HS2Next.ShaderMap.ApplyUrp(c.Root);
            foreach (var s in c.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) s.forceMatrixRecalculationPerRender = true;
            var expr = HS2Next.Motion.Face.CharaExpression.Of(c.Root);
            if (expr != null) expr.Blink = false;
            var cases = new List<(string name, StudioAnime row, float t)>
            {
                ("stand_0", Stand, 0f), ("stand_05", Stand, 0.5f), ("walk_0", Mod, 0f), ("walk_05", Mod, 0.5f),
                ("sit_0", Chair, 0f), ("sit_05", Chair, 0.5f), ("rifle_039", Rifle, 0.39f),
            };
            // Option items are instantiated by Load, after the character's mapping: the hook maps them (and the render settings).
            int hookObjects = 0;
            c.A.OptionItemsLoaded += (_, objs) =>
            {
                foreach (var o in objs)
                {
                    HS2Next.ShaderMap.ApplyUrp(o);
                    foreach (var smr in o.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.forceMatrixRecalculationPerRender = true;
                }
                hookObjects += objs.Count;
            };
            var bytes = new List<byte[]>();
            foreach (var (name, row, t) in cases)
            {
                c.A.Speed = 0f;
                hookObjects = 0;
                c.A.Load(row, t);
                c.M.ResetAll();
                c.Step(60);   // hair and bust settle on the held pose
                string prefix = Path.Combine(OutDir, "anim_" + name);
                CharaRenderTest.RenderViews(c.Root, prefix, asset, "full");
                string file = prefix + ".png";
                if (File.Exists(file)) File.Delete(file);
                File.Move(prefix + "_full.png", file);
                bytes.Add(File.ReadAllBytes(file));
                Info($"anim_{name}.png: {row.Name} ({row.Group}/{row.Category}/{row.No}) t {t}, normalized {c.A.NormalizedTime:F3}, items {c.A.Items.Count} (OptionItemsLoaded objects {hookObjects})");
            }
            int distinct = bytes.Select(b => Convert.ToBase64String(System.Security.Cryptography.SHA1.Create().ComputeHash(b))).Distinct().Count();
            Check("renders written and all different", distinct == cases.Count, $"{distinct} distinct of {cases.Count}");
        }
        finally
        {
            Object.DestroyImmediate(c.Root);
            GraphicsSettings.defaultRenderPipeline = null; QualitySettings.renderPipeline = null;
        }
    }
}
