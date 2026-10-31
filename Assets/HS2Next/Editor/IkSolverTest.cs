// IK solver and stage tests (S1–S16; package A of Phase 3.4).
// Run: -executeMethod IkSolverTest.Run [-only S1,S5] [-measure] (headless, -nographics, edit mode, ManualStepping).
// Synthetic chains (a skeleton with the census rest lengths: arm 2.65 / 2.28, leg 4.20 / 4.6796) and BR-Chan (the SFW
// visual female of Tools/targets.json) built through SceneOps.AddCharaFromCard into a session + runtime. The IK stage is
// installed by the test on the character's CharaMotion (the scene driver CharaPoseDrivers.Ik is switched off while the
// test runs, so the stage under test is the only one); the FK driver is the default one (FK through the model).
// -measure adds the measurements that belong to package A (info only, no gate): U4 / U6 (the IK-mode pose
// files of the library solved on BR-Chan), U8 (the FK compare poses' Body values as the base of IK legs) and U9 (solved
// bones without curves in the vanilla non-H clips; bindings read as data, nothing is played or rendered).
// Report: Spike/out/pose/ik_solver_report.txt, first line "SUMMARY: N PASS, M FAIL"; EditorApplication.Exit(1) on any FAIL.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HS2Next.Content;
using HS2Next.Content.Studio;
using HS2Next.Import.Pose;
using HS2Next.Motion;
using HS2Next.Motion.Fk;
using HS2Next.Motion.Ik;
using HS2Next.Save;
using HS2Next.Scenes;
using HS2Next.Scenes.Runtime;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static partial class IkSolverTest
{
    const float Dt = 1f / 60f;
    static string OutDir => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "pose");
    static StringBuilder rep;
    static int passes, fails, warns;
    static string current = "";
    static readonly Dictionary<string, (int pass, int fail)> perTest = new Dictionary<string, (int, int)>();
    static string[] only;
    static RuntimeServices services;
    static string femaleCard;
    static readonly AnimeRef Idle = new AnimeRef(ContentIds.Vanilla, 19, 0, 0);
    static readonly List<Vector3> s1Ends = new List<Vector3>();

    static string Arg(string name)
    {
        var a = TestArgs.Get();
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }
    static bool Has(string name) => Array.IndexOf(TestArgs.Get(), name) >= 0;

    static void Section(string id, string title) { current = id; rep.AppendLine(); rep.AppendLine($"== {id} {title}"); }
    static void Info(string s) => rep.AppendLine("   " + s);
    static void Check(string name, bool ok, string detail = "")
    {
        rep.AppendLine($"{(ok ? "PASS" : "FAIL")}  [{current}] {name}{(string.IsNullOrEmpty(detail) ? "" : "  " + detail)}");
        if (ok) passes++; else fails++;
        perTest.TryGetValue(current, out var v);
        perTest[current] = ok ? (v.pass + 1, v.fail) : (v.pass, v.fail + 1);
    }
    static void Warn(string name, string detail)
    {
        rep.AppendLine($"WARN  [{current}] {name}  {detail}");
        warns++;
    }

    static void Guard(string id, Action a)
    {
        if (only != null && !only.Contains(id)) return;
        current = id;
        bool rl = IkOptions.RotationLimits; float li = IkOptions.LimitMinInterior; var lo = IkOptions.LimbOrientation;
        try { a(); }
        catch (Exception e) { Check("exception", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        finally { IkOptions.RotationLimits = rl; IkOptions.LimitMinInterior = li; IkOptions.LimbOrientation = lo; }
    }

    public static void Run()
    {
        rep = new StringBuilder(); passes = fails = warns = 0; perTest.Clear(); s1Ends.Clear();
        var o = Arg("-only");
        only = o == null ? null : o.Split(',', '+').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        Directory.CreateDirectory(OutDir);
        var sw = Stopwatch.StartNew();
        var oldIk = CharaPoseDrivers.Ik;
        var oldFk = CharaPoseDrivers.Fk;
        var head = new StringBuilder();
        try
        {
            CharaPoseDrivers.Ik = null;
            if (!(CharaPoseDrivers.Fk is FkDriver)) CharaPoseDrivers.Fk = FkDriver.Instance;
            rep.AppendLine($"IK solver test (Unity {Application.unityVersion}); IK scene driver {(oldIk == null ? "not registered" : "registered (switched off for the test)")}");
            Guard("S1", S1);
            Guard("S2", S2);
            Guard("S3", S3);
            Guard("S4", S4);
            Guard("S5", S5);
            Guard("S6", S6);
            Guard("S7", S7);
            Guard("S12", S12);
            Guard("S15", S15);
            Guard("S17", S17);
            Guard("S18", S18);
            bool needChara = only == null || only.Any(x => x == "S8" || x == "S9" || x == "S10" || x == "S11" || x == "S13" || x == "S14" || x == "S16") || Has("-measure") || Has("-fix");
            if (needChara)
            {
                services = SceneRuntimeTest.NewServices(head);
                femaleCard = TestTargets.Abs(TestTargets.Load().visual.female);
                rep.Append(head);
                Guard("S8", S8);
                Guard("S9", S9);
                Guard("S10", S10);
                Guard("S11", S11);
                Guard("S13", S13);
                Guard("S14", S14);
                Guard("S16", S16);
                if (Has("-measure"))
                {
                    Guard("U4", U4U6);
                    Guard("U8", U8);
                    Guard("U9", U9);
                }
                if (Has("-fix"))
                {
                    CharaPoseDrivers.Ik = oldIk;
                    try { Guard("R8", R8); Guard("R2", R2); Guard("RC", RC); }
                    finally { CharaPoseDrivers.Ik = null; }
                }
            }
        }
        catch (Exception e) { current = "run"; Check("run (exception)", false, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
        finally
        {
            CharaPoseDrivers.Ik = oldIk;
            CharaPoseDrivers.Fk = oldFk;
            if (probe) Object.DestroyImmediate(probe.gameObject);
        }
        var sum = new StringBuilder();
        sum.AppendLine($"SUMMARY: {passes} PASS, {fails} FAIL{(warns > 0 ? $", {warns} WARN" : "")} ({sw.Elapsed.TotalSeconds:F0} s)");
        foreach (var kv in perTest.OrderBy(k => k.Key.Length).ThenBy(k => k.Key, StringComparer.Ordinal)) sum.AppendLine($"  {kv.Key}: {kv.Value.pass} pass, {kv.Value.fail} fail");
        rep.Insert(0, sum.ToString());
        File.WriteAllText(Path.Combine(OutDir, "ik_solver_report.txt"), rep.ToString());
        services?.Catalog?.UnloadAll();
        Debug.Log($"IK SOLVER TEST DONE: {passes} PASS, {fails} FAIL, {warns} WARN");
        if (fails > 0) TestExit.Exit(1);
    }

    // ================================================================ helpers

    static bool Bits(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
    static bool Bits(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
    static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    static bool Finite(Quaternion q) => !(float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z) || float.IsNaN(q.w));
    static float Angle(Quaternion a, Quaternion b)
    {
        var d = Quaternion.Inverse(a) * b;
        double v = Math.Sqrt((double)d.x * d.x + (double)d.y * d.y + (double)d.z * d.z);
        return (float)(2 * Math.Atan2(v, Math.Abs((double)d.w)) * 180 / Math.PI);
    }
    static float AngleV(Vector3 a, Vector3 b)
    {
        double dot = (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z;
        var c = Vector3.Cross(a, b);
        double cr = Math.Sqrt((double)c.x * c.x + (double)c.y * c.y + (double)c.z * c.z);
        return (float)(Math.Atan2(cr, dot) * 180 / Math.PI);
    }
    static string F(Vector3 v) => $"({v.x:G6}, {v.y:G6}, {v.z:G6})";
    static string G(double v) => v.ToString("G4", System.Globalization.CultureInfo.InvariantCulture);
    static Transform probe;
    /// <summary>What a Transform stores for <paramref name="q"/> (Unity normalises on set): the reference for "bit for bit".</summary>
    static Quaternion Stored(Quaternion q)
    {
        if (!probe) probe = new GameObject("ik_test_probe") { hideFlags = HideFlags.HideAndDontSave }.transform;
        probe.localRotation = q;
        return probe.localRotation;
    }
    static Vector3 RandUnit(System.Random r)
    {
        while (true)
        {
            var v = new Vector3((float)(r.NextDouble() * 2 - 1), (float)(r.NextDouble() * 2 - 1), (float)(r.NextDouble() * 2 - 1));
            float m = v.magnitude;
            if (m > 0.05f && m <= 1f) return v / m;
        }
    }
    static Vector3 Reject(Vector3 v, Vector3 u) => v - Vector3.Dot(v, u) * u;
    static double Pct(List<float> l, double p)
    {
        if (l.Count == 0) return double.NaN;
        var s = l.OrderBy(x => x).ToList();
        return s[Math.Min(s.Count - 1, (int)Math.Floor(p * (s.Count - 1) + 0.5))];
    }
    static string Dist(List<float> l) => l.Count == 0 ? "n 0" : $"n {l.Count}, median {G(Pct(l, 0.5))}, p95 {G(Pct(l, 0.95))}, max {G(l.Max())}";

    /// <summary>Local position / rotation / scale of every transform under a root.</summary>
    sealed class Snap
    {
        readonly Transform[] ts; readonly Vector3[] p, s; readonly Quaternion[] q;
        public Snap(Transform root)
        {
            ts = root.GetComponentsInChildren<Transform>(true);
            p = new Vector3[ts.Length]; s = new Vector3[ts.Length]; q = new Quaternion[ts.Length];
            for (int i = 0; i < ts.Length; i++) { p[i] = ts[i].localPosition; q[i] = ts[i].localRotation; s[i] = ts[i].localScale; }
        }
        public void Restore()
        {
            for (int i = 0; i < ts.Length; i++)
            {
                if (!ts[i]) continue;
                if (!Bits(ts[i].localPosition, p[i])) ts[i].localPosition = p[i];
                if (!Bits(ts[i].localRotation, q[i])) ts[i].localRotation = q[i];
                if (!Bits(ts[i].localScale, s[i])) ts[i].localScale = s[i];
            }
        }
        /// <summary>Names of the transforms that differ (bit level), optionally skipping some.</summary>
        public List<string> Diff(Func<Transform, bool> skip = null)
        {
            var l = new List<string>();
            for (int i = 0; i < ts.Length; i++)
            {
                var t = ts[i];
                if (!t || (skip != null && skip(t))) continue;
                if (!Bits(t.localPosition, p[i]) || !Bits(t.localRotation, q[i]) || !Bits(t.localScale, s[i])) l.Add(t.name);
            }
            return l;
        }
        public int Count => ts.Length;
    }
    static string Few(List<string> l) => l.Count == 0 ? "" : $"{l.Count}: " + string.Join(", ", l.Take(8)) + (l.Count > 8 ? ", ..." : "");

    // ---------------------------------------------------------------- synthetic skeleton

    /// <summary>A synthetic skeleton with the solved bones (census rest lengths, slight rest bends: elbows back, knees forward),
    /// a finger under each hand and a toe under each foot.</summary>
    sealed class Syn : IDisposable
    {
        public readonly GameObject Go;
        public readonly Transform Root;
        public readonly Dictionary<string, Transform> Map = new Dictionary<string, Transform>(StringComparer.Ordinal);
        public IkBones B;
        public Snap Rest;

        Transform Add(string name, Transform parent, Vector3 lp, Vector3 euler)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = lp;
            t.localRotation = Quaternion.Euler(euler);
            Map[name] = t;
            return t;
        }

        public Syn(Vector3 rootPos = default, Vector3 rootEuler = default, float rootScale = 1f)
        {
            Go = new GameObject("ik_syn") { hideFlags = HideFlags.DontSave };
            Root = Go.transform;
            Root.localPosition = rootPos; Root.localRotation = Quaternion.Euler(rootEuler); Root.localScale = Vector3.one * rootScale;
            var height = Add("cf_N_height", Root, Vector3.zero, Vector3.zero);
            var hips = Add("cf_J_Hips", height, new Vector3(0, 11.435f, 0), Vector3.zero);
            var s1 = Add("cf_J_Spine01", hips, new Vector3(0, 0.1f, 0), Vector3.zero);
            var s2 = Add("cf_J_Spine02", s1, new Vector3(0, 1.2f, 0.05f), new Vector3(-3, 0, 0));
            var s3 = Add("cf_J_Spine03", s2, new Vector3(0, 1.3f, -0.05f), new Vector3(2, 0, 0));
            Add("cf_J_Neck", s3, new Vector3(0, 1.6f, 0), Vector3.zero);
            foreach (var (side, sx) in new[] { ("L", -1f), ("R", 1f) })
            {
                var sh = Add("cf_J_Shoulder_" + side, s3, new Vector3(0.3f * sx, 1.2f, 0), Vector3.zero);
                var up = Add("cf_J_ArmUp00_" + side, sh, new Vector3(1.0f * sx, 0, 0), Vector3.zero);
                var low = Add("cf_J_ArmLow01_" + side, up, new Vector3(2.65f * sx, 0, 0), new Vector3(0, -10f * sx, 0));
                var hand = Add("cf_J_Hand_" + side, low, new Vector3(2.28f * sx, 0, 0), new Vector3(0, 3f * sx, 0));
                Add("cf_J_Hand_Index01_" + side, hand, new Vector3(0.6f * sx, 0, 0.15f), new Vector3(5, 10, 20));
                var kosi = Add("cf_J_Kosi01_" + side, hips, new Vector3(0, -0.5f, 0), Vector3.zero);
                var leg = Add("cf_J_LegUp00_" + side, kosi, new Vector3(0.88f * sx, -1.1f, -0.1f), Vector3.zero);
                var knee = Add("cf_J_LegLow01_" + side, leg, new Vector3(0, -4.2f, 0), new Vector3(5f, 0, 0));
                var roll = Add("cf_J_LegLowRoll_" + side, knee, new Vector3(0, -2.3296f, 0), Vector3.zero);
                var foot = Add("cf_J_Foot01_" + side, roll, new Vector3(0, -2.35f, 0), new Vector3(-2, 0, 0));
                Add("cf_J_Toes01_" + side, foot, new Vector3(0, -0.4f, 0.9f), new Vector3(10, 0, 0));
            }
            B = IkBones.Discover(Map, Root);
            Rest = new Snap(Root);
        }

        public void Dispose() { if (Go) Object.DestroyImmediate(Go); }
    }

    /// <summary>Target values from the current pose of a skeleton (root space), with optional offsets per target.</summary>
    static IkTargetValue[] Current(IkBones b)
    {
        var t = new IkTargetValue[IkGroupInfo.TargetCount];
        b.ReadTargets(t);
        return t;
    }

    static bool[] Groups(params IkGroup[] g)
    {
        var a = new bool[IkGroupInfo.Count];
        foreach (var x in g) a[(int)x] = true;
        return a;
    }
    static readonly IkGroup[] AllGroups = { IkGroup.Body, IkGroup.RightLeg, IkGroup.LeftLeg, IkGroup.RightArm, IkGroup.LeftArm };

    static void Solve(IkBones b, Transform root, IkTargetValue[] t, bool[] run, IkSolveInfo info)
    {
        IkSolver.Solve(b, root, t, run, info, new bool[IkBones.SlotCount]);
    }

    // ================================================================ S1 two-bone exact hit

    static void LimbRandom(IkLimb l, Snap rest, int seed, int n, Action<int, Vector3, Vector3, Vector3, float, float> each)
    {
        var r = new System.Random(seed);
        rest.Restore();
        Vector3 a = l.Root.position;
        float l1 = (l.Mid.position - a).magnitude, l2 = (l.End.position - l.Mid.position).magnitude, sum = l1 + l2;
        float dMin = Mathf.Abs(l1 - l2) + 1e-4f * sum, dMax = IkSolver.ReachClamp * sum;
        for (int i = 0; i < n; i++)
        {
            rest.Restore();
            var u = RandUnit(r);
            float d = Mathf.Lerp(dMin * 1.05f, dMax * 0.98f, (float)r.NextDouble());
            var g = a + d * u;
            var p = a + RandUnit(r) * sum;
            if (Reject(p - a, u).magnitude < 0.05f * sum) p += Vector3.Cross(u, Vector3.up).normalized * sum * 0.5f;
            each(i, a, g, p, l1, l2);
        }
    }

    static void S1()
    {
        Section("S1", "two-bone exact hit (1,000 seeded reachable goals per limb)");
        using (var syn = new Syn())
        {
            foreach (var g in new[] { IkGroup.LeftArm, IkGroup.LeftLeg })
            {
                var l = syn.B.Limb(g);
                float maxRes = 0, maxLen = 0; int clamped = 0, nan = 0;
                LimbRandom(l, syn.Rest, 1234 + (int)g, 1000, (i, a, goal, pole, l1, l2) =>
                {
                    IkSolver.SolveLimb(l, goal, pole, out bool c);
                    if (c) clamped++;
                    var e = l.End.position; var m = l.Mid.position;
                    if (!Finite(e) || !Finite(m)) { nan++; return; }
                    maxRes = Mathf.Max(maxRes, (e - goal).magnitude);
                    maxLen = Mathf.Max(maxLen, Mathf.Max(Mathf.Abs((m - a).magnitude - l1), Mathf.Abs((e - m).magnitude - l2)));
                    s1Ends.Add(e);
                });
                Check($"{g}: end residual ≤ 1e-4", maxRes <= 1e-4f && nan == 0, $"max {G(maxRes)}{(nan > 0 ? ", NaN " + nan : "")}");
                Check($"{g}: L1 / L2 preserved ≤ 1e-5", maxLen <= 1e-5f, $"max {G(maxLen)}");
                Check($"{g}: no goal reported clamped", clamped == 0, clamped.ToString());
            }
        }
    }

    // ================================================================ S2 reach clamp

    static void S2()
    {
        Section("S2", "reach clamp (goals at 1.0, 1.5, 3 × (L1 + L2))");
        using (var syn = new Syn())
        {
            var r = new System.Random(77);
            foreach (var g in new[] { IkGroup.LeftArm, IkGroup.RightLeg })
            {
                var l = syn.B.Limb(g);
                foreach (var k in new[] { 1.0f, 1.5f, 3f })
                {
                    float maxOff = 0, minRes = float.MaxValue, maxRes = 0; int notClamped = 0;
                    for (int i = 0; i < 20; i++)
                    {
                        syn.Rest.Restore();
                        Vector3 a = l.Root.position;
                        float sum = (l.Mid.position - a).magnitude + (l.End.position - l.Mid.position).magnitude;
                        var u = RandUnit(r);
                        var goal = a + k * sum * u;
                        IkSolver.SolveLimb(l, goal, a + RandUnit(r) * sum, out bool c);
                        if (!c) notClamped++;
                        var expect = a + IkSolver.ReachClamp * sum * u;
                        maxOff = Mathf.Max(maxOff, (l.End.position - expect).magnitude);
                        float res = (l.End.position - goal).magnitude;
                        minRes = Mathf.Min(minRes, res); maxRes = Mathf.Max(maxRes, res);
                    }
                    Check($"{g} at {k} × (L1 + L2): end on the line at 0.99999 (L1 + L2) ≤ 1e-4, clamped", maxOff <= 1e-4f && notClamped == 0, $"max off {G(maxOff)}, not clamped {notClamped}, residual {G(minRes)}..{G(maxRes)}");
                }
            }
            // through the full solve: residual and clamp reported in the solve info
            syn.Rest.Restore();
            var t = Current(syn.B);
            var info = new IkSolveInfo();
            var arm = syn.B.Limb(IkGroup.RightArm);
            float s = (arm.Mid.position - arm.Root.position).magnitude + (arm.End.position - arm.Mid.position).magnitude;
            var gw = arm.Root.position + new Vector3(2f * s, 0, 0);
            t[(int)IkTargetId.RightHand].Pos = syn.Root.InverseTransformPoint(gw);
            Solve(syn.B, syn.Root, t, Groups(IkGroup.RightArm), info);
            float expRes = (gw - arm.Root.position).magnitude - IkSolver.ReachClamp * s;
            Check("solve info: RightHand clamped, residual = |G − A| − 0.99999 (L1 + L2) (≤ 1e-4)", info.Clamped[(int)IkTargetId.RightHand] && Mathf.Abs(info.Residual[(int)IkTargetId.RightHand] - expRes) <= 1e-4f,
                $"clamped {info.Clamped[(int)IkTargetId.RightHand]}, residual {G(info.Residual[(int)IkTargetId.RightHand])} expected {G(expRes)}");
        }
    }

    // ================================================================ S3 minimum clamp

    static void S3()
    {
        Section("S3", "minimum clamp (goal at the root)");
        using (var syn = new Syn())
        {
            foreach (var g in new[] { IkGroup.LeftArm, IkGroup.LeftLeg })
            {
                var l = syn.B.Limb(g);
                syn.Rest.Restore();
                Vector3 a = l.Root.position;
                float l1 = (l.Mid.position - a).magnitude, l2 = (l.End.position - l.Mid.position).magnitude;
                float dMin = Mathf.Abs(l1 - l2) + 1e-4f * (l1 + l2);
                IkSolver.SolveLimb(l, a, l.Mid.position + Vector3.forward, out bool c);
                bool finite = Finite(l.Root.rotation) && Finite(l.Mid.rotation) && Finite(l.End.position);
                float d = (l.End.position - a).magnitude;
                Check($"{g}: no NaN, |end − root| = d_min (≤ 1e-4), clamped", finite && Mathf.Abs(d - dMin) <= 1e-4f && c, $"d {G(d)}, d_min {G(dMin)}, clamped {c}");
            }
        }
    }

    // ================================================================ S4 pole side

    static void S4()
    {
        Section("S4", "pole side (1,000 random poles per limb)");
        using (var syn = new Syn())
        {
            foreach (var g in new[] { IkGroup.RightArm, IkGroup.RightLeg })
            {
                var l = syn.B.Limb(g);
                int wrong = 0; float minH = float.MaxValue;
                LimbRandom(l, syn.Rest, 4321 + (int)g, 1000, (i, a, goal, pole, l1, l2) =>
                {
                    IkSolver.SolveLimb(l, goal, pole, out _);
                    var u = (goal - a).normalized;
                    var mside = Reject(l.Mid.position - a, u);
                    var pside = Reject(pole - a, u);
                    minH = Mathf.Min(minH, mside.magnitude);
                    if (Vector3.Dot(mside, pside) <= 0) wrong++;
                });
                Check($"{g}: mid joint on the pole's side of the root–goal line", wrong == 0, $"wrong {wrong}; min distance from the line {G(minH)}");
            }
        }
    }

    // ================================================================ S5 pole fallbacks

    static void S5()
    {
        Section("S5", "pole fallbacks (clip plane, rest bend direction)");
        using (var syn = new Syn())
        {
            foreach (var g in new[] { IkGroup.LeftArm, IkGroup.LeftLeg })
            {
                var l = syn.B.Limb(g);
                // pole on the root–goal line → the clip's bend plane
                syn.Rest.Restore();
                Vector3 a = l.Root.position, m0 = l.Mid.position, e0 = l.End.position;
                float sum = (m0 - a).magnitude + (e0 - m0).magnitude;
                var u = ((e0 - a).normalized + 0.3f * Vector3.Cross((e0 - a).normalized, Vector3.right).normalized).normalized;
                var goal = a + 0.7f * sum * u;
                var src = IkSolver.SolveLimb(l, goal, a + 0.4f * sum * u, out _);
                float side = Vector3.Dot(Reject(l.Mid.position - a, u), Reject(m0 - a, u));
                Check($"{g}: pole on the line → clip plane, mid on the clip's side", src == IkPoleSource.Clip && side > 0, $"source {src}, side {G(side)}");
                // straight clip limb on the line → rest bend direction
                syn.Rest.Restore();
                l.Mid.localRotation = Quaternion.identity;
                l.End.localRotation = Quaternion.identity;
                a = l.Root.position; e0 = l.End.position;
                u = (e0 - a).normalized;
                goal = a + 0.7f * sum * u;
                src = IkSolver.SolveLimb(l, goal, a + 0.2f * sum * u, out _);
                var restDir = l.Root.parent.rotation * l.RestBend;
                side = Vector3.Dot(Reject(l.Mid.position - a, u), restDir);
                Check($"{g}: straight clip limb and pole on the line → rest bend direction, mid on its side", src == IkPoleSource.Rest && side > 0 && l.RestBend.sqrMagnitude > 0.5f,
                    $"source {src}, side {G(side)}, rest bend {F(l.RestBend)}");
                // reported through the solve info
                syn.Rest.Restore();
                var t = Current(syn.B);
                t[(int)IkSolver.BendTargetOf(g)].Pos = syn.Root.InverseTransformPoint(l.Root.position);
                var info = new IkSolveInfo();
                Solve(syn.B, syn.Root, t, Groups(g), info);
                Check($"{g}: solve info reports the fallback (bend goal at the root → clip)", info.Pole[(int)g] == IkPoleSource.Clip, info.Pole[(int)g].ToString());
            }
            Info($"rest bend (root's parent frame): left arm {F(syn.B.Limb(IkGroup.LeftArm).RestBend)}, left leg {F(syn.B.Limb(IkGroup.LeftLeg).RestBend)}");
        }
    }

    // ================================================================ S6 end rotation

    static void S6()
    {
        Section("S6", "end rotation (hands / feet = target world rotation; fingers / toes keep their local rotation)");
        using (var syn = new Syn(new Vector3(1, 0, -2), new Vector3(0, 35, 0), 1.2f))
        {
            var finger = syn.Map["cf_J_Hand_Index01_L"]; var toe = syn.Map["cf_J_Toes01_R"];
            var fq = finger.localRotation; var tq = toe.localRotation;
            var t = Current(syn.B);
            t[(int)IkTargetId.LeftHand].Rot = new Vector3(30, 45, 10);
            t[(int)IkTargetId.LeftHand].Pos += new Vector3(0.5f, 1.0f, 1.0f);
            t[(int)IkTargetId.RightFoot].Rot = new Vector3(-20, 0, 10);
            t[(int)IkTargetId.RightFoot].Pos += new Vector3(0, 1.5f, 1.0f);
            var info = new IkSolveInfo();
            Solve(syn.B, syn.Root, t, Groups(IkGroup.LeftArm, IkGroup.RightLeg), info);
            float ah = Angle(syn.B.Limb(IkGroup.LeftArm).End.rotation, syn.Root.rotation * Quaternion.Euler(30, 45, 10));
            float af = Angle(syn.B.Limb(IkGroup.RightLeg).End.rotation, syn.Root.rotation * Quaternion.Euler(-20, 0, 10));
            Check("hand world rotation = root.rotation · Euler(rot) ≤ 0.01°", ah <= 0.01f, G(ah) + "°");
            Check("foot world rotation = root.rotation · Euler(rot) ≤ 0.01°", af <= 0.01f, G(af) + "°");
            Check("finger / toe local rotations unchanged bit for bit", Bits(finger.localRotation, fq) && Bits(toe.localRotation, tq));
            Check("hand / foot reached (root rotated 35°, scale 1.2) ≤ 1e-4", info.Residual[(int)IkTargetId.LeftHand] <= 1e-4f && info.Residual[(int)IkTargetId.RightFoot] <= 1e-4f,
                $"hand {G(info.Residual[(int)IkTargetId.LeftHand])}, foot {G(info.Residual[(int)IkTargetId.RightFoot])}");
        }
    }

    // ================================================================ S7 roll

    static float Twist(Quaternion before, Quaternion after, Vector3 axisLocal)
    {
        var d = Quaternion.Inverse(before) * after;
        var ax = axisLocal.normalized;
        float proj = d.x * ax.x + d.y * ax.y + d.z * ax.z;
        return Mathf.Abs((float)(2 * Math.Atan2(proj, d.w) * 180 / Math.PI));
    }

    static void S7()
    {
        Section("S7", "roll (IkLimbOrientation.SwingOnly: swing-only rotations keep the clip's twist)");
        IkOptions.LimbOrientation = IkLimbOrientation.SwingOnly;
        using (var syn = new Syn())
        {
            foreach (var g in new[] { IkGroup.LeftArm, IkGroup.RightLeg })
            {
                var l = syn.B.Limb(g);
                float maxUp = 0, maxLow = 0;
                for (int i = 0; i < 50; i++)
                {
                    syn.Rest.Restore();
                    Vector3 a = l.Root.position, m0 = l.Mid.position, e0 = l.End.position;
                    float sum = (m0 - a).magnitude + (e0 - m0).magnitude;
                    var n = Vector3.Cross(m0 - a, e0 - a).normalized;           // bend-plane normal
                    var dir = Quaternion.AngleAxis(-40 + 80 * i / 49f, n) * (e0 - a).normalized;
                    var goal = a + (0.55f + 0.4f * (i % 7) / 6f) * sum * dir;   // in the bend plane
                    var qu = l.Root.localRotation; var ql = l.Mid.localRotation;
                    IkSolver.SolveLimb(l, goal, m0, out _);
                    maxUp = Mathf.Max(maxUp, Twist(qu, l.Root.localRotation, l.Mid.localPosition));
                    maxLow = Mathf.Max(maxLow, Twist(ql, l.Mid.localRotation, l.End.parent == l.Mid ? l.End.localPosition : l.Mid.InverseTransformPoint(l.End.position)));
                }
                Check($"{g}: upper bone twist about its own axis unchanged ≤ 0.01°", maxUp <= 0.01f, G(maxUp) + "°");
                Check($"{g}: lower bone twist unchanged ≤ 0.01°", maxLow <= 0.01f, G(maxLow) + "°");
            }
        }
    }

    // ================================================================ S17 bend-plane orientation (fix round)

    static void S17()
    {
        Section("S17", "bend-plane limb orientation (default): the hinge lies in the root / goal / pole plane on the pole side; weak poles keep the input roll");
        using (var syn = new Syn())
        {
            var r = new System.Random(17);
            foreach (var g in new[] { IkGroup.LeftArm, IkGroup.RightLeg })
            {
                var l = syn.B.Limb(g);
                float worstPlane = 0, worstRes = 0; int wrongSide = 0, n = 0;
                for (int i = 0; i < 500; i++)
                {
                    syn.Rest.Restore();
                    // an input roll of the upper bone about its own axis (as a clip would have)
                    l.Root.localRotation = l.Root.localRotation * Quaternion.AngleAxis((float)(r.NextDouble() * 360), l.UpAxis);
                    Vector3 a = l.Root.position;
                    float sum = (l.Mid.position - a).magnitude + (l.End.position - l.Mid.position).magnitude;
                    var u = RandUnit(r);
                    var goal = a + Mathf.Lerp(0.3f, 0.95f, (float)r.NextDouble()) * sum * u;
                    var pole = a + 0.5f * sum * u + Vector3.Cross(u, RandUnit(r)).normalized * (0.1f + 0.5f * (float)r.NextDouble()) * sum;
                    IkOptions.LimbOrientation = IkLimbOrientation.BendPlane;
                    IkSolver.SolveLimb(l, goal, pole, out _);
                    var hinge = l.Root.rotation * l.UpBend;
                    var nrm = Vector3.Cross(l.Mid.position - a, goal - a).normalized;
                    worstPlane = Mathf.Max(worstPlane, Mathf.Abs(90f - AngleV(hinge, nrm)));
                    if (Vector3.Dot(hinge, Reject(pole - a, u)) <= 0) wrongSide++;
                    worstRes = Mathf.Max(worstRes, (l.End.position - goal).magnitude);
                    n++;
                }
                Check($"{g}: hinge in the bend plane ≤ 0.01° (500 random input rolls, goals, poles)", worstPlane <= 0.01f, G(worstPlane) + "°");
                Check($"{g}: hinge on the pole side", wrongSide == 0, wrongSide.ToString());
                Check($"{g}: end residual unchanged ≤ 1e-4", worstRes <= 1e-4f, G(worstRes));
                // a pole closer to the line than PoleFadeMin x (L1 + L2): identical to swing-only (the input's roll stays)
                int diff = 0;
                for (int i = 0; i < 50; i++)
                {
                    syn.Rest.Restore();
                    l.Root.localRotation = l.Root.localRotation * Quaternion.AngleAxis((float)(r.NextDouble() * 360), l.UpAxis);
                    var snap = new Snap(syn.Root);
                    Vector3 a = l.Root.position;
                    float sum = (l.Mid.position - a).magnitude + (l.End.position - l.Mid.position).magnitude;
                    var u = RandUnit(r);
                    var goal = a + 0.8f * sum * u;
                    var pole = a + 0.5f * sum * u + Vector3.Cross(u, RandUnit(r)).normalized * 0.5f * IkSolver.PoleFadeMin * sum;
                    IkOptions.LimbOrientation = IkLimbOrientation.SwingOnly;
                    IkSolver.SolveLimb(l, goal, pole, out _);
                    var q1 = l.Root.rotation; var q2 = l.Mid.rotation;
                    snap.Restore();
                    IkOptions.LimbOrientation = IkLimbOrientation.BendPlane;
                    IkSolver.SolveLimb(l, goal, pole, out _);
                    if (!Bits(q1, l.Root.rotation) || !Bits(q2, l.Mid.rotation)) diff++;
                }
                Check($"{g}: weak pole (< PoleFadeMin) = swing-only bit for bit", diff == 0, $"{diff} of 50");
            }
            Info($"rest hinge (upper bone frame): left arm {F(syn.B.Limb(IkGroup.LeftArm).UpBend)}, right leg {F(syn.B.Limb(IkGroup.RightLeg).UpBend)}");
        }
    }

    // ================================================================ S18 clavicle guard (fix round 2)

    static void S18()
    {
        Section("S18", "clavicle aim guard (fades in between 0.5 and 0.7 x the clavicle length; optional cap)");
        using (var syn = new Syn())
        {
            var l = syn.B.Limb(IkGroup.LeftArm);
            float Turn(float ratio, float deg, out bool unchanged)
            {
                syn.Rest.Restore();
                var c = l.Clavicle.position; var va = l.Root.position - c;
                var axis = Vector3.Cross(va, Vector3.forward).normalized;
                var tw = c + Quaternion.AngleAxis(deg, axis) * va * ratio;
                var t = Current(syn.B);
                t[(int)IkTargetId.LeftShoulder].Pos = syn.Root.InverseTransformPoint(tw);
                var q0 = l.Clavicle.localRotation;
                Solve(syn.B, syn.Root, t, Groups(IkGroup.LeftArm), new IkSolveInfo());
                unchanged = Bits(q0, l.Clavicle.localRotation);
                return Angle(q0, l.Clavicle.localRotation);
            }
            float a1 = Turn(1f, 30f, out _);
            Check("target at the clavicle length, 30°: full aim (30° ± 0.01)", Mathf.Abs(a1 - 30f) <= 0.01f, G(a1) + "°");
            Turn(0.1f, 90f, out bool u2);
            Check("target at 0.1 x the clavicle length: clavicle unchanged bit for bit", u2);
            float a3 = Turn(0.6f, 30f, out _);
            Check("target at 0.6 x the length, 30°: half weight (15° ± 0.1)", Mathf.Abs(a3 - 15f) <= 0.1f, G(a3) + "°");
            float a5 = Turn(1f, 80f, out _);
            Check("80° away, no cap by default: full aim (80° ± 0.01)", Mathf.Abs(a5 - 80f) <= 0.01f, G(a5) + "°");
            float cap = IkSolver.ClavicleMaxAngle;
            IkSolver.ClavicleMaxAngle = 45f;
            try
            {
                float a4 = Turn(1f, 80f, out _);
                Check("with ClavicleMaxAngle 45: capped at 45° (± 0.01)", Mathf.Abs(a4 - 45f) <= 0.01f, G(a4) + "°");
            }
            finally { IkSolver.ClavicleMaxAngle = cap; }
        }
    }

    // ================================================================ S12 latch (synthetic, CharaMotion)

    static void S12()
    {
        Section("S12", "latch (a chain no stage rewrites: 60 steps, toggling IK, release)");
        using (var syn = new Syn())
        {
            var motion = syn.Go.AddComponent<CharaMotion>();
            motion.AutoStep = false;
            var st = new IkStage(syn.Root, syn.B, motion);
            motion.Add(st);
            var t = Current(syn.B);
            t[(int)IkTargetId.Body].Pos += new Vector3(0.2f, -0.6f, 0.3f);
            t[(int)IkTargetId.LeftHand].Pos += new Vector3(0.5f, 1.5f, 1.2f);
            t[(int)IkTargetId.RightHand].Pos += new Vector3(-0.4f, 2.0f, 0.8f);
            t[(int)IkTargetId.LeftShoulder].Pos += new Vector3(0, 0.3f, 0.1f);
            t[(int)IkTargetId.RightFoot].Pos += new Vector3(0, 1.5f, 1.5f);
            t[(int)IkTargetId.LeftFoot].Pos += new Vector3(0.2f, 0.5f, -0.5f);
            t[(int)IkTargetId.RightThigh].Pos += new Vector3(0, 0.2f, 0.1f);
            for (int i = 0; i < t.Length; i++) st.SetTarget((IkTargetId)i, t[i].Pos, t[i].Rot);
            foreach (var g in AllGroups) st.Applied[(int)g] = true;
            st.Refresh();
            var input = new Snap(syn.Root);
            motion.Step(Dt);
            var first = new Snap(syn.Root);
            Check("step 1 solved all groups (owned 17 bones)", st.LastSolve.Valid && st.Owned.Count == 17, $"owned {st.Owned.Count}");
            int bad = 0; List<string> firstBad = null;
            for (int i = 0; i < 59; i++)
            {
                motion.Step(Dt);
                var d = first.Diff();
                if (d.Count > 0) { bad++; if (firstBad == null) firstBad = d; }
            }
            Check("60 steps bit-identical to step 1", bad == 0, bad + " steps; " + Few(firstBad ?? new List<string>()));
            foreach (var g in AllGroups) st.Applied[(int)g] = false;
            st.Refresh();
            var rel = input.Diff();
            Check("IK off (Refresh): inputs written back, every transform bit-equal to the pre-IK pose", rel.Count == 0 && !st.Enabled && st.Owned.Count == 0, Few(rel));
            foreach (var g in AllGroups) st.Applied[(int)g] = true;
            st.Refresh();
            motion.Step(Dt);
            var again = first.Diff();
            Check("IK on again: the solve equals step 1 bit for bit", again.Count == 0, Few(again));
            st.SetSuspended(true);
            var sus = input.Diff();
            Check("suspend: inputs written back (pre-IK pose)", sus.Count == 0, Few(sus));
            motion.Step(Dt);
            Check("suspended step writes nothing", input.Diff().Count == 0);
            st.SetSuspended(false);
            motion.Step(Dt);
            Check("resume: the solve equals step 1 bit for bit", first.Diff().Count == 0, Few(first.Diff()));
            // one group off while the others stay: its bones go back to the input at the next step
            st.Applied[(int)IkGroup.LeftArm] = false;
            st.Refresh();
            motion.Step(Dt);
            var arm = new List<string>();
            foreach (var s in IkBones.SlotsOf(IkGroup.LeftArm))
            {
                var bone = syn.B.Slots[s];
                if (!Bits(bone.localRotation, InputRot(input, syn.Root, bone))) arm.Add(bone.name);
            }
            Check("a group switched off: its bones show the input again at the next step (latch)", arm.Count == 0, Few(arm));
            Object.DestroyImmediate(motion);
        }
    }

    static Quaternion InputRot(Snap s, Transform root, Transform bone)
    {
        // rebuild from a fresh snapshot of the input: restore, read, re-apply the current pose
        var cur = new Snap(root);
        s.Restore();
        var q = bone.localRotation;
        cur.Restore();
        return q;
    }

    // ================================================================ S15 rotation limits

    static void S15()
    {
        Section("S15", "rotation limits (interior elbow / knee angle ≥ limitMinInterior; off = S1)");
        using (var syn = new Syn())
        {
            IkOptions.RotationLimits = true; IkOptions.LimitMinInterior = 20f;
            var r = new System.Random(15);
            foreach (var g in new[] { IkGroup.LeftArm, IkGroup.LeftLeg })
            {
                var l = syn.B.Limb(g);
                float minInt = 180; int notClamped = 0;
                for (int i = 0; i < 200; i++)
                {
                    syn.Rest.Restore();
                    Vector3 a = l.Root.position;
                    float u1 = (l.Mid.position - a).magnitude, u2 = (l.End.position - l.Mid.position).magnitude, sum = u1 + u2;
                    float dLim = Mathf.Sqrt(u1 * u1 + u2 * u2 - 2 * u1 * u2 * Mathf.Cos(20f * Mathf.Deg2Rad));
                    var goal = a + RandUnit(r) * (float)(r.NextDouble() * 0.95 * dLim);
                    IkSolver.SolveLimb(l, goal, a + RandUnit(r) * sum, out bool c);
                    if (!c) notClamped++;
                    minInt = Mathf.Min(minInt, AngleV(l.Root.position - l.Mid.position, l.End.position - l.Mid.position));
                }
                Check($"{g}: goals near the root → interior angle ≥ 20° (−0.01°), clamped", minInt >= 20f - 0.01f && notClamped == 0, $"min {G(minInt)}°, not clamped {notClamped}");
            }
            // goals whose interior angle is above the limit: identical to the option off
            IkOptions.RotationLimits = false;
            var off = new List<Vector3>();
            var l2 = syn.B.Limb(IkGroup.LeftArm);
            var goals = new List<(Vector3 g, Vector3 p)>();
            LimbRandom(l2, syn.Rest, 1234 + (int)IkGroup.LeftArm, 1000, (i, a, goal, pole, L1, L2) =>
            {
                IkSolver.SolveLimb(l2, goal, pole, out _);
                off.Add(l2.End.position);
                goals.Add((goal, pole));
            });
            bool sameAsS1 = s1Ends.Count < 1000 || off.Select((v, i) => Bits(v, s1Ends[i])).All(x => x);
            Check("option off: identical to S1 (bit for bit, same seed)", sameAsS1, s1Ends.Count < 1000 ? "S1 not run in this pass (deterministic rerun only)" : "");
            IkOptions.RotationLimits = true;
            int diff = 0, above = 0;
            for (int i = 0; i < goals.Count; i++)
            {
                syn.Rest.Restore();
                IkSolver.SolveLimb(l2, goals[i].g, goals[i].p, out _);
                float ia = AngleV(l2.Root.position - l2.Mid.position, l2.End.position - l2.Mid.position);
                if (ia < 21f) continue;
                above++;
                if (!Bits(l2.End.position, off[i])) diff++;
            }
            Check("option on, interior angle above the limit: identical to off", diff == 0, $"{diff} of {above}");
        }
    }

    // ================================================================ BR-Chan fixture

    sealed class Env : IDisposable
    {
        public readonly SceneSession S;
        public readonly SceneRuntime Rt;

        public Env(ulong seed = 0x1c1c1c1cUL)
        {
            S = SceneRuntimeTest.NewSession(services, seed);
            Rt = new SceneRuntime(S, services, null, false);
        }

        public string Add(string card, bool idle = true)
        {
            var r = Rt.Ops.AddCharaFromCard(card);
            if (r == null || !r.Ok) throw new Exception("add " + Path.GetFileName(card) + ": " + r);
            if (idle) S.SetAnimation(r.NewId, Idle);
            Rt.ApplyNow();
            return r.NewId;
        }

        public CharaRealization C(string id) => Rt.FindChara(id);
        public void Set(string id, string path, object v)
        {
            var r = S.Set(id, path, v);
            if (r == null || !r.Ok) throw new Exception($"set {path}: {r}");
        }
        public void Step(int n = 1) { for (int i = 0; i < n; i++) Rt.Tick(Dt); }

        /// <summary>Installs an IK stage on a built character (the scene driver is off during the test).</summary>
        public IkStage Ik(string id)
        {
            var c = C(id);
            var b = IkBones.Discover(c.Context.BodyBones, c.Root.transform);
            var st = new IkStage(c.Root.transform, b, c.Motion);
            c.Motion.Add(st);
            return st;
        }

        public void Dispose() => Rt.Dispose();
    }

    static void SetAll(IkStage st, IkTargetValue[] t, params IkGroup[] groups)
    {
        for (int i = 0; i < t.Length; i++) st.SetTarget((IkTargetId)i, t[i].Pos, t[i].Rot);
        Array.Clear(st.Applied, 0, st.Applied.Length);
        foreach (var g in groups) st.Applied[(int)g] = true;
        st.Refresh();
    }

    /// <summary>Generic reachable offsets from the current pose (root space).</summary>
    static IkTargetValue[] Offsets(IkBones b)
    {
        var t = Current(b);
        t[(int)IkTargetId.Body].Pos += new Vector3(0.15f, -0.4f, 0.2f);
        t[(int)IkTargetId.LeftHand].Pos += new Vector3(0.4f, 1.2f, 1.0f);
        t[(int)IkTargetId.RightHand].Pos += new Vector3(-0.6f, 1.5f, 0.8f);
        t[(int)IkTargetId.RightHand].Rot = new Vector3(20, 40, 0);
        t[(int)IkTargetId.LeftShoulder].Pos += new Vector3(0, 0.2f, 0.1f);
        t[(int)IkTargetId.RightShoulder].Pos += new Vector3(0, 0.1f, -0.1f);
        t[(int)IkTargetId.LeftArmChain].Pos += new Vector3(0, -0.5f, -1.0f);
        t[(int)IkTargetId.RightFoot].Pos += new Vector3(0, 1.2f, 1.2f);
        t[(int)IkTargetId.LeftFoot].Pos += new Vector3(0.2f, 0.4f, -0.4f);
        t[(int)IkTargetId.LeftFoot].Rot = new Vector3(-15, 5, 10);
        t[(int)IkTargetId.LeftLegChain].Pos += new Vector3(1.0f, 0, 1.0f);
        t[(int)IkTargetId.RightThigh].Pos += new Vector3(0, 0.15f, 0.1f);
        return t;
    }

    // ================================================================ S8 body

    static void S8()
    {
        Section("S8", "body (hips on the Body target; thigh frame turn with both legs; input rotation without)");
        using (var e = new Env())
        {
            var id = e.Add(femaleCard);
            e.Step(10);
            var c = e.C(id);
            var b = IkBones.Discover(c.Context.BodyBones, c.Root.transform);
            Check("IkBones on BR-Chan: every group available, nothing missing", b.GroupAvailable.All(x => x) && b.Missing.Count == 0, Few(b.Missing));
            var snap = new Snap(c.Root.transform);
            var root = c.Root.transform;
            var t = Current(b);
            t[(int)IkTargetId.Body].Pos += new Vector3(0.3f, -0.5f, 0.2f);
            // thigh targets: the current thighs turned about the hips by (10, 20, 5) and moved with the body
            var hp = Current(b)[(int)IkTargetId.Body].Pos;
            var rq = Quaternion.Euler(10, 20, 5);
            foreach (var th in new[] { IkTargetId.LeftThigh, IkTargetId.RightThigh })
                t[(int)th].Pos = t[(int)IkTargetId.Body].Pos + rq * (t[(int)th].Pos - hp);
            var info = new IkSolveInfo();
            Solve(b, root, t, Groups(IkGroup.Body, IkGroup.LeftLeg, IkGroup.RightLeg), info);
            var pBody = root.TransformPoint(t[(int)IkTargetId.Body].Pos);
            float dp = (b.Hips.position - pBody).magnitude;
            Check("hips world position = target (≤ 1e-5)", dp <= 1e-5f, $"{G(dp)}{(Bits(b.Hips.position, pBody) ? " (bit-equal)" : "")}");
            Vector3 tl = b.Limb(IkGroup.LeftLeg).Root.position, tr = b.Limb(IkGroup.RightLeg).Root.position;
            Vector3 pl = root.TransformPoint(t[(int)IkTargetId.LeftThigh].Pos), pr = root.TransformPoint(t[(int)IkTargetId.RightThigh].Pos);
            IkSolver.Frame(tr - tl, b.Hips.position - (tl + tr) * 0.5f, out var fa);
            IkSolver.Frame(pr - pl, pBody - (pl + pr) * 0.5f, out var ft);
            float fang = Angle(fa, ft);
            Check("thigh line + hips-above-thighs frame = the targets' frame ≤ 0.01°", fang <= 0.01f && !info.BodyDegenerate, G(fang) + "°");
            snap.Restore();
            var qIn = b.Hips.localRotation;
            Solve(b, root, t, Groups(IkGroup.Body, IkGroup.LeftLeg), info);
            Check("Body + one leg: hips rotation = the input bit for bit", Bits(b.Hips.localRotation, qIn));
            snap.Restore();
            Solve(b, root, t, Groups(IkGroup.Body), info);
            Check("Body alone: hips rotation = the input bit for bit, position on the target", Bits(b.Hips.localRotation, qIn) && (b.Hips.position - pBody).magnitude <= 1e-5f);
            snap.Restore();
        }
    }

    // ================================================================ S9 torso

    static void S9()
    {
        Section("S9", "torso (chest frame onto the shoulder targets over Spine01 / Spine02, three fixed passes)");
        using (var e = new Env())
        {
            var id = e.Add(femaleCard);
            e.Step(10);
            var c = e.C(id);
            var root = c.Root.transform;
            var b = IkBones.Discover(c.Context.BodyBones, root);
            var snap = new Snap(root);
            var r = new System.Random(9);
            float worst = 0, worstAt = 0; int spine3 = 0, degenerate = 0;
            var res = new List<float>();
            for (int i = 0; i < 40; i++)
            {
                snap.Restore();
                var t = Current(b);
                Vector3 s0 = root.InverseTransformPoint(b.Spine01.position);
                float ang = 5f + 40f * i / 39f;
                var rq = Quaternion.AngleAxis(ang, RandUnit(r));
                foreach (var sh in new[] { IkTargetId.LeftShoulder, IkTargetId.RightShoulder })
                    t[(int)sh].Pos = s0 + rq * (t[(int)sh].Pos - s0);
                var q3 = b.Spine03.localRotation;
                var info = new IkSolveInfo();
                Solve(b, root, t, Groups(IkGroup.Body, IkGroup.LeftArm, IkGroup.RightArm), info);
                if (!Bits(b.Spine03.localRotation, q3)) spine3++;
                if (info.TorsoDegenerate || !info.TorsoRan) degenerate++;
                res.Add(info.TorsoResidual);
                if (info.TorsoResidual > worst) { worst = info.TorsoResidual; worstAt = ang; }
            }
            snap.Restore();
            Info("torso residual over 40 target frames 5°..45° from the clip: " + Dist(res));
            foreach (int passes in new[] { 1, 2, 4 })
            {
                IkSolver.TorsoPasses = passes;
                var rp = new List<float>(); var r2 = new System.Random(9);
                try
                {
                    for (int i = 0; i < 40; i++)
                    {
                        snap.Restore();
                        var t = Current(b);
                        Vector3 s0 = root.InverseTransformPoint(b.Spine01.position);
                        var rq = Quaternion.AngleAxis(5f + 40f * i / 39f, RandUnit(r2));
                        foreach (var sh in new[] { IkTargetId.LeftShoulder, IkTargetId.RightShoulder }) t[(int)sh].Pos = s0 + rq * (t[(int)sh].Pos - s0);
                        var info = new IkSolveInfo();
                        Solve(b, root, t, Groups(IkGroup.Body, IkGroup.LeftArm, IkGroup.RightArm), info);
                        rp.Add(info.TorsoResidual);
                    }
                }
                finally { IkSolver.TorsoPasses = 3; }
                Info($"   (measurement) {passes} pass{(passes > 1 ? "es" : "")}: " + Dist(rp) + "; ≤ 25°: " + Dist(rp.Take(20).ToList()));
            }
            snap.Restore();
            var upTo25 = res.Take(20).ToList();
            Info("three passes, frames ≤ 25°: " + Dist(upTo25));
            Check("chest frame residual after the passes ≤ 0.5° (targets up to 45° from the clip)", worst <= 0.5f && degenerate == 0, $"max {G(worst)}° at {G(worstAt)}°; degenerate {degenerate}");
            Check("Spine03 local rotation unchanged (bit for bit)", spine3 == 0, spine3.ToString());
        }
    }

    // ================================================================ S10 clavicle

    static void S10()
    {
        Section("S10", "clavicle (the upper-arm root direction from the clavicle points at the shoulder target)");
        using (var e = new Env())
        {
            var id = e.Add(femaleCard);
            e.Step(10);
            var c = e.C(id);
            var root = c.Root.transform;
            var b = IkBones.Discover(c.Context.BodyBones, root);
            var snap = new Snap(root);
            foreach (var withBody in new[] { false, true })
            {
                snap.Restore();
                var t = Offsets(b);
                t[(int)IkTargetId.LeftShoulder].Pos += new Vector3(0.2f, 0.3f, -0.1f);
                t[(int)IkTargetId.RightShoulder].Pos += new Vector3(-0.1f, 0.25f, 0.2f);
                var info = new IkSolveInfo();
                float cap = IkSolver.ClavicleMaxAngle;
                IkSolver.ClavicleMaxAngle = 180f;   // the aim itself (the cap is S18's)
                try { Solve(b, root, t, withBody ? Groups(IkGroup.Body, IkGroup.LeftArm, IkGroup.RightArm) : Groups(IkGroup.LeftArm, IkGroup.RightArm), info); }
                finally { IkSolver.ClavicleMaxAngle = cap; }
                float worst = 0;
                foreach (var g in new[] { IkGroup.LeftArm, IkGroup.RightArm })
                {
                    var l = b.Limb(g);
                    var tw = root.TransformPoint(t[(int)IkSolver.RootTargetOf(g)].Pos);
                    worst = Mathf.Max(worst, AngleV(l.Root.position - l.Clavicle.position, tw - l.Clavicle.position));
                }
                Check($"{(withBody ? "with Body (after the torso)" : "arms only")}: aim ≤ 0.01°", worst <= 0.01f, G(worst) + "°; shoulder distance residual L " +
                    G(info.Residual[(int)IkTargetId.LeftShoulder]) + ", R " + G(info.Residual[(int)IkTargetId.RightShoulder]));
            }
            snap.Restore();
        }
    }

    // ================================================================ S11 groups off

    static void S11()
    {
        Section("S11", "groups off write nothing (32 group combinations)");
        using (var e = new Env())
        {
            var id = e.Add(femaleCard);
            e.Step(10);
            var c = e.C(id);
            var root = c.Root.transform;
            var b = IkBones.Discover(c.Context.BodyBones, root);
            var snap = new Snap(root);
            var t = Offsets(b);
            var slotSet = new HashSet<Transform>(b.Slots);
            int badOff = 0, badOther = 0, badNone = 0; var first = new List<string>();
            for (int mask = 0; mask < 32; mask++)
            {
                snap.Restore();
                var run = new bool[IkGroupInfo.Count];
                for (int g = 0; g < 5; g++) run[g] = (mask & (1 << g)) != 0;
                var owned = new bool[IkBones.SlotCount];
                IkSolver.Solve(b, root, t, run, new IkSolveInfo(), owned);
                var changed = new HashSet<string>(snap.Diff());
                if (mask == 0) { if (changed.Count > 0) { badNone++; first.Add("none: " + Few(changed.ToList())); } continue; }
                bool torso = run[0] && run[3] && run[4];
                for (int g = 0; g < 5; g++)
                {
                    if (run[g]) continue;
                    foreach (var s in IkBones.SlotsOf((IkGroup)g))
                    {
                        if (g == 0 && s != IkBones.SlotHips && torso) continue;
                        if (changed.Contains(b.Slots[s].name)) { badOff++; if (first.Count < 5) first.Add($"mask {mask}: {b.Slots[s].name}"); }
                    }
                }
                if (!torso)
                    foreach (var s in new[] { IkBones.SlotSpine01, IkBones.SlotSpine02 })
                        if (changed.Contains(b.Slots[s].name)) { badOff++; if (first.Count < 5) first.Add($"mask {mask}: {b.Slots[s].name} (no torso)"); }
                var others = changed.Where(n => !slotSet.Any(x => x && x.name == n)).ToList();
                if (others.Count > 0) { badOther++; if (first.Count < 5) first.Add($"mask {mask}: non-solved {Few(others)}"); }
                for (int s = 0; s < IkBones.SlotCount; s++)
                    if (!owned[s] && changed.Contains(b.Slots[s].name)) { badOff++; if (first.Count < 5) first.Add($"mask {mask}: {b.Slots[s].name} written but not owned"); }
            }
            snap.Restore();
            Check("no group: every transform bit-equal to the input", badNone == 0);
            Check("bones of the groups that are off: bit-equal to the input (31 combinations)", badOff == 0, badOff + "; " + string.Join("; ", first));
            Check("no transform outside the solved set is written", badOther == 0, badOther.ToString());
        }
    }

    // ================================================================ S13 FK base + owned

    static void S13()
    {
        Section("S13", "FK base + owned mask (IK over FK)");
        using (var e = new Env())
        {
            var id = e.Add(femaleCard);
            e.Step(5);
            var c = e.C(id);
            var fk = c.Motion.Get<FkStage>();
            if (fk == null) { Check("FkStage installed by the FK driver", false); return; }
            var spine = new SortedDictionary<string, Vec3>(StringComparer.Ordinal)
            {
                ["cf_J_Spine01"] = new Vec3(12, 4, 6), ["cf_J_Spine02"] = new Vec3(15, -6, 3), ["cf_J_Spine03"] = new Vec3(8, 2, -4),
                ["cf_J_Hips"] = new Vec3(-5, 10, 2), ["cf_J_Neck"] = new Vec3(10, 20, 5),
            };
            e.Set(id, "chara.fk.bones", spine);
            foreach (var g in FkGroupInfo.MemberNames) e.Set(id, "chara.fk.groups." + g, g == "body");
            e.Set(id, "chara.fk.enabled", true);
            e.Step(2);
            var st = e.Ik(id);
            var t = Current(st.Bones);   // feet etc. from the FK pose
            t[(int)IkTargetId.LeftFoot].Pos += new Vector3(0.3f, 1.0f, 0.8f);
            t[(int)IkTargetId.RightFoot].Pos += new Vector3(-0.2f, 1.5f, -0.6f);
            t[(int)IkTargetId.RightFoot].Rot = new Vector3(10, -20, 5);
            SetAll(st, t, IkGroup.LeftLeg, IkGroup.RightLeg);
            e.Step(3);
            float rl = st.LastSolve.Residual[(int)IkTargetId.LeftFoot], rr = st.LastSolve.Residual[(int)IkTargetId.RightFoot];
            Check("FK Body + IK legs: feet at their targets ≤ 1e-4", rl <= 1e-4f && rr <= 1e-4f, $"L {G(rl)}, R {G(rr)}");
            var bad = new List<string>();
            foreach (var k in new[] { "cf_J_Spine01", "cf_J_Spine02", "cf_J_Spine03", "cf_J_Hips" })
            {
                var bone = fk.Table.Find(k);
                if (bone == null || !bone.Bone) { bad.Add(k + " not an FK bone"); continue; }
                if (!Bits(bone.Bone.localRotation, Stored(Quaternion.Euler(U(spine[k]))))) bad.Add($"{k} {G(Angle(bone.Bone.localRotation, Stored(Quaternion.Euler(U(spine[k])))))}°");
            }
            Check("trunk = FK (Spine01–03, hips rotation bit for bit)", bad.Count == 0, Few(bad));
            var ownedFk = fk.Owned.Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
            Check("FkStage.Owned = the FK bones of the owned set (legs: 6)", ownedFk.Count == 6 && ownedFk.All(k => k.StartsWith("cf_J_Leg") || k.StartsWith("cf_J_Foot")), string.Join(",", ownedFk));
            // FK keeps owned leg bones at their IK values at 400 (the per-step write skips them)
            var footL = st.Bones.Limb(IkGroup.LeftLeg).End;
            var tw = c.Root.transform.TransformPoint(t[(int)IkTargetId.LeftFoot].Pos);
            Check("after the whole step (FK at 400 ran) the left foot is still on its target ≤ 1e-4", (footL.position - tw).magnitude <= 1e-4f, G((footL.position - tw).magnitude));

            // FK Neck + IK all → neck = FK
            e.Set(id, "chara.fk.groups.body", false);
            e.Set(id, "chara.fk.groups.neck", true);
            e.Step(1);
            SetAll(st, Offsets(st.Bones), AllGroups);
            e.Step(3);
            var neck = fk.Table.Find("cf_J_Neck");
            bool neckOk = neck != null && neck.Bone && Bits(neck.Bone.localRotation, Stored(Quaternion.Euler(U(spine["cf_J_Neck"]))));
            Check("FK Neck + IK all groups: neck = FK bit for bit", neckOk, neck == null ? "no neck FK bone" : G(Angle(neck.Bone.localRotation, Stored(Quaternion.Euler(U(spine["cf_J_Neck"]))))) + "°");
            Check("FK Neck + IK: FkStage.Owned has no Neck-group bone", fk.Owned.All(x => x.Group != FkGroup.Neck), string.Join(",", fk.Owned.Select(x => x.Key)));
            // IK stops → FkStage.Owned cleared
            Array.Clear(st.Applied, 0, st.Applied.Length);
            st.Refresh();
            Check("IK off: FkStage.Owned cleared", fk.Owned.Count == 0, fk.Owned.Count.ToString());
        }
    }

    static Vector3 U(Vec3 v) => new Vector3(v.X, v.Y, v.Z);

    // ================================================================ S14 probe

    static Dictionary<string, Quaternion> BodyRot(CharaRealization c, Func<string, bool> pick)
    {
        var d = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
        foreach (var kv in c.Context.BodyBones) if (kv.Value && pick(kv.Key)) d[kv.Key] = kv.Value.localRotation;
        return d;
    }

    static List<string> DiffBody(CharaRealization a, CharaRealization b, bool positions = true)
    {
        var l = new List<string>();
        foreach (var kv in a.Context.BodyBones)
        {
            if (!kv.Value || !b.Context.BodyBones.TryGetValue(kv.Key, out var o) || !o) continue;
            if (!Bits(kv.Value.localRotation, o.localRotation) || (positions && !Bits(kv.Value.localPosition, o.localPosition))) l.Add(kv.Key);
        }
        return l;
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++) { var r = FindDeep(root.GetChild(i), name); if (r) return r; }
        return null;
    }

    static void S14()
    {
        Section("S14", "probe (CharaMotion.Probe before IkPrepare: side-effect free, = the clip + hand patterns)");
        using (var e = new Env())
        {
            var a = e.Add(femaleCard); var b = e.Add(femaleCard); var plain = e.Add(femaleCard);
            var neckVal = new SortedDictionary<string, Vec3>(StringComparer.Ordinal) { ["cf_J_Neck"] = new Vec3(10, 25, 0) };
            foreach (var id in new[] { a, b })
            {
                e.Set(id, "chara.fk.bones", neckVal);
                foreach (var g in FkGroupInfo.MemberNames) e.Set(id, "chara.fk.groups." + g, g == "neck");
                e.Set(id, "chara.fk.enabled", true);
            }
            e.Step(8);
            var sa = e.Ik(a); var sb = e.Ik(b);
            var ta = Offsets(sa.Bones);
            SetAll(sa, ta, AllGroups); SetAll(sb, ta, AllGroups);
            e.Step(6);
            var ca = e.C(a); var cb = e.C(b); var cp = e.C(plain);
            var pre = DiffBody(ca, cb);
            Check("precondition: the twins A and B are bit-equal on the body skeleton", pre.Count == 0, Few(pre));
            var snap = new Snap(ca.Root.transform);
            float time = ca.Motion.Time; int steps = ca.Motion.StepCount;
            Dictionary<string, Quaternion> read = null;
            var src = new IkTargetValue[IkGroupInfo.TargetCount];
            ca.Motion.Probe(MotionPhase.IkPrepare, ca.Root.transform, () =>
            {
                read = BodyRot(ca, n => true);
                sa.Bones.ReadTargets(src);
            });
            // Unity's rotation setter normalises and is not idempotent (a value read back from a transform and set again is
            // stored with other last bits in about a third of the cases), so a transform the probe's stages rewrote cannot
            // always be restored bit for bit: positions / scales are, rotations within float rounding; the next step's
            // stages rewrite exactly those transforms (checked below against the twin).
            var after = snap.Diff();
            float worstRot = 0; var posBad = new List<string>();
            foreach (var n in after)
            {
                var t = FindDeep(ca.Root.transform, n);
                var t2 = FindDeep(cb.Root.transform, n);
                if (!t || !t2) { posBad.Add(n + " (not found)"); continue; }
                if (!Bits(t.localPosition, t2.localPosition) || !Bits(t.localScale, t2.localScale)) posBad.Add(n);
                worstRot = Mathf.Max(worstRot, Angle(t.localRotation, t2.localRotation));
            }
            Info($"after the probe: {snap.Count - after.Count} of {snap.Count} transforms bit-identical; {after.Count} rewritten by the probe's stages restored through the setter ({Few(after)}), max {G(worstRot)}° from the twin");
            Check("every transform restored: bit-identical, or (rewritten by the probe's stages) position / scale bit-identical and rotation ≤ 1e-4°", posBad.Count == 0 && worstRot <= 1e-4f, Few(posBad));
            Check("Time and StepCount unchanged", ca.Motion.Time == time && ca.Motion.StepCount == steps, $"{ca.Motion.Time} / {ca.Motion.StepCount}");
            // the read pose = the character without IK / look / FK at the same time (bones no later stage writes)
            var bad = new List<string>(); int compared = 0;
            foreach (var kv in read)
            {
                string n = kv.Key;
                if (n.Contains("_dam") || n.EndsWith("_s") || n.Contains("_s_") || n.StartsWith("cf_J_Mune") || n.StartsWith("cf_J_Neck") || n.StartsWith("cf_J_Head") || n.StartsWith("cf_J_Eye")
                    || n.Contains("Siri") || n.Contains("sk_") || n.StartsWith("cf_J_hair") || n.StartsWith("cf_J_Kokan") || n.StartsWith("cf_J_Ana")) continue;
                if (!cp.Context.BodyBones.TryGetValue(n, out var t) || !t) continue;
                compared++;
                if (!Bits(kv.Value, t.localRotation)) bad.Add($"{n} {G(Angle(kv.Value, t.localRotation))}°");
            }
            Check($"read pose = the plain character at the same time (local rotations bit-equal on {compared} body bones)", bad.Count == 0 && compared > 50, Few(bad));
            var plainSrc = new IkTargetValue[IkGroupInfo.TargetCount];
            IkBones.Discover(cp.Context.BodyBones, cp.Root.transform).ReadTargets(plainSrc);
            float maxSrc = 0;
            for (int i = 0; i < src.Length; i++) maxSrc = Mathf.Max(maxSrc, (src[i].Pos - plainSrc[i].Pos).magnitude);
            Info($"13 source-bone positions (root space) probe vs plain: max {G(maxSrc)} (bone modifiers / later stages may move positions)");
            e.Step(1);
            var post = DiffBody(ca, cb);
            Check("the next step after a probe = the twin without probe (body skeleton bit-equal)", post.Count == 0, Few(post));
            // cost
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) ca.Motion.Probe(MotionPhase.IkPrepare, ca.Root.transform, () => sa.Bones.ReadTargets(src));
            double ms = sw.Elapsed.TotalMilliseconds / 20;
            sw.Restart();
            for (int i = 0; i < 20; i++) ca.Motion.Probe(MotionPhase.Animation, ca.Root.transform, () => sa.Bones.ReadTargets(src));
            double msRecord = sw.Elapsed.TotalMilliseconds / 20;
            Info($"U2 probe without stages (record + compare + read): {msRecord:F3} ms");
            Info($"U2 probe cost on BR-Chan: {ms:F3} ms per probe (mean of 20; {snap.Count} transforms recorded)");
            if (ms > 0.5) Warn("probe cost above the 0.5 ms target", $"{ms:F3} ms"); else Check("probe cost ≤ 0.5 ms", true, $"{ms:F3} ms");
            // reentrancy guard
            var snap2 = new Snap(ca.Root.transform);
            bool threw = false;
            try { ca.Motion.Probe(MotionPhase.IkPrepare, ca.Root.transform, () => ca.Motion.Probe(MotionPhase.IkPrepare, ca.Root.transform, () => { })); }
            catch (InvalidOperationException) { threw = true; }
            Check("a nested probe throws (InvalidOperationException) and the outer probe ends cleanly (Probing false)", threw && !ca.Motion.Probing, threw ? "restored differences " + snap2.Diff().Count : "no exception");
        }
    }

    // ================================================================ S16 cost

    static Dictionary<string, Transform> NameMap(Transform root)
    {
        var d = new Dictionary<string, Transform>(StringComparer.Ordinal);
        var stack = new Stack<Transform>(); stack.Push(root);
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            if (!d.ContainsKey(t.name)) d[t.name] = t;
            for (int i = t.childCount - 1; i >= 0; i--) stack.Push(t.GetChild(i));
        }
        return d;
    }

    static void S16()
    {
        Section("S16", "cost (10 characters, all groups)");
        using (var e = new Env())
        {
            var id = e.Add(femaleCard);
            e.Step(5);
            var c = e.C(id);
            var clones = new List<GameObject>();
            try
            {
                var stages = new List<IkStage>();
                var orig = c.Root.transform;
                var st0 = new IkStage(orig, IkBones.Discover(c.Context.BodyBones, orig), null);
                stages.Add(st0);
                for (int i = 0; i < 9; i++)
                {
                    var go = Object.Instantiate(c.Root);
                    go.hideFlags = HideFlags.DontSave;
                    go.transform.position = new Vector3(5 * (i + 1), 0, 0);
                    clones.Add(go);
                    stages.Add(new IkStage(go.transform, IkBones.Discover(NameMap(go.transform), go.transform), null));
                }
                foreach (var st in stages) SetAll(st, Offsets(st.Bones), AllGroups);
                var frame = new MotionFrame(Dt, Dt, 0, 0);
                for (int k = 0; k < 10; k++) foreach (var st in stages) st.Step(frame);
                double solve = 0, stage = 0; int n = 0;
                for (int k = 0; k < 200; k++)
                    foreach (var st in stages) { st.Step(frame); solve += st.LastSolve.Milliseconds; stage += st.LastSolve.StageMilliseconds; n++; }
                double mSolve = solve / n, mStage = stage / n;
                Info($"U1 solve mean per character {mSolve * 1000:F2} µs, whole stage step (latch + snapshot + solve + mask) {mStage * 1000:F2} µs ({n} steps over 10 characters)");
                if (mSolve <= 0.02) Check("solve mean ≤ 0.02 ms", true, $"{mSolve:F4} ms");
                else if (mSolve <= 0.04) Warn("solve mean above 0.02 ms (WARN up to 2×)", $"{mSolve:F4} ms");
                else Check("solve mean ≤ 0.04 ms (FAIL above 2× the target)", false, $"{mSolve:F4} ms");
                // the doubled FK write: WriteApplied on BR-Chan with Body, Neck and both hands applied
                var fk = c.Motion.Get<FkStage>();
                if (fk != null)
                {
                    var vals = new SortedDictionary<string, Vec3>(StringComparer.Ordinal);
                    foreach (var bn in fk.Table.Bones) if (bn.Group == FkGroup.Body || bn.Group == FkGroup.Neck || bn.Group == FkGroup.RightHand || bn.Group == FkGroup.LeftHand) vals[bn.Key] = new Vec3(3, 4, 5);
                    e.Set(id, "chara.fk.bones", vals);
                    foreach (var g in FkGroupInfo.MemberNames) e.Set(id, "chara.fk.groups." + g, g == "body" || g == "neck" || g == "rightHand" || g == "leftHand");
                    e.Set(id, "chara.fk.enabled", true);
                    e.Rt.ApplyNow();
                    for (int k = 0; k < 20; k++) fk.WriteApplied();
                    var sw = Stopwatch.StartNew();
                    for (int k = 0; k < 500; k++) fk.WriteApplied();
                    double wa = sw.Elapsed.TotalMilliseconds / 500;
                    int nb = fk.Table.Of(FkGroup.Body).Count + fk.Table.Of(FkGroup.Neck).Count + fk.Table.Of(FkGroup.RightHand).Count + fk.Table.Of(FkGroup.LeftHand).Count;
                    Info($"U1 FkStage.WriteApplied (Body + Neck + hands, {nb} bones): {wa * 1000:F2} µs per call (the IK base adds one call per step; target ≤ 0.01 ms)");
                    if (wa > 0.01) Warn("FK base write above 0.01 ms", $"{wa:F4} ms"); else Check("FK base write ≤ 0.01 ms", true, $"{wa:F4} ms");
                }
            }
            finally { foreach (var go in clones) if (go) Object.DestroyImmediate(go); }
        }
    }

    // ================================================================ U4 / U6 corpus (measurement)

    static readonly string[] ComparePoses =
    {
        "CHAIRS/Standing next to.dat", "STANDING/Floor/Peace.dat", "STANDING/Floor/Im cute.dat", "STANDING/Floor/Hands up 2.dat",
        "TINNY HOME 6/Lying on floor.dat", "CHAIRS/Sitting 4.dat", "SOFA/Sitting edge.dat", "TINNY HOME 6/Near TV.dat",
    };

    static void U4U6()
    {
        Section("U4", "U4 / U6 (measurement): IK-mode pose files solved on BR-Chan (all groups; targets as stored, x10 for 1/10 files)");
        string root = Path.Combine(CardResolveTest.GameRoot, "UserData", "studio", "pose");
        if (!Directory.Exists(root)) { Info("pose library not found"); return; }
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(p => p.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal).ToList();
        using (var e = new Env())
        {
            var id = e.Add(femaleCard, idle: false);
            e.Step(3);
            var c = e.C(id);
            var croot = c.Root.transform;
            var b = IkBones.Discover(c.Context.BodyBones, croot);
            var snap = new Snap(croot);
            float[] l1 = new float[5];
            foreach (var g in new[] { IkGroup.RightLeg, IkGroup.LeftLeg, IkGroup.RightArm, IkGroup.LeftArm })
            {
                var l = b.Limb(g);
                l1[(int)g] = croot.InverseTransformVector(l.Mid.position - l.Root.position).magnitude;
            }
            var resOne = new List<float>(); var resTen = new List<float>(); var torsoOne = new List<float>(); var torsoTen = new List<float>();
            int nIk = 0, nOne = 0, nTen = 0, nOther = 0, clampedOne = 0, clampedTen = 0, unreadable = 0;
            var pLines = new List<string>();
            var sw = Stopwatch.StartNew();
            foreach (var f in files)
            {
                byte[] data;
                try { data = File.ReadAllBytes(f); } catch { unreadable++; continue; }
                if (!PoseFile.TryRead(data, out var pose, out _)) { unreadable++; continue; }
                if (!pose.EnableIK) continue;
                nIk++;
                var t = new IkTargetValue[IkGroupInfo.TargetCount];
                bool all = true;
                for (int i = 0; i < 13; i++)
                {
                    var en = pose.FindIk(i);
                    if (en == null) { all = false; break; }
                    t[i] = new IkTargetValue(new Vector3(en.Pos.X, en.Pos.Y, en.Pos.Z), IkGroupInfo.UsesRotation((IkTargetId)i) ? new Vector3(en.Rot.X, en.Rot.Y, en.Rot.Z) : Vector3.zero);
                }
                if (!all) { nOther++; continue; }
                var ratios = new List<float>();
                foreach (var g in new[] { IkGroup.RightLeg, IkGroup.LeftLeg, IkGroup.RightArm, IkGroup.LeftArm })
                    ratios.Add((t[(int)IkSolver.BendTargetOf(g)].Pos - t[(int)IkSolver.RootTargetOf(g)].Pos).magnitude / l1[(int)g]);
                ratios.Sort();
                float rr = (ratios[1] + ratios[2]) / 2;
                float factor = rr >= 0.03f && rr <= 0.3f ? 10f : rr >= 0.5f && rr <= 1.6f ? 1f : 0f;
                if (factor == 0) { nOther++; continue; }
                if (factor == 10) { nTen++; for (int i = 0; i < 13; i++) t[i].Pos *= 10f; } else nOne++;
                snap.Restore();
                var info = new IkSolveInfo();
                var run = new bool[5];
                for (int g = 0; g < 5; g++) run[g] = pose.ActiveIK[g];
                Solve(b, croot, t, run, info);
                foreach (var end in new[] { IkTargetId.LeftHand, IkTargetId.RightHand, IkTargetId.LeftFoot, IkTargetId.RightFoot })
                {
                    if (!run[(int)IkGroupInfo.GroupOf(end)]) continue;
                    if (info.Clamped[(int)end]) { if (factor == 1) clampedOne++; else clampedTen++; continue; }
                    (factor == 1 ? resOne : resTen).Add(info.Residual[(int)end]);
                }
                if (info.TorsoRan) (factor == 1 ? torsoOne : torsoTen).Add(info.TorsoResidual);
                string rel = f.Substring(root.Length + 1).Replace('\\', '/');
                int pi = Array.IndexOf(ComparePoses, rel);
                if (pi >= 0) pLines.Add($"P{pi + 1} {rel}: torso residual {G(info.TorsoResidual)}°, end residuals " +
                    string.Join(" / ", new[] { IkTargetId.LeftHand, IkTargetId.RightHand, IkTargetId.LeftFoot, IkTargetId.RightFoot }.Select(x => G(info.Residual[(int)x]) + (info.Clamped[(int)x] ? "c" : ""))));
            }
            snap.Restore();
            Info($"pose files {files.Count} (unreadable {unreadable}); enableIK {nIk}: factor 1 {nOne}, factor 10 {nTen}, other / incomplete {nOther}; {sw.Elapsed.TotalSeconds:F1} s");
            Info("U4 reachable end residuals, factor 1: " + Dist(resOne) + $"; clamped ends {clampedOne}");
            Info("U4 reachable end residuals, factor 10: " + Dist(resTen) + $"; clamped ends {clampedTen}");
            Info("U6 torso residual (°), factor 1: " + Dist(torsoOne));
            Info("U6 torso residual (°), factor 10: " + Dist(torsoTen));
            foreach (var l in pLines.OrderBy(x => x, StringComparer.Ordinal)) Info(l);
            Check("U4: reachable end residuals ≤ 1e-4 (factor 1 files)", resOne.Count > 0 && resOne.Max() <= 1e-4f, Dist(resOne));
        }
    }

    // ================================================================ U8 FK compare poses as the IK base (measurement)

    static void U8()
    {
        Section("U8", "U8 (measurement): the FK compare poses' Body values as the base of IK legs (S13 on BR-Chan)");
        string json = Path.Combine(ContentSettings.ProjectRoot, "Tools", "compare_poses.json");
        string root = Path.Combine(CardResolveTest.GameRoot, "UserData", "studio", "pose");
        if (!File.Exists(json) || !Directory.Exists(root)) { Info("compare_poses.json or the pose library not found"); return; }
        var paths = Regex.Matches(File.ReadAllText(json), "\"path\"\\s*:\\s*\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        var rows = FkRows.From(services.Db);
        var byNo = new Dictionary<int, FkRow>();
        foreach (var r in rows) if (!byNo.ContainsKey(r.No)) byNo[r.No] = r;
        var resAll = new List<float>(); int trunkBad = 0, n = 0;
        using (var e = new Env())
        {
            var id = e.Add(femaleCard);
            e.Step(3);
            var c = e.C(id);
            var fk = c.Motion.Get<FkStage>();
            var st = e.Ik(id);
            foreach (var rel in paths)
            {
                var f = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(f) || !PoseFile.TryRead(File.ReadAllBytes(f), out var pose, out _)) { Info("missing / unreadable: " + rel); continue; }
                var vals = new SortedDictionary<string, Vec3>(StringComparer.Ordinal);
                foreach (var en in pose.FK)
                    if (byNo.TryGetValue(en.Key, out var row) && FkGroupInfo.OfColumn(row.GroupColumn) == (int)FkGroup.Body) vals[row.Name] = new Vec3(en.Rot.X, en.Rot.Y, en.Rot.Z);
                Array.Clear(st.Applied, 0, 5); st.Refresh();
                e.Set(id, "chara.fk.bones", vals);
                foreach (var g in FkGroupInfo.MemberNames) e.Set(id, "chara.fk.groups." + g, g == "body");
                e.Set(id, "chara.fk.enabled", true);
                e.Step(2);
                var t = Current(st.Bones);
                t[(int)IkTargetId.LeftFoot].Pos += new Vector3(0.3f, 0.8f, 0.6f);
                t[(int)IkTargetId.RightFoot].Pos += new Vector3(-0.3f, 0.6f, -0.5f);
                SetAll(st, t, IkGroup.LeftLeg, IkGroup.RightLeg);
                e.Step(2);
                n++;
                foreach (var end in new[] { IkTargetId.LeftFoot, IkTargetId.RightFoot })
                    if (!st.LastSolve.Clamped[(int)end]) resAll.Add(st.LastSolve.Residual[(int)end]);
                int bad = 0;
                foreach (var kv in vals)
                {
                    var bone = fk.Table.Find(kv.Key);
                    if (bone == null || !bone.Bone || fk.Owned.Contains(bone)) continue;
                    if (!Bits(bone.Bone.localRotation, Stored(Quaternion.Euler(U(kv.Value))))) bad++;
                }
                if (bad > 0) { trunkBad++; Info($"{rel}: {bad} non-owned Body FK bones differ from FK"); }
            }
            Array.Clear(st.Applied, 0, 5); st.Refresh();
        }
        Info($"U8 {n} compare poses: reachable foot residuals {Dist(resAll)}; poses with a non-owned Body FK bone off its FK value: {trunkBad}");
        Check("U8: feet on target ≤ 1e-4 and the rest of the Body group = FK for every compare pose", resAll.Count > 0 && resAll.Max() <= 1e-4f && trunkBad == 0, Dist(resAll));
    }

    // ================================================================ U9 clip bindings (measurement)

    static void U9()
    {
        Section("U9", "U9 (measurement): solved bones the vanilla non-H clips do not write (latch path)");
        // Clips loaded from the game's bundles carry no editor curve data, so the bindings are found by behaviour: after one
        // step of the clip the solved bones are set to a sentinel, and a bone that still holds it after the next step is not
        // written by the clip (nothing else writes these bones with FK / IK off). No image is rendered.
        var solved = new[] { "cf_J_Hips", "cf_J_Spine01", "cf_J_Spine02", "cf_J_Shoulder_L", "cf_J_Shoulder_R", "cf_J_ArmUp00_L", "cf_J_ArmLow01_L", "cf_J_Hand_L",
            "cf_J_ArmUp00_R", "cf_J_ArmLow01_R", "cf_J_Hand_R", "cf_J_LegUp00_L", "cf_J_LegLow01_L", "cf_J_Foot01_L", "cf_J_LegUp00_R", "cf_J_LegLow01_R", "cf_J_Foot01_R" };
        var db = services.Db;
        var rows = db.AllAnimeCategories.SelectMany(cg => db.AnimeIn(cg, true)).Where(r => r.IsVanilla && !r.IsH).Distinct()
            .OrderBy(r => r.Group).ThenBy(r => r.Category).ThenBy(r => r.No).ToList();
        int lim = int.TryParse(Arg("-u9limit"), out var l9) ? l9 : int.MaxValue;
        var unbound = new Dictionary<string, int>(StringComparer.Ordinal);
        int tested = 0, withUnbound = 0, hipsPosUnbound = 0;
        var examples = new List<string>();
        var sentinel = Quaternion.Euler(1.234f, 2.345f, 3.456f);
        var sentinelPos = new Vector3(0.1234f, 9.876f, 0.2345f);
        var sw = Stopwatch.StartNew();
        using (var e = new Env())
        {
            var id = e.Add(femaleCard, idle: false);
            var c = e.C(id);
            var bones = solved.Select(n => c.Context.BodyBones.TryGetValue(n, out var t) ? t : null).ToArray();
            foreach (var r in rows)
            {
                if (tested >= lim) break;
                e.S.SetAnimation(id, new AnimeRef(ContentIds.Vanilla, r.Group, r.Category, r.No));
                e.Step(2);
                var stored = new Quaternion[bones.Length];
                for (int i = 0; i < bones.Length; i++) if (bones[i]) { bones[i].localRotation = sentinel; stored[i] = bones[i].localRotation; }
                var hips = bones[0];
                hips.localPosition = sentinelPos; var storedPos = hips.localPosition;
                e.Step(1);
                tested++;
                var miss = new List<string>();
                for (int i = 0; i < bones.Length; i++) if (bones[i] && Bits(bones[i].localRotation, stored[i])) miss.Add(solved[i]);
                bool posMiss = Bits(hips.localPosition, storedPos);
                if (posMiss) hipsPosUnbound++;
                if (miss.Count > 0 || posMiss)
                {
                    withUnbound++;
                    foreach (var m in miss) { unbound.TryGetValue(m, out int v); unbound[m] = v + 1; }
                    if (examples.Count < 12) examples.Add($"{r.Group}/{r.Category}/{r.No}: {string.Join(",", miss)}{(posMiss ? " +hips position" : "")}");
                }
            }
        }
        Info($"vanilla non-H anime rows {rows.Count}, tested {tested} ({sw.Elapsed.TotalSeconds:F1} s)");
        Info($"rows leaving at least one solved bone unwritten: {withUnbound}; rows not writing the cf_J_Hips position: {hipsPosUnbound}");
        foreach (var kv in unbound.OrderByDescending(x => x.Value)) Info($"   {kv.Key}: {kv.Value} rows");
        foreach (var x in examples) Info("   e.g. " + x);
    }
}
