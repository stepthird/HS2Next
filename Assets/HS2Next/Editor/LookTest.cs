// Batch test of the neck look and eye look stages (Runtime/Motion/Look).
// Run: -executeMethod LookTest.Run [-card <png>] [-render]
//   1. data checks (type states, values, bone bindings, Init references, helper objects);
//   2. worked examples (E1-E9, N1-N9, the combined example, the E2 / N1 traces) on a bare rig: p_cf_anim
//      from studio/base/00 + p_cf_head_bone, unposed and unshaped at the origin, stepped at 1/60 s until settled;
//   3. behaviours (type blend, limit-break hysteresis, AWAY switching, rate, Step(0), ResetAll, NaN guards,
//      first frame, NO_LOOK, look target, FK hook, target types, switches) on the rig;
//   4. card builds: the -card card (default <game>/UserData/chara/female/BR-Chan HS2 Rev2.png) and the first male
//      card of <game>/UserData/chara/male: bindings, defaults, first frame, the bending rule on shaped geometry;
//   5. -render (graphics device): BR-Chan face close-ups (URP) with the target far left / right / up / down,
//      eyes only and neck + eyes, to Spike/out/motion/look_<case>.png.
// Report: Spike/out/motion/look_report.txt (PASS/FAIL per check); EditorApplication.Exit(1) on any FAIL.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HS2Next.Chara;
using HS2Next.Content;
using HS2Next.Data;
using HS2Next.Motion;
using HS2Next.Motion.Look;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class LookTest
{
    const float Dt = 1f / 60f;
    const float Tol = 0.05f;   // degrees (task tolerance)
    static StringBuilder rep;
    static int passes, fails;
    static float worstExample;   // largest error over the worked examples (degrees)

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

    static void Section(string s) { rep.AppendLine(); rep.AppendLine("== " + s); }

    public static void Run()
    {
        rep = new StringBuilder(); passes = fails = 0; worstExample = 0;
        Directory.CreateDirectory(OutDir);
        bool render = TestArgs.Get().Contains("-render");
        var head = new StringBuilder();
        var gd = CardResolveTest.BuildDb(head);
        rep.AppendLine($"look test (graphics {SystemInfo.graphicsDeviceType}, render {(render ? "on" : "off")})");
        rep.Append(head);
        try
        {
            Guard("rig", () => RigTests(gd));
            string female = Arg("-card", Path.Combine(CardResolveTest.GameRoot, @"UserData\chara\female\BR-Chan HS2 Rev2.png"));
            Guard("card female", () => CardTests(gd, female));
            string maleDir = Path.Combine(CardResolveTest.GameRoot, @"UserData\chara\male");
            var male = Directory.Exists(maleDir)
                ? Directory.GetFiles(maleDir, "*.png", SearchOption.AllDirectories).OrderBy(f => f.Substring(maleDir.Length).Replace('\\', '/'), StringComparer.Ordinal).FirstOrDefault()
                : null;
            if (male != null) Guard("card male", () => CardTests(gd, male));
            else Check("male card present", false, maleDir);
            if (render) Guard("render", () => RenderCases(gd, female));
        }
        finally
        {
            LookTargets.DefaultCamera = null;
            EyeLookStage.GlobalEnabled = true;
        }
        rep.Insert(0, $"SUMMARY: {passes} PASS, {fails} FAIL; worst worked-example error {worstExample:F4} deg (tolerance {Tol})\n");
        File.WriteAllText(Path.Combine(OutDir, "look_report.txt"), rep.ToString());
        gd.UnloadAll();
        Debug.Log($"LOOK TEST DONE: {passes} PASS, {fails} FAIL");
        if (fails > 0) TestExit.Exit(1);
    }

    static void Guard(string what, Action a)
    {
        try { a(); }
        catch (Exception e) { Check(what + " (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
    }

    // ================================================================ rig

    sealed class Rig
    {
        public GameObject Root;
        public CharaContext Ctx;
        public CharaMotion Motion;
        public CharaLook Look;
        public NeckLookStage Neck;
        public EyeLookStage Eyes;
        public Transform Target;
        public Dictionary<string, Transform> B, H;
        public Transform Bone(string n) => B.TryGetValue(n, out var t) ? t : H.TryGetValue(n, out var u) ? u : null;
    }

    static Dictionary<string, Transform> Dict(Transform root)
    {
        var d = new Dictionary<string, Transform>();
        void Walk(Transform t) { if (!d.ContainsKey(t.name)) d[t.name] = t; for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i)); }
        Walk(root);
        return d;
    }

    static Rig BuildRig(ContentCatalog gd)
    {
        var root = new GameObject("chaF_lookrig");
        var top = new GameObject("BodyTop"); top.transform.SetParent(root.transform, false);
        var motion = root.AddComponent<CharaMotion>(); motion.AutoStep = false;
        var anim = Object.Instantiate(gd.LoadAsset<GameObject>("studio/base/00.unity3d", "p_cf_anim"));
        anim.name = "p_cf_anim"; anim.transform.SetParent(top.transform, false);
        var animator = anim.GetComponent<Animator>(); if (animator) animator.enabled = false;
        var b = Dict(CharaAssembler.Find(anim.transform, "cf_J_Root"));
        var hb = Object.Instantiate(gd.LoadAsset<GameObject>("chara/oo_base.unity3d", "p_cf_head_bone"));
        hb.name = "p_cf_head_bone"; hb.transform.SetParent(b["cf_J_Head_s"], false);
        var ctx = new CharaContext { Root = root, BodyTop = top, Anim = anim, HeadBone = hb, Motion = motion, BodyBones = b, HeadBones = Dict(hb.transform), Log = new List<string>(), Catalog = gd };
        motion.Log = ctx.Log;
        LookSetup.Attach(ctx);
        var r = new Rig { Root = root, Ctx = ctx, Motion = motion, Look = CharaLook.Of(root), B = b, H = ctx.HeadBones };
        r.Neck = motion.Get<NeckLookStage>(); r.Eyes = motion.Get<EyeLookStage>();
        r.Target = new GameObject("look_test_target").transform;
        r.Target.SetParent(root.transform, false);
        LookTargets.DefaultCamera = r.Target;
        return r;
    }

    static Rig FromBuild(GameObject chara, CharaContext ctx)
    {
        var r = new Rig { Root = chara, Ctx = ctx, Motion = ctx.Motion, Look = CharaLook.Of(chara), B = ctx.BodyBones, H = ctx.HeadBones };
        r.Motion.AutoStep = false;
        r.Neck = r.Motion.Get<NeckLookStage>(); r.Eyes = r.Motion.Get<EyeLookStage>();
        r.Target = new GameObject("look_test_target").transform;
        r.Target.SetParent(chara.transform, false);
        LookTargets.DefaultCamera = r.Target;
        return r;
    }

    // Steps until the look angles stop changing (or maxFrames); returns the frames stepped.
    static int Settle(Rig r, int maxFrames = 1500, float dt = Dt)
    {
        float prev = Signature(r); int quiet = 0;
        for (int f = 1; f <= maxFrames; f++)
        {
            r.Motion.Step(dt);
            float s = Signature(r);
            quiet = Mathf.Abs(s - prev) < 1e-6f ? quiet + 1 : 0;
            prev = s;
            if (quiet >= 10) return f;
        }
        return maxFrames;
    }

    static float Signature(Rig r)
    {
        float s = 0;
        if (r.Neck != null) foreach (var b in r.Neck.Bones) s += b.AngleH * 1.37f + b.AngleV * 0.71f + Quaternion.Angle(b.NeckBone.localRotation, Quaternion.identity) * 0.53f;
        if (r.Eyes != null) for (int i = 0; i < 2; i++) s += r.Eyes.AngleH(i) * (1.1f + i) + r.Eyes.AngleV(i) * (0.3f + i);
        return s;
    }

    static void Fresh(Rig r, int neckPtn, int eyePtn)
    {
        r.Motion.ResetAll();
        r.Look.SetNeckPattern(neckPtn);
        r.Look.SetEyePattern(eyePtn);
        r.Look.SetNeckTarget(null); r.Look.SetEyeTarget(null);   // camera = the test target
    }

    static string V2(float a, float b) => $"({a:F3}, {b:F3})";
    static float Wrap(float a) => Mathf.DeltaAngle(0f, a);

    static bool Near(float a, float b, float tol, ref float err) { err = Mathf.Max(err, Mathf.Abs(a - b)); return Mathf.Abs(a - b) <= tol; }

    // ================================================================ rig tests

    static void RigTests(ContentCatalog gd)
    {
        var r = BuildRig(gd);
        try
        {
            Section("rig: setup");
            foreach (var l in r.Ctx.Log) rep.AppendLine("   " + l);
            Check("stages registered", r.Neck != null && r.Eyes != null && r.Look != null,
                $"neck {(r.Neck != null)}, eyes {(r.Eyes != null)}, facade {(r.Look != null)}; order {string.Join(" < ", r.Motion.Stages.Select(s => s.Name + ":" + (int)s.Phase))}");
            if (r.Neck == null || r.Eyes == null || r.Look == null) return;
            Check("stage interface: eye stage provides IEyeLookOutputs", r.Motion.GetAll<IMotionStage>().OfType<IEyeLookOutputs>().Count() == 1);
            DataChecks(r, "rig", geometry: true);
            Examples(r);
            Behaviours(r);
        }
        finally { Object.DestroyImmediate(r.Root); }
    }

    static object Raw(Transform root, string type, string field)
    {
        foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
            if (mb != null && mb.GetType().Name == type)
                return mb.GetType().GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(mb);
        return null;
    }

    static void DataChecks(Rig r, string label, bool geometry)
    {
        Section($"{label}: data");
        var n = r.Neck; var e = r.Eyes;
        rep.AppendLine($"   compat types: neck calc from {n.Source}, eye calc from {e.Source}");
        // ---- neck
        int[] nTypes = { 3, 1, 2, 0, 4 };
        Check($"{label} neck: 5 type states with lookTypes 3,1,2,0,4", n.States.Length == 5 && n.States.Select(s => s.LookType).SequenceEqual(nTypes),
            "types " + string.Join(",", n.States.Select(s => s.LookType)));
        bool p0 = n.States[0].Params.All(p => p.Up == 0 && p.Down == 0 && p.Min == 0 && p.Max == 0) && n.States[0].HAngleLimit == 0;
        bool pRest = n.States.Skip(1).All(s => s.Params.Length == 2 && s.Params.All(p => p.Up == -25 && p.Down == 25 && p.Min == -40 && p.Max == 40)
                                              && s.HAngleLimit == 90 && s.VAngleLimit == 90 && s.LimitBreakCorrection == 10);
        bool leap = n.States.All(s => s.LeapSpeed == 2 && !s.SerializedLimitBreak);
        Check($"{label} neck: limits (ptn 0 all 0; 1-4 -25/25/-40/40, 90/90, correction 10, leap 2)", p0 && pRest && leap,
            string.Join(" | ", n.States.Select(s => $"{s.LookType}: lim {s.HAngleLimit}/{s.VAngleLimit} corr {s.LimitBreakCorrection} away {s.LimitAway} leap {s.LeapSpeed} p0 {s.Params[0].Up}/{s.Params[0].Down}/{s.Params[0].Min}/{s.Params[0].Max}")));
        Check($"{label} neck: limitAway 10 on AWAY only", n.States[2].LimitAway == 10 && n.States.Where((s, i) => i != 2).All(s => s.LimitAway == 0));
        Check($"{label} neck: bones 0 neck {{NeckRef, cf_J_Neck}} / 1 head {{HeadRef, cf_J_Head}}, no control bones",
            n.Bones.Length == 2 && n.Bones[0].Reference?.name == "NeckRef" && n.Bones[0].NeckBone?.name == "cf_J_Neck"
            && n.Bones[1].Reference?.name == "HeadRef" && n.Bones[1].NeckBone?.name == "cf_J_Head" && n.Bones.All(b => b.ControlBone == null)
            && n.Bones[0].Name == "首" && n.Bones[1].Name == "頭",
            string.Join(", ", n.Bones.Select(b => $"{b.Name}:{b.Reference?.name}/{b.NeckBone?.name}")));
        Check($"{label} neck: aim = aim, boneCalcAngle = NeckRef, controller on cf_J_Neck", n.Aim?.name == "aim" && n.CalcAngle?.name == "NeckRef" && n.Controller?.name == "cf_J_Neck");
        Check($"{label} neck: leapTime 1, calcLerp 1, skipCalc 0, enabled", n.ChangeTypeLeapTime == 1 && n.CalcLerp == 1 && !n.SkipCalc && n.CalcEnabled);
        float c25 = n.Curve != null ? n.Curve.Evaluate(0.25f) : -1, c50 = n.Curve != null ? n.Curve.Evaluate(0.5f) : -1;
        Check($"{label} neck: curve = 2u - u^2", Mathf.Abs(c25 - 0.4375f) < 1e-4f && Mathf.Abs(c50 - 0.75f) < 1e-4f, $"w(0.25) {c25:F5}, w(0.5) {c50:F5}");
        var rawPtn = Raw(r.Ctx.Anim.transform, "NeckLookControllerVer2", "ptnNo"); var rawRate = Raw(r.Ctx.Anim.transform, "NeckLookControllerVer2", "rate");
        Check($"{label} neck: serialized controller ptnNo 0, rate 1", Equals(rawPtn, 0) && Equals(rawRate, 1f), $"ptnNo {rawPtn}, rate {rawRate}");
        Check($"{label} neck: init backups = rest local rotation (not zero quaternions)",
            n.Bones.All(b => Mathf.Abs(Quaternion.Dot(b.FixAngleBackup, b.FixAngleBackup) - 1) < 1e-4f && Mathf.Abs(Quaternion.Dot(b.Rest, b.Rest) - 1) < 1e-4f));

        // ---- eyes
        int[] eTypes = { 3, 1, 2, 0, 4 };
        Check($"{label} eyes: 5 type states with lookTypes 3,1,2,0,4", e.States.Length == 5 && e.States.Select(s => s.LookType).SequenceEqual(eTypes),
            "types " + string.Join(",", e.States.Select(s => s.LookType)));
        float[,] tab = { { -10, 10, -13, 13, 2.5f, 110, 80 }, { -8, 12, -20, 20, 10, 160, 120 }, { -10, 5, -12, 12, 5, 110, 80 }, { -10, 10, -13, 13, 2.5f, 110, 80 }, { -10, 10, -13, 13, 2.5f, 180, 180 } };
        bool tabOk = true; var bad = new List<string>();
        for (int i = 0; i < 5 && i < e.States.Length; i++)
        {
            var s = e.States[i];
            float[] got = { s.Up, s.Down, s.Min, s.Max, s.LeapSpeed, s.HAngleLimit, s.VAngleLimit };
            for (int k = 0; k < 7; k++) if (got[k] != tab[i, k]) { tabOk = false; bad.Add($"ptn {i} col {k}: {got[k]} != {tab[i, k]}"); }
            if (s.Threshold != 0 || s.BendingMultiplier != 0.4f || s.MaxAngleDifference != 10 || s.ForntTagDis != 50 || s.NearDis != 2) { tabOk = false; bad.Add($"ptn {i} common values"); }
        }
        Check($"{label} eyes: state table (up/down/min/max/leap/hLimit/vLimit, threshold 0, x0.4, 10, 50, 2)", tabOk, string.Join("; ", bad));
        Check($"{label} eyes: rootNode cf_J_FaceUp_tz, eyes [cf_J_look_L L, cf_J_look_R R], sorasi 1.5, no targetObj",
            e.RootNode?.name == "cf_J_FaceUp_tz" && e.Eyes.Length == 2 && e.Eyes[0].Transform?.name == "cf_J_look_L" && e.Eyes[0].LR == 0
            && e.Eyes[1].Transform?.name == "cf_J_look_R" && e.Eyes[1].LR == 1 && e.SorasiRate == 1.5f && e.TargetObj == null
            && e.HeadLookVector == Vector3.forward && e.HeadUpVector == Vector3.up);
        var rawEye = Raw(r.Ctx.HeadBone.transform, "EyeLookController", "ptnNo");
        Check($"{label} eyes: serialized controller ptnNo 1", Equals(rawEye, 1), $"ptnNo {rawEye}");
        Check($"{label} eyes: Init references from the prefab rest pose: look (0,0,1), up (0,1,0), orig identity",
            e.ReferencesFromPrefab == 2 && e.Eyes.All(x => (x.RefLook - Vector3.forward).magnitude < 1e-5f && (x.RefUp - Vector3.up).magnitude < 1e-5f && RotationCompare.Deg(x.Orig, Quaternion.identity) < 1e-3f),
            string.Join(", ", e.Eyes.Select(x => $"{x.RefLook:F4}/{x.RefUp:F4}/{x.Orig.eulerAngles:F3}")) + $", from prefab {e.ReferencesFromPrefab}");

        // ---- targets and defaults
        var t = r.Look.Targets;
        Check($"{label} targets: helper objects under cf_J_Spine03 / cf_J_Head_s",
            t.NeckTargetP?.parent?.name == "cf_J_Spine03" && t.NeckTarget?.parent == t.NeckTargetP && t.EyesTargetP?.parent?.name == "cf_J_Head_s" && t.EyesTarget?.parent == t.EyesTargetP);
        Check($"{label} targets: Studio look target 'Look At Target' under cf_J_Head at (0,0,0.25)",
            t.LookAtTarget != null && t.LookAtTarget.parent?.name == "cf_J_Head" && (t.LookAtTargetDefaultLocal - new Vector3(0, 0, 0.25f)).magnitude < 1e-4f,
            t.LookAtTarget != null ? $"{t.LookAtTarget.name} under {t.LookAtTarget.parent?.name} at {t.LookAtTargetDefaultLocal:F3}" : "none");
        if (geometry)
        {
            var R = e.RootNode.position; var L = e.Eyes[0].Transform.position; var Rr = e.Eyes[1].Transform.position;
            bool g = (R - new Vector3(0, 16.4925f, -0.17f)).magnitude < 2e-3f && (L - new Vector3(-0.324f, 16.4925f, 0.3984f)).magnitude < 2e-3f
                     && (Rr - new Vector3(0.3236f, 16.4925f, 0.3984f)).magnitude < 2e-3f
                     && (n.CalcAngle.position - new Vector3(0, 15.115f, -0.33f)).magnitude < 2e-3f && (n.Aim.position - new Vector3(0, 16.635f, -0.23f)).magnitude < 2e-3f;
            Check($"{label} geometry: rest positions of rootNode, eyes, NeckRef, aim", g,
                $"R {R:F4}, L {L:F4}, Rr {Rr:F4}, NeckRef {n.CalcAngle.position:F4}, aim {n.Aim.position:F4}");
        }
    }

    // ================================================================ worked examples

    static void Examples(Rig r)
    {
        Section("rig: eye examples E1-E9, neck pattern 0, head at rest");
        var R = r.Eyes.RootNode.position;
        var eyes = new (string id, int ptn, Vector3 off, float lh, float lv, float rh, float rv, float rhl, float rhr, float rvr)[]
        {
            ("E1", 1, new Vector3(0, 0, 10), 0.787f, 0, -0.786f, 0, 0.0393f, -0.0393f, 0),
            ("E2", 1, new Vector3(3, 0, 10), 9.414f, 0, 6.337f, 0, 0.4707f, 0.3168f, 0),
            ("E3", 1, new Vector3(0, 10, 20), 0.382f, -8, -0.382f, -8, 0.0191f, -0.0191f, 1),
            ("E4", 1, new Vector3(0, 0, 0.5f), 5.101f, 0, -5.095f, 0, 0.255f, -0.2547f, 0),
            ("E5", 1, new Vector3(10, 0, 10), 20, 0, 20, 0, 1, 1, 0),
            ("E6", 1, new Vector3(0, 0, -10), 0.150f, 0, -0.150f, 0, 0.0075f, -0.0075f, 0),
            ("E7", 0, new Vector3(3, 0, 10), 0.150f, 0, -0.150f, 0, 0.0116f, -0.0115f, 0),
            ("E8", 2, new Vector3(0, 0, 10), -12, 0, -12, 0, -1, -1, 0),
            ("E9", 2, new Vector3(3, -1, 10), -8.586f, -2.284f, -8.586f, -2.330f, -0.7155f, -0.7155f, 0.2284f),
        };
        foreach (var x in eyes)
        {
            Fresh(r, 0, x.ptn);
            r.Target.position = R + x.off;
            int frames = Settle(r);
            var E = r.Eyes; float err = 0, rerr = 0;
            bool ok = Near(E.AngleH(0), x.lh, Tol, ref err) & Near(E.AngleV(0), x.lv, Tol, ref err) & Near(E.AngleH(1), x.rh, Tol, ref err) & Near(E.AngleV(1), x.rv, Tol, ref err);
            bool rates = Near(E.AngleHRate(0), x.rhl, 0.002f, ref rerr) & Near(E.AngleHRate(1), x.rhr, 0.002f, ref rerr) & Near(E.AngleVRate, x.rvr, 0.002f, ref rerr);
            double eul = 0;
            for (int i = 0; i < 2; i++) eul = Math.Max(eul, RotationCompare.Deg(E.Eyes[i].Transform.localRotation, Quaternion.Euler(E.AngleV(i), E.AngleH(i), 0)));
            worstExample = Mathf.Max(worstExample, err);
            Check($"{x.id} ptn {x.ptn} target R+{x.off:F1}", ok && rates && eul < 0.01f,
                $"L {V2(E.AngleH(0), E.AngleV(0))} R {V2(E.AngleH(1), E.AngleV(1))} (spec L {V2(x.lh, x.lv)} R {V2(x.rh, x.rv)}), rates {E.AngleHRate(0):F4}/{E.AngleHRate(1):F4}/{E.AngleVRate:F4}; " +
                $"max err {err:F4} deg, rate err {rerr:F4}, local vs Euler(V,H,0) {eul:F4} deg, settled in {frames} frames");
        }

        // E2 smoothing trace
        Fresh(r, 0, 1);
        r.Target.position = R + new Vector3(3, 0, 10);
        var want = new Dictionary<int, float> { { 1, 1.569f }, { 2, 2.877f }, { 5, 5.631f }, { 10, 7.894f }, { 30, 9.374f }, { 60, 9.414f } };
        var got = new List<string>(); float terr = 0; bool tok = true;
        for (int f = 1; f <= 60; f++)
        {
            r.Motion.Step(Dt);
            if (want.TryGetValue(f, out var w)) { tok &= Near(r.Eyes.AngleH(0), w, 0.005f, ref terr); got.Add($"{f}: {r.Eyes.AngleH(0):F3}"); }
        }
        Check("E2 trace (left angleH at frames 1/2/5/10/30/60)", tok, string.Join(", ", got) + $"; max err {terr:F4}");

        Section("rig: neck examples N1-N9, eye pattern 0");
        var N = r.Neck;
        void NeckCase(string id, Vector3 pos, float nv, float nh, float neckH, float neckV, float headH, float headV, bool fresh, int ptn = 1, bool broken = false)
        {
            if (fresh) Fresh(r, ptn, 0); else r.Look.SetNeckPattern(ptn);
            r.Target.position = pos;
            int frames = Settle(r);
            float err = 0;
            bool ok = Near(N.NowAngle.x, nv, Tol, ref err) & Near(N.NowAngle.y, nh, Tol, ref err)
                      & Near(N.Bones[0].AngleH, neckH, Tol, ref err) & Near(N.Bones[0].AngleV, neckV, Tol, ref err)
                      & Near(N.Bones[1].AngleH, headH, Tol, ref err) & Near(N.Bones[1].AngleV, headV, Tol, ref err);
            double eul = 0;
            foreach (var b in N.Bones) eul = Math.Max(eul, RotationCompare.Deg(b.NeckBone.localRotation, Quaternion.Euler(b.AngleV, b.AngleH, 0)));
            bool br = N.States[N.ClampPtn(ptn)].IsLimitBreak;
            worstExample = Mathf.Max(worstExample, err);
            Check($"{id} ptn {ptn} target {pos:F2}", ok && eul < 0.01f && br == broken,
                $"nowAngle {V2(N.NowAngle.x, N.NowAngle.y)} neck {V2(N.Bones[0].AngleH, N.Bones[0].AngleV)} head {V2(N.Bones[1].AngleH, N.Bones[1].AngleV)} " +
                $"(spec {V2(nv, nh)} / {V2(neckH, neckV)} / {V2(headH, headV)}), broken {br}; max err {err:F4} deg, local vs Euler(V,H,0) {eul:F4}, settled in {frames} frames");
        }
        var n1 = new Vector3(5, 16.5f, 20);
        NeckCase("N1", n1, 0.371f, 13.883f, 0, 0, 13.883f, 0.371f, true);
        NeckCase("N2", new Vector3(10, 26.5f, 10), -34.016f, 43.746f, 3.746f, -9.016f, 40, -25, true);
        NeckCase("N3", new Vector3(10, 16.5f, -0.33f), 0.779f, 90.452f, 40, 0, 40, 0.779f, true);
        NeckCase("N1 (before N4)", n1, 0.371f, 13.883f, 0, 0, 13.883f, 0.371f, true);
        NeckCase("N4", new Vector3(0, 16.5f, -20), 0, 0, 0, 0, 0, 0, false, 1, broken: true);
        NeckCase("N5", n1, 0.371f, 13.883f, 0, 0, 13.883f, 0.371f, false);
        NeckCase("N6", new Vector3(0, 16.5f, 20), -0.382f, 80, 40, 0, 40, -0.382f, true, 2);
        NeckCase("N7", new Vector3(11.5f, 16.5f, 20), -0.332f, 80, 40, 0, 40, -0.332f, false, 2);
        var n7 = N.Bones.Select(b => b.NeckBone.localRotation).ToArray();
        r.Look.SetNeckPattern(4);
        for (int f = 0; f < 120; f++) r.Motion.Step(Dt);
        double n8 = N.Bones.Select((b, i) => RotationCompare.Deg(b.NeckBone.localRotation, n7[i])).Max();
        Check("N8 ptn 4 (FIX) holds N7 for 2 s", n8 < 0.01f, $"max deviation {n8:F5} deg");
        r.Look.SetNeckPattern(3);
        for (int f = 0; f < 15; f++) r.Motion.Step(Dt);
        float y0 = Wrap(N.Bones[0].NeckBone.localEulerAngles.y), y1 = Wrap(N.Bones[1].NeckBone.localEulerAngles.y);
        float e9 = Mathf.Max(Mathf.Abs(y0 - 22.5f), Mathf.Abs(y1 - 22.5f));
        worstExample = Mathf.Max(worstExample, e9);
        Check("N9 ptn 3 (ANIMATION) after FIX, 0.25 s: output euler Y 22.5", e9 <= Tol, $"neck {y0:F3}, head {y1:F3}, timer {N.ChangeTypeTimer:F4} s; err {e9:F4}");
        for (int f = 0; f < 105; f++) r.Motion.Step(Dt);
        double n9b = N.Bones.Max(b => RotationCompare.Deg(b.NeckBone.localRotation, b.Rest));
        Check("N9 at 2 s equals the (rest) animation pose", n9b < 0.01f, $"max deviation {n9b:F5} deg");

        // N1 transition trace from FORWARD
        Fresh(r, 0, 0);
        r.Target.position = n1;
        for (int f = 0; f < 90; f++) r.Motion.Step(Dt);
        r.Look.SetNeckPattern(1);
        var nwant = new Dictionary<int, (float h, float y)> { { 1, (0.463f, 0.015f) }, { 6, (2.555f, 0.485f) }, { 15, (5.534f, 2.421f) }, { 30, (8.862f, 6.647f) }, { 60, (12.067f, 12.067f) }, { 120, (13.646f, 13.646f) } };
        var ngot = new List<string>(); float nerr = 0; bool nok = true;
        for (int f = 1; f <= 120; f++)
        {
            r.Motion.Step(Dt);
            if (!nwant.TryGetValue(f, out var w)) continue;
            float hy = Wrap(N.Bones[1].NeckBone.localEulerAngles.y);
            nok &= Near(N.Bones[1].AngleH, w.h, 0.005f, ref nerr) & Near(hy, w.y, 0.005f, ref nerr);
            ngot.Add($"{f}: {N.Bones[1].AngleH:F3}/{hy:F3}");
        }
        Check("N1 trace from FORWARD (head angleH / output euler Y at frames 1/6/15/30/60/120)", nok, string.Join(", ", ngot) + $"; max err {nerr:F4}");

        // combined
        Fresh(r, 1, 1);
        r.Target.position = n1;
        int cf = Settle(r);
        float cerr = 0, crerr = 0; var E2 = r.Eyes;
        bool cok = Near(E2.AngleH(0), 0.367f, Tol, ref cerr) & Near(E2.AngleV(0), -0.162f, Tol, ref cerr) & Near(E2.AngleH(1), -0.367f, Tol, ref cerr) & Near(E2.AngleV(1), -0.162f, Tol, ref cerr)
                   & Near(N.Bones[1].AngleH, 13.883f, Tol, ref cerr) & Near(N.Bones[1].AngleV, 0.371f, Tol, ref cerr);
        bool crok = Near(E2.AngleHRate(0), 0.0184f, 0.002f, ref crerr) & Near(E2.AngleHRate(1), -0.0183f, 0.002f, ref crerr) & Near(E2.AngleVRate, 0.0202f, 0.002f, ref crerr);
        worstExample = Mathf.Max(worstExample, cerr);
        Check("combined: N1 neck + eyes ptn 1", cok && crok,
            $"head {V2(N.Bones[1].AngleH, N.Bones[1].AngleV)}, L {V2(E2.AngleH(0), E2.AngleV(0))}, R {V2(E2.AngleH(1), E2.AngleV(1))}, rates {E2.AngleHRate(0):F4}/{E2.AngleHRate(1):F4}/{E2.AngleVRate:F4}; " +
            $"max err {cerr:F4}, rate err {crerr:F4}, settled in {cf} frames");
    }

    // ================================================================ behaviours

    static void Behaviours(Rig r)
    {
        Section("rig: behaviours");
        var N = r.Neck; var E = r.Eyes; var look = r.Look;
        var n1 = new Vector3(5, 16.5f, 20);

        // type blend over 1 s
        Fresh(r, 1, 0); r.Target.position = n1; Settle(r);
        float a1 = Quaternion.Angle(N.Bones[1].NeckBone.localRotation, Quaternion.identity);
        look.SetNeckPattern(0);
        for (int f = 0; f < 30; f++) r.Motion.Step(Dt);
        float aMid = Quaternion.Angle(N.Bones[1].NeckBone.localRotation, Quaternion.identity);
        for (int f = 0; f < 30; f++) r.Motion.Step(Dt);
        double aEnd = RotationCompare.Deg(N.Bones[1].NeckBone.localRotation, Quaternion.identity);
        Check("type blend TARGET -> FORWARD: 0.5 s = 25% of the start pose (w = 0.75), 1 s = done", Mathf.Abs(aMid - 0.25f * a1) < 0.01f && aEnd < 0.01f,
            $"start {a1:F3} deg, 0.5 s {aMid:F3} (expected {0.25f * a1:F3}), 1 s {aEnd:F4}");

        // limit-break hysteresis. h and v are both measured on projections: a target behind the
        // chest's frontal plane at the NeckRef height has v = 180, so a level sweep breaks right after 90 deg.
        // Sweep A keeps the target 2x higher than wide (v stays below 100 while h passes 100) to see the
        // 100 / 90 margins; sweep B is a level sweep at head height (what a camera orbit does).
        var nr = N.CalcAngle.position;
        var st = N.States[1];
        (int brk, int res, bool ok) Sweep(Func<float, Vector3> dir, int skipUp, int skipDown)
        {
            Fresh(r, 1, 0);
            int fb = -1, fr = -1; bool ok = true;
            for (int a = 80; a <= 110; a++)
            {
                r.Target.position = nr + dir(a) * 20f; r.Motion.Step(Dt);
                if (st.IsLimitBreak && fb < 0) fb = a;
                if (skipUp > 0 && a != skipUp) ok &= st.IsLimitBreak == (a > skipUp);
            }
            for (int a = 110; a >= 80; a--)
            {
                r.Target.position = nr + dir(a) * 20f; r.Motion.Step(Dt);
                if (!st.IsLimitBreak && fr < 0) fr = a;
                if (skipDown > 0 && a != skipDown) ok &= st.IsLimitBreak == (a > skipDown);
            }
            return (fb, fr, ok);
        }
        float S(float d) => Mathf.Sin(d * Mathf.Deg2Rad);
        float C(float d) => Mathf.Cos(d * Mathf.Deg2Rad);
        var sa = Sweep(a => new Vector3(S(a), 2 * S(a), C(a)), 100, 90);
        Check("limit-break hysteresis, sweep A (h 80 -> 110 -> 80, v < 100): breaks past 100, resumes inside 90", sa.ok && sa.brk == 101 && (sa.res == 90 || sa.res == 89),
            $"breaks at h = {sa.brk}, resumes at h = {sa.res}");
        float dyHead = 16.5f - nr.y;
        var sb = Sweep(a => new Vector3(S(a), dyHead / 20f, C(a)), 0, 0);
        Check("limit-break, sweep B (level at head height): break just past 90 (v flips behind the frontal plane), resume inside 90",
            sb.brk >= 90 && sb.brk <= 92 && sb.res <= 90 && sb.res >= 88, $"breaks at {sb.brk} deg, resumes at {sb.res} deg (spec text says 100; the formulas give this)");

        // AWAY switching
        Fresh(r, 2, 0);
        var aim = N.Aim.position;
        Vector3 AroundAim(float deg) => aim + new Vector3(Mathf.Sin(deg * Mathf.Deg2Rad), 0, Mathf.Cos(deg * Mathf.Deg2Rad)) * 20f;
        float Total() => N.Bones[0].AngleH + N.Bones[1].AngleH;
        r.Target.position = AroundAim(0); Settle(r); float s0 = Total();
        r.Target.position = AroundAim(30); Settle(r); float s30 = Total();
        r.Target.position = AroundAim(75); Settle(r); float s75 = Total();
        r.Target.position = AroundAim(30); Settle(r); float s30b = Total();
        r.Target.position = AroundAim(-75); Settle(r); float sm75 = Total();
        bool away = Mathf.Abs(s0 - 80) < Tol && Mathf.Abs(s30 - 80) < Tol && Mathf.Abs(s75 + 80) < Tol && Mathf.Abs(s30b + 80) < Tol && Mathf.Abs(sm75 - 80) < Tol;
        Check("AWAY side switching (target 0 / 30 / 75 / 30 / -75 deg -> +80 / +80 / -80 / -80 / +80)", away, $"{s0:F2} / {s30:F2} / {s75:F2} / {s30b:F2} / {sm75:F2}");

        // rate: X and Y blended towards cf_J_Neck, Z kept
        var t2 = new Vector3(10, 26.5f, 10);
        var np = N.Controller.position;
        string rates = ""; bool rateOk = true;
        foreach (float rate in new[] { 0f, 0.5f, 1f })
        {
            Fresh(r, 1, 0); look.SetNeckPattern(1, rate); r.Target.position = t2; Settle(r);
            var withRate = N.Bones.Select(b => new Vector2(b.AngleH, b.AngleV)).ToArray();
            Fresh(r, 1, 0); look.SetNeckPattern(1, 1f); r.Target.position = new Vector3(Mathf.Lerp(np.x, t2.x, rate), Mathf.Lerp(np.y, t2.y, rate), t2.z); Settle(r);
            float d = N.Bones.Select((b, i) => (new Vector2(b.AngleH, b.AngleV) - withRate[i]).magnitude).Max();
            rateOk &= d < 0.01f;
            rates += $"rate {rate}: head {V2(withRate[1].x, withRate[1].y)} neck {V2(withRate[0].x, withRate[0].y)} (vs explicit lerped target: {d:F4}); ";
        }
        Check("controller rate 0 / 0.5 / 1 = target with X, Y lerped towards cf_J_Neck (Z kept)", rateOk, rates);
        look.NeckRate = 1f;

        // Step(0) holds
        Fresh(r, 1, 1); r.Target.position = n1; Settle(r);
        var hold = N.Bones.Select(b => b.NeckBone.localRotation).Concat(E.Eyes.Select(x => x.Transform.localRotation)).ToArray();
        var holdA = new[] { E.AngleH(0), E.AngleV(0), E.AngleH(1), E.AngleV(1), N.Bones[1].AngleH };
        r.Target.position = new Vector3(-10, 20, 10);
        for (int f = 0; f < 5; f++) r.Motion.Step(0f);
        var after = N.Bones.Select(b => b.NeckBone.localRotation).Concat(E.Eyes.Select(x => x.Transform.localRotation)).ToArray();
        var afterA = new[] { E.AngleH(0), E.AngleV(0), E.AngleH(1), E.AngleV(1), N.Bones[1].AngleH };
        Check("Step(0) holds bones and angles exactly", hold.Zip(after, LookMath.Same).All(x => x) && holdA.SequenceEqual(afterA));
        // pause in AWAY too (deviation: hold for every type)
        Fresh(r, 2, 0); r.Target.position = n1; Settle(r);
        var holdAway = N.Bones.Select(b => b.NeckBone.localRotation).ToArray();
        for (int f = 0; f < 5; f++) r.Motion.Step(0f);
        Check("Step(0) holds the AWAY pose (all types hold while paused)", N.Bones.Select(b => b.NeckBone.localRotation).Zip(holdAway, LookMath.Same).All(x => x));

        // ResetAll
        Fresh(r, 1, 1); r.Target.position = n1; Settle(r);
        r.Motion.ResetAll();
        double restDev = N.Bones.Max(b => RotationCompare.Deg(b.NeckBone.localRotation, b.Rest));
        double eyeDev = E.Eyes.Max(x => RotationCompare.Deg(x.Transform.localRotation, x.Orig));
        bool cleared = !E.HasOutput && E.AngleH(0) == 0 && N.Bones.All(b => b.AngleH == 0 && b.AngleV == 0) && N.NowAngle == Vector2.zero && N.CurrentType == 0;
        r.Motion.Step(Dt);
        float h1 = N.Bones[1].AngleH;
        Check("ResetAll: rest pose, no history; the next step restarts like the first frame (head angleH 0.463)", restDev < 1e-3f && eyeDev < 1e-3f && cleared && Mathf.Abs(h1 - 0.463f) < 0.005f,
            $"neck/head from rest {restDev:F5} deg, eyes {eyeDev:F5} deg, state cleared {cleared}, frame 1 head angleH {h1:F4}");

        // NaN guards
        var spots = new (string, Func<Vector3>)[]
        {
            ("aim", () => N.Aim.position), ("cf_J_Head", () => r.Bone("cf_J_Head").position), ("NeckRef", () => N.CalcAngle.position),
            ("rootNode", () => E.RootNode.position), ("left eye", () => E.Eyes[0].Transform.position),
        };
        bool finite = true; var nanInfo = new List<string>();
        foreach (var (name, pos) in spots)
            foreach (var (np2, ep) in new[] { (1, 1), (2, 2), (1, 4), (4, 3), (0, 0) })
            {
                Fresh(r, np2, ep);
                for (int f = 0; f < 30; f++) { r.Target.position = pos(); r.Motion.Step(Dt); }
                bool ok = N.Bones.All(b => LookMath.Finite(b.NeckBone.localRotation) && LookMath.Finite(b.AngleH) && LookMath.Finite(b.AngleV))
                          && E.Eyes.All(x => LookMath.Finite(x.Transform.localRotation)) && LookMath.Finite(E.AngleH(0)) && LookMath.Finite(E.AngleV(1)) && LookMath.Finite(E.AngleVRate);
                if (!ok) nanInfo.Add($"{name} neck {np2} eye {ep}");
                finite &= ok;
            }
        Check("no NaN with the target at aim / head / NeckRef / rootNode / an eye (5 pattern pairs each)", finite, string.Join(", ", nanInfo));

        // first frame sane
        Fresh(r, 1, 1); r.Target.position = E.RootNode.position + new Vector3(0, 0, 10);
        r.Motion.Step(Dt);
        float ff = N.Bones.Max(b => Quaternion.Angle(b.NeckBone.localRotation, b.Rest));
        float fe = E.Eyes.Max(x => Quaternion.Angle(x.Transform.localRotation, x.Orig));
        Check("first frame sane (no flip): neck/head and eyes move less than 1 deg", ff < 1f && fe < 1f, $"neck/head {ff:F4} deg, eyes {fe:F4} deg");

        // NO_LOOK freeze
        Fresh(r, 0, 1); r.Target.position = E.RootNode.position + new Vector3(3, 0, 10); Settle(r);
        var frozen = E.Eyes.Select(x => x.Transform.localRotation).ToArray();
        look.SetEyePattern(3);
        r.Target.position = E.RootNode.position + new Vector3(-5, 3, 10);
        for (int f = 0; f < 30; f++) r.Motion.Step(Dt);
        Check("eye ptn 3 (NO_LOOK) freezes the last computed pose", E.Eyes.Select(x => x.Transform.localRotation).Zip(frozen, LookMath.Same).All(x => x));
        // NO_LOOK as the very first pattern: the rest pose, not a zero quaternion
        Fresh(r, 0, 3); r.Motion.Step(Dt);
        Check("eye ptn 3 as the first pattern keeps the rest rotation", E.Eyes.All(x => RotationCompare.Deg(x.Transform.localRotation, x.Orig) < 1e-3f && LookMath.Finite(x.Transform.localRotation)));

        // Studio look target (eye pattern 4)
        Fresh(r, 0, 0);
        bool guideEvent = false; look.Targets.LookAtGuideChanged += v => guideEvent = v;
        look.SetEyePattern(4);
        bool retarget = E.Target == look.Targets.LookAtTarget && !E.FollowCamera && look.Targets.LookAtGuideVisible && guideEvent && look.LookTargetSizeInteractable;
        Settle(r);
        float lh = E.AngleH(0), rh = E.AngleH(1), lv = E.AngleV(0), rv = E.AngleV(1);
        bool cross = Mathf.Abs(lh - 13) < Tol && Mathf.Abs(rh + 13) < Tol && Mathf.Abs(lv - 10) < Tol && Mathf.Abs(rv - 10) < Tol;
        look.Targets.MoveLookAtTarget(E.RootNode.position + new Vector3(0, 0, 10));
        Settle(r);
        float lh2 = E.AngleH(0);
        look.SetEyePattern(1);
        bool back = E.FollowCamera && !look.Targets.LookAtGuideVisible;
        look.Targets.ResetLookAtTarget();
        Check("eye ptn 4 targets 'Look At Target' (default spot: down limit, cross-eyed), follows it when moved, leaving 4 returns to the camera",
            retarget && cross && Mathf.Abs(lh2 - 0.787f) < Tol && back, $"default L {V2(lh, lv)} R {V2(rh, rv)}; moved 1 m ahead L angleH {lh2:F3}; retarget {retarget}, back {back}");

        // FK hook and button mapping
        look.SetNeckPattern(1);
        look.SetFkNeckGroupActive(true); int fkOn = look.NeckPattern;
        look.SetFkNeckGroupActive(false); int fkOff = look.NeckPattern;
        look.SetNeckButton(2); int b2 = look.NeckPattern;
        look.SetNeckPattern(2); int btnAway = look.NeckButton;
        Check("FK neck group -> ptn 4 and back; neck buttons {0,1,3,4}; AWAY has no button; buttons disabled with FK neck",
            fkOn == 4 && fkOff == 1 && b2 == 3 && btnAway == -1 && !CharaLook.NeckButtonsInteractable(true, true) && CharaLook.NeckButtonsInteractable(true, false),
            $"FK on {fkOn}, off {fkOff}, button 2 -> {b2}, AWAY button {btnAway}");

        // target types
        var T = look.Targets;
        T.ChangeLookEyesTarget(3);
        bool et = E.Target == T.EyesTarget && (T.EyesTarget.localPosition - new Vector3(0, 0.5f, 2)).magnitude < 1e-5f && Mathf.Abs(Mathf.DeltaAngle(T.EyesTargetP.localEulerAngles.z, 270)) < 1e-3f
                  && (T.EyesTargetP.localPosition - new Vector3(0, 0.7f, 0)).magnitude < 1e-5f && T.Status.eyesTargetType == 3 && T.Status.eyesTargetAngle == 90 && T.Status.eyesTargetRate == 0.5f;
        var right = Vector3.Dot(T.EyesTarget.position - T.EyesTargetP.position, r.Bone("cf_J_Head_s").right);
        T.ChangeLookEyesTarget(9);
        bool e9 = Mathf.Abs(T.EyesTarget.localPosition.y) < 1e-6f;
        T.ChangeLookNeckTarget(5);
        bool nt = N.Target == T.NeckTarget && (T.NeckTarget.localPosition - new Vector3(0, 0.5f, 0.8f)).magnitude < 1e-5f && Mathf.Abs(Mathf.DeltaAngle(T.NeckTargetP.localEulerAngles.z, 180)) < 1e-3f
                  && (T.NeckTargetP.localPosition - new Vector3(0, 2.7f, 0)).magnitude < 1e-5f;
        T.ChangeLookEyesTarget(0); T.ChangeLookNeckTarget(0);
        Check("target types: eyes 3 (90 deg) / 9 (straight ahead), neck 5 (180 deg); type 0 = camera", et && e9 && nt && E.FollowCamera && N.FollowCamera,
            $"eyes type 3 offset towards the character's right {right:F3} units (spec UNVERIFIED direction: +)");

        // switches
        Fresh(r, 0, 1); r.Target.position = E.RootNode.position + new Vector3(3, 0, 10); Settle(r);
        float before = E.AngleH(0);
        EyeLookStage.GlobalEnabled = false;
        r.Target.position = E.RootNode.position + new Vector3(-3, 0, 10);
        for (int f = 0; f < 30; f++) r.Motion.Step(Dt);
        float offG = E.AngleH(0);
        EyeLookStage.GlobalEnabled = true;
        look.EyeLookEnabled = false;
        for (int f = 0; f < 30; f++) r.Motion.Step(Dt);
        float offL = E.AngleH(0);
        look.EyeLookEnabled = true;
        for (int f = 0; f < 30; f++) r.Motion.Step(Dt);
        Check("eye switches: global and per-character off freeze the eyes, on resumes", offG == before && offL == before && E.AngleH(0) < 0,
            $"{before:F3} -> off {offG:F3}/{offL:F3} -> on {E.AngleH(0):F3}");

        // persistence round trip
        Fresh(r, 1, 1); r.Target.position = n1; Settle(r);
        var nq = new Quaternion[2]; var eq = new Quaternion[2];
        int sp = look.SaveNeck(nq); look.SaveEyes(eq);
        r.Motion.ResetAll();
        look.LoadNeck(4, nq); look.LoadEyes(eq);
        look.SetEyePattern(3);
        for (int f = 0; f < 60; f++) r.Motion.Step(Dt);
        double pd = N.Bones.Select((b, i) => RotationCompare.Deg(b.NeckBone.localRotation, nq[i])).Concat(E.Eyes.Select((x, i) => RotationCompare.Deg(x.Transform.localRotation, eq[i]))).Max();
        Check("scene state: neck FIX + eye NO_LOOK restore the saved pose", pd < 0.01f, $"saved ptn {sp}, max deviation after 1 s {pd:F5} deg");
    }

    // ================================================================ card builds

    static void CardTests(ContentCatalog gd, string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        Section("card: " + name);
        if (!File.Exists(path)) { Check("card exists", false, path); return; }
        var card = Card.Load(path);
        var asm = new CharaAssembler(gd);
        var chara = asm.BuildFromDocuments(card);
        try
        {
            var ctx = asm.Context;
            var lookLine = asm.Log.FirstOrDefault(l => l.StartsWith("look: neck"));
            rep.AppendLine("   " + (lookLine ?? "(no look line)"));
            foreach (var l in asm.Log.Where(l => l.StartsWith("motion:") || l.StartsWith("look: eye references"))) rep.AppendLine("   " + l);
            var r = FromBuild(chara, ctx);
            string sex = asm.IsMale ? "male" : "female";
            Check($"{sex} card: stages attached, one look line, no motion errors", r.Neck != null && r.Eyes != null && r.Look != null && lookLine != null && r.Motion.Errors == 0,
                $"errors {r.Motion.Errors}");
            if (r.Neck == null || r.Eyes == null || r.Look == null) return;
            DataChecks(r, sex, geometry: false);
            Check($"{sex} card: Studio defaults (neck ptn 3, eye ptn 0, both targets camera, rate 1)",
                r.Neck.PtnNo == 3 && r.Eyes.PtnNo == 0 && r.Neck.FollowCamera && r.Eyes.FollowCamera && r.Neck.Rate == 1 && r.Look.Status.neckLookPtn == 3);

            // default patterns, first frame
            var R = r.Eyes.RootNode.position;
            r.Target.position = R + r.Eyes.RootNode.forward * 10f;
            r.Motion.Step(Dt);
            double neckMove = r.Neck.Bones.Max(b => RotationCompare.Deg(b.NeckBone.localRotation, b.Rest));
            float eyeMove = r.Eyes.Eyes.Max(x => Quaternion.Angle(x.Transform.localRotation, x.Orig));
            Check($"{sex} card: first frame with the defaults (ANIMATION neck unchanged, FORWARD eyes small)", neckMove < 1e-3f && eyeMove < 1f, $"neck {neckMove:F5} deg, eyes {eyeMove:F4} deg");

            // bending rule on the shaped geometry: eyes ptn 1, neck 0, target 30 cm right / 1 m ahead of rootNode
            Fresh(r, 0, 1);
            r.Target.position = R + r.Eyes.RootNode.rotation * new Vector3(3, 0, 10);
            int frames = Settle(r);
            float err = 0; var st = r.Eyes.States[1]; var parts = new List<string>();
            for (int i = 0; i < 2; i++)
            {
                var eye = r.Eyes.Eyes[i];
                var d = Quaternion.Inverse(eye.Transform.parent.rotation) * (r.Eyes.TargetPos - eye.Transform.position).normalized;
                float rawH = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, rawV = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                float Bend(float a) => Mathf.Sign(a) * Mathf.Max(0.4f * Mathf.Abs(a), Mathf.Abs(a) - 10f);
                float h = eye.LR == 1 ? Mathf.Clamp(Bend(rawH), -st.Max, -st.Min) : Mathf.Clamp(Bend(rawH), st.Min, st.Max);
                float v = Mathf.Clamp(Bend(rawV), st.Up, st.Down);
                err = Mathf.Max(err, Mathf.Max(Mathf.Abs(h - eye.AngleH), Mathf.Abs(v - eye.AngleV)));
                parts.Add($"{(i == 0 ? "L" : "R")} raw {V2(rawH, rawV)} -> {V2(eye.AngleH, eye.AngleV)}");
            }
            Check($"{sex} card: eye bending rule on shaped geometry (raw from the eye parent frame)", err < Tol, string.Join(", ", parts) + $"; max err {err:F4} deg, {frames} frames");

            // neck on the card: N1-like target relative to the head; converges with the aim point on the line
            Fresh(r, 1, 1);
            r.Target.position = r.Neck.Aim.position + new Vector3(5, -0.135f, 20.23f);
            frames = Settle(r);
            var aimFwd = r.Neck.Aim.forward; var toT = (r.Target.position - r.Neck.Aim.position).normalized;
            float aimErr = Vector3.Angle(aimFwd, toT);
            Check($"{sex} card: neck TARGET converges with the aim forward on the target", aimErr < 0.05f,
                $"head {V2(r.Neck.Bones[1].AngleH, r.Neck.Bones[1].AngleV)}, neck {V2(r.Neck.Bones[0].AngleH, r.Neck.Bones[0].AngleV)}, aim-to-target {aimErr:F4} deg, {frames} frames, motion errors {r.Motion.Errors}");
            EyeMeshCheck(r, sex);
        }
        finally { Object.DestroyImmediate(chara); }
    }

    // The eye meshes must follow cf_J_look_L/R: bones of the eye renderers and the baked vertices for a gaze far
    // left vs far right (neck pattern 0, eye pattern 1).
    static void EyeMeshCheck(Rig r, string sex)
    {
        var lookL = r.Eyes.Eyes[0].Transform; var lookR = r.Eyes.Eyes[1].Transform;
        var eyeRenderers = r.Root.GetComponentsInChildren<Renderer>(true).Where(x => x.name.StartsWith("o_eye")).ToArray();
        foreach (var rend in eyeRenderers)
        {
            if (rend is SkinnedMeshRenderer smr)
            {
                var bones = smr.bones;
                int underLook = bones.Count(b => b != null && (b.IsChildOf(lookL) || b.IsChildOf(lookR)));
                int inHeadBone = bones.Count(b => b != null && b.IsChildOf(r.Ctx.HeadBone.transform));
                rep.AppendLine($"   {sex} {rend.name}: skinned, {bones.Length} bones ({inHeadBone} in p_cf_head_bone, {underLook} under cf_J_look_*), root {smr.rootBone?.name}, " +
                               $"bones {string.Join(",", bones.Take(8).Select(b => b ? b.name + (b.IsChildOf(r.Ctx.HeadBone.transform) ? "" : "(foreign)") : "null"))}{(bones.Length > 8 ? ",..." : "")}, enabled {rend.enabled}, active {rend.gameObject.activeInHierarchy}");
            }
            else rep.AppendLine($"   {sex} {rend.name}: {rend.GetType().Name} under {rend.transform.parent?.name}");
        }
        var eyeL = eyeRenderers.OfType<SkinnedMeshRenderer>().FirstOrDefault(x => x.name == "o_eyebase_L");
        if (eyeL == null) { Check($"{sex} card: o_eyebase_L is a skinned renderer", false, "not found"); return; }
        Vector3[] Bake()
        {
            var m = new Mesh();
            eyeL.BakeMesh(m, true);
            var v = m.vertices;
            for (int i = 0; i < v.Length; i++) v[i] = eyeL.transform.TransformPoint(v[i]);
            Object.DestroyImmediate(m);
            return v;
        }
        var R = r.Eyes.RootNode.position;
        Fresh(r, 0, 1); r.Target.position = R + new Vector3(-20, 0, 20); Settle(r);
        float hl = r.Eyes.AngleH(0); var qa = lookL.localRotation; var va = Bake();
        Fresh(r, 0, 1); r.Target.position = R + new Vector3(20, 0, 20); Settle(r);
        float hr = r.Eyes.AngleH(0); var qb = lookL.localRotation; var vb = Bake();
        float maxMove = 0; for (int i = 0; i < va.Length && i < vb.Length; i++) maxMove = Mathf.Max(maxMove, (va[i] - vb[i]).magnitude);
        Check($"{sex} card: o_eyebase_L vertices move with cf_J_look_L (gaze far left vs far right)", maxMove > 0.01f,
            $"angleH {hl:F2} / {hr:F2}, cf_J_look_L rotation differs by {Quaternion.Angle(qa, qb):F2} deg, max vertex move {maxMove:F4} units over {va.Length} vertices");
    }

    // ================================================================ renders

    static void RenderCases(ContentCatalog gd, string path)
    {
        Section("render (BR-Chan, URP)");
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Check("render needs a graphics device", false, "run without -nographics"); return; }
        ShaderUtil.allowAsyncCompilation = false;
        var asm = new CharaAssembler(gd);
        var chara = asm.BuildFromDocuments(path);
        try
        {
            var r = FromBuild(chara, asm.Context);
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(HS2Next.App.AppPipeline.MeasureAssetPath);
            HS2Next.ShaderMap.ApplyUrp(chara);
            var R = r.Eyes.RootNode.position;
            // Eye cases use Studio's movable look target (eye pattern 4) placed far away; front uses the
            // camera target (pattern 1); neck cases use neck + eye pattern 1 with the camera target.
            var cases = new (string name, int neck, int eye, Vector3 off)[]
            {
                ("front", 0, 1, new Vector3(0, 0, 20)),
                ("eyes_left", 0, 4, new Vector3(-20, 0, 20)), ("eyes_right", 0, 4, new Vector3(20, 0, 20)),
                ("eyes_up", 0, 4, new Vector3(0, 12, 20)), ("eyes_down", 0, 4, new Vector3(0, -12, 20)),
                ("neck_left", 1, 1, new Vector3(-25, 0, 15)), ("neck_right", 1, 1, new Vector3(25, 0, 15)),
                ("neck_up", 1, 1, new Vector3(0, 15, 15)), ("neck_down", 1, 1, new Vector3(0, -15, 15)),
            };
            // Blink off so that the captures show open eyes (the expression stage blinks on its own timer).
            var expr = HS2Next.Motion.Face.CharaExpression.Of(chara);
            if (expr != null) expr.Blink = false;
            rep.AppendLine($"   blink {(expr != null ? "off" : "(no expression component)")}");
            var smrs = chara.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            // Pass 0 reproduces the earlier renders (Unity's per-frame skinning cache on); pass 1 is the delivered
            // set with forceMatrixRecalculationPerRender, since a batch editor never advances the frame and
            // several Camera.Render calls in one frame otherwise reuse stale skinning for unchanged root bones.
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var s in smrs) s.forceMatrixRecalculationPerRender = pass == 1;
                var bytes = new Dictionary<string, byte[]>();
                foreach (var c in cases)
                {
                    Fresh(r, c.neck, c.eye == 4 ? 0 : c.eye);
                    r.Target.position = R + c.off;
                    if (c.eye == 4) { r.Look.SetEyePattern(4); r.Look.Targets.MoveLookAtTarget(R + c.off); }
                    int frames = Settle(r, 600);
                    float lh = r.Eyes.AngleH(0), lv = r.Eyes.AngleV(0);
                    var eyeQ = r.Eyes.Eyes[0].Transform.localRotation;
                    string prefix = Path.Combine(OutDir, "look_" + c.name + (pass == 0 ? "_cached" : ""));
                    CharaRenderTest.RenderViews(chara, prefix, asset, "face");
                    string file = prefix + ".png";
                    if (File.Exists(file)) File.Delete(file);
                    File.Move(prefix + "_face.png", file);
                    bytes[c.name] = File.ReadAllBytes(file);
                    if (pass == 0) { File.Delete(file); continue; }
                    rep.AppendLine($"   look_{c.name}.png: neck {c.neck} eye {c.eye}, target R+{c.off}, head {V2(r.Neck.Bones[1].AngleH, r.Neck.Bones[1].AngleV)} " +
                                   $"neck {V2(r.Neck.Bones[0].AngleH, r.Neck.Bones[0].AngleV)} eyes L {V2(lh, lv)} R {V2(r.Eyes.AngleH(1), r.Eyes.AngleV(1))}, " +
                                   $"cf_J_look_L euler {eyeQ.eulerAngles:F2} at capture, {frames} frames");
                    if (c.eye == 4)
                        Check($"render {c.name}: eye angles non-zero at capture", Mathf.Abs(lh) > 5f || Mathf.Abs(lv) > 5f, $"L {V2(lh, lv)}");
                }
                var front = bytes["front"];
                var same = cases.Where(c => c.name != "front" && bytes[c.name].SequenceEqual(front)).Select(c => c.name).ToList();
                if (pass == 0) rep.AppendLine($"   pass 0 (skinning cache on): renders byte-identical to front: {(same.Count == 0 ? "none" : string.Join(", ", same))}");
                else Check("renders differ from the front render (no stale skinning)", same.Count == 0, same.Count == 0 ? "all 8 differ" : "identical: " + string.Join(", ", same));
            }
            Check("renders written", cases.All(c => File.Exists(Path.Combine(OutDir, "look_" + c.name + ".png"))), $"{cases.Length} files in Spike/out/motion");
        }
        finally
        {
            Object.DestroyImmediate(chara);
            GraphicsSettings.defaultRenderPipeline = null; QualitySettings.renderPipeline = null;
        }
    }
}
